using System;
using System.IO;
using System.Linq;
using HorrorGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Photographs the placed gun pickups and the held template in the solo scene,
    /// under the shipped beam, with a bounds line per subject.
    /// <para>
    /// Exists because of a finding from the startle placement fix: PlaceGun and
    /// BuildHeldTemplate set identity world rotation on model-prefab instances, and by
    /// this project's three separate measurements (KitOrientation.Probe,
    /// PresenceRig.StandUp, the StartleShot transform dump) that replaces the FBX
    /// root's −90°X conversion and renders raw Blender axes. Nobody had photographed a
    /// placed gun since. The bounds line is the lying-flat detector: the pickup is
    /// authored 0.360 × 0.295 wide by 0.067 THIN (its cloth lies on the floor), so a
    /// placed pickup whose world-Y extent reads ~0.30 is standing on edge and wrong;
    /// ~0.07 is lying and right.
    /// </para>
    /// <para>
    /// Headless (no <c>-nographics</c>):
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath unity/HorrorGame -executeMethod
    ///   HorrorGame.EditorTools.GunShot.Batch -shotTag gun
    /// </code>
    /// </para>
    /// </summary>
    public static class GunShot
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const string SoloScenePath = "Assets/Scenes/Map_FirstSketch_Solo.unity";
        private const float EyeMetres = 1.6f;

        [MenuItem("HorrorGame/Shots/Gun Placement", priority = 221)]
        public static void Batch()
        {
            var tag = ArgValue("-shotTag") ?? "gun";
            EditorSceneManager.OpenScene(SoloScenePath, OpenSceneMode.Single);

            var outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Shots");
            Directory.CreateDirectory(outDir);

            var rig = new GameObject("GunShot_Rig");
            try
            {
                var camera = rig.AddComponent<Camera>();
                camera.fieldOfView = GameConstants.FovDefault;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 300f;
                camera.clearFlags = CameraClearFlags.Skybox;

                var post = rig.AddComponent<UniversalAdditionalCameraData>();
                post.renderPostProcessing = true;
                post.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                post.antialiasingQuality = AntialiasingQuality.High;

                var beam = new GameObject("Flashlight").AddComponent<Light>();
                beam.transform.SetParent(rig.transform, false);
                HorrorGame.Rendering.FlashlightBeam.Apply(beam);

                var shots = 0;
                shots += ShootPickup(camera, outDir, tag);
                shots += ShootHeldTemplate(camera, outDir, tag);
                shots += ShootHeldInHand(camera, outDir, tag);

                Debug.Log("[GunShot] " + shots + " frame(s) written to " + outDir + ".");
                if (Application.isBatchMode) EditorApplication.Exit(shots > 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[GunShot] " + ex);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rig);
            }
        }

        private static int ShootPickup(Camera camera, string outDir, string tag)
        {
            var markers = GameObject.Find("Markers");
            var guns = markers != null ? markers.transform.Find("Guns") : null;
            var pickup = guns != null
                ? guns.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Gun_B", StringComparison.Ordinal))
                : null;
            if (pickup == null)
            {
                Debug.LogWarning("[GunShot] no Gun_B* under Markers/Guns.");
                return 0;
            }

            LogBounds("pickup " + pickup.name, pickup);

            // The way a runner sees it on a dead-end floor: 2.2 m out at eye height,
            // approaching along the alcove's open horizontal direction — the pickup's
            // own forward points UP now that it lies flat (its stand-up is composed),
            // so it cannot be the approach axis; probe the four cardinals instead.
            var probeFrom = pickup.position + Vector3.up * 1.0f;
            var open = Vector3.forward;
            var best = 0f;
            foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                var reach = Physics.Raycast(probeFrom, dir, out var hit, 4f) ? hit.distance : 4f;
                if (reach > best) { best = reach; open = dir; }
            }

            var eye = pickup.position + open * Mathf.Min(2.2f, best - 0.3f) + Vector3.up * EyeMetres;
            camera.transform.SetPositionAndRotation(eye,
                Quaternion.LookRotation((pickup.position + Vector3.up * 0.05f - eye).normalized, Vector3.up));
            return Capture(camera, outDir, tag + "_pickup.png");
        }

        private static int ShootHeldTemplate(Camera camera, string outDir, string tag)
        {
            var template = UnityEngine.Object
                .FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == HorrorGame.EditorTools.SceneGen.MapSceneBuilder.HeldTemplateName);
            if (template == null)
            {
                Debug.LogWarning("[GunShot] no held template '"
                    + HorrorGame.EditorTools.SceneGen.MapSceneBuilder.HeldTemplateName + "' in the scene.");
                return 0;
            }

            var wasActive = template.gameObject.activeSelf;
            try
            {
                template.gameObject.SetActive(true);
                LogBounds("held template", template);
                var eye = template.position + new Vector3(0.35f, 0.15f, -0.5f);
                camera.transform.SetPositionAndRotation(eye,
                    Quaternion.LookRotation((template.position - eye).normalized, Vector3.up));
                return Capture(camera, outDir, tag + "_held_template.png");
            }
            finally
            {
                template.gameObject.SetActive(wasActive);
            }
        }

        /// <summary>
        /// The held gun as another runner sees it: a real rig posed with GunIdle, the
        /// held clone mounted by RunnerGun's OWN arithmetic — identity local rotation
        /// under RightUpperArm, offset <c>head.localPosition.magnitude ×
        /// GunMountArmsPerSpine</c> along the bone's +Y (RunnerGun.MountOffset,
        /// reproduced not referenced: the runtime method is private, and reproducing it
        /// here means a drift between the two is a photograph, not a mystery).
        /// This is the frame that has never been taken: the mount POINT is measured to
        /// 1e-7, but the clone's ORIENTATION under the bone has shipped sight unseen.
        /// </summary>
        private static int ShootHeldInHand(Camera camera, string outDir, string tag)
        {
            var rig = HorrorGame.Gameplay.PlayerEditor.PlayerFeelHarnessMenu.BuildRig();
            if (rig == null)
            {
                Debug.LogWarning("[GunShot] BuildRig failed — no held-in-hand frame.");
                return 0;
            }

            try
            {
                var view = rig.GetComponent<HorrorGame.Gameplay.Player.PlayerFirstPersonView>();
                if (view != null)
                {
                    view.IsOwner = false;
                    view.Apply();
                }

                var root = rig.transform;
                var arm = FindDeep(root, t => t.name == HorrorGame.Gameplay.Race.RunnerGun.ArmBone);
                var head = FindDeep(root, t => t.name == HorrorGame.Gameplay.Race.RunnerGun.HeadBone);
                if (arm == null || head == null)
                {
                    Debug.LogWarning("[GunShot] rig lacks " + HorrorGame.Gameplay.Race.RunnerGun.ArmBone
                        + "/" + HorrorGame.Gameplay.Race.RunnerGun.HeadBone + ".");
                    return 0;
                }

                // The SCENE template, exactly as RunnerGun.FindTemplate takes it — not
                // the raw asset. First cut instantiated the FBX asset and photographed a
                // 30-metre gun: the import's scale normalization lives on the scene
                // instance, and a harness that diverges from the runtime's path
                // photographs its own bug instead of the game's.
                var heldTemplate = UnityEngine.Object
                    .FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(t => t.name == HorrorGame.EditorTools.SceneGen.MapSceneBuilder.HeldTemplateName)
                    ?.gameObject;
                if (heldTemplate == null)
                {
                    Debug.LogWarning("[GunShot] no held template in the open scene.");
                    return 0;
                }

                // Pose first, then mount: the clip moves the bone the clone hangs from.
                var clips = AssetDatabase.LoadAllAssetsAtPath("Assets/Models/Player/Runner.fbx")
                    .OfType<AnimationClip>().ToArray();
                var gunIdle = clips.FirstOrDefault(c => c.name == "GunIdle");
                var animator = rig.GetComponentInChildren<Animator>();
                var poseTarget = animator != null ? animator.gameObject : rig;
                if (gunIdle != null)
                {
                    gunIdle.SampleAnimation(poseTarget, gunIdle.length * 0.25f);
                }

                var held = UnityEngine.Object.Instantiate(heldTemplate, arm, false);
                held.SetActive(true);
                held.transform.localRotation = Quaternion.identity;
                held.transform.localPosition = new Vector3(0f,
                    head.localPosition.magnitude * HorrorGame.Gameplay.Race.RunnerGun.GunMountArmsPerSpine, 0f);

                // Mirror of RunnerGun.Arm's bone-scale normalization — keep in sync by
                // hand; a drift between the two shows up as a photograph, which is the
                // whole reason this harness reproduces rather than references.
                var boneScale = arm.lossyScale;
                var wantScale = heldTemplate.transform.lossyScale;
                held.transform.localScale = new Vector3(
                    boneScale.x != 0f ? wantScale.x / boneScale.x : 1f,
                    boneScale.y != 0f ? wantScale.y / boneScale.y : 1f,
                    boneScale.z != 0f ? wantScale.z / boneScale.z : 1f);
                LogBounds("held in hand (GunIdle)", held.transform);

                rig.transform.position = Vector3.up * 200f; // clear of the map, same trick as the stages
                var count = 0;
                foreach (var (name, offset) in new (string, Vector3)[]
                {
                    ("front", new Vector3(0f, 1.35f, 2.0f)),
                    ("side", new Vector3(2.0f, 1.35f, 0.3f)),
                    ("close", new Vector3(0.9f, 1.15f, 0.9f)),
                })
                {
                    var eye = rig.transform.position + offset;
                    camera.transform.SetPositionAndRotation(eye,
                        Quaternion.LookRotation((rig.transform.position + Vector3.up * 1.1f - eye).normalized, Vector3.up));
                    count += Capture(camera, outDir, tag + "_inhand_" + name + ".png");
                }

                return count;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rig);
            }
        }

        private static Transform FindDeep(Transform root, Func<Transform, bool> match)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != root && match(t)) return t;
            }

            return null;
        }

        private static void LogBounds(string label, Transform subject)
        {
            var renderers = subject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.Log("[GunShot] " + label + ": no renderers");
                return;
            }

            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            Debug.Log("[GunShot] " + label + " world=" + subject.position.ToString("0.000")
                + " euler=" + subject.rotation.eulerAngles.ToString("0")
                + " boundsSize=" + b.size.ToString("0.000"));
        }

        private static string ArgValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal)) return args[i + 1];
            }

            return null;
        }

        private static int Capture(Camera camera, string outDir, string fileName)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(outDir, fileName), texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                return 1;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
}
