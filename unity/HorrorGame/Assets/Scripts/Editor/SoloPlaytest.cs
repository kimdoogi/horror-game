#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text;
using HorrorGame.Core.Roles;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.PlayerEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// An alias, not `using HorrorGame.Gameplay.Player`: that namespace holds fifteen
// Player* types, this file already imports six namespaces, and `using System` on top
// of `using UnityEngine` would make the bare name `Object` ambiguous. One name is
// needed from it, so one name is imported.
using PlayerAnimatorDriver = HorrorGame.Gameplay.Player.PlayerAnimatorDriver;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Turns the generated map into a match one person can press Play on.
    /// <para>
    /// The scene generator produces geometry, lights, a baked NavMesh and spawn
    /// markers — but no player, no monster and no match, because in a real session
    /// those are spawned by the host over the network (§13). That is correct for
    /// shipping and useless for the questions §14 puts first. Answering them should not
    /// require a lobby, two instances and a working transport.
    /// </para>
    /// <para>
    /// So this builds a throwaway scene beside the generated one: the map, a player at
    /// a §12 spawn marker, a monster at its own, and a <see cref="MatchDirector"/> that
    /// owns §01's whole loop — the clock, §03's clue chain and objective, §08's loot
    /// and shop, and §02's verdict. It never edits the generated scene, so regenerating
    /// the map cannot lose hand-placed work — there is none to lose.
    /// </para>
    /// <para>
    /// This assembly (<c>Assembly-CSharp-Editor</c>) is the only place this can live:
    /// the player rig is behind an asmdef and the monster and the match are not, and a
    /// predefined editor assembly is the one thing that can reference all of them.
    /// </para>
    /// </summary>
    public static class SoloPlaytest
    {
        private const string MapScenePath = "Assets/Scenes/Map_FirstSketch.unity";
        private const string SoloScenePath = "Assets/Scenes/Map_FirstSketch_Solo.unity";
        private const string MonsterModelPath = "Assets/Models/Characters/Monster.fbx";

        /// <summary>
        /// Only a fallback. The rig's own model is normally found by asking the instantiated
        /// prefab where it came from, so this file cannot disagree with the one
        /// <see cref="PlayerFeelHarnessMenu.BuildRig"/> actually put in the scene — that
        /// disagreement is precisely how a wiring pass ends up wiring a file nobody loads.
        /// </summary>
        private const string PlayerModelPathFallback = "Assets/Models/Player/Runner.fbx";

        /// <summary>
        /// §05's nine poses: the serialized field on <see cref="PlayerAnimatorDriver"/> and
        /// the FBX take that fills it. The names are the export contract with
        /// <c>tools/blender/gen_player.py</c>, and they are listed here rather than derived
        /// from <see cref="PlayerAnimationState"/> because the field names are what a scene
        /// file actually contains — which is what the audit below reads back.
        /// </summary>
        private static readonly string[,] AnimationSlots =
        {
            { "_idle", "Idle" },
            { "_walk", "Walk" },
            { "_run", "Run" },
            { "_crouch", "Crouch" },
            { "_crouchWalk", "CrouchWalk" },
            { "_carry", "Carry" },
            { "_carryIdle", "CarryIdle" },
            { "_carryHeavy", "CarryHeavy" },
            { "_death", "Death" },
        };

        /// <summary>
        /// Every reference on <see cref="PlayerAnimatorDriver"/> that has to be non-zero in
        /// the saved scene for the player to stop sliding: the Animator plus the nine clips.
        /// </summary>
        private static readonly string[] RequiredDriverReferences =
        {
            "_animator", "_idle", "_walk", "_run", "_crouch", "_crouchWalk",
            "_carry", "_carryIdle", "_carryHeavy", "_death",
        };

        private const string Rule = "════════════════════════════════════════════════════════════════════════";

        /// <summary>Seed for the whole match — layout, clue contents, loot and the monster. §13: a match replays from its seed.</summary>
        public const int PlaytestSeed = 20260731;

        // DELETED with §04's roles: PlaytestRole. The solo tester had to be told which of
        // the five 직업 they were, because the answer changed their top speed and which
        // §08 containers they could open. Twenty identical runners: there is nothing to
        // choose.

        /// <summary>
        /// Builds the solo scene and opens it. Press Play afterwards.
        /// </summary>
        [MenuItem("HorrorGame/Play/Build Solo Playtest Scene", priority = 20)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (!BuildScene())
            {
                return;
            }

            Debug.Log(
                "[SoloPlaytest] Built " + SoloScenePath + ".\n"
                + "  Press Play. WASD to move, mouse to look, Shift to run, Ctrl to crouch, Space to hop, "
                + "F for the flashlight, E for a door.\n"
                + "  §01 — you start on the rim of B1. Work in to the middle of the storey, drop down the "
                + "투하구, and do it again. Eight times. The middle of B8 is the finish; the creature is a "
                + "hazard, and being caught is 탈락.\n"
                + "  §03 clues — stand still, close, and hold the beam on a 표식 for "
                + Core.GameConstants.ClueReadSeconds + "s. Losing the light restarts it, and nothing is written down.\n"
                + "  §03 objective — E takes it; both hands, so no flashlight and no loot. Carry it into the apron to win.\n"
                + "  §08 loot — E picks up. The 궤짝 is a " + Core.GameConstants.SharedCarryMaxCarriers
                + "-person piece and will say so. The 금고 needs 정비공: set Local Role on the MatchDirector.\n"
                + "  §14 Q1 — is the chase fun? Let the monster see you and run for an S-corridor.\n"
                + "  §14 Q2 — does the peek dilemma work? Hold a diagonal while looking back and watch "
                + "the margin fall. §05: forward 100%, 45° peek 95%, backward 65%.");

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(SoloScenePath));
        }

        /// <summary>Builds the solo scene and enters Play mode in one step.</summary>
        [MenuItem("HorrorGame/Play/Build And Play Solo", priority = 21)]
        public static void BuildAndPlay()
        {
            Build();
            EditorApplication.isPlaying = true;
        }

        // DELETED with the co-op loop: Verify() and VerifyBatch(), and the
        // SoloMatchLoopTests they drove. That harness asserted §01's 왕복 ran end to end —
        // descend, find the 목표물, carry it up, sell the 전리품, buy from §08, go back
        // down — and every clause in it is about a game that no longer exists.
        //
        // Its replacement is not a new headless harness in this file; it is
        // Tests/PlayMode/Race/DescentPlaythroughTests, which walks a runner from the rim
        // of B1 to the middle of B8 through all seven 투하구 and prints the table. That is
        // the same question asked of the race, and it runs in the real test runner rather
        // than through -executeMethod.
        //
        // BuildBatch below is untouched and is still the batch entry point that matters:
        // it rebuilds the solo scene from the generated map.

        /// <summary>
        /// Batch entry point that rebuilds the solo scene and nothing else.
        /// <para>
        /// It exists because of a specific failure. The eight-storey race map was
        /// generated into <c>Map_FirstSketch.unity</c> and the game plays
        /// <c>Map_FirstSketch_Solo.unity</c>, which is a SEPARATE scene built from it. So a
        /// build was shipped, and reported as the race, that loaded the old five-storey
        /// co-operative map — because the code was checked and the artefact was not.
        /// </para>
        /// <para>
        /// <c>VerifyBatch</c> used to rebuild it too, but it also ran the whole verification
        /// pass, which is the wrong tool when all that is wanted is "put the current map
        /// into the scene the player actually loads".
        /// </para>
        /// <para>
        /// It exits non-zero on three things, and the difference matters: the map is
        /// missing, the build threw, or the scene it just wrote does not carry §05's
        /// animation wiring. The last one used to exit 0 — the scene was written with nine
        /// <c>{fileID: 0}</c> clip slots and this method said "rebuilt". That is the whole
        /// family of bug this repository keeps shipping, so the check reads the file back
        /// off disk rather than trusting the objects it just assigned.
        /// </para>
        /// </summary>
        public static void BuildBatch()
        {
            try
            {
                if (!BuildScene())
                {
                    // BuildScene has already said which file is missing and how to make it.
                    EditorApplication.Exit(1);
                    return;
                }

                var wired = AuditAnimatorWiring(SoloScenePath, out var audit);
                if (!wired)
                {
                    Debug.LogError(audit);
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log(
                    "[SoloPlaytest] " + SoloScenePath + " rebuilt from " + MapScenePath + ".\n" + audit);
                EditorApplication.Exit(0);
            }
            catch (System.Exception error)
            {
                Debug.LogError("[SoloPlaytest] " + error);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Assembles the scene and saves it. Prompt-free so a test or a batch run can
        /// call it; the menu item asks about unsaved work before getting here.
        /// </summary>
        /// <returns>False when the generated map is missing.</returns>
        internal static bool BuildScene()
        {
            if (!System.IO.File.Exists(MapScenePath))
            {
                Debug.LogError(
                    "[SoloPlaytest] " + MapScenePath + " does not exist. Run "
                    + "HorrorGame ▸ Scene Gen ▸ Generate First Map first — it builds §12's 첫 맵 스케치 "
                    + "and refuses to produce a map that breaks a §12 rule.");
                return false;
            }

            var scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Single);

            var player = SpawnPlayer(scene);

            // Before the save, because this is what the save is for. BuildRig already
            // makes an attempt; this one is tolerant about clip names, says what it
            // matched, and is the thing AuditAnimatorWiring below marks its homework
            // against. See WirePlayerAnimation for why both exist.
            WirePlayerAnimation(player);

            var monster = SpawnMonster(scene);
            EnsureAudioListener(player);
            SpawnMatch(player, monster);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SoloScenePath, saveAsCopy: false);
            AssetDatabase.Refresh();

            EditorSceneManager.OpenScene(SoloScenePath, OpenSceneMode.Single);
            return true;
        }

        private static GameObject SpawnPlayer(Scene scene)
        {
            var spawn = FindMarker(scene, "PlayerSpawn");
            var rig = PlayerFeelHarnessMenu.BuildRig();

            if (rig == null)
            {
                // BuildRig already explained why. A capsule still answers §14's
                // questions 1 and 2, which are the ones that decide the project.
                rig = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                rig.name = "Player (fallback capsule)";
                Object.DestroyImmediate(rig.GetComponent<Collider>());
                rig.AddComponent<CharacterController>();
            }

            if (spawn != null)
            {
                rig.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            }
            else
            {
                // Above the floor rather than in it — a CharacterController that starts
                // inside geometry falls through before physics settles.
                rig.transform.position = new Vector3(0f, 1.2f, 0f);
                Debug.LogWarning(
                    "[SoloPlaytest] No PlayerSpawn marker found; dropped the player at the origin. "
                    + "Regenerate the map to get §12's spawn markers.");
            }

            // §03 · §08's hands. One component, one prompt, one crosshair ray.
            rig.AddComponent<PlayerInteractor>();

            return rig;
        }

        // ── §05 animation wiring ────────────────────────────────────────────────────
        //
        // Set by WirePlayerAnimation so AuditAnimatorWiring can tell three failures apart
        // that all look identical in the scene file: the model imported no animation, the
        // model imported animation under different names, and the wiring never ran.
        private static string _lastPlayerModelPath = PlayerModelPathFallback;
        private static bool _wiringRanThisSession;

        /// <summary>
        /// Fills <see cref="PlayerAnimatorDriver"/>'s Animator and its nine clip slots from
        /// the FBX the rig is actually built out of.
        /// <para>
        /// <see cref="PlayerFeelHarnessMenu.BuildRig"/> already attempts this, and this is
        /// not a duplicate of it for two reasons. First, it matches clip names by exact
        /// equality; a Generic import can prefix a take with the rig's name
        /// (<c>Runner|Walk</c>), and exact equality would then assign nine nulls and log
        /// nine warnings that nobody reads — the player slides and the build says nothing.
        /// Second, this scene is a <em>saved artefact</em>: the harness's failure lasts one
        /// play session, but a null written into <c>Map_FirstSketch_Solo.unity</c> is what
        /// every test, render and player build then loads. So the scene builder checks its
        /// own work rather than inheriting someone else's optimism.
        /// </para>
        /// <para>
        /// Failures are logged as warnings <em>here</em> and turned into a non-zero exit by
        /// <see cref="BuildBatch"/>'s read-back audit. BuildScene is called by a dozen
        /// render and test entry points; an error raised here would fail them all on a
        /// defect they neither caused nor can fix, while the batch build — the one a human
        /// reads the exit code of — stops dead.
        /// </para>
        /// </summary>
        /// <param name="rig">The player assembled by <c>BuildRig</c>.</param>
        /// <returns>True when the Animator and all nine clips were assigned.</returns>
        internal static bool WirePlayerAnimation(GameObject rig)
        {
            _wiringRanThisSession = true;

            var driver = rig.GetComponent<PlayerAnimatorDriver>();
            if (driver == null)
            {
                // The capsule fallback has no driver, and that is not a defect worth a
                // warning — BuildRig already said the model was missing.
                return false;
            }

            var visual = FindModelInstance(rig, out var modelPath);
            _lastPlayerModelPath = modelPath;

            var clips = LoadModelClips(modelPath);
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;

            // Settings say animation is on and the asset database has no clips: the
            // imported artefact is older than the .meta that describes it. That is the
            // exact state a hand-edited .meta leaves behind, and it is invisible in source.
            if (clips.Count == 0 && importer != null && importer.importAnimation
                && importer.animationType != ModelImporterAnimationType.None)
            {
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
                clips = LoadModelClips(modelPath);
            }

            var animator = visual != null ? visual.GetComponentInChildren<Animator>(true) : null;
            if (animator == null)
            {
                animator = rig.GetComponentInChildren<Animator>(true);
            }

            AssignReference(driver, "_animator", animator);

            // The Avatar is reported, not required. A Generic import binds its clips by
            // transform path, so the graph plays without one — but an Animator whose
            // m_Avatar is zero means the importer never made one, which is the tell that
            // the .meta says Generic and avatarSetup still says NoAvatar. Cheap to print,
            // and it is the difference between "the rig is wrong" and "the clips are wrong".
            var avatar = animator != null && animator.avatar != null ? animator.avatar.name : "none";

            var log = new StringBuilder();
            log.Append("[SoloPlaytest] §05 animation wiring from ").Append(modelPath)
                .Append(" — Animator ").Append(animator != null ? "found" : "MISSING")
                .Append(" (avatar: ").Append(avatar).Append("), ")
                .Append(clips.Count).Append(" clip(s) imported.");

            var missing = new List<string>();
            for (var i = 0; i < AnimationSlots.GetLength(0); i++)
            {
                var field = AnimationSlots[i, 0];
                var wanted = AnimationSlots[i, 1];
                var clip = ResolveClip(clips, wanted, out var how);

                AssignReference(driver, field, clip);

                log.Append("\n  ").Append(wanted.PadRight(11))
                    .Append(clip != null ? "← \"" + clip.name + "\"  (" + how + ")" : "← NOTHING");

                if (clip == null)
                {
                    missing.Add(wanted);
                }
            }

            if (missing.Count == 0 && animator != null)
            {
                Debug.Log(log.ToString());
                return true;
            }

            Debug.LogWarning(
                log + "\n  " + missing.Count + " of " + AnimationSlots.GetLength(0)
                + " poses unassigned"
                + (animator == null ? " and no Animator on the rig" : string.Empty)
                + ". The player will slide. "
                + "Run HorrorGame.EditorTools.SoloPlaytest.BuildBatch for the full diagnosis.");
            return false;
        }

        /// <summary>
        /// Reads the saved scene file back off disk and reports whether every reference on
        /// <see cref="PlayerAnimatorDriver"/> is non-zero.
        /// <para>
        /// It parses the YAML rather than inspecting the loaded objects on purpose. The
        /// objects in memory were assigned by the code being checked, so asking them is
        /// asking the defendant; the scene file is what the game loads. A map generated
        /// into the wrong scene, a camera attached to nothing and a race nobody could win
        /// all survived a code review and all would have been caught by one read of the
        /// artefact.
        /// </para>
        /// </summary>
        /// <param name="scenePath">Project-relative path to the saved scene.</param>
        /// <param name="report">What was measured, formatted for a terminal.</param>
        /// <returns>True when one or more driver blocks exist and none holds a zero reference.</returns>
        internal static bool AuditAnimatorWiring(string scenePath, out string report)
        {
            if (!System.IO.File.Exists(scenePath))
            {
                report = Rule + "\n[SoloPlaytest] ANIMATION AUDIT FAILED — " + scenePath
                    + " was not written.\n" + Rule;
                return false;
            }

            var blocks = ReadDriverBlocks(scenePath);
            var measured = new StringBuilder();
            measured.Append("[SoloPlaytest] §05 ANIMATION WIRING, read back from ").Append(scenePath)
                .Append(" — ").Append(blocks.Count).Append(" PlayerAnimatorDriver block(s).");

            var zeros = new List<string>();
            foreach (var block in blocks)
            {
                foreach (var field in RequiredDriverReferences)
                {
                    var value = block.TryGetValue(field, out var raw) ? raw : "<field absent>";
                    measured.Append("\n  ").Append(field.PadRight(12)).Append(value);

                    if (value == "{fileID: 0}" || value == "<field absent>")
                    {
                        zeros.Add(field);
                    }
                }
            }

            if (blocks.Count > 0 && zeros.Count == 0)
            {
                measured.Append("\n  ").Append(blocks.Count * RequiredDriverReferences.Length)
                    .Append('/').Append(blocks.Count * RequiredDriverReferences.Length)
                    .Append(" references non-zero. The player is animated.");
                report = measured.ToString();
                return true;
            }

            report = Rule
                + "\n[SoloPlaytest] BUILD FAILED — " + scenePath + " has an unwired PlayerAnimatorDriver."
                + "\n                The player will slide across the floor in every test, render and build."
                + "\n\n" + DiagnoseAnimationFailure(blocks.Count, zeros)
                + "\n\n" + measured
                + "\n" + Rule;
            return false;
        }

        /// <summary>
        /// Turns "the slots are zero" into which of the three causes it is. They are
        /// indistinguishable in the scene file and need completely different fixes, and
        /// guessing wrong costs a Unity round trip each time.
        /// </summary>
        private static string DiagnoseAnimationFailure(int blockCount, List<string> zeros)
        {
            if (blockCount == 0)
            {
                return "  CAUSE: NO DRIVER — the scene contains no PlayerAnimatorDriver at all.\n"
                    + "         The rig fell back to the capsule, which means "
                    + _lastPlayerModelPath + " did not load.\n"
                    + "         FIX: regenerate the model (tools/blender/gen_player.py) and reimport.";
            }

            var importer = AssetImporter.GetAtPath(_lastPlayerModelPath) as ModelImporter;
            if (importer == null)
            {
                return "  CAUSE: MODEL MISSING — " + _lastPlayerModelPath
                    + " is not in the project.\n"
                    + "         FIX: regenerate it with tools/blender/gen_player.py.";
            }

            var clips = LoadModelClips(_lastPlayerModelPath);
            var takes = importer.importedTakeInfos;

            if (!importer.importAnimation
                || importer.animationType == ModelImporterAnimationType.None
                || clips.Count == 0)
            {
                return "  CAUSE: IMPORT WRONG — Unity imported no animation from "
                    + _lastPlayerModelPath + ".\n"
                    + "         measured: importAnimation=" + (importer.importAnimation ? "1" : "0")
                    + "  animationType=" + (int)importer.animationType
                    + " (" + importer.animationType + ")"
                    + "  clips=" + clips.Count
                    + "  takes in file=" + (takes != null ? takes.Length : 0) + "\n"
                    + "         FIX: in " + _lastPlayerModelPath + ".meta set\n"
                    + "                animationType: 2      # Generic — the rig is 13 bones, not a Mecanim humanoid\n"
                    + "                importAnimation: 1\n"
                    + "                addColliders: 0       # a MeshCollider on the player's own visual eats §03's interact ray\n"
                    + "              then delete Library/ or reimport the asset.";
            }

            var wanted = new List<string>();
            for (var i = 0; i < AnimationSlots.GetLength(0); i++)
            {
                wanted.Add(AnimationSlots[i, 1]);
            }

            var namesWrong = zeros.Exists(field => field != "_animator");
            if (namesWrong)
            {
                var found = new List<string>();
                foreach (var clip in clips)
                {
                    found.Add("\"" + clip.name + "\"");
                }

                return "  CAUSE: NAMES WRONG — " + _lastPlayerModelPath + " imported "
                    + clips.Count + " clip(s), but not under §05's names.\n"
                    + "         Unity named them: " + string.Join(", ", found) + "\n"
                    + "         §05 expects:     " + string.Join(", ", wanted) + "\n"
                    + "         unfilled slots:  " + string.Join(", ", zeros) + "\n"
                    + "         FIX: rename the takes in tools/blender/gen_player.py, or add a\n"
                    + "              clipAnimations list to the .meta that maps take → name.";
            }

            return "  CAUSE: NOT WIRED — " + _lastPlayerModelPath + " imported " + clips.Count
                + " correctly named clip(s) and the scene still holds "
                + string.Join(", ", zeros) + " as zero.\n"
                + "         WirePlayerAnimation "
                + (_wiringRanThisSession
                    ? "ran but its assignment did not survive the save."
                    : "did NOT run before the save — check that BuildScene still calls it.")
                + "\n         FIX: that is a bug in SoloPlaytest.BuildScene, not in the model.";
        }

        /// <summary>
        /// Every <see cref="PlayerAnimatorDriver"/> MonoBehaviour in a text scene, as
        /// field → raw YAML value. Blocks are matched by the script's GUID first — an
        /// editor class identifier can be blank in a freshly written scene, and a broken
        /// audit that silently finds nothing is the same bug one level up.
        /// </summary>
        private static List<Dictionary<string, string>> ReadDriverBlocks(string scenePath)
        {
            var guid = DriverScriptGuid();
            var blocks = new List<Dictionary<string, string>>();
            var lines = System.IO.File.ReadAllLines(scenePath);

            var start = -1;
            for (var i = 0; i <= lines.Length; i++)
            {
                var boundary = i == lines.Length || lines[i].StartsWith("--- ", System.StringComparison.Ordinal);
                if (!boundary)
                {
                    continue;
                }

                if (start >= 0)
                {
                    var block = ReadBlock(lines, start, i, guid);
                    if (block != null)
                    {
                        blocks.Add(block);
                    }
                }

                start = i;
            }

            return blocks;
        }

        private static Dictionary<string, string>? ReadBlock(string[] lines, int from, int to, string guid)
        {
            var fields = new Dictionary<string, string>(System.StringComparer.Ordinal);
            var isDriver = false;

            for (var i = from; i < to; i++)
            {
                var line = lines[i];

                // Exactly two spaces of indent, and not a sequence dash. Deeper lines
                // belong to a nested map (m_Modification, m_LocalPosition …) and a key
                // read out of one would collide with a top-level field of the same name;
                // dashes are the elements of _footfallPhases.
                if (line.Length < 3 || line[0] != ' ' || line[1] != ' '
                    || line[2] == ' ' || line[2] == '-')
                {
                    continue;
                }

                var colon = line.IndexOf(':');
                if (colon < 2)
                {
                    continue;
                }

                var key = line.Substring(2, colon - 2).Trim();
                var value = line.Substring(colon + 1).Trim();
                fields[key] = value;

                if (key == "m_Script" && guid.Length > 0 && value.Contains(guid))
                {
                    isDriver = true;
                }
                else if (key == "m_EditorClassIdentifier"
                         && value.EndsWith("PlayerAnimatorDriver", System.StringComparison.Ordinal))
                {
                    isDriver = true;
                }
            }

            return isDriver ? fields : null;
        }

        /// <summary>
        /// The GUID of <see cref="PlayerAnimatorDriver"/>'s script, found through the type
        /// rather than pasted in — a literal GUID in this file would be a second copy of a
        /// fact that only the .meta owns, and it would go stale silently.
        /// </summary>
        private static string DriverScriptGuid()
        {
            foreach (var guid in AssetDatabase.FindAssets("PlayerAnimatorDriver t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == typeof(PlayerAnimatorDriver))
                {
                    return guid;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// The FBX the rig is built from, asked of the instantiated prefab rather than of a
        /// constant. A constant here could disagree with the one <c>BuildRig</c> used, and
        /// then this would wire clips out of a file the scene does not contain.
        /// </summary>
        private static GameObject? FindModelInstance(GameObject rig, out string modelPath)
        {
            foreach (var transform in rig.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(transform.gameObject) as GameObject;
                if (source == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrEmpty(path)
                    && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    modelPath = path;
                    return transform.gameObject;
                }
            }

            modelPath = PlayerModelPathFallback;
            return null;
        }

        /// <summary>
        /// The FBX's animation sub-assets. <c>__preview__</c> clips are the editor's own
        /// scratch copies and are never the thing to serialise.
        /// </summary>
        private static List<AnimationClip> LoadModelClips(string modelPath)
        {
            var clips = new List<AnimationClip>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", System.StringComparison.Ordinal))
                {
                    clips.Add(clip);
                }
            }

            return clips;
        }

        /// <summary>
        /// Finds the clip for a pose, tolerating the two things an FBX importer legitimately
        /// does to a take name: prefixing it with the rig node (<c>Runner|Walk</c>) and
        /// changing its case. <paramref name="how"/> records which rule matched, so the
        /// success log shows when the exporter has started prefixing and the exact-match
        /// path has quietly stopped being the one doing the work.
        /// </summary>
        private static AnimationClip? ResolveClip(List<AnimationClip> clips, string wanted, out string how)
        {
            foreach (var clip in clips)
            {
                if (string.Equals(clip.name, wanted, System.StringComparison.Ordinal))
                {
                    how = "exact";
                    return clip;
                }
            }

            foreach (var clip in clips)
            {
                if (string.Equals(BareTakeName(clip.name), wanted, System.StringComparison.Ordinal))
                {
                    how = "after the rig prefix";
                    return clip;
                }
            }

            foreach (var clip in clips)
            {
                if (string.Equals(BareTakeName(clip.name), wanted, System.StringComparison.OrdinalIgnoreCase))
                {
                    how = "case-insensitive";
                    return clip;
                }
            }

            how = "no match";
            return null;
        }

        /// <summary>The take name without the <c>Rig|</c> prefix a Generic import can add.</summary>
        private static string BareTakeName(string clipName)
        {
            var bar = clipName.LastIndexOf('|');
            return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
        }

        private static void AssignReference(Object target, string field, Object? value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning("[SoloPlaytest] " + target.GetType().Name
                    + " has no serialized field '" + field + "'.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject SpawnMonster(Scene scene)
        {
            var spawn = FindMarker(scene, "MonsterSpawn");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterModelPath);

            var body = new GameObject("Monster");
            body.transform.position = spawn != null ? spawn.position : new Vector3(10f, 0f, 10f);

            if (model != null)
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.name = "Visual";
                visual.transform.SetParent(body.transform, false);
            }
            else
            {
                Debug.LogWarning("[SoloPlaytest] " + MonsterModelPath + " missing; the monster will be invisible "
                    + "but will still hunt. Regenerate it with tools/blender/gen_monster_ai.py.");
            }

            var nav = body.AddComponent<NavMeshAgent>();
            nav.height = 2.3f;      // Monster.fbx measures 2.34 m tall.
            nav.radius = 0.5f;      // 0.93 m wide, so it fits §12's corridors.
            nav.speed = Core.GameConstants.ThreatSpeedEarlyEvening;  // §07 tier 0. MatchDirector drives it per tier.
            nav.angularSpeed = 240f;
            nav.acceleration = 12f;
            nav.stoppingDistance = 0.3f;

            // Snap onto the baked surface. An agent spawned even slightly off the
            // NavMesh silently refuses to path, which reads as "the AI is broken".
            if (NavMesh.SamplePosition(body.transform.position, out var hit, 8f, NavMesh.AllAreas))
            {
                body.transform.position = hit.position;
            }
            else
            {
                Debug.LogWarning("[SoloPlaytest] No NavMesh within 8 m of the monster spawn. "
                    + "Rebake it — HorrorGame ▸ Scene Gen ▸ Generate First Map does it for you.");
            }

            var agent = body.AddComponent<MonsterAgent>();
            agent.AddComponent_DebugViewIfPresent();

            // Not initialized here on purpose: MatchDirector owns the seed (§13) and
            // starts the monster on the same stream it lays the match out from, with
            // SelfDriven off so one loop steps everything in a defined order.
            return body;
        }

        /// <summary>
        /// Drops in the thing that turns a walkable map into a race: §07's clock, §06's
        /// creature, §02's standings and §09's spectator seat.
        /// <para>
        /// It used to add two screens here and both are deleted. <c>MatchHud</c> hosted
        /// §08's shop, §03's clue overlay and §02's four-outcome end screen;
        /// <c>PlaytestGuidanceScreen</c> told a first-time tester the next step of the
        /// 왕복 loop. <c>MatchDirector</c> builds <c>RaceHud</c> itself, which is the
        /// race's own screen and needs no help from a playtest tool.
        /// </para>
        /// </summary>
        private static void SpawnMatch(GameObject player, GameObject monster)
        {
            var host = new GameObject("[Match]");
            var director = host.AddComponent<MatchDirector>();

            var serialized = new SerializedObject(director);
            SetObject(serialized, "_monster", monster.GetComponent<MonsterAgent>());
            SetObject(serialized, "_playerRoot", player.transform);
            SetInt(serialized, "_seed", PlaytestSeed);
            SetBool(serialized, "_autoStart", true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureAudioListener(GameObject player)
        {
            if (Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length > 0)
            {
                return;
            }

            var camera = player.GetComponentInChildren<Camera>();
            (camera != null ? camera.gameObject : player).AddComponent<AudioListener>();
        }

        /// <summary>
        /// Finds a spawn marker by name prefix, searching inactive objects too — the
        /// generator parents markers under a disabled container so they do not render.
        /// </summary>
        private static Transform? FindMarker(Scene scene, string prefix)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(includeInactive: true))
                .FirstOrDefault(t => t.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
                                     && t.childCount == 0);
        }

        private static void SetObject(SerializedObject serialized, string field, Object? value)
        {
            var property = Find(serialized, field);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetInt(SerializedObject serialized, string field, int value)
        {
            var property = Find(serialized, field);
            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void SetBool(SerializedObject serialized, string field, bool value)
        {
            var property = Find(serialized, field);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static SerializedProperty? Find(SerializedObject serialized, string field)
        {
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning("[SoloPlaytest] " + serialized.targetObject.GetType().Name
                    + " has no serialized field '" + field + "'.");
            }

            return property;
        }
    }

    /// <summary>Small helpers kept out of <see cref="SoloPlaytest"/>'s main flow.</summary>
    internal static class SoloPlaytestExtensions
    {
        /// <summary>
        /// Adds the monster's gizmo view when that component exists. It is the fastest
        /// way to see why the monster did something — state, aggro target, last-seen
        /// position and the §06 release timers, drawn in the scene view.
        /// </summary>
        internal static void AddComponent_DebugViewIfPresent(this MonsterAgent agent)
        {
            var type = typeof(MonsterAgent).Assembly.GetType("HorrorGame.Gameplay.Monster.MonsterDebugView");
            if (type != null)
            {
                agent.gameObject.AddComponent(type);
            }
        }
    }
}
