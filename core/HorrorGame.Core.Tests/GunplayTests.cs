using HorrorGame.Core;
using HorrorGame.Core.Race;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// The gun a runner finds on the floor: one shot, as far as their own light reaches,
    /// and being hit costs exactly what being caught costs.
    /// </summary>
    public sealed class GunplayTests
    {
        private static RaceState Racing(int runners = 4)
        {
            return new RaceState(runners);
        }

        [Test]
        public void A_shot_sends_the_target_back_to_their_start_and_leaves_them_racing()
        {
            var race = Racing();
            for (var storey = 1; storey <= 4; storey++)
            {
                race.ReportDescent(1, storey, storey * 10f);
            }

            Assert.That(race[1].Storey, Is.EqualTo(4));

            var shot = Gunplay.Fire(race, 0, 1, Gunplay.ShotsPerGun, 5f, 60f);

            Assert.That(shot, Is.EqualTo(ShotRefusal.None));
            Assert.That(race[1].Storey, Is.Zero, "back to B1, the same place the creature sends you.");
            Assert.That(race[1].Status, Is.EqualTo(RacerStatus.Running),
                "shot is a setback, not an elimination — there is only one price in this game.");
            Assert.That(race[1].TimesCaught, Is.EqualTo(1),
                "and the standings count it the same way, because it costs the same thing.");
        }

        [Test]
        public void The_range_is_the_flashlight_so_you_cannot_shoot_what_you_cannot_see()
        {
            var race = Racing();

            Assert.That(Gunplay.RangeMetres, Is.EqualTo(GameConstants.FlashlightRange),
                "the dark is the balance. A gun that outranges the beam kills things you "
                + "only heard, which is a coin flip and not a decision.");

            Assert.That(
                Gunplay.Judge(race, 0, 1, 1, Gunplay.RangeMetres - 0.01f),
                Is.EqualTo(ShotRefusal.None));

            Assert.That(
                Gunplay.Judge(race, 0, 1, 1, Gunplay.RangeMetres + 0.01f),
                Is.EqualTo(ShotRefusal.TooFar));

            Assert.That(Gunplay.RangeMetres, Is.LessThan(GameConstants.MonsterSightRange),
                "§06 notices you from further than you can shoot, so drawing on somebody in "
                + "the open is a decision that can be overheard.");
        }

        [Test]
        public void One_gun_is_one_shot_and_a_spent_gun_does_nothing()
        {
            var race = Racing();

            Assert.That(Gunplay.ShotsPerGun, Is.EqualTo(1));

            Assert.That(Gunplay.Fire(race, 0, 1, Gunplay.ShotsPerGun, 5f, 10f),
                Is.EqualTo(ShotRefusal.None));

            // The caller spends the shot; the rule refuses the next one on the count it is
            // handed. Twenty players with repeating fire is a shooting gallery, not a race.
            Assert.That(Gunplay.Fire(race, 0, 2, 0, 5f, 11f), Is.EqualTo(ShotRefusal.NoGun));
            Assert.That(race[2].Storey, Is.Zero, "and nothing happened to them.");
            Assert.That(race[2].TimesCaught, Is.Zero);
        }

        [Test]
        public void You_cannot_shoot_yourself_or_somebody_who_is_no_longer_racing()
        {
            var race = Racing();

            Assert.That(Gunplay.Judge(race, 0, 0, 1, 0f), Is.EqualTo(ShotRefusal.SelfShot));

            for (var storey = 1; storey < RaceState.Storeys; storey++)
            {
                race.ReportDescent(3, storey, storey * 5f);
            }

            race.ReportFinish(3, 50f);

            Assert.That(Gunplay.Fire(race, 0, 3, 1, 2f, 60f), Is.EqualTo(ShotRefusal.NotRacing),
                "shooting a finisher would take a place off the board after §02 awarded it.");
            Assert.That(race[3].Place, Is.EqualTo(1));
            Assert.That(race[3].Status, Is.EqualTo(RacerStatus.Finished));

            race.ReportEliminated(2, 61f);
            Assert.That(Gunplay.Fire(race, 0, 2, 1, 2f, 62f), Is.EqualTo(ShotRefusal.NotRacing),
                "an empty seat has nowhere to be sent back to.");
        }

        [Test]
        public void A_seat_that_is_not_in_this_race_is_refused_rather_than_thrown()
        {
            var race = Racing(2);

            Assert.That(Gunplay.Judge(race, 0, 5, 1, 1f), Is.EqualTo(ShotRefusal.NotASeat));
            Assert.That(Gunplay.Judge(race, -1, 1, 1, 1f), Is.EqualTo(ShotRefusal.NotASeat));
            Assert.That(Gunplay.Judge(null!, 0, 1, 1, 1f), Is.EqualTo(ShotRefusal.NotASeat),
                "§13 runs this on the host with ids off the wire. A malformed one is a "
                + "refusal, not an exception that takes the match down with it.");
        }

        [Test]
        public void A_runner_who_was_shot_can_run_it_again_and_win()
        {
            var race = Racing(2);
            for (var storey = 1; storey < RaceState.Storeys; storey++)
            {
                race.ReportDescent(1, storey, storey * 10f);
            }

            Gunplay.Fire(race, 0, 1, 1, 3f, 80f);
            Assert.That(race[1].Storey, Is.Zero);

            for (var storey = 1; storey < RaceState.Storeys; storey++)
            {
                Assert.That(race.ReportDescent(1, storey, 100f + storey), Is.True,
                    "B" + (storey + 1) + " refused after a shot — the descent has to be redoable "
                    + "or the gun is an elimination wearing a different name.");
            }

            Assert.That(race.ReportFinish(1, 200f), Is.EqualTo(1));
        }
    }
}
