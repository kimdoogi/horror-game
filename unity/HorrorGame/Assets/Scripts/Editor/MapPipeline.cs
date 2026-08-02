#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using HorrorGame.EditorTools.Dressing;
using HorrorGame.EditorTools.Rendering;
using HorrorGame.EditorTools.SceneGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Regenerates the map the whole way through, in the one order that produces a
    /// correct scene.
    /// <para>
    /// The three passes were written independently and each one saves the scene, so
    /// running them in the wrong order silently discards work rather than failing.
    /// Specifically: <see cref="MapSceneBuilder"/> writes a placeholder ambience and
    /// leaves Unity's default daytime skybox in <c>RenderSettings</c>, which means a
    /// map regenerated after the atmosphere pass renders under a bright procedural
    /// sky. That is not a subtle difference — the default sky is the brightest thing
    /// in the frame, and every smooth surface mirrors it, so the tile and metal
    /// floors come out glowing. Nothing errors; the map just stops looking like a
    /// night.
    /// </para>
    /// <para>
    /// So the order is fixed here rather than in a README: layout → dressing →
    /// atmosphere, atmosphere last because it is the only pass that knows what the
    /// air is supposed to look like.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -nographics -projectPath . \
    ///   -executeMethod HorrorGame.EditorTools.MapPipeline.RegenerateFromCommandLine \
    ///   [-mapSeed 20250731] [-dressSeed 4703] [-atmoTier 0]
    /// </code>
    /// </summary>
    public static class MapPipeline
    {
        /// <summary>Runs the whole chain at the default seeds and reports in the console.</summary>
        [MenuItem("HorrorGame/Scene Gen/Regenerate Map (layout → dressing → atmosphere)", priority = 10)]
        public static void RegenerateMenu()
        {
            if (!Regenerate(DescentMap.DefaultSeed, DressingScatter.DefaultSeed, 0, out var report))
            {
                EditorUtility.DisplayDialog("Map regeneration failed", report, "OK");
                Debug.LogError("[MapPipeline]\n" + report);
                return;
            }

            Debug.Log("[MapPipeline]\n" + report);
        }

        /// <summary>
        /// Batch entry point. Exits non-zero if any pass fails, so a failed dressing
        /// scatter or a §12 rejection stops a build instead of shipping the scene it
        /// left behind.
        /// </summary>
        public static void RegenerateFromCommandLine()
        {
            try
            {
                var mapSeed = IntArg("-mapSeed", DescentMap.DefaultSeed);
                var dressSeed = IntArg("-dressSeed", DressingScatter.DefaultSeed);
                var tier = IntArg("-atmoTier", 0);

                var ok = Regenerate(mapSeed, dressSeed, tier, out var report);
                if (!ok)
                {
                    Debug.LogError("[MapPipeline]\n" + report);
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log("[MapPipeline]\n" + report);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[MapPipeline] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Layout, then dressing, then atmosphere. Stops at the first failure and
        /// returns everything logged up to that point.
        /// </summary>
        /// <param name="mapSeed">Fixes the layout. Same seed, same rooms.</param>
        /// <param name="dressSeed">Fixes the scatter. Same seed, same crates.</param>
        /// <param name="tierIndex">§07 row whose environment gets baked into the scene.</param>
        /// <param name="report">Human-readable log of every pass that ran.</param>
        public static bool Regenerate(int mapSeed, int dressSeed, int tierIndex, out string report)
        {
            var log = new List<string>();

            log.Add("── layout ──");
            if (!MapSceneGenerator.Generate(mapSeed, out var layout))
            {
                log.Add(layout);
                report = string.Join("\n", log);
                return false;
            }

            log.Add(layout);

            log.Add("── dressing ──");
            EditorSceneManager.OpenScene(SceneGenPaths.MapScene, OpenSceneMode.Single);
            var dressed = DressingScatter.Run(dressSeed, out var dressing);
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            log.Add(dressing);

            // The atmosphere pass runs even when the scatter reported a failure.
            // The scatter's gate is about NavMesh reachability, which has nothing to
            // do with what the air looks like, and the scatter saves its scene
            // either way — so bailing out here would leave a scene on disk with
            // dressing in it and no environment, which is a worse artefact than the
            // one the gate is complaining about. The failure is still reported; it
            // just does not get to corrupt an unrelated pass.
            log.Add("── atmosphere ──");
            AtmosphereSetup.Configure();
            AtmosphereSetup.ApplyEnvironmentToMapScenes(tierIndex);
            log.Add("§07 tier " + tierIndex + " baked into every Map_ scene.");

            report = string.Join("\n", log);
            return dressed;
        }

        private static int IntArg(string flag, int fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal)
                    && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }
    }
}
