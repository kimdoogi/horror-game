#nullable enable

using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// A loop pinned to a place — §03's 지상 발전기, §12's per-zone 전기 패널.
    /// <para>
    /// Everything else continuous in this layer follows the player: the zone bed is
    /// whatever floor they are standing on, the tension bed is what time it is, the
    /// heartbeat is theirs. A landmark is the opposite and that is the point. §12 builds
    /// the map to be navigated without light — "어둠 = 목표의 잠금장치" (§03) — and a
    /// hum that stays put is the only thing in the mix a player can take a bearing on
    /// and walk toward. §05 already has the Listener turning their body to triangulate;
    /// this gives them something that does not move while they do it.
    /// </para>
    /// <para>
    /// Occluded and rolled off like any world sound, so a panel two rooms away is
    /// audibly two rooms away. It is on <see cref="AudioBus.Ambience"/> rather than
    /// Items because it is the room rather than an event in it, and because §12's floor
    /// alphabet must win against it — see <see cref="AudioTuning.TrimAmbienceDb"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Ambience Landmark")]
    public sealed class AmbienceLandmark : MonoBehaviour
    {
        [Tooltip("The loop. amb_generator_hum_loop or zone_hum_loop.")]
        [SerializeField]
        private AudioClip? loop;

        [Tooltip("Audible radius, metres. §12 caps a straight corridor at 20 m, so a " +
                 "landmark heard much further than that would sound through the map's " +
                 "own sight-line rules.")]
        [SerializeField]
        private float audibleRange = AudioTuning.DefaultWorldAudibleRange;

        [Tooltip("Level relative to the ambience bus, 0..1.")]
        [SerializeField, Range(0f, 1f)]
        private float level = 1f;

        private AudioBedVoice? _voice;

        /// <summary>The clip this landmark loops, or null.</summary>
        public AudioClip? Loop => loop;

        /// <summary>Current faded level, 0 to 1.</summary>
        public float Level => _voice != null ? _voice.Level : 0f;

        /// <summary>
        /// Wires the landmark from code. Safe after <c>Awake</c>.
        /// </summary>
        /// <param name="clip">The loop.</param>
        /// <param name="rangeMetres">Audible radius, metres.</param>
        /// <param name="level01">Level relative to the ambience bus, 0..1.</param>
        public void Configure(AudioClip? clip, float rangeMetres, float level01)
        {
            loop = clip;
            audibleRange = Mathf.Max(1f, rangeMetres);
            level = Mathf.Clamp01(level01);
            _voice?.SetClip(clip);
        }

        private void Awake()
        {
            _voice = new AudioBedVoice(
                gameObject, "landmark", AudioBus.Ambience, positional: true, audibleRange, occluded: true);
            _voice.SetClip(loop);
        }

        private void OnDisable()
        {
            _voice?.Stop();
        }

        private void Update()
        {
            if (_voice == null)
            {
                return;
            }

            _voice.Target = loop != null ? level : 0f;

            // Faded in rather than started at full: four players walking into the same
            // room would otherwise hear the panel switch on, which reads as a bug in a
            // building whose lights are a mechanic.
            _voice.Tick(Time.deltaTime, AudioTuning.ZoneAmbienceCrossfadeSeconds);
        }
    }
}
