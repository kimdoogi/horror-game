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
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HorrorGame.Gameplay.PlayerEditor
{
    /// <summary>
    /// Photographs what <see cref="PlayerViewMotion"/> does to the picture, from inside
    /// the real building, and measures how far the frame actually moved.
    /// <para>
    /// <b>Why a shot rig and not only a test.</b> <c>PlayerViewMotionTests</c> can assert
    /// that the eye moved 1.7 cm and that the number is bounded. It cannot say whether
    /// 1.7 cm is a walk or a trampoline, and that is the entire question — §14 says
    /// questions 1 and 2 「직접 만져봐야 나온다」 and this is the closest an automated
    /// pass can get to touching it. What it produces is a fixed set of frames that can
    /// be looked at, compared against the last set, and argued about, exactly as
    /// <c>MonsterShot</c> does for the creature.
    /// </para>
    /// <para>
    /// <b>The measurement underneath the frames.</b> Every shot is diffed against a
    /// still plate taken from the same spot with the motion switched off, and the report
    /// prints the mean absolute luminance change. That converts "does it read" into a
    /// number: a stride that moves the picture less than the render's own noise is
    /// invisible however good the arithmetic is, and one that moves it like a landing
    /// does is too much. The two are meant to be an order of magnitude apart and the
    /// report is where that is checked.
    /// </para>
    /// <para>
    /// <b>The body is really driven.</b> <see cref="PlayerMotor.Step"/> and
    /// <see cref="PlayerViewMotion.Tick"/> are called at
    /// <see cref="GameConstants.FixedStep"/> against the map's own collision, so the
    /// stride amplitude comes from the speed the controller actually reached rather than
    /// from the speed it was asked for. A rig that posed the camera by hand would
    /// photograph the tuning file instead of the game.
    /// </para>
    /// <para>
    /// Run WITHOUT <c>-nographics</c>, or every frame is black:
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath . -executeMethod
    ///   HorrorGame.Gameplay.PlayerEditor.ViewMotionShot.Batch -shotTag feel1
    /// </code>
    /// </para>
    /// </summary>
    public static class ViewMotionShot
    {
        private const string OutputDir = "Shots";
        private const int Width = 1280;
        private const int Height = 720;
        private const float Step = 1f / 50f;

        /// <summary>Batch entry point. Exits 0 on success, 1 on an exception, 2 on a missing scene.</summary>
        public static void Batch()
        {
            try
            {
                var scenePath = ArgValue("-shotScene") ?? "Assets/Scenes/Map_FirstSketch.unity";
                var tag = ArgValue("-shotTag") ?? "feel";

                if (!File.Exists(scenePath))
                {
                    Debug.LogError("[ViewMotionShot] No scene at " + scenePath);
                    EditorApplication.Exit(2);
                    return;
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                Capture(scene, tag);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ViewMotionShot] " + ex);
                EditorApplication.Exit(1);
            }
        }
        /// <summary>Renders the set and prints the report. Public so a menu item could call it.</summary>
        public static void Capture(Scene scene, string tag)
        {
            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDir);
            Directory.CreateDirectory(root);

            var rig = PlayerFeelHarnessMenu.BuildRig();
            if (rig == null)
            {
                throw new InvalidOperationException(
                    "PlayerFeelHarnessMenu.BuildRig returned null; Player.fbx is missing and there is no "
                    + "camera to photograph through.");
            }

            var readings = new List<Reading>();

            try
            {
                var stand = FindStandingSpot(scene);
                Teleport(rig, stand.Position);

                var look = rig.GetComponent<PlayerLook>();
                look.SetLook(stand.HeadingDegrees, 0f);

                var motor = rig.GetComponent<PlayerMotor>();
                motor.SteppedExternally = true;

                var view = rig.GetComponent<PlayerViewMotion>();
                view.TickedExternally = true;

                var camera = rig.GetComponentInChildren<Camera>();
                if (camera == null)
                {
                    throw new InvalidOperationException("The built rig has no camera.");
                }

                PrepareCamera(camera);
                DisableOtherCameras(camera);

                var session = new Session(root, tag, stand, motor, view, camera, readings);
                session.Rewind();

                Debug.Log("[ViewMotionShot] standing at " + motor.transform.position.ToString("0.00")
                          + " heading " + stand.HeadingDegrees.ToString("0") + "° with "
                          + stand.ClearMetres.ToString("0.0") + " m of corridor ahead"
                          + "; grounded " + motor.IsGrounded
                          + ", vertical " + motor.WorldVelocity.y.ToString("0.00", CultureInfo.InvariantCulture) + " m/s");

                // The reference frame a human looks at: this spot, this heading, nothing
                // moving. Every other shot has its own plate taken from the body's own
                // position, so the numbers below are the camera and not the walk.
                session.ShootReference();

                CaptureStride(session, "10_walk", new MoveInput(1f, 0f, false));
                CaptureStride(session, "20_sprint", new MoveInput(1f, 0f, true));

                CaptureConverged(session, "30_forward", new MoveInput(1f, 0f, false), 1.6f);
                CaptureConverged(session, "31_backpedal", new MoveInput(-1f, 0f, false), 1.6f);

                CaptureLanding(session);
                CaptureFlinch(session);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rig);
            }

            Report(readings);
        }

        /// <summary>
        /// Four frames across one stride cycle: the two footfalls and the two mid-stances.
        /// <para>
        /// Sampled by phase rather than by time, so the set is the same shape whatever
        /// speed it was taken at and two runs can be laid side by side. The two footfall
        /// frames should be the lowest of the four and the two mid-stances the highest —
        /// which is the one property of the figure-eight a still can actually show.
        /// </para>
        /// </summary>
        private static void CaptureStride(Session session, string label, MoveInput input)
        {
            session.Rewind();

            var wanted = new[] { 0f, 0.25f, 0.5f, 0.75f };
            var taken = new bool[wanted.Length];

            // The window has to be wider than one step's worth of phase or the sample is
            // stepped straight over: at §04's 질주 5.6 m/s a fixed step advances the cycle
            // by 15°, and a 4° window matched nothing at all on the first run.
            const float WindowDegrees = 24f;

            // Nothing is taken in the first half second, so every frame is of a body
            // already at speed rather than of the one accelerating out of a standstill.
            for (var i = 0; i < 400; i++)
            {
                session.Motor.Step(input, Step);
                session.View.Tick(Step);

                if (i < 25)
                {
                    continue;
                }

                for (var w = 0; w < wanted.Length; w++)
                {
                    if (taken[w] || Mathf.Abs(Mathf.DeltaAngle(
                        session.View.StridePhase * 360f, wanted[w] * 360f)) > WindowDegrees)
                    {
                        continue;
                    }

                    taken[w] = true;
                    session.Shoot(
                        label + "_p" + Mathf.RoundToInt(wanted[w] * 100f).ToString("00", CultureInfo.InvariantCulture),
                        label + " phase " + wanted[w].ToString("0.00", CultureInfo.InvariantCulture)
                        + " (" + session.Motor.GroundSpeed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s)");
                }
            }

            for (var w = 0; w < wanted.Length; w++)
            {
                if (!taken[w])
                {
                    Debug.LogWarning("[ViewMotionShot] " + label + " never reached stride phase " + wanted[w]
                        + "; the body probably ran out of corridor. Ground speed was "
                        + session.Motor.GroundSpeed.ToString("0.00", CultureInfo.InvariantCulture) + " m/s.");
                }
            }
        }

        /// <summary>Drives one input until the smoothed terms have converged, then shoots once.</summary>
        private static void CaptureConverged(Session session, string label, MoveInput input, float seconds)
        {
            session.Rewind();

            var steps = Mathf.RoundToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                session.Motor.Step(input, Step);
                session.View.Tick(Step);
            }

            // Walked on to the bottom of the stride, so what separates this frame from its
            // opposite number is §05's lean and not whichever foot happened to be down.
            for (var i = 0; i < 200 && session.View.StridePhase > 0.02f; i++)
            {
                session.Motor.Step(input, Step);
                session.View.Tick(Step);
            }

            session.Shoot(
                label,
                label + " (drag " + (session.View.Drag * 100f).ToString("0", CultureInfo.InvariantCulture) + "%)");
        }

        /// <summary>
        /// Drops the body as far as the ceiling allows and shoots the deepest frame of
        /// the landing.
        /// <para>
        /// §12 stacks five storeys and seven 계단, so the descent is most of a match. The
        /// height is measured rather than assumed: a first draft raised the body by a
        /// full 3.75 m storey, which put the capsule inside the slab above, blocked the
        /// fall and reported a landing a third of its real depth. Whatever the corridor
        /// has room for is the largest fall the building can actually produce.
        /// </para>
        /// </summary>
        private static void CaptureLanding(Session session)
        {
            session.Rewind();

            var start = session.Motor.transform.position;
            var controller = session.Motor.GetComponent<CharacterController>();
            var bodyHeight = controller != null ? controller.height : 1.75f;

            var ceiling = Physics.Raycast(
                start + new Vector3(0f, 0.1f, 0f), Vector3.up, out var above, 8f, ~0, QueryTriggerInteraction.Ignore)
                ? above.distance + 0.1f
                : 8f;

            var drop = Mathf.Max(0.4f, ceiling - bodyHeight - 0.15f);
            var raised = start + new Vector3(0f, drop, 0f);

            var deepestAt = -1;
            var deepest = 0f;

            Teleport(session.Motor.gameObject, raised);
            session.View.ResetMotion();

            for (var i = 0; i < 200; i++)
            {
                session.Motor.Step(new MoveInput(0f, 0f, false), Step);
                session.View.Tick(Step);

                if (-session.View.Translation.y > deepest)
                {
                    deepest = -session.View.Translation.y;
                    deepestAt = i;
                }
            }

            if (deepestAt < 0)
            {
                Debug.LogWarning("[ViewMotionShot] the drop produced no landing at all.");
                session.Rewind();
                return;
            }

            // Replayed rather than photographed on the way past: the deepest frame is only
            // known once the fall is over.
            Teleport(session.Motor.gameObject, raised);
            session.View.ResetMotion();
            for (var i = 0; i <= deepestAt; i++)
            {
                session.Motor.Step(new MoveInput(0f, 0f, false), Step);
                session.View.Tick(Step);
            }

            session.Shoot(
                "40_landing",
                "40 landing, " + drop.ToString("0.00", CultureInfo.InvariantCulture) + " m drop (all the ceiling allows)");
            session.Rewind();
        }

        /// <summary>Shoots the peak of §06's acquisition flinch.</summary>
        private static void CaptureFlinch(Session session)
        {
            session.Rewind();

            var peak = 0f;
            var peakAt = 0;
            var steps = Mathf.CeilToInt(ViewMotionTuning.FlinchSeconds / Step);

            session.View.Flinch();
            for (var i = 0; i < steps; i++)
            {
                session.View.Tick(Step);
                var strength = Quaternion.Angle(Quaternion.identity, session.View.Rotation);
                if (strength > peak)
                {
                    peak = strength;
                    peakAt = i;
                }
            }

            session.View.ResetMotion();
            session.View.Flinch();
            for (var i = 0; i <= peakAt; i++)
            {
                session.View.Tick(Step);
            }

            session.Shoot("50_flinch", "50 flinch (§06 추격 acquired)");
            session.Rewind();
        }

        /// <summary>
        /// One capture run: where the body stands, what it is looking through, and the
        /// readings collected so far.
        /// <para>
        /// A struct of five arguments passed down four call sites was the alternative, and
        /// the fourth one is where the plate and the camera got out of step on the first
        /// draft.
        /// </para>
        /// </summary>
        private sealed class Session
        {
            private readonly string _root;
            private readonly string _tag;
            private readonly Stand _stand;
            private readonly List<Reading> _readings;

            internal Session(
                string root,
                string tag,
                Stand stand,
                PlayerMotor motor,
                PlayerViewMotion view,
                Camera camera,
                List<Reading> readings)
            {
                _root = root;
                _tag = tag;
                _stand = stand;
                _readings = readings;
                Motor = motor;
                View = view;
                Camera = camera;
            }

            internal PlayerMotor Motor { get; }

            internal PlayerViewMotion View { get; }

            internal Camera Camera { get; }

            /// <summary>
            /// Puts the body back on the standing spot with a still camera and a full bar.
            /// <para>
            /// Called before every segment because the body really walks: eight seconds at
            /// §06's 걷기 covers sixteen metres and §12 caps a straight run at twenty, so
            /// the second segment of the first working draft started with its face against
            /// a wall and reported a ground speed of zero.
            /// </para>
            /// </summary>
            internal void Rewind()
            {
                Teleport(Motor.gameObject, _stand.Position);
                Motor.Stamina.Reset();

                for (var i = 0; i < 60; i++)
                {
                    Motor.Step(new MoveInput(0f, 0f, false), Step);
                }

                View.ResetMotion();
                View.Tick(Step);
            }

            /// <summary>The still reference frame a human compares the rest against.</summary>
            internal void ShootReference()
            {
                View.Scale = 0f;
                View.Tick(1e-6f);
                View.Apply();

                // Thrown away. The very first Render of a batch session catches whatever
                // mip levels happened to be resident, and on the first working run the
                // reference frame came out visibly noisier and darker than every shot
                // taken after it — which is a difference in the streaming system being
                // read as a difference in the camera.
                RenderPixels(Camera);

                WritePng(Path.Combine(_root, _tag + "_00_still.png"), RenderPixels(Camera));
                View.Scale = ViewMotionTuning.ScaleDefault;
                _readings.Add(new Reading("00 still (motion off)", 0f, 0f, 0f, 0f, 0f));
            }

            /// <summary>
            /// Writes the frame and measures it against a plate taken from the same body
            /// position with the motion switched off.
            /// <para>
            /// <b>A local plate, not the reference frame.</b> The first working draft diffed
            /// every shot against the frame taken at the standing spot, so a shot recorded
            /// after the body had walked sixteen metres reported an enormous difference —
            /// all of it the walk and none of it the camera. Switching the scale off and on
            /// around a single moment isolates exactly the thing being measured.
            /// </para>
            /// </summary>
            internal void Shoot(string fileSuffix, string label)
            {
                View.Scale = 0f;
                View.Tick(1e-6f);
                View.Apply();
                var plate = RenderPixels(Camera);

                View.Scale = ViewMotionTuning.ScaleDefault;
                View.Tick(1e-6f);
                View.Apply();
                var shot = RenderPixels(Camera);

                WritePng(Path.Combine(_root, _tag + "_" + fileSuffix + ".png"), shot);

                // The plate is written too. The number below says how much moved; only a
                // pair of frames says whether what moved is a body or a glitch, and a
                // human flipping between two images is the whole point of this rig.
                WritePng(Path.Combine(_root, _tag + "_" + fileSuffix + "_off.png"), plate);

                _readings.Add(new Reading(
                    label,
                    MeanAbsoluteDifference(plate, shot),
                    View.Translation.magnitude,
                    Quaternion.Angle(Quaternion.identity, View.Rotation),
                    Mathf.DeltaAngle(0f, View.Rotation.eulerAngles.x),
                    View.Drag));
            }
        }

        private static void Report(List<Reading> readings)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("[ViewMotionShot] how far the picture actually moves");
            report.AppendLine(
                "  frame                                    plate diff    eye off    view rot     pitch   §05 drag");

            foreach (var reading in readings)
            {
                report.AppendLine(
                    "  " + reading.Label.PadRight(40)
                    + reading.PlateDifference.ToString("0.00000", CultureInfo.InvariantCulture).PadLeft(10)
                    + (reading.TranslationMetres * 100f).ToString("0.00", CultureInfo.InvariantCulture).PadLeft(11) + " cm"
                    + reading.RotationDegrees.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(9) + " deg"
                    + reading.PitchDegrees.ToString("+0.00;-0.00; 0.00", CultureInfo.InvariantCulture).PadLeft(10)
                    + (reading.Drag * 100f).ToString("0", CultureInfo.InvariantCulture).PadLeft(9) + " %");
            }

            report.AppendLine("  bounds: " + (ViewMotionTuning.MaxTranslationMetres * 100f).ToString("0.0", CultureInfo.InvariantCulture)
                + " cm and " + ViewMotionTuning.MaxRotationDegrees.ToString("0.0", CultureInfo.InvariantCulture) + " deg");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Moves the body, switching the <see cref="CharacterController"/> off across the
        /// change.
        /// <para>
        /// The controller keeps its own copy of where it is and re-imposes it on the next
        /// <c>Move</c>, so assigning <c>transform.position</c> with it enabled is silently
        /// undone — which on the first run made a 3.75 m drop produce no landing at all
        /// and looked exactly like the landing code being dead.
        /// </para>
        /// </summary>
        internal static void Teleport(GameObject body, Vector3 position)
        {
            var controller = body.GetComponent<CharacterController>();
            if (controller == null)
            {
                body.transform.position = position;
                return;
            }

            controller.enabled = false;
            body.transform.position = position;
            controller.enabled = true;
        }

        /// <summary>
        /// A place to stand in the map with something to look at: a 후보 지점 or player
        /// spawn marker, facing whichever of eight headings has the longest clear run.
        /// <para>
        /// The heading matters more than the position. A camera facing a blank wall
        /// 1.5 m away photographs one texture, and every frame in the set then differs
        /// from the plate by nothing at all — which reads as the component being broken
        /// when it is the viewpoint that is.
        /// </para>
        /// </summary>
        internal static Stand FindStandingSpot(Scene scene)
        {
            var best = new Stand(new Vector3(0f, 1f, 0f), 0f, 0f);
            var bestScore = float.NegativeInfinity;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    // childCount, because the generator parents its markers under a
                    // container called PlayerSpawns which sits at the world origin — and
                    // the origin is outside the building, where every ray misses and the
                    // whole set comes back identical to the plate.
                    if (candidate.childCount != 0
                        || !candidate.name.StartsWith("PlayerSpawn", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var eye = candidate.position + new Vector3(0f, 1.63f, 0f);
                    if (!Physics.Raycast(eye, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        // Nothing to stand on. A marker floating over the void produces a
                        // camera falling for the whole capture.
                        continue;
                    }

                    for (var degrees = 0f; degrees < 360f; degrees += 30f)
                    {
                        var clear = ClearRun(eye, degrees);

                        // Room behind as well as in front. §05's backpedal shot walks the
                        // body backwards, and on the first working run it reversed
                        // straight into a wall a metre away and reported a drag of zero —
                        // which reads as the lean being unimplemented.
                        var behind = ClearRun(eye, degrees + 180f);

                        // A corridor with an end in it, not the longest possible run. The
                        // set is measured by how much the picture changes; a wall at a
                        // middle distance carries texture the eye can see move, open fog
                        // at 40 m carries none, and neither does a wall at 1.5 m.
                        var score = clear < 6f || behind < 5f
                            ? float.NegativeInfinity
                            : -Mathf.Abs(clear - 14f);

                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = new Stand(candidate.position + new Vector3(0f, 0.2f, 0f), degrees, clear);
                        }
                    }
                }
            }

            return best;
        }

        private static float ClearRun(Vector3 eye, float degrees)
        {
            var forward = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
            return Physics.Raycast(eye, forward, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : 40f;
        }

        internal static void PrepareCamera(Camera camera)
        {
            camera.fieldOfView = GameConstants.FovDefault;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 300f;

            var post = camera.GetComponent<UniversalAdditionalCameraData>();
            if (post == null)
            {
                post = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            // The same treatment MonsterShot gives its rig. Post-processing is where the
            // grade lives, and a frame rendered without it is a frame of a different game.
            post.renderPostProcessing = true;
            post.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            post.antialiasingQuality = AntialiasingQuality.High;
        }

        internal static void DisableOtherCameras(Camera keep)
        {
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (camera != keep)
                {
                    camera.enabled = false;
                }
            }
        }

        /// <summary>
        /// Mean absolute luminance change against the still plate, 0–1. The number that
        /// says whether a component reached the picture at all.
        /// </summary>
        private static float MeanAbsoluteDifference(Color[] plate, Color[] pixels)
        {
            if (plate.Length != pixels.Length || plate.Length == 0)
            {
                return 0f;
            }

            var total = 0.0;
            for (var i = 0; i < plate.Length; i++)
            {
                var a = (plate[i].r * 0.2126f) + (plate[i].g * 0.7152f) + (plate[i].b * 0.0722f);
                var b = (pixels[i].r * 0.2126f) + (pixels[i].g * 0.7152f) + (pixels[i].b * 0.0722f);
                total += Mathf.Abs(a - b);
            }

            return (float)(total / plate.Length);
        }

        private static Color[] RenderPixels(Camera camera)
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
                var pixels = texture.GetPixels();
                UnityEngine.Object.DestroyImmediate(texture);
                return pixels;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static void WritePng(string path, Color[] pixels)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        internal static string? ArgValue(string flag)
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

        internal readonly struct Stand
        {
            internal Stand(Vector3 position, float headingDegrees, float clearMetres)
            {
                Position = position;
                HeadingDegrees = headingDegrees;
                ClearMetres = clearMetres;
            }

            internal Vector3 Position { get; }

            internal float HeadingDegrees { get; }

            internal float ClearMetres { get; }
        }

        private readonly struct Reading
        {
            internal Reading(
                string label,
                float plateDifference,
                float translationMetres,
                float rotationDegrees,
                float pitchDegrees,
                float drag)
            {
                Label = label;
                PlateDifference = plateDifference;
                TranslationMetres = translationMetres;
                RotationDegrees = rotationDegrees;
                PitchDegrees = pitchDegrees;
                Drag = drag;
            }

            internal string Label { get; }

            internal float PlateDifference { get; }

            internal float TranslationMetres { get; }

            internal float RotationDegrees { get; }

            /// <summary>Signed pitch, so §05's lean can be read against its opposite number.</summary>
            internal float PitchDegrees { get; }

            /// <summary>§05's normalised directional cost at the moment of the shot, 0–1.</summary>
            internal float Drag { get; }
        }
    }
}
