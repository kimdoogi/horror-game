#nullable enable

#if HORRORGAME_STEAMWORKS

using HorrorGame.Steam.Voice;
using Steamworks;
using UnityEngine;

namespace HorrorGame.Steam.SteamworksBackend
{
    /// <summary>
    /// §13's 음성 캡처 + 코덱 row: <c>ISteamUser::GetVoice</c> and
    /// <c>DecompressVoice</c>, and deliberately nothing else.
    /// <para>
    /// This class knows how to turn a microphone into compressed bytes and compressed
    /// bytes back into samples. It does not know who may hear them. §13's cutoff lives in
    /// <c>Voice/VoiceAudience</c> and is applied by the sender and the host before a frame
    /// gets anywhere near a transport — which is what keeps the one part of the pipeline
    /// that is a game rule out of the platform-specific half, and therefore out of a
    /// future port's way.
    /// </para>
    /// <para>
    /// Steam's codec is free with the App ID, including §13's development 480, so voice can
    /// be tested at §14 step 5 long before a store page exists.
    /// </para>
    /// </summary>
    public sealed class SteamworksVoiceBackend : IVoiceBackend
    {
        private bool _capturing;
        private int _sampleRate;

        /// <inheritdoc />
        public bool IsAvailable => true;

        /// <summary>
        /// Steam's optimal rate, cached. Asking per frame would be a native call for a
        /// value that cannot change during a session, and the playback clip's format is
        /// built from it.
        /// </summary>
        public int SampleRate
        {
            get
            {
                if (_sampleRate == 0)
                {
                    _sampleRate = (int)SteamUser.GetVoiceOptimalSampleRate();
                }

                return _sampleRate;
            }
        }

        /// <inheritdoc />
        public bool IsCapturing => _capturing;

        /// <summary>
        /// Opens the microphone. Called only while someone is in range to hear — that is
        /// §13's cutoff at the sender, and it has the side effect that the operating
        /// system's microphone indicator is dark whenever a player is alone.
        /// </summary>
        public void StartCapture()
        {
            if (_capturing)
            {
                return;
            }

            SteamUser.StartVoiceRecording();
            _capturing = true;
        }

        /// <inheritdoc />
        public void StopCapture()
        {
            if (!_capturing)
            {
                return;
            }

            SteamUser.StopVoiceRecording();
            _capturing = false;
        }

        /// <inheritdoc />
        public VoiceReadResult ReadCompressedFrame(byte[] destination, out int byteCount)
        {
            byteCount = 0;

            if (!_capturing)
            {
                return VoiceReadResult.NotRecording;
            }

            if (destination == null || destination.Length == 0)
            {
                return VoiceReadResult.BufferTooSmall;
            }

            // Ask first: Steam only produces data when it hears speech, so the common case
            // is a single cheap call returning k_EVoiceResultNoData. Reading blind would
            // work but would hide the pending size, which is what tells us a buffer needs
            // to grow rather than that the microphone went quiet.
            var availability = SteamUser.GetAvailableVoice(out var pending);

            if (availability == EVoiceResult.k_EVoiceResultNoData || pending == 0U)
            {
                return VoiceReadResult.NoData;
            }

            if (availability != EVoiceResult.k_EVoiceResultOK)
            {
                return Translate(availability);
            }

            if (pending > (uint)destination.Length)
            {
                return VoiceReadResult.BufferTooSmall;
            }

            var read = SteamUser.GetVoice(true, destination, (uint)destination.Length, out var written);

            if (read != EVoiceResult.k_EVoiceResultOK || written == 0U)
            {
                return read == EVoiceResult.k_EVoiceResultOK ? VoiceReadResult.NoData : Translate(read);
            }

            byteCount = (int)written;
            return VoiceReadResult.Frame;
        }

        /// <inheritdoc />
        public bool TryDecompress(byte[] compressed, int compressedBytes, ref byte[] pcmDestination, out int pcmByteCount)
        {
            pcmByteCount = 0;

            if (compressed == null || compressedBytes <= 0)
            {
                return false;
            }

            if (pcmDestination == null || pcmDestination.Length == 0)
            {
                pcmDestination = new byte[VoiceTuning.PcmFrameCapacity];
            }

            var rate = (uint)SampleRate;

            // One retry, after growing. Steam reports the required size only by refusing,
            // so the loop is: try, grow, try again. A second refusal at the ceiling is a
            // dropped frame and a warning rather than an unbounded allocation.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var result = SteamUser.DecompressVoice(
                    compressed,
                    (uint)compressedBytes,
                    pcmDestination,
                    (uint)pcmDestination.Length,
                    out var written,
                    rate);

                if (result == EVoiceResult.k_EVoiceResultOK && written > 0U)
                {
                    pcmByteCount = (int)written;
                    return true;
                }

                if (result != EVoiceResult.k_EVoiceResultBufferTooSmall)
                {
                    return false;
                }

                var grown = pcmDestination.Length * 2;
                if (grown > VoiceTuning.PcmFrameCapacityLimit)
                {
                    Debug.LogWarning("[Voice] Dropping a frame that will not fit in "
                        + VoiceTuning.PcmFrameCapacityLimit + " bytes of PCM.");
                    return false;
                }

                pcmDestination = new byte[grown];
            }

            return false;
        }

        private static VoiceReadResult Translate(EVoiceResult result)
        {
            switch (result)
            {
                case EVoiceResult.k_EVoiceResultNoData:
                    return VoiceReadResult.NoData;
                case EVoiceResult.k_EVoiceResultBufferTooSmall:
                    return VoiceReadResult.BufferTooSmall;
                case EVoiceResult.k_EVoiceResultNotRecording:
                    return VoiceReadResult.NotRecording;
                default:
                    // k_EVoiceResultNotInitialized, Restricted (parental controls),
                    // UnsupportedCodec. All mean "no voice on this account or this build",
                    // and all are permanent for the session rather than worth retrying.
                    return VoiceReadResult.Unavailable;
            }
        }
    }
}

#endif
