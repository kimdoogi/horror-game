using System;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Sim
{
    /// <summary>
    /// One match's view of the building: the shared, precomputed geometry plus the
    /// only piece of world state a match actually changes — which zones are lit.
    /// <para>
    /// <b>Why this exists.</b> §03 fixes the map, so the geometry is shared between
    /// every match in a sweep and the routing tables are computed once. Lighting is
    /// not geometry: §04's 정비공 turns a zone on inside one match, and §03 makes that
    /// both the way clues get read and the way the monster is invited over. Sharing
    /// it would let one match light a zone in another — and since sweeps run matches
    /// in parallel, it would do so nondeterministically, which is the one failure a
    /// balance tool must never have.
    /// </para>
    /// </summary>
    public sealed class MatchWorld : IWorldProbe
    {
        private readonly CachedWorldProbe _shared;
        private readonly bool[] _litZones;

        /// <summary>Wraps the shared geometry with this match's own lighting.</summary>
        /// <param name="shared">The precomputed, read-only probe.</param>
        /// <param name="graph">The graph, for the zone count.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public MatchWorld(CachedWorldProbe shared, MapGraph graph)
        {
            _shared = shared ?? throw new ArgumentNullException(nameof(shared));
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            _litZones = new bool[graph.Zones.Length];
        }

        /// <summary>Brings a zone up or takes it down. §04's 구역 조명.</summary>
        public void SetZoneLit(int zoneId, bool lit)
        {
            if (zoneId >= 0 && zoneId < _litZones.Length)
            {
                _litZones[zoneId] = lit;
            }
        }

        /// <inheritdoc />
        public bool HasLineOfSight(Vec3 from, Vec3 to) => _shared.HasLineOfSight(from, to);

        /// <inheritdoc />
        public float NavigableDistance(Vec3 from, Vec3 to) => _shared.NavigableDistance(from, to);

        /// <inheritdoc />
        public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next) =>
            _shared.TryGetNextPathPoint(from, to, out next);

        /// <inheritdoc />
        public FloorMaterial SampleFloor(Vec3 position) => _shared.SampleFloor(position);

        /// <inheritdoc />
        public int ZoneIdAt(Vec3 position) => _shared.ZoneIdAt(position);

        /// <inheritdoc />
        public Vec3 SnapToNavigable(Vec3 desired) => _shared.SnapToNavigable(desired);

        /// <inheritdoc />
        public bool IsAreaLit(Vec3 position)
        {
            var zone = _shared.ZoneIdAt(position);
            return zone >= 0 && zone < _litZones.Length && _litZones[zone];
        }
    }
}
