#nullable enable

using HorrorGame.Steam;
using UnityEngine;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// Steam's own capture and codec, through the abstraction the Steam layer already
    /// publishes. §13's 음성 캡처 + 코덱 row.
    /// <para>
    /// <b>This class names <c>IVoiceBackend</c> and never <c>Steamworks</c>.</b> The
    /// inversion is the Steam layer's and the reason is
    /// <see cref="SteamBackendRegistry"/>'s: the assembly that references Steamworks.NET
    /// is <c>defineConstraints</c>-gated so Unity skips it on a machine without the
    /// package, and anything that named it directly would inherit that restriction and
    /// stop compiling. <c>SteamServices.Current.Voice</c> is never null and is a
    /// <c>SilentVoiceBackend</c> when there is no codec, which is why there is no null
    /// check and no fallback in this file.
    /// </para>
    /// <para>
    /// The frames are opaque. Nothing here inspects, transcodes or re-frames them — they
    /// go onto the wire exactly as Steam produced them and come back to
    /// <c>DecompressVoice</c> exactly as they arrived, which is also why
    /// <see cref="VoiceCodecId.Steam"/> exists as a distinct id on the packet.
    /// </para>
    /// </summary>
    public sealed class SteamVoiceLine : IVoiceLine
    {
        private readonly IVoiceBackend _backend;

        private byte[] _pcm = new byte[PcmScratchBytes];

        /// <summary>
        /// Initial size of the scratch <c>DecompressVoice</c> writes 16-bit PCM into.
        /// <para>
        /// Steam's optimal rate is around 24 kHz, so 16-bit mono is roughly 48 kB/s and
        /// this is about a third of a second — comfortably more than one frame. The
        /// backend grows it by reference if a future SDK disagrees, so the number is a
        /// starting guess and not a limit.
        /// </para>
        /// </summary>
        private const int PcmScratchBytes = 16 * 1024;

        /// <summary>Wraps the platform's voice backend.</summary>
        /// <param name="backend">Normally <c>SteamServices.Current.Voice</c>.</param>
        public SteamVoiceLine(IVoiceBackend backend)
        {
            _backend = backend;
        }

        /// <inheritdoc />
        public string Name => "Steam";

        /// <inheritdoc />
        public VoiceCodecId Codec => VoiceCodecId.Steam;

        /// <inheritdoc />
        public bool IsAvailable => _backend.IsAvailable;

        /// <inheritdoc />
        public int SampleRate => _backend.SampleRate;

        /// <inheritdoc />
        public bool IsCapturing => _backend.IsCapturing;

        /// <summary>
        /// Always −1: Steam's frames are opaque and the SDK's own encoder already suppresses
        /// silence, so a second gate here would only be a second place to be wrong.
        /// </summary>
        public float LastFrameRms => -1f;

        /// <inheritdoc />
        public void StartCapture() => _backend.StartCapture();

        /// <inheritdoc />
        public void StopCapture() => _backend.StopCapture();

        /// <inheritdoc />
        public int ReadEncodedFrame(byte[] destination)
        {
            if (destination == null)
            {
                return 0;
            }

            var result = _backend.ReadCompressedFrame(destination, out var byteCount);

            if (result == VoiceReadResult.BufferTooSmall)
            {
                // Not silently dropped. §13 measures the compressed stream at 초당 2~8KB,
                // so a frame that will not fit in the caller's buffer means the codec's
                // settings changed under us and the symptom would otherwise be a bad
                // microphone rather than a configuration problem.
                Debug.LogWarning("[Voice] Steam produced a frame larger than " + destination.Length
                                 + " bytes and it was dropped. §13 budgets 초당 2~8KB, so this is a codec "
                                 + "setting that moved, not a large frame.");
                return 0;
            }

            return result == VoiceReadResult.Frame ? byteCount : 0;
        }

        /// <inheritdoc />
        public int Decode(byte[] frame, int frameBytes, float[] destination, int destinationOffset, float gain)
        {
            if (frame == null || destination == null || frameBytes <= 0)
            {
                return 0;
            }

            if (!_backend.TryDecompress(frame, frameBytes, ref _pcm, out var pcmByteCount))
            {
                return 0;
            }

            var samples = pcmByteCount / 2;
            if (samples <= 0 || destinationOffset < 0 || destinationOffset + samples > destination.Length)
            {
                return 0;
            }

            // Steam hands back 16-bit signed mono, little-endian. The rule's gain is
            // applied on the way into float for the reason VoiceCodec.Decode gives: the
            // engine must not add a second, different roll-off on top of §01's linear one.
            for (var i = 0; i < samples; i++)
            {
                var lo = _pcm[i * 2];
                var hi = _pcm[(i * 2) + 1];
                var pcm = (short)(lo | (hi << 8));
                destination[destinationOffset + i] = (pcm / (float)short.MaxValue) * gain;
            }

            return samples;
        }
    }
}
