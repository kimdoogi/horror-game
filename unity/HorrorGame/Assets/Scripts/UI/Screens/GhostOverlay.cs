#nullable enable

using System.Globalization;
using HorrorGame.Core.Ghost;
using HorrorGame.UI.Readouts;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// What a dead player sees. §09.
    /// <para>
    /// <b>One channel, and therefore one widget.</b> §09 gives the ghost a rattle
    /// every forty-five seconds and nothing else, and the section's "최고의 순간" is
    /// the moment the living walk the wrong way while the ghost knows better and
    /// cannot say so. The cooldown is the largest thing on this overlay because the
    /// wait <em>is</em> the experience — <em>"쿨타임 45초 안에 다시 시도할 수 없다 …
    /// (유령의 절규)"</em>.
    /// </para>
    /// <para>
    /// <b>There is no voice element here, of any kind.</b> No microphone icon, no
    /// muted indicator, no greyed-out push-to-talk hint, no "you cannot speak"
    /// banner. Each of those describes a control that exists in some other state and
    /// teaches the player to go looking for it. §09's silence is not a setting that
    /// happens to be off: it is the reason a death cannot break the match
    /// ("죽은 사람이 정보를 주면 밸런스 붕괴"), and it is held in Core by
    /// <c>GhostState</c> having no method that accepts a message, a target or a
    /// payload. This screen holds it the same way — by containing no such widget —
    /// and §13 settles the argument for anyone who would rather mute a live channel:
    /// anything cut at the receiver is not cut at all.
    /// </para>
    /// <para>
    /// <b>The 전리품 line is gone.</b> This overlay used to draw the pile the dead
    /// player had dropped and what it was worth in credits, because §08 handed §09 that
    /// exact cruelty — <em>"유령이 된 본인은 자기 물건이 어디 있는지 보이는데 말할 수
    /// 없다."</em> DESCENT-PIVOT §7 step 7 deleted the loot and the credits, so there is
    /// no pile and no price, and the widget went with them. A race elimination is 탈락
    /// — you are out and unranked — not a player standing over property they lost.
    /// </para>
    /// <para>
    /// <b>§06's state is on here too, and it is the second half of "볼 게 있고 할 게
    /// 있다".</b> A ghost that can follow the monster through five storeys and read what
    /// it is doing is the answer to 죽으면 지루하다, and it costs the design nothing:
    /// everything on this overlay is information the player cannot pass on. It is also
    /// the only place in a <em>build</em> where §06's machine is legible —
    /// <c>MonsterDebugView</c> draws the same numbers with editor gizmos, on a selected
    /// object, which cannot answer §14's 「추격이 재밌는가?」 for anybody holding a
    /// controller. The strings arrive already formatted from the gameplay layer, because
    /// this assembly does not reference the monster's and should not start.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostOverlay : UiScreen
    {
        private RectTransform? _group;
        private Text? _title;
        private Text? _rattleText;
        private UiBar? _rattleBar;
        private Text? _failureText;
        private Text? _targetText;
        private Text? _keysText;
        private Text? _monsterTitle;
        private Text? _monsterState;
        private Text? _monsterWhere;
        private Text? _monsterClock;
        private Text? _verdictText;
        private UiBar? _verdictBar;

        private GhostState? _ghost;
        private GhostSignalFailure _lastFailure = GhostSignalFailure.None;

        private string _targetLabel = string.Empty;
        private float _targetDistance;
        private bool _verdictWaiting;
        private float _verdictProgress01;
        private string _rattleKeyLabel = "?";
        private string _endMatchKeyLabel = "?";

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderHud; }
        }

        /// <summary>False. A ghost's overlay is watched, not operated — the one verb is a key, not a button.</summary>
        protected override bool Interactive
        {
            get { return false; }
        }

        /// <summary>Whether the local player is currently dead.</summary>
        public bool IsActive
        {
            get { return _ghost != null; }
        }

        /// <summary>Takes over when the local player dies. §09 begins here and does not end until the match does — the 탈출 row is 불가능.</summary>
        public void Bind(GhostState ghost)
        {
            _ghost = ghost;
            _lastFailure = GhostSignalFailure.None;
            SetVisible(true);
            Apply(GhostReadout.From(ghost, _lastFailure));
        }

        /// <summary>Stops drawing. The match is over, or the ghost is being handed to the end screen.</summary>
        public void Unbind()
        {
            _ghost = null;
            SetVisible(false);
        }

        /// <summary>
        /// Records the outcome of a rattle attempt so the overlay can say which kind of
        /// failure it was. §09 keeps the two apart because only one of them — 너무
        /// 멀다 — is worth drifting somewhere to fix.
        /// </summary>
        public void NoteRattle(GhostRattle rattle)
        {
            _lastFailure = rattle.Failure;
        }

        /// <summary>
        /// Takes the bindings from whoever owns them.
        /// <para>
        /// The gameplay layer reads the keyboard, so the gameplay layer knows which keys
        /// these are; a second copy written here would be a prompt that keeps telling the
        /// player to press a key that has moved. <c>PlayerInteractor.InteractKeyLabel</c>
        /// exists for exactly the same reason and says so.
        /// </para>
        /// </summary>
        /// <param name="rattleKeyLabel">The key that shakes something. §09's only verb.</param>
        /// <param name="endMatchKeyLabel">The key held to ask for §02's verdict, once there is one.</param>
        /// <param name="legend">The whole scheme in one faint line, for a player who has never been dead before.</param>
        public void SetKeys(string rattleKeyLabel, string endMatchKeyLabel, string legend)
        {
            EnsureBuilt();

            _rattleKeyLabel = string.IsNullOrEmpty(rattleKeyLabel) ? "?" : rattleKeyLabel;
            _endMatchKeyLabel = string.IsNullOrEmpty(endMatchKeyLabel) ? "?" : endMatchKeyLabel;

            if (_keysText != null)
            {
                _keysText.text = legend ?? string.Empty;
            }
        }

        /// <summary>
        /// Names the 물건 the ghost is beside. §09 gives it one verb and it should know
        /// what that verb is about to touch — 「[E] 은수저 · 1.2m」 rather than a key
        /// prompt over nothing.
        /// </summary>
        /// <param name="label">What the object is called, or empty when there is none in range.</param>
        /// <param name="distanceMetres">Straight-line metres. Straight because a ghost passes through walls.</param>
        public void SetRattleTarget(string label, float distanceMetres)
        {
            _targetLabel = label ?? string.Empty;
            _targetDistance = distanceMetres;
        }

        /// <summary>
        /// §06's machine, in words. Pushed from the gameplay layer so this assembly never
        /// learns what a monster agent is; see the class remarks for why the dead are
        /// shown it at all.
        /// </summary>
        /// <param name="known">False when there is no monster in the scene — a harness, or a match not yet begun.</param>
        /// <param name="state">§06's 상태 column: 순찰 · 경계 · 추격 · 수색 · 정지, plus 침묵 when it is making no sound.</param>
        /// <param name="where">Distance and bearing from the ghost.</param>
        /// <param name="clock">§07's tier and the speed it grants.</param>
        public void SetMonsterWatch(bool known, string state, string where, string clock)
        {
            if (_monsterTitle == null || _monsterState == null || _monsterWhere == null || _monsterClock == null)
            {
                return;
            }

            _monsterTitle.gameObject.SetActive(known);
            _monsterState.gameObject.SetActive(known);
            _monsterWhere.gameObject.SetActive(known);
            _monsterClock.gameObject.SetActive(known);

            if (!known)
            {
                return;
            }

            _monsterState.text = state ?? string.Empty;
            _monsterWhere.text = where ?? string.Empty;
            _monsterClock.text = clock ?? string.Empty;
        }

        /// <summary>
        /// Offers §02's verdict once it has been reached, instead of showing it.
        /// <para>
        /// §09 keeps a dead player in the match — 탈출: 불가능 — so the moment §02 finishes
        /// counting is not a moment to take the building away from them. The host holds
        /// the end screen; this line is how the ghost learns it is being held, and the bar
        /// is the hold that asks for it.
        /// </para>
        /// </summary>
        public void SetVerdictWait(bool waiting, float progress01)
        {
            _verdictWaiting = waiting;
            _verdictProgress01 = progress01;
        }

        /// <summary>Draws one frame's readout. The client entry point.</summary>
        public void Apply(GhostReadout readout)
        {
            EnsureBuilt();

            if (_group == null || _title == null || _rattleText == null || _rattleBar == null
                || _failureText == null)
            {
                return;
            }

            _group.gameObject.SetActive(readout.IsGhost);
            if (!readout.IsGhost)
            {
                return;
            }

            _title.text = "유령";

            // §09's 신호 row. The bar fills toward the next attempt rather than draining
            // from the last one, because what the player is waiting for is the moment it
            // becomes possible again.
            _rattleBar.SetFill(readout.RattleCharge01);

            if (readout.CanRattle)
            {
                _rattleText.text = "신호 — 근처 물건을 흔들 수 있다";
                _rattleText.color = UiStyle.Trade;
            }
            else
            {
                _rattleText.text = "신호 " + readout.CooldownSecondsLeft.ToString(CultureInfo.InvariantCulture) + "초";
                _rattleText.color = UiStyle.InkFaint;
            }

            _failureText.text = readout.FailureLabel;

            DrawTarget(readout.CanRattle);
            DrawVerdict();
        }

        /// <summary>
        /// The 물건 under the verb. Empty when there is nothing in range, because §09
        /// splits 아직 흔들 수 없다 from 너무 멀다 and a prompt over nothing would blur
        /// the two back together.
        /// </summary>
        private void DrawTarget(bool armed)
        {
            if (_targetText == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(_targetLabel))
            {
                _targetText.text = string.Empty;
                return;
            }

            _targetText.text = "[" + _rattleKeyLabel + "] " + _targetLabel + " · "
                + _targetDistance.ToString("0.0", CultureInfo.InvariantCulture) + "m";
            _targetText.color = armed ? UiStyle.InkStrong : UiStyle.InkFaint;
        }

        private void DrawVerdict()
        {
            if (_verdictText == null || _verdictBar == null)
            {
                return;
            }

            _verdictText.gameObject.SetActive(_verdictWaiting);
            _verdictBar.Root.gameObject.SetActive(_verdictWaiting);

            if (!_verdictWaiting)
            {
                return;
            }

            _verdictText.text = "판은 끝났다 — [" + _endMatchKeyLabel + "]를 누르고 있으면 결과를 본다";
            _verdictBar.SetFill(_verdictProgress01);
        }

        /// <summary>
        /// Draws the bound ghost's current state. What <see cref="LateUpdate"/> does —
        /// public because <c>LateUpdate</c> does not fire outside play mode, and a capture
        /// rig photographing §09 would otherwise photograph an overlay that has never
        /// drawn. Does nothing while nobody is dead.
        /// </summary>
        public void Redraw()
        {
            if (_ghost == null)
            {
                return;
            }

            Apply(GhostReadout.From(_ghost, _lastFailure));
        }

        /// <summary>Pulls from the bound ghost each frame, for host and offline play.</summary>
        private void LateUpdate()
        {
            Redraw();
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            var group = UiFactory.Stretch(UiFactory.CreateRect("Ghost", root));
            _group = group;

            _title = UiFactory.CreateText("Title", group, Font, string.Empty, UiStyle.TextSize, UiStyle.InkFaint, TextAnchor.UpperCenter);
            UiFactory.Place((RectTransform)_title.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -UiStyle.ScreenMargin), new Vector2(400f, 28f));

            var bar = UiFactory.CreateBar("RattleBar", group, 280f, UiStyle.BarHeight);
            UiFactory.Place(bar.Root,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, UiStyle.ScreenMargin), new Vector2(280f, UiStyle.BarHeight));
            _rattleBar = bar;

            _rattleText = UiFactory.CreateText("RattleText", group, Font, string.Empty, UiStyle.TextSize, UiStyle.InkFaint, TextAnchor.LowerCenter);
            UiFactory.Place((RectTransform)_rattleText.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, UiStyle.ScreenMargin + UiStyle.LineGap), new Vector2(640f, 28f));

            _failureText = UiFactory.CreateText("Failure", group, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.Spent, TextAnchor.LowerCenter);
            UiFactory.Place((RectTransform)_failureText.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, UiStyle.ScreenMargin + (UiStyle.LineGap * 2f)), new Vector2(640f, 22f));

            // Directly over the cooldown bar: the object and the wait are one decision —
            // "can I shake this, and may I yet" — and reading them in two corners is how
            // a ghost misses its window.
            _targetText = UiFactory.CreateText("Target", group, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.LowerCenter);
            UiFactory.Place((RectTransform)_targetText.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiStyle.ScreenMargin + (UiStyle.LineGap * 3f)), new Vector2(640f, 22f));

            BuildMonsterWatch(group);
            BuildVerdictWait(group);

            _keysText = UiFactory.CreateText("Keys", group, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.LowerRight);
            UiFactory.Place((RectTransform)_keysText.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-UiStyle.ScreenMargin, UiStyle.ScreenMargin), new Vector2(720f, 22f));
        }

        /// <summary>
        /// §06's block, top right and away from the cooldown. Three lines because §06 has
        /// three questions a watcher asks in order — what is it doing, where is it, and
        /// how fast is it allowed to be (§07).
        /// </summary>
        private void BuildMonsterWatch(RectTransform group)
        {
            // Twice the usual margin. 추격 is drawn at title size and right-aligned, and at
            // the standard inset the glyph box ran within a few pixels of the frame edge —
            // measured on ghost_25_just_rattled at 1280 × 720.
            const float WatchMargin = UiStyle.ScreenMargin * 2f;

            _monsterTitle = UiFactory.CreateText("MonsterTitle", group, Font, "괴물", UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.UpperRight);
            UiFactory.Place((RectTransform)_monsterTitle.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-WatchMargin, -UiStyle.ScreenMargin), new Vector2(420f, 20f));

            _monsterState = UiFactory.CreateText("MonsterState", group, Font, string.Empty, UiStyle.TextSizeTitle, UiStyle.InkStrong, TextAnchor.UpperRight);
            UiFactory.Place((RectTransform)_monsterState.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-WatchMargin, -UiStyle.ScreenMargin - UiStyle.LineGap), new Vector2(420f, 44f));

            _monsterWhere = UiFactory.CreateText("MonsterWhere", group, Font, string.Empty, UiStyle.TextSize, UiStyle.InkQuiet, TextAnchor.UpperRight);
            UiFactory.Place((RectTransform)_monsterWhere.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-WatchMargin, -UiStyle.ScreenMargin - (UiStyle.LineGap * 3f)), new Vector2(420f, 26f));

            _monsterClock = UiFactory.CreateText("MonsterClock", group, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.UpperRight);
            UiFactory.Place((RectTransform)_monsterClock.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-WatchMargin, -UiStyle.ScreenMargin - (UiStyle.LineGap * 4.2f)), new Vector2(420f, 22f));
        }

        /// <summary>
        /// §02's offer, in the lower third rather than across the middle.
        /// <para>
        /// Photographed at the centre it sat straight over the vanishing point of the
        /// corridor the ghost was looking down, with §06's monster behind it. A player
        /// who is being asked whether they would like the match to stop is still watching
        /// the match, so the question goes under what they are watching.
        /// </para>
        /// </summary>
        private void BuildVerdictWait(RectTransform group)
        {
            var height = UiStyle.ScreenMargin + (UiStyle.LineGap * 6f);

            _verdictText = UiFactory.CreateText("Verdict", group, Font, string.Empty, UiStyle.TextSize, UiStyle.InkStrong, TextAnchor.LowerCenter);
            UiFactory.Place((RectTransform)_verdictText.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, height + UiStyle.LineGap), new Vector2(900f, 28f));

            var verdict = UiFactory.CreateBar("VerdictBar", group, 280f, UiStyle.BarHeight);
            UiFactory.Place(verdict.Root,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, height), new Vector2(280f, UiStyle.BarHeight));
            _verdictBar = verdict;
        }
    }
}
