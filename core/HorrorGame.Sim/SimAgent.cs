using System;
using HorrorGame.Core;
using HorrorGame.Core.Abilities;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Light;
using HorrorGame.Core.Map;
using HorrorGame.Core.Match;
using HorrorGame.Core.Math;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Session;

namespace HorrorGame.Sim
{
    /// <summary>What an agent is currently trying to do. One row of §01's 핵심 루프.</summary>
    public enum AgentIntent
    {
        /// <summary>Standing at the vehicle between descents. §08.</summary>
        AtVehicle = 0,

        /// <summary>Walking in through the door. §01.</summary>
        Descending = 1,

        /// <summary>Walking to a clue and reading it. §03.</summary>
        SeekClue = 2,

        /// <summary>Walking to a dead end for 전리품. §08 · §12.</summary>
        SeekLoot = 3,

        /// <summary>Standing at a 금고 turning it. §04's 정비공 income.</summary>
        WorkSafe = 4,

        /// <summary>Heading for the pinned site to pick the objective up. §03.</summary>
        SeekObjective = 5,

        /// <summary>Walking back out — battery, weight, or the clock. §03's 왕복.</summary>
        Surfacing = 6,

        /// <summary>Being chased. §06.</summary>
        Fleeing = 7,
    }

    /// <summary>
    /// One of §11's four players, as a policy over the real core systems.
    /// <para>
    /// The agent owns no rules. Every number it acts on comes from a Core type it
    /// steps — <see cref="Inventory"/>, <see cref="BatteryState"/>,
    /// <see cref="ClueReader"/>, <see cref="RunnerAbility"/> — and its only job is to
    /// choose which of them to step, which is exactly the job ARCHITECTURE.md gives a
    /// MonoBehaviour. If a rule appears in this file, it is in the wrong layer.
    /// </para>
    /// <para>
    /// The policy is deliberately competent rather than clever. §16-2 asks whether
    /// the price table produces §08's growth curve for players who are trying; a
    /// simulated player who dithers would answer a different question.
    /// </para>
    /// </summary>
    public sealed class SimAgent
    {
        /// <summary>Token value meaning "no action". No node index can collide with it.</summary>
        private const int NoDwell = int.MinValue;

        private readonly SimMap _map;
        private readonly MapGraph _graph;
        private readonly IWorldProbe _world;

        private int[] _route = Array.Empty<int>();
        private int _routeIndex;

        /// <summary>What the running dwell is being spent on. See <see cref="BeginDwell"/>.</summary>
        private int _dwellToken = NoDwell;

        /// <summary>The last action paid for in full, so it is not charged twice.</summary>
        private int _dwellDoneToken = NoDwell;

        /// <summary>Builds an agent standing at the vehicle. §08: the team starts on the surface with nothing.</summary>
        /// <param name="index">Slot 0–3; also the <see cref="MonsterTarget"/> id.</param>
        /// <param name="role">§04's role for this slot.</param>
        /// <param name="state">This slot's <see cref="PlayerState"/> inside the match.</param>
        /// <param name="map">The building.</param>
        /// <param name="world">This match's own view of it. Never the shared one — see <see cref="MatchWorld"/>.</param>
        /// <param name="random">The match's seeded stream — shared, so the whole match replays from one seed.</param>
        public SimAgent(int index, RoleId role, PlayerState state, SimMap map, IWorldProbe world, IRandomSource random)
        {
            Index = index;
            Role = role;
            State = state;
            _map = map;
            _graph = map.Graph;
            _world = world;

            Inventory = new Inventory();
            Battery = new BatteryState();
            Flashlight = new FlashlightState(Battery);
            Reader = new ClueReader();

            Runner = role == RoleId.Runner ? new RunnerAbility(world) : null;
            Flasher = role == RoleId.Flasher ? new FlasherAbility(world) : null;
            Listener = role == RoleId.Listener ? new ListenerAbility(world, random) : null;
            Observer = role == RoleId.Observer ? new ObserverAbility() : null;
            Engineer = role == RoleId.Engineer ? new EngineerAbility(world, 0) : null;

            CurrentNode = map.EntranceNode;
            Position = map.SurfacePosition;
            Intent = AgentIntent.AtVehicle;
        }

        /// <summary>Slot number, and the id the monster knows this agent by.</summary>
        public int Index { get; }

        /// <summary>§04 role.</summary>
        public RoleId Role { get; }

        /// <summary>Alive, ghost or escaped. Owned by <see cref="MatchState"/>.</summary>
        public PlayerState State { get; }

        /// <summary>§08's pockets. The only thing that decides this agent's speed.</summary>
        public Inventory Inventory { get; }

        /// <summary>§03's consumable clock.</summary>
        public BatteryState Battery { get; }

        /// <summary>§03's lock on the objective, and §08's give-away.</summary>
        public FlashlightState Flashlight { get; }

        /// <summary>§03's "그 자리에서 보고".</summary>
        public ClueReader Reader { get; }

        /// <summary>Non-null for the 주자.</summary>
        public RunnerAbility? Runner { get; }

        /// <summary>Non-null for the 섬광수.</summary>
        public FlasherAbility? Flasher { get; }

        /// <summary>Non-null for the 청음사.</summary>
        public ListenerAbility? Listener { get; }

        /// <summary>Non-null for the 관측자.</summary>
        public ObserverAbility? Observer { get; }

        /// <summary>Non-null for the 정비공.</summary>
        public EngineerAbility? Engineer { get; }

        /// <summary>Where the agent is, in world metres.</summary>
        public Vec3 Position { get; private set; }

        /// <summary>The graph node last passed through.</summary>
        public int CurrentNode { get; private set; }

        /// <summary>What the agent is doing. §01.</summary>
        public AgentIntent Intent { get; set; }

        /// <summary>The node the current route ends at, or -1.</summary>
        public int GoalNode { get; private set; } = -1;

        /// <summary>
        /// The node this agent has claimed, or -1. §01's team splits up; two agents
        /// walking to the same shelf is a policy bug, not a design feature.
        /// </summary>
        public int TargetNode { get; set; } = -1;

        /// <summary>Clue currently being read, or -1.</summary>
        public int TargetClueId { get; set; } = -1;

        /// <summary>Ground speed last tick, m/s. Feeds §04's 관측자 stillness test.</summary>
        public float LastSpeed { get; private set; }

        /// <summary>Whether the last step was taken backwards. §05's 후진 telemetry bucket.</summary>
        public bool LastStepBackward { get; private set; }

        /// <summary>
        /// Seconds this agent still owes to whatever it is standing over — §07's action
        /// costs, charged by <see cref="SimScenario"/>. Zero for the shipped simulator,
        /// which resolves every action in one fixed step.
        /// </summary>
        public float DwellSeconds { get; private set; }

        /// <summary>True while the agent is standing still paying <see cref="DwellSeconds"/>.</summary>
        public bool IsDwelling => DwellSeconds > 0f;

        /// <summary>Whether this agent's own light is burning right now.</summary>
        public bool IsLit => Flashlight.IsLit || _world.IsAreaLit(Position);

        /// <summary>True once the route has been walked to its end.</summary>
        public bool HasArrived => _routeIndex >= _route.Length;

        /// <summary>The node being walked to right now, or -1. The route's next hop, not its end.</summary>
        public int NextNode => _routeIndex < _route.Length ? _route[_routeIndex] : -1;

        /// <summary>
        /// Whether the monster can pick this agent out. §03 makes the flashlight the
        /// trade: light is how you read and how you are seen.
        /// <para>
        /// A dark agent is not invisible — the monster still finds anyone inside
        /// <see cref="GameConstants.FlashlightRange"/>, the distance the design uses
        /// for "as far as a light reaches". A dark agent standing on a §12 은폐 지점
        /// is hidden outright. Everything else is Core's own
        /// <c>MonsterBrain.CanSee</c>, which caps at
        /// <see cref="GameConstants.MonsterSightRange"/> and needs a sight line.
        /// </para>
        /// </summary>
        public bool IsConcealedFrom(Vec3 monsterPosition)
        {
            if (IsLit)
            {
                return false;
            }

            if (_graph.Nodes[CurrentNode].HasAny(MapNodeKind.Concealment) && HasArrived)
            {
                return true;
            }

            return Vec3.DistanceFlat(Position, monsterPosition) > GameConstants.FlashlightRange;
        }

        /// <summary>Points the agent at a node. A route that does not exist leaves it standing.</summary>
        public void RouteTo(int goalNode)
        {
            if (goalNode == GoalNode && !HasArrived)
            {
                return;
            }

            GoalNode = goalNode;
            _routeIndex = 0;

            if (goalNode == CurrentNode)
            {
                // Turning round in the middle of a corridor. The agent has passed
                // CurrentNode but is not standing on it, so going "back to where I am"
                // is a real walk — this is the manoeuvre §06's chase forces when the
                // monster gets between a player and where they were heading.
                _route = Position == _graph.Nodes[goalNode].Position
                    ? Array.Empty<int>()
                    : new[] { goalNode };
                return;
            }

            var path = _graph.ShortestPath(CurrentNode, goalNode, -1);
            if (path == null || path.Length < 2)
            {
                _route = Array.Empty<int>();
                return;
            }

            var hops = new int[path.Length - 1];
            Array.Copy(path, 1, hops, 0, hops.Length);
            _route = hops;
        }

        /// <summary>
        /// Starts an action that takes real time — §07's 전리품 하나 더 줍기 and the
        /// rest — or reports that the same action has just been paid for.
        /// <para>
        /// The token is what makes this callable every step from the same branch that
        /// performs the action. Without it the caller would restart the dwell on the
        /// step it completed and the agent would stand there for the rest of the night;
        /// with it, the sequence is "begin, wait, complete, act" and the action's own
        /// side effects still decide whether it ever happens again.
        /// </para>
        /// </summary>
        /// <param name="token">Identifies the action. See <see cref="MatchSimulator"/>'s token helpers.</param>
        /// <param name="seconds">What <see cref="SimScenario"/> prices this action at. Zero is a no-op.</param>
        /// <returns>True if the agent is busy and the caller must wait.</returns>
        public bool BeginDwell(int token, float seconds)
        {
            if (DwellSeconds > 0f)
            {
                return true;
            }

            if (_dwellDoneToken == token || !(seconds > 0f))
            {
                return false;
            }

            _dwellToken = token;
            DwellSeconds = seconds;
            return true;
        }

        /// <summary>
        /// Pays down a running dwell and reports whether the agent is still busy.
        /// <para>
        /// The world is stepped around this, not paused by it: §07's clock runs and
        /// §06's monster patrols while a player is bent over a 전리품, which is the
        /// entire reason a dwell is a cost rather than a formality.
        /// </para>
        /// </summary>
        /// <param name="deltaSeconds">The fixed step.</param>
        /// <returns>True while seconds remain, false on the step the action completes.</returns>
        public bool TickDwell(float deltaSeconds)
        {
            if (DwellSeconds <= 0f)
            {
                return false;
            }

            LastSpeed = 0f;
            LastStepBackward = false;
            DwellSeconds -= deltaSeconds;

            if (DwellSeconds > 0f)
            {
                return true;
            }

            // Completing step: clear the debt, remember what was paid for, and let the
            // caller act in this same step rather than losing one to bookkeeping.
            DwellSeconds = 0f;
            _dwellDoneToken = _dwellToken;
            return false;
        }

        /// <summary>
        /// Abandons a running dwell. §06's chase interrupts everything — a player being
        /// chased drops what they were doing, and the seconds already spent buy nothing,
        /// so coming back costs the full price again.
        /// </summary>
        public void CancelDwell()
        {
            DwellSeconds = 0f;
            _dwellToken = NoDwell;
            _dwellDoneToken = NoDwell;
        }

        /// <summary>Clears the route without moving. Used when a flee re-plans every node.</summary>
        public void ClearRoute()
        {
            _route = Array.Empty<int>();
            _routeIndex = 0;
            GoalNode = -1;
        }

        /// <summary>
        /// Walks for one step at <paramref name="speed"/> and records what §05 and
        /// §13 want to know about it.
        /// </summary>
        /// <param name="deltaSeconds">The fixed step.</param>
        /// <param name="speed">Resolved by <see cref="SpeedResolver"/>, never by this class.</param>
        /// <param name="backward">Whether this step was taken facing the wrong way. §05.</param>
        public void Advance(float deltaSeconds, float speed, bool backward)
        {
            LastSpeed = 0f;
            LastStepBackward = false;

            if (HasArrived || speed <= 0f || deltaSeconds <= 0f)
            {
                return;
            }

            var budget = speed * deltaSeconds;
            var travelled = 0f;

            while (budget > 0f && _routeIndex < _route.Length)
            {
                var target = _graph.Nodes[_route[_routeIndex]].Position;
                var delta = target - Position;
                var distance = delta.MagnitudeFlat;

                if (distance <= budget)
                {
                    Position = target;
                    CurrentNode = _route[_routeIndex];
                    _routeIndex++;
                    budget -= distance;
                    travelled += distance;
                    continue;
                }

                Position += delta / distance * budget;
                travelled += budget;
                budget = 0f;
            }

            LastSpeed = travelled / deltaSeconds;
            LastStepBackward = backward && travelled > 0f;
        }

        /// <summary>
        /// This agent's §05 · §08 speed for the step, resolved entirely by
        /// <see cref="SpeedResolver"/>. The load term is the only place a sweep can
        /// change the answer, and it arrives through <paramref name="overrides"/>.
        /// </summary>
        /// <param name="input">The step's intent — forward, peeking, or backing away.</param>
        /// <param name="wantsSprint">§06's 질주, which only the 주자 has and only while fleeing.</param>
        /// <param name="overrides">The sweep's shadow of §08's band table.</param>
        public float ResolveSpeed(MoveInput input, bool wantsSprint, BalanceOverrides overrides)
        {
            var load = overrides.LoadMultiplier(Inventory.TotalWeight, Inventory.BagEquipped);
            var context = new MovementContext(
                GameConstants.RunSpeed, load, Inventory.CarryingObjective, Inventory.BagEquipped);

            var sprintUnlocked = Role == RoleId.Runner && Inventory.CanSprint && wantsSprint;
            var staminaReady = Runner != null && Runner.StaminaSeconds > 0f;

            context.BaseSpeed = SpeedResolver.SelectBaseSpeed(input, context, sprintUnlocked, staminaReady);
            return SpeedResolver.Resolve(input, context);
        }

        /// <summary>True when the agent is standing at the vehicle. §08's 안전 지대.</summary>
        public bool AtVehicle => Position == _map.SurfacePosition;

        /// <summary>
        /// Walks the off-graph leg between the door and the vehicle at
        /// <paramref name="speed"/>. Returns true once the agent is there.
        /// </summary>
        public bool AdvanceToPoint(Vec3 target, float deltaSeconds, float speed)
        {
            LastSpeed = 0f;
            LastStepBackward = false;

            var delta = (target - Position).Flat;
            var distance = delta.MagnitudeFlat;
            if (distance <= 0f)
            {
                Position = target;
                return true;
            }

            var budget = speed * deltaSeconds;
            if (budget >= distance)
            {
                Position = target;
                LastSpeed = distance / deltaSeconds;
                return true;
            }

            Position += delta / distance * budget;
            LastSpeed = speed;
            return false;
        }

        /// <summary>Puts the agent just inside the door. §01's 잠입.</summary>
        public void PlaceAtEntrance()
        {
            CurrentNode = _map.EntranceNode;
            Position = _map.PositionOf(_map.EntranceNode);
            ClearRoute();
        }
    }
}
