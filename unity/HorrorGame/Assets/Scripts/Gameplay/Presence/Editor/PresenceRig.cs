#nullable enable

using System;
using System.IO;
using HorrorGame.Gameplay.Presence;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.Gameplay.PresenceEditor
{
    /// <summary>
    /// Turns the generator's two FBX files into the three prefabs the game loads: the
    /// 형상, one mote, and the view/audio root that owns them.
    /// <para>
    /// Everything here is a consequence of one thing the generator cannot do and one thing
    /// the importer does that is wrong for this asset:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Materials.</b> FBX carries neither metallic nor emission, so
    /// the imported meshes arrive with Unity's default white. <see cref="PresenceSkin"/>
    /// rebuilds them from the manifest and they are bound here — the same shape as
    /// <c>MonsterRig</c> binding <c>MonsterSkin</c>'s output.</description></item>
    /// <item><description><b>Colliders.</b> <c>AssetImportPolicy.ResolveModelCategory</c>
    /// grades anything in a new folder under <c>Assets/Models</c> as a Prop, and props
    /// import with a generated mesh collider. That is right for a 전리품 and wrong for
    /// this: a 그늘 you can walk into is a piece of furniture. The colliders are stripped
    /// at prefab build time as well as at instantiation, because two removals cost nothing
    /// and one missed removal turns the whole idea into an object.</description></item>
    /// </list>
    /// <para>
    /// Headless:
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod
    ///   HorrorGame.Gameplay.PresenceEditor.PresenceRig.Build
    /// </code>
    /// </para>
    /// </summary>
    public static class PresenceRig
    {
        /// <summary>Where the built prefabs live.</summary>
        public const string PrefabRoot = "Assets/Prefabs/Presence";

        /// <summary>The 형상, materials bound and colliders gone.</summary>
        public const string FigurePrefabPath = PrefabRoot + "/Presence_Figure.prefab";

        /// <summary>One flake.</summary>
        public const string MotePrefabPath = PrefabRoot + "/Presence_Mote.prefab";

        /// <summary>The root a match instantiates: <c>PresenceView</c> plus <c>PresenceAudio</c>.</summary>
        public const string ViewPrefabPath = PrefabRoot + "/Presence.prefab";

        /// <summary>Menu and batch entry point.</summary>
        [MenuItem("HorrorGame/Presence/Build 그늘 Prefabs")]
        public static void Build()
        {
            try
            {
                var view = BuildAll();
                Debug.Log("[PresenceRig] built " + ViewPrefabPath + " (" + view.name + "), "
                          + FigurePrefabPath + " and " + MotePrefabPath);

                if (IsBatch())
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[PresenceRig] " + ex);
                if (IsBatch())
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        /// <summary>Builds the materials and all three prefabs. Returns the view root.</summary>
        public static GameObject BuildAll()
        {
            PresenceSkin.BuildAll();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EnsureFolder(PrefabRoot);

            var voidMaterial = Require(PresenceSkin.Load(PresenceAssets.VoidMaterialName),
                PresenceAssets.VoidMaterialName);
            var grainMaterial = Require(PresenceSkin.Load(PresenceAssets.GrainMaterialName),
                PresenceAssets.GrainMaterialName);
            var dustMaterial = Require(PresenceSkin.Load(PresenceAssets.DustMaterialName),
                PresenceAssets.DustMaterialName);

            var figure = BuildModelPrefab(
                PresenceAssets.FigureModelPath, FigurePrefabPath, voidMaterial, grainMaterial,
                expectedHeightMetres: FigureHeightMetres);
            var mote = BuildModelPrefab(
                PresenceAssets.MoteModelPath, MotePrefabPath, dustMaterial, dustMaterial,
                expectedHeightMetres: 0f);

            return BuildViewPrefab(figure, mote);
        }

        /// <summary>
        /// What the 형상 has to measure once imported, metres — the same
        /// <c>FIGURE_HEIGHT</c> <c>gen_presence.py</c> asserts on its own side.
        /// <para>
        /// Checked on <em>both</em> sides of the FBX because the first render of this asset
        /// found it flat on the corridor floor, pointing at the camera, and both the
        /// generator's height assertion and the Blender preview had passed. See
        /// <see cref="StandUp"/>.
        /// </para>
        /// </summary>
        public const float FigureHeightMetres = 2.05f;

        /// <summary>Loads the built view prefab, or null before <see cref="Build"/> has run.</summary>
        public static GameObject? LoadViewPrefab() =>
            AssetDatabase.LoadAssetAtPath<GameObject>(ViewPrefabPath);

        private static GameObject BuildModelPrefab(
            string modelPath, string prefabPath, Material first, Material second,
            float expectedHeightMetres)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                throw new FileNotFoundException(
                    modelPath + " is missing. Run tools/blender/gen_presence.py first.");
            }

            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.transform.SetParent(root.transform, worldPositionStays: false);

                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var slots = renderer.sharedMaterials;
                    for (var i = 0; i < slots.Length; i++)
                    {
                        // Slot order comes from the generator: the void core is joined
                        // first, the grain second. A one-slot mesh takes `first` for both,
                        // which is what the mote wants.
                        slots[i] = i == 0 ? first : second;
                    }

                    renderer.sharedMaterials = slots;

                    // Nothing about the 그늘 receives or casts a shadow. A cast shadow
                    // would put the figure on the floor as an object with a position the
                    // player can triangulate, and §04's 관측자 already owns "read a
                    // position from something you can see".
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }

                StandUp(root, instance, prefabPath, expectedHeightMetres);
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Puts the model upright with its feet on its own pivot, and asserts it.
        /// <para>
        /// <b>This exists because the first render of the 형상 photographed it lying flat
        /// on the corridor floor, pointing at the camera.</b> Everything upstream had
        /// passed: the generator asserts its own 2.05 m height in Blender's Z-up space, a
        /// Blender preview render showed a standing figure, and <c>blendkit.export_fbx</c>
        /// asks for <c>axis_up='Y'</c>. What is easy to miss is that
        /// <c>export_fbx</c> catches <c>TypeError</c> and <b>retries with the rejected
        /// keyword removed</b> — deliberately, so a Blender version that retires a flag
        /// does not fail the whole asset build. The cost of that kindness is that a
        /// dropped <c>axis_up</c> is silent, and the only symptom is an asset that is
        /// wrong by exactly 90°.
        /// </para>
        /// <para>
        /// So the axis convention is asserted here, on the Unity side of the import, where
        /// it can be measured rather than assumed — and it is corrected rather than merely
        /// reported, because a generator that cannot be told which Blender is going to run
        /// it should not be the last word on which way is up.
        /// </para>
        /// </summary>
        private static void StandUp(GameObject root, GameObject instance, string prefabPath,
            float expectedHeightMetres)
        {
            var bounds = LocalBounds(root);
            if (bounds.size == Vector3.zero)
            {
                throw new InvalidOperationException(prefabPath + " has no renderer bounds — the mesh is empty.");
            }

            // A standing figure is taller than it is deep. If the long axis came through as
            // Z the file is Z-up, which is Blender's convention rather than Unity's.
            if (bounds.size.z > bounds.size.y * 1.5f)
            {
                instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                Debug.LogWarning("[PresenceRig] " + prefabPath + " imported Z-up ("
                                 + bounds.size.ToString("0.000")
                                 + ") and was stood upright here. blendkit.export_fbx's axis_up "
                                 + "did not survive; see PresenceRig.StandUp.");
                bounds = LocalBounds(root);
            }

            if (expectedHeightMetres <= 0f)
            {
                // A free mote is centred on its pivot, because PresenceView scales it and
                // a base pivot would make every scale change also move it.
                instance.transform.localPosition -= bounds.center;
                return;
            }

            // Feet on the pivot, so PlaceFigureAt can take a floor point straight from a
            // raycast without every caller knowing where the model's origin ended up.
            instance.transform.localPosition -= new Vector3(0f, bounds.min.y, 0f);
            bounds = LocalBounds(root);

            if (Mathf.Abs(bounds.size.y - expectedHeightMetres) > 0.03f)
            {
                throw new InvalidOperationException(
                    prefabPath + " stands " + bounds.size.y.ToString("0.000") + " m, not "
                    + expectedHeightMetres.ToString("0.000")
                    + " m. It has to sit between the player's 1.750 and the monster's 2.336 — "
                    + "a figure that reads as either one of those is the failure this asset is "
                    + "authored against.");
            }
        }

        private static Bounds LocalBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            var world = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                world.Encapsulate(renderers[i].bounds);
            }

            return new Bounds(
                root.transform.InverseTransformPoint(world.center),
                root.transform.InverseTransformVector(world.size));
        }

        private static GameObject BuildViewPrefab(GameObject figure, GameObject mote)
        {
            var root = new GameObject("Presence");
            try
            {
                var view = root.AddComponent<PresenceView>();
                var audio = root.AddComponent<PresenceAudio>();

                Bind(view, "_figurePrefab", figure);
                Bind(view, "_motePrefab", mote);

                Bind(audio, "_gathering", LoadClip(PresenceAssets.GatheringClipPath));
                Bind(audio, "_close", LoadClip(PresenceAssets.CloseClipPath));
                Bind(audio, "_taken", LoadClip(PresenceAssets.TakenClipPath));
                Bind(audio, "_returning", LoadClip(PresenceAssets.ReturnClipPath));

                return PrefabUtility.SaveAsPrefabAsset(root, ViewPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static AudioClip LoadClip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                throw new FileNotFoundException(
                    path + " is missing. tools/blender/gen_presence.py writes the four clips "
                    + "alongside the meshes — see its docstring for why they are not in tools/audio/.");
            }

            return clip;
        }

        /// <summary>
        /// Writes a private serialized field. Used rather than making the fields public
        /// because the fields are inspector wiring, and a public setter on a prefab
        /// reference is an invitation for runtime code to swap the asset — which is how a
        /// figure ends up being a white capsule in a shipped build.
        /// </summary>
        private static void Bind(Component target, string fieldName, UnityEngine.Object? value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    target.GetType().Name + " has no serialized field " + fieldName
                    + " — the prefab builder and the component have drifted apart.");
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material Require(Material? material, string name)
        {
            if (material == null)
            {
                throw new FileNotFoundException(
                    PresenceAssets.MaterialRoot + "/" + name + ".mat was not built. "
                    + "PresenceSkin.BuildAll should have made it.");
            }

            return material;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetPath)!.Replace('\\', '/');
            var leaf = Path.GetFileName(assetPath);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static bool IsBatch()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg == "-batchmode")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
