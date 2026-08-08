using System;
using System.Collections.Generic;
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
    /// Photographs the 깜짝 set pieces at their real markers in the solo scene, under
    /// the shipped beam, in both rest and fired states.
    /// <para>
    /// The dynamic proof lives elsewhere — the PlayMode playthrough log shows the
    /// director firing with its cooldown honoured to the decimal. What no log can
    /// show is whether a slammed leaf READS as a slammed leaf at trigger distance in
    /// this game's darkness, so this tool stages exactly the states
    /// <c>StartleDirector</c> produces (leaf swung on its hinge, skitterer
    /// mid-crossing, filament dead, the figure standing off-beam) and captures them
    /// with <see cref="MonsterShot"/>'s camera and beam conventions. It deliberately
    /// re-stages rather than reflecting into the director's private stage methods: a
    /// photograph procedure that can only be produced by running the game would drag
    /// play mode into a batch tool, and the director's own geometry is already pinned
    /// by StartleTests.
    /// </para>
    /// <para>
    /// Headless:
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath unity/HorrorGame -executeMethod
    ///   HorrorGame.EditorTools.StartleShot.Batch -shotTag startle
    /// </code>
    /// No <c>-nographics</c> — the render needs a device, same as every shot batch.
    /// </para>
    /// </summary>
    public static class StartleShot
    {
        private const int Width = 1280;
        private const int Height = 720;

        private const string SoloScenePath = "Assets/Scenes/Map_FirstSketch_Solo.unity";
        private const string MarkerRootName = "Markers";
        private const string StartleGroupName = "Startles";

        /// <summary>Eye height matches the runner rig; distance is just outside the 1.55 m trigger reach.</summary>
        private const float EyeMetres = 1.6f;
        private const float StandOffMetres = 2.2f;

        [MenuItem("HorrorGame/Shots/Startle Set Pieces", priority = 220)]
        public static void Batch()
        {
            var tag = ArgValue("-shotTag") ?? "startle";
            var scene = EditorSceneManager.OpenScene(SoloScenePath, OpenSceneMode.Single);
            var markers = GameObject.Find(MarkerRootName);
            var group = markers != null ? markers.transform.Find(StartleGroupName) : null;
            if (group == null)
            {
                Debug.LogError("[StartleShot] no '" + StartleGroupName + "' group under '" + MarkerRootName
                    + "' in " + SoloScenePath + " — regenerate the map and the solo scene first.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Shots");
            Directory.CreateDirectory(outDir);

            var rig = new GameObject("StartleShot_Rig");
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
                shots += ShootCabinet(group, camera, outDir, tag);
                shots += ShootSkitterer(group, camera, outDir, tag);
                shots += ShootPipe(group, camera, outDir, tag);
                shots += ShootBulbDeath(group, camera, outDir, tag);
                shots += ShootGlimpse(group, camera, outDir, tag);

                Debug.Log("[StartleShot] " + shots + " frame(s) written to " + outDir + " with tag '" + tag + "'.");
                if (Application.isBatchMode) EditorApplication.Exit(shots > 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[StartleShot] " + ex);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rig);
            }
        }

        // ------------------------------------------------------------------

        private static int ShootCabinet(Transform group, Camera camera, string outDir, string tag)
        {
            var marker = FirstMarker(group, "Startle_Cabinet");
            if (marker == null) return 0;

            // BuildStartles parents the shell + hinge/leaf under the marker, the door
            // kit's shape. Find the hinge by the door template's own convention.
            var hinge = FindDeep(marker, t => t.name.IndexOf("Hinge", StringComparison.Ordinal) >= 0);
            var count = 0;

            // The set piece ships INACTIVE — the director activates it when it fires.
            // Photograph the fired truth: activate for the shot, restore after.
            var inactive = marker.GetComponentsInChildren<Transform>(true)
                .Where(t => !t.gameObject.activeSelf).Select(t => t.gameObject).ToList();
            foreach (var go in inactive) go.SetActive(true);

            // Frame the SHELL's rendered bounds, not the marker point — the marker's
            // forward is the corridor axis, not the wall normal, and the first cut of
            // this tool photographed an empty corridor with the cabinet off-frame.
            var renderers = marker.GetComponentsInChildren<Renderer>(true);
            var subject = marker.position + Vector3.up * 0.9f;
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                subject = bounds.center;
            }

            // Stand off along the wall normal, not the corridor axis: the shell hangs
            // on a wall, so the readable viewpoint is in front of its face. Try both
            // sides and keep the one with walking room.
            var normal = marker.right;
            if (Physics.Raycast(subject, normal, out _, 1.8f)) normal = -normal;
            var sizeNote = "none";
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                sizeNote = b.size.ToString("0.000");
            }

            // The bounds line is the fast lying-flat detector: a shell whose height is
            // not the tallest world axis is the defect this tool's first outing caught.
            Debug.Log("[StartleShot] cabinet marker=" + marker.position + " subject=" + subject
                + " renderers=" + renderers.Length + " boundsSize=" + sizeNote
                + " hinge=" + (hinge != null ? hinge.name : "none"));
            FrameAt(camera, subject, subject + normal * 1.9f + Vector3.up * (EyeMetres - 0.9f));
            count += Capture(camera, outDir, tag + "_cabinet_shut.png");

            // Diagnostic wide: 3.5 m back down the corridor axis, whole assembly in frame.
            FrameAt(camera, subject, marker.position + marker.forward * 3.5f + Vector3.up * EyeMetres);
            count += Capture(camera, outDir, tag + "_cabinet_wide.png");


            if (hinge != null)
            {
                var kept = hinge.localRotation;
                // The runtime swings 80-100°; photograph the far end of the band.
                hinge.localRotation = kept * Quaternion.Euler(0f, -100f, 0f);
                count += Capture(camera, outDir, tag + "_cabinet_slammed.png");
                hinge.localRotation = kept;
            }
            else
            {
                Debug.LogWarning("[StartleShot] cabinet marker '" + marker.name + "' has no Hinge child — "
                    + "only the shut state was photographed. If the leaf is runtime-built, this is expected "
                    + "and the slammed state needs the PlayMode path.");
            }

            foreach (var go in inactive) go.SetActive(false);
            return count;
        }

        private static int ShootSkitterer(Transform group, Camera camera, string outDir, string tag)
        {
            var marker = FirstMarker(group, "Startle_Skitterer");
            // The template ships DISABLED (the gun-template pattern), and
            // GameObject.Find cannot see inactive objects — walk every transform.
            var template = UnityEngine.Object
                .FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == HorrorGame.EditorTools.SceneGen.MapSceneBuilder.StartleSkittererTemplateName)
                ?.gameObject;
            if (marker == null || template == null) return 0;

            // Mid-crossing: one body left of the corridor centre, 4 m ahead of the eye,
            // matching StageSkitterer's band. The template is the disabled scene object
            // the runtime clones — enable it in place for the photograph only.
            var wasActive = template.activeSelf;
            var keptPos = template.transform.position;
            var keptRot = template.transform.rotation;
            try
            {
                var eye = marker.position - marker.forward * StandOffMetres + Vector3.up * EyeMetres;
                var crossing = marker.position + marker.forward * 4f + marker.right * 0.4f;
                template.SetActive(true);
                template.transform.SetPositionAndRotation(
                    new Vector3(crossing.x, marker.position.y + 0.01f, crossing.z),
                    Quaternion.LookRotation(marker.right, Vector3.up));

                camera.transform.SetPositionAndRotation(eye,
                    Quaternion.LookRotation((crossing + Vector3.up * 0.2f - eye).normalized, Vector3.up));
                return Capture(camera, outDir, tag + "_skitterer_crossing.png");
            }
            finally
            {
                template.SetActive(wasActive);
                template.transform.SetPositionAndRotation(keptPos, keptRot);
            }
        }

        private static int ShootPipe(Transform group, Camera camera, string outDir, string tag)
        {
            var marker = FirstMarker(group, "Startle_PipeStub");
            if (marker == null) return 0;
            Frame(camera, marker.position + Vector3.up * 0.2f, marker);
            // The vent burst itself is a runtime particle system configured in
            // StartleDirector; the still photographs the torn stub the burst erupts
            // from. The burst's look was reviewed from the prop agent's beam renders.
            return Capture(camera, outDir, tag + "_pipestub.png");
        }

        private static int ShootBulbDeath(Transform group, Camera camera, string outDir, string tag)
        {
            var marker = FirstMarker(group, "Startle_BulbDeath");
            if (marker == null) return 0;

            // The nearest working Filament within the runtime's hunt range.
            var filament = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(l => l.name == "Filament" && l.enabled)
                .OrderBy(l => (l.transform.position - marker.position).sqrMagnitude)
                .FirstOrDefault();
            if (filament == null)
            {
                Debug.LogWarning("[StartleShot] no working Filament near '" + marker.name + "' — the bulb-death "
                    + "startle would refuse to fire here too (that is its designed behaviour, not a defect).");
                return 0;
            }

            var count = 0;
            Frame(camera, filament.transform.position, marker);
            count += Capture(camera, outDir, tag + "_bulb_alive.png");
            filament.enabled = false;
            count += Capture(camera, outDir, tag + "_bulb_dead.png");
            filament.enabled = true;
            return count;
        }

        private static int ShootGlimpse(Transform group, Camera camera, string outDir, string tag)
        {
            var marker = FirstMarker(group, "Startle_Glimpse");
            if (marker == null) return 0;

            // The same asset the runtime stages: the PresenceRig-built prefab (upright
            // wrapper, 2.05 m asserted, URP materials bound). The raw FBX imports lying
            // on its back at 0.186 m tall — photographing it would photograph the very
            // defect the runtime path was just fixed to avoid.
            var figureAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Presence/Presence_Figure.prefab")
                ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Presence/Presence_Figure.fbx");
            if (figureAsset == null) return 0;

            // The runtime places the figure 3.5-6.05 m from the eye where the beam is
            // NOT pointing. A photograph of "not where you are looking" is a black
            // frame, so this stages the moment AFTER the snap — the player has just
            // whipped the beam onto the spot the figure occupied. 5 m, half a corridor
            // off-axis: the edge-of-beam read the design intends.
            var figure = (GameObject)PrefabUtility.InstantiatePrefab(figureAsset);
            try
            {
                // 3.5 m dead ahead on the corridor axis — the runtime's NEAR bound and
                // the beam centred on it: the exact "snapped the beam onto it" frame.
                // The first cut stood it 5 m ahead and 0.9 m aside, which put it
                // behind a maze wall; the marker's own corridor cell is the one place
                // guaranteed walkable, so stay close and on-axis.
                var eye = marker.position + Vector3.up * EyeMetres;
                // The corridor's clear direction is measured, not assumed: raycast the
                // four cardinals from the eye and stand the figure down the longest.
                var open = marker.forward;
                var best = 0f;
                foreach (var dir in new[] { marker.forward, -marker.forward, marker.right, -marker.right })
                {
                    var reach = Physics.Raycast(eye, dir, out var hit, 6f) ? hit.distance : 6f;
                    if (reach > best) { best = reach; open = dir; }
                }

                var stand = marker.position + open * Mathf.Min(3.5f, best - 0.6f);
                Debug.Log("[StartleShot] glimpse marker=" + marker.position + " open=" + open
                    + " best=" + best.ToString("0.0") + " stand=" + stand
                    + " figureRenderers=" + figure.GetComponentsInChildren<Renderer>(true).Length);
                figure.transform.SetPositionAndRotation(
                    new Vector3(stand.x, marker.position.y, stand.z),
                    Quaternion.LookRotation((eye - stand).WithY(0f).normalized, Vector3.up));

                var fr = figure.GetComponentsInChildren<Renderer>(true);
                if (fr.Length > 0)
                {
                    var fb = fr[0].bounds;
                    for (var i = 1; i < fr.Length; i++) fb.Encapsulate(fr[i].bounds);
                    Debug.Log("[StartleShot] glimpse figure height=" + fb.size.y.ToString("0.000")
                        + " m (prefab path; 2.05 expected)");
                }

                camera.transform.SetPositionAndRotation(eye,
                    Quaternion.LookRotation((stand + Vector3.up * 1.2f - eye).normalized, Vector3.up));
                return Capture(camera, outDir, tag + "_glimpse.png");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        // ------------------------------------------------------------------

        private static Transform FirstMarker(Transform group, string prefix)
        {
            foreach (Transform child in group)
            {
                if (child.name.StartsWith(prefix, StringComparison.Ordinal)) return child;
            }

            Debug.LogWarning("[StartleShot] no marker with prefix '" + prefix + "'.");
            return null;
        }

        private static Transform FindDeep(Transform root, Func<Transform, bool> match)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != root && match(t)) return t;
            }

            return null;
        }

        /// <summary>Camera at eye height, StandOffMetres back along the marker's facing, looking at the subject.</summary>
        private static void Frame(Camera camera, Vector3 subject, Transform marker)
        {
            FrameAt(camera, subject, marker.position - marker.forward * StandOffMetres + Vector3.up * EyeMetres);
        }

        private static void FrameAt(Camera camera, Vector3 subject, Vector3 eye)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((subject - eye).normalized, Vector3.up));
        }

        private static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);

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
