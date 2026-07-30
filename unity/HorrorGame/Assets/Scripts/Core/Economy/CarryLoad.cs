namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// §08's weight-to-speed table as pure functions.
    /// <code>
    /// 총 무게 5 이하  →  정상 속도
    ///        6~10    →  −15%
    ///        11~15   →  −30%
    ///        16 이상  →  질주 불가 (주자도)
    /// </code>
    /// <para>
    /// Split out from <see cref="Inventory"/> because the bands are the part §16-2
    /// sweeps and the part every other system quotes: the shop UI wants to show
    /// what one more piece would cost, telemetry wants the band index, and the
    /// simulator wants the multiplier without building an inventory. §08 is
    /// explicit that the result is multiplicative — "§05 배율에 곱연산으로 적용된다"
    /// — so nothing here ever adds or subtracts a speed.
    /// </para>
    /// </summary>
    public static class CarryLoad
    {
        /// <summary>
        /// The §08 band multiplier for a total weight, ignoring the bag.
        /// <para>
        /// Bands are closed at their upper bound, so weight 5 is still free and 6 is
        /// the first penalty — the boundary the design reasons about. Weights below
        /// zero cannot happen through <see cref="Inventory"/>, but a corrupt network
        /// value must read as "unencumbered" rather than as a speed boost.
        /// </para>
        /// </summary>
        public static float BandMultiplier(int totalWeight)
        {
            if (totalWeight <= GameConstants.WeightFreeMax)
            {
                return 1f;
            }

            if (totalWeight <= GameConstants.WeightLightMax)
            {
                return GameConstants.WeightMulLight;
            }

            if (totalWeight <= GameConstants.WeightHeavyMax)
            {
                return GameConstants.WeightMulHeavy;
            }

            return GameConstants.WeightMulOverloaded;
        }

        /// <summary>
        /// Whether sprinting is possible at a total weight. §08's fourth band bans
        /// it outright and says "주자도" — the Runner's identity is not an exemption.
        /// </summary>
        public static bool CanSprintAt(int totalWeight) => totalWeight <= GameConstants.WeightHeavyMax;

        /// <summary>
        /// Band number 0–3, for telemetry and for the shop's "one more piece would
        /// cost you" hint. Kept separate from the multiplier so a histogram of
        /// player loads does not depend on the tuned values.
        /// </summary>
        public static int BandIndexOf(int totalWeight)
        {
            if (totalWeight <= GameConstants.WeightFreeMax)
            {
                return 0;
            }

            if (totalWeight <= GameConstants.WeightLightMax)
            {
                return 1;
            }

            if (totalWeight <= GameConstants.WeightHeavyMax)
            {
                return 2;
            }

            return 3;
        }

        /// <summary>
        /// The complete §08 load multiplier: the band times the bag's own −10%.
        /// <para>
        /// The bag is a second multiplier rather than extra weight on purpose. §08
        /// gives it a flat −10% and a +5 capacity, so a player who buys one but
        /// carries nothing is still slower — the cost is the strap, not the
        /// contents. Adding it as weight instead would make an empty bag free and a
        /// full one doubly punished.
        /// </para>
        /// <para>
        /// This is the value that reaches <c>MovementContext.LoadMultiplier</c>.
        /// The objective's own −20% (§03) is applied by movement from
        /// <c>MovementContext.CarryingObjective</c>, so it must not appear here or
        /// it lands twice.
        /// </para>
        /// </summary>
        public static float Resolve(int totalWeight, bool bagEquipped)
        {
            var m = BandMultiplier(totalWeight);
            return bagEquipped ? m * GameConstants.BagSpeedMultiplier : m;
        }

        /// <summary>
        /// Weight units a player can hold. §08 gives the bag "+5 수용"; the base
        /// figure is the unbagged allowance.
        /// </summary>
        public static int CapacityFor(bool bagEquipped) =>
            GameConstants.BaseCarryCapacity + (bagEquipped ? GameConstants.BagCapacityBonus : 0);
    }
}
