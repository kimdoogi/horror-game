#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HorrorGame.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.EditorTools.Rendering
{
    /// <summary>
    /// Times what one frame of this map costs to render, so a look change can be
    /// judged against its price instead of only against its picture.
    /// <para>
    /// Every lever in <see cref="AtmosphereSetup"/> — SSAO radius, shadow
    /// resolution, MSAA, an extra Volume override — is free in a screenshot and
    /// not free in play. Without a number here the honest sentence "this looks
    /// better" and the dishonest one "this ships" are indistinguishable, and a
    /// horror game that runs at 30 fps on the owner's machine is not shippable.
    /// </para>
    /// <para>
    /// <b>What this measures, exactly.</b> Wall-clock time for
    /// <see cref="Camera.Render"/> into an off-screen target at
    /// <see cref="Width"/>×<see cref="Height"/>, with §03's flashlight attached,
    /// at the same viewpoints <see cref="SceneShot"/> photographs. Each frame is
    /// followed by a one-pixel read-back, which stalls the CPU until the GPU has
    /// finished — without it Metal queues the work and the timer measures the
    /// submission, not the render.
    /// </para>
    /// <para>
    /// <b>What it is not.</b> Not a player-loop frame time: no physics, no
    /// animation, no networking, no UI, and the editor is running a debug build of
    /// its own scripts. Treat it as the renderer's share of the frame, and as a
    /// before/after comparison against itself — which is the question a look change
    /// actually raises.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath . \
    ///   -executeMethod HorrorGame.EditorTools.Rendering.FrameCost.Batch \
    ///   -shotScene Assets/Scenes/Map_FirstSketch.unity -costTag before
    /// </code>
    /// Run it WITHOUT <c>-nographics</c>. That flag disables the graphics device;
    /// the renders then cost nothing because they do not happen.
    /// </summary>
    public static class FrameCost
    {
        /// <summary>1080p, because that is what the owner's machine will run it at, and the cost of a pass scales with pixels.</summary>
        private const int Width = 1920;

        private const int Height = 1080;

        /// <summary>Frames rendered and thrown away before timing starts: shader variants, shadow maps and the SSAO history all compile or fill on first use.</summary>
        private const int WarmupFrames = 12;

        /// <summary>Frames timed per viewpoint. The median of this many is stable to about 2 % run to run.</summary>
        private const int TimedFrames = 40;

        /// <summary>Batch entry point.</summary>
        public static void Batch()
        {
            try
            {
                var scenePath = ArgValue("-shotScene") ?? "Assets/Scenes/Map_FirstSketch.unity";
                var tag = ArgValue("-costTag") ?? "cost";
                var tier = int.TryParse(ArgValue("-atmoTier"), out var t) ? t : 0;

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                // The same in-memory environment ShotBatch applies. A cost measured
                // against the map's saved ambience and a picture taken against the
                // §07 table would be two different frames.
                NightAtmosphere.ApplyEnvironment(NightAtmosphere.ForTier(tier));
                var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/HorrorGame_NightSky.mat");
                if (sky != null)
                {
                    RenderSettings.skybox = sky;
                }

                Report(Measure(scene), tag, scenePath, tier);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[FrameCost] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>One viewpoint's timing, in milliseconds.</summary>
        public readonly struct Sample
        {
            public Sample(string name, double median, double p95)
            {
                Name = name;
                MedianMs = median;
                P95Ms = p95;
            }

            public string Name { get; }

            public double MedianMs { get; }

            public double P95Ms { get; }
        }

        /// <summary>Times every first-person viewpoint in the scene and returns one sample each.</summary>
        public static List<Sample> Measure(Scene scene)
        {
            var samples = new List<Sample>();
            var rig = new GameObject("[FrameCost Camera]");

            try
            {
                var camera = rig.AddComponent<Camera>();
                camera.fieldOfView = HorrorGame.Core.GameConstants.FovDefault;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 300f;
                camera.clearFlags = CameraClearFlags.Skybox;

                var beam = new GameObject("Flashlight").AddComponent<Light>();
                beam.transform.SetParent(rig.transform, false);
                FlashlightBeam.Apply(beam);

                // MSAA is a pipeline setting, so the target has to be able to honour
                // it or the measurement quietly costs less than the game does.
                var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing),
                };
                var readback = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);

                try
                {
                    camera.targetTexture = target;

                    foreach (var view in Viewpoints(scene))
                    {
                        rig.transform.SetPositionAndRotation(view.Position, Quaternion.Euler(view.Euler));
                        samples.Add(TimeOne(camera, target, readback, view.Name));
                    }
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = null;
                    UnityEngine.Object.DestroyImmediate(readback);
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rig);
            }

            return samples;
        }

        private static Sample TimeOne(Camera camera, RenderTexture target, Texture2D readback, string name)
        {
            for (var i = 0; i < WarmupFrames; i++)
            {
                RenderAndSync(camera, target, readback);
            }

            var times = new double[TimedFrames];
            for (var i = 0; i < TimedFrames; i++)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                RenderAndSync(camera, target, readback);
                times[i] = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }

            Array.Sort(times);
            return new Sample(name, times[TimedFrames / 2], times[(int)(TimedFrames * 0.95f)]);
        }

        /// <summary>
        /// Renders one frame and blocks until the GPU has actually produced it.
        /// <para>
        /// <see cref="Camera.Render"/> returns as soon as the commands are queued.
        /// Timing it alone reports how fast this machine can fill a command buffer,
        /// which is the same number before and after any shading change — the
        /// measurement would look stable and mean nothing. Reading a single pixel
        /// back forces the queue to drain first.
        /// </para>
        /// </summary>
        private static void RenderAndSync(Camera camera, RenderTexture target, Texture2D readback)
        {
            camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0, recalculateMipMaps: false);
            readback.Apply(updateMipmaps: false);
            RenderTexture.active = previous;
        }

        /// <summary>
        /// The first-person viewpoints, chosen by the same rules
        /// <see cref="SceneShot"/> uses — player spawns, then one per §12 zone aimed
        /// down its longest clear line.
        /// <para>
        /// Duplicated rather than shared because SceneShot's chooser is private and
        /// that file belongs to another area. The rules are restated, not invented:
        /// a cost measured at viewpoints nobody photographs cannot be compared
        /// against the pictures, and the overhead survey view is excluded here
        /// because it is explicitly not a game frame.
        /// </para>
        /// </summary>
        private static List<View> Viewpoints(Scene scene)
        {
            var all = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(includeInactive: true))
                .ToArray();

            var views = new List<View>();

            // Spread across the ring of starts rather than taken off the front of an
            // Ordinal sort, which yielded 0, 1, 10, 11 — see SceneShot.Spread.
            var spawns = SceneShot.Spread(
                all.Where(t => t.name.StartsWith("PlayerSpawn_", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(SceneShot.TrailingIndex)
                    .ToArray(),
                4);

            for (var i = 0; i < spawns.Length; i++)
            {
                views.Add(new View(
                    "spawn" + i.ToString(CultureInfo.InvariantCulture),
                    spawns[i].position + new Vector3(0f, 1.63f, 0f),
                    new Vector3(3f, spawns[i].eulerAngles.y, 0f)));
            }

            // Every zone, not the first six. §12's building has eight storeys, and the
            // .Take(6) this replaces meant B7 and B8 — the two deepest, the two most
            // heavily dressed, and therefore the two most likely to be the frame budget
            // problem — had never once been measured. SceneShot carried the identical
            // cap and it was removed there at 471ffab for the same reason; this is the
            // second copy of that bug, which is what happens when a view list is written
            // twice instead of shared.
            foreach (var zone in all.Where(t => t.name.StartsWith("Zone_", StringComparison.OrdinalIgnoreCase)))
            {
                var bounds = Bounds(zone.gameObject);
                var eye = ClearStandingSpot(new Vector3(bounds.center.x, bounds.min.y + 1.63f, bounds.center.z));
                views.Add(new View(zone.name, eye, new Vector3(14f, OpenestBearing(eye), 0f)));
            }

            return views;
        }

        private static Bounds Bounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static float OpenestBearing(Vector3 eye)
        {
            var best = 0f;
            var bestDistance = -1f;

            for (var i = 0; i < 24; i++)
            {
                var degrees = i * 15f;
                var direction = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
                var distance = Physics.Raycast(eye, direction, out var hit, 40f) ? hit.distance : 40f;

                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = degrees;
                }
            }

            return best;
        }

        private static Vector3 ClearStandingSpot(Vector3 wanted)
        {
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

        private static void Report(IReadOnlyList<Sample> samples, string tag, string scenePath, int tier)
        {
            if (samples.Count == 0)
            {
                Debug.LogWarning("[FrameCost] no viewpoints found in " + scenePath);
                return;
            }

            var lines = new List<string>
            {
                "[FrameCost] " + tag + " — " + scenePath + ", §07 tier " + tier.ToString(CultureInfo.InvariantCulture)
                + ", " + Width.ToString(CultureInfo.InvariantCulture) + "×" + Height.ToString(CultureInfo.InvariantCulture)
                + ", MSAA " + Mathf.Max(1, QualitySettings.antiAliasing).ToString(CultureInfo.InvariantCulture) + "×",
                string.Format(CultureInfo.InvariantCulture, "  {0,-24} {1,10} {2,10} {3,8}", "view", "median ms", "p95 ms", "fps"),
            };

            foreach (var sample in samples)
            {
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0,-24} {1,10:0.00} {2,10:0.00} {3,8:0}",
                    sample.Name, sample.MedianMs, sample.P95Ms, 1000.0 / sample.MedianMs));
            }

            var medians = samples.Select(s => s.MedianMs).OrderBy(v => v).ToArray();
            var worst = samples.OrderByDescending(s => s.MedianMs).First();
            var typical = medians[medians.Length / 2];

            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "  typical {0:0.00} ms ({1:0} fps) · worst view {2} at {3:0.00} ms ({4:0} fps)",
                typical, 1000.0 / typical, worst.Name, worst.MedianMs, 1000.0 / worst.MedianMs));

            Debug.Log(string.Join("\n", lines));
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
            public View(string name, Vector3 position, Vector3 euler)
            {
                Name = name;
                Position = position;
                Euler = euler;
            }

            public string Name { get; }

            public Vector3 Position { get; }

            public Vector3 Euler { get; }
        }
    }
}
