#nullable enable

namespace HorrorGame.Steam.Offline
{
    /// <summary>
    /// The voice backend for a build with no codec.
    /// <para>
    /// This is the one capability with no honest local substitute. §13's voice pipeline
    /// starts at <c>ISteamUser::GetVoice()</c> and the codec is Steam's; without it
    /// there is no compressed frame format, and inventing one would mean two peers on
    /// different backends could not understand each other — a bug that only appears at
    /// step 5 when it is expensive.
    /// </para>
    /// <para>
    /// So it reports itself unavailable and stays quiet, which is exactly what §14 asks
    /// for: step 3's prototype validation is done with 음성은 디스코드, and in-game
    /// proximity voice does not arrive until step 5 with App ID 480. The pipeline above
    /// it still runs — the gate is computed, the microphone state machine is exercised,
    /// the transport counts refusals — so nothing here is a dead end, and nothing throws.
    /// </para>
    /// </summary>
    public sealed class SilentVoiceBackend : IVoiceBackend
    {
        /// <inheritdoc />
        public bool IsAvailable => false;

        /// <summary>Zero: there is no stream, so there is no rate. Callers must not create a playback clip from this.</summary>
        public int SampleRate => 0;

        /// <inheritdoc />
        public bool IsCapturing { get; private set; }

        /// <summary>
        /// Records that capture was requested without opening a microphone. The state is
        /// tracked rather than ignored so that the transmitter's start/stop logic — the
        /// part that implements §13's sender-side cutoff — is exercised in offline
        /// testing instead of being dead code until step 5.
        /// </summary>
        public void StartCapture()
        {
            IsCapturing = true;
        }

        /// <inheritdoc />
        public void StopCapture()
        {
            IsCapturing = false;
        }

        /// <inheritdoc />
        public VoiceReadResult ReadCompressedFrame(byte[] destination, out int byteCount)
        {
            byteCount = 0;
            return VoiceReadResult.Unavailable;
        }

        /// <summary>
        /// Always false. Nothing can arrive, because no peer on this backend can produce
        /// a frame; a frame that did arrive would be from a Steam-enabled peer speaking a
        /// codec this build does not have, and guessing at it would produce noise rather
        /// than speech.
        /// </summary>
        public bool TryDecompress(byte[] compressed, int compressedBytes, ref byte[] pcmDestination, out int pcmByteCount)
        {
            pcmByteCount = 0;
            return false;
        }
    }
}
