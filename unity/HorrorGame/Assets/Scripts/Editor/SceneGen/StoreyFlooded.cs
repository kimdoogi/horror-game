using HorrorGame.Core.Map;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// B7 수몰층 — the storey that stands in water. Cells x 20..30, z 20..30, on
    /// <c>OnLevel(6)</c>.
    /// <para>
    /// <b>One fact builds this floor.</b> Its surface is
    /// <see cref="FloorMaterial.Water"/>, whose §04 Listener clarity is 1.00 — the
    /// loudest thing in the building to stand on, above even 금속. Every other storey
    /// asks "can they hear me?" and answers with geometry. This one answers before the
    /// question is finished: <b>no.</b> Nothing crosses B7 unheard, ever, at any speed,
    /// by any role.
    /// </para>
    /// <para>
    /// <b>So the storey is priced, not hidden.</b> A floor nobody can sneak across is
    /// worthless as a maze — every corner you add is cover the player cannot use,
    /// because the Listener already has the fix and the creature already has the
    /// heading. What such a floor <em>can</em> sell is time. So this is the shortest
    /// route through the building: two 20 m halls, two 17.5 m spines, one rung across
    /// the middle, and almost nothing else. Ten junctions on a floor that could hold
    /// forty. §12's own words for the trade — 위험을 감수할 이유 — read literally here:
    /// the risk is that you are audible, and the reason is that you are quick.
    /// </para>
    /// <para>
    /// <b>It is drawn as the opposite of the ward storey.</b> B6 is a lattice on 카펫,
    /// the Listener's blind spot: many short legs, many choices, nobody can hear you and
    /// nobody gets anywhere. B7 is few long legs, few choices, everybody hears you and
    /// you are already across. A player who has learnt both is choosing between them
    /// every time the route branches, which is the only reason to have two of them.
    /// </para>
    /// <code>
    ///   북 회랑  z29  x21~29   20.0 m — the sprint, and the loudest 20 m in the game
    ///   남 회랑  z22  x21~29   20.0 m — the other sprint, one loop away
    ///   서 수직로 x21  z22~29  17.5 m ┐ these two are the S자 통로's first leg and the
    ///   중앙 수직로 x25 z21~29  20.0 m ┘ rung's far knee (see 양수기실 below)
    ///   동 수직로 x29  z22~29  17.5 m — the outer ring's neck; the 문 hangs mid-run
    /// </code>
    /// <para>
    /// <b>Why the two easternmost columns of the brief are solid.</b> The floor was
    /// assigned x 20..32 × z 20..30, which is 13 × 11 cells — 32.5 × 27.5 m, a
    /// <b>42.6 m</b> diagonal, over §12's 30~40 m band and a
    /// <see cref="MapValidator.RuleZoneDiagonal"/> failure before a single corridor is
    /// drawn. 11 × 11 is 38.9 m and legal, and costs nothing: a hall is capped at 20 m
    /// by <see cref="MapValidator.RuleStraightCorridor"/> anyway, so the two columns
    /// could never have carried a longer run. They are left as rock and the caller
    /// should declare <c>AddZone(…, FloorMaterial.Water, 6, 20, 20, 11, 11)</c>.
    /// </para>
    /// <para>
    /// <b>No 개방 공간 here, deliberately.</b> <see cref="MapSketch.OpenRoom"/> is only
    /// honest when a <see cref="MapKitPiece.HallOpen20x20"/> is actually built under it,
    /// and a 6.3 m room on a 3.75 m storey pushes 2.55 m of roof into the floor above —
    /// the B-003 defect that silently deletes corridor up there. B6 is another author's
    /// storey; this one does not reach into it. §12's 개방 공간 requirement is map-wide
    /// and is met by the 하역 베이 on B1. The openness of B7 is legibility, not volume:
    /// the sight lines are long and the choices are few, and neither is 개방 공간.
    /// </para>
    /// </summary>
    public static class StoreyFlooded
    {
        /// <summary>
        /// Draws the storey and marks it. The caller owns
        /// <see cref="MapSketch.AddZone"/> and <see cref="MapSketch.Stair"/>: a zone is
        /// a claim about the whole building's material alphabet and a stair spans two
        /// storeys, so neither belongs to the floor that only knows about itself.
        /// </summary>
        /// <param name="s">The sketch being authored. Its level cursor is moved to 6.</param>
        public static void Build(MapSketch s)
        {
            s.OnLevel(6);

            // ================================================================
            // The plan.
            //
            // Read it as one ring with a bar across it. The ring is the route —
            // north hall, east spine, south hall, west spine — and you can run it
            // either way round, which is the whole of what this storey sells. The
            // bar (중앙 수직로, x25) and the rung (z26, x21~25) cut the ring into
            // three 순환로 so that being met head-on is survivable, and they are the
            // only interior structure on the floor.
            //
            // Every leg is drawn at or just under §12's 20 m straight cap, which is
            // exactly why there are so few of them: a 20 m hall cannot also carry a
            // branch, because a spur leaving it collinearly makes it 22.5 m and a
            // spur leaving it sideways puts a junction where the sprint was.
            //
            //          2 2 2 2 2 2 2 2 2 2 3 3 3   <- cell X (20..32)
            //          0 1 2 3 4 5 6 7 8 9 0 1 2
            //   z30    . . . i . . . . . . . . .
            //   z29    . L # o # q # # # x . . .
            //   z28    . # . . . # . . . # . . .
            //   z27    . # . . . # . . . # . . .
            //   z26    . = # # # ; . . . # . . .
            //   z25    . # . . . # . . . 0 @ . .
            //   z24    . # . . . # . . . # . . .     <- (29,24) carries the 문
            //   z23    . # . . . # . . . # . . .
            //   z22    . y # # # ~ # # # ^ . . .
            //   z21    . . . . . N . . . . . . .
            //   z20    . . . . . . . . . . . . .
            //
            // z20 is rock on purpose. The floor needs one row of margin south of the
            // 남 회랑 so the 배수암거 spur at (25,21) can exist without pushing the
            // 중앙 수직로 to nine steps (22.5 m), and a corridor along z20 would have
            // been a fourth 20 m hall with nowhere to go.
            s.Plan(20, 20,
                "...i.........",
                ".L#o#q###x...",
                ".#...#...#...",
                ".#...#...#...",
                ".=###;...#...",
                ".#...#...0@..",
                ".#...#...#...",
                ".#...#...#...",
                ".y###~###^...",
                ".....N.......",
                ".............");

            // §12 단서 · 목표물 후보: three, each with 탈출로 2개 이상 — 침전조 has four
            // ways out, the other two have three. None is a 막힌 길, which matters more
            // on this floor than anywhere else: 단서 읽기 holds the beam still for
            // several seconds on the one surface that is broadcasting the whole time,
            // so a site with one exit here is not a risk, it is an appointment.
            //
            // They are spread one per side — south-centre, west, east — so that a
            // creature drawn by the noise of a read has to commit to a quarter of the
            // ring, and the reader leaves by the other three.
            s.Mark('~', MapNodeKind.CandidateSite, "B7_침전조");
            s.Mark('=', MapNodeKind.CandidateSite, "B7_양수기실");
            s.Mark('0', MapNodeKind.CandidateSite, "B7_보일러급수");

            // §12 관측자. A barred window over the water at the end of the only spur on
            // the east side: the tiler swaps the 막힌 길 cap for
            // MapKitPiece.ObservationPostBarredWindow at any leaf marked this way, so
            // the alcove is unreachable from the floor it looks down on. On B7 the post
            // is nearly redundant and that is the joke — everyone already knows where
            // the monster is. What it buys is the one thing sound does not give: which
            // way it is facing.
            s.Mark('@', MapNodeKind.ObservationPost, "B7_수위창");

            // §12 정비공: 전기 패널 구역당 1개, and the zone's three 후보 지점 are all
            // reachable from it — §03's "어둠 = 목표의 잠금장치" needs a key on every
            // floor that holds a candidate. It sits at the north-west knee, above the
            // waterline, which is the only reason it still closes.
            s.Mark('L', MapNodeKind.ElectricalPanel, "B7_배전반");

            // §12's 은폐 지점. A drain culvert off the 남 회랑: you stand in it and stop
            // moving, which is the only way to be quiet on 침수 — the surface's clarity
            // is a property of footsteps, not of standing still. It is also the one
            // 막힌 길 a player will willingly enter twice.
            s.Mark('N', MapNodeKind.Concealment, "B7_배수암거");

            // The third 막힌 길. §12 requires 20~25% of a floor's places to be one and
            // requires each to be worth walking into; the generator hangs the 전리품 on
            // any leaf automatically, so all three of these carry one. Three of thirteen
            // is 23.1%, mid-band, and it degrades the right way when the caller adds
            // 계단 — see the landing note at the foot of this method.
            s.Mark('i', MapNodeKind.None, "B7_소독약품고");

            // The rest of the floor, named. §03 spends the match asking players to build
            // a mental map and say it out loud, and "the junction by the north hall" is
            // not something anyone says twice. Ten junctions is few enough that a player
            // can hold all ten, which is the other half of this storey's bargain: it is
            // loud, and it is the only floor you can describe from memory.
            s.Mark('o', MapNodeKind.None, "B7_여과조");
            s.Mark('q', MapNodeKind.None, "B7_북수문");
            s.Mark('x', MapNodeKind.None, "B7_월류구");
            s.Mark(';', MapNodeKind.None, "B7_수문조작대");
            s.Mark('y', MapNodeKind.None, "B7_집수정");
            s.Mark('^', MapNodeKind.None, "B7_역지밸브실");

            // §12 정비공: "순환로의 목에 문 하나 → 잠그면 순환이 끊김." (29,24) is the
            // middle of the 동 수직로's southern run — a passage, not a junction and not
            // a bend, which is what MapSketch.Door demands and what
            // MapGraph.IsBottleneck measures. The run itself is 7.5 m; with the leaf
            // shut the way between its two ends is 47.5 m the long way round the ring
            // (north 10 m, west 10 m, down the 중앙 수직로 17.5 m, east 10 m), a gain of
            // 40 m against the 14.4 m §12 needs before locking anything buys even one
            // aggro release.
            //
            // It is the only door on the floor, and it is on the outer ring rather than
            // on the bar, because the bar is what makes the ring survivable: an Engineer
            // who could cut the middle would turn the shortest route in the building
            // into a corridor with one end.
            s.Door(29, 24);

            // ================================================================
            // 계단 landings — for whoever wires this floor to B6.
            //
            // Two cells are drawn to take one: (23,22) and (27,22), both straight-
            // through on the 남 회랑 with the 2 × 2 shaft footprint north of them —
            // (23..24, 23..24) and (27..28, 23..24) — left as rock on this storey, which
            // is what MapSketch.VerifyStairs checks. Landing mid-passage rather than on
            // a leaf is deliberate: a landing is forced to become a node, so a stair
            // here *adds* a place (13 → 14 → 15) and the 막힌 길 ratio walks 23.1% →
            // 21.4% → 20.0%, all inside §12's band. A stair landed on one of the three
            // 막힌 길 would instead delete one, and the floor would fail at 15.4%.
            //
            // Both are on the south wall, the far side of the ring from nothing in
            // particular — B7 has no far side. That is the point of it.
        }
    }
}
