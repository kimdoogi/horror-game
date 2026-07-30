using System;
using HorrorGame.Core;
using HorrorGame.Core.Light;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;
using HorrorGame.Core.Threat;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §03's light layer, asserted against the argument §03 makes rather than against the
    /// implementation.
    /// <para>
    /// §03 does something unusual: it makes one switch answer two questions. Light is the
    /// only thing that unlocks the objective ("어둠 = 목표의 잠금장치") and the main thing
    /// that gets you killed ("괴물이 잘 본다"), and the section's conclusion is that this is
    /// deliberate — "목표와 위험이 같은 스위치에 걸린다." Every test here is really the same
    /// test: that the two halves cannot be separated. The failure mode this suite exists to
    /// catch is somebody making light cheaper on one side only, which always looks like a
    /// small fix and always deletes the game's central dilemma.
    /// </para>
    /// </summary>
    [TestFixture]
    public class LightTests
    {
        // ====================================================================
        // Fixtures.
        // ====================================================================

        /// <summary>
        /// A hand-written <see cref="IWorldProbe"/>. Answers are fields so a test can state
        /// the world in a line, including the awkward worlds: no sight line, nothing
        /// navigable, a host that reports its own lit areas.
        /// </summary>
        private sealed class FakeProbe : IWorldProbe
        {
            public bool LineOfSight = true;
            public float PathDistance = 10f;
            public FloorMaterial Floor = FloorMaterial.Concrete;
            public int Zone;
            public bool Lit;
            public Func<Vec3, Vec3> Snap;

            public FakeProbe()
            {
                Snap = p => p;
            }

            public bool HasLineOfSight(Vec3 from, Vec3 to)
            {
                return LineOfSight;
            }

            public float NavigableDistance(Vec3 from, Vec3 to)
            {
                return PathDistance;
            }

            public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
            {
                next = to;
                return !float.IsInfinity(PathDistance);
            }

            public FloorMaterial SampleFloor(Vec3 position)
            {
                return Floor;
            }

            public int ZoneIdAt(Vec3 position)
            {
                return Zone;
            }

            public Vec3 SnapToNavigable(Vec3 desired)
            {
                return Snap(desired);
            }

            public bool IsAreaLit(Vec3 position)
            {
                return Lit;
            }
        }

        private static int StepsFor(float seconds)
        {
            return (int)((seconds / GameConstants.FixedStep) + 0.5f);
        }

        /// <summary>Steps a flashlight at <see cref="GameConstants.FixedStep"/>, the way the host does.</summary>
        private static void Hold(FlashlightState light, float seconds)
        {
            var steps = StepsFor(seconds);
            for (var i = 0; i < steps; i++)
            {
                light.Tick(GameConstants.FixedStep);
            }
        }

        private static void Hold(LightField field, float seconds)
        {
            var steps = StepsFor(seconds);
            for (var i = 0; i < steps; i++)
            {
                field.Tick(GameConstants.FixedStep);
            }
        }

        /// <summary>§07's 심야 row, as the float this system is allowed to know about.</summary>
        private static float LateNightMultiplier
        {
            get { return 1f - GameConstants.LateNightFlashlightPenalty; }
        }

        /// <summary>A lit flashlight on a full cell, ready to be asked about.</summary>
        private static FlashlightState LitFlashlight(bool upgraded)
        {
            var light = new FlashlightState(new BatteryState(), upgraded);
            Assert.That(light.TryTurnOn(), Is.True, "A full cell must be able to light the beam.");
            return light;
        }

        // ====================================================================
        // The invariants §03 and §08 rest on.
        // ====================================================================

        /// <summary>The relationship checks in <see cref="LightRules.Validate"/> must hold.</summary>
        [Test]
        public void LightRules_Validate_Passes()
        {
            Assert.DoesNotThrow(LightRules.Validate);
        }

        // ====================================================================
        // §08 — the 강화 손전등. "밝으면 더 잘 보이지만 더 잘 보인다."
        // ====================================================================

        /// <summary>
        /// §08 calls the 강화 손전등 "이 목록의 대표작" and states its reward and its price in
        /// one breath — 반경 2배 / 괴물이 2배 멀리서 본다. The item is only a dilemma while those
        /// are the same number, so this asserts they are literally the same number and not two
        /// that currently agree.
        /// </summary>
        [Test]
        public void UpgradedFlashlight_BenefitAndCost_AreOneNumber()
        {
            var upgraded = FlashlightOptics.Upgraded;

            Assert.That(upgraded.RangeMultiplier, Is.EqualTo(upgraded.DetectionMultiplier),
                "§08 buys reach and pays with visibility at the same rate. If these can differ, the "
                + "flagship item can be buffed without being priced.");

            Assert.That(upgraded.UpgradeFactor, Is.EqualTo(2f).Within(1e-4f),
                "§08: 반경 2배.");

            Assert.That(GameConstants.UpgradedFlashlightDetectionMultiplier,
                Is.EqualTo(GameConstants.UpgradedFlashlightRangeMultiplier).Within(1e-4f),
                "§08 states the factor twice and FlashlightOptics reads it once. If the two constants "
                + "drift apart, the unread one is a lie — retune both or change §08.");
        }

        /// <summary>
        /// The ratio test: whatever the upgrade does to the beam it must do to the distance the
        /// monster notices it. Written as a ratio rather than as two absolute values so it keeps
        /// working after §03's base numbers are retuned.
        /// </summary>
        [Test]
        public void UpgradedFlashlight_ScalesSightAndExposure_Together()
        {
            var standard = FlashlightOptics.Standard;
            var upgraded = FlashlightOptics.Upgraded;

            var benefit = upgraded.Range / standard.Range;
            var cost = upgraded.NoticeDistance / standard.NoticeDistance;

            Assert.That(benefit, Is.EqualTo(cost).Within(1e-4f),
                "§08's whole sentence is that these are the same ratio. A benefit of 2× against a "
                + "cost of 1.5× would make the 250-credit item strictly good, and §10's adoption "
                + "criterion — \"이 기능은 이득과 위험을 교환하는가?\" — would stop being met.");

            Assert.That(upgraded.HalfAngleDegrees, Is.EqualTo(standard.HalfAngleDegrees),
                "§08 buys reach, not spread. A wider beam would make clue reading easier without "
                + "making the reader more conspicuous — the one trade §03 does not allow.");
        }

        /// <summary>
        /// Both halves in play at once, through the real query: at a distance between the two
        /// notice distances the upgrade is what gets you found, and at a distance between the two
        /// readable radii it is what lets you read.
        /// </summary>
        [Test]
        public void UpgradedFlashlight_IsBothSeenFurtherAndSeesFurther()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);

            var between = (FlashlightOptics.Standard.NoticeDistance
                           + FlashlightOptics.Upgraded.NoticeDistance) * 0.5f;
            var monsterAt = new Vec3(0f, 0f, between);

            var standard = LitFlashlight(false);
            var upgradedLight = LitFlashlight(true);

            var standardVisibility = field.VisibilityOf(
                Vec3.Zero, Vec3.Forward, standard, 1f, monsterAt);
            var upgradedVisibility = field.VisibilityOf(
                Vec3.Zero, Vec3.Forward, upgradedLight, 1f, monsterAt);

            Assert.That(standardVisibility.IsNoticed, Is.False,
                "The issued flashlight must not give a player away from beyond its own notice "
                + "distance, or §08 would have nothing left to sell.");
            Assert.That(upgradedVisibility.IsNoticed, Is.True, "§08: 괴물이 2배 멀리서 본다.");
            Assert.That(upgradedVisibility.Score, Is.GreaterThan(standardVisibility.Score));

            // A mark past the issued light's readable radius but inside the upgrade's.
            var readableRadius =
                GameConstants.FlashlightRange * (1f - GameConstants.ClueMinReadableLightQuality);
            var mark = new Vec3(0f, 0f, readableRadius * 1.5f);

            Assert.That(
                field.CanReadAt(mark, standard.ConeAt(Vec3.Zero, Vec3.Forward, 1f)),
                Is.False);
            Assert.That(
                field.CanReadAt(mark, upgradedLight.ConeAt(Vec3.Zero, Vec3.Forward, 1f)),
                Is.True,
                "§08: 반경 2배. The reward has to be reachable or the price is unpaid for nothing.");
        }

        /// <summary>
        /// Records a contradiction between §08 and §12 that neither section acknowledges.
        /// <para>
        /// §08 prices the 강화 손전등 at "괴물이 2배 멀리서 본다", which doubles the notice
        /// distance to 50 m. §12 then makes that distance unreachable: it breaks line of sight
        /// every <see cref="GameConstants.LineOfSightBreakSpacingMin"/>–<see cref="GameConstants.LineOfSightBreakSpacingMax"/> m,
        /// caps a straight corridor at <see cref="GameConstants.MaxStraightCorridor"/> m, and
        /// caps a zone diagonal at <see cref="GameConstants.ZoneDiagonalMax"/> m. A legal map
        /// therefore has no 50 m sight line at all, so the extra 25 m the upgrade is priced with
        /// can never be collected — while the benefit (12 → 24 m of beam) is collected in full,
        /// because 24 m is inside the sight lines §12 guarantees.
        /// </para>
        /// <para>
        /// So §08's flagship dilemma item is, on a §12-legal map, close to a strict upgrade. See
        /// docs/BALANCE-FINDINGS.md; this test pins the arithmetic so a later retune has to
        /// confront it.
        /// </para>
        /// </summary>
        [Test]
        public void UpgradedFlashlight_NoticeDistance_ExceedsAnySightLineTheMapCanProvide()
        {
            Assert.That(FlashlightOptics.Standard.NoticeDistance,
                Is.LessThanOrEqualTo(GameConstants.LineOfSightBreakSpacingMax),
                "The issued light's price is payable: 25 m is a sight line §12 has to provide.");

            Assert.That(FlashlightOptics.Upgraded.NoticeDistance,
                Is.GreaterThan(GameConstants.LineOfSightBreakSpacingMax),
                "§08's doubled price needs a sight line §12 forbids. If this ever stops being true "
                + "the finding is closed and docs/BALANCE-FINDINGS.md must be updated.");

            Assert.That(FlashlightOptics.Upgraded.NoticeDistance,
                Is.GreaterThan(GameConstants.ZoneDiagonalMax),
                "It is longer than the largest zone §12 allows, so the extra reach is not even "
                + "collectable within one room.");

            Assert.That(FlashlightOptics.Upgraded.Range,
                Is.LessThanOrEqualTo(GameConstants.LineOfSightBreakSpacingMax),
                "The benefit, unlike the price, fits inside §12's geometry — which is exactly why "
                + "the trade is lopsided.");
        }

        // ====================================================================
        // §03 — 배터리가 떨어지면 단서를 읽을 수 없다.
        // ====================================================================

        /// <summary>
        /// §03's load-bearing consequence: "배터리가 떨어지면 단서를 읽을 수 없다." This is what
        /// converts §03's darkness lock into resource pressure and answers both "왜 나와야
        /// 하는가" and "왜 다시 들어가는가" at once, so it has to be literally true — not
        /// "harder", not "dimmer".
        /// </summary>
        [Test]
        public void DeadBattery_MakesClueReadingImpossible()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);
            var mark = new Vec3(0f, 0f, GameConstants.ClueReadRange);

            var alive = LitFlashlight(false);
            Assert.That(field.CanReadAt(mark, alive.ConeAt(Vec3.Zero, Vec3.Forward, 1f)), Is.True,
                "Control: a working flashlight held on a mark at ClueReadRange must read.");

            var flat = new FlashlightState(new BatteryState(0f, 0));
            Assert.That(flat.TryTurnOn(), Is.False, "An empty cell cannot light the beam.");
            Assert.That(flat.Battery.IsDead, Is.True, "No cell, no spares — §03's reason to surface.");

            var dark = flat.ConeAt(Vec3.Zero, Vec3.Forward, 1f);
            var sample = field.SampleAt(mark, dark);

            Assert.That(sample.Quality, Is.EqualTo(0f), "A dead battery lights nothing at all.");
            Assert.That(sample.Source, Is.EqualTo(LightSourceKind.None));
            Assert.That(sample.Quality, Is.LessThan(GameConstants.ClueMinReadableLightQuality),
                "This is the exact threshold ClueReader interrupts on, which is how §03's lock is "
                + "one fact instead of two systems' opinions.");
            Assert.That(sample.IsReadable, Is.False);
            Assert.That(field.CanReadAt(mark, dark), Is.False);
        }

        /// <summary>
        /// The cell running out mid-read must be a transition the host can see exactly once, at
        /// any step size. §03 makes going dark the interesting event — it is when a read dies —
        /// and a frame spike must not swallow it.
        /// </summary>
        [Test]
        public void BatteryRunningOut_ForcesTheLightOff_AndReportsItOnce()
        {
            var light = LitFlashlight(false);

            light.Tick(GameConstants.BatterySecondsPerCell * 10f);

            Assert.That(light.Battery.Charge, Is.EqualTo(0f),
                "Charge floors at empty rather than going negative, however long the step was.");
            Assert.That(light.IsOn, Is.False, "The switch cannot stay on with nothing behind it.");
            Assert.That(light.IsLit, Is.False);
            Assert.That(light.WentDarkThisTick, Is.True, "The transition happened on this step.");

            light.Tick(GameConstants.FixedStep);
            Assert.That(light.WentDarkThisTick, Is.False, "…and is reported once, not latched forever.");

            Assert.That(light.TryTurnOn(), Is.False,
                "Swapping is the only way back, and §03 puts the generator on the surface.");
        }

        /// <summary>
        /// A cell swap must not silently relight the beam. §03 charges "켤 때마다", so coming back
        /// from a dead battery costs another switch-on — which is what makes swapping mid-corridor
        /// a decision rather than a formality.
        /// </summary>
        [Test]
        public void SwappingACell_DoesNotRelightTheBeamByItself()
        {
            var battery = new BatteryState(0f, 1);
            var light = new FlashlightState(battery);

            Assert.That(light.TryTurnOn(), Is.False);
            Assert.That(battery.TrySwapCell(out var discarded), Is.True);
            Assert.That(discarded, Is.EqualTo(0f), "There was nothing left to throw away.");

            Assert.That(light.IsOn, Is.False, "The swap restores power, not the switch.");
            Assert.That(light.TryTurnOn(), Is.True);
            Assert.That(battery.SwitchOnCount, Is.EqualTo(1),
                "The press against a dead cell took nothing because there was nothing to take; the "
                + "press after the swap paid §03's \"켤 때마다\" charge in full.");
            Assert.That(battery.Charge,
                Is.EqualTo(GameConstants.BatterySecondsPerCell - GameConstants.BatterySwitchOnCost)
                    .Within(1e-3f));
        }

        /// <summary>
        /// §03's resupply is the 지상 발전기, on the surface, so a cell pulled out underground has
        /// nowhere to go. The discarded charge is the price of the safe play and has to be
        /// visible, or the simulator cannot tell §16-5's cell life apart from players hedging
        /// against it.
        /// </summary>
        [Test]
        public void SwappingAPartialCell_ThrowsTheRemainderAway()
        {
            var battery = new BatteryState(0.5f, 1);
            var half = GameConstants.BatterySecondsPerCell * 0.5f;

            Assert.That(battery.Charge, Is.EqualTo(half).Within(1e-3f));
            Assert.That(battery.ChargeFraction, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(battery.TotalRemainingSeconds,
                Is.EqualTo(half + GameConstants.BatterySecondsPerCell).Within(1e-3f));

            Assert.That(battery.TrySwapCell(out var discarded), Is.True);

            Assert.That(discarded, Is.EqualTo(half).Within(1e-3f));
            Assert.That(battery.WastedSeconds, Is.EqualTo(half).Within(1e-3f),
                "Half a cell is over 100 s of light and one 회중시계 of credits (§08). Swapping early "
                + "has to cost something or there is no decision.");
            Assert.That(battery.Charge, Is.EqualTo(GameConstants.BatterySecondsPerCell).Within(1e-3f));
            Assert.That(battery.SpareCells, Is.EqualTo(0));
        }

        /// <summary>An empty bag must leave the installed cell exactly as it was.</summary>
        [Test]
        public void SwappingWithNoSpare_ChangesNothing()
        {
            var battery = new BatteryState(0.25f, 0);
            var before = battery.Charge;

            Assert.That(battery.TrySwapCell(out var discarded), Is.False);
            Assert.That(discarded, Is.EqualTo(0f));
            Assert.That(battery.Charge, Is.EqualTo(before));
            Assert.That(battery.WastedSeconds, Is.EqualTo(0f));
        }

        /// <summary>
        /// The generator tops a cell up without creating one. That is why §07's "나가서 배터리
        /// 교체 ~1분" is worth paying even with an empty wallet — §08 is the only source of a
        /// spare, but the surface is a free refill of what you already hold.
        /// </summary>
        [Test]
        public void Recharging_TopsUpTheCell_ButCreatesNoCells()
        {
            var battery = new BatteryState(0.1f, 0);

            battery.Recharge();

            Assert.That(battery.Charge, Is.EqualTo(GameConstants.BatterySecondsPerCell).Within(1e-3f));
            Assert.That(battery.SpareCells, Is.EqualTo(0));
            Assert.That(battery.IsDead, Is.False);
        }

        /// <summary>
        /// §03 charges the switch, not the result. A player who presses `F` with less than
        /// <see cref="GameConstants.BatterySwitchOnCost"/> left spends it and stays dark, and the
        /// press still counts.
        /// </summary>
        [Test]
        public void SwitchingOnWithAlmostNothingLeft_SpendsItAnyway()
        {
            var fraction = GameConstants.BatterySwitchOnCost * 0.5f / GameConstants.BatterySecondsPerCell;
            var battery = new BatteryState(fraction, 0);
            var light = new FlashlightState(battery);

            Assert.That(light.TryTurnOn(), Is.False);
            Assert.That(light.IsLit, Is.False);
            Assert.That(battery.Charge, Is.EqualTo(0f));
            Assert.That(battery.SwitchOnCount, Is.EqualTo(1),
                "The charge was made. §03 lists \"켤 때마다\" as a consumption condition, not a "
                + "conditional one.");
        }

        // ====================================================================
        // §03 — 시간 경과 + 켤 때마다.
        // ====================================================================

        /// <summary>
        /// §03 charges the flashlight for existing as well as for shining — "시간 경과" comes
        /// first in its table — but not at the same rate, or leaving the light off would buy
        /// nothing.
        /// </summary>
        [Test]
        public void BatteryDrains_FasterWhileLit_ButAlwaysDrains()
        {
            var lit = new BatteryState();
            var dark = new BatteryState();
            var window = GameConstants.BatterySecondsPerCell * 0.25f;

            lit.Tick(window, true);
            dark.Tick(window, false);

            var litSpend = GameConstants.BatterySecondsPerCell - lit.Charge;
            var darkSpend = GameConstants.BatterySecondsPerCell - dark.Charge;

            Assert.That(litSpend, Is.EqualTo(window).Within(1e-3f),
                "A lit second costs a second: that is what BatterySecondsPerCell means.");
            Assert.That(darkSpend, Is.GreaterThan(0f), "§03: 시간 경과 costs on its own.");
            Assert.That(darkSpend, Is.LessThan(litSpend),
                "…but less, or §10's \"손전등을 켠다 / 배터리를 쓴다\" would not be a trade.");
            Assert.That(darkSpend,
                Is.EqualTo(window * GameConstants.BatteryIdleDrainMultiplier).Within(1e-3f));
        }

        /// <summary>
        /// §03's "켤 때마다" exists to make flicking the light worse than committing to it. Tested
        /// the way a player would experience it: for the same amount of light delivered, strobing
        /// costs far more charge.
        /// </summary>
        [Test]
        public void RapidToggling_CostsFarMoreThanLeavingItOn_ForTheSameLight()
        {
            var interval = GameConstants.BatterySwitchOnCost;
            const int cycles = 6;
            var litSeconds = interval * cycles;

            var committed = new FlashlightState(new BatteryState());
            Assert.That(committed.TryTurnOn(), Is.True);
            Hold(committed, litSeconds);

            var strobed = new FlashlightState(new BatteryState());
            for (var i = 0; i < cycles; i++)
            {
                Assert.That(strobed.TryTurnOn(), Is.True);
                Hold(strobed, interval);
                strobed.TurnOff();
                Hold(strobed, interval);
            }

            var committedSpend = GameConstants.BatterySecondsPerCell - committed.Battery.Charge;
            var strobedSpend = GameConstants.BatterySecondsPerCell - strobed.Battery.Charge;

            Assert.That(committed.Battery.SwitchOnCount, Is.EqualTo(1));
            Assert.That(strobed.Battery.SwitchOnCount, Is.EqualTo(cycles));

            Assert.That(strobedSpend, Is.GreaterThan(committedSpend * 1.5f),
                "Both runs delivered the same " + litSeconds + " s of light. If strobing were not "
                + "clearly worse, §03's \"켤 때마다\" would be decorative and the optimal play would "
                + "be to keep the beam off except for single frames — which also happens to defeat "
                + "the monster's perception of it.");
        }

        /// <summary>
        /// Records a weakness in §03's per-switch charge that §03 does not acknowledge.
        /// <para>
        /// Going dark for D seconds saves (1 − <see cref="GameConstants.BatteryIdleDrainMultiplier"/>) × D
        /// of charge and costs <see cref="GameConstants.BatterySwitchOnCost"/> to undo, so the
        /// switch pays for itself after 1.5 / 0.85 ≈ 1.76 s. That is shorter than a single clue
        /// read (<see cref="GameConstants.ClueReadSeconds"/> = 2.5 s), so the cheapest way to read
        /// three clues is to switch the light off between them — and a player who cycles the beam
        /// on a two-second rhythm halves their visibility (§03: 괴물이 잘 본다) while *saving*
        /// battery. §03 presents "켤 때마다" as a co-equal drain axis alongside "시간 경과"; at 1.5 s
        /// against a 210 s cell it is worth 0.7% of a cell per press and deters nothing beyond a
        /// two-second rhythm.
        /// </para>
        /// <para>
        /// See docs/BALANCE-FINDINGS.md. Implemented as documented; this pins the consequence.
        /// </para>
        /// </summary>
        [Test]
        public void SwitchOnCost_StopsDeterringAfterLessThanTwoSeconds()
        {
            Assert.That(LightRules.SwitchOffBreakEvenSeconds, Is.EqualTo(1.7647f).Within(0.001f),
                "1.5 / (1 − 0.15). If either constant moves, this number moves with it.");

            Assert.That(LightRules.SwitchOffBreakEvenSeconds, Is.LessThan(GameConstants.ClueReadSeconds),
                "The break-even is shorter than one clue read, so switching off between reads is "
                + "strictly correct play. If \"켤 때마다\" is meant to discourage strobing, "
                + "BatterySwitchOnCost has to exceed ClueReadSeconds × (1 − BatteryIdleDrainMultiplier).");

            var pressesPerCell = GameConstants.BatterySecondsPerCell / GameConstants.BatterySwitchOnCost;
            Assert.That(pressesPerCell, Is.GreaterThan(100f),
                "It takes over a hundred presses to burn a cell on switch-on charges alone, so this "
                + "axis contributes ~1% of realistic consumption despite §03 listing it as one of two.");
        }

        /// <summary>Zero and negative steps must change nothing rather than being treated as time.</summary>
        [Test]
        public void ZeroAndNegativeSteps_DrainNothing()
        {
            var light = LitFlashlight(false);
            var after = light.Battery.Charge;

            light.Tick(0f);
            light.Tick(-GameConstants.BatterySecondsPerCell);
            light.Tick(float.NaN);

            Assert.That(light.Battery.Charge, Is.EqualTo(after),
                "A paused host, a rewound clock and a NaN delta must all be no-ops. A negative step "
                + "that recharged the cell would make §03's resource pressure defeatable by "
                + "stuttering.");
            Assert.That(light.IsLit, Is.True);
        }

        // ====================================================================
        // §07 — 심야: 손전등 반경 −30%.
        // ====================================================================

        /// <summary>
        /// §07's 심야 row takes 30% off the flashlight radius from 16 minutes, and it composes
        /// with §08's upgrade rather than replacing it — a team that bought the 강화 손전등 keeps
        /// its advantage into the worse tiers.
        /// </summary>
        [Test]
        public void LateNight_CutsFlashlightRadiusByThirtyPercent()
        {
            Assert.That(FlashlightOptics.Standard.RangeAt(LateNightMultiplier),
                Is.EqualTo(8.4f).Within(0.01f), "§07: 12 m − 30%.");

            Assert.That(FlashlightOptics.Upgraded.RangeAt(LateNightMultiplier),
                Is.EqualTo(16.8f).Within(0.01f),
                "§08's upgrade multiplies and §07's penalty multiplies: 12 × 2 × 0.7. If one "
                + "overwrote the other, buying the flashlight would stop mattering exactly when the "
                + "night gets dangerous.");

            var light = LitFlashlight(false);
            Assert.That(light.RangeFor(LateNightMultiplier),
                Is.LessThan(light.RangeFor(1f)));
        }

        /// <summary>
        /// The penalty must actually start at §07's 16-minute mark, read through the threat
        /// system's own float rather than a number copied into this system.
        /// </summary>
        [Test]
        public void LateNightPenalty_StartsAtSixteenMinutes()
        {
            var beforeLateNight = ThreatCurve.At(GameConstants.ThreatTierSeconds * 2f - 1f);
            var lateNight = ThreatCurve.At(GameConstants.ThreatTierSeconds * 2f);

            Assert.That(FlashlightOptics.Standard.RangeAt(beforeLateNight.FlashlightRangeMultiplier),
                Is.EqualTo(GameConstants.FlashlightRange).Within(1e-3f),
                "§07 takes nothing from the flashlight before 심야.");

            Assert.That(FlashlightOptics.Standard.RangeAt(lateNight.FlashlightRangeMultiplier),
                Is.LessThan(GameConstants.FlashlightRange),
                "§07: the −30% arrives at 16 min.");
            Assert.That(FlashlightOptics.Standard.RangeAt(lateNight.FlashlightRangeMultiplier),
                Is.EqualTo(8.4f).Within(0.01f));
        }

        /// <summary>
        /// A missing or broken tier multiplier must put the player in the dark, not hand every
        /// later comparison a NaN. §07's clock is only readable on the surface, so a host that has
        /// lost track of it is a reachable state.
        /// </summary>
        [Test]
        public void BrokenTierMultiplier_ProducesDarkness_NotNaN()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);
            var light = LitFlashlight(false);
            var mark = new Vec3(0f, 0f, GameConstants.ClueReadRange);

            foreach (var bad in new[] { float.NaN, -1f, 0f })
            {
                var cone = light.ConeAt(Vec3.Zero, Vec3.Forward, bad);
                Assert.That(cone.IsLit, Is.False, "Multiplier " + bad + " must not light anything.");
                Assert.That(field.SampleAt(mark, cone).Quality, Is.EqualTo(0f));
                Assert.That(light.RangeFor(bad), Is.EqualTo(0f));
            }
        }

        /// <summary>
        /// Records a disagreement between §07 and §08 that neither section acknowledges.
        /// <para>
        /// §08's flagship argument is that brightness and visibility are one dial — "밝으면 더 잘
        /// 보이지만 더 잘 보인다". §07 then turns the radius dial down 30% at 심야 and says nothing
        /// about visibility. Read literally, that is what is implemented: the notice distance is
        /// §08's alone and 심야 does not shorten it. The alternative reading — that a dimmer beam
        /// is also a quieter one — would make 심야 partly a stealth *bonus*, which contradicts
        /// §07's own thesis that the pressure only rises.
        /// </para>
        /// <para>
        /// The split is not clean either way: the beam's footprint does shrink, so 심야 makes a
        /// player measurably less conspicuous inside the same unchanged notice distance. Options
        /// and arithmetic are in docs/BALANCE-FINDINGS.md; this pins the behaviour.
        /// </para>
        /// </summary>
        [Test]
        public void LateNight_DoesNotShortenTheDistanceTheMonsterNoticesTheBeam()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);
            var light = LitFlashlight(false);
            var monsterAt = new Vec3(0f, 0f, GameConstants.FlashlightNoticeDistance * 0.5f);

            var evening = field.VisibilityOf(Vec3.Zero, Vec3.Forward, light, 1f, monsterAt);
            var deepNight = field.VisibilityOf(
                Vec3.Zero, Vec3.Forward, light, LateNightMultiplier, monsterAt);

            Assert.That(deepNight.NoticeDistance, Is.EqualTo(evening.NoticeDistance).Within(1e-4f),
                "§07 takes reach, not exposure. If this ever changes, 심야 becomes partly a stealth "
                + "buff and docs/BALANCE-FINDINGS.md must be updated in the same commit.");
            Assert.That(deepNight.IsNoticed, Is.EqualTo(evening.IsNoticed));

            Assert.That(deepNight.Score, Is.LessThan(evening.Score),
                "The shorter beam does paint less wall, so 심야 leaves the player slightly less "
                + "conspicuous — the residue of §08's one-dial claim leaking through §07's penalty.");
        }

        // ====================================================================
        // §03/§04/§11 — 구역 조명 and 조명탄.
        // ====================================================================

        /// <summary>
        /// §03's reason the Engineer matters to the objective: "구역 전체가 밝다 · 여러 명이 동시에
        /// 읽는다." Several people, no beams, anywhere in the zone.
        /// </summary>
        [Test]
        public void ZoneLight_LetsSeveralPeopleReadWithoutAflashlight()
        {
            var probe = new FakeProbe();
            probe.Zone = 2;
            var field = new LightField(probe);

            var readerA = Vec3.Zero;
            var readerB = new Vec3(GameConstants.ZoneDiagonalMin * 0.5f, 0f, 0f);

            Assert.That(field.CanReadAt(readerA, LightCone.None), Is.False,
                "Control: an unlit zone is §03's lock, engaged.");

            field.SetZoneLit(probe.Zone, Vec3.Zero, true);

            Assert.That(field.IsZoneLit(probe.Zone), Is.True);
            Assert.That(field.CanReadAt(readerA, LightCone.None), Is.True);
            Assert.That(field.CanReadAt(readerB, LightCone.None), Is.True,
                "§03: 구역 전체가 밝다 — uniform, with no falloff, which is what makes two people at "
                + "opposite ends of one room able to read at the same time.");
            Assert.That(field.SampleArea(readerB).Source, Is.EqualTo(LightSourceKind.ZoneLight));
        }

        /// <summary>
        /// §03 prices the zone light with "괴물도 그쪽으로 온다", and §10 repeats it as the
        /// Engineer's own dilemma row. The lure is that price, exposed as a place to walk to.
        /// </summary>
        [Test]
        public void ZoneLight_PullsTheMonsterTowardIt()
        {
            var probe = new FakeProbe();
            probe.Zone = 3;
            var field = new LightField(probe);
            var panel = new Vec3(GameConstants.ZoneLightRadius, 0f, 0f);

            Assert.That(field.Lures, Is.Empty, "Nothing lit, nothing to be drawn to.");

            field.SetZoneLit(probe.Zone, panel, true);

            Assert.That(field.Lures.Count, Is.EqualTo(1));
            Assert.That(field.Lures[0].Position, Is.EqualTo(panel),
                "The breaker is somewhere the monster can actually walk to; a zone is not.");
            Assert.That(field.Lures[0].Kind, Is.EqualTo(LightSourceKind.ZoneLight));
            Assert.That(field.Lures[0].SourceId, Is.EqualTo(probe.Zone));
        }

        /// <summary>
        /// §04's second listed accident is "조명 끔", and its design note forbids protecting the
        /// team from it: "버그로 취급해 없애지 말 것." Cutting the breaker must put a reader back into
        /// §03's darkness immediately, mid-read or not.
        /// </summary>
        [Test]
        public void CuttingTheZoneLight_PutsReadersBackInTheDark()
        {
            var probe = new FakeProbe();
            probe.Zone = 1;
            var field = new LightField(probe);
            var reader = Vec3.Zero;

            field.SetZoneLit(probe.Zone, Vec3.Zero, true);
            Assert.That(field.CanReadAt(reader, LightCone.None), Is.True);

            field.SetZoneLit(probe.Zone, Vec3.Zero, false);

            Assert.That(field.CanReadAt(reader, LightCone.None), Is.False,
                "No grace period. §03's read needs 2.5 s of light and the Engineer just took it.");
            Assert.That(field.Lures, Is.Empty, "The monster stops being drawn to a dark room.");
            Assert.That(field.IsZoneLit(probe.Zone), Is.False);
        }

        /// <summary>
        /// §08 sells the 조명탄 as "구역을 밝힌다 (정비공 없이) · 1회용 · 소리를 낸다", and §11 calls
        /// it the paid stand-in for a missing 정비공. All three clauses, in order.
        /// </summary>
        [Test]
        public void Flare_LightsAZone_AndMakesNoise()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);
            var spot = new Vec3(GameConstants.ZoneDiagonalMin, 0f, 0f);

            var ignition = field.Ignite(spot);

            Assert.That(ignition.Position, Is.EqualTo(spot));
            Assert.That(ignition.Radius, Is.EqualTo(GameConstants.ZoneLightRadius),
                "§11 makes the flare a substitute for the 구역 조명, so it has to cover the same "
                + "ground — the inferiority is the noise, the single use and the falloff.");
            Assert.That(ignition.BurnSeconds, Is.EqualTo(GameConstants.FlareSeconds));

            Assert.That(ignition.NoiseLevel,
                Is.GreaterThan(GameConstants.ListenerSelfNoiseThreshold),
                "§08: 소리를 낸다. §04's Listener goes deaf on noise it makes itself, so a 청음사 who "
                + "strikes a flare must cut their own feed — otherwise the flare is a free 정비공 "
                + "and §11's \"열등한 대체재\" claim is false.");
            Assert.That(ignition.NoiseLevel,
                Is.LessThanOrEqualTo(GameConstants.EngineerTrapNoiseLevel),
                "…but the 소음 함정's job is to be the loudest thing in the zone (§04).");

            Assert.That(field.CanReadAt(spot, LightCone.None), Is.True, "§08: 구역을 밝힌다.");
            Assert.That(field.SampleArea(spot).Source, Is.EqualTo(LightSourceKind.Flare));
            Assert.That(field.Lures.Count, Is.EqualTo(1), "§03: 괴물도 그쪽으로 온다.");
        }

        /// <summary>
        /// §08's "1회용": the flare burns for a fixed time and then the room is dark again. The
        /// expiry is a one-shot handoff, because §03's interesting event is a room going dark
        /// while somebody is reading in it.
        /// </summary>
        [Test]
        public void Flare_Expires_AndTheRoomGoesDarkAgain()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);
            var spot = Vec3.Zero;

            var ignition = field.Ignite(spot);

            Hold(field, GameConstants.FlareSeconds * 0.5f);
            Assert.That(field.BurningFlares.Count, Is.EqualTo(1), "Halfway through, still burning.");
            Assert.That(field.CanReadAt(spot, LightCone.None), Is.True);

            Hold(field, GameConstants.FlareSeconds * 0.5f + GameConstants.FixedStep);

            Assert.That(field.BurningFlares, Is.Empty);
            Assert.That(field.CanReadAt(spot, LightCone.None), Is.False, "§03's lock closes again.");
            Assert.That(field.Lures, Is.Empty);

            Assert.That(field.TryTakeExpiredFlare(out var burntOut), Is.True);
            Assert.That(burntOut.Id, Is.EqualTo(ignition.FlareId));
            Assert.That(burntOut.IsBurning, Is.False);
            Assert.That(burntOut.RemainingSeconds, Is.EqualTo(0f), "Never negative.");
            Assert.That(field.TryTakeExpiredFlare(out _), Is.False,
                "One-shot: a room must not be darkened twice by the same flare.");
        }

        /// <summary>
        /// A frame spike must expire a flare exactly once, and two flares dying on the same step
        /// must both be reported — in the order they were lit, so the host applies them in the
        /// order the players caused them.
        /// </summary>
        [Test]
        public void Flare_FrameSpike_ExpiresEveryFlareExactlyOnce()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);

            var first = field.Ignite(Vec3.Zero);
            var second = field.Ignite(new Vec3(GameConstants.MapExtent * 0.5f, 0f, 0f));

            field.Tick(GameConstants.FlareSeconds * 100f);

            Assert.That(field.BurningFlares, Is.Empty);

            Assert.That(field.TryTakeExpiredFlare(out var a), Is.True);
            Assert.That(field.TryTakeExpiredFlare(out var b), Is.True);
            Assert.That(field.TryTakeExpiredFlare(out _), Is.False,
                "Two flares, two reports — no duplicates however long the step was.");

            Assert.That(a.Id, Is.EqualTo(first.FlareId));
            Assert.That(b.Id, Is.EqualTo(second.FlareId),
                "Simultaneous transitions come out in ignition order.");
        }

        /// <summary>
        /// A flare on the floor below must not light the room above. §12's map is multi-storey and
        /// §03's clue chain narrows by floor first — "그것은 물이 있는 층에 있다" — so a light that
        /// leaked between floors would leak the answer.
        /// </summary>
        [Test]
        public void Flare_DoesNotLightThroughAFloor()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);

            field.Ignite(Vec3.Zero);

            var directlyAbove = new Vec3(0f, GameConstants.ZoneLightRadius * 1.5f, 0f);
            Assert.That(field.CanReadAt(directlyAbove, LightCone.None), Is.False);
            Assert.That(field.SampleArea(directlyAbove).Quality, Is.EqualTo(0f));
        }

        /// <summary>
        /// An <see cref="IWorldProbe"/> that can find nothing navigable must still produce a
        /// light. A flare that failed to exist because the NavMesh was missing would fail exactly
        /// when a team most needs it.
        /// </summary>
        [Test]
        public void Flare_OnAProbeWithNothingNavigable_StillBurns()
        {
            var probe = new FakeProbe();
            probe.PathDistance = float.PositiveInfinity;
            probe.Snap = p => Vec3.Zero;
            var field = new LightField(probe);

            var ignition = field.Ignite(new Vec3(GameConstants.MapExtent, 0f, GameConstants.MapExtent));

            Assert.That(ignition.Position, Is.EqualTo(Vec3.Zero),
                "It lands wherever the probe says the floor is, not where it was thrown.");
            Assert.That(field.BurningFlares.Count, Is.EqualTo(1));
            Assert.That(field.CanReadAt(Vec3.Zero, LightCone.None), Is.True);
        }

        // ====================================================================
        // The single visibility query. §03: 괴물이 잘 본다.
        // ====================================================================

        /// <summary>
        /// A wider beam paints more of the room, so it must be more conspicuous. This is the
        /// monotonicity §08's "밝으면 더 잘 보이지만 더 잘 보인다" is really a claim about — and the
        /// reason the beam's half-angle is not something the shop can sell.
        /// </summary>
        [Test]
        public void Visibility_RisesWithBeamWidth()
        {
            var monsterAt = new Vec3(0f, 0f, GameConstants.FlashlightRange);
            var notice = FlashlightOptics.Standard.NoticeDistance;

            var narrow = new LightCone(
                Vec3.Zero, Vec3.Forward, GameConstants.FlashlightRange, GameConstants.FlashlightHalfAngle);
            var wide = new LightCone(
                Vec3.Zero, Vec3.Forward, GameConstants.FlashlightRange, GameConstants.FlashlightHalfAngle * 2f);

            var narrowScore = LightRules.Visibility(new LightVisibilityQuery(
                Vec3.Zero, monsterAt, narrow, notice, 0f, LightSourceKind.None, true)).Score;
            var wideScore = LightRules.Visibility(new LightVisibilityQuery(
                Vec3.Zero, monsterAt, wide, notice, 0f, LightSourceKind.None, true)).Score;

            Assert.That(narrowScore, Is.GreaterThan(0f));
            Assert.That(wideScore, Is.GreaterThan(narrowScore),
                "§03's 손전등 row prices the beam as \"괴물이 잘 본다\", and the only honest measure of "
                + "that is how much light is loose in the room.");
        }

        /// <summary>A longer beam is a bigger giveaway at the same monster distance.</summary>
        [Test]
        public void Visibility_RisesWithBeamRange()
        {
            var monsterAt = new Vec3(0f, 0f, GameConstants.FlashlightRange);
            var notice = FlashlightOptics.Standard.NoticeDistance;

            var shortBeam = new LightCone(
                Vec3.Zero, Vec3.Forward, GameConstants.FlashlightRange, GameConstants.FlashlightHalfAngle);
            var longBeam = new LightCone(
                Vec3.Zero, Vec3.Forward, FlashlightOptics.Upgraded.Range, GameConstants.FlashlightHalfAngle);

            var shortScore = LightRules.Visibility(new LightVisibilityQuery(
                Vec3.Zero, monsterAt, shortBeam, notice, 0f, LightSourceKind.None, true)).Score;
            var longScore = LightRules.Visibility(new LightVisibilityQuery(
                Vec3.Zero, monsterAt, longBeam, notice, 0f, LightSourceKind.None, true)).Score;

            Assert.That(longScore, Is.GreaterThan(shortScore),
                "Holding the notice distance fixed isolates the reach term: even before §08 doubles "
                + "the distance, a longer beam is already more conspicuous.");
        }

        /// <summary>
        /// §03 prices the 구역 조명 with "괴물도 그쪽으로 온다", but the person standing in it is
        /// giving themselves away too — and it stacks with their own beam rather than masking it.
        /// That combination is §03's clue-reading scene exactly.
        /// </summary>
        [Test]
        public void Visibility_RisesWhenLitByAZoneLight()
        {
            var monsterAt = new Vec3(0f, 0f, GameConstants.ZoneLightRadius * 0.5f);
            var notice = FlashlightOptics.Standard.NoticeDistance;
            var beam = new LightCone(
                Vec3.Zero, Vec3.Forward, GameConstants.FlashlightRange, GameConstants.FlashlightHalfAngle);

            var dark = LightRules.Visibility(new LightVisibilityQuery(
                Vec3.Zero, monsterAt, beam, notice, 0f, LightSourceKind.None, true));
            var lit = LightRules.Visibility(new LightVisibilityQuery(
                Vec3.Zero, monsterAt, beam, notice, 1f, LightSourceKind.ZoneLight, true));

            Assert.That(lit.Score, Is.GreaterThan(dark.Score));
            Assert.That(lit.DominantSource, Is.EqualTo(LightSourceKind.ZoneLight),
                "A player lit from outside is given away by the room, not by their torch — which is "
                + "the thing §04's Engineer needs to be told before he throws the breaker.");

            var unlitPlayer = LightRules.Visibility(new LightVisibilityQuery(
                Vec3.Zero, monsterAt, LightCone.None, 0f, 1f, LightSourceKind.ZoneLight, true));

            Assert.That(unlitPlayer.IsNoticed, Is.True,
                "§03's zone light works without a flashlight, so it must also give you away without "
                + "one. Turning your torch off inside a lit room is not stealth.");
            Assert.That(unlitPlayer.NoticeDistance, Is.EqualTo(GameConstants.ZoneLightRadius));
        }

        /// <summary>
        /// §12's entire geometry exists so that cover works. Light must not defeat it: no sight
        /// line means no light-borne giveaway, whatever is switched on.
        /// </summary>
        [Test]
        public void Visibility_IsZeroBehindCover()
        {
            var probe = new FakeProbe();
            probe.LineOfSight = false;
            probe.Zone = 1;
            var field = new LightField(probe);
            field.SetZoneLit(probe.Zone, Vec3.Zero, true);

            var light = LitFlashlight(true);
            var monsterAt = new Vec3(0f, 0f, GameConstants.AggroReleaseDistance * 0.5f);

            var visibility = field.VisibilityOf(Vec3.Zero, Vec3.Forward, light, 1f, monsterAt);

            Assert.That(visibility.Score, Is.EqualTo(0f),
                "An upgraded flashlight inside a lit zone, six metres away, behind a wall: §12 says "
                + "the wall wins. If light leaked through cover, §06's release rule and every "
                + "S-corridor in §12 would be dead weight.");
            Assert.That(visibility.IsNoticed, Is.False);
            Assert.That(visibility.HasLineOfSight, Is.False);
        }

        /// <summary>
        /// A player with no light in an unlit room is not giving anything away. Zero here means
        /// "light is not helping the monster", not "invisible" — §06 still gives it
        /// <see cref="GameConstants.MonsterSightRange"/> for bodies, and the perception system adds
        /// that term itself.
        /// </summary>
        [Test]
        public void Visibility_InTotalDarkness_IsZero()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);
            var dark = new FlashlightState(new BatteryState());
            var monsterAt = new Vec3(0f, 0f, GameConstants.AggroReleaseDistance * 0.25f);

            var visibility = field.VisibilityOf(Vec3.Zero, Vec3.Forward, dark, 1f, monsterAt);

            Assert.That(visibility.Score, Is.EqualTo(0f));
            Assert.That(visibility.NoticeDistance, Is.EqualTo(0f));
            Assert.That(visibility.IsNoticed, Is.False);
            Assert.That(visibility.DominantSource, Is.EqualTo(LightSourceKind.None));
            Assert.That(visibility.DistanceToMonster,
                Is.EqualTo(GameConstants.AggroReleaseDistance * 0.25f).Within(1e-3f),
                "The distance is still reported: perception needs it for its own §06 sight check.");
        }

        /// <summary>
        /// §03's 목표물 운반 rule — "양손을 쓴다 · 손전등을 들 수 없다" — has to resolve to exactly
        /// the same darkness as a flat battery, so that "누군가 비춰줘야 한다" is a consequence of
        /// the light rules and not a special case.
        /// </summary>
        [Test]
        public void CarryingTheObjective_IsTheSameDarknessAsAflatCell()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);
            var mark = new Vec3(0f, 0f, GameConstants.ClueReadRange);

            var carrier = field.SampleAt(mark, LightCone.None);
            var flatCell = field.SampleAt(
                mark, new FlashlightState(new BatteryState(0f, 0)).ConeAt(Vec3.Zero, Vec3.Forward, 1f));

            Assert.That(carrier.Quality, Is.EqualTo(flatCell.Quality));
            Assert.That(carrier.IsReadable, Is.False);
            Assert.That(flatCell.IsReadable, Is.False);
        }

        /// <summary>
        /// §03's "빛이 좁다" has to be a real obstacle: pointing roughly the right way must not be
        /// enough. A mark at <see cref="GameConstants.ClueReadRange"/> falls out of readability
        /// well before the edge of the cone.
        /// </summary>
        [Test]
        public void NarrowBeam_MustActuallyBeAimed()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);
            var distance = GameConstants.ClueReadRange;

            var onAxis = new Vec3(0f, 0f, distance);
            var atTheEdge = MathX.DirectionFromYaw(GameConstants.FlashlightHalfAngle * 0.9f) * distance;

            var beam = new LightCone(
                Vec3.Zero, Vec3.Forward, GameConstants.FlashlightRange, GameConstants.FlashlightHalfAngle);

            Assert.That(field.CanReadAt(onAxis, beam), Is.True);
            Assert.That(field.CanReadAt(atTheEdge, beam), Is.False,
                "§03 lists 손전등의 좁은 빛 as a reading obstacle. A cone that read equally well at "
                + "its edge would make the beam a floodlight with an angle limit.");
            Assert.That(beam.Contains(atTheEdge), Is.True,
                "Still inside the cone, though — it is lit, just not enough to read by.");
        }

        /// <summary>
        /// A degenerate aim direction must produce darkness rather than a cone pointing nowhere in
        /// particular. §05 syncs camera rotation over the network, so a dropped packet is a
        /// reachable way to be asked about a zero-length forward vector.
        /// </summary>
        [Test]
        public void DegenerateAimDirection_LightsNothing()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);
            var light = LitFlashlight(false);

            var cone = light.ConeAt(Vec3.Zero, Vec3.Zero, 1f);

            Assert.That(cone.IsLit, Is.False);
            Assert.That(cone.LitFootprint, Is.EqualTo(0f));
            Assert.That(field.SampleAt(new Vec3(0f, 0f, GameConstants.ClueReadRange), cone).Quality,
                Is.EqualTo(0f));
        }

        // ====================================================================
        // Empty worlds and bad arguments.
        // ====================================================================

        /// <summary>
        /// A field with nothing in it must answer every question as darkness rather than as an
        /// exception. All four players dead, every light out, is a normal late-match state.
        /// </summary>
        [Test]
        public void EmptyField_AnswersDarkness()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);

            Assert.That(field.BurningFlares, Is.Empty);
            Assert.That(field.LitZones, Is.Empty);
            Assert.That(field.Lures, Is.Empty);
            Assert.That(field.TryTakeExpiredFlare(out _), Is.False);
            Assert.That(field.SampleArea(Vec3.Zero).Quality, Is.EqualTo(0f));
            Assert.That(field.SampleArea(Vec3.Zero).Source, Is.EqualTo(LightSourceKind.None));
            Assert.That(field.CanReadAt(Vec3.Zero, LightCone.None), Is.False);

            Assert.DoesNotThrow(() => field.Tick(0f));
            Assert.DoesNotThrow(() => field.Tick(GameConstants.TargetMatchSecondsMax));
            Assert.DoesNotThrow(() => field.Tick(float.NaN));
            Assert.DoesNotThrow(() => field.SetZoneLit(-1, Vec3.Zero, true));
            Assert.That(field.LitZones, Is.Empty, "-1 is \"off the map\", not a zone.");
        }

        /// <summary>
        /// The host's own <see cref="IWorldProbe.IsAreaLit"/> has to gate reading the same way a
        /// tracked light does, or §03's lock would mean two different things depending on which
        /// system lit the room.
        /// </summary>
        [Test]
        public void HostReportedAreaLight_UnlocksReadingToo()
        {
            var probe = new FakeProbe();
            var field = new LightField(probe);

            Assert.That(field.CanReadAt(Vec3.Zero, LightCone.None), Is.False);

            probe.Lit = true;

            Assert.That(field.CanReadAt(Vec3.Zero, LightCone.None), Is.True);
            Assert.That(field.SampleArea(Vec3.Zero).Source, Is.EqualTo(LightSourceKind.AreaLit),
                "Reported as its own kind so telemetry can tell a map fixture apart from a player's "
                + "doing.");
        }

        /// <summary>Missing collaborators must fail loudly at construction, not silently at the first query.</summary>
        [Test]
        public void MissingCollaborators_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new LightField(null));
            Assert.Throws<ArgumentNullException>(() => new FlashlightState(null));

            var field = new LightField(new FakeProbe());
            Assert.Throws<ArgumentNullException>(
                () => field.VisibilityOf(Vec3.Zero, Vec3.Forward, null, 1f, Vec3.Zero));
        }

        /// <summary>
        /// §03 is explicit that leaving the building is "숨 돌리기이지 리셋이 아니다": the monster's
        /// aggro resets, time does not. Lights are not on its reset list either, so
        /// <see cref="LightField.Clear"/> must be a match-start operation — a surface trip that
        /// extinguished the flares would hand the team a free blackout.
        /// </summary>
        [Test]
        public void Clear_IsForANewMatch_NotForASurfaceTrip()
        {
            var probe = new FakeProbe();
            probe.Zone = 4;
            var field = new LightField(probe);

            field.SetZoneLit(probe.Zone, Vec3.Zero, true);
            field.Ignite(Vec3.Zero);

            Hold(field, GameConstants.FlareSeconds * 0.25f);

            Assert.That(field.BurningFlares.Count, Is.EqualTo(1),
                "§03's 부분 리셋 keeps 시간 flowing, so a flare left behind keeps burning down.");
            Assert.That(field.BurningFlares[0].BurnFraction, Is.LessThan(1f));
            Assert.That(field.IsZoneLit(probe.Zone), Is.True,
                "The Engineer's breaker is not on §03's reset list.");

            field.Clear();

            Assert.That(field.BurningFlares, Is.Empty);
            Assert.That(field.LitZones, Is.Empty);
            Assert.That(field.Lures, Is.Empty);
            Assert.That(field.TryTakeExpiredFlare(out _), Is.False);
        }

        /// <summary>
        /// §16-5 calls the cell life the value that sets the round-trip rhythm, so the number that
        /// rhythm is actually made of — how long one cell of light lasts once §03's switch-on
        /// charge is paid — has to be the one the simulator sweeps.
        /// </summary>
        [Test]
        public void OneCell_LastsItsRatedTime_MinusTheSwitchOnCharge()
        {
            var light = LitFlashlight(false);
            var expected = GameConstants.BatterySecondsPerCell - GameConstants.BatterySwitchOnCost;

            Assert.That(light.Battery.Charge, Is.EqualTo(expected).Within(1e-3f));

            // Stepped in two jumps rather than 10,000 fixed steps: the assertion is about the
            // rated life, not about float accumulation, and a single step keeps the arithmetic
            // exact either side of the boundary.
            light.Tick(expected - GameConstants.FixedStep);
            Assert.That(light.IsLit, Is.True, "Still lit one step short of the rated life.");

            light.Tick(GameConstants.FixedStep * 2f);
            Assert.That(light.IsLit, Is.False, "And out immediately after it.");

            Assert.That(
                GameConstants.BatterySecondsPerCell * GameConstants.EconomyReferenceCellsPerDescent,
                Is.LessThan(GameConstants.TargetMatchSecondsMin),
                "§16-5 calls the cell life the value that sets the round-trip rhythm. §08's reference "
                + "restock is four cells per descent, and four cells must not cover a whole match — "
                + "if they did, the team would never have to surface and §03's 왕복 구조, which the "
                + "battery is the first row of, would collapse.");
        }
    }
}
