namespace HorrorGame.Core.Movement
{
    /// <summary>
    /// Everything except the input that decides how fast a player moves this frame.
    /// <para>
    /// Every field is a primitive on purpose. §08's weight bands live in the
    /// economy, the objective lives in the clue system, and neither is referenced
    /// here — the economy hands movement a <c>float</c> (<c>Inventory.SpeedMultiplier</c>)
    /// and movement multiplies. That seam is what lets the two systems be written
    /// and tested independently.
    /// </para>
    /// <para>
    /// §08: "§05 배율에 곱연산으로 적용된다." Load, objective carry and bag all stack
    /// multiplicatively on top of the directional multiplier, so two penalties never
    /// cancel and none of them can be escaped by combining them.
    /// </para>
    /// </summary>
    public struct MovementContext
    {
        /// <summary>
        /// The unmodified speed for the movement mode the player is in, m/s:
        /// <see cref="GameConstants.WalkSpeed"/>, <see cref="GameConstants.RunSpeed"/>
        /// or <see cref="GameConstants.RunnerSprintSpeed"/> (§06).
        /// <see cref="SpeedResolver.SelectBaseSpeed"/> picks it.
        /// </summary>
        public float BaseSpeed;

        /// <summary>
        /// The §08 carry-weight multiplier, where 1 means unloaded. Comes straight
        /// from <c>Inventory.SpeedMultiplier</c>.
        /// <para>
        /// Note that a default-constructed <see cref="MovementContext"/> leaves this
        /// at 0 and therefore does not move. That is deliberate: silently reading 0
        /// as "unloaded" would hide a context that was never filled in. Use the
        /// constructor or <see cref="Unloaded"/>.
        /// </para>
        /// </summary>
        public float LoadMultiplier;

        /// <summary>
        /// Carrying the objective. §03: both hands are used, so there is no
        /// flashlight and no sprint, and §05 applies
        /// <see cref="GameConstants.ObjectiveCarrySpeedMultiplier"/> on top. This is
        /// what makes "누가 들 것인가" the last real decision of a match.
        /// </summary>
        public bool CarryingObjective;

        /// <summary>Bag equipped: +5 capacity for −10% speed (§08).</summary>
        public bool BagEquipped;

        /// <summary>Builds a full context.</summary>
        /// <param name="baseSpeed">Walk, run or sprint speed, m/s (§06).</param>
        /// <param name="loadMultiplier">§08 weight multiplier; 1 = unloaded.</param>
        /// <param name="carryingObjective">§03 objective carry.</param>
        /// <param name="bagEquipped">§08 bag.</param>
        public MovementContext(
            float baseSpeed, float loadMultiplier, bool carryingObjective = false, bool bagEquipped = false)
        {
            BaseSpeed = baseSpeed;
            LoadMultiplier = loadMultiplier;
            CarryingObjective = carryingObjective;
            BagEquipped = bagEquipped;
        }

        /// <summary>
        /// A context with no penalties at all — empty hands, no bag, no objective.
        /// The baseline the §05 table is quoted against, and the shape most tests
        /// and telemetry comparisons want.
        /// </summary>
        /// <param name="baseSpeed">Walk, run or sprint speed, m/s (§06).</param>
        public static MovementContext Unloaded(float baseSpeed) => new MovementContext(baseSpeed, 1f);
    }
}
