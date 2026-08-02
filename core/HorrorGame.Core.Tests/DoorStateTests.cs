using HorrorGame.Core;
using HorrorGame.Core.Map;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §12's doors, and the one thing they are for: buying a player time §06 does not
    /// otherwise let them have.
    /// </summary>
    public sealed class DoorStateTests
    {
        private const float Step = 1f / 60f;

        [Test]
        public void A_door_starts_open_and_lets_everything_through()
        {
            var door = new DoorState();

            Assert.That(door.Phase, Is.EqualTo(DoorPhase.Open));
            Assert.That(door.Blocks, Is.False);
            Assert.That(door.CanBeUsed, Is.True);
        }

        [Test]
        public void Shutting_one_takes_the_time_it_says_and_then_blocks()
        {
            var door = new DoorState();
            var elapsed = 0f;

            while (!door.Shut(Step) && elapsed < 5f)
            {
                elapsed += Step;
                Assert.That(door.Blocks, Is.False, "It does not block until it is actually shut.");
            }

            Assert.That(door.Phase, Is.EqualTo(DoorPhase.Shut));
            Assert.That(door.Blocks, Is.True);
            Assert.That(elapsed, Is.EqualTo(GameConstants.DoorShutSeconds).Within(Step * 2f));
        }

        [Test]
        public void Letting_go_early_loses_the_effort()
        {
            var door = new DoorState();
            door.Shut(GameConstants.DoorShutSeconds * 0.9f);
            Assert.That(door.ShutProgress01, Is.GreaterThan(0.8f));

            door.AbandonShut();

            Assert.That(door.ShutProgress01, Is.EqualTo(0f),
                "Shutting a door is a commitment. A player who can tap it, run, and come "
                + "back to a nearly-shut door pays none of the cost §06 charges for it.");
            Assert.That(door.Phase, Is.EqualTo(DoorPhase.Open));
        }

        [Test]
        public void The_creature_needs_longer_to_break_it_than_a_release_needs()
        {
            var door = new DoorState();
            door.Shut(GameConstants.DoorShutSeconds);

            var elapsed = 0f;
            while (!door.Break(Step) && elapsed < 30f)
            {
                elapsed += Step;
            }

            Assert.That(door.Phase, Is.EqualTo(DoorPhase.Broken));
            Assert.That(elapsed, Is.GreaterThan(GameConstants.AggroReleaseLineOfSightBreak),
                "§06 releases a chase after 3 s of broken sight. A door that came down "
                + "faster than that would be a beat of standing still that buys nothing.");
        }

        [Test]
        public void A_broken_door_stays_broken()
        {
            var door = new DoorState();
            door.Shut(GameConstants.DoorShutSeconds);
            door.Break(GameConstants.DoorBreakSeconds);
            Assert.That(door.Phase, Is.EqualTo(DoorPhase.Broken));

            Assert.That(door.Shut(GameConstants.DoorShutSeconds * 3f), Is.False);
            Assert.That(door.CanBeUsed, Is.False);
            Assert.That(door.Blocks, Is.False,
                "§07's escalation as geometry: every door the creature has come through is "
                + "a route that no longer costs it anything, so the same corridor is a "
                + "different problem at 심야 than it was at 초저녁.");
        }

        [Test]
        public void Driving_it_off_recovers_the_door_slowly_and_not_completely()
        {
            var door = new DoorState();
            door.Shut(GameConstants.DoorShutSeconds);
            door.Break(GameConstants.DoorBreakSeconds * 0.6f);
            var weakened = door.BreakProgress01;
            Assert.That(weakened, Is.GreaterThan(0.5f));

            // §04's flash drives it off for the same span it had already spent.
            door.Relax(GameConstants.DoorBreakSeconds * 0.6f);

            Assert.That(door.BreakProgress01, Is.GreaterThan(0f),
                "A flash that handed back a whole door would make the building the same "
                + "at 동트기 전 as at 초저녁.");
            Assert.That(door.BreakProgress01, Is.LessThan(weakened));
        }

        [Test]
        public void Opening_one_yourself_is_instant()
        {
            var door = new DoorState();
            door.Shut(GameConstants.DoorShutSeconds);

            Assert.That(door.Open(), Is.True);
            Assert.That(door.Blocks, Is.False,
                "Nobody has ever struggled to open a door away from themselves, and a "
                + "player who shut themselves in has to be able to get out at the speed "
                + "the thing behind them is arriving.");
        }
    }
}
