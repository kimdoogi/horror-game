#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections;
using System.Text;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Interaction
{
    /// <summary>
    /// The two things the owner found by playing: §08's dropped 전리품 hanging in the air,
    /// and §08's shop opening without being asked for. Both are checked the way they were
    /// found — a player standing in the world, a crosshair ray, and a real key event.
    /// <para>
    /// <b>Why not in EditMode.</b> <c>SoloMatchLoopTests</c> and
    /// <c>DropPlacementTests</c> both cover parts of this and both call the methods
    /// directly. That leaves the player's own path untested: the crosshair finding the
    /// piece, the key being read, the panel taking the mouse. This project has already
    /// shipped a build where every rule was right and none of that worked, with 575 green
    /// tests behind it — <c>InteractionPickupTests</c> exists for exactly that reason and
    /// this file is its other half.
    /// </para>
    /// <para>
    /// It lives in the predefined assembly because <c>MatchDirector</c> and
    /// <c>PlayerInteractor</c> do, and an <c>.asmdef</c> cannot reference a predefined
    /// assembly; the whole file compiles out of a player build on
    /// <c>UNITY_INCLUDE_TESTS</c>.
    /// </para>
    /// </summary>
    public sealed class InteractionDropTests
    {
        /// <summary>§13's seed for the layout under test — <c>SoloPlaytest.PlaytestSeed</c>.</summary>
        private const int Seed = 20260731;

        /// <summary>The scene <c>SoloPlaytest.BuildScene</c> writes, and Build Settings carries.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        private Keyboard? _keyboard;
        private bool _keyboardIsOurs;
        private InputSettings.BackgroundBehavior _backgroundBehaviour;
        private InputSettings.EditorInputBehaviorInPlayMode _editorBehaviour;

        /// <summary>
        /// Gives the run a keyboard that actually delivers. A batch editor has no input
        /// devices, is never the focused application, and resets device state every
        /// update unless told otherwise — all three turn a real key press into nothing at
        /// all, which is an excellent imitation of the defect being tested for.
        /// </summary>
        [SetUp]
        public void AddAKeyboardIfTheRunnerHasNone()
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

            // Loading the solo scene re-emits Mirror's packaging complaint about a folder
            // Unity itself calls immutable. The Test Framework fails a test on any
            // unexpected LogError; suppressing it is safe only because every step below
            // is asserted explicitly. Do not copy this into a test that proves something
            // by the absence of errors.
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>
        /// Puts the world back. The scene unload is not housekeeping: this loads a whole
        /// five-storey building into the active scene, and leaving it there has already
        /// made unrelated audio-occlusion tests fail against walls that should not exist.
        /// </summary>
        [UnityTearDown]
        public IEnumerator PutTheWorldBack()
        {
            InputSystem.settings.backgroundBehavior = _backgroundBehaviour;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorBehaviour;

            if (_keyboardIsOurs && _keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
            }

            _keyboard = null;
            _keyboardIsOurs = false;

            var solo = SceneManager.GetSceneByName(SoloScene);
            if (solo.IsValid() && solo.isLoaded)
            {
                var empty = SceneManager.CreateScene("InteractionDropTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(solo);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// §08's 대형 전리품, picked up and put down with the key. It has to reach a
        /// surface — "떨어진다" — it must not be inside the geometry when it gets there,
        /// and the crosshair has to be able to find it again, because a piece that cannot
        /// be picked back up is a piece that was thrown away.
        /// </summary>
        [UnityTest]
        public IEnumerator Dropping_a_piece_with_the_key_puts_it_on_the_floor_and_leaves_it_pickable()
        {
            yield return LoadMatch();

            var director = Object.FindFirstObjectByType<MatchDirector>();
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            Assert.That(director, Is.Not.Null, "the solo scene has no MatchDirector");
            Assert.That(motor, Is.Not.Null, "the solo scene has no player rig");

            var interactor = motor!.GetComponentInChildren<PlayerInteractor>();
            var camera = motor.GetComponentInChildren<Camera>();
            Assert.That(interactor, Is.Not.Null, "the player rig has no PlayerInteractor");
            Assert.That(camera, Is.Not.Null, "the player rig has no camera to cast the crosshair from");

            var piece = Object.FindFirstObjectByType<OversizeLootInteractable>();
            Assert.That(piece, Is.Not.Null, "§08's 대형 전리품 is not in this layout, so there is nothing to drop");

            var standing = Approach(piece!, out var survey);
            Assert.That(standing, Is.Not.Null,
                "§08's 궤짝 could not be approached with a clear line of sight.\n" + survey);

            Park(motor, camera!, standing!.Value, AimPoint(piece!));
            yield return null;

            Assert.That(interactor!.Focus, Is.SameAs(piece),
                "the crosshair did not find §08's 궤짝 to pick it up.\n" + Diagnose(camera!, piece!, interactor));

            yield return PressInteract();

            Assert.That(piece!.Carry, Is.Not.Null, "the 궤짝 has no SharedLootCarry behind it");
            Assert.That(piece.Carry!.CarrierCount, Is.EqualTo(1),
                "pressing the key at §08's 궤짝 did not take it up (" + piece.Refusal + ")");

            var heldAt = piece.transform.position;
            var feet = motor.transform.position;
            Assert.That(heldAt.y - feet.y, Is.GreaterThan(0.5f),
                "the 궤짝 is not being held near the hands, so this run proves nothing about dropping it");

            // Look at nothing in particular. The carried piece is the fallback focus
            // precisely so §03's "다시 누르면 내려놓는다" still has something to act on.
            Assert.That(interactor.Focus, Is.SameAs(piece),
                "the key came unbound from the piece in the hands, so it can never be put down");

            yield return PressInteract();

            Assert.That(piece.Carry!.CarrierCount, Is.Zero,
                "pressing the key again did not put §08's 궤짝 down (" + piece.Refusal + ")");

            // ---- it fell -------------------------------------------------
            var restingAt = piece.transform.position;
            Assert.That(restingAt.y, Is.LessThan(heldAt.y - 0.5f),
                "§08's 궤짝 stayed at the height it was released from — held " + heldAt.ToString("F2")
                + ", resting " + restingAt.ToString("F2") + ". This is the piece hanging in the air.");

            var grounded = Physics.Raycast(restingAt + (Vector3.up * ProbeRiseMetres), Vector3.down,
                out var surface, ProbeRiseMetres + RestingToleranceMetres, ~0, QueryTriggerInteraction.Ignore);
            Assert.That(grounded, Is.True,
                "nothing solid is under §08's dropped 궤짝 at " + restingAt.ToString("F2"));
            Assert.That(restingAt.y - surface.point.y, Is.LessThan(RestingToleranceMetres),
                "§08's dropped 궤짝 floats " + (restingAt.y - surface.point.y).ToString("F3")
                + " m above the surface under it");

            // ---- it is not inside a wall ---------------------------------
            var box = piece.GetComponent<Collider>();
            Assert.That(box, Is.Not.Null, "the dropped 궤짝 has no collider, so it cannot be found again");

            var bounds = box!.bounds;
            var extents = bounds.extents - (Vector3.one * WallCheckShrinkMetres);
            if (extents.x > 0f && extents.y > 0f && extents.z > 0f)
            {
                var buried = Physics.OverlapBox(
                    bounds.center, extents, piece.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                Assert.That(buried.Length, Is.Zero,
                    "§08's dropped 궤짝 is inside " + (buried.Length > 0 ? buried[0].name : "geometry")
                    + " — a piece a player cannot see or reach");
            }

            // ---- it is still pickable ------------------------------------
            // The point of putting it down upright rather than letting it settle on its
            // side: the fitted trigger box is grown upward from the model's base, and a
            // piece lying over takes that volume out from under the crosshair.
            //
            // A frame first, then an explicit sync. A collider that was switched off for
            // the carry, switched back on and moved in the same instant still reports its
            // old world bounds until the physics scene is told; in play FixedUpdate does
            // that before the Update that casts the crosshair, and here nothing would.
            // Aiming at the stale box missed the real one by three centimetres, which is
            // a test that fails on its own harness rather than on the game.
            yield return null;
            Physics.SyncTransforms();

            var backAt = Approach(piece, out var again);
            Assert.That(backAt, Is.Not.Null,
                "§08's dropped 궤짝 cannot be walked up to at all — it was put somewhere unreachable.\n" + again);

            Park(motor, camera!, backAt!.Value, AimPoint(piece));
            yield return null;

            Assert.That(interactor.Focus, Is.SameAs(piece),
                "the crosshair cannot find §08's 궤짝 where it was dropped, so it can never be picked up again.\n"
                + Diagnose(camera!, piece, interactor));
        }

        /// <summary>
        /// §01's 귀환. Arriving at the van sells the 전리품 and it does not put a screen
        /// up: the shop is the one panel operated with a cursor, and taking the mouse on a
        /// position test is what the owner reported as 갑자기 상점이 열림. The key at the
        /// 차량 is what opens it, and the prompt has to say so before it is pressed.
        /// </summary>
        [UnityTest]
        public IEnumerator The_shop_stays_shut_on_arrival_and_opens_on_the_key_at_the_vehicle()
        {
            yield return LoadMatch();

            var director = Object.FindFirstObjectByType<MatchDirector>();
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            Assert.That(director, Is.Not.Null, "the solo scene has no MatchDirector");
            Assert.That(motor, Is.Not.Null, "the solo scene has no player rig");

            var interactor = motor!.GetComponentInChildren<PlayerInteractor>();
            var camera = motor.GetComponentInChildren<Camera>();
            var input = motor.GetComponentInChildren<PlayerInputRouter>();
            var map = director!.Map;
            Assert.That(interactor, Is.Not.Null, "the player rig has no PlayerInteractor");
            Assert.That(map, Is.Not.Null, "the match has no map");

            var vehicle = Object.FindFirstObjectByType<SurfaceVehicleInteractable>();
            Assert.That(vehicle, Is.Not.Null, "§08's 지상 차량 was never placed");

            // Go down, then come back — the transition is the thing under test, and it
            // only fires on the edge.
            var below = Underground(map!);
            Assert.That(below, Is.Not.Null, "every candidate site is inside the 출입구 apron; the map is unusable");

            Warp(motor, below!.Value);
            yield return WaitForFixedSteps();
            Assert.That(director.LocalPlayerOnSurface, Is.False, "walking away from the 출입구 did not descend");

            Warp(motor, map!.Entrance);
            yield return WaitForFixedSteps();
            Assert.That(director.LocalPlayerOnSurface, Is.True, "walking into the apron did not surface");

            // The defect, in one line.
            Assert.That(director.ShopOpen, Is.False,
                "§08's shop opened by itself the moment the player reached the apron. §01 makes surfacing a "
                + "deliberate act and this panel takes the mouse; it must wait to be asked for.");
            Assert.That(input == null || input.LockCursor, Is.True,
                "arriving at the van released the cursor, which is the mouse being taken away mid-play");

            // Walk up to it and read the prompt. §03 forbids a HUD marker, so this line is
            // the only place a player can learn which key opens §08's shop.
            var standing = Approach(vehicle!, out var survey);
            Assert.That(standing, Is.Not.Null,
                "the 차량 could not be walked up to and looked at, so the shop cannot be opened at all.\n" + survey);

            Park(motor, camera!, standing!.Value, AimPoint(vehicle!));
            yield return null;

            Assert.That(interactor!.Focus, Is.SameAs(vehicle),
                "the crosshair does not find the 차량 from the apron.\n" + Diagnose(camera!, vehicle!, interactor));

            var prompt = interactor.Prompt;
            Assert.That(prompt, Is.Not.Null, "the interactor built no prompt screen");
            Assert.That(prompt!.IsVisible, Is.True, "the prompt is not drawn while the 차량 is in the crosshair");
            Assert.That(prompt.CurrentAction, Does.Contain(PlayerInteractor.InteractKeyLabel),
                "the prompt never names the key that opens §08's shop. Action line was: '"
                + prompt.CurrentAction + "'");

            yield return PressInteract();

            Assert.That(director.ShopOpen, Is.True,
                "pressing the key at the 차량 did not open §08's shop (" + vehicle!.Refusal + ")");
            Assert.That(input == null || !input.LockCursor, Is.True,
                "§08's shop is open and the cursor is still locked, so nothing on it can be clicked");

            yield return PressInteract();

            Assert.That(director.ShopOpen, Is.False, "the same key did not close §08's shop again");
            Assert.That(input == null || input.LockCursor, Is.True,
                "closing §08's shop did not give §05's camera the mouse back");
        }

        // ------------------------------------------------------------------
        // Harness.
        // ------------------------------------------------------------------

        private static IEnumerator LoadMatch()
        {
            SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var director = Object.FindFirstObjectByType<MatchDirector>();
            if (director != null && director.Map == null)
            {
                Assert.That(director.BeginMatch(Seed), Is.True, "BeginMatch refused");
            }

            yield return null;
        }

        /// <summary>
        /// Enough frames for <c>FixedUpdate</c> to have run at least once, so §01's phase
        /// transition has had its chance. Yielding <c>WaitForFixedUpdate</c> alone can
        /// return inside the same physics step the warp happened in.
        /// </summary>
        private static IEnumerator WaitForFixedSteps()
        {
            for (var i = 0; i < FixedStepsWaited; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            yield return null;
        }

        /// <summary>Moves the rig without the controller undoing it on its next step.</summary>
        private static void Warp(PlayerMotor motor, Vector3 target)
        {
            var controller = motor.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            motor.transform.position = target;

            if (controller != null)
            {
                controller.enabled = true;
            }

            Physics.SyncTransforms();
        }

        /// <summary>
        /// Somewhere outside §01's apron, as far from the monster's start as the layout
        /// allows: §06's monster would legitimately walk over and end a test that stands
        /// still, which would be correct behaviour and a useless run.
        /// </summary>
        private static Vector3? Underground(MatchMap map)
        {
            Vector3? best = null;
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

        /// <summary>
        /// Where a player would stand to use a thing: on a floor, inside its own reach,
        /// with nothing between the eye and it.
        /// <para>
        /// Rings of bearings rather than one fixed offset, because the two props this file
        /// walks up to are 1.28 m and 6.69 m long and both are placed against §12's
        /// geometry. A single stand-off would fail on the layout rather than on the defect
        /// it is watching — and the inner rings matter for the 차량 specifically: its
        /// trigger box is the size of the van, and a ray that starts <em>inside</em> a
        /// collider does not hit it, so standing too close is the same as not being there.
        /// </para>
        /// </summary>
        private static Vector3? Approach(Interactable prop, out string survey)
        {
            var log = new StringBuilder();
            var mark = AimPoint(prop);
            var collider = prop.GetComponentInChildren<Collider>();
            var floorY = prop.transform.position.y;

            log.AppendLine("  " + prop.name + " at " + prop.transform.position.ToString("0.00")
                + " aim " + mark.ToString("0.00")
                + " reach " + prop.ReachMetres.ToString("0.00")
                + " collider=" + (collider == null ? "NONE"
                    : collider.GetType().Name + " trigger=" + collider.isTrigger
                      + " size=" + collider.bounds.size.ToString("0.00")));

            for (var ring = 0; ring < ApproachRings; ring++)
            {
                var radius = ApproachNearestMetres + (ring * ApproachRingStepMetres);

                for (var i = 0; i < ApproachBearings; i++)
                {
                    var angle = i * (Mathf.PI * 2f / ApproachBearings);
                    var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    var foot = new Vector3(mark.x + offset.x, floorY, mark.z + offset.z);
                    var eye = foot + (Vector3.up * EyeHeightMetres);

                    // Every interactable's collider is a trigger, so ignoring triggers
                    // leaves exactly the solid world: any hit is a wall in the way.
                    var blocked = Physics.Linecast(eye, mark, out var wall, ~0, QueryTriggerInteraction.Ignore);
                    var grounded = Physics.Raycast(foot + (Vector3.up * 0.5f), Vector3.down, out _,
                        1.5f, ~0, QueryTriggerInteraction.Ignore);

                    // Inside the trigger box is the same as nowhere: the crosshair ray
                    // starts within the collider and never reports it.
                    var inside = collider != null && collider.bounds.Contains(eye);
                    var reachable = collider == null
                        || Vector3.Distance(eye, collider.ClosestPointOnBounds(eye)) < prop.ReachMetres;

                    log.AppendLine("      r=" + radius.ToString("0.0") + " " + (i * (360 / ApproachBearings))
                        + "°  blocked=" + (blocked ? wall.collider.name : "no")
                        + "  floor=" + grounded + "  insideBox=" + inside + "  inReach=" + reachable);

                    if (blocked || !grounded || inside || !reachable)
                    {
                        continue;
                    }

                    survey = log.ToString();
                    return foot;
                }
            }

            survey = log.ToString();
            return null;
        }

        /// <summary>
        /// Where the crosshair should be pointed: the middle of the trigger box, not the
        /// origin. Every prop's pivot is on its floor plane, so aiming at the origin aims
        /// at the floor it is standing on.
        /// </summary>
        private static Vector3 AimPoint(Interactable prop)
        {
            var collider = prop.GetComponentInChildren<Collider>();
            return collider != null ? collider.bounds.center : prop.transform.position;
        }

        /// <summary>
        /// Stands the rig on a spot and aims it the way §05 aims — yaw on the body, pitch
        /// on the head pivot. §05's own components are switched off rather than fought:
        /// <c>PlayerViewMotion</c> and <c>PlayerCameraRig</c> rewrite the camera every
        /// <c>LateUpdate</c>, so a rotation written straight onto it does not survive a
        /// frame.
        /// </summary>
        private static void Park(PlayerMotor motor, Camera camera, Vector3 standing, Vector3 mark)
        {
            Disable(motor.GetComponentInChildren<PlayerLook>());
            Disable(motor.GetComponentInChildren<PlayerCameraRig>());
            Disable(motor.GetComponentInChildren<PlayerViewMotion>());
            motor.enabled = false;

            var controller = motor.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            motor.transform.position = standing;

            var pivot = camera.transform.parent != null ? camera.transform.parent : camera.transform;
            var toMark = mark - camera.transform.position;

            var flat = new Vector3(toMark.x, 0f, toMark.z);
            motor.transform.rotation = flat.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(flat, Vector3.up)
                : motor.transform.rotation;

            var pitch = -Mathf.Atan2(toMark.y, flat.magnitude) * Mathf.Rad2Deg;
            pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            camera.transform.localRotation = Quaternion.identity;

            Physics.SyncTransforms();
        }

        private static void Disable(MonoBehaviour? component)
        {
            if (component != null)
            {
                component.enabled = false;
            }
        }

        /// <summary>
        /// Sends one real key-down/key-up pair through the Input System. Queued and then
        /// yielded, with no manual <c>InputSystem.Update()</c> between: the manual update
        /// consumes the event in the same instant, so the press is already a frame old by
        /// the time any <c>Update</c> reads it and <c>wasPressedThisFrame</c> is false.
        /// </summary>
        private IEnumerator PressInteract()
        {
            var keyboard = _keyboard!;
            var seen = false;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(PlayerInteractor.InteractKey));
            for (var frame = 0; frame < HeldFrames; frame++)
            {
                yield return null;
                var control = Keyboard.current != null ? Keyboard.current[PlayerInteractor.InteractKey] : null;
                seen |= control != null && control.isPressed;
            }

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;

            Assert.That(seen, Is.True,
                "the Input System never delivered the key to Keyboard.current, so this run proves nothing "
                + "about the game");
        }

        /// <summary>Everything the crosshair path depends on, for a failure that names its cause.</summary>
        private static string Diagnose(Camera camera, Interactable target, PlayerInteractor interactor)
        {
            var eye = camera.transform;
            var log = new StringBuilder();

            log.AppendLine("  eye        " + eye.position.ToString("0.00") + " forward " + eye.forward.ToString("0.00"));
            log.AppendLine("  target     " + target.name + " at " + target.transform.position.ToString("0.00")
                + "  reach " + target.ReachMetres.ToString("0.00"));

            var collider = target.GetComponentInChildren<Collider>();
            log.AppendLine("  collider   " + (collider == null ? "NONE — the crosshair cannot hit it"
                : collider.GetType().Name + " trigger=" + collider.isTrigger + " enabled=" + collider.enabled
                  + " bounds=" + collider.bounds.ToString()));

            var hits = Physics.RaycastAll(eye.position, eye.forward, 8f, ~0, QueryTriggerInteraction.Collide);
            log.AppendLine("  ray hits   " + hits.Length);
            for (var i = 0; i < hits.Length; i++)
            {
                var owner = hits[i].collider.GetComponentInParent<Interactable>();
                log.AppendLine("      " + hits[i].distance.ToString("0.000") + "  " + hits[i].collider.name
                    + "  trigger=" + hits[i].collider.isTrigger
                    + "  interactable=" + (owner == null ? "-" : owner.GetType().Name));
            }

            log.AppendLine("  focus      " + (interactor.Focus == null ? "NULL" : interactor.Focus.name));
            return log.ToString();
        }

        /// <summary>Frames the key is held. More than one so a one-frame scheduling slip is visible.</summary>
        private const int HeldFrames = 3;

        /// <summary>Physics steps waited on after a warp, so §01's phase edge is certain to have run.</summary>
        private const int FixedStepsWaited = 3;

        /// <summary>The harness rig's eye above its feet, metres. Used only to aim the approach probe.</summary>
        private const float EyeHeightMetres = 1.63f;

        /// <summary>Closest ring the approach probe tries, metres.</summary>
        private const float ApproachNearestMetres = 1.2f;

        /// <summary>How much further out each ring is, metres.</summary>
        private const float ApproachRingStepMetres = 0.6f;

        /// <summary>Rings tried. The outermost has to clear a 6.69 m 차량's own trigger box.</summary>
        private const int ApproachRings = 8;

        /// <summary>Bearings tried per ring. §12 puts things in corners and against walls.</summary>
        private const int ApproachBearings = 8;

        /// <summary>How far above a dropped piece the floor probe starts, metres.</summary>
        private const float ProbeRiseMetres = 0.1f;

        /// <summary>
        /// How far a dropped piece may sit above the surface under it, metres. Generous
        /// against <c>Interactable.FloorClearanceMetres</c>: this asks "on the floor or in
        /// the air", and the exact clearance has its own EditMode test.
        /// </summary>
        private const float RestingToleranceMetres = 0.1f;

        /// <summary>
        /// How much the buried-in-a-wall check shrinks the piece's own box, metres. The
        /// box is fitted to the model and the piece rests a centimetre or two off the
        /// floor, so an unshrunk overlap test reports the floor it is standing on.
        /// </summary>
        private const float WallCheckShrinkMetres = 0.06f;
    }
}
#endif
