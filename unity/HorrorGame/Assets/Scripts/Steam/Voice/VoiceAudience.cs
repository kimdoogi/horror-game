#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Math;

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// One player, as far as voice is concerned: where they are and whether they are
    /// allowed to hear anything.
    /// </summary>
    public readonly struct VoiceListener
    {
        /// <summary>Platform id of the player.</summary>
        public readonly NetUserId Id;

        /// <summary>
        /// World position, in the host's coordinates. Whoever computes an audience
        /// must use their own copy of this — see <see cref="VoiceRelay"/> for why a
        /// position sent by a client is not usable for the decision.
        /// </summary>
        public readonly Vec3 Position;

        /// <summary>
        /// Whether this player may receive voice at all. §09 turns a dead player into
        /// a ghost with a deliberately reduced ability to interact; whether a ghost
        /// hears the living is a gameplay decision, so the flag is set from outside
        /// rather than assumed here.
        /// </summary>
        public readonly bool CanReceive;

        /// <summary>Builds a listener row.</summary>
        public VoiceListener(NetUserId id, Vec3 position, bool canReceive)
        {
            Id = id;
            Position = position;
            CanReceive = canReceive;
        }
    }

    /// <summary>
    /// Who is close enough to be sent voice. This is the whole of §13's cutoff rule,
    /// in one pure function, and it is the only place the rule exists.
    /// <para>
    /// §13 is unambiguous about why it is a transmission rule and not a playback one:
    /// <c>거리 > 30m → 전송 자체를 중단</c>, because
    /// <c>전부 받아놓고 볼륨만 0으로 재생하면 클라이언트 조작으로 다 들린다</c> —
    /// receive-everything-and-mute is defeated by anyone willing to edit a config or
    /// attach a debugger, and a game about
    /// <em>see it, remember it, say it out loud</em> (§03) has nothing left if the
    /// other team hears the room where the clue was. §13 states the priority
    /// plainly: 대역폭 절감보다 도청 방지가 본질이다. Bandwidth is a rounding error at
    /// §13's 초당 2~8KB.
    /// </para>
    /// <para>
    /// Two structural consequences, both enforced elsewhere but decided here:
    /// <see cref="IVoiceTransport"/> has no broadcast method — every send takes a
    /// recipient list, and the only thing that produces recipient lists is this
    /// class — and the sender does not open the microphone at all when the list comes
    /// back empty (<see cref="VoiceTransmitter"/>).
    /// </para>
    /// <para>
    /// Distance is full 3D, not §12's floor-plan distance. The basement sits under the
    /// surface, and a flat distance would let a player standing on the ground above
    /// hear a conversation happening a floor below them.
    /// </para>
    /// </summary>
    public static class VoiceAudience
    {
        /// <summary>
        /// §13's cutoff, from <c>GameConstants.VoiceCutoffDistance</c>. Re-exported
        /// rather than re-declared: <c>GameConstants.Validate()</c> already checks it
        /// against the zone-light radius, and a second copy of the number here is how
        /// that check quietly stops applying.
        /// </summary>
        public static float CutoffDistance => GameConstants.VoiceCutoffDistance;

        /// <summary>Squared cutoff, so the per-frame test needs no square root.</summary>
        public static float CutoffDistanceSquared =>
            GameConstants.VoiceCutoffDistance * GameConstants.VoiceCutoffDistance;

        /// <summary>
        /// Fills <paramref name="destination"/> with everyone who may hear
        /// <paramref name="speaker"/>, and returns how many that is.
        /// <para>
        /// The caller owns the list and it is cleared first, so an audience can be
        /// recomputed every frame for a whole match without allocating. The speaker is
        /// never in their own audience.
        /// </para>
        /// </summary>
        public static int Select(
            NetUserId speaker,
            Vec3 speakerPosition,
            IReadOnlyList<VoiceListener> listeners,
            List<NetUserId> destination)
        {
            destination.Clear();

            if (listeners == null)
            {
                return 0;
            }

            for (var i = 0; i < listeners.Count; i++)
            {
                var listener = listeners[i];

                if (!listener.CanReceive || listener.Id == speaker || !listener.Id.IsValid)
                {
                    continue;
                }

                if (IsInRange(speakerPosition, listener.Position))
                {
                    destination.Add(listener.Id);
                }
            }

            return destination.Count;
        }

        /// <summary>
        /// Whether two points are within §13's cutoff.
        /// <para>
        /// Uses <c>&lt;=</c> so a player standing exactly on the boundary is included.
        /// The alternative is a hairline band where §13's rule and the audio falloff
        /// in <see cref="AudioSourceVoiceOutput"/> disagree, which sounds like a
        /// dropout rather than like distance.
        /// </para>
        /// </summary>
        public static bool IsInRange(Vec3 a, Vec3 b) => (a - b).SqrMagnitude <= CutoffDistanceSquared;
    }
}
