#nullable enable

using System;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// One speaker's decoded audio, between the network thread that writes it and the
    /// audio thread that plays it.
    /// <para>
    /// <b>Single producer, single consumer, and no lock.</b> Frames are written from the
    /// main thread inside Mirror's message handler; samples are read from Unity's audio
    /// thread inside the streaming clip's <c>PCMReaderCallback</c>. A lock on that path
    /// would let a network hitch stall the mixer, which is heard as a click across
    /// <em>every</em> sound in the game rather than as a dropout in one voice. So the
    /// producer only ever moves <see cref="_write"/> and the consumer only ever moves
    /// <see cref="_read"/>, both volatile, and neither ever writes the other's head.
    /// </para>
    /// <para>
    /// <b>Prime before playing.</b> Starting on the first frame that arrives means
    /// underrunning on the second, which sounds like clicking rather than like speech.
    /// <see cref="PrimeSeconds"/> of audio is accumulated first — the cost is that a
    /// talkspurt begins 80 ms late, which is below the threshold at which a listener
    /// notices and far below the 250 ms at which a conversation starts to collide.
    /// </para>
    /// </summary>
    public sealed class VoiceSpeakerStream
    {
        /// <summary>
        /// Seconds the ring holds.
        /// <para>
        /// Long enough to ride out a network hiccup, short enough that recovering from one
        /// does not leave a speaker permanently talking half a second in the past — which
        /// in a race where two runners are arguing about which gate to take is the
        /// difference between advice and noise.
        /// </para>
        /// </summary>
        public const float RingSeconds = 0.5f;

        /// <summary>Audio to accumulate before playback starts, seconds. See the class remarks.</summary>
        public const float PrimeSeconds = 0.08f;

        /// <summary>
        /// Seconds of silence after which the speaker is considered to have stopped and the
        /// stream is torn down. Also how the cutoff sounds when somebody walks out of
        /// range: the frames stop, the tail plays out, and the voice is gone.
        /// </summary>
        public const float IdleTimeoutSeconds = 0.5f;

        private readonly float[] _ring;
        private readonly int _primeSamples;

        private volatile int _write;
        private volatile int _read;
        private volatile bool _playing;

        /// <summary>Builds a ring for one speaker at <paramref name="sampleRate"/>.</summary>
        /// <param name="sampleRate">Hertz the line decodes at.</param>
        public VoiceSpeakerStream(int sampleRate)
        {
            var rate = sampleRate > 0 ? sampleRate : VoiceCodec.SampleRate;
            _ring = new float[Math.Max(VoiceCodec.FrameSamples * 2, (int)(rate * RingSeconds))];
            _primeSamples = Math.Max(VoiceCodec.FrameSamples, (int)(rate * PrimeSeconds));
        }

        /// <summary>Samples the ring can hold.</summary>
        public int Capacity => _ring.Length;

        /// <summary>Total samples ever written. Never wraps in a session's lifetime.</summary>
        public long TotalWritten { get; private set; }

        /// <summary>Samples the consumer asked for and the ring could not supply. Underruns.</summary>
        public int UnderrunSamples { get; private set; }

        /// <summary>How many samples are waiting to be played.</summary>
        public int Buffered
        {
            get
            {
                var pending = _write - _read;
                if (pending < 0)
                {
                    pending += _ring.Length;
                }

                return pending;
            }
        }

        /// <summary>Producer. Appends decoded samples. Main thread only.</summary>
        /// <param name="samples">Source PCM, already attenuated by the rule.</param>
        /// <param name="offset">First sample.</param>
        /// <param name="count">How many.</param>
        public void Write(float[] samples, int offset, int count)
        {
            if (samples == null || count <= 0 || offset < 0 || offset + count > samples.Length)
            {
                return;
            }

            var write = _write;
            for (var i = 0; i < count; i++)
            {
                _ring[write] = samples[offset + i];
                write++;
                if (write == _ring.Length)
                {
                    write = 0;
                }
            }

            _write = write;
            TotalWritten += count;

            if (!_playing && Buffered >= _primeSamples)
            {
                _playing = true;
            }
        }

        /// <summary>
        /// Consumer. Fills <paramref name="destination"/> and advances the read head.
        /// Audio thread only.
        /// </summary>
        /// <param name="destination">Receives samples; the remainder is zeroed.</param>
        public void Read(float[] destination)
        {
            if (destination == null)
            {
                return;
            }

            if (!_playing)
            {
                Array.Clear(destination, 0, destination.Length);
                return;
            }

            var available = Buffered;
            var take = available < destination.Length ? available : destination.Length;

            var read = _read;
            for (var i = 0; i < take; i++)
            {
                destination[i] = _ring[read];
                read++;
                if (read == _ring.Length)
                {
                    read = 0;
                }
            }

            _read = read;

            if (take < destination.Length)
            {
                Array.Clear(destination, take, destination.Length - take);
                UnderrunSamples += destination.Length - take;

                // Re-prime rather than limp along one sample ahead of the writer: an
                // underrun means the stream has fallen behind and playing every arriving
                // frame the instant it lands would underrun again on the next one.
                _playing = false;
            }
        }

        /// <summary>
        /// Reads the most recent <paramref name="count"/> samples <em>without</em>
        /// consuming them, newest last.
        /// <para>
        /// This is what the audio actually was, independent of whether a mixer ever ran —
        /// which is what makes it usable on a headless machine, where Unity's audio thread
        /// may never call <see cref="Read"/> at all. It is a diagnostic in the shipped
        /// build (the debug overlay's level meter) and it is the measurement a test makes
        /// on the far side of the wire.
        /// </para>
        /// </summary>
        /// <param name="destination">Receives the samples.</param>
        /// <param name="count">How many to take. Clamped to what has ever been written.</param>
        /// <returns>How many were copied.</returns>
        public int PeekLatest(float[] destination, int count)
        {
            if (destination == null || count <= 0)
            {
                return 0;
            }

            if (count > destination.Length)
            {
                count = destination.Length;
            }

            if (count > _ring.Length)
            {
                count = _ring.Length;
            }

            if (count > TotalWritten)
            {
                count = (int)TotalWritten;
            }

            var start = _write - count;
            while (start < 0)
            {
                start += _ring.Length;
            }

            for (var i = 0; i < count; i++)
            {
                destination[i] = _ring[(start + i) % _ring.Length];
            }

            return count;
        }
    }
}
