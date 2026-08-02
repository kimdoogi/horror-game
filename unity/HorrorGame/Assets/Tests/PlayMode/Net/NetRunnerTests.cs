#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
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
    /// A second runner, in another peer's world, moving because something crossed a
    /// UDP socket.
    /// <para>
    /// <b>Why this file exists.</b> <see cref="NetSocketTests"/> proved two peers can
    /// meet and exchange a message. It did not prove there is anything to be a peer
    /// <em>about</em>: at the commit before this one the project contained zero
    /// prefabs with a <c>NetworkIdentity</c>, so a host and a client shook hands over
    /// a real socket and then had nothing to say to each other. This fixture asserts
    /// on the thing the game is actually made of — one runner owned by each machine,
    /// replicated to the other, moving.
    /// </para>
    /// <para>
    /// <b>Both directions, because they fail differently.</b> §13 puts the host in
    /// charge, so a runner's position travels twice: the owner reports it with a
    /// <c>Command</c> (client → host, <c>NetPlayer.CmdReportView</c>), the host clamps
    /// it and every observer receives the host's value as a SyncVar (host → client).
    /// A test that only checked one of those would pass with the other silently dead,
    /// and the dead one is not the same bug: no Command means nobody can move, no
    /// SyncVar means everybody moves alone.
    /// </para>
    /// <para>
    /// <b>What makes this un-fakeable.</b> Three things, and none of them is a byte
    /// count on its own. (1) <c>NetworkClient.activeHost</c> is false and the server
    /// has no local connection, so Mirror's in-process host wire — which never
    /// touches a transport — is not what carried this. (2) The two copies of each
    /// runner are asserted to be different <c>GameObject</c>s sharing one
    /// <c>netId</c>; there is exactly one line of code that writes the server copy's
    /// replicated position, and it is a <c>[Command]</c> body. (3) The transport's own
    /// send hooks are counted, so the claim "bytes left this process" is measured at
    /// <c>KcpTransport.ClientSend</c>/<c>ServerSend</c> rather than inferred.
    /// </para>
    /// </summary>
    public sealed class NetRunnerTests
    {
        /// <summary>Loopback as an IPv4 literal, for the reason <see cref="NetSocketTests"/> writes down: a name resolves to ::1 on an IPv6 host and the failure is a timeout.</summary>
        private const string Loopback = "127.0.0.1";

        /// <summary>IPv4 only on both sockets. <c>Socket.DualMode</c> is documented to throw on platforms without IPv6, and this test is not about IPv6.</summary>
        private const bool DualMode = false;

        /// <summary>
        /// Port for the two-runner test. In the same 477xx block
        /// <see cref="NetSocketTests"/> uses and distinct from its 47701/47702, so a
        /// fixture that leaked a bound socket fails as "could not bind" in the test
        /// that leaked it rather than in this one.
        /// </summary>
        private const ushort RunnerPort = 47703;

        /// <summary>Port for the twenty-connection test. Distinct again, for the same reason.</summary>
        private const ushort CapPort = 47704;

        /// <summary>
        /// Port for the owner-reports test. A third number rather than reusing
        /// <see cref="RunnerPort"/>: UDP has no TIME_WAIT, so reuse should be safe, and
        /// "should be safe" is how a networking suite starts failing one run in twenty
        /// for a reason nobody can reproduce.
        /// </summary>
        private const ushort OwnerPort = 47705;

        /// <summary>
        /// Socket buffer bytes asked for by each bare KCP peer in the cap test.
        /// <para>
        /// kcp2k's default is 7 MB in each direction, sized for a shipped server's one
        /// socket. Twenty-one loopback peers that send a handshake and then nothing
        /// would ask the OS for about 300 MB of kernel buffers between them, which is
        /// a good way to make a test fail on a build agent for a reason that has
        /// nothing to do with the assertion. 64 KB is far more than a handshake needs.
        /// </para>
        /// </summary>
        private const int PeerSocketBufferBytes = 64 * 1024;

        /// <summary>Wall-clock seconds for a loopback handshake plus a spawn round trip. Seconds and not frames: under -batchmode the player loop is not waiting on a display.</summary>
        private const float ConnectSecondsBudget = 10f;

        /// <summary>Wall-clock seconds spent doing nothing after an assertion, to catch a state that was true for one frame and then undone.</summary>
        private const float SettleSeconds = 1f;

        /// <summary>Where the host's own runner is put. Non-zero on two axes so a copy stuck at the origin cannot pass for it.</summary>
        private static readonly Vector3 HostRunnerStart = new Vector3(2f, 0f, 3f);

        /// <summary>
        /// Where the host moves its runner to. Eight metres away — two orders of
        /// magnitude past <see cref="ArrivalToleranceMetres"/>, so "the client's copy
        /// is within tolerance of the target" cannot be satisfied by a copy that never
        /// moved.
        /// </summary>
        private static readonly Vector3 HostRunnerArrival = new Vector3(10f, 0f, 3f);

        /// <summary>
        /// Where the owning client says its runner is. Within
        /// <c>NetInterestScope.PerceptionRange</c> (40 m) of the host's runner, because
        /// §13's interest rules stop replicating past it and this test is not about
        /// culling.
        /// </summary>
        private static readonly Vector3 ReportedRunnerPosition = new Vector3(-4f, 0f, 6f);

        /// <summary>Yaw the owner claims. §05 sends it because a teammate's beam is a pointer.</summary>
        private const float ReportedYawDegrees = 137.5f;

        /// <summary>Pitch the owner claims, looking down. §05 insists on pitch: sweeping the floor and sweeping the ceiling are two different sentences.</summary>
        private const float ReportedPitchDegrees = -21.25f;

        /// <summary>Stamina the owner has. Halfway, so a quantiser that returned either end would be caught.</summary>
        private const float ReportedStaminaFraction = 0.5f;

        /// <summary>
        /// How far a replicated position may sit from the value that was sent: one
        /// send interval of the fastest thing in the game
        /// (<c>RunnerSprintSpeed</c> / <c>NetworkSendRate</c> = 5.6 / 30 ≈ 0.19 m).
        /// <para>
        /// Derived rather than picked, and derived from the two constants that decide
        /// it: that is the largest gap the smoothing in
        /// <c>NetPlayer.ApplyRemoteView</c> can still be blamed for. Anything larger
        /// is a value that did not arrive, not a value that arrived late.
        /// </para>
        /// </summary>
        private static readonly float ArrivalToleranceMetres =
            GameConstants.RunnerSprintSpeed / GameConstants.NetworkSendRate;

        /// <summary>
        /// How far a replicated angle may sit from the one that was sent. Yaw is
        /// quantised to a <c>ushort</c> over 360°, so one step is 360/65535 ≈ 0.0055°;
        /// this is a little under two steps and still four orders of magnitude below
        /// anything a player can aim.
        /// </summary>
        private const float AngleToleranceDegrees = 0.01f;

        private GameObject? _rig;
        private KcpTransport? _transport;
        private HorrorGameNetworkManager? _manager;
        private int _restoreTargetFrameRate;

        private readonly List<GameObject> _serverSpawned = new List<GameObject>();
        private readonly List<KcpClient> _peers = new List<KcpClient>();

        private int _clientPayloadBytesSent;
        private int _serverPayloadBytesSent;

        [SetUp]
        public void SetUp()
        {
            NetSession.ResetForTests();

            // Mirror pins Application.targetFrameRate to its send rate under
            // -batchmode. That is process-wide and would otherwise slow every PlayMode
            // test scheduled after this fixture.
            _restoreTargetFrameRate = Application.targetFrameRate;

            NetworkServer.aoi = null;
            NetworkClient.aoi = null;

            _clientPayloadBytesSent = 0;
            _serverPayloadBytesSent = 0;

            // A previous fixture in this assembly may have left a runner handler
            // installed against a NetworkClient that has since shut down. Registering
            // over one logs a warning that reads like a defect; starting clean is
            // cheaper than explaining it.
            NetRunner.UnregisterClientSpawnHandler();
            NetRunner.VisualFactory = null;
        }

        [TearDown]
        public void TearDown()
        {
            // Order matters and is not politeness. NetworkServer.Shutdown calls
            // Transport.active.ServerStop(), so the transport component has to still
            // be alive when it runs — destroying the rig first would leave the UDP
            // socket bound for the rest of the editor session.
            if (NetworkServer.active)
            {
                NetworkServer.Shutdown();
            }

            NetworkClient.Shutdown();

            for (var i = 0; i < _peers.Count; i++)
            {
                _peers[i].Disconnect();
            }

            _peers.Clear();

            NetworkServer.aoi = null;
            NetworkClient.aoi = null;
            NetworkManager.ResetStatics();

            for (var i = 0; i < _serverSpawned.Count; i++)
            {
                if (_serverSpawned[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_serverSpawned[i]);
                }
            }

            _serverSpawned.Clear();

            if (_rig != null)
            {
                UnityEngine.Object.DestroyImmediate(_rig);
            }

            _rig = null;
            _transport = null;
            _manager = null;

            NetRunner.VisualFactory = null;
            Transport.active = null;
            Application.targetFrameRate = _restoreTargetFrameRate;
            NetSession.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // the host's runner, on the client's machine
        // ------------------------------------------------------------------

        /// <summary>
        /// The host spawns a runner, a remote client builds its own copy of it, and
        /// when the host moves its copy the client's copy follows.
        /// <para>
        /// This is the assertion the project has never had. It fails if
        /// <see cref="NetRunner.AssetId"/> disagrees between the two ends, if the
        /// client never registered a spawn handler for it, if
        /// <c>HorrorInterestManagement</c> never makes the client an observer, if the
        /// SyncVar is not dirtied, or if the batch is never flushed — and every one of
        /// those is a way "20-player race" quietly means "one player and nineteen
        /// invisible ones".
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheHostsRunnerAppearsOnTheClientAndMovesWhenTheHostMovesIt()
        {
            yield return StartServerAndClient(RunnerPort);

            // The host's own body. Spawned unowned, which is what a remote client sees
            // of the host in a session: an object nobody on this machine reports for
            // and whose position is the host's word.
            var hostRunner = NetRunner.Build("HostRunner");
            hostRunner.transform.position = HostRunnerStart;
            _serverSpawned.Add(hostRunner);
            NetworkServer.Spawn(hostRunner, NetRunner.AssetId);

            var hostIdentity = hostRunner.GetComponent<NetworkIdentity>();
            Assert.That(hostIdentity, Is.Not.Null,
                "NetRunner.Build must produce a NetworkIdentity — that is the entire point of it. Without one, "
                + "NetworkServer.Spawn logs 'has no NetworkIdentity' and nothing is replicated.");

            Assert.That(hostIdentity!.netId, Is.Not.EqualTo(0u),
                "NetworkServer.Spawn did not give the runner a netId, so it was never spawned. Check the console "
                + "for 'is a prefab, it can't be spawned' or 'NetworkServer is not active'.");

            Assert.That(hostIdentity.assetId, Is.EqualTo(NetRunner.AssetId),
                "The spawn message carries this number and the client looks its handler up by it. If it is not "
                + "NetRunner.AssetId, no client can build this object.");

            // The client only becomes an observer once it has a body of its own —
            // HorrorInterestManagement measures distance from conn.identity — so this
            // waits for the client's own runner as well as the host's.
            yield return WaitFor(() =>
                NetworkClient.spawned.ContainsKey(hostIdentity.netId) && NetworkClient.localPlayer != null);

            Assert.That(NetworkClient.spawned.ContainsKey(hostIdentity.netId), Is.True,
                "The client never built a copy of the host's runner within " + ConnectSecondsBudget + " s. Either "
                + "the spawn message never arrived, or it arrived and Mirror had no way to build the object — the "
                + "console line for the second is 'Failed to spawn server object, did you forget to add it to the "
                + "NetworkManager? assetId=" + NetRunner.AssetId + "'.");

            var clientCopy = NetworkClient.spawned[hostIdentity.netId];

            Assert.That(ReferenceEquals(clientCopy.gameObject, hostRunner), Is.False,
                "The client's copy is the very same GameObject as the host's. That is host mode, or an in-process "
                + "shortcut, and it proves nothing about a network.");

            Assert.That(clientCopy.isOwned, Is.False,
                "The client thinks it owns the host's runner. An owned copy would report its own position back and "
                + "this test would be measuring the client talking to itself.");

            Assert.That(NetworkClient.activeHost, Is.False,
                "NetworkClient.activeHost means the connection is Mirror's in-process host wire, which never "
                + "touches the transport. Everything below is worthless if it is true.");

            Assert.That(NetworkServer.localConnection, Is.Null,
                "A local connection on the server is the same in-process shortcut seen from the other end.");

            // --- and now it moves ---

            var hostPlayer = hostRunner.GetComponent<NetPlayer>();
            Assert.That(hostPlayer, Is.Not.Null, "NetRunner.Build must put a NetPlayer on the runner.");

            var travelled = Vector3.Distance(HostRunnerStart, HostRunnerArrival);
            Assert.That(travelled, Is.GreaterThan(ArrivalToleranceMetres * 10f),
                "The fixture moves the runner " + travelled.ToString("0.00") + " m and accepts arrival within "
                + ArrivalToleranceMetres.ToString("0.000") + " m. If those are ever close, a runner that did not "
                + "move at all would pass — fix the fixture, not the assertion.");

            var serverBytesBefore = _serverPayloadBytesSent;

            // §03's 왕복 and §01's 투하구 both move a player without them having run
            // there, which is what TeleportTo is for. It is a [Server] method: the
            // host is the only machine allowed to say this.
            hostPlayer!.TeleportTo(HostRunnerArrival);

            yield return WaitFor(() =>
                Vector3.Distance(clientCopy.transform.position, HostRunnerArrival) <= ArrivalToleranceMetres);

            Assert.That(
                Vector3.Distance(clientCopy.transform.position, HostRunnerArrival),
                Is.LessThanOrEqualTo(ArrivalToleranceMetres),
                "The host moved its runner to " + HostRunnerArrival + " and the client's copy is still at "
                + clientCopy.transform.position + ". The object exists on both machines and its position does not "
                + "cross, which is the shape of a SyncVar that is never dirtied or an observer set that never "
                + "includes this connection.");

            Assert.That(_serverPayloadBytesSent, Is.GreaterThan(serverBytesBefore),
                "KcpTransport.ServerSend was never called between the teleport and the client's copy arriving, so "
                + "the transport is not being used at all on this connection. On its own this number would prove "
                + "little — Mirror also broadcasts a time snapshot every tick — but zero growth here means even "
                + "that is not happening, and the position above is what says the teleport is among the bytes.");

            // Then keep running with nobody touching anything: an interest rebuild
            // runs every 1/30 s and could still take the object away again.
            yield return WaitSeconds(SettleSeconds);

            Assert.That(NetworkClient.spawned.ContainsKey(hostIdentity.netId), Is.True,
                "The client's copy of the host's runner was destroyed a moment after it arrived. That is what an "
                + "interest rebuild that drops the connection looks like from here.");
        }

        // ------------------------------------------------------------------
        // the client's runner, on the host's machine
        // ------------------------------------------------------------------

        /// <summary>
        /// The client owns a runner, drives it, and the host's authoritative copy
        /// moves — which is §05's five rows making the trip they were written for.
        /// <para>
        /// The position asserted on is <c>NetPlayer.NetworkedPosition</c>, the SyncVar
        /// the host holds, and there is exactly one line in the project that writes it
        /// on a non-owner: the body of <c>CmdReportView</c>. A client-side prediction
        /// cannot produce it, and neither can the test — <c>CmdReportView</c> is
        /// private, so the only way to reach it is to bind a view source to the
        /// client's copy and let <c>NetPlayer</c> send it at
        /// <c>GameConstants.NetworkSendRate</c>.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheOwningClientsRunnerMovesOnTheHostBecauseACommandCrossedTheWire()
        {
            yield return StartServerAndClient(OwnerPort);

            // NetworkServer.connections.Count is checked first so the short circuit
            // keeps FirstConnection from throwing on a frame where the connection has
            // gone away — a wait loop that throws reports the wrong failure.
            yield return WaitFor(() =>
                NetworkClient.localPlayer != null
                && NetworkServer.connections.Count == 1
                && FirstConnection().identity != null);

            var owned = NetworkClient.localPlayer;
            Assert.That(owned, Is.Not.Null,
                "The client was never given a runner. HorrorGameNetworkManager.OnServerReady adds one with "
                + "NetworkServer.AddPlayerForConnection; if this is null, either that did not run or the spawn "
                + "message did not reach the client.");

            var serverIdentity = FirstConnection().identity;
            Assert.That(serverIdentity, Is.Not.Null,
                "The server has a connection but no player object on it, so nothing was added for this client.");

            Assert.That(owned!.netId, Is.EqualTo(serverIdentity!.netId),
                "The client's own runner and the server's copy of it have different netIds, so they are two "
                + "unrelated objects rather than two views of one.");

            Assert.That(ReferenceEquals(owned.gameObject, serverIdentity.gameObject), Is.False,
                "One GameObject cannot prove anything about two machines.");

            var ownerCopy = owned.GetComponent<NetPlayer>();
            var serverCopy = serverIdentity.GetComponent<NetPlayer>();

            Assert.That(ownerCopy, Is.Not.Null, "The client's runner has no NetPlayer.");
            Assert.That(serverCopy, Is.Not.Null, "The server's runner has no NetPlayer.");

            Assert.That(ownerCopy!.isOwned, Is.True,
                "The client does not own the runner that was spawned for it, so it will never report a position "
                + "for it — NetPlayer.BindLocalSource refuses on a copy this machine does not own.");

            Assert.That(serverCopy!.isOwned, Is.False,
                "The server's copy claims ownership. That happens in host mode, where nothing crosses a wire.");

            var startedAt = serverCopy.NetworkedPosition;
            var distance = Vector3.Distance(startedAt, ReportedRunnerPosition);
            Assert.That(distance, Is.GreaterThan(ArrivalToleranceMetres * 10f),
                "The host's copy starts at " + startedAt + " and the client will claim " + ReportedRunnerPosition
                + " — " + distance.ToString("0.00") + " m apart. If those were close, a runner that never reported "
                + "would pass.");

            var clientBytesBefore = _clientPayloadBytesSent;

            // §05's five rows, as the owner's controller would produce them. Nothing
            // here reaches into the network layer: INetPlayerViewSource is primitives
            // only (ARCHITECTURE §3), which is exactly why a test can stand in for the
            // first-person controller without simulating one.
            var view = new ScriptedView
            {
                Position = ReportedRunnerPosition,
                YawDegrees = ReportedYawDegrees,
                PitchDegrees = ReportedPitchDegrees,
                FlashlightOn = true,
                Carry = NetCarryState.Objective,
                StaminaFraction = ReportedStaminaFraction,
            };

            ownerCopy.BindLocalSource(view);

            yield return WaitFor(() =>
                Vector3.Distance(serverCopy.NetworkedPosition, ReportedRunnerPosition) <= ArrivalToleranceMetres);

            Assert.That(
                Vector3.Distance(serverCopy.NetworkedPosition, ReportedRunnerPosition),
                Is.LessThanOrEqualTo(ArrivalToleranceMetres),
                "The client reported " + ReportedRunnerPosition + " and the host's copy is at "
                + serverCopy.NetworkedPosition + ". CmdReportView is the only thing that writes that field on a "
                + "runner the host does not own, so this failing means the Command never arrived — check that the "
                + "connection is ready and authenticated and that the client owns the object.");

            Assert.That(_clientPayloadBytesSent, Is.GreaterThan(clientBytesBefore),
                "KcpTransport.ClientSend never ran after the view source was bound, so nothing was handed to a "
                + "socket. On its own a growing byte count would only prove KCP is pinging; taken with the "
                + "position above, it says the Command is what grew it.");

            Assert.That(serverCopy.FlashlightOn, Is.True,
                "§05 sends 손전등 on/off because §06 turns it into what the monster notices. It did not arrive.");

            Assert.That(serverCopy.Carry, Is.EqualTo(NetCarryState.Objective),
                "§05's 운반 상태 did not arrive. It is the most information-dense byte in the game — §03 says the "
                + "carrier cannot hold a light, so somebody else has to.");

            Assert.That(serverCopy.YawDegrees, Is.EqualTo(ReportedYawDegrees).Within(AngleToleranceDegrees),
                "Yaw did not survive the wire. §05: 남의 손전등 방향이 정보다.");

            Assert.That(serverCopy.PitchDegrees, Is.EqualTo(ReportedPitchDegrees).Within(AngleToleranceDegrees),
                "Pitch did not survive the wire. §05 asks for it separately: 바닥·천장을 비추는 것도 신호.");

            Assert.That(
                serverCopy.ApproximateStaminaFraction,
                Is.EqualTo(ReportedStaminaFraction).Within(1f / NetPlayer.StaminaApproximationSteps),
                "§05's 스태미나 row: 본인만 정확히, 남은 대략. 'Approximately' is one step of "
                + NetPlayer.StaminaApproximationSteps + "; this is further off than that, which means it did not "
                + "arrive rather than that it was rounded.");
        }

        // ------------------------------------------------------------------
        // §11's field — twenty, on real sockets
        // ------------------------------------------------------------------

        /// <summary>
        /// Twenty peers connect and are kept; the twenty-first is turned away.
        /// <para>
        /// <b>Twenty real sockets, not twenty fabricated connections.</b> Mirror allows
        /// exactly one <c>NetworkClient</c> per process, so the extra peers are bare
        /// <c>kcp2k.KcpClient</c>s — the same class <c>KcpTransport</c> uses for its
        /// own client, each with its own UDP socket, each completing KCP's handshake
        /// against the server's socket. That is what makes this an answer to "does the
        /// shipped build accept twenty" rather than to "does a dictionary hold twenty
        /// entries".
        /// </para>
        /// <para>
        /// The cap is enforced in <c>NetworkServer.IsConnectionAllowed</c>, before a
        /// <c>NetworkConnectionToClient</c> exists, from the number
        /// <c>NetworkManager.StartServer</c> passed to <c>NetworkServer.Listen</c> —
        /// which is <c>maxConnections</c>, which <c>HorrorGameNetworkManager.Awake</c>
        /// sets. <c>RaceLobby.EnsureManager</c> also writes it, and does run when a
        /// human presses 호스트, but only then; this fixture starts the server the way
        /// every other caller does and holds the component to twenty on its own.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheTwentiethRunnerIsAcceptedAndTheTwentyFirstIsRefused()
        {
            BuildRig(CapPort);
            _manager = _rig!.AddComponent<HorrorGameNetworkManager>();
            _manager.transport = _transport;
            Wake();
            yield return null;

            _manager.networkAddress = Loopback;

            Assert.That(_manager.maxConnections, Is.EqualTo(GameConstants.RaceRunnersMax),
                "HorrorGameNetworkManager.Awake is the only place this is set on the shipped path, and §11's field "
                + "is " + GameConstants.RaceRunnersMax + ".");

            _manager.StartServer();

            Assert.That(NetworkServer.active, Is.True, "StartServer did not bring the server up.");

            Assert.That(NetworkServer.maxConnections, Is.EqualTo(GameConstants.RaceRunnersMax),
                "This is the field the refusal actually reads. NetworkManager.StartServer copies maxConnections "
                + "into it through NetworkServer.Listen; if the two ever disagree, the inspector value is a lie.");

            for (var i = 0; i < GameConstants.RaceRunnersMax; i++)
            {
                ConnectBarePeer(CapPort);
            }

            yield return WaitForPeers(() => NetworkServer.connections.Count >= GameConstants.RaceRunnersMax);

            Assert.That(NetworkServer.connections.Count, Is.EqualTo(GameConstants.RaceRunnersMax),
                "Only " + NetworkServer.connections.Count + " of " + GameConstants.RaceRunnersMax
                + " peers were accepted. A shortfall here is either the cap (console: 'Server full') or a socket "
                + "that could not be opened; the console says which.");

            var accepted = NetworkServer.connections.Count;

            // Mirror reports a full server with Debug.LogError and retransmits can
            // produce it more than once, so the count of those lines is not something
            // a test should pin. The refusal is asserted on state instead: the server
            // still holds exactly twenty, and the twenty-first peer is not connected.
            LogAssert.ignoreFailingMessages = true;

            var extra = ConnectBarePeer(CapPort);

            yield return WaitForPeersSeconds(SettleSeconds);

            Assert.That(NetworkServer.connections.Count, Is.EqualTo(accepted),
                "The twenty-first peer was admitted. §11 caps the field at " + GameConstants.RaceRunnersMax
                + " because §12-A fixes the gates at 4 → 2 → 1 and refuses to widen them with the field — a "
                + "twenty-first runner would be queueing, not racing.");

            Assert.That(extra.connected, Is.False,
                "The twenty-first peer still believes it is connected. Mirror refused it in IsConnectionAllowed "
                + "and asked the transport to disconnect it; a peer that never hears that is a client sitting in a "
                + "lobby it is not in.");
        }

        // ------------------------------------------------------------------
        // rig
        // ------------------------------------------------------------------

        /// <summary>
        /// Brings up the shipped manager as a server and then, in the same process, as
        /// a real remote client.
        /// <para>
        /// <c>StartServer</c> + <c>StartClient</c> rather than <c>StartHost</c>:
        /// <c>StartHost</c> gives the manager a <c>LocalConnectionToServer</c> and
        /// never opens a socket, which would make every assertion in this file a
        /// statement about in-process memory. Mirror guards the two on
        /// <c>NetworkServer.active</c> and <c>NetworkClient.active</c> separately, so
        /// this arrangement is legal and is the only one in a single process that puts
        /// a datagram between the ends.
        /// </para>
        /// </summary>
        private IEnumerator StartServerAndClient(ushort port)
        {
            BuildRig(port);

            _manager = _rig!.AddComponent<HorrorGameNetworkManager>();
            _manager.transport = _transport;

            Wake();
            yield return null;

            // After Awake: on the branch where the manager builds its own transport it
            // writes networkAddress from the Steam layer, and this fixture would
            // rather be immune to which branch ran than depend on having predicted it.
            _manager.networkAddress = Loopback;

            // The transport's own send hooks. KcpTransport raises these from
            // ClientSend/ServerSend only — KCP's handshake traffic never appears here,
            // so a non-zero count is Mirror-level payload and nothing else.
            _transport!.OnClientDataSent += (segment, channelId) => _clientPayloadBytesSent += segment.Count;
            _transport!.OnServerDataSent += (connectionId, segment, channelId) =>
                _serverPayloadBytesSent += segment.Count;

            _manager.StartServer();

            Assert.That(NetworkServer.active, Is.True,
                "StartServer did not bring the server up. EnsureTransport chose " + _manager.ActiveTransportKind + ".");

            _manager.StartClient();

            yield return WaitFor(() => NetworkClient.isConnected && NetworkServer.connections.Count == 1);

            Assert.That(NetworkClient.isConnected, Is.True,
                "The client never reached Connected on " + Loopback + ":" + port + " within "
                + ConnectSecondsBudget + " s. Either KcpTransport could not bind — another process on that port, "
                + "or a host firewall refusing a UDP listener — or the handshake datagrams never arrived.");

            Assert.That(NetworkServer.connections.Count, Is.EqualTo(1),
                "The client thinks it is connected but the server has no connection for it.");

            Assert.That(FirstConnection().address, Does.Contain(Loopback),
                "The server's idea of where this client is comes from KcpTransport.ServerGetClientAddress, which "
                + "reads the IPEndPoint a datagram actually arrived from. A fabricated connection has no endpoint "
                + "to report. Got: '" + FirstConnection().address + "'.");
        }

        /// <summary>
        /// Builds the wire, and leaves it asleep.
        /// <para>
        /// The GameObject is created inactive because <c>KcpTransport.Awake</c> freezes
        /// its <c>KcpConfig</c> from the serialised fields — a port assigned after
        /// <c>AddComponent</c> on a live object is remembered by the inspector and
        /// ignored by the socket. Everything else that must exist before an
        /// <c>Awake</c> (the manager, and its transport field) goes on in the same
        /// window.
        /// </para>
        /// </summary>
        private void BuildRig(ushort port)
        {
            _rig = new GameObject("NetRunnerRig");
            _rig.SetActive(false);

            _transport = _rig.AddComponent<KcpTransport>();
            _transport.port = port;
            _transport.DualMode = DualMode;
        }

        private void Wake()
        {
            _rig!.SetActive(true);
            Transport.active = _transport;
        }

        /// <summary>
        /// Opens one more UDP socket and speaks KCP to the server on it.
        /// <para>
        /// A bare <c>KcpClient</c> and not a second <c>NetworkClient</c>, because
        /// Mirror's client is a static singleton — one per process. The peer never
        /// sends a Mirror message and never becomes ready; the question it answers is
        /// only whether the server accepts the connection, which is decided in
        /// <c>NetworkServer.IsConnectionAllowed</c> before any message is read.
        /// </para>
        /// </summary>
        private KcpClient ConnectBarePeer(ushort port)
        {
            var config = new KcpConfig(
                DualMode: DualMode,
                RecvBufferSize: PeerSocketBufferBytes,
                SendBufferSize: PeerSocketBufferBytes);

            var peer = new KcpClient(
                OnConnected: () => { },
                OnData: (ArraySegment<byte> data, KcpChannel channel) => { },
                OnDisconnected: () => { },
                OnError: (ErrorCode code, string reason) => { },
                config: config);

            _peers.Add(peer);
            peer.Connect(Loopback, port);
            return peer;
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
        /// Runs frames until the condition holds or the budget expires. Never asserts
        /// — the caller does, so the failure message can say what was being waited for.
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
        /// As <see cref="WaitFor"/>, but also ticks the bare peers. They are not in
        /// Mirror's player loop, so nothing else will: an unticked KCP peer never
        /// finishes its handshake and the test would time out on a socket that was
        /// working.
        /// </summary>
        private IEnumerator WaitForPeers(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + ConnectSecondsBudget;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                TickPeers();
                yield return null;
            }

            TickPeers();
        }

        /// <summary>Runs frames for a fixed stretch of wall clock, ticking the bare peers.</summary>
        private IEnumerator WaitForPeersSeconds(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                TickPeers();
                yield return null;
            }
        }

        private void TickPeers()
        {
            for (var i = 0; i < _peers.Count; i++)
            {
                _peers[i].Tick();
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

        /// <summary>
        /// The first-person controller's half of §05, scripted.
        /// <para>
        /// <c>INetPlayerViewSource</c> is deliberately primitives only, so standing in
        /// for the real controller costs six properties and imports nothing from the
        /// player layer. That is ARCHITECTURE §3's seam doing the job it was cut for.
        /// </para>
        /// </summary>
        private sealed class ScriptedView : INetPlayerViewSource
        {
            public Vector3 Position { get; set; }

            public float YawDegrees { get; set; }

            public float PitchDegrees { get; set; }

            public bool FlashlightOn { get; set; }

            public NetCarryState Carry { get; set; }

            public float StaminaFraction { get; set; }
        }
    }
}
