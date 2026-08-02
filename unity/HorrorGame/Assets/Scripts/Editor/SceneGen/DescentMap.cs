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
                var sites = 0;
                foreach (var mouth in floor.GateMouths)
                {
                    if (sites >= 3)
                    {
                        break;
                    }

                    sketch.Mark(mouth, MapNodeKind.CandidateSite, label + "_관문" + (sites + 1));
                    sites++;
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
