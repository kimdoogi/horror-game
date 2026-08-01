using HorrorGame.Core.Math;
using HorrorGame.Core.Threat;

namespace HorrorGame.Core.Presence
{
    /// <summary>
    /// How thick the 그늘 is at one point, as a pure function of three things that
    /// already exist: how much light is on the point (§03), how far the monster is
    /// (§06), and what row of the night it is (§07).
    /// <para>
    /// <b>This is the whole reason the 그늘 is not a second monster.</b> There is no
    /// position here, no path, no speed, no state machine and no target. It is a
    /// scalar field over the building, and a player's exposure to it is a property of
    /// where they are standing and what they are carrying rather than of anything
    /// deciding to come after them. §01 keeps its horror with one 이길 수 없는 적; a
    /// second pursuer would halve that, because a player who cannot model either
    /// threat stops modelling both. A condition does not compete for that attention —
    /// it competes for the flashlight.
    /// </para>
    /// <para>
    /// <b>Where it is present, and why that scales.</b> Everywhere the light is under
    /// §03's readable threshold and the monster is not. That is a predicate on a place,
    /// not a count of places, so it cannot go stale the way §07's patrol table did when
    /// the building went from one storey to five: a bigger building is more darkness by
    /// construction. §07 hard-codes "1개 구역" and "2개 구역" and therefore shrank to a
    /// fifth of the map; there is no number here that could shrink.
    /// </para>
    /// </summary>
    public static class PresenceDensity
    {
        /// <summary>
        /// Light quality at or above which no 그늘 forms at all. Exactly §03's
        /// <see cref="GameConstants.ClueMinReadableLightQuality"/>.
        /// <para>
        /// The same threshold, deliberately, not a similar one. §03's argument is that
        /// one switch answers two questions — "목표와 위험이 같은 스위치에 걸린다" — and
        /// this makes it three: light enough to read by *is* light enough to be safe in
        /// *is* light enough for the monster to notice. A player never has to learn a
        /// second brightness.
        /// </para>
        /// </summary>
        public static float SafeLightQuality => GameConstants.ClueMinReadableLightQuality;

        /// <summary>
        /// Inside this many metres of the monster there is no 그늘 at all. §06's own
        /// sight range — see <see cref="GameConstants.PresenceMonsterClearRadius"/> for
        /// the three guarantees that fall out of it.
        /// </summary>
        public static float MonsterClearRadius => GameConstants.PresenceMonsterClearRadius;

        /// <summary>Beyond this many metres the monster no longer thins the 그늘.</summary>
        public static float MonsterFringeRadius => GameConstants.PresenceMonsterFringeRadius;

        /// <summary>
        /// The §03 term: how far under readable light this point is, 0–1.
        /// <para>
        /// Linear rather than stepped, because unlike §07's ladder this one is a
        /// quantity the player is continuously adjusting — the fringe of a beam, the
        /// far edge of a 조명탄, a 구역 조명 two rooms away. A step would make "just
        /// inside the light" and "just outside it" feel identical right up until they
        /// did not.
        /// </para>
        /// </summary>
        /// <param name="lightQuality">From <c>LightField.SampleAt</c>. NaN reads as pitch dark, which is the safe direction for a rule that only ever adds pressure.</param>
        public static float DarknessFrom(float lightQuality)
        {
            if (float.IsNaN(lightQuality))
            {
                return 1f;
            }

            if (lightQuality >= SafeLightQuality)
            {
                return 0f;
            }

            if (lightQuality <= 0f)
            {
                return 1f;
            }

            return MathX.Clamp01(1f - (lightQuality / SafeLightQuality));
        }

        /// <summary>
        /// The §06 term: how much of the 그늘 survives the monster being this close,
        /// 0–1. Zero inside <see cref="MonsterClearRadius"/>, one beyond
        /// <see cref="MonsterFringeRadius"/>, smoothstepped between.
        /// <para>
        /// Smoothstepped rather than linear so the withdrawal reads as something
        /// happening rather than as a slider moving: the interesting part of the curve
        /// is the middle, where a player notices the dark thinning without yet being
        /// able to say why.
        /// </para>
        /// <para>
        /// A negative or NaN distance is treated as "on top of you" and clears the
        /// field. If the host cannot say where the monster is, the design's answer is
        /// the one that removes the second pressure rather than the one that adds it.
        /// </para>
        /// </summary>
        public static float MonsterClearanceFrom(float distanceToMonster)
        {
            if (float.IsNaN(distanceToMonster) || distanceToMonster <= MonsterClearRadius)
            {
                return 0f;
            }

            if (distanceToMonster >= MonsterFringeRadius)
            {
                return 1f;
            }

            return MathX.SmoothStep01(
                MathX.InverseLerp(MonsterClearRadius, MonsterFringeRadius, distanceToMonster));
        }

        /// <summary>
        /// The §07 term: how bold the 그늘 is on a given row of the threat table, 0–1.
        /// <see cref="GameConstants.PresenceBoldnessFloor"/> at 초저녁 rising to exactly
        /// 1.0 at 동트기 전.
        /// <para>
        /// Reads the tier <em>index</em> and never <c>ThreatCurve.ThreatScalar</c>: the
        /// scalar is documented as presentation-only and "nothing that decides an
        /// outcome may read it". Being taken is an outcome.
        /// </para>
        /// </summary>
        public static float BoldnessAtTier(int tierIndex)
        {
            var index = MathX.Clamp(tierIndex, 0, GameConstants.ThreatTierCount - 1);
            return MathX.Clamp01(
                GameConstants.PresenceBoldnessFloor + (GameConstants.PresenceBoldnessPerTier * index));
        }

        /// <summary>Convenience: <see cref="BoldnessAtTier"/> for a moment in the match. §07.</summary>
        public static float BoldnessAt(float matchElapsedSeconds) =>
            BoldnessAtTier(ThreatCurve.TierIndexAt(matchElapsedSeconds));

        /// <summary>
        /// Samples the field. The only entry point the rest of the game needs.
        /// </summary>
        /// <param name="lightQuality">Light on the point, 0–1, from <c>LightField</c>.</param>
        /// <param name="distanceToMonster">Metres to the monster. Host truth — see the remarks on <see cref="PresenceReading.ClearedByMonster"/> for why that is not a §13 leak.</param>
        /// <param name="matchElapsedSeconds">§07's clock.</param>
        /// <param name="onSurface">
        /// True on §01's 지상. The surface is the 안전 지대 by definition — §08 puts the
        /// shop, the sale and the 보급소 on it and §02 ends the match there — so no 그늘
        /// forms above ground however dark the far corner of the apron is. Without this
        /// a player could be silenced while standing at the van, which is not a dilemma,
        /// only a nuisance.
        /// </param>
        public static PresenceReading Sample(
            float lightQuality,
            float distanceToMonster,
            float matchElapsedSeconds,
            bool onSurface)
        {
            var darkness = DarknessFrom(lightQuality);
            var clearance = MonsterClearanceFrom(distanceToMonster);
            var boldness = BoldnessAt(matchElapsedSeconds);

            if (onSurface)
            {
                return new PresenceReading(0f, 0f, clearance, boldness, false);
            }

            var density = MathX.Clamp01(darkness * clearance * boldness);

            // "The monster pushed it back" is only worth reporting when there was
            // something to push: a player standing in their own beam sees no withdrawal
            // because there was no 그늘 there to withdraw.
            var clearedByMonster = darkness > 0f && clearance < 1f;

            return new PresenceReading(density, darkness, clearance, boldness, clearedByMonster);
        }

        /// <summary>
        /// Whether the 그늘 is present at all at a point — the predicate the coverage
        /// claim is made with, so "where is it" can be measured on a real map rather
        /// than asserted.
        /// </summary>
        public static bool IsPresent(float lightQuality, float distanceToMonster, bool onSurface) =>
            !onSurface && DarknessFrom(lightQuality) > 0f && MonsterClearanceFrom(distanceToMonster) > 0f;
    }
}
