#nullable enable

using System.Collections;
using HorrorGame.UI.Settings;
using HorrorGame.UI.Shell;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.UI
{
    /// <summary>
    /// The one thing about the shell that cannot be checked without running it: that
    /// 시작 actually arrives in a match.
    /// <para>
    /// Everything else the menu does is pinned in EditMode — the settings model, the
    /// clamps, the file, the rebind delivery — because none of it needs a frame. This
    /// does. The failure it exists to catch has no compile error and no log line: a
    /// coroutine that stalls on <c>allowSceneActivation</c>, a scene missing from Build
    /// Settings, a shell destroyed by its own singleton guard. All three leave a menu
    /// whose button does nothing, which is indistinguishable from a hang.
    /// </para>
    /// <para>
    /// The teardown is not politeness. <c>GameShell</c> is <c>DontDestroyOnLoad</c> by
    /// design — it has to outlive the scene change it performs — so a test that left one
    /// standing would put a full-screen canvas and an <c>EventSystem</c> into every
    /// PlayMode test that ran after it.
    /// </para>
    /// </summary>
    public sealed class UiFlowTests
    {
        private const string MenuScene = "Bootstrap";
        private const string MatchScene = "Map_FirstSketch_Solo";

        /// <summary>
        /// Wall-clock seconds to wait before calling a load stuck.
        /// <para>
        /// Seconds and not frames. Under <c>-batchmode -nographics</c> the player loop
        /// is not waiting on a display, so a few thousand frames go by in well under the
        /// half-second the map scene spends deserialising — a frame budget large enough
        /// to look generous timed out before the scene had finished loading, and the
        /// resulting failure looked exactly like the hang this test is for.
        /// </para>
        /// </summary>
        private const float LoadSecondsBudget = 90f;

        [UnityTest]
        public IEnumerator Menu_ComesUp_AndStartReachesTheMatchScene()
        {
            // Never the player's own settings file: this test presses buttons that save.
            SettingsStore.OverrideDirectory(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HorrorGameFlowTest"));

            yield return SceneManager.LoadSceneAsync(MenuScene, LoadSceneMode.Single);
            yield return null;

            var shell = GameShell.Instance;
            Assert.That(shell, Is.Not.Null,
                "The bootstrap scene attaches GameShell by name from the Editor layer, because the scene generator "
                + "deliberately does not reference the interface. A rename on either side compiles cleanly and leaves a "
                + "scene with no menu in it — this is the assertion that notices.");

            Assert.That(shell!.State, Is.EqualTo(GameShell.ShellState.Menu));
            Assert.That(Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length, Is.GreaterThan(0),
                "The menu builds its own hierarchy in code; if nothing built, the first thing a player sees is the "
                + "corridor with no words on it.");

            try
            {
                shell.StartMatch();

                var deadline = Time.realtimeSinceStartup + LoadSecondsBudget;
                while (shell.State != GameShell.ShellState.Match && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(shell.State, Is.EqualTo(GameShell.ShellState.Match),
                    "시작 did not arrive in a match within " + LoadSecondsBudget + " s. Either the scene is missing "
                    + "from Build Settings — LoadSceneAsync returns null and the shell bounces back to the menu — or the "
                    + "loading coroutine never released allowSceneActivation.");

                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(MatchScene),
                    "§14 step 1 is 'descend and come back up'. The shell's whole job is to put the player in the scene "
                    + "that can do that — the raw map has no player, no monster and no MatchDirector in it.");
            }
            finally
            {
                SettingsStore.OverrideDirectory(null);
            }
        }

        [TearDown]
        public void RemoveTheShell()
        {
            var shell = GameShell.Instance;
            if (shell != null)
            {
                Object.DestroyImmediate(shell.gameObject);
            }

            var events = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (events != null)
            {
                Object.DestroyImmediate(events.gameObject);
            }

            MatchPause.Clear();
            SettingsStore.OverrideDirectory(null);
        }
    }
}
