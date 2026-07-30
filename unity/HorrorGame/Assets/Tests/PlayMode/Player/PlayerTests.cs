#nullable enable

using System.Collections;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;
using HorrorGame.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.PlayerRig
{
    /// <summary>
    /// §05 as it actually behaves once a <see cref="CharacterController"/>, a floor and a
    /// frame loop are involved.
    /// <para>
    /// <c>MovementTests</c> in the core suite already pins the arithmetic. What it cannot
    /// see is the gap between "the resolver returned 3.64" and "the body moved 3.64 metres
    /// in a second" — a controller that applied speed per frame instead of per second, or
    /// scaled the raw input vector instead of a unit one, would pass every core test and
    /// still make §14's questions 1 and 2 unanswerable. So these tests measure distance
    /// travelled, not numbers returned.
    /// </para>
    /// </summary>
    public sealed class PlayerTests
    {
        private const float Step = 1f / 50f;
        private GameObject? _floor;
        private GameObject? _body;

        [TearDown]
        public void TearDown()
        {
            if (_body != null)
            {
                Object.DestroyImmediate(_body);
                _body = null;
            }

            if (_floor != null)
            {
                Object.DestroyImmediate(_floor);
                _floor = null;
            }
        }

        [UnityTest]
        public IEnumerator Forward_sprint_moves_the_body_at_section05_full_speed()
        {
            var motor = BuildPlayer(RoleId.Runner);
            yield return null;
            Settle(motor);

            var travelled = Travel(motor, new MoveInput(1f, 0f, true), 2f);

            Assert.That(travelled, Is.EqualTo(GameConstants.RunnerSprintSpeed).Within(0.02f),
                "§05: 전진 100% of 주자 질주 has to arrive as metres on the floor, not just as a "
                + "number out of SpeedResolver.");
        }

        [UnityTest]
        public IEnumerator Backpedalling_is_slower_than_the_monster()
        {
            var motor = BuildPlayer(RoleId.Runner);
            yield return null;
            Settle(motor);

            var travelled = Travel(motor, new MoveInput(-1f, 0f, true), 2f);

            Assert.That(travelled, Is.LessThan(GameConstants.MonsterBaseSpeed),
                "§05's conclusion is a statement about the engine, not about a table: "
                + "'뒷걸음은 괴물보다 느리다. 뒤를 보면 잡힌다.'");
            Assert.That(travelled,
                Is.EqualTo(GameConstants.RunnerSprintSpeed * GameConstants.MulBackward).Within(0.02f));
        }

        [UnityTest]
        public IEnumerator The_peek_costs_speed_continuously_rather_than_in_four_jumps()
        {
            var motor = BuildPlayer(RoleId.Runner);
            yield return null;

            // A 22.5 degree turn is not one of §05's four rows. If it costs either nothing
            // or the full diagonal, the analogue control §05 calls 실력 does not exist.
            var straight = SpeedResolver.MultiplierForHeading(0f);
            var slight = SpeedResolver.MultiplierForHeading(22.5f);
            var peek = SpeedResolver.MultiplierForHeading(GameConstants.PeekAngleDegrees);

            Assert.That(slight, Is.LessThan(straight));
            Assert.That(slight, Is.GreaterThan(peek));

            var safe = motor.SafePeekAngleDegrees(GameConstants.MonsterBaseSpeed, true);
            Assert.That(safe, Is.GreaterThan(GameConstants.PeekAngleDegrees),
                "§05 prices the 45° peek at 95%, which still beats the monster — the dilemma is "
                + "where the budget runs out, and that has to be a real, findable angle.");
        }

        [UnityTest]
        public IEnumerator The_sprint_bar_empties_and_drops_the_runner_to_running_speed()
        {
            var motor = BuildPlayer(RoleId.Runner);
            yield return null;

            var input = new MoveInput(1f, 0f, true);
            var steps = Mathf.RoundToInt((GameConstants.SprintStaminaSeconds + 1f) / Step);
            for (var i = 0; i < steps; i++)
            {
                motor.Step(input, Step);
            }

            Assert.That(motor.Stamina.Fraction, Is.EqualTo(0f).Within(0.02f),
                "§06 sizes the bar at 12 s so that '주자도 스태미나가 끝나면 잡힌다'.");
            Assert.That(motor.LastContext.BaseSpeed, Is.EqualTo(GameConstants.RunSpeed).Within(0.001f));

            motor.Step(input, Step);
            Assert.That(motor.ResolvedSpeed, Is.LessThan(GameConstants.MonsterBaseSpeed),
                "An exhausted Runner has to be catchable, or §06's dilemma has no downside.");
        }

        [UnityTest]
        public IEnumerator A_non_runner_never_outruns_the_monster()
        {
            var motor = BuildPlayer(RoleId.Listener);
            yield return null;

            motor.Step(new MoveInput(1f, 0f, true), Step);

            Assert.That(motor.ResolvedSpeed, Is.EqualTo(GameConstants.RunSpeed).Within(0.001f),
                "§04 gives 질주 to the Runner alone; everyone else's Shift buys 달리기 4.5.");
            Assert.That(motor.ResolvedSpeed, Is.LessThan(GameConstants.MonsterBaseSpeed));
            Assert.That(motor.Stamina.Fraction, Is.EqualTo(1f).Within(0.001f),
                "A role that cannot sprint must not be spending a sprint bar.");
        }

        [UnityTest]
        public IEnumerator Carrying_the_objective_costs_speed_and_forbids_sprinting()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var loadout = motor.GetComponent<PlayerLoadout>();
            yield return null;

            Assert.That(loadout.SetCarryingObjective(true), Is.True);
            motor.Step(new MoveInput(1f, 0f, true), Step);

            Assert.That(motor.LastContext.BaseSpeed, Is.EqualTo(GameConstants.RunSpeed).Within(0.001f),
                "§03: 주자가 들면 질주 불가.");
            Assert.That(motor.ResolvedSpeed,
                Is.LessThan(GameConstants.RunSpeed * GameConstants.ObjectiveCarrySpeedMultiplier + 0.001f),
                "§05 applies the objective's ×0.80 on top of everything else.");
        }

        [UnityTest]
        public IEnumerator The_bag_penalty_is_charged_once_and_not_twice()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var loadout = motor.GetComponent<PlayerLoadout>();
            yield return null;

            motor.Step(new MoveInput(1f, 0f, true), Step);
            var bare = motor.ResolvedSpeed;

            Assert.That(loadout.Inventory.TryEquipBag(), Is.True);
            motor.Step(new MoveInput(1f, 0f, true), Step);

            Assert.That(motor.ResolvedSpeed,
                Is.EqualTo(bare * GameConstants.BagSpeedMultiplier).Within(0.001f),
                "Inventory.SpeedMultiplier already contains §08's bag penalty. Setting "
                + "MovementContext.BagEquipped as well would charge it twice, which looks like "
                + "a feel problem and is arithmetic.");
        }

        [UnityTest]
        public IEnumerator Movement_can_be_locked_without_locking_the_view()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var look = motor.GetComponent<PlayerLook>();
            yield return null;

            motor.MovementLocked = true;
            var travelled = Travel(motor, new MoveInput(1f, 0f, true), 0.5f);
            Assert.That(travelled, Is.LessThan(0.01f));

            look.SetLook(123f, -20f);
            Assert.That(look.YawDegrees, Is.EqualTo(123f).Within(0.01f),
                "§05 decides this explicitly for §04's Observer: 이동만 정지, 마우스룩 허용.");
        }

        [UnityTest]
        public IEnumerator Mouse_yaw_defines_which_way_forward_is()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var look = motor.GetComponent<PlayerLook>();
            yield return null;

            look.SetLook(90f, 0f);
            Settle(motor);
            var start = motor.transform.position;
            for (var i = 0; i < Mathf.RoundToInt(0.5f / Step); i++)
            {
                motor.Step(new MoveInput(1f, 0f, false), Step);
            }

            var delta = motor.transform.position - start;
            Assert.That(delta.x, Is.GreaterThan(0.5f),
                "§05: 마우스 방향 = 이동 방향. At yaw 90 the body has to travel along +X.");
            Assert.That(Mathf.Abs(delta.z), Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator The_camera_holds_section05s_fov_window()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var rig = motor.GetComponent<PlayerCameraRig>();
            yield return null;

            rig.FieldOfView = 120f;
            Assert.That(rig.FieldOfView, Is.EqualTo(GameConstants.FovMax));
            Assert.That(rig.FirstPersonCamera!.fieldOfView, Is.EqualTo(GameConstants.FovMax).Within(0.01f),
                "§05 makes FOV a balance item: '95+ 곁눈질이 너무 쉬워 딜레마가 약화된다.'");

            rig.FieldOfView = 50f;
            Assert.That(rig.FieldOfView, Is.EqualTo(GameConstants.FovMin));
        }

        [UnityTest]
        public IEnumerator The_flashlight_cone_is_the_cores_cone()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var torch = motor.GetComponent<PlayerFlashlight>();
            var light = motor.GetComponentInChildren<Light>();
            yield return null;

            Assert.That(torch.State.TryTurnOn(), Is.True);
            yield return null;

            Assert.That(light.enabled, Is.True);
            Assert.That(light.spotAngle, Is.EqualTo(GameConstants.FlashlightHalfAngle * 2f).Within(0.01f),
                "§03's 빛이 좁다 is a half-angle; Unity's spotAngle is the full cone.");
            Assert.That(light.range, Is.EqualTo(GameConstants.FlashlightRange).Within(0.01f));

            torch.TierRangeMultiplier = 1f - GameConstants.LateNightFlashlightPenalty;
            yield return null;

            Assert.That(light.range,
                Is.EqualTo(GameConstants.FlashlightRange * (1f - GameConstants.LateNightFlashlightPenalty))
                    .Within(0.01f),
                "§07's 심야 penalty has to reach the actual beam, not just the rules object.");
        }

        [UnityTest]
        public IEnumerator Carrying_the_objective_takes_the_flashlight_away()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var torch = motor.GetComponent<PlayerFlashlight>();
            var loadout = motor.GetComponent<PlayerLoadout>();
            yield return null;

            torch.State.TryTurnOn();
            yield return null;
            Assert.That(torch.IsLit, Is.True);

            loadout.SetCarryingObjective(true);
            yield return null;

            Assert.That(torch.IsLit, Is.False,
                "§03: 양손을 쓴다 · 손전등을 들 수 없다 → 누군가 비춰줘야 한다.");
        }

        [UnityTest]
        public IEnumerator Footsteps_read_the_real_surface_underfoot()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var footsteps = motor.GetComponent<PlayerFootsteps>();
            _floor!.GetComponent<FloorSurfaceTag>().FloorMaterial = FloorMaterial.Gravel;
            yield return null;
            yield return null;

            Assert.That(footsteps.Surface, Is.EqualTo(FloorMaterial.Gravel),
                "§12 makes the floor material a system decision, so the lookup has to be a real "
                + "query against the geometry the player is standing on.");

            _floor.GetComponent<FloorSurfaceTag>().FloorMaterial = FloorMaterial.Metal;
            yield return null;
            yield return null;

            Assert.That(footsteps.Surface, Is.EqualTo(FloorMaterial.Metal),
                "Crossing a material boundary has to change the answer, or §04's Listener cannot "
                + "tell zone C from the stairwell.");
        }

        [UnityTest]
        public IEnumerator An_unauthored_floor_reports_None_rather_than_guessing()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var footsteps = motor.GetComponent<PlayerFootsteps>();
            Object.DestroyImmediate(_floor!.GetComponent<FloorSurfaceTag>());
            yield return null;
            yield return null;

            Assert.That(footsteps.Surface, Is.EqualTo(FloorMaterial.None),
                "A plausible-sounding wrong surface would misinform §04's Listener; silence is the "
                + "failure mode somebody notices.");
        }

        [UnityTest]
        public IEnumerator The_animation_graph_plays_and_reports_footfalls()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var driver = motor.gameObject.AddComponent<PlayerAnimatorDriver>();
            var animator = motor.gameObject.AddComponent<Animator>();

            SetClip(driver, "_idle", MakeClip("Idle", 1f));
            SetClip(driver, "_walk", MakeClip("Walk", 1f));
            SetClip(driver, "_run", MakeClip("Run", 1f));
            SetPrivate(driver, "_animator", animator);

            driver.enabled = false;
            driver.enabled = true;
            yield return null;

            Assert.That(driver.State, Is.EqualTo(PlayerAnimationState.Idle));

            var footfalls = 0;
            driver.Footfall += () => footfalls++;

            // Bounded by game time rather than frame count: batchmode runs uncapped, and 90
            // frames there can be a tenth of a second — not enough clip time for a foot to
            // come round at all.
            var input = new MoveInput(1f, 0f, true);
            for (var elapsed = 0f; elapsed < 2.5f; elapsed += Time.deltaTime)
            {
                motor.Step(input, Step);
                yield return null;
            }

            Assert.That(driver.State, Is.EqualTo(PlayerAnimationState.Run),
                "§05 needs the pose other players see to match what the motor is doing.");
            Assert.That(footfalls, Is.GreaterThan(0),
                "§12 makes footstep timing the Listener's channel, so the graph has to report "
                + "when a foot lands.");
        }

        [UnityTest]
        public IEnumerator The_feel_harness_builds_itself_and_survives_a_few_frames()
        {
            LogAssert.ignoreFailingMessages = true;

            var before = new System.Collections.Generic.HashSet<GameObject>(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects());

            var host = new GameObject("harness");
            host.AddComponent<PlayerFeelHarness>();

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.That(Object.FindAnyObjectByType<PlayerMotor>(), Is.Not.Null,
                "§14 asks one person to answer questions 1 and 2 alone; the harness has to stand "
                + "itself up.");

            // The harness parks its player and its pacer at the scene root, so anything new
            // has to go or it leaks into whichever test runs next.
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!before.Contains(root))
                {
                    Object.DestroyImmediate(root);
                }
            }

            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------- fixtures

        private PlayerMotor BuildPlayer(RoleId role)
        {
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "TestFloor";
            _floor.transform.localScale = new Vector3(200f, 1f, 200f);
            _floor.transform.position = new Vector3(0f, -0.5f, 0f);
            _floor.AddComponent<FloorSurfaceTag>().FloorMaterial = FloorMaterial.Concrete;

            _body = new GameObject("TestPlayer");
            _body.transform.position = new Vector3(0f, 0.1f, 0f);

            var controller = _body.AddComponent<CharacterController>();
            controller.height = 1.75f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.875f, 0f);

            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(_body.transform, false);
            pivot.localPosition = new Vector3(0f, 1.63f, 0f);

            var camera = new GameObject("Cam").AddComponent<Camera>();
            camera.transform.SetParent(pivot, false);

            var beam = new GameObject("Beam").AddComponent<Light>();
            beam.transform.SetParent(_body.transform, false);
            beam.type = LightType.Spot;

            var look = _body.AddComponent<PlayerLook>();
            look.PitchPivot = pivot;

            _body.AddComponent<PlayerLoadout>();

            var motor = _body.AddComponent<PlayerMotor>();
            motor.Role = role;

            // Nothing is feeding input, so Update must not step; every test drives Step
            // itself with an explicit delta, which is also how the host will drive it (§13).
            motor.SteppedExternally = true;

            _body.AddComponent<PlayerCameraRig>();
            _body.AddComponent<PlayerFlashlight>();
            _body.AddComponent<PlayerFootsteps>();

            return motor;
        }

        /// <summary>
        /// Runs enough steps for the controller to settle onto the floor, so a measurement
        /// is of locomotion rather than of the spawn frame.
        /// </summary>
        private static void Settle(PlayerMotor motor)
        {
            for (var i = 0; i < 25; i++)
            {
                motor.Step(new MoveInput(0f, 0f, false), Step);
            }
        }

        /// <summary>Average horizontal speed over a whole number of fixed steps, m/s.</summary>
        private static float Travel(PlayerMotor motor, MoveInput input, float seconds)
        {
            // Counted rather than accumulated: adding 0.02f fifty times does not reach 1.0f,
            // and an off-by-one step is a 2% error in a measurement that is checked to 1%.
            var steps = Mathf.RoundToInt(seconds / Step);
            var start = motor.transform.position;
            for (var i = 0; i < steps; i++)
            {
                motor.Step(input, Step);
            }

            var end = motor.transform.position;
            return new Vector2(end.x - start.x, end.z - start.z).magnitude / (steps * Step);
        }

        private static AnimationClip MakeClip(string clipName, float length)
        {
            var clip = new AnimationClip();
            clip.name = clipName;
            clip.legacy = false;
            clip.SetCurve("Pivot", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Linear(0f, 1.63f, length, 1.63f));
            clip.wrapMode = WrapMode.Loop;
            return clip;
        }

        private static void SetClip(PlayerAnimatorDriver driver, string field, AnimationClip clip)
        {
            SetPrivate(driver, field, clip);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var info = target.GetType().GetField(
                field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, "field " + field + " not found");
            info!.SetValue(target, value);
        }
    }
}
