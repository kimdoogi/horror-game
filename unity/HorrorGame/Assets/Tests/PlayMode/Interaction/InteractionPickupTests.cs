#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections;
using System.Linq;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Economy;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
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
    /// §08's pick-up, end to end: a player standing at a piece of 전리품, the real
    /// crosshair ray, a real key event through the Input System, and the weight §08
    /// charges for it landing in the inventory.
    /// <para>
    /// <b>Why this exists.</b> Every other test of the interaction layer calls
    /// <c>Interactable.OnPressed</c> directly — <c>SoloMatchLoopTests</c> and
    /// <c>MatchGuidanceTests</c> both say so in their own remarks, because a raycast
    /// needs a rendered frame. That left the whole path between the key and the rule
    /// untested: the crosshair ray, the reach gate, the prompt and the key read. The
    /// game shipped a build in which none of it worked and 575 tests stayed green.
    /// So this test deliberately touches nothing directly. It moves a player, waits
    /// for frames, and presses a key.
    /// </para>
    /// <para>
    /// It lives in the predefined assembly because <c>MatchDirector</c> and
    /// <c>PlayerInteractor</c> do, and an <c>.asmdef</c> cannot reference a predefined
    /// assembly. That is why <c>playModeTestRunnerEnabled</c> is on in
    /// <c>ProjectSettings.asset</c>; the whole file compiles out of a player build on
    /// <c>UNITY_INCLUDE_TESTS</c>.
    /// </para>
    /// </summary>
    public sealed class InteractionPickupTests
    {
        /// <summary>
        /// §13's seed for the layout under test. The same one
        /// <c>SoloPlaytest.PlaytestSeed</c> uses — quoted rather than referenced,
        /// because that class is editor-only and this test runs in a player loop.
        /// </summary>
        private const int Seed = 20260731;

        /// <summary>The scene <c>SoloPlaytest.BuildScene</c> writes, and Build Settings carries.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>How far the test stands from the piece. Inside §04's reach with room to spare.</summary>
        private const float StandOffMetres = 1.2f;

        private Keyboard? _keyboard;
        private bool _keyboardIsOurs;
        private InputSettings.BackgroundBehavior _backgroundBehaviour;
        private InputSettings.EditorInputBehaviorInPlayMode _editorBehaviour;

        [SetUp]
        public void AddAKeyboardIfTheRunnerHasNone()
        {
            // A batch-mode editor has no input devices at all, and a test that silently
            // did nothing because Keyboard.current was null would be the exact defect it
            // is here to catch. So the device is explicit.
            // A batch-mode editor is never the focused application, and the Input
            // System's default background behaviour disables every non-background device
            // while focus is elsewhere. A disabled Keyboard still exists, still answers
            // Keyboard.current, and has its state reset to zero on every update — so a
            // queued key press lands and is wiped before any MonoBehaviour can read it.
            // That is a property of the test host, not of the game, and without this it
            // silently turned a real key press into nothing at all. Restored in teardown.
            _backgroundBehaviour = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            // And the editor half of the same problem: by default a keyboard's state is
            // reset on every update unless the Game View has focus, which a batch-mode
            // run never has.
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

            // Building the solo scene reimports assets, which re-emits Mirror's
            // packaging complaint about an immutable folder it will not let anyone fix.
            // The Test Framework fails a test on any unexpected LogError; suppressing it
            // is safe here only because every step below is asserted explicitly.
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>
        /// Puts everything back — including the map.
        /// <para>
        /// <b>The scene unload is not housekeeping.</b> This test loads a whole
        /// five-storey building into the active scene, and a suite that ran an audio
        /// occlusion test afterwards found a wall between the monster and the ears in a
        /// scene that was supposed to be empty. Three <c>AudioSceneTests</c> failed that
        /// way before this existed, and none of them were about audio.
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
                var empty = SceneManager.CreateScene("InteractionPickupTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(solo);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// The whole §08 pick-up: look at a piece, press the key, hold the weight.
        /// <para>
        /// Fails if the crosshair ray misses, if the prompt never names the key, if the
        /// key is not read, or if the piece reaches the pocket without §08's weight.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Pressing_the_interact_key_at_a_loot_prop_banks_it_with_section_08_weight()
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

            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            Assert.That(motor, Is.Not.Null, "the solo scene has no player rig");

            var interactor = motor!.GetComponentInChildren<PlayerInteractor>();
            var loadout = motor.GetComponentInChildren<PlayerLoadout>();
            var camera = motor.GetComponentInChildren<Camera>();
            Assert.That(interactor, Is.Not.Null, "the player rig has no PlayerInteractor");
            Assert.That(loadout, Is.Not.Null, "the player rig has no PlayerLoadout");
            Assert.That(camera, Is.Not.Null, "the player rig has no camera to cast the crosshair from");

            var props = Object.FindObjectsByType<LootPropInteractable>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.That(props.Length, Is.GreaterThan(0), "§08 placed no pocketable 전리품 in this layout");

            // Stand the player at a piece the way a player would: on the floor, close
            // enough for §04's reach, with an unobstructed line to it.
            var target = ChooseReachablePiece(props, out var standing, out var survey);
            Assert.That(target, Is.Not.Null,
                "no piece of loot in this layout could be approached with a clear line of sight.\n" + survey);

            Park(motor, camera!, standing, AimPoint(target!));

            // One frame for the interactor's Update to run against the moved rig.
            yield return null;

            Assert.That(interactor!.Focus, Is.SameAs(target),
                "the crosshair ray did not find the piece it is pointing at.\n" + Diagnose(camera!, target!, interactor));

            var prompt = interactor.Prompt;
            Assert.That(prompt, Is.Not.Null, "the interactor built no prompt screen");
            Assert.That(prompt!.IsVisible, Is.True, "the prompt is not on screen while a piece is in the crosshair");
            Assert.That(prompt.CurrentTitle, Does.Contain(LootText.NameOf(target!.Loot)),
                "the prompt does not name the piece. Title was: '" + prompt.CurrentTitle + "'");
            Assert.That(prompt.CurrentAction, Does.Contain(PlayerInteractor.InteractKeyLabel),
                "the prompt never names the key, which is what 'there is no key to pick it up' feels like. "
                + "Action line was: '" + prompt.CurrentAction + "'");

            // On screen, not merely "visible". A canvas whose rectangle sits outside the
            // viewport reports itself active and draws nothing a player can read, which
            // is the same experience as no prompt at all.
            AssertDrawn(prompt);

            var pockets = loadout!.Inventory;
            var loot = target.Loot;
            var expected = LootCatalogue.WeightOf(loot);
            Assert.That(pockets.LootCount, Is.Zero, "the player started the test already carrying loot");

            // Everything the failure message needs, read before the piece is destroyed.
            var before = Diagnose(camera!, target, interactor);

            // The key, through the Input System, as a real device event.
            yield return PressInteract();

            if (pockets.LootCount != 1)
            {
                Assert.Fail("pressing the interact key at a piece of 전리품 did not pick it up.\n"
                    + _pressReport + before);
            }

            Assert.That(pockets.Loot[0], Is.EqualTo(loot), "a different §08 row landed in the pocket");
            Assert.That(pockets.TotalWeight, Is.EqualTo(expected),
                "§08 charges " + expected + " for " + LootText.NameOf(loot)
                + " and the inventory holds " + pockets.TotalWeight);
        }

        /// <summary>
        /// The same rig with the crosshair on nothing: no prompt, and the key does not
        /// bank a piece the player is not looking at.
        /// </summary>
        [UnityTest]
        public IEnumerator The_key_does_nothing_when_the_crosshair_is_on_nothing()
        {
            SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            Assert.That(motor, Is.Not.Null, "the solo scene has no player rig");

            var interactor = motor!.GetComponentInChildren<PlayerInteractor>();
            var loadout = motor.GetComponentInChildren<PlayerLoadout>();
            var camera = motor.GetComponentInChildren<Camera>();
            Assert.That(interactor, Is.Not.Null, "the player rig has no PlayerInteractor");

            // Straight up. §12's ceilings carry nothing interactive.
            camera!.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            yield return null;

            Assert.That(interactor!.Focus, Is.Null, "the crosshair found something in the ceiling");
            Assert.That(interactor.Prompt != null && interactor.Prompt.IsVisible, Is.False,
                "the prompt is drawn with nothing in the crosshair");

            yield return PressInteract();

            Assert.That(loadout!.Inventory.LootCount, Is.Zero, "the key banked loot the player was not looking at");
        }

        /// <summary>
        /// Sends one real key-down/key-up pair through the Input System.
        /// <para>
        /// Queued and then yielded, with no manual <c>InputSystem.Update()</c> in
        /// between. The manual update consumes the event in the same instant, so by the
        /// time any <c>MonoBehaviour.Update</c> runs the press is already a frame old and
        /// <c>wasPressedThisFrame</c> is false — the key is delivered and nothing sees
        /// it, which is a very good imitation of the defect this test is here to catch.
        /// Yielding instead lets the Input System's own early-update deliver it in the
        /// frame the game reads it.
        /// </para>
        /// </summary>
        private IEnumerator PressInteract()
        {
            var keyboard = _keyboard!;
            var log = new StringBuilder();

            var seen = false;
            log.AppendLine("  device " + keyboard.deviceId + " added=" + keyboard.added
                + " enabled=" + keyboard.enabled
                + " isCurrent=" + ReferenceEquals(keyboard, Keyboard.current)
                + " updateMode=" + InputSystem.settings.updateMode);

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(PlayerInteractor.InteractKey));
            for (var frame = 0; frame < HeldFrames; frame++)
            {
                yield return null;
                var control = Keyboard.current != null ? Keyboard.current[PlayerInteractor.InteractKey] : null;
                seen |= control != null && control.isPressed;
                log.AppendLine("  frame +" + frame
                    + "  current=" + (Keyboard.current == null ? "NULL" : Keyboard.current.deviceId.ToString())
                    + "  isPressed=" + (control != null && control.isPressed)
                    + "  wasPressedThisFrame=" + (control != null && control.wasPressedThisFrame));
            }

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;

            _pressReport = log.ToString();
            Assert.That(seen, Is.True,
                "the Input System never delivered the key to Keyboard.current, so this run proves "
                + "nothing about the game:\n" + _pressReport);
        }

        /// <summary>What the Input System reported on each frame of the last press.</summary>
        private string _pressReport = string.Empty;

        /// <summary>Frames the key is held. More than one so a one-frame scheduling slip is visible.</summary>
        private const int HeldFrames = 3;

        /// <summary>
        /// Picks a piece the test can actually stand at, and where to stand.
        /// <para>
        /// Eight bearings at <see cref="StandOffMetres"/>, first one with an
        /// unobstructed line from eye to piece wins. §12's layout puts plenty of loot
        /// in corners and against walls, and a test that always stood due east would
        /// fail on the geometry rather than on the defect it is watching.
        /// </para>
        /// </summary>
        private static LootPropInteractable? ChooseReachablePiece(
            LootPropInteractable[] props, out Vector3 standing, out string survey)
        {
            standing = Vector3.zero;

            var log = new StringBuilder();
            var ordered = props.OrderBy(p => p.GetInstanceID()).ToArray();

            foreach (var prop in ordered)
            {
                var mark = AimPoint(prop);
                var collider = prop.GetComponentInChildren<Collider>();
                var reported = log.Length < SurveyBudget;

                if (reported)
                {
                    log.AppendLine("  " + prop.name + " at " + prop.transform.position.ToString("0.00")
                        + " aim " + mark.ToString("0.00")
                        + " collider=" + (collider == null ? "NONE"
                            : collider.GetType().Name + " trigger=" + collider.isTrigger
                              + " size=" + collider.bounds.size.ToString("0.00")));
                }

                for (var i = 0; i < 8; i++)
                {
                    var angle = i * (Mathf.PI * 0.25f);
                    var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * StandOffMetres;
                    var foot = new Vector3(mark.x + offset.x, prop.transform.position.y, mark.z + offset.z);
                    var eye = foot + (Vector3.up * EyeHeightMetres);

                    // Every interactable's collider is a trigger, so ignoring triggers
                    // leaves exactly the solid world: any hit at all is a wall between
                    // the eye and the piece.
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
                    return prop;
                }
            }

            survey = log.ToString();
            return null;
        }

        /// <summary>Characters of approach survey worth carrying into a failure message.</summary>
        private const int SurveyBudget = 2000;

        /// <summary>
        /// Where the crosshair should be pointed: the middle of the piece's trigger box,
        /// not its origin. Every prop's pivot is on its floor plane, so aiming at the
        /// origin aims at the floor the piece is standing on.
        /// </summary>
        private static Vector3 AimPoint(Interactable prop)
        {
            var collider = prop.GetComponentInChildren<Collider>();
            return collider != null ? collider.bounds.center : prop.transform.position;
        }

        /// <summary>
        /// Puts the rig on the spot and aims it, the way §05 aims: yaw on the body,
        /// pitch on the head pivot.
        /// <para>
        /// Writing the camera's own rotation does not survive a frame —
        /// <c>PlayerViewMotion</c> and <c>PlayerCameraRig</c> both own it and rewrite it
        /// from the pivot every <c>LateUpdate</c>, which is exactly what a first attempt
        /// at this test discovered the hard way (the eye stayed on world forward and the
        /// crosshair found nothing). §05's own components are switched off here rather
        /// than fought, because their behaviour has its own PlayMode suite; what this
        /// test is about starts at the eye transform.
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
        /// Asserts the prompt is actually being drawn — canvas up, group awake, every
        /// line enabled with a face and an opaque colour.
        /// <para>
        /// <b>Deliberately not a pixel measurement.</b> A batch-mode runner has no
        /// display, so Unity never runs the canvas update that turns an overlay canvas
        /// into screen pixels or lets <c>CanvasScaler</c> apply its factor: the canvas
        /// rect stays the raw 640×480 game view and the prompt's world corners stay in
        /// the player rig's rotated frame. Two earlier versions of this check asserted
        /// on those numbers and failed on the harness rather than on the game, which is
        /// worse than not checking. What survives is what the defect actually looked
        /// like — a prompt that exists and is never put on screen.
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

        /// <summary>Everything the pick-up path depends on, for a failure message that names the cause.</summary>
        private static string Diagnose(Camera camera, LootPropInteractable target, PlayerInteractor interactor)
        {
            var eye = camera.transform;
            var mark = target.transform.position;
            var log = new StringBuilder();

            log.AppendLine("  eye            " + eye.position.ToString("0.00") + " forward " + eye.forward.ToString("0.00"));
            log.AppendLine("  piece          " + mark.ToString("0.00") + "  " + target.name
                + "  layer " + LayerMask.LayerToName(target.gameObject.layer));
            log.AppendLine("  eye → piece    " + Vector3.Distance(eye.position, mark).ToString("0.00")
                + " m, reach " + target.ReachMetres.ToString("0.00") + " m");

            var collider = target.GetComponentInChildren<Collider>();
            log.AppendLine("  collider       " + (collider == null ? "NONE — the crosshair cannot hit it"
                : collider.GetType().Name + " trigger=" + collider.isTrigger + " enabled=" + collider.enabled
                  + " bounds=" + collider.bounds.size.ToString("0.00")));

            var hits = Physics.RaycastAll(eye.position, eye.forward,
                Mathf.Max(GameConstants.ClueReadRange, GameConstants.EngineerReachDistance),
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
            log.AppendLine("  shop open      "
                + (interactor.Director != null ? interactor.Director.ShopOpen.ToString() : "no director"));
            return log.ToString();
        }

        /// <summary>
        /// Where the rig's camera sits above its feet. Mirrors the harness rig's own
        /// figure; used only to aim the reachability probe.
        /// </summary>
        private const float EyeHeightMetres = 1.63f;
    }
}
#endif
