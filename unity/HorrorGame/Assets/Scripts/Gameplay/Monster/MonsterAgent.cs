#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Monster;
using HorrorGame.Core.Session;
using HorrorGame.Core.Telemetry;
using HorrorGame.Core.Threat;
using UnityEngine;
using UnityEngine.AI;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// Drives §06's monster in Unity: steps <see cref="MonsterBrain"/> at
    /// <see cref="GameConstants.FixedStep"/> and applies the result to the transform,
    /// the <see cref="NavMeshAgent"/>, the animator and the audio sources.
    /// <para>
    /// <b>The brain is authoritative and this class decides nothing.</b> Every
    /// transition, timer and aggro rule in §06 already lives in Core, where it is
    /// tested without an engine. If Unity and the brain ever disagree about what the
    /// monster is doing, the brain is right and this adapter has a bug — so nothing
    /// here re-derives a state, and the state machine is never consulted for anything
    /// except which clip to play and which sound to make.
    /// </para>
    /// <para>
    /// It also does not steer. <see cref="NavMeshAgent"/> runs with
    /// <see cref="NavMeshAgent.updatePosition"/> and
    /// <see cref="NavMeshAgent.updateRotation"/> off, and the position comes from the
    /// brain walking its own path through <see cref="NavMeshWorldProbe"/>. Letting the
    /// agent steer would put §12's cornering behaviour under Unity's local avoidance
    /// instead of under the rules the balance sweeps measure, and the two do not
    /// produce the same monster.
    /// </para>
    /// <para>
    /// Host authority (§13): this component simulates. On a client it must be disabled
    /// and the transform driven by the network layer instead — a client that ran its
    /// own brain would need the true positions of every player, which is the thing
    /// §13 refuses to send.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public sealed class MonsterAgent : MonoBehaviour
    {
        /// <summary>
        /// Whole steps one <see cref="Simulate"/> call may take. Mirrors
        /// <c>MonsterBrain</c>'s own sub-step cap and exists for the same reason: a
        /// host that hitches must not let the monster catch up in one frame, because
        /// catching up in one frame is what teleporting through a wall looks like.
        /// </summary>
        private const int MaxStepsPerSimulate = 8;

        [Header("Rig")]
        [SerializeField]
        [Tooltip("Left empty, the NavMeshAgent on this object is used.")]
        private NavMeshAgent? _navAgent;

        [SerializeField]
        [Tooltip("Bone or empty transform sight is cast from. Falls back to the top of the agent capsule.")]
        private Transform? _sightOrigin;

        [Header("Perception")]
        [SerializeField]
        [Tooltip("Layers that block sight (§06 시야). Doors and level geometry — not characters.")]
        private LayerMask _sightBlockers = ~0;

        [SerializeField]
        [Tooltip("Layers the floor-material raycast may hit, for §12's per-zone surfaces.")]
        private LayerMask _floorLayers = ~0;

        [SerializeField]
        [Tooltip("Height above a reported target position that sight aims at. Rig geometry, not a §-number.")]
        private float _targetSightHeight = 1f;

        [Header("Presentation")]
        [SerializeField]
        private MonsterAnimationDriver? _animation;

        [SerializeField]
        private MonsterAudioDriver? _audio;

        [SerializeField]
        private MonsterFootsteps? _footsteps;

        [Header("Simulation")]
        [SerializeField]
        [Tooltip("Steps itself from FixedUpdate. Turn off when a host loop calls Simulate().")]
        private bool _selfDriven = true;

        [SerializeField]
        [Tooltip("Seed used when nobody calls Initialize. §13: a match replays from its seed.")]
        private int _fallbackSeed;

        private readonly List<MonsterTarget> _targets = new List<MonsterTarget>();
        private readonly List<MonsterSoundCue> _sounds = new List<MonsterSoundCue>();

        private MonsterBrain? _brain;
        private NavMeshWorldProbe? _probe;
        private ThreatTier _tier = ThreatCurve.At(0f);

        // Both accumulators are double for the reason MatchClock gives: a 35-minute
        // match is 105,000 additions of FixedStep, and in float those drift by the
        // better part of a second — which would move §07's tier boundaries and change
        // how many steps a given wall-clock second buys.
        private double _accumulator;
        private double _matchElapsedSeconds;
        private bool _clockDriven;
        private int _mapZoneCount;
        private bool _warnedOffNavMesh;

        /// <summary>The brain being driven, or null before <see cref="Initialize"/>.</summary>
        public MonsterBrain? Brain => _brain;

        /// <summary>The probe answering §06's perception, or null before <see cref="Initialize"/>.</summary>
        public NavMeshWorldProbe? Probe => _probe;

        /// <summary>§06's current row. <see cref="MonsterStateId.Patrol"/> before the brain exists.</summary>
        public MonsterStateId State => _brain != null ? _brain.State : MonsterStateId.Patrol;

        /// <summary>§07's row for the current match time — speed, patrol scope, standstill chance.</summary>
        public ThreatTier Tier => _tier;

        /// <summary>
        /// Whether the monster is making noise, straight from the brain. The audio and
        /// footstep drivers read this and nothing else, so §06's silent 정지 has
        /// exactly one authority.
        /// </summary>
        public bool IsAudible => _brain != null && _brain.IsAudible;

        /// <summary>Targets the host reported this tick. Read-only; the debug view draws them.</summary>
        public IReadOnlyList<MonsterTarget> Targets => _targets;

        /// <summary>Seconds since the match began, as last told by the host. §07.</summary>
        public float MatchElapsedSeconds => (float)_matchElapsedSeconds;

        /// <summary>Steps itself from <c>FixedUpdate</c> when true; a host loop calls <see cref="Simulate"/> when false.</summary>
        public bool SelfDriven
        {
            get => _selfDriven;
            set => _selfDriven = value;
        }

        /// <summary>
        /// Builds the brain and the probe and places the monster.
        /// </summary>
        /// <param name="random">
        /// The host's seeded stream (§13). Passed in rather than created here so a
        /// reported match replays: standstill timing and patrol destinations decide
        /// where the monster is standing when a player walks in.
        /// </param>
        /// <param name="map">§12's zone table, or null when the scene has no graph yet.</param>
        /// <param name="telemetry">Optional §13 sink; the brain reports chase duration into it.</param>
        public void Initialize(IRandomSource random, MapGraph? map = null, ITelemetrySink? telemetry = null)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var agent = ResolveAgent();

            // Everything the probe needs is rig geometry the prefab already carries.
            // Reading it off the agent rather than serialising a second copy means a
            // re-imported Monster.fbx cannot leave the sight ray at the old height.
            var eyeHeight = _sightOrigin != null
                ? Mathf.Max(0f, _sightOrigin.position.y - transform.position.y)
                : agent.height;

            _probe = new NavMeshWorldProbe(
                eyeHeight,
                _targetSightHeight,
                agent.radius,
                agent.height,
                _sightBlockers.value,
                _floorLayers.value,
                NavMesh.AllAreas);

            _probe.Map = map;
            _mapZoneCount = map != null ? map.Zones.Length : 0;

            // The agent is a navigation service, not a driver. Turning these off is
            // what leaves the brain in charge of where the monster is.
            agent.updatePosition = false;
            agent.updateRotation = false;
            if (agent.isOnNavMesh)
            {
                // Both setters log an error when the agent is not placed on a mesh, and
                // an unbaked test scene is a warning-worthy state, not a spam-worthy one.
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }

            var spawn = transform.position;
            if (!_probe.IsOnNavMesh(spawn))
            {
                Debug.LogWarning(
                    $"[{name}] spawn {spawn} is not on a NavMesh. §06's monster walks only through "
                    + "NavMesh.CalculatePath, so it will stand still until the surface is baked.",
                    this);
            }

            _brain = new MonsterBrain(_probe, random, spawn.ToVec3(), transform.forward.ToVec3(), telemetry);
            _accumulator = 0f;

            if (agent.isOnNavMesh)
            {
                agent.Warp(_brain.Position.ToVector3());
            }

            ApplyToTransform();

            if (_animation != null)
            {
                _animation.Apply(_brain.State, _brain.IsStunned, _tier.MonsterSpeed);
            }

            if (_audio != null)
            {
                _audio.OnStateEntered(_brain.State);
            }
        }

        /// <summary>
        /// Convenience overload seeding a <see cref="DeterministicRandom"/>. Prefer the
        /// <see cref="IRandomSource"/> overload from the host, which owns the match seed.
        /// </summary>
        public void Initialize(int seed) => Initialize(new DeterministicRandom(seed));

        /// <summary>
        /// Rebuilds the brain at a new spawn — §03's partial reset, where surfacing
        /// clears the monster's chase and position but not the clock.
        /// <para>
        /// The random stream continues rather than restarting, because §13's replay
        /// guarantee is about the whole match, not about each descent.
        /// </para>
        /// </summary>
        public void Respawn(Vector3 position, Vector3 facing, IRandomSource random, ITelemetrySink? telemetry = null)
        {
            transform.position = position;
            if (facing.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }

            var map = _probe != null ? _probe.Map : null;
            Initialize(random, map, telemetry);
        }

        /// <summary>
        /// Tells the monster what time it is, which is the only thing §07 changes about
        /// it: speed, patrol scope and how often it goes silent. The host's
        /// <c>MatchClock</c> owns this value; calling it once switches off the local
        /// stand-in clock for good.
        /// </summary>
        public void SetMatchElapsedSeconds(float seconds)
        {
            _clockDriven = true;
            _matchElapsedSeconds = seconds > 0f ? seconds : 0f;
        }

        /// <summary>
        /// How many zones the loaded map has, for §07's 순찰 column. Zero means unknown,
        /// and the tier then answers for the largest legal map — an upper bound, which
        /// is the safe direction to be wrong in only because the brain can never patrol
        /// a zone it has not walked into.
        /// </summary>
        public void SetMapZoneCount(int zoneCount)
        {
            _mapZoneCount = zoneCount > 0 ? zoneCount : 0;
        }

        /// <summary>
        /// Reports where a player is this tick, replacing any earlier reading for the
        /// same id. Positions are the true ones — §06's perception rules decide what
        /// the monster can actually see, and they are in Core.
        /// </summary>
        /// <param name="isConcealed">Inside a locker or under a desk: unseeable however clear the line is.</param>
        public void ReportTarget(int id, Vector3 position, bool isConcealed = false)
        {
            var reading = new MonsterTarget(id, position.ToVec3(), isConcealed);
            for (var i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].Id == id)
                {
                    _targets[i] = reading;
                    return;
                }
            }

            _targets.Add(reading);
        }

        /// <summary>
        /// Stops reporting a target — death (§09 turns them into a ghost) or a
        /// disconnect. The brain reads an absent chase target as infinitely far away,
        /// so aggro releases on the cover clause alone instead of pinning the monster
        /// to a corpse.
        /// </summary>
        public void ForgetTarget(int id)
        {
            for (var i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].Id == id)
                {
                    _targets.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>Drops every target reading. Used when the team surfaces (§03).</summary>
        public void ClearTargets() => _targets.Clear();

        /// <summary>
        /// Reports a noise for this tick — a footstep, a door, the Engineer's 소음 함정,
        /// a 조명탄 (§08). The cue carries its own range because those do not carry
        /// equally far, and it is consumed by the next step: the brain never buffers
        /// noise, so a host that drops a tick drops the sound with it.
        /// </summary>
        public void ReportSound(Vector3 position, float rangeMetres, float loudness = 1f)
        {
            _sounds.Add(new MonsterSoundCue(position.ToVec3(), rangeMetres, loudness));
        }

        /// <summary>
        /// The Flasher's 기절 (§04). Suspends the state machine — including the aggro
        /// release timer, so a flash buys seconds and never buys the escape itself.
        /// </summary>
        public void Stun(float seconds)
        {
            if (_brain == null || !(seconds > 0f))
            {
                return;
            }

            var wasStunned = _brain.IsStunned;
            _brain.Stun(seconds);

            // Only the flash that lands announces itself. A second Flasher extending an
            // existing stun (§04 — they extend, they do not stack) must not re-trigger
            // the cue, or two Flashers would sound like two monsters.
            if (!wasStunned && _audio != null)
            {
                _audio.OnStunned();
            }
        }

        /// <summary>
        /// Plays the kill. Presentation only, and deliberately so: §06 gives the state
        /// machine five states and a catch is not one of them, so whether a player has
        /// been caught is the host's rule to make and this is only how it looks.
        /// </summary>
        public void PlayGrab()
        {
            if (_animation != null)
            {
                _animation.PlayGrab();
            }

            if (_audio != null)
            {
                _audio.OnGrab();
            }
        }

        /// <summary>
        /// Advances the monster by <paramref name="deltaSeconds"/> of real time in whole
        /// <see cref="GameConstants.FixedStep"/> steps.
        /// <para>
        /// Whole steps, not the frame's delta: ARCHITECTURE §3 has the host drive core
        /// systems at a fixed rate, and it is the reason a 30 fps machine and a 144 fps
        /// machine produce the same match from the same seed. Public so a host loop can
        /// drive several systems in a defined order.
        /// </para>
        /// </summary>
        public void Simulate(float deltaSeconds)
        {
            if (_brain == null)
            {
                return;
            }

            if (deltaSeconds > 0f)
            {
                _accumulator += deltaSeconds;
            }

            var steps = 0;
            while (_accumulator >= GameConstants.FixedStep && steps < MaxStepsPerSimulate)
            {
                Step();
                _accumulator -= GameConstants.FixedStep;
                steps++;
            }

            if (steps >= MaxStepsPerSimulate)
            {
                // Drop the backlog rather than run it. See MaxStepsPerSimulate.
                _accumulator = 0f;
            }

            ApplyToTransform();
        }

        private void Awake()
        {
            ResolveAgent();
            if (_animation == null)
            {
                _animation = GetComponentInChildren<MonsterAnimationDriver>();
            }

            if (_audio == null)
            {
                _audio = GetComponentInChildren<MonsterAudioDriver>();
            }

            if (_footsteps == null)
            {
                _footsteps = GetComponentInChildren<MonsterFootsteps>();
            }
        }

        private void Start()
        {
            if (_brain == null)
            {
                // A designer dropping the prefab into a scene gets a working monster.
                // A match overrides this from the host's seed before it starts.
                Initialize(_fallbackSeed);
            }
        }

        private void FixedUpdate()
        {
            if (_selfDriven)
            {
                Simulate(Time.fixedDeltaTime);
            }
        }

        private void Step()
        {
            var brain = _brain;
            var probe = _probe;
            if (brain == null || probe == null)
            {
                return;
            }

            if (!_clockDriven)
            {
                // Stand-in clock so the prefab escalates through §07 on its own in a
                // test scene. The host's MatchClock replaces it the moment it speaks.
                _matchElapsedSeconds += GameConstants.FixedStep;
            }

            _tier = ThreatCurve.At((float)_matchElapsedSeconds);

            // §07 is the only thing that sets the monster's speed, and it is read every
            // step rather than cached: the 초저녁 4.4 and the 동트기 전 5.2 are different
            // creatures, and a constant here would freeze the night at one of them.
            var speed = _tier.MonsterSpeed;
            var patrolZones = _mapZoneCount > 0
                ? _tier.PatrolZoneCountFor(_mapZoneCount)
                : _tier.PatrolZoneCount;

            var input = new MonsterTickInput(speed, patrolZones, _tier.StandstillChance, _targets, _sounds);

            var previousState = brain.State;
            var previousPosition = brain.Position;

            probe.ClearCache();
            brain.Tick(GameConstants.FixedStep, input);

            // Cues live for exactly the tick they were handed to (MonsterSoundCue).
            _sounds.Clear();

            var state = brain.State;
            if (state != previousState && _audio != null)
            {
                _audio.OnStateEntered(state);
            }

            if (_animation != null)
            {
                _animation.Apply(state, brain.IsStunned, speed);
            }

            if (_footsteps != null)
            {
                // The surface under the MONSTER, not under the listener: §12 gives each
                // zone its own floor so that 청음사 can tell where the monster is from
                // the sound alone, and that only works if the clip follows the creature.
                var travelled = Vec3.Distance(previousPosition, brain.Position);
                _footsteps.Advance(travelled, probe.SampleFloor(brain.Position), brain.IsAudible);
            }

            // The agent never steers, but anything reading it — a designer in the
            // inspector, a debug overlay, an avoidance query — should see §07's speed
            // rather than the prefab's authored default.
            var agent = _navAgent;
            if (agent != null)
            {
                agent.speed = speed;
            }
        }

        private void ApplyToTransform()
        {
            var brain = _brain;
            if (brain == null)
            {
                return;
            }

            var position = brain.Position.ToVector3();
            var facing = brain.Facing.ToVector3();

            transform.position = position;
            if (facing.sqrMagnitude > 0f)
            {
                // No smoothing. §04's 관측자 reads the monster's facing to answer "누가
                // 표적인지", and MonsterBrain.Facing is what gates its vision — a
                // rendered heading that lags the one the rules use would make the
                // Observer's information quietly wrong.
                transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }

            var agent = _navAgent;
            if (agent != null && agent.enabled)
            {
                if (agent.isOnNavMesh)
                {
                    // Keeps the agent's internal location following the brain, so that
                    // NavMesh queries made through it stay valid. updatePosition is off,
                    // so this moves the agent and not the transform.
                    agent.nextPosition = position;
                    _warnedOffNavMesh = false;
                }
                else if (!_warnedOffNavMesh)
                {
                    _warnedOffNavMesh = true;
                    Debug.LogWarning($"[{name}] NavMeshAgent is off the NavMesh at {position}.", this);
                }
            }
        }

        private NavMeshAgent ResolveAgent()
        {
            if (_navAgent == null)
            {
                _navAgent = GetComponent<NavMeshAgent>();
            }

            return _navAgent!;
        }
    }
}
