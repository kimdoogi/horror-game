#nullable enable

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// Converts the codec's output into what Unity's audio pipeline wants.
    /// <para>
    /// Steam decompresses to 16-bit signed little-endian mono PCM; Unity's streaming
    /// clips take normalised floats. That is the entire gap, and it is worth one
    /// well-named function rather than an inline loop in the playback component,
    /// because the sign handling is the sort of thing that produces a stream that
    /// sounds like distorted static and gets blamed on the microphone.
    /// </para>
    /// </summary>
    public static class VoicePcm
    {
        /// <summary>
        /// Divisor taking a signed 16-bit sample to −1..1. 32768 rather than 32767 so
        /// the negative extreme maps to exactly −1 and no sample can exceed unity,
        /// which is what would clip.
        /// </summary>
        private const float SampleScale = 32768f;

        /// <summary>
        /// Decodes <paramref name="byteCount"/> bytes of 16-bit PCM into
        /// <paramref name="destination"/>, growing it if needed, and returns the sample
        /// count. An odd byte count is truncated: half a sample is not recoverable and
        /// reading past it would read whatever the buffer held last.
        /// </summary>
        public static int ToFloatSamples(byte[] pcm, int byteCount, ref float[] destination)
        {
            if (pcm == null || byteCount <= 1)
            {
                return 0;
            }

            if (byteCount > pcm.Length)
            {
                byteCount = pcm.Length;
            }

            var samples = byteCount / 2;

            if (destination == null || destination.Length < samples)
            {
                destination = new float[samples];
            }

            for (var i = 0; i < samples; i++)
            {
                var low = pcm[i * 2];
                var high = pcm[(i * 2) + 1];
                var value = (short)(low | (high << 8));
                destination[i] = value / SampleScale;
            }

            return samples;
        }
    }
}
