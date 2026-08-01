#nullable enable

using System.Globalization;
using HorrorGame.Core.Economy;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// §08's table, as words. The document names the four rows; nothing here invents a
    /// fifth, and every number quoted comes out of <c>LootCatalogue</c>.
    /// </summary>
    public static class LootText
    {
        /// <summary>The row's name, as §08 writes it.</summary>
        public static string NameOf(LootId loot)
        {
            switch (loot)
            {
                case LootId.Trinket: return "은수저 · 잡동사니";
                case LootId.Timepiece: return "회중시계 · 반지";
                case LootId.SafeDocument: return "금고 속 문서";
                case LootId.LargePiece: return "대형 초상화 · 궤짝";
                default: return "전리품";
            }
        }

        /// <summary>
        /// The §10 trade this piece asks for, said before it is taken: what it weighs,
        /// and what taking it costs in speed. §08 — "욕심이 곧 속도 저하이고, 속도
        /// 저하가 곧 죽음이다."
        /// </summary>
        public static string CostOf(LootId loot, Inventory? pockets)
        {
            var weight = LootCatalogue.WeightOf(loot);
            var line = "무게 " + weight.ToString(CultureInfo.InvariantCulture);

            if (pockets == null)
            {
                return line;
            }

            var after = pockets.SpeedMultiplierAfterAdding(loot);
            if (after < pockets.SpeedMultiplier)
            {
                var penalty = Mathf.RoundToInt((1f - after) * 100f);
                line += "   집으면 이동 " + penalty.ToString(CultureInfo.InvariantCulture) + "% 감소";
            }

            return line;
        }
    }

    /// <summary>
    /// One pocketable piece of §08's 전리품 lying on the floor.
    /// <para>
    /// The rule is <see cref="Inventory.TryAdd"/> and this component adds none of its
    /// own. Its whole job is that a refusal is spoken: §08's capacity and §03's "전리품
    /// 동시 소지 불가" are both enforced inside the inventory, and both of them look
    /// exactly like a broken key from outside.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LootPropInteractable : Interactable
    {
        private LootId _loot;

        /// <summary>Which row of §08's table this is.</summary>
        public LootId Loot
        {
            get { return _loot; }
        }

        /// <summary>Puts a piece on the floor, in the shape §08 gives its row.</summary>
        public static LootPropInteractable Spawn(LootId loot, Vector3 position, Transform? parent)
        {
            var prop = CreateProp(
                "Loot_" + loot, PropModels.ForLoot(loot, position), position, PropModels.YawFor(position));

            if (parent != null)
            {
                prop.transform.SetParent(parent, worldPositionStays: true);
            }

            var interactable = prop.AddComponent<LootPropInteractable>();
            interactable._loot = loot;
            return interactable;
        }

        /// <inheritdoc />
        public override string Title
        {
            get { return LootText.NameOf(_loot); }
        }

        /// <inheritdoc />
        public override string Detail
        {
            get
            {
                var interactor = PlayerInteractor.Active;
                return LootText.CostOf(_loot, interactor != null ? interactor.Pockets : null);
            }
        }

        /// <inheritdoc />
        public override string Action
        {
            get { return "집기"; }
        }

        /// <inheritdoc />
        public override void OnPressed(PlayerInteractor by)
        {
            var pockets = by.Pockets;
            if (pockets == null)
            {
                Refusal = "이 플레이어에게 소지품이 없다.";
                return;
            }

            if (pockets.CarryingObjective)
            {
                // §03: "전리품 동시 소지 불가 — 전리품은 그 전에 다 챙겨야 한다."
                Refusal = "§03 목표물을 든 손으로는 전리품을 집을 수 없다.";
                return;
            }

            if (!pockets.TryAdd(_loot))
            {
                Refusal = "§08 더 들 수 없다 — 남은 용량 "
                          + pockets.FreeCapacity.ToString(CultureInfo.InvariantCulture)
                          + ", 이 전리품 무게 "
                          + LootCatalogue.WeightOf(_loot).ToString(CultureInfo.InvariantCulture) + ".";
                return;
            }

            by.Director?.NoteLootTaken();
            Despawn(gameObject);
        }
    }
}
