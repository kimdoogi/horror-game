using System;
using System.Collections.Generic;
using HorrorGame.Core.Map;
using HorrorGame.Core.Session;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// One storey of §01's descent: three concentric ring bands, gates narrowing 4 → 2 → 1,
    /// and the 투하구 in the middle.
    /// <para>
    /// <b>Generated rather than drawn.</b> Every other storey in this project is a hand-typed
    /// ASCII plan, and that is right when a floor has a story to tell — 굴착층's spoil heap is
    /// in the wrong place on purpose and no generator would ever put it there. It is wrong
    /// here. §12-A gives every storey the same skeleton and the same three gate counts, so
    /// eight hand-drawn versions of one skeleton would be eight chances to typo the thing the
    /// whole game is made of. What varies between floors is a handful of numbers, and those
    /// are the arguments to <see cref="Build"/>.
    /// </para>
    /// <para>
    /// <b>The band is a zigzag, and that is load-bearing.</b> A ring band two cells thick could
    /// be drawn as two concentric squares, which would be a 52 m straight run on every side —
    /// four times §12's 20 m cap, and a sight line down which nothing survives. So the band
    /// alternates between its outer and inner track, connected by a rung. That single decision
    /// buys three §12 rules at once: no straight over the leg length, an S자 통로 at every
    /// jog, and one 순환로 per band because the zigzag closes on itself. It also makes the
    /// ring longer to walk than it looks, which is what turns "get to the middle" into a race
    /// rather than a diagonal.
    /// </para>
    /// <para>
    /// <b>How OFTEN it jogs is the whole difference between a maze and a jogging simulator,
    /// and it used to be wrong.</b> The band jogged every 4 cells, which sounds like a tight
    /// maze and is the opposite: it is cover every 10 m, and §12's own arithmetic says cover
    /// that close is free — a runner who is never more than 5 m from a corner can break aggro
    /// from anywhere, which is precisely the 10/10 너무 쉽다 the 주자 테스트 kept reporting.
    /// Recorded then, seed 20260802: 774 bends, mean nearest-neighbour spacing <b>3.1 m</b>,
    /// <b>0 of them</b> inside §12's 15~25 m 시야 차단 지점 간격, and the whole storey
    /// collapsing into ONE 시야 차단 지점 155 m deep. The legs are now 6~8 cells — 15~20 m,
    /// the window between §12's spacing floor and its straight-corridor ceiling. See
    /// <see cref="MinLegCells"/>.
    /// </para>
    /// <para>
    /// <b>That moved the census and it did NOT move the rule, and the two must not be
    /// confused again.</b> Measured on the map this generator writes today, seed 20260802,
    /// all eight storeys (720 places, 814 passages): 496 bends outside 개방 공간, grouped
    /// into <b>48 시야 차단 지점</b> where there used to be one, the deepest of them
    /// <b>95 m</b> instead of 155 m. That part is real and it is what the leg lengths bought.
    /// The spacing itself did not move at all: nearest-neighbour between bends is
    /// <b>2.5 m~7.5 m, mean 3.5 m, 0 of 496 inside §12's 15~25 m band</b>, and
    /// <c>sight-break-spacing</c> is still <b>[FAIL]</b> — on 16 of the 48 지점 being deeper
    /// than the <c>GameConstants.SightBreakPointSpanMax</c> 4.4 m a single piece of cover is
    /// allowed, not on the gaps between them.
    /// </para>
    /// <para>
    /// <b>Why the leg length cannot move that number, which is the part worth keeping.</b>
    /// <see cref="MinLegCells"/> is the length of a LEG; the census measures the gap between
    /// BENDS. A jog is a one-cell rung (<see cref="BandThickness"/> 2, so depth 1), so it
    /// plants two bends 2.5 m apart however long the straights either side of it are — and
    /// the alcoves, the gate mouths and the 중심 add more of the same. The histogram of
    /// passage lengths on the shipped graph says it plainly: 432 × 2.5 m, 120 × 5.0,
    /// 112 × 7.5, 32 × 10.0, 8 × 12.5, 64 × 15.0, 32 × 17.5, and 14 × 37.2 (the 투하구). The
    /// long straights the leg rule asks for are there; so are <b>432 passages one cell
    /// long</b>. Reaching §12's 15~25 m 시야 차단 지점 간격 would mean a band with no rungs,
    /// no alcoves and no gate mouths — which is a band with no S자 통로 (rule 3), no 막힌 길
    /// to hit §12's 20~25% (rule 5) and no three-way junction to hang a 후보 지점 on
    /// (rule 9). What is still open is the 지점 SPAN, and that is a question about how far a
    /// runner can travel inside one continuous piece of cover — B-007.
    /// </para>
    /// <para>
    /// <b>What that does NOT fix, measured rather than hoped.</b> The 주자 테스트 still reads
    /// 10/10 at §12's own parameters, and no arrangement of these walls will move it, because
    /// the test is barely sensitive to walls at the numbers §12 runs it on. Sweeping
    /// <see cref="RunnerTestSettings"/> over the SHIPPED floor: 0% escapable at an 어그로
    /// 시작 거리 of 3.4 m and 100% at 3.6 m; 100% at a monster speed of 5.4 m/s and 0% at
    /// 5.5. A step, not a slope — a release needs §06's 12 m of gap, a sprinting runner gains
    /// 0.8 m/s, and whether that arithmetic closes inside one sprint's 60 m is decided before
    /// the map is drawn.
    /// </para>
    /// <para>
    /// Geometry only shows up in the gap between those steps, and there this change does
    /// show up. At §07's 밤 tier (5.4 m/s) the old floor was 973/973 escapable and this one
    /// is 668/720 — 100% → 92.8% — and the ten-point sample comes off 10 for the first time,
    /// reading 8~10/10 across eight seeds. At a 3.6 m start it is 99.6% → 82.8%. At §12's own
    /// 10 m start and 4.8 m/s both floors are 100%, because there the release is free by
    /// arithmetic. The rest of the argument — including why 5~7/10 is out of reach for any
    /// map that obeys §12's own 20 m straight cap — is in the report beside this change.
    /// </para>
    /// <para>
    /// <b>Gates are the design.</b> §12-A: four ways from the outer band to the middle, two
    /// from the middle to the inner, and exactly one into the centre. Twenty players share one
    /// cell on the last step. Each gate carries a door — 1.1 s to shut, 4.5 s to break, never
    /// repaired — which in a race is not protection but a toll charged to everybody behind.
    /// </para>
    /// </summary>
    public static class RadialStorey
    {
        /// <summary>Cells the outer band is thick. Two tracks and the rungs between them.</summary>
        public const int BandThickness = 2;

        /// <summary>
        /// Longest a band may run without a jog, in cells — 8 × 2.5 m = 20 m, §12's
        /// <c>직선 통로 최대</c> exactly.
        /// </summary>
        public const int MaxLegCells = 8;

        /// <summary>
        /// Shortest a band may run between jogs, in cells — 6 × 2.5 m = 15 m, the floor of
        /// §12's <c>시야 차단 지점 간격 15~25m</c>.
        /// <para>
        /// <b>The band used to jog every 4 cells and that was the defect.</b> A jog every
        /// 10 m puts a runner within 5 m of cover from anywhere on the floor, and §12's own
        /// arithmetic says cover that close is free: "괴물이 그 모퉁이에 도달하는 시간 =
        /// D / 4.8초, 시야 차단 3초가 필요 → D ≥ 14.4m". Recorded on that map, seed 20260802:
        /// 774 bends at a mean nearest-neighbour spacing of <b>3.1 m</b>, and <b>0 of 774</b>
        /// inside §12's 15~25 m band. The floor was not a maze with long sight lines — its
        /// longest sight line was 17.5 m — it was a carpet of corners, which is the same
        /// thing as no corners at all. §12's own prescription for a 너무 쉽다 map is
        /// "시야 차단 지점을 줄인다", and this is that number.
        /// </para>
        /// <para>
        /// <b>What six delivered, measured on the map that ships now rather than hoped for.</b>
        /// Same seed, all eight storeys: 774 bends → <b>496</b>, and the whole storey's single
        /// 155 m-deep 시야 차단 지점 → <b>48 지점</b>, deepest 95 m. The nearest-neighbour
        /// figure this paragraph opens with did NOT improve: it is <b>2.5~7.5 m, mean 3.5 m,
        /// 0 of 496 in the band</b>, because the number that decides it is the 2.5 m rung at
        /// every jog and not the straight either side of it. The class remarks carry the
        /// arithmetic. Six is still the right floor for a LEG — it is what keeps a straight
        /// from being shorter than §12's own 시야 차단 지점 간격 — but nobody should read this
        /// constant as the fix for <c>sight-break-spacing</c>, because it measurably is not.
        /// </para>
        /// <para>
        /// 15~20 m is a one-cell-wide window and both walls are §12's: below 6 cells the
        /// bends are closer than 시야 차단 지점 간격 allows, above 8 they are a straight
        /// corridor longer than the 20 m cap. Every leg this generator draws is 6, 7 or 8
        /// cells and there is no room to be anywhere else.
        /// </para>
        /// </summary>
        public const int MinLegCells = 6;

        /// <summary>
        /// Cells that must separate two alcoves on the same rail — one per straight, since a
        /// leg is 6, 7 or 8 cells.
        /// <para>
        /// That is the design statement and the 막힌 길 ratio is the measurement that pins
        /// it. §12 wants 20~25% of a floor's places blind; with the legs at 6~8 cells the
        /// band has far fewer nodes than it did at a jog every 4, so the old spacing of 14
        /// measured <b>16.7%</b> — under the band, and §12 says why that is bad: "적으면 맵
        /// 지식 무의미". Sweeping the spacing, seed 20260802: 6 → 26.6% (over), 7 → 21.1%,
        /// 8 → 21.1%, 12 → 20.0% (on the edge). Seven is the leg length, it is the value
        /// with room on both sides of the band, and it means a runner on any straight has
        /// exactly one place to stand aside.
        /// </para>
        /// <para>
        /// There is no probability here on purpose. An earlier version rolled for each
        /// bearing and the dead-end ratio wandered with the seed — 18.0% at one rate, 19.1%
        /// at another, never reliably inside §12's band. Punching every bearing that is
        /// legal lands it in the band and lands it there every time, and a floor that obeys
        /// the rules by construction is worth more than one that usually does. The seed
        /// still decides the thing that should vary: where the gates are.
        /// </para>
        /// </summary>
        public const int AlcoveSpacing = 7;

        /// <summary>
        /// Draws one storey. The caller owns <see cref="MapSketch.AddZone"/> and
        /// <see cref="MapSketch.OnLevel"/>, because a zone's surface and a storey's place in
        /// the building are decisions about the building rather than about this floor.
        /// </summary>
        /// <param name="s">Sketch to draw into.</param>
        /// <param name="level">Storey.</param>
        /// <param name="centreX">Cell X of the middle.</param>
        /// <param name="centreZ">Cell Z of the middle.</param>
        /// <param name="random">Seeded. Decides gate angles and which spurs exist.</param>
        /// <param name="label">Prefix for named places, e.g. "B3".</param>
        /// <returns>Where things ended up, for the caller to hang chutes and spawns on.</returns>
        public static RadialStoreyResult Build(
            MapSketch s, int level, int centreX, int centreZ, IRandomSource random, string label)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            s.OnLevel(level);

            // Chebyshev radii. The walls between bands are the radii that get no corridor at
            // all except where a gate is punched, which is what makes a gate a gate.
            //
            //   d 0..1   중심      the chutes, and on B8 the finish
            //   d 2      wall      1 gate
            //   d 3..4   안쪽 고리
            //   d 5      wall      2 gates
            //   d 6..7   중간 고리
            //   d 8      wall      4 gates
            //   d 9..10  외곽 고리  players stand here
            var bands = new[]
            {
                // One rail, not two, and now it needs no jog either. The 안쪽 ring is 6 cells
                // from one corner to the next — 15.0 m, which is at once inside §12's 20 m
                // straight cap and exactly on the floor of its 15~25 m 시야 차단 지점 간격.
                // A ring that small has nowhere to put a jog that would not land closer than
                // 15 m to a corner, and JogOffsets returns nothing for it for that reason.
                //
                // It is also where sight-break-spacing runs out of room altogether, and the
                // arithmetic is worth writing down because no tuning gets past it. A 60 m lap
                // holds at most four 지점 at §12's 15 m spacing; the four corners are already
                // four; and §12-A then asks for one gate mouth into the 중심 and lands the two
                // 중간 gates on the same ring — seven. Seven 지점 do not fit on a 60 m ring.
                // The smallest ring on which one junction per side can be 15 m from both
                // corners has radius 6, which would push 중간 to 9 and 외곽 to 12, past
                // DescentMap's Radius = 11 and past its 23-cell zone.
                new Band("안쪽", 3, 3, 1),
                new Band("중간", 6, 7, 2),
                new Band("외곽", 9, 10, 4),
            };

            var result = new RadialStoreyResult(centreX, centreZ);
            var occupied = new HashSet<long>();

            // ── 중심 ────────────────────────────────────────────────────────────
            // A 3 × 3 room. Not a corridor: it is the one place on the floor where several
            // players can see each other arrive, and §01 needs the chutes to be a visible
            // choice rather than a cell you stumble into.
            //
            // The plus that replaced the filled block for one measurement is worth keeping
            // as a wrong turn: 11 islands became 18 and completion fell 98.1% to 91.2%,
            // because each of its four arms is a blind cell, each blind cell takes a
            // DeadEndCap, and four caps ringing the middle seal it harder than the block.
            //
            // **`[1] MonsterSpawn` was never this block's fault, and reading it that way
            // cost three days.** The audit collects markers whose names contain PlayerSpawn,
            // MonsterSpawn, Site, Candidate, Loot, Exit, Objective or Clue — so of the eight
            // middles, exactly ONE carries a marker: the 괴물 on B5. (B8's 도착점 is Korean
            // and matches nothing in that list, and a 투하구 is not collected either.) Every
            // arrangement of the middle could therefore only ever move the island count by
            // one, which is why four regenerations with genuinely different geometry
            // produced a byte-identical audit. What actually sealed the middle was the 문 on
            // the gate outside it — see the door decision below.
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    Put(s, level, null, occupied, centreX + dx, centreZ + dz);
                }
            }

            // And ONE piece over the top of them. B-010.
            //
            // The cells stay — they are what the graph is built from, so the middle is nine
            // places §12 can count, the inner gate has a passage to arrive along, and §02's
            // finish has somewhere to be marked. What changes is the GEOMETRY: the corridor
            // tiler skips any cell inside a room, so instead of nine 2.5 m passages walling
            // each other off it lays Chamber_Open_3x3, which is open at the middle of all
            // four edges and nothing else.
            //
            // That split is still right, and it is right for its own reasons rather than for
            // the island count. Room() alone produced geometry with no graph — four dock
            // cells with one neighbour each, §12 refusing to hang a door on a dead end and
            // refusing to mark a finish inside a room, both correctly. Corridor cells alone
            // produced nine walled passages where §01 wants one room. Cells for the graph, a
            // piece for the world.
            //
            // Measured, seed 20260802, all eight storeys: the chamber's doorway and the dock
            // cell outside it join. Sampling out from the middle in 0.10 m steps there is no
            // stretch of floor without navmesh on it anywhere along the gate bearing, and
            // middle→band is PathComplete on every floor. The piece was never the defect.
            s.Room(MapKitPiece.ChamberOpen3x3, centreX - 1, centreZ - 1, 3, 3, 0f);

            // And it is 개방 공간, which nobody had told §12.
            //
            // This is a correction to a MEASUREMENT before it is anything else. The middle
            // is a room you can see across; the nine cells under the chamber were being
            // counted as nine corridor cells with bends in them, so both MapValidator's
            // sight-break census and RunnerTest's cover test treated the inside of a lit
            // 7.5 m room as somewhere a sight line breaks. It is not. MapSketch.OpenRoom is
            // the only way to say so, and it is honest here for the reason its own remarks
            // give — the rectangle is the footprint of a room that is actually built.
            //
            // <b>It does NOT satisfy open-adjacent-to-maze, and for one round it looked as
            // though it did.</b> The chamber gives the rule the arrow it asks for — the
            // 안쪽 gate's passage stands against the chamber's doorway, on the same storey,
            // on all eight floors — and gives it nothing else. §12 says in the same breath
            // what an 개방 공간 is FOR: "멀리서 어그로를 건다 · 시야 15~25m 확보". Measured
            // on the shipped graph, seed 20260802: every one of the eight chambers spans
            // 7.1 m between its furthest two cells (7.5 m of footprint), against §12's 15 m
            // floor. MapValidator now measures that span, so the rule reads [FAIL] and says
            // 7.1 m out loud instead of passing on the arrow alone.
            //
            // <b>It went green for a worse reason than the arrow, and the reason is worth
            // recording.</b> The rule walked the raw graph, and the first 개방 공간 cell it
            // reached with a 미로 공간 neighbour was this chamber's chute mouth — whose
            // neighbour is the LANDING one storey down, 37.2 m away in plan and 3.75 m below.
            // It rendered a one-way 투하구 as "opens directly into". A fall is not a doorway;
            // the validator will not accept a storey-changing edge as 인접 any more.
            //
            // No storey of this building can pass the rule as it stands. The only kit piece
            // with §12's 15~25 m sight line is Hall_Open_20x20, which is 6.3 m tall on a
            // 3.75 m storey — in a tower where all eight floors share one square,
            // MapSketch.IntrudesOnStoreyAbove drops it on every storey but B1, because for
            // every other level the cells over it are somebody's corridor. What a floor
            // would have to GAIN is a real 개방 공간 at least 15 m across on its own storey:
            // a kit piece that is wide and no taller than a storey, or a band segment
            // widened into a hall and declared with OpenRoom. Both are geometry changes that
            // need a bake to judge, and both are somebody's job rather than a comment's.
            // Until then the honest state of this rule is FAIL — and MapSceneGenerator's
            // KnownFailingRules already carries it, listed as retired by the §01 pivot, so
            // the map still writes.
            s.OpenRoom(centreX - 1, centreZ - 1, 3, 3);

            result.Centre = new MapCell(centreX, centreZ, level);

            // ── the three bands, outermost first ────────────────────────────────
            for (var i = bands.Length - 1; i >= 0; i--)
            {
                var band = bands[i];
                var track = DrawZigzagBand(s, level, centreX, centreZ, band, random, occupied);
                result.Bands.Add(track);
            }

            // ── gates ───────────────────────────────────────────────────────────
            // Punched after the bands exist, so a gate can be placed where both sides
            // already have corridor to join. Angles are spread evenly and then jittered,
            // which keeps 20 players from all turning the same way off the spawn ring
            // while still guaranteeing the four outer gates are nowhere near each other.
            //
            // And each band's gates are pushed as far as the ring allows from the gates of
            // the band OUTSIDE it. That stagger is where the distance to the middle comes
            // from. Left to spread evenly on their own, the first version measured 35 m from
            // the rim to the centre against §12-D's 60~90 m band — with four outer gates
            // spread round the compass you are never more than a few metres from one, so the
            // floor was a diagonal with corners in it. Staggered, every band has to be walked
            // roughly half way round before the next way in appears, and the narrowing
            // becomes a distance instead of a fact.
            var previousGates = new List<MapCell>();
            var allGates = new List<MapCell>();

            // Every radius a band has a rail on. A gate crosses the band inside it on the
            // way through, and where it lands on a rail radius that the zigzag deliberately
            // SKIPPED, it fills the gap and welds two of that band's legs into one straight.
            // Measured on the new leg lengths, seed 20260802: one 외곽 gate whose bearing was
            // the 중간 band's own jog turned that band's 15 m + 15 m side into a single 35 m
            // run — §12's cap is 20 m, and the run was reported on B3 where nothing had
            // changed except which bearing the seed picked. See PunchGates.
            var rails = new HashSet<int>();
            for (var i = 0; i < bands.Length; i++)
            {
                rails.Add(bands[i].Inner);
                rails.Add(bands[i].Outer);
            }

            // Bands were appended outermost-first, so the list runs the other way from the
            // array. Indexing it with the array's own index reads the wrong band — which for
            // the alcoves was silently the wrong shape and for the gates was a crash.
            for (var i = bands.Length - 1; i >= 0; i--)
            {
                var band = bands[i];
                var innerEdge = i == 0 ? 1 : bands[i - 1].Inner;
                var wall = band.Inner - 1;

                var gates = PunchGates(
                    s, level, centreX, centreZ, wall, innerEdge, band, random, previousGates, occupied,
                    result.GateMouths,
                    result.Bands[bands.Length - 1 - i],
                    i == 0 ? null : result.Bands[bands.Length - i],
                    rails);
                result.Gates.Add(gates);
                previousGates = gates;
                allGates.AddRange(gates);

                // §12 wants 20~25% of a floor's places blind, each with a reason to walk in.
                // A generated zigzag ring has almost none — measured 14.0% before these —
                // because every cell of it leads somewhere. So each band gets alcoves punched
                // outward into the wall it sits against: one cell deep, ending nowhere. In a
                // race they are the only places to stand aside, which makes them the only
                // places to hide from another player, and §12 drops a 전리품 in each.
                var alcoves = PunchAlcoves(
                    s, level, centreX, centreZ, band, random, allGates,
                    result.Bands[bands.Length - 1 - i], occupied);

                // Furthest from this band's way inward first, so the LAST alcove on the
                // finished list is the one nearest the middle. That ordering is a contract:
                // DescentMap reads Alcoves[^1] as "the last alcove before the middle" and
                // marks it §12's 은폐 지점, which §12 then requires to be within 25 m of the
                // 출입구 — on B8 the 출입구 is the 도착점 in the middle.
                //
                // It used to hold by accident. The bands are walked outermost first, so the
                // 안쪽 band's alcoves were appended last whatever their order round the ring,
                // and with the old spacing the last one happened to land 15 m from the middle.
                // Moving the alcoves for the leg lengths moved it too, and the storey came
                // back with concealment-near-exit FAILING: "no 은폐 지점 within 25 m of
                // B8_도착점". Nothing about the hiding place had got worse — the list order
                // had. Sorting on the gate makes the contract true instead of lucky.
                if (gates.Count > 0)
                {
                    var inward = gates[0];
                    alcoves.Sort((a, b) => Reach(b, inward).CompareTo(Reach(a, inward)));
                }

                result.Alcoves.AddRange(alcoves);

                // One door a storey, on one of the two 중간 gates. Never on the four outer
                // gates and — B-010 — never on the single inner one.
                //
                // The rim is easy: a door on one of four parallel ways in is not a 병목 at
                // all, because the graph says shutting it forces a detour of almost nothing
                // with three other gates right there. A door is worth carrying to the place
                // where it costs everybody behind you something, and at the rim it costs
                // them a shrug.
                //
                // The inner gate is the opposite mistake and it cost three days. §12 asks
                // for a 문 at a 병목 and glosses the point as "잠그면 순환이 끊김 (전략적
                // 선택)" — locking it breaks a CIRCULATION, which presumes there is one to
                // break. There is none through the inner gate. It is the only edge into the
                // 중심, so locking it does not charge the field a detour, it deletes the
                // middle from the floor: no 투하구, and on B8 no §02 finish. That is not a
                // strategic choice in a 20-player race, it is a switch that ends the match.
                //
                // And it is why B-010 read as a geometry bug for three days. Anything at all
                // wrong with a door's geometry costs a DETOUR at the 중간 gate and a
                // COMPONENT at the inner one, so the same defect was invisible at one and an
                // island at the other. Measured on this map, seed 20260802: with the 문
                // geometry present the audit is 6863/6993 pairs, 11 islands, monster reach
                // 0/3; with it hidden and the surface re-baked, 6993/6993, 8 islands (the
                // per-storey floor), monster reach 3/3 — every storey's middle→band path
                // PathPartial → PathComplete. The 문 was the whole of it, and the half that
                // is not mine to fix is written up in the report beside this change:
                // MapSceneBuilder.BuildDoor places Doorway_Frame — a whole 2.5 m walled cell
                // — with its pivot on the cell CENTRE instead of AlignMinCorner'd to the
                // cell, at yaw 0 whichever way the passage runs.
                //
                // §12 caps a zone at 1~2 doors and a floor is one zone, so one is legal and
                // one is what a floor gets.
                if (i == 1)
                {
                    s.Door(gates[0].X, gates[0].Z);
                }
            }

            result.Bands.Reverse();
            result.Gates.Reverse();
            return result;
        }

        /// <summary>
        /// Draws one band as a zigzag between its outer and inner track, and returns every
        /// cell it used. The zigzag is the reason the band obeys §12; see the class remarks.
        /// <para>
        /// <b>Jogs are planned per SIDE, not counted along the walk.</b> The version this
        /// replaces carried a running counter and jogged whenever it reached
        /// <c>JogEvery</c>, suppressing the jog near a corner. That has a latent defect
        /// which JogEvery = 4 happened to miss and which any other period hits: the
        /// near-corner rule forces the cell back onto the OUTER rail without drawing a
        /// rung, so if the walk arrives at a corner while on the inner rail the two cells
        /// are diagonal neighbours and the band is cut in half. Measured: changing nothing
        /// but the period to 7 took the tower from 1 connected piece to <b>5</b>, with no
        /// exception thrown — MapSketch only refuses a cell that touches NOTHING, and both
        /// halves of a severed ring still touch themselves. Planning per side makes the
        /// parity a property of the plan instead of an accident of the arithmetic: an even
        /// number of jogs per side means the rail is always back on the outer track by the
        /// time the corner arrives.
        /// </para>
        /// </summary>
        private static List<MapCell> DrawZigzagBand(
            MapSketch s, int level, int cx, int cz, Band band, IRandomSource random, HashSet<long> occupied)
        {
            var cells = new List<MapCell>();
            var ring = Perimeter(cx, cz, band.Outer);
            var depth = band.Outer - band.Inner;

            // Perimeter walks four equal sides of 2 × radius cells, each beginning with its
            // corner, so a cell's offset along its own side is its ring index modulo that.
            var side = band.Outer * 2;
            var jogs = JogOffsets(side);

            // Track offset: 0 is the outer rail, depth is the inner one. Flipping it at a
            // planned jog and drawing the rung between is the whole trick.
            var offset = 0;

            for (var i = 0; i < ring.Count; i++)
            {
                var step = ring[i];

                // Corners AND their two neighbours sit on the outer rail. A corner's inward
                // neighbour is diagonal and §12 has no diagonals, so a corner can only be
                // reached along the rail it is on — and if the cell before or after it had
                // jogged inward, the corner would be left touching nothing at all. That is
                // not a cosmetic gap: MapSketch refuses to build a map with an orphan cell
                // in it, which is how this was found rather than shipped.
                var nearCorner = step.Corner
                                 || ring[(i + 1) % ring.Count].Corner
                                 || ring[(i + ring.Count - 1) % ring.Count].Corner;
                var here = nearCorner ? 0 : offset;
                Put(s, level, cells, occupied, step.X + (step.InX * here), step.Z + (step.InZ * here));

                if (nearCorner || System.Array.IndexOf(jogs, i % side) < 0)
                {
                    continue;
                }

                for (var t = 0; t <= depth; t++)
                {
                    Put(s, level, cells, occupied, step.X + (step.InX * t), step.Z + (step.InZ * t));
                }

                offset = offset == 0 ? depth : 0;
            }

            // The seam closes by construction rather than by luck: every side carries an
            // even number of jogs, so offset is 0 at every corner including the last one.
            return cells;
        }

        /// <summary>
        /// Where a side of <paramref name="sideCells"/> cells jogs, measured from its corner.
        /// <para>
        /// Two rules decide this and both are §12's. Every leg has to be 6~8 cells — 15 m is
        /// the floor of 시야 차단 지점 간격 and 20 m is 직선 통로 최대 — and the count has to
        /// be EVEN, because the corner and its two neighbours are pinned to the outer rail
        /// and an odd number of flips would arrive at the next corner on the inner one.
        /// </para>
        /// <para>
        /// Which leaves exactly one shape per band, and it is worth reading them out:
        /// </para>
        /// <list type="bullet">
        /// <item>안쪽, 6 cells a side: no jog at all. One 15.0 m leg, corner to corner.</item>
        /// <item>중간, 14 cells: jogs at 6 and 8 — 15.0 m, then a 5 m S, then 15.0 m. Two
        /// jogs two cells apart is the only even split of 14 that keeps both outer legs at
        /// or over 6 cells, and it happens to be §12's 기본 단위 drawn small.</item>
        /// <item>외곽, 20 cells: jogs at 7 and 13 — 17.5 m, 15.0 m, 17.5 m. This is the band
        /// twenty runners start on and the one they spend the most of a floor in.</item>
        /// </list>
        /// </summary>
        /// <param name="sideCells">Cells from one corner to the next — twice the ring radius.</param>
        /// <returns>Offsets along the side, or an empty array when the side needs no jog.</returns>
        private static int[] JogOffsets(int sideCells)
        {
            if (sideCells <= MaxLegCells)
            {
                return Array.Empty<int>();
            }

            // The two jogs are symmetric about the middle of the side, so one number decides
            // both: how long the outer legs are. It wants to be a third of the side — three
            // even legs — and it is held between §12's 6 and 8, and additionally below
            // (side − 2) / 2 so that the two jogs stay at least 2 cells apart. Two jogs on
            // the same cell would be no jog at all, and two jogs one cell apart would draw
            // the rung twice into the same pair of cells.
            var widest = System.Math.Min(MaxLegCells, (sideCells - 2) / 2);
            if (widest < MinLegCells)
            {
                // No split of this side has both outer legs at 6 cells or more — a side of
                // 10, say, would have to be 6 + 2 + 2. Rather than draw a 5 m leg and quietly
                // break 시야 차단 지점 간격, draw the side straight and let straight-corridor
                // fail out loud with the number: no band this generator declares reaches
                // here, and one that did would be a band the radii cannot hold.
                return Array.Empty<int>();
            }

            var outer = System.Math.Min(
                System.Math.Max((int)System.Math.Round(sideCells / 3.0), MinLegCells), widest);
            return new[] { outer, sideCells - outer };
        }

        /// <summary>
        /// Cuts <paramref name="band"/>'s gate cells through the wall at
        /// <paramref name="wall"/>, spread around the compass.
        /// </summary>
        private static List<MapCell> PunchGates(
            MapSketch s,
            int level,
            int cx,
            int cz,
            int wall,
            int innerEdge,
            Band band,
            IRandomSource random,
            List<MapCell> avoid,
            HashSet<long> occupied,
            List<MapCell> mouths,
            List<MapCell> bandCells,
            List<MapCell>? bandBelow,
            HashSet<int> rails)
        {
            var ring = Perimeter(cx, cz, band.Inner);
            var gates = new List<MapCell>();
            var span = band.Inner - innerEdge;

            // Gates go on the flat of a side, never near a diagonal — and this is a real
            // constraint rather than taste. On a Chebyshev ring the inward step is a single
            // axis, so it only reduces the radius while that axis is the dominant one. Punch
            // a gate five cells from the corner of a radius-6 ring and the passage runs three
            // cells without ever leaving radius 6: a spur into the wall, ending nowhere, with
            // a door asked for on a bend. MapSketch refuses it, which is how this was found.
            var clear = band.Inner - span - 1;
            // And only where the band's inner rail was actually drawn. The zigzag spends half
            // its length on the outer rail, and a gate leaving a bearing the band skipped is
            // not a branch off anything — the mouth cell has the gate passing straight through
            // it and nothing else, so it is a passage rather than a junction. §12 counts
            // 후보 지점 at junctions, and MapSketch refuses a mark on a passage rather than
            // letting the count come up short with nothing failing.
            // Both ENDS of the gate have to land on a rail that was actually drawn. The check
            // above catches a gate that branches off nothing; this one catches a gate that
            // arrives at nothing, which is worse because it looks like a way in and is a dead
            // end. Left unchecked they made every floor 34% blind against §12's 20~25% band,
            // and — the part that matters — a storey with four gates on the outer wall of
            // which two go nowhere is a storey with two gates.
            var open = ring.FindAll(p => !p.Corner
                                         && System.Math.Abs(p.InX != 0 ? p.Z - cz : p.X - cx) <= clear
                                         && bandCells.Exists(c => c.X == p.X && c.Z == p.Z)
                                         && Arrives(p, span, bandBelow)
                                         && CrossesOnlyDrawnRails(p, span, cx, cz, rails, occupied));
            if (open.Count == 0)
            {
                return gates;
            }

            var spacing = open.Count / band.Gates;

            // A quarter of the spacing of jitter: enough that two floors do not have their
            // gates at identical bearings, not enough for two gates to end up adjacent.
            var jitter = System.Math.Max(1, spacing / 4);

            // The stagger. Start walking from wherever is furthest from the gates of the band
            // outside this one, so arriving through those gates leaves the most ring to cover.
            var start = FurthestFrom(open, avoid);
            if (avoid.Count == 0)
            {
                start = random.NextInt(0, open.Count);
            }

            for (var g = 0; g < band.Gates; g++)
            {
                var step = open[(start + (g * spacing) + random.NextInt(0, jitter)) % open.Count];

                // Every cell from this band's inner rail through the wall to the band inside.
                // A gate is a short passage, not a doorway in a line — which is what lets
                // MapSketch.Door hang a leaf mid-passage on it.
                for (var t = 0; t <= span; t++)
                {
                    var x = step.X + (step.InX * t);
                    var z = step.Z + (step.InZ * t);
                    Put(s, level, null, occupied, x, z);
                    if (band.Inner - t == wall)
                    {
                        gates.Add(new MapCell(x, z, level));
                    }

                    // The cell where the gate branches off the band is a T-junction, and it
                    // is the only kind of place on a ring floor that has three ways out. §12
                    // wants three 후보 지점 per zone with two exits each, and the gate cells
                    // themselves cannot be marked — they are deliberately mid-passage so a
                    // door can hang on them, and MapSketch refuses a mark on a passage.
                    if (t == 0)
                    {
                        mouths.Add(new MapCell(x, z, level));
                    }
                }
            }

            return gates;
        }

        /// <summary>
        /// Punches one-cell blind alcoves outward from a band, avoiding its gates.
        /// </summary>
        private static List<MapCell> PunchAlcoves(
            MapSketch s,
            int level,
            int cx,
            int cz,
            Band band,
            IRandomSource random,
            List<MapCell> allGates,
            List<MapCell> bandCells,
            HashSet<long> occupied)
        {
            var alcoves = new List<MapCell>();

            // Both rails, in both directions: outward from the outer rail into the wall above
            // the band, and inward from the inner rail into the wall below it. One rail alone
            // measured 15.8% against §12's 20~25% band, because the zigzag spends half its
            // length on the other rail and every bearing it skipped is a bearing with nothing
            // to hang an alcove off.
            Punch(s, level, cx, cz, band.Outer, +1, random, allGates, bandCells, alcoves, occupied);
            Punch(s, level, cx, cz, band.Inner, -1, random, allGates, bandCells, alcoves, occupied);
            return alcoves;
        }

        private static void Punch(
            MapSketch s,
            int level,
            int cx,
            int cz,
            int radius,
            int outward,
            IRandomSource random,
            List<MapCell> allGates,
            List<MapCell> bandCells,
            List<MapCell> into,
            HashSet<long> occupied)
        {
            var ring = Perimeter(cx, cz, radius);

            // Away from corners, away from gates, and never two in a row — a pair of adjacent
            // alcoves would read as a two-cell room and the graph would join them.
            var lastAt = -AlcoveSpacing;
            for (var i = 0; i < ring.Count; i++)
            {
                var step = ring[i];
                if (step.Corner || i - lastAt < AlcoveSpacing)
                {
                    continue;
                }

                var x = step.X - (step.InX * outward);
                var z = step.Z - (step.InZ * outward);

                // Not on a gate, and not beside one either. A gate is a passage and its cells
                // have to stay passages: an alcove touching a gate cell turns it into a
                // junction, and MapSketch.Door refuses to hang a leaf on a junction because
                // there is no single edge for the validator to measure the detour of. The
                // list is every gate cut so far, not just this band's — an alcove punched
                // outward from 중간 lands in the same wall 외곽's gates cross.
                if (allGates.Exists(gate => System.Math.Abs(gate.X - x) + System.Math.Abs(gate.Z - z) <= 1))
                {
                    continue;
                }

                // Only where the band's outer rail was actually drawn. The zigzag spends half
                // its length on the inner rail, and an alcove hung off a bearing the band
                // skipped is an orphan cell — MapSketch refuses the whole map for one.
                // Checking the returned cell list rather than the radius is the point: the
                // radius is right by construction and tells you nothing.
                if (!bandCells.Exists(c => c.X == step.X && c.Z == step.Z))
                {
                    continue;
                }

                // The cell has to become a genuine leaf: empty now, with exactly one occupied
                // neighbour to hang off. Without this check, two alcoves punched from opposite
                // sides of the same wall met in the middle and became a passage — dead ends
                // fell to 12.1% while loops jumped from 17 to 27 and a 25 m straight appeared,
                // all three from the same cause. An alcove that joins something is not an
                // alcove.
                if (occupied.Contains(Key(x, z)) || Neighbours(occupied, x, z) != 1)
                {
                    continue;
                }

                Put(s, level, into, occupied, x, z);
                lastAt = i;
            }
        }

        /// <summary>
        /// True when a gate passage adds no cell to a band rail that the zigzag left empty.
        /// <para>
        /// A gate is the only thing on a storey that cuts ACROSS a band, and the cells it
        /// lays are indistinguishable from corridor once they are down. Where its bearing is
        /// one of the band's own jog bearings — the one cell per jog the zigzag deliberately
        /// does not draw on that rail — the gate patches the hole, and the band's two legs
        /// on either side of the jog become one straight run. Measured, seed 20260802,
        /// legs of 6 cells: exactly that happened on B3's 중간 band and the map reported a
        /// 35.0 m sight line against §12's 20 m cap, from a change that had nothing to do
        /// with gates. Nothing else on the floor can do this, because nothing else crosses
        /// a rail it is not part of.
        /// </para>
        /// <para>
        /// Stated as "may not create a cell at a rail radius", not "may not sit on a jog",
        /// because the jog bearings of the band below are not this band's to know and the
        /// radii are: the wall radii between bands belong to nobody and a gate is welcome to
        /// fill them, which is what a gate IS.
        /// </para>
        /// <para>
        /// <b>It buys a design property that was not asked for, and it is a good one.</b> A
        /// 외곽 gate has to cross the 중간 band's OUTER rail and land on its INNER one, and
        /// the only bearings where both exist are the ones where that band jogs. So every
        /// gate now delivers a runner onto the band inside AT ITS S-BEND rather than into the
        /// middle of a 15 m straight: you come through the wall and you are already at a
        /// corner, with cover on one side and a sight line down the other. Measured, seed
        /// 20260802: 4 · 2 · 1 gates on all eight storeys, none of them duplicated, and the
        /// same counts on eight different seeds.
        /// </para>
        /// </summary>
        private static bool CrossesOnlyDrawnRails(
            RingStep step, int span, int cx, int cz, HashSet<int> rails, HashSet<long> occupied)
        {
            for (var t = 0; t <= span; t++)
            {
                var x = step.X + (step.InX * t);
                var z = step.Z + (step.InZ * t);
                var radius = System.Math.Max(System.Math.Abs(x - cx), System.Math.Abs(z - cz));
                if (rails.Contains(radius) && !occupied.Contains(Key(x, z)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when the far end of a gate passage lands on a cell that exists.</summary>
        private static bool Arrives(RingStep step, int span, List<MapCell>? bandBelow)
        {
            if (bandBelow == null)
            {
                // The innermost gate arrives in the 3 x 3 middle, which is solid by
                // construction — there is nothing to miss.
                return true;
            }

            var x = step.X + (step.InX * span);
            var z = step.Z + (step.InZ * span);
            return bandBelow.Exists(c => c.X == x && c.Z == z);
        }

        /// <summary>
        /// Index into <paramref name="open"/> of the cell furthest from every cell in
        /// <paramref name="avoid"/>. Chebyshev, because that is the metric the rings are in.
        /// </summary>
        private static int FurthestFrom(List<RingStep> open, List<MapCell> avoid)
        {
            var best = 0;
            var bestDistance = -1;

            for (var i = 0; i < open.Count; i++)
            {
                var nearest = int.MaxValue;
                for (var j = 0; j < avoid.Count; j++)
                {
                    var d = System.Math.Max(
                        System.Math.Abs(open[i].X - avoid[j].X),
                        System.Math.Abs(open[i].Z - avoid[j].Z));
                    nearest = System.Math.Min(nearest, d);
                }

                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = i;
                }
            }

            return best;
        }

        private static void Put(
            MapSketch s, int level, List<MapCell>? into, HashSet<long> occupied, int x, int z)
        {
            s.Corridor(x, z, x, z);
            occupied.Add(Key(x, z));
            into?.Add(new MapCell(x, z, level));
        }

        /// <summary>
        /// Manhattan cells between two places on a storey — a stand-in for the walk, used
        /// only to order alcoves by how near the way inward they are. Manhattan rather than
        /// Chebyshev because the corridors have no diagonals, so it is the walk a runner
        /// takes wherever the ring is not in the way.
        /// </summary>
        private static int Reach(MapCell cell, MapCell to) =>
            System.Math.Abs(cell.X - to.X) + System.Math.Abs(cell.Z - to.Z);

        /// <summary>Packs a cell into one long so a HashSet can hold it without allocating.</summary>
        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>How many of the four orthogonal neighbours are already drawn.</summary>
        private static int Neighbours(HashSet<long> occupied, int x, int z)
        {
            var n = 0;
            if (occupied.Contains(Key(x + 1, z))) { n++; }
            if (occupied.Contains(Key(x - 1, z))) { n++; }
            if (occupied.Contains(Key(x, z + 1))) { n++; }
            if (occupied.Contains(Key(x, z - 1))) { n++; }
            return n;
        }

        /// <summary>
        /// The cells of a square ring at Chebyshev radius <paramref name="radius"/>, walked
        /// once round, each carrying the unit step that moves it one cell toward the middle.
        /// <para>
        /// Chebyshev rather than Euclidean, so a "ring" is a square and every cell of it is
        /// axis-aligned with its neighbours. §12 has no diagonals — the kit docks on grid
        /// edges and a bend is measured from geometry — so a round ring would be a staircase
        /// of one-cell steps and every one of them a bend.
        /// </para>
        /// <para>
        /// The inward step is carried rather than computed because it is only well defined on
        /// a side: at a corner both axes lead inward and neither alone does. Corners are
        /// flagged so callers can leave them alone.
        /// </para>
        /// </summary>
        private static List<RingStep> Perimeter(int cx, int cz, int radius)
        {
            var ring = new List<RingStep>();
            if (radius <= 0)
            {
                ring.Add(new RingStep(cx, cz, 0, 0, true));
                return ring;
            }

            // South side, west to east, inward is +z. Then east, north, west.
            for (var x = cx - radius; x < cx + radius; x++)
            {
                ring.Add(new RingStep(x, cz - radius, 0, 1, x == cx - radius));
            }

            for (var z = cz - radius; z < cz + radius; z++)
            {
                ring.Add(new RingStep(cx + radius, z, -1, 0, z == cz - radius));
            }

            for (var x = cx + radius; x > cx - radius; x--)
            {
                ring.Add(new RingStep(x, cz + radius, 0, -1, x == cx + radius));
            }

            for (var z = cz + radius; z > cz - radius; z--)
            {
                ring.Add(new RingStep(cx - radius, z, 1, 0, z == cz + radius));
            }

            return ring;
        }

        /// <summary>One cell of a square ring, with the direction that leads inward from it.</summary>
        private readonly struct RingStep
        {
            public RingStep(int x, int z, int inX, int inZ, bool corner)
            {
                X = x;
                Z = z;
                InX = inX;
                InZ = inZ;
                Corner = corner;
            }

            /// <summary>Cell X.</summary>
            public int X { get; }

            /// <summary>Cell Z.</summary>
            public int Z { get; }

            /// <summary>Unit step toward the middle, perpendicular to this side.</summary>
            public int InX { get; }

            /// <summary>Unit step toward the middle, perpendicular to this side.</summary>
            public int InZ { get; }

            /// <summary>True at the four corners, where "inward" is diagonal and therefore not a step.</summary>
            public bool Corner { get; }
        }

        private readonly struct Band
        {
            public Band(string name, int inner, int outer, int gates)
            {
                Name = name;
                Inner = inner;
                Outer = outer;
                Gates = gates;
            }

            /// <summary>§12-A's label — 외곽 · 중간 · 안쪽.</summary>
            public string Name { get; }

            /// <summary>Chebyshev radius of the track nearest the middle.</summary>
            public int Inner { get; }

            /// <summary>Chebyshev radius of the track nearest the rim.</summary>
            public int Outer { get; }

            /// <summary>Ways through the wall inside this band. §12-A: 4 · 2 · 1.</summary>
            public int Gates { get; }
        }
    }

    /// <summary>What <see cref="RadialStorey.Build"/> put where, so the caller can hang the rest on it.</summary>
    public sealed class RadialStoreyResult
    {
        /// <summary>Builds an empty result for a storey centred on the given cell.</summary>
        public RadialStoreyResult(int centreX, int centreZ)
        {
            CentreX = centreX;
            CentreZ = centreZ;
            Bands = new List<List<MapCell>>();
            Gates = new List<List<MapCell>>();
            Alcoves = new List<MapCell>();
            GateMouths = new List<MapCell>();
        }

        /// <summary>Cell X of the middle.</summary>
        public int CentreX { get; }

        /// <summary>Cell Z of the middle.</summary>
        public int CentreZ { get; }

        /// <summary>The middle cell itself — where the 투하구 stand.</summary>
        public MapCell Centre { get; set; }

        /// <summary>Cells of each band, innermost first.</summary>
        public List<List<MapCell>> Bands { get; }

        /// <summary>Gate cells of each band, innermost first. Counts are 1 · 2 · 4.</summary>
        public List<List<MapCell>> Gates { get; }

        /// <summary>Blind alcoves — §12's 막힌 길, and the only place on a floor to stand aside.</summary>
        public List<MapCell> Alcoves { get; }

        /// <summary>Where each gate branches off its band — the floor's only three-way junctions.</summary>
        public List<MapCell> GateMouths { get; }
    }
}
