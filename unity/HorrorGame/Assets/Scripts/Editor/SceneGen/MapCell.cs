using System;
using HorrorGame.Core.Math;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// One <see cref="MapKitCatalogue.GridMetres"/> cell of the building, addressed
    /// in whole grid steps from the world origin.
    /// <para>
    /// The whole map is laid out in these rather than in metres. §12's numeric rules
    /// — 직선 통로 20 m, S자 통로 10 m × 2, 구역 대각선 30~40 m — are all multiples of
    /// the kit's 2.5 m grid, so working in cells makes an illegal layout hard to
    /// write by accident and makes every piece dock without a seam. Metres appear
    /// only at the boundary, in <see cref="Centre"/>.
    /// </para>
    /// </summary>
    public readonly struct MapCell : IEquatable<MapCell>
    {
        /// <summary>Builds a cell address.</summary>
        public MapCell(int x, int z)
        {
            X = x;
            Z = z;
        }

        /// <summary>Cell index along world X. Cell 0 spans 0 ~ 2.5 m.</summary>
        public int X { get; }

        /// <summary>Cell index along world Z.</summary>
        public int Z { get; }

        /// <summary>Centre of the cell in metres, at floor level. Corridor centre lines run through these.</summary>
        public Vec3 Centre => new Vec3(
            (X + 0.5f) * MapKitCatalogue.GridMetres,
            0f,
            (Z + 0.5f) * MapKitCatalogue.GridMetres);

        /// <summary>Lowest corner of the cell in metres — where a kit piece's origin goes.</summary>
        public Vec3 Min => new Vec3(
            X * MapKitCatalogue.GridMetres,
            0f,
            Z * MapKitCatalogue.GridMetres);

        /// <summary>The neighbouring cell one step in a direction.</summary>
        public MapCell Step(MapDirection direction, int steps)
        {
            switch (direction)
            {
                case MapDirection.East: return new MapCell(X + steps, Z);
                case MapDirection.North: return new MapCell(X, Z + steps);
                case MapDirection.West: return new MapCell(X - steps, Z);
                case MapDirection.South: return new MapCell(X, Z - steps);
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, "Not a grid direction.");
            }
        }

        /// <summary>The neighbouring cell one step in a direction.</summary>
        public MapCell Step(MapDirection direction) => Step(direction, 1);

        /// <inheritdoc />
        public bool Equals(MapCell other) => X == other.X && Z == other.Z;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is MapCell other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (X * 397) ^ Z;

        /// <inheritdoc />
        public override string ToString() => "(" + X + "," + Z + ")";
    }

    /// <summary>
    /// The four grid directions. Only right angles exist on the map, which is what
    /// makes §12's "20m 넘는 직선 통로가 없다" and its 30° bend threshold decidable
    /// from geometry rather than from a designer's judgement.
    /// </summary>
    public enum MapDirection
    {
        /// <summary>+X.</summary>
        East = 0,

        /// <summary>+Z.</summary>
        North = 1,

        /// <summary>−X.</summary>
        West = 2,

        /// <summary>−Z.</summary>
        South = 3,
    }

    /// <summary>Helpers for <see cref="MapDirection"/>.</summary>
    public static class MapDirections
    {
        /// <summary>All four, in enum order.</summary>
        public static readonly MapDirection[] All =
        {
            MapDirection.East, MapDirection.North, MapDirection.West, MapDirection.South,
        };

        /// <summary>The reverse direction.</summary>
        public static MapDirection Opposite(MapDirection direction) => (MapDirection)(((int)direction + 2) % 4);

        /// <summary>
        /// Yaw in degrees that turns a piece authored facing <see cref="MapDirection.South"/>
        /// so that it faces <paramref name="direction"/>.
        /// <para>
        /// Every MapKit piece is authored with its first dock on the −Y edge, which
        /// imports as Unity −Z. A positive yaw about Y turns +Z toward +X, so the
        /// table below is simply that rotation applied to −Z.
        /// </para>
        /// </summary>
        public static float YawFacing(MapDirection direction)
        {
            switch (direction)
            {
                case MapDirection.South: return 0f;
                case MapDirection.West: return 90f;
                case MapDirection.North: return 180f;
                case MapDirection.East: return 270f;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, "Not a grid direction.");
            }
        }
    }
}
