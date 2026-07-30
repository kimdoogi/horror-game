#nullable enable

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// A fixed-size float ring buffer between the network and the audio thread.
    /// <para>
    /// It exists because the two ends run on different threads at unrelated rates.
    /// Frames arrive on the main thread whenever the network delivers them — bunched,
    /// late, occasionally not at all — while Unity's streaming
    /// <c>PCMReaderCallback</c> asks for a fixed block on the audio thread and cannot
    /// be made to wait. Every access is therefore locked, and the lock is held for a
    /// straight array copy and nothing else: an audio-thread lock that can block on
    /// anything slower than a memcpy is how a game gets audible glitching under load.
    /// </para>
    /// <para>
    /// When it overflows, the oldest audio is dropped rather than the newest. Voice is
    /// only useful live — §03's whole loop is 기억해서 말로 전달 — so a listener would
    /// rather lose a syllable from half a second ago than fall permanently behind the
    /// speaker.
    /// </para>
    /// </summary>
    public sealed class VoiceJitterBuffer
    {
        private readonly object _gate = new object();
        private readonly float[] _buffer;

        private int _readIndex;
        private int _count;

        /// <summary>Creates a buffer holding <paramref name="capacitySamples"/> samples. Capacity is clamped to at least one.</summary>
        public VoiceJitterBuffer(int capacitySamples)
        {
            _buffer = new float[capacitySamples < 1 ? 1 : capacitySamples];
        }

        /// <summary>Total capacity in samples.</summary>
        public int Capacity => _buffer.Length;

        /// <summary>Samples currently buffered.</summary>
        public int Available
        {
            get
            {
                lock (_gate)
                {
                    return _count;
                }
            }
        }

        /// <summary>Samples dropped to make room. A steadily rising count means the listener is receiving faster than it plays, which is a sample-rate mismatch, not jitter.</summary>
        public int OverflowSamples { get; private set; }

        /// <summary>Samples of silence handed to the audio thread because the buffer was empty. Ordinary between words.</summary>
        public int UnderflowSamples { get; private set; }

        /// <summary>Appends samples, discarding the oldest audio if it does not fit.</summary>
        public void Write(float[] source, int count)
        {
            if (source == null || count <= 0)
            {
                return;
            }

            if (count > source.Length)
            {
                count = source.Length;
            }

            lock (_gate)
            {
                if (count >= _buffer.Length)
                {
                    // A single write larger than the whole buffer: keep its tail, which
                    // is the most recent audio in it.
                    OverflowSamples += _count + (count - _buffer.Length);
                    System.Array.Copy(source, count - _buffer.Length, _buffer, 0, _buffer.Length);
                    _readIndex = 0;
                    _count = _buffer.Length;
                    return;
                }

                var overflow = _count + count - _buffer.Length;
                if (overflow > 0)
                {
                    _readIndex = (_readIndex + overflow) % _buffer.Length;
                    _count -= overflow;
                    OverflowSamples += overflow;
                }

                var writeIndex = (_readIndex + _count) % _buffer.Length;
                var firstChunk = _buffer.Length - writeIndex;
                if (firstChunk > count)
                {
                    firstChunk = count;
                }

                System.Array.Copy(source, 0, _buffer, writeIndex, firstChunk);
                if (firstChunk < count)
                {
                    System.Array.Copy(source, firstChunk, _buffer, 0, count - firstChunk);
                }

                _count += count;
            }
        }

        /// <summary>
        /// Fills <paramref name="destination"/> with up to <paramref name="count"/>
        /// samples, padding with silence, and returns how many were real. Called from
        /// the audio thread.
        /// </summary>
        public int Read(float[] destination, int count)
        {
            if (destination == null || count <= 0)
            {
                return 0;
            }

            if (count > destination.Length)
            {
                count = destination.Length;
            }

            int taken;
            lock (_gate)
            {
                taken = _count < count ? _count : count;

                var firstChunk = _buffer.Length - _readIndex;
                if (firstChunk > taken)
                {
                    firstChunk = taken;
                }

                System.Array.Copy(_buffer, _readIndex, destination, 0, firstChunk);
                if (firstChunk < taken)
                {
                    System.Array.Copy(_buffer, 0, destination, firstChunk, taken - firstChunk);
                }

                _readIndex = (_readIndex + taken) % _buffer.Length;
                _count -= taken;
            }

            if (taken < count)
            {
                UnderflowSamples += count - taken;
                System.Array.Clear(destination, taken, count - taken);
            }

            return taken;
        }

        /// <summary>Drops everything buffered. Used when a speaker goes out of range or leaves.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _readIndex = 0;
                _count = 0;
            }
        }
    }
}
