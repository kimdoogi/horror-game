#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Economy;

namespace HorrorGame.UI.Readouts
{
    /// <summary>
    /// What the player is carrying and what it is costing them. §08.
    /// <para>
    /// §08 states the consequence in one line — <em>"욕심이 곧 속도 저하이고, 속도
    /// 저하가 곧 죽음이다"</em> — and §10 lists "전리품을 챙긴다 / 시간을 쓰고
    /// 느려진다" as a dilemma the player is supposed to feel while deciding. A HUD
    /// that showed only a weight number would leave the second half of that trade to
    /// be inferred, so this readout carries the penalty as its own field and the HUD
    /// prints both.
    /// </para>
    /// <para>
    /// The penalty shown is <c>Inventory.SpeedMultiplier</c>, which is the exact
    /// float movement multiplies by (ARCHITECTURE §3's seam). It therefore already
    /// includes the bag's −10%, and deliberately excludes the objective's −20%,
    /// which movement applies from its own carry flag — showing it here would tell
    /// the player they are paying it twice.
    /// </para>
    /// </summary>
    public readonly struct LoadReadout
    {
        /// <summary>Builds a readout directly. Prefer <see cref="From"/>.</summary>
        public LoadReadout(int totalWeight, int capacity, float speedMultiplier, int band, bool canSprint, bool bagEquipped)
        {
            TotalWeight = totalWeight;
            Capacity = capacity;
            SpeedMultiplier = speedMultiplier;
            Band = band;
            CanSprint = canSprint;
            BagEquipped = bagEquipped;
        }

        /// <summary>Weight units carried — the number §08's band table is indexed by.</summary>
        public int TotalWeight { get; }

        /// <summary>Weight units the player could hold, bag included.</summary>
        public int Capacity { get; }

        /// <summary>The multiplier movement is applying right now. 1.0 while inside §08's free band.</summary>
        public float SpeedMultiplier { get; }

        /// <summary>§08's band, 0–3. Three is "질주 불가 (주자도)".</summary>
        public int Band { get; }

        /// <summary>Whether sprinting is still possible at this weight. §08's fourth band bans it for everybody.</summary>
        public bool CanSprint { get; }

        /// <summary>Whether a 가방 is strapped on — the −10% that applies even when the bag is empty.</summary>
        public bool BagEquipped { get; }

        /// <summary>
        /// Whether the HUD draws this at all. Hidden with empty hands: §01 is a horror
        /// game and a readout that says "0" is a readout that has nothing to say.
        /// </summary>
        public bool IsVisible
        {
            get { return TotalWeight > 0 || BagEquipped; }
        }

        /// <summary>The speed loss as whole percent — the form §08 writes its own table in ("−15%", "−30%").</summary>
        public int PenaltyPercent
        {
            get
            {
                var loss = (1f - SpeedMultiplier) * 100f;
                return (int)(loss + 0.5f);
            }
        }

        /// <summary>Whether the player is currently paying anything for what they carry.</summary>
        public bool IsPenalised
        {
            get { return PenaltyPercent > 0; }
        }

        /// <summary>
        /// How full the pack is, 0–1, for the bar. Clamped at the top because
        /// §03's objective adds weight without adding capacity — a carrier is over
        /// the line by design, not by a bug.
        /// </summary>
        public float Fill01
        {
            get
            {
                if (Capacity <= 0)
                {
                    return 0f;
                }

                var f = (float)TotalWeight / Capacity;
                return f > 1f ? 1f : f;
            }
        }

        /// <summary>
        /// What one more of the smallest piece of loot would cost, or 0 when nothing
        /// changes. §10 wants the trade legible <em>at the moment of choosing</em>,
        /// and the moment is standing over a 은수저 deciding — not afterwards.
        /// </summary>
        public int PenaltyPercentAfterOneMore
        {
            get
            {
                var next = CarryLoad.Resolve(TotalWeight + GameConstants.LootWeightTrinket, BagEquipped);
                var loss = (1f - next) * 100f;
                return (int)(loss + 0.5f);
            }
        }

        /// <summary>True when picking up one more trinket would drop the player into a worse §08 band.</summary>
        public bool OneMoreCosts
        {
            get { return PenaltyPercentAfterOneMore > PenaltyPercent; }
        }

        /// <summary>Reads a player's pockets. A null inventory reads as empty hands rather than throwing during scene setup.</summary>
        public static LoadReadout From(Inventory? inventory)
        {
            if (inventory == null)
            {
                return new LoadReadout(0, CarryLoad.CapacityFor(false), 1f, 0, true, false);
            }

            return new LoadReadout(
                inventory.TotalWeight,
                inventory.Capacity,
                inventory.SpeedMultiplier,
                inventory.WeightBand,
                inventory.CanSprint,
                inventory.BagEquipped);
        }
    }
}
