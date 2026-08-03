using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.EditorTools.SceneGen;

namespace HorrorGame.Sim
{
    /// <summary>
    /// The building the simulator runs matches in: <b>the one the game ships</b>.
    /// <para>
    /// <b>Why this file was rewritten.</b> It used to build its own four-zone ring out
    /// of <see cref="GameConstants"/> and never read the level. So on 2026-08-01 the
    /// Unity map grew from 74 places to 164 and from 133.9 m of monster route to
    /// 189.6 m, and the simulator reported a 2.5-minute median match and 1.2% reaching
    /// 심야 — identical to three significant figures, because it could not have been
    /// otherwise. F-006's own option 1 is "make traversal cost real time", the map was
    /// grown to do exactly that, and the measurement was taken against a different
    /// building (docs/BALANCE-FINDINGS.md F-006).
    /// </para>
    /// <para>
    /// It now calls <see cref="FirstMapSketch.Build"/> — the same call
    /// <c>MapSceneGenerator.Generate</c> makes before it lays a single FBX — and takes
    /// the <see cref="MapGraph"/> that comes out. Not a copy of it, not an export of
    /// it: the same sources, compiled into this assembly by HorrorGame.Sim.csproj, so
    /// "the map got bigger" and "matches got longer" are one measurement instead of
    /// two that can silently disagree.
    /// </para>
    /// <para>
    /// Fixed across matches on purpose. §03's 랜덤화 범위 table puts 맵 구조 in the
    /// 고정 column — "학습 가능해야 실력이 성장한다" — so a sweep that varied the map
    /// between seeds would be measuring the map generator, not the economy. What
    /// varies per seed is what §03 says varies: the objective's site, the clues and
    /// where the loot is.
    /// </para>
    /// <para>
    /// <b>Floors are storeys, and storeys are zones.</b> §03's chain narrows by floor
    /// ("물이 있는 층은 지하 5층이다") and then by site sign.
    /// <see cref="FirstMapSketch"/> gives each 구역 its own storey for exactly that
    /// reason, so the first two links of the chain narrow to a storey and the third
    /// narrows to one of that storey's three 후보 지점 (§12: 구역당 3개).
    /// </para>
    /// </summary>
    public sealed class SimMap
    {
        // §03 needs three marks per floor and needs a single mis-seen mark to land on
        // another real site on the same floor. ㅁ↔ㅇ and 6↔9 both do here; 좌↔우 maps
        // onto nothing, which is §03's one free warning. Reused on every storey,
        // because reuse is what makes the floor half of the chain load-bearing:
        // "ㅁ-6 좌" on its own is five places in this building.
        private static readonly SiteLabel[] SiteLabels =
        {
            new SiteLabel(ClueGlyph.WingMieum, ClueGlyph.Digit6, ClueGlyph.SideLeft),
            new SiteLabel(ClueGlyph.WingMieum, ClueGlyph.Digit9, ClueGlyph.SideLeft),
            new SiteLabel(ClueGlyph.WingIeung, ClueGlyph.Digit6, ClueGlyph.SideLeft),
        };

        // The mark each storey is signed with in its stairwells, deepest last. §03's
        // 혼동쌍 are only load-bearing if a misread lands on a floor that exists, so
        // 1↔7 (손글씨) and 6↔9 (뒤집힌 각도) are both signed onto real storeys: a team
        // that misreads the mapping walks confidently onto the wrong 층 rather than
        // being told it was wrong. The unpaired digits take the storeys below — nothing
        // misreads onto them and a misread of one lands nowhere, which is the same free
        // warning 좌↔우 gives on the site labels.
        //
        // The first four are the signs the four-zone predecessor used, in the same
        // order, so that the before/after in F-006 differs by the building and by
        // nothing else.
        //
        // 3·2·4·8 were appended on 2026-08-03 for B5~B8. The building grew to eight
        // storeys at 0ad8a30 and this table stayed at five, which threw on every
        // command and left the simulator dead for a day — CI builds HorrorGame.Sim and
        // never runs it, so nothing said so. ClueGlyphs.Digits stops at 9, so this
        // table is also the hard ceiling on storeys: a ninth needs an alphabet, not
        // another line here. SiteCatalog enforces the distinctness this relies on.
        private static readonly ClueGlyph[] FloorSigns =
        {
            ClueGlyph.Digit1,
            ClueGlyph.Digit7,
            ClueGlyph.Digit6,
            ClueGlyph.Digit9,
            ClueGlyph.Digit3,
            ClueGlyph.Digit2,
            ClueGlyph.Digit4,
            ClueGlyph.Digit8,
        };

        // How many of the 막힌 길 hold a 금고 rather than a loose piece.
        //
        // Not in GameConstants, and deliberately: §08 describes what is in a safe and
        // what opening one costs, and says nothing about how many a building has. It
        // is a property of a level, and this level does not declare any — so the
        // number lives with the code that invents it rather than being promoted into
        // the authority for numbers the game actually ships.
        //
        // Two is what the four-zone predecessor used, held deliberately: F-006's
        // before/after has to differ by the building and not by the 정비공's income.
        private const int SafesPerMap = 2;

        private readonly int[] _siteNodes;
        private readonly int[] _floorOfZone;

        private SimMap(
            MapSketchResult sketch,
            SiteCatalog catalog,
            int[] siteNodes,
            int[] floorOfZone,
            int entranceNode,
            Vec3 surfacePosition,
            int[] lootNodes,
            int[] safeNodes,
            int monsterSpawnNode)
        {
            Sketch = sketch;
            Graph = sketch.Graph;
            Probe = new MapGraphProbe(Graph);
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

        /// <summary>
        /// Everything the scene generator produced for this seed — graph, kit pieces,
        /// props and markers. Kept so the simulator can say which building it measured
        /// (<c>horrorsim map</c>) rather than asking anyone to take it on trust.
        /// </summary>
        public MapSketchResult Sketch { get; }

        /// <summary>The graph every position in a match lives on. <see cref="FirstMapSketch"/>'s, unmodified.</summary>
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

        /// <summary>Storeys in the building.</summary>
        public int StoreyCount => Catalog.Floors.Count;

        /// <summary>
        /// Storeys the objective can actually be placed on — those with a
        /// <see cref="FloorFeature"/>. <see cref="ObjectiveResolver"/> draws only from
        /// these, so when this is below <see cref="StoreyCount"/> every match measured
        /// here is a shallower descent than the building affords.
        /// <para>
        /// Public and printed because F-006 was a measurement taken against a building
        /// the game did not have, and nothing in the output said so. See
        /// <see cref="FeatureOf"/> for why the gap exists.
        /// </para>
        /// </summary>
        public int ObjectiveEligibleStoreys
        {
            get
            {
                var eligible = 0;
                for (var i = 0; i < Catalog.Floors.Count; i++)
                {
                    if (Catalog.Floors[i].Feature != FloorFeature.None)
                    {
                        eligible++;
                    }
                }

                return eligible;
            }
        }

        /// <summary>
        /// The storey-reach line every command prints, or empty when the objective can
        /// reach every storey and there is nothing to warn about.
        /// </summary>
        public string DescribeObjectiveReach()
        {
            var eligible = ObjectiveEligibleStoreys;
            if (eligible == StoreyCount)
            {
                return string.Empty;
            }

            return "  objective-eligible storeys: " + eligible + " of " + StoreyCount
                + " — the deepest " + (StoreyCount - eligible) + " have no §03 floor property, so the objective\n"
                + "  never lands there and every match here is a shallower descent than the building affords.\n"
                + "  §03 was deleted (game-design.md v1.0 · DESCENT-PIVOT §3); see SimMap.FeatureOf.\n";
        }

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
        /// placed where the map already punishes you for going. Read off the
        /// generator's own 전리품 markers, so the simulator picks up exactly what the
        /// scene spawns.
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
        /// Builds the shipped map at <see cref="FirstMapSketch.DefaultSeed"/> — the
        /// seed <c>Assets/Scenes/Map_FirstSketch.unity</c> was generated from, so the
        /// simulator and the scene are the same building down to which 전리품 waits in
        /// which 막힌 길.
        /// </summary>
        public static SimMap Build() => Build(FirstMapSketch.DefaultSeed);

        /// <summary>
        /// Builds the shipped map at an arbitrary seed. Deterministic: the same seed
        /// gives the identical building, per §03's 고정 column.
        /// </summary>
        /// <param name="seed">Passed straight to <see cref="FirstMapSketch.Build"/>.</param>
        /// <exception cref="InvalidOperationException">
        /// The generated map cannot carry §03's chain — no 출입구, a storey without its
        /// three 후보 지점, a floor surface the simulator has no §03 property for. Every
        /// message names the rule, because the alternative is a balance run against a
        /// building that quietly lost a link of the clue chain.
        /// </exception>
        public static SimMap Build(int seed)
        {
            var sketch = FirstMapSketch.Build(seed);
            var graph = sketch.Graph;

            var entrance = SingleEntrance(graph);
            var floorOfZone = SignTheStoreys(sketch, out var floors);
            var catalog = new SiteCatalog(floors, CollectSites(sketch, floorOfZone, out var siteNodes));

            var lootNodes = MarkerNodes(sketch, MapMarkerKind.LootSpawn);
            if (lootNodes.Length == 0)
            {
                throw new InvalidOperationException(
                    "The map has no 전리품 markers, so §08's economy has no income and F-006's "
                    + "'the economy cannot be tuned' would be true for the wrong reason.");
            }

            var monsterSpawn = MarkerNodes(sketch, MapMarkerKind.MonsterSpawn);
            if (monsterSpawn.Length != 1)
            {
                throw new InvalidOperationException(
                    "The map declares " + monsterSpawn.Length + " monster spawns and §06 has one monster.");
            }

            var surfacePosition = new Vec3(
                graph.Nodes[entrance].Position.X + GameConstants.ZoneDiagonalMax,
                graph.Nodes[entrance].Position.Y,
                graph.Nodes[entrance].Position.Z);

            return new SimMap(
                sketch,
                catalog,
                siteNodes,
                floorOfZone,
                entrance,
                surfacePosition,
                lootNodes,
                FurthestFromTheDoor(graph, entrance, lootNodes, SafesPerMap),
                monsterSpawn[0]);
        }

        /// <summary>
        /// Places where <see cref="MapGraph.NearestNode"/> cannot tell two storeys
        /// apart: pairs of nodes on different floors whose horizontal separation is
        /// under <paramref name="metres"/>.
        /// <para>
        /// Worth reporting rather than assuming away. <c>NearestNode</c> measures
        /// horizontally — deliberately, so that standing on a stairwell landing does
        /// not read as extra distance — and that was harmless when §12's map was one
        /// storey of four zones side by side. In a five-storey building the storeys
        /// sit on top of each other, so a position can snap onto a place two floors
        /// up, and every <see cref="Core.Session.IWorldProbe"/> answer taken at a raw
        /// position rather than at a node id inherits it. The count belongs in
        /// <c>horrorsim map</c> so a reader can weigh the numbers underneath it.
        /// </para>
        /// </summary>
        /// <param name="metres">Horizontal separation below which the two are indistinguishable.</param>
        public IReadOnlyList<string> CrossStoreyAmbiguities(float metres)
        {
            var found = new List<string>();
            var nodes = Graph.Nodes;

            for (var a = 0; a < nodes.Length; a++)
            {
                for (var b = a + 1; b < nodes.Length; b++)
                {
                    if (System.Math.Abs(nodes[a].Position.Y - nodes[b].Position.Y) < MathX.Epsilon)
                    {
                        continue;
                    }

                    var apart = Vec3.DistanceFlat(nodes[a].Position, nodes[b].Position);
                    if (apart < metres)
                    {
                        found.Add(
                            nodes[a] + " and " + nodes[b] + " are "
                            + apart.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                            + " m apart in plan on different storeys");
                    }
                }
            }

            return found;
        }

        // ====================================================================

        private static int SingleEntrance(MapGraph graph)
        {
            var entrances = graph.NodesOfKind(MapNodeKind.Entrance);
            if (entrances.Length != 1)
            {
                throw new InvalidOperationException(
                    "The map declares " + entrances.Length + " 출입구. §03's 왕복 turns around at one door and "
                    + "§08's vehicle is parked outside it, so the simulator needs exactly one.");
            }

            return entrances[0];
        }

        /// <summary>
        /// Gives every storey the two things §03's chain needs and §12's map does not
        /// carry: a property the first clue can name, and a sign the second clue can
        /// spell.
        /// <para>
        /// The property is derived from the floor surface rather than declared per
        /// storey, because §12 already guarantees the surfaces are distinct per zone —
        /// <c>MapValidator</c>'s floor-materials rule — which keeps
        /// <see cref="SiteCatalog"/>'s "두 층이 같은 속성이면 단서가 아니다" satisfied by
        /// construction rather than by care. Each pairing is the building's own: 타일 is
        /// the 저수조's wet floor (§03's "물이 있는 층"), 금속 the grating over the
        /// boilers, 자갈 the coal store with the 냉장고 in it, 나무 the one floor that can
        /// give way, 콘크리트 the loading bay and its ironwork.
        /// </para>
        /// <para>
        /// It is an <em>injection</em>, not the bijection this comment used to claim:
        /// §12's three deep surfaces map to <see cref="FloorFeature.None"/> because §03
        /// was deleted before they were authored. <see cref="FeatureOf"/> carries the
        /// reasoning and the bias that follows.
        /// </para>
        /// </summary>
        /// <param name="sketch">The generated map.</param>
        /// <param name="floors">Receives one descriptor per storey, shallowest first.</param>
        /// <returns>Floor index by zone id: 지하 1층 is −1.</returns>
        private static int[] SignTheStoreys(MapSketchResult sketch, out FloorDescriptor[] floors)
        {
            var rects = sketch.ZoneRects;
            var floorOfZone = new int[sketch.Graph.Zones.Length];
            var byLevel = new List<MapZoneRect>(rects);
            byLevel.Sort((a, b) => a.Level.CompareTo(b.Level));

            if (byLevel.Count > FloorSigns.Length)
            {
                throw new InvalidOperationException(
                    "The map has " + byLevel.Count + " storeys and only " + FloorSigns.Length
                    + " signs are written here. §03's second clue names a floor by its sign, so every storey "
                    + "needs one and no two may share.");
            }

            floors = new FloorDescriptor[byLevel.Count];
            for (var i = 0; i < byLevel.Count; i++)
            {
                var rect = byLevel[i];
                var floorIndex = -(rect.Level + 1);
                floorOfZone[rect.ZoneId] = floorIndex;
                floors[i] = new FloorDescriptor(floorIndex, FloorSigns[i], FeatureOf(rect));
            }

            return floorOfZone;
        }

        /// <summary>
        /// The §03 property a storey's floor surface gives it, or
        /// <see cref="FloorFeature.None"/> for the three surfaces added after §03 was
        /// deleted.
        /// <para>
        /// <b>Why B6~B8 are None rather than three new enum values.</b> This mapping
        /// existed to make §03's first clue discriminate ("그것은 물이 있는 층에 있다").
        /// §03 no longer says that. game-design.md v1.0 rewrote §03 into 「어둠과 시야」
        /// and DESCENT-PIVOT.md §3 lists 「§03 단서 3층위 (층→구역→지점)」 under 버린다:
        /// the destination in a race is known from the first second — it is <em>down</em>
        /// — so there is nothing left to narrow. Extending <c>FloorFeature</c> would be
        /// adding vocabulary to a mechanism the shipped game no longer reaches, and the
        /// enum lives in SiteLabel.cs, which the clean-up pass (DESCENT-PIVOT §7,
        /// 「단서 제거」) is going to delete outright.
        /// </para>
        /// <para>
        /// <see cref="SiteCatalog"/> permits this deliberately — its uniqueness rule
        /// exempts <c>None</c>, because a floor with no property is a legal floor that
        /// simply cannot host the objective. So the bijection the old doc comment
        /// promised is now an injection over the five storeys that still have one.
        /// </para>
        /// <para>
        /// <b>It costs a real bias, and the cost is printed rather than hidden.</b>
        /// <see cref="ObjectiveResolver"/> only places the objective on a storey with a
        /// property, so with three Nones the objective never lands on B6~B8 and every
        /// match measured here is a shallower descent than the building affords. That
        /// is F-006's exact pathology — measuring a building you do not have — so
        /// <c>horrorsim validate</c> and <c>horrorsim map</c> both state the eligible
        /// storey count out loud. Do not read a match length off this tool without
        /// reading that line.
        /// </para>
        /// </summary>
        private static FloorFeature FeatureOf(MapZoneRect zone)
        {
            switch (zone.Floor)
            {
                case FloorMaterial.Tile:
                    return FloorFeature.Water;
                case FloorMaterial.Metal:
                    return FloorFeature.Machinery;
                case FloorMaterial.Gravel:
                    return FloorFeature.Cold;
                case FloorMaterial.Wood:
                    return FloorFeature.Collapse;
                case FloorMaterial.Concrete:
                    return FloorFeature.Rust;

                // §12's three deep surfaces (F 병동 카펫, G 수몰층 침수, H 굴착층 흙).
                // Each has an obvious perceptible property — carpet is silence
                // underfoot, 침수 is water you cannot cross unheard, 흙 is dug earth —
                // and if §03 comes back those are the three to write. It has not.
                case FloorMaterial.Carpet:
                case FloorMaterial.Water:
                case FloorMaterial.Earth:
                    return FloorFeature.None;

                default:
                    throw new InvalidOperationException(
                        "Zone " + zone.Name + " is floored in " + zone.Floor + ", which this table has no case "
                        + "for. Add one: a surface that reaches here is a surface FirstMapSketch added after "
                        + "this file last read §12's table.");
            }
        }

        /// <summary>
        /// Turns §12's 후보 지점 into §03's catalog: three per storey, each signed so
        /// that a single mis-seen mark lands on another real site on the same storey.
        /// </summary>
        private static List<CandidateSite> CollectSites(
            MapSketchResult sketch, int[] floorOfZone, out int[] siteNodes)
        {
            var graph = sketch.Graph;
            var sites = new List<CandidateSite>();
            var nodes = new List<int>();

            for (var zone = 0; zone < graph.Zones.Length; zone++)
            {
                var inZone = graph.NodesOfKindInZone(zone, MapNodeKind.CandidateSite);
                if (inZone.Length != GameConstants.CandidateSitesPerZone)
                {
                    throw new InvalidOperationException(
                        "Zone " + graph.Zones[zone].Name + " has " + inZone.Length + " 후보 지점 and §12 asks for "
                        + GameConstants.CandidateSitesPerZone + ". §03's third clue pins one of them by its sign, "
                        + "and there are exactly that many signs.");
                }

                for (var k = 0; k < inZone.Length; k++)
                {
                    sites.Add(new CandidateSite(
                        sites.Count,
                        zone,
                        floorOfZone[zone],
                        graph.Nodes[inZone[k]].Position,
                        SiteLabels[k]));
                    nodes.Add(inZone[k]);
                }
            }

            siteNodes = nodes.ToArray();
            return sites;
        }

        private static int[] MarkerNodes(MapSketchResult sketch, MapMarkerKind kind)
        {
            var found = new List<int>();
            for (var i = 0; i < sketch.Markers.Length; i++)
            {
                if (sketch.Markers[i].Kind == kind && sketch.Markers[i].NodeId >= 0)
                {
                    found.Add(sketch.Markers[i].NodeId);
                }
            }

            found.Sort();
            return found.ToArray();
        }

        /// <summary>
        /// §08 puts 금고 속 문서 behind the 정비공, so the safes go on the dead ends
        /// furthest from the door by walking: the Engineer's income is also the
        /// Engineer's exposure.
        /// </summary>
        private static int[] FurthestFromTheDoor(MapGraph graph, int entrance, int[] candidates, int wanted)
        {
            var order = new List<int>(candidates);
            order.Sort((a, b) =>
            {
                var byDistance = graph.PathLength(entrance, b).CompareTo(graph.PathLength(entrance, a));
                return byDistance != 0 ? byDistance : a.CompareTo(b);
            });

            var take = System.Math.Min(wanted, order.Count);
            return order.GetRange(0, take).ToArray();
        }
    }
}
