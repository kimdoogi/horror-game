using HorrorGame.Core.Ghost;
using HorrorGame.Core.Math;
using HorrorGame.Core.Roles;

namespace HorrorGame.Core.Match
{
    /// <summary>
    /// Where one player stands in the match. §02 counts these to decide the outcome,
    /// §03 gates recovery on them, §09 makes two of them terminal.
    /// <para>
    /// One enum rather than a pair of alive/on-surface booleans, because two of the
    /// four combinations must never exist: §09 forbids a ghost from getting out, and
    /// nothing brings back somebody who already left. Making them unrepresentable
    /// beats validating them everywhere.
    /// </para>
    /// </summary>
    public enum PlayerStanding
    {
        /// <summary>지하 — in the danger, and the only place clues, loot and the objective exist. §03.</summary>
        Underground = 0,

        /// <summary>지상 — at the vehicle. Safe, sells, buys, heals; the clock keeps running (§07).</summary>
        Surface = 1,

        /// <summary>탈출 — out for good. §02's "누군가는 살아서 나가야 한다". Terminal.</summary>
        Escaped = 2,

        /// <summary>유령 — dead. §09: sees everything, says nothing, cannot leave. Terminal.</summary>
        Dead = 3,
    }

    /// <summary>
    /// One of §11's four players: which role they took, whether they are still
    /// alive, which side of the ground they are on, whether they are hurt, and what
    /// is in their hands.
    /// <para>
    /// Deliberately not an inventory. §08's <c>Inventory</c> owns weight, capacity
    /// and value; what this class needs from it is two booleans, pushed in through
    /// <see cref="SetLootState"/>. That is the seam ARCHITECTURE.md asks for — this
    /// file never learns what a 전리품 is, and the economy never learns what a ghost
    /// is.
    /// </para>
    /// <para>
    /// The three transitions with match-wide consequences — death, extraction and
    /// taking the objective — are <c>internal</c> and belong to
    /// <see cref="MatchState"/>. A death drops loot and possibly the objective, an
    /// extraction may be what recovers the objective, and only one player in the
    /// match may hold it; none of those invariants can be kept from inside a single
    /// player.
    /// </para>
    /// </summary>
    public sealed class PlayerState
    {
        private PlayerStanding _standing;
        private GhostState? _ghost;
        private bool _injured;
        private bool _carryingObjective;
        private bool _holdsLoot;
        private bool _oversizePieceInHands;

        /// <summary>
        /// Creates a player in their lobby role.
        /// <para>
        /// Starts on the surface by default: §01 opens with the team at the vehicle
        /// with an empty wallet, and the first descent is what starts the round trip.
        /// <see cref="RoleId.None"/> is accepted so a lobby can hold a player who has
        /// not picked yet; <see cref="MatchState"/> refuses to start in that state.
        /// </para>
        /// </summary>
        public PlayerState(int index, RoleId role, bool startOnSurface = true)
        {
            Index = index;
            Role = role;
            _standing = startOnSurface ? PlayerStanding.Surface : PlayerStanding.Underground;
        }

        /// <summary>Slot in the party, 0–3. Stable for the whole match, and what telemetry keys on rather than any identity (§13).</summary>
        public int Index { get; }

        /// <summary>
        /// The role taken in the lobby. Fixed for the match: §11 makes the absent
        /// role "그 판의 성격", which would mean nothing if the set could change
        /// halfway through.
        /// </summary>
        public RoleId Role { get; }

        /// <summary>Where this player stands. §02 / §09.</summary>
        public PlayerStanding Standing
        {
            get { return _standing; }
        }

        /// <summary>Alive — that is, not a ghost. An escaped player is still alive; they are simply out of reach.</summary>
        public bool IsAlive
        {
            get { return _standing != PlayerStanding.Dead; }
        }

        /// <summary>Dead, and therefore a ghost. §09 has no other death state.</summary>
        public bool IsGhost
        {
            get { return _standing == PlayerStanding.Dead; }
        }

        /// <summary>Left the match alive. §02 counts these — one is enough to keep the match's information.</summary>
        public bool HasEscaped
        {
            get { return _standing == PlayerStanding.Escaped; }
        }

        /// <summary>
        /// Still in the match: alive and not yet out. §02 cannot be decided while any
        /// of these remain, which is what makes "지금 나갈까?" a live question rather
        /// than a post-mortem.
        /// </summary>
        public bool IsStillInPlay
        {
            get { return _standing == PlayerStanding.Underground || _standing == PlayerStanding.Surface; }
        }

        /// <summary>
        /// At the vehicle. §03 makes this the only place an injury heals and §07 the
        /// only place the time can be read; an escaped player counts as up here too,
        /// since 탈출 happens at the vehicle.
        /// </summary>
        public bool IsOnSurface
        {
            get { return _standing == PlayerStanding.Surface || _standing == PlayerStanding.Escaped; }
        }

        /// <summary>
        /// Below ground. A ghost counts as down here: §09 keeps it in the building,
        /// watching, which is the whole point of the penalty.
        /// </summary>
        public bool IsUnderground
        {
            get { return _standing == PlayerStanding.Underground || _standing == PlayerStanding.Dead; }
        }

        /// <summary>The ghost this player became, or null while alive. §09.</summary>
        public GhostState? Ghost
        {
            get { return _ghost; }
        }

        /// <summary>Where they fell, or null if they have not. §08 drops their loot on this exact spot.</summary>
        public Vec3? DeathPosition
        {
            get { return _ghost != null ? (Vec3?)_ghost.DeathPosition : null; }
        }

        /// <summary>
        /// Hurt. §03 lists 부상 among the resources a round trip consumes, gained
        /// "잡혔다 탈출 시" and replenished "지상에서만".
        /// <para>
        /// A flag, not a severity: the document names no levels, no stacking and — see
        /// the report in docs/BALANCE-FINDINGS.md — no penalty either, so inventing a
        /// number here would be inventing balance rather than encoding it.
        /// </para>
        /// </summary>
        public bool IsInjured
        {
            get { return _injured; }
        }

        /// <summary>
        /// Whether the injury can be dealt with right now. §03: on the surface, and
        /// nowhere else. Whether a 응급킷 (§08) is also spent is the economy's rule;
        /// the place is this one's.
        /// </summary>
        public bool CanTreatInjury
        {
            get { return _injured && _standing == PlayerStanding.Surface; }
        }

        /// <summary>Holding the objective. §03's last decision of the match — "누가 들 것인가".</summary>
        public bool IsCarryingObjective
        {
            get { return _carryingObjective; }
        }

        /// <summary>Carrying any 전리품 at all, pushed in from the economy. §08.</summary>
        public bool HoldsLoot
        {
            get { return _holdsLoot; }
        }

        /// <summary>One end of an oversize piece (§08's 2인 운반) is in these hands. Pushed in from the economy.</summary>
        public bool OversizePieceInHands
        {
            get { return _oversizePieceInHands; }
        }

        /// <summary>
        /// Both hands free. §03 ("양손을 쓴다") and §08's oversize piece compete for
        /// the same pair of hands, so a player can hold one of the two, never both.
        /// </summary>
        public bool HandsFree
        {
            get { return !_carryingObjective && !_oversizePieceInHands; }
        }

        /// <summary>
        /// Whether a flashlight can be held. §03: the objective carrier cannot, which
        /// is what makes the escort two people — "운반자는 앞을 보지 못하므로 사실상
        /// 2인 1조 호송".
        /// </summary>
        public bool CanHoldFlashlight
        {
            get { return IsAlive && HandsFree; }
        }

        /// <summary>
        /// Whether the hands allow a sprint. §03: "주자가 들면 질주 불가" — the one
        /// rule that makes handing the objective to the Runner a real cost. Stamina
        /// and load belong to movement and the economy; this is only the hands.
        /// </summary>
        public bool CanSprint
        {
            get { return IsAlive && !_carryingObjective; }
        }

        /// <summary>
        /// Whether this player's microphone may transmit. False for a ghost — §09's
        /// 말하기 row is "불가능".
        /// <para>
        /// Gated at the sender, like §13's voice cutoff and for the same reason:
        /// "전부 받아놓고 볼륨만 0으로 재생하면 클라이언트 조작으로 다 들린다." A
        /// ghost that could be unmuted client-side would hand the team free
        /// information and take §09's balance argument with it.
        /// </para>
        /// </summary>
        public bool MayTransmitVoice
        {
            get { return IsAlive; }
        }

        /// <summary>
        /// Whether the objective could be taken right now. §03: both hands, and no
        /// 전리품 at the same time — "전리품은 그 전에 다 챙겨야 한다."
        /// </summary>
        public bool CanTakeObjective
        {
            get { return IsStillInPlay && !_carryingObjective && !_holdsLoot && !_oversizePieceInHands; }
        }

        /// <summary>
        /// Goes down. False when already below, or when the standing is terminal —
        /// §09's ghost stays where it died and an escapee does not come back.
        /// </summary>
        public bool TryDescend()
        {
            if (_standing != PlayerStanding.Surface)
            {
                return false;
            }

            _standing = PlayerStanding.Underground;
            return true;
        }

        /// <summary>
        /// Comes up to the vehicle. §03's 숨 돌리기 — not a reset; the clock and the
        /// injury both survive it, and only the surface can undo the injury.
        /// </summary>
        public bool TrySurface()
        {
            if (_standing != PlayerStanding.Underground)
            {
                return false;
            }

            _standing = PlayerStanding.Surface;
            return true;
        }

        /// <summary>
        /// Records an injury. §03: "잡혔다 탈출 시 (주자)" — surviving a grab, which
        /// is the only source the document names.
        /// </summary>
        /// <returns>
        /// False when the player is already hurt, or is not below ground. A grab needs
        /// the monster, and the monster is in the building (§01, §08) — the same
        /// reasoning that keeps deaths underground. No stacking either: §03 describes
        /// one 부상, healed in one place, and a second one would be a severity system
        /// the document does not have.
        /// </returns>
        public bool TryInjure()
        {
            if (_injured || _standing != PlayerStanding.Underground)
            {
                return false;
            }

            _injured = true;
            return true;
        }

        /// <summary>
        /// Heals the injury. §03 allows it on the surface only, which is one of the
        /// reasons a round trip exists at all.
        /// <para>
        /// The caller has already spent whatever §08 charges — 응급킷 is sold at the
        /// vehicle, so the item and the place agree. See docs/BALANCE-FINDINGS.md on
        /// the document never saying which of the two is the actual gate.
        /// </para>
        /// </summary>
        /// <returns>False when not hurt, or not on the surface.</returns>
        public bool TryTreatInjury()
        {
            if (!CanTreatInjury)
            {
                return false;
            }

            _injured = false;
            return true;
        }

        /// <summary>
        /// Pushes the economy's answer in. One call for both booleans so the two can
        /// never disagree halfway through a frame.
        /// </summary>
        /// <param name="holdsAnyLoot">Anything at all in pockets or hands. §08.</param>
        /// <param name="oversizePieceInHands">A share of a 대형 전리품, which occupies both hands. §08's 2인 운반.</param>
        public void SetLootState(bool holdsAnyLoot, bool oversizePieceInHands)
        {
            _holdsLoot = holdsAnyLoot || oversizePieceInHands;
            _oversizePieceInHands = oversizePieceInHands;
        }

        /// <summary>
        /// Kills the player and turns them into a ghost at that spot. §09.
        /// <para>
        /// Below ground only. §01 and §08 both label the surface a safe zone — "지상
        /// (안전 · 상점)", "지상 차량 = 안전 지대" — and §03's partial reset leaves the
        /// monster in the building when the team walks out. Neither section states
        /// "you cannot die up here" in those words, so this is an inference; it is the
        /// one that keeps 숨 돌리기 meaning anything.
        /// </para>
        /// <para>
        /// Also refused for a ghost and for somebody already out: dying twice would
        /// mint a second <see cref="GhostState"/> and silently move the loot pile §08
        /// already owes the team.
        /// </para>
        /// </summary>
        internal bool Kill(Vec3 deathPosition)
        {
            if (_standing != PlayerStanding.Underground)
            {
                return false;
            }

            _standing = PlayerStanding.Dead;
            _ghost = new GhostState(deathPosition);

            // Hands empty out. Whatever was in them is now on the floor (§08); where
            // the objective went is MatchState's business, since it read the flag
            // before calling.
            _carryingObjective = false;
            _holdsLoot = false;
            _oversizePieceInHands = false;
            return true;
        }

        /// <summary>
        /// Leaves the match for good. §02's 탈출.
        /// <para>
        /// From the surface only, because that is where §08 puts the vehicle: getting
        /// out means walking up first, which is precisely the trip the objective
        /// carrier cannot make alone in the dark (§03).
        /// </para>
        /// </summary>
        internal bool Extract()
        {
            if (_standing != PlayerStanding.Surface)
            {
                return false;
            }

            _standing = PlayerStanding.Escaped;
            return true;
        }

        /// <summary>Hands the objective over, or takes it back. Uniqueness across the party is <see cref="MatchState"/>'s invariant.</summary>
        internal void SetCarryingObjective(bool carrying)
        {
            _carryingObjective = carrying;
        }
    }
}
