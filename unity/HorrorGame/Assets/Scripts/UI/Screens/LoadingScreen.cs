#nullable enable

using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// What is on screen between the menu and the match.
    /// <para>
    /// <b>It exists because the alternative is a frozen window.</b> The map scene is a
    /// three-storey building with 932 kit pieces, a baked NavMesh and 166 audio clips
    /// behind it, and a synchronous <c>LoadScene</c> stops presenting frames until all
    /// of that is resident. An operating system draws "not responding" over a window
    /// that has stopped pumping, which on a first launch is indistinguishable from a
    /// crash — and this is a game whose store page will live or die on refund rates in
    /// the first ten minutes.
    /// </para>
    /// <para>
    /// <b>The progress bar is honest about being two-thirds of a bar.</b>
    /// <c>AsyncOperation.progress</c> stops at 0.9 and holds there until activation is
    /// allowed, so the fill is rescaled rather than shown raw: a bar that sticks at 90 %
    /// every single time teaches players to distrust it.
    /// </para>
    /// <para>
    /// The lines it shows are the game telling the player what it is about to ask of
    /// them. They come from the design's own sections rather than from a tips file,
    /// because everything here is load-bearing: a player who does not know that
    /// backwards is 65 % of forwards will find out by being caught.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoadingScreen : UiScreen
    {
        /// <summary>
        /// Where <c>AsyncOperation.progress</c> stops when activation is deferred.
        /// Not a tuned game value — a documented constant of the engine's loader.
        /// </summary>
        private const float ActivationHoldPoint = 0.9f;

        private static readonly string[] Lines =
        {
            "§05 — 뒷걸음은 전진의 65 %다. 거리를 확인하려고 뒤를 보면 괴물이 좁혀 온다.",
            "§06 — 괴물은 4.8 m/s, 달리기는 4.5 m/s. 주자만 도망칠 수 있다.",
            "§06 — 어그로 해제는 거리가 아니라 맵이다. 모퉁이를 돌고, 문을 닫고, 시야를 3초 끊어야 한다.",
            "§03 — 단서는 가지고 나올 수 없다. 그 자리에서 보고, 기억해서, 말로 전달한다.",
            "§03 — 배터리가 떨어지면 단서를 읽을 수 없다. 어둠이 잠금장치다.",
            "§07 — 나가도 시간은 흐른다. 나가는 것은 숨 돌리기이지 리셋이 아니다.",
            "§02 — 한 명이라도 살아 나가면 그 판의 정보는 남는다. 전멸하면 전부 사라진다.",
        };

        private UiBar? _bar;
        private Text? _percent;
        private Text? _line;
        private Text? _title;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderEnd + 10; }
        }

        /// <summary>False — nothing on this screen can be pressed, and a raycaster would eat the first click of the match.</summary>
        protected override bool Interactive
        {
            get { return false; }
        }

        /// <summary>Shows the screen and picks the line this load will carry.</summary>
        /// <param name="title">What is being loaded, in the player's words.</param>
        public void Open(string title)
        {
            SetVisible(true);

            if (_title != null)
            {
                _title.text = title;
            }

            if (_line != null)
            {
                _line.text = Lines[Random.Range(0, Lines.Length)];
            }

            SetProgress(0f);
        }

        /// <summary>Hides the screen.</summary>
        public void Close()
        {
            SetVisible(false);
        }

        /// <summary>
        /// Draws a raw <c>AsyncOperation.progress</c>, rescaled past the activation hold.
        /// </summary>
        public void SetLoadProgress(float rawProgress)
        {
            SetProgress(Mathf.Clamp01(rawProgress / ActivationHoldPoint));
        }

        /// <summary>Draws an already-normalised 0..1 fraction.</summary>
        public void SetProgress(float fraction01)
        {
            var value = Mathf.Clamp01(fraction01);
            _bar?.SetFill(value, UiStyle.Calm);

            if (_percent != null)
            {
                _percent.text = Mathf.RoundToInt(value * 100f).ToString(CultureInfo.InvariantCulture) + " %";
            }
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            var backing = UiFactory.CreateImage("Backing", root, new Color(0.008f, 0.008f, 0.012f, 1f));
            UiFactory.Stretch((RectTransform)backing.transform);

            _title = UiFactory.CreateText("Title", root, Font, "내려가는 중", UiStyle.TextSizeTitle, UiStyle.Ink, TextAnchor.LowerLeft);
            UiFactory.Place((RectTransform)_title.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(120f, 190f), new Vector2(900f, 46f));

            _line = UiFactory.CreateText("Line", root, Font, string.Empty, UiStyle.TextSize + 2, UiStyle.InkFaint, TextAnchor.LowerLeft);
            UiFactory.Place((RectTransform)_line.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(122f, 156f), new Vector2(1400f, 26f));

            _bar = UiFactory.CreateBar("Progress", root, 1680f, 4f);
            UiFactory.Place(_bar.Root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(120f, 118f), new Vector2(1680f, 4f));

            _percent = UiFactory.CreateText("Percent", root, Font, "0 %", UiControls.NoteSize, UiStyle.InkFaint, TextAnchor.LowerRight);
            UiFactory.Place((RectTransform)_percent.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-120f, 128f), new Vector2(200f, 22f));

            // Complete on build, so a review render photographs a loading screen rather
            // than an empty black frame with a rule on it.
            _line.text = Lines[0];
            SetProgress(0.42f);
        }
    }
}
