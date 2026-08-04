using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HorrorGame.Core.Math;

namespace HorrorGame.Core.Map
{
    /// <summary>
    /// One rule from §12, checked, with the reason it exists attached.
    /// <para>
    /// <see cref="Detail"/> is written for the person who broke the rule, not for a
    /// log: it names the place that failed and says what will happen in play. §12's
    /// checklist is only worth automating if a failure is actionable — "this map is
    /// invalid" sends a designer hunting, "corridor #12→#13 is 26 m, and a Runner
    /// gains 0.8 m/s, so aggro cannot be broken in it" does not.
    /// </para>
    /// </summary>
    public readonly struct MapValidationResult
    {
        /// <summary>Builds a result. Produced by <see cref="MapValidator"/>.</summary>
        /// <param name="ruleId">Stable lookup key — see the constants on <see cref="MapValidator"/>.</param>
        /// <param name="rule">§12's own wording of the rule, so the report reads as the document.</param>
        /// <param name="passed">Whether the map satisfies it.</param>
        /// <param name="detail">What was measured, which place failed, and why it matters.</param>
        /// <param name="isChecklistItem">True for the eight items left on §12's 검증 체크리스트.</param>
        public MapValidationResult(string ruleId, string rule, bool passed, string detail, bool isChecklistItem)
        {
            RuleId = ruleId;
            Rule = rule;
            Passed = passed;
            Detail = detail;
            IsChecklistItem = isChecklistItem;
        }

        /// <summary>Stable key for looking this result up in a <see cref="MapValidationReport"/>.</summary>
        public string RuleId { get; }

        /// <summary>§12's wording of the rule.</summary>
        public string Rule { get; }

        /// <summary>True when the map satisfies the rule.</summary>
        public bool Passed { get; }

        /// <summary>
        /// The measurement and its consequence. Populated on a pass too, so a report
        /// on a good map still shows how much headroom it has.
        /// </summary>
        public string Detail { get; }

        /// <summary>
        /// True for the items §12 lists under 검증 체크리스트 — eight now; it was eleven
        /// before the co-op deletions took 관측 지점, 단서·목표물 후보 지점 and 은폐 지점.
        /// The rest — zone count, map extent, connectivity, zone membership, 시야 차단 지점
        /// 간격, §12-D's centre-path — come from §12's 수치 규칙 tables instead, and are
        /// separated so that "the checklist passes" remains a statement about the checklist.
        /// <para>
        /// It briefly read seven: the §12 re-derivation deleted 개방 공간–미로 공간 인접
        /// outright. The deletion was wider than its own argument — the rule has two
        /// clauses and only the second rested on §04, so the first came back. See
        /// <see cref="MapValidator.RuleOpenAdjacentToMaze"/>.
        /// </para>
        /// </summary>
        public bool IsChecklistItem { get; }

        /// <summary>One line, in the form a designer wants to read it.</summary>
        public string Describe() =>
            (Passed ? "[ok]   " : "[FAIL] ") + RuleId + " — " + Rule
            + (string.IsNullOrEmpty(Detail) ? string.Empty : "\n           " + Detail);

        /// <inheritdoc />
        public override string ToString() => Describe();
    }

    /// <summary>
    /// The outcome of validating a map against §12.
    /// <para>
    /// Every rule is reported, pass or fail, because §12's checklist is a list of
    /// things a designer forgets rather than a single yes/no — a report that only
    /// showed the first failure would hide the other ten.
    /// </para>
    /// </summary>
    public sealed class MapValidationReport
    {
        private readonly MapValidationResult[] _results;

        /// <summary>Wraps a set of results. Produced by <see cref="MapValidator.Validate"/>.</summary>
        /// <param name="mapName">Label for the report header.</param>
        /// <param name="results">One entry per rule checked, in §12's order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is null.</exception>
        public MapValidationReport(string mapName, MapValidationResult[] results)
        {
            MapName = mapName;
            _results = results ?? throw new ArgumentNullException(nameof(results));
        }

        /// <summary>Name of the map this report is about, for reports covering several.</summary>
        public string MapName { get; }

        /// <summary>Every rule that was checked, in §12's order.</summary>
        public MapValidationResult[] Results => _results;

        /// <summary>True when no rule failed.</summary>
        public bool Passed
        {
            get
            {
                for (var i = 0; i < _results.Length; i++)
                {
                    if (!_results[i].Passed)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>True when every 검증 체크리스트 item passes, whatever the 수치 규칙 rules did.</summary>
        public bool ChecklistPassed
        {
            get
            {
                for (var i = 0; i < _results.Length; i++)
                {
                    if (_results[i].IsChecklistItem && !_results[i].Passed)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Only the failures, in §12's order.</summary>
        public MapValidationResult[] Failures
        {
            get
            {
                var failed = new List<MapValidationResult>();
                for (var i = 0; i < _results.Length; i++)
                {
                    if (!_results[i].Passed)
                    {
                        failed.Add(_results[i]);
                    }
                }

                return failed.ToArray();
            }
        }

        /// <summary>Ids of the rules that failed — the compact form for a test assertion.</summary>
        public string[] FailedRuleIds
        {
            get
            {
                var failures = Failures;
                var ids = new string[failures.Length];
                for (var i = 0; i < failures.Length; i++)
                {
                    ids[i] = failures[i].RuleId;
                }

                return ids;
            }
        }

        /// <summary>Looks up one rule's result.</summary>
        /// <exception cref="KeyNotFoundException">No such rule was checked.</exception>
        public MapValidationResult this[string ruleId]
        {
            get
            {
                for (var i = 0; i < _results.Length; i++)
                {
                    if (string.Equals(_results[i].RuleId, ruleId, StringComparison.Ordinal))
                    {
                        return _results[i];
                    }
                }

                throw new KeyNotFoundException(
                    "No §12 rule with id '" + ruleId + "' was checked. Ids are the constants on MapValidator.");
            }
        }

        /// <summary>The whole report as text, ready to paste into a bug or print from the editor.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("§12 map validation — ").Append(MapName).Append(": ")
                .Append(Passed ? "PASS" : "FAIL").Append('\n');
            for (var i = 0; i < _results.Length; i++)
            {
                text.Append(_results[i].Describe()).Append('\n');
            }

            return text.ToString();
        }

        /// <inheritdoc />
        public override string ToString() => Describe();
    }

    /// <summary>
    /// §12's 검증 체크리스트 as code.
    /// <para>
    /// §12 opens with "맵은 아트가 아니라 시스템이다" and closes with a checklist. This type
    /// is the join between those two sentences: a map that
    /// breaks one of the items fails here, in a unit test, instead of failing in a
    /// playtest where the symptom is "the chase feels bad" and the cause is a 26 m
    /// straight corridor nobody measured.
    /// </para>
    /// <para>
    /// Every threshold is read from <see cref="GameConstants"/>, and almost every one
    /// of those is §06 arithmetic — 3 s of cover at 4.8 m/s, a 0.8 m/s sprint margin.
    /// So a §06 retune moves what counts as a legal map automatically, which is
    /// exactly the coupling §12 claims: "§06의 수치가 맵의 성립 조건을 규정한다."
    /// </para>
    /// </summary>
    public static class MapValidator
    {
        // ====================================================================
        // §12 검증 체크리스트 — the items that are left, in the document's order.
        // ====================================================================

        /// <summary>"20m 넘는 직선 통로가 없다."</summary>
        public const string RuleStraightCorridor = "straight-corridor";

        /// <summary>
        /// "개방 공간이 최소 1개 있고, 미로 공간과 인접해 있다" — the half of it a race still
        /// has a subject for.
        /// <para>
        /// <b>Restored after being deleted whole, because the deletion was wider than its
        /// own argument.</b> The rule says two things: that an 개방 공간 EXISTS and TOUCHES
        /// a 미로 공간, and (as this file used to read it) that the room be at least
        /// <c>LineOfSightBreakSpacingMin</c> across. Only the second rests on §04. The
        /// first is a statement about the building — that the one room on a storey wide
        /// enough to be crossed in the open is joined to the cover, on the same floor,
        /// through a doorway rather than a fall — and the shipped map SATISFIES it. It was
        /// thrown away along with a clause the map does not satisfy, which is a gate given
        /// up for nothing.
        /// </para>
        /// <para>
        /// The 15 m clause stays deleted, with the argument the deletion made for it:
        /// 개방 공간 「멀리서 어그로를 건다 · 시야 15~25m 확보」 is one player's opening move,
        /// and the player is §04's 주자 — the only one who could out-run the creature and
        /// the only one with a reason to be seen on purpose. §04 is deleted; 질주 5.6
        /// belongs to all twenty; and §01 says what acquisition looks like in a race
        /// instead: 「안쪽 고리는 좁다. 마주치면 피할 수 없다」, which is §12's own 3 m ❌ row.
        /// Nobody picks a range, so a room sized for picking one is a requirement with no
        /// subject. The map's one 개방 공간 per storey is the 중심 chamber, and what §12-C
        /// asks of THAT room is not 15 m of sight but that the 투하구 choice be visible —
        /// 「무작위가 아니다, 맵을 아는 사람이 유리해야」. Its width is measured and printed
        /// below so the deletion did not delete the number.
        /// </para>
        /// <para>
        /// GameConstants.HallClearSightMin is untouched by any of this: it stands on its
        /// own §06 derivation (a hall must be crossable in sight of the creature from
        /// further than AggroReleaseDistance) and Editor/Dressing still enforces it.
        /// </para>
        /// </summary>
        public const string RuleOpenAdjacentToMaze = "open-adjacent-to-maze";

        /// <summary>"S자 통로(10m×2)가 구역당 최소 1개 있다."</summary>
        public const string RuleSCorridorPerZone = "s-corridor-per-zone";

        /// <summary>"순환로가 맵 전체에 3개 이상 있다." (§12's 수치 규칙 adds 구역당 1+.)</summary>
        public const string RuleLoops = "loops";

        /// <summary>"막힌 길 비율이 20~25%."</summary>
        public const string RuleDeadEnds = "dead-ends";

        /// <summary>"구역별 바닥 재질이 다르고 경계가 명확하다."</summary>
        public const string RuleFloorMaterials = "floor-materials";

        /// <summary>"잠글 수 있는 문이 구역당 1~2개, 병목에 있다."</summary>
        public const string RuleLockableDoors = "lockable-doors";

        /// <summary>"구역 간 진입점이 2~3개로 제한돼 있다."</summary>
        public const string RuleZoneEntryPoints = "zone-entry-points";

        // DELETED with §04's roles, §03's clue chain and the light economy — three §12
        // checklist rules that were requirements OF systems that no longer exist:
        //
        //   RuleObservationPosts     "관측 지점이 구역당 1개 이상" — §12 관측자's window.
        //   RuleCandidateSites       "단서·목표물 후보가 구역당 3개, 모두 탈출로 2개 이상",
        //                            which also carried the 전기 패널 requirement, so the
        //                            두 went together.
        //   RuleConcealmentNearExit  "출입구 근처에 은폐 지점" for §07 새벽's ambush. It was
        //                            ALREADY waived in MapSceneGenerator.KnownFailingRules
        //                            as obsolete — this deletes the waiver's subject.
        //
        // A rule that counts places for a role nobody plays is not a weakened gate; it is
        // a gate on a door that has been removed from the building.

        // ====================================================================
        // §12 수치 규칙 — the sizes the checklist assumes but does not restate.
        // ====================================================================

        /// <summary>"구역 개수 4~6."</summary>
        public const string RuleZoneCount = "zone-count";

        // DELETED in the §12 re-derivation: RuleZoneDiagonal, "구역 대각선 30~40m".
        //
        // The band sized a 구역 as a SUB-AREA of a floor. §12's own 근거 column for that
        // row is empty, and the two reasons the rest of §12 supplies are both about a
        // 구역 being smaller than a 층: 소리 → 바닥 재질 makes a zone the granularity at
        // which a footstep names a place, and 맵 한 층 asks that "주자가 구역 2~3개 관통
        // 가능" on one 60 m sprint.
        //
        // On 하강 a 구역 IS a 층 — eight zones, eight storeys, one surface each — and both
        // reasons invert. §01 makes the footstep name a STOREY on purpose ("여덟 개의 표면,
        // 층마다 하나"), so 3~4 zones per floor would need 3~4 surfaces per floor and would
        // break the audio map rather than serve it; and crossing "2~3 zones" on one sprint
        // would mean crossing two or three FLOORS, which §01 forbids structurally, because
        // a floor is only ever left through the 투하구 in its middle. DescentMap.Radius 11
        // then makes every storey 57.5 m square and 81.3 m across, which is not a map that
        // misses the band by a little — it is a map the band is not about.
        //
        // What still needs bounding is how far a runner must travel on a floor, and §12-D
        // already states it: centre-path, 외곽에서 중심까지 최단 90~140 m
        // (GameConstants.CentrePathMetresMin/Max). That rule is now RuleCentrePath below,
        // it gates, and the shipped map FAILS it on every storey. That is a map defect with
        // an address, not a reason to keep a rule about a 구역 that no longer exists.
        // (This paragraph used to end "MapValidator does not gate that rule". It does now;
        // the sentence was true when it was written and would be a lie if left standing.)
        //
        // GameConstants.ZoneDiagonalMin/Max stay: six live systems read them as a reference
        // distance (replication range, search waypoints, shadow distance, fog) and not one
        // of them is making a §12 claim.

        /// <summary>"맵 전체 100 × 100m."</summary>
        public const string RuleMapExtent = "map-extent";

        /// <summary>Every place must be walkable from every other. A map in two pieces is two maps.</summary>
        public const string RuleConnectivity = "connectivity";

        /// <summary>Each node must physically lie in the zone it claims, or every per-zone count above is fiction.</summary>
        public const string RuleZoneMembership = "zone-membership";

        /// <summary>"시야 차단 지점 간격 15~25m" — 질주 60m에 3~4번의 기회.</summary>
        public const string RuleSightBreakSpacing = "sight-break-spacing";

        /// <summary>
        /// §12-D: "외곽에서 중심까지 최단 90~140 m", per storey.
        /// <para>
        /// <b>§12-D says this is checked here and it never was.</b> The section lists five
        /// rules — <c>ring-gates</c>, <c>centre-path</c>, <c>centre-single-gate</c>,
        /// <c>chute-count</c>, <c>chute-landing</c> — under the sentence "MapValidator와
        /// 씬 감사기가 층마다 확인한다", and this file implemented none of them. It is the one
        /// that carries a number, and it is the rule that inherited 구역 대각선's job when a
        /// 구역 became a 층: how far a runner must travel on one floor.
        /// </para>
        /// <para>
        /// It was measured before it was gated — <c>MapSceneGenerator</c> printed it on
        /// every generation and the map has never been inside the band. A measurement that
        /// gates nothing is how a defect goes quiet, so it gates, it fails, and
        /// <c>MapSceneGenerator.KnownFailingRules</c> carries the waiver with the numbers
        /// in it. See <see cref="GameConstants.CentrePathMetresMin"/> for what the band is
        /// derived from — §01's 「맵을 아는 것이 실력」 is what a short storey spends.
        /// </para>
        /// </summary>
        public const string RuleCentrePath = "centre-path";

        /// <summary>
        /// Runs every rule and returns the whole report.
        /// </summary>
        /// <param name="graph">The map. An empty one fails loudly rather than vacuously passing.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is null.</exception>
        public static MapValidationReport Validate(MapGraph graph)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            var results = new List<MapValidationResult>
            {
                CheckStraightCorridor(graph),
                CheckOpenAdjacentToMaze(graph),
                CheckSCorridorPerZone(graph),
                CheckLoops(graph),
                CheckDeadEnds(graph),
                CheckFloorMaterials(graph),
                CheckLockableDoors(graph),
                CheckZoneEntryPoints(graph),
                CheckZoneCount(graph),
                CheckMapExtent(graph),
                CheckConnectivity(graph),
                CheckZoneMembership(graph),
                CheckSightBreakSpacing(graph),
                CheckCentrePath(graph),
            };

            return new MapValidationReport(
                string.IsNullOrEmpty(graph.Name) ? "unnamed map" : graph.Name!,
                results.ToArray());
        }

        // ====================================================================
        // 1 — 20m 넘는 직선 통로가 없다.
        // ====================================================================

        private static MapValidationResult CheckStraightCorridor(MapGraph graph)
        {
            var longest = graph.LongestStraightRun(out var chain);
            var passed = longest <= GameConstants.MaxStraightCorridor + MathX.Epsilon;

            string detail;
            if (passed)
            {
                detail = "Longest unbroken sight line is " + Metres(longest) + ", inside §12's "
                         + Metres(GameConstants.MaxStraightCorridor) + " limit.";
            }
            else
            {
                var overshoot = longest - GameConstants.MaxStraightCorridor;
                detail = "A sight line runs " + Metres(longest) + " without a bend of "
                         + Degrees(GameConstants.MapSightBreakingBendDegrees) + ", "
                         + Metres(overshoot) + " over §12's limit, along " + Chain(graph, chain)
                         + ". §12: \"넘으면 주자가 죽는다\" — a Runner gains only "
                         + (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed)
                             .ToString("0.#", CultureInfo.InvariantCulture)
                         + " m/s, so it cannot reach the far end of this before the monster closes, and "
                         + "nothing in between breaks the "
                         + Seconds(GameConstants.AggroReleaseLineOfSightBreak) + " of cover a release needs.";
            }

            return new MapValidationResult(
                RuleStraightCorridor, "20m 넘는 직선 통로가 없다", passed, detail, true);
        }

        // ====================================================================
        // 2 — 개방 공간이 최소 1개 있고, 미로 공간과 인접해 있다.
        // ====================================================================

        /// <summary>
        /// §12's second checklist item, checked as the pair it is drawn as rather than
        /// as two flags that happen to share an edge.
        /// <para>
        /// <b>The arrow has to be a step, not a fall.</b> On the 하강 tower every storey
        /// stacks in one column and the only edges between them are 투하구, which a runner
        /// can enter and never leave (§01). Walking the raw graph therefore let this rule
        /// pass on a pair 37 m apart in plan and a whole storey down — reported, verbatim,
        /// as "opens directly into". Adjacency across a one-way drop is not adjacency, so
        /// an edge that changes storey (<see cref="MapGraph.StoreyChangeMetres"/>) cannot
        /// be the 진입 this rule is looking for.
        /// </para>
        /// <para>
        /// <b>The width of the room is measured and printed, and is not the verdict.</b>
        /// See <see cref="RuleOpenAdjacentToMaze"/> for why: 15~25 m is the range §04's
        /// 주자 chose to be seen from, and the chooser is deleted. The number still gets
        /// printed on every run, pass or fail, because §12-C wants that room read for a
        /// different reason — the 투하구 choice inside it must be visible — and a width
        /// nobody can see is a width nobody will notice going wrong.
        /// </para>
        /// </summary>
        private static MapValidationResult CheckOpenAdjacentToMaze(MapGraph graph)
        {
            var open = graph.NodesOfKind(MapNodeKind.OpenSpace);
            var maze = graph.NodesOfKind(MapNodeKind.MazeSpace);

            string detail;
            var passed = false;

            if (open.Length == 0)
            {
                detail = "No 개방 공간 anywhere. §12 draws the map as \"[개방 공간] ──진입── [미로 공간]\" "
                         + "and gives the left-hand box its own job — on this tower it is §12-C's 중심 "
                         + "chamber, the one room on a storey a runner crosses in the open and the room "
                         + "the two 투하구 stand in. A map with no such room has nowhere the 투하구 choice "
                         + "is a choice.";
            }
            else if (maze.Length == 0)
            {
                detail = "There are " + open.Length + " 개방 공간 and no 미로 공간. §12: \"개방 공간만 "
                         + "있으면 도망칠 곳이 없다\" — the creature is met and never broken from.";
            }
            else
            {
                var rooms = OpenSpaceRooms(graph);
                var best = -1;
                var bestSpan = -1f;
                var strandedSpan = -1f;
                int[]? acrossAChute = null;

                for (var r = 0; r < rooms.Count; r++)
                {
                    var room = rooms[r];
                    var touch = MazeTouching(graph, room, false);
                    if (touch == null)
                    {
                        acrossAChute ??= MazeTouching(graph, room, true);
                        var lonely = Span(graph, room, out _);
                        strandedSpan = lonely > strandedSpan ? lonely : strandedSpan;
                        continue;
                    }

                    var span = Span(graph, room, out _);
                    if (span > bestSpan)
                    {
                        bestSpan = span;
                        best = r;
                    }
                }

                if (best < 0)
                {
                    detail = "There are " + open.Length + " 개방 공간 and " + maze.Length
                             + " 미로 공간, but none of them touch. §12 draws the pair as "
                             + "\"[개방 공간] ──진입── [미로 공간]\": the open room is where a runner is met "
                             + "with nothing between them, and cover is what ends that. If the two are not "
                             + "adjacent, the walk between them is itself an unbroken run and the "
                             + "structure buys nothing.";

                    if (acrossAChute != null)
                    {
                        detail += " The nearest thing to a 진입 on this map is "
                                  + graph.Nodes[acrossAChute[0]].Describe() + " → "
                                  + graph.Nodes[acrossAChute[1]].Describe()
                                  + ", which is a 투하구: a one-way drop onto another storey, not a doorway. "
                                  + "A Runner who takes it does not reach cover, they leave the floor.";
                    }
                    else if (strandedSpan >= 0f)
                    {
                        detail += " The widest 개방 공간 measures " + Metres(strandedSpan) + " across.";
                    }
                }
                else
                {
                    var room = rooms[best];
                    var span = Span(graph, room, out var widest);
                    var touch = MazeTouching(graph, room, false)!;

                    passed = true;
                    detail = "개방 공간 " + graph.Nodes[touch[0]].Describe() + " opens directly into "
                             + "미로 공간 " + graph.Nodes[touch[1]].Describe() + " on the same storey, so "
                             + "§12's 진입 is a doorway and not a 투하구. The room behind it measures "
                             + Metres(span) + " across (" + graph.Nodes[widest[0]].Describe() + " to "
                             + graph.Nodes[widest[1]].Describe() + ") — measured, not gated: §12's "
                             + Metres(GameConstants.LineOfSightBreakSpacingMin) + "~"
                             + Metres(GameConstants.LineOfSightBreakSpacingMax)
                             + " 어그로 시작 거리 was §04's 주자 choosing a range to be seen from, and §01 "
                             + "replaced the choice with 「마주치면 피할 수 없다」.";
                }
            }

            return new MapValidationResult(
                RuleOpenAdjacentToMaze, "개방 공간이 최소 1개 있고, 미로 공간과 인접해 있다", passed, detail, true);
        }

        /// <summary>
        /// True when a passage joins two storeys — a 계단 or a 투하구.
        /// <para>
        /// The same test <c>MapGraph.LongestStraightRun</c> makes before it lets a sight
        /// line begin, for the same reason: an edge that spans a floor is not a corridor
        /// and the things §12 measures along corridors are not measurable along it. It is
        /// duplicated here rather than shared because <see cref="MapGraph"/> keeps its
        /// copy private; <see cref="MapGraph.StoreyChangeMetres"/> is public precisely so
        /// the two cannot answer differently.
        /// </para>
        /// </summary>
        private static bool JoinsStoreys(MapGraph graph, int edgeId)
        {
            var a = graph.Nodes[graph.Edges[edgeId].A].Position.Y;
            var b = graph.Nodes[graph.Edges[edgeId].B].Position.Y;
            return System.Math.Abs(a - b) > MapGraph.StoreyChangeMetres;
        }

        /// <summary>
        /// The map's 개방 공간 grouped into rooms: maximal sets of 개방 공간 nodes joined
        /// to each other by passages that stay on one storey.
        /// <para>
        /// One room rather than one node, because a width is a property of the VOLUME and
        /// no single node has a size. Storey-changing edges are cut first, so two halls
        /// stacked one above the other with a 투하구 between them are two rooms and not one
        /// 30 m one.
        /// </para>
        /// </summary>
        private static List<int[]> OpenSpaceRooms(MapGraph graph)
        {
            var rooms = new List<int[]>();
            var seen = new bool[graph.Nodes.Length];

            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                if (seen[i] || !graph.Nodes[i].Has(MapNodeKind.OpenSpace))
                {
                    continue;
                }

                var room = new List<int>();
                var stack = new Stack<int>();
                stack.Push(i);
                seen[i] = true;

                while (stack.Count > 0)
                {
                    var at = stack.Pop();
                    room.Add(at);

                    var incident = graph.IncidentEdges(at);
                    for (var k = 0; k < incident.Length; k++)
                    {
                        if (JoinsStoreys(graph, incident[k]))
                        {
                            continue;
                        }

                        var other = graph.Edges[incident[k]].Other(at);
                        if (!seen[other] && graph.Nodes[other].Has(MapNodeKind.OpenSpace))
                        {
                            seen[other] = true;
                            stack.Push(other);
                        }
                    }
                }

                rooms.Add(room.ToArray());
            }

            return rooms;
        }

        /// <summary>
        /// How far apart a room's two furthest places stand, in metres, measured
        /// straight rather than by walking.
        /// <para>
        /// Straight-line because that is what 개방 공간 MEANS: the declaration says this
        /// rectangle is one volume you can see across, which is exactly why
        /// <c>RunnerTest</c> refuses to count a bend inside one as cover. Measuring the
        /// walk instead would credit a room for the corridor topology under it, which is
        /// the topology the declaration exists to override.
        /// </para>
        /// </summary>
        private static float Span(MapGraph graph, int[] room, out int[] furthest)
        {
            var best = 0f;
            furthest = new[] { room[0], room[0] };

            for (var i = 0; i < room.Length; i++)
            {
                for (var k = i + 1; k < room.Length; k++)
                {
                    var gap = Vec3.DistanceFlat(
                        graph.Nodes[room[i]].Position, graph.Nodes[room[k]].Position);
                    if (gap > best)
                    {
                        best = gap;
                        furthest = new[] { room[i], room[k] };
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// The 개방 공간 · 미로 공간 pair where a room meets the maze, or null when it
        /// does not.
        /// </summary>
        /// <param name="graph">The map.</param>
        /// <param name="room">Node ids of one 개방 공간, from <see cref="OpenSpaceRooms"/>.</param>
        /// <param name="acrossStoreys">
        /// False for the rule itself — a 진입 is a doorway you walk through. True only to
        /// name the near miss in a failure message, so that a map joined to its maze by
        /// nothing but a 투하구 is told which edge it was hoping counted.
        /// </param>
        private static int[]? MazeTouching(MapGraph graph, int[] room, bool acrossStoreys)
        {
            for (var i = 0; i < room.Length; i++)
            {
                var incident = graph.IncidentEdges(room[i]);
                for (var k = 0; k < incident.Length; k++)
                {
                    if (JoinsStoreys(graph, incident[k]) != acrossStoreys)
                    {
                        continue;
                    }

                    var other = graph.Edges[incident[k]].Other(room[i]);
                    if (graph.Nodes[other].Has(MapNodeKind.MazeSpace))
                    {
                        return new[] { room[i], other };
                    }
                }
            }

            return null;
        }

        // ====================================================================
        // 3 — S자 통로(10m×2)가 구역당 최소 1개 있다.
        // ====================================================================

        private static MapValidationResult CheckSCorridorPerZone(MapGraph graph)
        {
            var missing = new List<string>();
            var found = new List<string>();

            for (var z = 0; z < graph.Zones.Length; z++)
            {
                var path = graph.FindSCorridor(z);
                if (path == null)
                {
                    missing.Add(graph.Zones[z].Name);
                }
                else
                {
                    found.Add(graph.Zones[z].Name + " " + Chain(graph, path));
                }
            }

            var passed = graph.Zones.Length > 0 && missing.Count == 0;
            var transit = GameConstants.SCorridorLegLength * 2f / GameConstants.MonsterBaseSpeed;

            string detail;
            if (graph.Zones.Length == 0)
            {
                detail = "The map has no zones, so there is nothing to hold an S자 통로.";
            }
            else if (passed)
            {
                detail = "Every zone has one: " + string.Join("; ", found) + ".";
            }
            else
            {
                detail = "No S자 통로 in zone(s) " + string.Join(", ", missing)
                         + ". §12 calls this the map's base unit — two legs of "
                         + Metres(GameConstants.SCorridorLegLength) + " with a bend of at least "
                         + Degrees(GameConstants.MapSightBreakingBendDegrees) + " at each end of the connector, "
                         + Metres(GameConstants.SCorridorLegLength * 2f) + " in total, which the monster needs "
                         + Seconds(transit) + " to clear against the "
                         + Seconds(GameConstants.AggroReleaseLineOfSightBreak)
                         + " a release requires. Without one, a Runner cornered in this zone has only single "
                         + "corners, and §12 proves a single corner needs "
                         + Metres(GameConstants.SingleCornerMinDistance) + " of head start to work at all.";
            }

            return new MapValidationResult(
                RuleSCorridorPerZone, "S자 통로(10m×2)가 구역당 최소 1개 있다", passed, detail, true);
        }

        // ====================================================================
        // 4 — 순환로가 맵 전체에 3개 이상 있다 (수치 규칙: 구역당 1+).
        // ====================================================================

        private static MapValidationResult CheckLoops(MapGraph graph)
        {
            var total = graph.IndependentLoopCount;
            var loopless = new List<string>();
            for (var z = 0; z < graph.Zones.Length; z++)
            {
                var inZone = graph.IndependentLoopCountInZone(z);
                if (inZone < GameConstants.LoopsPerZoneMin)
                {
                    loopless.Add(graph.Zones[z].Name + " (" + inZone + ")");
                }
            }

            var passed = total >= GameConstants.LoopsTotalMin && loopless.Count == 0;

            var detail = new StringBuilder();
            detail.Append("Independent 순환로: ").Append(total).Append(" map-wide (need ")
                .Append(GameConstants.LoopsTotalMin).Append("+).");

            if (total < GameConstants.LoopsTotalMin)
            {
                detail.Append(" §12: \"트리 구조는 사형선고\" — with no ring to run, the monster never has to "
                              + "guess which way the Runner went, so chasing reduces to a straight-line "
                              + "speed comparison the Runner loses by "
                              + (GameConstants.MonsterBaseSpeed - GameConstants.RunSpeed)
                                  .ToString("0.#", CultureInfo.InvariantCulture)
                              + " m/s in every role but 주자.");
            }

            if (loopless.Count > 0)
            {
                detail.Append(" Zone(s) below §12's 구역당 ").Append(GameConstants.LoopsPerZoneMin)
                    .Append("+: ").Append(string.Join(", ", loopless))
                    .Append(". A map can reach ").Append(GameConstants.LoopsTotalMin)
                    .Append(" loops with all of them in one hall, which leaves every other zone a tree — "
                            + "the per-zone rule exists so a Runner cornered inside a zone has somewhere to go.");
            }

            return new MapValidationResult(
                RuleLoops, "순환로가 맵 전체에 3개 이상 있다 (구역당 1개 이상)", passed, detail.ToString(), true);
        }

        // ====================================================================
        // 5 — 막힌 길 비율이 20~25%.
        //
        // The rule was "…이고, 각각 보상이 있다", and the 보상 half is DELETED with §08.
        // §12 wanted a 전리품 in every dead end as "위험을 감수할 이유" — a reason to have
        // risked walking in. A co-op looting game needed that because a wrong turn with
        // nothing at the end of it was pure loss. A race does not: in 선착순, the cost of a
        // 막힌 길 is the seconds it took, every other runner is spending theirs somewhere
        // else at the same moment, and knowing which turns are wrong IS the reward.
        // The ratio band survives untouched and is doing all the work it ever did.
        // ====================================================================

        private static MapValidationResult CheckDeadEnds(MapGraph graph)
        {
            if (graph.Nodes.Length == 0)
            {
                return new MapValidationResult(
                    RuleDeadEnds, "막힌 길 비율이 20~25%", false,
                    "The map has no places in it, so the 막힌 길 ratio is undefined. §12's band is a "
                    + "statement about a built map; an empty one cannot satisfy it.", true);
            }

            var deadEnds = new List<int>();
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                if (!graph.IsDeadEnd(i))
                {
                    continue;
                }

                deadEnds.Add(i);
            }

            var ratio = deadEnds.Count / (float)graph.Nodes.Length;
            var inBand = ratio >= GameConstants.DeadEndRatioMin - MathX.Epsilon
                         && ratio <= GameConstants.DeadEndRatioMax + MathX.Epsilon;
            var passed = inBand;

            var detail = new StringBuilder();
            detail.Append("막힌 길: ").Append(deadEnds.Count).Append(" of ").Append(graph.Nodes.Length)
                .Append(" places = ").Append(Percent(ratio)).Append(" (§12 band ")
                .Append(Percent(GameConstants.DeadEndRatioMin)).Append('~')
                .Append(Percent(GameConstants.DeadEndRatioMax)).Append(").");

            if (ratio < GameConstants.DeadEndRatioMin - MathX.Epsilon)
            {
                detail.Append(" Too few: §12 says \"적으면 맵 지식 무의미\" — if almost every turn leads "
                              + "somewhere, learning the building stops paying, and §06's \"맵을 알아야 "
                              + "최적화된다\" has nothing to reward.");
            }
            else if (ratio > GameConstants.DeadEndRatioMax + MathX.Epsilon)
            {
                detail.Append(" Too many: §12 says \"많으면 운에 죽음\" — a Runner breaking aggro picks a "
                              + "passage under pressure, and at this density the pick is a coin flip rather "
                              + "than a decision.");
            }

            return new MapValidationResult(
                RuleDeadEnds, "막힌 길 비율이 20~25%", passed, detail.ToString(), true);
        }

        // ====================================================================
        // 6 — 구역별 바닥 재질이 다르고 경계가 명확하다.
        // ====================================================================

        private static MapValidationResult CheckFloorMaterials(MapGraph graph)
        {
            var problems = new List<string>();
            var seen = new Dictionary<FloorMaterial, int>();

            for (var z = 0; z < graph.Zones.Length; z++)
            {
                var zone = graph.Zones[z];
                if (zone.Floor == FloorMaterial.None)
                {
                    problems.Add(zone.Name + " has no floor material assigned, which leaves the Listener a "
                                 + "silent zone (§04's 위치 판별 falls back to "
                                 + GameConstants.ListenerClarityUnknown.ToString("0.##", CultureInfo.InvariantCulture)
                                 + " clarity, worse than every real surface)");
                    continue;
                }

                if (seen.TryGetValue(zone.Floor, out var firstZone))
                {
                    problems.Add(zone.Name + " and " + graph.Zones[firstZone].Name + " are both "
                                 + zone.Floor + ", so a footstep in either sounds the same and the "
                                 + "Listener cannot tell which one the monster is in");
                }
                else
                {
                    seen[zone.Floor] = z;
                }
            }

            for (var a = 0; a < graph.Zones.Length; a++)
            {
                for (var b = a + 1; b < graph.Zones.Length; b++)
                {
                    if (graph.Zones[a].OverlapsVolume(graph.Zones[b]))
                    {
                        problems.Add(graph.Zones[a].Name + " and " + graph.Zones[b].Name
                                     + " overlap, so a footstep inside the overlap belongs to two floor "
                                     + "materials at once — §12 requires \"재질 경계를 명확히 할 것\"");
                    }
                }
            }

            var passed = graph.Zones.Length > 0 && problems.Count == 0;
            string detail;
            if (graph.Zones.Length == 0)
            {
                detail = "The map has no zones, so there are no floor materials to distinguish.";
            }
            else if (passed)
            {
                var listing = new List<string>();
                for (var z = 0; z < graph.Zones.Length; z++)
                {
                    listing.Add(graph.Zones[z].Name + "=" + graph.Zones[z].Floor);
                }

                detail = "Distinct and non-overlapping: " + string.Join(", ", listing) + ".";
            }
            else
            {
                detail = "§12: \"구역별로 바닥 재질이 달라야 청음사가 위치를 판별할 수 있다. 아트 결정이 아니라 "
                         + "시스템 결정이다.\" " + string.Join("; ", problems) + ".";
            }

            return new MapValidationResult(
                RuleFloorMaterials, "구역별 바닥 재질이 다르고 경계가 명확하다", passed, detail, true);
        }
        // ====================================================================
        // 8 — 잠글 수 있는 문이 구역당 1~2개, 병목에 있다.
        // ====================================================================

        private static MapValidationResult CheckLockableDoors(MapGraph graph)
        {
            var problems = new List<string>();
            var counts = new List<string>();

            for (var z = 0; z < graph.Zones.Length; z++)
            {
                var doors = graph.LockableDoorsInZone(z);
                counts.Add(graph.Zones[z].Name + "=" + doors.Length);

                if (doors.Length < GameConstants.LockableDoorsPerZoneMin)
                {
                    problems.Add("zone " + graph.Zones[z].Name + " has no lockable door, so the Engineer "
                                 + "— §04's 사전 준비형 role — has nothing to prepare in it");
                }
                else if (doors.Length > GameConstants.LockableDoorsPerZoneMax)
                {
                    problems.Add("zone " + graph.Zones[z].Name + " has " + doors.Length
                                 + " lockable doors, over §12's cap of " + GameConstants.LockableDoorsPerZoneMax
                                 + " — \"많으면 정비공이 만능이 된다\"");
                }
            }

            for (var e = 0; e < graph.Edges.Length; e++)
            {
                if (!graph.Edges[e].HasLockableDoor || graph.IsBottleneck(e))
                {
                    continue;
                }

                var detour = graph.DetourWithout(e);
                problems.Add("the door on " + graph.Edges[e]
                             + " is not at a 병목: with it shut the way round is only "
                             + Metres(detour) + " against the passage's own " + Metres(graph.Edges[e].Length)
                             + ", a gain of " + Metres(detour - graph.Edges[e].Length) + " where §12 needs at "
                             + "least " + Metres(GameConstants.SingleCornerMinDistance) + " ("
                             + Seconds(GameConstants.AggroReleaseLineOfSightBreak) + " at the monster's "
                             + GameConstants.MonsterBaseSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                             + " m/s) before locking it buys even one aggro release");
            }

            var passed = graph.Zones.Length > 0 && problems.Count == 0;
            string detail;
            if (graph.Zones.Length == 0)
            {
                detail = "The map has no zones, so the per-zone door budget is undefined.";
            }
            else if (passed)
            {
                detail = "Doors per zone: " + string.Join(", ", counts) + ", each at a 병목.";
            }
            else
            {
                detail = "§12 정비공: \"순환로의 목에 문 하나 → 잠그면 순환이 끊김.\" "
                         + string.Join("; ", problems) + ".";
            }

            return new MapValidationResult(
                RuleLockableDoors, "잠글 수 있는 문이 구역당 1~2개, 병목에 있다", passed, detail, true);
        }
        // ====================================================================
        // 10 — 구역 간 진입점이 2~3개로 제한돼 있다.
        // ====================================================================

        private static MapValidationResult CheckZoneEntryPoints(MapGraph graph)
        {
            var problems = new List<string>();
            var listing = new List<string>();

            for (var a = 0; a < graph.Zones.Length; a++)
            {
                for (var b = a + 1; b < graph.Zones.Length; b++)
                {
                    var crossings = graph.EdgesBetweenZones(a, b);
                    if (crossings.Length == 0)
                    {
                        continue;
                    }

                    listing.Add(graph.Zones[a].Name + "–" + graph.Zones[b].Name + "=" + crossings.Length);

                    if (crossings.Length < GameConstants.ZoneEntryPointsMin)
                    {
                        problems.Add(graph.Zones[a].Name + "–" + graph.Zones[b].Name + " is joined by only "
                                     + crossings.Length + " passage, under §12's "
                                     + GameConstants.ZoneEntryPointsMin
                                     + " — one way between two zones is a bridge, so the monster never has to "
                                     + "guess and a single locked door seals a whole zone off");
                    }
                    else if (crossings.Length > GameConstants.ZoneEntryPointsMax)
                    {
                        problems.Add(graph.Zones[a].Name + "–" + graph.Zones[b].Name + " is joined by "
                                     + crossings.Length + " passages, over §12's "
                                     + GameConstants.ZoneEntryPointsMax
                                     + " — §07 measures the monster's patrol in whole zones ("
                                     + GameConstants.ThreatPatrolZonesEarlyEvening + " zone in 초저녁), which "
                                     + "only means something if crossing out of one is a decision rather than "
                                     + "a direction");
                    }
                }
            }

            var passed = problems.Count == 0 && listing.Count > 0;
            string detail;
            if (listing.Count == 0)
            {
                detail = "No passage joins any two zones, so the map is a set of islands and §12's 진입점 "
                         + "rule has nothing to constrain.";
            }
            else if (passed)
            {
                detail = "진입점 per adjacent pair: " + string.Join(", ", listing) + ".";
            }
            else
            {
                detail = string.Join("; ", problems) + ".";
            }

            return new MapValidationResult(
                RuleZoneEntryPoints, "구역 간 진입점이 2~3개로 제한돼 있다", passed, detail, true);
        }

        // ====================================================================
        // §12 수치 규칙.
        // ====================================================================

        private static MapValidationResult CheckZoneCount(MapGraph graph)
        {
            var count = graph.Zones.Length;
            var passed = count >= GameConstants.ZoneCountMin && count <= GameConstants.ZoneCountMax;
            var detail = passed
                ? count + " zones, inside §12's " + GameConstants.ZoneCountMin + "~"
                  + GameConstants.ZoneCountMax + "."
                : count + " zones, outside §12's " + GameConstants.ZoneCountMin + "~"
                  + GameConstants.ZoneCountMax + ". The band used to be sized by §03's clue chain "
                  + "narrowing 층 → 구역 → 지점 over three candidate sites per zone; the chain is "
                  + "deleted, and on the descent tower a 구역 IS a storey, so the band now says how "
                  + "deep the building may be. It is still bounded from above by §07's patrol scope ("
                  + GameConstants.ThreatPatrolZonesEarlyEvening + " zone early, "
                  + GameConstants.ThreatPatrolZonesNight + " later): past that the creature stops "
                  + "covering enough of the building to threaten anybody on it.";

            // The label quotes the LIVE band, not §12's original 4~6. A rule whose name
            // and whose bound disagree is a report that reads as broken every time it
            // passes; §12's 수치 규칙 table now says 4~9 for the reason the detail gives.
            return new MapValidationResult(
                RuleZoneCount,
                "구역 개수 " + GameConstants.ZoneCountMin + "~" + GameConstants.ZoneCountMax
                + " (구역 = 층, 그래서 건물의 깊이)",
                passed, detail, false);
        }

        private static MapValidationResult CheckMapExtent(MapGraph graph)
        {
            graph.Footprint(out var min, out var max);
            var width = max.X - min.X;
            var depth = max.Z - min.Z;
            var passed = graph.Zones.Length > 0
                         && width <= GameConstants.MapExtent + MathX.Epsilon
                         && depth <= GameConstants.MapExtent + MathX.Epsilon;

            var detail = graph.Zones.Length == 0
                ? "The map has no zones, so it has no extent."
                : passed
                    ? "Footprint " + Metres(width) + " × " + Metres(depth) + ", inside §12's "
                      + Metres(GameConstants.MapExtent) + " square."
                    : "Footprint " + Metres(width) + " × " + Metres(depth) + ", over §12's "
                      + Metres(GameConstants.MapExtent) + " square. §12 sizes the map so \"주자가 구역 "
                      + "2~3개 관통 가능\" on one sprint of " + Metres(GameConstants.SprintMaxTravelDistance)
                      + " (§05).";
            // The second half of that sentence used to price a wider storey in
            // BatterySecondsPerCell — the §03 round trip's battery cost. Both went
            // with the co-op game. What still bounds the extent is the sprint: a
            // storey wider than 2~3 zones per sprint means the creature can never
            // be outrun to a gate, which is the whole of §05's answer to §06.

            return new MapValidationResult(
                RuleMapExtent,
                "맵 한 층 " + Metres(GameConstants.MapExtent) + " 정사각형 이내",
                passed, detail, false);
        }

        private static MapValidationResult CheckConnectivity(MapGraph graph)
        {
            var components = graph.ConnectedComponentCount;
            var passed = graph.Nodes.Length > 0 && components == 1;
            var detail = graph.Nodes.Length == 0
                ? "The map has no places in it."
                : passed
                    ? "One walkable piece, " + graph.Nodes.Length + " places, " + graph.Edges.Length
                      + " passages."
                    : "The map is in " + components + " unconnected pieces. Every §12 rule below the first "
                      + "assumes one building: a clue chain that lands in an unreachable piece (§03) makes "
                      + "the match unwinnable, and the 막힌 길 ratio counts every isolated place as a dead end.";

            return new MapValidationResult(RuleConnectivity, "맵이 하나로 연결돼 있다", passed, detail, false);
        }

        private static MapValidationResult CheckZoneMembership(MapGraph graph)
        {
            var problems = new List<string>();
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                var node = graph.Nodes[i];
                if (node.ZoneId < 0 || node.ZoneId >= graph.Zones.Length)
                {
                    problems.Add(node.Describe() + " claims zone " + node.ZoneId + ", which does not exist");
                    continue;
                }

                if (!graph.Zones[node.ZoneId].Contains(node.Position))
                {
                    problems.Add(node.Describe() + " claims " + graph.Zones[node.ZoneId].Name
                                 + " but lies outside its box");
                }
            }

            var passed = problems.Count == 0;
            var detail = passed
                ? "Every place lies inside the zone it claims."
                : string.Join("; ", problems)
                  + ". Every per-zone count in §12 — floor material, 관측 지점, 문, 후보 지점 — is counted by "
                  + "the declared zone, so a place in the wrong one makes all of them wrong, and the "
                  + "Listener hears a floor the place is not standing on.";

            return new MapValidationResult(RuleZoneMembership, "각 지점이 자기 구역 안에 있다", passed, detail, false);
        }

        // ====================================================================
        // 시야 차단 지점 간격 15~25m — "질주 60m에 3~4번의 기회."
        //
        // The one row of §12's 수치 규칙 that had no implementation, which is why a
        // map with a corner every 4 m passed sixteen rules out of sixteen and still
        // graded 10/10 너무 쉽다. §12's checklist is a list of things that must be
        // present; this is the one number that says how far apart they have to be,
        // and without it "present" and "free" are the same map.
        // ====================================================================

        /// <summary>
        /// Groups the map's bends into 시야 차단 지점 and measures the gaps between them.
        /// <para>
        /// <b>A 지점 is a chance, not a corner.</b> §12 counts opportunities — "질주
        /// 60m에 3~4번의 기회" — and its own 기본 단위 is an S자 통로, "10m 구간 2개",
        /// which is two bends and <em>one</em> chance. Reading 간격 as the distance
        /// between individual bends would therefore make §12 contradict itself: no map
        /// could satisfy both that row and the S자 통로 row above it. So bends closer
        /// together than <see cref="GameConstants.LineOfSightBreakSpacingMin"/> are one
        /// 지점 — a Runner rounding them rides one unbroken sight line across the lot —
        /// and the spacing rule applies between 지점.
        /// </para>
        /// <para>
        /// <b>Which makes the width of a 지점 the other half of the rule.</b> Grouping
        /// alone would pass the very map this exists to reject: sixty metres of bends
        /// every four metres is one enormous group with nothing to be spaced from. A
        /// 지점 may therefore be no wider than <see cref="SightBreakPointSpanCap"/>.
        /// </para>
        /// <para>
        /// <b>That cap was retired for one round and is back with a different number.</b>
        /// It used to be <c>GameConstants.SightBreakPointSpanMax</c> — 14.4 m less the 10 m
        /// head start §12's 어그로 시작 거리 table endorses, i.e. 4.4 m — and the subtracted
        /// term is §04's 주자 <em>choosing</em> a range to be seen from. §04 is deleted and
        /// §01 replaced the choice with 「마주치면 피할 수 없다」, so that term had to go.
        /// Removing the whole cap did not follow from removing the term: strip the term and
        /// the honest cap is <see cref="GameConstants.SingleCornerMinDistance"/> itself,
        /// and the map is still six times over it. A gate the geometry cannot meet is a MAP
        /// defect, not a wrong gate, and it is waived by name in
        /// <c>MapSceneGenerator.KnownFailingRules</c> with both numbers in the entry.
        /// </para>
        /// <para>
        /// What the width guards is also measured in the race's own currency, as 탈출 대가
        /// against <see cref="GameConstants.ChaseTollSecondsMin"/> (one door) and
        /// <see cref="GameConstants.ChaseTollSecondsMax"/> (one storey, which is what a
        /// catch now costs); <c>MapSceneGenerator</c> prints the toll beside this report.
        /// The toll is a consequence and the span is a cause, and they disagree on this
        /// map — <b>0 of 720</b> places charge less than one door while cover runs
        /// continuously for tens of metres — so the toll does not stand in for the span.
        /// A toll measured against a monster that catches one runner at a time cannot see
        /// what continuous cover costs a race of twenty.
        /// </para>
        /// </summary>
        /// <summary>
        /// Widest one 시야 차단 지점 may be, metres — the §04 term stripped out rather than
        /// the cap thrown away.
        /// <para>
        /// §12 needs <see cref="GameConstants.SingleCornerMinDistance"/> of gap to buy
        /// <see cref="GameConstants.AggroReleaseLineOfSightBreak"/> of broken sight from a
        /// single corner. <c>GameConstants.SightBreakPointSpanMax</c> then subtracted
        /// <c>RunnerTestAggroStartDistance</c> from it, on the argument that a 주자 who
        /// chose to be seen from 10 m out arrives at the corner already carrying 10 m of
        /// the 14.4. Nobody chooses in a race — §01: 「마주치면 피할 수 없다」 — so a runner
        /// arrives at cover carrying nothing, and the whole 14.4 m has to be bought by the
        /// cover itself.
        /// </para>
        /// <para>
        /// So this is <see cref="GameConstants.SingleCornerMinDistance"/> with nothing
        /// taken off, and it is the strictly WEAKER of the two caps: the deleted premise
        /// was making the rule harsher than a race can justify, not softer. Cover deeper
        /// than this completes §06's release from a standing start with no run-up at all,
        /// which is 「도망칠 수 있다」 handed out rather than earned — and in a race that is
        /// not the caught runner's problem but every other runner's, because §06 is the
        /// only thing on the map that costs a leader time.
        /// </para>
        /// <para>
        /// Read from <see cref="GameConstants"/> rather than restated, so a §06 retune of
        /// the corner requirement moves the map rule with it — §12: "§06의 수치가 맵의 성립
        /// 조건을 규정한다". <c>GameConstants.SightBreakPointSpanMax</c> is deliberately NOT
        /// read here: it still carries the subtraction, and MapTests uses it to size a
        /// fixture spur.
        /// </para>
        /// </summary>
        private static float SightBreakPointSpanCap => GameConstants.SingleCornerMinDistance;

        private static MapValidationResult CheckSightBreakSpacing(MapGraph graph)
        {
            const string rule = "시야 차단 지점 간격 15~25m (질주 60m에 3~4번의 기회)";

            var corners = new List<int>();
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                // A bend drawn inside 개방 공간 hides nobody — §12 gives those rooms
                // 15~25 m of sight on purpose, because they are where aggro is taken.
                // RunnerTest applies the same exclusion, so this counts the corners
                // that actually decide the 실전 검증 score.
                if (!graph.Nodes[i].Has(MapNodeKind.OpenSpace) && graph.IsSightBreakingCorner(i))
                {
                    corners.Add(i);
                }
            }

            if (corners.Count == 0)
            {
                return new MapValidationResult(
                    RuleSightBreakSpacing, rule, false,
                    "The map has no 시야 차단 지점 outside its 개방 공간 at all, so §06's "
                    + Seconds(GameConstants.AggroReleaseLineOfSightBreak)
                    + " of broken sight can never begin and every chase is the straight-line speed "
                    + "comparison a Runner wins by only "
                    + (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed)
                        .ToString("0.#", CultureInfo.InvariantCulture)
                    + " m/s.", false);
            }

            var separation = CornerSeparations(graph, corners);
            var pointOf = GroupIntoSightBreakPoints(corners.Count, separation);
            var pointCount = 0;
            for (var i = 0; i < pointOf.Length; i++)
            {
                if (pointOf[i] + 1 > pointCount)
                {
                    pointCount = pointOf[i] + 1;
                }
            }

            var widest = new float[pointCount];
            var widestPair = new int[pointCount][];
            var nearest = new float[pointCount];
            var nearestPair = new int[pointCount][];
            for (var p = 0; p < pointCount; p++)
            {
                nearest[p] = float.PositiveInfinity;
                widestPair[p] = new[] { -1, -1 };
                nearestPair[p] = new[] { -1, -1 };
            }

            for (var i = 0; i < corners.Count; i++)
            {
                for (var k = 0; k < corners.Count; k++)
                {
                    if (i == k)
                    {
                        continue;
                    }

                    var gap = separation[i][k];
                    if (pointOf[i] == pointOf[k])
                    {
                        if (gap > widest[pointOf[i]])
                        {
                            widest[pointOf[i]] = gap;
                            widestPair[pointOf[i]] = new[] { corners[i], corners[k] };
                        }
                    }
                    else if (gap < nearest[pointOf[i]])
                    {
                        nearest[pointOf[i]] = gap;
                        nearestPair[pointOf[i]] = new[] { corners[i], corners[k] };
                    }
                }
            }

            var problems = new List<string>();
            var tooWide = -1;
            var tooLonely = -1;
            for (var p = 0; p < pointCount; p++)
            {
                if (widest[p] > SightBreakPointSpanCap + MathX.Epsilon
                    && (tooWide < 0 || widest[p] > widest[tooWide]))
                {
                    tooWide = p;
                }

                if (nearest[p] > GameConstants.LineOfSightBreakSpacingMax + MathX.Epsilon
                    && (tooLonely < 0 || nearest[p] > nearest[tooLonely]))
                {
                    tooLonely = p;
                }
            }

            if (tooWide >= 0)
            {
                problems.Add(
                    "One 시야 차단 지점 is " + Metres(widest[tooWide]) + " deep — "
                    + graph.Nodes[widestPair[tooWide][0]].Describe() + " to "
                    + graph.Nodes[widestPair[tooWide][1]].Describe()
                    + " with nothing further than " + Metres(GameConstants.LineOfSightBreakSpacingMin)
                    + " between any two of its bends, so a Runner rounding them holds one unbroken sight "
                    + "line the whole way. The cap is " + Metres(SightBreakPointSpanCap) + " — §12's "
                    + Metres(GameConstants.SingleCornerMinDistance)
                    + " single-corner requirement with nothing subtracted, because §01's 「마주치면 피할 수 "
                    + "없다」 means a runner reaches cover carrying no head start at all. Cover this deep "
                    + "finishes §06's " + Seconds(GameConstants.AggroReleaseLineOfSightBreak)
                    + " from a standing start, so the release is not bought, it is lying on the floor — "
                    + "and §06 is the only thing on this map that costs a leading runner time");
            }

            if (pointCount < 2)
            {
                problems.Add(
                    "The whole map holds one 시야 차단 지점, so there is no 간격 to measure. §12 sizes the "
                    + "gap so a Runner meets 3~4 of them inside one sprint's "
                    + Metres(GameConstants.SprintMaxTravelDistance) + "; with one, a chase is decided by "
                    + "whether the Runner happened to start near it");
            }
            else if (tooLonely >= 0 && nearestPair[tooLonely][0] < 0)
            {
                // Unreachable rather than far: the map is in pieces, which the
                // connectivity rule states plainly. Saying "∞ metres away" here would
                // send a designer looking for a corridor to shorten.
                problems.Add(
                    "One 시야 차단 지점 has no other one it can be walked to at all, so §12's 간격 is "
                    + "not a distance on this map — see the connectivity rule");
            }
            else if (tooLonely >= 0)
            {
                problems.Add(
                    "The nearest other 시야 차단 지점 to "
                    + graph.Nodes[nearestPair[tooLonely][0]].Describe() + " is "
                    + Metres(nearest[tooLonely]) + " away, over §12's "
                    + Metres(GameConstants.LineOfSightBreakSpacingMax)
                    + ". Between the two there is no cover, and a Runner crossing that stretch gains only "
                    + (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed)
                        .ToString("0.#", CultureInfo.InvariantCulture)
                    + " m/s — the same arithmetic §12 uses to cap a straight corridor at "
                    + Metres(GameConstants.MaxStraightCorridor));
            }

            var passed = problems.Count == 0;
            string detail;
            if (passed)
            {
                var closest = float.PositiveInfinity;
                var furthest = 0f;
                var span = 0f;
                for (var p = 0; p < pointCount; p++)
                {
                    closest = nearest[p] < closest ? nearest[p] : closest;
                    furthest = nearest[p] > furthest ? nearest[p] : furthest;
                    span = widest[p] > span ? widest[p] : span;
                }

                // "질주 60m에 3~4번의 기회" read back out of the measurement rather than
                // asserted: at the widest legal gap a sprint meets 60 ÷ 25 = 2.4 지점, at
                // the tightest 60 ÷ 15 = 4, which is the band's own arithmetic.
                var chances = GameConstants.SprintMaxTravelDistance / furthest;

                detail = pointCount + " 시야 차단 지점 built from " + corners.Count
                         + " bend(s), the deepest " + Metres(span) + " (cap "
                         + Metres(SightBreakPointSpanCap) + "), nearest-neighbour spacing "
                         + Metres(closest) + "~" + Metres(furthest)
                         + " inside §12's " + Metres(GameConstants.LineOfSightBreakSpacingMin) + "~"
                         + Metres(GameConstants.LineOfSightBreakSpacingMax) + ", so one "
                         + Metres(GameConstants.SprintMaxTravelDistance) + " sprint meets "
                         + chances.ToString("0.#", CultureInfo.InvariantCulture)
                         + " of them at worst (§12: 3~4번의 기회).";
            }
            else
            {
                detail = pointCount + " 시야 차단 지점 from " + corners.Count + " bend(s). "
                         + string.Join("; ", problems) + ".";
            }

            return new MapValidationResult(RuleSightBreakSpacing, rule, passed, detail, false);
        }

        /// <summary>Walking distance between every pair of bends, in the order they were collected.</summary>
        private static float[][] CornerSeparations(MapGraph graph, List<int> corners)
        {
            var separation = new float[corners.Count][];
            for (var i = 0; i < corners.Count; i++)
            {
                separation[i] = new float[corners.Count];
            }

            for (var i = 0; i < corners.Count; i++)
            {
                for (var k = i + 1; k < corners.Count; k++)
                {
                    var walk = graph.PathLength(corners[i], corners[k]);
                    separation[i][k] = walk;
                    separation[k][i] = walk;
                }
            }

            return separation;
        }

        /// <summary>
        /// Single-linkage grouping of bends into 시야 차단 지점 at §12's own minimum gap.
        /// <para>
        /// Single linkage rather than anything cleverer because that is what the
        /// geometry does: three bends at 0, 12 and 24 m are one continuous piece of
        /// cover even though the outer two are 24 m apart, since the sight line never
        /// comes back between them. A grouping that split them would report a legal
        /// 간격 for a stretch the monster never sees down.
        /// </para>
        /// </summary>
        /// <returns>Group index per bend, numbered from 0 with no gaps.</returns>
        private static int[] GroupIntoSightBreakPoints(int count, float[][] separation)
        {
            var parent = new int[count];
            for (var i = 0; i < count; i++)
            {
                parent[i] = i;
            }

            for (var i = 0; i < count; i++)
            {
                for (var k = i + 1; k < count; k++)
                {
                    if (separation[i][k] < GameConstants.LineOfSightBreakSpacingMin - MathX.Epsilon)
                    {
                        Union(parent, i, k);
                    }
                }
            }

            var label = new Dictionary<int, int>();
            var group = new int[count];
            for (var i = 0; i < count; i++)
            {
                var root = Find(parent, i);
                if (!label.TryGetValue(root, out var id))
                {
                    id = label.Count;
                    label[root] = id;
                }

                group[i] = id;
            }

            return group;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            var rootA = Find(parent, a);
            var rootB = Find(parent, b);
            if (rootA != rootB)
            {
                parent[rootB] = rootA;
            }
        }

        // ====================================================================
        // §12-D 외곽에서 중심까지 최단 90~140m — how far a runner must travel on
        // one floor.
        //
        // §12-D says "MapValidator와 씬 감사기가 층마다 확인한다" and MapValidator
        // never did. It was measured and printed by MapSceneGenerator instead,
        // where it has been outside the band since the tower was drawn. A number
        // that is printed and gates nothing is how a defect goes quiet, which is
        // the failure this rule exists to stop repeating.
        // ====================================================================

        /// <summary>
        /// Measures every storey's 외곽 → 중심 walk against §12-D's band.
        /// <para>
        /// <b>Where a storey starts.</b> A 투하구 landing (§01 — "떨어지면 언제나 다음 층
        /// 외곽"), which is where a runner arrives on B2–B8; and on the top storey, which
        /// nothing drops into, the whole 외곽 rail, because §01 stands twenty of them on it.
        /// Both are read off the graph: a landing is the lower end of a storey-changing
        /// edge, and the 외곽 rail is the outermost ring of the zone.
        /// </para>
        /// <para>
        /// <b>What counts as a ring.</b> Chebyshev radius from the zone's own centre —
        /// <c>max(|Δx|, |Δz|)</c> — because that is the metric a concentric storey is laid
        /// out in: one ring is one constant radius the whole way round, so "the outermost
        /// ring" is exactly "the largest radius any place in the zone stands at" and "the
        /// 중심" is the smallest. Neither needs to know the cell size, which is what lets a
        /// rule in Core measure a building the Editor drew.
        /// </para>
        /// <para>
        /// <b>Vacuous on a map with no 투하구, and it says so.</b> §12-D is a rule about a
        /// tower of storeys; a single-surface map has no 층 to walk down and this rule has
        /// nothing to measure on it. That is stated in the detail rather than hidden behind
        /// a bare pass.
        /// </para>
        /// </summary>
        private static MapValidationResult CheckCentrePath(MapGraph graph)
        {
            var rule = "외곽에서 중심까지 최단 " + GameConstants.CentrePathMetresMin + "~"
                       + GameConstants.CentrePathMetresMax + "m (§12-D, 층마다)";

            if (!HasStoreyChange(graph))
            {
                return new MapValidationResult(
                    RuleCentrePath, rule, true,
                    "Nothing on this map changes storey, so there is no 투하구, no 층 below the one you "
                    + "are on, and no 외곽 → 중심 descent for §12-D to bound. The rule is about the "
                    + "distance a runner must cover on a floor before they can leave it; a map with one "
                    + "surface has no such distance.", false);
            }

            var toMiddle = DistancesToStoreyMiddle(graph);
            var worst = float.PositiveInfinity;
            var longest = 0f;
            var outside = 0;
            var measured = 0;
            var storeysOutside = new List<string>();

            for (var zone = 0; zone < graph.Zones.Length; zone++)
            {
                var entries = StoreyEntries(graph, zone);
                var zoneWorst = float.PositiveInfinity;
                var zoneLongest = 0f;
                var zoneOutside = 0;
                var zoneMeasured = 0;

                for (var i = 0; i < entries.Count; i++)
                {
                    var walk = toMiddle[entries[i]];
                    if (float.IsNaN(walk) || float.IsPositiveInfinity(walk))
                    {
                        continue;
                    }

                    zoneMeasured++;
                    zoneWorst = walk < zoneWorst ? walk : zoneWorst;
                    zoneLongest = walk > zoneLongest ? walk : zoneLongest;
                    if (walk < GameConstants.CentrePathMetresMin - MathX.Epsilon
                        || walk > GameConstants.CentrePathMetresMax + MathX.Epsilon)
                    {
                        zoneOutside++;
                    }
                }

                if (zoneMeasured == 0)
                {
                    continue;
                }

                measured += zoneMeasured;
                outside += zoneOutside;
                worst = zoneWorst < worst ? zoneWorst : worst;
                longest = zoneLongest > longest ? zoneLongest : longest;

                if (zoneOutside > 0)
                {
                    storeysOutside.Add(
                        graph.Zones[zone].Name + " " + Metres(zoneWorst) + "~" + Metres(zoneLongest)
                        + " (" + zoneOutside + "/" + zoneMeasured + ")");
                }
            }

            if (measured == 0)
            {
                return new MapValidationResult(
                    RuleCentrePath, rule, false,
                    "This map has 투하구 in it, so it is a tower, and yet no storey has both a place a "
                    + "runner arrives at and a middle they can walk to. §12-D's distance is not "
                    + "measurable here, which is worse than being outside the band: the descent the "
                    + "whole race is made of cannot be shown to exist.", false);
            }

            var passed = outside == 0;
            var detail = measured + " storey entry point(s) walk " + Metres(worst) + "~" + Metres(longest)
                         + " to their own storey's middle = "
                         + (worst / GameConstants.RunSpeed).ToString("0.#", CultureInfo.InvariantCulture)
                         + "~"
                         + (longest / GameConstants.RunSpeed).ToString("0.#", CultureInfo.InvariantCulture)
                         + " s at 달리기 " + GameConstants.RunSpeed + " m/s, against §12-D's "
                         + Metres(GameConstants.CentrePathMetresMin) + "~"
                         + Metres(GameConstants.CentrePathMetresMax) + ". ";

            detail += passed
                ? "All inside."
                : outside + " of " + measured + " OUTSIDE: " + string.Join("; ", storeysOutside)
                  + ". §12-D says what a short storey spends — 「60~90 m로 줄이면 아는 사람이 2분 만에 "
                  + "끝내고, 그러면 맵을 아는 것이 실력이라는 전제가 보상 없이 사라진다」 — and §01 leaves "
                  + "a race only two sources of difference, 길을 아는가 and 어디까지 감수하는가. A floor "
                  + "solvable blind deletes the first. The fix is geometry: a longer rim-to-middle "
                  + "route in RadialStorey, not a wider band.";

            return new MapValidationResult(RuleCentrePath, rule, passed, detail, false);
        }

        /// <summary>True when any passage on the map joins two storeys — i.e. it is a tower.</summary>
        private static bool HasStoreyChange(MapGraph graph)
        {
            for (var e = 0; e < graph.Edges.Length; e++)
            {
                if (JoinsStoreys(graph, e))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Chebyshev radius of a place from its own zone's centre, in metres — which
        /// concentric ring it stands on, measured in a unit the graph actually carries.
        /// <para>
        /// <c>max(|Δx|, |Δz|)</c> rather than a straight line because a ring of a
        /// concentric maze is a SQUARE: its corner and the middle of its side are the same
        /// ring and are not the same distance from the axis. Euclid would call the corner
        /// an outer ring and split the 외곽 rail into four arcs.
        /// </para>
        /// </summary>
        private static float RingRadius(MapGraph graph, int node)
        {
            var zone = graph.Nodes[node].ZoneId;
            if (zone < 0 || zone >= graph.Zones.Length)
            {
                return float.NaN;
            }

            var centre = graph.Zones[zone].Center;
            var position = graph.Nodes[node].Position;
            var x = System.Math.Abs(position.X - centre.X);
            var z = System.Math.Abs(position.Z - centre.Z);
            return x > z ? x : z;
        }

        /// <summary>
        /// The places on a zone's innermost or outermost ring — its 중심, or its 외곽 고리.
        /// <para>
        /// <b>막힌 길 are not on the rail, and this is measured rather than assumed.</b> The
        /// outermost Chebyshev radius on every storey of the shipped tower is 27.5 m and
        /// holds <b>8</b> places, every one of them a dead end; the rail is the ring inside
        /// it at 25 m, <b>16</b> places, none of them dead ends. §01 stands twenty runners
        /// on the 외곽 고리 and a 막힌 길 is a pocket you can only be in by having walked into
        /// it — it is not a start line. So the outer-rail pick skips dead ends, and the
        /// first measurement of this rule (22 entry points, 50~80 m) was wrong for exactly
        /// that reason: it had found the pockets.
        /// </para>
        /// <para>
        /// The middle does NOT skip them. A 중심 that is a leaf is a map defect the
        /// 투하구 rules are about, and quietly measuring from the ring outside it would
        /// hide it.
        /// </para>
        /// </summary>
        /// <param name="graph">The map.</param>
        /// <param name="zone">Which zone.</param>
        /// <param name="outermost">True for the 외곽 rail, false for the middle.</param>
        private static List<int> RingExtreme(MapGraph graph, int zone, bool outermost)
        {
            var found = new List<int>();
            var pick = float.NaN;

            var members = graph.NodesInZone(zone);
            for (var i = 0; i < members.Length; i++)
            {
                if (outermost && graph.IsDeadEnd(members[i]))
                {
                    continue;
                }

                var radius = RingRadius(graph, members[i]);
                if (float.IsNaN(radius))
                {
                    continue;
                }

                if (float.IsNaN(pick)
                    || (outermost ? radius > pick + MathX.Epsilon : radius < pick - MathX.Epsilon))
                {
                    pick = radius;
                    found.Clear();
                    found.Add(members[i]);
                }
                else if (System.Math.Abs(radius - pick) <= MathX.Epsilon)
                {
                    found.Add(members[i]);
                }
            }

            return found;
        }

        /// <summary>
        /// Where a runner starts a storey: a 투하구 landing, or on the top storey the
        /// 외곽 rail §01 stands twenty of them on.
        /// <para>
        /// A landing is the LOWER end of a storey-changing edge because §01's 투하구 is
        /// one-way — "떨어지면 언제나 다음 층 외곽" — so the storey an edge delivers you to
        /// is the one underneath it. A storey nothing drops into is the top of the tower,
        /// and there the whole outer ring is a start line.
        /// </para>
        /// </summary>
        private static List<int> StoreyEntries(MapGraph graph, int zone)
        {
            var entries = new List<int>();
            for (var e = 0; e < graph.Edges.Length; e++)
            {
                if (!JoinsStoreys(graph, e))
                {
                    continue;
                }

                var a = graph.Nodes[graph.Edges[e].A];
                var b = graph.Nodes[graph.Edges[e].B];
                var landing = a.Position.Y < b.Position.Y ? a : b;
                if (landing.ZoneId == zone)
                {
                    entries.Add(landing.Id);
                }
            }

            return entries.Count > 0 ? entries : RingExtreme(graph, zone, true);
        }

        /// <summary>
        /// Walking distance from every place to its own storey's middle, in metres, or
        /// +∞ where the middle cannot be walked to.
        /// <para>
        /// One multi-source Dijkstra per storey, out from that floor's 중심. Two things
        /// make it per-storey rather than map-wide: §01 makes every floor an independent
        /// maze, and a 투하구 is a fall rather than a path, so a route that leaves the floor
        /// is not a route back to its middle. Public because <c>MapSceneGenerator</c>
        /// prices a chase in the same distance — 「중심까지 얼마나 남았는가」 is the only
        /// distance §02 settles the race on — and two answers to that question would let a
        /// rule and a report disagree about the same map.
        /// </para>
        /// </summary>
        /// <param name="graph">The map.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is null.</exception>
        public static float[] DistancesToStoreyMiddle(MapGraph graph)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            var best = new float[graph.Nodes.Length];
            for (var i = 0; i < best.Length; i++)
            {
                best[i] = float.PositiveInfinity;
            }

            var open = new List<int>();
            for (var zone = 0; zone < graph.Zones.Length; zone++)
            {
                var middle = RingExtreme(graph, zone, false);
                for (var i = 0; i < middle.Count; i++)
                {
                    best[middle[i]] = 0f;
                    open.Add(middle[i]);
                }
            }

            while (open.Count > 0)
            {
                var pick = 0;
                for (var i = 1; i < open.Count; i++)
                {
                    if (best[open[i]] < best[open[pick]])
                    {
                        pick = i;
                    }
                }

                var at = open[pick];
                open.RemoveAt(pick);

                var incident = graph.IncidentEdges(at);
                for (var i = 0; i < incident.Length; i++)
                {
                    var other = graph.Edges[incident[i]].Other(at);
                    if (graph.Nodes[other].ZoneId != graph.Nodes[at].ZoneId)
                    {
                        continue;
                    }

                    var candidate = best[at] + graph.Edges[incident[i]].Length;
                    if (candidate >= best[other])
                    {
                        continue;
                    }

                    best[other] = candidate;
                    open.Add(other);
                }
            }

            return best;
        }

        // ====================================================================
        // Formatting. Kept here so every message reads the same way.
        // ====================================================================

        private static string Metres(float value) =>
            float.IsPositiveInfinity(value)
                ? "no way round at all"
                : value.ToString("0.#", CultureInfo.InvariantCulture) + " m";

        private static string Seconds(float value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture) + " s";

        private static string Degrees(float value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture) + "°";

        private static string Percent(float fraction) =>
            (fraction * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";

        private static string Chain(MapGraph graph, int[] nodes)
        {
            if (nodes == null || nodes.Length == 0)
            {
                return "(nowhere)";
            }

            var text = new StringBuilder();
            for (var i = 0; i < nodes.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(" → ");
                }

                text.Append(graph.Nodes[nodes[i]].Describe());
            }

            return text.ToString();
        }
    }
}
