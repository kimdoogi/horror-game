using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;

namespace HorrorGame.Core.Telemetry
{
    /// <summary>
    /// One match, reduced to the numbers that answer §16's open balance questions.
    /// <para>
    /// Deliberately flat and small. §13 chose bucket counters over a database
    /// precisely so this could be a value type handed to Steam Stats, written to a
    /// JSONL line, or fed to the headless simulator's aggregator without any of
    /// them needing a schema. Every field maps to a question the design says it
    /// cannot answer from the document alone.
    /// </para>
    /// <para>No player-identifying data. §13: anonymous session id only.</para>
    /// </summary>
    public struct MatchSummary
    {
        /// <summary>The match seed. Replaying it reproduces the layout exactly.</summary>
        public int Seed;

        /// <summary>Anonymous per-session id. Never a Steam ID, never a name.</summary>
        public string? SessionId;

        /// <summary>Map identifier.</summary>
        public string? MapId;

        /// <summary>How it ended. §02.</summary>
        public MatchOutcome Outcome;

        /// <summary>Wall-clock length, seconds. Checked against §01's 25–35 min target.</summary>
        public float DurationSeconds;

        /// <summary>Descents into the basement. §03 expects 2–5; §07's curve is tuned against this.</summary>
        public int RoundTrips;

        /// <summary>Players who got out alive, 0–4. §02.</summary>
        public int PlayersEscaped;

        /// <summary>Deaths, 0–4. §09.</summary>
        public int PlayersDied;

        /// <summary>The four roles taken. §11 uses the spread to check no role is compulsory.</summary>
        public RoleId Role0;

        /// <summary>See <see cref="Role0"/>.</summary>
        public RoleId Role1;

        /// <summary>See <see cref="Role0"/>.</summary>
        public RoleId Role2;

        /// <summary>See <see cref="Role0"/>.</summary>
        public RoleId Role3;

        /// <summary>Credits earned across the match. §16-2 — the top-priority open question.</summary>
        public int CreditsEarned;

        /// <summary>Credits spent. Together with <see cref="CreditsEarned"/> this shows whether §08's growth curve is real.</summary>
        public int CreditsSpent;

        /// <summary>Loot pieces sold. §08.</summary>
        public int LootSold;

        /// <summary>Clues read. §03 — if this is far below <see cref="GameConstants.CluesRequiredToLocate"/>, clue reading is too dangerous.</summary>
        public int CluesRead;

        /// <summary>Clues misremembered and acted on wrongly. §03 wants this non-zero — it is the intended failure mode.</summary>
        public int ClueMisreads;

        /// <summary>Times the monster acquired a target. §06.</summary>
        public int AggroEvents;

        /// <summary>Total seconds under active chase. Bucketed to validate the 12 m release distance. §13.</summary>
        public float TotalAggroSeconds;

        /// <summary>Longest single chase, seconds. §06.</summary>
        public float LongestAggroSeconds;

        /// <summary>Seconds spent holding S. §13's check on the 65% backward multiplier. §05.</summary>
        public float BackpedalSeconds;

        /// <summary>Seconds spent moving at all — the denominator for <see cref="BackpedalSeconds"/>.</summary>
        public float TotalMovingSeconds;

        /// <summary>Batteries consumed. §16-5 — sets the round-trip rhythm.</summary>
        public int BatteriesUsed;

        /// <summary>Whether the objective left the basement. §02.</summary>
        public bool ObjectiveRecovered;

        /// <summary>Backward-movement share of all movement, 0–1. §05's peek dilemma is working when this stays low but non-zero.</summary>
        public float BackpedalRatio =>
            TotalMovingSeconds > 0.001f ? BackpedalSeconds / TotalMovingSeconds : 0f;

        /// <summary>Mean chase length, seconds. §06's release tuning target.</summary>
        public float AverageAggroSeconds =>
            AggroEvents > 0 ? TotalAggroSeconds / AggroEvents : 0f;
    }
}
