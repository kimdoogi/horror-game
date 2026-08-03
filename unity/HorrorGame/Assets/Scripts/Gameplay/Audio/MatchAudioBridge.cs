#nullable enable

using System.Collections.Generic;
using HorrorGame.Audio;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using UnityEngine;

namespace HorrorGame.Gameplay.Audio
{
    /// <summary>
    /// Turns the match into the handful of primitives <see cref="MatchAudioRig"/> takes,
    /// and fires §03 · §08's one-shots off the edges in its state.
    /// <para>
    /// <b>Why it polls instead of subscribing.</b> Nothing in the match raises events —
    /// <c>MatchDirector</c> and <c>PlayerFlashlight</c> both expose
    /// state and no notifications. Adding events would mean editing four systems this
    /// change does not own, and each new event would be a second place the same fact is
    /// written down. An edge detector over the state they already publish keeps the
    /// authority in one place: if the mix and the HUD ever disagree about whether a clue
    /// read succeeded, they are reading the same property and one of them has a bug,
    /// rather than the game having raised two different truths.
    /// </para>
    /// <para>
    /// <b>It is the only file in this change that knows what a match is.</b> The rig
    /// deliberately does not, so that it stays testable without one (ARCHITECTURE §3).
    /// That is also why this lives in the predefined assembly rather than beside the
    /// rig: <c>MatchDirector</c> is there, an asmdef cannot reference it, and pretending
    /// otherwise would have meant moving somebody else's file.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(40)]
    [AddComponentMenu("HorrorGame/Audio/Match Audio Bridge")]
    public sealed class MatchAudioBridge : MonoBehaviour
    {
        [Tooltip("The mix. Left empty, the one in the scene is found.")]
        [SerializeField]
        private MatchAudioRig? _rig;

        [Tooltip("§01's loop. Left empty, the one in the scene is found.")]
        [SerializeField]
        private MatchDirector? _director;

        [Tooltip("§06's antagonist. Left empty, the one in the scene is found.")]
        [SerializeField]
        private MonsterAgent? _monster;

        [Tooltip("The local player's beam. Left empty, the one in the scene is found.")]
        [SerializeField]
        private PlayerFlashlight? _flashlight;

        [Tooltip("The local player's camera body. Left empty, the one in the scene is found. Optional.")]
        [SerializeField]
        private PlayerViewMotion? _viewMotion;

        private MonsterStateId _previousMonsterState = MonsterStateId.Patrol;
        private bool _hasMonsterState;
        private bool _previousLit;
        // DELETED: _hasPrevious, _previousOnSurface, _previousShopOpen,
        // _previousBatteryDead, _previousBatteryLow, _hasBattery, _hasEconomy,
        // _previousSpareCells, _previousEarned, _previousSpent, _previousLootCount.
        // Eleven edge-detector fields for eleven cues nothing can raise any more:
        // 지상, the shop, the battery and the wallet. _previousLit stays — the torch
        // still has a switch.
        private bool _floorProbeBound;
        private bool _landmarksPlaced;

        private void Awake()
        {
            if (_rig == null)
            {
                _rig = FindFirstObjectByType<MatchAudioRig>();
            }

            if (_director == null)
            {
                _director = FindFirstObjectByType<MatchDirector>();
            }

            if (_monster == null)
            {
                _monster = FindFirstObjectByType<MonsterAgent>();
            }

            if (_flashlight == null)
            {
                _flashlight = FindFirstObjectByType<PlayerFlashlight>();
            }

            if (_viewMotion == null)
            {
                _viewMotion = FindFirstObjectByType<PlayerViewMotion>();
            }

            if (_rig != null && _monster != null)
            {
                _rig.SetMonster(_monster.transform);
            }
        }

        private void Update()
        {
            var rig = _rig;
            if (rig == null)
            {
                return;
            }

            BindFloorProbe(rig);
            PlaceLandmarks(rig);
            PushMonster(rig);
            PushViewMotion(rig);
            PushMatch(rig);
            PushFlashlight(rig);
        }

        /// <summary>
        /// Gives the player's body the two things §06 lets it know about the monster,
        /// and nothing else.
        /// <para>
        /// <b>This lives here rather than in <c>MatchDirector</c> on purpose.</b> Both
        /// signals already exist in this file for the mix, and routing the camera's
        /// copy through the same objects makes it impossible for the two to disagree:
        /// <see cref="PlayerViewMotion.Dread01"/> is literally
        /// <c>DangerSense.Danger01</c>, the number the heartbeat is crossfaded by, so
        /// the camera cannot become a sharper sense than the sound. <c>DangerSense</c>'s
        /// own remarks are the argument for why a blunt proximity signal is allowed at
        /// all — §04 sells the monster's position to the 청음사 and §11 makes the
        /// 관측자's read unbuyable — and that argument only covers a signal this coarse.
        /// </para>
        /// <para>
        /// The flinch fires on the edge into 추격, the same edge <c>MonsterAcquireTell</c>
        /// flares the creature's crest on. §06 makes that transition the most
        /// consequential event in a match and the person it happens to is usually
        /// running the other way, so the announcement has to reach a surface they are
        /// definitely looking at.
        /// </para>
        /// </summary>
        private void PushViewMotion(MatchAudioRig rig)
        {
            var viewMotion = _viewMotion;
            if (viewMotion == null)
            {
                return;
            }

            var danger = rig.Danger;
            viewMotion.Dread01 = danger != null ? danger.Danger01 : 0f;

            if (_monster == null)
            {
                return;
            }

            var state = _monster.State;
            if (_hasMonsterState && state == MonsterStateId.Chase && _previousMonsterState != MonsterStateId.Chase)
            {
                viewMotion.Flinch();
            }

            _previousMonsterState = state;
            _hasMonsterState = true;
        }

        /// <summary>
        /// Pins §03's 발전기 to the entrance and §12's 전기 패널 hum to each zone.
        /// <para>
        /// Done at runtime rather than baked into the scene for the same reason the rig
        /// is: <c>SoloPlaytest.BuildScene</c> rewrites the scene from the map, so
        /// anything placed by hand is gone the next time somebody regenerates. Doing it
        /// here also means one implementation of "where is a zone" instead of two.
        /// </para>
        /// <para>
        /// <b>The zone position used to be the centroid of that zone's §12 후보 지점</b>,
        /// which were deleted with §03. On the descent tower a 구역 IS a storey — 
        /// <c>DescentMap.Build</c> calls <c>AddZone</c> once per level — and the creature
        /// starts are exactly one per storey, carrying the zone name, so
        /// <c>MonsterSpawns</c> gives one point per zone by construction.
        /// </para>
        /// <para>
        /// It is a worse centroid and an honest one. A creature start is the place
        /// furthest by walking from the finish, so it sits out on the rim rather than in
        /// the middle of the floor. That matters less than it sounds: the hum's job is to
        /// say WHICH storey you are on, it carries
        /// <c>AudioTuning.DefaultWorldAudibleRange</c>, and a 57.5 m storey is inside it
        /// from anywhere. If a floor ever wants a hum at its middle, the finish-style
        /// marker is the thing to add — not a second opinion computed here.
        /// </para>
        /// </summary>
        private void PlaceLandmarks(MatchAudioRig rig)
        {
            if (_landmarksPlaced || _director == null)
            {
                return;
            }

            var map = _director.Map;
            if (map == null)
            {
                return;
            }

            _landmarksPlaced = true;

            // DELETED with the light economy: the generator landmark. It played
            // AmbienceLibrary.GeneratorLoop at map.Entrance — the 지상 발전기 that recharged
            // §03's cells, which is why it was the loudest thing on the surface and why it
            // sat at the way out. There is no surface, no generator and no cell.

            var byZone = new Dictionary<string, List<Vector3>>();
            var sites = map.MonsterSpawns;

            for (var i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                if (site == null)
                {
                    continue;
                }

                var key = ZoneKeyOf(site.name);
                if (!byZone.TryGetValue(key, out var bucket))
                {
                    bucket = new List<Vector3>();
                    byZone[key] = bucket;
                }

                bucket.Add(site.position);
            }

            foreach (var pair in byZone)
            {
                var sum = Vector3.zero;
                for (var i = 0; i < pair.Value.Count; i++)
                {
                    sum += pair.Value[i];
                }

                rig.PlaceLandmark(sum / pair.Value.Count, generator: false, label: pair.Key);
            }
        }

        /// <summary>
        /// The zone letter out of a generated marker name — the same rule
        /// <c>MatchMap</c> groups its own floors by, so a hum cannot land in a zone §12
        /// does not recognise. The generator writes
        /// <c>&lt;kind&gt;_&lt;zone name&gt;_&lt;node&gt;</c> and §12's zone names are
        /// "A 나무", "B 타일" and so on.
        /// </summary>
        private static string ZoneKeyOf(string markerName)
        {
            var underscore = markerName.IndexOf('_');
            if (underscore < 0 || underscore + 1 >= markerName.Length)
            {
                return markerName;
            }

            var rest = markerName.Substring(underscore + 1);
            var space = rest.IndexOf(' ');
            return space > 0 ? rest.Substring(0, space) : rest;
        }

        /// <summary>
        /// Points <see cref="FloorSurfaces"/> at the map's own sampler.
        /// <para>
        /// This is the single most important line in the file. §12 makes the floor
        /// material a gameplay channel and F-002 names a HUD that disagrees with the
        /// ears as the thing that ends §04's role — so the mix must answer "what is
        /// underfoot" with the <em>same</em> <c>IWorldProbe</c> the monster's brain and
        /// the Listener's ability read. The raycast fallback in
        /// <see cref="FloorSurfaces.Sample"/> can disagree with the map, and a footstep
        /// that names the wrong zone is not a wrong sound, it is a wrong answer.
        /// </para>
        /// <para>
        /// Deferred to <c>Update</c> rather than done in <c>Awake</c> because the probe
        /// does not exist until <c>MonsterAgent.Initialize</c> has run, which
        /// <c>MatchDirector</c> does when the match begins.
        /// </para>
        /// </summary>
        private void BindFloorProbe(MatchAudioRig rig)
        {
            if (_floorProbeBound || _monster == null)
            {
                return;
            }

            var probe = _monster.Probe;
            if (probe == null)
            {
                return;
            }

            _floorProbeBound = true;
            rig.SetFloorProbe(point => probe.SampleFloor(point.ToVec3()));
        }

        private void PushMonster(MatchAudioRig rig)
        {
            if (_monster == null)
            {
                return;
            }

            // §06's state is the whole reason 정지 is silent. It reaches MonsterAudio,
            // which stops the bed, the breath, the vocalisations and the footsteps
            // together — one state, one silence.
            rig.MonsterState = _monster.State;
        }

        private void PushMatch(MatchAudioRig rig)
        {
            var director = _director;
            if (director == null)
            {
                return;
            }

            rig.ElapsedSeconds = director.Clock.ElapsedSeconds;
            rig.TimeIsReadable = director.Clock.IsTimeReadable;

            // DELETED with §01's 지상 and §08's shop: the 표면 edge (surface_reached /
            // descend), the shop open/close edge, and the two calls below them. There is no
            // surface to come back up to — the race starts on the rim of B1 and ends in the
            // middle of B8 — and no shop to open. A cue that no state can raise is dead
            // audio wearing a green test.
        }
        // DELETED with §03: PushClue(MatchAudioRig, ClueReader). It turned the read state
        // machine into two sounds — clue_read_success on Complete, clue_read_failed when a
        // read in progress was interrupted. There is nothing to read.

        private void PushFlashlight(MatchAudioRig rig)
        {
            var flashlight = _flashlight;
            if (flashlight == null)
            {
                return;
            }

            var state = flashlight.State;
            var lit = state.IsLit;

            if (lit != _previousLit)
            {
                _previousLit = lit;
                rig.Play(lit ? AudioCueId.FlashlightOn : AudioCueId.FlashlightOff);
            }

            // DELETED with the light economy: everything below the on/off edge. Three
            // cues hung off BatteryState — battery_dead when the charge ran out,
            // battery_insert on a spare going away (the only edge that caught a half-used
            // cell being swapped), and battery_low under AudioTuning.BatteryWarningSeconds.
            //
            // The torch has no cell, so the switch is the only thing left that can make a
            // sound, and it is the whole of §03 that survives here: light is free, and
            // being seen is what it costs.
        }
    }
}
