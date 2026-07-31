#nullable enable

using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HorrorGame.UI
{
    /// <summary>
    /// The widgets a settings screen needs and a HUD does not: sliders, steppers,
    /// switches and key rows.
    /// <para>
    /// Built in code for the same reason as everything else in this layer
    /// (<see cref="UiFactory"/>): there are no UI prefabs, and a settings screen that
    /// silently fails to draw a row is a setting the player cannot reach and cannot
    /// report. It is a separate file from <see cref="UiFactory"/> because the HUD needs
    /// none of it — a readout is read, never operated.
    /// </para>
    /// <para>
    /// <b>Steppers rather than dropdowns.</b> A uGUI <c>Dropdown</c> needs an authored
    /// template hierarchy, a scroll rect and a blocker, all of which are prefab-shaped
    /// problems. A ◀ value ▶ stepper is three widgets, works with a pad as well as a
    /// mouse, and cannot open a list off the bottom of the screen.
    /// </para>
    /// </summary>
    public static class UiControls
    {
        /// <summary>
        /// Size of a row's explanatory line.
        /// <para>
        /// Two points larger than <see cref="UiStyle.TextSizeSmall"/>, which is sized for
        /// a HUD read in peripheral vision at arm's length. These lines are the only
        /// place the game explains why §05's field of view and §03's brightness are
        /// capped, and a reason nobody can read is a cap that looks like a bug.
        /// </para>
        /// </summary>
        public const int NoteSize = UiStyle.TextSizeSmall + 2;

        /// <summary>Height of one settings row, including its explanatory line.</summary>
        public const float SettingRowHeight = 64f;

        /// <summary>Gap between settings rows.</summary>
        public const float SettingRowGap = 6f;

        /// <summary>Width of the label column — wide enough for §05's longer Korean labels.</summary>
        public const float LabelWidth = 380f;

        /// <summary>Width of the control column.</summary>
        public const float ControlWidth = 300f;

        /// <summary>Width of the read-back column that shows the value as a number.</summary>
        public const float ValueWidth = 190f;

        /// <summary>A section title with a rule under it, so the screen has landmarks rather than a list.</summary>
        public static RectTransform CreateSection(RectTransform parent, Font? font, string title, float y, float width)
        {
            var root = UiFactory.CreateRect("Section_" + title, parent);
            UiFactory.Place(root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(width, 34f));

            var text = UiFactory.CreateText("Title", root, font, title, UiStyle.TextSize + 4, UiStyle.Ink, TextAnchor.LowerLeft);
            UiFactory.Place((RectTransform)text.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 8f), new Vector2(width, 26f));

            var rule = UiFactory.CreateImage("Rule", root, UiStyle.InkFaint);
            UiFactory.Place((RectTransform)rule.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(width, 1f));

            return root;
        }

        /// <summary>
        /// One row: a label, an optional reason under it, and room on the right for a
        /// control. The reason line is not decoration — two of this screen's settings
        /// change what happens in a match, and a player who cannot see why a range stops
        /// where it does reads the cap as a bug.
        /// </summary>
        public static RectTransform CreateRow(RectTransform parent, Font? font, string label, string note, float y, float width)
        {
            var root = UiFactory.CreateRect("Row_" + label, parent);
            UiFactory.Place(root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(width, SettingRowHeight));

            var title = UiFactory.CreateText("Label", root, font, label, UiStyle.TextSize, UiStyle.Ink, TextAnchor.UpperLeft);
            UiFactory.Place((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -4f), new Vector2(LabelWidth, 24f));

            if (!string.IsNullOrEmpty(note))
            {
                var reason = UiFactory.CreateText("Note", root, font, note, NoteSize, UiStyle.InkFaint, TextAnchor.UpperLeft);
                UiFactory.Place((RectTransform)reason.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -28f), new Vector2(width - 20f, 34f));
            }

            return root;
        }

        /// <summary>
        /// A horizontal slider inside a row. Returns the slider; the caller keeps it to
        /// write the value back when the screen is reopened.
        /// </summary>
        public static Slider CreateSlider(
            RectTransform row, float min, float max, float value, UnityAction<float> onChanged)
        {
            var root = UiFactory.CreateRect("Slider", row);
            UiFactory.Place(root, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-(ValueWidth + 16f), -10f), new Vector2(ControlWidth, 18f));

            var background = UiFactory.CreateImage("Background", root, UiStyle.BarBed, raycastTarget: true);
            var backgroundRect = UiFactory.Stretch((RectTransform)background.transform);
            backgroundRect.offsetMin = new Vector2(0f, 6f);
            backgroundRect.offsetMax = new Vector2(0f, -6f);

            var fillArea = UiFactory.CreateRect("Fill Area", root);
            UiFactory.Stretch(fillArea);
            fillArea.offsetMin = new Vector2(0f, 6f);
            fillArea.offsetMax = new Vector2(0f, -6f);

            var fill = UiFactory.CreateImage("Fill", fillArea, UiStyle.Calm);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var handleArea = UiFactory.CreateRect("Handle Slide Area", root);
            UiFactory.Stretch(handleArea);

            var handle = UiFactory.CreateImage("Handle", handleArea, UiStyle.Ink, raycastTarget: true);
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(0f, 1f);
            handleRect.sizeDelta = new Vector2(14f, 0f);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.transition = Selectable.Transition.ColorTint;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        /// <summary>The right-hand read-back for a row: the value as a number or a word.</summary>
        public static Text CreateValueText(RectTransform row, Font? font, string value)
        {
            var text = UiFactory.CreateText("Value", row, font, value, UiStyle.TextSize, UiStyle.Trade, TextAnchor.UpperRight);
            UiFactory.Place((RectTransform)text.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), new Vector2(ValueWidth, 24f));
            return text;
        }

        /// <summary>
        /// A ◀ value ▶ stepper. <paramref name="onStep"/> receives −1 or +1 and the
        /// caller decides what the next value is, so wrapping and clamping stay with the
        /// list rather than with the widget.
        /// </summary>
        public static Text CreateStepper(RectTransform row, Font? font, string value, Action<int> onStep)
        {
            var root = UiFactory.CreateRect("Stepper", row);
            UiFactory.Place(root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), new Vector2(ControlWidth + ValueWidth, 30f));

            var left = UiFactory.CreateButton("Prev", root, UiStyle.Row, delegate { onStep(-1); });
            UiFactory.Place((RectTransform)left.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(30f, 28f));
            Centre(UiFactory.CreateText("PrevLabel", left.transform, font, "◀", UiStyle.TextSize, UiStyle.Ink, TextAnchor.MiddleCenter), 30f);

            var right = UiFactory.CreateButton("Next", root, UiStyle.Row, delegate { onStep(1); });
            UiFactory.Place((RectTransform)right.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(30f, 28f));
            Centre(UiFactory.CreateText("NextLabel", right.transform, font, "▶", UiStyle.TextSize, UiStyle.Ink, TextAnchor.MiddleCenter), 30f);

            var text = UiFactory.CreateText("Value", root, font, value, UiStyle.TextSize, UiStyle.Trade, TextAnchor.MiddleCenter);
            UiFactory.Place((RectTransform)text.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ControlWidth + ValueWidth - 80f, 26f));
            return text;
        }

        /// <summary>
        /// A two-state switch drawn as a pressable word. Returns the label so the caller
        /// can rewrite it; <paramref name="onToggle"/> fires on every press.
        /// </summary>
        public static Text CreateSwitch(RectTransform row, Font? font, string value, UnityAction onToggle)
        {
            var button = UiFactory.CreateButton("Switch", row, UiStyle.Row, onToggle);
            UiFactory.Place((RectTransform)button.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), new Vector2(ValueWidth, 30f));

            var text = UiFactory.CreateText("Value", button.transform, font, value, UiStyle.TextSize, UiStyle.Trade, TextAnchor.MiddleCenter);
            UiFactory.Place((RectTransform)text.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ValueWidth - 12f, 26f));
            return text;
        }

        /// <summary>
        /// A menu button: wide, quiet, and the same shape everywhere. Returns the label
        /// so a caller can rewrite it — the pause menu's first entry changes wording.
        /// </summary>
        public static Text CreateMenuButton(
            RectTransform parent, Font? font, string label, Vector2 anchoredPosition, Vector2 size, UnityAction onClick)
        {
            var button = UiFactory.CreateButton("Button_" + label, parent, UiStyle.Row, onClick);
            UiFactory.Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, size);

            var text = UiFactory.CreateText("Label", button.transform, font, label, UiStyle.TextSize + 6, UiStyle.Ink, TextAnchor.MiddleCenter);
            UiFactory.Place((RectTransform)text.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 20f, size.y - 8f));
            return text;
        }

        private static void Centre(Text text, float width)
        {
            UiFactory.Place((RectTransform)text.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 24f));
        }
    }
}
