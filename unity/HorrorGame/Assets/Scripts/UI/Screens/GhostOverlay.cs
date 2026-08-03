#nullable enable

using HorrorGame.Core.Ghost;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// What a dead player sees. §09.
    /// <para>
    /// <b>No channel, and therefore no widget for one.</b> This overlay used to be
    /// built around a cooldown bar: §09 gave the ghost a rattle every forty-five
    /// seconds, and the wait <em>was</em> the experience. §11's 탈락자 rule deleted
    /// the rattle — 「살아 있는 사람에게 개입할 수 없다」 — so the bar, its countdown,
    /// its two failure messages and the 물건 prompt under it all went with it. What
    /// replaced them is a line naming what the ghost is currently watching, because
    /// the one key a spectator has now moves the camera rather than the world.
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
        private Text? _watchText;
        private Text? _keysText;
        private Text? _monsterTitle;
        private Text? _monsterState;
        private Text? _monsterWhere;
        private Text? _monsterClock;
        private Text? _verdictText;
        private UiBar? _verdictBar;

        private GhostState? _ghost;

        private string _watchLabel = string.Empty;
        private bool _verdictWaiting;
        private float _verdictProgress01;
        private string _watchKeyLabel = "?";
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
            _watchLabel = string.Empty;
            SetVisible(true);
            Redraw();
        }

        /// <summary>Stops drawing. The match is over, or the ghost is being handed to the end screen.</summary>
        public void Unbind()
        {
            _ghost = null;
            SetVisible(false);
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
        /// <param name="watchKeyLabel">The key that cuts to the next vantage. §09's only verb.</param>
        /// <param name="endMatchKeyLabel">The key held to ask for §02's verdict, once there is one.</param>
        /// <param name="legend">The whole scheme in one faint line, for a player who has never been dead before.</param>
        public void SetKeys(string watchKeyLabel, string endMatchKeyLabel, string legend)
        {
            EnsureBuilt();

            _watchKeyLabel = string.IsNullOrEmpty(watchKeyLabel) ? "?" : watchKeyLabel;
            _endMatchKeyLabel = string.IsNullOrEmpty(endMatchKeyLabel) ? "?" : endMatchKeyLabel;

            if (_keysText != null)
            {
                _keysText.text = legend ?? string.Empty;
            }
        }

        /// <summary>
        /// Names what the ghost is watching — a creature, the finish, its own body, or
        /// nothing while it is flying free.
        /// <para>
        /// Replaces <c>SetRattleTarget</c>, which named the 물건 the ghost was standing
        /// next to and about to shake. The verb changed from touching the world to
        /// choosing where to look at it from, so the label did too.
        /// </para>
        /// </summary>
        /// <param name="label">Empty while the ghost is flying free rather than holding a shot.</param>
        public void SetWatchSubject(string label)
        {
            _watchLabel = label ?? string.Empty;
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

        /// <summary>
        /// Draws one frame. Takes the ghost itself rather than a readout struct —
        /// <c>GhostReadout</c> is deleted, because once the rattle went it carried one
        /// field (<c>IsGhost</c>) that this class can read from a null check.
        /// </summary>
        public void Apply(GhostState? ghost)
        {
            EnsureBuilt();

            if (_group == null || _title == null || _watchText == null)
            {
                return;
            }

            _group.gameObject.SetActive(ghost != null);
            if (ghost == null)
            {
                return;
            }

            _title.text = "유령";

            // §09's whole surface now: 순위 없음, and no way to say anything about it.
            // The second line is where the camera is, not what the ghost can do.
            if (string.IsNullOrEmpty(_watchLabel))
            {
                _watchText.text = "[" + _watchKeyLabel + "] 다른 곳에서 본다";
                _watchText.color = UiStyle.InkFaint;
            }
            else
            {
                _watchText.text = _watchLabel + "  ·  [" + _watchKeyLabel + "] 다음";
                _watchText.color = UiStyle.InkStrong;
            }

            DrawVerdict();
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

            Apply(_ghost);
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

            // Where the RattleBar and its countdown, failure line and target prompt used
            // to sit — four widgets for one verb. One line replaces all four, because
            // the verb no longer has a cooldown, a range or a way to fail.
            _watchText = UiFactory.CreateText("Watch", group, Font, string.Empty, UiStyle.TextSize, UiStyle.InkFaint, TextAnchor.LowerCenter);
            UiFactory.Place((RectTransform)_watchText.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiStyle.ScreenMargin + UiStyle.LineGap), new Vector2(640f, 28f));

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
            // measured at 1280 × 720.
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
