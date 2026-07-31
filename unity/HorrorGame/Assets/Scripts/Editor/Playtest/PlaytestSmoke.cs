#nullable enable

using System;
using HorrorGame.Gameplay.Guidance;
using HorrorGame.Gameplay.Match;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Playtest
{
    /// <summary>
    /// Presses Play and checks that the thing a tester sees actually came up. §14.
    /// <para>
    /// <b>Why this is separate from every other gate.</b> Nothing else in this project
    /// runs <c>Start</c> or <c>Update</c> on the guidance screen: the EditMode tests drive
    /// <c>MatchGuidance</c> directly, and <see cref="GuidanceShot"/> photographs the
    /// canvas by binding and repainting it by hand. Both would stay green if the
    /// component threw on the first frame of a real session — which is exactly the
    /// hand-over this project has already had go wrong twice. So this one enters Play
    /// mode for real, waits for the match to be running, and asks the screen whether it
    /// is up and what it is telling the player.
    /// </para>
    /// <para>
    /// Entering Play mode reloads the domain, which throws away every static field and
    /// every event subscription — so the wait is driven from <c>SessionState</c>, which
    /// survives it, and re-armed from <c>InitializeOnLoad</c> on the other side.
    /// </para>
    /// <code>
    /// Unity -batchmode -nographics -silent-crashes -projectPath . \
    ///   -executeMethod HorrorGame.EditorTools.Playtest.PlaytestSmoke.Batch
    /// </code>
    /// <para>No <c>-quit</c>: this exits from its own callback, like the test runner.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class PlaytestSmoke
    {
        private const string PendingKey = "HorrorGame.PlaytestSmoke.Pending";
        private const string FramesKey = "HorrorGame.PlaytestSmoke.Frames";

        /// <summary>Editor frames to let the match settle before asking. Start, BeginMatch and one FixedUpdate all have to have run.</summary>
        private const int SettleFrames = 90;

        /// <summary>Editor frames after which a session that never reached Play mode is a failure rather than a hang.</summary>
        private const int GiveUpFrames = 1800;

        static PlaytestSmoke()
        {
            // Batch only, and not as a tidiness measure: this class ends by calling
            // EditorApplication.Exit. A stale SessionState flag left by an interrupted run
            // would otherwise re-arm inside somebody's open editor and close it the next
            // time they pressed Play.
            if (Application.isBatchMode && SessionState.GetBool(PendingKey, false))
            {
                EditorApplication.update += Pump;
            }
        }

        /// <summary>
        /// Prepares the scene the way the menu item does, then plays it and inspects the
        /// running game. Exits non-zero if any of it refuses.
        /// </summary>
        public static void Batch()
        {
            try
            {
                if (!StartPlaytest.Prepare(out var step, out var report))
                {
                    Debug.LogError("[PlaytestSmoke] " + step + " 실패\n" + report);
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log("[PlaytestSmoke] prepared\n" + report);

                SessionState.SetBool(PendingKey, true);
                SessionState.SetInt(FramesKey, 0);
                EditorApplication.update += Pump;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error)
            {
                Debug.LogError("[PlaytestSmoke] " + error);
                EditorApplication.Exit(1);
            }
        }

        private static void Pump()
        {
            var frames = SessionState.GetInt(FramesKey, 0) + 1;
            SessionState.SetInt(FramesKey, frames);

            if (frames > GiveUpFrames)
            {
                Finish(false, "play mode never settled after " + GiveUpFrames + " editor frames");
                return;
            }

            if (!EditorApplication.isPlaying || frames < SettleFrames)
            {
                return;
            }

            Inspect();
        }

        private static void Inspect()
        {
            var director = UnityEngine.Object.FindFirstObjectByType<MatchDirector>();
            if (director == null)
            {
                Finish(false, "no MatchDirector in the running scene");
                return;
            }

            if (!director.IsRunning)
            {
                Finish(false, "the match never started — MatchDirector.BeginMatch refused on Start");
                return;
            }

            var screen = UnityEngine.Object.FindFirstObjectByType<PlaytestGuidanceScreen>();
            if (screen == null)
            {
                Finish(false, "no PlaytestGuidanceScreen in the running scene");
                return;
            }

            if (!screen.IsVisible)
            {
                Finish(false, "the guidance screen exists but never built its canvas");
                return;
            }

            var guidance = screen.Guidance;
            if (guidance == null)
            {
                Finish(false, "the guidance screen came up unbound, so it has nothing to read");
                return;
            }

            if (guidance.Phase != GuidancePhase.Descend)
            {
                Finish(false, "§01 opens on the surface before the first descent; the first frame said "
                    + guidance.Phase + " — \"" + guidance.Line + "\"");
                return;
            }

            if (guidance.Line.Length == 0)
            {
                Finish(false, "the objective line came up empty");
                return;
            }

            if (!screen.ControlsVisible)
            {
                Finish(false, "the controls card was not up in the opening seconds");
                return;
            }

            Finish(true,
                "Play mode reached §01's opening state.\n"
                + "  phase     " + guidance.Phase + "\n"
                + "  line      " + guidance.Line + "\n"
                + "  controls  up\n"
                + "  clues     " + guidance.CluesRead + "/" + guidance.CluesRequired
                + " (map has " + guidance.CluesOnMap + ")\n"
                + "  monster   " + (guidance.Monster != null ? guidance.Monster.State.ToString() : "missing"));
        }

        private static void Finish(bool passed, string message)
        {
            EditorApplication.update -= Pump;
            SessionState.SetBool(PendingKey, false);

            if (passed)
            {
                Debug.Log("[PlaytestSmoke] PASS — " + message);
            }
            else
            {
                Debug.LogError("[PlaytestSmoke] FAIL — " + message);
            }

            EditorApplication.isPlaying = false;
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
