using System;
using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Telemetry;

namespace HorrorGame.Sim
{
    /// <summary>
    /// Command dispatch for the simulator. Kept separate from
    /// <see cref="Program"/> so the sweeps are reachable from tests.
    /// </summary>
    public static class SimCommands
    {
        /// <summary>Dispatches a subcommand.</summary>
        /// <param name="args">Arguments as passed on the command line.</param>
        /// <returns>A process exit code.</returns>
        public static int Run(string[] args)
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

            return 0;
        }

        private static int DescribeMap()
        {
            var map = SimMap.Build();
            Console.WriteLine(map.Graph.ToString());
            Console.WriteLine(MapValidator.Validate(map.Graph).Describe());
            Console.WriteLine(RunnerTest.Run(map.Graph, new Core.Session.DeterministicRandom(1)).Describe());
            return 0;
        }

        private static int RunOneMatch(string[] args)
        {
            var seed = IntArg(args, "--seed", 1);
            var map = SimMap.Build();
            var sink = new InMemoryTelemetrySink();
            var result = new MatchSimulator(map, seed, Overrides(args), sink, StartSeconds(args)).Run();

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
                var result = new MatchSimulator(map, seed, Overrides(args), sink, StartSeconds(args)).Run();

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

            BalanceOverrides.SelfCheck();

            var point = SweepRunner.RunPopulation(
                map, seed, matches, Overrides(args), "seeds " + seed + "…" + (seed + matches - 1),
                StartSeconds(args));

            Console.Write(point.Report.Describe(point.Sink));
            return 0;
        }

        private static int Sweep(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("sweep needs an axis: weight-mul-light | loot-value");
                return 1;
            }

            var matches = IntArg(args, "--matches", 500);
            var seed = IntArg(args, "--seed", 1);
            var verbose = HasFlag(args, "--verbose");
            var map = SimMap.Build();

            BalanceOverrides.SelfCheck();

            IReadOnlyList<SweepPoint> points;
            string axis;

            switch (args[1])
            {
                case "weight-mul-light":
                    axis = "WeightMulLight";
                    points = SweepRunner.WeightMulLight(
                        map, seed, matches, Values(args, DefaultWeightValues()), StartSeconds(args));
                    break;

                case "loot-value":
                    axis = "lootValue×";
                    points = SweepRunner.LootValueScale(
                        map, seed, matches, Values(args, DefaultLootValues()), StartSeconds(args));
                    break;

                default:
                    Console.Error.WriteLine("Unknown sweep axis '" + args[1] + "'.");
                    return 1;
            }

            Console.WriteLine("sweep " + args[1] + " — " + matches + " matches per point, seeds "
                + seed + "…" + (seed + matches - 1) + ", identical across points");
            Console.WriteLine();
            Console.Write(SweepRunner.Table(axis, points));

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

        /// <summary>
        /// §07's clock, wound forward before the match starts. A scenario knob, not a
        /// balance value: F-001's arithmetic is about a 4.8 m/s monster, which §07
        /// does not produce until 심야, so a question about the weight table cannot be
        /// answered by matches that end in 초저녁.
        /// </summary>
        private static float StartSeconds(string[] args) =>
            FloatArg(args, "--start-minutes", 0f) * 60f;

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
            Console.WriteLine();
            Console.WriteLine("Options usable on match/replay/run:");
            Console.WriteLine("  --weight-mul-light X     Shadow GameConstants.WeightMulLight");
            Console.WriteLine("  --loot-value X           Scale what 전리품 fetches at the vehicle");
            Console.WriteLine("  --start-minutes M        Wind §07's clock forward before the first descent");
        }
    }
}
