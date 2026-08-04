#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Race;
using HorrorGame.Core.Threat;
using HorrorGame.Gameplay.Match;
using HorrorGame.UI;
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
    /// <b>Host only.</b> §02: "도착 판정을 클라이언트가 내리면 경주 게임에서 가장 먼저
    /// 조작되는 값이 된다." Attach one, on the host, and let clients render the standings
    /// they are sent.
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
    public sealed class RaceDirector : MonoBehaviour, IRaceReadout
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

        /// <summary>True once <see cref="Begin"/> has sized a field. Nothing happens before that.</summary>
        public bool Started
        {
            get { return _rules != null; }
        }

        /// <summary>True once <see cref="RaceState.Over"/> went true and <see cref="Closed"/> was raised.</summary>
        public bool Over
        {
            get { return _over; }
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
            get { return _rules != null ? _rules.WinnerId : -1; }
        }

        /// <summary>How many have reached the bottom. §02 완주 counts them all, not just the first.</summary>
        public int Finishers
        {
            get { return _rules != null ? _rules.Finishers : 0; }
        }

        /// <summary>How many are still descending. Zero is what ends the match.</summary>
        public int StillRunning
        {
            get
            {
                var rules = _rules;
                if (rules == null)
                {
                    return 0;
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
        /// Sizes §02 to the field that actually turned up and finds the finish.
        /// <para>
        /// The count is the real one, not <see cref="GameConstants.RaceRunnersMax"/>: a
        /// twenty-seat race with six people in it never satisfies
        /// <see cref="RaceState.Over"/>, because fourteen seats stay Running forever and
        /// the match cannot end. Seats that empty later are <see cref="Withdraw"/>n.
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

            _rules = new RaceState(runners);
            _exits = new RaceExit[runners];
            _names = new string[runners];
            _runners.Clear();
            _crossing.Clear();
            _elapsedSeconds = 0f;
            _over = false;
            _winnerAnnounced = false;

            if (!_finishFound)
            {
                LocateFinish();
            }

            if (!_finishFound)
            {
                Debug.LogError(
                    "[Race] §02의 도착점을 찾지 못했다 — 이 판은 아무도 이길 수 없다. "
                    + "'" + FinishMarkerName + "' 이름의 오브젝트도, '" + MatchMap.EntranceLightPrefix
                    + "…' 조명도 '" + MatchMap.MapRootName + "' 아래에 없다. "
                    + "DescentMap이 만든 씬인지 확인하거나 BindFinish로 직접 지정하라.",
                    this);
            }

            Debug.Log(
                "[Race] §02 하강 시작 — " + runners + "명, 도착점은 B" + RaceState.Storeys + " 중심"
                + (_finishFound ? " " + _finish.ToString("0.0") : " (미발견)"), this);
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
        }

        /// <summary>
        /// A runner landed on a lower storey. Forwards §12's one-way drop to the rule, which
        /// is what later lets them finish at all.
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
        /// <param name="runnerId">Seat index.</param>
        /// <param name="elapsedSeconds">Seconds since the start.</param>
        /// <returns>True if the seat was still running.</returns>
        public bool Withdraw(int runnerId, float elapsedSeconds)
        {
            return Retire(runnerId, elapsedSeconds, RaceExit.Withdrawn);
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
        /// </summary>
        public IReadOnlyList<Racer> Standings
        {
            get { return _rules != null ? _rules.Standings() : Array.Empty<Racer>(); }
        }

        /// <summary>The finishers, winner first. §02 완주 — every one of them has a place.</summary>
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
        /// </summary>
        public Racer LocalRacer
        {
            get
            {
                var rules = _rules;
                return rules != null && LocalRacerId >= 0 && LocalRacerId < rules.Count
                    ? rules[LocalRacerId]
                    : default;
            }
        }

        /// <summary>How many started. §11's 2~20 — drawn so a runner can read the field thinning.</summary>
        public int RunnerCount
        {
            get { return _rules != null ? _rules.Count : 0; }
        }

        /// <summary>
        /// Seconds since the twenty left the rim of B1, as last given to <see cref="Tick"/>.
        /// <para>
        /// The race clock, and deliberately not §07's 시각: §07 gates the hour so that nobody
        /// can feel the threat tier climbing, and the pivot deleted the surface trip without
        /// deleting that gate. How long you personally have been running is something you
        /// would know from having stood on a starting line.
        /// </para>
        /// </summary>
        public float ElapsedSeconds
        {
            get { return _elapsedSeconds; }
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
