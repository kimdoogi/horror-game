#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Session;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using HorrorGame.UI;
using UnityEngine;
using MonsterStateId = HorrorGame.Core.Monster.MonsterStateId;

namespace HorrorGame.Gameplay.Match
{
    /// <summary>
    /// One match, from the first descent to §02's verdict. §01 · §02 · §03 · §07 · §08.
    /// <para>
    /// <b>This is the host.</b> ARCHITECTURE §3 says stateful core systems expose
    /// <c>Tick(float)</c> and never read a clock; something has to drive them at
    /// <see cref="GameConstants.FixedStep"/>, in a defined order, and that is the whole
    /// job of this class. It owns <c>MatchClock</c>, <c>MatchState</c>, the team
    /// <c>Wallet</c> and <c>Shop</c>, the <c>ObjectiveResolver</c> that knows this
    /// match's answer, and one <c>ClueReader</c> per player. It steps them, applies the
    /// results to transforms and screens, and decides nothing itself.
    /// </para>
    /// <para>
    /// <b>§07's partial reset is the subtlest thing here.</b> Surfacing clears the
    /// monster's chase state, its position and its aggro; it does not clear the clock,
    /// and the clock keeps running while the team argues at the van — "나가는 것은 숨
    /// 돌리기이지 리셋이 아니다". <c>MatchClock</c> already refuses to be paused, so the
    /// only thing that can get this wrong is the monster half: it goes through
    /// <c>ConsumeMonsterReset</c>, which fires exactly once per surfacing, and
    /// <c>MonsterAgent.Respawn</c>, which keeps the seeded stream running rather than
    /// restarting it.
    /// </para>
    /// <para>
    /// <b>Host authority (§13) is preserved by omission.</b> The
    /// <see cref="ObjectiveResolver"/> lives here and nowhere else, no property on this
    /// class returns the objective's site, and the only way a mark reaches a screen is
    /// as a <c>ClueReport</c> produced from one finished read. When Mirror arrives this
    /// class becomes the host-side behaviour unchanged; the client gets the screens.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-60)]
    public sealed class MatchDirector : MonoBehaviour
    {
        /// <summary>
        /// Whole fixed steps one <see cref="StepMatch"/> may take. Mirrors
        /// <c>MonsterAgent.MaxStepsPerSimulate</c> and exists for the same reason: a
        /// host that hitches must not let the match catch up in one frame, because
        /// catching up in one frame is what teleporting through a wall looks like.
        /// </summary>
        private const int MaxStepsPerFrame = 8;

        [Header("Match")]
        [SerializeField]
        [Tooltip("§13: a match replays from its seed. Changing it changes the whole layout.")]
        private int _seed = 20260731;

        [SerializeField]
        [Tooltip("Which of §04's five the local player is. §08's 금고 asks; §04's 질주 asks.")]
        private RoleId _localRole = RoleId.Runner;

        [SerializeField]
        [Tooltip("Begin the match on Start. Off when a test drives BeginMatch itself.")]
        private bool _autoStart = true;

        [Header("Wiring")]
        [SerializeField]
        [Tooltip("Left empty, the first MonsterAgent in the scene is used.")]
        private MonsterAgent? _monster;

        [SerializeField]
        [Tooltip("The local player's rig root. Left empty, the first PlayerMotor in the scene is used.")]
        private Transform? _playerRoot;

        private readonly Wallet _wallet = new Wallet();
        private readonly ClueReader _clueReader = new ClueReader();
        private readonly DroppedLootField _droppedLoot = new DroppedLootField();
        private readonly List<Light> _areaLights = new List<Light>();

        private MatchClock _clock = new MatchClock();
        private Shop? _shop;
        private LocalShopRequests? _shopRequests;
        private MatchState? _state;
        private MatchMap? _map;
        private ObjectiveResolver? _resolver;
        private DeterministicRandom? _rng;
        private NavMeshWorldProbe? _probe;

        private MatchHud? _hud;
        private PlayerInteractor? _interactor;
        private PlayerMotor? _motor;
        private PlayerLoadout? _loadout;
        private PlayerFlashlight? _flashlight;
        private PlayerInputRouter? _input;
        private PlayerLook? _look;

        private GameObject? _worldRoot;
        private ObjectivePropInteractable? _objectiveProp;
        private SurfaceApron? _apron;

        private ClueReadContext _clueContext;
        private int _revealedClueId = -1;
        private double _accumulator;
        private bool _running;
        private bool _onSurface = true;
        private float _grabDistance;
        private int _activeSeed;

        /// <summary>§07's clock. Never paused — that is the section.</summary>
        public MatchClock Clock
        {
            get { return _clock; }
        }

        /// <summary>The party, the objective and §09's ghosts. Null before <see cref="BeginMatch"/>.</summary>
        public MatchState? State
        {
            get { return _state; }
        }

        /// <summary>§08's one shared wallet and the stock bought out of it.</summary>
        public Shop Shop
        {
            get { return _shop ??= new Shop(_wallet); }
        }

        /// <summary>The building §03's chain narrows over. Null before <see cref="BeginMatch"/>.</summary>
        public MatchMap? Map
        {
            get { return _map; }
        }

        /// <summary>This player's §03 read attempt. Holds no clue content.</summary>
        public ClueReader ClueReader
        {
            get { return _clueReader; }
        }

        /// <summary>§08's 사망자의 전리품 — what fell where.</summary>
        public DroppedLootField DroppedLoot
        {
            get { return _droppedLoot; }
        }

        /// <summary>Clues this match has, §03's three plus any decoys. Zero before the match starts.</summary>
        public int ClueCount
        {
            get { return _resolver != null ? _resolver.ClueCount : 0; }
        }

        /// <summary>Round trips this layout was built to cost, inside §03's 2–5. A difficulty, not a location.</summary>
        public int PlannedRoundTrips
        {
            get { return _resolver != null ? _resolver.PlannedRoundTrips : 0; }
        }

        /// <summary>The objective in the world, or null once it has left the building.</summary>
        public ObjectivePropInteractable? ObjectiveProp
        {
            get { return _objectiveProp; }
        }

        /// <summary>
        /// §01's 지상, as painted geometry. Null before <see cref="BeginMatch"/> and on a
        /// scene the apron could find no floor in.
        /// </summary>
        public SurfaceApron? Apron
        {
            get { return _apron; }
        }

        /// <summary>The seat the person at the keyboard is in.</summary>
        public int LocalPlayerIndex
        {
            get { return 0; }
        }

        /// <summary>Which of §04's five the local player took.</summary>
        public RoleId LocalRole
        {
            get { return _localRole; }
        }

        /// <summary>Whether the match is being stepped.</summary>
        public bool IsRunning
        {
            get { return _running; }
        }

        /// <summary>Whether the local player is standing in §01's 지상 apron.</summary>
        public bool LocalPlayerOnSurface
        {
            get { return _onSurface; }
        }

        /// <summary>Whether §08's shop panel is up.</summary>
        public bool ShopOpen
        {
            get { return _hud != null && _hud.ShopOpen; }
        }

        /// <summary>
        /// §02's reading of the match right now, over the seats that are actually being
        /// played.
        /// <para>
        /// A solo playtest seats one person in a party §11 sizes at four, and
        /// <c>MatchState</c> will not build a lineup with an empty slot. Feeding the
        /// three empty seats to §02 would make a death read as 생존 — three phantom
        /// escapees — which is precisely the asymmetry §02 exists to price. So the tally
        /// counts occupied seats only and hands them to Core's evaluator unchanged.
        /// <c>OutcomeEvaluator</c> already reads 완전 승리 as "nobody was lost" rather
        /// than "four escaped" so that a short-handed party is judged by the same rule.
        /// </para>
        /// </summary>
        public MatchResolution Resolution
        {
            get
            {
                var state = _state;
                if (state == null)
                {
                    return OutcomeEvaluator.Evaluate(0, 0, 0, false);
                }

                var escaped = 0;
                var lost = 0;
                var inPlay = 0;

                for (var i = 0; i < OccupiedSeats; i++)
                {
                    var player = state.PlayerAt(i);
                    if (player.HasEscaped)
                    {
                        escaped++;
                    }
                    else if (player.IsGhost)
                    {
                        lost++;
                    }
                    else
                    {
                        inPlay++;
                    }
                }

                return OutcomeEvaluator.Evaluate(escaped, lost, inPlay, state.ObjectiveRecovered);
            }
        }

        /// <summary>§02's verdict, or <see cref="MatchOutcome.InProgress"/>.</summary>
        public MatchOutcome Outcome
        {
            get { return Resolution.Outcome; }
        }

        /// <summary>
        /// Lays out and starts a match. Idempotent in the sense that it tears down
        /// whatever the previous one left in the world first.
        /// </summary>
        /// <param name="seed">§13: the whole layout replays from this.</param>
        /// <returns>False when the scene could not carry a match; the reason is logged.</returns>
        public bool BeginMatch(int seed)
        {
            ClearWorld();
            ResolveWiring();

            if (!MatchMap.TryRead(out var map, out var failure) || map == null)
            {
                Debug.LogError("[Match] " + failure, this);
                return false;
            }

            _map = map;
            _activeSeed = seed;
            _rng = new DeterministicRandom(seed);
            _worldRoot = new GameObject("MatchWorld");

            CollectAreaLights();
            var probe = PrepareMonster();
            if (probe == null)
            {
                Debug.LogError(
                    "[Match] No NavMesh probe. §06's monster and §03's placement both walk through "
                    + "NavMesh queries; regenerate the map so the surface is baked.", this);
                return false;
            }

            _probe = probe;

            try
            {
                _resolver = new ObjectiveResolver(map.Catalog, probe, map.TeamEntryPoint.ToVec3(), _rng);
            }
            catch (ArgumentException error)
            {
                Debug.LogError("[Match] §03's chain cannot be laid out on this map: " + error.Message, this);
                return false;
            }

            if (!_resolver.VerifyChainConverges())
            {
                // ObjectiveResolver's own argument: a layout that does not converge is
                // unwinnable, and a match is far better off refusing to start than
                // sending players down for something that is not there.
                Debug.LogError(
                    "[Match] §03's clue chain does not converge on one site for seed " + seed
                    + ". Refusing to start.", this);
                return false;
            }

            if (_resolver.UsedUnreachableFallback)
            {
                Debug.LogWarning(
                    "[Match] No candidate site was reachable from the 출입구; the layout fell back to the "
                    + "level's declared positions. The NavMesh and the map data disagree.", this);
            }

            PlaceClues();
            PlaceObjective();
            PlaceLoot();
            PlaceVehicle();

            _shop = new Shop(_wallet);
            _shopRequests = new LocalShopRequests(_shop, _loadout != null ? _loadout.Inventory : null);

            _state = new MatchState(BuildLineup(_localRole), startOnSurface: true);
            _clock = new MatchClock(startOnSurface: true);
            _clueContext = default(ClueReadContext);
            _clueReader.Cancel();
            _revealedClueId = -1;
            _accumulator = 0d;

            if (_motor != null)
            {
                _motor.Role = _localRole;
            }

            MovePlayerToSpawn();
            _onSurface = true;
            _grabDistance = MeasureGrabDistance();

            BindHud();

            // Closed to start with, and closed on every 귀환 after it. §08: "1차 잠입 전
            // 구매력 0 — 맨몸으로 들어간다", so there is nothing to decide yet, and a panel
            // over the screen would take the camera away from a player whose first job is
            // to walk out of the door. The 차량's key is the only thing that opens it —
            // see the remarks on Surfaced.
            CloseShopAtVehicle();

            _running = true;

            Debug.Log(
                "[Match] seed " + seed
                + " · " + _resolver.ClueCount + " clues (§03 needs " + GameConstants.CluesRequiredToLocate + ")"
                + " · planned round trips " + _resolver.PlannedRoundTrips
                + " · " + map.ZoneCount + " zones"
                + " · local role " + _localRole, this);

            return true;
        }

        /// <summary>Stops stepping and unbinds the HUD. The world is left standing.</summary>
        public void EndMatch()
        {
            _running = false;
            _hud?.UnbindHud();
            _hud?.CloseShop();
            _hud?.DismissClue();
        }

        /// <summary>
        /// Advances the match in whole <see cref="GameConstants.FixedStep"/> steps.
        /// <para>
        /// Public and delta-taking so a headless test can drive a whole match without a
        /// frame ever being rendered — the same reason <c>PlayerMotor.Step</c> and
        /// <c>MonsterAgent.Simulate</c> are shaped this way (ARCHITECTURE §3).
        /// </para>
        /// </summary>
        public void StepMatch(float deltaSeconds)
        {
            if (!_running)
            {
                return;
            }

            if (deltaSeconds > 0f && !float.IsNaN(deltaSeconds) && !float.IsInfinity(deltaSeconds))
            {
                _accumulator += deltaSeconds;
            }

            var steps = 0;
            while (_accumulator >= GameConstants.FixedStep && steps < MaxStepsPerFrame && _running)
            {
                StepFixed();
                _accumulator -= GameConstants.FixedStep;
                steps++;
            }

            if (steps >= MaxStepsPerFrame)
            {
                // Drop the backlog rather than run it. See MaxStepsPerFrame.
                _accumulator = 0d;
            }
        }

        /// <summary>
        /// Takes this frame's measurement of §03's reading conditions from the player's
        /// eyes. A context with <c>ClueId &lt; 0</c> is what tells <c>ClueReader</c> the
        /// player looked away, so it is pushed every frame and not only when a mark is
        /// in view.
        /// </summary>
        public void SetClueContext(ClueReadContext context)
        {
            _clueContext = context;
        }

        /// <summary>Whether a point is under a zone light or the burning 출입구 light. §03.</summary>
        public bool IsAreaLit(Vector3 point)
        {
            for (var i = 0; i < _areaLights.Count; i++)
            {
                var light = _areaLights[i];
                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if ((light.transform.position - point).sqrMagnitude <= light.range * light.range)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Puts §03's objective in the local player's hands.
        /// <para>
        /// Two core objects have to move together: <c>Inventory</c> refuses while there
        /// is 전리품 in the pockets ("전리품 동시 소지 불가") and <c>MatchState</c> refuses
        /// a second carrier and records who has it. Taking one without the other would
        /// leave the match and the player's hands disagreeing about §03.
        /// </para>
        /// </summary>
        public bool TryTakeObjective(out string refusal)
        {
            var state = _state;
            var loadout = _loadout;
            if (state == null || loadout == null)
            {
                refusal = "이 판에는 목표물을 들 손이 없다.";
                return false;
            }

            SyncLootState();

            if (!loadout.SetCarryingObjective(true))
            {
                refusal = "§03 전리품 동시 소지 불가 — 들고 있는 전리품을 먼저 처리해야 한다.";
                return false;
            }

            if (!state.TryTakeObjective(LocalPlayerIndex))
            {
                loadout.SetCarryingObjective(false);
                refusal = "§03 지금은 목표물을 들 수 없다.";
                return false;
            }

            SyncLootState();
            refusal = string.Empty;
            return true;
        }

        /// <summary>Puts §03's objective down where the carrier is standing.</summary>
        public bool TryDropObjective(Vector3 where, out string refusal)
        {
            var state = _state;
            if (state == null || !state.TryDropObjective(where.ToVec3()))
            {
                refusal = "아무도 들고 있지 않다.";
                return false;
            }

            _loadout?.SetCarryingObjective(false);
            SyncLootState();
            refusal = string.Empty;
            return true;
        }

        /// <summary>
        /// §02 목표물 회수 — it left the building with somebody, so it leaves the world.
        /// <para>
        /// Taking the prop out of the world is only half of it: §03's carry is a flag on
        /// <c>Inventory</c>, put there by <see cref="TryTakeObjective"/>, and deactivating
        /// a <c>GameObject</c> cannot reach it. Left set, it keeps charging
        /// <see cref="GameConstants.ObjectiveWeight"/> against the load and keeps
        /// <c>MovementContext.CarryingObjective</c> true for a pair of hands that is
        /// demonstrably empty — the match is over and the thing is on the van.
        /// </para>
        /// <para>
        /// The interactor's fallback focus goes with it. <c>SetCarriedFocus</c> is what
        /// keeps the key bound to something whose collider is off while it is held; the
        /// prop this points at is about to be inactive, and after the next
        /// <see cref="BeginMatch"/> despawns the world it is destroyed.
        /// </para>
        /// </summary>
        private void RecoverObjective()
        {
            var prop = _objectiveProp;
            if (prop == null)
            {
                return;
            }

            _interactor?.ClearCarriedFocus(prop);
            prop.Recovered();
            _loadout?.SetCarryingObjective(false);
        }

        /// <summary>
        /// Opens §08's shop, if the team is at the vehicle. The one screen operated with
        /// a mouse, so the cursor comes back — but nothing is paused: §07 charges "상점
        /// 에서 고민 ~30초" and §10 lists shopping as a dilemma against the clock.
        /// </summary>
        public void OpenShopScreen()
        {
            if (_onSurface)
            {
                OpenShopAtVehicle();
            }
        }

        /// <summary>Closes §08's shop and gives the mouse back to §05's camera.</summary>
        public void CloseShopScreen()
        {
            CloseShopAtVehicle();
        }

        /// <summary>
        /// §02's decision: leave for good. Everything the team learned survives, and the
        /// match is over for this player.
        /// <para>
        /// No key is bound to this yet. §02's 생존 row is a team negotiation — "한 명
        /// 이라도 탈출하면 그 판에서 알아낸 정보가 보존된다" — and binding it to a single
        /// press at the van would let one mistimed keystroke end four people's match.
        /// The host calls it; §13's lobby layer is where the vote to leave belongs.
        /// Carrying the objective into the apron ends the match on its own (see
        /// <see cref="Surfaced"/>), which is §02's other terminal row.
        /// </para>
        /// </summary>
        public bool TryLeaveForGood(out string refusal)
        {
            var state = _state;
            if (state == null)
            {
                refusal = "판이 시작되지 않았다.";
                return false;
            }

            if (!_onSurface)
            {
                refusal = "§08 차량은 지상에 있다.";
                return false;
            }

            if (!state.TryExtract(LocalPlayerIndex))
            {
                refusal = "이미 나갔다.";
                return false;
            }

            if (state.ObjectiveRecovered)
            {
                RecoverObjective();
            }

            refusal = string.Empty;
            CheckOutcome();
            return true;
        }

        /// <summary>
        /// Keeps <c>PlayerState</c>'s view of the hands in step with the economy's.
        /// Called by an interactable the instant it changes anything, because §03's
        /// "no 전리품 while carrying" is checked in both objects and a one-frame
        /// disagreement is a rule that fires at the wrong time.
        /// </summary>
        public void NoteLootTaken()
        {
            SyncLootState();
            _hud?.RefreshShop();
        }

        private void Awake()
        {
            ResolveWiring();
        }

        private void Start()
        {
            if (_autoStart && !_running)
            {
                BeginMatch(_seed);
            }
        }

        private void FixedUpdate()
        {
            StepMatch(Time.fixedDeltaTime);
        }

        // ------------------------------------------------------------------
        // The step. Order matters and is the same every tick — §13's replay
        // guarantee is a property of this list, not of any one system in it.
        // ------------------------------------------------------------------

        private void StepFixed()
        {
            var state = _state;
            if (state == null)
            {
                return;
            }

            _clock.Tick(GameConstants.FixedStep);
            state.Tick(GameConstants.FixedStep);

            UpdatePhase();
            SyncLootState();

            var tier = _clock.Tier;
            if (_flashlight != null)
            {
                // §07's 심야 row: 손전등 반경 −30%. Pushed in as a float so the player
                // rig never imports the threat system (ARCHITECTURE §3).
                _flashlight.TierRangeMultiplier = tier.FlashlightRangeMultiplier;
            }

            // §07: "시각은 지상에서만 알 수 있다" — unless §08's 회중시계 was bought.
            _clock.SetPocketWatchOwned(Shop.StockOf(ShopItemId.PocketWatch) > 0);

            if (_clock.ConsumeMonsterReset())
            {
                ResetMonster();
            }

            StepMonster();
            StepClueRead();
            CheckGrab();

            while (_clock.TryDequeueTierAdvance(out var crossed))
            {
                Debug.Log("[Match] §07 " + crossed.Phase + " — 괴물 " + crossed.MonsterSpeed + " m/s"
                    + (crossed.MonsterKnowsExit ? ", 출입구를 안다" : string.Empty), this);
            }

            CheckOutcome();
        }

        private void StepMonster()
        {
            var monster = _monster;
            if (monster == null)
            {
                return;
            }

            // §07 is the only thing that sets the monster's speed and patrol scope, and
            // MonsterAgent reads both off the tier for the elapsed time the host gives
            // it. Handing it the clock rather than a speed keeps one authority.
            monster.SetMatchElapsedSeconds(_clock.ElapsedSeconds);
            if (_map != null)
            {
                monster.SetMapZoneCount(_map.ZoneCount);
            }

            if (_onSurface || _playerRoot == null)
            {
                // §01 · §08 make the surface a 안전 지대, and §03's partial reset leaves
                // the monster in the building when the team walks out. Reporting a
                // target that is standing at the van would have it hunt the safe zone.
                monster.ForgetTarget(LocalPlayerIndex);
            }
            else
            {
                // The true position, every tick. §06's perception rules are in Core and
                // decide what the monster can actually see.
                monster.ReportTarget(LocalPlayerIndex, _playerRoot.position);
            }

            monster.Simulate(GameConstants.FixedStep);
        }

        private void StepClueRead()
        {
            // A finished read that the player has looked away from is thrown away. §03
            // keeps no record: "그 자리에서 보고, 기억해서, 말로 전달해야 한다."
            if (_clueReader.State == ClueReadState.Complete && _clueContext.ClueId != _clueReader.ClueId)
            {
                _clueReader.Cancel();
                _revealedClueId = -1;
                _hud?.DismissClue();
            }

            _clueReader.Tick(GameConstants.FixedStep, _clueContext);

            switch (_clueReader.State)
            {
                case ClueReadState.Complete:
                    if (_revealedClueId != _clueReader.ClueId)
                    {
                        _revealedClueId = _clueReader.ClueId;
                        _hud?.ShowClueRevealed(ResolveRead(_clueReader.ClueId));
                    }

                    break;

                case ClueReadState.Reading:
                case ClueReadState.Interrupted:
                    _revealedClueId = -1;
                    _hud?.ShowClueProgress(_clueReader);
                    break;

                default:
                    _revealedClueId = -1;
                    _hud?.DismissClue();
                    break;
            }
        }

        /// <summary>
        /// The one channel clue content takes out of the host: what this reader, in
        /// these conditions, believes this mark says. §03's misread model has already
        /// had its say by the time this returns.
        /// </summary>
        private ClueReport ResolveRead(int clueId)
        {
            var resolver = _resolver;
            var rng = _rng;
            if (resolver == null || rng == null)
            {
                return ClueReport.Illegible(clueId, ClueLayer.None);
            }

            resolver.TryRead(clueId, _clueReader.Observation, rng, out var report);
            return report;
        }

        /// <summary>
        /// §06 gives the state machine five states and a catch is not one of them, so
        /// whether a player has been caught is the host's rule. The distance is the two
        /// bodies touching — the monster's own agent radius plus the player's controller
        /// radius — which is rig geometry rather than a tuned value, and it is only
        /// checked in 추격 because §06's other four states are not an attack.
        /// </summary>
        private void CheckGrab()
        {
            var state = _state;
            var monster = _monster;
            if (state == null || monster == null || _playerRoot == null || _onSurface || _grabDistance <= 0f)
            {
                return;
            }

            if (monster.State != MonsterStateId.Chase)
            {
                return;
            }

            var separation = _playerRoot.position - monster.transform.position;
            separation.y = 0f;
            if (separation.sqrMagnitude > _grabDistance * _grabDistance)
            {
                return;
            }

            var where = _playerRoot.position;
            monster.PlayGrab();
            DropEverything(where);

            if (state.TryKill(LocalPlayerIndex, where.ToVec3()))
            {
                // §08: "사망자의 전리품 — 떨어진다." The pile stays where it fell so the
                // team has a reason to come back for it.
                Debug.Log("[Match] §09 잡혔다 — " + where, this);
                monster.ForgetTarget(LocalPlayerIndex);
            }

            CheckOutcome();
        }

        private void CheckOutcome()
        {
            if (!_running)
            {
                return;
            }

            var resolution = Resolution;
            if (!resolution.IsFinal)
            {
                return;
            }

            EndMatch();

            if (_input != null)
            {
                _input.InputSuppressed = true;
                _input.LockCursor = false;
            }

            if (_look != null)
            {
                _look.LookLocked = true;
            }

            var nextSeed = _activeSeed + 1;
            _hud?.ShowEnd(resolution, () =>
            {
                _hud?.HideEnd();
                if (_input != null)
                {
                    _input.InputSuppressed = false;
                    _input.LockCursor = true;
                }

                if (_look != null)
                {
                    _look.LookLocked = false;
                }

                BeginMatch(nextSeed);
            });

            Debug.Log("[Match] §02 " + resolution.Outcome
                + " — 탈출 " + resolution.PlayersEscaped
                + ", 잃음 " + resolution.PlayersLost
                + ", 목표물 " + (resolution.ObjectiveRecovered ? "회수" : "미회수")
                + ", 정보 " + (resolution.InformationKept ? "보존" : "소실")
                + " · " + _clock.ElapsedSeconds.ToString("0") + "s", this);
        }

        // ------------------------------------------------------------------
        // §01's round trip.
        // ------------------------------------------------------------------

        private void UpdatePhase()
        {
            var map = _map;
            if (map == null || _playerRoot == null)
            {
                return;
            }

            var onSurface = map.IsOnSurface(_playerRoot.position);
            if (onSurface == _onSurface)
            {
                return;
            }

            _onSurface = onSurface;

            if (onSurface)
            {
                Surfaced();
            }
            else
            {
                Descended();
            }
        }

        /// <summary>
        /// §01's 귀환. Everything §08 says happens <em>to the loot</em> at the van happens
        /// here, because §08 describes loading it into the vehicle as the consequence of
        /// arriving rather than as an errand — "전리품을 차량에 실으면 → 가치만큼 크레딧".
        /// <para>
        /// <b>The shop is not one of those things, and this is the line that used to open
        /// it.</b> Walking into the apron is a movement, not a request: §01 makes coming
        /// up a deliberate act, and putting a mouse-driven panel over the screen on a
        /// position test took the camera away from a player who had only walked past the
        /// van — 갑자기 상점이 열림. §08 puts the shop <em>at the 차량</em>, so the 차량 is
        /// what opens it, on <c>SurfaceVehicleInteractable</c>'s key. Selling stays here:
        /// it is the section's own wording, it costs nothing, and it takes nothing away.
        /// </para>
        /// </summary>
        private void Surfaced()
        {
            var state = _state;
            if (state == null)
            {
                return;
            }

            state.PlayerAt(LocalPlayerIndex).TrySurface();
            _clock.SetTeamOnSurface(true);

            // §01's 숨 돌리기, said by the apron rather than by a line of text. The lamps
            // over the threshold swell as the player walks under them; the ambience bed
            // and the 귀환 cue are MatchAudioBridge's half of the same edge.
            _apron?.SetOnSurface(true);

            SellOversizeCarry();

            var sold = Shop.SellAll(_loadout != null ? _loadout.Inventory : null);
            if (sold > 0)
            {
                Debug.Log("[Match] §08 전리품 " + sold + " 크레딧 — 팀 지갑 " + Shop.Wallet.Credits, this);
            }

            SyncLootState();
            Resupply();

            if (state.PlayerAt(LocalPlayerIndex).IsCarryingObjective)
            {
                // §02: "목표물 회수 + 탈출." Reaching the way out with it in both hands
                // is the match; there is nothing left to decide.
                if (state.TryExtract(LocalPlayerIndex))
                {
                    RecoverObjective();
                }
            }

            // Said rather than shown, because the shop no longer shows itself. §03 rules
            // out a HUD marker, so the arrival line and the 차량's own prompt are the two
            // places a player can learn that the key is what opens it.
            Debug.Log(
                "[Match] §01 귀환 — 전리품은 차량에 실렸다. 상점은 차량을 보고 ["
                + PlayerInteractor.InteractKeyLabel + "] — §07 시계는 계속 간다.", this);

            CheckOutcome();
        }

        /// <summary>§01's 잠입. The clock does not care, which is §07's whole point.</summary>
        private void Descended()
        {
            _state?.PlayerAt(LocalPlayerIndex).TryDescend();
            _clock.SetTeamOnSurface(false);

            // The warmth draws back behind the player as they step over the line. §01
            // calls this the commitment, and until the apron existed it was the one
            // moment in the loop that changed nothing anybody could see.
            _apron?.SetOnSurface(false);

            CloseShopAtVehicle();
        }

        /// <summary>
        /// §03's 보충 column, at the van. The generator tops the installed cell back up
        /// for free (that is what makes a trip worth §07's minute even with an empty
        /// bag); spare cells only ever come from §08's shop, and the two upgrades that
        /// are worn rather than consumed are put on here.
        /// </summary>
        private void Resupply()
        {
            var shop = Shop;

            if (_flashlight != null)
            {
                var battery = _flashlight.State.Battery;

                while (shop.TryConsume(ShopItemId.Battery))
                {
                    battery.AddCells(1);
                }

                battery.Recharge();

                if (shop.StockOf(ShopItemId.UpgradedFlashlight) > 0 && !_flashlight.State.IsUpgraded)
                {
                    // §08's flagship, and its 대가 comes with it: the monster notices it
                    // from twice as far. FlashlightState applies both halves.
                    shop.TryConsume(ShopItemId.UpgradedFlashlight);
                    _flashlight.State.SetUpgraded(true);
                }
            }

            if (_loadout != null)
            {
                shop.TryEquipBag(_loadout.Inventory);
            }

            _state?.PlayerAt(LocalPlayerIndex).TryTreatInjury();
        }

        private void SellOversizeCarry()
        {
            var carried = _interactor != null ? _interactor.CarriedFocus as OversizeLootInteractable : null;
            if (carried == null || carried.Carry == null || _interactor == null)
            {
                return;
            }

            var value = Shop.SellCarried(carried.Carry);
            _interactor.SetOversizeCarry(carried, carrying: false);
            Interactable.Despawn(carried.gameObject);

            if (value > 0)
            {
                Debug.Log("[Match] §08 대형 전리품 " + value + " 크레딧 — 팀 지갑 " + Shop.Wallet.Credits, this);
            }
        }

        private void OpenShopAtVehicle()
        {
            var hud = _hud;
            var requests = _shopRequests;
            if (hud == null || requests == null || _state == null)
            {
                return;
            }

            hud.OpenShop(Shop, requests, _state.Roles.Slots, _state.MissingRole);

            // §08's shop is the one screen operated with a mouse, so the cursor comes
            // back — but nothing is paused (§07: "상점에서 고민 ~30초" is a cost) and the
            // feet stay free. Only aiming is pinned, because a cursor that has left the
            // window would otherwise fling the view.
            if (_input != null)
            {
                _input.LockCursor = false;
            }

            if (_look != null)
            {
                _look.LookLocked = true;
            }
        }

        private void CloseShopAtVehicle()
        {
            _hud?.CloseShop();

            if (_input != null)
            {
                _input.LockCursor = true;
            }

            if (_look != null)
            {
                _look.LookLocked = false;
            }
        }

        // ------------------------------------------------------------------
        // Layout.
        // ------------------------------------------------------------------

        private void PlaceClues()
        {
            var resolver = _resolver;
            if (resolver == null || _worldRoot == null)
            {
                return;
            }

            var markers = resolver.Markers;
            for (var i = 0; i < markers.Count; i++)
            {
                CluePropInteractable.Spawn(
                    markers[i].ClueId, markers[i].Position.ToVector3(), _worldRoot.transform);
            }
        }

        private void PlaceObjective()
        {
            var resolver = _resolver;
            var root = _worldRoot;
            if (resolver == null || root == null)
            {
                return;
            }

            // A push rather than a getter: nothing on this class returns the objective's
            // position, so there is nothing for a serialiser or an inspector to find.
            resolver.TryPlaceObjective(position =>
            {
                _objectiveProp = ObjectivePropInteractable.Spawn(position.ToVector3(), root.transform);
            });
        }

        private void PlaceLoot()
        {
            var map = _map;
            var rng = _rng;
            var root = _worldRoot;
            if (map == null || rng == null || root == null)
            {
                return;
            }

            var placements = MatchPlacement.DrawLoot(map.LootSpawns, rng);
            for (var i = 0; i < placements.Length; i++)
            {
                var placement = placements[i];
                if (placement.Loot == LootId.None)
                {
                    continue;
                }

                if (placement.InSafe)
                {
                    LootSafeInteractable.Spawn(placement.Position, root.transform);
                    continue;
                }

                if (LootCatalogue.Of(placement.Loot).AllowsSharedCarry)
                {
                    OversizeLootInteractable.Spawn(placement.Loot, placement.Position, root.transform);
                    continue;
                }

                LootPropInteractable.Spawn(placement.Loot, placement.Position, root.transform);
            }
        }

        /// <summary>
        /// §08's 차량 and the ground it is parked on.
        /// <para>
        /// The two belong in one step because they are one place. §08 calls the vehicle
        /// "안전 지대 + 상점 + 보급소" and §01 makes the ground around it the half of the
        /// loop that is safe; <see cref="SurfaceApron"/> is that ground given an edge,
        /// and it takes the van so it can hang the headlamps and the beacon on it. Both
        /// go under the match's world root, so <see cref="ClearWorld"/> takes them
        /// together.
        /// </para>
        /// </summary>
        private void PlaceVehicle()
        {
            var map = _map;
            var root = _worldRoot;
            if (map == null || root == null)
            {
                return;
            }

            var vehicle = SurfaceVehicleInteractable.Spawn(map.Entrance, root.transform);
            var body = vehicle != null ? vehicle.gameObject : null;

            // §12 marks the 출입구 on a stairwell cell in a 2.2 m service corridor and the
            // van is 2.81 m wide, so spawning it there stood it inside the brickwork. It
            // is moved to the nearest place inside §01's 지상 that its own footprint fits
            // — see SurfaceApron.Park.
            SurfaceApron.Park(body, map.Entrance, MatchMap.SurfaceRadius);

            _apron = SurfaceApron.Build(map.Entrance, MatchMap.SurfaceRadius, body, root.transform);
        }

        private void MovePlayerToSpawn()
        {
            var map = _map;
            if (map == null || _playerRoot == null || map.PlayerSpawns.Count == 0)
            {
                return;
            }

            var spawn = map.PlayerSpawns[0];
            var controller = _playerRoot.GetComponent<CharacterController>();

            // The controller has to be off while the transform is written, or it
            // overwrites the move on its own next step.
            if (controller != null)
            {
                controller.enabled = false;
            }

            _playerRoot.SetPositionAndRotation(spawn.position, spawn.rotation);

            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        /// <summary>
        /// §11's lineup. Four distinct roles with the local player's own among them, so
        /// exactly one of §04's five is missing — "그게 그 판의 성격이 된다."
        /// </summary>
        private static RoleSelection BuildLineup(RoleId localRole)
        {
            var roles = new List<RoleId> { localRole == RoleId.None ? RoleId.Runner : localRole };
            var all = RoleSelection.AllRoles;

            for (var i = 0; i < all.Count && roles.Count < GameConstants.PlayersPerMatch; i++)
            {
                if (!roles.Contains(all[i]))
                {
                    roles.Add(all[i]);
                }
            }

            return RoleSelection.FromRoles(roles.ToArray());
        }

        /// <summary>
        /// Seats a person is actually sitting in. One, until Mirror arrives — §13 keeps
        /// host authority and the network layer is a later step (§14).
        /// </summary>
        private const int OccupiedSeats = 1;

        // ------------------------------------------------------------------
        // Wiring.
        // ------------------------------------------------------------------

        private void ResolveWiring()
        {
            if (_monster == null)
            {
                _monster = FindFirstObjectByType<MonsterAgent>();
            }

            if (_motor == null)
            {
                _motor = FindFirstObjectByType<PlayerMotor>();
            }

            if (_playerRoot == null && _motor != null)
            {
                _playerRoot = _motor.transform;
            }

            if (_playerRoot != null)
            {
                if (_loadout == null)
                {
                    _loadout = _playerRoot.GetComponentInChildren<PlayerLoadout>();
                }

                if (_flashlight == null)
                {
                    _flashlight = _playerRoot.GetComponentInChildren<PlayerFlashlight>();
                }

                if (_input == null)
                {
                    _input = _playerRoot.GetComponentInChildren<PlayerInputRouter>();
                }

                if (_look == null)
                {
                    _look = _playerRoot.GetComponentInChildren<PlayerLook>();
                }

                if (_interactor == null)
                {
                    _interactor = _playerRoot.GetComponentInChildren<PlayerInteractor>();
                    if (_interactor == null)
                    {
                        _interactor = _playerRoot.gameObject.AddComponent<PlayerInteractor>();
                    }
                }

                _interactor.Bind(this, _motor, _loadout, _flashlight);
            }

            if (_hud == null)
            {
                _hud = GetComponentInChildren<MatchHud>();
                if (_hud == null)
                {
                    var child = new GameObject("MatchHud");
                    child.transform.SetParent(transform, worldPositionStays: false);
                    _hud = child.AddComponent<MatchHud>();
                }
            }
        }

        private void BindHud()
        {
            _hud?.BindHud(
                _localRole,
                _clock,
                _loadout != null ? _loadout.Inventory : null,
                _flashlight != null ? _flashlight.State : null,
                _motor != null ? _motor.Stamina : null);
        }

        /// <summary>
        /// Starts §06's monster on the host's seeded stream and takes its probe, which
        /// is the same <see cref="IWorldProbe"/> §03's placement reasons through — one
        /// answer about the world, not two.
        /// </summary>
        private NavMeshWorldProbe? PrepareMonster()
        {
            var monster = _monster;
            var rng = _rng;
            if (monster == null || rng == null)
            {
                return null;
            }

            if (_map != null && _map.MonsterSpawn != null)
            {
                monster.transform.position = _map.MonsterSpawn.position;
            }

            monster.SelfDriven = false;
            monster.Initialize(rng);
            monster.ClearTargets();

            var probe = monster.Probe;
            if (probe != null)
            {
                probe.LitQuery = IsAreaLit;
            }

            return probe;
        }

        /// <summary>
        /// §03's partial reset, monster half. The chase state, the position and the
        /// aggro go; the clock does not, and nobody here can make it.
        /// </summary>
        private void ResetMonster()
        {
            var monster = _monster;
            var map = _map;
            var rng = _rng;
            if (monster == null || map == null || rng == null)
            {
                return;
            }

            monster.ClearTargets();

            var spawn = map.MonsterSpawn != null ? map.MonsterSpawn.position : monster.transform.position;
            var facing = map.Entrance - spawn;
            facing.y = 0f;

            monster.Respawn(spawn, facing, rng);

            // Respawn rebuilds the probe, so the light query has to be re-attached or
            // §03's "is this area lit" quietly starts answering false.
            _probe = monster.Probe;
            if (_probe != null)
            {
                _probe.LitQuery = IsAreaLit;
            }

            Debug.Log("[Match] §03 부분 리셋 — 괴물의 추격 · 위치 · 어그로 초기화. 시계는 "
                + _clock.ElapsedSeconds.ToString("0") + "s 그대로.", this);
        }

        private float MeasureGrabDistance()
        {
            var monsterRadius = 0f;
            if (_monster != null)
            {
                var agent = _monster.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null)
                {
                    monsterRadius = agent.radius;
                }
            }

            var playerRadius = 0f;
            if (_playerRoot != null)
            {
                var controller = _playerRoot.GetComponent<CharacterController>();
                if (controller != null)
                {
                    playerRadius = controller.radius;
                }
            }

            return monsterRadius + playerRadius;
        }

        private void CollectAreaLights()
        {
            _areaLights.Clear();

            var lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < lights.Length; i++)
            {
                // Point lights only. §03's area sources are the Engineer's 구역 조명, a
                // 조명탄 and the burning 출입구 light; a player's own beam is a spot and
                // is already accounted for as the beam.
                if (lights[i].type == LightType.Point)
                {
                    _areaLights.Add(lights[i]);
                }
            }
        }

        private void SyncLootState()
        {
            var state = _state;
            var loadout = _loadout;
            if (state == null || loadout == null)
            {
                return;
            }

            state.PlayerAt(LocalPlayerIndex).SetLootState(
                loadout.Inventory.LootCount > 0 || loadout.CarryingOversizePiece,
                loadout.CarryingOversizePiece);
        }

        /// <summary>
        /// Empties the hands where the player fell. §08: "사망자의 전리품 — 떨어진다.
        /// 회수하려면 시체가 있는 곳으로 돌아가야 한다."
        /// </summary>
        private void DropEverything(Vector3 where)
        {
            var interactor = _interactor;

            if (_objectiveProp != null && _objectiveProp.IsCarried && interactor != null)
            {
                _objectiveProp.ForceDrop(interactor, where);
            }

            if (interactor != null && interactor.CarriedFocus is OversizeLootInteractable oversize)
            {
                oversize.ForceRelease(interactor);
            }

            if (_loadout != null)
            {
                _droppedLoot.DropFrom(_loadout.Inventory, where.ToVec3());
            }

            SyncLootState();
        }

        /// <summary>
        /// Empties the local player's hands between matches.
        /// <para>
        /// <b>The pockets are not in the world, so despawning it cannot reach them.</b>
        /// §08 pays for 전리품 at the vehicle it is carried to — "전리품을 차량에 실으면 →
        /// 가치만큼 크레딧" — and <see cref="Surfaced"/> sells whatever is in the pockets on
        /// the first 귀환 without asking which match earned it. Loot that outlived its own
        /// match is therefore free credits, and §02 already priced what a lost match is
        /// supposed to cost: 손실 drops the loot. §03's objective and §08's 대형 전리품 come
        /// off for the same reason — the props are gone, and a flag saying the hands are
        /// full of one would follow the player into a match that has neither.
        /// </para>
        /// <para>
        /// Both are released through their props rather than by clearing flags, and this
        /// runs before <see cref="ClearWorld"/>'s despawn so that there is still a prop to
        /// release: the share of an oversize piece is weight inside <c>SharedLootCarry</c>,
        /// which is the only thing that may give the hands back, and releasing is also what
        /// drops <c>PlayerInteractor</c>'s carried focus before it becomes a reference to a
        /// destroyed object.
        /// </para>
        /// </summary>
        private void ClearHands()
        {
            var interactor = _interactor;
            if (interactor != null)
            {
                if (_objectiveProp != null && _objectiveProp.IsCarried)
                {
                    _objectiveProp.ForceDrop(interactor, interactor.transform.position);
                }

                if (interactor.CarriedFocus is OversizeLootInteractable oversize)
                {
                    oversize.ForceRelease(interactor);
                }
            }

            var loadout = _loadout;
            if (loadout == null)
            {
                return;
            }

            loadout.SetCarryingObjective(false);
            loadout.SetCarryingOversizePiece(false);
            loadout.Inventory.DropAll();
        }

        private void ClearWorld()
        {
            _running = false;
            _resolver = null;
            _droppedLoot.Clear();
            _hud?.HideEnd();
            _hud?.CloseShop();
            _hud?.DismissClue();

            // Before the despawn, while the props it releases still exist.
            ClearHands();
            _objectiveProp = null;
            _apron = null;

            if (_worldRoot != null)
            {
                Interactable.Despawn(_worldRoot);
                _worldRoot = null;
            }
        }
    }
}
