#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Abilities;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Monster;
using HorrorGame.Core.Session;
using UnityEngine;
using Vec3 = HorrorGame.Core.Math.Vec3;

namespace HorrorGame.Audio
{
    /// <summary>
    /// Drives §04's 청음사 — it steps <c>ListenerAbility</c> and publishes the fix.
    /// <para>
    /// <b>It contains no rule.</b> Everything about what the Listener can hear, how
    /// wrong the fix is, when the feed cuts and what §12's floors are worth lives in
    /// <c>ListenerAbility</c> and <c>GameConstants</c>. This component's whole job is
    /// to turn Unity's world into the three arguments that ability takes — where the
    /// player is, how much noise they are making, and a snapshot of the monster — and
    /// to hand the answer to the HUD.
    /// </para>
    /// <para>
    /// The two ways the ability goes quiet are both real here, and neither is
    /// compensated for:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// §06's 정지. A stopped monster is silent, the ability returns nothing, and
    /// <see cref="MonsterAudio"/> stops the presence bed at the same moment — so the
    /// HUD losing the fix and the room going quiet are one event. "어디 갔어? 방금
    /// 여기 있었는데."
    /// </description></item>
    /// <item><description>
    /// §04's own price — "자기가 소리를 내면 못 듣는다". <see cref="NoiseMeter"/> reports
    /// the level, the ability cuts the feed above
    /// <c>GameConstants.ListenerSelfNoiseThreshold</c>, and
    /// <see cref="GameAudio.ReportSelfNoise"/> ducks and dulls the informational buses
    /// so the player hears why. §10 prices the role as 소리를 듣는다 / 자기가 조용해야
    /// 한다, and a price nobody can perceive is not a price.
    /// </description></item>
    /// </list>
    /// <para>
    /// Stepped at <c>GameConstants.FixedStep</c> from an accumulator rather than in
    /// <c>FixedUpdate</c>, because Unity's physics step is a project setting and this
    /// ability's fix interval (§04's 0.5 s) is a design number. They must not drift
    /// apart when someone changes the physics rate.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Listener Audio Driver")]
    public sealed class ListenerAudioDriver : MonoBehaviour
    {
        [Tooltip("Tracks the monster's transform and §06 state. Required.")]
        [SerializeField]
        private DangerSense? danger;

        [Tooltip("This player's own noise. §04: 자기가 소리를 내면 못 듣는다.")]
        [SerializeField]
        private NoiseMeter? noise;

        [Tooltip("Seed for the fix error. §13 requires a seeded match to replay identically, " +
                 "so the host hands this down rather than letting each client roll its own.")]
        [SerializeField]
        private int randomSeed = 1;

        private ListenerAbility? _ability;
        private IWorldProbe? _probe;
        private float _accumulator;

        private Vector3 _lastMonsterPosition;
        private Vector3 _monsterVelocity;
        private bool _hasMonsterPosition;

        /// <summary>
        /// The world the ability asks about. Assign the map layer's probe on spawn; the
        /// same instance <c>MonsterBrain</c> uses, so the ears and the rules read one
        /// map. Until it is set, a fallback answers only the two questions
        /// <c>ListenerAbility</c> actually asks.
        /// </summary>
        public IWorldProbe? Probe
        {
            get { return _probe; }
            set
            {
                _probe = value;
                _ability = null;
            }
        }

        /// <summary>Whether the role is active on this player. §11 leaves one of the five out every match.</summary>
        public bool IsListener { get; set; } = true;

        /// <summary>True when <see cref="Reading"/> holds a fix from this tick.</summary>
        public bool HasReading => _ability != null && _ability.HasReading;

        /// <summary>The current fix. Undefined unless <see cref="HasReading"/>.</summary>
        public ListenerReading Reading => _ability != null ? _ability.Reading : default;

        /// <summary>Why there is no fix. The HUD renders this — §04's costs have to be legible at the moment they bite.</summary>
        public AbilityFailure Failure => _ability != null ? _ability.Failure : AbilityFailure.Inactive;

        /// <summary>True when the player is the reason they cannot hear. §04.</summary>
        public bool IsSelfBlinded => _ability != null && _ability.IsSelfBlinded;

        /// <summary>
        /// The floor the ability believes it heard, or <see cref="FloorMaterial.None"/>
        /// with no fix. §12's alphabet, as the player would say it out loud.
        /// </summary>
        public FloorMaterial HeardFloor => HasReading ? Reading.Floor : FloorMaterial.None;

        /// <summary>Clears the fix. Call on death, on a role swap, and on §03's surfacing reset.</summary>
        public void ResetAbility()
        {
            _ability?.Reset();
            _hasMonsterPosition = false;
            _monsterVelocity = Vector3.zero;
        }

        private void Awake()
        {
            if (danger == null)
            {
                danger = GetComponent<DangerSense>();
            }

            if (noise == null)
            {
                noise = GetComponent<NoiseMeter>();
            }
        }

        private void OnDisable()
        {
            ResetAbility();
        }

        private void Update()
        {
            TrackMonster(Time.deltaTime);

            if (!IsListener)
            {
                _ability?.Reset();
                return;
            }

            EnsureAbility();

            _accumulator += Time.deltaTime;

            // Bounded so a loading hitch cannot spend a hundred steps in one frame.
            // Losing steps is harmless here — §04 takes a fix twice a second, and the
            // ability starts every interval overdue so the next audible tick produces
            // one immediately.
            var maxSteps = 8;
            var steps = 0;

            while (_accumulator >= GameConstants.FixedStep && steps < maxSteps)
            {
                _accumulator -= GameConstants.FixedStep;
                steps++;
                StepAbility(GameConstants.FixedStep);
            }

            if (steps >= maxSteps)
            {
                _accumulator = 0f;
            }
        }

        private void EnsureAbility()
        {
            if (_ability != null)
            {
                return;
            }

            _ability = new ListenerAbility(_probe ?? FallbackProbe.Shared, new DeterministicRandom(randomSeed));
        }

        private void StepAbility(float deltaSeconds)
        {
            if (_ability == null)
            {
                return;
            }

            var selfNoise = noise != null ? noise.Noise01 : 0f;
            var here = ToVec3(transform.position);

            var monster = danger != null ? danger.Monster : null;
            if (monster == null)
            {
                // No monster in the scene. Feeding a silent observation rather than
                // skipping the tick keeps the failure reason honest: the HUD says the
                // monster is silent, which is exactly what "there is nothing to hear"
                // means to a player.
                _ability.Tick(deltaSeconds, here, selfNoise,
                    MonsterObservation.Standstill(here, Vec3.Zero, MonsterObservation.NoTarget));
                return;
            }

            var state = danger != null ? danger.MonsterState : MonsterStateId.Patrol;
            var position = ToVec3(monster.position);

            var observation = state == MonsterStateId.Standstill
                ? MonsterObservation.Standstill(position, ToVec3(monster.forward), MonsterObservation.NoTarget)
                : MonsterObservation.Moving(position, ToVec3(_monsterVelocity), MonsterObservation.NoTarget);

            _ability.Tick(deltaSeconds, here, selfNoise, observation);
        }

        private void TrackMonster(float deltaSeconds)
        {
            var monster = danger != null ? danger.Monster : null;
            if (monster == null)
            {
                _hasMonsterPosition = false;
                _monsterVelocity = Vector3.zero;
                return;
            }

            var position = monster.position;
            if (!_hasMonsterPosition || deltaSeconds <= 0f)
            {
                _lastMonsterPosition = position;
                _hasMonsterPosition = true;
                return;
            }

            var velocity = (position - _lastMonsterPosition) / deltaSeconds;
            _lastMonsterPosition = position;

            // Smoothed, because §04 reads 이동 방향 off this and a single frame of
            // network jitter would swing the called bearing further than the ability's
            // own 30° error budget.
            _monsterVelocity = Vector3.Lerp(_monsterVelocity, velocity, 0.25f);
        }

        private static Vec3 ToVec3(Vector3 v)
        {
            return new Vec3(v.x, v.y, v.z);
        }

        /// <summary>
        /// Answers the two questions <c>ListenerAbility</c> asks, and nothing more.
        /// <para>
        /// Deliberately incomplete. <c>ListenerAbility</c> never calls
        /// <c>HasLineOfSight</c> — §04 makes hearing through walls the whole point —
        /// and never asks for a path, so the navigation members here return the honest
        /// "I do not know" rather than a plausible guess that some future caller might
        /// believe. Anything that needs a real probe must be given the map layer's.
        /// </para>
        /// </summary>
        private sealed class FallbackProbe : IWorldProbe
        {
            internal static readonly FallbackProbe Shared = new FallbackProbe();

            public bool HasLineOfSight(Vec3 from, Vec3 to) => true;

            public float NavigableDistance(Vec3 from, Vec3 to) => float.PositiveInfinity;

            public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
            {
                next = to;
                return false;
            }

            public FloorMaterial SampleFloor(Vec3 position) =>
                FloorSurfaces.Sample(new Vector3(position.X, position.Y, position.Z));

            public int ZoneIdAt(Vec3 position) => -1;

            public Vec3 SnapToNavigable(Vec3 desired) => desired;

            public bool IsAreaLit(Vec3 position) => false;
        }
    }
}
