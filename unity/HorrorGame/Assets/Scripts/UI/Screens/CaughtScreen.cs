#nullable enable

using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// The half-second the game spends telling a runner they have been sent home.
    /// §01 · §02 · §03 · §06.
    /// <para>
    /// <b>What was wrong.</b> Being caught is the second most important thing that can
    /// happen in this game and it was silent and instant. <c>RaceState.ReportCaught</c>
    /// zeroes the storey and <c>MatchDirector.SendBackToTheStartLine</c> writes the
    /// transform, both inside one fixed step, so the entire experience was: the creature's
    /// grab clip plays — and that clip plays on a lunge that MISSES too — and then the
    /// corridor is a different corridor. A player who cannot name what happened to them
    /// reads it as a bug, and this project has had exactly that reported before.
    /// </para>
    /// <para>
    /// <b>Half a second, split 0.10 / 0.10 / 0.30.</b> This is a race and every frame of a
    /// cutscene is a frame somebody else is running. The budget is affordable only because
    /// of one fact: <em>the runner is not frozen.</em> Being sent home is a position write,
    /// not a sequence — by the time this curtain's first frame draws they are already
    /// standing on B1's rim and their keys already work. So the curtain costs vision, not
    /// time, and it is sized to the smallest amount of vision that can carry a cut:
    /// 0.10 s down (about six frames at 60 Hz — fast enough to read as a splice rather
    /// than as a dip), 0.10 s of held black, 0.30 s back. Blind for 0.20 s of it.
    /// </para>
    /// <para>
    /// <b>Why a curtain at all, when the game is already nearly black.</b> §03 spends its
    /// whole section arguing the inner rings are unlit, so fading a B6 corridor to black
    /// communicates almost nothing — black on black is not an event. The curtain is not
    /// the message. The <em>message</em> is that §01 makes B1's rim bright — 「출발 …
    /// 밝고, 넓고, 안전하다」 — so what a caught runner actually experiences is an unlit
    /// inner ring becoming a lit outer one, in one frame, with no travel between them.
    /// Uncovered that is a pop, which is what a bug looks like. Covered, it is a cut,
    /// which is what an edit looks like. The curtain buys the cut; the two lines of text
    /// name it.
    /// </para>
    /// <para>
    /// <b>The words are two lines and they are short on purpose.</b> They live and die
    /// with the black — text alpha tracks curtain alpha, so the one saturated thing this
    /// class draws exists only while the corridor is hidden and is gone before the
    /// corridor is back. That leaves well under half a second to read, so the headline is
    /// one word and the second line is a glyph pattern rather than a sentence: <c>B6 →
    /// B1</c> is read, not parsed. Everything that deserves a longer look — how many times
    /// this has happened, where everyone else is — is on <see cref="RaceHud"/>, which is
    /// still there when the curtain lifts.
    /// </para>
    /// <para>
    /// <b>It never takes input.</b> <see cref="Interactive"/> is false and nothing here
    /// touches <c>timeScale</c>. A curtain that ate the movement keys would turn a
    /// presentation into a second punishment on top of the eight storeys, and §02 already
    /// charges enough.
    /// </para>
    /// <para>
    /// <b>The 투하구 should use this too, and today does not.</b> A drop through a chute is
    /// the same sentence — <em>you have been moved, you did not walk here</em> — and
    /// <c>MatchDirector.CheckChutes</c> currently teleports with no cover at all. Sharing
    /// one curtain is what would make this a grammar rather than a one-off; see
    /// <see cref="PlayDrop"/>, which exists for that call site.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaughtScreen : UiScreen
    {
        /// <summary>
        /// Seconds to full black. Six frames at 60 Hz: long enough not to strobe, short
        /// enough that the eye files it as a cut rather than as a fade.
        /// </summary>
        public const float FallSeconds = 0.10f;

        /// <summary>
        /// Seconds held at full black. This is the whole of the concealment — the frame
        /// the teleport is hidden in — and it is the only part of the budget the player is
        /// genuinely blind for beyond the fall.
        /// </summary>
        public const float HoldSeconds = 0.10f;

        /// <summary>
        /// Seconds back to clear. Three times the fall, because a lift that matched the
        /// cut would read as a second cut in the other direction, and the runner is
        /// already moving through a corridor they need to see.
        /// </summary>
        public const float LiftSeconds = 0.30f;

        /// <summary>
        /// The whole curtain, 0.50 s. Quoted by <c>tools/audio/gen_caught.py</c>, which
        /// asserts the sting delivers its energy inside <see cref="BlindSeconds"/> — so
        /// retiming this without retiming the sound fails there rather than in review.
        /// </summary>
        public const float TotalSeconds = FallSeconds + HoldSeconds + LiftSeconds;

        /// <summary>When the picture starts coming back. 0.20 s.</summary>
        public const float BlindSeconds = FallSeconds + HoldSeconds;

        /// <summary>
        /// Vertical offset of the headline from centre. Above the middle rather than on
        /// it: the crosshair-less centre of the frame is where the corridor reappears, and
        /// a word sitting exactly there is the last thing the eye lets go of.
        /// </summary>
        private const float HeadlineY = 16f;

        /// <summary>Where the storey line sits, under the headline.</summary>
        private const float DetailY = -22f;

        private Image? _curtain;
        private Text? _headline;
        private Text? _detail;

        /// <summary>
        /// The headline's colour before the curtain's alpha is applied to it. Held as a
        /// field because <see cref="Draw"/> rewrites the colour every frame: a catch is
        /// <c>Spent</c> and a 투하구 drop is <c>Ink</c>, and hard-coding either in the draw
        /// would silently repaint the other one.
        /// </summary>
        private Color _headlineBase = UiStyle.Spent;

        private float _elapsed = TotalSeconds;
        private bool _running;

        /// <inheritdoc />
        /// <remarks>
        /// One above the HUD, so the curtain covers the standings it is in the middle of
        /// changing — a board that visibly re-sorts behind a blackout is the pop this
        /// class exists to hide. Still well under <c>SortOrderPanel</c>: a pause menu
        /// opened during the half second must not end up behind black.
        /// </remarks>
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderHud + 1; }
        }

        /// <summary>
        /// False. See the class remarks — the runner is already back on the rim and
        /// running, and a raycaster over the whole screen would eat the click that shuts
        /// a door.
        /// </summary>
        protected override bool Interactive
        {
            get { return false; }
        }

        /// <summary>True while the curtain is on screen.</summary>
        public bool IsPlaying
        {
            get { return _running; }
        }

        /// <summary>
        /// How far through the curtain the last frame drew, 0~1. Exposed so a test can
        /// assert the shape rather than photograph it.
        /// </summary>
        public float Progress01
        {
            get { return Mathf.Clamp01(_elapsed / TotalSeconds); }
        }

        /// <summary>The curtain's alpha as last drawn. 0 when nothing is playing.</summary>
        public float Alpha
        {
            get { return _curtain != null && _running ? _curtain.color.a : 0f; }
        }

        /// <summary>
        /// Runs the curtain for a runner §06 has just sent back to B1.
        /// <para>
        /// Re-entrant on purpose: a second catch during the half second restarts the
        /// curtain rather than being dropped. Two catches that close together is a runner
        /// who was returned into a creature's patrol, which is a thing worth seeing twice.
        /// </para>
        /// </summary>
        /// <param name="fromStorey">
        /// The storey they were caught on, 0-based. Read <em>before</em>
        /// <c>RaceState.ReportCaught</c> runs, because that call sets it to 0 — the whole
        /// point of the line is the storey that was lost.
        /// </param>
        /// <param name="timesCaught">
        /// How many times this has now happened, including this one. Drawn only from the
        /// second onward: the first time needs no ordinal and a "1" beside the word would
        /// read as a score.
        /// </param>
        public void PlayCaught(int fromStorey, int timesCaught)
        {
            EnsureBuilt();

            _headlineBase = UiStyle.Spent;
            if (_headline != null)
            {
                _headline.text = "잡혔다";
            }

            if (_detail != null)
            {
                // Said as a distance, not as a fact about a storey. "B1" alone is where
                // they are; "B6 → B1" is what it cost, and the cost is the sentence §02
                // is making — 「모든 것을 잃지만 판은 잃지 않는다」.
                var line = StoreyName(fromStorey) + " → " + StoreyName(0);
                if (timesCaught > 1)
                {
                    line += "   " + timesCaught.ToString(CultureInfo.InvariantCulture) + "회";
                }

                _detail.text = line;
            }

            Begin();
        }

        /// <summary>
        /// Runs the same curtain for §01's 투하구, with the storeys the other way round.
        /// <para>
        /// Nothing calls this yet — <c>MatchDirector.CheckChutes</c> teleports uncovered.
        /// It is here because the drop and the catch are the same sentence with the sign
        /// flipped, and one curtain used for both is what makes the half second a language
        /// instead of a special case. The word is different because the fact is: a drop is
        /// something the runner did.
        /// </para>
        /// </summary>
        /// <param name="toStorey">The storey they landed on, 0-based.</param>
        public void PlayDrop(int toStorey)
        {
            EnsureBuilt();

            _headlineBase = UiStyle.Ink;
            if (_headline != null)
            {
                _headline.text = StoreyName(toStorey);
            }

            if (_detail != null)
            {
                _detail.text = "떨어졌다";
            }

            Begin();
        }

        /// <summary>Takes the curtain down at once. For a match ending mid-fade.</summary>
        public void Clear()
        {
            _running = false;
            _elapsed = TotalSeconds;
            SetVisible(false);
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            // Pure black, not the palette's near-black. A curtain is the absence of an
            // image; a tinted one at full alpha is a colour the player can see, and a
            // colour reads as an effect rather than as a splice.
            _curtain = UiFactory.CreateImage("Curtain", root, new Color(0f, 0f, 0f, 0f));
            UiFactory.Stretch((RectTransform)_curtain.transform);

            _headline = UiFactory.CreateText(
                "CaughtHeadline", root, Font, string.Empty,
                UiStyle.TextSizeTitle, UiStyle.Spent, TextAnchor.MiddleCenter);
            UiFactory.Place(
                (RectTransform)_headline.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, HeadlineY),
                new Vector2(720f, UiStyle.TextSizeTitle + 10f));

            _detail = UiFactory.CreateText(
                "CaughtDetail", root, Font, string.Empty,
                UiStyle.TextSize, UiStyle.InkStrong, TextAnchor.MiddleCenter);
            UiFactory.Place(
                (RectTransform)_detail.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, DetailY),
                new Vector2(720f, UiStyle.TextSize + 8f));
        }

        private void Begin()
        {
            _elapsed = 0f;
            _running = true;
            SetVisible(true);
            Draw(0f);
        }

        /// <summary>
        /// Advances the curtain on <em>unscaled</em> time.
        /// <para>
        /// Unscaled for two reasons and both are load-bearing. A pause opened during the
        /// half second must not park a black frame over the menu until the player unpauses
        /// it. And <c>gen_caught.py</c> asserts the sting lands inside
        /// <see cref="BlindSeconds"/>: audio does not know about <c>timeScale</c>, so a
        /// curtain that did would drift out of sync with its own sound.
        /// </para>
        /// <para>
        /// <c>LateUpdate</c> so the alpha written this frame is the one that renders,
        /// matching <see cref="RaceHud"/>'s cadence rather than racing it.
        /// </para>
        /// </summary>
        private void LateUpdate()
        {
            if (!_running)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;

            if (_elapsed >= TotalSeconds)
            {
                Clear();
                return;
            }

            Draw(_elapsed);
        }

        /// <summary>
        /// The curve, as one function of time so a test can assert it without a frame.
        /// <para>
        /// The fall is linear because six frames of easing is not a shape anybody sees;
        /// the lift is smoothstepped because thirty frames of linear alpha over a nearly
        /// black corridor bands visibly, which is the same artefact <c>UiGradient</c>
        /// records on the menu backdrop.
        /// </para>
        /// </summary>
        /// <param name="seconds">Seconds since the curtain began.</param>
        /// <returns>Curtain alpha, 0~1.</returns>
        public static float AlphaAt(float seconds)
        {
            if (seconds <= 0f)
            {
                return 0f;
            }

            if (seconds < FallSeconds)
            {
                return seconds / FallSeconds;
            }

            if (seconds < BlindSeconds)
            {
                return 1f;
            }

            var u = Mathf.Clamp01((seconds - BlindSeconds) / LiftSeconds);
            return 1f - Mathf.SmoothStep(0f, 1f, u);
        }

        private void Draw(float seconds)
        {
            var alpha = AlphaAt(seconds);

            if (_curtain != null)
            {
                var c = _curtain.color;
                _curtain.color = new Color(c.r, c.g, c.b, alpha);
            }

            // The words fade with the black rather than over the corridor. §03 does not
            // let this layer put a saturated glyph on a live frame, and the curtain is the
            // only reason one is allowed at all — so when the curtain goes, it goes.
            Tint(_headline, _headlineBase, alpha);
            Tint(_detail, UiStyle.InkStrong, alpha);
        }

        private static void Tint(Text? text, Color baseColor, float alpha)
        {
            if (text == null)
            {
                return;
            }

            text.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
        }

        /// <summary>
        /// §01's name for a storey. Index 0 is B1 — the same one-ahead rule
        /// <see cref="RaceHud"/> uses, kept identical so two screens never disagree about
        /// which floor a runner is on.
        /// </summary>
        private static string StoreyName(int storey)
        {
            return "B" + (storey + 1).ToString(CultureInfo.InvariantCulture);
        }
    }
}
