using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HorrorGame.Core.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

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

            // The second gate, and the one §12's checklist cannot be: MapValidator
            // judged the graph, and a graph is joined whatever the baked surface does.
            // B-001 is exactly that gap — thirteen islands under a map that passed
            // 17/17 — so the scene is measured against the surface the monster will
            // actually path on before it is allowed onto disk.
            var connectivity = NavMeshConnectivity.Audit(scene);
            if (!connectivity.Passed)
            {
                message = "The map built, but §06's monster cannot use it, so nothing was written.\n"
                    + connectivity.Describe();
                return false;
            }

            if (!EditorSceneManager.SaveScene(scene, SceneGenPaths.MapScene))
            {
                message = "Built the map but could not save it to " + SceneGenPaths.MapScene + ".";
                return false;
            }

            AssetDatabase.SaveAssets();
            RegisterScenes();

            message = "Wrote " + SceneGenPaths.MapScene + " from seed " + seed + ".\n"
                + quality.Describe()
                + Summarise(map)
                + connectivity.Describe();
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
        /// Checks the kit manifest still agrees with <see cref="MapKitCatalogue"/> and
        /// with the NavMesh agent the map will be baked for.
        /// <para>
        /// A re-export that changed <c>grid_metres</c> would leave every piece placed
        /// on the old lattice: docked, validated, and 1.25 m out of line all through the
        /// building. That is the failure this catches, and it is cheap enough to run
        /// every time.
        /// </para>
        /// <para>
        /// The 계단 rows are B-001's. The kit is modelled in Blender, which cannot read
        /// <c>ProjectSettings/NavMeshAreas.asset</c>, so <c>gen_mapkit.py</c> restates
        /// the agent it sized the stair for and publishes the stair's measured
        /// dimensions beside it. Retuning the agent here and the stair there is exactly
        /// how a building ends up with stairs nothing can climb, and the symptom —
        /// an antagonist that stands still — names neither file.
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

            if (TryReadNumber(text.text, "grid_metres", out var grid)
                && Mathf.Abs(grid - MapKitCatalogue.GridMetres) > 0.001f)
            {
                Debug.LogError("[SceneGen] The MapKit manifest says grid_metres = " + grid
                    + " but MapKitCatalogue.GridMetres is " + MapKitCatalogue.GridMetres
                    + ". Every piece would be placed on the wrong lattice — fix the constant before generating.");
            }

            VerifyStairFitsTheAgent(text.text);
        }

        /// <summary>
        /// Holds the kit's 계단 against the live agent settings, which is the check
        /// whose absence was B-001.
        /// <para>
        /// Three numbers decide whether a stair is a route or a wall, and all three are
        /// about the agent rather than about the stair: a riser taller than its climb
        /// does not bake; a landing shallower than four radii leaves less than one
        /// agent's width once Recast erodes the walkable region in from both sides, so
        /// the dog-leg turn either vanishes or survives on a knife edge; and a flight
        /// narrower than two radii is not a flight at all.
        /// </para>
        /// </summary>
        private static void VerifyStairFitsTheAgent(string manifest)
        {
            var agent = NavMesh.GetSettingsByID(0);

            if (TryReadNumber(manifest, "stair_rise_metres", out var rise) && rise > agent.agentClimb)
            {
                Debug.LogError("[SceneGen] The kit's 계단 rises " + rise.ToString("0.000")
                    + " m per tread against an agent climb of " + agent.agentClimb
                    + " m. The treads will not bake, every storey will be its own island, and §06's "
                    + "monster will never leave the floor it spawned on.");
            }

            // Four radii: one diameter is eroded away, one has to be left over, because
            // a turn the width of a knife edge is a turn one re-export deletes.
            var needed = 4f * agent.agentRadius;
            if (TryReadNumber(manifest, "stair_landing_depth_metres", out var landing) && landing < needed)
            {
                Debug.LogError("[SceneGen] The kit's 계단 landing is " + landing.ToString("0.00")
                    + " m deep and needs " + needed.ToString("0.00") + " m for an agent of radius "
                    + agent.agentRadius + " m to turn on it — " + (landing - (2f * agent.agentRadius)).ToString("0.00")
                    + " m survives the erosion. This is B-001: the flights bake, the landing does not join "
                    + "them, and the only thing that used to hide it was a NavMeshLink the monster cannot use.");
            }

            if (TryReadNumber(manifest, "stair_flight_clear_width_metres", out var flight)
                && flight <= 2f * agent.agentRadius)
            {
                Debug.LogError("[SceneGen] The kit's 계단 flights are " + flight.ToString("0.00")
                    + " m clear, which an agent of radius " + agent.agentRadius + " m erodes to nothing.");
            }

            if (TryReadNumber(manifest, "stair_headroom_metres", out var headroom) && headroom < agent.agentHeight)
            {
                Debug.LogError("[SceneGen] The kit's 계단 has " + headroom.ToString("0.00")
                    + " m of headroom at its tightest against an agent " + agent.agentHeight + " m tall.");
            }
        }

        /// <summary>
        /// Pulls one numeric field out of the manifest by name.
        /// <para>
        /// A deliberately small reader rather than a JSON dependency: the manifest is
        /// written by this repository's own generator, every key it looks for is unique
        /// in the document, and the alternative is a package reference in an editor
        /// assembly for four floats.
        /// </para>
        /// </summary>
        /// <returns>False when the key is absent or unparseable, which is never an error on its own.</returns>
        private static bool TryReadNumber(string manifest, string key, out float value)
        {
            value = 0f;
            var at = manifest.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            var colon = manifest.IndexOf(':', at);
            if (colon < 0)
            {
                return false;
            }

            var end = manifest.IndexOfAny(new[] { ',', '\n', '}' }, colon + 1);
            if (end < 0)
            {
                return false;
            }

            var raw = manifest.Substring(colon + 1, end - colon - 1).Trim();
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
