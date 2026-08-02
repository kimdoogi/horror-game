#nullable enable

using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Core.Race;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI
{
    /// <summary>
    /// Everything this HUD is allowed to know about the race. §02 · §13.
    /// <para>
    /// <b>Why an interface and not <c>RaceDirector</c> itself.</b> The director is a
    /// <c>MonoBehaviour</c> in the default assembly, and the default assembly
    /// references <c>HorrorGame.UI</c> rather than the other way round — the same
    /// one-way arrangement that lets <c>MatchHud</c> drive <c>HudScreen</c>. Naming
    /// the concrete type here would either invert that arrow or drag Mirror into a
    /// layer that <c>UiScreen</c>'s remarks deliberately keep clean of it. The
    /// director implements this; the screen never learns which class did.
    /// </para>
    /// <para>
    /// <b>Every member is a reading, never a request.</b> §02 puts arrival judgement
    /// on the host — <em>"도착 판정을 클라이언트가 내리면 경주 게임에서 가장 먼저
    /// 조작되는 값이 된다"</em> — so there is no method here that could report a
    /// finish, and a HUD that wanted to cheat would have to grow one in review.
    /// </para>
    /// <para>
    /// The shapes are <c>Core.Race</c>'s own, so the standings this draws are the
    /// standings <c>RaceState</c> computed and not a second ordering that could
    /// disagree with the host about who is second.
    /// </para>
    /// </summary>
    public interface IRaceReadout
    {
        /// <summary>
        /// Runners still descending, deepest first — <c>RaceState.Standings()</c>
        /// unaltered. Finishers and the eliminated are absent by construction, which
        /// is why this screen draws their fate from <see cref="LocalRacer"/> instead
        /// of looking for them in here.
        /// </summary>
        IReadOnlyList<Racer> Standings { get; }

        /// <summary>Seat index of the player at this screen, or −1 for a viewer with no seat.</summary>
        int LocalRacerId { get; }

        /// <summary>
        /// That player's standing. Read every refresh rather than cached, because the
        /// two transitions this screen exists to announce — the drop to the next
        /// storey and being caught — both arrive as a change to this struct.
        /// </summary>
        Racer LocalRacer { get; }

        /// <summary>How many started. §11's 2~20; drawn so a runner can read the field thinning.</summary>
        int RunnerCount { get; }

        /// <summary>
        /// Seconds since the twenty of them left the rim of B1.
        /// <para>
        /// This is the race clock, not §07's 시각. §07 gates the hour behind the
        /// surface and a 회중시계 so that the team cannot feel the threat tier
        /// climbing; the pivot deleted both the surface trip and the shop, but it did
        /// not delete the gate — <c>ClockReadout</c> still owns it and this property
        /// must never be wired to <c>MatchClock</c>. What a runner is allowed to know
        /// is how long they personally have been running, which they would know
        /// anyway from having stood on a starting line.
        /// </para>
        /// </summary>
        float ElapsedSeconds { get; }

        /// <summary>True once nobody is still running. <c>RaceState.Over</c>.</summary>
        bool Over { get; }

        /// <summary>
        /// A label for a seat. §05 of the pivot says the twenty are identical and that
        /// <em>"순위와 이름은 결과 화면에서 읽는다"</em>, so this is a name where one is
        /// known and a seat number where it is not — never anything that would let a
        /// runner tell two bodies apart in a corridor.
        /// </summary>
        /// <param name="racerId">Seat index.</param>
        string NameOf(int racerId);
    }

    /// <summary>
    /// The race HUD. §01 · §02 · §03 · §09 · §11.
    /// <para>
    /// <b>One number matters and it is a depth.</b> §01's loop is eight storeys of the
    /// same maze and the only question a runner ever has is how far down they are, so
    /// the storey is not printed as "3 / 8" in a corner — it is a column of eight ticks
    /// down the left edge with the lit one <em>moving downward</em> as the player
    /// descends. A fraction is a score and invites the reading "I have 37% of a game
    /// left"; a mark travelling down a shaft is the thing that is actually happening.
    /// Nothing else on this screen is drawn at that size.
    /// </para>
    /// <para>
    /// <b>Six rows, never twenty.</b> §11 designs for 11~20 and the standings would
    /// happily be twenty rows deep; at <c>UiStyle.LineGap</c> that is 440 px down a
    /// 1080 px frame — two-fifths of its height, and 2.2× this screen's whole lit
    /// area — permanently on, over the one corridor §03 has spent its entire section
    /// darkening. The rows are cut to the two facts a runner can act on — <em>who is
    /// winning</em> and <em>who is about to pass me</em> — which is the top four plus
    /// the runner directly ahead plus yourself. See <see cref="SelectRows"/>.
    /// </para>
    /// <para>
    /// <b>§03, and the rule that kept this HUD dark.</b> The game's argument is that
    /// the inner rings are unlit and that a flashlight is the only thing between the
    /// player and nothing — a bright overlay would hand back for free what the whole
    /// ring structure charges for. Four things follow, and all four are visible in the
    /// code below. There is no backing plate anywhere: every element is a glyph or a
    /// two-pixel rule drawn onto the corridor, because a filled panel is a lamp.
    /// Everything sits at <c>UiStyle.InkFaint</c> (α 0.42) except the player's own two
    /// facts, which get <c>UiStyle.Ink</c> (α 0.72). Storeys not yet reached are drawn
    /// in <c>UiStyle.BarBed</c>, so the unlit half of the gauge is structure rather
    /// than light. And saturated colour is spent exactly twice — on a runner who has
    /// reached the bottom storey, and on the verdict — because §02 makes the first the
    /// moment the match is about to end for everybody. Summed over every element a
    /// racing frame can contain, weighted by alpha and by luminance, that comes to
    /// about 770 px² of a 1920×1080 frame: <b>0.037% of the display</b>, against 0.083%
    /// for the same screen with the honest twenty-row board on it.
    /// </para>
    /// <para>
    /// <b>The screen is only allowed to get bright once the player has stopped
    /// playing.</b> The verdict headline is the largest, most saturated thing this
    /// class can draw, and it is unreachable while <see cref="RacerStatus.Running"/>:
    /// by the time it appears the player has either finished or been caught, and §09
    /// has turned them into a spectator whose night vision is no longer a resource
    /// anybody is charging for.
    /// </para>
    /// <para>
    /// Nothing here decides anything, in keeping with <see cref="UiScreen"/>: it turns
    /// <c>Core.Race</c> structs into strings, colours and positions.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaceHud : UiScreen
    {
        /// <summary>
        /// Rows of the standings taken from the top before the player's own
        /// neighbourhood. Four, because §11's bottleneck is one gate and the leaders
        /// are the people already through it; a fifth name adds a lit line and no
        /// decision.
        /// </summary>
        private const int TopRows = 4;

        /// <summary>
        /// Row budget: <see cref="TopRows"/>, the runner directly ahead, and the player.
        /// Also the number of rows a spectator sees, filled from the top — a ghost has
        /// no position of their own to find in the list and no night vision left to
        /// protect, so the same six lines simply become the deepest six.
        /// </summary>
        private const int MaxRows = TopRows + 2;

        /// <summary>
        /// Seconds between redraws. <c>RaceState.Standings()</c> allocates a list of up
        /// to twenty every call, and at 144 Hz that is a per-frame allocation for an
        /// ordering that cannot meaningfully change faster than a person can run
        /// 5.6 m/s. A descent or an elimination bypasses this — see <see cref="LateUpdate"/>.
        /// </summary>
        private const float RefreshIntervalSeconds = 0.2f;

        /// <summary>Vertical distance between two storey ticks. Eight of them span 7 × this, which fits the frame with the verdict line clear of it.</summary>
        private const float GaugeStep = 26f;

        /// <summary>Length of a storey tick the player has not reached.</summary>
        private const float GaugeTickWidth = 14f;

        /// <summary>Length of the tick the player is standing on. Long enough to find without reading, which is the point of a gauge.</summary>
        private const float GaugeTickWidthHere = 30f;

        /// <summary>Thickness of a tick. Same two pixels as <c>UiStyle.BarHeight</c> halved — a rule, not a bar.</summary>
        private const float GaugeTickHeight = 2f;

        /// <summary>Gap between the ticks and the storey numeral beside them.</summary>
        private const float GaugeLabelGap = 12f;

        /// <summary>Width reserved for a standings row's storey column, at the right edge.</summary>
        private const float RowDepthColumn = 46f;

        /// <summary>
        /// Scratch for <see cref="SelectRows"/>. A field rather than a local so the
        /// 5 Hz redraw allocates nothing of its own — <c>RaceState.Standings()</c>
        /// already allocates once per refresh, and that is one more than this screen
        /// would like.
        /// </summary>
        private readonly int[] _picked = new int[MaxRows];

        private IRaceReadout? _race;

        private Image[]? _gaugeTicks;
        private RectTransform? _gaugeLabelRect;
        private Text? _gaugeLabel;
        private RectTransform? _gaugeRemainingRect;
        private Text? _gaugeRemaining;

        private Text? _clock;
        private Text? _field;

        private Text[]? _rowWho;
        private Text[]? _rowDepth;

        private RectTransform? _verdictGroup;
        private Text? _verdictHeadline;
        private Text? _verdictLine;

        private float _nextRefresh;
        private int _drawnStorey = -1;
        private RacerStatus _drawnStatus = RacerStatus.Running;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderHud; }
        }

        /// <summary>
        /// False, for <c>HudScreen</c>'s reason: the player is looking through this at a
        /// corridor and the mouse belongs to the camera (§05).
        /// </summary>
        protected override bool Interactive
        {
            get { return false; }
        }

        /// <summary>The reading this screen is drawing, or null before <see cref="Bind"/>.</summary>
        public IRaceReadout? Race
        {
            get { return _race; }
        }

        /// <summary>
        /// Points the HUD at the race and shows it.
        /// <para>
        /// Pull rather than push, matching <c>HudScreen.Bind</c>: the host's own screen
        /// and offline play hand over the director directly, and a remote client is
        /// given an implementation fed by whatever the host last said. Neither path
        /// lets this class compute a standing.
        /// </para>
        /// </summary>
        /// <param name="race">The race to read. Null is legal and hides the screen.</param>
        public void Bind(IRaceReadout? race)
        {
            _race = race;
            _drawnStorey = -1;
            _nextRefresh = 0f;

            if (race == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            Redraw(race);
        }

        /// <summary>Stops reading and clears the screen — the match is over and the results screen owns the display.</summary>
        public void Unbind()
        {
            _race = null;
            SetVisible(false);
        }

        /// <summary>
        /// Draws one reading. Public so a test, or a client that is handed a frame
        /// rather than a live director, can put a known race on the screen without
        /// waiting a frame for <see cref="LateUpdate"/>.
        /// </summary>
        /// <param name="race">The reading to draw.</param>
        public void Redraw(IRaceReadout race)
        {
            EnsureBuilt();

            var local = race.LocalRacerId >= 0 ? race.LocalRacer : default;
            var racing = race.LocalRacerId >= 0 && local.Status == RacerStatus.Running;

            DrawGauge(local, racing);
            DrawClock(race);
            DrawStandings(race);
            DrawVerdict(race, local, racing);

            _drawnStorey = local.Storey;
            _drawnStatus = local.Status;
        }

        /// <summary>
        /// Refreshes on <see cref="RefreshIntervalSeconds"/>, or immediately when the
        /// player's own state changed.
        /// <para>
        /// The exception is the whole reason the cadence is affordable. A drop through
        /// a 투하구 and being caught are the two events in this game a fifth of a second
        /// of latency would be felt on, because both are things the player just did;
        /// everything else on this screen is other people's positions in a maze they
        /// cannot see, and those are stale by more than 200 ms no matter how often this
        /// redraws.
        /// </para>
        /// <para>
        /// <c>LateUpdate</c> rather than <c>Update</c>, and unscaled time for the gate:
        /// the cadence is a drawing decision, so a paused game must not leave the last
        /// frame's standings up any longer than an unpaused one would.
        /// </para>
        /// </summary>
        private void LateUpdate()
        {
            var race = _race;
            if (race == null)
            {
                return;
            }

            var local = race.LocalRacerId >= 0 ? race.LocalRacer : default;
            var moved = local.Storey != _drawnStorey || local.Status != _drawnStatus;

            if (!moved && Time.unscaledTime < _nextRefresh)
            {
                return;
            }

            _nextRefresh = Time.unscaledTime + RefreshIntervalSeconds;
            Redraw(race);
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            BuildGauge(root);
            BuildClock(root);
            BuildStandings(root);
            BuildVerdict(root);
        }

        // ------------------------------------------------------------------
        // §01 — the depth gauge. Left edge, vertically centred, and the only
        // element on this screen drawn larger than a caption.
        // ------------------------------------------------------------------

        private void BuildGauge(RectTransform root)
        {
            var ticks = new Image[RaceState.Storeys];
            for (var i = 0; i < ticks.Length; i++)
            {
                var tick = UiFactory.CreateImage("Storey" + i.ToString(CultureInfo.InvariantCulture), root, UiStyle.BarBed);
                UiFactory.Place(
                    (RectTransform)tick.transform,
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(UiStyle.ScreenMargin, TickY(i)),
                    new Vector2(GaugeTickWidth, GaugeTickHeight));
                ticks[i] = tick;
            }

            _gaugeTicks = ticks;

            _gaugeLabel = UiFactory.CreateText(
                "StoreyLabel", root, Font, string.Empty, UiStyle.TextSizeTitle, UiStyle.Ink, TextAnchor.MiddleLeft);
            _gaugeLabelRect = UiFactory.Place(
                (RectTransform)_gaugeLabel.transform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                Vector2.zero,
                new Vector2(180f, UiStyle.TextSizeTitle + 8f));

            _gaugeRemaining = UiFactory.CreateText(
                "StoreyRemaining", root, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.MiddleLeft);
            _gaugeRemainingRect = UiFactory.Place(
                (RectTransform)_gaugeRemaining.transform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                Vector2.zero,
                new Vector2(180f, UiStyle.TextSizeSmall + 6f));
        }

        /// <summary>
        /// Where storey <paramref name="storey"/>'s tick sits. B1 at the top and the
        /// bottom storey at the bottom — the gauge is a section through the building,
        /// so an index that grows must move the mark down the screen.
        /// </summary>
        private static float TickY(int storey)
        {
            var span = (RaceState.Storeys - 1) * GaugeStep;
            return (span * 0.5f) - (storey * GaugeStep);
        }

        private void DrawGauge(Racer local, bool racing)
        {
            if (_gaugeTicks == null || _gaugeLabel == null || _gaugeLabelRect == null
                || _gaugeRemaining == null || _gaugeRemainingRect == null)
            {
                return;
            }

            // A depth that has stopped changing is not a depth. Once the player has
            // finished or been caught the gauge comes down entirely and leaves the left
            // edge to the verdict, rather than freezing a lit mark on the storey they
            // happened to die on.
            for (var i = 0; i < _gaugeTicks.Length; i++)
            {
                _gaugeTicks[i].gameObject.SetActive(racing);
            }

            _gaugeLabel.gameObject.SetActive(racing);
            _gaugeRemaining.gameObject.SetActive(racing);

            if (!racing)
            {
                return;
            }

            var here = Mathf.Clamp(local.Storey, 0, RaceState.Storeys - 1);

            for (var i = 0; i < _gaugeTicks.Length; i++)
            {
                var rect = (RectTransform)_gaugeTicks[i].transform;
                var current = i == here;

                rect.sizeDelta = new Vector2(current ? GaugeTickWidthHere : GaugeTickWidth, GaugeTickHeight);

                // Three weights, and the darkest is the future. Storeys already behind
                // the player are dim but present — eight ticks with a mark two-thirds of
                // the way down is what makes this read as depth — while the ones below
                // are bed colour, because §03 does not let the player see what is under
                // them and neither should this.
                _gaugeTicks[i].color = current
                    ? UiStyle.Ink
                    : (i < here ? UiStyle.InkFaint : UiStyle.BarBed);
            }

            var y = TickY(here);
            var x = UiStyle.ScreenMargin + GaugeTickWidthHere + GaugeLabelGap;

            _gaugeLabelRect.anchoredPosition = new Vector2(x, y + 10f);
            _gaugeLabel.text = StoreyName(here);

            _gaugeRemainingRect.anchoredPosition = new Vector2(x, y - 18f);

            // Said as a direction and a distance rather than as progress. "아래로 5층"
            // is a thing left to do; "3 / 8" is a completion percentage, and §01's loop
            // is not a completion percentage.
            var below = RaceState.Storeys - 1 - here;
            _gaugeRemaining.text = below > 0
                ? "아래로 " + below.ToString(CultureInfo.InvariantCulture) + "층"
                : "마지막 층";
        }

        /// <summary>
        /// §01's name for a storey. Index 0 is B1, so the label is one ahead of the
        /// index everywhere in this file and nowhere else.
        /// </summary>
        private static string StoreyName(int storey)
        {
            return "B" + (storey + 1).ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // The clock and the field count. Top right — HudScreen owns the top
        // centre for §07 and both bottom corners, and two HUDs sharing a canvas
        // order must not share a corner.
        // ------------------------------------------------------------------

        private void BuildClock(RectTransform root)
        {
            _clock = UiFactory.CreateText(
                "Elapsed", root, Font, string.Empty, UiStyle.TextSize, UiStyle.InkFaint, TextAnchor.UpperRight);
            UiFactory.Place(
                (RectTransform)_clock.transform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-UiStyle.ScreenMargin, -UiStyle.ScreenMargin),
                new Vector2(220f, UiStyle.TextSize + 8f));

            _field = UiFactory.CreateText(
                "Field", root, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.UpperRight);
            UiFactory.Place(
                (RectTransform)_field.transform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-UiStyle.ScreenMargin, -(UiStyle.ScreenMargin + UiStyle.LineGap)),
                new Vector2(220f, UiStyle.TextSizeSmall + 6f));
        }

        private void DrawClock(IRaceReadout race)
        {
            if (_clock == null || _field == null)
            {
                return;
            }

            _clock.text = Elapsed(race.ElapsedSeconds);

            // §11's field, thinning. The pair is the only thing on this HUD that says
            // the creature is still working: §02 makes elimination final and unranked,
            // so a runner watching this number fall is watching people stop existing
            // rather than watching a kill feed, which is the difference §01 asks for
            // between a hazard and an antagonist.
            var live = race.Standings.Count;
            _field.text = live.ToString(CultureInfo.InvariantCulture)
                + " / " + race.RunnerCount.ToString(CultureInfo.InvariantCulture) + " 남음";
        }

        /// <summary>
        /// mm:ss, both fields padded.
        /// <para>
        /// Padded so the string never changes width. An unpadded clock reflows every
        /// time it crosses a minute, and a HUD element that twitches in peripheral
        /// vision is read as movement in a corridor — the one false positive §01 can
        /// least afford.
        /// </para>
        /// </summary>
        private static string Elapsed(float seconds)
        {
            var whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
            var minutes = whole / 60;
            var rest = whole % 60;
            return minutes.ToString("00", CultureInfo.InvariantCulture)
                + ":" + rest.ToString("00", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // §02 · §11 — the standings. Right edge, under the clock.
        // ------------------------------------------------------------------

        private void BuildStandings(RectTransform root)
        {
            var who = new Text[MaxRows];
            var depth = new Text[MaxRows];

            var top = -(UiStyle.ScreenMargin + (UiStyle.LineGap * 2.5f));

            for (var i = 0; i < MaxRows; i++)
            {
                var y = top - (i * UiStyle.LineGap);
                var suffix = i.ToString(CultureInfo.InvariantCulture);

                // Two right-aligned columns rather than one padded string: the face may
                // be proportional (UiFactory falls back to the engine's), and a single
                // line of "4  이름  B6" would put every storey in a different place down
                // the list. The storey is the column the eye scans, so it is the one
                // pinned to the edge.
                who[i] = UiFactory.CreateText(
                    "RowWho" + suffix, root, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.UpperRight);
                UiFactory.Place(
                    (RectTransform)who[i].transform,
                    new Vector2(1f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(-(UiStyle.ScreenMargin + RowDepthColumn), y),
                    new Vector2(300f, UiStyle.TextSizeSmall + 6f));

                depth[i] = UiFactory.CreateText(
                    "RowDepth" + suffix, root, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.UpperRight);
                UiFactory.Place(
                    (RectTransform)depth[i].transform,
                    new Vector2(1f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(-UiStyle.ScreenMargin, y),
                    new Vector2(RowDepthColumn, UiStyle.TextSizeSmall + 6f));
            }

            _rowWho = who;
            _rowDepth = depth;
        }

        private void DrawStandings(IRaceReadout race)
        {
            if (_rowWho == null || _rowDepth == null)
            {
                return;
            }

            var live = race.Standings;
            var self = IndexOf(live, race.LocalRacerId);
            var count = SelectRows(live.Count, self, _picked);

            for (var row = 0; row < MaxRows; row++)
            {
                var shown = row < count;
                _rowWho[row].gameObject.SetActive(shown);
                _rowDepth[row].gameObject.SetActive(shown);

                if (!shown)
                {
                    continue;
                }

                var index = _picked[row];
                var racer = live[index];
                var mine = index == self;

                // The position number carries the gap. When the player is eighth the
                // list reads 1 2 3 4 7 8, and the jump from 4 to 7 says "two people you
                // cannot see are between you and them" without spending a lit row on an
                // ellipsis. Every element this HUD does not draw is one the flashlight
                // gets to keep.
                _rowWho[row].text = (index + 1).ToString(CultureInfo.InvariantCulture)
                    + "  " + race.NameOf(racer.Id);
                _rowDepth[row].text = StoreyName(racer.Storey);

                var color = RowColor(racer, mine);
                _rowWho[row].color = color;
                _rowDepth[row].color = color;
            }
        }

        /// <summary>
        /// Chooses which of up to twenty live runners get a row.
        /// <para>
        /// <b>Top four, the runner directly ahead, and you.</b> §11 designs for a field
        /// of 11~20 funnelling through one inner gate, and the honest twenty-row board
        /// that field deserves would sit over the corridor for the entire descent. The
        /// cut is by usefulness rather than by fit: a runner can do exactly two things
        /// with a standing — chase the lead, or shut a door on the person about to take
        /// their place — and every row that serves neither is a lit line in a game whose
        /// §03 is an argument for not having lit lines. Positions five through eighteen
        /// serve neither.
        /// </para>
        /// <para>
        /// A viewer with no live position — a §09 ghost, or a spectator — gets the same
        /// six lines filled from the top, since there is no neighbourhood to centre on.
        /// </para>
        /// </summary>
        /// <param name="liveCount">How many runners are still descending.</param>
        /// <param name="self">The local player's index in the standings, or −1.</param>
        /// <param name="into">Receives the chosen indices, in draw order.</param>
        /// <returns>How many rows were chosen.</returns>
        private static int SelectRows(int liveCount, int self, int[] into)
        {
            var rows = 0;

            var top = Mathf.Min(TopRows, liveCount);
            for (var i = 0; i < top; i++)
            {
                into[rows++] = i;
            }

            if (self < 0)
            {
                // No seat in the live list. Keep filling from the top to the same budget.
                for (var i = top; i < liveCount && rows < MaxRows; i++)
                {
                    into[rows++] = i;
                }

                return rows;
            }

            if (self < TopRows)
            {
                return rows;
            }

            // The runner directly ahead is the whole reason the bottom of this list
            // exists — §11 says the doors decide the match, and a door is only ever shut
            // on the person immediately behind. Skipped when they are already a top row,
            // which is the case when the player is exactly fifth.
            if (self - 1 >= TopRows)
            {
                into[rows++] = self - 1;
            }

            into[rows++] = self;
            return rows;
        }

        /// <summary>Finds a seat in the standings, or −1 if that runner is not descending any more.</summary>
        private static int IndexOf(IReadOnlyList<Racer> live, int racerId)
        {
            if (racerId < 0)
            {
                return -1;
            }

            for (var i = 0; i < live.Count; i++)
            {
                if (live[i].Id == racerId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// The colour of one standings row.
        /// <para>
        /// Saturated colour is spent once, and on somebody else: a runner standing on
        /// the bottom storey is one gate from ending the match for all twenty (§02 —
        /// <em>"1등이 아니어도 완주 순위는 남는다"</em> is what keeps the rest running,
        /// and that clock starts the moment the first person lands on B8). It outranks
        /// the player's own row on purpose. Their own storey is already the largest
        /// thing on the screen; what they do not otherwise have is a reason to spend
        /// their last stamina now.
        /// </para>
        /// </summary>
        private static Color RowColor(Racer racer, bool mine)
        {
            if (racer.Storey >= RaceState.Storeys - 1)
            {
                return UiStyle.Trade;
            }

            return mine ? UiStyle.Ink : UiStyle.InkFaint;
        }

        // ------------------------------------------------------------------
        // §02 · §09 — the verdict. Screen centre, and never drawn while the
        // player is still running.
        // ------------------------------------------------------------------

        private void BuildVerdict(RectTransform root)
        {
            var group = UiFactory.Place(
                UiFactory.CreateRect("Verdict", root),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(900f, 120f));
            _verdictGroup = group;

            _verdictHeadline = UiFactory.CreateText(
                "VerdictHeadline", group, Font, string.Empty, UiStyle.TextSizeTitle, UiStyle.Ink, TextAnchor.MiddleCenter);
            UiFactory.Place(
                (RectTransform)_verdictHeadline.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 18f),
                new Vector2(900f, UiStyle.TextSizeTitle + 10f));

            _verdictLine = UiFactory.CreateText(
                "VerdictLine", group, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.MiddleCenter);
            UiFactory.Place(
                (RectTransform)_verdictLine.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -18f),
                new Vector2(900f, UiStyle.TextSizeSmall + 8f));
        }

        private void DrawVerdict(IRaceReadout race, Racer local, bool racing)
        {
            if (_verdictGroup == null || _verdictHeadline == null || _verdictLine == null)
            {
                return;
            }

            var show = race.LocalRacerId >= 0 && !racing;
            _verdictGroup.gameObject.SetActive(show);

            if (!show)
            {
                return;
            }

            if (local.Status == RacerStatus.Eliminated)
            {
                // §02: 탈락 is unranked, and saying so is the point. A place here — even a
                // "you were 6th when you died" — would rank corpses by how deep they got,
                // which the design refuses because it would reward dying in the right
                // order. The second line is §09's promise instead: you are still here.
                _verdictHeadline.text = "탈락";
                _verdictHeadline.color = UiStyle.Spent;
                _verdictLine.text = race.Over ? "경주가 끝났다" : "남은 경주를 본다";
                _verdictLine.color = UiStyle.InkFaint;
                return;
            }

            if (local.Status == RacerStatus.Finished)
            {
                var first = local.Place == 1;

                // §02 has exactly one winner and records a place for everybody else. The
                // two are drawn as the same sentence in two weights rather than as a win
                // screen and a consolation, because 완주 순위 is what a player who lost
                // the lead on B3 has been running for since B3.
                _verdictHeadline.text = local.Place.ToString(CultureInfo.InvariantCulture) + "위";
                _verdictHeadline.color = first ? UiStyle.Trade : UiStyle.Ink;
                _verdictLine.text = first
                    ? "가장 먼저 닿았다"
                    : (race.Over ? "완주" : "완주 — 남은 경주를 본다");
                _verdictLine.color = UiStyle.InkFaint;
                return;
            }

            // Still Running but not racing cannot happen through Bind; drawn defensively
            // so a bad reading is blank rather than a stale verdict over a live corridor.
            _verdictHeadline.text = string.Empty;
            _verdictLine.text = string.Empty;
        }
    }
}
