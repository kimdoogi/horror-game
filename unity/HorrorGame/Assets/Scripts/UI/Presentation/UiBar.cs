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

        /// <summary>Wraps an already-built bed and fill. Produced by <see cref="UiFactory.CreateBar"/>.</summary>
        public UiBar(RectTransform root, Image fill)
        {
            _root = root;
            _fill = fill;
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
            _fill.fillAmount = f;
            _fill.color = UiStyle.MeterColor(f);
        }

        /// <summary>Sets the fill and overrides the colour — for meters whose danger is not "low", such as a carry-weight band.</summary>
        public void SetFill(float fill01, Color color)
        {
            _fill.fillAmount = Mathf.Clamp01(fill01);
            _fill.color = color;
        }
    }
}
