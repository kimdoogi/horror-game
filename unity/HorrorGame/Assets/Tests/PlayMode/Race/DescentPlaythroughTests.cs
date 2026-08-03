#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Race;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Race;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Racing
{
    /// <summary>
    /// §01·§02, played: B1의 외곽에서 B8의 중심까지, 여덟 층을 실제로 내려간다.
    /// <para>
    /// <b>Why this exists.</b> Nobody has ever played this game from B1 to B8 — not the owner,
    /// not a test. <c>RaceTests</c> proves §02's rule against a rule; <c>RaceDirectorTests</c>
    /// proves the finish circle against a bare <c>GameObject</c> with no building round it; the
    /// NavMesh audit proves a bake against a graph. Every one of them is true and not one of
    /// them has ever put a body on the rim of B1 and taken it to the bottom. The chain nobody
    /// has walked is the chain the game <i>is</i>: spawn on a rim → cross a maze to the middle
    /// → step into a 투하구 → land on the rim below → seven times → touch the middle of B8 →
    /// win.
    /// </para>
    /// <para>
    /// <b>The maze is not solved, and that is deliberate.</b> A test cannot out-navigate a
    /// concentric maze with four gates and there is no reason for it to try, so the runner is
    /// warped along <see cref="NavMesh.CalculatePath"/>'s own corners the way
    /// <c>MonsterKillTests</c> warps toward the creature. Nothing about the descent is faked by
    /// that: the route is the baked NavMesh's own answer, the 투하구 does its own swallowing
    /// inside <c>MatchDirector.CheckChutes</c> at the same 50 Hz the shipped game gives it, and
    /// §02's standings are read off the <see cref="RaceDirector"/> the real match attaches. What
    /// the warp removes is only the question of whether a human is good at mazes, which is not
    /// the thing that has never been proven.
    /// </para>
    /// <para>
    /// <b>The drop is measured, not assumed — and it was not, for most of this file's life.</b>
    /// The descent used to confirm that <c>MatchDirector.CheckChutes</c> had moved the body and
    /// then set that body down on the 착지 coordinate, which threw away the one thing a 투하구 can
    /// be wrong about: whether the three metres between the mouth and the floor below exist. A
    /// round of storey shells blocked every drop in the building and this test still passed. So
    /// before anything is set down, a ray is fired straight down from
    /// <see cref="Chute.DropPoint"/> and the first thing it meets is measured against the 착지
    /// the same chute names, the 착지 is asked of the bake rather than of the marker, and the
    /// body's own capsule is asked what it would be standing inside. The setting-down stays,
    /// because a headless test cannot fall; what it discards now is only the half second of
    /// falling, and every reading was taken before it.
    /// </para>
    /// <para>
    /// <b>Nothing is asserted until the whole building has been measured.</b> An assertion on
    /// the first storey would report one broken link and hide seven. So each storey's
    /// reachability, drop and bookkeeping are recorded into <see cref="Leg"/>s, the descent
    /// carries on past a failure — warping where the NavMesh could not path, and marking the
    /// storey as warped — and the assertions fire at the end with the whole table attached. One
    /// run therefore names every storey that is broken rather than the first.
    /// </para>
    /// <para>
    /// It lives in the predefined assembly because <c>MatchDirector</c> does, and an
    /// <c>.asmdef</c> cannot reference one. Compiles out of a player build on
    /// <c>UNITY_INCLUDE_TESTS</c>.
    /// </para>
    /// </summary>
    public sealed class DescentPlaythroughTests
    {
        /// <summary>§13's seed for the layout under test — <c>SoloPlaytest.PlaytestSeed</c>.</summary>
        private const int Seed = 20260731;

        /// <summary>The scene <c>SoloPlaytest.BuildScene</c> writes, and the one the game loads.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>
        /// Metres between two floors. <c>MapKitCatalogue.StoreyMetres</c> is the authority and it
        /// lives in an editor assembly this one cannot reference, so it is quoted here in the
        /// same shape and for the same reason <c>MatchDirector.AttachChutes</c> quotes it.
        /// Measured in the artefact: the 착지 markers sit at 0, −3.75 … −26.25.
        /// </summary>
        private const float StoreyPitchMetres = 3.75f;

        /// <summary>One cell. <c>MapKitCatalogue.GridMetres</c>, quoted for the same reason.</summary>
        private const float CellMetres = 2.5f;

        /// <summary>
        /// Chebyshev radius, in metres, of the wall between 중간 고리 and 외곽 고리 —
        /// <c>RadialStorey</c>'s band table, <c>d 8 = wall, 4 gates</c>, at
        /// <see cref="CellMetres"/> a cell. Anything beyond this is on the rim, which is the
        /// whole structural claim §01 makes about a 투하구: "착지는 다음 층의 외곽". Chebyshev
        /// and not Euclidean because <c>RadialStorey</c> builds square rings, so the map's own
        /// metric for "how far out" is the square one.
        /// </summary>
        private const float RimWallMetres = 8f * CellMetres;

        /// <summary>
        /// How far a NavMesh sample may reach for a floor. Deliberately under half of
        /// <see cref="StoreyPitchMetres"/> (1.875 m): every storey of this tower sits directly on
        /// top of the last, so a generous snap radius would answer a question about B5 with the
        /// floor of B4 and the whole test would pass against the wrong building.
        /// </summary>
        private const float NavSnapMetres = 1.8f;

        /// <summary>
        /// Metres of unexplained movement that mean the match moved the body rather than the
        /// test. The only thing in a headless race step that moves a player at all is
        /// <c>MatchDirector.CheckChutes</c>, and it moves them from the middle of a storey to a
        /// corner of the one below — 25 m in plan on this map. Five metres is comfortably under
        /// that and comfortably over anything a <c>CharacterController</c> can do to itself
        /// depenetrating out of a wall the warp put it in.
        /// </summary>
        private const float CarriedMetres = 5f;

        /// <summary>
        /// §12's 투하구 count: one pair per storey but the last. Seven drops take a runner through
        /// eight storeys — B1 is where the race starts, not somewhere anybody falls to.
        /// </summary>
        private const int Drops = RaceState.Storeys - 1;

        /// <summary>
        /// Metres the first solid thing under a <see cref="Chute.DropPoint"/> may be off the 착지
        /// the same chute names, before the drop is landing on something that is not that floor.
        /// <para>
        /// The 착지 markers sit exactly on their storey's floor plane — measured in the artefact,
        /// y = 0, −3.75 … −26.25 — and a floor tile's top is that same plane, so on a building
        /// whose geometry agrees with its markers this figure is zero. The NavMesh audit's own
        /// bar for vertical junk on a good bake is a 0.045 m tallest climb; 0.15 m is over three
        /// times that, and it is under the 0.25 m by which a <c>MapSceneBuilder</c> storey shell's
        /// lid plate stands proud of the floor it covers — which is the thing this measurement
        /// exists to catch.
        /// </para>
        /// </summary>
        private const float LandsOnTheLandingMetres = 0.15f;

        /// <summary>
        /// The player capsule used to ask what a body would be inside at a drop point, when the
        /// rig in the scene has no <see cref="CharacterController"/> to read it off. The rig
        /// always has one — <c>PlayerFeelHarnessMenu.BuildRig</c> builds 0.30 m by 1.75 m — but a
        /// probe that silently measured a zero-radius point on a rig that changed shape would
        /// report open air everywhere.
        /// </summary>
        private const float RigRadiusMetres = 0.30f;

        /// <summary>The other half of <see cref="RigRadiusMetres"/>.</summary>
        private const float RigHeightMetres = 1.75f;

        /// <summary>
        /// Where <c>MapSceneBuilder</c> hangs the invisible boundary shell — its own
        /// <c>BoundaryRootName</c>, quoted here for the same reason <see cref="StoreyPitchMetres"/>
        /// is: the generator lives in an editor assembly this one cannot reference. Only used to
        /// say <em>which</em> defect a probe found, never to decide whether it found one.
        /// </summary>
        private const string BoundaryRootName = "Boundary";

        /// <summary>
        /// Whole-test cap, milliseconds. The building is on the order of a kilometre of corridor
        /// at <see cref="GameConstants.RunnerSprintSpeed"/> — a few hundred seconds of simulated
        /// time and some ten thousand fixed steps — and the wall-clock cost is dominated by
        /// loading a 50 MB scene. Ten minutes is many times that, so reaching it means something
        /// hung rather than something was slow.
        /// </summary>
        private const int TimeoutMilliseconds = 600000;

        /// <summary>What one storey of the descent did. Filled in as it is walked, asserted at the end.</summary>
        private sealed class Leg
        {
            public int Storey;
            public Vector3 StartedAt;
            public bool StartOnNavMesh;
            public bool MiddleOnNavMesh;
            public NavMeshPathStatus MiddleStatus;
            public bool MiddleReachable;
            public float MiddlePathMetres;
            public float MiddleGapMetres;
            public Vector3 MiddlePathEnded;
            public bool Warped;
            public bool MatchRunning;
            public bool HadChute;
            public bool Carried;
            public bool LandedWhereTheChuteSaid;
            public float MissedDropPointBy;
            public int LandedStorey = -1;
            public float LandingRimMetres;
            public int RecordedStorey = -1;

            // ── What the drop does, measured rather than assumed ─────────────
            public Vector3 DropPoint;
            public bool DropColumnFound;
            public string DropColumnHits = string.Empty;
            public float DropColumnMetres;
            public float DropColumnTopY;
            public int DropColumnStorey = -1;
            public bool DropLandsOnTheLanding;
            public string DropPointInside = string.Empty;
            public float HeadroomOverTheLandingMetres = float.PositiveInfinity;
            public bool LandingOnNavMesh;
            public int LandingNavStorey = -1;
            public float LandingNavGapMetres;
        }

        private readonly List<Leg> _legs = new List<Leg>();

        /// <summary>The last place this test put the body. See <see cref="CarriedByTheMatch"/>.</summary>
        private Vector3 _placed;

        /// <summary>
        /// Where the body was the instant the match took it, sampled inside the step rather than
        /// after the next <c>yield</c>. It has to be: the scene's own rig is live in a PlayMode
        /// test, so <c>PlayerMotor</c> keeps running and a body left three metres in the air for
        /// one frame has already started falling by the time a coroutine looks at it. Measuring
        /// after the yield would report the fall and blame the chute.
        /// </summary>
        private Vector3 _carriedTo;

        [SetUp]
        public void QuietenTheImporter()
        {
            // Loading the eight-storey scene re-emits Mirror's packaging complaint about an
            // immutable folder nobody is allowed to fix, and the Test Framework fails a test on
            // any unexpected LogError. Safe only because every step below is asserted.
            LogAssert.ignoreFailingMessages = true;
            _legs.Clear();
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
                var empty = SceneManager.CreateScene("DescentPlaythroughTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(solo);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// The race, run. Eight storeys, seven 투하구, one finish.
        /// </summary>
        [UnityTest]
        [Timeout(TimeoutMilliseconds)]
        public IEnumerator A_runner_can_descend_from_the_rim_of_B1_to_the_middle_of_B8()
        {
            SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var found = Object.FindFirstObjectByType<MatchDirector>();
            Assert.That(found, Is.Not.Null, "the solo scene has no MatchDirector — there is no match to run.");

            var director = found!;
            if (director.Map == null)
            {
                Assert.That(director.BeginMatch(Seed), Is.True,
                    "BeginMatch refused, so nothing below this line can be measured. The reason is in the "
                    + "[Match] error logged immediately above this failure.");
            }

            yield return null;

            // DELETED: the RaceMode guard. §01's race used to be gated behind a flag, with
            // the co-operative recovery match on the other side of it; the co-op path is
            // deleted, so BeginMatch has only one thing left it can start.

            // ── §06 is not what is being tested, and it is standing on the target ──
            // DescentMap puts the creature in the middle of a storey halfway down — "괴물은
            // 여기서 시작한다, 절반쯤 내려간 곳" — which is exactly the cell this test walks to.
            // Left alive it would kill the runner on that storey every single run, and the
            // report would be "caught on B5" instead of anything about the descent. The creature
            // has two tests of its own (MonsterKillTests, MonsterChaseTests) and this one is the
            // chain. Destroyed rather than disabled, because MatchDirector calls
            // MonsterAgent.Simulate directly and a disabled component still answers a call.
            //
            // ALL of them, 2026-08-03, and the plural is the point. This line used to read
            // FindFirstObjectByType and destroy one creature, which was the whole population
            // when DescentMap seeded a single start in the middle of B5. §12-B③ now declares
            // one per storey and MatchDirector.PrepareCreatures stands one up at each —
            // measured on this run: "[Match] §06 창조물 8마리 — 8개 층에 선언된 시작점 8개".
            // Destroying one of eight left seven creatures posted on exactly the cells the
            // legs below walk to, and left WHICH floor got cleared up to whatever
            // FindFirstObjectByType happened to return. The run still finished — but a pass
            // that depends on seven creatures not noticing a runner standing in the middle of
            // their floor is a pass for a reason this test does not name, and this repository
            // has shipped enough of those. §06 belongs to MonsterKillTests and
            // MonsterChaseTests; this test is the chain, and the chain is measured with the
            // building empty.
            var creatures = Object.FindObjectsByType<MonsterAgent>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < creatures.Length; i++)
            {
                if (creatures[i] != null)
                {
                    Object.Destroy(creatures[i].gameObject);
                }
            }

            Assert.That(creatures.Length, Is.GreaterThan(0),
                "no MonsterAgent in the scene at all. MatchDirector.PrepareCreatures builds one per "
                + "MatchMap.MonsterSpawns entry and refuses to begin the match if it cannot, so zero here means "
                + "the §06 half of this scene is not in the build and MonsterKillTests is measuring nothing.");

            yield return null;

            var rig = FindPlayerRoot();
            Assert.That(rig, Is.Not.Null, "no player rig in the scene, so nobody can run the race.");

            var attached = director.GetComponent<RaceDirector>();
            Assert.That(attached, Is.Not.Null,
                "MatchDirector has no RaceDirector on it — AttachRace never ran, so §02 has no standings, no "
                + "places and no winner no matter where anybody walks.");

            var player = rig!;
            var race = attached!;
            var seat = director.LocalPlayerIndex;

            Assert.That(race.Started, Is.True, "§02 never began — RaceDirector.Begin was refused. §11 sizes the field.");
            Assert.That(race.FinishFound, Is.True,
                "§02's 도착점 was not located, so RaceDirector.CheckFinish returns on its first line and nobody "
                + "can ever win this match. The scene needs an object named '" + RaceDirector.FinishMarkerName
                + "' or an '" + MatchMap.EntranceLightPrefix + "…' light under '" + MatchMap.MapRootName + "'.");

            var descentsAnnounced = 0;
            race.Descended += (id, landedOn) =>
            {
                if (id == seat)
                {
                    descentsAnnounced++;
                }
            };

            var middleX = race.Finish.x;
            var middleZ = race.Finish.z;

            // ── The 투하구, gathered by the storey whose middle they sit in ───────
            var chutesByStorey = new List<Chute>?[RaceState.Storeys];
            var allChutes = Object.FindObjectsByType<Chute>(FindObjectsSortMode.None);
            for (var i = 0; i < allChutes.Length; i++)
            {
                var above = allChutes[i].StoreyBelow - 1;
                if (above < 0 || above >= RaceState.Storeys)
                {
                    continue;
                }

                (chutesByStorey[above] ??= new List<Chute>()).Add(allChutes[i]);
            }

            // ── Link one: where the runner woke up ───────────────────────────────
            // One step first. §01's phase flag is recomputed inside StepMatch, so reading it
            // straight after BeginMatch reports the constructor rather than the world. That trap
            // caught MonsterKillTests first time out and it is the same flag.
            director.StepMatch(GameConstants.FixedStep);
            yield return null;

            var spawn = player.position;
            _placed = spawn;

            var spawnStorey = StoreyOf(spawn.y);
            var spawnRim = Chebyshev(spawn.x - middleX, spawn.z - middleZ);

            // DELETED: the LocalPlayerOnSurface guard. It asked whether a match had begun
            // on §01's 지상, which was how you told the co-operative flow from the race.
            // There is no surface left to be on — see link six below, also deleted.
            Assert.That(spawnStorey, Is.Zero,
                "the runner did not spawn on B1. They are on B" + (spawnStorey + 1) + ", y = "
                + spawn.y.ToString("0.00") + " m. §01: 20명이 B1 외곽 고리에서 출발한다.");
            Assert.That(spawnRim, Is.GreaterThan(RimWallMetres),
                "the runner spawned " + spawnRim.ToString("0.0") + " m (Chebyshev) from the middle of B1, inside "
                + "RadialStorey's d = 8 wall. They belong on the 외곽 고리 at d ≥ 9 — beyond "
                + RimWallMetres.ToString("0.0") + " m — with the whole storey between them and the 투하구. "
                + "Spawning inside the wall gives away most of a floor before anybody moves.");

            // ── Down the building ────────────────────────────────────────────────
            for (var storey = 0; storey < RaceState.Storeys; storey++)
            {
                var floorY = -storey * StoreyPitchMetres;
                var middle = new Vector3(middleX, floorY, middleZ);

                var leg = new Leg { Storey = storey, StartedAt = player.position };
                _legs.Add(leg);

                // ── B-010: is this storey's middle reachable from its rim? ───────
                // Pure NavMesh, measured before anybody walks anywhere, so the answer belongs to
                // the bake and not to the warp that follows it.
                leg.StartOnNavMesh = NavMesh.SamplePosition(
                    player.position, out var fromHit, NavSnapMetres, NavMesh.AllAreas);
                leg.MiddleOnNavMesh = NavMesh.SamplePosition(
                    middle, out var middleHit, NavSnapMetres, NavMesh.AllAreas);

                var middlePath = new NavMeshPath();
                if (leg.StartOnNavMesh && leg.MiddleOnNavMesh
                    && NavMesh.CalculatePath(fromHit.position, middleHit.position, NavMesh.AllAreas, middlePath))
                {
                    var corners = middlePath.corners;
                    leg.MiddleStatus = middlePath.status;
                    leg.MiddlePathMetres = PathLength(corners);
                    leg.MiddlePathEnded = corners.Length > 0 ? corners[corners.Length - 1] : middle;
                    leg.MiddleGapMetres = corners.Length > 0
                        ? Chebyshev(corners[corners.Length - 1].x - middleX, corners[corners.Length - 1].z - middleZ)
                        : float.PositiveInfinity;
                    leg.MiddleReachable = middlePath.status == NavMeshPathStatus.PathComplete;
                }
                else
                {
                    leg.MiddleStatus = NavMeshPathStatus.PathInvalid;
                    leg.MiddleGapMetres = float.PositiveInfinity;
                }

                // ── Where this leg walks to ──────────────────────────────────────
                // On the bottom storey that is §02's finish. Everywhere else it is the nearer of
                // the storey's two 투하구, which is what a runner picks and is DescentMap's whole
                // argument for hanging two of them.
                var chutes = chutesByStorey[storey];
                Chute? chute = null;
                if (storey < RaceState.Storeys - 1 && chutes != null && chutes.Count > 0)
                {
                    chute = Nearest(chutes, player.position);
                }

                leg.HadChute = chute != null;
                var target = chute != null ? chute.transform.position : middle;

                // ── Cross the maze ───────────────────────────────────────────────
                var walkPath = new NavMeshPath();
                var walkable = leg.StartOnNavMesh
                               && NavMesh.SamplePosition(target, out var targetHit, NavSnapMetres, NavMesh.AllAreas)
                               && NavMesh.CalculatePath(fromHit.position, targetHit.position, NavMesh.AllAreas, walkPath)
                               && walkPath.status == NavMeshPathStatus.PathComplete;

                if (walkable)
                {
                    yield return Follow(director, player, walkPath.corners);
                }
                else
                {
                    // The NavMesh cannot get there. Recorded as the failure it is, and then
                    // stepped over — so the storeys below this one are still measured by this
                    // run instead of being hidden behind it.
                    leg.Warped = true;
                    Place(player, new Vector3(target.x, player.position.y, target.z));

                    // StepAndWatch and not a bare StepMatch, and before the yield rather than
                    // after it: a warp lands squarely in the mouth, so the 투하구 fires on this
                    // very step. Stepping without watching left _carriedTo holding whatever the
                    // last leg put there — on the first run of this test that was Vector3.zero,
                    // and it produced seven confident failures measured against the world origin.
                    StepAndWatch(director, player);
                    yield return null;
                }

                // The last couple of metres, straight, inside Chamber_Open_3x3 — 7.5 m across, so
                // this never crosses a wall. NavMesh.SamplePosition may leave the path's end up
                // to NavSnapMetres from the mouth, which is outside Chute.MouthRadiusMetres
                // (1.4 m), and a runner who stopped there would stand beside the hole forever.
                if (!CarriedByTheMatch(player) && Flat(player.position - target) > 0.05f)
                {
                    yield return Approach(director, player, target);
                }

                leg.MatchRunning = director.IsRunning;

                if (chute == null)
                {
                    break;
                }

                // ── Down the hole ────────────────────────────────────────────────
                // Standing in the mouth and holding the ground, the way the owner stood in front
                // of the creature. Three seconds is 150 samples of a check that runs every step.
                var stood = player.position;
                var waited = 0f;
                var breaths = 0;
                var carried = CarriedByTheMatch(player);
                while (waited < 3f && !carried)
                {
                    Place(player, stood);
                    carried = StepAndWatch(director, player);
                    waited += GameConstants.FixedStep;

                    // Yielded in batches, and only while nothing has happened. MatchDirector
                    // steps itself from its own FixedUpdate, so a frame handed back to the
                    // engine is a step this test did not watch — harmless while the runner is
                    // just standing there, and the thing to avoid at the instant of the drop.
                    if (!carried && ++breaths % 25 == 0)
                    {
                        yield return null;
                        carried = Caught(player);
                    }
                }

                leg.Carried = carried;

                // Which one took them, rather than which one they were walking to. A storey has
                // two 투하구 two cells apart, and a route to the far one can pass through the
                // near one's mouth — that is a runner falling down a chute, not a bug, and
                // asserting against the chosen one would call it a bug.
                var taken = chute;
                leg.MissedDropPointBy = float.PositiveInfinity;
                if (leg.Carried && chutes != null)
                {
                    for (var c = 0; c < chutes.Count; c++)
                    {
                        var miss = Vector3.Distance(_carriedTo, chutes[c].DropPoint());
                        if (miss < leg.MissedDropPointBy)
                        {
                            leg.MissedDropPointBy = miss;
                            taken = chutes[c];
                        }
                    }

                    // A centimetre. The drop is a straight assignment of Chute.DropPoint inside
                    // CheckChutes, so the only thing between it and this reading is float
                    // arithmetic — not a tolerance for how far off the mark is acceptable.
                    leg.LandedWhereTheChuteSaid = leg.MissedDropPointBy < 0.01f;
                }

                leg.LandedStorey = StoreyOf(taken.Landing.y);
                leg.LandingRimMetres = Chebyshev(taken.Landing.x - middleX, taken.Landing.z - middleZ);

                // ── What the drop DOES, asked of the building ────────────────────
                // This block exists because the four lines under it used to be the whole answer.
                // The test confirmed the 투하구 had fired and then set the body down on the
                // landing coordinate, which threw away the only thing a drop can be wrong about:
                // whether the three metres between the mouth and the floor below are there. Every
                // storey shell built last round blocked every drop and this test still passed,
                // because nothing here ever looked down the hole.
                //
                // Asked with queries rather than by falling, and that is a decision rather than a
                // shortcut. A PlayMode test could yield frames and let §05's gravity do it, but
                // the answer would then depend on how many frames the engine handed back and on
                // whether MatchDirector's own FixedUpdate got one — the flakiest possible way to
                // measure a fact about static geometry. A ray and an overlap answer the same
                // question about the same colliders, in the same frame, every run.
                var dropPoint = taken.DropPoint();
                leg.DropPoint = dropPoint;

                // Straight down from the drop point, and deliberately with no lift on the ray's
                // origin. Chute.DropHeightMetres is 3.0 m and the kit's corridor gives exactly
                // 3.00 m of clear height (MapKit.manifest corridor_clear.height), so the drop
                // point IS the ceiling plane of the cell it drops into — half a metre of lift,
                // the way EscapeTests.Underfoot takes it, would start this ray inside the ceiling
                // slab and report the storey above.
                var reach = (RaceState.Storeys * StoreyPitchMetres) + 10f;
                leg.DropColumnFound = FirstHit(player, dropPoint, Vector3.down, reach, out var under);
                if (leg.DropColumnFound)
                {
                    leg.DropColumnMetres = under.distance;
                    leg.DropColumnTopY = under.point.y;
                    leg.DropColumnStorey = StoreyOf(under.point.y);
                    leg.DropColumnHits = Path(under.collider != null ? under.collider.transform : null);
                    leg.DropLandsOnTheLanding =
                        Mathf.Abs(under.point.y - taken.Landing.y) <= LandsOnTheLandingMetres
                        && leg.DropColumnStorey == leg.Storey + 1;
                }

                // Reported, not asserted, and the arithmetic says why. A standing body at the
                // drop point needs DropHeightMetres + the rig's own height of room — 3.0 + 1.75 =
                // 4.75 m — against a StoreyPitchMetres of 3.75 and 3.00 m of corridor clear. No
                // building assembled from this kit can give that, so demanding it would paint the
                // headline test red for a decision that lives in Chute.DropHeightMetres rather
                // than in any map. What it is worth is the naming: the report prints the
                // hierarchy path of everything the body would be inside, so a 'Map/Boundary/…'
                // plate in that list is a shell standing in a storey it is supposed to be outside
                // of, and the reader can tell that apart from the kit's own ceiling.
                leg.DropPointInside = InsideAt(player, dropPoint);

                // How much room the 착지 actually has over it, so the line above can be read
                // against a number instead of against the manifest.
                if (FirstHit(player, taken.Landing + (Vector3.up * 0.05f), Vector3.up, StoreyPitchMetres, out var lid))
                {
                    leg.HeadroomOverTheLandingMetres = lid.point.y - taken.Landing.y;
                }

                // The honest version of "does the drop land the runner on the storey below": not
                // the height of the drop point, which is a number this file could have computed
                // itself, but whether the bake has floor at the 착지 and whether that floor
                // belongs to the storey the chute claims. NavSnapMetres is 1.8 m, under half a
                // storey, so this cannot be answered by the floor above or below.
                leg.LandingOnNavMesh = NavMesh.SamplePosition(
                    taken.Landing, out var landHit, NavSnapMetres, NavMesh.AllAreas);
                if (leg.LandingOnNavMesh)
                {
                    leg.LandingNavStorey = StoreyOf(landHit.position.y);
                    leg.LandingNavGapMetres = Vector3.Distance(taken.Landing, landHit.position);
                }

                var duringDescent = race.Rules;
                leg.RecordedStorey = duringDescent != null ? duringDescent[seat].Storey : -1;

                // ── Stand up on the rim below ────────────────────────────────────
                // §05's gravity does the last three metres in the shipped game and this test
                // steps no PlayerMotor, so the body is set down on the 착지 the chute chose. That
                // is a simplification with a guard in front of it rather than a blindfold: the
                // block above has already measured what is under the drop point, how far below
                // it, which storey it belongs to and what the drop point is inside, and every one
                // of those readings was taken before this line moved anything. What is discarded
                // here is only the half second of falling, which no assertion below depends on.
                //
                // Snapped onto the bake when the bake has floor there, and left on the marker
                // when it has not — because "there is no NavMesh at this 착지" is itself a
                // finding, and silently keeping the marker used to hide it.
                var landing = leg.LandingOnNavMesh ? landHit.position : taken.Landing;

                Place(player, landing);
                director.StepMatch(GameConstants.FixedStep);
                yield return null;
            }

            // ── Touch the middle of B8 ───────────────────────────────────────────
            // RaceDirector samples arrivals inside its own Tick, which MatchDirector runs at the
            // end of every fixed step. Half a second standing on the finish is 25 samples of a
            // 2.5 m circle.
            var settle = 0f;
            while (settle < 0.5f)
            {
                Place(player, player.position);
                director.StepMatch(GameConstants.FixedStep);
                settle += GameConstants.FixedStep;
                yield return null;
            }

            var atTheEnd = race.Rules;
            var finalStorey = atTheEnd != null ? atTheEnd[seat].Storey : -1;
            var finalPlace = atTheEnd != null ? atTheEnd[seat].Place : -1;
            var report = Report(race, player.position, middleX, middleZ, seat, descentsAnnounced, allChutes.Length);

            // ── Every broken link, gathered before any of them is thrown ─────────
            // Not a chain of Assert.That. The first one to fire would end the test and hide
            // everything under it, and "B1's middle is unreachable" would then be the whole
            // report of a building with eight storeys in it. Collected in the order the game
            // depends on them, so the first line of the failure is the deepest cause.
            var broken = new List<string>();

            // ── Link two: every storey's middle is reachable from its rim (B-010) ─
            for (var i = 0; i < _legs.Count; i++)
            {
                var leg = _legs[i];
                if (leg.MiddleReachable)
                {
                    continue;
                }

                broken.Add(
                    "B-010 · B" + (leg.Storey + 1) + " — 외곽에서 중심까지 갈 수 없다: " + leg.MiddleStatus
                    + (leg.MiddleOnNavMesh ? string.Empty
                        : " (중심 " + NavSnapMetres.ToString("0.0") + " m 안에 NavMesh가 없다)")
                    + (leg.StartOnNavMesh ? string.Empty : " (출발점이 NavMesh 밖이다)")
                    + ". 중심 " + new Vector3(middleX, -leg.Storey * StoreyPitchMetres, middleZ).ToString("0.00")
                    + " 에서 " + Chebyshev(leg.StartedAt.x - middleX, leg.StartedAt.z - middleZ).ToString("0.0")
                    + " m 떨어진 외곽 " + leg.StartedAt.ToString("0.00") + " 에서 출발했고, 경로는 "
                    + (float.IsInfinity(leg.MiddleGapMetres)
                        ? "만들어지지도 않았다"
                        : leg.MiddlePathEnded.ToString("0.00") + " 에서 끊겼다 — 중심까지 "
                          + leg.MiddleGapMetres.ToString("0.0") + " m ("
                          + (leg.MiddleGapMetres / CellMetres).ToString("0.0")
                          + " 칸) 남은 자리. RadialStorey의 d = 2 벽, 중심 방으로 들어가는 단 하나의 관문이다")
                    + ". 이 층이 막혀 있으면 §01의 하강은 그 아래로 존재하지 않는다 — 아무도 이 층의 "
                    + "투하구에 닿지 못하고, 밑의 층들은 도달할 수 없는 방이다.");
            }

            // ── Link three: a 투하구 drops one storey, onto the rim ───────────────
            for (var i = 0; i < _legs.Count; i++)
            {
                var leg = _legs[i];
                if (leg.Storey >= RaceState.Storeys - 1)
                {
                    continue;
                }

                if (!leg.HadChute)
                {
                    broken.Add(
                        "투하구 · B" + (leg.Storey + 1) + " has no 투하구 paired with a 착지, so the descent stops "
                        + "here. §01 gives every storey but the last a pair; a storey without one is the bottom "
                        + "of the game whatever the map says.");
                    continue;
                }

                if (!leg.Carried)
                {
                    broken.Add(
                        "투하구 · B" + (leg.Storey + 1) + ": the runner stood in the mouth for three seconds and "
                        + "was not taken. MatchDirector.CheckChutes or Chute.Swallows refused — the mouth is "
                        + Chute.MouthRadiusMetres.ToString("0.0") + " m in plan with a 2.6 m height window, and an "
                        + "unbound chute swallows nobody at all.");
                    continue;
                }

                if (!leg.LandedWhereTheChuteSaid)
                {
                    broken.Add(
                        "투하구 · B" + (leg.Storey + 1) + ": the match moved the runner, but to a point "
                        + leg.MissedDropPointBy.ToString("0.00") + " m from the nearest of this storey's drop "
                        + "points. Chute.DropPoint is its 착지 plus " + Chute.DropHeightMetres.ToString("0.0")
                        + " m and CheckChutes assigns it directly, so this reading was taken inside the step "
                        + "that moved them — nothing had a frame in which to fall.");
                }

                if (leg.LandedStorey != leg.Storey + 1)
                {
                    broken.Add(
                        "투하구 · B" + (leg.Storey + 1) + "'s chute landed the runner on B" + (leg.LandedStorey + 1)
                        + ". A chute is exactly one storey; anything else is a hole through the building.");
                }

                if (leg.LandingRimMetres <= RimWallMetres)
                {
                    broken.Add(
                        "투하구 · B" + (leg.Storey + 1) + "'s chute landed the runner "
                        + leg.LandingRimMetres.ToString("0.0") + " m (Chebyshev) from the middle of B"
                        + (leg.LandedStorey + 1) + ", inside RadialStorey's d = 8 wall. §01: 착지는 다음 층의 "
                        + "외곽이다. Land anybody near a middle and one runner reaching one centre falls the rest "
                        + "of the way — the building becomes a single maze.");
                }

                // ── The drop itself, which this test used to set down and forget ──
                if (!leg.DropColumnFound)
                {
                    broken.Add(
                        "투하구 · B" + (leg.Storey + 1) + ": there is nothing at all under this chute's drop point "
                        + leg.DropPoint.ToString("0.00") + " — a ray " + ((RaceState.Storeys * StoreyPitchMetres)
                            + 10f).ToString("0") + " m long met no collider. §01 drops a runner from "
                        + Chute.DropHeightMetres.ToString("0.0") + " m and lets §05's gravity finish it; with no "
                        + "floor beneath, the drop is a fall out of the building and the descent ends here whatever "
                        + "the standings say.");
                }
                else if (!leg.DropLandsOnTheLanding)
                {
                    broken.Add(
                        "투하구 · B" + (leg.Storey + 1) + ": the first solid thing under the drop point is "
                        + leg.DropColumnHits + " at y " + leg.DropColumnTopY.ToString("0.00") + " ("
                        + leg.DropColumnMetres.ToString("0.00") + " m below the drop point), and this chute's 착지 "
                        + "is y " + leg.DropPoint.y.ToString("0.00") + " − " + Chute.DropHeightMetres.ToString("0.0")
                        + " = " + (leg.DropPoint.y - Chute.DropHeightMetres).ToString("0.00") + " on B"
                        + (leg.Storey + 2) + " — off by "
                        + Mathf.Abs(leg.DropColumnTopY - (leg.DropPoint.y - Chute.DropHeightMetres)).ToString("0.00")
                        + " m, against " + LandsOnTheLandingMetres.ToString("0.00") + " m of slack"
                        + (leg.DropColumnStorey == leg.Storey + 1
                            ? string.Empty
                            : ", and on B" + (leg.DropColumnStorey + 1) + " rather than B" + (leg.Storey + 2))
                        + ". A runner who steps into this 투하구 does not reach the floor of the storey below — "
                        + "they come down on " + leg.DropColumnHits + " — and every measurement the rest of the "
                        + "descent takes is taken standing on it. This is the reading DescentPlaythroughTests used "
                        + "to discard by setting the body down on the 착지 coordinate the moment the chute fired.");
                }

                if (!leg.LandingOnNavMesh)
                {
                    broken.Add(
                        "투하구 · B" + (leg.Storey + 1) + "'s 착지 "
                        + (leg.DropPoint - (Vector3.up * Chute.DropHeightMetres)).ToString("0.00")
                        + " has no NavMesh within "
                        + NavSnapMetres.ToString("0.0") + " m. The marker is somewhere the bake does not call "
                        + "walkable, so a runner dropped there is off the graph: §06 cannot path to them, "
                        + "PlayerReachAudit's 투하구 count is about markers rather than about floor, and the storey "
                        + "below is entered at a point the storey below does not have.");
                }
                else if (leg.LandingNavStorey != leg.Storey + 1)
                {
                    broken.Add(
                        "투하구 · B" + (leg.Storey + 1) + "'s 착지 snapped onto NavMesh on B"
                        + (leg.LandingNavStorey + 1) + ", " + leg.LandingNavGapMetres.ToString("0.00")
                        + " m away, not on B" + (leg.Storey + 2) + ". Sampling is capped at "
                        + NavSnapMetres.ToString("0.0") + " m — under half a " + StoreyPitchMetres.ToString("0.00")
                        + " m storey — so this is not a snap that reached through a floor; the floor at this 착지 "
                        + "is genuinely not the one §01 says the runner lands on.");
                }
            }

            // ── Link four: §02 wrote the descents down ───────────────────────────
            if (descentsAnnounced != Drops)
            {
                broken.Add(
                    "§02 · the race heard " + descentsAnnounced + " descent(s) for seat " + seat
                    + " and the runner fell " + Drops + " times. RaceDirector.Descended is what the standings, "
                    + "the HUD and §13's clients are all built on: a drop the race never hears did not happen as "
                    + "far as §02 is concerned.");
            }

            if (finalStorey != RaceState.Storeys - 1)
            {
                broken.Add(
                    "§02 · the race still has the runner on B" + (finalStorey + 1) + " after " + Drops
                    + " 투하구. RaceState.ReportFinish refuses an arrival from anybody whose storey is not the "
                    + "bottom one — 'you have to have got there' — so while this number is wrong the finish is "
                    + "unreachable no matter where the body stands.");
            }

            // ── Link five: the finish fires ──────────────────────────────────────
            if (race.WinnerId != seat)
            {
                broken.Add(
                    "§02 · the runner is standing on the middle of B8 and the race has no winner (WinnerId "
                    + race.WinnerId + "). This is the end of the whole chain — spawn, cross, fall, seven times, "
                    + "arrive — and the arrival did not register.");
            }
            else if (finalPlace != 1)
            {
                broken.Add("§02 · the first to touch the middle of B8 is 1위, and this runner was given "
                           + finalPlace + ".");
            }

            if (race.ExitOf(seat) != RaceExit.Finished)
            {
                broken.Add("§02 · the runner's race was closed as " + race.ExitOf(seat) + ", not Finished.");
            }

            // ── Link six: every storey kept stepping ─────────────────────────────
            // This link used to be two questions, and the second one is DELETED. It asked
            // whether MatchMap.IsOnSurface called the middle of a storey 지상 — because the
            // apron was a SurfaceRadius circle in plan around the 출입구 marker, the height
            // was thrown away, and on this map that marker is the middle of B8, which every
            // other storey sits directly above. A runner therefore finished each floor
            // inside a 안전 지대 where §06 had to forget them and §01's 귀환 fired: 숨 돌리기,
            // sell, resupply, once per storey, six cells short of the 투하구.
            //
            // There is no apron, no 지상 and no MatchMap.IsOnSurface any more, so the bug
            // it describes cannot be reintroduced by tuning a radius — it would take
            // re-adding the surface. What survives is the first question, which is about
            // the descent rather than about the co-op flow.
            // The last storey is exempt, and it is exempt because the runner WON.
            //
            // Reaching the middle of B8 is §02's finish. The field here is one seat, so that
            // arrival is also the last one: RaceDirector closes, MatchDirector stops
            // stepping, and IsRunning reads false at exactly the moment the test was hoping
            // to see. That is the race working. Asserting it here would demand that the game
            // keep running after somebody has won, which is the opposite of §02.
            //
            // It is exempt by INDEX rather than by "the last leg we happened to record", so
            // a run that stops on B7 and never reaches B8 still has its B7 leg checked and
            // still fails. And the exemption is not silent — the win is asserted, twice, by
            // clauses that were already here: the descent count against Drops, and
            // `race.ExitOf(seat) != RaceExit.Finished`, which is what actually distinguishes
            // "stopped because somebody won" from "stopped". The table above also prints
            // 경기 정지됨 on any storey it happened on.
            for (var i = 0; i < _legs.Count; i++)
            {
                if (_legs[i].Storey == RaceState.Storeys - 1)
                {
                    continue;
                }

                if (!_legs[i].MatchRunning)
                {
                    broken.Add(
                        "§01 · the match stopped stepping while the runner was in the middle of B"
                        + (_legs[i].Storey + 1) + ". MatchDirector.StepMatch is a no-op once EndMatch has run, so "
                        + "everything measured below this storey is a frozen world.");
                }
            }

            if (broken.Count > 0)
            {
                var why = new StringBuilder();
                why.AppendLine("§01의 하강이 끊긴 곳 " + broken.Count + "군데. 위에서부터 원인에 가깝다:");
                why.AppendLine();
                for (var i = 0; i < broken.Count; i++)
                {
                    why.AppendLine("  " + (i + 1) + ". " + broken[i]);
                    why.AppendLine();
                }

                why.Append(report);
                Assert.Fail(why.ToString());
            }

            Debug.Log("[Test] §01 하강 완주 — B1 외곽에서 B8 중심까지, 투하구 " + descentsAnnounced + "회." + report);
        }

        // ------------------------------------------------------------------
        // Walking.
        // ------------------------------------------------------------------

        /// <summary>
        /// Warps the runner along a NavMesh path, corner to corner, stepping the real match at
        /// <see cref="GameConstants.FixedStep"/> the whole way — so the 투하구 gets exactly the
        /// sampling rate the shipped game gives it. Stops the moment the match takes the body.
        /// </summary>
        private IEnumerator Follow(MatchDirector director, Transform player, Vector3[] corners)
        {
            var step = GameConstants.RunnerSprintSpeed * GameConstants.FixedStep;
            var frames = 0;

            for (var c = 0; c < corners.Length; c++)
            {
                var corner = corners[c];

                // Bounded by the distance rather than by a clock: a corner that cannot be reached
                // is a bug in this loop, not in the game, and an unbounded while would hang the
                // run instead of failing it.
                var guard = Mathf.CeilToInt(Flat(corner - player.position) / step) + 10;

                while (guard-- > 0 && Flat(corner - player.position) > step)
                {
                    Place(player, Vector3.MoveTowards(player.position, corner, step));

                    if (StepAndWatch(director, player))
                    {
                        yield break;
                    }

                    // Yielded in batches. Ten thousand fixed steps is ten thousand frames
                    // otherwise, and nothing in a headless match needs a frame between two 11 cm
                    // moves — though the engine does need one occasionally.
                    if (++frames % 25 == 0)
                    {
                        yield return null;

                        // The match stepped itself while that frame was out. See Caught.
                        if (Caught(player))
                        {
                            yield break;
                        }
                    }
                }

                Place(player, corner);

                if (StepAndWatch(director, player))
                {
                    yield break;
                }
            }

            yield return null;
        }

        /// <summary>
        /// The last stretch, straight and at walking pace. Only ever used inside the middle
        /// chamber, where there is nothing to walk into.
        /// </summary>
        private IEnumerator Approach(MatchDirector director, Transform player, Vector3 target)
        {
            var step = GameConstants.WalkSpeed * GameConstants.FixedStep;
            var flatTarget = new Vector3(target.x, player.position.y, target.z);
            var guard = Mathf.CeilToInt(Flat(flatTarget - player.position) / step) + 25;
            var frames = 0;

            while (guard-- > 0)
            {
                var to = Vector3.MoveTowards(player.position, flatTarget, step);
                to.y = player.position.y;
                Place(player, to);

                if (StepAndWatch(director, player) || Flat(player.position - flatTarget) <= 0.01f)
                {
                    break;
                }

                if (++frames % 25 == 0)
                {
                    yield return null;
                }
            }

            yield return null;
        }

        /// <summary>
        /// Puts a body somewhere and remembers where, so <see cref="CarriedByTheMatch"/> can tell
        /// the test's own warping apart from the match moving the player.
        /// <para>
        /// A <c>CharacterController</c> ignores writes to <c>transform.position</c> while it is
        /// enabled, which is exactly the kind of silent no-op that makes a test pass against a
        /// player who never moved.
        /// </para>
        /// </summary>
        private void Place(Transform root, Vector3 to)
        {
            var controller = root.GetComponent<CharacterController>();
            if (controller == null)
            {
                root.position = to;
                _placed = root.position;
                return;
            }

            controller.enabled = false;
            root.position = to;
            controller.enabled = true;
            _placed = root.position;
        }

        /// <summary>
        /// True when the body is somewhere this test did not put it. In a headless race step the
        /// only thing that moves a player is <c>MatchDirector.CheckChutes</c>, so this is the
        /// 투하구 firing — observed rather than assumed, which is the difference between testing
        /// the chute and testing the arithmetic this file could have done itself.
        /// </summary>
        private bool CarriedByTheMatch(Transform root)
        {
            return (root.position - _placed).sqrMagnitude > CarriedMetres * CarriedMetres;
        }

        /// <summary>
        /// One fixed step of the real match, and a look at the body the instant it returns.
        /// Records <see cref="_carriedTo"/> the moment the 투하구 fires, before any frame passes
        /// and before §05's gravity gets a chance to move the evidence.
        /// </summary>
        private bool StepAndWatch(MatchDirector director, Transform player)
        {
            director.StepMatch(GameConstants.FixedStep);
            if (!CarriedByTheMatch(player))
            {
                return false;
            }

            _carriedTo = player.position;
            return true;
        }

        /// <summary>
        /// The same look, taken after a frame was handed back to the engine rather than after a
        /// step this test drove. <c>MatchDirector.FixedUpdate</c> steps the match on its own, so
        /// a 투하구 can fire inside a <c>yield</c>; this catches that, at the cost of whatever
        /// §05's gravity did in the one physics tick in between.
        /// </summary>
        private bool Caught(Transform player)
        {
            if (!CarriedByTheMatch(player))
            {
                return false;
            }

            _carriedTo = player.position;
            return true;
        }

        // ------------------------------------------------------------------
        // Looking down the hole.
        // ------------------------------------------------------------------

        /// <summary>Room for every collider a probe can meet through eight storeys of building.</summary>
        private readonly RaycastHit[] _probe = new RaycastHit[32];

        /// <summary>
        /// The nearest collider along a ray, with the runner's own body skipped by name.
        /// <para>
        /// By name rather than by trusting PhysX to drop a ray that starts inside a convex shape:
        /// every one of these probes is fired at a point the match has just put the body on, so
        /// the origin is inside the player's own capsule, and "the thing under me is me" is
        /// exactly the shape of a reading that sends the next person to the wrong storey.
        /// </para>
        /// </summary>
        private bool FirstHit(Transform player, Vector3 from, Vector3 direction, float reach, out RaycastHit nearest)
        {
            var found = Physics.RaycastNonAlloc(
                from, direction, _probe, reach, ~0, QueryTriggerInteraction.Ignore);

            var best = -1;
            for (var i = 0; i < found; i++)
            {
                var t = _probe[i].collider != null ? _probe[i].collider.transform : null;
                if (t == null || t.IsChildOf(player))
                {
                    continue;
                }

                if (best < 0 || _probe[i].distance < _probe[best].distance)
                {
                    best = i;
                }
            }

            nearest = best >= 0 ? _probe[best] : default(RaycastHit);
            return best >= 0;
        }

        /// <summary>
        /// Everything a body standing with its feet at <paramref name="feet"/> would be inside,
        /// named by its place in the hierarchy — the capsule cast §01's drop point has never been
        /// asked to survive.
        /// <para>
        /// The capsule is the rig's own, read off the <see cref="CharacterController"/> in the
        /// scene rather than written down here, because the radius and height are the entire
        /// question of what fits where and a probe built from a constant would keep answering
        /// after the rig changed shape.
        /// </para>
        /// </summary>
        /// <returns>An empty string when the body fits — nothing is a finding here.</returns>
        private string InsideAt(Transform player, Vector3 feet)
        {
            var radius = RigRadiusMetres;
            var height = RigHeightMetres;
            var controller = player.GetComponent<CharacterController>();
            if (controller != null)
            {
                radius = controller.radius;
                height = controller.height;
            }

            // A CharacterController's own description of itself: transform.position is the feet
            // and the capsule's two sphere centres sit one radius in from each end.
            var lift = Mathf.Max(radius, 0.01f);
            var lower = feet + (Vector3.up * lift);
            var upper = feet + (Vector3.up * Mathf.Max(height - lift, lift));

            var hits = Physics.OverlapCapsule(lower, upper, radius, ~0, QueryTriggerInteraction.Ignore);
            var named = new List<string>();
            for (var i = 0; i < hits.Length; i++)
            {
                var t = hits[i] != null ? hits[i].transform : null;
                if (t == null || t.IsChildOf(player))
                {
                    continue;
                }

                var path = Path(t);
                if (!named.Contains(path))
                {
                    named.Add(path);
                }
            }

            if (named.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < named.Count && i < 4; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(named[i]);
            }

            if (named.Count > 4)
            {
                sb.Append(" 외 " + (named.Count - 4) + "개");
            }

            return sb.ToString();
        }

        /// <summary>
        /// The last few links of an object's hierarchy path — enough to find it in the scene
        /// without a wall of text, and enough to tell <c>Map/Boundary/StoreyShell_B4/Lid</c> apart
        /// from a corridor's own ceiling. Same shape and same purpose as <c>EscapeTests.Path</c>.
        /// </summary>
        private static string Path(Transform? of)
        {
            if (of == null)
            {
                return "(collider 없음)";
            }

            var links = new List<string>();
            var walk = of;
            while (walk != null && links.Count < 4)
            {
                links.Add(walk.name);
                walk = walk.parent;
            }

            var sb = new StringBuilder();
            for (var i = links.Count - 1; i >= 0; i--)
            {
                sb.Append(links[i]);
                if (i > 0)
                {
                    sb.Append('/');
                }
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // The report.
        // ------------------------------------------------------------------

        private string Report(
            RaceDirector race, Vector3 finalPosition, float middleX, float middleZ, int seat, int announced, int chutes)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine();

            // Which building this was measured in. The NavMesh is a separate asset the scene
            // references by GUID, so a scene and a bake of different vintages is a real way for
            // every number below to be true of nothing — say what was loaded rather than assume.
            sb.AppendLine("씬 " + SoloScene + " · 시드 " + Seed + " · 투하구 " + chutes + "개 (필요 "
                          + (Drops * 2) + "개, 층마다 남북 한 쌍)");
            sb.AppendLine("── §01 하강, 층별 ────────────────────────────────────────────────");
            sb.AppendLine("층   외곽→중심 (B-010)                투하구                        §02 기록  중심에서");

            for (var i = 0; i < _legs.Count; i++)
            {
                var leg = _legs[i];

                var reach = leg.MiddleReachable
                    ? "PathComplete " + leg.MiddlePathMetres.ToString("0.0") + " m"
                    : leg.MiddleStatus + (leg.MiddleOnNavMesh ? string.Empty : " (중심에 NavMesh 없음)");
                if (leg.Warped)
                {
                    reach += " [경로 없음 — 테스트가 건너뜀]";
                }

                var drop = !leg.HadChute
                    ? (leg.Storey == RaceState.Storeys - 1 ? "도착점" : "투하구 없음")
                    : !leg.Carried
                        ? "삼키지 않음"
                        : "↓ B" + (leg.LandedStorey + 1) + "  외곽 " + leg.LandingRimMetres.ToString("0.0") + " m"
                          + (leg.LandedWhereTheChuteSaid
                              ? string.Empty
                              : " (착지점에서 " + leg.MissedDropPointBy.ToString("0.00") + " m)");

                var recorded = leg.RecordedStorey >= 0 ? "B" + (leg.RecordedStorey + 1) : "—";
                var here = (leg.MatchRunning ? string.Empty : " 경기 정지됨")
                           ;

                sb.AppendLine(
                    ("B" + (leg.Storey + 1)).PadRight(5) + reach.PadRight(33) + drop.PadRight(30)
                    + recorded.PadRight(9) + here);
            }

            // ── What each drop actually drops into ───────────────────────────────
            // Printed on a green run as well as a red one, and that is the point of it. The
            // storey table above says "↓ B2 외곽 25.0 m" whether or not those three metres exist,
            // because it is built out of the chute's own bookkeeping; this one is built out of
            // rays fired at the building. A row here that reads '3.00 m ↓ FloorTile' is the claim
            // "the drop lands on the floor below" with a measurement behind it, and a row that
            // names a Map/Boundary plate is the shell standing in the way of §01.
            sb.AppendLine();
            sb.AppendLine("── §01 투하구, 구멍 아래로 쏜 광선 ─────────────────────────────");
            sb.AppendLine("층   낙하 지점         첫 충돌까지   착지에서   무엇에 닿았나");

            for (var i = 0; i < _legs.Count; i++)
            {
                var leg = _legs[i];
                if (!leg.HadChute)
                {
                    continue;
                }

                var landingY = leg.DropPoint.y - Chute.DropHeightMetres;
                var hit = !leg.DropColumnFound
                    ? "아무것도 없다"
                    : leg.DropColumnHits + " (B" + (leg.DropColumnStorey + 1) + ")";
                var off = leg.DropColumnFound
                    ? (leg.DropColumnTopY - landingY).ToString("+0.00;-0.00;0.00") + " m"
                    : "—";

                sb.AppendLine(
                    ("B" + (leg.Storey + 1)).PadRight(5)
                    + leg.DropPoint.ToString("0.0").PadRight(18)
                    + (leg.DropColumnFound ? leg.DropColumnMetres.ToString("0.00") + " m" : "—").PadRight(14)
                    + off.PadRight(11)
                    + hit
                    + (leg.DropLandsOnTheLanding ? string.Empty : "  ← 착지가 아니다"));

                sb.AppendLine(
                    "     착지 NavMesh " + (leg.LandingOnNavMesh
                        ? "B" + (leg.LandingNavStorey + 1) + " (" + leg.LandingNavGapMetres.ToString("0.00") + " m)"
                        : "없음 (반경 " + NavSnapMetres.ToString("0.0") + " m)")
                    + " · 착지 위 천장까지 "
                    + (float.IsInfinity(leg.HeadroomOverTheLandingMetres)
                        ? "막힌 것이 없다"
                        : leg.HeadroomOverTheLandingMetres.ToString("0.00") + " m (투하 높이 "
                          + Chute.DropHeightMetres.ToString("0.0") + " m)")
                    + (string.IsNullOrEmpty(leg.DropPointInside)
                        ? " · 낙하 지점에 몸이 들어간다"
                        : " · 낙하 지점의 몸이 겹치는 것: " + leg.DropPointInside));
            }

            var rules = race.Rules;

            sb.AppendLine();
            sb.AppendLine("도착점 " + race.Finish.ToString("0.00") + " (판정은 X/Z만), 마지막 위치 "
                          + finalPosition.ToString("0.00") + ", 중심까지 "
                          + Flat(new Vector3(finalPosition.x - middleX, 0f, finalPosition.z - middleZ)).ToString("0.00")
                          + " m — 판정 반경은 " + RaceDirector.FinishRadiusMetres.ToString("0.0") + " m");
            sb.AppendLine("§02 Descended " + announced + "회 / 필요 " + Drops + "회 · 좌석 " + seat + "의 층 "
                          + (rules != null ? "B" + (rules[seat].Storey + 1) : "—")
                          + " · 승자 " + race.WinnerId + " · 완주 " + race.Finishers + "명 · 경과 "
                          + race.ElapsedSeconds.ToString("0") + "초");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Measurements.
        // ------------------------------------------------------------------

        /// <summary>
        /// Which storey a height belongs to — the same rule <c>MatchDirector.AttachChutes</c>
        /// uses to bind a chute: the floor's own y over the storey pitch. 0 is B1.
        /// </summary>
        private static int StoreyOf(float y)
        {
            return Mathf.RoundToInt(-y / StoreyPitchMetres);
        }

        /// <summary>
        /// Square-ring distance. <c>RadialStorey</c> lays its bands as square rings at Chebyshev
        /// radii, so "how far out is this" is a max and not a hypotenuse — a corner cell of the
        /// 외곽 고리 is on the rim even though it is 35 m from the middle as the crow flies.
        /// </summary>
        private static float Chebyshev(float dx, float dz)
        {
            return Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        private static float PathLength(Vector3[] corners)
        {
            var total = 0f;
            for (var i = 1; i < corners.Length; i++)
            {
                total += Vector3.Distance(corners[i - 1], corners[i]);
            }

            return total;
        }

        private static Chute Nearest(List<Chute> chutes, Vector3 from)
        {
            var best = chutes[0];
            var bestGap = Flat(best.transform.position - from);
            for (var i = 1; i < chutes.Count; i++)
            {
                var gap = Flat(chutes[i].transform.position - from);
                if (gap < bestGap)
                {
                    best = chutes[i];
                    bestGap = gap;
                }
            }

            return best;
        }

        private static Transform? FindPlayerRoot()
        {
            var motor = Object.FindFirstObjectByType<HorrorGame.Gameplay.Player.PlayerMotor>();
            return motor != null ? motor.transform : null;
        }
    }
}
#endif
