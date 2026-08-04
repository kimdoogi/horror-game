#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Race;
using HorrorGame.Gameplay.Race;
using HorrorGame.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Racing
{
    /// <summary>
    /// §02 — 승패 조건 — as the scene actually runs it.
    /// <para>
    /// <c>RaceTests</c> in the core project already proves the rule: places, monotonic
    /// descent, unranked elimination. None of that is repeated here. What is tested is the
    /// half that touches a world — the radius that decides an arrival, the fact that the
    /// tower puts the middle of B1 directly above the finish, and above all §02's 완주:
    /// that the race does not end when the winner arrives.
    /// </para>
    /// <para>
    /// <b>Why the §02 assertions look paranoid.</b> The whole design argument for recording
    /// a place for everybody is that without it "선두를 놓친 사람에게" has five storeys of
    /// nothing to play for. The way that gets broken is one plausible line — ending the
    /// match on the first finish — so there are three separate tests pointed at it.
    /// </para>
    /// <para>
    /// Lives in the predefined assembly because <c>RaceDirector</c> does, and an
    /// <c>.asmdef</c> cannot reference one. Compiles out of a player build on
    /// <c>UNITY_INCLUDE_TESTS</c>.
    /// </para>
    /// </summary>
    public sealed class RaceDirectorTests
    {
        /// <summary>Where the tests put the middle of B8. Arbitrary and off the origin, so a bug that returns Vector3.zero fails.</summary>
        private static readonly Vector3 Finish = new Vector3(30f, -26.25f, 30f);

        /// <summary>The deepest storey index. <see cref="RaceState.Storeys"/> − 1 — B8.</summary>
        private const int Bottom = RaceState.Storeys - 1;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private RaceDirector _race = null!;

        [SetUp]
        public void SetUp()
        {
            var host = new GameObject("RaceDirector");
            _spawned.Add(host);
            _race = host.AddComponent<RaceDirector>();

            // Bound before Begin so the scene lookup never runs: these tests have no
            // generated building, and a director that shouted about a missing finish would
            // be shouting correctly.
            _race.BindFinish(Finish);
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        // ------------------------------------------------------------------
        // §02 완주 — the thing the whole component exists to protect.
        // ------------------------------------------------------------------

        [Test]
        public void WinnerArriving_DoesNotEndTheRace()
        {
            var bodies = StartRace(3);
            Descend(3);

            var closed = 0;
            _race.Closed += _ => closed++;

            Stand(bodies[1], Finish);
            _race.Tick(10f);

            Assert.That(_race.WinnerId, Is.EqualTo(1), "§02: the first to touch the middle of B8 wins.");
            Assert.That(_race.Over, Is.False,
                "§02 완주: two runners are still descending and the places below first are theirs to take. "
                + "A race that ends on the winner's arrival deletes the last three storeys for everybody else.");
            Assert.That(closed, Is.Zero, "Closed must fire on RaceState.Over and on nothing else.");
            Assert.That(_race.StillRunning, Is.EqualTo(2));
        }

        [Test]
        public void EverySubsequentFinisher_GetsAPlace()
        {
            var bodies = StartRace(3);
            Descend(3);

            var places = new List<int>();
            _race.Finished += (id, place) => places.Add(place);

            Stand(bodies[0], Finish);
            _race.Tick(10f);

            Stand(bodies[0], Far());
            Stand(bodies[2], Finish);
            _race.Tick(20f);

            Stand(bodies[2], Far());
            Stand(bodies[1], Finish);
            _race.Tick(30f);

            Assert.That(places, Is.EqualTo(new[] { 1, 2, 3 }),
                "§02: 완주 순위가 남는다 — Finished fires for every finisher, not once for the winner.");

            var results = _race.Results;
            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].Id, Is.EqualTo(0));
            Assert.That(results[1].Id, Is.EqualTo(2), "Second place belongs to whoever arrived second, and it is worth having.");
            Assert.That(results[2].Id, Is.EqualTo(1));
            Assert.That(results[1].ElapsedSeconds, Is.EqualTo(20f).Within(0.001f), "A place carries the time it was set at.");
        }

        [Test]
        public void RaceCloses_OnlyWhenNobodyIsStillRunning()
        {
            var bodies = StartRace(3);
            Descend(3);

            var closedWith = int.MinValue;
            _race.Closed += winner => closedWith = winner;

            Stand(bodies[2], Finish);
            _race.Tick(10f);
            Assert.That(_race.Over, Is.False, "One home, two running.");

            // Withdraw, not ReportCaught: being caught sends a runner back to B1 and leaves
            // them Running, so it can never close a race. An emptied seat is the only thing
            // in the game that takes somebody out of the field, which is exactly what this
            // test needs and exactly why Withdraw still exists.
            _race.Withdraw(0, 12f);
            _race.Tick(12f);
            Assert.That(_race.Over, Is.False, "One home, one seat empty, one still running — the last place is still live.");

            Stand(bodies[1], Finish);
            _race.Tick(15f);

            Assert.That(_race.Over, Is.True);
            Assert.That(closedWith, Is.EqualTo(2), "Closed carries the winner's seat.");
            Assert.That(_race.Finishers, Is.EqualTo(2));
        }

        // ------------------------------------------------------------------
        // The finish, as geometry.
        // ------------------------------------------------------------------

        [Test]
        public void ArrivalIsARadius_AndTheChamberWallIsOutsideIt()
        {
            var bodies = StartRace(2);
            Descend(2);

            // 3.0 m out: still inside Chamber_Open_3x3 (3.75 m to its wall) and outside the
            // 2.5 m circle. Being in the room is not arriving.
            Stand(bodies[0], Finish + new Vector3(3.0f, 0f, 0f));
            _race.Tick(5f);
            Assert.That(_race.Finishers, Is.Zero, "3.0 m from the middle is inside the chamber and outside the finish.");

            // 2.4 m: inside.
            Stand(bodies[0], Finish + new Vector3(2.4f, 0f, 0f));
            _race.Tick(6f);
            Assert.That(_race.Finishers, Is.EqualTo(1), "2.4 m is inside the " + RaceDirector.FinishRadiusMetres + " m circle.");
        }

        [Test]
        public void FinishRadius_CannotBeCrossedBetweenTwoTicks()
        {
            // §05's sprint is the fastest anything moves; §13 samples at FixedStep. If a
            // runner could cover the diameter in one step the check would be a coin toss.
            var perTick = GameConstants.RunnerSprintSpeed * GameConstants.FixedStep;

            Assert.That(perTick, Is.LessThan(RaceDirector.FinishRadiusMetres),
                "A runner must not be able to pass through the finish between two samples.");
            Assert.That(RaceDirector.FinishRadiusMetres / perTick, Is.GreaterThan(10f),
                "And not marginally: the circle should be many ticks wide at 5.6 m/s.");
        }

        [Test]
        public void TheTowerIsOneColumn_SoTheMiddleOfB1IsNotTheFinish()
        {
            // DescentMap stacks every storey on the same square, so the middle of B1 sits
            // directly above the middle of B8 in plan. The horizontal test alone would call
            // a runner queuing for B1's chute a winner.
            var bodies = StartRace(2);

            Stand(bodies[0], new Vector3(Finish.x, 0f, Finish.z));
            _race.Tick(3f);

            Assert.That(_race.Finishers, Is.Zero,
                "§02: the finish is the middle of B8, not the middle of the tower. The storey is the vertical test.");
            Assert.That(_race.WinnerId, Is.EqualTo(-1));

            for (var storey = 1; storey <= Bottom; storey++)
            {
                _race.ReportDescent(0, storey, 3f + storey);
            }

            _race.Tick(20f);
            Assert.That(_race.Finishers, Is.EqualTo(1), "Same position, eight storeys down: that is the finish.");
        }

        [Test]
        public void SameTickArrivals_AreOrderedByHowFarPastTheLine()
        {
            var bodies = StartRace(2);
            Descend(2);

            Stand(bodies[0], Finish + new Vector3(2.0f, 0f, 0f));
            Stand(bodies[1], Finish + new Vector3(0.3f, 0f, 0f));
            _race.Tick(10f);

            Assert.That(_race.WinnerId, Is.EqualTo(1),
                "Both were inside on the same tick; the one deeper in crossed the line earlier within the step.");
            Assert.That(_race.Rules![0].Place, Is.EqualTo(2), "The other still gets a place — §02 완주.");
        }

        [Test]
        public void FinishIsFoundFromTheGeneratorsOwnMark_WithoutBeingTold()
        {
            // Every other test binds the finish by hand. This one reproduces what the scene
            // generator actually leaves behind and makes the director go and find it, because
            // that is the path the shipped game takes and BindFinish hides it.
            //
            // The names and numbers are measured, not invented: DescentMap at its default
            // seed marks node "B8_도착점" at (31.25, -26.25, 31.25) and MapSketch turns it
            // into one marker, "EntranceLight_B8 굴착층_910". MapSceneBuilder then hangs the
            // fitting CorridorClearWidth + 0.6 = 2.8 m above the floor.
            var map = new GameObject("Map");
            _spawned.Add(map);

            var markers = new GameObject("Markers");
            markers.transform.SetParent(map.transform, worldPositionStays: false);

            var lamp = new GameObject("EntranceLight_B8 굴착층_910");
            lamp.transform.SetParent(markers.transform, worldPositionStays: false);
            lamp.transform.position = new Vector3(31.25f, -26.25f + 2.8f, 31.25f);
            lamp.AddComponent<Light>();

            // A zone light in the same scene, to prove the prefix is doing real work.
            var other = new GameObject("ZoneLight_B1 하역장_4");
            other.transform.SetParent(markers.transform, worldPositionStays: false);
            other.transform.position = new Vector3(5f, 0f, 5f);
            other.AddComponent<Light>();

            var host = new GameObject("Finder");
            _spawned.Add(host);
            var finder = host.AddComponent<RaceDirector>();

            Assert.That(finder.Begin(2), Is.True);

            Assert.That(finder.FinishFound, Is.True, "Without this, nobody can win the match.");
            Assert.That(finder.Finish.x, Is.EqualTo(31.25f).Within(0.001f), "The middle of B8 in plan, to the centimetre.");
            Assert.That(finder.Finish.z, Is.EqualTo(31.25f).Within(0.001f));

            var body = new GameObject("Runner");
            _spawned.Add(body);
            finder.Track(0, body.transform);
            for (var storey = 1; storey <= Bottom; storey++)
            {
                finder.ReportDescent(0, storey, storey);
            }

            // Standing on the floor of the finish chamber, 2.8 m under the light that located
            // it. The height difference must not matter — the arrival test is horizontal.
            body.transform.position = new Vector3(31.25f, -26.25f, 31.25f);
            finder.Tick(30f);

            Assert.That(finder.WinnerId, Is.EqualTo(0),
                "The runner is on the floor and the mark is on the ceiling. Arriving is an X/Z question.");
        }

        // ------------------------------------------------------------------
        // §02 탈락 and 시간 초과.
        // ------------------------------------------------------------------

        [Test]
        public void CaughtSendsARunnerBackToB1_AndTheyAreStillRacing()
        {
            var bodies = StartRace(2);
            Descend(2);

            Assert.That(_race.Rules![0].Storey, Is.EqualTo(RaceState.Storeys - 1),
                "Descend() takes the field all the way down, so this runner is on B8 — one "
                + "cell from winning, which is the most expensive moment to be caught in.");

            var retiredWith = RaceExit.Racing;
            _race.Retired += (id, why) => retiredWith = why;

            var caughtOn = -1;
            _race.Caught += (id, storey) => caughtOn = storey;

            _race.ReportCaught(0, 8f);

            Assert.That(caughtOn, Is.EqualTo(RaceState.Storeys - 1),
                "the event carries the storey they lost — all eight of them.");
            Assert.That(retiredWith, Is.EqualTo(RaceExit.Racing),
                "Retired must NOT fire. A caught runner is still in the race, and anything "
                + "listening for Retired to take somebody off a board would erase a live player.");
            Assert.That(_race.ExitOf(0), Is.EqualTo(RaceExit.Racing));
            Assert.That(_race.Rules[0].Status, Is.EqualTo(RacerStatus.Running));
            Assert.That(_race.Rules[0].Storey, Is.Zero, "back to B1 — a 투하구 is one-way.");
            Assert.That(_race.Rules[0].TimesCaught, Is.EqualTo(1));
        }

        [Test]
        public void ARunnerWhoWasCaughtCanStillWin()
        {
            var bodies = StartRace(2);
            Descend(2);
            _race.ReportCaught(0, 8f);

            Assert.That(_race.Rules![0].Storey, Is.Zero, "back on B1 with everything to redo.");

            // The whole descent again, which is what being caught actually costs.
            for (var storey = 1; storey < RaceState.Storeys; storey++)
            {
                _race.ReportDescent(0, storey, 10f + storey);
            }

            Stand(bodies[0], Finish);
            _race.Tick(30f);

            Assert.That(_race.Finishers, Is.EqualTo(1));
            Assert.That(_race.Rules[0].Place, Is.EqualTo(1),
                "§02 ranks arrival, not a clean run. Being caught costs eight storeys and "
                + "nothing else — a runner who pays that and still gets there first has won.");
            Assert.That(_race.Rules[0].TimesCaught, Is.EqualTo(1),
                "and the standings still say what it cost them.");
        }

        [Test]
        public void Timeout_LosesItForEverybody_OnlyWhenNobodyArrived()
        {
            StartRace(2);
            Descend(2);
            _race.TimeoutSeconds = 100f;

            var closedWith = int.MinValue;
            _race.Closed += winner => closedWith = winner;

            _race.Tick(99f);
            Assert.That(_race.Over, Is.False);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("시간 초과"));
            _race.Tick(100f);

            Assert.That(_race.Over, Is.True);
            Assert.That(closedWith, Is.EqualTo(-1), "§02 시간 초과 — 전원 패배. Nobody won.");
            Assert.That(_race.ExitOf(0), Is.EqualTo(RaceExit.TimedOut));
            Assert.That(_race.ExitOf(1), Is.EqualTo(RaceExit.TimedOut));
        }

        [Test]
        public void Timeout_DoesNotFireOnceSomebodyIsHome()
        {
            var bodies = StartRace(2);
            Descend(2);
            _race.TimeoutSeconds = 100f;

            Stand(bodies[0], Finish);
            _race.Tick(10f);
            Stand(bodies[0], Far());

            _race.Tick(500f);

            Assert.That(_race.Over, Is.False,
                "§02 conditions 시간 초과 on 아무도 도착하지 않고. With a finisher on the board the remaining "
                + "places are still live, and §07 is what closes the race — 누군가는 위험을 감수해야 판이 끝난다.");
            Assert.That(_race.ExitOf(1), Is.EqualTo(RaceExit.Racing));
        }

        // ------------------------------------------------------------------
        // The field.
        // ------------------------------------------------------------------

        [Test]
        public void FieldIsSizedFromTheRealCount_NotTheCap()
        {
            _race.Begin(4);

            Assert.That(_race.Rules!.Count, Is.EqualTo(4),
                "A twenty-seat race with four people in it never satisfies RaceState.Over: sixteen empty seats "
                + "stay Running and the match cannot end.");
        }

        [Test]
        public void OneRunnerIsNotARace_AndTheDirectorSaysSoRatherThanStarting()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("§11"));

            Assert.That(_race.Begin(1), Is.False);
            Assert.That(_race.Started, Is.False, "Refused, not clamped — a race resized behind the caller's back reports places for people who are not in it.");
        }

        [Test]
        public void AnEmptySeatCanBeWithdrawn_SoTheRaceCanStillEnd()
        {
            var bodies = StartRace(GameConstants.RaceRunnersMin);
            Descend(GameConstants.RaceRunnersMin);

            // §11's minimum is two, so rehearsing a descent alone means one seat nobody is
            // sitting in. Left Running it would hold the match open forever.
            _race.Withdraw(1, 0f);
            Assert.That(_race.ExitOf(1), Is.EqualTo(RaceExit.Withdrawn));
            Assert.That(_race.Over, Is.False, "Withdrawing does not by itself close a race that still has a runner.");

            Stand(bodies[0], Finish);
            _race.Tick(12f);

            Assert.That(_race.Over, Is.True);
            Assert.That(_race.WinnerId, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // The HUD's view.
        // ------------------------------------------------------------------

        [Test]
        public void Board_PutsFinishersFirst_ThenTheDeepest_ThenTheUnranked()
        {
            var bodies = StartRace(4);
            Descend(4);

            Stand(bodies[3], Finish);
            _race.Tick(10f);
            Stand(bodies[3], Far());

            _race.Withdraw(0, 11f);

            // One home, two still running, one seat empty. The three bands are what is
            // being asserted; the order inside the middle band is RaceState.Standings'
            // business and is tested next door. It is a withdrawal rather than a catch
            // because a caught runner stays in the middle band — there would be no third
            // band to put on the bottom.
            Assert.That(_race.Rules![0].Status, Is.EqualTo(RacerStatus.Eliminated));
            var board = _race.Board();

            Assert.That(board.Count, Is.EqualTo(4), "Every seat appears exactly once.");
            Assert.That(board[0].Id, Is.EqualTo(3));
            Assert.That(board[0].Status, Is.EqualTo(RacerStatus.Finished),
                "§02: putting the finishers on top is the screen saying that arriving is worth something on its own.");
            Assert.That(board[3].Id, Is.EqualTo(0));
            Assert.That(board[3].Status, Is.EqualTo(RacerStatus.Eliminated), "An emptied seat is last.");
            Assert.That(board[1].Status, Is.EqualTo(RacerStatus.Running));
            Assert.That(board[2].Status, Is.EqualTo(RacerStatus.Running));
        }

        [Test]
        public void TheReadoutIsAWindow_AndCarriesEverythingRaceHudNeeds()
        {
            // RaceHud names IRaceReadout and never this class. If a member of it stops being
            // answerable the HUD compiles and draws nonsense, so the contract is asserted
            // here rather than discovered on screen.
            StartRace(3);
            _race.LocalRacerId = 1;
            _race.SetName(1, "두기");

            IRaceReadout readout = _race;

            Assert.That(readout.RunnerCount, Is.EqualTo(3));
            Assert.That(readout.NameOf(1), Is.EqualTo("두기"));
            Assert.That(readout.NameOf(2), Is.EqualTo("3번"), "§05: a seat number where no name is known.");
            Assert.That(readout.LocalRacer.Id, Is.EqualTo(1));
            Assert.That(readout.LocalRacer.Storey, Is.Zero, "Everybody starts on the rim of B1.");

            _race.ReportDescent(1, 3, 40f);
            _race.Tick(41f);

            Assert.That(readout.LocalRacer.Storey, Is.EqualTo(3), "§09's announcements arrive as a change to this struct.");
            Assert.That(readout.ElapsedSeconds, Is.EqualTo(41f).Within(0.001f), "The race clock, not §07's 시각.");
            Assert.That(readout.Over, Is.False);
            Assert.That(readout.Standings.Count, Is.EqualTo(3));
        }

        [Test]
        public void Standings_AreDeepestFirst()
        {
            var bodies = StartRace(3);

            _race.ReportDescent(0, 1, 5f);
            _race.ReportDescent(1, 4, 6f);
            _race.ReportDescent(2, 2, 7f);

            var standings = _race.Standings;

            Assert.That(standings[0].Id, Is.EqualTo(1), "B5 is deeper than B3 is deeper than B2.");
            Assert.That(standings[1].Id, Is.EqualTo(2));
            Assert.That(standings[2].Id, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // Helpers.
        // ------------------------------------------------------------------

        /// <summary>Starts a race of <paramref name="runners"/> and gives every seat a body on the rim of B1.</summary>
        private Transform[] StartRace(int runners)
        {
            Assert.That(_race.Begin(runners), Is.True);

            var bodies = new Transform[runners];
            for (var i = 0; i < runners; i++)
            {
                var go = new GameObject("Runner_" + i);
                _spawned.Add(go);
                go.transform.position = Far();
                bodies[i] = go.transform;
                _race.Track(i, go.transform);
            }

            return bodies;
        }

        /// <summary>Drops every seat to B8, because most of these tests are about what happens there.</summary>
        private void Descend(int runners)
        {
            for (var id = 0; id < runners; id++)
            {
                for (var storey = 1; storey <= Bottom; storey++)
                {
                    _race.ReportDescent(id, storey, storey);
                }
            }
        }

        private static void Stand(Transform body, Vector3 at)
        {
            body.position = at;
        }

        /// <summary>Somewhere that is emphatically not the finish.</summary>
        private static Vector3 Far()
        {
            return Finish + new Vector3(40f, 0f, 40f);
        }
    }
}
#endif
