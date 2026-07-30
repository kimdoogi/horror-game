using HorrorGame.Core.Math;

namespace HorrorGame.Core.Ghost
{
    /// <summary>
    /// Why a rattle did not happen. §09 gives the ghost one verb and a 45 s
    /// cooldown, so the two ways of failing have to be told apart: the HUD showing
    /// "45초 남음" and the HUD showing "너무 멀다" are different kinds of anguish, and
    /// only one of them is worth walking towards something to fix.
    /// </summary>
    public enum GhostSignalFailure
    {
        /// <summary>The object was rattled.</summary>
        None = 0,

        /// <summary>Still inside <see cref="GameConstants.GhostRattleCooldownSeconds"/>. §09.</summary>
        OnCooldown = 1,

        /// <summary>
        /// Further than <see cref="GameConstants.GhostRattleRange"/>, or a
        /// non-finite position. §09 says "근처 물건", so the ghost has to be beside
        /// the thing it wants to move.
        /// </summary>
        OutOfRange = 2,
    }

    /// <summary>
    /// One attempt to shake something. §09's 신호 row — "근처 물건을 아주 약하게
    /// 흔든다 (쿨타임 45초)".
    /// <para>
    /// This is the ghost's entire outbound bandwidth, and the shape of the struct
    /// is the argument: it carries a place and nothing else. There is no text, no
    /// target player, no field for what the ghost knows. §09's balance rule —
    /// "죽은 사람이 정보를 주면 밸런스 붕괴" — survives because a rattle can only
    /// ever mean "here", never "the objective is on floor three".
    /// </para>
    /// </summary>
    public readonly struct GhostRattle
    {
        /// <summary>True when something actually moved.</summary>
        public readonly bool Occurred;

        /// <summary>Where the noise came from. Meaningless when <see cref="Occurred"/> is false.</summary>
        public readonly Vec3 Position;

        /// <summary>Distance from the ghost to the object at the moment it tried, metres.</summary>
        public readonly float Distance;

        /// <summary>Seconds before another attempt can succeed. Zero on a successful rattle means the cooldown was just armed — read <see cref="GhostState.RattleCooldownRemaining"/> for that.</summary>
        public readonly float SecondsUntilReady;

        /// <summary>Why nothing moved, or <see cref="GhostSignalFailure.None"/>.</summary>
        public readonly GhostSignalFailure Failure;

        /// <summary>Builds a result. Produced by <see cref="GhostState.TryRattle"/>.</summary>
        public GhostRattle(bool occurred, Vec3 position, float distance, float secondsUntilReady, GhostSignalFailure failure)
        {
            Occurred = occurred;
            Position = position;
            Distance = distance;
            SecondsUntilReady = secondsUntilReady;
            Failure = failure;
        }
    }
}
