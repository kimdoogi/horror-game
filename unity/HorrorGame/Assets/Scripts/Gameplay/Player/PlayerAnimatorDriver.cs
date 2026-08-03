#nullable enable

using System;
using HorrorGame.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Plays <c>Player.fbx</c>'s nine clips from what the motor actually did.
    /// <para>
    /// A Playables graph rather than an <c>AnimatorController</c> asset. Two reasons, and
    /// only the second is about convenience. The first is that clip playback speed has to
    /// be tied to §06's speeds — a walk clip authored for 2.0 m/s played while moving at
    /// 1.3 (§08's overloaded band, or §05's 65% backpedal) skates, and skating feet are
    /// exactly the signal §12 asks the Listener to trust. Scaling the clip by
    /// <c>groundSpeed / referenceSpeed</c> keeps the stride on the ground at every one of
    /// §05's multipliers, and it is far more direct here than through a blend tree. The
    /// second is that a state machine drawn in an editor window is a second, unreviewable
    /// copy of the state table below.
    /// </para>
    /// <para>
    /// The graph also produces <see cref="Footfall"/>. §12 makes footstep sound the
    /// Listener's positioning channel, so a step must be heard when the foot lands, not on
    /// a timer that happens to run near the right rate. Reading the phase of the clip that
    /// is actually playing is the only way those two stay in agreement while the clip is
    /// being sped up and slowed down.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public sealed class PlayerAnimatorDriver : MonoBehaviour
    {
        private const int StateCount = 9;

        [Header("Wiring")]
        [SerializeField]
        private Animator? _animator;

        [SerializeField]
        private PlayerMotor? _motor;

        [Tooltip("Drives the Crouch/CrouchWalk clips from the crouch key. Optional — a locker can still set Crouching by hand.")]
        [SerializeField]
        private PlayerStance? _stance;

        [Header("Clips (Player.fbx)")]
        [SerializeField]
        private AnimationClip? _idle;

        [SerializeField]
        private AnimationClip? _walk;

        [SerializeField]
        private AnimationClip? _run;

        [SerializeField]
        private AnimationClip? _crouch;

        [SerializeField]
        private AnimationClip? _crouchWalk;

        // DELETED with §03's 목표물 and §08's 대형 전리품: _carry, _carryIdle and
        // _carryHeavy. Three of the nine clip slots the scene serialised were poses for
        // holding something, and nothing is held. SoloPlaytest.AnimationSlots and
        // RequiredDriverReferences dropped their matching rows in the same commit, which
        // is the condition the previous round wrote down for removing them.

        [SerializeField]
        private AnimationClip? _death;

        [Header("Blending")]
        [Tooltip("Crossfade length in seconds. Presentation only — no rule reads it.")]
        [SerializeField]
        private float _blendSeconds = 0.15f;

        [Tooltip("Normalised times in the gait cycle where a foot lands. Two per cycle for a biped.")]
        [SerializeField]
        private float[] _footfallPhases = { 0f, 0.5f };

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly AnimationClipPlayable[] _clips = new AnimationClipPlayable[StateCount];
        private readonly bool[] _hasClip = new bool[StateCount];
        private readonly float[] _weights = new float[StateCount];

        private PlayerAnimationState _state = PlayerAnimationState.Idle;
        private bool _graphBuilt;
        private PlayerAnimationState _footfallState = PlayerAnimationState.Idle;
        private float _lastPhase = -1f;

        /// <summary>
        /// A foot landed. §12's Listener channel: <see cref="PlayerFootsteps"/> subscribes
        /// and picks the clip set from the surface under that foot.
        /// </summary>
        public event Action? Footfall;

        /// <summary>The pose being played. Read by the HUD and by anything that needs to know what others can see.</summary>
        public PlayerAnimationState State
        {
            get { return _state; }
        }

        /// <summary>
        /// Crouching, as set from outside. There is a crouch key now, and a wired
        /// <see cref="PlayerStance"/> is ORed with this rather than replacing it: the
        /// clips also serve the hiding-spot and clue-reading work, which drives them
        /// without this file having to learn what a locker is.
        /// <para>
        /// §05 requires the other three players to be able to read what someone is
        /// doing, so a crouch that changed the collider and the speed but left the
        /// visible body standing would be the worst of both — a player who looks
        /// walkable, moves at half speed, and reads to a teammate as nothing at all.
        /// </para>
        /// </summary>
        public bool Crouching { get; set; }

        /// <summary>
        /// Whether the pose is crouched this frame, from either source. What
        /// <see cref="Resolve"/> is actually asked.
        /// </summary>
        public bool CrouchingNow
        {
            get { return Crouching || (_stance != null && _stance.IsCrouched); }
        }

        /// <summary>§09: dead players play <see cref="PlayerAnimationState.Death"/> and stop being posed by movement.</summary>
        public bool Dead { get; set; }

        /// <summary>
        /// Chooses the pose from what a player is doing. Pure, so the mapping can be
        /// asserted in a test without a graph, an animator or a clip existing.
        /// </summary>
        /// <param name="groundSpeed">Measured horizontal speed, m/s.</param>
        /// <param name="crouching">Crouched.</param>
        /// <param name="dead">§09.</param>
        public static PlayerAnimationState Resolve(float groundSpeed, bool crouching, bool dead)
        {
            if (dead)
            {
                return PlayerAnimationState.Death;
            }

            // The project's one definition of "not moving", shared with PlayerFootsteps
            // and ViewMotionTuning. A runner who is idle to the animator and walking to
            // the mix is three systems disagreeing about one fact.
            var moving = groundSpeed > GameConstants.StillSpeedThreshold;

            // DELETED with §03 and §08: the carryingObjective and visiblyBurdened
            // parameters and the three poses they selected. Both were always false at
            // every call site once there was nothing to pick up.

            if (crouching)
            {
                return moving ? PlayerAnimationState.CrouchWalk : PlayerAnimationState.Crouch;
            }

            if (!moving)
            {
                return PlayerAnimationState.Idle;
            }

            // Halfway between §06's 걷기 2.0 and 달리기 4.5. Derived from the two speeds
            // rather than picked, so retuning either moves the crossover with it.
            var runThreshold = (GameConstants.WalkSpeed + GameConstants.RunSpeed) * 0.5f;
            return groundSpeed >= runThreshold ? PlayerAnimationState.Run : PlayerAnimationState.Walk;
        }

        /// <summary>
        /// The clip assigned to a pose, or null if that slot is empty.
        /// <para>
        /// This class owns the pose → clip mapping, so a capture rig asks it rather than
        /// loading the FBX and matching names again. Two copies of that mapping is how a
        /// review render ends up photographing the wrong clip and looking correct.
        /// </para>
        /// </summary>
        /// <param name="state">The pose.</param>
        public AnimationClip? ClipFor(PlayerAnimationState state)
        {
            switch (state)
            {
                case PlayerAnimationState.Idle: return _idle;
                case PlayerAnimationState.Walk: return _walk;
                case PlayerAnimationState.Run: return _run;
                case PlayerAnimationState.Crouch: return _crouch;
                case PlayerAnimationState.CrouchWalk: return _crouchWalk;
                case PlayerAnimationState.Death: return _death;
                default: return null;
            }
        }

        /// <summary>
        /// The authored speed of a locomotion clip, m/s, used to scale playback so the feet
        /// do not skate. Non-locomotion poses return 0, meaning "play at 1×".
        /// </summary>
        /// <param name="state">The pose.</param>
        public static float ReferenceSpeed(PlayerAnimationState state)
        {
            switch (state)
            {
                case PlayerAnimationState.Walk:
                case PlayerAnimationState.CrouchWalk:
                    return GameConstants.WalkSpeed;
                case PlayerAnimationState.Run:
                    return GameConstants.RunSpeed;
                default:
                    return 0f;
            }
        }

        private void Reset()
        {
            _animator = GetComponentInChildren<Animator>();
            _motor = GetComponentInParent<PlayerMotor>();
            _stance = GetComponentInParent<PlayerStance>();
        }

        private void Awake()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            if (_motor == null)
            {
                _motor = GetComponentInParent<PlayerMotor>();
            }

            if (_stance == null)
            {
                _stance = GetComponentInParent<PlayerStance>();
            }
        }

        private void OnEnable()
        {
            BuildGraph();
        }

        private void OnDisable()
        {
            DestroyGraph();
        }

        private void Update()
        {
            if (!_graphBuilt)
            {
                return;
            }

            var speed = _motor != null ? _motor.GroundSpeed : 0f;

            // Both carry flags are now constants, and they are passed rather than removed
            // from Resolve on purpose. They came from PlayerLoadout — §03's 목표물 in both
            // hands, and §08's 대형 전리품 — and a runner
            // in a race carries nothing but a torch, so neither can be true.
            //
            // Resolve keeps its two parameters and the driver keeps all nine clip slots.
            // Carry/CarryIdle/CarryHeavy are unreachable at runtime today, and an
            // unreachable pose is a very different thing from a missing one: the rig is
            // still assembled with nine clips, PlayerFeelHarness can still drive the poses
            // for a look, and the day something IS carried — a shut door being dragged, a
            // body — the animation exists rather than having to be re-authored.
            var next = Resolve(speed, CrouchingNow, Dead);
            _state = next;

            AdvanceWeights(next, Time.deltaTime);
            ApplyPlaybackSpeed(next, speed);
            PollFootfall(next);
        }

        private void BuildGraph()
        {
            if (_graphBuilt || _animator == null)
            {
                return;
            }

            // Root motion off: §05's speeds come from SpeedResolver, and a clip that also
            // moved the body would add an untracked second opinion about how fast a player
            // is — which is the one thing §14's questions 1 and 2 cannot tolerate.
            _animator.applyRootMotion = false;

            _graph = PlayableGraph.Create("PlayerAnimation:" + name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            _mixer = AnimationMixerPlayable.Create(_graph, StateCount);

            Attach(PlayerAnimationState.Idle, _idle);
            Attach(PlayerAnimationState.Walk, _walk);
            Attach(PlayerAnimationState.Run, _run);
            Attach(PlayerAnimationState.Crouch, _crouch);
            Attach(PlayerAnimationState.CrouchWalk, _crouchWalk);
            Attach(PlayerAnimationState.Death, _death);

            var output = AnimationPlayableOutput.Create(_graph, "PlayerAnimation", _animator);
            output.SetSourcePlayable(_mixer);

            var idleIndex = (int)PlayerAnimationState.Idle;
            if (_hasClip[idleIndex])
            {
                _weights[idleIndex] = 1f;
                _mixer.SetInputWeight(idleIndex, 1f);
            }

            _graph.Play();
            _graphBuilt = true;
        }

        private void Attach(PlayerAnimationState state, AnimationClip? clip)
        {
            if (clip == null)
            {
                return;
            }

            var index = (int)state;
            _clips[index] = AnimationClipPlayable.Create(_graph, clip);
            _clips[index].SetApplyFootIK(false);
            _graph.Connect(_clips[index], 0, _mixer, index);
            _mixer.SetInputWeight(index, 0f);
            _hasClip[index] = true;
        }

        private void DestroyGraph()
        {
            if (!_graphBuilt)
            {
                return;
            }

            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            for (var i = 0; i < StateCount; i++)
            {
                _hasClip[i] = false;
                _weights[i] = 0f;
            }

            _graphBuilt = false;
        }

        private void AdvanceWeights(PlayerAnimationState target, float deltaSeconds)
        {
            var targetIndex = (int)target;
            var rate = _blendSeconds > 0f ? deltaSeconds / _blendSeconds : 1f;
            var total = 0f;

            for (var i = 0; i < StateCount; i++)
            {
                if (!_hasClip[i])
                {
                    _weights[i] = 0f;
                    continue;
                }

                var want = i == targetIndex ? 1f : 0f;
                _weights[i] = Mathf.MoveTowards(_weights[i], want, rate);
                total += _weights[i];
            }

            if (total <= 0f)
            {
                return;
            }

            for (var i = 0; i < StateCount; i++)
            {
                _mixer.SetInputWeight(i, _hasClip[i] ? _weights[i] / total : 0f);
            }
        }

        private void ApplyPlaybackSpeed(PlayerAnimationState state, float groundSpeed)
        {
            var index = (int)state;
            if (!_hasClip[index])
            {
                return;
            }

            var reference = ReferenceSpeed(state);
            var speed = reference > 0f ? groundSpeed / reference : 1f;

            // Clamped below at zero only: a stopped player mid-blend should freeze the
            // stride rather than run it backwards, and there is no upper bound worth
            // inventing — the Runner's 5.6 over the 4.5 run clip is 1.24×, which is what
            // sprinting is supposed to look like.
            _clips[index].SetSpeed(speed > 0f ? speed : 0f);
        }

        private void PollFootfall(PlayerAnimationState state)
        {
            var index = (int)state;
            if (!_hasClip[index] || ReferenceSpeed(state) <= 0f || _footfallPhases == null
                || _footfallPhases.Length == 0)
            {
                _lastPhase = -1f;
                return;
            }

            var clip = _clips[index].GetAnimationClip();
            if (clip == null || clip.length <= 0f)
            {
                _lastPhase = -1f;
                return;
            }

            var phase = Mathf.Repeat((float)_clips[index].GetTime() / clip.length, 1f);

            // A change of pose restarts the accounting rather than firing: the first step
            // out of a standstill belongs to the new clip's own timeline, and reporting the
            // jump between two unrelated phases would put a footstep under a foot that is
            // still in the air.
            if (_lastPhase < 0f || state != _footfallState)
            {
                _footfallState = state;
                _lastPhase = phase;
                return;
            }

            var previous = _lastPhase;
            _lastPhase = phase;

            if (Mathf.Approximately(previous, phase))
            {
                return;
            }

            for (var i = 0; i < _footfallPhases.Length; i++)
            {
                if (CrossedMarker(previous, phase, Mathf.Repeat(_footfallPhases[i], 1f)))
                {
                    Footfall?.Invoke();
                }
            }
        }

        /// <summary>
        /// Whether the clip's normalised time passed <paramref name="marker"/> between two
        /// samples, wrap included. The wrap case is not an edge case to tidy away — a
        /// marker at phase 0 is crossed on every single cycle and nowhere else.
        /// </summary>
        private static bool CrossedMarker(float previous, float current, float marker)
        {
            if (current > previous)
            {
                return marker > previous && marker <= current;
            }

            // Wrapped past the end of the clip this frame.
            return marker > previous || marker <= current;
        }
    }
}
