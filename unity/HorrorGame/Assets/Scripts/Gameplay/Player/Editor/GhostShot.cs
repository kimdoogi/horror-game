#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HorrorGame.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace HorrorGame.Gameplay.PlayerEditor
{
    /// <summary>
    /// Photographs §09's 유령 at the two ranges it has to work at, in the game's own dark.
    /// <para>
    /// <b>3 m and 15 m, and neither is arbitrary.</b> 3 m is what a ghost looks like to
    /// itself — §09 gives it a free camera, so its own body is the thing it spends the
    /// rest of the match with. 15 m is <c>GameConstants.ObserverRange</c>, the range §04's
    /// 관측자 works at and the one <c>MonsterShot</c> and <c>PlayerBodyShot</c> already hold
    /// their subjects to: it is the distance at which a living player might catch one at
    /// the edge of a beam, which is the whole of 「방금 뭔가 흔들렸어?」.
    /// </para>
    /// <para>
    /// <b>Every frame is shot twice, with the observer's torch off and on.</b> That pair
    /// is the measurement this model exists to pass. §09's ghost is authored at an albedo
    /// an order of magnitude under the darkest §12 wall and carries a constant emission
    /// instead, so a beam put on it should change almost nothing — everything else in the
    /// game is learned by pointing the torch at it, and this is the one thing that does
    /// not answer. If the torch-on and torch-off readings differ much, the design is not
    /// in the asset and the ghost is just a dim monster.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath unity/HorrorGame -executeMethod \
    ///   HorrorGame.Gameplay.PlayerEditor.GhostShot.Batch -shotTag ghost
    /// </code>
    /// Run it WITHOUT <c>-nographics</c>, or every frame is black.
    /// </summary>
    public static class GhostShot
    {
        private const string OutputDir = "Shots";
        private const string ModelPath = "Assets/Models/Characters/Ghost.fbx";
        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>Metres between the camera and the ghost. See the class note.</summary>
        private static readonly float[] Distances = { 3f, 15f };

        /// <summary>
        /// Where <c>gen_player_ai.py</c> puts <c>HeadCameraAnchor</c>, in metres. A
        /// property of the model rather than a tuned game number, which is why it is not
        /// in <c>GameConstants</c> — the same literal <c>PlayerFeelHarnessMenu</c> builds
        /// its harness camera at.
        /// </summary>
        private const float EyeHeightMetres = 1.63f;

        /// <summary>Batch entry point. 0 on success, 1 on an exception, 2 on a missing asset.</summary>
        public static void Batch()
        {
            try
            {
                var scenePath = ViewMotionShot.ArgValue("-shotScene")
                    ?? "Assets/Scenes/Map_FirstSketch.unity";
                var tag = ViewMotionShot.ArgValue("-shotTag") ?? "ghost";
                if (!File.Exists(scenePath))
                {
                    Debug.LogError("[GhostShot] no scene at " + scenePath);
                    EditorApplication.Exit(2);
                    return;
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                Capture(scene, tag);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[GhostShot] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Menu twin of <see cref="Batch"/>.</summary>
        [MenuItem("HorrorGame/Play/Photograph the Ghost")]
        public static void ShootMenu()
        {
            Capture(EditorSceneManager.GetActiveScene(), "ghost");
        }

        private static void Capture(Scene scene, string tag)
        {
            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDir);
            Directory.CreateDirectory(root);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    ModelPath + " is not in the project. Run tools/blender/gen_ghost.py.");
            }

            // The night from the code, not from whatever AtmosphereSetup last baked into
            // this scene — see FirstPersonHandsShot for what that cost the last time.
            NightAtmosphere.ApplyEnvironment(NightAtmosphere.ForTier(0));

            var ghost = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var holder = new GameObject("GhostShotCamera");
            var camera = holder.AddComponent<Camera>();
            var beam = new GameObject("GhostShotBeam").AddComponent<Light>();
            beam.transform.SetParent(holder.transform, false);
            FlashlightBeam.Apply(beam);

            var lines = new List<string>();
            try
            {
                ViewMotionShot.PrepareCamera(camera);
                ViewMotionShot.DisableOtherCameras(camera);

                var stand = ViewMotionShot.FindStandingSpot(scene);
                var heading = Quaternion.Euler(0f, stand.HeadingDegrees, 0f) * Vector3.forward;
                var eye = stand.Position + Vector3.up * EyeHeightMetres;
                holder.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(heading));

                foreach (var wanted in Distances)
                {
                    var distance = Mathf.Min(wanted, stand.ClearMetres - 1.2f);
                    if (distance < 2f)
                    {
                        Debug.LogWarning("[GhostShot] only "
                            + stand.ClearMetres.ToString("F1", CultureInfo.InvariantCulture)
                            + " m of clear run here; skipping " + wanted + " m.");
                        continue;
                    }

                    // Facing the camera, standing on the floor: the wisps reach z = 0 in
                    // the model, so its own origin is its feet.
                    ghost.transform.SetPositionAndRotation(
                        stand.Position + heading * distance,
                        Quaternion.LookRotation(-heading, Vector3.up));

                    foreach (var lit in new[] { false, true })
                    {
                        beam.enabled = lit;
                        var name = string.Format(CultureInfo.InvariantCulture, "{0}_{1:00}m_{2}",
                            tag, Mathf.RoundToInt(distance), lit ? "beam" : "dark");
                        var stats = Render(camera, Path.Combine(root, name + ".png"));
                        lines.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0,-22} mean {1,5:F2}  legible% {2,5:F1}  brightest {3,5:F1}",
                            name, stats.Mean, stats.LegiblePercent, stats.Brightest));
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ghost);
                UnityEngine.Object.DestroyImmediate(holder);
            }

            Debug.Log("[GhostShot] §09's 유령, with the observer's torch off and on\n  "
                + string.Join("\n  ", lines)
                + "\n  The pair is the measurement: this model is authored at an albedo an "
                + "order of magnitude under a §12 wall and lit from within, so a beam on it "
                + "should barely move these numbers.");
        }

        private readonly struct Stats
        {
            public Stats(float mean, float legible, float brightest)
            {
                Mean = mean;
                LegiblePercent = legible;
                Brightest = brightest;
            }

            public float Mean { get; }

            public float LegiblePercent { get; }

            public float Brightest { get; }
        }

        /// <summary>
        /// Renders one frame, writes it, and returns the same three numbers
        /// <c>tools/render/frame_stats.py</c> reports — Rec.709 luma on the sRGB bytes,
        /// so a reading here and a reading from that script are the same reading.
        /// </summary>
        private static Stats Render(Camera camera, string path)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
            };
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());

                var pixels = texture.GetPixels32();
                double total = 0d;
                var legible = 0;
                var brightest = 0f;
                foreach (var pixel in pixels)
                {
                    var luma = 0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b;
                    total += luma;
                    if (luma >= 8f && luma <= 235f)
                    {
                        legible++;
                    }

                    brightest = Mathf.Max(brightest, luma);
                }

                return new Stats((float)(total / pixels.Length),
                                 100f * legible / pixels.Length, brightest);
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                UnityEngine.Object.DestroyImmediate(texture);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
