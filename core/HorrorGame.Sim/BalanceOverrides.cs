using System;
using HorrorGame.Core;
using HorrorGame.Core.Economy;

namespace HorrorGame.Sim
{
    /// <summary>
    /// The one place the simulator is allowed to disagree with
    /// <see cref="GameConstants"/>, and only for the duration of a sweep.
    /// <para>
    /// <b>Why this exists.</b> §16-2 asks what happens when a tuned number moves,
    /// and every tuned number in this project is a <c>const</c> — by design, because
    /// ARCHITECTURE.md wants one authority for balance. A sweep therefore cannot
    /// mutate the constant; it has to shadow it. This struct is that shadow, and it
    /// is deliberately tiny: two knobs, both defaulting to the shipped value, and a
    /// self-check that fails loudly if the shadow ever stops agreeing with Core at
    /// those defaults.
    /// </para>
    /// <para>
    /// <b>What it is not.</b> It is not a second balance table. Everything not named
    /// here is read from <see cref="GameConstants"/> at the point of use, and
    /// <see cref="IsDefault"/> is true for an ordinary match — which is the state
    /// every non-sweep command runs in, so the numbers the simulator reports are the
    /// game's numbers unless the command line says otherwise.
    /// </para>
    /// </summary>
    public readonly struct BalanceOverrides
    {
        private BalanceOverrides(float weightMulLight, float lootValueScale)
        {
            WeightMulLight = weightMulLight;
            LootValueScale = lootValueScale;
        }

        /// <summary>
        /// §08's first penalty band. The subject of <c>docs/BALANCE-FINDINGS.md</c>
        /// F-001: at the shipped 0.85 a sprinting Runner makes 4.76 m/s against a
        /// 4.8 m/s monster, so band 2 is not a slowdown but a switch. F-001's option
        /// 2 proposes 0.90.
        /// </summary>
        public float WeightMulLight { get; }

        /// <summary>
        /// A multiplier on what 전리품 fetches at the vehicle. This is §16-2's ratio
        /// stated as one number: prices are §08's table, income is that table times
        /// this. 1.0 is the shipped economy.
        /// </summary>
        public float LootValueScale { get; }

        /// <summary>Exactly what <see cref="GameConstants"/> says today.</summary>
        public static BalanceOverrides Default =>
            new BalanceOverrides(GameConstants.WeightMulLight, 1f);

        /// <summary>True when nothing is shadowed and the match is the shipped game.</summary>
        public bool IsDefault =>
            WeightMulLight == GameConstants.WeightMulLight && LootValueScale == 1f;

        /// <summary>Same overrides with a different §08 band-2 multiplier.</summary>
        public BalanceOverrides WithWeightMulLight(float value) =>
            new BalanceOverrides(value, LootValueScale);

        /// <summary>Same overrides with a different §16-2 loot-value ratio.</summary>
        public BalanceOverrides WithLootValueScale(float value) =>
            new BalanceOverrides(WeightMulLight, value);

        /// <summary>
        /// The §08 load multiplier that reaches <c>MovementContext.LoadMultiplier</c>.
        /// <para>
        /// Structurally identical to <see cref="CarryLoad.Resolve"/> — same band
        /// boundaries, same bag term, same order — with band 2's multiplier taken
        /// from <see cref="WeightMulLight"/> instead of the constant.
        /// <see cref="SelfCheck"/> proves the two agree whenever the override is the
        /// shipped value, which is what makes it safe to trust the swept values.
        /// </para>
        /// </summary>
        public float LoadMultiplier(int totalWeight, bool bagEquipped)
        {
            float band;
            if (totalWeight <= GameConstants.WeightFreeMax)
            {
                band = 1f;
            }
            else if (totalWeight <= GameConstants.WeightLightMax)
            {
                band = WeightMulLight;
            }
            else if (totalWeight <= GameConstants.WeightHeavyMax)
            {
                band = GameConstants.WeightMulHeavy;
            }
            else
            {
                band = GameConstants.WeightMulOverloaded;
            }

            return bagEquipped ? band * GameConstants.BagSpeedMultiplier : band;
        }

        /// <summary>
        /// Credits a sale is worth under <see cref="LootValueScale"/>, given what
        /// Core's <see cref="Shop"/> already paid.
        /// </summary>
        /// <param name="coreCredits">What <c>Shop.SellAll</c> deposited.</param>
        /// <returns>The adjustment to apply on top; negative means the team is refunded less.</returns>
        public int SaleAdjustment(int coreCredits)
        {
            if (LootValueScale == 1f || coreCredits <= 0)
            {
                return 0;
            }

            return (int)MathF.Round(coreCredits * LootValueScale) - coreCredits;
        }

        /// <summary>
        /// Proves the shadow is a shadow. Walks every weight from empty to well past
        /// the last band, with and without a bag, and requires
        /// <see cref="LoadMultiplier"/> at the shipped override to equal
        /// <see cref="CarryLoad.Resolve"/> bit for bit.
        /// <para>
        /// Run before every sweep. A sweep whose zero point does not reproduce the
        /// shipped game is not measuring a change, it is measuring two different
        /// simulators, and the difference would look exactly like a balance finding.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">The shadow has drifted from Core.</exception>
        public static void SelfCheck()
        {
            var shipped = Default;
            var limit = (GameConstants.WeightHeavyMax + GameConstants.LootWeightLargePiece) * 2;

            for (var weight = 0; weight <= limit; weight++)
            {
                for (var bag = 0; bag < 2; bag++)
                {
                    var bagged = bag == 1;
                    var mine = shipped.LoadMultiplier(weight, bagged);
                    var theirs = CarryLoad.Resolve(weight, bagged);
                    if (mine != theirs)
                    {
                        throw new InvalidOperationException(
                            "BalanceOverrides has drifted from CarryLoad at weight " + weight
                            + (bagged ? " with a bag: " : ": ") + mine + " vs " + theirs
                            + ". Every sweep result would be measuring the drift.");
                    }
                }
            }
        }
    }
}
