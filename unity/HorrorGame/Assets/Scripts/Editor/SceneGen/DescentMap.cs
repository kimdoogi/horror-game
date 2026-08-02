using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Session;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// 하강 — the eight-storey building §01's race runs down.
    /// <para>
    /// Every storey is the same skeleton (<see cref="RadialStorey"/>): three concentric
    /// bands, gates narrowing 4 → 2 → 1, and the 투하구 in the middle. Twenty players
    /// start on the outer ring of B1 and the first to reach the middle of B8 wins.
    /// </para>
    /// <code>
    ///  B1 [콘크리트]  ─┐  20명이 외곽 고리에서 출발
    ///  B2 [나무]      ─┤  투하구는 중심, 착지는 다음 층의 외곽
    ///  B3 [금속]      ─┤
    ///  B4 [자갈]      ─┤  괴물은 여기서 시작한다 — 절반쯤 내려간 곳
    ///  B5 [타일]      ─┤
    ///  B6 [카펫]      ─┤
    ///  B7 [물]        ─┤
    ///  B8 [흙]        ─┘  중심 = 도착점
    /// </code>
    /// <para>
    /// <b>The storeys sit directly on top of each other, and this time that is the point.</b>
    /// The building that came before spiralled, because a <see cref="MapSketch.Stair"/> needs
    /// two landings 2.5 m apart and two plans that overlap there. A <see cref="MapSketch.Chute"/>
    /// has no such constraint — it is a hole — so every floor can occupy the same square and
    /// the whole building is one tower. That is what a player should feel: eight identical
    /// rings, each one deeper, each one darker.
    /// </para>
    /// <para>
    /// <b>What differs between floors is the seed and the surface.</b> The gate bearings are
    /// seeded per storey, so the way in is somewhere else on every floor and there is no
    /// pattern to memorise once and reuse eight times — only eight layouts to learn, which is
    /// exactly the skill §01 wants to reward. The surface is §12's alphabet, one per storey,
    /// so a footstep tells you which floor somebody is on.
    /// </para>
    /// </summary>
    public static class DescentMap
    {
        /// <summary>Name the §12 reports use for this map.</summary>
        public const string MapName = "하강 — 요양원 지하 8층";

        /// <summary>Seed the shipped scene is generated from.</summary>
        public const int DefaultSeed = 20260802;

        /// <summary>Storeys. Eight, because §12 gives eight surfaces and no more.</summary>
        public const int Storeys = 8;

        /// <summary>
        /// Cells from the middle to the rim. <see cref="RadialStorey"/>'s outermost alcoves
        /// reach radius 11, so a zone has to be 23 cells square to contain a floor.
        /// </summary>
        public const int Radius = 11;

        /// <summary>Cell X and Z of every storey's middle. One tower, so it never moves.</summary>
        public const int Centre = 12;

        /// <summary>Players on the starting ring. §11's ceiling.</summary>
        public const int MaxPlayers = 20;

        /// <summary>§12's alphabet, deepest last. One per storey, so a footstep names a floor.</summary>
        private static readonly (FloorMaterial Floor, string Name)[] Storey =
        {
            (FloorMaterial.Concrete, "B1 하역장"),
            (FloorMaterial.Wood, "B2 기록보관소"),
            (FloorMaterial.Metal, "B3 기계실"),
            (FloorMaterial.Gravel, "B4 저탄장"),
            (FloorMaterial.Tile, "B5 저수조"),
            (FloorMaterial.Carpet, "B6 병동"),
            (FloorMaterial.Water, "B7 수몰층"),
            (FloorMaterial.Earth, "B8 굴착층"),
        };

        /// <summary>Builds the building. Deterministic: the same seed gives the same tower.</summary>
        public static MapSketchResult Build(int seed)
        {
            var sketch = new MapSketch()
                .Named(MapName)
                .DefaultKind(MapNodeKind.MazeSpace);

            var side = (Radius * 2) + 1;
            var corner = Centre - Radius;

            for (var level = 0; level < Storeys; level++)
            {
                sketch.AddZone(Storey[level].Name, Storey[level].Floor, level, corner, corner, side, side);
            }

            var floors = new RadialStoreyResult[Storeys];
            for (var level = 0; level < Storeys; level++)
            {
                // A seed per storey. The gate bearings are the only thing the seed decides,
                // and they are the only thing that should differ — every floor obeys the same
                // rules and none of them is a variation on the last one's layout.
                floors[level] = RadialStorey.Build(
                    sketch, level, Centre, Centre, new DeterministicRandom(seed + (level * 7919)), Storey[level].Name);
            }

            HangChutes(sketch, floors);
            MarkPlaces(sketch, floors);
            PlaceStarts(sketch, floors);

            return sketch.Build(seed, new DeterministicRandom(seed));
        }

        /// <summary>
        /// Twenty starting positions round the rim of B1, and the creature halfway down.
        /// <para>
        /// The starts are spread over the outer band's own cell list so nobody begins beside
        /// anybody: twenty players on a 80-cell ring is one every four cells, which is far
        /// enough that the first thing you see is a corridor rather than a crowd.
        /// </para>
        /// <para>
        /// The creature starts on B4, in the middle. §12-B wants the descent to get more
        /// dangerous rather than to start that way — put it on B1 and twenty people meet it
        /// at the starting line; put it on B8 and the first four floors are a walk.
        /// </para>
        /// </summary>
        private static void PlaceStarts(MapSketch sketch, RadialStoreyResult[] floors)
        {
            // Every rim cell is offered, not twenty of them. A cell only becomes a graph
            // node if it is a bend, a junction or an end — a straight stretch of rail is not
            // a place — so declaring exactly twenty landed twelve. Offering the whole ring
            // lets the generator keep whichever are real, and having more starting positions
            // than runners is what spreads twenty people round a ring instead of queueing
            // them along one arc of it.
            foreach (var cell in floors[0].Bands[floors[0].Bands.Count - 1])
            {
                sketch.PlayerStart(cell.X, cell.Z, 0);
            }

            var midway = Storeys / 2;
            sketch.MonsterStart(Centre, Centre, midway);
        }

        /// <summary>
        /// Joins each storey's middle to the rim of the one below.
        /// <para>
        /// Two per floor, landing on opposite sides. Which one you jump into decides where you
        /// start the next maze, and because the gates below are seeded independently, one of
        /// the two is usually nearer the way in — but only if you know that floor. That is the
        /// whole of §01's "맵을 아는 사람이 유리하다" expressed as two holes in the ground.
        /// </para>
        /// </summary>
        private static void HangChutes(MapSketch sketch, RadialStoreyResult[] floors)
        {
            for (var level = 0; level < Storeys - 1; level++)
            {
                var below = floors[level + 1];

                // The landing has to be a cell that exists on the rim below. The outer band's
                // own cell list is the only honest source — the zigzag means half the bearings
                // at a given radius are empty, and a chute that dropped a player into rock
                // would be a floor nobody could finish.
                var rim = below.Bands[below.Bands.Count - 1];
                var north = Pick(rim, +1);
                var south = Pick(rim, -1);

                sketch.Chute(Centre, Centre - 1, level, north.X, north.Z, "투하구 " + (level + 1) + "북");
                sketch.Chute(Centre, Centre + 1, level, south.X, south.Z, "투하구 " + (level + 1) + "남");
            }
        }

        /// <summary>
        /// Which of a storey's concentric rings a cell sits on.
        /// <para>
        /// Chebyshev distance from the middle, because that is the metric
        /// <see cref="RadialStorey"/> lays its bands out in — a "ring" there is a square, so
        /// Euclidean distance would call the middle of a side and its corner different rings
        /// and put two 후보 지점 on the same band.
        /// </para>
        /// <para>
        /// Measured from the floor's own centre rather than from <see cref="Centre"/>, so the
        /// answer stays right if a storey is ever built off the tower's axis.
        /// </para>
        /// </summary>
        private static int RingOf(RadialStoreyResult floor, MapCell cell) =>
            System.Math.Max(
                System.Math.Abs(cell.X - floor.CentreX),
                System.Math.Abs(cell.Z - floor.CentreZ));

        /// <summary>
        /// The cell this storey's one 문 hangs in, or null if it has none.
        /// <para>
        /// <see cref="RadialStoreyResult.Gates"/> runs innermost first — 안쪽 · 중간 · 외곽 —
        /// and <see cref="RadialStorey"/> hangs the storey's single door on the FIRST gate of
        /// the 중간 band, for reasons it writes out at length: at the rim a door is one of
        /// four parallel ways in and costs nobody a detour, and on the single 안쪽 gate it
        /// does not charge a detour either, it deletes the middle. So there is exactly one
        /// door per floor and it is <c>Gates[1][0]</c>.
        /// </para>
        /// </summary>
        private static MapCell? DoorGate(RadialStoreyResult floor) =>
            floor.Gates.Count > 1 && floor.Gates[1].Count > 0
                ? floor.Gates[1][0]
                : (MapCell?)null;

        /// <summary>
        /// True when a gate mouth is the cell one step outside the storey's 문 leaf.
        /// <para>
        /// A gate runs from its mouth inward one cell per step, so the mouth of the gate the
        /// door hangs in is its orthogonal neighbour and nothing else on the floor is.
        /// Measured, seed 20260802: this matches exactly one mouth on each of the eight
        /// storeys — (6,11) against the 문 at (7,11) on B1·B4·B6, (12,6) against (12,7) on
        /// B2·B5, (10,6) against (10,7) on B3·B7·B8.
        /// </para>
        /// </summary>
        private static bool IsDoorMouth(RadialStoreyResult floor, MapCell mouth)
        {
            var gate = DoorGate(floor);
            return gate.HasValue
                && System.Math.Abs(mouth.X - gate.Value.X) + System.Math.Abs(mouth.Z - gate.Value.Z) == 1;
        }

        /// <summary>The rim cell furthest along Z in the given direction.</summary>
        private static MapCell Pick(System.Collections.Generic.List<MapCell> rim, int sign)
        {
            var best = rim[0];
            foreach (var cell in rim)
            {
                if (cell.Z * sign > best.Z * sign)
                {
                    best = cell;
                }
            }

            return best;
        }

        /// <summary>
        /// Marks what §12 counts: the way out, the 관측 지점, the 배전반 and the 후보 지점.
        /// <para>
        /// §03's clue chain is gone, so a 후보 지점 is no longer a place the objective might
        /// be — it is a junction worth naming, and the validator still wants three per zone
        /// with two exits each, which on a ring floor is a useful shape to guarantee.
        /// </para>
        /// <para>
        /// <b>These marks are also the only evidence the map has that a storey is whole, and
        /// that is now the job they are chosen for.</b> <c>NavMeshConnectivity</c> collects
        /// markers whose names contain PlayerSpawn, MonsterSpawn, Site, Candidate, Loot,
        /// Exit, Objective or Clue. Nothing else on a storey matches: the 관측 지점, the
        /// 배전반, the 은신처 and B8's own 도착점 are named in Korean and are invisible to it,
        /// and a 투하구 is not collected either. So a floor's reachability is proved by its
        /// three 후보 지점 and by the 전리품 that land on its 막힌 길 — and by nothing else.
        /// </para>
        /// <para>
        /// <b>Which is why the 전리품 markers stay, even though §08 does not.</b> A race has
        /// no economy, and it would have been one line to stop placing them. Measured on seed
        /// 20260802, that line would have turned a 10-island FAIL into an 8-island PASS —
        /// the per-storey floor — with two eight-cell pockets of B2 and B5 still walled out
        /// of their own floors, because those four 전리품 are the only markers anywhere near
        /// them. §12's 막힌 길 are 20~25% of a floor by design; a marker on each of them is
        /// the only probe this map has on the fifth of itself that leads nowhere, and the
        /// runner test (<c>PlayerTraversal</c>) counts them as reachability too. What is
        /// deleted with §08 is the ECONOMY — the value, the van, the shop — not the probe.
        /// </para>
        /// </summary>
        private static void MarkPlaces(MapSketch sketch, RadialStoreyResult[] floors)
        {
            for (var level = 0; level < Storeys; level++)
            {
                var floor = floors[level];
                sketch.OnLevel(level);

                var label = Storey[level].Name.Substring(0, 2);

                // §02: the finish is the middle of the deepest floor. §12 calls it an 출입구
                // because that is the marker its rules are written against — and it is one:
                // the only way out of this building is down.
                if (level == Storeys - 1)
                {
                    sketch.Mark(floor.Centre, MapNodeKind.Entrance, label + "_도착점");
                }

                // Three 후보 지점 on the gate mouths — where a gate branches off its band,
                // which is the only shape on a ring floor with three ways out. The gate cells
                // themselves cannot carry a mark: they are deliberately mid-passage so a door
                // can hang on them, and MapSketch refuses a mark on a passage rather than
                // letting the §12 count come up short without anything failing.
                //
                // ONE PER RING, not the first three. RadialStorey appends its gate mouths
                // outermost band first — four 외곽, then two 중간, then one 안쪽 — so taking
                // the first three took three mouths off the 외곽 고리 and nothing else.
                // Measured, seed 20260802, all eight storeys: the three sites sat at
                // Chebyshev radius 9, 9, 9. Every storey's audited evidence was therefore
                // three points on the ring the runners start on, and the 중간 고리, the
                // 안쪽 고리 and the 중심 had no marker the audit collects — a floor whose
                // inner half was sealed off would have passed with the rim intact.
                //
                // One per ring is the same count §12 asks for and the same kind of place,
                // but it now spans the storey: r9, r6, r3 on every floor, so the audit's
                // pairs cross both walls and what they prove is 외곽 → 중간 → 안쪽 rather
                // than 외곽 → 외곽. Each is still a T-junction with three ways out, over
                // §12's two — measured on all eight storeys.
                //
                // NOT THE 문's OWN MOUTH. Taking the first mouth of each ring took, on the
                // 중간 ring, the mouth of the gate RadialStorey hangs the storey's one 문 in
                // — the door goes on that band's first gate and the mouths are appended in
                // the same order, so "first on the ring" and "beside the door" were the same
                // cell on all eight floors. That was written down as a feature. It is not.
                //
                // Measured on seed 20260802, comparing /tmp/gen_after.log:1009 with
                // /tmp/i_gen.log:511 — same seed, same geometry (both runs report the same
                // 2833 meshes / 3889024 triangles), only these three marks moved:
                //   worst snap 0.22 m → 3.90 m, and 3.90 m is 3.75 + 0.15 exactly — one
                //   storey pitch plus the height the baked surface sits above a floor
                //   (measured off NavMesh_Map_FirstSketch.asset: every navmesh vertex in the
                //   building is at one of y = 0.15, −3.60 … −26.10). A vertical metre-perfect
                //   storey, not a near miss.
                //   Seven of the eight 중간 marks — one per floor, B1…B7 — were reported in
                //   another storey's island. Every one of them was this cell. B8's was the
                //   only survivor, and only because it is the deepest floor: the snap prefers
                //   the floor BELOW (3.60 m) over the floor above (3.90 m), and B8 has none.
                //
                // The cause is not the mark's Y, which is right. It is that the mark stands
                // over a hole: NavMeshConnectivity samples with a 4 m radius and a storey is
                // 3.75 m, so a marker whose own floor is missing under it is caught by the
                // next floor down — and on a tower where all eight storeys are the same
                // square, that is the SAME cell one level away. The hole is the 문:
                // MapSceneBuilder.BuildDoor drops a whole 2.5 m walled Doorway_Frame with its
                // pivot on the cell centre instead of AlignMinCorner'd, at yaw 0 whichever
                // way the passage runs, and hangs a carving NavMeshObstacle beside it.
                //
                // <b>And fixing that door does not make this cell safe.</b> The obstacle
                // carves CorridorClearWidth × 0.14 m grown by the agent radius — measured off
                // the scene (Markers/Door_(7,11@L0) at x 18.75, its Hinge child at 17.65, so
                // CorridorClearWidth is 2.2 m) that is 3.2 × 1.14 m of surface removed every
                // time somebody shuts the door, reaching 1.6 m along a passage whose navmesh
                // is only about 1.2 m wide. A 후보 지점 one cell from the leaf is a probe
                // whose answer depends on whether a door is open. §12's whole reason for
                // refusing a mark on a 병목 passage applies here at one cell's remove.
                //
                // So: one per ring still, but on the 중간 ring take the mouth of the OTHER
                // gate. Measured, seed 20260802, that is (18,12) on B1·B4·B6 — a
                // JunctionCross4Way, four ways out against §12's two — and (13,18) on B2·B5
                // and (18,14) on B3·B7·B8, both JunctionT. All eight are real junction
                // geometry in the scene and all eight sit on baked surface.
                //
                // This does not hide the 문. The audit already catches it without a marker
                // on top of it: with the 문 present the report is 10 islands / 104 partial
                // pairs, and the two extra islands are four 전리품 sealed into pockets on B2
                // and B5 — the two floors whose door cell is (12,7). Hidden and re-baked it
                // is 8 islands / 0 partial (RadialStorey records the measurement). Both of
                // those numbers were taken with the 후보 지점 nowhere near a door.
                var rings = new System.Collections.Generic.List<int>();
                var sites = new System.Collections.Generic.List<MapCell>();

                while (sites.Count < GameConstants.CandidateSitesPerZone)
                {
                    // Outermost ring first, so 관문1 is the one a runner meets first.
                    var pick = -1;
                    var picked = -1;
                    for (var i = 0; i < floor.GateMouths.Count; i++)
                    {
                        if (IsDoorMouth(floor, floor.GateMouths[i]))
                        {
                            continue;
                        }

                        var ring = RingOf(floor, floor.GateMouths[i]);
                        if (ring <= picked || rings.Contains(ring))
                        {
                            continue;
                        }

                        picked = ring;
                        pick = i;
                    }

                    if (pick < 0)
                    {
                        break;
                    }

                    rings.Add(picked);
                    sites.Add(floor.GateMouths[pick]);
                }

                // A storey with fewer rings than §12 wants sites still has to carry three:
                // MapValidator compares the count exactly, and one short zone fails the whole
                // building. Topping up from the mouths that are left keeps that a question
                // about the layout rather than a map that will not build.
                //
                // The 문's own mouth is allowed here and only here, as the last cell on the
                // list. Reaching it means the floor has run out of junctions, which is a
                // layout to go and look at rather than a map to refuse — and it will not go
                // unnoticed, because a mark on that cell is exactly what the audit reports as
                // a one-storey snap. Seed 20260802 never reaches this loop: every storey
                // offers mouths on all three rings.
                for (var i = 0;
                     i < floor.GateMouths.Count && sites.Count < GameConstants.CandidateSitesPerZone;
                     i++)
                {
                    if (!sites.Contains(floor.GateMouths[i]))
                    {
                        sites.Add(floor.GateMouths[i]);
                    }
                }

                for (var i = 0; i < sites.Count; i++)
                {
                    sketch.Mark(sites[i], MapNodeKind.CandidateSite, label + "_관문" + (i + 1));
                }

                // One 관측 지점 and one 배전반 per zone, both on alcoves — the only places on
                // a ring floor where standing still does not block somebody.
                if (floor.Alcoves.Count >= 2)
                {
                    sketch.Mark(floor.Alcoves[0], MapNodeKind.ObservationPost, label + "_관측");
                    sketch.Mark(floor.Alcoves[floor.Alcoves.Count / 2], MapNodeKind.ElectricalPanel, label + "_배전반");
                }

                // §12 wants a 은폐 지점 near the way out. On B8 that is the finish; elsewhere
                // it is the last alcove before the middle.
                if (floor.Alcoves.Count >= 3)
                {
                    sketch.Mark(floor.Alcoves[floor.Alcoves.Count - 1], MapNodeKind.Concealment, label + "_은신처");
                }
            }
        }
    }
}
