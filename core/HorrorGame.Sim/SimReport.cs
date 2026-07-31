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

        /// <summary>A band of match lengths and the share of the population inside it.</summary>
        public readonly struct LengthWindow
        {
            /// <summary>Builds a window.</summary>
            public LengthWindow(float start, float width, float share)
            {
                Start = start;
                Width = width;
                Share = share;
            }

            /// <summary>Lower edge, in seconds.</summary>
            public float Start { get; }

            /// <summary>How wide the band is, in seconds.</summary>
            public float Width { get; }

            /// <summary>Fraction of matches inside it, 0–1.</summary>
            public float Share { get; }
        }

        /// <summary>
        /// The band of the given width that holds the most matches — the window §01
        /// would have to state if the design were rewritten around what the game
        /// actually produces (F-006's "accept a shorter match").
        /// <para>
        /// Anchored on match lengths that occur rather than on a grid, so the answer is
        /// the real optimum and not an artefact of a bin size. The scan is O(n) after a
        /// sort because both edges only ever move forward.
        /// </para>
        /// </summary>
        /// <param name="widthSeconds">How wide the band is. §01's own is 10 minutes.</param>
        public LengthWindow BestWindow(float widthSeconds)
        {
            if (_matches.Count == 0 || !(widthSeconds > 0f))
            {
                return new LengthWindow(0f, widthSeconds, 0f);
            }

            var lengths = new List<float>(_matches.Count);
            foreach (var m in _matches)
            {
                lengths.Add(m.DurationSeconds);
            }

            lengths.Sort();

            var bestStart = lengths[0];
            var bestCount = 0;
            var high = 0;

            for (var low = 0; low < lengths.Count; low++)
            {
                if (high < low)
                {
                    high = low;
                }

                while (high + 1 < lengths.Count && lengths[high + 1] <= lengths[low] + widthSeconds)
                {
                    high++;
                }

                var count = high - low + 1;
                if (count > bestCount)
                {
                    bestCount = count;
                    bestStart = lengths[low];
                }
            }

            return new LengthWindow(bestStart, widthSeconds, bestCount / (float)lengths.Count);
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

            // F-006 option 5 — "accept a shorter match and rewrite §01 around it" —
            // cannot be judged from a median. It needs the window §01 would have to say
            // instead, which is the 10-minute band this population most lands in, and
            // the share it would then claim. §01's own band is 10 minutes wide.
            var window = BestWindow(GameConstants.TargetMatchSecondsMax - GameConstants.TargetMatchSecondsMin);
            AppendRow(text, "  best 10-min window this population offers",
                Minutes(window.Start) + "~" + Minutes(window.Start + window.Width)
                + " holds " + Pct(window.Share));
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

            AppendActionCosts(text);

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

        /// <summary>
        /// §07's 행동 · 비용 table against what this population actually spent on the
        /// same four actions.
        /// <para>
        /// This block exists because F-006's largest unknown is not in any share of the
        /// population: §07 writes down what an action costs a person, and until this was
        /// measured nothing said what it costs a simulated agent. The right-hand ratio
        /// is the multiplier a real playtest is expected to apply to every match length
        /// on this page — see docs/BALANCE-FINDINGS.md F-006.
        /// </para>
        /// <para>
        /// Agent-seconds, not match seconds: four players act at once, so these sum to
        /// roughly four times the match. That is the correct unit for "what does one
        /// player spend to pick one thing up".
        /// </para>
        /// </summary>
        private void AppendActionCosts(StringBuilder text)
        {
            var sites = Mean(m => m.Ledger.SiteSearches);
            var pickups = Mean(m => m.Ledger.LootPickups);
            var trips = Mean(m => m.Ledger.ShopVisits);
            var players = (float)GameConstants.PlayersPerMatch;

            text.AppendLine("§07 행동 · 비용 — what the design prices, and what these agents spent");
            AppendRow(text, "  후보 지점 searched / 전리품 lifted / 왕복",
                Num(sites) + " / " + Num(pickups) + " / " + Num(trips) + " per match");

            // §07's first two rows are one player's decision — the other three carry on
            // — so they are priced in that player's seconds and measured the same way.
            AppendCost(text, "  한 층 더 탐색 (§07 ~3분 → ~60s per 후보 지점)",
                MeanRatio(m => m.Ledger.ClueWalkSeconds + m.Ledger.ClueStandSeconds,
                    m => m.Ledger.SiteSearches),
                60f);
            AppendCost(text, "  전리품 하나 더 줍기 (§07 ~40초)",
                MeanRatio(m => m.Ledger.LootWalkSeconds + m.Ledger.LootStandSeconds,
                    m => m.Ledger.LootPickups),
                40f);

            // The other two are the whole team's, and §07 prices them in wall-clock with
            // everybody present — so they are compared in match seconds. Quoting the
            // agent-second figure here would count the same stretch four times and make
            // the simulator look four times more generous than it is.
            AppendCost(text, "  나가서 + 상점, match seconds (§07 ~1분 + ~30초)",
                MeanRatio(m => m.Ledger.TeamSurfaceSeconds, m => m.Ledger.ShopVisits),
                90f);
            AppendCost(text, "  …and the walk out to reach the door, per player",
                MeanRatio(m => m.Ledger.ExitWalkSeconds, m => m.Ledger.ShopVisits),
                60f);

            AppendRow(text, "  agent-seconds: 단서 walk / stand",
                Num(Mean(m => m.Ledger.ClueWalkSeconds)) + " / " + Num(Mean(m => m.Ledger.ClueStandSeconds)));
            AppendRow(text, "  agent-seconds: 전리품 walk / stand",
                Num(Mean(m => m.Ledger.LootWalkSeconds)) + " / " + Num(Mean(m => m.Ledger.LootStandSeconds)));
            AppendRow(text, "  agent-seconds: 왕복 walk / at the vehicle",
                Num(Mean(m => m.Ledger.ExitWalkSeconds)) + " / " + Num(Mean(m => m.Ledger.VehicleSeconds)));
            AppendRow(text, "  agent-seconds: fleeing (§06)", Num(Mean(m => m.Ledger.FleeSeconds)));

            // The one number the whole block is for. Everything is converted to
            // agent-seconds so the four rows can be added at all: §07's two team rows
            // have all four players standing there, so they cost the party four times
            // what the clock says.
            var owed = (sites * 60f) + (pickups * 40f) + (trips * 90f * players);
            var spent = Mean(m => m.Ledger.ClueWalkSeconds + m.Ledger.ClueStandSeconds)
                        + Mean(m => m.Ledger.LootWalkSeconds + m.Ledger.LootStandSeconds)
                        + Mean(m => m.Ledger.ExitWalkSeconds + m.Ledger.VehicleSeconds);

            AppendRow(text, "  §07's bill ÷ what was spent, in agent-seconds",
                Num(owed) + " / " + Num(spent) + "  =  ×" + Num(spent <= 0f ? 0f : owed / spent));
            text.AppendLine();
        }

        /// <summary>
        /// One row of the action-cost block: what this population spent, and the factor
        /// that would take it to what §07 says the action costs. Above 1 means the
        /// simulator is charging less than the design does.
        /// </summary>
        private static void AppendCost(StringBuilder text, string label, float measured, float design)
        {
            var ratio = measured <= 0f ? 0f : design / measured;
            AppendRow(text, label,
                Num(measured) + "s measured   ×" + Num(ratio) + " to reach §07");
        }

        /// <summary>
        /// The population's total of a per-match numerator over its total of a per-match
        /// count — not the mean of the per-match ratios, which would weight a match that
        /// picked up one piece as heavily as one that picked up thirty.
        /// </summary>
        private float MeanRatio(Func<SimMatchResult, float> numerator, Func<SimMatchResult, int> count)
        {
            var top = 0.0;
            long bottom = 0;
            foreach (var m in _matches)
            {
                top += numerator(m);
                bottom += count(m);
            }

            return bottom == 0 ? 0f : (float)(top / bottom);
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
