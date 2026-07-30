using System.Collections.Generic;
using HorrorGame.Core.Roles;

namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// One row of §08's 전리품 table.
    /// <para>
    /// The row is data rather than four subclasses because every consumer wants
    /// the same three numbers — weight, value, and whether a role stands between
    /// the player and the piece — and because the simulator sweeps those numbers
    /// (§16-2). Behaviour that differs per piece lives in the systems that own it:
    /// the safe in <see cref="LootSafe"/>, the two-person haul in
    /// <see cref="SharedLootCarry"/>.
    /// </para>
    /// </summary>
    public readonly struct LootDefinition
    {
        /// <summary>Creates a row. Only <see cref="LootCatalogue"/> should need this.</summary>
        public LootDefinition(
            LootId id,
            int weight,
            int value,
            bool isConspicuous,
            RoleId gatedBy,
            bool allowsSharedCarry)
        {
            Id = id;
            Weight = weight;
            Value = value;
            IsConspicuous = isConspicuous;
            GatedBy = gatedBy;
            AllowsSharedCarry = allowsSharedCarry;
        }

        /// <summary>Which piece this row describes.</summary>
        public LootId Id { get; }

        /// <summary>Weight units. §08's 무게 column — this is what feeds the speed bands, so it is the piece's real price.</summary>
        public int Weight { get; }

        /// <summary>Credits paid when the piece reaches the vehicle. §08: "전리품을 차량에 실으면 → 가치만큼 크레딧" — no haggling, no markup.</summary>
        public int Value { get; }

        /// <summary>
        /// §08 flags 은수저·잡동사니 as "눈에 잘 보임 (유혹)". Placement uses this to
        /// put the worst-value loot where players will find it first, which is what
        /// makes the weight decision happen before they know better.
        /// </summary>
        public bool IsConspicuous { get; }

        /// <summary>
        /// The role that must act before the piece can be taken, or
        /// <see cref="RoleId.None"/>. §08 puts 금고 속 문서 behind the Engineer;
        /// §11's absolute rule means this must never gate a <em>required</em> path,
        /// which is why only optional loot is gated at all.
        /// </summary>
        public RoleId GatedBy { get; }

        /// <summary>
        /// True when two players may split the piece between them. §08 grants this
        /// to 대형 초상화·궤짝 only ("2인 운반"); a weight-1 trinket has no second
        /// handle, and letting anything be co-carried would erase the scene §08
        /// built the row for.
        /// </summary>
        public bool AllowsSharedCarry { get; }

        /// <summary>
        /// Credits per weight unit — the number §08 means by "효율". Kept as a
        /// derived property so the catalogue can be re-tuned without anyone
        /// re-deriving which piece is the efficient one by hand.
        /// </summary>
        public float ValuePerWeight => Weight > 0 ? Value / (float)Weight : 0f;
    }

    /// <summary>
    /// §08's 전리품 table, resolved.
    /// <para>
    /// Every number comes from <see cref="GameConstants"/>, so the balance sweep in
    /// §16-2 changes one file and this catalogue follows. Ordering matches the
    /// document's table top to bottom, which is also cheapest to dearest.
    /// </para>
    /// </summary>
    public static class LootCatalogue
    {
        private static readonly LootId[] AllIds =
        {
            LootId.Trinket,
            LootId.Timepiece,
            LootId.SafeDocument,
            LootId.LargePiece,
        };

        private static readonly LootDefinition Nothing =
            new LootDefinition(LootId.None, 0, 0, false, RoleId.None, false);

        private static readonly LootDefinition[] Rows =
        {
            new LootDefinition(
                LootId.Trinket,
                GameConstants.LootWeightTrinket,
                GameConstants.LootValueTrinket,
                true,
                RoleId.None,
                false),
            new LootDefinition(
                LootId.Timepiece,
                GameConstants.LootWeightTimepiece,
                GameConstants.LootValueTimepiece,
                false,
                RoleId.None,
                false),
            new LootDefinition(
                LootId.SafeDocument,
                GameConstants.LootWeightSafeDocument,
                GameConstants.LootValueSafeDocument,
                false,
                RoleId.Engineer,
                false),
            new LootDefinition(
                LootId.LargePiece,
                GameConstants.LootWeightLargePiece,
                GameConstants.LootValueLargePiece,
                false,
                RoleId.None,
                true),
        };

        /// <summary>Every real piece, in §08's table order. Excludes <see cref="LootId.None"/>.</summary>
        public static IReadOnlyList<LootId> All => AllIds;

        /// <summary>
        /// The row for a piece. An unknown id returns a zero-weight, zero-value row
        /// rather than throwing: loot arrives from map data and from the network, and
        /// a corrupt id must not be able to abort a match.
        /// </summary>
        public static LootDefinition Of(LootId id)
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

        /// <summary>Weight units of one piece. Zero for an unknown id.</summary>
        public static int WeightOf(LootId id) => Of(id).Weight;

        /// <summary>Credits one piece is worth at the vehicle. Zero for an unknown id.</summary>
        public static int ValueOf(LootId id) => Of(id).Value;

        /// <summary>Credits a whole bundle is worth. An empty or null bundle is worth nothing, not an error — a player can walk out with empty hands.</summary>
        public static int ValueOf(IReadOnlyList<LootId>? pieces)
        {
            if (pieces == null)
            {
                return 0;
            }

            var total = 0;
            for (var i = 0; i < pieces.Count; i++)
            {
                total += ValueOf(pieces[i]);
            }

            return total;
        }

        /// <summary>
        /// The piece with the best credits-per-weight, computed rather than named.
        /// §08 asserts 회중시계·반지 is "효율 최고"; a retune that quietly hands the
        /// crown to another row is a balance change, and
        /// <c>EconomyTests</c> fails on it.
        /// </summary>
        public static LootId MostEfficient
        {
            get
            {
                var best = LootId.None;
                var bestRate = float.NegativeInfinity;
                for (var i = 0; i < Rows.Length; i++)
                {
                    if (Rows[i].ValuePerWeight > bestRate)
                    {
                        bestRate = Rows[i].ValuePerWeight;
                        best = Rows[i].Id;
                    }
                }

                return best;
            }
        }
    }
}
