#nullable enable

using HorrorGame.Core;
using HorrorGame.Steam.Voice;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// Puts a speaker's voice into the world mix. Step ④ of §13's pipeline, from the
    /// audio layer's side.
    /// <para>
    /// §13 states the trick this rests on outright: "음성을 3D 오디오 소스로 재생하면
    /// 근접 음성이 자동으로 된다. 거리 계산 로직이 필요 없다. 엔진의 3D 오디오가 감쇠와
    /// 벽 차폐를 처리한다." <c>AudioSourceVoiceOutput</c> already does the first half —
    /// a positional, linear-rolloff source on the speaker's character. This adds the
    /// second: 벽 차폐, which the engine does <em>not</em> do by itself. Without an
    /// occluder a teammate two rooms away is as crisp as one in front of you, and the
    /// §12 geometry the whole game is built on stops meaning anything to speech.
    /// </para>
    /// <para>
    /// <b>It does not touch the rolloff.</b> <c>AudioSourceVoiceOutput</c> sets a linear
    /// curve ending exactly at <c>GameConstants.VoiceCutoffDistance</c> and explains
    /// why — anything else leaves a voice faintly audible at the distance transmission
    /// stops, so walking away would cut somebody off mid-word instead of fading them
    /// out. That is a §13 decision belonging to the voice layer, and re-deriving it
    /// here with this layer's own curve would silently break it.
    /// </para>
    /// <para>
    /// Voice gets its own occlusion profile (<see cref="OcclusionProfile.Voice"/>) with
    /// a corner at 2 kHz rather than the Listener channel's 800 Hz. Speech carries its
    /// consonants above 2 kHz, and §03 makes a mechanic out of a remembered number
    /// surviving the trip between two players — "6이었나 9였나". A wall may make a
    /// teammate hard to hear; it may not make them unintelligible.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Proximity Voice Audio")]
    public sealed class ProximityVoiceAudio : MonoBehaviour
    {
        private AudioSourceVoiceOutput? _output;
        private AudioSource? _source;
        private SoundOccluder? _occluder;
        private float _speakingHold;

        /// <summary>True while this character is producing speech, held briefly so a HUD indicator does not flicker on jitter.</summary>
        public bool IsSpeaking { get; private set; }

        /// <summary>How occluded this speaker is, 0 to 1. For a HUD that wants to show why somebody is hard to hear.</summary>
        public float Occlusion => _occluder != null ? _occluder.Occlusion : 0f;

        /// <summary>Samples waiting in the jitter buffer. Diagnostic.</summary>
        public int BufferedSamples => _output != null ? _output.BufferedSamples : 0;

        /// <summary>
        /// §13's transmission cutoff, metres — re-exported so a HUD can draw the range
        /// without importing the voice layer. Read from <c>GameConstants</c>, which is
        /// where the number lives.
        /// </summary>
        public static float CutoffDistance => GameConstants.VoiceCutoffDistance;

        private void Awake()
        {
            // AudioSourceVoiceOutput creates and configures its own AudioSource in
            // Awake, and VoicePlayerLink adds the output on bind. Adding it here as
            // well makes the ordering irrelevant: whichever runs first wins, and the
            // second call is a no-op.
            _output = GetComponent<AudioSourceVoiceOutput>();
            if (_output == null)
            {
                _output = gameObject.AddComponent<AudioSourceVoiceOutput>();
            }

            _source = GetComponent<AudioSource>();

            _occluder = GetComponent<SoundOccluder>();
            if (_occluder == null)
            {
                _occluder = gameObject.AddComponent<SoundOccluder>();
            }

            _occluder.SetBus(AudioBus.Voice);
            _occluder.BaseVolume = 1f;
        }

        private void Start()
        {
            // In Start rather than Awake: GameAudio may be a later object in the same
            // scene, and routing to a bus that does not exist yet would silently leave
            // voice on the default group.
            if (_source != null)
            {
                _source.outputAudioMixerGroup = GameAudio.GroupFor(AudioBus.Voice);
            }
        }

        private void Update()
        {
            if (_output == null)
            {
                return;
            }

            if (_output.IsSpeaking)
            {
                _speakingHold = AudioTuning.VoiceActivityHoldSeconds;
            }
            else if (_speakingHold > 0f)
            {
                _speakingHold -= Time.unscaledDeltaTime;
            }

            IsSpeaking = _speakingHold > 0f;
        }
    }
}
