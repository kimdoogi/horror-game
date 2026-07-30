using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using HorrorGame.Core.Telemetry;

namespace HorrorGame.Sim
{
    /// <summary>One point on a sweep: the value that was varied, and what came out.</summary>
    public sealed class SweepPoint
    {
        /// <summary>Builds a point.</summary>
        public SweepPoint(float value, SimReport report, InMemoryTelemetrySink sink)
        {
            Value = value;
            Report = report;
            Sink = sink;
        }

        /// <summary>The swept constant's value at this point.</summary>
        public float Value { get; }

        /// <summary>The population that ran at it.</summary>
        public SimReport Report { get; }

        /// <summary>§13's counters for that population.</summary>
        public InMemoryTelemetrySink Sink { get; }
    }

    /// <summary>
    /// Runs the same seeds at several values of one constant.
    /// <para>
    /// The seeds are held fixed across the sweep on purpose. §16-2 asks what a price
    /// change does, and comparing two populations drawn from different seeds would
    /// answer with the variance between them. Same seeds, one knob moved, difference
    /// attributable.
    /// </para>
    /// <para>
    /// Matches are independent — each owns its own <see cref="Core.Session.DeterministicRandom"/>,
    /// its own sink and its own <see cref="MatchSimulator"/> — so they are run in
    /// parallel and merged back in seed order. The merge is ordered, so the report is
    /// byte-identical run to run regardless of how the work was scheduled.
    /// </para>
    /// </summary>
    public static class SweepRunner
    {
        /// <summary>Runs one population and returns it with its telemetry.</summary>
        /// <param name="map">The fixed building.</param>
        /// <param name="baseSeed">First seed; the population is <paramref name="matches"/> consecutive seeds.</param>
        /// <param name="matches">How many matches to run.</param>
        /// <param name="overrides">The shadowed constants for this population.</param>
        /// <param name="label">Header for the report.</param>
        public static SweepPoint RunPopulation(
            SimMap map, int baseSeed, int matches, BalanceOverrides overrides, string label,
            float startSeconds = 0f)
        {
            var results = new SimMatchResult[matches];
            var sinks = new InMemoryTelemetrySink[matches];

            Parallel.For(0, matches, i =>
            {
                var sink = new InMemoryTelemetrySink();
                var sim = new MatchSimulator(map, baseSeed + i, overrides, sink, startSeconds);
                results[i] = sim.Run();
                sinks[i] = sink;
            });

            var merged = new InMemoryTelemetrySink();
            for (var i = 0; i < matches; i++)
            {
                Merge(merged, sinks[i]);
            }

            return new SweepPoint(0f, new SimReport(results, label), merged);
        }

        /// <summary>
        /// Sweeps §08's band-2 multiplier — <c>docs/BALANCE-FINDINGS.md</c> F-001.
        /// </summary>
        public static IReadOnlyList<SweepPoint> WeightMulLight(
            SimMap map, int baseSeed, int matches, IReadOnlyList<float> values, float startSeconds)
        {
            var points = new List<SweepPoint>();
            foreach (var value in values)
            {
                var overrides = BalanceOverrides.Default.WithWeightMulLight(value);
                var label = "WeightMulLight = " + value.ToString("0.00", CultureInfo.InvariantCulture);
                var point = RunPopulation(map, baseSeed, matches, overrides, label, startSeconds);
                points.Add(new SweepPoint(value, point.Report, point.Sink));
            }

            return points;
        }

        /// <summary>
        /// Sweeps §16-2's ratio: what 전리품 fetches, against §08's unchanged prices.
        /// </summary>
        public static IReadOnlyList<SweepPoint> LootValueScale(
            SimMap map, int baseSeed, int matches, IReadOnlyList<float> values, float startSeconds)
        {
            var points = new List<SweepPoint>();
            foreach (var value in values)
            {
                var overrides = BalanceOverrides.Default.WithLootValueScale(value);
                var label = "loot value × " + value.ToString("0.00", CultureInfo.InvariantCulture);
                var point = RunPopulation(map, baseSeed, matches, overrides, label, startSeconds);
                points.Add(new SweepPoint(value, point.Report, point.Sink));
            }

            return points;
        }

        /// <summary>
        /// A one-line-per-point table. The comparison §16-2 actually needs is between
        /// rows, so the sweep prints the rows next to each other rather than printing
        /// a full report per point.
        /// </summary>
        /// <param name="axis">Name of the swept constant.</param>
        /// <param name="points">The points, in sweep order.</param>
        public static string Table(string axis, IReadOnlyList<SweepPoint> points)
        {
            var text = new StringBuilder();

            text.Append(axis.PadRight(16))
                .Append("clear   partial survive wipe    aban    ")
                .AppendLine("len_med trips   deaths  earned  spent   unspent up@desc rChase  runEsc  rChaseL runEscL");

            foreach (var point in points)
            {
                var r = point.Report;
                text.Append(point.Value.ToString("0.00", CultureInfo.InvariantCulture).PadRight(16));
                text.Append(Cell(r.ShareOf(Core.Match.MatchOutcome.FullVictory) * 100f));
                text.Append(Cell(r.ShareOf(Core.Match.MatchOutcome.PartialVictory) * 100f));
                text.Append(Cell(r.ShareOf(Core.Match.MatchOutcome.Survived) * 100f));
                text.Append(Cell(r.ShareOf(Core.Match.MatchOutcome.Wiped) * 100f));
                text.Append(Cell(r.ShareOf(Core.Match.MatchOutcome.Abandoned) * 100f));
                text.Append(Cell(r.Percentile(m => m.DurationSeconds, 0.5f) / 60f));
                text.Append(Cell(r.Mean(m => m.RoundTrips)));
                text.Append(Cell(r.Mean(m => m.Summary.PlayersDied)));
                text.Append(Cell(r.Mean(m => m.CreditsEarned)));
                text.Append(Cell(r.Mean(m => m.CreditsSpent)));
                text.Append(Cell(r.Mean(m => m.CreditsUnspent)));
                text.Append(Cell(MeanUpgradeDescent(r)));

                // The two escape rates are the numbers F-001 turns on, so their
                // denominators travel with them — a rate over forty chases is not
                // evidence, and a reader must be able to see that at a glance.
                text.Append(Cell(r.Total(m => m.RunnerChases)));
                text.Append(Cell(Ratio(r, m => m.RunnerEscapes, m => m.RunnerChases) * 100f));
                text.Append(Cell(r.Total(m => m.RunnerChasesLoaded)));
                text.Append(Cell(Ratio(r, m => m.RunnerEscapesLoaded, m => m.RunnerChasesLoaded) * 100f));
                text.AppendLine();
            }

            return text.ToString();
        }

        private static float MeanUpgradeDescent(SimReport report)
        {
            // Mean descent index of the first 강화 아이템, over the matches that bought
            // one at all. Zero means nobody ever could.
            var total = report.Mean(m => m.FirstUpgradeDescent >= 0 ? m.FirstUpgradeDescent : 0);
            var share = report.Share(m => m.FirstUpgradeDescent >= 0);
            return share <= 0f ? 0f : total / share;
        }

        private static float Ratio(
            SimReport report, Func<SimMatchResult, int> numerator, Func<SimMatchResult, int> denominator)
        {
            var top = report.Total(numerator);
            var bottom = report.Total(denominator);
            return bottom == 0 ? 0f : top / (float)bottom;
        }

        private static string Cell(float value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture).PadRight(8);

        private static void Merge(InMemoryTelemetrySink into, InMemoryTelemetrySink from)
        {
            foreach (var pair in from.Counters)
            {
                into.Increment(pair.Key, pair.Value);
            }

            foreach (var summary in from.Summaries)
            {
                into.RecordMatchSummary(summary);
            }
        }
    }
}
