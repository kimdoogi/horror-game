#nullable enable

using System;
using System.Globalization;
using HorrorGame.Core.Match;
using HorrorGame.UI.Readouts;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// The ending. §02's four rows, drawn so that they cannot be mistaken for each
    /// other.
    /// <para>
    /// <b>The ledger is the screen.</b> §02 is not a win/lose table — its argument is
    /// an asymmetry in what survives: <em>"한 명이라도 탈출하면 그 판에서 알아낸
    /// 정보가 보존된다. 전멸하면 전리품 · 크레딧 · 정보가 전부 사라진다. 이 비대칭이
    /// '지금 나갈까?'를 진짜 갈등으로 만든다."</em> The argument the team had at
    /// minute twenty-two is only worth having if the screen at minute thirty-one pays
    /// it off, so the four keep/lose lines are the largest element after the
    /// headline, and 생존 differs from 패배 in three of them rather than in a word.
    /// </para>
    /// <para>
    /// <b>A wipe is drawn as a loss of everything, including the colour.</b> §02's
    /// 패배 row says "그 판의 모든 것을 잃는다"; on that screen every line is struck,
    /// nothing is highlighted, and there is no consolation figure — no "you got 60%
    /// of the way", no clue count, no partial credit. A total loss that displayed a
    /// score would be a partial loss.
    /// </para>
    /// <para>
    /// <b>And it never prints a clue.</b> §03's constraint is that the only copy of
    /// what the team learned is in a player's memory. A results screen that listed
    /// the marks would be the clue log arriving one screen late, and the team would
    /// learn to stop reading carefully and wait for the recap — which is exactly the
    /// behaviour §14's fourth verification question is checking for. The 정보 line
    /// reports <em>that</em> the knowledge survived, never <em>what</em> it was.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EndScreen : UiScreen
    {
        private Text? _headline;
        private Text? _condition;
        private Text? _epilogue;
        private Text? _tally;
        private SpoilWidgets[]? _spoils;
        private Button? _continueButton;
        private Text? _continueLabel;
        private Action? _onContinue;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderEnd; }
        }

        /// <summary>True — the only thing on it is the way out.</summary>
        protected override bool Interactive
        {
            get { return true; }
        }

        /// <summary>The ending currently drawn.</summary>
        public EndScreenReadout Current { get; private set; }

        /// <summary>
        /// Shows §02's verdict.
        /// </summary>
        /// <param name="resolution">
        /// The core's reading. Taken as-is rather than recomputed: §02's rule is "did
        /// anybody get out", <c>OutcomeEvaluator</c> already applies it, and a screen
        /// that re-derived the spoils could disagree with the match that produced them.
        /// </param>
        /// <param name="onContinue">What the continue button does. Null hides it.</param>
        public void Show(MatchResolution resolution, Action? onContinue)
        {
            EnsureBuilt();
            _onContinue = onContinue;
            Current = EndScreenReadout.From(resolution);
            SetVisible(true);
            Draw();
        }

        /// <summary>Hides the screen.</summary>
        public void Hide()
        {
            SetVisible(false);
            _onContinue = null;
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            var panel = UiFactory.CreateImage("Panel", root, UiStyle.Panel);
            var panelRect = UiFactory.Stretch((RectTransform)panel.transform);

            _headline = Line(panelRect, "Headline", new Vector2(0f, -140f), UiStyle.TextSizeHeadline, UiStyle.Ink);
            _condition = Line(panelRect, "Condition", new Vector2(0f, -212f), UiStyle.TextSize, UiStyle.InkFaint);
            _epilogue = Line(panelRect, "Epilogue", new Vector2(0f, -252f), UiStyle.TextSize, UiStyle.Ink);
            _tally = Line(panelRect, "Tally", new Vector2(0f, -300f), UiStyle.TextSizeSmall, UiStyle.InkFaint);

            _spoils = new SpoilWidgets[4];
            for (var i = 0; i < _spoils.Length; i++)
            {
                var y = -380f - (i * 52f);
                var label = SpoilText(panelRect, "SpoilLabel" + i, new Vector2(-300f, y), TextAnchor.MiddleRight, UiStyle.TextSize, UiStyle.Ink, 260f);
                var state = SpoilText(panelRect, "SpoilState" + i, new Vector2(-24f, y), TextAnchor.MiddleRight, UiStyle.TextSize, UiStyle.Ink, 200f);
                var note = SpoilText(panelRect, "SpoilNote" + i, new Vector2(20f, y), TextAnchor.MiddleLeft, UiStyle.TextSizeSmall, UiStyle.InkFaint, 520f);
                _spoils[i] = new SpoilWidgets(label, state, note);
            }

            _continueButton = UiFactory.CreateButton("Continue", panelRect, UiStyle.Row, OnContinueClicked);
            UiFactory.Place((RectTransform)_continueButton.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 80f), new Vector2(320f, 48f));
            _continueLabel = UiFactory.CreateText(
                "ContinueLabel", _continueButton.transform, Font, "나가기", UiStyle.TextSize, UiStyle.Ink, TextAnchor.MiddleCenter);
            UiFactory.Place((RectTransform)_continueLabel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 30f));
        }

        private void Draw()
        {
            if (_headline == null || _condition == null || _epilogue == null || _tally == null || _spoils == null)
            {
                return;
            }

            var readout = Current;

            _headline.text = readout.Headline;

            // Three visual registers, not four, because §02's top two rows are the same
            // kind of ending with a different cost. 생존 and 패배 are the pair the design
            // measures everything against, so they are the ones that must never look
            // alike.
            if (readout.IsTotalLoss)
            {
                _headline.color = UiStyle.Spent;
            }
            else if (readout.IsVictory)
            {
                _headline.color = UiStyle.Trade;
            }
            else
            {
                _headline.color = UiStyle.Ink;
            }

            _condition.text = readout.Condition;
            _epilogue.text = readout.Epilogue;

            _tally.text = "탈출 " + readout.Escaped.ToString(CultureInfo.InvariantCulture)
                + " · 남겨진 사람 " + readout.Lost.ToString(CultureInfo.InvariantCulture);

            DrawSpoil(_spoils[0], readout.Objective, false);

            // §02 hangs the entire "지금 나갈까?" argument on this one line, so on the
            // 생존 ending — where it is the only thing the team came home with — it is
            // the line that carries the colour.
            DrawSpoil(_spoils[1], readout.Information, !readout.IsVictory && readout.Information.Kept);

            DrawSpoil(_spoils[2], readout.Loot, false);
            DrawSpoil(_spoils[3], readout.Credits, false);

            if (_continueButton != null)
            {
                _continueButton.gameObject.SetActive(_onContinue != null);
            }
        }

        private static void DrawSpoil(SpoilWidgets widgets, SpoilLine line, bool emphasise)
        {
            widgets.Label.text = line.Label;
            widgets.State.text = line.Kept ? "남는다" : "잃었다";
            widgets.Note.text = line.Note;

            var color = line.Kept
                ? (emphasise ? UiStyle.Trade : UiStyle.Calm)
                : UiStyle.Spent;

            widgets.Label.color = line.Kept ? UiStyle.Ink : UiStyle.InkFaint;
            widgets.State.color = color;
            widgets.Note.color = UiStyle.InkFaint;
        }

        private void OnContinueClicked()
        {
            var callback = _onContinue;
            if (callback != null)
            {
                callback();
            }
        }

        private Text Line(RectTransform parent, string name, Vector2 position, int size, Color color)
        {
            var text = UiFactory.CreateText(name, parent, Font, string.Empty, size, color, TextAnchor.UpperCenter);
            UiFactory.Place((RectTransform)text.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), position, new Vector2(1200f, size + 14f));
            return text;
        }

        private Text SpoilText(
            RectTransform parent, string name, Vector2 position, TextAnchor alignment, int size, Color color, float width)
        {
            var text = UiFactory.CreateText(name, parent, Font, string.Empty, size, color, alignment);
            UiFactory.Place((RectTransform)text.transform,
                new Vector2(0.5f, 1f), new Vector2(alignment == TextAnchor.MiddleRight ? 1f : 0f, 1f), position, new Vector2(width, size + 12f));
            return text;
        }

        /// <summary>The three widgets of one ledger line.</summary>
        private sealed class SpoilWidgets
        {
            public SpoilWidgets(Text label, Text state, Text note)
            {
                Label = label;
                State = state;
                Note = note;
            }

            public Text Label { get; }

            public Text State { get; }

            public Text Note { get; }
        }
    }
}
