using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HorrorGame.Core.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// The command that builds the playable map.
    /// <para>
    /// Order matters and is the whole point: sketch → validate → build → grade.
    /// §12's checklist runs against the <em>graph</em>, before a single FBX is
    /// instantiated, and a failure aborts with the failing rules printed. Building
    /// first and validating the scene afterwards would leave a broken map on disk and
    /// make the failure look like a scene bug instead of a design one.
    /// </para>
    /// <para>
    /// Batch entry points return non-zero by calling <c>EditorApplication.Exit</c>, so
    /// CI notices. In the editor the same failure is a <c>LogError</c> and no scene is
    /// written.
    /// </para>
    /// </summary>
    public static class MapSceneGenerator
    {
        private const string SeedArgument = "-mapSeed";

        /// <summary>Generates §12's 첫 맵 스케치 at the default seed and saves it.</summary>
        [MenuItem("HorrorGame/Scene Gen/Generate First Map", priority = 20)]
        public static void GenerateFirstMapMenu()
        {
            if (!Generate(FirstMapSketch.DefaultSeed, out var message))
            {
                EditorUtility.DisplayDialog("§12 validation failed", message, "OK");
            }
        }

        /// <summary>Generates the map at a seed typed into a dialog, so a reported layout can be reproduced.</summary>
        [MenuItem("HorrorGame/Scene Gen/Generate First Map (choose seed)…", priority = 21)]
        public static void GenerateFirstMapWithSeedMenu()
        {
            var entered = EditorUtility.DisplayDialogComplex(
                "Seed",
                "Generate §12's first map from which seed?\n\nThe seed fixes the whole scene: the same seed "
                + "always rebuilds the same map, which is how a bad layout reported by a playtester gets "
                + "reproduced here.",
                "Default (" + FirstMapSketch.DefaultSeed + ")",
                "Cancel",
                "Random");

            if (entered == 1)
            {
                return;
            }

            var seed = entered == 0
                ? FirstMapSketch.DefaultSeed
                : Environment.TickCount;

            if (!Generate(seed, out var message))
            {
                EditorUtility.DisplayDialog("§12 validation failed", message, "OK");
            }
        }

        /// <summary>
        /// Grades the map without writing anything — the fast loop while tuning a
        /// layout, and what a test or CI job calls to see the 주자 테스트 number.
        /// </summary>
        [MenuItem("HorrorGame/Scene Gen/Report Map Quality", priority = 22)]
        public static void ReportQualityMenu()
        {
            var report = MapQualityReport.Measure(FirstMapSketch.Build(FirstMapSketch.DefaultSeed));
            Debug.Log("[SceneGen]\n" + report.Describe());
        }

        /// <summary>
        /// Batch entry point. Reads <c>-mapSeed &lt;n&gt;</c> from the command line and
        /// exits non-zero when §12 rejects the map.
        /// </summary>
        public static void GenerateFromCommandLine()
        {
            var seed = ReadSeedArgument();
            if (!Generate(seed, out var message))
            {
                Debug.LogError("[SceneGen] " + message);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[SceneGen] " + message);
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Sketches, validates, builds and saves. Returns false with the failing §12
        /// rules in <paramref name="message"/> and writes nothing.
        /// </summary>
        /// <param name="seed">Fixes the map. The same seed gives the same scene.</param>
        /// <param name="message">Report text, on success and on failure.</param>
        public static bool Generate(int seed, out string message)
        {
            MapSketchResult map;
            try
            {
                map = FirstMapSketch.Build(seed);
            }
            catch (MapSketchException error)
            {
                message = "The sketch could not be turned into a map at all (seed " + seed + "): " + error.Message;
                return false;
            }

            var quality = MapQualityReport.Measure(map);
            if (!quality.Buildable)
            {
                message = "§12 rejected the map at seed " + seed + ", so nothing was written.\n"
                    + DescribeFailures(quality.Validation);
                return false;
            }

            VerifyKitManifest();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sceneName = Path.GetFileNameWithoutExtension(SceneGenPaths.MapScene);
            SceneGenPaths.EnsureFolder(SceneGenPaths.SceneRoot);
            SceneGenPaths.EnsureFolder(SceneGenPaths.GeneratedRoot);

            MapSceneBuilder.Build(map, sceneName);

            if (!EditorSceneManager.SaveScene(scene, SceneGenPaths.MapScene))
            {
                message = "Built the map but could not save it to " + SceneGenPaths.MapScene + ".";
                return false;
            }

            AssetDatabase.SaveAssets();
            RegisterScenes();

            message = "Wrote " + SceneGenPaths.MapScene + " from seed " + seed + ".\n"
                + quality.Describe()
                + Summarise(map);
            return true;
        }

        /// <summary>
        /// Puts the generated scenes into Build Settings, bootstrap first.
        /// <para>
        /// <c>BuildPipelineScenes</c> falls back to discovering scenes on disk and warns
        /// when it has to, because load order is a design decision. This is where that
        /// decision is made: scene 0 is the bootstrap, so a player comes up on the menu
        /// rather than in the middle of the map with no transport.
        /// </para>
        /// </summary>
        public static void RegisterScenes()
        {
            var wanted = new List<string>();
            if (File.Exists(SceneGenPaths.BootstrapScene))
            {
                wanted.Add(SceneGenPaths.BootstrapScene);
            }

            if (File.Exists(SceneGenPaths.MapScene))
            {
                wanted.Add(SceneGenPaths.MapScene);
            }

            var scenes = new EditorBuildSettingsScene[wanted.Count];
            for (var i = 0; i < wanted.Count; i++)
            {
                scenes[i] = new EditorBuildSettingsScene(wanted[i], true);
            }

            EditorBuildSettings.scenes = scenes;
        }

        private static string DescribeFailures(MapValidationReport validation)
        {
            var failures = validation.Failures;
            var text = new System.Text.StringBuilder();
            for (var i = 0; i < failures.Length; i++)
            {
                text.Append(failures[i].Describe()).Append('\n');
            }

            return text.ToString();
        }

        private static string Summarise(MapSketchResult map)
        {
            var graph = map.Graph;
            var deadEnds = 0;
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                if (graph.IsDeadEnd(i))
                {
                    deadEnds++;
                }
            }

            return "Scene contents: " + map.Tiles.Length + " kit pieces, " + map.Props.Length + " props, "
                + map.Markers.Length + " markers; graph has " + graph.Nodes.Length + " places, "
                + graph.Edges.Length + " passages, " + graph.IndependentLoopCount + " 순환로, "
                + deadEnds + " 막힌 길.\n";
        }

        /// <summary>
        /// Checks the kit manifest still agrees with <see cref="MapKitCatalogue"/>.
        /// <para>
        /// A re-export that changed <c>grid_metres</c> would leave every piece placed
        /// on the old lattice: docked, validated, and 1.25 m out of line all through the
        /// building. That is the failure this catches, and it is cheap enough to run
        /// every time.
        /// </para>
        /// </summary>
        private static void VerifyKitManifest()
        {
            const string manifestPath = "Assets/Models/MapKit/MapKit.manifest.json";
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(manifestPath);
            if (text == null)
            {
                Debug.LogWarning("[SceneGen] " + manifestPath + " is missing, so the kit's grid could not be "
                    + "checked against MapKitCatalogue.GridMetres = " + MapKitCatalogue.GridMetres + ".");
                return;
            }

            var marker = "\"grid_metres\"";
            var at = text.text.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
            {
                return;
            }

            var colon = text.text.IndexOf(':', at);
            var comma = text.text.IndexOfAny(new[] { ',', '\n', '}' }, colon + 1);
            if (colon < 0 || comma < 0)
            {
                return;
            }

            var value = text.text.Substring(colon + 1, comma - colon - 1).Trim();
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var grid)
                && Mathf.Abs(grid - MapKitCatalogue.GridMetres) > 0.001f)
            {
                Debug.LogError("[SceneGen] The MapKit manifest says grid_metres = " + grid
                    + " but MapKitCatalogue.GridMetres is " + MapKitCatalogue.GridMetres
                    + ". Every piece would be placed on the wrong lattice — fix the constant before generating.");
            }
        }

        private static int ReadSeedArgument()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], SeedArgument, StringComparison.Ordinal)
                    && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
                {
                    return seed;
                }
            }

            return FirstMapSketch.DefaultSeed;
        }
    }
}
