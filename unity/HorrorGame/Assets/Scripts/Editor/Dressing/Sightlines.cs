#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.EditorTools.SceneGen;
using UnityEngine;

namespace HorrorGame.EditorTools.Dressing
{
    /// <summary>
    /// Measures what a player can still see after the dressing goes in.
    /// <para>
    /// §12 splits the map into two kinds of space and makes both mandatory: 개방 공간,
    /// where "멀리서 어그로를 건다" with 15~25 m of sight, and 미로 공간, where the sight
    /// line is broken and the aggro is released. Dressing can only damage the first
    /// one — filling a hall with crates turns the Runner's opening move into a
    /// suicide, and §04's Observer, who needs
    /// <see cref="GameConstants.HallClearSightMin"/> of safe sight, stops having a job.
    /// So the hall's longest clear line is measured after placement and the scatter
    /// fails if it has dropped below that number.
    /// </para>
    /// <para>
    /// Corridor sight lines are reported rather than enforced. Dressing that shortens
    /// them is <em>adding</em> the 시야 차단 지점 §12 wants at 15~25 m spacing, which is
    /// the point of the bulk pass; whether there are now too many is a question for
    /// the 주자 테스트 in <see cref="MapQualityReport"/>, which grades the map on
    /// escape success rate and is the only thing that can answer it.
    /// </para>
    /// </summary>
    public readonly struct Sightlines
    {
        /// <summary>How many cells belong to a §12 개방 공간.</summary>
        public readonly int HallCells;

        /// <summary>Longest clear line measured across the hall at eye height, metres.</summary>
        public readonly float LongestHallLine;

        /// <summary>Shortest of the hall's sampled lines, metres.</summary>
        public readonly float ShortestHallLine;

        /// <summary>Longest clear line measured along a corridor, metres.</summary>
        public readonly float LongestCorridorLine;

        /// <summary>Mean clear line along corridors, metres.</summary>
        public readonly float MeanCorridorLine;

        /// <summary>Corridor samples whose clear line is inside §12's 15~25 m blocker spacing.</summary>
        public readonly int CorridorSamplesInBand;

        /// <summary>Corridor samples taken.</summary>
        public readonly int CorridorSamples;

        private Sightlines(int hallCells, float longestHall, float shortestHall, float longestCorridor,
            float meanCorridor, int inBand, int samples)
        {
            HallCells = hallCells;
            LongestHallLine = longestHall;
            ShortestHallLine = shortestHall;
            LongestCorridorLine = longestCorridor;
            MeanCorridorLine = meanCorridor;
            CorridorSamplesInBand = inBand;
            CorridorSamples = samples;
        }

        /// <summary>
        /// Eye height used for the rays, metres.
        /// <para>
        /// Matches <c>SceneShot</c>'s camera rig and the dressing generator's stated
        /// assumption. §05 never gives a body size, so this is a working figure, not a
        /// design number — and it is the one that decides whether a 1.68 m crate stack
        /// counts as cover.
        /// </para>
        /// </summary>
        private const float EyeHeight = 1.63f;

        /// <summary>Measures the scene as it now stands.</summary>
        public static Sightlines Measure(DressingSpace space)
        {
            Physics.SyncTransforms();

            var hallCells = space.Cells.Where(c => space.Info(c).IsHall).ToArray();
            var longestHall = 0f;
            var shortestHall = float.MaxValue;

            if (hallCells.Length > 0)
            {
                // One hall per building is the §12 sketch's shape, but a stacked
                // building can have one per storey. Measured on the storey of the first
                // hall cell found: a bounding box drawn across two floors would report a
                // sight line through a slab.
                var level = hallCells[0].Level;
                hallCells = hallCells.Where(c => c.Level == level).ToArray();
                var minX = hallCells.Min(c => c.X);
                var maxX = hallCells.Max(c => c.X);
                var minZ = hallCells.Min(c => c.Z);
                var maxZ = hallCells.Max(c => c.Z);
                var floor = space.Info(hallCells[0]).FloorY;

                // One ray per cell row and per cell column, wall to wall. A hall that
                // still has one long line but has lost every other one is not an
                // 개방 공간 any more, so both ends of the distribution are reported.
                for (var z = minZ; z <= maxZ; z++)
                {
                    var zc = new MapCell(minX, z).Centre.Z;
                    var from = new Vector3(new MapCell(minX, z).Min.X + 0.3f, floor + EyeHeight, zc);
                    var to = new Vector3(new MapCell(maxX, z).Min.X + MapKitCatalogue.GridMetres - 0.3f,
                        floor + EyeHeight, zc);
                    Track(from, to, ref longestHall, ref shortestHall);
                }

                for (var x = minX; x <= maxX; x++)
                {
                    var xc = new MapCell(x, minZ).Centre.X;
                    var from = new Vector3(xc, floor + EyeHeight, new MapCell(x, minZ).Min.Z + 0.3f);
                    var to = new Vector3(xc, floor + EyeHeight,
                        new MapCell(x, maxZ).Min.Z + MapKitCatalogue.GridMetres - 0.3f);
                    Track(from, to, ref longestHall, ref shortestHall);
                }
            }

            var corridor = new List<float>();
            foreach (var cell in space.Cells)
            {
                var info = space.Info(cell);
                if (info.IsHall || info.IsDoorway)
                {
                    continue;
                }

                space.WalkableAxes(cell, out var alongX, out var alongZ);
                if (alongX == alongZ)
                {
                    continue;
                }

                var centre = cell.Centre;
                var origin = new Vector3(centre.X, info.FloorY + EyeHeight, centre.Z);
                var direction = alongX ? Vector3.right : Vector3.forward;
                corridor.Add(Clear(origin, direction, GameConstants.MapExtent));
            }

            var inBand = corridor.Count(d => d >= GameConstants.LineOfSightBreakSpacingMin
                                             && d <= GameConstants.LineOfSightBreakSpacingMax);

            return new Sightlines(
                hallCells.Length,
                longestHall,
                shortestHall == float.MaxValue ? 0f : shortestHall,
                corridor.Count > 0 ? corridor.Max() : 0f,
                corridor.Count > 0 ? corridor.Average() : 0f,
                inBand,
                corridor.Count);
        }

        /// <summary>The report block.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            if (HallCells > 0)
            {
                text.Append("§12 개방 공간: ").Append(HallCells).Append(" cells; clear sight at eye height ")
                    .Append(ShortestHallLine.ToString("0.0")).Append("–").Append(LongestHallLine.ToString("0.0"))
                    .Append(" m (needs ≥ ").Append(GameConstants.HallClearSightMin)
                    .Append(" m for §12's 멀리서 어그로).\n");
            }
            else
            {
                text.Append("§12 개방 공간: none found in this map.\n");
            }

            text.Append("Corridor sight lines: ").Append(CorridorSamples).Append(" sampled, mean ")
                .Append(MeanCorridorLine.ToString("0.0")).Append(" m, longest ")
                .Append(LongestCorridorLine.ToString("0.0")).Append(" m; ").Append(CorridorSamplesInBand)
                .Append(" fall inside §12's ").Append(GameConstants.LineOfSightBreakSpacingMin).Append("–")
                .Append(GameConstants.LineOfSightBreakSpacingMax)
                .Append(" m 시야 차단 지점 spacing. Reported, not enforced — whether the map is now too "
                        + "generous is the 주자 테스트's question.");
            return text.ToString();
        }

        private static void Track(Vector3 from, Vector3 to, ref float longest, ref float shortest)
        {
            var span = (to - from).magnitude;
            var clear = Clear(from, (to - from).normalized, span);
            longest = Mathf.Max(longest, clear);
            shortest = Mathf.Min(shortest, clear);
        }

        private static float Clear(Vector3 origin, Vector3 direction, float maximum)
        {
            return Physics.Raycast(origin, direction, out var hit, maximum) ? hit.distance : maximum;
        }
    }
}
