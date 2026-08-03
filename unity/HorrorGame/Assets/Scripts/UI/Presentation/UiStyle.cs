#nullable enable

using UnityEngine;

namespace HorrorGame.UI
{
    /// <summary>
    /// Colours, sizes and spacing for every screen.
    /// <para>
    /// <b>These are not tuned game values.</b> ARCHITECTURE §2 requires every number
    /// the design reasons about to live in <c>GameConstants</c>, and none of these
    /// change what happens in a match — a font size cannot make the monster faster.
    /// Anything here that starts affecting play has stopped being a style value and
    /// belongs in <c>GameConstants</c> with a § citation instead.
    /// </para>
    /// <para>
    /// The palette is built around one rule from §01: the game is dark and the HUD
    /// competes with the flashlight beam for the player's attention. So the normal
    /// state of every readout is dim grey at low opacity, and saturated colour is
    /// reserved for the two states that should pull the eye — something that has run
    /// out, and the one moment of a race a runner has to react to.
    /// </para>
    /// <para>
    /// <b>What the pivot took out of this file.</b> DESCENT-PIVOT §7 step 7 deleted the
    /// shop, so the ten <c>Shop*</c> layout constants that sized its panel went with it,
    /// along with <c>RowConflict</c> (a §08 row warning about a teammate's 직업),
    /// <c>RowSubstitute</c> (§11's stand-in for the missing role), <c>RowSelectedLift</c>
    /// (the shop's keyboard cursor), <c>SpentStrong</c> (a refusal sized for the shop's
    /// shortfall line), <c>TextSizeHeadline</c> (the co-op end screen's single word) and
    /// <c>SortOrderClue</c> (§03's reading overlay). None of them named a colour; each
    /// named a screen. <c>ColumnGap</c> is <c>ShopColumnGap</c> under a name that does
    /// not lie — the lobby's two columns of ten were always its second user.
    /// </para>
    /// </summary>
    public static class UiStyle
    {
        /// <summary>Ordinary readout text. Deliberately dim: the HUD is not the game.</summary>
        public static readonly Color Ink = new Color(0.78f, 0.78f, 0.74f, 0.72f);

        /// <summary>
        /// Body text on a full-screen panel the player opened on purpose.
        /// <para>
        /// <see cref="Ink"/>'s 0.72 alpha is sized for a readout glanced at in
        /// peripheral vision while a corridor is the thing being looked at. A panel is
        /// the opposite situation: it is opaque, it fills the display, and it is being
        /// read rather than sensed. Panels get this; the HUD never does.
        /// </para>
        /// </summary>
        public static readonly Color InkStrong = new Color(0.90f, 0.90f, 0.86f, 0.96f);

        /// <summary>Secondary text — units, reasons, the smaller half of a line.</summary>
        public static readonly Color InkFaint = new Color(0.72f, 0.72f, 0.68f, 0.42f);

        /// <summary>Secondary text on a panel. <see cref="InkFaint"/>'s alpha disappears against <see cref="Panel"/>.</summary>
        public static readonly Color InkQuiet = new Color(0.74f, 0.74f, 0.70f, 0.62f);

        /// <summary>A resource still comfortable, or a bar's filled portion at rest.</summary>
        public static readonly Color Calm = new Color(0.66f, 0.70f, 0.68f, 0.80f);

        /// <summary>
        /// The one thing on screen a runner has to act on now — the winner reaching the
        /// bottom storey, a field that has fallen below what a race needs.
        /// </summary>
        public static readonly Color Trade = new Color(0.85f, 0.66f, 0.30f, 0.92f);

        /// <summary>A resource that has run out, or a state the player cannot act out of. 탈락, closed.</summary>
        public static readonly Color Spent = new Color(0.78f, 0.29f, 0.24f, 0.95f);

        /// <summary>The empty part of any bar.</summary>
        public static readonly Color BarBed = new Color(0.10f, 0.10f, 0.10f, 0.55f);

        /// <summary>Full-screen panel backing for the lobby and the settings screen.</summary>
        public static readonly Color Panel = new Color(0.04f, 0.04f, 0.05f, 0.94f);

        /// <summary>One row inside a panel.</summary>
        public static readonly Color Row = new Color(0.10f, 0.10f, 0.11f, 0.85f);

        /// <summary>A row nobody is in yet, or an option that is not selectable.</summary>
        public static readonly Color RowDisabled = new Color(0.07f, 0.07f, 0.07f, 0.70f);

        /// <summary>The row that is you. §11's lobby is twenty identical seats and this is the only thing that distinguishes one of them.</summary>
        public static readonly Color RowAffordable = new Color(0.17f, 0.18f, 0.17f, 0.92f);

        /// <summary>Body text size, in reference pixels.</summary>
        public const int TextSize = 18;

        /// <summary>Smaller supporting text — a unit, a reason, the second line of a pair.</summary>
        public const int TextSizeSmall = 14;

        /// <summary>A panel's title, and the storey the runner is on.</summary>
        public const int TextSizeTitle = 34;

        /// <summary>Height of a HUD bar.</summary>
        public const float BarHeight = 4f;

        /// <summary>Width of a HUD bar.</summary>
        public const float BarWidth = 168f;

        /// <summary>Gap between stacked HUD lines.</summary>
        public const float LineGap = 22f;

        /// <summary>Inset from the screen edge for HUD corners.</summary>
        public const float ScreenMargin = 28f;

        /// <summary>Height of one selectable row on a panel.</summary>
        public const float RowHeight = 54f;

        /// <summary>Gap between two columns of rows on a panel.</summary>
        public const float ColumnGap = 24f;

        /// <summary>Reference resolution the canvas scales against.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>
        /// Canvas sort order for the in-world HUD. The lowest of the three, because
        /// every other screen is something the player opened deliberately and the HUD
        /// is something they did not.
        /// </summary>
        public const int SortOrderHud = 100;

        /// <summary>Canvas sort order for the lobby and the settings screen.</summary>
        public const int SortOrderPanel = 300;

        /// <summary>Canvas sort order for a screen nothing may draw over — the loading screen sits above this.</summary>
        public const int SortOrderEnd = 400;

        /// <summary>Degrees of view rotation per pixel of mouse movement in the §09 ghost camera. A comfort preference, not a balance value — the ghost's speed cannot reach the living.</summary>
        public const float GhostLookDegreesPerPixel = 0.12f;

        /// <summary>Steepest the ghost camera may look up or down, degrees. Prevents the view flipping over at the poles.</summary>
        public const float GhostPitchLimit = 89f;

        /// <summary>
        /// Colour for a bar at a given fill, blended across the three states above.
        /// Used by every meter so that "nearly gone" looks the same whether it is a
        /// load bar or a 45 s cooldown.
        /// </summary>
        public static Color MeterColor(float fill01)
        {
            if (fill01 <= 0.001f)
            {
                return Spent;
            }

            return fill01 < 0.34f ? Color.Lerp(Spent, Trade, fill01 / 0.34f) : Color.Lerp(Trade, Calm, (fill01 - 0.34f) / 0.66f);
        }
    }
}
