using System;
using System.Collections.Generic;

namespace HorrorGame.Core.Session
{
    /// <summary>
    /// Seeded randomness for the rules layer.
    /// <para>
    /// The core never touches <see cref="System.Random"/> statically or
    /// <c>UnityEngine.Random</c>. Two reasons, both load-bearing: a replay of the
    /// same seed must produce the same match so a balance bug can be reproduced,
    /// and §13 puts clue content and objective placement under host authority —
    /// they are generated once, on the host, from a seed the clients never see.
    /// </para>
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>A value in [0, 1).</summary>
        float NextFloat();

        /// <summary>A value in [<paramref name="min"/>, <paramref name="max"/>).</summary>
        float NextFloat(float min, float max);

        /// <summary>An integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>True with probability <paramref name="probability"/>.</summary>
        bool Chance(float probability);
    }

    /// <summary>
    /// The default <see cref="IRandomSource"/>: a deterministic xorshift128+
    /// stream that produces identical sequences on every platform.
    /// <para>
    /// <see cref="System.Random"/> is deliberately avoided — its algorithm is not
    /// contractual across .NET versions, so a seed captured from a player's
    /// crash report could replay differently on the machine investigating it.
    /// </para>
    /// </summary>
    public sealed class DeterministicRandom : IRandomSource
    {
        private ulong _s0;
        private ulong _s1;

        /// <summary>The seed this stream was created from. Log it with every match summary.</summary>
        public int Seed { get; }

        /// <summary>Creates a stream from an integer seed.</summary>
        public DeterministicRandom(int seed)
        {
            Seed = seed;

            // SplitMix64 the seed into the two state words. Seeding both words
            // from the raw value would leave a low-entropy state that takes many
            // draws to mix, which shows up as correlated early rolls — and the
            // early rolls are exactly the ones that place the objective.
            var x = (ulong)seed * 0x9E3779B97F4A7C15UL;
            _s0 = SplitMix64(ref x);
            _s1 = SplitMix64(ref x);

            if (_s0 == 0 && _s1 == 0)
            {
                _s1 = 0x9E3779B97F4A7C15UL;
            }
        }

        /// <inheritdoc />
        public float NextFloat()
        {
            // Take the top 24 bits: exactly the mantissa a float can represent,
            // so every result is distinct and uniformly spaced in [0, 1).
            return (NextULong() >> 40) * (1f / 16777216f);
        }

        /// <inheritdoc />
        public float NextFloat(float min, float max) => min + ((max - min) * NextFloat());

        /// <inheritdoc />
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            var range = (ulong)(maxExclusive - (long)minInclusive);
            return (int)(minInclusive + (long)(NextULong() % range));
        }

        /// <inheritdoc />
        public bool Chance(float probability)
        {
            if (probability <= 0f)
            {
                return false;
            }

            return probability >= 1f || NextFloat() < probability;
        }

        /// <summary>Fisher-Yates shuffle in place.</summary>
        public void Shuffle<T>(IList<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = NextInt(0, i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        private ulong NextULong()
        {
            var s1 = _s0;
            var s0 = _s1;
            var result = s0 + s1;
            _s0 = s0;
            s1 ^= s1 << 23;
            _s1 = s1 ^ s0 ^ (s1 >> 18) ^ (s0 >> 5);
            return result;
        }

        private static ulong SplitMix64(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
