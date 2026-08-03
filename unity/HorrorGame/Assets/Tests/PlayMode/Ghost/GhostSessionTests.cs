#if UNITY_INCLUDE_TESTS
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HorrorGame.Core;
using HorrorGame.Core.Ghost;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Ghost;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Race;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using HorrorGame.UI.Screens;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Ghosts
{
    /// <summary>
    /// §09 — 사망 처리 — 유령. Death is a change of seat, not the end of the screen.
    /// <para>
    /// <b>What this is here to stop happening again.</b> Before this pass a solo death
    /// went straight to §02's 패배 panel: §02 counts seats, one seat dead is 전멸, and
    /// 전멸 is final. §09 says the opposite — 죽으면 지루하다 / 볼 게 있고 할 게 있다 —
    /// so the section did not exist in the game at all. Every assertion below is one of
    /// §09's four rows or the wiring that lets them run.
    /// </para>
    /// <para>
    /// <b>The kill goes through <c>MatchDirector.CheckGrab</c>, not around it.</b>
    /// Calling <c>MatchState.TryKill</c> directly would prove that Core can mint a
    /// <c>GhostState</c>, which was never in doubt and was never the defect. What was
    /// missing is the path from §06 catching somebody to §09 taking their camera, so
    /// that path is what is driven: the monster is walked into 추격 with a real footstep
    /// cue and then put in contact with the player.
    /// </para>
    /// <para>
    /// <b>Two of these assert an absence, and absences need a method.</b> "There is no
    /// path to a voice channel" cannot be proved by not calling one, so it is asserted by
    /// reflection over every public member §09 exposes — the same instrument
    /// <c>UiTests.GhostUi_HasNoVoiceWidgetToDisable</c> already points at the readout and
    /// the overlay, widened here to the gameplay layer that was added around them.
    /// </para>
    /// <para>
    /// It lives in the predefined assembly because <c>MatchDirector</c> does, and an
    /// <c>.asmdef</c> cannot reference one. The file compiles out of a player build on
    /// <c>UNITY_INCLUDE_TESTS</c>.
    /// </para>
    /// </summary>
    public sealed class GhostSessionTests
    {
        /// <summary>§13's seed for the layout under test — <c>SoloPlaytest.PlaytestSeed</c>, quoted because that class is editor-only.</summary>
        private const int Seed = 20260731;

        /// <summary>The scene <c>SoloPlaytest.BuildScene</c> writes, and Build Settings carries.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>Fixed steps §06 is given to notice the bait. A budget, not a rule.</summary>
        private const int ChaseBudgetSteps = 600;

        /// <summary>How far in front of the monster the bait stands, metres. Inside §06's ±90° cone and well inside its sight range.</summary>
        private const float BaitDistanceMetres = 2f;

        [SetUp]
        public void QuietenTheImporter()
        {
            // Loading the five-storey scene re-emits Mirror's packaging complaint about an
            // immutable folder nobody is allowed to fix, and the Test Framework fails a
            // test on any unexpected LogError. Safe only because every step below is
            // asserted explicitly.
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>
        /// Puts the building back. A five-storey map left in the active scene has failed
        /// audio-occlusion tests that ran after it — see <c>InteractionPickupTests</c>.
        /// </summary>
        [UnityTearDown]
        public IEnumerator PutTheWorldBack()
        {
            var solo = SceneManager.GetSceneByName(SoloScene);
            if (solo.IsValid() && solo.isLoaded)
            {
                var empty = SceneManager.CreateScene("GhostSessionTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(solo);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        // ====================================================================
        // §09 — the state is entered, and §02 waits.
        // ====================================================================

        /// <summary>
        /// The whole of the wiring, asserted end to end: §06 catches the player, §09 takes
        /// over, and §02's verdict is held rather than shown. (This summary used to name
        /// §08's pile being reported to the ghost as a fourth step; the economy went on
        /// 2026-08-03 and there is no pile.)
        /// </summary>
        [UnityTest]
        public IEnumerator Being_caught_makes_a_ghost_and_does_not_end_the_screen()
        {
            var run = new Run();
            yield return run.Start();
            yield return run.Die();

            var director = run.Director;
            var ghosts = run.Ghosts;

            Assert.That(director.LocalPlayerIsGhost, Is.True,
                "§09: being caught turns the player into a ghost. MatchState.TryKill did not fire, or CheckGrab never called it.");

            Assert.That(ghosts.IsActive, Is.True,
                "§09 was reached in Core and nothing took the camera. That is the defect this pass exists to fix: "
                + "GhostSession never began, so a death is a locked view of a corpse.");

            Assert.That(ghosts.Ghost, Is.Not.Null);
            Assert.That(ghosts.Ghost!.SeesEntireMap, Is.True, "§09 시야: 맵 전체를 자유롭게 본다 (벽 통과).");

            // §02 has decided, and this is the assertion that changed shape with the
            // pivot. It read `director.Outcome == MatchOutcome.Wiped` — one seat, dead,
            // therefore 전멸, the co-op verdict that used to slam the panel up. A race has
            // no team to wipe: a caught runner is 탈락, and the standings say so by name.
            Assert.That(director.Race!.ExitOf(director.LocalPlayerIndex), Is.EqualTo(RaceExit.Caught),
                "§02: being caught is 탈락. The runner is out of the standings as something other than "
                + "Caught, which means RaceDirector.ReportCaught never ran — the defect where a caught "
                + "runner stayed Running forever and the race could never close.");

            Assert.That(director.IsRunning, Is.True,
                "§09: 죽으면 지루하다 → 볼 게 있고 할 게 있다. The match has to keep running or there is nothing to watch — "
                + "§07's clock, §06's hunt and the 45 s cooldown are all stepped from it.");

            Assert.That(director.RaceVerdictHeldForGhost, Is.True,
                "§02's verdict is reached and held for the ghost, not shown to it.");

            // ONE ASSERTION WAS REMOVED HERE on 2026-08-03 — DESCENT-PIVOT §7 step 7. It was
            //     var end = director.GetComponentInChildren<EndScreen>();
            //     Assert.That(end == null || !end.IsVisible, Is.True);
            // and it did not survive because UI/Screens/EndScreen.cs does not: 완전 승리 /
            // 부분 승리 / 생존 / 패배 were four ways a TEAM ended a 왕복, and a race ends one
            // runner four other ways (§02: 승리 · 완주 · 탈락 · 시간 초과).
            //
            // The line above still carries the half that matters — the verdict is REACHED and
            // HELD rather than shown — and it reads the director's own flag, which is what the
            // screen was only ever a consequence of. The half that is genuinely uncovered is
            // that nothing now checks a panel does not appear over a ghost's view. What would
            // appear is UI/RaceHud.cs's 탈락 verdict panel, and that panel has no test at all;
            // it is named in UiTests' own header as the gap left by EndScreenReadout.
        }

        /// <summary>
        /// §02's verdict is offered and taken, and taking it changes nothing about the
        /// verdict — the ghost decides <em>when</em>, never <em>what</em>.
        /// </summary>
        [UnityTest]
        public IEnumerator The_ghost_ends_the_match_deliberately_and_cannot_change_the_verdict()
        {
            var run = new Run();
            yield return run.Start();
            yield return run.Die();

            var director = run.Director;
            var ghosts = run.Ghosts;

            Assert.That(ghosts.VerdictIsWaiting, Is.True, "the ghost was never told §02 had finished.");

            var before = director.Race!.ExitOf(director.LocalPlayerIndex);
            Assert.That(ghosts.TryEndTheMatch(), Is.True, "the held verdict refused to open.");
            yield return null;

            Assert.That(director.Race!.ExitOf(director.LocalPlayerIndex), Is.EqualTo(before),
                "§09 offers the ghost the timing and nothing else. The standing moved when the ghost asked "
                + "for it — an eliminated player deciding their own placing.");

            Assert.That(ghosts.IsActive, Is.False, "the ghost kept the camera after the match ended.");
            Assert.That(director.IsRunning, Is.False, "the match kept stepping after §02's screen went up.");
        }

        // ====================================================================
        // DELETED with §09's 신호. Two tests stood here:
        //   The_rattle_is_the_only_channel_and_it_costs_forty_five_seconds
        //   Reaching_for_nothing_does_not_cost_the_wait
        // They pinned the 45 s cooldown, that it was driven by the match's own fixed
        // step, and that a reach at nothing did not burn the wait.
        //
        // §11's 탈락자 rule deleted the verb: 「살아 있는 사람에게 개입할 수 없다 —
        // 경주에서 죽은 사람이 산 사람을 도우면 그건 팀이다」. In a game where §12
        // makes 소리 the map, a placed noise is a forged footstep dropped by the only
        // entity with 맵 전체 시야, and the 45 s was priced for three ghosts rather
        // than §11's field of twenty.
        //
        // The replacement is The_ghost_changes_where_it_watches_from_and_the_world_
        // does_not_move, below: it asserts the new verb moves a camera and NOTHING
        // else, which is the property the deletion was for.
        // ====================================================================

        /// <summary>
        /// §09's replacement verb, and the property the rattle was deleted for.
        /// <para>
        /// The one key a spectator has now CUTS the camera — creature, finish, own body,
        /// free flight — and that is all it does. A rattle was a runner's footstep forged
        /// by somebody who could see the whole building (§12: 소리 → 바닥 재질이 지도다),
        /// so the test that it cannot come back is the test that pressing the key changes
        /// nothing but the view.
        /// </para>
        /// <para>
        /// <b>Control first, then the verb.</b> "Nothing moved" is not assertable on its
        /// own: §06's creatures patrol whether or not anybody is watching, and their audio
        /// bed rides along with them. So the run is split — the same number of steps with
        /// the key untouched, then with it pressed every step — and the claim is that
        /// pressing it adds NOTHING to the set of things that moved. That is exactly the
        /// property a rattle would have broken, and it survives the world being alive.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator The_ghost_changes_where_it_watches_from_and_the_world_does_not_move()
        {
            var run = new Run();
            yield return run.Start();
            yield return run.Die();

            var ghosts = run.Ghosts;

            // The eye is REPARENTED OUT of the session in Begin (SetParent(null)), so it
            // is not a child of GhostSession and GetComponentInChildren does not see it.
            // Take whichever camera is actually enabled — that is the one the ghost flies,
            // and moving it IS the verb, so it cannot be evidence against it.
            var eye = UnityEngine.Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.isActiveAndEnabled);
            Assert.That(eye, Is.Not.Null, "§09 took the camera away and did not leave one running.");

            const int Rounds = 16;
            const float StepSeconds = GameConstants.FixedStep * 2f;

            // Everything in the scene except the ghost's own camera rig — moving that IS
            // the verb, so it cannot be evidence against it.
            var watched = new List<Transform>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // The eye, everything above it, and everything hung under it — the
                // first-person hands ride on the camera and follow it by construction.
                if (t == eye!.transform || eye.transform.IsChildOf(t) || t.IsChildOf(eye.transform))
                {
                    continue;
                }

                watched.Add(t);
            }

            Assert.That(watched.Count, Is.GreaterThan(50),
                "the scene is nearly empty, so 'nothing moved' would be true of nothing.");

            // ── control: the same time passing, with the key untouched ──────────
            var movedByTheWorld = MoversOver(run, watched, Rounds, StepSeconds, cut: null);

            // ── the verb: identical stepping, cutting on every round ────────────
            var labels = new List<string>();
            var movedWithTheVerb = MoversOver(run, watched, Rounds, StepSeconds, cut: () =>
            {
                ghosts.CutToNextVantage();
                labels.Add(ghosts.WatchLabel);
            });

            Assert.That(labels.Distinct().Count(), Is.GreaterThan(1),
                "the cut key never changed what the ghost was watching — §09's one verb does nothing.");
            Assert.That(labels, Does.Contain(string.Empty),
                "the cycle never came back to free flight, so a ghost that cuts once is stuck in a shot.");

            var addedByTheVerb = movedWithTheVerb.Except(movedByTheWorld).ToList();
            Assert.That(addedByTheVerb, Is.Empty,
                "§11 탈락자: pressing §09's one key moved something the same seconds did not move on their "
                + "own — " + string.Join(", ", addedByTheVerb) + ". The dead cannot touch the race; that is "
                + "the whole reason 신호 was deleted.");

            yield return null;
        }

        /// <summary>
        /// Names every watched transform that moved while the run was stepped, optionally
        /// pressing something on each round. Position and rotation both, because a rattle
        /// that only spun a thing would still be a channel.
        /// </summary>
        private static HashSet<string> MoversOver(
            Run run, List<Transform> watched, int rounds, float stepSeconds, Action? cut)
        {
            var before = new List<(Vector3 P, Quaternion R)>(watched.Count);
            foreach (var t in watched)
            {
                before.Add(t == null ? (Vector3.zero, Quaternion.identity) : (t.position, t.rotation));
            }

            for (var i = 0; i < rounds; i++)
            {
                cut?.Invoke();
                run.RunSeconds(stepSeconds);
            }

            var movers = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < watched.Count; i++)
            {
                var t = watched[i];
                if (t == null)
                {
                    continue;
                }

                if (Vector3.Distance(t.position, before[i].P) > 0.001f
                    || Quaternion.Angle(t.rotation, before[i].R) > 0.05f)
                {
                    movers.Add(Path(t));
                }
            }

            return movers;
        }

        /// <summary>Full hierarchy path, so two objects with the same name are told apart.</summary>
        private static string Path(Transform t)
        {
            var name = t.name;
            for (var p = t.parent; p != null; p = p.parent)
            {
                name = p.name + "/" + name;
            }

            return name;
        }

        // ====================================================================
        // §09 — 말하기: 불가능. 탈출: 불가능.
        // ====================================================================

        /// <summary>
        /// There is no route from a ghost to a voice channel, and asserting that means
        /// asserting an absence: nothing §09 exposes accepts a message, a target or a
        /// payload, and nothing on it is named for a control that would.
        /// </summary>
        [UnityTest]
        public IEnumerator A_ghost_has_no_route_to_a_voice_channel()
        {
            var run = new Run();
            yield return run.Start();
            yield return run.Die();

            Assert.That(run.Director.LocalPlayerIsGhost, Is.True, "the runner was not caught.");
            Assert.That(run.Ghosts.Ghost!.CanSpeak, Is.False, "§09 말하기: 불가능.");

            // The scan is pointed at the gameplay layer, which is where a channel would
            // have to be opened for §09's silence to stop being structural.
            //
            // The rattle words are on this list on purpose. That reflection scan is the
            // one instrument in the project that catches somebody re-adding 신호 under
            // the old name, and the design's argument against it (§11 탈락자) is stronger
            // than the argument that put it there (§09's "유령에게는 그럴 이유가 딱히
            // 없다" — a claim about motive, and a design controls capability).
            var banned = new[]
            {
                "voice", "mic", "speak", "talk", "mute", "chat", "radio", "push", "message", "say",
                "rattle", "shake", "흔들", "signal", "신호",
            };
            var added = new[] { typeof(GhostSession) };

            foreach (var type in added)
            {
                foreach (var name in PublicNamesOf(type))
                {
                    var lower = name.ToLowerInvariant();
                    foreach (var word in banned)
                    {
                        Assert.That(lower.Contains(word), Is.False,
                            "§09: '" + type.Name + "." + name + "' is a channel a dead player could be given. "
                            + "The silence is structural — 죽은 사람이 정보를 주면 밸런스 붕괴 — and this is the layer "
                            + "that would have to open one for it to stop being structural.");
                    }
                }
            }

            // And the shape of it, which is the part a rename cannot dodge: nothing a
            // ghost can invoke takes a payload. GhostSession's whole instance surface is
            // verbs with no words in them.
            foreach (var method in typeof(GhostSession).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var parameter in method.GetParameters())
                {
                    Assert.That(
                        parameter.ParameterType == typeof(string) || parameter.ParameterType == typeof(object),
                        Is.False,
                        "§09: GhostSession." + method.Name + " takes a " + parameter.ParameterType.Name
                        + ". Its outbound bandwidth is now ZERO — after the rattle went there is "
                        + "nothing a ghost can send at all — and a payload parameter is where that "
                        + "would stop being true.");
                }
            }
        }

        /// <summary>
        /// 탈락에는 순위가 없다 — §02, and the whole point of the elimination rule.
        /// <para>
        /// <b>This test used to ask a different question and could not keep asking it.</b>
        /// It was <c>A_ghost_has_no_route_to_the_exit</c>: §09 탈출 불가능, proved by
        /// <c>MatchDirector.TryLeaveForGood</c> refusing and by flying the camera to §01's
        /// 지상 and checking the corpse had not surfaced with it. There is no exit and no
        /// surface — a race ends at the middle of B8, not at a door — so the co-op question
        /// has no subject.
        /// </para>
        /// <para>
        /// The race question underneath it is sharper, and it is the one the design
        /// argues for: give a caught runner a placing and safe crawling stops being
        /// punished, because dying slowly at the back would still beat finishing. So a
        /// ghost must be <see cref="RaceExit.Caught"/> and must not be able to move
        /// itself out of that, by flying, by waiting, or by asking the session to end.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator A_caught_runner_has_no_placing_and_cannot_get_one()
        {
            var run = new Run();
            yield return run.Start();
            yield return run.Die();

            var director = run.Director;
            var race = director.Race!;
            var index = director.LocalPlayerIndex;

            Assert.That(run.Ghosts.Ghost!.CanEscape, Is.False,
                "§09 탈출: 불가능 — 사망 페널티가 명확해진다.");
            Assert.That(race.ExitOf(index), Is.EqualTo(RaceExit.Caught),
                "§02: 잡히면 탈락. Anything else here is a caught runner still in the standings.");

            // DELETED: an assertion that MatchState.TryExtract refused a ghost. It needs
            // no replacement — a race has no extraction, so there is no call that could
            // be refused.

            // Flying does not finish a race. §09's ghost passes through walls, so it can be
            // at the finish in seconds; the body it left behind is what the race measures,
            // and that body is on the floor where it was caught.
            var finish = race.Finish;
            run.Fly(finish + (Vector3.up * 2f));
            run.RunSeconds(1f);

            Assert.That(race.ExitOf(index), Is.EqualTo(RaceExit.Caught),
                "flying the ghost's camera to the middle of B8 converted a 탈락 into a placing. §09 is a "
                + "view, not a body — the race must keep measuring the corpse.");

            yield return null;
        }

        // ====================================================================
        // The rig.
        // ====================================================================

        /// <summary>Every public field, property, method and parameter name a type declares.</summary>
        private static System.Collections.Generic.IEnumerable<string> PublicNamesOf(Type type)
        {
            const BindingFlags Declared =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var field in type.GetFields(Declared))
            {
                yield return field.Name;
            }

            foreach (var property in type.GetProperties(Declared))
            {
                yield return property.Name;
            }

            foreach (var method in type.GetMethods(Declared))
            {
                yield return method.Name;
                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.Name;
                }
            }
        }


        /// <summary>
        /// One solo match, driven by hand.
        /// <para>
        /// The director is stepped explicitly rather than left to <c>FixedUpdate</c> so a
        /// test can spend §09's forty-five seconds without waiting forty-five seconds, and
        /// so the number of steps a kill took is a number rather than a frame count.
        /// </para>
        /// </summary>
        private sealed class Run
        {
            private MatchDirector? _director;
            private PlayerMotor? _motor;
            private MonsterAgent? _monster;

            internal MatchDirector Director
            {
                get { return _director!; }
            }

            internal GhostSession Ghosts
            {
                get { return _director!.Ghosts!; }
            }

            internal IEnumerator Start()
            {
                SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
                yield return null;
                yield return null;

                _director = UnityEngine.Object.FindFirstObjectByType<MatchDirector>();
                _motor = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
                _monster = UnityEngine.Object.FindFirstObjectByType<MonsterAgent>();

                Assert.That(_director, Is.Not.Null, "the solo scene has no MatchDirector");
                Assert.That(_motor, Is.Not.Null, "the solo scene has no player rig");
                Assert.That(_monster, Is.Not.Null, "the solo scene has no monster");

                if (_director!.Map == null)
                {
                    Assert.That(_director.BeginMatch(Seed), Is.True, "BeginMatch refused");
                }

                yield return null;
                Assert.That(_director.Ghosts, Is.Not.Null,
                    "MatchDirector built no GhostSession, so §09 has nowhere to happen.");
            }

            // DELETED: Descend(). It walked the rig off §01's 지상 before every test in
            // this fixture, because §09 only minted a ghost below ground — §01 and §08 both
            // called the surface a 안전 지대 — so a death up there was refused and the test
            // would have measured nothing. It found somewhere to stand by scanning
            // MatchMap.CandidateSites for the deepest one MatchMap.IsOnSurface said no to.
            //
            // Neither the surface nor the 후보 지점 exist. A runner starts on the rim of B1,
            // 26 m under the street, and there is nowhere in the building a death is
            // refused — so the step this helper existed to perform has already happened by
            // the time Start() returns.

            /// <summary>
            /// Gets the player caught, the way a player gets caught: §06 hears a footstep,
            /// goes to 경계, sees the target, enters 추격, and <c>CheckGrab</c> measures the
            /// two bodies touching.
            /// </summary>
            internal IEnumerator Die()
            {
                var monster = _monster!;

                for (var i = 0; i < ChaseBudgetSteps && monster.State != MonsterStateId.Chase; i++)
                {
                    var bait = InFrontOf(monster);
                    Teleport(bait);

                    // §06's table gives 순찰 exactly one transition — 소리 감지 → 경계 —
                    // and only 경계 has 시야 확보 → 추격. A silent player standing in front
                    // of a patrolling monster is never seen, which is the design and not a
                    // shortcut this test may take.
                    monster.ReportSound(bait, GameConstants.MonsterSightRange);
                    _director!.StepMatch(GameConstants.FixedStep);
                }

                Assert.That(monster.State, Is.EqualTo(MonsterStateId.Chase),
                    "§06 never entered 추격, so CheckGrab — which only looks at 추격 — could not fire.");

                for (var i = 0; i < ChaseBudgetSteps && !_director!.LocalPlayerIsGhost; i++)
                {
                    Teleport(monster.transform.position);
                    _director.StepMatch(GameConstants.FixedStep);
                }

                yield return null;
            }

            /// <summary>Puts the ghost somewhere, through the component that owns where it is.</summary>
            internal void Fly(Vector3 to)
            {
                var fly = Ghosts.GetComponent<GhostFreeCamera>();
                Assert.That(fly, Is.Not.Null, "GhostSession has no free camera");

                // Null camera: GhostSession.Begin already gave this component the eye it
                // took off the rig, and picking one out of the scene here could hand it a
                // different camera from the one the ghost is actually looking through.
                fly!.Bind(Ghosts.Ghost!, null, to, Quaternion.identity);
            }

            /// <summary>Runs the match forward, in whole fixed steps.</summary>
            internal void RunSeconds(float seconds)
            {
                var steps = Mathf.CeilToInt(seconds / GameConstants.FixedStep);
                for (var i = 0; i < steps; i++)
                {
                    _director!.StepMatch(GameConstants.FixedStep);
                }
            }

            private static Vector3 InFrontOf(MonsterAgent monster)
            {
                var eye = monster.transform.position + (Vector3.up * 1.5f);

                for (var offset = 0f; offset <= 75f; offset += 15f)
                {
                    foreach (var sign in new[] { 1f, -1f })
                    {
                        var heading = Quaternion.Euler(0f, offset * sign, 0f) * monster.transform.forward;
                        heading.y = 0f;
                        if (heading.sqrMagnitude < 0.0001f)
                        {
                            continue;
                        }

                        heading.Normalize();
                        if (Physics.Raycast(
                            eye, heading, BaitDistanceMetres + 0.5f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            continue;
                        }

                        return monster.transform.position + (heading * BaitDistanceMetres);
                    }
                }

                return monster.transform.position + (monster.transform.forward * BaitDistanceMetres);
            }

            private void Teleport(Vector3 position)
            {
                var body = _motor!.gameObject;
                var controller = body.GetComponent<CharacterController>();
                if (controller != null)
                {
                    controller.enabled = false;
                }

                body.transform.position = position;

                if (controller != null)
                {
                    controller.enabled = true;
                }

                Physics.SyncTransforms();
            }
        }
    }
}
#endif
