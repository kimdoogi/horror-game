#nullable enable

using HorrorGame.UI;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// The one interaction prompt. §10.
    /// <para>
    /// There is exactly one, it draws whatever the crosshair is on, and it is blank
    /// the rest of the time — the same brief <c>HudScreen</c> works to, for the same
    /// §01 reason: an idle element is a light source in the corner of a horror game.
    /// </para>
    /// <para>
    /// It carries three lines and no fourth. What the thing is, what acting on it
    /// costs (§10: "얻는 게 있으면 대가가 있다", said while the choice is open), and —
    /// only after a refusal — the rule that refused. Nothing here remembers anything:
    /// looking away clears it, which is the same constraint §03 puts on the clue
    /// overlay and for the same reason.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionPromptScreen : UiScreen
    {
        private RectTransform? _group;
        private Text? _title;
        private Text? _detail;
        private Text? _refusal;
        private UiBar? _progress;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderHud; }
        }

        /// <summary>False. Interacting is done by looking and pressing, never by clicking.</summary>
        protected override bool Interactive
        {
            get { return false; }
        }

        /// <summary>Draws one thing's prompt.</summary>
        /// <param name="title">What it is.</param>
        /// <param name="detail">What it costs or needs, citing the section that says so.</param>
        /// <param name="refusal">Why the last attempt did nothing, or empty.</param>
        /// <param name="progress01">Held-interaction progress, or a negative value to hide the bar.</param>
        public void Show(string title, string detail, string refusal, float progress01)
        {
            EnsureBuilt();
            SetVisible(true);

            if (_group == null || _title == null || _detail == null || _refusal == null || _progress == null)
            {
                return;
            }

            _group.gameObject.SetActive(true);
            _title.text = title;
            _detail.text = detail;

            _refusal.text = refusal;
            _refusal.color = UiStyle.Spent;

            _progress.Visible = progress01 >= 0f;
            _progress.SetFill(progress01 < 0f ? 0f : progress01);
        }

        /// <summary>Clears the prompt. Nothing survives it.</summary>
        public void Clear()
        {
            if (_group != null)
            {
                _group.gameObject.SetActive(false);
            }
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            var group = UiFactory.Place(
                UiFactory.CreateRect("Prompt", root),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -PromptDropFromCentre),
                new Vector2(PromptWidth, PromptHeight));
            _group = group;

            _title = Line(group, "Title", new Vector2(0f, 0f), UiStyle.TextSize, UiStyle.Ink);
            _detail = Line(group, "Detail", new Vector2(0f, -LineStep), UiStyle.TextSizeSmall, UiStyle.Trade);
            _refusal = Line(group, "Refusal", new Vector2(0f, -(LineStep * 2f)), UiStyle.TextSizeSmall, UiStyle.Spent);

            var bar = UiFactory.CreateBar("HoldProgress", group, PromptBarWidth, UiStyle.BarHeight);
            UiFactory.Place(
                bar.Root,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -(LineStep * 3f)),
                new Vector2(PromptBarWidth, UiStyle.BarHeight));
            _progress = bar;

            group.gameObject.SetActive(false);
        }

        private Text Line(RectTransform parent, string name, Vector2 position, int size, Color colour)
        {
            var text = UiFactory.CreateText(name, parent, Font, string.Empty, size, colour, TextAnchor.UpperCenter);
            UiFactory.Place(
                (RectTransform)text.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                position,
                new Vector2(PromptWidth, size + LinePadding));
            return text;
        }

        // Layout only. The clue overlay sits at -140 from centre, so the prompt drops
        // further to keep both readable when a clue is being read in front of a prop.
        private const float PromptDropFromCentre = 40f;
        private const float PromptWidth = 720f;
        private const float PromptHeight = 140f;
        private const float PromptBarWidth = 200f;
        private const float LineStep = 26f;
        private const float LinePadding = 8f;
    }
}
