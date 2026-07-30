#nullable enable

using System;
using System.Collections.Generic;

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// One compressed voice frame in flight.
    /// <para>
    /// <see cref="Data"/> is <b>borrowed</b>. It is the sender's or the transport's
    /// reusable buffer and its contents are only valid for the duration of the call
    /// that handed it over; a subscriber that wants to keep the bytes must copy them.
    /// This is the price of not allocating a per-frame array during a chase, and it is
    /// stated here because it is the kind of contract that is otherwise discovered as
    /// intermittent audio corruption.
    /// </para>
    /// </summary>
    public readonly struct VoiceFrame
    {
        /// <summary>Who spoke. Set by the host on relay, never trusted from a client payload.</summary>
        public readonly NetUserId Speaker;

        /// <summary>Borrowed buffer holding the compressed frame.</summary>
        public readonly byte[] Data;

        /// <summary>Valid byte count within <see cref="Data"/>.</summary>
        public readonly int Length;

        /// <summary>Wraps a borrowed buffer.</summary>
        public VoiceFrame(NetUserId speaker, byte[] data, int length)
        {
            Speaker = speaker;
            Data = data;
            Length = length;
        }

        /// <summary>True when there is something worth sending.</summary>
        public bool HasAudio => Data != null && Length > 0;
    }

    /// <summary>
    /// How voice frames cross the network. Implemented by the Net layer over Mirror,
    /// and by <see cref="LoopbackVoiceTransport"/> for a session with no network.
    /// <para>
    /// <b>There is deliberately no broadcast method.</b> Both send paths demand an
    /// explicit recipient list, and the only thing in the codebase that produces one
    /// is <see cref="VoiceAudience.Select"/>, which applies §13's cutoff. That is what
    /// makes the anti-eavesdropping rule structural instead of a habit: a future
    /// transport cannot casually send voice to everyone in the match, because the API
    /// does not offer a way to say it.
    /// </para>
    /// <para>
    /// The topology is Mirror's, and §13 fixed it: 호스트 권한, no host migration. A
    /// client cannot reach another client, so a client's frame takes two hops and is
    /// gated twice — once by the speaker, which decides whether to spend the upload
    /// at all, and once by the host, which is the only gate an attacker cannot edit.
    /// </para>
    /// </summary>
    public interface IVoiceTransport
    {
        /// <summary>Whether frames can be sent right now. False before the match connects.</summary>
        bool IsReady { get; }

        /// <summary>
        /// Whether this peer is the host, and therefore the one that must re-gate other
        /// people's voice.
        /// </summary>
        bool IsHost { get; }

        /// <summary>
        /// Sends the local player's captured frame to <paramref name="audience"/>.
        /// <para>
        /// On a client this is one hop to the host, which will recompute the audience
        /// before forwarding; the list is still required, because the useful half of
        /// the sender's gate is the decision not to open the microphone or spend the
        /// upload when it is empty. On the host it is a direct targeted send.
        /// </para>
        /// <para>Implementations must ignore an empty audience rather than broadcast.</para>
        /// </summary>
        void SendCapturedFrame(VoiceFrame frame, IReadOnlyList<NetUserId> audience);

        /// <summary>
        /// Host only: forwards a client's frame to exactly
        /// <paramref name="recipients"/>. Called by <see cref="VoiceRelay"/>, which
        /// computes the list from the host's own view of the world.
        /// </summary>
        void RelayFrame(VoiceFrame frame, IReadOnlyList<NetUserId> recipients);

        /// <summary>
        /// A frame arrived that this peer is allowed to hear. Nothing further filters
        /// it — by the time it is raised, the cutoff has already been applied by the
        /// sender and by the host.
        /// </summary>
        event Action<VoiceFrame>? FrameReceived;

        /// <summary>
        /// Host only: a client's frame arrived and needs re-gating before it goes
        /// anywhere. Subscribed to by <see cref="VoiceRelay"/>. On a client this is
        /// never raised.
        /// </summary>
        event Action<VoiceFrame>? FrameNeedsRelay;
    }
}
