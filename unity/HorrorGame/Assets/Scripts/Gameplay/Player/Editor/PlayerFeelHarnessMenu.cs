#nullable enable

using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Movement;
using HorrorGame.Gameplay.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HorrorGame.Gameplay.PlayerEditor
{
    /// <summary>
    /// Builds the §05 feel harness with the real player rig and the real footstep clips,
    /// then enters play mode.
    /// <para>
    /// §14's first two validation questions have to be answered by one person with a mouse,
    /// and the thing that usually stops that happening is not the code — it is that the
    /// scene does not exist yet, and building it means dragging forty audio clips onto a
    /// component. So the wiring is done here, from <c>AssetDatabase</c>, where the paths
    /// are known and the failure is a console message rather than a silently empty array.
    /// </para>
    /// <para>
    /// Nothing is written to <c>Assets/Scenes</c>. The build pipeline falls back to
    /// discovering scenes on disk when Build Settings is empty
    /// (<c>BuildPipelineScenes.TryCollect</c>), so a development scene left lying there
    /// would eventually ship inside a player. The harness lives for the length of a play
    /// session and then it is gone.
    /// </para>
    /// </summary>
    public static class PlayerFeelHarnessMenu
    {
        private const string PlayerModelPath = "Assets/Models/Player/Runner.fbx";
        private const string FootstepFolder = "Assets/Audio/Footsteps";
        private const string ControlsPath =
            "Assets/Scripts/Gameplay/Player/Resources/PlayerControls.inputactions";

        // Rig dimensions from ASSETS.md: Player.fbx stands 1.75 m. These size the collider
        // and place the eye when the harness builds a rig from scratch; they describe the
        // model, not a tuned game value, which is why they are not in GameConstants. The
        // height itself comes from ViewMotionTuning, which derives the landing reference
        // from it — two copies of a body's height is exactly the kind of duplicate that
        // drifts and then makes a fall the wrong size.
        private const float RigHeightMetres = ViewMotionTuning.RigHeightMetres;
        private const float RigRadiusMetres = 0.3f;
        private const float EyeHeightMetres = 1.63f;

        [MenuItem("Horror Game/Player/Feel Harness - Section 05 Movement", false, 10)]
        private static void OpenHarness()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var harnessObject = new GameObject("[§05 Feel Harness]");
            var harness = harnessObject.AddComponent<PlayerFeelHarness>();

            var rig = BuildRig();
            if (rig != null)
            {
                Bind(harness, rig);
            }

            Selection.activeGameObject = harnessObject;
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Horror Game/Player/Report Section 05 Speed Table", false, 20)]
        private static void ReportSpeedTable()
        {
            // Prints §05's four rows straight out of the resolver, so the table in the
            // design document can be checked against the code without opening either.
            var report = new System.Text.StringBuilder();
            report.AppendLine("§05 speed table, resolved by SpeedResolver at 주자 질주 "
                + GameConstants.RunnerSprintSpeed.ToString("0.00", CultureInfo.InvariantCulture)
                + " m/s vs monster " + GameConstants.MonsterBaseSpeed.ToString("0.00", CultureInfo.InvariantCulture));

            AppendRow(report, "W      전진", 1f, 0f);
            AppendRow(report, "W+D    대각", 1f, 1f);
            AppendRow(report, "D      측면", 0f, 1f);
            AppendRow(report, "S      후진", -1f, 0f);

            Debug.Log(report.ToString());
        }

        private static void AppendRow(System.Text.StringBuilder report, string label, float forward, float strafe)
        {
            var input = new MoveInput(forward, strafe, true);
            var context = MovementContext.Unloaded(GameConstants.RunnerSprintSpeed);
            var speed = SpeedResolver.Resolve(input, context);
            var margin = new ChaseMargin(speed, GameConstants.MonsterBaseSpeed);

            report.AppendLine(
                label
                + "  x" + SpeedResolver.DirectionalMultiplier(input).ToString("0.00", CultureInfo.InvariantCulture)
                + "  = " + speed.ToString("0.00", CultureInfo.InvariantCulture) + " m/s"
                + "  margin " + margin.MetresPerSecond.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " m/s");
        }

        /// <summary>
        /// Assembles a complete, drivable player: character controller, visual rig,
        /// camera, flashlight and every gameplay component, wired together.
        /// <para>
        /// Public because the solo playtest scene builder needs the same rig in the
        /// real map. Two places that assemble a player would drift, and the one that
        /// drifted would be the one nobody was looking at.
        /// </para>
        /// </summary>
        /// <returns>The assembled player, or null if Player.fbx is missing.</returns>
        public static GameObject? BuildRig()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            if (model == null)
            {
                Debug.LogWarning(
                    "[Player] " + PlayerModelPath + " not found; the harness will fall back to a capsule. "
                    + "Speed and turning still answer §14's questions 1 and 2, but the carry pose and the "
                    + "flashlight mount will not be visible.");
                return null;
            }

            var body = new GameObject("Player");
            body.transform.position = Vector3.zero;

            var controller = body.AddComponent<CharacterController>();
            controller.height = RigHeightMetres;
            controller.radius = RigRadiusMetres;
            controller.center = new Vector3(0f, RigHeightMetres * 0.5f, 0f);
            controller.slopeLimit = 50f;

            // §12 depends on this number: the map's geometry is derived from what a
            // player cannot climb, and the jump's apex is bounded below it. See
            // GameConstants.PlayerStepOffsetMetres.
            controller.stepOffset = GameConstants.PlayerStepOffsetMetres;

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "Visual";
            visual.transform.SetParent(body.transform, false);

            var pivot = new GameObject("PitchPivot").transform;
            pivot.SetParent(body.transform, false);
            pivot.localPosition = new Vector3(0f, EyeHeightMetres, 0f);

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(pivot, false);
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = GameConstants.FovDefault;
            camera.gameObject.AddComponent<AudioListener>();

            var beam = new GameObject("Flashlight").AddComponent<Light>();
            beam.transform.SetParent(body.transform, false);
            beam.type = LightType.Spot;

            body.AddComponent<PlayerInputRouter>();
            var look = body.AddComponent<PlayerLook>();

            // Before the motor and the animator: both look for one in Awake, and
            // AddComponent runs Awake immediately, so this order is the wiring.
            var stance = body.AddComponent<PlayerStance>();
            var motor = body.AddComponent<PlayerMotor>();
            body.AddComponent<PlayerCameraRig>();
            var flashlight = body.AddComponent<PlayerFlashlight>();
            var animator = body.AddComponent<PlayerAnimatorDriver>();
            var footsteps = body.AddComponent<PlayerFootsteps>();

            // After the animator, so its Footfall event is already there to subscribe
            // to: the stride the camera rides is pinned to the step the player hears,
            // and a component that missed the subscription would drift out of phase
            // with the sound within a few seconds.
            var viewMotion = body.AddComponent<PlayerViewMotion>();

            // §01's race is played over the shoulder, so the camera that does it has to be
            // on the rig the moment the rig exists. It was written, committed, and attached
            // to nothing — the owner played a build I had called third-person and got first
            // person, because I checked the code instead of the scene. It ships off and
            // costs nothing until V is pressed.
            body.AddComponent<ThirdPersonCamera>();

            // DELETED from the rig: PlayerLoadout (§08's inventory and carry weight),
            // PlayerHeldProp (§03's 목표물 and §08's 대형 전리품 as actual models in the
            // hands) and `motor.Role = RoleId.Runner`. Every runner in a race carries a
            // torch, has 질주, and has no role to be assigned.
            _ = motor;

            // After every renderer exists and before anything is wired to it: this is
            // what decides that the owner sees hands and not a chest.
            ConfigureFirstPersonBody(body);
            AssignSerialized(flashlight, "_view", body.GetComponent<PlayerFirstPersonView>());

            AssignSerialized(look, "_pitchPivot", pivot);
            AssignSerialized(animator, "_animator", visual.GetComponentInChildren<Animator>());

            // Wired explicitly rather than left to the component's own Awake fallback:
            // this rig is saved into Map_FirstSketch_Solo.unity, and a scene that
            // relies on a runtime search is a scene where adding a second camera
            // silently moves the eye.
            AssignSerialized(viewMotion, "_motor", motor);
            AssignSerialized(viewMotion, "_animator", animator);
            AssignSerialized(viewMotion, "_stance", stance);
            AssignSerialized(viewMotion, "_cameraTransform", camera.transform);
            AssignSerialized(motor, "_stance", stance);
            AssignSerialized(animator, "_stance", stance);
            AssignClips(animator);
            AssignFootsteps(footsteps);
            AssignControls(body);

            return body;
        }

        /// <summary>
        /// Hides what would sit across the owner's near plane and leaves their hands
        /// drawn, with the whole body still casting its shadow.
        /// <para>
        /// The policy is <see cref="PlayerFirstPersonView"/>'s, not this file's, and that
        /// is the point. The version this replaced set <em>every</em> renderer on the
        /// model to ShadowsOnly — which did fix the chest across the bottom of the screen
        /// and took §05's 손 with it, leaving the player with nothing of their own on
        /// screen at all. A rule that decides what an owner sees belongs in a runtime
        /// component that the network layer can also reach (§13 draws three other bodies
        /// normally), not in an editor menu that only the harness runs.
        /// </para>
        /// <para>
        /// <c>Apply</c> is called explicitly because this runs outside play mode, where
        /// <c>Awake</c> never fires, and the result is serialised into
        /// <c>Map_FirstSketch_Solo.unity</c>.
        /// </para>
        /// </summary>
        private static void ConfigureFirstPersonBody(GameObject body)
        {
            var view = body.AddComponent<PlayerFirstPersonView>();
            AssignSerialized(view, "_rigRoot", body.transform);
            view.Apply();

            // The hands are drawn by the line above and legible because of this one: on a
            // shipped frame they measured 3.5 of 255 against a floor at 16.0. §03 keeps
            // the building dark; the fill reaches the arms' rendering layer and nothing
            // else, so it cannot pay for that darkness.
            body.AddComponent<PlayerHandFill>().Apply();

            // And the other half of the same disagreement. §05 holds the forearms up so
            // the owner can see their own hands at 80° FOV; from outside that is a person
            // standing with their elbows flared. This lowers them on the copy other
            // players look at, after the Animator, and leaves the owner's alone.
            body.AddComponent<PlayerWorldArms>();
        }

        private static void Bind(PlayerFeelHarness harness, GameObject rig)
        {
            AssignSerialized(harness, "_motor", rig.GetComponent<PlayerMotor>());
            AssignSerialized(harness, "_cameraRig", rig.GetComponent<PlayerCameraRig>());
            AssignSerialized(harness, "_footsteps", rig.GetComponent<PlayerFootsteps>());
            AssignSerialized(harness, "_input", rig.GetComponent<PlayerInputRouter>());
        }

        private static void AssignControls(GameObject body)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(ControlsPath);
            if (asset == null)
            {
                // Not fatal: PlayerInputRouter loads the same file from Resources at runtime.
                return;
            }

            AssignSerialized(body.GetComponent<PlayerInputRouter>(), "_actions", asset);
        }

        /// <summary>
        /// Assigns the nine clips <c>Player.fbx</c> carries. They are sub-assets of the FBX,
        /// so they are matched by name rather than by index — the importer's order is not a
        /// contract and §03 cares which one is the carry pose.
        /// </summary>
        private static void AssignClips(PlayerAnimatorDriver driver)
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(PlayerModelPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    clips[clip.name] = clip;
                }
            }

            AssignClip(driver, "_idle", clips, "Idle");
            AssignClip(driver, "_walk", clips, "Walk");
            AssignClip(driver, "_run", clips, "Run");
            AssignClip(driver, "_crouch", clips, "Crouch");
            AssignClip(driver, "_crouchWalk", clips, "CrouchWalk");
            AssignClip(driver, "_carry", clips, "Carry");
            AssignClip(driver, "_carryIdle", clips, "CarryIdle");
            AssignClip(driver, "_carryHeavy", clips, "CarryHeavy");
            AssignClip(driver, "_death", clips, "Death");
        }

        private static void AssignClip(
            PlayerAnimatorDriver driver, string field, Dictionary<string, AnimationClip> clips, string clipName)
        {
            if (clips.TryGetValue(clipName, out var clip))
            {
                AssignSerialized(driver, field, clip);
                return;
            }

            Debug.LogWarning("[Player] Player.fbx has no clip named '" + clipName + "'; that pose will not play.");
        }

        /// <summary>
        /// Fills the footstep sets from <c>Assets/Audio/Footsteps</c>. §12 makes the five
        /// surfaces a system requirement, so a missing set is reported rather than left as
        /// a silent floor.
        /// </summary>
        private static void AssignFootsteps(PlayerFootsteps footsteps)
        {
            var surfaces = new[]
            {
                FloorMaterial.Wood,
                FloorMaterial.Tile,
                FloorMaterial.Gravel,
                FloorMaterial.Concrete,
                FloorMaterial.Metal,
            };

            var serialized = new SerializedObject(footsteps);
            var sets = serialized.FindProperty("_clipSets");
            if (sets == null)
            {
                return;
            }

            sets.arraySize = surfaces.Length;

            for (var i = 0; i < surfaces.Length; i++)
            {
                var element = sets.GetArrayElementAtIndex(i);
                // intValue, not enumValueIndex: the latter is a position in the name list and
                // would silently shift if FloorMaterial ever gained a member.
                element.FindPropertyRelative("Material").intValue = (int)surfaces[i];

                FillClipArray(element.FindPropertyRelative("Walk"), surfaces[i], "walk");
                FillClipArray(element.FindPropertyRelative("Run"), surfaces[i], "run");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FillClipArray(SerializedProperty array, FloorMaterial surface, string gait)
        {
            var prefix = "step_" + surface.ToString().ToLowerInvariant() + "_player_" + gait + "_";
            var found = new List<AudioClip>();

            for (var variant = 1; variant <= 4; variant++)
            {
                var path = FootstepFolder + "/" + prefix
                    + variant.ToString("00", CultureInfo.InvariantCulture) + ".wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                {
                    found.Add(clip);
                }
            }

            if (found.Count == 0)
            {
                Debug.LogWarning(
                    "[Player] No clips matching " + FootstepFolder + "/" + prefix + "NN.wav. §12 needs all "
                    + "five surfaces distinguishable or §04's Listener cannot place the monster.");
            }

            array.arraySize = found.Count;
            for (var i = 0; i < found.Count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            }
        }

        private static void AssignSerialized(UnityEngine.Object? target, string field, UnityEngine.Object? value)
        {
            if (target == null)
            {
                return;
            }

            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning("[Player] " + target.GetType().Name + " has no serialized field '" + field + "'.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
