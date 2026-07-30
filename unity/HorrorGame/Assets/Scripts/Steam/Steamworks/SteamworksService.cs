#nullable enable

#if HORRORGAME_STEAMWORKS

using System;
using Steamworks;
using UnityEngine;

namespace HorrorGame.Steam.SteamworksBackend
{
    /// <summary>
    /// The real platform: Steamworks.NET behind <see cref="ISteamService"/>.
    /// <para>
    /// This whole assembly is skipped when the Steamworks.NET package is absent — its
    /// asmdef carries <c>defineConstraints: ["HORRORGAME_STEAMWORKS"]</c> and the
    /// <c>versionDefines</c> entry that sets that symbol only when the package resolves.
    /// The <c>#if</c> around every file is belt and braces: it keeps the intent visible
    /// in the source and keeps an IDE that ignores asmdef constraints honest.
    /// </para>
    /// <para>
    /// Nothing above this layer names a Steamworks type. That is what §13's closing claim
    /// depends on — 음성 · 네트워킹을 인터페이스로 추상화해두면 나중 확장 비용이 줄어든다.
    /// 코드 몇 줄 차이다 — and it is only true as long as this boundary stays sealed.
    /// </para>
    /// </summary>
    public sealed class SteamworksService : ISteamService
    {
        private SteamworksIdentity? _identity;
        private SteamworksLobbyService? _lobbies;
        private SteamworksTransportProvider? _transport;
        private SteamworksVoiceBackend? _voice;
        private SteamworksStatsService? _stats;
        private SteamworksCloudSave? _cloud;

        /// <inheritdoc />
        public string BackendName => "Steamworks.NET";

        /// <inheritdoc />
        public SteamBackendState State { get; private set; } = SteamBackendState.NotInitialized;

        /// <inheritdoc />
        public bool IsOnline => State == SteamBackendState.Ready;

        /// <inheritdoc />
        public string? OfflineReason { get; private set; }

        /// <summary>
        /// §13 계정 / 신원. Valid only once <see cref="Initialize"/> has succeeded, which
        /// <see cref="SteamServices"/> guarantees before handing the service out.
        /// </summary>
        public IUserIdentity Identity => _identity ?? throw NotInitialized();

        /// <inheritdoc />
        public ILobbyService Lobbies => _lobbies ?? throw NotInitialized();

        /// <inheritdoc />
        public IP2PTransportProvider Transport => _transport ?? throw NotInitialized();

        /// <inheritdoc />
        public IVoiceBackend Voice => _voice ?? throw NotInitialized();

        /// <inheritdoc />
        public IStatsService Stats => _stats ?? throw NotInitialized();

        /// <inheritdoc />
        public ICloudSaveService Cloud => _cloud ?? throw NotInitialized();

        /// <inheritdoc />
        public bool Initialize()
        {
            if (State == SteamBackendState.Ready)
            {
                return true;
            }

            // Must happen before InitEx: a process Steam did not launch has no other way
            // to say which App ID it is. Note the interaction with RestartAppIfNecessary
            // below — the file's presence is what makes that call a no-op, and
            // SteamAppIdFile only writes it while the project is on §13's 480 or in a
            // development build, so a release build gets the restart behaviour and a
            // development build gets to run straight from the editor.
            SteamAppIdFile.EnsureWritten();

            try
            {
#if !UNITY_EDITOR
                if (SteamAPI.RestartAppIfNecessary(new AppId_t(SteamAppConfig.AppId)))
                {
                    // Steam is relaunching us with the right environment. Quitting here is
                    // the documented behaviour; anything else runs a second copy.
                    OfflineReason = "Steam is relaunching the game";
                    Application.Quit();
                    return false;
                }
#endif

                var result = SteamAPI.InitEx(out var errorMessage);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    OfflineReason = DescribeInitFailure(result, errorMessage);
                    Debug.LogWarning("[Steam] " + OfflineReason);
                    return false;
                }
            }
            catch (Exception ex)
            {
                // DllNotFoundException on a machine with the package but no native plugin
                // (a CI runner, a stripped build), or a marshalling failure on a version
                // mismatch. Either way this is an offline session, not a crash: §14 step 3
                // says the game is playable without Steam at all.
                OfflineReason = "Steamworks native library unavailable: " + ex.Message;
                Debug.LogWarning("[Steam] " + OfflineReason);
                return false;
            }

            VerifyAppId();

            _identity = new SteamworksIdentity();
            _lobbies = new SteamworksLobbyService(_identity);
            _transport = new SteamworksTransportProvider();
            _voice = new SteamworksVoiceBackend();
            _stats = new SteamworksStatsService();
            _cloud = new SteamworksCloudSave();

            State = SteamBackendState.Ready;
            OfflineReason = null;

            // §13's relay is free but not instantaneous — Steam has to pick relays and
            // measure them. Starting now means the wait happens while players are still
            // in the lobby instead of on top of the first connection.
            _transport.PrepareRelay();

            return true;
        }

        /// <inheritdoc />
        public void RunCallbacks()
        {
            if (State != SteamBackendState.Ready)
            {
                return;
            }

            SteamAPI.RunCallbacks();
            _lobbies?.PumpDeferredEvents();
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            if (State != SteamBackendState.Ready)
            {
                return;
            }

            _voice?.StopCapture();
            _lobbies?.Dispose();

            _lobbies = null;
            _identity = null;
            _transport = null;
            _voice = null;
            _stats = null;
            _cloud = null;

            State = SteamBackendState.NotInitialized;

            try
            {
                SteamAPI.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Steam] Shutdown threw, continuing: " + ex.Message);
            }
        }

        private static void VerifyAppId()
        {
            var running = SteamUtils.GetAppID().m_AppId;
            if (running == SteamAppConfig.AppId)
            {
                return;
            }

            // Almost always a stale steam_appid.txt from an earlier run, and the symptom
            // otherwise is stats and lobbies quietly belonging to another game.
            Debug.LogError("[Steam] App ID mismatch: this build expects " + SteamAppConfig.AppId
                + " but Steam reports " + running + ". Delete " + SteamAppConfig.AppIdFileName
                + " next to the executable and relaunch.");
        }

        private static string DescribeInitFailure(ESteamAPIInitResult result, string? message)
        {
            var detail = string.IsNullOrEmpty(message) ? "no detail" : message;

            switch (result)
            {
                case ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient:
                    return "Steam is not running, so the game is offline (" + detail + ")";
                case ESteamAPIInitResult.k_ESteamAPIInitResult_VersionMismatch:
                    return "The Steam client is older than this build's Steamworks SDK (" + detail + ")";
                default:
                    return "Steam did not initialise: " + detail;
            }
        }

        private static InvalidOperationException NotInitialized() =>
            new InvalidOperationException(
                "SteamworksService was used before a successful Initialize(). Access the platform through "
                + "SteamServices.Current, which substitutes the offline backend when Steam is unavailable.");
    }

    /// <summary>
    /// Announces the Steamworks backend to <see cref="SteamBackendRegistry"/>.
    /// <para>
    /// The registration runs from here rather than from the abstraction, because a
    /// reference in that direction is what would break a build with no Steamworks
    /// package — the base assembly would have to name a type in an assembly that does not
    /// exist. <c>AfterAssembliesLoaded</c> is before any scene, so
    /// <c>SteamServices.Current</c> cannot be asked for a backend before this has run.
    /// </para>
    /// </summary>
    internal static class SteamworksBackendInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            SteamBackendRegistry.Register("Steamworks.NET", () => new SteamworksService());
        }
    }
}

#endif
