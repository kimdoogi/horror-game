using System;
using System.Collections.Generic;
using System.Linq;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §12 — 맵 설계 규칙.
    /// <para>
    /// §12 opens with "맵은 아트가 아니라 시스템이다", and this file is where that
    /// sentence has to become literally true: a map is good or bad by measurement, and
    /// the measurement is a number a test can hold. So every test here asserts the
    /// design's reasoning rather than the implementation — that a tree-shaped map is
    /// the 사형선고 §12 calls it, that a single corner really does need 14.4 m, that
    /// each of the eleven 검증 체크리스트 items fails <em>on its own</em> when a map
    /// breaks only that one thing, and that the 주자 테스트 lands where §12 says a
    /// playable map lands.
    /// </para>
    /// <para>
    /// Two fixtures carry most of it. <c>BuildSketchMap</c> is §12's 첫 맵 스케치 —
    /// B 타일 hall, A 나무, C 자갈, D 콘크리트 with the exit — laid out as a 2×2 block,
    /// and every checklist test breaks exactly one property of it.
    /// <c>BuildLongHouse</c> is the same construction strung out along §12's full 100 m
    /// with one 미로 구역 and three 개방 공간 halls; it is the map that answers §12's
    /// 실전 검증, because the compact one does not (see
    /// <see cref="SketchMap_PassesTheChecklistAndStillGradesTooEasy"/>).
    /// </para>
    /// </summary>
    [TestFixture]
    public class MapTests
    {
        // Layout slack, not balance values. §12's numbers are all distances, so the
        // fixture derives its geometry from GameConstants wherever the number means
        // something (ring legs are SCorridorLegLength plus a margin; a zone box is
        // sized from the 30~40 m diagonal band). These two are the leftovers: how much
        // longer than the legal minimum a leg is drawn, and how far a side passage
        // hangs off a corner. Both are chosen only so that no bend in the fixture
        // lands near MapSightBreakingBendDegrees by accident.
        private const float LegMargin = 2f;

        /// <summary>
        /// How far a side passage hangs off a ring corner, along each axis.
        /// <para>
        /// Half of <see cref="GameConstants.SightBreakPointSpanMax"/> per axis, so a
        /// spur's node stands <c>√2 × 2.2 = 3.1 m</c> from the corner it hangs off —
        /// inside the width §12 allows one 시야 차단 지점, and therefore part of that
        /// corner rather than a second one 3 m away. The fixture is meant to fail §12's
        /// rules only when a test asks it to, and before this was derived it failed
        /// sight-break-spacing everywhere purely because its spurs were 5.7 m long.
        /// </para>
        /// </summary>
        private const float SpurOffset = GameConstants.SightBreakPointSpanMax * 0.5f;

        // A probe offset: "just inside" and "just short of" a §12 threshold, small
        // enough that no other rule can be responsible for the result.
        private const float JustInside = 0.5f;

        private const int Seed = 20260730;

        /// <summary>§08 credits waiting at a 막힌 길 — §12 requires some, not an amount.</summary>
        private static int DeadEndReward => GameConstants.LootValueTrinket;

        /// <summary>A zone box whose XZ diagonal sits in the middle of §12's 30~40 m band.</summary>
        private static float ZoneSide =>
            (GameConstants.ZoneDiagonalMin + GameConstants.ZoneDiagonalMax) * 0.5f / MathF.Sqrt(2f);

        /// <summary>
        /// A ring leg: long enough to serve as one half of an S자 통로, and long enough
        /// that the four corners of a zone stand at §12's own 시야 차단 지점 간격 rather
        /// than chaining into one continuous piece of cover.
        /// </summary>
        private static float RingSide => GameConstants.LineOfSightBreakSpacingMin + LegMargin;

        /// <summary>
        /// How far a Runner has to have run before a single corner starts working, in
        /// metres. §12 does this sum in prose — the corner needs
        /// <see cref="GameConstants.SingleCornerMinDistance"/> of gap, the Runner starts
        /// with <see cref="GameConstants.RunnerTestAggroStartDistance"/> and opens
        /// 0.8 m/s while sprinting — so this is the distance at which its 판정 column
        /// flips from ❌ to ✅.
        /// </summary>
        private static float DistanceAtWhichACornerStartsWorking =>
            GameConstants.RunnerSprintSpeed
            * ((GameConstants.SingleCornerMinDistance - GameConstants.RunnerTestAggroStartDistance)
               / (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed));

        // ====================================================================
        // §12 첫 맵 스케치 — the map the section itself draws.
        // ====================================================================

        /// <summary>
        /// §12's own sketch must satisfy §12. If the document's example map cannot pass
        /// the document's checklist, either the checklist or the example is wrong, and
        /// every other test in this file is measuring against a bar nothing can clear.
        /// </summary>
        [Test]
        public void SketchMap_PassesEveryRuleInSection12()
        {
            var report = MapValidator.Validate(BuildSketchMap(Flaw.None));

            Assert.That(OtherFailures(report, MapValidator.RuleSightBreakSpacing), Is.Empty,
                "§12's own 첫 맵 스케치 fails its own rules:\n" + report.Describe());
            Assert.That(report.ChecklistPassed, Is.True);
            Assert.That(report.Results.Count(r => r.IsChecklistItem), Is.EqualTo(8),
                "§12's 검증 체크리스트 has eight items left. A validator that checks seven of them "
                + "reports PASS on a map that breaks the eighth. It was eleven until the "
                + "상점/전리품/단서 제거 round deleted 관측 지점, 단서·목표물 후보 지점 and 은폐 지점 — "
                + "three rules whose subjects (§04's 관측자, §03's clue chain, §07 새벽's ambush) no "
                + "longer exist.");
        }

        /// <summary>
        /// §14 puts the prototype at "B + A + S자 통로", and §12's sketch says what those
        /// are: B is the 타일 hall you take aggro in, A is the 나무 room you run to, and
        /// the passage between them is the thing that makes the run survivable.
        /// <para>
        /// The load-bearing assertion is the sight line: standing in the hall you must
        /// not be able to see into A, because §06 releases aggro on 3 s of broken sight
        /// and nothing else. A doorway between two rooms that leaves them in line is
        /// decoration; this one turns twice.
        /// </para>
        /// </summary>
        [Test]
        public void SketchMap_PrototypeIsTheHallTheRoomAndTheCorridorBetweenThem()
        {
            var map = BuildSketchMap(Flaw.None);
            var hall = map.Zones[map.Nodes[NodeNamed(map, "B 타일 도달1")].ZoneId];
            var room = map.Zones[map.Nodes[NodeNamed(map, "A 나무 도달1")].ZoneId];

            Assert.That(hall.Floor, Is.EqualTo(FloorMaterial.Tile), "§12: B is 타일.");
            Assert.That(room.Floor, Is.EqualTo(FloorMaterial.Wood), "§12: A is 나무.");
            Assert.That(hall.Floor, Is.Not.EqualTo(room.Floor),
                "§12 청음사: two adjacent zones sharing a surface leave the Listener unable to say "
                + "which one a footstep came from, which is the whole of the role.");

            Assert.That(map.NodesOfKindInZone(hall.Id, MapNodeKind.OpenSpace), Is.Not.Empty,
                "§12 draws B as the 개방 공간 — 시야 20m — because that is where aggro is taken from far "
                + "enough out for the release to be arithmetically possible.");
            Assert.That(map.EdgesBetweenZones(hall.Id, room.Id).Length,
                Is.InRange(GameConstants.ZoneEntryPointsMin, GameConstants.ZoneEntryPointsMax));

            var fromHall = NodeNamed(map, "B 타일 도달1");
            var intoRoom = NodeNamed(map, "A 나무 모퉁이");
            Assert.That(map.HasStraightSightLine(fromHall, intoRoom), Is.False,
                "The corridor from the hall into A leaves the two in line of sight, so a Runner who "
                + "took aggro in the hall is still visible after entering A. §06 releases on "
                + GameConstants.AggroReleaseLineOfSightBreak + " s of broken sight, so a passage that "
                + "does not break it buys nothing at all.");
            Assert.That(map.PathLength(fromHall, intoRoom), Is.LessThan(GameConstants.SprintMaxTravelDistance),
                "§12 frames every release inside one sprint's travel; a hall the Runner cannot leave "
                + "within that distance is not adjacent to anything.");
        }

        /// <summary>
        /// The compact sketch passes all eleven checklist items and still grades
        /// 너무 쉽다. That is not a bug in either — it is §12's own structure: the
        /// checklist is a list of things that must be present, and 실전 검증 is the
        /// separate question of whether they are spaced far enough apart to cost
        /// anything. Recorded as a test so nobody reads "the checklist passes" as
        /// "the map is finished".
        /// </summary>
        [Test]
        public void SketchMap_PassesTheChecklistAndStillGradesTooEasy()
        {
            var map = BuildSketchMap(Flaw.None);
            Assert.That(MapValidator.Validate(map).ChecklistPassed, Is.True);

            var runner = RunnerTest.RunAt(map, EveryNode(map));

            Assert.That(runner.Verdict, Is.EqualTo(RunnerTestVerdict.TooEasy),
                "If this map now grades 적정, the compact 2×2 layout stopped being the "
                + "cover-saturated case this test exists to record — recheck what moved:\n"
                + runner.Describe());
            Assert.That(runner.SuccessRate, Is.GreaterThan(GameConstants.RunnerTestPassRateMax),
                "Every checklist item present and aggro still breakable from everywhere: §12's "
                + "checklist is necessary and not sufficient, and 실전 검증 is what closes the gap.");
        }

        // ====================================================================
        // §12 검증 체크리스트 — eleven items, each broken on its own.
        // ====================================================================

        /// <summary>
        /// "20m 넘는 직선 통로가 없다." A shortcut across a room joins two passages into
        /// one unbroken sight line.
        /// </summary>
        [Test]
        public void Checklist_StraightCorridorOverTwentyMetres_FailsOnItsOwn()
        {
            var map = BuildSketchMap(Flaw.StraightCorridorOverTwentyMetres);
            var report = MapValidator.Validate(map);
            var result = AssertOnlyFailure(report, MapValidator.RuleStraightCorridor,
                "A sight line over §12's limit must fail this rule and only this rule.");

            Assert.That(map.LongestStraightRun(out _), Is.GreaterThan(GameConstants.MaxStraightCorridor),
                "The fixture was supposed to draw a run longer than §12 allows.");
            Assert.That(result.Detail, Does.Contain("넘으면 주자가 죽는다"),
                "§12 states the consequence, and a designer needs the consequence rather than the "
                + "measurement: the Runner gains "
                + (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed)
                + " m/s, so it cannot reach the far end of a longer straight before the monster closes.");
        }

        /// <summary>
        /// "개방 공간이 최소 1개 있고, 미로 공간과 인접해 있다." Both kinds present but
        /// nowhere touching — §12's "[개방 공간] ──진입── [미로 공간]" broken at the arrow.
        /// </summary>
        [Test]
        public void Checklist_OpenSpaceNotAdjacentToMaze_FailsOnItsOwn()
        {
            var map = BuildSketchMap(Flaw.OpenSpaceNotAdjacentToMaze);
            var report = MapValidator.Validate(map);

            Assert.That(map.NodesOfKind(MapNodeKind.OpenSpace), Is.Not.Empty,
                "This must be the adjacency failing, not the absence of an 개방 공간.");
            Assert.That(map.NodesOfKind(MapNodeKind.MazeSpace), Is.Not.Empty,
                "This must be the adjacency failing, not the absence of a 미로 공간.");

            var result = AssertOnlyFailure(report, MapValidator.RuleOpenAdjacentToMaze,
                "Aggro is taken in the open and broken in the maze. If the two never touch, the walk "
                + "between them is itself an unbroken run and the pair buys nothing.");
            Assert.That(result.Detail, Does.Contain("[개방 공간] ──진입── [미로 공간]"));
        }

        /// <summary>
        /// "S자 통로(10m×2)가 구역당 최소 1개 있다." Every leg in one zone is shortened
        /// below <see cref="GameConstants.SCorridorLegLength"/>, so the shape survives
        /// and only the length that makes it work is gone.
        /// </summary>
        [Test]
        public void Checklist_NoSCorridorInOneZone_FailsOnItsOwn()
        {
            var map = BuildSketchMap(Flaw.NoSCorridorInOneZone);
            var report = MapValidator.Validate(map);
            var result = AssertOnlyFailure(report, MapValidator.RuleSCorridorPerZone,
                "A zone whose legs are all under 10 m has corners but no S자 통로.");

            Assert.That(map.FindSCorridor(0), Is.Null);
            for (var z = 1; z < map.Zones.Length; z++)
            {
                Assert.That(map.FindSCorridor(z), Is.Not.Null,
                    "Only zone A was meant to lose its S자 통로.");
            }

            Assert.That(result.Detail, Does.Contain(map.Zones[0].Name),
                "§12's checklist is only worth automating if the failure says which zone to go and fix.");
        }

        /// <summary>
        /// "순환로가 맵 전체에 3개 이상 있다" plus §12's 수치 규칙 "구역당 1+". One zone is
        /// opened into a tree while the map-wide count stays far above three — the case
        /// the per-zone clause exists for.
        /// </summary>
        [Test]
        public void Checklist_ZoneWithNoLoop_FailsOnItsOwn()
        {
            var map = BuildSketchMap(Flaw.ZoneWithNoLoop);
            var report = MapValidator.Validate(map);

            Assert.That(map.IndependentLoopCountInZone(0), Is.Zero,
                "Zone A was meant to become a tree.");
            Assert.That(map.IndependentLoopCount, Is.GreaterThanOrEqualTo(GameConstants.LoopsTotalMin),
                "The map-wide count must still pass, or this test would not be isolating the per-zone "
                + "clause. §12 asks for both because a map can reach three loops with all of them in "
                + "one hall.");

            var result = AssertOnlyFailure(report, MapValidator.RuleLoops,
                "§12: \"트리 구조는 사형선고\" — a Runner cornered inside a loopless zone has nowhere "
                + "to send the monster the wrong way round.");
            Assert.That(result.Detail, Does.Contain("구역당"));
        }
        /// <summary>
        /// The other half of the same item: the ratio itself. §12 bands it at 20~25%
        /// from both sides — "적으면 맵 지식 무의미, 많으면 운에 죽음".
        /// </summary>
        [Test]
        public void Checklist_TooManyDeadEnds_FailsOnItsOwn()
        {
            var map = BuildSketchMap(Flaw.TooManyDeadEnds);
            var report = MapValidator.Validate(map);

            var deadEnds = EveryNode(map).Count(map.IsDeadEnd);
            Assert.That(deadEnds / (float)map.Nodes.Length,
                Is.GreaterThan(GameConstants.DeadEndRatioMax));
            Assert.That(EveryNode(map).Where(map.IsDeadEnd)
                    .All(n => map.Nodes[n].DeadEndRewardValue > 0), Is.True,
                "Every added 막힌 길 carries loot, so only the ratio clause is under test.");

            var result = AssertOnlyFailure(report, MapValidator.RuleDeadEnds,
                "At this density a Runner breaking aggro is picking a passage blind.");
            Assert.That(result.Detail, Does.Contain("많으면 운에 죽음"));
        }

        /// <summary>"구역별 바닥 재질이 다르고 경계가 명확하다" — two zones on 나무.</summary>
        [Test]
        public void Checklist_TwoZonesShareAFloor_FailsOnItsOwn()
        {
            var map = BuildSketchMap(Flaw.TwoZonesShareAFloor);
            var report = MapValidator.Validate(map);

            var duplicated = map.Zones.GroupBy(z => z.Floor).Single(g => g.Count() > 1).ToArray();
            Assert.That(duplicated[0].FootstepClarity,
                Is.EqualTo(duplicated[1].FootstepClarity).Within(0f),
                "The two zones now sound identical underfoot — " + duplicated[0].Name + " and "
                + duplicated[1].Name + " both report clarity " + duplicated[0].FootstepClarity
                + ". That is the failure in the form §04 experiences it: the Listener still gets a "
                + "fix, and the fix no longer names a room.");

            var result = AssertOnlyFailure(report, MapValidator.RuleFloorMaterials,
                "Two zones with one surface means a footstep in either sounds the same, and §04's "
                + "위치 판별 degenerates from 'which room' to 'somewhere in the building'.");
            Assert.That(result.Detail, Does.Contain("아트 결정이 아니라"),
                "§12 is explicit that the floor table is a systems decision. The failure should say so, "
                + "because the fix will otherwise be argued about as art.");
        }

        /// <summary>
        /// The ugly end of the same rule: a zone nobody assigned a surface to. §12
        /// never says "and it must not be blank", because it never occurred to it —
        /// so the validator has to.
        /// </summary>
        [Test]
        public void Checklist_ZoneWithNoFloorMaterial_FailsOnItsOwn()
        {
            var map = BuildSketchMap(Flaw.ZoneWithNoFloorMaterial);
            var report = MapValidator.Validate(map);

            var blank = map.Zones.Single(z => z.Floor == FloorMaterial.None);
            Assert.That(blank.FootstepClarity, Is.EqualTo(GameConstants.ListenerClarityUnknown).Within(1e-4f));
            Assert.That(blank.FootstepClarity, Is.LessThan(GameConstants.ListenerClarityConcrete),
                "An unassigned floor is quieter than 콘크리트, §12's dullest real surface. A zone the "
                + "Listener cannot hear at all is strictly better for the monster than any zone the "
                + "design actually describes.");

            AssertOnlyFailure(report, MapValidator.RuleFloorMaterials,
                "A blank surface has to fail loudly. Left alone it silently becomes the best zone on "
                + "the map to hunt in.");
        }
        /// <summary>
        /// "잠글 수 있는 문이 구역당 1~2개, 병목에 있다." The count stays legal and the
        /// door moves to a passage with a short way round — §12's harder clause, since
        /// a door that is not at a 병목 looks correct on a plan and does nothing in play.
        /// </summary>
        [Test]
        public void Checklist_DoorAwayFromBottleneck_FailsOnItsOwn()
        {
            var map = BuildSketchMap(Flaw.DoorAwayFromBottleneck);
            var report = MapValidator.Validate(map);

            var misplaced = EveryEdge(map).Single(e => map.Edges[e].HasLockableDoor && !map.IsBottleneck(e));
            var gain = map.DetourWithout(misplaced) - map.Edges[misplaced].Length;
            Assert.That(gain, Is.LessThan(GameConstants.SingleCornerMinDistance),
                "The way round has to be short enough that locking the door does not even buy the "
                + GameConstants.AggroReleaseLineOfSightBreak + " s of cover a release needs.");
            Assert.That(gain / GameConstants.MonsterBaseSpeed,
                Is.LessThan(GameConstants.AggroReleaseLineOfSightBreak),
                "Stated as §12 states it: the detour costs the monster less time than the release "
                + "requires, so the Engineer spent " + GameConstants.EngineerDoorLockSeconds
                + " s and a material on nothing.");

            for (var z = 0; z < map.Zones.Length; z++)
            {
                Assert.That(map.LockableDoorsInZone(z).Length,
                    Is.InRange(GameConstants.LockableDoorsPerZoneMin, GameConstants.LockableDoorsPerZoneMax),
                    "The per-zone budget must still be legal, or this test would be proving the cap "
                    + "rather than the 병목 clause.");
            }

            var result = AssertOnlyFailure(report, MapValidator.RuleLockableDoors, "See above.");
            Assert.That(result.Detail, Does.Contain("병목"));
        }

        /// <summary>
        /// The other clause of the same item: the cap. §12 stops at two doors a zone
        /// because "많으면 정비공이 만능이 된다" — an Engineer who can seal every approach
        /// stops making the choice §04 built the role around.
        /// </summary>
        [Test]
        public void Checklist_TooManyLockableDoors_FailsOnItsOwn()
        {
            var map = BuildSketchMap(Flaw.TooManyLockableDoors);
            var report = MapValidator.Validate(map);

            Assert.That(map.LockableDoorsInZone(0).Length,
                Is.GreaterThan(GameConstants.LockableDoorsPerZoneMax));
            Assert.That(EveryEdge(map).Where(e => map.Edges[e].HasLockableDoor).All(map.IsBottleneck),
                Is.True,
                "Every one of them is at a 병목, so only the cap is under test.");

            Assert.That(map.LockableDoorsInZone(0).Count(map.CutsALoop),
                Is.GreaterThan(GameConstants.LockableDoorsPerZoneMax),
                "§12 budgets the Engineer one or two loop-cutting doors a zone. Past that the role "
                + "stops choosing which 순환로 to break and simply breaks all of them — \"많으면 "
                + "정비공이 만능이 된다\" — and the zone becomes a place the Runner cannot use either.");

            AssertOnlyFailure(report, MapValidator.RuleLockableDoors, "See above.");
        }

        /// <summary>
        /// The min side of §12's 진입점 rule, which the sketch map cannot break on its
        /// own: taking a doorway out of it necessarily leaves an unrewarded 막힌 길 or a
        /// zone with no 전기 패널, so the rules are coupled. Checked on a two-zone map
        /// instead, where a single passage is the bridge §12 is warning about.
        /// </summary>
        [Test]
        public void ZoneEntryPoints_ASinglePassageBetweenZonesIsABridge()
        {
            var map = TwoZonesJoinedOnce();
            var report = MapValidator.Validate(map);
            var result = report[MapValidator.RuleZoneEntryPoints];

            Assert.That(map.EdgesBetweenZones(0, 1).Length, Is.EqualTo(1));
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Detail, Does.Contain("bridge"));
            Assert.That(map.IsBottleneck(map.EdgesBetweenZones(0, 1)[0]), Is.True,
                "One passage between two zones is a 병목 by construction — which is the problem. "
                + "§12 asks for " + GameConstants.ZoneEntryPointsMin + "~"
                + GameConstants.ZoneEntryPointsMax + " so that the monster has to guess and a single "
                + "locked door cannot seal a whole zone off.");
        }
        /// <summary>
        /// "구역 간 진입점이 2~3개로 제한돼 있다." Two extra doorways between the same
        /// pair of zones, which §07 cares about: it measures patrol scope in whole
        /// zones, and that only means something if leaving one is a decision.
        /// </summary>
        [Test]
        public void Checklist_TooManyZoneEntryPoints_FailsOnItsOwn()
        {
            var map = BuildSketchMap(Flaw.TooManyZoneEntryPoints);
            var report = MapValidator.Validate(map);

            var crowded = map.EdgesBetweenZones(0, 1);
            Assert.That(crowded.Length, Is.GreaterThan(GameConstants.ZoneEntryPointsMax));
            Assert.That(GameConstants.ThreatPatrolZonesEarlyEvening, Is.LessThan(map.Zones.Length),
                "§07 starts the monster patrolling " + GameConstants.ThreatPatrolZonesEarlyEvening
                + " zone of " + map.Zones.Length + ". A zone boundary that is more doorway than wall "
                + "makes that scope meaningless — the monster is never anywhere in particular.");

            AssertOnlyFailure(report, MapValidator.RuleZoneEntryPoints, "See above.");
        }
        // ====================================================================
        // §12 수치 규칙 — 시야 차단 지점 간격 15~25m.
        // ====================================================================

        /// <summary>
        /// "시야 차단 지점 간격 15~25m", broken on its own by widening one spur.
        /// <para>
        /// Nothing else about the map moves: the same nodes, the same degrees, the same
        /// passages. The bend that hangs off a ring corner simply stands further from
        /// it, and at
        /// <see cref="GameConstants.SightBreakPointSpanMax"/> the two stop being one
        /// 시야 차단 지점 and become cover deep enough to finish §06's
        /// <see cref="GameConstants.AggroReleaseLineOfSightBreak"/> from any distance at
        /// all — §12's first conclusion, 「주자는 멀리서 어그로를 걸어야 한다」, inverted.
        /// </para>
        /// </summary>
        [Test]
        public void SightBreakSpacing_CoverDeeperThanTheAggroHeadStart_FailsOnItsOwn()
        {
            var legal = BuildLongHouse(SpurOffset);
            Assert.That(MapValidator.Validate(legal).Failures, Is.Empty,
                "The unmodified long house has to be legal, or this test is measuring two changes.");

            // √2 × this is the span of the 지점 the spur and its corner form together.
            var wide = (GameConstants.SightBreakPointSpanMax + JustInside) / MathF.Sqrt(2f);
            var map = BuildLongHouse(wide);
            var report = MapValidator.Validate(map);

            Assert.That(map.Nodes.Length, Is.EqualTo(legal.Nodes.Length),
                "Widening a spur must not add a place, or another rule could be the one that broke.");
            Assert.That(map.Edges.Length, Is.EqualTo(legal.Edges.Length));

            var result = report[MapValidator.RuleSightBreakSpacing];
            Assert.That(result.Passed, Is.False,
                "Cover " + (GameConstants.SightBreakPointSpanMax + JustInside)
                + " m deep breaks aggro wherever it is picked up:\n" + report.Describe());
            Assert.That(result.IsChecklistItem, Is.False,
                "§12's 검증 체크리스트 has eleven items and this is not one of them — it comes from the "
                + "수치 규칙 table, like 구역 개수 and 맵 전체.");
            Assert.That(
                report.FailedRuleIds.Where(id => id != MapValidator.RuleSightBreakSpacing), Is.Empty,
                "Only the spacing was supposed to change. Full report:\n" + report.Describe());
            Assert.That(result.Detail, Does.Contain("주자는 멀리서 어그로를 걸어야 한다"),
                "§12 states the consequence, and the consequence is what a designer can act on.");
        }

        /// <summary>
        /// The other half of the same row: cover so far from the next cover that the
        /// stretch between them is an unbroken run. §12 caps the gap at 25 m for the
        /// same reason it caps a straight corridor at 20 — the Runner gains 0.8 m/s and
        /// cannot outrun the difference.
        /// </summary>
        [Test]
        public void SightBreakSpacing_CoverFurtherApartThanSection12Allows_Fails()
        {
            var lonely = MapValidator.Validate(
                Serpentine(4, GameConstants.LineOfSightBreakSpacingMax + LegMargin, false));
            var packed = MapValidator.Validate(
                Serpentine(4, GameConstants.LineOfSightBreakSpacingMax - LegMargin, false));

            Assert.That(packed[MapValidator.RuleSightBreakSpacing].Passed, Is.True,
                "Bends " + (GameConstants.LineOfSightBreakSpacingMax - LegMargin)
                + " m apart are inside §12's band:\n" + packed.Describe());
            Assert.That(lonely[MapValidator.RuleSightBreakSpacing].Passed, Is.False,
                "Bends " + (GameConstants.LineOfSightBreakSpacingMax + LegMargin)
                + " m apart leave a stretch with no cover in it at all:\n" + lonely.Describe());
            Assert.That(lonely[MapValidator.RuleSightBreakSpacing].Detail,
                Does.Contain("over §12's " + GameConstants.LineOfSightBreakSpacingMax + " m"),
                "The failure has to quote the bound it broke, not just say the map is wrong.");
        }

        /// <summary>
        /// The two fixtures, side by side, so that the compact sketch failing this rule
        /// is a recorded fact rather than something the next reader trips over.
        /// <para>
        /// §12's 첫 맵 스케치 is four zones edge to edge. §12 also caps a zone at a 40 m
        /// diagonal and a straight corridor at 20 m, so the bends either side of a zone
        /// boundary can only ever be a few metres apart — wider than one 시야 차단 지점
        /// may be, far short of the gap to the next. The compact sketch is cover-
        /// saturated by construction, which is exactly why
        /// <see cref="SketchMap_PassesTheChecklistAndStillGradesTooEasy"/> grades it
        /// 너무 쉽다 while every checklist item passes. This rule is the arithmetic
        /// behind that grade.
        /// </para>
        /// </summary>
        [Test]
        public void SightBreakSpacing_TheCompactSketchFailsItAndTheLongHouseDoesNot()
        {
            var compact = MapValidator.Validate(BuildSketchMap(Flaw.None));
            var strungOut = MapValidator.Validate(BuildLongHouse());

            Assert.That(compact.ChecklistPassed, Is.True,
                "All eleven 검증 체크리스트 items still pass on the compact sketch.");
            Assert.That(compact.FailedRuleIds, Is.EqualTo(new[] { MapValidator.RuleSightBreakSpacing }),
                "and this is the only rule it breaks:\n" + compact.Describe());

            Assert.That(strungOut.Failures, Is.Empty,
                "The long house is the map §12's 실전 검증 is run against, so it has to satisfy every "
                + "rule §12 states:\n" + strungOut.Describe());
        }

        /// <summary>
        /// The eight items are eight. This is the guard against a checklist test above
        /// silently covering two rules, or a ninth rule appearing without a test to
        /// break it.
        /// <para>
        /// It was eleven. Three went in the 상점/전리품/단서 제거 round because they were
        /// requirements OF systems that no longer exist: 관측 지점 (§04's 관측자),
        /// 단서·목표물 후보 지점 with its 전기 패널 clause (§03 and the light economy), and
        /// 은폐 지점 near the exit (§07 새벽's ambush, and already waived as obsolete in
        /// MapSceneGenerator.KnownFailingRules). §12's 막힌 길 rule kept its ratio band
        /// and lost its "각각 보상이 있다" half with §08.
        /// </para>
        /// </summary>
        [Test]
        public void Checklist_HasExactlyTheEightItemsSection12StillLists()
        {
            var covered = new[]
            {
                MapValidator.RuleStraightCorridor,
                MapValidator.RuleOpenAdjacentToMaze,
                MapValidator.RuleSCorridorPerZone,
                MapValidator.RuleLoops,
                MapValidator.RuleDeadEnds,
                MapValidator.RuleFloorMaterials,
                MapValidator.RuleLockableDoors,
                MapValidator.RuleZoneEntryPoints,
            };

            var reported = MapValidator.Validate(BuildSketchMap(Flaw.None)).Results
                .Where(r => r.IsChecklistItem)
                .Select(r => r.RuleId)
                .ToArray();

            Assert.That(reported, Is.EquivalentTo(covered),
                "§12's 검증 체크리스트 now has eight items and each one has a test above that breaks "
                + "only it. If this fails, a rule was added or renamed and its isolation test is "
                + "missing.");
        }

        // ====================================================================
        // 순환로 — "트리 구조는 사형선고." The loop detector has to be a graph
        // algorithm, because a false negative here ships a map that kills people.
        // ====================================================================

        /// <summary>
        /// A tree has no 순환로, and the detector must say so. §12 calls this shape a
        /// 사형선고 for a reason: with no ring anywhere, the monster never has a wrong
        /// guess to make, so a chase collapses into the speed comparison of §06 — which
        /// every role but 주자 loses by <c>4.8 − 4.5</c> m/s.
        /// </summary>
        [Test]
        public void LoopDetector_OnATree_FindsNothing()
        {
            var tree = Tree();

            Assert.That(tree.IndependentLoopCount, Is.Zero);
            Assert.That(tree.FindLoop(), Is.Null,
                "A back-edge search that reports a ring in a tree would let a 사형선고 map through "
                + "validation, and the symptom in play is only ever \"the chase feels unfair\".");
            Assert.That(tree.ConnectedComponentCount, Is.EqualTo(1));
            Assert.That(tree.Edges.Length, Is.EqualTo(tree.Nodes.Length - 1),
                "E = V − 1 is what makes it a tree; if the fixture stopped being one this test proves "
                + "nothing.");
            Assert.That(GameConstants.MonsterBaseSpeed, Is.GreaterThan(GameConstants.RunSpeed),
                "The reason a tree is fatal rather than merely dull.");
        }

        /// <summary>A ring is found, and what comes back is a walk a monster could be sent the wrong way round.</summary>
        [Test]
        public void LoopDetector_OnACycle_ReturnsARealWalk()
        {
            var ring = Ring(6);
            var found = ring.FindLoop();

            Assert.That(found, Is.Not.Null, "A six-node ring is a 순환로.");
            Assert.That(found!.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(found[0], Is.EqualTo(found[found.Length - 1]),
                "A 순환로 has to come back to where it started, or it is a corridor.");

            for (var i = 0; i + 1 < found.Length; i++)
            {
                Assert.That(AreJoined(ring, found[i], found[i + 1]), Is.True,
                    "Step " + i + " of the reported ring is not a passage that exists. A loop count "
                    + "is only worth having if the loop can actually be walked.");
            }

            Assert.That(found.Take(found.Length - 1).Distinct().Count(), Is.EqualTo(found.Length - 1),
                "The ring must not double back on itself.");
        }

        /// <summary>
        /// The count is the rank of the cycle space, not the number of closed walks. A
        /// figure-of-eight has three closed walks in it and two independent loops, and
        /// §12's "3개 이상" only means something under the second reading — otherwise a
        /// single ring could be counted three ways and pass.
        /// </summary>
        [Test]
        public void LoopDetector_FigureOfEight_CountsTwoNotThree()
        {
            var eight = FigureOfEight();

            Assert.That(eight.IndependentLoopCount, Is.EqualTo(2));
            Assert.That(eight.Edges.Length - eight.Nodes.Length + eight.ConnectedComponentCount,
                Is.EqualTo(2), "E − V + components is the definition being relied on.");
        }

        /// <summary>Closing one more ring adds exactly one loop — the property that makes the count usable as a budget.</summary>
        [Test]
        public void LoopDetector_OneExtraPassage_AddsExactlyOneLoop()
        {
            var open = Ring(6);
            var chorded = Ring(6, true);

            Assert.That(chorded.IndependentLoopCount, Is.EqualTo(open.IndependentLoopCount + 1),
                "A designer adding a passage to reach §12's " + GameConstants.LoopsTotalMin
                + " needs the count to move by one per passage, or the budget is unusable.");
        }

        /// <summary>
        /// Two separate pieces, each a tree, must count as zero loops rather than as
        /// <c>E − V + 1</c> would suggest. A map in two pieces is where a naive formula
        /// reports a phantom ring, and it is exactly the state a half-built level is in.
        /// </summary>
        [Test]
        public void LoopDetector_OnADisconnectedForest_CountsNoLoops()
        {
            var forest = Forest();

            Assert.That(forest.ConnectedComponentCount, Is.EqualTo(2));
            Assert.That(forest.IsConnected, Is.False);
            Assert.That(forest.IndependentLoopCount, Is.Zero,
                "E − V + 1 would report −1 here. Counting components is what keeps the answer honest "
                + "on a map that is still being built.");
            Assert.That(forest.FindLoop(), Is.Null);
        }

        /// <summary>
        /// A ring that leaves a zone and comes back is a map-wide 순환로 and not that
        /// zone's. §12 asks for both counts because a Runner cornered inside a zone
        /// needs somewhere to go without leaving it.
        /// </summary>
        [Test]
        public void LoopDetector_PerZone_IgnoresRingsThatLeaveTheZone()
        {
            var map = TwoZoneRing();

            Assert.That(map.IndependentLoopCount, Is.EqualTo(1),
                "The ring exists map-wide.");
            Assert.That(map.IndependentLoopCountInZone(0), Is.Zero,
                "Half a ring is a corridor. Counting it as the zone's 순환로 would pass a map where "
                + "every escape leads out of the zone the Runner is trapped in.");
            Assert.That(map.IndependentLoopCountInZone(1), Is.Zero);
        }

        /// <summary>The sketch map satisfies both of §12's loop rules, and by a margin.</summary>
        [Test]
        public void LoopDetector_OnTheSketchMap_MeetsBothOfSection12sCounts()
        {
            var map = BuildSketchMap(Flaw.None);

            Assert.That(map.IndependentLoopCount, Is.GreaterThanOrEqualTo(GameConstants.LoopsTotalMin));
            for (var z = 0; z < map.Zones.Length; z++)
            {
                Assert.That(map.IndependentLoopCountInZone(z),
                    Is.GreaterThanOrEqualTo(GameConstants.LoopsPerZoneMin),
                    "Zone " + map.Zones[z].Name + " is a tree.");
            }

            Assert.That(map.FindLoop(), Is.Not.Null);

            Assert.That(GameConstants.ZoneCountMin * GameConstants.LoopsPerZoneMin,
                Is.GreaterThanOrEqualTo(GameConstants.LoopsTotalMin),
                "§12 states two loop counts, and only one of them can ever bind: the smallest legal "
                + "map is " + GameConstants.ZoneCountMin + " zones needing "
                + GameConstants.LoopsPerZoneMin + " loop each, which already clears the map-wide "
                + GameConstants.LoopsTotalMin + ". The 구역당 rule is the one doing the work — so a "
                + "retune that relaxes it has nothing underneath it, whatever the map-wide number says.");
        }

        // ====================================================================
        // §12 실전 검증 — 주자 테스트. The number that says whether a map is good.
        // ====================================================================

        /// <summary>
        /// §12's 적정 band, on a map built the way §12 describes: one 미로 구역 where
        /// aggro can be broken, three 개방 공간 halls strung out over the full 100 m
        /// where it cannot, and the two adjacent.
        /// <para>
        /// This is the single most valuable assertion in the file. Everything the
        /// validator checks is necessary and none of it is sufficient — what decides a
        /// map is whether the Runner can <em>reach</em> cover before the monster
        /// closes, and that is a simulation, not a count. The map is run from every
        /// place on it rather than from ten sampled ones, so the rate is the map's own
        /// number rather than a draw.
        /// </para>
        /// </summary>
        [Test]
        public void RunnerTest_OnAWellFormedMap_LandsInSection12sBand()
        {
            var map = BuildLongHouse();
            Assert.That(MapValidator.Validate(map).Failures, Is.Empty,
                "The map graded here has to be §12-legal, or the grade is about something else.");

            var report = RunnerTest.RunAt(map, EveryNode(map));

            Assert.That(report.SuccessRate,
                Is.InRange(GameConstants.RunnerTestPassRateMin, GameConstants.RunnerTestPassRateMax),
                "§12's 실전 검증 puts a playable map at 5~7/10:\n" + report.Describe());
            Assert.That(report.Verdict, Is.EqualTo(RunnerTestVerdict.Balanced));
            Assert.That(report.Passed, Is.True);
            Assert.That(report.Advice, Does.Contain("적정"));
        }

        /// <summary>
        /// §12's own procedure, literally: ten points, sampled. The exhaustive run above
        /// is the map's true rate; this is the test a designer actually performs, and it
        /// has to agree — not on one lucky draw, but on draw after draw.
        /// <para>
        /// Asserted over several seeds on purpose. A ten-point sample of a 63% map
        /// scatters, and a single seed landing at 5 or 7 would make this test a coin
        /// flip dressed as a measurement. What §12's band is really claiming is that a
        /// designer who runs the ten points gets 적정 almost every time, so that is what
        /// is checked.
        /// </para>
        /// </summary>
        [Test]
        public void RunnerTest_TenSampledPoints_AgreeWithTheWholeMap()
        {
            var map = BuildLongHouse();
            var samples = Enumerable.Range(1, 8)
                .Select(seed => RunnerTest.Run(map, new DeterministicRandom(seed)))
                .ToArray();

            Assert.That(samples.All(s => s.SampleCount == GameConstants.RunnerTestSampleCount), Is.True,
                "§12: \"10개 지점에서 시도한다\", and its bands are quoted against ten.");

            var balanced = samples.Count(s => s.Verdict == RunnerTestVerdict.Balanced);
            Assert.That(balanced, Is.GreaterThanOrEqualTo(samples.Length - 1),
                "Only " + balanced + " of " + samples.Length + " ten-point runs graded 적정. §12's "
                + "procedure has to give a designer the same answer most times they run it, or the "
                + "band is describing the sample rather than the map:\n"
                + string.Join("\n", samples.Select(s => s.Successes + "/" + s.SampleCount + " " + s.Verdict)));

            Assert.That(samples.Average(s => s.Successes), Is.InRange(5.0, 7.0),
                "§12 writes the band as 5~7 out of 10, and the average of repeated runs is where the "
                + "map's own rate shows through the scatter.");
        }

        /// <summary>
        /// A corner every few metres and the release is free. §12's prescription for
        /// this is not "add difficulty" but "시야 차단 지점을 줄인다", and the report has
        /// to carry that prescription rather than just a grade.
        /// </summary>
        [Test]
        public void RunnerTest_OnAMapDrowningInCover_IsTooEasy()
        {
            var map = Serpentine(24, GameConstants.SCorridorLegLength * 0.4f, false);
            var report = RunnerTest.Run(map, new DeterministicRandom(Seed));

            Assert.That(report.SuccessRate, Is.GreaterThan(GameConstants.RunnerTestPassRateMax));
            Assert.That(report.Successes, Is.EqualTo(report.SampleCount),
                "With a sight-breaking corner every few metres, every point on the map can break "
                + "aggro:\n" + report.Describe());
            Assert.That(report.Verdict, Is.EqualTo(RunnerTestVerdict.TooEasy));
            Assert.That(report.Advice, Does.Contain("시야 차단 지점을 줄인다"),
                "§12 gives the fix, not just the grade, and a report that withholds it makes the "
                + "designer guess.");
        }

        /// <summary>
        /// One long straight corridor: §12's "넘으면 주자가 죽는다" as a measurement.
        /// Nothing about it is escapable, and the same map fails the very first
        /// checklist item — the two agreeing is the point.
        /// </summary>
        [Test]
        public void RunnerTest_OnOneLongStraightCorridor_IsHopeless()
        {
            var map = StraightCorridor();
            var report = RunnerTest.Run(map, new DeterministicRandom(Seed));

            Assert.That(report.Successes, Is.Zero,
                "A straight corridor has no corner to round, so §06's release — "
                + GameConstants.AggroReleaseLineOfSightBreak
                + " s of broken sight — can never begin:\n" + report.Describe());
            Assert.That(report.Verdict, Is.EqualTo(RunnerTestVerdict.TooHard));
            Assert.That(report.Advice, Does.Contain("S자 통로를 추가한다"));
            Assert.That(report.Attempts.All(a => a.BreaksCrossed == 0), Is.True,
                "Not one sight-breaking corner exists to be rounded.");

            Assert.That(MapValidator.Validate(map)[MapValidator.RuleStraightCorridor].Passed, Is.False,
                "§12's first checklist item and its 실전 검증 must agree about this map. If the "
                + "checklist passed a map the runner test scores 0/10, the checklist would be "
                + "measuring the wrong thing.");
        }

        /// <summary>
        /// §12's headline calculation, run rather than quoted: "시야 차단 3초가 필요 →
        /// D ≥ 14.4m". A Runner that rounds a single corner while the monster is closer
        /// than that does not get away, and the same corner works once the gap is
        /// wider — which is why §12 concludes 주자는 멀리서 어그로를 걸어야 한다.
        /// </summary>
        [Test]
        public void RunnerTest_ASingleCorner_WorksOnlyFromFourteenPointFourMetresOut()
        {
            var reachedEarly = SingleCorner(DistanceAtWhichACornerStartsWorking * 0.5f);
            var reachedLate = SingleCorner(DistanceAtWhichACornerStartsWorking + LegMargin);

            var early = RunnerTest.RunAt(reachedEarly, new[] { 0 });
            var late = RunnerTest.RunAt(reachedLate, new[] { 0 });

            Assert.That(early.Attempts[0].Released, Is.False,
                "Rounded too soon: the Runner has not yet opened the "
                + GameConstants.SingleCornerMinDistance + " m that "
                + GameConstants.AggroReleaseLineOfSightBreak + " s at "
                + GameConstants.MonsterBaseSpeed + " m/s costs, so the monster is round the corner "
                + "before the cover counts:\n" + early.Describe());
            Assert.That(early.Attempts[0].BreaksCrossed, Is.EqualTo(1),
                "The corner was rounded — it simply came too early. §12: \"단일 모퉁이에 의존하면 "
                + "안 된다\".");

            Assert.That(late.Attempts[0].Released, Is.True,
                "The same single corner, reached after the sprint has opened the gap, does release. "
                + "§12's conclusion is about distance at the corner, not about corners:\n"
                + late.Describe());
            Assert.That(late.Attempts[0].GapMetres,
                Is.GreaterThanOrEqualTo(GameConstants.AggroReleaseDistance),
                "§06 needs both clauses — the gap as well as the cover.");
        }

        /// <summary>
        /// §12's other conclusion: 연속 차단 works where a single corner does not. The
        /// S자 통로 releases from a standing start, at a distance where one corner has
        /// already been shown to fail.
        /// </summary>
        [Test]
        public void RunnerTest_AnSCorridor_ReleasesWhereASingleCornerCannot()
        {
            var oneCorner = SingleCorner(GameConstants.SCorridorLegLength);
            var sCorridor = Serpentine(6, GameConstants.SCorridorLegLength, true);

            var single = RunnerTest.RunAt(oneCorner, new[] { 0 });
            var chained = RunnerTest.RunAt(sCorridor, new[] { 0 });

            Assert.That(single.Attempts[0].Released, Is.False,
                "A corner at " + GameConstants.SCorridorLegLength + " m is reached before the gap is "
                + "wide enough to hold it.");
            Assert.That(chained.Attempts[0].Released, Is.True,
                "Legs of the same length, bent twice more, do release — §12: \"10m 구간 2개의 S자 "
                + "통로 하나면 해제가 성립한다. 이것이 맵의 기본 단위다.\":\n" + chained.Describe());
            Assert.That(chained.Attempts[0].BreaksCrossed, Is.GreaterThan(1),
                "\"연속 차단\" means more than one corner is doing the work.");
        }

        /// <summary>
        /// A bend drawn inside an 개방 공간 hides nobody: §12 gives halls 15~25 m sight
        /// lines on purpose, because that is where aggro is taken. The same geometry
        /// therefore has to grade differently depending on what the room is.
        /// </summary>
        [Test]
        public void RunnerTest_ABendInsideAHall_DoesNotBreakSight()
        {
            var maze = Serpentine(24, GameConstants.SCorridorLegLength * 0.4f, false);
            var hall = Serpentine(24, GameConstants.SCorridorLegLength * 0.4f, false, true);

            var inMaze = RunnerTest.RunAt(maze, EveryNode(maze));
            var inHall = RunnerTest.RunAt(hall, EveryNode(hall));

            Assert.That(inMaze.Successes, Is.EqualTo(inMaze.SampleCount));
            Assert.That(inHall.Successes, Is.Zero,
                "Identical corners, marked 개방 공간, must stop counting as 시야 차단 지점. §12 sizes a "
                + "hall's sight line at " + GameConstants.LineOfSightBreakSpacingMin + "~"
                + GameConstants.LineOfSightBreakSpacingMax + " m, which is longer than any bend drawn "
                + "inside one:\n" + inHall.Describe());
        }

        /// <summary>
        /// A seed has to replay a verdict exactly. §13 wants a balance report from a
        /// player to be reproducible here; a map that scores 5/10 in CI and 7/10 on the
        /// machine investigating why is not a measurement.
        /// </summary>
        [Test]
        public void RunnerTest_IsReproducibleFromItsSeed()
        {
            var map = BuildLongHouse();

            var first = RunnerTest.Run(map, new DeterministicRandom(Seed));
            var second = RunnerTest.Run(map, new DeterministicRandom(Seed));

            Assert.That(second.Successes, Is.EqualTo(first.Successes));
            Assert.That(second.Attempts.Select(a => a.StartNodeId),
                Is.EqualTo(first.Attempts.Select(a => a.StartNodeId)));
            for (var i = 0; i < first.Attempts.Length; i++)
            {
                Assert.That(second.Attempts[i].Released, Is.EqualTo(first.Attempts[i].Released));
                Assert.That(second.Attempts[i].ElapsedSeconds,
                    Is.EqualTo(first.Attempts[i].ElapsedSeconds).Within(0f),
                    "Attempt " + i + " diverged, so the test is reading a clock somewhere.");
            }
        }

        /// <summary>
        /// §12 samples ten points, and ten points means ten different places. Drawing
        /// the same corner twice would let one good corridor carry a verdict.
        /// </summary>
        [Test]
        public void RunnerTest_SamplesTenDistinctPlaces()
        {
            var map = BuildLongHouse();
            var report = RunnerTest.Run(map, new DeterministicRandom(Seed));

            Assert.That(report.Attempts.Select(a => a.StartNodeId).Distinct().Count(),
                Is.EqualTo(GameConstants.RunnerTestSampleCount),
                "Sampling with replacement would let a single escapable corner be graded ten times.");
        }

        /// <summary>
        /// §06 calls "질주를 언제 쓸 것인가" the Runner's real dilemma, and
        /// <see cref="RunnerTest"/> simulates both answers. Inside §12's search horizon
        /// it cannot matter, and this pins why.
        /// <para>
        /// The sprint bar carries <c>12 s × 5.6 = 67.2 m</c> of travel, and §12 frames
        /// every release inside one sprint's travel — 60 m. So the Runner can sprint the
        /// entire distance the test ever looks at, the bar is never scarce, and spending
        /// it immediately dominates every way of holding it: the gap at any instant is
        /// widest when the sprint was spent earliest. Every attempt therefore reports a
        /// delay of zero, and §06's dilemma has no effect on a map's grade.
        /// </para>
        /// <para>
        /// That is a fact about the numbers, not a defect in the map: the timing
        /// question is a fight against the <em>monster's</em> position, which this test
        /// fixes by construction. It is recorded here because it is invisible otherwise
        /// — the report has a SprintDelaySeconds column that cannot currently be
        /// anything but zero — and because it stops being true the moment §05's 60 m or
        /// §06's 12 s move. See docs/BALANCE-FINDINGS.md.
        /// </para>
        /// </summary>
        [Test]
        public void RunnerTest_SpendingTheSprintAtOnce_DominatesHoldingIt()
        {
            var sprintCarries = GameConstants.SprintStaminaSeconds * GameConstants.RunnerSprintSpeed;

            Assert.That(sprintCarries, Is.GreaterThan(GameConstants.SprintMaxTravelDistance),
                "§06's bar carries " + sprintCarries + " m and §12 only ever looks "
                + GameConstants.SprintMaxTravelDistance + " m along a route. While that holds, the "
                + "sprint cannot run out before the search does, so holding it can never pay.");

            var map = BuildLongHouse();
            var report = RunnerTest.RunAt(map, EveryNode(map));

            Assert.That(report.Attempts.All(a => a.SprintDelaySeconds <= MathX.Epsilon), Is.True,
                "An attempt reported a held sprint. That would mean the bar became scarce inside "
                + "§12's horizon — check whether §05's " + GameConstants.SprintMaxTravelDistance
                + " m or §06's " + GameConstants.SprintStaminaSeconds + " s moved, because §12's "
                + "cover spacing is derived from both.");

            var released = report.Attempts.Where(a => a.Released).ToArray();
            Assert.That(released, Is.Not.Empty);
            Assert.That(released.All(a => a.ElapsedSeconds < GameConstants.SprintStaminaSeconds), Is.True,
                "Every release lands before the bar empties, which is the same statement from the "
                + "other side: no map in §12's size range can make the Runner ration the sprint.");
        }

        // ====================================================================
        // Derived geometry — §12's arithmetic, measured off the graph.
        // ====================================================================

        /// <summary>
        /// §12: "구간 L = 10m, 총 20m, 통과 시간 = 20 / 4.8 = 4.2초 ≥ 3초." The margin is
        /// 1.2 s, and it is the entire reason the S자 통로 is called 맵의 기본 단위.
        /// </summary>
        [Test]
        public void SCorridor_TakesTheMonsterLongerToClearThanTheReleaseNeeds()
        {
            var transit = GameConstants.SCorridorLegLength * 2f / GameConstants.MonsterBaseSpeed;

            Assert.That(transit, Is.EqualTo(4.1667f).Within(0.01f), "§12 rounds this to 4.2 s.");
            Assert.That(transit, Is.GreaterThan(GameConstants.AggroReleaseLineOfSightBreak),
                "If two 10 m legs stopped outlasting the "
                + GameConstants.AggroReleaseLineOfSightBreak
                + " s of cover a release needs, §12's base unit would stop working and every zone on "
                + "every map would need redrawing.");
            Assert.That(GameConstants.SCorridorLegLength * 2f,
                Is.LessThanOrEqualTo(GameConstants.MaxStraightCorridor),
                "The S자 통로 is drawn from two legs that are each legal on their own — 20 m of "
                + "corridor bent twice. If the legs did not fit inside §12's straight-corridor limit "
                + "the section would be asking for a shape it also forbids.");
        }

        /// <summary>
        /// The S자 통로 the graph finds must really be one: two legs at full length and
        /// a qualifying bend at <em>both</em> interior nodes. One bend is a single
        /// corner, and §12 spends half a page proving a single corner is not enough.
        /// </summary>
        [Test]
        public void SCorridor_FoundInTheSketchMap_HasTwoLegsAndTwoBends()
        {
            var map = BuildSketchMap(Flaw.None);

            for (var z = 0; z < map.Zones.Length; z++)
            {
                var path = map.FindSCorridor(z);
                Assert.That(path, Is.Not.Null, "Zone " + map.Zones[z].Name + " has no S자 통로.");
                Assert.That(path!.Length, Is.EqualTo(4));

                var first = EdgeBetween(map, path[0], path[1]);
                var connector = EdgeBetween(map, path[1], path[2]);
                var second = EdgeBetween(map, path[2], path[3]);

                Assert.That(map.Edges[first].Length,
                    Is.GreaterThanOrEqualTo(GameConstants.SCorridorLegLength));
                Assert.That(map.Edges[second].Length,
                    Is.GreaterThanOrEqualTo(GameConstants.SCorridorLegLength));

                Assert.That(map.BendDegrees(path[1], first, connector),
                    Is.GreaterThanOrEqualTo(GameConstants.MapSightBreakingBendDegrees),
                    "First bend in " + map.Zones[z].Name + " does not break a sight line.");
                Assert.That(map.BendDegrees(path[2], connector, second),
                    Is.GreaterThanOrEqualTo(GameConstants.MapSightBreakingBendDegrees),
                    "Second bend in " + map.Zones[z].Name + " does not break a sight line — which "
                    + "makes this a single corner, the structure §12 proves insufficient.");

                var total = map.Edges[first].Length + map.Edges[connector].Length + map.Edges[second].Length;
                Assert.That(total / GameConstants.MonsterBaseSpeed,
                    Is.GreaterThan(GameConstants.AggroReleaseLineOfSightBreak),
                    "The monster has to spend longer clearing the S than a release needs, or the "
                    + "structure is decoration.");
                Assert.That(total,
                    Is.GreaterThan(Vec3.DistanceFlat(map.Nodes[path[0]].Position, map.Nodes[path[3]].Position)),
                    "Walking the S must be longer than looking across it. Their divergence is the "
                    + "whole of §12's argument: if the corridor's length and its sight line agreed, "
                    + "no amount of bending would buy the Runner anything.");
            }
        }

        /// <summary>
        /// §12: "괴물이 그 모퉁이에 도달하는 시간 = D / 4.8초, 시야 차단 3초가 필요 →
        /// D ≥ 14.4m." Not a tuned constant — a quotient of two §06 numbers, and the
        /// threshold a door has to beat before locking it is worth the Engineer's time.
        /// </summary>
        [Test]
        public void SingleCorner_RequiresFourteenPointFourMetres()
        {
            Assert.That(GameConstants.SingleCornerMinDistance,
                Is.EqualTo(GameConstants.AggroReleaseLineOfSightBreak * GameConstants.MonsterBaseSpeed)
                    .Within(0.01f),
                "14.4 m is 3 s at 4.8 m/s. If either §06 number is retuned this has to move with it, "
                + "or §12's map rules quietly stop matching the monster they were derived from.");

            Assert.That(GameConstants.SprintDistanceGain,
                Is.LessThan(GameConstants.SingleCornerMinDistance),
                "§12's table: a sprint from a standing start buys "
                + GameConstants.SprintDistanceGain + " m, which is less than a single corner needs. "
                + "That gap is precisely why the section concludes 주자는 멀리서 어그로를 걸어야 한다 "
                + "and why 연속 차단 exists at all.");

            Assert.That(GameConstants.RunnerTestAggroStartDistance + GameConstants.SprintDistanceGain,
                Is.GreaterThan(GameConstants.SingleCornerMinDistance),
                "§12's table rates a 10 m start ✅ — 10 + 9.6 = 19.6 m at the corner. The runner test "
                + "starts there for that reason.");
        }

        /// <summary>
        /// A door is worth hanging only where the way round costs the monster more time
        /// than a release needs. §12 words it as "순환로의 목에"; the threshold is 14.4 m,
        /// so the test walks a passage from just under it to just over.
        /// </summary>
        [Test]
        public void Bottleneck_ThresholdIsTheSingleCornerDistance()
        {
            var tooShort = DetourPair(GameConstants.SingleCornerMinDistance - JustInside);
            var justEnough = DetourPair(GameConstants.SingleCornerMinDistance + JustInside);

            Assert.That(tooShort.IsBottleneck(0), Is.False,
                "Locking this door forces a detour the monster clears in under "
                + GameConstants.AggroReleaseLineOfSightBreak
                + " s, so it does not buy even one aggro release — and the Engineer spent "
                + GameConstants.EngineerDoorLockSeconds + " s and "
                + GameConstants.EngineerDoorLockMaterialCost + " material on it.");
            Assert.That(justEnough.IsBottleneck(0), Is.True);

            Assert.That(justEnough.CutsALoop(0), Is.True,
                "§12: \"순환로의 목에 문 하나 → 잠그면 순환이 끊김.\" A passage with a way round is a "
                + "loop-cutting door; one without is a corridor-sealing one, and a designer needs to "
                + "know which is which.");
        }

        /// <summary>
        /// A bridge — a passage whose loss splits the map — is a 병목 by definition, and
        /// has to be reported as one rather than as an infinite-detour error.
        /// </summary>
        [Test]
        public void Bottleneck_APassageThatSplitsTheMapQualifiesOutright()
        {
            var chain = Tree();

            Assert.That(float.IsPositiveInfinity(chain.DetourWithout(0)), Is.True);
            Assert.That(chain.IsBottleneck(0), Is.True);
            Assert.That(chain.CutsALoop(0), Is.False,
                "There is no 순환로 to cut — shutting this seals a corridor, which is a different "
                + "decision for the Engineer to make.");
        }

        /// <summary>
        /// §12 정비공: locking the door must actually change the map. The probe is where
        /// door state lives, because shutting a door is a change to the world and not to
        /// the level.
        /// </summary>
        [Test]
        public void LockingADoor_LengthensTheWayRoundByAtLeastAnAggroRelease()
        {
            var map = BuildSketchMap(Flaw.None);
            var door = EveryEdge(map).First(e => map.Edges[e].HasLockableDoor);
            var a = map.Nodes[map.Edges[door].A].Position;
            var b = map.Nodes[map.Edges[door].B].Position;

            var probe = new MapGraphProbe(map);
            var open = probe.NavigableDistance(a, b);

            probe.SetEdgeBlocked(door, true);
            var shut = probe.NavigableDistance(a, b);

            Assert.That(probe.IsEdgeBlocked(door), Is.True);
            Assert.That(shut - open, Is.GreaterThanOrEqualTo(GameConstants.SingleCornerMinDistance),
                "Shutting the door has to cost the monster at least the "
                + GameConstants.AggroReleaseLineOfSightBreak + " s of ground a release needs, or §12's "
                + "\"잠그면 순환이 끊김\" is a sentence about nothing.");
            Assert.That((shut - open) / GameConstants.MonsterBaseSpeed,
                Is.GreaterThanOrEqualTo(GameConstants.AggroReleaseLineOfSightBreak));

            probe.SetEdgeBlocked(door, false);
            Assert.That(probe.NavigableDistance(a, b), Is.EqualTo(open).Within(1e-3f),
                "Door state is runtime, so reopening must restore the level exactly.");
        }

        // ====================================================================
        // Sight lines and straight runs.
        // ====================================================================

        /// <summary>
        /// A T-junction does not break a sight line that carries straight on through it,
        /// and that is why §12 measures a run rather than an edge: two 12 m corridors
        /// meeting head-on are a 24 m sight line however many doorways open off them.
        /// </summary>
        [Test]
        public void StraightRun_CarriesThroughAJunctionThatDoesNotTurn()
        {
            var map = StraightCorridor();

            var longest = map.LongestStraightRun(out var chain);
            Assert.That(longest, Is.GreaterThan(GameConstants.MaxStraightCorridor));
            Assert.That(chain.Length, Is.GreaterThan(2),
                "The run has to be reported as the chain of places it passes through — the designer "
                + "needs to know where to put the bend.");
            Assert.That(map.HasStraightSightLine(chain[0], chain[chain.Length - 1]), Is.True,
                "Sight and straight-run measurement have to agree, or the validator and the monster "
                + "would disagree about the same corridor.");
        }

        /// <summary>
        /// Sight is a straight run and distance is a walk, and §12's entire S자 통로
        /// argument is that the two diverge. If they agreed, no amount of bending would
        /// buy anything.
        /// </summary>
        [Test]
        public void SightAndWalkingDistance_DivergeAcrossAnSCorridor()
        {
            var map = Serpentine(6, GameConstants.SCorridorLegLength, true);
            var start = 0;
            var end = map.Nodes.Length - 1;

            Assert.That(map.HasStraightSightLine(start, end), Is.False);
            Assert.That(map.PathLength(start, end),
                Is.GreaterThan(Vec3.DistanceFlat(map.Nodes[start].Position, map.Nodes[end].Position)),
                "Walking has to be longer than looking, or the corridor is not bent at all.");
            Assert.That(map.PathLength(start, end) / GameConstants.MonsterBaseSpeed,
                Is.GreaterThan(GameConstants.AggroReleaseLineOfSightBreak));
        }

        /// <summary>
        /// A 시야 차단 지점 is derived from geometry, not declared. A designer who draws a
        /// gentle curve and labels it a corner must see it counted as the straight
        /// corridor it is.
        /// </summary>
        [Test]
        public void SightBreakingCorner_IsMeasuredNotDeclared()
        {
            var map = BuildSketchMap(Flaw.None);
            var ring = NodeNamed(map, "A 나무 도달2");
            var straight = StraightCorridor();

            Assert.That(map.IsSightBreakingCorner(ring), Is.True,
                "A ring corner turns 90° — well past §12's "
                + GameConstants.MapSightBreakingBendDegrees + "°.");
            Assert.That(straight.IsSightBreakingCorner(1), Is.False,
                "A place on a straight corridor is not cover, whatever it is called.");
        }

        // ====================================================================
        // Zones — the granularity almost every §12 rule counts in.
        // ====================================================================

        /// <summary>
        /// §12's floor table exists so the Listener can name a zone, so the surfaces
        /// have to be distinguishable from each other, not merely different enum values.
        /// </summary>
        [Test]
        public void FloorMaterials_AreDistinguishableToTheListener()
        {
            var clarities = new[]
            {
                MapZone.ClarityOf(FloorMaterial.Wood),
                MapZone.ClarityOf(FloorMaterial.Tile),
                MapZone.ClarityOf(FloorMaterial.Gravel),
                MapZone.ClarityOf(FloorMaterial.Concrete),
                MapZone.ClarityOf(FloorMaterial.Metal),
            };

            Assert.That(clarities.Distinct().Count(), Is.EqualTo(clarities.Length),
                "Two surfaces with the same clarity are one surface as far as §04's 위치 판별 is "
                + "concerned, and §12 assigns them per zone precisely to be told apart.");
            Assert.That(MapZone.ClarityOf(FloorMaterial.Metal), Is.EqualTo(clarities.Max()),
                "§12 gives stairs 금속 울림 — the loudest surface in the building, which is what makes "
                + "\"지금 계단이야\" a usable call.");
            Assert.That(MapZone.ClarityOf(FloorMaterial.Concrete), Is.EqualTo(clarities.Min()),
                "and 콘크리트 둔탁 the murkiest, which is what makes zone D the monster's best approach.");
        }

        /// <summary>
        /// Zones may sit edge to edge — §12's sketch stacks them — but must not
        /// interpenetrate, because a footstep inside an overlap belongs to two surfaces
        /// at once and "재질 경계를 명확히" is the one thing §12 says outright.
        /// </summary>
        [Test]
        public void Zones_MayTouchButMayNotOverlap()
        {
            var side = ZoneSide;
            var floor = new Vec3(side, 0f, side);
            var left = new MapZone(0, "A", FloorMaterial.Wood, new Vec3(side * 0.5f, 0f, side * 0.5f), floor);
            var flush = new MapZone(1, "B", FloorMaterial.Tile, new Vec3(side * 1.5f, 0f, side * 0.5f), floor);
            var overlapping = new MapZone(1, "B", FloorMaterial.Tile,
                new Vec3(side * 1.5f - JustInside, 0f, side * 0.5f), floor);

            Assert.That(left.OverlapsVolume(flush), Is.False,
                "A shared wall is how §12's sketch is drawn; rejecting it would make the section "
                + "impossible to build.");
            Assert.That(left.OverlapsVolume(overlapping), Is.True);
            Assert.That(left.OverlapsVolume(left), Is.False, "A zone does not overlap itself.");
        }

        /// <summary>
        /// §12 sizes a zone at 30~40 m across so that a Runner crosses two or three of
        /// them on one sprint, and so that a Listener's error names the wrong corner
        /// rather than the wrong room.
        /// </summary>
        [Test]
        public void ZoneDiagonal_SitsBetweenTheListenersErrorAndTheRunnersSprint()
        {
            var map = BuildSketchMap(Flaw.None);

            foreach (var zone in map.Zones)
            {
                Assert.That(zone.Diagonal,
                    Is.InRange(GameConstants.ZoneDiagonalMin, GameConstants.ZoneDiagonalMax));
                Assert.That(zone.Diagonal, Is.GreaterThan(GameConstants.ListenerErrorRadiusMax * 2f),
                    "A zone smaller than the Listener's own error radius would make a fix name the "
                    + "wrong zone, and §04's whole contribution is naming the right one.");
            }

            Assert.That(GameConstants.SprintMaxTravelDistance / GameConstants.ZoneDiagonalMax,
                Is.GreaterThanOrEqualTo(1f),
                "§12: \"주자가 구역 2~3개 관통 가능\" on one sprint of "
                + GameConstants.SprintMaxTravelDistance + " m.");
        }

        /// <summary>A zone's floor answers for any position inside it, and for nothing outside.</summary>
        [Test]
        public void FloorAt_AnswersInsideTheMapAndNowhereElse()
        {
            var map = BuildSketchMap(Flaw.None);
            var inside = map.Nodes[NodeNamed(map, "A 나무 도달1")].Position;

            Assert.That(map.FloorAt(inside), Is.EqualTo(FloorMaterial.Wood));
            Assert.That(map.ZoneIdAt(inside), Is.EqualTo(0));

            var offMap = new Vec3(-GameConstants.MapExtent, 0f, -GameConstants.MapExtent);
            Assert.That(map.ZoneIdAt(offMap), Is.EqualTo(-1));
            Assert.That(map.FloorAt(offMap), Is.EqualTo(FloorMaterial.None),
                "Off the map is not a surface. Returning a real one would let a Listener hear a "
                + "footstep from outside the building.");
        }

        /// <summary>
        /// Height is not distance. §12's rules are horizontal, and a stairwell landing
        /// must not read as extra range or the Observer's 15 m would mean something
        /// different upstairs.
        /// </summary>
        [Test]
        public void MapDistances_AreHorizontal()
        {
            var builder = OneZoneBuilder("계단실");
            var ground = builder.AddNode(0, new Vec3(10f, 0f, 10f), MapNodeKind.Stairwell, "1층");
            var landing = builder.AddNode(0, new Vec3(10f + GameConstants.SCorridorLegLength, 20f, 10f),
                MapNodeKind.Stairwell, "2층");
            builder.Connect(ground, landing);
            var map = builder.Build();

            Assert.That(map.Edges[0].Length, Is.EqualTo(GameConstants.SCorridorLegLength).Within(1e-3f),
                "The flight is 10 m of floor plan however tall it is.");
            Assert.That(map.NearestNode(new Vec3(10f, 50f, 10f)), Is.EqualTo(ground),
                "Standing directly above a place is standing at it.");
        }

        // ====================================================================
        // The ugly cases. Half a map is what a level looks like while it is
        // being built, and the validator is most useful exactly then.
        // ====================================================================

        /// <summary>An empty map must fail loudly rather than pass vacuously.</summary>
        [Test]
        public void EmptyMap_FailsEveryRuleItCanRatherThanPassingVacuously()
        {
            var empty = new MapGraph(
                Array.Empty<MapZone>(), Array.Empty<MapNode>(), Array.Empty<MapEdge>(), "빈 맵");

            var report = MapValidator.Validate(empty);

            Assert.That(report.Passed, Is.False,
                "Nothing satisfies a rule about places by having no places. A validator that passes "
                + "an empty map passes every map that is still being built.");
            Assert.That(report.ChecklistPassed, Is.False);
            Assert.That(report.FailedRuleIds, Does.Contain(MapValidator.RuleConnectivity));
            Assert.That(report.FailedRuleIds, Does.Contain(MapValidator.RuleDeadEnds));

            Assert.That(empty.IndependentLoopCount, Is.Zero);
            Assert.That(empty.ConnectedComponentCount, Is.Zero);
            Assert.That(empty.FindLoop(), Is.Null);
            Assert.That(empty.NearestNode(Vec3.Zero), Is.EqualTo(-1));
            Assert.That(empty.LongestStraightRun(out var chain), Is.Zero);
            Assert.That(chain, Is.Empty);
            Assert.That(empty.ZoneIdAt(Vec3.Zero), Is.EqualTo(-1));
        }

        /// <summary>One place and no passages: the state a level is in the moment it is created.</summary>
        [Test]
        public void SingleNodeMap_IsADeadEndAndNothingElse()
        {
            var builder = OneZoneBuilder("한 지점");
            builder.AddNode(0, new Vec3(10f, 0f, 10f), MapNodeKind.None, "유일한 방");
            var map = builder.Build();

            Assert.That(map.Degree(0), Is.Zero);
            Assert.That(map.IsDeadEnd(0), Is.True);
            Assert.That(map.IndependentLoopCount, Is.Zero);
            Assert.That(map.ConnectedComponentCount, Is.EqualTo(1));
            Assert.That(map.IsConnected, Is.True, "One piece is one piece.");

            var attempt = RunnerTest.RunAt(map, new[] { 0 }).Attempts[0];
            Assert.That(attempt.Released, Is.False);
            Assert.That(attempt.Explanation, Does.Contain("nowhere to run"),
                "Aggro taken where there is no passage out can only end one way, and the report has "
                + "to say that rather than report a rate.");

            Assert.That(MapValidator.Validate(map)[MapValidator.RuleDeadEnds].Passed, Is.False);
        }

        /// <summary>
        /// A map in two pieces is two maps. §03 chains three clues across the building,
        /// so a piece nothing can walk to is a match that cannot be won.
        /// </summary>
        [Test]
        public void DisconnectedMap_FailsConnectivity()
        {
            var map = Forest();
            var report = MapValidator.Validate(map);

            Assert.That(report[MapValidator.RuleConnectivity].Passed, Is.False);
            Assert.That(report[MapValidator.RuleConnectivity].Detail, Does.Contain("2"));

            var acrossThePieces = map.PathLength(0, map.Nodes.Length - 1);
            Assert.That(float.IsPositiveInfinity(acrossThePieces), Is.True,
                "Unreachable has to be infinity rather than a large number, or a monster would set "
                + "off towards a place it can never arrive at.");
            Assert.That(map.ShortestPath(0, map.Nodes.Length - 1, -1), Is.Null);
        }

        /// <summary>A place outside the zone it claims makes every per-zone count in §12 fiction.</summary>
        [Test]
        public void NodeOutsideItsOwnZone_FailsZoneMembership()
        {
            var builder = new MapGraphBuilder().Named("엉뚱한 구역");
            builder.AddZone("A", FloorMaterial.Wood, new Vec3(10f, 0f, 10f), new Vec3(20f, 0f, 20f));
            var a = builder.AddNode(0, new Vec3(5f, 0f, 5f), MapNodeKind.None, "안쪽");
            var b = builder.AddNode(0, new Vec3(GameConstants.MapExtent, 0f, GameConstants.MapExtent),
                MapNodeKind.None, "바깥쪽");
            builder.Connect(a, b);
            var map = builder.Build();

            var report = MapValidator.Validate(map);
            Assert.That(report[MapValidator.RuleZoneMembership].Passed, Is.False);
            Assert.That(report[MapValidator.RuleZoneMembership].Detail, Does.Contain("바깥쪽"),
                "The Listener would hear this place on a floor it is not standing on, so the report "
                + "has to name it.");
        }

        /// <summary>A passage from a place to itself is not a 순환로, and the graph refuses to pretend otherwise.</summary>
        [Test]
        public void SelfLoop_IsRejected()
        {
            var builder = OneZoneBuilder("자기 자신");
            var only = builder.AddNode(0, new Vec3(10f, 0f, 10f), MapNodeKind.None, "방");

            Assert.That(() => builder.Connect(only, only), Throws.ArgumentException,
                "§12's loops need two ways round. A self-loop would inflate the 순환로 count without "
                + "giving the monster anything to guess.");

            var zones = new[] { new MapZone(0, "A", FloorMaterial.Wood, Vec3.Zero, new Vec3(20f, 0f, 20f)) };
            var nodes = new[] { new MapNode(0, 0, Vec3.Zero, MapNodeKind.None, 0, null) };
            var edges = new[] { new MapEdge(0, 0, 0, 1f, false, null) };
            Assert.That(() => new MapGraph(zones, nodes, edges, "손으로 만든 맵"), Throws.ArgumentException);
        }

        /// <summary>Null is not a map, and validating one has to say so rather than crash somewhere deeper.</summary>
        [Test]
        public void NullMap_IsRejectedByEveryEntryPoint()
        {
            Assert.That(() => MapValidator.Validate(null), Throws.ArgumentNullException);
            Assert.That(() => RunnerTest.Run(null, new DeterministicRandom(Seed)),
                Throws.ArgumentNullException);
            Assert.That(() => RunnerTest.Run(BuildSketchMap(Flaw.None), null),
                Throws.ArgumentNullException);
            Assert.That(() => new MapGraphProbe(null), Throws.ArgumentNullException);
        }

        /// <summary>The runner test on a map with nothing in it reports nothing, not a verdict about nothing.</summary>
        [Test]
        public void RunnerTest_OnAnEmptyMap_ReportsNoAttempts()
        {
            var empty = new MapGraph(
                Array.Empty<MapZone>(), Array.Empty<MapNode>(), Array.Empty<MapEdge>(), null);

            var report = RunnerTest.Run(empty, new DeterministicRandom(Seed));

            Assert.That(report.SampleCount, Is.Zero);
            Assert.That(report.Successes, Is.Zero);
            Assert.That(report.SuccessRate, Is.Zero, "0/0 is 0, not NaN — a NaN would grade as 적정.");
            Assert.That(report.Verdict, Is.EqualTo(RunnerTestVerdict.TooHard));
            Assert.That(report.MapName, Is.EqualTo("unnamed map"));
        }

        /// <summary>
        /// A start point off the end of the map must produce a report rather than an
        /// exception: the editor re-runs the same ten node ids after an edit, and one of
        /// them may no longer exist.
        /// </summary>
        [Test]
        public void RunnerTest_FromAPlaceThatDoesNotExist_SaysSoInsteadOfThrowing()
        {
            var map = BuildSketchMap(Flaw.None);

            var report = RunnerTest.RunAt(map, new[] { -1, map.Nodes.Length });

            Assert.That(report.Attempts.All(a => !a.Released), Is.True);
            Assert.That(report.Attempts.All(a => a.Explanation.Contains("no such place")), Is.True);
        }

        /// <summary>
        /// A step longer than the fixed step could carry the Runner past a corner inside
        /// one update and hand back a release the map never earned, so the settings
        /// clamp it. Same for the numbers that would make the simulation meaningless.
        /// </summary>
        [Test]
        public void RunnerTestSettings_ClampWhatWouldMakeTheSimulationLie()
        {
            var absurd = new RunnerTestSettings(-1f, -1f, 0, GameConstants.FixedStep * 100f, -1f);

            Assert.That(absurd.MonsterSpeed, Is.EqualTo(GameConstants.MonsterBaseSpeed));
            Assert.That(absurd.AggroStartDistance, Is.EqualTo(GameConstants.RunnerTestAggroStartDistance));
            Assert.That(absurd.SampleCount, Is.EqualTo(GameConstants.RunnerTestSampleCount));
            Assert.That(absurd.RouteReachMetres, Is.EqualTo(GameConstants.SprintMaxTravelDistance));
            Assert.That(absurd.MonsterStep, Is.LessThanOrEqualTo(GameConstants.FixedStep),
                "A coarse step lets the Runner cross a corner and three metres of corridor in one "
                + "update, which reads as cover the map never provided.");

            var zeroStep = new RunnerTestSettings(
                GameConstants.MonsterBaseSpeed, GameConstants.RunnerTestAggroStartDistance,
                GameConstants.RunnerTestSampleCount, 0f, GameConstants.SprintMaxTravelDistance);
            Assert.That(zeroStep.MonsterStep, Is.EqualTo(GameConstants.FixedStep),
                "A zero step would never advance the clock — an infinite loop rather than a verdict.");
        }

        /// <summary>
        /// §07 raises the monster to 5.2 m/s by 새벽. A map that only works at 4.8 stops
        /// working then, and the settings exist so the sweep can find that out.
        /// </summary>
        [Test]
        public void RunnerTest_AtALaterThreatTier_IsNoEasier()
        {
            var map = BuildLongHouse();
            var start = EveryNode(map);

            var early = RunnerTest.RunAt(map, start, RunnerTestSettings.Default);
            var lateSettings = new RunnerTestSettings(
                GameConstants.ThreatSpeedBeforeSunrise,
                GameConstants.RunnerTestAggroStartDistance,
                GameConstants.RunnerTestSampleCount,
                GameConstants.FixedStep,
                GameConstants.SprintMaxTravelDistance);
            var late = RunnerTest.RunAt(map, start, lateSettings);

            Assert.That(GameConstants.ThreatSpeedBeforeSunrise,
                Is.GreaterThan(GameConstants.MonsterBaseSpeed));
            Assert.That(late.Successes, Is.LessThanOrEqualTo(early.Successes),
                "A faster monster cannot make a map easier to escape. If it does, the simulation is "
                + "measuring something other than the chase.");
            Assert.That(GameConstants.RunnerSprintSpeed - GameConstants.ThreatSpeedBeforeSunrise,
                Is.LessThan(GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed),
                "By 새벽 the Runner's margin has shrunk from "
                + (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed) + " to "
                + (GameConstants.RunnerSprintSpeed - GameConstants.ThreatSpeedBeforeSunrise)
                + " m/s, so every 시야 차단 지점 on the map has to sit closer together than it did at "
                + "초저녁.");
        }

        /// <summary>Looking up a rule that was never checked has to be an error, not a silent pass.</summary>
        [Test]
        public void Report_LookingUpARuleThatWasNotChecked_Throws()
        {
            var report = MapValidator.Validate(BuildSketchMap(Flaw.None));

            Assert.That(() => report["no-such-rule"], Throws.TypeOf<KeyNotFoundException>());
            Assert.That(report.Describe(), Does.Contain("[ok]   " + MapValidator.RuleStraightCorridor),
                "A report has to show the rules that passed as well as the ones that did not, or a "
                + "designer cannot tell a rule that was checked from one that was never run.");
            Assert.That(report.MapName, Is.EqualTo("§12 첫 맵 스케치"));
        }

        // ====================================================================
        // Fixtures.
        // ====================================================================

        private enum Flaw
        {
            None,
            StraightCorridorOverTwentyMetres,
            OpenSpaceNotAdjacentToMaze,
            NoSCorridorInOneZone,
            ZoneWithNoLoop,
            TooManyDeadEnds,
            TwoZonesShareAFloor,
            ZoneWithNoFloorMaterial,
            DoorAwayFromBottleneck,
            TooManyLockableDoors,
            TooManyZoneEntryPoints,
        }

        /// <summary>The corner ids and positions of one zone's ring, so the specials can hang off it.</summary>
        private sealed class Block
        {
            public Block(int zone, int[] corners, Vec3[] cornerPositions)
            {
                Zone = zone;
                Corners = corners;
                CornerPositions = cornerPositions;
            }

            public int Zone { get; }

            public int[] Corners { get; }

            public Vec3[] CornerPositions { get; }
        }

        /// <summary>
        /// §12's 첫 맵 스케치: B 타일 hall, A 나무, C 자갈 and D 콘크리트 with the exit,
        /// laid out as a 2×2 block inside §12's 100 m square.
        /// <para>
        /// Every zone is a ring of legs long enough to serve as an S자 통로, with side
        /// passages hung off the corners at 45° so that no two passages meeting anywhere
        /// on the map are within <see cref="GameConstants.MapSightBreakingBendDegrees"/>
        /// of each other by accident — the fixture must fail §12's rules only when a
        /// test asks it to.
        /// </para>
        /// </summary>
        private static MapGraph BuildSketchMap(Flaw flaw)
        {
            var map = new MapGraphBuilder().Named(
                flaw == Flaw.None ? "§12 첫 맵 스케치" : "§12 첫 맵 스케치 (" + flaw + ")");

            var side = ZoneSide;
            var g = SpurOffset;
            var maze = MapNodeKind.MazeSpace;
            var hallGate = flaw == Flaw.OpenSpaceNotAdjacentToMaze ? MapNodeKind.None : maze;

            // A 나무 — 단서 구역, bottom left.
            var a = AddZoneRing(
                map, "A 나무", FloorMaterial.Wood, new Vec3(0f, 0f, 0f), MapNodeKind.None,
                true,
                true,
                flaw == Flaw.NoSCorridorInOneZone,
                flaw != Flaw.ZoneWithNoLoop,
                flaw == Flaw.StraightCorridorOverTwentyMetres);

            var gateAne = Attach(map, a, 2, g, g, maze, "A 북동 통로", 0);
            var gateAnw = Attach(map, a, 3, -g, g, maze, "A 북서 통로", 0);
            var gateAse = Attach(map, a, 1, g, -g, maze, "A 남동 통로", 0);
            Attach(
                map, a, 0, -g, -g,
                MapNodeKind.None,
                "A 창고",
                DeadEndReward);
            Attach(map, a, 3, -g, -g, MapNodeKind.None, "A 다락", DeadEndReward);

            if (flaw == Flaw.TooManyDeadEnds)
            {
                Attach(map, a, 1, g, g, MapNodeKind.None, "A 광", DeadEndReward);
                Attach(map, a, 2, g, -g, MapNodeKind.None, "A 곳간", DeadEndReward);
            }

            // B 타일 — 개방 공간 (홀), top left.
            var b = AddZoneRing(
                map, "B 타일", FloorMaterial.Tile, new Vec3(0f, 0f, side), MapNodeKind.OpenSpace,
                true, true, false, true, false);

            var gateBsw = Attach(map, b, 0, -g, -g, hallGate, "B 남서 통로", 0);
            var gateBse = Attach(map, b, 1, g, -g, hallGate, "B 남동 통로", 0);
            var gateBne = Attach(map, b, 2, g, g, hallGate, "B 북동 통로", 0);
            Attach(map, b, 3, -g, g, MapNodeKind.None, "B 창고", DeadEndReward);
            Attach(map, b, 3, -g, -g, MapNodeKind.None, "B 배전실", DeadEndReward);

            // C 자갈 — 단서 구역, top right.
            var cFloor = flaw == Flaw.TwoZonesShareAFloor
                ? FloorMaterial.Wood
                : flaw == Flaw.ZoneWithNoFloorMaterial ? FloorMaterial.None : FloorMaterial.Gravel;
            var c = AddZoneRing(
                map, "C 자갈", cFloor, new Vec3(side, 0f, side), MapNodeKind.None,
                true, true, false, true, false);

            var gateCsw = Attach(map, c, 0, -g, -g, maze, "C 남서 통로", 0);
            var gateCnw = Attach(map, c, 3, -g, g, maze, "C 북서 통로", 0);
            var gateCse = Attach(map, c, 1, g, -g, maze, "C 남동 통로", 0);
            Attach(map, c, 2, g, g, MapNodeKind.None, "C 창고", DeadEndReward);
            Attach(map, c, 1, g, g, MapNodeKind.None, "C 자재실", DeadEndReward);

            // D 콘크리트 — 출구 구역, bottom right.
            var d = AddZoneRing(
                map, "D 콘크리트", FloorMaterial.Concrete, new Vec3(side, 0f, 0f), MapNodeKind.None,
                true, true, false, true, false);

            var gateDnw = Attach(map, d, 3, -g, g, maze, "D 북서 통로", 0);
            var gateDne = Attach(map, d, 2, g, g, maze, "D 북동 통로", 0);
            var gateDsw = Attach(map, d, 0, -g, -g, maze, "D 남서 통로", 0);
            Attach(map, d, 1, g, -g, MapNodeKind.None, "D 창고", DeadEndReward);
            Attach(map, d, 0, g, -g, MapNodeKind.None, "D 자재실", DeadEndReward);
            Attach(
                map, d, 1, -g, -g,
                MapNodeKind.None,
                "D 모퉁이",
                DeadEndReward);

            var exit = map.AddNode(
                d.Zone,
                new Vec3(d.CornerPositions[1].X + g, 0f, d.CornerPositions[1].Z + (RingSide * 0.5f)),
                MapNodeKind.Entrance | maze,
                "D 출입구");
            map.Connect(d.Corners[1], exit);
            map.Connect(d.Corners[2], exit);

            // 구역 간 진입점 — two per adjacent pair (§12: 2~3). The four gates that meet
            // in the middle of the map form a small ring of their own, which is why a
            // door hung there is not at a 병목.
            var crowdTheEngineer = flaw == Flaw.TooManyLockableDoors;
            map.Connect(gateAnw, gateBsw, crowdTheEngineer, "A–B 북쪽 잠금문");
            map.Connect(gateAne, gateBse, flaw == Flaw.DoorAwayFromBottleneck, "A–B 잠금문");
            map.Connect(gateBse, gateCsw);
            map.Connect(gateBne, gateCnw);
            map.Connect(gateCsw, gateDnw);
            map.Connect(gateCse, gateDne);
            map.Connect(gateDnw, gateAne);
            map.Connect(gateDsw, gateAse, crowdTheEngineer, "A–D 잠금문");

            if (flaw == Flaw.TooManyZoneEntryPoints)
            {
                map.Connect(gateAnw, gateBsw);
                map.Connect(gateAnw, gateBsw);
            }

            return map.Build();
        }

        /// <summary>
        /// The same construction strung out along §12's full 100 m: one 미로 구역 at the
        /// near end and three 개방 공간 halls beyond it.
        /// <para>
        /// This is the map §12's 실전 검증 is run against. It is legal by every rule in
        /// the section, and it is the shape §12 actually argues for — "개방 공간만 있으면
        /// 도망칠 곳이 없고, 미로만 있으면 멀리서 어그로를 걸 수 없다" — so breaking aggro
        /// is possible from the near half of the building and not from the far half.
        /// That is what a 5~7/10 map is.
        /// </para>
        /// </summary>
        private static MapGraph BuildLongHouse() => BuildLongHouse(SpurOffset);

        /// <summary>
        /// The long house with its 미로 구역's side passages hung <paramref name="spur"/>
        /// off each axis instead of <see cref="SpurOffset"/>.
        /// <para>
        /// The one knob that moves 시야 차단 지점 간격 and nothing else: no node is
        /// added or removed, no degree changes, no passage is re-routed. A wider spur
        /// simply stands its bend further from the ring corner it hangs off, so the two
        /// stop being one 시야 차단 지점 and become a piece of cover that is deep enough
        /// to finish §06's release from anywhere.
        /// </para>
        /// </summary>
        private static MapGraph BuildLongHouse(float spur)
        {
            var map = new MapGraphBuilder().Named("§12 개방 공간 + 미로 공간");
            var side = ZoneSide;
            var g = spur;
            var maze = MapNodeKind.MazeSpace;
            var hall = MapNodeKind.OpenSpace;

            var a = AddZoneRing(map, "A 나무", FloorMaterial.Wood, new Vec3(0f, 0f, 0f), maze,
                true, true, false, true, false);
            var gateAse = Attach(map, a, 1, g, -g, maze, "A 남동 통로", 0);
            var gateAne = Attach(map, a, 2, g, g, maze, "A 북동 통로", 0);
            Attach(map, a, 0, -g, -g, maze, "A 창고", DeadEndReward);
            Attach(map, a, 0, g, -g, maze, "A 곳간", DeadEndReward);
            Attach(map, a, 3, -g, -g, maze, "A 다락", DeadEndReward);
            Attach(map, a, 3, -g, g, maze, "A 광", DeadEndReward);

            var b = AddZoneRing(map, "B 타일", FloorMaterial.Tile, new Vec3(side, 0f, 0f), hall,
                true, true, false, true, false);
            var gateBsw = Attach(map, b, 0, -g, -g, hall, "B 남서 통로", 0);
            var gateBnw = Attach(map, b, 3, -g, g, hall, "B 북서 통로", 0);
            var gateBse = Attach(map, b, 1, g, -g, hall, "B 남동 통로", 0);
            var gateBne = Attach(map, b, 2, g, g, hall, "B 북동 통로", 0);
            Attach(map, b, 0, g, -g, hall, "B 창고", DeadEndReward);
            Attach(map, b, 3, g, g, hall, "B 배전실", DeadEndReward);

            var c = AddZoneRing(map, "C 자갈", FloorMaterial.Gravel, new Vec3(side * 2f, 0f, 0f), hall,
                true, true, false, true, false);
            var gateCsw = Attach(map, c, 0, -g, -g, hall, "C 남서 통로", 0);
            var gateCnw = Attach(map, c, 3, -g, g, hall, "C 북서 통로", 0);
            var gateCse = Attach(map, c, 1, g, -g, hall, "C 남동 통로", 0);
            var gateCne = Attach(map, c, 2, g, g, hall, "C 북동 통로", 0);
            Attach(map, c, 0, g, -g, hall, "C 창고", DeadEndReward);

            var d = AddZoneRing(map, "D 콘크리트", FloorMaterial.Concrete, new Vec3(side * 3f, 0f, 0f), hall,
                true, true, false, true, false);
            var gateDsw = Attach(map, d, 0, -g, -g, hall, "D 남서 통로", 0);
            var gateDnw = Attach(map, d, 3, -g, g, hall, "D 북서 통로", 0);
            Attach(map, d, 1, g, -g, hall, "D 창고", DeadEndReward);
            Attach(map, d, 1, -g, -g, MapNodeKind.None, "D 모퉁이", DeadEndReward);

            var exit = map.AddNode(
                d.Zone,
                new Vec3(d.CornerPositions[1].X + g, 0f, d.CornerPositions[1].Z + (RingSide * 0.5f)),
                MapNodeKind.Entrance | hall,
                "D 출입구");
            map.Connect(d.Corners[1], exit);
            map.Connect(d.Corners[2], exit);

            map.Connect(gateAse, gateBsw);
            map.Connect(gateAne, gateBnw);
            map.Connect(gateBse, gateCsw);
            map.Connect(gateBne, gateCnw);
            map.Connect(gateCse, gateDsw);
            map.Connect(gateCne, gateDnw);

            return map.Build();
        }

        private static Vec3 At(Vec3 origin, float x, float z) => new Vec3(origin.X + x, 0f, origin.Z + z);

        /// <summary>
        /// Adds one zone as a square ring of four corners: a 순환로, an S자 통로 and a
        /// 잠글 수 있는 문 at the neck of the ring, all from the same four legs.
        /// </summary>
        private static Block AddZoneRing(
            MapGraphBuilder map,
            string zoneName,
            FloorMaterial floor,
            Vec3 origin,
            MapNodeKind cornerKind,
            bool observationPost,
            bool candidateOnCorner2,
            bool halveTheLegs,
            bool closeRing,
            bool diagonalShortcut)
        {
            var zone = map.AddZone(
                zoneName,
                floor,
                new Vec3(origin.X + (ZoneSide * 0.5f), 0f, origin.Z + (ZoneSide * 0.5f)),
                new Vec3(ZoneSide, 0f, ZoneSide));

            var inset = (ZoneSide - RingSide) * 0.5f;
            var positions = new[]
            {
                At(origin, inset, inset),
                At(origin, inset + RingSide, inset),
                At(origin, inset + RingSide, inset + RingSide),
                At(origin, inset, inset + RingSide),
            };

            // 후보 지점 → 도달 지점, and the fourth corner loses §04's 관측 지점 outright.
            // Both parameters are kept in the signature and ignored: every caller passes
            // a literal, and threading a dead flag through eight call sites to delete it
            // would touch more of this fixture than the rules under test.
            _ = observationPost;
            _ = candidateOnCorner2;

            var kinds = new[]
            {
                cornerKind | MapNodeKind.ReachProbe,
                cornerKind | MapNodeKind.ReachProbe,
                cornerKind | MapNodeKind.ReachProbe,
                cornerKind,
            };

            var names = new[] { " 도달1", " 도달2", " 도달3", " 모퉁이" };
            var corners = new int[4];
            for (var i = 0; i < 4; i++)
            {
                corners[i] = map.AddNode(zone, positions[i], kinds[i], zoneName + names[i]);
            }

            for (var i = 0; i < 4; i++)
            {
                if (i == 3 && !closeRing)
                {
                    continue;
                }

                var from = corners[i];
                var to = corners[(i + 1) % 4];
                var carriesDoor = i == 0;

                if (halveTheLegs)
                {
                    var far = positions[(i + 1) % 4];
                    var mid = map.AddNode(
                        zone,
                        new Vec3((positions[i].X + far.X) * 0.5f, 0f, (positions[i].Z + far.Z) * 0.5f),
                        MapNodeKind.None,
                        zoneName + " 중간");
                    map.Connect(from, mid, carriesDoor, carriesDoor ? zoneName + " 잠금문" : null);
                    map.Connect(mid, to);
                }
                else
                {
                    map.Connect(from, to, carriesDoor, carriesDoor ? zoneName + " 잠금문" : null);
                }
            }

            if (diagonalShortcut)
            {
                map.Connect(corners[0], corners[2], false, zoneName + " 직선 지름길");
            }

            return new Block(zone, corners, positions);
        }

        /// <summary>Hangs a side passage off a ring corner, 45° from both of its legs.</summary>
        private static int Attach(
            MapGraphBuilder map,
            Block block,
            int cornerIndex,
            float dx,
            float dz,
            MapNodeKind kind,
            string name,
            int reward)
        {
            var at = block.CornerPositions[cornerIndex];
            var id = map.AddNode(block.Zone, new Vec3(at.X + dx, 0f, at.Z + dz), kind, name, reward);
            map.Connect(block.Corners[cornerIndex], id);
            return id;
        }

        private static MapGraphBuilder OneZoneBuilder(string name)
        {
            var builder = new MapGraphBuilder().Named(name);
            builder.AddZone(
                "단일 구역", FloorMaterial.Wood,
                new Vec3(GameConstants.MapExtent * 0.5f, 0f, GameConstants.MapExtent * 0.5f),
                new Vec3(GameConstants.MapExtent, 0f, GameConstants.MapExtent));
            return builder;
        }

        /// <summary>A corridor bent once, with the corner <paramref name="firstLeg"/> metres from the start.</summary>
        private static MapGraph SingleCorner(float firstLeg)
        {
            var builder = OneZoneBuilder("단일 모퉁이");

            // The route runs to the edge of what the test will search — §12 frames the
            // release inside one sprint's travel — so that the corner is what decides
            // the attempt rather than the corridor running out.
            var secondLeg = GameConstants.SprintMaxTravelDistance - firstLeg - JustInside;
            var start = builder.AddNode(0, new Vec3(1f, 0f, 1f), MapNodeKind.None, "출발");
            var corner = builder.AddNode(0, new Vec3(1f, 0f, 1f + firstLeg), MapNodeKind.None, "모퉁이");
            var beyond = builder.AddNode(0, new Vec3(1f + secondLeg, 0f, 1f + firstLeg), MapNodeKind.None, "너머");
            builder.Connect(start, corner);
            builder.Connect(corner, beyond);
            return builder.Build();
        }

        /// <summary>A corridor that turns 90° every <paramref name="leg"/> metres.</summary>
        private static MapGraph Serpentine(int nodeCount, float leg, bool asOneSCorridor) =>
            Serpentine(nodeCount, leg, asOneSCorridor, false);

        private static MapGraph Serpentine(int nodeCount, float leg, bool asOneSCorridor, bool insideAHall)
        {
            var builder = OneZoneBuilder(asOneSCorridor ? "S자 통로" : "지그재그");
            var kind = insideAHall ? MapNodeKind.OpenSpace : MapNodeKind.MazeSpace;
            var x = 1f;
            var z = 1f;
            var previous = -1;

            for (var i = 0; i < nodeCount; i++)
            {
                var id = builder.AddNode(0, new Vec3(x, 0f, z), kind, null, DeadEndReward);
                if (previous >= 0)
                {
                    builder.Connect(previous, id);
                }

                previous = id;
                if (i % 2 == 0)
                {
                    z += leg;
                }
                else
                {
                    x += leg;
                }
            }

            return builder.Build();
        }

        /// <summary>One straight run, far past §12's limit and with nothing to round.</summary>
        private static MapGraph StraightCorridor()
        {
            var builder = OneZoneBuilder("긴 직선 통로");
            var span = GameConstants.MaxStraightCorridor * 0.75f;
            var previous = -1;
            for (var i = 0; i < 5; i++)
            {
                var id = builder.AddNode(0, new Vec3(1f + (i * span), 0f, 1f), MapNodeKind.None, "지점 " + i);
                if (previous >= 0)
                {
                    builder.Connect(previous, id);
                }

                previous = id;
            }

            return builder.Build();
        }

        /// <summary>
        /// Two places joined directly and by a way round that is
        /// <paramref name="detourGain"/> metres longer. Edge 0 is the direct passage.
        /// </summary>
        private static MapGraph DetourPair(float detourGain)
        {
            var builder = OneZoneBuilder("병목 후보");
            var span = GameConstants.SCorridorLegLength;
            var depth = detourGain * 0.5f;

            var left = builder.AddNode(0, new Vec3(1f, 0f, 1f), MapNodeKind.None, "이쪽");
            var right = builder.AddNode(0, new Vec3(1f + span, 0f, 1f), MapNodeKind.None, "저쪽");
            var overLeft = builder.AddNode(0, new Vec3(1f, 0f, 1f + depth), MapNodeKind.None, "돌아가는 길 1");
            var overRight = builder.AddNode(0, new Vec3(1f + span, 0f, 1f + depth), MapNodeKind.None, "돌아가는 길 2");

            builder.Connect(left, right, true, "문");
            builder.Connect(left, overLeft);
            builder.Connect(overLeft, overRight);
            builder.Connect(overRight, right);
            return builder.Build();
        }

        /// <summary>A ring of <paramref name="nodes"/> places, optionally with one chord across it.</summary>
        private static MapGraph Ring(int nodes) => Ring(nodes, false);

        private static MapGraph Ring(int nodes, bool withChord)
        {
            var builder = OneZoneBuilder(withChord ? "순환로 + 지름길" : "순환로");
            var radius = GameConstants.ZoneDiagonalMin * 0.5f;
            var centre = MapCentre;

            for (var i = 0; i < nodes; i++)
            {
                var angle = i * 2f * MathF.PI / nodes;
                builder.AddNode(
                    0,
                    new Vec3(centre + (radius * MathF.Cos(angle)), 0f, centre + (radius * MathF.Sin(angle))),
                    MapNodeKind.MazeSpace,
                    "고리 " + i);
            }

            for (var i = 0; i < nodes; i++)
            {
                builder.Connect(i, (i + 1) % nodes);
            }

            if (withChord)
            {
                builder.Connect(0, nodes / 2);
            }

            return builder.Build();
        }

        /// <summary>Two rings sharing one place: three closed walks, two independent loops.</summary>
        private static MapGraph FigureOfEight()
        {
            var builder = OneZoneBuilder("8자");
            var leg = GameConstants.SCorridorLegLength;
            var centre = MapCentre;
            var shared = builder.AddNode(0, new Vec3(centre, 0f, centre), MapNodeKind.MazeSpace, "매듭");

            for (var loop = 0; loop < 2; loop++)
            {
                var direction = loop == 0 ? 1f : -1f;
                var first = builder.AddNode(
                    0, new Vec3(centre + (leg * direction), 0f, centre), MapNodeKind.MazeSpace, null);
                var second = builder.AddNode(
                    0,
                    new Vec3(centre + (leg * direction), 0f, centre + (leg * direction)),
                    MapNodeKind.MazeSpace,
                    null);
                builder.Connect(shared, first);
                builder.Connect(first, second);
                builder.Connect(second, shared);
            }

            return builder.Build();
        }

        private static float MapCentre => GameConstants.MapExtent * 0.5f;

        /// <summary>A chain with a branch: connected, no rings anywhere. §12's 사형선고.</summary>
        private static MapGraph Tree()
        {
            var builder = OneZoneBuilder("트리 구조");
            var leg = GameConstants.SCorridorLegLength;
            var root = builder.AddNode(0, new Vec3(1f, 0f, 1f), MapNodeKind.MazeSpace, "뿌리");
            var trunk = builder.AddNode(0, new Vec3(1f + leg, 0f, 1f), MapNodeKind.MazeSpace, "줄기");
            var left = builder.AddNode(0, new Vec3(1f + leg, 0f, 1f + leg), MapNodeKind.MazeSpace, "왼쪽 가지");
            var right = builder.AddNode(0, new Vec3(1f + (leg * 2f), 0f, 1f), MapNodeKind.MazeSpace, "오른쪽 가지");
            var tip = builder.AddNode(0, new Vec3(1f + (leg * 2f), 0f, 1f + leg), MapNodeKind.MazeSpace, "끝");

            builder.Connect(root, trunk);
            builder.Connect(trunk, left);
            builder.Connect(trunk, right);
            builder.Connect(right, tip);
            return builder.Build();
        }

        /// <summary>Two trees that cannot reach each other — a map in the state a half-built level is in.</summary>
        private static MapGraph Forest()
        {
            var builder = OneZoneBuilder("두 조각");
            var leg = GameConstants.SCorridorLegLength;

            for (var piece = 0; piece < 2; piece++)
            {
                var offset = piece * GameConstants.ZoneDiagonalMax;
                var first = builder.AddNode(0, new Vec3(1f + offset, 0f, 1f), MapNodeKind.MazeSpace, null);
                var second = builder.AddNode(0, new Vec3(1f + offset, 0f, 1f + leg), MapNodeKind.MazeSpace, null);
                var third = builder.AddNode(
                    0, new Vec3(1f + offset + leg, 0f, 1f + leg), MapNodeKind.MazeSpace, null);
                builder.Connect(first, second);
                builder.Connect(second, third);
            }

            return builder.Build();
        }

        /// <summary>A ring split across two zones: a map-wide 순환로 that belongs to neither.</summary>
        private static MapGraph TwoZoneRing()
        {
            var builder = new MapGraphBuilder().Named("구역을 넘나드는 순환로");
            var side = ZoneSide;
            builder.AddZone("A", FloorMaterial.Wood, new Vec3(side * 0.5f, 0f, side * 0.5f),
                new Vec3(side, 0f, side));
            builder.AddZone("B", FloorMaterial.Tile, new Vec3(side * 1.5f, 0f, side * 0.5f),
                new Vec3(side, 0f, side));

            var leg = GameConstants.SCorridorLegLength;
            var a0 = builder.AddNode(0, new Vec3(leg, 0f, leg), MapNodeKind.MazeSpace, "A 아래");
            var a1 = builder.AddNode(0, new Vec3(leg, 0f, leg * 2f), MapNodeKind.MazeSpace, "A 위");
            var b0 = builder.AddNode(1, new Vec3(side + leg, 0f, leg), MapNodeKind.MazeSpace, "B 아래");
            var b1 = builder.AddNode(1, new Vec3(side + leg, 0f, leg * 2f), MapNodeKind.MazeSpace, "B 위");

            builder.Connect(a0, a1);
            builder.Connect(a1, b1);
            builder.Connect(b1, b0);
            builder.Connect(b0, a0);
            return builder.Build();
        }

        /// <summary>The same two zones with one of the two doorways bricked up.</summary>
        private static MapGraph TwoZonesJoinedOnce()
        {
            var builder = new MapGraphBuilder().Named("다리 하나로 이어진 두 구역");
            var side = ZoneSide;
            builder.AddZone("A", FloorMaterial.Wood, new Vec3(side * 0.5f, 0f, side * 0.5f),
                new Vec3(side, 0f, side));
            builder.AddZone("B", FloorMaterial.Tile, new Vec3(side * 1.5f, 0f, side * 0.5f),
                new Vec3(side, 0f, side));

            var leg = GameConstants.SCorridorLegLength;
            var a0 = builder.AddNode(0, new Vec3(leg, 0f, leg), MapNodeKind.MazeSpace, "A 아래");
            var a1 = builder.AddNode(0, new Vec3(leg, 0f, leg * 2f), MapNodeKind.MazeSpace, "A 위");
            var b0 = builder.AddNode(1, new Vec3(side + leg, 0f, leg), MapNodeKind.MazeSpace, "B 아래");
            var b1 = builder.AddNode(1, new Vec3(side + leg, 0f, leg * 2f), MapNodeKind.MazeSpace, "B 위");

            builder.Connect(a0, a1);
            builder.Connect(a1, b1);
            builder.Connect(b1, b0);
            return builder.Build();
        }

        // ====================================================================
        // Helpers.
        // ====================================================================

        private static int[] EveryNode(MapGraph map) => Enumerable.Range(0, map.Nodes.Length).ToArray();

        private static int[] EveryEdge(MapGraph map) => Enumerable.Range(0, map.Edges.Length).ToArray();

        private static int NodeNamed(MapGraph map, string name)
        {
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                if (string.Equals(map.Nodes[i].Name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            throw new InvalidOperationException("The fixture has no place named '" + name + "'.");
        }

        private static int EdgeBetween(MapGraph map, int from, int to)
        {
            foreach (var edgeId in map.IncidentEdges(from))
            {
                if (map.Edges[edgeId].Other(from) == to)
                {
                    return edgeId;
                }
            }

            throw new InvalidOperationException("No passage joins " + from + " and " + to + ".");
        }

        private static bool AreJoined(MapGraph map, int from, int to) =>
            map.IncidentEdges(from).Any(e => map.Edges[e].Other(from) == to);

        /// <summary>
        /// Asserts that a map broke exactly one §12 rule, and that the failure says
        /// which. A checklist item that cannot be broken on its own is not being tested
        /// — some other rule is.
        /// </summary>
        private static MapValidationResult AssertOnlyFailure(
            MapValidationReport report, string ruleId, string why)
        {
            Assert.That(OtherFailures(report, ruleId), Is.Empty,
                why + "\nExactly one rule was supposed to fail. Full report:\n" + report.Describe());

            var result = report[ruleId];
            Assert.That(result.Passed, Is.False);
            Assert.That(result.IsChecklistItem, Is.True,
                "This is one of §12's eleven 검증 체크리스트 items.");
            Assert.That(result.Describe(), Does.StartWith("[FAIL] " + ruleId),
                "A failure has to name the rule it broke, or the report sends the designer hunting.");
            Assert.That(result.Detail, Is.Not.Empty,
                "\"This map is invalid\" is not actionable; §12's checklist is only worth automating "
                + "if the failure says what was measured and what it costs in play.");
            return result;
        }

        /// <summary>
        /// Every rule the compact sketch broke apart from the one under test — and
        /// apart from <see cref="MapValidator.RuleSightBreakSpacing"/>, which it breaks
        /// unconditionally.
        /// <para>
        /// §12's 첫 맵 스케치 packs four zones edge to edge, and §12 caps a zone at a
        /// 40 m diagonal and a straight corridor at 20 m. So the bends on either side
        /// of every zone boundary stand <c>ZoneSide − RingSide</c> apart — a handful of
        /// metres, which is wider than one 시야 차단 지점 may be and far short of the
        /// gap to the next one. No arrangement of this layout can satisfy the rule:
        /// it is cover-saturated by construction, which is the same fact
        /// <see cref="SketchMap_PassesTheChecklistAndStillGradesTooEasy"/> records from
        /// the other side, and
        /// <see cref="SightBreakSpacing_TheCompactSketchFailsItAndTheLongHouseDoesNot"/>
        /// pins deliberately rather than leaving it as a surprise here.
        /// </para>
        /// <para>
        /// Filtering it out is therefore not a tolerance: the eleven 검증 체크리스트
        /// items each still have to fail alone, which is what these tests are for.
        /// </para>
        /// </summary>
        private static string[] OtherFailures(MapValidationReport report, string ruleId) =>
            report.FailedRuleIds
                .Where(id => id != ruleId && id != MapValidator.RuleSightBreakSpacing)
                .ToArray();
    }
}
