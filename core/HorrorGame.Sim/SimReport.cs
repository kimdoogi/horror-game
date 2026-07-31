using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Match;
using HorrorGame.Core.Telemetry;

namespace HorrorGame.Sim
{
    /// <summary>
    /// A population of simulated matches, summarised against the design's own
    /// targets rather than against a general notion of "balanced".
    /// <para>
    /// Every line here is a claim the document makes about itself: §01's 25~35분,
    /// §02's four outcomes, §03's 2~5 왕복, §07's threat curve actually biting, §08's
    /// growth curve. A simulator that reported means and standard deviations would be
    /// leaving the reader to do the comparison the design has already specified.
    /// </para>
    /// </summary>
    public sealed class SimReport
    {
        private readonly List<SimMatchResult> _matches;

        /// <summary>Aggregates a set of matches. The list is kept in seed order so the report is reproducible.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="matches"/> is null.</exception>
        public SimReport(IReadOnlyList<SimMatchResult> matches, string label)
        {
            if (matches == null)
            {
                throw new ArgumentNullException(nameof(matches));
            }

            _matches = new List<SimMatchResult>(matches);
            Label = label;
        }

        /// <summary>What this population is, for the report header.</summary>
        public string Label { get; }

        /// <summary>How many matches were run.</summary>
        public int Count => _matches.Count;

        /// <summary>Share of matches ending in one of §02's rows.</summary>
        public float ShareOf(MatchOutcome outcome) => Share(m => m.Outcome == outcome);

        /// <summary>Share of matches inside §01's 25~35분 window.</summary>
        public float ShareInMatchLengthWindow => Share(m =>
            m.DurationSeconds >= GameConstants.TargetMatchSecondsMin
            && m.DurationSeconds <= GameConstants.TargetMatchSecondsMax);

        /// <summary>Share of matches inside §03's 2~5 왕복.</summary>
        public float ShareInRoundTripWindow => Share(m =>
            m.RoundTrips >= GameConstants.ExpectedRoundTripsMin
            && m.RoundTrips <= GameConstants.ExpectedRoundTripsMax);

        /// <summary>Mean of a per-match figure.</summary>
        public float Mean(Func<SimMatchResult, float> pick)
        {
            if (_matches.Count == 0)
            {
                return 0f;
            }

            var total = 0.0;
            foreach (var m in _matches)
            {
                total += pick(m);
            }

            return (float)(total / _matches.Count);
        }

        /// <summary>Share of matches satisfying a predicate.</summary>
        public float Share(Func<SimMatchResult, bool> predicate)
        {
            if (_matches.Count == 0)
            {
                return 0f;
            }

            var hits = 0;
            foreach (var m in _matches)
            {
                if (predicate(m))
                {
                    hits++;
                }
            }

            return hits / (float)_matches.Count;
        }

        /// <summary>A percentile of a per-match figure, nearest-rank.</summary>
        public float Percentile(Func<SimMatchResult, float> pick, float fraction)
        {
            if (_matches.Count == 0)
            {
                return 0f;
            }

            var values = new List<float>(_matches.Count);
            foreach (var m in _matches)
            {
                values.Add(pick(m));
            }

            values.Sort();
            var index = (int)MathF.Round(MathF.Max(0f, MathF.Min(1f, fraction)) * (values.Count - 1));
            return values[index];
        }

        /// <summary>
        /// A percentile over the sub-population a filter keeps, nearest-rank. Zero when
        /// the filter keeps nothing.
        /// </summary>
        /// <param name="pick">The per-match figure.</param>
        /// <param name="fraction">0 is the smallest kept value, 1 the largest.</param>
        /// <param name="keep">Which matches count. A population split is a claim; naming it here keeps it one line.</param>
        public float PercentileWhere(
            Func<SimMatchResult, float> pick, float fraction, Func<SimMatchResult, bool> keep)
        {
            var values = new List<float>(_matches.Count);
            foreach (var m in _matches)
            {
                if (keep(m))
                {
                    values.Add(pick(m));
                }
            }

            if (values.Count == 0)
            {
                return 0f;
            }

            values.Sort();
            var index = (int)MathF.Round(MathF.Max(0f, MathF.Min(1f, fraction)) * (values.Count - 1));
            return values[index];
        }

        /// <summary>Share of a sub-population satisfying a predicate. Zero when the filter keeps nothing.</summary>
        /// <param name="predicate">What is being counted.</param>
        /// <param name="keep">Which matches are in the denominator.</param>
        public float ShareWhere(Func<SimMatchResult, bool> predicate, Func<SimMatchResult, bool> keep)
        {
            var kept = 0;
            var hits = 0;
            foreach (var m in _matches)
            {
                if (!keep(m))
                {
                    continue;
                }

                kept++;
                if (predicate(m))
                {
                    hits++;
                }
            }

            return kept == 0 ? 0f : hits / (float)kept;
        }

        /// <summary>Total of a per-match integer across the population.</summary>
        public long Total(Func<SimMatchResult, int> pick)
        {
            long total = 0;
            foreach (var m in _matches)
            {
                total += pick(m);
            }

            return total;
        }

        /// <summary>The design's own scorecard, as text.</summary>
        /// <param name="sink">The telemetry the same matches produced, for §13's buckets.</param>
        public string Describe(InMemoryTelemetrySink? sink)
        {
            var text = new StringBuilder();

            text.Append("=== ").Append(Label).Append(" — ").Append(Count).AppendLine(" matches");
            text.AppendLine();

            text.AppendLine("§02 outcome mix");
            AppendRow(text, "  완전 승리 (clear)", Pct(ShareOf(MatchOutcome.FullVictory)));
            AppendRow(text, "  부분 승리 (partial)", Pct(ShareOf(MatchOutcome.PartialVictory)));
            AppendRow(text, "  생존 (survived)", Pct(ShareOf(MatchOutcome.Survived)));
            AppendRow(text, "  패배 (wipe)", Pct(ShareOf(MatchOutcome.Wiped)));
            AppendRow(text, "  포기 (abandoned)", Pct(ShareOf(MatchOutcome.Abandoned)));
            AppendRow(text, "  objective recovered", Pct(Share(m => m.Summary.ObjectiveRecovered)));
            text.AppendLine();

            text.AppendLine("§01 match length — target 25~35 min");
            AppendRow(text, "  median", Minutes(Percentile(m => m.DurationSeconds, 0.5f)));
            AppendRow(text, "  p10 / p90", Minutes(Percentile(m => m.DurationSeconds, 0.1f))
                + " / " + Minutes(Percentile(m => m.DurationSeconds, 0.9f)));
            AppendRow(text, "  inside the window", Pct(ShareInMatchLengthWindow));
            AppendRow(text, "  hit the sim's 40-min cap", Pct(Share(m => m.HitTimeCap)));

            // §03's battery is the reason to come out and §08 sells the reason to go
            // back in — so a team that surfaces with an empty wallet and no light has
            // no second descent. On a building this size that is a whole population of
            // short matches, and quoting the median without it would be quoting the
            // median of two different games (docs/BALANCE-FINDINGS.md F-006).
            AppendRow(text, "  ended with every light dead", Pct(Share(m => m.EndedOutOfLight)));
            AppendRow(text, "  median of the rest",
                Minutes(PercentileWhere(m => m.DurationSeconds, 0.5f, m => !m.EndedOutOfLight)));
            AppendRow(text, "  inside the window, of the rest",
                Pct(ShareWhere(
                    m => m.DurationSeconds >= GameConstants.TargetMatchSecondsMin
                         && m.DurationSeconds <= GameConstants.TargetMatchSecondsMax,
                    m => !m.EndedOutOfLight)));
            text.AppendLine();

            text.AppendLine("§03 round trips — target 2~5");
            AppendRow(text, "  mean actual", Num(Mean(m => m.RoundTrips)));
            AppendRow(text, "  mean planned by the layout", Num(Mean(m => m.PlannedRoundTrips)));
            AppendRow(text, "  inside the window", Pct(ShareInRoundTripWindow));
            AppendRow(text, "  chain pinned a site", Pct(Share(m => m.ObjectivePinned)));
            AppendRow(text, "  walked to a site and found nothing", Num(Mean(m => m.WrongSiteVisits)));
            AppendRow(text, "  clue reads / misreads per match",
                Num(Mean(m => m.Summary.CluesRead)) + " / " + Num(Mean(m => m.Summary.ClueMisreads)));
            text.AppendLine();

            text.AppendLine("§07 threat curve");
            AppendRow(text, "  mean tier at end (0=초저녁 … 4=동트기 전)", Num(Mean(m => m.FinalTierIndex)));
            // Each of §07's five tiers named separately, because F-006 is a claim about
            // how many of them anybody ever sees. One "심야 or later" row cannot say
            // whether 새벽's "괴물이 출입구를 안다" is content or decoration.
            AppendRow(text, "  reached 심야 or later (tier 2, 16 min)", Pct(Share(m => m.FinalTierIndex >= 2)));
            AppendRow(text, "  reached 새벽 or later (tier 3, 24 min)", Pct(Share(m => m.FinalTierIndex >= 3)));
            AppendRow(text, "  reached 동트기 전 (tier 4, 32 min)", Pct(Share(m => m.FinalTierIndex >= 4)));
            AppendRow(text, "  chases per match", Num(Mean(m => m.Chases)));
            AppendRow(text, "  chases broken", Pct(RatioOf(m => m.ChaseEscapes, m => m.Chases)));
            AppendRow(text, "  mean aggro seconds", Num(Mean(m => m.Summary.TotalAggroSeconds)));
            AppendRow(text, "  longest aggro seen", Num(Percentile(m => m.Summary.LongestAggroSeconds, 1f)));
            AppendRow(text, "  deaths per match", Num(Mean(m => m.Summary.PlayersDied)));
            text.AppendLine();

            text.AppendLine("§06 · §08 the Runner's escape (F-001's population)");
            AppendRow(text, "  chases on the 주자", Num(Mean(m => m.RunnerChases)));
            AppendRow(text, "  broken", Pct(RatioOf(m => m.RunnerEscapes, m => m.RunnerChases)));
            AppendRow(text, "  of those, 주자 was in weight band 2+", Num(Mean(m => m.RunnerChasesLoaded)));
            AppendRow(text, "  broken while loaded", Pct(RatioOf(m => m.RunnerEscapesLoaded, m => m.RunnerChasesLoaded)));
            AppendRow(text, "  peak weight band reached by anyone", Num(Mean(m => m.PeakWeightBand)));
            text.AppendLine();

            text.AppendLine("§08 · §16-2 the economy");
            AppendRow(text, "  buying power before the 1st descent", Num(Mean(m => m.CreditsBeforeFirstDescent))
                + "  (§08 requires 0)");
            AppendRow(text, "  credits after the 1st return", Num(Mean(m => m.CreditsAfterFirstReturn)));
            AppendRow(text, "  could afford a 소모품 after the 1st return",
                Pct(Share(m => m.CreditsAfterFirstReturn >= ShopCatalogue.CheapestCost)));
            AppendRow(text, "  could afford a 강화 아이템 after the 1st return",
                Pct(Share(m => m.CreditsAfterFirstReturn >= ShopCatalogue.CheapestUpgradeCost)));
            AppendRow(text, "  bought a 강화 아이템 at all", Pct(Share(m => m.FirstUpgradeDescent >= 0)));
            AppendRow(text, "  … on descent #", Num(MeanWhere(m => m.FirstUpgradeDescent, m => m.FirstUpgradeDescent >= 0)));
            AppendRow(text, "  earned per match", Num(Mean(m => m.CreditsEarned)));
            AppendRow(text, "  spent per match", Num(Mean(m => m.CreditsSpent)));
            AppendRow(text, "  unspent at the end", Num(Mean(m => m.CreditsUnspent)));
            AppendRow(text, "  earned ÷ cost of one of everything ("
                + ShopCatalogue.CostOfOneOfEverything.ToString(CultureInfo.InvariantCulture) + ")",
                Num(Mean(m => m.CreditsEarned) / ShopCatalogue.CostOfOneOfEverything));
            AppendRow(text, "  loot pieces sold / left behind",
                Num(Mean(m => m.LootSold)) + " / " + Num(Mean(m => m.LootLeftBehind)));
            text.AppendLine();

            if (sink != null)
            {
                text.AppendLine("§13 telemetry buckets, as the shipped build would send them");
                AppendBuckets(text, sink, TelemetryBuckets.RoundTripsBuckets);
                AppendBuckets(text, sink, TelemetryBuckets.AggroDurationBuckets);
                AppendBuckets(text, sink, TelemetryBuckets.BackpedalShareBuckets);
                AppendBuckets(text, sink, TelemetryBuckets.OutcomeCounters);
                AppendBuckets(text, sink, TelemetryBuckets.RolePickCounters);
                AppendBuckets(text, sink, TelemetryBuckets.PurchaseCounters);
                text.AppendLine();
            }

            return text.ToString();
        }

        private float MeanWhere(Func<SimMatchResult, float> pick, Func<SimMatchResult, bool> predicate)
        {
            var total = 0.0;
            var count = 0;
            foreach (var m in _matches)
            {
                if (predicate(m))
                {
                    total += pick(m);
                    count++;
                }
            }

            return count == 0 ? 0f : (float)(total / count);
        }

        private float RatioOf(Func<SimMatchResult, int> numerator, Func<SimMatchResult, int> denominator)
        {
            var top = Total(numerator);
            var bottom = Total(denominator);
            return bottom == 0 ? 0f : top / (float)bottom;
        }

        private static void AppendBuckets(
            StringBuilder text, InMemoryTelemetrySink sink, IReadOnlyList<string> counters)
        {
            for (var i = 0; i < counters.Count; i++)
            {
                var count = sink.Count(counters[i]);
                if (count == 0)
                {
                    continue;
                }

                AppendRow(text, "  " + counters[i], count.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AppendRow(StringBuilder text, string label, string value)
        {
            text.Append(label);
            for (var i = label.Length; i < 52; i++)
            {
                text.Append(' ');
            }

            text.Append(' ').AppendLine(value);
        }

        private static string Pct(float value) =>
            (value * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";

        private static string Num(float value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Minutes(float seconds) =>
            (seconds / 60f).ToString("0.0", CultureInfo.InvariantCulture) + " min";
    }
}
