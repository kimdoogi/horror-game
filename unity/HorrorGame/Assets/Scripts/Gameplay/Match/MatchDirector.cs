#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Audio;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Match;
using HorrorGame.Core.Monster;
using HorrorGame.Core.Session;
using HorrorGame.Core.Voice;
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
    /// One descent, from the start line on B1's rim to §02's verdict. §01 · §02 · §06 · §07 · §09.
    /// <para>
    /// <b>This is the host, and there is only one game in it.</b> Up to twenty runners
    /// start on the outer ring of B1, cut inward through §12's narrowing gates, drop
    /// through a 투하구 onto the RIM of the storey below, and do it eight times. First to
    /// the middle of B8 wins. A §06 creature patrols each floor as a HAZARD: being caught
    /// is 탈락 — out, unranked, and the race carries on without you. That is the whole
    /// game.
    /// </para>
    /// <para>
    /// <b>What used to be here.</b> This class carried two games separated by an
    /// <c>if (_raceMode)</c> branch, and the co-operative recovery match on the other side
    /// of it was not merely dead — it ran. A <c>Shop</c> was lazily constructed on the
    /// first race tick by the <c>_shop ??= new Shop(_wallet)</c> getter, loot state was
    /// synced into <c>PlayerState</c> every fixed step, <c>DropEverything</c> fired on
    /// death, and <c>UpdatePhase</c> tested §01's 지상 apron on a building that has none.
    /// DESCENT-PIVOT §7 step 7 「상점/전리품/단서 제거」 is this change and it deletes rather
    /// than gates: §03's clue chain and objective, §08's shop · wallet · credits · 전리품 ·
    /// 궤짝, the 차량 and its 지상 apron, §01's 왕복 and 귀환, and the flag itself. A
    /// <c>bool</c> with one legal value is a lie about what the game is, so <c>_raceMode</c>
    /// went with the branch it guarded.
    /// </para>
    /// <para>
    /// <b>§04 went next, and it went from here last.</b> That deletion left one survivor in
    /// this file for a whole round: <c>BuildLineup()</c>, a private helper that assembled
    /// four DISTINCT <c>RoleId</c>s out of <c>RoleSelection.AllRoles</c> purely so
    /// <c>MatchState</c>'s constructor would accept them, and it ran on every single
    /// <see cref="BeginMatch"/>. It was labelled a scaffold, which is why two sweeps read
    /// past it. The owner's line is 「직업도 다 없애」 · 「캐릭터는 다 똑같이 생겨도되지」, so
    /// there is no lineup to build: twenty identical runners start on the rim and the only
    /// thing that separates them at the finish is the descent they just made. The floor's
    /// 발소리 clarity used to be read through <c>ListenerAbility</c> — 청음사's class — and is
    /// now read from <see cref="MapZone.ClarityOf"/>, which is the same table in §12's own
    /// namespace and the one <c>VoiceRules</c> already asks. Nothing on this class knows
    /// what a role is; do not give it one back to satisfy a constructor.
    /// </para>
    /// <para>
    /// <b>Two things that were never wired, found while deleting, and fixed here.</b>
    /// (1) Nothing ever called <c>RaceDirector.ReportCaught</c>, so a runner killed by §06
    /// stayed <c>RacerStatus.Running</c> in the standings for ever — §02 could not close and
    /// the RaceHud's verdict line was unreachable. <see cref="CheckGrab"/> reports it now.
    /// (2) <see cref="AttachRace"/> sized the field from <c>GameConstants.PlayersPerMatch</c>
    /// — the co-op party of four — and never withdrew the seats nobody was sitting in, so
    /// three phantom runners kept the race open for ever. Both are the same failure this
    /// project keeps finding: a system that reports and a system that plays, disagreeing.
    /// </para>
    /// <para>
    /// <b>§12-B③ is a count, and this class is where it becomes true.</b> 「괴물이 안쪽을
    /// 순찰한다」 is written about every floor and a 투하구 is a fall, not a path, so a
    /// creature that starts on B5 patrols B5 and nowhere else. The map declares one start
    /// per storey; <see cref="PrepareCreatures"/> stands one agent on each of them and
    /// <see cref="VerifyCreatureCount"/> refuses the match if the two numbers ever differ —
    /// which is the one thing standing between a §06 audit that reads "8 of 8 storeys" and
    /// a game that still runs a single monster.
    /// </para>
    /// <para>
    /// <b>Host authority (§13) is preserved by omission.</b> Nothing on this class hands
    /// out a position anybody has not earned by looking at it. When Mirror arrives this
    /// class is the host-side behaviour unchanged; clients get the screens.
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

        /// <summary>
        /// The seat the person at this keyboard is in. One local runner per machine; §11's
        /// other nineteen are other machines, and §13's host owns the standings.
        /// </summary>
        private const int LocalSeat = 0;

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

        /// <summary>
        /// How far the creature reaches to lean on a door, metres. Its own agent radius
        /// (0.417) plus an arm — geometry rather than a tuned value.
        /// </summary>
        private const float DoorReachMetres = 1.4f;

        [Header("§01 하강")]
        [SerializeField]
        [Tooltip("§13: a descent replays from its seed. Changing it changes the whole tower.")]
        private int _seed = 20260731;

        [SerializeField]
        [Tooltip("Begin the descent on Start. Off when a test or §11's lobby drives BeginMatch itself.")]
        private bool _autoStart = true;

        [Header("Wiring")]
        [SerializeField]
        [Tooltip("The rig every creature is cut from. Left empty, the first MonsterAgent in the scene is used.")]
        private MonsterAgent? _monster;

        [SerializeField]
        [Tooltip("The local runner's rig root. Left empty, the first PlayerMotor in the scene is used.")]
        private Transform? _playerRoot;

        private readonly List<Light> _areaLights = new List<Light>();

        /// <summary>
        /// Every creature this match is running, one per <see cref="MatchMap.MonsterSpawns"/>
        /// entry. See <see cref="PrepareCreatures"/> for what guarantees that count.
        /// </summary>
        private readonly List<Creature> _creatures = new List<Creature>();

        /// <summary>Every 투하구 in the scene, wired at match start. §01.</summary>
        private readonly List<Chute> _chutes = new List<Chute>();

        /// <summary>
        /// §01's safety net: the runtime half of "you cannot leave the building". The
        /// physical shell is the primary fix and this is what catches what it misses. See
        /// <see cref="OutOfBounds"/> and <see cref="CheckBounds"/>.
        /// </summary>
        private readonly OutOfBounds _bounds = new OutOfBounds();

        private readonly Collider[] _doorSearch = new Collider[DoorSearchBuffer];

        private MatchClock _clock = new MatchClock();

        /// <summary>
        /// How many times §06 has caught this machine's runner and sent them back to B1.
        /// <para>
        /// The observable fact of being caught. There used to be one already —
        /// <c>LocalPlayerIsGhost</c> — and everything that wanted to know whether the
        /// creature had reached you read it, because being caught and becoming a spectator
        /// were the same event. §09's spectator is deleted with the elimination that gave
        /// it a reason to exist, so this is what is left to watch.
        /// </para>
        /// </summary>
        public int LocalTimesCaught { get; private set; }

        private MatchMap? _map;
        private DeterministicRandom? _rng;

        private PlayerInteractor? _interactor;
        private PlayerMotor? _motor;
        private PlayerFlashlight? _flashlight;
        private PlayerInputRouter? _input;
        private PlayerLook? _look;

        private GameObject? _worldRoot;

        /// <summary>§02's standings in the scene. Null until <see cref="AttachRace"/> has run.</summary>
        private RaceDirector? _raceDirector;

        private double _accumulator;
        private bool _running;
        private int _activeSeed;

        /// <summary>Throttle for the proximity report. See <see cref="CheckGrab"/>.</summary>
        private float _lastGrabReport;

        /// <summary>Ground covered since the last footstep was raised for §06. See <see cref="TakeFootstepCue"/>.</summary>
        private float _strideTravelled;

        private Vector3 _lastFootstepPosition;

        /// <summary>How loud the local runner is. Optional — a rig without one still makes noise by speed.</summary>
        private NoiseMeter? _noise;

        /// <summary>What the local runner's microphone is doing this tick. See <see cref="TakeVoiceCue"/>.</summary>
        private VoiceEffort _voiceEffort;

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

            /// <summary>The §07 start marker it belongs to. Null on a scene with none.</summary>
            internal Transform? Spawn { get; }

            /// <summary>Whether this director cloned it, and therefore owns destroying it.</summary>
            internal bool Cloned { get; }

            /// <summary>§06's catch, as an act. See MonsterLunge — the rule is in Core.</summary>
            internal MonsterLunge Lunge;
        }

        /// <summary>
        /// Raised on the step §01's safety net puts the local runner back: where they were,
        /// and where they were returned to.
        /// <para>
        /// <b>This is the half a player sees, and it is deliberately an event rather than a
        /// screen.</b> The recovery itself is legible on its own — position is written and
        /// rotation is not, so the camera keeps the heading the player was holding and the
        /// world snaps back around them rather than the player being re-spawned facing
        /// somewhere new. What that does not do is NAME what happened, and the name belongs
        /// on <c>RaceHud</c>, which is §02's screen and not this class's. The event and
        /// <see cref="SecondsOutsideTheMap"/> are the whole of what the HUD needs.
        /// </para>
        /// </summary>
        public event Action<Vector3, Vector3>? RunnerPutBack;

        /// <summary>§07's clock. Never paused — that is the section.</summary>
        public MatchClock Clock
        {
            get { return _clock; }
        }

        // DELETED with Core/Match/MatchState: the `State` property and the seat table
        // behind it. It was "the last co-operative object still standing", kept because
        // §09's ghost had to live somewhere — MatchState.TryKill was what minted it.
        // Everything else on the type (목표물, 전리품, 부상, 지상/지하, 탈출, 포기, §02's
        // four-way resolution) was the co-op game, and its constructor demanded four
        // DISTINCT §04 roles, which is the only reason BuildLineup() ever existed.
        //
        // RaceState already answers every question the race asks of a field of 2~20:
        // who is running, who finished in what place, who is out, when it ends. A seat
        // table beside it is the exact defect that made the race unwinnable a week ago.
        // The ghost is now one nullable field on this class, minted where it is used.

        /// <summary>The building the race is run in. Null before <see cref="BeginMatch"/>.</summary>
        public MatchMap? Map
        {
            get { return _map; }
        }

        /// <summary>
        /// §02's standings — who is where, who is out, who won. Null before
        /// <see cref="BeginMatch"/> and on a scene §11 refused to size a field for.
        /// </summary>
        public RaceDirector? Race
        {
            get { return _raceDirector; }
        }

        /// <summary>The seat the person at the keyboard is in.</summary>
        public int LocalPlayerIndex
        {
            get { return LocalSeat; }
        }

        /// <summary>Whether the match is being stepped.</summary>
        public bool IsRunning
        {
            get { return _running; }
        }

        /// <summary>
        /// How many times §01's safety net has had to put the local runner back inside the
        /// building this match. Zero is the number a shipped map should read.
        /// <para>
        /// Public because it is the only number that says whether the shell is holding. A
        /// storey that produces one recovery per playthrough has a seam in it, and the
        /// [Match] warning <see cref="CheckBounds"/> logs names the position it happened at.
        /// </para>
        /// </summary>
        public int OutOfBoundsRecoveries
        {
            get { return _bounds.Recoveries; }
        }

        /// <summary>
        /// How long the local runner has been off the map, seconds. 0 whenever they are on
        /// it, and it never passes <see cref="OutOfBounds.OutsideGraceSeconds"/> — the step
        /// that reaches it is the step they are put back.
        /// <para>
        /// It is here so a screen can COUNT DOWN. A teleport with no warning reads as a bug;
        /// "맵 밖 — 1.4초 후 복귀" reads as a rule. See the remarks on <see cref="RunnerPutBack"/>.
        /// </para>
        /// </summary>
        public float SecondsOutsideTheMap
        {
            get { return _bounds.SecondsOutside; }
        }

        /// <summary>
        /// How hard the local runner is speaking right now. The voice transport sets this
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
        /// audio bed, §09's spectator camera and §14's guidance all took
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
        /// Lays out and starts a descent. Idempotent in the sense that it tears down
        /// whatever the previous one left in the world first.
        /// </summary>
        /// <param name="seed">§13: the whole tower replays from this.</param>
        /// <returns>False when the scene could not carry a race; the reason is logged.</returns>
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

            if (PrepareCreatures() == null)
            {
                Debug.LogError(
                    "[Match] No NavMesh probe. §06's creatures walk through NavMesh queries and §01's "
                    + "out-of-bounds net samples the same surface; regenerate the map so it is baked.", this);
                return false;
            }

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
            if (!VerifyCreatureCount(map))
            {
                return false;
            }

            // §01's safety net starts every match with no memory of the last one. Its anchor
            // is the runner's last honest footprint, and a footprint from the previous
            // building would put a runner back inside a map that no longer exists — every
            // seed lays out a different tower on the same 57.5 m square.
            _bounds.Reset();

            // No startOnSurface argument on either any more, and its absence is the
            // point. §01's 지상 was where a co-operative team regrouped, shopped and
            // re-supplied; a runner starts on the rim of B1 with the maze in front of
            // them and nothing behind. MatchClock dropped the whole 지상 half of its
            // surface, so there is no longer a flag to pass false to.
            LocalTimesCaught = 0;
            _clock = new MatchClock();
            _accumulator = 0d;

            MovePlayerToSpawn();

            AttachDoors();
            AttachChutes();

            _noise = _playerRoot != null ? _playerRoot.GetComponentInChildren<NoiseMeter>(true) : null;
            _lastFootstepPosition = _playerRoot != null ? _playerRoot.position : Vector3.zero;
            _strideTravelled = 0f;

            AttachRace();

            _running = true;
            _seed = seed;

            Debug.Log(
                "[Match] §01 하강 시작 — seed " + seed + " · B1 외곽 " + map.PlayerSpawns.Count
                + "개 출발점 · 여덟 층 아래가 도착점이다.", this);
            return true;
        }

        /// <summary>Stops stepping. The world is left standing, so the standings stay readable.</summary>
        public void EndMatch()
        {
            _running = false;

            // §09 ends with the match and never outlives it. A ghost left flying after
            // the director stopped would be a camera in a building whose creatures have
            // stopped moving — §09's whole argument for the seat is that there is
            // something to watch.
        }

        /// <summary>
        /// Advances the match in whole <see cref="GameConstants.FixedStep"/> steps.
        /// <para>
        /// Public and delta-taking so a headless test can drive a whole descent without a
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
        /// Whether a point is under a zone light. §06 asks it: a runner standing in a lit
        /// place is easier to see, and <c>NavMeshWorldProbe.LitQuery</c> is how the rule
        /// reaches Core without Core learning what a <c>Light</c> is.
        /// <para>
        /// The reader it was built for — §03's "you cannot read a mark in the dark" — is
        /// gone with the clue chain. §06's perception is the caller that remains, and it is
        /// the reason the darkness of the inner rings costs the leader something.
        /// </para>
        /// </summary>
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
            // Hangs on the map: it is the building that decides this, not the runner
            // who is still alive, which is most of the match, and this step is what keeps
            // them alive. Whether the match is running at all is _running, tested by
            // StepMatch before it gets here.
            if (_map == null)
            {
                return;
            }

            _clock.Tick(GameConstants.FixedStep);

            if (_flashlight != null)
            {
                // §07's 심야 row: 손전등 반경 −30%. Pushed in as a float so the player
                // rig never imports the threat system (ARCHITECTURE §3).
                _flashlight.TierRangeMultiplier = _clock.Tier.FlashlightRangeMultiplier;
            }

            StepCreatures();
            PushDoors();
            CheckGrab();
            CheckChutes();

            // AFTER CheckChutes and BEFORE the race is ticked, and both halves of that are
            // load-bearing.
            //
            // After, because a 투하구 that fires on this step has to have told the guard
            // before the guard looks: CheckChutes leaves the runner three metres in the air
            // over the storey below with no floor within reach of them, which is what
            // falling out of the world looks like from the outside. Chute.Swallows is the
            // only thing in this project that knows the difference, so it is the only thing
            // that gets to say so — see OutOfBounds.Descended.
            //
            // Before, because RaceDirector.Tick is what samples §02's finish circle. A
            // runner who crossed the footprint outside the walls and is standing on the
            // middle of B8 must be put back BEFORE the race is asked whether anybody has
            // arrived, or the guard would run one step too late to matter and the exploit it
            // exists to close would still win the match.
            CheckBounds();

            _raceDirector?.Tick(_clock.ElapsedSeconds);

            while (_clock.TryDequeueTierAdvance(out var crossed))
            {
                Debug.Log("[Match] §07 " + crossed.Phase + " — 괴물 " + crossed.MonsterSpeed + " m/s"
                    + (crossed.MonsterKnowsExit ? ", 도착점을 안다" : string.Empty), this);
            }
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
        /// Storey is asked with <see cref="MapGraph.StoreyChangeMetres"/> throughout this
        /// file, because §12's 층 and §06's reach must not answer "is this the same floor"
        /// two different ways.
        /// </para>
        /// </summary>
        private void StepCreatures()
        {
            if (_creatures.Count == 0)
            {
                return;
            }

            var hunted = _playerRoot == null ? null : LocalStoreyCreature();

            // Taken once for the whole match, not once per creature. The stride is a
            // property of the runner's legs — ReportFootsteps used to advance an
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

            // §07's 순찰 column is written in zones and on this tower a zone is a storey —
            // DescentMap calls AddZone once per level. It used to be read off §03's
            // candidate-site catalog, which is deleted; MonsterStoreyCount asks the same
            // question of the markers the creatures are standing on. See MatchMap.
            var zones = _map != null ? _map.MonsterStoreyCount : 0;

            for (var i = 0; i < _creatures.Count; i++)
            {
                var creature = _creatures[i];
                var monster = creature.Agent;
                if (monster == null)
                {
                    continue;
                }

                // §07 is the only thing that sets the creature's speed and patrol scope, and
                // MonsterAgent reads both off the tier for the elapsed time the host gives
                // it. Handing it the clock rather than a speed keeps one authority. Per
                // creature because §07 is per match: every one of them is at the same hour.
                monster.SetMatchElapsedSeconds(_clock.ElapsedSeconds);
                monster.SetMapZoneCount(zones);

                if (!ReferenceEquals(creature, hunted))
                {
                    // A runner who has been caught is dropped for a reason that matters more
                    // than it looks: the rig stays where it fell, so without this the
                    // creature would stand over the body re-acquiring it every tick and
                    // §06's machine would never leave 추격 again. §09 takes the player out of
                    // the world; the seat has to leave §06's target list with them.
                    //
                    // And a creature on another storey is forgotten every tick, so that a
                    // runner who drops through a 투하구 leaves nothing behind them: the floor
                    // they left goes back to 순찰 rather than holding a reading of somebody
                    // who is now three metres underneath it.
                    monster.ForgetTarget(LocalSeat);
                }
                else
                {
                    // The true position, every tick. §06's perception rules are in Core and
                    // decide what the creature can actually see.
                    monster.ReportTarget(LocalSeat, _playerRoot!.position);

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
        /// <c>MatchMap.MonsterStoreyCount</c> and <c>MapGraph</c> ask, so §12's 층 and §06's
        /// reach cannot disagree about what a floor is.
        /// </para>
        /// </summary>
        private static bool OnSameStorey(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.y - b.y) < MapGraph.StoreyChangeMetres;
        }

        /// <summary>
        /// The runner's feet, raised as a noise for §06. Delivered by
        /// <see cref="StepCreatures"/> to the creature on the runner's own floor.
        /// <para>
        /// <b>This is the door out of 순찰, and it was nailed shut.</b> §06's table gives
        /// 순찰 exactly one transition — 소리 감지 → 경계 — and no sight edge, which is
        /// deliberate: the creature is not meant to acquire you across a room just by
        /// facing you. But nothing in the shipping game ever raised a sound cue. The
        /// only caller of <c>ReportSound</c> in the project was an editor screenshot
        /// tool, so the creature's one way out of patrol led nowhere and it could never
        /// chase, catch or eliminate anybody. The owner walked up to it and stood there and
        /// nothing happened, which is precisely correct behaviour for a machine with no
        /// input. <c>MonsterKillTests</c> holds the reproduction.
        /// </para>
        /// <para>
        /// <b>Distance, not frames.</b> A step is
        /// <see cref="AudioTuning.FootstepStrideMetres"/> of ground covered, which is
        /// the same rule <c>FootstepAudio</c> plays a clip on — so what the creature
        /// hears and what the room hears are the same event, at the same instant,
        /// without either one depending on the other. Sound must not be a per-frame
        /// spray: <c>MonsterBrain</c> takes the loudest cue in a tick, and a continuous
        /// stream would make a walking player as loud as a running one.
        /// </para>
        /// <para>
        /// <b>Crouching is silent and that is the point.</b> The range is
        /// <see cref="GameConstants.MonsterFootstepHearingRange"/> scaled by the
        /// surface's own §12 clarity and by how hard the runner is working, which is the
        /// same 0~1 <c>NoiseMeter</c> uses. One quantity, so 「소리로 남의 위치를 안다」 and
        /// 「소리로 들킨다」 stay one decision.
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
            // so the creature still hunts rather than silently going deaf.
            var effort = _noise != null
                ? _noise.Noise01
                : Mathf.Clamp01(moved.magnitude / (GameConstants.RunSpeed * Time.deltaTime + 0.0001f));

            if (effort <= 0.01f)
            {
                return null;
            }

            // §12's table, asked in §12's namespace. How clearly 콘크리트, 물, 흙 carry a
            // step is a property of the FLOOR, and 하강 gives every storey its own surface.
            // This used to be read through ListenerAbility — 청음사's class, §04, deleted
            // with the roles — which meant a race with no classes in it still reached into
            // one to decide how loud a footstep was. MapZone holds the identical table and
            // is what VoiceRules already asks, so the runner's feet and the runner's voice
            // now agree about the floor by construction rather than by coincidence.
            var clarity = MapZone.ClarityOf(FloorSurfaces.Sample(here));
            var range = GameConstants.MonsterFootstepHearingRange * clarity * effort;
            if (range <= 0.1f)
            {
                return null;
            }

            return new NoiseCue(here, range, effort);
        }

        /// <summary>
        /// The runner's voice, raised as a noise for §06. Delivered by
        /// <see cref="StepCreatures"/> to the creature on the runner's own floor.
        /// <para>
        /// §12-A's maze is meant to be argued through — two runners who meet at a gate should
        /// be able to say which way they came, and proximity voice is what makes a corridor
        /// full of identical people frightening. But §06 leaves 순찰 on 소리 감지 and nothing
        /// else, so a voice has to be a sound or talking is free. It is not free: a whisper is
        /// inaudible to the creature by construction, ordinary speech carries as far as the
        /// surface underfoot allows, and a shout reaches it from further than it reaches the
        /// person you are shouting at.
        /// </para>
        /// <para>
        /// The loudness handed over is <see cref="VoiceRules.SelfNoise"/> rather than a fresh
        /// scale, because <c>MonsterBrain</c> takes the LOUDEST cue in a tick and a voice has
        /// to be able to beat a footstep. Somebody who shouts while running should be found by
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
        /// in which the runner never walked. Separating "a step happened" from "who heard
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

            /// <summary>How far it carries, §12's surface and the runner's effort already applied.</summary>
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
                if (t.name.EndsWith(" 착지", StringComparison.Ordinal))
                {
                    landings[t.name.Substring(0, t.name.Length - 3)] = t;
                }
            }

            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith("투하구 ", StringComparison.Ordinal)
                    || t.name.EndsWith(" 착지", StringComparison.Ordinal)
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
                var storey = Mathf.RoundToInt(-landing.position.y / OutOfBounds.StoreyPitchMetres);
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
            if (root == null || _chutes.Count == 0)
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
                    _raceDirector.ReportDescent(LocalSeat, _chutes[i].StoreyBelow, _clock.ElapsedSeconds);
                }

                // The one thing that can tell §01's own falling from falling out of the
                // world. For the next 0.78 s the runner has no floor within reach and every
                // test the guard could run says "outside"; this line is why it says nothing.
                // The landing rather than the drop point, because the landing is the floor —
                // if this descent's own landing turns out to be broken, the guard has
                // somewhere on the rim below to put them, not a point three metres above it.
                _bounds.Descended(_chutes[i].Landing);

                Debug.Log("[Match] §01 B" + (_chutes[i].StoreyBelow + 1) + " 도착 — "
                          + _clock.ElapsedSeconds.ToString("0") + "초", this);
                return;
            }
        }

        /// <summary>
        /// A runner who is outside the building, or falling out of it, is put back.
        /// <para>
        /// <b>The owner reported this from playing: 「맵밖으로 나갈수가있눈거같은데」.</b> It is
        /// not cosmetic. §02 is a race to the MIDDLE and everything on a floor exists to make
        /// reaching it hard; a runner outside the walls walks straight across the footprint,
        /// ignores every gate, every door and every creature, and wins. Another agent is
        /// building the physical shell, which is the right primary fix. This is the net under
        /// it, because a <c>CharacterController</c> at <c>GameConstants.RunnerSprintSpeed</c>
        /// tunnels through thin colliders, depenetration has seams, and twenty players find
        /// shapes nobody modelled. The rule, the thresholds and every derivation are in
        /// <see cref="OutOfBounds"/>; this method is the wiring.
        /// </para>
        /// <para>
        /// <b>The NavMesh IS the world here, which is what makes a NavMesh-shaped guard
        /// legal.</b> It used to be gated behind race mode, because the co-operative match
        /// had §01's 지상 and <c>SurfaceApron</c> built that ground at RUNTIME, long after
        /// the NavMesh asset was baked — so the guard would have put a player standing at the
        /// van back inside the building every match, correctly by its own rule. There is no
        /// apron and no van any more, so there is nothing in the world that is not in the
        /// bake, and the gate went with the mode.
        /// </para>
        /// <para>
        /// A runner who has been caught is skipped for the reason <see cref="CheckGrab"/>
        /// skips one: §09 takes the player out of the world and leaves the body where it
        /// fell. A body is not a runner, and moving it would move the thing the spectator is
        /// looking at.
        /// </para>
        /// <para>
        /// The controller is switched off across the write for the same reason
        /// <see cref="CheckChutes"/> and <see cref="MovePlayerToSpawn"/> switch it off: a
        /// live <c>CharacterController</c> silently ignores writes to
        /// <c>transform.position</c>, which is the exact shape of bug that makes a fix pass
        /// review and do nothing. Rotation is deliberately left alone — the player keeps the
        /// heading they were holding, so what they see is the world snapping back around
        /// them and not a respawn.
        /// </para>
        /// </summary>
        private void CheckBounds()
        {
            var root = _playerRoot;
            if (root == null)
            {
                _bounds.Idle();
                return;
            }

            var leaving = root.position;
            if (!_bounds.Tick(GameConstants.FixedStep, leaving, OutOfBounds.SampleFloor(leaving)))
            {
                return;
            }

            var to = _bounds.Recovery;
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

            // A warning, not a log line. Zero is the number a shipped map should read, so
            // every one of these is either a seam in the shell or a bug in this guard, and
            // both want to be yellow. LogWarning rather than LogError because the Test
            // Framework fails a test on an unexpected LogError and a recovery is a thing
            // that WORKED — the map is what failed.
            Debug.LogWarning(
                "[Match] §01 맵 밖 — B" + (Mathf.RoundToInt(-to.y / OutOfBounds.StoreyPitchMetres) + 1)
                + " 주자를 되돌린다: "
                + (_bounds.Reason == OutOfBoundsReason.BelowTheBuilding
                    ? "건물 아래로 떨어졌다 (마지막 발자국보다 "
                      + OutOfBounds.StoreyPitchMetres.ToString("0.00") + " m 넘게 아래)"
                    : "발밑에 바닥이 없다 (NavMesh " + OutOfBounds.FloorSearchRadiusMetres.ToString("0.0")
                      + " m 안에 아무것도, " + OutOfBounds.OutsideGraceSeconds.ToString("0.0") + "초 동안)")
                + ". 나간 자리 " + leaving.ToString("0.00") + " → 마지막 발자국 " + to.ToString("0.00")
                + " — " + Vector3.Distance(leaving, to).ToString("0.0") + " m 무효, "
                + _clock.ElapsedSeconds.ToString("0") + "초, 이번 판 " + _bounds.Recoveries + "번째.", this);

            var moved = RunnerPutBack;
            if (moved != null)
            {
                moved(leaving, to);
            }
        }

        // ------------------------------------------------------------------
        // §02. The race is the only game in the building, so it is also the
        // only thing that can end a match.
        // ------------------------------------------------------------------

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
        /// <b>The empty seats are withdrawn, and until now they were not.</b> §11 sizes a
        /// field at 2~20 and <c>RaceState.Over</c> means "nobody is still Running"; a seat
        /// that no machine ever fills stays Running for the whole match, so a field sized
        /// larger than the people in it can never close and §02's verdict can never be
        /// reached. This used to begin a field of <c>GameConstants.PlayersPerMatch</c> — the
        /// CO-OP party of four — with one runner tracked and three phantoms, which is
        /// exactly that: the race was structurally unfinishable and nothing failed. Solo is
        /// begun at <see cref="GameConstants.RaceRunnersMin"/>, the smallest field §11
        /// accepts, and the seat nobody is in is withdrawn on the spot.
        /// </para>
        /// <para>
        /// This is a scaffold and it is marked as one. §11's lobby knows how many people
        /// actually turned up (<c>RaceParty</c>), and when <c>RaceLobby</c> drives
        /// <see cref="BeginMatch"/> for a real field it is the lobby that should size it and
        /// fill the seats. What must not come back is a field size taken from a constant
        /// that describes a different game.
        /// </para>
        /// </summary>
        private void AttachRace()
        {
            _raceDirector = GetComponent<RaceDirector>() ?? gameObject.AddComponent<RaceDirector>();

            // Rebound rather than added to. AttachRace runs once per BeginMatch on a
            // component that outlives the match, so += alone would leave the previous
            // match's handlers subscribed and log every finish twice on the second race.
            _raceDirector.Finished -= OnRunnerFinished;
            _raceDirector.Retired -= OnRunnerRetired;
            _raceDirector.Closed -= RaceClosed;

            var seats = GameConstants.RaceRunnersMin;
            if (!_raceDirector.Begin(seats))
            {
                Debug.LogError("[Match] §02 refused to start with " + seats + " runners.", this);
                _raceDirector = null;
                return;
            }

            _raceDirector.Finished += OnRunnerFinished;
            _raceDirector.Retired += OnRunnerRetired;
            _raceDirector.Closed += RaceClosed;

            if (_playerRoot != null)
            {
                _raceDirector.Track(LocalSeat, _playerRoot);
            }

            for (var seat = 0; seat < seats; seat++)
            {
                if (seat != LocalSeat)
                {
                    _raceDirector.Withdraw(seat, 0f);
                }
            }

            var hud = FindFirstObjectByType<RaceHud>();
            if (hud == null)
            {
                var go = new GameObject("RaceHud");
                go.transform.SetParent(transform, false);
                hud = go.AddComponent<RaceHud>();
            }

            hud.Bind(_raceDirector);
        }

        private void OnRunnerFinished(int seat, int place)
        {
            Debug.Log("[Match] §02 " + (place == 1 ? "우승" : place + "위") + " — 좌석 " + seat
                      + ", " + _clock.ElapsedSeconds.ToString("0") + "초", this);
        }

        /// <summary>
        /// A runner stopped racing without a place. §02 탈락 — out, and unranked.
        /// <para>
        /// Logged and nothing else. The elimination itself already happened wherever the
        /// cause was: <see cref="CheckGrab"/> reports the catch, <c>RaceDirector.Tick</c>
        /// reports §07's timeout, and the standings are the single record of both.
        /// </para>
        /// </summary>
        private void OnRunnerRetired(int seat, RaceExit why)
        {
            if (why == RaceExit.Withdrawn)
            {
                // Not an elimination. §11's minimum field is two and a solo playtest fills
                // one of them, so the other is emptied on the spot — see AttachRace.
                Debug.Log("[Match] §02 빈 자리 — 좌석 " + seat + "에는 아무도 없다. §11 최소 인원을 채운 자리.", this);
                return;
            }

            Debug.Log("[Match] §02 탈락 — 좌석 " + seat + " · " + why + " · "
                      + _clock.ElapsedSeconds.ToString("0") + "초. 순위 없음, 경주는 계속된다.", this);
        }

        /// <summary>
        /// §02 has decided: nobody is still descending.
        /// <para>
        /// <b>The only thing that ends a match.</b> The co-operative verdict this replaced —
        /// <c>OutcomeEvaluator</c> over 탈출 · 손실 · 목표물 회수, drawn on <c>MatchHud</c>'s
        /// end screen — is deleted with the game it scored. §02 is now one question with one
        /// answer: who reached the middle of B8 first. <c>RaceHud</c> already draws it, and
        /// draws it brightly, because by the time it appears the reader has either finished
        /// or been eliminated and their night vision is no longer a resource anybody is
        /// charging for.
        /// </para>
        /// <para>
        /// <b>Nothing is held for a spectator any more.</b> It used to be: §09 kept the
        /// match stepping so a caught runner had a live building to look at instead of a
        /// results panel. Being caught no longer ends anybody's race — they are put back on
        /// B1 and keep running — so the only seats left when §02 closes are seats that
        /// finished, and a finisher wants the standings.
        /// </para>
        /// <para>
        /// <b>It does not lay out the next race.</b> The old path restarted itself on
        /// <c>seed + 1</c> from the end screen's continue button. §11's lobby is what agrees
        /// a seed with nineteen other machines (<c>RaceLobby.AgreedSeed</c>), and a director
        /// that quietly rebuilt the tower under a field that had not agreed to it would be
        /// twenty people in twenty different buildings.
        /// </para>
        /// </summary>
        private void RaceClosed(int winner)
        {
            Debug.Log(
                "[Match] §02 경주 종료 — " + (winner >= 0 ? "우승 좌석 " + winner : "완주자 없음 (§07 시간 초과)")
                + " · " + _clock.ElapsedSeconds.ToString("0") + "초 · seed " + _activeSeed, this);

            StopRacing();
        }

        /// <summary>Stops stepping and gives the cursor back. The standings stay on screen.</summary>
        private void StopRacing()
        {
            if (!_running)
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
        }

        // ------------------------------------------------------------------
        // §12's doors — the race's core interaction.
        // ------------------------------------------------------------------

        /// <summary>
        /// Puts <see cref="DoorInteractable"/> on every door the generator laid down.
        /// <para>
        /// The scene carries a door's geometry — a frame, a hinged leaf, a blocking
        /// collider and a carving obstacle — and not its behaviour, because
        /// <c>MapSceneBuilder</c> lives in its own editor assembly and cannot see a
        /// runtime component. Attaching here is the same arrangement §09's
        /// the chutes use, and it has the same benefit: a door works in any
        /// scene the generator writes, with nothing to remember to author.
        /// </para>
        /// <para>
        /// DESCENT-PIVOT §2② is why this matters more than it used to. In the co-operative
        /// game a shut door was a wall against the CREATURE; in a race it is a wall against
        /// the person behind you — 1.1 s to close, 4.5 s to break, and a broken one never
        /// closes again, so the leader's door is a thing the field does not have.
        /// </para>
        /// </summary>
        private void AttachDoors()
        {
            var built = 0;
            foreach (var group in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!group.name.StartsWith("Door_", StringComparison.Ordinal)
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
        /// first blocking door in it.
        /// </para>
        /// <para>
        /// The sphere is 3D and 1.4 m across, so it cannot reach through the kit's 3.75 m
        /// storey — nothing here needs the storey test <see cref="StepCreatures"/> uses,
        /// and saying so is cheaper than adding one that would never fire.
        /// </para>
        /// <para>
        /// <c>Physics.OverlapSphereNonAlloc</c> into one shared buffer: this runs once per
        /// creature per fixed step — eight times at 50 Hz — and the allocating overload
        /// would have made §12's doors 400 garbage arrays a second.
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

        // ------------------------------------------------------------------
        // §06's catch, and §02's 탈락.
        // ------------------------------------------------------------------

        /// <summary>
        /// §06 gives the state machine five states and a catch is not one of them, so
        /// whether a runner has been caught is the host's rule. The rule itself lives in
        /// <see cref="MonsterLunge"/>, engine-free and tested; this is the wiring — it
        /// feeds the struct a distance and a §06 state and does what the struct says.
        /// <para>
        /// <b>This used to be two capsules touching.</b> The instant the creature's agent
        /// radius overlapped the runner's controller radius, the runner died, and the
        /// 1.37 s <c>Grab</c> clip played over a body that was already dead. Nobody could
        /// see an attack happen because there was not one — there was a proximity test.
        /// Now the creature commits at 1.8 m, travels at 7.0 m/s for 0.55 s, and the
        /// strike either lands or costs it 0.8 s of standing still.
        /// </para>
        /// <para>
        /// <b>A catch is 탈락, and the standings are where that is recorded.</b> Nothing
        /// used to tell §02 about it: the ghost was minted, §09 took the camera,
        /// and the runner stayed <c>RacerStatus.Running</c> in <c>RaceState</c> for the rest
        /// of the match — so the race could not close, <c>RaceHud</c>'s verdict line was
        /// unreachable, and a caught player watched a scoreboard that still had them in it.
        /// <c>ReportCaught</c> below is the fix, and it is called BEFORE §09 takes the seat
        /// so that the standings are already correct when the spectator's screen first draws.
        /// </para>
        /// <para>
        /// <b>The storey test is not defensive tidying, it is the whole reason a runner can
        /// walk down this building alive.</b> The separation below is FLAT — <c>y</c> is
        /// zeroed, and it has to be, because a creature's feet and a runner's feet are at
        /// different heights on the same floor. §01 stacks eight storeys in one column and
        /// <c>DescentMap.SeedCreature</c> posts every creature at its own floor's MIDDLE,
        /// which is the SAME X and Z on all eight. So the moment there is more than one
        /// creature, a runner arriving at the middle of B3 is a flat 0.0 m from the
        /// creatures on B1, B2, B4 … B8 as well, and an unguarded loop over them would
        /// eliminate the runner instantly, from up to 26 m below, with the killer never
        /// appearing on screen. Each creature is therefore asked only about a runner on its
        /// own floor — <see cref="OnSameStorey"/>, <see cref="MapGraph.StoreyChangeMetres"/>.
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
            if (_playerRoot == null || _creatures.Count == 0)
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
                // match. A runner already being sent home is not catchable again —
                // second kill anyway, but reaching it would play the grab again over a
                // runner who is no longer there.
                //
                // A creature on another floor is never chasing this runner for the purposes
                // of a catch, however close the flat distance says it is. See the remarks.
                var reachable = OnSameStorey(monster.transform.position, here);
                var chasing = reachable && monster.State == MonsterStateId.Chase;

                var separation = here - monster.transform.position;
                separation.y = 0f;

                // Out of reach by construction when the creature is on another storey, so
                // MonsterLunge sees a distance it will never commit at and runs its
                // recovery down honestly rather than being fed a false 0 m.
                var distance = reachable ? separation.magnitude : float.PositiveInfinity;

                // Why nothing is happening, said out loud. The owner stood in front of the
                // creature and did not die, and there are three different reasons that can
                // be true at once — it is not in 추격, they are already out, or the lunge is
                // mid-recovery. Guessing between them from outside cost a rebuild; this says
                // which.
                if (reachable && distance < 6f && Time.time - _lastGrabReport > 0.5f)
                {
                    _lastGrabReport = Time.time;
                    Debug.Log("[Match] 괴물 " + distance.ToString("0.00") + " m · §06 " + monster.State
                              + " · 덮치기 " + creature.Lunge.State
                              + (chasing ? string.Empty : "  ← 추격이 아니라 판정 없음")
                              , this);
                }

                // FixedStep, not Time.deltaTime. CheckGrab runs inside StepFixed, which
                // StepMatch may call several times in one frame to burn down its
                // accumulator — so a frame delta here advanced the strike by a whole frame
                // per fixed step, and a 0.55 s commit resolved in a different number of
                // steps depending on frame rate. §06's lunge is the one window where the
                // runner's survival is decided by tenths of a second, and it was being
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
                        // §06, not §04. 주자 is what everybody in this building is — it is
                        // the whole field, not a class somebody picked — so the section that
                        // owns this line is the creature's, and dodging is a §06 miss.
                        Debug.Log("[Match] §06 덮치기를 피했다 — " + distance.ToString("0.00") + " m", this);
                        continue;

                    case LungeEvent.Hit:
                        break;

                    default:
                        continue;
                }

                var where = _playerRoot.position;

                // §02: caught is not death. The creature sends a runner back to the place
                // they started on B1 and they keep racing — everything they had is gone,
                // which after B6 is a very great deal, but the game is not.
                //
                // The ghost used to be minted here, and it is not any more. §09's spectator
                // existed because 탈락 was permanent and being sent to a menu two minutes
                // into a twenty-minute match is the most boring thing this design can do to
                // somebody. Nothing is permanent now, so there is nobody to spectate: the
                // runner is back on the rim and running before the grab clip has finished.
                Debug.Log(
                    "[Match] §06 에게 잡혔다 — " + where.ToString("0.0")
                    + " 에서 출발선으로 돌려보낸다.", this);

                // §06 forgets a target that is no longer where it was looking, and it is
                // told BEFORE the runner moves: a creature still holding this seat would
                // spend its next tick pathing toward a body that is now eight storeys up,
                // and on a floor it cannot leave that is a creature walking into a wall.
                monster.ForgetTarget(LocalSeat);

                SendBackToTheStartLine();
                LocalTimesCaught++;
                _raceDirector?.ReportCaught(LocalSeat, _clock.ElapsedSeconds);

                // The seat has resolved, so this tick is over. The other creatures' lunges
                // resume on the next one, by which time the runner is on B1's rim and none
                // of them can commit at a body — which is the rule the single-creature
                // version got from `chasing` and this one has to get from the loop ending.
                break;
            }
        }

        // ------------------------------------------------------------------
        // Layout and wiring.
        // ------------------------------------------------------------------

        /// <summary>
        /// §06 caught this runner: put them back where they started and let them run.
        /// <para>
        /// <b>Their own starting place, not a fresh one.</b> <c>RaceRunners.LocalStart</c>
        /// is the cell §13 dealt this machine at the start line, and coming back to it is
        /// the difference between a punishment and a re-roll — a runner sent to a random
        /// part of the ring might land nearer B1's way in than they began, which would make
        /// being caught occasionally lucky. It is also the only place on the map a runner is
        /// allowed to stand without having fallen into it: the 투하구 are one-way, so
        /// anywhere below B1 is unreachable from above.
        /// </para>
        /// <para>
        /// The controller goes off across the write for the same reason it does in
        /// <see cref="CheckChutes"/> and <see cref="MovePlayerToSpawn"/> — left on, it
        /// depenetrates the move away on its own next step and the runner ends up back
        /// beside the creature that just caught them.
        /// </para>
        /// </summary>
        private void SendBackToTheStartLine()
        {
            if (_playerRoot == null)
            {
                return;
            }

            var home = RaceRunners.LocalStart;

            if (home == Vector3.zero)
            {
                // No start line was ever taken — a solo playtest, or a session where §13's
                // answer never arrived. MovePlayerToSpawn's seat-0 marker is wrong for a
                // party and right for one person, and one person is exactly who is here.
                MovePlayerToSpawn();
                return;
            }

            var controller = _playerRoot.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            _playerRoot.position = home;

            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        private void MovePlayerToSpawn()
        {
            var map = _map;
            if (map == null || _playerRoot == null || map.PlayerSpawns.Count == 0)
            {
                return;
            }

            // Seat 0's marker. Right for a solo playtest and wrong for twenty machines, all
            // of which would land in this one cell — RaceRunners.TakeTheStartLine waits for
            // §13's own answer and moves the rig afterwards. That ordering is written down
            // in RaceLobby.OnSceneLoaded, which calls BeginMatch and then the coroutine.
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

        // BuildLineup() was here. It made a RoleSelection of four DISTINCT §04 RoleIds —
        // 주자 first, then whatever RoleSelection.AllRoles offered up to
        // GameConstants.PlayersPerMatch, the CO-OP party of four — and handed it to
        // MatchState's constructor on every BeginMatch. It was never played: no seat but
        // LocalSeat was read and nothing in this file asked what role anybody had. It
        // existed to satisfy a constructor, was documented as a scaffold, and survived two
        // sweeps on the strength of that word while running in every shipped match.
        //
        // 「직업도 다 없애」 · 「캐릭터는 다 똑같이 생겨도되지」. Twenty identical bodies start
        // on B1's rim. There is no lineup to build, so there is no method to keep.

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

                // One argument, and that is the whole of §08's deletion seen from here. It
                // used to be Bind(this, motor, loadout, flashlight): the interactor needed
                // the pockets to refuse a 전리품 while the 목표물 was in both hands, and the
                // torch to take it out of the hand that was about to be full. There is
                // nothing to pick up in a race — the only thing the key still opens and
                // shuts is §12's door.
                _interactor.Bind(this);
            }

        }

        /// <summary>
        /// Stands one §06 creature on every declared start and takes the primary's probe,
        /// which is what proves the scene's NavMesh is baked.
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
        /// PlayMode test that calls <see cref="BeginMatch"/> goes red, loudly, naming both
        /// counts. The reverse — this half landing without the map's — is the shipped map's
        /// own case: one start, one creature, equal, and the audit still says <em>1 of 8
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

        private void CollectAreaLights()
        {
            _areaLights.Clear();

            var lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < lights.Length; i++)
            {
                // Point lights only. §12's area sources are the outer ring's 구역 조명 and
                // the light burning over §02's 도착점; a runner's own beam is a spot and is
                // already accounted for as the beam.
                if (lights[i].type == LightType.Point)
                {
                    _areaLights.Add(lights[i]);
                }
            }
        }

        private void ClearWorld()
        {
            _running = false;

            DespawnClonedCreatures();

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
        /// reference on this component points at it, and a second <see cref="BeginMatch"/>
        /// has to find it exactly where the first one did.
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
