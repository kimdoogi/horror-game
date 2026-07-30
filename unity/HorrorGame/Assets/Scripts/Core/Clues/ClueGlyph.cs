using System;
using System.Collections.Generic;

namespace HorrorGame.Core.Clues
{
    /// <summary>
    /// One mark a clue can be written with.
    /// <para>
    /// §03 does not treat a clue as a sentence — it treats it as a handful of
    /// marks that the reader has to carry out in their head. That is why the
    /// alphabet is enumerated here instead of being a string: the whole confusion
    /// system in §03 ("6 ↔ 9 뒤집힌 각도에서", "ㅁ ↔ ㅇ 흐릿할 때") is a rule about
    /// pairs of marks, and a rule cannot be applied to arbitrary text.
    /// </para>
    /// <para>
    /// The set is deliberately small. Every glyph is either a member of one of
    /// §03's four confusion pairs, or a digit that belongs to no pair — and the
    /// unpaired digits matter just as much, because they are the marks a player
    /// can trust. If every glyph were confusable, reading a clue would be a coin
    /// flip and §03's "기억해서 말로 전달" would stop being a skill.
    /// </para>
    /// </summary>
    public enum ClueGlyph
    {
        /// <summary>Nothing was perceived — the read failed, or a field is unset.</summary>
        Unreadable = 0,

        /// <summary>1. Confusable with <see cref="Digit7"/> in handwriting. §03.</summary>
        Digit1 = 1,

        /// <summary>2. Belongs to no confusion pair — a mark the team can trust.</summary>
        Digit2 = 2,

        /// <summary>3. Belongs to no confusion pair.</summary>
        Digit3 = 3,

        /// <summary>4. Belongs to no confusion pair.</summary>
        Digit4 = 4,

        /// <summary>5. Belongs to no confusion pair.</summary>
        Digit5 = 5,

        /// <summary>6. Confusable with <see cref="Digit9"/> from an inverted angle. §03.</summary>
        Digit6 = 6,

        /// <summary>7. Confusable with <see cref="Digit1"/> in handwriting. §03.</summary>
        Digit7 = 7,

        /// <summary>8. Belongs to no confusion pair.</summary>
        Digit8 = 8,

        /// <summary>9. Confusable with <see cref="Digit6"/> from an inverted angle. §03.</summary>
        Digit9 = 9,

        /// <summary>ㅁ — a wing marker. Confusable with <see cref="WingIeung"/> when blurred. §03.</summary>
        WingMieum = 20,

        /// <summary>ㅇ — a wing marker. Confusable with <see cref="WingMieum"/> when blurred. §03.</summary>
        WingIeung = 21,

        /// <summary>좌 — left. Confusable with <see cref="SideRight"/> in a reflection. §03.</summary>
        SideLeft = 30,

        /// <summary>우 — right. Confusable with <see cref="SideLeft"/> in a reflection. §03.</summary>
        SideRight = 31,
    }

    /// <summary>
    /// The circumstance that makes a confusion pair collapse, one flag per row of
    /// §03's 혼동쌍 table.
    /// <para>
    /// Flags rather than a plain enum because a single look can present several of
    /// them at once — a worn sign hanging upside down in a dying beam — while a
    /// given glyph only ever answers to one of them. Keeping the two separate is
    /// what makes "each pair misreads only under its own condition" checkable
    /// instead of emergent.
    /// </para>
    /// </summary>
    [Flags]
    public enum MisreadCondition
    {
        /// <summary>Nothing about this look invites a misread.</summary>
        None = 0,

        /// <summary>뒤집힌 각도 — the mark is rotated far enough out of upright. Fires 6↔9. §03.</summary>
        InvertedAngle = 1,

        /// <summary>손글씨체 — the mark is handwritten rather than printed. Fires 1↔7. §03.</summary>
        Handwriting = 2,

        /// <summary>흐릿함 — wear, distance or haze have softened the mark. Fires ㅁ↔ㅇ. §03.</summary>
        Blur = 4,

        /// <summary>거울 · 반사면 — the mark is only readable as a reflection. Fires 좌↔우. §03.</summary>
        Reflection = 8,
    }

    /// <summary>
    /// One row of §03's 혼동쌍 table: two marks and the circumstance under which a
    /// reader swaps them.
    /// </summary>
    public readonly struct ClueConfusionPair
    {
        /// <summary>One member of the pair.</summary>
        public readonly ClueGlyph A;

        /// <summary>The other member. The relation is symmetric — §03 gives no direction.</summary>
        public readonly ClueGlyph B;

        /// <summary>The circumstance that collapses the two. §03's second column.</summary>
        public readonly MisreadCondition Condition;

        /// <summary>Creates a pair. Only <see cref="ClueGlyphs"/> should need this.</summary>
        public ClueConfusionPair(ClueGlyph a, ClueGlyph b, MisreadCondition condition)
        {
            A = a;
            B = b;
            Condition = condition;
        }

        /// <summary>True when <paramref name="glyph"/> is one of the two marks.</summary>
        public bool Contains(ClueGlyph glyph) => glyph == A || glyph == B;

        /// <summary>
        /// The mark a reader lands on instead of <paramref name="glyph"/>, or
        /// <see cref="ClueGlyph.Unreadable"/> when the glyph is not in this pair.
        /// </summary>
        public ClueGlyph Other(ClueGlyph glyph)
        {
            if (glyph == A)
            {
                return B;
            }

            return glyph == B ? A : ClueGlyph.Unreadable;
        }
    }

    /// <summary>
    /// The glyph alphabet and §03's confusion table.
    /// <para>
    /// This table is the one part of the clue system §03 insists must never be
    /// randomised: "맵 구조 · 단서 조합 방식 — 고정. 학습 가능해야 실력이 성장한다."
    /// A team learns that a 6 on a hanging sign is worth double-checking, and that
    /// learning is only worth anything if the pairs are the same every match.
    /// </para>
    /// </summary>
    public static class ClueGlyphs
    {
        private static readonly ClueConfusionPair[] PairTable =
        {
            new ClueConfusionPair(ClueGlyph.Digit6, ClueGlyph.Digit9, MisreadCondition.InvertedAngle),
            new ClueConfusionPair(ClueGlyph.Digit1, ClueGlyph.Digit7, MisreadCondition.Handwriting),
            new ClueConfusionPair(ClueGlyph.WingMieum, ClueGlyph.WingIeung, MisreadCondition.Blur),
            new ClueConfusionPair(ClueGlyph.SideLeft, ClueGlyph.SideRight, MisreadCondition.Reflection),
        };

        private static readonly ClueGlyph[] DigitTable =
        {
            ClueGlyph.Digit1, ClueGlyph.Digit2, ClueGlyph.Digit3, ClueGlyph.Digit4, ClueGlyph.Digit5,
            ClueGlyph.Digit6, ClueGlyph.Digit7, ClueGlyph.Digit8, ClueGlyph.Digit9,
        };

        /// <summary>§03's 혼동쌍 table, in the order the document lists it.</summary>
        public static IReadOnlyList<ClueConfusionPair> Pairs => PairTable;

        /// <summary>The digits a floor or a room number can be signed with, 1–9.</summary>
        public static IReadOnlyList<ClueGlyph> Digits => DigitTable;

        /// <summary>
        /// Finds the mark <paramref name="glyph"/> can be mistaken for.
        /// <para>
        /// Returns false for the marks §03 leaves alone. Callers must treat that as
        /// "this mark is trustworthy" rather than as an error: an unpaired digit is
        /// the only thing a player can report without hedging.
        /// </para>
        /// </summary>
        public static bool TryGetConfusion(ClueGlyph glyph, out ClueGlyph partner, out MisreadCondition condition)
        {
            for (var i = 0; i < PairTable.Length; i++)
            {
                if (PairTable[i].Contains(glyph))
                {
                    partner = PairTable[i].Other(glyph);
                    condition = PairTable[i].Condition;
                    return true;
                }
            }

            partner = ClueGlyph.Unreadable;
            condition = MisreadCondition.None;
            return false;
        }

        /// <summary>True when §03 plants no partner for this mark, so it always reads true.</summary>
        public static bool IsTrustworthy(ClueGlyph glyph) =>
            glyph != ClueGlyph.Unreadable && !TryGetConfusion(glyph, out _, out _);

        /// <summary>True for 1–9.</summary>
        public static bool IsDigit(ClueGlyph glyph) => glyph >= ClueGlyph.Digit1 && glyph <= ClueGlyph.Digit9;

        /// <summary>True for ㅁ and ㅇ.</summary>
        public static bool IsWing(ClueGlyph glyph) => glyph == ClueGlyph.WingMieum || glyph == ClueGlyph.WingIeung;

        /// <summary>True for 좌 and 우.</summary>
        public static bool IsSide(ClueGlyph glyph) => glyph == ClueGlyph.SideLeft || glyph == ClueGlyph.SideRight;

        /// <summary>The numeric value of a digit glyph, or -1 for anything else.</summary>
        public static int DigitValue(ClueGlyph glyph) => IsDigit(glyph) ? (int)glyph : -1;

        /// <summary>
        /// The glyph a number is signed with, or <see cref="ClueGlyph.Unreadable"/>
        /// outside 1–9. A building with more than nine signed floors would need a
        /// second glyph per sign, which §03's pair model does not describe.
        /// </summary>
        public static ClueGlyph FromDigit(int value) =>
            value >= 1 && value <= 9 ? (ClueGlyph)value : ClueGlyph.Unreadable;

        /// <summary>
        /// The mark as a human reads it. The UI draws its own art; this exists so
        /// logs, telemetry and test failures name the mark the way the design
        /// document does.
        /// </summary>
        public static string Render(ClueGlyph glyph)
        {
            switch (glyph)
            {
                case ClueGlyph.WingMieum: return "ㅁ";
                case ClueGlyph.WingIeung: return "ㅇ";
                case ClueGlyph.SideLeft: return "좌";
                case ClueGlyph.SideRight: return "우";
                case ClueGlyph.Unreadable: return "?";
                default:
                    var digit = DigitValue(glyph);
                    return digit > 0 ? digit.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";
            }
        }
    }
}
