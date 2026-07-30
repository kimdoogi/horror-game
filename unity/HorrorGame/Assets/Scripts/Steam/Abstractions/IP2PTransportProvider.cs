#nullable enable

namespace HorrorGame.Steam
{
    /// <summary>Which wire the match runs over.</summary>
    public enum P2PTransportKind
    {
        /// <summary>
        /// Same machine or same LAN, no platform involved. §14 steps 1–3 run here:
        /// 같은 PC 2인스턴스.
        /// </summary>
        LocalLoopback,

        /// <summary>
        /// Steam Networking Sockets, with Steam Datagram Relay behind it. §13's
        /// 릴레이가 무료인 것이 결정적이다 — this is the row that removes the only
        /// line item that would have cost real money.
        /// </summary>
        SteamSockets,
    }

    /// <summary>
    /// Everything the Net layer needs in order to point Mirror at the host, and
    /// nothing else.
    /// <para>
    /// Deliberately not a transport. Mirror's transport is a component the Net layer
    /// owns (FizzySteamworks for Steam sockets, KCP for loopback), and if this
    /// interface returned one, the Steam assembly would have to reference Mirror and
    /// the whole folder would stop compiling for a contributor who has neither
    /// package. What it returns instead is the address string and the kind, which is
    /// the actual seam: <c>networkManager.networkAddress = provider.AddressFor(host)</c>.
    /// </para>
    /// <para>
    /// §13 calls the relay free and decisive, but it is not instant — Steam has to
    /// pick relays and measure ping first. <see cref="PrepareRelay"/> exists so that
    /// happens while players are still in the lobby rather than adding seconds to the
    /// first connection.
    /// </para>
    /// </summary>
    public interface IP2PTransportProvider
    {
        /// <summary>Which wire this backend provides.</summary>
        P2PTransportKind Kind { get; }

        /// <summary>
        /// The address a client must give Mirror to reach <paramref name="host"/>.
        /// For Steam sockets that is the host's id in decimal, which is what
        /// FizzySteamworks parses out of <c>networkAddress</c>; for loopback it is
        /// <see cref="LocalAddress"/>.
        /// </summary>
        string AddressFor(NetUserId host);

        /// <summary>The address of a host on this machine. §14 step 2's two instances.</summary>
        string LocalAddress { get; }

        /// <summary>
        /// True once relay access is usable. False does not block connecting — a
        /// direct route may exist — it only means a NAT-bound pair might wait.
        /// </summary>
        bool IsRelayReady { get; }

        /// <summary>
        /// Starts relay initialisation. Cheap, idempotent, and worth calling as soon
        /// as the player opens the lobby screen.
        /// </summary>
        void PrepareRelay();

        /// <summary>One line for the log and the debug overlay: kind, relay state, address form.</summary>
        string Describe();
    }
}
