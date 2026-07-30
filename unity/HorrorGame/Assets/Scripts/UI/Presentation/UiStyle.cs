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
    /// reserved for the two states that should pull the eye — a resource that has
    /// run out, and a §10 trade the player is in the middle of making.
    /// </para>
    /// </summary>
    public static class UiStyle
    {
        /// <summary>Ordinary readout text. Deliberately dim: the HUD is not the game.</summary>
        public static readonly Color Ink = new Color(0.78f, 0.78f, 0.74f, 0.72f);

        /// <summary>Secondary text — units, reasons, the smaller half of a line.</summary>
        public static readonly Color InkFaint = new Color(0.72f, 0.72f, 0.68f, 0.42f);

        /// <summary>A resource still comfortable, or a bar's filled portion at rest.</summary>
        public static readonly Color Calm = new Color(0.66f, 0.70f, 0.68f, 0.80f);

        /// <summary>A §10 trade currently being paid — a load penalty, an upgraded beam, a bought drawback.</summary>
        public static readonly Color Trade = new Color(0.85f, 0.66f, 0.30f, 0.92f);

        /// <summary>A resource that has run out, or a state the player cannot act out of. §03's lock, closed.</summary>
        public static readonly Color Spent = new Color(0.78f, 0.29f, 0.24f, 0.95f);

        /// <summary>The empty part of any bar.</summary>
        public static readonly Color BarBed = new Color(0.10f, 0.10f, 0.10f, 0.55f);

        /// <summary>Full-screen panel backing for the shop, the lobby and the end screen.</summary>
        public static readonly Color Panel = new Color(0.04f, 0.04f, 0.05f, 0.94f);

        /// <summary>One row inside a panel.</summary>
        public static readonly Color Row = new Color(0.10f, 0.10f, 0.11f, 0.85f);

        /// <summary>A row the team cannot afford, or an option that is not selectable.</summary>
        public static readonly Color RowDisabled = new Color(0.07f, 0.07f, 0.07f, 0.70f);

        /// <summary>A row the interface is warning about — §08's 소음기 with a 청음사 on the roster.</summary>
        public static readonly Color RowConflict = new Color(0.20f, 0.07f, 0.06f, 0.88f);

        /// <summary>A row §11 marks as this match's stand-in for the missing role.</summary>
        public static readonly Color RowSubstitute = new Color(0.13f, 0.12f, 0.06f, 0.90f);

        /// <summary>Body text size, in reference pixels.</summary>
        public const int TextSize = 18;

        /// <summary>Smaller supporting text — the §08 대가 line, a unit, a reason.</summary>
        public const int TextSizeSmall = 14;

        /// <summary>A panel's title.</summary>
        public const int TextSizeTitle = 34;

        /// <summary>The single word an end screen leads with.</summary>
        public const int TextSizeHeadline = 56;

        /// <summary>Height of a HUD bar.</summary>
        public const float BarHeight = 4f;

        /// <summary>Width of a HUD bar.</summary>
        public const float BarWidth = 168f;

        /// <summary>Gap between stacked HUD lines.</summary>
        public const float LineGap = 22f;

        /// <summary>Inset from the screen edge for HUD corners.</summary>
        public const float ScreenMargin = 28f;

        /// <summary>Height of one selectable row in the shop or the lobby.</summary>
        public const float RowHeight = 54f;

        /// <summary>Gap between rows.</summary>
        public const float RowGap = 4f;

        /// <summary>Reference resolution the canvas scales against.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>
        /// Canvas sort order for the in-world HUD. The lowest of the four, because
        /// every other screen is something the player opened deliberately and the HUD
        /// is something they did not.
        /// </summary>
        public const int SortOrderHud = 100;

        /// <summary>Canvas sort order for the clue overlay — above the HUD, below anything that stops the world.</summary>
        public const int SortOrderClue = 200;

        /// <summary>Canvas sort order for the shop and the lobby.</summary>
        public const int SortOrderPanel = 300;

        /// <summary>Canvas sort order for the §02 end screen. Nothing draws over an ending.</summary>
        public const int SortOrderEnd = 400;

        /// <summary>Degrees of view rotation per pixel of mouse movement in the §09 ghost camera. A comfort preference, not a balance value — the ghost's speed cannot reach the living.</summary>
        public const float GhostLookDegreesPerPixel = 0.12f;

        /// <summary>Steepest the ghost camera may look up or down, degrees. Prevents the view flipping over at the poles.</summary>
        public const float GhostPitchLimit = 89f;

        /// <summary>
        /// Colour for a bar at a given fill, blended across the three states above.
        /// Used by every meter so that "nearly gone" looks the same whether it is a
        /// battery, a stamina bar or a 45 s cooldown.
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
