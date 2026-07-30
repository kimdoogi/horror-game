using HorrorGame.Core.Math;

namespace HorrorGame.Core.Clues
{
    /// <summary>
    /// Everything about one look at one mark that decides whether the reader gets
    /// it right.
    /// <para>
    /// §03 splits the reading obstacles into two kinds without naming the split:
    /// the ones that belong to the mark ("낡아서 지워진 부분", handwriting, a sign
    /// hung upside down) and the ones that belong to the moment ("손전등의 좁은 빛",
    /// "급하게 봐야 하는 상황"). This struct is the two kinds already combined, which
    /// is why it is the input to the misread model and not the clue itself: the same
    /// mark read carefully and read in a panic are two different reads.
    /// </para>
    /// <para>
    /// Values are clamped on construction. A caller that hands in a light quality of
    /// 12 or a negative duration has a bug, but the clue system is not the place to
    /// discover it — a clamped read fails safe, an unclamped one produces a
    /// probability outside [0, 1] and silently changes the match.
    /// </para>
    /// </summary>
    public readonly struct GlyphViewing
    {
        /// <summary>
        /// How far the mark is rotated out of upright in the reader's view, degrees,
        /// wrapped to (-180, 180]. 180 is upside down. This is the mark's mounting
        /// and the reader's own orientation already added together — a sign hung
        /// upside down and read by a player who has tilted their head is upright
        /// again, and §03's 6↔9 pair should not fire on it.
        /// </summary>
        public readonly float ViewAngleDegrees;

        /// <summary>
        /// How softened the mark is, 0–1: wear, distance and haze combined. Past
        /// <see cref="GameConstants.ClueIllegibleBlur"/> nothing is readable.
        /// </summary>
        public readonly float Blur;

        /// <summary>
        /// Unbroken seconds spent on this mark. Below
        /// <see cref="GameConstants.ClueReadSeconds"/> the read has not happened at
        /// all; above <see cref="GameConstants.ClueConfidentReadSeconds"/> extra time
        /// buys nothing.
        /// </summary>
        public readonly float SecondsObserved;

        /// <summary>
        /// Light on the mark, 0–1. §03 makes darkness the lock on the objective, so
        /// this is both a gate (below
        /// <see cref="GameConstants.ClueMinReadableLightQuality"/> there is no read)
        /// and a dial (dim light widens every confusion pair).
        /// </summary>
        public readonly float LightQuality;

        /// <summary>The mark is handwritten rather than printed. §03's 1↔7 condition.</summary>
        public readonly bool Handwritten;

        /// <summary>The mark can only be read as a reflection. §03's 좌↔우 condition.</summary>
        public readonly bool ThroughReflection;

        /// <summary>Creates a viewing, clamping the analogue terms into range.</summary>
        public GlyphViewing(
            float viewAngleDegrees,
            float blur,
            float secondsObserved,
            float lightQuality,
            bool handwritten,
            bool throughReflection)
        {
            ViewAngleDegrees = MathX.NormalizeAngle(viewAngleDegrees);
            Blur = MathX.Clamp01(blur);
            SecondsObserved = secondsObserved > 0f ? secondsObserved : 0f;
            LightQuality = MathX.Clamp01(lightQuality);
            Handwritten = handwritten;
            ThroughReflection = throughReflection;
        }

        /// <summary>
        /// A printed mark, upright, unworn, under full light, looked at for as long
        /// as anyone ever needs to.
        /// <para>
        /// This is the reference point the misread model is defined against: under
        /// these conditions no glyph in the game may be misread, because a player
        /// who has done everything right must be able to trust what they saw. Every
        /// misread in a match traces back to one of these six terms being worse.
        /// </para>
        /// </summary>
        public static GlyphViewing Ideal =>
            new GlyphViewing(0f, 0f, GameConstants.ClueConfidentReadSeconds, 1f, false, false);

        /// <summary>This viewing with the mark rotated. Used to express §03's 뒤집힌 각도.</summary>
        public GlyphViewing WithAngle(float viewAngleDegrees) =>
            new GlyphViewing(viewAngleDegrees, Blur, SecondsObserved, LightQuality, Handwritten, ThroughReflection);

        /// <summary>This viewing with the mark softened. §03's 흐릿할 때.</summary>
        public GlyphViewing WithBlur(float blur) =>
            new GlyphViewing(ViewAngleDegrees, blur, SecondsObserved, LightQuality, Handwritten, ThroughReflection);

        /// <summary>This viewing cut short or drawn out. §03's 급하게 봐야 하는 상황.</summary>
        public GlyphViewing WithSeconds(float secondsObserved) =>
            new GlyphViewing(ViewAngleDegrees, Blur, secondsObserved, LightQuality, Handwritten, ThroughReflection);

        /// <summary>This viewing under different light. §03's 손전등의 좁은 빛.</summary>
        public GlyphViewing WithLight(float lightQuality) =>
            new GlyphViewing(ViewAngleDegrees, Blur, SecondsObserved, lightQuality, Handwritten, ThroughReflection);

        /// <summary>This viewing of a handwritten mark. §03's 손글씨체.</summary>
        public GlyphViewing AsHandwritten() =>
            new GlyphViewing(ViewAngleDegrees, Blur, SecondsObserved, LightQuality, true, ThroughReflection);

        /// <summary>This viewing of a mark seen in a mirror. §03's 거울 · 반사면.</summary>
        public GlyphViewing AsReflection() =>
            new GlyphViewing(ViewAngleDegrees, Blur, SecondsObserved, LightQuality, Handwritten, true);
    }

    /// <summary>
    /// What a reader takes away from one mark: the mark they believe they saw, and
    /// how likely they were to be wrong.
    /// <para>
    /// <see cref="Actual"/> is present for the host's own bookkeeping — telemetry
    /// counts misreads (§13's <c>ClueMisreads</c>) and the simulator needs to know
    /// when a wrong turn was the model's fault. It must never reach a client: a
    /// client that can compare the two knows the answer, and §03 dies the moment
    /// anything but the player's memory carries the mark out of the basement.
    /// </para>
    /// </summary>
    public readonly struct GlyphPerception
    {
        /// <summary>What is written there. Host-side only.</summary>
        public readonly ClueGlyph Actual;

        /// <summary>What the reader believes is written there. This is the only value a client may see.</summary>
        public readonly ClueGlyph Perceived;

        /// <summary>The confusion pair this mark belongs to, if any. §03's table.</summary>
        public readonly MisreadCondition Condition;

        /// <summary>The probability the misread had, before the die was rolled. Telemetry and tuning.</summary>
        public readonly float MisreadChance;

        /// <summary>False when the light, the wear or the haste left nothing to perceive.</summary>
        public readonly bool Legible;

        /// <summary>Creates a perception.</summary>
        public GlyphPerception(
            ClueGlyph actual,
            ClueGlyph perceived,
            MisreadCondition condition,
            float misreadChance,
            bool legible)
        {
            Actual = actual;
            Perceived = perceived;
            Condition = condition;
            MisreadChance = misreadChance;
            Legible = legible;
        }

        /// <summary>True when the reader walked away with the wrong mark. §03's intended failure.</summary>
        public bool Misread => Legible && Perceived != Actual;
    }

    /// <summary>
    /// The reader's side of a completed read, handed back by <see cref="ClueReader"/>.
    /// <para>
    /// Separate from <see cref="GlyphViewing"/> because the reader does not know
    /// what it is looking at. The mark's own properties — wear, handwriting, how the
    /// sign hangs — are host-side content, so the two halves are only ever combined
    /// inside <see cref="ObjectiveResolver"/>. That split is what stops a client
    /// from assembling a viewing and asking the misread model questions offline.
    /// </para>
    /// </summary>
    public readonly struct ClueObservation
    {
        /// <summary>Unbroken seconds the beam stayed on the clue.</summary>
        public readonly float SecondsHeld;

        /// <summary>
        /// The worst light the read had to survive, 0–1. The weakest moment decides,
        /// not the average: §03's obstacle is "손전등의 좁은 빛", and a beam that
        /// slipped off the mark for a second cost the reader that second.
        /// </summary>
        public readonly float WorstLightQuality;

        /// <summary>How far out of upright the reader's own view was, degrees.</summary>
        public readonly float ViewAngleDegrees;

        /// <summary>Blur the reader contributed — distance, haze, a shaking hand. 0–1.</summary>
        public readonly float Blur;

        /// <summary>Creates an observation, clamping the analogue terms into range.</summary>
        public ClueObservation(float secondsHeld, float worstLightQuality, float viewAngleDegrees, float blur)
        {
            SecondsHeld = secondsHeld > 0f ? secondsHeld : 0f;
            WorstLightQuality = MathX.Clamp01(worstLightQuality);
            ViewAngleDegrees = MathX.NormalizeAngle(viewAngleDegrees);
            Blur = MathX.Clamp01(blur);
        }

        /// <summary>
        /// The best a reader can do: upright, steady, in full light, unhurried. Any
        /// misread that survives this came from the mark, not from the reading.
        /// </summary>
        public static ClueObservation Ideal =>
            new ClueObservation(GameConstants.ClueConfidentReadSeconds, 1f, 0f, 0f);
    }
}
