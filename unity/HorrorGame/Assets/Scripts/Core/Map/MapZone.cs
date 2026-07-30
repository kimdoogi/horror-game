using System.Globalization;
using HorrorGame.Core.Math;

namespace HorrorGame.Core.Map
{
    /// <summary>
    /// One 구역 of the map: an axis-aligned box with a floor surface.
    /// <para>
    /// A zone is the unit almost every other system counts in. §07 measures the
    /// monster's patrol in zones, §12 requires a distinct floor material per zone so
    /// the Listener has a map at all, and §03 narrows the objective floor → zone →
    /// site. So a zone is not a piece of level dressing; it is the granularity at
    /// which the design's information economy is defined.
    /// </para>
    /// <para>
    /// A box rather than an arbitrary polygon on purpose. §12 demands "재질 경계를
    /// 명확히 할 것" — the Listener's whole ability rests on a footstep belonging to
    /// exactly one surface — and a box is the shape whose boundary a designer cannot
    /// accidentally make ambiguous. <see cref="MapValidator"/> enforces that no two
    /// zones overlap, which is what makes <see cref="MapGraph.ZoneIdAt"/> a total
    /// function rather than a guess.
    /// </para>
    /// </summary>
    public sealed class MapZone
    {
        /// <summary>
        /// Builds a zone from its centre and extent.
        /// </summary>
        /// <param name="id">Index into <see cref="MapGraph.Zones"/>.</param>
        /// <param name="name">Label used in validator failures — §12's sketch names them A/B/C/D.</param>
        /// <param name="floor">
        /// The surface underfoot. §12 assigns 나무 · 타일 · 자갈 · 콘크리트 to A–D and
        /// 금속 to stairs; <see cref="FloorMaterial.None"/> fails validation because
        /// it would leave the Listener with a silent zone.
        /// </param>
        /// <param name="center">Centre in metres.</param>
        /// <param name="size">
        /// Full extent in metres: X wide, Y tall, Z deep. Y may be 0 for a
        /// single-storey map, in which case the zone is treated as covering every
        /// height at that footprint.
        /// </param>
        public MapZone(int id, string name, FloorMaterial floor, Vec3 center, Vec3 size)
        {
            Id = id;
            Name = name;
            Floor = floor;
            Center = center;
            Size = new Vec3(
                size.X < 0f ? -size.X : size.X,
                size.Y < 0f ? -size.Y : size.Y,
                size.Z < 0f ? -size.Z : size.Z);
        }

        /// <summary>Index into <see cref="MapGraph.Zones"/>.</summary>
        public int Id { get; }

        /// <summary>Designer-facing label. Appears verbatim in every validator message about this zone.</summary>
        public string Name { get; }

        /// <summary>
        /// The floor surface. §12: "구역별로 바닥 재질이 달라야 청음사가 위치를 판별할
        /// 수 있다. 아트 결정이 아니라 시스템 결정이다."
        /// </summary>
        public FloorMaterial Floor { get; }

        /// <summary>Centre of the zone, metres.</summary>
        public Vec3 Center { get; }

        /// <summary>Full extent, metres, always non-negative.</summary>
        public Vec3 Size { get; }

        /// <summary>Lowest corner of the zone box.</summary>
        public Vec3 Min => new Vec3(
            Center.X - (Size.X * 0.5f),
            Center.Y - (Size.Y * 0.5f),
            Center.Z - (Size.Z * 0.5f));

        /// <summary>Highest corner of the zone box.</summary>
        public Vec3 Max => new Vec3(
            Center.X + (Size.X * 0.5f),
            Center.Y + (Size.Y * 0.5f),
            Center.Z + (Size.Z * 0.5f));

        /// <summary>
        /// Horizontal diagonal, metres. §12 pins this to 30~40 m, which is what
        /// makes a zone crossable inside one sprint
        /// (<see cref="GameConstants.SprintMaxTravelDistance"/> is 60 m) and small
        /// enough that a Listener fix off by
        /// <see cref="GameConstants.ListenerErrorRadiusMax"/> names the wrong corner
        /// rather than the wrong zone.
        /// </summary>
        public float Diagonal => new Vec3(Size.X, 0f, Size.Z).MagnitudeFlat;

        /// <summary>
        /// True when the position lies inside this zone. A zone with
        /// <see cref="Size"/>.Y of 0 ignores height, so a single-storey map does not
        /// have to guess a ceiling.
        /// <para>
        /// Bounds are inclusive on both sides. Two zones sharing a face would
        /// therefore both claim the seam — which is exactly why
        /// <see cref="MapValidator"/> rejects overlapping zones instead of picking a
        /// winner and hiding the ambiguity from the designer.
        /// </para>
        /// </summary>
        public bool Contains(Vec3 position)
        {
            var min = Min;
            var max = Max;

            if (position.X < min.X || position.X > max.X)
            {
                return false;
            }

            if (position.Z < min.Z || position.Z > max.Z)
            {
                return false;
            }

            if (Size.Y <= 0f)
            {
                return true;
            }

            return position.Y >= min.Y && position.Y <= max.Y;
        }

        /// <summary>
        /// True when two zones share any volume — a footstep in the overlap would
        /// belong to two floor materials at once, which is the one thing §12's
        /// "경계를 명확히" rule forbids.
        /// <para>
        /// Touching faces do not count as overlap: a strict comparison is used so
        /// that zones tiled edge to edge (§12's sketch stacks them) are legal, while
        /// any genuine interpenetration is not.
        /// </para>
        /// </summary>
        public bool OverlapsVolume(MapZone other)
        {
            if (other == null || ReferenceEquals(other, this))
            {
                return false;
            }

            var aMin = Min;
            var aMax = Max;
            var bMin = other.Min;
            var bMax = other.Max;

            if (aMax.X <= bMin.X || bMax.X <= aMin.X)
            {
                return false;
            }

            if (aMax.Z <= bMin.Z || bMax.Z <= aMin.Z)
            {
                return false;
            }

            // A zone with no declared height spans every floor, so it can only be
            // separated horizontally. Two flat zones therefore overlap whenever
            // their footprints do — which is the conservative reading, and the one
            // that keeps a designer from stacking rooms without saying so.
            if (Size.Y <= 0f || other.Size.Y <= 0f)
            {
                return true;
            }

            return aMax.Y > bMin.Y && bMax.Y > aMin.Y;
        }

        /// <summary>
        /// Listener clarity of this zone's floor, on the 0~1 scale §04 uses. §12's
        /// 발소리 column turned into a number: 금속 울림 is the clearest signal in the
        /// building and 콘크리트 둔탁 the murkiest, which is what makes "지금 계단이야"
        /// a usable call and zone D the monster's best approach.
        /// </summary>
        public float FootstepClarity => ClarityOf(Floor);

        /// <summary>
        /// Listener clarity for a surface. Exposed so a probe can answer for a
        /// position that is between zones (a stairwell landing) without inventing a
        /// zone for it.
        /// </summary>
        public static float ClarityOf(FloorMaterial material)
        {
            switch (material)
            {
                case FloorMaterial.Wood:
                    return GameConstants.ListenerClarityWood;

                case FloorMaterial.Tile:
                    return GameConstants.ListenerClarityTile;

                case FloorMaterial.Gravel:
                    return GameConstants.ListenerClarityGravel;

                case FloorMaterial.Concrete:
                    return GameConstants.ListenerClarityConcrete;

                case FloorMaterial.Metal:
                    return GameConstants.ListenerClarityMetal;

                default:
                    return GameConstants.ListenerClarityUnknown;
            }
        }

        /// <inheritdoc />
        public override string ToString() =>
            "zone " + Id + " " + Name + " (" + Floor + ", diagonal "
            + Diagonal.ToString("0.#", CultureInfo.InvariantCulture) + " m)";
    }
}
