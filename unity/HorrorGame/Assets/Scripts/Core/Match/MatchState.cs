using System;
using System.Collections.Generic;
using HorrorGame.Core.Math;
using HorrorGame.Core.Roles;

namespace HorrorGame.Core.Match
{
    /// <summary>
    /// One match: four players (§11), the objective (§03), the ghosts (§09) and the
    /// §02 outcome that falls out of where everybody ended up.
    /// <para>
    /// It holds no clock. §07's time lives in <c>MatchClock</c>, which every rule
    /// that cares about the hour reads directly; duplicating it here would give the
    /// match two clocks that could disagree. The <paramref name="deltaSeconds"/> this
    /// class takes is only what §09's rattle cooldown needs.
    /// </para>
    /// <para>
    /// The outcome is computed, never stored. §02 is a function of the tally
    /// (<see cref="OutcomeEvaluator"/>), so two players resolving in the same tick
    /// cannot produce different endings depending on the order the host processed
    /// them, and there is no cached verdict to go stale when somebody dies one tick
    /// after reaching the surface.
    /// </para>
    /// <para>
    /// Three transitions run through this class rather than through
    /// <see cref="PlayerState"/>, because each has a consequence beyond the player it
    /// happens to: a death drops the objective (§08's parallel rule), an extraction
    /// is what recovers it (§02), and only one player in the match may hold it (§03).
    /// </para>
    /// </summary>
    public sealed class MatchState
    {
        private readonly PlayerState[] _players;
        private readonly RoleSelection _roles;

        private bool _abandoned;
        private bool _objectiveRecovered;
        private int _objectiveCarrier = NoCarrier;
        private Vec3? _objectiveRestingPosition;

        /// <summary>Sentinel for "nobody is holding the objective".</summary>
        private const int NoCarrier = -1;

        /// <summary>
        /// Starts a match from a settled lineup.
        /// <para>
        /// Everyone starts on the surface: §01 opens at the vehicle with an empty
        /// wallet, and §08's "1차 잠입 전 구매력 0" only means anything if the first
        /// descent is a decision the team makes rather than a state they wake up in.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="roles"/> is null.</exception>
        /// <exception cref="ArgumentException">The lineup is not four distinct roles — §11 has no other shape.</exception>
        public MatchState(RoleSelection roles, bool startOnSurface = true)
        {
            if (roles == null)
            {
                throw new ArgumentNullException(nameof(roles));
            }

            if (!roles.IsComplete)
            {
                throw new ArgumentException(
                    "§11 starts a match with " + GameConstants.PlayersPerMatch
                    + " distinct roles chosen; this lineup still has an empty slot.", nameof(roles));
            }

            _roles = roles;
            _players = new PlayerState[roles.SlotCount];
            for (var i = 0; i < _players.Length; i++)
            {
                _players[i] = new PlayerState(i, roles.RoleOf(i), startOnSurface);
            }
        }

        /// <summary>The party. Indexed by slot, fixed for the match.</summary>
        public IReadOnlyList<PlayerState> Players
        {
            get { return _players; }
        }

        /// <summary>Who took what. §11.</summary>
        public RoleSelection Roles
        {
            get { return _roles; }
        }

        /// <summary>The role this match does without, and the item that partly covers it. §11.</summary>
        public RoleGap Gap
        {
            get { return _roles.Gap; }
        }

        /// <summary>The absent role. §11: "그게 그 판의 성격이 된다."</summary>
        public RoleId MissingRole
        {
            get { return _roles.MissingRole; }
        }

        /// <summary>§11's 절대 규칙 for this lineup: still finishable, and what it will be short of.</summary>
        public CompletabilityReport Completability
        {
            get { return _roles.Completability; }
        }

        /// <summary>How the match stands right now, per §02, including what would survive it.</summary>
        public MatchResolution Resolution
        {
            get
            {
                int escaped;
                int lost;
                int inPlay;
                Tally(out escaped, out lost, out inPlay);

                if (_abandoned)
                {
                    return OutcomeEvaluator.Abandon(escaped, lost, inPlay, _objectiveRecovered);
                }

                return OutcomeEvaluator.Evaluate(escaped, lost, inPlay, _objectiveRecovered);
            }
        }

        /// <summary>Shorthand for <c>Resolution.Outcome</c>. §02.</summary>
        public MatchOutcome Outcome
        {
            get { return Resolution.Outcome; }
        }

        /// <summary>Whether the objective has left the basement. §02's first column.</summary>
        public bool ObjectiveRecovered
        {
            get { return _objectiveRecovered; }
        }

        /// <summary>Whether somebody has the objective in their hands right now. §03.</summary>
        public bool ObjectiveInHands
        {
            get { return _objectiveCarrier != NoCarrier; }
        }

        /// <summary>Who is carrying it, or -1. §03's "누가 들 것인가" — the match's last decision.</summary>
        public int ObjectiveCarrierIndex
        {
            get { return _objectiveCarrier; }
        }

        /// <summary>
        /// Where the objective is lying, or null when nobody has put it down (or it is
        /// out of the building). §08's 사망자의 전리품 rule applied to the objective —
        /// see <see cref="TryKill"/>.
        /// </summary>
        public Vec3? ObjectiveRestingPosition
        {
            get { return _objectiveRestingPosition; }
        }

        /// <summary>Players who got out alive. §02 keeps the match's information the moment this passes zero.</summary>
        public int EscapedCount
        {
            get
            {
                int escaped;
                int lost;
                int inPlay;
                Tally(out escaped, out lost, out inPlay);
                return escaped;
            }
        }

        /// <summary>Ghosts. §09.</summary>
        public int GhostCount
        {
            get
            {
                int escaped;
                int lost;
                int inPlay;
                Tally(out escaped, out lost, out inPlay);
                return lost;
            }
        }

        /// <summary>Players still alive and still in the match — the ones §02 is waiting on.</summary>
        public int StillInPlayCount
        {
            get
            {
                int escaped;
                int lost;
                int inPlay;
                Tally(out escaped, out lost, out inPlay);
                return inPlay;
            }
        }

        /// <summary>
        /// Whether §03's two-person escort is still physically available: at least
        /// <see cref="GameConstants.ObjectiveEscortMinPlayers"/> players alive and in
        /// the match, so one can carry and one can light the way.
        /// <para>
        /// Information, not a rule. §03 calls the escort "사실상 2인 1조" — in
        /// practice, not by prohibition — and §02's 부분 승리 row admits a single
        /// escapee carrying the objective out. So a lone survivor is allowed to try it
        /// in the dark, and this property is what tells the HUD (and telemetry) that
        /// they are attempting the thing §03 says needs two. See
        /// docs/BALANCE-FINDINGS.md.
        /// </para>
        /// </summary>
        public bool ObjectiveEscortStillPossible
        {
            get { return StillInPlayCount >= GameConstants.ObjectiveEscortMinPlayers; }
        }

        /// <summary>One player.</summary>
        /// <exception cref="ArgumentOutOfRangeException">No such slot.</exception>
        public PlayerState PlayerAt(int playerIndex)
        {
            RequirePlayer(playerIndex);
            return _players[playerIndex];
        }

        /// <summary>
        /// Steps the match. Only §09's rattle cooldowns need it; everything else here
        /// is event-driven. A zero, negative, NaN or huge delta is handled by
        /// <c>GhostState.Tick</c>, and a match with no ghosts in it does nothing at all.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            for (var i = 0; i < _players.Length; i++)
            {
                var ghost = _players[i].Ghost;
                if (ghost != null)
                {
                    ghost.Tick(deltaSeconds);
                }
            }
        }

        /// <summary>
        /// Hands the objective to a player. §03: both hands, no 전리품 at the same
        /// time, and one carrier at a time.
        /// </summary>
        /// <returns>
        /// False when the objective is already out of the building, already held, or
        /// when this player's hands are not free — <c>PlayerState.CanTakeObjective</c>
        /// gives the reason.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">No such slot.</exception>
        public bool TryTakeObjective(int playerIndex)
        {
            RequirePlayer(playerIndex);

            if (_objectiveRecovered || _objectiveCarrier != NoCarrier)
            {
                return false;
            }

            var player = _players[playerIndex];
            if (!player.CanTakeObjective)
            {
                return false;
            }

            player.SetCarryingObjective(true);
            _objectiveCarrier = playerIndex;
            _objectiveRestingPosition = null;
            return true;
        }

        /// <summary>
        /// Puts the objective down at <paramref name="where"/>. §03 makes carrying it
        /// exclusive, so putting it down is the only way to pick anything else up.
        /// </summary>
        /// <returns>False when nobody is carrying it.</returns>
        public bool TryDropObjective(Vec3 where)
        {
            if (_objectiveCarrier == NoCarrier)
            {
                return false;
            }

            _players[_objectiveCarrier].SetCarryingObjective(false);
            _objectiveCarrier = NoCarrier;
            _objectiveRestingPosition = where;
            return true;
        }

        /// <summary>
        /// Kills a player. §09 turns them into a ghost on the spot; §08 leaves their
        /// 전리품 there for the team to come back for.
        /// <para>
        /// If they were carrying the objective it lands where they fell. The document
        /// does not say so outright — §08 states the rule for 전리품 only — but the
        /// objective is carried in the same hands, and the alternatives are worse: it
        /// cannot vanish (§02 would become unwinnable through no decision of the
        /// team's) and it cannot stay in a ghost's hands (§09 forbids a ghost from
        /// carrying it out). Dropping it where the carrier died is also the reading
        /// that keeps §02's "지금 나갈까?" honest — the objective stays recoverable at a
        /// price.
        /// </para>
        /// <para>
        /// The pile itself belongs to the economy. Call
        /// <c>DroppedLootField.DropFrom(inventory, PlayerAt(i).DeathPosition)</c>, then
        /// tell the ghost what it is looking at with
        /// <c>PlayerAt(i).Ghost.NoteOwnLootDropped(value)</c> — §09's cruelty is that
        /// it can see the pile and cannot say a word about it.
        /// </para>
        /// </summary>
        /// <returns>
        /// False when the player is not below ground — §01's "지상(안전 · 상점)" and
        /// §08's "지상 차량 = 안전 지대" put deaths inside the building — or when they
        /// already left or already died.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">No such slot.</exception>
        public bool TryKill(int playerIndex, Vec3 deathPosition)
        {
            RequirePlayer(playerIndex);

            var player = _players[playerIndex];
            var wasCarrying = player.IsCarryingObjective;

            if (!player.Kill(deathPosition))
            {
                return false;
            }

            if (wasCarrying)
            {
                _objectiveCarrier = NoCarrier;
                _objectiveRestingPosition = deathPosition;
            }

            return true;
        }

        /// <summary>
        /// Takes a player out of the match for good. §02's 탈출, from the surface —
        /// §08 puts the vehicle up there, so getting out means having walked up.
        /// <para>
        /// If they were carrying the objective, this is the moment §02 calls 목표물
        /// 회수: the objective left with them, and the outcome moves from 생존 to a
        /// victory row.
        /// </para>
        /// </summary>
        /// <returns>False for a ghost (§09 — 탈출 불가), for somebody already out, or for anyone still below ground.</returns>
        /// <exception cref="ArgumentOutOfRangeException">No such slot.</exception>
        public bool TryExtract(int playerIndex)
        {
            RequirePlayer(playerIndex);

            var player = _players[playerIndex];
            var wasCarrying = player.IsCarryingObjective;

            if (!player.Extract())
            {
                return false;
            }

            if (wasCarrying)
            {
                player.SetCarryingObjective(false);
                _objectiveCarrier = NoCarrier;
                _objectiveRestingPosition = null;
                _objectiveRecovered = true;
            }

            return true;
        }

        /// <summary>
        /// Ends the session because the host left. §13 runs on host authority with no
        /// migration, so there is nothing to hand the match to.
        /// <para>
        /// Refused once §02 has already decided the match: a finished match cannot be
        /// retroactively abandoned, and a wipe in particular must stay a wipe.
        /// </para>
        /// </summary>
        /// <returns>False when already abandoned or already resolved.</returns>
        public bool TryAbandon()
        {
            if (_abandoned)
            {
                return false;
            }

            int escaped;
            int lost;
            int inPlay;
            Tally(out escaped, out lost, out inPlay);

            if (OutcomeEvaluator.Evaluate(escaped, lost, inPlay, _objectiveRecovered).IsFinal)
            {
                return false;
            }

            _abandoned = true;
            return true;
        }

        private void Tally(out int escaped, out int lost, out int inPlay)
        {
            escaped = 0;
            lost = 0;
            inPlay = 0;

            for (var i = 0; i < _players.Length; i++)
            {
                var player = _players[i];
                if (player.HasEscaped)
                {
                    escaped++;
                }
                else if (player.IsGhost)
                {
                    lost++;
                }
                else
                {
                    inPlay++;
                }
            }
        }

        private void RequirePlayer(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= _players.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerIndex), playerIndex, "§11 seats " + _players.Length + " players.");
            }
        }
    }
}
