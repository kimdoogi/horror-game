#nullable enable

using System;
using System.Collections;
using System.IO;
using HorrorGame.Core;
using HorrorGame.Net;
using HorrorGame.UI;
using HorrorGame.UI.Settings;
using HorrorGame.UI.Shell;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace HorrorGame.Tests.PlayMode.Net
{
    /// <summary>
    /// That 시작 asks §11's lobby before it loads a scene, that something is actually
    /// listening when it asks, and that pressing 호스트 and then 출발 arrives in the
    /// maze with the runners' bodies still in existence.
    /// <para>
    /// <b>Why this is a networking test.</b> <c>LobbyEntry</c>, <c>LobbyScreen</c> and
    /// <c>RaceLobby</c> were all written, all committed and all correct, and
    /// <c>LobbyEntry.TryOpen</c> had zero callers — so the only button in the shipped
    /// build still went straight to a single-player scene load and the entire lobby was
    /// unreachable code. That is the same failure as the map generated into a scene the
    /// game does not load: invisible in source, obvious in the artefact. This fixture is
    /// the assertion that notices it coming back.
    /// </para>
    /// <para>
    /// <b>The fixture cannot name <c>RaceLobby</c>, and that is the architecture rather
    /// than an omission.</b> <c>RaceLobby</c> references <c>MatchDirector</c>, which has
    /// no <c>asmdef</c>, so it lives in <c>Assembly-CSharp</c> — and no assembly
    /// definition may reference the default assembly. Every assertion below therefore
    /// goes through something a build could also go through: <c>LobbyEntry</c> and
    /// <c>LobbyScreen</c> in <c>HorrorGame.UI</c>, and <c>NetworkServer</c> /
    /// <c>NetworkClient</c> in Mirror. That constraint is exactly why the seam exists;
    /// see <c>GameShell</c>'s remarks.
    /// </para>
    /// </summary>
    public sealed class LobbyEntryWiringTests
    {
        /// <summary>Scene 0 of the build — the one holding <c>GameShell</c>.</summary>
        private const string MenuScene = "Bootstrap";

        /// <summary>Where 시작 ends up. Named here so the teardown can unload it.</summary>
        private const string MatchScene = "Map_FirstSketch_Solo";

        /// <summary>
        /// Wall-clock seconds allowed for the descent's scene load.
        /// <para>
        /// Same budget and same reasoning as <c>UiFlowTests.LoadSecondsBudget</c>: under
        /// <c>-batchmode -nographics</c> thousands of frames pass while the eight-storey
        /// scene is still deserialising, so a frame count generous enough to look safe
        /// times out before the load finishes.
        /// </para>
        /// </summary>
        private const float LoadSecondsBudget = 120f;

        /// <summary>Seconds allowed for a host to come up and hand itself a body. Two message hops in host mode.</summary>
        private const float SessionSecondsBudget = 10f;

        /// <summary>
        /// Connection id for the second runner. Any value but 0, which is the host's own.
        /// <para>
        /// A hand-made <c>NetworkConnectionToClient</c> and not a second socket, because
        /// Mirror allows exactly one <c>NetworkClient</c> per process and host mode has
        /// already taken it — a genuinely remote peer needs a second process, which
        /// <c>NetSocketTests</c> gets as close to as one process can. What is needed here
        /// is only that §11's floor of two is met so the host may press 출발;
        /// <c>NetTests</c> builds its connections the same way.
        /// </para>
        /// </summary>
        private const int SecondRunnerConnectionId = 42;

        /// <summary>
        /// The hook the gameplay layer installed at start-up, captured before anything
        /// clears it.
        /// <para>
        /// <c>[OneTimeSetUp]</c> and a static, because <c>RaceLobby.Install</c> is a
        /// <c>[RuntimeInitializeOnLoadMethod]</c> that runs once per play-mode session and
        /// this fixture's own <c>[SetUp]</c> is the only thing in the suite that nulls the
        /// result. Capturing it here is the only place it can be read; the teardown puts
        /// it back so the rest of the run is not left with a dead seam.
        /// </para>
        /// </summary>
        private static Func<Action, bool>? _installedHook;

        [OneTimeSetUp]
        public void CaptureTheInstalledHook()
        {
            _installedHook = LobbyEntry.Intercept;
        }

        [SetUp]
        public void SetUp()
        {
            // Never the player's own settings file: bringing the shell up initialises
            // the settings service, which writes.
            SettingsStore.OverrideDirectory(Path.Combine(Path.GetTempPath(), "HorrorGameLobbySeamTest"));

            // RaceLobby installs the real hook from a RuntimeInitializeOnLoadMethod, and
            // it would open an actual lobby over the test. Cleared so this fixture is
            // measuring the shell's question rather than the gameplay layer's answer —
            // except in the one test below that deliberately puts it back.
            LobbyEntry.ResetForTests();

            // Mirror's Utils.IsHeadless() is true under -batchmode, and NetworkManager
            // answers that by pinning Application.targetFrameRate to its send rate — a
            // PROCESS-WIDE setting that outlives the fixture that caused it. All three
            // sibling fixtures in this folder capture and restore it and say so in their
            // own remarks; this one starts a real host through RaceLobby and must do the
            // same. It matters more here than there because this fixture sorts FIRST in
            // the Net namespace, so a value left pinned is the value NetHumanRunnerTests,
            // NetRunnerTests and NetSocketTests each capture as "the original" and
            // faithfully restore — and the whole suite after them runs at a frame rate
            // this fixture chose.
            _restoreTargetFrameRate = Application.targetFrameRate;
        }

        /// <summary>What <c>Application.targetFrameRate</c> was before Mirror pinned it. See <see cref="SetUp"/>.</summary>
        private int _restoreTargetFrameRate;

        /// <summary>
        /// Something answered. §11's lobby installs itself into <see cref="LobbyEntry"/>
        /// at start-up, so a shipped build's 시작 has somewhere to go.
        /// <para>
        /// <b>Why this is worth its own test.</b> The seam is one-way by necessity and
        /// therefore silent by construction: <c>GameShell</c> asks a question it does not
        /// know the answer to, and a build in which nobody installed an answer behaves
        /// exactly like a build with no lobby in it — 시작 descends alone, no error, no
        /// warning, no compile failure. The next test proves the shell asks; this one
        /// proves there is a listener, and the two together are the claim.
        /// </para>
        /// </summary>
        [Test]
        public void TheGameplayLayerArmsTheSeamAtStartUp()
        {
            Assert.That(_installedHook, Is.Not.Null,
                "Nothing installed itself into LobbyEntry.Intercept. RaceLobby.Install is a "
                + "[RuntimeInitializeOnLoadMethod(AfterSceneLoad)] and it is the only thing in the project that arms "
                + "this seam, so a null here means 시작 falls straight through to a single-player scene load and the "
                + "twenty-player race has no way in — which is the exact state this fixture was written for.");
        }

        /// <summary>
        /// The shell hands 시작 to whatever installed itself in <see cref="LobbyEntry"/>,
        /// and stays out of the match scene while the lobby has the flow.
        /// </summary>
        [UnityTest]
        public IEnumerator StartAsksTheLobbyBeforeItLoadsAnything()
        {
            yield return SceneManager.LoadSceneAsync(MenuScene, LoadSceneMode.Single);
            yield return null;

            var shell = GameShell.Instance;
            Assert.That(shell, Is.Not.Null, "The bootstrap scene did not bring a GameShell up.");
            Assert.That(shell!.State, Is.EqualTo(GameShell.ShellState.Menu));

            var asked = 0;
            Action? handedBack = null;

            LobbyEntry.Intercept = onBegin =>
            {
                asked++;
                handedBack = onBegin;
                return true;
            };

            shell.BeginFromMenu();
            yield return null;

            Assert.That(asked, Is.EqualTo(1),
                "GameShell.BeginFromMenu did not call LobbyEntry.TryOpen. This is exactly the state the project "
                + "shipped in: a lobby that hosts, joins and seats twenty runners, wired to nothing, because the one "
                + "button that could reach it loaded a solo scene instead.");

            Assert.That(shell.State, Is.Not.EqualTo(GameShell.ShellState.Loading),
                "The shell started loading the match scene even though the lobby said it had taken the flow. Nobody "
                + "would ever see the lobby.");

            Assert.That(shell.State, Is.Not.EqualTo(GameShell.ShellState.Match),
                "The shell arrived in a match with the lobby holding the flow.");

            Assert.That(handedBack, Is.Not.Null,
                "The lobby was given no way to hand the flow back, so agreeing to start would strand every runner in "
                + "the lobby.");

            // The callback the lobby is holding has to be the one that consumes
            // LobbyEntry's latch on the way past — i.e. the shell's own entry point, not
            // a naked StartMatch. If it were StartMatch, PassNextThrough would leave the
            // latch armed and the *next* 시작 after a race would skip the lobby silently.
            Assert.That(handedBack!.Method.Name, Is.EqualTo(nameof(GameShell.BeginFromMenu)),
                "The lobby's 'now descend' callback is " + handedBack.Method.Name + ", not "
                + nameof(GameShell.BeginFromMenu) + ". LobbyEntry.PassNextThrough only makes sense if the callback "
                + "comes back through TryOpen — see the latch in LobbyEntry.");
        }

        /// <summary>
        /// The whole path a human walks: 시작 → a lobby with a 호스트 button on it → a
        /// real Mirror session at §11's ceiling → 출발 → the maze, with the runner's body
        /// still in existence and standing on the rim of B1.
        /// <para>
        /// <b>The last two assertions are the ones that were failing silently.</b> The
        /// descent is entered through <c>SceneManager.LoadSceneAsync(..., Single)</c>,
        /// which destroys every object in the lobby's scene — including the runners
        /// <c>HorrorGameNetworkManager.OnServerReady</c> spawned, whose
        /// <c>NetworkIdentity.OnDestroy</c> then calls <c>NetworkServer.Destroy</c> and
        /// despawns them for everybody. Nothing spawns them again, because a client sends
        /// <c>ReadyMessage</c> exactly once, at connect. Every existing test that has two
        /// peers in it never loads a scene, and every existing test that loads a scene has
        /// no peers, so the party arriving in one building with nobody's body left in it
        /// was invisible to the whole suite.
        /// </para>
        /// <para>
        /// <b>And the placement.</b> <c>NetRunner.Build</c> leaves a runner at the origin
        /// and <c>NetRaceStartPoints.PlaceSpawnedRunners</c> moves it onto B1's rim when
        /// the building appears — but that method skips a connection whose
        /// <c>identity</c> is null and reports only what it placed, so a party whose
        /// bodies did not survive the load places zero runners and says nothing. "On a
        /// spawn marker and not at the origin it was born at" is the smallest statement
        /// that distinguishes the two.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator HostingFromTheMenuReachesTheMazeWithARunnerStillAlive()
        {
            if (_installedHook == null)
            {
                Assert.Fail(
                    "There is no installed lobby hook to walk through — see "
                    + nameof(TheGameplayLayerArmsTheSeamAtStartUp) + ", which fails for the same reason.");
            }

            // The real one, not the stub the other test uses: this walks the build's path.
            LobbyEntry.Intercept = _installedHook;

            yield return SceneManager.LoadSceneAsync(MenuScene, LoadSceneMode.Single);
            yield return null;

            var shell = GameShell.Instance;
            Assert.That(shell, Is.Not.Null, "The bootstrap scene did not bring a GameShell up.");

            shell!.BeginFromMenu();
            yield return null;
            yield return null;

            var screen = UnityEngine.Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            Assert.That(screen, Is.Not.Null,
                "시작 did not put a LobbyScreen in the world. The seam was armed, so something between "
                + "LobbyEntry.TryOpen and RaceLobby.Open refused the flow.");

            var host = FindButton(screen!, "Host");
            var start = FindButton(screen!, "Start");
            Assert.That(host, Is.Not.Null, "The lobby has no 호스트 button; there is no way into a session by hand.");
            Assert.That(start, Is.Not.Null, "The lobby has no 출발 button; a host could never start the race.");
            Assert.That(host!.interactable, Is.True, "호스트 is dark on an offline lobby — nothing can be pressed.");

            host.onClick.Invoke();

            var deadline = Time.realtimeSinceStartup + SessionSecondsBudget;
            while (!NetworkServer.active && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(NetworkServer.active, Is.True,
                "호스트 did not produce a server. RaceLobby.RequestHost adds a HorrorGameNetworkManager at runtime and "
                + "calls StartHost; if the port is taken or the transport failed to bind, this is what it looks like.");

            Assert.That(NetworkManager.singleton, Is.Not.Null, "A server is up with no NetworkManager behind it.");
            Assert.That(NetworkManager.singleton.maxConnections, Is.EqualTo(GameConstants.RaceRunnersMax),
                "§11's field is " + GameConstants.RaceRunnersMax + " and NetworkServer.Listen was given a different "
                + "ceiling. The twenty-first runner is refused by the map, not by the socket — see "
                + "HorrorGameNetworkManager.Awake.");

            // The host is a player too, and its body is created on the ready message
            // rather than at connect, so it takes a frame or two of host-mode message
            // pumping to arrive.
            deadline = Time.realtimeSinceStartup + SessionSecondsBudget;
            while (LocalRunner() == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var runner = LocalRunner();
            Assert.That(runner, Is.Not.Null,
                "The host has no runner of its own. HorrorGameNetworkManager.OnServerReady builds one from "
                + "NetRunner.Build and hands it to AddPlayerForConnection; without it there is nobody in the race and "
                + "nothing for a peer to see.");

            var runnerNetId = runner!.netId;
            Assert.That(runnerNetId, Is.Not.EqualTo(0u), "The host's runner was never spawned.");

            // §11 refuses to start below two — "one runner is not a race" — so the field
            // has to reach the floor before 출발 means anything.
            NetworkServer.AddConnection(new NetworkConnectionToClient(SecondRunnerConnectionId));

            deadline = Time.realtimeSinceStartup + SessionSecondsBudget;
            while (!start!.interactable && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(start!.interactable, Is.True,
                "출발 stayed dark with " + NetworkServer.connections.Count + " connection(s) on the server. The lobby "
                + "polls NetworkServer.connections every RaceLobby.PollSeconds and lights the button at "
                + GameConstants.RaceRunnersMin + "; a second runner that never reaches the roster means nobody can "
                + "ever start a race.");

            start.onClick.Invoke();

            deadline = Time.realtimeSinceStartup + LoadSecondsBudget;
            while (shell.State != GameShell.ShellState.Match && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(shell.State, Is.EqualTo(GameShell.ShellState.Match),
                "출발 did not arrive in a match within " + LoadSecondsBudget + " s. Either RaceLobby.BeginDescent never "
                + "handed the flow back through LobbyEntry.PassNextThrough, or the scene load stalled.");

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(MatchScene),
                "The party agreed on a seed and then landed somewhere other than the descent scene.");

            yield return null;

            // ── The link that was broken ───────────────────────────────────────────
            var survivor = LocalRunner();
            Assert.That(survivor, Is.Not.Null,
                "The host's runner did not survive the descent's scene load. A LoadSceneMode.Single load destroys every "
                + "object in the old scene, and a destroyed NetworkIdentity on the server calls NetworkServer.Destroy — "
                + "so the whole party arrives in the same building with nobody's body in it, and nothing spawns them "
                + "again because ReadyMessage is sent once, at connect. RaceLobby.KeepBodiesAcrossTheLoad is what "
                + "carries them over.");

            Assert.That(survivor!.netId, Is.EqualTo(runnerNetId),
                "The runner in the maze is a different object from the one in the lobby — netId " + survivor.netId
                + " where the lobby had " + runnerNetId + ". Anything holding the old id is now pointing at nothing.");

            Assert.That(NetworkServer.spawned.ContainsKey(runnerNetId), Is.True,
                "The server no longer has the runner in NetworkServer.spawned, so no join and no observer rebuild will "
                + "ever mention it again.");

            Assert.That(survivor.gameObject.scene.name, Is.EqualTo("DontDestroyOnLoad"),
                "The runner is sitting in a normal scene, which means it survived this load by luck and will not "
                + "survive the next one.");

            // ── And it is standing on the rim, not at the origin it was built at ───
            var nearest = NearestPlayerSpawn(survivor.transform.position, out var distance);
            Assert.That(nearest, Is.Not.Null,
                "No PlayerSpawn_* markers in the descent scene — the map was not generated by the scene generator, and "
                + "no runner can be placed on the rim of B1.");

            Assert.That(survivor.transform.position, Is.Not.EqualTo(Vector3.zero),
                "The runner is still at the world origin, where NetRunner.Build created it. §01 starts the field on the "
                + "rim of B1; nobody moved it there.");

            Assert.That(distance, Is.LessThan(3f),
                "The runner is " + distance.ToString("0.0") + " m from the nearest PlayerSpawn marker ('"
                + nearest!.name + "'). NetRaceStartPoints.PlaceSpawnedRunners teleports each connection's runner onto "
                + "one of the ring's markers once the building exists; 3 m is one map cell plus the character's own "
                + "radius.");
        }

        /// <summary>The runner this machine owns, or null. Host mode included.</summary>
        private static NetworkIdentity? LocalRunner()
        {
            var connection = NetworkServer.localConnection;
            var identity = connection != null ? connection.identity : NetworkClient.localPlayer;
            if (identity == null)
            {
                return null;
            }

            return identity.TryGetComponent(out NetPlayer _) ? identity : null;
        }

        /// <summary>A named button anywhere under a screen. The names are <c>LobbyScreen</c>'s own.</summary>
        private static Button? FindButton(LobbyScreen screen, string name)
        {
            var buttons = screen.GetComponentsInChildren<Button>(includeInactive: true);
            for (var i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].name == name)
                {
                    return buttons[i];
                }
            }

            return null;
        }

        /// <summary>
        /// The closest <c>PlayerSpawn_*</c> marker to a point, measured horizontally.
        /// <para>
        /// Horizontally because the runner's height is settled by the character controller
        /// and the marker's is not, and the claim being made is about which cell of the rim
        /// somebody is standing in.
        /// </para>
        /// </summary>
        private static Transform? NearestPlayerSpawn(Vector3 point, out float distance)
        {
            Transform? best = null;
            distance = float.MaxValue;

            // The marker group first. The descent scene carries some 7,500 prefab
            // instances and a whole-scene Transform sweep is seconds of test time for a
            // list of 36 objects that has a parent.
            var group = GameObject.Find("PlayerSpawns");
            var all = group != null
                ? group.GetComponentsInChildren<Transform>(includeInactive: true)
                : UnityEngine.Object.FindObjectsByType<Transform>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (var i = 0; i < all.Length; i++)
            {
                if (!all[i].name.StartsWith("PlayerSpawn_", StringComparison.Ordinal))
                {
                    continue;
                }

                var flat = all[i].position - point;
                flat.y = 0f;

                var d = flat.magnitude;
                if (d < distance)
                {
                    distance = d;
                    best = all[i];
                }
            }

            return best;
        }

        /// <summary>
        /// Puts the world back — the session, the shell, the EventSystem <em>and the
        /// scenes</em>.
        /// <para>
        /// <b>The scene unload is the point, and it was missing.</b> This fixture used to
        /// destroy the two objects and leave <c>Bootstrap</c> loaded, and Bootstrap is not
        /// an empty menu: its <c>MenuBackdrop</c> is built out of real
        /// <c>Corridor_Straight_10m</c> kit pieces with their colliders, and hangs three
        /// <c>Practical</c> lamps in them. Alphabetically <c>Net</c> sorts between
        /// <c>Monster</c> and <c>PlayerRig</c>, so every PlayMode fixture after this one
        /// inherited that corridor and those lamps, and three of them were measuring it
        /// instead of themselves:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <c>PlayerTests.Movement_can_be_locked_without_locking_the_view</c> builds its rig
        /// at the world origin — inside the backdrop — and a <c>CharacterController</c>
        /// depenetrating out of a corridor wall travelled 0.904 m/s with movement locked.
        /// </description></item>
        /// <item><description>
        /// Both <c>PresenceSessionTests</c> darkness assertions read 0.166010931 instead of
        /// 0. That number is not approximate and it is worth writing down, because it is
        /// what identifies the culprit: the third Practical sits at world (-1.25, 2.9,
        /// -0.9) at intensity 0.45 over a 5.5 m range, which is 1.9963 m from
        /// <c>PresenceSubject.SamplePoint</c> at (0, 1.63, 0), and
        /// <c>QualityFrom</c>'s (1 - d/range)² × (intensity / 1.1) is 0.16601094. A §12
        /// practical was lighting the player §03 says is unlit.
        /// </description></item>
        /// </list>
        /// <para>
        /// All three pass in isolation and all three fail with byte-identical numbers the
        /// moment this fixture runs first, which is the whole diagnosis. It is now a
        /// <c>[UnityTearDown]</c> rather than a <c>[TearDown]</c> because
        /// <c>UnloadSceneAsync</c> needs frames, and the blank scene goes in first because
        /// Unity refuses to unload the last loaded scene. Same shape as
        /// <c>GhostSessionTests</c>, <c>MonsterKillTests</c> and three others.
        /// </para>
        /// <para>
        /// <b>The session teardown is the same argument one layer down.</b> A host left
        /// running holds a UDP port, keeps a <c>NetworkManager</c> singleton alive across
        /// every later fixture, and — since the runner bodies are now deliberately kept in
        /// <c>DontDestroyOnLoad</c> — leaves loose <c>NetworkIdentity</c> objects in every
        /// scene that follows. <c>StopHost</c> comes before the manager is destroyed so
        /// that the lobby still exists to hear <c>NetSession.Ended</c> and clear the
        /// party's statics; destroying it first would strand them until the next race
        /// inherited them.
        /// </para>
        /// </summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (NetworkServer.active || NetworkClient.active)
            {
                var manager = NetworkManager.singleton;
                if (manager != null)
                {
                    manager.StopHost();
                }
                else
                {
                    NetworkClient.Shutdown();
                    NetworkServer.Shutdown();
                }

                yield return null;
            }

            var leftover = NetworkManager.singleton;
            if (leftover != null)
            {
                UnityEngine.Object.DestroyImmediate(leftover.gameObject);
            }

            // Bodies that were carried into DontDestroyOnLoad on purpose, plus the body
            // template RaceRunners keeps there. Neither belongs to any scene, so nothing
            // else in this teardown can reach them.
            var identities = UnityEngine.Object.FindObjectsByType<NetworkIdentity>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < identities.Length; i++)
            {
                if (identities[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(identities[i].gameObject);
                }
            }

            // Whatever the gameplay layer installed is put back, so a fixture that runs
            // after this one sees the build's own wiring rather than the hole this one
            // punched in it.
            LobbyEntry.ResetForTests();
            LobbyEntry.Intercept = _installedHook;

            // GameShell is DontDestroyOnLoad by design, so a shell left standing would
            // put a full-screen canvas into every PlayMode test scheduled after this one
            // — the reason UiFlowTests tears its own down the same way.
            var shell = GameShell.Instance;
            if (shell != null)
            {
                UnityEngine.Object.DestroyImmediate(shell.gameObject);
            }

            var events = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (events != null)
            {
                UnityEngine.Object.DestroyImmediate(events.gameObject);
            }

            MatchPause.Clear();
            SettingsStore.OverrideDirectory(null);

            // Put the process-wide frame cap back — see SetUp for why this fixture in
            // particular must.
            Application.targetFrameRate = _restoreTargetFrameRate;

            // Blank scene first: Unity refuses to unload the last loaded scene, and after a
            // Single load one of these two is all there is.
            var blank = default(Scene);
            var names = new[] { MenuScene, MatchScene };

            for (var i = 0; i < names.Length; i++)
            {
                var scene = SceneManager.GetSceneByName(names[i]);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                if (!blank.IsValid())
                {
                    blank = SceneManager.CreateScene("LobbyEntryWiringTests_Empty");
                    SceneManager.SetActiveScene(blank);
                }

                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }
    }
}
