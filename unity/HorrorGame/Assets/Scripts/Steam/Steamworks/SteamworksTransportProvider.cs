#nullable enable

#if HORRORGAME_STEAMWORKS

using Steamworks;
using UnityEngine;

namespace HorrorGame.Steam.SteamworksBackend
{
    /// <summary>
    /// §13's P2P 네트워킹 and NAT 통과 + 릴레이 rows, reduced to the two things the Net
    /// layer actually needs: an address, and the knowledge that it should be using Steam
    /// sockets.
    /// <para>
    /// §13 calls the free relay decisive — 릴레이가 무료인 것이 결정적이다 — because
    /// relaying traffic for players who cannot connect directly is the one item in the
    /// plan that would otherwise cost real money per player. Steam Datagram Relay does it
    /// at no cost, which is why there is no fallback path here: without SDR this project
    /// would need a server, and §13 spent a section establishing that it has none.
    /// </para>
    /// <para>
    /// The address is the host's SteamID64 in decimal, which is what FizzySteamworks reads
    /// out of Mirror's <c>networkAddress</c>. Returning a string rather than a transport
    /// object is what keeps this assembly free of a Mirror reference — see
    /// <see cref="IP2PTransportProvider"/>.
    /// </para>
    /// </summary>
    public sealed class SteamworksTransportProvider : IP2PTransportProvider
    {
        private bool _relayRequested;

        /// <inheritdoc />
        public P2PTransportKind Kind => P2PTransportKind.SteamSockets;

        /// <inheritdoc />
        public string LocalAddress => "localhost";

        /// <summary>
        /// Whether relay access has been arranged for.
        /// <para>
        /// Reports the request rather than Steam's own progress, because nothing in the
        /// game gates on it: a pair that can connect directly does so without waiting, and
        /// a pair that cannot is going to wait regardless of what a UI says. Steam exposes
        /// the finer-grained state through <c>SteamNetworkingUtils.GetRelayNetworkStatus</c>
        /// if a connection screen ever wants to distinguish "measuring relays" from "ready".
        /// </para>
        /// </summary>
        public bool IsRelayReady => _relayRequested;

        /// <summary>
        /// The host's id in decimal — the form FizzySteamworks expects.
        /// <para>
        /// Falls back to loopback for an invalid host, because that produces an immediate,
        /// legible connection failure instead of a client sitting on a black screen waiting
        /// for a peer that was never named.
        /// </para>
        /// </summary>
        public string AddressFor(NetUserId host) => host.IsValid ? host.ToString() : LocalAddress;

        /// <summary>
        /// Asks Steam to start measuring and selecting relays. Idempotent, and cheap enough
        /// to call whenever the lobby screen opens.
        /// </summary>
        public void PrepareRelay()
        {
            if (_relayRequested)
            {
                return;
            }

            _relayRequested = true;
            SteamNetworkingUtils.InitRelayNetworkAccess();
            Debug.Log("[Steam] Relay network access requested (Steam Datagram Relay — §13's free NAT traversal).");
        }

        /// <inheritdoc />
        public string Describe() =>
            "Steam Sockets transport, relay " + (_relayRequested ? "requested" : "not requested");
    }
}

#endif
