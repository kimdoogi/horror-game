using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Map
{
    /// <summary>§12's verdict on a map, from the 주자 테스트 success-rate table.</summary>
    public enum RunnerTestVerdict
    {
        /// <summary>Below §12's band. "너무 어렵다 — S자 통로를 추가한다."</summary>
        TooHard = 0,

        /// <summary>Inside §12's 5~7/10 band. "적정."</summary>
        Balanced = 1,

        /// <summary>Above §12's band. "너무 쉽다 — 시야 차단 지점을 줄인다."</summary>
        TooEasy = 2,
    }

    /// <summary>
    /// One run of §12's 주자 테스트 from one point: aggro taken, and either broken or not.
    /// </summary>
    public readonly struct RunnerTestAttempt
    {
        /// <summary>Builds an attempt record. Produced by <see cref="RunnerTest"/>.</summary>
        /// <param name="startNodeId">Where the Runner took aggro.</param>
        /// <param name="released">Whether §06's release conditions were both met.</param>
        /// <param name="elapsedSeconds">Time from aggro to release, or to the end of the attempt.</param>
        /// <param name="gapMetres">Distance between Runner and monster at that moment, along the route.</param>
        /// <param name="breaksCrossed">Sight-breaking corners the Runner had rounded by then.</param>
        /// <param name="route">Node ids the Runner ran, in order.</param>
        /// <param name="sprintDelaySeconds">How long the Runner held the sprint before spending it (§06's real dilemma).</param>
        /// <param name="explanation">Why it went the way it did, in a designer's terms.</param>
        public RunnerTestAttempt(
            int startNodeId,
            bool released,
            float elapsedSeconds,
            float gapMetres,
            int breaksCrossed,
            int[] route,
            float sprintDelaySeconds,
            string explanation)
        {
            StartNodeId = startNodeId;
            Released = released;
            ElapsedSeconds = elapsedSeconds;
            GapMetres = gapMetres;
            BreaksCrossed = breaksCrossed;
            Route = route;
            SprintDelaySeconds = sprintDelaySeconds;
            Explanation = explanation;
        }

        /// <summary>Node the Runner took aggro at. §12: "맵의 임의 지점에서".</summary>
        public int StartNodeId { get; }

        /// <summary>True when the Runner broke aggro — §06's 12 m and 3 s, both.</summary>
        public bool Released { get; }

        /// <summary>Seconds from taking aggro to the release, or to being caught or running out of map.</summary>
        public float ElapsedSeconds { get; }

        /// <summary>Runner-to-monster distance along the route at the end of the attempt, metres.</summary>
        public float GapMetres { get; }

        /// <summary>Sight-breaking corners rounded. §12's whole point is that one is not enough.</summary>
        public int BreaksCrossed { get; }

        /// <summary>The escape the Runner ran, as node ids. Empty when no route existed at all.</summary>
        public int[] Route { get; }

        /// <summary>
        /// Seconds the Runner waited before sprinting. §06 asks the question directly —
        /// "처음부터 질주 → 거리는 벌지만 차단 지점 도달 전에 소진 / 아껴두면 → 그 사이에
        /// 잡힐 수 있음" — so the answer the map forces is worth reporting.
        /// </summary>
        public float SprintDelaySeconds { get; }

        /// <summary>Plain-language account of the attempt, for a report a designer will act on.</summary>
        public string Explanation { get; }
    }

    /// <summary>
    /// Parameters of the 주자 테스트. Everything defaults to the §06/§12 numbers; the
    /// knobs exist so the simulator can sweep a map against a later threat tier (§07
    /// raises the monster to 5.2 m/s by 32 min, and a map that only works at 4.8
    /// stops working then).
    /// </summary>
    public readonly struct RunnerTestSettings
    {
        /// <summary>Builds settings, clamping the values that would otherwise make the simulation meaningless.</summary>
        /// <param name="monsterSpeed">m/s. §07's tier speed, or <see cref="GameConstants.MonsterBaseSpeed"/>.</param>
        /// <param name="aggroStartDistance">Metres the monster starts behind. §12's table endorses 10 m.</param>
        /// <param name="sampleCount">Points to try. §12 says 10, and its bands are quoted against 10.</param>
        /// <param name="stepSeconds">Simulation step. Clamped to (0, <see cref="GameConstants.FixedStep"/>].</param>
        /// <param name="routeReachMetres">How far along an escape route to look. §12 frames the release inside one sprint's travel.</param>
        public RunnerTestSettings(
            float monsterSpeed,
            float aggroStartDistance,
            int sampleCount,
            float stepSeconds,
            float routeReachMetres)
        {
            MonsterSpeed = monsterSpeed > 0f ? monsterSpeed : GameConstants.MonsterBaseSpeed;
            AggroStartDistance = aggroStartDistance > 0f
                ? aggroStartDistance
                : GameConstants.RunnerTestAggroStartDistance;
            SampleCount = sampleCount > 0 ? sampleCount : GameConstants.RunnerTestSampleCount;

            // A step above FixedStep could carry the Runner past a corner inside one
            // update and hand back a release the map never earned; a step at or below
            // it only costs time. Zero or negative would never advance the clock at
            // all, so it becomes the fixed step rather than an infinite loop.
            MonsterStep = stepSeconds > 0f && stepSeconds < GameConstants.FixedStep
                ? stepSeconds
                : GameConstants.FixedStep;

            RouteReachMetres = routeReachMetres > 0f
                ? routeReachMetres
                : GameConstants.SprintMaxTravelDistance;
        }

        /// <summary>Monster speed, m/s.</summary>
        public float MonsterSpeed { get; }

        /// <summary>Metres the monster starts behind the Runner.</summary>
        public float AggroStartDistance { get; }

        /// <summary>Points sampled. §12's verdict table is written against 10.</summary>
        public int SampleCount { get; }

        /// <summary>Simulation step in seconds, never above <see cref="GameConstants.FixedStep"/>.</summary>
        public float MonsterStep { get; }

        /// <summary>
        /// How far along an escape route the test looks, metres. §12 frames the
        /// release inside one sprint's travel — "질주 60m에 3~4번의 기회" — so a map that
        /// cannot deliver a release inside <see cref="GameConstants.SprintMaxTravelDistance"/>
        /// has failed on §12's own terms, and the search does not need to run further.
        /// </summary>
        public float RouteReachMetres { get; }

        /// <summary>The §06/§12 numbers as written.</summary>
        public static RunnerTestSettings Default => new RunnerTestSettings(
            GameConstants.MonsterBaseSpeed,
            GameConstants.RunnerTestAggroStartDistance,
            GameConstants.RunnerTestSampleCount,
            GameConstants.FixedStep,
            GameConstants.SprintMaxTravelDistance);
    }

    /// <summary>
    /// The result of §12's 실전 검증 — the single number that says whether a map is any good.
    /// </summary>
    public sealed class RunnerTestReport
    {
        private readonly RunnerTestAttempt[] _attempts;

        /// <summary>Wraps the attempts and derives §12's verdict from them.</summary>
        /// <param name="mapName">Label for the report header.</param>
        /// <param name="attempts">One per sampled point.</param>
        /// <exception cref="ArgumentNullException"><paramref name="attempts"/> is null.</exception>
        public RunnerTestReport(string mapName, RunnerTestAttempt[] attempts)
        {
            MapName = mapName;
            _attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));

            var released = 0;
            for (var i = 0; i < _attempts.Length; i++)
            {
                if (_attempts[i].Released)
                {
                    released++;
                }
            }

            Successes = released;
        }

        /// <summary>Name of the map tested.</summary>
        public string MapName { get; }

        /// <summary>Every attempt, in sample order.</summary>
        public RunnerTestAttempt[] Attempts => _attempts;

        /// <summary>How many of the sampled points could break aggro.</summary>
        public int Successes { get; }

        /// <summary>Points tried. §12 says 10.</summary>
        public int SampleCount => _attempts.Length;

        /// <summary>Share of points that could break aggro, 0 on an empty sample.</summary>
        public float SuccessRate => _attempts.Length == 0 ? 0f : Successes / (float)_attempts.Length;

        /// <summary>
        /// §12's judgement. The bands are contiguous at
        /// <see cref="GameConstants.RunnerTestPassRateMin"/> and
        /// <see cref="GameConstants.RunnerTestPassRateMax"/>, which is slightly
        /// stricter than §12's own table: that table rates 8/10 이상 too easy and
        /// 3/10 이하 too hard, and says nothing about 4/10. Here 4/10 is TooHard —
        /// see docs/BALANCE-FINDINGS.md.
        /// </summary>
        public RunnerTestVerdict Verdict
        {
            get
            {
                var rate = SuccessRate;
                if (rate < GameConstants.RunnerTestPassRateMin - MathX.Epsilon)
                {
                    return RunnerTestVerdict.TooHard;
                }

                return rate > GameConstants.RunnerTestPassRateMax + MathX.Epsilon
                    ? RunnerTestVerdict.TooEasy
                    : RunnerTestVerdict.Balanced;
            }
        }

        /// <summary>True when the rate lands in §12's 적정 band.</summary>
        public bool Passed => Verdict == RunnerTestVerdict.Balanced;

        /// <summary>§12's own prescription for the verdict — the fix, not just the grade.</summary>
        public string Advice
        {
            get
            {
                switch (Verdict)
                {
                    case RunnerTestVerdict.TooEasy:
                        return "너무 쉽다 — 시야 차단 지점을 줄인다 (§12). Aggro is a threat the players can "
                               + "shrug off, so §06's chase never becomes the pressure the game is built on.";

                    case RunnerTestVerdict.TooHard:
                        return "너무 어렵다 — S자 통로를 추가한다 (§12). With the release out of reach, 주자 "
                               + "stops being the role that can escape and §06's speed ladder decides every "
                               + "chase before it starts.";

                    default:
                        return "적정 (§12). Breaking aggro is possible from most of the map and never free.";
                }
            }
        }

        /// <summary>The report as text, one line per sampled point.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("§12 주자 테스트 — ").Append(MapName).Append(": ")
                .Append(Successes).Append('/').Append(SampleCount).Append(" (")
                .Append((SuccessRate * 100f).ToString("0.#", CultureInfo.InvariantCulture)).Append("%), ")
                .Append(Verdict).Append('\n')
                .Append("  ").Append(Advice).Append('\n');

            for (var i = 0; i < _attempts.Length; i++)
            {
                var a = _attempts[i];
                text.Append("  ").Append(a.Released ? "released " : "CAUGHT   ")
                    .Append("from #").Append(a.StartNodeId).Append(": ").Append(a.Explanation).Append('\n');
            }

            return text.ToString();
        }

        /// <inheritdoc />
        public override string ToString() => Describe();
    }

    /// <summary>
    /// §12's 실전 검증: "맵의 임의 지점에서 어그로를 걸고, 해제할 수 있는가? 10개 지점에서
    /// 시도한다."
    /// <para>
    /// This is the most valuable thing in the map system, because it converts "is
    /// this map any good" into a number that a test can hold. Everything
    /// <see cref="MapValidator"/> checks is necessary and none of it is sufficient: a
    /// map can satisfy all eleven checklist items and still be unescapable, because
    /// what matters is not whether an S-corridor exists but whether the Runner can
    /// reach one before the monster closes.
    /// </para>
    /// <para>
    /// The simulation is §12's own arithmetic, run per corner instead of once. §12
    /// computes the single-corner case by hand — "괴물이 그 모퉁이에 도달하는 시간 =
    /// D / 4.8초, 시야 차단 3초가 필요 → D ≥ 14.4m" — and this does the same thing along
    /// a whole escape route, so consecutive cover (연속 차단) falls out rather than
    /// being special-cased: while the monster is still short of the second corner, the
    /// first one is behind it and the sight line is still broken.
    /// </para>
    /// <para>
    /// Deterministic. Sample points come from an <see cref="IRandomSource"/> and time
    /// advances in explicit steps, so a seed reproduces a verdict exactly — a map that
    /// scores 5/10 in CI scores 5/10 on the machine investigating why.
    /// </para>
    /// </summary>
    public static class RunnerTest
    {
        /// <summary>
        /// Runs the test on the §06/§12 numbers, sampling points from
        /// <paramref name="random"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public static RunnerTestReport Run(MapGraph graph, IRandomSource random) =>
            Run(graph, random, RunnerTestSettings.Default);

        /// <summary>
        /// Runs the test with explicit settings.
        /// <para>
        /// Points are drawn without replacement so that ten samples are ten different
        /// places. A map with fewer places than <see cref="RunnerTestSettings.SampleCount"/>
        /// cycles through them again rather than shortening the sample, because §12's
        /// verdict bands are quoted against ten tries and a rate out of three would not
        /// mean the same thing.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public static RunnerTestReport Run(MapGraph graph, IRandomSource random, RunnerTestSettings settings)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (graph.Nodes.Length == 0)
            {
                return new RunnerTestReport(NameOf(graph), Array.Empty<RunnerTestAttempt>());
            }

            var order = new int[graph.Nodes.Length];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            for (var i = order.Length - 1; i > 0; i--)
            {
                var j = random.NextInt(0, i + 1);
                var swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }

            var starts = new int[settings.SampleCount];
            for (var i = 0; i < starts.Length; i++)
            {
                starts[i] = order[i % order.Length];
            }

            return RunAt(graph, starts, settings);
        }

        /// <summary>
        /// Runs the test from named points instead of sampled ones — how a test pins a
        /// specific corner of a map, and how the editor re-runs the same ten points
        /// after a change.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public static RunnerTestReport RunAt(MapGraph graph, int[] startNodes, RunnerTestSettings settings)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (startNodes == null)
            {
                throw new ArgumentNullException(nameof(startNodes));
            }

            var attempts = new RunnerTestAttempt[startNodes.Length];
            for (var i = 0; i < startNodes.Length; i++)
            {
                attempts[i] = Attempt(graph, startNodes[i], settings);
            }

            return new RunnerTestReport(NameOf(graph), attempts);
        }

        /// <summary>Runs the test from named points on the §06/§12 numbers.</summary>
        public static RunnerTestReport RunAt(MapGraph graph, int[] startNodes) =>
            RunAt(graph, startNodes, RunnerTestSettings.Default);

        private static string NameOf(MapGraph graph) =>
            string.IsNullOrEmpty(graph.Name) ? "unnamed map" : graph.Name!;

        // ====================================================================
        // One point.
        // ====================================================================

        private static RunnerTestAttempt Attempt(MapGraph graph, int startNode, RunnerTestSettings settings)
        {
            if (startNode < 0 || startNode >= graph.Nodes.Length)
            {
                return new RunnerTestAttempt(
                    startNode, false, 0f, settings.AggroStartDistance, 0, Array.Empty<int>(), 0f,
                    "no such place on the map");
            }

            if (graph.Degree(startNode) == 0)
            {
                return new RunnerTestAttempt(
                    startNode, false, 0f, settings.AggroStartDistance, 0, new[] { startNode }, 0f,
                    "nowhere to run: " + graph.Nodes[startNode].Describe()
                    + " has no passage out of it, so aggro taken here can only end one way");
            }

            var best = default(RunnerTestAttempt);
            var haveBest = false;
            var routesTried = 0;

            var route = new List<int> { startNode };
            var visited = new bool[graph.Nodes.Length];
            visited[startNode] = true;

            Explore(graph, settings, route, visited, 0f, ref routesTried, ref best, ref haveBest);

            if (haveBest)
            {
                return best;
            }

            return new RunnerTestAttempt(
                startNode, false, 0f, settings.AggroStartDistance, 0, new[] { startNode }, 0f,
                "no escape route explored from " + graph.Nodes[startNode].Describe());
        }

        // Depth-first over simple paths, evaluating only the maximal ones: a release
        // that happens on a prefix happens at the same instant on every extension of
        // it, so evaluating prefixes separately would only repeat work.
        private static void Explore(
            MapGraph graph,
            RunnerTestSettings settings,
            List<int> route,
            bool[] visited,
            float arcSoFar,
            ref int routesTried,
            ref RunnerTestAttempt best,
            ref bool haveBest)
        {
            if (haveBest && best.Released)
            {
                return;
            }

            if (routesTried >= GameConstants.RunnerTestRouteLimitPerPoint)
            {
                return;
            }

            var at = route[route.Count - 1];
            var incident = graph.IncidentEdges(at);
            var extended = false;

            for (var k = 0; k < incident.Length; k++)
            {
                var edgeId = incident[k];
                var next = graph.Edges[edgeId].Other(at);
                if (visited[next])
                {
                    continue;
                }

                var arc = arcSoFar + graph.Edges[edgeId].Length;
                if (arc > settings.RouteReachMetres)
                {
                    continue;
                }

                extended = true;
                visited[next] = true;
                route.Add(next);
                Explore(graph, settings, route, visited, arc, ref routesTried, ref best, ref haveBest);
                route.RemoveAt(route.Count - 1);
                visited[next] = false;

                if (haveBest && best.Released)
                {
                    return;
                }
            }

            if (extended || route.Count < 2)
            {
                return;
            }

            routesTried++;
            var attempt = Evaluate(graph, settings, route.ToArray());
            if (!haveBest || (attempt.Released && !best.Released) || attempt.GapMetres > best.GapMetres)
            {
                best = attempt;
                haveBest = true;
            }
        }

        /// <summary>
        /// Simulates one escape route, trying §06's two answers to "질주를 언제 쓸
        /// 것인가": spend the sprint at once, or hold it until a given corner.
        /// </summary>
        private static RunnerTestAttempt Evaluate(MapGraph graph, RunnerTestSettings settings, int[] route)
        {
            BuildRoute(graph, route, out var arc, out var breaks);

            // Candidate strategies: sprint immediately, or hold the sprint until the
            // Runner reaches each sight-breaking corner. The arrival times are quoted
            // at plain running speed so that the strategy is a decision the player
            // could actually make from the map, not one derived from its own outcome.
            var delays = new List<float> { 0f };
            for (var i = 1; i < arc.Length; i++)
            {
                if (breaks[i])
                {
                    delays.Add(arc[i] / GameConstants.RunSpeed);
                }
            }

            var best = default(RunnerTestAttempt);
            var haveBest = false;

            for (var d = 0; d < delays.Count; d++)
            {
                var attempt = Simulate(graph, settings, route, arc, breaks, delays[d]);
                if (attempt.Released)
                {
                    return attempt;
                }

                if (!haveBest || attempt.GapMetres > best.GapMetres)
                {
                    best = attempt;
                    haveBest = true;
                }
            }

            return best;
        }

        private static void BuildRoute(MapGraph graph, int[] route, out float[] arc, out bool[] breaks)
        {
            arc = new float[route.Length];
            breaks = new bool[route.Length];

            for (var i = 1; i < route.Length; i++)
            {
                arc[i] = arc[i - 1] + Vec3.DistanceFlat(
                    graph.Nodes[route[i - 1]].Position, graph.Nodes[route[i]].Position);
            }

            // A corner breaks the sight line when the corridor turns far enough and
            // the turn is not inside a room you can see across. §12 gives 개방 공간
            // 15~25 m sight lines on purpose — that is where aggro is taken — so a
            // bend drawn inside one hides nobody.
            for (var i = 1; i + 1 < route.Length; i++)
            {
                if (graph.Nodes[route[i]].Has(MapNodeKind.OpenSpace))
                {
                    continue;
                }

                var incoming = (graph.Nodes[route[i]].Position - graph.Nodes[route[i - 1]].Position).Flat;
                var outgoing = (graph.Nodes[route[i + 1]].Position - graph.Nodes[route[i]].Position).Flat;
                breaks[i] = MathX.AngleBetween(incoming, outgoing) >= GameConstants.MapSightBreakingBendDegrees;
            }
        }

        private static RunnerTestAttempt Simulate(
            MapGraph graph,
            RunnerTestSettings settings,
            int[] route,
            float[] arc,
            bool[] breaks,
            float sprintDelay)
        {
            var total = arc[arc.Length - 1];
            var step = settings.MonsterStep;

            var runner = 0f;
            var monster = -settings.AggroStartDistance;
            var stamina = GameConstants.SprintStaminaSeconds;
            var engaged = false;
            var sinceSprint = GameConstants.SprintRecoveryDelaySeconds;
            var brokenFor = 0f;
            var elapsed = 0f;
            var breaksCrossed = 0;

            while (true)
            {
                // §06's sprint as a resource: a burst cannot be restarted until the
                // bar has recovered its re-engage fraction, and the refill only
                // begins after the delay. Without that a Runner could tap Shift and
                // hold an unlimited 60%-duty sprint (docs/BALANCE-FINDINGS.md), which
                // would grade every map as escapable.
                var wantsSprint = elapsed >= sprintDelay - MathX.Epsilon;
                var canSprint = stamina > 0f
                                && (engaged
                                    || stamina >= GameConstants.SprintReengageStaminaFraction
                                    * GameConstants.SprintStaminaSeconds);
                var sprinting = wantsSprint && canSprint;

                if (sprinting)
                {
                    engaged = true;
                    sinceSprint = 0f;
                    stamina -= step;
                    if (stamina <= 0f)
                    {
                        stamina = 0f;
                        engaged = false;
                    }
                }
                else
                {
                    sinceSprint += step;
                    if (sinceSprint >= GameConstants.SprintRecoveryDelaySeconds)
                    {
                        stamina += step * (GameConstants.SprintStaminaSeconds
                                           / GameConstants.SprintStaminaRecoverySeconds);
                        if (stamina > GameConstants.SprintStaminaSeconds)
                        {
                            stamina = GameConstants.SprintStaminaSeconds;
                        }
                    }
                }

                var runnerSpeed = sprinting ? GameConstants.RunnerSprintSpeed : GameConstants.RunSpeed;
                runner += runnerSpeed * step;
                monster += settings.MonsterSpeed * step;
                elapsed += step;

                var gap = runner - monster;

                if (gap <= 0f)
                {
                    return new RunnerTestAttempt(
                        route[0], false, elapsed, gap, breaksCrossed, route, sprintDelay,
                        "caught after " + Seconds(elapsed) + " and " + Metres(runner) + " of running: "
                        + Describe(graph, route)
                        + ". " + BreakSummary(breaksCrossed)
                        + " §06: the monster is only "
                        + (settings.MonsterSpeed - GameConstants.RunSpeed)
                            .ToString("0.#", CultureInfo.InvariantCulture)
                        + " m/s faster than running, so this route needed cover, not speed.");
                }

                if (runner > total)
                {
                    return new RunnerTestAttempt(
                        route[0], false, elapsed, gap, breaksCrossed, route, sprintDelay,
                        "ran " + Metres(total) + " to the end of " + Describe(graph, route)
                        + " still held at " + Metres(gap) + ", inside §12's "
                        + Metres(settings.RouteReachMetres) + " of one sprint's travel. "
                        + BreakSummary(breaksCrossed)
                        + " §12 asks for 3~4 chances inside that distance; this route offered none that held.");
                }

                var covered = false;
                var crossed = 0;
                for (var i = 1; i < arc.Length; i++)
                {
                    if (!breaks[i])
                    {
                        continue;
                    }

                    if (arc[i] < runner)
                    {
                        crossed++;
                    }

                    if (monster < arc[i] && arc[i] < runner)
                    {
                        covered = true;
                    }
                }

                breaksCrossed = crossed;
                brokenFor = covered ? brokenFor + step : 0f;

                if (brokenFor >= GameConstants.AggroReleaseLineOfSightBreak - MathX.Epsilon
                    && gap >= GameConstants.AggroReleaseDistance - MathX.Epsilon)
                {
                    return new RunnerTestAttempt(
                        route[0], true, elapsed, gap, breaksCrossed, route, sprintDelay,
                        "released after " + Seconds(elapsed) + " at " + Metres(gap) + " with "
                        + Seconds(brokenFor) + " of unbroken cover"
                        + (sprintDelay <= MathX.Epsilon
                            ? " (sprinted from the start)"
                            : " (held the sprint " + Seconds(sprintDelay) + ")")
                        + ", over " + BreakSummary(breaksCrossed) + " along " + Describe(graph, route) + ".");
                }
            }
        }

        private static string BreakSummary(int breaks)
        {
            if (breaks == 0)
            {
                return "No sight-breaking corner was ever rounded.";
            }

            return breaks == 1
                ? "1 sight-breaking corner rounded — §12: \"단일 모퉁이에 의존하면 안 된다\"."
                : breaks + " sight-breaking corners rounded.";
        }

        private static string Describe(MapGraph graph, int[] route)
        {
            var text = new StringBuilder();
            for (var i = 0; i < route.Length; i++)
            {
                if (i > 0)
                {
                    text.Append('→');
                }

                text.Append('#').Append(route[i]);
                var name = graph.Nodes[route[i]].Name;
                if (!string.IsNullOrEmpty(name))
                {
                    text.Append(' ').Append(name);
                }
            }

            return text.ToString();
        }

        private static string Metres(float value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture) + " m";

        private static string Seconds(float value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture) + " s";
    }
}
