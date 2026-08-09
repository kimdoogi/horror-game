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
    /// are the arguments to <see cref="Build"/> plus the seed.
    /// </para>
    /// <para>
    /// <b>The radii are a budget, and every one of them is spoken for.</b> B-019 (the rim is
    /// 42.5 m too close to the middle) and B-007 (95 m of continuous cover against a 14.4 m
    /// cap) are the same defect read from two ends, and the fix is the same edit: fewer,
    /// longer, straighter legs arranged so the way in spirals. Both are decided by this
    /// table, so it is written out in full:
    /// </para>
    /// <code>
    ///   d 0..1   중심          the 3 x 3 chamber, the 투하구, and on B8 the finish
    ///   d 2      wall          one cell of it — the single 중심 관문, always on an axis
    ///   d 3      안쪽 고리      a broken ring: 23 of its 24 cells (see InnerRadius)
    ///   d 4      —             the 중간 관문's last two steps cross here
    ///   d 5      LANE          the 중간 관문 runs 20 m round this ring before turning in
    ///   d 6      —             the 중간 관문's first two steps cross here
    ///   d 7      중간 고리      the band's rail; jogs at 6 and 12 of each 14-cell side
    ///   d 8      중간 지그재그   the same band's outward leg
    ///   d 9      wall          the four 외곽 관문 cross here, and alcoves hang in it
    ///   d 10     외곽 고리      the band twenty runners start on; jogs at 8 and 14 of 20
    ///   d 11     외곽 지그재그   the outward leg — the rim, and where a 투하구 lands
    /// </code>
    /// <para>
    /// <b>Why the bands jog OUTWARD now, and why that single flip is what unlocked both
    /// blockers.</b> A band two cells thick has to alternate between its rails or its sides
    /// are 50 m sight lines. Which of the two rails it treats as home decides what is left
    /// over: when the 중간 band ran on d6/d7 the wall at d5 was pressed against a rail that
    /// is drawn half the time, so nothing could be built in it. Pushing the same band out to
    /// d7/d8 empties d4, d5 and d6 completely — three clear radii — and d5 becomes a ring
    /// that belongs to nobody. That ring is where the 중간 관문 spends 20 m walking round
    /// before it turns inward, and 20 m added to a passage every route crosses is 20 m added
    /// to the rim→middle walk with <em>no</em> widening of the spread between the shortest and
    /// the longest. Length without spread is the only currency §12-D's 90~140 m band accepts.
    /// </para>
    /// <para>
    /// <b>Every bend on this floor is placed against one number: 12.5 m.</b>
    /// <c>MapValidator</c> groups two bends into one 시야 차단 지점 when the walk between them
    /// is under <see cref="GameConstants.LineOfSightBreakSpacingMin"/> (15 m), and caps the
    /// span of a group at <see cref="GameConstants.SingleCornerMinDistance"/> (14.4 m). On a
    /// 2.5 m grid that means: bends 12.5 m or closer are one 지점, and a 지점 may be at most
    /// 12.5 m across. So a bend may have company, but only within five cells of it, and the
    /// next bend after that has to be six cells away or more. Everything below — where a jog
    /// goes, where a 관문 may leave a band, where an alcove may hang — is that rule applied.
    /// The old floor broke it in one place and it cascaded: a 관문 was three cells of corridor
    /// joining a bend on one band to a bend on the next, so the two bands' bends became one
    /// 지점, and through four 관문 the whole storey became a single piece of cover 95 m deep.
    /// </para>
    /// <para>
    /// <b>The three lengths a 관문 is allowed to be.</b> Two steps, three steps, or six and
    /// over — and nothing between. Under six steps the 관문 welds the bend it leaves to the
    /// bend it arrives at, and the welded span is (bend cluster) + (관문) + (bend cluster);
    /// at two or three steps that totals 10 m or 12.5 m and fits under the cap, at four or
    /// five it is 15 m or 17.5 m and does not. At six steps and over there is no weld at all.
    /// The 외곽 관문 is three steps, the 중심 관문 two, and the 중간 관문 is twelve because it
    /// takes the d5 lane. Nothing on this floor is four or five.
    /// </para>
    /// <para>
    /// <b>The 안쪽 고리 is broken on purpose, and it is the one thing here that could not be
    /// solved any other way.</b> §12-A puts exactly one 관문 into the 중심 and the chamber
    /// piece only opens at the middle of an edge, so that 관문 has to leave the 안쪽 ring on
    /// an axis — three cells from the ring's corner on both sides. A T-junction there is a
    /// bend 7.5 m from two other bends, which makes one 지점 spanning 15.0 m: 0.6 m over the
    /// cap, on every storey, with no arrangement of a 24-cell ring able to avoid it. Removing
    /// the single cell on one side of that junction turns it from a T into an L: the ring
    /// becomes an arc, its far end a 막힌 길, and the walk from the arc's far corner to the
    /// middle becomes 37.5 m of one-way corridor instead of a choice of two short ways round.
    /// The 순환로 the band loses is bought back many times over by the 외곽 and 중간 rings and
    /// by the four 관문 between them — <c>loops</c> reads far above §12's 구역당 1개.
    /// </para>
    /// <para>
    /// <b>Gates are the design.</b> §12-A: four ways from the outer band to the middle, two
    /// from the middle to the inner, and exactly one into the centre. Twenty players share one
    /// cell on the last step. One of the two 중간 관문 carries a door — 1.1 s to shut, 4.5 s to
    /// break, never repaired — which in a race is not protection but a toll charged to
    /// everybody behind.
    /// </para>
    /// <para>
    /// <b>What the seed still decides.</b> The 외곽 관문 are pinned: a 관문 may only leave a
    /// band at a jog and only arrive at one, and on rings of 20 and 14 cells there is exactly
    /// one pair of jogs per side that line up. What the seed picks instead is which axis the
    /// 안쪽 arc opens on and which way it is broken (eight arrangements per storey), which
    /// follows through to which pair of opposite sides carries the two 중간 관문 and which
    /// corner of the arc they arrive at; and the phase of every band's alcoves. Eight storeys
    /// choosing independently is 8^8 buildings before the alcoves are counted, and two seeds
    /// that agree on a storey still disagree on where its 막힌 길 are.
    /// </para>
    /// </summary>
    public static class RadialStorey
    {
        /// <summary>Cells a band is thick. Its rail, and the outward leg of its zigzag.</summary>
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
        /// <b>The band used to jog every 4 cells and that was the first defect.</b> A jog
        /// every 10 m puts a runner within 5 m of cover from anywhere on the floor, and
        /// §12's own arithmetic says cover that close is free: "괴물이 그 모퉁이에 도달하는
        /// 시간 = D / 4.8초, 시야 차단 3초가 필요 → D ≥ 14.4m". §12's own prescription for a
        /// 너무 쉽다 map is "시야 차단 지점을 줄인다", and this is that number.
        /// </para>
        /// <para>
        /// 15~20 m is a one-cell-wide window and both walls are §12's: below 6 cells the
        /// bends are closer than 시야 차단 지점 간격 allows, above 8 they are a straight
        /// corridor longer than the 20 m cap. Every leg this generator draws — on a band, on
        /// the d5 lane, or between a 관문's turns — is 6, 7 or 8 cells and there is no room
        /// to be anywhere else.
        /// </para>
        /// </summary>
        public const int MinLegCells = 6;

        /// <summary>
        /// Cells that must separate two alcoves on the same rail.
        /// <para>
        /// §12 wants 20~25% of a floor's places blind, each with a reason to walk in. A
        /// generated ring has almost none, because every cell of it leads somewhere. So each
        /// band gets alcoves punched into the empty radius beside it: one cell deep, ending
        /// nowhere. In a race they are the only places to stand aside, which makes them the
        /// only places to hide from another player.
        /// </para>
        /// <para>
        /// An alcove hangs a third passage on a rail cell, which makes that cell a bend, so
        /// the spacing is a 시야 차단 지점 number before it is a 막힌 길 number: at 6 cells an
        /// alcove's junction is exactly 15 m from the next one and the two stay separate
        /// 지점. Below that they would chain, and a chain of alcoves is how the old floor got
        /// a 지점 95 m deep.
        /// </para>
        /// </summary>
        public const int AlcoveSpacing = 6;

        /// <summary>
        /// 막힌 길 a storey gets, and the number §12's 20~25% band is met by.
        /// <para>
        /// Every alcove is one leaf node hung on a cell that was a graph node already, so the
        /// ratio is (arc end + alcoves) ÷ (places + alcoves) and it is arithmetic rather than
        /// luck: at 528 places without them, 18 lands on 21.1% and every storey reports the
        /// same figure whatever the seed. The sites outnumber this — see
        /// <see cref="PunchAlcoves"/> — so which of them are taken is the seed's business and
        /// how many are taken is §12's.
        /// </para>
        /// </summary>
        private const int AlcovesPerStorey = 18;

        /// <summary>
        /// Cells from the middle to the furthest thing this generator draws — the 외곽 고리's
        /// outward leg. <c>DescentMap.Radius</c> matches it, so a cell beyond this is rock.
        /// </summary>
        private const int MapRadius = 11;

        /// <summary>Chebyshev radius of the 안쪽 고리 — a 24-cell ring with one cell missing.</summary>
        private const int InnerRadius = 3;

        /// <summary>Chebyshev radius of the empty ring the 중간 관문 walks round. Nothing else is here.</summary>
        private const int LaneRadius = 5;

        /// <summary>
        /// Cells the 중간 관문 walks along <see cref="LaneRadius"/> — 8 × 2.5 m = 20 m, §12's
        /// straight-corridor cap exactly, and the only run length the rings allow.
        /// <para>
        /// It is forced rather than chosen. A 관문 entering the lane at a corner and stepping
        /// two radii inward at the end can only arrive at a corner of the 안쪽 arc if the run
        /// is <c>LaneRadius − InnerRadius = 2</c> cells or <c>LaneRadius + InnerRadius = 8</c>;
        /// two is far shorter than §12's 15 m 시야 차단 지점 간격 floor, so eight is the whole
        /// solution set. Sweeping the radii says the same thing about the pair: with the arc at
        /// 3 and the lane at 5 the run is 8 and legal, and every other pairing that fits inside
        /// <see cref="MapRadius"/> puts it outside 6~8.
        /// </para>
        /// </summary>
        private const int LaneRun = 8;

        /// <summary>Chebyshev radius of the 중간 고리's rail. Its sides are 14 cells.</summary>
        private const int MiddleRadius = 7;

        /// <summary>Chebyshev radius of the 외곽 고리's rail. Its sides are 20 cells.</summary>
        private const int OuterRadius = 10;

        /// <summary>
        /// Where the 중간 고리 jogs, measured from each corner of its 14-cell side.
        /// <para>
        /// Six from the corner and six from each other, which leaves two cells between the
        /// second jog and the next corner. That is the only split of 14 that gives one jog
        /// standing entirely on its own: the pair of bends at 6 is 15 m from the corner behind
        /// it and 15 m from the jog at 12, so it is a 시야 차단 지점 2.5 m across with nothing
        /// else in it. A 외곽 관문 may only arrive at a bend that clean, because it welds
        /// itself to whatever it lands on. The jog at 12 is 5 m from the corner and shares a
        /// 지점 with it — 7.5 m across, still inside the cap — and that is where a 중간 관문
        /// leaves, because a 관문 leaving adds its own first turn to the group.
        /// </para>
        /// </summary>
        private static readonly int[] MiddleJogs = { 6, 12 };

        /// <summary>
        /// Where the 외곽 고리 jogs, measured from each corner of its 20-cell side.
        /// <para>
        /// 8 · 6 · 6 — three legs of 20 m, 15 m and 15 m, every one of them inside §12's
        /// straight-corridor cap and at or over its 시야 차단 지점 간격 floor. Both jogs stand
        /// alone: 20 m from the corner behind, 15 m from each other, 15 m from the corner
        /// ahead. Eight is also the offset that lines the first jog up with the 중간 고리's
        /// clean jog one cell over, which is what lets a 외곽 관문 be three steps instead of
        /// four. See <see cref="PunchOuterGates"/>.
        /// </para>
        /// </summary>
        private static readonly int[] OuterJogs = { 8, 14 };

        /// <summary>
        /// Draws one storey. The caller owns <see cref="MapSketch.AddZone"/> and
        /// <see cref="MapSketch.OnLevel"/>, because a zone's surface and a storey's place in
        /// the building are decisions about the building rather than about this floor.
        /// </summary>
        /// <param name="s">Sketch to draw into.</param>
        /// <param name="level">Storey.</param>
        /// <param name="centreX">Cell X of the middle.</param>
        /// <param name="centreZ">Cell Z of the middle.</param>
        /// <param name="random">Seeded. Decides which axis the 안쪽 arc opens on and the alcove phase.</param>
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

            var result = new RadialStoreyResult(centreX, centreZ);
            var occupied = new HashSet<long>();

            DrawCentre(s, level, centreX, centreZ, occupied);
            result.Centre = new MapCell(centreX, centreZ, level);

            // The seed's two decisions, taken first because everything inward of the 중간
            // 고리 hangs off them: which of the four axes the 중심 관문 stands on, and which
            // side of it the 안쪽 고리 is cut.
            var mouthSide = random.NextInt(0, 4);
            var breakAhead = random.NextInt(0, 2) == 0;

            var inner = DrawInnerArc(s, level, centreX, centreZ, mouthSide, breakAhead, occupied);
            var middle = DrawBand(s, level, centreX, centreZ, MiddleRadius, MiddleJogs, occupied);
            var outer = DrawBand(s, level, centreX, centreZ, OuterRadius, OuterJogs, occupied);

            result.Bands.Add(inner.Cells);
            result.Bands.Add(middle.Cells);
            result.Bands.Add(outer.Cells);

            // ── 관문, innermost first ────────────────────────────────────────────
            //
            // The 중심 관문 is two cells of corridor on an axis: out of the arc's open end,
            // through the d2 wall, into the chamber. It is the one place on the floor where
            // twenty runners have to take turns, and §12-A says so.
            var centreGate = PunchCentreGate(s, level, inner, occupied);
            result.Gates.Add(centreGate);
            result.GateMouths.Add(inner.Mouth);

            var middleGates = PunchMiddleGates(
                s, level, centreX, centreZ, inner, middle, result.GateMouths, occupied);
            result.Gates.Add(middleGates);

            var outerGates = PunchOuterGates(
                s, level, centreX, centreZ, middle, outer, result.GateMouths, occupied);
            result.Gates.Add(outerGates);

            // ── 막힌 길 ─────────────────────────────────────────────────────────
            //
            // Punched after every 관문 exists, because an alcove beside a 관문 cell would
            // turn a passage into a junction and MapSketch.Door refuses to hang a leaf on
            // one. Each band gives up its alcoves to the empty radius beside it: the 외곽
            // band into the d9 wall, the 중간 band into the d6 gap, the 안쪽 arc into d4.
            var gateCells = new List<MapCell>();
            gateCells.AddRange(centreGate);
            gateCells.AddRange(middleGates);
            gateCells.AddRange(outerGates);

            var sites = new List<AlcoveSite>();
            CollectAlcoveSites(outer, sites);
            CollectAlcoveSites(middle, sites);
            CollectCornerAlcoveSites(centreX, centreZ, OuterRadius, sites);
            CollectCornerAlcoveSites(centreX, centreZ, MiddleRadius, sites);
            CollectCornerAlcoveSites(centreX, centreZ, InnerRadius, sites);
            PunchAlcoves(s, level, centreX, centreZ, random, gateCells, sites, result.Alcoves, occupied);

            // Furthest from the way inward first, so the LAST alcove on the finished list is
            // the one nearest the middle. That ordering is a contract: it is what "the alcove
            // nearest the 중심" means to anything that reads Alcoves[^1].
            result.Alcoves.Sort((a, b) => Reach(b, inner.Mouth).CompareTo(Reach(a, inner.Mouth)));

            // One door a storey, on one of the two 중간 관문. Never on the four outer ones and
            // — B-010 — never on the single inner one.
            //
            // The rim is easy: a door on one of four parallel ways in is not a 병목 at all,
            // because the graph says shutting it forces a detour of almost nothing with three
            // other 관문 right there. A door is worth carrying to the place where it costs
            // everybody behind you something, and at the rim it costs them a shrug.
            //
            // The inner 관문 is the opposite mistake and it cost three days. §12 asks for a 문
            // at a 병목 and glosses the point as "잠그면 순환이 끊김 (전략적 선택)" — locking it
            // breaks a CIRCULATION, which presumes there is one to break. There is none through
            // the 중심 관문. It is the only edge into the 중심, so locking it does not charge
            // the field a detour, it deletes the middle from the floor: no 투하구, and on B8 no
            // §02 finish. That is not a strategic choice in a 20-player race, it is a switch
            // that ends the match.
            //
            // §12 caps a zone at 1~2 doors and a floor is one zone, so one is legal and one is
            // what a floor gets.
            if (middleGates.Count > 0)
            {
                s.Door(middleGates[0].X, middleGates[0].Z);
            }

            return result;
        }

        // ====================================================================
        // 중심 — a 3 x 3 room, one piece over the top of it, and 개방 공간.
        // ====================================================================

        private static void DrawCentre(MapSketch s, int level, int cx, int cz, HashSet<long> occupied)
        {
            // Not a corridor: it is the one place on the floor where several players can see
            // each other arrive, and §01 needs the chutes to be a visible choice rather than a
            // cell you stumble into.
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    Put(s, level, null, occupied, cx + dx, cz + dz);
                }
            }

            // And ONE piece over the top of them. B-010.
            //
            // The cells stay — they are what the graph is built from, so the middle is nine
            // places §12 can count, the 중심 관문 has a passage to arrive along, and §02's
            // finish has somewhere to be marked. What changes is the GEOMETRY: the corridor
            // tiler skips any cell inside a room, so instead of nine 2.5 m passages walling
            // each other off it lays Chamber_Open_3x3, which is open at the middle of all four
            // edges and nothing else. That is also why the 중심 관문 has to arrive on an axis,
            // and why the 안쪽 고리 has to be broken to make room for it — see the class docs.
            s.Room(MapKitPiece.ChamberOpen3x3, cx - 1, cz - 1, 3, 3, 0f);

            // And it is 개방 공간, which nobody had told §12.
            //
            // This is a correction to a MEASUREMENT before it is anything else. The middle is a
            // room you can see across; the nine cells under the chamber were being counted as
            // nine corridor cells with bends in them, so both MapValidator's sight-break census
            // and RunnerTest's cover test treated the inside of a lit 7.5 m room as somewhere a
            // sight line breaks. It is not.
            //
            // It does NOT satisfy open-adjacent-to-maze's own reason for existing: §12 says in
            // the same breath what an 개방 공간 is FOR — "멀리서 어그로를 건다 · 시야 15~25m
            // 확보" — and every one of these chambers spans 7.1 m between its furthest two
            // cells. MapValidator measures that span and says it out loud. What a floor would
            // have to GAIN is a real 개방 공간 at least 15 m across on its own storey, and the
            // only kit piece that wide is 6.3 m tall on a 3.75 m storey, so in a tower where
            // all eight floors share one square it can only ever be built on B1.
            s.OpenRoom(cx - 1, cz - 1, 3, 3);
        }

        // ====================================================================
        // 안쪽 고리 — the broken ring, and the 중심 관문 at its open end.
        // ====================================================================

        /// <summary>
        /// Draws the 안쪽 고리 as an arc: every cell of the radius-3 ring except the one next
        /// to the 중심 관문's mouth.
        /// <para>
        /// <b>Why a cell is missing.</b> The chamber opens at the middle of an edge, so the
        /// 중심 관문 leaves this ring on an axis, three cells from a corner on either side. As
        /// a T-junction that mouth is a bend 7.5 m from two other bends and the three of them
        /// are one 시야 차단 지점 15.0 m across — over
        /// <see cref="GameConstants.SingleCornerMinDistance"/> by 0.6 m, on every storey, and
        /// nothing about a 24-cell ring can move it: its four corners are 15 m apart by
        /// construction and any fifth bend on it lands inside 12.5 m of two of them. Drop the
        /// cell on one side and the mouth is an L instead: it shares a 7.5 m 지점 with one
        /// corner, the other three corners stand alone 15 m apart, and the arc's far end
        /// becomes a 막힌 길 §12 was asking for anyway.
        /// </para>
        /// <para>
        /// <b>And it is what makes the last ring worth walking.</b> Closed, this ring offers a
        /// runner arriving at a corner two ways round and charges 22.5 m. Broken, the corner
        /// the 중간 관문 arrive at is 37.5 m of one-way corridor from the middle, and the way
        /// back out is not a shortcut — which is §01's 「맵을 아는 사람이 유리하다」 written as
        /// one missing cell.
        /// </para>
        /// </summary>
        private static InnerArc DrawInnerArc(
            MapSketch s, int level, int cx, int cz, int mouthSide, bool breakAhead, HashSet<long> occupied)
        {
            var ring = Perimeter(cx, cz, InnerRadius);
            var side = InnerRadius * 2;

            // The middle cell of a side is the one standing on an axis, and the chamber only
            // opens on an axis.
            var mouthIndex = (side * mouthSide) + InnerRadius;
            var breakIndex = Wrap(mouthIndex + (breakAhead ? 1 : -1), ring.Count);

            var arc = new InnerArc
            {
                Cells = new List<MapCell>(),
                Step = ring[mouthIndex],
                Distance = new Dictionary<long, int>(),
            };

            for (var i = 0; i < ring.Count; i++)
            {
                if (i == breakIndex)
                {
                    continue;
                }

                Put(s, level, arc.Cells, occupied, ring[i].X, ring[i].Z);
            }

            arc.Mouth = new MapCell(ring[mouthIndex].X, ring[mouthIndex].Z, level);

            // Walking away from the break is the only direction the arc goes, so a cell's
            // distance from the middle is just how far along the arc it sits. The four corners
            // land at 3, 9, 15 and 21 cells, and the two 중간 관문 arrive at the middle pair —
            // 22.5 m and 37.5 m — because that is what puts §12-D's total inside 90~140 m
            // without spreading the shortest and longest routes apart. See PunchMiddleGates.
            var direction = breakAhead ? -1 : 1;
            for (var t = 0; t < ring.Count - 1; t++)
            {
                var at = ring[Wrap(mouthIndex + (direction * t), ring.Count)];
                arc.Distance[Key(at.X, at.Z)] = t;
            }

            return arc;
        }

        /// <summary>Cells of arc between the 중심 관문 and the nearer of the two 중간 관문.</summary>
        private const int MiddleGateArcNear = 9;

        /// <summary>Cells of arc between the 중심 관문 and the further of the two 중간 관문.</summary>
        private const int MiddleGateArcFar = 21;

        /// <summary>Cuts the single 중심 관문: out of the arc's open end, through d2, into the chamber.</summary>
        private static List<MapCell> PunchCentreGate(
            MapSketch s, int level, InnerArc arc, HashSet<long> occupied)
        {
            var gates = new List<MapCell>();
            var step = arc.Step;

            // Two steps. The first is the wall cell §12-A counts as the 관문 and the second
            // docks against the chamber's own doorway. Two rather than three because at four
            // and five steps a 관문 welds the bends at its ends into one over-cap 시야 차단
            // 지점 — see the class docs.
            for (var t = 1; t <= 2; t++)
            {
                var x = step.X + (step.InX * t);
                var z = step.Z + (step.InZ * t);
                Put(s, level, null, occupied, x, z);
                if (t == 1)
                {
                    gates.Add(new MapCell(x, z, level));
                }
            }

            return gates;
        }

        // ====================================================================
        // The two zigzag bands.
        // ====================================================================

        /// <summary>
        /// Draws a band as a zigzag between its rail at <paramref name="radius"/> and an
        /// outward leg one cell further out, and returns every cell it used.
        /// <para>
        /// <b>The band is a zigzag, and that is load-bearing.</b> A ring band two cells thick
        /// could be drawn as two concentric squares, which would be a 50 m straight run on
        /// every side — two and a half times §12's 20 m cap, and a sight line down which
        /// nothing survives. So the band alternates between its rail and the leg outside it,
        /// connected by a rung. That single decision buys three §12 rules at once: no straight
        /// over the leg length, an S자 통로 at every jog, and one 순환로 per band because the
        /// zigzag closes on itself.
        /// </para>
        /// <para>
        /// <b>It jogs OUTWARD, which is the change B-019 turned on.</b> Drawn inward, a band
        /// presses against the radius on its inside and there is nowhere to build anything
        /// there; drawn outward it leaves that radius empty, and an empty ring is where a 관문
        /// can spend 20 m of walking. The wall it jogs into is the one on the far side from
        /// the middle, which nothing else wants: the 외곽 band's outward leg is the rim itself
        /// and the 중간 band's is pressed against the d9 wall the 외곽 관문 cross.
        /// </para>
        /// <para>
        /// <b>Jogs are planned per SIDE, not counted along the walk.</b> An earlier version
        /// carried a running counter and jogged whenever it reached a period, suppressing the
        /// jog near a corner. That has a latent defect which one period happened to miss and
        /// which any other hits: the near-corner rule forces the cell back onto the rail
        /// without drawing a rung, so if the walk arrives at a corner while on the outward leg
        /// the two cells are diagonal neighbours and the band is cut in half. Measured:
        /// changing nothing but the period took the tower from 1 connected piece to <b>5</b>,
        /// with no exception thrown — MapSketch only refuses a cell that touches NOTHING, and
        /// both halves of a severed ring still touch themselves. Planning per side makes the
        /// parity a property of the plan instead of an accident of the arithmetic: an even
        /// number of jogs per side means the rail is always back home by the time the corner
        /// arrives.
        /// </para>
        /// </summary>
        private static ZigzagBand DrawBand(
            MapSketch s, int level, int cx, int cz, int radius, int[] jogs, HashSet<long> occupied)
        {
            var band = new ZigzagBand
            {
                Cells = new List<MapCell>(),
                Radius = radius,
                Jogs = new List<BandJog>(),
            };

            var ring = Perimeter(cx, cz, radius);
            var side = radius * 2;

            // 0 is the rail, 1 the leg one cell further out.
            var offset = 0;

            for (var i = 0; i < ring.Count; i++)
            {
                var step = ring[i];

                // Corners AND their two neighbours sit on the rail. A corner's outward
                // neighbour is diagonal to the next side's and §12 has no diagonals, so a
                // corner can only be reached along the rail it is on — and if the cell before
                // or after it had jogged outward, the corner would be left touching nothing at
                // all. That is not a cosmetic gap: MapSketch refuses to build a map with an
                // orphan cell in it, which is how this was found rather than shipped.
                var nearCorner = step.Corner
                                 || ring[(i + 1) % ring.Count].Corner
                                 || ring[(i + ring.Count - 1) % ring.Count].Corner;
                var here = nearCorner ? 0 : offset;
                Put(s, level, band.Cells, occupied, step.X - (step.InX * here), step.Z - (step.InZ * here));

                if (nearCorner || Array.IndexOf(jogs, i % side) < 0)
                {
                    continue;
                }

                // The rung. Both cells exist, which is what keeps the walk connected across
                // the flip, and both of them are bends — a 시야 차단 지점 2.5 m across.
                Put(s, level, band.Cells, occupied, step.X, step.Z);
                Put(s, level, band.Cells, occupied, step.X - step.InX, step.Z - step.InZ);

                band.Jogs.Add(new BandJog
                {
                    Offset = i % side,
                    Index = i,
                    Rail = new MapCell(step.X, step.Z, level),
                    Leg = new MapCell(step.X - step.InX, step.Z - step.InZ, level),
                    Step = step,
                });

                offset = offset == 0 ? 1 : 0;
            }

            // The seam closes by construction rather than by luck: every side carries an even
            // number of jogs, so the walk is back on the rail at every corner including the
            // last one.
            return band;
        }

        // ====================================================================
        // 관문.
        // ====================================================================

        /// <summary>
        /// Cuts the two 중간 관문: out of the 중간 고리 at a jog, straight down to the empty
        /// d5 ring, twenty metres round it, and in to a corner of the 안쪽 arc.
        /// <para>
        /// <b>Twenty metres round a ring nobody lives on is what closed B-019.</b> Every route
        /// from the rim to the middle crosses this 관문 exactly once, so its length is added
        /// to the shortest walk and the longest walk alike — the only kind of length that
        /// moves §12-D's floor without pushing its ceiling. Measured on the shipped seed the
        /// rim→중심 walk was 47.5~82.5 m against a 90~140 m band; the lane, the 관문's four
        /// radial steps and the broken arc together put it inside.
        /// </para>
        /// <para>
        /// <b>Every cell of it is a bend placed on purpose.</b> Leaving the band at the jog
        /// 12 cells along a side means the mouth already shares a 지점 with the corner two
        /// cells further on, and the 관문's first turn — where it meets the d5 ring — is two
        /// steps beyond the mouth, so the whole group is 10 m across. Then 20 m of straight to
        /// the lane's own corner, 20 m more to the turn inward, and two steps to the arc: three
        /// legs, each inside §12's straight cap and at or over its 시야 차단 지점 간격 floor.
        /// The arrival is a corner of the arc that is already a bend, so it costs nothing.
        /// </para>
        /// <para>
        /// <b>Why the geometry has no free parameters.</b> A jog 12 cells along a 14-cell side
        /// of the radius-7 ring stands five cells off the axis, so dropping two radii lands
        /// exactly on a corner of the radius-5 lane; eight cells round that lane from a corner
        /// lands exactly two cells short of the next one, and dropping two radii from there
        /// lands exactly on a corner of the radius-3 arc. Nothing was tuned to make that
        /// happen — it is what a Chebyshev ring does when you step two radii inward — and it
        /// is why the two 관문 that arrive at one arc corner come from opposite sides of the
        /// floor rather than from adjacent ones.
        /// </para>
        /// </summary>
        private static List<MapCell> PunchMiddleGates(
            MapSketch s,
            int level,
            int cx,
            int cz,
            InnerArc arc,
            ZigzagBand middle,
            List<MapCell> mouths,
            HashSet<long> occupied)
        {
            var gates = new List<MapCell>();
            var lane = Perimeter(cx, cz, LaneRadius);

            for (var j = 0; j < middle.Jogs.Count; j++)
            {
                var jog = middle.Jogs[j];
                if (jog.Offset != MiddleJogs[1])
                {
                    // The clean jog is where the 외곽 관문 arrive; a 관문 leaving there too
                    // would chain the two bands into one 지점 through both of them.
                    continue;
                }

                var step = jog.Step;

                // Down one radius, one cell toward the axis of the side, then down again.
                //
                // Two straight steps inward from a jog 12 cells along a 14-cell side would land
                // on a CORNER of the lane, and a corner offers only one legal way round: the
                // other side of it carries straight on in the direction the 관문 arrived, which
                // welds the descent and the whole lane leg into one 27.5 m sight line. The
                // sideways cell moves the entry one off the corner, which does three things at
                // once — both ways round the lane are now turns, the corner itself becomes a
                // bend the 관문 can use, and the two ways round are DIFFERENT LENGTHS. That last
                // one is what makes §12-D's band reachable: the 관문 that arrives at the far end
                // of the arc takes the short way and the one that arrives near the middle takes
                // the long way, so both routes to the 중심 cost within a few metres of the same.
                // Two steps inward puts a jog at offset 12 of a 14-cell side onto a corner of
                // the lane. Which corner is decided by which side the jog is on.
                var entryX = step.X + (step.InX * 2);
                var entryZ = step.Z + (step.InZ * 2);
                var entry = IndexOf(lane, entryX, entryZ);
                if (entry < 0)
                {
                    continue;
                }

                for (var d = -1; d <= 1; d += 2)
                {
                    // Only one of the two ways round the lane is a turn.
                    //
                    // The 관문 comes down onto the lane's corner along one axis, and one of the
                    // lane's two sides at that corner carries straight on in the same
                    // direction. Taking it would join the 관문's two radial steps to the whole
                    // 20 m lane leg as ONE sight line — measured, seed 20260802: 27.5 m against
                    // §12's 20 m cap, and the rule named the 도달 지점 sitting in the middle of
                    // it. The other way is perpendicular, so the corner is a bend and both legs
                    // are inside the cap.
                    var first = lane[Wrap(entry + d, lane.Count)];
                    if (((first.X - entryX) * step.InX) + ((first.Z - entryZ) * step.InZ) != 0)
                    {
                        continue;
                    }

                    var exit = Wrap(entry + (d * LaneRun), lane.Count);
                    var landing = lane[exit];
                    var reached = Key(landing.X + (landing.InX * 2), landing.Z + (landing.InZ * 2));
                    if (!arc.Distance.TryGetValue(reached, out var along)
                        || (along != MiddleGateArcNear && along != MiddleGateArcFar))
                    {
                        continue;
                    }

                    Put(s, level, null, occupied, step.X + step.InX, step.Z + step.InZ);
                    Put(s, level, null, occupied, entryX, entryZ);

                    for (var t = 1; t <= LaneRun; t++)
                    {
                        var at = lane[Wrap(entry + (d * t), lane.Count)];
                        Put(s, level, null, occupied, at.X, at.Z);
                    }

                    // And in to the arc. This last cell is the only one on the whole 관문 that
                    // is a plain PASSAGE — the descent turns at the dog-leg, the lane turns at
                    // both ends — so it is where the storey's one 문 hangs. MapSketch refuses a
                    // door anywhere else, and it is right to: §12 hangs a 문 "순환로의 목에" and
                    // the validator prices it as the detour of one edge, which a bend does not
                    // have. Shutting this one costs the whole field the walk round to the other
                    // 중간 관문, which is what §12 means by a 병목.
                    var doorCell = new MapCell(landing.X + landing.InX, landing.Z + landing.InZ, level);
                    Put(s, level, null, occupied, doorCell.X, doorCell.Z);

                    gates.Add(doorCell);
                    mouths.Add(jog.Rail);
                    break;
                }
            }

            return gates;
        }

        /// <summary>
        /// Cuts the four 외곽 관문 through the d9 wall.
        /// <para>
        /// <b>Three steps, and one of them sideways.</b> A 관문 has to leave a bend and arrive
        /// at one — anywhere else it plants a new bend in the middle of a leg, which lands
        /// inside 12.5 m of the jogs on either side and chains them. On a 20-cell side the
        /// 외곽 고리's first jog stands two cells off the axis; on a 14-cell side the 중간
        /// 고리's clean jog stands one cell off it. They do not line up, and they cannot be
        /// made to: every legal jog schedule for those two sides is pinned by §12's own 6~8
        /// cell leg window. So the 관문 steps in, one cell along, and in again — and the
        /// sideways step costs 2.5 m, which is what keeps the welded group at 12.5 m instead
        /// of 10. That is under
        /// <see cref="GameConstants.SingleCornerMinDistance"/> and it is the tightest thing on
        /// this floor.
        /// </para>
        /// <para>
        /// <b>Which is also why there are exactly four.</b> One aligned pair of jogs per side,
        /// four sides, four 관문 — §12-A's count is not imposed on this geometry, it falls out
        /// of it. The cost is that the four bearings are the same on every storey and every
        /// seed; what varies is everything inward of them.
        /// </para>
        /// </summary>
        private static List<MapCell> PunchOuterGates(
            MapSketch s,
            int level,
            int cx,
            int cz,
            ZigzagBand middle,
            ZigzagBand outer,
            List<MapCell> mouths,
            HashSet<long> occupied)
        {
            var gates = new List<MapCell>();

            for (var j = 0; j < outer.Jogs.Count; j++)
            {
                var jog = outer.Jogs[j];
                if (jog.Offset != OuterJogs[0])
                {
                    continue;
                }

                var step = jog.Step;

                // One step in, one along, one in. The along-step is toward the axis of the
                // side, which is where the 중간 고리's clean jog is.
                var wallX = step.X + step.InX;
                var wallZ = step.Z + step.InZ;

                // Perpendicular to the inward step, in the direction the ring is walked.
                var alongX = step.InZ;
                var alongZ = -step.InX;

                var overX = wallX + alongX;
                var overZ = wallZ + alongZ;
                var landingX = overX + step.InX;
                var landingZ = overZ + step.InZ;

                if (!middle.Cells.Exists(c => c.X == landingX && c.Z == landingZ))
                {
                    continue;
                }

                Put(s, level, null, occupied, wallX, wallZ);
                Put(s, level, null, occupied, overX, overZ);

                gates.Add(new MapCell(wallX, wallZ, level));
                mouths.Add(jog.Rail);
            }

            return gates;
        }

        // ====================================================================
        // 막힌 길.
        // ====================================================================

        /// <summary>
        /// Punches one-cell blind alcoves, and punches every one of them off a cell that is
        /// already a bend.
        /// <para>
        /// <b>That restriction is the whole method, and it is a 시야 차단 지점 rule before it
        /// is a 막힌 길 rule.</b> An alcove hangs a third passage on a rail cell, which turns
        /// that cell into a bend. Put it in the middle of a leg and it lands inside 12.5 m of
        /// the jogs at both ends, chaining them into one 지점 — measured, seed 20260802,
        /// alcoves punched every 6 bearings: the deepest 지점 went from 12.5 m to <b>55 m</b>
        /// and 60 of 92 지점 were over §12's cap, on a floor that had passed the rule cleanly
        /// the round before. There is no spacing that fixes it either: with the 외곽 고리's
        /// corners at 0 and 20 and its jogs at 8 and 14, no offset on that side is six cells
        /// clear of all four.
        /// </para>
        /// <para>
        /// So an alcove goes where a bend already is — the two cells of a jog, and the ring
        /// corners — and costs the census nothing at all: the cell was a 시야 차단 지점 before
        /// the alcove and is the same one after, because a leaf is not a bend and the junction
        /// it hangs off was already counted. §12's 20~25% 막힌 길 is then a question of how
        /// many of those sites are taken, which is <see cref="AlcovesPerStorey"/>.
        /// </para>
        /// </summary>
        private static void PunchAlcoves(
            MapSketch s,
            int level,
            int cx,
            int cz,
            IRandomSource random,
            List<MapCell> gates,
            List<AlcoveSite> sites,
            List<MapCell> into,
            HashSet<long> occupied)
        {
            // A seeded rotation rather than a seeded roll per site. An earlier version rolled
            // for each bearing and the 막힌 길 ratio wandered with the seed — 18.0% at one
            // rate, 19.1% at another, never reliably inside §12's band. Taking a fixed COUNT
            // from a rotated list puts the same number of 막힌 길 on every floor and still
            // puts them somewhere else on every seed.
            var start = sites.Count == 0 ? 0 : random.NextInt(0, sites.Count);
            var punched = 0;

            for (var k = 0; k < sites.Count && punched < AlcovesPerStorey; k++)
            {
                var site = sites[(k + start) % sites.Count];
                var x = site.X;
                var z = site.Z;

                // Inside the zone. RadialStorey draws out to the 외곽 고리's outward leg and
                // DescentMap sizes the storey to hold exactly that, so a site one cell further
                // out is a cell in the rock.
                if (Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) > MapRadius)
                {
                    continue;
                }

                // Not on a 관문 cell, and not beside one either. A 관문 is a passage and its
                // cells have to stay passages: an alcove touching one turns it into a junction,
                // and MapSketch.Door refuses to hang a leaf on a junction because there is no
                // single edge for the validator to measure the detour of.
                if (gates.Exists(gate => Math.Abs(gate.X - x) + Math.Abs(gate.Z - z) <= 1))
                {
                    continue;
                }

                // The cell has to become a genuine leaf: empty now, with exactly one occupied
                // neighbour to hang off. Without this check, two alcoves punched from opposite
                // sides of the same wall met in the middle and became a passage — 막힌 길 fell
                // to 12.1% while loops jumped from 17 to 27 and a 25 m straight appeared, all
                // three from the same cause. An alcove that joins something is not an alcove.
                if (occupied.Contains(Key(x, z)) || Neighbours(occupied, x, z) != 1)
                {
                    continue;
                }

                // And it may not lengthen the straight it hangs off. An alcove is a cell in
                // line with the corridor on the far side of its junction, so at a ring corner
                // it does not turn — it extends one of the two legs meeting there by one cell.
                // Measured, seed 20260802: an alcove on a 외곽 corner turned that band's 20 m
                // leg into 22.5 m and straight-corridor failed on a floor that had just passed
                // it. The other outward cell at the same corner extends the 15 m leg instead
                // and is fine, which is why both are offered and this decides between them.
                // MaxLegCells + 1, because a run of N cells is N − 1 steps of travel: nine
                // cells in a line is §12's 20 m exactly and ten is 22.5 m, which is the number
                // straight-corridor reported.
                if (StraightCells(occupied, x, z) > MaxLegCells + 1)
                {
                    continue;
                }

                Put(s, level, into, occupied, x, z);
                punched++;
            }
        }

        /// <summary>Every cell an alcove could hang off: the two cells of each jog, and each corner.</summary>
        private static void CollectAlcoveSites(ZigzagBand band, List<AlcoveSite> into)
        {
            for (var i = 0; i < band.Jogs.Count; i++)
            {
                var jog = band.Jogs[i];

                // Inward off the rail, and outward off the leg — the two directions that lead
                // into the empty radius on either side of a band.
                into.Add(new AlcoveSite(jog.Rail.X + jog.Step.InX, jog.Rail.Z + jog.Step.InZ));
                into.Add(new AlcoveSite(jog.Leg.X - jog.Step.InX, jog.Leg.Z - jog.Step.InZ));
            }
        }

        /// <summary>Every cell an alcove could hang off a ring's four corners — outward, on both axes.</summary>
        private static void CollectCornerAlcoveSites(int cx, int cz, int radius, List<AlcoveSite> into)
        {
            var ring = Perimeter(cx, cz, radius);
            for (var i = 0; i < ring.Count; i++)
            {
                if (!ring[i].Corner)
                {
                    continue;
                }

                // A corner has no single outward axis — both of them lead out of the ring —
                // so both are offered and whichever is legal is taken.
                into.Add(new AlcoveSite(ring[i].X + (ring[i].X > cx ? 1 : -1), ring[i].Z));
                into.Add(new AlcoveSite(ring[i].X, ring[i].Z + (ring[i].Z > cz ? 1 : -1)));
            }
        }

        // ====================================================================
        // Plumbing.
        // ====================================================================

        private static int IndexOf(List<RingStep> ring, int x, int z)
        {
            for (var i = 0; i < ring.Count; i++)
            {
                if (ring[i].X == x && ring[i].Z == z)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int Wrap(int value, int count) => ((value % count) + count) % count;

        private static void Put(
            MapSketch s, int level, List<MapCell>? into, HashSet<long> occupied, int x, int z)
        {
            if (!occupied.Add(Key(x, z)))
            {
                return;
            }

            s.Corridor(x, z, x, z);
            into?.Add(new MapCell(x, z, level));
        }

        /// <summary>
        /// Manhattan cells between two places on a storey — a stand-in for the walk, used only
        /// to order alcoves by how near the way inward they are. Manhattan rather than
        /// Chebyshev because the corridors have no diagonals, so it is the walk a runner takes
        /// wherever the ring is not in the way.
        /// </summary>
        private static int Reach(MapCell cell, MapCell to) =>
            Math.Abs(cell.X - to.X) + Math.Abs(cell.Z - to.Z);

        /// <summary>Packs a cell into one long so a HashSet can hold it without allocating.</summary>
        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>
        /// Drawn cells in line with an empty cell's single neighbour — how long a straight this
        /// cell would join if it were drawn, counting the cell itself.
        /// </summary>
        private static int StraightCells(HashSet<long> occupied, int x, int z)
        {
            var longest = 0;
            for (var axis = 0; axis < 2; axis++)
            {
                var dx = axis == 0 ? 1 : 0;
                var dz = axis == 0 ? 0 : 1;
                var run = 1;
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    for (var t = 1; ; t++)
                    {
                        if (!occupied.Contains(Key(x + (dx * sign * t), z + (dz * sign * t))))
                        {
                            break;
                        }

                        run++;
                    }
                }

                longest = run > longest ? run : longest;
            }

            return longest;
        }

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
        /// axis-aligned with its neighbours. §12 has no diagonals — the kit docks on grid edges
        /// and a bend is measured from geometry — so a round ring would be a staircase of
        /// one-cell steps and every one of them a bend.
        /// </para>
        /// <para>
        /// The inward step is carried rather than computed because it is only well defined on a
        /// side: at a corner both axes lead inward and neither alone does. Corners are flagged
        /// so callers can leave them alone.
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

        /// <summary>One flip of a zigzag band: the rail cell, the leg cell, and where it sits.</summary>
        private struct BandJog
        {
            /// <summary>Cells from this side's corner.</summary>
            public int Offset;

            /// <summary>Index into the band's own perimeter walk.</summary>
            public int Index;

            /// <summary>The cell on the band's rail — where a 관문 may leave.</summary>
            public MapCell Rail;

            /// <summary>The cell one step outward — where a 관문 from outside may arrive.</summary>
            public MapCell Leg;

            /// <summary>The ring step the rail cell came from, for the inward direction.</summary>
            public RingStep Step;
        }

        /// <summary>A cell an alcove may be punched into, hanging off a bend that already exists.</summary>
        private readonly struct AlcoveSite
        {
            public AlcoveSite(int x, int z)
            {
                X = x;
                Z = z;
            }

            /// <summary>Cell X of the alcove itself.</summary>
            public int X { get; }

            /// <summary>Cell Z of the alcove itself.</summary>
            public int Z { get; }
        }

        /// <summary>A drawn zigzag band and the jogs along it.</summary>
        private struct ZigzagBand
        {
            /// <summary>Every cell of the band.</summary>
            public List<MapCell> Cells;

            /// <summary>Chebyshev radius of the rail.</summary>
            public int Radius;

            /// <summary>Every flip, in the order the ring was walked.</summary>
            public List<BandJog> Jogs;
        }

        /// <summary>The broken 안쪽 고리, its open end, and the corner the 중간 관문 arrive at.</summary>
        private struct InnerArc
        {
            /// <summary>Every cell of the arc.</summary>
            public List<MapCell> Cells;

            /// <summary>The open end, on an axis, where the 중심 관문 leaves.</summary>
            public MapCell Mouth;

            /// <summary>The ring step the mouth came from, for the inward direction.</summary>
            public RingStep Step;

            /// <summary>Cells of arc between the 중심 관문 and each cell of the arc.</summary>
            public Dictionary<long, int> Distance;
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

        /// <summary>Where each gate leaves its band — the floor's three-way junctions.</summary>
        public List<MapCell> GateMouths { get; }
    }
}
