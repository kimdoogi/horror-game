using HorrorGame.Core.Map;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// B6 · F 병동 — 카펫. The maze storey. Cells x 8..20, z 20..28.
    /// <para>
    /// <b>Why this floor is a lattice when the others are rings.</b> Every storey above
    /// this one is a corridor wrapped round one big room, and every storey above this
    /// one is <em>audible</em>: §04's 청음사 reads 금속 at 1.00 clarity, 타일 at 0.85,
    /// even 콘크리트 at 0.50, so "something is on B3" is a sentence somebody can say
    /// before it costs anything. 카펫 is 0.22 — under the 0.35 §04 falls back to when it
    /// has no idea at all. Down here the early warning is worse than having no floor to
    /// stand on, and the creature arrives unannounced.
    /// </para>
    /// <para>
    /// <b>So the plan has to be the warning.</b> If the team cannot hear the thing
    /// coming, the only thing that can save them is having already learned the
    /// building, and a layout only rewards that if being lost is possible: short legs,
    /// a junction every few metres, and no sight line long enough to check a room from
    /// outside it. The longest straight here is 15.0 m against §12's 20 m cap, and there
    /// are only three of them — the ward's two east wings and the west outer passage.
    /// Everywhere else the corridor turns before it has told you anything.
    /// </para>
    /// <para>
    /// <b>The fiction is the same argument.</b> A 1970s 요양원 ward is a corridor with
    /// cells off it, and a cell has a door onto the corridor rather than onto the world
    /// — 병실, 배선실, 이불창고, 세면장, 격리실. That gives §12 exactly the 막힌 길 it
    /// wants (nine of them, 23.7% of this storey's places, each carrying its 전리품) and
    /// gives §03 the named landmarks a mental map is built out of. The 간호사실 is the
    /// 관측 지점: the one room in the building whose job was already to watch the
    /// corridor through a grille.
    /// </para>
    /// <para>
    /// <b>Measured, against §12.</b> 67 corridor cells (418.75 m² of floor) resolving to
    /// 38 places and 43 passages: 6 independent 순환로 wholly inside the zone, 9 막힌 길
    /// = <b>23.7%</b> inside the 20~25% band and every one of them a named room, longest
    /// unbroken straight <b>15.0 m</b> against the 20 m cap, one S자 통로 of
    /// 10 m + 7.5 m + 10 m, and the storey's single 문 on a passage whose detour is
    /// 27.5 m against its own 7.5 m — a gain of 20.0 m where §12 needs 14.4.
    /// </para>
    /// <code>
    ///  x  8..12   병실 구획   two stacked rings of corridor; every room off them is a leaf
    ///  x 12..15   꺾인 목     the only two ways east — north over z26, south under z20
    ///  x 14..20   순환로      one ring: S자 통로 10 m × 2, and the 문 on its west neck
    /// </code>
    /// <para>
    /// <b>Footprint.</b> 13 × 9 cells = 32.5 × 22.5 m, whose 39.53 m diagonal sits
    /// inside §12's 30~40 m band — the same shape B3 기계실 uses. The brief's 13 × 12
    /// would measure 44.23 m across and fail 구역 대각선 on its own, so the maze is
    /// drawn nine rows deep and the three northern rows of the region are left as rock.
    /// The caller therefore wants
    /// <c>AddZone("F 병동", FloorMaterial.Carpet, 5, 8, 20, 13, 9)</c>; a 12-deep rect
    /// would still contain every cell drawn here, but it would fail the diagonal.
    /// </para>
    /// </summary>
    public static class StoreyMaze
    {
        /// <summary>
        /// Draws B6 병동 onto a sketch. The caller owns the zone, the 계단 and the seed;
        /// this method owns the walls.
        /// </summary>
        public static void Build(MapSketch s)
        {
            s.OnLevel(5);

            // ================================================================
            // The plan.
            //
            //          8 9 0 1 2 3 4 5 6 7 8 9 0   <- cell X (8..20)
            //   z28     . i . q . . . . o . ( . .
            //   z27     # # # # # . . # # # # . .
            //   z26     # . . . # L # # . . # # #
            //   z25     # . y . # # . . . . . . #
            //   z24     x # 0 # # . # # # # # # #
            //   z23     # . # . ) . # . . . # . #
            //   z22     # . # . . . # . . . # . #
            //   z21     # # # # # . # # # # N # #
            //   z20     . [ . . # # # . . . ] . >
            //
            // The plan keys are a sketch-wide namespace — MapSketch throws if two
            // storeys draw the same character — so these are what FirstMapSketch and
            // 굴착층 between them have not already spent: 0 · L · N · o · x · y and
            // i · q · ( · ) · [ · ] · >.
            //
            // Read it as three east–west 복도 at z27, z24 and z21, none of which
            // crosses the storey: each is cut in the middle and the two halves are
            // joined by a jog one row off the line — north over z26, south under z20.
            // That is the whole trick of this floor. A corridor that ran the full 13
            // cells would be 30 m of sight line and §12 would reject it at 20; broken
            // and offset, the same walk is 15 m at most and costs two corners, and the
            // player who has not learned which jog leads where cannot tell the west
            // half from the east half at a glance. There is no third crossing: west and
            // east meet at exactly two places, so choosing wrong is a walk back.
            //
            // The four cells (12,25)(13,25)(12,26)(13,26) are mutually adjacent, so the
            // tiler reads them as one 5 × 5 m room the north jog passes through rather
            // than as four corners: the ward's 처치실. That is the one place on this
            // storey a player can stand in the middle of, and it is as much 개방 공간 as
            // B6 gets — a real one is a 20 × 20 m hall, and a hall on the maze storey
            // would be somewhere to see across, which is exactly what 카펫 is here to
            // deny. §12's 개방 공간 rule is answered by 하역장 and 기계실 upstairs.
            // ================================================================
            s.Plan(8, 20,
                ".i.q....o.(..",
                "#####..####..",
                "#...#L##..###",
                "#.y.##......#",
                "x#0##.#######",
                "#.#.).#...#.#",
                "#.#...#...#.#",
                "#####.####N##",
                ".[..###...].>");

            // ================================================================
            // 동쪽 순환로 — the east wing, and the three §12 rules it carries at once.
            //
            // Cells (14,21)–(18,21)–(18,24)–(14,24) are a closed ring round a solid
            // 3 × 2 block, and it is deliberately the only piece of this storey that is
            // *not* a maze:
            //
            //   · S자 통로. Its two long legs are 10.0 m each — five cells with nothing
            //     branching off the middle three, so the graph keeps them as single
            //     edges — joined by a 7.5 m connector with a 90° bend at both ends.
            //     That is §12's 기본 단위 stated exactly: 10 m 구간 2개, 2회 굽음, and
            //     20 m of travel against the 3 s an aggro release needs.
            //   · 순환로. One independent loop wholly inside the zone (this storey has
            //     six), so a Runner cornered in the ward has somewhere to go that is not
            //     back the way they came.
            //   · 병목. Shutting the west leg leaves 27.5 m the long way round against
            //     the leg's own 7.5 m — a 20.0 m detour, comfortably over the 14.4 m
            //     §12 prices an aggro release at.
            //
            // It is also why the ward is survivable at all. Carpet means the Listener
            // cannot tell anyone where the monster is; the ring means a Runner who
            // already knows the building can break sight on it without being told.
            // ================================================================

            // §12 정비공: one lockable door, "순환로의 목에". (14,22) is mid-passage —
            // the corridor runs straight through it, so it is a length of wall with a
            // leaf in it rather than a junction with a leaf across it, which is the
            // distinction MapSketch throws on. Shut, it turns the ring into a
            // horseshoe and the walk from the south corridor to the middle one from
            // 7.5 m into 27.5 m.
            s.Door(14, 22);

            // ================================================================
            // The places. §12 counts these per zone, so every one is a Mark and every
            // Mark sits on a bend, a junction or an end — a flag on a pass-through cell
            // never reaches the graph.
            // ================================================================

            // 단서 · 목표물 후보 ×3, each with 2+ ways out (§12: "하나 막히면 다른 쪽" —
            // reading a clue takes 2.5 s of held beam, and a site with one exit turns
            // an arrival into a death). Spread one per third of the storey so a clue
            // that names 병동 still leaves the search worth doing: 목욕실 is the
            // four-way at the heart of the west cell block, 처치실 is the 5 × 5 m room
            // the north jog runs through, 온돌방 is the ring's south-east corner.
            s.Mark('0', MapNodeKind.CandidateSite, "F_목욕실");
            s.Mark('L', MapNodeKind.CandidateSite, "F_처치실");
            s.Mark('N', MapNodeKind.CandidateSite, "F_온돌방");

            // 관측 지점: the 간호사실. A ward already had a room for watching the
            // corridor without standing in it, and the kit gives a marked leaf the
            // 격자창 rather than the 막힌 길 cap — same topology, and the bars are what
            // make standing there survivable while §04's 15 m hold runs.
            s.Mark('o', MapNodeKind.ObservationPost, "F_간호사실");

            // 전기 패널: one per zone (§12 정비공), on the T where the west outer
            // corridor meets the middle one — the only point every candidate site can
            // be reached from without crossing the ward twice, which is what "전기 패널
            // 접근 가능" has to mean on a floor this folded.
            s.Mark('x', MapNodeKind.ElectricalPanel, "F_배전반");

            // 은폐 지점: the 이불창고, an alcove buried inside the west cell block with
            // one way in and no line of sight to either corridor. §12 asks for
            // concealment for §07 새벽; on 카펫 it is also the one place a player can
            // stand while the thing that cannot be heard goes past.
            s.Mark('y', MapNodeKind.Concealment, "F_이불창고");

            // 막힌 길 ×9 — 23.7% of this storey's 38 places, inside §12's 20~25% band,
            // and each one a room with a job rather than a stub: the generator hangs a
            // 전리품 on every leaf, and §12 wants that to be a reason to have walked in.
            // Two of them are already marked above (간호사실, 이불창고); these are the
            // other seven. 남자병실 · 여자병실 open off the north corridor, 당직실 sits
            // where the night staff could see both, 배선실 is the ward pantry hidden
            // one cell off the middle corridor, and 격리실 is the far south-east corner
            // — the longest walk from anywhere on the floor, which is exactly why it is
            // where something worth carrying is left.
            s.Mark('i', MapNodeKind.None, "F_남자병실");
            s.Mark('q', MapNodeKind.None, "F_여자병실");
            s.Mark('(', MapNodeKind.None, "F_당직실");
            s.Mark(')', MapNodeKind.None, "F_배선실");
            s.Mark('[', MapNodeKind.None, "F_세면장");
            s.Mark(']', MapNodeKind.None, "F_변소");
            s.Mark('>', MapNodeKind.None, "F_격리실");

            // ================================================================
            // For whoever hangs the 계단 on this storey.
            //
            // A shaft is fixed geometry: the landing is (x, z) and the shaft stands on
            // (x, z+1)…(x+1, z+2), which has to be clear of corridor on this storey and
            // on the one above. Ten cells here satisfy that, and only two of them are
            // worth using: (12,27) and (19,26), at opposite ends of the north corridor.
            //
            // Of the other eight, (13,26) is a 후보 지점 and the four leaves along z28
            // would stop being 막힌 길 the moment a stair gave them a second way out,
            // which drops the ratio below §12's 20%. And (15,21), (16,21) and (16,24)
            // must not be used at all: a landing is a node, and a node in the middle of
            // one of the ring's 10 m legs splits it into two 5 m edges and takes the
            // S자 통로 with it.
            // ================================================================
        }
    }
}
