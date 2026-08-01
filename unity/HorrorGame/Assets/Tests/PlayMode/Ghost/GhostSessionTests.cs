#if UNITY_INCLUDE_TESTS
#nullable enable

using System;
using System.Collections;
using System.Reflection;
using HorrorGame.Core;
using HorrorGame.Core.Ghost;
using HorrorGame.Core.Match;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Ghost;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using HorrorGame.UI.Readouts;
using HorrorGame.UI.Screens;
using NUnit.Framework;
using UnityEngine;
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
        /// over, §08's pile is reported to the ghost, and §02's verdict is held rather
        /// than shown.
        /// </summary>
        [UnityTest]
        public IEnumerator Being_caught_makes_a_ghost_and_does_not_end_the_screen()
        {
            var run = new Run();
            yield return run.Start();
            yield return run.Descend();
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

            // §02 has decided — one seat, dead, so 전멸 — and that is exactly the case that
            // used to slam the panel up.
            Assert.That(director.Outcome, Is.EqualTo(MatchOutcome.Wiped),
                "the tally is unchanged: §02 still reads a solo death as 전멸.");

            Assert.That(director.IsRunning, Is.True,
                "§09: 죽으면 지루하다 → 볼 게 있고 할 게 있다. The match has to keep running or there is nothing to watch — "
                + "§07's clock, §06's hunt and the 45 s cooldown are all stepped from it.");

            Assert.That(director.EndScreenHeldForGhost, Is.True,
                "§02's verdict is reached and held for the ghost, not shown to it.");

            var end = director.GetComponentInChildren<EndScreen>();
            Assert.That(end == null || !end.IsVisible, Is.True,
                "§09 must not be thrown to §02's panel the instant the monster lands. The end screen is up.");
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
            yield return run.Descend();
            yield return run.Die();

            var director = run.Director;
            var ghosts = run.Ghosts;

            Assert.That(ghosts.VerdictIsWaiting, Is.True, "the ghost was never told §02 had finished.");

            var before = director.Outcome;
            Assert.That(ghosts.TryEndTheMatch(), Is.True, "the held end screen refused to open.");
            yield return null;

            Assert.That(director.Outcome, Is.EqualTo(before),
                "§09 offers the ghost the timing and nothing else. The outcome moved when the ghost asked for it.");

            Assert.That(ghosts.IsActive, Is.False, "the ghost kept the camera after the match ended.");
            Assert.That(director.IsRunning, Is.False, "the match kept stepping after §02's screen went up.");
        }

        /// <summary>
        /// §09's 신호 row. One rattle, then forty-five seconds of nothing, then one more —
        /// and a failed attempt costs the ghost none of that wait.
        /// </summary>
        [UnityTest]
        public IEnumerator The_rattle_is_the_only_channel_and_it_costs_forty_five_seconds()
        {
            var run = new Run();
            yield return run.Start();
            yield return run.Descend();
            yield return run.Die();

            var ghosts = run.Ghosts;
            var ghost = ghosts.Ghost!;

            Assert.That(ghost.CanRattle, Is.True,
                "§09 starts a ghost armed: the seconds right after it watched itself die are the most informative it will ever have.");

            // Flown to something worth shaking, because §12's corridors are largely bare
            // and where a player falls very often has nothing in it. This is the flight
            // §09 gives the ghost instead of a voice.
            var thing = FindSomethingToShake(ghost.Position.ToVector3());
            Assert.That(thing, Is.Not.Null, "this map has no Interactable at all, so §09's one verb cannot be exercised.");
            run.Fly(thing!.transform.position - (Vector3.forward * 1.2f));

            var found = ghosts.LookForSomethingToShake();
            Assert.That(found.Found, Is.True,
                "the ghost is standing next to " + thing!.name + " and GhostRattleTarget found nothing to shake.");
            Assert.That(found.Distance, Is.LessThanOrEqualTo(GameConstants.GhostRattleRange));

            Assert.That(ghosts.TryRattle(out var first), Is.True, "the first rattle was refused: " + first.Failure);
            Assert.That(first.Occurred, Is.True);
            Assert.That(ghost.RattleCount, Is.EqualTo(1));
            Assert.That(ghost.RattleCooldownRemaining,
                Is.EqualTo(GameConstants.GhostRattleCooldownSeconds).Within(1e-3f),
                "§09: 쿨타임 45초.");

            // Immediately again — the whole of 「쿨타임 45초 안에 다시 시도할 수 없다」.
            Assert.That(ghosts.TryRattle(out var tooSoon), Is.False);
            Assert.That(tooSoon.Failure, Is.EqualTo(GhostSignalFailure.OnCooldown));
            Assert.That(ghost.RattleCount, Is.EqualTo(1), "a refused attempt still moved the counter.");

            // One tick short of the wait. The cooldown is Core's and is stepped by
            // MatchState.Tick off the host's fixed step, so this drives the real clock
            // rather than poking the field.
            run.RunSeconds(GameConstants.GhostRattleCooldownSeconds - 0.5f);
            Assert.That(ghost.CanRattle, Is.False,
                "the 45 s came round early — the cooldown is not being driven by the match's own step.");
            Assert.That(ghosts.TryRattle(out var stillTooSoon), Is.False);
            Assert.That(stillTooSoon.Failure, Is.EqualTo(GhostSignalFailure.OnCooldown));

            run.RunSeconds(1f);
            Assert.That(ghost.CanRattle, Is.True, "the 45 s never came round at all.");

            ghosts.LookForSomethingToShake();
            Assert.That(ghosts.TryRattle(out var second), Is.True, "the second rattle was refused: " + second.Failure);
            Assert.That(ghost.RattleCount, Is.EqualTo(2));

            yield return null;
        }

        /// <summary>
        /// A rattle that lands nowhere near anything does not arm the cooldown. §09's
        /// ghost is already being punished by the wait; charging it another forty-five
        /// seconds for pressing the key beside an empty corridor would add a trap on top
        /// of a penalty.
        /// </summary>
        [UnityTest]
        public IEnumerator Reaching_for_nothing_does_not_cost_the_wait()
        {
            var run = new Run();
            yield return run.Start();
            yield return run.Descend();
            yield return run.Die();

            var ghosts = run.Ghosts;
            var ghost = ghosts.Ghost!;

            // Far outside the building, where §12 has nothing at all.
            run.Fly(new Vector3(0f, 400f, 0f));

            Assert.That(ghosts.LookForSomethingToShake().Found, Is.False,
                "there is geometry 400 m above the map, so this case is not being exercised.");

            Assert.That(ghosts.TryRattle(out var nothing), Is.False);
            Assert.That(nothing.Failure, Is.EqualTo(GhostSignalFailure.OutOfRange),
                "§09 keeps 너무 멀다 and 아직 흔들 수 없다 apart; only one of them is worth flying somewhere to fix.");
            Assert.That(ghost.CanRattle, Is.True, "a missed reach spent the 45 s.");
            Assert.That(ghost.RattleCount, Is.Zero);

            yield return null;
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
            yield return run.Descend();
            yield return run.Die();

            var state = run.Director.State!;
            var player = state.PlayerAt(run.Director.LocalPlayerIndex);

            Assert.That(player.MayTransmitVoice, Is.False,
                "§09 말하기: 불가능, and §13 gates it at the sender — 전부 받아놓고 볼륨만 0으로 재생하면 클라이언트 조작으로 다 들린다.");
            Assert.That(player.Ghost!.CanSpeak, Is.False, "§09 말하기: 불가능.");

            // The scan is pointed at the layer THIS pass added and nowhere else.
            // MatchTests.Section09_Ghost_CannotSpeakAtAll already holds the line inside
            // GhostState, and UiTests.GhostUi_HasNoVoiceWidgetToDisable holds it across
            // the readout and the overlay. Re-running those here is not extra safety, it
            // is a second copy of a rule that can now disagree with the first — the first
            // draft of this test did exactly that and failed on GhostState.CanSpeak, which
            // is §09's own row written down rather than a channel on offer.
            var banned = new[] { "voice", "mic", "speak", "talk", "mute", "chat", "radio", "push", "message", "say" };
            var added = new[] { typeof(GhostSession), typeof(GhostRattleTarget) };

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
                        + ". Its entire outbound bandwidth is one rattle every "
                        + GameConstants.GhostRattleCooldownSeconds + " s, and a rattle can only ever mean 'here'.");
                }
            }

            // The one outbound thing there is, and the shape of it is the argument: a
            // place, a distance, a wait, and a reason it failed. No text, no recipient.
            foreach (var field in typeof(GhostRattle).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.That(
                    field.FieldType == typeof(bool) || field.FieldType == typeof(float)
                    || field.FieldType == typeof(GhostSignalFailure)
                    || field.FieldType == typeof(HorrorGame.Core.Math.Vec3),
                    Is.True,
                    "§09: GhostRattle." + field.Name + " is a " + field.FieldType.Name
                    + ". A rattle can only ever mean 'here'; a field that could carry more is a second channel.");
            }
        }

        /// <summary>
        /// §09 탈출: 불가능. The ghost cannot leave by any route the match exposes, and
        /// the one that looks closest — <c>MatchDirector.TryLeaveForGood</c> — refuses.
        /// </summary>
        [UnityTest]
        public IEnumerator A_ghost_has_no_route_to_the_exit()
        {
            var run = new Run();
            yield return run.Start();
            yield return run.Descend();
            yield return run.Die();

            var director = run.Director;
            var state = director.State!;
            var index = director.LocalPlayerIndex;

            Assert.That(state.PlayerAt(index).Ghost!.CanEscape, Is.False, "§09 탈출: 불가능 — 사망 페널티가 명확해진다.");

            Assert.That(director.TryLeaveForGood(out var refusal), Is.False,
                "§09's ghost walked out of the match through §02's own door.");
            Assert.That(refusal, Is.Not.Empty);

            Assert.That(state.TryExtract(index), Is.False,
                "MatchState let a ghost extract. §02 rests on this: if the dead could leave, 누군가는 살아서 나가야 한다 "
                + "would cost nothing and a wipe would be unreachable.");

            // And flying to the surface is not an exit either. §09's ghost passes through
            // walls, so it can reach §01's 지상 in seconds; the body it left behind is
            // what the match measures, and it is still underground.
            var map = director.Map!;
            run.Fly(map.Entrance + (Vector3.up * 2f));
            run.RunSeconds(1f);

            Assert.That(director.LocalPlayerOnSurface, Is.False,
                "flying the camera to the 출입구 surfaced the player. §09's ghost is a view, not a body — "
                + "MatchDirector.UpdatePhase must keep measuring the corpse.");
            Assert.That(state.PlayerAt(index).HasEscaped, Is.False);
            Assert.That(director.TryLeaveForGood(out _), Is.False,
                "standing the ghost's camera in §01's 안전 지대 opened §02's exit.");

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

        private static Interactable? FindSomethingToShake(Vector3 from)
        {
            var best = (Interactable?)null;
            var bestDistance = float.PositiveInfinity;

            foreach (var thing in UnityEngine.Object.FindObjectsByType<Interactable>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var distance = Vector3.Distance(thing.transform.position, from);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = thing;
                }
            }

            return best;
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

            /// <summary>
            /// Walks the rig out of §01's 지상. §09 only mints a ghost below ground —
            /// §01 and §08 both label the surface a 안전 지대 — so a death up here is
            /// refused and the test would be measuring nothing.
            /// </summary>
            internal IEnumerator Descend()
            {
                var map = _director!.Map!;
                var deepest = float.PositiveInfinity;
                var chosen = _motor!.transform.position;

                for (var i = 0; i < map.CandidateSites.Count; i++)
                {
                    var at = map.CandidateSites[i].position;
                    if (map.IsOnSurface(at) || at.y >= deepest)
                    {
                        continue;
                    }

                    deepest = at.y;
                    chosen = at;
                }

                Teleport(chosen + (Vector3.up * 0.2f));
                _director.StepMatch(GameConstants.FixedStep);
                yield return null;

                Assert.That(_director.LocalPlayerOnSurface, Is.False,
                    "the player is still in §01's 지상, where §09 refuses to make a ghost.");
            }

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
