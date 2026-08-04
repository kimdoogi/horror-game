#nullable enable

using System;
using HorrorGame.Core.Voice;
using Mirror;
using UnityEngine;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// A speaker's frame on its way UP to the host. §13's step ②, first hop.
    /// <para>
    /// <b>There is no speaker field, and that is the security of the whole system.</b>
    /// The host stamps the speaker from <c>NetworkConnectionToClient</c> — the socket the
    /// datagram arrived on — so a client cannot claim to be anybody. Everything a client
    /// is trusted with here is what it is saying and how hard; where it is standing comes
    /// from <c>NetPlayer</c>'s host-authoritative position, which is already speed-clamped.
    /// </para>
    /// <para>
    /// <see cref="Payload"/> is <b>borrowed</b>: on send it is the capture's reusable
    /// buffer, and on receive it points into Mirror's reader, so it is valid only for the
    /// duration of the handler. Anything that wants to keep the bytes copies them. This is
    /// the price of not allocating 50 arrays a second per speaker, and it is said out loud
    /// because the alternative way to learn it is intermittent audio corruption.
    /// </para>
    /// </summary>
    public struct VoiceUpstreamMessage : NetworkMessage
    {
        /// <summary>Which codec produced <see cref="Payload"/>. See <see cref="VoiceCodecId"/>.</summary>
        public byte Codec;

        /// <summary>
        /// §01's three efforts, as <see cref="VoiceEffort"/>. The host re-derives the range
        /// from it — a client that lies here can only make itself quieter or make the
        /// creature come for it, because <c>MatchDirector</c> reports the same effort to
        /// §06 on the speaker's own machine.
        /// </summary>
        public byte Effort;

        /// <summary>One encoded frame. Borrowed — see the type remarks.</summary>
        public ArraySegment<byte> Payload;
    }

    /// <summary>
    /// A frame on its way DOWN to one listener the host decided is close enough. §13's
    /// step ②, second hop.
    /// <para>
    /// <b>Why the speaker's position is on the packet.</b> The obvious design has the
    /// listener look the speaker up in <c>NetworkClient.spawned</c> and read
    /// <c>NetPlayer.NetworkedPosition</c>, and it would work almost always. Two things
    /// make "almost" the wrong bar. Voice frames leave at
    /// <see cref="VoiceCodec.FramesPerSecond"/> — 50 Hz — while spawns, SyncVars and
    /// <c>HorrorInterestManagement</c>'s rebuild all run at
    /// <c>GameConstants.NetworkSendRate</c>, 30 Hz, so the first frame of a talkspurt can
    /// and does arrive before the listener has ever heard of the speaker; without a
    /// position on the packet that frame is silently dropped, and "the first syllable is
    /// missing" is exactly the kind of defect nobody files. And
    /// <c>NetInterestScope.PerceptionRange</c> is derived from
    /// <c>GameConstants.VoiceCutoffDistance</c> (30 m) while the audible range is
    /// <c>VoiceRules.ShoutRange</c> (30 m) — two constants that agree today by coincidence
    /// and that nothing forces to agree tomorrow. Twelve bytes a frame (600 B/s, 7.3% of
    /// the stream) buys independence from both.
    /// </para>
    /// </summary>
    public struct VoiceDownstreamMessage : NetworkMessage
    {
        /// <summary>
        /// The speaker's connection id, as the host knows it. Stable for the session,
        /// unique, never client-declared, and the key playback files voices under.
        /// </summary>
        public int SpeakerConnectionId;

        /// <summary>
        /// The speaker's §11 lobby seat, or −1 when no lobby seated them. Carried only so
        /// <c>RaceHud</c> can mark the standings row of somebody talking — §02's
        /// <c>Racer.Id</c> is a seat index, so this is the one field that joins a voice to
        /// a name on screen.
        /// </summary>
        public int SpeakerSeatIndex;

        /// <summary>Which codec produced <see cref="Payload"/>.</summary>
        public byte Codec;

        /// <summary>How hard they are speaking. The listener needs it to pick the range.</summary>
        public byte Effort;

        /// <summary>Where the host says the speaker is. See the type remarks.</summary>
        public Vector3 SpeakerPosition;

        /// <summary>One encoded frame. Borrowed — see <see cref="VoiceUpstreamMessage"/>.</summary>
        public ArraySegment<byte> Payload;
    }

    /// <summary>Shared helpers for turning wire bytes back into the rule's own types.</summary>
    public static class VoiceWire
    {
        /// <summary>
        /// Mirror channel voice rides. Unreliable, and the choice is not an optimisation.
        /// <para>
        /// A retransmitted voice frame is worthless: by the time KCP has noticed the loss
        /// and resent it, the 20 ms it contained is in the past and playing it would put
        /// the speaker permanently behind. Worse, the reliable channel is <em>ordered</em>,
        /// so one lost voice datagram would hold up every <c>NetPlayer</c> position update
        /// queued behind it — a dropped syllable would become a stutter in everybody's
        /// movement. <see cref="VoiceCodec"/>'s self-contained frames exist so that
        /// unreliable is safe here.
        /// </para>
        /// </summary>
        public const int Channel = Channels.Unreliable;

        /// <summary>Turns a wire byte back into an effort, refusing anything outside §01's three.</summary>
        /// <param name="raw">The byte off the wire.</param>
        public static VoiceEffort ToEffort(byte raw)
        {
            switch (raw)
            {
                case (byte)VoiceEffort.Whisper:
                    return VoiceEffort.Whisper;
                case (byte)VoiceEffort.Talk:
                    return VoiceEffort.Talk;
                case (byte)VoiceEffort.Shout:
                    return VoiceEffort.Shout;
                default:
                    return VoiceEffort.Silent;
            }
        }

        /// <summary>Turns a wire byte back into a codec id, refusing anything unknown.</summary>
        /// <param name="raw">The byte off the wire.</param>
        public static VoiceCodecId ToCodec(byte raw)
        {
            switch (raw)
            {
                case (byte)VoiceCodecId.Adpcm:
                    return VoiceCodecId.Adpcm;
                case (byte)VoiceCodecId.Steam:
                    return VoiceCodecId.Steam;
                default:
                    return VoiceCodecId.None;
            }
        }
    }
}
