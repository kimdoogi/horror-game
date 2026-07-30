#nullable enable

using UnityEngine;

namespace HorrorGame.Steam
{
    /// <summary>
    /// Keeps the platform alive for the length of the process: creates the service,
    /// pumps its callbacks every frame, and shuts it down on quit.
    /// <para>
    /// It installs itself. Steamworks delivers every asynchronous result — lobby created,
    /// lobby entered, friend accepted an invite — through a callback pump that has to be
    /// called from the main thread, so if this component is missing from a scene the
    /// symptom is not an error but a lobby screen that never advances. Making it a scene
    /// object would mean every scene needs it and every new scene is a chance to forget;
    /// a <see cref="RuntimeInitializeOnLoadMethod"/> cannot be forgotten.
    /// </para>
    /// <para>
    /// <c>BeforeSplashScreen</c> is late enough that the backend assembly has registered
    /// itself (it registers at <c>AfterAssembliesLoaded</c>) and early enough that no
    /// gameplay code has asked for a lobby yet.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SteamRuntimeHost : MonoBehaviour
    {
        private static SteamRuntimeHost? _instance;

        /// <summary>The live host, or null before the game boots.</summary>
        public static SteamRuntimeHost? Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var host = new GameObject("[SteamRuntime]");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<SteamRuntimeHost>();

            // Force creation here rather than lazily on the first lobby call, so the
            // "which backend am I on" log line appears at boot next to the other startup
            // diagnostics instead of minutes later in the middle of a match.
            var service = SteamServices.Current;

            if (!service.IsOnline && SteamAppConfig.IsDevelopmentAppId)
            {
                Debug.Log("[Steam] Running offline on development App ID " + SteamAppConfig.AppId
                    + " (Spacewar). Local hosting works; invites and in-game voice do not.");
            }
        }

        private void OnApplicationQuit()
        {
            SteamServices.ShutdownAndReset();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;

                // Leaving play mode destroys this object without quitting the process, so
                // the shutdown has to happen here too: Steam refuses to initialise twice
                // in one process, which would make the second play-mode entry offline for
                // no visible reason.
                SteamServices.ShutdownAndReset();
            }
        }

        private void Update()
        {
            if (SteamServices.Exists)
            {
                SteamServices.Current.RunCallbacks();
            }
        }
    }
}
