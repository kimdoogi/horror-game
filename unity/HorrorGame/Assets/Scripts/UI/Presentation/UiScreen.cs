#nullable enable

using UnityEngine;

namespace HorrorGame.UI
{
    /// <summary>
    /// The shared skeleton of every screen in this layer: one canvas, built once,
    /// shown and hidden by whatever owns it.
    /// <para>
    /// <b>What a screen may and may not do.</b> ARCHITECTURE §1 puts rules in Core
    /// and leaves this layer to represent them. So a subclass reads core state and
    /// writes strings, colours and fill fractions — it never advances a clock, never
    /// spends a credit and never decides an outcome. Where the player has to be able
    /// to <em>act</em>, the screen raises a request through an interface
    /// (<see cref="IShopRequests"/>, <see cref="IRoleClaimRequests"/>) and waits to
    /// be told what happened, because §13 gives the host authority over both and a
    /// client that applied its own purchase would be guessing.
    /// </para>
    /// <para>
    /// This assembly deliberately does not reference Mirror or Steamworks. A
    /// <c>NetworkBehaviour</c> cannot be written here even by accident, which is the
    /// cheapest possible enforcement of ARCHITECTURE §4's rule that clue contents and
    /// the objective's location never leave the host.
    /// </para>
    /// </summary>
    public abstract class UiScreen : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Face to draw with. Leave empty for the engine's built-in font; supply one carrying Korean glyphs for a shipping build.")]
        private Font? _font;

        private Canvas? _canvas;
        private bool _built;

        /// <summary>The face this screen draws with. Resolved once, on first build.</summary>
        protected Font? Font
        {
            get { return UiFactory.ResolveFont(_font); }
        }

        /// <summary>This screen's canvas, or null before <see cref="Build"/> has run.</summary>
        protected Canvas? Canvas
        {
            get { return _canvas; }
        }

        /// <summary>Whether the screen is currently drawn.</summary>
        public bool IsVisible
        {
            get { return _canvas != null && _canvas.gameObject.activeSelf; }
        }

        /// <summary>Canvas sort order — one of the <c>UiStyle.SortOrder*</c> values.</summary>
        protected abstract int SortOrder { get; }

        /// <summary>Whether this screen accepts clicks. False for read-only overlays.</summary>
        protected abstract bool Interactive { get; }

        /// <summary>Builds the hierarchy under <paramref name="root"/>. Called exactly once.</summary>
        protected abstract void Build(RectTransform root);

        /// <summary>
        /// Shows or hides the screen, building it on first use.
        /// <para>
        /// Building lazily rather than in <c>Awake</c> keeps a screen the match never
        /// opens — most matches never reach an end screen other than one of the four —
        /// from costing a canvas rebuild at load.
        /// </para>
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (visible)
            {
                EnsureBuilt();
            }

            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(visible);
            }
        }

        /// <summary>Builds the canvas if it does not exist yet. Safe to call every frame.</summary>
        protected void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _built = true;

            var canvas = UiFactory.CreateCanvas(GetType().Name + "Canvas", SortOrder, Interactive);
            canvas.transform.SetParent(transform, false);
            _canvas = canvas;

            if (Interactive)
            {
                UiFactory.EnsureEventSystem();
            }

            var root = UiFactory.Stretch(UiFactory.CreateRect("Root", canvas.transform));
            Build(root);
        }

        /// <summary>Tears the canvas down. Unity calls this; subclasses that override must call base.</summary>
        protected virtual void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
            }
        }
    }
}
