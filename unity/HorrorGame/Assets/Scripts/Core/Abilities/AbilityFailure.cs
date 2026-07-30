namespace HorrorGame.Core.Abilities
{
    /// <summary>
    /// Why a §04 ability is not producing its effect.
    /// <para>
    /// Every ability reports one of these instead of a bare bool, because the HUD
    /// has to explain itself. "15 m 이내로 들어가라" and "자재가 없다" are different
    /// problems with different fixes, and a player who cannot tell them apart reads
    /// the ability as broken rather than as constrained. §10's dilemma map only
    /// works when the cost is legible at the exact moment it bites.
    /// </para>
    /// <para>
    /// One enum serves all five abilities on purpose: the HUD renders reasons from a
    /// single table, and the four players share one vocabulary for what went wrong.
    /// Each ability documents the subset it can return.
    /// </para>
    /// </summary>
    public enum AbilityFailure
    {
        /// <summary>Nothing is wrong — the ability is doing what it does.</summary>
        None = 0,

        /// <summary>The ability has not been driven yet, or is switched off.</summary>
        Inactive = 1,

        /// <summary>Still recovering. §04 trades the Flasher's strength for reusability, and this is the other half of that trade.</summary>
        OnCooldown = 2,

        /// <summary>The monster is further away than the ability reaches.</summary>
        OutOfRange = 3,

        /// <summary>In range, but outside the cone. §04 — the flash has to be aimed.</summary>
        OutsideCone = 4,

        /// <summary>Something opaque is in the way. Light needs a sight line; sound does not.</summary>
        LineOfSightBlocked = 5,

        /// <summary>
        /// The monster is making no sound at all. §06's 정지 row is the only state
        /// whose 소리 column is 없음, and silencing the Listener is what that state
        /// is for: "침묵이 가장 무서운 소리다."
        /// </summary>
        MonsterSilent = 6,

        /// <summary>
        /// The listener is the loud one. §04: "자기가 소리를 내면 못 듣는다" —
        /// running or opening a door cuts the feed.
        /// </summary>
        SelfNoise = 7,

        /// <summary>Translation has not stopped. §04/§05 — the Observer's feet must be still; its head need not be.</summary>
        Moving = 8,

        /// <summary>Standing still, but not yet for long enough. §04's 3 초.</summary>
        NotStillLongEnough = 9,

        /// <summary>
        /// No path exists between the monster and the Runner, so the taunt has
        /// nothing to pull. §12 — a Runner sealed behind a locked door cannot
        /// deliver the monster anywhere.
        /// </summary>
        MonsterUnreachable = 10,

        /// <summary>Not enough 정비 자재. §04: "시간과 자재."</summary>
        NoMaterials = 11,

        /// <summary>A job is already in progress. §04 — one pair of hands, one install at a time.</summary>
        Busy = 12,

        /// <summary>There is nothing at this site to act on, or it has already been done.</summary>
        NothingToActOn = 13,

        /// <summary>The sprint bar is empty. §06: "주자도 스태미나가 끝나면 잡힌다."</summary>
        OutOfStamina = 14,

        /// <summary>Both hands are on the objective. §03: "주자가 들면 질주 불가."</summary>
        CarryingObjective = 15,

        /// <summary>Carrying too much to sprint. §08 — at weight 16 the sprint is gone, even for the Runner.</summary>
        LoadTooHeavy = 16,
    }
}
