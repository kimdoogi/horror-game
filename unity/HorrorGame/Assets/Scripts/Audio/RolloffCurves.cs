#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// Builds the custom distance rolloff every positional source in the game uses.
    /// <para>
    /// Unity's three built-in modes are all wrong here for the same reason: they were
    /// picked for a free field. <c>Logarithmic</c> is amplitude ∝ 1/d, 6 dB per
    /// doubling, which is what sound does outdoors with nothing to reflect off.
    /// The game is a basement — §12 builds it out of corridors capped at 20 m and
    /// rooms 30–40 m across — where sound is guided and returned, and the measured
    /// decay is far shallower. <c>Linear</c> is the opposite error: it makes the last
    /// third of the range far too loud and then stops.
    /// </para>
    /// <para>
    /// So the curve is baked: a power law with <see cref="AudioTuning.RolloffExponent"/>
    /// over the useful range, then a short taper to true silence at the limit. The
    /// taper is not cosmetic — Unity holds the final curve value for every distance
    /// beyond <c>maxDistance</c>, so a curve that ends above zero is a source that is
    /// never silent, at any distance, for the whole match.
    /// </para>
    /// <para>
    /// The range itself is never invented here. Callers pass one of the design's own
    /// distances: <c>GameConstants.ListenerHearingRange</c> for anything the 청음사
    /// reads, <c>GameConstants.VoiceCutoffDistance</c> for §13's voice — which is
    /// already what <c>AudioSourceVoiceOutput</c> does — and
    /// <see cref="AudioTuning.DefaultWorldAudibleRange"/> for props.
    /// </para>
    /// </summary>
    public static class RolloffCurves
    {
        private static readonly Dictionary<int, AnimationCurve> Cache = new Dictionary<int, AnimationCurve>();

        /// <summary>
        /// The curve for a source audible out to <paramref name="maxDistance"/> metres.
        /// <para>
        /// Cached per rounded distance: the curve is immutable in practice and every
        /// footstep emitter in the map wants the same one, so building it per source
        /// would allocate a keyframe array per prefab instance for no reason. Callers
        /// must not mutate the returned curve.
        /// </para>
        /// </summary>
        public static AnimationCurve For(float maxDistance)
        {
            var key = Mathf.RoundToInt(Mathf.Max(1f, maxDistance) * 10f);
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var curve = Build(key / 10f);
            Cache[key] = curve;
            return curve;
        }

        /// <summary>
        /// Applies the project's 3D settings to a source: spatialised, no Doppler,
        /// and the baked rolloff out to <paramref name="maxDistance"/>.
        /// <para>
        /// Doppler is off everywhere, for the reason
        /// <c>AudioSourceVoiceOutput</c> already gives for voice: §05's sprint is fast
        /// enough to shift pitch audibly, and a chase is the exact moment a footstep
        /// has to stay recognisable as the surface it is on. §12's alphabet does not
        /// survive being transposed.
        /// </para>
        /// </summary>
        public static void ApplyPositional(AudioSource source, float maxDistance)
        {
            if (source == null)
            {
                return;
            }

            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.minDistance = AudioTuning.RolloffReferenceDistance;
            source.maxDistance = Mathf.Max(AudioTuning.RolloffReferenceDistance + 1f, maxDistance);
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, For(source.maxDistance));
        }

        /// <summary>
        /// Applies the settings for a non-positional bed or interface cue: fully 2D,
        /// so it is unaffected by where the player is looking. §05 makes the stereo
        /// image load-bearing for everything else, which is precisely why the beds
        /// must not compete for it.
        /// </summary>
        public static void ApplyFlat(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 500f;
        }

        private static AnimationCurve Build(float maxDistance)
        {
            var reference = AudioTuning.RolloffReferenceDistance;
            var taperStart = Mathf.Lerp(maxDistance, reference, AudioTuning.RolloffSilenceTaperFraction);
            var keys = new List<Keyframe>(AudioTuning.RolloffCurveSamples + 2);

            // Unity normalises a custom rolloff curve's time axis to
            // distance / maxDistance and ignores minDistance, so the full-volume
            // region has to be written into the curve as a flat leading segment.
            keys.Add(new Keyframe(0f, 1f));
            keys.Add(new Keyframe(Mathf.Clamp01(reference / maxDistance), 1f));

            for (var i = 0; i < AudioTuning.RolloffCurveSamples; i++)
            {
                var t = (i + 1f) / AudioTuning.RolloffCurveSamples;

                // Sample geometrically so the near field — where the gradient a
                // Listener triangulates with actually lives (§05, "몸을 돌려 삼각측량")
                // — gets as many knots as the far field.
                var distance = reference * Mathf.Pow(maxDistance / reference, t);
                if (distance >= maxDistance)
                {
                    break;
                }

                var amplitude = Mathf.Pow(reference / distance, AudioTuning.RolloffExponent);

                if (distance > taperStart)
                {
                    // Straight line into silence over the last stretch, so the range
                    // limit is heard as a fade rather than as a cut.
                    var taper = 1f - Mathf.InverseLerp(taperStart, maxDistance, distance);
                    amplitude *= taper;
                }

                keys.Add(new Keyframe(distance / maxDistance, amplitude));
            }

            keys.Add(new Keyframe(1f, 0f));

            // Piecewise-linear tangents rather than smoothed ones. A smoothed curve
            // overshoots at the corner where the flat full-volume segment meets the
            // power law, and an overshoot in a rolloff curve is a source that gets
            // *louder* as you back away from it.
            var array = keys.ToArray();
            for (var i = 0; i < array.Length; i++)
            {
                var previous = i > 0 ? Slope(array[i - 1], array[i]) : 0f;
                var next = i < array.Length - 1 ? Slope(array[i], array[i + 1]) : 0f;
                array[i].inTangent = previous;
                array[i].outTangent = next;
            }

            return new AnimationCurve(array);
        }

        private static float Slope(Keyframe from, Keyframe to)
        {
            var run = to.time - from.time;
            return run > 1e-6f ? (to.value - from.value) / run : 0f;
        }
    }
}
