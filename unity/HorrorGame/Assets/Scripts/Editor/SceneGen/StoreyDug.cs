using HorrorGame.Core.Map;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// B8 굴착층 — the deepest storey, and the one the building does not account for.
    /// <para>
    /// <b>Every other floor is drawn from a service layout.</b> 하역장 is a bay with
    /// docks at fixed offsets; 기록보관소 is aisles between two cross passages;
    /// 기계실 is a hall with a gallery round it. Those shapes are readable — a player
    /// who finds one aisle knows where the next one is, which is exactly the
    /// affordance §03 spends the match asking for. This storey is drawn from a dig,
    /// so it has none of it: the passages widen where the ground gave and pinch where
    /// it did not, the chambers stop for no reason a floor plan would record, and
    /// nothing repeats. There is no bay to count from and no symmetry to complete.
    /// Learning B8 is memorising it.
    /// </para>
    /// <para>
    /// <b>흙 is what makes that a systems statement rather than a mood.</b> §04 rates
    /// a footstep on dug soil at 0.40 clarity — the second quietest surface in the
    /// game, under 콘크리트's 0.50 and above only 카펫 — and only 0.05 above the 0.35
    /// the Listener falls back to for a zone with no surface at all. So a step down
    /// here tells the Listener very nearly what a step on nothing tells them. The
    /// storey that is hardest to hold a map of is also the one the map-holder cannot
    /// be told about, and both halves of that come from the same fact: it was not
    /// built, it was excavated, and not by whoever poured the floors above it.
    /// </para>
    /// <para>
    /// <b>The names are the horror.</b> Upstairs every place has its job written on
    /// the door — 배전반, 보일러 홀, 기록고, 영안실 — and §12's 막힌 길 are rooms with
    /// work in them for exactly that reason. Down here nobody labelled anything,
    /// because nobody down here was doing a job the building knows about. So the
    /// places are named the way a person names somewhere they found rather than
    /// somewhere they were shown: 파다만데, 메운데, 눌린흙, 표해둔데, 세운돌. Each is a
    /// thing that was plainly done on purpose and none of them says what the purpose
    /// was, which is the unease this storey is for. Nothing here is bloody; the
    /// trouble is that it is all deliberate.
    /// </para>
    /// <para>
    /// <b>Measured, against §12.</b> 60 corridor cells (375 m² of floor) resolving to
    /// 36 places and 38 passages: 3 independent 순환로, 8 막힌 길 = <b>22.2%</b> inside
    /// the 20~25% band, longest unbroken straight <b>12.5 m</b> against the 20 m cap,
    /// one S자 통로 of 10 m + 2.5 m + 10 m, and the storey's single 문 on a passage
    /// whose detour is 85 m against its own 5 m — a gain of 80 m where §12 needs 14.4.
    /// </para>
    /// <para>
    /// <b>The dig stops short of its rectangle</b> — x 31 and z 19 are solid, so the
    /// worked ground is 11 × 11 cells, 27.5 m square, <b>38.89 m</b> across the
    /// diagonal. That is arithmetic before it is fiction: the full 12 × 12 window is
    /// 42.43 m corner to corner and §12 caps a zone at 40 m, so a zone declared over
    /// the whole window would fail 구역 대각선 no matter what was drawn inside it. The
    /// caller's <c>AddZone</c> must therefore be <c>(FloorMaterial.Earth, 7, 20, 8,
    /// 11, 11)</c>. It also happens to be the truthful shape for a dig, which ends
    /// where the digging ended and not on a boundary somebody surveyed.
    /// </para>
    /// </summary>
    public static class StoreyDug
    {
        /// <summary>
        /// Draws B8 굴착층 onto a sketch: the plan, the places §12 counts, and the one
        /// 문. The caller owns <see cref="MapSketch.AddZone"/> and
        /// <see cref="MapSketch.Stair"/>, because a zone's surface and a storey's
        /// vertical joints are decisions about the building rather than about this
        /// floor.
        /// </summary>
        /// <param name="s">Sketch to draw into. Left on storey 7.</param>
        public static void Build(MapSketch s)
        {
            s.OnLevel(7);

            // ================================================================
            // B8 · 굴착층 — 흙. Worked ground x 20..30, z 8..18.
            //
            // Read it as a dig rather than as a floor plan. The cut starts at the
            // south-west (파다만데, where somebody swung west and stopped), runs east
            // along the deepest line, doglegs north one cell and runs east again —
            // that stagger is the S자 통로, and it reads as a dig that missed its aim
            // and corrected rather than as two corridors. From the east end the work
            // climbs the east wall in three offset moves, turns back along the north,
            // and comes down the west in another two, closing a 85 m ring that is
            // nowhere square. Inside it, one passage crosses the middle and throws off
            // six blind spurs: those are the 막힌 길, and §12 puts 전리품 in every one
            // of them — "위험을 감수할 이유".
            //
            // Nothing on this storey is 개방 공간. §12's aggro geometry wants somewhere
            // to be seen from 15~25 m out and this floor deliberately has no such
            // place: the widest thing on it is a two-cell chamber. That is legal
            // because the rule is map-wide — 하역장's 20 m bay answers it — and it is
            // the point of putting this floor at the bottom. There is no distance to
            // take aggro from down here, so a Runner who is seen is already committed.
            //
            // The plan keys are punctuation on purpose. MapSketch's key table belongs
            // to the whole sketch, not to one storey, and the letters and digits are
            // spent: FirstMapSketch alone draws 57 of them. A collision does not
            // degrade — Plan throws — so the storeys that came later take characters
            // no floor plan would otherwise reach for.
            // ================================================================

            //          0 1 2 3 4 5 6 7 8 9 0 1   <- cell X (20..31)
            //   z19     . . . . . . . . . . . .
            //   z18     . . # # # . . . ? . . .
            //   z17     . . # . # # # # # < . .
            //   z16     . = # . . . # . . . . .
            //   z15     . # ^ # # . ~ # # # . .
            //   z14     . # . . # . # . . # # .
            //   z13     # # # . @ # # # _ . # .
            //   z12     # . ' . # . . / . # # .
            //   z11     # # . . - . . . . # . .
            //   z10     . # . . . # # # # # . .
            //   z9      ; # # # # # . . . # . .
            //   z8      . . . . . . . . . : . .
            s.Plan(20, 8,
                "............",
                "..###...?...",
                "..#.#####<..",
                ".=#...#.....",
                ".#^##.~###..",
                ".#..#.#..##.",
                "###.@###_.#.",
                "#.'.#../.##.",
                "##..-....#..",
                ".#...#####..",
                ";#####...#..",
                ".........:..");

            // §12 단서 · 목표물 후보: three per zone, every one with 탈출로 2개 이상.
            // All three are junctions of degree 3, so "하나 막히면 다른 쪽" is true of
            // each of them and reading a clue here is a risk rather than a death. They
            // are also the three places on the storey where somebody clearly did
            // something and left no way to tell what — which is why they are the
            // places worth searching, and why none of them explains itself.
            //
            // 표해둔데 (24,13) sits where the cross passage meets the spur south: a
            // mark scored into the wall at a fork, of the kind you make so you can
            // find your way back. Whoever made it did not need to come back this way.
            s.Mark('@', MapNodeKind.CandidateSite, "B8_표해둔데");

            // 눌린흙 (26,15) is on the north side of the ring, where the floor is
            // packed flat and hard over about two square metres. Nothing was stored
            // here — stacked weight leaves edges. Something rested here, often enough
            // and long enough to compact the ground under it.
            s.Mark('~', MapNodeKind.CandidateSite, "B8_눌린흙");

            // 세운돌 (22,15) is in the widening: a stone set upright where the passage
            // opens out. It carries nothing, holds nothing up and blocks nothing. It
            // was stood on end and left standing.
            s.Mark('^', MapNodeKind.CandidateSite, "B8_세운돌");

            // §12 관측자: "없으면 관측자는 죽으러 가야 한다." A leaf marked 관측 지점 gets
            // the barred opening rather than the 막힌 길 cap — same topology, and the
            // bars are what make standing there survivable. 엿보는틈 (28,18) is a slot
            // scraped through into the north-east corner at head height: you can see
            // the whole north run through it and there is no way to reach you from it.
            // It is dug from this side, which is the part worth noticing.
            s.Mark('?', MapNodeKind.ObservationPost, "B8_엿보는틈");

            // §12 정비공: "전기 패널 구역당 1개" — the zone is unlightable without one,
            // and §03 makes darkness "목표의 잠금장치", so a candidate the Engineer
            // cannot light is a lock with no key. But the building's own feed does not
            // reach a floor the building does not have. 이은전선 (21,16) is the answer
            // somebody else made: a line tapped off the storey above, spliced by hand
            // and run down the wall of the widening in staples. It is a 배전반 only in
            // the sense that everything down here draws off it.
            s.Mark('=', MapNodeKind.ElectricalPanel, "B8_이은전선");

            // §12's last checklist item is about the 출입구 four storeys up, so nothing
            // requires a 은폐 지점 here — this one is for §07's later tiers, when the
            // monster patrols every zone and the way back is five 계단 long. 기어든틈
            // (29,17) is a gap in the east wall a person fits through on their belly.
            // It was not dug to be hidden in. It is hideable in.
            s.Mark('<', MapNodeKind.Concealment, "B8_기어든틈");

            // The 막힌 길. §12 wants 20~25% of the storey's places to be blind and each
            // to hold a 전리품 — "위험을 감수할 이유" — and the generator drops the loot
            // on every leaf automatically, so what these marks buy is the other half:
            // a name. §03 spends the match asking players to hold a map in their heads
            // and say it out loud, and an unnamed spur is not a landmark, it is a
            // wrong turn. Eight of the storey's 36 places are leaves = 22.2%.
            //
            // 파다만데 (20,9): the cut runs one cell west off the deepest line and
            // stops mid-swing, the face still rough. Nothing made them stop here.
            s.Mark(';', MapNodeKind.None, "B8_파다만데");

            // 더깊은데 (29,8): the only place on the storey where the floor keeps
            // going. It is a shaft, not a room, and it is not finished.
            s.Mark(':', MapNodeKind.None, "B8_더깊은데");

            // 메운데 (24,11): a spur that was dug out and then filled back in, the
            // spoil packed down flush with the passage floor. Somebody spent the
            // labour twice.
            s.Mark('-', MapNodeKind.None, "B8_메운데");

            // 긁은자국 (22,12): parallel grooves worked into the west face at about
            // chest height, evenly spaced, going nowhere. Made with a tool.
            s.Mark('\'', MapNodeKind.None, "B8_긁은자국");

            // 흙더미 (27,12): the spoil heap. It is the wrong size — far more earth
            // than the passages that reach it account for, so most of what was dug on
            // this storey was dug somewhere the map does not go.
            s.Mark('/', MapNodeKind.None, "B8_흙더미");

            // 낮은굴 (28,13): the roof drops to about a metre and the passage carries
            // on at that height. It was not abandoned for being too low. It was cut
            // that way, and it ends squarely.
            s.Mark('_', MapNodeKind.None, "B8_낮은굴");

            // §12 정비공: "구역당 잠글 수 있는 문 1~2개 … 순환로의 목에 문 하나 →
            // 잠그면 순환이 끊김." (21,14) is the whole neck: the single cell joining
            // the widening in the north-west to the west descent, mid-passage between
            // two junctions rather than on either of them, which is what MapSketch
            // insists on — a door on a junction has no edge to shut. Closed, the way
            // between its two ends goes the entire ring: 85 m against the passage's
            // own 5 m, a gain of 80 m where §12 asks 14.4 m (3 s of broken sight at
            // the monster's 4.8 m/s) before locking a door buys anything at all.
            //
            // It is also the one thing on this storey that was fitted rather than dug,
            // and it was fitted from the north side.
            s.Door(21, 14);
        }
    }
}
