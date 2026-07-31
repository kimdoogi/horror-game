#nullable enable

using System.Globalization;
using HorrorGame.Core;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Roles;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// §08's 금고 — eight seconds of Engineer work, and the document inside it.
    /// <para>
    /// <b>The gate belongs to the role, not to the key.</b> <see cref="LootSafe.Tick"/>
    /// takes the working player's <see cref="RoleId"/> and answers
    /// <see cref="SafeWorkResult.WrongRole"/> for anyone but §04's 정비공, so the
    /// refusal is the rule speaking rather than a check invented here. §11's 절대 규칙
    /// survives that: the safe is 전리품, §03 makes 전리품 optional, so a party without
    /// an Engineer loses money and not the match — and the prompt says exactly that
    /// rather than reading as a locked door.
    /// </para>
    /// <para>
    /// The work is held down and the progress comes straight from the core object, so
    /// walking away mid-drill abandons it in §04's sense — a preparation role, hands-on
    /// at the site (<see cref="GameConstants.EngineerReachDistance"/>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LootSafeInteractable : Interactable
    {
        private readonly LootSafe _safe = new LootSafe();

        /// <summary>The core object. Exposed so a headless test can assert §04's gate.</summary>
        public LootSafe Safe
        {
            get { return _safe; }
        }

        /// <summary>Puts a safe against a wall.</summary>
        public static LootSafeInteractable Spawn(Vector3 position, Transform? parent)
        {
            var prop = CreateProp(
                "LootSafe",
                PrimitiveType.Cube,
                position + (Vector3.up * (PropSize.y * 0.5f)),
                PropSize,
                PropColour);

            if (parent != null)
            {
                prop.transform.SetParent(parent, worldPositionStays: true);
            }

            return prop.AddComponent<LootSafeInteractable>();
        }

        /// <inheritdoc />
        public override string Title
        {
            get { return _safe.IsEmptied ? "빈 금고" : "금고"; }
        }

        /// <inheritdoc />
        public override string Detail
        {
            get
            {
                if (_safe.IsEmptied)
                {
                    return "이미 비었다.";
                }

                if (_safe.IsOpen)
                {
                    return "§08 " + LootText.NameOf(_safe.Contents)
                           + " — 무게 " + LootCatalogue.WeightOf(_safe.Contents).ToString(CultureInfo.InvariantCulture);
                }

                return "§04 정비공 " + GameConstants.EngineerSafeSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                       + "초 작업 — 누르고 있어야 한다.";
            }
        }

        /// <inheritdoc />
        public override bool NeedsHold
        {
            get { return !_safe.IsOpen; }
        }

        /// <inheritdoc />
        public override float HoldProgress01
        {
            get { return _safe.Progress01; }
        }

        /// <inheritdoc />
        public override void OnPressed(PlayerInteractor by)
        {
            if (!_safe.IsOpen)
            {
                return;
            }

            if (_safe.IsEmptied)
            {
                Refusal = "이미 비었다.";
                return;
            }

            var pockets = by.Pockets;
            if (!_safe.TryTakeContents(pockets))
            {
                Refusal = pockets != null && pockets.CarryingObjective
                    ? "§03 목표물을 든 손으로는 전리품을 집을 수 없다."
                    : "§08 더 들 수 없다 — 문서는 금고에 그대로 있다.";
                return;
            }

            by.Director?.NoteLootTaken();
            Refusal = string.Empty;
        }

        /// <inheritdoc />
        public override void OnHeld(PlayerInteractor by, float deltaSeconds)
        {
            var result = _safe.Tick(deltaSeconds, by.Role);

            switch (result)
            {
                case SafeWorkResult.WrongRole:
                    // §11: the absent role costs the team money, never the match.
                    Refusal = "§08 금고는 정비공만 연다 — 이 판에는 없는 손이다. "
                              + "전리품은 §03에서 선택 사항이니 판은 그대로 진행된다.";
                    break;

                case SafeWorkResult.JustOpened:
                    Refusal = string.Empty;
                    break;

                default:
                    Refusal = string.Empty;
                    break;
            }
        }

        // Rig geometry: how big a wall safe looks.
        private static readonly Vector3 PropSize = new Vector3(0.7f, 0.7f, 0.5f);
        private static readonly Color PropColour = new Color(0.30f, 0.32f, 0.36f);
    }
}
