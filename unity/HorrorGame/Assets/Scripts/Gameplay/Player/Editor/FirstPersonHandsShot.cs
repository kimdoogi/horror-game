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
    /// Photographs what the player sees of their own hands, in each of §03's states,
    /// from inside the real building — and measures where on the screen each hand landed.
    /// <para>
    /// <b>Why this and not a test.</b> A test can assert that the arms renderer is drawn
    /// and that the body one is ShadowsOnly. It cannot answer the two questions that
    /// actually matter: <i>does this look like my own hands</i>, and <i>can I tell what I
    /// am holding without a HUD</i>. The first needs a person to look at a PNG. The second
    /// needs four PNGs side by side. That is what this writes.
    /// </para>
    /// <para>
    /// <b>The number under the picture.</b> Every shot reports the viewport coordinate of
    /// both hands and of the flashlight mount — 0,0 is bottom-left of the frame, 1,1 is
    /// top-right — so "the hands are on screen" stops being an opinion. The defect this
    /// fixes was invisible for exactly that reason: the model measured perfectly against
    /// every §03 and §05 requirement written from the outside, and nobody had measured it
    /// from the inside.
    /// </para>
    /// <para>
    /// Run WITHOUT <c>-nographics</c>, or every frame is black:
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath . -executeMethod
    ///   HorrorGame.Gameplay.PlayerEditor.FirstPersonHandsShot.Batch -shotTag hands1
    /// </code>
    /// </para>
    /// </summary>
    public static class FirstPersonHandsShot
    {
        private const string OutputDir = "Shots";
        private const int Width = 1280;
        private const int Height = 720;
        private const float Step = 1f / 50f;

        /// <summary>
        /// Degrees of downward pitch for the "as played" frames. Not a tuned value — it is
        /// where a person points a torch when they are walking somewhere, and §05 leaves
        /// pitch entirely to the player.
        /// </summary>
        private const float PlayingPitch = 12f;

        /// <summary>Batch entry point. Exits 0 on success, 1 on an exception, 2 on a missing scene.</summary>
        public static void Batch()
        {
            try
            {
                var scenePath = ViewMotionShot.ArgValue("-shotScene") ?? "Assets/Scenes/Map_FirstSketch.unity";
                var tag = ViewMotionShot.ArgValue("-shotTag") ?? "hands";

                if (!File.Exists(scenePath))
                {
                    Debug.LogError("[FirstPersonHandsShot] No scene at " + scenePath);
                    EditorApplication.Exit(2);
                    return;
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                Capture(scene, tag);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[FirstPersonHandsShot] " + ex);
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
                // The night, from the code, before anything is measured. Ambient and fog
                // are scene state — URP has no fog override and ambient is a lighting
                // setting — so a review shot otherwise photographs whatever
                // AtmosphereSetup last baked into this file. That is exactly how ART.md
                // §1's five zone views drifted out of band without anybody being able to
                // name a change: the map was regenerated twice and the atmosphere pass was
                // never re-run, so the scene and NightAtmosphere.cs disagreed and only the
                // scene was ever photographed. A measurement that cannot be reproduced from
                // the source is not a measurement.
                HorrorGame.Rendering.NightAtmosphere.ApplyEnvironment(
                    HorrorGame.Rendering.NightAtmosphere.ForTier(0));

                var stand = ViewMotionShot.FindStandingSpot(scene);
                ViewMotionShot.Teleport(rig, stand.Position);
                rig.GetComponent<PlayerLook>().SetLook(stand.HeadingDegrees, 0f);

                var motor = rig.GetComponent<PlayerMotor>();
                motor.SteppedExternally = true;

                var view = rig.GetComponent<PlayerViewMotion>();
                view.TickedExternally = true;

                var camera = rig.GetComponentInChildren<Camera>();
                if (camera == null)
                {
                    throw new InvalidOperationException("The built rig has no camera.");
                }

                ViewMotionShot.PrepareCamera(camera);
                ViewMotionShot.DisableOtherCameras(camera);

                var session = new Session(root, tag, rig, stand.HeadingDegrees, motor, view, camera, readings);

                // Settle on the floor: the controller re-imposes its own position on the
                // first Move, so a frame taken before that is a frame of a body still
                // arriving.
                for (var i = 0; i < 60; i++)
                {
                    motor.Step(new MoveInput(0f, 0f, false), Step);
                }

                // Thrown away. The first Render of a batch session catches whatever mip
                // levels happen to be resident and comes out measurably brighter than
                // every frame after it — 13.8 mean luminance against 9.9 on the first
                // working run, which reads as a lighting difference between two states
                // that are lit identically.
                session.Shoot("__warmup", "warm-up, discarded", torchOn: false);

                // §03's four states, as the player meets them, at the pitch somebody
                // actually walks a dark corridor at: the beam lands on the floor seven
                // metres ahead instead of on a wall twelve metres away, which is where a
                // player points it and therefore what the shot has to show.
                session.Shoot("00_empty", "empty hands, torch off", torchOn: false, pitch: PlayingPitch);
                session.Shoot("10_torch", "torch in hand, lit", torchOn: true, pitch: PlayingPitch);
                // DELETED: the 20_loot and 30_objective plates. They photographed §08's
                // 대형 전리품 in both hands and §03's 목표물 with no hand left for the torch
                // — the two states this tool was originally built to prove existed. A
                // runner's hands hold a torch and nothing else, so there is no third
                // state left to shoot.

                // The same two, dead level. This is the conservative framing — pitching
                // down lifts the hands up the screen, so a level view is the worst case
                // and the one gen_player_model.py asserts against §05's 80° FOV.
                session.Shoot("01_empty_level", "empty hands, level view", torchOn: false);
                session.Shoot("11_torch_level", "torch in hand, level view", torchOn: true);

                // The two verbs §05's table does not have, from inside. A crouch that
                // the owner cannot tell they are in is a state they will fight, and a
                // hop with no camera response reads as a teleport — neither is a thing
                // an assertion can catch, which is why they are frames.
                CaptureStance(session);

                // Is the hidden body still casting? Measured, not asserted — see
                // MeasureShadow.
                MeasureShadow(session);

                // Does the movement reach the hands? Two frames half a stride apart, at a
                // walk. The hands are animated by the clip and the eye rides the head bone
                // plus PlayerViewMotion's offset, so the two must not be rigid against each
                // other — a hand welded to the view while the corridor slides past reads
                // worse than no hand at all.
                CaptureStridePair(session);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rig);
            }

            Report(readings);
        }

        /// <summary>
        /// Renders the same moment twice — once with the policy applied and once with the
        /// hidden body's shadow casting switched off — and reports how much of the picture
        /// the player's own shadow is responsible for.
        /// <para>
        /// The one claim in this change a picture cannot settle on its own. ShadowsOnly
        /// looks exactly like a disabled renderer from the front; the only way to know the
        /// shadow survived the fix is to take it away and see the frame change. §03 makes
        /// a narrow beam the only light in the building, so a body that stopped casting
        /// would cost the game atmosphere nobody would ever file a bug about.
        /// </para>
        /// </summary>
        private static void MeasureShadow(Session session)
        {
            // Both ways down the corridor. Which one shows the shadow is a fact about the
            // room, not about the fix: a torch held in front puts the body BEHIND the only
            // light the player carries, so their own shadow falls behind them and the
            // frame cannot contain it. It appears when something else in §12 is lighting
            // them — a ceiling strip, another player's beam — which is exactly the moment
            // §03 says the shadow is worth having.
            session.ShootShadowPair("50_shadow_ahead", "own shadow, facing the clear run", 0f);
            session.ShootShadowPair("51_shadow_behind", "own shadow, facing back", 180f);
        }

        /// <summary>
        /// Photographs the two stances §05's control table does not have: crouched still,
        /// crouched moving, and the top of a hop.
        /// <para>
        /// Everything here is driven through the game's own components — the stance
        /// toggles itself, the motor takes the jump, and <see cref="Session.Shoot"/> poses
        /// the rig through <c>PlayerAnimatorDriver.Resolve</c>. So the eye height in the
        /// crouched frames is the one the <c>Crouch</c> clip's <c>HeadCameraAnchor</c>
        /// gives, and the eye height in the airborne frame is
        /// <see cref="GameConstants.JumpApexMetres"/> of real controller travel, not a
        /// camera moved by this file.
        /// </para>
        /// </summary>
        private static void CaptureStance(Session session)
        {
            var stance = session.Rig.GetComponent<PlayerStance>();
            if (stance == null)
            {
                Debug.LogWarning("[FirstPersonHandsShot] The built rig has no PlayerStance; "
                    + "the crouch and jump frames were skipped.");
                return;
            }

            session.Motor.Stamina.Reset();
            session.View.ResetMotion();
            Settle(session, stance, new MoveInput(0f, 0f, false), 40);

            stance.Crouch();
            Settle(session, stance, new MoveInput(0f, 0f, false), 40);
            session.Shoot("60_crouch", "§12 은폐 — crouched, still", torchOn: true, pitch: PlayingPitch);
            session.Shoot("61_crouch_level", "§12 은폐 — crouched, level view", torchOn: true);

            Settle(session, stance, new MoveInput(1f, 0f, true), 60);
            session.Shoot("62_crouch_walk", "crouch-walking — Shift is ignored down here",
                torchOn: true, pitch: PlayingPitch, driveTime: 0.25f);

            if (!stance.TryStand())
            {
                Debug.LogWarning("[FirstPersonHandsShot] Could not stand up at the shot spot; "
                    + "the airborne frame was skipped.");
                return;
            }

            Settle(session, stance, new MoveInput(0f, 0f, false), 40);

            // Up to the apex and no further. Stepping until the vertical velocity turns
            // over is the honest way to find it — it is where the hop actually peaks
            // rather than where the arithmetic says it should.
            var floor = session.Rig.transform.position.y;
            stance.RequestJump();

            var rise = 0f;
            for (var i = 0; i < 200; i++)
            {
                session.Motor.Step(new MoveInput(0f, 0f, false), Step);
                stance.Tick(Step);
                session.View.Tick(Step);

                rise = session.Rig.transform.position.y - floor;
                if (session.Motor.VerticalVelocity <= 0f)
                {
                    break;
                }
            }

            Debug.Log("[FirstPersonHandsShot] apex reached: " + rise.ToString("0.000", CultureInfo.InvariantCulture)
                + " m against GameConstants.JumpApexMetres " + GameConstants.JumpApexMetres.ToString(
                    "0.000", CultureInfo.InvariantCulture)
                + " and PlayerStepOffsetMetres " + GameConstants.PlayerStepOffsetMetres.ToString(
                    "0.000", CultureInfo.InvariantCulture)
                + ". §12: a hop must not reach a ledge walking cannot.");

            session.Shoot("70_airborne", "mid-air at the apex of a hop", torchOn: true, pitch: PlayingPitch);

            // Down again, so nothing after this photographs a player still in the air.
            Settle(session, stance, new MoveInput(0f, 0f, false), 80);
        }

        /// <summary>Drives the motor, the stance and the view together for a number of fixed steps.</summary>
        private static void Settle(Session session, PlayerStance stance, MoveInput input, int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                session.Motor.Step(input, Step);
                stance.Tick(Step);
                session.View.Tick(Step);
            }
        }

        /// <summary>
        /// Walks the body and shoots two frames half a stride apart, so the report can say
        /// how far the hands moved <em>in the frame</em> between them.
        /// </summary>
        private static void CaptureStridePair(Session session)
        {
            session.Motor.Stamina.Reset();
            session.View.ResetMotion();

            var input = new MoveInput(1f, 0f, false);
            for (var i = 0; i < 60; i++)
            {
                session.Motor.Step(input, Step);
                session.View.Tick(Step);
            }

            session.Shoot("40_walk_a", "walking, stride phase A", torchOn: true,
                pitch: PlayingPitch, driveTime: 0f);

            // Half a cycle of the walk clip: the opposite foot, and the opposite end of
            // the arm swing.
            session.Shoot("41_walk_b", "walking, stride phase B", torchOn: true,
                pitch: PlayingPitch, driveTime: 0.5f);
        }

        /// <summary>One capture run: the rig, the camera, and the readings collected so far.</summary>
        private sealed class Session
        {
            private readonly string _root;
            private readonly string _tag;
            private readonly GameObject _rig;
            private readonly float _heading;
            private readonly List<Reading> _readings;

            internal Session(
                string root,
                string tag,
                GameObject rig,
                float heading,
                PlayerMotor motor,
                PlayerViewMotion view,
                Camera camera,
                List<Reading> readings)
            {
                _root = root;
                _tag = tag;
                _rig = rig;
                _heading = heading;
                _readings = readings;
                Motor = motor;
                View = view;
                Camera = camera;
            }

            internal PlayerMotor Motor { get; }

            internal PlayerViewMotion View { get; }

            internal Camera Camera { get; }

            /// <summary>The rig being photographed. Needed by the stance frames, which drive components this class does not hold.</summary>
            internal GameObject Rig
            {
                get { return _rig; }
            }

            /// <summary>
            /// Sets a §03 state, poses the rig through the same components the game uses,
            /// renders, and records where the hands landed on screen.
            /// </summary>
            /// <param name="fileSuffix">File name part.</param>
            /// <param name="label">Human label for the report.</param>
            /// <param name="torchOn">Whether the player has switched the light on.</param>
            /// <param name="pitch">Downward view pitch in degrees; 0 is level.</param>
            /// <param name="driveTime">Normalised time into the pose clip, 0–1.</param>
            internal void Shoot(
                string fileSuffix,
                string label,
                bool torchOn,
                float pitch = 0f,
                float driveTime = 0f)
            {
                _rig.GetComponent<PlayerLook>().SetLook(_heading, pitch);

                // The player presses F and the light comes on. There is no longer anything
                // that could overrule that — §03's 「양손을 쓴다」 and §08's two-handed 궤짝
                // are both deleted, and with them PlayerFlashlight.EnforceCarryRules.
                var flashlight = _rig.GetComponent<PlayerFlashlight>();
                if (torchOn)
                {
                    flashlight.State.TryTurnOn();
                }
                else
                {
                    flashlight.State.TurnOff();
                }

                // The component's own call, not a re-implementation of it: this is what
                // decides the beam and whether the torch is in the fist.
                flashlight.RefreshPresentation();

                var state = Pose(driveTime);
                var pixels = RenderPixels(Camera);

                if (fileSuffix.StartsWith("__", StringComparison.Ordinal))
                {
                    return;
                }

                WritePng(Path.Combine(_root, _tag + "_" + fileSuffix + ".png"), pixels);
                _readings.Add(Measure(label + " [" + state + "]"));
            }

            /// <summary>
            /// Shoots a frame with the first-person policy applied, then the same frame
            /// with every hidden renderer's shadow switched off, and records the mean
            /// absolute luminance between them.
            /// </summary>
            /// <param name="fileSuffix">File name part; the no-shadow plate gets "_noshadow".</param>
            /// <param name="label">Human label for the report.</param>
            /// <param name="headingOffset">Degrees to add to the standing heading.</param>
            internal void ShootShadowPair(string fileSuffix, string label, float headingOffset)
            {
                var flashlight = _rig.GetComponent<PlayerFlashlight>();
                flashlight.State.TryTurnOn();
                flashlight.RefreshPresentation();

                _rig.GetComponent<PlayerLook>().SetLook(_heading + headingOffset, PlayingPitch);
                Pose(0f);

                var withShadow = RenderPixels(Camera);
                WritePng(Path.Combine(_root, _tag + "_" + fileSuffix + ".png"), withShadow);

                var hidden = new List<SkinnedMeshRenderer>();
                foreach (var renderer in _rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    {
                        hidden.Add(renderer);
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                    }
                }

                var withoutShadow = RenderPixels(Camera);
                WritePng(Path.Combine(_root, _tag + "_" + fileSuffix + "_noshadow.png"), withoutShadow);

                for (var i = 0; i < hidden.Count; i++)
                {
                    hidden[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }

                var difference = MeanAbsoluteDifference(withShadow, withoutShadow);
                Debug.Log("[FirstPersonHandsShot] " + label + ": " + hidden.Count
                    + " hidden renderer(s) still casting; switching their shadows off changes "
                    + (difference * 100f).ToString("0.000", CultureInfo.InvariantCulture)
                    + "% of the frame's mean luminance. Zero would mean the shadow was lost "
                    + "with the body — §03's beam is the only light and that shadow is free "
                    + "atmosphere.");
            }

            /// <summary>
            /// Poses the rig for the state those flags produce and puts the eye on the
            /// head bone, both through the game's own code.
            /// <para>
            /// The pose is sampled rather than played because this runs outside play mode,
            /// where the Playables graph never ticks. Which clip is decided by
            /// <see cref="PlayerAnimatorDriver.Resolve"/> and fetched with
            /// <see cref="PlayerAnimatorDriver.ClipFor"/>, so the state machine being
            /// photographed is the game's.
            /// </para>
            /// </summary>
            private PlayerAnimationState Pose(float normalisedTime)
            {
                var driver = _rig.GetComponent<PlayerAnimatorDriver>();

                // The two carry flags are gone from Resolve entirely — nothing is held,
                // so there was never a call that passed true. Resolve is still asked
                // rather than the clip picked here, because the point of this tool is
                // that the state machine being photographed is the game's own.
                var state = PlayerAnimatorDriver.Resolve(
                    Motor.GroundSpeed, driver.CrouchingNow, driver.Dead);

                var clip = driver.ClipFor(state);
                var animator = _rig.GetComponentInChildren<Animator>();
                if (clip != null && animator != null)
                {
                    clip.SampleAnimation(animator.gameObject, normalisedTime * clip.length);
                }

                // Both of these are LateUpdate's work, run by hand because there is no
                // LateUpdate outside play mode: the eye goes on the head bone and the beam
                // goes on the hand. Without the second the torch lights the floor between
                // the player's boots and every shot is of a light nobody is holding.
                _rig.GetComponent<PlayerCameraRig>().SnapToHeadAnchor();
                _rig.GetComponent<PlayerFlashlight>().SnapBeamToMount();
                View.Apply();
                return state;
            }

            private Reading Measure(string label)
            {
                var view = _rig.GetComponent<PlayerFirstPersonView>();
                var spot = _rig.GetComponentInChildren<Light>();
                return new Reading(
                    label,
                    Viewport(PlayerRigBones.Find(_rig.transform, PlayerRigParts.LeftHandBone)),
                    Viewport(PlayerRigBones.Find(_rig.transform, PlayerRigParts.RightHandBone)),
                    Viewport(PlayerRigBones.Find(_rig.transform, PlayerRigBones.FlashlightMount)),
                    view.ArmRendererCount,
                    TorchDrawn(view),
                    Beam(spot));
            }

            /// <summary>
            /// The beam as the renderer sees it. §05 makes it the pointing device three
            /// teammates read, so a review shot that cannot say whether it was on, where it
            /// was and how far it reached is not a review of the flashlight.
            /// </summary>
            private static string Beam(Light? spot)
            {
                if (spot == null)
                {
                    return "no light";
                }

                return (spot.enabled ? "on " : "off")
                    + " " + spot.transform.position.ToString("0.00")
                    + " fwd" + spot.transform.forward.ToString("0.00")
                    + " r" + spot.range.ToString("0.0", CultureInfo.InvariantCulture)
                    + " " + spot.spotAngle.ToString("0", CultureInfo.InvariantCulture) + "deg"
                    + " i" + spot.intensity.ToString("0.0", CultureInfo.InvariantCulture);
            }

            private static bool TorchDrawn(PlayerFirstPersonView view)
            {
                var props = view.HandProps;
                for (var i = 0; i < props.Count; i++)
                {
                    if (props[i].enabled)
                    {
                        return true;
                    }
                }

                return false;
            }

            private Vector3 Viewport(Transform? bone)
            {
                return bone == null ? new Vector3(-1f, -1f, -1f) : Camera.WorldToViewportPoint(bone.position);
            }
        }

        private static void Report(List<Reading> readings)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("[FirstPersonHandsShot] where the player's own hands land in their own frame");
            report.AppendLine("  0,0 is bottom-left and 1,1 is top-right; a coordinate outside 0..1 is off screen.");
            report.AppendLine(
                "  frame                                                left hand        right hand       torch"
                + "            arms  torch");

            foreach (var reading in readings)
            {
                report.AppendLine(
                    "  " + reading.Label.PadRight(52)
                    + Format(reading.LeftHand) + " " + Format(reading.RightHand) + " " + Format(reading.Mount)
                    + reading.ArmRenderers.ToString(CultureInfo.InvariantCulture).PadLeft(6)
                    + (reading.TorchDrawn ? "   drawn  " : "   -      ")
                    + reading.Beam);
            }

            Debug.Log(report.ToString());
        }

        private static string Format(Vector3 viewport)
        {
            var onScreen = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f
                && viewport.y >= 0f && viewport.y <= 1f;
            return ("(" + viewport.x.ToString("0.00", CultureInfo.InvariantCulture) + ","
                + viewport.y.ToString("0.00", CultureInfo.InvariantCulture) + ")"
                + (onScreen ? " on " : " OFF")).PadRight(16);
        }

        /// <summary>Mean absolute luminance difference between two frames, 0–1.</summary>
        private static float MeanAbsoluteDifference(Color[] a, Color[] b)
        {
            if (a.Length != b.Length || a.Length == 0)
            {
                return 0f;
            }

            var total = 0.0;
            for (var i = 0; i < a.Length; i++)
            {
                var x = (a[i].r * 0.2126f) + (a[i].g * 0.7152f) + (a[i].b * 0.0722f);
                var y = (b[i].r * 0.2126f) + (b[i].g * 0.7152f) + (b[i].b * 0.0722f);
                total += Mathf.Abs(x - y);
            }

            return (float)(total / a.Length);
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

        private readonly struct Reading
        {
            internal Reading(
                string label,
                Vector3 leftHand,
                Vector3 rightHand,
                Vector3 mount,
                int armRenderers,
                bool torchDrawn,
                string beam)
            {
                Label = label;
                LeftHand = leftHand;
                RightHand = rightHand;
                Mount = mount;
                ArmRenderers = armRenderers;
                TorchDrawn = torchDrawn;
                Beam = beam;
            }

            internal string Label { get; }

            /// <summary>Viewport position of the left wrist. x,y in 0..1 means on screen.</summary>
            internal Vector3 LeftHand { get; }

            /// <summary>Viewport position of the right wrist — the torch hand.</summary>
            internal Vector3 RightHand { get; }

            /// <summary>Viewport position of <c>FlashlightMount</c>, i.e. the lens.</summary>
            internal Vector3 Mount { get; }

            /// <summary>How many renderers the first-person policy decided are the owner's hands.</summary>
            internal int ArmRenderers { get; }

            /// <summary>Whether the torch mesh was drawn for this frame.</summary>
            internal bool TorchDrawn { get; }

            /// <summary>The spot light's state, position, aim, reach and cone at the moment of the shot.</summary>
            internal string Beam { get; }
        }
    }
}
