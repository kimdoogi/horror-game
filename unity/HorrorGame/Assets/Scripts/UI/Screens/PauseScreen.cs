#nullable enable

using System;
using HorrorGame.UI.Shell;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// The pause menu: 계속, 설정, 메뉴로 나가기.
    /// <para>
    /// <b>Opening this stops the match.</b> <see cref="MatchPause"/> does the work and
    /// explains why it has to; the part that belongs here is that the screen says so.
    /// The race is scored on elapsed time (§02's 완주 순위), and a player has no way to
    /// check whether a paused game kept charging them — so the only honest answer is a
    /// line on the screen that makes the promise.
    /// </para>
    /// <para>
    /// <b>It darkens the world behind it.</b> Nothing is advancing while this is up, and
    /// the screen should look like it. (The remark this replaces contrasted it with
    /// §08's shop, which was deliberately translucent because the team was standing in
    /// the open with the night advancing. DESCENT-PIVOT §7 step 7 deleted the shop.)
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseScreen : UiScreen
    {
        private Action? _onResume;
        private Action? _onSettings;
        private Action? _onQuitToMenu;

        private Text? _clockNote;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderPanel + 40; }
        }

        /// <inheritdoc />
        protected override bool Interactive
        {
            get { return true; }
        }

        /// <summary>Stops the match and shows the menu.</summary>
        public void Open(Action onResume, Action onSettings, Action onQuitToMenu)
        {
            _onResume = onResume;
            _onSettings = onSettings;
            _onQuitToMenu = onQuitToMenu;

            MatchPause.Pause();
            SetVisible(true);

            if (_clockNote != null)
            {
                _clockNote.text = MatchPause.IsPaused
                    ? "기록 시계가 멈췄다. 괴물도 함께 멈춰 있다."
                    : "기록 시계를 멈추지 못했다 — 이 판은 계속 흐르고 있다.";

                _clockNote.color = MatchPause.IsPaused ? UiStyle.Calm : UiStyle.Spent;
            }
        }

        /// <summary>Hides the menu and starts the match again.</summary>
        public void Resume()
        {
            SetVisible(false);
            MatchPause.Resume();
            _onResume?.Invoke();
        }

        /// <summary>Hides the menu without restarting the match — the settings screen is about to open over it.</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        /// <summary>Shows the menu again with the match still stopped.</summary>
        public void Show()
        {
            SetVisible(true);
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            // 0.90 rather than 0.82: the far end of a corridor is the brightest thing in
            // this game's frame, and at 0.82 it read straight through the one line this
            // screen exists to make — that §07's clock has stopped.
            var dim = UiFactory.CreateImage("Dim", root, new Color(0.01f, 0.01f, 0.015f, 0.90f), raycastTarget: true);
            UiFactory.Stretch((RectTransform)dim.transform);

            var column = UiFactory.CreateRect("Column", root);
            UiFactory.Place(column, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 460f));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Title", column, Font, "멈춤", UiStyle.TextSizeTitle + 8, UiStyle.Ink, TextAnchor.UpperCenter).transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(520f, 56f));

            // Written on build as well as on Open, so a review render photographs the
            // promise this screen exists to make rather than a gap where it goes.
            _clockNote = UiFactory.CreateText(
                "ClockNote", column, Font,
                "기록 시계가 멈췄다. 괴물도 함께 멈춰 있다.",
                UiControls.NoteSize, UiStyle.Calm, TextAnchor.UpperCenter);
            UiFactory.Place((RectTransform)_clockNote.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(520f, 24f));

            UiControls.CreateMenuButton(column, Font, "계속", new Vector2(0f, 40f), new Vector2(400f, 60f), delegate { Resume(); });
            UiControls.CreateMenuButton(column, Font, "설정", new Vector2(0f, -30f), new Vector2(400f, 60f), delegate { _onSettings?.Invoke(); });
            UiControls.CreateMenuButton(column, Font, "메뉴로 나가기", new Vector2(0f, -100f), new Vector2(400f, 60f), delegate { _onQuitToMenu?.Invoke(); });

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "LeaveNote", column, Font,
                    "메뉴로 나가면 이 경주는 끝난다. §02의 완주 순위는 B8 한가운데에 닿아서 받는 것이지 여기서 나가서 받는 것이 아니다.",
                    UiControls.NoteSize, UiStyle.InkFaint, TextAnchor.UpperCenter).transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(520f, 44f));
        }
    }
}
