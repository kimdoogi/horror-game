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
        /// <param name="isChecklistItem">True for the eleven items on §12's 검증 체크리스트.</param>
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
        /// True for the eleven items §12 lists under 검증 체크리스트. The rest —
        /// zone count, zone diagonal, map extent, connectivity — come from §12's
        /// 수치 규칙 table instead, and are separated so that "the checklist passes"
        /// remains a statement about the checklist.
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

        /// <summary>True when the eleven 검증 체크리스트 items all pass, whatever the 수치 규칙 rules did.</summary>
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
    /// §12 opens with "맵은 아트가 아니라 시스템이다" and closes with an eleven-item
    /// checklist. This type is the join between those two sentences: a map that
    /// breaks one of the eleven fails here, in a unit test, instead of failing in a
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
        // §12 검증 체크리스트 — the eleven items, in the document's order.
        // ====================================================================

        /// <summary>"20m 넘는 직선 통로가 없다."</summary>
        public const string RuleStraightCorridor = "straight-corridor";

        /// <summary>"개방 공간이 최소 1개 있고, 미로 공간과 인접해 있다."</summary>
        public const string RuleOpenAdjacentToMaze = "open-adjacent-to-maze";

        /// <summary>"S자 통로(10m×2)가 구역당 최소 1개 있다."</summary>
        public const string RuleSCorridorPerZone = "s-corridor-per-zone";

        /// <summary>"순환로가 맵 전체에 3개 이상 있다." (§12's 수치 규칙 adds 구역당 1+.)</summary>
        public const string RuleLoops = "loops";

        /// <summary>"막힌 길 비율이 20~25%이고, 각각 보상이 있다."</summary>
        public const string RuleDeadEnds = "dead-ends";

        /// <summary>"구역별 바닥 재질이 다르고 경계가 명확하다."</summary>
        public const string RuleFloorMaterials = "floor-materials";

        /// <summary>"관측 지점이 구역당 1개 이상 있다."</summary>
        public const string RuleObservationPosts = "observation-posts";

        /// <summary>"잠글 수 있는 문이 구역당 1~2개, 병목에 있다."</summary>
        public const string RuleLockableDoors = "lockable-doors";

        /// <summary>"단서·목표물 후보가 구역당 3개, 모두 탈출로 2개 이상."</summary>
        public const string RuleCandidateSites = "candidate-sites";

        /// <summary>"구역 간 진입점이 2~3개로 제한돼 있다."</summary>
        public const string RuleZoneEntryPoints = "zone-entry-points";

        /// <summary>"출입구 근처에 은폐 지점이 있다 (§07 새벽 단계 대응)."</summary>
        public const string RuleConcealmentNearExit = "concealment-near-exit";

        // ====================================================================
        // §12 수치 규칙 — the sizes the checklist assumes but does not restate.
        // ====================================================================

        /// <summary>"구역 개수 4~6."</summary>
        public const string RuleZoneCount = "zone-count";

        /// <summary>"구역 대각선 30~40m."</summary>
        public const string RuleZoneDiagonal = "zone-diagonal";

        /// <summary>"맵 전체 100 × 100m."</summary>
        public const string RuleMapExtent = "map-extent";

        /// <summary>Every place must be walkable from every other. A map in two pieces is two maps.</summary>
        public const string RuleConnectivity = "connectivity";

        /// <summary>Each node must physically lie in the zone it claims, or every per-zone count above is fiction.</summary>
        public const string RuleZoneMembership = "zone-membership";

        /// <summary>"시야 차단 지점 간격 15~25m" — 질주 60m에 3~4번의 기회.</summary>
        public const string RuleSightBreakSpacing = "sight-break-spacing";

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
                CheckObservationPosts(graph),
                CheckLockableDoors(graph),
                CheckCandidateSites(graph),
                CheckZoneEntryPoints(graph),
                CheckConcealmentNearExit(graph),
                CheckZoneCount(graph),
                CheckZoneDiagonal(graph),
                CheckMapExtent(graph),
                CheckConnectivity(graph),
                CheckZoneMembership(graph),
                CheckSightBreakSpacing(graph),
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
        /// <b>Two things have to be true and only one of them used to be checked.</b>
        /// §12 draws "[개방 공간] ──진입── [미로 공간]" and says what it is for in the
        /// same breath — 어그로 시작 거리 15~25 m, with its own table rating a 3 m start
        /// ❌. So the arrow is not the whole rule: the box on the left has to be a place
        /// where 15 m of sight actually exists. A room a Runner crosses in two strides
        /// satisfies "touches a 미로 공간" and satisfies nothing else, and passing it
        /// tells a designer the map has a structure it does not have.
        /// </para>
        /// <para>
        /// <b>And the arrow itself has to be a step, not a fall.</b> On the 하강 tower
        /// every storey stacks in one column and the only edges between them are 투하구,
        /// which a runner can enter and never leave (§01). Walking the raw graph
        /// therefore let this rule pass on a pair 37 m apart in plan and a whole storey
        /// down — reported, verbatim, as "opens directly into". Adjacency across a
        /// one-way drop is not adjacency, so an edge that changes storey
        /// (<see cref="MapGraph.StoreyChangeMetres"/>) cannot be the 진입 this rule is
        /// looking for.
        /// </para>
        /// </summary>
        private static MapValidationResult CheckOpenAdjacentToMaze(MapGraph graph)
        {
            var open = graph.NodesOfKind(MapNodeKind.OpenSpace);
            var maze = graph.NodesOfKind(MapNodeKind.MazeSpace);

            // §12 asks for aggro to be taken from 15~25 m, and that band is the same
            // pair of numbers as 시야 차단 지점 간격 — the distance a Runner must buy
            // before a corner can pay it back. The floor of the band is what an
            // 개방 공간 has to be able to hold, so it is read from there rather than
            // written down twice.
            var needed = GameConstants.LineOfSightBreakSpacingMin;

            string detail;
            var passed = false;

            if (open.Length == 0)
            {
                detail = "No 개방 공간 anywhere. §12: aggro has to be taken from 15~25 m out, and its own "
                         + "table rates a 3 m start ❌ — with nowhere open, the Runner can only pull the "
                         + "monster from a distance at which the release is arithmetically impossible.";
            }
            else if (maze.Length == 0)
            {
                detail = "There are " + open.Length + " 개방 공간 and no 미로 공간. §12: \"개방 공간만 "
                         + "있으면 도망칠 곳이 없다\" — aggro can be taken and never broken.";
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
                             + "\"[개방 공간] ──진입── [미로 공간]\": the Runner takes aggro in the open and must "
                             + "reach cover before the monster closes. If the two are not adjacent, the walk "
                             + "between them is itself an unbroken run and the structure buys nothing.";

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

                    passed = span >= needed - MathX.Epsilon;
                    if (passed)
                    {
                        detail = "개방 공간 " + graph.Nodes[touch[0]].Describe() + " opens directly into "
                                 + "미로 공간 " + graph.Nodes[touch[1]].Describe() + " on the same storey, and "
                                 + "the room behind it is " + Metres(span) + " across ("
                                 + graph.Nodes[widest[0]].Describe() + " to "
                                 + graph.Nodes[widest[1]].Describe() + "), so §12's "
                                 + Metres(needed) + "~" + Metres(GameConstants.LineOfSightBreakSpacingMax)
                                 + " 어그로 시작 거리 exists in it.";
                    }
                    else
                    {
                        detail = "The widest 개방 공간 that touches a 미로 공간 is only " + Metres(span)
                                 + " across — " + graph.Nodes[widest[0]].Describe() + " to "
                                 + graph.Nodes[widest[1]].Describe() + ", against §12's "
                                 + Metres(needed) + " 어그로 시작 거리 floor. It does touch ("
                                 + graph.Nodes[touch[0]].Describe() + " → "
                                 + graph.Nodes[touch[1]].Describe()
                                 + "), so the arrow in \"[개방 공간] ──진입── [미로 공간]\" is drawn; what is "
                                 + "missing is the box on the left. §12's 어그로 시작 거리 table rates a 3 m "
                                 + "start ❌ and this room is nearer to that than to " + Metres(needed)
                                 + ": a Runner standing "
                                 + "in it is taken from a distance at which the release is arithmetically "
                                 + "impossible, because a sprint gains only "
                                 + (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed)
                                     .ToString("0.#", CultureInfo.InvariantCulture)
                                 + " m/s and a single corner needs "
                                 + Metres(GameConstants.SingleCornerMinDistance) + " of gap. A room this "
                                 + "size is a junction with the walls taken out, not §12's 개방 공간.";
                    }
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
        /// One room rather than one node, because §12's 15~25 m is a property of the
        /// VOLUME and no single node has a size. Storey-changing edges are cut first, so
        /// two halls stacked one above the other with a 투하구 between them are two rooms
        /// and not one 30 m one.
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
        // 5 — 막힌 길 비율이 20~25%이고, 각각 보상이 있다.
        // ====================================================================

        private static MapValidationResult CheckDeadEnds(MapGraph graph)
        {
            if (graph.Nodes.Length == 0)
            {
                return new MapValidationResult(
                    RuleDeadEnds, "막힌 길 비율이 20~25%이고, 각각 보상이 있다", false,
                    "The map has no places in it, so the 막힌 길 ratio is undefined. §12's band is a "
                    + "statement about a built map; an empty one cannot satisfy it.", true);
            }

            var deadEnds = new List<int>();
            var unrewarded = new List<string>();
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                if (!graph.IsDeadEnd(i))
                {
                    continue;
                }

                deadEnds.Add(i);
                if (graph.Nodes[i].DeadEndRewardValue <= 0)
                {
                    unrewarded.Add(graph.Nodes[i].Describe());
                }
            }

            var ratio = deadEnds.Count / (float)graph.Nodes.Length;
            var inBand = ratio >= GameConstants.DeadEndRatioMin - MathX.Epsilon
                         && ratio <= GameConstants.DeadEndRatioMax + MathX.Epsilon;
            var passed = inBand && unrewarded.Count == 0;

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

            if (unrewarded.Count > 0)
            {
                detail.Append(" These 막힌 길 hold no 전리품 · 자재, so there is no reason to have walked "
                              + "in: ").Append(string.Join(", ", unrewarded))
                    .Append(". §12 requires a reward on each — \"위험을 감수할 이유\".");
            }

            return new MapValidationResult(
                RuleDeadEnds, "막힌 길 비율이 20~25%이고, 각각 보상이 있다", passed, detail.ToString(), true);
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
        // 7 — 관측 지점이 구역당 1개 이상 있다.
        // ====================================================================

        private static MapValidationResult CheckObservationPosts(MapGraph graph)
        {
            var missing = new List<string>();
            var counts = new List<string>();
            for (var z = 0; z < graph.Zones.Length; z++)
            {
                var posts = graph.NodesOfKindInZone(z, MapNodeKind.ObservationPost);
                counts.Add(graph.Zones[z].Name + "=" + posts.Length);
                if (posts.Length < GameConstants.ObservationPostsPerZoneMin)
                {
                    missing.Add(graph.Zones[z].Name);
                }
            }

            var passed = graph.Zones.Length > 0 && missing.Count == 0;
            string detail;
            if (graph.Zones.Length == 0)
            {
                detail = "The map has no zones, so there is nowhere to require a 관측 지점.";
            }
            else if (passed)
            {
                detail = "관측 지점 per zone: " + string.Join(", ", counts) + ".";
            }
            else
            {
                detail = "No 관측 지점 in zone(s) " + string.Join(", ", missing)
                         + ". §12 관측자: \"없으면 관측자는 죽으러 가야 한다\" — the ability needs the monster "
                         + "inside " + Metres(GameConstants.ObserverRange) + " (§04) held still for "
                         + Seconds(GameConstants.ObserverStillSeconds)
                         + ", and standing that close on the floor is inside the monster's own "
                         + Metres(GameConstants.MonsterSightRange) + " sight range.";
            }

            return new MapValidationResult(
                RuleObservationPosts, "관측 지점이 구역당 1개 이상 있다", passed, detail, true);
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
        // 9 — 단서·목표물 후보가 구역당 3개, 모두 탈출로 2개 이상.
        // ====================================================================

        private static MapValidationResult CheckCandidateSites(MapGraph graph)
        {
            var problems = new List<string>();
            var counts = new List<string>();

            for (var z = 0; z < graph.Zones.Length; z++)
            {
                var sites = graph.NodesOfKindInZone(z, MapNodeKind.CandidateSite);
                counts.Add(graph.Zones[z].Name + "=" + sites.Length);

                if (sites.Length != GameConstants.CandidateSitesPerZone)
                {
                    problems.Add("zone " + graph.Zones[z].Name + " has " + sites.Length
                                 + " candidate sites, not §12's " + GameConstants.CandidateSitesPerZone
                                 + " — the objective is placed by choosing one per match (§03), so a zone with "
                                 + "fewer narrows the search for the players and a zone with more dilutes what "
                                 + "a clue is worth");
                }

                var panels = graph.NodesOfKindInZone(z, MapNodeKind.ElectricalPanel);

                for (var s = 0; s < sites.Length; s++)
                {
                    var site = sites[s];
                    var exits = graph.Degree(site);
                    if (exits < GameConstants.CandidateSiteMinExits)
                    {
                        problems.Add(graph.Nodes[site].Describe() + " has " + exits
                                     + " way(s) out, under §12's " + GameConstants.CandidateSiteMinExits
                                     + " — \"하나 막히면 다른 쪽\". Reading a clue takes "
                                     + Seconds(GameConstants.ClueReadSeconds)
                                     + " of held beam (§03); with one exit, a monster arriving during the read "
                                     + "is not a risk, it is a death");
                    }

                    if (panels.Length == 0)
                    {
                        problems.Add(graph.Nodes[site].Describe()
                                     + " is in a zone with no 전기 패널, so the Engineer cannot light it and "
                                     + "§03's \"어둠 = 목표의 잠금장치\" becomes a lock with no key — §12 requires "
                                     + "\"전기 패널 접근 가능\" at every candidate and \"전기 패널 구역당 1개\"");
                    }
                }
            }

            var passed = graph.Zones.Length > 0 && problems.Count == 0;
            string detail;
            if (graph.Zones.Length == 0)
            {
                detail = "The map has no zones, so there is nowhere to place candidate sites.";
            }
            else if (passed)
            {
                detail = "Candidate sites per zone: " + string.Join(", ", counts)
                         + ", each with " + GameConstants.CandidateSiteMinExits + "+ exits and a 전기 패널 in zone.";
            }
            else
            {
                detail = "§12: \"모든 후보가 위 조건을 만족해야 한다.\" " + string.Join("; ", problems) + ".";
            }

            return new MapValidationResult(
                RuleCandidateSites, "단서·목표물 후보가 구역당 3개, 모두 탈출로 2개 이상", passed, detail, true);
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
        // 11 — 출입구 근처에 은폐 지점이 있다 (§07 새벽 단계 대응).
        // ====================================================================

        private static MapValidationResult CheckConcealmentNearExit(MapGraph graph)
        {
            var entrances = graph.NodesOfKind(MapNodeKind.Entrance);
            if (entrances.Length == 0)
            {
                return new MapValidationResult(
                    RuleConcealmentNearExit, "출입구 근처에 은폐 지점이 있다", false,
                    "The map has no 출입구 marked, so there is no way out of it and nothing for the "
                    + "concealment rule to sit next to. §02 makes leaving the building the win condition.",
                    true);
            }

            // "근처" is read as no further than §12 ever allows a player to be from
            // cover — the widest legal gap between 시야 차단 지점. Any further and the
            // §07 새벽 monster, which knows the exit, catches the escort in the open
            // between the hiding place and the door.
            var reach = GameConstants.LineOfSightBreakSpacingMax;
            var problems = new List<string>();
            var listing = new List<string>();

            for (var i = 0; i < entrances.Length; i++)
            {
                var entrance = entrances[i];
                var nearby = graph.NodesWithinWalk(entrance, reach);
                var best = -1;
                for (var k = 0; k < nearby.Length; k++)
                {
                    if (graph.Nodes[nearby[k]].Has(MapNodeKind.Concealment))
                    {
                        best = nearby[k];
                        break;
                    }
                }

                if (best < 0)
                {
                    problems.Add("no 은폐 지점 within " + Metres(reach) + " of "
                                 + graph.Nodes[entrance].Describe());
                }
                else
                {
                    listing.Add(graph.Nodes[entrance].Describe() + " → " + graph.Nodes[best].Describe()
                                + " (" + Metres(graph.PathLength(entrance, best)) + ")");
                }
            }

            var passed = problems.Count == 0;
            var detail = passed
                ? "Concealment within " + Metres(reach) + " of every exit: " + string.Join("; ", listing) + "."
                : string.Join("; ", problems)
                  + ". §12's last item exists for §07 새벽, the tier where the monster knows the exit and "
                  + "patrols every zone with no 정지 at all (standstill chance "
                  + GameConstants.ThreatStandstillChanceNone.ToString("0.##", CultureInfo.InvariantCulture)
                  + ") at " + GameConstants.ThreatSpeedPreDawn.ToString("0.#", CultureInfo.InvariantCulture)
                  + " m/s. With nowhere to wait, the objective escort — "
                  + GameConstants.ObjectiveEscortMinPlayers + " players moving at "
                  + GameConstants.ObjectiveCarrySpeedMultiplier.ToString("0.##", CultureInfo.InvariantCulture)
                  + " speed with no sprint (§03) — has to walk into it.";

            return new MapValidationResult(
                RuleConcealmentNearExit, "출입구 근처에 은폐 지점이 있다 (§07 새벽 단계 대응)", passed, detail, true);
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
                  + GameConstants.ZoneCountMax + ". The band is sized by what has to fit: "
                  + GameConstants.CluesRequiredToLocate + " clues narrowing 층 → 구역 → 지점 (§03), "
                  + GameConstants.CandidateSitesPerZone + " candidate sites in each, and an exit. Fewer "
                  + "zones and a single clue names the objective outright; more and §07's patrol scope ("
                  + GameConstants.ThreatPatrolZonesEarlyEvening + " zone early, "
                  + GameConstants.ThreatPatrolZonesNight + " later) stops covering enough of the map to "
                  + "threaten anybody.";

            return new MapValidationResult(RuleZoneCount, "구역 개수 4~6", passed, detail, false);
        }

        private static MapValidationResult CheckZoneDiagonal(MapGraph graph)
        {
            var problems = new List<string>();
            for (var z = 0; z < graph.Zones.Length; z++)
            {
                var diagonal = graph.Zones[z].Diagonal;
                if (diagonal < GameConstants.ZoneDiagonalMin - MathX.Epsilon)
                {
                    problems.Add(graph.Zones[z].Name + " is " + Metres(diagonal) + " across, under §12's "
                                 + Metres(GameConstants.ZoneDiagonalMin) + ": a Listener fix off by "
                                 + Metres(GameConstants.ListenerErrorRadiusMax)
                                 + " (§04) would name the wrong zone rather than the wrong corner, which is "
                                 + "the one confusion §12 does not want");
                }
                else if (diagonal > GameConstants.ZoneDiagonalMax + MathX.Epsilon)
                {
                    problems.Add(graph.Zones[z].Name + " is " + Metres(diagonal) + " across, over §12's "
                                 + Metres(GameConstants.ZoneDiagonalMax) + ": a Runner covers "
                                 + Metres(GameConstants.SprintMaxTravelDistance)
                                 + " on a full sprint (§05), and §12 sizes a zone so two or three of them can "
                                 + "be crossed in one");
                }
            }

            var passed = graph.Zones.Length > 0 && problems.Count == 0;
            var detail = graph.Zones.Length == 0
                ? "The map has no zones to measure."
                : passed
                    ? "Every zone diagonal is inside " + Metres(GameConstants.ZoneDiagonalMin) + "~"
                      + Metres(GameConstants.ZoneDiagonalMax) + "."
                    : string.Join("; ", problems) + ".";

            return new MapValidationResult(RuleZoneDiagonal, "구역 대각선 30~40m", passed, detail, false);
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
                      + " (§05); a bigger building makes the round trip §03 requires cost more battery than "
                      + "the " + Seconds(GameConstants.BatterySecondsPerCell) + " one cell pays for.";

            return new MapValidationResult(RuleMapExtent, "맵 전체 100 × 100m", passed, detail, false);
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
        /// 지점 may therefore be no wider than
        /// <see cref="GameConstants.SightBreakPointSpanMax"/>, which is §12's own
        /// 14.4 m single-corner requirement minus the 10 m head start its 어그로 시작
        /// 거리 table endorses: cover deeper than that completes the release from
        /// wherever it is picked up, and 「주자는 멀리서 어그로를 걸어야 한다」 stops
        /// being true of the map.
        /// </para>
        /// </summary>
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
                if (widest[p] > GameConstants.SightBreakPointSpanMax + MathX.Epsilon
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
                    + "line the whole way. §12 allows " + Metres(GameConstants.SightBreakPointSpanMax)
                    + " — its own " + Metres(GameConstants.SingleCornerMinDistance)
                    + " single-corner requirement less the " + Metres(GameConstants.RunnerTestAggroStartDistance)
                    + " head start its 어그로 시작 거리 table endorses. Cover this deep finishes §06's "
                    + Seconds(GameConstants.AggroReleaseLineOfSightBreak)
                    + " from wherever it is picked up, which inverts §12's first conclusion — 「주자는 "
                    + "멀리서 어그로를 걸어야 한다」 — and leaves the 실전 검증 with nothing to grade");
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

                detail = pointCount + " 시야 차단 지점 built from " + corners.Count
                         + " bend(s), the widest " + Metres(span) + " deep (§12 allows "
                         + Metres(GameConstants.SightBreakPointSpanMax)
                         + "), nearest-neighbour spacing " + Metres(closest) + "~" + Metres(furthest)
                         + " inside §12's " + Metres(GameConstants.LineOfSightBreakSpacingMin) + "~"
                         + Metres(GameConstants.LineOfSightBreakSpacingMax) + ".";
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
