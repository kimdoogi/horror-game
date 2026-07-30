using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Monster;
using HorrorGame.Core.Session;
using HorrorGame.Core.Telemetry;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §06 — the monster's state machine, aggro release and perception.
    /// <para>
    /// These tests assert the design's reasoning rather than the implementation:
    /// that the release cannot be bought with distance alone or with cover alone,
    /// that broken cover starts over, that a released monster walks to where it last
    /// saw someone (which is what turns a Runner's escape route into a delivery
    /// route), that a standstill is silent, and that a frame spike can neither skip a
    /// state nor cut a corner.
    /// </para>
    /// <para>
    /// Two of them pin contradictions the document does not acknowledge — see
    /// docs/BALANCE-FINDINGS.md F-002 and F-003. They are named
    /// <c>..._AsSection06LiterallyWrites</c> so that nobody "fixes" them by accident.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MonsterTests
    {
        private const int Seed = 20260730;
        private const int TargetId = 7;

        // A probe offset, not a balance value: "just inside" and "just short of" the
        // §06 thresholds, small enough that no other rule can be responsible.
        private const float JustInside = 0.1f;

        // The distance a fleeing target holds in these tests: §12's 14.4 m, the range
        // a single corner starts working from. It sits above the 12 m release distance
        // and well inside MonsterSightRange, so neither clause fires by accident.
        private static float HoldDistance => GameConstants.SingleCornerMinDistance;

        // ====================================================================
        // The §06 table, transition by transition.
        // ====================================================================

        /// <summary>순찰 → 소리 감지 → 경계, and 경계 walks to the noise.</summary>
        [Test]
        public void Patrol_OnSound_EntersAlert()
        {
            var world = new OpenRoom();
            var brain = NewBrain(world);
            var noise = new Vec3(0f, 0f, HoldDistance);

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol));

            brain.Tick(0f, Input().WithSounds(Cues(noise)));

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Alert));
            Assert.That(brain.Destination.HasValue, Is.True);
            Assert.That(Vec3.DistanceFlat(brain.Destination!.Value, noise), Is.LessThan(MathX.Epsilon),
                "§06: 경계 is '소리 방향으로 이동' — the noise itself is the destination.");
        }

        /// <summary>경계 → 시야 확보 → 추격, remembering who and where.</summary>
        [Test]
        public void Alert_OnSight_EntersChase()
        {
            var world = new OpenRoom();
            var brain = AlertedBrain(world, out var seenAt);

            brain.Tick(0f, Input().WithTargets(Targets(seenAt)));

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Chase));
            Assert.That(brain.ChaseTargetId, Is.EqualTo(TargetId));
            Assert.That(brain.IsRoaring, Is.True, "§06: 추격 is the only state with 포효.");
            Assert.That(brain.LastSeenPosition.HasValue, Is.True);
            Assert.That(Vec3.DistanceFlat(brain.LastSeenPosition!.Value, seenAt), Is.LessThan(MathX.Epsilon));
        }

        /// <summary>경계 → 3초 무소득 → 순찰, and not a moment before.</summary>
        [Test]
        public void Alert_WithNothingFound_ReturnsToPatrolAfterThreeSeconds()
        {
            var world = new OpenRoom();
            var brain = AlertedBrain(world, out _);

            Advance(brain, GameConstants.AlertGiveUpSeconds - JustInside, Input());
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Alert),
                "§06 gives 경계 a full 3 s; giving up early would make a noise-based lure useless.");

            Advance(brain, JustInside * 2f, Input());
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol));
        }

        /// <summary>추격 → 해제 조건 충족 → 수색.</summary>
        [Test]
        public void Chase_WhenReleaseConditionsAreMet_EntersSearch()
        {
            var world = new OpenRoom();
            var brain = ChasingBrain(world, out _);
            world.LineOfSight = false;

            AdvanceHolding(brain, GameConstants.AggroReleaseLineOfSightBreak + JustInside, HoldDistance);

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Search));
            Assert.That(brain.ChaseTargetId, Is.EqualTo(MonsterBrain.NoTarget));
            Assert.That(brain.IsRoaring, Is.False);
        }

        /// <summary>수색 → 15초 무소득 → 순찰.</summary>
        [Test]
        public void Search_GivesUpAfterFifteenSeconds()
        {
            var world = new OpenRoom();
            var brain = SearchingBrain(world, out _);

            Advance(brain, GameConstants.SearchGiveUpSeconds - JustInside, Input());
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Search));

            Advance(brain, JustInside * 2f, Input());
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol));
        }

        /// <summary>정지 → 5초 후 → 순찰.</summary>
        [Test]
        public void Standstill_ReturnsToPatrolAfterFiveSeconds()
        {
            var world = new OpenRoom();
            var brain = StandingStillBrain(world);

            Advance(brain, GameConstants.StandstillSeconds - JustInside, Input());
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Standstill));

            Advance(brain, JustInside * 2f, Input());
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol));
        }

        /// <summary>정지 → 소리 감지 → 경계. The monster is listening, which is the point of it.</summary>
        [Test]
        public void Standstill_OnSound_EntersAlert()
        {
            var world = new OpenRoom();
            var brain = StandingStillBrain(world);

            brain.Tick(0f, Input().WithSounds(Cues(new Vec3(0f, 0f, HoldDistance))));

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Alert),
                "§06: 정지 is '멈춰서 듣는다' — the silence is a trap, not deafness.");
        }

        /// <summary>순찰 → 정지, scheduled inside §06's 15~30 s window.</summary>
        [Test]
        public void Patrol_EntersStandstillInsideTheFifteenToThirtySecondWindow()
        {
            var world = new OpenRoom();
            var brain = NewBrain(world);

            Assert.That(brain.SecondsUntilStandstill,
                Is.InRange(GameConstants.StandstillIntervalMin, GameConstants.StandstillIntervalMax));

            var waited = AdvanceUntil(brain, Input(), MonsterStateId.Standstill,
                GameConstants.StandstillIntervalMax + 1f);

            Assert.That(waited, Is.InRange(GameConstants.StandstillIntervalMin - GameConstants.FixedStep,
                GameConstants.StandstillIntervalMax + GameConstants.FixedStep),
                "§06: 순찰 중 랜덤하게(15~30초 간격) 정지를 넣는다.");
        }

        /// <summary>
        /// §07's 새벽 rows set 정지 to 없음. At zero chance the monster never stops, so
        /// the Listener never gets the break that late-match tension depends on losing.
        /// </summary>
        [Test]
        public void Patrol_WithNoStandstillChance_NeverStopsMoving()
        {
            var world = new OpenRoom();
            var brain = NewBrain(world);
            var lateNight = new MonsterTickInput(GameConstants.MonsterBaseSpeed, GameConstants.ZoneCountMax, 0f);

            // Four times the longest standstill interval: if a standstill were going
            // to slip through, it would have done so several times over.
            var ticks = SubSteps(GameConstants.StandstillIntervalMax * 4f);
            for (var i = 0; i < ticks; i++)
            {
                brain.Tick(GameConstants.FixedStep, lateNight);
                Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol));
                Assert.That(brain.IsAudible, Is.True,
                    "§07: at 새벽 the monster never goes quiet, so the Listener never loses it.");
            }
        }

        // ====================================================================
        // ...and no other transition. Two of these are F-002.
        // ====================================================================

        /// <summary>
        /// F-002. §06's table gives 순찰 exactly one transition — 소리 감지 → 경계 — so a
        /// patrolling monster staring straight at a player does nothing. §04 says the
        /// Runner "괴물의 어그로 강제 획득", which under the literal table has to be earned
        /// with noise first (Patrol → Alert → Chase), and a silent player is invisible
        /// to a patrol however well lit they are.
        /// <para>
        /// Implemented as written. If the table gains a sight edge for 순찰, this test
        /// fails on purpose and docs/BALANCE-FINDINGS.md F-002 must be updated with it.
        /// </para>
        /// </summary>
        [Test]
        public void Patrol_DoesNotChaseOnSight_AsSection06LiterallyWrites()
        {
            var world = new OpenRoom();
            var brain = NewBrain(world);

            // The player is kept five metres dead ahead of the monster's own facing,
            // in the open, with a clear sight line, for the length of a full alert.
            var ticks = SubSteps(GameConstants.AlertGiveUpSeconds);
            for (var i = 0; i < ticks; i++)
            {
                brain.Tick(GameConstants.FixedStep, Input(0f).WithTargets(Targets(UnderItsNose(brain))));
                Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol));
            }

            Assert.That(brain.ChaseTargetId, Is.EqualTo(MonsterBrain.NoTarget));
        }

        /// <summary>
        /// F-002, the sharper half. §06 gives 수색 one exit, 15초 무소득 → 순찰, and no way
        /// back into 추격 — so a searching monster walks past a player in plain sight
        /// for the full fifteen seconds. "무소득" implies the opposite, which is exactly
        /// why this is a finding and not a bug fix.
        /// </summary>
        [Test]
        public void Search_DoesNotReacquireOnSight_AsSection06LiterallyWrites()
        {
            var world = new OpenRoom();
            var brain = SearchingBrain(world, out _);

            var ticks = SubSteps(GameConstants.SearchGiveUpSeconds - JustInside);
            for (var i = 0; i < ticks; i++)
            {
                brain.Tick(GameConstants.FixedStep, Input().WithTargets(Targets(UnderItsNose(brain))));
                Assert.That(brain.State, Is.EqualTo(MonsterStateId.Search));
            }

            Assert.That(brain.ChaseTargetId, Is.EqualTo(MonsterBrain.NoTarget));
        }

        /// <summary>
        /// F-002 again. §06's 정지 row lists 소리 감지 and the 5 s timeout only, so
        /// walking silently into a standing monster's face is safe — while the design
        /// note under that same table promises "방심하고 나오는 순간 걸린다".
        /// </summary>
        [Test]
        public void Standstill_DoesNotChaseOnSight_AsSection06LiterallyWrites()
        {
            var world = new OpenRoom();
            var brain = StandingStillBrain(world);

            // A standstill does not move or turn, so this really is under its nose for
            // the whole five seconds.
            var visible = Targets(UnderItsNose(brain));
            Advance(brain, GameConstants.StandstillSeconds - JustInside, Input().WithTargets(visible));

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Standstill));
            Assert.That(brain.ChaseTargetId, Is.EqualTo(MonsterBrain.NoTarget));
        }

        /// <summary>
        /// §06 gives 추격 exactly one exit, and it is 수색. A chase never lapses back to
        /// 순찰 and never drops to 경계 on a noise — so noise is not a way to shake a
        /// monster off a friend, which is what makes §04's Flasher and Engineer the
        /// only answers.
        /// </summary>
        [Test]
        public void Chase_HasNoExitOtherThanSearch()
        {
            var world = new OpenRoom();
            var brain = ChasingBrain(world, out _);
            world.LineOfSight = false;

            // Cover the whole time, a distance just short of the release, and a noise
            // every tick: none of it may move the monster off Chase.
            var noise = Cues(new Vec3(GameConstants.MapExtent * 0.5f, 0f, 0f));
            var ticks = SubSteps(GameConstants.SearchGiveUpSeconds * 4f);
            for (var i = 0; i < ticks; i++)
            {
                brain.Tick(GameConstants.FixedStep, HoldingInput(brain, GameConstants.AggroReleaseDistance - JustInside)
                    .WithSounds(noise));
            }

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Chase));
            Assert.That(brain.LineOfSightBrokenSeconds,
                Is.GreaterThan(GameConstants.AggroReleaseLineOfSightBreak),
                "The cover clause was satisfied for a full minute — only the distance clause held the aggro.");
        }

        /// <summary>
        /// §06 lists 소리 감지 → 경계 for 순찰 and 정지, not for 경계 itself, so a second
        /// noise neither retargets nor extends an alert. A repeating 소음 함정 (§04)
        /// therefore cannot pin the monster in place — worth knowing before the
        /// Engineer's trap is tuned.
        /// </summary>
        [Test]
        public void Alert_IsNotExtendedByFurtherNoise()
        {
            var world = new OpenRoom();
            var brain = AlertedBrain(world, out _);
            var noise = Cues(new Vec3(0f, 0f, HoldDistance));

            // Measured as a duration: the moment 경계 ends the monster is patrolling
            // again and the same noise re-alerts it, so only the length of the first
            // alert can show whether the extra noise bought anything.
            var lasted = 0f;
            var limit = SubSteps(GameConstants.AlertGiveUpSeconds * 2f);
            for (var i = 0; i < limit && brain.State == MonsterStateId.Alert; i++)
            {
                brain.Tick(GameConstants.FixedStep, Input().WithSounds(noise));
                lasted += GameConstants.FixedStep;
            }

            Assert.That(lasted, Is.EqualTo(GameConstants.AlertGiveUpSeconds).Within(GameConstants.FixedStep * 2f),
                "§06 gives 경계 no sound edge, so a repeating 소음 함정 cannot hold the monster past 3 s.");
        }

        // ====================================================================
        // §06 — aggro release. "어그로 해제는 거리가 아니라 맵을 쓰는 것이다."
        // ====================================================================

        /// <summary>
        /// 11.9 m is not 12 m. The distance clause is absolute, so no amount of cover
        /// buys a release from inside it — this is what stops "hide in the nearest
        /// doorway" from replacing §12's 연속 차단.
        /// </summary>
        [Test]
        public void Aggro_DoesNotReleaseJustInsideTwelveMetres()
        {
            var world = new OpenRoom();
            var brain = ChasingBrain(world, out _);
            world.LineOfSight = false;

            AdvanceHolding(brain, GameConstants.AggroReleaseLineOfSightBreak * 3f,
                GameConstants.AggroReleaseDistance - JustInside);

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Chase),
                "§06 requires 거리 12m 이상 AND 시야 차단 3초. At 11.9 m the first clause fails, "
                + "and nine seconds of cover must not substitute for it.");
        }

        /// <summary>
        /// 2.9 s of cover is not 3 s. Distance alone releases nothing either — §06's
        /// own arithmetic (0.8 m/s × 12 s = 9.6 m &lt; 12 m) only works if both clauses
        /// are required.
        /// </summary>
        [Test]
        public void Aggro_DoesNotReleaseJustShortOfThreeSecondsOfCover()
        {
            var world = new OpenRoom();
            var brain = ChasingBrain(world, out _);
            world.LineOfSight = false;

            AdvanceHolding(brain, GameConstants.AggroReleaseLineOfSightBreak - JustInside, HoldDistance);
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Chase),
                "The target is 14.4 m away — beyond the release distance — and still must not be released "
                + "until the cover has held for the full 3 s.");

            AdvanceHolding(brain, JustInside * 2f, HoldDistance);
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Search));
        }

        /// <summary>
        /// A sight line regained at 2.9 s costs the runner all 2.9 s, not the
        /// difference. §12's 연속 차단 requirement is only justified if partial cover is
        /// worthless: two 2.9 s hides must total nothing.
        /// </summary>
        [Test]
        public void Aggro_SightRegained_ResetsTheCoverTimerToZero()
        {
            var world = new OpenRoom();
            var brain = ChasingBrain(world, out _);
            world.LineOfSight = false;

            var almost = GameConstants.AggroReleaseLineOfSightBreak - JustInside;
            AdvanceHolding(brain, almost, HoldDistance);
            Assert.That(brain.LineOfSightBrokenSeconds, Is.EqualTo(almost).Within(GameConstants.FixedStep));

            // One glimpse.
            world.LineOfSight = true;
            AdvanceHolding(brain, GameConstants.FixedStep, HoldDistance);
            Assert.That(brain.LineOfSightBrokenSeconds, Is.EqualTo(0f).Within(MathX.Epsilon),
                "§06: the 3 s must be continuous, so regaining sight spends the cover entirely.");

            // The same 2.9 s again must still not be enough.
            world.LineOfSight = false;
            AdvanceHolding(brain, almost, HoldDistance);
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Chase),
                "Two hides of 2.9 s add up to nothing. If they added up, a single corner would be enough "
                + "and §12's whole S-corridor rule would be decoration.");

            AdvanceHolding(brain, JustInside * 2f, HoldDistance);
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Search),
                "...and 3 s of unbroken cover at 14.4 m does release.");
        }

        /// <summary>
        /// §06's load-bearing consequence: on release the monster goes to the LAST SEEN
        /// position, not to wherever the target is now. This is what makes a Runner
        /// breaking aggro near the team "괴물을 팀에 배달하는 것", and why the direction of
        /// the escape is a strategy rather than a detail.
        /// </summary>
        [Test]
        public void Release_SendsTheMonsterToLastSeen_NotToTheTargetsCurrentPosition()
        {
            var world = new OpenRoom();
            var brain = ChasingBrain(world, out var seenAt);
            world.LineOfSight = false;

            // The runner keeps going — far away, and sideways, so "last seen" and
            // "where they are now" cannot be confused for one another.
            var runnerNow = new Vec3(GameConstants.MapExtent, 0f, 0f);
            var fleeing = Input().WithTargets(Targets(runnerNow));

            var elapsed = AdvanceUntil(brain, fleeing, MonsterStateId.Search,
                GameConstants.AggroReleaseLineOfSightBreak * 2f);

            Assert.That(elapsed,
                Is.EqualTo(GameConstants.AggroReleaseLineOfSightBreak).Within(GameConstants.FixedStep * 2f),
                "The release must land on the 3 s mark, not somewhere after it.");
            Assert.That(brain.LastSeenPosition.HasValue, Is.True);
            Assert.That(Vec3.DistanceFlat(brain.LastSeenPosition!.Value, seenAt), Is.LessThan(MathX.Epsilon),
                "The monster's memory is the position it last had eyes on.");
            Assert.That(brain.Destination.HasValue, Is.True);
            Assert.That(Vec3.DistanceFlat(brain.Destination!.Value, seenAt), Is.LessThan(MathX.Epsilon),
                "§06: 해제 후 괴물 행동 = 마지막 목격 위치로 이동 → 수색.");

            // And the sweep stays there rather than drifting after the runner.
            Advance(brain, GameConstants.SearchGiveUpSeconds * 0.5f, fleeing);
            Assert.That(Vec3.DistanceFlat(brain.Position, seenAt),
                Is.LessThanOrEqualTo(GameConstants.SearchRadius + GameConstants.MonsterWaypointTolerance),
                "§06: 수색 is '마지막 위치 반경을 뒤짐' — it must not wander off the last sighting.");
            Assert.That(Vec3.DistanceFlat(brain.Position, runnerNow), Is.GreaterThan(GameConstants.SearchRadius),
                "If the monster drifted toward the live position, breaking aggro toward the team would be "
                + "free and §06's delivery problem would not exist.");
        }

        // ====================================================================
        // §06 — 정지: "침묵이 가장 무서운 소리다."
        // ====================================================================

        /// <summary>
        /// The Listener reads <see cref="MonsterBrain.IsAudible"/>, and §06's 소리 column
        /// says exactly one state is silent. If any other state went quiet the
        /// Listener's silence would stop meaning "it has stopped", which is the only
        /// reason the standstill frightens anybody.
        /// </summary>
        [Test]
        public void Standstill_IsTheOnlySilentState()
        {
            var world = new OpenRoom();

            Assert.That(NewBrain(world).IsAudible, Is.True, "순찰 — 발소리.");
            Assert.That(AlertedBrain(world, out _).IsAudible, Is.True, "경계 — 발소리.");
            Assert.That(ChasingBrain(world, out _).IsAudible, Is.True, "추격 — 발소리+포효.");
            Assert.That(SearchingBrain(world, out _).IsAudible, Is.True, "수색 — 발소리.");

            var standing = StandingStillBrain(world);
            Assert.That(standing.IsAudible, Is.False,
                "§06: 정지 emits 없음. This is the game's weapon — the Listener loses the monster.");
            Assert.That(standing.IsRoaring, Is.False);
        }

        /// <summary>A standstill does not move, or the silence would be a lie.</summary>
        [Test]
        public void Standstill_DoesNotMove()
        {
            var world = new OpenRoom();
            var brain = StandingStillBrain(world);
            var before = brain.Position;

            Advance(brain, GameConstants.StandstillSeconds - JustInside, Input());

            Assert.That(Vec3.DistanceFlat(brain.Position, before), Is.LessThan(MathX.Epsilon));
            Assert.That(brain.IsAudible, Is.False);
        }

        // ====================================================================
        // §04 — the Flasher's stun suspends and resumes.
        // ====================================================================

        /// <summary>
        /// A flash landing on the sub-step a transition was due must delay it, not
        /// consume it: §04's ability is worth 2.5 s of the monster's time, and the
        /// state it interrupts has to come back exactly as it was.
        /// </summary>
        [Test]
        public void Stun_SuspendsTheStateMidTransitionAndResumesIt()
        {
            var world = new OpenRoom();
            var brain = AlertedBrain(world, out _);

            var almost = GameConstants.AlertGiveUpSeconds - JustInside;
            Advance(brain, almost, Input());
            var frozenAt = brain.Position;
            var elapsedBefore = brain.StateElapsedSeconds;

            brain.Stun(GameConstants.FlashStunSeconds);
            Assert.That(brain.IsStunned, Is.True);

            Advance(brain, GameConstants.FlashStunSeconds, Input());
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Alert),
                "§04: 기절 suspends the state machine — the 3 s give-up must not have run down.");
            Assert.That(brain.StateElapsedSeconds, Is.EqualTo(elapsedBefore).Within(MathX.Epsilon));
            Assert.That(Vec3.DistanceFlat(brain.Position, frozenAt), Is.LessThan(MathX.Epsilon),
                "A stunned monster does not travel.");
            Assert.That(brain.StunSecondsRemaining, Is.LessThanOrEqualTo(GameConstants.FixedStep),
                "The stun is spent — its resolution is quantised to one sub-step.");

            Advance(brain, JustInside * 2f, Input());
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol),
                "...and the suspended transition fires as soon as the stun ends.");
        }

        /// <summary>
        /// The flash must not do the Runner's job. If cover time accrued while the
        /// monster was blind, one 2.5 s stun plus a doorway would break aggro — §15
        /// already threw out time-based release ("기다리는 것은 실력이 아니다").
        /// </summary>
        [Test]
        public void Stun_DoesNotAdvanceTheAggroReleaseTimer()
        {
            var world = new OpenRoom();
            var brain = ChasingBrain(world, out _);
            world.LineOfSight = false;

            brain.Stun(GameConstants.FlashStunSeconds);
            AdvanceHolding(brain, GameConstants.FlashStunSeconds, HoldDistance);

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Chase));
            Assert.That(brain.LineOfSightBrokenSeconds, Is.EqualTo(0f).Within(MathX.Epsilon),
                "§04 buys the Flasher seconds, not aggro.");
        }

        /// <summary>Overlapping flashes extend to the longer time rather than stacking.</summary>
        [Test]
        public void Stun_OverlappingFlashesExtendRatherThanStack()
        {
            var world = new OpenRoom();
            var brain = ChasingBrain(world, out _);

            brain.Stun(GameConstants.FlashStunSeconds);
            Advance(brain, GameConstants.FlashStunSeconds * 0.5f, Input());
            brain.Stun(GameConstants.FlashStunSeconds);

            Assert.That(brain.StunSecondsRemaining,
                Is.EqualTo(GameConstants.FlashStunSeconds).Within(GameConstants.FixedStep),
                "Two Flashers must not be able to chain the monster indefinitely.");

            brain.Stun(GameConstants.FlashStunSeconds * 0.25f);
            Assert.That(brain.StunSecondsRemaining,
                Is.EqualTo(GameConstants.FlashStunSeconds).Within(GameConstants.FixedStep),
                "A shorter flash during a longer one changes nothing.");
        }

        /// <summary>A zero or negative stun is a miss, not a state change. §04's cone can miss.</summary>
        [Test]
        public void Stun_OfZeroSeconds_DoesNothing()
        {
            var world = new OpenRoom();
            var brain = NewBrain(world);

            brain.Stun(0f);
            brain.Stun(-GameConstants.FlashStunSeconds);

            Assert.That(brain.IsStunned, Is.False);
        }

        // ====================================================================
        // Frame spikes — §12's corners and §06's timers must both survive one.
        // ====================================================================

        /// <summary>
        /// A 2 s hitch must not teleport the monster through a corner. The monster
        /// walks §12's S-corridor one path point at a time, so after the spike it is
        /// still on the corridor and its straight-line progress is strictly less than
        /// the distance it walked — which is the entire premise of §12.
        /// </summary>
        [Test]
        public void FrameSpike_WalksAroundTheCornerInsteadOfThroughIt()
        {
            // §12's base unit: two 10 m legs, "통과 시간 = 20 / 4.8 = 4.2초 ≥ 3초".
            var leg = GameConstants.SCorridorLegLength;
            var world = new SCorridor(
                new Vec3(0f, 0f, 0f),
                new Vec3(0f, 0f, leg),
                new Vec3(leg, 0f, leg),
                new Vec3(leg, 0f, leg * 2f));

            var spawn = world.PointAt(leg * 0.5f);
            var brain = new MonsterBrain(world, new DeterministicRandom(Seed), spawn, Vec3.Forward);

            // A noise at the far end of the S, so the monster is walking the corridor.
            brain.Tick(0f, Input().WithSounds(Cues(world.PointAt(world.TotalLength))));
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Alert));

            var startArc = world.ArcOf(brain.Position);
            const float spike = 2f;
            brain.Tick(spike, Input());

            var walked = world.ArcOf(brain.Position) - startArc;
            var straightLine = Vec3.DistanceFlat(spawn, brain.Position);

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Alert),
                "2 s is inside §06's 3 s give-up, so the spike must not have skipped past 경계.");
            Assert.That(walked, Is.EqualTo(GameConstants.MonsterBaseSpeed * spike).Within(GameConstants.MonsterWaypointTolerance),
                "The monster must cover exactly the time it was given — no lost distance either.");
            Assert.That(world.DistanceToCorridor(brain.Position),
                Is.LessThan(GameConstants.MonsterWaypointTolerance),
                "It left the corridor, which means it cut the corner through a wall.");
            Assert.That(straightLine, Is.LessThan(walked - GameConstants.MonsterWaypointTolerance),
                "§12: path length must diverge from the sight line. If these were equal the monster "
                + "moved along the sight line and the Runner's escape would be impossible on every map.");
        }

        /// <summary>
        /// One enormous tick must pass through every state the timers say it should,
        /// in order — a 20 s hitch during a chase under cover ends in 순찰, but only
        /// via 수색, and the aggro it reports lasted 3 s rather than 20.
        /// </summary>
        [Test]
        public void FrameSpike_CannotSkipAState()
        {
            var world = new OpenRoom();
            var telemetry = new RecordingTelemetry();
            var brain = ChasingBrain(world, out _, telemetry);
            world.LineOfSight = false;

            // The runner is a long way off, so the distance clause is satisfied
            // throughout and only the timers decide what happens.
            var fleeing = Input().WithTargets(Targets(new Vec3(GameConstants.MapExtent, 0f, 0f)));
            brain.Tick(GameConstants.AggroReleaseLineOfSightBreak + GameConstants.SearchGiveUpSeconds + 2f, fleeing);

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol));

            var search = telemetry.Counters.IndexOf("monster.state.search");
            var patrol = telemetry.Counters.IndexOf("monster.state.patrol");
            Assert.That(search, Is.GreaterThanOrEqualTo(0),
                "§06 has no edge from 추격 to 순찰. The monster must have passed through 수색.");
            Assert.That(patrol, Is.GreaterThan(search), "...and 수색 must have come first.");

            Assert.That(telemetry.Observations, Is.Not.Empty);
            Assert.That(telemetry.Observations[0].Key, Is.EqualTo("monster.aggro_seconds"));
            Assert.That(telemetry.Observations[0].Value,
                Is.EqualTo(GameConstants.AggroReleaseLineOfSightBreak).Within(GameConstants.FixedStep * 2f),
                "§13 wants to know how long aggro lasts; a spike must not inflate the answer.");
        }

        /// <summary>
        /// A zero-length tick advances no timer but still sees: the state machine's
        /// instantaneous edges have to fire on the frame they become true, and a host
        /// may legitimately tick a paused simulation.
        /// </summary>
        [Test]
        public void ZeroDelta_SeesTheTargetWithoutAdvancingAnyTimer()
        {
            var world = new OpenRoom();
            var brain = AlertedBrain(world, out var seenAt);
            var before = brain.SecondsUntilStandstill;

            brain.Tick(0f, Input().WithTargets(Targets(seenAt)));

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Chase));
            Assert.That(brain.StateElapsedSeconds, Is.EqualTo(0f).Within(MathX.Epsilon));
            Assert.That(brain.SecondsUntilStandstill, Is.EqualTo(before).Within(MathX.Epsilon));
        }

        /// <summary>Negative and NaN deltas are treated as zero rather than rewinding anything.</summary>
        [Test]
        public void NegativeOrNaNDelta_IsTreatedAsZero()
        {
            var world = new OpenRoom();
            var brain = NewBrain(world);
            var before = brain.SecondsUntilStandstill;

            brain.Tick(-1f, Input());
            brain.Tick(float.NaN, Input());

            Assert.That(brain.SecondsUntilStandstill, Is.EqualTo(before).Within(MathX.Epsilon));
            Assert.That(float.IsNaN(brain.Position.X), Is.False);
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol));
        }

        // ====================================================================
        // Ugly cases: no players, no sounds, nothing navigable.
        // ====================================================================

        /// <summary>
        /// An <see cref="IWorldProbe"/> that reports nothing reachable — off-mesh
        /// geometry, a broken NavMesh bake — must leave the monster standing where it
        /// is, still running its state machine, with no NaN anywhere.
        /// </summary>
        [Test]
        public void NothingReachable_LeavesTheMonsterStandingButStillThinking()
        {
            var world = new VoidWorld();
            var spawn = new Vec3(GameConstants.MapExtent * 0.5f, 0f, GameConstants.MapExtent * 0.5f);
            var brain = new MonsterBrain(world, new DeterministicRandom(Seed), spawn, Vec3.Forward);

            Advance(brain, GameConstants.StandstillIntervalMax + GameConstants.StandstillSeconds, Input());

            Assert.That(Vec3.DistanceFlat(brain.Position, spawn), Is.LessThan(MathX.Epsilon),
                "With no path point available the monster must hold position rather than slide toward a target.");
            Assert.That(float.IsNaN(brain.Position.X) || float.IsNaN(brain.Position.Z), Is.False);
            Assert.That(brain.KnownZoneIds, Is.Empty, "ZoneIdAt returned -1 everywhere: there is no zone to remember.");
            Assert.That(brain.State, Is.AnyOf(MonsterStateId.Patrol, MonsterStateId.Standstill),
                "The state machine keeps running even when the world offers nowhere to walk.");
        }

        /// <summary>A noise the monster cannot path to is not heard — a wall is not a shortcut.</summary>
        [Test]
        public void UnreachableOrTooDistantNoise_IsNotHeard()
        {
            var world = new OpenRoom();
            var brain = NewBrain(world);

            // In range as the crow flies, but quieter than the distance it must carry.
            var faint = new[]
            {
                new MonsterSoundCue(new Vec3(0f, 0f, GameConstants.MapExtent), GameConstants.GhostRattleRange),
            };

            Advance(brain, 1f, Input().WithSounds(faint));

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol));
        }

        /// <summary>Four dead players, no noise, null collections: all ordinary states.</summary>
        [Test]
        public void EmptyAndNullSenses_AreOrdinary()
        {
            var world = new OpenRoom();
            var brain = NewBrain(world);

            var empty = new MonsterTickInput(
                GameConstants.MonsterBaseSpeed, 1, 0f,
                Array.Empty<MonsterTarget>(), Array.Empty<MonsterSoundCue>());

            Advance(brain, 1f, empty);
            Advance(brain, 1f, new MonsterTickInput(GameConstants.MonsterBaseSpeed, 1, 0f));

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Patrol));
            Assert.That(brain.ChaseTargetId, Is.EqualTo(MonsterBrain.NoTarget));
        }

        /// <summary>
        /// A target the host stops reporting — dead, and now a ghost (§09) — releases
        /// the monster on the cover clause instead of pinning it in 추격 forever.
        /// </summary>
        [Test]
        public void ChaseTargetThatDisappears_ReleasesOnCoverAlone()
        {
            var world = new OpenRoom();
            var brain = ChasingBrain(world, out _);
            world.LineOfSight = false;

            Advance(brain, GameConstants.AggroReleaseLineOfSightBreak + JustInside,
                Input().WithTargets(Array.Empty<MonsterTarget>()));

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Search),
                "A target that no longer exists is infinitely far away; the alternative is a monster "
                + "stuck chasing a corpse for the rest of the match.");
        }

        /// <summary>A concealed target is not seen however clear the sight line is. §06: 숨거나.</summary>
        [Test]
        public void ConcealedTarget_IsNotSeen()
        {
            var world = new OpenRoom();
            var brain = AlertedBrain(world, out var seenAt);
            var hidden = new[] { new MonsterTarget(TargetId, seenAt, true) };

            Advance(brain, GameConstants.AlertGiveUpSeconds - JustInside, Input().WithTargets(hidden));

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Alert));
            Assert.That(brain.ChaseTargetId, Is.EqualTo(MonsterBrain.NoTarget));
        }

        /// <summary>
        /// Vision has a direction, which is what §04's 관측자 reads. A target behind the
        /// monster is not seen; the same target in front of it is.
        /// </summary>
        [Test]
        public void TargetBehindTheMonster_IsNotSeen()
        {
            var world = new OpenRoom();
            var behind = new Vec3(0f, 0f, -GameConstants.MonsterSightRange * 0.5f);

            var brain = AlertedBrain(world, out _);
            Advance(brain, GameConstants.FixedStep, Input().WithTargets(Targets(behind)));
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Alert));

            var facingBack = new MonsterBrain(world, new DeterministicRandom(Seed), Vec3.Zero, -Vec3.Forward);
            facingBack.Tick(0f, Input().WithSounds(Cues(behind)));
            facingBack.Tick(0f, Input().WithTargets(Targets(behind)));
            Assert.That(facingBack.State, Is.EqualTo(MonsterStateId.Chase));
        }

        /// <summary>A target beyond <see cref="GameConstants.MonsterSightRange"/> is not seen.</summary>
        [Test]
        public void TargetBeyondSightRange_IsNotSeen()
        {
            var world = new OpenRoom();
            var brain = AlertedBrain(world, out _);
            var tooFar = new Vec3(0f, 0f, GameConstants.MonsterSightRange + 1f);

            brain.Tick(0f, Input().WithTargets(Targets(tooFar)));

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Alert));
        }

        /// <summary>
        /// Simultaneous transitions resolve the way §06 lists them. A noise arriving on
        /// the sub-step a standstill expires still sends the monster to 경계, and a
        /// target appearing on the sub-step 경계 runs out is still chased — losing a
        /// player to an evaluation order would be the worst possible way to lose one.
        /// </summary>
        [Test]
        public void SimultaneousTransitions_ResolveInTheOrderSection06ListsThem()
        {
            var world = new OpenRoom();

            // The last tick is two sub-steps long, so the give-up threshold is certain
            // to be crossed inside it and the sense really is competing with the timer.
            var overlap = GameConstants.FixedStep * 2f;

            var standing = StandingStillBrain(world);
            Advance(standing, GameConstants.StandstillSeconds - GameConstants.FixedStep, Input());
            standing.Tick(overlap, Input().WithSounds(Cues(new Vec3(0f, 0f, HoldDistance))));
            Assert.That(standing.State, Is.EqualTo(MonsterStateId.Alert),
                "§06 lists 소리 감지 → 경계 before the 5 s timeout.");

            var alerted = AlertedBrain(world, out var seenAt);
            Advance(alerted, GameConstants.AlertGiveUpSeconds - GameConstants.FixedStep, Input());
            alerted.Tick(overlap, Input().WithTargets(Targets(seenAt)));
            Assert.That(alerted.State, Is.EqualTo(MonsterStateId.Chase),
                "§06 lists 시야 확보 → 추격 before 3초 무소득 → 순찰.");
        }

        // ====================================================================
        // §07 — patrol scope is measured in zones.
        // ====================================================================

        /// <summary>
        /// §07's 초저녁 row limits patrol to 1개 구역. The monster must not wander into a
        /// second zone, or the early game loses the safe zone the team explores in.
        /// </summary>
        [Test]
        public void PatrolScope_OfOneZone_StaysInsideIt()
        {
            var world = new OpenRoom { ZoneSplitX = 0f };
            var brain = new MonsterBrain(world, new DeterministicRandom(Seed),
                new Vec3(-GameConstants.ZoneDiagonalMin, 0f, 0f), Vec3.Forward);

            var earlyEvening = new MonsterTickInput(GameConstants.MonsterBaseSpeed, 1, 0f);
            var ticks = SubSteps(GameConstants.ThreatTierSeconds);
            for (var i = 0; i < ticks; i++)
            {
                brain.Tick(GameConstants.FixedStep, earlyEvening);
                Assert.That(brain.Position.X, Is.LessThan(world.ZoneSplitX),
                    "§07: at 1개 구역 the monster's route must not cross into the next zone.");
            }

            Assert.That(brain.KnownZoneIds, Is.EqualTo(new[] { 0 }));
        }

        /// <summary>
        /// §07 widens the scope as the night goes on (2개 구역, 절반, 전체). At two zones
        /// the monster must actually take the second one, or the threat curve's pressure
        /// never arrives.
        /// </summary>
        [Test]
        public void PatrolScope_OfTwoZones_ExpandsIntoTheSecond()
        {
            var world = new OpenRoom { ZoneSplitX = 0f };
            var brain = new MonsterBrain(world, new DeterministicRandom(Seed),
                new Vec3(-GameConstants.ZoneDiagonalMin, 0f, 0f), Vec3.Forward);

            var night = new MonsterTickInput(GameConstants.MonsterBaseSpeed, 2, 0f);
            Advance(brain, GameConstants.ThreatTierSeconds, night);

            Assert.That(brain.KnownZoneIds, Contains.Item(1),
                "§07: at 2개 구역 the patrol route has to grow to two zones.");
        }

        // ====================================================================
        // The numbers the state machine leans on.
        // ====================================================================

        /// <summary>
        /// §06's release is only expensive because the monster can see further than
        /// the release distance. If sight were shorter, standing 12 m away in the open
        /// would break aggro for free and §12's cover rules would be pointless.
        /// </summary>
        [Test]
        public void MonsterSightRange_ExceedsTheReleaseDistance()
        {
            Assert.That(GameConstants.MonsterSightRange, Is.GreaterThan(GameConstants.AggroReleaseDistance));
            Assert.That(GameConstants.MonsterSightRange,
                Is.InRange(GameConstants.LineOfSightBreakSpacingMin, GameConstants.LineOfSightBreakSpacingMax),
                "§12 guarantees 15~25 m sight lines in open space and the first map sketch labels the hall "
                + "시야 20m. Outside that band the monster either cannot acquire at §12's aggro-start "
                + "distances or acquires straight through the cover the map is required to provide.");
        }

        /// <summary>
        /// The waypoint tolerance must stay under one sub-step of travel, or the monster
        /// stalls short of every path point instead of walking.
        /// </summary>
        [Test]
        public void WaypointTolerance_IsSmallerThanOneSubStepOfTravel()
        {
            var perStep = GameConstants.MonsterBaseSpeed * GameConstants.FixedStep;
            Assert.That(GameConstants.MonsterWaypointTolerance, Is.LessThan(perStep));
        }

        /// <summary>
        /// A standstill costs the monster less time than an alert plus a search, so
        /// §06's silence is a cheap habit rather than a rare event — that is what makes
        /// it a reliable weapon against the Listener.
        /// </summary>
        [Test]
        public void StandstillWindow_IsShorterThanTheSearchItInterrupts()
        {
            Assert.That(GameConstants.StandstillSeconds, Is.LessThan(GameConstants.SearchGiveUpSeconds));
            Assert.That(GameConstants.StandstillIntervalMin, Is.GreaterThan(GameConstants.StandstillSeconds),
                "§06: the gap between standstills must exceed a standstill, or the monster is stopped "
                + "more often than it is moving and the Listener never gets a fix at all.");
        }

        // ====================================================================
        // Fixtures and helpers.
        // ====================================================================

        private static MonsterTickInput Input(float standstillChance = 1f) =>
            new MonsterTickInput(GameConstants.MonsterBaseSpeed, GameConstants.ZoneCountMax, standstillChance);

        private static MonsterTarget[] Targets(Vec3 position) =>
            new[] { new MonsterTarget(TargetId, position) };

        /// <summary>
        /// A point five metres along the monster's own facing — inside
        /// <see cref="GameConstants.MonsterSightRange"/> and dead centre of its cone,
        /// so "it did not see them" can never be the reason a test passes.
        /// </summary>
        private static Vec3 UnderItsNose(MonsterBrain brain) =>
            brain.Position + (brain.Facing * (GameConstants.MonsterSightRange * 0.25f));

        private static MonsterSoundCue[] Cues(Vec3 position) =>
            new[] { new MonsterSoundCue(position, GameConstants.MapExtent) };

        /// <summary>A patrolling monster at the origin, facing +Z.</summary>
        private static MonsterBrain NewBrain(OpenRoom world, ITelemetrySink? telemetry = null) =>
            new MonsterBrain(world, new DeterministicRandom(Seed), Vec3.Zero, Vec3.Forward, telemetry);

        /// <summary>
        /// A monster in 경계 that has not moved: the setup ticks are zero-length, so the
        /// distances the aggro tests assert on are not polluted by 0.096 m of travel.
        /// </summary>
        private static MonsterBrain AlertedBrain(OpenRoom world, out Vec3 seenAt, ITelemetrySink? telemetry = null)
        {
            world.LineOfSight = true;
            seenAt = new Vec3(0f, 0f, HoldDistance);
            var brain = NewBrain(world, telemetry);
            brain.Tick(0f, Input().WithSounds(Cues(seenAt)));
            return brain;
        }

        /// <summary>A monster in 추격 of <see cref="TargetId"/>, seen at 14.4 m dead ahead.</summary>
        private static MonsterBrain ChasingBrain(OpenRoom world, out Vec3 seenAt, ITelemetrySink? telemetry = null)
        {
            var brain = AlertedBrain(world, out seenAt, telemetry);
            brain.Tick(0f, Input().WithTargets(Targets(seenAt)));
            return brain;
        }

        /// <summary>A monster in 수색 around the position it last saw the target at.</summary>
        private static MonsterBrain SearchingBrain(OpenRoom world, out Vec3 lastSeen)
        {
            var brain = ChasingBrain(world, out lastSeen);
            world.LineOfSight = false;

            // Stops on the sub-step the release fires, so 수색 starts with its 15 s
            // clock at zero and the give-up tests measure what they claim to.
            var limit = SubSteps(GameConstants.AggroReleaseLineOfSightBreak * 2f);
            for (var i = 0; i < limit && brain.State != MonsterStateId.Search; i++)
            {
                brain.Tick(GameConstants.FixedStep, HoldingInput(brain, HoldDistance));
            }

            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Search), "Fixture failed to reach 수색.");
            world.LineOfSight = true;
            return brain;
        }

        /// <summary>A monster in 정지, reached the way §06 says it is reached — from patrol.</summary>
        private static MonsterBrain StandingStillBrain(OpenRoom world)
        {
            var brain = NewBrain(world);
            AdvanceUntil(brain, Input(), MonsterStateId.Standstill, GameConstants.StandstillIntervalMax + 1f);
            Assert.That(brain.State, Is.EqualTo(MonsterStateId.Standstill), "Fixture failed to reach 정지.");
            return brain;
        }

        private static void Advance(MonsterBrain brain, float seconds, MonsterTickInput input)
        {
            var steps = SubSteps(seconds);
            for (var i = 0; i < steps; i++)
            {
                brain.Tick(GameConstants.FixedStep, input);
            }
        }

        /// <summary>
        /// Advances with the target holding <paramref name="distance"/> ahead of the
        /// monster every sub-step — a Runner keeping its lead. Without this the monster
        /// walks to the last seen position and closes the gap itself, which would test
        /// the wrong clause of §06's release rule.
        /// </summary>
        private static void AdvanceHolding(MonsterBrain brain, float seconds, float distance)
        {
            var steps = SubSteps(seconds);
            for (var i = 0; i < steps; i++)
            {
                brain.Tick(GameConstants.FixedStep, HoldingInput(brain, distance));
            }
        }

        /// <summary>One tick's input with the target holding <paramref name="distance"/> dead ahead.</summary>
        private static MonsterTickInput HoldingInput(MonsterBrain brain, float distance) =>
            Input().WithTargets(Targets(brain.Position + (Vec3.Forward * distance)));

        /// <summary>Ticks until the state is reached, returning the seconds it took.</summary>
        private static float AdvanceUntil(
            MonsterBrain brain, MonsterTickInput input, MonsterStateId state, float limitSeconds)
        {
            var steps = SubSteps(limitSeconds);
            for (var i = 0; i < steps; i++)
            {
                brain.Tick(GameConstants.FixedStep, input);
                if (brain.State == state)
                {
                    return (i + 1) * GameConstants.FixedStep;
                }
            }

            return limitSeconds;
        }

        private static int SubSteps(float seconds) =>
            (int)MathF.Ceiling(seconds / GameConstants.FixedStep);

        /// <summary>
        /// A featureless navigable plane: straight-line paths, one sight-line switch,
        /// and an optional zone boundary at <see cref="ZoneSplitX"/>. Geometry is not
        /// what the transition tests are about, so there is none.
        /// </summary>
        private sealed class OpenRoom : IWorldProbe
        {
            public bool LineOfSight = true;

            public float ZoneSplitX = float.PositiveInfinity;

            public bool HasLineOfSight(Vec3 from, Vec3 to) => LineOfSight;

            public float NavigableDistance(Vec3 from, Vec3 to) => Vec3.DistanceFlat(from, to);

            public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
            {
                next = to;
                return true;
            }

            public FloorMaterial SampleFloor(Vec3 position) => FloorMaterial.Concrete;

            public int ZoneIdAt(Vec3 position) => position.X >= ZoneSplitX ? 1 : 0;

            public Vec3 SnapToNavigable(Vec3 desired) => desired.Flat;

            public bool IsAreaLit(Vec3 position) => false;
        }

        /// <summary>A probe that reports nothing reachable, nothing visible and no zones.</summary>
        private sealed class VoidWorld : IWorldProbe
        {
            public bool HasLineOfSight(Vec3 from, Vec3 to) => false;

            public float NavigableDistance(Vec3 from, Vec3 to) => float.PositiveInfinity;

            public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
            {
                next = from;
                return false;
            }

            public FloorMaterial SampleFloor(Vec3 position) => FloorMaterial.None;

            public int ZoneIdAt(Vec3 position) => -1;

            public Vec3 SnapToNavigable(Vec3 desired) => desired;

            public bool IsAreaLit(Vec3 position) => false;
        }

        /// <summary>
        /// A world that is one polyline — §12's S자 통로. Walking distance is arc length
        /// along it and the path points are its bends, so anything that moves toward a
        /// destination in a straight line leaves the corridor and the test can see it.
        /// </summary>
        private sealed class SCorridor : IWorldProbe
        {
            private readonly Vec3[] _nodes;
            private readonly float[] _arcs;

            public SCorridor(params Vec3[] nodes)
            {
                _nodes = nodes;
                _arcs = new float[nodes.Length];
                for (var i = 1; i < nodes.Length; i++)
                {
                    _arcs[i] = _arcs[i - 1] + Vec3.DistanceFlat(nodes[i - 1], nodes[i]);
                }
            }

            public float TotalLength => _arcs[_arcs.Length - 1];

            /// <summary>Arc length of the point on the corridor nearest <paramref name="p"/>.</summary>
            public float ArcOf(Vec3 p)
            {
                var bestArc = 0f;
                var bestDistance = float.PositiveInfinity;

                for (var i = 0; i + 1 < _nodes.Length; i++)
                {
                    var a = _nodes[i];
                    var ab = (_nodes[i + 1] - a).Flat;
                    var length = ab.MagnitudeFlat;
                    if (length <= MathX.Epsilon)
                    {
                        continue;
                    }

                    var t = MathX.Clamp01(Vec3.Dot((p - a).Flat, ab) / (length * length));
                    var distance = Vec3.DistanceFlat(p, a + (ab * t));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestArc = _arcs[i] + (length * t);
                    }
                }

                return bestArc;
            }

            /// <summary>The corridor point at an arc length, clamped to its ends.</summary>
            public Vec3 PointAt(float arc)
            {
                arc = MathX.Clamp(arc, 0f, TotalLength);
                for (var i = 0; i + 1 < _nodes.Length; i++)
                {
                    if (arc > _arcs[i + 1])
                    {
                        continue;
                    }

                    var span = _arcs[i + 1] - _arcs[i];
                    var t = span <= MathX.Epsilon ? 0f : (arc - _arcs[i]) / span;
                    return Vec3.Lerp(_nodes[i], _nodes[i + 1], t);
                }

                return _nodes[_nodes.Length - 1];
            }

            /// <summary>How far off the corridor a point is — zero unless something cut a corner.</summary>
            public float DistanceToCorridor(Vec3 p) => Vec3.DistanceFlat(p, PointAt(ArcOf(p)));

            public bool HasLineOfSight(Vec3 from, Vec3 to) => false;

            public float NavigableDistance(Vec3 from, Vec3 to) => MathF.Abs(ArcOf(to) - ArcOf(from));

            public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
            {
                var here = ArcOf(from);
                var there = ArcOf(to);
                next = PointAt(there);

                if (MathF.Abs(there - here) <= MathX.Epsilon)
                {
                    return true;
                }

                if (there > here)
                {
                    for (var i = 0; i < _arcs.Length; i++)
                    {
                        if (_arcs[i] > here + MathX.Epsilon && _arcs[i] < there)
                        {
                            next = PointAt(_arcs[i]);
                            return true;
                        }
                    }
                }
                else
                {
                    for (var i = _arcs.Length - 1; i >= 0; i--)
                    {
                        if (_arcs[i] < here - MathX.Epsilon && _arcs[i] > there)
                        {
                            next = PointAt(_arcs[i]);
                            return true;
                        }
                    }
                }

                return true;
            }

            public FloorMaterial SampleFloor(Vec3 position) => FloorMaterial.Wood;

            public int ZoneIdAt(Vec3 position) => 0;

            public Vec3 SnapToNavigable(Vec3 desired) => PointAt(ArcOf(desired));

            public bool IsAreaLit(Vec3 position) => false;
        }

        /// <summary>Remembers what the brain reported, so a single tick's history can be asserted on.</summary>
        private sealed class RecordingTelemetry : ITelemetrySink
        {
            public readonly List<string> Counters = new List<string>();

            public readonly List<KeyValuePair<string, float>> Observations =
                new List<KeyValuePair<string, float>>();

            public void Increment(string counter, int amount = 1) => Counters.Add(counter);

            public void Observe(string histogram, float value) =>
                Observations.Add(new KeyValuePair<string, float>(histogram, value));

            public void RecordMatchSummary(MatchSummary summary)
            {
            }

            public void Flush()
            {
            }
        }
    }
}
