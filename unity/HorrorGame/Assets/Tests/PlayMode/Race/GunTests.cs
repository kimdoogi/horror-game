#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Monster;
using HorrorGame.Core.Race;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Race;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Racing
{
    /// <summary>
    /// §01's 총, from the alcove it lies in to the runner it sends back to B1.
    /// <para>
    /// <c>GunplayTests</c> in the core project already proves the RULE — one shot, twelve
    /// metres, self-shots refused, a hit calling <c>ReportCaught</c>. None of that is
    /// repeated here. What is tested is every part that touches a world, which is exactly
    /// the part a rule cannot prove: that the generator actually put guns in the building
    /// and put them somewhere legal, that the crosshair-and-key path takes one, that a
    /// landed shot moves a real seat in a real <c>RaceDirector</c>, and that a miss costs
    /// the same as a hit.
    /// </para>
    /// <para>
    /// <b>Why the placement test loads the whole building.</b> Because the defect it is
    /// written against is the one this repository keeps finding: a thing that was generated
    /// into a scene the game does not load, or generated into no scene at all. Asserting
    /// against a map the test built itself would prove the placement code runs, not that
    /// four guns are in the artefact. It fails, correctly and loudly, on a map that predates
    /// the placement pass — that is a regeneration that has not happened, not a bug in the
    /// test.
    /// </para>
    /// <para>
    /// Lives in the predefined assembly because <c>RunnerGun</c> does, and an
    /// <c>.asmdef</c> cannot reference one. Compiles out of a player build on
    /// <c>UNITY_INCLUDE_TESTS</c>.
    /// </para>
    /// </summary>
    public sealed class GunTests
    {
        /// <summary>The generated eight-storey building, as the solo playtest loads it.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>Seed the solo scene's own match is begun with when it has not begun one.</summary>
        private const int Seed = 20260804;

        /// <summary>
        /// Guns §07's band should produce on §01's tower: one each on B3, B4, B5 and B6.
        /// <para>
        /// Derived the way the generator derives it — <c>RaceState.Storeys</c> = 8, a
        /// quarter left clear at each end, one gun per remaining storey — rather than typed,
        /// so a tower that grows moves the expectation with it. <c>MapSceneBuilder</c>'s
        /// <c>GunBandInset</c> is the authority and this is the same arithmetic; it cannot
        /// be referenced because that class is in an editor assembly.
        /// </para>
        /// </summary>
        private static int BandInset => RaceState.Storeys / 4;

        /// <summary>
        /// Metres of plan clearance a gun must keep from every 착지, 출발점, 창조물 spawn
        /// and 문 hinge.
        /// <para>
        /// <c>MapKitCatalogue.CorridorClearWidth</c>, restated for the reason
        /// <c>Chute.DropHeightMetres</c> and <c>DescentPlaythroughTests.StoreyPitchMetres</c>
        /// are restated: this assembly cannot reference the editor's. It is the LARGEST
        /// radius <c>Editor/Dressing/KeepOut</c> uses — a door leaf's own reach — so a gun
        /// that clears it clears every keep-out volume in the building.
        /// </para>
        /// </summary>
        private const float KeepOutClearanceMetres = 2.2f;

        /// <summary>
        /// Metres a gun must keep from a 투하구, which is the runner's own sight range.
        /// <para>
        /// The design rule rather than a clearance: the drop is the one place on a floor
        /// every runner is already walking to, so a gun visible from it costs no detour and
        /// the detour is the decision. Same constant on both sides —
        /// <see cref="GameConstants.FlashlightRange"/> is in Core, which both assemblies see.
        /// </para>
        /// </summary>
        private static float ChuteClearanceMetres => GameConstants.FlashlightRange;

        /// <summary>How near a 도달 지점 a gun has to be to count as standing on it, metres. A cell is 2.5 m.</summary>
        private const float OnTheProbeMetres = 0.5f;

        /// <summary>Where the shooter's eye sits above their feet. Only used to aim the test's own ray.</summary>
        private const float EyeHeightMetres = 1.63f;

        /// <summary>Storey the target is put on before being shot, so "sent back" is visible as a change.</summary>
        private const int TargetStorey = 4;

        /// <summary>
        /// Frames the interact key is held. More than one so a scheduling slip between the
        /// Input System's update and <c>PlayerInteractor.Update</c> is visible as a failure
        /// rather than as a flake.
        /// </summary>
        private const int PressFrames = 3;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private Keyboard? _keyboard;
        private bool _keyboardIsOurs;
        private Mouse? _mouse;
        private bool _mouseIsOurs;
        private InputSettings.BackgroundBehavior _backgroundBehaviour;
        private InputSettings.EditorInputBehaviorInPlayMode _editorBehaviour;

        [SetUp]
        public void GiveTheRunnerHandsAndAKeyboard()
        {
            // A batch-mode editor has no input devices at all, and a test that silently did
            // nothing because Keyboard.current was null would be the exact defect it exists
            // to catch — the same reasoning, and the same fixture, as InteractionKeyPathTests.
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

            _mouse = Mouse.current;
            if (_mouse == null)
            {
                _mouse = InputSystem.AddDevice<Mouse>();
                _mouseIsOurs = true;
            }

            InputSystem.EnableDevice(_mouse);

            // RunnerGun's seat resolver is static and a test that installed one would leak
            // it into every test after it. Cleared both ways.
            RunnerGun.SeatOf = null;
        }

        [UnityTearDown]
        public IEnumerator PutTheWorldBack()
        {
            RunnerGun.SeatOf = null;

            InputSystem.settings.backgroundBehavior = _backgroundBehaviour;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorBehaviour;

            if (_keyboardIsOurs && _keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
            }

            if (_mouseIsOurs && _mouse != null)
            {
                InputSystem.RemoveDevice(_mouse);
            }

            _keyboard = null;
            _mouse = null;
            _keyboardIsOurs = false;
            _mouseIsOurs = false;

            for (var i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();

            // The scene unload is not housekeeping: an eight-storey building left in the
            // active scene put a wall between a monster and a listener and failed three
            // AudioSceneTests that had nothing to do with maps.
            var solo = SceneManager.GetSceneByName(SoloScene);
            if (solo.IsValid() && solo.isLoaded)
            {
                var empty = SceneManager.CreateScene("GunTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(solo);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // 1. Placement — the guns are in the artefact, on 막힌 길, and legal.
        // ------------------------------------------------------------------

        /// <summary>
        /// Every gun in the generated building stands on a 도달 지점 the reach audit already
        /// proves a runner can walk to, on a storey in §07's band, clear of every keep-out
        /// volume and out of sight of its floor's 투하구.
        /// <para>
        /// Reachability is asserted by COINCIDENCE with a <c>ReachProbe_*</c> marker rather
        /// than by a fresh NavMesh query, and that is the strong form rather than the lazy
        /// one: those 176 markers are the population <c>PlayerTraversal</c> reports
        /// <em>100.0% complete</em> over and <c>NavMeshConnectivity</c> pairs into 3482
        /// routes. A gun standing on one is reachable by the same evidence the whole
        /// building is signed off with; a gun standing anywhere else is reachable only by a
        /// measurement nobody has taken.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Every_gun_stands_on_a_reachable_dead_end_and_clear_of_every_keep_out()
        {
            yield return LoadMap();

            var markers = FindMarkerRoot();
            Assert.That(markers, Is.Not.Null,
                "the loaded scene has no '" + GunPickup.MapRootName + "/" + GunPickup.MarkerRootName
                + "' group at all, so it is not a generated map.");

            var guns = markers!.Find(GunPickup.GroupName);
            var first = BandInset;
            var last = RaceState.Storeys - 1 - BandInset;
            var expected = last - first + 1;

            Assert.That(guns, Is.Not.Null,
                "no '" + GunPickup.GroupName + "' group under " + GunPickup.MarkerRootName
                + ". Either MapSceneBuilder.BuildGuns did not run or this scene was written before "
                + "it existed — regenerate the map (지도 생성) and re-open the solo scene. The gun is "
                + "wired end to end and there is nothing in the building to pick up.");

            Assert.That(guns!.childCount, Is.EqualTo(expected + 1),
                "expected " + expected + " guns on B" + (first + 1) + "~B" + (last + 1) + " plus the one "
                + GunPickup.NamePrefix + " template, and found " + guns.childCount + " children.");

            var probes = PointsUnder(markers, "ReachProbes");
            Assert.That(probes.Count, Is.GreaterThan(0), "the map has no 도달 지점 markers to be reachable at.");

            var keepOut = new List<KeyValuePair<string, Vector3>>();
            keepOut.AddRange(Named(markers, "ChuteLandings", "착지"));
            keepOut.AddRange(Named(markers, "PlayerSpawns", "출발점"));
            keepOut.AddRange(Named(markers, "MonsterSpawns", "창조물"));
            foreach (Transform child in markers)
            {
                if (child.Find("Hinge") != null)
                {
                    keepOut.Add(new KeyValuePair<string, Vector3>("문 " + child.name, child.position));
                }
            }

            var chutes = Named(markers, "Chutes", "투하구");
            var report = new StringBuilder();
            var seen = new List<string>();

            foreach (Transform gun in guns)
            {
                if (gun.name == RunnerGun.HeldTemplateName)
                {
                    Assert.That(gun.gameObject.activeSelf, Is.False,
                        "the held-gun template is active in the scene, so every map has a revolver "
                        + "floating in an alcove that nobody is holding.");
                    continue;
                }

                seen.Add(gun.name);

                var onProbe = float.PositiveInfinity;
                for (var i = 0; i < probes.Count; i++)
                {
                    onProbe = Mathf.Min(onProbe, Flat(gun.position, probes[i]));
                }

                Assert.That(onProbe, Is.LessThanOrEqualTo(OnTheProbeMetres),
                    gun.name + " is " + M(onProbe) + " m from the nearest 도달 지점. It is therefore "
                    + "standing somewhere the reach audit has never measured, and 「100.0% complete」 "
                    + "says nothing about whether a runner can get to it.");

                var nearestKeepOut = Nearest(gun.position, keepOut, out var whatKeepOut);
                Assert.That(nearestKeepOut, Is.GreaterThanOrEqualTo(KeepOutClearanceMetres),
                    gun.name + " is " + M(nearestKeepOut) + " m from " + whatKeepOut + ", inside the "
                    + M(KeepOutClearanceMetres) + " m every keep-out volume needs. Editor/Dressing/KeepOut "
                    + "exists because run 11 dropped scenery under a 투하구 and every runner who took "
                    + "that chute landed on it.");

                var nearestChute = Nearest(gun.position, chutes, out var whatChute);
                Assert.That(nearestChute, Is.GreaterThanOrEqualTo(ChuteClearanceMetres),
                    gun.name + " is " + M(nearestChute) + " m from " + whatChute + ", inside the "
                    + M(ChuteClearanceMetres) + " m a runner can see. A gun you can spot from the drop "
                    + "you were already walking to costs no detour, and the detour is the decision.");

                report.Append(gun.name).Append(" probe ").Append(M(onProbe))
                    .Append(" m · keep-out ").Append(M(nearestKeepOut))
                    .Append(" m · 투하구 ").Append(M(nearestChute)).Append(" m\n");
            }

            Assert.That(seen.Count, Is.EqualTo(expected),
                "expected one gun per storey on B" + (first + 1) + "~B" + (last + 1) + " and found "
                + seen.Count + ": " + string.Join(", ", seen));

            for (var storey = first; storey <= last; storey++)
            {
                var name = GunPickup.NamePrefix + (storey + 1);
                Assert.That(seen, Contains.Item(name),
                    "B" + (storey + 1) + " has no gun. §07's band is 「" + (first + 1) + "~" + (last + 1)
                    + "」 and a hole in it means one storey plays a different game from its neighbours.");
            }

            Debug.Log("[GunTests] " + report);
        }

        /// <summary>
        /// The guns in the map answer the crosshair. A pickup with no component on it is
        /// scenery, and scenery is what the whole feature would silently be.
        /// </summary>
        [UnityTest]
        public IEnumerator The_generated_guns_carry_the_pickup_component()
        {
            yield return LoadMap();

            var attached = GunPickup.AttachAll();
            var live = Object.FindObjectsByType<GunPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(attached, Is.GreaterThan(0),
                "GunPickup.AttachAll found nothing to attach to. The map has no '"
                + GunPickup.GroupName + "' group, or its children are not named '"
                + GunPickup.NamePrefix + "*'.");
            Assert.That(live.Length, Is.EqualTo(attached),
                "AttachAll reported " + attached + " guns and the scene holds " + live.Length
                + " GunPickup components — it is not idempotent, and a gun with two of them can be "
                + "taken twice.");

            // Twice, because MatchDirector, a scene load and a test may all reasonably ask.
            Assert.That(GunPickup.AttachAll(), Is.EqualTo(attached), "AttachAll is not idempotent.");
            Assert.That(
                Object.FindObjectsByType<GunPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(attached),
                "a second AttachAll added components.");
        }

        // ------------------------------------------------------------------
        // 2. Pickup — the existing crosshair and key, not a method call.
        // ------------------------------------------------------------------

        /// <summary>
        /// Looking at a gun and pressing the interact key arms the runner.
        /// <para>
        /// Driven through <c>PlayerInteractor</c>'s real <c>Update</c> — the crosshair ray,
        /// the focus, the key — rather than by calling <c>Take</c>, because "the pickup
        /// works" and "the pickup is reachable by the key the player actually has" are
        /// different claims and only the second one ships.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Pressing_the_interact_key_at_a_gun_arms_the_runner()
        {
            var runner = BuildRunner(Vector3.zero, Quaternion.identity, out var interactor, out _);
            var pickup = BuildPickup(new Vector3(0f, EyeHeightMetres, 1.5f));

            yield return null;
            Physics.SyncTransforms();
            yield return null;

            Assert.That(interactor.Focus, Is.SameAs(pickup),
                "the crosshair is not on the gun; PlayerInteractor.Focus is "
                + (interactor.Focus == null ? "nothing" : interactor.Focus.Title)
                + ". The trigger MapSceneBuilder puts on a gun is what the ray has to find.");
            Assert.That(pickup.Action, Is.EqualTo("줍기"),
                "the prompt offers no verb, so the runner is not told the key does anything.");

            yield return PressInteract();

            Assert.That(pickup.Taken, Is.True, "the key did not take the gun.");

            var gun = runner.GetComponentInChildren<RunnerGun>();
            Assert.That(gun, Is.Not.Null,
                "nothing on the runner is holding the gun. GunPickup gives the interactor a RunnerGun "
                + "when it has none, so a null here means that path did not run.");
            Assert.That(gun!.ShotsLeft, Is.EqualTo(Gunplay.ShotsPerGun),
                "the runner picked a gun up and it has " + gun.ShotsLeft + " shots.");
            Assert.That(gun.Armed, Is.True, "ShotsLeft is set but nothing is in the hand.");

            // A second gun changes nothing. Gunplay.ShotsPerGun's argument is that twenty
            // players with repeating fire turns a maze into a shooting gallery, and two
            // pickups in one pair of hands is a reload with extra steps.
            var second = BuildPickup(new Vector3(0f, EyeHeightMetres, 1.5f));
            Assert.That(gun.TryTake(second), Is.False, "a second gun was taken on top of the first.");
            Assert.That(second.Taken, Is.False, "the refused gun was consumed anyway.");
        }

        // ------------------------------------------------------------------
        // 3. Firing — a hit costs a descent, a miss costs the shot.
        // ------------------------------------------------------------------

        /// <summary>
        /// A landed shot sends the target back to the storey they started on and leaves
        /// them running, which is exactly what §06's creature costs.
        /// </summary>
        [UnityTest]
        public IEnumerator A_hit_sends_the_target_back_to_their_own_start_line()
        {
            var race = BuildRace(2);
            var shooter = BuildRunner(Vector3.zero, Quaternion.identity, out _, out var gun);
            gun.Bind(race, 0);

            var target = BuildTarget(new Vector3(0f, 0f, 6f), 1);
            yield return null;
            Physics.SyncTransforms();

            Assert.That(race.ReportDescent(1, TargetStorey, 30f), Is.True,
                "the target could not be put on B" + (TargetStorey + 1) + " to be knocked off it.");
            Assert.That(race.Rules![1].Storey, Is.EqualTo(TargetStorey));

            Assert.That(gun.TryTake(BuildPickup(new Vector3(0f, EyeHeightMetres, 1.5f))), Is.True,
                "the shooter could not be armed.");

            var outcome = gun.Fire();

            Assert.That(outcome, Is.EqualTo(ShotRefusal.None),
                "the shot did not land: " + outcome + ". The target is " + target.name + " at "
                + Flat(shooter.transform.position, target.transform.position).ToString("0.00")
                + " m, inside Gunplay.RangeMetres of " + Gunplay.RangeMetres + " m.");

            var hit = race.Rules![1];
            Assert.That(hit.Storey, Is.Zero,
                "the target is still on B" + (hit.Storey + 1) + ". §06 sends a caught runner to the "
                + "cell they started from on B1 and RaceState.ReportCaught is the one call that does "
                + "it — a gun with its own kind of setback would be a second rule for a player to learn.");
            Assert.That(hit.Status, Is.EqualTo(RacerStatus.Running),
                "being shot took the target out of the race. Nothing eliminates a player: "
                + "ReportEliminated is only reached by a disconnect.");
            Assert.That(hit.TimesCaught, Is.EqualTo(1), "the setback was not recorded.");
            Assert.That(gun.ShotsLeft, Is.Zero, "the gun still has a shot after firing it.");
            Assert.That(gun.Armed, Is.True, "the spent gun vanished; a spent gun is still a gun.");

            // And it cannot be done twice. One shot per gun is the whole balance.
            Assert.That(gun.Fire(), Is.EqualTo(ShotRefusal.NoGun), "the gun fired a second time.");
            Assert.That(race.Rules![1].TimesCaught, Is.EqualTo(1), "a second setback was recorded.");
        }

        /// <summary>
        /// A miss spends the shot and moves nobody. The shot has to cost the same whether it
        /// lands or not, or firing speculatively down a corridor is free.
        /// </summary>
        [UnityTest]
        public IEnumerator A_miss_spends_the_shot_and_moves_nobody()
        {
            var race = BuildRace(2);
            var shooter = BuildRunner(Vector3.zero, Quaternion.identity, out _, out var gun);
            gun.Bind(race, 0);

            // Behind the shooter, so the crosshair ray finds nothing at all.
            BuildTarget(new Vector3(0f, 0f, -6f), 1);
            yield return null;
            Physics.SyncTransforms();

            Assert.That(race.ReportDescent(1, TargetStorey, 30f), Is.True);
            Assert.That(gun.TryTake(BuildPickup(new Vector3(0f, EyeHeightMetres, 1.5f))), Is.True);

            var outcome = gun.Fire();

            Assert.That(gun.ShotsLeft, Is.Zero,
                "a miss left the shot in the gun. Gunplay's argument is that one shot is a decision "
                + "you make once and have to live with; a free miss is not a decision.");
            Assert.That(outcome, Is.Not.EqualTo(ShotRefusal.None), "a shot at nothing reported a hit.");
            Assert.That(race.Rules![1].Storey, Is.EqualTo(TargetStorey),
                "the runner behind the shooter was sent back to B1 by a shot fired the other way.");
            Assert.That(race.Rules![1].TimesCaught, Is.Zero, "a miss recorded a setback.");

            Assert.That(shooter, Is.Not.Null);
        }

        /// <summary>
        /// Nobody shoots themselves back to the start line, however the ray is aimed.
        /// </summary>
        [UnityTest]
        public IEnumerator A_runner_cannot_shoot_themselves()
        {
            var race = BuildRace(2);
            BuildRunner(Vector3.zero, Quaternion.identity, out _, out var gun);
            gun.Bind(race, 0);

            // Everything the ray hits answers with the shooter's own seat.
            RunnerGun.SeatOf = _ => 0;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(wall);
            wall.transform.position = new Vector3(0f, EyeHeightMetres, 4f);

            yield return null;
            Physics.SyncTransforms();

            Assert.That(race.ReportDescent(0, TargetStorey, 30f), Is.True);
            Assert.That(gun.TryTake(BuildPickup(new Vector3(0f, EyeHeightMetres, 1.5f))), Is.True);

            gun.Fire();

            Assert.That(race.Rules![0].Storey, Is.EqualTo(TargetStorey),
                "the shooter sent themselves back to B1.");
            Assert.That(gun.ShotsLeft, Is.Zero, "the shot was not spent.");
        }

        // ------------------------------------------------------------------
        // 4. The report — §06's one way out of 순찰.
        // ------------------------------------------------------------------

        /// <summary>
        /// A gunshot is louder and carries further than anything else a runner can do.
        /// <para>
        /// §06's table gives 순찰 exactly one transition — 소리 감지 → 경계 — so a noise that
        /// did not beat a footstep would leave the creature listening to somebody's feet
        /// while a revolver went off beside it. <c>MonsterBrain</c> takes the loudest cue in
        /// a tick, so "loudest" is literal and this is the assertion that keeps it true when
        /// §04's voice table or §06's hearing range is retuned.
        /// </para>
        /// </summary>
        [Test]
        public void A_gunshot_outranges_and_outshouts_everything_else_a_runner_can_do()
        {
            Assert.That(RunnerGun.GunshotRangeMetres,
                Is.GreaterThan(GameConstants.MonsterFootstepHearingRange),
                "a gunshot carries " + M(RunnerGun.GunshotRangeMetres) + " m against a sprint's "
                + M(GameConstants.MonsterFootstepHearingRange) + " m on the loudest floor, so the "
                + "creature would rather listen to feet.");

            Assert.That(RunnerGun.GunshotLoudness, Is.GreaterThanOrEqualTo(1f),
                "MonsterBrain takes the loudest cue in a tick and the gunshot is not at the top of "
                + "the scale.");

            Assert.That(RunnerGun.GunshotRangeMetres, Is.GreaterThan(GameConstants.MonsterSightRange),
                "the creature can see further than it can hear a gunshot, which makes firing at "
                + "somebody safer than standing in the open.");
        }

        /// <summary>
        /// The creature on the shooter's floor is told. Without this, §06's one door out of
        /// 순찰 stays shut and the loudest thing in the building does nothing at all — which
        /// is the precise defect <c>MonsterKillTests</c> holds the reproduction for.
        /// </summary>
        [UnityTest]
        public IEnumerator Firing_hands_the_creature_a_sound_it_can_act_on()
        {
            yield return LoadMap();

            var director = Object.FindFirstObjectByType<MatchDirector>();
            Assert.That(director, Is.Not.Null, "the solo scene has no MatchDirector");

            var monster = director!.LocalStoreyMonster;
            Assert.That(monster, Is.Not.Null,
                "no creature shares the runner's storey, so there is nothing to hear the shot. §12-B③ "
                + "puts one on every floor and MatchDirector.PrepareCreatures stands them up.");

            // The gun is fired through the same component the game uses, on the same rig, so
            // the cue that reaches the creature is the shipped one rather than a hand-made
            // ReportSound the test wrote itself.
            var runner = Object.FindFirstObjectByType<PlayerInteractor>();
            Assert.That(runner, Is.Not.Null, "the solo scene has no player rig");

            var rig = runner!.transform.root;
            var controller = rig.GetComponent<CharacterController>();

            // Stood beside it, because the assertion is about the CUE and not about the
            // geometry of one generated floor: a creature that happened to start 50 m away
            // would be out of GunshotRangeMetres and the test would be measuring where
            // DescentMap put it.
            if (controller != null)
            {
                controller.enabled = false;
            }

            rig.position = monster!.transform.position + new Vector3(0f, 0f, 3f);

            // And it STAYS off until the shot is fired. Re-enabling it let two frames of
            // gravity run before the assertions, and 3 m north of a creature is not promised
            // to be floor: over a 투하구 mouth the rig falls, and past ~1.8 m of fall
            // (MapGraph.StoreyChangeMetres) the runner's own storey — and therefore
            // LocalStoreyMonster — becomes the floor below. Nothing between here and Fire()
            // needs the controller: the assertions are about the cue, and a rig that cannot
            // fall cannot change floors.
            yield return null;

            var gun = rig.gameObject.GetComponent<RunnerGun>() ?? rig.gameObject.AddComponent<RunnerGun>();
            yield return null;

            // Re-resolve, then stand beside whoever it IS, with no frame in between.
            //
            // This assertion used to be "the local creature did not change", and it went red
            // about one run in three: the creatures are patrolling through all of this, and
            // §12-B③'s creature one floor down can walk a 계단 up to within
            // StoreyChangeMetres of the runner's height and, being nearer in flat distance
            // than the 3 m we just stood off, win LocalStoreyCreature's tie-break. Nothing
            // was wrong with the game — the test was asserting that a live building holds
            // still. The subject is "the creature on the shooter's floor is told", and it is
            // told whoever it turns out to be, so the test now takes the answer instead of
            // demanding a particular one, and takes it in the same instant it acts on it.
            monster = director.LocalStoreyMonster;
            Assert.That(monster, Is.Not.Null,
                "no creature shares the runner's storey after the move, so there is nothing to "
                + "hear the shot.");
            rig.position = monster!.transform.position + new Vector3(0f, 0f, 3f);

            Assert.That(gun.TryTake(BuildPickup(rig.position + (Vector3.up * EyeHeightMetres))),
                Is.True, "the runner could not be armed");
            Assert.That(director.LocalStoreyMonster, Is.SameAs(monster),
                "the creature local to the rig changed within a single frame, with nothing "
                + "moving. That is not the patrol race this test was rewritten for.");
            Assert.That(monster.State, Is.EqualTo(MonsterStateId.Patrol),
                "the creature is already out of 순찰 before the shot, so this test cannot tell what "
                + "moved it.");

            gun.Fire();

            var shot = gun.LastGunshot;
            Assert.That(shot.Heard, Is.True,
                "no creature was handed the report. A gunshot that §06 cannot hear is the loudest "
                + "thing in the building being inaudible, and MonsterKillTests holds the "
                + "reproduction of what a creature with no sound input does: nothing, forever.");
            Assert.That(shot.RangeMetres, Is.EqualTo(RunnerGun.GunshotRangeMetres).Within(0.001f));
            Assert.That(shot.Loudness, Is.EqualTo(RunnerGun.GunshotLoudness).Within(0.001f));

            // §06's table gives 순찰 exactly one transition — 소리 감지 → 경계 — and nothing
            // in the shipping game ever raised a sound cue, so that door led nowhere. One
            // fixed step is all it takes when the cue is in range; the assertion is that the
            // creature stopped patrolling, not which row it moved to.
            monster.Simulate(GameConstants.FixedStep);
            Assert.That(monster.State, Is.Not.EqualTo(MonsterStateId.Patrol),
                "the creature heard a revolver go off three metres away and carried on patrolling. "
                + "§06's one way out of 순찰 is 소리 감지, and this is the loudest cue the game can "
                + "raise.");
        }

        // ------------------------------------------------------------------
        // Scaffolding.
        // ------------------------------------------------------------------

        private IEnumerator LoadMap()
        {
            // Loading the solo scene re-emits Mirror's packaging complaint about a folder
            // Unity itself calls immutable. Suppressed only because every step below is
            // asserted explicitly.
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var director = Object.FindFirstObjectByType<MatchDirector>();
            if (director != null && director.Map == null)
            {
                director.BeginMatch(Seed);
            }

            yield return null;

            foreach (var agent in Object.FindObjectsByType<MonsterAgent>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                agent.enabled = false;
            }
        }

        private static Transform? FindMarkerRoot()
        {
            foreach (var t in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == GunPickup.MarkerRootName && t.parent != null
                    && t.parent.name == GunPickup.MapRootName)
                {
                    return t;
                }
            }

            return null;
        }

        private static List<Vector3> PointsUnder(Transform markers, string group)
        {
            var found = new List<Vector3>();
            var root = markers.Find(group);
            if (root == null)
            {
                return found;
            }

            foreach (Transform child in root)
            {
                found.Add(child.position);
            }

            return found;
        }

        private static List<KeyValuePair<string, Vector3>> Named(
            Transform markers, string group, string label)
        {
            var found = new List<KeyValuePair<string, Vector3>>();
            var root = markers.Find(group);
            if (root == null)
            {
                return found;
            }

            foreach (Transform child in root)
            {
                found.Add(new KeyValuePair<string, Vector3>(label + " " + child.name, child.position));
            }

            return found;
        }

        private static float Nearest(
            Vector3 at, List<KeyValuePair<string, Vector3>> points, out string what)
        {
            what = "nothing";
            var nearest = float.PositiveInfinity;
            for (var i = 0; i < points.Count; i++)
            {
                var gap = Flat(at, points[i].Value);
                if (gap < nearest)
                {
                    nearest = gap;
                    what = points[i].Key;
                }
            }

            return nearest;
        }

        /// <summary>Plan distance. The vertical is dropped for the reason Gunplay.Judge drops it.</summary>
        private static float Flat(Vector3 a, Vector3 b)
        {
            var d = a - b;
            d.y = 0f;
            return d.magnitude;
        }

        private static string M(float metres) => metres.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>A rig with an eye, a crosshair and hands. No motor: nothing here moves.</summary>
        private GameObject BuildRunner(
            Vector3 at, Quaternion facing, out PlayerInteractor interactor, out RunnerGun gun)
        {
            var runner = new GameObject("Runner");
            _spawned.Add(runner);
            runner.transform.SetPositionAndRotation(at, facing);

            var eye = new GameObject("Eye", typeof(Camera));
            eye.transform.SetParent(runner.transform, false);
            eye.transform.localPosition = new Vector3(0f, EyeHeightMetres, 0f);

            interactor = runner.AddComponent<PlayerInteractor>();
            gun = runner.AddComponent<RunnerGun>();
            return runner;
        }

        /// <summary>Another runner, with a body the crosshair can hit and a seat it answers with.</summary>
        private GameObject BuildTarget(Vector3 at, int seat)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _spawned.Add(body);
            body.name = "Runner_" + seat.ToString(CultureInfo.InvariantCulture);
            body.transform.position = at + (Vector3.up * (EyeHeightMetres * 0.5f));

            var gun = body.AddComponent<RunnerGun>();
            gun.Seat = seat;
            return body;
        }

        /// <summary>
        /// A gun on the floor, built the way <c>MapSceneBuilder.PlaceGun</c> builds one: a
        /// trigger the crosshair can find and nothing solid.
        /// </summary>
        private GunPickup BuildPickup(Vector3 at)
        {
            var go = new GameObject("Gun_Test");
            _spawned.Add(go);
            go.transform.position = at;

            var trigger = go.AddComponent<BoxCollider>();
            trigger.size = Vector3.one * 0.275f;
            trigger.isTrigger = true;

            return go.AddComponent<GunPickup>();
        }

        private RaceDirector BuildRace(int runners)
        {
            var host = new GameObject("RaceDirector");
            _spawned.Add(host);
            var race = host.AddComponent<RaceDirector>();

            // Bound before Begin so no scene lookup runs: these tests have no building, and
            // a director shouting about a missing finish would be shouting correctly.
            race.BindFinish(new Vector3(30f, -26.25f, 30f));
            Assert.That(race.Begin(runners), Is.True, "the race refused to begin with " + runners + " runners");
            return race;
        }

        /// <summary>
        /// Taps the interact key the way <c>InteractionKeyPathTests</c> does, and for the
        /// reason it records: queued and then YIELDED, with no manual
        /// <c>InputSystem.Update()</c> in between. A manual update consumes the event in the
        /// same instant, so by the time any <c>MonoBehaviour.Update</c> runs the press is a
        /// frame old and <c>wasPressedThisFrame</c> is false — the key is delivered and
        /// nothing sees it, which is an excellent imitation of the defect this is here to
        /// catch.
        /// </summary>
        private IEnumerator PressInteract()
        {
            var keyboard = _keyboard;
            Assert.That(keyboard, Is.Not.Null, "no keyboard to press with");

            var seen = false;
            for (var frame = 0; frame < PressFrames; frame++)
            {
                InputSystem.QueueStateEvent(keyboard!, new KeyboardState(PlayerInteractor.InteractKey));
                yield return null;

                var control = Keyboard.current != null ? Keyboard.current[PlayerInteractor.InteractKey] : null;
                seen |= control != null && control.isPressed;
            }

            InputSystem.QueueStateEvent(keyboard!, new KeyboardState());
            yield return null;
            yield return null;

            Assert.That(seen, Is.True,
                "the Input System never delivered the key to Keyboard.current, so this run proves "
                + "nothing about the pickup — it proves the harness did not press anything.");
        }
    }
}
#endif
