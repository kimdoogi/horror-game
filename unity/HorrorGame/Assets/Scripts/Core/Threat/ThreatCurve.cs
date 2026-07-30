using HorrorGame.Core.Math;

namespace HorrorGame.Core.Threat
{
    /// <summary>
    /// §07's threat table, as a pure function of elapsed match time.
    /// <para>
    /// This is the whole of "시간 = 위협도": every system that gets harder as the
    /// night wears on reads its number from here instead of keeping a timer of its
    /// own, so there is exactly one place where the night is defined and one place
    /// to look when a match felt wrong.
    /// </para>
    /// <para>
    /// <b>Stepped mechanics, continuous presentation.</b> §07 argues for a clock
    /// over a descent counter partly on the grounds that pressure becomes
    /// "연속적" — continuous — and then states the pressure as five discrete rows.
    /// Both readings are implemented, deliberately, and they are not the same
    /// thing:
    /// </para>
    /// <para>
    /// <see cref="At"/> steps. Nothing in the row is interpolatable in any honest
    /// way: patrol scope is a whole number of zones, "괴물이 출입구를 안다" is a
    /// boolean, and the speeds are a ladder other sections derive geometry from —
    /// §12's 14.4 m single-corner rule is exactly 3 s × 4.8 m/s, so a speed
    /// drifting continuously through 4.8 would make that map rule true for one
    /// instant per match rather than for a tier. A stepped speed is also the only
    /// version a player can perceive: +0.0004 m/s per tick is unfalsifiable, while
    /// a step is a moment you can hear the footsteps change on.
    /// </para>
    /// <para>
    /// <see cref="ThreatScalar"/> is the continuous half, and presentation is where
    /// it belongs — music layers, ambience, HUD tint, telemetry buckets. Nothing
    /// that decides an outcome may read it. The continuity §07 actually needs is
    /// already there without touching the monster at all: time is spent smoothly,
    /// so the cost of "한 층 더 탐색" rises smoothly whatever the current row says.
    /// </para>
    /// </summary>
    public static class ThreatCurve
    {
        /// <summary>
        /// The table. Private and copied out by value — a public array would let
        /// any caller retune §07 at runtime, and a mutated row would be invisible
        /// to every test.
        /// </summary>
        private static readonly ThreatTier[] Tiers = BuildTiers();

        /// <summary>Rows in §07's table. §07's last row is open-ended, so this is also the highest index plus one.</summary>
        public static int TierCount => GameConstants.ThreatTierCount;

        /// <summary>
        /// When the last, open-ended tier begins, seconds — 32 min. §07.
        /// <para>
        /// Also the point at which <see cref="ThreatScalar"/> saturates: §07 stops
        /// escalating here, so continuing to ramp would invent pressure the
        /// document does not describe.
        /// </para>
        /// </summary>
        public static float FinalTierStartSeconds =>
            GameConstants.ThreatTierSeconds * (GameConstants.ThreatTierCount - 1);

        /// <summary>
        /// The threat tier at <paramref name="elapsedSeconds"/> into the match. §07.
        /// <para>
        /// A boundary belongs to the later tier: §07's "0~8분" band is [0, 8), so
        /// at exactly 8:00.000 the monster is already on the 밤 row. Past the last
        /// boundary the last row is returned forever rather than the caller falling
        /// off the end of the table — 동트기 전 has no successor.
        /// </para>
        /// <para>
        /// Negative and NaN inputs resolve to the first tier. A clock that has not
        /// started is 초저녁, and a NaN that silently became "생존 불가 수준" would
        /// be the worst possible failure mode.
        /// </para>
        /// </summary>
        public static ThreatTier At(float elapsedSeconds) => Tier(TierIndexAt(elapsedSeconds));

        /// <summary>
        /// A row by index, clamped into the table. Useful for UI that wants to show
        /// what is coming, and for telemetry that stores an index.
        /// <para>
        /// Clamped against the table itself rather than
        /// <see cref="GameConstants.ThreatTierCount"/>, so if the constant and the
        /// table ever disagree a match degrades to the nearest real row instead of
        /// throwing mid-tick. <c>ThreatTests</c> asserts they agree.
        /// </para>
        /// </summary>
        public static ThreatTier Tier(int index) =>
            Tiers[MathX.Clamp(index, 0, Tiers.Length - 1)];

        /// <summary>
        /// Monster speed at <paramref name="elapsedSeconds"/>, m/s — the seam
        /// ARCHITECTURE.md's diagram names (<c>ThreatCurve.MonsterSpeed → float →
        /// MonsterBrain</c>).
        /// <para>
        /// Absolute, and it replaces <see cref="GameConstants.MonsterBaseSpeed"/>
        /// rather than scaling it. See <see cref="ThreatTier.MonsterSpeed"/> for what
        /// that costs the margins §06 and §12 derive from 4.8.
        /// </para>
        /// </summary>
        public static float MonsterSpeed(float elapsedSeconds) => At(elapsedSeconds).MonsterSpeed;

        /// <summary>
        /// Row index at <paramref name="elapsedSeconds"/>. Same boundary and
        /// saturation rules as <see cref="At"/>.
        /// </summary>
        public static int TierIndexAt(float elapsedSeconds)
        {
            // NaN fails every comparison, so this one guard covers NaN, negatives
            // and a clock that has not been ticked yet.
            if (!(elapsedSeconds > 0f))
            {
                return 0;
            }

            // Checked before the cast: casting +Infinity or a value beyond int
            // range is undefined, and a frame spike or a corrupt delta must not be
            // able to produce a tier index out of nowhere.
            if (elapsedSeconds >= FinalTierStartSeconds)
            {
                return GameConstants.ThreatTierCount - 1;
            }

            return (int)(elapsedSeconds / GameConstants.ThreatTierSeconds);
        }

        /// <summary>
        /// How far through the current tier the match is, 0–1. Presentation only —
        /// see the class remarks on stepping.
        /// <para>
        /// Returns 1 for the open-ended last tier: there is no end to be a fraction
        /// of, and saturating is the honest answer for a night that has stopped
        /// getting worse.
        /// </para>
        /// </summary>
        public static float TierProgress(float elapsedSeconds)
        {
            if (!(elapsedSeconds > 0f))
            {
                return 0f;
            }

            var index = TierIndexAt(elapsedSeconds);
            if (index >= GameConstants.ThreatTierCount - 1)
            {
                return 1f;
            }

            var start = index * GameConstants.ThreatTierSeconds;
            return MathX.InverseLerp(start, start + GameConstants.ThreatTierSeconds, elapsedSeconds);
        }

        /// <summary>
        /// The continuous 0–1 reading of §07's pressure: 0 at match start, 1 once
        /// the last tier begins, linear in between.
        /// <para>
        /// This is §07's "압박: 연속적" made available to the layers that can
        /// actually express it — music, ambience, telemetry. It is deliberately
        /// linear in *time* rather than in monster speed, because time is the thing
        /// §07 calls the currency; blending it against the speed ladder would make
        /// the ramp jump wherever the ladder does, which is precisely the
        /// discontinuity the scalar exists to hide.
        /// </para>
        /// <para>
        /// No mechanic may read this. If a rule needs a number, it needs
        /// <see cref="At"/> — see the class remarks.
        /// </para>
        /// </summary>
        public static float ThreatScalar(float elapsedSeconds)
        {
            if (!(elapsedSeconds > 0f))
            {
                return 0f;
            }

            if (elapsedSeconds >= FinalTierStartSeconds)
            {
                return 1f;
            }

            return MathX.Clamp01(elapsedSeconds / FinalTierStartSeconds);
        }

        /// <summary>
        /// Transcribes §07's five rows. The order is the escalation order and the
        /// index is the 8-minute band, so nothing here may be reordered.
        /// </summary>
        private static ThreatTier[] BuildTiers()
        {
            // 1.0 is the identity multiplier, not a tuned value: before 심야 §07
            // takes nothing away from the flashlight.
            const float noLightPenalty = 1f;

            // §07's 손전등 반경 −30% is introduced by 심야 and, being cumulative,
            // stays on for 새벽 and 동트기 전 as well. See ThreatTier's remarks.
            var lateNightLight = noLightPenalty - GameConstants.LateNightFlashlightPenalty;

            return new[]
            {
                new ThreatTier(
                    0,
                    NightPhase.EarlyEvening,
                    GameConstants.ThreatSpeedEarlyEvening,
                    PatrolScope.FixedZones,
                    GameConstants.ThreatPatrolZonesEarlyEvening,
                    GameConstants.ThreatStandstillChanceFrequent,
                    noLightPenalty,
                    false),

                new ThreatTier(
                    1,
                    NightPhase.Night,
                    GameConstants.ThreatSpeedNight,
                    PatrolScope.FixedZones,
                    GameConstants.ThreatPatrolZonesNight,
                    GameConstants.ThreatStandstillChanceNormal,
                    noLightPenalty,
                    false),

                new ThreatTier(
                    2,
                    NightPhase.LateNight,
                    GameConstants.ThreatSpeedLateNight,
                    PatrolScope.HalfTheMap,
                    0,
                    GameConstants.ThreatStandstillChanceRare,
                    lateNightLight,
                    false),

                new ThreatTier(
                    3,
                    NightPhase.PreDawn,
                    GameConstants.ThreatSpeedPreDawn,
                    PatrolScope.WholeMap,
                    0,
                    GameConstants.ThreatStandstillChanceNone,
                    lateNightLight,
                    true),

                new ThreatTier(
                    4,
                    NightPhase.BeforeSunrise,
                    GameConstants.ThreatSpeedBeforeSunrise,
                    PatrolScope.WholeMap,
                    0,
                    GameConstants.ThreatStandstillChanceNone,
                    lateNightLight,
                    true),
            };
        }
    }
}
