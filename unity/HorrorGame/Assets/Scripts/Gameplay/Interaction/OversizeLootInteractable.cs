#nullable enable

using System.Globalization;
using HorrorGame.Core;
using HorrorGame.Core.Economy;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// §08's 대형 초상화 · 궤짝 — the row the section says exists to produce one scene.
    /// <para>
    /// <b>Why the prompt is loud about the second pair of hands.</b> §08's table marks
    /// this row "2인 운반 or 극심한 감속" and calls the corridor moment "최고의 순간:
    /// 두 명이 궤짝을 들고 좁은 통로를 지나는데 괴물이 온다." A player standing alone
    /// in front of it has to be told which half of that "or" they are choosing —
    /// <c>SharedLootCarry</c> will let one person take the whole weight, and then
    /// §08's weight bands take their speed away with no explanation attached. So the
    /// prompt names the two-carrier rule before the key is pressed, and names the
    /// weight that landed on one pair of hands afterwards.
    /// </para>
    /// <para>
    /// Every rule is <see cref="SharedLootCarry"/>'s: how the share splits, what
    /// happens when a partner lets go, and whether a set of hands can take it at all.
    /// This component holds the piece's transform and its refusals.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OversizeLootInteractable : Interactable
    {
        private SharedLootCarry? _carry;

        /// <summary>The core object tracking who has hold of it. Null before <see cref="Spawn"/>.</summary>
        public SharedLootCarry? Carry
        {
            get { return _carry; }
        }

        /// <summary>Puts an oversize piece on the floor, at the size §08 needs it to be.</summary>
        public static OversizeLootInteractable Spawn(LootId loot, Vector3 position, Transform? parent)
        {
            // 1.28 m of 궤짝 or 1.63 m of 초상화, against an assumed 1.6 m corridor. The
            // model is the argument: §08's "두 명이 궤짝을 들고 좁은 통로를 지나는데" only
            // reads as a decision if the piece visibly does not fit through one.
            var prop = CreateProp("OversizeLoot_" + loot, PropModels.ForLoot(loot, position), position);

            if (parent != null)
            {
                prop.transform.SetParent(parent, worldPositionStays: true);
            }

            var interactable = prop.AddComponent<OversizeLootInteractable>();
            interactable._carry = new SharedLootCarry(loot);
            return interactable;
        }

        /// <inheritdoc />
        public override string Title
        {
            get { return _carry != null ? LootText.NameOf(_carry.Loot) : "대형 전리품"; }
        }

        /// <inheritdoc />
        public override string Detail
        {
            get
            {
                var carry = _carry;
                if (carry == null)
                {
                    return string.Empty;
                }

                if (carry.IsCarriedBy(CurrentPockets()))
                {
                    return "§08 2인 운반 — 지금 "
                           + carry.CarrierCount.ToString(CultureInfo.InvariantCulture) + "명이 들고 있다. "
                           + "내 손 무게 " + carry.WeightPerCarrier.ToString(CultureInfo.InvariantCulture)
                           + ".   다시 누르면 내려놓는다.";
                }

                return "§08 2인 운반 or 극심한 감속 — 무게 "
                       + carry.TotalWeight.ToString(CultureInfo.InvariantCulture)
                       + ", 정원 " + GameConstants.SharedCarryMaxCarriers.ToString(CultureInfo.InvariantCulture)
                       + "명 (현재 " + carry.CarrierCount.ToString(CultureInfo.InvariantCulture) + "명).";
            }
        }

        /// <inheritdoc />
        public override string Action
        {
            get
            {
                var carry = _carry;
                return carry != null && carry.IsCarriedBy(CurrentPockets()) ? "내려놓기" : "들기 (2인)";
            }
        }

        /// <inheritdoc />
        protected override bool GlowsWhenTargeted
        {
            get
            {
                var carry = _carry;
                return carry == null || !carry.IsCarriedBy(CurrentPockets());
            }
        }

        /// <inheritdoc />
        public override void OnPressed(PlayerInteractor by)
        {
            var carry = _carry;
            var pockets = by.Pockets;
            if (carry == null || pockets == null)
            {
                return;
            }

            if (carry.IsCarriedBy(pockets))
            {
                Release(by, carry, pockets);
                SetGlow(false);
                return;
            }

            if (carry.IsSold)
            {
                Refusal = "이미 차량에 실었다.";
                return;
            }

            if (carry.CarrierCount >= GameConstants.SharedCarryMaxCarriers)
            {
                Refusal = "§08 " + GameConstants.SharedCarryMaxCarriers.ToString(CultureInfo.InvariantCulture)
                          + "명이 이미 들고 있다.";
                return;
            }

            if (!carry.TryPickUp(pockets))
            {
                // The two ways §08 and §03 refuse, told apart so the fix is obvious:
                // empty your hands, or empty your pockets.
                Refusal = pockets.HandsFree
                    ? "§08 혼자 들기에는 주머니가 찼다 — 남은 용량 "
                      + pockets.FreeCapacity.ToString(CultureInfo.InvariantCulture)
                      + ", 이 전리품 무게 " + carry.TotalWeight.ToString(CultureInfo.InvariantCulture)
                      + ". 팔거나 버리고 오거나, 한 명 더 필요하다."
                    : "§03 양손이 비어 있어야 한다.";
                return;
            }

            var soleCarrier = carry.CarrierCount < GameConstants.SharedCarryMaxCarriers;
            if (soleCarrier)
            {
                // §08's other half of "2인 운반 or 극심한 감속", said out loud. Alone in
                // a solo playtest this is the only branch a player can reach, and it must
                // not read as "the game let me do the two-person thing by myself".
                Refusal = "§08 혼자 들었다 — 이 궤짝은 "
                          + GameConstants.SharedCarryMaxCarriers.ToString(CultureInfo.InvariantCulture)
                          + "인 운반용이다. 무게 "
                          + carry.WeightPerCarrier.ToString(CultureInfo.InvariantCulture)
                          + " 전부가 이 손에 있다.";
            }
            else
            {
                Refusal = string.Empty;
            }

            by.SetOversizeCarry(this, carrying: true);
            AttachTo(by);
            SetGlow(false);
        }

        /// <summary>Lets go without pressing the key — a death, a sale, or the piece being taken away.</summary>
        public void ForceRelease(PlayerInteractor by)
        {
            var carry = _carry;
            var pockets = by.Pockets;
            if (carry == null || pockets == null || !carry.IsCarriedBy(pockets))
            {
                return;
            }

            Release(by, carry, pockets);
        }

        private void Release(PlayerInteractor by, SharedLootCarry carry, Inventory pockets)
        {
            var outcome = carry.Release(pockets);
            by.SetOversizeCarry(this, carrying: false);

            if (outcome == CarryReleaseOutcome.LeftOnTheFloor
                || outcome == CarryReleaseOutcome.DroppedTooHeavyForSoleCarrier)
            {
                Detach(by);
            }

            Refusal = outcome == CarryReleaseOutcome.DroppedTooHeavyForSoleCarrier
                ? "§08 남은 사람이 감당할 수 없어 바닥에 떨어졌다."
                : string.Empty;
        }

        private void AttachTo(PlayerInteractor by)
        {
            var anchor = by.CarryAnchor;
            if (anchor == null)
            {
                return;
            }

            transform.SetParent(anchor, worldPositionStays: false);
            transform.localPosition = Vector3.forward * CarryOffset;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Puts the piece down. §08's "떨어진다", and the only place this component
        /// decides where it ends up.
        /// <para>
        /// It used to keep the carried position and only correct the height, with a
        /// raycast that took the project's default trigger handling. Three things were
        /// wrong with that and all three put the piece somewhere a player could not use
        /// it: the sideways position was 1.05 m along the camera's forward, so a player
        /// facing a wall pushed a 궤짝 into it; the probe hit triggers, so a piece let go
        /// beside another interactable landed on <em>its</em> box and stayed in the air;
        /// and the piece kept the camera's pitch, so it lay over at whatever angle the
        /// player was looking. <see cref="DropPlacement"/> answers all three.
        /// </para>
        /// </summary>
        private void Detach(PlayerInteractor by)
        {
            // Read while it is still parented to the eye — unparenting keeps the world
            // pose, so this is the position the hands were holding it at.
            var held = transform.position;
            transform.SetParent(null, worldPositionStays: true);

            transform.SetPositionAndRotation(
                DropPlacement.Resolve(held, by.Body.position, by.Body),
                DropPlacement.Upright(transform.rotation));
        }

        private Inventory? CurrentPockets()
        {
            var interactor = PlayerInteractor.Active;
            return interactor != null ? interactor.Pockets : null;
        }

        // How far in front of the chest a carried piece is held. §08 charges for it in
        // weight, not in metres; the piece's own size comes from the model.
        private const float CarryOffset = 1.05f;
    }
}
