using System;
using HorrorGame.Core;
using HorrorGame.Core.Math;
using HorrorGame.Core.Movement;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §05 (조작과 이동) and the stamina half of §06, asserted as the design's own
    /// reasoning rather than as a restatement of the code.
    /// <para>
    /// §14 puts this file at the centre of the project: its validation questions 1
    /// and 2 are "추격이 재밌는가" and "곁눈질 딜레마가 작동하는가", and it says outright
    /// that "어그로 수치와 속도 배율에 게임이 걸려 있다". Every test here is a sentence
    /// from §05 or §06 turned into arithmetic, so retuning a multiplier fails
    /// whichever sentence it stopped being true of.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MovementTests
    {
        // ====================================================================
        // §05 — the speed table, and the fact that it is a curve.
        // ====================================================================

        /// <summary>
        /// §05's four rows must come out exactly at the four keyboard inputs that
        /// produce them. The curve between them is an interpolation, but the table
        /// itself is the design and must not be approximated.
        /// </summary>
        [Test]
        public void DirectionalTable_MatchesSection05_AtTheFourInputs()
        {
            Assert.That(SpeedResolver.DirectionalMultiplier(new MoveInput(1f, 0f)),
                Is.EqualTo(GameConstants.MulForward).Within(1e-5f), "W — 전진 100%.");
            Assert.That(SpeedResolver.DirectionalMultiplier(new MoveInput(1f, 1f)),
                Is.EqualTo(GameConstants.MulDiagonal).Within(1e-5f), "W+D — 대각 95%, the 45° peek.");
            Assert.That(SpeedResolver.DirectionalMultiplier(new MoveInput(0f, 1f)),
                Is.EqualTo(GameConstants.MulStrafe).Within(1e-5f), "D — 측면 90%.");
            Assert.That(SpeedResolver.DirectionalMultiplier(new MoveInput(-1f, 0f)),
                Is.EqualTo(GameConstants.MulBackward).Within(1e-5f), "S — 후진 65%.");

            Assert.That(SpeedResolver.DirectionalMultiplier(new MoveInput(1f, -1f)),
                Is.EqualTo(GameConstants.MulDiagonal).Within(1e-5f), "W+A must cost the same as W+D.");
        }

        /// <summary>
        /// §05: "이산적 선택이 아니라 아날로그 조절이라는 점이 중요하다 — 마우스를 몇 도
        /// 돌릴지가 곧 실력이다." A few degrees of turn must cost a few degrees'
        /// worth of speed. If the multiplier were four buckets, every heading inside
        /// a bucket would return the same number and turning would be free until it
        /// suddenly was not.
        /// </summary>
        [Test]
        public void DirectionalMultiplier_IsAnalogue_NotFourBuckets()
        {
            var at5 = SpeedResolver.DirectionalMultiplier(InputAtHeading(5f));
            var at10 = SpeedResolver.DirectionalMultiplier(InputAtHeading(10f));
            var at20 = SpeedResolver.DirectionalMultiplier(InputAtHeading(20f));
            var at30 = SpeedResolver.DirectionalMultiplier(InputAtHeading(30f));

            Assert.That(at5, Is.LessThan(GameConstants.MulForward));
            Assert.That(at10, Is.LessThan(at5));
            Assert.That(at20, Is.LessThan(at10));
            Assert.That(at30, Is.LessThan(at20));
            Assert.That(at30, Is.GreaterThan(GameConstants.MulDiagonal),
                "30° must cost less than the full 45° peek, or the peek stops being an angle the player chooses.");

            // Half the peek angle must cost about half the peek's price. Exactly
            // half, in fact, on a piecewise-linear curve — the point is that the
            // cost is proportional to the turn rather than snapped to a row.
            var halfPeek = SpeedResolver.DirectionalMultiplier(InputAtHeading(GameConstants.PeekAngleDegrees * 0.5f));
            var halfPrice = GameConstants.MulForward
                            - ((GameConstants.MulForward - GameConstants.MulDiagonal) * 0.5f);
            Assert.That(halfPeek, Is.EqualTo(halfPrice).Within(1e-4f));
        }

        /// <summary>
        /// §05 orders the table so that information always costs speed: every degree
        /// further from your heading is slower than the degree before it. A curve
        /// that rose anywhere would sell a player a free look, and §10's dilemma
        /// principle would have a hole in it exactly where the game is tensest.
        /// </summary>
        [Test]
        public void DirectionalMultiplier_DecreasesMonotonically_AsTheHeadingTurnsAway()
        {
            var previous = SpeedResolver.MultiplierForHeading(0f);
            Assert.That(previous, Is.EqualTo(GameConstants.MulForward).Within(1e-6f));

            for (var degrees = 0.25f; degrees <= GameConstants.BackwardAngleDegrees; degrees += 0.25f)
            {
                var current = SpeedResolver.MultiplierForHeading(degrees);

                Assert.That(current, Is.LessThanOrEqualTo(previous + 1e-6f),
                    $"Turning to {degrees}° got faster. §05 requires the cost to rise with the angle.");
                Assert.That(previous - current, Is.LessThan(0.01f),
                    $"The curve jumps at {degrees}°, which reads to the player as a bucket boundary.");

                previous = current;
            }

            Assert.That(previous, Is.EqualTo(GameConstants.MulBackward).Within(1e-5f));
        }

        /// <summary>
        /// Turning left and right must cost the same. §05 makes the peek a choice of
        /// angle; if one side were cheaper it would become a choice of side, and the
        /// map would have a handedness the design never asked for.
        /// </summary>
        [Test]
        public void DirectionalMultiplier_IsSymmetric_LeftAndRight()
        {
            for (var degrees = 0f; degrees <= GameConstants.BackwardAngleDegrees; degrees += 7.5f)
            {
                var right = SpeedResolver.DirectionalMultiplier(InputAtHeading(degrees));
                var left = SpeedResolver.DirectionalMultiplier(InputAtHeading(-degrees));
                Assert.That(left, Is.EqualTo(right).Within(1e-5f), $"Asymmetric at {degrees}°.");
                Assert.That(SpeedResolver.MultiplierForHeading(-degrees),
                    Is.EqualTo(SpeedResolver.MultiplierForHeading(degrees)).Within(1e-6f));
            }
        }

        /// <summary>The angle knots must stay ordered, or the interpolation runs backwards between two of them.</summary>
        [Test]
        public void AngleKnots_AreOrderedAndSpanAHalfTurn()
        {
            Assert.That(GameConstants.PeekAngleDegrees, Is.GreaterThan(0f));
            Assert.That(GameConstants.PeekAngleDegrees, Is.LessThan(GameConstants.StrafeAngleDegrees));
            Assert.That(GameConstants.StrafeAngleDegrees, Is.LessThan(GameConstants.BackwardAngleDegrees));
            Assert.That(GameConstants.BackwardAngleDegrees, Is.EqualTo(180f),
                "Straight back is half a turn; anything else means the curve never reaches 후진 65%.");
        }

        /// <summary>
        /// An input vector's heading must survive the round trip, since that heading
        /// is the only thing the multiplier reads.
        /// </summary>
        [Test]
        [TestCase(0f)]
        [TestCase(17f)]
        [TestCase(45f)]
        [TestCase(90f)]
        [TestCase(133f)]
        [TestCase(180f)]
        public void HeadingOffset_RoundTripsThroughTheInputVector(float degrees)
        {
            Assert.That(InputAtHeading(degrees).HeadingOffsetDegrees, Is.EqualTo(degrees).Within(0.01f));
            Assert.That(InputAtHeading(-degrees).HeadingOffsetDegrees, Is.EqualTo(degrees).Within(0.01f),
                "The offset is unsigned — the multiplier does not care which way you turned.");
        }

        // ====================================================================
        // §05 — the table's whole purpose: the comparison against the monster.
        // ====================================================================

        /// <summary>
        /// §05's table quotes a "괴물(4.8) 대비" column: +0.8 / +0.52 / +0.24 / −1.16.
        /// Those four numbers are the design's argument, so they are asserted
        /// directly rather than derived from the multipliers again.
        /// </summary>
        [Test]
        public void Section05Table_MonsterMargins_MatchTheDocument()
        {
            var runner = MovementContext.Unloaded(GameConstants.RunnerSprintSpeed);

            Assert.That(Margin(new MoveInput(1f, 0f), runner), Is.EqualTo(0.8f).Within(0.01f), "전진: +0.8 벌어짐.");
            Assert.That(Margin(new MoveInput(1f, 1f), runner), Is.EqualTo(0.52f).Within(0.01f), "대각: +0.52 벌어짐.");
            Assert.That(Margin(new MoveInput(0f, 1f), runner), Is.EqualTo(0.24f).Within(0.01f), "측면: +0.24 벌어짐.");
            Assert.That(Margin(new MoveInput(-1f, 0f), runner), Is.EqualTo(-1.16f).Within(0.01f), "후진: −1.16 좁혀짐.");
        }

        /// <summary>
        /// §05's headline conclusion — "뒷걸음은 괴물보다 느리다. 뒤를 보면 잡힌다" — has
        /// to hold for every speed a player can be moving at, not just the Runner's
        /// sprint. If any base speed backpedalled faster than the monster, that speed
        /// would become the safe way to watch your pursuer and the dilemma would be
        /// solved by holding one key.
        /// </summary>
        [Test]
        public void Backpedalling_LosesGroundToTheMonster_AtEveryBaseSpeed()
        {
            var speeds = new[] { GameConstants.WalkSpeed, GameConstants.RunSpeed, GameConstants.RunnerSprintSpeed };

            foreach (var baseSpeed in speeds)
            {
                var context = MovementContext.Unloaded(baseSpeed);
                var margin = SpeedResolver.MarginVersusMonster(
                    new MoveInput(-1f, 0f), context, GameConstants.MonsterBaseSpeed);

                Assert.That(margin.IsLosingGround, Is.True,
                    $"Backpedalling at base {baseSpeed} m/s does not lose ground: {margin}.");
                Assert.That(margin.SecondsToOpen(GameConstants.AggroReleaseDistance),
                    Is.EqualTo(float.PositiveInfinity),
                    "Facing backwards must never be able to open the release distance.");
            }

            // And every heading past pure strafe, not only the S key: the cost curve
            // crosses monster speed somewhere, and it must cross before you can see
            // behind you.
            var sprint = MovementContext.Unloaded(GameConstants.RunnerSprintSpeed);
            Assert.That(Margin(InputAtHeading(GameConstants.BackwardAngleDegrees * 0.75f), sprint),
                Is.LessThan(0f), "135° — most of the way round — must already be losing ground.");
        }

        /// <summary>
        /// §05 calls the 45° peek 숙련 기술: the skilled player watches the monster
        /// while still pulling away. That only exists if the peek margin is positive,
        /// and the design puts it at +0.52 m/s.
        /// </summary>
        [Test]
        public void FortyFiveDegreePeek_KeepsAPositiveMargin()
        {
            var runner = MovementContext.Unloaded(GameConstants.RunnerSprintSpeed);
            var margin = SpeedResolver.MarginVersusMonster(
                new MoveInput(1f, 1f), runner, GameConstants.MonsterBaseSpeed);

            Assert.That(margin.IsGainingGround, Is.True,
                "§05: 곁눈질 must still out-pace the monster, or checking your distance is strictly wrong "
                + "and the analogue dilemma collapses into 'never look back'.");
            Assert.That(margin.MetresPerSecond, Is.EqualTo(0.52f).Within(0.01f));
            Assert.That(margin.DistanceChangeOver(GameConstants.SprintStaminaSeconds),
                Is.EqualTo(6.24f).Within(0.01f),
                "§05 recomputes the sprint's gain at 6.2 m once the peek multiplier is applied.");

            // The window that still gains ground closes just past pure strafe. That
            // narrowness is the skill §05 is describing: the player is choosing an
            // angle inside a band about 105° wide, not picking one of four rows.
            Assert.That(Margin(new MoveInput(0f, 1f), runner), Is.GreaterThan(0f), "Pure strafe still gains ground.");

            var crossing = FirstHeadingLosingGround(GameConstants.RunnerSprintSpeed);
            Assert.That(crossing, Is.GreaterThan(GameConstants.StrafeAngleDegrees),
                "A sprinting Runner must keep a margin all the way round to pure strafe, or the peek has no room.");
            Assert.That(crossing, Is.LessThan(GameConstants.BackwardAngleDegrees * 0.75f),
                "And must be losing ground well before facing backwards, or 후진 65% is not the threat §05 says it is.");
        }

        /// <summary>
        /// §06: "질주만으로는 절대 못 벌린다." One bar cannot open the 12 m release
        /// distance, and at the peek multiplier it is further from doing so than
        /// §06's own straight-line arithmetic suggests. This is the reason aggro
        /// release had to become a map problem (§12) instead of a speed problem.
        /// </summary>
        [Test]
        public void OneBar_CannotOpenTheReleaseDistance_EvenLessSoWhilePeeking()
        {
            var runner = MovementContext.Unloaded(GameConstants.RunnerSprintSpeed);

            var straight = SpeedResolver.MarginVersusMonster(
                new MoveInput(1f, 0f), runner, GameConstants.MonsterBaseSpeed);
            var peeking = SpeedResolver.MarginVersusMonster(
                new MoveInput(1f, 1f), runner, GameConstants.MonsterBaseSpeed);

            Assert.That(straight.SecondsToOpen(GameConstants.AggroReleaseDistance),
                Is.GreaterThan(GameConstants.SprintStaminaSeconds),
                "§06: 12 m at +0.8 m/s takes 15 s, and the bar only lasts 12 s.");
            Assert.That(peeking.SecondsToOpen(GameConstants.AggroReleaseDistance),
                Is.GreaterThan(straight.SecondsToOpen(GameConstants.AggroReleaseDistance)),
                "Looking back must make the release strictly harder, never easier.");
        }

        /// <summary>
        /// The margin helper has to answer "how long have I got" as well as "am I
        /// faster", because that is the question the HUD is really being asked
        /// during a chase.
        /// </summary>
        [Test]
        public void ChaseMargin_AnswersHowLongUntilCaught()
        {
            var runner = MovementContext.Unloaded(GameConstants.RunnerSprintSpeed);
            var backwards = SpeedResolver.MarginVersusMonster(
                new MoveInput(-1f, 0f), runner, GameConstants.MonsterBaseSpeed);

            // 12 m of release distance, thrown away at 1.16 m/s.
            Assert.That(backwards.SecondsUntilCaught(GameConstants.AggroReleaseDistance),
                Is.EqualTo(GameConstants.AggroReleaseDistance / 1.16f).Within(0.05f));

            var forwards = SpeedResolver.MarginVersusMonster(
                new MoveInput(1f, 0f), runner, GameConstants.MonsterBaseSpeed);
            Assert.That(forwards.SecondsUntilCaught(GameConstants.AggroReleaseDistance),
                Is.EqualTo(float.PositiveInfinity), "While gaining ground there is no time-to-caught.");

            Assert.That(backwards.SecondsUntilCaught(0f), Is.EqualTo(0f), "Already caught.");
            Assert.That(backwards.SecondsUntilCaught(float.NaN), Is.EqualTo(0f), "A garbage gap must not produce NaN.");
            Assert.That(backwards.DistanceChangeOver(-5f), Is.EqualTo(0f), "Time does not run backwards.");
        }

        // ====================================================================
        // §08 — DELETED. Two tests stood here:
        //   Resolve_StacksLoadObjectiveAndBagMultiplicatively
        //   CarryingTheObjective_LeavesEvenTheRunnerBelowTheMonster
        // Together they pinned §05's product against §03's 목표물 and §08's 가방:
        // that the penalties multiply rather than cancel, and that a carrier can
        // never outrun the creature so "2인 1조 호송" stays compulsory. Both facts
        // are about things a runner carries, and a runner carries nothing. What is
        // left of the product — direction × stance — is pinned by the §05 tests
        // above and by MarginVersusMonster below, which is the assertion that
        // actually decides whether the race is survivable.
        // ====================================================================

        /// <summary>
        /// Analogue deflection scales the speed without touching the table. A stick
        /// at half tilt is half as fast in the same direction — §05 wants speed
        /// controlled continuously, and a keyboard's W+D must not be a secret 141%.
        /// </summary>
        [Test]
        public void Resolve_Deflection_ScalesSpeedWithoutChangingTheTable()
        {
            var context = MovementContext.Unloaded(GameConstants.RunSpeed);

            var full = SpeedResolver.Resolve(new MoveInput(1f, 0f), context);
            var half = SpeedResolver.Resolve(new MoveInput(0.5f, 0f), context);
            Assert.That(half, Is.EqualTo(full * 0.5f).Within(1e-4f));

            var keyboardDiagonal = SpeedResolver.Resolve(new MoveInput(1f, 1f), context);
            var stickDiagonal = SpeedResolver.Resolve(
                new MoveInput(MathF.Sqrt(0.5f), MathF.Sqrt(0.5f)), context);
            Assert.That(keyboardDiagonal, Is.EqualTo(stickDiagonal).Within(1e-4f),
                "W+D has length √2; if it were not clamped, the diagonal would be the fastest way to travel.");
            Assert.That(keyboardDiagonal,
                Is.EqualTo(GameConstants.RunSpeed * GameConstants.MulDiagonal).Within(1e-4f));
        }

        /// <summary>
        /// Nothing pressed, nothing moves — and the multiplier for a heading that
        /// does not exist must not be NaN, because it would spread into the position
        /// integrator and never come back.
        /// </summary>
        [Test]
        public void Resolve_IdleInput_IsZeroNotNaN()
        {
            var context = MovementContext.Unloaded(GameConstants.RunSpeed);
            var idle = new MoveInput(0f, 0f);

            Assert.That(idle.IsIdle, Is.True);
            Assert.That(SpeedResolver.Resolve(idle, context), Is.EqualTo(0f));
            Assert.That(SpeedResolver.DirectionalMultiplier(idle),
                Is.EqualTo(GameConstants.MulForward).Within(1e-6f),
                "An absent heading reads as forward; the deflection is what zeroes the speed.");
            Assert.That(SpeedResolver.ResolveVelocity(idle, context, 37f), Is.EqualTo(Vec3.Zero));
        }

        /// <summary>
        /// A default-constructed context has a zero load multiplier and must not
        /// move. Reading 0 as "unloaded" would hide a context nobody filled in, and
        /// the bug would surface as a player who is mysteriously never slowed by
        /// loot.
        /// </summary>
        [Test]
        public void Resolve_DefaultContext_DoesNotMove()
        {
            Assert.That(SpeedResolver.Resolve(new MoveInput(1f, 0f), default), Is.EqualTo(0f));
            Assert.That(SpeedResolver.Resolve(new MoveInput(1f, 0f), new MovementContext(GameConstants.RunSpeed, 0f)),
                Is.EqualTo(0f));
        }

        /// <summary>
        /// §13 puts the host in authority, which means a client can send anything at
        /// all. Garbage input must resolve to a stopped player, never to NaN
        /// (unbounded position) or a negative speed (walking backwards through the
        /// map).
        /// </summary>
        [Test]
        public void Resolve_GarbageInputAndContext_AreClampedToStopped()
        {
            var context = MovementContext.Unloaded(GameConstants.RunSpeed);

            Assert.That(SpeedResolver.Resolve(new MoveInput(float.NaN, 0f), context), Is.EqualTo(0f));
            Assert.That(SpeedResolver.Resolve(new MoveInput(float.PositiveInfinity, 0f), context), Is.EqualTo(0f));

            var overdriven = SpeedResolver.Resolve(new MoveInput(50f, 0f), context);
            Assert.That(overdriven, Is.EqualTo(GameConstants.RunSpeed).Within(1e-4f),
                "An out-of-range axis must clamp to full deflection, not scale the speed by 50.");

            var negativeLoad = new MovementContext(GameConstants.RunSpeed, -2f);
            Assert.That(SpeedResolver.Resolve(new MoveInput(1f, 0f), negativeLoad), Is.EqualTo(0f),
                "A broken load multiplier must stop the player, not reverse them.");

            var nanLoad = new MovementContext(GameConstants.RunSpeed, float.NaN);
            Assert.That(SpeedResolver.Resolve(new MoveInput(1f, 0f), nanLoad), Is.EqualTo(0f));
            Assert.That(SpeedResolver.Resolve(new MoveInput(1f, 0f),
                new MovementContext(float.NaN, 1f)), Is.EqualTo(0f));
        }

        /// <summary>
        /// §05: "마우스 방향 = 이동 방향. 뒤를 보려고 마우스를 돌리면 이동 기준도 함께
        /// 돌아간다." W follows the camera, and the resulting velocity carries the
        /// resolved speed and nothing else.
        /// </summary>
        [Test]
        public void ResolveVelocity_FollowsTheCameraYaw()
        {
            var context = MovementContext.Unloaded(GameConstants.RunSpeed);
            var forwardInput = new MoveInput(1f, 0f);

            var facingNorth = SpeedResolver.ResolveVelocity(forwardInput, context, 0f);
            Assert.That(facingNorth.Z, Is.EqualTo(GameConstants.RunSpeed).Within(1e-3f));
            Assert.That(facingNorth.X, Is.EqualTo(0f).Within(1e-3f));

            var facingEast = SpeedResolver.ResolveVelocity(forwardInput, context, 90f);
            Assert.That(facingEast.X, Is.EqualTo(GameConstants.RunSpeed).Within(1e-3f),
                "Turning the mouse 90° must turn W with it.");
            Assert.That(facingEast.Z, Is.EqualTo(0f).Within(1e-3f));

            // Strafing right of east is south, in Unity's left-handed frame.
            var strafeEast = SpeedResolver.ResolveVelocity(new MoveInput(0f, 1f), context, 90f);
            Assert.That(strafeEast.Z, Is.EqualTo(-GameConstants.RunSpeed * GameConstants.MulStrafe).Within(1e-3f));

            // The velocity's length is exactly the resolved scalar, at every heading.
            for (var degrees = 0f; degrees <= GameConstants.BackwardAngleDegrees; degrees += 15f)
            {
                var input = InputAtHeading(degrees);
                var velocity = SpeedResolver.ResolveVelocity(input, context, 213f);
                Assert.That(velocity.MagnitudeFlat,
                    Is.EqualTo(SpeedResolver.Resolve(input, context)).Within(1e-3f), $"At {degrees}°.");
            }

            Assert.That(SpeedResolver.ResolveVelocity(forwardInput, context, float.NaN), Is.EqualTo(Vec3.Zero),
                "A corrupt camera yaw must stop the player rather than send them to NaN.");
        }

        /// <summary>
        /// §06's ladder is chosen by Shift plus permission: walk without it, run with
        /// it, sprint only when the bar allows it. The 직업 and 하중 vetoes that used
        /// to sit alongside the bar are gone with §04 and §08, so the bar is now the
        /// only thing that can refuse a sprint — which is what makes 12 s a number a
        /// player can feel rather than one of three reasons Shift did nothing.
        /// </summary>
        [Test]
        public void SelectBaseSpeed_WalksRunsAndSprintsOnlyWhenAllowed()
        {
            var walking = new MoveInput(1f, 0f);
            var shift = new MoveInput(1f, 0f, sprintHeld: true);

            Assert.That(SpeedResolver.SelectBaseSpeed(walking, true, true),
                Is.EqualTo(GameConstants.WalkSpeed));
            Assert.That(SpeedResolver.SelectBaseSpeed(shift, sprintUnlocked: false, staminaReady: true),
                Is.EqualTo(GameConstants.RunSpeed), "A runner without the sprint unlocked still runs — §05 never drops anyone to a walk for it.");
            Assert.That(SpeedResolver.SelectBaseSpeed(shift, sprintUnlocked: true, staminaReady: false),
                Is.EqualTo(GameConstants.RunSpeed), "An empty bar drops the runner to a run, not to a walk.");
            Assert.That(SpeedResolver.SelectBaseSpeed(shift, sprintUnlocked: true, staminaReady: true),
                Is.EqualTo(GameConstants.RunnerSprintSpeed));
        }

        // ====================================================================
        // §06 — stamina: 12 s of sprint, 20 s to refill.
        // ====================================================================

        /// <summary>§06: a full bar is exactly 12 s of sprint, and it ends in exhaustion rather than trailing off.</summary>
        [Test]
        public void FullBar_LastsTwelveSeconds()
        {
            var stamina = new StaminaState { SprintRequested = true };
            var sprinted = 0f;
            var steps = 0;

            while (!stamina.ExhaustedThisTick && steps < 100000)
            {
                stamina.Tick(GameConstants.FixedStep);
                sprinted += stamina.LastSprintSeconds;
                steps++;
            }

            Assert.That(stamina.ExhaustedThisTick, Is.True, "The bar never ran out.");
            Assert.That(sprinted, Is.EqualTo(GameConstants.SprintStaminaSeconds).Within(0.05f));
            Assert.That(stamina.Fraction, Is.EqualTo(0f));
            Assert.That(stamina.IsSprinting, Is.False);

            var travel = sprinted * GameConstants.RunnerSprintSpeed * GameConstants.MulDiagonal;
            Assert.That(travel, Is.GreaterThan(GameConstants.SprintMaxTravelDistance - 1f),
                "§05 sizes 질주 최대 이동 거리 from this bar at the peek multiplier.");
        }

        /// <summary>
        /// §06: an empty bar refills in 20 s. The recovery delay sits in front of
        /// that rather than inside it, so the documented refill time stays the
        /// documented refill time.
        /// </summary>
        [Test]
        public void EmptyBar_RefillsInTwentySeconds_AfterTheDelay()
        {
            var fromZero = new StaminaState(0f);
            var seconds = StepUntilFull(fromZero);
            Assert.That(seconds, Is.EqualTo(GameConstants.SprintStaminaRecoverySeconds).Within(0.1f),
                "§06: 스태미나 완전 회복 20초.");

            var drained = DrainedState();
            var afterSprinting = StepUntilFull(drained);
            Assert.That(afterSprinting,
                Is.EqualTo(GameConstants.SprintStaminaRecoverySeconds + GameConstants.SprintRecoveryDelaySeconds)
                    .Within(0.1f),
                "A bar emptied by sprinting waits out the recovery delay first.");
        }

        /// <summary>A partial sprint must cost exactly the time it was used for — the bar is a resource, not a cooldown.</summary>
        [Test]
        public void PartialSprint_CostsExactlyWhatItUsed()
        {
            var stamina = new StaminaState { SprintRequested = true };
            var target = GameConstants.SprintStaminaSeconds * 0.25f;
            var sprinted = 0f;

            while (sprinted < target)
            {
                stamina.Tick(GameConstants.FixedStep);
                sprinted += stamina.LastSprintSeconds;
            }

            Assert.That(stamina.Fraction, Is.EqualTo(0.75f).Within(0.01f));
            Assert.That(stamina.SecondsRemaining,
                Is.EqualTo(GameConstants.SprintStaminaSeconds - sprinted).Within(0.05f));
            Assert.That(stamina.IsSprinting, Is.True);
            Assert.That(stamina.SprintAvailable, Is.True);
        }

        /// <summary>
        /// The exploit the lockout exists for: chaining tiny sprints. §06 promises
        /// "주자도 스태미나가 끝나면 잡힌다", and that promise dies if a burst returns
        /// more than it costs. Tapping must be strictly worse than committing, and it
        /// must not sustain an escape.
        /// </summary>
        [Test]
        public void TappingSprint_IsStrictlyWorseThanCommittingToIt()
        {
            const float window = 300f;
            var tapped = SprintSecondsOver(window, 0.1f, 0.1f, 0f);
            var held = SprintSecondsOver(window, window, 0f, 0f);

            Assert.That(tapped, Is.LessThan(held),
                "A 0.1 s on/off pattern bought more sprint than simply holding Shift, which is the exploit "
                + "the recovery delay and the re-engage floor exist to remove.");

            var tapSpeed = BlendedSpeed(tapped / window);
            var holdSpeed = BlendedSpeed(held / window);

            Assert.That(tapSpeed, Is.LessThan(GameConstants.MonsterBaseSpeed),
                $"Tap-cycling averaged {tapSpeed:0.000} m/s, which out-paces the monster indefinitely — "
                + "§06's 무한 도주 방지 would be defeated by a macro.");
            Assert.That(tapSpeed, Is.LessThan(holdSpeed - 0.05f),
                "Tapping must be clearly, not marginally, worse — otherwise players will still do it.");
        }

        /// <summary>
        /// An exhausted bar must stay unusable until it has recovered a real amount.
        /// Without the floor, a Runner at zero spends each sliver of returning
        /// stamina the frame it appears and is never actually caught.
        /// </summary>
        [Test]
        public void ExhaustedBar_RefusesSprintUntilTheReengageFraction()
        {
            var stamina = DrainedState();
            Assert.That(stamina.IsLockedOut, Is.True);
            Assert.That(stamina.SprintAvailable, Is.False);

            var predicted = stamina.SecondsUntilSprintAvailable;
            stamina.SprintRequested = true;

            var elapsed = 0f;
            var sprintedWhileLocked = 0f;
            while (stamina.IsLockedOut && elapsed < 60f)
            {
                stamina.Tick(GameConstants.FixedStep);
                sprintedWhileLocked += stamina.LastSprintSeconds;
                elapsed += GameConstants.FixedStep;
            }

            Assert.That(sprintedWhileLocked, Is.EqualTo(0f),
                "Holding Shift through the lockout must not hand out a single frame of sprint.");
            Assert.That(stamina.Fraction,
                Is.EqualTo(GameConstants.SprintReengageStaminaFraction).Within(0.01f));
            Assert.That(elapsed, Is.EqualTo(predicted).Within(0.1f),
                "SecondsUntilSprintAvailable is what the HUD shows; it must match what actually happens.");
            Assert.That(elapsed,
                Is.EqualTo(GameConstants.SprintRecoveryDelaySeconds
                           + (GameConstants.SprintReengageStaminaFraction
                              * GameConstants.SprintStaminaRecoverySeconds)).Within(0.1f));
        }

        /// <summary>
        /// A zero-length step is not a transition. The host ticks paused and loading
        /// frames, and a stall must cost neither stamina nor a state change.
        /// </summary>
        [Test]
        public void ZeroDeltaSeconds_ChangesNothing()
        {
            var stamina = new StaminaState { SprintRequested = true };

            for (var i = 0; i < 1000; i++)
            {
                stamina.Tick(0f);
            }

            Assert.That(stamina.Fraction, Is.EqualTo(1f));
            Assert.That(stamina.LastSprintSeconds, Is.EqualTo(0f));
            Assert.That(stamina.IsSprinting, Is.False, "Nothing was observed, so nothing was granted.");

            stamina.Tick(GameConstants.FixedStep);
            Assert.That(stamina.IsSprinting, Is.True, "A real step after any number of empty ones must still work.");
        }

        /// <summary>Negative and NaN steps are ignored rather than run backwards or poisoning the bar.</summary>
        [Test]
        public void NegativeAndNaNDeltaSeconds_AreIgnored()
        {
            var stamina = new StaminaState { SprintRequested = true };
            stamina.Tick(GameConstants.FixedStep);
            var reference = stamina.Fraction;

            stamina.Tick(-5f);
            stamina.Tick(float.NaN);

            Assert.That(stamina.Fraction, Is.EqualTo(reference));
            Assert.That(float.IsNaN(stamina.Fraction), Is.False);
            Assert.That(stamina.LastSprintSeconds, Is.EqualTo(0f));
        }

        /// <summary>
        /// A frame spike must not tunnel through exhaustion. The sprint may only
        /// take what the bar held, the exhaustion has to stay observable even when
        /// the same step also refills, and the leftover time has to land on the
        /// recovery delay rather than vanish.
        /// </summary>
        [Test]
        public void HugeDeltaSeconds_DoesNotTunnelPastExhaustion()
        {
            // A spike longer than what is left in the bar. The sprint may take only
            // what is there; the remainder of the frame starts paying off the
            // recovery delay instead of disappearing.
            var partial = new StaminaState { SprintRequested = true };
            while (partial.SecondsRemaining > 0.2f)
            {
                partial.Tick(GameConstants.FixedStep);
            }

            var left = partial.SecondsRemaining;
            partial.Tick(0.5f);

            Assert.That(partial.LastSprintSeconds, Is.EqualTo(left).Within(1e-3f),
                $"A 0.5 s spike on a {left:0.00} s bar must grant {left:0.00} s of sprint, not 0.5 s.");
            Assert.That(partial.ExhaustedThisTick, Is.True);
            Assert.That(partial.Fraction, Is.EqualTo(0f));
            Assert.That(partial.RecoveryDelayRemaining,
                Is.EqualTo(GameConstants.SprintRecoveryDelaySeconds - (0.5f - left)).Within(1e-3f),
                "The part of the frame the sprint could not use must advance the recovery, not vanish.");

            // A spike so long that the bar empties and refills inside it. The
            // exhaustion still has to be visible, or a listener watching only the
            // end-of-step level would never see the Runner run out.
            var spike = new StaminaState { SprintRequested = true };
            spike.Tick(1000f);

            Assert.That(spike.LastSprintSeconds, Is.EqualTo(GameConstants.SprintStaminaSeconds).Within(1e-3f),
                "1000 s of frame time must not buy 1000 s of sprint.");
            Assert.That(spike.ExhaustedThisTick, Is.True,
                "The exhaustion happened inside the step and must be reported, or the transition is skipped.");
            Assert.That(spike.Fraction, Is.EqualTo(1f), "988 s of the spike were spent recovering.");
            Assert.That(spike.IsSprinting, Is.False);

            // Infinity is a corrupt step, not an infinite sprint.
            var infinite = new StaminaState { SprintRequested = true };
            infinite.Tick(float.PositiveInfinity);
            Assert.That(infinite.LastSprintSeconds, Is.EqualTo(GameConstants.SprintStaminaSeconds).Within(1e-3f));
            Assert.That(float.IsNaN(infinite.Fraction), Is.False);
            Assert.That(infinite.Fraction, Is.InRange(0f, 1f));
        }

        /// <summary>
        /// §06 sizes the bar by the distance it covers ("최대 이동 60m"), so standing
        /// still with Shift held must not burn the escape. A player who has stopped
        /// is hiding, which is the opposite of sprinting.
        /// </summary>
        [Test]
        public void HoldingShiftWhileStandingStill_DoesNotDrainTheBar()
        {
            var stamina = new StaminaState();
            var idle = new MoveInput(0f, 0f, sprintHeld: true);

            for (var i = 0; i < 1000; i++)
            {
                stamina.Tick(GameConstants.FixedStep, idle);
            }

            Assert.That(stamina.Fraction, Is.EqualTo(1f));
            Assert.That(stamina.IsSprinting, Is.False);

            stamina.Tick(GameConstants.FixedStep, new MoveInput(1f, 0f, sprintHeld: true));
            Assert.That(stamina.IsSprinting, Is.True);
        }

        /// <summary>Reset returns a spawned or respawned player to a full bar with nothing pending.</summary>
        [Test]
        public void Reset_ReturnsAFullBar()
        {
            var stamina = DrainedState();
            stamina.Reset();

            Assert.That(stamina.Fraction, Is.EqualTo(1f));
            Assert.That(stamina.SprintAvailable, Is.True);
            Assert.That(stamina.RecoveryDelayRemaining, Is.EqualTo(0f));
            Assert.That(stamina.SprintRequested, Is.False);
        }

        // ====================================================================
        // Findings — contradictions the design does not acknowledge.
        // See docs/BALANCE-FINDINGS.md. These tests pin the consequence so a
        // later retune cannot erase it silently.
        // ====================================================================

        /// <summary>
        /// §06 concludes "주자도 스태미나가 끝나면 잡힌다 — 무한 도주 방지" and proves it
        /// with one bar: 0.8 m/s × 12 s = 9.6 m, short of the 12 m release distance.
        /// It never runs the second bar.
        /// <para>
        /// Sprint drains in 12 s and refills in 20 s, so the sustainable share of
        /// time spent sprinting is 12/32 = 37.5%. Blend the speeds at that duty and
        /// the Runner averages 4.91 m/s against a 4.8 m/s monster — a permanent
        /// +0.11 m/s. The gap opens forever; only the size of the map stops it.
        /// </para>
        /// <para>
        /// Break-even would need the refill at 32 s rather than 20 s. That is a
        /// designer's decision, so the contradiction is pinned rather than fixed.
        /// </para>
        /// </summary>
        [Test]
        public void Finding_SprintCycling_BeatsTheMonsterOnSection06NumbersAlone()
        {
            var duty = GameConstants.SprintStaminaSeconds
                       / (GameConstants.SprintStaminaSeconds + GameConstants.SprintStaminaRecoverySeconds);
            Assert.That(duty, Is.EqualTo(0.375f).Within(1e-4f), "12 s of drain against 20 s of refill.");

            var sustained = BlendedSpeed(duty);
            Assert.That(sustained, Is.EqualTo(4.9125f).Within(1e-3f));
            Assert.That(sustained, Is.GreaterThan(GameConstants.MonsterBaseSpeed),
                "If this now fails, §06's stamina numbers were retuned into agreeing with their own "
                + "무한 도주 방지 claim — update docs/BALANCE-FINDINGS.md in the same commit.");

            // One bar cannot open the release distance; cycling the bar can.
            var perCycle = ((GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed)
                            * GameConstants.SprintStaminaSeconds)
                           - ((GameConstants.MonsterBaseSpeed - GameConstants.RunSpeed)
                              * GameConstants.SprintStaminaRecoverySeconds);
            Assert.That(perCycle, Is.EqualTo(3.6f).Within(0.01f), "9.6 m gained, 6.0 m given back.");
            Assert.That(GameConstants.AggroReleaseDistance / perCycle, Is.LessThan(4f),
                "Four cycles — under two minutes — reach the release distance §06 calls unreachable.");

            var breakEvenRecovery = GameConstants.SprintStaminaSeconds
                                    * (((GameConstants.RunnerSprintSpeed - GameConstants.RunSpeed)
                                        / (GameConstants.MonsterBaseSpeed - GameConstants.RunSpeed)) - 1f);
            Assert.That(breakEvenRecovery, Is.EqualTo(32f).Within(0.1f));
            Assert.That(breakEvenRecovery, Is.GreaterThan(GameConstants.SprintStaminaRecoverySeconds),
                "The documented 20 s refill is below the break-even the design's own speeds imply.");
        }

        /// <summary>
        /// The other half of the same finding, and the reason it is survivable: the
        /// sustainable escape only exists while the Runner runs blind.
        /// <para>
        /// The duty cycle multiplies both speeds, so the §05 directional multiplier
        /// scales the whole average. At the 95% peek the cycling Runner drops to
        /// 4.62 m/s and loses ground; the escape only works within about 12° of
        /// straight ahead, where the monster is invisible. §05's dilemma therefore
        /// contains its own exploit — but it does contain it, which is why this is a
        /// tuning note rather than a hole.
        /// </para>
        /// </summary>
        [Test]
        public void Finding_SprintCycling_OnlyWorksIfYouNeverLookBack()
        {
            const float window = 300f;
            var duty = SprintSecondsOver(window, window, 0f, 0f) / window;
            var straight = BlendedSpeed(duty);

            Assert.That(straight, Is.GreaterThan(GameConstants.MonsterBaseSpeed),
                $"Cycling the implemented bar averages {straight:0.000} m/s straight ahead.");
            Assert.That(straight * GameConstants.MulDiagonal, Is.LessThan(GameConstants.MonsterBaseSpeed),
                "§05's peek must claw the sustained escape back — a Runner who watches the monster loses ground.");

            var widestSustainableHeading = 0f;
            for (var degrees = 0f; degrees <= GameConstants.BackwardAngleDegrees; degrees += 0.05f)
            {
                if (straight * SpeedResolver.MultiplierForHeading(degrees) <= GameConstants.MonsterBaseSpeed)
                {
                    break;
                }

                widestSustainableHeading = degrees;
            }

            Assert.That(widestSustainableHeading, Is.GreaterThan(0f));
            Assert.That(widestSustainableHeading, Is.LessThan(GameConstants.PeekAngleDegrees),
                "The sustainable window must stay narrower than the 45° peek, or the skill §05 rewards "
                + "would also be the exploit.");
        }

        /// <summary>
        /// §05 revises 질주 최대 이동 거리 from 67 m to 60 m "once the 95% peek
        /// multiplier was accounted for", but its own multiplier gives
        /// 5.6 × 0.95 × 12 = 63.84 m, not 60 m. The 3.84 m matters to §12: cover is
        /// spaced 15–25 m apart, so the difference is a quarter of a chance to break
        /// line of sight. Pinned so the number that is wrong can be chosen
        /// deliberately.
        /// </summary>
        [Test]
        public void Finding_PeekSprintTravel_ExceedsTheDocumentedSixtyMetres()
        {
            var peekTravel = GameConstants.RunnerSprintSpeed
                             * GameConstants.MulDiagonal
                             * GameConstants.SprintStaminaSeconds;

            Assert.That(peekTravel, Is.EqualTo(63.84f).Within(0.01f));
            Assert.That(peekTravel, Is.GreaterThan(GameConstants.SprintMaxTravelDistance),
                "§05's arithmetic and §05's table disagree about the sprint's reach.");
            Assert.That(peekTravel - GameConstants.SprintMaxTravelDistance,
                Is.LessThan(GameConstants.LineOfSightBreakSpacingMin),
                "The disagreement is under one cover interval, which is why it has gone unnoticed.");
        }

        // ====================================================================
        // Helpers.
        // ====================================================================

        /// <summary>An input pushed fully at <paramref name="degrees"/> off the camera's forward.</summary>
        private static MoveInput InputAtHeading(float degrees, bool sprintHeld = false)
        {
            var radians = degrees * MathX.Deg2Rad;
            return new MoveInput(MathF.Cos(radians), MathF.Sin(radians), sprintHeld);
        }

        /// <summary>Metres per second this input gains on a base-speed monster.</summary>
        private static float Margin(MoveInput input, MovementContext context) =>
            SpeedResolver.MarginVersusMonster(input, context, GameConstants.MonsterBaseSpeed).MetresPerSecond;

        /// <summary>
        /// The heading, in degrees off forward, at which this base speed stops
        /// out-pacing the monster. -1 if it never does.
        /// </summary>
        private static float FirstHeadingLosingGround(float baseSpeed)
        {
            var context = MovementContext.Unloaded(baseSpeed);
            for (var degrees = 0f; degrees <= GameConstants.BackwardAngleDegrees; degrees += 0.05f)
            {
                if (Margin(InputAtHeading(degrees), context) < 0f)
                {
                    return degrees;
                }
            }

            return -1f;
        }

        /// <summary>
        /// Average speed of a Runner who spends <paramref name="duty"/> of the time
        /// sprinting and the rest running, straight ahead. The blend is what a
        /// sustained chase actually looks like.
        /// </summary>
        private static float BlendedSpeed(float duty) =>
            (duty * GameConstants.RunnerSprintSpeed) + ((1f - duty) * GameConstants.RunSpeed);

        /// <summary>A bar drained to zero by sprinting, with the recovery delay pending.</summary>
        private static StaminaState DrainedState()
        {
            var stamina = new StaminaState { SprintRequested = true };
            var guard = 0;
            while (stamina.Fraction > 0f && guard < 100000)
            {
                stamina.Tick(GameConstants.FixedStep);
                guard++;
            }

            stamina.SprintRequested = false;
            return stamina;
        }

        /// <summary>Seconds of fixed steps until the bar is full again.</summary>
        private static float StepUntilFull(StaminaState stamina)
        {
            stamina.SprintRequested = false;
            var elapsed = 0f;
            while (stamina.Fraction < 1f && elapsed < 600f)
            {
                stamina.Tick(GameConstants.FixedStep);
                elapsed += GameConstants.FixedStep;
            }

            return elapsed;
        }

        /// <summary>
        /// Total sprint seconds granted over <paramref name="windowSeconds"/> while
        /// holding Shift for <paramref name="onSeconds"/> and releasing it for
        /// <paramref name="offSeconds"/>, over and over. An
        /// <paramref name="offSeconds"/> of zero holds it down for the whole window.
        /// </summary>
        private static float SprintSecondsOver(
            float windowSeconds, float onSeconds, float offSeconds, float initialFraction)
        {
            var stamina = new StaminaState(initialFraction);
            var step = GameConstants.FixedStep;
            var sprinted = 0f;
            var elapsed = 0f;
            var phase = 0f;
            var holding = true;

            while (elapsed < windowSeconds)
            {
                stamina.SprintRequested = holding;
                stamina.Tick(step);
                sprinted += stamina.LastSprintSeconds;
                elapsed += step;
                phase += step;

                if (offSeconds <= 0f)
                {
                    continue;
                }

                if (holding && phase >= onSeconds - 1e-4f)
                {
                    holding = false;
                    phase = 0f;
                }
                else if (!holding && phase >= offSeconds - 1e-4f)
                {
                    holding = true;
                    phase = 0f;
                }
            }

            return sprinted;
        }
    }
}
