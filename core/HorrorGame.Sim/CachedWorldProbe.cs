using System;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Sim
{
    /// <summary>
    /// <see cref="MapGraphProbe"/>'s answers, precomputed.
    /// <para>
    /// <b>Why.</b> A 35-minute match is 105,000 steps at
    /// <see cref="Core.GameConstants.FixedStep"/>, and the monster asks the probe for
    /// a path on every one of them. <see cref="MapGraph.ShortestPath"/> runs Dijkstra
    /// and allocates, so a sweep over thousands of matches spends essentially all of
    /// its time re-deriving the same route through the same fixed building. §03 puts
    /// 맵 구조 in the 고정 column, so the answers cannot change between matches — which
    /// makes them cacheable exactly rather than approximately.
    /// </para>
    /// <para>
    /// <b>Honesty.</b> This class computes nothing itself. Every table entry is an
    /// answer the wrapped <see cref="MapGraphProbe"/> gave, and
    /// <see cref="Verify"/> re-asks it for every node pair and fails if a single
    /// answer differs. A cache that quietly disagreed with Core would look exactly
    /// like a balance finding, so the simulator refuses to start without the check.
    /// </para>
    /// </summary>
    public sealed class CachedWorldProbe : IWorldProbe
    {
        private readonly MapGraphProbe _inner;
        private readonly MapGraph _graph;
        private readonly int _n;

        private readonly float[] _distance;
        private readonly int[] _nextHop;
        private readonly bool[] _sight;

        /// <summary>Wraps a probe and fills the tables from it.</summary>
        /// <param name="inner">Core's probe. Every answer here came from it.</param>
        /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
        public CachedWorldProbe(MapGraphProbe inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _graph = inner.Graph;
            _n = _graph.Nodes.Length;

            _distance = new float[_n * _n];
            _nextHop = new int[_n * _n];
            _sight = new bool[_n * _n];

            for (var a = 0; a < _n; a++)
            {
                for (var b = 0; b < _n; b++)
                {
                    var slot = (a * _n) + b;
                    _distance[slot] = _graph.PathLength(a, b);
                    _sight[slot] = _graph.HasStraightSightLine(a, b);

                    if (a == b)
                    {
                        _nextHop[slot] = a;
                        continue;
                    }

                    var route = _graph.ShortestPath(a, b, -1);
                    _nextHop[slot] = route != null && route.Length >= 2 ? route[1] : -1;
                }
            }
        }

        /// <summary>The probe being cached, for anything that wants the live object (zone lighting).</summary>
        public MapGraphProbe Inner => _inner;

        /// <inheritdoc />
        public bool HasLineOfSight(Vec3 from, Vec3 to)
        {
            var a = _graph.NearestNode(from);
            var b = _graph.NearestNode(to);
            return a >= 0 && b >= 0 && _sight[(a * _n) + b];
        }

        /// <inheritdoc />
        public float NavigableDistance(Vec3 from, Vec3 to)
        {
            var a = _graph.NearestNode(from);
            var b = _graph.NearestNode(to);
            if (a < 0 || b < 0)
            {
                return float.PositiveInfinity;
            }

            var along = _distance[(a * _n) + b];
            if (float.IsPositiveInfinity(along))
            {
                return float.PositiveInfinity;
            }

            return along
                   + Vec3.DistanceFlat(from, _graph.Nodes[a].Position)
                   + Vec3.DistanceFlat(to, _graph.Nodes[b].Position);
        }

        /// <inheritdoc />
        public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
        {
            next = from;
            var a = _graph.NearestNode(from);
            var b = _graph.NearestNode(to);
            if (a < 0 || b < 0)
            {
                return false;
            }

            var hop = _nextHop[(a * _n) + b];
            if (hop < 0)
            {
                if (a != b)
                {
                    return false;
                }

                next = to;
                return true;
            }

            next = a == b ? to : _graph.Nodes[hop].Position;
            return true;
        }

        /// <inheritdoc />
        public FloorMaterial SampleFloor(Vec3 position) => _inner.SampleFloor(position);

        /// <inheritdoc />
        public int ZoneIdAt(Vec3 position) => _inner.ZoneIdAt(position);

        /// <inheritdoc />
        public Vec3 SnapToNavigable(Vec3 desired) => _inner.SnapToNavigable(desired);

        /// <inheritdoc />
        public bool IsAreaLit(Vec3 position) => _inner.IsAreaLit(position);

        /// <summary>
        /// Re-asks the wrapped probe for every node pair and requires the cache to
        /// agree exactly. Run once at startup.
        /// </summary>
        /// <exception cref="InvalidOperationException">The cache disagrees with Core.</exception>
        public void Verify()
        {
            for (var a = 0; a < _n; a++)
            {
                for (var b = 0; b < _n; b++)
                {
                    var from = _graph.Nodes[a].Position;
                    var to = _graph.Nodes[b].Position;

                    if (_inner.HasLineOfSight(from, to) != HasLineOfSight(from, to))
                    {
                        throw new InvalidOperationException(
                            "CachedWorldProbe disagrees with MapGraphProbe on sight " + a + "→" + b + ".");
                    }

                    var mine = NavigableDistance(from, to);
                    var theirs = _inner.NavigableDistance(from, to);
                    if (mine != theirs)
                    {
                        throw new InvalidOperationException(
                            "CachedWorldProbe disagrees with MapGraphProbe on distance " + a + "→" + b
                            + ": " + mine + " vs " + theirs + ".");
                    }

                    var mineHas = TryGetNextPathPoint(from, to, out var mineNext);
                    var theirsHas = _inner.TryGetNextPathPoint(from, to, out var theirsNext);
                    if (mineHas != theirsHas || (mineHas && mineNext != theirsNext))
                    {
                        throw new InvalidOperationException(
                            "CachedWorldProbe disagrees with MapGraphProbe on the next hop "
                            + a + "→" + b + ".");
                    }
                }
            }
        }
    }
}
