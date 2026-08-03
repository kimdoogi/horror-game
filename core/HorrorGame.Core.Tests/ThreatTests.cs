using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Match;
using HorrorGame.Core.Threat;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §07 — 시간 = 위협도. The threat table and the clock that walks it.
    /// <para>
    /// These tests are written as assertions about §07's own reasoning, so they
    /// fail when the design's logic breaks rather than when an implementation
    /// detail moves. Several of them pin contradictions between §07 and the
    /// sections that quote its numbers; those are marked and cross-referenced to
    /// docs/BALANCE-FINDINGS.md, and a "failure" there means someone retuned on
    /// purpose and the finding needs updating in the same commit.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ThreatTests
    {
        // ====================================================================
        // The table itself. §07's 시간대별 위협 단계.
        // ====================================================================

        /// <summary>
        /// Transcribes §07's table row by row. The literals here are the design
        /// document, not code — this is the one place they belong, so that editing
        /// a constant fails against the document instead of quietly redefining it.
        /// </summary>
        [Test]
        public void ThreatTable_TranscribesSection07()
        {
            var speeds = new[] { 4.4f, 4.6f, 4.8f, 5.0f, 5.2f };
            var phases = new[]
            {
                NightPhase.EarlyEvening, NightPhase.Night, NightPhase.LateNight,
                NightPhase.PreDawn, NightPhase.BeforeSunrise,
            };

            Assert.That(ThreatCurve.TierCount, Is.EqualTo(speeds.Length),
                "§07's table has five rows. Adding a sixth night means amending the document first.");

            for (var i = 0; i < ThreatCurve.TierCount; i++)
            {
                var tier = ThreatCurve.Tier(i);
                Assert.That(tier.Index, Is.EqualTo(i),
                    "Tier(i) must return row i — the table must not be reordered.");
                Assert.That(tier.MonsterSpeed, Is.EqualTo(speeds[i]).Within(1e-4f),
                    $"§07 row {i} speed.");
                Assert.That(tier.Phase, Is.EqualTo(phases[i]), $"§07 row {i} 시각.");
                Assert.That(tier.StartSeconds,
                    Is.EqualTo(i * GameConstants.ThreatTierSeconds).Within(1e-3f),
                    "§07's bands are eight minutes wide, with no gaps.");
            }

            Assert.That(ThreatCurve.Tier(ThreatCurve.TierCount - 1).IsFinal, Is.True,
                "동트기 전 is open-ended (§07): there is no row after it.");
            Assert.That(ThreatCurve.Tier(ThreatCurve.TierCount - 1).EndSeconds,
                Is.EqualTo(float.PositiveInfinity),
                "§07 gives 32분+ no end. The night stops getting worse; it does not stop.");
        }

        /// <summary>
        /// The speed ladder §07 actually specifies: an even 0.2 m/s step per band,
        /// crossing §06's headline 4.8 exactly once, at 심야.
        /// </summary>
        [Test]
        public void MonsterSpeed_ClimbsEvenly_AndHitsSection06sLadderOnlyAtLateNight()
        {
            var step = ThreatCurve.Tier(1).MonsterSpeed - ThreatCurve.Tier(0).MonsterSpeed;

            for (var i = 1; i < ThreatCurve.TierCount; i++)
            {
                var delta = ThreatCurve.Tier(i).MonsterSpeed - ThreatCurve.Tier(i - 1).MonsterSpeed;
                Assert.That(delta, Is.GreaterThan(0f), "§07's pressure must never fall between tiers.");
                Assert.That(delta, Is.EqualTo(step).Within(1e-3f),
                    "§07's speed column is an even ladder. An uneven step means one band escalates "
                    + "harder than the others, which the document does not say.");
            }

            Assert.That(ThreatCurve.Tier(2).MonsterSpeed,
                Is.EqualTo(GameConstants.MonsterBaseSpeed).Within(1e-4f),
                "§06's 4.8 is the 심야 row. Every margin §06 and §12 compute from MonsterBaseSpeed is "
                + "therefore a statement about 16–24 min alone.");
            Assert.That(ThreatCurve.At(0f).MonsterSpeed, Is.LessThan(GameConstants.MonsterBaseSpeed),
                "The monster only reaches 4.8 at 16 min (§07).");
            Assert.That(ThreatCurve.At(ThreatCurve.FinalTierStartSeconds).MonsterSpeed,
                Is.GreaterThan(GameConstants.MonsterBaseSpeed));
        }

        /// <summary>
        /// §07's 정지 column, 잦음 → 없음. The standstill is the design's stated
        /// weapon ("침묵이 가장 무서운 소리다", §06) and §07 takes it away exactly when
        /// §01 has the team escorting the objective — from then on the monster is
        /// always audible and never loseable, which is the same rule read from both
        /// ends.
        /// </summary>
        [Test]
        public void StandstillChance_FallsToSilenceEndingAtPreDawn()
        {
            for (var i = 1; i < ThreatCurve.TierCount; i++)
            {
                Assert.That(ThreatCurve.Tier(i).StandstillChance,
                    Is.LessThanOrEqualTo(ThreatCurve.Tier(i - 1).StandstillChance),
                    "§07: standstills only ever get rarer.");
            }

            foreach (var tier in AllTiers())
            {
                Assert.That(tier.StandstillChance, Is.InRange(0f, 1f), "A chance must be a probability.");
            }

            Assert.That(ThreatCurve.Tier(0).StandstillChance, Is.GreaterThan(0.5f),
                "§07 calls 초저녁 standstills 잦음 — more often than not, or the Listener is never "
                + "taught that silence means the monster stopped.");
            Assert.That(ThreatCurve.Tier(3).StandstillChance, Is.EqualTo(0f),
                "§07's 새벽 row says 없음. The monster never goes quiet again from the escort onwards.");
            Assert.That(ThreatCurve.Tier(4).StandstillChance, Is.EqualTo(0f), "§07's 32분+ row says 없음.");
        }

        /// <summary>
        /// §07's 추가 column is implemented cumulatively: the 심야 flashlight penalty
        /// stays on through 새벽 and 동트기 전, and the monster never un-learns the
        /// exit. Read strictly per row the penalty would end at 24 min and the
        /// monster would forget the way out at 32 min — pressure would fall twice in
        /// a table whose whole argument is that pressure only rises.
        /// <para>
        /// Recorded as a finding: the document never writes the word "cumulative".
        /// </para>
        /// </summary>
        [Test]
        public void ExtraColumn_IsCumulative_SoPressureNeverFalls()
        {
            var lit = 1f - GameConstants.LateNightFlashlightPenalty;

            Assert.That(ThreatCurve.Tier(0).FlashlightRangeMultiplier, Is.EqualTo(1f),
                "§07 takes nothing from the flashlight before 심야.");
            Assert.That(ThreatCurve.Tier(1).FlashlightRangeMultiplier, Is.EqualTo(1f));
            Assert.That(ThreatCurve.Tier(2).FlashlightRangeMultiplier, Is.EqualTo(lit).Within(1e-4f),
                "§07's 심야 row: 손전등 반경 −30%.");
            Assert.That(ThreatCurve.Tier(3).FlashlightRangeMultiplier, Is.EqualTo(lit).Within(1e-4f),
                "The beam must not grow back at 24 min. §03 calls darkness the objective's lock; "
                + "handing 30% of the range back during the escort would unlock it.");
            Assert.That(ThreatCurve.Tier(4).FlashlightRangeMultiplier, Is.EqualTo(lit).Within(1e-4f));

            Assert.That(ThreatCurve.Tier(2).MonsterKnowsExit, Is.False,
                "§07 grants exit knowledge at 새벽, not before — 심야 is still the tier where running "
                + "for the stairs is a plan.");
            Assert.That(ThreatCurve.Tier(3).MonsterKnowsExit, Is.True, "§07's 새벽 row / §01's 호송 scene.");
            Assert.That(ThreatCurve.Tier(4).MonsterKnowsExit, Is.True,
                "Knowledge cannot be lost. A per-row reading would have the monster forget the exit at "
                + "32 min, which no amount of retuning would make sensible.");

            for (var i = 1; i < ThreatCurve.TierCount; i++)
            {
                Assert.That(ThreatCurve.Tier(i).FlashlightRangeMultiplier,
                    Is.LessThanOrEqualTo(ThreatCurve.Tier(i - 1).FlashlightRangeMultiplier),
                    "§07: no tier may hand light back.");
                Assert.That(ThreatCurve.Tier(i).MonsterKnowsExit || !ThreatCurve.Tier(i - 1).MonsterKnowsExit,
                    Is.True, "§07: no tier may take exit knowledge away.");
            }
        }

        // ====================================================================
        // Boundaries. §07's bands are [start, start + 8 min).
        // ====================================================================

        /// <summary>
        /// Every eight-minute boundary must be exact, and must belong to the later
        /// tier: §07's "0~8분" band is [0, 8), so at 8:00.000 the monster is already
        /// on the 밤 row. Checked one fixed step either side, because a boundary that
        /// is off by one step is a boundary that a frame-rate change can move.
        /// </summary>
        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void TierBoundary_IsExactAndBelongsToTheLaterTier(int tierIndex)
        {
            var start = tierIndex * GameConstants.ThreatTierSeconds;

            Assert.That(ThreatCurve.TierIndexAt(start), Is.EqualTo(tierIndex),
                $"§07: {tierIndex * 8} min is the first instant of row {tierIndex}, not the last of the previous one.");
            Assert.That(ThreatCurve.TierIndexAt(start + GameConstants.FixedStep), Is.EqualTo(tierIndex),
                "One step past the boundary is still the new tier.");

            var expectedBefore = tierIndex == 0 ? 0 : tierIndex - 1;
            Assert.That(ThreatCurve.TierIndexAt(start - GameConstants.FixedStep), Is.EqualTo(expectedBefore),
                "One step before the boundary is still the previous tier — and before zero is still 초저녁.");
        }

        /// <summary>
        /// Past 32 min the curve saturates on the last row instead of indexing off
        /// the end of the table. §07 stops describing the night there, so a 40- or
        /// 400-minute match must keep getting 동트기 전 rather than an exception or an
        /// invented sixth tier.
        /// </summary>
        [Test]
        public void PastTheLastBoundary_TheCurveSaturates()
        {
            var last = ThreatCurve.TierCount - 1;
            var final = ThreatCurve.FinalTierStartSeconds;

            var probes = new[]
            {
                final,
                final + GameConstants.FixedStep,
                final * 2f,
                GameConstants.TargetMatchSecondsMax * 10f,
                float.MaxValue,
                float.PositiveInfinity,
            };

            foreach (var t in probes)
            {
                Assert.That(ThreatCurve.TierIndexAt(t), Is.EqualTo(last), $"At {t}s the table has no next row.");
                Assert.That(ThreatCurve.At(t).Phase, Is.EqualTo(NightPhase.BeforeSunrise));
                Assert.That(ThreatCurve.At(t).MonsterSpeed,
                    Is.EqualTo(GameConstants.ThreatSpeedBeforeSunrise).Within(1e-4f),
                    "The monster must stop accelerating at 5.2 — §07 describes no speed above it.");
                Assert.That(ThreatCurve.ThreatScalar(t), Is.EqualTo(1f), "The scalar saturates with the table.");
                Assert.That(ThreatCurve.TierProgress(t), Is.EqualTo(1f),
                    "An open-ended tier has no fraction to be part-way through.");
            }

            Assert.That(ThreatCurve.Tier(ThreatCurve.TierCount).Index, Is.EqualTo(last),
                "An out-of-range index clamps rather than throwing mid-tick.");
            Assert.That(ThreatCurve.Tier(-3).Index, Is.EqualTo(0));
        }

        /// <summary>
        /// A clock that has not started, a negative delta that slipped through, or a
        /// NaN must all read as 초저녁. The alternative — a NaN silently resolving to
        /// "생존 불가 수준" — would be the worst failure mode this system has.
        /// </summary>
        [Test]
        public void DegenerateElapsedTime_ReadsAsTheFirstTier()
        {
            var probes = new[] { 0f, -1f, -GameConstants.ThreatTierSeconds, float.NaN, float.NegativeInfinity };

            foreach (var t in probes)
            {
                Assert.That(ThreatCurve.TierIndexAt(t), Is.EqualTo(0), $"{t} must read as 초저녁.");
                Assert.That(ThreatCurve.At(t).Phase, Is.EqualTo(NightPhase.EarlyEvening));

                var scalar = ThreatCurve.ThreatScalar(t);
                Assert.That(float.IsNaN(scalar), Is.False, "A NaN must not escape into the audio mix.");
                Assert.That(scalar, Is.EqualTo(0f));

                var progress = ThreatCurve.TierProgress(t);
                Assert.That(float.IsNaN(progress), Is.False);
                Assert.That(progress, Is.EqualTo(0f));
            }
        }

        // ====================================================================
        // §07's "압박: 연속적" against its own discrete table.
        // ====================================================================

        /// <summary>
        /// The deliberate split: mechanics step, presentation does not.
        /// <para>
        /// §07 lists "압박: 연속적" as a reason for choosing a clock, then states the
        /// pressure as five rows. Interpolating the rows is not available — patrol
        /// scope is a whole number of zones and "괴물이 출입구를 안다" is a boolean —
        /// and it would also dissolve the one speed §12's geometry is derived from.
        /// So the rows step and <see cref="ThreatCurve.ThreatScalar"/> carries the
        /// continuity for music, ambience and telemetry.
        /// </para>
        /// </summary>
        [Test]
        public void Pressure_StepsInTheRules_AndFlowsInTheScalar()
        {
            var lateInTier = GameConstants.ThreatTierSeconds - GameConstants.FixedStep;

            Assert.That(ThreatCurve.At(lateInTier).MonsterSpeed,
                Is.EqualTo(ThreatCurve.At(0f).MonsterSpeed).Within(0f),
                "Inside a tier the monster's speed must not move at all: §12's 14.4 m single-corner "
                + "rule is 3 s × one exact speed, and a drifting speed makes that rule true for an "
                + "instant instead of a tier.");

            Assert.That(ThreatCurve.ThreatScalar(lateInTier), Is.GreaterThan(ThreatCurve.ThreatScalar(0f)),
                "The scalar must move while the tier does not — that is the whole point of having both.");

            Assert.That(ThreatCurve.ThreatScalar(0f), Is.EqualTo(0f));
            Assert.That(ThreatCurve.ThreatScalar(ThreatCurve.FinalTierStartSeconds), Is.EqualTo(1f));
            Assert.That(ThreatCurve.ThreatScalar(ThreatCurve.FinalTierStartSeconds / 2f),
                Is.EqualTo(0.5f).Within(1e-4f), "Linear in time, because time is what §07 calls the currency.");

            var previous = -1f;
            for (var t = 0f; t <= GameConstants.TargetMatchSecondsMax * 1.5f; t += 1f)
            {
                var scalar = ThreatCurve.ThreatScalar(t);
                Assert.That(scalar, Is.InRange(0f, 1f), $"The scalar left 0–1 at {t}s.");
                Assert.That(scalar, Is.GreaterThanOrEqualTo(previous), $"The scalar fell at {t}s.");
                previous = scalar;
            }

            Assert.That(ThreatCurve.TierProgress(GameConstants.ThreatTierSeconds / 2f),
                Is.EqualTo(0.5f).Within(1e-4f), "Half way through 초저녁.");
        }

        // ====================================================================
        // §07's 순찰 column against §12's 4–6 zones.
        // ====================================================================

        /// <summary>
        /// §07 writes the patrol column in two units — absolute zones early,
        /// proportions later — and §12 lets a map have 4–6 zones. Resolving against
        /// the real map is therefore a rule, not a rounding detail.
        /// </summary>
        [Test]
        public void PatrolScope_ResolvesAgainstTheActualMap()
        {
            Assert.That(ThreatCurve.Tier(0).PatrolZoneCountFor(GameConstants.ZoneCountMax),
                Is.EqualTo(GameConstants.ThreatPatrolZonesEarlyEvening),
                "§07: 초저녁 patrols one zone whatever the map size. Most of the map is survivable early.");
            Assert.That(ThreatCurve.Tier(1).PatrolZoneCountFor(GameConstants.ZoneCountMax),
                Is.EqualTo(GameConstants.ThreatPatrolZonesNight));

            Assert.That(ThreatCurve.Tier(2).PatrolZoneCountFor(6), Is.EqualTo(3), "§07 심야: 절반 of six zones.");
            Assert.That(ThreatCurve.Tier(2).PatrolZoneCountFor(5), Is.EqualTo(3),
                "절반 rounds up: rounding down would make 심야 patrol exactly as much as 밤 and escalate nothing.");

            for (var zones = GameConstants.ZoneCountMin; zones <= GameConstants.ZoneCountMax; zones++)
            {
                Assert.That(ThreatCurve.Tier(3).PatrolZoneCountFor(zones), Is.EqualTo(zones),
                    "§07 새벽: 전체 means every zone the map has.");
                Assert.That(ThreatCurve.Tier(4).PatrolZoneCountFor(zones), Is.EqualTo(zones));

                for (var i = 1; i < ThreatCurve.TierCount; i++)
                {
                    Assert.That(ThreatCurve.Tier(i).PatrolZoneCountFor(zones),
                        Is.GreaterThanOrEqualTo(ThreatCurve.Tier(i - 1).PatrolZoneCountFor(zones)),
                        $"§07: patrol coverage must never shrink — it does between tiers {i - 1} and {i} on a {zones}-zone map.");
                    Assert.That(ThreatCurve.Tier(i).PatrolZoneCountFor(zones), Is.LessThanOrEqualTo(zones),
                        "The monster cannot patrol more zones than the map has.");
                }
            }

            foreach (var tier in AllTiers())
            {
                Assert.That(tier.PatrolZoneCountFor(0), Is.EqualTo(0),
                    "A world probe that knows of no zones must produce a monster that patrols none, "
                    + "not a negative count or an exception at match start.");
                Assert.That(tier.PatrolZoneCountFor(-4), Is.EqualTo(0));
                Assert.That(tier.PatrolZoneCount, Is.EqualTo(tier.PatrolZoneCountFor(GameConstants.ZoneCountMax)),
                    "The fixed signature answers for the largest legal map, as documented.");
            }
        }

        // ====================================================================
        // MatchClock. §07's "시계 하나" — the stamp on every RaceState entry, and
        // the row that says how fast the creature is.
        //
        // DELETED with the co-operative game, because the race has no 지상:
        //   Clock_KeepsRunningWhileTheTeamIsOnTheSurface
        //   Clock_Surfacing_ResetsTheMonsterAndNotTheClock
        //   Clock_SurfacingIsIdempotent_AndThePendingResetSurvivesADive
        //   Clock_IsReadableOnlyOnTheSurfaceOrWithAPocketWatch
        //   Clock_TierCrossingAndSurfacingOnTheSameStep_BothReport
        // All five turned on SetTeamOnSurface. A runner starts on the rim of B1 and
        // the only way out is down: there is no vehicle to come back to, so §03's
        // 부분 리셋 has nothing to charge for, and §07's 「시각은 지상에서만 알 수
        // 있다」 has no 지상 to be readable on. The 회중시계 that lifted that
        // restriction was sold by §08, which has no currency left to sell it for.
        // ====================================================================

        /// <summary>
        /// A frame spike must not swallow a tier. §07's escalations are the beats the
        /// audio layer and the telemetry buckets hang on, so crossing three
        /// boundaries in one 24-minute step has to report three, in order.
        /// </summary>
        [Test]
        public void Clock_FrameSpike_AnnouncesEveryCrossedTierInOrder()
        {
            var clock = new MatchClock();

            Assert.That(clock.TryDequeueTierAdvance(out _), Is.False,
                "A match starts in 초저녁; arriving there is not an escalation.");

            clock.Tick(GameConstants.ThreatTierSeconds * (ThreatCurve.TierCount + 1));

            var announced = new List<int>();
            while (clock.TryDequeueTierAdvance(out var tier))
            {
                announced.Add(tier.Index);
            }

            Assert.That(announced, Is.EqualTo(new[] { 1, 2, 3, 4 }),
                "Every tier the match passed through must be reported once, oldest first. Skipping one "
                + "loses an audio beat and a telemetry bucket that §16 needs.");
            Assert.That(clock.TierIndex, Is.EqualTo(ThreatCurve.TierCount - 1),
                "A spike is honoured in full: the night does not owe back the seconds the machine dropped.");
            Assert.That(clock.TryDequeueTierAdvance(out _), Is.False, "Nothing left to report.");
        }

        /// <summary>
        /// Time only moves forward. A zero, negative, NaN or infinite delta is
        /// ignored outright: a poisoned accumulator would take §07's whole curve with
        /// it, silently pinning every later query to 초저녁.
        /// </summary>
        [Test]
        public void Clock_DegenerateDeltas_AreIgnoredAndDoNotPoisonTheClock()
        {
            var clock = new MatchClock();
            clock.Tick(GameConstants.FixedStep);
            var afterOneStep = clock.ElapsedSeconds;

            clock.Tick(0f);
            clock.Tick(-GameConstants.ThreatTierSeconds);
            clock.Tick(float.NaN);
            clock.Tick(float.PositiveInfinity);
            clock.Tick(float.NegativeInfinity);

            Assert.That(float.IsNaN(clock.ElapsedSeconds), Is.False, "A NaN delta must not reach the accumulator.");
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(afterOneStep).Within(0f),
                "None of those deltas is a length of time the match can advance by.");
            Assert.That(clock.TierIndex, Is.EqualTo(0));

            clock.Tick(GameConstants.FixedStep);
            Assert.That(clock.ElapsedSeconds, Is.GreaterThan(afterOneStep),
                "And the clock still works afterwards — the guard rejects the delta, not the clock.");
        }

        /// <summary>
        /// A whole tier of fixed steps must land on the boundary, not near it.
        /// <para>
        /// 24,000 additions of a float 0.02 drift by a visible fraction of a second,
        /// which would move §07's 8-minute boundaries around by frame rate. §07 makes
        /// this value the game's only currency, so the accumulator is double and the
        /// boundary is exact.
        /// </para>
        /// </summary>
        [Test]
        public void Clock_AWholeTierOfFixedSteps_LandsExactlyOnTheBoundary()
        {
            var clock = new MatchClock();
            var steps = (int)System.MathF.Round(GameConstants.ThreatTierSeconds / GameConstants.FixedStep);

            for (var i = 0; i < steps; i++)
            {
                clock.Tick(GameConstants.FixedStep);
            }

            Assert.That(clock.ElapsedSeconds, Is.EqualTo(GameConstants.ThreatTierSeconds).Within(0.005f),
                $"After {steps} fixed steps the clock is off by more than 5 ms. A float accumulator drifts "
                + "far further than that over a 35-minute match, and §07's boundaries would move with it.");
            Assert.That(clock.TierIndex, Is.EqualTo(1),
                "8:00 must arrive at the 24,000th step, not one step late.");
        }

        /// <summary>
        /// §01 targets 25–35 minutes and §07's last tier begins at 32. Read together:
        /// the fastest intended match ends under escort pressure and only an
        /// overrunning one reaches "생존 불가 수준". That is the shape §07 is aiming
        /// for, and it is worth failing the build over if either number moves.
        /// </summary>
        [Test]
        public void MatchLengthTargets_LandInTheIntendedTiers()
        {
            Assert.That(ThreatCurve.At(GameConstants.TargetMatchSecondsMin).Phase,
                Is.EqualTo(NightPhase.PreDawn),
                "§01's shortest target match still ends in 새벽 — the monster already knows the exit "
                + "when a fast team is escorting the objective out.");
            Assert.That(ThreatCurve.At(GameConstants.TargetMatchSecondsMax).Phase,
                Is.EqualTo(NightPhase.BeforeSunrise),
                "§01's longest target match reaches 동트기 전. §07 calls that 생존 불가, so overrunning "
                + "is meant to be lethal rather than merely slow.");
            Assert.That(ThreatCurve.FinalTierStartSeconds,
                Is.GreaterThan(GameConstants.TargetMatchSecondsMin).And
                    .LessThan(GameConstants.TargetMatchSecondsMax),
                "The unsurvivable tier must begin inside §01's target window: earlier and a normal match "
                + "cannot be won, later and §07's final threat never appears.");
        }

        // ====================================================================
        // Findings — §07's numbers against the sections that quote them.
        // See docs/BALANCE-FINDINGS.md.
        // ====================================================================

        /// <summary>
        /// <b>Finding.</b> §06 opens with "걷기 2.0 &lt; 달리기 4.5 &lt; 괴물 4.8 &lt; 주자
        /// 질주 5.6" and calls that one line the thing that decides the whole game.
        /// §07's first row sets the monster to 4.4 — below a running player. For the
        /// first eight minutes every role outruns the monster, and running has no
        /// stamina cost in §06, so the escape is unbounded rather than merely
        /// possible.
        /// <para>
        /// §06 does say the speed varies by time of night, but not that the variation
        /// inverts its ladder, and §16 lists 시간 위협 as settled. The document has to
        /// choose; this test pins what it currently says.
        /// </para>
        /// </summary>
        [Test]
        public void Finding_FirstTierMonsterIsSlowerThanARunningPlayer()
        {
            var earlyEvening = ThreatCurve.At(0f);

            Assert.That(earlyEvening.MonsterSpeed, Is.LessThan(GameConstants.RunSpeed),
                "§07's 초저녁 monster (4.4) is slower than §06's running player (4.5), so §06's "
                + "\"일반 직업은 도망칠 수 없다\" is false for the first eight minutes — the whole of the "
                + "first descent. If this now passes, the tier speeds were retuned on purpose and "
                + "docs/BALANCE-FINDINGS.md must be updated in the same commit.");

            var nightMargin = ThreatCurve.Tier(1).MonsterSpeed - GameConstants.RunSpeed;
            var designMargin = GameConstants.MonsterBaseSpeed - GameConstants.RunSpeed;

            Assert.That(nightMargin, Is.GreaterThan(0f), "By 밤 the monster is at least faster than running.");
            Assert.That(nightMargin, Is.LessThan(designMargin),
                "§06 calls its +0.3 margin the source of the \"거의 도망칠 수 있는데 안 되는\" tension. "
                + "§07 only produces exactly +0.3 during 심야: before it the margin is smaller or "
                + "negative, after it larger.");
            Assert.That(ThreatCurve.Tier(2).MonsterSpeed - GameConstants.RunSpeed,
                Is.EqualTo(designMargin).Within(1e-4f), "심야 is the tier §06 was written about.");
        }

        /// <summary>
        /// <b>Finding.</b> §06 computes the Runner's sprint gain as
        /// (5.6 − 4.8) × 12 s = 9.6 m and concludes "질주만으로는 절대 못 벌린다" against
        /// a 12 m release distance — the argument §12's entire map ruleset is built
        /// on. At §07's early speeds the same arithmetic gives 14.4 m and 12.0 m, so
        /// the distance §06 calls unreachable is reachable, or exactly tied, for the
        /// first sixteen minutes.
        /// </summary>
        [Test]
        public void Finding_ASingleSprintOpensTheReleaseDistanceBeforeLateNight()
        {
            var earlyGain = SprintGainAgainst(ThreatCurve.Tier(0));
            var nightGain = SprintGainAgainst(ThreatCurve.Tier(1));
            var lateNightGain = SprintGainAgainst(ThreatCurve.Tier(2));

            Assert.That(earlyGain, Is.GreaterThan(GameConstants.AggroReleaseDistance),
                "At 초저녁 one sprint opens more than the 12 m release distance (14.4 m). §06's "
                + "\"어그로 해제는 거리가 아니라 맵을 쓰는 것\" — and therefore §12's S-corridor rules — "
                + "only becomes true at 16 minutes.");
            Assert.That(nightGain, Is.EqualTo(GameConstants.AggroReleaseDistance).Within(0.05f),
                "At 밤 the sprint gain ties the release distance exactly (12.0 m), which makes whether "
                + "release needs distance AND a 3 s sight break, or either alone, load-bearing in a way "
                + "§06 never had to decide.");
            Assert.That(lateNightGain, Is.LessThan(GameConstants.AggroReleaseDistance),
                "From 심야 on, §06's arithmetic holds again.");
            Assert.That(lateNightGain, Is.EqualTo(GameConstants.SprintDistanceGain).Within(0.05f),
                "§06/§12's published 9.6 m is the 심야 figure.");
        }

        /// <summary>
        /// <b>Finding.</b> §12 derives its most important number — 14.4 m, the
        /// distance at which a single corner can break line of sight — as 3 s of
        /// cover at 4.8 m/s. §07 runs the monster at 5.0 and then 5.2, which needs
        /// 15.0 m and 15.6 m. Every single-corner escape §12 validates silently stops
        /// working for the last third of the match, while the S-corridor, having
        /// margin, survives.
        /// </summary>
        [Test]
        public void Finding_TheSingleCornerRuleExpiresInTheLateTiers()
        {
            var finalSpeed = ThreatCurve.At(ThreatCurve.FinalTierStartSeconds).MonsterSpeed;
            var neededAtFinalSpeed = GameConstants.AggroReleaseLineOfSightBreak * finalSpeed;

            Assert.That(neededAtFinalSpeed, Is.GreaterThan(GameConstants.SingleCornerMinDistance),
                "§12's 14.4 m single-corner distance is 3 s × 4.8 m/s. At 5.2 m/s the same corner needs "
                + "15.6 m, so maps validated against §12 lose their single-corner escapes after 32 min "
                + "without anything in the document saying so.");

            Assert.That(GameConstants.AggroReleaseLineOfSightBreak * ThreatCurve.Tier(3).MonsterSpeed,
                Is.GreaterThan(GameConstants.SingleCornerMinDistance),
                "It already fails at 새벽, which is the tier §01 schedules the escort in.");

            var sCorridorTransit = GameConstants.SCorridorLegLength * 2f / finalSpeed;
            Assert.That(sCorridorTransit, Is.GreaterThan(GameConstants.AggroReleaseLineOfSightBreak),
                "§12's S-corridor keeps working at 5.2 m/s — it was specified with margin, and that is "
                + "the shape the fix should follow if the corner rule is retuned.");
        }

        // DELETED with §08's carry weight: Finding_TheWeightCliffOnlyBitesFromLateNightOnwards.
        // It refined F-001 by noting the cliff was tier-dependent — a loaded runner
        // at 5.6 × 0.85 = 4.76 m/s still beat the creature's 4.4 and 4.6 but not its
        // 4.8, so "picking up one chest ends the escape" was only true from 심야 on.
        // There is nothing to pick up. What survives, and is asserted above, is the
        // UNLOADED margin against each tier — which is the whole of what §07 does to
        // a race now: it closes the gap on someone who is carrying nothing.


        /// <summary>
        /// <b>Finding.</b> §07's 절반 patrol scope adds nothing on §12's smallest legal
        /// map: half of four zones is two, which is what 밤 already patrolled. On a
        /// 4-zone map the 심야 escalation is speed and light only.
        /// </summary>
        [Test]
        public void Finding_HalfTheMapEscalatesNothingOnAFourZoneMap()
        {
            var night = ThreatCurve.Tier(1).PatrolZoneCountFor(GameConstants.ZoneCountMin);
            var lateNight = ThreatCurve.Tier(2).PatrolZoneCountFor(GameConstants.ZoneCountMin);

            Assert.That(lateNight, Is.EqualTo(night),
                "§07's 순찰 column escalates 1 → 2 → 절반 → 전체, but on a 4-zone map (§12's minimum) "
                + "절반 is 2 — identical to 밤. The 16-minute step then delivers only +0.2 m/s and the "
                + "flashlight penalty, which is a quieter escalation than the table implies.");

            Assert.That(ThreatCurve.Tier(2).PatrolZoneCountFor(GameConstants.ZoneCountMax),
                Is.GreaterThan(ThreatCurve.Tier(1).PatrolZoneCountFor(GameConstants.ZoneCountMax)),
                "On a 6-zone map the same step does escalate. Map size changes what §07 means.");
        }

        /// <summary>
        /// The Runner has to survive the whole night, or §04's role identity ends
        /// mid-match. It does — but §05's numbers thin out badly: the sprint margin
        /// falls from +1.2 m/s to +0.4, and the 45° peek from +0.92 to +0.12.
        /// </summary>
        [Test]
        public void FinalTier_LeavesTheRunnerTheOnlyRoleThatCanFlee_Barely()
        {
            var finalSpeed = ThreatCurve.At(ThreatCurve.FinalTierStartSeconds).MonsterSpeed;

            Assert.That(finalSpeed, Is.LessThan(GameConstants.RunnerSprintSpeed),
                "§04/§06: if the night ever outran the sprint, the Runner would stop being a role.");
            Assert.That(GameConstants.RunnerSprintSpeed * GameConstants.MulDiagonal,
                Is.GreaterThan(finalSpeed),
                "§05's 45° peek must still outpace the monster in the last tier, or the game's stated "
                + "skill expression becomes strictly wrong exactly when it matters most.");

            var finalGain = SprintGainAgainst(ThreatCurve.At(ThreatCurve.FinalTierStartSeconds));
            Assert.That(finalGain, Is.LessThan(GameConstants.SprintDistanceGain),
                "A full sprint buys 4.8 m at 동트기 전 against §12's published 9.6 m, so cover spacing "
                + "tuned at 심야 is roughly twice what the last tier can actually reach.");
            Assert.That(finalGain, Is.GreaterThan(0f), "But it still buys something.");
        }

        // ====================================================================
        // Helpers.
        // ====================================================================

        /// <summary>Every row of the table, in order.</summary>
        private static IEnumerable<ThreatTier> AllTiers()
        {
            for (var i = 0; i < ThreatCurve.TierCount; i++)
            {
                yield return ThreatCurve.Tier(i);
            }
        }

        /// <summary>
        /// Metres a Runner opens on the monster over one full sprint bar, at a given
        /// tier's speed. §06's own calculation, re-run per tier.
        /// </summary>
        private static float SprintGainAgainst(ThreatTier tier) =>
            (GameConstants.RunnerSprintSpeed - tier.MonsterSpeed) * GameConstants.SprintStaminaSeconds;
    }
}
