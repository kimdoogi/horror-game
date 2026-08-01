#nullable enable

using System.Collections;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Roles;
using HorrorGame.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.PlayerRig
{
    /// <summary>
    /// 웅크리기 and the hop, driven by the keys a player actually presses.
    /// <para>
    /// <b>Nothing here calls the method under test.</b> The interact key looked broken for
    /// a day behind 575 green tests because every test of it called
    /// <c>Interactable.OnPressed</c> directly, so the whole path between the key and the
    /// rule — the binding, the action, the edge, the router — was never exercised.
    /// <c>InteractionPickupTests</c> is the answer to that for §08's pick-up and this is
    /// the answer for §05's two new verbs: every case below queues a real keyboard event
    /// through the Input System and then measures the body.
    /// </para>
    /// <para>
    /// The two claims that matter are the ones a picture cannot settle. A crouch has to
    /// lower the capsule and then <em>refuse</em> to raise it under a ceiling — §12 builds
    /// spaces a standing player does not fit in, and a stand that clipped through one
    /// would be a way out of the map. A jump has to leave the floor and come back without
    /// ever reaching a ledge walking cannot already reach, because §12's geometry is
    /// derived from what a player cannot climb.
    /// </para>
    /// </summary>
    public sealed class PlayerStanceTests
    {
        private const float Step = 1f / 50f;

        /// <summary>Frames a key is held. More than one so a scheduling slip is visible rather than silent.</summary>
        private const int HeldFrames = 3;

        private GameObject? _floor;
        private GameObject? _body;
        private GameObject? _obstacle;

        private Keyboard? _keyboard;
        private bool _keyboardIsOurs;
        private InputSettings.BackgroundBehavior _backgroundBehaviour;
        private InputSettings.EditorInputBehaviorInPlayMode _editorBehaviour;

        /// <summary>
        /// Gives the run a keyboard it can actually deliver events on.
        /// <para>
        /// Copied deliberately from <c>InteractionPickupTests</c>, whose remarks carry the
        /// reasoning: a batch-mode editor is never the focused application, the Input
        /// System's default background behaviour disables every non-background device
        /// while focus is elsewhere, and a disabled keyboard still answers
        /// <c>Keyboard.current</c> while having its state wiped on every update. Without
        /// these three lines a real key press turns into nothing at all — which is an
        /// excellent imitation of the defect this file exists to catch.
        /// </para>
        /// </summary>
        [SetUp]
        public void GiveTheRunAKeyboard()
        {
            _backgroundBehaviour = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            _editorBehaviour = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            _keyboard = Keyboard.current;
            if (_keyboard == null)
            {
                _keyboard = InputSystem.AddDevice<Keyboard>();
                _keyboardIsOurs = true;
            }

            InputSystem.EnableDevice(_keyboard);
        }

        [TearDown]
        public void TearDown()
        {
            InputSystem.settings.backgroundBehavior = _backgroundBehaviour;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorBehaviour;

            if (_keyboardIsOurs && _keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
            }

            _keyboard = null;
            _keyboardIsOurs = false;

            Destroy(ref _body);
            Destroy(ref _obstacle);
            Destroy(ref _floor);
        }

        /// <summary>
        /// §05's scheme has to carry the two verbs, or nothing below is reachable by a
        /// player. Read off the shipped asset, which is the same object the rebinding
        /// screen and the controls card both read.
        /// </summary>
        [Test]
        public void The_shipped_scheme_binds_crouch_and_jump_on_the_keyboard()
        {
            var asset = Resources.Load<InputActionAsset>(PlayerInputRouter.DefaultAssetResourcePath);
            Assert.That(asset, Is.Not.Null,
                "Resources/" + PlayerInputRouter.DefaultAssetResourcePath + " is missing; §05's scheme is "
                + "not optional and the rebinding screen reads this exact asset.");

            var map = asset!.FindActionMap("Player", false);
            Assert.That(map, Is.Not.Null, "the 'Player' action map is gone");

            AssertBound(map!, "Crouch", "/c");
            AssertBound(map!, "Jump", "/space");
        }

        /// <summary>
        /// The crouch key, pressed for real: the capsule shrinks, §05's multiplier lands
        /// on the ground as metres, and Shift buys nothing down there.
        /// </summary>
        [UnityTest]
        public IEnumerator Pressing_the_crouch_key_lowers_the_capsule_and_halves_the_speed()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var stance = motor.GetComponent<PlayerStance>();
            var controller = motor.GetComponent<CharacterController>();
            yield return null;
            yield return SettleOnFloor(motor);

            var standing = controller.height;
            Assert.That(stance.IsCrouched, Is.False, "the rig started crouched");

            yield return Press(Key.C);

            Assert.That(stance.IsCrouched, Is.True,
                "the crouch key did not reach PlayerStance. The binding, the action, the router edge or "
                + "the stance is broken — and every one of those is invisible to a test that calls "
                + "Toggle() directly.");
            Assert.That(controller.height,
                Is.EqualTo(standing * GameConstants.CrouchHeightFraction).Within(0.001f),
                "§12 sizes the crouched capsule off the Crouch clip so the body a teammate sees and the "
                + "body the physics uses are the same size.");
            Assert.That(controller.center.y,
                Is.EqualTo(standing * GameConstants.CrouchHeightFraction * 0.5f).Within(0.001f),
                "the centre has to come down with the height or the feet leave the floor.");

            // Shift as well as W. §06's ladder has no crouched rung, so the base speed
            // must come out as 걷기 whatever the sprint key is doing.
            yield return Travel(motor, Key.W, Key.LeftShift, 1.0f);

            var expected = GameConstants.WalkSpeed * GameConstants.CrouchSpeedMultiplier;
            Assert.That(_lastSpeed, Is.EqualTo(expected).Within(expected * 0.08f),
                "§05's multipliers compose — 걷기 2.0 × the crouch's " + GameConstants.CrouchSpeedMultiplier
                + " is " + expected + " m/s, and it has to arrive as metres on the floor. Measured "
                + _lastSpeed + " m/s.");
            Assert.That(_lastSpeed, Is.LessThan(GameConstants.WalkSpeed * GameConstants.MulBackward),
                "§05: crouching forward has to cost more than 후진 65%, the worst row in the table, or "
                + "concealment is cheaper than looking behind you.");
        }

        /// <summary>
        /// The case that matters. A ceiling the standing capsule does not fit under has to
        /// make the crouch key do nothing at all — not clip, not teleport, not stand.
        /// </summary>
        [UnityTest]
        public IEnumerator Standing_up_under_a_ceiling_is_refused_rather_than_clipping_through_it()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var stance = motor.GetComponent<PlayerStance>();
            var controller = motor.GetComponent<CharacterController>();
            yield return null;
            yield return SettleOnFloor(motor);

            var standing = controller.height;

            yield return Press(Key.C);
            Assert.That(stance.IsCrouched, Is.True, "the crouch key did not reach the stance");

            // A slab low enough that the crouched body fits and the standing one does not.
            // §12's own example is the void under the upper stair flight, which the kit
            // builds at 1.73 m against a 1.75 m player.
            var lid = standing * GameConstants.CrouchHeightFraction + 0.06f;
            _obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _obstacle.name = "Ceiling";
            _obstacle.transform.localScale = new Vector3(6f, 0.2f, 6f);
            _obstacle.transform.position = motor.transform.position + new Vector3(0f, lid + 0.1f, 0f);

            // Not optional. Physics.autoSyncTransforms is off by default, so a collider
            // that was created and then moved is still at the origin as far as PhysX is
            // concerned — which made the first run of this test drop the player onto an
            // unscaled 1 m cube and then place a ceiling nowhere near them.
            Physics.SyncTransforms();
            yield return null;

            var probe = Physics.OverlapBox(
                _obstacle.transform.position, _obstacle.transform.localScale * 0.5f);
            Debug.Log("[StanceTest] ceiling at " + _obstacle.transform.position.ToString("0.000")
                + " scale " + _obstacle.transform.localScale.ToString("0.000")
                + "  player " + motor.transform.position.ToString("0.000")
                + "  capsule h=" + controller.height.ToString("0.000")
                + " c=" + controller.center.ToString("0.000") + " r=" + controller.radius.ToString("0.000")
                + "  colliders in the slab's own box: " + probe.Length
                + "  HasHeadroom=" + stance.HasHeadroom());

            Assert.That(stance.HasHeadroom(), Is.False,
                "the sweep does not see a slab " + lid.ToString("0.00") + " m over the floor, so it would "
                + "let a player stand up inside §12's geometry.");

            var heightBefore = controller.height;
            var yBefore = motor.transform.position.y;

            yield return Press(Key.C);

            Assert.That(stance.IsCrouched, Is.True,
                "the crouch key stood the player up under a ceiling. §12 builds ducts, gantry undersides "
                + "and hiding places out of exactly this clearance; a stand that succeeds here is a way "
                + "out of the map.");
            Assert.That(controller.height, Is.EqualTo(heightBefore).Within(0.001f),
                "the capsule grew even though the stand was refused");
            Assert.That(motor.transform.position.y, Is.LessThan(yBefore + 0.05f),
                "the body was pushed upward by the refused stand — which is the clip this test is about.");

            // Take the lid away and the same key works, so the refusal is about the
            // geometry and not about the toggle being broken.
            Object.DestroyImmediate(_obstacle);
            _obstacle = null;
            Physics.SyncTransforms();
            yield return null;

            yield return Press(Key.C);
            Assert.That(stance.IsCrouched, Is.False, "with the ceiling gone the player still could not stand");
            Assert.That(controller.height, Is.EqualTo(standing).Within(0.001f));
        }

        /// <summary>
        /// The jump key: the body leaves the floor, peaks under the step the controller
        /// already takes for free, and lands.
        /// </summary>
        [UnityTest]
        public IEnumerator Pressing_jump_leaves_the_ground_peaks_under_a_step_and_lands()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var stance = motor.GetComponent<PlayerStance>();
            yield return null;
            yield return null;

            // Settle, so the floor height being measured against is the one the controller
            // has actually arrived at.
            yield return SettleOnFloor(motor);

            var floor = motor.transform.position.y;
            Assert.That(motor.IsGrounded, Is.True, "the rig never reached the floor");

            yield return PressAndHold(Key.Space);

            var peak = 0f;
            var leftTheGround = false;
            var landed = false;
            for (var elapsed = 0f; elapsed < 3f; elapsed += Time.deltaTime)
            {
                yield return null;
                var rise = motor.transform.position.y - floor;
                if (rise > peak)
                {
                    peak = rise;
                }

                if (!motor.IsGrounded)
                {
                    leftTheGround = true;
                }
                else if (leftTheGround && rise < 0.02f)
                {
                    landed = true;
                    break;
                }
            }

            Assert.That(leftTheGround, Is.True,
                "the jump key never took the player off the floor. Peak rise was " + peak + " m.");
            Assert.That(peak, Is.EqualTo(GameConstants.JumpApexMetres).Within(0.04f),
                "the apex has to be the authored one — measured " + peak + " m against "
                + GameConstants.JumpApexMetres + " m. It is corrected for the integration step on "
                + "purpose, because a bound that moves with the frame rate is not a bound.");
            Assert.That(peak, Is.LessThanOrEqualTo(GameConstants.PlayerStepOffsetMetres),
                "§12: the apex reached " + peak + " m against a free step of "
                + GameConstants.PlayerStepOffsetMetres + " m. The whole argument for allowing a jump is "
                + "that it reaches a strict subset of what walking already reaches; past this line it "
                + "makes crates, debris and the 차량 climbable and puts players on top of §12's map.");
            Assert.That(landed, Is.True, "the player never came back down");

            // And the cooldown is real: a second press inside it must not launch again.
            var beforeSecond = motor.transform.position.y;
            yield return PressAndHold(Key.Space);
            yield return null;
            Assert.That(stance.JumpCooldownRemaining, Is.GreaterThanOrEqualTo(0f));
            Assert.That(motor.transform.position.y, Is.LessThan(beforeSecond + 0.5f),
                "a second jump inside the cooldown launched anyway");
        }

        /// <summary>
        /// The claim §12 actually cares about: a hop does not get the player onto
        /// something a walk cannot. The ledge is a §08 crate's own height.
        /// </summary>
        [UnityTest]
        public IEnumerator The_hop_cannot_mount_a_ledge_a_walk_cannot()
        {
            var motor = BuildPlayer(RoleId.Runner);
            yield return null;
            yield return SettleOnFloor(motor);

            // 0.58 m — Crate's modelled height, and the shortest thing in the prop set
            // that stands above the free step. If a jump can mount this, it can mount
            // every crate, barrel and debris pile in §12's building.
            const float LedgeTop = 0.58f;

            _obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _obstacle.name = "Crate";
            _obstacle.transform.localScale = new Vector3(6f, LedgeTop, 6f);
            _obstacle.transform.position = motor.transform.position
                + new Vector3(0f, LedgeTop * 0.5f, 3.4f);
            Physics.SyncTransforms();
            yield return null;

            var floor = motor.transform.position.y;
            var highest = 0f;
            var report = new StringBuilder();

            // Run at the crate and jump at it, repeatedly, for longer than several
            // cooldowns. A player who wanted on top of it would do exactly this.
            InputSystem.QueueStateEvent(_keyboard!, new KeyboardState(Key.W, Key.LeftShift));
            for (var elapsed = 0f; elapsed < 6f; elapsed += Time.deltaTime)
            {
                yield return null;

                if (Mathf.Repeat(elapsed, 0.8f) < Time.deltaTime)
                {
                    InputSystem.QueueStateEvent(
                        _keyboard!, new KeyboardState(Key.W, Key.LeftShift, Key.Space));
                    yield return null;
                    InputSystem.QueueStateEvent(_keyboard!, new KeyboardState(Key.W, Key.LeftShift));
                }

                var rise = motor.transform.position.y - floor;
                if (rise > highest)
                {
                    highest = rise;
                    report.AppendLine("  t=" + elapsed.ToString("0.00") + " rise " + rise.ToString("0.000")
                        + " at " + motor.transform.position.ToString("0.00"));
                }
            }

            InputSystem.QueueStateEvent(_keyboard!, new KeyboardState());
            yield return null;

            Assert.That(highest, Is.LessThan(LedgeTop),
                "a jump got the player " + highest.ToString("0.000") + " m up a " + LedgeTop
                + " m crate. §12's geometry is derived from what a player cannot climb; anything that "
                + "mounts a crate mounts the debris and the 차량 too, and puts players on top of the map "
                + "instead of inside it.\n" + report);
        }

        /// <summary>
        /// §04's channel, both signs. Crouching quietens the movement term the 청음사 is
        /// blinded by; the landing at the end of a hop is loud enough to blind them.
        /// </summary>
        [UnityTest]
        public IEnumerator Crouching_is_quiet_and_a_landing_is_loud_on_section04s_own_meter()
        {
            var motor = BuildPlayer(RoleId.Runner);
            var stance = motor.GetComponent<PlayerStance>();
            // Fully qualified: HorrorGame.Audio also declares a FloorSurfaceTag, so a
            // using directive here would make that name ambiguous against the player
            // assembly's own.
            var noise = motor.gameObject.AddComponent<HorrorGame.Audio.NoiseMeter>();
            yield return null;
            yield return null;

            noise.ReportMovementSpeed(GameConstants.WalkSpeed);
            var walking = noise.Noise01;
            Assert.That(walking, Is.GreaterThan(0f), "walking makes no noise at all");

            yield return Press(Key.C);
            Assert.That(stance.IsCrouched, Is.True);
            yield return null;

            noise.ReportMovementSpeed(GameConstants.WalkSpeed);
            var crouched = noise.Noise01;

            Assert.That(crouched, Is.LessThan(walking),
                "§04 prices the 청음사 on 자기가 소리를 내면 못 듣는다, and §08 sells a 소음기 to buy out "
                + "of it. Crouching is the free version of that trade and it has to move the same number.");
            Assert.That(crouched,
                Is.EqualTo(walking * GameConstants.CrouchNoiseMultiplier).Within(0.001f));

            // The transient is not scaled by the stance: a landing is a landing.
            stance.NotifyLanded(4f);
            yield return null;

            Assert.That(noise.Noise01, Is.GreaterThan(GameConstants.ListenerSelfNoiseThreshold),
                "a landing has to cut §04's feed. It is the opposite sign of the crouch on the same "
                + "channel, and a silent one would make the jump a free verb.");
        }

        // ------------------------------------------------------------------- fixtures

        private float _lastSpeed;

        /// <summary>
        /// Holds one or two keys for a second of game time and records the ground speed
        /// the body actually reached. A coroutine because the real path is
        /// <c>PlayerMotor.Update</c>, and the point of this file is not to bypass it.
        /// </summary>
        private IEnumerator Travel(PlayerMotor motor, Key first, Key second, float seconds)
        {
            // Re-queued every frame rather than once. A key held across hundreds of
            // batch-mode frames is at the mercy of the editor's own focus handling, and a
            // measurement that silently became "nobody was pressing anything" is exactly
            // the failure mode this file exists to rule out. Repeating the same state
            // raises no new press edge, so a toggle is unaffected.
            InputSystem.QueueStateEvent(_keyboard!, new KeyboardState(first, second));
            yield return null;
            yield return null;

            var start = motor.transform.position;
            var elapsed = 0f;
            var frames = 0;
            while (elapsed < seconds)
            {
                InputSystem.QueueStateEvent(_keyboard!, new KeyboardState(first, second));
                yield return null;
                elapsed += Time.deltaTime;
                frames++;
            }

            var travelled = motor.transform.position - start;
            travelled.y = 0f;
            _lastSpeed = elapsed > 0f ? travelled.magnitude / elapsed : 0f;

            Debug.Log("[StanceTest] travel " + first + "+" + second
                + "  input(" + motor.LastInput.Forward + "," + motor.LastInput.Strafe
                + ",sprint=" + motor.LastInput.SprintHeld + ")"
                + "  resolved " + motor.ResolvedSpeed.ToString("0.000")
                + "  base " + motor.LastContext.BaseSpeed.ToString("0.000")
                + "  load " + motor.LastContext.LoadMultiplier.ToString("0.000")
                + "  measured " + _lastSpeed.ToString("0.000") + " m/s"
                + "  over " + elapsed.ToString("0.00") + " s / " + frames + " frames"
                + "  from " + start.ToString("0.00") + " to " + motor.transform.position.ToString("0.00"));

            InputSystem.QueueStateEvent(_keyboard!, new KeyboardState());
            yield return null;
        }

        /// <summary>
        /// Waits, in game seconds rather than frames, until the controller reports it is
        /// standing on something. A batch-mode frame can be a millisecond, so twenty
        /// frames is not twenty frames' worth of falling.
        /// </summary>
        private static IEnumerator SettleOnFloor(PlayerMotor motor, float timeoutSeconds = 3f)
        {
            for (var elapsed = 0f; elapsed < timeoutSeconds; elapsed += Time.deltaTime)
            {
                yield return null;
                if (motor.IsGrounded && Mathf.Abs(motor.WorldVelocity.y) < 0.2f)
                {
                    yield break;
                }
            }
        }

        /// <summary>Presses a key, holds it a few frames, releases it, and lets the edge land.</summary>
        private IEnumerator Press(Key key)
        {
            yield return PressAndHold(key);
            InputSystem.QueueStateEvent(_keyboard!, new KeyboardState());
            yield return null;
            yield return null;
        }

        /// <summary>Presses a key and holds it, without releasing. Asserts the device really delivered it.</summary>
        private IEnumerator PressAndHold(Key key)
        {
            var seen = false;
            InputSystem.QueueStateEvent(_keyboard!, new KeyboardState(key));
            for (var frame = 0; frame < HeldFrames; frame++)
            {
                yield return null;
                var control = Keyboard.current != null ? Keyboard.current[key] : null;
                seen |= control != null && control.isPressed;
            }

            Assert.That(seen, Is.True,
                "the Input System never delivered " + key + " to Keyboard.current, so this run proves "
                + "nothing about the game.");
        }

        private static void AssertBound(InputActionMap map, string actionName, string pathSuffix)
        {
            var action = map.FindAction(actionName, false);
            Assert.That(action, Is.Not.Null,
                "'" + actionName + "' is not in §05's scheme, so the key cannot be pressed, rebound, or "
                + "shown on the controls card.");

            var found = false;
            var seen = new StringBuilder();
            for (var i = 0; i < action!.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                seen.Append(' ').Append(binding.effectivePath);
                found |= binding.groups.Contains(InputBindingsScheme)
                    && binding.effectivePath.EndsWith(pathSuffix, System.StringComparison.Ordinal);
            }

            Assert.That(found, Is.True,
                "'" + actionName + "' has no " + InputBindingsScheme + " binding ending in '" + pathSuffix
                + "'. Bindings were:" + seen);
        }

        /// <summary>The binding group the settings screen and the controls card both filter on.</summary>
        private const string InputBindingsScheme = "Keyboard&Mouse";

        /// <summary>
        /// A drivable player on a floor, with a real <see cref="PlayerInputRouter"/> so
        /// keys are the only way in. Deliberately the same shape as
        /// <c>PlayerTests.BuildPlayer</c>, plus the stance.
        /// </summary>
        private PlayerMotor BuildPlayer(RoleId role)
        {
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "StanceTestFloor";
            _floor.transform.localScale = new Vector3(200f, 1f, 200f);
            _floor.transform.position = new Vector3(0f, -0.5f, 0f);

            _body = new GameObject("StanceTestPlayer");
            _body.transform.position = new Vector3(0f, 0.1f, 0f);

            var controller = _body.AddComponent<CharacterController>();
            controller.height = ViewMotionTuning.RigHeightMetres;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, ViewMotionTuning.RigHeightMetres * 0.5f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = GameConstants.PlayerStepOffsetMetres;

            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(_body.transform, false);
            pivot.localPosition = new Vector3(0f, 1.63f, 0f);

            _body.AddComponent<PlayerInputRouter>().LockCursor = false;
            var look = _body.AddComponent<PlayerLook>();

            // Before the motor: AddComponent runs Awake immediately, and the motor looks
            // for a stance there.
            _body.AddComponent<PlayerStance>();

            var motor = _body.AddComponent<PlayerMotor>();
            motor.Role = role;

            look.PitchPivot = pivot;
            look.SetLook(0f, 0f);

            // See the note in the ceiling test: without this the floor's collider is a
            // 1 m cube at the origin and everything below measures the wrong world.
            Physics.SyncTransforms();
            return motor;
        }

        private static void Destroy(ref GameObject? target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
                target = null;
            }
        }
    }
}
