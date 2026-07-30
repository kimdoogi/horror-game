using System;
using HorrorGame.Core.Math;

namespace HorrorGame.Core.Light
{
    /// <summary>
    /// The pure arithmetic of §03's light layer: how conspicuous a beam is, how fast light
    /// falls off, and what §03's per-switch charge is actually worth.
    /// <para>
    /// Separated from <see cref="LightField"/> so that every one of these can be checked
    /// without a world, a probe or a match. §03 is the section the rest of the game hangs
    /// off — the objective is locked behind light and the danger is unlocked by it — so the
    /// formulas need to be inspectable on their own.
    /// </para>
    /// <para>
    /// Nothing here introduces a tuned number. Every value is either read from
    /// <see cref="GameConstants"/> or derived from two of them, and where a curve was needed
    /// the normalisation was chosen so that no new constant appeared: that is why the
    /// falloffs are linear in the source's own radius and why
    /// <see cref="BeamConspicuousness"/> saturates against the largest beam §08 can sell
    /// rather than against a cap someone would have to pick.
    /// </para>
    /// </summary>
    public static class LightRules
    {
        // The brightest thing §03 and §08 between them can produce: the 강화 손전등 at full
        // reach. Used only as the denominator of a ratio, so it fixes the scale of
        // BeamConspicuousness without being a tuned value of its own.
        private static readonly float ReferenceFootprint = new LightCone(
            Vec3.Zero,
            Vec3.Forward,
            FlashlightOptics.Upgraded.Range,
            GameConstants.FlashlightHalfAngle).LitFootprint;

        /// <summary>
        /// Footprint of the brightest beam §08 sells, in the units of
        /// <see cref="LightCone.LitFootprint"/>. Exposed so a test can state the scale
        /// rather than rediscover it.
        /// </summary>
        public static float ReferenceBeamFootprint
        {
            get { return ReferenceFootprint; }
        }

        /// <summary>
        /// The dark interval, in seconds, above which switching the light off saves charge
        /// and below which it wastes it.
        /// <para>
        /// §03 charges the flashlight twice — "시간 경과 + 켤 때마다" — and this is where the
        /// two meet. Going dark for D seconds saves
        /// (1 − <see cref="GameConstants.BatteryIdleDrainMultiplier"/>) × D of charge and
        /// costs <see cref="GameConstants.BatterySwitchOnCost"/> to undo, so the switch only
        /// pays past this point. Below it, flicking the light is strictly worse than leaving
        /// it on — which is the behaviour "켤 때마다" exists to produce.
        /// </para>
        /// <para>
        /// At §03's current numbers this is about 1.8 s, and that is small enough to be a
        /// finding rather than a deterrent: see docs/BALANCE-FINDINGS.md. It is computed
        /// rather than written down so a retune of either constant moves it.
        /// </para>
        /// </summary>
        public static float SwitchOffBreakEvenSeconds
        {
            get
            {
                var saved = 1f - GameConstants.BatteryIdleDrainMultiplier;
                if (saved <= 0f)
                {
                    // Idle drain at or above lit drain: going dark never saves anything, so
                    // there is no interval that pays. Positive infinity says that exactly.
                    return float.PositiveInfinity;
                }

                return GameConstants.BatterySwitchOnCost / saved;
            }
        }

        /// <summary>
        /// How much a beam gives its owner away, 0–1, before distance is applied. §03:
        /// "괴물이 잘 본다".
        /// <para>
        /// Grows with the surface the beam paints
        /// (<see cref="LightCone.LitFootprint"/>) and saturates:
        /// <c>f / (f + reference)</c>. Two properties matter and neither survives a
        /// simpler formula. It is <b>strictly</b> increasing in both reach and spread, so
        /// §08 cannot sell a brighter light without selling a cost — the flagship item's
        /// "밝으면 더 잘 보이지만 더 잘 보인다" is a monotonicity claim. And it never reaches
        /// 1, so there is no cap to tune and no beam width at which extra brightness becomes
        /// free.
        /// </para>
        /// <para>
        /// A consequence worth knowing: §07's 심야 −30% shrinks the footprint as well as the
        /// reach, so 심야 makes a player modestly harder to notice while making them much
        /// blinder. That is the closest a literal reading of §07 gets to leaving brightness
        /// and visibility on one dial; the notice *distance* is untouched. See
        /// docs/BALANCE-FINDINGS.md.
        /// </para>
        /// </summary>
        public static float BeamConspicuousness(in LightCone beam)
        {
            var footprint = beam.LitFootprint;
            if (footprint <= 0f || ReferenceFootprint <= 0f)
            {
                return 0f;
            }

            return MathX.Clamp01(footprint / (footprint + ReferenceFootprint));
        }

        /// <summary>
        /// Linear falloff of a light of radius <paramref name="radius"/> at distance
        /// <paramref name="distance"/>: 1 at the source, 0 at the edge.
        /// <para>
        /// Linear rather than inverse-square on purpose. §12 builds the map out of measured
        /// distances — cover every 15–25 m, corridors no longer than 20 m — and a designer
        /// has to be able to read a radius off the map and know what it means. An
        /// inverse-square curve would put almost all of a 18 m zone light's effect in its
        /// first few metres and make §03's "여러 명이 동시에 읽는다" false in practice.
        /// </para>
        /// </summary>
        public static float RadialFalloff(float distance, float radius)
        {
            if (radius <= 0f || float.IsNaN(distance) || float.IsNaN(radius))
            {
                return 0f;
            }

            return MathX.Clamp01(1f - (distance / radius));
        }

        /// <summary>
        /// The single visibility answer. §03's danger half, for the perception system.
        /// <para>
        /// Combines the player's own beam with whatever area light is on them as independent
        /// giveaways — <c>1 − (1 − beam)(1 − area)</c> — so a player reading a clue by
        /// flashlight <i>inside</i> a lit zone is more visible than either alone, and neither
        /// term can mask the other. That is the situation §03 is describing when it says the
        /// goal and the danger share a switch, and it is the one a max() would have
        /// flattered.
        /// </para>
        /// <para>
        /// Cover wins outright: no sight line means a score of 0 whatever is lit. §12's
        /// entire geometry exists to make that reachable, and softening it here would price
        /// the map out of the game.
        /// </para>
        /// <para>
        /// <see cref="PlayerVisibility.NoticeDistance"/> is the larger of the two sources'
        /// reaches, so an unlit player standing in a 구역 조명 is found from
        /// <see cref="GameConstants.ZoneLightRadius"/> and a lit one from their beam's own
        /// <see cref="FlashlightOptics.NoticeDistance"/> — which is the number §08 doubles.
        /// </para>
        /// </summary>
        public static PlayerVisibility Visibility(in LightVisibilityQuery query)
        {
            // Horizontal, like every other range check in the core (Vec3.DistanceFlat):
            // a monster one floor up is not further away for having climbed the stairs, and
            // the sight-line term is what actually rules out another floor.
            var distance = Vec3.DistanceFlat(query.PlayerPosition, query.MonsterPosition);

            var beamLit = query.Beam.IsLit;
            var beamRadius = beamLit ? query.BeamNoticeDistance : 0f;
            var areaRadius = query.AreaLightQuality > 0f ? GameConstants.ZoneLightRadius : 0f;
            var noticeDistance = beamRadius > areaRadius ? beamRadius : areaRadius;

            if (!query.HasLineOfSight || noticeDistance <= 0f)
            {
                return new PlayerVisibility(
                    0f, noticeDistance, distance, query.HasLineOfSight, LightSourceKind.None);
            }

            var beamTerm = beamLit
                ? BeamConspicuousness(query.Beam) * RadialFalloff(distance, beamRadius)
                : 0f;
            var areaTerm = query.AreaLightQuality * RadialFalloff(distance, areaRadius);

            var score = 1f - ((1f - beamTerm) * (1f - areaTerm));

            var dominant = LightSourceKind.None;
            if (beamTerm > 0f || areaTerm > 0f)
            {
                dominant = beamTerm >= areaTerm ? LightSourceKind.Flashlight : query.AreaLightKind;
            }

            return new PlayerVisibility(score, noticeDistance, distance, true, dominant);
        }

        /// <summary>
        /// Guards the relationships §03 and §08 depend on. Called by <c>LightTests</c> and by
        /// the simulator on startup, the same way <see cref="GameConstants.Validate"/> is.
        /// <para>
        /// It is a separate entry point rather than an addition to
        /// <see cref="GameConstants.Validate"/> so that the light system owns its own
        /// invariants; the checks are about how these numbers are *used*, which is this
        /// namespace's business.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">A relationship §03 or §08 relies on no longer holds.</exception>
        public static void Validate()
        {
            Require(
                MathX.Approximately(
                    GameConstants.UpgradedFlashlightDetectionMultiplier,
                    GameConstants.UpgradedFlashlightRangeMultiplier),
                "§08: the 강화 손전등's reward and its price are one number — \"밝으면 더 잘 보이지만 더 잘 "
                + "보인다\". UpgradedFlashlightRangeMultiplier and "
                + "UpgradedFlashlightDetectionMultiplier have drifted apart, so the item now buys "
                + "sight more cheaply (or more dearly) than §08 says. Retune both or change §08.");

            Require(
                GameConstants.FlashlightNoticeDistance > GameConstants.MonsterSightRange,
                "§03: \"괴물이 잘 본다\" is the flashlight's price. If the notice distance is inside "
                + "MonsterSightRange the monster already saw the body, and switching the light on "
                + "costs nothing at all.");

            Require(
                GameConstants.FlareIgniteNoiseLevel > GameConstants.ListenerSelfNoiseThreshold,
                "§08/§04: the 조명탄's price is \"소리를 낸다\". Below ListenerSelfNoiseThreshold a "
                + "청음사 could strike one without cutting their own feed (§04: 자기가 소리를 내면 못 "
                + "듣는다), and the flare would be a free 정비공.");

            Require(
                GameConstants.FlareIgniteNoiseLevel <= GameConstants.EngineerTrapNoiseLevel,
                "§04: the 소음 함정's job is to be the loudest thing in the zone. A flare louder than "
                + "the trap makes the Engineer's trap redundant.");

            // On-axis, LightCone.QualityAt falls off as 1 − d/R, so the radius a clue is
            // still *readable* at is R × (1 − ClueMinReadableLightQuality), not R.
            var lateNightReadableRadius =
                GameConstants.FlashlightRange
                * (1f - GameConstants.LateNightFlashlightPenalty)
                * (1f - GameConstants.ClueMinReadableLightQuality);

            Require(
                lateNightReadableRadius > GameConstants.ClueReadRange,
                "§03/§07: after 심야's −30% the issued flashlight must still light a clue brightly "
                + "enough to read at ClueReadRange, or 심야 does not make reading harder — it makes it "
                + "impossible with the issued kit, and §03's three-clue chain cannot finish without "
                + "§08's upgrade or a 정비공.");

            Require(
                GameConstants.ZoneLightRadius > GameConstants.ClueReadRange,
                "§03: a 구역 조명 exists so \"여러 명이 동시에 읽는다\". A radius no bigger than one "
                + "reader's reach would not let two people stand at the same mark.");

            Require(
                GameConstants.BatterySecondsPerCell > GameConstants.BatterySwitchOnCost,
                "§03: one switch-on must not consume a whole cell, or \"켤 때마다\" stops being a cost "
                + "and becomes the only cost.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("LightRules.Validate failed — " + message);
            }
        }
    }
}
