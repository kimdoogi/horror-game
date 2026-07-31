using HorrorGame.Core.Map;
using HorrorGame.Core.Session;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// 요양원 지하 — the building §12's 첫 맵 스케치 grew into: five storeys, one 구역
    /// each, joined only by 계단.
    /// <para>
    /// <b>Why one zone per storey.</b> §12 asks for 4~6 구역 and gives each its own
    /// floor surface, and §12's 청음사 table has exactly five surfaces — 나무 · 타일 ·
    /// 자갈 · 콘크리트 · 금속. <see cref="MapValidator"/> rejects two zones sharing a
    /// material, so the alphabet caps the building at <b>five</b> zones however
    /// generous the 4~6 band looks. Five is therefore also the deepest legal building,
    /// and stacking them one per storey is what buys the most: §03's clue chain narrows
    /// by 층 before anything else, so here 층 and 구역 are the same narrowing and a
    /// single clue is worth a whole floor.
    /// </para>
    /// <para>
    /// <b>Why depth is the only lever left.</b> §12 also caps a zone's diagonal at 40 m,
    /// so the largest legal zone is about 756 m² and five of them about 3 780 m² — which
    /// the previous three-storey layout already spent (5 × 750 m²). <b>A §12-legal map
    /// cannot be made bigger by area at all.</b> It can be made bigger two ways, and
    /// this layout uses both: more storeys, and more corridor inside the same
    /// rectangles. The storeys matter most because they are strictly sequential — every
    /// route to 저수조 crosses four other floors — where the old layout reached its
    /// deepest zone in 115 m.
    /// </para>
    /// <code>
    ///  B1  z 29~38   [D 하역장 · 콘크리트]   출입구 · 20 m 하역 베이 · 수위실 · 유류고
    ///                     │ 계단 ×2  (베이 남쪽 서비스 통로)
    ///  B2  z 21~30   [A 기록보관소 · 나무]   서고 격자 · 예배실 · 약제실 · 폐지고
    ///                     │ 계단 ×2  (서고 남동)
    ///  B3  z 15~23   [E 기계실 · 금속]       보일러 홀 · 갤러리 · 공구실 · 배관실
    ///                     │ 계단 ×2  (기계실 남서)
    ///  B4  z  8~17   [C 저탄장 · 자갈]       저탄조 · 소각로 · 냉장고 · 코크스고
    ///                     │ 계단 ×2  (저탄장 남동)
    ///  B5  z  2~10   [B 저수조 · 타일]       수조 · 영안실 · 침전조 — 막다른 층
    /// </code>
    /// <para>
    /// <b>Every descent costs a floor.</b> The two 계단 that arrive on a storey and the
    /// two that leave it are at opposite ends of it, and the plan between them is never
    /// a straight line — §12 forbids one longer than 20 m anyway. A storey is not a
    /// landing to pass through, it is a room to cross, and crossing it is the traversal
    /// cost §07 assumes when it prices "한 층 더 탐색" at about three minutes
    /// (docs/BALANCE-FINDINGS.md F-006).
    /// </para>
    /// <para>
    /// <b>Every place has a name.</b> §03 spends the match asking players to build a
    /// mental map — see a clue, remember it, say it out loud, come back for the
    /// objective — and a corridor nobody can name is not a landmark. So the 막힌 길 §12
    /// requires at 20~25% are rooms with jobs (세탁실, 영안실, 소각로, 약제실, 공구실,
    /// 예배실…) rather than stubs, and each carries the 전리품 §12 asks for as the
    /// reason to have walked in.
    /// </para>
    /// <para>
    /// Layout conventions: cells are 2.5 m; zones are 12 × 10 or 13 × 9 cells, whose
    /// 39.05 m and 39.53 m diagonals sit inside §12's 30~40 m band; the building
    /// occupies 50 × 92.5 m of §12's 100 m square and 15 m of depth. Nine cells
    /// (20.0 m) is the longest legal straight, so no corridor here crosses a whole
    /// storey.
    /// </para>
    /// </summary>
    public static class FirstMapSketch
    {
        /// <summary>Name the §12 reports use for this map.</summary>
        public const string MapName =
            "요양원 지하 5층 (B1 하역장 · B2 기록보관소 · B3 기계실 · B4 저탄장 · B5 저수조)";

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

            // §12 청음사: five surfaces, five zones, no two alike — and one storey each,
            // so "which floor" and "which zone" are the same question. 금속 goes to the
            // plant room rather than being kept for stairs alone: a whole storey of
            // steel grating is the one place in the building nobody crosses unheard.
            sketch.AddZone("D 하역장", FloorMaterial.Concrete, 0, 16, 29, 12, 10);
            sketch.AddZone("A 기록보관소", FloorMaterial.Wood, 1, 8, 21, 12, 10);
            sketch.AddZone("E 기계실", FloorMaterial.Metal, 2, 15, 15, 13, 9);
            sketch.AddZone("C 저탄장", FloorMaterial.Gravel, 3, 8, 8, 12, 10);
            sketch.AddZone("B 저수조", FloorMaterial.Tile, 4, 14, 2, 13, 9);

            BuildLoadingDock(sketch);
            BuildRecordsWing(sketch);
            BuildPlantRoom(sketch);
            BuildCoalStore(sketch);
            BuildCistern(sketch);
            BuildStairs(sketch);

            // §12 정비공: "구역당 잠글 수 있는 문 1~2개 — 많으면 정비공이 만능이 된다."
            // Every one sits mid-passage on the neck of that storey's 순환로, so shutting
            // it forces the long way round; the validator checks the detour is worth
            // more than AggroReleaseLineOfSightBreak at the monster's speed.
            sketch.OnLevel(0).Door(17, 31);   // D — the bay's west service run
            sketch.OnLevel(1).Door(8, 27);    // A — the west stack aisle
            sketch.OnLevel(2).Door(26, 17);   // E — the east gallery, on the ring's neck
            sketch.OnLevel(3).Door(16, 16);   // C — the bunker's east tunnel
            sketch.OnLevel(4).Door(16, 5);    // B — the tank's west vault

            return sketch.Build(seed, new DeterministicRandom(seed));
        }

        // ====================================================================
        // B1 · D 하역장 — 콘크리트. Cells x 16..27, z 29..38.
        //
        // Where the lorries came down and where the players come in. The 20 × 20 m
        // bay is the map's 개방 공간 (§12): the one place with a 20 m sight line, so
        // the one place a Runner can pull aggro from the 15~25 m §12's own table
        // calls survivable. The 출입구 is in the north-west corner and the two 계단
        // down to 기록보관소 are on the bay's south service run, so the walk in and
        // the walk down are the whole length of the floor apart.
        // ====================================================================

        private static void BuildLoadingDock(MapSketch s)
        {
            s.OnLevel(0);
            //          6 7 8 9 0 1 2 3 4 5 6 7   <- cell X (16..27)
            //   z38     X # # H . . . . . . j .
            //   z37     # . . . . # # # # # # .
            //   z36     P # k . . # . . . . # .
            //   z35     # # . . . # . . . . # .
            //   z34     # . O . . # . . . . # .
            //   z33     # # # # # # . m . . # .
            //   z32     # 3 . . . 1 # # # # # .
            //   z31     . # . . . . # . . # . .
            //   z30     . # . # # # # # . # # l
            //   z29     # # # # . n . 2 # # . .
            s.Plan(16, 29,
                "X##H......j.",
                "#....######.",
                "P#k..#....#.",
                "##...#....#.",
                "#.O..#....#.",
                "######.m..#.",
                "#3...1#####.",
                ".#....#..#..",
                ".#.#####.##l",
                "####.n.2##..");

            // The bay is one 20 × 20 piece over cells 20..27 × 31..38, so the tiler
            // lays nothing inside it and the ring within it runs across open floor. Its
            // docks are at fixed local offsets (6.25 m and 13.75 m along an edge),
            // which is why the mouths are at x ∈ {22, 25} on the south wall and
            // z ∈ {33, 36} on the west and could not be anywhere else.
            //
            // Nothing stands above it. A hall is 6.3 m tall on a 3.75 m storey, so its
            // roof rises 2.55 m into the floor above and leaves any corridor up there
            // 1.99 m of headroom against a 2.30 m monster — the defect that used to
            // drop two of these rooms with a LogError on every generation (B-003).
            // This one is on the top storey and 기계실's is under 기록보관소's solid
            // ground, so both are built.
            s.Room(MapKitPiece.HallOpen20x20, 20, 31, 8, 8, 0f);

            // 개방 공간 (§12): the bay itself and nothing else. The service passages
            // west and south of it are 미로 공간, which is why the walk to the 출입구
            // is the one stretch of D where a corner still hides you.
            s.OpenRoom(20, 31, 8, 8);

            // §12 단서 · 목표물 후보: three per zone, every one with 탈출로 2개 이상.
            // Which one is live is chosen per match on the host and never written into
            // the scene (§13) — all three are generated identically.
            s.Mark('1', MapNodeKind.CandidateSite, "D_하역대");
            s.Mark('2', MapNodeKind.CandidateSite, "D_계량대");
            s.Mark('3', MapNodeKind.CandidateSite, "D_반입구");
            s.Mark('O', MapNodeKind.ObservationPost, "D_감시창");
            s.Mark('P', MapNodeKind.ElectricalPanel, "D_배전반");
            s.Mark('H', MapNodeKind.Concealment, "D_사물함");
            s.Mark('k', MapNodeKind.None, "D_세탁실");
            s.Mark('n', MapNodeKind.None, "D_수위실");
            s.Mark('m', MapNodeKind.None, "D_소독실");
            s.Mark('j', MapNodeKind.None, "D_유류고");
            s.Mark('l', MapNodeKind.None, "D_계량실");
            // §12 계단 = 금속: the way out is also the loudest thing in the building to
            // stand on. This shaft climbs to the surface, which the map does not model,
            // so its upper flight lands off the plan on purpose.
            s.Mark('X', MapNodeKind.Entrance | MapNodeKind.Stairwell, "D_출입구");
            s.Room(MapKitPiece.StairwellMetal, 16, 39, 2, 2, 0f);
        }

        // ====================================================================
        // B2 · A 기록보관소 — 나무. Cells x 8..19, z 21..30.
        //
        // The record stacks: aisles between two long cross passages on a timber floor
        // that gives every step away. This is the storey a Runner wants to be caught
        // on — the lattice puts a wrong turn in the chase for the monster as well as
        // for the player — and it is the 미로 공간 that answers the bay above. The
        // 계단 down from 하역장 land in the north-east and the ones down to 기계실
        // leave from the south-east, and there is no passage along the east wall
        // joining them: the only way between the two is out to the west stacks and
        // back, which is what makes a descent cost a floor.
        // ====================================================================

        private static void BuildRecordsWing(MapSketch s)
        {
            s.OnLevel(1);
            //          8 9 0 1 2 3 4 5 6 7 8 9   <- cell X (8..19)
            //   z30     . p . e . t . . . . # .
            //   z29     # # # # # # . # . . # .
            //   z28     # . . . # . . # . # # #
            //   z27     # . # # # . # # # # . W
            //   z26     # . # . . . # . . # . .
            //   z25     Q # 4 . 5 # # v . # . .
            //   z24     # . # . # . u . . # . .
            //   z23     # . d . # . . . . 6 # #
            //   z22     # . . . # . . . . # . .
            //   z21     # # # # # r . . # # w .
            s.Plan(8, 21,
                ".p.e.t....#.",
                "######.#..#.",
                "#...#..#.###",
                "#.###.####.W",
                "#.#...#..#..",
                "Q#4.5##v.#..",
                "#.#.#.u..#..",
                "#.d.#....6##",
                "#...#....#..",
                "#####r..##w.");

            s.Mark('4', MapNodeKind.CandidateSite, "A_대장고");
            s.Mark('5', MapNodeKind.CandidateSite, "A_열람실");
            s.Mark('6', MapNodeKind.CandidateSite, "A_사서실");
            s.Mark('W', MapNodeKind.ObservationPost, "A_격자창");
            s.Mark('Q', MapNodeKind.ElectricalPanel, "A_배전반");
            s.Mark('p', MapNodeKind.None, "A_폐지고");
            s.Mark('d', MapNodeKind.None, "A_예배실");
            s.Mark('e', MapNodeKind.None, "A_약제실");
            s.Mark('r', MapNodeKind.None, "A_등사실");
            s.Mark('t', MapNodeKind.None, "A_서고");
            s.Mark('u', MapNodeKind.None, "A_문서고");
            s.Mark('v', MapNodeKind.None, "A_필사실");
            s.Mark('w', MapNodeKind.None, "A_소독실");
        }

        // ====================================================================
        // B3 · E 기계실 — 금속. Cells x 15..27, z 15..23.
        //
        // Steel grating over the boilers. §12 관측자 gets a real 갤러리 here: a walkway
        // round the hall to watch the floor from, which is the "높이 차 · 창문 · 격자"
        // the section asks for rather than a corridor with a label on it. The grating
        // is also why the Listener always knows when somebody is on B3 — 금속 is the
        // clearest surface in §12's table, so this is the storey nobody crosses
        // unheard.
        // ====================================================================

        private static void BuildPlantRoom(MapSketch s)
        {
            s.OnLevel(2);
            //          5 6 7 8 9 0 1 2 3 4 5 6 7   <- cell X (15..27)
            //   z23     . . # # # # # # . * c # Z
            //   z22     . . # . . . . # . . # . .
            //   z21     # # S . z . # a # # # # #
            //   z20     # # # # # # # . & . . # #
            //   z19     # . s . . ! . # . . . # .
            //   z18     # q . . . . # % # # # # .
            //   z17     . . b # # # # . . . . # .
            //   z16     . # # . . . # # # # # # .
            //   z15     # # . . . . $ . . . . + .
            s.Plan(15, 15,
                "..######.*c#Z",
                "..#....#..#..",
                "##S.z.#a#####",
                "#######.&..##",
                "#.s..!.#...#.",
                "#.....#%####.",
                "..b####....#.",
                ".##...######.",
                "##....$....+.");

            // The boiler hall is cells 20..27 × 15..22, entirely west of 기록보관소's
            // footprint (x 8..19), so its 6.3 m roof rises into solid ground rather
            // than into a corridor and the room is built (see 하역장's note on B-003).
            s.Room(MapKitPiece.HallOpen20x20, 20, 15, 8, 8, 0f);
            s.OpenRoom(15, 15, 13, 9);

            s.Mark('a', MapNodeKind.CandidateSite, "E_보일러하단");
            s.Mark('b', MapNodeKind.CandidateSite, "E_급수펌프");
            s.Mark('c', MapNodeKind.CandidateSite, "E_송풍기실");
            s.Mark('Z', MapNodeKind.ObservationPost, "E_격자창");
            s.Mark('S', MapNodeKind.ElectricalPanel, "E_배전반");
            s.Mark('s', MapNodeKind.None, "E_배관실");
            s.Mark('z', MapNodeKind.None, "E_계기실");
            s.Mark('!', MapNodeKind.None, "E_급수실");
            s.Mark('$', MapNodeKind.None, "E_석탄승강기");
            s.Mark('%', MapNodeKind.None, "E_연도");
            s.Mark('&', MapNodeKind.None, "E_재받이");
            s.Mark('*', MapNodeKind.None, "E_송풍구");
            s.Mark('+', MapNodeKind.None, "E_배기실");
        }

        // ====================================================================
        // B4 · C 저탄장 — 자갈. Cells x 8..17, z 8..17.
        //
        // Long tunnels round a coal bunker, and almost nothing else. The storey exists
        // to be dangerous: its corners are the ends of long runs, so a Runner who takes
        // aggro mid-tunnel has one decision rather than four. Gravel underfoot means
        // you are heard making it. Half the 막힌 길 in the building are here, which is
        // §12's bargain stated plainly — 위험을 감수할 이유.
        // ====================================================================

        private static void BuildCoalStore(MapSketch s)
        {
            s.OnLevel(3);
            //          8 9 0 1 2 3 4 5 6 7 8 9   <- cell X (8..19)
            //   z17     # # # # # # . . # # # .
            //   z16     # . . . . # . . # . . .
            //   z15     # # # # . # # # 8 . . .
            //   z14     . . . . . . . . # Y . .
            //   z13     # # # # 7 # # # R . . .
            //   z12     # . . . # . . B . . . .
            //   z11     # . . . # A . . . C . .
            //   z10     # . . . # . . . 9 # # #
            //   z9      # . . . # . . . # . . #
            //   z8      # # # # # # # # # . D #
            s.Plan(8, 8,
                "######..###.",
                "#....#..#...",
                "####.###8...",
                "........#Y..",
                "####7###R...",
                "#...#..B....",
                "#...#A...C..",
                "#...#...9###",
                "#...#...#..#",
                "#########.D#");

            s.Mark('7', MapNodeKind.CandidateSite, "C_투탄구");
            s.Mark('8', MapNodeKind.CandidateSite, "C_저탄조");
            s.Mark('9', MapNodeKind.CandidateSite, "C_반출구");
            s.Mark('Y', MapNodeKind.ObservationPost, "C_점검창");
            s.Mark('R', MapNodeKind.ElectricalPanel, "C_배전반");
            s.Mark('A', MapNodeKind.None, "C_재처리장");
            s.Mark('B', MapNodeKind.None, "C_냉장고");
            s.Mark('C', MapNodeKind.None, "C_코크스고");
            s.Mark('D', MapNodeKind.None, "C_소각로");
        }

        // ====================================================================
        // B5 · B 저수조 — 타일. Cells x 14..26, z 2..10.
        //
        // §03's clue chain names a floor before it names anything else — "물이 있는
        // 층은 지하 5층이다" — so the water is here and nowhere else. Two barrel
        // vaults round the tank, tiled, with the mortuary off the south wall. It is
        // the deepest storey and the only one with no way out of its own: every route
        // back to the 출입구 is four 계단 and four other zones.
        // ====================================================================

        private static void BuildCistern(MapSketch s)
        {
            s.OnLevel(4);
            //          4 5 6 7 8 9 0 1 2 3 4 5 6   <- cell X (14..26)
            //   z10     . . M # # # # g . J # # #
            //   z9      . . # . . . . # . . . . #
            //   z8      # # # F . . G # # # # # #
            //   z7      . . # . . . . . . . V . #
            //   z6      . . f # # # # # h I . . #
            //   z5      . . # . . . . . # . U . #
            //   z4      . . # . . . . . # . # # #
            //   z3      . E # # # # # # # . T . K
            //   z2      . . . . . . . . . . . . .
            s.Plan(14, 2,
                "..M####g.J###",
                "..#....#....#",
                "###F..G######",
                "..#.......V.#",
                "..f#####hI..#",
                "..#.....#.U.#",
                "..#.....#.###",
                ".E#######.T.K",
                ".............");

            s.Mark('f', MapNodeKind.CandidateSite, "B_수조북단");
            s.Mark('g', MapNodeKind.CandidateSite, "B_밸브실");
            s.Mark('h', MapNodeKind.CandidateSite, "B_침전조");
            s.Mark('V', MapNodeKind.ObservationPost, "B_점검구");
            s.Mark('M', MapNodeKind.ElectricalPanel, "B_배전반");
            s.Mark('E', MapNodeKind.None, "B_세척실");
            s.Mark('F', MapNodeKind.None, "B_펌프실");
            s.Mark('G', MapNodeKind.None, "B_밸브고");
            s.Mark('I', MapNodeKind.None, "B_수문실");
            s.Mark('J', MapNodeKind.None, "B_여과실");
            s.Mark('T', MapNodeKind.None, "B_영안실");
            s.Mark('U', MapNodeKind.None, "B_약품고");
            s.Mark('K', MapNodeKind.None, "B_배수실");
        }

        // ====================================================================
        // 계단 — the eight vertical joints.
        //
        // Two per storey boundary, which is §12's 구역 간 진입점 2~3개 read vertically:
        // one stair between two floors would be a bridge the monster never has to
        // guess at, and one locked door would seal a whole storey. Each pair arrives
        // at the opposite end of the storey from the pair that leaves it, so a descent
        // is a traverse rather than a step.
        // ====================================================================

        private static void BuildStairs(MapSketch s)
        {
            // B1 하역장 ⇄ B2 기록보관소. Both surface on the bay's south service run,
            // 8 m apart, so a Runner coming up from the stacks can pick which end of
            // the run to appear at — and so can whatever is following.
            s.Stair(15, 29, 0, "계단 D–A 서");
            s.Stair(18, 30, 0, "계단 D–A 동");

            // B2 기록보관소 ⇄ B3 기계실, off the south-east of the stacks.
            s.Stair(15, 21, 1, "계단 A–E 서");
            s.Stair(18, 23, 1, "계단 A–E 동");

            // B3 기계실 ⇄ B4 저탄장, off the west flank of the boiler hall.
            s.Stair(14, 15, 2, "계단 E–C 서");
            s.Stair(18, 17, 2, "계단 E–C 동");

            // B4 저탄장 ⇄ B5 저수조, at the bottom of the coal tunnels.
            s.Stair(14, 8, 3, "계단 C–B 서");
            s.Stair(18, 10, 3, "계단 C–B 동");
        }
    }
}
