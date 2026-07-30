using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;

namespace HorrorGame.Sim
{
    /// <summary>
    /// The building the simulator runs matches in: §12's 첫 맵 스케치, built through
    /// Core's own <see cref="MapGraphBuilder"/> and answered through Core's own
    /// <see cref="MapGraphProbe"/>.
    /// <para>
    /// Fixed across matches on purpose. §03's 랜덤화 범위 table puts 맵 구조 in the
    /// 고정 column — "학습 가능해야 실력이 성장한다" — so a sweep that varied the map
    /// between seeds would be measuring the map generator, not the economy. What
    /// varies per seed is what §03 says varies: the objective's site, the clues and
    /// where the loot is.
    /// </para>
    /// <para>
    /// <b>Floors are zones.</b> §03's chain narrows by floor ("물이 있는 층은 지하
    /// 3층이다") and then by site sign, and §12's map is a graph of four zones. The
    /// simulator maps one onto the other — zone A is 지하 1층, D is 지하 4층 — so the
    /// first two links of the chain narrow to a zone and the third narrows to one of
    /// that zone's three 후보 지점 (§12: 구역당 3개). Nothing downstream can tell the
    /// difference, because <see cref="SiteCatalog"/> only ever compares floor
    /// indices for equality.
    /// </para>
    /// </summary>
    public sealed class SimMap
    {
        // §12's geometry, derived from the constants rather than chosen: a zone is
        // square with the mean allowed diagonal, and a ring leg is one S-corridor
        // leg plus enough slack that the ring closes.
        private const float LayoutSlack = 2f;
        private const float SpurOffset = 4f;

        private readonly int[] _siteNodes;
        private readonly int[] _floorOfZone;

        private SimMap(
            MapGraph graph,
            SiteCatalog catalog,
            int[] siteNodes,
            int[] floorOfZone,
            int entranceNode,
            Vec3 surfacePosition,
            int[] lootNodes,
            int[] safeNodes,
            int monsterSpawnNode)
        {
            Graph = graph;
            Probe = new MapGraphProbe(graph);
            World = new CachedWorldProbe(Probe);
            World.Verify();
            Catalog = catalog;
            _siteNodes = siteNodes;
            _floorOfZone = floorOfZone;
            EntranceNode = entranceNode;
            SurfacePosition = surfacePosition;
            LootNodes = lootNodes;
            SafeNodes = safeNodes;
            MonsterSpawnNode = monsterSpawnNode;
        }

        /// <summary>The graph every position in a match lives on.</summary>
        public MapGraph Graph { get; }

        /// <summary>
        /// The <see cref="Core.Session.IWorldProbe"/> the monster, the abilities and
        /// the objective layout all reason through. Core's own implementation — the
        /// simulator does not answer world queries itself, or it would be measuring
        /// its own idea of the map instead of §12's.
        /// </summary>
        public MapGraphProbe Probe { get; }

        /// <summary>
        /// The same probe with its answers precomputed. Everything that runs inside
        /// the fixed-step loop uses this; <see cref="Probe"/> is kept for the two
        /// operations that change the world rather than query it — lighting a zone
        /// and shutting a door.
        /// </summary>
        public CachedWorldProbe World { get; }

        /// <summary>The building, for §03's narrowing chain.</summary>
        public SiteCatalog Catalog { get; }

        /// <summary>The node just inside the door. §03's 왕복 turns around here.</summary>
        public int EntranceNode { get; }

        /// <summary>
        /// The vehicle. §08: "지상 차량 = 안전 지대 + 상점 + 보급소".
        /// <para>
        /// Deliberately <em>not</em> a node in the graph. §12's rules are about the
        /// building — a dead end with no reward, a 40 m straight run, a place outside
        /// its own zone — and the vehicle is none of the things those rules are
        /// judging, so putting it in the graph would fail three checks for being a car
        /// park. Agents walk to it off-graph instead.
        /// </para>
        /// <para>
        /// It sits one <see cref="GameConstants.ZoneDiagonalMax"/> from the door,
        /// which is the simulator's stand-in for §07 pricing "시각 확인하러 나가기" at
        /// about a minute: the climb has to cost something or surfacing would be free,
        /// and §07's whole argument is that it is not.
        /// </para>
        /// </summary>
        public Vec3 SurfacePosition { get; }

        /// <summary>
        /// Dead ends that carry a reward. §12: "막힌 길에 좋은 것을 둔다" — loot is
        /// placed where the map already punishes you for going.
        /// </summary>
        public int[] LootNodes { get; }

        /// <summary>Dead ends that hold a 금고 instead of a loose piece. §08 gates these behind the 정비공.</summary>
        public int[] SafeNodes { get; }

        /// <summary>Where the monster starts, and where §03's partial reset puts it back.</summary>
        public int MonsterSpawnNode { get; }

        /// <summary>The graph node a candidate site sits on.</summary>
        /// <param name="siteId">A site id from <see cref="Catalog"/>.</param>
        public int NodeOfSite(int siteId) => _siteNodes[siteId];

        /// <summary>The floor index this zone is signed with. See the note on floors above.</summary>
        public int FloorOfZone(int zoneId) => _floorOfZone[zoneId];

        /// <summary>Where a node is, in world metres.</summary>
        public Vec3 PositionOf(int nodeId) => Graph.Nodes[nodeId].Position;

        /// <summary>
        /// Builds the map. Deterministic and argument-free: every match in a sweep
        /// gets the identical building, per §03's 고정 column.
        /// </summary>
        public static SimMap Build()
        {
            var zoneSide = (GameConstants.ZoneDiagonalMin + GameConstants.ZoneDiagonalMax)
                           * 0.5f / MathF.Sqrt(2f);
            var ringSide = GameConstants.SCorridorLegLength + LayoutSlack;

            var map = new MapGraphBuilder().Named("§12 첫 맵 스케치 (sim)");

            var a = AddZoneRing(map, "A 나무", FloorMaterial.Wood, new Vec3(0f, 0f, 0f),
                MapNodeKind.MazeSpace, zoneSide, ringSide);
            var b = AddZoneRing(map, "B 타일", FloorMaterial.Tile, new Vec3(0f, 0f, zoneSide),
                MapNodeKind.OpenSpace, zoneSide, ringSide);
            var c = AddZoneRing(map, "C 자갈", FloorMaterial.Gravel, new Vec3(zoneSide, 0f, zoneSide),
                MapNodeKind.MazeSpace, zoneSide, ringSide);
            var d = AddZoneRing(map, "D 콘크리트", FloorMaterial.Concrete, new Vec3(zoneSide, 0f, 0f),
                MapNodeKind.MazeSpace, zoneSide, ringSide);

            var maze = MapNodeKind.MazeSpace;
            var reward = GameConstants.LootValueTrinket;

            // Gates. Three per zone so §12's 구역당 진입점 2~3 holds on every pair.
            var gateAne = Attach(map, a, 2, SpurOffset, SpurOffset, maze | MapNodeKind.ElectricalPanel, "A 북동 통로", 0);
            var gateAnw = Attach(map, a, 3, -SpurOffset, SpurOffset, maze, "A 북서 통로", 0);
            var gateAse = Attach(map, a, 1, SpurOffset, -SpurOffset, maze, "A 남동 통로", 0);
            var lootA1 = Attach(map, a, 0, -SpurOffset, -SpurOffset, MapNodeKind.None, "A 창고", reward);
            var lootA2 = Attach(map, a, 3, -SpurOffset, -SpurOffset, MapNodeKind.None, "A 다락", reward);

            var gateBsw = Attach(map, b, 0, -SpurOffset, -SpurOffset, maze | MapNodeKind.ElectricalPanel, "B 남서 통로", 0);
            var gateBse = Attach(map, b, 1, SpurOffset, -SpurOffset, maze, "B 남동 통로", 0);
            var gateBne = Attach(map, b, 2, SpurOffset, SpurOffset, maze, "B 북동 통로", 0);
            var lootB1 = Attach(map, b, 3, -SpurOffset, SpurOffset, MapNodeKind.None, "B 창고", reward);
            var lootB2 = Attach(map, b, 3, -SpurOffset, -SpurOffset, MapNodeKind.None, "B 배전실", reward);

            var gateCsw = Attach(map, c, 0, -SpurOffset, -SpurOffset, maze | MapNodeKind.ElectricalPanel, "C 남서 통로", 0);
            var gateCnw = Attach(map, c, 3, -SpurOffset, SpurOffset, maze, "C 북서 통로", 0);
            var gateCse = Attach(map, c, 1, SpurOffset, -SpurOffset, maze, "C 남동 통로", 0);
            var lootC1 = Attach(map, c, 2, SpurOffset, SpurOffset, MapNodeKind.None, "C 창고", reward);
            var lootC2 = Attach(map, c, 1, SpurOffset, SpurOffset, MapNodeKind.None, "C 자재실", reward);

            var gateDnw = Attach(map, d, 3, -SpurOffset, SpurOffset, maze | MapNodeKind.ElectricalPanel, "D 북서 통로", 0);
            var gateDne = Attach(map, d, 2, SpurOffset, SpurOffset, maze, "D 북동 통로", 0);
            var gateDsw = Attach(map, d, 0, -SpurOffset, -SpurOffset, maze, "D 남서 통로", 0);
            var lootD1 = Attach(map, d, 1, SpurOffset, -SpurOffset, MapNodeKind.None, "D 창고", reward);
            var lootD2 = Attach(map, d, 0, SpurOffset, -SpurOffset, MapNodeKind.None, "D 자재실", reward);
            var lootD3 = Attach(map, d, 1, -SpurOffset, -SpurOffset, MapNodeKind.Concealment, "D 은폐 지점", reward);

            var entrance = map.AddNode(
                d.Zone,
                new Vec3(d.CornerPositions[1].X + SpurOffset, 0f, d.CornerPositions[1].Z + (ringSide * 0.5f)),
                MapNodeKind.Entrance | MapNodeKind.MazeSpace,
                "D 출입구");
            map.Connect(d.Corners[1], entrance);
            map.Connect(d.Corners[2], entrance);

            map.Connect(gateAnw, gateBsw);
            map.Connect(gateAne, gateBse);
            map.Connect(gateBse, gateCsw);
            map.Connect(gateBne, gateCnw);
            map.Connect(gateCsw, gateDnw);
            map.Connect(gateCse, gateDne);
            map.Connect(gateDnw, gateAne);
            map.Connect(gateDsw, gateAse);

            var graph = map.Build();
            var surfacePosition = new Vec3(
                graph.Nodes[entrance].Position.X + GameConstants.ZoneDiagonalMax,
                0f,
                graph.Nodes[entrance].Position.Z);

            var floorOfZone = new int[4];
            floorOfZone[a.Zone] = -1;
            floorOfZone[b.Zone] = -2;
            floorOfZone[c.Zone] = -3;
            floorOfZone[d.Zone] = -4;

            var floors = new[]
            {
                // §03's 혼동쌍 are only load-bearing if a misread lands on a floor that
                // exists. 1↔7 (손글씨) and 6↔9 (뒤집힌 각도) are therefore both signed
                // onto real floors: a team that misreads the mapping walks confidently
                // into the wrong 층 rather than being told it was wrong.
                new FloorDescriptor(-1, ClueGlyph.Digit1, FloorFeature.Water),
                new FloorDescriptor(-2, ClueGlyph.Digit7, FloorFeature.Machinery),
                new FloorDescriptor(-3, ClueGlyph.Digit6, FloorFeature.Cold),
                new FloorDescriptor(-4, ClueGlyph.Digit9, FloorFeature.Rust),
            };

            // Three labels per floor, chosen so that a single mis-seen mark maps onto
            // another real site on the same floor (ㅁ↔ㅇ and 6↔9 both do), while the
            // 좌↔우 pair maps onto nothing — §03's one free warning.
            var labels = new[]
            {
                new SiteLabel(ClueGlyph.WingMieum, ClueGlyph.Digit6, ClueGlyph.SideLeft),
                new SiteLabel(ClueGlyph.WingMieum, ClueGlyph.Digit9, ClueGlyph.SideLeft),
                new SiteLabel(ClueGlyph.WingIeung, ClueGlyph.Digit6, ClueGlyph.SideLeft),
            };

            var blocks = new[] { a, b, c, d };
            var sites = new List<CandidateSite>();
            var siteNodes = new List<int>();

            for (var z = 0; z < blocks.Length; z++)
            {
                var block = blocks[z];
                for (var k = 0; k < GameConstants.CandidateSitesPerZone; k++)
                {
                    var node = block.Corners[k];
                    var siteId = sites.Count;
                    sites.Add(new CandidateSite(
                        siteId, block.Zone, floorOfZone[block.Zone], graph.Nodes[node].Position, labels[k]));
                    siteNodes.Add(node);
                }
            }

            var catalog = new SiteCatalog(floors, sites);

            var lootNodes = new[] { lootA1, lootA2, lootB1, lootB2, lootC1, lootC2, lootD1, lootD2, lootD3 };

            // §08 puts 금고 속 문서 behind the Engineer, so the safes go on the two
            // dead ends furthest from the door: the Engineer's income is also the
            // Engineer's exposure.
            var safeNodes = new[] { lootA2, lootB1 };

            return new SimMap(
                graph, catalog, siteNodes.ToArray(), floorOfZone,
                entrance, surfacePosition, lootNodes, safeNodes, b.Corners[2]);
        }

        private sealed class Block
        {
            public Block(int zone, int[] corners, Vec3[] cornerPositions)
            {
                Zone = zone;
                Corners = corners;
                CornerPositions = cornerPositions;
            }

            public int Zone { get; }

            public int[] Corners { get; }

            public Vec3[] CornerPositions { get; }
        }

        private static Vec3 At(Vec3 origin, float x, float z) => new Vec3(origin.X + x, 0f, origin.Z + z);

        private static Block AddZoneRing(
            MapGraphBuilder map,
            string zoneName,
            FloorMaterial floor,
            Vec3 origin,
            MapNodeKind cornerKind,
            float zoneSide,
            float ringSide)
        {
            var zone = map.AddZone(
                zoneName,
                floor,
                new Vec3(origin.X + (zoneSide * 0.5f), 0f, origin.Z + (zoneSide * 0.5f)),
                new Vec3(zoneSide, 0f, zoneSide));

            var inset = (zoneSide - ringSide) * 0.5f;
            var positions = new[]
            {
                At(origin, inset, inset),
                At(origin, inset + ringSide, inset),
                At(origin, inset + ringSide, inset + ringSide),
                At(origin, inset, inset + ringSide),
            };

            // Three 후보 지점 and one 관측 지점 per zone (§12's 검증 체크리스트). The
            // ring itself is the S-corridor: four right-angle bends, each one a
            // sight break, which is what §12's "단일 모퉁이는 실패한다" demands.
            var kinds = new[]
            {
                cornerKind | MapNodeKind.CandidateSite,
                cornerKind | MapNodeKind.CandidateSite,
                cornerKind | MapNodeKind.CandidateSite,
                cornerKind | MapNodeKind.ObservationPost,
            };

            var names = new[] { " 후보1", " 후보2", " 후보3", " 관측지점" };
            var corners = new int[4];
            for (var i = 0; i < 4; i++)
            {
                corners[i] = map.AddNode(zone, positions[i], kinds[i], zoneName + names[i]);
            }

            for (var i = 0; i < 4; i++)
            {
                var carriesDoor = i == 0;
                map.Connect(corners[i], corners[(i + 1) % 4], carriesDoor, carriesDoor ? zoneName + " 잠금문" : null);
            }

            return new Block(zone, corners, positions);
        }

        private static int Attach(
            MapGraphBuilder map, Block block, int cornerIndex, float dx, float dz,
            MapNodeKind kind, string name, int reward)
        {
            var at = block.CornerPositions[cornerIndex];
            var id = map.AddNode(block.Zone, new Vec3(at.X + dx, 0f, at.Z + dz), kind, name, reward);
            map.Connect(block.Corners[cornerIndex], id);
            return id;
        }
    }
}
