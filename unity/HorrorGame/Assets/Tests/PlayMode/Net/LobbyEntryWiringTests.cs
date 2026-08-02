#nullable enable

using System;
using System.Collections;
using System.IO;
using HorrorGame.UI;
using HorrorGame.UI.Settings;
using HorrorGame.UI.Shell;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Net
{
    /// <summary>
    /// That 시작 asks §11's lobby before it loads a scene.
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
    /// <b>What it deliberately does not do.</b> It does not let the fall-through run.
    /// Proving the other branch means letting <c>StartMatch</c> load the match scene,
    /// which <c>UiFlowTests.Menu_ComesUp_AndStartReachesTheMatchScene</c> already does
    /// end to end; repeating a 90-second scene load here to re-prove it would buy
    /// nothing. What is untested anywhere else, and what this covers, is the branch that
    /// did not exist until the seam was called.
    /// </para>
    /// </summary>
    public sealed class LobbyEntryWiringTests
    {
        /// <summary>Scene 0 of the build — the one holding <c>GameShell</c>.</summary>
        private const string MenuScene = "Bootstrap";

        [SetUp]
        public void SetUp()
        {
            // Never the player's own settings file: bringing the shell up initialises
            // the settings service, which writes.
            SettingsStore.OverrideDirectory(Path.Combine(Path.GetTempPath(), "HorrorGameLobbySeamTest"));

            // RaceLobby installs the real hook from a RuntimeInitializeOnLoadMethod, and
            // it would open an actual lobby over the test. Cleared so this fixture is
            // measuring the shell's question rather than the gameplay layer's answer.
            LobbyEntry.ResetForTests();
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
        /// Puts the world back — the shell, the EventSystem <em>and the scene</em>.
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
        /// </summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LobbyEntry.ResetForTests();

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

            var menu = SceneManager.GetSceneByName(MenuScene);
            if (menu.IsValid() && menu.isLoaded)
            {
                var empty = SceneManager.CreateScene("LobbyEntryWiringTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(menu);
            }
        }
    }
}
