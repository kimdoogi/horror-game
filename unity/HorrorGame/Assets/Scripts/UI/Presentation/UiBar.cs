#nullable enable

using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI
{
    /// <summary>
    /// A one-dimensional meter: a bed, a fill, and nothing else.
    /// <para>
    /// Every quantity the HUD shows is a fraction of something the player is
    /// spending — a cell of battery (§03), a Runner's twelve seconds (§06), a
    /// ghost's forty-five (§09). They are drawn by the same widget on purpose: a
    /// player learns "the bar shrinking is a thing running out" once, and §01's
    /// promise that the four players share the same senses is easier to keep when
    /// the interface has one vocabulary rather than five.
    /// </para>
    /// <para>
    /// Not a <c>MonoBehaviour</c>. It is a handle onto two <c>Image</c>s built at
    /// runtime, so it costs no component slot, no inspector wiring and no prefab —
    /// which matters because the scenes this HUD lives in are generated (see the
    /// Editor layer) rather than authored.
    /// </para>
    /// </summary>
    public sealed class UiBar
    {
        private readonly RectTransform _root;
        private readonly Image _fill;
        private readonly RectTransform _fillRect;
        private readonly Image? _bed;

        /// <summary>Wraps an already-built bed and fill. Produced by <see cref="UiFactory.CreateBar"/>.</summary>
        /// <param name="root">The bar's own transform.</param>
        /// <param name="fill">The filled portion's image, which carries the colour.</param>
        /// <param name="fillRect">
        /// The filled portion's transform, which carries the fraction. See
        /// <see cref="UiFactory.CreateBar"/> for why the fraction is a right-hand anchor
        /// rather than <c>Image.fillAmount</c>.
        /// </param>
        /// <param name="bed">The empty portion, for callers that need it visible. Optional — the HUD does not.</param>
        public UiBar(RectTransform root, Image fill, RectTransform fillRect, Image? bed = null)
        {
            _root = root;
            _fill = fill;
            _fillRect = fillRect;
            _bed = bed;
        }

        /// <summary>The bar's transform, for callers that lay it out.</summary>
        public RectTransform Root
        {
            get { return _root; }
        }

        /// <summary>Whether the bar is drawn at all. §01's HUD hides what it has nothing to say about.</summary>
        public bool Visible
        {
            get { return _root.gameObject.activeSelf; }
            set { _root.gameObject.SetActive(value); }
        }

        /// <summary>
        /// Sets the fill, 0–1, and colours it from <see cref="UiStyle.MeterColor"/>.
        /// Values outside the range are clamped rather than rejected: a meter fed a
        /// bad number should look wrong, not throw in the middle of a chase.
        /// </summary>
        public void SetFill(float fill01)
        {
            var f = Mathf.Clamp01(fill01);
            Resize(f);
            _fill.color = UiStyle.MeterColor(f);
        }

        /// <summary>Sets the fill and overrides the colour — for meters whose danger is not "low", such as a carry-weight band.</summary>
        public void SetFill(float fill01, Color color)
        {
            Resize(Mathf.Clamp01(fill01));
            _fill.color = color;
        }

        /// <summary>
        /// Recolours the empty half.
        /// <para>
        /// <see cref="UiStyle.BarBed"/> is nearly invisible on purpose — on the HUD a
        /// meter's bed would be one more lit thing in a dark corridor, and a bar that
        /// is only its fill still reads because it is shrinking in front of you. On a
        /// panel that is not true: §08's shop draws §07's night as a single wide bar
        /// that barely moves while the team argues, and with an invisible bed a
        /// nearly-full one is indistinguishable from a horizontal rule.
        /// </para>
        /// </summary>
        public void SetBedColor(Color color)
        {
            if (_bed != null)
            {
                _bed.color = color;
            }
        }

        /// <summary>
        /// Writes the fraction as the fill's right-hand anchor.
        /// <para>
        /// The offsets are re-zeroed every time because moving an anchor does not move
        /// the offsets with it: a rect anchored to a shrinking span keeps whatever
        /// <c>offsetMax</c> it had, and the bar would draw a few pixels past its own end.
        /// </para>
        /// </summary>
        private void Resize(float fill01)
        {
            _fillRect.anchorMin = new Vector2(0f, 0f);
            _fillRect.anchorMax = new Vector2(fill01, 1f);
            _fillRect.offsetMin = Vector2.zero;
            _fillRect.offsetMax = Vector2.zero;
        }
    }
}
