#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Math;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Monster;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace HorrorGame.Tests.PlayMode.Monster
{
    /// <summary>
    /// §14's first verification question — "추격이 재밌는가?" — reduced to the part a
    /// machine can answer: <b>is there a chase at all?</b>
    /// <para>
    /// Whether it is fun is a person's judgement. Whether the antagonist closes on you,
    /// gives up where §06 says it gives up, and can be escaped through the geometry §12
    /// says it can be escaped through, is measurable — and until all three are true the
    /// fun question cannot even be asked. B-001 was exactly that state: a fragmented
    /// NavMesh left the monster walking into walls, so §06's aggro numbers, §12's whole
    /// S-corridor argument and the Runner's entire identity were untestable at once.
    /// </para>
    /// <para>
    /// So this suite asserts on distances and clocks, in PlayMode, on the real baked
    /// surface:
    /// </para>
    /// <list type="number">
    /// <item>The monster closes on a runner it has been told about, and arrives —
    /// anywhere on its own storey, because §12-C's 투하구 are one-way and a creature is a
    /// per-storey hazard.</item>
    /// <item>§06's release fires at 12 m + 3 s of cover, and sends the monster to the
    /// <em>last seen</em> position rather than to where the player actually is — the
    /// clause that makes the direction of an escape a strategy.</item>
    /// <item>§12's S-corridor breaks a chase, and — with everything else held fixed —
    /// a single corner in its place does not.</item>
    /// </list>
    /// <para>
    /// <b>Why the tick loop is not a frame loop.</b> Every step here is
    /// <see cref="MonsterAgent.Simulate"/> called by hand at
    /// <see cref="GameConstants.FixedStep"/> with the wall clock ignored, so a hundred
    /// seconds of chase costs no wall time and a slow machine measures the same numbers
    /// as a fast one. Nothing in the monster's path depends on rendering: the brain
    /// walks <see cref="NavMesh.CalculatePath"/> and asks <see cref="Physics.Raycast"/>
    /// for sight, and both answer synchronously.
    /// </para>
    /// <para>
    /// <b>Why §07 is pinned.</b> <see cref="MonsterAgent"/> reads its speed from the
    /// threat curve every step, and tier 0 (초저녁) runs at 4.4 m/s. Every number §06
    /// and §12 are written against is the 4.8 m/s of
    /// <see cref="GameConstants.MonsterBaseSpeed"/>, which is 심야's row — so the clock
    /// is held there and the setup asserts that it really is that row. Measuring the
    /// chase at 4.4 and quoting it against 4.8 would be a quiet lie.
    /// </para>
    /// </summary>
    public sealed class MonsterChaseTests
    {
        // ====================================================================
        // The map the solo playtest is built on.
        // ====================================================================

        /// <summary>
        /// The generated map, with §12's spawn markers and the baked surface B-001 was
        /// about. <c>SoloPlaytest</c> builds the solo scene out of exactly this plus a
        /// player, a monster and a <c>MatchDirector</c>; this suite assembles the same
        /// two participants itself because <c>SoloPlaytest</c> lives in
        /// <c>Assembly-CSharp-Editor</c>, which no assembly definition may reference,
        /// and because a chase test has to <em>place</em> the two rather than accept
        /// wherever a match put them.
        /// </summary>
        private const string MapScenePath = "Assets/Scenes/Map_FirstSketch.unity";

        /// <summary>§13: a reported match replays from its seed. Any fixed value; this one is the playtest's.</summary>
        private const int Seed = 20260731;

        /// <summary>The local player's id, as <c>MatchDirector</c> numbers them.</summary>
        private const int PlayerId = 0;

        // ====================================================================
        // The rig. These mirror SoloPlaytest.SpawnMonster and MatchDirector.
        // ====================================================================

        /// <summary>Monster.fbx measures 2.34 m tall; the agent capsule rounds it. Same value SoloPlaytest uses.</summary>
        private const float MonsterHeight = 2.3f;

        /// <summary>0.93 m wide, so it fits §12's corridors. Same value SoloPlaytest uses.</summary>
        private const float MonsterRadius = 0.5f;

        /// <summary>
        /// Half-width of a player, metres. <c>MatchDirector.MeasureGrabDistance</c> adds
        /// this to the agent radius and calls the sum a catch, so "reached the player"
        /// here means exactly what being caught means in a match.
        /// </summary>
        private const float PlayerRadius = 0.5f;

        /// <summary>
        /// 20 minutes in — §07's 심야 row, the one whose monster speed is
        /// <see cref="GameConstants.MonsterBaseSpeed"/>. Asserted in <see cref="SetUp"/>.
        /// </summary>
        private const float TierPinSeconds = 20f * 60f;

        /// <summary>
        /// Range on the footstep cue the test feeds the monster, metres. Deliberately
        /// map-sized: §04's audibility is a different question with its own tests, and a
        /// chase test that failed because the monster could not hear the player from
        /// across the basement would be reporting on the wrong system.
        /// <para>
        /// A multiple of <see cref="GameConstants.MapExtent"/> rather than the extent
        /// itself: the brain measures a cue's range as <em>walked</em> distance, and a
        /// three-storey basement 100 m across has 140 m walks in it. Set to the extent,
        /// this silently muted the player and the monster patrolled instead of hunting.
        /// </para>
        /// </summary>
        private const float SoundRange = GameConstants.MapExtent * 8f;

        /// <summary>Ceiling on any single simulated hunt, seconds of match time.</summary>
        private const float HuntTimeoutSeconds = 240f;

        /// <summary>
        /// Closest the release test will take aggro from, metres. Far enough that the
        /// sighting is a real one rather than a monster standing on the player, and low
        /// enough that this map's cramped MonsterSpawn has somewhere to offer.
        /// </summary>
        private const float MinimumBaitDistance = 5f;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly StringBuilder _report = new StringBuilder();

        [SetUp]
        public void SetUp()
        {
            _report.Clear();

            var tier = HorrorGame.Core.Threat.ThreatCurve.At(TierPinSeconds);
            Assert.That(tier.MonsterSpeed, Is.EqualTo(GameConstants.MonsterBaseSpeed).Within(0.001f),
                "§07's row at " + TierPinSeconds + "s is no longer the one that runs at §06's "
                + GameConstants.MonsterBaseSpeed + " m/s (it is " + tier.MonsterSpeed + " m/s). Every "
                + "number this suite reports is quoted against §06, so pin a different time rather "
                + "than letting the two drift apart.");
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();

            if (_report.Length > 0)
            {
                Debug.Log(_report.ToString());
            }
        }

        // ====================================================================
        // 1. Is there an antagonist? — B-001's headline.
        // ====================================================================

        /// <summary>
        /// The monster is told where a runner is and hunts them across its own storey,
        /// from its §12 spawn to the furthest place on that floor, on the baked surface.
        /// <para>
        /// Two assertions, and the second is the one B-001 failed. <b>Closing:</b> the
        /// walk still to be done shrinks, sampled every second, so a monster that made
        /// progress and then stalled against a seam is caught rather than averaged out.
        /// <b>Arrival:</b> it ends within <c>MatchDirector</c>'s own grab distance, which
        /// is the only definition of "reached the player" the game itself uses.
        /// </para>
        /// <para>
        /// The player stands still. That is not a soft case — it is the isolating one:
        /// a stationary target removes every variable except whether the navigation
        /// surface joins the two points, which is precisely what was broken.
        /// </para>
        /// <para>
        /// <b>Why the target is no longer a PlayerSpawn.</b> §01's race stacks eight
        /// storeys in one column, starts all twenty runners on the rim of B1, and starts
        /// the creature in the middle of B5. Every <c>PlayerSpawn_</c> marker is therefore
        /// on B1 and the creature is three floors under them, so the co-operative version
        /// of this question — "walk to a player spawn" — has no answer on this map and
        /// failed with <i>no 'PlayerSpawn_' marker has a complete NavMesh path</i>. That is
        /// not the creature being broken; it is the question being stale. §12-B lever 3
        /// (「괴물이 안쪽을 순찰한다」) is written about the ring structure <em>every</em>
        /// storey has, and §12-C makes the 투하구 the only vertical connection and makes it
        /// one-way, so the honest question on a tower is <b>can the creature reach a runner
        /// anywhere on its own floor</b> — which is what a runner descending through B5
        /// actually meets.
        /// </para>
        /// <para>
        /// <b>The storey is asserted, not assumed.</b> The sweep takes the furthest marker
        /// with a complete path, and on a correct tower that can only be a marker on the
        /// creature's own floor. If it ever is not, something has joined two storeys —
        /// almost certainly a <c>NavMeshLink</c> laid over a 투하구, which is B-001's exact
        /// reprise: <c>NavMesh.CalculatePath</c> routes through a link and
        /// <c>MonsterBrain</c> walks <c>NavMeshPath.corners</c>, so a link turns the
        /// connectivity audit green without the creature ever being able to make the drop
        /// a runner has to make. The height check below is the tripwire for that.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator MonsterClosesDistanceAndReachesARunnerAcrossItsOwnStorey()
        {
            yield return LoadMap();

            var monsterSpawn = RequireMarker("MonsterSpawn");
            var runnerAt = FurthestReachableMarker(monsterSpawn);

            // §01's storeys are one-way-connected, so a complete path may not change
            // floor. Measured against the pitch the scene itself declares — see
            // StoreyPitchMetres — rather than against a number written down here.
            var pitch = StoreyPitchMetres();
            var climb = Mathf.Abs(runnerAt.y - monsterSpawn.y);
            if (pitch > 0f)
            {
                Assert.That(climb, Is.LessThan(pitch * 0.5f),
                    "the furthest place the creature can walk to is " + Metres(climb) + " above or below its "
                    + "own storey, and §12-C's 투하구 are one-way holes with no way back up. Something has "
                    + "joined two floors on the NavMesh — a link over a chute is the likely one, and it makes "
                    + "the connectivity audit green while the creature still cannot follow a runner down. "
                    + "The storey pitch this scene declares is " + Metres(pitch) + ".");
            }

            var agent = BuildMonster(monsterSpawn, runnerAt - monsterSpawn);
            var player = runnerAt;

            var opening = PathLength(agent.transform.position, player);
            Assert.That(opening, Is.LessThan(float.PositiveInfinity),
                "There is no complete NavMesh path from the creature's spawn to " + runnerAt
                + ". This is B-001 exactly: the antagonist cannot reach a runner standing on its own "
                + "floor, and every assertion below would be measuring a monster walking into a wall.");

            var grab = MonsterRadius + PlayerRadius;
            var elapsed = 0f;
            var sampleAt = 1f;
            var previousSample = opening;
            var worstRegression = 0f;
            var reachedAt = -1f;
            var enteredChaseAt = -1f;
            var strandedAt = -1f;

            while (elapsed < HuntTimeoutSeconds)
            {
                StepOnce(agent, player, audible: true);
                elapsed += GameConstants.FixedStep;

                if (enteredChaseAt < 0f && agent.State == MonsterStateId.Chase)
                {
                    enteredChaseAt = elapsed;
                }

                if (FlatDistance(agent.transform.position, player) <= grab)
                {
                    reachedAt = elapsed;
                    break;
                }

                if (elapsed < sampleAt)
                {
                    continue;
                }

                sampleAt += 1f;
                var remaining = PathLength(agent.transform.position, player);

                // A monster standing somewhere with no complete path to the player is
                // B-001's exact signature, and it is a different failure from a slow one.
                if (remaining >= float.PositiveInfinity)
                {
                    strandedAt = elapsed;
                    break;
                }

                // Rising is the interesting direction. A monster that is walking its
                // path always has less of it left than it did a second ago; a rise means
                // it turned around, was re-pathed onto a longer route, or stopped and
                // the path was recomputed from a different mesh polygon.
                var regression = remaining - previousSample;
                if (regression > worstRegression)
                {
                    worstRegression = regression;
                }

                previousSample = remaining;

                // A trace rather than only a verdict. "It did not arrive" is the same
                // sentence whether the monster never moved, walked into a seam and
                // stopped, or is simply slow, and those are three different bugs.
                if (Mathf.Abs(elapsed - Mathf.Round(elapsed / 20f) * 20f) < GameConstants.FixedStep)
                {
                    Line("  t=" + Seconds(elapsed) + " " + agent.State
                        + " at " + agent.transform.position.ToString("F1")
                        + " path " + Metres(remaining)
                        + " straight " + Metres(FlatDistance(agent.transform.position, player))
                        + " heading for " + (agent.Brain!.Destination.HasValue
                            ? agent.Brain!.Destination!.Value.ToVector3().ToString("F1")
                            : "nowhere"));
                }
            }

            var closingSpeed = reachedAt > 0f ? (opening - grab) / reachedAt : 0f;

            Line("§14 Q1 — can the creature reach a runner on its own storey at all?");
            Line("  route            " + Metres(opening) + " of NavMesh path, creature spawn → " + runnerAt);
            Line("  straight line    " + Metres(FlatDistance(monsterSpawn, runnerAt)));
            Line("  storey           creature " + monsterSpawn.y.ToString("0.00", CultureInfo.InvariantCulture)
                + " m, runner " + runnerAt.y.ToString("0.00", CultureInfo.InvariantCulture)
                + " m, pitch " + (pitch > 0f ? Metres(pitch) : "no 투하구 on this map"));
            Line("  chase entered    " + (enteredChaseAt >= 0f ? Seconds(enteredChaseAt) : "never"));
            Line("  reached          " + (reachedAt > 0f ? Seconds(reachedAt) : "NOT REACHED in "
                + Seconds(HuntTimeoutSeconds)));
            Line("  closing speed    " + closingSpeed.ToString("0.00", CultureInfo.InvariantCulture)
                + " m/s of route, against §06's " + GameConstants.MonsterBaseSpeed + " m/s of ground speed");
            Line("  worst 1 s rise   " + Metres(worstRegression) + " of route (0 is a monster that never backtracked)");

            if (reachedAt < 0f)
            {
                PostMortem(agent, player);
            }

            Assert.That(strandedAt, Is.LessThan(0f),
                "After " + Seconds(strandedAt) + " of hunting, the monster was standing somewhere with no "
                + "complete NavMesh path to the player at all. That is B-001: the surface is in pieces and "
                + "the walk simply ends.");

            Assert.That(reachedAt, Is.GreaterThan(0f),
                "The monster never got within " + Metres(grab) + " of a stationary player in "
                + Seconds(HuntTimeoutSeconds) + ", starting " + Metres(opening) + " away along a complete "
                + "path. This is B-001: §06's chase and §12's escape geometry both assume the antagonist "
                + "can follow you anywhere on its floor that you can walk. Read the post-mortem "
                + "above — if the path is complete and corner 1 is 0.0 m away, the surface is fine and "
                + "the monster is deadlocked on a duplicated path corner, which is what "
                + "NavMesh.CalculatePath emits at a NavMeshLink.");

            // One second of a 4.8 m/s creature is 4.8 m, so 2 m of rise is a real
            // reversal rather than a path recomputed onto a neighbouring polygon.
            Assert.That(worstRegression, Is.LessThan(2f),
                "The remaining walk grew by " + Metres(worstRegression) + " inside one second. The "
                + "monster is not closing steadily — it is oscillating, which is what a partial path "
                + "against a surface seam looks like from the outside.");

            Assert.That(closingSpeed, Is.GreaterThan(GameConstants.RunSpeed * 0.75f),
                "The monster covered its route at " + closingSpeed.ToString("0.00", CultureInfo.InvariantCulture)
                + " m/s against a ground speed of " + GameConstants.MonsterBaseSpeed + " m/s. It arrives, but "
                + "so slowly that something is repeatedly stopping it.");
        }

        // ====================================================================
        // 2. §06's release — and where it sends the monster.
        // ====================================================================

        /// <summary>
        /// §06's 어그로 해제: 12 m of separation <em>and</em> 3 continuous seconds with the
        /// sight line broken, and then — the clause the whole Runner role turns on — the
        /// monster walks to the <b>last seen position</b>, not to where the player now is.
        /// <para>
        /// §06 spells out the price of that: "주자가 팀 쪽에서 어그로를 끊으면 괴물을 팀에
        /// 배달하는 것". If the monster silently re-acquired the player's true position on
        /// release, the direction of an escape would stop mattering and a whole layer of
        /// §06's strategy would evaporate with no visible symptom. So the test asserts
        /// the destination is the old position and is <em>far</em> from the new one.
        /// </para>
        /// <para>
        /// The chase is started <em>next to</em> the monster rather than across the map,
        /// and deliberately: the release rules and the ability to cross the map are two
        /// different claims, and hanging this measurement off a map-length hunt would let
        /// one broken surface hide the answer to the other question. See
        /// <see cref="MonsterClosesDistanceAndReachesARunnerAcrossItsOwnStorey"/> for that one.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AggroReleaseSendsTheMonsterToTheLastSeenPositionNotThePlayer()
        {
            yield return LoadMap();

            var monsterSpawn = RequireMarker("MonsterSpawn");

            // Built once to borrow its probe — sight is §06's rule and the test must not
            // own a second copy of it — then rebuilt facing the bait it found, because
            // the monster's heading is fixed at construction and gates its vision.
            var scout = BuildMonster(monsterSpawn, Vector3.forward);
            var bait = NearbyVisiblePlace(scout.Probe!, scout.transform.position);
            Discard(scout);

            var agent = BuildMonster(monsterSpawn, bait - monsterSpawn);
            var probe = agent.Probe;
            Assert.That(probe, Is.Not.Null, "MonsterAgent.Initialize did not build a probe.");

            // Walk a real chase up first rather than starting in one. Entering Chase by
            // the §06 route — noise, then 경계, then a sight line — is what makes the
            // 마지막 목격 위치 an actual sighting instead of a value the test wrote.
            var player = bait;
            var elapsed = 0f;
            while (elapsed < 30f && agent.State != MonsterStateId.Chase)
            {
                StepOnce(agent, player, audible: true);
                elapsed += GameConstants.FixedStep;
            }

            Assert.That(agent.State, Is.EqualTo(MonsterStateId.Chase),
                "The monster never took aggro on a player it could see and path to, so §06's release "
                + "cannot be measured at all.");

            var brain = agent.Brain!;
            var seenAt = brain.LastSeenPosition!.Value.ToVector3();
            var sightedFrom = agent.transform.position;

            // Somewhere the monster genuinely cannot see and cannot be next to: §06 needs
            // 12 m, and the hide has to survive the monster walking the length of its
            // memory toward the last sighting, so the search asks for a good deal more.
            var hide = BestHidingPlace(probe!, agent.transform.position);
            player = hide;

            var brokeAt = elapsed;
            var releasedAt = -1f;
            var minimumSeparation = float.PositiveInfinity;
            var everSighted = false;

            while (elapsed - brokeAt < 30f)
            {
                // Silent. The player is hiding, so no footstep cue — and §06 gives Chase
                // no sound edge anyway, so this only keeps the record honest.
                StepOnce(agent, player, audible: false);
                elapsed += GameConstants.FixedStep;

                var separation = FlatDistance(agent.transform.position, player);
                if (separation < minimumSeparation)
                {
                    minimumSeparation = separation;
                }

                if (probe!.HasLineOfSight(agent.transform.position.ToVec3(), player.ToVec3()))
                {
                    everSighted = true;
                }

                if (agent.State == MonsterStateId.Search)
                {
                    releasedAt = elapsed - brokeAt;
                    break;
                }
            }

            var destination = brain.Destination.HasValue ? brain.Destination.Value.ToVector3() : Vector3.zero;
            var toLastSeen = FlatDistance(destination, seenAt);
            var toPlayerNow = FlatDistance(destination, player);

            Line("§06 어그로 해제 — 12 m + 3 s of broken sight");
            Line("  sighted from     " + sightedFrom.ToString("F1") + ", last seen at " + seenAt.ToString("F1"));
            Line("  hid at           " + player.ToString("F1") + ", "
                + Metres(FlatDistance(sightedFrom, player)) + " away, no line of sight");
            Line("  separation       never below " + Metres(minimumSeparation)
                + ", against §06's " + GameConstants.AggroReleaseDistance + " m");
            Line("  sight regained   " + (everSighted ? "YES — the hide leaked" : "no"));
            Line("  released after   " + (releasedAt >= 0f ? Seconds(releasedAt) : "never")
                + ", against §06's " + GameConstants.AggroReleaseLineOfSightBreak + " s");
            Line("  headed for       " + destination.ToString("F1")
                + " — " + Metres(toLastSeen) + " from the last sighting, "
                + Metres(toPlayerNow) + " from where the player actually is");

            Assert.That(everSighted, Is.False,
                "The hiding place leaked a sight line while the monster closed on the last sighting, so "
                + "the 3 s cover clock was restarted by the map rather than by the rules. That makes the "
                + "release time below meaningless.");

            Assert.That(minimumSeparation, Is.GreaterThanOrEqualTo(GameConstants.AggroReleaseDistance),
                "The monster came within " + Metres(minimumSeparation) + " while the player was hidden, "
                + "so §06's distance clause was not continuously satisfied.");

            Assert.That(releasedAt, Is.GreaterThan(0f),
                "The monster never released aggro on a player it could not see, "
                + Metres(minimumSeparation) + " away, in 30 s. §06 gives it "
                + GameConstants.AggroReleaseLineOfSightBreak + " s.");

            // The brain evaluates the clause after adding the step, so the release lands
            // in the first sub-step at or past 3 s and never before it.
            Assert.That(releasedAt, Is.GreaterThanOrEqualTo(GameConstants.AggroReleaseLineOfSightBreak - 0.001f),
                "Aggro released after " + Seconds(releasedAt) + " of broken sight. §06 requires "
                + GameConstants.AggroReleaseLineOfSightBreak + " continuous seconds, and a monster that "
                + "gives up sooner makes §12's 연속 차단 unnecessary — one corner would do.");

            Assert.That(releasedAt, Is.LessThanOrEqualTo(GameConstants.AggroReleaseLineOfSightBreak + 0.5f),
                "Aggro took " + Seconds(releasedAt) + " to release with both §06 clauses satisfied "
                + "throughout. Longer than the 3 s the design promises is a different game to hide in.");

            Assert.That(agent.State, Is.EqualTo(MonsterStateId.Search),
                "§06 sends a released monster to 수색, not anywhere else.");

            // The load-bearing pair. Near the sighting AND far from the player: either
            // one alone can be satisfied by accident when the two happen to be close.
            Assert.That(toLastSeen, Is.LessThan(2f),
                "On release the monster headed for " + destination + ", " + Metres(toLastSeen)
                + " from where it last saw the player. §06: \"어그로가 풀리면 괴물은 마지막으로 본 "
                + "위치로 향한다.\"");

            Assert.That(toPlayerNow, Is.GreaterThanOrEqualTo(GameConstants.AggroReleaseDistance),
                "On release the monster headed for a point " + Metres(toPlayerNow) + " from the player's "
                + "true position — close enough that it is tracking the player rather than the memory. "
                + "That would delete §06's whole trade: \"도망치는 방향이 전략이 되는 지점\".");
        }

        // ====================================================================
        // 3. §12's S-corridor, and the single corner it was invented to replace.
        // ====================================================================

        /// <summary>
        /// §12 ① S자 통로: "구간 L = 10m, 총 20m, 통과 시간 = 20 / 4.8 = 4.2초 ≥ 3초 ✅ …
        /// 10m 구간 2개의 S자 통로 하나면 해제가 성립한다. 이것이 맵의 기본 단위다."
        /// <para>
        /// Built rather than found, because the claim is about geometry and the generated
        /// map contains no S-corridor to point at yet. Two 10 m legs, §12's 2.5 m grid,
        /// walls that actually stop a raycast, a surface baked at runtime, and a Runner
        /// spending §06's sprint down it while §06's monster follows on the real
        /// <see cref="NavMesh"/>.
        /// </para>
        /// <para>
        /// Read this together with <see cref="ASingleCornerDoesNotBreakAChase"/>. That
        /// test is the same rig, the same route length, the same aggro distance and the
        /// same runner — with the second 10 m leg removed. One variable, opposite
        /// outcomes, which is the only way to show the S-corridor did the work.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AnSCorridorOfTwoTenMetreLegsBreaksAChase()
        {
            yield return null;

            var leg = GameConstants.SCorridorLegLength;
            var route = new[]
            {
                new Vector3(0f, 0f, 0f),            // the monster starts here
                new Vector3(Approach, 0f, 0f),      // 모퉁이 1
                new Vector3(Approach, 0f, leg),     // 구간 L — 10 m
                new Vector3(Approach + leg, 0f, leg),        // 구간 L — 10 m
                new Vector3(Approach + leg, 0f, leg + 20f),  // and out
            };

            var run = RunTheGauntlet("S자 통로 (10 m + 10 m)", route);

            Line("§12 ① S자 통로 — two " + leg + " m legs");
            ReportGauntlet(run, route);
            Line("  §12's arithmetic 2 × " + leg + " m / " + GameConstants.MonsterBaseSpeed + " m/s = "
                + (2f * leg / GameConstants.MonsterBaseSpeed).ToString("0.0", CultureInfo.InvariantCulture)
                + " s of traversal, against the " + GameConstants.AggroReleaseLineOfSightBreak
                + " s of cover §06 needs");

            Assert.That(run.Chased, Is.True,
                "The monster never took aggro, so nothing was escaped from.");

            Assert.That(run.ReleasedAt, Is.GreaterThan(0f),
                "The Runner ran §12's own basic map unit — two " + leg + " m legs — with §06's sprint, "
                + "starting " + Metres(GameConstants.RunnerTestAggroStartDistance) + " ahead, and the "
                + "monster did not let go. §12: \"10m 구간 2개의 S자 통로 하나면 해제가 성립한다. "
                + "이것이 맵의 기본 단위다.\" If this fails, every corridor in the game is the wrong "
                + "length. " + run.Ending);

            Assert.That(run.GapAtRelease, Is.GreaterThanOrEqualTo(GameConstants.AggroReleaseDistance),
                "The release fired at " + Metres(run.GapAtRelease) + ", inside §06's "
                + GameConstants.AggroReleaseDistance + " m clause. Both clauses have to hold, so this is "
                + "the brain letting go early rather than the corridor working.");

            Assert.That(run.DestinationToLastSeen, Is.LessThan(2f),
                "Released, but headed for a point " + Metres(run.DestinationToLastSeen) + " from the last "
                + "sighting rather than for the sighting itself (§06).");
        }

        /// <summary>
        /// The control, and §12's headline: "단일 모퉁이는 실패한다."
        /// <para>
        /// Identical rig, identical 62.5 m of route, identical 10 m aggro start, identical
        /// Runner — one corner instead of three, with the two 10 m legs replaced by the
        /// straight they would have bent. §12's arithmetic says the cover this buys is the
        /// time the monster needs to reach the corner, and at this aggro distance that is
        /// short of 3 s.
        /// </para>
        /// <para>
        /// Without this test the S-corridor result proves nothing: a monster that released
        /// on any corner at all would pass it just as happily, and §12's 연속 차단 rule —
        /// which is why every corridor in the map is shaped the way it is — would be
        /// unfalsified.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ASingleCornerDoesNotBreakAChase()
        {
            yield return null;

            var leg = GameConstants.SCorridorLegLength;
            var route = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(Approach, 0f, 0f),                    // the one corner
                new Vector3(Approach, 0f, (2f * leg) + 20f),      // the same distance, straight
            };

            var run = RunTheGauntlet("단일 모퉁이", route);

            Line("§12 — 단일 모퉁이는 실패한다 (the control)");
            ReportGauntlet(run, route);

            Assert.That(run.Chased, Is.True,
                "The monster never took aggro, so this control says nothing about the corner.");

            Assert.That(run.ReleasedAt, Is.LessThan(0f),
                "A single corner broke the chase after " + Seconds(run.ReleasedAt) + ". §12 is built on "
                + "this being impossible — \"단일 모퉁이에 의존하면 안 된다 — 안정적 해제에는 연속 차단이 "
                + "필요하다\" — and the S-corridor test above becomes vacuous if any corner will do. "
                + "Either §06's cover window shrank or the corridor rig is leaking sight.");
        }

        // ====================================================================
        // The gauntlet rig: a corridor, a Runner and a monster.
        // ====================================================================

        /// <summary>
        /// Straight run before the first corner, metres. It has to hold the monster's
        /// start, the Runner's <see cref="GameConstants.RunnerTestAggroStartDistance"/>
        /// head start and enough room to establish the chase, and it is a multiple of
        /// §12's 2.5 m grid so the corridor rasterises onto cell centres.
        /// </summary>
        private const float Approach = 22.5f;

        /// <summary>§12's grid. The corridor is one cell wide, which clears the 0.5 m agent radius twice over.</summary>
        private const float Cell = 2.5f;

        /// <summary>Wall height, metres. Well above the monster's 2.3 m so no sight line goes over one.</summary>
        private const float WallHeight = 3f;

        /// <summary>
        /// Where the corridor is built. Far outside <see cref="GameConstants.MapExtent"/>,
        /// so a map scene left loaded by another test cannot contribute geometry, a
        /// NavMesh polygon or a raycast hit to the measurement.
        /// </summary>
        private static readonly Vector3 RigOrigin = new Vector3(5000f, 0f, 5000f);

        private readonly struct Gauntlet
        {
            public Gauntlet(bool chased, float releasedAt, float gapAtRelease, float gapAtFirstCorner,
                            float caughtAt, float destinationToLastSeen, float runnerTravelled,
                            int sightRegains, float longestCover, float monsterTravelled,
                            float monsterGroundSpeed, float separationRate, string ending)
            {
                MonsterGroundSpeed = monsterGroundSpeed;
                SeparationRate = separationRate;
                Chased = chased;
                ReleasedAt = releasedAt;
                GapAtRelease = gapAtRelease;
                GapAtFirstCorner = gapAtFirstCorner;
                CaughtAt = caughtAt;
                DestinationToLastSeen = destinationToLastSeen;
                RunnerTravelled = runnerTravelled;
                SightRegains = sightRegains;
                LongestCover = longestCover;
                MonsterTravelled = monsterTravelled;
                Ending = ending;
            }

            /// <summary>
            /// How many times the monster got its sight line back after having lost it
            /// for a moment. §12's single-corner arithmetic turns on this happening as
            /// the monster rounds the corner; a zero here means it never looked.
            /// </summary>
            public int SightRegains { get; }

            /// <summary>Longest unbroken stretch of cover the Runner bought, seconds. §06 needs 3.</summary>
            public float LongestCover { get; }

            /// <summary>Metres of corridor the monster covered, for comparison with the Runner's.</summary>
            public float MonsterTravelled { get; }

            /// <summary>
            /// Metres of corridor the monster covered per second of chase — §06's
            /// <see cref="GameConstants.MonsterBaseSpeed"/> as the ground actually
            /// delivered it, rather than as the tier table promises it.
            /// </summary>
            public float MonsterGroundSpeed { get; }

            /// <summary>
            /// Metres per second the gap opened while the Runner was sprinting and still
            /// had corridor left. §06's whole speed ladder is the claim that this is
            /// <see cref="GameConstants.RunnerSprintSpeed"/> − 4.8 = 0.8, and it is the
            /// one number the design says the game is balanced on.
            /// </summary>
            public float SeparationRate { get; }

            /// <summary>Whether the monster ever entered §06's 추격.</summary>
            public bool Chased { get; }

            /// <summary>Seconds from the chase starting to §06's release, or -1 if it never came.</summary>
            public float ReleasedAt { get; }

            /// <summary>Runner-to-monster separation at the release, metres.</summary>
            public float GapAtRelease { get; }

            /// <summary>Separation as the Runner rounded the first corner — §12's own table column.</summary>
            public float GapAtFirstCorner { get; }

            /// <summary>Seconds until the monster got within grab distance, or -1.</summary>
            public float CaughtAt { get; }

            /// <summary>Metres between the released monster's destination and the last sighting.</summary>
            public float DestinationToLastSeen { get; }

            /// <summary>Metres of corridor the Runner covered.</summary>
            public float RunnerTravelled { get; }

            /// <summary>How the attempt ended, in words.</summary>
            public string Ending { get; }
        }

        /// <summary>
        /// Builds a corridor along <paramref name="route"/>, starts the monster at its
        /// mouth with the Runner <see cref="GameConstants.RunnerTestAggroStartDistance"/>
        /// ahead, and runs §06 until somebody wins.
        /// <para>
        /// The Runner is §06's, not an idealisation: sprint at
        /// <see cref="GameConstants.RunnerSprintSpeed"/> until
        /// <see cref="GameConstants.SprintStaminaSeconds"/> is spent, then
        /// <see cref="GameConstants.RunSpeed"/> — which is slower than the monster, so a
        /// route that has not delivered the release by then never will. That is §06's
        /// "주자도 스태미나가 끝나면 잡힌다", and it is what stops a long enough straight
        /// corridor from passing this test on distance alone.
        /// </para>
        /// </summary>
        private Gauntlet RunTheGauntlet(string label, Vector3[] route)
        {
            var arc = ArcLengths(route);
            BuildCorridor(label, route);

            var start = PointAlong(route, arc, 0f);
            var facing = PointAlong(route, arc, 1f) - start;
            var agent = BuildMonster(start, facing);

            var runnerArc = GameConstants.RunnerTestAggroStartDistance;
            var total = arc[arc.Length - 1];
            var grab = MonsterRadius + PlayerRadius;
            var firstCorner = arc.Length > 1 ? arc[1] : total;

            var elapsed = 0f;
            var chasedAt = -1f;
            var releasedAt = -1f;
            var caughtAt = -1f;
            var gapAtRelease = 0f;
            var gapAtFirstCorner = -1f;
            var destinationToLastSeen = float.PositiveInfinity;
            var lastSeen = Vector3.zero;
            var ending = "ran out of simulated time";
            var sightRegains = 0;
            var blindReports = 0;
            var longestCover = 0f;
            var previousCover = 0f;
            var monsterStart = agent.transform.position;
            var monsterTravelled = 0f;
            var monsterWas = monsterStart;
            var arcGapAtChase = -1f;
            var arcGapAtSprintEnd = 0f;
            var sprintSeconds = 0f;

            while (elapsed < 60f)
            {
                var player = PointAlong(route, arc, runnerArc);
                StepOnce(agent, player, audible: true);
                elapsed += GameConstants.FixedStep;

                var brain = agent.Brain!;
                monsterTravelled += Vector3.Distance(monsterWas, agent.transform.position);
                monsterWas = agent.transform.position;

                // §06 zeroes this the instant sight comes back, which is what makes two
                // 2 s hides worth nothing — so a fall from a real value to zero is the
                // monster looking round the corner, and the peak is the cover the
                // corridor actually bought.
                var cover = brain.LineOfSightBrokenSeconds;
                if (cover > longestCover)
                {
                    longestCover = cover;
                }

                if (previousCover > 0.25f && cover <= 0f && agent.State == MonsterStateId.Chase)
                {
                    sightRegains++;
                }

                if (previousCover <= 0f && cover > 0f && agent.State == MonsterStateId.Chase && blindReports < 3)
                {
                    blindReports++;
                    WhyBlind(agent, player);
                }

                previousCover = cover;

                if (brain.LastSeenPosition.HasValue && agent.State == MonsterStateId.Chase)
                {
                    lastSeen = brain.LastSeenPosition.Value.ToVector3();
                }

                // Half-second trace. "The chase broke" is the same sentence whether the
                // corridor hid the Runner or the monster stopped walking, and §12's whole
                // argument is about which.
                if (Mathf.Abs(elapsed - (Mathf.Round(elapsed / 0.5f) * 0.5f)) < GameConstants.FixedStep * 0.5f)
                {
                    Line("    t=" + Seconds(elapsed) + " " + agent.State
                        + " monster " + Metres(monsterTravelled) + " along"
                        + ", runner " + Metres(runnerArc)
                        + ", gap " + Metres(FlatDistance(agent.transform.position, player))
                        + ", cover " + Seconds(cover));
                }

                if (chasedAt < 0f && agent.State == MonsterStateId.Chase)
                {
                    chasedAt = elapsed;
                }

                // §06's sprint, spent from the moment aggro lands. Before that the Runner
                // has no reason to be running at all.
                if (chasedAt >= 0f)
                {
                    var sprintFor = elapsed - chasedAt;
                    var speed = sprintFor <= GameConstants.SprintStaminaSeconds
                        ? GameConstants.RunnerSprintSpeed
                        : GameConstants.RunSpeed;
                    runnerArc = Mathf.Min(total, runnerArc + (speed * GameConstants.FixedStep));
                }

                var gap = FlatDistance(agent.transform.position, player);

                if (gapAtFirstCorner < 0f && runnerArc >= firstCorner)
                {
                    gapAtFirstCorner = gap;
                }

                // Along the corridor, not through the wall. §06's 0.8 m/s is a claim
                // about ground covered, and the flat gap collapses it every time the
                // route bends — which on an S-corridor is most of the interesting part.
                if (chasedAt >= 0f)
                {
                    if (arcGapAtChase < 0f)
                    {
                        arcGapAtChase = runnerArc - monsterTravelled;
                    }

                    // Only while the Runner is spending §06's sprint and has corridor
                    // left. After either runs out the gap closes by design, and averaging
                    // that in would report the sprint as slower than it is.
                    if (elapsed - chasedAt <= GameConstants.SprintStaminaSeconds
                        && runnerArc < total - 0.001f)
                    {
                        sprintSeconds = elapsed - chasedAt;
                        arcGapAtSprintEnd = runnerArc - monsterTravelled;
                    }
                }

                if (chasedAt >= 0f && agent.State == MonsterStateId.Search)
                {
                    releasedAt = elapsed - chasedAt;
                    gapAtRelease = gap;
                    destinationToLastSeen = agent.Brain!.Destination.HasValue
                        ? FlatDistance(agent.Brain!.Destination!.Value.ToVector3(), lastSeen)
                        : float.PositiveInfinity;
                    ending = "released at " + Seconds(releasedAt) + " with " + Metres(gap) + " between them";
                    break;
                }

                if (gap <= grab)
                {
                    caughtAt = elapsed;
                    ending = "caught at " + Seconds(elapsed) + " after "
                        + Metres(runnerArc - GameConstants.RunnerTestAggroStartDistance) + " of running";
                    break;
                }

                if (runnerArc >= total - 0.001f && elapsed - chasedAt > 20f)
                {
                    ending = "the Runner reached the end of the corridor and was still being chased";
                    break;
                }
            }

            var chaseSeconds = chasedAt >= 0f ? elapsed - chasedAt : 0f;

            return new Gauntlet(
                chasedAt >= 0f, releasedAt, gapAtRelease, gapAtFirstCorner, caughtAt,
                destinationToLastSeen, runnerArc - GameConstants.RunnerTestAggroStartDistance,
                sightRegains, longestCover, monsterTravelled,
                chaseSeconds > 0f ? monsterTravelled / chaseSeconds : 0f,
                sprintSeconds > 0f ? (arcGapAtSprintEnd - arcGapAtChase) / sprintSeconds : 0f,
                ending);
        }

        private void ReportGauntlet(Gauntlet run, Vector3[] route)
        {
            var arc = ArcLengths(route);
            Line("  corridor         " + Metres(arc[arc.Length - 1]) + " of route, "
                + (route.Length - 2) + " corner(s), " + Cell + " m clear width");
            Line("  aggro started at " + Metres(GameConstants.RunnerTestAggroStartDistance)
                + " (§12's endorsed row)");
            Line("  gap at corner 1  " + (run.GapAtFirstCorner >= 0f ? Metres(run.GapAtFirstCorner) : "never reached")
                + ", against §12's 14.4 m for a single corner to hold 3 s");
            Line("  released         " + (run.ReleasedAt > 0f
                ? Seconds(run.ReleasedAt) + " after aggro, at " + Metres(run.GapAtRelease)
                : "NO"));
            Line("  caught           " + (run.CaughtAt > 0f ? Seconds(run.CaughtAt) : "no"));
            Line("  longest cover    " + Seconds(run.LongestCover) + " unbroken, against §06's "
                + GameConstants.AggroReleaseLineOfSightBreak + " s");
            Line("  sight regained   " + run.SightRegains + " time(s) mid-chase — §12's single-corner "
                + "arithmetic assumes the monster gets its eyes back at the corner");
            Line("  runner covered   " + Metres(run.RunnerTravelled)
                + ", monster " + Metres(run.MonsterTravelled));
            Line("  monster speed    " + Speed(run.MonsterGroundSpeed)
                + " of corridor, against §06's " + GameConstants.MonsterBaseSpeed + " m/s");
            Line("  gap opened at    " + Speed(run.SeparationRate)
                + " while sprinting, against §06's "
                + (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed).ToString("0.0#",
                    CultureInfo.InvariantCulture) + " m/s ("
                + GameConstants.RunnerSprintSpeed + " − " + GameConstants.MonsterBaseSpeed + ")");
            Line("  ending           " + run.Ending);
        }

        /// <summary>
        /// Rasterises the route onto §12's 2.5 m grid and builds a floor for every cell
        /// it touches and a wall on every edge it does not.
        /// <para>
        /// Boxes with colliders, not a mesh: sight in §06 is
        /// <see cref="Physics.Raycast"/> and walking is a baked surface, so a corridor
        /// that is not made of colliders is not a corridor to either of them. The bake
        /// collects children only, which is what keeps a map scene left loaded by another
        /// test out of the result.
        /// </para>
        /// </summary>
        private void BuildCorridor(string label, Vector3[] route)
        {
            var root = new GameObject("[Corridor] " + label);
            _spawned.Add(root);
            root.transform.position = Vector3.zero;

            var cells = new HashSet<Vector2Int>();
            var arc = ArcLengths(route);
            for (var d = 0f; d <= arc[arc.Length - 1]; d += 0.2f)
            {
                cells.Add(CellOf(PointAlong(route, arc, d)));
            }

            var neighbours = new[]
            {
                new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            };

            foreach (var cell in cells)
            {
                var centre = CentreOf(cell);
                Box(root, "Floor " + cell.x + "," + cell.y,
                    centre + (Vector3.down * 0.1f), new Vector3(Cell, 0.2f, Cell));

                for (var i = 0; i < neighbours.Length; i++)
                {
                    var side = cell + neighbours[i];
                    if (cells.Contains(side))
                    {
                        continue;
                    }

                    var offset = new Vector3(neighbours[i].x, 0f, neighbours[i].y) * (Cell * 0.5f);
                    var size = neighbours[i].x != 0
                        ? new Vector3(0.2f, WallHeight, Cell)
                        : new Vector3(Cell, WallHeight, 0.2f);

                    Box(root, "Wall " + cell.x + "," + cell.y + " " + i,
                        centre + offset + (Vector3.up * (WallHeight * 0.5f)), size);
                }
            }

            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.BuildNavMesh();

            // Colliders created this frame are not in the physics broadphase until the
            // next fixed step, and the whole simulation below runs inside this one.
            Physics.SyncTransforms();

            var mouth = PointAlong(route, arc, 0f);
            Assert.That(NavMesh.SamplePosition(mouth, out _, MonsterHeight, NavMesh.AllAreas), Is.True,
                "The runtime bake produced no navigable surface at the mouth of the " + label
                + " corridor, so there is nothing for §06's monster to walk on.");
        }

        private static void Box(GameObject root, string name, Vector3 centre, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root.transform, worldPositionStays: true);
            box.transform.position = centre;
            box.transform.localScale = size;
        }

        /// <summary>
        /// The cell a point falls in. Rounded, not floored, so that a route point lands
        /// on a cell <em>centre</em> — the route is the corridor's centre line, and
        /// flooring put it on the cell corner instead, which is where the walls are.
        /// The Runner then ran the whole gauntlet embedded in the south wall.
        /// </summary>
        private static Vector2Int CellOf(Vector3 point)
        {
            return new Vector2Int(
                Mathf.RoundToInt((point.x - RigOrigin.x) / Cell),
                Mathf.RoundToInt((point.z - RigOrigin.z) / Cell));
        }

        private static Vector3 CentreOf(Vector2Int cell)
        {
            return RigOrigin + new Vector3(cell.x * Cell, 0f, cell.y * Cell);
        }

        private static float[] ArcLengths(Vector3[] route)
        {
            var arc = new float[route.Length];
            for (var i = 1; i < route.Length; i++)
            {
                arc[i] = arc[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            }

            return arc;
        }

        /// <summary>A point <paramref name="distance"/> metres along the route, in world space.</summary>
        private static Vector3 PointAlong(Vector3[] route, float[] arc, float distance)
        {
            var total = arc[arc.Length - 1];
            var d = Mathf.Clamp(distance, 0f, total);

            for (var i = 1; i < route.Length; i++)
            {
                if (d > arc[i])
                {
                    continue;
                }

                var span = arc[i] - arc[i - 1];
                var t = span > 0f ? (d - arc[i - 1]) / span : 0f;
                return RigOrigin + Vector3.Lerp(route[i - 1], route[i], t);
            }

            return RigOrigin + route[route.Length - 1];
        }

        // ====================================================================
        // Shared rig.
        // ====================================================================

        /// <summary>
        /// One <see cref="GameConstants.FixedStep"/> of §06, with the world told to the
        /// monster the way a host tells it: the player's true position, and a footstep.
        /// §06's perception decides what it does with them.
        /// </summary>
        private static void StepOnce(MonsterAgent agent, Vector3 playerPosition, bool audible)
        {
            agent.SetMatchElapsedSeconds(TierPinSeconds);
            agent.ReportTarget(PlayerId, playerPosition);
            if (audible)
            {
                agent.ReportSound(playerPosition, SoundRange);
            }

            agent.Simulate(GameConstants.FixedStep);
        }

        /// <summary>
        /// The monster as <c>SoloPlaytest</c> spawns it, minus the art: the same agent
        /// dimensions, the same snap onto the surface, and the brain driven by this test
        /// instead of by <c>FixedUpdate</c>.
        /// </summary>
        private MonsterAgent BuildMonster(Vector3 position, Vector3 facing)
        {
            var body = new GameObject("Monster (chase test)");
            _spawned.Add(body);
            body.transform.position = position;

            var flat = new Vector3(facing.x, 0f, facing.z);
            if (flat.sqrMagnitude > 0f)
            {
                body.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            }

            var nav = body.AddComponent<NavMeshAgent>();
            nav.height = MonsterHeight;
            nav.radius = MonsterRadius;
            nav.speed = GameConstants.MonsterBaseSpeed;
            nav.angularSpeed = 240f;
            nav.acceleration = 12f;
            nav.stoppingDistance = 0.3f;

            if (NavMesh.SamplePosition(position, out var hit, 8f, NavMesh.AllAreas))
            {
                body.transform.position = hit.position;
            }

            var agent = body.AddComponent<MonsterAgent>();

            // One loop drives everything, in a defined order — the same arrangement
            // MatchDirector uses, and the reason a hundred seconds of chase costs no
            // wall-clock time here.
            agent.SelfDriven = false;
            agent.Initialize(Seed);
            return agent;
        }

        private IEnumerator LoadMap()
        {
#if UNITY_EDITOR
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode(
                MapScenePath, new LoadSceneParameters(LoadSceneMode.Single));

            Assert.That(load, Is.Not.Null,
                "Could not load " + MapScenePath + ". Generate it with "
                + "HorrorGame ▸ Scene Gen ▸ Generate First Map.");

            while (!load!.isDone)
            {
                yield return null;
            }

            // A frame for Awake and the NavMesh data to be live.
            yield return null;
            Physics.SyncTransforms();
#else
            Assert.Ignore("The map scene is loaded through the editor scene manager, so this suite "
                + "measures §06 in the editor's play mode rather than in a built player.");
            yield break;
#endif
        }

        /// <summary>
        /// Asks, at the exact spot the monster gave up, the four questions that separate
        /// the ways a hunt can die.
        /// <para>
        /// This exists because a stalled monster and an unreachable player produce the
        /// same sentence and are different bugs, and because the NavMesh audit and the
        /// monster do not ask the surface the same question. The audit snaps a
        /// <em>marker</em> onto the mesh with a 4 m radius and paths from there; the
        /// monster snaps <em>wherever it has walked to</em> with a body-height radius and
        /// paths from there. A surface that is whole by the first measure can still strand
        /// the second, and this is the comparison that says which happened.
        /// </para>
        /// </summary>
        private void PostMortem(MonsterAgent agent, Vector3 player)
        {
            var at = agent.transform.position;
            var probe = agent.Probe!;

            var snapped = NavMesh.SamplePosition(at, out var hit, MonsterHeight, NavMesh.AllAreas);
            var raw = new NavMeshPath();
            var fromSnapped = new NavMeshPath();

            NavMesh.CalculatePath(at, player, NavMesh.AllAreas, raw);
            if (snapped)
            {
                NavMesh.CalculatePath(hit.position, player, NavMesh.AllAreas, fromSnapped);
            }

            var destination = agent.Brain!.Destination;
            var probeDistance = destination.HasValue
                ? probe.NavigableDistance(at.ToVec3(), destination.Value)
                : float.NaN;

            Line("  post-mortem — where the hunt actually died");
            Line("    stopped at     " + at.ToString("F2") + ", state " + agent.State);
            Line("    snapped to     " + (snapped
                ? hit.position.ToString("F2") + " (" + Metres(Vector3.Distance(at, hit.position)) + " away)"
                : "NOTHING within " + Metres(MonsterHeight) + " — it is standing off the surface"));
            Line("    path from raw  " + raw.status + "   ← what the audit's kind of query sees");
            Line("    path from snap " + (snapped ? fromSnapped.status.ToString() : "n/a")
                + "   ← what the monster's own probe sees");
            Line("    probe distance " + (destination.HasValue
                ? (float.IsInfinity(probeDistance) ? "UNREACHABLE" : Metres(probeDistance))
                : "no destination")
                + " to " + (destination.HasValue ? destination.Value.ToVector3().ToString("F2") : "-"));
            Line("    the two disagreeing is B-001 surviving the audit: the surface joins the "
                + "markers, and does not join where the monster walked to.");

            // The corners are the thing MonsterBrain actually walks. It only ever asks
            // for corner 1, so if corner 1 is the point it is already standing on, it
            // steps onto itself forever and no amount of complete path helps.
            if (snapped && destination.HasValue)
            {
                var walked = new NavMeshPath();
                NavMesh.CalculatePath(hit.position, destination.Value.ToVector3(), NavMesh.AllAreas, walked);
                var corners = walked.corners;
                Line("    corners        " + corners.Length + ", " + walked.status);
                for (var i = 0; i < corners.Length && i < 4; i++)
                {
                    Line("      [" + i + "] " + corners[i].ToString("F2")
                        + "  " + Metres(Vector3.Distance(at, corners[i])) + " from the monster"
                        + (i == 1 ? "   ← the only one MonsterBrain ever asks for" : string.Empty));
                }
            }
        }

        /// <summary>
        /// Why the monster cannot see something it is standing a few metres from — the
        /// question the trace raises and cannot answer.
        /// <para>
        /// Re-casts §06's own sight ray with §06's own numbers and names what stopped it,
        /// and asks the probe for the walk at the same instant. Between them, "it went
        /// blind" and "it lost the path" stop being the same observation.
        /// </para>
        /// </summary>
        private void WhyBlind(MonsterAgent agent, Vector3 player)
        {
            var at = agent.transform.position;
            var eye = at + (Vector3.up * MonsterHeight);
            var chest = player + Vector3.up;

            var delta = chest - eye;
            var distance = delta.magnitude;
            var direction = delta / distance;
            var span = distance - (MonsterRadius * 2f);

            var blocked = Physics.Raycast(
                eye + (direction * MonsterRadius), direction, out var hit, span, ~0,
                QueryTriggerInteraction.Ignore);

            var walk = agent.Probe!.NavigableDistance(at.ToVec3(), player.ToVec3());
            var hasNext = agent.Probe!.TryGetNextPathPoint(at.ToVec3(), player.ToVec3(), out var next);

            Line("    sight lost — monster " + at.ToString("F2") + " eye " + eye.ToString("F2")
                + ", player " + player.ToString("F2") + " chest " + chest.ToString("F2"));
            Line("      ray " + Metres(span) + " blocked by "
                + (blocked ? hit.collider.name + " at " + hit.point.ToString("F2") : "NOTHING"));
            Line("      walk " + (float.IsInfinity(walk) ? "UNREACHABLE" : Metres(walk))
                + ", next point " + (hasNext ? next.ToVector3().ToString("F2")
                    + " (" + Metres(Vector3.Distance(at, next.ToVector3())) + " away)" : "NONE"));
        }

        // ====================================================================
        // Finding places on a map nobody hand-placed.
        // ====================================================================

        private static Vector3 RequireMarker(string name)
        {
            var found = Markers(name);
            Assert.That(found.Count, Is.GreaterThan(0),
                "No '" + name + "' marker in " + MapScenePath + ". §12's spawn markers are what the "
                + "chase is measured between.");

            return found[0];
        }

        /// <summary>
        /// The gameplay marker that is furthest from <paramref name="from"/> by NavMesh
        /// path — the longest honest hunt the map offers, and therefore the one most likely
        /// to cross whatever seam is left in the bake.
        /// <para>
        /// No name filter. On §01's tower the reachability test <em>is</em> the storey
        /// filter: the 투하구 are one-way holes and not NavMesh links, so the set of markers
        /// with a complete path from the creature is exactly the set on its own floor.
        /// Asking for a particular kind of marker on top of that was what broke this test —
        /// every <c>PlayerSpawn_</c> is on B1 and the creature is on B5, so the filter and
        /// the map disagreed and the disagreement read as B-001.
        /// </para>
        /// </summary>
        private static Vector3 FurthestReachableMarker(Vector3 from)
        {
            var best = Vector3.zero;
            var bestLength = -1f;
            var considered = 0;

            foreach (var candidate in Markers(string.Empty))
            {
                considered++;
                var length = PathLength(from, candidate);
                if (length < float.PositiveInfinity && length > bestLength)
                {
                    bestLength = length;
                    best = candidate;
                }
            }

            Assert.That(bestLength, Is.GreaterThan(0f),
                "None of the " + considered + " gameplay markers in " + MapScenePath + " has a complete "
                + "NavMesh path from " + from + ". That is B-001: the creature's spawn is on its own island, "
                + "and on a per-storey design that means its whole floor is unwalkable rather than merely "
                + "cut off from the others.");

            return best;
        }

        /// <summary>
        /// The height of one storey, read off the scene rather than written down.
        /// <para>
        /// The 투하구 mouths are laid one per storey down the same column, so the smallest
        /// gap between two distinct mouth heights is the storey pitch — which makes this a
        /// measurement of the building under test rather than a copy of
        /// <c>MapKitCatalogue.StoreyMetres</c>, a constant this assembly cannot reference
        /// (it is editor-only) and should not duplicate.
        /// </para>
        /// <para>
        /// Returns 0 on a map with no 투하구, i.e. one that is not a §01 tower at all, and
        /// the caller skips the storey check rather than inventing a pitch for it.
        /// </para>
        /// </summary>
        private static float StoreyPitchMetres()
        {
            var heights = new List<float>();

            for (var s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                    {
                        if (!t.name.StartsWith("투하구 ", System.StringComparison.Ordinal)
                            || t.name.EndsWith(" 착지", System.StringComparison.Ordinal))
                        {
                            continue;
                        }

                        heights.Add(t.position.y);
                    }
                }
            }

            heights.Sort();

            var pitch = float.PositiveInfinity;
            for (var i = 1; i < heights.Count; i++)
            {
                var gap = heights[i] - heights[i - 1];

                // Two mouths on the SAME storey are the pair §01 puts either side of the
                // middle; they are the same height and must not be read as a pitch.
                if (gap > 0.01f && gap < pitch)
                {
                    pitch = gap;
                }
            }

            return float.IsInfinity(pitch) ? 0f : pitch;
        }

        /// <summary>
        /// Somewhere within sight of the monster to take aggro from: on the surface,
        /// walkable, and as far off as the map allows short of §06's sight range.
        /// <para>
        /// Swept rather than written down. The map is regenerated and a checked-in
        /// coordinate would be inside a wall by the next sketch, and the sweep asks the
        /// monster's own probe whether it can see the spot rather than deciding for it.
        /// </para>
        /// <para>
        /// The floor is <see cref="MinimumBaitDistance"/> rather than §12's endorsed 10 m,
        /// because it turned out that nothing 10 m from this map's MonsterSpawn is both
        /// walkable and visible from it — a fact about where that spawn sits, and not the
        /// question this test is asking. §12's aggro distance is measured properly in the
        /// corridor gauntlet, where the geometry is built rather than found.
        /// </para>
        /// </summary>
        private static Vector3 NearbyVisiblePlace(NavMeshWorldProbe probe, Vector3 monster)
        {
            var best = Vector3.zero;
            var bestDistance = -1f;

            for (var radius = MinimumBaitDistance; radius <= 18f; radius += 1f)
            {
                for (var degrees = 0f; degrees < 360f; degrees += 5f)
                {
                    var guess = monster + (Quaternion.Euler(0f, degrees, 0f) * Vector3.forward * radius);
                    if (!NavMesh.SamplePosition(guess, out var hit, 1.5f, NavMesh.AllAreas))
                    {
                        continue;
                    }

                    var distance = FlatDistance(monster, hit.position);
                    if (distance < MinimumBaitDistance
                        || distance > GameConstants.MonsterSightRange - 2f)
                    {
                        continue;
                    }

                    if (PathLength(monster, hit.position) >= float.PositiveInfinity)
                    {
                        continue;
                    }

                    if (!probe.HasLineOfSight(monster.ToVec3(), hit.position.ToVec3()))
                    {
                        continue;
                    }

                    if (distance > bestDistance)
                    {
                        bestDistance = distance;
                        best = hit.position;
                    }
                }
            }

            Assert.That(bestDistance, Is.GreaterThan(0f),
                "Nowhere between " + MinimumBaitDistance + " m and §06's "
                + GameConstants.MonsterSightRange + " m sight range is both walkable from the monster "
                + "spawn and visible from it, so no chase can be started on this map at all.");

            return best;
        }

        /// <summary>Removes a monster this test built, so the next one starts clean.</summary>
        private void Discard(MonsterAgent agent)
        {
            _spawned.Remove(agent.gameObject);
            Object.DestroyImmediate(agent.gameObject);
        }

        /// <summary>
        /// A place the monster can neither see nor be near: on the surface, past §06's
        /// release distance several times over, with no line of sight.
        /// <para>
        /// Chosen by measurement rather than written down, because the map is regenerated
        /// and a checked-in coordinate would be inside a wall by the next sketch. Height
        /// separation is preferred — this is a three-storey basement, and a different
        /// storey is the one hiding place a monster walking toward the last sighting
        /// cannot accidentally get a sight line into.
        /// </para>
        /// </summary>
        private static Vector3 BestHidingPlace(NavMeshWorldProbe probe, Vector3 monster)
        {
            var best = Vector3.zero;
            var bestScore = -1f;

            foreach (var candidate in Markers(string.Empty))
            {
                var straight = FlatDistance(monster, candidate);
                if (straight < GameConstants.AggroReleaseDistance * 2.5f)
                {
                    continue;
                }

                if (PathLength(monster, candidate) >= float.PositiveInfinity)
                {
                    continue;
                }

                if (probe.HasLineOfSight(monster.ToVec3(), candidate.ToVec3()))
                {
                    continue;
                }

                var score = straight + (Mathf.Abs(candidate.y - monster.y) * 10f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            Assert.That(bestScore, Is.GreaterThan(0f),
                "Nowhere on this map is both reachable and out of sight of " + monster
                + ", so §06's release cannot be measured on it.");

            return best;
        }

        /// <summary>
        /// Leaf transforms whose name carries <paramref name="prefix"/>, snapped onto the
        /// surface. Leaves only: the generator parents markers under containers whose own
        /// names start the same way and which sit at the world origin.
        /// </summary>
        private static List<Vector3> Markers(string prefix)
        {
            var found = new List<Vector3>();

            for (var s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                    {
                        if (t.childCount != 0)
                        {
                            continue;
                        }

                        if (prefix.Length > 0
                            && t.name.IndexOf(prefix, System.StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        if (prefix.Length == 0 && !IsGameplayMarker(t.name))
                        {
                            continue;
                        }

                        if (NavMesh.SamplePosition(t.position, out var hit, 4f, NavMesh.AllAreas))
                        {
                            found.Add(hit.position);
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>The same set of names <c>NavMeshConnectivity</c> audits, and for the same reason.</summary>
        private static bool IsGameplayMarker(string name)
        {
            string[] kinds = { "PlayerSpawn", "MonsterSpawn", "Candidate", "Site", "Loot", "Exit", "Objective", "Clue" };
            for (var i = 0; i < kinds.Length; i++)
            {
                if (name.IndexOf(kinds[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================
        // Arithmetic.
        // ====================================================================

        /// <summary>
        /// Walked length, not the sight line. §12's argument is that the two diverge, so
        /// "how far has it still got to come" is only ever the first of them.
        /// </summary>
        private static float PathLength(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path)
                || path.status != NavMeshPathStatus.PathComplete)
            {
                return float.PositiveInfinity;
            }

            var corners = path.corners;
            var length = 0f;
            for (var i = 1; i < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
            }

            return length;
        }

        /// <summary>Flat separation — the quantity §06's 12 m is measured in.</summary>
        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static string Metres(float value) =>
            value.ToString("0.0", CultureInfo.InvariantCulture) + " m";

        private static string Seconds(float value) =>
            value.ToString("0.00", CultureInfo.InvariantCulture) + " s";

        private static string Speed(float value) =>
            value.ToString("0.00", CultureInfo.InvariantCulture) + " m/s";

        private void Line(string text) => _report.Append("[ChaseTest] ").Append(text).Append('\n');
    }
}
