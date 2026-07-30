#nullable enable

using System.Globalization;
using System.Text;
using HorrorGame.Core.Map;
using HorrorGame.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Gameplay.PlayerEditor
{
    /// <summary>
    /// Checks that the assets §05 and §12 depend on are still there and still the right
    /// shape.
    /// <para>
    /// None of this can live in a PlayMode test, because a test assembly cannot reach
    /// <see cref="AssetDatabase"/> at runtime — and none of it is hypothetical. Every item
    /// here fails silently rather than loudly:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// Re-importing <c>Player.fbx</c> with <b>Optimize Game Objects</b> on removes the four
    /// attachment bones. The camera then sits at the origin and the flashlight points out
    /// of the player's navel, so §05's "남의 손전등 방향이 정보다" becomes noise — with no
    /// error anywhere.
    /// </description></item>
    /// <item><description>
    /// A footstep clip re-imported as stereo does not warn. Unity simply stops
    /// spatialising it, and §04's Listener loses direction and distance on that surface
    /// while everything still "works" (ASSETS.md §1).
    /// </description></item>
    /// <item><description>
    /// A missing surface set means one §12 zone is silent, which the Listener reads as
    /// "the monster is not there".
    /// </description></item>
    /// </list>
    /// <para>
    /// Run from the menu, or from CI with
    /// <c>-executeMethod HorrorGame.Gameplay.PlayerEditor.PlayerAssetValidation.RunBatch</c>,
    /// which exits non-zero on the first problem.
    /// </para>
    /// </summary>
    public static class PlayerAssetValidation
    {
        private const string PlayerModelPath = "Assets/Models/Characters/Player.fbx";
        private const string FootstepFolder = "Assets/Audio/Footsteps";
        private const int FootstepVariants = 4;

        private static readonly string[] RequiredClips =
        {
            "Idle", "Walk", "Run", "Crouch", "CrouchWalk", "Carry", "CarryIdle", "CarryHeavy", "Death",
        };

        private static readonly FloorMaterial[] RequiredSurfaces =
        {
            FloorMaterial.Wood, FloorMaterial.Tile, FloorMaterial.Gravel,
            FloorMaterial.Concrete, FloorMaterial.Metal,
        };

        [MenuItem("Horror Game/Player/Validate Player Assets", false, 30)]
        private static void RunFromMenu()
        {
            var report = new StringBuilder();
            var failures = Validate(report);

            if (failures == 0)
            {
                Debug.Log(report.ToString());
                return;
            }

            Debug.LogError(report.ToString());
        }

        /// <summary>CI entry point. Exits 1 when anything §05 or §12 relies on is missing or wrong.</summary>
        public static void RunBatch()
        {
            var report = new StringBuilder();
            var failures = Validate(report);
            Debug.Log(report.ToString());
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>
        /// Appends a line per check and returns how many failed, so a caller can decide
        /// between a log, an error and an exit code.
        /// </summary>
        /// <param name="report">Receives the human-readable result.</param>
        public static int Validate(StringBuilder report)
        {
            var failures = 0;

            report.AppendLine("[Player assets] §05 rig, §05 controls, §12 surfaces");

            failures += CheckRig(report);
            failures += CheckControls(report);
            failures += CheckFootsteps(report);

            report.AppendLine("[Player assets] " + (failures == 0 ? "all checks passed" : failures + " FAILED"));
            return failures;
        }

        private static int Check(StringBuilder report, bool condition, string label)
        {
            report.AppendLine((condition ? "  ok   " : "  FAIL ") + label);
            return condition ? 0 : 1;
        }

        private static int CheckRig(StringBuilder report)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            if (model == null)
            {
                return Check(report, false, PlayerModelPath + " is missing");
            }

            var failures = 0;
            var instance = Object.Instantiate(model);

            try
            {
                failures += Check(
                    report,
                    PlayerRigBones.Find(instance.transform, PlayerRigBones.HeadCameraAnchor) != null,
                    PlayerRigBones.HeadCameraAnchor + " bone present (§05 puts the eye here)");
                failures += Check(
                    report,
                    PlayerRigBones.Find(instance.transform, PlayerRigBones.FlashlightMount) != null,
                    PlayerRigBones.FlashlightMount + " bone present (§05: the beam is a pointing device)");
                failures += Check(
                    report,
                    PlayerRigBones.Find(instance.transform, PlayerRigBones.ObjectiveMount) != null,
                    PlayerRigBones.ObjectiveMount + " bone present (§03 carry)");
                failures += Check(
                    report,
                    instance.GetComponentInChildren<Animator>() != null,
                    "Animator present");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            var found = 0;
            var names = new StringBuilder();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(PlayerModelPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    found++;
                    names.Append(clip.name).Append(' ');
                }
            }

            failures += Check(
                report, found == RequiredClips.Length,
                RequiredClips.Length + " clips expected, " + found + " found: " + names.ToString().Trim());

            foreach (var required in RequiredClips)
            {
                var present = names.ToString().Contains(required);
                if (!present)
                {
                    failures += Check(report, false, "clip '" + required + "' missing");
                }
            }

            return failures;
        }

        private static int CheckControls(StringBuilder report)
        {
            var asset = Resources.Load<InputActionAsset>(PlayerInputRouter.DefaultAssetResourcePath);
            if (asset == null)
            {
                return Check(
                    report, false,
                    "Resources/" + PlayerInputRouter.DefaultAssetResourcePath + " does not load");
            }

            var map = asset.FindActionMap("Player", false);
            if (map == null)
            {
                return Check(report, false, "PlayerControls has no 'Player' action map");
            }

            var failures = 0;

            // §05's control table, one row at a time.
            failures += CheckBinding(report, map, "Move", "<Keyboard>/w", "W/S/A/D relative to the view");
            failures += CheckBinding(report, map, "Move", "<Keyboard>/s", "W/S/A/D relative to the view");
            failures += CheckBinding(report, map, "Move", "<Keyboard>/a", "W/S/A/D relative to the view");
            failures += CheckBinding(report, map, "Move", "<Keyboard>/d", "W/S/A/D relative to the view");
            failures += CheckBinding(report, map, "Look", "<Mouse>/delta", "mouse defines forward");
            failures += CheckBinding(report, map, "Sprint", "<Keyboard>/leftShift", "Shift = 달리기 / 질주");
            failures += CheckBinding(report, map, "Flashlight", "<Keyboard>/f", "F = 손전등 on/off");

            return failures;
        }

        private static int CheckBinding(
            StringBuilder report, InputActionMap map, string actionName, string path, string why)
        {
            var action = map.FindAction(actionName, false);
            if (action == null)
            {
                return Check(report, false, "action '" + actionName + "' missing (" + why + ")");
            }

            foreach (var binding in action.bindings)
            {
                if (binding.path == path)
                {
                    return Check(report, true, actionName + " <- " + path);
                }
            }

            return Check(report, false, actionName + " is not bound to " + path + " (" + why + ")");
        }

        private static int CheckFootsteps(StringBuilder report)
        {
            var failures = 0;

            foreach (var surface in RequiredSurfaces)
            {
                foreach (var gait in new[] { "walk", "run" })
                {
                    var found = 0;
                    var stereo = 0;

                    for (var variant = 1; variant <= FootstepVariants; variant++)
                    {
                        var path = FootstepFolder + "/step_" + surface.ToString().ToLowerInvariant()
                            + "_player_" + gait + "_"
                            + variant.ToString("00", CultureInfo.InvariantCulture) + ".wav";

                        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                        if (clip == null)
                        {
                            continue;
                        }

                        found++;
                        if (clip.channels != 1)
                        {
                            stereo++;
                        }
                    }

                    failures += Check(
                        report,
                        found == FootstepVariants && stereo == 0,
                        surface + " " + gait + ": " + found + "/" + FootstepVariants
                        + " clips, " + stereo + " stereo (must be 0 — a stereo clip does not spatialise)");
                }
            }

            return failures;
        }
    }
}
