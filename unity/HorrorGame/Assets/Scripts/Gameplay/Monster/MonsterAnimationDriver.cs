#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Monster;
using UnityEngine;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// Plays <c>Monster.fbx</c>'s seven clips against §06's state machine.
    /// <para>
    /// The controller <see cref="HorrorGame.Gameplay.MonsterEditor.MonsterRig"/> builds
    /// has two layers, and the split is what lets §06's table be honest:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Locomotion</b> — a blend tree on measured ground speed running
    /// Standstill → Patrol → Chase. It owns the hips and both legs.</item>
    /// <item><b>Attention</b> — the state's character (Alert's head snaps, Search's
    /// casting about) masked to the spine and above.</item>
    /// </list>
    /// <para>
    /// §06 has the monster <em>moving</em> in 경계 ("소리 방향으로 이동") and in 수색
    /// ("마지막 위치 반경을 뒤짐"), but both clips are authored in place — the design
    /// describes what the creature is paying attention to, not what its legs are doing.
    /// Playing either as a whole-body clip therefore skates it across §12's floor at
    /// whatever speed §07's tier says. Layering fixes that at the root: the legs always
    /// walk at the speed the creature is actually travelling, and the state decides
    /// what everything above the hips is doing.
    /// </para>
    /// <para>
    /// <b>Speed comes from measurement, in both directions.</b> The clips' own ground
    /// speeds were measured in Blender from the weight-bearing foot and published in
    /// <c>Monster.clips.json</c>; the creature's actual speed comes from how far
    /// <c>MonsterBrain</c> moved it this step. Playback rate is the ratio. The previous
    /// version divided §07's tier speed by <see cref="GameConstants.MonsterBaseSpeed"/>,
    /// which assumes every clip was authored at 4.8 m/s — true of Chase and wrong by
    /// 3.4× for Patrol, which is why the patrolling monster skated.
    /// </para>
    /// <para>
    /// Nothing here is required for the monster to work. A prefab with no Animator, or
    /// a controller missing a state, degrades to no animation rather than to an
    /// exception — the rules are in the brain and they run either way.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterAnimationDriver : MonoBehaviour
    {
        /// <summary>The blend-tree state that owns the hips and legs on layer 0.</summary>
        public const string LocomotionStateName = "Locomotion";

        /// <summary>Blend parameter: the creature's measured ground speed, m/s.</summary>
        public const string SpeedParameterName = "Speed";

        /// <summary>
        /// Playback-rate parameter on the locomotion state. Carries
        /// measured ÷ authored, so a stride covers the ground it claims to.
        /// </summary>
        public const string GaitRateParameterName = "GaitRate";

        /// <summary>§04's 섬광수. A one-shot: <c>AssetImportPolicy</c> forbids it looping.</summary>
        public const string StunnedClipName = "Stunned";

        /// <summary>The kill. A one-shot, raised by the host rather than by a state.</summary>
        public const string GrabClipName = "Grab";

        /// <summary>Layer index of the locomotion blend tree, its stun and its kill.</summary>
        public const int BodyLayer = 0;

        /// <summary>Layer index of the masked attention clips.</summary>
        public const int AttentionLayer = 1;

        /// <summary>
        /// Attention-layer state names indexed by <see cref="MonsterStateId"/>. An empty
        /// entry means the state has nothing to say above the hips and the layer fades
        /// out — Patrol and Standstill are already fully described by the blend tree.
        /// Order is the enum's, and the enum is §06's table, so a state added there
        /// breaks the build here rather than silently playing the wrong clip.
        /// </summary>
        private static readonly string[] AttentionStateNames =
        {
            string.Empty,   // Patrol     — the blend tree is the whole animation
            "Alert",        // 경계        — head snaps toward the sound
            "Chase",        // 추격        — the committed forward lean and pumping arms
            "Search",       // 수색        — casting about, arms dragging
            string.Empty,   // Standstill — the blend tree's zero-speed pose is the state
        };

        [SerializeField]
        [Tooltip("Left empty, the Animator on this object or a child is used.")]
        private Animator? _animator;

        [Header("Blending")]
        [SerializeField]
        [Tooltip("Blend into a state that is not a reaction, seconds. Presentation — not a §-number.")]
        private float _settleSeconds = 0.28f;

        [SerializeField]
        [Tooltip("Blend into Alert, Chase, Stunned or Grab, seconds. A reaction must read as sudden.")]
        private float _snapSeconds = 0.07f;

        [SerializeField]
        [Tooltip("Seconds for the attention layer's weight to reach its target.")]
        private float _attentionFadeSeconds = 0.22f;

        [Header("The blend tree's members, as measured in Blender")]
        [SerializeField]
        [Tooltip("Patrol's own ground speed. From Assets/Models/Characters/Monster.clips.json.")]
        private float _patrolClipSpeed = 1.4085f;

        [SerializeField]
        [Tooltip("Patrol's cycle length, seconds. Same manifest.")]
        private float _patrolClipSeconds = 1.6f;

        [SerializeField]
        [Tooltip("Chase's own ground speed. From Assets/Models/Characters/Monster.clips.json.")]
        private float _chaseClipSpeed = 4.5795f;

        [SerializeField]
        [Tooltip("Chase's cycle length, seconds. Same manifest.")]
        private float _chaseClipSeconds = 0.6667f;

        [SerializeField]
        [Tooltip("Standstill's length, seconds. It is the tree's zero-speed member. Same manifest.")]
        private float _standstillClipSeconds = 3f;

        [Header("Peers")]
        [SerializeField]
        private MonsterStandstillHold? _standstill;

        [SerializeField]
        private MonsterAcquireTell? _acquireTell;

        private int _locomotionHash;
        private int[] _attentionHashes = System.Array.Empty<int>();
        private int _stunnedHash;
        private int _grabHash;
        private bool _hasSpeed;
        private bool _hasGaitRate;

        private MonsterStateId _appliedState = MonsterStateId.Patrol;
        private bool _appliedStunned;
        private bool _grabPlaying;
        private bool _applied;
        private float _attentionWeight;
        private float _attentionTarget;

        /// <summary>The creature's speed as last applied, m/s. Read by the debug view.</summary>
        public float AppliedSpeed { get; private set; }

        /// <summary>Playback rate the locomotion state is running at. 1 means the clip's own cadence.</summary>
        public float GaitRate { get; private set; } = 1f;

        /// <summary>
        /// Puts the animator on the state the brain is in.
        /// </summary>
        /// <param name="state">§06's current row. Authority: <see cref="MonsterBrain.State"/>.</param>
        /// <param name="isStunned">§04's flash, which suspends the state rather than replacing it.</param>
        /// <param name="speedMetresPerSecond">
        /// How fast the creature is <em>actually</em> travelling, from the brain's own
        /// position delta — not §07's tier speed. The two differ whenever the monster is
        /// cornering, waiting on a path or standing still, and it is the real one the
        /// feet have to match.
        /// </param>
        public void Apply(MonsterStateId state, bool isStunned, float speedMetresPerSecond)
        {
            var animator = _animator;
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return;
            }

            AppliedSpeed = Mathf.Max(0f, speedMetresPerSecond);

            _standstill?.SetState(state, isStunned);
            _acquireTell?.SetState(state, isStunned);

            var locomotion = !isStunned && !_grabPlaying;
            if (_hasSpeed)
            {
                animator.SetFloat(SpeedParameterName, locomotion ? AppliedSpeed : 0f);
            }

            GaitRate = locomotion ? RateFor(AppliedSpeed) : 1f;
            if (_hasGaitRate)
            {
                animator.SetFloat(GaitRateParameterName, GaitRate);
            }
            else
            {
                // No parameter on the controller: fall back to scaling the whole
                // animator. Coarser — it drags the attention layer along with it — but
                // still far closer than leaving the gait at its authored cadence.
                animator.speed = GaitRate;
            }

            var changed = !_applied || state != _appliedState || isStunned != _appliedStunned;
            _appliedState = state;
            _appliedStunned = isStunned;
            _applied = true;

            _attentionTarget = isStunned || _grabPlaying ? 0f : AttentionWeightFor(state);

            if (!changed)
            {
                return;
            }

            // §06 moving on outranks the kill animation: the brain is the authority on
            // what the monster is doing, and a Grab still playing while it has gone back
            // to patrolling would be the adapter contradicting it.
            _grabPlaying = false;

            if (isStunned)
            {
                Play(BodyLayer, _stunnedHash, _snapSeconds);
                return;
            }

            Play(BodyLayer, _locomotionHash, BlendFor(state));
            var attention = AttentionHashOf(state);
            if (attention != 0)
            {
                Play(AttentionLayer, attention, BlendFor(state));
            }
        }

        /// <summary>
        /// Plays the kill clip once. Presentation only: §06 has five states and a catch
        /// is not one of them, so nothing about the brain changes here.
        /// </summary>
        public void PlayGrab()
        {
            if (_grabHash == 0)
            {
                return;
            }

            _grabPlaying = true;
            _attentionTarget = 0f;
            Play(BodyLayer, _grabHash, _snapSeconds);
        }

        /// <summary>
        /// Playback rate that makes a stride cover the ground it claims to.
        /// <para>
        /// The blend tree already mixes Patrol and Chase by speed, so the clip playing at
        /// a given moment has its own nominal ground speed — the interpolation of the two
        /// authored speeds at the current blend point. Dividing the real speed by that is
        /// what leaves the feet planted; it lands on 1.0 whenever the blend point matches
        /// reality and only departs from it at the ends of the range, where the tree has
        /// run out of clips to interpolate.
        /// </para>
        /// <para>
        /// Below Patrol's own speed the tree blends toward Standstill, whose ground speed
        /// is zero — so the nominal speed falls with the real one and the rate stays near
        /// 1 rather than diverging. That is why Standstill is a member of the locomotion
        /// tree and not only a state of its own.
        /// </para>
        /// </summary>
        public float RateFor(float speedMetresPerSecond)
        {
            var speed = Mathf.Max(0f, speedMetresPerSecond);
            var nominal = NominalSpeedAt(speed);

            // A creature barely moving has no stride to match, and dividing by a nominal
            // speed approaching zero would spin the legs. Hold the rate at 1 and let the
            // tree's Standstill member carry the pose.
            if (nominal <= 0.05f)
            {
                return 1f;
            }

            // Clamped because a rate outside this band stops reading as the same
            // creature: 0.35 is a stagger and 2.5 is a cartoon. §07's tiers span
            // 4.4–5.2 m/s against a 4.58 m/s clip, so the honest range never comes
            // close to either end and the clamp only ever catches a bug.
            return Mathf.Clamp(speed / nominal, 0.35f, 2.5f);
        }

        /// <summary>
        /// Ground speed the blend tree actually produces at a given blend point.
        /// <para>
        /// <b>Not the blend parameter.</b> Setting the thresholds to the clips' own
        /// speeds makes it tempting to assume the tree outputs whatever the parameter
        /// says, and it does not: Mecanim time-scales a tree's children so they finish
        /// together, so a blend of two cycles has the weighted-average <em>duration</em>
        /// and the weighted-average <em>stride</em>, and speed is the quotient of those
        /// two averages rather than the average of the quotients.
        /// </para>
        /// <para>
        /// The gap is not small. Halfway between Patrol (2.25 m per 1.60 s cycle) and
        /// Chase (3.05 m per 0.667 s cycle) the tree runs at 5.31/2.27 = 2.34 m/s while
        /// the parameter reads 2.99 — 28% of foot slide, at exactly the speed a monster
        /// rounding a §12 corner travels at. This is the correction for it.
        /// </para>
        /// </summary>
        public float NominalSpeedAt(float blendSpeed)
        {
            if (blendSpeed <= 0f)
            {
                return 0f;
            }

            float still, patrol, chase;
            if (blendSpeed <= _patrolClipSpeed)
            {
                var t = _patrolClipSpeed > 0f ? blendSpeed / _patrolClipSpeed : 1f;
                still = 1f - t;
                patrol = t;
                chase = 0f;
            }
            else
            {
                var span = _chaseClipSpeed - _patrolClipSpeed;
                var t = span > 0f ? Mathf.Clamp01((blendSpeed - _patrolClipSpeed) / span) : 1f;
                still = 0f;
                patrol = 1f - t;
                chase = t;
            }

            var stride = patrol * _patrolClipSpeed * _patrolClipSeconds
                         + chase * _chaseClipSpeed * _chaseClipSeconds;
            var duration = still * _standstillClipSeconds
                           + patrol * _patrolClipSeconds
                           + chase * _chaseClipSeconds;

            return duration > 1e-4f ? stride / duration : 0f;
        }

        /// <summary>
        /// Overrides the tree's measured members. Used by the prefab builder so the
        /// numbers travel from <c>Monster.clips.json</c> into the prefab instead of
        /// being retyped here.
        /// </summary>
        public void SetClipFacts(float patrolSpeed, float patrolSeconds,
                                 float chaseSpeed, float chaseSeconds, float standstillSeconds)
        {
            _patrolClipSpeed = patrolSpeed > 0f ? patrolSpeed : _patrolClipSpeed;
            _patrolClipSeconds = patrolSeconds > 0f ? patrolSeconds : _patrolClipSeconds;
            _chaseClipSpeed = chaseSpeed > 0f ? chaseSpeed : _chaseClipSpeed;
            _chaseClipSeconds = chaseSeconds > 0f ? chaseSeconds : _chaseClipSeconds;
            _standstillClipSeconds = standstillSeconds > 0f ? standstillSeconds : _standstillClipSeconds;
        }

        private void Awake()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            if (_standstill == null)
            {
                _standstill = GetComponentInChildren<MonsterStandstillHold>();
            }

            if (_acquireTell == null)
            {
                _acquireTell = GetComponentInChildren<MonsterAcquireTell>();
            }

            CacheHashes();
        }

        private void Update()
        {
            var animator = _animator;
            if (animator == null)
            {
                return;
            }

            FadeAttention(animator, Time.deltaTime);

            if (!_grabPlaying)
            {
                return;
            }

            // Let the one-shot finish, then fall back to whatever §06 says the monster
            // is doing. Asking the animator rather than timing it means a re-authored
            // Grab of a different length still hands control back at the right moment.
            var info = animator.GetCurrentAnimatorStateInfo(BodyLayer);
            if (info.shortNameHash == _grabHash && info.normalizedTime < 1f)
            {
                return;
            }

            _grabPlaying = false;
            _applied = false;
        }

        /// <summary>
        /// Eases the attention layer in and out.
        /// <para>
        /// A weight that snapped would put the head into Alert's first frame with no
        /// transition while the legs kept walking, which reads as two animations rather
        /// than one creature.
        /// </para>
        /// </summary>
        private void FadeAttention(Animator animator, float deltaSeconds)
        {
            if (animator.layerCount <= AttentionLayer)
            {
                return;
            }

            if (Mathf.Approximately(_attentionWeight, _attentionTarget))
            {
                return;
            }

            var step = _attentionFadeSeconds > 0f
                ? Mathf.Max(0f, deltaSeconds) / _attentionFadeSeconds
                : 1f;
            _attentionWeight = Mathf.MoveTowards(_attentionWeight, _attentionTarget, step);
            animator.SetLayerWeight(AttentionLayer, _attentionWeight);
        }

        /// <summary>
        /// How much of the state's character shows above the hips.
        /// <para>
        /// Chase is deliberately below 1. Its clip carries the whole committed forward
        /// lean, and at full weight the masked torso fights the blend tree's own Chase
        /// contribution — the same pose applied twice reads as an over-rotation. 0.85
        /// keeps the lean and leaves the legs' own upper-body motion visible underneath.
        /// </para>
        /// </summary>
        private static float AttentionWeightFor(MonsterStateId state)
        {
            switch (state)
            {
                case MonsterStateId.Alert:
                    return 1f;
                case MonsterStateId.Search:
                    return 1f;
                case MonsterStateId.Chase:
                    return 0.85f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Blend time into a state.
        /// <para>
        /// Two speeds, and the difference is §06's meaning rather than taste. Alert and
        /// Chase are <em>reactions</em> — it heard you, it saw you — and a reaction that
        /// eases in is a reaction the player does not notice, which loses the one frame
        /// of warning §12's corner arithmetic assumes they get. Patrol, Search and
        /// Standstill are things it settles into, and 정지 in particular has to arrive
        /// without a jolt or the stillness announces itself.
        /// </para>
        /// </summary>
        private float BlendFor(MonsterStateId state) =>
            state == MonsterStateId.Alert || state == MonsterStateId.Chase ? _snapSeconds : _settleSeconds;

        private void CacheHashes()
        {
            _locomotionHash = Animator.StringToHash(LocomotionStateName);
            _attentionHashes = new int[AttentionStateNames.Length];
            for (var i = 0; i < AttentionStateNames.Length; i++)
            {
                _attentionHashes[i] = string.IsNullOrEmpty(AttentionStateNames[i])
                    ? 0
                    : Animator.StringToHash(AttentionStateNames[i]);
            }

            _stunnedHash = Animator.StringToHash(StunnedClipName);
            _grabHash = Animator.StringToHash(GrabClipName);

            var animator = _animator;
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            // Setting a parameter the controller does not declare logs an error every
            // frame. Asking once is cheaper than a screen of red during bring-up.
            var parameters = animator.parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].type != AnimatorControllerParameterType.Float)
                {
                    continue;
                }

                _hasSpeed |= parameters[i].name == SpeedParameterName;
                _hasGaitRate |= parameters[i].name == GaitRateParameterName;
            }
        }

        private int AttentionHashOf(MonsterStateId state)
        {
            var index = (int)state;
            return index >= 0 && index < _attentionHashes.Length ? _attentionHashes[index] : 0;
        }

        private void Play(int layer, int stateHash, float blendSeconds)
        {
            var animator = _animator;
            if (animator == null || stateHash == 0)
            {
                return;
            }

            // A controller that has not been authored yet is a bring-up state, not an
            // error worth a stack trace on every transition.
            if (animator.runtimeAnimatorController == null
                || layer >= animator.layerCount
                || !animator.HasState(layer, stateHash))
            {
                return;
            }

            // Re-crossfading into the state the layer is already in restarts the clip,
            // which would leave a patrolling monster stuck on the first frame of its
            // walk cycle and, through MonsterFootsteps, silent.
            var current = animator.GetCurrentAnimatorStateInfo(layer);
            if (current.shortNameHash == stateHash && !animator.IsInTransition(layer))
            {
                return;
            }

            if (blendSeconds > 0f)
            {
                animator.CrossFadeInFixedTime(stateHash, blendSeconds, layer);
            }
            else
            {
                animator.Play(stateHash, layer);
            }
        }
    }
}
