#nullable enable

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Playtest
{
    /// <summary>
    /// Prepares a standalone player that boots straight into a playable match.
    /// <para>
    /// Left alone, <c>EditorBuildSettings</c> ships <c>Bootstrap</c> and
    /// <c>Map_FirstSketch</c>. Neither is playable: the bootstrap menu's buttons are
    /// not yet wired to anything, and the generated map is geometry, lights and spawn
    /// markers with no player, no monster and no <c>MatchDirector</c> — because in a
    /// real session §13's host spawns those over the network. A build made from that
    /// list opens on a menu that does nothing, or drops the player into an empty
    /// building.
    /// </para>
    /// <para>
    /// The playable scene is <c>Map_FirstSketch_Solo</c>, assembled by
    /// <see cref="SoloPlaytest"/>. This puts it first so a double-click starts a
    /// match, which is what someone testing the game actually wants.
    /// </para>
    /// <para>
    /// When the lobby is wired up, Bootstrap becomes the right first scene and this
    /// becomes a playtest-only convenience. It deliberately does not delete the other
    /// scenes from the list — they are still needed in the player.
    /// </para>
    /// </summary>
    public static class StandaloneBuild
    {
        private const string SoloScene = "Assets/Scenes/Map_FirstSketch_Solo.unity";

        /// <summary>Rebuilds the solo scene and makes it the player's first scene.</summary>
        [MenuItem("Horror/Build/Prepare Playable Build (solo first)", priority = 10)]
        public static void Prepare()
        {
            if (!File.Exists(SoloScene) && !SoloPlaytest.BuildScene())
            {
                Debug.LogError(
                    "[StandaloneBuild] The solo scene does not exist and could not be built. "
                    + "Run HorrorGame ▸ Play ▸ ▶ START PLAYTEST once first.");
                return;
            }

            var existing = EditorBuildSettings.scenes.ToList();
            var solo = existing.FirstOrDefault(s =>
                string.Equals(s.path, SoloScene, StringComparison.Ordinal));

            if (solo != null)
            {
                existing.Remove(solo);
            }
            else
            {
                solo = new EditorBuildSettingsScene(SoloScene, enabled: true);
            }

            solo.enabled = true;
            existing.Insert(0, solo);
            EditorBuildSettings.scenes = existing.ToArray();

            Debug.Log("[StandaloneBuild] Build scenes, in load order:\n  "
                + string.Join("\n  ", EditorBuildSettings.scenes.Select(
                    s => (s.enabled ? "[x] " : "[ ] ") + s.path)));
        }

        /// <summary>
        /// Batch entry point: prepare, then hand over to the normal build pipeline.
        /// <code>
        /// Unity -batchmode -quit -projectPath . -executeMethod HorrorGame.EditorTools.Playtest.StandaloneBuild.PrepareBatch
        /// </code>
        /// </summary>
        public static void PrepareBatch()
        {
            try
            {
                Prepare();

                var first = EditorBuildSettings.scenes.FirstOrDefault();
                var ok = first != null && first.enabled
                         && string.Equals(first.path, SoloScene, StringComparison.Ordinal);

                if (!ok)
                {
                    Debug.LogError("[StandaloneBuild] The solo scene is not first in the build list.");
                }

                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[StandaloneBuild] " + ex);
                EditorApplication.Exit(2);
            }
        }
    }
}
