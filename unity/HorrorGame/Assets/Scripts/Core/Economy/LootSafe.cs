using HorrorGame.Core.Math;
using HorrorGame.Core.Roles;

namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// What one tick of work on a safe achieved.
    /// </summary>
    public enum SafeWorkResult
    {
        /// <summary>Nobody is working it. Time passing does not open a safe.</summary>
        Idle = 0,

        /// <summary>Someone is trying, but §08 puts this loot behind the Engineer and they are not one.</summary>
        WrongRole = 1,

        /// <summary>The Engineer is working. Progress advanced (or the frame was zero-length).</summary>
        Working = 2,

        /// <summary>It opened on this tick. Reported exactly once, whatever the frame length was.</summary>
        JustOpened = 3,

        /// <summary>Already open. Nothing left to do here.</summary>
        AlreadyOpen = 4,
    }

    /// <summary>
    /// A safe holding one 금고 속 문서.
    /// <para>
    /// §08 gives this loot the note "금고를 열어야 함 (정비공)", and §04 makes the
    /// Engineer a preparation role — "즉석 사용 불가". Together they mean the
    /// document costs <see cref="GameConstants.EngineerSafeSeconds"/> of a specific
    /// player standing still in a dangerous room, which is the whole reason the
    /// piece is worth more per weight than a trinket.
    /// </para>
    /// <para>
    /// Progress is not lost when the Engineer walks away. Nothing in the design says
    /// a half-drilled safe re-locks itself, and inventing a decay rate would invent
    /// a tuned number the document never priced — so the safe remembers, and
    /// interruptions cost only the time already spent.
    /// </para>
    /// </summary>
    public sealed class LootSafe
    {
        private float _workedSeconds;
        private bool _open;
        private bool _emptied;

        /// <summary>Whether the door is open.</summary>
        public bool IsOpen => _open;

        /// <summary>Whether the document has been taken. An opened, emptied safe is scenery.</summary>
        public bool IsEmptied => _emptied;

        /// <summary>Seconds of Engineer work banked so far, clamped to the requirement.</summary>
        public float WorkedSeconds => _workedSeconds;

        /// <summary>Progress 0–1, for the interaction bar.</summary>
        public float Progress01 =>
            GameConstants.EngineerSafeSeconds > 0f
                ? MathX.Clamp01(_workedSeconds / GameConstants.EngineerSafeSeconds)
                : 1f;

        /// <summary>What is inside. §08's table has exactly one thing in a safe.</summary>
        public LootId Contents => LootId.SafeDocument;

        /// <summary>
        /// Advances the drilling.
        /// <para>
        /// Takes the delta explicitly and never reads a clock, so a seeded replay
        /// opens the safe on the same tick (ARCHITECTURE §2). A frame spike cannot
        /// overshoot: the banked time is clamped to the requirement and
        /// <see cref="SafeWorkResult.JustOpened"/> is returned exactly once, so a
        /// 10-second hitch produces the same sequence of transitions as 500 normal
        /// frames — nothing tunnels past the open.
        /// </para>
        /// </summary>
        /// <param name="deltaSeconds">Elapsed time. Zero or negative advances nothing, which is what a paused or clamped frame should mean.</param>
        /// <param name="workerRole">The role currently interacting, or <see cref="RoleId.None"/> when nobody is.</param>
        public SafeWorkResult Tick(float deltaSeconds, RoleId workerRole)
        {
            if (_open)
            {
                return SafeWorkResult.AlreadyOpen;
            }

            if (workerRole == RoleId.None)
            {
                return SafeWorkResult.Idle;
            }

            if (workerRole != RoleId.Engineer)
            {
                // §08: the safe is the Engineer's income and nobody else's. §11's
                // rule that no role may be compulsory holds because this loot is
                // optional (§03) — a team without an Engineer loses money, not the
                // match.
                return SafeWorkResult.WrongRole;
            }

            if (deltaSeconds <= 0f)
            {
                return SafeWorkResult.Working;
            }

            _workedSeconds += deltaSeconds;
            if (_workedSeconds >= GameConstants.EngineerSafeSeconds)
            {
                _workedSeconds = GameConstants.EngineerSafeSeconds;
                _open = true;
                return SafeWorkResult.JustOpened;
            }

            return SafeWorkResult.Working;
        }

        /// <summary>
        /// Moves the document into a player's inventory.
        /// <para>
        /// Fails if the safe is shut, already emptied, or the player has no room —
        /// and a failure for lack of room leaves the document in the safe rather
        /// than on the floor, so the team can come back for it once someone sells.
        /// </para>
        /// </summary>
        public bool TryTakeContents(Inventory? taker)
        {
            if (taker == null || !_open || _emptied)
            {
                return false;
            }

            if (!taker.TryAdd(LootId.SafeDocument))
            {
                return false;
            }

            _emptied = true;
            return true;
        }
    }
}
