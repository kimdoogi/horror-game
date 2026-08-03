#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections;
using System.Linq;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace HorrorGame.Tests.PlayMode.Interaction
{
    /// <summary>
    /// The interact key, end to end, on the only thing left in the race that answers it:
    /// §12-B's 문. A runner standing in the shipped building, the real crosshair ray, a
    /// real key event through the Input System, and a door that comes shut.
    /// <para>
    /// <b>Where this came from.</b> This is <c>InteractionPickupTests</c> re-pointed, not
    /// a new test. That file drove the same path at a piece of §08 전리품 and
    /// <c>InteractionDropTests</c> drove its other half; DESCENT-PIVOT §7 step 7 deleted
    /// the economy on 2026-08-03 and took <c>LootPropInteractable</c>,
    /// <c>OversizeLootInteractable</c> and <c>Inventory</c> with it. The QUESTION those
    /// tests asked did not go with the answer they used to check:
    /// </para>
    /// <para>
    /// <i>Does pressing the key at the thing in the crosshair change the world?</i>
    /// </para>
    /// <para>
    /// That question is exactly as load-bearing in a race as it was in the co-op game, and
    /// arguably more so — §12-B makes the 문 the whole of a runner's interaction with the
    /// building and with the nineteen people behind them, and <c>PlayerInteractor.Probe</c>
    /// now says so in as many words ("the door is the only interactable"). So the crosshair
    /// ray, the reach gate, the prompt and the key read are re-tested against a door, and
    /// the weight assertion that used to end the pick-up is replaced by the thing a shut
    /// door actually does: a collider that blocks the corridor.
    /// </para>
    /// <para>
    /// <b>Why not in EditMode.</b> <c>DoorState</c> has core tests and they call
    /// <c>Shut(dt)</c> directly, because a raycast needs a rendered frame. That leaves the
    /// player's own path untested — the crosshair finding the door and the key being read —
    /// and this project has already shipped a build where every rule was right, none of
    /// that worked, and 575 tests were green.
    /// </para>
    /// <para>
    /// It lives in the predefined assembly because <c>MatchDirector</c> and
    /// <c>PlayerInteractor</c> do, and an <c>.asmdef</c> cannot reference a predefined
    /// assembly. That is why <c>playModeTestRunnerEnabled</c> is on in
    /// <c>ProjectSettings.asset</c>; the whole file compiles out of a player build on
    /// <c>UNITY_INCLUDE_TESTS</c>.
    /// </para>
    /// </summary>
    public sealed class InteractionKeyPathTests
    {
        /// <summary>
        /// §13's seed for the layout under test. The same one
        /// <c>SoloPlaytest.PlaytestSeed</c> uses — quoted rather than referenced, because
        /// that class is editor-only and this test runs in a player loop.
        /// </summary>
        private const int Seed = 20260731;

        /// <summary>The scene <c>SoloPlaytest.BuildScene</c> writes, and Build Settings carries.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>How far the test stands from the leaf, in metres. Re-checked against the door's own reach.</summary>
        private const float StandOffMetres = 1.2f;

        /// <summary>Where the rig's camera sits above its feet; used only to aim the reachability probe.</summary>
        private const float EyeHeightMetres = 1.63f;

        /// <summary>Frames the key is held per <see cref="PressInteract"/>. More than one so a scheduling slip is visible.</summary>
        private const int HeldFrames = 3;

        /// <summary>Characters of approach survey worth carrying into a failure message.</summary>
        private const int SurveyBudget = 2000;

        private Keyboard? _keyboard;
        private bool _keyboardIsOurs;
        private InputSettings.BackgroundBehavior _backgroundBehaviour;
        private InputSettings.EditorInputBehaviorInPlayMode _editorBehaviour;
        private string _pressReport = string.Empty;

        [SetUp]
        public void AddAKeyboardIfTheRunnerHasNone()
        {
            // A batch-mode editor has no input devices at all, and a test that silently did
            // nothing because Keyboard.current was null would be the exact defect it is here
            // to catch. So the device is explicit.
            //
            // A batch-mode editor is also never the focused application, and the Input
            // System's default background behaviour disables every non-background device
            // while focus is elsewhere. A disabled Keyboard still exists, still answers
            // Keyboard.current, and has its state reset to zero on every update — so a
            // queued key press lands and is wiped before any MonoBehaviour can read it.
            // That is a property of the test host, not of the game. Restored in teardown.
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
            // unexpected LogError; suppressing it is safe here only because every step below
            // is asserted explicitly. Do not copy this into a test that proves something by
            // the absence of errors.
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>
        /// Puts everything back — including the map.
        /// <para>
        /// <b>The scene unload is not housekeeping.</b> This loads a whole eight-storey
        /// building into the active scene, and a suite that ran an audio occlusion test
        /// afterwards found a wall between the monster and the ears in a scene that was
        /// supposed to be empty. Three <c>AudioSceneTests</c> failed that way before the
        /// unload existed, and none of them were about audio.
        /// </para>
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
                var empty = SceneManager.CreateScene("InteractionKeyPathTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(solo);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// The whole interaction the race has: look at a door, hold the key, and have the
        /// corridor behind you close.
        /// <para>
        /// Fails if the crosshair ray misses, if the prompt never names the key, if the key
        /// is not read, if the hold does not accumulate, or if the door reports itself shut
        /// without the collider that makes 「부서진 문은 다시 닫히지 않는다」 mean anything to
        /// the person running up behind.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Holding_the_interact_key_at_a_door_shuts_it_and_blocks_the_corridor()
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

            var doors = Object.FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None);
            Assert.That(doors.Length, Is.GreaterThan(0),
                "no DoorInteractable in the loaded match. §12 puts one or two lockable doors on every "
                + "storey's bottleneck and MatchDirector.AttachDoors is what turns the generator's "
                + "Door_*/Hinge group into one — a race with no door has no interaction at all.");

            // A door is not survey-ready in the frame the component was added: Bind() runs
            // Apply(), which switches the blocking collider and the NavMesh obstacle, and
            // Unity defers collider state to the end of the frame.
            yield return null;
            Physics.SyncTransforms();

            var found = ChooseApproachableDoor(doors, out var standing, out var survey);
            Assert.That(found, Is.Not.Null,
                "no door in this layout could be approached on foot with a clear line of sight.\n" + survey);

            // Widened to a non-nullable local before anything captures it: the closure handed
            // to HoldInteract below would otherwise be dereferencing a nullable the compiler
            // cannot follow into a lambda.
            var door = found!;

            Park(motor, camera!, standing, AimPoint(door));

            // One frame for the interactor's Update to run against the moved rig.
            yield return null;

            Assert.That(interactor!.Focus, Is.SameAs(door),
                "the crosshair ray did not find the door it is pointing at.\n" + Diagnose(camera!, door, interactor));

            var prompt = interactor.Prompt;
            Assert.That(prompt, Is.Not.Null, "the interactor built no prompt screen");
            Assert.That(prompt!.IsVisible, Is.True, "the prompt is not on screen while a door is in the crosshair");
            Assert.That(prompt.CurrentTitle, Does.Contain("문"),
                "the prompt does not name the door. Title was: '" + prompt.CurrentTitle + "'");
            Assert.That(prompt.CurrentAction, Does.Contain(PlayerInteractor.InteractKeyLabel),
                "the prompt never names the key, which is what 'there is no key to shut it' feels like. "
                + "Action line was: '" + prompt.CurrentAction + "'");

            // On screen, not merely "visible". A canvas whose rectangle sits outside the
            // viewport reports itself active and draws nothing a player can read, which is
            // the same experience as no prompt at all.
            AssertDrawn(prompt);

            Assert.That(door.State.Phase, Is.EqualTo(DoorPhase.Open),
                "the generator leaves every 문 open, and this test is about shutting one");
            Assert.That(door.NeedsHold, Is.True,
                "§12-B prices shutting a door at " + GameConstants.DoorShutSeconds.ToString("0.0")
                + "초 of standing still. A door that could be tapped shut would hand the leader a free wall.");

            var before = Diagnose(camera!, door, interactor);
            var found2 = Blocker(door);
            Assert.That(found2, Is.Not.Null, "the door's Hinge child carries no collider to block the corridor with");

            var blocker = found2!;
            Assert.That(blocker.enabled, Is.False, "an open door must not be blocking anybody");

            // The key, held, through the Input System, as real device events — long enough
            // for §12-B's 1.1 s and then some, because a batch frame is not a fixed step.
            yield return HoldInteract(GameConstants.DoorShutSeconds * 3f, () => door.State.Phase != DoorPhase.Open);

            if (door.State.Phase != DoorPhase.Shut)
            {
                Assert.Fail("holding the interact key at an open 문 did not shut it. Phase is "
                    + door.State.Phase + ", hold progress " + door.State.ShutProgress01.ToString("0.00")
                    + ".\n" + _pressReport + before);
            }

            Assert.That(door.State.Blocks, Is.True);

            // One frame for DoorInteractable.Apply's collider switch to reach the physics scene.
            yield return null;
            Physics.SyncTransforms();

            Assert.That(blocker.enabled, Is.True,
                "the rule says shut and the corridor is still open. §12-B's whole value in a race is that "
                + "the person behind you has to spend "
                + GameConstants.DoorBreakSeconds.ToString("0.0") + "초 or go round — a DoorState that "
                + "flipped without the collider following would be a door nobody can be stopped by.");
        }

        /// <summary>
        /// The same rig with the crosshair on nothing: no prompt, and the key does not act
        /// on something the player is not looking at.
        /// </summary>
        [UnityTest]
        public IEnumerator The_key_does_nothing_when_the_crosshair_is_on_nothing()
        {
            yield return LoadMatch();

            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            Assert.That(motor, Is.Not.Null, "the solo scene has no player rig");

            var interactor = motor!.GetComponentInChildren<PlayerInteractor>();
            var camera = motor.GetComponentInChildren<Camera>();
            Assert.That(interactor, Is.Not.Null, "the player rig has no PlayerInteractor");

            var doors = Object.FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None);
            var phasesBefore = doors.Select(d => d.State.Phase).ToArray();

            // Straight up. §12's ceilings carry nothing interactive.
            camera!.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            yield return null;

            Assert.That(interactor!.Focus, Is.Null, "the crosshair found something in the ceiling");
            Assert.That(interactor.Prompt != null && interactor.Prompt.IsVisible, Is.False,
                "the prompt is drawn with nothing in the crosshair");

            yield return HoldInteract(GameConstants.DoorShutSeconds * 2f, () => false);

            for (var i = 0; i < doors.Length; i++)
            {
                Assert.That(doors[i].State.Phase, Is.EqualTo(phasesBefore[i]),
                    "the key moved a door the player was not looking at (" + doors[i].name + ")");
            }
        }

        // ------------------------------------------------------------------- fixtures

        /// <summary>
        /// Loads the shipped race scene, starts a match if the scene did not, and takes the
        /// creature out of the frame.
        /// <para>
        /// <b>The creature is switched off deliberately.</b> This test parks a stationary
        /// player at an arbitrary door for over a second of game time, and §06's creature
        /// covers 4.8 m in that second. A run in which it happened to be on the same storey
        /// would fail on a kill rather than on the key, which is a flake and not a finding.
        /// Being caught has its own suite — <c>MonsterKillTests</c> and
        /// <c>MonsterChaseTests</c> — and neither of them is switched off here.
        /// </para>
        /// </summary>
        private IEnumerator LoadMatch()
        {
            SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var director = Object.FindFirstObjectByType<MatchDirector>();
            Assert.That(director, Is.Not.Null, "the solo scene has no MatchDirector");

            // The scene begins its own match on Start; if it did not, begin one.
            if (director!.Map == null)
            {
                Assert.That(director.BeginMatch(Seed), Is.True, "BeginMatch refused");
            }

            yield return null;

            foreach (var agent in Object.FindObjectsByType<MonsterAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                agent.enabled = false;
            }
        }

        /// <summary>
        /// Picks a door the test can actually stand at, and where to stand.
        /// <para>
        /// Eight bearings at <see cref="StandOffMetres"/>; first one with floor underfoot
        /// and an unobstructed line from eye to leaf wins. A door sits in a corridor, so at
        /// most two of the eight are ever clear, and a test that always stood due east would
        /// fail on the geometry rather than on the defect it is watching.
        /// </para>
        /// </summary>
        private static DoorInteractable? ChooseApproachableDoor(
            DoorInteractable[] doors, out Vector3 standing, out string survey)
        {
            standing = Vector3.zero;

            var log = new StringBuilder();
            var ordered = doors.OrderBy(d => d.GetInstanceID()).ToArray();

            foreach (var door in ordered)
            {
                var mark = AimPoint(door);
                var reported = log.Length < SurveyBudget;

                if (reported)
                {
                    log.AppendLine("  " + door.name + " at " + door.transform.position.ToString("0.00")
                        + " aim " + mark.ToString("0.00")
                        + " phase " + door.State.Phase
                        + " reach " + door.ReachMetres.ToString("0.00"));
                }

                for (var i = 0; i < 8; i++)
                {
                    var angle = i * (Mathf.PI * 0.25f);
                    var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * StandOffMetres;
                    var foot = new Vector3(mark.x + offset.x, door.transform.position.y, mark.z + offset.z);
                    var eye = foot + (Vector3.up * EyeHeightMetres);

                    if (Vector3.Distance(eye, mark) > door.ReachMetres)
                    {
                        // Standing further away than the door's own reach would fail the
                        // gate inside Probe and prove nothing about the crosshair.
                        continue;
                    }

                    // Every interactable's grab volume is a trigger, so ignoring triggers
                    // leaves exactly the solid world: any hit at all is a wall between the
                    // eye and the leaf.
                    var blocked = Physics.Linecast(eye, mark, out var wall, ~0, QueryTriggerInteraction.Ignore);

                    // Floor under the feet, so the test is not standing in a void.
                    var grounded = Physics.Raycast(foot + (Vector3.up * 0.5f), Vector3.down, out var floor,
                        1.5f, ~0, QueryTriggerInteraction.Ignore);

                    if (reported)
                    {
                        log.AppendLine("      bearing " + (i * 45) + "°  blocked="
                            + (blocked ? wall.collider.name : "no")
                            + "  floor=" + (grounded ? floor.collider.name : "NONE"));
                    }

                    if (blocked || !grounded)
                    {
                        continue;
                    }

                    standing = foot;
                    survey = log.ToString();
                    return door;
                }
            }

            survey = log.ToString();
            return null;
        }

        /// <summary>
        /// Where the crosshair should be pointed: the middle of the door's grab volume, not
        /// its origin. The group's pivot is on the floor plane at one jamb, so aiming at the
        /// origin aims at the floor beside the doorway.
        /// </summary>
        private static Vector3 AimPoint(Interactable door)
        {
            var trigger = Trigger(door);
            return trigger != null ? trigger.bounds.center : door.transform.position;
        }

        /// <summary>The grab volume the crosshair can hit — the one collider that is a trigger.</summary>
        private static Collider? Trigger(Interactable door)
        {
            return door.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => c.isTrigger);
        }

        /// <summary>
        /// The collider that stops a body when the door is shut.
        /// <para>
        /// Found on the <c>Hinge</c> child by name rather than as "the first non-trigger
        /// collider", because that is exactly how <c>MatchDirector.AttachDoors</c> binds it
        /// (<c>door.Bind(hinge, hinge.GetComponent&lt;Collider&gt;(), …)</c>) and it is not
        /// the only non-trigger collider under a door: <c>MapSceneBuilder</c> gives every
        /// renderer a <c>MeshCollider</c>, so the swinging leaf has one too and it is
        /// enabled whatever the door is doing. Matching on the shape rather than the name
        /// would pick up the leaf on any hierarchy reorder and quietly test nothing.
        /// </para>
        /// </summary>
        private static Collider? Blocker(Interactable door)
        {
            var hinge = door.transform.Find("Hinge");
            return hinge != null ? hinge.GetComponent<Collider>() : null;
        }

        /// <summary>
        /// Puts the rig on the spot and aims it, the way §05 aims: yaw on the body, pitch on
        /// the head pivot.
        /// <para>
        /// Writing the camera's own rotation does not survive a frame —
        /// <c>PlayerViewMotion</c> and <c>PlayerCameraRig</c> both own it and rewrite it from
        /// the pivot every <c>LateUpdate</c>, which is exactly what a first attempt at this
        /// test discovered the hard way (the eye stayed on world forward and the crosshair
        /// found nothing). §05's own components are switched off here rather than fought,
        /// because their behaviour has its own PlayMode suite; what this test is about starts
        /// at the eye transform.
        /// </para>
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
        /// Holds the interact key down for up to <paramref name="seconds"/> of game time,
        /// releasing early once <paramref name="done"/> is true.
        /// <para>
        /// Queued and then yielded, with no manual <c>InputSystem.Update()</c> in between.
        /// A manual update consumes the event in the same instant, so by the time any
        /// <c>MonoBehaviour.Update</c> runs the press is already a frame old and
        /// <c>wasPressedThisFrame</c> is false — the key is delivered and nothing sees it,
        /// which is a very good imitation of the defect this test is here to catch. Yielding
        /// instead lets the Input System's own early-update deliver it in the frame the game
        /// reads it.
        /// </para>
        /// <para>
        /// Bounded by <c>Time.time</c> rather than by a frame count, on purpose: a batch-mode
        /// run is uncapped and a thousand frames there can be a tenth of a second, which is
        /// less than §12-B's hold and would fail on the harness rather than on the game.
        /// </para>
        /// </summary>
        private IEnumerator HoldInteract(float seconds, System.Func<bool> done)
        {
            var keyboard = _keyboard!;
            var log = new StringBuilder();
            var seen = false;

            log.AppendLine("  device " + keyboard.deviceId + " added=" + keyboard.added
                + " enabled=" + keyboard.enabled
                + " isCurrent=" + ReferenceEquals(keyboard, Keyboard.current)
                + " updateMode=" + InputSystem.settings.updateMode);

            var deadline = Time.time + seconds;
            var frames = 0;

            while (Time.time < deadline && !done())
            {
                // Re-queued every frame. One state event marks the key down until something
                // says otherwise, but re-sending is harmless and survives an editor update
                // mode that resets device state between frames.
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(PlayerInteractor.InteractKey));
                yield return null;

                var control = Keyboard.current != null ? Keyboard.current[PlayerInteractor.InteractKey] : null;
                seen |= control != null && control.isPressed;

                if (frames < HeldFrames)
                {
                    log.AppendLine("  frame +" + frames
                        + "  current=" + (Keyboard.current == null ? "NULL" : Keyboard.current.deviceId.ToString())
                        + "  isPressed=" + (control != null && control.isPressed)
                        + "  wasPressedThisFrame=" + (control != null && control.wasPressedThisFrame));
                }

                frames++;
            }

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;

            log.AppendLine("  held " + frames + " frames over " + seconds.ToString("0.00") + " s of game time");
            _pressReport = log.ToString();

            Assert.That(seen, Is.True,
                "the Input System never delivered the key to Keyboard.current, so this run proves "
                + "nothing about the game:\n" + _pressReport);
        }

        /// <summary>
        /// Asserts the prompt is actually being drawn — canvas up, every line enabled with a
        /// face and an opaque colour.
        /// <para>
        /// <b>Deliberately not a pixel measurement.</b> A batch-mode runner has no display,
        /// so Unity never runs the canvas update that turns an overlay canvas into screen
        /// pixels or lets <c>CanvasScaler</c> apply its factor: the canvas rect stays the raw
        /// 640×480 game view and the prompt's world corners stay in the player rig's rotated
        /// frame. Two earlier versions of this check asserted on those numbers and failed on
        /// the harness rather than on the game, which is worse than not checking. What
        /// survives is what the defect actually looked like — a prompt that exists and is
        /// never put on screen.
        /// </para>
        /// </summary>
        private static void AssertDrawn(InteractionPromptScreen prompt)
        {
            var canvas = prompt.GetComponentInChildren<Canvas>(true);
            Assert.That(canvas, Is.Not.Null, "the prompt built no canvas");
            Assert.That(canvas!.isActiveAndEnabled, Is.True, "the prompt's canvas is off");

            var texts = canvas.GetComponentsInChildren<Text>(true)
                .Where(t => !string.IsNullOrEmpty(t.text))
                .ToArray();
            Assert.That(texts.Length, Is.GreaterThan(0), "the prompt canvas carries no text");

            foreach (var text in texts)
            {
                Assert.That(text.isActiveAndEnabled, Is.True,
                    "the prompt line '" + text.name + "' carries text and is not being drawn");
                Assert.That(text.font, Is.Not.Null,
                    "the prompt line '" + text.name + "' has no face, so it draws nothing");
                Assert.That(text.color.a, Is.GreaterThan(0.01f),
                    "the prompt line '" + text.name + "' is fully transparent");
            }
        }

        /// <summary>Everything the key path depends on, for a failure message that names the cause.</summary>
        private static string Diagnose(Camera camera, DoorInteractable target, PlayerInteractor interactor)
        {
            var eye = camera.transform;
            var mark = AimPoint(target);
            var log = new StringBuilder();

            log.AppendLine("  eye            " + eye.position.ToString("0.00") + " forward " + eye.forward.ToString("0.00"));
            log.AppendLine("  door           " + target.transform.position.ToString("0.00") + "  " + target.name
                + "  layer " + LayerMask.LayerToName(target.gameObject.layer)
                + "  phase " + target.State.Phase);
            log.AppendLine("  eye → leaf     " + Vector3.Distance(eye.position, mark).ToString("0.00")
                + " m, reach " + target.ReachMetres.ToString("0.00") + " m");

            var trigger = Trigger(target);
            log.AppendLine("  grab volume    " + (trigger == null ? "NONE — the crosshair cannot hit it"
                : trigger.GetType().Name + " enabled=" + trigger.enabled
                  + " bounds=" + trigger.bounds.size.ToString("0.00")));

            var blocker = Blocker(target);
            log.AppendLine("  blocker        " + (blocker == null ? "NONE"
                : blocker.GetType().Name + " enabled=" + blocker.enabled));

            var hits = Physics.RaycastAll(eye.position, eye.forward, target.ReachMetres,
                ~0, QueryTriggerInteraction.Collide);
            log.AppendLine("  crosshair hits " + hits.Length);
            foreach (var hit in hits.OrderBy(h => h.distance))
            {
                var owner = hit.collider.GetComponentInParent<Interactable>();
                log.AppendLine("      " + hit.distance.ToString("0.000") + "  " + hit.collider.name
                    + "  trigger=" + hit.collider.isTrigger
                    + "  interactable=" + (owner == null ? "-" : owner.GetType().Name));
            }

            log.AppendLine("  focus          " + (interactor.Focus == null ? "NULL" : interactor.Focus.name));
            log.AppendLine("  Keyboard       " + (Keyboard.current == null ? "NULL" : Keyboard.current.name)
                + "  E pressed=" + (Keyboard.current != null && Keyboard.current[PlayerInteractor.InteractKey].isPressed));
            return log.ToString();
        }
    }
}
#endif
