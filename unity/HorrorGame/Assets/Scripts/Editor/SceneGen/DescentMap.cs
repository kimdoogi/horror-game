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
    ///                    20명이 외곽 고리에서 출발.  층마다 괴물 하나, 중심에서 시작
    ///                    투하구는 중심, 착지는 다음 층의 외곽      괴물이 듣는 거리
    ///  B1 [콘크리트]  ─┐                                            17.5 m
    ///  B2 [나무]      ─┤                                            28.0 m
    ///  B3 [금속]      ─┤                                            35.0 m
    ///  B4 [자갈]      ─┤                                            24.5 m
    ///  B5 [타일]      ─┤                                            29.8 m
    ///  B6 [카펫]      ─┤                                             7.7 m
    ///  B7 [물]        ─┤                                            35.0 m
    ///  B8 [흙]        ─┘  중심 = 도착점                             14.0 m
    /// </code>
    /// <para>
    /// <b>그 오른쪽 열은 고른 값이 아니라 계산된 값이다.</b> A creature hears a footstep
    /// when the WALKED distance to it is under
    /// <c>GameConstants.MonsterFootstepHearingRange</c> × the surface's §12 clarity ×
    /// how hard the runner is working — <c>MatchDirector.ReportFootsteps</c> hands that
    /// range to <c>MonsterBrain.FindLoudestAudibleSound</c>, which measures with
    /// <c>NavigableDistance</c> and not through walls. 35 m × <c>ListenerClarity*</c> is
    /// therefore the radius each floor's creature owns, and because §12 gives every storey
    /// its own surface, the same creature is a different animal on every floor: it hears a
    /// third of B3 and B7 and one fourteenth of B6. Nobody chose that curve; the surface
    /// alphabet chose it. See <see cref="PlaceStarts"/>.
    /// </para>
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
        /// Twenty starting positions round the rim of B1, and one creature on every storey.
        /// <para>
        /// The starts are spread over the outer band's own cell list so nobody begins beside
        /// anybody: twenty players on a 80-cell ring is one every four cells, which is far
        /// enough that the first thing you see is a corridor rather than a crowd.
        /// </para>
        /// <para>
        /// <b>Eight creatures, one per storey, because §07 asks for eight and the building
        /// cannot move one.</b> §07's 순찰 column is written in ZONES — "1개 구역", "2개
        /// 구역", "절반", "전체" — and on this map a zone IS a storey: <see cref="Build"/>
        /// calls <c>AddZone</c> once per level, so the building has eight of them, inside
        /// <c>GameConstants.ZoneCountMax</c>. Run §07's own table through
        /// <c>ThreatTier.PatrolZoneCountFor(8)</c> and it asks for 1 zone at 초저녁, 2 at
        /// 밤, 4 at 심야 (<c>PatrolScope.HalfTheMap</c>) and 8 at 새벽 and 동트기 전
        /// (<c>WholeMap</c>). A creature cannot answer that by walking: a
        /// <see cref="MapSketch.Chute"/> is one-way and is not a NavMesh link, so the storey
        /// a creature is authored on is the only storey it will ever stand on. Eight zones
        /// asked for at minute 24, eight creatures authored at minute 0. One creature at
        /// B5's middle can satisfy §07's first row and nothing after it.
        /// </para>
        /// <para>
        /// <b>B1 included, and the objection that kept it empty does not survive being
        /// measured.</b> The old comment here read "put it on B1 and twenty people meet it at
        /// the starting line". They do not. The creature is seeded at the MIDDLE, and §12-D
        /// measured this generator's own 외곽→중심 path at 118 m; a creature hears a footstep
        /// only when the walked distance is under 35 m × the surface's clarity, and B1 is
        /// 콘크리트 — <c>GameConstants.ListenerClarityConcrete</c> 0.50, so 17.5 m. The
        /// starting ring is a hundred metres outside its hearing. It is also outrun: §07's
        /// 초저녁 row is <c>ThreatSpeedEarlyEvening</c> 4.4 m/s, BELOW §05's 달리기 4.5, so
        /// for the first eight minutes — the whole of a leader's race, 8 × 26 s — a creature
        /// costs a detour and a sprint rather than a life. §06's "달리기로는 도망칠 수 없다"
        /// is the 심야 row, and 심야 belongs to whoever is late (§07 지각자 처벌).
        /// </para>
        /// <para>
        /// <b>One kind of creature, not eight stat blocks.</b> §07 makes speed, 정지 and the
        /// flashlight penalty functions of the CLOCK — <c>ThreatTier</c>'s constructor is
        /// <c>internal</c> precisely so nothing can invent a per-floor monster — so a second
        /// ladder written into the map would contradict the one §07 already runs. The
        /// per-floor difference the descent needs is here anyway and is 4.5× wide: hearing is
        /// 35 m × <c>ListenerClarity</c>, and §12 gives each storey its own surface, so the
        /// creature owns 17.5 m of B1, 28.0 of B2, 35.0 of B3, 24.5 of B4, 29.8 of B5, 7.7 of
        /// B6, 35.0 of B7 and 14.0 of B8. <b>That sequence is not a descent curve</b> — it
        /// peaks at B3 and B7, bottoms at B6, and B8's 흙 (0.40) is the second quietest floor
        /// in the building while holding §02's finish. The escalation with depth is the
        /// clock's, because in a race downward depth IS elapsed time; the map's job is only
        /// to make sure §07 has somewhere to escalate on every floor.
        /// </para>
        /// <para>
        /// <b>The middle of the floor, which is a seed and not a post.</b> DESCENT-PIVOT §2③:
        /// "괴물은 중심에서 시작해 안쪽 두 고리를 돈다". At t=0 every runner is on B1's rim,
        /// 118 m from every middle in the building, so no creature begins the match within
        /// hearing of anybody; by the time the leader reaches a middle its creature has
        /// patrolled off it. What the seed guarantees is §12-B lever 3 — 외곽은 안전하고
        /// 중심은 위험하다 — on all eight floors instead of one. B8 is the one place this
        /// rule lands on something else: §12-C says out loud that "B8의 중심은 투하구가
        /// 아니라 도착점", so B8's creature is the only one seeded on an 출입구. That is the
        /// situation §12's own <c>concealment-near-exit</c> rule was written for (§07 새벽),
        /// and this map already satisfies it.
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

            // THE PRIMARY IS DECLARED LAST, and that ordering is the whole safety of this
            // change. MapSketch.MonsterStart as it stands keeps ONE creature — the last
            // call overwrites the field — and the runtime downstream is still
            // single-creature too: MatchMap hands MatchDirector MonsterSpawns[0] and
            // MonsterShot.IsAnchor matches the bare name "MonsterSpawn". So the last
            // declaration is the primary, in both a MapSketch that has grown a list and one
            // that has not. Put B5's middle last and an unpatched MapSketch produces exactly
            // the map that ships today — one creature at B5, y −15.0 — rather than silently
            // moving it to whichever floor happened to be authored last. The change is inert
            // if its other half does not land, which is the only honest way to write half a
            // change into a file you own when the other half is in a file you do not.
            //
            // B5 is the primary for the reason the single placement always had: it is
            // Storeys / 2, half way down, so a build that can only carry one creature carries
            // it where §12-B wants the descent to turn dangerous rather than start that way.
            var primary = Storeys / 2;

            for (var level = 0; level < Storeys; level++)
            {
                if (level == primary)
                {
                    continue;
                }

                SeedCreature(sketch, floors[level], level);
            }

            SeedCreature(sketch, floors[primary], primary);
        }

        /// <summary>
        /// Puts one creature in the middle of a storey.
        /// <para>
        /// From the floor's OWN recorded middle rather than from <see cref="Centre"/>, for
        /// the same reason <see cref="RingOf"/> measures from it: the constant is where the
        /// tower's axis is, <see cref="RadialStoreyResult.Centre"/> is where this floor's
        /// middle actually got built, and only the second one is still right if a storey is
        /// ever moved off the axis. It is also the exact cell <see cref="MarkPlaces"/> hangs
        /// B8's 도착점 on, so the creature and the finish agree about where a middle is.
        /// </para>
        /// </summary>
        private static void SeedCreature(MapSketch sketch, RadialStoreyResult floor, int level) =>
            sketch.MonsterStart(floor.Centre.X, floor.Centre.Z, level);

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
        /// and put two 도달 지점 on the same band.
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
        /// Marks the two things a race floor has: §02's finish, and the 도달 지점 —
        /// the places this map ASSERTS a runner can reach.
        /// <para>
        /// <b>What used to be here and why it went.</b> Every storey used to carry a 관측
        /// 지점 (§12 관측자, so somebody could watch the creature from out of its reach), a
        /// 배전반 (§04 정비공's 전기 패널, which made a zone lightable), a 은신처 (§12's 은폐
        /// 지점, for §07 새벽 when the creature has learned the way out), and three 후보 지점
        /// (§03's clue chain, three per zone with one picked per match). DESCENT-PIVOT §7
        /// step 7 deleted the roles, the clue chain and the light economy; none of those four
        /// has had a mechanic behind it since. Searched across the runtime, not assumed: no
        /// component, director or system reads <c>MapNodeKind.ObservationPost</c>,
        /// <c>ElectricalPanel</c> or <c>Concealment</c> — the only readers left were
        /// <c>MapValidator</c> rules written for the co-operative game. A mark whose only
        /// reader is the rule that counts it is furniture.
        /// </para>
        /// <para>
        /// The 배전반 was also the map's last PROP: <c>MapSketch.BuildProps</c> hangs a
        /// <c>WallPanelElectrical</c> on every <c>ElectricalPanel</c> mark and on nothing
        /// else, so deleting the mark takes eight panels off the walls. Nothing replaces
        /// them. The runner's light simply works; there is no panel to find and no zone to
        /// switch on. Darkness stays — it is the floor's, not a task's.
        /// </para>
        /// <para>
        /// <b>도달 지점 — and this is a change of job, not a change of name.</b> The
        /// audits collect a storey's markers and ask whether each can walk to the others
        /// (<c>NavMeshConnectivity</c>) and whether a runner-shaped body can get to them
        /// (<c>PlayerTraversal</c>). They see PlayerSpawn, MonsterSpawn and the 도달 지점;
        /// B8's 도착점 and every 투하구 are named in Korean and are invisible to them. So a
        /// floor's reachability is proved by its 도달 지점 and by nothing else, and a marker
        /// kind that means exactly that is what the map should be placing.
        /// </para>
        /// <para>
        /// It replaces two things that were both already doing this job under other names:
        /// <list type="number">
        /// <item><b>The 전리품 at every 막힌 길.</b> 152 of the old 220 markers. §08 is gone
        /// and it would have been one line to stop placing them; measured on seed 20260802,
        /// that line turned a 10-island FAIL into an 8-island PASS with two eight-cell
        /// pockets of B2 and B5 still walled out of their own floors, because those four
        /// markers were the only ones anywhere near them. §12's 막힌 길 are 20~25% of a
        /// floor by design and a marker on each is the only probe this map has on the fifth
        /// of itself that leads nowhere. The ECONOMY is deleted — the credit value, the
        /// pickup, the van, the shop. The probe is kept and told what it is for. A
        /// 도달 지점 has no value, nothing spawns on it and nothing can be carried off it;
        /// a 전리품 that nobody could pick up would have been a lie, and this is not one.</item>
        /// <item><b>The three 후보 지점 per storey.</b> These stopped being §03 sites a
        /// pivot ago — the comment they replace already said the job they are CHOSEN for is
        /// being a floor's evidence, and everything about the choice below (one per ring,
        /// never the 문's own mouth) is a reachability argument with no clue in it. They were
        /// §03-shaped only in the enum. A race floor wants nothing from three named
        /// junctions AS junctions: no role visits one, nothing is hidden at one, and if all
        /// three vanished the audit would still catch a sealed band, because the 막힌 길
        /// probes inside it would island out with it. So the case for keeping them is only
        /// the case for evidence, and it is a real one — on B2…B8, where there are no player
        /// spawns, these three are the ONLY markers standing on a band rail rather than one
        /// step off it in an alcove. Measured on B1, seed 20260802: the 막힌 길 probes sit at
        /// Chebyshev radius 4, 5, 8 and 11 and these sit at 9, 6 and 3, so the floor's
        /// evidence spans seven radii instead of four, and a wall that seals a rail without
        /// sealing the alcoves hanging off it has three markers standing on it.</item>
        /// </list>
        /// One kind, one name, one reason: 176 도달 지점 where there were 152 전리품 and 24
        /// 후보 지점, at exactly the same cells. The marker count, the pair count and every
        /// island number are unchanged by construction — see the report on this change.
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

                // One 도달 지점 per band, on the gate mouths — where a gate branches off its
                // band, which is the only shape on a ring floor with three ways out. The gate
                // cells themselves cannot carry a mark: they are deliberately mid-passage so a
                // door can hang on them, and MapSketch refuses a mark on a passage rather than
                // letting the count come up short without anything failing.
                //
                // THE COUNT IS THE BAND COUNT, and that is the §12 dependency going. It used
                // to be GameConstants.CandidateSitesPerZone — "단서·목표물 후보가 구역당 3개",
                // §03's number for how far a clue chain narrows the search. §03 is deleted, so
                // borrowing its 3 would be keeping the co-operative game's arithmetic and
                // calling it a coincidence that RadialStorey also builds three bands. What the
                // probes are one-of is a BAND: a band with no marker on it is a band the audit
                // cannot see into, and RadialStorey.Build is the only thing that knows how many
                // there are. Both numbers are 3 today and they mean different things; if a
                // storey ever grows a fourth ring this one follows it and the other would not.
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
                // is only about 1.2 m wide. A 도달 지점 one cell from the leaf is a probe
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
                // pairs, and the two extra islands are four 막힌 길 probes sealed into pockets
                // on B2 and B5 — the two floors whose door cell is (12,7). Hidden and re-baked
                // it is 8 islands / 0 partial (RadialStorey records the measurement). Both of
                // those numbers were taken with the band probes nowhere near a door.
                var rings = new System.Collections.Generic.List<int>();
                var probes = new System.Collections.Generic.List<MapCell>();

                while (probes.Count < floor.Bands.Count)
                {
                    // Outermost ring first, so 도달1 is the one a runner meets first.
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
                    probes.Add(floor.GateMouths[pick]);
                }

                // A storey that ran out of usable mouths before it ran out of bands still
                // carries one probe per band, taken from whatever mouths are left. A band with
                // no probe on it is a band the audit cannot see into: seal it off and the
                // markers inside it are its 막힌 길 probes alone, which are one step OFF the
                // rail and prove the alcove rather than the way past it.
                //
                // The 문's own mouth is allowed here and only here, as the last cell on the
                // list. Reaching it means the floor has run out of junctions, which is a
                // layout to go and look at rather than a map to refuse — and it will not go
                // unnoticed, because a mark on that cell is exactly what the audit reports as
                // a one-storey snap. Seed 20260802 never reaches this loop: every storey
                // offers mouths on all three rings.
                for (var i = 0;
                     i < floor.GateMouths.Count && probes.Count < floor.Bands.Count;
                     i++)
                {
                    if (!probes.Contains(floor.GateMouths[i]))
                    {
                        probes.Add(floor.GateMouths[i]);
                    }
                }

                for (var i = 0; i < probes.Count; i++)
                {
                    sketch.Mark(probes[i], MapNodeKind.ReachProbe, label + "_도달" + (i + 1));
                }

                // Nothing else. What stood here was a 관측 지점 and a 배전반 on
                // Alcoves[0] and Alcoves[Count/2], and a 은신처 on Alcoves[^1] — §12's
                // 관측자 vantage, §04's 전기 패널 and §07 새벽's hiding place. All three are
                // gone with the game that read them, and none of them was a probe: the cells
                // are 막힌 길, so they already carry a 도달 지점 from MapSketch's own leaf
                // sweep and this loop never added evidence, only furniture.
                //
                // RadialStorey still sorts its alcoves so the last one is nearest the middle
                // (see PunchAlcoves) — that ordering was a contract with the 은신처 mark and
                // it is now unread. Left alone deliberately: it is stable, it costs a sort,
                // and the next thing that wants "the alcove nearest the middle" will find it
                // already true instead of finding a list in gate order.
            }
        }
    }
}
