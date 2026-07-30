#nullable enable

using System.Linq;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.PlayerEditor;
using HorrorGame.Gameplay.Playtest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Turns the generated map into something one person can press Play on.
    /// <para>
    /// The scene generator produces geometry, lights, a baked NavMesh and spawn
    /// markers — but no player and no monster, because in a real match those are
    /// spawned by the host over the network (§13). That is correct for shipping and
    /// useless for the question §14 puts first: does the chase feel good? Answering
    /// it should not require a lobby, two instances and a working transport.
    /// </para>
    /// <para>
    /// So this builds a throwaway scene beside the generated one: the map, a player
    /// at a §12 spawn marker, a monster at its own, and the link that reports one to
    /// the other. It never edits the generated scene, so regenerating the map cannot
    /// lose hand-placed work — there is none to lose.
    /// </para>
    /// <para>
    /// This assembly (<c>Assembly-CSharp-Editor</c>) is the only place this can live:
    /// the player rig is behind an asmdef and the monster is not, and a predefined
    /// editor assembly is the one thing that can reference both.
    /// </para>
    /// </summary>
    public static class SoloPlaytest
    {
        private const string MapScenePath = "Assets/Scenes/Map_FirstSketch.unity";
        private const string SoloScenePath = "Assets/Scenes/Map_FirstSketch_Solo.unity";
        private const string MonsterModelPath = "Assets/Models/Characters/Monster.fbx";

        /// <summary>Seed for the monster's patrol and standstill rolls. §13: a match replays from its seed.</summary>
        private const int PlaytestSeed = 20260731;

        /// <summary>
        /// Builds the solo scene and opens it. Press Play afterwards.
        /// </summary>
        [MenuItem("HorrorGame/Play/Build Solo Playtest Scene", priority = 0)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (!System.IO.File.Exists(MapScenePath))
            {
                EditorUtility.DisplayDialog(
                    "No map yet",
                    "Assets/Scenes/Map_FirstSketch.unity does not exist.\n\n"
                    + "Run HorrorGame ▸ Scene Gen ▸ Generate First Map first — it builds §12's "
                    + "첫 맵 스케치 and refuses to produce a map that breaks a §12 rule.",
                    "OK");
                return;
            }

            var scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Single);

            var player = SpawnPlayer(scene);
            var monster = SpawnMonster(scene);
            WireLink(player, monster);
            EnsureAudioListener(player);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SoloScenePath, saveAsCopy: false);
            AssetDatabase.Refresh();

            EditorSceneManager.OpenScene(SoloScenePath, OpenSceneMode.Single);

            Debug.Log(
                "[SoloPlaytest] Built " + SoloScenePath + ".\n"
                + "  Press Play. WASD to move, mouse to look, Shift to run, F for the flashlight.\n"
                + "  §14 Q1 — is the chase fun? Let the monster see you and run for an S-corridor.\n"
                + "  §14 Q2 — does the peek dilemma work? Hold a diagonal while looking back and watch "
                + "the margin fall. §05: forward 100%, 45° peek 95%, backward 65%.\n"
                + "  Monster state changes are logged to this console.");

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(SoloScenePath));
        }

        /// <summary>Builds the solo scene and enters Play mode in one step.</summary>
        [MenuItem("HorrorGame/Play/Build And Play Solo", priority = 1)]
        public static void BuildAndPlay()
        {
            Build();
            EditorApplication.isPlaying = true;
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
                    + "but will still hunt. Regenerate it with tools/blender/gen_monster_model.py.");
            }

            var nav = body.AddComponent<NavMeshAgent>();
            nav.height = 2.3f;      // Monster.fbx measures 2.34 m tall.
            nav.radius = 0.5f;      // 0.93 m wide, so it fits §12's corridors.
            nav.speed = 4.4f;       // §07 tier 0. MonsterAgent overrides this per tier.
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
            agent.Initialize(PlaytestSeed);

            return body;
        }

        private static void WireLink(GameObject player, GameObject monster)
        {
            var link = player.AddComponent<SoloPlaytestLink>();
            SetPrivateField(link, "_monster", monster.GetComponent<MonsterAgent>());
            SetPrivateField(link, "_player", player.transform);
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

        private static void SetPrivateField(Object target, string field, Object? value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"[SoloPlaytest] {target.GetType().Name} has no serialized field '{field}'. "
                    + "It will fall back to finding its reference at runtime.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
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
