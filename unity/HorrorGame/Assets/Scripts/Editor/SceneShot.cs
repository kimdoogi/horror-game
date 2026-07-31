#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Renders scenes to PNG from batch mode, so the look can be reviewed without a
    /// human sitting in front of the editor.
    /// <para>
    /// Art direction is the one part of this project that cannot be verified by a
    /// test. §03 makes darkness the central mechanic and §05 puts the player in a
    /// narrow beam in the dark — whether that reads as frightening or as an unlit
    /// grey box is a judgement about pixels, and the only way to make it is to look
    /// at the pixels.
    /// </para>
    /// <para>
    /// Run WITHOUT <c>-nographics</c>: that flag disables the graphics device, and
    /// every shot comes out black.
    /// </para>
    /// </summary>
    public static class SceneShot
    {
        private const string OutputDir = "Shots";
        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>
        /// Batch entry point. Renders a set of viewpoints for whichever scene is named
        /// by <c>-shotScene</c>, or the first map scene found.
        /// <code>
        /// Unity -batchmode -quit -projectPath . -executeMethod HorrorGame.EditorTools.SceneShot.Batch -shotScene Assets/Scenes/Map_FirstSketch.unity
        /// </code>
        /// </summary>
        public static void Batch()
        {
            try
            {
                var scenePath = ArgValue("-shotScene") ?? "Assets/Scenes/Map_FirstSketch.unity";
                var tag = ArgValue("-shotTag") ?? "shot";

                if (!File.Exists(scenePath))
                {
                    Debug.LogError("[SceneShot] No scene at " + scenePath);
                    EditorApplication.Exit(2);
                    return;
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var shots = Capture(scene, tag);

                Debug.Log("[SceneShot] wrote " + shots.Count + " shot(s):\n  " + string.Join("\n  ", shots));
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SceneShot] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Renders a standard set of viewpoints and returns the written paths.</summary>
        public static List<string> Capture(Scene scene, string tag)
        {
            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDir);
            Directory.CreateDirectory(root);

            var written = new List<string>();
            var rig = new GameObject("[SceneShot Camera]");

            try
            {
                var camera = rig.AddComponent<Camera>();
                camera.fieldOfView = HorrorGame.Core.GameConstants.FovDefault;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 300f;
                camera.clearFlags = CameraClearFlags.Skybox;

                // The player is never in this basement without a flashlight (§03 gives
                // one to everyone), so a shot taken without one is not a picture of the
                // game. It is a picture of a room nobody will ever stand in.
                var beam = AddFlashlight(rig, on: !HasFlag("-shotNoLight"));
                var survey = AddSurveyLight();

                foreach (var view in BuildViews(scene))
                {
                    // A survey view swaps the torch for the survey light, so the two
                    // never appear in the same frame and no shot is half one and half
                    // the other.
                    beam.enabled = !view.Survey && !HasFlag("-shotNoLight");
                    survey.enabled = view.Survey;

                    rig.transform.SetPositionAndRotation(view.Position, Quaternion.Euler(view.Euler));
                    var path = Path.Combine(root, tag + "_" + view.Name + ".png");
                    RenderTo(camera, path);
                    written.Add(path);
                }

                UnityEngine.Object.DestroyImmediate(survey.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rig);
            }

            return written;
        }

        /// <summary>
        /// Chooses viewpoints from the scene's own contents rather than fixed
        /// coordinates, so a regenerated map is still framed sensibly.
        /// </summary>
        private static List<View> BuildViews(Scene scene)
        {
            var all = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(includeInactive: true))
                .ToArray();

            var bounds = ComputeBounds(scene);
            var views = new List<View>();

            // Top-down, to judge the layout as a whole. Explicitly NOT a game frame:
            // it is 60 m above a building lit by a 12 m torch, so under the game's own
            // lighting it renders as an almost entirely black rectangle — which is
            // correct, and useless for the one thing it is for. Capture() gives this
            // view a survey light of its own; see AddSurveyLight.
            var span = Mathf.Max(bounds.size.x, bounds.size.z);
            views.Add(new View(
                "overhead",
                new Vector3(bounds.center.x, bounds.max.y + Mathf.Max(30f, span * 0.9f), bounds.center.z),
                new Vector3(90f, 0f, 0f),
                survey: true));

            // Eye height at each player spawn, which is what the game actually looks
            // like — §05 is first person, so a flattering angle nobody will ever
            // occupy tells us nothing.
            var spawns = all.Where(t => t.name.StartsWith("PlayerSpawn_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.name, StringComparer.Ordinal)
                .Take(4)
                .ToArray();

            for (var i = 0; i < spawns.Length; i++)
            {
                var eye = spawns[i].position + new Vector3(0f, 1.63f, 0f);
                views.Add(new View("spawn" + i, eye, new Vector3(3f, spawns[i].eulerAngles.y, 0f)));
            }

            // One shot per zone at eye height, because §12 gives each zone its own
            // floor material and character and they should not look interchangeable.
            //
            // Pitched down a little rather than level, and aimed down the longest
            // clear line rather than at a fixed compass bearing. Both were wrong
            // before and both mattered: a level camera at 1.63 m fills the frame with
            // wall and shows almost no floor, so the reviewer was asked to tell §12's
            // five 바닥 재질 apart in pictures that barely contained one; and a fixed
            // 35° yaw put three of the five cameras a metre from a wall, which is a
            // photograph of a brick, not of a place.
            foreach (var zone in all.Where(t => t.name.StartsWith("Zone_", StringComparison.OrdinalIgnoreCase)).Take(6))
            {
                var zb = ComputeBounds(zone.gameObject);
                var eye = ClearStandingSpot(new Vector3(zb.center.x, zb.min.y + 1.63f, zb.center.z));
                views.Add(new View(zone.name, eye, new Vector3(14f, OpenestBearing(eye), 0f)));
            }

            if (spawns.Length == 0)
            {
                views.Add(new View("centre", bounds.center + new Vector3(0f, 1.63f, 0f), new Vector3(2f, 0f, 0f)));
            }

            return views;
        }

        /// <summary>
        /// The compass bearing with the most room in front of it, in degrees.
        /// <para>
        /// Answers the review question "can you tell where you are from a frame?" by
        /// not sabotaging it: the interesting thing about a §12 zone — the depth, the
        /// far doorway, the S-corridor bending away — is only ever visible along the
        /// open axis, and a camera pointed at the nearest wall shows none of it no
        /// matter how well the room is lit.
        /// </para>
        /// </summary>
        private static float OpenestBearing(Vector3 eye)
        {
            var best = 0f;
            var bestDistance = -1f;

            // 24 samples: fine enough to find a corridor mouth, coarse enough that the
            // whole set of shots still takes a second.
            for (var i = 0; i < 24; i++)
            {
                var degrees = i * 15f;
                var direction = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;

                // Unobstructed counts as the full cast, so an open hall beats a
                // corridor rather than tying with it at "no hit".
                var distance = Physics.Raycast(eye, direction, out var hit, 40f) ? hit.distance : 40f;

                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = degrees;
                }
            }

            return best;
        }

        /// <summary>
        /// Nudges a camera position out of whatever it is standing inside.
        /// <para>
        /// A zone's bounding-box centre is frequently a wall, a pillar or a stack of
        /// crates — the dressing pass puts 1074 pieces in this map, and the middle of
        /// a room is exactly where the big ones go. A shot taken from inside geometry
        /// is a black frame with a bright smear on it, and three of those in a set of
        /// five is enough to make a working scene look broken.
        /// </para>
        /// <para>
        /// Searches a widening ring for somewhere a player-sized capsule would fit,
        /// and gives up back at the original point rather than skipping the zone: a
        /// missing shot hides a problem, an ugly one shows it.
        /// </para>
        /// </summary>
        private static Vector3 ClearStandingSpot(Vector3 wanted)
        {
            // §06 puts the monster at 0.93 m across, so this is a little over a body.
            const float Radius = 0.5f;

            if (!Physics.CheckSphere(wanted, Radius))
            {
                return wanted;
            }

            for (var step = 1; step <= 5; step++)
            {
                var distance = step * 1.5f;
                for (var turn = 0; turn < 8; turn++)
                {
                    var angle = turn * Mathf.PI * 0.25f;
                    var candidate = wanted + new Vector3(
                        Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

                    if (!Physics.CheckSphere(candidate, Radius))
                    {
                        return candidate;
                    }
                }
            }

            return wanted;
        }

        /// <summary>
        /// Attaches §03's flashlight, so shots show what the player sees rather than
        /// what an unlit scene looks like.
        /// <para>
        /// Delegates every beam value to <see cref="HorrorGame.Rendering.FlashlightBeam"/>.
        /// This used to build its own light — a dimmer one, with soft shadows the
        /// player rig does not use — which meant the art was reviewed against a beam
        /// that is not in the game.
        /// </para>
        /// </summary>
        private static Light AddFlashlight(GameObject rig, bool on)
        {
            var beam = new GameObject("Flashlight").AddComponent<Light>();
            beam.transform.SetParent(rig.transform, false);
            HorrorGame.Rendering.FlashlightBeam.Apply(beam);
            beam.enabled = on;
            return beam;
        }

        /// <summary>
        /// A light that exists only so the top-down layout view is legible.
        /// <para>
        /// Deliberately not part of the game's lighting and never on in a first-person
        /// frame. The overhead shot answers a different question from the others —
        /// "are §12's zones, loops and dead ends where the validator says they are" —
        /// and answering it needs the building visible, which the game's own lighting
        /// will never make it from 60 m up. Keeping it a separate switch is what stops
        /// this convenience from quietly flattering the shots that are supposed to
        /// show the dark.
        /// </para>
        /// </summary>
        private static Light AddSurveyLight()
        {
            var go = new GameObject("[SceneShot Survey Light]");
            go.transform.rotation = Quaternion.Euler(72f, 30f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.color = new Color(0.86f, 0.90f, 1f);
            light.shadows = LightShadows.Soft;
            light.enabled = false;
            return light;
        }

        private static bool HasFlag(string flag) =>
            Environment.GetCommandLineArgs().Any(a => string.Equals(a, flag, StringComparison.Ordinal));

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

        private static Bounds ComputeBounds(Scene scene)
        {
            var renderers = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Renderer>(includeInactive: false))
                .ToArray();

            return Combine(renderers);
        }

        private static Bounds ComputeBounds(GameObject go) =>
            Combine(go.GetComponentsInChildren<Renderer>(includeInactive: false));

        private static Bounds Combine(IReadOnlyList<Renderer> renderers)
        {
            if (renderers.Count == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * 10f);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Count; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static string? ArgValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private readonly struct View
        {
            public readonly string Name;
            public readonly Vector3 Position;
            public readonly Vector3 Euler;

            /// <summary>Lit by the survey light instead of the flashlight. Not a game frame.</summary>
            public readonly bool Survey;

            public View(string name, Vector3 position, Vector3 euler, bool survey = false)
            {
                Name = name;
                Position = position;
                Euler = euler;
                Survey = survey;
            }
        }
    }
}
