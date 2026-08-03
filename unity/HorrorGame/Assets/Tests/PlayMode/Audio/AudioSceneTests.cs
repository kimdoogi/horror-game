#nullable enable

using System.Collections;
using System.Collections.Generic;
using HorrorGame.Audio;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HorrorGame.Tests.PlayMode.Audio
{
    /// <summary>
    /// Measures the things a batch run cannot hear.
    /// <para>
    /// <c>AudioTests</c> in EditMode already checks the mix's <em>reasoning</em> — the
    /// occlusion floor, the rolloff shape, §04's noise ordering. None of that catches
    /// the failure this layer actually had, which was that all of it was correct and
    /// nothing was in a scene. These tests enter Play mode, build the same graph
    /// <c>MatchAudioRig</c> builds for the solo playtest, and assert on what comes out:
    /// which clip a surface picks, whether the bed crossed when the zone did, whether
    /// §06's 정지 is silent, and whether a wall moves the filter.
    /// </para>
    /// <para>
    /// Everything asserted here is engine-state rather than audio-engine state — a
    /// clip reference, a fader level, an <see cref="AudioLowPassFilter"/> corner. Unity
    /// runs <c>-batchmode</c> on a null sound driver, so a test that believed
    /// <c>AudioSource.isPlaying</c> would be testing the driver rather than the mix.
    /// </para>
    /// </summary>
    public sealed class AudioSceneTests
    {
        /// <summary>
        /// Metres of the walk used to prove a surface. Comfortably more than
        /// <see cref="AudioTuning.FootstepStrideMetres"/>, so several steps land on each
        /// floor and a single unlucky frame cannot decide the result.
        /// </summary>
        private const float WalkMetresPerSurface = 3f;

        /// <summary>Seconds to let a crossfade or an occlusion probe settle. Longer than both time constants.</summary>
        private const float SettleSeconds = 1.5f;

        /// <summary>
        /// The scene <c>SoloPlaytest.BuildScene</c> writes — the only one this fixture
        /// loads. Named at fixture scope so <see cref="PutTheSceneBack"/> can unload it
        /// without depending on which test left it standing.
        /// </summary>
        private const string SoloSceneName = "Map_FirstSketch_Solo";

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private SurfaceAudioLibrary? _surfaces;
        private MatchAudioLibrary? _library;
        private GameObject? _listener;
        private GameAudio? _mix;

        [SetUp]
        public void SetUp()
        {
            // Built from AudioClipCatalog rather than from the wired asset, so the test
            // exercises the same naming rule the wiring step does. If a clip is renamed,
            // both fail — which is the point: the filenames are the authority, and a test
            // that read the already-wired asset would happily pass on a stale one.
            _surfaces = ScriptableObject.CreateInstance<SurfaceAudioLibrary>();
            _library = ScriptableObject.CreateInstance<MatchAudioLibrary>();

            var report = AudioClipCatalog.Populate(_surfaces, _library, LoadClip);

            Assert.That(report.Complete, Is.True,
                "The shipped clip set does not match AudioClipCatalog: " + report.Summary
                + ". Every assertion below would be measuring an empty library rather than "
                + "the mix.");

            _listener = Spawn("Ears");
            _listener.AddComponent<AudioListener>();
            _listener.transform.position = new Vector3(0f, 1.6f, 0f);

            var mix = Spawn("[Audio]");
            _mix = mix.AddComponent<GameAudio>();
        }

        [TearDown]
        public void TearDown()
        {
            FloorSurfaces.Probe = null;

            // Restored here rather than at the end of the one test that sets it: an
            // assertion that fails mid-test would otherwise leave the whole suite
            // ignoring errors, and a test suite that cannot see an error is worse than
            // no suite at all.
            LogAssert.ignoreFailingMessages = false;

            for (var i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();

            if (_surfaces != null)
            {
                Object.DestroyImmediate(_surfaces);
            }

            if (_library != null)
            {
                Object.DestroyImmediate(_library);
            }
        }

        /// <summary>
        /// Puts an empty scene back after the one test here that loads a match.
        /// <para>
        /// <b>Why this moved out of the test body.</b> It used to be the last four lines of
        /// <see cref="TheSoloPlaytestScene_ComesOutAudible"/>, which means it only ran when
        /// that test <em>passed</em> — every assertion above it leaves the eight-storey solo
        /// scene loaded for the rest of the run, and the failure that causes is silent,
        /// order-dependent and lands on somebody else's fixture. That is the exact shape of
        /// the leak <c>LobbyEntryWiringTests</c> shipped, so it is closed the same way and
        /// in the same place: a teardown, which runs on the failing path too.
        /// </para>
        /// <para>
        /// Alongside the plain <c>[TearDown]</c> rather than merged into it, because that
        /// one runs synchronously and <c>UnloadSceneAsync</c> needs frames; the two touch
        /// nothing in common, so their order does not matter. It costs nothing on the
        /// fixture's other tests — none of them loads a scene, so the guard falls through.
        /// </para>
        /// </summary>
        [UnityTearDown]
        public IEnumerator PutTheSceneBack()
        {
            var solo = UnityEngine.SceneManagement.SceneManager.GetSceneByName(SoloSceneName);
            if (solo.IsValid() && solo.isLoaded)
            {
                var blank = UnityEngine.SceneManagement.SceneManager.CreateScene("AudioTests_Blank");
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(blank);
                yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(solo);
            }
        }

        // ====================================================================
        // §12 — the floor is a gameplay channel, so the wrong surface is a wrong
        // answer rather than a wrong sound.
        // ====================================================================

        [UnityTest]
        public IEnumerator WalkingAcrossTheFiveSurfaces_PlaysTheClipForTheFloorUnderfoot()
        {
            var floors = new[]
            {
                FloorMaterial.Wood, FloorMaterial.Tile, FloorMaterial.Gravel,
                FloorMaterial.Concrete, FloorMaterial.Metal,
            };

            // The surface as a function of position, exactly as MatchAudioBridge binds
            // the map's own IWorldProbe. §12 makes this a rules-grade query and F-002
            // names a mix that disagrees with the rules as the thing that ends §04.
            FloorSurfaces.Probe = point => FloorAtStripe(point, floors);

            var player = Spawn("Player");
            player.transform.position = new Vector3(0f, 0f, 0f);

            var steps = player.AddComponent<FootstepAudio>();
            steps.Configure(_surfaces, FootstepActor.PlayerWalk, null);

            yield return null;

            // One continuous walk across all five stripes, sampling every step as it
            // happens rather than after a leg finishes. FootstepAudio records the floor
            // it sampled at the moment it chose the clip, so pairing the two is exact —
            // and it never has to guess which side of a boundary a step landed on.
            var heard = new Dictionary<FloorMaterial, string>();
            var lastCount = steps.StepCount;
            var travelled = 0f;
            var total = floors.Length * WalkMetresPerSurface;

            while (travelled < total)
            {
                var step = GameConstants.WalkSpeed * Time.deltaTime;
                travelled += step;

                var position = player.transform.position;
                position.x += step;
                player.transform.position = position;

                yield return null;

                if (steps.StepCount == lastCount)
                {
                    continue;
                }

                lastCount = steps.StepCount;

                var clip = steps.LastClip;
                Assert.That(clip, Is.Not.Null, "A step was counted but no clip came out of it.");

                var floor = steps.CurrentFloor;
                var expected = "step_" + AudioClipCatalog.TokenOf(floor) + "_player_walk_";

                Assert.That(clip!.name, Does.StartWith(expected),
                    "§12: 구역별로 바닥 재질이 달라야 청음사가 위치를 판별할 수 있다. Standing on "
                    + floor + " must play " + expected + "*, not " + clip.name + " — the floor is a "
                    + "system decision, so the wrong clip is the game telling the Listener the "
                    + "wrong zone.");

                heard[floor] = clip.name;
            }

            Assert.That(steps.StepCount, Is.GreaterThan(0),
                "§04 gives the 청음사 nothing but footsteps. A walk that produces none is the role "
                + "with no input at all.");

            for (var i = 0; i < floors.Length; i++)
            {
                Assert.That(heard.ContainsKey(floors[i]), Is.True,
                    "No step was heard on " + floors[i] + ". §12 built the map so that every zone "
                    + "has a different floor; a surface that never sounds is a letter missing from "
                    + "the alphabet §04 reads position off.");
            }

            Assert.That(new HashSet<string>(heard.Values).Count, Is.EqualTo(floors.Length),
                "All five surfaces must sound different. §12 makes the material map the Listener's "
                + "only location cue, and two zones that share a clip are one zone to them.");
        }

        [UnityTest]
        public IEnumerator TheMonstersStepsUseTheMonsterSet_NotThePlayers()
        {
            FloorSurfaces.Probe = _ => FloorMaterial.Gravel;

            var monster = Spawn("Monster");
            var steps = monster.AddComponent<FootstepAudio>();
            steps.Configure(_surfaces, FootstepActor.MonsterStep, null);

            yield return null;
            yield return WalkAlongX(monster.transform, 0f, WalkMetresPerSurface);

            Assert.That(steps.LastClip, Is.Not.Null);
            Assert.That(steps.LastClip!.name, Does.StartWith("step_gravel_monster_step_"),
                "§04 hands the Listener the monster's steps as information and the player's own as "
                + "interference. Playing one set for the other would make the role read its own feet.");
        }

        // ====================================================================
        // §12 — the room tone and the footstep are the same claim about a place.
        // ====================================================================

        [UnityTest]
        public IEnumerator CrossingAZoneBoundary_CrossfadesTheAmbienceBed()
        {
            var floors = new[] { FloorMaterial.Wood, FloorMaterial.Tile };
            FloorSurfaces.Probe = point => FloorAtStripe(point, floors);

            var host = Spawn("[Ambience]");
            var zones = host.AddComponent<ZoneAmbienceDirector>();
            // Null where SurfaceBed was: §01's 지상 bed is deleted, so what a zone sounds
            // like is §12's floor material and nothing else.
            zones.Configure(_surfaces, null, System.Array.Empty<AudioClip?>());

            _listener!.transform.position = new Vector3(1f, 1.6f, 0f);
            yield return Settle();

            var first = zones.CurrentBed;
            Assert.That(first, Is.Not.Null,
                "A zone with no room tone is a zone the player cannot hear themselves standing in.");
            Assert.That(zones.CurrentFloor, Is.EqualTo(FloorMaterial.Wood));
            Assert.That(first!.name, Is.EqualTo("amb_zone_a_wood_loop"),
                "§12 keys the bed on the floor material on purpose: a room that sounded 나무 while "
                + "its footsteps said 타일 would be a map contradicting itself.");

            // Step across the boundary — one stride, which is all §12 promises.
            _listener.transform.position = new Vector3(WalkMetresPerSurface + 1f, 1.6f, 0f);
            yield return Settle();

            var second = zones.CurrentBed;
            Assert.That(zones.CurrentFloor, Is.EqualTo(FloorMaterial.Tile));
            Assert.That(second, Is.Not.Null);
            Assert.That(second!.name, Is.EqualTo("amb_zone_b_tile_loop"));
            Assert.That(second, Is.Not.SameAs(first),
                "§12 wants 재질 경계를 명확히. A bed that does not change at a boundary makes the "
                + "whole basement one room to §04's Listener.");
        }

        // ====================================================================
        // §07 — the night, felt without being read.
        // ====================================================================

        [UnityTest]
        public IEnumerator TheThreatBedCrossfades_AsTheClockAdvances()
        {
            var host = Spawn("[Threat]");
            var threat = host.AddComponent<ThreatBedDirector>();
            threat.Configure(_library!.TensionBeds, _library.TierStingers);

            threat.ElapsedSeconds = 0f;
            yield return Settle();
            yield return Settle();

            var earlyOnTierZero = threat.LevelOfTier(0);
            var earlyOnTierThree = threat.LevelOfTier(3);

            Assert.That(earlyOnTierZero, Is.GreaterThan(0.1f),
                "§07's 초저녁 has a bed and the match starts in it.");
            Assert.That(earlyOnTierThree, Is.LessThan(0.05f),
                "새벽 must not already be playing at minute zero.");

            // Three tiers on. §07's table: 0~8 초저녁, 8~16 밤, 16~24 심야, 24~32 새벽.
            threat.ElapsedSeconds = GameConstants.ThreatTierSeconds * 3f;

            // The crossfade time constant is deliberately long (§07's pressure is
            // "연속적"), so this waits several of them rather than a frame.
            for (var i = 0; i < 6; i++)
            {
                yield return Settle();
            }

            Assert.That(threat.LevelOfTier(3), Is.GreaterThan(earlyOnTierThree + 0.1f),
                "§07 gives the mix a continuous scalar precisely so a player can feel the night "
                + "advancing underground. A bed that never moves is §07 implemented as a number "
                + "nobody can perceive.");

            Assert.That(threat.LevelOfTier(0), Is.LessThan(earlyOnTierZero),
                "The tier that has passed has to give way, or the five beds pile up into one.");
        }

        // ====================================================================
        // §06 — the silence is the weapon.
        // ====================================================================

        [UnityTest]
        public IEnumerator Standstill_ProducesNoSoundAtAll()
        {
            FloorSurfaces.Probe = _ => FloorMaterial.Concrete;

            var monster = Spawn("Monster");
            monster.transform.position = new Vector3(0f, 0f, 5f);

            var feet = new GameObject("audio_footsteps");
            feet.transform.SetParent(monster.transform, false);
            _spawned.Add(feet);

            var steps = feet.AddComponent<FootstepAudio>();
            steps.Configure(_surfaces, FootstepActor.MonsterStep, null);

            var voice = monster.AddComponent<MonsterAudio>();
            voice.Configure(_library, steps);

            // Chase first, so the test proves the silence is a state change rather than a
            // component that was never wired.
            voice.State = MonsterStateId.Chase;
            yield return Settle();

            Assert.That(voice.VocalisationCount, Is.GreaterThan(0),
                "§06's 추격 row reads 발소리+포효. A monster that never roars while chasing has no "
                + "escalation for 정지 to remove.");
            Assert.That(voice.PresenceLevel, Is.GreaterThan(0f));
            Assert.That(steps.Audible, Is.True);

            yield return WalkAlongX(monster.transform, 0f, WalkMetresPerSurface);
            Assert.That(steps.StepCount, Is.GreaterThan(0));

            // 정지.
            voice.State = MonsterStateId.Standstill;
            var vocalisationsAtStop = voice.VocalisationCount;
            var stepsAtStop = steps.StepCount;

            yield return Settle();
            yield return Settle();
            yield return WalkAlongX(monster.transform, 0f, WalkMetresPerSurface * 2f);

            Assert.That(voice.IsAudible, Is.False);
            Assert.That(voice.VocalisationCount, Is.EqualTo(vocalisationsAtStop),
                "§06: 정지 | 멈춰서 듣는다 | 없음. Not quieter — none. A single growl during a "
                + "standstill hands back the position the state exists to take away.");

            Assert.That(steps.Audible, Is.False);
            Assert.That(steps.StepCount, Is.EqualTo(stepsAtStop),
                "The footsteps stop with everything else. §04 gives the 청음사 no other input, so a "
                + "step during 정지 is the mechanic failing outright.");

            Assert.That(voice.PresenceLevel, Is.LessThan(0.02f),
                "The presence bed goes with it. §06 spends a call-out box on 침묵이 가장 무서운 "
                + "소리다, and an atmosphere loop humming through the silence defuses it.");
            Assert.That(voice.BreathLevel, Is.LessThan(0.02f));
        }

        // ====================================================================
        // §12 geometry — a wall has to cost the cue its top end and nothing else.
        // ====================================================================

        [UnityTest]
        public IEnumerator AWallBetweenTheMonsterAndTheEars_LowersTheFilterCorner()
        {
            _listener!.transform.position = new Vector3(0f, 1.6f, 0f);

            var emitter = Spawn("MonsterStep");
            emitter.transform.position = new Vector3(0f, 1.6f, 8f);

            var source = emitter.AddComponent<AudioSource>();
            source.playOnAwake = false;
            GameAudio.Configure(source, AudioBus.Footsteps, true, GameConstants.AudibleRangeMetres);

            var occluder = emitter.AddComponent<SoundOccluder>();
            occluder.SetBus(AudioBus.Footsteps);
            occluder.BaseVolume = 1f;

            var filter = emitter.GetComponent<AudioLowPassFilter>();
            Assert.That(filter, Is.Not.Null, "SoundOccluder adds its own filter; without one it cannot muffle.");

            yield return Settle();

            var openCutoff = filter!.cutoffFrequency;
            var openOcclusion = occluder.Occlusion;

            Assert.That(openOcclusion, Is.LessThan(0.05f),
                "Nothing is in the way yet.");
            Assert.That(openCutoff, Is.GreaterThanOrEqualTo(20000f),
                "A clear path must not colour the cue at all — §12's dry separation is the best "
                + "case §04's alphabet ever gets.");

            // A wall, exactly the way §12 builds them: solid, opaque, between the two.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.position = new Vector3(0f, 1.6f, 4f);
            wall.transform.localScale = new Vector3(8f, 6f, 0.4f);
            _spawned.Add(wall);

            yield return Settle();
            yield return Settle();

            var walledCutoff = filter.cutoffFrequency;

            Assert.That(occluder.Occlusion, Is.GreaterThan(0.9f),
                "A solid wall across the whole aperture is full occlusion, not a doorway.");

            Assert.That(walledCutoff, Is.LessThan(openCutoff * 0.5f),
                "§12's geometry has to reach the mix. An unfiltered footstep makes every room in the "
                + "basement sound like the one the player is standing in.");

            Assert.That(walledCutoff,
                Is.GreaterThanOrEqualTo(AudioTuning.ListenerChannelOcclusionFloorHz - 1f),
                "F-003: the corner stops at the measured identity floor. Below it the five surfaces "
                + "stop separating by 1.4x and the 청음사 can no longer name a floor through a wall — "
                + "which §04 makes the whole role.");

            Assert.That(walledCutoff,
                Is.LessThanOrEqualTo(AudioTuning.ListenerChannelOcclusionFloorHz + 1f),
                "Full occlusion means the floor, exactly. A corner above it would mean the wall cost "
                + "less than the measurement says it does.");

            // And the wall must cost level as well as brightness, gently — F-002 measures
            // the filter alone as already removing 25.8 dB from 자갈.
            Assert.That(occluder.Occlusion * AudioTuning.ListenerChannelOcclusionAttenuationDb,
                Is.GreaterThan(AudioTuning.WorldOcclusionAttenuationDb),
                "§04 makes wall penetration a mechanic for this channel only; stacking a realistic "
                + "broadband loss on top of the filter would delete the role.");
        }

        // ====================================================================
        // The rig — the thing that was actually missing.
        // ====================================================================

        [UnityTest]
        public IEnumerator TheRigBuildsAnAudibleScene_AndTheCensusCanSeeIt()
        {
            FloorSurfaces.Probe = _ => FloorMaterial.Concrete;

            var player = Spawn("Player");
            player.transform.position = Vector3.zero;
            _listener!.transform.SetParent(player.transform, worldPositionStays: false);

            var monster = Spawn("Monster");
            monster.transform.position = new Vector3(0f, 0f, 6f);

            var host = Spawn("[AudioRig]");
            var rig = host.AddComponent<MatchAudioRig>();

            // The three seams the scene fills in. In the solo playtest these arrive
            // deserialised; here they are pushed in after Awake, which is the harder
            // case and the one the Net layer will hit when it spawns a local player.
            rig.SetPlayer(player.transform);
            rig.SetMonster(monster.transform);
            rig.SetLibrary(_library);

            yield return Settle();

            Assert.That(rig.Zones, Is.Not.Null, "§12's zone bed.");
            Assert.That(rig.Threat, Is.Not.Null, "§07's five beds.");
            Assert.That(rig.Heartbeat, Is.Not.Null);
            Assert.That(rig.Danger, Is.Not.Null);
            Assert.That(rig.Cues, Is.Not.Null);
            Assert.That(rig.PlayerSteps, Is.Not.Null);
            Assert.That(rig.MonsterSteps, Is.Not.Null);
            Assert.That(rig.MonsterVoice, Is.Not.Null);
            // DELETED with §04: an assertion that rig.ListenerDriver was not null. Its
            // own message claimed the driver "is now what every runner hears through
            // §12's walls" — it was not. ListenerAudioDriver stepped 청음사's ability
            // and published a fix no code in the project read. The sentence was written
            // to keep a survivor rather than to describe one, which is the exact bug
            // this round exists to end. Occlusion is asserted where it is real: the
            // AudioOcclusion / FootstepAudio path below.

            var census = AudioSceneCensus.Take();

            Assert.That(census.HasListener, Is.True,
                "§05: 3D 오디오는 카메라 기준 → 헤드폰 필수. A scene without an AudioListener is deaf, "
                + "not quiet.");
            Assert.That(census.HasMix, Is.True, "GameAudio owns the bus gains.");
            Assert.That(census.Total, Is.GreaterThan(10),
                "The rig spawns a source per bed layer, per cue channel and per emitter. A handful "
                + "would mean most of the graph never got built.");

            // Every one-shot family the race can still fire has to actually resolve to a clip.
            //
            // Five rows were struck from this list on 2026-08-03 with the systems that raised
            // them — ClueReadSuccess, ClueReadFailed (§03's chain), ShopOpen, LootPickupHeavy
            // (§08's 상점 and 궤짝) and BatteryInsert (the light economy). Nothing in a race
            // plays them; a cue asserted to resolve that nothing can raise is a green number
            // over dead audio, which is the exact shape this file exists to catch.
            Assert.That(rig.Play(AudioCueId.FlashlightOn), Is.True, "§05's F key.");
            Assert.That(rig.Play(AudioCueId.DoorOpen), Is.True,
                "§12-B's 문 is the last interaction the race has, and opening one is the loudest "
                + "thing a runner does on purpose.");
        }

        /// <summary>
        /// The claim the whole change is about, checked against the actual artefact.
        /// <para>
        /// Every other test here builds its own scene, which proves the components work
        /// and proves nothing about the scene a person presses Play on. This one loads
        /// <c>Map_FirstSketch_Solo.unity</c> as the wiring step left it and counts what
        /// is making noise. It is the only test that fails if somebody regenerates the
        /// solo scene and forgets to re-run
        /// <c>HorrorGame ▸ Audio ▸ Build Audible Solo Playtest Scene</c> — which, given
        /// that <c>SoloPlaytest.BuildScene</c> rewrites that file from scratch, is the
        /// way this will actually break.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheSoloPlaytestScene_ComesOutAudible()
        {
#if UNITY_EDITOR
            const string ScenePath = "Assets/Scenes/" + SoloSceneName + ".unity";

            if (!System.IO.File.Exists(ScenePath))
            {
                Assert.Ignore(ScenePath + " has not been built. Run "
                    + "HorrorGame ▸ Audio ▸ Build Audible Solo Playtest Scene.");
            }

            // The scene carries a whole match — a fragmented NavMesh, a storey the
            // creature cannot path across. None of that is
            // this test's business, and letting those errors fail it would make the
            // audio suite red for a bug in the map.
            LogAssert.ignoreFailingMessages = true;

            var parameters = new UnityEngine.SceneManagement.LoadSceneParameters(
                UnityEngine.SceneManagement.LoadSceneMode.Single);

            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ScenePath, parameters);

            yield return null;
            yield return Settle();
            yield return Settle();

            var rig = Object.FindFirstObjectByType<MatchAudioRig>();
            Assert.That(rig, Is.Not.Null,
                "The solo scene has no MatchAudioRig. §05 makes 3D audio the game and §04 gives the "
                + "청음사 nothing else, so a playtest scene with no mix cannot answer §14's questions "
                + "even when everything else in it works.");

            Assert.That(rig!.Library, Is.Not.Null,
                "The rig is there and has no clip set, which is silence with extra steps.");

            var census = AudioSceneCensus.Take();

            Debug.Log("[AudioSceneTests] solo playtest scene:\n" + census.Report);

            Assert.That(census.HasListener, Is.True, "§05: 3D 오디오는 카메라 기준 → 헤드폰 필수.");
            Assert.That(census.HasMix, Is.True);
            Assert.That(census.Total, Is.GreaterThan(20),
                "The mix spawns a source per §07 tier, per heartbeat layer, per zone-bed side, per "
                + "landmark and per emitter. A handful would mean the rig never built its graph.");

            Assert.That(rig.Zones!.CurrentBed, Is.Not.Null,
                "§12's room tone. The player is standing somewhere, and somewhere has a floor.");

            // The empty scene goes back in PutTheSceneBack, not here. Here it only ran when
            // every assertion above it passed, and a whole match inherited by the rest of
            // the run — GameAudio included, which evicts the one SetUp builds — is not a
            // thing that should depend on this test succeeding.
#else
            Assert.Ignore("Editor only — the built scene is reached through EditorSceneManager.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator OpeningADoor_BlindsTheListener_AndTheMixHearsWhy()
        {
            var player = Spawn("Player");
            var noise = player.AddComponent<NoiseMeter>();
            noise.IsLocalPlayer = true;

            var cues = player.AddComponent<AudioCuePlayer>();
            cues.Configure(_library, noise);

            yield return null;

            Assert.That(noise.BlindsListener, Is.False, "Standing still is the quietest thing there is.");

            Assert.That(cues.Play(AudioCueId.DoorOpen), Is.True);
            yield return null;

            Assert.That(noise.BlindsListener, Is.True,
                "§04 names the door specifically — 문을 열면 정보가 끊긴다. A door that plays a clip "
                + "without raising noise is loud and harmless, which is the same as not being a cost.");

            // The duck engages over SelfNoiseDuckSeconds and the transient is held for
            // SelfNoiseTransientSeconds, so this samples inside the hold rather than after it.
            yield return Wait(AudioTuning.SelfNoiseDuckSeconds * 2f);

            Assert.That(GameAudio.SelfBlindness, Is.GreaterThan(0f),
                "§10 prices the role as 소리를 듣는다 / 자기가 조용해야 한다, and a price nobody can "
                + "perceive is not a price. The world has to get quieter and duller, not the HUD "
                + "show an error.");

            Assert.That(GameAudio.CutoffCeilingHz(AudioBus.Monster),
                Is.LessThan(AudioTuning.OcclusionOpenCutoffHz),
                "§04's two prices should sound like one price: being loud costs the Listener the "
                + "same top end a wall would.");

            Assert.That(GameAudio.CutoffCeilingHz(AudioBus.Ambience),
                Is.EqualTo(AudioTuning.OcclusionOpenCutoffHz),
                "Only the informational buses move. Ducking the room tone as well would read as the "
                + "game glitching rather than as the player being loud.");

            // And it has to let go, or one door would end the role for the match.
            yield return Wait(AudioTuning.SelfNoiseTransientSeconds
                + AudioTuning.SelfNoiseDecaySeconds
                + (AudioTuning.SelfNoiseDuckSeconds * 6f));

            Assert.That(noise.BlindsListener, Is.False);
            Assert.That(GameAudio.SelfBlindness, Is.LessThan(0.05f),
                "§04 cuts the feed while the noise lasts. A duck that never released would make the "
                + "first door a permanent cost.");
        }

        [UnityTest]
        public IEnumerator TheHeartbeatFollowsDanger_AndKeepsBeatingThroughAStandstill()
        {
            var player = Spawn("Player");
            _listener!.transform.SetParent(player.transform, worldPositionStays: false);

            var monster = Spawn("Monster");
            monster.transform.position = new Vector3(0f, 0f, 3f);

            var danger = player.AddComponent<DangerSense>();
            danger.Monster = monster.transform;
            danger.MonsterState = MonsterStateId.Chase;

            var heart = player.AddComponent<HeartbeatDirector>();
            heart.Configure(_library!.HeartbeatLow, _library.HeartbeatMid, _library.HeartbeatHigh);

            for (var i = 0; i < 4; i++)
            {
                yield return Settle();
            }

            var chased = heart.HighLevel;
            Assert.That(chased, Is.GreaterThan(0.1f),
                "§06's 추격 at three metres is the maximum this scale has.");

            danger.MonsterState = MonsterStateId.Standstill;
            yield return Settle();

            Assert.That(danger.Danger01, Is.GreaterThan(0f),
                "§06: a heartbeat that dropped to nothing the moment the monster stopped would hand "
                + "the player exactly the information 정지 exists to take away.");

            Assert.That(danger.Danger01, Is.LessThan(DangerSense.Evaluate(MonsterStateId.Chase, 3f)),
                "It is still the quiet state; relief is real.");
        }

        // ====================================================================
        // Helpers.
        // ====================================================================

        /// <summary>
        /// §12's zones as parallel stripes along X, <see cref="WalkMetresPerSurface"/>
        /// wide. Stands in for the map's own <c>IWorldProbe.SampleFloor</c>, which is
        /// what <c>MatchAudioBridge</c> binds in a real match.
        /// </summary>
        private static FloorMaterial FloorAtStripe(Vector3 point, FloorMaterial[] floors)
        {
            var index = Mathf.Clamp(
                Mathf.FloorToInt(point.x / WalkMetresPerSurface), 0, floors.Length - 1);
            return floors[index];
        }

        /// <summary>
        /// Walks a transform along +X at <c>GameConstants.WalkSpeed</c>, one frame at a
        /// time. Speed matters: <see cref="FootstepAudio"/> derives its cadence and its
        /// walk/run choice from measured movement, so teleporting would produce a sprint
        /// and then drop the burst.
        /// </summary>
        private static IEnumerator WalkAlongX(Transform mover, float startX, float metres)
        {
            var position = mover.position;
            position.x = startX;
            mover.position = position;

            var travelled = 0f;
            while (travelled < metres)
            {
                var step = GameConstants.WalkSpeed * Time.deltaTime;
                travelled += step;

                position = mover.position;
                position.x += step;
                mover.position = position;

                yield return null;
            }
        }

        private static IEnumerator Settle()
        {
            return Wait(SettleSeconds);
        }

        /// <summary>
        /// Advances play by a wall-clock duration. Every fade in this layer is an
        /// exponential approach with a time constant, so a test waits multiples of the
        /// constant rather than a fixed frame count — which would be a different
        /// duration on every machine.
        /// </summary>
        private static IEnumerator Wait(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private GameObject Spawn(string objectName)
        {
            var go = new GameObject(objectName);
            _spawned.Add(go);
            return go;
        }

        /// <summary>
        /// Resolves a clip path against the project.
        /// <para>
        /// These are PlayMode tests, so the assembly is a runtime one and
        /// <c>AssetDatabase</c> is only there when the Editor is. That is fine — the
        /// question this suite asks is about the mix, not about how assets get loaded in
        /// a player, and a build that shipped these tests would have bigger problems.
        /// </para>
        /// </summary>
        private static AudioClip? LoadClip(string assetPath)
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
#else
            return null;
#endif
        }
    }
}
