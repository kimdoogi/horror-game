#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using HorrorGame.Core.Map;
using HorrorGame.EditorTools.Dressing;
using HorrorGame.EditorTools.Rendering;
using HorrorGame.EditorTools.SceneGen;
using HorrorGame.Gameplay.Race;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

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
        /// Layout, then dressing, then a second audit of the dressed building, then
        /// atmosphere. Stops at the first failure and returns everything logged up to
        /// that point. The audit that decides the return value is the one that ran
        /// after the dressing rebake — see the comment on it.
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

            // ── the audit again, on the building that actually loads ────────────────
            //
            // docs/BLOCKERS.md B-009 recurring through a different pass. The layout
            // stage above prints its NavMesh and player-traversal numbers BEFORE any
            // dressing exists. Then <see cref="DressingScatter"/> includes every solid
            // piece in the bake (it sets ignoreFromBuild only on the non-solid ones)
            // and rebakes to SceneGenPaths.NavMeshRoot + "/NavMesh_" + sceneName +
            // ".asset" — byte-for-byte the path MapSceneBuilder wrote. So the asset the
            // layout gate measured is overwritten by a surface with two thousand solid
            // props eroded into it, and the green audit in the log describes a building
            // with no crates in it. Two rounds read that log and believed it, which is
            // the same wrong-instrument failure as reading the DLLs for a scene defect.
            //
            // So: measure again, here, after the scatter and its rebake, and let THIS
            // audit decide the pipeline's exit code. It costs a fraction of the scatter
            // and it is the only audit whose subject is the scene the game loads.
            log.Add("── audit, after dressing (the surface the game actually loads) ──");
            var dressedConnectivity = NavMeshConnectivity.Audit(scene);
            log.Add(dressedConnectivity.Describe());
            var dressedReach = PlayerTraversal.Audit(scene);
            log.Add(dressedReach.Describe());

            var auditedAfterDressing = dressedConnectivity.Passed && dressedReach.Passed;
            if (!auditedAfterDressing)
            {
                log.Add(
                    "The layout stage's audit above passed and this one did not. The difference between them "
                    + "is the dressing, and this one is the one that counts — it read the rebaked NavMesh, "
                    + "which is the asset the scene references.");
            }

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
            return dressed && auditedAfterDressing;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  THE ROSTER — every match a different building, and every building audited
        // ═══════════════════════════════════════════════════════════════════════════
        //
        // §11's lobby has always picked a seed, agreed it with everybody, broadcast it
        // and logged it. The scene it entered was pre-baked at DescentMap.DefaultSeed, so
        // the seed decided only where runners start; the maze was 20260802 every match,
        // forever. For a race whose stated skill is 「맵을 아는 사람이 유리하다」 that makes
        // map knowledge a permanent asset rather than a per-match one, and the twentieth
        // match is the first one already solved.
        //
        // The two ways to fix it were baking N maps at author time and generating one at
        // run time. ProbeSeeds below measured both, per seed, on this machine:
        //
        //     sketch (DescentMap.Build)                    0.01 – 0.04 s
        //     geometry (prefab instantiation + first bake)  1.00 – 1.01 s
        //     dressing scatter                              3.73 – 3.89 s
        //     NavMeshSurface.BuildNavMesh, dressed          0.81 – 0.86 s   ← runtime-capable
        //     ----------------------------------------------------------
        //     what a player COULD do at match start        ≈ 5.7 s
        //
        //     §12 gate (MapValidator.Validate)             69.2 – 70.3 s
        //     NavMesh connectivity + player traversal     114.5 – 129.7 s
        //     ----------------------------------------------------------
        //     what it could NOT do                         ≈ 190 s  (3.2 min)
        //
        // <b>Generating the building at run time is affordable. Proving it is not.</b>
        // Six seconds is an ordinary loading screen. Three minutes on twenty machines
        // before every match is not a game, and cutting it means shipping a generator with
        // no gate — which is the entire reason this project can trust its maps at all. A
        // map that fails its own gate in front of twenty people is worse than a repeated
        // one. The 5.7 s figure is also the honest reason NOT to close this door forever:
        // if the audits ever get cheap, the generator is already engine-free and the
        // number that blocks route (b) is the audit, not the build. See the report on this
        // change.

        /// <summary>Folder holding one scene per pre-baked building. Regenerable, like everything the generator writes.</summary>
        public const string DescentSceneFolder = "Assets/Scenes/Descent";

        /// <summary>Scene name stem. The slot index is appended, so slot 3 is <c>Map_Descent_3</c>.</summary>
        public const string DescentScenePrefix = "Map_Descent_";

        /// <summary>
        /// Where the manifest goes. Must end in a folder called <c>Resources</c> or
        /// <c>Resources.Load</c> cannot see it in a player.
        /// </summary>
        public const string RosterResourceFolder = SceneGenPaths.GeneratedRoot + "/Resources";

        /// <summary>
        /// The seeds offered to the roster baker, in slot order.
        /// <para>
        /// <b>This list is a nomination, not a guarantee.</b> Nothing here is selectable
        /// until <see cref="BakeRoster"/> has put it through the same three gates the
        /// shipped map goes through and written a scene for it; a seed that fails is left
        /// out of the manifest, so the game gets a shorter roster rather than a building
        /// with nine islands in it. Add to this list from <see cref="ProbeSeeds"/>'s
        /// output, never from a hunch.
        /// </para>
        /// <para>
        /// <b>Why eight.</b> N is how many matches a player sees before the building
        /// repeats, and the cost is one scene and one bake each — measured on the artefact
        /// rather than estimated: <c>Map_FirstSketch_Solo.unity</c> is 42 729 620 bytes and
        /// <c>NavMesh_Map_FirstSketch.asset</c> is 293 520 bytes, so a slot is 41.0 MiB of
        /// YAML in the repository. Eight of them is 328 MiB checked in. Sixteen would buy
        /// one more fortnight of novelty for another 328 MiB of git history, and a repeat
        /// every eighth match is already past the point where a player stops recognising
        /// the floor they landed on. The count is not load-bearing anywhere: the lobby
        /// selects by position in whatever the baker published, so this list can grow or
        /// shrink without a code change.
        /// </para>
        /// <para>
        /// <b>DescentMap.DefaultSeed is last on purpose.</b> The baker stages every slot
        /// through <c>SceneGenPaths.MapScene</c> — <c>MapSceneGenerator.Generate</c> writes
        /// that one path and nothing else — so whichever seed is baked last is the one left
        /// sitting in the shipped map scene. Ending on the default means a roster bake
        /// leaves that pair holding exactly the building it held before, rather than
        /// whichever candidate happened to be at the end of the list.
        /// </para>
        /// <para>
        /// <b>How far each of these has been screened, exactly.</b> All eight came out of a
        /// 96-seed sweep of §12's graph gate — run 2026-08-04 against a working tree whose
        /// <c>MapValidator</c> was identical to commit 1b4b267, which matters because that
        /// file was being changed the same afternoon (see <see cref="ProbeSeeds"/>). 96/96
        /// writable, 0 hard failures, 0 exceptions: every seed failed exactly the same
        /// already-waived rules and none failed anything else. The graph gate does not
        /// discriminate between seeds, which is precisely why the NavMesh and traversal
        /// gates are the ones that matter. The
        /// first three have additionally been through <see cref="ProbeSeeds"/>, which is
        /// the whole author-time gate: geometry, dressing, bake, connectivity audit and
        /// player-traversal audit. All three passed. The last is what ships today. The four
        /// in between have had the graph gate only, and are nominations rather than known
        /// buildings — <see cref="BakeRoster"/> screens every one of them again and drops
        /// whichever fail, so an unscreened seed can cost a roster slot but cannot put a
        /// bad building in the game.
        /// </para>
        /// </summary>
        public static readonly int[] RosterSeeds =
        {
            // Through the full author-time gate — ProbeSeeds, 2026-08-04, all PASS.
            463793241,
            1246502161,
            143331277,

            // Graph gate only. BakeRoster is what will decide these.
            5537973,
            377221360,
            1290368555,
            203597007,

            // Last on purpose — see above. Also the building that ships today.
            DescentMap.DefaultSeed,
        };

        /// <summary>Bakes every slot in <see cref="RosterSeeds"/> and reports in the console.</summary>
        [MenuItem("HorrorGame/Scene Gen/Bake Descent Roster (every match a different building)", priority = 11)]
        public static void BakeRosterMenu()
        {
            if (!BakeRoster(RosterSeeds, DressingScatter.DefaultSeed, 0, out var report))
            {
                EditorUtility.DisplayDialog("Descent roster bake failed", report, "OK");
                Debug.LogError("[MapPipeline]\n" + report);
                return;
            }

            Debug.Log("[MapPipeline]\n" + report);
        }

        /// <summary>
        /// Batch entry point for the roster bake. Non-zero when any slot failed its gates,
        /// so a CI job cannot ship a roster that is quietly one building short.
        /// <para>
        /// <c>-rosterSeeds a,b,c</c> bakes that list instead of <see cref="RosterSeeds"/>.
        /// It exists because a slot costs about ten minutes of gate and the roster is
        /// meant to GROW: a three-slot bake finishes in half an hour and proves the second
        /// match differs from the first, where the full nomination list is an hour and a
        /// half. Nothing downstream is sized by this — the lobby selects by position in
        /// whatever the manifest holds — so a short roster is a smaller roster and not a
        /// different feature. The nomination list itself is left alone, because editing it
        /// to bake fewer would lose the record of what has been screened.
        /// </para>
        /// <code>
        /// Unity -batchmode -quit -nographics -projectPath . \
        ///   -executeMethod HorrorGame.EditorTools.MapPipeline.BakeRosterFromCommandLine \
        ///   [-rosterSeeds 463793241,1246502161,20260802] [-dressSeed 4703] [-atmoTier 0]
        /// </code>
        /// </summary>
        public static void BakeRosterFromCommandLine()
        {
            try
            {
                var dressSeed = IntArg("-dressSeed", DressingScatter.DefaultSeed);
                var tier = IntArg("-atmoTier", 0);

                var chosen = SeedListArg("-rosterSeeds");
                var seeds = chosen.Count > 0 ? chosen.ToArray() : RosterSeeds;

                var ok = BakeRoster(seeds, dressSeed, tier, out var report);
                Debug.Log("[MapPipeline]\n" + report);
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[MapPipeline] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Bakes one scene per seed, publishes only the ones that pass every gate, and
        /// writes the manifest the lobby reads.
        /// <para>
        /// <b>This method IS the enforcement.</b> "The audit bar must hold for every seed
        /// that can be selected" is not a promise kept by a list somebody curated — it is
        /// kept by the fact that a building reaches the manifest only by surviving, in
        /// order:
        /// </para>
        /// <list type="number">
        /// <item><b>§12's checklist</b> and <b>the NavMesh connectivity audit</b> and
        /// <b>the player-traversal audit</b>, inside <c>MapSceneGenerator.Generate</c>,
        /// which rolls the whole generation back rather than writing a map that failed
        /// one.</item>
        /// <item><b>The dressing pass and a second connectivity + traversal audit</b>, in
        /// <see cref="Regenerate"/>, taken on the rebaked surface — because the dressing
        /// rebakes over the same asset and one piece of scenery once sealed a dead end on
        /// B7 and turned a green map into nine islands.</item>
        /// <item><b>A THIRD connectivity + traversal audit, on the copy</b>, below. The
        /// slot scene is a copy of the staging scene with its NavMesh reference moved to a
        /// copy of the bake, and that repointing is exactly the kind of step that can
        /// leave a scene pathing on a surface belonging to a different building. The audit
        /// that decides whether a slot is published is the one that read the file the game
        /// will open.</item>
        /// </list>
        /// <para>
        /// <b><c>-forceWrite</c> is refused outright.</b> That flag exists so somebody can
        /// walk a broken map, and <c>Generate</c> returns true for a forced write. A forced
        /// building in the roster would be a building the lobby may pick for twenty people,
        /// so the whole bake stops rather than publishing one.
        /// </para>
        /// </summary>
        /// <param name="seeds">Candidate seeds, in slot order. Publishing order follows this.</param>
        /// <param name="dressSeed">Fixes the scatter. The same for every slot, so the buildings differ by LAYOUT.</param>
        /// <param name="tierIndex">§07 row whose environment gets baked in.</param>
        /// <param name="report">Everything that happened, slot by slot.</param>
        public static bool BakeRoster(int[] seeds, int dressSeed, int tierIndex, out string report)
        {
            var log = new List<string>();
            var published = new List<DescentBuilding>();
            var refused = 0;

            if (HasFlag("-forceWrite"))
            {
                report = "-forceWrite is on the command line. A roster is a list of buildings the lobby may "
                    + "put twenty people into, and a forced generation is one with a named defect in it, so "
                    + "nothing was baked. Run the roster without it, or fix what is failing.";
                return false;
            }

            if (seeds == null || seeds.Length == 0)
            {
                report = "No candidate seeds, so there is nothing to bake.";
                return false;
            }

            // ── the seed that is left behind ─────────────────────────────────────────
            //
            // Every slot is staged THROUGH SceneGenPaths.MapScene and its solo assembly,
            // because those are the only two paths the generator writes. So the last seed
            // in this list is the building the shipped pair is holding when the bake ends
            // — and a bake that ends on anything else has quietly replaced the map every
            // other measurement in this repository was taken on, with no line anywhere
            // saying so. RosterSeeds ends on DescentMap.DefaultSeed for exactly this
            // reason; a hand-picked -rosterSeeds list can forget to. Said out loud rather
            // than enforced: rebaking the shipped map at a new seed is a legitimate thing
            // to want, and it is not a legitimate thing to do by accident.
            if (seeds[seeds.Length - 1] != DescentMap.DefaultSeed)
            {
                log.Add(
                    "NOTE — this bake ends on seed " + seeds[seeds.Length - 1] + ", not DescentMap.DefaultSeed ("
                    + DescentMap.DefaultSeed + "). " + SceneGenPaths.MapScene + " and " + SceneGenPaths.MatchScene
                    + " will be left holding that building instead of the one they hold now. Put "
                    + DescentMap.DefaultSeed + " last if that is not what you meant.");
            }

            SceneGenPaths.EnsureFolder(DescentSceneFolder);
            SceneGenPaths.EnsureFolder(RosterResourceFolder);

            var whole = Stopwatch.StartNew();
            var staged = new List<(int Slot, int Seed)>();

            // ── phase 1 · bake every slot ────────────────────────────────────────────
            for (var slot = 0; slot < seeds.Length; slot++)
            {
                var seed = seeds[slot];
                var clock = Stopwatch.StartNew();

                log.Add(string.Empty);
                log.Add("════ slot " + slot + " · seed " + seed + " ════");

                if (!Regenerate(seed, dressSeed, tierIndex, out var slotReport))
                {
                    refused++;
                    log.Add(slotReport);
                    log.Add(
                        "REFUSED. Seed " + seed + " did not pass its gates, so no scene was written for it and it "
                        + "is NOT in the manifest. The roster is one building shorter; nobody can be sent here.");
                    continue;
                }

                log.Add(slotReport);

                // The playable assembly. The map scene is layout; this is the one 시작
                // loads, with the rig, the creature and the MatchDirector in it, and it is
                // therefore the one a slot has to be a copy of.
                SoloPlaytest.Build();

                if (!StageSlot(slot, out var staging))
                {
                    refused++;
                    log.Add("REFUSED while staging: " + staging);
                    continue;
                }

                staged.Add((slot, seed));
                log.Add(
                    "staged " + DescentScenePrefix + slot + "  ("
                    + clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s) · " + staging);
            }

            // ── phase 2 · audit the files in the state they will ship in ─────────────
            //
            // NOT inside the loop above, and the reason is a trap this project has fallen
            // into three times under other names. Regenerate ends with
            // AtmosphereSetup.ApplyEnvironmentToMapScenes, which walks
            // AssetDatabase.FindAssets("t:Scene", ["Assets/Scenes"]) — RECURSIVELY — and
            // re-saves every scene whose file name starts with "Map_". Every slot scene
            // does. So a slot audited inside the loop is a slot that the NEXT slot's
            // atmosphere pass then rewrites, and the audit that passed would have been
            // taken on a file that no longer exists in that form. What it changes is only
            // RenderSettings, which cannot move a navmesh — but "it cannot matter" is the
            // sentence that produced B-009. The audit that decides publication reads the
            // artefact after everything that writes to it has finished.
            log.Add(string.Empty);
            log.Add("════ audit, on the slot scenes as they will ship ════");

            for (var i = 0; i < staged.Count; i++)
            {
                if (!AuditSlot(staged[i].Slot, staged[i].Seed, out var building, out var failure, out var note))
                {
                    refused++;
                    log.Add("REFUSED at publication: slot " + staged[i].Slot + " — " + failure);
                    continue;
                }

                published.Add(building);
                log.Add("PUBLISHED as roster index " + (published.Count - 1) + " → " + building + "  ·  " + note);
            }

            var manifest = DescentRoster.Format(published);
            var manifestPath = RosterResourceFolder + "/" + DescentRoster.ManifestResourceName + ".txt";
            File.WriteAllText(manifestPath, manifest);
            AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceUpdate);

            RegisterDescentScenes(published);
            AssetDatabase.SaveAssets();

            // Read back what was just written rather than trusting the list in memory: the
            // fingerprint printed here is the one a running game will compute, and if the
            // import did not take, this is where it shows.
            DescentRoster.Reload();

            log.Add(string.Empty);
            log.Add("════ roster ════");
            log.Add(
                published.Count + " building(s) published, " + refused + " refused, in "
                + whole.Elapsed.TotalMinutes.ToString("F1", CultureInfo.InvariantCulture) + " min.");
            log.Add("manifest : " + manifestPath);
            log.Add("read back: " + DescentRoster.Count + " building(s), fingerprint "
                + DescentRoster.Fingerprint.ToString("X8"));
            log.Add(
                "A player sees a repeat every " + Math.Max(1, DescentRoster.Count)
                + " match(es). RaceLobby refuses to host if any of these is missing from Build Settings.");

            report = string.Join("\n", log);

            // A short roster is not a success. Every refusal is a seed somebody nominated
            // that the gates threw out, and the honest exit code says so — otherwise the
            // list silently shrinks one seed at a time and nobody notices until the
            // building repeats every other match.
            return refused == 0 && published.Count == seeds.Length && DescentRoster.Count == published.Count;
        }

        /// <summary>
        /// Copies the staging pair into a slot and repoints the copy at its own bake.
        /// Writes the slot; decides nothing.
        /// </summary>
        private static bool StageSlot(int slot, out string note)
        {
            var stagingScene = SceneGenPaths.MatchScene;
            var stagingNav = SceneGenPaths.NavMeshRoot + "/NavMesh_"
                + Path.GetFileNameWithoutExtension(SceneGenPaths.MapScene) + ".asset";

            var sceneName = DescentScenePrefix + slot;
            var scenePath = DescentSceneFolder + "/" + sceneName + ".unity";
            var navPath = SceneGenPaths.NavMeshRoot + "/NavMesh_" + sceneName + ".asset";

            AssetDatabase.DeleteAsset(scenePath);
            AssetDatabase.DeleteAsset(navPath);

            if (!AssetDatabase.CopyAsset(stagingScene, scenePath))
            {
                note = "could not copy " + stagingScene + " to " + scenePath;
                return false;
            }

            if (!AssetDatabase.CopyAsset(stagingNav, navPath))
            {
                note = "could not copy " + stagingNav + " to " + navPath;
                return false;
            }

            AssetDatabase.Refresh();

            // ── the repointing, and why the copy needs its own bake ──────────────────
            //
            // A copied scene still references the STAGING bake by GUID, and the next slot
            // overwrites that file. Left alone, every slot would path on whichever
            // building was baked last — eight different mazes over one navmesh, which is
            // the exact shape of B-009 (an audit that measured a surface the scene did not
            // reference) multiplied by eight.
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var surface = FindSurface(scene);
            if (surface == null)
            {
                note = scenePath + " has no NavMeshSurface, so §06's creature cannot move in it.";
                return false;
            }

            var data = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navPath);
            if (data == null)
            {
                note = navPath + " did not import as NavMeshData.";
                return false;
            }

            surface.navMeshData = data;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();

            note = "navmesh repointed to " + navPath;
            return true;
        }

        /// <summary>
        /// Opens a staged slot, audits it, and reads the generation it will claim to be.
        /// This is the call that decides whether a building is selectable.
        /// </summary>
        /// <param name="note">What had to be stood down to take the measurement. Never empty in practice.</param>
        private static bool AuditSlot(int slot, int seed, out DescentBuilding building, out string failure, out string note)
        {
            building = default;

            var sceneName = DescentScenePrefix + slot;
            var scenePath = DescentSceneFolder + "/" + sceneName + ".unity";

            if (!File.Exists(scenePath))
            {
                failure = scenePath + " is not on disk.";
                note = string.Empty;
                return false;
            }

            // Opened fresh so the audit reads the file rather than whatever the editor
            // still had in memory from the pass that wrote it.
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // Never saved — see StandTheRunnersDown for why the file must keep its rig.
            note = StandTheRunnersDown(scene);

            var connectivity = NavMeshConnectivity.Audit(scene);
            var reach = PlayerTraversal.Audit(scene);
            if (!connectivity.Passed || !reach.Passed)
            {
                failure = "the slot scene did not pass its own audit, so it is not a building anybody may be sent "
                    + "to.\n" + note + "\n" + connectivity.Describe() + reach.Describe();
                return false;
            }

            var generation = FindGenerationStamp(scene);
            if (generation == null)
            {
                failure = scenePath + " carries no " + DescentRoster.GenerationStampPrefix
                    + " stamp, so a running game cannot prove it is in this building rather than another one "
                    + "with the same file name.";
                return false;
            }

            if (generation.IndexOf("forced", StringComparison.Ordinal) >= 0)
            {
                failure = scenePath + " is stamped " + generation + " — a generation that was forced past a gate. "
                    + "A roster may not offer one.";
                return false;
            }

            building = new DescentBuilding(seed, sceneName, generation);
            failure = string.Empty;

            // The numbers, on the way OUT as well as on the way to a refusal. This is the
            // audit that decides publication — the only one whose subject is the file the
            // game opens — and until now a passing slot printed no measurement at all, so
            // the bake log's strongest line was the one a reader could not check. A green
            // number nobody wrote down is the shape of every defect in this project's
            // history.
            note = note + "\n    " + OneLine(connectivity.Describe()) + "\n    " + OneLine(reach.Describe());
            return true;
        }

        /// <summary>
        /// Switches off every <c>CharacterController</c> in an opened slot scene, in
        /// memory and without saving, and names what it switched off.
        /// <para>
        /// <b>This is the difference between measuring the building and measuring a
        /// runner.</b> <c>SoloPlaytest.Build</c> parks the assembled rig on the first
        /// PlayerSpawn it finds (<c>rig.transform.SetPositionAndRotation(spawn.position,
        /// spawn.rotation)</c>) and gives it a <c>CharacterController</c>.
        /// <c>PlayerTraversal</c> then asks, of every start marker,
        /// <c>Physics.CheckCapsule(..., ~0, QueryTriggerInteraction.Ignore)</c> — "can the
        /// player's capsule stand here" — and the rig's own controller answers no. The
        /// audit collides with its own subject and reports the start as somewhere a runner
        /// "starts the match inside the building".
        /// </para>
        /// <para>
        /// <b>Measured, not argued.</b> On the shipped pair at gen-20260805-002808, taken
        /// over the SAME baked surface (guid 26ffd78e0ece1459686bbf4580765605, referenced
        /// by both files): <c>Map_FirstSketch.unity</c>, which has no rig in it, reports
        /// PlayerReach PASS · starts 36/36 · B1 18 077 walkable places.
        /// <c>Map_FirstSketch_Solo.unity</c>, which is the same building plus the rig,
        /// reports FAIL · starts 34/36 · B1 18 065 — twelve places fewer, all of them under
        /// the rig, and the two starts lost are PlayerSpawn_10 and PlayerSpawn_11, which
        /// are both at (23.75, 0.00, 6.25), which is where <c>SoloPlaytest</c> put it. That
        /// is the whole of the difference between the two reports. It is also why the
        /// 2026-08-05 bake staged eight slots and published none: this audit is the first
        /// one in the pipeline whose subject is the solo assembly rather than the layout,
        /// so it was the first to meet the rig.
        /// </para>
        /// <para>
        /// <b>Why here and why nothing is saved.</b> The rig belongs in the file — it is
        /// the player, and <c>MatchDirector.MovePlayerToSpawn</c> moves it onto a marker at
        /// <c>Start</c> whatever position it was serialised at, so moving it in the artefact
        /// would fix nothing and would put a second author on <c>SoloPlaytest</c>'s
        /// placement. What must not happen is a GATE that reads the runner as if it were
        /// the level. The real repair belongs in <c>PlayerTraversal</c>, which should not
        /// count a body it is simulating as geometry — see the report on this change; until
        /// that lands, <c>PlayerReachAudit.AuditBatch</c> keeps failing the solo scene for
        /// this reason and only this reason.
        /// </para>
        /// <para>
        /// <b>It is a record, not a silence.</b> The returned line goes into the bake log on
        /// every slot, published or refused, naming each body and where it was standing. A
        /// gate that quietly excludes something is how a building with nine islands gets
        /// shipped; a gate that prints what it excluded is one a reader can disagree with.
        /// Nothing else is touched: props, walls, doors and the creature's own collider all
        /// stay in, so a crate that seals a start is still a refusal.
        /// </para>
        /// </summary>
        private static string StandTheRunnersDown(UnityEngine.SceneManagement.Scene scene)
        {
            var stood = new List<string>();
            var roots = scene.GetRootGameObjects();

            for (var i = 0; i < roots.Length; i++)
            {
                var bodies = roots[i].GetComponentsInChildren<CharacterController>(includeInactive: true);
                for (var k = 0; k < bodies.Length; k++)
                {
                    if (!bodies[k].enabled)
                    {
                        continue;
                    }

                    // Disabling a CharacterController takes it out of physics queries —
                    // it is a Collider, and Collider.enabled is what CheckCapsule and
                    // CapsuleCast honour. Nothing is destroyed and nothing is saved.
                    bodies[k].enabled = false;
                    stood.Add(bodies[k].name + " at "
                        + bodies[k].transform.position.ToString("F2", CultureInfo.InvariantCulture));
                }
            }

            if (stood.Count == 0)
            {
                return "no runner body in this scene, so the audit below is of the building alone.";
            }

            // The audit reads Physics directly and the transforms have not moved, but the
            // collider set has; syncing costs nothing and removes the question.
            Physics.SyncTransforms();

            return "stood " + stood.Count + " runner body/bodies down for the measurement (they are in the file "
                + "and nothing was saved): " + string.Join(", ", stood)
                + ". A CharacterController parked on a PlayerSpawn makes PlayerTraversal answer its own "
                + "question — see StandTheRunnersDown.";
        }

        /// <summary>
        /// Puts the descent slots into Build Settings without taking over the base list.
        /// <para>
        /// <c>MapSceneGenerator.RegisterScenes</c> rewrites the list WHOLESALE from three
        /// hard-coded paths, so it is called first and appended to rather than replaced:
        /// one owner for "which scenes does the shipped game need", and this adds the ones
        /// it cannot know about. It also means a plain map regeneration silently drops
        /// every slot again — which is why <c>RaceLobby.VerifyRoster</c> refuses to host
        /// when a listed building is not in the build.
        /// </para>
        /// </summary>
        private static void RegisterDescentScenes(List<DescentBuilding> published)
        {
            MapSceneGenerator.RegisterScenes();

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (var i = 0; i < published.Count; i++)
            {
                var path = DescentSceneFolder + "/" + published[i].SceneName + ".unity";
                if (!File.Exists(path))
                {
                    continue;
                }

                var already = false;
                for (var k = 0; k < scenes.Count; k++)
                {
                    if (string.Equals(scenes[k].path, path, StringComparison.Ordinal))
                    {
                        already = true;
                        break;
                    }
                }

                if (!already)
                {
                    scenes.Add(new EditorBuildSettingsScene(path, enabled: true));
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  THE SCREEN — what a candidate seed costs, and whether it survives the gates
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds each candidate seed's geometry, dresses it, bakes it and audits it —
        /// and saves NOTHING.
        /// <para>
        /// Two jobs, one method, because they are the same measurement. It is the screen a
        /// seed has to survive before it is worth putting in <see cref="RosterSeeds"/>, and
        /// it is the instrument that answers "what would generating this at run time
        /// cost": the phases it times are exactly the phases a runtime generator would have
        /// to run on every machine at the start of every match, and the audits it runs are
        /// the ones a runtime generator has no way to act on.
        /// </para>
        /// <code>
        /// Unity -batchmode -quit -nographics -projectPath . \
        ///   -executeMethod HorrorGame.EditorTools.MapPipeline.ProbeSeedsFromCommandLine \
        ///   -probeSeeds 463793241,1246502161 [-dressSeed 4703]
        /// </code>
        /// </summary>
        public static void ProbeSeedsFromCommandLine()
        {
            try
            {
                var dressSeed = IntArg("-dressSeed", DressingScatter.DefaultSeed);
                var seeds = SeedListArg("-probeSeeds");

                if (seeds.Count == 0)
                {
                    Debug.LogError("[MapPipeline] -probeSeeds <a,b,c> is required and was not given.");
                    EditorApplication.Exit(2);
                    return;
                }

                var ok = ProbeSeeds(seeds, dressSeed, out var report);
                Debug.Log("[MapPipeline]\n" + report);
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[MapPipeline] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Runs the whole author-time gate on each seed in an unsaved scene, timing every
        /// phase. Returns true when every seed passed.
        /// </summary>
        public static bool ProbeSeeds(IReadOnlyList<int> seeds, int dressSeed, out string report)
        {
            var log = new List<string>();
            var allPassed = true;

            log.Add("── seed screen · " + seeds.Count + " candidate(s), dressing seed " + dressSeed + " ──");
            log.Add(
                "Every phase below is timed. A runtime generator would pay geometry + bake on every machine at "
                + "the start of every match, and would have nothing to do with the three audit lines.");

            log.Add(string.Empty);
            log.Add(CheckRosterFormat());

            for (var i = 0; i < seeds.Count; i++)
            {
                var seed = seeds[i];
                log.Add(string.Empty);
                log.Add("──── seed " + seed + " ────");

                var sketchClock = Stopwatch.StartNew();
                MapSketchResult map;
                try
                {
                    map = DescentMap.Build(seed);
                }
                catch (Exception ex)
                {
                    allPassed = false;
                    log.Add("  DescentMap.Build THREW " + ex.GetType().Name + ": " + ex.Message);
                    continue;
                }

                sketchClock.Stop();

                // ── MapValidator.Validate, and NOT MapQualityReport.Measure ──────────
                //
                // Measure is what MapSceneGenerator.Generate calls, so taking anything
                // less looks like a relaxed bar. It is not, and the difference is
                // checkable rather than argued: Generate reads exactly one thing off the
                // report to decide — quality.Buildable, which is defined as
                // Validation.Passed and nothing else. Everything else Measure computes
                // goes into Describe(), a log line.
                //
                // Searched across the whole project rather than assumed: MapQualityReport
                // .BreakSpacings has no reader outside DescribeSpacing() in its own file,
                // and Measure has two callers — ReportQualityMenu, whose job IS to print
                // the report, and Generate. So the expensive member gates nothing
                // anywhere.
                //
                // It costs: Measure is 144.1 s per seed and Validate is 44-70 s of that,
                // the remainder being MeasureBreakSpacing's all-pairs MapGraph.PathLength
                // over every sight-breaking corner. Screening on the whole report would
                // pay ~75-100 s per seed for prose, which is what turns "add another
                // building" into an argument about time.
                //
                // NOTE ON PROVENANCE. The pass/fail this line prints depends on which
                // MapValidator is on disk, and that file was being edited by another
                // change on 2026-08-04 (two rules retired as obsolete after the pivot).
                // The screen's own DECISION does not depend on it — a §12 failure that is
                // in MapSceneGenerator.KnownFailingRules is written anyway, and every seed
                // measured was writable both before and after — but a §12 TALLY taken from
                // this log is only comparable against the validator it ran with. Quote it.
                var validateClock = Stopwatch.StartNew();
                var validation = MapValidator.Validate(map.Graph);
                validateClock.Stop();

                // A fresh scene per seed, never saved. The scene name is what
                // MapSceneBuilder derives its bake asset path from, so it is given one
                // that belongs to nothing shipped.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SceneGenPaths.EnsureFolder(SceneGenPaths.GeneratedRoot);
                SceneGenPaths.EnsureFolder(SceneGenPaths.NavMeshRoot);

                var buildClock = Stopwatch.StartNew();
                var root = MapSceneBuilder.Build(map, ProbeSceneName);
                buildClock.Stop();

                var scene = EditorSceneManager.GetActiveScene();

                var dressClock = Stopwatch.StartNew();
                var dressed = DressingScatter.Run(dressSeed, out var dressing);
                dressClock.Stop();

                // The bake on its own, after the dressing is in — this is the number a
                // runtime generator would have to live with, because NavMeshSurface.BuildNavMesh
                // is the same call in a player as it is here.
                var surface = root != null ? root.GetComponentInChildren<NavMeshSurface>(true) : null;
                var bakeClock = Stopwatch.StartNew();
                if (surface != null)
                {
                    surface.BuildNavMesh();
                }

                bakeClock.Stop();

                var auditClock = Stopwatch.StartNew();
                var connectivity = NavMeshConnectivity.Audit(scene);
                var reach = PlayerTraversal.Audit(scene);
                auditClock.Stop();

                var passed = dressed && connectivity.Passed && reach.Passed;
                allPassed &= passed;

                log.Add("  " + (passed ? "PASS" : "FAIL") + "  seed " + seed);
                log.Add("    sketch   " + Secs(sketchClock) + "  " + map.Tiles.Length + " tiles · "
                    + map.Markers.Length + " markers · " + map.Graph.Nodes.Length + " nodes");
                // The rule COUNT is printed beside the verdict on purpose. A §12 tally is
                // only comparable against the validator that produced it, and the number
                // of rules that ran is the cheapest honest way to say which one that was —
                // a log reading "0 failures / 12 rules" and one reading "0 failures / 14
                // rules" are not the same claim, and without this they look identical.
                log.Add("    §12 gate " + Secs(validateClock) + "  passed=" + validation.Passed
                    + " failures=" + validation.Failures.Length + " [" + RuleIds(validation)
                    + "] over " + validation.Results.Length + " rules");
                log.Add("    geometry " + Secs(buildClock) + "  (prefab instantiation + first bake)");
                log.Add("    dressing " + Secs(dressClock) + "  ok=" + dressed);
                log.Add("    BAKE     " + Secs(bakeClock) + "  NavMeshSurface.BuildNavMesh, dressed building");
                log.Add("    audits   " + Secs(auditClock) + "  connectivity + player traversal");
                log.Add("    " + OneLine(connectivity.Describe()));
                log.Add("    " + OneLine(reach.Describe()));

                if (!dressed)
                {
                    log.Add("    dressing said: " + OneLine(dressing));
                }
            }

            // The two bakes this screen leaves behind. Both are files this method created —
            // MapSceneBuilder names its asset after ProbeSceneName and DressingScatter
            // names its rebake after the (unsaved, therefore unnamed) scene — and neither
            // is referenced by anything, because no scene was saved. Removed so a probe run
            // leaves the generated tree exactly as it found it.
            foreach (var leftover in new[]
            {
                SceneGenPaths.NavMeshRoot + "/NavMesh_" + ProbeSceneName + ".asset",
                SceneGenPaths.NavMeshRoot + "/NavMesh_Map.asset",
                SceneGenPaths.NavMeshRoot + "/NavMesh_Untitled.asset",
            })
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(leftover) != null)
                {
                    AssetDatabase.DeleteAsset(leftover);
                    log.Add("cleaned up " + leftover);
                }
            }

            log.Add(string.Empty);
            log.Add(allPassed
                ? "Every candidate passed. They may go in MapPipeline.RosterSeeds; the bake will gate them again."
                : "At least one candidate failed. It must NOT go in MapPipeline.RosterSeeds.");

            report = string.Join("\n", log);
            return allPassed;
        }

        /// <summary>
        /// Round-trips a made-up roster through <c>DescentRoster</c>'s own formatter and
        /// parser and takes the fingerprint of both.
        /// <para>
        /// One formatter, one parser, one fingerprint, and this is what proves they agree.
        /// The manifest is the ONLY thing making twenty machines choose the same building,
        /// so a formatter that writes something its own parser rejects would leave every
        /// client with an empty roster and every match back on one building — silently,
        /// because an empty roster is also the legitimate state of a build that has never
        /// baked one. Run here rather than in a test because this file is the one that
        /// writes the manifest, and because it costs a millisecond.
        /// </para>
        /// </summary>
        private static string CheckRosterFormat()
        {
            var sample = new List<DescentBuilding>
            {
                new DescentBuilding(20260802, "Map_Descent_0", "gen-20260804-201542-seed20260802"),
                new DescentBuilding(-1, "Map Descent 1 with spaces", "gen-x"),
            };

            var text = DescentRoster.Format(sample);
            if (!DescentRoster.TryParse(text, out var back, out var failure))
            {
                return "ROSTER FORMAT BROKEN — DescentRoster.Format wrote something TryParse refuses: " + failure;
            }

            if (back.Length != sample.Count)
            {
                return "ROSTER FORMAT BROKEN — wrote " + sample.Count + " building(s), read back " + back.Length + ".";
            }

            for (var i = 0; i < back.Length; i++)
            {
                if (back[i].Seed != sample[i].Seed
                    || !string.Equals(back[i].SceneName, sample[i].SceneName, StringComparison.Ordinal)
                    || !string.Equals(back[i].Generation, sample[i].Generation, StringComparison.Ordinal))
                {
                    return "ROSTER FORMAT BROKEN — building " + i + " came back as " + back[i]
                        + " instead of " + sample[i] + ".";
                }
            }

            var one = DescentRoster.FingerprintOf(text);
            var two = DescentRoster.FingerprintOf(DescentRoster.Format(back));
            if (one != two)
            {
                return "ROSTER FINGERPRINT BROKEN — the same roster hashed to " + one.ToString("X8")
                    + " and " + two.ToString("X8") + " across a round trip.";
            }

            return "roster format: 2 buildings written, parsed back identical, fingerprint " + one.ToString("X8")
                + " both times (negative seed and a scene name with spaces included on purpose).";
        }

        /// <summary>Rule ids of everything §12 failed, so a screen log names the rule rather than a count.</summary>
        private static string RuleIds(MapValidationReport validation)
        {
            var ids = new List<string>();
            for (var i = 0; i < validation.Failures.Length; i++)
            {
                ids.Add(validation.Failures[i].RuleId);
            }

            return string.Join(", ", ids);
        }

        /// <summary>
        /// Scene name the screen builds into.
        /// <para>
        /// Deliberately not any shipped scene's name: <c>MapSceneBuilder.BakeNavMesh</c>
        /// derives its asset path from this string, so a probe run must not be able to
        /// overwrite the bake the shipped map references.
        /// </para>
        /// </summary>
        private const string ProbeSceneName = "Descent_SeedProbe";

        private static NavMeshSurface? FindSurface(UnityEngine.SceneManagement.Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var surface = roots[i].GetComponentInChildren<NavMeshSurface>(true);
                if (surface != null)
                {
                    return surface;
                }
            }

            return null;
        }

        /// <summary>
        /// The generation a scene was committed as, read off the stamp object
        /// <c>MapSceneGenerator.Commit</c> leaves in it. Null when there is none.
        /// </summary>
        private static string? FindGenerationStamp(UnityEngine.SceneManagement.Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var found = FindGenerationStamp(roots[i].transform);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string? FindGenerationStamp(Transform node)
        {
            if (node.name.StartsWith(DescentRoster.GenerationStampPrefix, StringComparison.Ordinal))
            {
                return node.name.Substring(DescentRoster.GenerationStampPrefix.Length);
            }

            for (var i = 0; i < node.childCount; i++)
            {
                var found = FindGenerationStamp(node.GetChild(i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string Secs(Stopwatch watch) =>
            watch.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture).PadLeft(8) + " s";

        /// <summary>Squeezes a multi-line audit report onto one line so a slot fits in a scannable log.</summary>
        private static string OneLine(string text) =>
            (text ?? string.Empty).Replace("\r", string.Empty).Replace('\n', ' ').Trim();

        private static bool HasFlag(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<int> SeedListArg(string flag)
        {
            var found = new List<int>();
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = args[i + 1].Split(',');
                for (var k = 0; k < parts.Length; k++)
                {
                    if (int.TryParse(
                            parts[k].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        found.Add(parsed);
                    }
                }

                break;
            }

            return found;
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
