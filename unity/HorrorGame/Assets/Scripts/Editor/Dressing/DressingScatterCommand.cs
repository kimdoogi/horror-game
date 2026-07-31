#nullable enable

using System;
using System.Globalization;
using HorrorGame.EditorTools.SceneGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HorrorGame.EditorTools.Dressing
{
    /// <summary>
    /// Menu and batch entry points for the dressing pass.
    /// <para>
    /// Kept separate from <see cref="DressingScatter"/> so the algorithm has no
    /// opinion about dialogs, saving, or exit codes — the same split
    /// <see cref="MapSceneGenerator"/> makes, and for the same reason: CI runs the
    /// batch path and a designer runs the menu path, and they must not be able to
    /// drift into producing different scenes.
    /// </para>
    /// </summary>
    public static class DressingScatterCommand
    {
        private const string SeedArgument = "-dressSeed";
        private const string SceneArgument = "-dressScene";

        /// <summary>Scatters into the open scene at the default seed and saves it.</summary>
        [MenuItem("HorrorGame/Dressing/Scatter Into Open Scene", priority = 40)]
        public static void ScatterMenu()
        {
            if (!ScatterAndSave(DressingScatter.DefaultSeed, out var message))
            {
                EditorUtility.DisplayDialog("Dressing scatter failed", message, "OK");
                Debug.LogError("[Dressing] " + message);
                return;
            }

            Debug.Log("[Dressing]\n" + message);
        }

        /// <summary>Scatters at a chosen seed, so a layout somebody disliked can be re-rolled or reproduced.</summary>
        [MenuItem("HorrorGame/Dressing/Scatter (choose seed)…", priority = 41)]
        public static void ScatterWithSeedMenu()
        {
            var choice = EditorUtility.DisplayDialogComplex(
                "Seed",
                "Scatter the dressing kit from which seed?\n\nThe seed fixes the whole layout: the same seed always "
                + "produces the same dressing, which is how a corridor somebody says looks wrong gets reproduced here.",
                "Default (" + DressingScatter.DefaultSeed + ")",
                "Cancel",
                "Random");

            if (choice == 1)
            {
                return;
            }

            var seed = choice == 0 ? DressingScatter.DefaultSeed : Environment.TickCount;
            if (!ScatterAndSave(seed, out var message))
            {
                EditorUtility.DisplayDialog("Dressing scatter failed", message, "OK");
                Debug.LogError("[Dressing] " + message);
                return;
            }

            Debug.Log("[Dressing]\n" + message);
        }

        /// <summary>Strips the dressing back out, leaving the generated map untouched.</summary>
        [MenuItem("HorrorGame/Dressing/Remove Dressing", priority = 42)]
        public static void RemoveMenu()
        {
            var mapRoot = GameObject.Find(MapSceneBuilder.MapRootName);
            if (mapRoot == null)
            {
                return;
            }

            DressingScatter.Remove(mapRoot);
            EditorSceneManager.MarkSceneDirty(mapRoot.scene);
            EditorSceneManager.SaveScene(mapRoot.scene);
            Debug.Log("[Dressing] Removed.");
        }

        /// <summary>
        /// Batch entry point.
        /// <code>
        /// Unity -batchmode -quit -projectPath . \
        ///   -executeMethod HorrorGame.EditorTools.Dressing.DressingScatterCommand.ScatterFromCommandLine \
        ///   -dressScene Assets/Scenes/Map_FirstSketch.unity -dressSeed 4703
        /// </code>
        /// Exits non-zero when a §08 or §12 constraint could not be met, so a build job
        /// notices a scatter that sealed a route rather than shipping it.
        /// </summary>
        public static void ScatterFromCommandLine()
        {
            try
            {
                var scenePath = Argument(SceneArgument) ?? SceneGenPaths.MapScene;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                var seed = DressingScatter.DefaultSeed;
                var raw = Argument(SeedArgument);
                if (raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    seed = parsed;
                }

                if (!ScatterAndSave(seed, out var message))
                {
                    Debug.LogError("[Dressing] " + message);
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log("[Dressing]\n" + message);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Dressing] " + ex);
                EditorApplication.Exit(1);
            }
        }

        private static bool ScatterAndSave(int seed, out string message)
        {
            var ok = DressingScatter.Run(seed, out message);
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);

            // The scene is saved either way. A failed run leaves a report naming the
            // constraint it broke, and looking at the offending corridor is how anybody
            // fixes it — a rollback would delete the evidence.
            if (!EditorSceneManager.SaveScene(scene))
            {
                message += "\nThe scatter ran but the scene could not be saved.";
                return false;
            }

            AssetDatabase.SaveAssets();
            return ok;
        }

        private static string? Argument(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
