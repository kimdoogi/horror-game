#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Photographs §08's 대형 전리품 through the player's own eye: on the floor before it
    /// is touched, in the hands, and back on the floor after the hands open.
    /// <para>
    /// <b>Why a picture and not another assertion.</b> <c>InteractionDropTests</c> already
    /// drives the real key and measures where the piece lands, and
    /// <c>SoloMatchLoopTests</c> does it again headlessly. Neither can answer the question
    /// the owner actually asked, which was about a thing they could see: a 궤짝 hanging in
    /// the air at chest height. A number that says "0.020 m above the surface" and a frame
    /// that shows the crate sitting on the floor are two different kinds of evidence, and
    /// the second is the one the defect was reported in.
    /// </para>
    /// <para>
    /// <b>Two exposures of every frame.</b> §03 makes the building dark and §03 also takes
    /// the torch away from anyone whose hands are full, so the honest frame of a player
    /// carrying a crate is nearly black — that is the game, and <c>_asplayed</c> is it.
    /// The <c>_fill</c> twin raises the ambient term only, so the same geometry can be
    /// read. Nothing else differs between the pair; if a shape is in one it is in both.
    /// </para>
    /// <para>
    /// Run WITHOUT <c>-nographics</c>, or every frame is black for a different and much
    /// less interesting reason:
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath . \
    ///   -executeMethod HorrorGame.EditorTools.DropShot.Batch -shotTag drop
    /// </code>
    /// </para>
    /// </summary>
    public static class DropShot
    {
        private const string OutputDir = "Shots";
        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>Batch entry point. Exits 0 when every frame was written, 1 otherwise.</summary>
        public static void Batch()
        {
            try
            {
                var tag = ArgValue("-shotTag") ?? "drop";
                Debug.Log("[DropShot]\n" + Capture(tag));
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[DropShot] " + error);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Menu twin, so the same set can be made without a terminal.</summary>
        [MenuItem("HorrorGame/Play/Photograph a Dropped Piece", priority = 25)]
        public static void Menu()
        {
            Debug.Log("[DropShot]\n" + Capture("drop"));
        }

        /// <summary>
        /// Builds the solo scene, walks §08's 궤짝 through pick up → put down, and writes
        /// the frames. Returns the report that goes under them.
        /// </summary>
        public static string Capture(string tag)
        {
            if (!SoloPlaytest.BuildScene())
            {
                throw new InvalidOperationException(
                    "the solo scene could not be built, so there is nothing to photograph");
            }

            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDir);
            Directory.CreateDirectory(root);

            var director = UnityEngine.Object.FindFirstObjectByType<MatchDirector>();
            var motor = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
            if (director == null || motor == null)
            {
                throw new InvalidOperationException("the solo scene has no MatchDirector or no player rig");
            }

            if (!director.BeginMatch(SoloPlaytest.PlaytestSeed))
            {
                throw new InvalidOperationException("BeginMatch refused the playtest seed");
            }

            var interactor = motor.GetComponentInChildren<PlayerInteractor>();
            var camera = motor.GetComponentInChildren<Camera>();
            var look = motor.GetComponent<PlayerLook>();
            var rig = motor.GetComponent<PlayerCameraRig>();
            var torch = motor.GetComponentInChildren<PlayerFlashlight>();
            var crate = UnityEngine.Object.FindFirstObjectByType<OversizeLootInteractable>();

            if (interactor == null || camera == null || look == null || crate == null)
            {
                throw new InvalidOperationException(
                    "the rig is missing an interactor, a camera or a look, or §08's 궤짝 is not on the map");
            }

            PrepareCamera(camera);
            DisableOtherCameras(camera);

            var report = new System.Text.StringBuilder();
            report.AppendLine("§08's 대형 전리품, picked up and put down, seen from the eye that did it");

            var spawnedAt = crate!.transform.position;
            var stand = ApproachFrom(spawnedAt);
            Teleport(motor.gameObject, stand);
            look.SetLook(YawTowards(stand, spawnedAt), LookDownDegrees);
            Settle(motor, rig);

            Light(torch, on: true, rig);
            Shoot(camera, root, tag, "00_on_the_floor");
            report.AppendLine("  00_on_the_floor      spawned at " + Fmt(spawnedAt) + ", " + GapText(crate));

            // ---- into the hands ------------------------------------------
            crate.OnPressed(interactor!);
            if (crate.Carry == null || crate.Carry!.CarrierCount != 1)
            {
                throw new InvalidOperationException("§08's 궤짝 refused to be picked up: " + crate.Refusal);
            }

            Physics.SyncTransforms();
            Light(torch, on: true, rig);
            var heldAt = crate.transform.position;
            Shoot(camera, root, tag, "10_in_the_hands");
            report.AppendLine("  10_in_the_hands      held at " + Fmt(heldAt)
                + ", " + (heldAt.y - motor.transform.position.y).ToString("F2", CultureInfo.InvariantCulture)
                + " m above the boots, torch "
                + (torch != null && torch.InHand ? "in hand" : "stowed — §03 takes it from full hands"));

            // ---- and back out onto the floor -----------------------------
            crate.OnPressed(interactor!);
            if (crate.Carry!.CarrierCount != 0)
            {
                throw new InvalidOperationException("§08's 궤짝 refused to be put down: " + crate.Refusal);
            }

            Physics.SyncTransforms();
            Light(torch, on: true, rig);
            var restingAt = crate.transform.position;
            Shoot(camera, root, tag, "20_dropped_at_my_feet");
            report.AppendLine("  20_dropped_at_my_feet resting at " + Fmt(restingAt)
                + ", fell " + (heldAt.y - restingAt.y).ToString("F2", CultureInfo.InvariantCulture)
                + " m, " + GapText(crate)
                + ", tilt " + TiltText(crate) + ", " + BuriedText(crate));

            // Backed off and looked at again — the frame that answers "can I find the
            // thing I put down", which is the half of the defect a landing height cannot
            // settle.
            var backAt = ApproachFrom(restingAt);
            Teleport(motor.gameObject, backAt);
            look.SetLook(YawTowards(backAt, restingAt), LookDownDegrees);
            Settle(motor, rig);
            Light(torch, on: true, rig);
            Shoot(camera, root, tag, "30_walked_back_to_it");
            // No crosshair reading here on purpose: PlayerInteractor casts it from Update,
            // which does not run outside play mode. Whether the dropped piece is findable
            // again is settled by InteractionDropTests, which presses the real key in a
            // real frame; this frame only shows what that looks like.
            report.AppendLine("  30_walked_back_to_it  from " + Fmt(backAt));

            // A witness camera, because a first-person frame cannot show contact with the
            // floor — the piece's own base is behind its front face from every angle the
            // eye can take.
            ShootWitness(root, tag, "40_witness_side", crate.transform, restingAt);
            DisableOtherCameras(camera);
            report.AppendLine("  40_witness_side       side view, " + GapText(crate));

            // ---- the wall case -------------------------------------------
            // The half of the fix a clear corridor never exercises: facing a wall, the
            // sweep runs out of room and the piece goes to the boots instead of through
            // the bricks.
            var wall = StandFacingAWall(motor, look, rig, restingAt);
            if (!wall.HasValue)
            {
                report.AppendLine("  50_dropped_facing_a_wall  skipped — no wall this rig could stand within "
                    + CarryOffsetMetres.ToString("F2", CultureInfo.InvariantCulture)
                    + " m of, so the sweep would not be exercised and the frame would prove nothing");
            }
            else
            {
                Light(torch, on: true, rig);

                crate.OnPressed(interactor!);
                if (crate.Carry!.CarrierCount != 1)
                {
                    report.AppendLine("  50_dropped_facing_a_wall  skipped — the 궤짝 would not come back up ("
                        + crate.Refusal + ")");
                }
                else
                {
                    Physics.SyncTransforms();
                    crate.OnPressed(interactor!);
                    Physics.SyncTransforms();
                    Light(torch, on: true, rig);

                    var atWall = crate.transform.position;
                    var boots = motor.transform.position;
                    var pushed = Vector3.Distance(
                        new Vector3(atWall.x, 0f, atWall.z), new Vector3(boots.x, 0f, boots.z));

                    Shoot(camera, root, tag, "50_dropped_facing_a_wall");
                    report.AppendLine("  50_dropped_facing_a_wall  wall "
                        + wall.Value.ToString("F2", CultureInfo.InvariantCulture)
                        + " m ahead at hand height (carry offset "
                        + CarryOffsetMetres.ToString("F2", CultureInfo.InvariantCulture)
                        + " m), landed " + pushed.ToString("F2", CultureInfo.InvariantCulture)
                        + " m from the boots — "
                        + (pushed <= wall.Value + 0.01f ? "on this side of it" : "THROUGH IT")
                        + ", " + GapText(crate) + ", " + BuriedText(crate));
                }
            }

            return report.ToString();
        }

        /// <summary>
        /// A place to stand that can see the piece: the direction with the most room, at
        /// arm's length plus a step, so the crosshair has something to find.
        /// </summary>
        private static Vector3 ApproachFrom(Vector3 target)
        {
            var eye = target + new Vector3(0f, EyeHeightMetres, 0f);
            var bestClear = -1f;
            var bestDegrees = 0f;

            for (var degrees = 0f; degrees < 360f; degrees += 15f)
            {
                var forward = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
                var clear = Physics.Raycast(eye, forward, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)
                    ? hit.distance
                    : 12f;

                if (clear > bestClear)
                {
                    bestClear = clear;
                    bestDegrees = degrees;
                }
            }

            var away = Quaternion.Euler(0f, bestDegrees, 0f) * Vector3.forward;
            var stand = target + (away * Mathf.Min(StandOffMetres, Mathf.Max(bestClear - 0.4f, 0.6f)));

            // Onto whatever is under it, then a little clear of it: a CharacterController
            // teleported into the floor climbs out on its next move and the frame is of a
            // body still arriving.
            return Physics.Raycast(stand + (Vector3.up * 2f), Vector3.down, out var floor, 8f, ~0,
                QueryTriggerInteraction.Ignore)
                ? new Vector3(stand.x, floor.point.y + 0.15f, stand.z)
                : stand;
        }

        /// <summary>
        /// Puts the rig nose-to-nose with a wall and returns how far ahead that wall is,
        /// measured from where the body actually ended up.
        /// <para>
        /// Verified after the teleport rather than before it, and at the height the hands
        /// hold a piece rather than at the eye. The first version of this searched from
        /// the drop point at 1.6 m, found a ledge that is not there at 1.5 m, and reported
        /// a wall-facing drop that had no wall in it — a frame that would have been read
        /// as proof of a case it never exercised. Returns null when no heading leaves a
        /// wall inside the carry offset, because a shot that cannot fail is not evidence.
        /// </para>
        /// </summary>
        private static float? StandFacingAWall(
            PlayerMotor motor, PlayerLook look, PlayerCameraRig? rig, Vector3 near)
        {
            var from = near + new Vector3(0f, EyeHeightMetres, 0f);

            for (var degrees = 0f; degrees < 360f; degrees += 10f)
            {
                var forward = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
                if (!Physics.Raycast(from, forward, out var wall, 6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                var stand = wall.point - (forward * WallStandOffMetres);
                if (!Physics.Raycast(stand + (Vector3.up * 2f), Vector3.down, out var floor, 8f, ~0,
                    QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                Teleport(motor.gameObject, new Vector3(stand.x, floor.point.y + 0.15f, stand.z));
                look.SetLook(degrees, LookDownDegrees);
                Settle(motor, rig);

                // From the body that is actually standing there now, at the height a
                // carried piece is held.
                var boots = motor.transform.position;
                var hands = boots + new Vector3(0f, HandHeightMetres, 0f);
                if (Physics.Raycast(hands, forward, out var ahead, CarryOffsetMetres, ~0,
                    QueryTriggerInteraction.Ignore))
                {
                    return ahead.distance;
                }
            }

            return null;
        }

        private static float YawTowards(Vector3 from, Vector3 to)
        {
            var d = to - from;
            d.y = 0f;
            return d.sqrMagnitude < 0.0001f ? 0f : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        private static void Settle(PlayerMotor motor, PlayerCameraRig? rig)
        {
            Physics.SyncTransforms();

            if (rig != null)
            {
                // What LateUpdate does. Without it the eye sits at the pivot's authored
                // height rather than on the head bone, and every frame is of a camera the
                // player never looks through.
                rig.SnapToHeadAnchor();
            }
        }

        private static void Light(PlayerFlashlight? torch, bool on, PlayerCameraRig? rig)
        {
            if (torch == null)
            {
                return;
            }

            if (on)
            {
                torch.State.TryTurnOn();
            }
            else
            {
                torch.State.TurnOff();
            }

            // The game's own rule, run rather than restated: hands full takes the light
            // away again, and that is the frame a carrier really gets.
            torch.EnforceCarryRules();
            torch.RefreshPresentation();
            torch.SnapBeamToMount();

            if (rig != null)
            {
                rig.SnapToHeadAnchor();
            }
        }

        private static string GapText(OversizeLootInteractable piece)
        {
            var at = piece.transform.position;
            return Physics.Raycast(at + (Vector3.up * 0.4f), Vector3.down, out var under, 8f, ~0,
                QueryTriggerInteraction.Ignore)
                ? (at.y - under.point.y).ToString("F3", CultureInfo.InvariantCulture) + " m above the surface"
                : "nothing solid underneath";
        }

        private static string TiltText(OversizeLootInteractable piece)
        {
            var upright = Quaternion.Euler(0f, piece.transform.eulerAngles.y, 0f);
            return Quaternion.Angle(piece.transform.rotation, upright).ToString("F1", CultureInfo.InvariantCulture) + "°";
        }

        private static string BuriedText(OversizeLootInteractable piece)
        {
            var box = piece.GetComponent<Collider>();
            if (box == null)
            {
                return "no collider";
            }

            var bounds = box.bounds;
            var extents = bounds.extents - (Vector3.one * 0.02f);
            if (extents.x <= 0f || extents.y <= 0f || extents.z <= 0f)
            {
                return "too small to test";
            }

            var inside = Physics.OverlapBox(
                bounds.center, extents, piece.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            if (inside.Length == 0)
            {
                return "clear of geometry";
            }

            // How far in, not just whether. A wide piece set down against a wall touches
            // it — the sweep leaves the crosshair's own tolerance and nothing more — and
            // the difference between a few centimetres of contact and a piece swallowed
            // by the bricks is the whole question. Measured against the piece's real
            // shape rather than its bounding box.
            var deepest = 0f;
            var name = inside[0].name;

            for (var i = 0; i < inside.Length; i++)
            {
                if (Physics.ComputePenetration(
                    box, piece.transform.position, piece.transform.rotation,
                    inside[i], inside[i].transform.position, inside[i].transform.rotation,
                    out _, out var depth) && depth > deepest)
                {
                    deepest = depth;
                    name = inside[i].name;
                }
            }

            return deepest <= 0f
                ? "touching " + name + ", no measurable overlap"
                : "overlapping " + name + " by " + deepest.ToString("F3", CultureInfo.InvariantCulture) + " m";
        }

        private static string Fmt(Vector3 v)
        {
            return v.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Writes the pair: the frame as it is lit in the game, and the same frame with
        /// the ambient term raised so the shapes can be read.
        /// </summary>
        private static void Shoot(Camera camera, string root, string tag, string suffix)
        {
            RenderTo(camera, Path.Combine(root, tag + "_" + suffix + "_asplayed.png"));

            var ambient = RenderSettings.ambientLight;
            var mode = RenderSettings.ambientMode;
            var intensity = RenderSettings.ambientIntensity;

            try
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.42f);
                RenderSettings.ambientIntensity = 1f;
                RenderTo(camera, Path.Combine(root, tag + "_" + suffix + "_fill.png"));
            }
            finally
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientIntensity = intensity;
            }
        }

        /// <summary>
        /// A lit side view of the piece where it came to rest. The eye cannot photograph
        /// its own feet, and "is it touching the floor" is a question about the base.
        /// </summary>
        private static void ShootWitness(string root, string tag, string suffix, Transform piece, Vector3 restingAt)
        {
            var holder = new GameObject("DropShot Witness");
            try
            {
                var camera = holder.AddComponent<Camera>();
                camera.fieldOfView = 45f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 100f;

                var side = piece.right * WitnessDistanceMetres;
                var from = restingAt + side + new Vector3(0f, WitnessHeightMetres, 0f);
                holder.transform.position = from;
                holder.transform.LookAt(restingAt + new Vector3(0f, 0.25f, 0f));

                var lamp = new GameObject("DropShot Witness Light");
                try
                {
                    lamp.transform.position = from + new Vector3(0f, 1.5f, 0f);
                    lamp.transform.rotation = Quaternion.LookRotation(
                        (restingAt - lamp.transform.position).normalized);

                    var light = lamp.AddComponent<Light>();
                    light.type = LightType.Spot;
                    light.range = 12f;
                    light.spotAngle = 70f;
                    light.intensity = 12f;
                    light.color = new Color(0.92f, 0.94f, 1f);
                    light.shadows = LightShadows.Soft;

                    DisableOtherCameras(camera);
                    RenderTo(camera, Path.Combine(root, tag + "_" + suffix + ".png"));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(lamp);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        private static void PrepareCamera(Camera camera)
        {
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 300f;

            var post = camera.GetComponent<UniversalAdditionalCameraData>();
            if (post == null)
            {
                post = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            post.renderPostProcessing = true;
            post.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            post.antialiasingQuality = AntialiasingQuality.High;
        }

        private static void DisableOtherCameras(Camera keep)
        {
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                camera.enabled = camera == keep;
            }
        }

        private static void Teleport(GameObject body, Vector3 position)
        {
            var controller = body.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            body.transform.position = position;

            if (controller != null)
            {
                controller.enabled = true;
            }

            Physics.SyncTransforms();
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

        /// <summary>Eye height used only to aim the search rays, metres.</summary>
        private const float EyeHeightMetres = 1.6f;

        /// <summary>How far back the photographer stands from the piece, metres. Inside §03's reading distance.</summary>
        private const float StandOffMetres = 1.8f;

        /// <summary>
        /// How far from a wall the wall-facing drop is taken, metres. Shorter than
        /// <c>OversizeLootInteractable</c>'s 1.05 m carry offset, so a drop that did not
        /// sweep for room would put the piece through the bricks.
        /// </summary>
        private const float WallStandOffMetres = 0.75f;

        /// <summary>
        /// <c>OversizeLootInteractable.CarryOffset</c>, restated here because it is private
        /// there and this tool has to be able to say whether the sweep was exercised at
        /// all. If that number moves, this one has to move with it.
        /// </summary>
        private const float CarryOffsetMetres = 1.05f;

        /// <summary>Roughly where the hands hold a piece above the boots, metres. Only used to aim a probe.</summary>
        private const float HandHeightMetres = 1.4f;

        /// <summary>Degrees of downward pitch for the eye frames — where a person looks at something on the floor.</summary>
        private const float LookDownDegrees = 22f;

        private const float WitnessDistanceMetres = 2.2f;
        private const float WitnessHeightMetres = 0.55f;
    }
}
