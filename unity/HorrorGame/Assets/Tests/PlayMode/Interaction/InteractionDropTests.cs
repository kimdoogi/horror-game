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
    /// A thing the owner found by playing: a dropped 전리품 hanging in the air. It is
    /// checked the way it was found — a player standing in the world, a crosshair ray, and
    /// a real key event.
    /// <para>
    /// This file used to hold a second test, over §08's shop opening without being asked
    /// for. §01's race deleted §08 and with it the 차량 the shop lived on, so that test was
    /// removed rather than silenced; the reasoning is written out where it stood, below the
    /// remaining test.
    /// </para>
    /// <para>
    /// <b>Why this test did not go the same way, decided 2026-08-03.</b> The shop test was
    /// deleted because the thing it drove is unreachable — <c>MatchDirector.PlaceVehicle</c>
    /// is on the co-operative branch and the only scene with a <c>MatchDirector</c> ships
    /// <c>_raceMode: 1</c>. The 궤짝 is the opposite case, and it was checked rather than
    /// assumed: <c>PlaceLoot</c> is called <em>inside</em> the race branch of
    /// <c>BeginMatch</c> (<c>MatchDirector.cs:421</c>), so §08's loot is spawned into every
    /// race, the 궤짝 is in the shipped solo scene, and this test picks it up and puts it
    /// down through the real crosshair and the real key. It is live code, it is reachable
    /// by a player, and its drop placement is broken — deleting the test would leave all
    /// three true and unwatched.
    /// </para>
    /// <para>
    /// <b>The feature's own future, stated because the test's is downstream of it.</b>
    /// <c>docs/DESCENT-PIVOT.md</c> §3 lists 「§08 전리품 · 크레딧 · 판매」 under 버린다
    /// (통화가 없다) and §7 step 7 is 「상점/전리품/단서 제거」 — a step that has not run.
    /// Until it does, a runner in a race about speed can pick up a 1.28 m 궤짝 that blocks
    /// their view and takes their sprint, for no reward that exists any more. The right
    /// end state is that step 7 removes the <c>PlaceLoot</c> call from the race branch and
    /// this test dies with it. What must <em>not</em> happen is this test being deleted
    /// first: <c>DropPlacement</c> and the crosshair-and-key path outlive §08 either way —
    /// F-006 §5c puts battery cells on the floor for runners to find — so the coverage has
    /// to be re-pointed at whatever the runner actually carries, not dropped.
    /// </para>
    /// <para>
    /// <b>Why not in EditMode.</b> <c>SoloMatchLoopTests</c> and
    /// <c>DropPlacementTests</c> both cover parts of this and both call the methods
    /// directly. That leaves the player's own path untested: the crosshair finding the
    /// piece and the key being read. This project has already shipped a build where every
    /// rule was right and none of that worked, with 575 green tests behind it —
    /// <c>InteractionPickupTests</c> exists for exactly that reason and this file is its
    /// other half.
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
            // The probe is the piece's OWN oriented box, not its world AABB. That
            // distinction was wrong here and it mattered: the check used to take
            // Collider.bounds — an axis-aligned box — and hand its extents to an
            // OverlapBox rotated by the piece's yaw, which is two different boxes
            // mixed. On the 1.276 × 0.694 m 궤짝 at 45° the AABB is 0.697 m
            // half-extent on both axes, so the rotated probe measured 1.27 × 1.27 m —
            // 0.29 m of phantom crate on each side of the short axis, in a corridor
            // §12 gives 2.2 m of clear width. That is enough to report a wall that is
            // not touching the piece, which is a test that fails on its own harness.
            var box = piece.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null,
                "the dropped 궤짝 has no BoxCollider, so the crosshair cannot find it again "
                + "(Interactable.FitTrigger fits one to every prop)");

            var size = Vector3.Scale(box!.size, box.transform.lossyScale);
            var centre = box.transform.TransformPoint(box.center);
            var pose = box.transform.rotation;
            var half = (size * 0.5f) - (Vector3.one * WallCheckShrinkMetres);

            if (half.x > 0f && half.y > 0f && half.z > 0f)
            {
                var buried = Physics.OverlapBox(centre, half, pose, ~0, QueryTriggerInteraction.Ignore);
                Assert.That(buried.Length, Is.Zero,
                    "§08's dropped 궤짝 is inside the geometry — a piece a player cannot see or reach.\n"
                    + Burial(box!, centre, size, pose, buried));
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

        // ------------------------------------------------------------------
        // The_shop_stays_shut_on_arrival_and_opens_on_the_key_at_the_vehicle was
        // DELETED here on 2026-08-03, with §08 itself.
        //
        // It asserted that arriving at the 차량 did not open the shop and that the key
        // there did, and it failed with "§08's 지상 차량 was never placed". That is true
        // and correct: the van is placed by MatchDirector.PlaceVehicle, which is on the
        // co-operative branch of BeginMatch, and Map_FirstSketch_Solo.unity — the only
        // scene in the project with a MatchDirector — serialises _raceMode: 1. §01's race
        // has no 지상, no 차량 and no economy to shop in; DESCENT-PIVOT deleted §08.
        //
        // Coverage lost, and whether anything still needs it:
        //   · The van, the shop panel and the surfacing edge that used to open it. Nothing
        //     needs them — all three are unreachable on every scene the game ships.
        //   · The cursor rule the test doubled as a guard for ("a panel that takes the
        //     mouse must be asked for") is NOT lost: MatchPause is the only screen left
        //     that takes the cursor, it sets PlayerInputRouter.LockCursor itself
        //     (Scripts/UI/Shell/MatchPause.cs:121), and it is opened by a key by
        //     construction — there is no position test left in the game that could open a
        //     panel by itself, which was the whole defect.
        //   · The crosshair-and-key path over a large prop is still covered, on the same
        //     rig and in the same file, by the 궤짝 test above.
        //
        // The harness this test owned alone — Warp, WaitForFixedSteps, Underground and
        // FixedStepsWaited — went with it; every one of them existed to cross §01's
        // surface boundary, and MatchMap.HasSurface is false on a tower.
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
        /// Where a player would stand to use a thing: on a floor, inside its own reach,
        /// with nothing between the eye and it.
        /// <para>
        /// Rings of bearings rather than one fixed offset, because the piece this file walks
        /// up to is 1.28 m long, is dropped wherever the player was standing, and is placed
        /// against §12's geometry. A single stand-off would fail on the layout rather than
        /// on the defect it is watching. The inner rings still matter: a ray that starts
        /// <em>inside</em> a trigger collider does not hit it, so standing too close is the
        /// same as not being there.
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

        /// <summary>
        /// How badly the piece is buried, in a form that decides the next step rather than
        /// just naming a collider.
        /// <para>
        /// <b>Why a shrink walk and not <c>ComputePenetration</c>.</b> The obvious call
        /// returns a depth and a push direction, and it does not work here: it refuses any
        /// pair involving a non-convex <c>MeshCollider</c>, and every kit piece is exactly
        /// that — the FBXes import with <c>addColliders: 1</c> and one merged shell per
        /// module. <c>OverlapBox</c> is the only query that answers against a triangle
        /// mesh, so the depth is measured by asking it repeatedly with a smaller box and
        /// reporting the first size that comes clear.
        /// </para>
        /// <para>
        /// <b>What the number means.</b> A piece that comes clear at a few centimetres is a
        /// tolerance argument — this test's own <see cref="WallCheckShrinkMetres"/> is
        /// grazing kit geometry and the placement is essentially right. A piece that needs
        /// tens of centimetres is through a wall, and the cause is upstream:
        /// <c>DropPlacement.ClearOfGeometry</c> sweeps a sphere of
        /// <c>Interactable.MinimumTargetMetres * 0.5</c> = 0.15 m — the crosshair's aim
        /// tolerance — and stops the piece's <em>centre</em> that far from whatever it
        /// finds. The 궤짝 is 1.276 × 0.694 m in plan, so it needs 0.35 m of room broadside
        /// and 0.64 m end-on. Nothing in the drop path has ever asked for it.
        /// </para>
        /// </summary>
        private static string Burial(
            BoxCollider piece, Vector3 centre, Vector3 size, Quaternion pose, Collider[] hits)
        {
            var log = new StringBuilder();
            log.AppendLine("  piece    at " + piece.transform.position.ToString("0.000")
                + "  yaw " + piece.transform.eulerAngles.y.ToString("0.0") + "°"
                + "  box " + size.ToString("0.000") + " m");

            for (var i = 0; i < hits.Length; i++)
            {
                log.AppendLine("  inside   " + hits[i].name);
            }

            var smallest = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f;
            for (var shrink = WallCheckShrinkMetres; shrink < smallest; shrink += BurialProbeStepMetres)
            {
                var half = (size * 0.5f) - (Vector3.one * shrink);
                if (half.x <= 0f || half.y <= 0f || half.z <= 0f)
                {
                    break;
                }

                if (Physics.OverlapBox(centre, half, pose, ~0, QueryTriggerInteraction.Ignore).Length == 0)
                {
                    log.AppendLine("  clear at " + shrink.ToString("0.00")
                        + " m off every side. Under ~0.1 m this is a tolerance argument; above it "
                        + "the piece is through the wall and DropPlacement's 0.15 m sweep is the "
                        + "cause — it never asks whether the piece itself fits.");
                    return log.ToString();
                }
            }

            log.AppendLine("  never comes clear, however far the probe is shrunk — the piece's own "
                + "centre is inside the geometry, not just its corners.");
            return log.ToString();
        }

        /// <summary>Step of the burial-depth walk, metres. A centimetre: finer than the fix would be judged to.</summary>
        private const float BurialProbeStepMetres = 0.01f;

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

        /// <summary>The harness rig's eye above its feet, metres. Used only to aim the approach probe.</summary>
        private const float EyeHeightMetres = 1.63f;

        /// <summary>Closest ring the approach probe tries, metres.</summary>
        private const float ApproachNearestMetres = 1.2f;

        /// <summary>How much further out each ring is, metres.</summary>
        private const float ApproachRingStepMetres = 0.6f;

        /// <summary>
        /// Rings tried. Eight is inherited from when this harness also had to clear a
        /// 6.69 m §08 van's trigger box; it is kept because the 궤짝 is dropped in whatever
        /// dead end the runner was in, and §12's dead ends are the tightest cells on the map.
        /// </summary>
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
