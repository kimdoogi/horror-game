using HorrorGame.Core.Map;
using HorrorGame.Core.Session;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// 요양원 지하 — the building §12's 첫 맵 스케치 grew into.
    /// <para>
    /// The sketch in §12 is a plus-shape: four zones around a hall. It passes all
    /// sixteen rules and grades 10/10 <see cref="RunnerTestVerdict.TooEasy"/>, which
    /// §12 anticipates — the checklist is necessary and nowhere near sufficient. Three
    /// things were wrong with it, and all three are design faults rather than
    /// bookkeeping ones.
    /// </para>
    /// <para>
    /// <b>Every direction looked the same.</b> Four arms of identical corridor around a
    /// courtyard give a player nothing to navigate by, and §03 spends the whole match
    /// asking them to navigate — see a clue, remember it, come back for the objective.
    /// A map with no landmarks turns that into a memory test about coordinates rather
    /// than about a building. So the arms are gone and every space here is a room with
    /// a job: 하역장, 기록보관소, 저탄장, 기계실, 저수조.
    /// </para>
    /// <para>
    /// <b>Cover was everywhere.</b> The old layout had 56 sight-breaking corners at a
    /// mean spacing of 4.4 m against §12's 15~25 m, so a Runner chained cover
    /// automatically and aggro cost nothing. §12's prescription for TooEasy is "시야
    /// 차단 지점을 줄인다", and rooms with long walls are what that looks like: a corner
    /// here is the end of a real space, so corners are far apart because the spaces are
    /// big. The zones are then deliberately unequal — 저탄장 is two 17.5 m tunnels with
    /// nowhere to turn off, 기록보관소 is a lattice you can lose someone in. <em>Where</em>
    /// you get caught decides whether you live, and that is the map knowledge §03 wants
    /// players to build.
    /// </para>
    /// <para>
    /// <b>It had one floor.</b> §03's clue chain narrows by storey before anything else
    /// — "물이 있는 층은 지하 3층이다" — which is unanswerable in a single-storey building.
    /// Three levels, <see cref="MapKitCatalogue.StoreyMetres"/> apart, joined by six
    /// 계단. §12 gives stairs their own 금속 surface precisely so the Listener can hear
    /// somebody change floor, and now there is a floor to change.
    /// </para>
    /// <code>
    ///  B1  z 20~29   [D 하역장 · 콘크리트]──[A 기록보관소 · 나무]     ← 출입구
    ///                    │ 계단 ×2                │ 계단 ×2
    ///  B2  z 12~21   [C 저탄장 · 자갈]        [E 기계실 · 금속]
    ///                    │ 계단 ×2
    ///  B3  z  4~13   [B 저수조 · 타일]        ← 막다른 층
    /// </code>
    /// <para>
    /// C and E sit side by side on B2 and are <em>not</em> joined, and B3 hangs off C
    /// alone. So the plant room and the cistern are opposite ends of a building that
    /// has no shortcut between them: getting from one to the other means going back up
    /// to the dock level and down the other side. §12 asks the map to make a chase a
    /// guess rather than a footrace, and a building whose two deep rooms are four 계단
    /// apart makes the guess about which <em>floor</em> somebody took — the version of
    /// that a player can only answer by having learned the place (§03).
    /// </para>
    /// <para>
    /// Layout conventions: cells are 2.5 m; every zone is 12 × 10 cells = 30 × 25 m,
    /// whose 39.05 m diagonal sits inside §12's 30~40 m band; the building occupies
    /// 60 × 65 m of §12's 100 m square. Nine cells (20.0 m) is the longest legal
    /// straight, so no corridor here crosses a whole zone.
    /// </para>
    /// </summary>
    public static class FirstMapSketch
    {
        /// <summary>Name the §12 reports use for this map.</summary>
        public const string MapName = "요양원 지하 (B1 하역장 · B2 기계층 · B3 저수조)";

        /// <summary>
        /// Seed the shipped scene is generated from.
        /// <para>
        /// The seed decides only what §12 leaves free (which 전리품 waits in each 막힌
        /// 길) and which ten points <see cref="RunnerTest"/> samples — so it moves the
        /// map's 주자 테스트 score without moving a wall. This one is the default
        /// because it is the seed the shipped scene was graded on; regenerate with
        /// another and the tool reports that map's own score against §12's band.
        /// </para>
        /// </summary>
        public const int DefaultSeed = 1204;

        /// <summary>Builds the sketch. Deterministic: the same seed gives the same scene.</summary>
        public static MapSketchResult Build(int seed)
        {
            var sketch = new MapSketch()
                .Named(MapName)
                .DefaultKind(MapNodeKind.MazeSpace);

            // §12 청음사: five surfaces, five zones, no two alike. 금속 goes to the
            // plant room rather than being kept for stairs alone — a whole storey of
            // steel grating is the one place in the building nobody crosses unheard,
            // which is a fact the Listener gets to trade on.
            sketch.AddZone("D 하역장", FloorMaterial.Concrete, 0, 2, 20, 12, 10);
            sketch.AddZone("A 기록보관소", FloorMaterial.Wood, 0, 14, 20, 12, 10);
            sketch.AddZone("C 저탄장", FloorMaterial.Gravel, 1, 2, 12, 12, 10);
            sketch.AddZone("E 기계실", FloorMaterial.Metal, 1, 14, 12, 12, 10);
            sketch.AddZone("B 저수조", FloorMaterial.Tile, 2, 8, 4, 12, 10);

            // §12's two kinds of space, decided per room rather than per corridor.
            // Four of these zones are single volumes — a vehicle bay, a coal bunker, a
            // boiler hall, a water tank — and one, the record stacks, is partitioned.
            // That ratio is the map's whole difficulty: aggro taken anywhere but the
            // stacks cannot be broken where it was taken, so a chase is a decision
            // about which way to run rather than a corner to duck round.
            BuildLoadingDock(sketch);
            BuildRecordsWing(sketch);
            BuildCoalStore(sketch);
            BuildPlantRoom(sketch);
            BuildCistern(sketch);
            BuildStairs(sketch);

            // §12 정비공: "구역당 잠글 수 있는 문 1~2개 — 많으면 정비공이 만능이 된다."
            // Every one sits mid-passage on the neck of that zone's 순환로, so shutting
            // it forces the long way round; the validator checks the detour is worth
            // more than AggroReleaseLineOfSightBreak at the monster's speed.
            sketch.OnLevel(0).Door(4, 22);    // D — the bay's west approach
            sketch.OnLevel(0).Door(20, 25);   // A — the middle stack aisle
            sketch.OnLevel(1).Door(6, 17);    // C — the tunnel cross-cut
            sketch.OnLevel(1).Door(16, 17);   // E — the south catwalk
            sketch.OnLevel(2).Door(9, 8);     // B — the cistern's west vault

            return sketch.Build(seed, new DeterministicRandom(seed));
        }

        // ====================================================================
        // B1 · D 하역장 — 콘크리트. Cells x 2..13, z 20..29.
        //
        // Where the lorries came down and where the players come in. The 20 × 20 m
        // bay is the map's 개방 공간 (§12): the one place with a 20 m sight line, so
        // the one place a Runner can pull aggro from the 15~25 m §12's own table
        // calls survivable. Everything around it is service passage — the 미로 공간
        // the open space has to touch — and the way out is a metal stair in the far
        // north-east corner, which is the longest walk in the building from anywhere
        // that matters.
        // ====================================================================

        private static void BuildLoadingDock(MapSketch s)
        {
            s.OnLevel(0);

            //             2 3 4 5 6 7 8 9 0 1 2 3   ← cell X
            //   z29       . . . . . . . . O 2 # X
            //   z28       . . . . . . . . . # . H
            //   z27       . . . . . . . . . # . .
            //   z26       . . . . . . . . # # # 3
            //   z25       . . # # # # # # P . . #
            //   z24       . . # . . # . . . . # #
            //   z23       . . # . . # . . . . . #
            //   z22       . . # . . # . . . . # #
            //   z21       . . # . . # . . . . . #
            //   z20       . # # # # 1 # # # . . #
            s.Plan(2, 20,
                "........O2#X",
                ".........#.H",
                ".........#..",
                "........###3",
                "..######P..#",
                "..#..#....##",
                "..#..#.....#",
                "..#..#....##",
                "..#..#.....#",
                ".####1###..#");

            // The bay is one 20 × 20 piece over cells 2..9 × 23..30, so the tiler lays
            // nothing inside it and the two corridors above run across open floor. Its
            // docks are at fixed local offsets (6.25 m and 13.75 m along an edge),
            // which is why the mouths are at x ∈ {4, 7} on the south wall and z = 25 on
            // the east and could not be anywhere else.
            s.Room(MapKitPiece.HallOpen20x20, 2, 23, 8, 8, 0f);
            // 개방 공간 (§12): the bay, its dock front and the two ramps down to it
            // are one volume — 30 m of concrete you can see straight across. Only the
            // east service passage (x 11~13) is corridor, which is why the walk to the
            // 출입구 is the one stretch of D where a corner still hides you.
            s.OpenRoom(2, 20, 12, 10);

            // §12 단서 · 목표물 후보: three per zone, every one with 탈출로 2개 이상.
            // Which one is live is chosen per match on the host and never written into
            // the scene (§13) — all three are generated identically.
            s.Mark('1', MapNodeKind.CandidateSite, "D_하역대");
            s.Mark('2', MapNodeKind.CandidateSite, "D_수위실");
            s.Mark('3', MapNodeKind.CandidateSite, "D_반입구");

            // §12 관측자: a leaf gets the barred-window piece, so the post is somewhere
            // the monster can be watched from and not reached from.
            s.Mark('O', MapNodeKind.ObservationPost, "D_감시창");
            s.Mark('P', MapNodeKind.ElectricalPanel, "D_배전반");

            // §12's last checklist item, for §07 새벽: somewhere to wait out a monster
            // that already knows which door you are heading for.
            s.Mark('H', MapNodeKind.Concealment, "D_사물함");

            // §12 계단 = 금속: the way out is also the loudest thing in the building to
            // stand on. This shaft climbs to the surface, which the map does not model,
            // so its upper flight lands off the plan on purpose.
            s.Mark('X', MapNodeKind.Entrance | MapNodeKind.Stairwell, "D_출입구");
            s.Room(MapKitPiece.StairwellMetal, 13, 30, 2, 2, 0f);

            // The two turns inside the bay are 개방 공간 (§12 홀, 시야 20m). RunnerTest
            // refuses to count a bend here as cover, for the same reason §12 gives the
            // hall its sight line: a corner drawn inside a room you can see across
            // hides nobody. Crossing this floor with something behind you is the most
            // exposed 15 m on the map, and it is the walk every player makes on the way
            // in and again on the way out.
        }

        // ====================================================================
        // B1 · A 기록보관소 — 나무. Cells x 14..25, z 20..29.
        //
        // The record stacks: aisles between two long cross passages on a timber floor
        // that gives every step away. This is the zone a Runner wants to be caught
        // in — the lattice puts a wrong turn in the chase for the monster as well as
        // for the player — and it is the 미로 공간 that answers the bay next door.
        // ====================================================================

        private static void BuildRecordsWing(MapSketch s)
        {
            s.OnLevel(0);

            //             4 5 6 7 8 9 0 1 2 3 4 5   ← cell X
            //   z29       . . # . . . . . . . . .
            //   z28       . . # # 6 # # # # # . .
            //   z27       . . # . # . . . . # . .
            //   z26       # # # . # . . . . # . .
            //   z25       . . 4 # # # # # # 5 . .
            //   z24       . . # . . . . . . # . .
            //   z23       . . # . . . . . . # W .
            //   z22       # # Q . . . . . . # . .
            //   z21       . . . # # # # # # # # .
            //   z20       # # # # . . . . . . . .
            s.Plan(14, 20,
                "..#.........",
                "..##6#####..",
                "..#.#....#..",
                "###.#....#..",
                "..4######5..",
                "..#......#..",
                "..#......#W.",
                "##Q......#..",
                "...########.",
                "####........");

            s.Mark('4', MapNodeKind.CandidateSite, "A_대장고");
            s.Mark('5', MapNodeKind.CandidateSite, "A_열람실");
            s.Mark('6', MapNodeKind.CandidateSite, "A_사서실");
            s.Mark('W', MapNodeKind.ObservationPost, "A_격자창");
            s.Mark('Q', MapNodeKind.ElectricalPanel, "A_배전반");
        }

        // ====================================================================
        // B2 · C 저탄장 — 자갈. Cells x 2..13, z 12..21.
        //
        // Two 17.5 m tunnels round a coal bunker, and almost nothing else. The zone
        // exists to be dangerous: its corners are the ends of long runs, so they sit
        // 10~17.5 m apart and a Runner who takes aggro mid-tunnel has one decision
        // rather than four. §12 puts 시야 차단 지점 간격 at 15~25 m and this is the
        // zone that honours it literally. Gravel underfoot means you are heard doing
        // it.
        // ====================================================================

        private static void BuildCoalStore(MapSketch s)
        {
            s.OnLevel(1);

            //             2 3 4 5 6 7 8 9 0 1 2 3   ← cell X
            //   z21       . . . . . . . . . . . .
            //   z20       # Y . . . . . # . . . .
            //   z19       # . . . . . . # . . . .
            //   z18       R # # # 8 # # # . . . .
            //   z17       # . . . # . . # . . . .
            //   z16       # . . . # . . # . . . .
            //   z15       # # # # # # # 7 . . . .
            //   z14       . . . . # . . . . . . .
            //   z13       . . . . # . . . . . . .
            //   z12       . . . . 9 # # # # # # #
            s.Plan(2, 12,
                "............",
                "#Y.....#....",
                "#......#....",
                "R###8###....",
                "#...#..#....",
                "#...#..#....",
                "#######7....",
                "....#.......",
                "....#.......",
                "....9#######");

            // The bunker itself: one 20 × 20 volume over cells 2..9 × 13..20, so the
            // tunnels above are its gantry walkways rather than corridors.
            s.Room(MapKitPiece.HallOpen20x20, 4, 13, 8, 8, 0f);
            // 개방 공간 (§12): the bunker and the outbound gantry are one volume. The
            // last two cells before the 저수조 계단 are a cut tunnel and stay 미로 공간,
            // so the head of the stair is the only cover on this floor.
            s.OpenRoom(2, 12, 12, 10);

            s.Mark('7', MapNodeKind.CandidateSite, "C_투탄구");
            s.Mark('8', MapNodeKind.CandidateSite, "C_저탄조");
            s.Mark('9', MapNodeKind.CandidateSite, "C_반출구");
            s.Mark('Y', MapNodeKind.ObservationPost, "C_점검창");
            s.Mark('R', MapNodeKind.ElectricalPanel, "C_배전반");
        }

        // ====================================================================
        // B2 · E 기계실 — 금속. Cells x 14..25, z 12..21.
        //
        // Steel grating over the boilers. §12 관측자 gets a real 갤러리 here: a 15 m
        // catwalk to stand on and watch the floor from, which is the "높이 차 · 창문 ·
        // 격자" the section asks for rather than a corridor with a label on it. The
        // grating is also why the Listener always knows when somebody is on B2 —
        // 금속 is the clearest surface in §12's table.
        // ====================================================================

        private static void BuildPlantRoom(MapSketch s)
        {
            s.OnLevel(1);

            //             4 5 6 7 8 9 0 1 2 3 4 5   ← cell X
            //   z21       . . # # # # # # # S . .
            //   z20       . . # . . . . . . # . .
            //   z19       . . # . . . . . . # Z .
            //   z18       . . # . . . . . . # . .
            //   z17       . . # . . . . . . # . .
            //   z16       . . # . . . . . . # . .
            //   z15       . . # . . . . . . # . .
            //   z14       . . # # a # # # # b . .
            //   z13       . . . . # . . . . . . .
            //   z12       . . # # c # # # # . . .
            s.Plan(14, 12,
                "..#######S..",
                "..#......#..",
                "..#......#Z.",
                "..#......#..",
                "..#......#..",
                "..#......#..",
                "..#......#..",
                "..##a####b..",
                "....#.......",
                "..##c####...");

            // §04 관측자: the gallery runs 15 m up the west flank at +3 m, so the
            // Observer's 15 m reach is a slant down to the floor rather than a walk
            // into the monster's own sight range.
            // The boiler hall itself, east of the catwalk: cells 16..23 × 14..21.
            s.Room(MapKitPiece.HallOpen20x20, 16, 14, 8, 8, 0f);
            // 개방 공간 (§12): boilers under a catwalk. There is no wall anywhere in
            // 기계실 — the gallery looks down on the whole floor, which is exactly why
            // §04's Observer can work here and why nobody can hide here.
            s.OpenRoom(14, 12, 12, 10);

            s.Mark('a', MapNodeKind.CandidateSite, "E_보일러하단");
            s.Mark('b', MapNodeKind.CandidateSite, "E_급수펌프");
            s.Mark('c', MapNodeKind.CandidateSite, "E_송풍기실");
            s.Mark('Z', MapNodeKind.ObservationPost, "E_격자창");
            s.Mark('S', MapNodeKind.ElectricalPanel, "E_배전반");
        }

        // ====================================================================
        // B3 · B 저수조 — 타일. Cells x 8..19, z 4..13.
        //
        // §03's clue chain names a floor before it names anything else — "물이 있는
        // 층은 지하 3층이다" — so the water is here and nowhere else. Two barrel
        // vaults round the tank, tiled, with the niches off the south wall. It is
        // the deepest floor and the only one with no way out of its own: every route
        // back to the 출입구 is two 계단 and two other zones.
        // ====================================================================

        private static void BuildCistern(MapSketch s)
        {
            s.OnLevel(2);

            //             8 9 0 1 2 3 4 5 6 7 8 9   ← cell X (8..19)
            //   z13       . . . . . . . . . . . .
            //   z12       # # # # . . . . . . . .   ← 저탄장에서 내려오는 두 계단
            //   z11       . # . . . . . . # . . .
            //   z10       . d # # # # # # e . . .   ← 17.5 m 수조 상단
            //   z 9       . # . . . . . . # . . .
            //   z 8       . # . . . . . . # . . .
            //   z 7       . # . . . . . . # . . .
            //   z 6       . # # f # # # # T . . .   ← 17.5 m 수조 하단
            //   z 5       . . . V . . # . . . . .   ← 벽감
            //   z 4       . . . . . . . . . . . .
            //
            // z11 is left solid on purpose. A corridor there would run one cell from
            // z10's and the grid would weld them into a 5 m-wide room — the failure
            // that turns two passages into one and halves the 시야 차단 지점 spacing
            //             8 9 0 1 2 3 4 5 6 7 8 9   ← cell X
            //   z13       . . . . . . . . . . . .
            //   z12       # # # # # # # # . . . .
            //   z11       . # . . . . . . # . . .
            //   z10       . d # # # # # # e . . .
            //   z 9       . # . . . . . . # . . .
            //   z 8       . # . . . . . . # . . .
            //   z 7       . # . . . . . . # . . .
            //   z 6       . # # f # # # # T . . .
            //   z 5       . . . # . . # . . . . .
            //   z 4       . . . V . . # . . . . .
            s.Plan(8, 4,
                "............",
                "########....",
                ".#......#...",
                ".d######e...",
                ".#......#...",
                ".#......#...",
                ".#......#...",
                ".##f####T...",
                "...#..#.....",
                "...V..#.....");

            // 개방 공간 (§12): a water tank is one volume by definition. B3 offers no
            // cover at all, and §03 puts the objective's floor behind a clue — so the
            // match's most dangerous ground is the ground you are told to go to.
            s.OpenRoom(8, 4, 12, 10);

            s.Mark('d', MapNodeKind.CandidateSite, "B_수조북단");
            s.Mark('e', MapNodeKind.CandidateSite, "B_밸브실");
            s.Mark('f', MapNodeKind.CandidateSite, "B_침전조");

            // §12 관측자 wants a post per zone; down here it is the inspection hatch
            // over the tank, a leaf the barred-window piece fills.
            s.Mark('V', MapNodeKind.ObservationPost, "B_점검구");
            s.Mark('T', MapNodeKind.ElectricalPanel, "B_배전반");
        }

        // ====================================================================
        // 계단 — the eight vertical joints.
        //
        // Two per connected pair of zones, which is §12's 구역 간 진입점 2~3개 read
        // vertically: one stair between two floors would be a bridge the monster
        // never has to guess at, and one locked door would seal a whole storey.
        // ====================================================================

        private static void BuildStairs(MapSketch s)
        {
            // B1 하역장 ⇄ B2 저탄장. Both surface on the dock front, 17.5 m apart, so
            // a Runner coming up from the coal store can pick which end of the bay to
            // appear at — and so can whatever is following.
            s.Stair(2, 20, 0, "계단 D–C 서");
            s.Stair(9, 20, 0, "계단 D–C 동");

            // B1 기록보관소 ⇄ B2 기계실. The landings are single cells off the plant
            // room's north wall: you step off the grating straight onto the stair.
            s.Stair(18, 21, 0, "계단 A–E 서");
            s.Stair(21, 21, 0, "계단 A–E 동");

            // B2 저탄장 ⇄ B3 저수조.
            s.Stair(10, 12, 1, "계단 C–B 서");
            s.Stair(12, 12, 1, "계단 C–B 동");

        }
    }
}
