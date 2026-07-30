#nullable enable

namespace HorrorGame.Steam
{
    /// <summary>
    /// The App ID, in one place, and the file name Steam looks for beside the
    /// executable.
    /// <para>
    /// §13's 인프라가 아니라 행정 table prices the real App ID at $100 and it does not
    /// exist yet, while the same section notes that <c>480</c> (Spacewar) already
    /// allows 로비 · P2P · 음성 테스트 before the store page exists. So the whole
    /// project is developed against a borrowed App ID and has to switch to the real
    /// one later without a hunt — hence exactly one declaration of the number, and
    /// nothing else in the codebase is allowed to name an App ID.
    /// </para>
    /// <para>
    /// §14 step 7 warns not to delay the store page. When it lands, the App ID
    /// switch is the single line marked below.
    /// </para>
    /// </summary>
    public static class SteamAppConfig
    {
        /// <summary>
        /// Spacewar, Valve's public test App ID. §13's 개발용 App ID row. Every Steam
        /// account owns it, so any contributor can run lobbies, P2P and voice
        /// against it without owning our game.
        /// </summary>
        public const uint DevAppId = 480u;

        // ====================================================================
        //  ▼▼▼ THE ONE LINE TO CHANGE AT RELEASE ▼▼▼
        //
        //  Replace DevAppId with the numeric App ID issued by Steamworks, e.g.
        //      public const uint AppId = 1234560u;
        //
        //  Nothing else needs editing: steam_appid.txt, the init check and the
        //  Steamworks callbacks all read AppId from here. Note that switching off
        //  DevAppId also stops the build from shipping steam_appid.txt, which is
        //  what Valve asks for in a release build — see SteamAppIdFile.
        // ====================================================================
        /// <summary>The App ID this build initialises Steamworks with.</summary>
        public const uint AppId = DevAppId;

        /// <summary>
        /// True while the project is still borrowing Spacewar. Guards the two
        /// behaviours that are correct in development and wrong in a released
        /// build: writing <c>steam_appid.txt</c> next to the player, and treating a
        /// failed Steam init as ordinary rather than alarming.
        /// <para>
        /// A property rather than a <c>const</c> on purpose: a const would fold to
        /// <c>false</c> at release and bury every call site under CS0162
        /// unreachable-code warnings, which is a poor reward for shipping.
        /// </para>
        /// </summary>
        public static bool IsDevelopmentAppId => AppId == DevAppId;

        /// <summary>
        /// The file Steamworks reads to learn which App ID an un-launched-by-Steam
        /// process belongs to. The name is fixed by the Steamworks SDK.
        /// </summary>
        public const string AppIdFileName = "steam_appid.txt";

        /// <summary>
        /// Lobby metadata keys. §13 gives the host authority over the match, so the
        /// lobby only ever carries what a joining client needs to reach that host —
        /// never anything from §03 (clue contents, objective location), which
        /// §13's 아키텍처 table keeps host-side precisely because a client can read
        /// its own memory. Lobby data is world-readable by anyone in the lobby, so
        /// treat this list as public.
        /// </summary>
        public static class LobbyKeys
        {
            /// <summary>SteamID64 of the host, as a decimal string. This is the address the transport connects to.</summary>
            public const string HostId = "host_id";

            /// <summary>Display name of the lobby, for the friend's join prompt.</summary>
            public const string Name = "name";

            /// <summary>Build version, so a mismatched client fails with a message instead of a desync.</summary>
            public const string Version = "version";

            /// <summary>Map identifier. Cosmetic — the layout itself is generated host-side from the seed.</summary>
            public const string MapId = "map";

            /// <summary>Match state, so a lobby that is already underway stops advertising itself as joinable.</summary>
            public const string Status = "status";
        }
    }
}
