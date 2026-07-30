#nullable enable

using System;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// A set of interchangeable variants of one sound, picked from without immediate
    /// repetition.
    /// <para>
    /// The generator ships four variants of every footstep and two or three of most
    /// one-shots, and picking uniformly at random from four still repeats about a
    /// quarter of the time — which at §06's sprint cadence is audible within a second
    /// and reads as a broken loop rather than as a floor. Excluding the previous pick
    /// costs nothing and removes the artefact entirely.
    /// </para>
    /// <para>
    /// The randomness is <see cref="UnityEngine.Random"/> on purpose. ARCHITECTURE
    /// bans ambient randomness from Core because a seed has to replay a match exactly;
    /// which of four indistinguishable scuffs is heard changes no rule, is never read
    /// back, and is not part of the replay. Anything that <em>is</em> — the Listener's
    /// error, the misread model — draws from <c>IRandomSource</c> in Core instead.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class AudioClipBank
    {
        [SerializeField]
        private AudioClip?[] clips = Array.Empty<AudioClip?>();

        [Tooltip("Level of this bank relative to its family, 0..1. Use for a variant " +
                 "set that was authored louder or quieter than its siblings.")]
        [SerializeField, Range(0f, 2f)]
        private float gain = 1f;

        private int _previous = -1;

        /// <summary>How many variants this bank holds.</summary>
        public int Count => clips != null ? clips.Length : 0;

        /// <summary>True when there is nothing to play. Callers must check — an empty bank is a wiring mistake, not an error.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>Level of this bank relative to its family, 0..2.</summary>
        public float Gain => gain;

        /// <summary>Replaces the variants. Used by tooling that populates a library from disk.</summary>
        public void SetClips(AudioClip?[] value)
        {
            clips = value ?? Array.Empty<AudioClip?>();
            _previous = -1;
        }

        /// <summary>
        /// A variant other than the last one returned, or null when the bank is empty.
        /// A bank of one returns that one every time; there is nothing else to do, and
        /// refusing to play it would be worse than repeating it.
        /// </summary>
        public AudioClip? Next()
        {
            var count = Count;
            if (count == 0)
            {
                return null;
            }

            if (count == 1)
            {
                _previous = 0;
                return clips[0];
            }

            int index;
            do
            {
                index = UnityEngine.Random.Range(0, count);
            }
            while (index == _previous);

            _previous = index;
            return clips[index];
        }

        /// <summary>The variant at an index, or null when out of range. For tooling and tests.</summary>
        public AudioClip? At(int index)
        {
            return index >= 0 && index < Count ? clips[index] : null;
        }
    }
}
