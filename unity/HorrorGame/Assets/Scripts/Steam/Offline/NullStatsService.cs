#nullable enable

using System.Collections.Generic;

namespace HorrorGame.Steam.Offline
{
    /// <summary>
    /// Stats that go nowhere — but countably nowhere.
    /// <para>
    /// §13's 텔레메트리 1단계 is Steam Stats and nothing else, so with no platform there
    /// is no store to write to. What this does instead is keep the totals in memory,
    /// which turns "did the recorder emit the counters I expected" into a question a
    /// developer can answer during an offline session, and matches what
    /// <c>InMemoryTelemetrySink</c> does for the core suite: §13's counters are only
    /// readable as a distribution if they are readable at all.
    /// </para>
    /// <para>
    /// <see cref="IsAvailable"/> is false, so callers that report telemetry state to the
    /// player tell the truth. Nothing is uploaded and nothing is persisted: an offline
    /// session's balance data is deliberately discarded rather than queued, because a
    /// queue would be a save file, a save file would need a schema, and §13 spent a whole
    /// section establishing that this game needs neither.
    /// </para>
    /// </summary>
    public sealed class NullStatsService : IStatsService
    {
        private readonly Dictionary<string, int> _stats = new Dictionary<string, int>(System.StringComparer.Ordinal);
        private readonly HashSet<string> _achievements = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>False: writes reach memory, not Steam.</summary>
        public bool IsAvailable => false;

        /// <summary>Everything written this session, for a debug overlay.</summary>
        public IReadOnlyDictionary<string, int> Snapshot => _stats;

        /// <summary>Times <see cref="Store"/> was called. One per match, if the recorder is wired correctly.</summary>
        public int StoreCount { get; private set; }

        /// <inheritdoc />
        public bool TryGetStat(string name, out int value)
        {
            if (!string.IsNullOrEmpty(name) && _stats.TryGetValue(name, out value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        /// <inheritdoc />
        public bool AddToStat(string name, int amount)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            _stats.TryGetValue(name, out var current);

            // Saturate rather than wrap, for the reason InMemoryTelemetrySink gives: a
            // wrapped counter reads as a small plausible number and a balance decision
            // taken on it is wrong with no way to tell.
            _stats[name] = amount > 0 && current > int.MaxValue - amount ? int.MaxValue : current + amount;
            return true;
        }

        /// <inheritdoc />
        public bool SetStat(string name, int value)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            _stats[name] = value;
            return true;
        }

        /// <inheritdoc />
        public bool UnlockAchievement(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            _achievements.Add(id);
            return true;
        }

        /// <inheritdoc />
        public bool IsAchievementUnlocked(string id) => id != null && _achievements.Contains(id);

        /// <summary>
        /// Counts the call and succeeds. Returning false would make
        /// <see cref="SteamStatsTelemetrySink"/> keep retrying an upload that has no
        /// destination.
        /// </summary>
        public bool Store()
        {
            StoreCount++;
            return true;
        }
    }
}
