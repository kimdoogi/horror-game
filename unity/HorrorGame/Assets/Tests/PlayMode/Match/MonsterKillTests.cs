#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
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
    /// crosses four systems that each have a legitimate reason to do nothing (§01's
    /// surface apron, §09's ghost, §06's aggro, the lunge's own recovery window), so the
    /// useful thing is a test that walks the path and names the first link that is open.
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

        [SetUp]
        public void QuietenTheImporter()
        {
            // Loading the five-storey scene re-emits Mirror's packaging complaint about an
            // immutable folder nobody is allowed to fix, and the Test Framework fails a
            // test on any unexpected LogError. Safe only because every step is asserted.
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

            var state = director.State;
            Assert.That(state, Is.Not.Null, "the match started without a state");
            Assert.That(state!.PlayerAt(director.LocalPlayerIndex).Ghost, Is.Null,
                "the local player was already a ghost before the test put them anywhere. §09 skips "
                + "the whole grab path for a corpse, which is one of the four things that can look "
                + "like 'the monster does not kill me'.");

            // ── Walk up to it and stand there ───────────────────────────────────
            // What the owner actually did. The walking matters and is not scenery: a
            // player who never moves makes no footstep, and a footstep is §06's only
            // route out of 순찰 beyond arm's length. Teleporting a body into the
            // creature's face and holding it there tests the one case where doing
            // nothing is correct.
            var start = monster!.transform.position + monster.transform.forward * 6f;
            start.y = monster.transform.position.y;
            Warp(player!, start);
            director.StepMatch(GameConstants.FixedStep);
            yield return null;

            // Walk at the creature, re-aiming every step. Aiming once at a point six
            // metres away would be aiming at where it USED to be — it starts moving the
            // moment it notices, and the first version of this test spent ten seconds
            // holding a body four metres from a monster it had already walked away from.
            var walking = 0f;
            while (walking < 8f && Flat(player!.position - monster.transform.position) > GameConstants.MonsterAttackRange * 0.9f)
            {
                var step = GameConstants.WalkSpeed * GameConstants.FixedStep;
                var toward = monster.transform.position;
                toward.y = player.position.y;
                Warp(player, Vector3.MoveTowards(player.position, toward, step));

                director.StepMatch(GameConstants.FixedStep);
                walking += GameConstants.FixedStep;
                yield return null;
            }

            var stand = player!.position;

            // One step before asking. §01's phase flag is recomputed inside StepMatch and
            // it starts true — every match begins with the team at the van — so reading
            // it straight after a warp reports where the player spawned, not where they
            // are. That is a real trap and it caught this test first time out.
            director.StepMatch(GameConstants.FixedStep);
            yield return null;

            var fromEntrance = Flat(stand - director.Map!.Entrance);
            Assert.That(director.LocalPlayerOnSurface, Is.False,
                "the creature is standing inside §01's 지상 apron — " + fromEntrance.ToString("0.0")
                + " m from the 출입구, radius " + MatchMap.SurfaceRadius.ToString("0.0")
                + " m — where the match makes players untouchable on purpose. Anyone standing next "
                + "to it there is safe by design, and that is worth knowing: it is one real answer "
                + "to 괴물앞에있어도 안죽는데.");

            // ── Run the match and watch ─────────────────────────────────────────
            var trace = new StringBuilder();
            var elapsed = 0f;
            var everChased = false;
            var everCommitted = false;
            var closest = float.MaxValue;
            var lastLogged = -1f;

            while (elapsed < BudgetSeconds && state.PlayerAt(director.LocalPlayerIndex).Ghost == null)
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
                                     + "  " + gap.ToString("0.00") + " m");
                }

                yield return null;
            }

            var died = state.PlayerAt(director.LocalPlayerIndex).Ghost != null;

            // ── If it did not happen, say which link was open ───────────────────
            var why = new StringBuilder();
            why.AppendLine("서서 " + BudgetSeconds.ToString("0") + "초를 기다렸는데 죽지 않았다.");
            why.AppendLine("가장 가까웠던 거리 " + closest.ToString("0.00") + " m, 공격 사거리는 "
                           + GameConstants.MonsterAttackRange.ToString("0.0") + " m.");
            why.AppendLine();

            if (!everChased)
            {
                why.AppendLine("§06 추격에 한 번도 들어가지 않았다 — 괴물이 눈앞의 사람을 못 본 것이다.");
                why.AppendLine("MonsterBrain의 시야 판정, NavMeshWorldProbe의 _sightBlockers 레이어, "
                               + "또는 씬의 NavMesh 중 하나가 원인이다. 덮치기는 애초에 평가되지 않는다.");
            }
            else if (!everCommitted)
            {
                why.AppendLine("추격에는 들어갔지만 덮치기가 한 번도 커밋되지 않았다 — MonsterLunge.Tick이 "
                               + "거리를 사거리 안으로 보지 못했다. 두 거리가 서로 다른 기준점에서 재어지고 있다.");
            }
            else
            {
                why.AppendLine("덮쳤는데 죽지 않았다 — 접촉 판정(MonsterAttackReach)이나 "
                               + "MatchState.TryKill이 거절하고 있다.");
            }

            why.AppendLine();
            why.Append(trace);

            Assert.That(died, Is.True, why.ToString());
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
