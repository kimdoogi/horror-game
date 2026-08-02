using HorrorGame.Core;
using HorrorGame.Core.Monster;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §06's catch as an act, and the one outcome the five constants exist to produce:
    /// a running player is caught by a committed lunge and a sprinting 주자 is not.
    /// <para>
    /// These drive the struct at a fixed step with a distance that closes at the real
    /// speed difference, rather than asserting on the constants directly. A test that
    /// re-computed 1.375 m from the same numbers the code uses would agree with any
    /// arithmetic, including wrong arithmetic.
    /// </para>
    /// </summary>
    public sealed class MonsterLungeTests
    {
        private const float Step = 1f / 60f;

        [Test]
        public void It_commits_at_the_range_and_not_before()
        {
            var lunge = new MonsterLunge();

            Assert.That(lunge.Tick(Step, chasing: true, GameConstants.MonsterAttackRange + 0.05f),
                Is.EqualTo(LungeEvent.None), "Out of range is not an attack.");
            Assert.That(lunge.State, Is.EqualTo(LungeState.Ready));

            Assert.That(lunge.Tick(Step, chasing: true, GameConstants.MonsterAttackRange - 0.01f),
                Is.EqualTo(LungeEvent.Committed));
            Assert.That(lunge.State, Is.EqualTo(LungeState.Committed));
        }

        [Test]
        public void A_running_player_cannot_escape_a_committed_lunge()
        {
            // §01: 「이길 수 없는 존재」. The creature closes at MonsterLungeSpeed − RunSpeed
            // for the whole strike, and the gap it has to make up is range − reach.
            var outcome = Resolve(GameConstants.RunSpeed);

            Assert.That(outcome, Is.EqualTo(LungeEvent.Hit),
                "A player who is merely running has to be caught. If this passes as a miss "
                + "the creature has an attack that lands on nobody and §06's chase stops "
                + "being the pressure the game is built on.");
        }

        [Test]
        public void A_sprinting_runner_survives_one()
        {
            // §04 주자's whole role, in one moment: 0.8 m/s of margin against §06's 4.8,
            // spent at the only instant it decides anything.
            var outcome = Resolve(GameConstants.RunnerSprintSpeed);

            Assert.That(outcome, Is.EqualTo(LungeEvent.Missed),
                "§04's sprint has to buy something here, or the margin §06 gives the "
                + "Runner is a number with no moment attached to it.");
        }

        [Test]
        public void A_miss_costs_the_creature_time_it_cannot_chase_in()
        {
            var lunge = new MonsterLunge();
            lunge.Tick(Step, chasing: true, 0.1f);
            Assert.That(lunge.State, Is.EqualTo(LungeState.Committed));

            // Take it out to a miss.
            var elapsed = 0f;
            while (lunge.State == LungeState.Committed && elapsed < 5f)
            {
                lunge.Tick(Step, chasing: true, GameConstants.MonsterAttackRange * 2f);
                elapsed += Step;
            }

            Assert.That(lunge.State, Is.EqualTo(LungeState.Recovering));
            Assert.That(lunge.SpeedNow(GameConstants.MonsterBaseSpeed), Is.EqualTo(0f),
                "A miss has to stop it. Recovery is the only moment in a match when the "
                + "creature is not closing, and it is what the sprint is spent to buy.");

            var recovery = 0f;
            var seen = LungeEvent.None;
            while (recovery < GameConstants.MonsterAttackRecoverySeconds * 3f)
            {
                seen = lunge.Tick(Step, chasing: true, GameConstants.MonsterAttackRange * 2f);
                recovery += Step;
                if (seen == LungeEvent.Recovered)
                {
                    break;
                }
            }

            Assert.That(seen, Is.EqualTo(LungeEvent.Recovered));
            Assert.That(recovery, Is.GreaterThan(GameConstants.MonsterAttackRecoverySeconds - Step * 2f));
        }

        [Test]
        public void Losing_the_target_cancels_a_lunge_already_in_the_air()
        {
            var lunge = new MonsterLunge();
            lunge.Tick(Step, chasing: true, 0.1f);
            Assert.That(lunge.State, Is.EqualTo(LungeState.Committed));

            lunge.Tick(Step, chasing: false, 0.1f);

            Assert.That(lunge.State, Is.EqualTo(LungeState.Ready),
                "§04 섬광수 stuns the creature mid-lunge and §06's other four states are "
                + "not an attack. A strike that landed anyway would be killing people "
                + "through an ability that had already worked.");
        }

        [Test]
        public void It_travels_faster_while_committed_than_it_chases()
        {
            var lunge = new MonsterLunge();
            Assert.That(lunge.SpeedNow(GameConstants.MonsterBaseSpeed),
                Is.EqualTo(GameConstants.MonsterBaseSpeed));

            lunge.Tick(Step, chasing: true, 0.1f);

            Assert.That(lunge.SpeedNow(GameConstants.MonsterBaseSpeed),
                Is.EqualTo(GameConstants.MonsterLungeSpeed));
            Assert.That(GameConstants.MonsterLungeSpeed, Is.GreaterThan(GameConstants.MonsterBaseSpeed),
                "A pounce is not a sprint.");
        }

        /// <summary>
        /// Commits at the range and runs the strike out with the gap closing at the real
        /// speed difference, returning whatever the strike resolved to.
        /// </summary>
        private static LungeEvent Resolve(float playerSpeed)
        {
            var lunge = new MonsterLunge();
            var gap = GameConstants.MonsterAttackRange;
            var closing = GameConstants.MonsterLungeSpeed - playerSpeed;

            var first = lunge.Tick(Step, chasing: true, gap);
            Assert.That(first, Is.EqualTo(LungeEvent.Committed));

            var elapsed = 0f;
            while (elapsed < 5f)
            {
                gap = System.Math.Max(0f, gap - closing * Step);
                var seen = lunge.Tick(Step, chasing: true, gap);
                elapsed += Step;
                if (seen == LungeEvent.Hit || seen == LungeEvent.Missed)
                {
                    return seen;
                }
            }

            return LungeEvent.None;
        }
    }
}
