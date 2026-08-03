using HorrorGame.Core.Race;

namespace HorrorGame.Core.Telemetry
{
    /// <summary>
    /// One race, reduced to the numbers that answer §16's open balance questions.
    /// <para>
    /// Deliberately flat and small. §13 chose bucket counters over a database
    /// precisely so this could be a value type handed to Steam Stats, written to a
    /// JSONL line, or fed to the headless simulator's aggregator without any of
    /// them needing a schema. Every field maps to a question the design says it
    /// cannot answer from the document alone.
    /// </para>
    /// <para>No player-identifying data. §13: anonymous session id only.</para>
    /// <para>
    /// DELETED with the co-op game: <c>Role0</c>–<c>Role3</c> (§04 직업 — twenty
    /// identical runners have no role to spread), <c>CluesRead</c> /
    /// <c>ClueMisreads</c> (§03 단서), <c>BatteriesUsed</c> (§08's battery
    /// economy), <c>ObjectiveRecovered</c> (§03 목표물), and before them
    /// <c>CreditsEarned</c> / <c>CreditsSpent</c> / <c>LootSold</c>. A telemetry
    /// field no code path can raise is a zero that reads like a measurement — that
    /// is the trap, and it is why these went rather than being left at their
    /// defaults.
    /// </para>
    /// </summary>
    public struct MatchSummary
    {
        /// <summary>The race seed. Replaying it reproduces the layout exactly.</summary>
        public int Seed;

        /// <summary>Anonymous per-session id. Never a Steam ID, never a name.</summary>
        public string? SessionId;

        /// <summary>Map identifier.</summary>
        public string? MapId;

        /// <summary>How the local runner's race ended. §02 — 완주, 탈락, or still running.</summary>
        public RacerStatus Outcome;

        /// <summary>Wall-clock length, seconds. §01's target for a full descent.</summary>
        public float DurationSeconds;

        /// <summary>
        /// Deepest storey reached, 0 (B1's rim) to <see cref="RaceState.Storeys"/>.
        /// <para>
        /// This is the race's central measurement and it replaces the co-op
        /// <c>RoundTrips</c>. §01 makes going down the whole game, so "how far did
        /// they get" is what says whether the building is too long, too hard or
        /// too short. A distribution piled on B1 and B2 means §06 or §12-A is
        /// eating the field before the race has a shape.
        /// </para>
        /// </summary>
        public int DeepestStorey;

        /// <summary>Runners who reached the middle of B8. §02 완주.</summary>
        public int RunnersFinished;

        /// <summary>Runners the creature caught. §02 탈락 — out and unranked.</summary>
        public int RunnersEliminated;

        /// <summary>Times a creature acquired a target. §06.</summary>
        public int AggroEvents;

        /// <summary>Total seconds under active chase. Bucketed to validate the 12 m release distance. §13.</summary>
        public float TotalAggroSeconds;

        /// <summary>Longest single chase, seconds. §06.</summary>
        public float LongestAggroSeconds;

        /// <summary>Seconds spent holding S. §13's check on the 65% backward multiplier. §05.</summary>
        public float BackpedalSeconds;

        /// <summary>Seconds spent moving at all — the denominator for <see cref="BackpedalSeconds"/>.</summary>
        public float TotalMovingSeconds;

        /// <summary>Backward-movement share of all movement, 0–1. §05's peek dilemma is working when this stays low but non-zero.</summary>
        public float BackpedalRatio =>
            TotalMovingSeconds > 0.001f ? BackpedalSeconds / TotalMovingSeconds : 0f;

        /// <summary>Mean chase length, seconds. §06's release tuning target.</summary>
        public float AverageAggroSeconds =>
            AggroEvents > 0 ? TotalAggroSeconds / AggroEvents : 0f;
    }
}
