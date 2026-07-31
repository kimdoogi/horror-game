#nullable enable

using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.UI.Settings
{
    /// <summary>
    /// The bounds every stored preference is clamped to, and where each bound comes
    /// from.
    /// <para>
    /// <b>Two kinds of number live here and they are not the same kind.</b>
    /// ARCHITECTURE §2 says every value the design reasons about belongs in
    /// <c>GameConstants</c>, and <see cref="UiStyle"/> already draws the line the same
    /// way: a font size cannot make the monster faster, so it is not a game value. A
    /// settings screen sits astride that line, because two of the things a player can
    /// change here <em>do</em> change what happens in a match.
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Balance.</b> Field of view is §05's, explicitly — "기본 80, 조절 범위 70~90"
    /// — so this file does not restate it. It defers to
    /// <see cref="HorrorGame.Gameplay.Player.PlayerCameraRig.ClampFov"/>, whose own
    /// doc comment says it exists "so a settings screen can show the range without
    /// duplicating the numbers", and that clamps against
    /// <c>GameConstants.FovMin</c> / <c>FovMax</c>.
    /// </description></item>
    /// <item><description>
    /// <b>Comfort.</b> Mouse sensitivity and invert-Y change how a hand reaches a
    /// heading, not what a player can see at once; <c>PlayerInputRouter</c> says so in
    /// as many words. They are the same class of value as
    /// <see cref="UiStyle.GhostLookDegreesPerPixel"/> and live in this layer.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Brightness is the awkward one and it is handled by derivation rather than by
    /// taste.</b> §03 makes darkness the lock on progress — "어둠 = 목표의 잠금장치" —
    /// so a free gamma slider is a mechanic-removal tool, and yet <c>GameConstants</c>
    /// has no §03 gamma value to defer to because the design document never imagined
    /// the player's monitor. Rather than invent one,
    /// <see cref="BrightnessGainSpan"/> is taken from
    /// <c>GameConstants.ClueMinReadableLightQuality</c>: §03 puts its threshold at 20 %
    /// of full light, so the widest a display preference may move the picture is the
    /// same 20 %. At that width a surface sitting at §03's floor is still below it and
    /// one above is still above it — the slider changes how comfortable the dark is to
    /// look at and cannot change which side of §03's threshold anything is on.
    /// </para>
    /// </summary>
    public static class SettingsLimits
    {
        /// <summary>
        /// Slowest mouse, in multiples of the player rig's authored degrees-per-count.
        /// A comfort bound: below this a 180° turn needs more desk than exists.
        /// </summary>
        public const float MouseSensitivityMin = 0.25f;

        /// <summary>Fastest mouse, same units. Above this the view is unaimable rather than fast.</summary>
        public const float MouseSensitivityMax = 4f;

        /// <summary>Unchanged from the rig's own authored value. §05's turning speed is not a balance item.</summary>
        public const float MouseSensitivityDefault = 1f;

        /// <summary>Every volume slider's floor. Silence is a legitimate choice; §04's warning is a label, not a lock.</summary>
        public const float VolumeMin = 0f;

        /// <summary>Every volume slider's ceiling. <c>GameAudio</c> takes 0..1 and applies §-derived trims underneath.</summary>
        public const float VolumeMax = 1f;

        /// <summary>
        /// Widest fractional change in displayed luminance a player may apply, either
        /// way. §03's own threshold, reused — see the type remarks for why this is a
        /// derivation and not a preference.
        /// </summary>
        public static float BrightnessGainSpan
        {
            get { return GameConstants.ClueMinReadableLightQuality; }
        }

        /// <summary>Dimmest the player may make the picture, as a linear multiplier on luminance.</summary>
        public static float BrightnessGainMin
        {
            get { return 1f - BrightnessGainSpan; }
        }

        /// <summary>Brightest the player may make the picture, as a linear multiplier on luminance.</summary>
        public static float BrightnessGainMax
        {
            get { return 1f + BrightnessGainSpan; }
        }

        /// <summary>
        /// The 0..1 slider position that leaves the picture exactly as the art
        /// direction graded it. Not 0.5 — the gain range is symmetric in <em>linear</em>
        /// light and the exposure applied from it is symmetric in stops, so the neutral
        /// point sits where <see cref="BrightnessExposure"/> returns zero.
        /// </summary>
        public static float BrightnessNeutral01
        {
            get { return Mathf.InverseLerp(BrightnessGainMin, BrightnessGainMax, 1f); }
        }

        /// <summary>
        /// Post-exposure in EV for a 0..1 slider position — what
        /// <see cref="BrightnessGrade"/> writes into its colour grading override.
        /// <para>
        /// Exposure rather than a gamma curve because ART.md §3.7 puts the project in
        /// linear colour under an ACES tonemapper: a gamma tweak applied after the
        /// tonemapper flattens the toe and returns the milky look ART.md §3.7 warns
        /// about, while an exposure offset goes in before it and keeps the curve the
        /// art was graded against.
        /// </para>
        /// </summary>
        public static float BrightnessExposure(float slider01)
        {
            var gain = Mathf.Lerp(BrightnessGainMin, BrightnessGainMax, Mathf.Clamp01(slider01));
            return Mathf.Log(gain, 2f);
        }

        /// <summary>The percentage a slider position represents, for the label. −20 % … +20 %.</summary>
        public static int BrightnessPercent(float slider01)
        {
            var gain = Mathf.Lerp(BrightnessGainMin, BrightnessGainMax, Mathf.Clamp01(slider01));
            return Mathf.RoundToInt((gain - 1f) * 100f);
        }
    }
}
