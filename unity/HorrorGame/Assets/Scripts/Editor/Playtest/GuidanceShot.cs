#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Gameplay.Guidance;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HorrorGame.EditorTools.Playtest
{
    /// <summary>
    /// Photographs the guidance overlays over the real map, so "is this legible?" can be
    /// answered by looking rather than by hoping.
    /// <para>
    /// <b>Why a tool and not a screenshot key.</b> The overlays are the whole point of
    /// this change and the failure mode is silent: dim grey text over a lit concrete
    /// floor, or a panel sitting on top of §08's shop rows. Neither shows up in a test.
    /// So the frames are rendered the same way <c>SceneShot</c> renders the map — one
    /// camera into a <c>RenderTexture</c> — with every canvas in the scene temporarily
    /// switched to camera space, because a Screen Space Overlay canvas is composited by
    /// the display and never appears in <c>Camera.Render</c>.
    /// </para>
    /// <para>
    /// The switch is reverted before the scene is left, and the scene is never saved, so
    /// nothing this writes can reach a player.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath . \
    ///   -executeMethod HorrorGame.EditorTools.Playtest.GuidanceShot.Batch -shotTag guide
    /// </code>
    /// </summary>
    public static class GuidanceShot
    {
        private const string SoloScenePath = "Assets/Scenes/Map_FirstSketch_Solo.unity";
        private const string ShotFolder = "Shots";

        /// <summary>1080p, which is also <c>UiStyle.ReferenceResolution</c> — so the canvas scales 1:1 and the frame is what a tester sees.</summary>
        private const int Width = 1920;

        /// <summary>See <see cref="Width"/>.</summary>
        private const int Height = 1080;

        /// <summary>Renders every guidance state and writes them into <c>Shots/</c>.</summary>
        [MenuItem("HorrorGame/Play/Photograph Guidance Overlays", priority = 23)]
        public static void Menu()
        {
            Debug.Log("[GuidanceShot] " + Capture(Tag()));
        }

        /// <summary>Batch entry point. Exits non-zero when no frame could be rendered.</summary>
        public static void Batch()
        {
            try
            {
                var report = Capture(Tag());
                Debug.Log("[GuidanceShot] " + report);
                EditorApplication.Exit(report.Contains("0 frames") ? 1 : 0);
            }
            catch (Exception error)
            {
                Debug.LogError("[GuidanceShot] " + error);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Opens the solo scene, drives one match into each §01 phase worth photographing,
        /// and renders a frame at each.
        /// </summary>
        private static string Capture(string tag)
        {
            if (!File.Exists(SoloScenePath) && !SoloPlaytest.BuildScene())
            {
                return "0 frames — the solo scene could not be built.";
            }

            EditorSceneManager.OpenScene(SoloScenePath, OpenSceneMode.Single);

            var director = UnityEngine.Object.FindFirstObjectByType<MatchDirector>();
            var motor = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
            var screen = UnityEngine.Object.FindFirstObjectByType<PlaytestGuidanceScreen>();

            if (director == null || motor == null || screen == null)
            {
                return "0 frames — the scene is missing the match, the rig or the guidance screen.";
            }

            if (!director.BeginMatch(SoloPlaytest.PlaytestSeed))
            {
                return "0 frames — BeginMatch refused.";
            }

            var camera = motor.GetComponentInChildren<Camera>();
            if (camera == null)
            {
                return "0 frames — the player rig has no camera.";
            }

            screen.Bind(director);
            screen.SetVisible(true);

            Directory.CreateDirectory(ShotFolder);
            var written = new List<string>();
            var map = director.Map;

            // 1 · 지상, controls card up. The brightest frame, and the one a tester meets first.
            screen.SetPanels(controls: true, questions: false);
            written.Add(Shoot(director, screen, camera, tag + "_surface"));

            // 2 · 지하, §14's questions up. The dark frame — the legibility question.
            if (map != null)
            {
                MoveTo(motor.transform, FarthestSite(map));
                StepOnce(director);
                screen.SetPanels(controls: false, questions: true);
                written.Add(Shoot(director, screen, camera, tag + "_underground"));

                // 3 · 귀환 — §08's shop opens by itself at the van and covers the screen.
                // The one frame where the guidance line has to share the display with a
                // full-screen panel it does not own.
                MoveTo(motor.transform, map.Entrance);
                StepOnce(director);
                screen.SetPanels(controls: false, questions: false);
                written.Add(Shoot(director, screen, camera, tag + "_van"));

                // 4 · 목표물 운반 — the load line, live.
                var prop = director.ObjectiveProp;
                if (prop != null)
                {
                    MoveTo(motor.transform, prop.transform.position);
                    StepOnce(director);
                    director.TryTakeObjective(out _);
                    StepOnce(director);
                    written.Add(Shoot(director, screen, camera, tag + "_carry"));

                    // 5 · §02's end screen with the run summary beside it.
                    MoveTo(motor.transform, map.Entrance);
                    StepOnce(director);
                    written.Add(Shoot(director, screen, camera, tag + "_end"));
                }
            }

            return written.Count + " frames: " + string.Join(", ", written);
        }

        /// <summary>
        /// Repaints, puts every canvas in front of the camera, renders, and puts them back.
        /// </summary>
        private static string Shoot(
            MatchDirector director, PlaytestGuidanceScreen screen, Camera camera, string name)
        {
            screen.Redraw();

            // Post-processing off for the duration. In a real session these canvases are
            // Screen Space Overlay and are composited by the display, so URP's grade and
            // its chromatic aberration never touch them; leaving it on here would put
            // colour fringing on every glyph and answer "is this legible" with an
            // artefact of the photograph rather than with the truth.
            var urp = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var hadPost = urp != null && urp.renderPostProcessing;
            if (urp != null)
            {
                urp.renderPostProcessing = false;
            }

            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            var restore = new List<CanvasState>();

            foreach (var canvas in canvases)
            {
                if (!canvas.isRootCanvas)
                {
                    // A nested canvas inherits its root's mode; switching it does nothing
                    // and restoring it would fight the root.
                    continue;
                }

                restore.Add(new CanvasState(canvas));
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;

                // One plane for all of them, so sortingOrder decides what covers what —
                // which is the thing being checked. Pressed right up against the near
                // clip plane so nothing in the world, including the player's own body
                // mesh, gets in front of it: an overlay canvas is never occluded, and a
                // photograph that showed it occluded would be reporting a defect that
                // does not exist.
                canvas.planeDistance = camera.nearClipPlane * 1.1f;
            }

            Canvas.ForceUpdateCanvases();

            var path = Path.Combine(ShotFolder, name + ".png");
            RenderTo(camera, path);

            foreach (var entry in restore)
            {
                entry.Restore();
            }

            if (urp != null)
            {
                urp.renderPostProcessing = hadPost;
            }

            Canvas.ForceUpdateCanvases();
            return name + ".png";
        }

        /// <summary>How a canvas was configured before the shot, so it can be put back.</summary>
        private readonly struct CanvasState
        {
            private readonly Canvas _canvas;
            private readonly RenderMode _mode;
            private readonly Camera? _camera;
            private readonly float _plane;

            internal CanvasState(Canvas canvas)
            {
                _canvas = canvas;
                _mode = canvas.renderMode;
                _camera = canvas.worldCamera;
                _plane = canvas.planeDistance;
            }

            internal void Restore()
            {
                _canvas.renderMode = _mode;
                _canvas.worldCamera = _camera;
                _canvas.planeDistance = _plane;
            }
        }

        private static void RenderTo(Camera camera, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
            };

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

                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static void MoveTo(Transform player, Vector3 target)
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            player.position = target;

            if (controller != null)
            {
                controller.enabled = true;
            }

            Physics.SyncTransforms();
        }

        private static void StepOnce(MatchDirector director)
        {
            var context = default(ClueReadContext);
            context.ClueId = -1;
            director.SetClueContext(context);
            director.StepMatch(GameConstants.FixedStep);
        }

        private static Vector3 FarthestSite(MatchMap map)
        {
            var best = map.Entrance;
            var bestDistance = -1f;
            var monsterStart = map.MonsterSpawn != null ? map.MonsterSpawn.position : map.Entrance;

            for (var i = 0; i < map.CandidateSites.Count; i++)
            {
                var site = map.CandidateSites[i].position;
                if (map.IsOnSurface(site))
                {
                    continue;
                }

                var distance = Vector3.Distance(site, monsterStart);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = site;
                }
            }

            return best;
        }

        private static string Tag()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-shotTag", StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return "guide";
        }
    }
}
