#nullable enable

using UnityEngine;

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// Step ④ of §13's pipeline: a speaker's voice, played from a 3D
    /// <see cref="AudioSource"/> on that speaker's character.
    /// <para>
    /// Put this on a player character alongside <see cref="VoicePlayerLink"/>. It plays
    /// a streaming <see cref="AudioClip"/> whose reader callback drains a
    /// <see cref="VoiceJitterBuffer"/>, so the audio thread never waits for the
    /// network. §13's 핵심 트릭 does the rest: because the source is positional,
    /// attenuation and wall occlusion are the engine's problem and proximity voice
    /// needs no distance code here.
    /// </para>
    /// <para>
    /// The rolloff is <em>linear</em> and ends exactly at
    /// <c>GameConstants.VoiceCutoffDistance</c>, matching §13's transmission cutoff.
    /// That agreement matters: with the engine's default logarithmic curve a voice is
    /// still faintly audible at the distance where transmission stops, so walking away
    /// from a speaker would cut them off mid-word instead of fading them out. The same
    /// number therefore governs both ends of the pipeline, and it is read from
    /// <see cref="VoiceAudience.CutoffDistance"/> rather than copied.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioSourceVoiceOutput : MonoBehaviour, IPositionalVoiceOutput
    {
        private AudioSource? _source;
        private AudioClip? _clip;
        private VoiceJitterBuffer? _buffer;
        private float[] _scratch = new float[VoiceTuning.PcmFrameCapacity / 2];

        private int _sampleRate;
        private int _primeSamples;
        private float _silenceSeconds;
        private bool _speaking;

        /// <summary>Samples waiting to be played. Zero while the speaker is silent.</summary>
        public int BufferedSamples => _buffer?.Available ?? 0;

        /// <summary>Whether this output is currently producing sound.</summary>
        public bool IsSpeaking => _speaking;

        /// <summary>Samples dropped because playback fell behind. See <see cref="VoiceJitterBuffer.OverflowSamples"/>.</summary>
        public int OverflowSamples => _buffer?.OverflowSamples ?? 0;

        /// <inheritdoc />
        public void Configure(int sampleRate)
        {
            if (sampleRate <= 0 || sampleRate == _sampleRate)
            {
                return;
            }

            EnsureSource();

            _sampleRate = sampleRate;
            _primeSamples = Mathf.Max(1, (int)(sampleRate * VoiceTuning.PlaybackPrimeSeconds));
            _buffer = new VoiceJitterBuffer((int)(sampleRate * VoiceTuning.PlaybackBufferSeconds));

            StopPlayback();

            if (_clip != null)
            {
                Destroy(_clip);
            }

            // One second of loop length: long enough that the reader callback is asked
            // for sensible block sizes, short enough that a clip carries no meaningful
            // memory cost. The audio is never actually stored in it — stream:true means
            // every block comes from the callback below.
            _clip = AudioClip.Create(
                "voice_" + name,
                sampleRate,
                1,
                sampleRate,
                true,
                OnAudioRead);

            if (_source != null)
            {
                _source.clip = _clip;
            }
        }

        /// <inheritdoc />
        public void SubmitPcm(byte[] pcm, int byteCount)
        {
            if (_buffer == null || byteCount <= 0)
            {
                return;
            }

            var samples = VoicePcm.ToFloatSamples(pcm, byteCount, ref _scratch);
            if (samples <= 0)
            {
                return;
            }

            _buffer.Write(_scratch, samples);
            _silenceSeconds = 0f;

            if (!_speaking && _buffer.Available >= _primeSamples)
            {
                StartPlayback();
            }
        }

        /// <inheritdoc />
        public void ResetOutput()
        {
            _buffer?.Clear();
            StopPlayback();
        }

        private void Awake()
        {
            EnsureSource();
        }

        private void OnDestroy()
        {
            if (_clip != null)
            {
                Destroy(_clip);
                _clip = null;
            }
        }

        private void Update()
        {
            if (!_speaking)
            {
                return;
            }

            _silenceSeconds += Time.unscaledDeltaTime;

            if (_silenceSeconds >= VoiceTuning.PlaybackIdleTimeoutSeconds && BufferedSamples == 0)
            {
                // Frames stopped arriving: the speaker went quiet, left, or crossed
                // §13's cutoff. Stopping rather than looping silence means a later
                // frame primes the buffer again instead of playing into a stale tail.
                StopPlayback();
            }
        }

        private void EnsureSource()
        {
            if (_source != null)
            {
                return;
            }

            _source = GetComponent<AudioSource>();

            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = VoiceTuning.PlaybackFullVolumeRadius;
            _source.maxDistance = VoiceAudience.CutoffDistance;

            // Voice must not be pitch-shifted by movement: §05's sprint is fast enough
            // for Doppler to be audible, and a chase is exactly when speech has to stay
            // intelligible.
            _source.dopplerLevel = 0f;
        }

        private void StartPlayback()
        {
            if (_source == null || _clip == null)
            {
                return;
            }

            _speaking = true;
            _silenceSeconds = 0f;
            _source.Play();
        }

        private void StopPlayback()
        {
            _speaking = false;
            _silenceSeconds = 0f;

            if (_source != null && _source.isPlaying)
            {
                _source.Stop();
            }
        }

        /// <summary>
        /// Unity's streaming clip callback. Runs on the audio thread, so it does
        /// nothing but drain the ring buffer — which pads with silence on underrun.
        /// </summary>
        private void OnAudioRead(float[] data)
        {
            var buffer = _buffer;
            if (buffer == null)
            {
                System.Array.Clear(data, 0, data.Length);
                return;
            }

            buffer.Read(data, data.Length);
        }
    }
}
