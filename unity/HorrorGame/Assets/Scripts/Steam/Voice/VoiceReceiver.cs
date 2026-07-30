#nullable enable

using System;

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// Step ③ of §13's pipeline: take an arriving frame, decompress it, and hand the
    /// samples to the speaker's positional output.
    /// <para>
    /// Deliberately has no filtering of its own. By the time a frame reaches here the
    /// cutoff has been applied twice — once by the speaker and once, authoritatively,
    /// by the host (<see cref="VoiceRelay"/>). §13 rejects the alternative outright:
    /// 전부 받아놓고 볼륨만 0으로 재생하면 클라이언트 조작으로 다 들린다. A receiver
    /// that decided what to play would be exactly that design, so this one plays
    /// everything it is given, and the reason it can be trusted to is that it is never
    /// given anything it should not have.
    /// </para>
    /// <para>
    /// A plain class rather than a component: it holds a codec buffer and a
    /// subscription, has no transform and no per-frame work.
    /// </para>
    /// </summary>
    public sealed class VoiceReceiver
    {
        private readonly IVoiceTransport _transport;
        private readonly IVoiceRoster _roster;
        private readonly IVoiceBackend _backend;

        private byte[] _pcm = new byte[VoiceTuning.PcmFrameCapacity];
        private bool _attached;

        /// <summary>
        /// Builds a receiver. <paramref name="backend"/> defaults to the running
        /// service's codec, which is what makes the offline build a no-op rather than a
        /// failure.
        /// </summary>
        public VoiceReceiver(IVoiceTransport transport, IVoiceRoster? roster = null, IVoiceBackend? backend = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _roster = roster ?? VoiceRoster.Shared;
            _backend = backend ?? SteamServices.Current.Voice;
        }

        /// <summary>Frames played. Diagnostic.</summary>
        public int PlayedFrames { get; private set; }

        /// <summary>
        /// Frames that arrived for a speaker with no audible character here — a player
        /// whose body has not spawned locally yet, or a ghost (§09) with no voice
        /// output. Dropped rather than queued: audio held for a character that may never
        /// appear would play from the wrong place when it did.
        /// </summary>
        public int DroppedNoOutput { get; private set; }

        /// <summary>Frames the codec refused. A rising count means version-mismatched peers.</summary>
        public int DroppedUndecodable { get; private set; }

        /// <summary>Starts playing incoming frames. Idempotent.</summary>
        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            _transport.FrameReceived += OnFrameReceived;
            _attached = true;
        }

        /// <summary>Stops playing incoming frames. Idempotent.</summary>
        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _transport.FrameReceived -= OnFrameReceived;
            _attached = false;
        }

        private void OnFrameReceived(VoiceFrame frame)
        {
            if (!frame.HasAudio || !_backend.IsAvailable)
            {
                return;
            }

            var output = _roster.GetOutput(frame.Speaker);
            if (output == null)
            {
                DroppedNoOutput++;
                return;
            }

            if (!_backend.TryDecompress(frame.Data, frame.Length, ref _pcm, out var pcmBytes) || pcmBytes <= 0)
            {
                DroppedUndecodable++;
                return;
            }

            output.Configure(_backend.SampleRate);
            output.SubmitPcm(_pcm, pcmBytes);
            PlayedFrames++;
        }
    }
}
