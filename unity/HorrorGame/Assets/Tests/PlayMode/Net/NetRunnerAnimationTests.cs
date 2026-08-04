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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Net
{
    /// <summary>
    /// Every other player slides. This is the fixture that says whether they still do.
    /// <para>
    /// <b>The defect.</b> <c>NetRunnerBody</c> built a remote runner out of the shipped
    /// model and its own summary conceded "The copy is not animated", so in §11's
    /// twenty-runner field the thing a player looks at most — somebody else — was a
    /// mannequin in <c>Runner.fbx</c>'s bind pose gliding through the maze. The local rig
    /// has driven nine clips since <c>PlayerAnimatorDriver</c> existed. Nothing drove them
    /// on anybody else.
    /// </para>
    /// <para>
    /// <b>The bar, which is <see cref="NetSocketTests"/>'s.</b> Two peers, two UDP
    /// sockets, <c>NetworkClient.activeHost</c> false, and every claim made about a
    /// <em>different <c>GameObject</c></em> from the one that produced it. Nothing here
    /// binds a view source, builds a body, loads a clip or sets the transform of the
    /// object it asserts on: the only path from the moving legs to the moving bones is
    /// <c>CmdReportView</c> over KCP and the <c>_position</c> SyncVar coming back out of
    /// it, and the byte counters on <c>KcpTransport</c>'s own send hooks are asserted to
    /// have moved. This project has shipped an animator attached to nothing, a camera
    /// attached to nothing and a race recorded into an object nothing read, so the pose
    /// is not asserted from a field alone — <see cref="NetRunnerAnimation.WeightOf"/>
    /// reads the mixer, and the <em>bones of the remote body</em> are measured before and
    /// during the walk. A graph that exists and does not evaluate leaves them exactly
    /// where they were.
    /// </para>
    /// <para>
    /// <b>Why the descent scene.</b> The remote body is a copy of the local rig's model,
    /// and that rig is assembled by an editor-only menu out of <c>AssetDatabase</c>. The
    /// only place a shipped build ever has one is a loaded scene, which is exactly
    /// <c>NetRunnerBody</c>'s argument for copying it, so the fixture loads
    /// <c>Map_FirstSketch_Solo</c> — the descent the game actually loads — and takes the
    /// player out of it.
    /// </para>
    /// </summary>
    public sealed class NetRunnerAnimationTests
    {
        /// <summary>Loopback as an IPv4 literal — a name resolves to ::1 on an IPv6 host and the failure is a timeout, not an error.</summary>
        private const string Loopback = "127.0.0.1";

        /// <summary>IPv4 only on both sockets. <c>Socket.DualMode</c> is documented to throw on platforms without IPv6, and none of this is about IPv6.</summary>
        private const bool DualMode = false;

        /// <summary>Port for the locomotion test. Next in the 477xx block after the neighbouring fixtures' 47701–47708.</summary>
        private const ushort GaitPort = 47709;

        /// <summary>Port for the teleport test. A separate number, so a fixture that leaked a bound socket fails where it leaked it.</summary>
        private const ushort TeleportPort = 47710;

        /// <summary>The descent, as <c>SoloPlaytest.BuildScene</c> wrote it and as the game loads it.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>Wall-clock seconds for a loopback handshake plus a spawn round trip. Seconds and not frames: under <c>-batchmode</c> the player loop is not waiting on a display.</summary>
        private const float ConnectSecondsBudget = 10f;

        /// <summary>
        /// The component the fixture switches off in the loaded descent, by type name.
        /// <para>
        /// It is not referenced as a type because this assembly does not reference
        /// <c>HorrorGame.Gameplay</c> — deliberately, since the subject here is the
        /// network layer and the player layer. Switching it off is not tidiness: its
        /// <c>OutOfBounds</c> guard teleports the player back inside the building the
        /// moment it decides they have left it, and this fixture walks the player onto a
        /// floor of its own well outside the map. A match director recovering a runner
        /// mid-assertion would read here as a teleport, which is the other test's subject.
        /// </para>
        /// </summary>
        private const string MatchDirectorTypeName = "MatchDirector";

        /// <summary>
        /// Where the player is walked, in metres from the world origin on both plan axes.
        /// <para>
        /// The generated building occupies x, z ∈ [6.25, 56.25], so this is several
        /// hundred metres clear of every wall, chute mouth and NavMesh island in it. The
        /// point is not secrecy — it is that a walk which bumps a corridor wall measures
        /// the corridor, and §01's storeys are 2.5 m cells.
        /// </para>
        /// </summary>
        private const float TestGroundMetres = 500f;

        /// <summary>
        /// Seconds spent walking, sprinting and standing in each phase of the gait test.
        /// <para>
        /// At §06's 걷기 2.0 m/s that is 3 m — some forty snapshots at
        /// <see cref="GameConstants.NetworkSendRate"/>, against a filter whose time
        /// constant is four of them, so the estimate is converged many times over before
        /// anything is asserted.
        /// </para>
        /// </summary>
        private const float PhaseSeconds = 1.5f;

        /// <summary>
        /// Seconds allowed for the body to fall back to Idle once the player stops.
        /// <see cref="NetRunnerAnimation.StillGraceSeconds"/> plus its decay measures
        /// 0.317 s from a standstill; this is six times that, so a failure here is a body
        /// that never stops rather than one that took a beat longer than expected on a
        /// loaded machine.
        /// </summary>
        private const float StopSecondsBudget = 2f;

        /// <summary>
        /// How far the teleport test moves a runner, metres.
        /// <para>
        /// <b>Derived from <c>NetInterestScope.PerceptionRange</c>, and it has to be.</b>
        /// This was a flat 40 m — a plain diagonal across one 57.5 m storey — and 40 m is
        /// OUTSIDE perception range, so <c>HorrorInterestManagement</c> did the correct
        /// thing and told the client to despawn the runner in the middle of the jump.
        /// <c>NetRunner.UnspawnOnClient</c> destroys the body, and the measurement loop
        /// below then read a destroyed <c>NetRunnerAnimation</c> and threw
        /// <c>MissingReferenceException</c>. There was nothing wrong with the guard: the
        /// scenario was simply unobservable, because a runner that leaves your bubble has
        /// no body on your machine to sprint with.
        /// </para>
        /// <para>
        /// Forty metres is a plain diagonal across one 57.5 m storey — smaller than being
        /// sent from the middle of B8 back to a B1 cell (~38 m through eight floors), and
        /// larger than §11's lobby-to-rim placement, which <c>NetHumanRunnerTests</c>
        /// measures at 8.84 m. Differentiated naively it is 1200 m/s, which is 214 times
        /// §06's sprint: without the guard the body plays a sprint cycle while it is being
        /// put somewhere it never ran to.
        /// </para>
        /// <para>
        /// It is deliberately at the edge of <c>NetInterestScope.PerceptionRange</c>, which
        /// is why the test turns interest management off before jumping — see the comment
        /// at that line. Shrinking the jump to stay inside the bubble was the other option
        /// and it was rejected: the distances this guard exists for are exactly the ones
        /// that cross the radius, and a test that only ever jumps 4 m would stop covering
        /// them.
        /// </para>
        /// </summary>
        private const float TeleportMetres = 40f;

        /// <summary>
        /// Smallest bone rotation, in degrees, that counts as "the body moved".
        /// <para>
        /// Any real gait swings a thigh through tens of degrees, and a
        /// <c>PlayableGraph</c> that was built and never evaluated leaves every bone at
        /// exactly the value it was instantiated with — the difference between the two
        /// answers is not marginal, so the threshold only has to be clear of numerical
        /// noise. This is the assertion the old summary's "the copy is not animated"
        /// fails on, and it fails at 0.000°.
        /// </para>
        /// </summary>
        private const float MovedDegrees = 5f;

        /// <summary>
        /// §06's 걷기/달리기 crossover, m/s. Derived here the same way
        /// <c>PlayerAnimatorDriver.Resolve</c> derives it, rather than written down, so
        /// retuning either speed moves the fixture's expectations with the game's.
        /// </summary>
        private static readonly float RunThreshold =
            (GameConstants.WalkSpeed + GameConstants.RunSpeed) * 0.5f;

        private GameObject? _rig;
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

            // Repairing a sibling fixture's teardown, not standing in for the shipped
            // installation: NetRunnerTests deliberately empties NetRunner.VisualFactory to
            // prove a bodiless runner still replicates, and static state outlives a
            // fixture. That the *build* installs it is asserted separately, on a flag only
            // Unity's own [RuntimeInitializeOnLoadMethod] can set.
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
            DestroyIfPresent(ref _floor);

            _transport = null;
            _manager = null;

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
                    var empty = SceneManager.CreateScene("NetRunnerAnimationTests_Empty");
                    SceneManager.SetActiveScene(empty);
                    yield return SceneManager.UnloadSceneAsync(solo);
                }

                _soloSceneLoaded = false;
            }

            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // 1 — the gait, from the wire
        // ------------------------------------------------------------------

        /// <summary>
        /// A person walks, then sprints, then stops, and the body every other machine
        /// draws for them walks, runs and stands — with its bones actually moving.
        /// <para>
        /// <b>What it would catch.</b> Delete <c>NetRunnerAnimation</c> and the body
        /// returns to the bind pose: <c>State</c> never leaves Idle and the bone sweep
        /// measures 0°. Drive it from <c>transform.position</c> instead of the SyncVar and
        /// the speed is a filtered copy of a filter. Differentiate per frame instead of
        /// per snapshot and the estimate collapses toward zero on every frame no snapshot
        /// landed on, which at 60 fps is half of them. Choose the pose with a private
        /// threshold instead of <c>PlayerAnimatorDriver.Resolve</c> and the sprint
        /// assertion goes on passing while a remote runner and their own screen disagree
        /// about which verb they are doing.
        /// </para>
        /// <para>
        /// <b>Why it cannot be satisfied in memory.</b> The animated object is the
        /// <em>host's</em> copy of a runner the host does not own. The only line in the
        /// project that writes its position is the body of the private
        /// <c>CmdReportView</c>, which is a <c>[Command]</c>; the two copies are asserted
        /// to be different <c>GameObject</c>s; <c>NetworkClient.activeHost</c> is asserted
        /// false so no in-process host wire can be carrying it; and
        /// <c>KcpTransport.ClientSend</c>'s own byte counter is asserted to have grown
        /// while the player was walking. The movement itself comes out of
        /// <c>PlayerMotor.Step</c> — §05's speed table against a real
        /// <c>CharacterController</c> — and not out of an assignment.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ARemoteRunnersLegsWalkRunAndStopFromWhatCrossesTheWire()
        {
            yield return LoadTheDescent();

            AssertTheBuildInstalledTheBridge();

            var motor = TakeThePlayerOutOfTheMatch();
            var driver = motor.GetComponentInChildren<PlayerAnimatorDriver>(true);

            // Asserted before the network exists, so a missing clip is reported as a
            // missing clip rather than as a runner who would not animate. This is the
            // state SoloPlaytest.AuditAnimatorWiring exists to diagnose, and if it is
            // wrong then nothing below can mean anything.
            Assert.That(driver, Is.Not.Null,
                "The descent's player rig has no PlayerAnimatorDriver, so there is no clip set for a remote body "
                + "to borrow. PlayerFeelHarnessMenu.BuildRig puts one on the rig root.");

            Assert.That(driver!.ClipFor(PlayerAnimationState.Walk), Is.Not.Null,
                "The rig's driver has no Walk clip. Run HorrorGame.EditorTools.SoloPlaytest.BuildBatch — its "
                + "read-back audit of Map_FirstSketch_Solo.unity says which of the three causes it is.");

            Assert.That(driver.ClipFor(PlayerAnimationState.Run), Is.Not.Null, "The rig's driver has no Run clip.");

            yield return StartServerAndClient(GaitPort);

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

            var serverCopy = serverIdentity.GetComponent<NetPlayer>();
            Assert.That(serverCopy, Is.Not.Null, "The server's runner has no NetPlayer.");

            Assert.That(serverCopy!.HasLocalSource, Is.False,
                "The host's copy of somebody else's runner has a local view source, so it could be reading the "
                + "rig directly instead of the wire. Nothing below would then be about a network.");

            // Nothing in this fixture binds the rig; NetLocalRunner does, on the copy this
            // machine owns, from a factory NetPlayerBridge installed at startup.
            var binder = owned.GetComponent<NetLocalRunner>();
            Assert.That(binder, Is.Not.Null, "NetRunner.Build did not put a NetLocalRunner on the runner.");

            yield return WaitFor(() => binder!.BoundSource != null);

            Assert.That(binder!.BoundSource, Is.InstanceOf<PlayerRigNetView>(),
                "Nothing bound the shipped rig as the report source within " + ConnectSecondsBudget
                + " s, so no position will ever leave this machine and the body has nothing to animate from.");

            // --- the body every other machine draws for this player ---

            var remote = serverIdentity.GetComponentInChildren<NetRunnerAnimation>(true);
            Assert.That(remote, Is.Not.Null,
                "The host's copy of somebody else's runner has no NetRunnerAnimation at all, so it is the "
                + "mannequin this fixture exists for. NetRunnerBody.Build adds it to the body it copies out of "
                + "the local rig, and NetRunner.VisualFactory is the seam that calls it.");

            Assert.That(remote!.HasClips, Is.True,
                "The remote body was built with no clips. NetRunnerBody hands it the local rig's own "
                + "PlayerAnimatorDriver through ClipFor; an empty set means that driver's slots are empty, which "
                + "SoloPlaytest.AuditAnimatorWiring reads the saved scene back to diagnose.");

            Assert.That(remote.IsPlaying, Is.True,
                "The remote body has clips and no running PlayableGraph, so nothing is driving its Animator.");

            var bones = BonesOf(serverIdentity.gameObject);
            Assert.That(bones.Length, Is.GreaterThan(0),
                "The remote body has no skinned bones to measure, so Runner.fbx did not import onto it.");

            // --- 걷기 ---

            motor.SteppedExternally = true;

            var bytesBefore = _clientPayloadBytesSent;
            var rest = Pose(bones);
            var swept = 0f;

            yield return Drive(motor, new MoveInput(1f, 0f, false), PhaseSeconds,
                () => swept = Mathf.Max(swept, MaxDegreesFrom(rest, bones)));

            Assert.That(motor.GroundSpeed, Is.GreaterThan(GameConstants.StillSpeedThreshold),
                "The player's own rig is not moving, so this is a movement failure and not a network one — check "
                + "the fixture's floor collider before reading anything below.");

            Assert.That(motor.GroundSpeed, Is.LessThan(RunThreshold),
                "The fixture asked for §06's 걷기 " + GameConstants.WalkSpeed + " m/s and the rig is doing "
                + motor.GroundSpeed.ToString("0.00") + " m/s, which is already past the 달리기 crossover at "
                + RunThreshold.ToString("0.00") + ". The Walk assertion below would be asserting the wrong verb.");

            Assert.That(_clientPayloadBytesSent, Is.GreaterThan(bytesBefore),
                "KcpTransport.ClientSend never ran while the player was walking, so nothing was handed to a "
                + "socket and whatever the body is doing, it is not doing it from a network.");

            Assert.That(remote.SamplesTaken, Is.GreaterThan(0),
                "The remote body has differentiated no wire positions at all. Every assertion about its pose "
                + "would then be a statement about its initial state — which is Idle, and which would pass a "
                + "'not running' test for entirely the wrong reason.");

            // ±40% of 걷기 is [1.2, 2.8] m/s, which is deliberately inside the pose
            // assertion's own bounds — above StillSpeedThreshold and below the 3.25
            // crossover — so this can only fail on a reconstruction that is wrong in a way
            // the pose has not noticed yet.
            Assert.That(remote.GroundSpeed, Is.EqualTo(GameConstants.WalkSpeed).Within(GameConstants.WalkSpeed * 0.4f),
                "The speed reconstructed from §05's 위치 row is " + remote.GroundSpeed.ToString("0.00")
                + " m/s and the player is walking at " + motor.GroundSpeed.ToString("0.00")
                + ". The clip's playback speed is groundSpeed / referenceSpeed, so an estimate this far out is "
                + "feet that skate — and §12 makes the stride the Listener's distance cue.");

            Assert.That(remote.State, Is.EqualTo(PlayerAnimationState.Walk),
                "A player walking at " + motor.GroundSpeed.ToString("0.00")
                + " m/s is drawn to everybody else as " + remote.State + ".");

            Assert.That(remote.WeightOf(PlayerAnimationState.Walk), Is.GreaterThan(0.5f),
                "NetRunnerAnimation says Walk and the mixer is only " +
                remote.WeightOf(PlayerAnimationState.Walk).ToString("0.00")
                + " of the way there, so the pose lives in a field and not in the graph. That is this project's "
                + "signature failure written in animation: chosen, reviewed, and not in the artefact.");

            Assert.That(swept, Is.GreaterThan(MovedDegrees),
                "The remote body's bones moved " + swept.ToString("0.00") + "° while the player walked "
                + (GameConstants.WalkSpeed * PhaseSeconds).ToString("0.0")
                + " m. This is the defect verbatim — 'the copy is not animated' measures 0.00° — and it is "
                + "measured on the bones rather than on a state field precisely because a graph can be built, "
                + "reported as playing, and never evaluated.");

            // --- 달리기 ---

            yield return Drive(motor, new MoveInput(1f, 0f, true), PhaseSeconds, null);

            Assert.That(motor.GroundSpeed, Is.GreaterThan(RunThreshold),
                "The fixture asked for §06's 질주 and the rig is doing " + motor.GroundSpeed.ToString("0.00")
                + " m/s, under the 달리기 crossover. Stamina may have run out sooner than expected — even then "
                + "SpeedResolver falls back to " + GameConstants.RunSpeed + " m/s, so this failing is a movement "
                + "failure.");

            Assert.That(remote.State, Is.EqualTo(PlayerAnimationState.Run),
                "A player sprinting at " + motor.GroundSpeed.ToString("0.00")
                + " m/s is drawn to everybody else as " + remote.State + " (reconstructed speed "
                + remote.GroundSpeed.ToString("0.00") + " m/s). The pose is chosen by "
                + "PlayerAnimatorDriver.Resolve, so a disagreement here is a disagreement about the number "
                + "handed to it.");

            Assert.That(remote.WeightOf(PlayerAnimationState.Run), Is.GreaterThan(0.5f),
                "The remote body says Run and the mixer has not crossfaded to it.");

            // --- and standing still ---

            var stopDeadline = Time.realtimeSinceStartup + StopSecondsBudget;
            while (remote.State != PlayerAnimationState.Idle && Time.realtimeSinceStartup < stopDeadline)
            {
                motor.Step(new MoveInput(0f, 0f, false), Time.deltaTime);
                yield return null;
            }

            Assert.That(remote.State, Is.EqualTo(PlayerAnimationState.Idle),
                "The player stopped " + StopSecondsBudget + " s ago and their body is still playing "
                + remote.State + " at " + remote.GroundSpeed.ToString("0.00")
                + " m/s. A stationary runner sends no snapshots — Mirror only replicates a SyncVar that changed "
                + "— so 'no new position' is the only evidence a remote body ever gets that somebody stopped. "
                + "NetRunnerAnimation.StillGraceSeconds is what reads it.");
        }

        // ------------------------------------------------------------------
        // 2 — a teleported runner does not sprint
        // ------------------------------------------------------------------

        /// <summary>
        /// A runner is moved forty metres between two snapshots, and the body drawn for
        /// them does not break into a sprint on the way.
        /// <para>
        /// <b>What it would catch.</b> §01 drops a runner through a 투하구 onto the rim of
        /// the floor below and §06 sends a caught one back to the cell they started from on
        /// B1; neither is running, and both move a position tens of metres in one tick. A
        /// naive differentiation reads 1200 m/s and plays a sprint cycle while the body is
        /// being put somewhere it never ran to — the single most confusing thing another
        /// player could be shown, because a sprint is exactly the signal §12 asks them to
        /// trust.
        /// </para>
        /// <para>
        /// <b>Why the guard needs no new byte on the wire.</b>
        /// <c>NetPlayer.CmdReportView</c> already clamps a reported position to
        /// <c>RunnerSprintSpeed × elapsed</c>, so the host itself guarantees that nothing
        /// which <em>ran</em> can move faster than that; the one writer that is not clamped
        /// is <c>TeleportTo</c>. The assertion on
        /// <see cref="NetRunnerAnimation.TeleportsIgnored"/> is what makes this test mean
        /// something: without it, a body that never received the jump at all would pass
        /// for the same reason as a body that received it and handled it.
        /// </para>
        /// <para>
        /// <b>Why it cannot be satisfied in memory.</b> The body asserted on is the
        /// <em>client's own</em> copy, built by <c>NetRunner.SpawnOnClient</c> from a spawn
        /// message, with <c>NetworkClient.activeHost</c> false and
        /// <c>KcpTransport.ServerSend</c>'s byte counter asserted to have grown. The move
        /// is made by <c>TeleportTo</c>, a <c>[Server]</c> method, on the other
        /// <c>GameObject</c>.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ATeleportedRemoteRunnerDoesNotSprintAcrossTheBuilding()
        {
            yield return LoadTheDescent();

            AssertTheBuildInstalledTheBridge();

            TakeThePlayerOutOfTheMatch();

            yield return StartServerAndClient(TeleportPort);

            // Spawned unowned, which is exactly what a remote client sees of somebody else.
            var hostRunner = NetRunner.Build("HostRunner");
            _serverSpawned.Add(hostRunner);
            NetworkServer.Spawn(hostRunner, NetRunner.AssetId);

            var hostIdentity = hostRunner.GetComponent<NetworkIdentity>();
            Assert.That(hostIdentity, Is.Not.Null, "NetRunner.Build must produce a NetworkIdentity.");

            yield return WaitFor(() => NetworkClient.spawned.ContainsKey(hostIdentity!.netId));

            Assert.That(NetworkClient.spawned.ContainsKey(hostIdentity!.netId), Is.True,
                "The client never built a copy of the host's runner within " + ConnectSecondsBudget + " s.");

            var clientCopy = NetworkClient.spawned[hostIdentity.netId];

            Assert.That(ReferenceEquals(clientCopy.gameObject, hostRunner), Is.False,
                "The client's copy is the very same GameObject as the host's — host mode, or an in-process "
                + "shortcut, and it proves nothing about a network.");

            Assert.That(NetworkClient.activeHost, Is.False,
                "NetworkClient.activeHost means Mirror's in-process host wire. Everything below is worthless "
                + "if it is true.");

            var remote = clientCopy.GetComponentInChildren<NetRunnerAnimation>(true);
            Assert.That(remote, Is.Not.Null,
                "The client's copy of the host's runner has no NetRunnerAnimation, so it is the mannequin.");

            Assert.That(remote!.IsPlaying, Is.True, "The remote body has no running PlayableGraph.");

            // ----------------------------------------------------------------
            // Both ends of the jump have to be inside the observer's bubble, or there is
            // no body on the client to measure.
            //
            // HorrorInterestManagement.OnRebuildObservers measures from
            // conn.identity.transform.position — the client's OWN runner on the server —
            // and drops any identity further than NetInterestScope.PerceptionRange from it.
            // A jump that ends outside that radius is despawned by Mirror and destroyed by
            // NetRunner.UnspawnOnClient, which is correct behaviour and makes the sprint
            // this test is about unobservable: a runner who left your bubble has no body on
            // your machine to sprint with.
            //
            // So the runner is parked ON the observer first and the landing is measured
            // from there. Done before teleportsBefore is read, so this placement is not the
            // teleport under test.
            var observerIdentity = FirstConnection().identity;
            Assert.That(observerIdentity, Is.Not.Null,
                "The connection has no player identity, so interest management has nothing to measure from and "
                + "this test cannot know whether its jump stays observable.");

            var hostPlayer = hostRunner.GetComponent<NetPlayer>();
            Assert.That(hostPlayer, Is.Not.Null, "The host's runner has no NetPlayer to teleport.");

            // ----------------------------------------------------------------
            // Interest management OFF for this test, deliberately and with the reason
            // stated, because it is the one thing here that could look like cheating.
            //
            // HorrorInterestManagement despawns any identity further than
            // NetInterestScope.PerceptionRange (40 m) from conn.identity, and
            // NetRunner.UnspawnOnClient then DESTROYS the body. Every real teleport in this
            // game is at or beyond that radius — B8's middle back to a B1 cell is ~38 m —
            // so with the manager running there is no client-side body left to measure and
            // the loop below reads a destroyed component. Worse, it was not even stable:
            // conn.identity is the client's own player, NetLocalRunner re-binds it to the
            // fixture rig 500 m out at a moment this test does not control, so the same
            // jump was in range on one run and out of it on the next.
            //
            // Nothing about the guard depends on the manager: NetRunnerAnimation reads
            // NetPlayer.NetworkedPosition and never asks who can see it. Turning the
            // manager off removes a variable this test does not own; the interest rule is
            // somebody else's test. Restored in TearDown by NetworkManager.ResetStatics.
            NetworkServer.aoi = null;

            var ground = hostPlayer!.NetworkedPosition;

            // Let it settle and seed its differentiator against the position it was parked
            // at. A runner standing still is Idle, which is also what it must still be
            // after the jump — so the teleport counter below is what tells the two apart.
            yield return WaitFrames(5);

            Assert.That(remote.State, Is.EqualTo(PlayerAnimationState.Idle),
                "A runner that has not moved since it spawned is playing " + remote.State + ".");

            var teleportsBefore = remote.TeleportsIgnored;
            var bytesBefore = _serverPayloadBytesSent;

            var observer = observerIdentity!.transform.position;
            var landing = ground + new Vector3(TeleportMetres, 0f, 0f);
            hostPlayer.TeleportTo(landing);

            // Watched every frame rather than sampled once at the end: the sprint this
            // guards against is a transient, and a single assertion after the fact would
            // miss a body that ran for a quarter of a second and then stopped — which is
            // long enough to see and long enough to disbelieve everything else on screen.
            var sawRun = false;
            var fastest = 0f;
            var deadline = Time.realtimeSinceStartup + ConnectSecondsBudget;

            while (Time.realtimeSinceStartup < deadline)
            {
                // Checked every frame rather than trusted: a jump past
                // NetInterestScope.PerceptionRange makes HorrorInterestManagement despawn
                // the runner, NetRunner.UnspawnOnClient destroys the body, and every read
                // below then throws MissingReferenceException from inside the loop — which
                // reports as a crashed test rather than as the thing that happened. See
                // TeleportMetres, which is derived from that radius so this cannot fire.
                Assert.That(remote != null, Is.True,
                    "The client's copy was destroyed mid-teleport, so there is no body left to measure. This is "
                    + "usually interest management working: HorrorInterestManagement drops any identity further "
                    + "than NetInterestScope.PerceptionRange (" + NetInterestScope.PerceptionRange.ToString("0.0")
                    + " m) from conn.identity, and NetRunner.UnspawnOnClient then destroys the body. "
                    + "observer=" + observer.ToString("0.0")
                    + " observerNow=" + (observerIdentity == null
                        ? "(destroyed)" : observerIdentity.transform.position.ToString("0.0"))
                    + " landing=" + landing.ToString("0.0")
                    + " runnerNow=" + (hostRunner == null
                        ? "(destroyed)" : hostRunner.transform.position.ToString("0.0"))
                    + " jump=" + TeleportMetres.ToString("0.0") + " m"
                    + " observerToLanding=" + Vector3.Distance(observer, landing).ToString("0.0") + " m");

                sawRun |= remote!.State == PlayerAnimationState.Run;
                fastest = Mathf.Max(fastest, remote.GroundSpeed);

                if (remote.TeleportsIgnored > teleportsBefore
                    && Vector3.Distance(remote.transform.position, landing) < 1f)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(_serverPayloadBytesSent, Is.GreaterThan(bytesBefore),
                "KcpTransport.ServerSend never ran after the teleport, so the new position was never handed to "
                + "a socket and this test watched a body that was told nothing.");

            Assert.That(remote.TeleportsIgnored, Is.GreaterThan(teleportsBefore),
                "The body never saw a step it could not have run. Either the SyncVar did not arrive — in which "
                + "case 'it did not sprint' is true of a body that received nothing — or the guard's ceiling is "
                + "wrong. The ceiling is RunnerSprintSpeed × (measured interval + "
                + NetRunnerAnimation.TeleportSlackSeconds.ToString("0.000") + " s), which at one send interval "
                + "is " + (GameConstants.RunnerSprintSpeed
                           * (NetRunnerAnimation.MinimumSampleSeconds + NetRunnerAnimation.TeleportSlackSeconds))
                    .ToString("0.00")
                + " m, against a " + TeleportMetres + " m jump.");

            Assert.That(sawRun, Is.False,
                "The body played a Run cycle while it was being teleported " + TeleportMetres
                + " m. Over one send interval that is " + (TeleportMetres * GameConstants.NetworkSendRate)
                + " m/s — " + (TeleportMetres * GameConstants.NetworkSendRate / GameConstants.RunnerSprintSpeed)
                    .ToString("0")
                + " times §06's sprint — so a runner dropped down a 투하구 or sent back to B1 sprints across the "
                + "map in everybody else's view.");

            Assert.That(fastest, Is.LessThan(RunThreshold),
                "The reconstructed speed peaked at " + fastest.ToString("0.00")
                + " m/s during the teleport, over the 달리기 crossover at " + RunThreshold.ToString("0.00")
                + ". It did not last long enough to change the pose this run, which makes it a defect that will "
                + "show on somebody else's frame timing rather than one that does not exist.");

            Assert.That(remote.State, Is.EqualTo(PlayerAnimationState.Idle),
                "The runner is standing " + TeleportMetres + " m from where they were and their body is playing "
                + remote.State + ".");
        }

        // ------------------------------------------------------------------
        // rig
        // ------------------------------------------------------------------

        /// <summary>
        /// Asserts that the shipped build, not this fixture, wired the player layer to the
        /// network layer. <see cref="NetPlayerBridge.InstalledAtStartup"/> is set in a
        /// private <c>[RuntimeInitializeOnLoadMethod]</c> and nowhere else, so nothing in a
        /// test can make it true — and without it there is no
        /// <c>NetRunner.VisualFactory</c>, no body, and nothing to animate.
        /// </summary>
        private static void AssertTheBuildInstalledTheBridge()
        {
            Assert.That(NetPlayerBridge.InstalledAtStartup, Is.True,
                "HorrorGame.Net.PlayerBridge never installed itself. Only Unity's own "
                + "[RuntimeInitializeOnLoadMethod] can set this, so a false here means the assembly is not in "
                + "the build — and then remote runners have no body at all, let alone an animated one.");
        }

        /// <summary>
        /// Loads the descent the game actually ships, and lets its own <c>Start</c> lay the
        /// match out.
        /// <para>
        /// <c>LogAssert.ignoreFailingMessages</c> goes on for the rest of the test: the
        /// solo scene brings up a whole match — NavMesh, monster, audio — and this
        /// fixture's subject is none of those. Every assertion here is on measured state
        /// rather than on a log line.
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
        /// Switches the match off and puts the shipped rig on a floor of the fixture's own,
        /// well outside the building.
        /// <para>
        /// The rig itself is untouched — same model, same <see cref="PlayerMotor"/>, same
        /// <c>PlayerLook</c>, same <c>PlayerAnimatorDriver</c>, and it is still the rig
        /// <c>NetRunnerBody</c> copies and <c>PlayerRigNetView</c> reports. Only the ground
        /// under it belongs to the fixture, and that is the point: a walk that clips a
        /// corridor wall measures the corridor. §01's cells are 2.5 m and the phases below
        /// cover 11 m.
        /// </para>
        /// <para>
        /// The <c>CharacterController</c> is switched off across the move for the same
        /// reason <c>MatchDirector.CheckChutes</c> does it — a controller resolves a
        /// teleport as a sweep and would refuse to cross the building's walls on the way.
        /// </para>
        /// </summary>
        private PlayerMotor TakeThePlayerOutOfTheMatch()
        {
            var disabled = 0;
            var behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (var i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null
                    && string.Equals(behaviours[i].GetType().Name, MatchDirectorTypeName, StringComparison.Ordinal))
                {
                    behaviours[i].enabled = false;
                    disabled++;
                }
            }

            Assert.That(disabled, Is.GreaterThan(0),
                "The descent scene has no " + MatchDirectorTypeName + " to switch off. It is found by type name "
                + "because this assembly does not reference HorrorGame.Gameplay, so a rename lands here — and "
                + "left running, its OutOfBounds guard teleports the player back into the building mid-walk.");

            var motor = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
            Assert.That(motor, Is.Not.Null,
                "The descent scene has no player rig, so there is no model for a remote body to be copied from "
                + "and no legs to walk.");

            var visual = motor!.transform.Find(NetRunnerBody.RigVisualChildName);
            Assert.That(visual, Is.Not.Null,
                "The shipped rig has no '" + NetRunnerBody.RigVisualChildName + "' child — the name "
                + "PlayerFeelHarnessMenu.BuildRig gives the model and the thing NetRunnerBody copies.");

            _floor = new GameObject("NetRunnerAnimationTests_Floor");
            _floor!.transform.position = new Vector3(TestGroundMetres, -0.5f, TestGroundMetres);

            var ground = _floor!.AddComponent<BoxCollider>();

            // A collider with no renderer: GameObject.CreatePrimitive would pull in
            // RenderPipelineAsset.defaultMaterial, which is the magenta scar NetRunner
            // documents, and nothing here is drawn. Wide enough that the three phases
            // below (11 m) cannot reach an edge by two orders of magnitude.
            ground.size = new Vector3(2000f, 1f, 2000f);

            var controller = motor.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            motor.transform.position = new Vector3(TestGroundMetres, 0.1f, TestGroundMetres);

            if (controller != null)
            {
                controller.enabled = true;
            }

            return motor;
        }

        /// <summary>
        /// Steps §05's own motor for a stretch of wall clock, one step per frame.
        /// <para>
        /// <c>SteppedExternally</c> is the seam <see cref="PlayerMotor"/> documents for
        /// exactly this: §13 allows one authority and one step, so the fixture driving the
        /// legs has to be the only thing driving them.
        /// </para>
        /// </summary>
        private static IEnumerator Drive(PlayerMotor motor, MoveInput input, float seconds, Action? perFrame)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                motor.Step(input, Time.deltaTime);

                if (perFrame != null)
                {
                    perFrame();
                }

                yield return null;
            }
        }

        /// <summary>
        /// Brings up the shipped manager as a server and then, in the same process, as a
        /// real remote client. <c>StartServer</c> + <c>StartClient</c> and never
        /// <c>StartHost</c>: the latter gives the manager a
        /// <c>LocalConnectionToServer</c> and never opens a socket.
        /// </summary>
        private IEnumerator StartServerAndClient(ushort port)
        {
            _rig = new GameObject("NetRunnerAnimationRig");
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
                "Another NetworkManager owns the singleton, so this fixture's transport and address are not the "
                + "ones the session will run on.");

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

            Assert.That(FirstConnection().address, Does.Contain(Loopback),
                "The server's idea of where this client is comes from KcpTransport.ServerGetClientAddress, "
                + "which reads the IPEndPoint a datagram actually arrived from. A fabricated connection has no "
                + "endpoint to report. Got: '" + FirstConnection().address + "'.");
        }

        /// <summary>
        /// Every bone every skinned renderer on a body is deformed by. The artefact the
        /// animation claim is measured on — a pose lives in these transforms and nowhere
        /// else.
        /// </summary>
        private static Transform[] BonesOf(GameObject body)
        {
            var found = new List<Transform>();
            var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            for (var i = 0; i < skins.Length; i++)
            {
                var bones = skins[i].bones;
                for (var j = 0; j < bones.Length; j++)
                {
                    if (bones[j] != null && !found.Contains(bones[j]))
                    {
                        found.Add(bones[j]);
                    }
                }
            }

            return found.ToArray();
        }

        private static Quaternion[] Pose(Transform[] bones)
        {
            var pose = new Quaternion[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                pose[i] = bones[i].localRotation;
            }

            return pose;
        }

        /// <summary>
        /// The largest angle any bone has turned since <paramref name="from"/> was taken,
        /// degrees. The maximum and not the mean: a gait moves the legs and leaves the
        /// spine nearly still, so an average over thirteen bones would understate a
        /// perfectly good walk cycle.
        /// </summary>
        private static float MaxDegreesFrom(Quaternion[] from, Transform[] bones)
        {
            var worst = 0f;
            for (var i = 0; i < bones.Length && i < from.Length; i++)
            {
                worst = Mathf.Max(worst, Quaternion.Angle(from[i], bones[i].localRotation));
            }

            return worst;
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

        private static IEnumerator WaitFrames(int frames)
        {
            for (var i = 0; i < frames; i++)
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
