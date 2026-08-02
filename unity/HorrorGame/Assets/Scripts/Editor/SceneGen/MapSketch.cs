using System;
using System.Collections.Generic;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>What a generated marker is for, so the scene builder knows what to spawn.</summary>
    public enum MapMarkerKind
    {
        /// <summary>Where the four players come in (§01 진입). Sits next to the 출입구.</summary>
        PlayerSpawn,

        /// <summary>Where the monster starts — the place furthest by walking from the exit (§07 초저녁).</summary>
        MonsterSpawn,

        /// <summary>
        /// A 단서 · 목표물 후보 지점. §12 builds three per zone; §13 says which one is
        /// live exists <em>only</em> on the host, so every candidate is generated
        /// identically and the scene carries no hint of the answer.
        /// </summary>
        CandidateSite,

        /// <summary>A 전리품 drop (§08). Every 막힌 길 gets one — §12's "위험을 감수할 이유".</summary>
        LootSpawn,

        /// <summary>
        /// §12's 잠글 수 있는 문. Sits mid-passage on a 순환로's neck, which is why it is a
        /// marker and not a node: <see cref="Door"/> names a CELL a corridor runs through,
        /// and the graph has no vertex there.
        /// <para>
        /// The map has been placing these since the first sketch and the scene never built
        /// one — the validator measured the detour a shut door would force and the door
        /// itself was never instantiated. This is the marker that ends that.
        /// </para>
        /// </summary>
        LockableDoor,

        /// <summary>A light the Engineer's 구역 조명 switches (§04). Starts off — §03 "어둠 = 목표의 잠금장치".</summary>
        ZoneLight,

        /// <summary>A light that is already on: the way out, so players can find it in the dark.</summary>
        EntranceLight,
    }

    /// <summary>A kit piece placed at a grid cell, with the yaw that docks it correctly.</summary>
    public readonly struct MapTilePlacement
    {
        /// <summary>Builds a placement.</summary>
        public MapTilePlacement(MapKitPiece piece, MapCell origin, float yawDegrees, int zoneId)
        {
            Piece = piece;
            Origin = origin;
            YawDegrees = yawDegrees;
            ZoneId = zoneId;
        }

        /// <summary>Which piece.</summary>
        public MapKitPiece Piece { get; }

        /// <summary>Lowest cell of the footprint after rotation — the scene builder aligns the piece's bounds to it.</summary>
        public MapCell Origin { get; }

        /// <summary>Yaw about Y, degrees.</summary>
        public float YawDegrees { get; }

        /// <summary>Zone the piece belongs to, or −1 when it straddles the boundary.</summary>
        public int ZoneId { get; }
    }

    /// <summary>A prop or marker at a world position — not grid-snapped.</summary>
    public readonly struct MapPropPlacement
    {
        /// <summary>Builds a prop placement.</summary>
        public MapPropPlacement(MapKitPiece piece, Vec3 position, float yawDegrees, int zoneId, string name)
        {
            Piece = piece;
            Position = position;
            YawDegrees = yawDegrees;
            ZoneId = zoneId;
            Name = name;
        }

        /// <summary>Which piece.</summary>
        public MapKitPiece Piece { get; }

        /// <summary>World position, metres.</summary>
        public Vec3 Position { get; }

        /// <summary>Yaw about Y, degrees.</summary>
        public float YawDegrees { get; }

        /// <summary>Owning zone.</summary>
        public int ZoneId { get; }

        /// <summary>Name for the scene object, so a designer can find it.</summary>
        public string Name { get; }
    }

    /// <summary>An empty marker the runtime finds by name — spawns, sites, loot, lights.</summary>
    public readonly struct MapMarkerPlacement
    {
        /// <summary>Builds a marker.</summary>
        public MapMarkerPlacement(MapMarkerKind kind, Vec3 position, int zoneId, int nodeId, string name)
        {
            Kind = kind;
            Position = position;
            ZoneId = zoneId;
            NodeId = nodeId;
            Name = name;
        }

        /// <summary>What it is for.</summary>
        public MapMarkerKind Kind { get; }

        /// <summary>World position, metres.</summary>
        public Vec3 Position { get; }

        /// <summary>Owning zone.</summary>
        public int ZoneId { get; }

        /// <summary>Graph node this marker sits on, or −1.</summary>
        public int NodeId { get; }

        /// <summary>Scene object name.</summary>
        public string Name { get; }
    }

    /// <summary>Everything one run of the generator produced.</summary>
    public sealed class MapSketchResult
    {
        /// <summary>Wraps a generated map.</summary>
        public MapSketchResult(
            int seed,
            MapGraph graph,
            MapTilePlacement[] tiles,
            MapPropPlacement[] props,
            MapMarkerPlacement[] markers,
            MapZoneRect[] zoneRects)
        {
            Seed = seed;
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            Tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
            Props = props ?? throw new ArgumentNullException(nameof(props));
            Markers = markers ?? throw new ArgumentNullException(nameof(markers));
            ZoneRects = zoneRects ?? throw new ArgumentNullException(nameof(zoneRects));
        }

        /// <summary>The seed this map was generated from. The same seed rebuilds it byte for byte.</summary>
        public int Seed { get; }

        /// <summary>The §12 graph — the thing <see cref="MapValidator"/> and <see cref="RunnerTest"/> judge.</summary>
        public MapGraph Graph { get; }

        /// <summary>Grid-snapped kit pieces.</summary>
        public MapTilePlacement[] Tiles { get; }

        /// <summary>Props: doors, panels, floor slabs.</summary>
        public MapPropPlacement[] Props { get; }

        /// <summary>Spawns, sites, loot and lights.</summary>
        public MapMarkerPlacement[] Markers { get; }

        /// <summary>Zone footprints in cells, for floor slabs and grouping.</summary>
        public MapZoneRect[] ZoneRects { get; }
    }

    /// <summary>A zone's footprint in grid cells on one storey, alongside the §12 surface it sounds like.</summary>
    public readonly struct MapZoneRect
    {
        /// <summary>Builds a zone rect.</summary>
        public MapZoneRect(
            int zoneId, string name, FloorMaterial floor, int level, int cellX, int cellZ, int cellsX, int cellsZ)
        {
            ZoneId = zoneId;
            Name = name;
            Floor = floor;
            Level = level;
            CellX = cellX;
            CellZ = cellZ;
            CellsX = cellsX;
            CellsZ = cellsZ;
        }

        /// <summary>
        /// Storey this zone occupies. §03 narrows the objective by floor first, so a
        /// zone belongs to exactly one — a room split across two storeys would make
        /// "지하 3층" unanswerable.
        /// </summary>
        public int Level { get; }

        /// <summary>Index into <see cref="MapGraph.Zones"/>.</summary>
        public int ZoneId { get; }

        /// <summary>§12's label — A/B/C/D.</summary>
        public string Name { get; }

        /// <summary>§12 청음사: the surface underfoot.</summary>
        public FloorMaterial Floor { get; }

        /// <summary>Lowest cell along X.</summary>
        public int CellX { get; }

        /// <summary>Lowest cell along Z.</summary>
        public int CellZ { get; }

        /// <summary>Width in cells.</summary>
        public int CellsX { get; }

        /// <summary>Depth in cells.</summary>
        public int CellsZ { get; }

        /// <summary>Centre in metres, at this storey's floor.</summary>
        public Vec3 Centre => new Vec3(
            (CellX + (CellsX * 0.5f)) * MapKitCatalogue.GridMetres,
            MapKitCatalogue.FloorY(Level),
            (CellZ + (CellsZ * 0.5f)) * MapKitCatalogue.GridMetres);

        /// <summary>
        /// Full extent in metres. Y is one whole storey, so a zone is a box with a
        /// ceiling rather than an infinite column.
        /// <para>
        /// That is what lets one room sit directly under another: <c>MapZone</c>
        /// treats a zero-height zone as covering every floor and therefore as
        /// overlapping anything beneath it, which would fail §12's "재질 경계를 명확히
        /// 할 것" the moment the building gained a second storey. Declaring the height
        /// makes two stacked zones separate volumes, and a footstep still belongs to
        /// exactly one surface.
        /// </para>
        /// </summary>
        public Vec3 Size => new Vec3(
            CellsX * MapKitCatalogue.GridMetres,
            MapKitCatalogue.StoreyMetres,
            CellsZ * MapKitCatalogue.GridMetres);

        /// <summary>True when a cell lies in this zone — same storey and inside the footprint.</summary>
        public bool Contains(MapCell cell) =>
            cell.Level == Level
            && cell.X >= CellX && cell.X < CellX + CellsX
            && cell.Z >= CellZ && cell.Z < CellZ + CellsZ;
    }

    /// <summary>
    /// Thrown when a sketch cannot be turned into a map at all — before
    /// <see cref="MapValidator"/> ever sees it.
    /// </summary>
    public sealed class MapSketchException : Exception
    {
        /// <summary>Builds the exception.</summary>
        public MapSketchException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// A map written as grid cells, turned into a <see cref="MapGraph"/> and a list of
    /// kit pieces.
    /// <para>
    /// The direction of travel matters: cells first, graph second, geometry third.
    /// §12 says "맵은 아트가 아니라 시스템이다", so nothing here places a piece by eye —
    /// a corridor cell with three neighbours <em>is</em> a T junction, a cell with one
    /// is <em>a</em> 막힌 길, and both the graph node and the FBX follow from that one
    /// fact. A designer who edits the cell layout cannot leave the graph and the
    /// geometry disagreeing, which is the failure mode that makes hand-built levels
    /// validate green and play wrong.
    /// </para>
    /// <para>
    /// Nodes are only created where the corridor does something — a bend, a junction,
    /// a dead end. A straight file of cells becomes one edge, because §12's straight
    /// rule measures unbroken sight and an invented mid-corridor node would neither
    /// break sight nor be a place anyone can go.
    /// </para>
    /// </summary>
    public sealed class MapSketch
    {
        private readonly List<MapZoneRect> _zones = new List<MapZoneRect>();
        private readonly HashSet<MapCell> _corridor = new HashSet<MapCell>();
        private readonly List<RoomRect> _rooms = new List<RoomRect>();
        private readonly Dictionary<MapCell, CellMark> _marks = new Dictionary<MapCell, CellMark>();
        private readonly List<MapCell> _doorCells = new List<MapCell>();
        private readonly List<MapPropPlacement> _extraProps = new List<MapPropPlacement>();
        private readonly List<StairRun> _stairs = new List<StairRun>();
        private readonly List<ChuteRun> _chutes = new List<ChuteRun>();
        // Keyed BY STOREY, not by sketch. A plan key names a place inside one floor
        // plan and is consumed by the Mark() calls immediately below it, so scoping it
        // to the whole building only meant that authoring a new storey required reading
        // every other storey's file to find out which characters were still free. Three
        // floors written in parallel each hit that, and one of them collided on 'i'
        // after every author had already gone hunting for punctuation nobody else had
        // taken. A collision WITHIN a storey is still an error, because there it really
        // does silently move whichever §12 requirement was hung on the first one.
        private readonly Dictionary<int, Dictionary<char, MapCell>> _keyed =
            new Dictionary<int, Dictionary<char, MapCell>>();
        private readonly List<RoomRect> _openRooms = new List<RoomRect>();
        private string _name = "unnamed map";
        private MapNodeKind _defaultKind = MapNodeKind.None;
        private int _level;
        private int _offsetX;
        private int _offsetZ;

        /// <summary>Names the map. The name appears in every §12 validator message about it.</summary>
        public MapSketch Named(string name)
        {
            _name = name;
            return this;
        }

        /// <summary>
        /// Sets the storey that following <see cref="Plan"/>, <see cref="Corridor"/>,
        /// <see cref="Room"/>, <see cref="Mark"/> and <see cref="Door"/> calls apply to.
        /// <para>
        /// A cursor rather than a parameter on every call because a floor plan is
        /// authored one storey at a time and repeating the level on every line is where
        /// a typo hides. <see cref="Stair"/> takes its levels explicitly for exactly
        /// the opposite reason: it is the one call that spans two of them, so it must
        /// not depend on which one happens to be current.
        /// </para>
        /// </summary>
        public MapSketch OnLevel(int level)
        {
            _level = level;
            return this;
        }

        /// <summary>
        /// Shifts every cell of following <see cref="Plan"/>, <see cref="Corridor"/>,
        /// <see cref="OpenRoom"/>, <see cref="Room"/>, <see cref="Door"/> and
        /// <see cref="Stair"/> calls by a whole number of cells. Reset it with
        /// <c>Offset(0, 0)</c>.
        /// <para>
        /// <b>Why a whole storey needs to move.</b> A <see cref="Stair"/> is fixed
        /// geometry — the two landings are <c>(x, z)</c> and <c>(x + 1, z)</c> on
        /// adjacent floors — so two storeys can only be joined where their plans
        /// actually overlap. A floor authored on its own has no way to know where the
        /// floor above it ended up, and the building this map is has been descending in
        /// a spiral since B1: each storey sits mostly beside the one above and overlaps
        /// it only near the stairs. Three storeys drawn in parallel by three people
        /// landed in three unrelated corners of the grid, and one of them shared no row
        /// with the floor it was supposed to hang from.
        /// </para>
        /// <para>
        /// The alternative was rewriting each floor plan at its new coordinates, which
        /// means retyping every <see cref="Mark"/> and <see cref="Door"/> cell by hand
        /// and re-verifying a graph that was already measured. This keeps a floor plan a
        /// floor plan: the storey says what it looks like, the building says where it
        /// is. It also makes a storey reusable — the same 병동 can be a different floor
        /// of a different building.
        /// </para>
        /// <para>
        /// Zones are declared by the caller through <see cref="AddZone"/> and are NOT
        /// offset, because the caller already knows both numbers and hiding one of them
        /// in cursor state is how a zone rect and the plan inside it drift apart.
        /// </para>
        /// </summary>
        /// <param name="cellsX">Cells to add to every X.</param>
        /// <param name="cellsZ">Cells to add to every Z.</param>
        public MapSketch Offset(int cellsX, int cellsZ)
        {
            _offsetX = cellsX;
            _offsetZ = cellsZ;
            return this;
        }

        /// <summary>
        /// Kind applied to every node that is not already 개방 공간.
        /// <para>
        /// §12 recognises two kinds of space and puts the whole chase on the boundary
        /// between them: "개방 공간만 있으면 도망칠 곳이 없고, 미로만 있으면 멀리서 어그로를
        /// 걸 수 없다." A corridor is 미로 공간 by construction — it is 2.2 m wide and
        /// bends every few metres — so declaring that per cell would be noise, and
        /// forgetting it on one cell would silently change what
        /// <see cref="RunnerTest"/> counts as cover.
        /// </para>
        /// </summary>
        public MapSketch DefaultKind(MapNodeKind kind)
        {
            _defaultKind = kind;
            return this;
        }

        /// <summary>Zones declared so far.</summary>
        public IReadOnlyList<MapZoneRect> Zones => _zones;

        /// <summary>
        /// Declares a rectangle of the current storey 개방 공간 — one volume you can
        /// see across, rather than a set of passages.
        /// <para>
        /// §12 splits the map into two kinds of space and hangs the entire chase on
        /// the difference: 개방 공간 is where aggro is taken from 15~25 m out, 미로 공간
        /// is where it is broken. <see cref="RunnerTest"/> reads that literally — a
        /// bend at an 개방 공간 node is not counted as cover, "because a corner drawn
        /// inside a room you can see across hides nobody".
        /// </para>
        /// <para>
        /// So this is the most consequential call in a sketch, and it is only honest
        /// when the rectangle is the footprint of a room that is actually built: pass
        /// the same cells as the <see cref="MapKitPiece.HallOpen20x20"/> covering them.
        /// A path drawn through such a room bends because feet do, not because there is
        /// a wall at the bend, and the simulator has to be told which of the two it is
        /// looking at or its verdict stops describing the map that ships.
        /// </para>
        /// </summary>
        public MapSketch OpenRoom(int cellX, int cellZ, int cellsX, int cellsZ)
        {
            _openRooms.Add(new RoomRect(
                MapKitPiece.HallOpen20x20, _offsetX + cellX, _offsetZ + cellZ, cellsX, cellsZ, 0f, _level));
            return this;
        }

        /// <summary>
        /// Declares a zone as a cell rectangle. §12 wants 4~6 of these, each 30~40 m
        /// across the diagonal and each with its own surface.
        /// </summary>
        /// <param name="name">§12's label, quoted verbatim in validator failures.</param>
        /// <param name="floor">§12 청음사: must differ from every other zone's.</param>
        /// <param name="level">Storey. Two zones may share a footprint only when they are on different ones.</param>
        /// <param name="cellX">Lowest cell along X.</param>
        /// <param name="cellZ">Lowest cell along Z.</param>
        /// <param name="cellsX">Width in cells.</param>
        /// <param name="cellsZ">Depth in cells.</param>
        /// <exception cref="MapSketchException">The rect overlaps a zone already declared on the same storey.</exception>
        public int AddZone(
            string name, FloorMaterial floor, int level, int cellX, int cellZ, int cellsX, int cellsZ)
        {
            var rect = new MapZoneRect(_zones.Count, name, floor, level, cellX, cellZ, cellsX, cellsZ);
            for (var i = 0; i < _zones.Count; i++)
            {
                var other = _zones[i];
                if (other.Level != level)
                {
                    // Stacking is the point: §03 asks players to learn which floor a
                    // thing is on, which is only a question worth asking when two
                    // different rooms can share a footprint.
                    continue;
                }

                var overlapX = cellX < other.CellX + other.CellsX && other.CellX < cellX + cellsX;
                var overlapZ = cellZ < other.CellZ + other.CellsZ && other.CellZ < cellZ + cellsZ;
                if (overlapX && overlapZ)
                {
                    throw new MapSketchException(
                        "Zone " + name + " overlaps " + other.Name + " on storey " + level
                        + ". §12 requires \"재질 경계를 명확히 할 것\": a footstep inside an overlap would belong "
                        + "to two surfaces at once and the Listener could not name a zone at all.");
                }
            }

            _zones.Add(rect);
            return rect.ZoneId;
        }

        /// <summary>
        /// Declares a straight file of corridor cells, inclusive at both ends. This is
        /// the only way to add walkable space, so every metre of the map is on the grid.
        /// </summary>
        /// <exception cref="MapSketchException">The run is diagonal.</exception>
        public MapSketch Corridor(int x0, int z0, int x1, int z1)
        {
            if (x0 != x1 && z0 != z1)
            {
                throw new MapSketchException(
                    "Corridor (" + x0 + "," + z0 + ")→(" + x1 + "," + z1 + ") is diagonal. The kit docks on "
                    + "grid edges and §12's bend threshold is decided from geometry, so only right angles exist.");
            }

            var steps = System.Math.Max(System.Math.Abs(x1 - x0), System.Math.Abs(z1 - z0));
            var stepX = System.Math.Sign(x1 - x0);
            var stepZ = System.Math.Sign(z1 - z0);
            for (var i = 0; i <= steps; i++)
            {
                _corridor.Add(new MapCell(
                    _offsetX + x0 + (stepX * i), _offsetZ + z0 + (stepZ * i), _level));
            }

            return this;
        }

        /// <summary>
        /// Reads a storey's floor plan as a picture and turns it into corridor cells.
        /// <para>
        /// The rows are given north first, the way a plan is drawn, so the source
        /// reads as the building rather than as a list of coordinates. That is not a
        /// convenience: §12's failures are all shapes — a 22.5 m straight, a spur one
        /// cell from another spur that silently welds into a room — and a shape is
        /// something a designer can see in a picture and cannot see in forty
        /// <see cref="Corridor"/> calls.
        /// </para>
        /// <para>
        /// A space or '.' is solid. '#' is corridor. Any other character is corridor
        /// <em>and</em> a key, recoverable with <see cref="Key"/>, so that the places
        /// §12 counts — 후보 지점, 관측 지점, 계단, 출입구 — are marked on the drawing
        /// where a reader can check them against it.
        /// </para>
        /// </summary>
        /// <param name="originX">Cell X of the leftmost column.</param>
        /// <param name="originZ">Cell Z of the <em>bottom</em> row, since Z grows north and the rows are drawn top-down.</param>
        /// <param name="rows">One string per cell row, northernmost first.</param>
        /// <exception cref="MapSketchException">A key character is used twice.</exception>
        public MapSketch Plan(int originX, int originZ, params string[] rows)
        {
            if (rows == null || rows.Length == 0)
            {
                throw new MapSketchException("A floor plan with no rows describes no building.");
            }

            for (var r = 0; r < rows.Length; r++)
            {
                var z = originZ + (rows.Length - 1 - r);
                var row = rows[r];
                for (var c = 0; c < row.Length; c++)
                {
                    var symbol = row[c];
                    if (symbol == ' ' || symbol == '.')
                    {
                        continue;
                    }

                    var cell = new MapCell(_offsetX + originX + c, _offsetZ + z, _level);
                    _corridor.Add(cell);
                    if (symbol == '#')
                    {
                        continue;
                    }

                    var keys = KeysOn(_level);
                    if (keys.ContainsKey(symbol))
                    {
                        throw new MapSketchException(
                            "Plan key '" + symbol + "' is used twice on storey " + _level + ", at "
                            + keys[symbol] + " and " + cell + ". A key names one place; reusing it would "
                            + "silently move whichever §12 requirement was hung on the first one. (Keys on "
                            + "other storeys are free — this table is per storey.)");
                    }

                    keys[symbol] = cell;
                }
            }

            return this;
        }

        /// <summary>The cell a plan key was drawn at.</summary>
        /// <exception cref="MapSketchException">No plan used that character.</exception>
        public MapCell Key(char symbol)
        {
            if (!KeysOn(_level).TryGetValue(symbol, out var cell))
            {
                throw new MapSketchException(
                    "No plan drew the key '" + symbol + "'. Every §12 requirement is hung on a key, so a missing "
                    + "one means the rule it carried is not on the map at all. Storey " + _level
                    + " is the one being asked — keys are per storey, so a plan drawn on another "
                    + "floor does not answer here.");
            }

            return cell;
        }

        /// <summary>The key table for one storey, created on first use.</summary>
        private Dictionary<char, MapCell> KeysOn(int level)
        {
            if (!_keyed.TryGetValue(level, out var keys))
            {
                keys = new Dictionary<char, MapCell>();
                _keyed[level] = keys;
            }

            return keys;
        }

        /// <summary>
        /// Declares a rectangle covered by one large piece — the hall or a stairwell.
        /// Cells inside get no corridor tile; the room's own geometry covers them.
        /// </summary>
        public MapSketch Room(MapKitPiece piece, int cellX, int cellZ, int cellsX, int cellsZ, float yawDegrees)
        {
            _rooms.Add(new RoomRect(
                piece, _offsetX + cellX, _offsetZ + cellZ, cellsX, cellsZ, yawDegrees, _level));
            return this;
        }

        /// <summary>
        /// Hangs a 계단 between two storeys and returns nothing but the obligation to
        /// have drawn both landings.
        /// <para>
        /// <see cref="MapKitPiece.StairwellMetal"/> is a 2 × 2-cell switchback with
        /// both of its docks on the same edge, one at floor level and one exactly
        /// <see cref="MapKitCatalogue.StoreyMetres"/> above it. So a stair is fixed
        /// geometry, not a free-form connection: the lower landing is the cell south
        /// of the shaft's west column and the upper landing is the cell south of its
        /// east column. Those two are 2.5 m apart on the plan and one storey apart in
        /// the world.
        /// </para>
        /// <para>
        /// The graph edge this produces is the only way between the two floors, which
        /// is what makes §03's clue chain — 층 → 구역 → 지점 — a real narrowing, and
        /// what gives the Listener the 금속 footstep §12 reserves for stairs. The
        /// shaft's spine wall means both landings are 90° turns, so a stair always
        /// breaks a sight line.
        /// </para>
        /// </summary>
        /// <param name="x">Cell X of the shaft's west column. The lower landing is (x, z), the upper (x + 1, z).</param>
        /// <param name="z">Cell Z of the landings — the shaft itself occupies z + 1 and z + 2.</param>
        /// <param name="upperLevel">Storey the upper landing is on. The lower landing is one below it.</param>
        /// <param name="name">Label for the graph edge, so a report can say which stair.</param>
        public MapSketch Stair(int x, int z, int upperLevel, string name)
        {
            var sx = _offsetX + x;
            var sz = _offsetZ + z;
            _stairs.Add(new StairRun(
                new MapCell(sx, sz, upperLevel + 1),
                new MapCell(sx + 1, sz, upperLevel),
                sx,
                sz + 1,
                upperLevel + 1,
                name));
            return this;
        }

        /// <summary>
        /// Hangs a 투하구 — §01's one-way drop from the middle of a storey to the rim of
        /// the one below.
        /// <para>
        /// <b>Not a stair, and the difference is the game.</b> A <see cref="Stair"/> joins
        /// two landings 2.5 m apart in plan and can be climbed both ways. A chute drops you
        /// across the whole floor and cannot be climbed at all: you arrive on the OUTER ring
        /// of the storey below and solve that maze from the beginning. That is what makes
        /// eight storeys eight mazes instead of one maze and seven staircases.
        /// </para>
        /// <para>
        /// One way matters twice over. It means a player cannot retreat upward from the
        /// creature, so the only direction is the one the race is in; and it means the graph
        /// has no route back, so <see cref="MapValidator"/>'s reachability is measured the
        /// way the match is actually played.
        /// </para>
        /// </summary>
        /// <param name="x">Cell X of the mouth, on the upper storey.</param>
        /// <param name="z">Cell Z of the mouth.</param>
        /// <param name="upperLevel">Storey the mouth is on. The landing is one below.</param>
        /// <param name="landingX">Cell X of the landing, on the storey below.</param>
        /// <param name="landingZ">Cell Z of the landing.</param>
        /// <param name="name">Label for the graph edge, so a report can say which chute.</param>
        public MapSketch Chute(int x, int z, int upperLevel, int landingX, int landingZ, string name)
        {
            _chutes.Add(new ChuteRun(
                new MapCell(_offsetX + x, _offsetZ + z, upperLevel),
                new MapCell(_offsetX + landingX, _offsetZ + landingZ, upperLevel + 1),
                name));
            return this;
        }

        /// <summary>
        /// Marks what a place is for. The flags are what <see cref="MapValidator"/>
        /// counts per zone, so a forgotten mark is a failing rule rather than a role
        /// with nowhere to work.
        /// </summary>
        /// <exception cref="MapSketchException">The cell is not corridor.</exception>
        public MapSketch Mark(char key, MapNodeKind kind, string name) =>
            Mark(Key(key), kind, name);

        /// <summary>Marks what a place is for, by cell.</summary>
        /// <exception cref="MapSketchException">The cell is not corridor.</exception>
        public MapSketch Mark(MapCell cell, MapNodeKind kind, string name)
        {
            if (!_corridor.Contains(cell))
            {
                throw new MapSketchException(
                    "Cannot mark " + cell + " as " + kind + ": no corridor there. A mark on empty space would "
                    + "make §12's per-zone counts describe a place nobody can stand in.");
            }

            if (_marks.TryGetValue(cell, out var existing))
            {
                _marks[cell] = new CellMark(existing.Kind | kind, string.IsNullOrEmpty(existing.Name) ? name : existing.Name);
            }
            else
            {
                _marks[cell] = new CellMark(kind, name);
            }

            return this;
        }

        /// <summary>
        /// Hangs a lockable door in the passage running through a cell. §12 정비공:
        /// 1~2 per zone and only at a 병목, both of which the validator checks —
        /// this only says where the leaf goes.
        /// </summary>
        public MapSketch Door(int x, int z)
        {
            _doorCells.Add(new MapCell(_offsetX + x, _offsetZ + z, _level));
            return this;
        }

        /// <summary>Places a prop that is not implied by the topology — a wall panel, a cap.</summary>
        public MapSketch Prop(MapKitPiece piece, Vec3 position, float yawDegrees, int zoneId, string name)
        {
            _extraProps.Add(new MapPropPlacement(piece, position, yawDegrees, zoneId, name));
            return this;
        }

        /// <summary>
        /// Turns the sketch into a graph and a pile of pieces.
        /// <para>
        /// <paramref name="random"/> decides only what §12 leaves free: which 전리품
        /// sits in each 막힌 길, and which places carry loot beyond the required ones.
        /// The topology is fixed, because a seed that could move a corridor could move
        /// the map out of §12 compliance, and a generator that sometimes emits an
        /// illegal map is a generator nobody can trust a seed from.
        /// </para>
        /// </summary>
        /// <exception cref="MapSketchException">The cells cannot form a map.</exception>
        public MapSketchResult Build(int seed, IRandomSource random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (_zones.Count == 0)
            {
                throw new MapSketchException("A map with no zones has nothing for §12 to count.");
            }

            VerifyStairs();
            VerifyRoomWalls();
            var zoneOf = ResolveZones();
            var nodeCells = FindNodeCells();

            // A landing sits at the end of a corridor, which on its own storey looks
            // like a straight run continuing or an end to be capped. It has to be a
            // node either way, because the stair is an edge and an edge needs both of
            // its ends to exist.
            for (var i = 0; i < _stairs.Count; i++)
            {
                nodeCells.Add(_stairs[i].Lower);
                nodeCells.Add(_stairs[i].Upper);
            }

            for (var i = 0; i < _chutes.Count; i++)
            {
                nodeCells.Add(_chutes[i].Mouth);
                nodeCells.Add(_chutes[i].Landing);
            }

            // A mark on a cell the corridor runs straight through would vanish: nodes
            // exist only where the passage does something, so the flag would never
            // reach the graph and §12's per-zone count would silently be one short.
            foreach (var pair in _marks)
            {
                if (!nodeCells.Contains(pair.Key))
                {
                    throw new MapSketchException(
                        "Cell " + pair.Key + " is marked " + pair.Value.Kind + " but the corridor runs straight "
                        + "through it, so it is not a place — it is the middle of a passage. Move the mark to a "
                        + "bend, a junction or an end, or the §12 count for this zone will be short by one "
                        + "without anything failing.");
                }
            }

            var builder = new MapGraphBuilder().Named(_name);
            for (var i = 0; i < _zones.Count; i++)
            {
                builder.AddZone(_zones[i].Name, _zones[i].Floor, _zones[i].Centre, _zones[i].Size);
            }

            var nodeIdOf = new Dictionary<MapCell, int>();
            var orderedNodeCells = new List<MapCell>(nodeCells);
            orderedNodeCells.Sort(CompareCells);
            var rewards = new DeadEndRewards(random);

            foreach (var cell in orderedNodeCells)
            {
                _marks.TryGetValue(cell, out var mark);
                var name = string.IsNullOrEmpty(mark.Name) ? _zones[zoneOf[cell]].Name + cell.ToString() : mark.Name;
                nodeIdOf[cell] = builder.AddNode(zoneOf[cell], cell.Centre, KindOf(mark, cell), name, 0);
            }

            var edgeCells = new List<List<MapCell>>();
            var edgeEnds = new List<MapCell[]>();
            BuildEdges(nodeCells, nodeIdOf, builder, edgeCells, edgeEnds);
            for (var i = 0; i < _stairs.Count; i++)
            {
                builder.Connect(nodeIdOf[_stairs[i].Lower], nodeIdOf[_stairs[i].Upper]);
            }

            for (var i = 0; i < _chutes.Count; i++)
            {
                builder.Connect(nodeIdOf[_chutes[i].Mouth], nodeIdOf[_chutes[i].Landing]);
            }

            // Rewards can only be assigned once the degrees are known, so the graph is
            // built twice: once to learn the topology, once with the 막힌 길 보상 §12
            // requires on every leaf. Cheap, and it keeps IsDeadEnd the single source
            // of truth about what a dead end is.
            var probe = builder.Build();
            var finalBuilder = new MapGraphBuilder().Named(_name);
            for (var i = 0; i < _zones.Count; i++)
            {
                finalBuilder.AddZone(_zones[i].Name, _zones[i].Floor, _zones[i].Centre, _zones[i].Size);
            }

            var lootAt = new Dictionary<MapCell, int>();
            foreach (var cell in orderedNodeCells)
            {
                _marks.TryGetValue(cell, out var mark);
                var id = nodeIdOf[cell];
                var reward = probe.IsDeadEnd(id) ? rewards.Next() : 0;
                if (reward > 0)
                {
                    lootAt[cell] = reward;
                }

                var name = string.IsNullOrEmpty(mark.Name) ? _zones[zoneOf[cell]].Name + cell.ToString() : mark.Name;
                finalBuilder.AddNode(zoneOf[cell], cell.Centre, KindOf(mark, cell), name, reward);
            }

            var doorEdgeCells = new HashSet<MapCell>(_doorCells);
            var unusedDoors = new HashSet<MapCell>(_doorCells);
            for (var i = 0; i < edgeEnds.Count; i++)
            {
                var hasDoor = false;
                MapCell doorCell = default;
                foreach (var cell in edgeCells[i])
                {
                    if (doorEdgeCells.Contains(cell))
                    {
                        hasDoor = true;
                        doorCell = cell;
                        unusedDoors.Remove(cell);
                        break;
                    }
                }

                var a = nodeIdOf[edgeEnds[i][0]];
                var b = nodeIdOf[edgeEnds[i][1]];
                finalBuilder.Connect(a, b, hasDoor, hasDoor ? "door " + doorCell : null);
            }

            if (unusedDoors.Count > 0)
            {

                foreach (var cell in unusedDoors)
                {
                    throw new MapSketchException(
                        "A door was asked for at " + cell + ", but no passage runs through that cell — it is a "
                        + "junction, a bend or an end. §12 hangs a door \"순환로의 목에\", which is a passage, and "
                        + "the validator measures the detour of an edge; a door on a junction has no edge to shut.");
                }
            }

            for (var i = 0; i < _stairs.Count; i++)
            {
                finalBuilder.Connect(
                    nodeIdOf[_stairs[i].Lower], nodeIdOf[_stairs[i].Upper], false, _stairs[i].Name);
            }

            for (var i = 0; i < _chutes.Count; i++)
            {
                finalBuilder.Connect(
                    nodeIdOf[_chutes[i].Mouth], nodeIdOf[_chutes[i].Landing], false, _chutes[i].Name);
            }

            var graph = finalBuilder.Build();
            var tiles = BuildTiles(zoneOf, doorEdgeCells);
            var props = BuildProps(zoneOf, doorEdgeCells);
            var markers = BuildMarkers(graph, nodeIdOf, zoneOf, lootAt);
            return new MapSketchResult(seed, graph, tiles, props, markers, _zones.ToArray());
        }

        private MapNodeKind KindOf(CellMark mark, MapCell cell)
        {
            var kind = mark.Kind;
            for (var i = 0; i < _openRooms.Count; i++)
            {
                var room = _openRooms[i];
                if (room.Level == cell.Level
                    && cell.X >= room.CellX && cell.X < room.CellX + room.CellsX
                    && cell.Z >= room.CellZ && cell.Z < room.CellZ + room.CellsZ)
                {
                    return kind | MapNodeKind.OpenSpace;
                }
            }

            return (kind & MapNodeKind.OpenSpace) != 0 ? kind : kind | _defaultKind;
        }

        private static int CompareCells(MapCell a, MapCell b)
        {
            if (a.Level != b.Level)
            {
                return a.Level.CompareTo(b.Level);
            }

            return a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X);
        }

        private Dictionary<MapCell, int> ResolveZones()
        {
            var zoneOf = new Dictionary<MapCell, int>();
            var orphans = new List<MapCell>();
            foreach (var cell in _corridor)
            {
                var found = -1;
                for (var i = 0; i < _zones.Count; i++)
                {
                    if (_zones[i].Contains(cell))
                    {
                        found = i;
                        break;
                    }
                }

                if (found < 0)
                {
                    orphans.Add(cell);
                }
                else
                {
                    zoneOf[cell] = found;
                }
            }

            if (orphans.Count > 0)
            {
                orphans.Sort(CompareCells);
                var text = new StringBuilder();
                for (var i = 0; i < orphans.Count && i < 12; i++)
                {
                    if (i > 0)
                    {
                        text.Append(", ");
                    }

                    text.Append(orphans[i]);
                }

                throw new MapSketchException(
                    orphans.Count + " corridor cell(s) lie outside every zone: " + text
                    + ". §12 counts floor material, 관측 지점, 문 and 후보 지점 per zone, so a place in no zone "
                    + "is a place none of those rules can see — and the Listener would hear "
                    + FloorMaterial.None + " under it.");
            }

            return zoneOf;
        }

        /// <summary>
        /// Checks every 계단 against the shaft geometry before anything is built.
        /// <para>
        /// A stair that lands on empty space, or whose shaft runs through a corridor,
        /// produces a graph that validates and a building whose floors do not meet.
        /// That is the worst failure this generator can have, so it is caught here
        /// rather than discovered in a screenshot.
        /// </para>
        /// </summary>
        private void VerifyStairs()
        {
            for (var i = 0; i < _stairs.Count; i++)
            {
                var stair = _stairs[i];
                RequireLanding(stair, stair.Lower, "lower");
                RequireLanding(stair, stair.Upper, "upper");

                // The shaft is 7.05 m tall: it stands on the lower floor and its top
                // flight passes through the upper storey's slab. Both storeys therefore
                // have to be clear of corridor where it stands.
                for (var dx = 0; dx < 2; dx++)
                {
                    for (var dz = 0; dz < 2; dz++)
                    {
                        for (var level = stair.ShaftLevel - 1; level <= stair.ShaftLevel; level++)
                        {
                            var occupied = new MapCell(stair.ShaftX + dx, stair.ShaftZ + dz, level);
                            if (_corridor.Contains(occupied))
                            {
                                throw new MapSketchException(
                                    "Stair " + stair.Name + " has its shaft at " + occupied
                                    + ", where a corridor already runs. The shaft is "
                                    + MapKitCatalogue.GridMetres * 2f + " m square and reaches a whole storey up, "
                                    + "so a corridor sharing that footprint would be a passage cut through the "
                                    + "flights.");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks that no passage crosses a room's wall anywhere but a doorway.
        /// <para>
        /// A kit room is a closed box with openings at fixed offsets. The graph does
        /// not know that: two adjacent corridor cells are an edge whether or not there
        /// is a wall between them, so a layout that runs a corridor into the side of a
        /// hall produces a map that validates against all sixteen §12 rules, bakes a
        /// NavMesh, and cannot be walked. That is the worst failure this generator can
        /// have — everything downstream keeps agreeing with the graph, and only a
        /// player finds out.
        /// </para>
        /// <para>
        /// The doorway offsets come from the kit manifest: a hall docks 6.25 m and
        /// 13.75 m along each edge, which is the centre of the third and the sixth
        /// cell. So this is not a rule invented here; it is the geometry, asserted.
        /// </para>
        /// </summary>
        private void VerifyRoomWalls()
        {
            for (var i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room.Piece == MapKitPiece.StairwellMetal)
                {
                    continue;
                }

                var doors = new HashSet<MapCell>();
                foreach (var dock in DockCells(room))
                {
                    doors.Add(dock.Cell);
                }

                foreach (var cell in _corridor)
                {
                    if (cell.Level != room.Level || InsideRoom(cell) || doors.Contains(cell))
                    {
                        continue;
                    }

                    foreach (var direction in MapDirections.All)
                    {
                        var neighbour = cell.Step(direction);
                        if (!_corridor.Contains(neighbour) || !InRoom(room, neighbour))
                        {
                            continue;
                        }

                        throw new MapSketchException(
                            "The passage from " + cell + " into " + neighbour + " crosses the wall of the "
                            + room.Piece + " at (" + room.CellX + "," + room.CellZ + "). That room only opens at "
                            + Describe(doors) + ", so the graph would carry an edge through a solid wall: §12's "
                            + "checklist would still pass, the NavMesh would still bake, and the map would be "
                            + "unwalkable. Move the passage onto a doorway or move the room.");
                    }
                }
            }
        }

        private static bool InRoom(RoomRect room, MapCell cell) =>
            cell.Level == room.Level
            && cell.X >= room.CellX && cell.X < room.CellX + room.CellsX
            && cell.Z >= room.CellZ && cell.Z < room.CellZ + room.CellsZ;

        private static string Describe(HashSet<MapCell> cells)
        {
            var ordered = new List<MapCell>(cells);
            ordered.Sort(CompareCells);
            var text = new StringBuilder();
            for (var i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(ordered[i]);
            }

            return text.ToString();
        }

        private void RequireLanding(StairRun stair, MapCell landing, string which)
        {
            if (!_corridor.Contains(landing))
            {
                throw new MapSketchException(
                    "Stair " + stair.Name + " has no corridor at its " + which + " landing " + landing
                    + ". §12 gives 계단 their own 금속 surface because the Listener has to hear a floor change; "
                    + "a stair nobody can step off is not a floor change, it is a hole.");
            }
        }

        private HashSet<MapCell> FindNodeCells()
        {
            var landings = new HashSet<MapCell>();
            for (var i = 0; i < _stairs.Count; i++)
            {
                landings.Add(_stairs[i].Lower);
                landings.Add(_stairs[i].Upper);
            }

            var nodes = new HashSet<MapCell>();
            foreach (var cell in _corridor)
            {
                var count = 0;
                var mask = 0;
                foreach (var dir in MapDirections.All)
                {
                    if (_corridor.Contains(cell.Step(dir)))
                    {
                        count++;
                        mask |= 1 << (int)dir;
                    }
                }

                if (count == 0 && !landings.Contains(cell))
                {
                    throw new MapSketchException(
                        "Corridor cell " + cell + " touches nothing. §12's connectivity rule treats the map as "
                        + "one building; an island is a second map nobody can walk to.");
                }

                var straightThrough = count == 2
                    && ((mask == 0b0101) || (mask == 0b1010));
                if (!straightThrough)
                {
                    nodes.Add(cell);
                }
            }

            if (nodes.Count == 0)
            {
                throw new MapSketchException(
                    "The corridor has no bends, junctions or ends, so there is nowhere to put a node. "
                    + "A ring with no corner cannot exist on a grid — this means the sketch is empty.");
            }

            return nodes;
        }

        private void BuildEdges(
            HashSet<MapCell> nodeCells,
            Dictionary<MapCell, int> nodeIdOf,
            MapGraphBuilder builder,
            List<List<MapCell>> edgeCells,
            List<MapCell[]> edgeEnds)
        {
            var walked = new HashSet<HalfEdge>();
            var ordered = new List<MapCell>(nodeCells);
            ordered.Sort(CompareCells);

            foreach (var start in ordered)
            {
                foreach (var dir in MapDirections.All)
                {
                    if (!_corridor.Contains(start.Step(dir)) || walked.Contains(new HalfEdge(start, dir)))
                    {
                        continue;
                    }

                    var body = new List<MapCell>();
                    var at = start.Step(dir);
                    var heading = dir;
                    var guard = 0;
                    while (!nodeCells.Contains(at))
                    {
                        body.Add(at);
                        at = at.Step(heading);
                        if (++guard > 4096)
                        {
                            throw new MapSketchException("Runaway corridor walk from " + start + " heading " + dir + ".");
                        }
                    }

                    if (at.Equals(start))
                    {
                        throw new MapSketchException(
                            "The passage leaving " + start + " to the " + dir + " returns to the same place without "
                            + "passing a junction. §12's 순환로 needs two distinct ways round, and a self-loop is "
                            + "neither a loop nor a corridor.");
                    }

                    walked.Add(new HalfEdge(start, dir));
                    walked.Add(new HalfEdge(at, MapDirections.Opposite(heading)));
                    edgeCells.Add(body);
                    edgeEnds.Add(new[] { start, at });
                    builder.Connect(nodeIdOf[start], nodeIdOf[at]);
                }
            }
        }

        /// <summary>
        /// Whether a room is taller than the storey it stands on and has somebody
        /// walking over it.
        /// <para>
        /// §12's 개방 공간 is a 20 m room with a 6.3 m ceiling; a storey is
        /// <see cref="MapKitCatalogue.StoreyMetres"/> = 3.75 m. A hall on B2 therefore
        /// pushes 2.55 m of itself up into B1, and the corridor that runs over it is
        /// left with 1.99 m of headroom against a 2.00 m agent. Measured on this map:
        /// six places on B1 — including both 계단 landings on the 하역장 side and the
        /// whole of 기록보관소's southern cross-passage — had no navigable surface at
        /// all, which cut B1 in half and left 기계실 an island of its own.
        /// </para>
        /// <para>
        /// Nothing else could see it. §12's checklist is horizontal and per-storey, the
        /// graph says the corridor is there, and the scene looks right from every angle
        /// because the intruding roof is above head height in the screenshot. So the
        /// room is dropped and the conflict is stated: a 개방 공간 needs two storeys of
        /// clearance, and the layout has to give it one — either by moving the hall out
        /// from under the corridor or by moving the corridor.
        /// </para>
        /// </summary>
        private bool IntrudesOnStoreyAbove(RoomRect room)
        {
            // A 계단 is the one piece whose job is to occupy two storeys.
            if (room.Piece == MapKitPiece.StairwellMetal
                || MapKitCatalogue.HeightMetres(room.Piece) <= MapKitCatalogue.StoreyMetres)
            {
                return false;
            }

            var above = new List<MapCell>();
            for (var x = room.CellX; x < room.CellX + room.CellsX; x++)
            {
                for (var z = room.CellZ; z < room.CellZ + room.CellsZ; z++)
                {
                    var over = new MapCell(x, z, room.Level - 1);
                    if (_corridor.Contains(over))
                    {
                        above.Add(over);
                    }
                }
            }

            if (above.Count == 0)
            {
                return false;
            }

            UnityEngine.Debug.LogError(
                "[SceneGen] " + room.Piece + " at (" + room.CellX + "," + room.CellZ + "@L" + room.Level
                + ") is " + MapKitCatalogue.HeightMetres(room.Piece) + " m tall on a "
                + MapKitCatalogue.StoreyMetres + " m storey, so its roof rises into the storey above and leaves "
                + above.Count + " place(s) up there under 2 m of headroom — "
                + string.Join(", ", above.GetRange(0, System.Math.Min(6, above.Count)))
                + ". §06's monster is 2.30 m; it cannot path through any of them, so the room is not built. "
                + "Move the 개방 공간 out from under the corridor, or move the corridor.");
            return true;
        }

        /// <summary>
        /// Every cell that has to open onto a 계단, and which way the shaft is.
        /// <para>
        /// A landing's own storey cannot see the stair at all: <see cref="MapCell.Step"/>
        /// never leaves a storey and the shaft's cells are not corridor, so
        /// <see cref="NeighbourMask"/> counts a landing as having one fewer passage than
        /// it really has. The tiler then picks a piece for that lower count and puts a
        /// wall exactly where the flight arrives — a straight where a T belongs, a
        /// corner where a straight belongs — and the storeys stop being connected.
        /// </para>
        /// <para>
        /// Nothing reports it. §12's checklist reads the graph, and the graph has the
        /// stair edge because <see cref="Stair"/> adds it explicitly; the geometry
        /// disagrees silently, which is B-001's signature and the reason each floor was
        /// its own island. §06's monster paths on the surface, not on the graph.
        /// </para>
        /// <para>
        /// Always north: <see cref="MapKitPiece.StairwellMetal"/> carries both docks on
        /// one edge and the generator places every shaft at yaw 0, so the landings are
        /// always the cells south of it.
        /// </para>
        /// </summary>
        private Dictionary<MapCell, MapDirection> StairMouths()
        {
            var mouths = new Dictionary<MapCell, MapDirection>();

            foreach (var stair in _stairs)
            {
                mouths[stair.Lower] = MapDirection.North;
                mouths[stair.Upper] = MapDirection.North;
            }

            // A 계단 dropped in as a Room rather than through Stair — the 출입구 shaft,
            // whose upper flight leaves the building and so has no landing to declare.
            foreach (var room in _rooms)
            {
                if (room.Piece != MapKitPiece.StairwellMetal)
                {
                    continue;
                }

                var lower = new MapCell(room.CellX, room.CellZ - 1, room.Level);
                var upper = new MapCell(room.CellX + 1, room.CellZ - 1, room.Level - 1);
                if (_corridor.Contains(lower))
                {
                    mouths[lower] = MapDirection.North;
                }

                if (_corridor.Contains(upper))
                {
                    mouths[upper] = MapDirection.North;
                }
            }

            return mouths;
        }

        /// <summary>
        /// <see cref="NeighbourMask"/> plus the 계단 a landing opens onto, which is the
        /// mask the tiler has to choose a piece from.
        /// </summary>
        private int MaskWithStair(MapCell cell, Dictionary<MapCell, MapDirection> mouths) =>
            mouths.TryGetValue(cell, out var toShaft)
                ? NeighbourMask(cell) | (1 << (int)toShaft)
                : NeighbourMask(cell);

        private MapTilePlacement[] BuildTiles(Dictionary<MapCell, int> zoneOf, HashSet<MapCell> doorCells)
        {
            var tiles = new List<MapTilePlacement>();
            var consumed = new HashSet<MapCell>();

            var landings = StairMouths();
            foreach (var stair in _stairs)
            {
                // The shaft is authored with both docks on its −Y edge, so yaw 0 opens
                // it south onto the two landings the sketch already checked.
                tiles.Add(new MapTilePlacement(
                    MapKitPiece.StairwellMetal,
                    new MapCell(stair.ShaftX, stair.ShaftZ, stair.ShaftLevel),
                    0f,
                    zoneOf.TryGetValue(stair.Lower, out var stairZone) ? stairZone : -1));
            }

            foreach (var room in _rooms)
            {
                if (IntrudesOnStoreyAbove(room))
                {
                    continue;
                }

                tiles.Add(new MapTilePlacement(
                    room.Piece,
                    new MapCell(room.CellX, room.CellZ, room.Level),
                    room.YawDegrees,
                    ZoneOfRoom(room, zoneOf)));

                for (var x = room.CellX; x < room.CellX + room.CellsX; x++)
                {
                    for (var z = room.CellZ; z < room.CellZ + room.CellsZ; z++)
                    {
                        consumed.Add(new MapCell(x, z, room.Level));
                    }
                }

                PlugUnusedDocks(room, zoneOf, tiles, consumed);
            }

            var ordered = new List<MapCell>(_corridor);
            ordered.Sort(CompareCells);

            // Dead-end caps first: the cap is 2 cells long, so it has to claim its
            // outward cell before the straight tiler does.
            foreach (var cell in ordered)
            {
                if (consumed.Contains(cell))
                {
                    continue;
                }

                var mask = MaskWithStair(cell, landings);

                // A landing has one corridor neighbour and looks exactly like a 막힌 길
                // from the grid's point of view. Capping it would wall the stair off,
                // and the map would come apart into one piece per storey.
                if (CountBits(mask) != 1 || doorCells.Contains(cell) || landings.ContainsKey(cell))
                {
                    continue;
                }

                var inward = FirstDirection(mask);

                // §12 관측자 asks for somewhere the monster can be watched from
                // ObserverRange without being reachable. A leaf is exactly that shape,
                // so an alcove marked 관측 지점 gets the barred window rather than the
                // 막힌 길 cap — same topology, and the bars are what make standing
                // there survivable.
                _marks.TryGetValue(cell, out var mark);
                if ((mark.Kind & MapNodeKind.ObservationPost) != 0)
                {
                    tiles.Add(new MapTilePlacement(
                        MapKitPiece.ObservationPostBarredWindow, cell,
                        MapDirections.YawFacing(inward), zoneOf[cell]));
                    consumed.Add(cell);
                    continue;
                }

                var outward = MapDirections.Opposite(inward);
                var beyond = cell.Step(outward);
                if (_corridor.Contains(beyond) || consumed.Contains(beyond) || InsideRoom(beyond))
                {
                    continue;
                }

                // The cap is authored with its dock on −Y and its body running +Y, so
                // it faces the corridor it closes off.
                tiles.Add(new MapTilePlacement(
                    MapKitPiece.DeadEndCap, MinCell(cell, beyond), MapDirections.YawFacing(inward), zoneOf[cell]));
                consumed.Add(cell);
                consumed.Add(beyond);
            }

            foreach (var cell in ordered)
            {
                if (consumed.Contains(cell))
                {
                    continue;
                }

                var mask = MaskWithStair(cell, landings);
                var count = CountBits(mask);

                // §12 정비공's door is the graph edge and the leaf, not the frame.
                //
                // MapKitPiece.DoorwayFrame bakes as a wall. Measured on the five-storey
                // map: with the frame at all five 문 cells the surface came apart into
                // 3 islands and 51~76% of marker pairs, and with a plain corridor piece
                // in its place — everything else identical, same graph, same
                // MapValidator PASS — the same scene bakes 1830/1830 pairs, 1 island,
                // monster reach 19/19. The frame's opening does not survive Recast's
                // erosion at agentRadius 0.5 m, so §06's monster reads it as a
                // partition. The old three-storey map hid this because all five of its
                // doors sat on well-connected 순환로 necks, where a sealed cell costs a
                // detour rather than a component.
                //
                // So the passage stays open geometry and the door is expressed by the
                // DoorPanelLockable leaf BuildProps hangs at the same cell (which is
                // already out of the bake, as a locked door has to be — §04 locks it at
                // runtime, not at bake time). Restore the frame here when
                // build_doorway_frame in tools/blender/gen_mapkit.py leaves a clear
                // opening wider than 2 × agentRadius.
                if (doorCells.Contains(cell))
                {
                    var axis = (mask & 0b0101) != 0 ? MapDirection.East : MapDirection.North;
                    tiles.Add(new MapTilePlacement(
                        MapKitPiece.CorridorStraight2m5, cell, axis == MapDirection.North ? 0f : 90f, zoneOf[cell]));
                    consumed.Add(cell);
                    continue;
                }

                if (count == 4)
                {
                    tiles.Add(new MapTilePlacement(MapKitPiece.JunctionCross4Way, cell, 0f, zoneOf[cell]));
                    consumed.Add(cell);
                    continue;
                }

                if (count == 3)
                {
                    // Authored with docks S, N and E, so the yaw is decided by the side
                    // that has no passage.
                    var missing = MissingDirection(mask);
                    var yaw = missing == MapDirection.West ? 0f
                        : missing == MapDirection.North ? 90f
                        : missing == MapDirection.East ? 180f
                        : 270f;
                    tiles.Add(new MapTilePlacement(MapKitPiece.JunctionT, cell, yaw, zoneOf[cell]));
                    consumed.Add(cell);
                    continue;
                }

                if (count == 2 && !IsStraight(mask))
                {
                    // Authored with docks S and E.
                    var yaw = mask == 0b1001 ? 0f
                        : mask == 0b1100 ? 90f
                        : mask == 0b0110 ? 180f
                        : 270f;
                    tiles.Add(new MapTilePlacement(MapKitPiece.CorridorCornerL, cell, yaw, zoneOf[cell]));
                    consumed.Add(cell);
                    continue;
                }

                // Straight, or a dead end with no room for a cap. Tiled greedily with
                // the longest piece that fits so the scene is not made of 2.5 m stubs.
                var along = IsStraight(mask) && (mask & 0b0101) != 0 ? MapDirection.East : MapDirection.North;
                if (count == 1)
                {
                    along = (mask & 0b0101) != 0 ? MapDirection.East : MapDirection.North;
                }

                var run = new List<MapCell> { cell };
                var next = cell.Step(along);
                while (_corridor.Contains(next) && !consumed.Contains(next) && !doorCells.Contains(next)
                       && IsStraight(MaskWithStair(next, landings))
                       && SameAxis(MaskWithStair(next, landings), along))
                {
                    run.Add(next);
                    next = next.Step(along);
                }

                var index = 0;
                while (index < run.Count)
                {
                    var left = run.Count - index;
                    MapKitPiece piece;
                    int span;
                    if (left >= 4)
                    {
                        piece = MapKitPiece.CorridorStraight10m;
                        span = 4;
                    }
                    else if (left >= 2)
                    {
                        piece = MapKitPiece.CorridorStraight5m;
                        span = 2;
                    }
                    else
                    {
                        piece = MapKitPiece.CorridorStraight2m5;
                        span = 1;
                    }

                    var origin = run[index];
                    tiles.Add(new MapTilePlacement(
                        piece, origin, along == MapDirection.North ? 0f : 90f, zoneOf[origin]));
                    for (var s = 0; s < span; s++)
                    {
                        consumed.Add(run[index + s]);
                    }

                    index += span;
                }
            }

            return tiles.ToArray();
        }

        /// <summary>
        /// Closes the doorways of a room that no corridor arrives at.
        /// <para>
        /// A kit room carries a fixed set of docks and the generator only uses the ones
        /// the layout needs, so the rest are holes straight through the outside wall.
        /// In an unlit basement that is not a subtle defect: the skybox is the
        /// brightest thing in the scene, so an unused dock reads as a window onto
        /// daylight from three storeys underground and destroys the one thing §03 is
        /// built on — that the only light down here is the one you are carrying.
        /// </para>
        /// <para>
        /// The 막힌 길 cap is the piece for it: it is authored as a blind recess with a
        /// plinth, so plugging a doorway with one leaves an alcove rather than a
        /// patch. The 계단 is the deliberate exception — its upper flight is the way
        /// out of the building (§02), and daylight at the top of it is the point.
        /// </para>
        /// </summary>
        private void PlugUnusedDocks(
            RoomRect room,
            Dictionary<MapCell, int> zoneOf,
            List<MapTilePlacement> tiles,
            HashSet<MapCell> consumed)
        {
            if (room.Piece == MapKitPiece.StairwellMetal)
            {
                return;
            }

            foreach (var dock in DockCells(room))
            {
                if (_corridor.Contains(dock.Cell) || InsideRoom(dock.Cell) || consumed.Contains(dock.Cell))
                {
                    continue;
                }

                // The cap is two cells long and docks on one end, so it needs the cell
                // behind the doorway as well. Where that is taken, leaving the opening
                // is better than driving a cap through whatever owns it.
                var beyond = dock.Cell.Step(MapDirections.Opposite(dock.Inward));
                if (_corridor.Contains(beyond) || InsideRoom(beyond) || consumed.Contains(beyond))
                {
                    continue;
                }

                var zone = zoneOf.TryGetValue(dock.Cell, out var found) ? found : ZoneOfRoom(room, zoneOf);
                tiles.Add(new MapTilePlacement(
                    MapKitPiece.DeadEndCap,
                    MinCell(dock.Cell, beyond),
                    MapDirections.YawFacing(dock.Inward),
                    zone));
                consumed.Add(dock.Cell);
                consumed.Add(beyond);
            }
        }

        /// <summary>
        /// The cells a room's doorways open onto, with the direction back into the room.
        /// Offsets come from the kit manifest, not from guesswork: a hall docks 6.25 m
        /// and 13.75 m along each edge, which is the centre of the third and sixth cell.
        /// </summary>
        private static IEnumerable<DockSite> DockCells(RoomRect room)
        {
            var x0 = room.CellX;
            var z0 = room.CellZ;
            var w = room.CellsX;
            var d = room.CellsZ;

            if (room.Piece == MapKitPiece.HallOpen20x20)
            {
                foreach (var offset in new[] { 2, 5 })
                {
                    yield return new DockSite(new MapCell(x0 + offset, z0 - 1, room.Level), MapDirection.North);
                    yield return new DockSite(new MapCell(x0 + offset, z0 + d, room.Level), MapDirection.South);
                    yield return new DockSite(new MapCell(x0 - 1, z0 + offset, room.Level), MapDirection.East);
                    yield return new DockSite(new MapCell(x0 + w, z0 + offset, room.Level), MapDirection.West);
                }

                yield break;
            }

            if (room.Piece == MapKitPiece.ObservationPostGallery)
            {
                yield return new DockSite(new MapCell(x0, z0 - 1, room.Level), MapDirection.North);
                yield return new DockSite(new MapCell(x0, z0 + d, room.Level), MapDirection.South);
            }
        }

        private readonly struct DockSite
        {
            public DockSite(MapCell cell, MapDirection inward)
            {
                Cell = cell;
                Inward = inward;
            }

            /// <summary>The cell outside the doorway.</summary>
            public MapCell Cell { get; }

            /// <summary>Direction from that cell back into the room.</summary>
            public MapDirection Inward { get; }
        }

        private MapPropPlacement[] BuildProps(Dictionary<MapCell, int> zoneOf, HashSet<MapCell> doorCells)
        {
            var props = new List<MapPropPlacement>(_extraProps);

            foreach (var pair in _marks)
            {
                var cell = pair.Key;
                var mark = pair.Value;
                if (!zoneOf.TryGetValue(cell, out var zone))
                {
                    continue;
                }

                if ((mark.Kind & MapNodeKind.ElectricalPanel) != 0)
                {
                    props.Add(new MapPropPlacement(
                        MapKitPiece.WallPanelElectrical, cell.Centre, 0f, zone,
                        "ElectricalPanel_" + _zones[zone].Name + "_" + cell.X + "_" + cell.Z));
                }
            }

            foreach (var cell in doorCells)
            {
                if (!zoneOf.TryGetValue(cell, out var zone))
                {
                    continue;
                }

                props.Add(new MapPropPlacement(
                    MapKitPiece.DoorPanelLockable, cell.Centre, 0f, zone,
                    "DoorPanel_" + cell.Level + "_" + cell.X + "_" + cell.Z));
            }

            props.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return props.ToArray();
        }

        private MapMarkerPlacement[] BuildMarkers(
            MapGraph graph,
            Dictionary<MapCell, int> nodeIdOf,
            Dictionary<MapCell, int> zoneOf,
            Dictionary<MapCell, int> lootAt)
        {
            var markers = new List<MapMarkerPlacement>();

            foreach (var cell in _doorCells)
            {
                zoneOf.TryGetValue(cell, out var doorZone);
                markers.Add(new MapMarkerPlacement(
                    MapMarkerKind.LockableDoor, cell.Centre, doorZone, -1,
                    "Door_" + cell));
            }

            var entrances = graph.NodesOfKind(MapNodeKind.Entrance);
            if (entrances.Length == 0)
            {
                throw new MapSketchException(
                    "No 출입구 is marked. §02 makes leaving the building the win condition, so a map without "
                    + "one cannot be won and §12's concealment rule has nothing to sit beside.");
            }

            // Players come in at the exit and the monster starts as far from it as the
            // building allows: §07 초저녁 patrols one zone, and starting the monster on
            // top of the door would spend the quiet opening the whole time curve needs.
            var entrance = entrances[0];
            var spawnRing = graph.NodesWithinWalk(entrance, GameConstants.LineOfSightBreakSpacingMin);
            for (var i = 0; i < GameConstants.PlayersPerMatch; i++)
            {
                var node = i < spawnRing.Length ? spawnRing[i] : entrance;
                markers.Add(new MapMarkerPlacement(
                    MapMarkerKind.PlayerSpawn, graph.Nodes[node].Position, graph.Nodes[node].ZoneId, node,
                    "PlayerSpawn_" + i));
            }

            var farthest = entrance;
            var farthestDistance = 0f;
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                var distance = graph.PathLength(entrance, i);
                if (!float.IsPositiveInfinity(distance) && distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthest = i;
                }
            }

            markers.Add(new MapMarkerPlacement(
                MapMarkerKind.MonsterSpawn, graph.Nodes[farthest].Position, graph.Nodes[farthest].ZoneId, farthest,
                "MonsterSpawn"));

            var sites = graph.NodesOfKind(MapNodeKind.CandidateSite);
            for (var i = 0; i < sites.Length; i++)
            {
                var node = sites[i];
                markers.Add(new MapMarkerPlacement(
                    MapMarkerKind.CandidateSite, graph.Nodes[node].Position, graph.Nodes[node].ZoneId, node,
                    "CandidateSite_" + graph.Zones[graph.Nodes[node].ZoneId].Name + "_" + i));
            }

            foreach (var pair in lootAt)
            {
                var node = nodeIdOf[pair.Key];
                markers.Add(new MapMarkerPlacement(
                    MapMarkerKind.LootSpawn, graph.Nodes[node].Position, graph.Nodes[node].ZoneId, node,
                    "LootSpawn_" + graph.Zones[graph.Nodes[node].ZoneId].Name
                    + "_" + pair.Key.Level + "_" + pair.Key.X + "_" + pair.Key.Z));
            }

            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                var node = graph.Nodes[i];
                var isEntrance = node.HasAny(MapNodeKind.Entrance);
                var kind = isEntrance ? MapMarkerKind.EntranceLight : MapMarkerKind.ZoneLight;

                // One light per junction rather than per cell: §04's 구역 조명 lights a
                // whole zone at once, so the unit that matters is the zone group, and a
                // light in every corridor cell would make the dark §03 depends on
                // impossible to switch off convincingly.
                if (graph.Degree(i) < 2 && !isEntrance)
                {
                    continue;
                }

                markers.Add(new MapMarkerPlacement(
                    kind, node.Position, node.ZoneId, i,
                    (isEntrance ? "EntranceLight_" : "ZoneLight_") + graph.Zones[node.ZoneId].Name + "_" + i));
            }

            markers.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return markers.ToArray();
        }

        private int ZoneOfRoom(RoomRect room, Dictionary<MapCell, int> zoneOf)
        {
            for (var x = room.CellX; x < room.CellX + room.CellsX; x++)
            {
                for (var z = room.CellZ; z < room.CellZ + room.CellsZ; z++)
                {
                    if (zoneOf.TryGetValue(new MapCell(x, z, room.Level), out var zone))
                    {
                        return zone;
                    }
                }
            }

            return -1;
        }

        private bool InsideRoom(MapCell cell)
        {
            for (var i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room.Level == cell.Level
                    && cell.X >= room.CellX && cell.X < room.CellX + room.CellsX
                    && cell.Z >= room.CellZ && cell.Z < room.CellZ + room.CellsZ)
                {
                    return true;
                }
            }

            return false;
        }

        private int NeighbourMask(MapCell cell)
        {
            var mask = 0;
            foreach (var dir in MapDirections.All)
            {
                if (_corridor.Contains(cell.Step(dir)))
                {
                    mask |= 1 << (int)dir;
                }
            }

            return mask;
        }

        private static bool IsStraight(int mask) => mask == 0b0101 || mask == 0b1010;

        private static bool SameAxis(int mask, MapDirection along) =>
            along == MapDirection.East ? (mask & 0b0101) == 0b0101 : (mask & 0b1010) == 0b1010;

        private static int CountBits(int mask)
        {
            var count = 0;
            while (mask != 0)
            {
                count += mask & 1;
                mask >>= 1;
            }

            return count;
        }

        private static MapDirection FirstDirection(int mask)
        {
            foreach (var dir in MapDirections.All)
            {
                if ((mask & (1 << (int)dir)) != 0)
                {
                    return dir;
                }
            }

            throw new MapSketchException("A corridor cell with no neighbours reached the tiler.");
        }

        private static MapDirection MissingDirection(int mask)
        {
            foreach (var dir in MapDirections.All)
            {
                if ((mask & (1 << (int)dir)) == 0)
                {
                    return dir;
                }
            }

            throw new MapSketchException("A four-way cell was treated as a T junction.");
        }

        /// <summary>
        /// The origin corner of a two-cell piece — the lower X and the lower Z, on the
        /// storey both cells are already on.
        /// <para>
        /// Carrying the level is not a detail. <see cref="MapCell(int, int)"/> means
        /// "on the topmost storey", so dropping it here silently moved every 막힌 길
        /// cap and every plugged room dock onto B1 whatever floor it belonged to. The
        /// corridor that should have been capped stayed open on its own storey, and a
        /// two-cell block of walls appeared in the middle of B1 — one of them standing
        /// across the junction at (4,20), which is on the route from the 하역장 to the
        /// 저탄장 계단. Both halves of that are invisible: §12's checklist is
        /// per-storey and horizontal, and a stray cap looks like architecture.
        /// </para>
        /// </summary>
        /// <exception cref="MapSketchException">The two cells are on different storeys.</exception>
        private static MapCell MinCell(MapCell a, MapCell b)
        {
            if (a.Level != b.Level)
            {
                throw new MapSketchException(
                    "A two-cell piece was asked to span " + a + " and " + b + ", which are on different storeys. "
                    + "Nothing in the kit climbs except the 계단, and it is placed by MapSketch.Stair.");
            }

            return new MapCell(System.Math.Min(a.X, b.X), System.Math.Min(a.Z, b.Z), a.Level);
        }

        private readonly struct CellMark
        {
            public CellMark(MapNodeKind kind, string name)
            {
                Kind = kind;
                Name = name;
            }

            public MapNodeKind Kind { get; }

            public string Name { get; }
        }

        private readonly struct RoomRect
        {
            public RoomRect(
                MapKitPiece piece, int cellX, int cellZ, int cellsX, int cellsZ, float yawDegrees, int level)
            {
                Piece = piece;
                CellX = cellX;
                CellZ = cellZ;
                CellsX = cellsX;
                CellsZ = cellsZ;
                YawDegrees = yawDegrees;
                Level = level;
            }

            public MapKitPiece Piece { get; }

            public int CellX { get; }

            public int CellZ { get; }

            public int CellsX { get; }

            public int CellsZ { get; }

            public float YawDegrees { get; }

            public int Level { get; }
        }

        /// <summary>
        /// One 계단 between two storeys: the two landings it joins and the shaft that
        /// holds the flights.
        /// </summary>
        /// <summary>A 투하구: where you jump, and where you land one storey down.</summary>
        private readonly struct ChuteRun
        {
            public ChuteRun(MapCell mouth, MapCell landing, string name)
            {
                Mouth = mouth;
                Landing = landing;
                Name = name;
            }

            /// <summary>The hole in the middle of the upper storey.</summary>
            public MapCell Mouth { get; }

            /// <summary>Where it puts you, on the rim of the storey below.</summary>
            public MapCell Landing { get; }

            /// <summary>Label for the graph edge.</summary>
            public string Name { get; }
        }

        private readonly struct StairRun
        {
            public StairRun(MapCell lower, MapCell upper, int shaftX, int shaftZ, int shaftLevel, string name)
            {
                Lower = lower;
                Upper = upper;
                ShaftX = shaftX;
                ShaftZ = shaftZ;
                ShaftLevel = shaftLevel;
                Name = name;
            }

            /// <summary>Landing on the deeper storey — the shaft's own floor.</summary>
            public MapCell Lower { get; }

            /// <summary>Landing one storey up, reached by the second flight.</summary>
            public MapCell Upper { get; }

            /// <summary>Lowest cell of the 2 × 2 shaft along X.</summary>
            public int ShaftX { get; }

            /// <summary>Lowest cell of the shaft along Z. The landings are at <c>ShaftZ − 1</c>.</summary>
            public int ShaftZ { get; }

            /// <summary>Storey the shaft stands on — the same as <see cref="Lower"/>.</summary>
            public int ShaftLevel { get; }

            /// <summary>Label carried onto the graph edge, so a §12 report can name the stair.</summary>
            public string Name { get; }
        }

        private readonly struct HalfEdge : IEquatable<HalfEdge>
        {
            private readonly MapCell _cell;
            private readonly MapDirection _direction;

            public HalfEdge(MapCell cell, MapDirection direction)
            {
                _cell = cell;
                _direction = direction;
            }

            public bool Equals(HalfEdge other) => _cell.Equals(other._cell) && _direction == other._direction;

            public override bool Equals(object? obj) => obj is HalfEdge other && Equals(other);

            public override int GetHashCode() => (_cell.GetHashCode() * 397) ^ (int)_direction;
        }

        /// <summary>
        /// Picks what is waiting in each 막힌 길. §12 requires a reward on every one —
        /// "위험을 감수할 이유" — and §08's catalogue is where the values live, so the
        /// generator never invents a number.
        /// </summary>
        private sealed class DeadEndRewards
        {
            private readonly IRandomSource _random;

            public DeadEndRewards(IRandomSource random)
            {
                _random = random;
            }

            public int Next()
            {
                var all = LootCatalogue.All;
                var pick = all[_random.NextInt(0, all.Count)];
                var value = LootCatalogue.ValueOf(pick);

                // A zero-value entry would leave the leaf unrewarded and fail §12, so a
                // catalogue that ever gains one falls back to the cheapest real piece
                // rather than silently producing an invalid map.
                return value > 0 ? value : GameConstants.LootValueTrinket;
            }
        }
    }
}
