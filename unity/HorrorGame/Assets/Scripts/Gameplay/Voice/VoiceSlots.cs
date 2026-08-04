#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// How many people one runner can hear at once, and which ones when more than that
    /// are shouting. The answer to §11's twenty.
    /// <para>
    /// <b>Why a cap exists at all.</b> One voice stream is
    /// <see cref="VoiceCodec.BytesPerSecondPerStream"/> — 8.2 kB/s — and the host forwards
    /// a copy of it to every listener the rule admits. With §11's full field in one place
    /// and everybody shouting, the uncapped arithmetic is
    /// <c>20 speakers × 19 listeners × 8.2 kB/s = 3.1 MB/s</c> off the host's uplink, or
    /// 25 Mbit/s. That is not a tuning problem, it is a design that does not ship, and it
    /// is reachable: §12-A funnels the whole field through one gate per storey and the
    /// middle of B8 is where all twenty are trying to stand.
    /// </para>
    /// <para>
    /// <b>Why three.</b> The cap is the budget, so it is chosen by the arithmetic and not
    /// by taste: three slots puts the same worst case at
    /// <c>20 × 3 × 8.2 kB/s = 492 kB/s</c>, 3.9 Mbit/s, which a domestic uplink can carry
    /// and which Steam's free SDR relay is sized for. It happens to agree with what a
    /// person can actually use — speech intelligibility collapses past three concurrent
    /// talkers — but the reason it is 3 and not 4 is the megabit, and if the codec ever
    /// gets cheaper this number should move with it rather than stay where somebody liked
    /// it.
    /// </para>
    /// <para>
    /// <b>Nearest wins, and a slot is held through the gaps in a sentence.</b> Admission is
    /// by distance because that is what the rule already says loudness is — a speaker
    /// admitted over a nearer one would be audible while somebody louder was silent, which
    /// reads as the game arbitrarily muting the person beside you. The hold exists because
    /// speech is not continuous: an ordinary inter-word pause is 50~200 ms, so a slot
    /// released the instant frames stop would be stolen between two words of the same
    /// sentence.
    /// </para>
    /// </summary>
    public sealed class VoiceSlots
    {
        /// <summary>
        /// Concurrent speakers one listener is sent. See the class remarks for the
        /// arithmetic that chose it.
        /// </summary>
        public const int PerListener = 3;

        /// <summary>
        /// Seconds a slot survives with no frames arriving. Covers an inter-word pause and
        /// releases within a quarter second of somebody genuinely stopping.
        /// </summary>
        public const float HoldSeconds = 0.25f;

        private readonly Dictionary<int, Slot[]> _byListener = new Dictionary<int, Slot[]>(GameConstants.RaceRunnersMax);

        /// <summary>How many admissions were refused because every slot held somebody nearer.</summary>
        public int Refusals { get; private set; }

        /// <summary>How many times a further speaker was evicted for a nearer one.</summary>
        public int Evictions { get; private set; }

        /// <summary>
        /// Decides whether <paramref name="listenerId"/> hears <paramref name="speakerId"/>
        /// this frame, and books the slot when it does.
        /// </summary>
        /// <param name="listenerId">The connection being sent to.</param>
        /// <param name="speakerId">The connection being sent.</param>
        /// <param name="distanceMetres">Between the two of them, from the host's authoritative positions.</param>
        /// <param name="now">Seconds on any monotonic clock. <c>Time.unscaledTimeAsDouble</c> in the game.</param>
        public bool TryAdmit(int listenerId, int speakerId, float distanceMetres, double now)
        {
            if (!_byListener.TryGetValue(listenerId, out var slots))
            {
                slots = new Slot[PerListener];
                for (var i = 0; i < slots.Length; i++)
                {
                    slots[i].SpeakerId = -1;
                }

                _byListener[listenerId] = slots;
            }

            var free = -1;
            var furthest = -1;
            var furthestDistance = float.NegativeInfinity;

            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i].SpeakerId == speakerId)
                {
                    // Already holding it. Refreshing the distance as well as the clock
                    // matters: a speaker who walks away must become the eviction candidate
                    // rather than keeping the slot on the strength of where they used to
                    // stand.
                    slots[i].Distance = distanceMetres;
                    slots[i].LastFrameTime = now;
                    return true;
                }

                var stale = slots[i].SpeakerId < 0 || now - slots[i].LastFrameTime > HoldSeconds;
                if (stale)
                {
                    if (free < 0)
                    {
                        free = i;
                    }

                    continue;
                }

                if (slots[i].Distance > furthestDistance)
                {
                    furthestDistance = slots[i].Distance;
                    furthest = i;
                }
            }

            if (free >= 0)
            {
                Take(slots, free, speakerId, distanceMetres, now);
                return true;
            }

            if (furthest >= 0 && distanceMetres < furthestDistance)
            {
                Evictions++;
                Take(slots, furthest, speakerId, distanceMetres, now);
                return true;
            }

            Refusals++;
            return false;
        }

        /// <summary>
        /// Forgets a listener's slots. Called when a connection goes away, so a
        /// twenty-match session does not carry a dead runner's booking into the next one.
        /// </summary>
        /// <param name="listenerId">The connection that left.</param>
        public void Forget(int listenerId)
        {
            _byListener.Remove(listenerId);
        }

        /// <summary>Drops every booking. Called when the server stops.</summary>
        public void Clear()
        {
            _byListener.Clear();
            Refusals = 0;
            Evictions = 0;
        }

        private static void Take(Slot[] slots, int index, int speakerId, float distanceMetres, double now)
        {
            slots[index].SpeakerId = speakerId;
            slots[index].Distance = distanceMetres;
            slots[index].LastFrameTime = now;
        }

        private struct Slot
        {
            internal int SpeakerId;
            internal float Distance;
            internal double LastFrameTime;
        }
    }
}
