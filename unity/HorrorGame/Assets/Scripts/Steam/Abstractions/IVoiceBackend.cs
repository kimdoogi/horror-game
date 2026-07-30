#nullable enable

namespace HorrorGame.Steam
{
    /// <summary>What came back from a capture read.</summary>
    public enum VoiceReadResult
    {
        /// <summary>Nothing to send. The normal result: the codec only produces data when it hears speech.</summary>
        NoData,

        /// <summary>A frame was written to the destination buffer.</summary>
        Frame,

        /// <summary>Capture is not running. A caller bug — read after stopping.</summary>
        NotRecording,

        /// <summary>No codec on this backend. Offline builds report this once and stop asking.</summary>
        Unavailable,

        /// <summary>
        /// The destination buffer was too small for a pending frame, and the frame is
        /// still pending. Distinguished from <see cref="NoData"/> because dropping
        /// frames silently sounds exactly like a bad microphone.
        /// </summary>
        BufferTooSmall,
    }

    /// <summary>
    /// §13's 음성 캡처 + 코덱 row — microphone capture, compression and
    /// decompression, and nothing else.
    /// <para>
    /// The proximity rule is <b>not</b> here on purpose. §13's voice pipeline is four
    /// steps (캡처 → 압축 전송 → 압축해제 → 3D 오디오 소스), and only the first and
    /// third are platform work. Keeping the cutoff out of the backend is what stops
    /// a future port from having to re-implement the one part of the pipeline that is
    /// a game rule — see <see cref="Voice.VoiceAudience"/>.
    /// </para>
    /// <para>
    /// Buffers are supplied by the caller and reused. Voice runs at tens of frames a
    /// second per speaker for a whole match; allocating a <c>byte[]</c> per frame
    /// would hand the GC a steady drip of garbage during the exact moments — a chase
    /// — when a hitch is least forgivable.
    /// </para>
    /// </summary>
    public interface IVoiceBackend
    {
        /// <summary>
        /// Whether this backend has a codec at all. False offline: §14 step 3 says
        /// 음성은 디스코드 until step 5, so an offline build genuinely has no in-game
        /// voice rather than a broken one.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Sample rate, in hertz, that <see cref="TryDecompress"/> produces. Also the
        /// rate a playback clip must be created at. Zero when unavailable.
        /// </summary>
        int SampleRate { get; }

        /// <summary>Whether the microphone is open right now.</summary>
        bool IsCapturing { get; }

        /// <summary>
        /// Opens the microphone. Idempotent. Called only when someone is close enough
        /// to hear — see <see cref="Voice.VoiceTransmitter"/> — which also means the
        /// OS microphone indicator stays off when nobody could receive anything.
        /// </summary>
        void StartCapture();

        /// <summary>Closes the microphone. Idempotent.</summary>
        void StopCapture();

        /// <summary>
        /// Drains at most one compressed frame into <paramref name="destination"/>.
        /// <paramref name="byteCount"/> is zero unless the result is
        /// <see cref="VoiceReadResult.Frame"/>.
        /// </summary>
        VoiceReadResult ReadCompressedFrame(byte[] destination, out int byteCount);

        /// <summary>
        /// Decompresses one frame to 16-bit signed mono PCM at
        /// <see cref="SampleRate"/>.
        /// <para>
        /// <paramref name="pcmDestination"/> is passed by reference so the backend can
        /// grow it when a frame does not fit, instead of forcing every caller to guess
        /// a worst case. The grown array is returned to the caller to keep and reuse.
        /// </para>
        /// </summary>
        bool TryDecompress(byte[] compressed, int compressedBytes, ref byte[] pcmDestination, out int pcmByteCount);
    }
}
