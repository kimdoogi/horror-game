#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Race;
using HorrorGame.Gameplay.Race;
using HorrorGame.Net;
using HorrorGame.Net.PlayerBridge;
using kcp2k;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Net
{
    /// <summary>
    /// §02 crossing the wire, and refusing to cross it the wrong way. §13.
    /// <para>
    /// <b>The defect this fixture exists to keep closed.</b> Descents, finishes and catches
    /// used to be recorded by each machine's own <c>MatchDirector</c> into its own
    /// <c>RaceState</c>: twenty machines, twenty private scoreboards, every one of them
    /// seating the local player at seat 0, and §13's 「순위 · 도착 판정 — 호스트만」 applied to
    /// nothing at all. Nothing failed, because nothing asked.
    /// </para>
    /// <para>
    /// <b>The bar is <see cref="NetSocketTests"/>'s and it is not negotiable here.</b> Two
    /// peers, two UDP sockets, <c>NetworkClient.activeHost</c> asserted false,
    /// <c>NetworkServer.localConnection</c> asserted null, the owner's runner and the host's
    /// runner asserted to be <em>different</em> <c>GameObject</c>s sharing one
    /// <c>netId</c>, and the bytes counted at <c>KcpTransport</c>'s own
    /// <c>OnClientDataSent</c> / <c>OnServerDataSent</c> hooks — which the transport raises
    /// from <c>ClientSend</c>/<c>ServerSend</c> only, so KCP's handshake never shows up
    /// there and a non-zero count is Mirror-level payload and nothing else. A state that
    /// in-process memory could satisfy proves nothing about a race between two machines, and
    /// this project has already shipped one multiplayer game that had never had two peers
    /// connected.
    /// </para>
    /// <para>
    /// <b>Nothing here fakes the rule either.</b> The §02 side is the shipped
    /// <see cref="RaceDirector"/> with its real <c>RaceState</c>, installed into
    /// <see cref="NetRace"/> by its own <c>Begin</c>; the report is the shipped
    /// <c>NetPlayer.CmdReportDescent</c> called through <see cref="NetRace.SendDescent"/>;
    /// the standings come back through the shipped <c>RaceStandingsMessage</c> handler that
    /// <c>HorrorGameNetworkManager.OnStartClient</c> registers. There is no test double in
    /// this file, because a test double is how the last one of these passed while the game
    /// did not work.
    /// </para>
    /// </summary>
    public sealed class NetRaceStandingsTests
    {
        /// <summary>Loopback as an IPv4 literal — a name resolves to ::1 on an IPv6 host and the failure is a timeout, not an error.</summary>
        private const string Loopback = "127.0.0.1";

        /// <summary>IPv4 only on both sockets. <c>Socket.DualMode</c> is documented to throw on platforms without IPv6, and none of this is about IPv6.</summary>
        private const bool DualMode = false;

        /// <summary>Port for the descent test. Same 477xx block as the neighbouring fixtures, past their 47701–47708.</summary>
        private const ushort DescentPort = 47709;

        /// <summary>Port for the storey-skip test.</summary>
        private const ushort SkipPort = 47710;

        /// <summary>Port for the arrival test.</summary>
        private const ushort FinishPort = 47711;

        /// <summary>Port for the catch test. A distinct number again, so a fixture that leaked a bound socket fails where it leaked it.</summary>
        private const ushort CaughtPort = 47712;

        /// <summary>Wall-clock seconds for a loopback handshake plus a round trip. Seconds and not frames: under <c>-batchmode</c> the player loop is not waiting on a display.</summary>
        private const float ConnectSecondsBudget = 10f;

        /// <summary>
        /// §11's smallest legal field. Two seats, and in this arrangement only one of them
        /// has a body: <c>StartServer</c> + <c>StartClient</c> in one process gives the host
        /// no connection of its own, which is §13's headless-host case and the one CI runs.
        /// </summary>
        private const int Seats = GameConstants.RaceRunnersMin;

        /// <summary>The seat the connected client is dealt. Seat 0 is the bodiless host — see <see cref="Seats"/>.</summary>
        private const int ClientSeat = 1;

        /// <summary>
        /// Where §02's finish is put, in world space.
        /// <para>
        /// Bound rather than located, because there is no generated building in this scene
        /// and <c>RaceDirector.LocateFinish</c> would find nothing —
        /// <c>RaceDirector.BindFinish</c> exists for exactly this. Far from the origin so
        /// that a runner sitting at <c>Vector3.zero</c>, which is where
        /// <c>NetRunner.Build</c> leaves one, is nowhere near it: 141 m away against a
        /// 2.5 m circle.
        /// </para>
        /// </summary>
        private static readonly Vector3 Finish = new Vector3(100f, 0f, 100f);

        private GameObject? _rig;
        private GameObject? _raceRig;
        private KcpTransport? _transport;
        private HorrorGameNetworkManager? _manager;
        private RaceDirector? _race;
        private int _restoreTargetFrameRate;

        private int _clientPayloadBytesSent;
        private int _serverPayloadBytesSent;

        [SetUp]
        public void SetUp()
        {
            NetSession.ResetForTests();
            NetRace.ResetForTests();
            RaceParty.Clear();

            // Mirror pins Application.targetFrameRate to its send rate under -batchmode.
            // Process-wide, so it would otherwise slow every PlayMode test after this one.
            _restoreTargetFrameRate = Application.targetFrameRate;

            NetworkServer.aoi = null;
            NetworkClient.aoi = null;

            _clientPayloadBytesSent = 0;
            _serverPayloadBytesSent = 0;

            NetRunner.UnregisterClientSpawnHandler();
            NetRaceStartPoints.Uninstall();

            // Repairing a sibling fixture's teardown, not standing in for the shipped
            // installation: NetRunnerTests deliberately empties NetRunner.VisualFactory and
            // static state outlives a fixture. Without the bridge, NetLocalRunner logs a
            // warning on the owned runner and a warning is a failure here.
            NetRunnerBody.Forget();
            NetPlayerBridge.Install();
        }

        [TearDown]
        public void TearDown()
        {
            // Order matters and is not politeness. NetworkServer.Shutdown calls
            // Transport.active.ServerStop(), so the transport component has to still be
            // alive when it runs — destroying the rig first would leave the UDP socket bound
            // for the rest of the editor session and every later test would fail to bind.
            if (NetworkServer.active)
            {
                NetworkServer.Shutdown();
            }

            NetworkClient.Shutdown();

            NetworkServer.aoi = null;
            NetworkClient.aoi = null;
            NetworkManager.ResetStatics();

            DestroyIfPresent(ref _raceRig);
            DestroyIfPresent(ref _rig);

            _transport = null;
            _manager = null;
            _race = null;

            NetRaceStartPoints.Uninstall();
            NetRunnerBody.Forget();

            Transport.active = null;
            Application.targetFrameRate = _restoreTargetFrameRate;

            NetRace.ResetForTests();
            RaceParty.Clear();
            NetSession.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // 1 — a descent crosses the wire, both ways
        // ------------------------------------------------------------------

        /// <summary>
        /// A client falls through a 투하구, the host's own <c>RaceState</c> records it, and
        /// the standings the host broadcasts arrive back at the client carrying the new row.
        /// <para>
        /// <b>This is the assertion the project did not have.</b> It fails if
        /// <c>NetPlayer.CmdReportDescent</c> stops being sent, if
        /// <c>HorrorGameNetworkManager</c> stops registering the standings handler, if
        /// <c>RaceDirector.Begin</c> stops installing itself as <c>NetRace.Authority</c>, if
        /// the seat stops being assigned so the report lands on nobody, or if the broadcast
        /// stops going out — every one of which returns the game to twenty private
        /// scoreboards, and none of which any other test in the repository would notice.
        /// </para>
        /// <para>
        /// <b>Why it cannot be satisfied in memory.</b> The row asserted on is read from
        /// <c>NetRace.Standings</c>, whose only writer in the whole project is the private
        /// <c>NetRace.OnClientStandings</c> — a <c>NetworkClient</c> message handler. The
        /// client is asserted not to be <c>activeHost</c>, so its connection is a real
        /// <c>KcpTransport</c> one and the only path into that handler is
        /// <c>OnTransportData</c>. Both directions are additionally asserted to have moved
        /// bytes at the transport's own send hooks after the exchange began.
        /// </para>
        /// <para>
        /// <b>And the seat is the host's, not the client's.</b> The <c>[Command]</c> carries
        /// no seat at all; the host reads it off the <see cref="NetPlayer"/> the connection
        /// owns. Seat 0 is asserted untouched, which is the assertion that would fail if
        /// anybody ever put a seat field in that payload.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ADescentReportedByARemoteClientLandsInTheHostsRaceStateAndComesBackAsStandings()
        {
            yield return StartSessionAndRace(DescentPort);

            var ownerCopy = OwnerRunner();
            var serverCopy = ServerRunner();

            Assert.That(ReferenceEquals(ownerCopy.gameObject, serverCopy.gameObject), Is.False,
                "One GameObject cannot prove anything about two machines.");

            Assert.That(ownerCopy.netId, Is.EqualTo(serverCopy.netId),
                "The two copies are supposed to be the same runner seen from two ends.");

            // --- §11's seat, assigned by the host and replicated ---

            yield return WaitFor(() => ownerCopy.SeatIndex == ClientSeat);

            Assert.That(serverCopy.SeatIndex, Is.EqualTo(ClientSeat),
                "RaceDirector.BindNetworkedRunners did not give this connection its §11 seat. NetPlayer"
                + ".AssignSeat had exactly one caller before this change — HorrorGameNetworkManager"
                + ".OnServerAddPlayer, which Mirror never reaches on this project's spawn path — so every "
                + "runner carried seat −1 and a report from one could not be attributed to anybody.");

            Assert.That(ownerCopy.SeatIndex, Is.EqualTo(ClientSeat),
                "The host assigned the seat and the client never heard it. The seat is a SyncVar and this is "
                + "the one number a client needs in order to know which row of the standings is its own.");

            // Baselines taken after the handshake and the seat have settled, so growth below
            // is this exchange's traffic and not the connection's.
            var clientBytesBefore = _clientPayloadBytesSent;
            var serverBytesBefore = _serverPayloadBytesSent;
            var framesBefore = NetRace.Standings.FramesApplied;

            // --- the report goes up ---

            Assert.That(NetRace.SendDescent(1), Is.True,
                "NetRace.SendDescent found no runner on this machine to report through. It routes the "
                + "[Command] via NetworkClient.localPlayer, which is the only object Mirror will accept one "
                + "from on this connection.");

            yield return WaitFor(() =>
                _race!.RowOf(ClientSeat).Storey == 1 && NetRace.Standings.FramesApplied > framesBefore);

            // --- it landed in the one RaceState that matters ---

            Assert.That(_race!.Rules, Is.Not.Null,
                "The host has no RaceState. §13 makes this machine the only one that has one, so there is "
                + "nothing for a report to land in.");

            Assert.That(NetRace.DescentsAccepted, Is.EqualTo(1),
                "The host's rule never accepted a descent. NetRace.DescentsRefused is "
                + NetRace.DescentsRefused + " — a non-zero value there means RaceDirector.AcceptDescent "
                + "turned the claim down, most likely because the seat was wrong.");

            Assert.That(_race!.Rules![ClientSeat].Storey, Is.EqualTo(1),
                "The client reported a descent and the host's own RaceState still says B1 for that seat. "
                + "This is the defect verbatim: §02 recorded on the client and nowhere else.");

            Assert.That(_race!.Rules![0].Storey, Is.EqualTo(0),
                "Seat 0 moved. The [Command] carries no seat — the host reads it off the NetPlayer the "
                + "connection owns — so a report that moved somebody else means a seat field has been added "
                + "to the payload, and with it the ability to send a rival back eight storeys.");

            // --- and the standings came back down ---

            Assert.That(NetRace.Standings.HasFrame, Is.True,
                "No standings frame ever reached this client. HorrorGameNetworkManager.OnStartClient "
                + "registers the handler; NetworkClient.Shutdown clears the table, so a handler installed "
                + "once at load would be gone by the second session and this is what that looks like.");

            Assert.That(NetRace.Standings.RunnerCount, Is.EqualTo(Seats),
                "The frame did not carry §11's field size. RaceStandingsMessage.Rows is seat-indexed, so its "
                + "length is how a client learns how many started.");

            Assert.That(NetRace.Standings.RowOf(ClientSeat).Storey, Is.EqualTo(1),
                "The host recorded the descent and the client was never told. That is half the defect: the "
                + "standings on this screen would still say B1 while the host ran a race in which they are on "
                + "B2.");

            Assert.That(NetRace.Standings.RowOf(ClientSeat).Id, Is.EqualTo(ClientSeat),
                "A row's seat is its index in the array; a row that disagrees means NetRaceStandings.Apply "
                + "has stopped indexing by seat.");

            Assert.That(NetRace.Standings.Standings.Count, Is.EqualTo(Seats),
                "The host's own live order did not arrive. RaceStandingsMessage.LiveOrder is what makes the "
                + "client a renderer rather than a second opinion about who is second.");

            Assert.That(NetRace.Standings.Standings[0].Id, Is.EqualTo(ClientSeat),
                "The host sorts the standings deepest first and this runner is a storey below seat 0, so it "
                + "has to be the first row. Getting seat 0 here means the order was re-derived locally rather "
                + "than read off the wire.");

            // --- and it moved over a socket ---

            Assert.That(_clientPayloadBytesSent, Is.GreaterThan(clientBytesBefore),
                "KcpTransport.ClientSend never ran after the report, so the [Command] was not handed to a "
                + "socket. Whatever moved, moved in process memory.");

            Assert.That(_serverPayloadBytesSent, Is.GreaterThan(serverBytesBefore),
                "KcpTransport.ServerSend never ran after the report, so the standings were not handed to a "
                + "socket. Same failure, other direction.");

            AssertNotHostMode();
        }

        // ------------------------------------------------------------------
        // 2 — the guard on the claim
        // ------------------------------------------------------------------

        /// <summary>
        /// A client claiming to have arrived on the bottom storey from the top one is
        /// refused, and its row does not move.
        /// <para>
        /// <b>Why monotonic is not enough.</b> <c>RaceState.ReportDescent</c> already drops
        /// anything at or above where a runner is, which stops a report going backwards and
        /// stops nothing else: "I am on B8" from a runner on B1 goes strictly forward and
        /// would be accepted. That single packet is the whole race, and §02 names arrival as
        /// the most attractive value in the build to lie about. §12 puts a 투하구 at the
        /// middle of a floor and lands you on the RIM of the one below, so a descent is by
        /// construction exactly one storey; <c>RaceDirector.AcceptDescent</c> enforces that
        /// and this is the test of it.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AClientCannotClaimSevenStoreysInOneReport()
        {
            yield return StartSessionAndRace(SkipPort);

            var ownerCopy = OwnerRunner();
            yield return WaitFor(() => ownerCopy.SeatIndex == ClientSeat);

            var refusedBefore = NetRace.DescentsRefused;

            // B8's index. From B1 that is the entire descent in one message.
            Assert.That(NetRace.SendDescent(RaceState.Storeys - 1), Is.True,
                "The report was never sent, so this test proves nothing about the guard.");

            yield return WaitFor(() => NetRace.DescentsRefused > refusedBefore);

            Assert.That(NetRace.DescentsRefused, Is.EqualTo(refusedBefore + 1),
                "The host never turned the claim down within " + ConnectSecondsBudget + " s. Either it was "
                + "accepted — check RaceDirector.AcceptDescent's one-storey rule — or the [Command] never "
                + "arrived, in which case the neighbouring descent test is failing too.");

            Assert.That(NetRace.DescentsAccepted, Is.EqualTo(0),
                "The host accepted a seven-storey drop. §12's chutes go one floor at a time and this is the "
                + "single most valuable lie available in this build.");

            Assert.That(_race!.Rules![ClientSeat].Storey, Is.EqualTo(0),
                "The refused claim moved the runner anyway.");

            Assert.That(NetRace.Standings.RowOf(ClientSeat).Storey, Is.EqualTo(0),
                "The client's own standings show the storey it asked for rather than the one the host "
                + "granted. A refusal that is invisible on the liar's screen is a refusal they will keep "
                + "trying.");

            AssertNotHostMode();
        }

        // ------------------------------------------------------------------
        // 3 — arrival is judged on the host's body
        // ------------------------------------------------------------------

        /// <summary>
        /// A client standing its own copy of its runner on the middle of B8 does not finish;
        /// the same runner finishes the moment the <em>host's</em> copy is there.
        /// <para>
        /// <b>This is §02's sentence as a measurement.</b> "도착 판정을 클라이언트가 내리면
        /// 경주 게임에서 가장 먼저 조작되는 값이 된다" — so the body <c>RaceDirector.CheckFinish</c>
        /// measures has to be the one the host owns. It is:
        /// <c>NetPlayer.CmdReportView</c> writes <c>transform.position</c> on the server from
        /// the value it has just clamped against §05's sprint speed, and
        /// <c>RaceDirector.BindNetworkedRunners</c> tracks that transform. Before this
        /// change <c>MatchDirector.AttachRace</c> tracked only the local first-person rig,
        /// which is right for exactly one seat and wrong for nineteen.
        /// </para>
        /// <para>
        /// The record is advanced to the bottom storey the honest way — seven accepted
        /// reports, one storey each, over the socket — because <c>RaceState.ReportFinish</c>
        /// refuses an arrival from anybody whose storey is not the bottom one, and a test
        /// that reached B8 by writing the field would not be testing the same object the
        /// game uses.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AnArrivalIsJudgedOnTheHostsCopyOfTheRunnerAndNotTheClients()
        {
            yield return StartSessionAndRace(FinishPort);

            var ownerCopy = OwnerRunner();
            var serverCopy = ServerRunner();

            yield return WaitFor(() => ownerCopy.SeatIndex == ClientSeat);

            // Down the tower, one storey per accepted report.
            for (var storey = 1; storey < RaceState.Storeys; storey++)
            {
                var target = storey;
                Assert.That(NetRace.SendDescent(target), Is.True, "The descent report was not sent.");

                yield return WaitFor(() => _race!.Rules![ClientSeat].Storey == target);

                Assert.That(_race!.Rules![ClientSeat].Storey, Is.EqualTo(target),
                    "The host did not accept the drop onto B" + (target + 1) + " within "
                    + ConnectSecondsBudget + " s. Accepted so far: " + NetRace.DescentsAccepted
                    + ", refused: " + NetRace.DescentsRefused + ".");
            }

            Assert.That(_race!.Rules![ClientSeat].Storey, Is.EqualTo(RaceState.Storeys - 1),
                "The runner is not on the bottom storey, so nothing below tests an arrival.");

            // --- the client puts ITS copy on the finish. Nothing may happen. ---
            //
            // The owned copy stays where it is put: NetPlayer.Update only applies a remote
            // view when !isOwned, and only reports when a view source is bound, and this
            // fixture binds none.
            ownerCopy.transform.position = Finish;

            _race!.Tick(1f);
            yield return null;
            _race!.Tick(2f);

            Assert.That(_race!.Rules![ClientSeat].Status, Is.EqualTo(RacerStatus.Running),
                "§02 recorded an arrival from a position only the client had. That is the whole of what §13 "
                + "forbids: the host measures the body it owns, and the client's transform is not it.");

            Assert.That(_race!.Rules!.WinnerId, Is.EqualTo(-1),
                "A winner was declared off a client-side transform.");

            // --- now the HOST's copy is there. §02 has to fire. ---
            //
            // TeleportTo rather than an assignment, because it is the host-side placement
            // API: it writes the authoritative _position as well as the transform, so the
            // server's own ApplyRemoteView does not drag the body back to the origin on the
            // next frame.
            serverCopy.TeleportTo(Finish);
            yield return null;

            _race!.Tick(3f);

            Assert.That(_race!.Rules![ClientSeat].Status, Is.EqualTo(RacerStatus.Finished),
                "The host's own copy of the runner is standing on the middle of B8 with the record reading "
                + "the bottom storey, and §02 did not record an arrival. RaceDirector.BindNetworkedRunners is "
                + "what puts that body in front of the finish circle — check that it tracked seat "
                + ClientSeat + ".");

            Assert.That(_race!.Rules![ClientSeat].Place, Is.EqualTo(1),
                "§02 records a place for every finisher and this is the first one.");

            yield return WaitFor(() =>
                NetRace.Standings.RowOf(ClientSeat).Status == RacerStatus.Finished);

            Assert.That(NetRace.Standings.RowOf(ClientSeat).Place, Is.EqualTo(1),
                "The host recorded the arrival and never told the client its place. §02's 완주 순위 is the "
                + "reason eighteenth place is worth having, and it only exists on a screen if it crosses the "
                + "wire.");

            Assert.That(NetRace.Standings.WinnerId, Is.EqualTo(ClientSeat),
                "The winner did not arrive with the standings.");

            AssertNotHostMode();
        }

        // ------------------------------------------------------------------
        // 4 — the catch
        // ------------------------------------------------------------------

        /// <summary>
        /// A client reports §06's catch and the host's rule is what sends the runner back to
        /// B1 — storey 0, 잡힘 1회, still Running.
        /// <para>
        /// <b>The catch is a report and not a host-side verdict, on purpose.</b> §06 is not
        /// on the wire: <c>MatchDirector</c> holds no reference to Mirror, it stands eight
        /// <c>MonsterAgent</c>s on <em>every</em> machine, and <c>NetMonster</c> — the
        /// component written for §13's 「괴물 AI를 호스트가 돌린다」 — has zero consumers in
        /// the project. The full argument, and what would change it, is on
        /// <c>RaceDirector.AcceptCaught</c>. What this test pins down is the half that is
        /// not a trade-off: the <em>seat</em> is the host's, so a client can report its own
        /// catch and nobody else's, and the consequence — 탈락 is not death, the runner keeps
        /// racing from the rim of B1 — is applied by <c>RaceState</c> on the host and
        /// broadcast.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ACatchReportedByAClientIsAppliedByTheHostsRuleAndBroadcast()
        {
            yield return StartSessionAndRace(CaughtPort);

            var ownerCopy = OwnerRunner();
            yield return WaitFor(() => ownerCopy.SeatIndex == ClientSeat);

            Assert.That(NetRace.SendDescent(1), Is.True, "The descent report was not sent.");
            yield return WaitFor(() => _race!.Rules![ClientSeat].Storey == 1);

            Assert.That(_race!.Rules![ClientSeat].Storey, Is.EqualTo(1),
                "The runner never reached B2, so a catch here would have nothing to cost them.");

            Assert.That(NetRace.SendCaught(), Is.True,
                "NetRace.SendCaught found no runner on this machine to report through.");

            yield return WaitFor(() => _race!.Rules![ClientSeat].TimesCaught == 1);

            Assert.That(NetRace.CatchesAccepted, Is.EqualTo(1),
                "The host's rule never accepted the catch. Refused: " + NetRace.CatchesRefused + ".");

            var row = _race!.Rules![ClientSeat];

            Assert.That(row.TimesCaught, Is.EqualTo(1),
                "The host did not record the catch. Before this change nothing about §06 crossed the wire at "
                + "all, so a runner caught on a client stayed on B2 in everybody else's standings for the "
                + "rest of the match.");

            Assert.That(row.Storey, Is.EqualTo(0),
                "§02: 잡히면 출발선으로 돌아간다. B1's rim is the one place in the building a runner is "
                + "allowed to stand without having fallen into it.");

            Assert.That(row.Status, Is.EqualTo(RacerStatus.Running),
                "Being caught is not death. RaceState.ReportCaught leaves the runner Running; only a "
                + "disconnect reaches Eliminated.");

            yield return WaitFor(() => NetRace.Standings.RowOf(ClientSeat).TimesCaught == 1);

            Assert.That(NetRace.Standings.RowOf(ClientSeat).Storey, Is.EqualTo(0),
                "The client's standings still show B2 after the host put it back on B1.");

            Assert.That(NetRace.Standings.RowOf(ClientSeat).TimesCaught, Is.EqualTo(1),
                "잡힘 횟수 did not cross the wire. It is what tells a screen why somebody who was on B6 a "
                + "minute ago is on B1 now, instead of that reading as a bug.");

            AssertNotHostMode();
        }

        // ------------------------------------------------------------------
        // rig
        // ------------------------------------------------------------------

        /// <summary>
        /// Brings a real server and a real client up on <paramref name="port"/>, waits for
        /// the client's runner to exist on both machines, and stands §02 up on the host.
        /// <para>
        /// <b>StartServer then StartClient, not StartHost.</b> <c>StartHost</c> gives the
        /// manager an in-process <c>LocalConnectionToServer</c> and never opens a socket.
        /// Mirror's two guards are <c>NetworkServer.active</c> and
        /// <c>NetworkClient.active</c> respectively, so a server-only start followed by a
        /// client start is legal and is the only arrangement in one process that puts a real
        /// datagram between them.
        /// </para>
        /// </summary>
        private IEnumerator StartSessionAndRace(ushort port)
        {
            _rig = new GameObject("NetRaceStandingsRig");
            _rig.SetActive(false);

            // Inactive while these are assigned: KcpTransport.Awake freezes its KcpConfig
            // from the serialised fields, so a port written after AddComponent on a live
            // object is remembered by the inspector and ignored by the socket.
            _transport = _rig.AddComponent<KcpTransport>();
            _transport.port = port;
            _transport.DualMode = DualMode;

            _manager = _rig.AddComponent<HorrorGameNetworkManager>();
            _manager.transport = _transport;

            _rig.SetActive(true);
            Transport.active = _transport;
            yield return null;

            Assert.That(NetworkManager.singleton, Is.SameAs(_manager),
                "Another NetworkManager owns the singleton, so this fixture's settings are not the ones the "
                + "session will run on.");

            _manager.networkAddress = Loopback;

            _transport.OnClientDataSent += (segment, channelId) => _clientPayloadBytesSent += segment.Count;
            _transport.OnServerDataSent += (connectionId, segment, channelId) =>
                _serverPayloadBytesSent += segment.Count;

            _manager.StartServer();

            Assert.That(NetworkServer.active, Is.True,
                "StartServer did not bring the server up. EnsureTransport chose "
                + _manager.ActiveTransportKind + ".");

            _manager.StartClient();

            yield return WaitFor(() =>
                NetworkClient.isConnected
                && NetworkServer.connections.Count == 1
                && NetworkClient.localPlayer != null
                && FirstConnection().identity != null);

            Assert.That(NetworkClient.isConnected, Is.True,
                "The client never connected on " + Loopback + ":" + port + " within " + ConnectSecondsBudget
                + " s. Either KcpTransport could not bind — another process on that port, or a host firewall "
                + "refusing a UDP listener — or the handshake datagrams never arrived.");

            Assert.That(NetworkClient.localPlayer, Is.Not.Null,
                "The client was never given a runner, so it has no object to send a [Command] on. "
                + "HorrorGameNetworkManager.OnServerReady adds one with NetworkServer.AddPlayerForConnection.");

            Assert.That(FirstConnection().address, Does.Contain(Loopback),
                "The server's idea of where this client is comes from KcpTransport.ServerGetClientAddress, "
                + "which reads the IPEndPoint a datagram actually arrived from. A fabricated connection has "
                + "no endpoint to report. Got: '" + FirstConnection().address + "'.");

            // §11's seat map, exactly as RaceLobby.Settle leaves it on the host immediately
            // before the descent's scene loads: seat → connection id, in arrival order.
            // Seat 0 is this machine, which in a StartServer + StartClient arrangement has
            // no connection of its own — Mirror reserves connection id 0 for a host's local
            // connection and there is not one here, so seat 0 stays bodiless and only the
            // remote client gets a runner. That is §13's headless host, and it is the shape
            // CI runs in.
            RaceParty.Settle(0, new List<int> { 0, FirstConnection().connectionId });

            _raceRig = new GameObject("NetRaceStandingsRace");
            _race = _raceRig.AddComponent<RaceDirector>();
            _race.BindFinish(Finish);

            Assert.That(_race.Begin(Seats), Is.True, "§11 refused a field of " + Seats + ".");

            Assert.That(_race.Judging, Is.True,
                "The director did not take host authority. NetRace.ThisMachineJudges is "
                + NetRace.ThisMachineJudges + " with NetworkServer.active " + NetworkServer.active
                + " — a director that is not judging decides nothing and this fixture would be testing an "
                + "empty branch.");

            Assert.That(ReferenceEquals(NetRace.Authority, _race), Is.True,
                "RaceDirector.Begin did not install itself as §02's authority, so a client's report has "
                + "nowhere to land.");
        }

        /// <summary>The runner this machine's client owns — the copy a <c>[Command]</c> may be called on.</summary>
        private static NetPlayer OwnerRunner()
        {
            var local = NetworkClient.localPlayer;
            Assert.That(local, Is.Not.Null, "The client has no local player.");

            var player = local!.GetComponent<NetPlayer>();
            Assert.That(player, Is.Not.Null, "The client's runner has no NetPlayer.");
            return player!;
        }

        /// <summary>The host's authoritative copy of the same runner.</summary>
        private static NetPlayer ServerRunner()
        {
            var identity = FirstConnection().identity;
            Assert.That(identity, Is.Not.Null, "The server has a connection but no player object on it.");

            var player = identity!.GetComponent<NetPlayer>();
            Assert.That(player, Is.Not.Null, "The server's runner has no NetPlayer.");
            return player!;
        }

        /// <summary>
        /// The two facts that would make every other assertion in this file worthless.
        /// Repeated per test rather than hoisted, because a fixture that checked them once
        /// in a helper is a fixture where one test can quietly stop checking them.
        /// </summary>
        private static void AssertNotHostMode()
        {
            Assert.That(NetworkClient.activeHost, Is.False,
                "NetworkClient.activeHost means the connection is a LocalConnectionToServer — Mirror's "
                + "in-process host wire, which never touches the transport. Everything above is worthless if "
                + "it is true.");

            Assert.That(NetworkServer.localConnection, Is.Null,
                "A local connection on the server is the same in-process shortcut seen from the other end.");
        }

        private static NetworkConnectionToClient FirstConnection()
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                return conn;
            }

            throw new InvalidOperationException("No connection on the server.");
        }

        private static void DestroyIfPresent(ref GameObject? go)
        {
            if (go != null)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            go = null;
        }

        /// <summary>
        /// Runs frames until the condition holds or the budget expires. Never asserts — the
        /// caller does, so the failure message can say what was being waited for.
        /// </summary>
        private static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + ConnectSecondsBudget;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }
    }
}
