using System;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Clues
{
    /// <summary>
    /// Turns §03's 혼동쌍 table into a probability, then into what the player
    /// believes they saw.
    /// <para>
    /// The shape of the model is dictated by the table. §03 gives each pair one
    /// condition and nothing else — no probabilities, no interaction between rows —
    /// so the model is built as <em>condition strength × attention</em>:
    /// </para>
    /// <list type="bullet">
    /// <item>A mark can only be misread as its own partner, and only when that
    /// partner's condition is present. A blurred 6 is a 6. An upside-down ㅁ is a ㅁ.
    /// This is what keeps the pairs learnable, which §03 requires of everything it
    /// calls 조합 방식.</item>
    /// <item>The other obstacles §03 lists — "손전등의 좁은 빛", "급하게 봐야 하는
    /// 상황" — widen a pair that is already open. They never open one. That is the
    /// literal reading of "여기에 읽기 방해 요소를 얹는다": they are laid on top.</item>
    /// <item>Care never reaches certainty. §03 plants the ambiguity in the mark, so
    /// <see cref="GameConstants.ClueMisreadFocusedFraction"/> of the risk survives a
    /// perfect look — unless the pair's condition is absent, in which case the risk
    /// is exactly zero.</item>
    /// </list>
    /// <para>
    /// Every draw comes from an <see cref="IRandomSource"/>, so a seed replays a
    /// match's misreads exactly. That matters more here than anywhere else in the
    /// game: "6이었나 9였나" is the story players tell afterwards, and a bug report
    /// about it is worthless if the same seed reads the sign correctly next time.
    /// </para>
    /// </summary>
    public static class MisreadModel
    {
        /// <summary>
        /// Whether this look produced a mark at all.
        /// <para>
        /// Three separate walls, all from §03: no light is the objective's lock, a
        /// mark worn past <see cref="GameConstants.ClueIllegibleBlur"/> has stopped
        /// being a mark, and a look shorter than
        /// <see cref="GameConstants.ClueReadSeconds"/> never became a read
        /// ("오래 비춰야 읽힌다"). An illegible look is safe — it leaves the team with
        /// nothing, not with something wrong.
        /// </para>
        /// </summary>
        public static bool IsLegible(GlyphViewing viewing) =>
            viewing.LightQuality >= GameConstants.ClueMinReadableLightQuality
            && viewing.Blur < GameConstants.ClueIllegibleBlur
            && viewing.SecondsObserved >= GameConstants.ClueReadSeconds;

        /// <summary>
        /// Which of §03's conditions this look presents, regardless of what is
        /// written. Used by tests to prove a pair stays shut under the other three
        /// conditions, and by telemetry to attribute a misread to a row of the table.
        /// </summary>
        public static MisreadCondition ActiveConditions(GlyphViewing viewing)
        {
            var active = MisreadCondition.None;

            if (ConditionStrength(MisreadCondition.InvertedAngle, viewing) > 0f)
            {
                active |= MisreadCondition.InvertedAngle;
            }

            if (ConditionStrength(MisreadCondition.Handwriting, viewing) > 0f)
            {
                active |= MisreadCondition.Handwriting;
            }

            if (ConditionStrength(MisreadCondition.Blur, viewing) > 0f)
            {
                active |= MisreadCondition.Blur;
            }

            if (ConditionStrength(MisreadCondition.Reflection, viewing) > 0f)
            {
                active |= MisreadCondition.Reflection;
            }

            return active;
        }

        /// <summary>
        /// How strongly one row of §03's table applies to this look, 0–1, already
        /// scaled by that row's weight.
        /// <para>
        /// The two geometric conditions ramp — a sign tilted 130° is nearly a 6 and
        /// nearly a 9, one at 180° is unambiguously both — and the two categorical
        /// conditions do not, because a mark is either handwritten or printed, and
        /// either only readable in a mirror or not.
        /// </para>
        /// </summary>
        public static float ConditionStrength(MisreadCondition condition, GlyphViewing viewing)
        {
            switch (condition)
            {
                case MisreadCondition.InvertedAngle:
                    var offUpright = System.Math.Abs(viewing.ViewAngleDegrees);
                    return MathX.InverseLerp(GameConstants.ClueInvertedViewAngle, 180f, offUpright)
                           * GameConstants.ClueMisreadWeightInvertedAngle;

                case MisreadCondition.Handwriting:
                    return viewing.Handwritten ? GameConstants.ClueMisreadWeightHandwriting : 0f;

                case MisreadCondition.Blur:
                    return MathX.InverseLerp(GameConstants.ClueBlurConfusionThreshold, 1f, viewing.Blur)
                           * GameConstants.ClueMisreadWeightBlur;

                case MisreadCondition.Reflection:
                    return viewing.ThroughReflection ? GameConstants.ClueMisreadWeightReflection : 0f;

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// How much the moment is working against the reader, 0–1: §03's "손전등의
        /// 좁은 빛" and "급하게 봐야 하는 상황".
        /// <para>
        /// The worse of the two decides rather than their sum. A reader in the dark
        /// is already at the limit of what they can make out, and being unhurried as
        /// well does not help — which is also why a player cannot buy safety by
        /// standing in front of a clue for a minute with a dying battery.
        /// </para>
        /// </summary>
        public static float Interference(GlyphViewing viewing)
        {
            var dark = 1f - viewing.LightQuality;
            var haste = 1f - MathX.InverseLerp(
                GameConstants.ClueReadSeconds,
                GameConstants.ClueConfidentReadSeconds,
                viewing.SecondsObserved);

            return MathX.Clamp01(System.Math.Max(dark, haste));
        }

        /// <summary>
        /// The probability this look ends with the wrong mark. Zero for a mark §03
        /// plants no partner for, zero when the partner's condition is absent, and
        /// zero when the look produced nothing at all.
        /// </summary>
        public static float MisreadChance(ClueGlyph glyph, GlyphViewing viewing)
        {
            if (!IsLegible(viewing) || !ClueGlyphs.TryGetConfusion(glyph, out _, out var condition))
            {
                return 0f;
            }

            var strength = ConditionStrength(condition, viewing);
            if (strength <= 0f)
            {
                return 0f;
            }

            var attention = MathX.Lerp(
                GameConstants.ClueMisreadFocusedFraction, 1f, Interference(viewing));

            return MathX.Clamp01(GameConstants.ClueMisreadChanceMax * strength * attention);
        }

        /// <summary>
        /// What the reader walks away believing.
        /// <para>
        /// Host-side. The <see cref="GlyphPerception.Perceived"/> half is the only
        /// thing that may cross the wire (ARCHITECTURE §4: the host answers with the
        /// rendered glyph for the clue being read, and nothing else) — and note that
        /// what crosses is the <em>misread</em>, so even a client reading its own
        /// memory learns what its player believes rather than what is true.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="rng"/> is null.</exception>
        public static GlyphPerception Perceive(ClueGlyph glyph, GlyphViewing viewing, IRandomSource rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            ClueGlyphs.TryGetConfusion(glyph, out var partner, out var condition);

            if (glyph == ClueGlyph.Unreadable || !IsLegible(viewing))
            {
                return new GlyphPerception(glyph, ClueGlyph.Unreadable, condition, 0f, false);
            }

            var chance = MisreadChance(glyph, viewing);

            // Chance() must not draw at probability 0, or the ideal-viewing
            // guarantee would depend on the stream's next value.
            var confused = chance > 0f && rng.Chance(chance);

            return new GlyphPerception(
                glyph, confused ? partner : glyph, condition, chance, true);
        }

        /// <summary>
        /// Perceives all three marks of a site label.
        /// <para>
        /// Each mark is drawn independently and in a fixed order — wing, number,
        /// side — so a seed reproduces the label a team argued about. Independence is
        /// the point: a team that gets two marks right and one wrong walks
        /// confidently to a real room that is not the room, which is §03's failure
        /// mode rather than a degenerate one.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="rng"/> is null.</exception>
        public static SiteLabel PerceiveLabel(
            SiteLabel label, GlyphViewing viewing, IRandomSource rng, out bool anyMisread)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var wing = Perceive(label.Wing, viewing, rng);
            var number = Perceive(label.Number, viewing, rng);
            var side = Perceive(label.Side, viewing, rng);

            anyMisread = wing.Misread || number.Misread || side.Misread;

            return new SiteLabel(wing.Perceived, number.Perceived, side.Perceived);
        }
    }
}
