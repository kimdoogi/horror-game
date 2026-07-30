#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Roles;
using HorrorGame.Net.Host;
using HorrorGame.Steam;
using kcp2k;
using Mirror;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// §13's networking decision, wired up: Mirror, host authority, no host
    /// migration, Steam transport with a local wire behind it.
    /// <para>
    /// <b>Two transports, one code path.</b> §14 orders the work so that step 2 —
    /// "Mirror 로컬 호스트 — 같은 PC 2인스턴스" — happens two steps before Steam
    /// enters the plan at all. So the transport is chosen at runtime and everything
    /// above it is identical: FizzySteamworks when a Steam backend is compiled in and
    /// the platform is actually up, KCP on localhost otherwise. A contributor with no
    /// Steam client, a CI machine, and two windows on one desk all take the same
    /// path.
    /// </para>
    /// <para>
    /// <b>The host leaving ends the session.</b> §13: "호스트 이탈 — 세션 종료.
    /// 마이그레이션 하지 않음." There is no promotion, no state handoff and no
    /// reconnect window, and that is a design decision rather than an omission: the
    /// host is the only machine holding §03's answers, so a migrated session would
    /// either lose the match's premise or hand it to somebody new — and handing it to
    /// somebody new is exactly what §13 forbids.
    /// </para>
    /// </summary>
    [AddComponentMenu("HorrorGame/Net/Horror Game Network Manager")]
    public class HorrorGameNetworkManager : NetworkManager
    {
        [Header("HorrorGame")]
        [Tooltip("Spawned on the server at start-up. Holds §11's four seats.")]
        [SerializeField]
        private NetLobby? _lobbyPrefab;

        [Tooltip("Refuse connections once the match is running. §07 makes the clock the pressure; "
                 + "a player dropped into hour two has no game to play.")]
        [SerializeField]
        private bool _refuseLateJoins = true;

        private NetLobby? _lobby;

        /// <summary>Which wire this session is running on.</summary>
        public NetTransportKind ActiveTransportKind { get; private set; } = NetTransportKind.Local;

        /// <summary>The lobby object, on the host. Null on a client and before the server starts.</summary>
        public NetLobby? Lobby => _lobby;

        /// <inheritdoc />
        public override void Awake()
        {
            // §11 fixes the party at four. Mirror counts the host among them, so this
            // is the whole table, not four guests.
            maxConnections = GameConstants.PlayersPerMatch;

            // §13's budget argument depends on the traffic staying inside what Steam
            // relays for free, and §05's five synced rows are cheap at this rate.
            sendRate = GameConstants.NetworkSendRate;

            base.Awake();

            // After base.Awake(), because a duplicate manager destroys itself in
            // there — and a destroyed manager must not have left Transport.active
            // pointing at a component that is about to go away.
            if (singleton != this)
            {
                return;
            }

            EnsureTransport();
            EnsureInterestManagement();
        }

        /// <summary>
        /// Picks the wire. Steam when it is really there, localhost otherwise.
        /// <para>
        /// The Steam path is reached through <see cref="NetTransportRegistry"/> and
        /// never by naming FizzySteamworks: that assembly is editor-and-standalone
        /// only and references Steamworks.NET, and a direct reference would drag both
        /// restrictions into every build. See <see cref="NetTransportRegistry"/> for
        /// the full argument.
        /// </para>
        /// </summary>
        private void EnsureTransport()
        {
            if (transport != null)
            {
                Transport.active = transport;
                return;
            }

            var steam = SteamServices.Current;
            var wantSteam = steam.IsOnline
                            && steam.Transport.Kind == P2PTransportKind.SteamSockets
                            && NetTransportRegistry.HasPlatformTransport;

            if (wantSteam)
            {
                var platform = NetTransportRegistry.TryCreate(gameObject);
                if (platform != null)
                {
                    transport = platform;
                    Transport.active = platform;
                    ActiveTransportKind = NetTransportKind.SteamSockets;
                    networkAddress = steam.Transport.LocalAddress;
                    Debug.Log("[Net] Transport: " + NetTransportRegistry.BackendName + " — " + steam.Transport.Describe());
                    return;
                }
            }

            // §14 step 2. Two instances on one PC, no platform involved, and the same
            // path a contributor without Steam develops on all the way through step 3.
            var local = gameObject.GetComponent<KcpTransport>();
            if (local == null)
            {
                local = gameObject.AddComponent<KcpTransport>();
            }

            transport = local;
            Transport.active = local;
            ActiveTransportKind = NetTransportKind.Local;
            networkAddress = steam.Transport.LocalAddress;

            Debug.Log(
                "[Net] Transport: local KCP on " + networkAddress
                + (NetTransportRegistry.HasPlatformTransport
                    ? " (Steam backend present but offline: " + (steam.OfflineReason ?? "unknown") + ")"
                    : " (no platform transport compiled in)"));
        }

        private void EnsureInterestManagement()
        {
            if (GetComponent<InterestManagementBase>() == null)
            {
                gameObject.AddComponent<HorrorInterestManagement>();
            }
        }

        /// <inheritdoc />
        public override void OnStartServer()
        {
            NetSession.SetPhase(NetSessionPhase.Lobby);

            if (_lobbyPrefab != null)
            {
                var lobby = Instantiate(_lobbyPrefab);
                NetworkServer.Spawn(lobby.gameObject);
                _lobby = lobby;
            }
            else
            {
                _lobby = NetLobby.Instance;
            }
        }

        /// <inheritdoc />
        public override void OnStopServer()
        {
            // The answers die with the session. §13 keeps them host-side; leaving them
            // installed across sessions would let a second match start already knowing
            // the first one's objective, which is the same leak by a slower route.
            HostSecrets.Clear();
            _lobby = null;

            NetSession.RaiseEnded(NetSessionEndReason.LocalRequest);
        }

        /// <inheritdoc />
        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            if (_refuseLateJoins && NetSession.Phase == NetSessionPhase.InMatch)
            {
                Debug.Log("[Net] Refusing a late join: the match is already running (§07).");
                conn.Disconnect();
                return;
            }

            var lobby = _lobby ?? NetLobby.Instance;
            if (lobby == null)
            {
                return;
            }

            // §11's party is four. A fifth arrival is turned away at the door rather
            // than admitted as a spectator: there is no spectator role in §04, and a
            // fifth player in voice range would break §03's "말로 전달" constraint by
            // being an extra memory that never has to go downstairs.
            //
            // The seat's name is the transport address — which is the host's Steam id
            // on the Steam wire — and the lobby UI resolves it to a persona name
            // through ILobbyService.Members. Deliberately not a string the client
            // sends: §13 gets identity from Steam (계정 / 신원), and a self-declared
            // name is an impersonation vector even among four friends.
            if (lobby.TrySeat(conn.connectionId, conn.address) < 0)
            {
                Debug.Log("[Net] Refusing a connection: all " + GameConstants.PlayersPerMatch + " seats are taken.");
                conn.Disconnect();
            }
        }

        /// <inheritdoc />
        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            var lobby = _lobby ?? NetLobby.Instance;
            lobby?.Vacate(conn.connectionId);

            base.OnServerDisconnect(conn);
        }

        /// <summary>
        /// Spawns a player and tells it which of §04's roles it is.
        /// <para>
        /// The role comes from the lobby's <c>RoleSelection</c>, which is the core's
        /// copy and the only authoritative one. A player who has not picked yet
        /// spawns with <see cref="RoleId.None"/> and is filled in when they do —
        /// §11's choice belongs in the lobby, and blocking the spawn on it would mean
        /// a player who is deciding cannot see the others.
        /// </para>
        /// </summary>
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);

            var identity = conn.identity;
            if (identity == null)
            {
                return;
            }

            NetInterestScope.Apply(identity.gameObject, NetInterestClass.Perception);

            var lobby = _lobby ?? NetLobby.Instance;
            if (lobby == null || !identity.TryGetComponent(out NetPlayer player))
            {
                return;
            }

            var seatIndex = lobby.SeatIndexOf(conn.connectionId);
            var role = seatIndex >= 0 && seatIndex < lobby.Seats.Count ? lobby.Seats[seatIndex].Role : RoleId.None;
            player.AssignRole(seatIndex, role);
        }

        /// <inheritdoc />
        public override void OnClientConnect()
        {
            base.OnClientConnect();
            NetSession.SetPhase(NetSessionPhase.Lobby);
        }

        /// <summary>
        /// The host went away. §13's 호스트 이탈 → 세션 종료, with no migration.
        /// <para>
        /// A dedicated-server game would try to reconnect here. This one cannot: the
        /// host is the only machine that has ever held the objective's position or the
        /// clue contents (§13), so there is nothing to reconnect to and nobody who
        /// could stand in. The honest thing is to say so and go back to the menu.
        /// </para>
        /// </summary>
        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();

            // A host running in host mode also gets this on shutdown; its own
            // OnStopServer already reported the reason, so do not overwrite it.
            if (!NetworkServer.active)
            {
                NetSession.RaiseEnded(NetSessionEndReason.HostLeft);
            }
        }

        /// <inheritdoc />
        public override void OnClientError(TransportError error, string reason)
        {
            base.OnClientError(error, reason);
            Debug.LogWarning("[Net] Transport error on the client: " + error + " — " + reason);
        }

        /// <summary>
        /// Locks the lineup and moves everyone into the match. Host only.
        /// <para>
        /// Refuses an incomplete lineup, because §11's whole structure is four
        /// distinct roles out of five; a match started with a gap would have two roles
        /// absent and the section's "5개 중 4개" premise would not hold.
        /// </para>
        /// </summary>
        public bool TryBeginMatch()
        {
            if (!NetworkServer.active)
            {
                return false;
            }

            var lobby = _lobby ?? NetLobby.Instance;
            if (lobby == null || lobby.SettledSelection() == null)
            {
                return false;
            }

            NetSession.SetPhase(NetSessionPhase.InMatch);
            return true;
        }
    }

    /// <summary>Which wire a session is running on. Shown in the debug overlay.</summary>
    public enum NetTransportKind
    {
        /// <summary>KCP on localhost. §14 steps 1–3.</summary>
        Local,

        /// <summary>FizzySteamworks over Steam Networking Sockets, relayed by SDR. §13.</summary>
        SteamSockets,
    }
}
