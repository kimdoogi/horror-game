#nullable enable

using System.Collections.Generic;
using HorrorGame.Audio;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
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
    /// <c>MatchDirector</c>, <c>ClueReader</c> and <c>PlayerFlashlight</c> all expose
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
        private bool _hasPrevious;
        private bool _previousLit;
        private bool _previousOnSurface;
        private bool _previousShopOpen;
        private bool _previousBatteryDead;
        private bool _previousBatteryLow;
        private bool _hasBattery;
        private int _previousSpareCells;
        private ClueReadState _previousClueState = ClueReadState.Idle;
        private bool _hasEconomy;
        private int _previousEarned;
        private int _previousSpent;
        private int _previousLootCount;
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
        /// The zone position is the centroid of that zone's §12 candidate sites. Those
        /// are the markers the generator actually emits and they are by definition
        /// spread through the zone; a room's true centre would be better and nothing in
        /// the scene knows where that is.
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

            rig.PlaceLandmark(map.Entrance, generator: true, label: "surface");

            var byZone = new Dictionary<string, List<Vector3>>();
            var sites = map.CandidateSites;

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

            var onSurface = director.LocalPlayerOnSurface;
            rig.OnSurface = onSurface;

            if (!_hasPrevious)
            {
                _hasPrevious = true;
                _previousOnSurface = onSurface;
                _previousShopOpen = director.ShopOpen;
                _previousClueState = director.ClueReader.State;
                return;
            }

            if (onSurface != _previousOnSurface)
            {
                _previousOnSurface = onSurface;
                rig.Play(onSurface ? AudioCueId.SurfaceReached : AudioCueId.Descend);
            }

            var shopOpen = director.ShopOpen;
            if (shopOpen != _previousShopOpen)
            {
                _previousShopOpen = shopOpen;
                rig.Play(shopOpen ? AudioCueId.ShopOpen : AudioCueId.ShopClose);
            }

            PushClue(rig, director.ClueReader);
            PushEconomy(rig, director);
        }

        /// <summary>
        /// §03's read, as two sounds.
        /// <para>
        /// Both edges matter and they are not symmetrical. The success is the only
        /// confirmation a player gets that the memorising starts now — §03 forbids
        /// writing it down, so there is no artefact to look at. The failure has to be
        /// unmistakable for the opposite reason: §03 says the progress is <em>gone</em>,
        /// not paused, and a player who thinks they are still counting will stand in the
        /// dark believing they are working.
        /// </para>
        /// </summary>
        private void PushClue(MatchAudioRig rig, ClueReader reader)
        {
            var state = reader.State;
            if (state == _previousClueState)
            {
                return;
            }

            var previous = _previousClueState;
            _previousClueState = state;

            if (state == ClueReadState.Complete)
            {
                rig.Play(AudioCueId.ClueReadSuccess);
                return;
            }

            // Only from Reading: dropping out of Idle or out of a completed read is not
            // a failure, and playing the sting for it would teach the player to ignore it.
            if (state == ClueReadState.Interrupted && previous == ClueReadState.Reading)
            {
                rig.Play(AudioCueId.ClueReadFailed);
            }
        }

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

            var battery = state.Battery;

            var dead = battery.IsDead;
            if (dead != _previousBatteryDead)
            {
                _previousBatteryDead = dead;
                if (dead)
                {
                    rig.Play(AudioCueId.BatteryDead);
                }
            }

            // A spare going away is a cell being fitted, and it is the only edge that
            // catches every case: swapping a half-used cell never touches IsDead, and
            // §03's round trip exists precisely so that a player tops up before the dark
            // arrives rather than after.
            if (!_hasBattery)
            {
                _hasBattery = true;
                _previousSpareCells = battery.SpareCells;
            }
            else if (battery.SpareCells < _previousSpareCells)
            {
                _previousSpareCells = battery.SpareCells;
                rig.Play(AudioCueId.BatteryInsert);
            }
            else
            {
                _previousSpareCells = battery.SpareCells;
            }

            var low = !dead && battery.Charge <= AudioTuning.BatteryWarningSeconds;
            if (low != _previousBatteryLow)
            {
                _previousBatteryLow = low;
                if (low)
                {
                    rig.Play(AudioCueId.BatteryLow);
                }
            }
        }

        /// <summary>
        /// §08's loot and the shared wallet, from the two monotone counters the economy
        /// already keeps.
        /// <para>
        /// <c>TotalEarned</c> and <c>TotalSpent</c> only ever rise, so an edge on them is
        /// unambiguous in a way a credit balance is not — selling 40 and buying 40 in the
        /// same frame leaves <c>Credits</c> untouched and both of those counters moved.
        /// </para>
        /// <para>
        /// <b>A refused purchase makes no sound and cannot yet.</b> §08's
        /// <c>PurchaseOutcome</c> is returned to the caller and never stored, so there is
        /// no state to detect the edge in. <c>shop_denied.wav</c> is wired into the cue
        /// table and waiting for whoever owns the shop screen to call
        /// <see cref="MatchAudioRig.Play"/> with it — a sound with no trigger is a
        /// smaller problem than a bridge that guesses.
        /// </para>
        /// </summary>
        private void PushEconomy(MatchAudioRig rig, MatchDirector director)
        {
            var wallet = director.Shop.Wallet;

            if (!_hasEconomy)
            {
                _hasEconomy = true;
                _previousEarned = wallet.TotalEarned;
                _previousSpent = wallet.TotalSpent;
                _previousLootCount = LocalLootCount();
                return;
            }

            if (wallet.TotalEarned > _previousEarned)
            {
                _previousEarned = wallet.TotalEarned;
                rig.Play(AudioCueId.LootSell);
            }

            if (wallet.TotalSpent > _previousSpent)
            {
                _previousSpent = wallet.TotalSpent;
                rig.Play(AudioCueId.ShopPurchase);
            }

            var pockets = PlayerInteractor.Active != null ? PlayerInteractor.Active.Pockets : null;
            var count = pockets != null ? pockets.LootCount : 0;

            if (count > _previousLootCount && pockets != null)
            {
                // The newest piece is the one that was just added; §08's pieces differ by
                // material as well as by weight, and a 궤짝 that sounded like a 은수저
                // would undersell the decision the player just made.
                rig.Play(CueForLoot(pockets.Loot[count - 1]));
            }

            _previousLootCount = count;
        }

        private int LocalLootCount()
        {
            var pockets = PlayerInteractor.Active != null ? PlayerInteractor.Active.Pockets : null;
            return pockets != null ? pockets.LootCount : 0;
        }

        /// <summary>
        /// §08's four pieces, by what they are made of.
        /// <para>
        /// The mapping is the design's own descriptions, not a guess: 은수저 · 잡동사니
        /// is metal, 회중시계 · 반지 is the jewel set, 금고 속 문서 is paper, and
        /// 대형 초상화 · 궤짝 is the heavy wooden one. §08 says the 궤짝 "exists to
        /// create one scene", and it should sound like the piece that does.
        /// </para>
        /// </summary>
        private static AudioCueId CueForLoot(LootId loot)
        {
            switch (loot)
            {
                case LootId.Timepiece:
                    return AudioCueId.LootPickupGlass;
                case LootId.SafeDocument:
                    return AudioCueId.LootPickupPaper;
                case LootId.LargePiece:
                    return AudioCueId.LootPickupHeavy;
                default:
                    return AudioCueId.LootPickupMetal;
            }
        }
    }
}
