using System.Collections.Generic;
using HorrorGame.Core.Roles;

namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// §08's 분류 column: what kind of problem an item solves.
    /// <para>
    /// Kept as data because the growth curve is expressed in these terms — §08's
    /// table says "1차 귀환 후: 소모품 몇 개" and "2~3차 귀환 후: 강화 아이템 1개", so
    /// the category is what the balance test measures against.
    /// </para>
    /// </summary>
    public enum ShopCategory
    {
        /// <summary>Not a real item.</summary>
        None = 0,

        /// <summary>소모품 — burned every descent. The floor of the team's spending.</summary>
        Consumable = 1,

        /// <summary>탐험 — changes how far the team can get.</summary>
        Exploration = 2,

        /// <summary>정보 — changes what the team knows.</summary>
        Information = 3,

        /// <summary>위험 — changes where the monster is, or who can hear it.</summary>
        Danger = 4,
    }

    /// <summary>
    /// One row of §08's 구매 목록, with its 효과 and its 대가.
    /// <para>
    /// Every flag here is a drawback the document states, because §08 opens the
    /// table by insisting each item has one. They are exposed as bools rather than
    /// as behaviour so that the systems that own the consequences — light, audio,
    /// the monster's perception — read a primitive and never reference the economy
    /// (ARCHITECTURE §3).
    /// </para>
    /// </summary>
    public readonly struct ShopItemDefinition
    {
        /// <summary>Creates a row. Only <see cref="ShopCatalogue"/> should need this.</summary>
        public ShopItemDefinition(
            ShopItemId id,
            ShopCategory category,
            int cost,
            bool isSingleUse,
            bool isCapabilityUpgrade,
            bool makesNoise,
            bool drawsTheMonster,
            bool isOneWay,
            bool disablesListener,
            RoleId substitutesFor)
        {
            Id = id;
            Category = category;
            Cost = cost;
            IsSingleUse = isSingleUse;
            IsCapabilityUpgrade = isCapabilityUpgrade;
            MakesNoise = makesNoise;
            DrawsTheMonster = drawsTheMonster;
            IsOneWay = isOneWay;
            DisablesListener = disablesListener;
            SubstitutesFor = substitutesFor;
        }

        /// <summary>Which item this row describes.</summary>
        public ShopItemId Id { get; }

        /// <summary>§08's 분류.</summary>
        public ShopCategory Category { get; }

        /// <summary>Credits. Proposed in §16-2's absence — see the derivation in <see cref="GameConstants"/>.</summary>
        public int Cost { get; }

        /// <summary>True when one purchase survives one use. §08 marks 조명탄 and 미끼 "1회용"; 소모품 are single use by definition.</summary>
        public bool IsSingleUse { get; }

        /// <summary>
        /// True when the item is what §08's growth table calls a 강화 아이템: a
        /// lasting purchase that changes where the team can go or what it can
        /// perceive.
        /// <para>
        /// 분필 is deliberately excluded even though it lasts. Its 효과 is "지나온 길
        /// 표시" — it records ground already walked and grants no new reach or
        /// perception, and §12's fixed map means a team that has learned the layout
        /// gets nothing from it. Pricing it as an upgrade would break §08's "1차
        /// 귀환 후: 소모품 몇 개" checkpoint for an item nobody would call an upgrade.
        /// </para>
        /// </summary>
        public bool IsCapabilityUpgrade { get; }

        /// <summary>Using it makes noise — §08's 대가 for 조명탄 and 감지기, and §04's Listener goes deaf while it happens.</summary>
        public bool MakesNoise { get; }

        /// <summary>Using it pulls the monster toward the user. §08: the upgraded flashlight is seen twice as far, and 괴물도 흔적을 따라온다.</summary>
        public bool DrawsTheMonster { get; }

        /// <summary>§08's 밧줄: "편도만". A shortcut you cannot come back through is a commitment, not a convenience.</summary>
        public bool IsOneWay { get; }

        /// <summary>
        /// §08's 소음기: "자기도 못 듣게 됨 → 청음사 무효". The only item in the list
        /// that removes a role's ability rather than adding to the team's, which is
        /// why §08 says outright "청음사가 있는 팀은 사면 안 된다".
        /// </summary>
        public bool DisablesListener { get; }

        /// <summary>
        /// The missing role this item partly covers, per §11's 돈으로 메우기 column,
        /// or <see cref="RoleId.None"/>. Every one of them is deliberately inferior
        /// to the role — §11 keeps roles valuable precisely by making the purchase
        /// worse.
        /// </summary>
        public RoleId SubstitutesFor { get; }
    }

    /// <summary>
    /// §08's 구매 목록, resolved against <see cref="GameConstants"/>.
    /// <para>
    /// The derived totals at the bottom exist so the balance tests and the §16-2
    /// price sweep can ask questions like "what is the cheapest upgrade" without
    /// restating a number that lives in <see cref="GameConstants"/>. Once a price
    /// moves, every answer here moves with it.
    /// </para>
    /// </summary>
    public static class ShopCatalogue
    {
        private static readonly ShopItemDefinition Nothing = new ShopItemDefinition(
            ShopItemId.None, ShopCategory.None, 0, false, false, false, false, false, false, RoleId.None);

        private static readonly ShopItemDefinition[] Rows =
        {
            // 소모품 — bought every trip, so they set the floor of the curve.
            new ShopItemDefinition(
                ShopItemId.Battery, ShopCategory.Consumable, GameConstants.ShopCostBattery,
                true, false, false, false, false, false, RoleId.None),
            new ShopItemDefinition(
                ShopItemId.RepairMaterials, ShopCategory.Consumable, GameConstants.ShopCostRepairMaterials,
                true, false, false, false, false, false, RoleId.None),
            new ShopItemDefinition(
                ShopItemId.FirstAidKit, ShopCategory.Consumable, GameConstants.ShopCostFirstAidKit,
                true, false, false, false, false, false, RoleId.None),

            // 탐험 — how far the team can get.
            new ShopItemDefinition(
                ShopItemId.UpgradedFlashlight, ShopCategory.Exploration, GameConstants.ShopCostUpgradedFlashlight,
                false, true, false, true, false, false, RoleId.None),
            new ShopItemDefinition(
                ShopItemId.Flare, ShopCategory.Exploration, GameConstants.ShopCostFlare,
                true, false, true, true, false, false, RoleId.Engineer),
            new ShopItemDefinition(
                ShopItemId.Chalk, ShopCategory.Exploration, GameConstants.ShopCostChalk,
                false, false, false, true, false, false, RoleId.None),
            new ShopItemDefinition(
                ShopItemId.Rope, ShopCategory.Exploration, GameConstants.ShopCostRope,
                false, true, false, false, true, false, RoleId.None),
            new ShopItemDefinition(
                ShopItemId.Bag, ShopCategory.Exploration, GameConstants.ShopCostBag,
                false, true, false, false, false, false, RoleId.None),

            // 정보 — what the team knows.
            new ShopItemDefinition(
                ShopItemId.Blueprint, ShopCategory.Information, GameConstants.ShopCostBlueprint,
                false, true, false, false, false, false, RoleId.None),
            new ShopItemDefinition(
                ShopItemId.PocketWatch, ShopCategory.Information, GameConstants.ShopCostPocketWatch,
                false, true, false, false, false, false, RoleId.None),
            new ShopItemDefinition(
                ShopItemId.Detector, ShopCategory.Information, GameConstants.ShopCostDetector,
                false, true, true, false, false, false, RoleId.Listener),

            // 위험 — where the monster is, and who can hear it.
            new ShopItemDefinition(
                ShopItemId.Bait, ShopCategory.Danger, GameConstants.ShopCostBait,
                true, false, true, true, false, false, RoleId.Runner),
            new ShopItemDefinition(
                ShopItemId.Suppressor, ShopCategory.Danger, GameConstants.ShopCostSuppressor,
                false, true, false, false, false, true, RoleId.None),
        };

        private static readonly ShopItemId[] AllIds = BuildIds();

        /// <summary>Every item §08 sells, in the document's order.</summary>
        public static IReadOnlyList<ShopItemId> All => AllIds;

        /// <summary>
        /// The row for an item. An unknown id returns a zero-cost, no-effect row
        /// rather than throwing — shop requests arrive over the network, and a
        /// malformed one must fail the purchase, not the match.
        /// </summary>
        public static ShopItemDefinition Of(ShopItemId id)
        {
            for (var i = 0; i < Rows.Length; i++)
            {
                if (Rows[i].Id == id)
                {
                    return Rows[i];
                }
            }

            return Nothing;
        }

        /// <summary>Price of one unit. Zero for an unknown id, which <see cref="Shop"/> rejects separately.</summary>
        public static int CostOf(ShopItemId id) => Of(id).Cost;

        /// <summary>Whether the item exists in §08's list.</summary>
        public static bool Exists(ShopItemId id) => Of(id).Id != ShopItemId.None;

        /// <summary>The cheapest thing on the shelf — the price a wallet must beat to buy anything at all.</summary>
        public static int CheapestCost
        {
            get
            {
                var min = int.MaxValue;
                for (var i = 0; i < Rows.Length; i++)
                {
                    if (Rows[i].Cost < min)
                    {
                        min = Rows[i].Cost;
                    }
                }

                return min == int.MaxValue ? 0 : min;
            }
        }

        /// <summary>
        /// The cheapest 강화 아이템. §08's growth table turns on this number: after
        /// the first return the team must be under it, and after the second or third
        /// it must be over.
        /// </summary>
        public static int CheapestUpgradeCost
        {
            get
            {
                var min = int.MaxValue;
                for (var i = 0; i < Rows.Length; i++)
                {
                    if (Rows[i].IsCapabilityUpgrade && Rows[i].Cost < min)
                    {
                        min = Rows[i].Cost;
                    }
                }

                return min == int.MaxValue ? 0 : min;
            }
        }

        /// <summary>What one of everything would cost. The upper bound of "필요한 건 다 있는데" — no real team needs this much.</summary>
        public static int CostOfOneOfEverything
        {
            get
            {
                var total = 0;
                for (var i = 0; i < Rows.Length; i++)
                {
                    total += Rows[i].Cost;
                }

                return total;
            }
        }

        /// <summary>
        /// The item §11 offers in place of a missing role, or
        /// <see cref="ShopItemId.None"/> when money cannot cover that absence —
        /// which for 관측자 is the point: §11 calls it "유일하게 살 수 없는 정보".
        /// </summary>
        public static ShopItemId SubstituteFor(RoleId missingRole)
        {
            if (missingRole == RoleId.None)
            {
                return ShopItemId.None;
            }

            for (var i = 0; i < Rows.Length; i++)
            {
                if (Rows[i].SubstitutesFor == missingRole)
                {
                    return Rows[i].Id;
                }
            }

            return ShopItemId.None;
        }

        private static ShopItemId[] BuildIds()
        {
            var ids = new ShopItemId[Rows.Length];
            for (var i = 0; i < Rows.Length; i++)
            {
                ids[i] = Rows[i].Id;
            }

            return ids;
        }
    }
}
