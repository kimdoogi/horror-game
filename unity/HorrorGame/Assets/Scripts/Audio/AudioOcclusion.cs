#nullable enable

using HorrorGame.Core.Map;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// How much of a sound's path is blocked, and what that costs it.
    /// <para>
    /// A profile exists per family rather than per emitter because §04 makes wall
    /// penetration a <em>mechanic</em> for exactly one family and mere realism for
    /// the rest. The 청음사 is defined by hearing through walls; a door hinge is not.
    /// A single "realistic" occlusion setting would therefore either delete the role
    /// or make the building sound like paper.
    /// </para>
    /// </summary>
    public readonly struct OcclusionProfile
    {
        /// <summary>Builds a profile. Prefer the named presets.</summary>
        public OcclusionProfile(float floorCutoffHz, float fullAttenuationDb)
        {
            FloorCutoffHz = floorCutoffHz;
            FullAttenuationDb = fullAttenuationDb;
        }

        /// <summary>
        /// Corner the low-pass reaches at full occlusion, hertz. The number that
        /// decides whether §12's five-surface alphabet survives — see
        /// <see cref="AudioTuning.ListenerChannelOcclusionFloorHz"/> for the
        /// measurement.
        /// </summary>
        public float FloorCutoffHz { get; }

        /// <summary>Broadband attenuation at full occlusion, dB. Negative.</summary>
        public float FullAttenuationDb { get; }

        /// <summary>
        /// §12's floor materials and §06's monster cues — everything the Listener
        /// reads. The gentlest profile in the game, deliberately.
        /// </summary>
        public static OcclusionProfile ListenerChannel => new OcclusionProfile(
            AudioTuning.ListenerChannelOcclusionFloorHz,
            AudioTuning.ListenerChannelOcclusionAttenuationDb);

        /// <summary>§13's 근접 음성. Keeps the consonant band so a muffled teammate is still a legible one.</summary>
        public static OcclusionProfile Voice => new OcclusionProfile(
            AudioTuning.VoiceOcclusionFloorHz,
            AudioTuning.VoiceOcclusionAttenuationDb);

        /// <summary>Props, doors, loot — sound that carries no information a player acts on.</summary>
        public static OcclusionProfile World => new OcclusionProfile(
            AudioTuning.WorldOcclusionFloorHz,
            AudioTuning.WorldOcclusionAttenuationDb);

        /// <summary>The profile a bus uses. <see cref="AudioBus.Interface"/> is never occluded and returns <see cref="World"/> unused.</summary>
        public static OcclusionProfile For(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Footsteps:
                case AudioBus.Monster:
                    return ListenerChannel;

                case AudioBus.Voice:
                    return Voice;

                default:
                    return World;
            }
        }
    }

    /// <summary>
    /// The occlusion curve: occlusion amount in, filter corner and gain out.
    /// <para>
    /// This is the Unity-side answer to <c>docs/BALANCE-FINDINGS.md</c> F-003, whose
    /// brief is exact — the Listener must lose <em>precision</em> with distance
    /// without losing the surface's <em>identity</em>. The two live in different
    /// places in the signal, which is what makes the brief achievable at all:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Identity is spectral.</b> It is the ratio between two surfaces' spectral
    /// centroids, and a low-pass destroys it by dragging every surface down onto its
    /// own corner. So the corner is clamped: it never goes below
    /// <see cref="OcclusionProfile.FloorCutoffHz"/>, measured as the lowest corner at
    /// which all five surfaces still separate by F-003's 1.4×.
    /// </description></item>
    /// <item><description>
    /// <b>Precision is level.</b> It is how far the cue stands above the ambience
    /// bed, and that is what distance is allowed to take away — see
    /// <see cref="AudioTuning.RolloffExponent"/> for the measured chain that puts an
    /// occluded footstep 1.3 dB over the bed at 25 m and 1.1 dB under it at 40 m.
    /// </description></item>
    /// </list>
    /// <para>
    /// So distance attenuates and occlusion filters, and neither is allowed to do the
    /// other's job. A Listener at range hears a floor they can name and a position
    /// they cannot pin, which is exactly what §04's error radius already models.
    /// </para>
    /// <para>
    /// Static and engine-free apart from <see cref="Mathf"/>, so the curve can be
    /// asserted against in an EditMode test without a scene.
    /// </para>
    /// </summary>
    public static class AudioOcclusion
    {
        /// <summary>
        /// Low-pass corner, hertz, at an occlusion amount of
        /// <paramref name="occlusion01"/>.
        /// <para>
        /// Interpolated geometrically, not linearly: hearing is logarithmic in
        /// frequency, so a linear sweep from 22 kHz to 800 Hz spends 90% of its travel
        /// in the top octave where nothing is audible and then falls off a cliff. In
        /// log space, half-occluded lands near 4.2 kHz — audibly muffled and still
        /// well clear of the identity floor.
        /// </para>
        /// </summary>
        public static float CutoffHz(in OcclusionProfile profile, float occlusion01)
        {
            var t = Mathf.Clamp01(occlusion01);
            var open = Mathf.Max(profile.FloorCutoffHz, AudioTuning.OcclusionOpenCutoffHz);
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(open), Mathf.Log(profile.FloorCutoffHz), t));
        }

        /// <summary>
        /// Linear gain multiplier at an occlusion amount of
        /// <paramref name="occlusion01"/>.
        /// <para>
        /// Applied on top of whatever the low-pass already removed, which for the
        /// Listener's channel is a measured 7.3 dB A-weighted on average — and 25.8 dB
        /// from 자갈 against 0.3 dB from 콘크리트. That spread is F-002 happening for
        /// free and correctly: the mix does not need a table to make gravel vanish
        /// through a wall, because the clips already do it. What the mix must not do
        /// is pile a second, flat wall loss on top and take the role with it.
        /// </para>
        /// </summary>
        public static float Gain(in OcclusionProfile profile, float occlusion01)
        {
            var t = Mathf.Clamp01(occlusion01);
            return DecibelsToLinear(profile.FullAttenuationDb * t);
        }

        /// <summary>
        /// Converts a blocked-ray count into an occlusion amount.
        /// <para>
        /// The direct ray is weighted more heavily than the offsets, because a path
        /// that is clear down the middle and blocked at the edges is a doorway, and a
        /// doorway is not a wall. §12 builds the whole map out of thresholds and
        /// bottlenecks, so getting this distinction wrong would mis-filter most of the
        /// building.
        /// </para>
        /// </summary>
        /// <param name="directBlocked">Whether the straight line from source to listener is blocked.</param>
        /// <param name="offsetBlocked">How many of the offset rays are blocked.</param>
        /// <param name="offsetCount">How many offset rays were cast. Zero falls back to the direct ray alone.</param>
        public static float FromRays(bool directBlocked, int offsetBlocked, int offsetCount)
        {
            if (offsetCount <= 0)
            {
                return directBlocked ? 1f : 0f;
            }

            // Half the answer is "is the line of sight blocked at all", half is "how
            // much of the aperture around it is". A clear direct ray therefore caps
            // occlusion at 0.5 however solid the surroundings, and a blocked one
            // floors it at 0.5 however open they are.
            var direct = directBlocked ? 1f : 0f;
            var spread = (float)offsetBlocked / offsetCount;
            return Mathf.Clamp01((direct * 0.5f) + (spread * 0.5f));
        }

        /// <summary>
        /// A-weighted level change, dB, that the occlusion floor does to one of §12's
        /// surfaces. Measured, and the reason F-002 exists.
        /// <para>
        /// Low-passing the shipped monster footsteps at
        /// <see cref="AudioTuning.ListenerChannelOcclusionFloorHz"/> through the
        /// engine's filter and re-measuring A-weighted RMS:
        /// </para>
        /// <code>
        ///   wood      +1.0 dB      concrete   +0.3 dB
        ///   metal     −1.6 dB      tile      −10.7 dB
        ///   gravel   −25.8 dB
        /// </code>
        /// <para>
        /// F-002's inversion in one column. 자갈's 부스럭 (§12) is broadband high
        /// frequency and a wall takes essentially all of it; 콘크리트's 둔탁 is a low
        /// thud and passes through untouched. So through a wall gravel is 26 dB
        /// quieter than concrete, while <c>GameConstants.ListenerClarity*</c> tells the
        /// player gravel is the clearer of the two.
        /// </para>
        /// <para>
        /// <b>The mix already reproduces this correctly and for free</b> — the clips
        /// have those spectra, so the filter does it without a table. This method
        /// exists to hand F-002 a measured number, not to drive anything: nothing in
        /// this layer calls it in a way that changes what is heard. The fix F-002
        /// actually needs is its option 1, making clarity a function of occlusion
        /// inside <c>ListenerAbility</c>, and that is a Core change and a designer's
        /// decision. Wiring a second, parallel clarity into the presentation layer
        /// would produce exactly the disagreement between HUD and ears that the
        /// finding is about.
        /// </para>
        /// </summary>
        public static float OccludedLevelChangeDb(FloorMaterial material)
        {
            switch (material)
            {
                case FloorMaterial.Wood:
                    return 1.0f;
                case FloorMaterial.Tile:
                    return -10.7f;
                case FloorMaterial.Gravel:
                    return -25.8f;
                case FloorMaterial.Concrete:
                    return 0.3f;
                case FloorMaterial.Metal:
                    return -1.6f;
                default:
                    return 0f;
            }
        }

        /// <summary>Decibels to a linear amplitude multiplier. Zero dB is unity.</summary>
        public static float DecibelsToLinear(float decibels)
        {
            return Mathf.Pow(10f, decibels / 20f);
        }

        /// <summary>
        /// Linear amplitude to decibels, floored at −80 dB so silence has a finite
        /// value an <c>AudioMixer</c> parameter can be set to.
        /// </summary>
        public static float LinearToDecibels(float linear)
        {
            return linear <= AudioTuning.SilenceThreshold ? -80f : 20f * Mathf.Log10(linear);
        }
    }
}
