#nullable enable

using System;
using HorrorGame.Audio;
using HorrorGame.Core.Map;
using HorrorGame.Core.Monster;
using HorrorGame.Core.Threat;
using UnityEngine;

namespace HorrorGame.Gameplay.Audio
{
    /// <summary>
    /// Assembles the whole mix onto a scene that has a player, a monster and a pair of
    /// ears, and drives it from four primitives.
    /// <para>
    /// <b>Why the rig builds the graph in code instead of being a prefab.</b>
    /// <c>SoloPlaytest.BuildScene</c> throws the solo scene away and rebuilds it from the
    /// generated map on every invocation, and the generated map is itself rebuilt
    /// whenever §12's validator is re-run. Anything hand-placed in that scene is
    /// therefore already gone. A component that spawns its own sources survives both,
    /// and — more usefully — it is the same code path a PlayMode test exercises, so
    /// "the scene came out audible" is a claim a test can make rather than something
    /// somebody has to put headphones on to check.
    /// </para>
    /// <para>
    /// <b>It knows nothing about the match.</b> §07's clock, §06's state machine and
    /// §03's round trip all arrive as an int, a bool and a float
    /// (ARCHITECTURE §3 — systems couple through primitives). That is what keeps this
    /// assembly free of <c>MatchDirector</c> and <c>MonsterAgent</c>, and it is why the
    /// test can drive the whole mix without a match running: it sets
    /// <see cref="ElapsedSeconds"/> to 20 minutes and §07's beds cross, exactly as they
    /// would twenty minutes into a real one.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Match Audio Rig")]
    [DefaultExecutionOrder(-50)]
    public sealed class MatchAudioRig : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Everything the match sounds like. Built by HorrorGame ▸ Audio ▸ Rebuild Audio Libraries.")]
        [SerializeField]
        private MatchAudioLibrary? library;

        [Header("Scene")]
        [Tooltip("The local player's body. Left empty, the AudioListener's root is used.")]
        [SerializeField]
        private Transform? player;

        [Tooltip("The monster. Left empty, nothing hunts and the presence bed stays silent.")]
        [SerializeField]
        private Transform? monster;

        [Header("Levels")]
        [Tooltip("Level of the per-zone electrical hum (§12's 전기 패널), 0..1. Low: it is " +
                 "a landmark, not an event, and §12's floor alphabet has to win against it.")]
        [SerializeField, Range(0f, 1f)]
        private float landmarkLevel = 0.5f;

        private GameAudio? _mix;
        private ZoneAmbienceDirector? _zones;
        private ThreatBedDirector? _threat;
        private HeartbeatDirector? _heartbeat;
        private DangerSense? _danger;
        private NoiseMeter? _noise;
        private AudioCuePlayer? _cues;
        private FootstepAudio? _playerSteps;
        private FootstepAudio? _monsterSteps;
        private MonsterAudio? _monsterAudio;
        private ListenerAudioDriver? _listenerDriver;

        private int _announcedTier = -1;
        private bool _built;

        /// <summary>
        /// Seconds since the match began. §07's only input to the mix, and the reason
        /// the tension bed is always mid-transition — see
        /// <see cref="ThreatBedDirector"/>.
        /// </summary>
        public float ElapsedSeconds { get; set; }

        /// <summary>
        /// Whether the local player is above ground. §03's round trip: the surface is
        /// the safe half and must not sound like the basement, so the zone bed swaps and
        /// the heartbeat stops.
        /// </summary>
        public bool OnSurface { get; set; }

        /// <summary>
        /// Whether §07 lets the team read the clock — on the surface, or holding a
        /// 회중시계. Gates the tier stinger, because §08 sells the watch that removes
        /// the cost of not knowing and a free announcement would refund it.
        /// </summary>
        public bool TimeIsReadable { get; set; }

        /// <summary>
        /// The monster's §06 state. Feeding it here is what makes 정지 silent: the state
        /// reaches <see cref="MonsterAudio"/>, which stops the bed, the breath, the
        /// vocalisations and the footsteps together.
        /// </summary>
        public MonsterStateId MonsterState { get; set; } = MonsterStateId.Patrol;

        /// <summary>Whether §04's role is active on the local player. §11 leaves one of the five out every match.</summary>
        public bool LocalPlayerIsListener
        {
            get { return _listenerDriver != null && _listenerDriver.IsListener; }
            set
            {
                if (_listenerDriver != null)
                {
                    _listenerDriver.IsListener = value;
                }
            }
        }

        /// <summary>The clip set. Null means the whole mix is silent, which is a wiring bug, not a state.</summary>
        public MatchAudioLibrary? Library => library;

        /// <summary>The bus gains. Created here when the scene has none, so a mix always exists.</summary>
        public GameAudio? Mix => _mix;

        /// <summary>The zone bed. §12's material map, heard.</summary>
        public ZoneAmbienceDirector? Zones => _zones;

        /// <summary>§07's five beds.</summary>
        public ThreatBedDirector? Threat => _threat;

        /// <summary>The local player's heartbeat.</summary>
        public HeartbeatDirector? Heartbeat => _heartbeat;

        /// <summary>How much trouble the local player is in, 0 to 1.</summary>
        public DangerSense? Danger => _danger;

        /// <summary>The local player's own noise. §04's price.</summary>
        public NoiseMeter? Noise => _noise;

        /// <summary>Every one-shot in the game.</summary>
        public AudioCuePlayer? Cues => _cues;

        /// <summary>The local player's footsteps.</summary>
        public FootstepAudio? PlayerSteps => _playerSteps;

        /// <summary>The monster's footsteps — §04's actual input.</summary>
        public FootstepAudio? MonsterSteps => _monsterSteps;

        /// <summary>§06's presence, breath and vocalisations.</summary>
        public MonsterAudio? MonsterVoice => _monsterAudio;

        /// <summary>§04's 청음사, stepped against Core's own ability.</summary>
        public ListenerAudioDriver? ListenerDriver => _listenerDriver;

        /// <summary>
        /// Points the mix at the monster. Called on spawn, and with null on despawn —
        /// at which point danger decays rather than snapping to zero, because §06's
        /// release is not an event the player gets told about.
        /// </summary>
        public void SetMonster(Transform? value)
        {
            monster = value;

            if (_danger != null)
            {
                _danger.Monster = value;
            }

            AttachMonsterAudio(value);
        }

        /// <summary>Points the mix at the local player's body.</summary>
        public void SetPlayer(Transform? value)
        {
            player = value;
            AttachPlayerAudio(value);
        }

        /// <summary>
        /// Swaps the clip set and re-applies it to every director already built.
        /// <para>
        /// Needed because the graph is assembled in <c>Awake</c>: anything that spawns
        /// this rig from code rather than deserialising it from a scene — a test, or the
        /// Net layer spawning a local player — has no chance to fill the field first.
        /// </para>
        /// </summary>
        public void SetLibrary(MatchAudioLibrary? value)
        {
            library = value;

            _zones?.Configure(
                value != null ? value.Surfaces : null,
                value != null ? value.SurfaceBed : null,
                CollectIncidental());

            _threat?.Configure(
                value != null ? value.TensionBeds : null,
                value != null ? value.TierStingers : null);

            _heartbeat?.Configure(
                value != null ? value.HeartbeatLow : null,
                value != null ? value.HeartbeatMid : null,
                value != null ? value.HeartbeatHigh : null);

            _cues?.Configure(value, _noise);
            _playerSteps?.Configure(value != null ? value.Surfaces : null, FootstepActor.PlayerWalk, _noise);
            _monsterSteps?.Configure(value != null ? value.Surfaces : null, FootstepActor.MonsterStep, null);
            _monsterAudio?.Configure(value, _monsterSteps);
        }

        /// <summary>
        /// Makes §12's material map the same answer the rules get.
        /// <para>
        /// Assign the map layer's own sampler — <c>IWorldProbe.SampleFloor</c>, bridged
        /// to world space. Until it is assigned, <see cref="FloorSurfaces"/> falls back
        /// to a raycast, which can disagree with the map; F-002 already names a HUD that
        /// disagrees with the ears as the thing that ends §04's role, and this is the
        /// same disagreement one layer down.
        /// </para>
        /// </summary>
        public void SetFloorProbe(Func<Vector3, FloorMaterial>? probe)
        {
            FloorSurfaces.Probe = probe;
        }

        /// <summary>Plays a one-shot at the local player. §04's noise cost is applied with it.</summary>
        public bool Play(AudioCueId cue)
        {
            return _cues != null && _cues.Play(cue);
        }

        /// <summary>Plays a one-shot somewhere else in the world — a door across the zone, a 소음 함정.</summary>
        public bool PlayAt(AudioCueId cue, Vector3 position)
        {
            return _cues != null && _cues.PlayAt(cue, position);
        }

        /// <summary>§04's 섬광수 landing. Routed here so the monster's own emitter plays it.</summary>
        public void PlayMonsterStun()
        {
            _monsterAudio?.PlayStun();
        }

        /// <summary>The kill. §06's table has no state for it, so it is an event.</summary>
        public void PlayMonsterGrab()
        {
            _monsterAudio?.PlayGrab();
        }

        /// <summary>
        /// Builds the graph. Idempotent, and called from <c>Awake</c> — public so a
        /// test can stand the mix up without waiting a frame for the engine to do it.
        /// </summary>
        public void Build()
        {
            if (_built)
            {
                return;
            }

            _built = true;

            if (library == null)
            {
                // Nothing assigned. Rather than come up silent — the one failure this
                // layer must never have — fall back to the wired asset by name. See
                // MatchAudioLibrary.ResourceName for why it lives in Resources.
                library = MatchAudioLibrary.LoadDefault();

                if (library == null)
                {
                    Debug.LogWarning(
                        "[MatchAudioRig] No MatchAudioLibrary assigned and none in Resources. The "
                        + "match will be silent — §05 makes 3D audio the game and §04 gives the "
                        + "청음사 nothing else. Run HorrorGame ▸ Audio ▸ Rebuild Audio Libraries.",
                        this);
                }
            }

            EnsureMix();

            var ears = GameAudio.ListenerTransform;
            if (player == null && ears != null)
            {
                player = ears.root;
            }

            BuildGlobalDirectors();
            AttachPlayerAudio(player);
            AttachMonsterAudio(monster);
        }

        private void Awake()
        {
            Build();
        }

        private void Update()
        {
            if (_zones != null)
            {
                _zones.IsOnSurface = OnSurface;
            }

            if (_threat != null)
            {
                _threat.ElapsedSeconds = ElapsedSeconds;

                // §07's bed keeps running above ground — the night does not pause while
                // the team shops — but the tier announcement only fires where §07 says
                // the hour can be known at all.
                var tier = ThreatCurve.TierIndexAt(ElapsedSeconds);
                if (tier != _announcedTier)
                {
                    _announcedTier = tier;
                    _threat.AnnounceTier(tier, TimeIsReadable);
                }
            }

            if (_danger != null)
            {
                _danger.MonsterState = MonsterState;
            }

            if (_heartbeat != null)
            {
                // §03's surface is the half of the loop where nothing is hunting. A
                // heartbeat there would make the safe half feel unsafe, and the round
                // trip only works if the two halves feel different.
                _heartbeat.Active = !OnSurface;
            }

            if (_monsterAudio != null)
            {
                _monsterAudio.State = MonsterState;
            }
        }

        private void EnsureMix()
        {
            if (GameAudio.Instance != null)
            {
                _mix = GameAudio.Instance;
                return;
            }

            _mix = gameObject.GetComponent<GameAudio>();
            if (_mix == null)
            {
                _mix = gameObject.AddComponent<GameAudio>();
            }
        }

        private void BuildGlobalDirectors()
        {
            _zones = Ensure<ZoneAmbienceDirector>(gameObject);
            _zones.Configure(
                library != null ? library.Surfaces : null,
                library != null ? library.SurfaceBed : null,
                CollectIncidental());

            _threat = Ensure<ThreatBedDirector>(gameObject);
            _threat.Configure(
                library != null ? library.TensionBeds : null,
                library != null ? library.TierStingers : null);
        }

        private AudioClip?[] CollectIncidental()
        {
            var bank = library != null ? library.Incidental : null;
            if (bank == null)
            {
                return Array.Empty<AudioClip?>();
            }

            var clips = new AudioClip?[bank.Count];
            for (var i = 0; i < clips.Length; i++)
            {
                clips[i] = bank.At(i);
            }

            return clips;
        }

        private void AttachPlayerAudio(Transform? body)
        {
            if (body == null)
            {
                return;
            }

            var host = body.gameObject;

            _noise = Ensure<NoiseMeter>(host);
            _noise.IsLocalPlayer = true;

            _danger = Ensure<DangerSense>(host);
            _danger.Monster = monster;

            _heartbeat = Ensure<HeartbeatDirector>(host);
            _heartbeat.Configure(
                library != null ? library.HeartbeatLow : null,
                library != null ? library.HeartbeatMid : null,
                library != null ? library.HeartbeatHigh : null);

            _cues = Ensure<AudioCuePlayer>(host);
            _cues.Configure(library, _noise);

            _listenerDriver = Ensure<ListenerAudioDriver>(host);

            // A child object rather than the body itself: FootstepAudio requires an
            // AudioSource, and the body may already carry one belonging to another
            // system. Two components fighting over one source is a footstep that cuts
            // off a door, which §04 reads as a missing step.
            var feet = ChildNamed(body, "audio_footsteps");
            _playerSteps = Ensure<FootstepAudio>(feet);
            _playerSteps.Configure(
                library != null ? library.Surfaces : null, FootstepActor.PlayerWalk, _noise);
        }

        private void AttachMonsterAudio(Transform? body)
        {
            if (body == null)
            {
                return;
            }

            var feet = ChildNamed(body, "audio_footsteps");
            _monsterSteps = Ensure<FootstepAudio>(feet);
            _monsterSteps.Configure(
                library != null ? library.Surfaces : null, FootstepActor.MonsterStep, null);

            _monsterAudio = Ensure<MonsterAudio>(body.gameObject);
            _monsterAudio.Configure(library, _monsterSteps);
            _monsterAudio.State = MonsterState;
        }

        /// <summary>
        /// Places a landmark loop. §03's 발전기 and §12's per-zone 전기 패널 — the only
        /// continuous sounds in the mix that stay put while the player moves.
        /// </summary>
        /// <param name="where">World position.</param>
        /// <param name="generator">True for §03's surface generator, false for a §12 zone panel.</param>
        /// <param name="label">
        /// Distinguishing suffix for the object's name — §12's zone letter, normally.
        /// It only reaches the hierarchy and the census, and that is the point: five
        /// identically named hums are five things nobody can tell apart in the one
        /// report that exists to say which sound is coming from where.
        /// </param>
        public AmbienceLandmark? PlaceLandmark(Vector3 where, bool generator, string label)
        {
            var clip = library == null
                ? null
                : generator ? library.GeneratorLoop : library.ZoneHumLoop;

            if (clip == null)
            {
                return null;
            }

            var host = new GameObject(
                (generator ? "audio_landmark_generator" : "audio_landmark_zone") + "_" + label);
            host.transform.SetParent(transform, worldPositionStays: false);
            host.transform.position = where;

            var landmark = host.AddComponent<AmbienceLandmark>();
            landmark.Configure(clip, AudioTuning.DefaultWorldAudibleRange, generator ? 1f : landmarkLevel);
            return landmark;
        }

        private static GameObject ChildNamed(Transform parent, string childName)
        {
            var existing = parent.Find(childName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var child = new GameObject(childName);
            child.transform.SetParent(parent, worldPositionStays: false);
            return child;
        }

        private static T Ensure<T>(GameObject host)
            where T : Component
        {
            var component = host.GetComponent<T>();
            return component != null ? component : host.AddComponent<T>();
        }
    }
}
