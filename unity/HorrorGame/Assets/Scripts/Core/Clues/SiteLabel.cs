using System;

namespace HorrorGame.Core.Clues
{
    /// <summary>
    /// A property that distinguishes one floor of the building from the others.
    /// <para>
    /// §03's first clue is not a location, it is a property: "그것은 물이 있는 층에
    /// 있다". That is the whole reason the chain needs a second clue — a property is
    /// useless until something maps it onto a floor number. Water is the document's
    /// worked example; the rest of the set exists because a property that only ever
    /// takes one value carries no information at all.
    /// </para>
    /// <para>
    /// A property belongs to at most one floor, enforced by
    /// <see cref="SiteCatalog"/>: the second clue is phrased as a mapping
    /// ("물이 있는 층은 지하 3층이다"), and a mapping with two answers is not a clue.
    /// </para>
    /// </summary>
    public enum FloorFeature
    {
        /// <summary>
        /// No distinguishing property. A floor like this can hold clues and loot but
        /// never the objective — §03's first clue would have nothing to say about it.
        /// </summary>
        None = 0,

        /// <summary>물 — standing water. §03's worked example.</summary>
        Water = 1,

        /// <summary>기계 — running machinery, and the noise that comes with it (§04 청음사).</summary>
        Machinery = 2,

        /// <summary>냉기 — cold enough to see your breath.</summary>
        Cold = 3,

        /// <summary>녹 — rust over every surface.</summary>
        Rust = 4,

        /// <summary>붕괴 — a partially collapsed floor.</summary>
        Collapse = 5,
    }

    /// <summary>
    /// How a candidate site is signed inside the building: a wing, a number and a
    /// side, e.g. "ㅁ-6 좌".
    /// <para>
    /// The shape is chosen so that each of §03's four confusion pairs has somewhere
    /// to live. The wing carries ㅁ↔ㅇ, the number carries 6↔9 and 1↔7, and the side
    /// carries 좌↔우. A team that misremembers exactly one mark therefore walks to a
    /// real place that is the wrong place — which is the failure §03 is designed
    /// around, and is strictly better than walking into a wall, because nothing
    /// tells them they were wrong until they arrive.
    /// </para>
    /// <para>
    /// Labels are unique per floor and reused across floors on purpose. Reuse is
    /// what makes the floor half of the chain load-bearing: "ㅁ-6 좌" on its own is
    /// several places in the building.
    /// </para>
    /// </summary>
    public readonly struct SiteLabel : IEquatable<SiteLabel>
    {
        /// <summary>Wing marker — ㅁ or ㅇ.</summary>
        public readonly ClueGlyph Wing;

        /// <summary>Room number — 1–9.</summary>
        public readonly ClueGlyph Number;

        /// <summary>Side of the wing — 좌 or 우.</summary>
        public readonly ClueGlyph Side;

        /// <summary>Creates a label from its three marks.</summary>
        public SiteLabel(ClueGlyph wing, ClueGlyph number, ClueGlyph side)
        {
            Wing = wing;
            Number = number;
            Side = side;
        }

        /// <summary>The label a failed read produces: three marks, none of them perceived.</summary>
        public static SiteLabel Unreadable =>
            new SiteLabel(ClueGlyph.Unreadable, ClueGlyph.Unreadable, ClueGlyph.Unreadable);

        /// <summary>
        /// True when each mark is drawn from the right alphabet.
        /// <see cref="SiteCatalog"/> rejects a map that signs a site any other way,
        /// because a label outside the alphabet has no confusion partner and would
        /// quietly become the one site §03's misread model cannot touch.
        /// </summary>
        public bool IsWellFormed =>
            ClueGlyphs.IsWing(Wing) && ClueGlyphs.IsDigit(Number) && ClueGlyphs.IsSide(Side);

        /// <inheritdoc />
        public bool Equals(SiteLabel other) => Wing == other.Wing && Number == other.Number && Side == other.Side;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is SiteLabel other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine((int)Wing, (int)Number, (int)Side);

        /// <summary>Mark-for-mark equality. Two labels differing in one mark are two different places.</summary>
        public static bool operator ==(SiteLabel a, SiteLabel b) => a.Equals(b);

        /// <summary>Mark-for-mark inequality.</summary>
        public static bool operator !=(SiteLabel a, SiteLabel b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() =>
            ClueGlyphs.Render(Wing) + "-" + ClueGlyphs.Render(Number) + " " + ClueGlyphs.Render(Side);
    }
}
