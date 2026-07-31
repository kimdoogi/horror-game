#nullable enable

using System.IO;
using System.Linq;
using HorrorGame.Audio;
using HorrorGame.Gameplay.Audio;
using HorrorGame.Gameplay.Monster;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.EditorTools.Audio
{
    /// <summary>
    /// Turns 166 verified WAVs into a scene that makes noise.
    /// <para>
    /// Two steps, deliberately separate. <see cref="RebuildLibraries"/> reads
    /// <c>Assets/Audio</c> and writes two assets; <see cref="WireOpenScene"/> drops one
    /// component into a scene and points it at them. The split is what makes the
    /// wiring survive <c>SoloPlaytest.BuildScene</c>, which discards the solo scene and
    /// rebuilds it from the generated map every time it runs — the libraries are assets
    /// and outlive that, and re-wiring is one component rather than a hundred inspector
    /// fields.
    /// </para>
    /// <para>
    /// <b>Nothing here decides how anything sounds.</b> Every level, filter corner and
    /// crossfade time is <c>AudioTuning</c>'s, and the clip-to-slot mapping is
    /// <see cref="AudioClipCatalog"/>'s. This file only moves objects around a scene, so
    /// that a mix decision can never hide in a tool.
    /// </para>
    /// </summary>
    public static class AudioSceneWiring
    {
        /// <summary>
        /// Where the generated libraries are written.
        /// <para>
        /// A <c>Resources</c> folder so <c>MatchAudioLibrary.LoadDefault</c> can find
        /// the clip set with no help from a scene — see that member for why the scene
        /// is not a reliable place to keep the reference.
        /// </para>
        /// </summary>
        public const string LibraryFolder = "Assets/Audio/Resources";

        /// <summary>§12's five surfaces, as an asset.</summary>
        public const string SurfaceLibraryPath = LibraryFolder + "/SurfaceAudioLibrary.asset";

        /// <summary>Everything else, as an asset.</summary>
        public const string MatchLibraryPath = LibraryFolder + "/MatchAudioLibrary.asset";

        private const string SoloScenePath = "Assets/Scenes/Map_FirstSketch_Solo.unity";
        private const string RigObjectName = "[Audio]";

        /// <summary>
        /// Rebuilds both libraries from the shipped clip set, creating the assets if
        /// they do not exist. Safe to re-run after regenerating the audio.
        /// </summary>
        [MenuItem("HorrorGame/Audio/Rebuild Audio Libraries", priority = 20)]
        public static void RebuildLibraries()
        {
            var report = RebuildLibraries(out var surfaces, out var match);

            var gaps = match.Validate(out var wired, out var gapCount);
            var message =
                "[AudioWiring] " + report.Summary + "\n"
                + "  libraries: " + SurfaceLibraryPath + ", " + MatchLibraryPath + "\n"
                + "  " + wired + " clip slots filled, " + gapCount + " gaps\n"
                + gaps;

            if (report.Complete && gapCount == 0)
            {
                Debug.Log(message, match);
            }
            else
            {
                Debug.LogWarning(message + "\n  missing files:\n    "
                    + string.Join("\n    ", report.Missing.Take(20)), match);
            }
        }

        /// <summary>
        /// Adds the mix to whatever scene is open. Idempotent — running it twice leaves
        /// one rig.
        /// </summary>
        [MenuItem("HorrorGame/Audio/Wire Audio Into Open Scene", priority = 21)]
        public static void WireOpenScene()
        {
            var scene = SceneManager.GetActiveScene();
            var rig = Wire(scene);

            EditorSceneManager.MarkSceneDirty(scene);

            if (rig == null)
            {
                Debug.LogError("[AudioWiring] Could not wire " + scene.name
                    + " — the clip libraries are missing. Run Rebuild Audio Libraries first.");
                return;
            }

            Debug.Log("[AudioWiring] " + scene.name + " now carries " + RigObjectName
                + ". Press Play, then HorrorGame ▸ Audio ▸ Report Live Audio Sources.", rig.gameObject);
        }

        /// <summary>
        /// Builds the solo playtest scene and wires the mix into it, in one step.
        /// <para>
        /// The reason this exists rather than a note in the README:
        /// <c>SoloPlaytest.Build</c> writes the scene from scratch, so a wiring pass that
        /// has to be remembered afterwards is a wiring pass that will be forgotten, and
        /// the failure is silence — which is exactly what this whole change is about.
        /// </para>
        /// </summary>
        [MenuItem("HorrorGame/Audio/Build Audible Solo Playtest Scene", priority = 22)]
        public static void BuildAudibleSoloScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (!BuildAudibleSoloSceneQuiet(out var summary))
            {
                Debug.LogError("[AudioWiring] " + summary);
                return;
            }

            Debug.Log("[AudioWiring] " + summary);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(SoloScenePath));
        }

        /// <summary>
        /// Batch entry point. Rebuilds the libraries, rebuilds the solo scene, wires the
        /// mix in and exits non-zero when the scene came out silent.
        /// </summary>
        public static void BuildAudibleSoloSceneBatch()
        {
            var ok = BuildAudibleSoloSceneQuiet(out var summary);
            Debug.Log("[AudioWiring] " + summary);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Rebuilds both library assets from disk and writes them, without logging.
        /// <para>
        /// <b>The save is not optional and is not the caller's job.</b> A
        /// <c>ScriptableObject</c> filled in memory and left undirtied looks completely
        /// wired for the rest of the session and is empty on disk — and the next
        /// <c>AssetDatabase.Refresh</c> silently resets the in-memory copy to match.
        /// That failure produces a game that is audible while the tool that wired it is
        /// still running and silent ever afterwards, which is precisely the bug this
        /// whole change exists to remove. So the write happens here, once, where every
        /// caller gets it.
        /// </para>
        /// </summary>
        public static AudioClipCatalog.Report RebuildLibraries(
            out SurfaceAudioLibrary surfaces, out MatchAudioLibrary match)
        {
            if (!AssetDatabase.IsValidFolder(LibraryFolder))
            {
                Directory.CreateDirectory(LibraryFolder);
                AssetDatabase.Refresh();
            }

            surfaces = LoadOrCreate<SurfaceAudioLibrary>(SurfaceLibraryPath);
            match = LoadOrCreate<MatchAudioLibrary>(MatchLibraryPath);

            var report = AudioClipCatalog.Populate(
                surfaces, match, path => AssetDatabase.LoadAssetAtPath<AudioClip>(path));

            EditorUtility.SetDirty(surfaces);
            EditorUtility.SetDirty(match);
            AssetDatabase.SaveAssets();

            return report;
        }

        /// <summary>
        /// Drops a <see cref="MatchAudioRig"/> and a <c>MatchAudioBridge</c> into a
        /// scene and points them at the libraries.
        /// <para>
        /// The landmark hums are not placed here. They need the map's own zone
        /// markers, which <c>MatchAudioBridge</c> reads at runtime — and doing it
        /// there means one implementation that also covers a scene the bootstrap
        /// installed rather than this step.
        /// </para>
        /// </summary>
        /// <returns>The rig, or null when the libraries have not been built.</returns>
        public static MatchAudioRig? Wire(Scene scene)
        {
            var surfaces = AssetDatabase.LoadAssetAtPath<SurfaceAudioLibrary>(SurfaceLibraryPath);
            var match = AssetDatabase.LoadAssetAtPath<MatchAudioLibrary>(MatchLibraryPath);

            if (surfaces == null || match == null)
            {
                RebuildLibraries(out surfaces, out match);
            }

            var host = scene.GetRootGameObjects().FirstOrDefault(o => o.name == RigObjectName);
            if (host == null)
            {
                host = new GameObject(RigObjectName);
                SceneManager.MoveGameObjectToScene(host, scene);
            }

            var rig = host.GetComponent<MatchAudioRig>();
            if (rig == null)
            {
                rig = host.AddComponent<MatchAudioRig>();
            }

            var serialized = new SerializedObject(rig);
            SetObject(serialized, "library", match);
            SetObject(serialized, "player", FindPlayerRoot(scene));
            SetObject(serialized, "monster", FindMonsterRoot(scene));
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // The bridge is a separate component because it is the only piece that knows
            // what a match is; see its own remarks. Added unconditionally — a scene with
            // no MatchDirector simply leaves it idle.
            if (host.GetComponent<MatchAudioBridge>() == null)
            {
                host.AddComponent<MatchAudioBridge>();
            }

            EnsureListener(scene);

            return rig;
        }

        private static bool BuildAudibleSoloSceneQuiet(out string summary)
        {
            var report = RebuildLibraries(out _, out _);

            if (!SoloPlaytest.BuildScene())
            {
                summary = "SoloPlaytest.BuildScene failed — generate the map first.";
                return false;
            }

            var scene = SceneManager.GetActiveScene();
            var rig = Wire(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SoloScenePath, saveAsCopy: false);
            AssetDatabase.Refresh();

            // Re-read from disk rather than validating the instance we just filled. The
            // in-memory copy can be ahead of the asset — an AssetDatabase.Refresh in
            // between reloads it — and the number worth reporting is the one a fresh
            // editor and a shipped build will see, not the one this process happens to
            // be holding.
            var onDisk = AssetDatabase.LoadAssetAtPath<MatchAudioLibrary>(MatchLibraryPath);
            var wired = 0;
            var gaps = -1;
            onDisk?.Validate(out wired, out gaps);

            summary =
                report.Summary + "; " + wired + " slots filled, " + gaps + " gaps; "
                + (rig != null ? "rig wired into " : "NO RIG in ") + SoloScenePath;

            return rig != null && report.Complete && gaps == 0;
        }

        private static void EnsureListener(Scene scene)
        {
            if (Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
            {
                return;
            }

            // §05 pins the ears to the camera — "3D 오디오는 카메라 기준 → 헤드폰 필수".
            // A scene with no listener is not quiet, it is deaf.
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.gameObject.AddComponent<AudioListener>();
            }
        }

        private static Transform? FindPlayerRoot(Scene scene)
        {
            var listener = Object.FindFirstObjectByType<AudioListener>();
            if (listener != null)
            {
                return listener.transform.root;
            }

            return scene.GetRootGameObjects()
                .FirstOrDefault(o => o.GetComponent<CharacterController>() != null
                                     || o.GetComponentInChildren<CharacterController>() != null)?.transform;
        }

        private static Transform? FindMonsterRoot(Scene scene)
        {
            var agent = Object.FindFirstObjectByType<MonsterAgent>();
            return agent != null ? agent.transform : null;
        }

        private static T LoadOrCreate<T>(string path)
            where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void SetObject(SerializedObject serialized, string field, Object? value)
        {
            var property = serialized.FindProperty(field);
            if (property != null)
            {
                property.objectReferenceValue = value;
                return;
            }

            Debug.LogWarning("[AudioWiring] " + serialized.targetObject.GetType().Name
                + " has no serialized field '" + field + "'.");
        }

        private static void SetFloat(SerializedObject serialized, string field, float value)
        {
            var property = serialized.FindProperty(field);
            if (property != null)
            {
                property.floatValue = value;
            }
        }
    }
}
