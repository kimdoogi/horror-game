#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Race;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Match
{
    /// <summary>
    /// Standing in front of the creature kills you.
    /// <para>
    /// <b>Why this exists.</b> The owner played the solo build, walked up to the monster,
    /// stood there, and did not die — <i>괴물앞에있어도 안죽는데?</i>. The answer at the time
    /// was a diagnostic log handed back for them to read, which is the wrong shape of
    /// answer: it asks the person who found the bug to also localise it. The kill path
    /// crosses several systems that each have a legitimate reason to do nothing (§06's
    /// aggro, the lunge's own recovery window, §09's ghost), so the useful thing is a test
    /// that walks the path and names the first link that is open.
    /// </para>
    /// <para>
    /// <b>Nothing here is faked.</b> The temptation is to force <c>MonsterStateId.Chase</c>
    /// and assert the lunge — that would pass while the real complaint stayed true,
    /// because "the monster never notices me" and "the monster notices me and cannot
    /// kill me" are both this bug from where the player is standing. So the test moves a
    /// body into the creature's face and steps the real <see cref="MatchDirector"/>: the
    /// brain does its own seeing, the lunge does its own committing, and §09 does its own
    /// killing.
    /// </para>
    /// <para>
    /// <b>What §01's race changed, and why this file had to move with it.</b> The map is
    /// now one column of eight concentric-maze storeys, the creature starts in the MIDDLE
    /// of B5, and the middle of every storey is where the 투하구 are. The version of this
    /// test written for the co-operative map put the runner six metres in front of the
    /// creature and walked them straight in — and on the tower that walk goes down a hole:
    /// the run recorded <c>[Race] §01 0번 B6 착지</c>, then measured ten seconds of a
    /// creature on B5 failing to kill a body on B6 that was 33.74 m away. Every number it
    /// reported was true and none of them was about the owner's complaint. So the approach
    /// is now chosen against the scene's own <see cref="Chute"/> components — the same
    /// objects <c>MatchDirector.CheckChutes</c> tests the player against every tick — and
    /// a runner swallowed by one fails the test immediately, by name, rather than
    /// silently turning it into a measurement of something else.
    /// </para>
    /// <para>
    /// It lives in the predefined assembly because <c>MatchDirector</c> does, and an
    /// <c>.asmdef</c> cannot reference one.
    /// </para>
    /// </summary>
    public sealed class MonsterKillTests
    {
        /// <summary>§13's seed for the layout under test — <c>SoloPlaytest.PlaytestSeed</c>.</summary>
        private const int Seed = 20260731;

        /// <summary>The scene <c>SoloPlaytest.BuildScene</c> writes.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>
        /// Seconds of match to run before giving up. §06 needs a moment to notice — the
        /// brain's aggro is a rule with a rise time, not an <c>if</c> — and the lunge adds
        /// its commit and contact windows on top. Ten seconds is roughly fifteen times
        /// what the whole chain needs, so a timeout here means something is off, not slow.
        /// </summary>
        private const float BudgetSeconds = 10f;

        /// <summary>
        /// Furthest run-up the search will look for, metres. Half again
        /// <see cref="GameConstants.MonsterPatrolNoticeRange"/>, because a walk that begins
        /// outside the contact range is the owner's actual story: they walked into it
        /// rather than being placed inside it.
        /// <para>
        /// It is a ceiling, not a requirement. §01 puts the creature in the middle of a
        /// storey and the middle is a 3×3 chamber, so on this map the open ground around
        /// the creature runs out well before 6 m on most bearings. The search takes the
        /// longest clear run-up any bearing offers and the report says what it got; a
        /// shorter one still asks the owner's question, it just hands more of the setup to
        /// the placement and less to the walking.
        /// </para>
        /// </summary>
        private const float ApproachStartMetres = GameConstants.MonsterPatrolNoticeRange * 1.5f;

        /// <summary>
        /// Seconds the walk-in is allowed. <see cref="ApproachStartMetres"/> at
        /// <see cref="GameConstants.WalkSpeed"/> is three seconds; the rest is slack for a
        /// creature that turns and walks off mid-approach.
        /// </summary>
        private const float ApproachBudgetSeconds = 8f;

        /// <summary>
        /// Bearings tried when choosing which side to come at the creature from. Five
        /// degrees is finer than the 2.5 m cell the map is drawn on at every radius this
        /// search uses, so a gap that exists is found.
        /// </summary>
        private const int ApproachBearings = 72;

        /// <summary>
        /// How far off the direct line a runner will step to get round a hole, degrees.
        /// The 투하구 mouth is 1.4 m across against a 2.5 m cell, so a hole never blocks a
        /// corridor — there is always a way past, and a person takes it rather than
        /// walking in.
        /// </summary>
        private const float SidestepDegrees = 40f;

        /// <summary>
        /// Metres between samples along a candidate approach. A quarter of
        /// <see cref="Chute.MouthRadiusMetres"/>: the shortest chord a sampled line can cut
        /// through a mouth and still be missed is twice the sample spacing, so at a quarter
        /// only a line passing within 1 cm of the mouth's rim can slip through — and that
        /// line is not one a runner walks down anyway.
        /// </summary>
        private const float ApproachSampleMetres = Chute.MouthRadiusMetres * 0.25f;

        /// <summary>
        /// Radius the approach search snaps candidate ground with, metres. Two things decide
        /// it and both are lower bounds pointing the same way: it is a little over the
        /// player capsule's own radius, so a sample needing more snap than this is not
        /// somewhere a body fits; and it is under a sixth of the 3.75 m storey pitch, so a
        /// sample can never be answered by the floor above or below. That second one is the
        /// point — a "place to stand" on another storey is what this test used to be
        /// measuring, and 4 m is the snap radius that let it.
        /// </summary>
        private const float GroundSnapMetres = 0.6f;

        [SetUp]
        public void QuietenTheImporter()
        {
            // Loading the eight-storey scene re-emits Mirror's packaging complaint about an
            // immutable folder nobody is allowed to fix, and the Test Framework fails a
            // test on any unexpected LogError. Safe only because every step is asserted.
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>
        /// Puts the building back. An eight-storey map left in the active scene has failed
        /// audio-occlusion tests that ran after it — see <c>InteractionPickupTests</c>.
        /// </summary>
        [UnityTearDown]
        public IEnumerator PutTheWorldBack()
        {
            var solo = SceneManager.GetSceneByName(SoloScene);
            if (solo.IsValid() && solo.isLoaded)
            {
                var empty = SceneManager.CreateScene("MonsterKillTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(solo);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// The owner's report, as an assertion: a player standing inside the creature's
        /// reach, in the open, with clear line of sight, dies.
        /// </summary>
        [UnityTest]
        public IEnumerator Standing_in_front_of_the_creature_kills_you()
        {
            SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var director = Object.FindFirstObjectByType<MatchDirector>();
            Assert.That(director, Is.Not.Null, "the solo scene has no MatchDirector");

            if (director!.Map == null)
            {
                Assert.That(director.BeginMatch(Seed), Is.True, "BeginMatch refused");
            }

            yield return null;

            var monster = Object.FindFirstObjectByType<MonsterAgent>();
            Assert.That(monster, Is.Not.Null, "the solo scene has no MonsterAgent to walk up to");

            var player = FindPlayerRoot();
            Assert.That(player, Is.Not.Null, "no player rig in the scene, so nobody can stand anywhere");

            // Was `director.State.PlayerAt(LocalPlayerIndex).Ghost is null`, read off a
            // seat table nothing else in the game consulted. LocalPlayerIsGhost is the
            // better assertion by the same reasoning: it is the property the game itself
            // reads to decide whether the grab path runs at all.
            Assert.That(director.LocalPlayerIsGhost, Is.False,
                "the local player was already a ghost before the test put them anywhere. §09 skips "
                + "the whole grab path for a corpse, which is one of the things that can look "
                + "like 'the monster does not kill me'.");

            // ── The holes in the floor ──────────────────────────────────────────
            // MatchDirector.AttachChutes binds these at BeginMatch, and CheckChutes asks
            // every one of them whether it has swallowed the player, every tick. Asking
            // the same objects the same question is the only way this test can be sure it
            // is guarding against the real rule rather than against a copy of it that
            // drifts.
            var chutes = Object.FindObjectsByType<Chute>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(chutes.Length, Is.GreaterThan(0),
                "§01's 투하구 are not bound on this map, so this test cannot tell a runner standing in "
                + "front of the creature from a runner falling past it to the storey below — which is "
                + "exactly how this test stopped measuring anything.");

            var playerHeight = BodyHeight(player!);
            var monsterHeight = BodyHeight(monster!.transform);
            Assert.That(playerHeight, Is.GreaterThan(0f), "the player rig has no body to take an eye height from");
            Assert.That(monsterHeight, Is.GreaterThan(0f), "the creature has no body to aim a sight line at");

            // ── Choose a side to come at it from ────────────────────────────────
            // §01 puts the creature in the MIDDLE of its storey and the middle of a storey
            // is where the 투하구 are: on B5 the two mouths sit 2.5 m from the creature's
            // own post, due north and due south. A bearing is only usable if the WHOLE walk
            // along it stays out of every mouth and stays on this storey's floor.
            var post = monster.transform.position;
            var survey = new StringBuilder();
            var bearing = ChooseApproach(
                post, chutes, playerHeight, monsterHeight, monster.transform, survey, out var runUp);

            Assert.That(bearing.HasValue, Is.True,
                "there is nowhere within " + ApproachStartMetres.ToString("0.0") + " m of the creature at "
                + post.ToString("0.0") + " that a runner can stand on this storey, out of every 투하구, with "
                + "the creature in sight. That is a fact about the map, not about §06, and it means the "
                + "middle of a storey cannot be approached on foot at all:\n" + survey);

            Debug.Log("[KillTest] 괴물의 자리 " + post.ToString("0.00") + " — 같은 층에서 확보한 접근로 "
                      + runUp.ToString("0.00") + " m, 투하구 " + chutes.Length + "개를 피해서.");

            var start = post + (bearing!.Value * runUp);
            start.y = post.y;
            Warp(player!, start);
            director.StepMatch(GameConstants.FixedStep);
            yield return null;

            // ── Walk at it ──────────────────────────────────────────────────────
            // Re-aimed every step. Aiming once at a point six metres away would be aiming
            // at where it USED to be — it starts moving the moment it notices, and an
            // earlier version of this test spent ten seconds holding a body four metres
            // from a monster it had already walked away from.
            //
            // Around the holes, not into them. A runner who meets a 1.4 m mouth in a 2.5 m
            // cell steps past it; NextFoot does the same, and if it cannot it stands still
            // rather than descending. Falling in is separately fatal to the run — see the
            // guard below — because a runner on the floor below is not standing in front of
            // anything.
            var walking = 0f;
            var sidesteps = 0;
            while (walking < ApproachBudgetSeconds
                   && Flat(player!.position - monster.transform.position) > GameConstants.MonsterAttackRange * 0.9f)
            {
                var step = GameConstants.WalkSpeed * GameConstants.FixedStep;
                var toward = monster.transform.position;
                toward.y = player.position.y;

                var next = NextFoot(player.position, toward, step, chutes, out var turned);
                sidesteps += turned ? 1 : 0;
                Warp(player, next);

                director.StepMatch(GameConstants.FixedStep);
                walking += GameConstants.FixedStep;

                var fell = SwallowedBy(chutes, player.position);
                if (fell != null)
                {
                    Assert.Fail(
                        "the runner walked into " + fell.name + " on the way to the creature and was dropped to B"
                        + (fell.StoreyBelow + 1) + ". Nothing measured after this point is about §06 — it is a "
                        + "descent, and it is how this test came to spend ten seconds reporting a 33.74 m gap "
                        + "between two floors. The approach search let a step through, or the creature walked "
                        + "over a 투하구 and the runner followed it in.");
                }

                yield return null;
            }

            var stand = player!.position;

            // One step before asking anything about where they are: the phase flags and
            // the creature's own perception are recomputed inside StepMatch, so reading
            // them straight after a warp reports the frame before the warp.
            director.StepMatch(GameConstants.FixedStep);
            yield return null;

            // ── The run is only about §06 if the body really got there ──────────
            // §06 gives 순찰 one sight transition and it is the contact exception at
            // MonsterPatrolNoticeRange — see GameConstants, whose own remarks cite this
            // test. A run that ended further out than that never asked the owner's
            // question, and would fail for a reason ("it did not see me") that is correct
            // behaviour at that distance.
            var reached = Flat(stand - monster.transform.position);
            Assert.That(reached, Is.LessThanOrEqualTo(GameConstants.MonsterPatrolNoticeRange),
                "the walk ended " + reached.ToString("0.00") + " m from the creature, outside §06's "
                + GameConstants.MonsterPatrolNoticeRange.ToString("0.0") + " m contact range, so this run "
                + "never put anybody in front of anything. The creature walked away faster than a walking "
                + "player follows (" + sidesteps + " step(s) went round a 투하구), or the approach was blocked.");

            var standingIn = SwallowedBy(chutes, stand);
            Assert.That(standingIn, Is.Null,
                "the runner ended up standing inside " + (standingIn != null ? standingIn.name : string.Empty)
                + ", which is a hole, not a floor.");

            // ── Run the match and watch ─────────────────────────────────────────
            var trace = new StringBuilder();
            var elapsed = 0f;
            var everChased = false;
            var everCommitted = false;
            var closest = float.MaxValue;
            var lastLogged = -1f;

            while (elapsed < BudgetSeconds && !director.LocalPlayerIsGhost)
            {
                // Hold the ground they reached. A body left to its own devices slides
                // off a charging creature and the test would be measuring a chase.
                Warp(player, stand);

                director.StepMatch(GameConstants.FixedStep);
                elapsed += GameConstants.FixedStep;

                var gap = Flat(player.position - monster.transform.position);
                closest = Mathf.Min(closest, gap);
                everChased |= monster.State == MonsterStateId.Chase;
                everCommitted |= director.LungePhase == LungeState.Committed;

                if (elapsed - lastLogged >= 0.5f)
                {
                    lastLogged = elapsed;
                    trace.AppendLine("  " + elapsed.ToString("0.0") + "s  §06 " + monster.State
                                     + "  덮치기 " + director.LungePhase
                                     + "  " + gap.ToString("0.00") + " m"
                                     + "  Δy " + (player.position.y - monster.transform.position.y).ToString("0.00") + " m");
                }

                yield return null;
            }

            var died = director.LocalPlayerIsGhost;

            // ── If it did not happen, say which link was open ───────────────────
            var why = new StringBuilder();
            why.AppendLine("서서 " + BudgetSeconds.ToString("0") + "초를 기다렸는데 죽지 않았다.");
            why.AppendLine("가장 가까웠던 거리 " + closest.ToString("0.00") + " m, 공격 사거리는 "
                           + GameConstants.MonsterAttackRange.ToString("0.0") + " m.");
            why.AppendLine("선 자리 " + stand.ToString("0.00") + ", 괴물의 자리 " + post.ToString("0.00")
                           + " — 같은 층, 투하구 밖, 접근로 " + runUp.ToString("0.00") + " m.");
            why.AppendLine();

            if (!everChased)
            {
                why.AppendLine("§06 추격에 한 번도 들어가지 않았다 — 괴물이 눈앞의 사람을 못 본 것이다.");
                why.AppendLine("MonsterBrain의 접촉 예외(MonsterPatrolNoticeRange "
                               + GameConstants.MonsterPatrolNoticeRange.ToString("0.0") + " m), "
                               + "NavMeshWorldProbe의 _sightBlockers 레이어, "
                               + "또는 씬의 NavMesh 중 하나가 원인이다. 덮치기는 애초에 평가되지 않는다.");
            }
            else if (!everCommitted)
            {
                why.AppendLine("추격에는 들어갔지만 덮치기가 한 번도 커밋되지 않았다 — MonsterLunge.Tick이 "
                               + "거리를 사거리 안으로 보지 못했다. 두 거리가 서로 다른 기준점에서 재어지고 있다.");
            }
            else
            {
                why.AppendLine("덮쳤는데 죽지 않았다 — 접촉 판정(MonsterAttackReach)이 거절하거나, "
                               + "MatchDirector가 이미 유령을 하나 들고 있다(_ghost != null).");
            }

            why.AppendLine();
            why.Append(trace);

            Assert.That(died, Is.True, why.ToString());
        }

        // ------------------------------------------------------------------
        // Getting to the creature on a map whose middles are holes.
        // ------------------------------------------------------------------

        /// <summary>
        /// The longest clear run-up at the creature the map offers, as a bearing and a
        /// distance — or a zero <paramref name="runUpMetres"/> if there is none.
        /// <para>
        /// Each bearing is walked <em>outward</em> from <see cref="GameConstants.MonsterAttackRange"/>
        /// and stops at the first sample that is either inside a 투하구 mouth or off
        /// navigable ground within <see cref="GroundSnapMetres"/> — a radius chosen to be
        /// under a sixth of the storey pitch, so no sample can be satisfied by the floor
        /// above or below.
        /// </para>
        /// <para>
        /// The winner is the bearing with the longest clear run that also has line of sight
        /// to the creature from its far end, because "in the open, with clear line of
        /// sight" is what the owner's complaint describes. Longest rather than first, so
        /// the test uses whatever room the chamber has instead of whichever bearing the
        /// loop happened to try first.
        /// </para>
        /// </summary>
        private static Vector3? ChooseApproach(
            Vector3 post, Chute[] chutes, float playerHeight, float monsterHeight,
            Transform monster, StringBuilder survey, out float runUpMetres)
        {
            var aim = post + (Vector3.up * monsterHeight * 0.5f);
            var eyeRise = Vector3.up * playerHeight * 0.9f;

            Vector3? best = null;
            runUpMetres = 0f;

            for (var i = 0; i < ApproachBearings; i++)
            {
                var degrees = i * (360f / ApproachBearings);
                var direction = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;

                var clear = 0f;
                var stopped = "reached the search ceiling";
                for (var reach = GameConstants.MonsterAttackRange;
                     reach <= ApproachStartMetres;
                     reach += ApproachSampleMetres)
                {
                    var sample = post + (direction * reach);
                    sample.y = post.y;

                    var mouth = SwallowedBy(chutes, sample);
                    if (mouth != null)
                    {
                        stopped = mouth.name;
                        break;
                    }

                    if (!UnityEngine.AI.NavMesh.SamplePosition(
                            sample, out _, GroundSnapMetres, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        stopped = "no floor on this storey";
                        break;
                    }

                    clear = reach;
                }

                if (clear <= 0f)
                {
                    survey.AppendLine("  " + degrees.ToString("000") + "°  nowhere to stand — " + stopped);
                    continue;
                }

                var stand = post + (direction * clear);
                stand.y = post.y;
                if (!CanSee(stand + eyeRise, aim, monster))
                {
                    survey.AppendLine("  " + degrees.ToString("000") + "°  " + clear.ToString("0.0")
                        + " m clear, but the creature is out of sight from the far end");
                    continue;
                }

                survey.AppendLine("  " + degrees.ToString("000") + "°  " + clear.ToString("0.0")
                    + " m clear, creature in sight (stopped by " + stopped + ")");

                if (clear > runUpMetres)
                {
                    runUpMetres = clear;
                    best = direction;
                }
            }

            return best;
        }

        /// <summary>
        /// One walking step toward <paramref name="toward"/> that does not end in a hole.
        /// A 투하구 mouth is 1.4 m across in a 2.5 m cell, so there is always room beside
        /// one; a person walks round it, and standing still is the last resort rather than
        /// stepping in.
        /// </summary>
        private static Vector3 NextFoot(Vector3 from, Vector3 toward, float step, Chute[] chutes, out bool turned)
        {
            turned = false;

            var direct = Vector3.MoveTowards(from, toward, step);
            if (SwallowedBy(chutes, direct) == null)
            {
                return direct;
            }

            var heading = toward - from;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.000001f)
            {
                return from;
            }

            heading.Normalize();
            for (var sign = -1; sign <= 1; sign += 2)
            {
                var candidate = from + (Quaternion.Euler(0f, sign * SidestepDegrees, 0f) * heading * step);
                candidate.y = from.y;
                if (SwallowedBy(chutes, candidate) == null)
                {
                    turned = true;
                    return candidate;
                }
            }

            return from;
        }

        /// <summary>
        /// The 투하구 that would take a runner standing at <paramref name="position"/>, by
        /// the chute's own rule — <c>MatchDirector.CheckChutes</c> asks exactly this.
        /// </summary>
        private static Chute? SwallowedBy(Chute[] chutes, Vector3 position)
        {
            for (var i = 0; i < chutes.Length; i++)
            {
                if (chutes[i] != null && chutes[i].Swallows(position))
                {
                    return chutes[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Clear line from an eye to the creature. A hit on the creature's own body is not
        /// something in the way — the segment ends inside it.
        /// </summary>
        private static bool CanSee(Vector3 eye, Vector3 aim, Transform monster)
        {
            if (!Physics.Linecast(eye, aim, out var hit, ~0, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.transform != null && hit.transform.IsChildOf(monster);
        }

        /// <summary>
        /// How tall a body is, taken off the body rather than written down: the controller
        /// or agent the game itself moves it with, and the rendered bounds as a last
        /// resort. Used only to put an eye and an aim point at plausible heights.
        /// </summary>
        private static float BodyHeight(Transform root)
        {
            var controller = root.GetComponent<CharacterController>();
            if (controller != null)
            {
                return controller.height;
            }

            var agent = root.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                return agent.height;
            }

            var collider = root.GetComponentInChildren<Collider>();
            return collider != null ? collider.bounds.size.y : 0f;
        }

        private static Transform? FindPlayerRoot()
        {
            var motor = Object.FindFirstObjectByType<HorrorGame.Gameplay.Player.PlayerMotor>();
            return motor != null ? motor.transform : null;
        }

        /// <summary>
        /// Puts a body somewhere. A <c>CharacterController</c> ignores writes to
        /// <c>transform.position</c> while it is enabled, which is exactly the kind of
        /// silent no-op that makes a test pass against a player who never moved.
        /// </summary>
        private static void Warp(Transform root, Vector3 to)
        {
            var controller = root.GetComponent<CharacterController>();
            if (controller == null)
            {
                root.position = to;
                return;
            }

            controller.enabled = false;
            root.position = to;
            controller.enabled = true;
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }
    }
}
#endif
