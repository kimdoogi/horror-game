using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Abilities;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Map;
using HorrorGame.Core.Match;
using HorrorGame.Core.Math;
using HorrorGame.Core.Monster;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Session;
using HorrorGame.Core.Telemetry;
using HorrorGame.Core.Threat;

namespace HorrorGame.Sim
{
    /// <summary>
    /// One headless match, stepped at <see cref="GameConstants.FixedStep"/>.
    /// <para>
    /// Everything that decides anything is a Core object this class steps:
    /// <see cref="MatchClock"/> for §07's curve, <see cref="MonsterBrain"/> for §06's
    /// state machine, <see cref="ObjectiveResolver"/> and <see cref="ClueChain"/> for
    /// §03, <see cref="Inventory"/> and <see cref="Shop"/> for §08,
    /// <see cref="SpeedResolver"/> for §05, <see cref="OutcomeEvaluator"/> for §02.
    /// This file contributes a policy — where four agents choose to walk — and
    /// nothing else. The one exception is documented on
    /// <see cref="BalanceOverrides"/>, which shadows a single §08 constant so that a
    /// sweep is possible at all.
    /// </para>
    /// <para>
    /// <b>Host authority (§13).</b> The simulator is the host. The objective's
    /// position is taken once through <see cref="ObjectiveResolver.TryPlaceObjective"/>
    /// and kept in a private field the agents never read. They walk to a site because
    /// <see cref="ClueChain.Narrow"/> pinned it from what they believe they read, and
    /// the host answers <see cref="ObjectiveResolver.IsObjectiveSite"/> when they
    /// arrive. A misread therefore costs a wasted descent, exactly as §03 says it
    /// should.
    /// </para>
    /// <para>
    /// <b>What the agents do not know.</b> §03 randomises clue positions, clue
    /// contents and loot placement per match, and fixes the map. So the agents know
    /// the building — which nodes are 후보 지점 and which are dead ends, both of which
    /// §12 makes structural — and know nothing about what is on them until somebody
    /// walks up and looks. That gap is where §01's minutes go.
    /// </para>
    /// </summary>
    public sealed class MatchSimulator
    {
        // §07's table is five tiers of ThreatTierSeconds. A match still running when
        // the whole table has been spent is not a match any more, so the simulator
        // stops it and files it as abandoned rather than letting a policy bug run
        // forever. §01 wants 25~35 minutes; this sits well past 새벽.
        private static readonly float TimeCapSeconds =
            GameConstants.ThreatTierSeconds * GameConstants.ThreatTierCount;

        private readonly SimMap _map;
        private readonly MatchWorld _world;
        private readonly BalanceOverrides _overrides;
        private readonly DeterministicRandom _rng;

        private readonly MatchState _match;
        private readonly MatchClock _clock;
        private readonly MatchRecorder _recorder;
        private readonly Wallet _wallet;
        private readonly Shop _shop;
        private readonly ObjectiveResolver _resolver;
        private readonly SimAgent[] _agents;
        private readonly DroppedLootField _dropped = new DroppedLootField();

        private readonly LootId[] _lootAt;
        private readonly LootSafe?[] _safeAt;
        private readonly bool[] _lootInspected;
        private readonly bool[] _siteInspected;
        private readonly bool[] _clueFound;
        private readonly bool[] _clueRead;
        private readonly int[] _siteOfNode;
        private readonly List<MonsterTarget> _targets = new List<MonsterTarget>();
        private readonly List<MonsterSoundCue> _sounds = new List<MonsterSoundCue>();
        private readonly RoleId[] _roster;

        private MonsterBrain _monster;
        private ClueBelief _belief = ClueBelief.Empty;
        private Vec3 _objectivePosition;
        private int _pinnedSiteId = -1;
        private int _carrierIndex = -1;
        private int _descentIndex;
        private int _surfacingsSeen;
        private int _misreadsSeen;
        private bool _evacuating;
        private bool _objectiveTaken;
        private bool _chaseWasActive;
        private int _chaseTargetAtStart = -1;
        private bool _chaseTargetLoadedAtStart;

        private SimMatchResult _result;

        /// <summary>
        /// Lays out a match. Nothing is stepped until <see cref="Run"/>.
        /// </summary>
        /// <param name="map">The fixed building. §03 puts 맵 구조 in the 고정 column.</param>
        /// <param name="seed">The only source of variation. The same seed replays the match exactly.</param>
        /// <param name="overrides">The sweep's shadowed constants, or <see cref="BalanceOverrides.Default"/>.</param>
        /// <param name="sink">§13's telemetry sink. Receives one <see cref="MatchSummary"/>.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <param name="startSeconds">
        /// Seconds to advance §07's clock before the first descent, so a scenario can
        /// begin at 심야 instead of 초저녁. Zero is a normal match. This is a
        /// designer's what-if, not a balance value: it exists because §06's numbers
        /// only start to differ once the monster is at 4.8, and a match that ends in
        /// 초저녁 cannot answer a question about 심야.
        /// </param>
        public MatchSimulator(
            SimMap map, int seed, BalanceOverrides overrides, ITelemetrySink sink, float startSeconds = 0f)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            _world = new MatchWorld(map.World, map.Graph);
            _overrides = overrides;
            _rng = new DeterministicRandom(seed);

            var lineups = RoleSelection.AllLineups();
            var roles = lineups[_rng.NextInt(0, lineups.Count)];

            _match = new MatchState(roles);
            _clock = new MatchClock();
            _wallet = new Wallet();
            _shop = new Shop(_wallet);

            _recorder = new MatchRecorder(sink, seed, TelemetryPrivacy.NewSessionId(_rng), map.Graph.Name);
            _recorder.SetRoster(roles.RoleOf(0), roles.RoleOf(1), roles.RoleOf(2), roles.RoleOf(3));

            _roster = new RoleId[GameConstants.PlayersPerMatch];
            _agents = new SimAgent[GameConstants.PlayersPerMatch];
            for (var i = 0; i < _agents.Length; i++)
            {
                _roster[i] = roles.RoleOf(i);
                _agents[i] = new SimAgent(i, roles.RoleOf(i), _match.PlayerAt(i), map, _world, _rng);
            }

            _resolver = new ObjectiveResolver(map.Catalog, _world, map.PositionOf(map.EntranceNode), _rng);

            _objectivePosition = map.PositionOf(map.EntranceNode);
            _resolver.TryPlaceObjective(where => _objectivePosition = where);

            _clueRead = new bool[_resolver.ClueCount];
            _clueFound = new bool[_resolver.ClueCount];

            _siteInspected = new bool[map.Catalog.Sites.Count];
            _siteOfNode = new int[map.Graph.Nodes.Length];
            for (var i = 0; i < _siteOfNode.Length; i++)
            {
                _siteOfNode[i] = -1;
            }

            for (var i = 0; i < map.Catalog.Sites.Count; i++)
            {
                _siteOfNode[map.NodeOfSite(map.Catalog.Sites[i].SiteId)] = map.Catalog.Sites[i].SiteId;
            }

            _lootAt = new LootId[map.LootNodes.Length];
            _safeAt = new LootSafe?[map.LootNodes.Length];
            _lootInspected = new bool[map.LootNodes.Length];
            PlaceLoot();

            _monster = NewMonster();

            if (startSeconds > 0f)
            {
                _clock.Tick(startSeconds);
            }

            _result = default(SimMatchResult);
            _result.Seed = seed;
            _result.PlannedRoundTrips = _resolver.PlannedRoundTrips;
            _result.FirstUpgradeSeconds = -1f;
            _result.FirstUpgradeDescent = -1;
        }

        /// <summary>Runs the match to §02's verdict, or to §07's table running out.</summary>
        public SimMatchResult Run()
        {
            var dt = GameConstants.FixedStep;

            while (true)
            {
                _clock.Tick(dt);
                _recorder.Tick(dt);
                _match.Tick(dt);

                if (_clock.SurfacingCount > _surfacingsSeen)
                {
                    _surfacingsSeen = _clock.SurfacingCount;
                    _recorder.RecordRoundTrip();
                }

                if (_clock.ConsumeMonsterReset())
                {
                    // §03's 부분 리셋: the monster's chase state, position and aggro
                    // go; the clock does not.
                    _monster = NewMonster();
                    foreach (var agent in _agents)
                    {
                        agent.Listener?.Reset();
                        agent.Observer?.Reset();
                        agent.Runner?.Reset();
                    }
                }

                StepMonster(dt);
                StepAgents(dt);
                TeamSurfacePhase();
                SettleTeamPosition();

                if (_match.Resolution.IsFinal)
                {
                    break;
                }

                if (_clock.ElapsedSeconds >= TimeCapSeconds)
                {
                    _result.HitTimeCap = true;
                    _match.TryAbandon();
                    break;
                }
            }

            return Finish();
        }

        // ====================================================================
        // Setup.
        // ====================================================================

        private MonsterBrain NewMonster() =>
            new MonsterBrain(_world, _rng, _map.PositionOf(_map.MonsterSpawnNode), Vec3.Forward);

        private void PlaceLoot()
        {
            // §12: "막힌 길에 좋은 것을 둔다." One piece per rewarded dead end, drawn
            // uniformly from §08's table minus 금고 속 문서 — that row is not loose
            // loot, it is behind a safe, so the safe nodes carry it instead.
            var loose = new[] { LootId.Trinket, LootId.Timepiece, LootId.LargePiece };

            for (var i = 0; i < _map.LootNodes.Length; i++)
            {
                if (Array.IndexOf(_map.SafeNodes, _map.LootNodes[i]) >= 0)
                {
                    _lootAt[i] = LootId.None;
                    _safeAt[i] = new LootSafe();
                }
                else
                {
                    _lootAt[i] = loose[_rng.NextInt(0, loose.Length)];
                }
            }
        }

        // ====================================================================
        // The monster (§06 · §07).
        // ====================================================================

        private void StepMonster(float dt)
        {
            var tier = _clock.Tier;

            _targets.Clear();
            _sounds.Clear();

            foreach (var agent in _agents)
            {
                if (!agent.State.IsStillInPlay || !agent.State.IsUnderground)
                {
                    continue;
                }

                _targets.Add(new MonsterTarget(
                    agent.Index, agent.Position, agent.IsConcealedFrom(_monster.Position)));

                // §08's 강화 손전등: "괴물이 2배 멀리서 본다." A beam is noticed from
                // further away than a body can be made out, so it arrives through the
                // attention channel rather than through sight — and only when there is
                // a line to see it along.
                if (agent.IsLit && _world.HasLineOfSight(_monster.Position, agent.Position))
                {
                    _sounds.Add(new MonsterSoundCue(
                        agent.Position, agent.Flashlight.NoticeDistance, GameConstants.FlareIgniteNoiseLevel));
                }

                // §04's 주자: a taunt is a shout, and §06 gives 순찰 exactly one
                // transition — 소리 감지 → 경계. So this is how aggro is pulled.
                if (agent.Runner != null && agent.Runner.AggroForced)
                {
                    _sounds.Add(new MonsterSoundCue(agent.Position, GameConstants.RunnerTauntRange, 1f));
                }
            }

            var input = new MonsterTickInput(
                tier.MonsterSpeed,
                tier.PatrolZoneCountFor(_map.Graph.Zones.Length),
                tier.StandstillChance,
                _targets,
                _sounds);

            _monster.Tick(dt, input);

            TrackChase(dt, tier);
        }

        private void TrackChase(float dt, ThreatTier tier)
        {
            var chasing = _monster.State == MonsterStateId.Chase;
            _recorder.SetAggroActive(chasing);

            if (chasing && !_chaseWasActive)
            {
                _result.Chases++;
                _chaseTargetAtStart = _monster.ChaseTargetId;

                var target = AgentOrNull(_chaseTargetAtStart);
                _chaseTargetLoadedAtStart = target != null
                    && CarryLoad.BandIndexOf(target.Inventory.TotalWeight) > 0;

                if (target != null && target.Role == RoleId.Runner)
                {
                    _result.RunnerChases++;
                    if (_chaseTargetLoadedAtStart)
                    {
                        _result.RunnerChasesLoaded++;
                    }
                }
            }
            else if (!chasing && _chaseWasActive)
            {
                // Released rather than caught: a catch kills, and the kill has already
                // removed the target from the list by the time this runs.
                var target = AgentOrNull(_chaseTargetAtStart);
                if (target != null && target.State.IsStillInPlay)
                {
                    _result.ChaseEscapes++;
                    if (target.Role == RoleId.Runner)
                    {
                        _result.RunnerEscapes++;
                        if (_chaseTargetLoadedAtStart)
                        {
                            _result.RunnerEscapesLoaded++;
                        }

                        target.Runner?.NotifyAggroReleased();
                    }
                }

                _chaseTargetAtStart = -1;
            }

            _chaseWasActive = chasing;

            if (!chasing)
            {
                return;
            }

            // §06's catch, modelled the way RunnerTest models it: the chase ends when
            // the gap closes. The monster covers tier.MonsterSpeed × dt this step, so
            // anything inside that has been reached.
            var prey = AgentOrNull(_monster.ChaseTargetId);
            if (prey == null || !prey.State.IsStillInPlay || !prey.State.IsUnderground)
            {
                return;
            }

            var reach = (tier.MonsterSpeed * dt) + GameConstants.MonsterWaypointTolerance;
            if (Vec3.DistanceFlat(_monster.Position, prey.Position) <= reach)
            {
                Kill(prey);
            }
        }

        private void Kill(SimAgent agent)
        {
            if (!_match.TryKill(agent.Index, agent.Position))
            {
                return;
            }

            _recorder.RecordPlayerDied();

            // §08: "사망자의 전리품 — 떨어진다." An empty-handed death leaves no pile,
            // and DroppedLootField says so with -1 rather than with an empty one.
            var pile = _dropped.DropFrom(agent.Inventory, agent.Position);
            if (pile >= 0)
            {
                agent.State.Ghost?.NoteOwnLootDropped(_dropped.ValueOf(pile));
            }

            agent.State.SetLootState(false, false);
            agent.TargetNode = -1;

            if (_carrierIndex == agent.Index)
            {
                _carrierIndex = -1;
                _objectiveTaken = _match.ObjectiveInHands;
            }
        }

        private SimAgent? AgentOrNull(int index) =>
            index >= 0 && index < _agents.Length ? _agents[index] : null;

        // ====================================================================
        // The agents.
        // ====================================================================

        private void StepAgents(float dt)
        {
            foreach (var agent in _agents)
            {
                if (!agent.State.IsStillInPlay)
                {
                    continue;
                }

                if (agent.State.IsOnSurface)
                {
                    StepOnSurface(dt, agent);
                }
                else
                {
                    StepUnderground(dt, agent);
                }

                _recorder.RecordMovement(dt, agent.LastSpeed > 0f, agent.LastStepBackward);

                if (agent.Inventory.WeightBand > _result.PeakWeightBand)
                {
                    _result.PeakWeightBand = agent.Inventory.WeightBand;
                }
            }
        }

        private void StepOnSurface(float dt, SimAgent agent)
        {
            agent.Intent = AgentIntent.AtVehicle;

            if (agent.AtVehicle)
            {
                return;
            }

            // The vehicle is a walk from the door, so surfacing and descending both
            // cost real seconds — §07 prices "시각 확인하러 나가기" at about a minute.
            var input = new MoveInput(1f, 0f, true);
            agent.AdvanceToPoint(_map.SurfacePosition, dt, agent.ResolveSpeed(input, false, _overrides));
        }

        private void StepUnderground(float dt, SimAgent agent)
        {
            ManageLight(dt, agent);
            StepAbilities(dt, agent);

            var chased = _monster.State == MonsterStateId.Chase && _monster.ChaseTargetId == agent.Index;
            if (chased || TooCloseToTheMonster(agent))
            {
                Flee(dt, agent);
                return;
            }

            agent.Runner?.Tick(dt, false, agent.Inventory.CarryingObjective, agent.Inventory.CanSprint);

            ChooseIntent(agent);

            switch (agent.Intent)
            {
                case AgentIntent.SeekClue:
                    WorkClue(dt, agent);
                    break;
                case AgentIntent.SeekLoot:
                    WorkLoot(dt, agent);
                    break;
                case AgentIntent.SeekObjective:
                    WorkObjective(dt, agent);
                    break;
                default:
                    WorkSurfacing(dt, agent);
                    break;
            }
        }

        /// <summary>
        /// §06 · §10: a player who can tell where the monster is walks the other way.
        /// The perception is deliberately narrow — the roar of a chase (§06 gives 추격
        /// the 발소리+포효 row), or having lit it up with their own beam. Anything
        /// wider would be giving the team information §04 sells a whole role for.
        /// </summary>
        private bool TooCloseToTheMonster(SimAgent agent)
        {
            var distance = Vec3.DistanceFlat(agent.Position, _monster.Position);
            if (distance > GameConstants.AggroReleaseDistance)
            {
                return false;
            }

            if (_monster.IsRoaring)
            {
                return true;
            }

            return agent.IsLit
                   && distance <= agent.Flashlight.RangeFor(_clock.Tier.FlashlightRangeMultiplier)
                   && _world.HasLineOfSight(agent.Position, _monster.Position);
        }

        private void ManageLight(float dt, SimAgent agent)
        {
            if (_world.IsAreaLit(agent.Position))
            {
                // §08's battery is the resource §03's 왕복 is built on, so a lit zone
                // is spent rather than wasted.
                agent.Flashlight.TurnOff();
            }
            else if (!agent.Flashlight.IsOn)
            {
                agent.Flashlight.TryTurnOn();
            }

            agent.Flashlight.Tick(dt);

            if (!agent.Battery.CanPowerLight && agent.Battery.SpareCells > 0
                && agent.Battery.TrySwapCell(out _))
            {
                _recorder.RecordBatteryUsed();
            }
        }

        private void StepAbilities(float dt, SimAgent agent)
        {
            var observation = _monster.State == MonsterStateId.Standstill
                ? MonsterObservation.Standstill(_monster.Position, _monster.Facing, _monster.ChaseTargetId)
                : MonsterObservation.Moving(
                    _monster.Position, _monster.Facing * _clock.Tier.MonsterSpeed, _monster.ChaseTargetId);

            // §04's 청음사 is deafened by its own steps; the scale is the fastest
            // anyone in §06's table moves, so walking already sits near the threshold.
            agent.Listener?.Tick(
                dt, agent.Position, agent.LastSpeed / GameConstants.RunnerSprintSpeed, observation);

            agent.Observer?.Tick(dt, agent.Position, agent.LastSpeed, 0f, observation);

            if (agent.Flasher != null)
            {
                agent.Flasher.Tick(dt);

                var threatened = _monster.State == MonsterStateId.Chase
                                 && Vec3.DistanceFlat(agent.Position, _monster.Position) <= GameConstants.FlashRange;

                if (threatened && agent.Flasher.IsReady
                    && agent.Flasher.TryFlash(
                        agent.Position, (_monster.Position - agent.Position).NormalizedFlat, observation, out _)
                    && agent.Flasher.TryConsumeStun(out var stunSeconds))
                {
                    _monster.Stun(stunSeconds);
                }
            }

            if (agent.Runner != null && _monster.State == MonsterStateId.Chase
                && _monster.ChaseTargetId != agent.Index && _monster.ChaseTargetId >= 0)
            {
                // §04: the 주자 takes the chase off whoever cannot survive it.
                agent.Runner.TryTaunt(agent.Position, observation, out _);
            }

            StepEngineer(dt, agent);
        }

        private void StepEngineer(float dt, SimAgent agent)
        {
            var engineer = agent.Engineer;
            if (engineer == null)
            {
                return;
            }

            engineer.Tick(dt, agent.Position);

            if (engineer.TryTakeOutcome(out var outcome) && outcome.Action == EngineerAction.ZoneLight)
            {
                // §03: a lit zone is read without a flashlight — and the monster comes
                // to it, which this simulator gets for free because a lit agent is a
                // visible agent.
                _world.SetZoneLit(outcome.ZoneId, outcome.ZoneLit);
            }

            if (engineer.IsBusy || engineer.Materials <= 0 || !agent.HasArrived)
            {
                return;
            }

            var zone = _world.ZoneIdAt(agent.Position);
            if (zone < 0 || engineer.IsZoneLit(zone) || !ZoneHasWorkLeft(zone))
            {
                return;
            }

            engineer.TryBegin(
                EngineerRequest.ZoneLight(agent.CurrentNode, agent.Position, zone, true), agent.Position, out _);
        }

        // ====================================================================
        // Intent (§01's loop, as a policy).
        // ====================================================================

        private void ChooseIntent(SimAgent agent)
        {
            // The carrier can die. §03's escort has to survive that, or the team
            // stands at the vehicle forever with the answer in its pocket.
            if (_pinnedSiteId >= 0 && !_objectiveTaken
                && (_carrierIndex < 0 || !_agents[_carrierIndex].State.IsStillInPlay))
            {
                ChooseCarrier();
            }

            if (agent.Inventory.CarryingObjective || _evacuating || MustSurface(agent))
            {
                agent.Intent = AgentIntent.Surfacing;
                return;
            }

            if (_pinnedSiteId >= 0 && !_objectiveTaken && agent.Index == _carrierIndex && ReadyForTheObjective())
            {
                agent.Intent = AgentIntent.SeekObjective;
                return;
            }

            // §03 makes the clue chain the only thing that must happen, so it outranks
            // 전리품 — which §03 also calls optional outright.
            if (NextClueTarget(agent) >= 0)
            {
                agent.Intent = AgentIntent.SeekClue;
                return;
            }

            if (NextLootTarget(agent) >= 0)
            {
                agent.Intent = AgentIntent.SeekLoot;
                return;
            }

            // Nothing left for this agent to trade time for, so §01's core decision
            // has answered itself: go and get it.
            if (_pinnedSiteId >= 0 && !_objectiveTaken && agent.Index == _carrierIndex)
            {
                agent.Intent = AgentIntent.SeekObjective;
                return;
            }

            ReconsiderChain();
            agent.Intent = AgentIntent.Surfacing;
        }

        /// <summary>
        /// §01's core decision, taken the way §01's own flow takes it: the objective
        /// comes out on the last descent — "3차 잠입 → 목표물 발견 → 지금 들고 나갈까,
        /// 전리품 더 챙길까?" — so the team clears the building of 전리품 first and picks
        /// the objective up when there is nothing left to trade time for.
        /// <para>
        /// It also stops early when the night has turned (§07's 심야) or when somebody
        /// has died, because §02 makes "누군가는 살아서 나가야 한다" the thing that is
        /// actually being protected.
        /// </para>
        /// </summary>
        private bool ReadyForTheObjective()
        {
            if (_clock.TierIndex >= GameConstants.ThreatTierCount - 3 || _match.GhostCount > 0)
            {
                return true;
            }

            for (var i = 0; i < _lootAt.Length; i++)
            {
                if (_safeAt[i] != null)
                {
                    if (HasLivingEngineer())
                    {
                        return false;
                    }

                    continue;
                }

                if (_lootAt[i] != LootId.None)
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasLivingEngineer()
        {
            foreach (var agent in _agents)
            {
                if (agent.State.IsStillInPlay && agent.Role == RoleId.Engineer)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// §03: "틀렸으면 다시 들어가야 한다." When the building has been read out and
        /// the chain still has not converged, somebody remembered a mark wrong. The
        /// team goes back to the signs — and because <see cref="ObjectiveResolver.TryRead"/>
        /// keeps no record, the second look can come out differently.
        /// </summary>
        private void ReconsiderChain()
        {
            if (_pinnedSiteId >= 0 || _clueRead.Length == 0)
            {
                return;
            }

            var anythingLeft = false;
            for (var i = 0; i < _clueRead.Length; i++)
            {
                if (_clueFound[i] && !_clueRead[i])
                {
                    anythingLeft = true;
                    break;
                }
            }

            if (anythingLeft)
            {
                return;
            }

            _belief = ClueBelief.Empty;
            Array.Clear(_clueRead, 0, _clueRead.Length);
        }

        private bool MustSurface(SimAgent agent)
        {
            // §03: "배터리가 떨어지면 단서를 읽을 수 없다." The dead battery is the
            // reason to come out, and §08 sells the reason to go back in.
            if (!agent.Battery.CanPowerLight && agent.Battery.SpareCells == 0)
            {
                return true;
            }

            // §07's 생존 불가 수준 — but only if the team can read a clock. §07 puts the
            // hour on the surface, and §08 sells the 회중시계 that moves it underground.
            if (_clock.IsTimeReadable && _clock.TierIndex >= GameConstants.ThreatTierCount - 1)
            {
                return true;
            }

            return agent.Inventory.FreeCapacity <= 0;
        }

        // ====================================================================
        // Actions.
        // ====================================================================

        private void WorkClue(float dt, SimAgent agent)
        {
            var node = NextClueTarget(agent);
            if (node < 0)
            {
                WorkSurfacing(dt, agent);
                return;
            }

            agent.TargetNode = node;
            agent.RouteTo(node);

            if (!agent.HasArrived)
            {
                agent.Reader.Cancel();
                agent.TargetClueId = -1;
                Walk(dt, agent, forward: 1f, strafe: 0f, sprint: true, wantsSprint: false);
                return;
            }

            agent.Advance(dt, 0f, false);

            var site = _siteOfNode[node];
            if (site >= 0)
            {
                _siteInspected[site] = true;
            }

            var clueId = ClueAt(node);
            if (clueId < 0)
            {
                // Nothing written here. §03's randomised clue placement means most
                // 후보 지점 are a wasted walk, and that is where §01's minutes go.
                agent.TargetNode = -1;
                return;
            }

            _clueFound[clueId] = true;
            agent.TargetClueId = clueId;

            var context = new ClueReadContext
            {
                ClueId = clueId,
                DistanceToClue = 0f,
                ReaderSpeed = 0f,
                LightQuality = agent.IsLit ? 1f : 0f,
                ViewAngleDegrees = 0f,
                Blur = 0f,
            };

            agent.Reader.Tick(dt, context);

            if (agent.Reader.State != ClueReadState.Complete)
            {
                return;
            }

            // §13: the read leaves the host as a ClueReport and nothing else.
            if (_resolver.TryRead(clueId, agent.Reader.Observation, _rng, out var report))
            {
                // Only the host can tell a misread from a read, which is why §13's
                // counter is filed here. ObjectiveResolver counts them; this notices
                // the counter move.
                var misread = _resolver.Misreads > _misreadsSeen;
                _misreadsSeen = _resolver.Misreads;

                _belief = _belief.Absorb(report);
                _recorder.RecordClueRead(misread);
                Narrow();
            }

            _clueRead[clueId] = true;
            agent.Reader.Cancel();
            agent.TargetClueId = -1;
            agent.TargetNode = -1;
        }

        private void Narrow()
        {
            var narrowing = ClueChain.Narrow(_map.Catalog, _belief);

            if (narrowing.Status == NarrowingStatus.Contradiction)
            {
                // §03's only free warning: the sign named no floor in this building, so
                // the belief is not incomplete, it is wrong. Everything gets re-read.
                _belief = ClueBelief.Empty;
                Array.Clear(_clueRead, 0, _clueRead.Length);
                return;
            }

            if (narrowing.Status != NarrowingStatus.Pinned)
            {
                return;
            }

            _pinnedSiteId = narrowing.SiteId;
            _result.ObjectivePinned = true;
            ChooseCarrier();
        }

        private void ChooseCarrier()
        {
            // §03: "주자가 들면 질주 불가" — so the team hands it to anyone else if it
            // can, and the last survivor carries it regardless.
            var fallback = -1;
            for (var i = 0; i < _agents.Length; i++)
            {
                if (!_agents[i].State.IsStillInPlay)
                {
                    continue;
                }

                if (fallback < 0)
                {
                    fallback = i;
                }

                if (_agents[i].Role != RoleId.Runner)
                {
                    _carrierIndex = i;
                    return;
                }
            }

            _carrierIndex = fallback;
        }

        private void WorkObjective(float dt, SimAgent agent)
        {
            if (!agent.State.CanTakeObjective)
            {
                // §03: "전리품은 그 전에 다 챙겨야 한다." Hands full means a trip out
                // first, which is where §03's last 왕복 comes from.
                WorkSurfacing(dt, agent);
                return;
            }

            var node = _map.NodeOfSite(_pinnedSiteId);
            agent.RouteTo(node);

            if (!agent.HasArrived)
            {
                Walk(dt, agent, forward: 1f, strafe: 0f, sprint: true, wantsSprint: false);
                return;
            }

            if (!_resolver.IsObjectiveSite(_pinnedSiteId))
            {
                // Nothing here. §03: "틀렸으면 다시 들어가야 한다."
                _result.WrongSiteVisits++;
                _pinnedSiteId = -1;
                _carrierIndex = -1;
                _belief = ClueBelief.Empty;
                Array.Clear(_clueRead, 0, _clueRead.Length);
                return;
            }

            // Host-side invariant, and the only place the objective's actual position
            // is ever read. §13 keeps that position on the host; this checks that the
            // site the *players* reasoned their way to is the place the host put it,
            // so a layout bug shows up as a crash here rather than as a quietly
            // teleporting objective in the numbers.
            if (Vec3.DistanceFlat(agent.Position, _objectivePosition) > GameConstants.ClueReadRange)
            {
                throw new InvalidOperationException(
                    "The objective's site and the objective's position disagree. §03's chain pinned site "
                    + _pinnedSiteId + ", the host placed the objective elsewhere, and no amount of correct "
                    + "play would find it.");
            }

            if (_match.TryTakeObjective(agent.Index))
            {
                agent.Inventory.TryPickUpObjective();
                _objectiveTaken = true;
                _evacuating = true;
                agent.Intent = AgentIntent.Surfacing;
            }
        }

        private void WorkLoot(float dt, SimAgent agent)
        {
            var node = NextLootTarget(agent);
            if (node < 0)
            {
                WorkSurfacing(dt, agent);
                return;
            }

            agent.TargetNode = node;
            agent.RouteTo(node);

            if (!agent.HasArrived)
            {
                Walk(dt, agent, forward: 1f, strafe: 0f, sprint: true, wantsSprint: false);
                return;
            }

            agent.Advance(dt, 0f, false);

            var slot = Array.IndexOf(_map.LootNodes, node);
            if (slot < 0)
            {
                agent.TargetNode = -1;
                return;
            }

            _lootInspected[slot] = true;

            var safe = _safeAt[slot];
            if (safe != null)
            {
                // §08 gates 금고 속 문서 behind the 정비공; anyone else turns the handle
                // and gets nowhere, which LootSafe decides, not this file.
                var work = safe.Tick(dt, agent.Role);

                if (work == SafeWorkResult.Working)
                {
                    return;
                }

                if (safe.IsOpen && safe.TryTakeContents(agent.Inventory))
                {
                    _safeAt[slot] = null;
                    agent.State.SetLootState(agent.Inventory.LootCount > 0, false);
                }
                else if (safe.IsEmptied)
                {
                    _safeAt[slot] = null;
                }

                // Wrong role, or open with no room for the 문서. Either way this agent
                // is done here; leaving the target set would leave it standing at a
                // safe it cannot empty for the rest of the night.
                agent.TargetNode = -1;
                return;
            }

            var piece = _lootAt[slot];
            if (piece != LootId.None && agent.Inventory.TryAdd(piece))
            {
                _lootAt[slot] = LootId.None;
                agent.State.SetLootState(agent.Inventory.LootCount > 0, false);
            }

            agent.TargetNode = -1;
        }

        private void WorkSurfacing(float dt, SimAgent agent)
        {
            agent.Intent = AgentIntent.Surfacing;
            agent.Reader.Cancel();
            agent.TargetNode = -1;
            agent.RouteTo(_map.EntranceNode);

            if (!agent.HasArrived)
            {
                Walk(dt, agent, forward: 1f, strafe: 0f, sprint: true, wantsSprint: false);
                return;
            }

            agent.State.TrySurface();
        }

        private void Flee(float dt, SimAgent agent)
        {
            agent.Intent = AgentIntent.Fleeing;
            agent.Reader.Cancel();
            agent.TargetNode = -1;

            // Re-decided every step, not only at junctions. §06's monster walks the
            // same corridors, so the moment it gets between a player and where they
            // were going, the corridor has two ends and only one of them is an escape.
            // §12's escape is a route, not a step: "단일 모퉁이는 실패한다." A player
            // who only ever picks the better of two adjacent nodes never accumulates
            // the 12 m §06 asks for, because every corner hands the choice back. So
            // the flee commits to somewhere far and runs the corridors to get there,
            // and re-plans when the route ends or when the next hop has turned bad.
            if (agent.HasArrived || NextHopTurnedBad(agent))
            {
                agent.ClearRoute();
                var destination = FarthestFromMonster(agent);
                if (destination >= 0)
                {
                    agent.RouteTo(destination);
                }
            }

            var target = agent.NextNode;

            // §06: only the 주자 has anything faster than running to spend, and §03
            // takes it away again the moment the objective is in its hands.
            var wantsSprint = agent.Role == RoleId.Runner;
            agent.Runner?.Tick(dt, wantsSprint, agent.Inventory.CarryingObjective, agent.Inventory.CanSprint);

            // §05's 후진 belongs to the player who has *seen* the monster and is
            // giving ground while keeping it in view. It does not belong in a chase:
            // MulBackward puts every role below every tier of §07's speed table, so a
            // player who backpedals out of an active chase has decided to die. So this
            // is the retreat, and the chase below it is a run.
            var givingGround = _monster.ChaseTargetId != agent.Index
                               && target >= 0
                               && MonsterDistance(_map.PositionOf(target)) < MonsterDistance(agent.Position);

            if (givingGround)
            {
                Walk(dt, agent, forward: -1f, strafe: 0f, sprint: true, wantsSprint: wantsSprint);
            }
            else
            {
                Walk(dt, agent, forward: 1f, strafe: 0f, sprint: true, wantsSprint: wantsSprint);
            }
        }

        /// <summary>
        /// How far a point is from the monster, for a player deciding which way to run.
        /// <para>
        /// Walking distance plus straight-line distance. Walking distance alone is the
        /// right measure — §12's whole S-corridor argument is that path and sight
        /// diverge — but on a graph it snaps the monster to its nearest node, and every
        /// neighbour of the node the monster is standing on then scores identically.
        /// The straight-line term breaks that tie the way a player's eyes would.
        /// </para>
        /// </summary>
        private float MonsterDistance(Vec3 point) =>
            _world.NavigableDistance(point, _monster.Position)
            + Vec3.DistanceFlat(point, _monster.Position);

        private bool NextHopTurnedBad(SimAgent agent)
        {
            var next = agent.NextNode;
            return next >= 0 && MonsterDistance(_map.PositionOf(next)) < MonsterDistance(agent.Position);
        }

        /// <summary>
        /// The place in the building furthest from the monster by walking distance.
        /// §12: "막힌 길 — 많으면 운에 죽음", so a dead end is only chosen when the map
        /// offers nothing else.
        /// </summary>
        private int FarthestFromMonster(SimAgent agent)
        {
            var best = -1;
            var bestScore = float.NegativeInfinity;

            for (var node = 0; node < _map.Graph.Nodes.Length; node++)
            {
                var position = _map.PositionOf(node);
                if (float.IsPositiveInfinity(_world.NavigableDistance(agent.Position, position)))
                {
                    continue;
                }

                var penalty = _map.Graph.IsDeadEnd(node) ? GameConstants.MapExtent : 0f;
                var score = MonsterDistance(position) - penalty;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }

            return best;
        }

        private void Walk(float dt, SimAgent agent, float forward, float strafe, bool sprint, bool wantsSprint)
        {
            var input = new MoveInput(forward, strafe, sprint);
            var speed = agent.ResolveSpeed(input, wantsSprint, _overrides);
            agent.Advance(dt, speed, input.IsBackpedalling);
        }

        // ====================================================================
        // The surface (§08).
        // ====================================================================

        private void TeamSurfacePhase()
        {
            // Team-level and atomic. §08's 공용 지갑 means the shop is one negotiation,
            // not four, and descending one player at a time would make §03's 왕복
            // counter tick on every straggler.
            foreach (var agent in _agents)
            {
                if (agent.State.IsStillInPlay && (!agent.State.IsOnSurface || !agent.AtVehicle))
                {
                    return;
                }
            }

            if (_match.StillInPlayCount == 0)
            {
                return;
            }

            RunShop();

            foreach (var agent in _agents)
            {
                if (!agent.State.IsStillInPlay)
                {
                    continue;
                }

                if (_evacuating)
                {
                    if (_match.TryExtract(agent.Index))
                    {
                        _recorder.RecordPlayerEscaped();
                        if (_match.ObjectiveRecovered)
                        {
                            _recorder.RecordObjectiveRecovered();
                        }
                    }

                    continue;
                }

                if (agent.State.TryDescend())
                {
                    agent.PlaceAtEntrance();
                    agent.Intent = AgentIntent.Descending;
                }
            }

            if (!_evacuating)
            {
                _descentIndex++;
            }
        }

        private void RunShop()
        {
            if (_descentIndex == 0)
            {
                _result.CreditsBeforeFirstDescent = _wallet.Credits;
            }

            foreach (var agent in _agents)
            {
                if (!agent.State.IsStillInPlay)
                {
                    continue;
                }

                var pieces = agent.Inventory.LootCount;
                var credits = _shop.SellAll(agent.Inventory);
                var adjustment = _overrides.SaleAdjustment(credits);

                if (adjustment > 0)
                {
                    _wallet.Deposit(adjustment);
                }
                else if (adjustment < 0)
                {
                    _wallet.TrySpend(-adjustment);
                }

                if (pieces > 0)
                {
                    _recorder.RecordLootSold(credits + adjustment);
                    _result.LootSold += pieces;
                }

                agent.State.SetLootState(false, false);
                agent.State.TryTreatInjury();
            }

            if (_wallet.Credits > _result.PeakCredits)
            {
                _result.PeakCredits = _wallet.Credits;
            }

            if (_surfacingsSeen == 1)
            {
                _result.CreditsAfterFirstReturn = _wallet.Credits;
            }

            Buy();

            // §07: coming up is how the team learns the hour, so this is where the
            // decision to stop is actually made.
            if (_clock.TierIndex >= GameConstants.ThreatTierCount - 1 || _objectiveTaken)
            {
                _evacuating = true;
            }

            // §03: "배터리가 떨어지면 단서를 읽을 수 없다." When every light is dead and
            // the wallet cannot buy another cell, the run is over on §03's own terms —
            // §02's 생존 row, chosen rather than lost. Batteries are bought above, so
            // by here the shop has already done what it can.
            if (!AnyoneStillHasLight())
            {
                _evacuating = true;
            }
        }

        private bool AnyoneStillHasLight()
        {
            foreach (var agent in _agents)
            {
                if (agent.State.IsStillInPlay
                    && (agent.Battery.CanPowerLight || agent.Battery.SpareCells > 0))
                {
                    return true;
                }
            }

            return false;
        }

        private void Buy()
        {
            // §08's own ordering: 소모품 first, because §03 makes the battery the thing
            // that decides whether the team can read anything at all; then §11's
            // missing-role stand-in; then the 정보 and 탐험 upgrades.
            foreach (var agent in _agents)
            {
                if (agent.State.IsStillInPlay && agent.Battery.SpareCells == 0)
                {
                    var target = agent;
                    TryPurchase(ShopItemId.Battery, () => target.Battery.AddCells(1));
                }
            }

            foreach (var agent in _agents)
            {
                if (agent.Engineer != null
                    && agent.Engineer.Materials <= GameConstants.EngineerZoneLightMaterialCost)
                {
                    var engineer = agent.Engineer;
                    TryPurchase(ShopItemId.RepairMaterials, () => engineer.AddMaterials(1));
                }
            }

            var gap = _match.Gap;
            if (gap.IsInShopList)
            {
                var substitute = ShopCatalogue.SubstituteFor(gap.MissingRole);
                if (_shop.StockOf(substitute) == 0 && !_shop.ConflictsWithRoster(substitute, _roster))
                {
                    TryPurchase(substitute, null);
                }
            }

            if (_shop.StockOf(ShopItemId.PocketWatch) == 0)
            {
                TryPurchase(ShopItemId.PocketWatch, () => _clock.SetPocketWatchOwned(true));
            }

            if (_shop.StockOf(ShopItemId.UpgradedFlashlight) == 0)
            {
                TryPurchase(ShopItemId.UpgradedFlashlight, () =>
                {
                    foreach (var agent in _agents)
                    {
                        agent.Flashlight.SetUpgraded(true);
                    }
                });
            }

            if (_shop.StockOf(ShopItemId.Bag) == 0)
            {
                TryPurchase(ShopItemId.Bag, () =>
                {
                    foreach (var agent in _agents)
                    {
                        if (agent.State.IsStillInPlay && !agent.Inventory.BagEquipped
                            && _shop.TryEquipBag(agent.Inventory))
                        {
                            return;
                        }
                    }
                });
            }
        }

        private void TryPurchase(ShopItemId item, Action? apply)
        {
            if (_shop.TryBuy(item) != PurchaseOutcome.Purchased)
            {
                return;
            }

            var cost = ShopCatalogue.CostOf(item);
            _recorder.RecordPurchase(item, cost);
            _result.CreditsSpent += cost;

            if (ShopCatalogue.Of(item).IsCapabilityUpgrade && _result.FirstUpgradeSeconds < 0f)
            {
                _result.FirstUpgradeSeconds = _clock.ElapsedSeconds;
                _result.FirstUpgradeDescent = _descentIndex;
            }

            apply?.Invoke();
        }

        // ====================================================================
        // Queries the policy needs.
        // ====================================================================

        private void SettleTeamPosition()
        {
            foreach (var agent in _agents)
            {
                if (agent.State.IsStillInPlay && agent.State.IsUnderground)
                {
                    _clock.SetTeamOnSurface(false);
                    return;
                }
            }

            _clock.SetTeamOnSurface(true);
        }

        private bool ZoneHasWorkLeft(int zoneId)
        {
            for (var i = 0; i < _map.Catalog.Sites.Count; i++)
            {
                var site = _map.Catalog.Sites[i];
                if (site.ZoneId == zoneId && !_siteInspected[site.SiteId])
                {
                    return true;
                }
            }

            for (var i = 0; i < _clueRead.Length; i++)
            {
                if (_clueFound[i] && !_clueRead[i] && _resolver.Markers[i].ZoneId == zoneId)
                {
                    return true;
                }
            }

            return false;
        }

        private int ClueAt(int node)
        {
            for (var i = 0; i < _resolver.Markers.Count; i++)
            {
                var marker = _resolver.Markers[i];
                if (_clueRead[marker.ClueId])
                {
                    continue;
                }

                if (_map.Graph.NearestNode(marker.Position) == node)
                {
                    return marker.ClueId;
                }
            }

            return -1;
        }

        /// <summary>
        /// The node this agent should walk to for §03's chain: a clue somebody has
        /// already found and nobody has read, or the nearest 후보 지점 nobody has looked
        /// at yet. Returns -1 when the building has been read out.
        /// </summary>
        private int NextClueTarget(SimAgent agent)
        {
            var best = -1;
            var bestDistance = float.PositiveInfinity;

            for (var i = 0; i < _clueRead.Length; i++)
            {
                if (!_clueFound[i] || _clueRead[i])
                {
                    continue;
                }

                var node = _map.Graph.NearestNode(_resolver.Markers[i].Position);
                if (node < 0 || IsClaimed(agent, node))
                {
                    continue;
                }

                var distance = _world.NavigableDistance(agent.Position, _resolver.Markers[i].Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = node;
                }
            }

            if (best >= 0)
            {
                return best;
            }

            for (var i = 0; i < _map.Catalog.Sites.Count; i++)
            {
                var site = _map.Catalog.Sites[i];
                if (_siteInspected[site.SiteId])
                {
                    continue;
                }

                var node = _map.NodeOfSite(site.SiteId);
                if (IsClaimed(agent, node))
                {
                    continue;
                }

                var distance = _world.NavigableDistance(agent.Position, site.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = node;
                }
            }

            return best;
        }

        /// <summary>
        /// The dead end this agent should walk to for §08's 전리품 — one it can still
        /// carry something out of, or one nobody has looked in yet.
        /// </summary>
        private int NextLootTarget(SimAgent agent)
        {
            var best = -1;
            var bestDistance = float.PositiveInfinity;

            for (var i = 0; i < _lootAt.Length; i++)
            {
                var node = _map.LootNodes[i];
                var safe = _safeAt[i];

                if (safe != null
                    && (agent.Role != RoleId.Engineer || !agent.Inventory.CanAdd(LootId.SafeDocument)))
                {
                    continue;
                }

                if (_lootInspected[i] && safe == null
                    && (_lootAt[i] == LootId.None || !agent.Inventory.CanAdd(_lootAt[i])))
                {
                    continue;
                }

                if (IsClaimed(agent, node))
                {
                    continue;
                }

                var distance = _world.NavigableDistance(agent.Position, _map.PositionOf(node));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = node;
                }
            }

            return best;
        }

        private bool IsClaimed(SimAgent agent, int node)
        {
            foreach (var other in _agents)
            {
                if (other.Index != agent.Index && other.State.IsStillInPlay && other.TargetNode == node)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================
        // Close.
        // ====================================================================

        private SimMatchResult Finish()
        {
            var resolution = _match.Resolution;
            var summary = _recorder.Complete(resolution.Outcome);

            _result.Summary = summary;
            _result.Outcome = resolution.Outcome;
            _result.DurationSeconds = _clock.ElapsedSeconds;
            _result.RoundTrips = _clock.SurfacingCount;
            _result.CreditsUnspent = _wallet.Credits;
            _result.CreditsEarned = _wallet.TotalEarned;
            _result.FinalTierIndex = _clock.TierIndex;

            for (var i = 0; i < _lootAt.Length; i++)
            {
                if (_lootAt[i] != LootId.None || _safeAt[i] != null)
                {
                    _result.LootLeftBehind++;
                }
            }

            return _result;
        }
    }
}
