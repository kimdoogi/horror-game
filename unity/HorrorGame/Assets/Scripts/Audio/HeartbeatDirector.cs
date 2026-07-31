#nullable enable

using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// The local player's heartbeat, in three layers crossfaded by
    /// <see cref="DangerSense"/>.
    /// <para>
    /// A heartbeat is the only non-diegetic sound in the game that is allowed to tell
    /// a player something, and it is allowed because it tells them nothing they could
    /// act on precisely. §04 sells 괴물의 위치 to the 청음사 and §11 makes the 관측자's
    /// read unbuyable; a pulse that got faster near the monster would undercut both if
    /// it were sharp enough to navigate by. So it is deliberately blunt — three
    /// layers, a long release, and mixed at the level of the room tone rather than
    /// above it (<see cref="AudioTuning.HeartbeatMatchDb"/>).
    /// </para>
    /// <para>
    /// The rate rises with danger as well as the level. A pulse that only got louder
    /// reads as a volume automation; one that also speeds up reads as a body, and the
    /// two together are what a player notices without being able to say when it
    /// started. §06's 정지 keeps it elevated on purpose — see <see cref="DangerSense"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DangerSense))]
    [AddComponentMenu("HorrorGame/Audio/Heartbeat Director")]
    public sealed class HeartbeatDirector : MonoBehaviour
    {
        [Tooltip("UI/heartbeat_low.wav — always underneath once there is any danger at all.")]
        [SerializeField]
        private AudioClip? low;

        [Tooltip("UI/heartbeat_mid.wav")]
        [SerializeField]
        private AudioClip? mid;

        [Tooltip("UI/heartbeat_high.wav — 추격, or the monster in the same room.")]
        [SerializeField]
        private AudioClip? high;

        [Tooltip("Overall level of the heartbeat family, 0..1, on top of the bus trim.")]
        [SerializeField, Range(0f, 1f)]
        private float level = 1f;

        private DangerSense? _danger;
        private AudioBedVoice? _low;
        private AudioBedVoice? _mid;
        private AudioBedVoice? _high;
        private float _match = 1f;

        /// <summary>Whether the heartbeat plays. Cleared above ground (§03's safe half) and on death.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Current faded level of the quietest layer, 0 to 1. For a test, and for a mix debug view.</summary>
        public float LowLevel => _low != null ? _low.Level : 0f;

        /// <summary>Current faded level of the middle layer, 0 to 1.</summary>
        public float MidLevel => _mid != null ? _mid.Level : 0f;

        /// <summary>Current faded level of the loudest layer, 0 to 1. Reached only in 추격 or close quarters.</summary>
        public float HighLevel => _high != null ? _high.Level : 0f;

        /// <summary>
        /// Wires the three layers from code, for a scene assembled at runtime.
        /// Re-applies to the live voices when called after <c>Awake</c>.
        /// </summary>
        public void Configure(AudioClip? lowLayer, AudioClip? midLayer, AudioClip? highLayer)
        {
            low = lowLayer;
            mid = midLayer;
            high = highLayer;

            _low?.SetClip(lowLayer);
            _mid?.SetClip(midLayer);
            _high?.SetClip(highLayer);
        }

        private void Awake()
        {
            _danger = GetComponent<DangerSense>();
            _match = AudioOcclusion.DecibelsToLinear(AudioTuning.HeartbeatMatchDb);

            _low = new AudioBedVoice(gameObject, "heartbeat_low", AudioBus.Ambience, false);
            _mid = new AudioBedVoice(gameObject, "heartbeat_mid", AudioBus.Ambience, false);
            _high = new AudioBedVoice(gameObject, "heartbeat_high", AudioBus.Ambience, false);

            _low.SetClip(low);
            _mid.SetClip(mid);
            _high.SetClip(high);
        }

        private void OnDisable()
        {
            _low?.Stop();
            _mid?.Stop();
            _high?.Stop();
        }

        private void Update()
        {
            if (_danger == null || _low == null || _mid == null || _high == null)
            {
                return;
            }

            var dt = Time.deltaTime;
            var danger = Active ? _danger.Danger01 : 0f;
            var gain = level * _match;

            // Overlapping ramps rather than exclusive bands: at any danger at least one
            // layer is at full and the next is on its way in, so the pulse never thins
            // out in the middle of a transition.
            var lowWeight = Mathf.Clamp01(Mathf.InverseLerp(0f, AudioTuning.HeartbeatLowThreshold, danger));
            var midWeight = Mathf.Clamp01(Mathf.InverseLerp(AudioTuning.HeartbeatLowThreshold, AudioTuning.HeartbeatMidThreshold, danger));
            var highWeight = Mathf.Clamp01(Mathf.InverseLerp(AudioTuning.HeartbeatMidThreshold, AudioTuning.HeartbeatHighThreshold, danger));

            _low.Target = lowWeight * (1f - (highWeight * 0.5f)) * gain;
            _mid.Target = midWeight * gain;
            _high.Target = highWeight * gain;

            _low.Tick(dt, AudioTuning.HeartbeatCrossfadeSeconds);
            _mid.Tick(dt, AudioTuning.HeartbeatCrossfadeSeconds);
            _high.Tick(dt, AudioTuning.HeartbeatCrossfadeSeconds);

            ApplyRate(danger);
        }

        /// <summary>
        /// Speeds the pulse up with danger. Applied to every layer so a crossfade in
        /// progress does not audibly change tempo halfway through.
        /// </summary>
        private void ApplyRate(float danger)
        {
            var rate = Mathf.Lerp(1f, AudioTuning.HeartbeatMaxRateScale, danger);
            SetPitch(_low, rate);
            SetPitch(_mid, rate);
            SetPitch(_high, rate);
        }

        private static void SetPitch(AudioBedVoice? voice, float rate)
        {
            if (voice == null)
            {
                return;
            }

            voice.Pitch = rate;
        }
    }
}
