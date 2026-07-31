#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using HorrorGame.Core.Map;
using HorrorGame.EditorTools.SceneGen;
using UnityEngine;

namespace HorrorGame.EditorTools.Dressing
{
    /// <summary>
    /// The walkable space of a generated map, read back off the scene.
    /// <para>
    /// Deliberately derived from the <em>placed geometry</em> rather than from
    /// <see cref="MapSketchResult"/>. Three reasons, and the third is the real one:
    /// the scatterer runs on whatever scene is open, including one a designer has
    /// nudged by hand; a piece's real world bounds already account for its rotation
    /// and for multi-cell pieces like <c>Corridor_Straight_10m</c>; and a dressing
    /// pass that trusted the sketch could park a crate inside a wall that had been
    /// moved, which is exactly the class of "almost right" failure
    /// <see cref="MapSceneBuilder"/> warns about.
    /// </para>
    /// <para>
    /// Everything is addressed in <see cref="CellKey"/>s — a <see cref="MapCell"/>
    /// plus a storey — so §12's numeric rules, which are all multiples of the kit's
    /// 2.5 m grid, stay expressible while a stacked building stays addressable.
    /// </para>
    /// </summary>
    /// <summary>
    /// A cell address that knows which storey it is on.
    /// <para>
    /// <see cref="MapCell"/> is deliberately two-dimensional — §12's rules are about
    /// plan geometry — and that is fine until the map generator produces a building
    /// with more than one basement level, which it now does (B1, B2, B3). Two cells
    /// with the same X and Z on different storeys are different places, and merging
    /// them makes a corridor on B3 believe it has the walls and the ceiling height of
    /// the corridor above it. The symptom is dressing hung at the wrong height in the
    /// wrong rooms, which looks like a placement bug and is really an addressing one.
    /// </para>
    /// </summary>
    public readonly struct CellKey : IEquatable<CellKey>
    {
        /// <summary>Builds an address.</summary>
        public CellKey(MapCell cell, int level)
        {
            Cell = cell;
            Level = level;
        }

        /// <summary>The plan address.</summary>
        public MapCell Cell { get; }

        /// <summary>Storey index, in whole <see cref="DressingSpace.StoreyMetres"/> steps from y = 0.</summary>
        public int Level { get; }

        /// <summary>Cell index along world X.</summary>
        public int X => Cell.X;

        /// <summary>Cell index along world Z.</summary>
        public int Z => Cell.Z;

        /// <summary>Centre of the cell in metres, at the storey's nominal floor.</summary>
        public HorrorGame.Core.Math.Vec3 Centre => Cell.Centre;

        /// <summary>Lowest corner of the cell in metres.</summary>
        public HorrorGame.Core.Math.Vec3 Min => Cell.Min;

        /// <summary>The neighbouring address one step along, on the same storey.</summary>
        public CellKey Step(MapDirection direction) => new CellKey(Cell.Step(direction), Level);

        /// <inheritdoc />
        public bool Equals(CellKey other) => Cell.Equals(other.Cell) && Level == other.Level;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is CellKey other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (Cell.GetHashCode() * 397) ^ Level;

        /// <inheritdoc />
        public override string ToString() => Cell + "@" + Level;
    }

    /// <summary>
    /// The walkable space of a generated map, read back off the placed geometry.
    /// See the file header for why it is measured rather than taken from the sketch.
    /// </summary>
    public sealed class DressingSpace
    {
        /// <summary>
        /// Height of one storey, metres — <c>storey_metres</c> in the MapKit manifest.
        /// A kit measurement, not a design number; it is only used to decide which
        /// storey a piece of geometry belongs to.
        /// </summary>
        public const float StoreyMetres = 3.75f;

        /// <summary>Half the difference between a cell and the corridor's clear section, metres.</summary>
        public static readonly float WallInset =
            (MapKitCatalogue.GridMetres - MapKitCatalogue.CorridorClearWidth) * 0.5f;

        private readonly Dictionary<CellKey, CellInfo> _cells = new Dictionary<CellKey, CellInfo>();

        private DressingSpace()
        {
        }

        /// <summary>Every walkable cell, in a fixed order so a seed replays exactly.</summary>
        public IReadOnlyList<CellKey> Cells { get; private set; } = Array.Empty<CellKey>();

        /// <summary>The map root the space was read from.</summary>
        public GameObject Root { get; private set; } = null!;

        /// <summary>
        /// Reads the open scene's map into a cell space.
        /// </summary>
        /// <param name="mapRoot">The <c>Map</c> object <see cref="MapSceneBuilder"/> creates.</param>
        /// <param name="error">Why the read failed, when it returns null.</param>
        public static DressingSpace? Read(GameObject mapRoot, out string error)
        {
            var space = new DressingSpace { Root = mapRoot };

            foreach (var zone in ZoneRoots(mapRoot))
            {
                var tiles = zone.transform.Find("Tiles");
                if (tiles == null)
                {
                    continue;
                }

                var palette = PaletteFor(zone.name);
                foreach (Transform tile in tiles)
                {
                    space.AddTile(tile.gameObject, zone.name, palette);
                }
            }

            var shared = mapRoot.transform.Find("Shared");
            if (shared != null)
            {
                foreach (Transform tile in shared)
                {
                    space.AddTile(tile.gameObject, "Shared", "utility");
                }
            }

            if (space._cells.Count == 0)
            {
                error = "No walkable cells found under " + mapRoot.name
                    + ". Expected Zone_*/Tiles and Shared children, as MapSceneBuilder writes them.";
                return null;
            }

            // Ordinal ordering, not hierarchy ordering. Unity's child order is stable in
            // practice and not contractual; a scatter that depended on it would produce
            // a different layout from the same seed after a re-generate, which is the
            // one thing this tool promises not to do.
            space.Cells = space._cells.Keys
                .OrderBy(c => c.Level)
                .ThenBy(c => c.X)
                .ThenBy(c => c.Z)
                .ToArray();

            space.ResolveHeights();
            error = string.Empty;
            return space;
        }

        /// <summary>Whether a cell is part of the walkable building.</summary>
        public bool IsWalkable(CellKey cell) => _cells.ContainsKey(cell);

        /// <summary>Everything known about one walkable cell.</summary>
        public CellInfo Info(CellKey cell) => _cells[cell];

        /// <summary>How many walkable neighbours a cell has. 1 means a dead end (§12's 막힌 길).</summary>
        public int Degree(CellKey cell)
        {
            var n = 0;
            foreach (var direction in MapDirections.All)
            {
                if (IsWalkable(cell.Step(direction)))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Whether the cell edge in this direction is a wall, i.e. nothing walkable beyond it.</summary>
        public bool HasWall(CellKey cell, MapDirection direction) => !IsWalkable(cell.Step(direction));

        /// <summary>
        /// The axes a player can travel along through a cell. §12's clear-channel rule
        /// only has to hold along a direction somebody actually walks.
        /// </summary>
        public void WalkableAxes(CellKey cell, out bool alongX, out bool alongZ)
        {
            alongX = IsWalkable(cell.Step(MapDirection.East)) || IsWalkable(cell.Step(MapDirection.West));
            alongZ = IsWalkable(cell.Step(MapDirection.North)) || IsWalkable(cell.Step(MapDirection.South));
        }

        /// <summary>
        /// The strip of a cell a player may use, on one axis, in world metres.
        /// <para>
        /// Bounded by the corridor's clear section where there is a wall and by the
        /// cell edge where the neighbour is open — so a junction or a hall cell
        /// correctly reports a wider band than a corridor does, without the caller
        /// needing to know which it is looking at.
        /// </para>
        /// </summary>
        public void Band(CellKey cell, bool acrossX, out float min, out float max)
        {
            var lo = acrossX ? cell.Min.X : cell.Min.Z;
            var hi = lo + MapKitCatalogue.GridMetres;
            var negative = acrossX ? MapDirection.West : MapDirection.South;
            var positive = acrossX ? MapDirection.East : MapDirection.North;
            min = lo + (HasWall(cell, negative) ? WallInset : 0f);
            max = hi - (HasWall(cell, positive) ? WallInset : 0f);
        }

        /// <summary>World position of the middle of a cell's wall face, at floor level.</summary>
        public Vector3 WallFace(CellKey cell, MapDirection direction)
        {
            var centre = cell.Centre;
            var normal = Normal(direction);
            var half = (MapKitCatalogue.GridMetres * 0.5f) - WallInset;
            return new Vector3(centre.X, Info(cell).FloorY, centre.Z) + (normal * half);
        }

        /// <summary>Unit vector pointing from a cell centre toward the given edge.</summary>
        public static Vector3 Normal(MapDirection direction)
        {
            switch (direction)
            {
                case MapDirection.East: return Vector3.right;
                case MapDirection.North: return Vector3.forward;
                case MapDirection.West: return Vector3.left;
                default: return Vector3.back;
            }
        }

        /// <summary>The §12 zone palette a floor surface belongs to.</summary>
        public static string PaletteFor(string zoneName)
        {
            // §12's zone table pairs a floor with a character, and the dressing follows
            // the character rather than the colour: 나무 is the old storage wing, 타일 is
            // the institutional hall, 자갈 is the unfinished flooded end — which is where
            // §03's worked clue "그것은 물이 있는 층에 있다" gets somewhere to point — and
            // 콘크리트 is plant and services.
            if (zoneName.EndsWith(FloorMaterial.Wood.ToString(), StringComparison.Ordinal))
            {
                return "storage";
            }

            if (zoneName.EndsWith(FloorMaterial.Tile.ToString(), StringComparison.Ordinal))
            {
                return "institutional";
            }

            if (zoneName.EndsWith(FloorMaterial.Gravel.ToString(), StringComparison.Ordinal))
            {
                return "wet";
            }

            return "utility";
        }

        private static IEnumerable<GameObject> ZoneRoots(GameObject mapRoot)
        {
            foreach (Transform child in mapRoot.transform)
            {
                if (child.name.StartsWith(MapSceneBuilder.ZonePrefix, StringComparison.Ordinal))
                {
                    yield return child.gameObject;
                }
            }
        }

        private void AddTile(GameObject tile, string zoneName, string palette)
        {
            // Floor slabs cover a zone's whole rectangle, walls included, so counting
            // them as walkable would place dressing inside solid geometry.
            if (tile.name.StartsWith("FloorTile", StringComparison.Ordinal)
                || tile.name.StartsWith("FloorBoundary", StringComparison.Ordinal))
            {
                return;
            }

            var renderers = tile.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0)
            {
                return;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            // Which storey the piece sits on. The generated building now stacks B1,
            // B2 and B3 over the same footprint, so X and Z alone are not an address.
            var level = Mathf.RoundToInt(bounds.min.y / StoreyMetres);

            var grid = MapKitCatalogue.GridMetres;
            var x0 = Mathf.FloorToInt(bounds.min.x / grid);
            var x1 = Mathf.CeilToInt(bounds.max.x / grid);
            var z0 = Mathf.FloorToInt(bounds.min.z / grid);
            var z1 = Mathf.CeilToInt(bounds.max.z / grid);

            for (var x = x0; x <= x1; x++)
            {
                for (var z = z0; z <= z1; z++)
                {
                    var cell = new CellKey(new MapCell(x, z), level);
                    var centre = cell.Centre;
                    // Centre-in-bounds rather than any overlap: a piece whose bounds
                    // graze the next cell by a bevel's width does not make that cell
                    // walkable.
                    if (centre.X < bounds.min.x + 0.05f || centre.X > bounds.max.x - 0.05f
                        || centre.Z < bounds.min.z + 0.05f || centre.Z > bounds.max.z - 0.05f)
                    {
                        continue;
                    }

                    if (!_cells.TryGetValue(cell, out var info))
                    {
                        info = new CellInfo(cell, zoneName, palette);
                        _cells[cell] = info;
                    }

                    info.Pieces.Add(tile.name);
                    info.BaseY = Mathf.Min(info.BaseY, bounds.min.y);
                }
            }
        }

        /// <summary>
        /// Measures each cell's floor and ceiling by raycast.
        /// <para>
        /// Measured rather than assumed because the building is not flat: the metal
        /// stairwell §12 gives its own 발소리 row climbs a storey, and a piece hung at a
        /// nominal 3.0 m over a staircase would be inside the treads. The kit's clear
        /// height is only used as the fallback when nothing is overhead.
        /// </para>
        /// </summary>
        private void ResolveHeights()
        {
            Physics.SyncTransforms();

            // Chest height above the piece's own lowest bound. The probe has to start
            // *inside* the corridor: a ray dropped from above the building lands on the
            // roof, and every measurement downstream — where a crate stands, where a
            // bulb hangs, where a sight-line ray is cast — would be one storey out and
            // still look plausible in a report.
            var probeRise = MapKitCatalogue.CorridorClearWidth * 0.4f;

            foreach (var cell in Cells)
            {
                var info = _cells[cell];
                var centre = cell.Centre;

                info.FloorY = info.BaseY;
                var from = new Vector3(centre.X, info.BaseY + probeRise, centre.Z);
                if (Physics.Raycast(from, Vector3.down, out var down, probeRise + 1f))
                {
                    info.FloorY = down.point.y;
                }

                var up = new Vector3(centre.X, info.FloorY + 0.2f, centre.Z);
                info.CeilingY = info.FloorY + CeilingClearFallback;
                if (Physics.Raycast(up, Vector3.up, out var hit, CeilingClearFallback * 2f))
                {
                    info.CeilingY = hit.point.y;
                }
            }
        }

        /// <summary>
        /// Ceiling height used when nothing is overhead, metres.
        /// <para>
        /// The MapKit manifest's <c>corridor_clear.height</c>. It is a kit measurement,
        /// not a design number, and it only applies where the raycast found no ceiling
        /// at all — under the hall's open span, for instance.
        /// </para>
        /// </summary>
        private const float CeilingClearFallback = 3.0f;

        /// <summary>One walkable cell.</summary>
        public sealed class CellInfo
        {
            internal CellInfo(CellKey cell, string zoneName, string palette)
            {
                Cell = cell;
                ZoneName = zoneName;
                Palette = palette;
                BaseY = float.MaxValue;
            }

            /// <summary>Which cell this is.</summary>
            public CellKey Cell { get; }

            /// <summary>The §12 zone that owns it, or "Shared".</summary>
            public string ZoneName { get; }

            /// <summary>The dressing palette the zone's floor implies.</summary>
            public string Palette { get; }

            /// <summary>Names of the kit pieces covering the cell — enough to tell a hall from a corridor.</summary>
            public List<string> Pieces { get; } = new List<string>();

            /// <summary>Lowest renderer bound of the covering pieces, before the raycast refines it.</summary>
            public float BaseY { get; internal set; }

            /// <summary>Measured floor height, metres.</summary>
            public float FloorY { get; internal set; }

            /// <summary>Measured ceiling height, metres.</summary>
            public float CeilingY { get; internal set; }

            /// <summary>Footprints already claimed in this cell, world-space XZ.</summary>
            public List<Rect> Occupied { get; } = new List<Rect>();

            /// <summary>Whether any covering piece is the §12 개방 공간 hall.</summary>
            public bool IsHall => Pieces.Any(p => p.StartsWith("HallOpen", StringComparison.Ordinal));

            /// <summary>Whether any covering piece is a doorway — never dressed, it is a §04 bottleneck.</summary>
            public bool IsDoorway => Pieces.Any(p => p.StartsWith("Doorway", StringComparison.Ordinal)
                                                    || p.StartsWith("DoorPanel", StringComparison.Ordinal));

            /// <summary>Whether any covering piece is the metal stairwell.</summary>
            public bool IsStair => Pieces.Any(p => p.StartsWith("Stairwell", StringComparison.Ordinal));

            /// <summary>Whether any covering piece is an observation post — §04's Observer needs the sightline kept.</summary>
            public bool IsObservationPost =>
                Pieces.Any(p => p.StartsWith("ObservationPost", StringComparison.Ordinal));

            /// <summary>Whether any covering piece is a §12 막힌 길 cap.</summary>
            public bool IsDeadEndCap => Pieces.Any(p => p.StartsWith("DeadEndCap", StringComparison.Ordinal));
        }
    }
}
