using HorrorGame.Core.Math;

namespace HorrorGame.Core.Presence
{
    /// <summary>
    /// One sample of the 그늘, with the three terms that produced it kept separate.
    /// <para>
    /// The terms are carried rather than collapsed because they mean different things
    /// to different consumers and a single float cannot tell them apart. A density of
    /// zero is "you are safe" to the rules, but to the player it is either "your torch
    /// is on" or "something is standing within 20 m of you", and those are opposite
    /// pieces of news. Presentation reads the terms; the accrual reads only
    /// <see cref="Density"/>.
    /// </para>
    /// </summary>
    public readonly struct PresenceReading
    {
        /// <summary>
        /// How fast the pool fills here, 0–1. The product of the other three terms,
        /// clamped. Zero means nothing is accruing, for whatever reason.
        /// </summary>
        public readonly float Density;

        /// <summary>
        /// §03's term: how far under <see cref="PresenceDensity.SafeLightQuality"/> the
        /// point is, 0–1. This is the number the player is holding a switch over.
        /// </summary>
        public readonly float Darkness;

        /// <summary>
        /// §06's term: how much of the 그늘 the monster's distance leaves standing, 0–1.
        /// Zero within <see cref="GameConstants.PresenceMonsterClearRadius"/>.
        /// </summary>
        public readonly float MonsterClearance;

        /// <summary>§07's term: the night's own multiplier, 0.5 at 초저녁 and 1.0 at 동트기 전.</summary>
        public readonly float Boldness;

        /// <summary>
        /// True when there is darkness here and the monster is thinning it — the tell.
        /// <para>
        /// <b>Not a §13 leak.</b> §13 keeps clue contents and the objective's location on
        /// the host; the monster's approach is not in that set, and this is anyway a
        /// description of something the player is looking at. The dark visibly
        /// withdrawing is a world event with a cause, exactly like footsteps getting
        /// louder — the difference is that §07's 정지 row silences the footsteps and
        /// nothing silences this. That is the point of it.
        /// </para>
        /// </summary>
        public readonly bool ClearedByMonster;

        /// <summary>Builds a reading. Every term is clamped; NaN reads as zero.</summary>
        public PresenceReading(
            float density,
            float darkness,
            float monsterClearance,
            float boldness,
            bool clearedByMonster)
        {
            Density = Safe01(density);
            Darkness = Safe01(darkness);
            MonsterClearance = Safe01(monsterClearance);
            Boldness = Safe01(boldness);
            ClearedByMonster = clearedByMonster;
        }

        /// <summary>Nothing here. A lit room, the 지상, or the monster's own twenty metres.</summary>
        public static PresenceReading None
        {
            get { return default(PresenceReading); }
        }

        /// <summary>True when the pool would fill at this point.</summary>
        public bool IsPresent
        {
            get { return Density > 0f; }
        }

        private static float Safe01(float value) => float.IsNaN(value) ? 0f : MathX.Clamp01(value);
    }
}
