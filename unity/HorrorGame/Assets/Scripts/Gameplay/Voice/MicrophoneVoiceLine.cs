#nullable enable

using UnityEngine;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// Unity's <c>Microphone</c> plus <see cref="VoiceCodec"/>. The line a session with
    /// no Steam runs on — §14 steps 1–3, CI, and any contributor who has never installed
    /// the client.
    /// <para>
    /// <b>Why this exists at all when Steam has a voice API.</b> The same argument
    /// <see cref="HorrorGame.Net.NetTransportRegistry"/> makes for the transport: §14
    /// orders the work so that "Mirror 로컬 호스트 — 같은 PC 2인스턴스" happens two steps
    /// before Steam enters the plan, and a voice system that only worked on Steam would be
    /// a voice system nobody could develop against. The selection rule is the transport's,
    /// not a second one invented here — see <see cref="VoiceLines.Choose"/>.
    /// </para>
    /// <para>
    /// <b>Reading a Unity microphone is reading a ring buffer.</b> <c>Microphone.Start</c>
    /// hands back an <c>AudioClip</c> that the driver writes into in a loop, and
    /// <c>Microphone.GetPosition</c> says where the write head is. Nothing tells you when
    /// it wraps, so this keeps its own read head and takes one frame at a time from
    /// between the two. If the read head is ever more than a buffer behind — a hitch
    /// during a chase, or a scene load — the frames in between are genuinely gone and
    /// pretending otherwise would play stale audio; the read head is snapped forward and
    /// the loss is counted in <see cref="OverrunFrames"/>.
    /// </para>
    /// </summary>
    public sealed class MicrophoneVoiceLine : IVoiceLine
    {
        /// <summary>
        /// Length of the driver's ring buffer, seconds.
        /// <para>
        /// One second is fifty frames of headroom against
        /// <see cref="VoiceCodec.FrameMilliseconds"/>, which is two orders of magnitude
        /// more than the one or two frames a healthy 20 ms drain leaves outstanding. It is
        /// sized for the unhealthy case instead: a garbage-collection pause or a chunk of
        /// map streaming in can stall the main thread for tens of milliseconds, and a
        /// buffer shorter than the worst stall turns a hitch into a dropout.
        /// </para>
        /// </summary>
        private const int RingSeconds = 1;

        /// <summary>
        /// How many frames to drain in one call.
        /// <para>
        /// Bounded on purpose. The driver can have several frames pending after a stall,
        /// and an unbounded drain would let a stall in the audio device turn into a stall
        /// in the frame loop — trading a dropout for a hitch, which is the worse of the
        /// two while somebody is being chased. Four frames is 80 ms, more than a 30 Hz
        /// tick, so the queue still shrinks under any sustained backlog.
        /// </para>
        /// </summary>
        public const int MaxFramesPerDrain = 4;

        /// <summary>Samples the driver's ring holds. The modulus every read head arithmetic uses.</summary>
        private const int RingSamples = VoiceCodec.SampleRate * RingSeconds;

        private readonly float[] _frame = new float[VoiceCodec.FrameSamples];

        private AudioClip? _clip;
        private string? _device;
        private int _readHead;
        private bool _reportedNoDevice;

        /// <inheritdoc />
        public string Name => "Microphone";

        /// <inheritdoc />
        public VoiceCodecId Codec => VoiceCodecId.Adpcm;

        /// <inheritdoc />
        public int SampleRate => VoiceCodec.SampleRate;

        /// <summary>
        /// Frames the driver overwrote before this class read them. Non-zero means the
        /// main thread stalled for longer than <see cref="RingSeconds"/>; it is a
        /// diagnostic for the debug overlay, not an error to act on.
        /// </summary>
        public int OverrunFrames { get; private set; }

        /// <inheritdoc />
        public bool IsAvailable
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return Microphone.devices.Length > 0;
#endif
            }
        }

        /// <inheritdoc />
        public bool IsCapturing => _clip != null;

        /// <inheritdoc />
        public float LastFrameRms { get; private set; } = -1f;

        /// <inheritdoc />
        public void StartCapture()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (_clip != null)
            {
                return;
            }

            var devices = Microphone.devices;
            if (devices.Length == 0)
            {
                if (!_reportedNoDevice)
                {
                    _reportedNoDevice = true;

                    // Once, and by name. A headless host and a player who has revoked the
                    // microphone permission look identical from here, and both of them
                    // should be able to read the log and know why nobody can hear them.
                    Debug.Log("[Voice] No capture device on this machine, so this runner is mute. "
                              + "Everyone else is still audible — playback does not need a microphone.");
                }

                return;
            }

            // devices[0] is the operating system's default, which is what a player who has
            // never opened a settings screen expects and the only choice this class can
            // make without a settings screen to read.
            _device = devices[0];

            _clip = Microphone.Start(_device, true, RingSeconds, VoiceCodec.SampleRate);
            _readHead = 0;
#endif
        }

        /// <inheritdoc />
        public void StopCapture()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (_clip == null)
            {
                return;
            }

            if (_device != null)
            {
                Microphone.End(_device);
            }

            Object.Destroy(_clip);
            _clip = null;
            _readHead = 0;
#endif
        }

        /// <inheritdoc />
        public int ReadEncodedFrame(byte[] destination)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return 0;
#else
            var clip = _clip;
            var device = _device;
            if (clip == null || device == null || destination == null)
            {
                return 0;
            }

            var writeHead = Microphone.GetPosition(device);
            if (writeHead < 0 || writeHead >= RingSamples)
            {
                return 0;
            }

            var pending = writeHead - _readHead;
            if (pending < 0)
            {
                pending += RingSamples;
            }

            if (pending < VoiceCodec.FrameSamples)
            {
                return 0;
            }

            // More than a whole buffer behind means the driver has already overwritten what
            // was not read. Playing it would put the speaker a second in the past, which is
            // worse than the gap.
            if (pending > RingSamples - VoiceCodec.FrameSamples)
            {
                OverrunFrames += pending / VoiceCodec.FrameSamples;
                _readHead = writeHead;
                return 0;
            }

            // GetData fills from an offset in the clip's own ring and wraps for us, so one
            // call is enough as long as the request does not exceed the clip length —
            // which a single 20 ms frame never does.
            clip.GetData(_frame, _readHead);
            _readHead = (_readHead + VoiceCodec.FrameSamples) % RingSamples;

            // Measured on the way past, while the samples are already in cache. The gate
            // that reads it is in VoiceCapture; see IVoiceLine.LastFrameRms for why it is
            // not derived from the encoded frame.
            LastFrameRms = VoiceCodec.Rms(_frame, 0, VoiceCodec.FrameSamples);

            return VoiceCodec.Encode(_frame, 0, destination);
#endif
        }

        /// <inheritdoc />
        public int Decode(byte[] frame, int frameBytes, float[] destination, int destinationOffset, float gain) =>
            VoiceCodec.Decode(frame, frameBytes, destination, destinationOffset, gain);
    }
}
