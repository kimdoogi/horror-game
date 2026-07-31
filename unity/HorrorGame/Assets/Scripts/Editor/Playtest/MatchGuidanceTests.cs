#nullable enable

using System;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Match;
using HorrorGame.Gameplay.Guidance;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.EditorTools.Playtest
{
    /// <summary>
    /// Drives a real match through §01's loop and checks that the line at the bottom of
    /// the screen says the right thing at every point on it — including the points §01's
    /// diagram does not draw.
    /// <para>
    /// <b>Why this test and not a reading of the code.</b> <see cref="MatchGuidance"/>
    /// exists to be right for somebody who has never seen the game, and the case it will
    /// actually be judged on is the confused one: the tester who finds the objective
    /// before reading a mark, who puts it down again, who comes back up with nothing. A
    /// phase table that was only ever walked in its intended order is a phase table whose
    /// out-of-order branches have never executed. So the walk below deliberately goes
    /// sideways, and the assertions are about what a person would be told rather than
    /// about which enum member came out.
    /// </para>
    /// <para>
    /// <b>And it pins §03.</b> After every completed read it asserts the guidance line
    /// does not contain the mark the overlay just drew. §03's whole mechanic is that the
    /// only copy of a clue is in a player's head — a guidance line that echoed the
    /// overlay would be the 단서 로그 the design forbids, and it would be an easy and
    /// invisible thing to add later.
    /// </para>
    /// <para>
    /// Lives in <c>Assembly-CSharp-Editor</c> for the same reason
    /// <c>SoloMatchLoopTests</c> does: it is the only assembly that can see the match,
    /// the player rig behind its asmdef, and <c>EditorSceneManager</c> at once.
    /// </para>
    /// </summary>
    public sealed class MatchGuidanceTests
    {
        /// <summary>
        /// §01's loop, in order: 지상 → 잠입 → 단서 → 전리품 → 귀환 → 판매 · 구매 →
        /// 다시 잠입 → 목표물 → 탈출. Every arc has to name itself.
        /// </summary>
        [Test]
        public void Objective_line_follows_section_01s_loop()
        {
            // Building the scene reimports assets, which re-emits Mirror's package-cache
            // meta complaint (B-002). Same suppression and same justification as
            // SoloMatchLoopTests: nothing below is proved by the absence of a log line.
            LogAssert.ignoreFailingMessages = true;

            try
            {
                var match = Fixture.Build();
                var guidance = match.Guidance;

                // --- 지상, before the first descent --------------------------------
                guidance.Observe();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.Descend),
                    "§01 opens at the van with an empty wallet; the first thing to say is 'go down'. Line: " + guidance.Line);
                Assert.That(guidance.Line, Does.Contain("아래로 내려가세요"));

                // --- 잠입 ---------------------------------------------------------
                match.GoUnderground();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.ReadClues),
                    "underground with nothing read, §03's first job is the marks. Line: " + guidance.Line);
                Assert.That(guidance.Line, Does.Contain("표식을 찾아 손전등을 고정하세요"));
                Assert.That(guidance.Line, Does.Contain("(0/" + GameConstants.CluesRequiredToLocate + ")"),
                    "the count has to start honest: " + guidance.Line);

                // --- §08 전리품, and the live weight it costs ------------------------
                Assert.That(match.TakeSomeLoot(), Is.True, "§08 put no pocketable loot on the map");
                guidance.Observe();
                Assert.That(guidance.Note, Does.Contain("무게"),
                    "§08 makes the load a live decision; it has to be on screen while it is being made");
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.ReadClues),
                    "picking loot up is not a step of §03's chain");

                // --- §03's chain, one mark at a time -------------------------------
                var read = 0;
                foreach (var clue in match.Clues)
                {
                    if (read >= GameConstants.CluesRequiredToLocate)
                    {
                        break;
                    }

                    match.HoldBeamOn(clue.ClueId);
                    guidance.Observe();

                    var mark = match.LastMarkDrawn();
                    if (mark.Length > 0)
                    {
                        // §03: 그 자리에서 보고, 기억해서, 말로 전달해야 한다.
                        Assert.That(guidance.Line, Does.Not.Contain(mark),
                            "§03 forbids a clue log, and the guidance line just became one");
                        Assert.That(guidance.Note, Does.Not.Contain(mark),
                            "§03 forbids a clue log, and the load line just became one");

                        read++;
                        Assert.That(guidance.CluesRead, Is.EqualTo(read),
                            "a legible read has to move the count");
                    }

                    if (read > 0 && read < GameConstants.CluesRequiredToLocate)
                    {
                        Assert.That(guidance.Line,
                            Does.Contain("(" + read + "/" + GameConstants.CluesRequiredToLocate + ")"),
                            "the count has to be current: " + guidance.Line);
                    }

                    // Look away. §03 keeps no record — but what the player already knows
                    // is not un-known by turning their head.
                    match.LookAway();
                    guidance.Observe();
                    Assert.That(guidance.CluesRead, Is.EqualTo(read),
                        "looking away must not roll the progress back");
                }

                Assert.That(read, Is.EqualTo(GameConstants.CluesRequiredToLocate),
                    "this seed's marks never came out legible under an ideal beam, so the chain cannot converge");

                // Reading one of them again is not progress.
                match.HoldBeamOn(match.Clues[0].ClueId);
                guidance.Observe();
                Assert.That(guidance.CluesRead, Is.EqualTo(GameConstants.CluesRequiredToLocate),
                    "re-reading a mark you already have must not count twice");
                match.LookAway();

                guidance.Observe();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.FindObjective),
                    "§03's chain has converged, so the next thing is the objective. Line: " + guidance.Line);
                Assert.That(guidance.Line, Does.Contain("목표물을 찾으세요"));

                // --- 귀환 · 판매 · 상점 -------------------------------------------
                match.GoToTheSurface();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.ShopAndDescendAgain),
                    "§08 sells on arrival, so the van is about the shop and the next trip. Line: " + guidance.Line);
                Assert.That(guidance.Line, Does.Contain("상점"),
                    "the panel that just covered the screen is the thing to explain: " + guidance.Line);
                Assert.That(guidance.Line, Does.Contain("크레딧"),
                    "§08's shared wallet is the number the next decision is made against: " + guidance.Line);
                Assert.That(guidance.LootSold, Is.GreaterThan(0), "§08's loot did not register as sold");
                Assert.That(guidance.Credits, Is.GreaterThan(0), "§08's shared wallet stayed empty");
                Assert.That(guidance.RoundTrips, Is.EqualTo(1), "§03's 왕복 count did not move");
                Assert.That(guidance.Note, Is.Empty, "the pockets are empty at the van; there is no load to report");

                // --- 다시 잠입 — the chain is remembered ---------------------------
                match.GoUnderground();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.FindObjective),
                    "going back down must not reset §03's progress. Line: " + guidance.Line);

                // --- 목표물 운반 ---------------------------------------------------
                Assert.That(match.TakeTheObjective(), Is.True, "§03's objective refused a player with free hands");
                guidance.Observe();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.CarryObjectiveOut));
                Assert.That(guidance.Line, Does.Contain("출입구로 운반하세요"));
                Assert.That(guidance.Line, Does.Contain("손전등"),
                    "§03's carry rule is that both hands are used; that is the thing a tester needs told");

                // --- §02 -----------------------------------------------------------
                match.GoToTheSurface();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.MatchOver));
                Assert.That(guidance.Resolution.Outcome, Is.EqualTo(MatchOutcome.FullVictory),
                    "§02 expected 완전 승리 after carrying the objective out");
                Assert.That(guidance.CluesRead, Is.EqualTo(GameConstants.CluesRequiredToLocate),
                    "the end summary has to report the marks that were actually read");
                Assert.That(guidance.Deaths, Is.Zero);
                Assert.That(guidance.ElapsedSeconds, Is.GreaterThan(0f), "§07's clock never ran");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>
        /// The same guidance, driven by somebody who does not know the loop. Every branch
        /// here is reachable in a real session and none of them is the order §01 draws.
        /// </summary>
        [Test]
        public void Objective_line_is_right_when_the_tester_acts_out_of_order()
        {
            LogAssert.ignoreFailingMessages = true;

            try
            {
                var match = Fixture.Build();
                var guidance = match.Guidance;

                // Straight to the objective, having read nothing at all. The hands beat
                // the chain: telling this player to go and find marks would be wrong.
                match.GoUnderground();
                Assert.That(match.TakeTheObjective(), Is.True, "§03's objective refused a player with free hands");
                guidance.Observe();

                Assert.That(guidance.CluesRead, Is.Zero, "nothing was read; the count must say so");
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.CarryObjectiveOut),
                    "carrying beats reading — the line has to follow the hands. Line: " + guidance.Line);
                Assert.That(guidance.Note, Does.Contain("무게"),
                    "§08 counts the objective's weight, so the load line has to show it");

                // Put it down again. Back to §03's chain, still at zero, and the load
                // line goes with it.
                Assert.That(match.DropTheObjective(), Is.True, "§03's objective could not be put down again");
                guidance.Observe();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.ReadClues),
                    "hands free and nothing read; back to §03's first job. Line: " + guidance.Line);
                Assert.That(guidance.Line, Does.Contain("(0/" + GameConstants.CluesRequiredToLocate + ")"));
                Assert.That(guidance.Note, Is.Empty, "the hands are empty; there is no load to report");

                // Up with nothing. §01's first arc is over even though nothing was
                // achieved, so the line must not go back to "go down".
                match.GoToTheSurface();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.ShopAndDescendAgain),
                    "a wasted trip is still a trip. Line: " + guidance.Line);
                Assert.That(guidance.Line, Does.Not.Contain("아래로 내려가세요"),
                    "§01's opening line belongs to a player who has never been down");

                // 전리품 in the hands while standing in the apron — §08 sells on arrival,
                // so this only happens out of sequence, and it still has an answer.
                Assert.That(match.TakeSomeLoot(), Is.True, "§08 put no pocketable loot on the map");
                guidance.Observe();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.SellLoot),
                    "loot in the hands at the van is a sale waiting to happen. Line: " + guidance.Line);
                Assert.That(guidance.Line, Does.Contain("차량에 팔고 상점을 여세요"));

                match.EmptyThePockets();
                guidance.Observe();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.ShopAndDescendAgain),
                    "hands empty at the van, so the line goes back to the shop. Line: " + guidance.Line);
                Assert.That(guidance.Line, Does.Contain("상점"));

                // And a match that is over says so, whatever else is true.
                Assert.That(match.Director.TryLeaveForGood(out var refusal), Is.True,
                    "§02's 생존 row refused: " + refusal);
                guidance.Observe();
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.MatchOver));
                Assert.That(guidance.Resolution.Outcome, Is.EqualTo(MatchOutcome.Survived));

                // A new match starts the tally over — an end screen's 계속 button must
                // not carry the last run's numbers into the next one.
                Assert.That(match.Director.BeginMatch(SoloPlaytest.PlaytestSeed + 7), Is.True,
                    "BeginMatch refused the follow-up seed");
                guidance.Observe();
                Assert.That(guidance.CluesRead, Is.Zero);
                Assert.That(guidance.LootSold, Is.Zero);
                Assert.That(guidance.RoundTrips, Is.Zero);
                Assert.That(guidance.Phase, Is.EqualTo(GuidancePhase.Descend),
                    "a fresh match opens on §01's first line again. Line: " + guidance.Line);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>
        /// One built solo scene and the handful of moves a tester can make in it.
        /// <para>
        /// The stepping helpers are this file's own rather than <c>SoloMatchLoopTests</c>'
        /// — ARCHITECTURE §5 gives each system exactly one test file and forbids editing
        /// another's, so the eight lines are copied rather than shared. They do what that
        /// file's do and for the same reasons: interactables are called directly because a
        /// crosshair ray needs a rendered frame, and the player is moved by transform
        /// because §05's movement has its own PlayMode suite.
        /// </para>
        /// </summary>
        private sealed class Fixture
        {
            private Fixture(
                MatchDirector director,
                Transform player,
                PlayerInteractor interactor,
                MatchHud hud,
                MatchMap map,
                CluePropInteractable[] clues)
            {
                Director = director;
                Guidance = new MatchGuidance(director);
                _player = player;
                _interactor = interactor;
                _hud = hud;
                _map = map;
                Clues = clues;
            }

            private readonly Transform _player;
            private readonly PlayerInteractor _interactor;
            private readonly MatchHud _hud;
            private readonly MatchMap _map;

            internal MatchDirector Director { get; }

            internal MatchGuidance Guidance { get; }

            internal CluePropInteractable[] Clues { get; }

            internal static Fixture Build()
            {
                Assert.That(SoloPlaytest.BuildScene(), Is.True, "the solo playtest scene could not be built");

                var director = UnityEngine.Object.FindFirstObjectByType<MatchDirector>();
                Assert.That(director, Is.Not.Null, "the built scene has no MatchDirector");

                var motor = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
                Assert.That(motor, Is.Not.Null, "the built scene has no player rig");

                var interactor = motor!.GetComponentInChildren<PlayerInteractor>();
                Assert.That(interactor, Is.Not.Null, "the player rig has no PlayerInteractor");

                Assert.That(director!.BeginMatch(SoloPlaytest.PlaytestSeed), Is.True, "BeginMatch refused");

                var hud = UnityEngine.Object.FindFirstObjectByType<MatchHud>();
                Assert.That(hud, Is.Not.Null, "the built scene has no MatchHud");

                var map = director.Map;
                Assert.That(map, Is.Not.Null, "the match has no map");

                var clues = UnityEngine.Object.FindObjectsByType<CluePropInteractable>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                Array.Sort(clues, (a, b) => a.ClueId.CompareTo(b.ClueId));
                Assert.That(clues.Length, Is.GreaterThanOrEqualTo(GameConstants.CluesRequiredToLocate),
                    "§03 needs at least " + GameConstants.CluesRequiredToLocate + " marks on the map");

                return new Fixture(director, motor.transform, interactor!, hud!, map!, clues);
            }

            /// <summary>Walks out of §01's apron and settles the phase. Observes once at the end.</summary>
            internal void GoUnderground()
            {
                Teleport(FarthestSiteFromTheMonster());
                Step(GameConstants.FixedStep);
                Guidance.Observe();
            }

            /// <summary>Walks back into §01's apron, which is where §08 sells. Observes once at the end.</summary>
            internal void GoToTheSurface()
            {
                Teleport(_map.Entrance);
                Step(GameConstants.FixedStep);
                Guidance.Observe();
            }

            /// <summary>Holds an unbroken beam on one mark for longer than §03 asks.</summary>
            internal void HoldBeamOn(int clueId)
            {
                PushRead(clueId, 1f, GameConstants.ClueReadSeconds * 1.2f);
            }

            /// <summary>Turns away. §03 keeps no record of what was on screen.</summary>
            internal void LookAway()
            {
                PushRead(-1, 0f, GameConstants.FixedStep * 2f);
            }

            /// <summary>
            /// What the overlay drew for the last completed read, or empty. A string that
            /// is compared against and never stored — see the class remarks.
            /// </summary>
            internal string LastMarkDrawn()
            {
                var overlay = _hud.Clue;
                return overlay != null ? overlay.Current.MarkText : string.Empty;
            }

            internal bool TakeSomeLoot()
            {
                var loot = UnityEngine.Object.FindObjectsByType<LootPropInteractable>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);

                if (loot.Length == 0)
                {
                    return false;
                }

                loot[0].OnPressed(_interactor);
                Step(GameConstants.FixedStep);
                return true;
            }

            /// <summary>
            /// Puts §08's 전리품 back on the floor. <c>BeginMatch</c> rebuilds the world
            /// but not the player's pockets, so a run that ends holding loot would carry
            /// it into the next match — real, and not what the branch under test is about.
            /// </summary>
            internal void EmptyThePockets()
            {
                var pockets = _interactor.Pockets;
                if (pockets != null)
                {
                    pockets.DropAll();
                }

                Director.NoteLootTaken();
                Step(GameConstants.FixedStep);
            }

            internal bool TakeTheObjective()
            {
                var prop = Director.ObjectiveProp;
                if (prop == null)
                {
                    return false;
                }

                Teleport(prop.transform.position);
                Step(GameConstants.FixedStep);
                prop.OnPressed(_interactor);
                Step(GameConstants.FixedStep);
                return prop.IsCarried;
            }

            internal bool DropTheObjective()
            {
                var prop = Director.ObjectiveProp;
                if (prop == null || !prop.IsCarried)
                {
                    return false;
                }

                prop.OnPressed(_interactor);
                Step(GameConstants.FixedStep);
                return !prop.IsCarried;
            }

            private void PushRead(int clueId, float lightQuality, float seconds)
            {
                var steps = Mathf.CeilToInt(seconds / GameConstants.FixedStep);
                for (var i = 0; i < steps; i++)
                {
                    var context = default(ClueReadContext);
                    context.ClueId = clueId;
                    context.DistanceToClue = 0f;
                    context.ReaderSpeed = 0f;
                    context.LightQuality = lightQuality;
                    context.ViewAngleDegrees = 0f;
                    context.Blur = 0f;

                    Director.SetClueContext(context);
                    Director.StepMatch(GameConstants.FixedStep);
                }
            }

            private void Step(float seconds)
            {
                var steps = Mathf.CeilToInt(seconds / GameConstants.FixedStep);
                for (var i = 0; i < steps; i++)
                {
                    var context = default(ClueReadContext);
                    context.ClueId = -1;
                    Director.SetClueContext(context);
                    Director.StepMatch(GameConstants.FixedStep);
                }
            }

            private void Teleport(Vector3 target)
            {
                var controller = _player.GetComponent<CharacterController>();
                if (controller != null)
                {
                    controller.enabled = false;
                }

                _player.position = target;

                if (controller != null)
                {
                    controller.enabled = true;
                }

                Physics.SyncTransforms();
            }

            /// <summary>
            /// A 후보 지점 outside §01's apron and as far from §06's spawn as the map
            /// allows. This run stands still for seconds at a time; a nearer site would
            /// have the monster walk up and end the match, which is correct behaviour and
            /// a useless test.
            /// </summary>
            private Vector3 FarthestSiteFromTheMonster()
            {
                var best = _map.Entrance;
                var bestDistance = -1f;
                var monsterStart = _map.MonsterSpawn != null ? _map.MonsterSpawn.position : _map.Entrance;

                for (var i = 0; i < _map.CandidateSites.Count; i++)
                {
                    var site = _map.CandidateSites[i].position;
                    if (_map.IsOnSurface(site))
                    {
                        continue;
                    }

                    var distance = Vector3.Distance(site, monsterStart);
                    if (distance > bestDistance)
                    {
                        bestDistance = distance;
                        best = site;
                    }
                }

                return best;
            }
        }
    }
}
