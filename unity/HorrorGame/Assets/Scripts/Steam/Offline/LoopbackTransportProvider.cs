#nullable enable

namespace HorrorGame.Steam.Offline
{
    /// <summary>
    /// The address of a host on this machine — §14 step 2's 같은 PC 2인스턴스, and the
    /// only transport available before step 4 introduces Steam.
    /// <para>
    /// It reports <see cref="P2PTransportKind.LocalLoopback"/>, which is the signal the
    /// Net layer uses to pick Mirror's default transport instead of FizzySteamworks. It
    /// is not a stub: two instances on one machine, or two machines on a LAN with a
    /// typed-in address, is a genuinely working configuration and is how the five
    /// verification questions in §14 get answered.
    /// </para>
    /// </summary>
    public sealed class LoopbackTransportProvider : IP2PTransportProvider
    {
        /// <inheritdoc />
        public P2PTransportKind Kind => P2PTransportKind.LocalLoopback;

        /// <summary>
        /// Loopback. A remote host is unreachable without a platform, so returning its
        /// id would produce a connection attempt that hangs; returning loopback fails
        /// immediately and legibly.
        /// </summary>
        public string AddressFor(NetUserId host) => LocalAddress;

        /// <inheritdoc />
        public string LocalAddress => "localhost";

        /// <summary>
        /// True, because there is no relay to wait for. Reporting false would make UI
        /// that waits for relay readiness hang forever on a configuration that is
        /// already fully working.
        /// </summary>
        public bool IsRelayReady => true;

        /// <summary>Nothing to prepare. Steam Datagram Relay is §13's, and §13 is not here yet.</summary>
        public void PrepareRelay()
        {
        }

        /// <inheritdoc />
        public string Describe() => "Loopback transport (localhost) — no relay, no NAT traversal";
    }
}
