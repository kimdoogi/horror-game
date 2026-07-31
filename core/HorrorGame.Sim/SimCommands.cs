using System;
using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Map;
using HorrorGame.Core.Telemetry;
using HorrorGame.EditorTools.SceneGen;

namespace HorrorGame.Sim
{
    /// <summary>
    /// Command dispatch for the simulator. Kept separate from
    /// <see cref="Program"/> so the sweeps are reachable from tests.
    /// </summary>
    public static class SimCommands
    {
        // How many layouts `validate` checks §03's chain converges on. One would not
        // be evidence — the resolver draws a different objective, different hosts and
        // a different decoy count per seed — and the check is a few microseconds each.
        private const int ChainSampleSeeds = 500;

        /// <summary>Dispatches a subcommand.</summary>
        /// <param name="args">Arguments as passed on the command line.</param>
        /// <returns>A process exit code.</returns>
        public static int Run(string[] args)
        {
            try
            {
                return Dispatch(args);
            }
            catch (MapSketchException error)
            {
                // The simulator builds the shipped map from FirstMapSketch rather than
                // from a copy of it, so a map that is mid-edit stops the simulator. That
                // is the trade F-006 was worth making — a tool that cannot be run against
                // a broken building is better than one that quietly measures a different
                // one — but the failure has to read as "the map does not build" and not
                // as a crash in a balance tool.
                Console.Error.WriteLine(
                    "The map does not currently build, so there is nothing to measure:\n  "
                    + error.Message
                    + "\nFix unity/HorrorGame/Assets/Scripts/Editor/SceneGen/, then run this again. "
                    + "MapSceneGenerator will be refusing to write the scene for the same reason.");
                return 5;
            }
        }

        private static int Dispatch(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            switch (args[0])
            {
                case "validate":
                    return Validate();

                case "map":
                    return DescribeMap();

                case "match":
                    return RunOneMatch(args);

                case "run":
                    return RunPopulation(args);

                case "replay":
                    return Replay(args);

                case "sweep":
                    return Sweep(args);

                case "help":
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                    PrintUsage();
                    return 1;
            }
        }

        // ====================================================================

        private static int Validate()
        {
            Console.WriteLine("Balance constants are internally consistent.");

            BalanceOverrides.SelfCheck();
            Console.WriteLine("BalanceOverrides reproduces CarryLoad exactly at the shipped values.");

            var map = SimMap.Build();
            Console.WriteLine("CachedWorldProbe reproduces MapGraphProbe on every node pair.");

            var report = MapValidator.Validate(map.Graph);
            Console.WriteLine("§12 map validation: "
                + (report.Passed ? "passed" : "failed [" + string.Join(", ", report.FailedRuleIds) + "]"));

            if (!report.Passed)
            {
                // A gate that prints "failed" and exits 0 is not a gate. §12's checklist
                // is the one MapSceneGenerator refuses to write a scene through, so a
                // balance run taken while it is red is a balance run on a map that
                // cannot ship.
                Console.Error.WriteLine(
                    "§12 rejects this map, so no measurement taken from it describes the shipped game. "
                    + "MapSceneGenerator would refuse to write the scene for the same reasons.");
                return 6;
            }

            var unwinnable = FirstUnwinnableLayout(map, ChainSampleSeeds);
            if (unwinnable >= 0)
            {
                Console.Error.WriteLine(
                    "Seed " + unwinnable + " lays out a match whose §03 chain does not converge on the objective: "
                    + "a team that read every clue correctly would still not be pinned to the right 후보 지점. "
                    + "That is a catalog defect — the storey signs or the site labels — not a balance finding.");
                return 4;
            }

            Console.WriteLine("§03's chain converges on the objective in all " + ChainSampleSeeds
                + " sampled layouts.");

            // Reported rather than enforced: it is a property of the building and of
            // MapGraph.NearestNode, and the simulator's job is to say what it measured.
            var ambiguous = map.CrossStoreyAmbiguities(GameConstants.MonsterWaypointTolerance);
            Console.WriteLine("Places NearestNode cannot tell apart across storeys (within "
                + GameConstants.MonsterWaypointTolerance.ToString("0.##", CultureInfo.InvariantCulture)
                + " m in plan): " + ambiguous.Count + ".");
            for (var i = 0; i < ambiguous.Count && i < 8; i++)
            {
                Console.WriteLine("  " + ambiguous[i]);
            }

            return 0;
        }

        private static int DescribeMap()
        {
            var map = SimMap.Build();
            Console.Write(DescribeTheBuilding(map));
            Console.WriteLine();
            Console.Write(MapQualityReport.Measure(map.Sketch).Describe());
            return 0;
        }

        /// <summary>
        /// The banner every population report carries: which building these numbers
        /// were measured in.
        /// <para>
        /// docs/BALANCE-FINDINGS.md F-006 spent a whole pass being answered against a
        /// map the game does not have, and nothing in the output said so. Printing the
        /// building's own §12 census next to the match numbers is what makes the next
        /// reader able to check: these lines must reproduce docs/STATUS.md §1.6, which
        /// is measured from the Unity scene by a different tool.
        /// </para>
        /// </summary>
        private static string DescribeTheBuilding(SimMap map)
        {
            var graph = map.Graph;
            var deadEnds = 0;
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                if (graph.IsDeadEnd(i))
                {
                    deadEnds++;
                }
            }

            var validation = MapValidator.Validate(graph);
            graph.Footprint(out var min, out var max);

            var text = new System.Text.StringBuilder();
            text.Append("=== the building these matches were run in\n");
            text.Append("  ").Append(graph.Name).Append("  (seed ").Append(map.Sketch.Seed).Append(")\n");
            text.Append("  ").Append(graph.Zones.Length).Append(" zones · ")
                .Append(graph.Nodes.Length).Append(" places · ")
                .Append(graph.Edges.Length).Append(" passages · ")
                .Append(graph.IndependentLoopCount).Append(" 순환로 · ")
                .Append(deadEnds).Append(" 막힌 길 · footprint ")
                .Append(Metres(max.X - min.X)).Append(" × ").Append(Metres(max.Z - min.Z)).Append('\n');
            text.Append("  §12 validation ").Append(validation.Passed ? "PASS" : "FAIL")
                .Append(" · 후보 지점 ").Append(map.Catalog.Sites.Count)
                .Append(" · 전리품 ").Append(map.LootNodes.Length)
                .Append(" · 금고 ").Append(map.SafeNodes.Length)
                .Append(" · monster spawn ").Append(Metres(graph.PathLength(map.EntranceNode, map.MonsterSpawnNode)))
                .Append(" from the door\n");
            text.Append("  built by FirstMapSketch.Build — the same call MapSceneGenerator makes before it lays\n");
            text.Append("  a single FBX, compiled into this binary rather than exported to it (F-006).\n");
            return text.ToString();
        }

        private static string Metres(float value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture) + " m";

        /// <summary>
        /// The first seed whose layout a perfect team could not solve, or −1.
        /// <para>
        /// <see cref="ObjectiveResolver.VerifyChainConverges"/> is the host-side
        /// invariant that §03's three clues, read correctly, name exactly one site and
        /// that it is the right one. It is the check that would catch the way this
        /// simulator's map could go wrong: the storey signs and the site labels are
        /// the simulator's own, and a duplicate label on a storey would make a share of
        /// matches quietly unwinnable — which would look exactly like a balance finding.
        /// </para>
        /// </summary>
        private static int FirstUnwinnableLayout(SimMap map, int seeds)
        {
            for (var seed = 1; seed <= seeds; seed++)
            {
                var rng = new Core.Session.DeterministicRandom(seed);
                var resolver = new ObjectiveResolver(
                    map.Catalog, map.World, map.PositionOf(map.EntranceNode), rng);
                if (!resolver.VerifyChainConverges())
                {
                    return seed;
                }
            }

            return -1;
        }

        private static int RunOneMatch(string[] args)
        {
            var seed = IntArg(args, "--seed", 1);
            var map = SimMap.Build();
            var sink = new InMemoryTelemetrySink();
            var result = new MatchSimulator(map, seed, Overrides(args), sink, Scenario(args)).Run();

            Console.WriteLine("seed " + result.Seed
                + "  outcome " + result.Outcome
                + "  " + (result.DurationSeconds / 60f).ToString("0.0", CultureInfo.InvariantCulture) + " min"
                + "  round trips " + result.RoundTrips + " (planned " + result.PlannedRoundTrips + ")"
                + "  tier " + result.FinalTierIndex);
            Console.WriteLine("  clues " + result.Summary.CluesRead + " read / " + result.Summary.ClueMisreads
                + " misread   pinned=" + result.ObjectivePinned + "   wrong sites " + result.WrongSiteVisits);
            Console.WriteLine("  credits earned " + result.CreditsEarned + "  spent " + result.CreditsSpent
                + "  unspent " + result.CreditsUnspent
                + "  loot " + result.LootSold + " sold / " + result.LootLeftBehind + " left");
            Console.WriteLine("  chases " + result.Chases + " (" + result.ChaseEscapes + " broken)"
                + "  deaths " + result.Summary.PlayersDied
                + "  escaped " + result.Summary.PlayersEscaped);

            return 0;
        }

        private static int Replay(string[] args)
        {
            // §13's whole diagnosis loop is "replay the seed". This command proves the
            // loop closes: the same seed twice must produce identical summaries.
            var seed = IntArg(args, "--seed", 1);
            var repeats = IntArg(args, "--times", 3);
            var map = SimMap.Build();

            SimMatchResult? first = null;
            for (var i = 0; i < repeats; i++)
            {
                var sink = new InMemoryTelemetrySink();
                var result = new MatchSimulator(map, seed, Overrides(args), sink, Scenario(args)).Run();

                if (first == null)
                {
                    first = result;
                    continue;
                }

                if (!Identical(first.Value, result))
                {
                    Console.Error.WriteLine("Seed " + seed + " did not replay identically on run " + (i + 1) + ".");
                    return 3;
                }
            }

            Console.WriteLine("Seed " + seed + " replayed identically " + repeats + " times: "
                + first!.Value.Outcome + ", "
                + (first.Value.DurationSeconds / 60f).ToString("0.00", CultureInfo.InvariantCulture) + " min, "
                + first.Value.CreditsEarned + " credits earned, "
                + first.Value.Summary.CluesRead + " clues read.");
            return 0;
        }

        private static int RunPopulation(string[] args)
        {
            var matches = IntArg(args, "--matches", 500);
            var seed = IntArg(args, "--seed", 1);
            var map = SimMap.Build();
            var scenario = Scenario(args);

            BalanceOverrides.SelfCheck();

            var point = SweepRunner.RunPopulation(
                map, seed, matches, Overrides(args), "seeds " + seed + "…" + (seed + matches - 1),
                scenario);

            Console.Write(DescribeTheBuilding(map));
            Console.WriteLine();

            if (!scenario.IsDefault)
            {
                // A run under a scenario is not the shipped game, and F-006 has already
                // cost this project one round of numbers quoted against a thing the game
                // does not have. Say so in the output rather than in the command line
                // the reader no longer has.
                Console.WriteLine("=== scenario (NOT the shipped game): " + scenario);
                Console.WriteLine();
            }

            Console.Write(point.Report.Describe(point.Sink));
            return 0;
        }

        private static int Sweep(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine(
                    "sweep needs an axis: weight-mul-light | loot-value | dwell | tier-minutes | start-cells");
                return 1;
            }

            var matches = IntArg(args, "--matches", 500);
            var seed = IntArg(args, "--seed", 1);
            var verbose = HasFlag(args, "--verbose");
            var map = SimMap.Build();
            var scenario = Scenario(args);

            BalanceOverrides.SelfCheck();

            IReadOnlyList<SweepPoint> points;
            string axis;

            // F-001's axes print F-001's columns and F-006's axes print F-006's. The
            // two findings ask different questions of the same population and a row wide
            // enough for both is a row nobody reads.
            var lengthTable = true;

            switch (args[1])
            {
                case "weight-mul-light":
                    axis = "WeightMulLight";
                    lengthTable = false;
                    points = SweepRunner.WeightMulLight(
                        map, seed, matches, Values(args, DefaultWeightValues()), scenario);
                    break;

                case "loot-value":
                    axis = "lootValue×";
                    lengthTable = false;
                    points = SweepRunner.LootValueScale(
                        map, seed, matches, Values(args, DefaultLootValues()), scenario);
                    break;

                case "dwell":
                    axis = "§07 costs ×";
                    points = SweepRunner.DwellScale(
                        map, seed, matches, Values(args, DefaultDwellValues()), scenario);
                    break;

                case "tier-minutes":
                    axis = "tier (min)";
                    points = SweepRunner.TierMinutes(
                        map, seed, matches, Values(args, DefaultTierValues()), scenario);
                    break;

                case "start-cells":
                    axis = "spare cells";
                    points = SweepRunner.StartingCells(
                        map, seed, matches, Values(args, DefaultCellValues()), scenario);
                    break;

                default:
                    Console.Error.WriteLine("Unknown sweep axis '" + args[1] + "'.");
                    return 1;
            }

            Console.Write(DescribeTheBuilding(map));
            Console.WriteLine();
            Console.WriteLine("sweep " + args[1] + " — " + matches + " matches per point, seeds "
                + seed + "…" + (seed + matches - 1) + ", identical across points");

            if (!scenario.IsDefault)
            {
                Console.WriteLine("held fixed across every point: " + scenario);
            }

            Console.WriteLine();
            Console.Write(lengthTable
                ? SweepRunner.LengthTable(axis, points)
                : SweepRunner.Table(axis, points));

            if (verbose)
            {
                Console.WriteLine();
                foreach (var point in points)
                {
                    Console.Write(point.Report.Describe(point.Sink));
                }
            }

            return 0;
        }

        // ====================================================================

        private static bool Identical(SimMatchResult a, SimMatchResult b) =>
            a.Outcome == b.Outcome
            && a.DurationSeconds == b.DurationSeconds
            && a.RoundTrips == b.RoundTrips
            && a.CreditsEarned == b.CreditsEarned
            && a.CreditsSpent == b.CreditsSpent
            && a.CreditsUnspent == b.CreditsUnspent
            && a.Summary.CluesRead == b.Summary.CluesRead
            && a.Summary.ClueMisreads == b.Summary.ClueMisreads
            && a.Summary.PlayersDied == b.Summary.PlayersDied
            && a.Summary.TotalAggroSeconds == b.Summary.TotalAggroSeconds
            && a.Summary.AggroEvents == b.Summary.AggroEvents
            && a.LootSold == b.LootSold
            && a.WrongSiteVisits == b.WrongSiteVisits;

        private static float[] DefaultWeightValues() =>
            new[] { GameConstants.WeightMulLight, 0.86f, 0.88f, 0.90f, 0.92f, 0.95f };

        private static float[] DefaultLootValues() =>
            new[] { 0.5f, 0.75f, 1f, 1.5f, 2f, 3f };

        // 0 is the simulator as F-006 measured it; 1 is §07's action-cost table charged
        // exactly as written; the rest bracket a team slower than the designer expects,
        // which is the whole human-overhead question.
        private static float[] DefaultDwellValues() =>
            new[] { 0f, 0.5f, 1f, 1.5f, 2f, 3f };

        // 8 is §07's table as written. The rest compress it towards the match the game
        // actually produces — F-006 option 2.
        private static float[] DefaultTierValues() =>
            new[] { 8f, 6f, 4f, 3f, 2f, 1.5f };

        // §08's 맨몸 is 0. Above 3 the light stops being a constraint at all, which is
        // worth seeing rather than assuming.
        private static float[] DefaultCellValues() =>
            new[] { 0f, 1f, 2f, 3f, 4f };

        /// <summary>
        /// Everything the command line can say that is not a
        /// <see cref="GameConstants"/> value — §07's action costs, §07's clock rate, the
        /// bootstrap grubstake, and the hour the match starts at. Defaults to
        /// <see cref="SimScenario.Default"/>, which is the shipped game.
        /// </summary>
        private static SimScenario Scenario(string[] args)
        {
            var scenario = SimScenario.Default
                .WithStartSeconds(FloatArg(args, "--start-minutes", 0f) * 60f)
                .WithStartingSpareCells(IntArg(args, "--start-cells", 0));

            var tier = FloatArg(args, "--tier-minutes", float.NaN);
            if (!float.IsNaN(tier))
            {
                scenario = scenario.WithTierMinutes(tier);
            }

            // One flag for §07's whole table, because the four costs are one claim about
            // how long a person takes and sweeping them independently would answer a
            // question the design document does not ask.
            var dwell = FloatArg(args, "--dwell", float.NaN);
            if (!float.IsNaN(dwell))
            {
                var table = SimScenario.SevenTable.ScaledDwell(dwell);
                scenario = scenario
                    .WithSiteSearchSeconds(table.SiteSearchSeconds)
                    .WithLootPickupSeconds(table.LootPickupSeconds)
                    .WithSurfaceTransitSeconds(table.SurfaceTransitSeconds)
                    .WithShopSeconds(table.ShopSeconds);
            }

            // …and then each cost individually, for the cases where a designer wants to
            // know which row of §07's table is carrying the result.
            var search = FloatArg(args, "--search-seconds", float.NaN);
            if (!float.IsNaN(search))
            {
                scenario = scenario.WithSiteSearchSeconds(search);
            }

            var pickup = FloatArg(args, "--pickup-seconds", float.NaN);
            if (!float.IsNaN(pickup))
            {
                scenario = scenario.WithLootPickupSeconds(pickup);
            }

            var climb = FloatArg(args, "--climb-seconds", float.NaN);
            if (!float.IsNaN(climb))
            {
                scenario = scenario.WithSurfaceTransitSeconds(climb);
            }

            var shop = FloatArg(args, "--shop-seconds", float.NaN);
            if (!float.IsNaN(shop))
            {
                scenario = scenario.WithShopSeconds(shop);
            }

            return scenario;
        }

        private static BalanceOverrides Overrides(string[] args)
        {
            var overrides = BalanceOverrides.Default;

            var weight = FloatArg(args, "--weight-mul-light", float.NaN);
            if (!float.IsNaN(weight))
            {
                overrides = overrides.WithWeightMulLight(weight);
            }

            var loot = FloatArg(args, "--loot-value", float.NaN);
            if (!float.IsNaN(loot))
            {
                overrides = overrides.WithLootValueScale(loot);
            }

            return overrides;
        }

        private static IReadOnlyList<float> Values(string[] args, float[] fallback)
        {
            var raw = StringArg(args, "--values", null);
            if (raw == null)
            {
                return fallback;
            }

            var parts = raw.Split(',');
            var values = new List<float>(parts.Length);
            foreach (var part in parts)
            {
                if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    values.Add(value);
                }
            }

            return values.Count > 0 ? values : (IReadOnlyList<float>)fallback;
        }

        private static bool HasFlag(string[] args, string name)
        {
            foreach (var arg in args)
            {
                if (arg == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static string? StringArg(string[] args, string name, string? fallback)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return fallback;
        }

        private static int IntArg(string[] args, string name, int fallback)
        {
            var raw = StringArg(args, name, null);
            return raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private static float FloatArg(string[] args, string name, float fallback)
        {
            var raw = StringArg(args, name, null);
            return raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("horrorsim — headless balance simulator");
            Console.WriteLine();
            Console.WriteLine("Usage: horrorsim <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  validate                 Check the tuned constants, the override shadow and the map");
            Console.WriteLine("  map                      Print the §12 map, its validation report and the 주자 테스트");
            Console.WriteLine("  match  --seed N          Run one match and print it");
            Console.WriteLine("  replay --seed N [--times K]");
            Console.WriteLine("                           Prove a seed replays identically (§13)");
            Console.WriteLine("  run    --matches N --seed S");
            Console.WriteLine("                           Run a population and print the design's own scorecard");
            Console.WriteLine("  sweep  <axis> [--matches N] [--seed S] [--values a,b,c] [--verbose]");
            Console.WriteLine("                           Vary one constant across a range over seeded matches");
            Console.WriteLine();
            Console.WriteLine("Sweep axes:");
            Console.WriteLine("  weight-mul-light         §08's band-2 multiplier — BALANCE-FINDINGS F-001");
            Console.WriteLine("  loot-value               §16-2's 전리품 가치 ↔ 아이템 가격 ratio");
            Console.WriteLine("  dwell                    §07's action-cost table charged as dwell — F-006 option 3");
            Console.WriteLine("  tier-minutes             Length of one §07 threat band — F-006 option 2");
            Console.WriteLine("  start-cells              Spare batteries carried in — F-006's bootstrap");
            Console.WriteLine();
            Console.WriteLine("Options usable on match/replay/run/sweep:");
            Console.WriteLine("  --weight-mul-light X     Shadow GameConstants.WeightMulLight");
            Console.WriteLine("  --loot-value X           Scale what 전리품 fetches at the vehicle");
            Console.WriteLine("  --start-minutes M        Wind §07's clock forward before the first descent");
            Console.WriteLine("  --start-cells N          Spare battery cells each player carries in (§08 맨몸 = 0)");
            Console.WriteLine("  --tier-minutes M         Length of one §07 threat band (§07 as written = 8)");
            Console.WriteLine("  --dwell X                Charge §07's action costs, scaled (0 = shipped sim, 1 = §07)");
            Console.WriteLine("  --search-seconds S       …or set one row: searching a 후보 지점");
            Console.WriteLine("  --pickup-seconds S       …picking up one 전리품");
            Console.WriteLine("  --climb-seconds S        …the climb out, each way");
            Console.WriteLine("  --shop-seconds S         …상점에서 고민");
        }
    }
}
