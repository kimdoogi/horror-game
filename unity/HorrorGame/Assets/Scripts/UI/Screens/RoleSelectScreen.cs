#nullable enable

using System.Globalization;
using HorrorGame.Core;
using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;
using HorrorGame.UI.Readouts;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// The lobby. §11 — <em>"4인 플레이 + 5개 중 4개 선택."</em>
    /// <para>
    /// <b>The screen is about the hole, not about the picks.</b> §11 treats the
    /// missing role as the match's character — "매판 하나가 빠지고, 그게 그 판의
    /// 성격이 된다" — and gives it a table: what each absence costs, and what money
    /// can do about it. So the gap panel is the largest thing here, it updates as the
    /// last slot fills, and it names §11's stand-in with the stand-in's own weakness
    /// beside it, because §11 keeps roles valuable by making every substitute
    /// strictly worse.
    /// </para>
    /// <para>
    /// <b>The 관측자 row says 불가능 and means it.</b> §11: "관측자만 대체 수단이
    /// 없다 — 유일하게 살 수 없는 정보를 제공하는 직업이다." Four of the five
    /// absences have a price tag; this one has a sentence instead, and that
    /// asymmetry is deliberate rather than missing content.
    /// </para>
    /// <para>
    /// <b>The 섬광탄 is shown as unpriced.</b> §11 promises it for a missing 섬광수,
    /// §08's 구매 목록 does not stock it, and ARCHITECTURE §6 says a layer that finds
    /// two sections disagreeing records the disagreement instead of quietly picking a
    /// side. <c>RoleGap.IsInShopList</c> is Core's flag for it; this screen prints
    /// "가격 미정" rather than inventing a number.
    /// </para>
    /// <para>
    /// Claiming goes through <see cref="IRoleClaimRequests"/>. Four people picking
    /// from five roles is a contested write and §13 gives the host authority over it,
    /// so losing a race looks exactly like a board that came back unchanged.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoleSelectScreen : UiScreen
    {
        private readonly LobbyBoard _board = new LobbyBoard();

        private RoleSelection? _selection;
        private IRoleClaimRequests? _requests;
        private int _localPlayerIndex = -1;

        private RoleWidgets[]? _roleRows;
        private Text? _gapTitle;
        private Text? _gapProblem;
        private Text? _gapSubstitute;
        private Text? _gapSubstituteLimit;
        private Text? _completability;
        private Button? _startButton;
        private Text? _startLabel;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderPanel; }
        }

        /// <summary>True — the lobby is where the mouse belongs.</summary>
        protected override bool Interactive
        {
            get { return true; }
        }

        /// <summary>The board currently drawn.</summary>
        public LobbyBoard Board
        {
            get { return _board; }
        }

        /// <summary>Opens the lobby over a selection.</summary>
        /// <param name="selection">The lobby's picks — the host's copy, or a client's mirror of it.</param>
        /// <param name="requests">Where claims go. §13 resolves them on the host.</param>
        /// <param name="localPlayerIndex">Which slot this client speaks for, so its own pick can be marked.</param>
        public void Open(RoleSelection selection, IRoleClaimRequests requests, int localPlayerIndex)
        {
            _selection = selection;
            _requests = requests;
            _localPlayerIndex = localPlayerIndex;
            SetVisible(true);
            Refresh();
        }

        /// <summary>Closes the lobby.</summary>
        public void Close()
        {
            SetVisible(false);
            _selection = null;
            _requests = null;
        }

        /// <summary>Redraws from the selection. Call whenever the host confirms a claim.</summary>
        public void Refresh()
        {
            EnsureBuilt();
            _board.Refresh(_selection, _localPlayerIndex);
            Draw();
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            var panel = UiFactory.CreateImage("Panel", root, UiStyle.Panel);
            var panelRect = UiFactory.Place(
                (RectTransform)panel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240f, 880f));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Title", panelRect, Font,
                    "직업 선택 — " + GameConstants.RoleCount.ToString(CultureInfo.InvariantCulture)
                        + "개 중 " + GameConstants.PlayersPerMatch.ToString(CultureInfo.InvariantCulture) + "개",
                    UiStyle.TextSizeTitle, UiStyle.Ink, TextAnchor.UpperLeft).transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(700f, 44f));

            BuildRoleRows(panelRect);
            BuildGapPanel(panelRect);

            _startButton = UiFactory.CreateButton("Start", panelRect, UiStyle.Row, OnStartClicked);
            UiFactory.Place((RectTransform)_startButton.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 24f), new Vector2(300f, 48f));
            _startLabel = UiFactory.CreateText(
                "StartLabel", _startButton.transform, Font, "잠입", UiStyle.TextSize, UiStyle.Ink, TextAnchor.MiddleCenter);
            UiFactory.Place((RectTransform)_startLabel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280f, 30f));
        }

        private void BuildRoleRows(RectTransform panel)
        {
            var roles = RoleSelection.AllRoles;
            _roleRows = new RoleWidgets[roles.Count];

            for (var i = 0; i < roles.Count; i++)
            {
                var role = roles[i];
                var y = -92f - (i * (UiStyle.RowHeight + 16f));

                var button = UiFactory.CreateButton("Role_" + role, panel, UiStyle.Row, delegate { OnRoleClicked(role); });
                var rect = UiFactory.Place((RectTransform)button.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, y), new Vector2(720f, UiStyle.RowHeight + 10f));

                var name = RowText(rect, "Name", TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(14f, -6f), 240f, UiStyle.TextSize, UiStyle.Ink);
                var ability = RowText(rect, "Ability", TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(180f, -6f), 540f, UiStyle.TextSizeSmall, UiStyle.Ink);

                // §04's 제약, drawn under the ability at the same weight as anything else
                // on the row. §10's principle applies to picking a role too: an ability
                // shown without its constraint is an advertisement.
                var limit = RowText(rect, "Limit", TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(180f, 8f), 540f, UiStyle.TextSizeSmall, UiStyle.Trade);
                var holder = RowText(rect, "Holder", TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(14f, 8f), 240f, UiStyle.TextSizeSmall, UiStyle.InkFaint);

                _roleRows[i] = new RoleWidgets(button, button.targetGraphic as Image, name, ability, limit, holder);
            }
        }

        private void BuildGapPanel(RectTransform panel)
        {
            var gap = UiFactory.CreateImage("Gap", panel, UiStyle.RowSubstitute);
            var gapRect = UiFactory.Place((RectTransform)gap.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -92f), new Vector2(440f, 300f));

            _gapTitle = GapText(gapRect, "GapTitle", new Vector2(16f, -14f), UiStyle.TextSizeTitle, UiStyle.Trade);
            _gapProblem = GapText(gapRect, "GapProblem", new Vector2(16f, -66f), UiStyle.TextSize, UiStyle.Ink);
            _gapSubstitute = GapText(gapRect, "GapSubstitute", new Vector2(16f, -114f), UiStyle.TextSize, UiStyle.Ink);
            _gapSubstituteLimit = GapText(gapRect, "GapSubstituteLimit", new Vector2(16f, -148f), UiStyle.TextSizeSmall, UiStyle.InkFaint);
            _completability = GapText(gapRect, "Completability", new Vector2(16f, -206f), UiStyle.TextSizeSmall, UiStyle.InkFaint);
        }

        private void Draw()
        {
            DrawRoleRows();
            DrawGap();

            if (_startButton != null && _startLabel != null)
            {
                _startButton.interactable = _board.IsComplete;
                _startLabel.text = _board.IsComplete ? "잠입" : "직업을 고르는 중";
            }
        }

        private void DrawRoleRows()
        {
            if (_roleRows == null)
            {
                return;
            }

            var options = _board.Options;
            for (var i = 0; i < _roleRows.Length && i < options.Count; i++)
            {
                var option = options[i];
                var widgets = _roleRows[i];

                widgets.Name.text = option.Name;
                widgets.Ability.text = option.Ability;
                widgets.Limit.text = option.Limit;

                if (option.TakenByLocalPlayer)
                {
                    widgets.Holder.text = "내 선택";
                    widgets.Holder.color = UiStyle.Trade;
                }
                else if (option.Taken)
                {
                    widgets.Holder.text = (option.TakenBySlot + 1).ToString(CultureInfo.InvariantCulture) + "번";
                    widgets.Holder.color = UiStyle.InkFaint;
                }
                else
                {
                    widgets.Holder.text = string.Empty;
                }

                // A taken role stays visible and unpressable. Hiding it would hide the
                // shape of the lineup, and the lineup is what §11 wants argued about.
                widgets.Button.interactable = !option.Taken || option.TakenByLocalPlayer;

                if (widgets.Background != null)
                {
                    widgets.Background.color = option.TakenByLocalPlayer
                        ? UiStyle.RowSubstitute
                        : (option.Taken ? UiStyle.RowDisabled : UiStyle.Row);
                }
            }
        }

        private void DrawGap()
        {
            if (_gapTitle == null || _gapProblem == null || _gapSubstitute == null
                || _gapSubstituteLimit == null || _completability == null)
            {
                return;
            }

            if (!_board.IsComplete)
            {
                _gapTitle.text = "아직 정해지지 않음";
                _gapProblem.text = "남은 직업 " + _board.Unclaimed.Count.ToString(CultureInfo.InvariantCulture) + "개";
                _gapSubstitute.text = string.Empty;
                _gapSubstituteLimit.text = string.Empty;
                _completability.text = string.Empty;
                return;
            }

            _gapTitle.text = UiStrings.Role(_board.MissingRole) + " 없음";
            _gapProblem.text = _board.AbsenceProblem;

            if (!_board.SubstituteExists)
            {
                // §11's one row with no answer. Said as a sentence, because a blank here
                // would read as data the lobby failed to load.
                _gapSubstitute.text = "돈으로 메울 수 없다";
                _gapSubstitute.color = UiStyle.Spent;
                _gapSubstituteLimit.text = _board.SubstituteLimit;
            }
            else if (_board.SubstituteMissingFromShop)
            {
                // §11 names it, §08 does not sell it. Reported, not resolved.
                _gapSubstitute.text = _board.SubstituteName + " — 가격 미정";
                _gapSubstitute.color = UiStyle.Trade;
                _gapSubstituteLimit.text = _board.SubstituteLimit + " (§08 목록에 없음)";
            }
            else
            {
                _gapSubstitute.text = _board.SubstituteName + " "
                    + _board.SubstituteCost.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
                _gapSubstitute.color = UiStyle.Trade;
                _gapSubstituteLimit.text = _board.SubstituteLimit;
            }

            var report = _board.Completability;
            if (report == null)
            {
                _completability.text = string.Empty;
                return;
            }

            // §11's 절대 규칙 — "필수 직업이 있으면 풀이 가짜가 된다" — answered for this
            // lineup. It should always say yes; if it ever says no, a role has become
            // compulsory and the design has a problem the lobby should not hide.
            _completability.text = report.IsCompletable
                ? "이 조합으로 목표물 회수 가능 · 힘든 항목 "
                    + report.DegradedCount.ToString(CultureInfo.InvariantCulture) + "개"
                : "이 조합으로는 끝낼 수 없다: "
                    + (report.Blocker.HasValue ? UiStrings.Capability(report.Blocker.Value) : UiStrings.Unknown);
            _completability.color = report.IsCompletable ? UiStyle.InkFaint : UiStyle.Spent;
        }

        private void OnRoleClicked(RoleId role)
        {
            if (_requests == null)
            {
                return;
            }

            var options = _board.Options;
            for (var i = 0; i < options.Count; i++)
            {
                if (options[i].Role == role && options[i].TakenByLocalPlayer)
                {
                    _requests.RequestRelease();
                    Refresh();
                    return;
                }
            }

            _requests.RequestClaim(role);
            Refresh();
        }

        private void OnStartClicked()
        {
            if (_requests == null)
            {
                return;
            }

            _requests.RequestStart();
            Refresh();
        }

        private Text GapText(RectTransform parent, string name, Vector2 position, int size, Color color)
        {
            var text = UiFactory.CreateText(name, parent, Font, string.Empty, size, color, TextAnchor.UpperLeft);
            UiFactory.Place((RectTransform)text.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(408f, size + 10f));
            return text;
        }

        private Text RowText(
            RectTransform parent, string name, TextAnchor alignment, Vector2 anchor, Vector2 position, float width, int size, Color color)
        {
            var text = UiFactory.CreateText(name, parent, Font, string.Empty, size, color, alignment);
            UiFactory.Place((RectTransform)text.transform, anchor, anchor, position, new Vector2(width, size + 8f));
            return text;
        }

        /// <summary>The widgets of one role row.</summary>
        private sealed class RoleWidgets
        {
            public RoleWidgets(Button button, Image? background, Text name, Text ability, Text limit, Text holder)
            {
                Button = button;
                Background = background;
                Name = name;
                Ability = ability;
                Limit = limit;
                Holder = holder;
            }

            public Button Button { get; }

            public Image? Background { get; }

            public Text Name { get; }

            public Text Ability { get; }

            /// <summary>§04's 제약 — never hidden, see the class remarks.</summary>
            public Text Limit { get; }

            public Text Holder { get; }
        }
    }
}
