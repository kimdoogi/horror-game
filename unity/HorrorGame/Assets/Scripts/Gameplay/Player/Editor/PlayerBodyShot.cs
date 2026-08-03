#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HorrorGame.Core;
using HorrorGame.Core.Movement;
using HorrorGame.Gameplay.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace HorrorGame.Gameplay.PlayerEditor
{
    /// <summary>
    /// Photographs a teammate the way the other three actually see one: down a corridor,
    /// in someone else's beam, at 3, 8 and 15 m — and measures whether the silhouette and
    /// the §04 role colour survive each distance.
    /// <para>
    /// <b>Why the distances are those three.</b> 3 m is a teammate beside you at a clue.
    /// 8 m is the length of a §12 corridor leg. 15 m is <c>GameConstants.ObserverRange</c>
    /// and the far end of §12's 시야 차단 지점 spacing — the range §04's 관측자 has to work
    /// at, and the one <c>MonsterShot</c> already holds the creature to. If a player and
    /// the monster are both unreadable at 15 m the role system and the observation posts
    /// are both decoration.
    /// </para>
    /// <para>
    /// <b>The numbers under the pictures.</b> Height in pixels, the fraction of the body's
    /// footprint that is above the frame's black floor, and the Rec.709 luma of the role
    /// band against the body around it. The last is the one that matters and the one an
    /// eye is worst at: §05 picks the five role colours for **value** separation because
    /// hue discrimination collapses in the dark, so "can you tell a 청음사 from a 주자"
    /// is a contrast measurement, not an impression.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath unity/HorrorGame -executeMethod \
    ///   HorrorGame.Gameplay.PlayerEditor.PlayerBodyShot.Batch -shotTag body1
    /// </code>
    /// Run it WITHOUT <c>-nographics</c>, or every frame is black.
    /// </summary>
    public static class PlayerBodyShot
    {
        private const string OutputDir = "Shots";
        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>Metres between the camera and the teammate. See the class note.</summary>
        private static readonly float[] Distances = { 3f, 8f, 15f };

        /// <summary>Batch entry point. 0 on success, 1 on an exception, 2 on a missing scene.</summary>
        public static void Batch()
        {
            try
            {
                var scenePath = ViewMotionShot.ArgValue("-shotScene")
                    ?? "Assets/Scenes/Map_FirstSketch.unity";
                var tag = ViewMotionShot.ArgValue("-shotTag") ?? "body";
                if (!File.Exists(scenePath))
                {
                    Debug.LogError("[PlayerBodyShot] No scene at " + scenePath);
                    EditorApplication.Exit(2);
                    return;
                }

                Capture(EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single), tag);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PlayerBodyShot] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Renders the set and prints the report.</summary>
        public static void Capture(Scene scene, string tag)
        {
            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDir);
            Directory.CreateDirectory(root);

            var subject = PlayerFeelHarnessMenu.BuildRig();
            var observer = PlayerFeelHarnessMenu.BuildRig();
            if (subject == null || observer == null)
            {
                throw new InvalidOperationException(
                    "PlayerFeelHarnessMenu.BuildRig returned null; Player.fbx is missing.");
            }

            var lines = new List<string>();
            try
            {
                // The subject is a REMOTE player, which is the whole point: §13 draws the
                // other three whole and PlayerFirstPersonView hides only its owner's body.
                // Photographing an owner rig would photograph a shadow.
                var view = subject.GetComponent<PlayerFirstPersonView>();
                view.IsOwner = false;
                view.Apply();
                view.SetHandPropVisible(true);

                // See FirstPersonHandsShot: the night comes from the code, not from
                // whatever AtmosphereSetup last baked into this scene file.
                HorrorGame.Rendering.NightAtmosphere.ApplyEnvironment(
                    HorrorGame.Rendering.NightAtmosphere.ForTier(0));

                var stand = ViewMotionShot.FindStandingSpot(scene);
                var heading = Quaternion.Euler(0f, stand.HeadingDegrees, 0f) * Vector3.forward;

                var camera = observer.GetComponentInChildren<Camera>();
                ViewMotionShot.PrepareCamera(camera);
                ViewMotionShot.DisableOtherCameras(camera);
                observer.GetComponent<PlayerMotor>().SteppedExternally = true;
                observer.GetComponent<PlayerViewMotion>().TickedExternally = true;
                observer.GetComponent<PlayerLook>().SetLook(stand.HeadingDegrees, 0f);

                // The observer's own body must not be in its own frame, and its own torch
                // must be lit: the whole question is what a teammate looks like in the one
                // light §03 allows.
                var observerView = observer.GetComponent<PlayerFirstPersonView>();
                observerView.IsOwner = true;
                observerView.Apply();
                observerView.SetHandPropVisible(true);
                var torch = observer.GetComponentInChildren<PlayerFlashlight>();
                if (torch != null)
                {
                    torch.State.TryTurnOn();
                }

                ViewMotionShot.Teleport(observer, stand.Position);
                for (var i = 0; i < 40; i++)
                {
                    observer.GetComponent<PlayerMotor>().Step(new MoveInput(0f, 0f, false), 0.02f);
                }

                // Clamped to the corridor that is actually there. The first run put the
                // teammate at 15 m in a room whose longest clear run is under 13, so the
                // subject stood inside the end wall and the 15 m frame photographed brick
                // — with a projected bounding box still reporting 38 px of "body".
                foreach (var wanted in Distances)
                {
                    var distance = Mathf.Min(wanted, stand.ClearMetres - 1.2f);
                    if (distance < 2f)
                    {
                        Debug.LogWarning("[PlayerBodyShot] the chosen stand has only "
                            + stand.ClearMetres.ToString("F1", CultureInfo.InvariantCulture)
                            + " m of clear run; skipping " + wanted + " m.");
                        continue;
                    }

                    ViewMotionShot.Teleport(subject, stand.Position + heading * distance);
                    subject.transform.rotation = Quaternion.LookRotation(-heading, Vector3.up);
                    for (var i = 0; i < 12; i++)
                    {
                        subject.GetComponent<PlayerMotor>().Step(new MoveInput(0f, 0f, false), 0.02f);
                    }

                    // Without this the subject stands in the bind pose — arms straight out
                    // sideways — because nothing advances an Animator outside play mode.
                    // The first run photographed a T-posed teammate and the silhouette that
                    // is the whole subject of this shot was the one thing not being shown.
                    var animator = subject.GetComponentInChildren<Animator>();
                    if (animator != null)
                    {
                        animator.Update(0f);
                        for (var i = 0; i < 30; i++)
                        {
                            animator.Update(0.033f);
                        }
                    }

                    // Named after the distance TAKEN, not the distance wanted. ART.md
                    // §7.13: the clamp above is correct and the filename was not, so every
                    // run since this tool existed has written land_body_15m.png at 10 m —
                    // GameConstants.ObserverRange, the whole reason 15 is in the list, has
                    // never actually been photographed and the file said it had. A shot
                    // that lies about where it was taken from is worse than a missing one.
                    var name = string.Format(CultureInfo.InvariantCulture,
                        "{0}_{1:00}m", tag, Mathf.RoundToInt(distance));
                    if (distance < wanted - 0.05f)
                    {
                        Debug.LogWarning(string.Format(CultureInfo.InvariantCulture,
                            "[PlayerBodyShot] wanted {0:0} m and the longest clear run here "
                            + "is {1:0.0} m, so this frame is {2:0.0} m and is named for it. "
                            + "Until a stand with {3:0.0} m of run is found, the far band is "
                            + "not being photographed at all.",
                            wanted, stand.ClearMetres, distance, wanted + 1.2f));
                    }
                    var pixels = Render(camera, Path.Combine(root, name + ".png"));
                    lines.Add(Measure(name, distance, camera, subject, pixels));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(subject);
                UnityEngine.Object.DestroyImmediate(observer);
            }

            Debug.Log("[PlayerBodyShot] a teammate in someone else's beam\n"
                + "  shot            dist   px tall  body luma   wall luma  contrast  role vs body\n"
                + string.Join("\n", lines));
        }

        /// <summary>Renders one frame to a PNG and returns its pixels.</summary>
        private static Color[] Render(Camera camera, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
            };
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return texture.GetPixels();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(texture);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        /// <summary>
        /// Turns one frame into the four numbers that decide whether the model works at
        /// this distance.
        /// <para>
        /// The body's box is projected rather than searched for: which pixels are the
        /// player is a fact about the scene, and finding them by brightness would only
        /// measure whichever of the player and the wall happened to be brighter. Contrast
        /// is against a ring just outside that box — the wall the silhouette has to be
        /// read from — and it is the same quantity <c>MonsterShot</c> holds the creature
        /// to at the same 15 m, so the two are comparable.
        /// </para>
        /// </summary>
        private static string Measure(string name, float distance, Camera camera,
                                      GameObject subject, Color[] pixels)
        {
            var bounds = new Bounds(subject.transform.position + Vector3.up * 0.9f, Vector3.zero);
            foreach (var renderer in subject.GetComponentsInChildren<Renderer>(true))
            {
                bounds.Encapsulate(renderer.bounds);
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                var point = camera.WorldToScreenPoint(corner);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            var x0 = Mathf.Clamp(Mathf.FloorToInt(min.x), 0, Width - 1);
            var x1 = Mathf.Clamp(Mathf.CeilToInt(max.x), 0, Width - 1);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(min.y), 0, Height - 1);
            var y1 = Mathf.Clamp(Mathf.CeilToInt(max.y), 0, Height - 1);

            var body = new List<float>();
            var ring = new List<float>();
            var roleHigh = 0f;
            for (var y = Mathf.Max(0, y0 - 30); y <= Mathf.Min(Height - 1, y1 + 30); y++)
            {
                for (var x = Mathf.Max(0, x0 - 30); x <= Mathf.Min(Width - 1, x1 + 30); x++)
                {
                    var c = pixels[y * Width + x];
                    var luma = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    if (x >= x0 && x <= x1 && y >= y0 && y <= y1)
                    {
                        body.Add(luma);
                        roleHigh = Mathf.Max(roleHigh, luma);
                    }
                    else
                    {
                        ring.Add(luma);
                    }
                }
            }

            var bodyMean = Mean(body);
            var wallMean = Mean(ring);
            return string.Format(CultureInfo.InvariantCulture,
                "  {0,-14} {1,4:F0}m {2,6} px  {3,8:F4}  {4,10:F4}  {5,8:F4}  {6,8:F4}",
                name, distance, y1 - y0, bodyMean, wallMean,
                Mathf.Abs(bodyMean - wallMean), Mathf.Abs(roleHigh - bodyMean));
        }

        private static float Mean(List<float> values)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            var total = 0f;
            foreach (var value in values)
            {
                total += value;
            }

            return total / values.Count;
        }
    }
}
