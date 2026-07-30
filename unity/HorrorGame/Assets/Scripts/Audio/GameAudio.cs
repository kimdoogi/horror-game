#nullable enable

using System;
using HorrorGame.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace HorrorGame.Audio
{
    /// <summary>
    /// The mix. One per session: it owns the bus gains, the optional
    /// <see cref="AudioMixer"/> routing, and the two global states that bend the
    /// whole mix — §04's self-noise blindness and a settings change.
    /// <para>
    /// <b>It works with or without a mixer asset.</b> That is not a hedge, it is the
    /// only arrangement that keeps this layer buildable independently of whoever
    /// authors assets: an <see cref="AudioMixer"/> is an editor-created asset with no
    /// runtime constructor, so a code layer that requires one cannot be brought up on
    /// its own. When <see cref="mixer"/> is assigned, groups and exposed parameters
    /// do the work and <see cref="Gain"/> returns unity. When it is not, the same
    /// numbers are applied per-source through <see cref="ApplyVolume"/>. Both paths
    /// read the same constants, so the mix does not change when the asset arrives.
    /// </para>
    /// <para>
    /// Every method is safe to call with no instance in the scene. A component that
    /// wants to make a sound should never have to check whether the mixer exists yet
    /// — it asks for a gain, gets the tuned default, and plays.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Game Audio")]
    public sealed class GameAudio : MonoBehaviour
    {
        private const int BusCount = 7;

        /// <summary>
        /// Group path per bus inside the mixer asset, and the stem of its exposed
        /// volume parameter. Index is <see cref="AudioBus"/>.
        /// </summary>
        private static readonly string[] BusNames =
        {
            "Master", "Footsteps", "Monster", "Ambience", "Items", "Interface", "Voice",
        };

        private static readonly float[] BusTrimDb =
        {
            AudioTuning.TrimMasterDb,
            AudioTuning.TrimFootstepsDb,
            AudioTuning.TrimMonsterDb,
            AudioTuning.TrimAmbienceDb,
            AudioTuning.TrimItemsDb,
            AudioTuning.TrimInterfaceDb,
            AudioTuning.TrimVoiceDb,
        };

        [Header("Routing")]
        [Tooltip("Optional. When set, buses route to groups named Master/<Bus> and " +
                 "volumes are written to exposed parameters named Vol<Bus>. When null, " +
                 "the same gains are applied per AudioSource instead.")]
        [SerializeField]
        private AudioMixer? mixer;

        [Header("Occlusion")]
        [Tooltip("Layers that block sound. Should be the same geometry that blocks " +
                 "IWorldProbe.HasLineOfSight, or the mix will disagree with the rules.")]
        [SerializeField]
        private LayerMask occluderMask = ~0;

        [Header("Player volumes, 0..1")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float footstepsVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float monsterVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float itemsVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float interfaceVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;

        private readonly float[] _userVolume = new float[BusCount];
        private readonly float[] _smoothedVolume = new float[BusCount];
        private readonly AudioMixerGroup?[] _groups = new AudioMixerGroup?[BusCount];

        private float _selfBlindness;
        private float _selfBlindnessTarget;
        private bool _groupsResolved;

        /// <summary>The live mix, or null before one is placed in the scene.</summary>
        public static GameAudio? Instance { get; private set; }

        /// <summary>
        /// Where the ears are. Used by <see cref="SoundOccluder"/> to aim its rays and
        /// by the directors to measure proximity.
        /// <para>
        /// Resolved from the scene's <see cref="AudioListener"/> rather than from the
        /// local player, because §05 pins the listener to the camera — "3D 오디오는
        /// 카메라 기준 → 헤드폰 필수" — and the camera is not always parented to the
        /// body (§09's ghost sees the whole map).
        /// </para>
        /// </summary>
        public static Transform? ListenerTransform
        {
            get
            {
                if (_cachedListener != null)
                {
                    return _cachedListener;
                }

                var listener = FindFirstObjectByType<AudioListener>();
                _cachedListener = listener != null ? listener.transform : null;
                return _cachedListener;
            }
        }

        private static Transform? _cachedListener;

        /// <summary>
        /// How blinded the local Listener is by their own noise, 0 to 1. §04: "자기가
        /// 소리를 내면 못 듣는다."
        /// <para>
        /// The rule itself is Core's — <c>ListenerAbility</c> cuts the feed and returns
        /// <c>AbilityFailure.SelfNoise</c>. This is the same fact expressed in the mix,
        /// so the player hears <em>why</em> the readout went away instead of reading an
        /// error message: the world gets quieter and duller while they are the loudest
        /// thing in it. §10 lists the price as 소리를 듣는다 / 자기가 조용해야 한다, and
        /// a price you cannot perceive is not a price.
        /// </para>
        /// </summary>
        public static float SelfBlindness => Instance != null ? Instance._selfBlindness : 0f;

        /// <summary>Layers treated as blocking sound. Everything, when there is no instance.</summary>
        public static LayerMask OccluderMask => Instance != null ? Instance.occluderMask : (LayerMask)(~0);

        /// <summary>
        /// Linear gain a source on <paramref name="bus"/> should multiply its own
        /// volume by. Includes the bus trim, the player's slider, the master slider and
        /// the self-noise duck — everything except distance and occlusion, which belong
        /// to the source.
        /// <para>
        /// Returns 1 for the trim portion when a mixer asset is routing, so the two
        /// paths never double-apply. It still carries the duck, because a duck that
        /// only worked with an asset present would be a mechanic that switched itself
        /// off in a test scene.
        /// </para>
        /// </summary>
        public static float Gain(AudioBus bus)
        {
            var instance = Instance;
            if (instance == null)
            {
                return AudioOcclusion.DecibelsToLinear(BusTrimDb[(int)bus]) * DuckOf(bus, 0f);
            }

            return instance.GainOf(bus);
        }

        /// <summary>
        /// Upper bound, hertz, on a source's low-pass corner for this bus. Normally
        /// transparent; drops to
        /// <see cref="AudioTuning.SelfNoiseDuckCutoffHz"/> on the informational buses
        /// while the local Listener is self-blinded, so their own noise costs them the
        /// same top end a wall would.
        /// </summary>
        public static float CutoffCeilingHz(AudioBus bus)
        {
            var blindness = SelfBlindness;
            if (blindness <= 0f || !IsInformational(bus))
            {
                return AudioTuning.OcclusionOpenCutoffHz;
            }

            return Mathf.Exp(Mathf.Lerp(
                Mathf.Log(AudioTuning.OcclusionOpenCutoffHz),
                Mathf.Log(AudioTuning.SelfNoiseDuckCutoffHz),
                blindness));
        }

        /// <summary>
        /// Routes a source to its bus and applies the project's 3D settings.
        /// <paramref name="maxDistance"/> is ignored for a flat source.
        /// <para>
        /// Call once at spawn. It does not set volume — that is
        /// <see cref="ApplyVolume"/>, which has to run whenever the mix moves.
        /// </para>
        /// </summary>
        public static void Configure(AudioSource? source, AudioBus bus, bool positional, float maxDistance)
        {
            if (source == null)
            {
                return;
            }

            source.outputAudioMixerGroup = GroupFor(bus);

            if (positional)
            {
                RolloffCurves.ApplyPositional(source, maxDistance);
            }
            else
            {
                RolloffCurves.ApplyFlat(source);
            }
        }

        /// <summary>
        /// Sets a source's volume from its own 0..1 level and its bus. The one place
        /// the fallback path is applied, so a caller never has to know whether a mixer
        /// asset exists.
        /// </summary>
        public static void ApplyVolume(AudioSource? source, AudioBus bus, float level01)
        {
            if (source == null)
            {
                return;
            }

            source.volume = Mathf.Max(0f, level01) * Gain(bus);
        }

        /// <summary>The mixer group for a bus, or null when no mixer asset is routing.</summary>
        public static AudioMixerGroup? GroupFor(AudioBus bus)
        {
            var instance = Instance;
            if (instance == null)
            {
                return null;
            }

            instance.ResolveGroups();
            return instance._groups[(int)bus];
        }

        /// <summary>
        /// Sets the player's volume for a bus, 0 to 1. Smoothed, so a settings slider
        /// does not click.
        /// </summary>
        public static void SetUserVolume(AudioBus bus, float volume01)
        {
            if (Instance == null)
            {
                return;
            }

            Instance._userVolume[(int)bus] = Mathf.Clamp01(volume01);
        }

        /// <summary>The player's current volume for a bus, 0 to 1.</summary>
        public static float GetUserVolume(AudioBus bus)
        {
            return Instance != null ? Instance._userVolume[(int)bus] : 1f;
        }

        /// <summary>
        /// Reports the local Listener's own noise on the 0~1 scale
        /// <c>GameConstants.ListenerSelfNoiseThreshold</c> is quoted against. Called
        /// every frame by <see cref="NoiseMeter"/>.
        /// <para>
        /// The mix ramps in from the threshold rather than switching at it: the rule is
        /// a hard gate (Core returns a fix or it does not), but a hard gate in the mix
        /// would make walking-into-running sound like a bug. The two are consistent
        /// because the audible ramp only starts once the gate has already closed.
        /// </para>
        /// </summary>
        public static void ReportSelfNoise(float noise01)
        {
            if (Instance == null)
            {
                return;
            }

            var threshold = GameConstants.ListenerSelfNoiseThreshold;
            var over = Mathf.InverseLerp(threshold, 1f, noise01);
            Instance._selfBlindnessTarget = noise01 > threshold ? Mathf.Max(0.5f, over) : 0f;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameAudio] A second mix was enabled; destroying it. The bus gains are global.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            _cachedListener = null;

            _userVolume[(int)AudioBus.Master] = masterVolume;
            _userVolume[(int)AudioBus.Footsteps] = footstepsVolume;
            _userVolume[(int)AudioBus.Monster] = monsterVolume;
            _userVolume[(int)AudioBus.Ambience] = ambienceVolume;
            _userVolume[(int)AudioBus.Items] = itemsVolume;
            _userVolume[(int)AudioBus.Interface] = interfaceVolume;
            _userVolume[(int)AudioBus.Voice] = voiceVolume;

            for (var i = 0; i < BusCount; i++)
            {
                _smoothedVolume[i] = _userVolume[i];
            }

            ResolveGroups();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                _cachedListener = null;
            }
        }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;

            for (var i = 0; i < BusCount; i++)
            {
                _smoothedVolume[i] = Approach(_smoothedVolume[i], _userVolume[i], dt, AudioTuning.BusVolumeSmoothingSeconds);
            }

            _selfBlindness = Approach(_selfBlindness, _selfBlindnessTarget, dt, AudioTuning.SelfNoiseDuckSeconds);

            if (mixer != null)
            {
                PushToMixer();
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            _userVolume[(int)AudioBus.Master] = masterVolume;
            _userVolume[(int)AudioBus.Footsteps] = footstepsVolume;
            _userVolume[(int)AudioBus.Monster] = monsterVolume;
            _userVolume[(int)AudioBus.Ambience] = ambienceVolume;
            _userVolume[(int)AudioBus.Items] = itemsVolume;
            _userVolume[(int)AudioBus.Interface] = interfaceVolume;
            _userVolume[(int)AudioBus.Voice] = voiceVolume;
        }

        private float GainOf(AudioBus bus)
        {
            var index = (int)bus;
            var user = _smoothedVolume[index] * (bus == AudioBus.Master ? 1f : _smoothedVolume[(int)AudioBus.Master]);
            var duck = DuckOf(bus, _selfBlindness);

            // With a mixer asset the trim is a group volume, so applying it here as
            // well would square it.
            var trim = mixer != null ? 1f : AudioOcclusion.DecibelsToLinear(BusTrimDb[index]);
            return trim * user * duck;
        }

        private void PushToMixer()
        {
            if (mixer == null)
            {
                return;
            }

            for (var i = 0; i < BusCount; i++)
            {
                var bus = (AudioBus)i;
                var user = _smoothedVolume[i];
                var duck = DuckOf(bus, _selfBlindness);
                var linear = Mathf.Clamp01(user) * duck;
                var decibels = BusTrimDb[i] + AudioOcclusion.LinearToDecibels(linear);
                mixer.SetFloat("Vol" + BusNames[i], decibels);
            }
        }

        private void ResolveGroups()
        {
            if (_groupsResolved || mixer == null)
            {
                _groupsResolved = true;
                return;
            }

            _groupsResolved = true;

            for (var i = 0; i < BusCount; i++)
            {
                var path = i == 0 ? BusNames[0] : BusNames[0] + "/" + BusNames[i];
                var matches = mixer.FindMatchingGroups(path);
                if (matches != null && matches.Length > 0)
                {
                    _groups[i] = matches[0];
                    continue;
                }

                Debug.LogWarning(
                    "[GameAudio] The mixer has no group at \"" + path + "\". That bus falls back to "
                    + "per-source gain, which sounds the same but cannot be ducked as a group.", this);
            }
        }

        /// <summary>
        /// The self-noise duck, per bus. §04 takes the monster away from a noisy
        /// Listener, so only the two buses that carry the monster move; ducking
        /// ambience or interface as well would read as the game glitching rather than
        /// as the player being loud.
        /// </summary>
        private static float DuckOf(AudioBus bus, float blindness)
        {
            if (blindness <= 0f || !IsInformational(bus))
            {
                return 1f;
            }

            return AudioOcclusion.DecibelsToLinear(AudioTuning.SelfNoiseDuckDb * blindness);
        }

        private static bool IsInformational(AudioBus bus)
        {
            return bus == AudioBus.Footsteps || bus == AudioBus.Monster;
        }

        /// <summary>
        /// Exponential approach with a time constant, framerate independent. Used
        /// everywhere in this layer so that every fade behaves the same way at 30 fps
        /// and at 240.
        /// </summary>
        internal static float Approach(float current, float target, float deltaSeconds, float seconds)
        {
            if (seconds <= 0f || deltaSeconds <= 0f)
            {
                return target;
            }

            var k = 1f - Mathf.Exp(-deltaSeconds / Math.Max(1e-4f, seconds));
            return current + ((target - current) * k);
        }
    }
}
