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

    /// <summary>A zone's footprint in grid cells, alongside the §12 surface it sounds like.</summary>
    public readonly struct MapZoneRect
    {
        /// <summary>Builds a zone rect.</summary>
        public MapZoneRect(int zoneId, string name, FloorMaterial floor, int cellX, int cellZ, int cellsX, int cellsZ)
        {
            ZoneId = zoneId;
            Name = name;
            Floor = floor;
            CellX = cellX;
            CellZ = cellZ;
            CellsX = cellsX;
            CellsZ = cellsZ;
        }

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

        /// <summary>Centre in metres.</summary>
        public Vec3 Centre => new Vec3(
            (CellX + (CellsX * 0.5f)) * MapKitCatalogue.GridMetres,
            0f,
            (CellZ + (CellsZ * 0.5f)) * MapKitCatalogue.GridMetres);

        /// <summary>Full extent in metres. Y is zero: a single-storey map has no ceiling to guess (§12 prototype).</summary>
        public Vec3 Size => new Vec3(
            CellsX * MapKitCatalogue.GridMetres,
            0f,
            CellsZ * MapKitCatalogue.GridMetres);

        /// <summary>True when a cell lies in this zone.</summary>
        public bool Contains(MapCell cell) =>
            cell.X >= CellX && cell.X < CellX + CellsX && cell.Z >= CellZ && cell.Z < CellZ + CellsZ;
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
        private string _name = "unnamed map";
        private MapNodeKind _defaultKind = MapNodeKind.None;

        /// <summary>Names the map. The name appears in every §12 validator message about it.</summary>
        public MapSketch Named(string name)
        {
            _name = name;
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
        /// Declares a zone as a cell rectangle. §12 wants 4~6 of these, each 30~40 m
        /// across the diagonal and each with its own surface.
        /// </summary>
        /// <exception cref="MapSketchException">The rect overlaps a zone already declared.</exception>
        public int AddZone(string name, FloorMaterial floor, int cellX, int cellZ, int cellsX, int cellsZ)
        {
            var rect = new MapZoneRect(_zones.Count, name, floor, cellX, cellZ, cellsX, cellsZ);
            for (var i = 0; i < _zones.Count; i++)
            {
                var other = _zones[i];
                var overlapX = cellX < other.CellX + other.CellsX && other.CellX < cellX + cellsX;
                var overlapZ = cellZ < other.CellZ + other.CellsZ && other.CellZ < cellZ + cellsZ;
                if (overlapX && overlapZ)
                {
                    throw new MapSketchException(
                        "Zone " + name + " overlaps " + other.Name + ". §12 requires \"재질 경계를 명확히 할 것\": "
                        + "a footstep inside an overlap would belong to two surfaces at once and the Listener "
                        + "could not name a zone at all.");
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
                _corridor.Add(new MapCell(x0 + (stepX * i), z0 + (stepZ * i)));
            }

            return this;
        }

        /// <summary>
        /// Declares a rectangle covered by one large piece — the hall or a stairwell.
        /// Cells inside get no corridor tile; the room's own geometry covers them.
        /// </summary>
        public MapSketch Room(MapKitPiece piece, int cellX, int cellZ, int cellsX, int cellsZ, float yawDegrees)
        {
            _rooms.Add(new RoomRect(piece, cellX, cellZ, cellsX, cellsZ, yawDegrees));
            return this;
        }

        /// <summary>
        /// Marks what a place is for. The flags are what <see cref="MapValidator"/>
        /// counts per zone, so a forgotten mark is a failing rule rather than a role
        /// with nowhere to work.
        /// </summary>
        /// <exception cref="MapSketchException">The cell is not corridor.</exception>
        public MapSketch Mark(int x, int z, MapNodeKind kind, string name)
        {
            var cell = new MapCell(x, z);
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
            _doorCells.Add(new MapCell(x, z));
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

            var zoneOf = ResolveZones();
            var nodeCells = FindNodeCells();

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
                nodeIdOf[cell] = builder.AddNode(zoneOf[cell], cell.Centre, KindOf(mark), name, 0);
            }

            var edgeCells = new List<List<MapCell>>();
            var edgeEnds = new List<MapCell[]>();
            BuildEdges(nodeCells, nodeIdOf, builder, edgeCells, edgeEnds);

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
                finalBuilder.AddNode(zoneOf[cell], cell.Centre, KindOf(mark), name, reward);
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

            var graph = finalBuilder.Build();
            var tiles = BuildTiles(zoneOf, doorEdgeCells);
            var props = BuildProps(zoneOf, doorEdgeCells);
            var markers = BuildMarkers(graph, nodeIdOf, zoneOf, lootAt);
            return new MapSketchResult(seed, graph, tiles, props, markers, _zones.ToArray());
        }

        private MapNodeKind KindOf(CellMark mark)
        {
            var kind = mark.Kind;
            return (kind & MapNodeKind.OpenSpace) != 0 ? kind : kind | _defaultKind;
        }

        private static int CompareCells(MapCell a, MapCell b) =>
            a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X);

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

        private HashSet<MapCell> FindNodeCells()
        {
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

                if (count == 0)
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

        private MapTilePlacement[] BuildTiles(Dictionary<MapCell, int> zoneOf, HashSet<MapCell> doorCells)
        {
            var tiles = new List<MapTilePlacement>();
            var consumed = new HashSet<MapCell>();

            foreach (var room in _rooms)
            {
                tiles.Add(new MapTilePlacement(
                    room.Piece,
                    new MapCell(room.CellX, room.CellZ),
                    room.YawDegrees,
                    ZoneOfRoom(room, zoneOf)));

                for (var x = room.CellX; x < room.CellX + room.CellsX; x++)
                {
                    for (var z = room.CellZ; z < room.CellZ + room.CellsZ; z++)
                    {
                        consumed.Add(new MapCell(x, z));
                    }
                }
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

                var mask = NeighbourMask(cell);
                if (CountBits(mask) != 1 || doorCells.Contains(cell))
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

                var mask = NeighbourMask(cell);
                var count = CountBits(mask);

                if (doorCells.Contains(cell))
                {
                    var axis = (mask & 0b0101) != 0 ? MapDirection.East : MapDirection.North;
                    tiles.Add(new MapTilePlacement(
                        MapKitPiece.DoorwayFrame, cell, axis == MapDirection.North ? 0f : 90f, zoneOf[cell]));
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
                       && IsStraight(NeighbourMask(next)) && SameAxis(NeighbourMask(next), along))
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
                        "ElectricalPanel_" + _zones[zone].Name));
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
                    "DoorPanel_" + cell.X + "_" + cell.Z));
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
                    "LootSpawn_" + graph.Zones[graph.Nodes[node].ZoneId].Name + "_" + pair.Key.X + "_" + pair.Key.Z));
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
                    if (zoneOf.TryGetValue(new MapCell(x, z), out var zone))
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
                if (cell.X >= room.CellX && cell.X < room.CellX + room.CellsX
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

        private static MapCell MinCell(MapCell a, MapCell b) =>
            new MapCell(System.Math.Min(a.X, b.X), System.Math.Min(a.Z, b.Z));

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
            public RoomRect(MapKitPiece piece, int cellX, int cellZ, int cellsX, int cellsZ, float yawDegrees)
            {
                Piece = piece;
                CellX = cellX;
                CellZ = cellZ;
                CellsX = cellsX;
                CellsZ = cellsZ;
                YawDegrees = yawDegrees;
            }

            public MapKitPiece Piece { get; }

            public int CellX { get; }

            public int CellZ { get; }

            public int CellsX { get; }

            public int CellsZ { get; }

            public float YawDegrees { get; }
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
