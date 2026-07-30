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
    /// The ghost's own dropped loot is shown, because §08 hands §09 that exact
    /// cruelty: <em>"유령이 된 본인은 자기 물건이 어디 있는지 보이는데 말할 수
    /// 없다."</em> Seeing the pile and the credits on it, with no way to point at it
    /// but a rattle every forty-five seconds, is the design working.
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
        private Text? _lootText;

        private GhostState? _ghost;
        private GhostSignalFailure _lastFailure = GhostSignalFailure.None;

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

        /// <summary>Draws one frame's readout. The client entry point.</summary>
        public void Apply(GhostReadout readout)
        {
            EnsureBuilt();

            if (_group == null || _title == null || _rattleText == null || _rattleBar == null
                || _failureText == null || _lootText == null)
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

            if (readout.SeesOwnLoot)
            {
                _lootText.text = "내 전리품 "
                    + readout.OwnLootValue.ToString(CultureInfo.InvariantCulture) + " 크레딧 · "
                    + readout.OwnLootDistance.ToString("0", CultureInfo.InvariantCulture) + "m";
                _lootText.color = UiStyle.Trade;
            }
            else
            {
                _lootText.text = string.Empty;
            }
        }

        /// <summary>Pulls from the bound ghost each frame, for host and offline play.</summary>
        private void LateUpdate()
        {
            if (_ghost == null)
            {
                return;
            }

            Apply(GhostReadout.From(_ghost, _lastFailure));
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

            _lootText = UiFactory.CreateText("Loot", group, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.Trade, TextAnchor.LowerLeft);
            UiFactory.Place((RectTransform)_lootText.transform,
                Vector2.zero, Vector2.zero, new Vector2(UiStyle.ScreenMargin, UiStyle.ScreenMargin), new Vector2(520f, 22f));
        }
    }
}
