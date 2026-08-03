using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HorrorGame.Core;
using HorrorGame.Core.Match;
using HorrorGame.Core.Race;
using HorrorGame.Core.Session;
using HorrorGame.Core.Telemetry;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §13 텔레메트리 1단계 — the bucket counters, the sink and the recorder.
    /// <para>
    /// §13's claim is that a distribution falls out of plain counters with no
    /// database behind it. That only holds if the bands are total: Steam Stats
    /// aggregates globally and returns nothing back, so a measurement that fell
    /// outside every band would not go missing, it would silently shrink every
    /// percentage computed from that family — months later, in aggregate, with no
    /// way to tell. Most of what follows is therefore about totality and about
    /// boundaries landing where the counter's own name says they do.
    /// </para>
    /// <para>
    /// Two tests pin contradictions between §13 and the sections it measures.
    /// Those are cross-referenced to docs/BALANCE-FINDINGS.md, and a failure there
    /// means someone retuned deliberately and the finding needs updating in the
    /// same commit.
    /// </para>
    /// </summary>
    [TestFixture]
    public class TelemetryTests
    {
        // ====================================================================
        // §13's literal counter names.
        // ====================================================================

        /// <summary>
        /// Transcribes §13's own code block. The literals here are the design
        /// document, not the implementation — this is the one place they belong, so
        /// that changing a bucket constant fails against the document instead of
        /// quietly renaming a live Steam stat and discarding its history.
        /// </summary>
        [Test]
        public void AggroDurationCounters_TranscribeSection13()
        {
            Assert.That(TelemetryBuckets.AggroDurationBuckets, Is.EqualTo(new[]
            {
                "aggro_duration_0_5s",
                "aggro_duration_5_10s",
                "aggro_duration_10_15s",
                "aggro_duration_15s_plus",
            }), "§13 writes these four counters out by name. Renaming one loses its global history.");
        }

        /// <summary>
        /// Every UNIFORM family follows §13's template: uniform bands, one open tail,
        /// and a name that states its own bounds.
        /// <para>
        /// DeepestStoreyBuckets is deliberately not in this list and does not belong
        /// in it. Its domain is closed — <c>RaceState.Storeys</c> is the whole
        /// building — so it has one band per storey and no open tail. A tail there
        /// could only ever count a bug, and giving it one would make an impossible
        /// storey look like a legitimate top band. It gets its own test below.
        /// </para>
        /// </summary>
        [Test]
        public void EveryUniformFamily_FollowsSection13sTemplate()
        {
            foreach (var family in new[]
            {
                TelemetryBuckets.AggroDurationBuckets,
                TelemetryBuckets.BackpedalShareBuckets,
            })
            {
                Assert.That(family.Count, Is.GreaterThan(1), "A single-band histogram is a counter, not a distribution.");
                Assert.That(family[family.Count - 1], Does.EndWith("_plus"),
                    "§13's template ends every histogram with an open band, or the top of the distribution is lost.");

                for (var i = 0; i < family.Count - 1; i++)
                {
                    Assert.That(family[i], Does.Not.EndWith("_plus"),
                        "Only the last band may be open-ended, or two bands would overlap.");
                }
            }
        }

        // ====================================================================
        // Boundaries: inclusive-exclusive, and where the name says they are.
        // ====================================================================

        /// <summary>
        /// A value landing exactly on a boundary belongs to the band above, and the
        /// largest float below that boundary belongs to the band beneath.
        /// <para>
        /// The boundary is recomputed here the same way the band name is built —
        /// <c>width × i</c> — because that is the actual claim: the number printed
        /// in a counter's name has to be the number that files a measurement into
        /// it. A boundary derived by division instead would round independently and
        /// put a value one band away from its own label.
        /// </para>
        /// </summary>
        [Test]
        public void Boundaries_AreInclusiveBelowAndExclusiveAbove()
        {
            AssertBoundaries(
                TelemetryBuckets.AggroDurationBuckets,
                GameConstants.TelemetryAggroBucketSeconds,
                TelemetryBuckets.AggroDuration);

            AssertBoundaries(
                TelemetryBuckets.BackpedalShareBuckets,
                GameConstants.TelemetryBackpedalShareBucketFraction,
                TelemetryBuckets.BackpedalShare);
        }

        /// <summary>
        /// Every in-domain value lands in a band whose printed label contains it.
        /// <para>
        /// This is the independent check: the label is parsed back into bounds and
        /// the value tested against them, so a scheme that files consistently but
        /// mislabels — the failure that would make a whole histogram unreadable
        /// while every internal assertion still passed — cannot survive.
        /// </para>
        /// </summary>
        [Test]
        public void EveryValue_LandsInABandWhoseLabelContainsIt()
        {
            AssertLabelledCorrectly(
                TelemetryBuckets.AggroDurationHistogram,
                TelemetryBuckets.AggroDurationBuckets,
                GameConstants.TelemetryAggroBucketSeconds,
                1f,
                "s",
                TelemetryBuckets.AggroDuration);

            AssertLabelledCorrectly(
                TelemetryBuckets.BackpedalShareHistogram,
                TelemetryBuckets.BackpedalShareBuckets,
                GameConstants.TelemetryBackpedalShareBucketFraction,
                100f,
                "pct",
                TelemetryBuckets.BackpedalShare);
        }

        /// <summary>
        /// Negatives, zero, ±infinity and NaN are all still bucketed. None of them
        /// can occur — durations, counts and shares are accumulated from
        /// non-negative deltas — but "cannot occur" is not a property the histogram
        /// can rely on, because a dropped observation makes the family's sum
        /// disagree with the number of measurements and every percentage taken from
        /// it wrong by an unknown amount.
        /// </summary>
        [Test]
        public void ExtremeAndImpossibleValues_AreStillBucketed()
        {
            var bottom = new[]
            {
                float.NegativeInfinity, -float.MaxValue, -1e9f, -1f, -float.Epsilon, float.NaN, 0f,
            };

            var top = new[] { float.PositiveInfinity, float.MaxValue, 1e9f };

            foreach (var value in bottom)
            {
                Assert.That(TelemetryBuckets.AggroDuration(value),
                    Is.EqualTo(TelemetryBuckets.AggroDurationBuckets[0]),
                    $"An impossible aggro duration ({value}) must still land in the lowest band.");
                Assert.That(TelemetryBuckets.BackpedalShare(value),
                    Is.EqualTo(TelemetryBuckets.BackpedalShareBuckets[0]),
                    $"An impossible backpedal share ({value}) must still land in the lowest band.");
            }

            foreach (var value in top)
            {
                Assert.That(TelemetryBuckets.AggroDuration(value),
                    Is.EqualTo(Last(TelemetryBuckets.AggroDurationBuckets)),
                    $"A runaway aggro duration ({value}) must land in the open band, not nowhere.");
                Assert.That(TelemetryBuckets.BackpedalShare(value),
                    Is.EqualTo(Last(TelemetryBuckets.BackpedalShareBuckets)),
                    $"A share above 1 ({value}) is impossible but must still land in the open band.");
            }
        }

        /// <summary>
        /// Every storey gets its own counter, so the shape of the distribution — not
        /// just how deep the median got — is visible. §01 makes descending the whole
        /// game, so this is the histogram the building is judged by.
        /// <para>
        /// REPLACES RoundTripBands_GiveEachOfSection03sOutcomesItsOwnCounter, which
        /// pinned one band per return to a 지상 the race does not have.
        /// </para>
        /// </summary>
        [Test]
        public void DeepestStoreyBands_GiveEachStoreyItsOwnCounter()
        {
            Assert.That(TelemetryBuckets.DeepestStoreyBuckets.Count, Is.EqualTo(RaceState.Storeys),
                "One band per storey and not one more — the domain is closed, so there is no tail.");

            for (var storey = 0; storey < RaceState.Storeys; storey++)
            {
                Assert.That(TelemetryBuckets.DeepestStorey(storey),
                    Is.EqualTo("deepest_storey_b" + (storey + 1).ToString(CultureInfo.InvariantCulture)),
                    "The name says the B-number a player would say out loud, so reading a Steam Stats page "
                    + "against §12's floor plans needs no mental minus one.");
            }

            var deepest = Last(TelemetryBuckets.DeepestStoreyBuckets);
            Assert.That(deepest, Is.EqualTo("deepest_storey_b" + RaceState.Storeys.ToString(CultureInfo.InvariantCulture)),
                "B8 carries the finish, so the deepest band is the one that says the race was completable.");

            foreach (var storey in new[] { RaceState.Storeys, RaceState.Storeys + 1, 1000, int.MaxValue })
            {
                Assert.That(TelemetryBuckets.DeepestStorey(storey), Is.EqualTo(deepest),
                    "A storey the map cannot produce is a wiring bug; it is clamped so the family still totals "
                    + "the number of races, and it is legible because it lands on a real floor.");
            }

            foreach (var storey in new[] { -1, -1000, int.MinValue })
            {
                Assert.That(TelemetryBuckets.DeepestStorey(storey),
                    Is.EqualTo(TelemetryBuckets.DeepestStoreyBuckets[0]),
                    "A negative storey is impossible, and must still be counted somewhere.");
            }
        }

        /// <summary>
        /// Where the 후진 비율 tail sits, and why. §13 asks for the share to check
        /// §05's 65% multiplier but names no bands, so the tail is placed at the
        /// point where more precision stops changing the answer.
        /// <para>
        /// A team spending share <c>s</c> of its movement backwards averages
        /// <c>1 − s × (1 − MulBackward)</c> of full speed. The share at which that
        /// average drops to §05's 측면 90% is the point past which they are moving
        /// worse than a player who never faced forward at all — and that share must
        /// fall inside the last finite band, so the tail begins just after it.
        /// </para>
        /// </summary>
        [Test]
        public void BackpedalShareTail_SitsAtSection05sStrafeEquivalence()
        {
            var strafeEquivalentShare = (1f - GameConstants.MulStrafe) / (1f - GameConstants.MulBackward);
            Assert.That(strafeEquivalentShare, Is.EqualTo(0.2857f).Within(0.001f),
                "§05: 10% of speed lost, at 35% lost per second of backpedalling.");

            var tailStart = GameConstants.TelemetryBackpedalShareBucketFraction
                            * (GameConstants.TelemetryBackpedalShareBucketCount - 1);

            Assert.That(strafeEquivalentShare, Is.LessThanOrEqualTo(tailStart),
                "The crossover must be inside the measured range, or the bands stop before the finding does.");
            Assert.That(strafeEquivalentShare,
                Is.GreaterThan(tailStart - GameConstants.TelemetryBackpedalShareBucketFraction),
                "The crossover must fall inside the last finite band, not several bands below the tail.");

            var averageAtTail = 1f - (tailStart * (1f - GameConstants.MulBackward));
            Assert.That(averageAtTail, Is.LessThan(GameConstants.MulStrafe),
                "At the tail the team's average movement is already worse than permanent strafing (§05).");
        }

        // ====================================================================
        // FINDING — §13's aggro bands have no resolution where §06 needs them.
        // ====================================================================

        /// <summary>
        /// Pins a contradiction between §13 and §06 that the document does not
        /// acknowledge, so a later retune cannot erase it silently.
        /// <para>
        /// §13 states that the 어그로 지속 시간 histogram validates §06's 12 m 해제
        /// 거리. Run §06's own arithmetic and the histogram cannot: at the best
        /// sustainable escape speed §05 allows (sprint × the 45° peek = 5.32 m/s)
        /// the Runner gains 0.52 m/s, so opening the full 12 m takes 23 s of
        /// unbroken sprinting — nearly twice the 12 s stamina bar, before the 20 s
        /// refill and §06's "주자도 스태미나가 끝나면 잡힌다" are counted at all.
        /// Every chase that actually depends on the 12 m figure therefore lands in
        /// <c>aggro_duration_15s_plus</c>, a single open band with no resolution
        /// inside it. Even a chase starting halfway there — 6 m to open — already
        /// reaches the last finite band.
        /// </para>
        /// <para>
        /// The lower bands are not empty; they measure something else. A chase that
        /// began beyond 12 m (a Runner taunting from §04's 25 m, or a sighting
        /// across §12's open space) never has to open any distance and ends after
        /// §06's 3 s of broken line of sight alone. So the histogram as specified
        /// answers "how often does a chase start far away", and §06's question is
        /// invisible in it.
        /// </para>
        /// <para>
        /// This is an instrumentation gap rather than a balance error, and the fix
        /// is a designer's call — extend the bands past 15 s, or split the histogram
        /// by start distance. See docs/BALANCE-FINDINGS.md.
        /// </para>
        /// </summary>
        [Test]
        public void AggroDurationBands_CannotResolveTheChasesSection06IsAbout()
        {
            var peekSpeed = GameConstants.RunnerSprintSpeed * GameConstants.MulDiagonal;
            var gainRate = peekSpeed - GameConstants.MonsterBaseSpeed;
            Assert.That(gainRate, Is.EqualTo(0.52f).Within(0.01f), "§05 computes the peek margin at +0.52 m/s.");

            var tailStart = GameConstants.TelemetryAggroBucketSeconds
                            * (GameConstants.TelemetryAggroBucketCount - 1);
            var lastFiniteBandStart = GameConstants.TelemetryAggroBucketSeconds
                                      * (GameConstants.TelemetryAggroBucketCount - 2);

            var secondsToOpenTheReleaseDistance = GameConstants.AggroReleaseDistance / gainRate;
            Assert.That(secondsToOpenTheReleaseDistance, Is.EqualTo(23.08f).Within(0.05f));

            Assert.That(secondsToOpenTheReleaseDistance, Is.GreaterThan(tailStart),
                "§13 says these bands validate §06's 12 m release, but a chase that has to open 12 m "
                + "cannot finish anywhere inside them — it lands in the open tail with everything else. "
                + "If this now passes, the bands were widened on purpose: update docs/BALANCE-FINDINGS.md.");

            Assert.That(secondsToOpenTheReleaseDistance, Is.GreaterThan(GameConstants.SprintStaminaSeconds),
                "§06's own conclusion — 질주만으로는 절대 못 벌린다 — is why the duration lands in the tail.");

            var fromHalfway = ((GameConstants.AggroReleaseDistance * 0.5f) / gainRate)
                              + GameConstants.AggroReleaseLineOfSightBreak;
            Assert.That(fromHalfway, Is.GreaterThan(lastFiniteBandStart),
                "Even 6 m of separation to open already reaches the last finite band.");
            Assert.That(fromHalfway, Is.LessThan(tailStart),
                "So the whole 10–15 s band is a knife edge, and everything harder is in the tail.");

            Assert.That(GameConstants.AggroReleaseLineOfSightBreak,
                Is.LessThan(GameConstants.TelemetryAggroBucketSeconds),
                "The lowest band is filled by chases that started beyond 12 m and only had to break "
                + "line of sight — §12's geometry, not §06's release distance.");
        }

        // ====================================================================
        // §02's verdicts. The old FINDING here is RESOLVED BY DELETION.
        //
        // A test called OutcomeCounters_Section13sThree_CannotClassifySection02sResults
        // stood at this spot. It pinned a real contradiction: §13 asked for three
        // counters (클리어 / 전멸 / 포기) and the co-op §02 defined four results, of
        // which 생존 — "목표물 없이 탈출 — 그 판의 정보는 남는다" — matched none.
        // The whole tension it described was "지금 나갈까?", an argument about
        // leaving with or without the 목표물.
        //
        // The pivot deleted both sides of the contradiction. §02 now has two
        // verdicts and they are total: 완주 carries a place, 탈락 carries none.
        // There is nothing to leave with, nowhere to leave to, and no information
        // that survives a run. The test below is what replaced it.
        // ====================================================================

        /// <summary>
        /// Every outcome, including an undefined value cast in from the network, has
        /// a counter — so the family sums to the number of matches and the clear
        /// rate has a trustworthy denominator.
        /// </summary>
        [Test]
        public void EveryOutcome_HasACounterInTheFamily()
        {
            foreach (var outcome in Enum.GetValues<RacerStatus>())
            {
                Assert.That(TelemetryBuckets.OutcomeCounters, Does.Contain(TelemetryBuckets.Outcome(outcome)),
                    $"{outcome} must be provisionable as a Steam stat.");
            }

            Assert.That(TelemetryBuckets.Outcome(RacerStatus.Finished),
                Is.EqualTo(TelemetryBuckets.OutcomeFinished), "§02 완주.");
            Assert.That(TelemetryBuckets.Outcome(RacerStatus.Eliminated),
                Is.EqualTo(TelemetryBuckets.OutcomeEliminated),
                "§02 탈락 — and it is a counter of its own, not a share of the finishes, because a "
                + "탈락 has no place and so cannot be read off a ranking.");

            Assert.That(TelemetryBuckets.Outcome((RacerStatus)99), Is.EqualTo(TelemetryBuckets.OutcomeUnknown));
            Assert.That(TelemetryBuckets.Outcome(RacerStatus.Running),
                Is.EqualTo(TelemetryBuckets.OutcomeInProgress),
                "A race reported as still running is a caller bug, and must not inflate the finishes.");
        }

        // ====================================================================
        // Categorical families: NONE. This section is empty on purpose.
        //
        // §08's 아이템별 구매 카운터 lived here, covered by
        // PurchaseCounters_CoverEverySection08Item. It went with the shop:
        // DESCENT-PIVOT §3 버린다 drops 전리품 · 크레딧 · 판매 outright ("통화가
        // 없다"), so ShopItemId — the type that test enumerated — no longer exists
        // and there is nothing left to count.
        //
        // §13's 직업별 선택 카운터 5개 lived here too, covered by
        // RolePickCounters_GiveEachSection04RoleItsOwn. It checked §11's absolute
        // rule — "필수 직업이 있으면 풀이 가짜가 된다" — by comparing five role
        // counters against each other. §04 v1.1 deleted 직업: 「캐릭터는 다 똑같이
        // 생겨도되지」. Twenty identical runners have no spread to measure, and a
        // counter family that can only ever report one shape is not a measurement.
        //
        // Do not re-add either without first re-adding the system it counts. Both
        // are design changes, not telemetry changes.
        // ====================================================================


        /// <summary>
        /// The provisioning list must be free of duplicates and blanks: a Steam stat
        /// that was never declared is discarded at runtime with no error, so this
        /// list and the Steamworks page are the same list.
        /// </summary>
        [Test]
        public void AllCounters_AreUniqueNonEmptyAndRecognised()
        {
            var all = TelemetryBuckets.AllCounters;
            Assert.That(all.Count, Is.GreaterThan(0));
            Assert.That(all.Distinct().Count(), Is.EqualTo(all.Count), "Duplicate counter name in the provisioning list.");

            foreach (var name in all)
            {
                Assert.That(string.IsNullOrWhiteSpace(name), Is.False);
                Assert.That(TelemetryBuckets.IsKnownCounter(name), Is.True);
            }

            Assert.That(TelemetryBuckets.IsKnownCounter(TelemetryBuckets.InvalidCounterName), Is.False,
                "The diagnostic rows exist to be noticed, so they must not read as normal traffic.");
            Assert.That(TelemetryBuckets.IsKnownCounter(null), Is.False);
        }

        /// <summary>
        /// An observation whose histogram nobody recognises is not silently dropped.
        /// A dropped measurement makes the histogram it belonged to look merely
        /// unpopular, which is indistinguishable from a real finding.
        /// </summary>
        [Test]
        public void Bucket_UnknownOrBlankHistogram_IsStillCountedSomewhere()
        {
            Assert.That(TelemetryBuckets.Bucket("clue_read_seconds", 3f),
                Is.EqualTo("clue_read_seconds" + TelemetryBuckets.UnbucketedSuffix));
            Assert.That(TelemetryBuckets.Bucket(null, 3f), Is.EqualTo(TelemetryBuckets.InvalidCounterName));
            Assert.That(TelemetryBuckets.Bucket("   ", 3f), Is.EqualTo(TelemetryBuckets.InvalidCounterName));

            Assert.That(TelemetryBuckets.Bucket(TelemetryBuckets.AggroDurationHistogram, 7f),
                Is.EqualTo("aggro_duration_5_10s"));
            Assert.That(TelemetryBuckets.Bucket(TelemetryBuckets.DeepestStoreyHistogram, 3f),
                Is.EqualTo("deepest_storey_b4"));
            Assert.That(TelemetryBuckets.Bucket(TelemetryBuckets.BackpedalShareHistogram, 0.07f),
                Is.EqualTo("backpedal_share_5_10pct"));
        }

        // ====================================================================
        // §13 — 익명 세션 ID만 쓴다. 개인정보는 수집하지 않는다.
        // ====================================================================

        /// <summary>
        /// The session id is checked by shape, so §13's rule holds against
        /// identifier formats nobody enumerated. Each case below is a real thing a
        /// call site might reach for, and all of them are rejected by the same
        /// single rule rather than by a list.
        /// </summary>
        [Test]
        [TestCase("76561198012345678", TestName = "SteamID64")]
        [TestCase("[U:1:52049647]", TestName = "SteamID3")]
        [TestCase("https://steamcommunity.com/id/doogi", TestName = "ProfileUrl")]
        [TestCase("player@example.com", TestName = "Email")]
        [TestCase("192.168.0.14", TestName = "IpAddress")]
        [TestCase("C:\\Users\\doogi\\AppData\\LocalLow", TestName = "WindowsProfilePath")]
        [TestCase("/Users/doogi/Library/Application Support", TestName = "MacProfilePath")]
        [TestCase("DooGi", TestName = "Nickname")]
        [TestCase("0123456789ABCDEF", TestName = "UppercaseHexMightBeAHashOfSomething")]
        [TestCase("0123456789abcde", TestName = "TooShort")]
        [TestCase("0123456789abcdef0", TestName = "TooLong")]
        [TestCase("0123456789abcdeg", TestName = "NotHex")]
        [TestCase("", TestName = "Empty")]
        public void SessionId_AnythingButOurOwnToken_IsScrubbed(string candidate)
        {
            Assert.That(TelemetryPrivacy.IsAnonymousSessionId(candidate), Is.False);
            Assert.That(TelemetryPrivacy.Sanitize(candidate), Is.EqualTo(TelemetryPrivacy.RedactedSessionId),
                "§13 collects no personal data, so a session id that is not ours by shape is replaced.");
        }

        /// <summary>
        /// A missing session id is redacted rather than passed through as null, so
        /// nothing downstream has to branch on it and "no id" is visibly the same
        /// row as "an id we refused".
        /// </summary>
        [Test]
        public void SessionId_Null_IsRedactedNotPassedThrough()
        {
            Assert.That(TelemetryPrivacy.IsAnonymousSessionId(null), Is.False);
            Assert.That(TelemetryPrivacy.Sanitize(null), Is.EqualTo(TelemetryPrivacy.RedactedSessionId));
        }

        /// <summary>
        /// A generated id passes, and it is reproducible from the seed — §13's
        /// diagnosis loop is "replay the seed", so the id a match reported has to
        /// come back with it. Anonymity is unaffected: the seed identifies a match,
        /// never a person.
        /// </summary>
        [Test]
        public void NewSessionId_IsAcceptedAndReproducibleFromTheSeed()
        {
            var a = TelemetryPrivacy.NewSessionId(new DeterministicRandom(20260730));
            var b = TelemetryPrivacy.NewSessionId(new DeterministicRandom(20260730));
            var other = TelemetryPrivacy.NewSessionId(new DeterministicRandom(20260731));

            Assert.That(TelemetryPrivacy.IsAnonymousSessionId(a), Is.True);
            Assert.That(TelemetryPrivacy.Sanitize(a), Is.EqualTo(a));
            Assert.That(b, Is.EqualTo(a), "The same seed must reproduce the same session id.");
            Assert.That(other, Is.Not.EqualTo(a), "Adjacent seeds must not collide.");
            Assert.That(a.Length, Is.EqualTo(TelemetryPrivacy.SessionIdLength));

            Assert.Throws<ArgumentNullException>(() => TelemetryPrivacy.NewSessionId(null!));
        }

        // ====================================================================
        // InMemoryTelemetrySink.
        // ====================================================================

        /// <summary>Counters accumulate, and a decrement — which Steam Stats cannot represent — is refused and counted.</summary>
        [Test]
        public void Sink_Increment_AccumulatesAndRefusesDecrements()
        {
            var sink = new InMemoryTelemetrySink();

            sink.Increment("round_trips_3");
            sink.Increment("round_trips_3", 4);
            Assert.That(sink.Count("round_trips_3"), Is.EqualTo(5));

            sink.Increment("round_trips_3", 0);
            sink.Increment("round_trips_3", -2);
            Assert.That(sink.Count("round_trips_3"), Is.EqualTo(5),
                "A globally aggregated counter that can go down cannot be read as a total.");
            Assert.That(sink.RejectedIncrements, Is.EqualTo(2));

            Assert.That(sink.Count("never_touched"), Is.EqualTo(0));
            Assert.That(sink.Count(null), Is.EqualTo(0));
        }

        /// <summary>A blank counter name is counted where it can be seen rather than thrown or dropped.</summary>
        [Test]
        public void Sink_Increment_BlankName_GoesToTheDiagnosticCounter()
        {
            var sink = new InMemoryTelemetrySink();
            sink.Increment(null!);
            sink.Increment("");
            sink.Increment("   ");

            Assert.That(sink.Count(TelemetryBuckets.InvalidCounterName), Is.EqualTo(3));
            Assert.That(sink.UnknownCounterNames(), Does.Contain(TelemetryBuckets.InvalidCounterName));
        }

        /// <summary>A counter saturates instead of wrapping: a wrapped total reads as a plausible small number.</summary>
        [Test]
        public void Sink_Increment_SaturatesInsteadOfWrapping()
        {
            var sink = new InMemoryTelemetrySink();
            sink.Increment("outcome_clear", int.MaxValue);
            sink.Increment("outcome_clear", 100);

            Assert.That(sink.Count("outcome_clear"), Is.EqualTo(int.MaxValue));
        }

        /// <summary>
        /// <see cref="ITelemetrySink.Observe"/> takes the raw measurement and the
        /// sink appends the band, per its own contract. The raw values are kept too,
        /// so a proposed re-banding can be checked against matches already run.
        /// </summary>
        [Test]
        public void Sink_Observe_BandsTheValueAndKeepsTheMeasurement()
        {
            var sink = new InMemoryTelemetrySink();
            var values = new[] { 1f, 4.9f, 5f, 12f, 15f, 99f };

            foreach (var v in values)
            {
                sink.Observe(TelemetryBuckets.AggroDurationHistogram, v);
            }

            Assert.That(sink.Observations(TelemetryBuckets.AggroDurationHistogram), Is.EqualTo(values));
            Assert.That(sink.Count("aggro_duration_0_5s"), Is.EqualTo(2));
            Assert.That(sink.Count("aggro_duration_5_10s"), Is.EqualTo(1));
            Assert.That(sink.Count("aggro_duration_10_15s"), Is.EqualTo(1));
            Assert.That(sink.Count("aggro_duration_15s_plus"), Is.EqualTo(2));

            Assert.That(sink.Observations("never_observed"), Is.Empty);
            Assert.That(sink.Observations(null), Is.Empty);
        }

        /// <summary>
        /// The totality claim, end to end: however hostile the measurements, the
        /// bands sum to the number of observations. §13's percentages are only
        /// readable while this holds.
        /// </summary>
        [Test]
        public void Sink_BandsAlwaysSumToTheNumberOfObservations()
        {
            var sink = new InMemoryTelemetrySink();
            var values = new[]
            {
                float.NaN, float.NegativeInfinity, float.PositiveInfinity, -1f, 0f,
                float.Epsilon, 4.999f, 5f, 5.001f, 10f, 14.999f, 15f, 1e30f, float.MaxValue,
            };

            foreach (var v in values)
            {
                sink.Observe(TelemetryBuckets.AggroDurationHistogram, v);
            }

            Assert.That(sink.TotalIn(TelemetryBuckets.AggroDurationBuckets), Is.EqualTo(values.Length),
                "A measurement outside every band would not go missing — it would silently shrink "
                + "every percentage computed from this family.");
            Assert.That(sink.UnknownCounterNames(), Is.Empty);
        }

        /// <summary>
        /// The sink sanitises the session id even when the summary was assembled by
        /// hand. <see cref="MatchSummary"/> is a plain struct, so the last gate
        /// before the data leaves the process has to be the one that holds (§13).
        /// </summary>
        [Test]
        public void Sink_RecordMatchSummary_ScrubsTheSessionIdItWasGiven()
        {
            var sink = new InMemoryTelemetrySink();
            sink.RecordMatchSummary(new MatchSummary { SessionId = "76561198012345678", Seed = 7 });

            Assert.That(sink.Summaries.Count, Is.EqualTo(1));
            Assert.That(sink.LastSummary!.Value.SessionId, Is.EqualTo(TelemetryPrivacy.RedactedSessionId));
            Assert.That(sink.LastSummary!.Value.Seed, Is.EqualTo(7), "Only the session id is touched.");
        }

        /// <summary>Flush is counted, not destructive: a test asserts after the fact, and whether the flush happened is itself worth asserting.</summary>
        [Test]
        public void Sink_FlushIsObservable_AndClearResetsEverything()
        {
            var sink = new InMemoryTelemetrySink();
            sink.Increment("outcome_wipe");
            sink.Observe(TelemetryBuckets.AggroDurationHistogram, 2f);
            sink.RecordMatchSummary(default);
            sink.Flush();

            Assert.That(sink.FlushCount, Is.EqualTo(1));
            Assert.That(sink.Count("outcome_wipe"), Is.EqualTo(1));

            sink.Clear();

            Assert.That(sink.FlushCount, Is.EqualTo(0));
            Assert.That(sink.Counters, Is.Empty);
            Assert.That(sink.Summaries, Is.Empty);
            Assert.That(sink.LastSummary, Is.Null);
            Assert.That(sink.Observations(TelemetryBuckets.AggroDurationHistogram), Is.Empty);
        }

        // ====================================================================
        // MatchRecorder — a scripted match against a hand-computed expectation.
        // ====================================================================

        /// <summary>
        /// Runs a scripted match and checks every field of the summary against a
        /// figure worked out by hand, plus every counter the sink should have seen.
        /// <para>
        /// The script deliberately contains the deltas that must be ignored (zero,
        /// negative, NaN, infinite), a chase that is still running when the match
        /// ends, and movement samples that are not moving — so the expectation below
        /// is only reachable if all of those are handled.
        /// </para>
        /// </summary>
        [Test]
        public void Recorder_ScriptedMatch_MatchesAHandComputedSummary()
        {
            var recorder = RunScriptedMatch(out var sink);
            var summary = recorder.Complete(RacerStatus.Finished);

            // Time: 60 + 600 + 90 of quiet, then the three chases that were ticked
            // (4 + 12 + 6). The zero, negative, NaN and infinite deltas contribute
            // nothing.
            Assert.That(summary.DurationSeconds, Is.EqualTo(772f).Within(1e-3f));

            Assert.That(summary.Seed, Is.EqualTo(ScriptedSeed));
            Assert.That(summary.MapId, Is.EqualTo(ScriptedMapId));
            Assert.That(summary.SessionId, Is.EqualTo(recorder.SessionId));
            Assert.That(TelemetryPrivacy.IsAnonymousSessionId(summary.SessionId), Is.True);
            Assert.That(summary.Outcome, Is.EqualTo(RacerStatus.Finished));

            Assert.That(summary.DeepestStorey, Is.EqualTo(3),
                "B4 was the deepest reached; the later B2 arrival must not lower it.");
            Assert.That(summary.RunnersFinished, Is.EqualTo(3));
            Assert.That(summary.RunnersEliminated, Is.EqualTo(1));

            // Four chases: 4 s, 12 s, an event reported directly at 20 s, and 6 s
            // that was still running when Complete closed the match.
            Assert.That(summary.AggroEvents, Is.EqualTo(4));
            Assert.That(summary.TotalAggroSeconds, Is.EqualTo(42f).Within(1e-3f));
            Assert.That(summary.LongestAggroSeconds, Is.EqualTo(20f).Within(1e-3f));
            Assert.That(summary.AverageAggroSeconds, Is.EqualTo(10.5f).Within(1e-3f));

            // 100 s forward + 20 s backward + 5 s backward-while-"not moving"
            // (backpedalling is movement), and 50 s of standing still ignored.
            Assert.That(summary.TotalMovingSeconds, Is.EqualTo(125f).Within(1e-3f));
            Assert.That(summary.BackpedalSeconds, Is.EqualTo(25f).Within(1e-3f));
            Assert.That(summary.BackpedalRatio, Is.EqualTo(0.2f).Within(1e-4f));

            // What the sink should have seen.
            Assert.That(sink.Observations(TelemetryBuckets.AggroDurationHistogram),
                Is.EqualTo(new[] { 4f, 12f, 20f, 6f }));
            Assert.That(sink.Count("aggro_duration_0_5s"), Is.EqualTo(1));
            Assert.That(sink.Count("aggro_duration_5_10s"), Is.EqualTo(1));
            Assert.That(sink.Count("aggro_duration_10_15s"), Is.EqualTo(1));
            Assert.That(sink.Count("aggro_duration_15s_plus"), Is.EqualTo(1));
            Assert.That(sink.TotalIn(TelemetryBuckets.AggroDurationBuckets), Is.EqualTo(summary.AggroEvents));

            Assert.That(sink.Count(TelemetryBuckets.OutcomeFinished), Is.EqualTo(1));
            Assert.That(sink.TotalIn(TelemetryBuckets.OutcomeCounters), Is.EqualTo(1),
                "One race, one outcome.");

            Assert.That(sink.Count("deepest_storey_b4"), Is.EqualTo(1));
            Assert.That(sink.Count("backpedal_share_20_25pct"), Is.EqualTo(1));

            Assert.That(sink.Summaries.Count, Is.EqualTo(1));
            Assert.That(sink.FlushCount, Is.EqualTo(1));
            Assert.That(sink.UnknownCounterNames(), Is.Empty,
                "Every counter a real match emits must be provisionable in Steamworks.");
        }

        /// <summary>
        /// Per-match figures cannot exist before the match ends, so nothing about
        /// the outcome, the roster or the shares is emitted early — while per-event
        /// measurements already are, so an abandoned match still contributes what it
        /// observed.
        /// </summary>
        [Test]
        public void Recorder_BeforeComplete_EmitsEventsButNoMatchLevelCounters()
        {
            var recorder = RunScriptedMatch(out var sink);

            Assert.That(sink.Count("aggro_duration_10_15s"), Is.EqualTo(1), "A chase that ended is an event.");

            Assert.That(sink.TotalIn(TelemetryBuckets.OutcomeCounters), Is.EqualTo(0));
            Assert.That(sink.TotalIn(TelemetryBuckets.DeepestStoreyBuckets), Is.EqualTo(0),
                "A race still being run must not file a depth — only the final figure answers the question.");
            Assert.That(sink.TotalIn(TelemetryBuckets.BackpedalShareBuckets), Is.EqualTo(0));
            Assert.That(sink.Summaries, Is.Empty);
            Assert.That(sink.FlushCount, Is.EqualTo(0));

            Assert.That(recorder.Snapshot().Outcome, Is.EqualTo(RacerStatus.Running));
            Assert.That(recorder.IsComplete, Is.False);
        }

        /// <summary>
        /// A chase still running when the match ends is counted. This is not an edge
        /// case — a wipe happens *during* a chase, so discarding the open one would
        /// systematically drop the longest chases, exactly the population §06's
        /// release distance has to be judged against, and the surviving data would
        /// claim chases are short and easily escaped.
        /// </summary>
        [Test]
        public void Recorder_ChaseStillRunningAtTheEnd_IsCounted()
        {
            var sink = new InMemoryTelemetrySink();
            var recorder = new MatchRecorder(sink, 1);

            recorder.SetAggroActive(true);
            recorder.Tick(GameConstants.SprintStaminaSeconds);
            Assert.That(recorder.IsAggroActive, Is.True);
            Assert.That(recorder.Snapshot().AggroEvents, Is.EqualTo(0),
                "An unclosed chase must not reach the summary early — AverageAggroSeconds divides by the count.");
            Assert.That(recorder.CurrentAggroSeconds,
                Is.EqualTo(GameConstants.SprintStaminaSeconds).Within(1e-3f));

            var summary = recorder.Complete(RacerStatus.Eliminated);

            Assert.That(summary.AggroEvents, Is.EqualTo(1));
            Assert.That(summary.TotalAggroSeconds,
                Is.EqualTo(GameConstants.SprintStaminaSeconds).Within(1e-3f));
            Assert.That(summary.LongestAggroSeconds,
                Is.EqualTo(GameConstants.SprintStaminaSeconds).Within(1e-3f));
            Assert.That(sink.Count("aggro_duration_10_15s"), Is.EqualTo(1),
                "12 s of stamina lands in §13's third band.");
        }

        /// <summary>
        /// Releasing and reacquiring inside one step is two chases, and the first
        /// may be zero seconds long. §06's question is how easily aggro ends, so an
        /// acquisition that ended immediately is a data point, not noise.
        /// </summary>
        [Test]
        public void Recorder_SimultaneousReleaseAndReacquire_CountsTwoChases()
        {
            var sink = new InMemoryTelemetrySink();
            var recorder = new MatchRecorder(sink, 1);

            recorder.SetAggroActive(true);
            recorder.SetAggroActive(true);
            Assert.That(recorder.Snapshot().AggroEvents, Is.EqualTo(0), "Setting the same state twice is not an event.");

            recorder.SetAggroActive(false);
            recorder.SetAggroActive(true);
            recorder.Tick(6f);
            var summary = recorder.Complete(RacerStatus.Eliminated);

            Assert.That(summary.AggroEvents, Is.EqualTo(2));
            Assert.That(summary.TotalAggroSeconds, Is.EqualTo(6f).Within(1e-3f));
            Assert.That(sink.Observations(TelemetryBuckets.AggroDurationHistogram), Is.EqualTo(new[] { 0f, 6f }));
            Assert.That(sink.TotalIn(TelemetryBuckets.AggroDurationBuckets), Is.EqualTo(2));
        }

        /// <summary>
        /// Degenerate deltas are ignored rather than clamped. A NaN reaching the
        /// accumulators would make every field of the summary NaN for the rest of
        /// the match, and the loss would only surface once the data had been
        /// aggregated with everyone else's.
        /// </summary>
        [Test]
        public void Recorder_DegenerateDeltas_ChangeNothing()
        {
            var sink = new InMemoryTelemetrySink();
            var recorder = new MatchRecorder(sink, 1);

            recorder.SetAggroActive(true);
            foreach (var delta in new[] { 0f, -1f, -1e9f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                recorder.Tick(delta);
                recorder.RecordMovement(delta, true, true);
            }

            var summary = recorder.Complete(RacerStatus.Eliminated);

            Assert.That(summary.DurationSeconds, Is.EqualTo(0f));
            Assert.That(summary.TotalAggroSeconds, Is.EqualTo(0f));
            Assert.That(summary.TotalMovingSeconds, Is.EqualTo(0f));
            Assert.That(summary.BackpedalSeconds, Is.EqualTo(0f));
            Assert.That(float.IsNaN(summary.BackpedalRatio), Is.False);
            Assert.That(float.IsNaN(summary.AverageAggroSeconds), Is.False);
            Assert.That(sink.Count(TelemetryBuckets.OutcomeEliminated), Is.EqualTo(1));
        }

        /// <summary>
        /// A frame spike lengthens the chase it happened during rather than losing
        /// it or skipping past it. Nothing here infers a transition from elapsed
        /// time, and a chase's band is decided from its total when it closes, so
        /// there is no threshold for a spike to tunnel through.
        /// </summary>
        [Test]
        public void Recorder_FrameSpike_LandsInTheBandItsLengthDeserves()
        {
            var sink = new InMemoryTelemetrySink();
            var recorder = new MatchRecorder(sink, 1);

            var spike = GameConstants.TelemetryAggroBucketSeconds * GameConstants.TelemetryAggroBucketCount * 10f;

            recorder.SetAggroActive(true);
            recorder.Tick(spike);
            recorder.SetAggroActive(false);

            Assert.That(sink.Count(Last(TelemetryBuckets.AggroDurationBuckets)), Is.EqualTo(1));
            Assert.That(recorder.Snapshot().DurationSeconds, Is.EqualTo(spike).Within(1e-2f),
                "§07's night does not owe the team the seconds their machine dropped.");
            Assert.That(recorder.Snapshot().AggroEvents, Is.EqualTo(1), "The chase is not lost, and not doubled.");
        }

        /// <summary>
        /// Backpedalling is movement whatever the caller's threshold says, so the
        /// share can never exceed 1 — a value no consumer of a ratio would think to
        /// check.
        /// </summary>
        [Test]
        public void Recorder_BackwardImpliesMoving_SoTheShareCannotExceedOne()
        {
            var sink = new InMemoryTelemetrySink();
            var recorder = new MatchRecorder(sink, 1);

            recorder.RecordMovement(10f, false, true);
            recorder.RecordMovement(10f, false, false);

            var summary = recorder.Complete(RacerStatus.Eliminated);

            Assert.That(summary.TotalMovingSeconds, Is.EqualTo(10f).Within(1e-3f));
            Assert.That(summary.BackpedalSeconds, Is.EqualTo(10f).Within(1e-3f));
            Assert.That(summary.BackpedalRatio, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(summary.BackpedalRatio, Is.LessThanOrEqualTo(1f));
            Assert.That(sink.Count(Last(TelemetryBuckets.BackpedalShareBuckets)), Is.EqualTo(1),
                "A share of 1 is past §05's strafe equivalence and belongs in the open band.");
        }

        /// <summary>
        /// A non-finite chase length costs one data point instead of the whole
        /// match. Poisoning <see cref="MatchSummary.TotalAggroSeconds"/> with NaN
        /// would take <see cref="MatchSummary.AverageAggroSeconds"/> and every
        /// aggregate built on it with no visible symptom.
        /// </summary>
        [Test]
        public void Recorder_NonFiniteChaseLength_DoesNotPoisonTheSummary()
        {
            var sink = new InMemoryTelemetrySink();
            var recorder = new MatchRecorder(sink, 1);

            recorder.RecordAggroEvent(float.NaN);
            recorder.RecordAggroEvent(float.PositiveInfinity);
            recorder.RecordAggroEvent(-5f);
            recorder.RecordAggroEvent(8f);

            var summary = recorder.Complete(RacerStatus.Eliminated);

            Assert.That(summary.AggroEvents, Is.EqualTo(4), "The acquisitions were real even if the timings were not.");
            Assert.That(summary.TotalAggroSeconds, Is.EqualTo(8f).Within(1e-3f));
            Assert.That(summary.LongestAggroSeconds, Is.EqualTo(8f).Within(1e-3f));
            Assert.That(summary.AverageAggroSeconds, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(sink.TotalIn(TelemetryBuckets.AggroDurationBuckets), Is.EqualTo(4));
        }

        /// <summary>
        /// Completing twice reports once. "The local runner was caught" racing "the
        /// last finisher arrived" is an ordinary way to arrive here twice, and a
        /// double-counted race biases every global average with no way to decrement a
        /// Steam counter back.
        /// </summary>
        [Test]
        public void Recorder_CompleteTwice_ReportsOnce()
        {
            var sink = new InMemoryTelemetrySink();
            var recorder = new MatchRecorder(sink, 42);

            recorder.Tick(10f);

            var first = recorder.Complete(RacerStatus.Eliminated);
            var second = recorder.Complete(RacerStatus.Finished);

            Assert.That(recorder.IsComplete, Is.True);
            Assert.That(second.Outcome, Is.EqualTo(first.Outcome),
                "The second call must not be able to rewrite how the match ended.");
            Assert.That(sink.Summaries.Count, Is.EqualTo(1));
            Assert.That(sink.FlushCount, Is.EqualTo(1));
            Assert.That(sink.TotalIn(TelemetryBuckets.OutcomeCounters), Is.EqualTo(1));
            Assert.That(recorder.Snapshot().Outcome, Is.EqualTo(RacerStatus.Eliminated));
        }

        /// <summary>Events arriving after the match closed cannot change a summary that was already sent.</summary>
        [Test]
        public void Recorder_EventsAfterComplete_AreIgnored()
        {
            var sink = new InMemoryTelemetrySink();
            var recorder = new MatchRecorder(sink, 1);
            var summary = recorder.Complete(RacerStatus.Eliminated);

            recorder.Tick(100f);
            recorder.SetAggroActive(true);
            recorder.RecordAggroEvent(30f);
            recorder.RecordMovement(10f, true, true);
            recorder.RecordStoreyReached(7);
            recorder.RecordRunnerEliminated();
            recorder.RecordRunnerFinished();

            Assert.That(recorder.Snapshot(), Is.EqualTo(summary));
            Assert.That(sink.Summaries.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A session id handed to the recorder is sanitised at construction, so no
        /// call site can leak one through the summary (§13).
        /// </summary>
        [Test]
        public void Recorder_SanitisesTheSessionIdItWasGiven()
        {
            var leaky = new MatchRecorder(new InMemoryTelemetrySink(), 1, "76561198012345678");
            Assert.That(leaky.SessionId, Is.EqualTo(TelemetryPrivacy.RedactedSessionId));

            var clean = TelemetryPrivacy.NewSessionId(new DeterministicRandom(9));
            var ok = new MatchRecorder(new InMemoryTelemetrySink(), 1, clean);
            Assert.That(ok.SessionId, Is.EqualTo(clean));

            Assert.Throws<ArgumentNullException>(() => new MatchRecorder(null!, 1));
        }

        /// <summary>
        /// Runner counts are clamped at §11's twenty, and a storey the map cannot
        /// produce is ignored rather than clamped into the deepest band.
        /// <para>
        /// The clamp is the point: a summary that says twenty-three runners finished
        /// a twenty-person race is worse than one that says twenty, because nobody
        /// aggregating it would notice. The storey is the opposite case and gets the
        /// opposite treatment — inventing a B8 arrival out of a bad index would put
        /// wiring bugs in exactly the band the whole histogram exists to read.
        /// </para>
        /// </summary>
        [Test]
        public void Recorder_ClampsRunnerCounts_AndIgnoresImpossibleStoreys()
        {
            var sink = new InMemoryTelemetrySink();
            var recorder = new MatchRecorder(sink, 1);

            for (var i = 0; i < GameConstants.RaceRunnersMax + 3; i++)
            {
                recorder.RecordRunnerEliminated();
                recorder.RecordRunnerFinished();
            }

            recorder.RecordStoreyReached(3);
            recorder.RecordStoreyReached(RaceState.Storeys);
            recorder.RecordStoreyReached(-1);
            recorder.RecordStoreyReached(1);

            var summary = recorder.Complete(RacerStatus.Eliminated);

            Assert.That(summary.RunnersEliminated, Is.EqualTo(GameConstants.RaceRunnersMax));
            Assert.That(summary.RunnersFinished, Is.EqualTo(GameConstants.RaceRunnersMax));
            Assert.That(summary.DeepestStorey, Is.EqualTo(3),
                "the two out-of-range calls changed nothing, and going back UP a storey does not "
                + "lower the deepest — §09's spectator flies the whole building after the runner dies");
        }

        // ====================================================================
        // MatchSummary's derived properties, including the degenerate cases.
        // ====================================================================

        /// <summary>
        /// <see cref="MatchSummary.BackpedalRatio"/> is the share of movement time,
        /// and is zero rather than NaN when nothing moved. §05's peek dilemma is
        /// working when this stays low but never reaches zero, so a NaN here would
        /// read as the strongest possible finding.
        /// </summary>
        [Test]
        public void BackpedalRatio_IsTheShareOfMovementTime_AndZeroWhenNothingMoved()
        {
            var still = new MatchSummary { BackpedalSeconds = 0f, TotalMovingSeconds = 0f };
            Assert.That(still.BackpedalRatio, Is.EqualTo(0f));

            var impossible = new MatchSummary { BackpedalSeconds = 5f, TotalMovingSeconds = 0f };
            Assert.That(impossible.BackpedalRatio, Is.EqualTo(0f),
                "Backpedal time with no movement time is impossible; it must not divide by zero.");
            Assert.That(float.IsNaN(impossible.BackpedalRatio), Is.False);

            var real = new MatchSummary { BackpedalSeconds = 25f, TotalMovingSeconds = 125f };
            Assert.That(real.BackpedalRatio, Is.EqualTo(0.2f).Within(1e-5f));

            var all = new MatchSummary { BackpedalSeconds = 30f, TotalMovingSeconds = 30f };
            Assert.That(all.BackpedalRatio, Is.EqualTo(1f).Within(1e-5f));
        }

        /// <summary>
        /// Pins the consequence of <see cref="MatchSummary.BackpedalRatio"/>'s
        /// divide-by-zero guard being a threshold rather than a zero test: a match
        /// with a millisecond or less of movement reports a 0% share even when every
        /// one of those milliseconds was backwards.
        /// <para>
        /// Harmless in practice — one fixed step is
        /// <see cref="GameConstants.FixedStep"/> = 20 ms, twenty times the guard, so
        /// any match with a single moving frame is above it — but it is a real
        /// discontinuity and it belongs in a test rather than in someone's memory.
        /// </para>
        /// </summary>
        [Test]
        public void BackpedalRatio_BelowItsOwnGuard_ReportsZeroRatherThanTheTrueShare()
        {
            var sliver = new MatchSummary { BackpedalSeconds = 0.001f, TotalMovingSeconds = 0.001f };
            Assert.That(sliver.BackpedalRatio, Is.EqualTo(0f),
                "The guard is 'greater than 0.001', so exactly one millisecond of movement reports nothing.");

            var oneStep = new MatchSummary
            {
                BackpedalSeconds = GameConstants.FixedStep,
                TotalMovingSeconds = GameConstants.FixedStep,
            };
            Assert.That(oneStep.BackpedalRatio, Is.EqualTo(1f).Within(1e-4f),
                "One fixed step is far above the guard, so no real match is affected.");
        }

        /// <summary>
        /// <see cref="MatchSummary.AverageAggroSeconds"/> is §06's release tuning
        /// target, and is zero rather than NaN for a match in which the monster
        /// never acquired anyone — which is an ordinary, informative result, not an
        /// error.
        /// </summary>
        [Test]
        public void AverageAggroSeconds_IsTheMean_AndZeroWithNoEvents()
        {
            var untouched = new MatchSummary { AggroEvents = 0, TotalAggroSeconds = 0f };
            Assert.That(untouched.AverageAggroSeconds, Is.EqualTo(0f));

            var inconsistent = new MatchSummary { AggroEvents = 0, TotalAggroSeconds = 30f };
            Assert.That(inconsistent.AverageAggroSeconds, Is.EqualTo(0f),
                "Chase seconds with no chases is impossible; it must not divide by zero.");
            Assert.That(float.IsNaN(inconsistent.AverageAggroSeconds), Is.False);

            var real = new MatchSummary { AggroEvents = 4, TotalAggroSeconds = 42f };
            Assert.That(real.AverageAggroSeconds, Is.EqualTo(10.5f).Within(1e-4f));
            Assert.That(TelemetryBuckets.AggroDuration(real.AverageAggroSeconds),
                Is.EqualTo("aggro_duration_10_15s"));
        }

        // ====================================================================
        // Helpers.
        // ====================================================================

        private const int ScriptedSeed = 20260730;
        private const string ScriptedMapId = "manor_basement_01";

        /// <summary>
        /// The scripted match, stopping short of <see cref="MatchRecorder.Complete"/>
        /// so that both the "nothing emitted early" test and the full-summary test
        /// can drive the same script.
        /// </summary>
        private static MatchRecorder RunScriptedMatch(out InMemoryTelemetrySink sink)
        {
            sink = new InMemoryTelemetrySink();
            var recorder = new MatchRecorder(
                sink,
                ScriptedSeed,
                TelemetryPrivacy.NewSessionId(new DeterministicRandom(ScriptedSeed)),
                ScriptedMapId);

            // Deltas that must not register at all.
            recorder.Tick(0f);
            recorder.Tick(-1f);
            recorder.Tick(float.NaN);
            recorder.Tick(float.PositiveInfinity);

            recorder.Tick(60f);
            recorder.Tick(600f);
            recorder.Tick(90f);

            // Chase 1: 4 s. Chase 2: 12 s. Chase 3 reported directly at 20 s.
            // Chase 4 opens, runs 6 s and is still open when the match ends.
            recorder.SetAggroActive(true);
            recorder.Tick(4f);
            recorder.SetAggroActive(false);
            recorder.SetAggroActive(true);
            recorder.Tick(12f);
            recorder.SetAggroActive(false);
            recorder.RecordAggroEvent(20f);
            recorder.SetAggroActive(true);
            recorder.Tick(6f);

            recorder.RecordMovement(100f, true, false);
            recorder.RecordMovement(20f, true, true);
            recorder.RecordMovement(5f, false, true);
            recorder.RecordMovement(50f, false, false);
            recorder.RecordMovement(0f, true, false);

            // Down to B4, then back up to B2 — the storey the summary must report is
            // the deepest one ever reached, not the last one visited.
            recorder.RecordStoreyReached(0);
            recorder.RecordStoreyReached(1);
            recorder.RecordStoreyReached(3);
            recorder.RecordStoreyReached(1);

            recorder.RecordRunnerEliminated();
            recorder.RecordRunnerFinished();
            recorder.RecordRunnerFinished();
            recorder.RecordRunnerFinished();

            return recorder;
        }

        private static void AssertBoundaries(
            IReadOnlyList<string> family, float width, Func<float, string> classify)
        {
            for (var i = 1; i < family.Count; i++)
            {
                var boundary = width * i;

                Assert.That(classify(boundary), Is.EqualTo(family[i]),
                    $"A value exactly on {boundary} belongs to the band above it — bands are [low, high).");
                Assert.That(classify(MathF.BitDecrement(boundary)), Is.EqualTo(family[i - 1]),
                    $"The largest value below {boundary} belongs to the band beneath it.");
            }
        }

        private static void AssertLabelledCorrectly(
            string prefix,
            IReadOnlyList<string> family,
            float width,
            float displayScale,
            string unit,
            Func<float, string> classify)
        {
            // Three points inside each band, none of them on a boundary — the
            // boundaries have their own test, and keeping them out of this one means
            // no float knife edge can obscure a genuine labelling error.
            var offsets = new[] { 0.1f, 0.5f, 0.9f };

            for (var i = 0; i < family.Count; i++)
            {
                foreach (var offset in offsets)
                {
                    var value = (width * i) + (width * offset);
                    var counter = classify(value);

                    Assert.That(counter, Is.EqualTo(family[i]));
                    AssertLabelContains(counter, prefix, unit, displayScale, value);
                }
            }

            // And one value far above the top band's own lower bound.
            var beyond = width * family.Count * 100f;
            AssertLabelContains(classify(beyond), prefix, unit, displayScale, beyond);
        }

        /// <summary>
        /// Parses a band's bounds back out of its own name and checks the value
        /// against them. This is the independent half of the claim: a scheme could
        /// file every value consistently and still label the bands wrongly, and
        /// nothing inside the implementation would notice.
        /// </summary>
        private static void AssertLabelContains(
            string counter, string prefix, string unit, float displayScale, float value)
        {
            Assert.That(counter, Does.StartWith(prefix + "_"));
            var body = counter.Substring(prefix.Length + 1);

            float low;
            float high;

            if (body.EndsWith("_plus", StringComparison.Ordinal))
            {
                var lowText = Strip(body.Substring(0, body.Length - "_plus".Length), unit);
                low = ParseDisplay(lowText) / displayScale;
                high = float.PositiveInfinity;
            }
            else
            {
                var parts = Strip(body, unit).Split('_');
                Assert.That(parts.Length, Is.EqualTo(2), $"'{counter}' does not state a range.");
                low = ParseDisplay(parts[0]) / displayScale;
                high = ParseDisplay(parts[1]) / displayScale;
            }

            Assert.That(value, Is.GreaterThanOrEqualTo(low), $"{value} is below the range '{counter}' claims.");
            Assert.That(value, Is.LessThan(high), $"{value} is above the range '{counter}' claims.");
        }

        private static string Strip(string text, string unit) =>
            unit.Length == 0 || !text.EndsWith(unit, StringComparison.Ordinal)
                ? text
                : text.Substring(0, text.Length - unit.Length);

        private static float ParseDisplay(string text) =>
            float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static string Last(IReadOnlyList<string> names) => names[names.Count - 1];
    }
}
