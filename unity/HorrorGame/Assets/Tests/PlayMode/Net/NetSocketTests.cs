#nullable enable

using System;
using System.Collections;
using HorrorGame.Core;
using HorrorGame.Net;
using kcp2k;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Net
{
    /// <summary>
    /// Sent by a client that had to open a UDP socket to send it. §14 step 2.
    /// <para>
    /// A struct at namespace scope rather than a nested one, because Mirror's weaver
    /// generates the reader and the writer for every <c>NetworkMessage</c> it can see
    /// being sent, and a type it cannot resolve fails at run time with "no writer
    /// found" rather than at compile time. <c>RaceLobbyBeginMessage</c> is declared
    /// the same way, one namespace over, for the same reason.
    /// </para>
    /// </summary>
    public struct SocketProbeMessage : NetworkMessage
    {
        /// <summary>A number the host cannot invent. Chosen by the client, echoed by the host.</summary>
        public int Token;
    }

    /// <summary>The host's answer. Carries the token back, plus the connection id it arrived on.</summary>
    public struct SocketProbeReplyMessage : NetworkMessage
    {
        /// <summary>The client's own number, come back around.</summary>
        public int Token;

        /// <summary>Mirror's id for the socket the probe arrived on. Zero would mean the local (host) connection.</summary>
        public int ConnectionId;
    }

    /// <summary>
    /// Two peers, one socket, and bytes that actually left the process's transport.
    /// <para>
    /// <b>Why this file exists.</b> Everything else in this folder builds Mirror's
    /// connection objects by hand — <c>new NetworkConnectionToClient(1)</c> and
    /// <c>NetworkServer.AddConnection</c> — over a transport whose own summary says it
    /// is "standing in for a socket rather than opening one". That is the right shape
    /// for <see cref="NetTests"/>, whose subject is which bytes Mirror decides to
    /// write. It is the wrong shape for the question "can two machines talk", and
    /// answering that question with in-process objects is exactly why nobody noticed
    /// that this twenty-player race had never had two peers connected. So this fixture
    /// opens a real socket and refuses to pass without one.
    /// </para>
    /// <para>
    /// <b>Host and client in one process is not host mode.</b> Mirror's host mode wires
    /// the local client to the server with <c>LocalConnectionToServer</c> and never
    /// touches the transport, which would prove nothing. <c>NetworkServer.Listen</c> +
    /// <c>NetworkClient.Connect</c> in the same process instead produce a genuine
    /// remote connection: two UDP sockets, KCP's handshake, a connection id, and a peer
    /// address the server had to learn from a datagram. Both assertions below that could
    /// not survive host mode — <c>activeHost</c> false and the peer address being a real
    /// loopback endpoint — are there to keep it that way.
    /// </para>
    /// <para>
    /// <b>KCP and not FizzySteamworks.</b> §13 ships the Steam wire, but Fizzy needs a
    /// running Steam client and a live app id, so a test that depended on it would be a
    /// test that fails on CI and on any contributor's machine. <c>KcpTransport</c> is
    /// the fallback <see cref="HorrorGameNetworkManager"/> already chooses when Steam is
    /// not up (§14 step 2 — "Mirror 로컬 호스트 — 같은 PC 2인스턴스"), so testing it is
    /// testing the path every contributor and every CI run actually takes.
    /// </para>
    /// </summary>
    public sealed class NetSocketTests
    {
        /// <summary>
        /// Loopback, spelled as an IPv4 literal.
        /// <para>
        /// Not "localhost": <c>KcpClient.Connect</c> resolves the name and takes
        /// <c>addresses[0]</c>, which on a machine with IPv6 configured is <c>::1</c>.
        /// Paired with <see cref="DualMode"/> below that would be a client on one
        /// address family and a server on another, which fails as a timeout rather
        /// than as an error — the worst possible way for a networking test to break.
        /// </para>
        /// </summary>
        private const string Loopback = "127.0.0.1";

        /// <summary>
        /// IPv4 only, on both sockets.
        /// <para>
        /// kcp2k's default binds <c>::</c> with <c>Socket.DualMode</c>, which is right
        /// for a shipped server and one more thing that can be unavailable on a build
        /// agent (<c>DualMode</c> is documented to throw <c>NotSupportedException</c>).
        /// The question here is whether Mirror can carry a message between two peers,
        /// not whether this host has IPv6, so the test removes the variable.
        /// </para>
        /// </summary>
        private const bool DualMode = false;

        /// <summary>
        /// Port for the bare-Mirror test. Deliberately not kcp2k's default 7777.
        /// <para>
        /// A developer with the game running, or a second Unity, would be holding 7777,
        /// and <c>Socket.Bind</c> throws on a port already taken. A number well outside
        /// the range anything in this project uses makes the test's failure mean what it
        /// says instead of meaning "something else was running".
        /// </para>
        /// </summary>
        private const ushort BarePort = 47701;

        /// <summary>Port for the shipped-manager test. Distinct from <see cref="BarePort"/> so the two never race for a bind.</summary>
        private const ushort ManagerPort = 47702;

        /// <summary>
        /// Wall-clock seconds allowed for a loopback handshake plus a round trip.
        /// <para>
        /// Seconds and not frames, for the reason <c>UiFlowTests</c> already wrote down:
        /// under <c>-batchmode</c> the player loop is not waiting on a display, so a
        /// frame budget is not a time budget. KCP's interval is 10 ms and this is
        /// loopback, so a healthy connection takes a handful of frames; ten seconds is
        /// two orders of magnitude of headroom and still fails fast when the socket
        /// never comes up.
        /// </para>
        /// </summary>
        private const float ConnectSecondsBudget = 10f;

        /// <summary>
        /// Wall-clock seconds the shipped-manager test keeps running after the
        /// connection is up, doing nothing.
        /// <para>
        /// One second is roughly thirty of the manager's own ticks
        /// (<c>GameConstants.NetworkSendRate</c> is <c>30</c>), which is far more than
        /// the one or two frames a refusal or a timeout needs to come back through the
        /// transport.
        /// </para>
        /// </summary>
        private const float SettleSeconds = 1f;

        /// <summary>The token the probe carries. Non-zero and not a small integer, so a zeroed struct cannot pass for it.</summary>
        private const int ProbeToken = 0x5A3C17;

        private GameObject? _rig;
        private KcpTransport? _transport;
        private HorrorGameNetworkManager? _manager;
        private int _restoreTargetFrameRate;

        private int _serverProbesReceived;
        private int _serverProbeToken;
        private int _serverProbeConnectionId;
        private int _clientRepliesReceived;
        private SocketProbeReplyMessage _clientReply;
        private int _clientPayloadBytesSent;
        private int _serverPayloadBytesSent;

        [SetUp]
        public void SetUp()
        {
            NetSession.ResetForTests();

            // Mirror's Utils.IsHeadless() is true under -batchmode, and NetworkManager
            // answers that by pinning Application.targetFrameRate to its send rate. That
            // is a process-wide setting which would otherwise outlive this fixture and
            // slow every PlayMode test scheduled after it.
            _restoreTargetFrameRate = Application.targetFrameRate;

            // A previous fixture's interest manager may have been destroyed while these
            // still pointed at it. Mirror's own null checks handle that, but starting
            // from a known state is cheaper than reasoning about it.
            NetworkServer.aoi = null;
            NetworkClient.aoi = null;

            _serverProbesReceived = 0;
            _serverProbeToken = 0;
            _serverProbeConnectionId = -1;
            _clientRepliesReceived = 0;
            _clientReply = default;
            _clientPayloadBytesSent = 0;
            _serverPayloadBytesSent = 0;
        }

        [TearDown]
        public void TearDown()
        {
            // Order matters and is not politeness. NetworkServer.Shutdown calls
            // Transport.active.ServerStop(), so the transport component has to still be
            // alive and still be Transport.active when it runs — destroying the rig
            // first would leave the UDP socket bound for the rest of the editor session
            // and every later test would fail to bind.
            if (NetworkServer.active)
            {
                NetworkServer.Shutdown();
            }

            NetworkClient.Shutdown();

            NetworkServer.aoi = null;
            NetworkClient.aoi = null;
            NetworkManager.ResetStatics();

            if (_rig != null)
            {
                UnityEngine.Object.DestroyImmediate(_rig);
            }

            _rig = null;
            _transport = null;
            _manager = null;

            Transport.active = null;
            Application.targetFrameRate = _restoreTargetFrameRate;
            NetSession.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // §14 step 2 — two peers, one wire
        // ------------------------------------------------------------------

        /// <summary>
        /// A server listens, a client connects to it over UDP, a message goes each way,
        /// and the client leaves without the server being told twice.
        /// <para>
        /// This is the assertion the project did not have. It fails if the transport
        /// cannot bind, if KCP's handshake never completes, if Mirror's batcher never
        /// flushes, if the message id is not registered on the far side, or if the
        /// disconnect is not noticed — every one of which is a way "20-player" stops
        /// being true, and none of which an in-process connection object can catch.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AHostAndAClientMeetOverARealSocketAndAMessageCrossesBothWays()
        {
            BuildTransport(BarePort);
            Wake();

            // The transport's own send hooks, so the test can say bytes left through the
            // wire rather than inferring it from a callback that could have been invoked
            // in-process. KcpTransport raises these from ClientSend/ServerSend only —
            // KCP's own handshake traffic does not appear here, so a non-zero count is
            // Mirror-level payload and nothing else.
            _transport!.OnClientDataSent += (segment, channelId) => _clientPayloadBytesSent += segment.Count;
            _transport!.OnServerDataSent += (connectionId, segment, channelId) => _serverPayloadBytesSent += segment.Count;

            // §11's field, as the socket layer would have to carry it. Passing the race
            // maximum rather than a convenient 1 means this test also proves Mirror was
            // asked to hold twenty — which is the number RaceLobby sets and the number
            // GameConstants.PlayersPerMatch (4) does not.
            NetworkServer.Listen(GameConstants.RaceRunnersMax);

            // requireAuthentication: false on both sides, and it is not laziness. Mirror
            // only marks a connection authenticated from NetworkManager.OnServerAuthenticated;
            // this test deliberately runs without a NetworkManager so that a failure here
            // is a transport failure and cannot be a manager configuration failure. The
            // shipped path is covered by the next test, which does have one.
            NetworkServer.RegisterHandler<SocketProbeMessage>(OnServerProbe, requireAuthentication: false);

            NetworkClient.Connect(Loopback);
            NetworkClient.RegisterHandler<SocketProbeReplyMessage>(OnClientReply, requireAuthentication: false);

            yield return WaitFor(() => NetworkClient.isConnected && NetworkServer.connections.Count == 1);

            Assert.That(NetworkClient.isConnected, Is.True,
                "The client never reached Connected on " + Loopback + ":" + BarePort + " within "
                + ConnectSecondsBudget + " s. Either KcpTransport could not bind (another process on that port, or "
                + "a host firewall refusing a UDP listener) or the handshake datagrams never arrived.");

            Assert.That(NetworkServer.connections.Count, Is.EqualTo(1),
                "The client thinks it is connected but the server has no connection for it. That is the shape of a "
                + "one-way socket — a client bound to a different address family than the server, most often.");

            // --- the four assertions that a host-mode fake could not satisfy ---

            Assert.That(NetworkClient.activeHost, Is.False,
                "NetworkClient.activeHost means the connection is a LocalConnectionToServer — Mirror's in-process "
                + "host wire, which never touches the transport. This test is worthless if it is true.");

            Assert.That(NetworkServer.localConnection, Is.Null,
                "A local connection on the server is the same fake seen from the other end.");

            var conn = FirstConnection();
            Assert.That(conn, Is.Not.InstanceOf<LocalConnectionToClient>(),
                "The server's connection has to be a remote one for any of this to mean anything.");

            Assert.That(conn.address, Does.Contain(Loopback),
                "The server's idea of where this client is comes from KcpTransport.ServerGetClientAddress, which reads "
                + "the IPEndPoint a datagram actually arrived from. A fabricated connection has no endpoint to report. "
                + "Got: '" + conn.address + "'.");

            // --- a message crosses, and comes back ---

            NetworkClient.Send(new SocketProbeMessage { Token = ProbeToken });

            yield return WaitFor(() => _clientRepliesReceived > 0);

            Assert.That(_serverProbesReceived, Is.EqualTo(1),
                "The probe never reached the server. Mirror batches sends and flushes them in NetworkLateUpdate, so "
                + "this failing while the connection is up points at the batch never being flushed or the message id "
                + "not being registered server-side.");

            Assert.That(_serverProbeToken, Is.EqualTo(ProbeToken),
                "The server received a probe but not this one's token — the payload did not survive the wire.");

            Assert.That(_serverProbeConnectionId, Is.Not.EqualTo(0),
                "Connection id 0 is Mirror's reserved id for the host's own local connection. A remote client must "
                + "have been given a real one.");

            Assert.That(_clientRepliesReceived, Is.EqualTo(1),
                "The server answered but the client never heard it. The wire is one-way, which is worse than dead.");

            Assert.That(_clientReply.Token, Is.EqualTo(ProbeToken),
                "The answer came back carrying a different number than the one that went out.");

            Assert.That(_clientReply.ConnectionId, Is.EqualTo(_serverProbeConnectionId),
                "The reply was addressed to a different connection than the probe arrived on.");

            Assert.That(_clientPayloadBytesSent, Is.GreaterThan(0),
                "KcpTransport.ClientSend never ran, so nothing was handed to a socket. Whatever moved, moved in "
                + "process memory.");

            Assert.That(_serverPayloadBytesSent, Is.GreaterThan(0),
                "KcpTransport.ServerSend never ran. Same failure, other direction.");

            // --- and it ends cleanly ---

            NetworkClient.Disconnect();

            yield return WaitFor(() => !NetworkClient.isConnected && NetworkServer.connections.Count == 0);

            Assert.That(NetworkClient.isConnected, Is.False,
                "The client did not come down when asked. §13 ends a session rather than migrating it, so a leave "
                + "that does not take is a session that cannot end.");

            Assert.That(NetworkServer.connections.Count, Is.EqualTo(0),
                "The server still holds the connection after the client left. A seat that is never vacated is a seat "
                + "the twenty-first runner is refused for.");
        }

        /// <summary>
        /// The same handshake, but through <see cref="HorrorGameNetworkManager"/> — the
        /// component the game actually ships — rather than around it.
        /// <para>
        /// The previous test proves Mirror and the transport work here. This one proves
        /// the project's own manager does: that <c>EnsureTransport</c> leaves a usable
        /// wire, that <c>OnStartServer</c> and <c>OnServerConnect</c> do not turn a real
        /// arrival away, and that both ends land in <see cref="NetSessionPhase.Lobby"/>.
        /// Between them, the failure the repo keeps repeating — code that reviews clean
        /// and is not in the artefact — has nowhere left to hide on this path.
        /// </para>
        /// <para>
        /// <b>StartServer then StartClient, not StartHost.</b> <c>StartHost</c> would
        /// give the manager an in-process local client and never open a socket. Mirror's
        /// two guards are <c>NetworkServer.active</c> and <c>NetworkClient.active</c>
        /// respectively, so a server-only start followed by a client start is legal and
        /// is the only arrangement in one process that puts a real datagram between them.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheShippedNetworkManagerAcceptsARealRemoteClient()
        {
            BuildTransport(ManagerPort);

            // Assigned before the object wakes, so HorrorGameNetworkManager.EnsureTransport
            // takes its "transport already set" branch. Letting it build its own KcpTransport
            // would put the session on kcp2k's default port 7777 with DualMode on, which is
            // the shipped configuration but not a testable one — see BarePort and DualMode.
            _manager = _rig!.AddComponent<HorrorGameNetworkManager>();
            _manager.transport = _transport;

            // §04's roles are gone, so there is nothing to spawn a body for and this
            // project has no player prefab; RaceLobby turns the same flag off for the
            // same reason (RaceLobby.EnsureManager). Left on, Mirror logs an error the
            // moment the client asks for a player and the test fails on the log, not on
            // the network.
            _manager.autoCreatePlayer = false;

            Wake();
            yield return null;

            // After Awake, not before: EnsureTransport writes networkAddress from the
            // Steam layer's LocalAddress on the branch where it builds its own transport,
            // and this test would rather be immune to which branch it took than depend on
            // having predicted it.
            _manager.networkAddress = Loopback;

            Assert.That(_manager.maxConnections, Is.EqualTo(GameConstants.RaceRunnersMax),
                "HorrorGameNetworkManager.Awake sets maxConnections, and StartServer hands it to "
                + "NetworkServer.Listen — this is the cap a socket actually enforces in the shipped build. It read "
                + "GameConstants.PlayersPerMatch (" + GameConstants.PlayersPerMatch + ") until the co-operative "
                + "game was pivoted away, and was corrected afterwards by RaceLobby.EnsureManager — which does run, "
                + "but only for a server started through the lobby's 호스트 button. §11's field is "
                + GameConstants.RaceRunnersMax + " for every server this component starts, including this one.");

            _manager.StartServer();

            Assert.That(NetworkServer.active, Is.True,
                "StartServer did not bring the server up. EnsureTransport chose "
                + _manager.ActiveTransportKind + ".");

            Assert.That(NetSession.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "OnStartServer moves the session into §11's lobby phase. Without that, OnServerConnect's late-join "
                + "guard has no phase to read.");

            _manager.StartClient();

            yield return WaitFor(() => NetworkClient.isConnected && NetworkServer.connections.Count == 1);

            Assert.That(NetworkClient.isConnected, Is.True,
                "The shipped manager's client never connected to the shipped manager's server on " + Loopback + ":"
                + ManagerPort + ". This is the first time in this project's history that this has been asked.");

            Assert.That(NetworkServer.connections.Count, Is.EqualTo(1),
                "The manager's server did not keep the connection. HorrorGameNetworkManager.OnServerConnect refuses a "
                + "connection when the phase is InMatch (§07) or when every seat is taken (§11) — check which.");

            Assert.That(NetworkClient.activeHost, Is.False,
                "StartServer + StartClient must not have collapsed into host mode; if it did, no socket was used.");

            var conn = FirstConnection();
            Assert.That(conn.address, Does.Contain(Loopback),
                "The manager's server has no real endpoint for its client. Got: '" + conn.address + "'.");

            Assert.That(conn.isAuthenticated, Is.True,
                "NetworkManager authenticates immediately when no Authenticator is installed, and every default "
                + "Mirror handler requires it. An unauthenticated connection is one that will be dropped the first "
                + "time it says anything.");

            // Then keep running for a while with nobody touching anything. A refusal —
            // OnServerConnect calling conn.Disconnect() — arrives as a transport event on
            // a later tick, so a connection that is only asserted on the frame it opened
            // is a connection that has not been shown to hold.
            yield return WaitSeconds(SettleSeconds);

            Assert.That(NetworkServer.connections.Count, Is.EqualTo(1),
                "The connection was accepted and then dropped. That is what a late-join refusal or a full lobby looks "
                + "like from here.");

            _manager.StopClient();

            yield return WaitFor(() => NetworkServer.connections.Count == 0);

            Assert.That(NetworkServer.connections.Count, Is.EqualTo(0),
                "OnServerDisconnect did not clear the connection, so §13's 'host leaving ends the session' has "
                + "nothing to end.");
        }

        // ------------------------------------------------------------------
        // rig
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the wire the test runs on, and leaves it asleep.
        /// <para>
        /// The GameObject is created inactive on purpose: <c>KcpTransport.Awake</c>
        /// freezes its <c>KcpConfig</c> from the serialised fields, so a port and a
        /// DualMode assigned after <c>AddComponent</c> on a live object would be
        /// remembered by the inspector and ignored by the socket — the transport would
        /// bind 7777 with IPv6 no matter what this file says. Adding the component
        /// while the object is inactive defers <c>Awake</c> until
        /// <see cref="Wake"/>, which is the only window in which these values can still
        /// be chosen. Anything else that has to exist before <c>Awake</c> — the
        /// <see cref="HorrorGameNetworkManager"/> and its transport field — goes on in
        /// the same window.
        /// </para>
        /// </summary>
        private void BuildTransport(ushort port)
        {
            _rig = new GameObject("NetSocketRig");
            _rig.SetActive(false);

            _transport = _rig.AddComponent<KcpTransport>();
            _transport.port = port;
            _transport.DualMode = DualMode;
        }

        /// <summary>Runs every <c>Awake</c> on the rig and publishes the wire.</summary>
        private void Wake()
        {
            _rig!.SetActive(true);
            Transport.active = _transport;
        }

        private void OnServerProbe(NetworkConnectionToClient conn, SocketProbeMessage message)
        {
            _serverProbesReceived++;
            _serverProbeToken = message.Token;
            _serverProbeConnectionId = conn.connectionId;

            conn.Send(new SocketProbeReplyMessage
            {
                Token = message.Token,
                ConnectionId = conn.connectionId,
            });
        }

        private void OnClientReply(SocketProbeReplyMessage message)
        {
            _clientRepliesReceived++;
            _clientReply = message;
        }

        private static NetworkConnectionToClient FirstConnection()
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                return conn;
            }

            throw new InvalidOperationException("No connection on the server.");
        }

        /// <summary>
        /// Runs frames until <paramref name="condition"/> holds or the budget expires.
        /// Never asserts — the caller does, so that the failure message can say what was
        /// being waited for.
        /// </summary>
        private static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + ConnectSecondsBudget;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        /// <summary>
        /// Runs frames for a fixed stretch of wall clock. Frame-driven rather than
        /// <c>WaitForSeconds</c>, because the transport only ticks inside Mirror's
        /// player-loop hooks and a coroutine that sleeps does not run them.
        /// </summary>
        private static IEnumerator WaitSeconds(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }
    }
}
