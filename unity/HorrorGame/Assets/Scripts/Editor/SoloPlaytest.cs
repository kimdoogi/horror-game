#nullable enable

using System.Linq;
using HorrorGame.Core.Roles;
using HorrorGame.Gameplay.Guidance;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.MatchEditor;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.PlayerEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

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

        /// <summary>Seed for the whole match — layout, clue contents, loot and the monster. §13: a match replays from its seed.</summary>
        public const int PlaytestSeed = 20260731;

        /// <summary>
        /// Which of §04's five the solo tester plays. §11 has one role missing every
        /// match; 주자 is the one §14's first two questions are about, so it is the
        /// default. Change it on the MatchDirector component in the built scene to
        /// reach §08's 금고, which only 정비공 can open.
        /// </summary>
        public const RoleId PlaytestRole = RoleId.Runner;

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
                + "  Nobody has to read this. The scene carries PlaytestGuidanceScreen: one line at the bottom "
                + "says what to do next, F1 reopens the controls card and F2 shows §14's five questions. "
                + "HorrorGame ▸ Play ▸ ▶ START PLAYTEST does the whole thing in one click.\n"
                + "  Press Play. WASD to move, mouse to look, Shift to run, F for the flashlight, E to interact.\n"
                + "  §01 loop — walk out of the 출입구 apron to descend; walk back into it to surface. Arriving "
                + "sells the 전리품 into the shared wallet, tops the cell up at §03's 지상 발전기 and opens §08's shop. "
                + "E puts the shop away and gives the mouse back; E on the 차량 brings it back.\n"
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

        /// <summary>
        /// Drives a whole match headlessly and reports whether §01's loop ran. The menu
        /// twin of <c>SoloMatchLoopTests</c>, so the same check can be made from a
        /// terminal with <c>-executeMethod HorrorGame.EditorTools.SoloPlaytest.VerifyBatch</c>.
        /// </summary>
        [MenuItem("HorrorGame/Play/Verify Solo Match Loop", priority = 22)]
        public static void Verify()
        {
            var report = SoloMatchLoopTests.RunAll();
            if (report.Passed)
            {
                Debug.Log("[SoloPlaytest] " + report.Summary);
                return;
            }

            Debug.LogError("[SoloPlaytest] " + report.Summary);
        }

        /// <summary>Batch entry point. Exits non-zero when §01's loop did not run end to end.</summary>
        public static void VerifyBatch()
        {
            var report = SoloMatchLoopTests.RunAll();
            Debug.Log("[SoloPlaytest] " + report.Summary);
            EditorApplication.Exit(report.Passed ? 0 : 1);
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
        /// Drops in the thing that turns a walkable map into a match: §07's clock,
        /// §03's chain, §08's economy and §02's verdict, plus the screens.
        /// </summary>
        private static void SpawnMatch(GameObject player, GameObject monster)
        {
            var host = new GameObject("[Match]");
            var director = host.AddComponent<MatchDirector>();

            var hud = new GameObject("MatchHud");
            hud.transform.SetParent(host.transform, worldPositionStays: false);
            hud.AddComponent<MatchHud>();

            // §14's tester has not read anything. MatchHud draws the game's own screens
            // and deliberately adds no widgets; this draws the things a first-time player
            // needs and a player who has read §01 does not — the next step of the loop,
            // the live bindings, §14's five questions and the run's numbers. It is built
            // here rather than by the bootstrap because it belongs to a playtest.
            var guide = new GameObject("PlaytestGuidance");
            guide.transform.SetParent(host.transform, worldPositionStays: false);
            guide.AddComponent<PlaytestGuidanceScreen>();

            var serialized = new SerializedObject(director);
            SetObject(serialized, "_monster", monster.GetComponent<MonsterAgent>());
            SetObject(serialized, "_playerRoot", player.transform);
            SetInt(serialized, "_seed", PlaytestSeed);
            SetInt(serialized, "_localRole", (int)PlaytestRole);
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
