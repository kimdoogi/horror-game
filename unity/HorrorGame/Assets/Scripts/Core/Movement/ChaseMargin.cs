using System.Globalization;

namespace HorrorGame.Core.Movement
{
    /// <summary>
    /// The one number a chased player actually needs: am I faster than the monster
    /// right now, and by how much.
    /// <para>
    /// §05's entire argument reduces to a comparison — 전진 5.6 is +0.8 on the
    /// monster, 곁눈질 5.32 is +0.52, 후진 3.64 is −1.16 — and the design's
    /// conclusion ("뒷걸음은 괴물보다 느리다. 뒤를 보면 잡힌다") is a statement about
    /// the sign of that difference. Computing it in one place means the HUD, the
    /// telemetry in §13 and the balance tests all agree on what "losing ground"
    /// means instead of each subtracting speeds their own way.
    /// </para>
    /// <para>
    /// The monster's speed is passed in rather than read from
    /// <see cref="GameConstants.MonsterBaseSpeed"/>, because §07 scales it per
    /// time-of-night: a margin computed against the base speed would quietly stop
    /// being true after the first threat tier.
    /// </para>
    /// </summary>
    public readonly struct ChaseMargin
    {
        /// <summary>Builds a margin from two speeds in m/s.</summary>
        /// <param name="playerSpeed">The player's resolved speed, after §05 and §08.</param>
        /// <param name="monsterSpeed">The monster's current speed, from the §07 threat tier.</param>
        public ChaseMargin(float playerSpeed, float monsterSpeed)
        {
            PlayerSpeed = playerSpeed;
            MonsterSpeed = monsterSpeed;
        }

        /// <summary>The player's speed this frame, m/s, with every §05 and §08 multiplier applied.</summary>
        public float PlayerSpeed { get; }

        /// <summary>The monster's speed being compared against, m/s (§06 base, scaled by §07).</summary>
        public float MonsterSpeed { get; }

        /// <summary>
        /// Metres per second the gap is opening. Negative means the monster is
        /// closing — §05's whole point is that this goes negative the moment you
        /// turn far enough to see behind you.
        /// </summary>
        public float MetresPerSecond => PlayerSpeed - MonsterSpeed;

        /// <summary>Strictly pulling away.</summary>
        public bool IsGainingGround => MetresPerSecond > 0f;

        /// <summary>Strictly being caught up with.</summary>
        public bool IsLosingGround => MetresPerSecond < 0f;

        /// <summary>
        /// How the gap changes if this margin is held for
        /// <paramref name="seconds"/>. Negative time is treated as zero rather than
        /// run backwards.
        /// </summary>
        /// <param name="seconds">Duration to project over.</param>
        public float DistanceChangeOver(float seconds)
        {
            if (float.IsNaN(seconds) || seconds <= 0f)
            {
                return 0f;
            }

            return MetresPerSecond * seconds;
        }

        /// <summary>
        /// Seconds before the monster closes <paramref name="gapMetres"/> at this
        /// margin, or <see cref="float.PositiveInfinity"/> while the player is not
        /// losing ground. This is the number the HUD's chase readout wants and the
        /// one §14's validation question 1 ("추격이 재밌는가") is really about.
        /// </summary>
        /// <param name="gapMetres">Current distance to the monster, metres.</param>
        public float SecondsUntilCaught(float gapMetres)
        {
            if (float.IsNaN(gapMetres) || gapMetres <= 0f)
            {
                return 0f;
            }

            var closing = -MetresPerSecond;
            if (closing <= 0f)
            {
                return float.PositiveInfinity;
            }

            return gapMetres / closing;
        }

        /// <summary>
        /// Seconds of holding this margin needed to open <paramref name="metres"/>
        /// of new distance, or <see cref="float.PositiveInfinity"/> if the margin
        /// cannot open it at all. §06 uses exactly this arithmetic to conclude that
        /// 12 m of release distance is unreachable on one 12 s sprint, which is why
        /// aggro release had to become a map problem.
        /// </summary>
        /// <param name="metres">Distance to open, metres.</param>
        public float SecondsToOpen(float metres)
        {
            if (float.IsNaN(metres) || metres <= 0f)
            {
                return 0f;
            }

            if (MetresPerSecond <= 0f)
            {
                return float.PositiveInfinity;
            }

            return metres / MetresPerSecond;
        }

        /// <inheritdoc />
        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "player {0:0.00} m/s vs monster {1:0.00} m/s ({2:+0.00;-0.00} m/s)",
            PlayerSpeed, MonsterSpeed, MetresPerSecond);
    }
}
