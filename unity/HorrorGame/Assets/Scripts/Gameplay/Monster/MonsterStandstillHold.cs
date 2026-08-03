#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Monster;
using UnityEngine;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// Stops the monster moving at all while somebody is looking at it in §06's 정지.
    /// <para>
    /// §06 calls this state the game's weapon and says why:
    /// </para>
    /// <blockquote>
    /// 괴물이 멈추면 <b>소리를 내지 않는다.</b> 그러면 <b>청음사가 위치를 잃고</b>,
    /// 플레이어들은 <i>"어디 갔어? 방금 여기 있었는데"</i> 하게 되고 … <b>침묵이 가장
    /// 무서운 소리다.</b>
    /// </blockquote>
    /// <para>
    /// The clip is already authored as measured stillness — one 4.5° head roll in three
    /// seconds, feet welded, no breathing, and the generator asserts all of that. This
    /// component takes the last step the clip cannot take on its own: while a player
    /// has it in frame, playback is <b>frozen outright</b>, so the creature is not
    /// merely still, it is <em>identical</em> between two glances. A player who looks
    /// away and looks back cannot say whether it moved, because the only honest answer
    /// is that it did not — and then, in the moment nobody is watching, the head turns.
    /// </para>
    /// <para>
    /// Why gate it on being observed rather than freezing always: a permanently frozen
    /// pose is a statue and reads as a bug, and the head turn is what tells the player
    /// afterwards that the thing is awake. Doing the turn only while unobserved is what
    /// converts stillness into doubt, and doubt is what §06 asked for. It is also the
    /// only version that is honest — a turn performed on camera answers the question
    /// the state exists to leave open.
    /// </para>
    /// <para>
    /// <b>Presentation only.</b> Nothing here reaches <c>MonsterBrain</c>: the state,
    /// its timers and its silence are already decided in Core and are unchanged by
    /// whether anyone is looking. This decides frames, not rules — which is also why it
    /// is safe for it to depend on a local camera on a host-authoritative creature
    /// (§13).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterStandstillHold : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Left empty, the Animator on this object or a child is used.")]
        private Animator? _animator;

        [SerializeField]
        [Tooltip("How far off the observer's view axis still counts as 'in frame', degrees. Presentation.")]
        [Range(10f, 90f)]
        private float _observedHalfAngle = 55f;

        [SerializeField]
        [Tooltip("Seconds to ease playback back in once nobody is looking. A hard restart is a visible tick.")]
        private float _releaseSeconds = 0.6f;

        [SerializeField]
        [Tooltip("Height above the transform the view test aims at, metres. Rig geometry, not a §-number.")]
        private float _chestHeight = 1.7f;

        private bool _holding;
        private float _release;
        private bool _active;

        /// <summary>Whether playback is currently pinned. Read by the capture rig and the debug view.</summary>
        public bool IsHolding => _holding;

        /// <summary>
        /// Tells the hold which §06 row the brain is on. Called from
        /// <see cref="MonsterAnimationDriver"/> so there is one path from the brain to
        /// every presentation component.
        /// </summary>
        public void SetState(MonsterStateId state, bool isStunned)
        {
            // A flash outranks the hold. §04 buys the Flasher a visible window
            // (GameConstants.MonsterStunSeconds) and a creature frozen through its own
            // recoil would hide the one piece of feedback that ability has.
            var wanted = state == MonsterStateId.Standstill && !isStunned;
            if (wanted == _active)
            {
                return;
            }

            _active = wanted;
            if (!wanted)
            {
                Release();
            }
        }

        /// <summary>
        /// Applies the hold for one observer. Public and camera-argued for the same
        /// reason <see cref="MonsterBeamResolve.Apply(Camera)"/> is: the capture rig
        /// drives the real component rather than approximating it.
        /// </summary>
        public void Apply(Camera? observer, float deltaSeconds)
        {
            var animator = _animator;
            if (animator == null)
            {
                return;
            }

            var shouldHold = _active && IsObservedBy(observer);
            if (shouldHold)
            {
                _holding = true;
                _release = 0f;
                animator.speed = 0f;
                return;
            }

            if (!_holding)
            {
                return;
            }

            // Easing rather than snapping back. Restarting a stopped animator at full
            // rate is a visible tick, and a tick at the moment the player looks away is
            // the one frame that would give the trick away in a recording.
            _release += Mathf.Max(0f, deltaSeconds);
            var t = _releaseSeconds > 0f ? Mathf.Clamp01(_release / _releaseSeconds) : 1f;
            animator.speed = t;
            if (t >= 1f)
            {
                _holding = false;
            }
        }

        private void Awake()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }
        }

        private void LateUpdate()
        {
            Apply(Camera.main, Time.deltaTime);
        }

        private void Release()
        {
            _release = _releaseSeconds;
            if (_animator != null)
            {
                _animator.speed = 1f;
            }

            _holding = false;
        }

        /// <summary>
        /// Whether the creature is inside the observer's view.
        /// <para>
        /// A cone test rather than a frustum test, and generous at 55°: the failure that
        /// matters is holding when the player <em>can</em> see it, because a creature
        /// caught mid-turn at the edge of the screen is the trick showing. Being
        /// occasionally too cautious costs nothing — the clip it falls back to is
        /// already 4.5° of motion in three seconds.
        /// </para>
        /// <para>
        /// Deliberately not a raycast. §06's Standstill is most dangerous exactly when
        /// the creature is half-behind a doorframe, and an occlusion test would let it
        /// turn its head in the gap the player is staring through.
        /// </para>
        /// </summary>
        private bool IsObservedBy(Camera? observer)
        {
            if (observer == null)
            {
                return false;
            }

            var eye = observer.transform;
            var toMonster = transform.position + Vector3.up * _chestHeight - eye.position;
            if (toMonster.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            return Vector3.Angle(eye.forward, toMonster) <= _observedHalfAngle;
        }
    }
}
