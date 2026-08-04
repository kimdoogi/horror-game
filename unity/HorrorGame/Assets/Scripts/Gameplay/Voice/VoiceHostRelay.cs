#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core.Voice;
using Mirror;
using UnityEngine;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// The host's half of §13's step ②: one speaker's frame goes out to exactly the
    /// people <see cref="VoiceRules"/> says are close enough, and to nobody else.
    /// <para>
    /// <b>There is no broadcast method here on purpose.</b> Every send in this class is
    /// addressed to one connection that <see cref="VoiceRules.ClearRange"/> admitted. That
    /// is what makes 도청 방지 structural rather than a habit — a future edit cannot
    /// casually send voice to the whole match, because there is no call that would do it.
    /// The speaker's own machine applies the same test before it opens the microphone
    /// (<see cref="VoiceCapture"/>), but that half runs on the speaker's computer and is
    /// therefore advisory; this one is the gate an attacker cannot edit.
    /// </para>
    /// <para>
    /// <b>Clear-air range, not the occluded one, and the asymmetry is deliberate.</b>
    /// Occlusion only ever <em>shrinks</em> a voice's reach — <c>OccludedFraction</c> is
    /// 0.62 — so gating on the clear range is a superset of everybody who could possibly
    /// hear, and the listener narrows it with its own line of sight. Putting the raycast
    /// here instead would cost the host up to 20 × 20 physics queries per 20 ms frame and
    /// would still be wrong, because the wall that matters is the one between two people
    /// and the host would have to test it per pair anyway.
    /// </para>
    /// <para>
    /// <b>The bytes are never decoded.</b> A frame arrives compressed, is measured, and
    /// leaves compressed. §13's whole budget argument depends on the host not becoming a
    /// mixer: decoding twenty streams to re-encode them would cost the host more CPU than
    /// the match does and would add a codec generation of loss for nothing.
    /// </para>
    /// </summary>
    public sealed class VoiceHostRelay
    {
        /// <summary>
        /// Largest frame the host will forward, bytes.
        /// <para>
        /// <b>The one place a client's bytes are amplified, so the one place a size limit
        /// belongs.</b> A frame arriving here is copied to up to
        /// <see cref="VoiceSlots.PerListener"/> other machines, so an oversized payload
        /// costs the host three times what it cost the sender to send. §13 is relaxed about
        /// cheating — 치팅 방어 거의 불필요 for a PvE game played with friends — and this is
        /// not a cheat guard: it is the difference between one confused client and a host
        /// whose uplink is saturated for everybody.
        /// </para>
        /// <para>
        /// 2 KB is twelve times <see cref="VoiceCodec.FrameBytes"/> and a quarter of §13's
        /// whole per-second budget, so no legitimate 20 ms frame from either codec can
        /// approach it — Steam's are a few hundred bytes even when polled slowly.
        /// </para>
        /// </summary>
        public const int MaxAcceptedFrameBytes = 2 * 1024;

        private readonly IVoicePositions _positions;
        private readonly VoiceSlots _slots = new VoiceSlots();
        private readonly List<int> _listeners = new List<int>();

        /// <summary>Builds a relay over a position source.</summary>
        /// <param name="positions">Normally <see cref="NetVoicePositions"/>.</param>
        public VoiceHostRelay(IVoicePositions positions)
        {
            _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        }

        /// <summary>Frames clients handed the host.</summary>
        public int FramesReceived { get; private set; }

        /// <summary>Downstream sends the host made. One frame to three listeners counts three.</summary>
        public int FramesForwarded { get; private set; }

        /// <summary>Payload bytes the host put on the wire downstream. The bandwidth claim, measured.</summary>
        public long BytesForwarded { get; private set; }

        /// <summary>
        /// Frames the host dropped because nobody was in range. Not an error — it is the
        /// cutoff winning a race against the speaker's own gate, which is one tick behind
        /// the host's view of the world.
        /// </summary>
        public int DroppedNoAudience { get; private set; }

        /// <summary>Frames the host dropped because it could not place the speaker.</summary>
        public int DroppedNoSpeaker { get; private set; }

        /// <summary>
        /// Frames refused for exceeding <see cref="MaxAcceptedFrameBytes"/>. Non-zero means
        /// either a codec whose frame size moved or somebody pushing on the host's uplink.
        /// </summary>
        public int DroppedOversized { get; private set; }

        /// <summary>Sends the slot allocator refused. See <see cref="VoiceSlots"/>.</summary>
        public int DroppedNoSlot => _slots.Refusals;

        /// <summary>The slot book, for the debug overlay.</summary>
        public VoiceSlots Slots => _slots;

        /// <summary>
        /// Takes one frame from <paramref name="speakerConnectionId"/> and forwards it to
        /// everybody in range.
        /// </summary>
        /// <param name="speakerConnectionId">Read from the socket the frame arrived on, never from the payload.</param>
        /// <param name="effort">§01's three, off the wire.</param>
        /// <param name="codec">Which codec produced <paramref name="payload"/>.</param>
        /// <param name="payload">Borrowed compressed bytes. Not kept.</param>
        /// <param name="now">Seconds on a monotonic clock, for <see cref="VoiceSlots"/>.</param>
        /// <returns>How many listeners it went to.</returns>
        public int Accept(
            int speakerConnectionId,
            VoiceEffort effort,
            VoiceCodecId codec,
            ArraySegment<byte> payload,
            double now)
        {
            FramesReceived++;

            if (codec == VoiceCodecId.None || payload.Count <= 0 || payload.Array == null)
            {
                return 0;
            }

            if (payload.Count > MaxAcceptedFrameBytes)
            {
                DroppedOversized++;
                return 0;
            }

            // The rule, asked once. A Silent effort has zero range by construction, so a
            // client that sends frames while claiming not to be speaking reaches nobody.
            var range = VoiceRules.ClearRange(effort);
            if (range <= 0f)
            {
                DroppedNoAudience++;
                return 0;
            }

            if (!_positions.TryGetSpeakerPosition(speakerConnectionId, out var from))
            {
                DroppedNoSpeaker++;
                return 0;
            }

            var seat = _positions.SeatIndexOf(speakerConnectionId);
            _positions.CopyListeners(_listeners);

            var sent = 0;
            var rangeSquared = range * range;

            for (var i = 0; i < _listeners.Count; i++)
            {
                var listenerId = _listeners[i];
                if (listenerId == speakerConnectionId)
                {
                    // Nobody is sent their own voice. §13's pipeline has no monitor path
                    // and a runner who could hear themselves at their own position would
                    // be at distance 0 — full gain, permanently.
                    continue;
                }

                if (!_positions.TryGetSpeakerPosition(listenerId, out var to))
                {
                    continue;
                }

                var offset = to - from;
                var distanceSquared = offset.sqrMagnitude;
                if (distanceSquared >= rangeSquared)
                {
                    continue;
                }

                if (!_slots.TryAdmit(listenerId, speakerConnectionId, Mathf.Sqrt(distanceSquared), now))
                {
                    continue;
                }

                if (!NetworkServer.connections.TryGetValue(listenerId, out var conn) || conn == null)
                {
                    continue;
                }

                conn.Send(
                    new VoiceDownstreamMessage
                    {
                        SpeakerConnectionId = speakerConnectionId,
                        SpeakerSeatIndex = seat,
                        Codec = (byte)codec,
                        Effort = (byte)effort,
                        SpeakerPosition = from,
                        Payload = payload,
                    },
                    VoiceWire.Channel);

                FramesForwarded++;
                BytesForwarded += payload.Count;
                sent++;
            }

            if (sent == 0)
            {
                DroppedNoAudience++;
            }

            return sent;
        }

        /// <summary>Releases a departed connection's slots.</summary>
        /// <param name="connectionId">The connection that left.</param>
        public void Forget(int connectionId) => _slots.Forget(connectionId);

        /// <summary>Resets the book and the counters. Called when the server stops.</summary>
        public void Clear()
        {
            _slots.Clear();
            FramesReceived = 0;
            FramesForwarded = 0;
            BytesForwarded = 0;
            DroppedNoAudience = 0;
            DroppedNoSpeaker = 0;
        }
    }
}
