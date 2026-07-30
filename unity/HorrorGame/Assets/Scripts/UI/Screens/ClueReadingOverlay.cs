#nullable enable

using HorrorGame.Core.Clues;
using HorrorGame.UI.Readouts;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// The clue-reading overlay. §03.
    /// <para>
    /// Three things and no fourth: the beam-holding progress, the mark once it
    /// resolves, and the reason a read stopped. §03 builds the whole information
    /// economy of the game on the reader having no copy — <em>"그 자리에서 보고,
    /// 기억해서, 말로 전달해야 한다"</em> — and lists the consequences it wants:
    /// forced talking, having to stand in the dangerous room, "6이었나 9였나",
    /// revisits when the team remembered wrong.
    /// </para>
    /// <para>
    /// <b>There is no log, no history, no journal and no recap.</b> This class holds
    /// exactly one <see cref="ClueReadingView"/> — a value, not a collection — and
    /// overwrites it. No list, array, dictionary or queue appears anywhere on the
    /// path from the host's answer to this screen, and that is the property to check
    /// if anybody ever asks for "just a small reminder of the last clue": granting it
    /// requires introducing a container here, which is a visible change to a file
    /// whose only job is not to have one. The mark disappears the moment the player
    /// stops looking at it, because §03's storage is the player's memory and adding a
    /// second store deletes every consequence the section is built on — including
    /// §14's verification question 4.
    /// </para>
    /// <para>
    /// It also stores no <c>ClueReport</c>. <see cref="ShowRevealed"/> flattens the
    /// host's answer into a string on arrival, so nothing structured about the clue
    /// survives the frame. Under ARCHITECTURE §4 the host answers only for the mark
    /// this player is standing in front of; this is where that answer stops.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClueReadingOverlay : UiScreen
    {
        private ClueReadingView _view = ClueReadingView.None;

        private RectTransform? _group;
        private Text? _layerText;
        private Text? _markText;
        private Text? _statusText;
        private UiBar? _progress;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderClue; }
        }

        /// <summary>False — reading a clue is done by standing still with a light, not by clicking.</summary>
        protected override bool Interactive
        {
            get { return false; }
        }

        /// <summary>What is on screen right now. There is no second one, and no previous one.</summary>
        public ClueReadingView Current
        {
            get { return _view; }
        }

        /// <summary>
        /// Draws the progress half: how long the beam has been held, and what broke it.
        /// <para>
        /// <c>ClueReader</c> carries no clue content — it knows only how long its own
        /// player stood still in the light — so a client may run one locally and this
        /// method may be called from a client without §13 being involved.
        /// </para>
        /// </summary>
        /// <param name="reader">The local reader, or null when nothing is being read.</param>
        /// <param name="layer">Which link of §03's chain the host says the mark is, or <see cref="ClueLayer.None"/>.</param>
        public void ShowProgress(ClueReader? reader, ClueLayer layer)
        {
            Apply(ClueReadingView.Reading(reader, layer));
        }

        /// <summary>
        /// Draws the mark the host resolved for this player's finished read.
        /// <para>
        /// The report is consumed here and dropped. Reading the same clue twice can
        /// legitimately produce two different answers (§03's misread model), and this
        /// screen must not smooth that over — it has nothing to compare against, by
        /// construction.
        /// </para>
        /// </summary>
        public void ShowRevealed(ClueReport report)
        {
            Apply(ClueReadingView.Revealed(report));
        }

        /// <summary>
        /// Clears the overlay. Called when the player looks away, walks off, dies or
        /// surfaces — every one of which ends the read, and none of which leaves a
        /// record.
        /// </summary>
        public void Dismiss()
        {
            Apply(ClueReadingView.None);
        }

        /// <summary>Draws a view. The single write path; <see cref="_view"/> is replaced, never appended to.</summary>
        public void Apply(ClueReadingView view)
        {
            EnsureBuilt();
            _view = view;

            if (_group == null || _layerText == null || _markText == null || _statusText == null || _progress == null)
            {
                return;
            }

            _group.gameObject.SetActive(view.IsVisible);
            if (!view.IsVisible)
            {
                return;
            }

            _layerText.text = view.Layer == ClueLayer.None ? string.Empty : UiStrings.ClueLayerName(view.Layer);

            // The bar is the §03 gate made visible: it fills only while the player is
            // close, still and lit, and it goes to zero rather than pausing when one of
            // those stops being true.
            _progress.Visible = view.InProgress;
            _progress.SetFill(view.Progress);

            if (view.IsRevealed)
            {
                // The mark, as large as it can be drawn. §03's confusion pairs live in
                // the glyph itself, so the player is looking at "6" or "9" and not at a
                // sentence describing one.
                _markText.text = view.Legible ? view.MarkText : "…";
                _markText.color = view.Legible ? UiStyle.Ink : UiStyle.InkFaint;
                _statusText.text = view.Legible ? "기억해라 — 남지 않는다" : "읽을 수 없다";
                _statusText.color = view.Legible ? UiStyle.InkFaint : UiStyle.Spent;
                return;
            }

            _markText.text = string.Empty;

            if (view.State == ClueReadState.Interrupted)
            {
                // §03 makes darkness a lock rather than a penalty: the progress is gone,
                // not paused, and the wording says so or players will assume otherwise.
                _statusText.text = view.LightWasLost
                    ? UiStrings.Interrupt(ClueReadInterrupt.LightLost) + " — 처음부터"
                    : view.InterruptLabel;
                _statusText.color = UiStyle.Spent;
                return;
            }

            _statusText.text = "비추고 있어라";
            _statusText.color = UiStyle.InkFaint;
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            var group = UiFactory.Place(
                UiFactory.CreateRect("Clue", root),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -140f),
                new Vector2(720f, 220f));
            _group = group;

            _layerText = Label(group, "Layer", TextAnchor.UpperCenter, new Vector2(0f, 0f), UiStyle.TextSizeSmall, UiStyle.InkFaint);
            _markText = Label(group, "Mark", TextAnchor.UpperCenter, new Vector2(0f, -30f), UiStyle.TextSizeHeadline, UiStyle.Ink);
            _statusText = Label(group, "Status", TextAnchor.UpperCenter, new Vector2(0f, -116f), UiStyle.TextSizeSmall, UiStyle.InkFaint);

            var progress = UiFactory.CreateBar("Progress", group, 220f, UiStyle.BarHeight);
            UiFactory.Place(
                progress.Root,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -150f),
                new Vector2(220f, UiStyle.BarHeight));
            _progress = progress;
        }

        private Text Label(RectTransform parent, string name, TextAnchor alignment, Vector2 position, int size, Color color)
        {
            var text = UiFactory.CreateText(name, parent, Font, string.Empty, size, color, alignment);
            UiFactory.Place(
                (RectTransform)text.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                position,
                new Vector2(720f, size + 12f));
            return text;
        }
    }
}
