#nullable enable

#if HORRORGAME_STEAMWORKS

using Steamworks;

namespace HorrorGame.Steam.SteamworksBackend
{
    /// <summary>
    /// §13's 업적 · 통계 row on ISteamUserStats — which is also all of 텔레메트리 1단계,
    /// since §13's answer to "where does the balance data go" is 통계 항목을 정의하면
    /// Steam이 저장하고 글로벌 집계까지 해준다.
    /// <para>
    /// No explicit request for the player's current values: this version of the Steamworks
    /// SDK fetches them automatically at initialisation, and the old
    /// <c>RequestCurrentStats</c> is gone. Until they arrive, writes fail — which is why
    /// <see cref="SteamStatsTelemetrySink"/> buffers and retries instead of assuming a
    /// write landed. §13's data is a per-match summary, so a few seconds of unavailability
    /// at boot costs nothing.
    /// </para>
    /// <para>
    /// A stat must be declared on the Steamworks partner site before Steam will accept a
    /// write, and an undeclared one fails silently. The name check that catches that lives
    /// in <see cref="SteamStatsTelemetrySink"/>, against the core's own list.
    /// </para>
    /// </summary>
    public sealed class SteamworksStatsService : IStatsService
    {
        /// <summary>
        /// Whether Steam is in a state where stat writes can succeed.
        /// <para>
        /// Being logged on is the necessary condition; the sufficient one is that the
        /// player's stats have arrived, and the only honest test for that is a
        /// <c>GetStat</c> that succeeds — which is exactly what
        /// <see cref="AddToStat"/> does before writing. So this stays a coarse "is there a
        /// Steam session" answer for UI, and correctness lives in the per-write check
        /// rather than in a flag that has to be kept true.
        /// </para>
        /// </summary>
        public bool IsAvailable => SteamUser.BLoggedOn();

        /// <inheritdoc />
        public bool TryGetStat(string name, out int value)
        {
            value = 0;
            return !string.IsNullOrEmpty(name) && SteamUserStats.GetStat(name, out value);
        }

        /// <summary>
        /// Adds to a counter. Steam offers no atomic increment, so this is a read, an add
        /// and a write against Steam's local cache — which is why it is called once per
        /// counter per match from a buffered flush rather than per event.
        /// </summary>
        public bool AddToStat(string name, int amount)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // Explicitly typed, not var: Steam overloads GetStat on int and float, and
            // an inferred out variable cannot choose between them.
            if (!SteamUserStats.GetStat(name, out int current))
            {
                // Either the stat is not declared on the partner site or the player's stats
                // have not arrived yet. Both mean "do not write", and the caller keeps the
                // value buffered for a later attempt.
                return false;
            }

            var next = amount > 0 && current > int.MaxValue - amount ? int.MaxValue : current + amount;
            return SteamUserStats.SetStat(name, next);
        }

        /// <inheritdoc />
        public bool SetStat(string name, int value) =>
            !string.IsNullOrEmpty(name) && SteamUserStats.SetStat(name, value);

        /// <inheritdoc />
        public bool UnlockAchievement(string id) =>
            !string.IsNullOrEmpty(id) && SteamUserStats.SetAchievement(id);

        /// <inheritdoc />
        public bool IsAchievementUnlocked(string id) =>
            !string.IsNullOrEmpty(id) && SteamUserStats.GetAchievement(id, out var unlocked) && unlocked;

        /// <summary>
        /// Uploads everything written since the last store. Steam keeps writes local until
        /// this is called, so a match that ends without it contributes nothing to §13's
        /// histograms.
        /// </summary>
        public bool Store() => SteamUserStats.StoreStats();
    }
}

#endif
