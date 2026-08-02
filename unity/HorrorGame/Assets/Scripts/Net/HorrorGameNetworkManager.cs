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

        [Tooltip("Give every connection a networked runner as soon as it reports ready. §01's race has no "
                 + "spectators, so a connection without a body is a player who cannot start.")]
        [SerializeField]
        private bool _spawnRunnerForEachPlayer = true;

        private NetLobby? _lobby;

        /// <summary>Which wire this session is running on.</summary>
        public NetTransportKind ActiveTransportKind { get; private set; } = NetTransportKind.Local;

        /// <summary>The lobby object, on the host. Null on a client and before the server starts.</summary>
        public NetLobby? Lobby => _lobby;

        /// <inheritdoc />
        public override void Awake()
        {
            // ---------------------------------------------------------------
            // §11's field, on the component that owns the number.
            //
            // This is the value a socket actually enforces: NetworkManager.StartServer
            // hands it to NetworkServer.Listen, and NetworkServer.IsConnectionAllowed
            // kicks anything past it before a NetworkConnectionToClient is ever built.
            //
            // It used to read GameConstants.PlayersPerMatch — the co-operative game's
            // four — and be corrected afterwards by RaceLobby.EnsureManager, which
            // writes GameConstants.RaceRunnersMax over it. Two things were wrong with
            // that, and only one of them is the obvious one. RaceLobby.EnsureManager
            // does run: RaceLobby.Install is a [RuntimeInitializeOnLoadMethod] that
            // hands LobbyEntry its Intercept, GameShell.BeginFromMenu calls
            // LobbyEntry.TryOpen (LobbyEntryWiringTests asserts exactly that), and
            // RequestHost / RequestJoin call EnsureManager. So a human pressing 시작
            // and then 호스트 did get twenty. What did not was every other way a
            // server can start — a manager placed in a scene, a StartServer from a
            // test or a tool, any future entry point — because each of those would
            // have to remember to repeat a line that lives in another assembly. §11's
            // ceiling is a property of this component, not a courtesy the lobby does
            // for it.
            //
            // Twenty is a MAP limit before it is a network one (§12-A fixes the gates
            // at 4 → 2 → 1 and §11 refuses to widen them with the field), which is why
            // it is RaceRunnersMax and not "as many as the transport will hold".
            // ---------------------------------------------------------------
            maxConnections = GameConstants.RaceRunnersMax;

            // §13's budget argument depends on the traffic staying inside what Steam
            // relays for free, and §05's five synced rows are cheap at this rate.
            sendRate = GameConstants.NetworkSendRate;

            // ---------------------------------------------------------------
            // §01 puts the field round the rim, not on one square of it.
            //
            // Mirror's default is PlayerSpawnMethod.Random, which is the enum's zero and
            // therefore what an unset inspector gives. Random is wrong here and the
            // arithmetic is not close: DescentMap offers B1's whole outer ring and the
            // generated scene resolves to 28 distinct points, so drawing twenty of them
            // with replacement lands at least two runners on the same marker with
            // probability 1 − 28!/(8!·28²⁰) = 0.99991. That is not a rare collision, it is
            // the expected outcome — the birthday problem with twenty people and
            // twenty-eight days.
            //
            // RoundRobin walks NetRaceStartPoints' de-duplicated list in hierarchy order
            // and cannot repeat until it has used all of it, which is exactly §11's
            // requirement: twenty runners, twenty places, and the field spread round the
            // ring rather than queueing along one arc of it.
            // ---------------------------------------------------------------
            playerSpawnMethod = PlayerSpawnMethod.RoundRobin;

            // ---------------------------------------------------------------
            // The transport COMPONENT has to exist before base.Awake(), because
            // Mirror's InitializeSingleton refuses to finish without one:
            //
            //     Debug.LogError("No Transport on Network Manager...");
            //     return false;                        // NetworkManager.cs:736
            //
            // and Awake() early-returns on that false. It then never runs
            // ApplyConfiguration() — send rate, tick rate, disconnect timeouts,
            // the NetworkServer and NetworkClient settings — and never subscribes
            // SceneManager.sceneLoaded += OnSceneLoaded.
            //
            // This was the shipped 호스트 button's behaviour: RaceLobby.EnsureManager
            // AddComponents this manager onto a bare object, so the error fired on
            // every press and the session came up on a manager Mirror had never
            // configured. Every existing net test missed it because they all build
            // the object, AddComponent<KcpTransport>() and THEN the manager — the
            // opposite order from the one the game uses.
            //
            // Only the component is attached here. Claiming Transport.active is
            // still left until after the duplicate check below, for the reason that
            // check was written: a manager that is about to destroy itself must not
            // leave the global pointing at a component that goes away with it.
            // Mirror's own duplicate branch returns before touching Transport.active,
            // so an attached-but-unclaimed transport is safe across it.
            // ---------------------------------------------------------------
            AttachTransport();

            base.Awake();

            // After base.Awake(), because a duplicate manager destroys itself in
            // there — and a destroyed manager must not have left Transport.active
            // pointing at a component that is about to go away.
            if (singleton != this)
            {
                return;
            }

            // Mirror's auto-create path Instantiates playerPrefab and logs an error
            // when it is null. There is no runner prefab asset in this project and
            // NetRunner explains why there cannot be one yet, so the body is built in
            // OnServerReady instead — the same moment in the handshake, one message
            // earlier. Left on with an empty prefab field, every join would fail with
            // "The PlayerPrefab is empty on the NetworkManager".
            if (playerPrefab == null)
            {
                autoCreatePlayer = false;
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
            AttachTransport();

            // AttachTransport always leaves one, so this is the whole of the work
            // that is left: claim the global and say which wire it is. Split out of
            // the choosing so the component can exist before base.Awake() without
            // the global being claimed by a manager that may destroy itself there.
            Transport.active = transport;

            Debug.Log(
                ActiveTransportKind == NetTransportKind.SteamSockets
                    ? "[Net] Transport: " + NetTransportRegistry.BackendName
                      + " — " + SteamServices.Current.Transport.Describe()
                    : "[Net] Transport: local KCP on " + networkAddress
                      + (NetTransportRegistry.HasPlatformTransport
                          ? " (Steam backend present but offline: "
                            + (SteamServices.Current.OfflineReason ?? "unknown") + ")"
                          : " (no platform transport compiled in)"));
        }

        /// <summary>
        /// Chooses the wire and attaches it, touching nothing global.
        /// <para>
        /// Separate from <see cref="EnsureTransport"/> because Mirror needs the
        /// component to be on the object before <c>base.Awake()</c> — see the note
        /// there — while <c>Transport.active</c> must not be claimed until the
        /// duplicate-manager check has run. Idempotent: called from both.
        /// </para>
        /// </summary>
        private void AttachTransport()
        {
            if (transport != null)
            {
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
                    ActiveTransportKind = NetTransportKind.SteamSockets;
                    networkAddress = steam.Transport.LocalAddress;
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
            ActiveTransportKind = NetTransportKind.Local;
            networkAddress = steam.Transport.LocalAddress;
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

            // §01's starting line. Installed rather than scanned once, because on the
            // shipped path this runs in the bootstrap scene — the building the runners
            // start in does not exist yet, and the markers arrive with it. See
            // NetRaceStartPoints.Install.
            NetRaceStartPoints.Install();

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

            // The ring belongs to the building this session ran in. Mirror's start
            // position list is static and would otherwise carry a dead layout's markers
            // into the next match.
            NetRaceStartPoints.Uninstall();

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

            // The seat's name is the transport address — which is the host's Steam id
            // on the Steam wire — and the lobby UI resolves it to a persona name
            // through ILobbyService.Members. Deliberately not a string the client
            // sends: §13 gets identity from Steam (계정 / 신원), and a self-declared
            // name is an impersonation vector even among four friends.
            //
            // A full lobby no longer disconnects the arrival, and that is the pivot
            // showing through rather than a relaxation. NetLobby is §04's table: four
            // seats, five roles, exactly one absent. §04's roles were deleted when
            // this became §01's twenty-runner race, so "all four seats are taken" is
            // now a statement about a mode that no longer exists.
            //
            // It is a latent cap rather than an active one today, and that is worth
            // saying precisely: no lobby is spawned on the race path — _lobbyPrefab is
            // empty on the manager RaceLobby builds at runtime, and NetLobby.Instance
            // is null until something spawns one — so this branch is currently
            // unreachable. It would arm itself the day anybody spawns a NetLobby, and
            // then the fifth runner would be refused with maxConnections reading
            // twenty and no line in the log explaining it. The ceiling that counts is
            // §11's, and Mirror enforces it in NetworkServer.IsConnectionAllowed from
            // the value Awake set above.
            if (lobby.TrySeat(conn.connectionId, conn.address) < 0)
            {
                Debug.Log(
                    "[Net] Runner " + conn.connectionId + " joined without a §04 lobby seat: all "
                    + GameConstants.PlayersPerMatch + " are taken. §11's field is "
                    + GameConstants.RaceRunnersMax + " and the race needs no seat, so this is not a refusal.");
            }
        }

        /// <summary>
        /// Gives an arriving connection a body. §01's race has no spectators.
        /// <para>
        /// <b>Why <c>OnServerReady</c> and not <c>OnServerAddPlayer</c>.</b> Mirror
        /// reaches <c>OnServerAddPlayer</c> only through
        /// <c>OnServerAddPlayerInternal</c>, which refuses — with a
        /// <c>Debug.LogError</c> — when <c>autoCreatePlayer</c> is on and
        /// <c>playerPrefab</c> is null. This project has no runner prefab asset (see
        /// <see cref="NetRunner"/>), so <c>Awake</c> turns that flag off and the body
        /// is added one message earlier, on the ready that every client sends anyway.
        /// <c>NetworkServer.AddPlayerForConnection</c> sets the connection ready
        /// itself, so nothing in the handshake is skipped.
        /// </para>
        /// <para>
        /// The asset id is <see cref="NetRunner.AssetId"/> rather than a prefab's,
        /// which is the whole reason a client can build the same object: the spawn
        /// message carries that number and every client registered a handler for it
        /// in <see cref="OnStartClient"/>.
        /// </para>
        /// <para>
        /// <b>A body exists from ready, not from descent.</b> <c>RaceLobby</c> says "a
        /// lobby spawns nobody", and this arrives one phase earlier than that sentence
        /// expects: a runner is created while the party is still in §11's lobby and
        /// stands at the origin until the map places it. That is deliberate — the
        /// client sends <c>ReadyMessage</c> exactly once, at connect, so a spawn gated
        /// on <see cref="NetSessionPhase.InMatch"/> would never fire at all. The
        /// standing cost is one spawn message and one idle object per member —
        /// <see cref="NetPlayer"/> reports nothing until an owner binds a view source,
        /// so its SyncVars never go dirty in a lobby — and that is the honest price of
        /// having one spawn path instead of two.
        /// <see cref="_spawnRunnerForEachPlayer"/> turns it off for anything that wants
        /// the other trade.
        /// </para>
        /// </summary>
        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            if (_spawnRunnerForEachPlayer && conn.identity == null && TryAddRunner(conn))
            {
                // AddPlayerForConnection already called SetClientReady and spawned the
                // observers, so calling base here would send every observed object a
                // second time.
                return;
            }

            base.OnServerReady(conn);
        }

        /// <summary>
        /// Builds one runner and hands it to Mirror. Destroys it again if Mirror
        /// refuses — an object with a <c>NetworkIdentity</c> that was never spawned is
        /// a leak that survives the session, because nothing on the server's side
        /// knows to clean it up.
        /// </summary>
        private bool TryAddRunner(NetworkConnectionToClient conn)
        {
            var runner = NetRunner.Build("NetRunner [connId=" + conn.connectionId + "]");

            // §01 starts the field on the rim of B1. Mirror's start positions are the
            // engine-level way to say that, and NetRaceStartPoints is what fills them
            // from the generated map's PlayerSpawn markers — this used to return null on
            // every call in the project's history, which is why twenty runners appeared
            // inside each other at (0, 0, 0).
            //
            // It still returns null when a runner is added before the building exists,
            // which on the shipped path is every runner: a client sends ReadyMessage once,
            // at connect, and that is while the party is in §11's lobby. Those bodies are
            // put on the ring by NetRaceStartPoints.PlaceSpawnedRunners the moment the
            // descent's scene loads, through NetPlayer.TeleportTo — which exists precisely
            // so a placement is not mistaken for a client claiming to have run there.
            var start = GetStartPosition();
            if (start != null)
            {
                runner.transform.SetPositionAndRotation(start.position, start.rotation);
            }

            if (NetworkServer.AddPlayerForConnection(conn, runner, NetRunner.AssetId))
            {
                return true;
            }

            Destroy(runner);
            return false;
        }

        /// <summary>
        /// Teaches this machine how to build a runner somebody else owns.
        /// <para>
        /// Registered here rather than in <c>Awake</c> because
        /// <c>NetworkClient.Shutdown</c> clears the spawn handler table — anything
        /// registered before a session would be gone by the second one, which is the
        /// same trap <c>RaceLobby.RegisterClientHandlers</c> already documents for
        /// message handlers.
        /// </para>
        /// </summary>
        public override void OnStartClient()
        {
            base.OnStartClient();
            NetRunner.RegisterClientSpawnHandler();
        }

        /// <inheritdoc />
        public override void OnStopClient()
        {
            NetRunner.UnregisterClientSpawnHandler();
            base.OnStopClient();
        }

        /// <inheritdoc />
        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            var lobby = _lobby ?? NetLobby.Instance;
            lobby?.Vacate(conn.connectionId);

            base.OnServerDisconnect(conn);
        }

        /// <summary>
        /// Spawns a player from a <c>playerPrefab</c> and tells it which of §04's
        /// roles it is.
        /// <para>
        /// <b>Not the path the race takes.</b> Mirror only reaches this when
        /// <c>autoCreatePlayer</c> is on, which <c>Awake</c> turns off while there is
        /// no prefab — and there is none. §01's runner is added in
        /// <see cref="OnServerReady"/> from <see cref="NetRunner"/> instead. This
        /// override is kept because the day a runner prefab exists it becomes the
        /// better path, and because deleting it would delete the role assignment
        /// below with it.
        /// </para>
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
