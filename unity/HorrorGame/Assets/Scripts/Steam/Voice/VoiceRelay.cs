#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core.Math;

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// The host's half of §13's cutoff: a client's voice frame is forwarded only to
    /// the players the <em>host</em> can see are within range.
    /// <para>
    /// This is the gate that actually stops eavesdropping, and the reason it exists
    /// separately from the sender's gate is that the sender's gate runs on the
    /// attacker's machine. §13 gives the host authority over the match, so the host is
    /// the only participant whose view of the world an attacker cannot edit. A
    /// modified client can lie about where it is, can decline to gate at all, and can
    /// ask for frames it should not have — and every one of those attempts arrives
    /// here, where the recipient list is recomputed from the host's own roster.
    /// </para>
    /// <para>
    /// Concretely, the position used for the speaker is
    /// <see cref="IVoiceRoster.TryGetPosition"/> on the host, never a coordinate
    /// carried in the packet. A packet that claims a position is a packet that lets a
    /// client teleport its voice next to whoever it wants to listen to. For the same
    /// reason the speaker id is the connection's identity as the transport knows it,
    /// not a field the sender chose.
    /// </para>
    /// <para>
    /// Not a <c>MonoBehaviour</c>: it holds no scene state, and keeping it a plain
    /// class means the rule can be exercised with a fake transport and a hand-built
    /// roster.
    /// </para>
    /// </summary>
    public sealed class VoiceRelay
    {
        private readonly IVoiceTransport _transport;
        private readonly IVoiceRoster _roster;
        private readonly List<VoiceListener> _listeners = new List<VoiceListener>();
        private readonly List<NetUserId> _recipients = new List<NetUserId>();

        private bool _attached;

        /// <summary>Builds a relay over a transport and the host's roster.</summary>
        public VoiceRelay(IVoiceTransport transport, IVoiceRoster roster)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        }

        /// <summary>Frames forwarded so far. Host-side diagnostic.</summary>
        public int RelayedFrames { get; private set; }

        /// <summary>
        /// Frames dropped because nobody was in range. In a spread-out four-player
        /// match this being zero for a whole match means the cutoff is not working.
        /// </summary>
        public int DroppedOutOfRange { get; private set; }

        /// <summary>
        /// Frames dropped because the host has never heard of the speaker. Should stay
        /// at zero; a rising count means voice is arriving from a connection with no
        /// character, which is worth noticing rather than forwarding blind.
        /// </summary>
        public int DroppedUnknownSpeaker { get; private set; }

        /// <summary>Starts re-gating incoming client frames. Idempotent.</summary>
        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            _transport.FrameNeedsRelay += OnFrameNeedsRelay;
            _attached = true;
        }

        /// <summary>Stops re-gating. Idempotent.</summary>
        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _transport.FrameNeedsRelay -= OnFrameNeedsRelay;
            _attached = false;
        }

        /// <summary>
        /// Re-gates and forwards one frame. Public so the Net layer can call it
        /// directly from a Mirror command handler, which is the shape that lets the
        /// speaker id come from the connection rather than the payload.
        /// </summary>
        public void Relay(VoiceFrame frame)
        {
            if (!frame.HasAudio || !frame.Speaker.IsValid)
            {
                return;
            }

            if (!_roster.TryGetPosition(frame.Speaker, out var speakerPosition))
            {
                DroppedUnknownSpeaker++;
                return;
            }

            _roster.CopyListenersTo(_listeners);

            if (VoiceAudience.Select(frame.Speaker, speakerPosition, _listeners, _recipients) == 0)
            {
                // §13: 전송 자체를 중단. The frame stops here — it is not forwarded
                // with a volume hint, because a client that receives it can play it.
                DroppedOutOfRange++;
                return;
            }

            RelayedFrames++;
            _transport.RelayFrame(frame, _recipients);
        }

        private void OnFrameNeedsRelay(VoiceFrame frame) => Relay(frame);

        /// <summary>
        /// The host's own view of where a speaker is, exposed for a debug overlay.
        /// Returns <see cref="Vec3.Zero"/> for an unknown speaker.
        /// </summary>
        public Vec3 SpeakerPosition(NetUserId speaker) =>
            _roster.TryGetPosition(speaker, out var position) ? position : Vec3.Zero;
    }
}
