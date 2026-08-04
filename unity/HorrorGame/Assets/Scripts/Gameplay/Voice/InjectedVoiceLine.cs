#nullable enable

using System;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// A line whose microphone is a caller. The seam a headless machine injects at.
    /// <para>
    /// <b>Why this is shipped code and not test code.</b> A batch-mode Unity has no
    /// capture device, so <see cref="MicrophoneVoiceLine.IsAvailable"/> is false and every
    /// test of the voice path would otherwise be a test of a system that refused to start
    /// — which is the shape of an assertion that passes without proving anything, and this
    /// repository has shipped several. <see cref="Push"/> puts a known waveform exactly
    /// where the driver's samples would land, so everything downstream of it — the codec,
    /// the audience rule, the socket, the far side's attenuation — is the real thing being
    /// measured. It encodes with <see cref="VoiceCodec"/> and reports
    /// <see cref="VoiceCodecId.Adpcm"/>, so a receiver cannot tell an injected frame from
    /// a spoken one and does not have a branch for it.
    /// </para>
    /// <para>
    /// It is also the honest place to put a future "play a file into the game" debug tool,
    /// which is the other reason it is not hidden in a fixture.
    /// </para>
    /// </summary>
    public sealed class InjectedVoiceLine : IVoiceLine
    {
        /// <summary>
        /// Frames the queue holds before it starts dropping the oldest.
        /// <para>
        /// Sixteen is 320 ms at <see cref="VoiceCodec.FrameMilliseconds"/> — long enough
        /// that a caller can push a burst and let the drain catch up over a few frames,
        /// short enough that a caller who pushes and never drains cannot grow this without
        /// bound. Dropping the oldest rather than the newest matches what a real driver
        /// does when its ring wraps.
        /// </para>
        /// </summary>
        public const int QueueCapacityFrames = 16;

        private readonly byte[][] _queue = new byte[QueueCapacityFrames][];
        private readonly float[] _pending = new float[VoiceCodec.FrameSamples];

        private readonly float[] _levels = new float[QueueCapacityFrames];

        private int _head;
        private int _count;
        private int _pendingSamples;
        private bool _capturing;

        /// <summary>Builds the queue's frame buffers up front, so pushing allocates nothing.</summary>
        public InjectedVoiceLine()
        {
            for (var i = 0; i < _queue.Length; i++)
            {
                _queue[i] = new byte[VoiceCodec.FrameBytes];
            }
        }

        /// <inheritdoc />
        public string Name => "Injected";

        /// <inheritdoc />
        public VoiceCodecId Codec => VoiceCodecId.Adpcm;

        /// <inheritdoc />
        public bool IsAvailable => true;

        /// <inheritdoc />
        public int SampleRate => VoiceCodec.SampleRate;

        /// <inheritdoc />
        public bool IsCapturing => _capturing;

        /// <inheritdoc />
        public float LastFrameRms { get; private set; } = -1f;

        /// <summary>Encoded frames the queue dropped because nothing drained it. Diagnostic.</summary>
        public int DroppedFrames { get; private set; }

        /// <summary>Frames waiting to be drained.</summary>
        public int QueuedFrames => _count;

        /// <inheritdoc />
        public void StartCapture() => _capturing = true;

        /// <inheritdoc />
        public void StopCapture()
        {
            _capturing = false;

            // The queue is cleared with the microphone, not kept. §13's sender-side cutoff
            // is "produce nothing when nobody can hear", and a queue that survived the
            // close would let audio captured while somebody was in range leak out one tick
            // after they left it.
            _head = 0;
            _count = 0;
            _pendingSamples = 0;
        }

        /// <summary>
        /// Feeds PCM in as if a driver had captured it. Whole frames are encoded and
        /// queued; a partial tail is held until the next push completes it.
        /// <para>
        /// Ignored while the microphone is closed, because that is what a real device does
        /// and because a test that could push into a closed line would not be testing the
        /// cutoff.
        /// </para>
        /// </summary>
        /// <param name="samples">−1~1 PCM at <see cref="VoiceCodec.SampleRate"/>.</param>
        /// <param name="offset">First sample to take.</param>
        /// <param name="count">How many.</param>
        /// <returns>How many whole frames were encoded and queued.</returns>
        public int Push(float[] samples, int offset, int count)
        {
            if (!_capturing || samples == null || count <= 0)
            {
                return 0;
            }

            if (offset < 0 || offset + count > samples.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            var framed = 0;
            var read = offset;
            var left = count;

            while (left > 0)
            {
                var take = VoiceCodec.FrameSamples - _pendingSamples;
                if (take > left)
                {
                    take = left;
                }

                Array.Copy(samples, read, _pending, _pendingSamples, take);
                _pendingSamples += take;
                read += take;
                left -= take;

                if (_pendingSamples < VoiceCodec.FrameSamples)
                {
                    break;
                }

                _pendingSamples = 0;

                if (_count == _queue.Length)
                {
                    // Full: the oldest frame is the one a driver would already have
                    // overwritten, so it is the one that goes.
                    DroppedFrames++;
                    _head = (_head + 1) % _queue.Length;
                    _count--;
                }

                var slot = (_head + _count) % _queue.Length;
                _count++;

                // Measured per queued frame rather than once per push: the caller may hand
                // over a burst spanning a word and a pause, and the gate downstream judges
                // frames, not pushes.
                _levels[slot] = VoiceCodec.Rms(_pending, 0, VoiceCodec.FrameSamples);
                VoiceCodec.Encode(_pending, 0, _queue[slot]);
                framed++;
            }

            return framed;
        }

        /// <inheritdoc />
        public int ReadEncodedFrame(byte[] destination)
        {
            if (!_capturing || destination == null || _count == 0)
            {
                return 0;
            }

            if (destination.Length < VoiceCodec.FrameBytes)
            {
                return 0;
            }

            Array.Copy(_queue[_head], 0, destination, 0, VoiceCodec.FrameBytes);
            LastFrameRms = _levels[_head];
            _head = (_head + 1) % _queue.Length;
            _count--;

            return VoiceCodec.FrameBytes;
        }

        /// <inheritdoc />
        public int Decode(byte[] frame, int frameBytes, float[] destination, int destinationOffset, float gain) =>
            VoiceCodec.Decode(frame, frameBytes, destination, destinationOffset, gain);
    }
}
