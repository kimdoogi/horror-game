#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Threat;
using UnityEngine;

namespace HorrorGame.Rendering
{
    /// <summary>
    /// The state of the air and the grade at one moment of §07's night.
    /// <para>
    /// Grouped into a value rather than passed as eight floats because every
    /// consumer wants all of it at once: the editor bakes one of these into a
    /// scene, and <see cref="ThreatAtmosphereDirector"/> pushes one into the
    /// Volume every frame. Splitting them would let the fog and the grade drift to
    /// different points of the night, which is exactly the bug nobody would spot
    /// in a screenshot.
    /// </para>
    /// </summary>
    public readonly struct NightAtmosphereSettings
    {
        /// <summary>
        /// Ambient landing on up-facing surfaces — which is the floor, not the sky.
        /// Kept below <see cref="AmbientEquator"/>: a basement has no sky, and a
        /// floor lit more than the walls it stands between reads as an outdoor
        /// plaza at dusk.
        /// </summary>
        public Color AmbientSky { get; }

        /// <summary>
        /// Ambient on vertical surfaces — the walls. The brightest of the three,
        /// because walls are what §03 needs readable outside the beam: a corridor
        /// is navigable when you can see where it turns.
        /// </summary>
        public Color AmbientEquator { get; }

        /// <summary>Ambient on down-facing surfaces — the ceiling. Darkest of the three; nothing under a basement lights it.</summary>
        public Color AmbientGround { get; }

        /// <summary>Colour a surface converges to at distance. Brighter than the walls, or depth reads as a black hole.</summary>
        public Color FogColour { get; }

        /// <summary>Exponential-squared fog density. Derived from §12's sight lines — see <see cref="NightAtmosphere"/>.</summary>
        public float FogDensity { get; }

        /// <summary>Post-exposure in EV applied by colour grading. This, not the light rigs, sets where "dark" sits.</summary>
        public float PostExposure { get; }

        /// <summary>Saturation offset, −100..100. Negative all night; §05's basement has no colour of its own.</summary>
        public float Saturation { get; }

        /// <summary>
        /// Contrast offset, −100..100.
        /// <para>
        /// Small numbers here do much more than they look like they should. URP
        /// applies contrast in ACEScc log space around a mid-grey pivot of about
        /// 0.41, so a value that reads like "a bit punchier" pushes an already-dark
        /// ambient almost to zero: at +26 the walls of an unlit room measured a
        /// median luminance of 1 out of 255 — black, not dark. The night is made
        /// out of the ambient values and the fog, and this stays low enough to let
        /// them survive.
        /// </para>
        /// </summary>
        public float Contrast { get; }

        /// <summary>White balance temperature, −100..100. Negative is colder.</summary>
        public float Temperature { get; }

        /// <summary>Vignette strength, 0..1. Closes the frame as the night gets worse.</summary>
        public float VignetteIntensity { get; }

        /// <summary>Creates a settings value. All fields are art-tuned; see <see cref="NightAtmosphere"/> for the reasoning.</summary>
        public NightAtmosphereSettings(
            Color ambientSky,
            Color ambientEquator,
            Color ambientGround,
            Color fogColour,
            float fogDensity,
            float postExposure,
            float saturation,
            float contrast,
            float temperature,
            float vignetteIntensity)
        {
            AmbientSky = ambientSky;
            AmbientEquator = ambientEquator;
            AmbientGround = ambientGround;
            FogColour = fogColour;
            FogDensity = fogDensity;
            PostExposure = postExposure;
            Saturation = saturation;
            Contrast = contrast;
            Temperature = temperature;
            VignetteIntensity = vignetteIntensity;
        }
    }

    /// <summary>
    /// §07's night, expressed as light and air instead of as monster speed.
    /// <para>
    /// §07 gives the night five rows and calls the pressure "연속적" — continuous.
    /// <see cref="ThreatCurve"/> resolves that tension by stepping the mechanics
    /// and offering <see cref="ThreatCurve.ThreatScalar"/> for "music layers,
    /// ambience, HUD tint" — presentation. Atmosphere is presentation, so this
    /// class interpolates: there is no frame on which the room visibly changes
    /// colour, but a player comparing 초저녁 to 동트기 전 is looking at a different
    /// building. §07 already darkens 심야 mechanically (손전등 반경 −30%); if the
    /// picture did not darken with it, the tier would be a number in a log rather
    /// than something you feel.
    /// </para>
    /// <para>
    /// Nothing here may decide an outcome. These are art values — the gameplay
    /// numbers they are derived from live in <see cref="GameConstants"/> and are
    /// referenced, not copied.
    /// </para>
    /// </summary>
    public static class NightAtmosphere
    {
        /// <summary>
        /// Fog density at 초저녁, for <see cref="FogMode.ExponentialSquared"/>.
        /// <para>
        /// Solved rather than eyeballed. Exp² fog leaves a surface at distance
        /// <c>d</c> showing <c>exp(−(density·d)²)</c> of itself, so a density of
        /// <c>√(ln 2) / d</c> puts the half-way point exactly at <c>d</c>. That
        /// distance is <see cref="GameConstants.LineOfSightBreakSpacingMax"/>
        /// (25 m) — the longest sight line §12 obliges a legal map to provide, and
        /// also <see cref="GameConstants.FlashlightNoticeDistance"/>, the range at
        /// which the Runner can pull aggro. So at the exact distance §12's escape
        /// arithmetic depends on, a shape is still half there: visible enough to
        /// aim a taunt at, hazed enough that the corridor has depth. Anything
        /// denser and the Runner cannot see what they are baiting, which breaks
        /// §12 rather than the mood.
        /// </para>
        /// <para>
        /// At <see cref="GameConstants.FlashlightRange"/> (12 m) this costs the
        /// beam about 15% of its contrast — noticeable as air, not as a filter.
        /// At <see cref="GameConstants.ZoneDiagonalMax"/> (40 m) it is 83%, so the
        /// far wall of the largest zone is gone. That is the intended reading of
        /// "구역 대각선 30~40m": you cannot see the whole zone at once.
        /// </para>
        /// </summary>
        public static readonly float FogDensityEarlyEvening =
            Mathf.Sqrt(Mathf.Log(2f)) / GameConstants.LineOfSightBreakSpacingMax;

        /// <summary>
        /// How much denser the air is by 동트기 전 than at 초저녁.
        /// <para>
        /// Capped deliberately. The last tier still has to be playable enough for
        /// §02's escape to be attemptable, and §12's 25 m sight line is a map
        /// contract that does not expire at 32 minutes — the Runner test has to
        /// still pass. 1.45× moves the half-visibility point from 25 m to about
        /// 17 m, which stays inside §12's 15–25 m band. A denser night would
        /// silently invalidate the map.
        /// </para>
        /// </summary>
        private const float FogDensityFinalMultiplier = 1.45f;

        /// <summary>
        /// The look at 초저녁 (§07 tier 0) — cold, dark, but everything still
        /// legible. This is the "readable dark" end of the ramp.
        /// <para>
        /// The colours look implausibly dark written down because they are sRGB and
        /// the project renders linear: 0.06 here is about 0.005 of the light the
        /// flashlight puts on a wall a few metres away. That ratio is the entire
        /// point — it has to be small enough that §03's beam is obviously doing the
        /// work, and large enough that a corner has a shape before you point the
        /// beam at it. Both ends were set by looking at renders, not by theory.
        /// </para>
        /// </summary>
        /// <remarks>
        /// These were raised roughly 2.6× in sRGB — about 8× in linear — from the
        /// values first tuned here, and the reason is a bug rather than a change of
        /// taste. They were set while the scene still had Unity's default daytime
        /// skybox in <c>RenderSettings</c>, left there by the map generator, with
        /// reflection intensity at its default 1. That sky was quietly contributing
        /// most of the light in every frame. Once the night sky went in and the
        /// reflection came down to <see cref="ReflectionIntensity"/>, the same
        /// numbers rendered a frame that was 89% pure black with 8% of its pixels
        /// legible — the ambient had never actually been carrying the room.
        /// </remarks>
        private static readonly NightAtmosphereSettings EarlyEvening = new NightAtmosphereSettings(
            ambientSky: new Color(0.098f, 0.106f, 0.126f),
            ambientEquator: new Color(0.115f, 0.125f, 0.148f),
            ambientGround: new Color(0.045f, 0.046f, 0.052f),
            fogColour: new Color(0.160f, 0.176f, 0.205f),
            fogDensity: FogDensityEarlyEvening,
            postExposure: 1.28f,
            saturation: -34f,
            contrast: 10f,
            temperature: -14f,
            vignetteIntensity: 0.34f);

        /// <summary>
        /// The look at 동트기 전 (§07 tier 4, "생존 불가 수준"). Ambient is about a
        /// third of 초저녁 in linear terms — the beam is nearly the only light left
        /// — and the frame has closed in. Not black: §03 wants darkness to gate the
        /// objective, and a player who cannot tell a doorway from a wall is not
        /// being gated, they are being denied input.
        /// <para>
        /// Where the gap lives was measured, not guessed, and both obvious answers
        /// were wrong first. Scaling only the ambient produced a frame about 5%
        /// darker than 초저녁 — invisible, because the flashlight dominates every
        /// frame and the flashlight is not ours to dim. Adding a full stop of
        /// exposure on top of a third of the ambient produced a black screen,
        /// which the brief is right to call a bug rather than atmosphere. What
        /// works is most of the loss in ambient (halved, so the room outside the
        /// beam recedes) and only a third of a stop in exposure (so the beam still
        /// lands). The rest of "worse" is carried by things that cost no light at
        /// all: a heavier vignette, colder white balance, and the last of the
        /// colour drained out.
        /// </para>
        /// </summary>
        private static readonly NightAtmosphereSettings BeforeSunrise = new NightAtmosphereSettings(
            ambientSky: new Color(0.058f, 0.061f, 0.072f),
            ambientEquator: new Color(0.068f, 0.073f, 0.086f),
            ambientGround: new Color(0.032f, 0.033f, 0.037f),
            fogColour: new Color(0.123f, 0.135f, 0.158f),
            fogDensity: FogDensityEarlyEvening * FogDensityFinalMultiplier,
            postExposure: 0.98f,
            saturation: -48f,
            contrast: 15f,
            temperature: -32f,
            vignetteIntensity: 0.54f);

        /// <summary>
        /// The atmosphere at <paramref name="elapsedSeconds"/> into the match.
        /// <para>
        /// Reads <see cref="ThreatCurve.ThreatScalar"/> — the continuous half of
        /// §07, which that class explicitly reserves for ambience. Presentation is
        /// the one place interpolating the night is honest: a stepped grade would
        /// be a visible cut every eight minutes, and a cut reads as a bug.
        /// </para>
        /// </summary>
        public static NightAtmosphereSettings At(float elapsedSeconds) =>
            Lerp(ThreatCurve.ThreatScalar(elapsedSeconds));

        /// <summary>
        /// The atmosphere on §07 row <paramref name="tierIndex"/>, clamped into the
        /// table.
        /// <para>
        /// For tooling that wants one row rather than a moment: the editor bakes a
        /// tier into a scene so a still frame can be judged, and a comparison shot
        /// of tier 0 against tier 4 is how "the night gets worse" gets reviewed at
        /// all. Uses the same ramp as <see cref="At"/>, sampled at the instant the
        /// row begins, so the two can never disagree.
        /// </para>
        /// </summary>
        public static NightAtmosphereSettings ForTier(int tierIndex)
        {
            var last = Mathf.Max(1, GameConstants.ThreatTierCount - 1);
            return Lerp(Mathf.Clamp01((float)tierIndex / last));
        }

        /// <summary>
        /// The ramp itself. <paramref name="t"/> is 0 at 초저녁 and 1 once 동트기 전
        /// begins; values outside that are clamped, because §07 has no row before
        /// the first or after the last.
        /// </summary>
        public static NightAtmosphereSettings Lerp(float t)
        {
            // NaN fails every comparison, so clamping through Mathf.Clamp01 alone
            // would pass it straight into the grade and black the screen out.
            var k = float.IsNaN(t) ? 0f : Mathf.Clamp01(t);

            return new NightAtmosphereSettings(
                Color.Lerp(EarlyEvening.AmbientSky, BeforeSunrise.AmbientSky, k),
                Color.Lerp(EarlyEvening.AmbientEquator, BeforeSunrise.AmbientEquator, k),
                Color.Lerp(EarlyEvening.AmbientGround, BeforeSunrise.AmbientGround, k),
                Color.Lerp(EarlyEvening.FogColour, BeforeSunrise.FogColour, k),
                Mathf.Lerp(EarlyEvening.FogDensity, BeforeSunrise.FogDensity, k),
                Mathf.Lerp(EarlyEvening.PostExposure, BeforeSunrise.PostExposure, k),
                Mathf.Lerp(EarlyEvening.Saturation, BeforeSunrise.Saturation, k),
                Mathf.Lerp(EarlyEvening.Contrast, BeforeSunrise.Contrast, k),
                Mathf.Lerp(EarlyEvening.Temperature, BeforeSunrise.Temperature, k),
                Mathf.Lerp(EarlyEvening.VignetteIntensity, BeforeSunrise.VignetteIntensity, k));
        }

        /// <summary>
        /// How much of the skybox's specular reflection a surface is allowed to
        /// pick up.
        /// <para>
        /// Unity defaults this to 1, and that default is what made the tile and
        /// metal floors the brightest objects in the game. Ambient here is around
        /// 0.005 linear, but <c>reflectionIntensity</c> is a separate term that
        /// does not read the ambient colours at all: a smooth surface mirrors the
        /// skybox cubemap at full strength regardless of how dark the diffuse
        /// ambient is. §12 gives 타일 and 금속 the two lowest roughness values on
        /// purpose — so the beam streaks across them — and at full reflection that
        /// design decision inverted into two floors that glow without a beam
        /// anywhere near them.
        /// </para>
        /// <para>
        /// Not zero. Zero removes the sheen that makes a wet tile floor read as
        /// wet rather than as flat paint, and the sheen is the cue that tells
        /// 청음사 players which zone they are standing in from a single frame.
        /// 0.25 keeps the streak under the beam and takes the room-wide glow away.
        /// </para>
        /// </summary>
        public const float ReflectionIntensity = 0.25f;

        /// <summary>
        /// Writes <paramref name="settings"/> into the scene's environment lighting.
        /// <para>
        /// Fog and ambient are scene state, not Volume state — URP has no fog
        /// override, and ambient is a lighting setting. This is the one function
        /// that knows that, so the editor bake and the runtime director cannot
        /// drift apart.
        /// </para>
        /// </summary>
        public static void ApplyEnvironment(NightAtmosphereSettings settings)
        {
            // Gradient rather than flat. A flat term lights the ceiling and the
            // floor identically, which is what makes an unlit corridor read as a
            // diagram; a darker ground colour costs nothing and puts a shading
            // gradient on every surface the beam is not touching.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = settings.AmbientSky;
            RenderSettings.ambientEquatorColor = settings.AmbientEquator;
            RenderSettings.ambientGroundColor = settings.AmbientGround;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = ReflectionIntensity;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = settings.FogColour;
            RenderSettings.fogDensity = settings.FogDensity;
        }
    }
}
