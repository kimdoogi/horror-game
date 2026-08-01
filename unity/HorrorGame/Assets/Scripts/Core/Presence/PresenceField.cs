using System;
using System.Collections.Generic;

namespace HorrorGame.Core.Presence
{
    /// <summary>
    /// Everything the 그늘 is doing to the four players, and the one place anything asks
    /// about it.
    /// <para>
    /// The shape mirrors <c>LightField</c> on purpose, because the two are the same
    /// switch seen from opposite sides. <c>LightField</c> answers "can this be read" and
    /// "who can see me"; this answers "what is the dark charging me for". §03's sentence
    /// — 목표와 위험이 같은 스위치에 걸린다 — was only half true while the switch had one
    /// cost. With this it has two, in opposite directions, and neither position on it is
    /// free.
    /// </para>
    /// <para>
    /// <b>What this is not.</b> There is no monster here. No brain, no path, no target,
    /// no aggro, no state machine that pursues anything. The 그늘 has no location and
    /// therefore no distance to a player, so none of §06's vocabulary can be applied to
    /// it and none of §06's numbers can be moved by it. That is the design constraint
    /// this class is built to make structurally true rather than merely intended: §01
    /// keeps its horror with one unkillable pursuer, and a second one would not double
    /// the fear, it would halve the first — a player who cannot model either threat
    /// stops modelling both.
    /// </para>
    /// <para>
    /// Host-side (§13). It reads the monster's true distance, which clients do not have;
    /// what leaves the host is a stage and a pool fraction per player, which is a
    /// description of what that player can already see happening to them.
    /// </para>
    /// </summary>
    public sealed class PresenceField
    {
        private readonly PresenceState[] _players;
        private readonly List<PresenceToll> _tolls = new List<PresenceToll>();

        /// <summary>
        /// Creates a field for a match.
        /// </summary>
        /// <param name="playerCount">§11: four. Any positive count works, so the simulator can run one.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="playerCount"/> is not positive.</exception>
        public PresenceField(int playerCount)
        {
            if (playerCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount,
                    "A match needs at least one player for the 그늘 to charge.");
            }

            _players = new PresenceState[playerCount];
            for (var i = 0; i < playerCount; i++)
            {
                _players[i] = new PresenceState(i);
            }
        }

        /// <summary>Every player's exposure, indexed by player index.</summary>
        public IReadOnlyList<PresenceState> Players
        {
            get { return _players; }
        }

        /// <summary>
        /// One player's exposure.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">No such player.</exception>
        public PresenceState StateOf(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= _players.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(playerIndex), playerIndex,
                    "No such player in this match.");
            }

            return _players[playerIndex];
        }

        /// <summary>
        /// Steps one player. Called once per player per fixed step from the host loop.
        /// <para>
        /// An unknown player index is ignored rather than thrown on: this runs inside the
        /// fixed step beside §06's brain, and a player list that changed mid-frame must
        /// not be able to stop the monster from ticking.
        /// </para>
        /// </summary>
        /// <param name="deltaSeconds">Step length. Zero or negative changes nothing.</param>
        /// <param name="matchElapsedSeconds">§07's clock, for the tier the 그늘 reads its boldness from.</param>
        /// <param name="input">Where this player is and what light is on them.</param>
        public void Tick(float deltaSeconds, float matchElapsedSeconds, in PresenceTickInput input)
        {
            if (input.PlayerIndex < 0 || input.PlayerIndex >= _players.Length)
            {
                return;
            }

            var state = _players[input.PlayerIndex];

            // A ghost (§09) and an escapee (§02) are out of the building's reach. §09
            // already takes both of this toll's halves permanently — 말하기 불가능, and a
            // ghost has nothing left to misremember — so charging them again would be
            // charging for something already gone.
            var reading = input.InPlay
                ? PresenceDensity.Sample(
                    input.LightQuality, input.DistanceToMonster, matchElapsedSeconds, input.OnSurface)
                : PresenceReading.None;

            state.Tick(deltaSeconds, reading);

            if (state.TryTakeToll(out var toll))
            {
                _tolls.Add(toll);
            }
        }

        /// <summary>
        /// Takes one toll the 그늘 has collected, oldest first. Same one-shot handoff as
        /// <c>LightField.TryTakeExpiredFlare</c>, for the same reason: a silence must be
        /// applied exactly once however the host paces its reads.
        /// </summary>
        public bool TryTakeToll(out PresenceToll toll)
        {
            if (_tolls.Count == 0)
            {
                toll = default(PresenceToll);
                return false;
            }

            toll = _tolls[0];
            _tolls.RemoveAt(0);
            return true;
        }

        /// <summary>How many players are at or past <see cref="PresenceStage.Close"/>. §14's one-line read on whether this is doing anything.</summary>
        public int CountAtLeast(PresenceStage stage)
        {
            var count = 0;
            for (var i = 0; i < _players.Length; i++)
            {
                if (_players[i].Stage >= stage)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Empties the field for a new match. See <see cref="PresenceState.Clear"/> on why this is not §03's 부분 리셋.</summary>
        public void Clear()
        {
            for (var i = 0; i < _players.Length; i++)
            {
                _players[i].Clear();
            }

            _tolls.Clear();
        }
    }

    /// <summary>
    /// One player's situation for one step, as primitives.
    /// <para>
    /// Bundled rather than passed positionally because two of the four are floats that
    /// would be silently swappable at a call site, and swapping light quality for a
    /// distance would produce a 그늘 that pools in lit rooms and nowhere else — a bug
    /// that reads as a tuning problem for a week.
    /// </para>
    /// </summary>
    public readonly struct PresenceTickInput
    {
        /// <summary>Which player.</summary>
        public readonly int PlayerIndex;

        /// <summary>
        /// Light on the <b>player</b>, 0–1 — not on what they are pointing at.
        /// <para>
        /// The distinction is the whole of §03's escort. A player holding a lit torch is
        /// standing in its spill and reads 1.0; a player carrying 목표물 cannot hold a
        /// torch at all ("양손을 쓴다"), so their light is whatever a teammate is putting
        /// on them. §03 already says 누군가 비춰줘야 한다 and gives no reason beyond
        /// seeing where you are going. This is the reason.
        /// </para>
        /// </summary>
        public readonly float LightQuality;

        /// <summary>Metres from this player to the monster. Host truth; see <see cref="PresenceReading.ClearedByMonster"/>.</summary>
        public readonly float DistanceToMonster;

        /// <summary>True on §01's 지상. The 안전 지대 is safe from this too.</summary>
        public readonly bool OnSurface;

        /// <summary>False for a §09 ghost or a §02 escapee — see <see cref="PresenceField.Tick"/>.</summary>
        public readonly bool InPlay;

        /// <summary>Builds an input.</summary>
        public PresenceTickInput(
            int playerIndex,
            float lightQuality,
            float distanceToMonster,
            bool onSurface,
            bool inPlay)
        {
            PlayerIndex = playerIndex;
            LightQuality = lightQuality;
            DistanceToMonster = distanceToMonster;
            OnSurface = onSurface;
            InPlay = inPlay;
        }
    }
}
