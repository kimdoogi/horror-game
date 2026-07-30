#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Monster;
using UnityEngine;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// Plays <c>Monster.fbx</c>'s seven clips against §06's state machine.
    /// <para>
    /// The mapping is one to one and the clips are named for the states, so this class
    /// has no table of exceptions and no opinion: <c>Patrol · Alert · Chase · Search ·
    /// Standstill</c> are the five states, <c>Stunned</c> is §04's flash suspending
    /// whichever state was running, and <c>Grab</c> is the kill — an event the host
    /// raises, not a sixth state (see <see cref="MonsterAgent.PlayGrab"/>).
    /// </para>
    /// <para>
    /// <b>Standstill animates.</b> ASSETS.md §3.1 is explicit that 정지 "moves, makes no
    /// sound" — the creature keeps breathing and turning its head while the Listener
    /// loses it. A frozen pose would tell every player looking at it that something
    /// changed, which is the one thing §06's silence is supposed to hide.
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
        /// <summary>
        /// Animator state names, indexed by <see cref="MonsterStateId"/>. Order is the
        /// enum's order and the enum is §06's table, so an added state would break the
        /// build here rather than silently play the wrong clip.
        /// </summary>
        private static readonly string[] StateClipNames =
        {
            "Patrol",
            "Alert",
            "Chase",
            "Search",
            "Standstill",
        };

        /// <summary>§04's 섬광수. A one-shot: <c>AssetImportPolicy</c> forbids it looping.</summary>
        private const string StunnedClipName = "Stunned";

        /// <summary>The kill. A one-shot, raised by the host rather than by a state.</summary>
        private const string GrabClipName = "Grab";

        /// <summary>Float parameter carrying the current §07 speed in m/s, when the controller has one.</summary>
        private const string SpeedParameterName = "Speed";

        [SerializeField]
        [Tooltip("Left empty, the Animator on this object or a child is used.")]
        private Animator? _animator;

        [SerializeField]
        [Tooltip("Blend time between state clips, seconds. Presentation only — not a §-number.")]
        private float _crossFadeSeconds = 0.12f;

        [SerializeField]
        [Tooltip("Layer the state clips live on.")]
        private int _layer;

        private int[] _stateHashes = System.Array.Empty<int>();
        private int _stunnedHash;
        private int _grabHash;
        private bool _hasSpeedParameter;

        private MonsterStateId _appliedState = MonsterStateId.Patrol;
        private bool _appliedStunned;
        private bool _grabPlaying;
        private bool _applied;

        /// <summary>
        /// Puts the animator on the state the brain is in.
        /// <para>
        /// Called every fixed step, and it re-crossfades only when something actually
        /// changed — an Animator asked to cross-fade into the state it is already in
        /// restarts the clip, which would leave a patrolling monster stuck on the first
        /// frame of its walk cycle and, through <see cref="MonsterFootsteps"/>, silent.
        /// </para>
        /// </summary>
        /// <param name="state">§06's current row. Authority: <see cref="MonsterBrain.State"/>.</param>
        /// <param name="isStunned">§04's flash, which suspends the state rather than replacing it.</param>
        /// <param name="speedMetresPerSecond">§07's speed for this moment, used to scale the gait.</param>
        public void Apply(MonsterStateId state, bool isStunned, float speedMetresPerSecond)
        {
            var animator = _animator;
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return;
            }

            // The clips were authored at §06's headline 4.8 m/s — the 심야 row, per
            // ThreatTier. Scaling playback by the tier's own speed is what makes the
            // 초저녁 monster read as slower instead of moon-walking at the same cadence,
            // and it is what carries the change through to the footstep cadence.
            var locomotion = !isStunned && !_grabPlaying;
            animator.speed = locomotion && speedMetresPerSecond > 0f
                ? speedMetresPerSecond / GameConstants.MonsterBaseSpeed
                : 1f;

            if (_hasSpeedParameter)
            {
                animator.SetFloat(SpeedParameterName, speedMetresPerSecond);
            }

            var changed = !_applied || state != _appliedState || isStunned != _appliedStunned;
            _appliedState = state;
            _appliedStunned = isStunned;
            _applied = true;

            if (!changed)
            {
                return;
            }

            // §06 moving on outranks the kill animation: the brain is the authority on
            // what the monster is doing, and a Grab still playing while it has gone back
            // to patrolling would be the adapter contradicting it.
            _grabPlaying = false;

            Play(isStunned ? _stunnedHash : StateHashOf(state));
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
            Play(_grabHash);
        }

        private void Awake()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            CacheHashes();
        }

        private void Update()
        {
            if (!_grabPlaying)
            {
                return;
            }

            var animator = _animator;
            if (animator == null)
            {
                _grabPlaying = false;
                return;
            }

            // Let the one-shot finish, then fall back to whatever §06 says the monster
            // is doing. Asking the animator rather than timing it means a re-authored
            // Grab of a different length still hands control back at the right moment.
            var info = animator.GetCurrentAnimatorStateInfo(_layer);
            if (info.shortNameHash == _grabHash && info.normalizedTime < 1f)
            {
                return;
            }

            _grabPlaying = false;
            _applied = false;
        }

        private void CacheHashes()
        {
            _stateHashes = new int[StateClipNames.Length];
            for (var i = 0; i < StateClipNames.Length; i++)
            {
                _stateHashes[i] = Animator.StringToHash(StateClipNames[i]);
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
                if (parameters[i].type == AnimatorControllerParameterType.Float
                    && parameters[i].name == SpeedParameterName)
                {
                    _hasSpeedParameter = true;
                    break;
                }
            }
        }

        private int StateHashOf(MonsterStateId state)
        {
            var index = (int)state;
            return index >= 0 && index < _stateHashes.Length ? _stateHashes[index] : 0;
        }

        private void Play(int stateHash)
        {
            var animator = _animator;
            if (animator == null || stateHash == 0)
            {
                return;
            }

            // A controller that has not been authored yet is a bring-up state, not an
            // error worth a stack trace on every transition.
            if (animator.runtimeAnimatorController == null || !animator.HasState(_layer, stateHash))
            {
                return;
            }

            if (_crossFadeSeconds > 0f)
            {
                animator.CrossFadeInFixedTime(stateHash, _crossFadeSeconds, _layer);
            }
            else
            {
                animator.Play(stateHash, _layer);
            }
        }
    }
}
