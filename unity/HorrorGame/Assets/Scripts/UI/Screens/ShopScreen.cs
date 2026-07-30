#nullable enable

using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Roles;
using HorrorGame.UI.Readouts;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// The shop at the surface vehicle. §08.
    /// <para>
    /// <b>One wallet, shown once, with no name on it.</b> §08 is explicit that the
    /// shared purse is the mechanism and not a convenience: <em>"공용 지갑이 핵심.
    /// 크레딧은 팀 공용이다. 그래서 매번 협상이 발생한다. … 돈이 개인 것이면 협동
    /// 게임이 아니게 된다."</em> So there is exactly one balance on this screen, it is
    /// labelled 팀 크레딧, and no element anywhere is keyed by player. A "your share"
    /// column would end the argument §08 exists to cause.
    /// </para>
    /// <para>
    /// <b>Every row carries its 대가.</b> §08 opens the list with "전부 §10 딜레마
    /// 원리를 따른다 — 얻는 게 있으면 대가가 있다", and the flagship row is the 강화
    /// 손전등, whose drawback <em>is</em> its benefit. Rendering the 효과 without the
    /// 대가 would turn a dilemma map into a shopping list, so both columns are always
    /// drawn and the 대가 is the one in colour.
    /// </para>
    /// <para>
    /// <b>The clock does not stop.</b> §07 charges roughly thirty seconds for
    /// "상점에서 고민" and §10 lists "나가서 쉰다 · 구매한다 / 시간이 흐른다" as a
    /// dilemma in its own right, so this screen never pauses anything and does not
    /// darken the world behind it — the team is standing in the open with the night
    /// advancing.
    /// </para>
    /// <para>
    /// Nothing is bought here. The screen raises an <see cref="IShopRequests"/> and
    /// redraws from whatever the host then says is true (§13), which is also what a
    /// player sees when a teammate spent the credits first.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopScreen : UiScreen
    {
        private readonly ShopBoard _board = new ShopBoard();

        private Shop? _shop;
        private IShopRequests? _requests;
        private IReadOnlyList<RoleId>? _roster;
        private RoleId _missingRole = RoleId.None;

        private Text? _creditsText;
        private Text? _ledgerText;
        private Text? _gapText;
        private RowWidgets[]? _rows;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderPanel; }
        }

        /// <summary>True — this is the one screen the player operates with a mouse.</summary>
        protected override bool Interactive
        {
            get { return true; }
        }

        /// <summary>The board currently drawn. Exposed for tests and for the Net layer's redraw hook.</summary>
        public ShopBoard Board
        {
            get { return _board; }
        }

        /// <summary>
        /// Opens the shop over a team's stock and wallet.
        /// </summary>
        /// <param name="shop">
        /// The team's shop. A client may hold a mirrored copy: unlike the clue tables
        /// and the objective's location (ARCHITECTURE §4), prices and stock are not
        /// secret — they are merely host-<em>authoritative</em>, which is what
        /// <paramref name="requests"/> is for.
        /// </param>
        /// <param name="requests">Where purchase and sale requests go.</param>
        /// <param name="roster">The four roles in play, so §08's 소음기 can be flagged against a 청음사. Null flags nothing.</param>
        /// <param name="missingRole">The absent role, so §11's stand-in can be picked out.</param>
        public void Open(Shop shop, IShopRequests requests, IReadOnlyList<RoleId>? roster, RoleId missingRole)
        {
            _shop = shop;
            _requests = requests;
            _roster = roster;
            _missingRole = missingRole;
            SetVisible(true);
            Refresh();
        }

        /// <summary>Closes the shop and drops its references — the team is going back down.</summary>
        public void Close()
        {
            SetVisible(false);
            _shop = null;
            _requests = null;
        }

        /// <summary>Redraws from the shop. Call after the host confirms anything.</summary>
        public void Refresh()
        {
            EnsureBuilt();
            _board.Refresh(_shop, _roster, _missingRole);
            Draw();
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            var panel = UiFactory.CreateImage("Panel", root, UiStyle.Panel);
            var panelRect = UiFactory.Place(
                (RectTransform)panel.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(1180f, 960f));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Title", panelRect, Font, "차량 — 보급소", UiStyle.TextSizeTitle, UiStyle.Ink, TextAnchor.UpperLeft).transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(600f, 44f));

            // The shared wallet, drawn once and drawn large. §08: there is no second
            // number on this screen, and no name attached to this one.
            _creditsText = UiFactory.CreateText(
                "Credits", panelRect, Font, string.Empty, UiStyle.TextSizeTitle, UiStyle.Trade, TextAnchor.UpperRight);
            UiFactory.Place((RectTransform)_creditsText.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(520f, 44f));

            _ledgerText = UiFactory.CreateText(
                "Ledger", panelRect, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.UpperRight);
            UiFactory.Place((RectTransform)_ledgerText.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -70f), new Vector2(520f, 22f));

            _gapText = UiFactory.CreateText(
                "Gap", panelRect, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.UpperLeft);
            UiFactory.Place((RectTransform)_gapText.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -70f), new Vector2(640f, 22f));

            var sell = UiFactory.CreateButton("Sell", panelRect, UiStyle.Row, OnSellClicked);
            UiFactory.Place((RectTransform)sell.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 24f), new Vector2(320f, 44f));
            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "SellLabel", sell.transform, Font, "전리품 싣기 → 크레딧", UiStyle.TextSize, UiStyle.Ink, TextAnchor.MiddleCenter).transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 30f));

            BuildRows(panelRect);
        }

        private void BuildRows(RectTransform panel)
        {
            var ids = ShopCatalogue.All;
            _rows = new RowWidgets[ids.Count];

            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var y = -110f - (i * (UiStyle.RowHeight + UiStyle.RowGap));

                var button = UiFactory.CreateButton("Row_" + id, panel, UiStyle.Row, delegate { OnBuyClicked(id); });
                var rect = UiFactory.Place((RectTransform)button.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, y), new Vector2(1124f, UiStyle.RowHeight));

                var name = RowText(rect, "Name", TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(14f, -6f), 300f, UiStyle.TextSize, UiStyle.Ink);
                var category = RowText(rect, "Category", TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(14f, 6f), 300f, UiStyle.TextSizeSmall, UiStyle.InkFaint);
                var effect = RowText(rect, "Effect", TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(330f, -6f), 520f, UiStyle.TextSize, UiStyle.Ink);
                var price = RowText(rect, "Price", TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(330f, 6f), 520f, UiStyle.TextSizeSmall, UiStyle.Trade);
                var cost = RowText(rect, "Cost", TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-96f, -6f), 200f, UiStyle.TextSize, UiStyle.Ink);
                var stock = RowText(rect, "Stock", TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-14f, 6f), 200f, UiStyle.TextSizeSmall, UiStyle.InkFaint);

                _rows[i] = new RowWidgets(button, button.targetGraphic as Image, name, category, effect, price, cost, stock);
            }
        }

        private void Draw()
        {
            if (_creditsText == null || _ledgerText == null || _gapText == null || _rows == null)
            {
                return;
            }

            _creditsText.text = "팀 크레딧 " + _board.TeamCredits.ToString(CultureInfo.InvariantCulture);
            _creditsText.color = _board.CanBuyAnything ? UiStyle.Trade : UiStyle.Spent;

            _ledgerText.text = "번 것 " + _board.TotalEarned.ToString(CultureInfo.InvariantCulture)
                + " · 쓴 것 " + _board.TotalSpent.ToString(CultureInfo.InvariantCulture);

            _gapText.text = _board.MissingRole == RoleId.None
                ? string.Empty
                : "이번 판에 없는 직업: " + UiStrings.Role(_board.MissingRole);

            var rows = _board.Rows;
            for (var i = 0; i < _rows.Length && i < rows.Count; i++)
            {
                DrawRow(_rows[i], rows[i]);
            }
        }

        private void DrawRow(RowWidgets widgets, ShopRow row)
        {
            widgets.Name.text = row.Name;
            widgets.Category.text = UiStrings.Category(row.Category);
            widgets.Effect.text = row.Effect;

            // §08's 대가 column. Where the document writes "—" the line stays empty
            // rather than inventing a drawback; the 배터리 genuinely has none, which is
            // why §03 could make the flashlight universal issue.
            widgets.Price.text = row.Price;

            widgets.Cost.text = row.Cost.ToString(CultureInfo.InvariantCulture);
            widgets.Cost.color = row.Affordable ? UiStyle.Ink : UiStyle.Spent;

            widgets.Stock.text = row.Stock > 0
                ? "보유 " + row.Stock.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            widgets.Button.interactable = row.Affordable;

            if (widgets.Background != null)
            {
                if (row.ConflictsWithRoster)
                {
                    // §08: "청음사가 있는 팀은 사면 안 된다." Still buyable — §04 says the
                    // team's own accidents are content — but never quietly.
                    widgets.Background.color = UiStyle.RowConflict;
                }
                else if (row.CoversMissingRole)
                {
                    // §11's 돈으로 메우기, pointed at the hole this particular match has.
                    widgets.Background.color = UiStyle.RowSubstitute;
                }
                else
                {
                    widgets.Background.color = row.Affordable ? UiStyle.Row : UiStyle.RowDisabled;
                }
            }

            if (row.ConflictsWithRoster)
            {
                widgets.Price.text = row.Price + "   ← 이 팀에는 청음사가 있다";
                widgets.Price.color = UiStyle.Spent;
            }
            else if (row.CoversMissingRole)
            {
                widgets.Price.color = UiStyle.Trade;
                widgets.Name.text = row.Name + "  ※";
            }
            else
            {
                widgets.Price.color = UiStyle.Trade;
            }
        }

        private void OnBuyClicked(ShopItemId item)
        {
            if (_requests == null)
            {
                return;
            }

            _requests.RequestPurchase(item, 1);
            Refresh();
        }

        private void OnSellClicked()
        {
            if (_requests == null)
            {
                return;
            }

            _requests.RequestSellCarriedLoot();
            Refresh();
        }

        private Text RowText(
            RectTransform parent,
            string name,
            TextAnchor alignment,
            Vector2 anchor,
            Vector2 position,
            float width,
            int size,
            Color color)
        {
            var text = UiFactory.CreateText(name, parent, Font, string.Empty, size, color, alignment);
            UiFactory.Place((RectTransform)text.transform, anchor, anchor, position, new Vector2(width, size + 8f));
            return text;
        }

        /// <summary>The widgets of one drawn row, kept so a redraw writes rather than rebuilds.</summary>
        private sealed class RowWidgets
        {
            public RowWidgets(
                Button button, Image? background, Text name, Text category, Text effect, Text price, Text cost, Text stock)
            {
                Button = button;
                Background = background;
                Name = name;
                Category = category;
                Effect = effect;
                Price = price;
                Cost = cost;
                Stock = stock;
            }

            public Button Button { get; }

            public Image? Background { get; }

            public Text Name { get; }

            public Text Category { get; }

            public Text Effect { get; }

            /// <summary>§08's 대가 column — the field this screen exists to keep on screen.</summary>
            public Text Price { get; }

            public Text Cost { get; }

            public Text Stock { get; }
        }
    }
}
