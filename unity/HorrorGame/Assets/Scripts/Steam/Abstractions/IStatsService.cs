#nullable enable

namespace HorrorGame.Steam
{
    /// <summary>
    /// §13's 업적 · 통계 row, which is also the entirety of 텔레메트리 1단계
    /// (인프라 0).
    /// <para>
    /// The shape is dictated by what §13 actually asks for: named integer counters
    /// that the platform stores and aggregates globally, so that
    /// <c>aggro_duration_0_5s</c> and its three siblings add up to a histogram
    /// without a database existing anywhere. There is no query side and no read-back
    /// of aggregates, because Steam does not offer one usefully — §13's answer to
    /// "how do I see the distribution" is the partner site, not code.
    /// </para>
    /// <para>
    /// Leaderboards are missing on purpose. §13 lists them as free, but §15 discarded
    /// 판 사이 메타 프로그레션 and §08 completes its growth curve inside one match, so
    /// there is no score that survives a match to rank. Adding the API "because it is
    /// free" would mean maintaining an async call-result path that nothing calls.
    /// </para>
    /// <para>
    /// Every method returns a bool rather than throwing. Stats are best-effort
    /// bookkeeping: §13's whole point is that no infrastructure is load-bearing, and
    /// a failed stat write must never be able to interrupt a match.
    /// </para>
    /// </summary>
    public interface IStatsService
    {
        /// <summary>
        /// Whether writes reach the platform. False offline, and false until the
        /// platform has handed over the player's current values.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Reads a stored counter. False when unknown or unavailable.</summary>
        bool TryGetStat(string name, out int value);

        /// <summary>
        /// Adds to a counter. Read-modify-write against the platform's cached value,
        /// which is the only form Steam offers — and the reason batching lives one
        /// layer up in <see cref="SteamStatsTelemetrySink"/> instead of here, where
        /// every increment would be a round trip through the cache.
        /// </summary>
        bool AddToStat(string name, int amount);

        /// <summary>Overwrites a counter. Used for gauges, not for §13's histograms.</summary>
        bool SetStat(string name, int value);

        /// <summary>
        /// Unlocks an achievement. Idempotent on every platform, so callers do not
        /// have to remember what they already unlocked.
        /// </summary>
        bool UnlockAchievement(string id);

        /// <summary>Whether an achievement is already unlocked. False when unknown.</summary>
        bool IsAchievementUnlocked(string id);

        /// <summary>
        /// Uploads everything written so far. Steam batches locally until this is
        /// called, so without it a match's telemetry dies with the process.
        /// </summary>
        bool Store();
    }
}
