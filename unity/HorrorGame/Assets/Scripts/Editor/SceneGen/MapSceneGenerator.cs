using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

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

        // The tower's axis, its outermost ring, and the RingOf() that read a place's ring
        // out of world metres used to be declared here, so that this file could work out
        // where a storey starts and where its middle is. They moved into MapValidator when
        // §12-D's centre-path became a rule instead of a log line: the rule and this report
        // have to agree about which places are 외곽 and which is 중심, and the only way to
        // guarantee that is for there to be one implementation. MapValidator's version is
        // grid-free — a ring is a constant Chebyshev radius from the zone's own centre —
        // because Core cannot see DescentMap or MapKitCatalogue.

        /// <summary>Generates §12's 첫 맵 스케치 at the default seed and saves it.</summary>
        [MenuItem("HorrorGame/Scene Gen/Generate First Map", priority = 20)]
        public static void GenerateFirstMapMenu()
        {
            if (!Generate(DescentMap.DefaultSeed, out var message))
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
                "Default (" + DescentMap.DefaultSeed + ")",
                "Cancel",
                "Random");

            if (entered == 1)
            {
                return;
            }

            var seed = entered == 0
                ? DescentMap.DefaultSeed
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
            var report = MapQualityReport.Measure(DescentMap.Build(DescentMap.DefaultSeed));
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
        /// Sketches, validates, builds, audits, and — only if all of that holds —
        /// commits the scene and its bake as one generation. Returns false with the
        /// failing rules in <paramref name="message"/>, having left every file it is
        /// allowed to write byte-identical to how it found them.
        /// <para>
        /// <b>Why this method is a transaction.</b> The three gates below decide whether
        /// the scene reaches disk; the NavMesh bake was never behind them.
        /// <c>MapSceneBuilder.BakeNavMesh</c> ends in
        /// <c>AssetDatabase.DeleteAsset</c> + <c>CreateAsset</c>, which fires the moment
        /// the surface is built — before a single gate has run — and the file it
        /// replaces is the one the shipped scene references by GUID
        /// (<c>26ffd78e0ece1459686bbf4580765605</c>). Measured on disk 2026-08-03:
        /// <c>Map_FirstSketch.unity</c> 01:03, <c>Map_FirstSketch_Solo.unity</c> 01:08,
        /// <c>NavMesh_Map_FirstSketch.asset</c> <b>01:59</b> — the 01:59 run is the one
        /// whose log ends "so nothing was written". The scene was pathed against a
        /// surface baked from geometry the generator had judged unfit and thrown away,
        /// and nothing on disk said so.
        /// </para>
        /// <para>
        /// That is not a cosmetic mismatch. It is why B-009 got three byte-identical
        /// audits off changing geometry and why B-010's playthrough measurement had to
        /// be discarded: the instrument and the specimen came from different runs. So
        /// from the first line that can touch disk this method holds a snapshot of
        /// everything the generator is allowed to write, and either commits a whole
        /// generation or puts every byte back.
        /// </para>
        /// <para>
        /// <c>-forceWrite</c> still works and still writes a COHERENT pair: it moves a
        /// run from "rejected" to "committed", never from "rejected" to "half written".
        /// The scene, the bake and the log all carry the same generation stamp with
        /// <c>-forced</c> in it, so a forced build is identifiable from the artefact
        /// rather than from whoever remembers running it.
        /// </para>
        /// </summary>
        /// <param name="seed">Fixes the map. The same seed gives the same scene.</param>
        /// <param name="message">Report text, on success and on failure.</param>
        public static bool Generate(int seed, out string message)
        {
            var generation = NewGenerationId(seed);

            MapSketchResult map;
            try
            {
                map = DescentMap.Build(seed);
            }
            catch (MapSketchException error)
            {
                message = "The sketch could not be turned into a map at all (seed " + seed + "): " + error.Message;
                return false;
            }

            var quality = MapQualityReport.Measure(map);
            if (!quality.Buildable)
            {
                // A §12 failure stops the map reaching disk, and should: a map that breaks
                // the rules the escape maths is derived from is not a map.
                //
                // Except for the ones on KnownFailingRules, which is not the same thing as
                // "except for the ones nobody wants to fix". Each entry there carries what
                // was measured, what was required and what fixing the MAP would take, so a
                // §12 verdict of "green with two waivers" is a shorter statement than the
                // waivers themselves rather than a substitute for them.
                var blocking = DescribeBlockingFailures(quality.Validation, out var deferredOnly);

                if (!deferredOnly)
                {
                    message = "§12 rejected the map at seed " + seed + ", so nothing was written.\n" + blocking;
                    return false;
                }

                // Every generation, not once. This is the line that keeps a waived defect
                // from turning into a forgotten one, so it prints the whole §12 failure list
                // and not just its length.
                Debug.LogWarning(
                    "[SceneGen] §12 is failing " + quality.Validation.Failures.Length
                    + " rule(s) that KnownFailingRules waives by name, so the map was written anyway. "
                    + "This build has KNOWN MAP DEFECTS in it — read KnownFailingRules for what each one "
                    + "measured, what §12 required, and what fixing the geometry would take.\n"
                    + DescribeFailures(quality.Validation));
            }

            VerifyKitManifest();

            // ── everything below this line can touch disk ────────────────────────────
            //
            // Captured BEFORE EnsureFolder, because creating a folder is already a write
            // and a rollback that cannot undo it is not a rollback. See the class docs
            // on GeneratedTree for what is snapshotted and what is only watched.
            var sceneName = Path.GetFileNameWithoutExtension(SceneGenPaths.MapScene);
            var tree = GeneratedTree.Capture(NavMeshAssetPathFor(sceneName));

            // Set on every path that has already decided what disk should look like —
            // committed, or rolled back by Reject. Anything that leaves the try block
            // without it left the tree mid-generation, which is what the finally is for.
            var settled = false;

            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SceneGenPaths.EnsureFolder(SceneGenPaths.SceneRoot);
                SceneGenPaths.EnsureFolder(SceneGenPaths.GeneratedRoot);

                // The return value used to be dropped. It is the only handle on the
                // NavMeshSurface this run baked, and the commit below needs it to prove
                // that the asset the scene will reference is the one this run produced.
                var root = MapSceneBuilder.Build(map, sceneName);

                // Gates that were forced past, in the order they were forced. Empty on a
                // clean generation, and it is what puts "-forced" in the stamp.
                var forced = new List<string>();

                // The second gate, and the one §12's checklist cannot be: MapValidator
                // judged the graph, and a graph is joined whatever the baked surface does.
                // B-001 is exactly that gap — thirteen islands under a map that passed
                // 17/17 — so the scene is measured against the surface the monster will
                // actually path on before it is allowed onto disk.
                var connectivity = NavMeshConnectivity.Audit(scene);
                if (!connectivity.Passed)
                {
                    // -forceWrite writes the scene anyway and says exactly what is wrong with it.
                    //
                    // This is not a way to silence the gate. The gate is right: §06's creature
                    // cannot reach part of this building and that is a defect. But it is a defect
                    // in ONE system, and the map has eight mazes, sixteen doors, two chutes a
                    // floor and twenty starting positions that nobody has ever walked. Holding
                    // all of that behind the monster means the only thing anybody can playtest is
                    // the thing that already works.
                    //
                    // The failure is printed in full, every time, and the generation stamp on
                    // the scene, on the bake and in the log says -forced. A build made this way
                    // is a playtest build with a named defect, not a build that passed.
                    if (!HasFlag("-forceWrite"))
                    {
                        settled = true;
                        return Reject(
                            tree,
                            generation,
                            "the §06 NavMesh gate",
                            "The map built, but §06's monster cannot use it, so nothing was written.\n"
                            + connectivity.Describe()
                            + "\n\nPass -forceWrite to write it anyway and walk the maze while this is open.",
                            out message);
                    }

                    forced.Add("§06 NavMesh connectivity");
                    Debug.LogWarning(
                        "[SceneGen] -forceWrite: the surface is BROKEN and the scene was written anyway. "
                        + "§06's creature cannot reach part of this building. Everything you see in this "
                        + "build about the maze, the chutes and the doors is real; everything you see "
                        + "about the monster is not.\n" + connectivity.Describe());
                }

                // The third gate, and the one the first two structurally cannot be. Both of
                // them measure the monster: §12's checklist is a graph the monster's chase
                // is derived from, and the connectivity audit walks the surface the monster
                // is baked onto. Neither has ever asked whether a *player* can walk the
                // building, and the two bodies are not the same — agentClimb is 0.75 m and
                // stepOffset is 0.40 m, so every riser between them is a stair only the
                // antagonist can use. A map can score 1830/1830 with one island while a
                // human is locked on the entrance storey, which is exactly what shipped.
                var reach = PlayerTraversal.Audit(scene);
                if (!reach.Passed && HasFlag("-forceWrite"))
                {
                    forced.Add("player traversal");
                    Debug.LogWarning(
                        "[SceneGen] -forceWrite: a PLAYER cannot walk all of this either.\n" + reach.Describe());
                }
                else if (!reach.Passed)
                {
                    settled = true;
                    return Reject(
                        tree,
                        generation,
                        "the player-traversal gate",
                        "The map built and §06's monster can use it, but a player cannot, so nothing was "
                        + "written.\n" + reach.Describe(),
                        out message);
                }

                if (forced.Count > 0)
                {
                    generation += "-forced";
                }

                if (!Commit(scene, root, sceneName, generation, forced, out var refusal))
                {
                    // Commit only returns false before the scene reaches disk, so the
                    // rollback below is enough to leave nothing half-written.
                    settled = true;
                    return Reject(tree, generation, "the commit", refusal, out message);
                }

                settled = true;
                message = "Wrote " + SceneGenPaths.MapScene + " and " + NavMeshAssetPathFor(sceneName)
                    + " from seed " + seed + ", both as " + generation + ".\n"
                    + quality.Describe()
                    + DescribeCentrePath(quality.Validation)
                    + DescribeChaseToll(map.Graph)
                    + Summarise(map)
                    + connectivity.Describe()
                    + reach.Describe();
                return true;
            }
            finally
            {
                // An exception between the first write and the commit is the same defect
                // as a failed gate — a bake on disk from a generation with no scene — so
                // it unwinds the same way rather than leaving the tree mid-generation.
                if (!settled)
                {
                    Debug.LogError(
                        "[SceneGen] " + generation + " threw after it had started writing. Rolling back so the "
                        + "bake on disk does not outlive the scene it belongs to. " + RollBackEverything(tree));
                }
            }
        }

        /// <summary>
        /// Puts the tree back and says so in one line, then hands the caller the failure
        /// report with the rollback appended.
        /// </summary>
        /// <param name="gate">Which gate refused, named the way a human would say it.</param>
        /// <param name="reason">The gate's own report — what is wrong with the map.</param>
        private static bool Reject(
            GeneratedTree tree, string generation, string gate, string reason, out string message)
        {
            var rollback = RollBackEverything(tree);
            Debug.Log("[SceneGen] " + generation + " was REJECTED at " + gate + " — " + rollback);
            message = reason + "\n\n" + rollback;
            return false;
        }

        /// <summary>
        /// Undoes a rejected run on disk AND in the session, which are two different
        /// places the same generation can survive in.
        /// <para>
        /// <c>BuildNavMesh</c> registers its data with the global NavMesh — that is the
        /// mesh <c>SamplePosition</c> and <c>CalculatePath</c> read — so a rejected bake
        /// stays queryable in this editor session after its files are gone. Leaving it
        /// there would recreate B-009 from the other end: a tool run next in the same
        /// session, against a scene it did not reload, would measure a generation that
        /// exists nowhere on disk. Clearing it costs nothing, because opening any scene
        /// re-registers its surfaces on load.
        /// </para>
        /// </summary>
        private static string RollBackEverything(GeneratedTree tree)
        {
            NavMesh.RemoveAllNavMeshData();
            return tree.RollBack();
        }

        /// <summary>
        /// Writes the scene and the bake as one generation, stamps both with the same id,
        /// and then reads the artefact back to check that is what actually happened.
        /// <para>
        /// The order is the fix. <c>NavMeshSurface.BuildNavMesh()</c> never touches the
        /// file system — it calls <c>NavMeshBuilder.BuildNavMeshData</c>, assigns the
        /// result to <c>m_NavMeshData</c> and registers it with the global NavMesh — so
        /// the bake can exist, be audited, and be thrown away without leaving a trace.
        /// The only line that reaches disk is <c>AssetDatabase.CreateAsset</c>, and that
        /// belongs HERE, after the gates, in the same breath as <c>SaveScene</c>.
        /// </para>
        /// <para>
        /// It has to be in that order and not the other way round: a NavMeshData that is
        /// not yet an asset when the scene is saved has no file for the surface to point
        /// at, so <c>NavMeshSurface.m_NavMeshData</c> serialises as <c>{fileID: 0}</c> —
        /// a scene with no surface at all, which every audit in this project would
        /// happily call a pass, because <c>SamplePosition</c> against nothing reports
        /// nothing rather than failing. That is why this method re-reads the saved
        /// <c>.unity</c> afterwards and looks for the GUID. It looks for the GUID and not
        /// for <c>{fileID: 0}</c> because every scene has one of those already: the
        /// legacy <c>NavMeshSettings</c> block at the top of the file carries an empty
        /// <c>m_NavMeshData</c> whenever the map is baked through a surface, which is
        /// always here.
        /// </para>
        /// </summary>
        /// <param name="root">The map root <see cref="MapSceneBuilder.Build"/> returned.</param>
        /// <param name="forced">Gates this run was forced past; empty on a clean run.</param>
        /// <param name="refusal">Why nothing was written. Only set when this returns false.</param>
        private static bool Commit(
            Scene scene,
            GameObject root,
            string sceneName,
            string generation,
            List<string> forced,
            out string refusal)
        {
            var navPath = NavMeshAssetPathFor(sceneName);
            var surface = root != null ? root.GetComponentInChildren<NavMeshSurface>(true) : null;
            var data = surface != null ? surface.navMeshData : null;

            if (data == null)
            {
                // Not fatal — the scene is still worth having — but it is the one state in
                // which §06 cannot exist, and it has shipped unnoticed before.
                Debug.LogError(
                    "[SceneGen] " + generation + " has no baked NavMeshData to commit, so the scene about to be "
                    + "written references no surface and §06's creature cannot move in it.");
            }
            else if (!AssetDatabase.Contains(data))
            {
                // The forward-compatible half. Today MapSceneBuilder.BakeNavMesh has
                // already written the asset by the time we get here; when that write moves
                // out of the builder (see the report on this change), this is the line that
                // keeps the pair coherent, and it runs after the gates by construction.
                SceneGenPaths.EnsureFolder(SceneGenPaths.NavMeshRoot);
                AssetDatabase.DeleteAsset(navPath);
                AssetDatabase.CreateAsset(data, navPath);
            }
            else
            {
                // The builder's path formula is duplicated in NavMeshAssetPathFor, so it is
                // checked rather than trusted: a bake committed anywhere else is a scene
                // referencing a file nobody maintains.
                var actual = AssetDatabase.GetAssetPath(data);
                if (!string.Equals(actual, navPath, StringComparison.Ordinal))
                {
                    Debug.LogError(
                        "[SceneGen] " + generation + " baked into " + actual + " but this generator commits "
                        + navPath + ". The scene is about to reference the first and every tool in the "
                        + "repository looks for the second.");
                    navPath = actual;
                }
            }

            // The stamp, in the scene. A named empty transform and nothing else: a
            // component would have to live in an editor assembly, which is not in the
            // player, so the shipped scene would carry a missing script instead of a
            // fact. The name is chosen to match none of the prefixes NavMeshConnectivity
            // and PlayerTraversal collect ("PlayerSpawn", "CandidateSite", "LootSpawn",
            // "Exit"…), so stamping cannot move a future audit's numbers.
            var stampName = GenerationStampPrefix + generation;
            var stamp = new GameObject(stampName);
            if (stamp.scene != scene)
            {
                SceneManager.MoveGameObjectToScene(stamp, scene);
            }

            if (!EditorSceneManager.SaveScene(scene, SceneGenPaths.MapScene))
            {
                refusal = "Built the map but could not save it to " + SceneGenPaths.MapScene + ".";
                return false;
            }

            // The stamp, on the bake. userData rather than the object's name because the
            // .asset is Unity's binary format and the name is not greppable in it, while
            // the .meta is text, is tracked in git, and sits beside the file it describes
            // — the same reason AssetImportPolicy records intent there. Written after the
            // save so no reimport can interleave with serialising the scene.
            var stamped = false;
            var importer = AssetImporter.GetAtPath(navPath);
            if (importer != null)
            {
                importer.userData = generation + "; the bake for " + SceneGenPaths.MapScene
                    + (forced.Count > 0 ? "; forced past " + string.Join(", ", forced) : string.Empty);

                // Flush the .meta without a reimport first, then check the file — the
                // stamp is only worth having if it is actually in the bytes, and this
                // whole change exists because "the call was made" and "the file says so"
                // turned out to be different things. SaveAndReimport is the fallback and
                // is safe here only because the scene is already written.
                AssetDatabase.WriteImportSettingsIfDirty(navPath);
                stamped = string.Equals(ReadStamp(navPath), generation, StringComparison.Ordinal);
                if (!stamped)
                {
                    importer.SaveAndReimport();
                    stamped = string.Equals(ReadStamp(navPath), generation, StringComparison.Ordinal);
                }
            }

            AssetDatabase.SaveAssets();
            RegisterScenes();

            // ── the artefact, read back ─────────────────────────────────────────────
            // Not a formality. Every defect this file's history is made of was invisible
            // in the source and obvious in the file: a map generated into the wrong
            // scene, a bake belonging to a different run. The scene is 11 MB of text and
            // the two facts that matter are one IndexOf each.
            var guid = AssetDatabase.AssetPathToGUID(navPath);
            var sceneText = File.Exists(SceneGenPaths.MapScene)
                ? File.ReadAllText(SceneGenPaths.MapScene)
                : string.Empty;
            var referencesBake = !string.IsNullOrEmpty(guid)
                && sceneText.IndexOf("guid: " + guid, StringComparison.Ordinal) >= 0;
            var carriesStamp = sceneText.IndexOf(stampName, StringComparison.Ordinal) >= 0;

            if (!referencesBake)
            {
                Debug.LogError(
                    "[SceneGen] " + SceneGenPaths.MapScene + " was written WITHOUT a reference to " + navPath
                    + " (guid " + guid + "). §06 has no surface in the scene that ships, and no audit run "
                    + "afterwards would say so — SamplePosition against nothing reports nothing, not a failure.");
            }

            if (!carriesStamp || !stamped)
            {
                Debug.LogError(
                    "[SceneGen] " + generation + " could not stamp both artefacts (scene "
                    + (carriesStamp ? "ok" : "MISSING") + ", bake meta " + (stamped ? "ok" : "MISSING")
                    + "). The pair on disk is coherent, but nothing on disk proves it, which is the state "
                    + "this change exists to end.");
            }

            // The one line. Everything a human needs to decide whether a number they are
            // about to quote is about this map: which generation, which two files, how big
            // the surface is, and whether a gate was forced. The vertex count is the GLOBAL
            // triangulation — the mesh SamplePosition and CalculatePath read, which
            // MapSceneBuilder cleared before baking, so it is the same surface both audits
            // above walked and not a second opinion about it.
            var bytes = File.Exists(navPath) ? new FileInfo(navPath).Length : 0L;
            Debug.Log(
                "[SceneGen] " + generation + ": " + SceneGenPaths.MapScene + " and " + navPath
                + " were BOTH written by this run — " + NavMesh.CalculateTriangulation().vertices.Length
                + " vertices, " + bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes"
                + (forced.Count > 0 ? ", FORCED past " + string.Join(" and ", forced) : string.Empty)
                + "; the same stamp is on '" + stampName + "' in the scene and in " + navPath
                + ".meta, so anything else claiming to be this map is one grep from being caught.");

            refusal = null;
            return true;
        }

        /// <summary>
        /// Names one run, in a form that survives into the artefacts and sorts by time:
        /// <c>gen-20260803-021407-seed20260802</c>, plus <c>-forced</c> when a gate was
        /// overridden. Only characters that are safe in a GameObject name and in the
        /// plain YAML scalar of a <c>.meta</c> — no colons, no hashes, no quotes.
        /// </summary>
        private static string NewGenerationId(int seed) =>
            "gen-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-seed" + seed.ToString(CultureInfo.InvariantCulture);

        /// <summary>Prefix of the empty transform that carries the generation id in the scene.</summary>
        private const string GenerationStampPrefix = "SceneGen_";

        /// <summary>
        /// Where the bake for a scene lives. Mirrors the path
        /// <c>MapSceneBuilder.BakeNavMesh</c> builds; <see cref="Commit"/> checks the two
        /// agree rather than assuming it, because a silent disagreement here is a scene
        /// referencing a file no other tool in the repository ever looks at.
        /// </summary>
        private static string NavMeshAssetPathFor(string sceneName) =>
            SceneGenPaths.NavMeshRoot + "/NavMesh_" + sceneName + ".asset";

        /// <summary>
        /// Reads the generation id back out of a bake's <c>.meta</c>, or null when the
        /// file predates stamping — which is itself the useful answer, because it means
        /// something other than this generator last wrote that surface.
        /// </summary>
        private static string ReadStamp(string assetPath)
        {
            var meta = assetPath + ".meta";
            if (!File.Exists(meta))
            {
                return null;
            }

            foreach (var line in File.ReadAllLines(meta))
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("userData:", StringComparison.Ordinal))
                {
                    continue;
                }

                // Unquoted in practice — the id is letters, digits and dashes — but a
                // future longer stamp could make Unity quote the scalar, and a stamp that
                // reads back with a stray quote would look like a mismatch and cry wolf.
                var value = trimmed.Substring("userData:".Length).Trim().Trim('\'', '"');
                if (value.Length == 0)
                {
                    return null;
                }

                var end = value.IndexOf(';');
                return end < 0 ? value : value.Substring(0, end);
            }

            return null;
        }

        /// <summary>
        /// Puts the generated scenes into Build Settings, bootstrap first.
        /// <para>
        /// <c>BuildPipelineScenes</c> falls back to discovering scenes on disk and warns
        /// when it has to, because load order is a design decision. This is where that
        /// decision is made: scene 0 is the bootstrap, so a player comes up on the menu
        /// rather than in the middle of the map with no transport.
        /// </para>
        /// <para>
        /// The list is rewritten wholesale rather than appended to, so every scene the
        /// shipped game needs has to be named here. <see cref="SceneGenPaths.MatchScene"/>
        /// is one of them and was missing: regenerating the map dropped the scene 시작
        /// loads, and because <c>LoadSceneAsync</c> returns null rather than throwing for
        /// an unlisted scene, the only symptom was a menu button that did nothing.
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

            if (File.Exists(SceneGenPaths.MatchScene))
            {
                wanted.Add(SceneGenPaths.MatchScene);
            }

            // The descent roster. RaceLobby refuses to host when one of these is missing
            // from Build Settings, because LoadSceneAsync answers an unlisted scene with
            // null and the whole field would sit on a loading screen — B-005 with twenty
            // people on it. So regenerating the map must not silently drop them, which is
            // why they are re-added here rather than only by MapPipeline.BakeRoster.
            //
            // READ OUT OF THE MANIFEST, NOT OFF THE DISK, and that distinction is the whole
            // point. A slot scene exists on disk the moment it is STAGED; it becomes a
            // building somebody may be sent to only when BakeRoster's publication audit
            // passes and writes its name into the manifest. Discovering from the folder
            // would put every refused slot back into the build — measured, not feared: the
            // 2026-08-05 bake staged eight scenes and published none of them, and a
            // disk-driven version of this block shipped all eight (328 MiB of buildings the
            // lobby may not offer) into EditorBuildSettings. The manifest is the only thing
            // that knows which of those files a player is allowed to load.
            //
            // The literal paths duplicate MapPipeline's constants because that class is in
            // Assembly-CSharp-Editor, which references THIS assembly and not the reverse.
            const string descentFolder = "Assets/Scenes/Descent";
            const string rosterManifest = SceneGenPaths.GeneratedRoot + "/Resources/DescentRoster.txt";
            if (Directory.Exists(descentFolder) && File.Exists(rosterManifest))
            {
                var manifest = File.ReadAllText(rosterManifest);
                var slots = Directory.GetFiles(descentFolder, "*.unity");
                Array.Sort(slots, StringComparer.Ordinal);
                foreach (var slot in slots)
                {
                    // Substring rather than a parse: this file must not have to know the
                    // manifest's column layout, and a name that is absent from the text
                    // cannot have been published under any layout.
                    var name = Path.GetFileNameWithoutExtension(slot);
                    if (manifest.IndexOf(name, StringComparison.Ordinal) >= 0)
                    {
                        wanted.Add(slot.Replace('\\', '/'));
                    }
                }
            }

            var scenes = new EditorBuildSettingsScene[wanted.Count];
            for (var i = 0; i < wanted.Count; i++)
            {
                scenes[i] = new EditorBuildSettingsScene(wanted[i], true);
            }

            EditorBuildSettings.scenes = scenes;
        }

        /// <summary>
        /// Rules the shipped map FAILS, waived by name so the map can still be written
        /// while the failure stays visible on every single generation.
        /// <para>
        /// <b>An entry here is a record; deleting the rule would be forgetting.</b> This
        /// list was emptied once, and the way it was emptied is why it now has a required
        /// shape: three rules went green without the map moving — the graph is identical on
        /// both sides of that diff, 48 시야 차단 지점, spacing 15 m, deepest 95 m, every
        /// storey 81.3 m across — because two rules were deleted and the third had its
        /// failing clause removed. Net gating change: −3, +0.
        /// </para>
        /// <para>
        /// <b>Every entry states the measured value, the required value, and what it would
        /// take to fix the MAP.</b> Without the numbers a waiver cannot be checked, and an
        /// unchecked waiver outlives its defect: <c>straight-corridor</c> sat on this list
        /// citing "22.5 m against 20 m" long after the geometry had been fixed to 17.5 m and
        /// the rule had started passing. That entry is retired and stays retired — the map
        /// measures 17 m and the rule is green.
        /// </para>
        /// <list type="bullet">
        /// <item><description><c>sight-break-spacing</c> (B-007) — <b>measured 95 m,
        /// allowed 14.4 m: 6.6× over.</b> The 간격 half of the rule is delivered exactly
        /// (every 시야 차단 지점 has another 15 m away, none over 25 m, so 「질주 60m에 3~4번의
        /// 기회」 holds). What fails is the width of one 지점: single-linkage grouping at 15 m
        /// runs one continuous piece of cover 95 m through the maze. The cap was 4.4 m while
        /// it subtracted §04's 주자 head start; with the deleted term stripped out it is
        /// <c>SingleCornerMinDistance</c> = 14.4 m, and the map is still six times over the
        /// weaker number. <b>To fix the map:</b> break the concentric bands with straight
        /// stretches at least 15 m long, so that a runner rounding one corner cannot chain
        /// into the next without re-entering sight — i.e. fewer, longer legs in
        /// <c>RadialStorey</c>, which trades against the 20 m straight-corridor cap and is
        /// the reason it is not a five-minute change.</description></item>
        /// <item><description><c>centre-path</c> (B-019) — <b>measured 47.5~82.5 m, required
        /// 90~140 m: all 30 storey entry points are outside, 7.5~42.5 m short.</b> Per storey:
        /// B1 47.5~82.5 m over its 16 rail cells, B2–B6 and B8 70~75 m, B7 60~65 m. §12-D says
        /// MapValidator checks this per storey and MapValidator never did; it was printed here
        /// instead and has never once been inside the band. <b>To fix the map:</b> a longer
        /// rim-to-middle route in <c>RadialStorey</c> — more bands, or gates set so the way in
        /// spirals rather than cuts across. §12-D says what the short version costs:
        /// 「60~90 m로 줄이면 아는 사람이 2분 만에 끝내고, 그러면 맵을 아는 것이 실력이라는 전제가
        /// 보상 없이 사라진다」.</description></item>
        /// </list>
        /// <para>
        /// <b>What does not belong here.</b> A rule the race does not want should be deleted
        /// with its reasoning at the rule's own tombstone, not parked here — that is what
        /// happened to <c>zone-diagonal</c>, whose subject (a 구역 SMALLER than a 층) the
        /// pivot removed. And a rule the map passes should not be waived by being deleted
        /// alongside one it fails: <c>open-adjacent-to-maze</c> came back for exactly that
        /// reason, minus the one clause that rested on §04.
        /// </para>
        /// </summary>
        private static readonly string[] KnownFailingRules =
        {
            MapValidator.RuleSightBreakSpacing,
            MapValidator.RuleCentrePath,
        };

        /// <summary>
        /// Describes the failures that must stop the build, and reports whether every
        /// failure is one of <see cref="KnownFailingRules"/>.
        /// </summary>
        /// <param name="deferredOnly">True when nothing unknown failed.</param>
        private static string DescribeBlockingFailures(MapValidationReport validation, out bool deferredOnly)
        {
            var text = new System.Text.StringBuilder();
            deferredOnly = true;

            foreach (var failure in validation.Failures)
            {
                if (System.Array.IndexOf(KnownFailingRules, failure.RuleId) >= 0)
                {
                    continue;
                }

                deferredOnly = false;
                text.Append(failure.Describe()).Append('\n');
            }

            return text.ToString();
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

        /// <summary>
        /// Reprints §12-D's <c>centre-path</c> verdict beside the map summary.
        /// <para>
        /// <b>It reprints; it does not measure.</b> This method used to own the
        /// measurement, because <see cref="MapValidator"/> implemented none of §12-D's five
        /// rules — <c>ring-gates</c>, <c>centre-path</c>, <c>centre-single-gate</c>,
        /// <c>chute-count</c>, <c>chute-landing</c> — despite §12-D's own sentence
        /// "MapValidator와 씬 감사기가 층마다 확인한다". A number printed here gated nothing,
        /// and the map has never once been inside the band, so the defect was visible and
        /// free. It is <see cref="MapValidator.RuleCentrePath"/> now, it fails, and
        /// <see cref="KnownFailingRules"/> waives it with the numbers.
        /// </para>
        /// <para>
        /// It stays in the generation message because the rest of this message is the map's
        /// dimensions and a reader comparing storeys should not have to go and find the §12
        /// report to see the one number that says how long a floor takes to cross.
        /// </para>
        /// </summary>
        private static string DescribeCentrePath(MapValidationReport validation)
        {
            var result = validation[MapValidator.RuleCentrePath];
            return "§12-D centre-path — " + (result.Passed ? "[ok]" : "[FAIL, waived]") + " "
                   + result.Detail + "\n";
        }

        /// <summary>
        /// 탈출 대가 — what a chase costs a runner, which is what the race grades a map on
        /// in place of §12's 주자 테스트 pass rate.
        /// <para>
        /// <b>Why a toll rather than a rate.</b> 「도망칠 수 있다」 meant 「죽지 않는다」 when
        /// §06 ended a caught runner's match. It no longer does: a caught runner is sent
        /// back to the cell they started from on B1 and keeps racing, losing every storey
        /// they had. So the question a race asks is not whether the release fires — §06's
        /// own arithmetic answers that for everybody, identically, since §04's roles went —
        /// but what it cost, in the only currency §07 leaves ("시간이 유일한 통화다") and the
        /// one §02 settles the match in ("먼저 닿는다").
        /// </para>
        /// <para>
        /// Toll = seconds from aggro to release, plus any ground given back walked home at
        /// <see cref="GameConstants.RunSpeed"/>. Ground is measured to the runner's OWN
        /// storey's middle, because §01 makes every floor an independent maze and a 투하구
        /// is a fall rather than a path. Banded by
        /// <see cref="GameConstants.ChaseTollSecondsMin"/> (one door — the only other way
        /// §01 lets a runner cost a rival time) and
        /// <see cref="GameConstants.ChaseTollSecondsMax"/> (one storey — what the cheapest
        /// possible catch, on B1, now costs).
        /// </para>
        /// <para>
        /// The ceiling is reported per storey as well as flat, because the catch it is
        /// derived from is storey-scaled: being caught on B<c>k</c> costs <c>k</c> storeys,
        /// so a 25 s escape is a breach on B1 and a bargain on B6.
        /// </para>
        /// </summary>
        private static string DescribeChaseToll(MapGraph graph)
        {
            var starts = new int[graph.Nodes.Length];
            for (var i = 0; i < starts.Length; i++)
            {
                starts[i] = i;
            }

            var report = HorrorGame.Core.Map.RunnerTest.RunAt(graph, starts);

            // +∞ where a middle cannot be walked to, which is the same "do not price
            // this one" the local version used to signal with NaN.
            var toMiddle = MapValidator.DistancesToStoreyMiddle(graph);

            var tolls = new List<float>();
            var belowFloor = 0;
            var overFlatCeiling = 0;
            var overStoreyCeiling = 0;
            var neverReleased = 0;

            foreach (var attempt in report.Attempts)
            {
                if (!attempt.Released)
                {
                    neverReleased++;
                    continue;
                }

                var start = attempt.StartNodeId;
                if (float.IsPositiveInfinity(toMiddle[start]))
                {
                    continue;
                }

                // Where the runner had got to when the release fired: 달리기 until the
                // sprint starts, 질주 after it, which is how RunnerTest itself moves them.
                var covered = (GameConstants.RunSpeed * Mathf.Min(attempt.ElapsedSeconds, attempt.SprintDelaySeconds))
                              + (GameConstants.RunnerSprintSpeed
                                 * Mathf.Max(0f, attempt.ElapsedSeconds - attempt.SprintDelaySeconds));
                var arc = 0f;
                var at = attempt.Route[0];
                for (var i = 1; i < attempt.Route.Length; i++)
                {
                    var step = EdgeLength(graph, attempt.Route[i - 1], attempt.Route[i]);
                    if (arc + step > covered)
                    {
                        break;
                    }

                    arc += step;
                    at = attempt.Route[i];
                }

                if (float.IsPositiveInfinity(toMiddle[at]))
                {
                    continue;
                }

                var givenBack = toMiddle[at] - toMiddle[start];
                var toll = attempt.ElapsedSeconds + (Mathf.Max(0f, givenBack) / GameConstants.RunSpeed);
                tolls.Add(toll);

                if (toll < GameConstants.ChaseTollSecondsMin)
                {
                    belowFloor++;
                }

                if (toll > GameConstants.ChaseTollSecondsMax)
                {
                    overFlatCeiling++;

                    // Storey index from the zone, which DescentMap numbers B1..B8 in order.
                    // Being caught on B(k) costs k storeys, so that is this place's ceiling.
                    var storey = graph.Nodes[start].ZoneId + 1;
                    if (toll > GameConstants.ChaseTollSecondsMax * storey)
                    {
                        overStoreyCeiling++;
                    }
                }
            }

            if (tolls.Count == 0)
            {
                return "§12 탈출 대가: nothing released anywhere, so there is no toll to measure — every "
                       + "chase on this map ends in a catch.\n";
            }

            tolls.Sort();
            var text = new System.Text.StringBuilder();
            text.Append("§12 탈출 대가 (§12's 실전 검증, priced instead of counted): ")
                .Append(tolls.Count).Append(" chases, min ")
                .Append(tolls[0].ToString("0.#", CultureInfo.InvariantCulture)).Append(" · median ")
                .Append(tolls[tolls.Count / 2].ToString("0.#", CultureInfo.InvariantCulture)).Append(" · p75 ")
                .Append(tolls[(3 * tolls.Count) / 4].ToString("0.#", CultureInfo.InvariantCulture)).Append(" · max ")
                .Append(tolls[tolls.Count - 1].ToString("0.#", CultureInfo.InvariantCulture))
                .Append(" s, against ").Append(GameConstants.ChaseTollSecondsMin).Append("~")
                .Append(GameConstants.ChaseTollSecondsMax.ToString("0.#", CultureInfo.InvariantCulture))
                .Append(" s (한 문 ~ 한 층).\n");

            text.Append("  ").Append(belowFloor)
                .Append(" below the floor — a chase cheaper than shutting one door is one §01 gives a "
                        + "tool for and nobody would use.\n");
            text.Append("  ").Append(overFlatCeiling).Append(" over the flat ceiling, of which ")
                .Append(overStoreyCeiling)
                .Append(" over their own storey's (B(k) costs k storeys to be caught on) — a chase dearer "
                        + "than the catch it prevents is one a runner should stop running from.\n");

            if (neverReleased > 0)
            {
                text.Append("  ").Append(neverReleased)
                    .Append(" places never released at all, so they charge a catch rather than a toll.\n");
            }

            return text.ToString();
        }

        // StoreyEntries, RingOf and DistancesToStoreyMiddle used to be declared here. They
        // live in MapValidator now — the first two private to the centre-path rule, the
        // third public as MapValidator.DistancesToStoreyMiddle — and the move is the point
        // rather than tidying: §12-D's centre-path rule and the 탈출 대가 report below both
        // answer 「중심까지 얼마나 남았는가」, and two implementations of that could disagree
        // about the same map — a gate that fails and a report that reassures.
        //
        // The Core version reads a ring as a constant Chebyshev radius from the zone's own
        // centre instead of dividing world metres by MapKitCatalogue.GridMetres, because
        // Core cannot see the kit. Checked against the version it replaced rather than
        // assumed equivalent: both pick out 30 storey entry points at 47.5~82.5 m on seed
        // 20260802. The first attempt did not — it found 22 at 50~80 m, because a plain
        // "outermost ring" is 8 막힌 길 pockets hanging one cell outside the 외곽 rail, not
        // the rail. See MapValidator.RingExtreme.

        private static float EdgeLength(MapGraph graph, int a, int b)
        {
            var incident = graph.IncidentEdges(a);
            for (var i = 0; i < incident.Length; i++)
            {
                if (graph.Edges[incident[i]].Touches(b))
                {
                    return graph.Edges[incident[i]].Length;
                }
            }

            return 0f;
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

            return DescentMap.DefaultSeed;
        }

        /// <summary>True when the editor was launched with this command-line flag.</summary>
        private static bool HasFlag(string flag)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A byte-level snapshot of everything a generator run is allowed to write,
        /// taken before the first write and put back when the run is rejected.
        /// <para>
        /// <b>Why the whole folder and not the one file.</b> The NavMesh asset is the
        /// escape that cost three days, but it is not the only one:
        /// <c>MapSceneBuilder.SurfaceAssets</c> mints <c>Floor_*.mat</c> and
        /// <c>Surface_*.asset</c> with <c>AssetDatabase.CreateAsset</c> during the same
        /// unconditional build pass, and any future generated thing will land in the same
        /// place. <see cref="SceneGenPaths.GeneratedRoot"/> is already defined as
        /// "everything a generated scene references … disposable, overwritable from the
        /// same seed", so snapshotting the folder catches the next escape without anyone
        /// having to remember to add it here.
        /// </para>
        /// <para>
        /// The scenes are WATCHED rather than snapshotted — their digests are recorded
        /// and compared, but they are never rewritten. A rejected run must not reach
        /// <c>SaveScene</c> at all, so a scene that changed is not something to repair
        /// quietly; it is a defect that should be shouted about.
        /// </para>
        /// <para>
        /// Empty folders are the one thing not restored: a run that created
        /// <c>Generated/NavMesh/</c> and was then rejected leaves the directory behind
        /// with no files in it. git does not track directories, so the repository is
        /// still byte-identical; the claim in the log is about files, and it is worded
        /// that way on purpose.
        /// </para>
        /// </summary>
        private sealed class GeneratedTree
        {
            private readonly Dictionary<string, byte[]> _snapshot =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);

            private readonly Dictionary<string, string> _watched =
                new Dictionary<string, string>(StringComparer.Ordinal);

            private string _digest;
            private string _navPath;
            private string _previousGeneration;

            /// <summary>Reads every file the generator may write, plus the scenes it must not.</summary>
            /// <param name="navMeshAssetPath">The bake this run will replace, named so the rollback can say whose it is.</param>
            public static GeneratedTree Capture(string navMeshAssetPath)
            {
                var tree = new GeneratedTree { _navPath = navMeshAssetPath };

                foreach (var pair in ReadFolder(SceneGenPaths.GeneratedRoot))
                {
                    tree._snapshot[pair.Key] = pair.Value;
                }

                tree._digest = Digest(tree._snapshot);

                foreach (var scene in new[]
                         {
                             SceneGenPaths.MapScene, SceneGenPaths.MatchScene, SceneGenPaths.BootstrapScene,
                         })
                {
                    tree._watched[scene] = File.Exists(scene) ? Sha(File.ReadAllBytes(scene)) : "(absent)";
                }

                // Whose bake is currently on disk. Read now because the run is about to
                // overwrite it, and a rollback that can name the generation it restored is
                // the difference between "nothing was written" and "nothing was written,
                // and what is there belongs to the scene that is there".
                tree._previousGeneration = ReadStamp(navMeshAssetPath);
                return tree;
            }

            /// <summary>
            /// Restores every file, deletes every file the run added, and then MEASURES
            /// that the folder is back to the digest it started from rather than claiming
            /// it. Returns the one line a human reads.
            /// </summary>
            public string RollBack()
            {
                var restored = new List<string>();
                var removed = new List<string>();

                foreach (var pair in _snapshot)
                {
                    if (File.Exists(pair.Key) && SameBytes(File.ReadAllBytes(pair.Key), pair.Value))
                    {
                        continue;
                    }

                    var folder = Path.GetDirectoryName(pair.Key);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    File.WriteAllBytes(pair.Key, pair.Value);
                    restored.Add(pair.Key);
                }

                foreach (var path in ReadFolder(SceneGenPaths.GeneratedRoot).Keys)
                {
                    if (_snapshot.ContainsKey(path))
                    {
                        continue;
                    }

                    File.Delete(path);
                    removed.Add(path);
                }

                if (restored.Count > 0 || removed.Count > 0)
                {
                    // ForceUpdate rather than a plain Refresh: a restored .meta carries the
                    // GUID the shipped scenes reference, and Unity has to re-read it for
                    // that mapping to come back — the same thing that happens when a
                    // deleted asset is checked out again.
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                }

                var after = ReadFolder(SceneGenPaths.GeneratedRoot);
                var digest = Digest(after);
                var identical = string.Equals(digest, _digest, StringComparison.Ordinal);

                if (!identical)
                {
                    Debug.LogError(
                        "[SceneGen] The rollback did NOT restore " + SceneGenPaths.GeneratedRoot
                        + ": digest " + _digest + " before, " + digest + " after, " + _snapshot.Count
                        + " files then, " + after.Count + " now. Treat every measurement taken against "
                        + "this tree as suspect until it is checked by hand.");
                }

                foreach (var pair in _watched)
                {
                    var now = File.Exists(pair.Key) ? Sha(File.ReadAllBytes(pair.Key)) : "(absent)";
                    if (!string.Equals(now, pair.Value, StringComparison.Ordinal))
                    {
                        Debug.LogError(
                            "[SceneGen] " + pair.Key + " CHANGED during a run that wrote nothing. A rejected "
                            + "generation must never reach SaveScene; this one did, and the scene on disk is "
                            + "not the one the last accepted generation left.");
                    }
                }

                return "nothing was written; the " + after.Count + " files under " + SceneGenPaths.GeneratedRoot
                    + " are byte-identical to before this run"
                    + (identical ? " (digest " + digest + ")" : " — THEY ARE NOT, see the error above")
                    + (restored.Count > 0 || removed.Count > 0
                        ? ", after putting back " + restored.Count + " and removing " + removed.Count
                        : string.Empty)
                    + ", so " + Path.GetFileName(_navPath) + " still belongs to "
                    + (string.IsNullOrEmpty(_previousGeneration)
                        ? "an unstamped generation (it predates the stamp, or something other than this "
                          + "generator wrote it)"
                        : _previousGeneration) + ".";
            }

            /// <summary>Every file under a folder, keyed by the project-relative path Unity uses.</summary>
            private static Dictionary<string, byte[]> ReadFolder(string folder)
            {
                var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                if (!Directory.Exists(folder))
                {
                    return files;
                }

                foreach (var path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    var key = path.Replace('\\', '/');
                    files[key] = File.ReadAllBytes(path);
                }

                return files;
            }

            /// <summary>
            /// One number for a whole tree — path, length and contents of every file, in a
            /// fixed order. Quoted in the log so two runs can be compared by eye.
            /// </summary>
            private static string Digest(Dictionary<string, byte[]> files)
            {
                var keys = new List<string>(files.Keys);
                keys.Sort(StringComparer.Ordinal);

                using (var stream = new MemoryStream())
                {
                    foreach (var key in keys)
                    {
                        var header = System.Text.Encoding.UTF8.GetBytes(key + "\n" + files[key].Length + "\n");
                        stream.Write(header, 0, header.Length);
                        stream.Write(files[key], 0, files[key].Length);
                    }

                    stream.Position = 0;
                    using (var sha = SHA256.Create())
                    {
                        return Hex(sha.ComputeHash(stream));
                    }
                }
            }

            private static string Sha(byte[] bytes)
            {
                using (var sha = SHA256.Create())
                {
                    return Hex(sha.ComputeHash(bytes));
                }
            }

            /// <summary>Twelve hex characters: enough to compare two runs, short enough to read.</summary>
            private static string Hex(byte[] hash)
            {
                var text = new System.Text.StringBuilder(12);
                for (var i = 0; i < 6 && i < hash.Length; i++)
                {
                    text.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }

            private static bool SameBytes(byte[] left, byte[] right)
            {
                if (left.Length != right.Length)
                {
                    return false;
                }

                for (var i = 0; i < left.Length; i++)
                {
                    if (left[i] != right[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
