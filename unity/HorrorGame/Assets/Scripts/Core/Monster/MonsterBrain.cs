using System;
using System.Collections.Generic;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;
using HorrorGame.Core.Telemetry;

namespace HorrorGame.Core.Monster
{
    /// <summary>
    /// §06's monster: the state machine, the aggro rules and the perception that
    /// feeds them.
    /// <para>
    /// Everything physical is asked through <see cref="IWorldProbe"/>, and the
    /// monster walks with <see cref="IWorldProbe.TryGetNextPathPoint"/> rather than
    /// straight at its target. That is not tidiness — §12's entire argument is that
    /// path length and sight line diverge ("S자 통로 20 m = 4.2초 > 차단 3초"), and a
    /// monster that moved along the sight line would erase the gap the Runner
    /// escapes through, on every map, silently.
    /// </para>
    /// <para>
    /// The transitions are the §06 table and nothing more, with one exception that is
    /// written down rather than smuggled in. Where the table gives a state no edge —
    /// Search has no way back into Chase — this class has none either, and
    /// <c>MonsterTests</c> pins the consequence.
    /// </para>
    /// <para>
    /// The exception is 순찰's missing sight edge, which now exists inside
    /// <see cref="GameConstants.MonsterPatrolNoticeRange"/> — four metres — and nowhere
    /// beyond it. The table is right that a patrol must not acquire across a room; it
    /// simply did not consider a player standing in the creature's face, and under the
    /// literal reading one could, indefinitely, and be walked past. That was found by
    /// playing the game, not by reading it. Everything else about 순찰 is unchanged:
    /// beyond arm's length, aggro is still bought with noise.
    /// </para>
    /// </summary>
    public sealed class MonsterBrain
    {
        /// <summary><see cref="ChaseTargetId"/> when the monster is not chasing anybody.</summary>
        public const int NoTarget = -1;

        // Path following is bounded per sub-step so that a probe which keeps handing
        // back the point the monster already stands on cannot hang the host loop.
        private const int MaxPathPointsPerSubStep = 8;

        // A host that hitches longer than this is broken in a way the brain must not
        // paper over: catching up in one call is what teleporting looks like.
        private const int MaxSubStepsPerTick = 1024;

        // Bounded rejection sampling for a patrol or search destination. Failing all
        // of them leaves the monster standing, which is the honest outcome when the
        // probe says nothing nearby is navigable.
        private const int DestinationAttempts = 8;

        private readonly IWorldProbe _probe;
        private readonly IRandomSource _random;
        private readonly ITelemetrySink? _telemetry;

        // Zones the monster has stood in, oldest first. §07 expresses patrol scope
        // as a zone count, and the brain deliberately has no map: the first
        // PatrolZoneCount entries here are its licence to roam.
        private readonly List<int> _knownZoneIds = new List<int>();

        private MonsterStateId _state = MonsterStateId.Patrol;
        private float _stateElapsedSeconds;
        private Vec3 _position;
        private Vec3 _facing;
        private Vec3? _destination;
        private Vec3? _lastSeenPosition;
        private int _chaseTargetId = NoTarget;
        private float _lineOfSightBrokenSeconds;
        private float _secondsUntilStandstill;
        private float _stunSecondsRemaining;

        /// <summary>
        /// Creates a monster standing at <paramref name="spawnPosition"/>, snapped
        /// onto navigable ground, patrolling.
        /// </summary>
        /// <param name="probe">The only way this class may touch the world.</param>
        /// <param name="random">
        /// Seeded per §13 so a reported match replays: standstill timing and patrol
        /// destinations are the monster's only random choices, and both change where
        /// it is when a player walks in.
        /// </param>
        /// <param name="spawnPosition">Where the monster starts.</param>
        /// <param name="facing">Initial look direction; a degenerate value becomes +Z.</param>
        /// <param name="telemetry">
        /// Optional. §13 names "how long does aggro last" as a question worth
        /// measuring, and chase duration is only knowable in here.
        /// </param>
        public MonsterBrain(
            IWorldProbe probe,
            IRandomSource random,
            Vec3 spawnPosition,
            Vec3 facing,
            ITelemetrySink? telemetry = null)
        {
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _telemetry = telemetry;

            _position = _probe.SnapToNavigable(spawnPosition);
            var flat = facing.NormalizedFlat;
            _facing = flat.SqrMagnitude > 0f ? flat : Vec3.Forward;

            RememberCurrentZone();
            ScheduleNextStandstill();
        }

        /// <summary>Which row of §06's table the monster is currently on.</summary>
        public MonsterStateId State => _state;

        /// <summary>
        /// Seconds spent in <see cref="State"/>. This is the quantity §06's 3 s, 15 s
        /// and 5 s give-up rules are measured against, so it is exposed for the HUD
        /// debug overlay and for tests rather than recomputed anywhere else.
        /// </summary>
        public float StateElapsedSeconds => _stateElapsedSeconds;

        /// <summary>Where the monster is, in world metres.</summary>
        public Vec3 Position => _position;

        /// <summary>
        /// Flat unit vector the monster looks along. It is the direction of travel,
        /// and it gates vision — §04's 관측자 reads exactly this to answer "누가 표적인지".
        /// </summary>
        public Vec3 Facing => _facing;

        /// <summary>
        /// The point the monster is currently walking to, or null when it has none
        /// (standing still, or the probe reports nothing reachable).
        /// </summary>
        public Vec3? Destination => _destination;

        /// <summary>
        /// Where the monster last actually saw its target, or null if it never has.
        /// <para>
        /// §06 makes this load-bearing rather than cosmetic: on release the monster
        /// goes here and searches, so a Runner that breaks aggro next to the team has
        /// "괴물을 팀에 배달하는 것". The direction of the escape is the strategy, and
        /// this field is the whole reason it is one.
        /// </para>
        /// </summary>
        public Vec3? LastSeenPosition => _lastSeenPosition;

        /// <summary>Id of the chased target, or <see cref="NoTarget"/>. §06.</summary>
        public int ChaseTargetId => _chaseTargetId;

        /// <summary>
        /// How long the sight line to the chase target has been unbroken-for, in
        /// seconds. §06 requires 3 continuous seconds; regaining sight sets this back
        /// to zero, which is why hiding twice for 2 s each is worth nothing.
        /// </summary>
        public float LineOfSightBrokenSeconds => _lineOfSightBrokenSeconds;

        /// <summary>Seconds until the next standstill roll while patrolling. §06: 15~30 s.</summary>
        public float SecondsUntilStandstill => _secondsUntilStandstill;

        /// <summary>True while a flash (§04) is suspending the state machine.</summary>
        public bool IsStunned => _stunSecondsRemaining > 0f;

        /// <summary>Seconds of stun left. §04 / §16-3.</summary>
        public float StunSecondsRemaining => _stunSecondsRemaining;

        /// <summary>
        /// Whether the monster is making noise right now — the Listener's entire feed.
        /// <para>
        /// False in <see cref="MonsterStateId.Standstill"/> and true everywhere else,
        /// exactly as §06's 소리 column reads. This is the game's weapon: when it goes
        /// false the Listener loses the monster and the team asks "어디 갔어? 방금 여기
        /// 있었는데". Nothing else may decide the answer, or the silence stops being
        /// trustworthy information.
        /// </para>
        /// </summary>
        public bool IsAudible => _state != MonsterStateId.Standstill;

        /// <summary>
        /// True in Chase, where §06's 소리 column reads 발소리+포효. The roar is what
        /// tells a player without the Listener role that the aggro is theirs.
        /// </summary>
        public bool IsRoaring => _state == MonsterStateId.Chase;

        /// <summary>
        /// Zones the monster has been in, oldest first — its patrol route.
        /// <para>
        /// §07's scope is applied to the front of this list: the monster expands into
        /// new zones until the route holds <c>PatrolZoneCount</c> of them and then
        /// stays inside it. Exposed because the map validator and telemetry both want
        /// to know where a monster actually ranged, which no other system can see.
        /// </para>
        /// </summary>
        public IReadOnlyList<int> KnownZoneIds => _knownZoneIds;

        /// <summary>
        /// Steps with <see cref="MonsterTickInput.Default"/> — the 초저녁 threat row
        /// and nobody sensed. This is the <c>Tick(float)</c> shape ARCHITECTURE §3
        /// requires of every stateful core system; a host with a threat curve and
        /// players should call the overload.
        /// </summary>
        public void Tick(float deltaSeconds) => Tick(deltaSeconds, MonsterTickInput.Default);

        /// <summary>
        /// Advances the state machine by <paramref name="deltaSeconds"/>.
        /// <para>
        /// The tick is split into sub-steps of at most
        /// <see cref="GameConstants.FixedStep"/>. That is what makes a frame spike
        /// harmless in the two ways §06 and §12 care about: a 2 s hitch cannot step
        /// over a 3 s or 5 s give-up window without evaluating it, and the monster
        /// still walks its path one waypoint at a time instead of covering 9.6 m of
        /// chord straight through a corner.
        /// </para>
        /// <para>
        /// A non-positive or NaN delta runs a single zero-length sub-step: the
        /// instantaneous edges (a sound heard, a sight line gained) must still be able
        /// to fire on the frame they become true, and a paused host is allowed to tick.
        /// </para>
        /// </summary>
        public void Tick(float deltaSeconds, in MonsterTickInput input)
        {
            if (!(deltaSeconds > 0f))
            {
                Step(0f, input);
                return;
            }

            // Clamped before the cast: a delta large enough to overflow an int is a
            // broken host, and an overflowed sub-step count would be a teleport.
            var raw = MathF.Ceiling(deltaSeconds / GameConstants.FixedStep);
            var subSteps = raw >= MaxSubStepsPerTick ? MaxSubStepsPerTick : (int)raw;
            if (subSteps < 1)
            {
                subSteps = 1;
            }

            var dt = deltaSeconds / subSteps;
            for (var i = 0; i < subSteps; i++)
            {
                Step(dt, input);
            }
        }

        /// <summary>
        /// Suspends the state machine for <paramref name="seconds"/> — the Flasher's
        /// 기절 (§04), <see cref="GameConstants.FlashStunSeconds"/> long.
        /// <para>
        /// Suspend means suspend: state, its elapsed time, the aggro-release timer,
        /// perception and movement all freeze, and resume untouched. Freezing the
        /// release timer is the load-bearing part — if cover time accrued while the
        /// monster was blind, one flash would do the Runner's whole job, and §04 buys
        /// the Flasher seconds, not aggro. §15 also already discarded time-based
        /// release ("기다리는 것은 실력이 아니다").
        /// </para>
        /// <para>
        /// Overlapping flashes extend to the longer remaining time rather than
        /// stacking, so two Flashers cannot chain a monster indefinitely.
        /// </para>
        /// </summary>
        public void Stun(float seconds)
        {
            if (!(seconds > 0f))
            {
                return;
            }

            if (seconds > _stunSecondsRemaining)
            {
                _stunSecondsRemaining = seconds;
            }

            _telemetry?.Increment("monster.stun");
        }

        private void Step(float dt, in MonsterTickInput input)
        {
            if (_stunSecondsRemaining > 0f)
            {
                _stunSecondsRemaining -= dt;
                if (_stunSecondsRemaining < 0f)
                {
                    _stunSecondsRemaining = 0f;
                }

                return;
            }

            RememberCurrentZone();

            var speed = input.MoveSpeed > 0f ? input.MoveSpeed : 0f;
            var budget = speed * dt;

            // A transition returns immediately and moves on the next sub-step. At
            // FixedStep that costs at most 0.096 m of travel, which is cheaper than
            // any of the alternatives for making a transition and a move share a step.
            switch (_state)
            {
                case MonsterStateId.Patrol:
                    StepPatrol(dt, input, budget);
                    break;
                case MonsterStateId.Alert:
                    StepAlert(dt, input, budget);
                    break;
                case MonsterStateId.Chase:
                    StepChase(dt, input, budget);
                    break;
                case MonsterStateId.Search:
                    StepSearch(dt, input, budget);
                    break;
                case MonsterStateId.Standstill:
                    StepStandstill(dt, input);
                    break;
            }
        }

        // 순찰 | 경로 이동 | 발소리 | 소리 감지 → 경계   (§06)
        private void StepPatrol(float dt, in MonsterTickInput input, float budget)
        {
            // §06's table gives 순찰 no sight edge, and at range that is right — see
            // MonsterPatrolNoticeRange. This is the contact exception: something that
            // walks into the creature's face gets noticed, because the alternative is
            // measured absurdity rather than designed restraint. Sound still wins ties;
            // it is evaluated first below only because a cue is cheaper to test.
            var underfoot = FindVisibleTarget(input.Targets, GameConstants.MonsterPatrolNoticeRange);
            if (underfoot.HasValue)
            {
                EnterChase(underfoot.Value);
                return;
            }

            var heard = FindLoudestAudibleSound(input.Sounds);
            if (heard.HasValue)
            {
                // A noise landing on the same sub-step as a due standstill wins:
                // §06 lists 소리 감지 → 경계 as Patrol's only transition, and the
                // standstill is a choice the monster makes when nothing happens.
                EnterAlert(heard.Value);
                return;
            }

            _stateElapsedSeconds += dt;
            _secondsUntilStandstill -= dt;
            if (_secondsUntilStandstill <= 0f)
            {
                // §06: "순찰 중 랜덤하게(15~30초 간격) 정지를 넣는다." §07 then thins
                // the standstills out over the night, down to none at 새벽 — so the
                // schedule fires either way and the tier decides whether it lands.
                var takeIt = _random.Chance(input.StandstillChance);
                ScheduleNextStandstill();
                if (takeIt)
                {
                    EnterStandstill();
                    return;
                }
            }

            if (!_destination.HasValue || HasArrived(_destination.Value) || !IsReachable(_destination.Value))
            {
                _destination = ChoosePatrolPoint(input.PatrolZoneCount);
            }

            if (_destination.HasValue)
            {
                MoveAlongPath(_destination.Value, budget);
            }
        }

        // 경계 | 소리 방향으로 이동 | 발소리 | 시야 확보 → 추격 / 3초 무소득 → 순찰   (§06)
        private void StepAlert(float dt, in MonsterTickInput input, float budget)
        {
            // Sight is evaluated before the give-up timer, so a target that becomes
            // visible on the very sub-step Alert expires is chased. §06 lists 시야
            // 확보 first, and losing a player to a rounding order would be the worst
            // possible way to lose one.
            var seen = FindVisibleTarget(input.Targets);
            if (seen.HasValue)
            {
                EnterChase(seen.Value);
                return;
            }

            _stateElapsedSeconds += dt;
            if (_stateElapsedSeconds >= GameConstants.AlertGiveUpSeconds)
            {
                EnterPatrol();
                return;
            }

            // No re-targeting on a second noise: §06 gives 소리 감지 → 경계 to 순찰 and
            // 정지 only, so 경계 runs its 3 s out on the sound that started it.
            if (_destination.HasValue && IsReachable(_destination.Value))
            {
                MoveAlongPath(_destination.Value, budget);
            }
        }

        // 추격 | 직진 | 발소리+포효 | 해제 조건 충족 → 수색   (§06)
        private void StepChase(float dt, in MonsterTickInput input, float budget)
        {
            _stateElapsedSeconds += dt;

            var target = FindTarget(input.Targets, _chaseTargetId);
            if (target.HasValue && CanSee(target.Value))
            {
                // §06: the sight line has to stay broken for 3 continuous seconds,
                // so regaining it spends the cover from scratch. Two 2 s
                // hides are worth nothing, which is what forces §12's 연속 차단.
                _lastSeenPosition = target.Value.Position;
                _lineOfSightBrokenSeconds = 0f;
                _destination = target.Value.Position;
            }
            else
            {
                _lineOfSightBrokenSeconds += dt;
                _destination = _lastSeenPosition;
            }

            // §06: 거리 12m 이상 + 시야 차단 3초 유지 — both, together, and the
            // distance is the true separation measured flat and straight, because
            // that is what §06's own arithmetic measures (5.6 − 4.8 = 0.8 m/s over
            // 12 s = 9.6 m < 12 m). Using path distance here would hand the release
            // away for free the moment an S-corridor doubled it back on itself, and
            // the map rules in §12 exist precisely because 12 m must be expensive.
            //
            // A target the host has stopped reporting — dead and now a ghost (§09),
            // or disconnected — is infinitely far away by definition, so the cover
            // clause alone releases the monster instead of pinning it in Chase.
            var separation = target.HasValue
                ? Vec3.DistanceFlat(_position, target.Value.Position)
                : float.PositiveInfinity;

            if (_lineOfSightBrokenSeconds >= GameConstants.AggroReleaseLineOfSightBreak
                && separation >= GameConstants.AggroReleaseDistance)
            {
                EnterSearch();
                return;
            }

            if (_destination.HasValue)
            {
                MoveAlongPath(_destination.Value, budget);
            }
        }

        // 수색 | 마지막 위치 반경을 뒤짐 | 발소리 | 15초 무소득 → 순찰   (§06)
        private void StepSearch(float dt, in MonsterTickInput input, float budget)
        {
            _stateElapsedSeconds += dt;
            if (_stateElapsedSeconds >= GameConstants.SearchGiveUpSeconds)
            {
                EnterPatrol();
                return;
            }

            // §06 gives 수색 exactly one exit, 15초 무소득 → 순찰: no sight edge and no
            // sound edge. Pinned by MonsterTests, recorded as F-002 — a searching
            // monster walking past a lit player is the design's literal text, and
            // changing it is the designer's call.
            if (!_destination.HasValue || HasArrived(_destination.Value) || !IsReachable(_destination.Value))
            {
                _destination = ChooseSearchPoint();
            }

            if (_destination.HasValue)
            {
                MoveAlongPath(_destination.Value, budget);
            }
        }

        // 정지 | 멈춰서 듣는다 | 없음 | 소리 감지 → 경계 / 5초 후 → 순찰   (§06)
        private void StepStandstill(float dt, in MonsterTickInput input)
        {
            // 소리 감지 is listed first and wins a tie on the expiring sub-step: a
            // monster that hears something at 4.99 s should leave for the noise, not
            // resume its route.
            var heard = FindLoudestAudibleSound(input.Sounds);
            if (heard.HasValue)
            {
                EnterAlert(heard.Value);
                return;
            }

            _stateElapsedSeconds += dt;
            if (_stateElapsedSeconds >= GameConstants.StandstillSeconds)
            {
                EnterPatrol();
            }

            // No movement and no sound. That silence is the state's entire purpose.
        }

        private void EnterPatrol()
        {
            _state = MonsterStateId.Patrol;
            _stateElapsedSeconds = 0f;
            _destination = null;
            _chaseTargetId = NoTarget;
            _lineOfSightBrokenSeconds = 0f;

            // The standstill clock restarts with the route rather than carrying a
            // countdown across a chase, so a monster never gives up and freezes in
            // the same breath. _lastSeenPosition is deliberately kept: it is the
            // monster's memory, and §06 leans on it the next time aggro breaks.
            ScheduleNextStandstill();
            _telemetry?.Increment("monster.state.patrol");
        }

        private void EnterAlert(Vec3 soundPosition)
        {
            _state = MonsterStateId.Alert;
            _stateElapsedSeconds = 0f;
            _destination = _probe.SnapToNavigable(soundPosition);
            _telemetry?.Increment("monster.state.alert");
        }

        private void EnterChase(MonsterTarget target)
        {
            _state = MonsterStateId.Chase;
            _stateElapsedSeconds = 0f;
            _chaseTargetId = target.Id;
            _lastSeenPosition = target.Position;
            _lineOfSightBrokenSeconds = 0f;
            _destination = target.Position;
            _telemetry?.Increment("monster.state.chase");
        }

        private void EnterSearch()
        {
            // §13 asks how long aggro lasts; this is the only place that knows.
            _telemetry?.Observe("monster.aggro_seconds", _stateElapsedSeconds);

            _state = MonsterStateId.Search;
            _stateElapsedSeconds = 0f;
            _chaseTargetId = NoTarget;
            _lineOfSightBrokenSeconds = 0f;

            // Straight to the last seen position — not to wherever the target is now.
            _destination = _lastSeenPosition.HasValue
                ? _probe.SnapToNavigable(_lastSeenPosition.Value)
                : (Vec3?)null;
            _telemetry?.Increment("monster.state.search");
        }

        private void EnterStandstill()
        {
            _state = MonsterStateId.Standstill;
            _stateElapsedSeconds = 0f;
            _destination = null;
            _telemetry?.Increment("monster.state.standstill");
        }

        private void ScheduleNextStandstill() =>
            _secondsUntilStandstill = _random.NextFloat(
                GameConstants.StandstillIntervalMin, GameConstants.StandstillIntervalMax);

        private void RememberCurrentZone()
        {
            var zone = _probe.ZoneIdAt(_position);
            if (zone < 0)
            {
                return;
            }

            for (var i = 0; i < _knownZoneIds.Count; i++)
            {
                if (_knownZoneIds[i] == zone)
                {
                    return;
                }
            }

            _knownZoneIds.Add(zone);
        }

        private Vec3? ChoosePatrolPoint(int patrolZoneCount)
        {
            var currentZone = _probe.ZoneIdAt(_position);
            for (var attempt = 0; attempt < DestinationAttempts; attempt++)
            {
                // A leg is capped at a zone diagonal (§12: 30~40 m) so patrol reads
                // as covering a zone rather than crossing the map.
                var yaw = _random.NextFloat(-180f, 180f);
                var radius = _random.NextFloat(GameConstants.MonsterWaypointTolerance, GameConstants.ZoneDiagonalMax);
                var candidate = _probe.SnapToNavigable(_position + (MathX.DirectionFromYaw(yaw) * radius));

                if (HasArrived(candidate) || !IsReachable(candidate))
                {
                    continue;
                }

                if (!IsPatrolZoneAllowed(_probe.ZoneIdAt(candidate), patrolZoneCount, currentZone))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private bool IsPatrolZoneAllowed(int zoneId, int patrolZoneCount, int currentZoneId)
        {
            // An unzoned point (probe returns -1) carries no scope to enforce, and
            // the zone the monster is standing in is always allowed — otherwise a
            // chase that dragged it out of scope would leave it unable to walk at all.
            if (zoneId < 0 || zoneId == currentZoneId)
            {
                return true;
            }

            var allowed = patrolZoneCount < 1 ? 1 : patrolZoneCount;
            for (var i = 0; i < _knownZoneIds.Count && i < allowed; i++)
            {
                if (_knownZoneIds[i] == zoneId)
                {
                    return true;
                }
            }

            // §07 widens the scope over the night (1개 구역 → 2개 구역 → 절반 → 전체),
            // and the brain has no map to widen it against — so a zone it has never
            // entered is accepted only while its route still holds fewer than
            // PatrolZoneCount zones. The route grows one zone at a time as the
            // monster actually walks into them, and stops growing when it is full.
            return _knownZoneIds.Count < allowed;
        }

        private Vec3? ChooseSearchPoint()
        {
            var centre = _lastSeenPosition ?? _position;
            for (var attempt = 0; attempt < DestinationAttempts; attempt++)
            {
                // §06: "마지막 위치 반경을 뒤짐" — the sweep stays inside SearchRadius
                // of the last sighting, which is what makes running past the monster
                // and doubling back a real option.
                var yaw = _random.NextFloat(-180f, 180f);
                var radius = _random.NextFloat(GameConstants.MonsterWaypointTolerance, GameConstants.SearchRadius);
                var candidate = _probe.SnapToNavigable(centre + (MathX.DirectionFromYaw(yaw) * radius));

                if (HasArrived(candidate) || !IsReachable(candidate))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private void MoveAlongPath(Vec3 destination, float budget)
        {
            for (var i = 0; i < MaxPathPointsPerSubStep && budget > 0f; i++)
            {
                if (HasArrived(destination))
                {
                    return;
                }

                // The only way the monster ever moves. Nothing here interpolates
                // toward the destination itself, so a corner is walked around and a
                // large budget is spent one path segment at a time.
                if (!_probe.TryGetNextPathPoint(_position, destination, out var next))
                {
                    return;
                }

                var toNext = next - _position;
                var distance = toNext.Magnitude;
                if (distance <= GameConstants.MonsterWaypointTolerance)
                {
                    _position = next;
                    continue;
                }

                Face(toNext);

                if (distance > budget)
                {
                    _position += toNext / distance * budget;
                    return;
                }

                _position = next;
                budget -= distance;
            }
        }

        private void Face(Vec3 direction)
        {
            var flat = direction.NormalizedFlat;
            if (flat.SqrMagnitude > 0f)
            {
                _facing = flat;
            }
        }

        private bool HasArrived(Vec3 point) =>
            Vec3.DistanceFlat(_position, point) <= GameConstants.MonsterWaypointTolerance;

        private bool IsReachable(Vec3 point)
        {
            var d = _probe.NavigableDistance(_position, point);
            return !float.IsNaN(d) && d < float.PositiveInfinity;
        }

        private bool CanSee(MonsterTarget target) =>
            CanSee(target, GameConstants.MonsterSightRange);

        private bool CanSee(MonsterTarget target, float rangeMetres)
        {
            if (target.IsConcealed)
            {
                return false;
            }

            var to = (target.Position - _position).Flat;
            if (to.MagnitudeFlat > rangeMetres)
            {
                return false;
            }

            if (to.SqrMagnitudeFlat > 0f
                && MathX.AngleBetween(_facing, to) > GameConstants.MonsterSightHalfAngle)
            {
                return false;
            }

            return _probe.HasLineOfSight(_position, target.Position);
        }

        private MonsterTarget? FindVisibleTarget(IReadOnlyList<MonsterTarget>? targets) =>
            FindVisibleTarget(targets, GameConstants.MonsterSightRange);

        private MonsterTarget? FindVisibleTarget(IReadOnlyList<MonsterTarget>? targets, float rangeMetres)
        {
            if (targets == null)
            {
                return null;
            }

            MonsterTarget? best = null;
            var bestScore = float.PositiveInfinity;

            for (var i = 0; i < targets.Count; i++)
            {
                var candidate = targets[i];
                if (!CanSee(candidate, rangeMetres))
                {
                    continue;
                }

                // Ranked by walking distance, because the nearest target down a
                // corridor is the one the monster can actually reach. A target that
                // is visible but unreachable (across a stairwell void) is ranked
                // behind every reachable one, yet still chaseable when it is alone.
                var path = _probe.NavigableDistance(_position, candidate.Position);
                var score = !float.IsNaN(path) && path < float.PositiveInfinity
                    ? path
                    : GameConstants.MapExtent + Vec3.DistanceFlat(_position, candidate.Position);

                if (best == null
                    || score < bestScore
                    || (MathX.Approximately(score, bestScore) && candidate.Id < best.Value.Id))
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private static MonsterTarget? FindTarget(IReadOnlyList<MonsterTarget>? targets, int id)
        {
            if (targets == null || id == NoTarget)
            {
                return null;
            }

            for (var i = 0; i < targets.Count; i++)
            {
                if (targets[i].Id == id)
                {
                    return targets[i];
                }
            }

            return null;
        }

        private Vec3? FindLoudestAudibleSound(IReadOnlyList<MonsterSoundCue>? sounds)
        {
            if (sounds == null)
            {
                return null;
            }

            Vec3? best = null;
            var bestLoudness = float.NegativeInfinity;
            var bestDistance = float.PositiveInfinity;

            for (var i = 0; i < sounds.Count; i++)
            {
                var cue = sounds[i];
                if (!(cue.RangeMetres > 0f))
                {
                    continue;
                }

                // Walked distance, not through-the-wall distance: the monster must be
                // pulled the way §12's geometry says it has to travel. An unreachable
                // noise is +∞ and therefore inaudible, which is also the right answer
                // for a probe that reports nothing reachable at all.
                var distance = _probe.NavigableDistance(_position, cue.Position);
                if (float.IsNaN(distance) || distance > cue.RangeMetres)
                {
                    continue;
                }

                if (best == null
                    || cue.Loudness > bestLoudness
                    || (MathX.Approximately(cue.Loudness, bestLoudness) && distance < bestDistance))
                {
                    best = cue.Position;
                    bestLoudness = cue.Loudness;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
