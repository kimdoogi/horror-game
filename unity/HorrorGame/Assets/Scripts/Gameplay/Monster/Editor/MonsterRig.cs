#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HorrorGame.Core;
using HorrorGame.Gameplay.Monster;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace HorrorGame.Gameplay.MonsterEditor
{
    /// <summary>
    /// Builds the monster's AnimatorController and its prefab from the generated FBX.
    /// <para>
    /// The creature has existed as a 2,994-triangle rig with seven clips and has never
    /// been on screen, because nothing assembled it: no materials, no controller, no
    /// prefab. This is that assembly, and it is a script rather than a hand-authored
    /// asset for the reason the whole <c>tools/blender</c> directory exists — a
    /// regenerated model with a different clip length or a re-measured gait speed
    /// rebuilds correctly instead of silently keeping the old numbers.
    /// </para>
    /// <para>
    /// <b>Two layers, and the split is §06's.</b> §06 has the monster moving in 경계 and
    /// 수색, but the Alert and Search clips are authored in place — the design is
    /// describing attention, not legs. A one-layer controller therefore has to choose
    /// between a creature that skates and a creature that never turns its head. So:
    /// </para>
    /// <list type="number">
    /// <item><b>Locomotion</b> — a 1D blend tree over the creature's measured ground
    /// speed with Standstill (0 m/s), Patrol and Chase as its members, plus Stunned and
    /// Grab as full-body one-shots. Owns the hips and both legs.</item>
    /// <item><b>Attention</b> — Alert, Chase and Search, masked to everything above the
    /// hips, faded in by weight.</item>
    /// </list>
    /// <para>
    /// Headless:
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod
    ///   HorrorGame.Gameplay.MonsterEditor.MonsterRig.Build
    /// </code>
    /// </para>
    /// </summary>
    public static class MonsterRig
    {
        /// <summary>
        /// The generated model, and the only monster in the project.
        /// <para>
        /// Owned by <c>tools/blender/gen_monster_ai.py</c>, which fits the 29-bone rig to
        /// a committed sculpt. It took this path over from
        /// <c>gen_monster_model.py</c> — the hull-and-tube generator, which can no longer
        /// write here without being asked, because two generators feeding one filename is
        /// a creature decided by whoever ran last.
        /// </para>
        /// </summary>
        public const string ModelPath = "Assets/Models/Characters/Monster.fbx";

        /// <summary>Measured clip lengths and gait speeds, written by the same generator.</summary>
        public const string ClipManifestPath = "Assets/Models/Characters/Monster.clips.json";

        /// <summary>Where the assembled creature lives.</summary>
        public const string PrefabRoot = "Assets/Prefabs/Monster";

        /// <summary>The prefab a scene or the host spawns.</summary>
        public const string PrefabPath = PrefabRoot + "/Monster.prefab";

        /// <summary>The controller the prefab's Animator runs.</summary>
        public const string ControllerPath = PrefabRoot + "/Monster.controller";

        /// <summary>The mask that splits legs from attention.</summary>
        public const string MaskPath = PrefabRoot + "/MonsterAttention.mask";

        /// <summary>
        /// Bones the Locomotion layer keeps. Everything else is handed to the Attention
        /// layer when its weight comes up.
        /// <para>
        /// Hips is on this list and that is the load-bearing entry: the generator's
        /// grounding and stance-lock passes write the creature's whole vertical bob and
        /// fore-aft foot lock into the hips' translation curve. Letting an in-place
        /// Alert clip drive Hips would throw both away and put the feet back through
        /// the floor.
        /// </para>
        /// </summary>
        private static readonly string[] LocomotionBones =
        {
            "Hips",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
            "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
        };

        /// <summary>Builds materials, controller, mask and prefab. Menu and batch entry point.</summary>
        [MenuItem("HorrorGame/Monster/Build Prefab")]
        public static void Build()
        {
            try
            {
                var skins = MonsterSkin.BuildAll();
                var clips = LoadClipManifest();
                var controller = BuildController(clips);
                var prefab = BuildPrefab(controller, skins, clips);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[MonsterRig] built " + PrefabPath + "\n  materials: "
                          + string.Join(", ", skins.Keys)
                          + "\n  layers: " + controller.layers.Length
                          + "\n  gait: Patrol " + clips.SpeedOf("Patrol").ToString("0.000")
                          + " m/s, Chase " + clips.SpeedOf("Chase").ToString("0.000") + " m/s"
                          + "\n  renderers: " + prefab.GetComponentsInChildren<Renderer>(true).Length);

                if (IsBatch())
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[MonsterRig] " + ex);
                if (IsBatch())
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        // ====================================================================
        // Controller.
        // ====================================================================

        /// <summary>Builds (or rebuilds in place) the two-layer controller.</summary>
        public static AnimatorController BuildController(ClipManifest manifest)
        {
            EnsureFolder(PrefabRoot);
            var clips = LoadClips();

            // Rebuilt from scratch rather than patched. A controller is a graph of
            // sub-assets and half-updating one leaves orphaned states that still answer
            // HasState, which is worse than either a correct rebuild or a clean failure.
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(MonsterAnimationDriver.SpeedParameterName, AnimatorControllerParameterType.Float);
            controller.AddParameter(MonsterAnimationDriver.GaitRateParameterName, AnimatorControllerParameterType.Float);

            BuildLocomotionLayer(controller, clips, manifest);
            BuildAttentionLayer(controller, clips);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void BuildLocomotionLayer(AnimatorController controller,
                                                 IReadOnlyDictionary<string, AnimationClip> clips,
                                                 ClipManifest manifest)
        {
            var layer = controller.layers[0];
            layer.name = "Locomotion";
            layer.defaultWeight = 1f;
            controller.layers = new[] { layer };

            var machine = layer.stateMachine;

            var tree = new BlendTree
            {
                name = MonsterAnimationDriver.LocomotionStateName,
                blendType = BlendTreeType.Simple1D,
                blendParameter = MonsterAnimationDriver.SpeedParameterName,
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            // Thresholds are the clips' OWN measured ground speeds, so the blend
            // parameter is a physical quantity rather than a 0–1 dial. That is what lets
            // MonsterAnimationDriver compute the residual playback rate in closed form
            // instead of needing a lookup table.
            //
            // Standstill is a member at 0 m/s and not merely a state of its own. A
            // monster easing to a halt at a §12 corner has to have somewhere to blend
            // TO, and §06's 정지 pose — feet welded, no weight shift — is exactly the
            // right zero-speed leg pose.
            tree.AddChild(clips["Standstill"], 0f);
            tree.AddChild(clips["Patrol"], manifest.SpeedOf("Patrol"));
            tree.AddChild(clips["Chase"], manifest.SpeedOf("Chase"));

            var locomotion = machine.AddState(MonsterAnimationDriver.LocomotionStateName);
            locomotion.motion = tree;
            locomotion.writeDefaultValues = false;
            locomotion.speedParameterActive = true;
            locomotion.speedParameter = MonsterAnimationDriver.GaitRateParameterName;
            machine.defaultState = locomotion;

            AddOneShot(machine, clips, MonsterAnimationDriver.StunnedClipName);
            AddOneShot(machine, clips, MonsterAnimationDriver.GrabClipName);
        }

        private static void BuildAttentionLayer(AnimatorController controller,
                                                IReadOnlyDictionary<string, AnimationClip> clips)
        {
            var mask = BuildMask();

            controller.AddLayer("Attention");
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.avatarMask = mask;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;

            // Starts at zero. §06's Patrol has nothing to say above the hips, and a
            // creature that came up in the world already turning its head would be
            // signalling attention it does not have.
            layer.defaultWeight = 0f;
            layers[layers.Length - 1] = layer;
            controller.layers = layers;

            var machine = layer.stateMachine;
            foreach (var name in new[] { "Alert", "Chase", "Search" })
            {
                var state = machine.AddState(name);
                state.motion = clips[name];
                state.writeDefaultValues = false;
                if (name == "Alert")
                {
                    machine.defaultState = state;
                }
            }
        }

        private static void AddOneShot(AnimatorStateMachine machine,
                                       IReadOnlyDictionary<string, AnimationClip> clips, string name)
        {
            var state = machine.AddState(name);
            state.motion = clips[name];

            // No exit transition. MonsterAnimationDriver watches normalizedTime and
            // hands control back to §06's current state, which is the only authority on
            // what the monster is doing — a transition here would let the controller
            // decide, and it does not know.
            state.writeDefaultValues = false;
        }

        /// <summary>
        /// Builds the avatar mask that gives the legs to Locomotion and everything else
        /// to Attention.
        /// <para>
        /// Paths are read from the imported hierarchy rather than assumed, because the
        /// FBX importer decides where the rig root sits relative to the Animator and a
        /// hard-coded path that stops matching produces a mask that silently masks
        /// nothing.
        /// </para>
        /// </summary>
        private static AvatarMask BuildMask()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                throw new FileNotFoundException(ModelPath + " is missing. Run tools/blender/gen_monster_ai.py.");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var root = instance.transform;
                var paths = new List<string>();
                foreach (var t in instance.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    paths.Add(RelativePath(root, t));
                }

                var mask = new AvatarMask { name = "MonsterAttention" };
                mask.transformCount = paths.Count;

                var locomotion = new HashSet<string>(LocomotionBones, StringComparer.Ordinal);
                var kept = 0;
                for (var i = 0; i < paths.Count; i++)
                {
                    var path = paths[i];
                    var leaf = path.Length == 0 ? string.Empty : path.Substring(path.LastIndexOf('/') + 1);
                    var active = !locomotion.Contains(leaf);
                    mask.SetTransformPath(i, path);
                    mask.SetTransformActive(i, active);
                    if (active)
                    {
                        kept++;
                    }
                }

                if (kept == paths.Count)
                {
                    throw new InvalidOperationException(
                        "The attention mask excluded no bones — none of " + string.Join(", ", LocomotionBones)
                        + " was found in " + ModelPath + ". A mask that masks nothing puts an in-place "
                        + "Alert clip on the legs, which is the foot-slide this layering exists to remove.");
                }

                var previous = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
                if (previous != null)
                {
                    AssetDatabase.DeleteAsset(MaskPath);
                }

                AssetDatabase.CreateAsset(mask, MaskPath);
                return mask;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (target == root)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            for (var t = target; t != null && t != root; t = t.parent)
            {
                parts.Add(t.name);
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        // ====================================================================
        // Prefab.
        // ====================================================================

        private static GameObject BuildPrefab(AnimatorController controller,
                                              IReadOnlyDictionary<string, Material> skins,
                                              ClipManifest manifest)
        {
            EnsureFolder(PrefabRoot);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                throw new FileNotFoundException(ModelPath + " is missing.");
            }

            var root = new GameObject("Monster");
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
                body.name = "Body";
                body.transform.SetParent(root.transform, worldPositionStays: false);

                var animator = body.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    animator = body.AddComponent<Animator>();
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                // The creature spends most of a match off screen behind §12's corners
                // and still has to be in the right pose the moment a beam finds it —
                // §06's whole 정지 trick is about what it was doing while unobserved.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                BindMaterials(body, skins);
                AddNavAgent(root);
                AddBehaviours(root, body, animator, manifest);

                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Puts the generated materials on the mesh, matched by the name the FBX carries.
        /// <para>
        /// By name and not by slot index: the slot order falls out of the order Blender
        /// happened to join the pieces in, so an added rib shard could reorder it and
        /// swap the hide onto the maw with nothing failing.
        /// </para>
        /// </summary>
        private static void BindMaterials(GameObject body, IReadOnlyDictionary<string, Material> skins)
        {
            var bound = 0;
            foreach (var renderer in body.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        continue;
                    }

                    // The FBX importer appends " (Instance)" and similar; match on the
                    // generator's name being present rather than on equality.
                    foreach (var pair in skins)
                    {
                        if (materials[i].name.Contains(pair.Key))
                        {
                            materials[i] = pair.Value;
                            bound++;
                            break;
                        }
                    }
                }

                renderer.sharedMaterials = materials;
            }

            if (bound == 0)
            {
                throw new InvalidOperationException(
                    "No material slot on " + ModelPath + " matched a generated skin name. The creature "
                    + "would ship with the FBX's flat colours, which is the state this pass exists to end.");
            }
        }

        private static void AddNavAgent(GameObject root)
        {
            var agent = root.AddComponent<NavMeshAgent>();

            // Height and radius describe the creature, and BLOCKERS.md B-001 turns on
            // exactly these two numbers: the fragmented bake is suspected to be doorways
            // narrower than the agent radius. They are written from the measured model
            // rather than typed, so a re-export that widens the shoulders shows up as a
            // bake failure instead of as clipping.
            agent.height = MonsterHeightMetres;
            agent.radius = MonsterRadiusMetres;
            agent.speed = GameConstants.MonsterBaseSpeed;
            agent.acceleration = GameConstants.MonsterBaseSpeed * 4f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = GameConstants.MonsterWaypointTolerance;

            // MonsterAgent turns both of these off at Initialize; setting them here as
            // well means a prefab dropped into a scene without a host never gets one
            // frame of Unity steering §12's cornering behaviour.
            agent.updatePosition = false;
            agent.updateRotation = false;
        }

        /// <summary>
        /// Measured height of the creature, metres, from the generator's
        /// <c>ASSET_REPORT</c> line. Mirrors <c>AssetImportPolicy.MonsterHeightMetres</c>,
        /// which the importer asserts the imported bounds against.
        /// </summary>
        private const float MonsterHeightMetres = 2.336f;

        /// <summary>
        /// Agent radius, metres. Half the creature's measured 0.834 m width, which is what
        /// §12's doorways have to clear — see BLOCKERS.md B-001, where a frame narrower
        /// than about twice this is the second suspect for the fragmented bake.
        /// <para>
        /// Down from 0.483 when the sculpt replaced the hull creature, and the direction is
        /// the safe one: the NavMesh was baked around the wider figure, so every surface
        /// this agent can reach is a surface something 13 cm broader could already stand on.
        /// A sculpt that came out <em>wider</em> would be the dangerous case, and would have
        /// to be re-baked rather than accommodated here.
        /// </para>
        /// </summary>
        private const float MonsterRadiusMetres = 0.417f;

        private static void AddBehaviours(GameObject root, GameObject body, Animator animator,
                                          ClipManifest manifest)
        {
            root.AddComponent<MonsterAgent>();

            var standstill = root.AddComponent<MonsterStandstillHold>();
            var tell = root.AddComponent<MonsterAcquireTell>();
            root.AddComponent<MonsterBeamResolve>();

            var driver = root.AddComponent<MonsterAnimationDriver>();
            driver.SetClipFacts(
                manifest.SpeedOf("Patrol"), manifest.SecondsOf("Patrol"),
                manifest.SpeedOf("Chase"), manifest.SecondsOf("Chase"),
                manifest.SecondsOf("Standstill"));

            // Serialized references are wired here rather than left to the components'
            // GetComponentInChildren fallbacks, so an inspector shows what is connected.
            Wire(driver, "_animator", animator);
            Wire(driver, "_standstill", standstill);
            Wire(driver, "_acquireTell", tell);
            Wire(standstill, "_animator", animator);

            var renderers = body.GetComponentsInChildren<Renderer>(includeInactive: true);
            Wire(root.GetComponent<MonsterBeamResolve>(), "_renderers", renderers);
            Wire(tell, "_renderers", renderers);
        }

        private static void Wire(UnityEngine.Object target, string field, UnityEngine.Object? value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null)
            {
                throw new InvalidOperationException(target.GetType().Name + " has no serialized field " + field);
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Wire(UnityEngine.Object target, string field, UnityEngine.Object[] values)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null)
            {
                throw new InvalidOperationException(target.GetType().Name + " has no serialized field " + field);
            }

            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ====================================================================
        // Assets and manifest.
        // ====================================================================

        /// <summary>Every clip the FBX carries, by name.</summary>
        public static Dictionary<string, AnimationClip> LoadClips()
        {
            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    clips[clip.name] = clip;
                }
            }

            foreach (var required in new[] { "Patrol", "Alert", "Chase", "Search", "Standstill", "Stunned", "Grab" })
            {
                if (!clips.ContainsKey(required))
                {
                    throw new InvalidOperationException(
                        ModelPath + " has no clip named '" + required + "'. §06's state machine has five states "
                        + "and the creature also needs §04's stun and the kill; a missing one is a state with "
                        + "no animation. Found: " + string.Join(", ", clips.Keys));
                }
            }

            return clips;
        }

        /// <summary>Reads the generator's measured clip facts.</summary>
        public static ClipManifest LoadClipManifest()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ClipManifestPath);
            if (asset == null)
            {
                throw new FileNotFoundException(
                    ClipManifestPath + " is missing. Run tools/blender/gen_monster_ai.py.");
            }

            var manifest = JsonUtility.FromJson<ClipManifest>(asset.text);
            if (manifest == null || manifest.clips == null || manifest.clips.Length == 0)
            {
                throw new InvalidOperationException(ClipManifestPath + " lists no clips.");
            }

            return manifest;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static bool IsBatch() =>
            Environment.GetCommandLineArgs().Any(a => string.Equals(a, "-batchmode", StringComparison.Ordinal));

        [Serializable]
        public sealed class ClipManifest
        {
            public int fps;
            public ClipFacts[] clips = Array.Empty<ClipFacts>();

            /// <summary>The clip's own measured ground speed, m/s.</summary>
            public float SpeedOf(string name) => Find(name).ground_speed_mps;

            /// <summary>The clip's length, seconds.</summary>
            public float SecondsOf(string name) => Find(name).seconds;

            private ClipFacts Find(string name)
            {
                foreach (var clip in clips)
                {
                    if (string.Equals(clip.name, name, StringComparison.Ordinal))
                    {
                        return clip;
                    }
                }

                throw new InvalidOperationException(
                    ClipManifestPath + " has no entry for '" + name + "'.");
            }
        }

        [Serializable]
        public sealed class ClipFacts
        {
            public string name = string.Empty;
            public int frames;
            public float seconds;
            public bool loops;
            public bool locomotion;
            public float ground_speed_mps;
            public float stance_speed_spread_mps;
        }
    }
}
