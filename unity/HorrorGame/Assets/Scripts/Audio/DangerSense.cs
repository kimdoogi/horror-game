#nullable enable

using HorrorGame.Core.Monster;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// How much trouble the local player is in, 0 to 1 — the one number the heartbeat
    /// and the monster's presence bed are both crossfaded by.
    /// <para>
    /// It is deliberately built from §06's state machine and distance <em>only</em>.
    /// Two things it must not read, and why:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Who the monster is targeting.</b> §04 makes that the 관측자's ability and §11
    /// calls it the only information in the game that cannot be bought. A heartbeat
    /// that quickened for the target would hand every player the Observer's job.
    /// </description></item>
    /// <item><description>
    /// <b>Anything host-only.</b> §13 keeps clue contents and the objective's location
    /// on the host; the monster's position and animation state are replicated already
    /// because four players have to see it move. Nothing here needs more than that.
    /// </description></item>
    /// </list>
    /// <para>
    /// 정지 still contributes. §06 calls the silence the game's weapon — "어디 갔어?
    /// 방금 여기 있었는데" — so a heartbeat that fell to nothing the moment the monster
    /// stopped would give away the state that exists to take information away. It
    /// drops, because relief is real, but not to zero, and it drops on
    /// <see cref="AudioTuning.DangerReleaseSeconds"/> rather than instantly.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Danger Sense")]
    public sealed class DangerSense : MonoBehaviour
    {
        [Tooltip("The monster in the scene. Left null between spawns; danger decays to zero.")]
        [SerializeField]
        private Transform? monster;

        private float _danger;

        /// <summary>Smoothed danger, 0 to 1.</summary>
        public float Danger01 => _danger;

        /// <summary>Distance to the monster in metres, or infinity when there is none.</summary>
        public float MonsterDistance { get; private set; } = float.PositiveInfinity;

        /// <summary>
        /// The monster's current §06 state. Set by whatever replicates it — the same
        /// value that drives the animator, so the mix and the animation can never
        /// disagree about whether it is roaring.
        /// </summary>
        public MonsterStateId MonsterState { get; set; } = MonsterStateId.Patrol;

        /// <summary>Whether there is a monster to be afraid of at all.</summary>
        public bool HasMonster => monster != null;

        /// <summary>The monster this sense is tracking. Assigned on spawn by the Net layer.</summary>
        public Transform? Monster
        {
            get { return monster; }
            set { monster = value; }
        }

        /// <summary>
        /// Danger for a state at a distance, before smoothing. Static and pure, so the
        /// curve can be asserted against without a scene.
        /// </summary>
        public static float Evaluate(MonsterStateId state, float distanceMetres)
        {
            var weight = WeightOf(state);
            if (weight <= 0f)
            {
                return 0f;
            }

            var proximity = 1f - Mathf.Clamp01(distanceMetres / AudioTuning.DangerProximityRange);
            var scale = Mathf.Lerp(AudioTuning.DangerDistanceFloorShare, 1f, proximity);
            return Mathf.Clamp01(weight * scale);
        }

        /// <summary>How much each of §06's five states contributes before distance is applied.</summary>
        public static float WeightOf(MonsterStateId state)
        {
            switch (state)
            {
                case MonsterStateId.Chase:
                    return AudioTuning.DangerChaseWeight;
                case MonsterStateId.Alert:
                    return AudioTuning.DangerAlertWeight;
                case MonsterStateId.Search:
                    return AudioTuning.DangerSearchWeight;
                case MonsterStateId.Standstill:
                    return AudioTuning.DangerStandstillWeight;
                default:
                    return AudioTuning.DangerPatrolWeight;
            }
        }

        private void Update()
        {
            var target = 0f;

            var ears = GameAudio.ListenerTransform;
            if (monster != null && ears != null)
            {
                var delta = monster.position - ears.position;

                // Flat, like every other distance in the design: §04's own reading
                // measures horizontally so a stairwell does not read as extra range.
                delta.y = 0f;
                MonsterDistance = delta.magnitude;
                target = Evaluate(MonsterState, MonsterDistance);
            }
            else
            {
                MonsterDistance = float.PositiveInfinity;
            }

            // Asymmetric: fear arrives faster than it leaves.
            var seconds = target > _danger
                ? AudioTuning.DangerAttackSeconds
                : AudioTuning.DangerReleaseSeconds;

            _danger = GameAudio.Approach(_danger, target, Time.deltaTime, seconds);
        }
    }
}
