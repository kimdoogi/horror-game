#nullable enable

using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Threat;
using HorrorGame.UI.Readouts;
using UnityEngine;
using UnityEngine.InputSystem;
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
    /// <b>Every row carries its 대가, on the same line and at the same size.</b> §08
    /// opens the list with "전부 §10 딜레마 원리를 따른다 — 얻는 게 있으면 대가가
    /// 있다", and the flagship row is the 강화 손전등, which the section calls "이
    /// 목록의 대표작" precisely because its drawback <em>is</em> its benefit. Drawing
    /// the 효과 large and the 대가 small underneath turns a dilemma map back into a
    /// shopping list with footnotes, so the two sit side by side across a rule, the
    /// 대가 in colour, and a row §08 marks "—" says so rather than going blank.
    /// </para>
    /// <para>
    /// <b>§11's hole is the headline.</b> "매판 하나가 빠지고, 그게 그 판의 성격이
    /// 된다", and §08's economy is what stops that absence making a role compulsory.
    /// So the missing role, its 난제 and its 돈으로 메우기 answer lead the screen, and
    /// the row that answers it is marked in the list. Two of §11's five answers are
    /// not purchases — the 관측자 has none by design and the 섬광탄 is missing from
    /// §08's list — and both are said out loud, because a screen that shows nothing
    /// for them is indistinguishable from a screen that has not noticed.
    /// </para>
    /// <para>
    /// <b>The clock does not stop, and now it shows.</b> §07 charges roughly thirty
    /// seconds for "상점에서 고민" and §10 lists "나가서 쉰다 · 구매한다 / 시간이
    /// 흐른다" as a dilemma in its own right. This screen pauses nothing, and it
    /// carries §07's night itself: a full-screen panel drawn over the HUD hides the
    /// HUD's clock, so a shop that did not redraw it would have quietly removed the
    /// one instrument that makes shopping cost something. The hour is legible here
    /// only because §07 says it is — "시각은 지상에서만 알 수 있다", and the vehicle
    /// is the surface — and the gate that decides so lives in Core.
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
        private MatchClock? _clock;
        private bool _clockGivenByCaller;

        private Text? _creditsText;
        private Text? _ledgerText;
        private Text? _nightText;
        private Text? _nightWarningText;
        private Text? _visitText;
        private UiBar? _nightBar;
        private Text? _gapText;
        private Text? _messageText;
        private RowWidgets[]? _rows;

        private float _visitSeconds;
        private int _visitSecondsDrawn = -1;
        private int _selected;
        private int _columnBreak;
        private ShopItemId _awaitingConfirm = ShopItemId.None;

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

        /// <summary>Which row the keyboard cursor is on. §08's list is operable without a mouse.</summary>
        public int SelectedIndex
        {
            get { return _selected; }
        }

        /// <summary>The last thing this screen said about a purchase or a sale — a refusal, a confirmation, or an empty string.</summary>
        public string Message
        {
            get { return _messageText != null ? _messageText.text : string.Empty; }
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
            _visitSeconds = 0f;
            _visitSecondsDrawn = -1;
            _awaitingConfirm = ShopItemId.None;
            _selected = 0;
            ResolveClock();
            SetVisible(true);
            SetMessage(string.Empty, UiStyle.InkQuiet);
            Refresh();
        }

        /// <summary>Closes the shop and drops its references — the team is going back down.</summary>
        public void Close()
        {
            SetVisible(false);
            _shop = null;
            _requests = null;
        }

        /// <summary>
        /// Hands the screen §07's clock explicitly.
        /// <para>
        /// The host's own screen picks the clock up from its sibling HUD (see
        /// <see cref="ResolveClock"/>); a networked client is handed one instead,
        /// because the object it should read is whatever the Net layer is keeping in
        /// step with the host rather than whatever happens to be in the scene.
        /// </para>
        /// </summary>
        public void BindClock(MatchClock? clock)
        {
            _clock = clock;
            _clockGivenByCaller = true;
        }

        /// <summary>Redraws from the shop. Call after the host confirms anything.</summary>
        public void Refresh()
        {
            EnsureBuilt();
            _board.Refresh(_shop, _roster, _missingRole, _clock, _visitSeconds);
            Draw();
        }

        /// <summary>
        /// Keeps §07 moving while nobody presses anything.
        /// <para>
        /// The whole point of the element is that it changes on its own: a static
        /// "심야" would read as a label on the shop rather than as a cost being paid
        /// for standing in it. Only the night strip is rewritten per frame — the
        /// thirteen rows change when the wallet does, and rewriting a <c>Text</c>
        /// dirties the canvas whether or not the string differs.
        /// </para>
        /// </summary>
        private void Update()
        {
            if (!IsVisible)
            {
                return;
            }

            // Scaled, not unscaled. §07's clock is stepped by the match and the pause
            // menu stops the match, so a visit counter on unscaled time would run ahead
            // of the night it is reporting the cost of.
            TickVisit(Time.deltaTime);
            ReadKeys();
        }

        /// <summary>
        /// Advances §07's half of the screen by an explicit amount and redraws it.
        /// <para>
        /// Delta-taking rather than frame-driven, for the same reason
        /// <c>MatchDirector.StepMatch</c> and <c>ClueReader.Tick</c> are: the cost of
        /// standing at the vehicle is a design claim, and a claim that can only be
        /// observed by watching a running game is a claim nothing can check
        /// (ARCHITECTURE §3).
        /// </para>
        /// </summary>
        /// <param name="deltaSeconds">Seconds elapsed. Non-positive values are ignored.</param>
        public void TickVisit(float deltaSeconds)
        {
            if (deltaSeconds > 0f && !float.IsNaN(deltaSeconds) && !float.IsInfinity(deltaSeconds))
            {
                _visitSeconds += deltaSeconds;
            }

            _board.TickNight(_clock, _visitSeconds);
            DrawNight();
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
                new Vector2(UiStyle.ShopPanelWidth, UiStyle.ShopPanelHeight));

            BuildHeader(panelRect);
            BuildRows(panelRect);
            BuildFooter(panelRect);
        }

        // ------------------------------------------------------------------
        // The header: one wallet, §07's night, §11's hole.
        // ------------------------------------------------------------------

        private void BuildHeader(RectTransform panel)
        {
            var pad = UiStyle.ShopPanelPadding;
            var inner = UiStyle.ShopPanelWidth - (pad * 2f);

            HeaderText(panel, "Title", "차량 — 보급소", TextAnchor.UpperLeft, 0f, -20f, 620f, UiStyle.TextSizeTitle, UiStyle.InkStrong);

            // The shared wallet, drawn once and drawn large. §08: there is no second
            // number on this screen, and no name attached to this one.
            _creditsText = HeaderText(
                panel, "Credits", string.Empty, TextAnchor.UpperRight, 0f, -16f, 620f, UiStyle.TextSizeTitle, UiStyle.Trade, right: true);
            _ledgerText = HeaderText(
                panel, "Ledger", string.Empty, TextAnchor.UpperRight, 0f, -62f, 560f, UiStyle.TextSizeSmall, UiStyle.InkQuiet, right: true);

            // §07 · §10 — the cost of standing here, which is the only cost this screen
            // charges that is not a credit. The visit counter sits on the warning's line
            // rather than the ledger's: both are §07, and the ledger is a §08 number.
            _nightText = HeaderText(
                panel, "Night", string.Empty, TextAnchor.UpperLeft, 0f, -68f, 860f, UiStyle.TextSize + 2, UiStyle.InkStrong);
            _nightWarningText = HeaderText(
                panel, "NightWarning", string.Empty, TextAnchor.UpperLeft, 0f, -96f, 860f, UiStyle.TextSize, UiStyle.Trade);
            _visitText = HeaderText(
                panel, "Visit", string.Empty, TextAnchor.UpperRight, 0f, -96f, 420f, UiStyle.TextSize + 2, UiStyle.Trade, right: true);

            var bar = UiFactory.CreateBar("NightBar", panel, inner, UiStyle.ShopNightBarHeight);
            UiFactory.Place(bar.Root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(pad, -126f), new Vector2(inner, UiStyle.ShopNightBarHeight));
            bar.SetBedColor(UiStyle.RowDisabled);
            _nightBar = bar;

            // Clear of the bar above it: at 19 pt this line is 27 px tall, and the header
            // has to stack §07's countdown, its 추가, the visit counter, the bar and §11's
            // sentence without any two of them sharing a pixel.
            _gapText = HeaderText(
                panel, "Gap", string.Empty, TextAnchor.UpperLeft, 0f, -138f, inner, UiStyle.TextSize + 1, UiStyle.Trade);
        }

        private Text HeaderText(
            RectTransform panel,
            string name,
            string value,
            TextAnchor alignment,
            float x,
            float y,
            float width,
            int size,
            Color color,
            bool right = false)
        {
            var text = UiFactory.CreateText(name, panel, Font, value, size, color, alignment);
            var anchor = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            var offset = right
                ? new Vector2(-UiStyle.ShopPanelPadding + x, y)
                : new Vector2(UiStyle.ShopPanelPadding + x, y);
            UiFactory.Place((RectTransform)text.transform, anchor, anchor, offset, new Vector2(width, size + 8f));
            return text;
        }

        // ------------------------------------------------------------------
        // The list: §08's order, §08's 분류, two columns.
        // ------------------------------------------------------------------

        /// <summary>
        /// Lays §08's list out in two columns, split on one of its own 분류 boundaries.
        /// <para>
        /// Thirteen rows tall enough to carry a 효과 <em>and</em> a 대가 do not fit in
        /// one column of a 1080-line display, and shrinking the 대가 to make them fit
        /// is what produced the screen this replaces. The split point is computed from
        /// the categories rather than written down, so adding an item to §08's list
        /// rebalances the columns instead of overflowing one of them.
        /// </para>
        /// </summary>
        private void BuildRows(RectTransform panel)
        {
            var ids = ShopCatalogue.All;
            _rows = new RowWidgets[ids.Count];
            _columnBreak = ColumnBreak(ids);

            var columnWidth = (UiStyle.ShopPanelWidth - (UiStyle.ShopPanelPadding * 2f) - UiStyle.ShopColumnGap) * 0.5f;
            var y = -UiStyle.ShopRowsTop;
            var x = UiStyle.ShopPanelPadding;
            var category = ShopCategory.None;

            for (var i = 0; i < ids.Count; i++)
            {
                if (i == _columnBreak)
                {
                    x = UiStyle.ShopPanelPadding + columnWidth + UiStyle.ShopColumnGap;
                    y = -UiStyle.ShopRowsTop;
                    category = ShopCategory.None;
                }

                var id = ids[i];
                var definition = ShopCatalogue.Of(id);

                if (definition.Category != category)
                {
                    category = definition.Category;

                    // CreateSection pins itself to the panel's left edge; the second
                    // column has to be moved over, and repositioning the returned root
                    // keeps one implementation of "a heading with a rule under it".
                    var section = UiControls.CreateSection(panel, Font, UiStrings.Category(category), y, columnWidth);
                    UiFactory.Place(section, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(columnWidth, 34f));

                    // The heading is built at the HUD's ink weight, which is a whole
                    // stop too dim to act as a landmark on an opaque panel.
                    var heading = section.GetComponentInChildren<Text>(true);
                    if (heading != null)
                    {
                        heading.color = UiStyle.InkStrong;
                    }

                    y -= UiStyle.ShopGroupHeight;
                }

                _rows[i] = BuildRow(panel, id, i, x, y, columnWidth);
                y -= UiStyle.ShopRowHeight + UiStyle.ShopRowGap;
            }
        }

        /// <summary>The index §08's list is cut at: the 분류 boundary nearest the middle, so neither column runs off the panel.</summary>
        private static int ColumnBreak(IReadOnlyList<ShopItemId> ids)
        {
            var half = ids.Count / 2;
            var best = ids.Count;
            var bestDistance = int.MaxValue;
            var category = ShopCategory.None;

            for (var i = 0; i < ids.Count; i++)
            {
                var next = ShopCatalogue.Of(ids[i]).Category;
                if (i > 0 && next != category)
                {
                    var distance = i > half ? i - half : half - i;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }

                category = next;
            }

            return best;
        }

        private RowWidgets BuildRow(RectTransform panel, ShopItemId id, int index, float x, float y, float width)
        {
            var button = UiFactory.CreateButton("Row_" + id, panel, UiStyle.Row, delegate { OnRowClicked(index); });

            // uGUI must not run a second cursor over these. This screen draws its own
            // selection so the keyboard and the mouse agree about which row is live;
            // leaving navigation on would let the EventSystem highlight a different one.
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            var rect = UiFactory.Place((RectTransform)button.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, UiStyle.ShopRowHeight));

            var cursor = UiFactory.CreateImage("Cursor", rect, UiStyle.Trade);
            UiFactory.Place((RectTransform)cursor.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(UiStyle.ShopCursorWidth, UiStyle.ShopRowHeight));

            var name = RowText(rect, "Name", TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(14f, -4f), 250f, UiStyle.TextSize + 2, UiStyle.InkStrong);
            var cost = RowText(rect, "Cost", TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-14f, -4f), 130f, UiStyle.TextSizeTitle - 10, UiStyle.InkStrong);

            var effect = RowText(rect, "Effect", TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(14f, -28f), 290f, UiStyle.TextSize - 1, UiStyle.InkStrong);

            // The rule is the whole argument of §08's table in one pixel: what you get,
            // and immediately beside it what it costs you that is not money.
            var rule = UiFactory.CreateImage("Rule", rect, UiStyle.InkQuiet);
            UiFactory.Place((RectTransform)rule.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(312f, -28f), new Vector2(1f, 20f));

            var price = RowText(rect, "Price", TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(326f, -28f), width - 340f, UiStyle.TextSize - 1, UiStyle.Trade);

            // The third line is set at the same size as the second, not at a caption
            // size. It carries the shortfall — §08's "how much more loot" — and §11's
            // mark, and measured against its own row a 15 pt red glyph never reached
            // full coverage: 2.7:1, which is a number nobody reads across a room.
            var note = RowText(rect, "Note", TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(14f, -51f), width - 200f, UiStyle.TextSize - 1, UiStyle.InkQuiet);
            var stock = RowText(rect, "Stock", TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-14f, -51f), 180f, UiStyle.TextSize - 1, UiStyle.InkQuiet);

            return new RowWidgets(button, button.targetGraphic as Image, cursor, name, effect, rule, price, cost, note, stock);
        }

        // ------------------------------------------------------------------
        // The footer: sell, say what happened, say which keys.
        // ------------------------------------------------------------------

        private void BuildFooter(RectTransform panel)
        {
            var pad = UiStyle.ShopPanelPadding;

            var sell = UiFactory.CreateButton("Sell", panel, UiStyle.Row, OnSellClicked);
            var sellNavigation = sell.navigation;
            sellNavigation.mode = Navigation.Mode.None;
            sell.navigation = sellNavigation;
            UiFactory.Place((RectTransform)sell.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(pad, 26f), new Vector2(330f, 44f));
            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "SellLabel", sell.transform, Font, "전리품 싣기 → 크레딧", UiStyle.TextSize, UiStyle.InkStrong, TextAnchor.MiddleCenter).transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(310f, 30f));

            _messageText = UiFactory.CreateText("Message", panel, Font, string.Empty, UiStyle.TextSize + 2, UiStyle.InkQuiet, TextAnchor.MiddleLeft);
            UiFactory.Place((RectTransform)_messageText.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(pad + 356f, 26f), new Vector2(760f, 44f));

            var hint = UiFactory.CreateText("Keys", panel, Font, UiStrings.ShopKeyHint, UiStyle.TextSizeSmall + 1, UiStyle.InkQuiet, TextAnchor.MiddleRight);
            UiFactory.Place((RectTransform)hint.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-pad, 26f), new Vector2(400f, 44f));
        }

        // ------------------------------------------------------------------
        // Drawing.
        // ------------------------------------------------------------------

        private void Draw()
        {
            if (_creditsText == null || _ledgerText == null || _gapText == null || _rows == null)
            {
                return;
            }

            _creditsText.text = "팀 크레딧 " + _board.TeamCredits.ToString(CultureInfo.InvariantCulture);
            _creditsText.color = _board.CanBuyAnything ? UiStyle.Trade : UiStyle.SpentStrong;

            _ledgerText.text = "번 것 " + _board.TotalEarned.ToString(CultureInfo.InvariantCulture)
                + " · 쓴 것 " + _board.TotalSpent.ToString(CultureInfo.InvariantCulture);

            _gapText.text = _board.RoleGapLine;

            // §11: the 섬광탄 case is a disagreement between two sections rather than a
            // missing feature, so it is coloured as a refusal and not as an offer.
            _gapText.color = _board.SubstituteItem == ShopItemId.None ? UiStyle.SpentStrong : UiStyle.Trade;

            var rows = _board.Rows;
            for (var i = 0; i < _rows.Length && i < rows.Count; i++)
            {
                DrawRow(_rows[i], rows[i], i == _selected);
            }

            DrawNight();
        }

        private void DrawNight()
        {
            if (_nightText == null || _nightWarningText == null || _visitText == null || _nightBar == null)
            {
                return;
            }

            var night = _board.Night;

            // §07: "안에서는 시간 감각이 없다." The strip is switched off entirely rather
            // than showing a placeholder, exactly as the HUD does — the shop is only ever
            // open on the surface, so this is a guard rather than a state, and a shop
            // that invented an hour would undo the 회중시계's whole reason to exist.
            _nightText.gameObject.SetActive(night.IsVisible);
            _nightWarningText.gameObject.SetActive(night.IsVisible);
            _nightBar.Visible = night.IsVisible;

            if (night.IsVisible)
            {
                // Time remaining, not time spent: every other meter in this game drains,
                // so a shop bar that filled would be the only one in the project that
                // reads the other way round. Redrawn every frame — that is the element
                // whose whole job is to move while nobody touches anything.
                var remaining = 1f - _board.TierProgress;
                _nightBar.SetFill(remaining, UiStyle.MeterColor(remaining));
            }

            // The words only change once a second, and writing a Text dirties the canvas
            // whether or not the string differs.
            var seconds = (int)_board.SecondsAtTheVehicle;
            if (seconds == _visitSecondsDrawn)
            {
                return;
            }

            _visitSecondsDrawn = seconds;
            _visitText.text = UiStrings.ShopVisitSeconds(_board.SecondsAtTheVehicle);

            // The counter gets louder as the deliberation eats the time left before
            // §07's next rung, through the same Calm → Trade → Spent ramp every meter in
            // the game uses. §07 prices "상점에서 고민" at about thirty seconds, and this
            // is what makes those thirty seconds cost more at 15:30 than at 2:00 without
            // this layer writing a deadline of its own down.
            _visitText.color = UiStyle.MeterColor(1f - _board.TimePressure01);

            if (!night.IsVisible)
            {
                return;
            }

            var countdown = UiStrings.NightCountdown(_board.SecondsUntilNightWorsens, _board.NextPhase);
            _nightText.text = string.IsNullOrEmpty(countdown)
                ? night.PhaseLabel
                : night.PhaseLabel + " · " + countdown;
            _nightText.color = night.IsLate ? UiStyle.Trade : UiStyle.InkStrong;

            _nightWarningText.text = night.WarningLabel;
            _nightWarningText.color = night.Phase == NightPhase.BeforeSunrise ? UiStyle.SpentStrong : UiStyle.Trade;
        }

        private void DrawRow(RowWidgets widgets, ShopRow row, bool selected)
        {
            widgets.Name.text = row.Name;
            widgets.Effect.text = row.Effect;
            widgets.Cost.text = row.Cost.ToString(CultureInfo.InvariantCulture);
            widgets.Cost.color = row.Affordable ? UiStyle.InkStrong : UiStyle.SpentStrong;

            // §08's 대가 column. Where the document writes "—" the line says so rather
            // than going blank: the 배터리 genuinely has no drawback — which is why §03
            // could make the flashlight universal issue — and a blank there is
            // indistinguishable from a row that failed to draw.
            var hasPrice = !string.IsNullOrEmpty(row.Price);
            widgets.Price.text = hasPrice ? row.Price : UiStrings.Unknown;
            widgets.Price.color = hasPrice ? UiStyle.Trade : UiStyle.InkQuiet;

            widgets.Stock.text = UiStrings.ShopStock(row.Stock);

            // The shortfall is never dropped for a marked row: the one the team most
            // needs is the one it most needs the arithmetic for. §08's argument is
            // "how much more loot", and §11's mark says "this is the row".
            var shortfall = UiStrings.ShopShortfall(row.Shortfall);

            if (row.ConflictsWithRoster)
            {
                // §08: "청음사가 있는 팀은 사면 안 된다." Still buyable — §04 says the
                // team's own accidents are content — but never quietly, and never on a
                // single press: see TryBuy.
                widgets.Note.text = Join("청음사가 있다 — §08: 사면 안 된다", shortfall);
                widgets.Note.color = UiStyle.SpentStrong;
            }
            else if (row.CoversMissingRole)
            {
                // §11's 돈으로 메우기, pointed at the hole this particular match has.
                widgets.Note.text = Join("※ " + UiStrings.Role(_board.MissingRole) + " 없음 · 이 판의 대체품", shortfall);
                widgets.Note.color = row.Affordable ? UiStyle.Trade : UiStyle.SpentStrong;
            }
            else if (!row.Affordable)
            {
                widgets.Note.text = shortfall;
                widgets.Note.color = UiStyle.SpentStrong;
            }
            else
            {
                widgets.Note.text = string.Empty;
                widgets.Note.color = UiStyle.InkQuiet;
            }

            widgets.Cursor.gameObject.SetActive(selected);

            if (widgets.Background != null)
            {
                widgets.Background.color = RowColor(row, selected);
            }
        }

        /// <summary>
        /// What a row's own value has to say before anybody reads a word of it: this is
        /// the one the cursor is on, this one warns, this one answers §11's hole, this
        /// one the shared purse covers, this one it does not.
        /// </summary>
        private static Color RowColor(ShopRow row, bool selected)
        {
            Color background;
            if (row.ConflictsWithRoster)
            {
                background = UiStyle.RowConflict;
            }
            else if (row.CoversMissingRole)
            {
                background = UiStyle.RowSubstitute;
            }
            else
            {
                background = row.Affordable ? UiStyle.RowAffordable : UiStyle.RowDisabled;
            }

            // Selection lifts the row rather than replacing its colour. Replacing it made
            // the cursor's row the brightest thing on the panel whatever it was, so a
            // 250-credit item the team could not afford outshone the ones it could — two
            // different facts fighting over one channel. The lift is small enough that a
            // selected unaffordable row stays darker than an unselected affordable one,
            // and the cursor bar is what says "Enter buys this".
            return selected ? background + UiStyle.RowSelectedLift : background;
        }

        // ------------------------------------------------------------------
        // Operating it.
        // ------------------------------------------------------------------

        /// <summary>
        /// Arrow keys, Enter and Tab.
        /// <para>
        /// Not WASD, and not by oversight: §08's shop leaves the feet free — the match
        /// is not paused and the player can walk away from the vehicle mid-argument —
        /// so binding movement keys to a list would make the team's shopping move
        /// somebody's body. E is left alone because the interactor already uses it to
        /// put the screen away.
        /// </para>
        /// </summary>
        private void ReadKeys()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.upArrowKey.wasPressedThisFrame)
            {
                MoveSelection(-1);
            }

            if (keyboard.downArrowKey.wasPressedThisFrame)
            {
                MoveSelection(1);
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                MoveSelection(-_columnBreak);
            }

            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                MoveSelection(_columnBreak);
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                TryBuy(_selected);
            }

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                OnSellClicked();
            }
#else
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                MoveSelection(-1);
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                MoveSelection(1);
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                MoveSelection(-_columnBreak);
            }

            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                MoveSelection(_columnBreak);
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                TryBuy(_selected);
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                OnSellClicked();
            }
#endif
        }

        /// <summary>Moves the cursor, clamped rather than wrapped — a list that wraps loses somebody's place mid-argument.</summary>
        public void MoveSelection(int delta)
        {
            var count = _board.Rows.Count;
            if (count <= 0)
            {
                return;
            }

            var next = _selected + delta;
            if (next < 0)
            {
                next = 0;
            }
            else if (next >= count)
            {
                next = count - 1;
            }

            if (next == _selected)
            {
                return;
            }

            _selected = next;

            // Moving off a warned row cancels its confirmation. §08's 소음기 may be
            // bought by mistake, but not by a keystroke aimed at something else.
            _awaitingConfirm = ShopItemId.None;
            Draw();
        }

        private void OnRowClicked(int index)
        {
            _selected = index;
            TryBuy(index);
        }

        /// <summary>
        /// Asks for one of something, and says why when nothing happens.
        /// <para>
        /// The old screen made an unaffordable row unclickable, which is an affordance
        /// and not an answer: §08's argument is arithmetic — "강화 손전등 하나 살까,
        /// 배터리 3개 살까?" — and the number four people need is how far short they
        /// are. So every row stays pressable and a refusal is spoken.
        /// </para>
        /// </summary>
        public void TryBuy(int index)
        {
            var rows = _board.Rows;
            if (_requests == null || index < 0 || index >= rows.Count)
            {
                return;
            }

            var row = rows[index];
            if (row.Id == ShopItemId.None)
            {
                return;
            }

            if (row.ConflictsWithRoster && _awaitingConfirm != row.Id)
            {
                // §08 states it outright — "청음사가 있는 팀은 사면 안 된다" — and §04
                // says the team's own accidents are content. Both are true if the
                // mistake takes two presses instead of one.
                _awaitingConfirm = row.Id;
                SetMessage(UiStrings.ShopConflictConfirm(row.Name), UiStyle.SpentStrong);
                return;
            }

            if (!row.Affordable)
            {
                _awaitingConfirm = ShopItemId.None;
                SetMessage(
                    row.Name + " " + row.Cost.ToString(CultureInfo.InvariantCulture)
                    + " · " + UiStrings.Purchase(PurchaseOutcome.NotEnoughCredits)
                    + " · " + UiStrings.ShopShortfall(row.Shortfall),
                    UiStyle.SpentStrong);
                return;
            }

            _awaitingConfirm = ShopItemId.None;

            var stockBefore = row.Stock;
            _requests.RequestPurchase(row.Id, 1);
            Refresh();

            // The local implementation resolves against the shop on the spot and can say
            // exactly what happened; a networked one cannot, and is read through the
            // board instead. Neither path may claim a refusal it has not been told about.
            var local = _requests as LocalShopRequests;
            if (local != null)
            {
                SetMessage(
                    local.LastOutcome == PurchaseOutcome.Purchased
                        ? UiStrings.ShopBought(row.Name, row.Price)
                        : row.Name + " · " + UiStrings.Purchase(local.LastOutcome),
                    local.LastOutcome == PurchaseOutcome.Purchased ? UiStyle.Trade : UiStyle.SpentStrong);
                return;
            }

            if (_board.Rows[index].Stock > stockBefore)
            {
                SetMessage(UiStrings.ShopBought(row.Name, row.Price), UiStyle.Trade);
                return;
            }

            // §13 gives the host the wallet. A client's screen has asked and has not
            // been answered yet — which is a different thing from a refusal, and saying
            // so is what stops the next redraw looking like a bug.
            SetMessage("요청 보냄 · " + row.Name, UiStyle.InkQuiet);
        }

        private void OnSellClicked()
        {
            if (_requests == null)
            {
                return;
            }

            var earnedBefore = _board.TotalEarned;
            _requests.RequestSellCarriedLoot();
            Refresh();

            var local = _requests as LocalShopRequests;
            if (local != null)
            {
                SetMessage(UiStrings.ShopSold(local.LastSaleValue), local.LastSaleValue > 0 ? UiStyle.Trade : UiStyle.InkQuiet);
                return;
            }

            var banked = _board.TotalEarned - earnedBefore;
            SetMessage(banked > 0 ? UiStrings.ShopSold(banked) : "요청 보냄", banked > 0 ? UiStyle.Trade : UiStyle.InkQuiet);
        }

        /// <summary>Joins two halves of a note with §08's own separator, dropping either if it is empty.</summary>
        private static string Join(string head, string tail)
        {
            if (string.IsNullOrEmpty(tail))
            {
                return head;
            }

            return string.IsNullOrEmpty(head) ? tail : head + " · " + tail;
        }

        private void SetMessage(string value, Color color)
        {
            if (_messageText == null)
            {
                return;
            }

            _messageText.text = value;
            _messageText.color = color;
        }

        /// <summary>
        /// Finds §07's clock without a wire that does not exist.
        /// <para>
        /// The screens are siblings: the object that owns this one builds one child per
        /// screen and binds the HUD to the match clock before any of them is shown, so
        /// the clock the HUD is reading is by construction the clock this match is
        /// stepping. A scene-wide search would find the wrong one under §14's
        /// two-instances-on-one-machine test; a static would be worse. Where a caller
        /// knows better it says so through <see cref="BindClock"/>, which wins.
        /// </para>
        /// <para>
        /// Re-read on every open rather than cached: a new match builds a new
        /// <c>MatchClock</c> and rebinds the HUD, and a shop still holding the last
        /// one would open on the previous night's hour — a wrong number, which §07
        /// treats as worse than none.
        /// </para>
        /// </summary>
        private void ResolveClock()
        {
            if (_clockGivenByCaller)
            {
                return;
            }

            _clock = null;

            var parent = transform.parent;
            if (parent == null)
            {
                return;
            }

            var hud = parent.GetComponentInChildren<HudScreen>(true);
            if (hud != null)
            {
                _clock = hud.Clock;
            }
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
                Button button,
                Image? background,
                Image cursor,
                Text name,
                Text effect,
                Image rule,
                Text price,
                Text cost,
                Text note,
                Text stock)
            {
                Button = button;
                Background = background;
                Cursor = cursor;
                Name = name;
                Effect = effect;
                Rule = rule;
                Price = price;
                Cost = cost;
                Note = note;
                Stock = stock;
            }

            public Button Button { get; }

            public Image? Background { get; }

            /// <summary>The keyboard cursor's edge marker on this row.</summary>
            public Image Cursor { get; }

            public Text Name { get; }

            /// <summary>§08's 효과 column.</summary>
            public Text Effect { get; }

            /// <summary>The hairline between what you get and what it costs you.</summary>
            public Image Rule { get; }

            /// <summary>§08's 대가 column — the field this screen exists to keep on screen.</summary>
            public Text Price { get; }

            /// <summary>Credits.</summary>
            public Text Cost { get; }

            /// <summary>What this row means for this team: §11's stand-in, §08's conflict, or how far short the wallet is.</summary>
            public Text Note { get; }

            public Text Stock { get; }
        }
    }
}
