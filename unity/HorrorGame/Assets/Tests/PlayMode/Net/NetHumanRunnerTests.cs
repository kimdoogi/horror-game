#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Movement;
using HorrorGame.Gameplay.Player;
using HorrorGame.Net;
using HorrorGame.Net.PlayerBridge;
using kcp2k;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Net
{
    /// <summary>
    /// The three things that stood between "the tests pass" and "twenty people can race":
    /// a human on the wire, a body to see them in, and twenty different places to start.
    /// <para>
    /// <b>Why these are not in <see cref="NetRunnerTests"/>.</b> That fixture proves the
    /// wire carries a runner, and it does it by handing <c>NetPlayer</c> a
    /// <c>ScriptedView</c> of its own — which was the right way to test the wire and is
    /// exactly why nobody noticed that nothing else ever handed it one.
    /// <c>NetPlayer.BindLocalSource</c> had no caller outside those tests, so the game
    /// shipped with a working network and nobody on it. <b>Nothing in this file binds a
    /// view source, builds a body or places a spawn point.</b> Every one of those has to
    /// be done by shipped code or the assertions below cannot pass, and that is the whole
    /// design of the fixture.
    /// </para>
    /// <para>
    /// <b>The same bar as its neighbours.</b> Two peers, two UDP sockets,
    /// <c>NetworkClient.activeHost</c> false, the server's connection not a
    /// <c>LocalConnectionToClient</c>, and every claim made about a <em>different
    /// GameObject</em> from the one that produced it. A state that in-process memory could
    /// satisfy proves nothing here, and this project has been burned by exactly that.
    /// </para>
    /// </summary>
    public sealed class NetHumanRunnerTests
    {
        /// <summary>Loopback as an IPv4 literal — a name resolves to ::1 on an IPv6 host and the failure is a timeout, not an error.</summary>
        private const string Loopback = "127.0.0.1";

        /// <summary>IPv4 only on both sockets. <c>Socket.DualMode</c> is documented to throw on platforms without IPv6, and none of this is about IPv6.</summary>
        private const bool DualMode = false;

        /// <summary>Port for the human-on-the-wire test. Same 477xx block as the neighbouring fixtures, past their 47701–47705.</summary>
        private const ushort HumanPort = 47706;

        /// <summary>Port for the body test.</summary>
        private const ushort BodyPort = 47707;

        /// <summary>Port for the starting-line test. A separate number again, so a fixture that leaked a bound socket fails where it leaked it.</summary>
        private const ushort RimPort = 47708;

        /// <summary>The descent, as <c>SoloPlaytest.BuildScene</c> wrote it and as the game loads it.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>Wall-clock seconds for a loopback handshake plus a spawn round trip. Seconds and not frames: under <c>-batchmode</c> the player loop is not waiting on a display.</summary>
        private const float ConnectSecondsBudget = 10f;

        /// <summary>Wall-clock seconds allowed for the rig to walk <see cref="MinimumTravelMetres"/>. At §05's 걷기 2.0 m/s that distance takes under a second, so this is five times over.</summary>
        private const float DriveSecondsBudget = 5f;

        /// <summary>Yaw the rig is pointed at. Not 0 and not a multiple of 90, so an axis dropped anywhere between the mouse and the wire shows up as a wrong number rather than as the same number.</summary>
        private const float RigYawDegrees = 137.5f;

        /// <summary>
        /// Pitch the rig is aimed at, in <c>PlayerLook</c>'s convention — <b>positive is
        /// looking down</b>. <see cref="INetPlayerViewSource"/> uses the opposite sign, so
        /// this is also the test for that conversion; see the assertion.
        /// </summary>
        private const float RigPitchDegreesLookingDown = 21.25f;

        /// <summary>
        /// How far a replicated position may sit from the value that produced it: one send
        /// interval of the fastest thing in the game
        /// (<c>RunnerSprintSpeed / NetworkSendRate</c> = 5.6 / 30 ≈ 0.19 m). Derived from
        /// the two constants that decide it, exactly as <see cref="NetRunnerTests"/>
        /// derives the same number — anything larger is a value that did not arrive rather
        /// than one that arrived late.
        /// </summary>
        private static readonly float ArrivalToleranceMetres =
            GameConstants.RunnerSprintSpeed / GameConstants.NetworkSendRate;

        /// <summary>
        /// How far the rig is walked before anything is asserted — twenty times
        /// <see cref="ArrivalToleranceMetres"/>, about 3.7 m, under two seconds at §05's
        /// 걷기.
        /// <para>
        /// Twice the ten-times separation the assertion below demands, on purpose: the
        /// figure the assertion compares is the distance between where the <em>host's
        /// copy</em> started and where the player finished, and that is the walked distance
        /// minus up to one send interval of lag. Driving to exactly the assertion's
        /// threshold would make the test fail on a slow frame rather than on a defect.
        /// </para>
        /// </summary>
        private static readonly float MinimumTravelMetres = ArrivalToleranceMetres * 20f;

        /// <summary>Angle tolerance. Yaw is quantised to a <c>ushort</c> over 360°, so one step is ~0.0055°; this is under two steps.</summary>
        private const float AngleToleranceDegrees = 0.01f;

        /// <summary>
        /// Chebyshev radius, in metres, of the wall between 중간 고리 and 외곽 고리 —
        /// <c>RadialStorey</c>'s band table, <c>d 8 = wall, 4 gates</c>, at 2.5 m a cell.
        /// <para>
        /// Quoted rather than referenced because <c>RadialStorey</c> lives in an editor
        /// assembly this one cannot reference — the same reason and the same number
        /// <c>DescentPlaythroughTests.RimWallMetres</c> writes down. Anything beyond it is
        /// on the rim, which is §01's whole structural claim about where a race starts.
        /// Chebyshev and not Euclidean because the generator builds square rings.
        /// </para>
        /// </summary>
        private const float RimWallMetres = 20f;

        /// <summary>
        /// Name prefix of the light the generator leaves burning at the 출입구 — which on
        /// this tower is §02's finish, the middle of B8. Mirrors
        /// <c>MatchMap.EntranceLightPrefix</c>, which lives in the predefined assembly.
        /// It is the only mark in the scene for the middle of the column, so it is how
        /// this fixture learns where "the middle" is instead of writing a coordinate down.
        /// </summary>
        private const string EntranceLightPrefix = "EntranceLight";

        private GameObject? _rig;
        private GameObject? _drivableRig;
        private GameObject? _floor;
        private KcpTransport? _transport;
        private HorrorGameNetworkManager? _manager;
        private int _restoreTargetFrameRate;
        private bool _soloSceneLoaded;

        private readonly List<GameObject> _serverSpawned = new List<GameObject>();

        private int _clientPayloadBytesSent;
        private int _serverPayloadBytesSent;

        [SetUp]
        public void SetUp()
        {
            NetSession.ResetForTests();

            // Mirror pins Application.targetFrameRate to its send rate under -batchmode.
            // Process-wide, so it would otherwise slow every PlayMode test after this one.
            _restoreTargetFrameRate = Application.targetFrameRate;

            NetworkServer.aoi = null;
            NetworkClient.aoi = null;

            _clientPayloadBytesSent = 0;
            _serverPayloadBytesSent = 0;
            _soloSceneLoaded = false;

            NetRunner.UnregisterClientSpawnHandler();
            NetRaceStartPoints.Uninstall();

            // Put the seams back. NetRunnerTests deliberately empties NetRunner.VisualFactory
            // to prove a bodiless runner still replicates, and static state outlives a
            // fixture — so this is repairing a sibling's teardown, not standing in for the
            // shipped installation. That the build installs it is asserted separately and
            // on a flag only Unity's own [RuntimeInitializeOnLoadMethod] can set; see
            // AssertTheBuildInstalledTheBridge.
            NetRunnerBody.Forget();
            NetPlayerBridge.Install();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Order matters and is not politeness. NetworkServer.Shutdown calls
            // Transport.active.ServerStop(), so the transport component has to still be
            // alive when it runs — tearing the scene down first would leave the UDP socket
            // bound for the rest of the editor session and every later test would fail to
            // bind.
            if (NetworkServer.active)
            {
                NetworkServer.Shutdown();
            }

            NetworkClient.Shutdown();

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

            DestroyIfPresent(ref _rig);
            DestroyIfPresent(ref _drivableRig);
            DestroyIfPresent(ref _floor);

            _transport = null;
            _manager = null;

            // The subscription is static and a fixture that shuts the server down without
            // going through NetworkManager.StopServer never reaches OnStopServer, so this
            // is the only place it comes off.
            NetRaceStartPoints.Uninstall();
            NetRunnerBody.Forget();

            Transport.active = null;
            Application.targetFrameRate = _restoreTargetFrameRate;
            NetSession.ResetForTests();

            if (_soloSceneLoaded)
            {
                var solo = SceneManager.GetSceneByName(SoloScene);
                if (solo.IsValid() && solo.isLoaded)
                {
                    var empty = SceneManager.CreateScene("NetHumanRunnerTests_Empty");
                    SceneManager.SetActiveScene(empty);
                    yield return SceneManager.UnloadSceneAsync(solo);
                }

                _soloSceneLoaded = false;
            }

            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // 1 — a human on the wire
        // ------------------------------------------------------------------

        /// <summary>
        /// A person walks their character forward, and the host's authoritative copy of
        /// their runner walks with them. Nothing in this test binds anything.
        /// <para>
        /// <b>What it would catch.</b> Delete
        /// <c>NetRunner.LocalViewSourceFactory</c>'s installation, or
        /// <see cref="NetLocalRunner"/>, or the <c>PlayerRigNetView</c> that reads the rig,
        /// and this fails while every other network test stays green — because every other
        /// network test supplies its own view source. That gap is the state the project
        /// shipped in.
        /// </para>
        /// <para>
        /// <b>Why it cannot be satisfied in memory.</b> The position asserted on is
        /// <c>NetPlayer.NetworkedPosition</c> on the <em>server's</em> copy, and the only
        /// line in the project that writes it on a runner the host does not own is the body
        /// of the private <c>CmdReportView</c>. The two copies are asserted to be different
        /// <c>GameObject</c>s sharing one <c>netId</c>, <c>NetworkClient.activeHost</c> is
        /// asserted false, and the movement itself comes out of <c>PlayerMotor.Step</c> —
        /// §05's own speed table against a real <c>CharacterController</c> — rather than
        /// out of an assignment.
        /// </para>
        /// <para>
        /// <b>The pitch assertion is not decoration.</b> <c>PlayerLook</c> counts pitch
        /// positive downward and <see cref="INetPlayerViewSource"/> counts it negative
        /// downward; <c>NetPlayer.ApplyRemoteView</c> agrees with the interface. Passed
        /// through unconverted, every runner sweeping the floor would be seen sweeping the
        /// ceiling — an inverted message rather than a cosmetic error, in the one row §05
        /// added specifically because "바닥·천장을 비추는 것도 신호".
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AHumanWalkingTheShippedRigMovesTheHostsCopyOfTheirRunner()
        {
            AssertTheBuildInstalledTheBridge();

            var motor = BuildDrivableRig();
            var look = motor.GetComponent<PlayerLook>();

            // Aimed before the session comes up, so the very first report already carries
            // an angle that is not the default.
            look.SetLook(RigYawDegrees, RigPitchDegreesLookingDown);

            yield return StartServerAndClient(HumanPort);

            yield return WaitFor(() =>
                NetworkClient.localPlayer != null
                && NetworkServer.connections.Count == 1
                && FirstConnection().identity != null);

            var owned = NetworkClient.localPlayer;
            Assert.That(owned, Is.Not.Null,
                "The client was never given a runner. HorrorGameNetworkManager.OnServerReady adds one with "
                + "NetworkServer.AddPlayerForConnection.");

            var serverIdentity = FirstConnection().identity;
            Assert.That(serverIdentity, Is.Not.Null, "The server has a connection but no player object on it.");

            Assert.That(ReferenceEquals(owned!.gameObject, serverIdentity!.gameObject), Is.False,
                "One GameObject cannot prove anything about two machines.");

            Assert.That(NetworkClient.activeHost, Is.False,
                "NetworkClient.activeHost means Mirror's in-process host wire, which never touches the "
                + "transport. Everything below is worthless if it is true.");

            Assert.That(NetworkServer.localConnection, Is.Null,
                "A local connection on the server is the same in-process shortcut seen from the other end.");

            var ownerCopy = owned.GetComponent<NetPlayer>();
            var serverCopy = serverIdentity.GetComponent<NetPlayer>();
            Assert.That(ownerCopy, Is.Not.Null, "The client's runner has no NetPlayer.");
            Assert.That(serverCopy, Is.Not.Null, "The server's runner has no NetPlayer.");

            // --- the binding itself, before a single metre is walked ---

            var binder = owned.GetComponent<NetLocalRunner>();
            Assert.That(binder, Is.Not.Null,
                "NetRunner.Build did not put a NetLocalRunner on the runner, so nothing on this machine is "
                + "looking for the player's rig at all.");

            yield return WaitFor(() => binder!.BoundSource != null);

            Assert.That(binder!.BoundSource, Is.Not.Null,
                "Nothing bound a view source to the runner this machine owns within " + ConnectSecondsBudget
                + " s, with a PlayerMotor sitting in the scene. This is the defect verbatim: the wire works and "
                + "no human is on it. Check that HorrorGame.Net.PlayerBridge is compiled into the build and that "
                + "NetPlayerBridge's [RuntimeInitializeOnLoadMethod] ran.");

            Assert.That(binder.BoundSource, Is.InstanceOf<PlayerRigNetView>(),
                "Something bound a view source, but not the one that reads the shipped rig. Got: "
                + binder.BoundSource!.GetType().FullName + ".");

            Assert.That(ownerCopy!.HasLocalSource, Is.True,
                "NetLocalRunner believes it bound a source and NetPlayer does not have one — BindLocalSource "
                + "refuses on a copy this machine does not own, so check ownership.");

            Assert.That(serverCopy!.HasLocalSource, Is.False,
                "The server's copy of somebody else's runner has a local source. That is the host reporting a "
                + "position on behalf of a client, which is not what §13's authority means.");

            // --- and now a person walks ---

            var startedAt = serverCopy.NetworkedPosition;
            var rigStart = motor.transform.position;

            // §13: one authority, one step. PlayerMotor.Update steps itself unless
            // something else is doing it, and this test is the something else — the same
            // seam the Mirror layer is documented to use when it drives a replicated input.
            motor.SteppedExternally = true;

            var clientBytesBefore = _clientPayloadBytesSent;
            var driveDeadline = Time.realtimeSinceStartup + DriveSecondsBudget;

            while (Vector3.Distance(rigStart, motor.transform.position) < MinimumTravelMetres
                   && Time.realtimeSinceStartup < driveDeadline)
            {
                // §05's 전진 at 걷기 2.0 m/s: SprintHeld false, so SelectBaseSpeed returns
                // WalkSpeed. Deliberately the slowest row in the table — it is comfortably
                // under the host's sanity clamp (RunnerSprintSpeed per interval), so a
                // failure here cannot be the clamp eating the movement.
                motor.Step(new MoveInput(1f, 0f, false), Time.deltaTime);
                yield return null;
            }

            var travelled = Vector3.Distance(rigStart, motor.transform.position);
            Assert.That(travelled, Is.GreaterThanOrEqualTo(MinimumTravelMetres),
                "The rig walked " + travelled.ToString("0.00") + " m in " + DriveSecondsBudget
                + " s and §05's 걷기 is " + GameConstants.WalkSpeed + " m/s, so the character itself did not "
                + "move. This is a movement failure, not a network one — check the floor collider and "
                + "CharacterController.minMoveDistance before reading anything below.");

            var walkedTo = motor.transform.position;

            yield return WaitFor(() =>
                Vector3.Distance(serverCopy.NetworkedPosition, walkedTo) <= ArrivalToleranceMetres);

            Assert.That(
                Vector3.Distance(serverCopy.NetworkedPosition, walkedTo),
                Is.LessThanOrEqualTo(ArrivalToleranceMetres),
                "The player walked to " + walkedTo + " and the host's copy of their runner is at "
                + serverCopy.NetworkedPosition + ". CmdReportView is the only thing that writes that field on a "
                + "runner the host does not own, so this failing means the Command never arrived — with a view "
                + "source bound, that points at the connection not being ready or the runner not being owned.");

            Assert.That(Vector3.Distance(startedAt, walkedTo), Is.GreaterThan(ArrivalToleranceMetres * 10f),
                "The host's copy started " + Vector3.Distance(startedAt, walkedTo).ToString("0.00")
                + " m from where the player ended up. If that is ever close to the tolerance, a runner that "
                + "never reported would pass — fix the fixture, not the assertion.");

            Assert.That(_clientPayloadBytesSent, Is.GreaterThan(clientBytesBefore),
                "KcpTransport.ClientSend never ran while the player was walking, so nothing was handed to a "
                + "socket. On its own a growing byte count would only prove KCP is pinging; taken with the "
                + "position above, it says the Command is what grew it.");

            // --- §05's other four rows, off the same rig ---

            Assert.That(serverCopy.YawDegrees, Is.EqualTo(RigYawDegrees).Within(AngleToleranceDegrees),
                "The player's yaw did not survive the trip. §05: 남의 손전등 방향이 정보다.");

            Assert.That(
                serverCopy.PitchDegrees,
                Is.EqualTo(-RigPitchDegreesLookingDown).Within(AngleToleranceDegrees),
                "The player is looking DOWN by " + RigPitchDegreesLookingDown + "° (PlayerLook counts pitch "
                + "positive downward) and the wire says " + serverCopy.PitchDegrees + "°. "
                + "INetPlayerViewSource counts it negative downward and NetPlayer.ApplyRemoteView builds the "
                + "head anchor as Euler(-pitch), so a positive value here means every remote runner is drawn "
                + "sweeping the ceiling when they are sweeping the floor. PlayerRigNetView.PitchDegrees is "
                + "where the conversion lives.");

            Assert.That(serverCopy.Carry, Is.EqualTo(NetCarryState.Empty),
                "§05's 운반 상태 row for a player with an empty inventory should be Empty, and it is "
                + serverCopy.Carry + " — the bridge is reading the loadout wrongly, which would draw a "
                + "flashlight on a pair of full hands.");

            Assert.That(serverCopy.ApproximateStaminaFraction, Is.GreaterThan(0f),
                "The player has not sprinted, so §05's 스태미나 row should be full and it arrived at zero. "
                + "A bar everybody else reads as empty is a teammate they will not wait for.");
        }

        // ------------------------------------------------------------------
        // 2 — a body to see them in
        // ------------------------------------------------------------------

        /// <summary>
        /// A runner somebody else owns is drawn with the shipped model, and the runner
        /// <em>this</em> machine owns is not drawn twice.
        /// <para>
        /// <b>What it would catch.</b> Empty <c>NetRunner.VisualFactory</c> again and the
        /// first half fails: every other player is an invisible position. Copy the owner's
        /// first-person policy onto a remote body — which is what a naive
        /// <c>Instantiate</c> of the rig's model does, because §05 sets the torso and legs
        /// to <c>ShadowsOnly</c> — and the second half fails: everyone is a walking shadow
        /// with hands. Forget to switch the owner's own proxy body off and the third fails:
        /// the player sees a motionless copy of themselves standing on their spawn marker
        /// for the whole race.
        /// </para>
        /// <para>
        /// <b>Why it cannot be satisfied in memory.</b> The renderers asserted on are the
        /// ones on the <em>client's own build</em> of the host's runner — a different
        /// <c>GameObject</c>, constructed by <c>NetRunner.SpawnOnClient</c> from a spawn
        /// message, with <c>NetworkClient.activeHost</c> false. That is literally the
        /// object a remote player would be looking at.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ARunnerSomebodyElseOwnsIsDrawnWithTheShippedModelAndTheOwnerIsNotDrawnTwice()
        {
            yield return LoadTheDescent();

            AssertTheBuildInstalledTheBridge();

            var rig = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
            Assert.That(rig, Is.Not.Null, "The descent scene has no player rig, so there is no model to copy.");

            var rigVisual = rig!.transform.Find(NetRunnerBody.RigVisualChildName);
            Assert.That(rigVisual, Is.Not.Null,
                "The shipped rig has no '" + NetRunnerBody.RigVisualChildName + "' child. That is the name "
                + "PlayerFeelHarnessMenu.BuildRig gives the model, and NetRunnerBody looks for it first.");

            var rigSkins = rigVisual!.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(rigSkins.Length, Is.GreaterThan(0),
                "The shipped rig's model carries no SkinnedMeshRenderer, so Runner.fbx did not import.");

            var rigMaterials = MaterialNames(rigSkins);

            yield return StartServerAndClient(BodyPort);

            // The host's own body: spawned unowned, which is exactly what a remote client
            // sees of somebody else in a session.
            var hostRunner = NetRunner.Build("HostRunner");
            _serverSpawned.Add(hostRunner);
            NetworkServer.Spawn(hostRunner, NetRunner.AssetId);

            var hostIdentity = hostRunner.GetComponent<NetworkIdentity>();
            Assert.That(hostIdentity, Is.Not.Null, "NetRunner.Build must produce a NetworkIdentity.");

            yield return WaitFor(() =>
                NetworkClient.spawned.ContainsKey(hostIdentity!.netId) && NetworkClient.localPlayer != null);

            Assert.That(NetworkClient.spawned.ContainsKey(hostIdentity!.netId), Is.True,
                "The client never built a copy of the host's runner within " + ConnectSecondsBudget + " s.");

            var clientCopy = NetworkClient.spawned[hostIdentity.netId];

            Assert.That(ReferenceEquals(clientCopy.gameObject, hostRunner), Is.False,
                "The client's copy is the very same GameObject as the host's. That is host mode, or an "
                + "in-process shortcut, and it proves nothing about a network.");

            Assert.That(clientCopy.isOwned, Is.False,
                "The client thinks it owns the host's runner, so this is not a remote body at all.");

            Assert.That(NetworkClient.activeHost, Is.False,
                "NetworkClient.activeHost means Mirror's in-process host wire. Everything below is worthless "
                + "if it is true.");

            // --- the body exists, on the copy a remote player would look at ---

            var remoteSkins = clientCopy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(remoteSkins.Length, Is.GreaterThan(0),
                "A runner this machine does not own has no renderer at all, so twenty players would race as "
                + "twenty invisible positions. NetRunner.VisualFactory is the seam — HorrorGame.Net.PlayerBridge "
                + "fills it from the local rig's model, and there is no NetRunnerVisual prefab on disk to fall "
                + "back to.");

            Assert.That(MaterialNames(remoteSkins), Is.EqualTo(rigMaterials),
                "The remote body is not made of the same materials as the shipped rig, so it is a different "
                + "character. Runner's materials: " + string.Join(", ", rigMaterials) + ". Remote body's: "
                + string.Join(", ", MaterialNames(remoteSkins)) + ".");

            // --- and it is DRAWN, not left in the owner's ShadowsOnly policy ---

            var drawn = 0;
            for (var i = 0; i < remoteSkins.Length; i++)
            {
                if (remoteSkins[i].shadowCastingMode != ShadowCastingMode.ShadowsOnly)
                {
                    drawn++;
                }
            }

            Assert.That(_serverPayloadBytesSent, Is.GreaterThan(0),
                "KcpTransport.ServerSend never ran, so the spawn message the client built this body from did "
                + "not come off a socket at all.");

            Assert.That(drawn, Is.EqualTo(remoteSkins.Length),
                "Only " + drawn + " of " + remoteSkins.Length + " renderers on the remote body are drawn; the "
                + "rest are ShadowCastingMode.ShadowsOnly. That is §05's first-person policy — 자기 몸은 안 "
                + "보이므로 — copied onto somebody else's body, which turns every other player into a shadow "
                + "with hands. PlayerFirstPersonView.IsOwner = false is what undoes it, and NetRunnerBody is "
                + "where that happens.");

            // The source must not have been changed on the way past. A copy that reached
            // into the rig to fix itself would leave the local player able to see their
            // own chest across the near plane.
            var ownerBodyStillHidden = false;
            for (var i = 0; i < rigSkins.Length; i++)
            {
                if (rigSkins[i].shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                {
                    ownerBodyStillHidden = true;
                }
            }

            Assert.That(ownerBodyStillHidden, Is.True,
                "Nothing under the local player's own rig is ShadowsOnly any more, so building a remote body "
                + "mutated the rig it was copied from and the owner can now see their own torso across the "
                + "bottom of the screen (§05).");

            // --- the owner is not shown a duplicate of themselves ---

            var owned = NetworkClient.localPlayer;
            Assert.That(owned, Is.Not.Null, "The client was never given a runner of its own.");

            var ownVisual = owned!.transform.Find(NetRunner.VisualChildName);
            Assert.That(ownVisual, Is.Not.Null,
                "The runner this machine owns was built without a body at all. It should be built with one and "
                + "then switched off — the assertion below is what proves the switching off happened, and it "
                + "cannot mean anything if there was never a body.");

            yield return WaitFor(() => !ownVisual!.gameObject.activeSelf);

            Assert.That(ownVisual!.gameObject.activeSelf, Is.False,
                "The runner this machine owns is drawing its own body. §01's local player is already standing "
                + "in the scene with the camera on their head, so this is a second, motionless copy of "
                + "themselves at their spawn marker for the whole race. NetLocalRunner switches it off.");
        }

        // ------------------------------------------------------------------
        // 3 — twenty different places to start
        // ------------------------------------------------------------------

        /// <summary>
        /// §11's field gets §01's rim: twenty runners, twenty distinct markers on B1, none
        /// of them the origin.
        /// <para>
        /// <b>What it would catch.</b> Before this,
        /// <c>NetworkManager.GetStartPosition()</c> returned null on every call the project
        /// had ever made — <c>NetworkStartPosition</c> registers the list and the generated
        /// map has none — so every runner spawned at (0, 0, 0) and twenty people appeared
        /// inside each other. It also catches the subtler version: the scene's 36
        /// PlayerSpawn markers resolve to only 28 distinct points, and the first two in
        /// hierarchy order are the same point to the millimetre, so round-robin over the
        /// raw list puts runners 1 and 2 exactly 0.00 m apart. The de-duplication in
        /// <see cref="NetRaceStartPoints"/> is what the separation assertion is about.
        /// </para>
        /// <para>
        /// <b>Why it cannot be satisfied in memory.</b> The markers are read off a scene
        /// loaded from disk, not built here; the position the client's own runner ends up
        /// at arrived in a spawn message on a UDP socket and is read from the client's own
        /// <c>GameObject</c>; and the re-placement half asserts a move that only
        /// <c>NetPlayer.TeleportTo</c> — a <c>[Server]</c> method — can make, observed on
        /// the other end.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TwentyRunnersGetTwentyDistinctPlacesOnB1sRim()
        {
            yield return LoadTheDescent();

            var rig = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
            Assert.That(rig, Is.Not.Null, "The descent scene has no player rig.");

            var controller = rig!.GetComponent<CharacterController>();
            Assert.That(controller, Is.Not.Null, "The player rig has no CharacterController to measure against.");

            // The width of a player, taken off the player rather than written down here.
            // Two runners closer than this are standing inside one another, which is the
            // bug in its original form moved onto the rim.
            var bodyWidthMetres = controller!.radius * 2f;

            var middle = FindTheMiddleOfTheColumn();

            yield return StartServerAndClient(RimPort);

            // --- the shipped server registered the ring on its own ---

            Assert.That(NetRaceStartPoints.Found, Is.GreaterThan(0),
                "HorrorGameNetworkManager.OnStartServer installs NetRaceStartPoints and it found no PlayerSpawn "
                + "markers at all in a loaded descent. Either Map/Markers/PlayerSpawns was renamed or the scene "
                + "was generated without §01's starting line.");

            Assert.That(NetRaceStartPoints.Registered, Is.GreaterThanOrEqualTo(GameConstants.RaceRunnersMax),
                "The map offers " + NetRaceStartPoints.Registered + " distinct starting points from "
                + NetRaceStartPoints.Found + " markers, and §11's field is " + GameConstants.RaceRunnersMax
                + ". Mirror wraps round when it runs out, so the last few runners would share a marker with the "
                + "first few. DescentMap.PlaceStarts offers B1's whole rim precisely so this cannot happen.");

            Assert.That(NetworkManager.startPositions.Count, Is.EqualTo(NetRaceStartPoints.Registered),
                "NetRaceStartPoints thinks it registered " + NetRaceStartPoints.Registered
                + " points and Mirror's own list holds " + NetworkManager.startPositions.Count
                + ". The list Mirror reads is the one that decides where anybody spawns.");

            // --- the client's own runner arrived on one of them, over the socket ---

            yield return WaitFor(() =>
                NetworkClient.localPlayer != null
                && NetworkServer.connections.Count == 1
                && FirstConnection().identity != null);

            var owned = NetworkClient.localPlayer;
            Assert.That(owned, Is.Not.Null, "The client was never given a runner.");

            var serverIdentity = FirstConnection().identity;
            Assert.That(ReferenceEquals(owned!.gameObject, serverIdentity!.gameObject), Is.False,
                "One GameObject cannot prove anything about two machines.");

            Assert.That(NetworkClient.activeHost, Is.False,
                "StartServer + StartClient must not have collapsed into host mode; if it did, no socket was used.");

            AssertOnAStartMarker(
                owned.transform.position,
                middle,
                "the runner the client actually received");

            // --- twenty of them, all different, all far enough apart to stand in ---

            var manager = _manager;
            Assert.That(manager, Is.Not.Null, "The fixture lost its network manager.");

            var places = new List<Vector3>(GameConstants.RaceRunnersMax);
            for (var i = 0; i < GameConstants.RaceRunnersMax; i++)
            {
                // GetStartPosition is the exact call HorrorGameNetworkManager.TryAddRunner
                // makes for every arriving connection — this is the shipped path being
                // asked §11's question twenty times, not a re-implementation of it.
                var start = manager!.GetStartPosition();
                Assert.That(start, Is.Not.Null,
                    "GetStartPosition returned null on runner " + (i + 1) + " of " + GameConstants.RaceRunnersMax
                    + ". Null is the answer that put every runner in this project's history at the origin.");

                places.Add(start!.position);
            }

            var closest = float.MaxValue;
            var closestPair = string.Empty;
            for (var i = 0; i < places.Count; i++)
            {
                AssertOnTheRim(places[i], middle, "start " + (i + 1));

                for (var j = i + 1; j < places.Count; j++)
                {
                    var gap = Vector3.Distance(places[i], places[j]);
                    if (gap < closest)
                    {
                        closest = gap;
                        closestPair = "starts " + (i + 1) + " and " + (j + 1) + " at " + places[i] + " and "
                                      + places[j];
                    }
                }
            }

            Assert.That(closest, Is.GreaterThan(bodyWidthMetres),
                "Two of §11's twenty runners start " + closest.ToString("0.000") + " m apart — "
                + closestPair + " — and a player is " + bodyWidthMetres.ToString("0.00")
                + " m across. They would spawn inside each other. The generated scene writes one marker per rim "
                + "CELL but positions it at the graph NODE the cell resolves to, so several markers share a "
                + "point; NetRaceStartPoints drops the repeats, and this failing means it stopped.");

            // --- and the lobby's ordering: born at the origin, put on the ring later ---

            var serverCopy = serverIdentity.GetComponent<NetPlayer>();
            var ownerCopy = owned.GetComponent<NetPlayer>();
            var binder = owned.GetComponent<NetLocalRunner>();
            Assert.That(serverCopy, Is.Not.Null, "The server's runner has no NetPlayer.");
            Assert.That(ownerCopy, Is.Not.Null, "The client's runner has no NetPlayer.");
            Assert.That(binder, Is.Not.Null, "The client's runner has no NetLocalRunner.");

            yield return WaitFor(() => binder!.BoundSource != null);

            // Detaching the rig is what reproduces §11's lobby rather than what dodges it:
            // a runner is created when a connection reports ready, several seconds before
            // the descent's scene exists, so on the shipped path there is no rig to report
            // from and the body stands at the origin holding a position nobody has
            // corrected. NetLocalRunner does not re-bind — it stops looking after the
            // first success — so this state holds for the rest of the test.
            ownerCopy!.UnbindLocalSource();
            serverCopy!.TeleportTo(Vector3.zero);

            // NetworkedPosition and not transform.position: an owned copy applies the
            // replicated value once, at OnStartClient, and never again — ApplyRemoteView
            // is guarded on !isOwned, because the owner is supposed to be the one moving.
            // The SyncVar is the thing that crossed the wire, so it is the thing to read.
            yield return WaitFor(() => ownerCopy.NetworkedPosition.magnitude < ArrivalToleranceMetres);

            Assert.That(ownerCopy.NetworkedPosition.magnitude, Is.LessThan(ArrivalToleranceMetres),
                "The fixture could not put the runner back at the origin, so the recovery below would prove "
                + "nothing. TeleportTo is a [Server] method and its value reaches the client as a SyncVar.");

            Assert.That(NetRaceStartPoints.PlaceSpawnedRunners(), Is.GreaterThan(0),
                "PlaceSpawnedRunners moved nobody. This is the call that runs when the descent's scene finishes "
                + "loading, and without it every runner spawned during §11's lobby stays at (0, 0, 0) for the "
                + "whole race.");

            yield return WaitFor(() =>
                ownerCopy.NetworkedPosition.magnitude > ArrivalToleranceMetres * 10f);

            AssertOnAStartMarker(
                ownerCopy.NetworkedPosition,
                middle,
                "the runner after the building arrived and PlaceSpawnedRunners ran");
        }

        // ------------------------------------------------------------------
        // rig
        // ------------------------------------------------------------------

        /// <summary>
        /// Asserts that the shipped build, not this fixture, wired the player layer to the
        /// network layer.
        /// <para>
        /// <see cref="NetPlayerBridge.InstalledAtStartup"/> is set in a private
        /// <c>[RuntimeInitializeOnLoadMethod]</c> and nowhere else, so nothing in a test
        /// can make it true. That distinction is the entire point: a fixture calling
        /// <c>Install</c> is how this project ended up with a network layer that only
        /// worked in tests.
        /// </para>
        /// </summary>
        private static void AssertTheBuildInstalledTheBridge()
        {
            Assert.That(NetPlayerBridge.InstalledAtStartup, Is.True,
                "HorrorGame.Net.PlayerBridge never installed itself. Only Unity's own "
                + "[RuntimeInitializeOnLoadMethod] can set this, so a false here means the assembly is not in "
                + "the build or the entry point was deleted — and then nothing implements INetPlayerViewSource "
                + "in a shipped player, which is the defect this whole fixture exists for.");
        }

        /// <summary>
        /// Loads the descent the game actually ships, and lets its own <c>Start</c> lay the
        /// match out.
        /// <para>
        /// <c>LogAssert.ignoreFailingMessages</c> goes on for the rest of the test: the
        /// solo scene brings up a whole match — NavMesh, monster, audio, §03's chain — and
        /// this fixture's subject is none of those. Every assertion below is on state
        /// rather than on a log line, so nothing is being hidden that this file is
        /// responsible for.
        /// </para>
        /// </summary>
        private IEnumerator LoadTheDescent()
        {
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
            _soloSceneLoaded = true;

            // Two frames: one for the load to take effect, one for every Start to run.
            yield return null;
            yield return null;
        }

        /// <summary>
        /// A player that can be walked, built from the shipped components.
        /// <para>
        /// Not the FBX rig — that one is assembled by an editor-only menu out of
        /// <c>AssetDatabase</c> and is the subject of the body test, which loads the real
        /// scene for it. What matters here is that the movement is §05's own
        /// <see cref="PlayerMotor"/> against a real <see cref="CharacterController"/> and
        /// the view is §05's own <see cref="PlayerLook"/>, because those are the four
        /// components <c>PlayerRigNetView</c> reads.
        /// </para>
        /// <para>
        /// The floor is a collider with no renderer. A <c>GameObject.CreatePrimitive</c>
        /// would drag in <c>RenderPipelineAsset.defaultMaterial</c>, which is the magenta
        /// scar <see cref="NetRunner"/> already documents, for no benefit — nothing here
        /// is drawn.
        /// </para>
        /// </summary>
        private PlayerMotor BuildDrivableRig()
        {
            _floor = new GameObject("Floor");
            _floor!.transform.position = new Vector3(0f, -0.5f, 0f);
            var ground = _floor!.AddComponent<BoxCollider>();

            // Wide enough that §05's 걷기 cannot walk off it inside DriveSecondsBudget:
            // 2.0 m/s × 5 s = 10 m, and this is a hundred times that in each direction.
            ground.size = new Vector3(2000f, 1f, 2000f);

            _drivableRig = new GameObject("Player");
            _drivableRig!.transform.position = new Vector3(0f, 0.1f, 0f);

            var controller = _drivableRig!.AddComponent<CharacterController>();

            // Height and centre from the constant that describes the model —
            // ViewMotionTuning.RigHeightMetres is what PlayerFeelHarnessMenu.BuildRig uses
            // for the same two fields — so the capsule's feet are at the object's origin
            // and §05's position row means what it means on the shipped rig.
            //
            // The radius is deliberately left at Unity's default. The rig's own 0.3 m is a
            // private field of an editor-only menu, and writing a second copy of it here
            // would be a tuned number this file has no authority over; nothing in this test
            // touches anything but a flat floor.
            controller.height = ViewMotionTuning.RigHeightMetres;
            controller.center = new Vector3(0f, ViewMotionTuning.RigHeightMetres * 0.5f, 0f);
            controller.stepOffset = GameConstants.PlayerStepOffsetMetres;

            _drivableRig!.AddComponent<PlayerLook>();
            _drivableRig!.AddComponent<PlayerLoadout>();

            // Last, because PlayerMotor.Awake wires itself to whatever is already on the
            // object and adds a PlayerStance if there is none — the same ordering
            // PlayerFeelHarnessMenu.BuildRig documents.
            return _drivableRig!.AddComponent<PlayerMotor>();
        }

        /// <summary>
        /// Where the middle of the column is, read off the scene's own mark for it.
        /// <para>
        /// The generator leaves exactly one <c>EntranceLight</c> burning, and on §01's
        /// tower that mark is §02's finish — the middle of B8. Its X and Z are therefore
        /// the axis of the whole building, which is what "the rim" is measured from. Taken
        /// from the scene rather than written down, so a regenerated map on a different
        /// seed does not silently make this fixture assert about a coordinate that has
        /// moved.
        /// </para>
        /// </summary>
        private static Vector3 FindTheMiddleOfTheColumn()
        {
            var lights = UnityEngine.Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i].name.StartsWith(EntranceLightPrefix, StringComparison.Ordinal))
                {
                    return lights[i].transform.position;
                }
            }

            throw new InvalidOperationException(
                "No '" + EntranceLightPrefix + "' in the descent scene. It is the only mark for §02's finish, "
                + "so without it there is nothing to measure a rim against.");
        }

        /// <summary>
        /// Asserts a position is one of the registered starting points, and then that that
        /// point is on the rim.
        /// <para>
        /// <b>The marker check is not redundant, and leaving it out was a real hole.</b>
        /// The generated building occupies x, z ∈ [6.25, 56.25] with its middle at 31.25,
        /// so the world origin — the value every runner in this project's history spawned
        /// at — is 31.25 m from the middle in plan and at y 0, and would therefore
        /// <em>pass</em> a rim test on its own. The bug this fixture exists to catch has to
        /// be caught by being nowhere near a marker, not by being near the middle.
        /// </para>
        /// </summary>
        private static void AssertOnAStartMarker(Vector3 position, Vector3 middle, string what)
        {
            var nearest = float.MaxValue;
            for (var i = 0; i < NetworkManager.startPositions.Count; i++)
            {
                var start = NetworkManager.startPositions[i];
                if (start == null)
                {
                    continue;
                }

                nearest = Mathf.Min(nearest, Vector3.Distance(position, start.position));
            }

            Assert.That(nearest, Is.LessThanOrEqualTo(ArrivalToleranceMetres),
                what + " is at " + position + ", which is " + nearest.ToString("0.00")
                + " m from the nearest of the " + NetworkManager.startPositions.Count
                + " registered PlayerSpawn markers. (0, 0, 0) is 8.84 m from the closest marker in this "
                + "building and sits outside it entirely, so this is what 'every runner spawns at the origin' "
                + "looks like from the receiving end.");

            AssertOnTheRim(position, middle, what);
        }

        /// <summary>
        /// Asserts a position is on B1's outer ring: outside the wall that separates the
        /// 중간 고리 from the 외곽 고리, and above §02's finish rather than on some other
        /// storey.
        /// </summary>
        private static void AssertOnTheRim(Vector3 position, Vector3 middle, string what)
        {
            var chebyshev = Mathf.Max(
                Mathf.Abs(position.x - middle.x),
                Mathf.Abs(position.z - middle.z));

            Assert.That(chebyshev, Is.GreaterThan(RimWallMetres),
                "§01 starts the field on the RIM of B1 and " + what + " is " + chebyshev.ToString("0.00")
                + " m from the middle of the column in plan, inside the " + RimWallMetres
                + " m wall RadialStorey puts between the 중간 고리 and the 외곽 고리. Position: " + position + ".");

            Assert.That(position.y, Is.GreaterThan(middle.y),
                "§01 starts the field on B1 and finishes it at the middle of B8, " + what + " is at y "
                + position.y.ToString("0.00") + " and §02's finish is at y " + middle.y.ToString("0.00")
                + ". A runner who starts at or below the finish has no descent to make.");
        }

        /// <summary>
        /// The shared material names on a set of renderers, sorted, so two bodies can be
        /// compared without depending on renderer order.
        /// <para>
        /// <c>sharedMaterials</c> and not <c>materials</c>: the latter instantiates, which
        /// both leaks a material per call and appends " (Instance)" to every name.
        /// </para>
        /// </summary>
        private static List<string> MaterialNames(SkinnedMeshRenderer[] renderers)
        {
            var names = new List<string>();
            for (var i = 0; i < renderers.Length; i++)
            {
                var materials = renderers[i].sharedMaterials;
                for (var j = 0; j < materials.Length; j++)
                {
                    if (materials[j] != null)
                    {
                        names.Add(materials[j].name);
                    }
                }
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>
        /// Brings up the shipped manager as a server and then, in the same process, as a
        /// real remote client.
        /// <para>
        /// <c>StartServer</c> + <c>StartClient</c> rather than <c>StartHost</c>:
        /// <c>StartHost</c> gives the manager a <c>LocalConnectionToServer</c> and never
        /// opens a socket, which would make every assertion in this file a statement about
        /// in-process memory.
        /// </para>
        /// </summary>
        private IEnumerator StartServerAndClient(ushort port)
        {
            _rig = new GameObject("NetHumanRunnerRig");
            _rig!.SetActive(false);

            // Inactive while these are assigned: KcpTransport.Awake freezes its KcpConfig
            // from the serialised fields, so a port written after AddComponent on a live
            // object is remembered by the inspector and ignored by the socket.
            _transport = _rig!.AddComponent<KcpTransport>();
            _transport!.port = port;
            _transport!.DualMode = DualMode;

            _manager = _rig!.AddComponent<HorrorGameNetworkManager>();
            _manager!.transport = _transport;

            _rig!.SetActive(true);
            Transport.active = _transport;
            yield return null;

            Assert.That(NetworkManager.singleton, Is.SameAs(_manager),
                "Another NetworkManager owns the singleton, so this fixture's settings — the transport, the "
                + "address, §11's cap — are not the ones the session will run on.");

            _manager!.networkAddress = Loopback;

            // KcpTransport raises these from ClientSend/ServerSend only — KCP's own
            // handshake never appears here, so a non-zero count is Mirror-level payload.
            _transport!.OnClientDataSent += (segment, channelId) => _clientPayloadBytesSent += segment.Count;
            _transport!.OnServerDataSent += (connectionId, segment, channelId) =>
                _serverPayloadBytesSent += segment.Count;

            _manager!.StartServer();

            Assert.That(NetworkServer.active, Is.True,
                "StartServer did not bring the server up. EnsureTransport chose " + _manager!.ActiveTransportKind
                + ".");

            _manager!.StartClient();

            yield return WaitFor(() => NetworkClient.isConnected && NetworkServer.connections.Count == 1);

            Assert.That(NetworkClient.isConnected, Is.True,
                "The client never reached Connected on " + Loopback + ":" + port + " within "
                + ConnectSecondsBudget + " s. Either KcpTransport could not bind — another process on that "
                + "port, or a host firewall refusing a UDP listener — or the handshake datagrams never arrived.");

            Assert.That(NetworkServer.connections.Count, Is.EqualTo(1),
                "The client thinks it is connected but the server has no connection for it.");

            Assert.That(FirstConnection().address, Does.Contain(Loopback),
                "The server's idea of where this client is comes from KcpTransport.ServerGetClientAddress, "
                + "which reads the IPEndPoint a datagram actually arrived from. A fabricated connection has no "
                + "endpoint to report. Got: '" + FirstConnection().address + "'.");
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

        private static void DestroyIfPresent(ref GameObject? target)
        {
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }

            target = null;
        }
    }
}
