#nullable enable

using System;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// The first thing anybody sees. Title, 시작, 혼자 내려가기, 설정, 종료.
    /// <para>
    /// <b>Two ways down, because 시작 alone could not reach a race.</b> 시작 goes
    /// through <c>LobbyEntry</c> to §11's lobby, and the lobby refuses to start below
    /// <c>GameConstants.RaceRunnersMin</c> — <em>"혼자서는 경주가 되지 않는다"</em>,
    /// which is correct and is also why a player on their own could press the only
    /// button on this menu and never see the building. 혼자 내려가기 is
    /// <c>GameShell.StartMatch</c> with no question asked: the same descent, one runner,
    /// no placings worth arguing about. It is labelled as the practice run it is rather
    /// than dressed up as a race, because §02's 순위 is the whole point and a solo
    /// "1위" would be a lie the menu told.
    /// </para>
    /// <para>
    /// <b>It is drawn over the game, not over a colour.</b> The scene behind this canvas
    /// is a lit corridor with the project's own fog, grade and flashlight on it, drifting
    /// slowly (<c>MenuBackdrop</c>). That is a store-page decision as much as an
    /// art one — §13 lists the store page as a deliverable and the first screenshot of a
    /// horror game is the menu — but it is also the cheapest honest promise the game can
    /// make: what is behind these four words is the actual renderer, the actual fog
    /// density solved in ART.md §3.4, and the actual beam at <c>FlashlightBeam</c>'s
    /// intensity. A flat charcoal rectangle promises nothing and is a lie by omission.
    /// </para>
    /// <para>
    /// <b>The words are dim.</b> <see cref="UiStyle"/>'s rule — the interface never
    /// competes with the beam — applies here more than anywhere: a menu that punches out
    /// of the frame at full white sets the eye's adaptation, and the first thing the
    /// player does after pressing 시작 is walk into a room graded to a median luminance
    /// of 3–16 out of 255. The scrim behind the text is there so the words survive a
    /// bright frame of the drift, not so they shout.
    /// </para>
    /// <para>
    /// <b>The headphone line is not decoration.</b> §05 settles it with "3D 오디오는
    /// 카메라 기준 → 헤드폰 필수", and §13 carries that onto the store page. It used to
    /// be argued from §04's 청음사 — a role the pivot deleted — and the argument
    /// survives the role because the two things a runner navigates by are both
    /// positional: which corridor the creature is in, and which direction the voice of
    /// the runner beside them is coming from (§13's 근접 음성). On speakers both
    /// collapse to "somewhere", so the requirement is stated before the first match
    /// rather than in an options tab they may never open.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuScreen : UiScreen
    {
        private const float ButtonWidth = 420f;
        private const float ButtonHeight = 62f;
        private const float ButtonGap = 14f;

        private Action? _onStart;
        private Action? _onSolo;
        private Action? _onSettings;
        private Action? _onQuit;

        private Text? _headphoneText;
        private Image? _headphoneBand;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderPanel; }
        }

        /// <inheritdoc />
        protected override bool Interactive
        {
            get { return true; }
        }

        /// <summary>Shows the menu and wires its four entries.</summary>
        /// <param name="onStart">시작 — ask §11's lobby, and race whoever is in it.</param>
        /// <param name="onSolo">혼자 내려가기 — §14 step 1: build a match and go down alone.</param>
        /// <param name="onSettings">Opens the settings screen over this one.</param>
        /// <param name="onQuit">Leaves the game.</param>
        public void Open(Action onStart, Action onSolo, Action onSettings, Action onQuit)
        {
            _onStart = onStart;
            _onSolo = onSolo;
            _onSettings = onSettings;
            _onQuit = onQuit;
            SetVisible(true);
            RefreshHeadphoneNotice();
        }

        /// <summary>Hides the menu without dropping its wiring — 설정 comes back here.</summary>
        public void Close()
        {
            SetVisible(false);
        }

        /// <summary>
        /// Redraws §05's headphone requirement. Loud until the player has been through
        /// the settings screen once, quiet after — the fact does not change, only how
        /// hard it is arguing for attention.
        /// </summary>
        public void RefreshHeadphoneNotice()
        {
            if (_headphoneText == null || _headphoneBand == null)
            {
                return;
            }

            var seen = Settings.SettingsService.Current.HeadphoneNoticeSeen;

            _headphoneText.text = seen
                ? "§05 · 헤드폰 권장 — 3D 오디오는 카메라 기준이다. 괴물도, 옆 주자의 목소리도 방향으로 온다."
                : "§05 · 헤드폰이 필요하다. 3D 오디오는 카메라 기준이며, 스피커로는 괴물의 방향도 근접 음성의 방향도 성립하지 않는다.";

            _headphoneText.color = seen ? UiStyle.InkFaint : UiStyle.Trade;
            _headphoneBand.color = seen ? new Color(0f, 0f, 0f, 0.45f) : new Color(0.16f, 0.10f, 0.03f, 0.72f);
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            // A left-hand scrim rather than a full-screen one. The drift is the
            // photograph; darkening all of it to make text legible would be paying for
            // the words with the picture.
            //
            // Ramped in the mesh rather than assembled out of flat bands. Two bands of
            // decreasing alpha left a hard vertical seam down the middle of the corridor
            // — straight through the floor, at the exact centre of the frame a store page
            // opens on.
            var scrim = UiFactory.CreateImage("Scrim", root, new Color(0.01f, 0.01f, 0.015f, 0.66f));
            var scrimRect = (RectTransform)scrim.transform;
            scrimRect.anchorMin = new Vector2(0f, 0f);
            scrimRect.anchorMax = new Vector2(0.58f, 1f);
            scrimRect.offsetMin = Vector2.zero;
            scrimRect.offsetMax = Vector2.zero;
            scrim.gameObject.AddComponent<UiGradient>().Set(1f, 0f);

            var column = UiFactory.CreateRect("Column", root);
            UiFactory.Place(column, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, 40f), new Vector2(560f, 640f));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Title", column, Font, "요양원 지하", 84, UiStyle.Ink, TextAnchor.UpperLeft).transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(560f, 96f));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Subtitle", column, Font, "선착순 미로 탈출 · 지하 8층 · 가제", UiStyle.TextSize + 6, UiStyle.InkFaint, TextAnchor.UpperLeft).transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(2f, -100f), new Vector2(560f, 28f));

            var rule = UiFactory.CreateImage("Rule", column, UiStyle.InkFaint);
            UiFactory.Place((RectTransform)rule.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -142f), new Vector2(320f, 1f));

            var y = -200f;
            AddEntry(column, "시작", "로비 — 같이 내려간다", ref y, delegate { _onStart?.Invoke(); });
            AddEntry(column, "혼자 내려가기", "순위 없음 · 연습", ref y, delegate { _onSolo?.Invoke(); });
            AddEntry(column, "설정", "시야 · 조작 · 소리 · 화면", ref y, delegate { _onSettings?.Invoke(); });
            AddEntry(column, "종료", string.Empty, ref y, delegate { _onQuit?.Invoke(); });

            _headphoneBand = UiFactory.CreateImage("HeadphoneBand", root, new Color(0.16f, 0.10f, 0.03f, 0.72f));
            UiFactory.Place((RectTransform)_headphoneBand.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(1420f, 42f));

            _headphoneText = UiFactory.CreateText(
                "Headphones", _headphoneBand.transform, Font, string.Empty, UiStyle.TextSize, UiStyle.Trade, TextAnchor.MiddleCenter);
            UiFactory.Place((RectTransform)_headphoneText.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1380f, 30f));

            var build = UiFactory.CreateText(
                "Build", root, Font, Application.version, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.LowerRight);
            UiFactory.Place((RectTransform)build.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-UiStyle.ScreenMargin, UiStyle.ScreenMargin), new Vector2(300f, 20f));

            // Leaves the screen complete the moment it is built, so a review render —
            // which never calls Open — photographs the menu a player would see rather
            // than one with a blank band across the bottom of it.
            RefreshHeadphoneNotice();
        }

        private void AddEntry(RectTransform column, string label, string note, ref float y, UnityEngine.Events.UnityAction onClick)
        {
            var button = UiFactory.CreateButton("Entry_" + label, column, new Color(0.09f, 0.09f, 0.10f, 0.85f), onClick);
            UiFactory.Place((RectTransform)button.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(ButtonWidth, ButtonHeight));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Label", button.transform, Font, label, UiStyle.TextSize + 10, UiStyle.Ink, TextAnchor.MiddleLeft).transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(240f, 34f));

            if (!string.IsNullOrEmpty(note))
            {
                UiFactory.Place(
                    (RectTransform)UiFactory.CreateText(
                        "Note", button.transform, Font, note, UiControls.NoteSize, UiStyle.InkFaint, TextAnchor.MiddleRight).transform,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(260f, 24f));
            }

            y -= ButtonHeight + ButtonGap;
        }
    }
}
