using System;
using HorrorGame.Core;
using HorrorGame.Core.Abilities;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// The five §04 abilities, asserted against the reasoning §04 gives for them
    /// rather than against their implementations.
    /// <para>
    /// Each role in §04 is a strong ability plus a specific price, and the price is
    /// the part that decays first under maintenance: it is always tempting to smooth
    /// away the Listener's blindness, to let the Observer's timer resume, or to stop
    /// the Engineer from locking a teammate in. These tests exist so that any of
    /// those "fixes" fails the build and has to be argued for.
    /// </para>
    /// </summary>
    [TestFixture]
    public class AbilityTests
    {
        // ====================================================================
        // Fixtures.
        // ====================================================================

        /// <summary>
        /// A hand-written <see cref="IWorldProbe"/>. Answers are fields so a test can
        /// state the world in one line, including the awkward worlds: nothing
        /// reachable, no sight line, an unassigned floor.
        /// </summary>
        private sealed class FakeProbe : IWorldProbe
        {
            public bool LineOfSight = true;
            public float PathDistance = 10f;
            public FloorMaterial Floor = FloorMaterial.Concrete;
            public int Zone = 1;
            public bool Lit;
            public Func<Vec3, FloorMaterial> FloorSampler;
            public Func<Vec3, int> ZoneSampler;

            public FakeProbe()
            {
                FloorSampler = p => Floor;
                ZoneSampler = p => Zone;
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
                return !float.IsInfinity(PathDistance) && !float.IsNaN(PathDistance);
            }

            public FloorMaterial SampleFloor(Vec3 position)
            {
                return FloorSampler(position);
            }

            public int ZoneIdAt(Vec3 position)
            {
                return ZoneSampler(position);
            }

            public Vec3 SnapToNavigable(Vec3 desired)
            {
                return desired;
            }

            public bool IsAreaLit(Vec3 position)
            {
                return Lit;
            }
        }

        private const int Seed = 20260730;

        private static int StepsFor(float seconds)
        {
            return (int)((seconds / GameConstants.FixedStep) + 0.5f);
        }

        private static MonsterObservation Walking(Vec3 position)
        {
            return MonsterObservation.Moving(position, Vec3.Forward * GameConstants.MonsterBaseSpeed, 0);
        }

        // ====================================================================
        // 청음사 — §04 / §06 / §12.
        // ====================================================================

        /// <summary>
        /// §06 builds 정지 specifically to take this ability away: it is the one state
        /// whose 소리 column is 없음, and the section calls the silence the game's
        /// weapon. A silent monster must therefore produce no fix at all — not a
        /// stale one, not a vague one.
        /// </summary>
        [Test]
        public void Listener_HearsNothing_WhileTheMonsterIsInStandstill()
        {
            var probe = new FakeProbe();
            probe.Floor = FloorMaterial.Tile;
            var listener = new ListenerAbility(probe, new DeterministicRandom(Seed));
            var at = Vec3.Zero;
            var monsterAt = new Vec3(0f, 0f, GameConstants.ListenerHearingRange * 0.25f);

            listener.Tick(GameConstants.FixedStep, at, 0f, Walking(monsterAt));
            Assert.That(listener.HasReading, Is.True,
                "Sanity: an audible monster well inside hearing range has to be locatable, or the rest of this test proves nothing.");

            for (var i = 0; i < StepsFor(GameConstants.StandstillSeconds); i++)
            {
                listener.Tick(GameConstants.FixedStep, at, 0f,
                    MonsterObservation.Standstill(monsterAt, Vec3.Forward, 0));
            }

            Assert.That(listener.HasReading, Is.False,
                "§06: 정지 makes no sound, so the Listener must lose the monster entirely. Keeping a decayed fix "
                + "would defuse the state the design calls 이 게임의 무기.");
            Assert.That(listener.Failure, Is.EqualTo(AbilityFailure.MonsterSilent));

            listener.Tick(GameConstants.FixedStep, at, 0f, Walking(monsterAt));
            Assert.That(listener.HasReading, Is.True,
                "The moment it moves again the fix returns — the silence costs information, not the role.");
        }

        /// <summary>
        /// §04's constraint on the role: "자기가 소리를 내면 못 듣는다. 뛰거나 문을 열면
        /// 정보가 끊긴다." §10 prices the ability the same way — 소리를 듣는다 in
        /// exchange for 자기가 조용해야 한다.
        /// </summary>
        [Test]
        public void Listener_GoesBlind_WhileItIsTheOneMakingNoise()
        {
            var probe = new FakeProbe();
            var listener = new ListenerAbility(probe, new DeterministicRandom(Seed));
            var at = Vec3.Zero;
            var monsterAt = new Vec3(0f, 0f, GameConstants.ListenerHearingRange * 0.5f);

            listener.Tick(GameConstants.FixedStep, at, GameConstants.ListenerSelfNoiseThreshold, Walking(monsterAt));
            Assert.That(listener.HasReading, Is.True,
                "At exactly the threshold the feed survives — the constraint is about running and doors, not about breathing.");

            listener.Tick(GameConstants.FixedStep, at, GameConstants.ListenerSelfNoiseThreshold * 2f, Walking(monsterAt));
            Assert.That(listener.HasReading, Is.False, "§04: running cuts the feed.");
            Assert.That(listener.Failure, Is.EqualTo(AbilityFailure.SelfNoise));
            Assert.That(listener.IsSelfBlinded, Is.True,
                "The HUD's fix for this is 'stop moving', not 'get closer', so it needs its own flag.");

            listener.Tick(GameConstants.FixedStep, at, 0f, Walking(monsterAt));
            Assert.That(listener.HasReading, Is.True,
                "§04 cuts the feed while the noise lasts. Standing still has to bring it straight back, or the role "
                + "becomes unusable rather than constrained.");
        }

        /// <summary>
        /// §12 makes the floor plan the Listener's instrument: "구역별로 바닥 재질이
        /// 달라야 청음사가 위치를 판별할 수 있다. 아트 결정이 아니라 시스템 결정이다."
        /// If two materials read the same, that sentence is false and a zone's floor
        /// stops being information.
        /// </summary>
        [Test]
        public void Listener_Precision_IsDecidedByTheFloorUnderTheMonster()
        {
            var distance = GameConstants.ListenerHearingRange * 0.5f;

            var metal = ListenerAbility.ErrorRadiusFor(FloorMaterial.Metal, distance);
            var tile = ListenerAbility.ErrorRadiusFor(FloorMaterial.Tile, distance);
            var wood = ListenerAbility.ErrorRadiusFor(FloorMaterial.Wood, distance);
            var gravel = ListenerAbility.ErrorRadiusFor(FloorMaterial.Gravel, distance);
            var concrete = ListenerAbility.ErrorRadiusFor(FloorMaterial.Concrete, distance);
            var unassigned = ListenerAbility.ErrorRadiusFor(FloorMaterial.None, distance);

            Assert.That(metal, Is.LessThan(tile), "§12: 금속 울림 gives the monster away more than 타일.");
            Assert.That(tile, Is.LessThan(wood), "§12: 딱딱·반향 carries better than a 삐걱.");
            Assert.That(wood, Is.LessThan(gravel), "§12: a creak pins a spot better than 부스럭.");
            Assert.That(gravel, Is.LessThan(concrete), "§12: 콘크리트 둔탁 is the quietest floor on the map.");
            Assert.That(concrete, Is.LessThan(unassigned),
                "An unassigned floor must read worse than every real one, so a map that skipped §12's material pass "
                + "degrades visibly instead of silently.");

            Assert.That(ListenerAbility.ErrorRadiusFor(FloorMaterial.Concrete, distance),
                Is.GreaterThan(ListenerAbility.ErrorRadiusFor(FloorMaterial.Concrete, distance * 0.1f)),
                "Distance still matters within one material — the two effects multiply rather than replace each other.");
        }

        /// <summary>
        /// The same walk, twice, differing only in what the monster is standing on.
        /// The fix on 금속 is exact; the fix on 콘크리트 is not — and it is that gap,
        /// not the range, that makes §12's material map worth building.
        /// </summary>
        [Test]
        public void Listener_SameWalkOnTwoFloors_ProducesDifferentFixes()
        {
            var at = Vec3.Zero;
            var monsterAt = new Vec3(0f, 0f, GameConstants.ListenerHearingRange * 0.5f);

            var stairwell = new FakeProbe();
            stairwell.Floor = FloorMaterial.Metal;
            var onMetal = new ListenerAbility(stairwell, new DeterministicRandom(Seed));
            onMetal.Tick(GameConstants.FixedStep, at, 0f, Walking(monsterAt));

            var basement = new FakeProbe();
            basement.Floor = FloorMaterial.Concrete;
            var onConcrete = new ListenerAbility(basement, new DeterministicRandom(Seed));
            onConcrete.Tick(GameConstants.FixedStep, at, 0f, Walking(monsterAt));

            Assert.That(onMetal.Reading.Precision01, Is.GreaterThan(onConcrete.Reading.Precision01),
                "§12: the floor is the instrument. Equal precision on 금속 and 콘크리트 would make the material "
                + "layout decorative.");
            Assert.That(onMetal.Reading.ErrorRadius, Is.EqualTo(0f).Within(MathX.Epsilon),
                "§12 calls 금속 울림 the clearest surface, so a stairwell transit is worth calling out precisely.");
            Assert.That(Vec3.DistanceFlat(onMetal.Reading.EstimatedPosition, monsterAt), Is.EqualTo(0f).Within(MathX.Epsilon));

            Assert.That(onConcrete.Reading.ErrorRadius, Is.GreaterThan(0f));
            Assert.That(Vec3.DistanceFlat(onConcrete.Reading.EstimatedPosition, monsterAt),
                Is.LessThanOrEqualTo(onConcrete.Reading.ErrorRadius + MathX.Epsilon),
                "The reported error radius has to actually bound the error, or the HUD circle lies.");
        }

        /// <summary>
        /// §12 asks for clear material boundaries. The consequence, once the fix has
        /// error in it, is that a monster near a boundary gets called in the wrong
        /// room — which is the same family of mistake as §03's "6이었나 9였나" and is
        /// why the material is reported from the estimate rather than from the truth.
        /// </summary>
        [Test]
        public void Listener_ABadFix_NamesTheNeighbouringRoom()
        {
            var monsterAt = new Vec3(0f, 0f, GameConstants.ListenerHearingRange * 0.75f);
            var probe = new FakeProbe();
            probe.FloorSampler = p =>
                Vec3.DistanceFlat(p, monsterAt) < MathX.Epsilon ? FloorMaterial.Concrete : FloorMaterial.Wood;
            probe.ZoneSampler = p => Vec3.DistanceFlat(p, monsterAt) < MathX.Epsilon ? 1 : 2;

            var listener = new ListenerAbility(probe, new DeterministicRandom(Seed));
            var namedTheWrongRoom = false;

            for (var i = 0; i < StepsFor(GameConstants.ListenerFixIntervalSeconds * 10f); i++)
            {
                listener.Tick(GameConstants.FixedStep, Vec3.Zero, 0f, Walking(monsterAt));
                if (listener.HasReading && listener.Reading.Floor == FloorMaterial.Wood && listener.Reading.ZoneId == 2)
                {
                    namedTheWrongRoom = true;
                }
            }

            Assert.That(namedTheWrongRoom, Is.True,
                "On a dull floor the Listener's estimate can land across a material boundary, and it reports what it "
                + "thinks it heard. Reporting the true material instead would hand the team a perfect zone read from "
                + "an imperfect position, which is not the ability §04 describes.");
        }

        /// <summary>Sound is not light. Hearing through geometry is the point of the role, so no sight line is consulted.</summary>
        [Test]
        public void Listener_HearsThroughWalls()
        {
            var probe = new FakeProbe();
            probe.LineOfSight = false;
            var listener = new ListenerAbility(probe, new DeterministicRandom(Seed));

            listener.Tick(GameConstants.FixedStep, Vec3.Zero, 0f,
                Walking(new Vec3(0f, 0f, GameConstants.ListenerHearingRange * 0.5f)));

            Assert.That(listener.HasReading, Is.True,
                "§04 gives the Listener sound, and §12's zones are separated by walls. A sight-line requirement would "
                + "reduce the role to a worse flashlight.");
        }

        /// <summary>Outside hearing range there is nothing, and the boundary itself still works.</summary>
        [Test]
        public void Listener_OutOfHearingRange_ReportsWhy()
        {
            var probe = new FakeProbe();
            var listener = new ListenerAbility(probe, new DeterministicRandom(Seed));

            listener.Tick(GameConstants.FixedStep, Vec3.Zero, 0f,
                Walking(new Vec3(0f, 0f, GameConstants.ListenerHearingRange)));
            Assert.That(listener.HasReading, Is.True, "Exactly at the range limit the monster is still audible.");

            listener.Tick(GameConstants.FixedStep, Vec3.Zero, 0f,
                Walking(new Vec3(0f, 0f, GameConstants.ListenerHearingRange + 1f)));
            Assert.That(listener.HasReading, Is.False);
            Assert.That(listener.Failure, Is.EqualTo(AbilityFailure.OutOfRange));
        }

        /// <summary>
        /// §04 promises 이동 방향, which only exists for something that is moving. A
        /// monster creeping below the audible-movement floor has a position and no
        /// bearing, and the reading has to say so rather than invent one.
        /// </summary>
        [Test]
        public void Listener_CallsNoBearing_ForAMonsterThatIsBarelyMoving()
        {
            var probe = new FakeProbe();
            probe.Floor = FloorMaterial.Metal;
            var listener = new ListenerAbility(probe, new DeterministicRandom(Seed));
            var monsterAt = new Vec3(0f, 0f, GameConstants.ListenerHearingRange * 0.25f);

            listener.Tick(GameConstants.FixedStep, Vec3.Zero, 0f, new MonsterObservation(
                monsterAt,
                Vec3.Forward * (GameConstants.ListenerMinDirectionSpeed * 0.5f),
                Vec3.Forward,
                false,
                0));
            Assert.That(listener.HasReading, Is.True);
            Assert.That(listener.Reading.HasMovementDirection, Is.False);
            Assert.That(listener.Reading.MovementDirection, Is.EqualTo(Vec3.Zero));

            listener.Reset();
            listener.Tick(GameConstants.FixedStep, Vec3.Zero, 0f, Walking(monsterAt));
            Assert.That(listener.Reading.HasMovementDirection, Is.True);
            Assert.That(MathX.AngleBetween(listener.Reading.MovementDirection, Vec3.Forward),
                Is.LessThanOrEqualTo(GameConstants.ListenerDirectionErrorMaxDegrees),
                "On 금속 the bearing should be near-exact, and it can never be wronger than the error ceiling.");
        }

        /// <summary>
        /// Zero delta holds the last fix; a frame spike costs at most one extra fix.
        /// Neither may fabricate information the Listener did not hear.
        /// </summary>
        [Test]
        public void Listener_ZeroAndHugeDelta_BehaveThemselves()
        {
            var probe = new FakeProbe();
            var listener = new ListenerAbility(probe, new DeterministicRandom(Seed));
            var monsterAt = new Vec3(0f, 0f, GameConstants.ListenerHearingRange * 0.5f);

            listener.Tick(GameConstants.FixedStep, Vec3.Zero, 0f, Walking(monsterAt));
            var first = listener.Reading.EstimatedPosition;

            listener.Tick(0f, Vec3.Zero, 0f, Walking(monsterAt));
            Assert.That(listener.HasReading, Is.True);
            Assert.That(listener.Reading.EstimatedPosition, Is.EqualTo(first),
                "A zero-length step is not a new footstep, so the fix must not be re-rolled.");

            listener.Tick(GameConstants.ListenerFixIntervalSeconds * 1000f, Vec3.Zero, 0f, Walking(monsterAt));
            Assert.That(listener.HasReading, Is.True);
            Assert.That(listener.SecondsToNextFix, Is.EqualTo(GameConstants.ListenerFixIntervalSeconds).Within(MathX.Epsilon),
                "A spike must take one fix and reset the interval, not bank a thousand of them.");

            listener.Tick(0f, Vec3.Zero, 0f, MonsterObservation.Standstill(monsterAt, Vec3.Forward, 0));
            Assert.That(listener.HasReading, Is.False, "Even a zero-length step must notice the silence.");
        }

        // ====================================================================
        // 관측자 — §04 / §05.
        // ====================================================================

        private static void HoldStill(ObserverAbility observer, float seconds, Vec3 at, MonsterObservation monster, bool turnHead)
        {
            var yaw = 0f;
            var steps = StepsFor(seconds);
            for (var i = 0; i < steps; i++)
            {
                if (turnHead)
                {
                    // Whip the view around while the feet stay planted. §05 chose this
                    // deliberately: "발이 묶인 채 둘러보는 게 더 무섭다."
                    yaw = MathX.NormalizeAngle(yaw + 37f);
                }

                observer.Tick(GameConstants.FixedStep, at, 0f, yaw, monster);
            }
        }

        /// <summary>
        /// §05's ruling on the Observer, tested from both sides: mouselook must not
        /// break the read ("화면이 얼면 조작감 최악"), and a single step must.
        /// </summary>
        [Test]
        public void Observer_SurvivesMouselook_ButNotOneStep()
        {
            var observer = new ObserverAbility();
            var at = Vec3.Zero;
            var monster = new MonsterObservation(
                new Vec3(0f, 0f, GameConstants.ObserverRange * 0.8f), Vec3.Zero, Vec3.Forward, false, 2);

            HoldStill(observer, GameConstants.ObserverStillSeconds * 1.1f, at, monster, true);

            Assert.That(observer.IsReading, Is.True,
                "§05: the 3 seconds pin the feet, not the head. Turning the camera cannot cancel the read.");
            Assert.That(observer.RevealedTargetPlayerIndex, Is.EqualTo(2),
                "§04: the ability's whole output is 누가 표적인지.");
            Assert.That(observer.Failure, Is.EqualTo(AbilityFailure.None));

            HoldStill(observer, GameConstants.ObserverStillSeconds, at, monster, true);
            Assert.That(observer.IsReading, Is.True, "More looking around does not wear the read out.");

            observer.Tick(GameConstants.FixedStep, at, GameConstants.WalkSpeed, 0f, monster);
            Assert.That(observer.IsReading, Is.False, "§04: 움직이면 끊긴다.");
            Assert.That(observer.Failure, Is.EqualTo(AbilityFailure.Moving));
            Assert.That(observer.RevealedTargetPlayerIndex, Is.EqualTo(MonsterObservation.NoTarget));
        }

        /// <summary>
        /// §04 says the window restarts, not resumes. Two stretches of 2.8 s add to
        /// 5.6 s and must still produce nothing — otherwise the Observer could shuffle
        /// around a room and collect its three seconds in instalments, and §12's
        /// 관측 지점 would stop being worth walking to.
        /// </summary>
        [Test]
        public void Observer_ThreeSecondWindow_RestartsFromZero()
        {
            var observer = new ObserverAbility();
            var at = Vec3.Zero;
            var monster = new MonsterObservation(
                new Vec3(0f, 0f, GameConstants.ObserverRange * 0.5f), Vec3.Zero, Vec3.Forward, false, 1);

            var almost = GameConstants.ObserverStillSeconds * 0.93f;

            HoldStill(observer, almost, at, monster, false);
            Assert.That(observer.IsReading, Is.False);
            Assert.That(observer.Failure, Is.EqualTo(AbilityFailure.NotStillLongEnough));
            Assert.That(observer.Progress01, Is.GreaterThan(0.5f));

            observer.Tick(GameConstants.FixedStep, at, GameConstants.WalkSpeed, 0f, monster);
            Assert.That(observer.Progress01, Is.EqualTo(0f),
                "One step costs the whole window. A pause-and-resume timer would be a different, much weaker ability.");

            HoldStill(observer, almost, at, monster, false);
            Assert.That(observer.IsReading, Is.False,
                "2.8 s + 2.8 s is not 3 s of stillness. §04's 정지 3초 is one unbroken stretch.");

            HoldStill(observer, GameConstants.ObserverStillSeconds - almost + GameConstants.FixedStep, at, monster, false);
            Assert.That(observer.IsReading, Is.True, "Completing the stretch does produce the read.");
        }

        /// <summary>
        /// §04's 지속 clause names movement as the only thing that breaks the read, so
        /// stillness accrues whether or not the monster is inside 15 m. An Observer who
        /// has been holding its breath gets the answer the instant the monster steps
        /// into range — which is what an observation post is for.
        /// </summary>
        [Test]
        public void Observer_KeepsItsStillness_WhileTheMonsterIsFarAway()
        {
            var observer = new ObserverAbility();
            var at = Vec3.Zero;
            var far = new MonsterObservation(
                new Vec3(0f, 0f, GameConstants.ObserverRange * 2f), Vec3.Zero, Vec3.Forward, false, 3);

            HoldStill(observer, GameConstants.ObserverStillSeconds * 1.5f, at, far, false);
            Assert.That(observer.IsReading, Is.False);
            Assert.That(observer.Failure, Is.EqualTo(AbilityFailure.OutOfRange));
            Assert.That(observer.StillSeconds, Is.EqualTo(GameConstants.ObserverStillSeconds).Within(MathX.Epsilon));

            var near = new MonsterObservation(
                new Vec3(0f, 0f, GameConstants.ObserverRange * 0.9f), Vec3.Zero, Vec3.Forward, false, 3);
            observer.Tick(GameConstants.FixedStep, at, 0f, 0f, near);

            Assert.That(observer.IsReading, Is.True,
                "§04 gates the reveal on distance and gates the window on movement. Resetting the window on range too "
                + "would be a third rule the section does not contain.");
            Assert.That(observer.RevealedTargetPlayerIndex, Is.EqualTo(3));
        }

        /// <summary>
        /// §04 gates activation on distance alone — no sight line — so this ability
        /// needs no world seam at all. §12's 관측 지점 (2층 난간 · 창문 · 격자) therefore
        /// exist to make the read survivable rather than possible: "없으면 관측자는
        /// 죽으러 가야 한다."
        /// </summary>
        [Test]
        public void Observer_ReadsOnDistanceAlone_WithNoSightLineRequired()
        {
            var observer = new ObserverAbility();
            var throughAWall = new MonsterObservation(
                new Vec3(GameConstants.ObserverRange * 0.99f, 0f, 0f), Vec3.Zero, Vec3.Right, false, 0);

            HoldStill(observer, GameConstants.ObserverStillSeconds * 1.1f, Vec3.Zero, throughAWall, false);

            Assert.That(observer.IsReading, Is.True,
                "Adding a line-of-sight requirement here would be a design change, not a bug fix: §04's 발동 clause "
                + "is 15m 이내 + 이동 정지 3초 and nothing else.");
            Assert.That(observer.DistanceToMonster,
                Is.LessThanOrEqualTo(GameConstants.ObserverRange));
        }

        /// <summary>
        /// Height must not read as distance — §12 puts observation posts on second-floor
        /// railings precisely so the Observer can be above the monster.
        /// </summary>
        [Test]
        public void Observer_MeasuresDistanceHorizontally()
        {
            var observer = new ObserverAbility();
            var below = new MonsterObservation(
                new Vec3(0f, -GameConstants.ObserverRange, GameConstants.ObserverRange * 0.5f),
                Vec3.Zero, Vec3.Forward, false, 1);

            HoldStill(observer, GameConstants.ObserverStillSeconds * 1.1f, Vec3.Zero, below, false);

            Assert.That(observer.IsReading, Is.True,
                "§12's 관측 지점 are 높이 차 — a railing overlooking the hall. Counting the drop as range would delete "
                + "the safest place to use the ability.");
        }

        /// <summary>
        /// A contradiction §04 does not acknowledge, pinned so a retune cannot bury it.
        /// <para>
        /// §04 requires 15 m and 3 s of stillness. §06 gives the monster 4.8 m/s, so it
        /// covers 14.4 m in those 3 s — which is also §12's <c>SingleCornerMinDistance</c>,
        /// derived from the same product. The Observer's activation range and the
        /// monster's three-second reach are the same distance to within 0.6 m.
        /// </para>
        /// <para>
        /// Against an approaching monster the ability therefore returns its answer with
        /// 0.6 m — an eighth of a second — to spare, and §07 makes it worse: at 새벽 the
        /// monster moves at 5.0 m/s (5.0 × 3 = 15.0 m, exactly the range) and at
        /// 동트기 전 at 5.2 m/s (15.6 m, beyond it). From the 새벽 tier on, an Observer
        /// facing a monster that walks toward it cannot finish a read at all.
        /// </para>
        /// <para>
        /// That may be intended — §11 calls this the role that needs protecting, and §12
        /// demands observation posts — but the document never states that the ability is
        /// unusable head-on, and §11's "아이템으로 대체 불가" means there is no fallback.
        /// See docs/BALANCE-FINDINGS.md.
        /// </para>
        /// </summary>
        [Test]
        public void Observer_AgainstAnApproachingMonster_AnswersWithNoTimeLeft()
        {
            var reach = GameConstants.MonsterBaseSpeed * GameConstants.ObserverStillSeconds;
            Assert.That(reach, Is.EqualTo(GameConstants.SingleCornerMinDistance).Within(0.01f),
                "The monster's 3-second reach is the same 14.4 m §12 derives for a single corner — same product, "
                + "different section.");
            Assert.That(GameConstants.ObserverRange - reach, Is.EqualTo(0.6f).Within(0.01f));

            var observer = new ObserverAbility();
            var at = Vec3.Zero;
            var monsterZ = GameConstants.ObserverRange;
            var distanceAtReveal = float.NaN;

            while (monsterZ > 0f)
            {
                var monster = MonsterObservation.Moving(
                    new Vec3(0f, 0f, monsterZ), new Vec3(0f, 0f, -GameConstants.MonsterBaseSpeed), 1);
                observer.Tick(GameConstants.FixedStep, at, 0f, 0f, monster);

                if (observer.IsReading && float.IsNaN(distanceAtReveal))
                {
                    distanceAtReveal = monsterZ;
                }

                monsterZ -= GameConstants.MonsterBaseSpeed * GameConstants.FixedStep;
            }

            Assert.That(float.IsNaN(distanceAtReveal), Is.False,
                "At base speed the read does complete — but only just.");
            Assert.That(distanceAtReveal, Is.LessThan(1f),
                "§04's 15 m and 3 s versus §06's 4.8 m/s: the answer arrives with the monster on top of the Observer. "
                + "At §07's 새벽 speed (5.0 m/s) the same walk-in never completes at all. If the design retunes either "
                + "number, docs/BALANCE-FINDINGS.md must be updated with it.");
        }

        // ====================================================================
        // 주자 — §04 / §06 / §08.
        // ====================================================================

        /// <summary>
        /// §06: "주자도 스태미나가 끝나면 잡힌다." A full bar buys 12 s of 5.6 m/s and
        /// then the Runner is an ordinary player at 4.5 — below the monster's 4.8.
        /// </summary>
        [Test]
        public void Runner_Sprint_EndsWithTheStaminaBar()
        {
            var runner = new RunnerAbility(new FakeProbe());
            var sprinted = 0f;

            for (var i = 0; i < StepsFor(GameConstants.SprintStaminaSeconds * 1.25f); i++)
            {
                runner.Tick(GameConstants.FixedStep, true, false, true);
                sprinted += runner.LastTickSprintSeconds;
            }

            Assert.That(sprinted, Is.EqualTo(GameConstants.SprintStaminaSeconds).Within(0.05f),
                "§06 fixes the budget at 12 s of sprinting, however long Shift is held.");
            Assert.That(runner.IsSprinting, Is.False);
            Assert.That(runner.Failure, Is.EqualTo(AbilityFailure.OutOfStamina));
            Assert.That(runner.BaseSpeed, Is.EqualTo(GameConstants.RunSpeed).Within(MathX.Epsilon),
                "§05 gives Shift to everyone as 달리기, so a spent Runner keeps running — it just cannot outrun "
                + "the monster any more.");
            Assert.That(runner.BaseSpeed, Is.LessThan(GameConstants.MonsterBaseSpeed));

            Assert.That(sprinted * (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed),
                Is.LessThan(GameConstants.AggroReleaseDistance),
                "§06: one bar must not be enough to open the release distance, or §12's S-corridors are decoration.");
        }

        /// <summary>
        /// A frame spike must not turn a 12-second bar into a 30-second sprint. The
        /// ability reports how much of the step was actually sprinted so the host can
        /// integrate the spike without launching the Runner through §12's cover spacing.
        /// </summary>
        [Test]
        public void Runner_FrameSpike_CannotSprintLongerThanItsBar()
        {
            var runner = new RunnerAbility(new FakeProbe());
            var spike = GameConstants.SprintStaminaSeconds * 2.5f;

            runner.Tick(spike, true, false, true);

            Assert.That(runner.LastTickSprintSeconds, Is.EqualTo(GameConstants.SprintStaminaSeconds).Within(1e-3f),
                "A 30 s step may only contain 12 s of sprinting.");
            Assert.That(runner.IsSprinting, Is.False);

            var recoveryRate = GameConstants.SprintStaminaSeconds / GameConstants.SprintStaminaRecoverySeconds;
            Assert.That(runner.StaminaSeconds,
                Is.EqualTo((spike - GameConstants.SprintStaminaSeconds) * recoveryRate).Within(1e-2f),
                "The rest of the spike was spent running, so it recovers — one 30 s frame must not cost the same as "
                + "one 12 s frame.");
        }

        /// <summary>
        /// §03: "주자가 들면 질주 불가", and §08 removes the sprint entirely at weight 16.
        /// Both arrive as bare bools, which is the seam ARCHITECTURE §3 asks for — no
        /// inventory or movement type is imported to learn either fact.
        /// </summary>
        [Test]
        public void Runner_LosesTheSprint_ToTheObjectiveAndToItsLoad()
        {
            var runner = new RunnerAbility(new FakeProbe());

            runner.Tick(GameConstants.FixedStep, true, true, true);
            Assert.That(runner.IsSprinting, Is.False);
            Assert.That(runner.Failure, Is.EqualTo(AbilityFailure.CarryingObjective));
            Assert.That(runner.BaseSpeed, Is.LessThan(GameConstants.MonsterBaseSpeed),
                "§03's last decision — 누가 들 것인가 — only bites because the carrier cannot escape.");

            runner.Reset();
            runner.Tick(GameConstants.FixedStep, true, false, false);
            Assert.That(runner.IsSprinting, Is.False);
            Assert.That(runner.Failure, Is.EqualTo(AbilityFailure.LoadTooHeavy));

            runner.Reset();
            runner.Tick(GameConstants.FixedStep, false, false, true);
            Assert.That(runner.BaseSpeed, Is.EqualTo(GameConstants.WalkSpeed).Within(MathX.Epsilon));
            Assert.That(runner.Failure, Is.EqualTo(AbilityFailure.None),
                "Not holding Shift is not a failure — the HUD has nothing to explain.");
        }

        /// <summary>
        /// §04 gives the Runner 어그로 강제 획득, and §12 gives it a shape: aggro is taken
        /// from across an 개방 공간, because "주자는 멀리서 어그로를 걸어야 한다." A taunt
        /// the monster cannot walk to pulls nothing — a Runner sealed behind the
        /// Engineer's own door is the obvious case.
        /// </summary>
        [Test]
        public void Runner_Taunt_NeedsRangeAndAWalkableRoute()
        {
            var probe = new FakeProbe();
            var runner = new RunnerAbility(probe);
            var at = Vec3.Zero;

            var tooFar = MonsterObservation.Moving(
                new Vec3(0f, 0f, GameConstants.RunnerTauntRange + 1f), Vec3.Forward, 0);
            Assert.That(runner.TryTaunt(at, tooFar, out var why), Is.False);
            Assert.That(why, Is.EqualTo(AbilityFailure.OutOfRange));
            Assert.That(runner.AggroForced, Is.False);

            var inRange = MonsterObservation.Moving(
                new Vec3(0f, 0f, GameConstants.RunnerTauntRange * 0.8f), Vec3.Forward, 0);

            probe.PathDistance = float.PositiveInfinity;
            Assert.That(runner.TryTaunt(at, inRange, out why), Is.False,
                "An IWorldProbe reporting nothing reachable is a legitimate map state, not an error.");
            Assert.That(why, Is.EqualTo(AbilityFailure.MonsterUnreachable));
            Assert.That(runner.AggroForced, Is.False);

            probe.PathDistance = GameConstants.RunnerTauntRange;
            Assert.That(runner.TryTaunt(at, inRange, out why), Is.True);
            Assert.That(why, Is.EqualTo(AbilityFailure.None));
            Assert.That(runner.AggroForced, Is.True);
            Assert.That(runner.TauntCount, Is.EqualTo(1));
            Assert.That(runner.LastTauntPosition, Is.EqualTo(at),
                "§06 sends the monster to where it last saw the Runner, so this is the position that decides whether "
                + "breaking aggro delivers the monster to the team.");

            runner.NotifyAggroReleased();
            Assert.That(runner.AggroForced, Is.False,
                "§06 gives the release to the monster — 12 m plus 3 s of broken sight line — not to the Runner.");
        }

        /// <summary>
        /// A contradiction between §06's stamina numbers and its own conclusion, pinned
        /// so a retune has to face it.
        /// <para>
        /// §06 offers stamina as the answer to endless flight — "주자도 스태미나가 끝나면
        /// 잡힌다 · 무한 도주 방지". But 12 s of sprint against 20 s of refill is a
        /// sustainable duty cycle of 12 / (12 + 20) = 0.375, and at 0.375 the Runner
        /// averages 0.375 × 0.8 = 0.3 m/s more than the monster. The 12 m release
        /// distance is therefore reachable by attrition in 40 s of continuous chase.
        /// </para>
        /// <para>
        /// So stamina does not cap escape; §06's other clause — 3 s of *broken sight
        /// line* — is the only thing that does. That is exactly the section's stated
        /// intent ("어그로 해제는 거리가 아니라 맵을 쓰는 것이다"), but it means the
        /// stamina bar is pacing rather than a limit, and the two sentences read as if
        /// stamina were the limit. See docs/BALANCE-FINDINGS.md.
        /// </para>
        /// </summary>
        [Test]
        public void Runner_StaminaIsPacing_NotACapOnTotalDistance()
        {
            var sustainableDuty = GameConstants.SprintStaminaSeconds
                                  / (GameConstants.SprintStaminaSeconds + GameConstants.SprintStaminaRecoverySeconds);
            Assert.That(sustainableDuty, Is.EqualTo(0.375f).Within(0.001f));

            var runner = new RunnerAbility(new FakeProbe());
            var window = GameConstants.SprintStaminaSeconds * 25f;
            var sprinted = 0f;

            for (var i = 0; i < StepsFor(window); i++)
            {
                runner.Tick(GameConstants.FixedStep, true, false, true);
                sprinted += runner.LastTickSprintSeconds;
            }

            var measured = sprinted / window;
            Assert.That(measured, Is.EqualTo(sustainableDuty).Within(0.05f),
                "Holding Shift forever yields the rate-limited duty cycle, in usable bursts rather than a 50 Hz "
                + "flicker. If this measures near 1.0 the re-engage floor was removed and the sprint became endless.");
            Assert.That(measured, Is.GreaterThan(0.2f),
                "It must also not lock out entirely — the Runner has to get real sprints, not scraps.");

            var averageGain = sustainableDuty * (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed);
            Assert.That(averageGain, Is.GreaterThan(0f));
            Assert.That(GameConstants.AggroReleaseDistance / averageGain, Is.LessThan(GameConstants.TargetMatchSecondsMin),
                "40 s of straight-line chase opens the 12 m release distance on stamina alone. §06's own conclusion — "
                + "that release means using the map — survives only because it also requires 3 s of broken sight line.");
        }

        // ====================================================================
        // 정비공 — §04 / §07 / §12.
        // ====================================================================

        private static Vec3 SiteAt
        {
            get { return new Vec3(4f, 0f, 4f); }
        }

        private static Vec3 OutOfReach
        {
            get { return SiteAt + (Vec3.Right * (GameConstants.EngineerReachDistance * 3f)); }
        }

        /// <summary>
        /// §04's constraint on the role: "즉석 사용 불가 — 사전 준비형." Nothing may happen
        /// on the tick the job is asked for, and a zero-length step is not progress.
        /// </summary>
        [Test]
        public void Engineer_CannotActInstantly()
        {
            foreach (var action in new[]
                     {
                         EngineerAction.LockDoor, EngineerAction.ZoneLight, EngineerAction.NoiseTrap,
                         EngineerAction.Barricade, EngineerAction.OpenSafe
                     })
            {
                Assert.That(EngineerActions.SetupSeconds(action), Is.GreaterThan(0f),
                    "§04 gives every Engineer action a setup time. A zero here would make it instant and delete the "
                    + "role's only constraint.");
            }

            var engineer = new EngineerAbility(new FakeProbe(), 4);
            var request = EngineerRequest.LockDoor(7, SiteAt, 0, false);

            Assert.That(engineer.TryBegin(request, SiteAt, out var why), Is.True);
            Assert.That(why, Is.EqualTo(AbilityFailure.None));
            Assert.That(engineer.IsDoorLocked(7), Is.False, "Beginning is not doing.");
            Assert.That(engineer.HasPendingOutcome, Is.False);

            engineer.Tick(0f, SiteAt);
            Assert.That(engineer.IsDoorLocked(7), Is.False, "A zero-length step cannot finish a 3.5 s job.");

            engineer.Tick(GameConstants.EngineerDoorLockSeconds * 0.99f, SiteAt);
            Assert.That(engineer.IsDoorLocked(7), Is.False);
            Assert.That(engineer.Progress01, Is.GreaterThan(0.9f));
            Assert.That(engineer.IsBusy, Is.True);

            engineer.Tick(GameConstants.EngineerDoorLockSeconds * 0.02f, SiteAt);
            Assert.That(engineer.IsDoorLocked(7), Is.True);
            Assert.That(engineer.Materials, Is.EqualTo(4 - GameConstants.EngineerDoorLockMaterialCost));
        }

        /// <summary>§04: "시간과 자재." Without the 자재 there is no job at all.</summary>
        [Test]
        public void Engineer_CannotActWithoutMaterials()
        {
            var empty = new EngineerAbility(new FakeProbe(), 0);
            Assert.That(empty.TryBegin(EngineerRequest.LockDoor(1, SiteAt, 0, false), SiteAt, out var why), Is.False);
            Assert.That(why, Is.EqualTo(AbilityFailure.NoMaterials));

            empty.Tick(GameConstants.EngineerDoorLockSeconds * 10f, SiteAt);
            Assert.That(empty.IsDoorLocked(1), Is.False, "A refused job must not quietly run in the background.");

            // A barricade costs more than a door, so one unit is not enough for it.
            var oneUnit = new EngineerAbility(new FakeProbe(), GameConstants.EngineerBarricadeMaterialCost - 1);
            Assert.That(oneUnit.TryBegin(EngineerRequest.Barricade(2, SiteAt, 0, false), SiteAt, out why), Is.False);
            Assert.That(why, Is.EqualTo(AbilityFailure.NoMaterials));

            oneUnit.AddMaterials(GameConstants.EngineerBarricadeMaterialCost);
            Assert.That(oneUnit.TryBegin(EngineerRequest.Barricade(2, SiteAt, 0, false), SiteAt, out why), Is.True,
                "§08 restocks 정비 자재 at the 지상 차량 — that is one of §03's reasons to walk back out.");
        }

        /// <summary>
        /// §04's design note, as a test: "실수가 아군을 죽인다 (문 안에 아군 · 조명 끔 ·
        /// 함정에 주자) … 버그로 취급해 없애지 말 것." Locking a teammate in must be a
        /// reachable outcome that the rules report, not a state the rules prevent.
        /// </summary>
        [Test]
        public void Engineer_CanLockATeammateIn()
        {
            var engineer = new EngineerAbility(new FakeProbe(), 4);
            var sealingTwoPeopleIn = EngineerRequest.LockDoor(11, SiteAt, 2, true);

            Assert.That(engineer.TryBegin(sealingTwoPeopleIn, SiteAt, out var why), Is.True,
                "§04 forbids treating this as a mistake to be blocked. The ability must not refuse.");
            Assert.That(why, Is.EqualTo(AbilityFailure.None));

            engineer.Tick(GameConstants.EngineerDoorLockSeconds, SiteAt);

            Assert.That(engineer.IsDoorLocked(11), Is.True);
            Assert.That(engineer.TryTakeOutcome(out var outcome), Is.True);
            Assert.That(outcome.TeammatesSealedIn, Is.EqualTo(2));
            Assert.That(outcome.TeamRouteBlocked, Is.True, "§10: 경로를 차단한다 / 아군도 막힌다.");
            Assert.That(outcome.HurtTheTeam, Is.True,
                "The accident is recorded rather than prevented — §04 calls it the role's identity and 방송 콘텐츠의 핵심.");
            Assert.That(engineer.Failure, Is.EqualTo(AbilityFailure.None),
                "Nothing failed. The team is simply now living with a locked door.");

            Assert.That(engineer.TryTakeOutcome(out _), Is.False,
                "One completed job is one outcome — a host that reads twice must not lock the door twice.");
        }

        /// <summary>
        /// §04's third listed accident: "함정에 주자." A trap is armed for whatever walks
        /// into it, and the Runner — the one role that sprints down corridors without
        /// looking — is the likeliest victim.
        /// </summary>
        [Test]
        public void Engineer_CanTrapTheRunner()
        {
            var engineer = new EngineerAbility(new FakeProbe(), 4);
            Assert.That(engineer.TryBegin(EngineerRequest.NoiseTrap(5, SiteAt), SiteAt, out _), Is.True);
            engineer.Tick(GameConstants.EngineerTrapSeconds, SiteAt);
            Assert.That(engineer.IsTrapArmed(5), Is.True);
            engineer.TryTakeOutcome(out _);

            const int runnerIndex = 2;
            Assert.That(engineer.TryTriggerTrap(5, runnerIndex, false, out var trigger), Is.True,
                "No check on who stepped on it. §04: 버그로 취급해 없애지 말 것.");
            Assert.That(trigger.CaughtTeammate, Is.True);
            Assert.That(trigger.TrippedByPlayerIndex, Is.EqualTo(runnerIndex));
            Assert.That(trigger.NoiseLevel, Is.GreaterThan(GameConstants.ListenerSelfNoiseThreshold),
                "The trap has to be loud enough to matter: §06 moves the monster on a sound, and §04 blinds the "
                + "Listener with one.");

            Assert.That(engineer.IsTrapArmed(5), Is.False, "A tripped trap is spent.");
            Assert.That(engineer.TryTriggerTrap(5, runnerIndex, false, out _), Is.False);
        }

        /// <summary>
        /// §04's second listed accident: "조명 끔." §03 needs 2.5 s of light on a clue to
        /// read it, so cutting a zone mid-read is a real way to cost the team a round
        /// trip — and it must stay possible.
        /// </summary>
        [Test]
        public void Engineer_CanCutTheLightsOnAReadingTeam()
        {
            var engineer = new EngineerAbility(new FakeProbe(), 4);
            const int zone = 3;

            Assert.That(engineer.TryBegin(EngineerRequest.ZoneLight(20, SiteAt, zone, true), SiteAt, out _), Is.True);
            engineer.Tick(GameConstants.EngineerZoneLightSeconds, SiteAt);
            Assert.That(engineer.IsZoneLit(zone), Is.True, "§03: a lit zone is how several people read one clue at once.");
            engineer.TryTakeOutcome(out var lightsUp);
            Assert.That(lightsUp.MaterialsSpent, Is.EqualTo(GameConstants.EngineerZoneLightMaterialCost));

            var materialsBefore = engineer.Materials;
            Assert.That(engineer.TryBegin(EngineerRequest.ZoneLight(20, SiteAt, zone, false), SiteAt, out _), Is.True);
            engineer.Tick(GameConstants.EngineerZoneLightSeconds, SiteAt);

            Assert.That(engineer.IsZoneLit(zone), Is.False);
            Assert.That(engineer.TryTakeOutcome(out var blackout), Is.True);
            Assert.That(blackout.ZoneWentDark, Is.True);
            Assert.That(blackout.HurtTheTeam, Is.True);
            Assert.That(engineer.Materials, Is.EqualTo(materialsBefore),
                "Throwing a breaker back off costs no 자재 — which is exactly why this accident is so cheap to have.");

            Assert.That(engineer.TryBegin(EngineerRequest.ZoneLight(20, SiteAt, zone, false), SiteAt, out var why), Is.False);
            Assert.That(why, Is.EqualTo(AbilityFailure.NothingToActOn),
                "A dark zone cannot be darkened again — the HUD should say so rather than run a pointless 2 s job.");
        }

        /// <summary>
        /// §04 makes the work hands-on, so walking away abandons it. The time is gone
        /// and the materials were never taken — §07's "시간이 유일한 통화다" means the
        /// lost seconds are already the full price.
        /// </summary>
        [Test]
        public void Engineer_WalkingAway_CostsTheTimeAndNotTheMaterials()
        {
            var engineer = new EngineerAbility(new FakeProbe(), 4);
            Assert.That(engineer.TryBegin(EngineerRequest.Barricade(8, SiteAt, 0, false), SiteAt, out _), Is.True);

            engineer.Tick(GameConstants.EngineerBarricadeSeconds * 0.75f, SiteAt);
            Assert.That(engineer.IsBusy, Is.True);

            engineer.Tick(GameConstants.FixedStep, OutOfReach);

            Assert.That(engineer.IsBusy, Is.False);
            Assert.That(engineer.Failure, Is.EqualTo(AbilityFailure.Moving));
            Assert.That(engineer.IsBarricaded(8), Is.False);
            Assert.That(engineer.HasPendingOutcome, Is.False);
            Assert.That(engineer.Materials, Is.EqualTo(4));

            Assert.That(engineer.TryBegin(EngineerRequest.Barricade(8, SiteAt, 0, false), SiteAt, out _), Is.True);
            engineer.Tick(GameConstants.FixedStep, SiteAt);
            Assert.That(engineer.Progress01, Is.LessThan(0.1f),
                "Restarting starts over. Banking abandoned progress would smuggle instant use back in.");
        }

        /// <summary>
        /// A frame spike may finish the job it is working on and nothing else. Leftover
        /// time must not roll into the next one, or a single long frame would let the
        /// Engineer install two things at once.
        /// </summary>
        [Test]
        public void Engineer_FrameSpike_FinishesOneJobAndBanksNothing()
        {
            var engineer = new EngineerAbility(new FakeProbe(), 8);

            Assert.That(engineer.TryBegin(EngineerRequest.OpenSafe(4, SiteAt), SiteAt, out _), Is.True);
            engineer.Tick(GameConstants.EngineerSafeSeconds * 100f, SiteAt);

            Assert.That(engineer.IsSafeOpen(4), Is.True);
            Assert.That(engineer.TryTakeOutcome(out var outcome), Is.True);
            Assert.That(outcome.Action, Is.EqualTo(EngineerAction.OpenSafe));
            Assert.That(engineer.IsBusy, Is.False);

            Assert.That(engineer.TryBegin(EngineerRequest.LockDoor(4, SiteAt, 0, false), SiteAt, out _), Is.True);
            engineer.Tick(GameConstants.FixedStep, SiteAt);
            Assert.That(engineer.IsDoorLocked(4), Is.False,
                "The spike's surplus 792 s must not pay for the next job.");
        }

        /// <summary>Reach, duplicates and one-job-at-a-time, each with the reason the HUD will show.</summary>
        [Test]
        public void Engineer_ReportsWhyItCannotStart()
        {
            var engineer = new EngineerAbility(new FakeProbe(), 8);

            Assert.That(engineer.TryBegin(EngineerRequest.LockDoor(1, SiteAt, 0, false), OutOfReach, out var why), Is.False);
            Assert.That(why, Is.EqualTo(AbilityFailure.OutOfRange));

            Assert.That(engineer.TryBegin(EngineerRequest.LockDoor(1, SiteAt, 0, false), SiteAt, out _), Is.True);
            Assert.That(engineer.TryBegin(EngineerRequest.NoiseTrap(2, SiteAt), SiteAt, out why), Is.False);
            Assert.That(why, Is.EqualTo(AbilityFailure.Busy), "§04: one pair of hands.");

            engineer.Tick(GameConstants.EngineerDoorLockSeconds, SiteAt);
            engineer.TryTakeOutcome(out _);

            Assert.That(engineer.TryBegin(EngineerRequest.LockDoor(1, SiteAt, 0, false), SiteAt, out why), Is.False);
            Assert.That(why, Is.EqualTo(AbilityFailure.NothingToActOn), "That door is already locked.");

            Assert.That(engineer.TryBegin(
                new EngineerRequest(EngineerAction.None, 1, SiteAt, -1, false, 0, false), SiteAt, out why), Is.False);
            Assert.That(why, Is.EqualTo(AbilityFailure.NothingToActOn),
                "An empty action must be refused rather than treated as a zero-second job.");
        }

        /// <summary>
        /// A consequence of §04's setup times against §12's map rules that neither
        /// section states, pinned here.
        /// <para>
        /// Multiply each setup time by the monster's 4.8 m/s and compare it with the
        /// distances §12 permits: a 3.5 s door lock needs 16.8 m of warning, which is
        /// more than §12's minimum 15 m gap between cover; a 6 s barricade needs 28.8 m,
        /// more than the 20 m longest straight corridor and more than the 25 m widest
        /// cover gap; an 8 s safe needs 38.4 m, nearly the full diagonal of the largest
        /// legal zone.
        /// </para>
        /// <para>
        /// So in a map built exactly to §12, the barricade and the safe can never be
        /// completed while the monster is visible anywhere in the same corridor, and the
        /// door lock cannot be completed at the distance §12 guarantees you can see. The
        /// Engineer is a between-encounters role, full stop — which matches "사전 준비형"
        /// but is stronger than §04 says, and it is why §11's fallback for a missing
        /// Engineer is a 조명탄 rather than another way to lock a door.
        /// </para>
        /// </summary>
        [Test]
        public void Engineer_SetupTimes_ExceedEveryWarningTheMapCanGive()
        {
            var doorReach = GameConstants.EngineerDoorLockSeconds * GameConstants.MonsterBaseSpeed;
            var barricadeReach = GameConstants.EngineerBarricadeSeconds * GameConstants.MonsterBaseSpeed;
            var safeReach = GameConstants.EngineerSafeSeconds * GameConstants.MonsterBaseSpeed;

            Assert.That(doorReach, Is.GreaterThan(GameConstants.LineOfSightBreakSpacingMin),
                "3.5 s of lock is 16.8 m of monster. §12 only promises 15 m between cover, so seeing the monster "
                + "coming is not enough time to lock the door in front of it.");
            Assert.That(barricadeReach, Is.GreaterThan(GameConstants.MaxStraightCorridor),
                "6 s of barricade is 28.8 m, and §12 caps a straight corridor at 20 m. A barricade can never be "
                + "finished with the monster in view down a legal corridor.");
            Assert.That(barricadeReach, Is.GreaterThan(GameConstants.LineOfSightBreakSpacingMax));
            Assert.That(safeReach, Is.GreaterThan(GameConstants.ZoneDiagonalMin),
                "8 s of safe-cracking is 38.4 m — more than the smallest legal zone is wide. §08's 금고 속 문서 is "
                + "loot you take only when the monster is in another zone entirely.");
            Assert.That(safeReach, Is.LessThan(GameConstants.ZoneDiagonalMax),
                "In the largest legal zone it just fits, which is the only reason the safe is openable under pressure "
                + "at all. If §12's zone size shrinks, docs/BALANCE-FINDINGS.md needs updating.");
        }

        // ====================================================================
        // 섬광수 — §04 / §16-3.
        // ====================================================================

        private static MonsterObservation MonsterAtAngle(float degrees, float distance)
        {
            return MonsterObservation.Moving(
                MathX.DirectionFromYaw(degrees) * distance, Vec3.Forward * GameConstants.MonsterBaseSpeed, 0);
        }

        /// <summary>
        /// §04: the flash is weak but reusable, and the cooldown is the whole of the
        /// price. Firing inside it must fail outright rather than fire a weaker flash.
        /// </summary>
        [Test]
        public void Flasher_CannotBeSpammedInsideItsCooldown()
        {
            var flasher = new FlasherAbility(new FakeProbe());
            var monster = MonsterAtAngle(0f, GameConstants.FlashRange * 0.5f);

            Assert.That(flasher.TryFlash(Vec3.Zero, Vec3.Forward, monster, out var first), Is.True);
            Assert.That(first.Stunned, Is.True);
            Assert.That(flasher.IsReady, Is.False);
            Assert.That(flasher.Charge01, Is.EqualTo(0f).Within(MathX.Epsilon));

            Assert.That(flasher.TryFlash(Vec3.Zero, Vec3.Forward, monster, out var second), Is.False);
            Assert.That(second.Fired, Is.False);
            Assert.That(second.Stunned, Is.False);
            Assert.That(second.Failure, Is.EqualTo(AbilityFailure.OnCooldown));
            Assert.That(flasher.Failure, Is.EqualTo(AbilityFailure.OnCooldown));

            flasher.Tick(GameConstants.FlashCooldownSeconds * 0.99f);
            Assert.That(flasher.IsReady, Is.False);
            Assert.That(flasher.TryFlash(Vec3.Zero, Vec3.Forward, monster, out _), Is.False);

            flasher.Tick(GameConstants.FlashCooldownSeconds);
            Assert.That(flasher.IsReady, Is.True);
            Assert.That(flasher.CooldownRemaining, Is.EqualTo(0f),
                "A frame spike must clamp at ready, not bank negative cooldown into the next use.");
            Assert.That(flasher.TryFlash(Vec3.Zero, Vec3.Forward, monster, out var third), Is.True);
            Assert.That(third.Stunned, Is.True);
        }

        /// <summary>
        /// §04 makes this an aimed cone, and §12 explains why the geometry matters:
        /// "넓은 곳에서는 괴물이 우회한다", so the Flasher's stage is the 미로 공간.
        /// </summary>
        [Test]
        public void Flasher_RespectsRangeAndCone()
        {
            var justInside = new FlasherAbility(new FakeProbe());
            Assert.That(justInside.TryFlash(Vec3.Zero, Vec3.Forward,
                MonsterAtAngle(GameConstants.FlashConeHalfAngle * 0.9f, GameConstants.FlashRange * 0.9f),
                out var hit), Is.True);
            Assert.That(hit.Stunned, Is.True);
            Assert.That(hit.StunSeconds, Is.EqualTo(GameConstants.FlashStunSeconds).Within(MathX.Epsilon));

            var wideAngle = new FlasherAbility(new FakeProbe());
            Assert.That(wideAngle.TryFlash(Vec3.Zero, Vec3.Forward,
                MonsterAtAngle(GameConstants.FlashConeHalfAngle * 1.1f, GameConstants.FlashRange * 0.9f),
                out var missedAngle), Is.True);
            Assert.That(missedAngle.Fired, Is.True);
            Assert.That(missedAngle.Stunned, Is.False);
            Assert.That(missedAngle.Failure, Is.EqualTo(AbilityFailure.OutsideCone));

            var tooFar = new FlasherAbility(new FakeProbe());
            Assert.That(tooFar.TryFlash(Vec3.Zero, Vec3.Forward,
                MonsterAtAngle(0f, GameConstants.FlashRange * 1.5f), out var missedRange), Is.True);
            Assert.That(missedRange.Stunned, Is.False);
            Assert.That(missedRange.Failure, Is.EqualTo(AbilityFailure.OutOfRange));
            Assert.That(missedRange.Distance, Is.GreaterThan(GameConstants.FlashRange));

            Assert.That(tooFar.IsReady, Is.False,
                "A miss still burns the cooldown. §10 prices every benefit; a free retry until the angle is right "
                + "would make aiming meaningless.");
            Assert.That(tooFar.LastFlashFailure, Is.EqualTo(AbilityFailure.OutOfRange));

            var noAim = new FlasherAbility(new FakeProbe());
            Assert.That(noAim.TryFlash(Vec3.Zero, Vec3.Zero,
                MonsterAtAngle(0f, GameConstants.FlashRange * 0.5f), out var degenerate), Is.True);
            Assert.That(degenerate.Stunned, Is.False,
                "A zero-length aim vector is a miss, not a hit on everything in range.");
        }

        /// <summary>Light, not sound — §04's flash cannot reach around §12's corners.</summary>
        [Test]
        public void Flasher_DoesNotReachThroughGeometry()
        {
            var probe = new FakeProbe();
            probe.LineOfSight = false;
            var flasher = new FlasherAbility(probe);

            Assert.That(flasher.TryFlash(Vec3.Zero, Vec3.Forward,
                MonsterAtAngle(0f, GameConstants.FlashRange * 0.5f), out var result), Is.True);
            Assert.That(result.Stunned, Is.False);
            Assert.That(result.Failure, Is.EqualTo(AbilityFailure.LineOfSightBlocked));
            Assert.That(flasher.TryConsumeStun(out _), Is.False);
        }

        /// <summary>
        /// The stun is handed to the monster system exactly once, so ticking and reading
        /// at different rates cannot double-apply it.
        /// </summary>
        [Test]
        public void Flasher_HandsTheStunOverOnce()
        {
            var flasher = new FlasherAbility(new FakeProbe());
            flasher.TryFlash(Vec3.Zero, Vec3.Forward, MonsterAtAngle(0f, GameConstants.FlashRange * 0.5f), out _);

            Assert.That(flasher.PendingStunSeconds, Is.EqualTo(GameConstants.FlashStunSeconds).Within(MathX.Epsilon));
            Assert.That(flasher.TryConsumeStun(out var seconds), Is.True);
            Assert.That(seconds, Is.EqualTo(GameConstants.FlashStunSeconds).Within(MathX.Epsilon));
            Assert.That(flasher.TryConsumeStun(out _), Is.False);
            Assert.That(flasher.StunCount, Is.EqualTo(1));
        }

        /// <summary>
        /// §01: "저지는 전부 일시적 · 이길 수 없는 적 → 공포가 유지된다." The guard that
        /// keeps that true is arithmetic: the stun must stay shorter than the 3 s of
        /// broken sight line §06 requires to release aggro, or a flash would buy a free
        /// aggro reset and the Flasher would quietly become the strongest role in §04.
        /// §16-3 lists both numbers as unresolved, so this relationship needs pinning.
        /// </summary>
        [Test]
        public void Flasher_StunIsShorterThanAnAggroRelease()
        {
            Assert.That(GameConstants.FlashStunSeconds, Is.LessThan(GameConstants.AggroReleaseLineOfSightBreak),
                "§06 needs 3 s of broken sight line to release aggro. A stun that long would make the flash an "
                + "escape button rather than a moment of relief.");
            Assert.That(GameConstants.FlashRange, Is.LessThan(GameConstants.AggroReleaseDistance),
                "§04's flash is used from inside the distance at which aggro could break — the Flasher has to be in "
                + "danger to be useful.");
            Assert.That(GameConstants.FlashCooldownSeconds, Is.GreaterThan(GameConstants.FlashStunSeconds * 2f),
                "Reusable, not spammable: the recharge has to dominate the effect or §04's 효과가 약하다 stops being true.");
        }
    }
}
