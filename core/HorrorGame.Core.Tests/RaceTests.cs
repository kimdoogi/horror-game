using System.Linq;
using HorrorGame.Core;
using HorrorGame.Core.Race;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §02, which is now the whole of what the game is about: eight storeys, one winner.
    /// </summary>
    public sealed class RaceTests
    {
        private static RaceState Started(int runners = 20)
        {
            var race = new RaceState(runners);
            for (var id = 0; id < runners; id++)
            {
                for (var storey = 1; storey < RaceState.Storeys; storey++)
                {
                    race.ReportDescent(id, storey, storey * 60f);
                }
            }

            return race;
        }

        [Test]
        public void The_first_to_the_bottom_wins()
        {
            var race = Started();

            Assert.That(race.ReportFinish(7, 480f), Is.EqualTo(1));
            Assert.That(race.ReportFinish(3, 492f), Is.EqualTo(2));
            Assert.That(race.ReportFinish(11, 501f), Is.EqualTo(3));

            Assert.That(race.WinnerId, Is.EqualTo(7));
            Assert.That(race.Results().Select(r => r.Id), Is.EqualTo(new[] { 7, 3, 11 }));
        }

        [Test]
        public void Finishing_second_is_still_worth_something()
        {
            var race = Started();
            race.ReportFinish(1, 400f);
            var second = race.ReportFinish(2, 410f);

            Assert.That(second, Is.EqualTo(2),
                "§02 records a place for everybody who reaches the bottom. Without that, second "
                + "is worth exactly what last is, and a player who loses the lead on B3 has five "
                + "storeys of nothing to play for — which is five storeys of the map doing nothing.");
        }

        [Test]
        public void You_cannot_finish_a_race_you_have_not_run()
        {
            var race = new RaceState(4);
            race.ReportDescent(0, 3, 100f);

            Assert.That(race.ReportFinish(0, 101f), Is.EqualTo(0),
                "Accepting a finish from somebody standing on B4 would make eight storeys "
                + "skippable with one packet, and this is the single most attractive call in "
                + "the build to lie about.");
            Assert.That(race.WinnerId, Is.EqualTo(-1));
        }

        [Test]
        public void A_chute_only_goes_down()
        {
            var race = new RaceState(4);
            Assert.That(race.ReportDescent(0, 4, 100f), Is.True);
            Assert.That(race.ReportDescent(0, 2, 110f), Is.False,
                "§12's chutes are one way. On the host the only thing that can report a runner "
                + "getting shallower is a client that should not be believed.");
            Assert.That(race[0].Storey, Is.EqualTo(4));
        }

        [Test]
        public void Being_caught_takes_you_out_and_does_not_rank_you()
        {
            var race = Started(6);
            Assert.That(race.ReportEliminated(4, 300f), Is.True);

            Assert.That(race[4].Status, Is.EqualTo(RacerStatus.Eliminated));
            Assert.That(race[4].Place, Is.EqualTo(0),
                "§01 makes the creature the reason to be afraid, not a scoring element. Ranking "
                + "the dead by how deep they got would pay players for dying in the right order.");
            Assert.That(race.ReportFinish(4, 310f), Is.EqualTo(0), "and they cannot come back");
        }

        [Test]
        public void The_race_ends_when_nobody_is_still_running()
        {
            var race = Started(3);
            Assert.That(race.Over, Is.False);

            race.ReportFinish(0, 400f);
            race.ReportEliminated(1, 410f);
            Assert.That(race.Over, Is.False);

            race.ReportFinish(2, 430f);
            Assert.That(race.Over, Is.True);
        }

        [Test]
        public void Standings_put_the_deepest_runner_first()
        {
            var race = new RaceState(4);
            race.ReportDescent(0, 2, 60f);
            race.ReportDescent(1, 5, 90f);
            race.ReportDescent(2, 5, 80f);
            race.ReportDescent(3, 1, 30f);

            var order = race.Standings().Select(r => r.Id).ToArray();

            Assert.That(order[0], Is.EqualTo(2), "same storey, got there sooner");
            Assert.That(order[1], Is.EqualTo(1));
            Assert.That(order, Is.EqualTo(new[] { 2, 1, 0, 3 }));
        }

        [Test]
        public void Being_caught_sends_a_runner_back_to_B1_and_leaves_them_running()
        {
            var race = new RaceState(4);

            for (var storey = 1; storey <= 5; storey++)
            {
                race.ReportDescent(0, storey, storey * 30f);
            }

            Assert.That(race[0].Storey, Is.EqualTo(5), "five 투하구 is B6.");

            Assert.That(race.ReportCaught(0, 200f), Is.True);

            Assert.That(race[0].Status, Is.EqualTo(RacerStatus.Running),
                "§06's creature is a hazard, not an executioner — being caught costs the "
                + "descent, not the game.");
            Assert.That(race[0].Storey, Is.Zero,
                "back to B1. A 투하구 is one-way, so any other storey is a place the map has "
                + "no route into.");
            Assert.That(race[0].TimesCaught, Is.EqualTo(1));
            Assert.That(race.Over, Is.False, "nobody left the race, so nothing about it ended.");
        }

        [Test]
        public void A_runner_sent_back_can_descend_the_whole_way_again()
        {
            var race = new RaceState(2);

            for (var storey = 1; storey <= 7; storey++)
            {
                race.ReportDescent(0, storey, storey * 10f);
            }

            race.ReportCaught(0, 100f);

            // The point of resetting the storey rather than nudging it: ReportDescent is
            // monotonic, so a record still reading B8 would refuse every drop on the way
            // back down and the runner would be stuck on B1 for the rest of the match with
            // no error anywhere.
            for (var storey = 1; storey <= 7; storey++)
            {
                Assert.That(race.ReportDescent(0, storey, 100f + (storey * 10f)), Is.True,
                    "B" + (storey + 1) + " refused the second time down.");
            }

            Assert.That(race.ReportFinish(0, 200f), Is.EqualTo(1),
                "a runner who was caught and ran it again is still allowed to win.");
            Assert.That(race[0].TimesCaught, Is.EqualTo(1), "the count survives the finish.");
        }

        [Test]
        public void Being_caught_does_nothing_to_a_runner_who_has_already_finished()
        {
            var race = new RaceState(2);

            for (var storey = 1; storey <= 7; storey++)
            {
                race.ReportDescent(0, storey, storey * 10f);
            }

            race.ReportFinish(0, 80f);

            Assert.That(race.ReportCaught(0, 90f), Is.False,
                "the creature reaching a finisher would take a place off the board.");
            Assert.That(race[0].Status, Is.EqualTo(RacerStatus.Finished));
            Assert.That(race[0].Place, Is.EqualTo(1));
            Assert.That(race[0].Storey, Is.EqualTo(RaceState.Storeys - 1));
        }

        [Test]
        public void Nothing_in_the_game_eliminates_a_runner_except_an_empty_seat()
        {
            var race = new RaceState(2);

            race.ReportCaught(0, 10f);
            race.ReportCaught(0, 20f);
            race.ReportCaught(0, 30f);

            Assert.That(race[0].Status, Is.EqualTo(RacerStatus.Running),
                "caught three times and still in it.");
            Assert.That(race[0].TimesCaught, Is.EqualTo(3));

            // The one thing that still ends a runner's race without them finishing it.
            Assert.That(race.ReportEliminated(1, 40f), Is.True);
            Assert.That(race[1].Status, Is.EqualTo(RacerStatus.Eliminated));
        }

        [Test]
        public void The_field_is_bounded_by_the_map_rather_than_by_the_network()
        {
            Assert.That(() => new RaceState(1), Throws.ArgumentException.Or.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => new RaceState(GameConstants.RaceRunnersMax + 1),
                Throws.ArgumentException.Or.TypeOf<System.ArgumentOutOfRangeException>(),
                "§12-A fixes the gate counts at 4 · 2 · 1 and refuses to scale them with the "
                + "field. Past twenty the inner cell stops being a decision and becomes a line.");
            Assert.That(() => new RaceState(GameConstants.RaceRunnersMax), Throws.Nothing);
        }
    }
}
