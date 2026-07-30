#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Clues;

namespace HorrorGame.UI.Readouts
{
    /// <summary>
    /// One player's attempt to read one mark, right now. §03.
    /// <para>
    /// <b>There is no clue log, and there must never be one.</b> §03 states the
    /// constraint as the reason the game works: <em>"그 자리에서 보고, 기억해서,
    /// 말로 전달해야 한다."</em> Everything the section lists as a consequence —
    /// forced communication, having to stand in the dangerous room, "6이었나
    /// 9였나", the revisit when the team got it wrong — is downstream of the player
    /// having no copy. A journal, a screenshot key, a "last clue" line in the corner
    /// or a results-screen recap all do the same damage, and §14's verification
    /// question 4 ("6이었나 9였나 대화가 나오는가") is the thing that stops working
    /// when they exist.
    /// </para>
    /// <para>
    /// So this type is a <c>readonly struct</c> describing exactly one read, it is
    /// held in exactly one field by the overlay, and it is overwritten or cleared
    /// rather than appended to. <b>No collection type appears anywhere on this
    /// path</b> — no list, no array, no dictionary, no queue — which is the property
    /// to check in review, because a log would have to introduce one.
    /// </para>
    /// <para>
    /// It also stores no <c>ClueReport</c>, only the strings drawn from one. The
    /// report is flattened at the moment it arrives, so nothing structured about the
    /// clue survives the frame that showed it: there is no <c>SiteLabel</c> a later
    /// feature could compare against, and no <c>FloorFeature</c> to look up. Under
    /// ARCHITECTURE §4 the host answers "what am I looking at" for the mark in front
    /// of this player and nothing else; this is the client end of that promise.
    /// </para>
    /// </summary>
    public readonly struct ClueReadingView
    {
        /// <summary>Builds a view directly. Prefer <see cref="Reading"/> and <see cref="Revealed"/>.</summary>
        public ClueReadingView(
            ClueReadState state,
            ClueReadInterrupt interrupt,
            float progress,
            ClueLayer layer,
            bool legible,
            string markText)
        {
            State = state;
            Interrupt = interrupt;
            Progress = progress;
            Layer = layer;
            Legible = legible;
            MarkText = markText;
        }

        /// <summary>Where the attempt has got to.</summary>
        public ClueReadState State { get; }

        /// <summary>Why it stopped, if it did.</summary>
        public ClueReadInterrupt Interrupt { get; }

        /// <summary>How much of <see cref="GameConstants.ClueReadSeconds"/> has been held, 0–1.</summary>
        public float Progress { get; }

        /// <summary>Which link of §03's chain this mark is. Visible before the read finishes — a player can see what kind of note they found.</summary>
        public ClueLayer Layer { get; }

        /// <summary>Whether the finished read produced anything. §03 prefers nothing over something wrong.</summary>
        public bool Legible { get; }

        /// <summary>
        /// The mark itself, already rendered — "물", "물 → 3", "ㅁ-6 좌". A string and
        /// not a structure, on purpose: see the class remarks.
        /// </summary>
        public string MarkText { get; }

        /// <summary>Whether the overlay draws anything.</summary>
        public bool IsVisible
        {
            get { return State != ClueReadState.Idle; }
        }

        /// <summary>True while the beam is being held. §03: "오래 비춰야 읽힌다".</summary>
        public bool InProgress
        {
            get { return State == ClueReadState.Reading; }
        }

        /// <summary>True once the mark is on screen to be memorised.</summary>
        public bool IsRevealed
        {
            get { return State == ClueReadState.Complete; }
        }

        /// <summary>
        /// True when the light was what broke the read.
        /// <para>
        /// Called out separately because §03 makes darkness the lock rather than a
        /// penalty — <c>ClueReader</c> discards the progress instead of pausing it,
        /// and the overlay says so, so that a player whose cell died knows they are
        /// starting from zero and not from where they left off.
        /// </para>
        /// </summary>
        public bool LightWasLost
        {
            get { return State == ClueReadState.Interrupted && Interrupt == ClueReadInterrupt.LightLost; }
        }

        /// <summary>Why the read stopped, in words. Empty while nothing has gone wrong.</summary>
        public string InterruptLabel
        {
            get { return State == ClueReadState.Interrupted ? UiStrings.Interrupt(Interrupt) : string.Empty; }
        }

        /// <summary>Nothing is being read. The state the overlay returns to and stays in.</summary>
        public static ClueReadingView None
        {
            get
            {
                return new ClueReadingView(
                    ClueReadState.Idle, ClueReadInterrupt.None, 0f, ClueLayer.None, false, string.Empty);
            }
        }

        /// <summary>
        /// The progress half of the overlay, from the reader a client may run locally.
        /// <para>
        /// <c>ClueReader</c> holds no clue content — it only knows how long its own
        /// player has stood still in the light — so a client running one learns
        /// nothing it could not already see. The mark itself arrives separately, from
        /// the host, through <see cref="Revealed"/>.
        /// </para>
        /// </summary>
        /// <param name="reader">The local reader, or null when the player is not near a mark.</param>
        /// <param name="layer">Which kind of note it is, as reported by the host. <see cref="ClueLayer.None"/> if unknown.</param>
        public static ClueReadingView Reading(ClueReader? reader, ClueLayer layer)
        {
            if (reader == null || reader.State == ClueReadState.Idle)
            {
                return None;
            }

            return new ClueReadingView(
                reader.State,
                reader.LastInterrupt,
                reader.Progress,
                layer,
                false,
                string.Empty);
        }

        /// <summary>
        /// The host's answer for the mark this player just finished reading.
        /// <para>
        /// The report is consumed here and not retained: what comes out is a rendered
        /// string, and the caller is expected to drop the <c>ClueReport</c> on the
        /// floor. Reading the same clue again may legitimately produce a different
        /// answer — <c>ClueReport</c> says so — and the interface must not smooth that
        /// over, because the inconsistency is §03's misread model doing its job.
        /// </para>
        /// </summary>
        public static ClueReadingView Revealed(ClueReport report)
        {
            return new ClueReadingView(
                ClueReadState.Complete,
                ClueReadInterrupt.None,
                1f,
                report.Layer,
                report.Legible,
                report.Legible ? RenderMark(report) : string.Empty);
        }

        /// <summary>
        /// Turns one report into the marks a player has to carry out in their head.
        /// <para>
        /// §03's confusion pairs live in the glyphs, so the site pin is drawn as the
        /// three marks it is — wing, number, side — and never expanded into a sentence.
        /// A sentence would be a description a player could half-remember correctly;
        /// "ㅁ-6 좌" is four characters they either kept or did not.
        /// </para>
        /// </summary>
        private static string RenderMark(ClueReport report)
        {
            switch (report.Layer)
            {
                case ClueLayer.Feature:
                    return UiStrings.Feature(report.Feature);

                case ClueLayer.FloorMapping:
                    return UiStrings.Feature(report.Feature) + " → " + ClueGlyphs.Render(report.FloorNumber);

                case ClueLayer.SitePin:
                    return ClueGlyphs.Render(report.Label.Wing)
                        + "-" + ClueGlyphs.Render(report.Label.Number)
                        + " " + ClueGlyphs.Render(report.Label.Side);

                default:
                    return string.Empty;
            }
        }
    }
}
