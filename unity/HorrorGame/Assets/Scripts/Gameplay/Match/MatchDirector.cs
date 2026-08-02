#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Audio;
using HorrorGame.Core;
using HorrorGame.Core.Abilities;
using HorrorGame.Core.Map;
using HorrorGame.Core.Monster;
using HorrorGame.Core.Race;
using HorrorGame.Core.Voice;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Ghost;
using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Session;
using HorrorGame.Gameplay.Ghost;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using HorrorGame.Gameplay.Race;
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
    /// match's answer, one <c>ClueReader</c> per player, and — since 하강 — one §06
    /// creature per storey. It steps them, applies the results to transforms and screens,
    /// and decides nothing itself.
    /// </para>
    /// <para>
    /// <b>§12-B③ is a count, and this class is where it becomes true.</b> 「괴물이 안쪽을
    /// 순찰한다」 is written about every floor and a 투하구 is a fall, not a path, so a
    /// creature that starts on B5 patrols B5 and nowhere else. The map declares one start
    /// per storey; <c>PrepareCreatures</c> stands one agent on each of them and
    /// <c>VerifyCreatureCount</c> refuses the match if the two numbers ever differ — which
    /// is the one thing standing between a §06 audit that reads "8 of 8 storeys" and a
    /// game that still runs a single monster.
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
        [Tooltip("The rig every creature is cut from. Left empty, the first MonsterAgent in the scene is used.")]
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
        private GhostSession? _ghosts;
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

        [Header("§01 하강")]
        [SerializeField]
        [Tooltip("§01's race. Off runs the co-operative recovery match this grew out of.")]
        private bool _raceMode = true;

        /// <summary>
        /// True when this is §01's race rather than the co-operative recovery match.
        /// <para>
        /// The two share a map, a creature, a set of doors and a darkness, and share
        /// nothing else: the race has no 지상, no shop, no objective and no way out but
        /// down. Switching here rather than deleting keeps the old path readable while the
        /// new one is being proven. See BeginMatch.
        /// </para>
        /// </summary>
        public bool RaceMode
        {
            get { return _raceMode; }
        }
        private float _grabDistance;

        /// <summary>
        /// One creature: the agent, where it started, and its own §06 catch.
        /// <para>
        /// <b>The lunge is per creature and that is the reason this type exists.</b>
        /// <see cref="MonsterLunge"/> is a struct holding a 0.55 s commit and a 0.8 s
        /// recovery, and one shared copy across eight creatures would let the creature on
        /// B3 commit and the creature on B4 recover — a runner who drops through a 투하구
        /// mid-lunge would arrive to a creature that is already half way through a strike
        /// it never started. §06 gives the catch to a creature, not to a match.
        /// </para>
        /// </summary>
        private sealed class Creature
        {
            internal Creature(MonsterAgent agent, Transform? spawn, bool cloned)
            {
                Agent = agent;
                Spawn = spawn;
                Cloned = cloned;
            }

            /// <summary>The agent in the scene.</summary>
            internal MonsterAgent Agent { get; }

            /// <summary>The §07 start marker it belongs to, for §03's partial reset. Null on a scene with none.</summary>
            internal Transform? Spawn { get; }

            /// <summary>Whether this director cloned it, and therefore owns destroying it.</summary>
            internal bool Cloned { get; }

            /// <summary>§06's catch, as an act. See MonsterLunge — the rule is in Core.</summary>
            internal MonsterLunge Lunge;
        }

        /// <summary>
        /// Every creature this match is running, one per <see cref="MatchMap.MonsterSpawns"/>
        /// entry. See PrepareCreatures for what guarantees that count.
        /// </summary>
        private readonly List<Creature> _creatures = new List<Creature>();

        /// <summary>Throttle for the proximity report. See CheckGrab.</summary>
        private float _lastGrabReport;

        /// <summary>Ground covered since the last footstep was raised for §06. See TakeFootstepCue.</summary>
        private float _strideTravelled;

        private Vector3 _lastFootstepPosition;

        /// <summary>How loud the local player is. Optional — a rig without one still makes noise by speed.</summary>
        private NoiseMeter? _noise;

        /// <summary>What the local player's microphone is doing this tick. See TakeVoiceCue.</summary>
        private VoiceEffort _voiceEffort;

        /// <summary>Every 투하구 in the scene, wired at match start. §01.</summary>
        private readonly List<Chute> _chutes = new List<Chute>();

        /// <summary>§02's standings in the scene. Null outside race mode.</summary>
        private RaceDirector? _raceDirector;

        private int _activeSeed;
        private bool _endScreenHeldForGhost;

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
        /// §09's seat for the dead. Null until <see cref="ResolveWiring"/> has run; the
        /// session itself is inert while nobody has died.
        /// </summary>
        public GhostSession? Ghosts
        {
            get { return _ghosts; }
        }

        /// <summary>
        /// Whether the local player is dead and still playing. §09.
        /// <para>
        /// Not the same question as "did the match end". A death is a change of seat, not
        /// the end of anything: §09 keeps the player in the building — "탈출: 불가능" —
        /// and §02 only finishes when every seat has resolved, which with four players is
        /// three deaths later.
        /// </para>
        /// </summary>
        public bool LocalPlayerIsGhost
        {
            get { return _state != null && _state.PlayerAt(LocalPlayerIndex).IsGhost; }
        }

        /// <summary>
        /// How hard the local player is speaking right now. The voice transport sets this
        /// every tick; §06 hears the result. Silent when nobody is holding the key.
        /// </summary>
        public VoiceEffort VoiceEffort
        {
            get { return _voiceEffort; }
            set { _voiceEffort = value; }
        }

        /// <summary>
        /// Where §06's lunge is in its cycle. Read-only, and here for one reason: when
        /// the owner reported standing in front of the creature and not dying, there
        /// were four things that could have been true and no way to tell them apart from
        /// outside. <c>MonsterKillTests</c> reads this so a failure says which link was
        /// open instead of only that the player survived.
        /// </summary>
        public LungeState LungePhase
        {
            get
            {
                var here = LocalStoreyCreature();
                return here != null ? here.Lunge.State : LungeState.Ready;
            }
        }

        /// <summary>
        /// How many §06 creatures this match is actually running. §12-B③.
        /// <para>
        /// <b>The number that has to match the audit.</b> <c>NavMeshConnectivity</c> counts
        /// MonsterSpawn markers in the editor and prints 「over N of 8 storeys」; this counts
        /// the agents standing in the world and being ticked. The whole point of the pair is
        /// that a build can be caught claiming eight and running one — which is this
        /// project's signature failure and was the shape of B-001, B-008 and the race that
        /// recorded descents into an object nothing read.
        /// </para>
        /// <para>
        /// <see cref="BeginMatch"/> refuses to start a match in which this disagrees with
        /// <see cref="MatchMap.MonsterSpawns"/>, so a green run of anything is already
        /// evidence that the two agree. This property is here so a test can say the number
        /// out loud rather than infer it.
        /// </para>
        /// </summary>
        public int MonsterCount
        {
            get { return _creatures.Count; }
        }

        /// <summary>
        /// The creature on the local runner's own storey, or null when they are alone on
        /// their floor.
        /// <para>
        /// <b>Everything that can only follow one creature should follow this one.</b> §06's
        /// audio bed, §09's ghost camera and §14's guidance all took
        /// <c>FindFirstObjectByType&lt;MonsterAgent&gt;()</c> when there was exactly one to
        /// find. With one per storey that call returns an arbitrary floor's creature, so a
        /// runner on B1 could be given the 추격 bed of the creature seven floors below —
        /// the same class of defect as a HUD that disagrees with the ears (F-002).
        /// </para>
        /// </summary>
        public MonsterAgent? LocalStoreyMonster
        {
            get
            {
                var here = LocalStoreyCreature();
                return here != null ? here.Agent : null;
            }
        }

        /// <summary>
        /// Whether §02 has decided and the end screen is waiting on §09's ghost to ask
        /// for it. See <see cref="CheckOutcome"/>.
        /// </summary>
        public bool EndScreenHeldForGhost
        {
            get { return _endScreenHeldForGhost; }
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
            var probe = PrepareCreatures();
            if (probe == null)
            {
                Debug.LogError(
                    "[Match] No NavMesh probe. §06's monster and §03's placement both walk through "
                    + "NavMesh queries; regenerate the map so the surface is baked.", this);
                return false;
            }

            _probe = probe;

            // ── §12-B③ 층마다 하나, checked rather than assumed ────────────────────
            //
            // THIS IS THE LINE THAT KEEPS THE §06 AUDIT HONEST. The audit counts
            // MonsterSpawn markers in the editor and prints "over N of 8 storeys"; a map
            // that grows eight starts turns that line green whether or not anything in the
            // running game ever hunts on eight floors. So the host refuses to run a match
            // in which the two disagree: one agent per declared start, counted, or no
            // match at all. A build that ships one creature and an eight-storey audit
            // cannot get past here, and the log below prints both numbers side by side so
            // a reader never has to take either on trust.
            //
            // Refusing rather than warning, for the same reason ObjectiveResolver refuses a
            // chain that does not converge: a match played against the wrong number of
            // creatures is not a degraded match, it is a different game, and it would be
            // measured as if it were this one.
            if (!VerifyCreatureCount(map))
            {
                return false;
            }

            // ── §01's race, or the co-operative recovery match it grew out of ──────
            //
            // Everything between here and MovePlayerToSpawn is the old game: §03's clue
            // chain, the objective, the loot, the van, the shop, and the 지상 apron that
            // makes the surface a safe zone. The race has none of it. There is no way out
            // of the building — the 출입구 marker is now §02's FINISH at the middle of B8 —
            // so a surface phase is not a phase, it is a contradiction.
            //
            // GATED rather than deleted, and that is a decision worth defending. This file
            // is 2,139 lines and the co-op systems reach into most of them; cutting them out
            // on the same night five other things are being wired is how a night ends with
            // nothing that compiles. Gated, the race path is short and readable and the old
            // path is provably unreachable, and deleting it afterwards is a mechanical
            // change nobody has to be brave about. docs/DESCENT-PIVOT.md §3.
            if (_raceMode)
            {
                // §08 is NOT laid out here, and its absence is the change. DESCENT-PIVOT §3
                // lists 「§08 전리품 · 크레딧 · 판매」 under 버린다 with the reason 「통화가
                // 없다」, and §7 step 7 is 「상점/전리품/단서 제거」 — a step that had never
                // run. PlaceLoot stood at exactly this line, ABOVE MovePlayerToSpawn, while
                // PlaceClues, PlaceObjective and PlaceVehicle all sat below on the
                // co-operative path; that one misplaced call was the whole of §08 still
                // shipping in a race. It put one piece on every one of the 152 LootSpawn
                // markers of Map_FirstSketch_Solo — §12 puts one at every 막힌 길, 19 per
                // storey — and the first draw is always §01's 궤짝: a 1.276 × 0.694 m chest
                // held 1.05 m in front of the eyes, weighing GameConstants.LootWeightLargePiece
                // (5, "equals WeightFreeMax, which is why one piece ends the free band"), for
                // credits that no longer exist to spend anywhere. In a race about speed the
                // reward for taking it is that you cannot see the gate you are running at.
                //
                // The carry MECHANIC is deliberately left standing: PlayerInteractor, the
                // crosshair, the key and DropPlacement are all still wired, because F-006
                // §5c puts battery cells on the floor for runners to find and that is the
                // same path with a different thing on the end of it. What goes is §08's
                // table, not the hands.
                _state = new MatchState(BuildLineup(_localRole), startOnSurface: false);
                _clock = new MatchClock(startOnSurface: false);
                _clueContext = default(ClueReadContext);
                _clueReader.Cancel();
                _revealedClueId = -1;
                _accumulator = 0d;

                MovePlayerToSpawn();

                // Never true again for the rest of the match. §01's 지상 is where a
                // co-operative team regroups and shops; a runner starts on the rim of B1
                // with the maze in front of them and nothing behind.
                _onSurface = false;

                _grabDistance = MeasureGrabDistance();
                AttachDoors();
                AttachChutes();

                _noise = _playerRoot != null ? _playerRoot.GetComponentInChildren<NoiseMeter>(true) : null;
                _lastFootstepPosition = _playerRoot != null ? _playerRoot.position : Vector3.zero;
                _strideTravelled = 0f;

                BindHud();
                _hud?.CloseShop();
                AttachRace();

                _running = true;
                _seed = seed;

                Debug.Log("[Match] §01 하강 시작 — B1 외곽. 여덟 층 아래가 도착점이다.", this);
                return true;
            }

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
            AttachDoors();
            AttachChutes();

            // §06's ears. Optional on purpose: a rig assembled by a test has no audio on
            // it, and a monster that went deaf whenever the sound layer was absent would
            // hide the very bug this wiring exists to fix. See TakeFootstepCue.
            _noise = _playerRoot != null ? _playerRoot.GetComponentInChildren<NoiseMeter>(true) : null;
            _lastFootstepPosition = _playerRoot != null ? _playerRoot.position : Vector3.zero;
            _strideTravelled = 0f;

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
            _endScreenHeldForGhost = false;

            // §09 ends with the match and never outlives it: the ghost's cooldown is
            // ticked from the step this call stops, so a ghost left flying afterwards
            // would be one whose 45 s never comes round again.
            _ghosts?.End();

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

            if (LocalPlayerIsGhost)
            {
                // §03's read belongs to a pair of eyes that is now flying somewhere else.
                // PlayerInteractor is switched off while §09 has the camera, so nothing is
                // pushing a fresh context — and a reader ticked against the last live one
                // would finish a mark the player was halfway through when they died.
                _clueContext = default(ClueReadContext);
            }

            StepCreatures();
            PushDoors();
            StepClueRead();
            CheckGrab();
            CheckChutes();
            _raceDirector?.Tick(_clock.ElapsedSeconds);

            while (_clock.TryDequeueTierAdvance(out var crossed))
            {
                Debug.Log("[Match] §07 " + crossed.Phase + " — 괴물 " + crossed.MonsterSpeed + " m/s"
                    + (crossed.MonsterKnowsExit ? ", 출입구를 안다" : string.Empty), this);
            }

            CheckOutcome();
        }

        /// <summary>
        /// Steps every §06 creature, and tells the one the runner is actually in the
        /// building with where they are.
        /// <para>
        /// <b>Only the creature on the runner's own storey is told anything.</b> That is a
        /// host decision and it is not tidiness — §06's sight test is FLAT
        /// (<c>MonsterBrain.CanSee</c> compares <c>MagnitudeFlat</c>, dropping Y), written
        /// when the building was one floor. 하강 stacks eight floors in one column and
        /// <c>DescentMap.SeedCreature</c> posts every creature at its own storey's MIDDLE,
        /// which is the same X and Z on all eight. So a runner standing in the middle of B3
        /// is, in §06's arithmetic, nose to nose with all eight creatures at once; the only
        /// thing between them is the slab's collider, and a 투하구 is a 1.4 m hole in it.
        /// Without this filter the creature two floors down acquires a runner it can never
        /// reach (<c>NavigableDistance</c> is +∞ across storeys), never returns to 순찰, and
        /// §12-B③'s 「괴물이 안쪽을 순찰한다」 quietly stops being true on seven floors.
        /// </para>
        /// <para>
        /// <b>The same fact is what makes the filter safe.</b> A creature cannot leave its
        /// floor — §12-C makes the 투하구 one-way and not a NavMeshLink, and the NavMesh
        /// audit reads <em>islands 8</em>, one per storey — so a creature on another floor
        /// could do nothing with the report if it had it. The measurement that would refute
        /// this is that audit line: if islands ever drops below 8, two storeys have been
        /// joined, a creature CAN follow a runner down, and this filter becomes a lie.
        /// </para>
        /// <para>
        /// Storey is asked with <see cref="MapGraph.StoreyChangeMetres"/>, the same
        /// constant <c>MatchMap.IsOnSurface</c> uses, because 지상, 층 and §06 must not
        /// answer "is this the same floor" three different ways.
        /// </para>
        /// </summary>
        private void StepCreatures()
        {
            if (_creatures.Count == 0)
            {
                return;
            }

            var hunted = _onSurface || _playerRoot == null || LocalPlayerIsGhost
                ? null
                : LocalStoreyCreature();

            // Taken once for the whole match, not once per creature. The stride is a
            // property of the player's legs — ReportFootsteps used to advance an
            // accumulator as a side effect of reporting, so a loop over eight creatures
            // would have given the first one the step and the other seven silence.
            //
            // Taken even when nobody is listening, which is the other half of the same
            // point: the accumulator measures ground covered, and letting it run on
            // while the runner crosses a floor that has no creature would bank the whole
            // storey and spend it as one enormous footstep the instant they landed on a
            // floor that does. A step is a step; who hears it is decided below.
            var footstep = TakeFootstepCue();
            var voice = hunted != null ? TakeVoiceCue() : null;

            for (var i = 0; i < _creatures.Count; i++)
            {
                var creature = _creatures[i];
                var monster = creature.Agent;
                if (monster == null)
                {
                    continue;
                }

                // §07 is the only thing that sets the monster's speed and patrol scope, and
                // MonsterAgent reads both off the tier for the elapsed time the host gives
                // it. Handing it the clock rather than a speed keeps one authority. Per
                // creature because §07 is per match: every one of them is at the same hour.
                monster.SetMatchElapsedSeconds(_clock.ElapsedSeconds);
                if (_map != null)
                {
                    monster.SetMapZoneCount(_map.ZoneCount);
                }

                if (!ReferenceEquals(creature, hunted))
                {
                    // §01 · §08 make the surface a 안전 지대, and §03's partial reset leaves
                    // the monster in the building when the team walks out. Reporting a
                    // target that is standing at the van would have it hunt the safe zone.
                    //
                    // A ghost is dropped for a different reason and it matters more than it
                    // looks: the rig stays where it fell, so without this the monster would
                    // stand over the corpse re-acquiring it every tick and §06's machine would
                    // never leave 추격 again. §09 takes the player out of the world; the seat
                    // has to leave §06's target list with them.
                    //
                    // And a creature on another storey is forgotten every tick, so that a
                    // runner who drops through a 투하구 leaves nothing behind them: the floor
                    // they left goes back to 순찰 rather than holding a reading of somebody
                    // who is now three metres underneath it.
                    monster.ForgetTarget(LocalPlayerIndex);
                }
                else
                {
                    // The true position, every tick. §06's perception rules are in Core and
                    // decide what the monster can actually see.
                    monster.ReportTarget(LocalPlayerIndex, _playerRoot!.position);

                    if (footstep.HasValue)
                    {
                        monster.ReportSound(footstep.Value.At, footstep.Value.RangeMetres, footstep.Value.Loudness);
                    }

                    if (voice.HasValue)
                    {
                        monster.ReportSound(voice.Value.At, voice.Value.RangeMetres, voice.Value.Loudness);
                    }
                }

                monster.Simulate(GameConstants.FixedStep);
            }
        }

        /// <summary>
        /// The creature the local runner shares a floor with, or null.
        /// <para>
        /// Nearest by flat distance among the creatures within
        /// <see cref="MapGraph.StoreyChangeMetres"/> of the runner's height, because
        /// §12-B③ puts one creature on a floor and "nearest" only has to break a tie a
        /// correct map never offers.
        /// </para>
        /// </summary>
        private Creature? LocalStoreyCreature()
        {
            var root = _playerRoot;
            if (root == null)
            {
                return null;
            }

            Creature? best = null;
            var bestDistance = float.PositiveInfinity;
            var here = root.position;

            for (var i = 0; i < _creatures.Count; i++)
            {
                var agent = _creatures[i].Agent;
                if (agent == null || !OnSameStorey(agent.transform.position, here))
                {
                    continue;
                }

                var flat = agent.transform.position - here;
                flat.y = 0f;

                var distance = flat.sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = _creatures[i];
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// Whether two world points are on one storey.
        /// <para>
        /// <see cref="MapGraph.StoreyChangeMetres"/> — 1.8 m, "the vertical separation above
        /// which two places are on different storeys and nothing sees between them", half
        /// the kit's 3.75 m floor pitch. Not a number invented here, and the same one
        /// <c>MatchMap.IsOnSurface</c> and <c>MapGraph</c> ask, so §01's 지상, §12's 층 and
        /// §06's reach cannot disagree about what a floor is.
        /// </para>
        /// </summary>
        private static bool OnSameStorey(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.y - b.y) < MapGraph.StoreyChangeMetres;
        }

        /// <summary>
        /// The player's feet, raised as a noise for §06. Delivered by
        /// <see cref="StepCreatures"/> to the creature on the runner's own floor.
        /// <para>
        /// <b>This is the door out of 순찰, and it was nailed shut.</b> §06's table gives
        /// 순찰 exactly one transition — 소리 감지 → 경계 — and no sight edge, which is
        /// deliberate: the creature is not meant to acquire you across a room just by
        /// facing you. But nothing in the shipping game ever raised a sound cue. The
        /// only caller of <c>ReportSound</c> in the project was an editor screenshot
        /// tool, so the monster's one way out of patrol led nowhere and it could never
        /// chase, catch or kill anybody. The owner walked up to it and stood there and
        /// nothing happened, which is precisely correct behaviour for a machine with no
        /// input. <c>MonsterKillTests</c> holds the reproduction.
        /// </para>
        /// <para>
        /// <b>Distance, not frames.</b> A step is
        /// <see cref="AudioTuning.FootstepStrideMetres"/> of ground covered, which is
        /// the same rule <c>FootstepAudio</c> plays a clip on — so what the monster
        /// hears and what the room hears are the same event, at the same instant,
        /// without either one depending on the other. Sound must not be a per-frame
        /// spray: <c>MonsterBrain</c> takes the loudest cue in a tick, and a continuous
        /// stream would make a walking player as loud as a running one.
        /// </para>
        /// <para>
        /// <b>Crouching is silent and that is the point.</b> The range is
        /// <see cref="GameConstants.MonsterFootstepHearingRange"/> scaled by the
        /// surface's own §12 clarity and by how hard the player is working, which is the
        /// same 0~1 <c>NoiseMeter</c> uses to blind the 청음사. One quantity, so §10's
        /// "hear it or be heard" stays one decision.
        /// </para>
        /// </summary>
        private NoiseCue? TakeFootstepCue()
        {
            var root = _playerRoot;
            if (root == null)
            {
                return null;
            }

            var here = root.position;
            var moved = here - _lastFootstepPosition;
            moved.y = 0f;

            _strideTravelled += moved.magnitude;
            _lastFootstepPosition = here;

            if (_strideTravelled < AudioTuning.FootstepStrideMetres)
            {
                return null;
            }

            _strideTravelled = 0f;

            // How hard they are working. Without a meter — a rig assembled by a test, or
            // a headless host — fall back on speed over the same ladder the meter uses,
            // so the monster still hunts rather than silently going deaf.
            var effort = _noise != null
                ? _noise.Noise01
                : Mathf.Clamp01(moved.magnitude / (GameConstants.RunSpeed * Time.deltaTime + 0.0001f));

            if (effort <= 0.01f)
            {
                return null;
            }

            var clarity = ListenerAbility.ClarityOf(FloorSurfaces.Sample(here));
            var range = GameConstants.MonsterFootstepHearingRange * clarity * effort;
            if (range <= 0.1f)
            {
                return null;
            }

            return new NoiseCue(here, range, effort);
        }

        /// <summary>
        /// The player's voice, raised as a noise for §06. Delivered by
        /// <see cref="StepCreatures"/> to the creature on the runner's own floor.
        /// <para>
        /// §12-A's maze is meant to be argued through — two players who meet at a gate should
        /// be able to say which way they came. But §06 leaves 순찰 on 소리 감지 and nothing
        /// else, so a voice has to be a sound or the whole coordination layer is free. It is
        /// not free: a whisper is inaudible to the creature by construction, ordinary speech
        /// carries as far as the surface underfoot allows, and a shout reaches it from further
        /// than it reaches the person you are shouting at.
        /// </para>
        /// <para>
        /// The loudness handed over is <see cref="VoiceRules.SelfNoise"/> rather than a fresh
        /// scale, because <c>MonsterBrain</c> takes the LOUDEST cue in a tick and a voice has
        /// to be able to beat a footstep. Somebody who shouts while walking should be found by
        /// the shout.
        /// </para>
        /// </summary>
        private NoiseCue? TakeVoiceCue()
        {
            var root = _playerRoot;
            if (root == null || _voiceEffort == VoiceEffort.Silent)
            {
                return null;
            }

            var here = root.position;
            var range = VoiceRules.MonsterHearingRangeMetres(_voiceEffort, FloorSurfaces.Sample(here));
            if (range <= 0.1f)
            {
                return null;
            }

            return new NoiseCue(here, range, VoiceRules.SelfNoise(_voiceEffort));
        }

        /// <summary>
        /// A noise the host raised this tick, held rather than delivered.
        /// <para>
        /// It exists because raising a noise and hearing one used to be the same call.
        /// <c>ReportFootsteps(monster)</c> advanced the stride accumulator as a side effect
        /// of telling one creature, which is exactly correct while there is one creature and
        /// silently wrong the moment there are eight: the first agent in the loop would take
        /// the step and reset the metre count, and the other seven would be handed a match
        /// in which the player never walked. Separating "a step happened" from "who heard
        /// it" is what makes the per-storey rule above expressible at all.
        /// </para>
        /// </summary>
        private readonly struct NoiseCue
        {
            internal NoiseCue(Vector3 at, float rangeMetres, float loudness)
            {
                At = at;
                RangeMetres = rangeMetres;
                Loudness = loudness;
            }

            /// <summary>Where the noise was made.</summary>
            internal Vector3 At { get; }

            /// <summary>How far it carries, §12's surface and §04's effort already applied.</summary>
            internal float RangeMetres { get; }

            /// <summary>0~1. <c>MonsterBrain</c> takes the loudest cue in a tick.</summary>
            internal float Loudness { get; }
        }

        /// <summary>
        /// Finds every 투하구 the generator laid down and hands it its landing.
        /// <para>
        /// The markers come in pairs named by the map — "투하구 3북" and "투하구 3북 착지" —
        /// so the pairing is done here by name rather than by a payload on the marker, which
        /// is the same arrangement the doors already use.
        /// </para>
        /// </summary>
        private void AttachChutes()
        {
            _chutes.Clear();

            var landings = new Dictionary<string, Transform>();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name.EndsWith(" 착지", System.StringComparison.Ordinal))
                {
                    landings[t.name.Substring(0, t.name.Length - 3)] = t;
                }
            }

            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith("투하구 ", System.StringComparison.Ordinal)
                    || t.name.EndsWith(" 착지", System.StringComparison.Ordinal)
                    || !landings.TryGetValue(t.name, out var landing))
                {
                    continue;
                }

                var chute = t.GetComponent<Chute>();
                if (chute == null)
                {
                    chute = t.gameObject.AddComponent<Chute>();
                }

                // The storey is read off the landing's height rather than parsed out of the
                // name: MapKitCatalogue.StoreyMetres is the authority for where a floor is,
                // and a name is a label.
                var storey = Mathf.RoundToInt(-landing.position.y / 3.75f);
                chute.Bind(landing.position, storey);
                _chutes.Add(chute);
            }

            if (_chutes.Count > 0)
            {
                Debug.Log("[Match] §01 투하구 " + _chutes.Count + "개 — 뛰어내리면 아래층 외곽이다", this);
            }
        }

        /// <summary>
        /// A runner standing in a 투하구 falls to the storey below.
        /// <para>
        /// §01: the landing is on the RIM, so each floor is its own maze solved from the
        /// beginning. Dropped from a height rather than teleported, because the half second
        /// of falling in the dark toward a floor you have not seen is the only moment of a
        /// descent that is not navigation.
        /// </para>
        /// </summary>
        private void CheckChutes()
        {
            var root = _playerRoot;
            if (root == null || _chutes.Count == 0 || LocalPlayerIsGhost)
            {
                return;
            }

            for (var i = 0; i < _chutes.Count; i++)
            {
                if (!_chutes[i].Swallows(root.position))
                {
                    continue;
                }

                var to = _chutes[i].DropPoint();
                var controller = root.GetComponent<CharacterController>();
                if (controller != null)
                {
                    controller.enabled = false;
                }

                root.position = to;

                if (controller != null)
                {
                    controller.enabled = true;
                }

                // §02 through the director, not through a RaceState of our own. There used
                // to be a second RaceState on this class and this line fed it, so every
                // descent in the game was recorded somewhere nothing read: the HUD, the
                // finish check and the standings all hang off _raceDirector, and its rule
                // refuses ReportFinish to a runner it never saw descend. The race was
                // unwinnable and nothing failed.
                if (_raceDirector != null)
                {
                    _raceDirector.ReportDescent(LocalPlayerIndex, _chutes[i].StoreyBelow, _clock.ElapsedSeconds);
                }

                Debug.Log("[Match] §01 B" + (_chutes[i].StoreyBelow + 1) + " 도착 — "
                          + _clock.ElapsedSeconds.ToString("0") + "초", this);
                return;
            }
        }

        /// <summary>
        /// Stands §02 up: the director that owns the standings, and the HUD that shows
        /// them.
        /// <para>
        /// Attached at match start rather than authored into the scene, which is the same
        /// arrangement the doors and the 투하구 already use and for the same reason — the
        /// scene generator is in an editor assembly that cannot reference this one, so it
        /// writes markers and the director wires them. It also means a race works in any
        /// scene the generator produces with nothing to remember.
        /// </para>
        /// <para>
        /// Solo for now: one runner, because §11's field arrives with the lobby. RaceState
        /// refuses fewer than two — one runner is not a race and nothing in §12's geometry
        /// means anything without somebody to be ahead of — so a solo playtest is begun as
        /// a field of two with the second seat empty. That is a scaffold and it is marked
        /// as one; the lobby replaces it.
        /// </para>
        /// </summary>
        private void AttachRace()
        {
            _raceDirector = GetComponent<RaceDirector>() ?? gameObject.AddComponent<RaceDirector>();

            var runners = Mathf.Max(GameConstants.RaceRunnersMin, PlayersInMatch);
            if (!_raceDirector.Begin(runners))
            {
                Debug.LogError("[Match] §02 refused to start with " + runners + " runners.", this);
                _raceDirector = null;
                return;
            }

            if (_playerRoot != null)
            {
                _raceDirector.Track(LocalPlayerIndex, _playerRoot);
            }

            var hud = FindFirstObjectByType<RaceHud>();
            if (hud == null)
            {
                var go = new GameObject("RaceHud");
                go.transform.SetParent(transform, false);
                hud = go.AddComponent<RaceHud>();
            }

            hud.Bind(_raceDirector);

            _raceDirector.Finished += (id, place) =>
                Debug.Log("[Match] §02 " + (place == 1 ? "우승" : place + "위") + " — 좌석 " + id, this);
            _raceDirector.Closed += winner =>
                Debug.Log("[Match] §02 경주 종료. 우승 좌석 " + winner, this);
        }

        /// <summary>How many seats this match has. One until §11's lobby fills them.</summary>
        private int PlayersInMatch
        {
            get { return GameConstants.PlayersPerMatch; }
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
        /// Puts <see cref="DoorInteractable"/> on every door the generator laid down.
        /// <para>
        /// The scene carries a door's geometry — a frame, a hinged leaf, a blocking
        /// collider and a carving obstacle — and not its behaviour, because
        /// <c>MapSceneBuilder</c> lives in its own editor assembly and cannot see a
        /// runtime component. Attaching here is the same arrangement §09's
        /// <c>GhostSession</c> uses, and it has the same benefit: a door works in any
        /// scene the generator writes, with nothing to remember to author.
        /// </para>
        /// </summary>
        private void AttachDoors()
        {
            var built = 0;
            foreach (var group in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!group.name.StartsWith("Door_", System.StringComparison.Ordinal)
                    || group.GetComponent<DoorInteractable>() != null)
                {
                    continue;
                }

                var hinge = group.Find("Hinge");
                if (hinge == null)
                {
                    continue;
                }

                var door = group.gameObject.AddComponent<DoorInteractable>();
                door.Bind(hinge, hinge.GetComponent<Collider>(),
                          hinge.GetComponent<UnityEngine.AI.NavMeshObstacle>());
                built++;
            }

            if (built > 0)
            {
                Debug.Log("[Match] §12 문 " + built + "개 — 닫을 수 있다", this);
            }
        }

        /// <summary>
        /// Every creature leans on whatever shut door is in ITS way.
        /// <para>
        /// §12's doors are a wall to the player and a delay to the creature, and this
        /// method is the whole difference. Without it a shut door is permanent cover and
        /// §01's 「이길 수 없는 존재」 becomes something you lock in a room.
        /// </para>
        /// <para>
        /// It lives here rather than on <c>MonsterAgent</c> because of an assembly
        /// boundary that is worth stating: the monster is its own asmdef and
        /// <c>DoorInteractable</c> is in Assembly-CSharp, so the reference only runs one
        /// way. The director can see both.
        /// </para>
        /// <para>
        /// A sphere rather than the creature's path: the doors carve the NavMesh, so a
        /// shut one is already routed AROUND and asking the path what blocks it returns
        /// nothing. What the creature does is arrive and work on it, which is what a
        /// player watching the handle shake would expect.
        /// </para>
        /// <para>
        /// Per creature because a door is a place, not a match state: a runner who shuts a
        /// door on B2 has done nothing about the door the creature on B6 is already working
        /// on. Each one gets its own <see cref="DoorReachMetres"/> sphere and pushes the
        /// first blocking door in it, which is the same rule the single creature had.
        /// </para>
        /// <para>
        /// The sphere is 3D and 1.4 m across, so it cannot reach through the kit's 3.75 m
        /// storey — nothing here needs the storey test <see cref="StepCreatures"/> uses,
        /// and saying so is cheaper than adding one that would never fire.
        /// </para>
        /// <para>
        /// <c>Physics.OverlapSphereNonAlloc</c> into one shared buffer: this now runs
        /// once per creature per fixed step — eight times at 50 Hz — and the allocating
        /// overload would have made §12's doors 400 garbage arrays a second.
        /// <see cref="DoorSearchBuffer"/> says what happens if it fills.
        /// </para>
        /// </summary>
        private void PushDoors()
        {
            for (var creature = 0; creature < _creatures.Count; creature++)
            {
                var monster = _creatures[creature].Agent;
                if (monster == null)
                {
                    continue;
                }

                var found = Physics.OverlapSphereNonAlloc(
                    monster.transform.position, DoorReachMetres, _doorSearch, ~0, QueryTriggerInteraction.Collide);

                for (var i = 0; i < found; i++)
                {
                    var collider = _doorSearch[i];
                    if (collider == null)
                    {
                        continue;
                    }

                    var door = collider.GetComponentInParent<DoorInteractable>();
                    if (door == null || !door.State.Blocks)
                    {
                        continue;
                    }

                    if (door.Push(Time.deltaTime))
                    {
                        Debug.Log("[Match] §12 문이 부서졌다 — " + door.name, this);
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// Colliders one creature's door search may return.
        /// <para>
        /// A 1.4 m sphere in §12's 2.2 m corridors touches a floor tile, a wall or two, a
        /// ceiling cap and whatever prop is underfoot; 32 is several times that. If it ever
        /// did fill, the cost is one creature failing to notice one door on one tick and
        /// finding it on the next — which is why this is a fixed buffer rather than a
        /// growing one. The allocating overload is what it replaced, and it was allocating
        /// once per creature per fixed step.
        /// </para>
        /// </summary>
        private const int DoorSearchBuffer = 32;

        private readonly Collider[] _doorSearch = new Collider[DoorSearchBuffer];

        /// <summary>
        /// How far the creature reaches to lean on a door, metres. Its own agent radius
        /// (0.417) plus an arm — geometry rather than a tuned value.
        /// </summary>
        private const float DoorReachMetres = 1.4f;

        /// <summary>
        /// §06 gives the state machine five states and a catch is not one of them, so
        /// whether a player has been caught is the host's rule. The rule itself lives in
        /// <see cref="MonsterLunge"/>, engine-free and tested; this is the wiring — it
        /// feeds the struct a distance and a §06 state and does what the struct says.
        /// <para>
        /// <b>This used to be two capsules touching.</b> The instant the monster's agent
        /// radius overlapped the player's controller radius, the player died, and the
        /// 1.37 s <c>Grab</c> clip played over a body that was already dead. Nobody could
        /// see an attack happen because there was not one — there was a proximity test.
        /// Now the creature commits at 1.8 m, travels at 7.0 m/s for 0.55 s, and the
        /// strike either lands or costs it 0.8 s of standing still.
        /// </para>
        /// <para>
        /// <b>The storey test is not defensive tidying, it is the whole reason a runner can
        /// walk down this building alive.</b> The separation below is FLAT — <c>y</c> is
        /// zeroed, and it has to be, because a creature's feet and a player's feet are at
        /// different heights on the same floor. §01 stacks eight storeys in one column and
        /// <c>DescentMap.SeedCreature</c> posts every creature at its own floor's MIDDLE,
        /// which is the SAME X and Z on all eight. So the moment there is more than one
        /// creature, a runner arriving at the middle of B3 is a flat 0.0 m from the
        /// creatures on B1, B2, B4 … B8 as well, and an unguarded loop over them would kill
        /// the runner instantly, from up to 26 m below, with the killer never appearing on
        /// screen. Each creature is therefore asked only about a runner on its own floor —
        /// <see cref="OnSameStorey"/>, <see cref="MapGraph.StoreyChangeMetres"/>, the same
        /// constant §01's 지상 and §12's 층 already use.
        /// </para>
        /// <para>
        /// Every creature is stepped, not just the near one, because <c>MonsterLunge.Tick</c>
        /// is how a lunge is CANCELLED: given <c>chasing: false</c> it drops back to Ready.
        /// A creature the runner dropped away from mid-commit would otherwise stay
        /// Committed for the rest of the match, with the speed override
        /// <see cref="MonsterAgent.SetLunge"/> left switched on — 7.0 m/s of §06 patrolling
        /// a floor nobody is on.
        /// </para>
        /// </summary>
        private void CheckGrab()
        {
            var state = _state;
            if (state == null || _playerRoot == null || _onSurface || _creatures.Count == 0)
            {
                return;
            }

            var here = _playerRoot.position;

            for (var i = 0; i < _creatures.Count; i++)
            {
                var creature = _creatures[i];
                var monster = creature.Agent;
                if (monster == null)
                {
                    continue;
                }

                // §09 leaves the body where it fell and it stays there for the rest of the
                // match. A corpse is not catchable — MatchState.TryKill refuses a second kill
                // anyway, but reaching it would drop an already-empty inventory and play the
                // grab again over a player who is no longer there.
                //
                // A creature on another floor is never chasing this runner for the purposes
                // of a catch, however close the flat distance says it is. See the remarks.
                var reachable = OnSameStorey(monster.transform.position, here);
                var chasing = reachable && monster.State == MonsterStateId.Chase && !LocalPlayerIsGhost;

                var separation = here - monster.transform.position;
                separation.y = 0f;

                // Out of reach by construction when the creature is on another storey, so
                // MonsterLunge sees a distance it will never commit at and runs its
                // recovery down honestly rather than being fed a false 0 m.
                var distance = reachable ? separation.magnitude : float.PositiveInfinity;

                // Why nothing is happening, said out loud. The owner stood in front of the
                // creature and did not die, and there are four different reasons that can be
                // true at once — it is not in 추격, they are already a ghost, they are on
                // §01's surface apron, or the lunge is mid-recovery. Guessing between them
                // from outside cost a rebuild; this says which.
                if (reachable && distance < 6f && Time.time - _lastGrabReport > 0.5f)
                {
                    _lastGrabReport = Time.time;
                    Debug.Log("[Match] 괴물 " + distance.ToString("0.00") + " m · §06 " + monster.State
                              + " · 덮치기 " + creature.Lunge.State
                              + (chasing ? string.Empty : "  ← 추격이 아니라 판정 없음")
                              + (LocalPlayerIsGhost ? "  ← 이미 유령" : string.Empty), this);
                }

                // FixedStep, not Time.deltaTime. CheckGrab runs inside StepFixed, which
                // StepMatch may call several times in one frame to burn down its
                // accumulator — so a frame delta here advanced the strike by a whole frame
                // per fixed step, and a 0.55 s commit resolved in a different number of
                // steps depending on frame rate. §06's lunge is the one window where the
                // player's survival is decided by tenths of a second, and it was being
                // measured on the wrong clock.
                var previous = creature.Lunge.State;
                var outcome = creature.Lunge.Tick(GameConstants.FixedStep, chasing, distance);

                if (previous != creature.Lunge.State || outcome != LungeEvent.None)
                {
                    monster.SetLunge(creature.Lunge.State == LungeState.Committed,
                                     creature.Lunge.SpeedNow(monster.ChaseSpeed));
                }

                switch (outcome)
                {
                    case LungeEvent.Committed:
                        // The clip plays NOW, while the creature is still travelling. That is
                        // the whole change: an attack somebody can see coming.
                        monster.PlayGrab();
                        Debug.Log("[Match] §06 덮친다 — " + distance.ToString("0.00") + " m", this);
                        continue;

                    case LungeEvent.Missed:
                        Debug.Log("[Match] §04 주자 — 덮치기를 피했다, " + distance.ToString("0.00") + " m", this);
                        continue;

                    case LungeEvent.Hit:
                        break;

                    default:
                        continue;
                }

                var where = _playerRoot.position;

                // §08: "사망자의 전리품 — 떨어진다." The pile stays where it fell so the team
                // has a reason to come back for it, and its value is what §09 then makes the
                // ghost watch — "자기 물건이 어디 있는지 보이는데 말할 수 없다."
                var pileValue = DropEverything(where);

                if (state.TryKill(LocalPlayerIndex, where.ToVec3()))
                {
                    Debug.Log("[Match] §09 잡혔다 — " + where, this);
                    monster.ForgetTarget(LocalPlayerIndex);
                    EnterGhost(state.PlayerAt(LocalPlayerIndex).Ghost, pileValue);
                }

                // The seat has resolved, so this tick is over. The other creatures' lunges
                // resume on the next one, by which time LocalPlayerIsGhost is true and none
                // of them can commit at a corpse — which is the rule the single-creature
                // version got from `chasing` and this one has to get from the loop ending.
                CheckOutcome();
                break;
            }
        }

        /// <summary>
        /// Hands the seat over to §09. The match keeps running; this is a change of what
        /// the player is doing, not the end of what they are doing it in.
        /// <para>
        /// §08's pile is reported to the ghost here rather than inside <c>MatchState</c>
        /// because the value belongs to the economy — <c>DroppedLootField</c> is what
        /// priced it — and <c>MatchState.TryKill</c>'s own documentation asks the caller
        /// to make exactly this call for exactly that reason.
        /// </para>
        /// <para>
        /// The torch goes out. §03 charges the battery per second and a dead player cannot
        /// spend that resource for the team; leaving it burning on a corpse would drain a
        /// cell the living still have to buy, and light the room for §06 while it did.
        /// </para>
        /// </summary>
        private void EnterGhost(GhostState? ghost, int droppedPileValue)
        {
            if (ghost == null)
            {
                return;
            }

            ghost.NoteOwnLootDropped(droppedPileValue);

            if (_flashlight != null)
            {
                _flashlight.State.TurnOff();
                _flashlight.EnforceCarryRules();
                _flashlight.RefreshPresentation();
            }

            // §01's HUD comes down with the body. Every row on it belongs to a living
            // player — §03's battery, §04's stamina, §08's load — and a dead one has
            // none of them; photographed with it still up, §09's own overlay was sharing
            // a corner with two meters that could never move again.
            _hud?.UnbindHud();
            _hud?.CloseShop();
            _hud?.DismissClue();
            _clueReader.Cancel();
            _revealedClueId = -1;

            var ghosts = _ghosts;
            if (ghosts == null)
            {
                Debug.LogWarning(
                    "[Match] §09 has nowhere to put the dead player — no GhostSession on this director, "
                    + "so the death is a locked camera instead of a spectator.", this);
                return;
            }

            ghosts.MatchEndRequested = ShowHeldEndScreen;

            // The creature on the floor the body is lying on, not simply "the monster".
            // §09's whole promise is 「죽으면 지루하다 → 볼 게 있고 할 게 있다」, and what
            // there is to watch is the thing that just killed you still hunting the floor
            // you died on. Handed the primary instead, a ghost on B2 would spend the match
            // watching a §06 state machine three storeys away. Falls back to the primary so
            // that a death on a floor with no creature still has something to show.
            ghosts.Begin(ghost, _playerRoot, LocalStoreyMonster ?? _monster);
        }

        /// <summary>
        /// §02's verdict, applied when there is one — unless §09 is still using the
        /// building.
        /// <para>
        /// <b>This is the line that made a solo death end the screen.</b> §02 computes
        /// over seats, so with one seat occupied a death is 전멸 and 전멸 is final, and
        /// the end screen went up in the same tick the monster caught the player. §09 says
        /// the opposite: the dead keep playing — "죽으면 지루하다 → 볼 게 있고 할 게 있다"
        /// — and being thrown to a results panel is precisely what that row rules out.
        /// </para>
        /// <para>
        /// So the verdict is reached and then <em>held</em>. Nothing about §02 changes: the
        /// outcome is still computed from the same tally by the same evaluator, the ghost
        /// cannot alter it, and <c>MatchState</c> will not let a ghost extract. What
        /// changes is who decides when to look at it.
        /// </para>
        /// <para>
        /// <b>Nothing here asks how many people are playing.</b> With four, one death
        /// leaves three in play, <c>IsFinal</c> is false and this method returns before the
        /// hold is even considered — the ghost simply spectates, which is §09 as written.
        /// The hold only ever appears when §02 has genuinely finished, and then it is
        /// offered to whichever local seat is a ghost. Solo is that case with the other
        /// three chairs empty.
        /// </para>
        /// </summary>
        private void CheckOutcome()
        {
            if (!_running)
            {
                return;
            }

            var resolution = Resolution;
            if (!resolution.IsFinal)
            {
                _endScreenHeldForGhost = false;
                _ghosts?.NoteVerdictReached(false);
                return;
            }

            var ghosts = _ghosts;
            if (ghosts != null && ghosts.IsActive)
            {
                if (!_endScreenHeldForGhost)
                {
                    _endScreenHeldForGhost = true;
                    ghosts.MatchEndRequested = ShowHeldEndScreen;
                    ghosts.NoteVerdictReached(true);

                    Debug.Log(
                        "[Match] §02 " + resolution.Outcome
                        + " — 결과는 나왔지만 §09의 유령이 아직 건물 안에 있다. "
                        + "[" + GhostSession.EndMatchKey + "]를 누르고 있으면 결과 화면을 연다.", this);
                }

                // §07's clock is still running and §06 is still hunting, on purpose: a
                // free camera over a live match is the only instrument this project has
                // for §14 Q1, and stopping the match would take it away.
                return;
            }

            ShowEndScreen(resolution);
        }

        /// <summary>The ghost asked. Shows the verdict §02 had already reached, unchanged.</summary>
        private void ShowHeldEndScreen()
        {
            if (!_running || !_endScreenHeldForGhost)
            {
                return;
            }

            _ghosts?.End();
            ShowEndScreen(Resolution);
        }

        private void ShowEndScreen(MatchResolution resolution)
        {
            _endScreenHeldForGhost = false;
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

        /// <summary>
        /// §08's table over §12's 막힌 길 — <b>on the co-operative branch only</b>.
        /// <para>
        /// DESCENT-PIVOT §7 step 7 「상점/전리품/단서 제거」 took the race's call to this out
        /// of <see cref="BeginMatch"/>; the reasoning is written at the line it stood on.
        /// It is still called for the recovery match this grew out of, where 전리품 has a
        /// 차량 to be sold at and credits to become, and it will go with that branch.
        /// </para>
        /// </summary>
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

            if (_ghosts == null)
            {
                // Built here rather than authored into the scene for the same reason the
                // HUD is: §09 is a state every seat can enter and no scene should have to
                // remember to carry it. It costs nothing until somebody dies.
                _ghosts = GetComponentInChildren<GhostSession>();
                if (_ghosts == null)
                {
                    var child = new GameObject("GhostSession");
                    child.transform.SetParent(transform, worldPositionStays: false);
                    _ghosts = child.AddComponent<GhostSession>();
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
        /// Stands one §06 creature on every declared start and takes the primary's probe,
        /// which is the same <see cref="IWorldProbe"/> §03's placement reasons through —
        /// one answer about the world, not two.
        /// <para>
        /// <b>One agent per <see cref="MatchMap.MonsterSpawns"/> entry, cloned from the
        /// scene's own rig.</b> §12-B③ writes 「괴물이 안쪽을 순찰한다」 about every floor and
        /// <see cref="GameConstants.MonstersPerStorey"/> is the count that follows; §12-C
        /// makes the 투하구 one-way and not a NavMeshLink, so a creature can never change
        /// floors and eight floors need eight creatures. Cloned rather than built here
        /// because the rig carries a NavMeshAgent sized to §12's corridors, a skinned mesh,
        /// an animation driver, an audio driver and footsteps — a second creature assembled
        /// from scratch would be a second, quietly different, monster.
        /// </para>
        /// <para>
        /// <b>The primary start keeps the authored rig</b> and every other start gets a
        /// clone, so a scene with one start behaves exactly as it did before this method
        /// grew a loop — the same object, in the same place, found by the same
        /// <c>FindFirstObjectByType</c> the presentation layers use.
        /// </para>
        /// <para>
        /// Clones are parented under the match's world root and destroyed with it, and are
        /// also held in <see cref="_creatures"/> so <see cref="ClearWorld"/> can take them
        /// down BEFORE the next match reads the scene. A director that left them standing
        /// would add eight creatures per <see cref="BeginMatch"/>, and the second match of
        /// a session would be played against sixteen.
        /// </para>
        /// <para>
        /// <b>§13 still replays.</b> All eight brains draw from the host's single seeded
        /// stream rather than from eight streams of their own, which is deterministic
        /// because both the order they are built in and the order
        /// <see cref="StepCreatures"/> ticks them in are the order of
        /// <see cref="MatchMap.MonsterSpawns"/> — a list this map sorts by marker name. Give
        /// each creature its own <c>DeterministicRandom</c> and the seed would have to grow
        /// a per-storey derivation nobody has written down; take the order away and the
        /// same seed stops producing the same match.
        /// </para>
        /// </summary>
        private NavMeshWorldProbe? PrepareCreatures()
        {
            var authored = _monster;
            var rng = _rng;
            if (authored == null || rng == null)
            {
                return null;
            }

            _creatures.Clear();

            var map = _map;
            var spawns = map != null ? map.MonsterSpawns : null;
            var primary = map != null ? map.MonsterSpawn : null;

            if (spawns == null || spawns.Count == 0)
            {
                // No starts declared: the scene's own creature stays where it was authored.
                // The old behaviour, and the only one available — inventing a spawn would
                // put §06 somewhere no section chose.
                _creatures.Add(new Creature(authored, null, cloned: false));
            }
            else
            {
                for (var i = 0; i < spawns.Count; i++)
                {
                    var spawn = spawns[i];
                    var isPrimary = primary == null ? i == 0 : ReferenceEquals(spawn, primary);
                    var agent = isPrimary ? authored : CloneCreature(authored, spawn);
                    if (agent == null)
                    {
                        continue;
                    }

                    agent.transform.position = spawn.position;
                    _creatures.Add(new Creature(agent, spawn, cloned: !isPrimary));
                }
            }

            NavMeshWorldProbe? primaryProbe = null;
            for (var i = 0; i < _creatures.Count; i++)
            {
                var agent = _creatures[i].Agent;

                agent.SelfDriven = false;
                agent.Initialize(rng);
                agent.ClearTargets();

                var probe = agent.Probe;
                if (probe != null)
                {
                    probe.LitQuery = IsAreaLit;
                }

                if (ReferenceEquals(agent, authored) || primaryProbe == null)
                {
                    primaryProbe = probe;
                }
            }

            return primaryProbe;
        }

        /// <summary>
        /// A second creature, cut from the one the scene carries.
        /// <para>
        /// <c>Instantiate</c> rather than a prefab reference: the solo scene's creature is
        /// assembled by <c>SoloPlaytest.SpawnMonster</c> at generation time, not saved as a
        /// prefab, so the rig in the scene IS the authority for what a creature is. Copying
        /// it means an animation clip, an audio driver or a NavMeshAgent radius fixed on the
        /// original is fixed on all eight, which is the failure mode a hand-built second
        /// creature has.
        /// </para>
        /// <para>
        /// Nothing about the brain is copied — <c>MonsterBrain</c> and
        /// <c>NavMeshWorldProbe</c> are plain fields, not serialised, so a clone arrives
        /// with neither and is given both by <c>Initialize</c> on the host's own seeded
        /// stream (§13).
        /// </para>
        /// </summary>
        private MonsterAgent? CloneCreature(MonsterAgent authored, Transform spawn)
        {
            var copy = Instantiate(authored.gameObject, spawn.position, authored.transform.rotation);
            copy.name = authored.name + " @ " + spawn.name;

            if (_worldRoot != null)
            {
                copy.transform.SetParent(_worldRoot.transform, worldPositionStays: true);
            }

            var agent = copy.GetComponent<MonsterAgent>();
            if (agent == null)
            {
                // The rig is a MonsterAgent by definition — this is here so that a copy
                // that somehow arrives without one is destroyed rather than left standing
                // in the building as a mute duplicate of the creature.
                Debug.LogError("[Match] §06 창조물 복제본에 MonsterAgent가 없다 — " + copy.name, this);
                Interactable.Despawn(copy);
                return null;
            }

            return agent;
        }

        /// <summary>
        /// §12-B③'s count, checked against the world instead of against the map.
        /// <para>
        /// <b>This is the answer to "what stops the audit going green on a game that still
        /// runs one creature".</b> <c>NavMeshConnectivity</c> measures MonsterSpawn markers
        /// in the editor; this measures agents standing in the scene and being ticked, and
        /// refuses the match if the two numbers differ. Both numbers are then printed on one
        /// line of the match log, so no reader has to hold one of them in their head.
        /// </para>
        /// <para>
        /// It cannot be satisfied by half a change. A map that grows eight starts while the
        /// runtime still stands up one fails here and the match does not begin — every
        /// PlayMode test that calls <c>BeginMatch</c> goes red, loudly, naming both counts.
        /// The reverse — this half landing without the map's — is the shipped map's own
        /// case: one start, one creature, equal, and the audit still says <em>1 of 8
        /// storeys</em>, which is the truth.
        /// </para>
        /// </summary>
        private bool VerifyCreatureCount(MatchMap map)
        {
            var declared = map.MonsterSpawns.Count;
            var standing = _creatures.Count;

            if (declared > 0 && standing != declared)
            {
                Debug.LogError(
                    "[Match] §12-B③ 창조물 수가 맞지 않는다 — 맵은 " + declared + "개의 시작점을 선언했는데 "
                    + standing + "마리만 섰다. §06's audit counts the markers and would report "
                    + map.MonsterStoreyCount + " storeys with a creature; the match would be played with "
                    + standing + ". Refusing to start rather than run a building the report does not "
                    + "describe.", this);
                return false;
            }

            Debug.Log(
                "[Match] §06 창조물 " + standing + "마리 — " + map.MonsterStoreyCount + "개 층에 선언된 시작점 "
                + declared + "개. §12-B③ 층마다 " + GameConstants.MonstersPerStorey + "마리."
                + (declared == 0 ? "  ← 이 맵에는 §06이 없다" : string.Empty), this);

            return true;
        }

        /// <summary>
        /// §03's partial reset, monster half. The chase state, the position and the
        /// aggro go; the clock does not, and nobody here can make it.
        /// <para>
        /// Every creature, each back to its OWN start. Sending them all to the primary
        /// would collapse eight floors' worth of §06 onto one storey on the first 귀환 —
        /// and since a creature cannot climb, seven of them would never come back.
        /// </para>
        /// </summary>
        private void ResetMonster()
        {
            var map = _map;
            var rng = _rng;
            if (map == null || rng == null || _creatures.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _creatures.Count; i++)
            {
                var creature = _creatures[i];
                var monster = creature.Agent;
                if (monster == null)
                {
                    continue;
                }

                monster.ClearTargets();

                // Its own lunge goes with its aggro: §03 calls this a reset of 추격, and a
                // creature left mid-commit would land a strike begun before the team walked
                // out of the building.
                creature.Lunge = default(MonsterLunge);

                var spawn = creature.Spawn != null ? creature.Spawn.position : monster.transform.position;
                var facing = map.Entrance - spawn;
                facing.y = 0f;

                monster.Respawn(spawn, facing, rng);

                // Respawn rebuilds the probe, so the light query has to be re-attached or
                // §03's "is this area lit" quietly starts answering false.
                var probe = monster.Probe;
                if (probe != null)
                {
                    probe.LitQuery = IsAreaLit;
                }

                if (ReferenceEquals(monster, _monster))
                {
                    _probe = probe;
                }
            }

            Debug.Log("[Match] §03 부분 리셋 — 괴물 " + _creatures.Count + "마리의 추격 · 위치 · 어그로 초기화. 시계는 "
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
        /// <returns>
        /// What the pile is worth in credits, so §09's ghost can be told what it is
        /// looking at — "자기 물건이 어디 있는지 보이는데 말할 수 없다." Zero for an
        /// empty-handed death, which makes no pile at all.
        /// </returns>
        private int DropEverything(Vector3 where)
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

            var value = 0;
            if (_loadout != null)
            {
                // DropFrom hands back the PILE'S INDEX, not its price, and returns −1 for
                // an empty-handed death. Read as a value it makes the first pile of every
                // match worth zero — which is exactly what §09's overlay showed the first
                // time this was photographed: a ghost standing over its own 은수저 being
                // told it had dropped 0 크레딧.
                var pile = _droppedLoot.DropFrom(_loadout.Inventory, where.ToVec3());
                value = pile >= 0 ? _droppedLoot.ValueOf(pile) : 0;
            }

            SyncLootState();
            return value;
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
            _endScreenHeldForGhost = false;

            // Before anything else: §09 has the player's camera unparented from the rig
            // and three of the rig's components switched off. A new match laid out around
            // a rig in that state would spawn a player who cannot move and cannot see.
            _ghosts?.End();

            DespawnClonedCreatures();

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

        /// <summary>
        /// Takes down the creatures this director cloned, and leaves the scene's own alone.
        /// <para>
        /// <b>Deactivated first, then destroyed.</b> <c>Interactable.Despawn</c> is
        /// <c>Destroy</c> in play mode, which does not take effect until the end of the
        /// frame — so a clone destroyed here is still alive and still findable by
        /// <c>FindFirstObjectByType&lt;MonsterAgent&gt;()</c> for the rest of the frame that
        /// is laying out the next match. Switching it off first takes it out of that search
        /// immediately, because the default <c>FindObjectsInactive.Exclude</c> skips it.
        /// </para>
        /// <para>
        /// The authored rig is never destroyed: it belongs to the scene, the inspector
        /// reference on this component points at it, and a second <c>BeginMatch</c> has to
        /// find it exactly where the first one did.
        /// </para>
        /// </summary>
        private void DespawnClonedCreatures()
        {
            for (var i = 0; i < _creatures.Count; i++)
            {
                var creature = _creatures[i];
                if (!creature.Cloned || creature.Agent == null)
                {
                    continue;
                }

                var body = creature.Agent.gameObject;
                body.SetActive(false);
                Interactable.Despawn(body);
            }

            _creatures.Clear();
        }
    }
}
