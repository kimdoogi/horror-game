using HorrorGame.Core.Math;

namespace HorrorGame.Core.Clues
{
    /// <summary>
    /// Where a clue is, with nothing about what it says.
    /// <para>
    /// The host needs this to spawn the prop, and the prop has to exist in the world
    /// or nobody could find it. Splitting position from content is what lets the
    /// world be built for everyone while §13's "단서 내용 — 호스트만 보유" still holds:
    /// a client can see that there is a piece of paper on the desk, which is all a
    /// player standing in the room can see either.
    /// </para>
    /// <para>
    /// Markers do leak a little by existing — the site-pin clue sits on the
    /// objective's floor, so the set of marker floors contains it. That is
    /// unavoidable for a physical clue and is why the layout puts the other clues
    /// elsewhere: the marker set narrows the search the same way walking the
    /// building does, and no further.
    /// </para>
    /// </summary>
    public readonly struct ClueMarker
    {
        /// <summary>Identifies the clue in <see cref="ObjectiveResolver.TryRead"/>.</summary>
        public readonly int ClueId;

        /// <summary>Zone it is in. §12.</summary>
        public readonly int ZoneId;

        /// <summary>Floor it is on.</summary>
        public readonly int FloorIndex;

        /// <summary>Where to put the prop.</summary>
        public readonly Vec3 Position;

        /// <summary>Creates a marker.</summary>
        public ClueMarker(int clueId, int zoneId, int floorIndex, Vec3 position)
        {
            ClueId = clueId;
            ZoneId = zoneId;
            FloorIndex = floorIndex;
            Position = position;
        }
    }

    /// <summary>
    /// What one reader believes one clue said.
    /// <para>
    /// This is the reply ARCHITECTURE §4 describes — "the host replies with the
    /// rendered glyph for <em>that</em> clue only" — and it is deliberately not the
    /// clue. Every mark in it has already been through
    /// <see cref="MisreadModel"/>, so the value that crosses the wire is the
    /// player's belief. A client that logs every report it receives ends up with a
    /// transcript of what its own player thought they saw, which is exactly what
    /// that player could have written down anyway.
    /// </para>
    /// <para>
    /// Reading the same clue twice can produce two different reports. That is not a
    /// bug and it is not a re-roll of the world: §03 stores nothing, so there is no
    /// record to be consistent with. It is also the structural reason a clue cannot
    /// be carried out — the only durable copy is in a player's head.
    /// </para>
    /// </summary>
    public readonly struct ClueReport
    {
        /// <summary>Which clue was read.</summary>
        public readonly int ClueId;

        /// <summary>Which link of §03's chain it is. Visible: a player can see what kind of note they found.</summary>
        public readonly ClueLayer Layer;

        /// <summary>
        /// False when the read produced nothing — the light died, the mark is too far
        /// gone, or the beam was not held long enough. A failed read leaves the team
        /// with nothing rather than with something wrong.
        /// </summary>
        public readonly bool Legible;

        /// <summary>
        /// The property named. §03's first clue asserts the objective is on the floor
        /// with this property; its second asserts this property belongs to the floor
        /// signed <see cref="FloorNumber"/>. Words, not marks, so §03's four pairs
        /// cannot touch it.
        /// </summary>
        public readonly FloorFeature Feature;

        /// <summary>
        /// The floor sign, as perceived, on <see cref="ClueLayer.FloorMapping"/>. This
        /// is where 6↔9 and 1↔7 bite.
        /// </summary>
        public readonly ClueGlyph FloorNumber;

        /// <summary>
        /// The site sign, as perceived, on <see cref="ClueLayer.SitePin"/>. This is
        /// where ㅁ↔ㅇ and 좌↔우 bite.
        /// </summary>
        public readonly SiteLabel Label;

        /// <summary>Creates a report.</summary>
        public ClueReport(
            int clueId, ClueLayer layer, bool legible, FloorFeature feature, ClueGlyph floorNumber, SiteLabel label)
        {
            ClueId = clueId;
            Layer = layer;
            Legible = legible;
            Feature = feature;
            FloorNumber = floorNumber;
            Label = label;
        }

        /// <summary>A read that produced nothing. Names the clue, so the reader knows what to come back to.</summary>
        public static ClueReport Illegible(int clueId, ClueLayer layer) =>
            new ClueReport(clueId, layer, false, FloorFeature.None, ClueGlyph.Unreadable, SiteLabel.Unreadable);
    }
}
