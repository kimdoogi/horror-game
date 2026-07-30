#nullable enable

namespace HorrorGame.Steam
{
    /// <summary>
    /// Whether the platform is actually behind the interface.
    /// <para>
    /// <see cref="Offline"/> is not an error state. §14 step 3 prototypes the whole
    /// game before Steam enters the plan at all, and the same path is what CI and a
    /// contributor without Steam installed use, so it is a supported way to run.
    /// </para>
    /// </summary>
    public enum SteamBackendState
    {
        /// <summary><see cref="ISteamService.Initialize"/> has not been called yet.</summary>
        NotInitialized,

        /// <summary>Talking to a real Steam client.</summary>
        Ready,

        /// <summary>Running on the local stand-in: no Steam client, or built without the DLLs.</summary>
        Offline,
    }

    /// <summary>
    /// Everything §13 gets from Steam, behind one seam.
    /// <para>
    /// §13's table is the specification: P2P 네트워킹 · NAT 통과 + 릴레이 ·
    /// 로비 / 친구 초대 · 음성 캡처 + 코덱 · 계정 / 신원 · 세이브 ·
    /// 업적 · 통계. Each one is a sub-interface below, and every one of them is
    /// expressed in primitives — <see cref="NetUserId"/>, <c>byte[]</c>, <c>int</c>,
    /// <c>string</c>. No Steamworks type crosses this boundary. That is the whole
    /// reason the boundary exists: §13 closes by saying that abstracting voice and
    /// networking now makes a later non-Steam port 코드 몇 줄 차이, and the way to
    /// keep that true is to make it impossible to accidentally leak a
    /// <c>CSteamID</c> into gameplay code.
    /// </para>
    /// <para>
    /// Two implementations exist and exactly one is compiled per build; see
    /// <see cref="SteamServices"/> for how the choice is made and why it cannot
    /// break a machine without Steamworks installed.
    /// </para>
    /// <para>
    /// Nullable reference types are enabled per-file across this folder with
    /// <c>#nullable enable</c>. Unity has no project-wide switch for it and
    /// ARCHITECTURE §2 requires honest annotations, so the directive is repeated
    /// rather than assumed.
    /// </para>
    /// </summary>
    public interface ISteamService
    {
        /// <summary>Human-readable backend name, for the log line and the debug overlay.</summary>
        string BackendName { get; }

        /// <summary>Whether a real platform is behind this instance.</summary>
        SteamBackendState State { get; }

        /// <summary>
        /// Shorthand for <see cref="State"/> being <see cref="SteamBackendState.Ready"/>.
        /// UI uses it to decide whether to offer "invite friends" at all; it must
        /// never be used to decide whether the game may start.
        /// </summary>
        bool IsOnline { get; }

        /// <summary>
        /// Why the offline stand-in is in use, or null when online. Shown verbatim in
        /// the lobby UI: "Steam is not running" is a fixable problem and the player
        /// deserves to know which one it is.
        /// </summary>
        string? OfflineReason { get; }

        /// <summary>§13 계정 / 신원.</summary>
        IUserIdentity Identity { get; }

        /// <summary>§13 로비 / 친구 초대.</summary>
        ILobbyService Lobbies { get; }

        /// <summary>§13 P2P 네트워킹 and NAT 통과 + 릴레이.</summary>
        IP2PTransportProvider Transport { get; }

        /// <summary>§13 음성 캡처 + 코덱 — the codec only. The proximity rule lives in <c>Voice/</c>.</summary>
        IVoiceBackend Voice { get; }

        /// <summary>§13 업적 · 통계, which is also all of 텔레메트리 1단계.</summary>
        IStatsService Stats { get; }

        /// <summary>§13 세이브.</summary>
        ICloudSaveService Cloud { get; }

        /// <summary>
        /// Brings the platform up. Returns false when it could not be reached, in
        /// which case the caller substitutes the offline stand-in — it must not
        /// retry in a loop or block the boot.
        /// </summary>
        bool Initialize();

        /// <summary>
        /// Pumps platform callbacks. Steamworks delivers every asynchronous result
        /// through this, so it has to be called from the main thread every frame or
        /// nothing — lobby creation, joins, invites — ever completes. The offline
        /// backend drains its own deferred-event queue here so that both backends
        /// have the same "results arrive on a later frame" timing, and code written
        /// against the stand-in does not break when Steam appears.
        /// </summary>
        void RunCallbacks();

        /// <summary>
        /// Releases the platform. Safe to call twice, and safe to call after a failed
        /// <see cref="Initialize"/>. §13's 호스트 이탈 decision is 세션 종료, so there
        /// is no state worth preserving across this.
        /// </summary>
        void Shutdown();
    }
}
