using HorrorGame.Core.Map;
using HorrorGame.Core.Session;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// §12's 첫 맵 스케치, as cells.
    /// <para>
    /// The document draws it and the drawing is the specification:
    /// </para>
    /// <code>
    ///         ┌──────────────┐
    ///         │   B 타일      │  ← 개방 공간 (홀), 시야 20m
    ///         │  [관측지점]    │
    ///         └───┬──────┬───┘
    ///       S자통로│      │순환로
    ///         ┌───┴──┐ ┌─┴────┐
    ///         │A 나무 │ │C 자갈 │
    ///         │[단서] │ │[단서] │
    ///         └───┬──┘ └──┬───┘
    ///             │  [문]  │      ← 정비공 병목
    ///         ┌───┴───────┴───┐
    ///         │   D 콘크리트    │
    ///         │   [출구]        │
    ///         └────────────────┘
    /// </code>
    /// <para>
    /// All four zones are built, not just §14's B + A. Two reasons. §12's own 수치
    /// 규칙 puts the legal zone count at 4~6 and explains why — three clues have to
    /// narrow 층 → 구역 → 지점 (§03) — so a two-zone map fails validation on the
    /// document's own arithmetic and could never be graded. And once the map is
    /// generated rather than modelled, C and D cost minutes rather than days: §14's
    /// "구역 3개 (수작업)" was budgeting for hand-building, which is the thing this
    /// tool removes. §14's prototype slice is a subgraph of what comes out — B, A and
    /// the corridor between them — so nothing here delays it.
    /// </para>
    /// <para>
    /// Layout conventions: cells are 2.5 m, the map runs 0~77.5 m east and 5~80 m
    /// north, and every zone is 12 × 10 cells = 30 × 25 m, whose 39.05 m diagonal
    /// sits inside §12's 30~40 m band with room for the Listener's error radius.
    /// </para>
    /// </summary>
    public static class FirstMapSketch
    {
        /// <summary>Name the §12 reports use for this map.</summary>
        public const string MapName = "§12 첫 맵 스케치 (B+A+C+D)";

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

            // ── Zones ────────────────────────────────────────────────────────
            // §12 청음사: four surfaces, four zones, no two alike. The order matches
            // the document's own table so zone ids read A=0, B=1, C=2, D=3.
            sketch.AddZone("A 나무", FloorMaterial.Wood, 0, 12, 12, 10);
            sketch.AddZone("B 타일", FloorMaterial.Tile, 9, 22, 12, 10);
            sketch.AddZone("C 자갈", FloorMaterial.Gravel, 19, 12, 12, 10);
            sketch.AddZone("D 콘크리트", FloorMaterial.Concrete, 9, 2, 12, 10);

            BuildZoneA(sketch);
            BuildZoneB(sketch);
            BuildZoneC(sketch);
            BuildZoneD(sketch);

            MarkZoneA(sketch);
            MarkZoneB(sketch);
            MarkZoneC(sketch);
            MarkZoneD(sketch);

            // §12 정비공: "구역당 잠글 수 있는 문 1~2개 — 많으면 정비공이 만능이 된다."
            // Each of these sits mid-passage on a ring, so shutting one forces the
            // long way round; the validator checks that the detour is worth more than
            // AggroReleaseLineOfSightBreak at the monster's speed.
            sketch.Door(4, 16);    // A — the inner link, cut and the ring is the only way
            sketch.Door(11, 22);   // B — the south strip between the A crossings and the hall
            sketch.Door(26, 16);   // C — mirror of A
            sketch.Door(14, 3);    // D — the exit ring's long side, §12's [문] in the sketch

            return sketch.Build(seed, new DeterministicRandom(seed));
        }

        // ====================================================================
        // A 나무 — 미로 공간. §12's sketch puts [단서] here and joins it to B by the
        // S자 통로. Cells x 0..11, z 12..21.
        // ====================================================================

        private static void BuildZoneA(MapSketch s)
        {
            // The 순환로. Its bottom, right and left sides are left unsplit on purpose:
            // three consecutive ring sides of 17.5 m / 12.5 m / 12.5 m are what
            // MapGraph.FindSCorridor reads as an S자 통로, because both outer legs clear
            // §12's 10 m and both bends are right angles. Hanging a junction in the
            // middle of one of them would quietly delete the zone's only S.
            s.Corridor(1, 14, 8, 14);    // bottom, 17.5 m
            s.Corridor(8, 14, 8, 19);    // right, 12.5 m
            s.Corridor(8, 19, 1, 19);    // top, 17.5 m — the side that carries the spurs
            s.Corridor(1, 19, 1, 14);    // left, 12.5 m

            // Inner link: a second 순환로 inside the zone, so a Runner cornered in A has
            // somewhere to go without leaving it (§12 구역당 1+).
            s.Corridor(3, 19, 3, 16);
            s.Corridor(3, 16, 6, 16);
            s.Corridor(6, 16, 6, 19);

            // North spine to B — §12's S자 통로 in the sketch. Two right-angle bends
            // 5 m and 10 m apart, then the two crossings into B.
            s.Corridor(6, 19, 6, 21);
            s.Corridor(6, 21, 10, 21);

            // South spine to D.
            s.Corridor(8, 14, 8, 12);
            s.Corridor(8, 12, 11, 12);

            // 막힌 길 (§12 20~25%, each rewarded) and the 관측 지점 alcove. Every spur
            // keeps a clear cell between itself and any other passage: two corridors
            // one cell apart are adjacent on the grid, which would silently weld them
            // into a room and turn the 막힌 길 into a loop.
            s.Corridor(2, 19, 2, 21);    // loot spur
            s.Corridor(4, 19, 4, 21);    // loot spur
            s.Corridor(10, 21, 10, 19);  // loot spur
            s.Corridor(10, 12, 10, 14);  // 관측 지점 — a barred alcove off the south spine
            s.Corridor(1, 14, 1, 12);    // loot spur, hung on the corner so the S survives
        }

        private static void MarkZoneA(MapSketch s)
        {
            // §12 단서 · 목표물 후보: three per zone, every one with 탈출로 2개 이상.
            // Which one is live is chosen per match on the host and never written into
            // the scene (§13) — all three are generated identically.
            s.Mark(1, 14, MapNodeKind.CandidateSite, "A_Candidate_SW");
            s.Mark(1, 19, MapNodeKind.CandidateSite, "A_Candidate_NW");
            s.Mark(3, 16, MapNodeKind.CandidateSite, "A_Candidate_Inner");
            s.Mark(10, 14, MapNodeKind.ObservationPost, "A_ObservationPost");
            s.Mark(8, 14, MapNodeKind.ElectricalPanel, "A_ElectricalPanel");
        }

        // ====================================================================
        // B 타일 — 개방 공간. The 20 × 20 hall is the only place on the map where
        // aggro can be taken from the 15~25 m §12's table calls safe. Cells
        // x 9..20, z 22..31.
        // ====================================================================

        private static void BuildZoneB(MapSketch s)
        {
            // The hall itself: one 20 × 20 piece over cells 11..18 × 23..30, so the
            // tiler lays nothing inside it.
            s.Room(MapKitPiece.HallOpen20x20, 11, 23, 8, 8, 0f);

            // Inside the hall the graph is a ring joining the four doorways the piece
            // actually has. Every node on it is 개방 공간, which is what makes
            // RunnerTest refuse to count a bend here as cover: §12 gives the hall a
            // 20 m sight line on purpose, and a corner drawn inside one hides nobody.
            s.Corridor(13, 25, 16, 25);
            s.Corridor(16, 25, 16, 28);
            s.Corridor(16, 28, 13, 28);
            s.Corridor(13, 28, 13, 25);

            // Spokes out to the hall's four docks.
            s.Corridor(13, 22, 13, 25);
            s.Corridor(16, 31, 16, 28);
            s.Corridor(10, 25, 13, 25);

            // West side: the two crossings down into A, and the strip that ties them
            // to the hall's south door.
            s.Corridor(9, 22, 9, 25);
            s.Corridor(9, 25, 10, 25);
            s.Corridor(9, 22, 13, 22);

            // East side: B's own S자 통로 — 10 m up, 2.5 m across, 12.5 m up, both
            // bends square. The second leg is where the 관측 갤러리 sits.
            s.Corridor(19, 22, 19, 26);
            s.Corridor(19, 26, 20, 26);
            s.Corridor(20, 26, 20, 31);
            s.Room(MapKitPiece.ObservationPostGallery, 20, 26, 1, 6, 0f);

            // The 순환로 back to C and round the north. The hall keeps three of its
            // eight docks: a fourth on the east wall would have to run up the cell
            // beside the gallery, and two passages one cell apart are one passage.
            s.Corridor(19, 22, 20, 22);
            s.Corridor(13, 31, 19, 31);
            s.Corridor(19, 31, 20, 31);

            // 막힌 길: two alcoves off the hall floor and two off the corridors.
            s.Corridor(9, 25, 9, 27);
            s.Corridor(13, 22, 15, 22);
            s.Corridor(16, 25, 16, 24);
            s.Corridor(13, 28, 12, 28);
        }

        private static void MarkZoneB(MapSketch s)
        {
            s.Mark(13, 25, MapNodeKind.OpenSpace, "B_Hall_SW");
            s.Mark(16, 25, MapNodeKind.OpenSpace, "B_Hall_SE");
            s.Mark(16, 28, MapNodeKind.OpenSpace, "B_Hall_NE");
            s.Mark(13, 28, MapNodeKind.OpenSpace, "B_Hall_NW");

            s.Mark(9, 25, MapNodeKind.CandidateSite, "B_Candidate_W");
            s.Mark(13, 22, MapNodeKind.CandidateSite, "B_Candidate_S");
            s.Mark(16, 31, MapNodeKind.CandidateSite, "B_Candidate_N");

            // §12's sketch marks [관측지점] in B and nowhere else. The gallery runs the
            // length of the S-corridor's second leg, looking back down it.
            s.Mark(20, 26, MapNodeKind.ObservationPost, "B_ObservationPost_Gallery");
            s.Mark(19, 22, MapNodeKind.ElectricalPanel, "B_ElectricalPanel");
        }

        // ====================================================================
        // C 자갈 — 미로 공간, A mirrored about the map's spine. §12's sketch joins it
        // to B by a 순환로 rather than an S. Cells x 19..30, z 12..21.
        // ====================================================================

        private static void BuildZoneC(MapSketch s)
        {
            s.Corridor(22, 14, 29, 14);
            s.Corridor(22, 14, 22, 19);
            s.Corridor(22, 19, 29, 19);
            s.Corridor(29, 19, 29, 14);

            s.Corridor(27, 19, 27, 16);
            s.Corridor(24, 16, 27, 16);
            s.Corridor(24, 16, 24, 19);

            s.Corridor(24, 19, 24, 21);
            s.Corridor(19, 21, 24, 21);

            s.Corridor(22, 14, 22, 12);
            s.Corridor(19, 12, 22, 12);

            s.Corridor(28, 19, 28, 21);
            s.Corridor(26, 19, 26, 21);
            s.Corridor(20, 21, 20, 19);
            s.Corridor(20, 12, 20, 14);
            s.Corridor(29, 14, 29, 12);
        }

        private static void MarkZoneC(MapSketch s)
        {
            s.Mark(29, 14, MapNodeKind.CandidateSite, "C_Candidate_SE");
            s.Mark(29, 19, MapNodeKind.CandidateSite, "C_Candidate_NE");
            s.Mark(27, 16, MapNodeKind.CandidateSite, "C_Candidate_Inner");
            s.Mark(20, 14, MapNodeKind.ObservationPost, "C_ObservationPost");
            s.Mark(22, 14, MapNodeKind.ElectricalPanel, "C_ElectricalPanel");
        }

        // ====================================================================
        // D 콘크리트 — the way out. §02 makes leaving the building the win condition
        // and §07 새벽 makes the door an ambush, so this is the zone that has to hold
        // both the 출입구 and somewhere to wait beside it. Cells x 9..20, z 2..11.
        // ====================================================================

        private static void BuildZoneD(MapSketch s)
        {
            s.Corridor(12, 3, 17, 3);
            s.Corridor(17, 3, 17, 9);
            s.Corridor(17, 9, 12, 9);
            s.Corridor(12, 9, 12, 3);

            // North-west link up into A.
            s.Corridor(11, 9, 12, 9);
            s.Corridor(11, 9, 11, 11);
            s.Corridor(10, 11, 11, 11);

            // North-east link up into C. Its long leg is also D's S자 통로: 10 m down
            // the ring's right side, 2.5 m across, 15 m north.
            s.Corridor(17, 5, 19, 5);
            s.Corridor(19, 5, 19, 11);
            s.Corridor(19, 11, 20, 11);

            // 출입구 — the metal stair to the surface. §12's floor table gives stairs
            // 금속, the clearest surface the Listener has, so the way out is also the
            // loudest thing to walk on.
            s.Corridor(15, 9, 15, 11);
            s.Room(MapKitPiece.StairwellMetal, 14, 10, 2, 2, 0f);

            // 은폐 지점 within reach of the door (§12's last checklist item, for §07 새벽).
            s.Corridor(14, 9, 14, 7);

            // 관측 지점 and 막힌 길.
            s.Corridor(15, 3, 15, 5);
            s.Corridor(12, 3, 12, 2);
            s.Corridor(17, 3, 18, 3);
            s.Corridor(17, 9, 17, 11);
        }

        private static void MarkZoneD(MapSketch s)
        {
            s.Mark(12, 3, MapNodeKind.CandidateSite, "D_Candidate_SW");
            s.Mark(17, 3, MapNodeKind.CandidateSite, "D_Candidate_SE");
            s.Mark(12, 9, MapNodeKind.CandidateSite, "D_Candidate_NW");
            s.Mark(15, 5, MapNodeKind.ObservationPost, "D_ObservationPost");
            s.Mark(17, 9, MapNodeKind.ElectricalPanel, "D_ElectricalPanel");
            s.Mark(14, 7, MapNodeKind.Concealment, "D_Concealment");
            s.Mark(15, 11, MapNodeKind.Entrance | MapNodeKind.Stairwell, "D_Entrance");
        }
    }
}
