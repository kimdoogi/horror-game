#nullable enable

using System;
using Mirror;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// Where a platform transport announces itself to the Net layer.
    /// <para>
    /// The same inversion <c>SteamBackendRegistry</c> uses, and for the same reason.
    /// FizzySteamworks' own assembly definition restricts itself to the editor and
    /// the three standalone players, and it references Steamworks.NET. If
    /// <see cref="HorrorGameNetworkManager"/> named <c>FizzySteamworks</c> directly,
    /// <c>HorrorGame.Net</c> would inherit both restrictions and would stop
    /// compiling on a machine without those packages — which is exactly the
    /// situation §14 steps 1–3 develop in, where there is no Steam at all.
    /// </para>
    /// <para>
    /// So the constrained assembly points at this one, never the reverse, and a
    /// build with no Steam transport simply finds nothing registered and falls back
    /// to the local wire.
    /// </para>
    /// </summary>
    public static class NetTransportRegistry
    {
        private static Func<GameObject, Transport>? _factory;

        /// <summary>Name of the registered platform transport, or null when none is compiled in.</summary>
        public static string? BackendName { get; private set; }

        /// <summary>Whether a platform transport is available in this build.</summary>
        public static bool HasPlatformTransport => _factory != null;

        /// <summary>
        /// Registers the factory that adds the platform transport component to a
        /// GameObject. Called from a <c>RuntimeInitializeOnLoadMethod</c> at
        /// <c>AfterAssembliesLoaded</c>, before anything can ask for a transport.
        /// </summary>
        /// <param name="backendName">For the log line and the debug overlay.</param>
        /// <param name="factory">Adds and configures the transport on the given object.</param>
        public static void Register(string backendName, Func<GameObject, Transport> factory)
        {
            BackendName = backendName;
            _factory = factory;
        }

        /// <summary>
        /// Builds the platform transport onto <paramref name="owner"/>, or returns
        /// null when there is none or when construction failed. Never throws: a
        /// transport that cannot be built is a local session, not a dead boot — the
        /// same rule <c>SteamServices</c> follows.
        /// </summary>
        public static Transport? TryCreate(GameObject owner)
        {
            var factory = _factory;
            if (factory == null || owner == null)
            {
                return null;
            }

            try
            {
                return factory(owner);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Net] Transport backend '" + BackendName + "' could not be built: " + ex);
                return null;
            }
        }

        /// <summary>Forgets the registered backend. Tests only.</summary>
        public static void ResetForTests()
        {
            _factory = null;
            BackendName = null;
        }
    }
}
