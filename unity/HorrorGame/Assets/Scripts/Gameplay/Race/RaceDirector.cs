#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Race;
using HorrorGame.Core.Threat;
using HorrorGame.Gameplay.Match;
using HorrorGame.Net;
using HorrorGame.UI;
using Mirror;
using UnityEngine;

namespace HorrorGame.Gameplay.Race
{
    /// <summary>
    /// Why a runner stopped racing, in the words §02 uses.
    /// <para>
    /// <see cref="RacerStatus"/> has one way to be out and that is correct — §02 ranks
    /// finishers and nothing else, so a rule that distinguished a corpse from a
    /// latecomer would be inventing a scoreboard for the unranked. But a player is owed
    /// the reason on their own screen: "잡혔다" and "시간 초과" are the same standing and
    /// very different evenings. So the reason lives here, on the body, and the rule stays
    /// as narrow as it was written.
    /// </para>
    /// </summary>
    public enum RaceExit
    {
        /// <summary>Still descending.</summary>
        Racing = 0,

        /// <summary>Touched the middle of B8. §02 승리 if the place was 1, 완주 otherwise.</summary>
        Finished = 1,

        /// <summary>§02 탈락 — the creature caught them. §09 turns them into a spectator.</summary>
        Caught = 2,

        /// <summary>§02 시간 초과 — §07 ran out of table with nobody home. 전원 패배.</summary>
        TimedOut = 3,

        /// <summary>The seat emptied: a disconnect, or a field padded up to §11's minimum.</summary>
        Withdrawn = 4,
    }

    /// <summary>
    /// §02 in the scene: the body that feeds <see cref="RaceState"/> and reacts to it.
    /// <para>
    /// <b>The split, and why it is worth a whole component.</b> <see cref="RaceState"/> is
    /// the rule — engine-free, deterministic, tested, and the only thing that decides who
    /// won. It cannot see a scene, so it cannot know that somebody is standing on the
    /// finish. This is the half that looks: it holds the runners' bodies, measures them
    /// against the middle of B8, and turns what it finds into the three calls the rule
    /// accepts. Nothing here decides anything; every verdict is a return value from
    /// <see cref="RaceState"/>.
    /// </para>
    /// <para>
    /// <b>Finishing is not winning, and this component is where that survives or dies.</b>
    /// §02 records a place for everybody who reaches the bottom — "1등이 아니어도 완주
    /// 순위는 남는다. 그래서 선두를 놓친 사람에게도 계속 달릴 이유가 있고, 마지막 한
    /// 층에서 3등이 2등을 문으로 막는 일이 벌어진다." The obvious way to build a race is
    /// to end it when the first runner arrives, and that one line would delete five
    /// storeys of play for nineteen people. So:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="Finished"/> fires for <em>every</em> finisher with
    /// their place, not once for the winner.</description></item>
    /// <item><description>There is no event, method or flag on this component that means
    /// "somebody won, stop". <see cref="Closed"/> fires on <see cref="RaceState.Over"/>
    /// and on nothing else — when nobody is still running.</description></item>
    /// <item><description>The console says so out loud the moment the winner is decided,
    /// because the wrong wiring is invisible in code review and obvious in a log.</description></item>
    /// </list>
    /// <para>
    /// <b>Driven, not self-driving.</b> No <c>Update</c>, no <c>FixedUpdate</c>: one
    /// <see cref="Tick"/> per fixed step from whoever owns the match order, plus
    /// <see cref="ReportCaught"/> and <see cref="ReportDescent"/> as they happen. §13's
    /// replay guarantee is a property of a fixed call order, and a component that stepped
    /// itself would be a second, unordered opinion about when §02 runs.
    /// </para>
    /// <para>
    /// <b>Host only — and until now that sentence was a comment rather than a
    /// mechanism.</b> §02: "도착 판정을 클라이언트가 내리면 경주 게임에서 가장 먼저
    /// 조작되는 값이 된다." One of these ran on <em>every</em> machine, each fed by its own
    /// <c>MatchDirector</c> from its own scene, each seating the local player at seat 0.
    /// Twenty people in one match held twenty private scoreboards and §13's authority
    /// applied to nothing.
    /// </para>
    /// <para>
    /// <b>So this class is now two things, chosen once at <see cref="Begin"/> by
    /// <see cref="NetRace.ThisMachineJudges"/>.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>On the host</b> (and offline, which is the same thing with a
    /// field of one) it is what it always was: it holds the one <see cref="RaceState"/>,
    /// measures bodies against §02's finish, and — new — publishes the result through
    /// <see cref="NetRace"/> after every accepted change. It also implements
    /// <see cref="IRaceAuthority"/>, which is the whole of what the Net layer is allowed to
    /// ask of the rule.</description></item>
    /// <item><description><b>On a client</b> it holds no <see cref="RaceState"/> at all —
    /// <see cref="Rules"/> is null, deliberately and structurally, because a client with a
    /// rule object is a client that can answer §02's questions on its own. Every member of
    /// <see cref="IRaceReadout"/> resolves through <c>NetRace.Standings</c>, which is only
    /// ever written by a message handler. The HUD is unchanged and cannot tell the
    /// difference, which is the point: it never named this class.</description></item>
    /// </list>
    /// <para>
    /// <b>The two reports go up, the standings come down.</b> A 투하구 and §06's grab are
    /// detected on the machine the player is sitting at — the chutes are colliders in the
    /// local scene, and the creature is simulated locally on every machine because
    /// <c>MatchDirector</c> contains no reference to Mirror and <c>NetMonster</c> has no
    /// consumer. <see cref="ReportLocalDescent"/> and <see cref="ReportLocalCaught"/> are
    /// therefore the only two calls in the game that a client may make about §02, and both
    /// are requests: the host's <see cref="AcceptDescent"/> and <see cref="AcceptCaught"/>
    /// return the verdict, and the client learns it the same way everybody else does — from
    /// the next broadcast.
    /// </para>
    /// <para>
    /// <b>It is also <see cref="IRaceReadout"/>, which is a one-way window.</b>
    /// <c>RaceHud</c> names the interface and never this class, because the default
    /// assembly references <c>HorrorGame.UI</c> and not the reverse. Everything on that
    /// interface is a reading: there is deliberately no way for a screen to report an
    /// arrival, so a HUD that wanted to cheat would have to grow the method in review.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaceDirector : MonoBehaviour, IRaceReadout, IRaceAuthority
    {
        /// <summary>
        /// What <c>DescentMap.MarkPlaces</c> calls the finish: the middle of the deepest
        /// storey, marked <c>MapNodeKind.Entrance</c> because §12's rules are written
        /// against 출입구 — and it is one, since the only way out of this building is down.
        /// <para>
        /// Looked for first and, today, never found — which is the point of writing it down.
        /// The name is a <em>graph node</em> name, and <c>MapSketch.BuildMarkers</c> does not
        /// carry node names into the scene: an Entrance node becomes an <c>EntranceLight</c>
        /// named after its zone and node index instead. Building the map at
        /// <c>DescentMap.DefaultSeed</c> gives node <c>B8_도착점</c> at (31.25, −26.25, 31.25)
        /// and one marker at that same point, <c>EntranceLight_B8 굴착층_910</c>. So the light
        /// is what actually locates the finish, and this constant is here so that a scene
        /// which ever does name the spot wins the lookup outright instead of being
        /// second-guessed by a light.
        /// </para>
        /// </summary>
        public const string FinishMarkerName = "B8_도착점";

        /// <summary>
        /// How near the middle of B8 counts as arriving, metres.
        /// <para>
        /// <b>2.5 m is one cell</b> (<c>MapKitCatalogue.GridMetres</c>), and the finish is
        /// marked on the centre <em>cell</em> — so this is exactly "standing on the square
        /// the map calls the finish, or reaching into it."
        /// </para>
        /// <para>
        /// <b>It cannot be tripped from outside the room.</b> <c>RadialStorey</c> lays
        /// <c>Chamber_Open_3x3</c> over the middle: three cells square, 7.5 m across, so
        /// its walls are 3.75 m from the centre. A 2.5 m circle is wholly inside that
        /// chamber and never reaches the corridor feeding it — a runner queuing at the
        /// last gate has not finished.
        /// </para>
        /// <para>
        /// <b>Nobody can cross it between two samples.</b> §05's sprint is 5.6 m/s and
        /// <see cref="GameConstants.FixedStep"/> is 1/50 s, so the fastest a runner moves
        /// is 0.112 m per tick. The circle is 22 ticks wide.
        /// </para>
        /// <para>
        /// <b>Wider than <see cref="Chute.MouthRadiusMetres"/> (1.4 m) on purpose.</b> A
        /// 투하구 is something you step <em>into</em>, and missing it costs you another lap
        /// of the middle — a real, recoverable mistake. The finish is something you
        /// <em>touch</em>, and a runner who sprinted through the middle of B8 and was not
        /// recorded would be the worst bug this game could have.
        /// </para>
        /// </summary>
        public const float FinishRadiusMetres = 2.5f;

        [Header("§02 승패 조건")]
        [SerializeField]
        [Tooltip("§02 시간 초과, seconds. 0 or less uses §07's own exhaustion time — the moment the threat table runs out.")]
        private float _timeoutSeconds;

        [SerializeField]
        [Tooltip("Log every descent, arrival and elimination. §02 is decided in a handful of frames and they are hard to see any other way.")]
        private bool _verbose = true;

        /// <summary>Every runner whose body this host is watching. Kept ordered by seat — see <see cref="Track"/>.</summary>
        private readonly List<Runner> _runners = new List<Runner>();

        /// <summary>Runners inside the finish this tick, reused so a 50 Hz check allocates nothing.</summary>
        private readonly List<Crossing> _crossing = new List<Crossing>();

        /// <summary>The scoreboard <see cref="Board"/> hands out, reused for the same reason.</summary>
        private readonly List<Racer> _board = new List<Racer>();

        /// <summary>Scratch for <see cref="Board"/>'s tail — the unranked, gathered before they are sorted.</summary>
        private readonly List<Racer> _retired = new List<Racer>();

        /// <summary>What to call each seat on the results screen. See <see cref="NameOf"/>.</summary>
        private string[] _names = Array.Empty<string>();

        private RaceState? _rules;
        private RaceExit[] _exits = Array.Empty<RaceExit>();
        private Vector3 _finish;
        private float _elapsedSeconds;
        private bool _finishFound;
        private bool _over;
        private bool _winnerAnnounced;

        /// <summary>
        /// Whether this machine decides §02. Taken once, at <see cref="Begin"/>, from
        /// <see cref="NetRace.ThisMachineJudges"/>.
        /// <para>
        /// Latched rather than asked per tick, and the difference is a real failure mode: a
        /// client whose host drops mid-match would see <c>NetworkClient.active</c> go false
        /// and start judging its own arrivals — awarding itself first place in a race that
        /// has already ended for everybody. §13 ends a session rather than migrating it, so
        /// the answer taken at the start line is the answer for the whole descent.
        /// </para>
        /// </summary>
        private bool _judging = true;

        /// <summary>
        /// §11's field size, kept separately from <see cref="RaceState.Count"/> because a
        /// client has no <see cref="RaceState"/> to ask.
        /// </summary>
        private int _fieldSize;

        /// <summary>Set when the rule accepted something the rest of the field has not been told about yet.</summary>
        private bool _standingsDirty;

        /// <summary>Next unscaled time a heartbeat frame is due. See <see cref="NetRace.HeartbeatSeconds"/>.</summary>
        private float _nextHeartbeat;

        /// <summary>Next unscaled time the host re-scans for runner bodies. Same cadence as the heartbeat.</summary>
        private float _nextBodyScan;

        /// <summary>
        /// A runner has dropped a storey. Seat, then the storey they landed on (0 is B1).
        /// <para>
        /// Raised after <see cref="RaceState"/> accepted it, so a rejected report — §12's
        /// chutes are one-way and the rule drops anything that goes backwards — is silent.
        /// </para>
        /// </summary>
        public event Action<int, int>? Descended;

        /// <summary>
        /// A runner reached the middle of B8. Seat, then their place — 1 for the winner.
        /// <para>
        /// <b>Fires for every finisher.</b> This is §02's 완주 row and the reason the
        /// eighteenth place is worth having. Anything that listens for "the race is decided"
        /// wants <see cref="Closed"/>, not this.
        /// </para>
        /// </summary>
        public event Action<int, int>? Finished;

        /// <summary>
        /// A runner stopped racing without a place. Seat, then why — caught, timed out or
        /// withdrawn. §09 wants this: it is the moment a player becomes a spectator.
        /// </summary>
        public event Action<int, RaceExit>? Retired;

        /// <summary>
        /// The race is over — nobody is still running. Carries the winner's seat, or −1 for
        /// §02's 시간 초과, where nobody arrived and everybody lost.
        /// <para>
        /// <b>The only signal that should end a match.</b> It is deliberately not raised
        /// when the winner is decided; see the class remarks.
        /// </para>
        /// </summary>
        public event Action<int>? Closed;

        /// <summary>
        /// §02's rules — the standings, and the only thing that decides who won. Null until
        /// <see cref="Begin"/>.
        /// <para>
        /// Called <c>Rules</c> and not <c>Race</c> because the whole point of this component
        /// is that it is not the rule. Read it; do not report to it directly, or the
        /// director's events and exit reasons will describe a race that already moved on.
        /// </para>
        /// </summary>
        public RaceState? Rules
        {
            get { return _rules; }
        }

        /// <summary>
        /// True once <see cref="Begin"/> has sized a field. Nothing happens before that.
        /// <para>
        /// The field size and not <see cref="Rules"/>, because a client is started and has
        /// no rule: it renders the host's. Asking <c>_rules != null</c> would make every
        /// client look like a race that never began.
        /// </para>
        /// </summary>
        public bool Started
        {
            get { return _fieldSize > 0; }
        }

        /// <summary>
        /// Whether this machine is the one deciding §02 — arrivals, the timeout, and when
        /// the race closes. False on a client, which renders the standings it is sent.
        /// </summary>
        public bool Judging
        {
            get { return _judging; }
        }

        /// <summary>
        /// True once <see cref="RaceState.Over"/> went true and <see cref="Closed"/> was
        /// raised. On a client, true once the host said so — a client never closes a race.
        /// </summary>
        public bool Over
        {
            get { return _rules != null ? _over : NetRace.Standings.Over; }
        }

        /// <summary>
        /// Where the middle of B8 is. Meaningful only when <see cref="FinishFound"/>.
        /// <para>
        /// <b>X and Z are exact; Y is a ceiling.</b> The centre cell of the descent map's
        /// bottom storey is (31.25, 31.25) in plan and that is what this reports, to the
        /// centimetre. The height is the light fitting's — <c>MapSceneBuilder.BuildLight</c>
        /// hangs it <c>CorridorClearWidth + 0.6</c> = 2.8 m over a floor at −26.25 m — and
        /// that is the right height for what a HUD would do with it, because §03 makes the
        /// destination a light visible from anywhere on the inner ring: "중심은 투하구만
        /// 빛난다 … 목적지가 보인다는 것이 중요하다". The arrival test uses nothing but X and
        /// Z. See <see cref="Tick"/> for why that is not a shortcut.
        /// </para>
        /// </summary>
        public Vector3 Finish
        {
            get { return _finish; }
        }

        /// <summary>
        /// Whether the finish was located. False means <b>no runner can ever win this
        /// match</b>, which is why <see cref="Begin"/> shouts about it rather than
        /// returning quietly.
        /// </summary>
        public bool FinishFound
        {
            get { return _finishFound; }
        }

        /// <summary>
        /// §02 시간 초과, seconds. Defaults to <see cref="ThreatCurve.FinalTierStartSeconds"/>
        /// — 32 min, the point §07's table runs out and 동트기 전 begins.
        /// <para>
        /// §02 words the timeout as "§07이 마지막 단계를 지난다", so the number belongs to
        /// §07 and is quoted here rather than chosen. Overridable in the inspector for
        /// playtests that do not want to sit through half an hour.
        /// </para>
        /// </summary>
        public float TimeoutSeconds
        {
            get { return _timeoutSeconds > 0f ? _timeoutSeconds : ThreatCurve.FinalTierStartSeconds; }
            set { _timeoutSeconds = value; }
        }

        /// <summary>The winner's seat, or −1 while nobody has arrived. §02 승리.</summary>
        public int WinnerId
        {
            get { return _rules != null ? _rules.WinnerId : NetRace.Standings.WinnerId; }
        }

        /// <summary>How many have reached the bottom. §02 완주 counts them all, not just the first.</summary>
        public int Finishers
        {
            get
            {
                var rules = _rules;
                if (rules != null)
                {
                    return rules.Finishers;
                }

                var counted = 0;
                var standings = NetRace.Standings;
                for (var seat = 0; seat < standings.RunnerCount; seat++)
                {
                    if (standings.RowOf(seat).Status == RacerStatus.Finished)
                    {
                        counted++;
                    }
                }

                return counted;
            }
        }

        /// <summary>
        /// How many are still descending. Zero is what ends the match.
        /// <para>
        /// On a client this is the length of the host's own live order, not a re-derived
        /// count: <c>RaceStandingsMessage.LiveOrder</c> names exactly the seats the host
        /// considers still running.
        /// </para>
        /// </summary>
        public int StillRunning
        {
            get
            {
                var rules = _rules;
                if (rules == null)
                {
                    return NetRace.Standings.Standings.Count;
                }

                var live = 0;
                for (var i = 0; i < rules.Count; i++)
                {
                    if (rules[i].Status == RacerStatus.Running)
                    {
                        live++;
                    }
                }

                return live;
            }
        }

        /// <summary>
        /// Sizes §02 to the field that actually turned up, finds the finish, and decides
        /// which of the two things this component is for the rest of the match.
        /// <para>
        /// The count is the real one, not <see cref="GameConstants.RaceRunnersMax"/>: a
        /// twenty-seat race with six people in it never satisfies
        /// <see cref="RaceState.Over"/>, because fourteen seats stay Running forever and
        /// the match cannot end. Seats that empty later are <see cref="Withdraw"/>n.
        /// </para>
        /// <para>
        /// <b>The <see cref="RaceState"/> is built on the host and nowhere else.</b> That is
        /// §13 as a structure rather than as a convention: on a client there is no object in
        /// this process that can answer "who is second", so no future edit can accidentally
        /// ask one. It also means <see cref="Rules"/> being null is the test for "this
        /// machine renders rather than decides", and a test can assert it.
        /// </para>
        /// <para>
        /// <b>The seat comes from the lobby, not from a constant.</b>
        /// <see cref="RaceParty.LocalSeat"/> is the row §11 dealt this machine; every
        /// machine used to draw its HUD as seat 0 because nothing ever set
        /// <see cref="LocalRacerId"/> at all — the gauge and the verdict were unreachable in
        /// the shipped build, since <c>RaceHud</c> draws both only when the seat is ≥ 0.
        /// </para>
        /// </summary>
        /// <param name="runners">Seats in this match. §11: 2~20.</param>
        /// <returns>
        /// False if §11 refuses the field, in which case nothing starts and every later
        /// call is a no-op. Refused rather than clamped — a race quietly resized to
        /// somebody else's number would report places for people who are not in it.
        /// </returns>
        public bool Begin(int runners)
        {
            if (runners < GameConstants.RaceRunnersMin || runners > GameConstants.RaceRunnersMax)
            {
                Debug.LogError(
                    "[Race] §11은 " + GameConstants.RaceRunnersMin + "~" + GameConstants.RaceRunnersMax
                    + "명을 받는다. " + runners + "명으로는 경주가 시작되지 않는다. "
                    + "혼자 달리는 판을 예행하려면 " + GameConstants.RaceRunnersMin
                    + "명으로 시작하고 빈 자리는 Withdraw로 비워라.",
                    this);
                return false;
            }

            _judging = NetRace.ThisMachineJudges;
            _fieldSize = runners;
            _rules = _judging ? new RaceState(runners) : null;
            _exits = new RaceExit[runners];
            _names = new string[runners];
            _runners.Clear();
            _crossing.Clear();
            _elapsedSeconds = 0f;
            _over = false;
            _winnerAnnounced = false;
            _standingsDirty = false;
            _nextHeartbeat = 0f;
            _nextBodyScan = 0f;

            LocalRacerId = RaceParty.Settled
                ? Mathf.Clamp(RaceParty.LocalSeat, 0, runners - 1)
                : 0;

            // §02's board is names, and SetName has no caller on a client — the roster
            // that knew them was destroyed by the scene load. RaceLobby hands them to
            // RaceParty on its way out and this is where they are picked up, on the host
            // and the client alike, so both machines draw one set of names rather than the
            // host drawing people and every client drawing 1번 … 20번.
            var lobbyNames = RaceParty.SeatNames;
            for (var seat = 0; seat < lobbyNames.Length && seat < _names.Length; seat++)
            {
                _names[seat] = lobbyNames[seat];
            }

            if (_judging)
            {
                // The seam the Net layer reports into. Installed even offline, where
                // NetRace.ReportDescent's own NetworkServer.active check makes it inert —
                // one installation path is worth more than a branch that only the networked
                // build ever takes, because a branch only the networked build takes is a
                // branch no offline test covers.
                NetRace.Authority = this;
                BindNetworkedRunners();
            }
            else
            {
                NetRace.Authority = null;
            }

            if (!_finishFound)
            {
                LocateFinish();
            }

            if (!_finishFound && _judging)
            {
                Debug.LogError(
                    "[Race] §02의 도착점을 찾지 못했다 — 이 판은 아무도 이길 수 없다. "
                    + "'" + FinishMarkerName + "' 이름의 오브젝트도, '" + MatchMap.EntranceLightPrefix
                    + "…' 조명도 '" + MatchMap.MapRootName + "' 아래에 없다. "
                    + "DescentMap이 만든 씬인지 확인하거나 BindFinish로 직접 지정하라.",
                    this);
            }

            Debug.Log(
                "[Race] §02 하강 시작 — " + runners + "명, 이 기계의 좌석 " + LocalRacerId + "번, "
                + (_judging ? "§13 판정자(호스트)" : "§13 관전 렌더러(클라이언트)")
                + ", 도착점은 B" + RaceState.Storeys + " 중심"
                + (_finishFound ? " " + _finish.ToString("0.0") : " (미발견)"), this);

            if (_judging)
            {
                NetRace.Broadcast();
            }

            return true;
        }

        /// <summary>
        /// Overrides where the finish is, in world space.
        /// <para>
        /// For a scene the generator did not write, and for tests, which need a finish
        /// without a building around it. Binding before <see cref="Begin"/> skips the
        /// lookup entirely.
        /// </para>
        /// </summary>
        /// <param name="position">The middle of the deepest storey.</param>
        public void BindFinish(Vector3 position)
        {
            _finish = position;
            _finishFound = true;
        }

        /// <summary>
        /// Watches a runner's body. Nothing is measured against a seat that was never
        /// tracked, so this is what puts a player in the race as far as arrival is
        /// concerned.
        /// <para>
        /// Kept in seat order rather than call order: two runners crossing on the same tick
        /// are separated by how far past the line they are, and where that is a tie the
        /// lower seat goes first. §13's replay guarantee needs the tie broken the same way
        /// on every machine far more than it needs it broken fairly.
        /// </para>
        /// </summary>
        /// <param name="runnerId">Seat index.</param>
        /// <param name="body">The transform that walks. Re-tracking a seat replaces it.</param>
        public void Track(int runnerId, Transform body)
        {
            var rules = _rules;
            if (rules == null || runnerId < 0 || runnerId >= rules.Count || body == null)
            {
                return;
            }

            for (var i = 0; i < _runners.Count; i++)
            {
                if (_runners[i].Id == runnerId)
                {
                    _runners[i] = new Runner(runnerId, body);
                    return;
                }
            }

            var at = _runners.Count;
            while (at > 0 && _runners[at - 1].Id > runnerId)
            {
                at--;
            }

            _runners.Insert(at, new Runner(runnerId, body));
        }

        /// <summary>
        /// Stops watching a body. Called automatically the moment a runner finishes or goes
        /// out — §09 leaves a dead player in the world as a ghost, and a ghost drifting
        /// through the middle of B8 must not be measured against the finish.
        /// </summary>
        /// <param name="runnerId">Seat index.</param>
        public void Untrack(int runnerId)
        {
            for (var i = 0; i < _runners.Count; i++)
            {
                if (_runners[i].Id == runnerId)
                {
                    _runners.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// One step of §02. Measures every tracked runner against the finish, applies §02's
        /// 시간 초과, and closes the race when nobody is left running.
        /// <para>
        /// <b>The arrival test is horizontal, and the vertical half is the rule rather than
        /// the geometry.</b> Every storey of this building sits directly on top of the last
        /// — <c>DescentMap</c>: "every floor can occupy the same square and the whole
        /// building is one tower" — so the middle of B1 is directly above the finish, and a
        /// height comparison is the fragile way to tell them apart. <see cref="RaceState"/>
        /// already refuses a finish from anyone whose storey is not the bottom one, which is
        /// the same question asked of the thing that actually knows: the record of who
        /// dropped down which chute. The check is repeated here before the distance test
        /// only so that nineteen runners crossing the middle of B3 do not spend a tick each
        /// being turned down.
        /// </para>
        /// <para>
        /// <b>Nothing in here runs on a client, and that is the §02 fix.</b>
        /// <see cref="Rules"/> is null there, so the first line returns — no arrival is
        /// judged, no timeout is applied and no race is closed on a machine that is not the
        /// host. A client's clock and standings come from
        /// <see cref="RaceStandingsMessage"/> instead, which is why
        /// <see cref="ElapsedSeconds"/> does not read the field this method writes when
        /// there is no rule to write it for.
        /// </para>
        /// </summary>
        /// <param name="elapsedSeconds">Seconds since the start. Becomes a finisher's time.</param>
        public void Tick(float elapsedSeconds)
        {
            var rules = _rules;
            if (rules == null || _over)
            {
                return;
            }

            _elapsedSeconds = elapsedSeconds;

            CheckFinish(rules, elapsedSeconds);
            CheckTimeout(rules, elapsedSeconds);
            CheckOver(rules);
            Publish();
        }

        /// <summary>
        /// Sends the standings out if anything changed, and once a second regardless. Host
        /// only; called at the end of every <see cref="Tick"/>.
        /// <para>
        /// <b>Two cadences, because there are two kinds of change.</b> A descent, a finish
        /// or a catch is a discrete event a player just caused and must see immediately, so
        /// the accepting call marks the standings dirty and this flushes on the same step —
        /// one fixed step, 20 ms, and not a frame later. The race clock changes continuously
        /// and is printed as mm:ss, so it rides a <see cref="NetRace.HeartbeatSeconds"/>
        /// heartbeat instead of a per-tick frame. Broadcasting the table at 50 Hz would be
        /// ~9.5 kB/s per observer for a screen that redraws at 5 Hz.
        /// </para>
        /// <para>
        /// The body re-scan shares the heartbeat: a runner whose <see cref="NetPlayer"/> was
        /// spawned after the race began — or replaced across a scene load — is picked up
        /// within a second rather than never. <see cref="Track"/> replaces by seat, so
        /// re-scanning is idempotent.
        /// </para>
        /// </summary>
        private void Publish()
        {
            if (!_judging)
            {
                return;
            }

            var now = Time.unscaledTime;

            if (now >= _nextBodyScan)
            {
                _nextBodyScan = now + NetRace.HeartbeatSeconds;
                BindNetworkedRunners();
            }

            if (!_standingsDirty && now < _nextHeartbeat)
            {
                return;
            }

            _standingsDirty = false;
            _nextHeartbeat = now + NetRace.HeartbeatSeconds;
            NetRace.Broadcast();
        }

        /// <summary>
        /// Puts the host's copy of every networked runner in front of §02's finish circle,
        /// and tells each one which seat it is.
        /// <para>
        /// <b>The body the host measures must be the host's, not the player's.</b>
        /// <c>NetPlayer.CmdReportView</c> writes <c>transform.position</c> on the server
        /// from the clamped value it just accepted, so the transform on the host's copy of a
        /// remote runner is §13's own answer to "where is that person" — speed-limited,
        /// arrived over a socket, and not something the owner can set directly. Measuring
        /// the local first-person rig instead would be right for exactly one seat and wrong
        /// for nineteen; before this, <c>MatchDirector.AttachRace</c> tracked only the local
        /// rig, so on the host nobody else could finish and on a client the only person who
        /// could was themselves.
        /// </para>
        /// <para>
        /// <b>The seat comes from §11's lobby.</b> <see cref="RaceParty.SeatConnectionIds"/>
        /// is the host-side seat → connection map <c>RaceLobby</c> settled immediately before
        /// the descent loaded, so seat <em>i</em> is whoever holds connection
        /// <c>SeatConnectionIds[i]</c> and a client never chooses its own number.
        /// <c>NetPlayer.AssignSeat</c> had exactly one caller —
        /// <c>HorrorGameNetworkManager.OnServerAddPlayer</c>, which Mirror never reaches on
        /// this project's spawn path (see <c>OnServerReady</c>) — so every runner in every
        /// shipped session carried seat −1, and a report from one could not have been
        /// attributed to anybody.
        /// </para>
        /// </summary>
        private void BindNetworkedRunners()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            var seats = RaceParty.SeatConnectionIds;
            for (var seat = 0; seat < seats.Length && seat < _fieldSize; seat++)
            {
                if (!NetworkServer.connections.TryGetValue(seats[seat], out var connection)
                    || connection == null
                    || connection.identity == null
                    || !connection.identity.TryGetComponent(out NetPlayer player))
                {
                    continue;
                }

                if (player.SeatIndex != seat)
                {
                    player.AssignSeat(seat);
                }

                // Not the seat this machine is sitting in: that one is the first-person rig
                // MatchDirector already tracked, and it is a better body than the replicated
                // proxy because it has not been through a quantised round trip.
                if (seat != LocalRacerId)
                {
                    Track(seat, player.transform);
                }
            }
        }

        /// <summary>
        /// This machine's own runner fell through a 투하구. The one call
        /// <c>MatchDirector.CheckChutes</c> should make.
        /// <para>
        /// <b>On the host it is the rule; on a client it is a request.</b> That branch is
        /// the whole of §13 as far as a descent is concerned, and putting it here rather
        /// than in <c>MatchDirector</c> keeps Mirror out of the class that steps the match —
        /// which today contains no reference to it at all.
        /// </para>
        /// </summary>
        /// <param name="storey">The storey landed on. 0 is B1.</param>
        /// <param name="elapsedSeconds">Seconds since the start, on this machine's clock. Ignored on a client — the host times it.</param>
        /// <returns>
        /// True if the host's rule accepted it. <b>Always false on a client</b>, where the
        /// answer has not arrived yet: the caller must not treat that as a refusal, and
        /// nothing in the game does — the standings arrive as a broadcast a round trip later.
        /// </returns>
        public bool ReportLocalDescent(int storey, float elapsedSeconds)
        {
            if (_judging)
            {
                var accepted = ReportDescent(LocalRacerId, storey, elapsedSeconds);
                if (accepted)
                {
                    // The host's own clamp forgiveness. A client gets this inside
                    // CmdReportDescent; the host never sends that command, so without this
                    // line the host is the one runner in the session whose avatar crawls
                    // the twenty-odd metres from the chute to the rim below at 5.6 m/s.
                    NetRace.ForgiveLocalClamp();
                }

                return accepted;
            }

            NetRace.SendDescent(storey);
            return false;
        }

        /// <summary>
        /// §06's creature caught this machine's own runner. The one call
        /// <c>MatchDirector.CheckGrab</c> should make. See <see cref="ReportLocalDescent"/>
        /// for the branch and for what the return value does and does not mean.
        /// </summary>
        /// <param name="elapsedSeconds">Seconds since the start, on this machine's clock.</param>
        /// <returns>True if the host's rule accepted it; always false on a client.</returns>
        public bool ReportLocalCaught(float elapsedSeconds)
        {
            if (_judging)
            {
                var accepted = ReportCaught(LocalRacerId, elapsedSeconds);
                if (accepted)
                {
                    // Same as the descent, and the distance is larger: B8's middle to your
                    // own B1 cell is about thirty-eight metres through eight floors.
                    NetRace.ForgiveLocalClamp();
                }

                return accepted;
            }

            NetRace.SendCaught();
            return false;
        }

        /// <summary>
        /// A runner landed on a lower storey. Forwards §12's one-way drop to the rule, which
        /// is what later lets them finish at all.
        /// <para>
        /// Host side. A client reaches this only through <see cref="AcceptDescent"/>, one
        /// round trip and one seat check earlier.
        /// </para>
        /// </summary>
        /// <param name="runnerId">Seat index.</param>
        /// <param name="storey">Storey they landed on. 0 is B1.</param>
        /// <param name="elapsedSeconds">Seconds since the start.</param>
        /// <returns>True if this moved them; false if the rule dropped it.</returns>
        public bool ReportDescent(int runnerId, int storey, float elapsedSeconds)
        {
            var rules = _rules;
            if (rules == null || _over || runnerId < 0 || runnerId >= rules.Count)
            {
                return false;
            }

            if (!rules.ReportDescent(runnerId, storey, elapsedSeconds))
            {
                return false;
            }

            _standingsDirty = true;

            if (_verbose)
            {
                Debug.Log(
                    "[Race] §01 " + runnerId + "번 B" + (storey + 1) + " 착지 — "
                    + elapsedSeconds.ToString("0") + "초", this);
            }

            Descended?.Invoke(runnerId, storey);
            return true;
        }

        /// <summary>
        /// The creature caught a runner. §02 탈락 — out, unranked, no revival.
        /// <para>
        /// Unranked is the point. §01 makes the creature a hazard rather than the
        /// antagonist, and a game that placed corpses by how deep they got would pay people
        /// for dying in the right order.
        /// </para>
        /// </summary>
        /// <param name="runnerId">Seat index.</param>
        /// <param name="elapsedSeconds">Seconds since the start.</param>
        /// <returns>
        /// True if this sent them back; false if they had already finished or left.
        /// </returns>
        public bool ReportCaught(int runnerId, float elapsedSeconds)
        {
            var rules = _rules;
            if (rules == null || _over || runnerId < 0 || runnerId >= rules.Count)
            {
                return false;
            }

            var storey = rules[runnerId].Storey;

            if (!rules.ReportCaught(runnerId, elapsedSeconds))
            {
                return false;
            }

            _standingsDirty = true;

            // Not Untrack, and not an entry in _exits: a runner who has been caught has not
            // stopped racing, so taking them off the standings would leave a live player
            // invisible on every HUD in the match. Retire is now only reached by Withdraw.
            if (_verbose)
            {
                Debug.Log(
                    "[Race] §02 " + runnerId + "번이 B" + (storey + 1) + " 에서 잡혔다 — "
                    + elapsedSeconds.ToString("0") + "초. 출발선으로 돌려보낸다. "
                    + storey + "개 층을 다시 내려가야 한다 (" + rules[runnerId].TimesCaught
                    + "번째).", this);
            }

            Caught?.Invoke(runnerId, storey);
            return true;
        }

        /// <summary>
        /// §06's creature caught a runner and sent them back to B1. Carries the storey they
        /// were on, which is what the loss actually was.
        /// <para>
        /// Raised instead of <see cref="Retired"/>, and that difference is the change: a
        /// caught runner is still in the standings, still on somebody's screen, and still
        /// able to win. Anything listening for <c>Retired</c> to take a player off a board
        /// must not fire here.
        /// </para>
        /// </summary>
        public event Action<int, int>? Caught;

        // ------------------------------------------------------------------
        // IRaceAuthority — the only door §02 opens to a client's report, and the
        // two guards on it. Host side; NetRace refuses to call these anywhere else.
        // ------------------------------------------------------------------

        /// <summary>
        /// One seat's standing. <see cref="IRaceAuthority"/>'s read of the table
        /// <see cref="NetRace"/> packs into a frame.
        /// </summary>
        /// <param name="seat">Seat index.</param>
        public Racer RowOf(int seat)
        {
            var rules = _rules;
            if (rules != null)
            {
                return seat >= 0 && seat < rules.Count ? rules[seat] : default;
            }

            return NetRace.Standings.RowOf(seat);
        }

        /// <inheritdoc />
        /// <summary>
        /// A client says its runner fell onto <paramref name="storey"/>.
        /// <para>
        /// <b>One storey, and only the next one.</b> <see cref="RaceState.ReportDescent"/> is
        /// monotonic — it refuses anything at or above where the runner already is — but
        /// monotonic is not the same as small: it would happily accept "I am on B8" from a
        /// runner on B1, which is the entire race in one packet and the single most valuable
        /// lie available in this build. §12 puts a 투하구 at the middle of a floor and lands
        /// you on the RIM of the one below, so a descent is by construction exactly one
        /// storey, and anything else is either a client that has lost track of where it is
        /// or one that is lying. Both are refused the same way and neither is fatal: the
        /// runner keeps the storey the host already had for them.
        /// </para>
        /// <para>
        /// <b>What this does not check, said plainly.</b> It does not verify that the runner
        /// was standing in a 투하구, because the mouths are <c>Chute</c> components the
        /// gameplay layer owns and this class has never been given the list. The host has
        /// that scene too, so the check is available the day <c>MatchDirector</c> hands the
        /// chutes over — see <c>NetPlayer.CmdReportDescent</c>. Until then the cost of the
        /// gap is bounded by the paragraph above and by §12's one-way structure: a runner
        /// who claims a storey they are not on still has to walk every floor between here
        /// and B8 in the world, because <see cref="CheckFinish"/> measures a body and not a
        /// number.
        /// </para>
        /// </summary>
        /// <param name="seat">The seat the host assigned to the reporting connection.</param>
        /// <param name="storey">The storey claimed.</param>
        /// <returns>True if the rule moved them.</returns>
        public bool AcceptDescent(int seat, int storey)
        {
            var rules = _rules;
            if (rules == null || seat < 0 || seat >= rules.Count)
            {
                return false;
            }

            var expected = rules[seat].Storey + 1;
            if (storey != expected)
            {
                if (_verbose)
                {
                    Debug.Log(
                        "[Race] §13 " + seat + "번의 하강 보고를 거절했다 — B" + (storey + 1)
                        + "이라고 했지만 호스트가 아는 다음 층은 B" + (expected + 1)
                        + "이다. §12의 투하구는 한 층씩만 내려간다.", this);
                }

                return false;
            }

            return ReportDescent(seat, storey, _elapsedSeconds);
        }

        /// <inheritdoc />
        /// <summary>
        /// A client says §06's creature caught its runner.
        /// <para>
        /// <b>Why this is a report and not a host-side verdict — the decision, and what
        /// would change it.</b> §06 is not on the wire at all today: <c>MatchDirector</c>
        /// contains no reference to Mirror, <c>PrepareCreatures</c> stands eight
        /// <c>MonsterAgent</c>s on every machine, and <c>NetMonster</c> — the component
        /// written for §13's 「괴물 AI를 호스트가 돌린다」 — has <b>zero consumers</b> in the
        /// project. So the two floors' worth of creature on this machine and on that one are
        /// not the same creature: each is chasing its own local runner and they diverge from
        /// the first sighting. "Lift §06 to the host" is therefore not a bandwidth trade, it
        /// is building the networked monster that does not exist, and it lands on a file
        /// this change does not own.
        /// </para>
        /// <para>
        /// It would also make the game worse in the one place §06 was rebuilt to be good.
        /// The lunge commits at 1.8 m and travels at 7.0 m/s for 0.55 s specifically so the
        /// attack is "an attack somebody can see coming"; a relayed commit arriving 60–150 ms
        /// late is 0.42–1.05 m of that window spent before the client is told, on a 1.8 m
        /// commit distance.
        /// </para>
        /// <para>
        /// <b>The cost of the choice, honestly.</b> A client can decline to report its own
        /// catch. What that buys is much less than it looks: the storey a runner is recorded
        /// on is the floor their next descent has to be one below, and the only way onto a
        /// lower floor is to fall through its 투하구 in the world. A cheat that hides a catch
        /// keeps a standings row saying B6 while walking B1 → B6 again anyway, and cannot
        /// finish until <see cref="CheckFinish"/> finds its <em>body</em> inside the circle at
        /// the middle of B8 with the record already reading the bottom storey. The yield is a
        /// false HUD row and zero race progress. What it cannot do is the thing that would
        /// matter — report somebody else — because the seat is
        /// <c>NetPlayer.AssignSeat</c>'s and the transport chooses which object the
        /// <c>[Command]</c> arrived on.
        /// </para>
        /// <para>
        /// <b>What would change my mind:</b> a consumer for <c>NetMonster</c>. The day
        /// <c>MatchDirector.StepCreatures</c> is gated on <c>NetworkServer.active</c> and
        /// the eight creatures are spawned identities, the host holds an authoritative
        /// creature position per storey and the catch becomes the same shape as the finish —
        /// a geometric test on the host over bodies it owns — at which point this method
        /// should be <em>deleted</em> rather than kept beside it, because a report and a
        /// verdict for one event is two answers to one question, which is the failure this
        /// whole repository keeps finding.
        /// </para>
        /// </summary>
        /// <param name="seat">The seat the host assigned to the reporting connection.</param>
        /// <returns>True if the rule sent them back to B1.</returns>
        public bool AcceptCaught(int seat)
        {
            return ReportCaught(seat, _elapsedSeconds);
        }

        /// <inheritdoc />
        /// <summary>
        /// §01's 총 — somebody shot somebody, and this is the only place that decides
        /// whether it landed.
        /// <para>
        /// <b>The range is measured here, not reported.</b> <c>RunnerGun</c> raycasts on the
        /// shooter's machine to find out WHO is under the crosshair, which is the one fact
        /// only that machine has. How far away they were is a fact the host has too — it
        /// tracks a body per seat (<see cref="Track"/>, and on the host
        /// <c>BindNetworkedRunners</c> tracks the host's own copy of every remote runner) —
        /// so taking the distance off the wire would be handing the shooter the one number
        /// that decides whether a rival loses eight storeys. This is the whole reason
        /// <c>AcceptShot</c> has no distance parameter.
        /// </para>
        /// <para>
        /// A seat the host is not tracking refuses rather than defaulting to zero metres.
        /// Zero would be inside every range there is, so the failure mode of "the body was
        /// not found" has to be a miss and not a guaranteed hit.
        /// </para>
        /// <para>
        /// <c>Gunplay.ShotsPerGun</c> rather than a count carried up from the client, for
        /// the same reason <c>RunnerGun.Fire</c> passes it: the judgement is about the shot
        /// that was just fired, and a spent counter would make every shot refuse with
        /// <c>NoGun</c>. The gun itself is spent on the shooter's machine either way — a
        /// refusal here does not give the bullet back.
        /// </para>
        /// </summary>
        /// <param name="shooterSeat">Read off the NetPlayer the command arrived on.</param>
        /// <param name="targetSeat">Who the shooter's crosshair was on.</param>
        /// <returns>True if the rule sent the target back to B1.</returns>
        public bool AcceptShot(int shooterSeat, int targetSeat)
        {
            var rules = _rules;
            if (rules == null)
            {
                return false;
            }

            var shooter = BodyOf(shooterSeat);
            var target = BodyOf(targetSeat);
            if (shooter == null || target == null)
            {
                if (_verbose)
                {
                    Debug.Log(
                        "[Race] §01 " + shooterSeat + "번의 사격을 거절했다 — 호스트가 "
                        + (shooter == null ? shooterSeat : targetSeat)
                        + "번의 몸을 추적하고 있지 않다. 사거리를 잴 수 없으면 빗나간 것이다.", this);
                }

                return false;
            }

            var metresApart = Vector3.Distance(shooter.position, target.position);
            var outcome = Gunplay.Fire(
                rules, shooterSeat, targetSeat, Gunplay.ShotsPerGun, metresApart, _elapsedSeconds);

            Debug.Log(outcome == ShotRefusal.None
                ? "[Race] §01 " + shooterSeat + "번이 " + targetSeat + "번을 맞혔다 — "
                  + metresApart.ToString("0.0") + " m, 호스트 측정. 출발선으로."
                : "[Race] §01 " + shooterSeat + "번의 사격 — " + outcome + ", "
                  + metresApart.ToString("0.0") + " m (호스트 측정).", this);

            return outcome == ShotRefusal.None;
        }

        /// <summary>The transform this director tracks for a seat, or null. See <see cref="Track"/>.</summary>
        /// <param name="seat">Seat index.</param>
        private Transform? BodyOf(int seat)
        {
            for (var i = 0; i < _runners.Count; i++)
            {
                if (_runners[i].Id == seat)
                {
                    return _runners[i].Body;
                }
            }

            return null;
        }

        /// <summary>
        /// A seat emptied — somebody disconnected, or the field was padded up to §11's
        /// minimum to rehearse a race with one person in it.
        /// <para>
        /// <see cref="RaceState"/> has no idea of an empty seat, and it should not: §11
        /// sizes the field before the match and the rule is written for a field that stays
        /// the size it started. But a seat nobody is sitting in is Running forever, and
        /// <see cref="RaceState.Over"/> would never come true. So an empty seat is closed
        /// out the one way the rule has of ending a race without a place, and the reason it
        /// happened is kept here rather than smuggled into the rule.
        /// </para>
        /// </summary>
        /// <para>
        /// <b>A seat with somebody in it is refused, loudly.</b> This is not defensive
        /// tidying — <c>MatchDirector.AttachRace</c> begins a field of
        /// <see cref="GameConstants.RaceRunnersMin"/> and withdraws every seat except its
        /// own constant 0, which is correct for the solo playtest it was written for and is
        /// a match-ending bug the moment the host's <see cref="RaceState"/> is the only one
        /// that counts: on the host it would eliminate every client in the session before
        /// anybody had taken a step, and §02's verdict would be "우승 좌석 0" over a field of
        /// nineteen people who were never allowed to run. §11's field has to be sized from
        /// the party and the empty seats named individually; until
        /// <c>MatchDirector</c> does that, this refuses the ones that are not empty, because
        /// this class can see the connections and that class cannot.
        /// </para>
        /// </summary>
        /// <param name="runnerId">Seat index.</param>
        /// <param name="elapsedSeconds">Seconds since the start.</param>
        /// <returns>True if the seat was still running and was empty.</returns>
        public bool Withdraw(int runnerId, float elapsedSeconds)
        {
            if (SeatHasABody(runnerId))
            {
                Debug.LogWarning(
                    "[Race] §11 " + runnerId + "번 자리를 비우라는 요청을 거절했다 — 그 자리에는 "
                    + "연결된 주자가 있다. 빈 자리만 Withdraw로 닫아라. "
                    + "MatchDirector.AttachRace가 §11의 인원을 파티가 아니라 상수에서 가져오고 있으면 "
                    + "이 줄이 매 판 나온다.", this);
                return false;
            }

            return Retire(runnerId, elapsedSeconds, RaceExit.Withdrawn);
        }

        /// <summary>
        /// Closes every seat in the field that nobody is sitting in, and leaves the rest
        /// alone.
        /// <para>
        /// <b>The loop belongs here, not in <c>MatchDirector</c>.</b> Deciding which seats
        /// are empty means asking §13 which connections exist, and that class deliberately
        /// names no Mirror type. It used to withdraw every seat except its own and rely on
        /// <see cref="Withdraw"/> refusing the occupied ones — which worked, and printed a
        /// warning naming the bug on every machine in every match. Measured on a real
        /// two-instance session before this existed: 「§11 1번 자리를 비우라는 요청을
        /// 거절했다」 on the host, for the seat the client was standing in.
        /// </para>
        /// <para>
        /// A seat still has to be closed when nobody is in it, for the reason
        /// <see cref="Withdraw"/> gives: <c>RaceState.Over</c> means "nobody is still
        /// Running", so a field padded to §11's minimum for a solo playtest could never
        /// close and §02's verdict could never be reached.
        /// </para>
        /// </summary>
        /// <param name="elapsedSeconds">Seconds since the start.</param>
        /// <returns>How many seats were closed.</returns>
        public int WithdrawEmptySeats(float elapsedSeconds)
        {
            var closed = 0;
            for (var seat = 0; seat < _fieldSize; seat++)
            {
                if (seat == LocalRacerId || SeatHasABody(seat))
                {
                    continue;
                }

                if (Withdraw(seat, elapsedSeconds))
                {
                    closed++;
                }
            }

            return closed;
        }

        /// <summary>
        /// Whether §13 has a live connection sitting in this seat. Always false offline,
        /// where there are no connections and every seat but the local one really is empty.
        /// </summary>
        /// <param name="seat">Seat index.</param>
        private static bool SeatHasABody(int seat)
        {
            if (!NetworkServer.active)
            {
                return false;
            }

            var seats = RaceParty.SeatConnectionIds;
            if (seat < 0 || seat >= seats.Length)
            {
                return false;
            }

            return NetworkServer.connections.TryGetValue(seats[seat], out var connection)
                   && connection != null
                   && connection.identity != null;
        }

        /// <summary>Why a runner stopped, in §02's words. <see cref="RaceExit.Racing"/> while they have not.</summary>
        /// <param name="runnerId">Seat index.</param>
        public RaceExit ExitOf(int runnerId)
        {
            return runnerId >= 0 && runnerId < _exits.Length ? _exits[runnerId] : RaceExit.Racing;
        }

        /// <summary>
        /// Everybody still descending, deepest first. §02's live positions — see
        /// <see cref="RaceState.Standings"/> for why depth-then-time is the closest thing a
        /// maze race has to a position.
        /// <para>
        /// The rule builds a fresh list each read, so this is a refresh-rate property and
        /// not a per-draw-call one. <c>RaceHud</c> reads it at 5 Hz for exactly that reason.
        /// </para>
        /// <para>
        /// <b>On a client this is the host's list, not a re-sort of the host's rows.</b>
        /// <c>RaceStandingsMessage.LiveOrder</c> carries the ordering the host's own
        /// <see cref="RaceState.Standings"/> produced; a client that sorted for itself would
        /// be deciding who is second, which is the sentence §02 spends a paragraph on.
        /// </para>
        /// </summary>
        public IReadOnlyList<Racer> Standings
        {
            get { return _rules != null ? _rules.Standings() : NetRace.Standings.Standings; }
        }

        /// <summary>
        /// The finishers, winner first. §02 완주 — every one of them has a place.
        /// <para>
        /// Host only, and empty on a client rather than rebuilt from the broadcast rows: it
        /// is a results-screen reading with no consumer on the HUD, and inventing an
        /// ordering for it on a client would be the one thing this class now exists to
        /// prevent. The day a results screen needs it on every machine, the place to put the
        /// order is the frame.
        /// </para>
        /// </summary>
        public IReadOnlyList<Racer> Results
        {
            get { return _rules != null ? _rules.Results() : Array.Empty<Racer>(); }
        }

        /// <summary>
        /// Seat of the player at this screen, or −1 for a machine with nobody in the race —
        /// a dedicated host, or a viewer. Set once, at match start.
        /// <para>
        /// The director does not need it to run: §02 is decided over every tracked body and
        /// this is only ever used to answer "and what is happening to <em>me</em>". It lives
        /// here because <see cref="IRaceReadout"/> is the whole of what a screen may know,
        /// and a HUD that had to be told its own seat separately would be a second place for
        /// the answer to be wrong.
        /// </para>
        /// </summary>
        public int LocalRacerId { get; set; } = -1;

        /// <summary>
        /// That player's standing, read fresh. §09's two announcements — dropping a storey
        /// and being caught — both arrive as a change to this struct and to nothing else.
        /// <para>
        /// <b>On a client it is the host's row for this seat, looked up by index.</b> That
        /// is the whole of why <c>RaceStandingsMessage.Rows</c> is seat-indexed rather than
        /// a list of rows carrying their own ids: "which of these is me" has to be an array
        /// index and not a search, or the answer can be missing.
        /// </para>
        /// </summary>
        public Racer LocalRacer
        {
            get
            {
                var rules = _rules;
                if (rules == null)
                {
                    return NetRace.Standings.RowOf(LocalRacerId);
                }

                return LocalRacerId >= 0 && LocalRacerId < rules.Count ? rules[LocalRacerId] : default;
            }
        }

        /// <summary>How many started. §11's 2~20 — drawn so a runner can read the field thinning.</summary>
        public int RunnerCount
        {
            get { return _rules != null ? _rules.Count : NetRace.Standings.RunnerCount; }
        }

        /// <summary>
        /// Seconds since the twenty left the rim of B1, as last given to <see cref="Tick"/>.
        /// <para>
        /// The race clock, and deliberately not §07's 시각: §07 gates the hour so that nobody
        /// can feel the threat tier climbing, and the pivot deleted the surface trip without
        /// deleting that gate. How long you personally have been running is something you
        /// would know from having stood on a starting line.
        /// </para>
        /// <para>
        /// <b>The host's clock on a client, at <see cref="NetRace.HeartbeatSeconds"/>.</b>
        /// It is a race, so two people looking at each other's screens have to see the same
        /// number; a locally counted clock would drift over §07's thirty-two minutes for
        /// nothing. One second is exactly the resolution <c>RaceHud</c> prints it at.
        /// </para>
        /// </summary>
        public float ElapsedSeconds
        {
            get { return _rules != null ? _elapsedSeconds : NetRace.Standings.ElapsedSeconds; }
        }

        /// <summary>
        /// A label for a seat: the name where one was given, and 번호 where it was not.
        /// <para>
        /// §05 of the pivot makes the twenty identical and says "순위와 이름은 결과 화면에서
        /// 읽는다", so this is for a scoreboard and never for telling two bodies apart in a
        /// corridor — which is why it is a string keyed by seat and not anything attached to
        /// a runner in the world.
        /// </para>
        /// </summary>
        /// <param name="racerId">Seat index.</param>
        public string NameOf(int racerId)
        {
            if (racerId >= 0 && racerId < _names.Length && !string.IsNullOrEmpty(_names[racerId]))
            {
                return _names[racerId];
            }

            return (racerId + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "번";
        }

        /// <summary>
        /// Names a seat, for the results screen. Ignored for a seat outside the field.
        /// </summary>
        /// <param name="racerId">Seat index.</param>
        /// <param name="name">What to call them. Empty restores the seat number.</param>
        public void SetName(int racerId, string name)
        {
            if (racerId >= 0 && racerId < _names.Length)
            {
                _names[racerId] = name;
            }
        }

        /// <summary>
        /// The whole field as a HUD wants to draw it: finishers by place, then everyone
        /// still running by depth, then the unranked.
        /// <para>
        /// <b>The order is the argument.</b> Put the eighteen people still running above the
        /// two who are home and the screen says the race is about who is deepest; put the
        /// finishers on top and it says what §02 says — that arriving is worth something on
        /// its own, and that the person in third is chasing a row they can still take.
        /// </para>
        /// <para>
        /// The returned list is reused between calls. A HUD may call this every refresh; it
        /// must not hold on to the result across one.
        /// </para>
        /// </summary>
        public IReadOnlyList<Racer> Board()
        {
            _board.Clear();

            var rules = _rules;
            if (rules == null)
            {
                // A client draws the host's board and nothing else: the finishers in place
                // order, then the host's own live order, then the seats that emptied. Every
                // row and every position came off the wire — see RaceStandingsMessage.
                var standings = NetRace.Standings;

                for (var place = 1; place <= standings.RunnerCount; place++)
                {
                    for (var seat = 0; seat < standings.RunnerCount; seat++)
                    {
                        var racer = standings.RowOf(seat);
                        if (racer.Status == RacerStatus.Finished && racer.Place == place)
                        {
                            _board.Add(racer);
                        }
                    }
                }

                var told = standings.Standings;
                for (var i = 0; i < told.Count; i++)
                {
                    _board.Add(told[i]);
                }

                _retired.Clear();
                for (var seat = 0; seat < standings.RunnerCount; seat++)
                {
                    var racer = standings.RowOf(seat);
                    if (racer.Status == RacerStatus.Eliminated)
                    {
                        _retired.Add(racer);
                    }
                }

                _retired.Sort(CompareRetired);
                for (var i = 0; i < _retired.Count; i++)
                {
                    _board.Add(_retired[i]);
                }

                return _board;
            }

            var results = rules.Results();
            for (var i = 0; i < results.Count; i++)
            {
                _board.Add(results[i]);
            }

            var live = rules.Standings();
            for (var i = 0; i < live.Count; i++)
            {
                _board.Add(live[i]);
            }

            _retired.Clear();
            for (var i = 0; i < rules.Count; i++)
            {
                if (rules[i].Status == RacerStatus.Eliminated)
                {
                    _retired.Add(rules[i]);
                }
            }

            // Deepest first among the unranked too. They have no place and never will, but
            // "잡혔다, B6에서" is the only thing left to tell a spectator about their own run
            // and it is worth ordering.
            _retired.Sort(CompareRetired);
            for (var i = 0; i < _retired.Count; i++)
            {
                _board.Add(_retired[i]);
            }

            return _board;
        }

        /// <summary>
        /// Takes §02's seam down with the component.
        /// <para>
        /// The match scene is unloaded before the session is, so this runs before
        /// <c>HorrorGameNetworkManager.OnStopServer</c> clears the same field. Both are
        /// needed: this one covers a director destroyed while the server keeps running —
        /// a second match, or a test — and a stale one would have <see cref="NetRace"/>
        /// building a frame out of a destroyed <c>MonoBehaviour</c>, which in Unity is a
        /// null that does not compare equal to null.
        /// </para>
        /// </summary>
        private void OnDestroy()
        {
            if (ReferenceEquals(NetRace.Authority, this))
            {
                NetRace.Authority = null;
            }
        }

        // ------------------------------------------------------------------
        // The tick, in the order it runs.
        // ------------------------------------------------------------------

        private void CheckFinish(RaceState rules, float elapsedSeconds)
        {
            if (!_finishFound)
            {
                return;
            }

            _crossing.Clear();

            var bottom = RaceState.Storeys - 1;
            for (var i = 0; i < _runners.Count; i++)
            {
                var runner = _runners[i];
                var racer = rules[runner.Id];
                if (racer.Status != RacerStatus.Running || racer.Storey != bottom || runner.Body == null)
                {
                    continue;
                }

                var flat = runner.Body.position - _finish;
                flat.y = 0f;

                var sqr = flat.sqrMagnitude;
                if (sqr > FinishRadiusMetres * FinishRadiusMetres)
                {
                    continue;
                }

                _crossing.Add(new Crossing(runner.Id, sqr));
            }

            if (_crossing.Count == 0)
            {
                return;
            }

            // Two runners inside the circle on the same tick: the one further in travelled
            // further past the line, so they crossed it earlier within the step. At 5.6 m/s
            // that is at most 0.112 m of difference, which is to say the tie is nearly always
            // decided by the seat number below — but it is decided the same way everywhere,
            // and §13 needs that more than it needs the truth of who leaned in first.
            _crossing.Sort(CompareCrossings);

            for (var i = 0; i < _crossing.Count; i++)
            {
                var id = _crossing[i].Id;
                var place = rules.ReportFinish(id, elapsedSeconds);
                if (place == 0)
                {
                    continue;
                }

                _exits[id] = RaceExit.Finished;
                _standingsDirty = true;
                Untrack(id);

                Debug.Log(
                    "[Race] §02 " + id + "번 도착 — " + place + "위, "
                    + elapsedSeconds.ToString("0") + "초", this);

                if (place == 1 && !_winnerAnnounced)
                {
                    _winnerAnnounced = true;

                    // Said out loud because the mistake this component exists to prevent is
                    // invisible in a diff and obvious in a log: a match that ends here has
                    // thrown away §02's 완주 순위 and everything the map was built to make
                    // possible in the last three storeys.
                    Debug.Log(
                        "[Race] §02 승자 결정 — " + id + "번. 경주는 계속된다: "
                        + StillRunning + "명이 아직 달리고 있고, 완주 순위는 그들의 것이다.", this);
                }

                Finished?.Invoke(id, place);
            }
        }

        private void CheckTimeout(RaceState rules, float elapsedSeconds)
        {
            // §02 conditions 시간 초과 on "아무도 도착하지 않고". Once somebody is home there is
            // no clock: the places already recorded stand, and §07 closes the rest of the
            // race by being 생존 불가 수준 down there — "누군가는 위험을 감수해야 판이 끝난다."
            // A post-winner timer would be a fifth outcome the document does not have.
            if (rules.Finishers > 0 || elapsedSeconds < TimeoutSeconds)
            {
                return;
            }

            var closed = 0;
            for (var id = 0; id < rules.Count; id++)
            {
                if (Retire(id, elapsedSeconds, RaceExit.TimedOut))
                {
                    closed++;
                }
            }

            if (closed > 0)
            {
                Debug.LogWarning(
                    "[Race] §02 시간 초과 — " + TimeoutSeconds.ToString("0") + "초 동안 아무도 B"
                    + RaceState.Storeys + " 중심에 닿지 못했다. " + closed + "명 전원 패배.", this);
            }
        }

        private void CheckOver(RaceState rules)
        {
            if (!rules.Over)
            {
                return;
            }

            _over = true;
            _runners.Clear();

            // The last frame, sent before anything else reacts to Closed. §02's verdict is
            // the one standing a client must not have to infer, and StopRacing stops the
            // ticks that would otherwise carry it out.
            _standingsDirty = true;
            NetRace.Broadcast();
            _standingsDirty = false;

            var winner = rules.WinnerId;
            Debug.Log(
                winner >= 0
                    ? "[Race] §02 경주 종료 — 우승 " + winner + "번, 완주 " + rules.Finishers + "명 / "
                      + rules.Count + "명"
                    : "[Race] §02 경주 종료 — 도착자 없음. 전원 패배.",
                this);

            Closed?.Invoke(winner);
        }

        private bool Retire(int runnerId, float elapsedSeconds, RaceExit why)
        {
            var rules = _rules;
            if (rules == null || runnerId < 0 || runnerId >= rules.Count)
            {
                return false;
            }

            if (!rules.ReportEliminated(runnerId, elapsedSeconds))
            {
                return false;
            }

            _exits[runnerId] = why;
            _standingsDirty = true;
            Untrack(runnerId);

            if (_verbose)
            {
                Debug.Log(
                    "[Race] §02 " + runnerId + "번 자리가 비었다 (" + why + ") — B"
                    + (rules[runnerId].Storey + 1) + ", " + elapsedSeconds.ToString("0")
                    + "초. 이 판에서 유일하게 주자를 빼는 길이다.", this);
            }

            Retired?.Invoke(runnerId, why);
            return true;
        }

        // ------------------------------------------------------------------
        // Finding the finish.
        // ------------------------------------------------------------------

        /// <summary>
        /// Locates the middle of B8 in whatever the generator left behind.
        /// <para>
        /// Two ways, in order of how directly they name the thing. The first is a transform
        /// called <see cref="FinishMarkerName"/> — what <c>DescentMap</c> calls the mark,
        /// and what a hand-built or future scene would most likely use. The second is the
        /// generator's own: an <c>Entrance</c> node becomes the one light in the building
        /// that is left burning, because §03 wants the destination visible from the inner
        /// ring, and <c>DescentMap.MarkPlaces</c> marks exactly one node 출입구 — the finish.
        /// </para>
        /// <para>
        /// The prefix is not ambiguous: <c>DescentMap</c> at its default seed produces 773
        /// <c>ZoneLight</c> markers and exactly one <c>EntranceLight</c>. Where a map marks
        /// several the deepest wins, because §02's finish is at the bottom of the building by
        /// definition and a building whose exit is upstairs is the old co-operative map,
        /// which has no race in it to confuse.
        /// </para>
        /// </summary>
        private void LocateFinish()
        {
            // Scoped to the generated building where there is one, and to the whole scene
            // where there is not — the same sweep AttachChutes already does, and for the same
            // reason: it runs once, at match start, and a director that could not find the
            // finish because it did not look everywhere would be a very expensive saving.
            // `is null` rather than `== null`: UnityEngine.Object overloads the operator to
            // report a destroyed object as null, and that overload is opaque to the compiler's
            // null analysis. GameObject.Find cannot hand back a destroyed object, so the
            // reference form is both correct here and the one the compiler can follow.
            var root = GameObject.Find(MatchMap.MapRootName);

            var transforms = root is null
                ? FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                : root.GetComponentsInChildren<Transform>(includeInactive: true);

            for (var i = 0; i < transforms.Length; i++)
            {
                if (string.Equals(transforms[i].name, FinishMarkerName, StringComparison.Ordinal))
                {
                    BindFinish(transforms[i].position);
                    return;
                }
            }

            var lights = root is null
                ? FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                : root.GetComponentsInChildren<Light>(includeInactive: true);

            var found = false;
            var deepest = Vector3.zero;
            for (var i = 0; i < lights.Length; i++)
            {
                if (!lights[i].name.StartsWith(MatchMap.EntranceLightPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var at = lights[i].transform.position;
                if (!found || at.y < deepest.y)
                {
                    deepest = at;
                    found = true;
                }
            }

            if (found)
            {
                BindFinish(deepest);
            }
        }

        private static int CompareCrossings(Crossing a, Crossing b)
        {
            var byDepth = a.SqrDistance.CompareTo(b.SqrDistance);
            return byDepth != 0 ? byDepth : a.Id.CompareTo(b.Id);
        }

        private static int CompareRetired(Racer a, Racer b)
        {
            var byStorey = b.Storey.CompareTo(a.Storey);
            return byStorey != 0 ? byStorey : a.ElapsedSeconds.CompareTo(b.ElapsedSeconds);
        }

        /// <summary>A seat and the body sitting in it. §13: the host holds these, clients hold none.</summary>
        private readonly struct Runner
        {
            public Runner(int id, Transform body)
            {
                Id = id;
                Body = body;
            }

            public int Id { get; }

            public Transform Body { get; }
        }

        /// <summary>A runner found inside the finish this tick, and how far past the line.</summary>
        private readonly struct Crossing
        {
            public Crossing(int id, float sqrDistance)
            {
                Id = id;
                SqrDistance = sqrDistance;
            }

            public int Id { get; }

            public float SqrDistance { get; }
        }
    }
}
