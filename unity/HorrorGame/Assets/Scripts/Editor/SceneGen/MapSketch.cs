using System;
using System.Collections.Generic;
using System.Text;
using HorrorGame.Core;
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
        /// 도달 지점 — a marker whose only job is to be somewhere the reachability
        /// audits must be able to walk to.
        /// <para>
        /// <b>It replaces two markers that were doing this job under other names.</b>
        /// <c>CandidateSite</c> was §12's three-per-zone 단서·목표물 후보, and
        /// <c>LootSpawn</c> was a §08 전리품 on every 막힌 길 — 24 + 152 = 176 of the
        /// 220 markers <c>NavMeshConnectivity</c> pairs into its 3482 routes. Both
        /// systems are deleted, and 80% of the building's proof of reachability was
        /// riding on them by accident.
        /// </para>
        /// <para>
        /// Same cells, no payload. A <c>LootSpawn</c> was a reward with a position —
        /// <c>DeadEndRewards.Next()</c> drew a §08 credit value and hung it on the node.
        /// A <c>ReachProbe</c> is a position with nothing on it: nothing spawns, nothing
        /// is worth anything, and the set no longer depends on a random draw at all.
        /// The failure mode inverts with the name — a missing 전리품 was a balance
        /// complaint, a probe the audit cannot reach is a failed build.
        /// </para>
        /// </summary>
        ReachProbe,

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

        /// <summary>
        /// §01's 투하구 — the hole in the middle of a storey. Carries the landing position on
        /// the storey below in <see cref="MapMarkerPlacement.Name"/>'s companion marker, so
        /// the runtime can drop a player without knowing the graph.
        /// </summary>
        Chute,

        /// <summary>Where a 투하구 puts you down: the rim of the storey below.</summary>
        ChuteLanding,

        // DELETED with §04's 정비공 and the light economy: ZoneLight. 567 point lights,
        // one at every junction of degree 2 or more, authored DISABLED and switchable
        // only by the Engineer at a 배전반. With no role to switch them and no panel to
        // switch them at, they were 567 lamps nobody could ever turn on. Darkness is
        // untouched by their removal — they were off.

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
        private readonly List<MapCell> _playerStarts = new List<MapCell>();

        // EVERY declared creature, in declaration order — not the last one.
        //
        // This was `private MapCell? _monsterStart` and the assignment below it overwrote.
        // DescentMap.PlaceStarts calls MonsterStart once per storey, so eight declarations
        // collapsed into one and the artefact was byte-identical to a single call: that is
        // why the §06 audit could only ever print "monster reach … over 1 of 8 storeys" and
        // name the other seven under "no creature on". §12-B③ 「괴물이 안쪽을 순찰한다 …
        // 외곽은 안전하고 중심은 위험하다」 is written about every floor, and one field made it
        // unstateable.
        //
        // The LAST entry is the primary, and that is a contract with DescentMap rather than
        // an accident: its comment declares B5 last precisely so that a runtime which can
        // only carry one creature carries it half way down. See PrimaryMonsterSpawnName for
        // what the primary's marker is named and which three consumers depend on it.
        private readonly List<MapCell> _monsterStarts = new List<MapCell>();

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
        /// Declares where a runner starts. §01 puts all of them on the OUTER ring of the
        /// top storey.
        /// <para>
        /// Without this the generator falls back on its old rule — spawn everybody within
        /// walking distance of the 출입구 — which was right when the 출입구 was the way in
        /// and is catastrophic now that it is the FINISH. Twenty players would begin the race
        /// standing on the line they are racing to.
        /// </para>
        /// </summary>
        /// <param name="x">Cell X.</param>
        /// <param name="z">Cell Z.</param>
        /// <param name="level">Storey.</param>
        public MapSketch PlayerStart(int x, int z, int level)
        {
            _playerStarts.Add(new MapCell(_offsetX + x, _offsetZ + z, level));
            return this;
        }

        /// <summary>
        /// Declares where a creature starts. Call it once per creature — every call is kept.
        /// <para>
        /// The fallback, used only when nothing at all is declared, is "as far from the 출입구
        /// as the building allows", which in a race puts it on the top storey among twenty
        /// people at the starting line. §12-B wants it deep — the inner rings of a middle
        /// floor, so the descent gets more dangerous rather than starting that way.
        /// </para>
        /// <para>
        /// <b>It appends, and it used to assign.</b> A single field meant that a building
        /// declaring a creature on each of eight storeys produced exactly the same map as one
        /// declaring a single creature on the last of them, with nothing anywhere saying seven
        /// had been dropped. §12-B③ asks for one on every floor; the audit could only report
        /// what the markers said, and the markers said one.
        /// </para>
        /// <para>
        /// <b>The last call is the primary.</b> Only its marker is named
        /// <see cref="PrimaryMonsterSpawnName"/>, which is what a runtime that carries one
        /// creature finds; every other call adds a marker the §06 audit measures and a
        /// multi-agent runtime can instantiate. So declaring more creatures never MOVES the
        /// one that already ran.
        /// </para>
        /// </summary>
        /// <param name="x">Cell X.</param>
        /// <param name="z">Cell Z.</param>
        /// <param name="level">Storey.</param>
        public MapSketch MonsterStart(int x, int z, int level)
        {
            _monsterStarts.Add(new MapCell(_offsetX + x, _offsetZ + z, level));
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

            foreach (var cell in orderedNodeCells)
            {
                _marks.TryGetValue(cell, out var mark);
                var name = string.IsNullOrEmpty(mark.Name) ? _zones[zoneOf[cell]].Name + cell.ToString() : mark.Name;
                nodeIdOf[cell] = builder.AddNode(zoneOf[cell], cell.Centre, KindOf(mark, cell), name);
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

            // The graph is still built twice: once to learn the topology, once knowing
            // it. It used to be so that §12's 막힌 길 보상 could be hung on every leaf;
            // it is now so that every leaf can be given a 도달 지점 marker. Same pass,
            // same cells, no §08 value — and it keeps IsDeadEnd the single source of
            // truth about what a dead end is.
            var probe = builder.Build();
            var finalBuilder = new MapGraphBuilder().Named(_name);
            for (var i = 0; i < _zones.Count; i++)
            {
                finalBuilder.AddZone(_zones[i].Name, _zones[i].Floor, _zones[i].Centre, _zones[i].Size);
            }

            var leaves = new List<MapCell>();
            foreach (var cell in orderedNodeCells)
            {
                _marks.TryGetValue(cell, out var mark);
                var id = nodeIdOf[cell];
                if (probe.IsDeadEnd(id))
                {
                    leaves.Add(cell);
                }

                var name = string.IsNullOrEmpty(mark.Name) ? _zones[zoneOf[cell]].Name + cell.ToString() : mark.Name;

                // The node's reward is now always 0. §12's 막힌 길 보상 was a §08 credit
                // value; a race pays for a dead end in the only currency it has, which is
                // time. The parameter survives because MapNode carries it and MapValidator
                // still reads it — see CheckDeadEnds.
                finalBuilder.AddNode(zoneOf[cell], cell.Centre, KindOf(mark, cell), name);
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

            // No door cells passed to BuildProps: the 문짝 is hung off the LockableDoor
            // marker by MapSceneBuilder.BuildDoor, and hanging a second one here is what
            // put two leaves in every doorway. See BuildProps's remarks.
            var props = BuildProps(zoneOf);
            var markers = BuildMarkers(graph, nodeIdOf, zoneOf, leaves);
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

            // Every 막힌 길 the tiler is responsible for closing, gathered before a piece
            // is placed so the two passes below can be checked against it rather than
            // trusted. One corridor neighbour, no 문 across it, no 계단 arriving at it,
            // and not a cell a room already floors — the same set §12 counts from the
            // graph, seen from the tiler's side.
            var blindEnds = new List<MapCell>();
            foreach (var cell in ordered)
            {
                if (InsideRoom(cell) || doorCells.Contains(cell) || landings.ContainsKey(cell))
                {
                    continue;
                }

                if (CountBits(MaskWithStair(cell, landings)) == 1)
                {
                    blindEnds.Add(cell);
                }
            }

            // Dead-end caps first: the cap is 2 cells long, so it has to claim its
            // outward cell before the straight tiler does.
            var capOrigins = new List<MapCell>();
            foreach (var cell in ordered)
            {
                if (consumed.Contains(cell))
                {
                    continue;
                }

                // A cell a room already covers gets no corridor piece. Without this the
                // chamber and nine corridor tiles were both placed on the same nine cells,
                // and the corridor walls won — the audit came back byte-identical to the run
                // with no room at all, which is how this was found. The room supplies the
                // geometry; the cells stay only so the graph has places to count.
                if (InsideRoom(cell))
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

                // DELETED with §04's 관측자: the ObservationPostBarredWindow branch that
                // stood here. An alcove marked 관측 지점 got the barred window instead of
                // the 막힌 길 cap — same topology, and the bars were what made standing
                // there survivable while a role's whole job was watching the creature from
                // ObserverRange. Nobody watches now: in a race, standing still to look at
                // the hazard is losing. Measured on the shipped scene, exactly 8 leaves
                // took this branch, one per storey; they now take the DeadEnd_Cap the
                // other 144 already take, which closes the same blind end and leaves the
                // alcove equally open toward its corridor.
                var outward = MapDirections.Opposite(inward);
                var beyond = cell.Step(outward);
                if (_corridor.Contains(beyond) || consumed.Contains(beyond) || InsideRoom(beyond))
                {
                    continue;
                }

                // There used to be a third guard here: no cap within Chebyshev 3 of
                // another cap's origin. It is gone, and this is why.
                //
                // It was added by 0a65f45 to stop caps clustering — "in one 3 x 3 there
                // were FOUR" — and that commit's own measurement records what it bought:
                // "the NavMesh audit came back byte-identical to the run before it …
                // the caps were a real defect and they were NOT the cause of the islands.
                // Committing the fix … not because it bought anything measurable." So the
                // rule never protected the bake. It was a tidiness rule with a magic 3 in
                // it, and the claim it left behind — that a declined cell is "still walled
                // by its neighbours' geometry" — was false. A declined cell falls through
                // to the greedy straight tiler and gets a Corridor_Straight_2m5, which is
                // authored with walls on its two long sides and NOTHING at either end.
                // Measured on the shipped scene: 152 막힌 길, 104 capped, 8 barred, and 40
                // left as 2.2 m doorways out of the maze — five per storey, the same five
                // on all eight, one of them 9.0 m from B8's finish across the open floor
                // slab. That is the owner's "맵밖으로 나갈수가있는거같은데".
                //
                // What removing it costs, measured on the same scene by placing all 40:
                // caps per storey 13 → 18, cap footprints sharing an edge 0 → 2, densest
                // 3 x 3 by cap origin 1 → 2. The four-in-a-3x3 lump does not come back.
                // Nothing else moves: no two 막힌 길 on this map want the same outward
                // cell (measured: 0 contentions), so no cap loses its place, and every one
                // of the 40 has a degree-3 Junction_T as its one neighbour, so no pair of
                // caps can seal each other in. The numbers are logged below rather than
                // enforced, because the thing worth knowing is how dense the caps got, and
                // a silent 'continue' is what turned that into a hole in the building.
                //
                // The cap is authored with its dock on −Y and its body running +Y, so
                // it faces the corridor it closes off.
                var capAt = MinCell(cell, beyond);
                tiles.Add(new MapTilePlacement(
                    MapKitPiece.DeadEndCap, capAt, MapDirections.YawFacing(inward), zoneOf[cell]));
                capOrigins.Add(capAt);
                consumed.Add(cell);
                consumed.Add(beyond);
            }

            // Second pass: close whatever the cap could not take.
            //
            // A cap needs a second cell, and there are three ways it can be refused one —
            // the cell beyond is corridor, is already taken, or belongs to a room. When
            // that happens the 막힌 길 must NOT be handed to the greedy tiler: §12's maze
            // is what makes §02's race to the middle cost anything, and a corridor end
            // that opens onto the storey's own floor slab is a runner walking round the
            // 4 → 2 → 1 gates instead of through them. MapSceneBuilder.BuildFloorSlab
            // pours that slab under the walls and between the corridors, so outside is
            // not a void here — it is a shortcut.
            //
            // ObservationPostBarredWindow is the kit's only ONE-cell blind end: authored
            // with a solid W wall, a solid N wall at the far end and its single dock on
            // −Y, so it closes the end inside the cell it is given and claims nothing it
            // could take from a neighbour. That is what makes this pass total — it cannot
            // itself be refused. The window it carries sits at 1.00 ~ 2.20 m in a SIDE
            // wall behind 0.13 m bars, above solid masonry, so it is a sightline and never
            // a passage; §12's 창문 · 격자 is the same fixture doing the same job one cell
            // further in. The cost, said plainly: where this fires the cell reads as an
            // Observer post rather than as a 막힌 길 with a 전리품 plinth, and
            // DressingSpace.IsObservationPost keys off the piece name, so the dresser will
            // leave those walls bare. On the shipped map it fires nowhere — all 40 take a
            // real cap — and an unexercised branch is worth exactly what it says on the
            // log line below.
            var barred = 0;
            foreach (var cell in blindEnds)
            {
                if (consumed.Contains(cell))
                {
                    continue;
                }

                tiles.Add(new MapTilePlacement(
                    MapKitPiece.ObservationPostBarredWindow, cell,
                    MapDirections.YawFacing(FirstDirection(MaskWithStair(cell, landings))),
                    zoneOf[cell]));
                consumed.Add(cell);
                barred++;
            }

            ReportBlindEnds(blindEnds, capOrigins, consumed, barred);

            foreach (var cell in ordered)
            {
                if (consumed.Contains(cell))
                {
                    continue;
                }

                var mask = MaskWithStair(cell, landings);
                var count = CountBits(mask);

                // §12 정비공's door is the graph edge and ONE leaf, not the frame.
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
                // So the passage stays open geometry and the 문 itself is the
                // LockableDoor marker BuildMarkers emits at this same cell:
                // MapSceneBuilder.BuildDoor hangs ONE Door_Panel_Lockable off a hinge
                // there and MatchDirector.AttachDoors binds that hinge, which is what
                // §04 swings, blocks with and carves with. It is out of the bake, as a
                // runtime-locked door has to be. BuildProps used to hang a SECOND panel
                // at this cell's centre — see the note there — and this tile is what it
                // stood in front of.
                //
                // Keep this branch. It is not the only thing flooring the cell — the
                // greedy run below refuses to pass THROUGH a 문 (the doorCells guard in
                // its while) but would happily start a run AT one, so deleting this
                // would still leave walkable geometry. What it is the only thing doing
                // is making the 문 exactly one 2.5 m piece: the leaf's 2.20 m opening
                // then has a tile seam at each jamb instead of sitting mid-way along a
                // merged 10 m corridor, and a 계단 landing sharing the cell cannot turn a
                // 문 into a JunctionT. Restore the frame here — INSTEAD of this piece,
                // never on top of it, which is the mistake MapSceneBuilder.BuildDoor's
                // remarks record — when build_doorway_frame in
                // tools/blender/gen_mapkit.py leaves a clear opening wider than
                // 2 × agentRadius.
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
        /// States what happened to every 막힌 길, and refuses to build a map with an open
        /// one.
        /// <para>
        /// This is the falsifier for the pass above, and it is a throw rather than a log
        /// because of what the alternative looked like: 40 corridor ends stood open in a
        /// shipped scene, on every one of the eight storeys, and nothing said so. The
        /// §12 checklist reads the graph — where those cells are perfectly good 막힌 길 —
        /// the NavMesh audit reads the baked surface, which the storey's floor slab is
        /// not part of, and the 주자 tests walk the intended route. Three green gates and
        /// a hole in the building. The count below is the one number that can see it.
        /// </para>
        /// <para>
        /// The cap density is reported and not enforced, so that 0a65f45's measurement
        /// stays comparable: it counted "14 pairs inside two cells of each other" on one
        /// floor and called that clustering. If a future layout brings the lump back, it
        /// shows up here as a number instead of as 40 silently declined caps.
        /// </para>
        /// </summary>
        /// <exception cref="MapSketchException">A 막힌 길 reached the greedy tiler, which would leave it open at the far end.</exception>
        private static void ReportBlindEnds(
            List<MapCell> blindEnds, List<MapCell> capOrigins, HashSet<MapCell> consumed, int barred)
        {
            var open = new List<MapCell>();
            foreach (var cell in blindEnds)
            {
                if (!consumed.Contains(cell))
                {
                    open.Add(cell);
                }
            }

            if (open.Count > 0)
            {
                throw new MapSketchException(
                    open.Count + " 막힌 길 reached the straight tiler with nothing closing them: "
                    + Describe(new HashSet<MapCell>(open)) + ". Corridor_Straight_2m5 is authored with "
                    + "walls on its two long sides and nothing at either end, so each of these would be a "
                    + MapKitCatalogue.CorridorClearWidth.ToString("0.00")
                    + " m doorway out of §12's maze onto the storey's own floor slab — and §02 is a race to "
                    + "the middle that the maze is the whole cost of. Close it with a piece, not with a "
                    + "comment.");
            }

            // Pairs of caps whose origins sit inside two cells of each other, on the same
            // storey. 0a65f45's unit, so the two runs can be compared directly.
            var close = 0;
            var tightest = int.MaxValue;
            for (var i = 0; i < capOrigins.Count; i++)
            {
                for (var j = i + 1; j < capOrigins.Count; j++)
                {
                    if (capOrigins[i].Level != capOrigins[j].Level)
                    {
                        continue;
                    }

                    var gap = System.Math.Max(
                        System.Math.Abs(capOrigins[i].X - capOrigins[j].X),
                        System.Math.Abs(capOrigins[i].Z - capOrigins[j].Z));
                    tightest = System.Math.Min(tightest, gap);
                    if (gap <= 2)
                    {
                        close++;
                    }
                }
            }

            UnityEngine.Debug.Log(
                "[SceneGen] 막힌 길 " + blindEnds.Count + "개 전부 막혔다: 막힌 길 cap "
                + capOrigins.Count + ", 격자창 " + (blindEnds.Count - capOrigins.Count - barred)
                + " (관측 지점) + " + barred + " (cap이 들어갈 자리가 없어서), 열린 채 남은 곳 "
                + open.Count + ". "
                + "Cap origins inside two cells of one another: " + close + " pair(s), tightest "
                + (tightest == int.MaxValue ? "n/a" : tightest.ToString()) + " cell(s).");
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

            if (room.Piece == MapKitPiece.ChamberOpen3x3)
            {
                // The middle cell of each edge, and nowhere else — Chamber_Open_3x3 is
                // authored open at 3.75 m along a 7.5 m side, which is the centre of the
                // second of three cells.
                //
                // This entry is not bookkeeping. VerifyRoomWalls is the check that made the
                // chamber worth using: with the room in place it named, at generation time,
                // the exact passage crossing the exact wall. Without the entry its doorway
                // list is empty and it refuses everything, which is the correct behaviour for
                // a room it has never been told about.
                yield return new DockSite(new MapCell(x0 + 1, z0 - 1, room.Level), MapDirection.North);
                yield return new DockSite(new MapCell(x0 + 1, z0 + d, room.Level), MapDirection.South);
                yield return new DockSite(new MapCell(x0 - 1, z0 + 1, room.Level), MapDirection.East);
                yield return new DockSite(new MapCell(x0 + w, z0 + 1, room.Level), MapDirection.West);
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

        /// <summary>
        /// The kit pieces that are placed off the grid rather than on it: §12's 전기 패널,
        /// and anything a map author added by hand with <see cref="Prop"/>.
        /// <para>
        /// <b>No 문짝 here, and that is the two-leaf door fixed.</b> This method used to add a
        /// <see cref="MapKitPiece.DoorPanelLockable"/> at every 문 cell, so every door in
        /// the building had TWO leaves. The keeper is the other one:
        /// <c>MapSceneBuilder.BuildDoor</c> hangs a leaf off the LockableDoor marker's
        /// hinge, <c>MatchDirector.AttachDoors</c> binds that hinge as §04's swinging
        /// leaf, and the blocking collider and the carving <c>NavMeshObstacle</c> live on
        /// it. The one deleted here had no interactable and nothing that could ever
        /// rotate it, and — searched across the whole repo — no reader at all. The single
        /// place that matches its name, <c>DressingSpace.CellInfo.IsDoorway</c>, walks
        /// only <c>Zone_*/Tiles</c> and <c>Map/Shared</c>, while these props were
        /// parented to <c>Zone_*/Props</c>.
        /// </para>
        /// <para>
        /// Measured in the written scene before the fix (Map_FirstSketch.unity, 8 doors):
        /// 16 <c>Door_Panel_Lockable</c> prefab instances — 8 at
        /// <c>Map/Zone_*/Props/DoorPanel_L_X_Z</c> and 8 at
        /// <c>Map/Markers/Door_(x,z@Ln)/Hinge/Leaf</c>. The prop was dropped at the cell
        /// CENTRE at a fixed yaw 0, and the leaf's origin is its hinge stile, so its
        /// 1.10 m of geometry ran from the cell centre to one jamb: a door standing shut
        /// across exactly half of the corridor's 2.20 m clear width, on eight storeys,
        /// with nothing in the game able to open it.
        /// </para>
        /// <para>
        /// <b>And it was solid.</b> This was filed as cosmetic; it is not.
        /// <c>MapSceneBuilder.Finish</c> gives every prop renderer a <c>MeshCollider</c>,
        /// and the scene says so — each <c>DoorPanel_*</c> instance carries an
        /// <c>m_Material</c> override pointing at a <c>PhysicsMaterial</c>
        /// (<c>Assets/Scenes/Generated/Materials/Surface_*.asset</c>), which only a
        /// collider has. So every 문 in the building was physically 1.10 m wide, not the
        /// 2.20 m §12 sizes a 병목 at, while §06's creature — which navigates the baked
        /// surface, and the prop was kept out of the bake — walked straight through the
        /// closed half. Nothing failed because a runner's <c>CharacterController</c> is
        /// 0.6 m across and still fitted.
        /// </para>
        /// <para>
        /// Removing it cannot move the BAKE: the prop loop calls
        /// <c>KeepOutOfNavMeshBake</c> and the scene carries <c>m_IgnoreFromBuild: 1</c>
        /// on each, so islands and marker pairs are computed from unchanged input. §12's
        /// "[ok] lockable-doors" is <c>MapValidator.CheckLockableDoors</c> reading the
        /// GRAPH's <c>HasLockableDoor</c> edges and never saw a prop. The generator's own
        /// "Scene contents" line is the cheap check that this landed: 16 props before,
        /// 8 after, with 1488 kit pieces and 1099 markers unchanged.
        /// </para>
        /// </summary>
        private MapPropPlacement[] BuildProps(Dictionary<MapCell, int> zoneOf)
        {
            // DELETED with the light economy: the loop over _marks that stood here and
            // put a MapKitPiece.WallPanelElectrical at every MapNodeKind.ElectricalPanel.
            // It was the ONLY prop generator in the sketch — measured on the shipped
            // scene, all 8 prefab instances under any Zone_*/Props were named
            // ElectricalPanel_*, one per storey, and there was nothing else. With no
            // 정비공 and no zone lights to switch, there is nothing behind the panel door.
            //
            // _extraProps stays, and so does this method: it is the general path an
            // authored prop takes, and a builder that silently dropped one would be a
            // worse trap than a method that currently just sorts.
            var props = new List<MapPropPlacement>(_extraProps);

            props.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return props.ToArray();
        }

        private MapMarkerPlacement[] BuildMarkers(
            MapGraph graph,
            Dictionary<MapCell, int> nodeIdOf,
            Dictionary<MapCell, int> zoneOf,
            List<MapCell> leaves)
        {
            var markers = new List<MapMarkerPlacement>();

            // This marker IS the 문 — the whole door and its only leaf. The name is the
            // contract: MapSceneBuilder.BuildDoor builds Door_(x,z@Ln)/Hinge/Leaf under
            // it, and MatchDirector.AttachDoors finds a door by the "Door_" prefix and a
            // child called "Hinge", so a renamed marker is a door §04 can never lock.
            // Nothing else may place a Door_Panel_Lockable at these cells; BuildProps
            // did, and that is what gave every doorway a second, permanently shut leaf.
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

            // The 출입구 both fallbacks are measured from — players in walking distance of it,
            // the creature as far from it as the building allows. Both are fallbacks now: a
            // sketch that declares its own starts (§01's tower declares all of them) never
            // reaches either. See BuildMonsterSpawns.
            var entrance = entrances[0];

            if (_playerStarts.Count > 0)
            {
                for (var i = 0; i < _playerStarts.Count; i++)
                {
                    if (!nodeIdOf.TryGetValue(_playerStarts[i], out var node))
                    {
                        continue;
                    }

                    markers.Add(new MapMarkerPlacement(
                        MapMarkerKind.PlayerSpawn, graph.Nodes[node].Position, graph.Nodes[node].ZoneId, node,
                        "PlayerSpawn_" + markers.Count));
                }
            }
            else
            {
                var spawnRing = graph.NodesWithinWalk(entrance, GameConstants.LineOfSightBreakSpacingMin);
                for (var i = 0; i < GameConstants.PlayersPerMatch; i++)
                {
                    var node = i < spawnRing.Length ? spawnRing[i] : entrance;
                    markers.Add(new MapMarkerPlacement(
                        MapMarkerKind.PlayerSpawn, graph.Nodes[node].Position, graph.Nodes[node].ZoneId, node,
                        "PlayerSpawn_" + i));
                }
            }

            for (var i = 0; i < _chutes.Count; i++)
            {
                var chute = _chutes[i];
                if (!nodeIdOf.TryGetValue(chute.Mouth, out var mouth)
                    || !nodeIdOf.TryGetValue(chute.Landing, out var landing))
                {
                    continue;
                }

                // Two markers rather than one with a payload: MapMarkerPlacement carries a
                // position and a name and nothing else, and widening it for one consumer
                // would put a §01 concept into the shape every marker has. The runtime pairs
                // them by name, which is the same thing the door geometry already does.
                markers.Add(new MapMarkerPlacement(
                    MapMarkerKind.Chute, graph.Nodes[mouth].Position,
                    graph.Nodes[mouth].ZoneId, mouth, chute.Name));
                markers.Add(new MapMarkerPlacement(
                    MapMarkerKind.ChuteLanding, graph.Nodes[landing].Position,
                    graph.Nodes[landing].ZoneId, landing, chute.Name + " 착지"));
            }

            BuildMonsterSpawns(markers, graph, nodeIdOf, entrance);

            // 도달 지점, from the two sources that used to be 후보 지점 and 전리품, under
            // one name and one naming scheme so the audits match a single prefix.
            //
            // The marks are iterated rather than graph.NodesOfKind, because a marked node
            // has to hand back its CELL to be named the same way a leaf is. The two sets
            // are disjoint by construction — a band probe stands on a rail, which has
            // degree 2 or more, and a leaf has degree 1 — so no cell is emitted twice.
            foreach (var pair in _marks)
            {
                if ((pair.Value.Kind & MapNodeKind.ReachProbe) == 0
                    || !nodeIdOf.TryGetValue(pair.Key, out var probeNode))
                {
                    continue;
                }

                markers.Add(ReachProbeAt(graph, probeNode, pair.Key));
            }

            for (var i = 0; i < leaves.Count; i++)
            {
                markers.Add(ReachProbeAt(graph, nodeIdOf[leaves[i]], leaves[i]));
            }

            // One light in the whole building, and it is the finish. The 567 ZoneLights
            // that used to come out of this loop are deleted with §04's 정비공 — see
            // MapMarkerKind. The 출입구 light stays because three things find §02's
            // finish through it (MatchMap.FindEntrance, RaceDirector's fallback, and
            // PlayerTraversal.CollectMarkers), and because in a dark maze the finish is
            // the one thing worth being able to see from across the room.
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                var node = graph.Nodes[i];
                if (!node.HasAny(MapNodeKind.Entrance))
                {
                    continue;
                }

                markers.Add(new MapMarkerPlacement(
                    MapMarkerKind.EntranceLight, node.Position, node.ZoneId, i,
                    "EntranceLight_" + graph.Zones[node.ZoneId].Name + "_" + i));
            }

            markers.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return markers.ToArray();
        }

        /// <summary>
        /// One 도달 지점 marker, named from its CELL rather than its node index.
        /// <para>
        /// The cell is what makes the name stable. A node index moves whenever the graph
        /// gains or loses a vertex anywhere earlier in the sort, so an index-named marker
        /// silently renames half the building on an unrelated edit — and every audit
        /// matches these by name. <c>(level, x, z)</c> is where the probe physically is
        /// and does not move unless the probe does.
        /// </para>
        /// <para>
        /// The prefix is the contract with three editor-side readers —
        /// <c>NavMeshConnectivity.CollectPoints</c>, <c>PlayerTraversal.CollectMarkers</c>
        /// and <c>Editor/Dressing/Reachability</c>. Change it here and all three must
        /// change in the same commit, or the audit measures a building with no probes in
        /// it and reports a cheerful 100%.
        /// </para>
        /// </summary>
        private static MapMarkerPlacement ReachProbeAt(MapGraph graph, int node, MapCell cell)
        {
            return new MapMarkerPlacement(
                MapMarkerKind.ReachProbe,
                graph.Nodes[node].Position,
                graph.Nodes[node].ZoneId,
                node,
                "ReachProbe_" + graph.Zones[graph.Nodes[node].ZoneId].Name
                + "_" + cell.Level + "_" + cell.X + "_" + cell.Z);
        }

        /// <summary>
        /// The name the ONE creature a single-agent runtime runs is found under, and the
        /// prefix every other creature's marker is built from.
        /// <para>
        /// <b>It is bare on purpose and three consumers depend on it staying bare.</b>
        /// <c>MatchMap.TryRead</c> takes <c>MonsterSpawns[0]</c> after sorting the group's
        /// children with <c>string.CompareOrdinal</c>, and a string always sorts before the
        /// strings that extend it, so the bare name wins that sort no matter how many
        /// creatures are added or what they are called. <c>MonsterShot.IsAnchor</c> matches
        /// this exact string — its own remark says why: "<c>MonsterSpawns</c> — the container
        /// — starts with <c>MonsterSpawn</c>, sits at the world origin, and won the first
        /// search outright". <c>SoloPlaytest.FindMarker</c> takes the first leaf whose name
        /// starts with it, in hierarchy order, which <c>MapSceneBuilder</c> writes in the same
        /// ordinal order.
        /// </para>
        /// <para>
        /// <b>So the count of objects named exactly this is the count of creatures the game
        /// actually runs, and the child count of the <c>MonsterSpawns</c> group is the count
        /// the map declares.</b> While those two disagree the building has creatures nothing
        /// instantiates, and both numbers are in the same scene where one assertion can
        /// compare them. That is deliberate: a map half that renamed the primary would move
        /// the shipped creature off B5 the moment it landed, without any test failing.
        /// </para>
        /// </summary>
        private const string PrimaryMonsterSpawnName = "MonsterSpawn";

        /// <summary>
        /// Puts a <see cref="MapMarkerKind.MonsterSpawn"/> at every declared start — all of
        /// them, in declaration order.
        /// <para>
        /// <b>What this unblocks.</b> §12-B③ 「괴물이 안쪽을 순찰한다」 is written about every
        /// floor, and <c>NavMeshConnectivity.MeasureMonsterReach</c> already loops storeys and
        /// names the ones with no creature. With one marker it could only ever measure one
        /// floor; with eight it measures eight, and its <c>MonsterUnreachable</c> list is part
        /// of <c>Report.Passed</c>. Seven of this tower's floors have never had monster reach
        /// measured, so this can newly FAIL, and a floor whose inner rings the agent (radius
        /// 0.5 m, height 2.0 m, climb 0.75 m — bigger than the runner's capsule in every
        /// dimension but step) cannot get to is a discovery rather than a regression.
        /// </para>
        /// <para>
        /// <b>Naming.</b> The last declaration is the primary and gets
        /// <see cref="PrimaryMonsterSpawnName"/> bare; the rest get
        /// <c>MonsterSpawn_&lt;zone&gt;_&lt;node&gt;</c>. Every one of them still contains
        /// "MonsterSpawn", which is what <c>NavMeshConnectivity.CollectPoints</c> and
        /// <c>MeasureMonsterReach</c> match on (<c>IndexOf</c>, ordinal-ignore-case), so all
        /// eight are collected as origins on their own storeys. The node id is in the name
        /// because it is unique per building: <c>MapSceneBuilder.Child</c> is find-or-create,
        /// so two markers sharing a name would silently become one object and the map would
        /// lose a creature without a count changing anywhere.
        /// </para>
        /// <para>
        /// <b>A declared start that is not a node throws.</b> The code this replaced fell back
        /// to "as far from the 출입구 as the building allows" whenever the lookup missed, which
        /// on a tower is a different STOREY — the creature moves floors and nothing fails.
        /// That fallback is right for a sketch that declares nothing at all, and wrong for one
        /// that declared a place the graph does not have; <see cref="Build"/> already refuses a
        /// <see cref="Mark"/> on a straight-through cell for exactly this reason.
        /// </para>
        /// </summary>
        /// <param name="markers">The marker list being built.</param>
        /// <param name="graph">The finished graph — positions and zone names come from it.</param>
        /// <param name="nodeIdOf">Cell → node id, so a declared cell can be checked against the graph.</param>
        /// <param name="entrance">§01's 출입구 node, used only by the no-declaration fallback.</param>
        /// <exception cref="MapSketchException">A start is not a graph node, or two name the same cell.</exception>
        private void BuildMonsterSpawns(
            List<MapMarkerPlacement> markers,
            MapGraph graph,
            Dictionary<MapCell, int> nodeIdOf,
            int entrance)
        {
            if (_monsterStarts.Count == 0)
            {
                // §07 초저녁 patrols one zone, and starting the monster on top of the door
                // would spend the quiet opening the whole time curve needs. Unchanged: this
                // is what every map that declares no start still gets.
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
                    MapMarkerKind.MonsterSpawn, graph.Nodes[farthest].Position, graph.Nodes[farthest].ZoneId,
                    farthest, PrimaryMonsterSpawnName));
                return;
            }

            var seen = new HashSet<MapCell>();
            for (var i = 0; i < _monsterStarts.Count; i++)
            {
                var cell = _monsterStarts[i];

                if (!seen.Add(cell))
                {
                    throw new MapSketchException(
                        "Two creatures are declared at " + cell + ". §12-B③ puts one on each floor's inner "
                        + "rings, so two on one cell is a floor somewhere with none — and because the scene "
                        + "builder is find-or-create by name, the duplicate would collapse into a single "
                        + "object and the map would carry one creature fewer than it says it does.");
                }

                if (!nodeIdOf.TryGetValue(cell, out var node))
                {
                    throw new MapSketchException(
                        "A creature is declared at " + cell + ", but the corridor runs straight through that "
                        + "cell, so the graph has no node there and there is nothing to hang the spawn on. Put "
                        + "it on a bend, a junction or an end. This used to fall back on 'as far from the 출입구 "
                        + "as the building allows', which on §01's tower is another STOREY — the creature would "
                        + "move floors and the §06 audit would report a floor it was never on.");
                }

                // Ordinal-last for everything but the primary: CompareOrdinal puts the bare
                // name first, which is what pins MatchMap's MonsterSpawns[0] to this cell.
                var isPrimary = i == _monsterStarts.Count - 1;
                var name = isPrimary
                    ? PrimaryMonsterSpawnName
                    : PrimaryMonsterSpawnName + "_" + graph.Zones[graph.Nodes[node].ZoneId].Name + "_" + node;

                markers.Add(new MapMarkerPlacement(
                    MapMarkerKind.MonsterSpawn, graph.Nodes[node].Position, graph.Nodes[node].ZoneId, node, name));

                if (isPrimary)
                {
                    // Printed because of what sits a few lines below it in the same log.
                    // NavMeshConnectivity counts STOREYS WITH A MARKER, so the moment these
                    // markers exist its §06 line reads "over 8 of 8 storeys" — and that is a
                    // statement about the map, not about how many creatures the match runs.
                    // MatchMap.TryRead still takes MonsterSpawns[0] and MatchDirector still
                    // owns one agent, so until the runtime spawns one per marker the running
                    // game has ONE creature on eight floors while the audit above it is green.
                    // This line is the number to check that claim against; nothing inside a
                    // map sketch can see MatchDirector, so the assertion that closes the gap
                    // has to live beside the director and compare its agent count to this one.
                    UnityEngine.Debug.Log(
                        "[SceneGen] 괴물 시작점: " + _monsterStarts.Count + " declared, " + _monsterStarts.Count
                        + " MonsterSpawn markers written. Primary '" + PrimaryMonsterSpawnName + "' at "
                        + graph.Nodes[node].Position + " in " + graph.Zones[graph.Nodes[node].ZoneId].Name
                        + " — a runtime that reads MatchMap.MonsterSpawns[0] instantiates that one and no other. "
                        + "Count the agents the director ticks against " + _monsterStarts.Count
                        + " before reading the §06 storey count below as a creature count.");
                }
            }
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

        // DELETED with §08: the DeadEndRewards class. It drew one value per 막힌 길 out
        // of LootCatalogue so §12's "위험을 감수할 이유" had a number on it. It was the
        // ONLY consumer of Build's IRandomSource — measured, not assumed: `random`
        // appeared exactly twice in this file, at the constructor and at the draw, and
        // DescentMap hands Build a fresh DeterministicRandom(seed) that RadialStorey
        // never sees. So the reward stream can be removed outright without shifting one
        // cell of any authored layout, and the probe set is now fully deterministic —
        // strictly more so than the loot it replaces.
    }
}
