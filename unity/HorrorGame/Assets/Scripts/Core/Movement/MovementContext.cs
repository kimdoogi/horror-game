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

        // DELETED with §03 and §08: CarryingObjective and BagEquipped, and the two
        // optional constructor parameters that set them. They were the last two
        // reasons a runner could be slower than another runner. A race starts twenty
        // people who are identical — 「캐릭터는 다 똑같이 생겨도되지」 — so the only
        // thing that separates two runners is the route they picked and whether they
        // stopped to shut a door. Re-adding either means re-adding a thing to carry.

        /// <summary>Builds a context.</summary>
        /// <param name="baseSpeed">Walk, run or sprint speed, m/s (§06).</param>
        /// <param name="loadMultiplier">Stance multiplier; 1 = upright and unencumbered.</param>
        public MovementContext(float baseSpeed, float loadMultiplier)
        {
            BaseSpeed = baseSpeed;
            LoadMultiplier = loadMultiplier;
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
