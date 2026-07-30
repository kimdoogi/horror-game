#nullable enable

using System;
using HorrorGame.Steam.Offline;
using UnityEngine;

namespace HorrorGame.Steam
{
    /// <summary>
    /// Where a real platform backend announces itself.
    /// <para>
    /// The registration is inverted — the backend finds the abstraction, never the other
    /// way round — and that inversion is the entire reason this project compiles on a
    /// machine with no Steamworks package. The assembly holding
    /// <c>SteamworksService</c> has a <c>defineConstraints</c> entry, so Unity skips it
    /// (and its reference to <c>com.rlabrecque.steamworks.net</c>) when the package is
    /// absent. If this class instead named that type, the reference would have to point
    /// the other way and <em>this</em> assembly would fail to compile — which is exactly
    /// the failure the requirement forbids.
    /// </para>
    /// <para>
    /// It is also how a port stays cheap, per §13's closing claim: a second platform
    /// registers here from its own constrained assembly and nothing else in the codebase
    /// changes.
    /// </para>
    /// </summary>
    public static class SteamBackendRegistry
    {
        private static Func<ISteamService>? _factory;

        /// <summary>Name of the registered backend, or null when none is compiled in.</summary>
        public static string? BackendName { get; private set; }

        /// <summary>Whether a platform backend is compiled into this build.</summary>
        public static bool HasBackend => _factory != null;

        /// <summary>
        /// Registers the backend factory. Called from a
        /// <c>RuntimeInitializeOnLoadMethod</c> at
        /// <c>AfterAssembliesLoaded</c> — before anything can ask for
        /// <see cref="SteamServices.Current"/>, and without either assembly needing to
        /// know when the other loads.
        /// </summary>
        public static void Register(string backendName, Func<ISteamService> factory)
        {
            BackendName = backendName;
            _factory = factory;
        }

        /// <summary>
        /// Builds the registered backend, or null when there is none. Never throws: a
        /// backend constructor that fails is an offline session, not a dead boot.
        /// </summary>
        public static ISteamService? TryCreate()
        {
            var factory = _factory;
            if (factory == null)
            {
                return null;
            }

            try
            {
                return factory();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Steam] Backend '" + BackendName + "' could not be constructed: " + ex);
                return null;
            }
        }
    }

    /// <summary>
    /// The single access point to §13's platform services.
    /// <para>
    /// <b>Both compile paths, spelled out</b>, because the requirement that this folder
    /// build with the Steamworks DLLs absent is easy to satisfy accidentally and easy to
    /// break accidentally:
    /// </para>
    /// <para>
    /// <b>With the package installed.</b> The asmdef's <c>versionDefines</c> entry sees
    /// <c>com.rlabrecque.steamworks.net</c> and defines <c>HORRORGAME_STEAMWORKS</c>.
    /// The nested <c>HorrorGame.Steam.SteamworksBackend</c> assembly's matching
    /// <c>defineConstraints</c> is satisfied, so it compiles, references Steamworks.NET
    /// and registers itself with <see cref="SteamBackendRegistry"/> on load.
    /// <see cref="Current"/> builds it, calls <see cref="ISteamService.Initialize"/>, and
    /// on success the game is on Steam. On failure — Steam not running, wrong App ID,
    /// no launch through Steam — it falls back to <see cref="NullSteamService"/> with the
    /// reason attached.
    /// </para>
    /// <para>
    /// <b>Without the package.</b> The define is never set, so Unity skips the nested
    /// assembly entirely — including its reference to a package that does not exist,
    /// which is the part that would otherwise be a hard compile error. Nothing registers,
    /// <see cref="Current"/> goes straight to <see cref="NullSteamService"/>, and every
    /// consumer of <see cref="ISteamService"/> compiles and runs unchanged. This is the
    /// path CI takes and the path §14 steps 1–3 develop on.
    /// </para>
    /// <para>
    /// <see cref="Current"/> is never null and never throws, so no caller needs a
    /// fallback of its own. That is deliberate: a service locator that can return null is
    /// a null check at every call site, and the ones that get forgotten are the ones in
    /// the code paths nobody runs until release day.
    /// </para>
    /// </summary>
    public static class SteamServices
    {
        private static ISteamService? _current;

        /// <summary>
        /// The live service. Created and initialised on first access.
        /// </summary>
        public static ISteamService Current => _current ??= CreateAndInitialize();

        /// <summary>Whether the service has been created yet. False before the first access.</summary>
        public static bool Exists => _current != null;

        /// <summary>
        /// Replaces the service, for tests and for an editor tool that wants the offline
        /// backend on purpose. Shuts down whatever was there first.
        /// </summary>
        public static void Override(ISteamService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (!ReferenceEquals(_current, service))
            {
                _current?.Shutdown();
                _current = service;
            }
        }

        /// <summary>
        /// Shuts the service down and forgets it. Called when the application quits and
        /// from the editor when leaving play mode, so the next run starts clean — Steam
        /// does not tolerate two initialised instances in one process.
        /// </summary>
        public static void ShutdownAndReset()
        {
            if (_current == null)
            {
                return;
            }

            try
            {
                _current.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            _current = null;
        }

        private static ISteamService CreateAndInitialize()
        {
            var backend = SteamBackendRegistry.TryCreate();

            if (backend != null)
            {
                if (backend.Initialize())
                {
                    Debug.Log("[Steam] " + backend.BackendName + " ready as "
                        + backend.Identity.LocalName + " (" + backend.Identity.LocalId + "), App ID "
                        + SteamAppConfig.AppId + ". " + backend.Transport.Describe());
                    return backend;
                }

                var reason = backend.OfflineReason ?? "Steam did not initialise";
                backend.Shutdown();

                var fallback = new NullSteamService(reason);
                fallback.Initialize();
                return fallback;
            }

            var offline = new NullSteamService(
                "built without the Steamworks.NET package, so there is no Steam backend compiled in");
            offline.Initialize();
            return offline;
        }
    }
}
