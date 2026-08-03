#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Race;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Race;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PlayerLook = HorrorGame.Gameplay.Player.PlayerLook;
using PlayerMotor = HorrorGame.Gameplay.Player.PlayerMotor;
using PlayerStance = HorrorGame.Gameplay.Player.PlayerStance;

namespace HorrorGame.Tests.PlayMode.Racing
{
    /// <summary>
    /// §01·§02: 맵 밖으로 나갈 수 없다. The building has to hold the runner in.
    /// <para>
    /// <b>Why this exists.</b> The owner played the solo build and reported
    /// <i>"맵밖으로 나갈수가있눈거같은데 이거 못나가게 막아야지"</i> — you can get outside
    /// the map. That is not a cosmetic complaint on this game. §02 is a race to the MIDDLE
    /// and the entire design is the maze that makes the middle expensive: three concentric
    /// bands, gates narrowing 4 → 2 → 1, a door on every gate, a creature per storey. A
    /// runner who steps through the wall walks the diagonal instead — no gates, no doors,
    /// no §06 — and wins. <b>An escapable map means there is no game.</b> None of the 113
    /// PlayMode tests looks for it: <c>DescentPlaythroughTests</c> proves the intended
    /// route exists, the NavMesh audit proves the graph is connected, and both are
    /// questions about what a runner CAN reach. This is the question about what they must
    /// NOT.
    /// </para>
    /// <para>
    /// <b>Adversarial on purpose.</b> A test that walks forward once and reports "still
    /// inside" proves nothing — the owner did not find this by walking forward once. So
    /// the runner is put down on hundreds of points of each storey's own walkable surface
    /// and driven at the outside at §05's 질주 speed, on a fan of bearings including the
    /// grazing ones a capsule squeezes through when it will not go straight in; then into
    /// the outermost dead ends, with the hop, over and over; then across a 투하구 mouth
    /// off-centre at sprint. Nothing is warped through anything: every metre of it is
    /// <c>PlayerMotor.Step</c> driving the scene's own <c>CharacterController</c> through
    /// the scene's own colliders, which is the only mover that can answer whether a wall
    /// is a wall.
    /// </para>
    /// <para>
    /// <b>The question asked of the end position is the project's own.</b>
    /// <see cref="NavMesh.SamplePosition"/> — is there floor under this runner, on this
    /// storey — is what <c>PlayerReachAudit</c>, <c>MonsterKillTests</c> and the NavMesh
    /// audit all ask, so a boundary that satisfies this test satisfies them. A hand-drawn
    /// box round the building would not: the map is 57.5 m square and its own generator
    /// decides where the rim is, so a magic box would be a second, drifting copy of the
    /// map's dimensions and would pass while the map moved.
    /// </para>
    /// <para>
    /// <b>Why one step out is the whole game and not a metre of it.</b>
    /// <c>MapSceneBuilder.BuildFloorSlab</c> pours a <c>FloorTile</c> across every zone's
    /// entire rectangle — 23 × 23 cells, 57.5 m square, one storey each — at 0.01 m under
    /// the kit's own floors, and its own remarks say what that means: it runs "under the
    /// walls and under the solid ground between corridors". It is out of the NavMesh bake
    /// (B-001) but its collider is still there, because §04's 발소리 raycast needs it. So
    /// §12's maze is a set of walls standing on an open plaza, and a runner who gets past
    /// one wall is not in a gap — they are on a continuous, walkable, unbaked floor that
    /// reaches from the rim to the middle of the storey. <c>CharacterController.stepOffset</c>
    /// is 0.40 m and the lip is 0.01 m, so stepping down onto it and back up again costs
    /// nothing. That is why the verdict below is "is there NavMesh under this body" and not
    /// "did they fall": on this map most of the outside is not a void, it is a shortcut.
    /// </para>
    /// <para>
    /// <b>Nothing is added to the scene, so nothing is added to the bake.</b> This fixture
    /// creates no <see cref="GameObject"/> other than the empty scene its teardown swaps
    /// in, and it never touches <c>NavMeshSurface</c>. The 220 markers / 3482 pairs /
    /// 8 islands the map writes on are measured by the editor audit against the baked
    /// asset before this test ever loads the scene, and a PlayMode test cannot change an
    /// asset it does not write.
    /// </para>
    /// <para>
    /// <b>The sample is deliberate, and it was not always.</b> The run that found the first
    /// holes reported 탈출 0회 on B1, B2, B4, B6 and B7 — and the five open ends it found are
    /// on all eight storeys. Nothing was sealed on those five floors; the stride of
    /// <see cref="SamplesPerStorey"/> points simply stepped over the leaking cell on five
    /// storeys out of eight, and the report gave a reader nothing to weigh a zero against.
    /// <b>Absence of a hit was absence of a sample, and it read as a sealed floor.</b> So the
    /// sample now has three sources — the rim, every 열린 끝 the storey's own surface can be
    /// shown to have (<see cref="FindOpenEnds"/>), and then the stride — and every attack that
    /// worked anywhere is made again on all eight storeys. What the report prints against a
    /// zero is how thoroughly the floor was looked at: the fraction of its walkable surface
    /// sampled, how many openings were found and attacked, and how many attacks were carried
    /// over from other floors.
    /// </para>
    /// <para>
    /// <b>A failure here is the owner's bug reproduced, and the coordinates are the
    /// point.</b> Each escape names its storey, its cell, the bearing it was driven on and
    /// what it ended up standing on, so the next person can go and look at that cell
    /// rather than re-derive the search.
    /// </para>
    /// <para>
    /// It lives in the predefined assembly because <c>MatchDirector</c> does, and an
    /// <c>.asmdef</c> cannot reference one. Compiles out of a player build on
    /// <c>UNITY_INCLUDE_TESTS</c>.
    /// </para>
    /// </summary>
    public sealed class EscapeTests
    {
        /// <summary>§13's seed for the layout under test — <c>SoloPlaytest.PlaytestSeed</c>.</summary>
        private const int Seed = 20260731;

        /// <summary>The scene <c>SoloPlaytest.BuildScene</c> writes, and the one the game loads.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>
        /// Metres between two floors. <c>MapKitCatalogue.StoreyMetres</c> is the authority and
        /// it lives in an editor assembly this one cannot reference, so it is quoted here in
        /// the same shape and for the same reason <c>DescentPlaythroughTests</c> quotes it.
        /// </summary>
        private const float StoreyPitchMetres = 3.75f;

        /// <summary>One cell. <c>MapKitCatalogue.GridMetres</c>, quoted for the same reason.</summary>
        private const float CellMetres = 2.5f;

        /// <summary>
        /// How far a NavMesh sample may reach for floor under the runner, metres — the number
        /// the whole verdict turns on, so here is the arithmetic that fixes it.
        /// <para>
        /// <b>Upper bound: how far outside the NavMesh a runner LEGALLY standing in a corridor
        /// can be.</b> The bake erodes <c>agentRadius</c> 0.5 m off every wall
        /// (<c>ProjectSettings/NavMeshAreas.asset</c>), the player capsule's radius is 0.30 m
        /// (<c>PlayerFeelHarnessMenu.BuildRig</c>), so a body pressed flat against a wall sits
        /// 0.20 m outside the surface and one jammed into a 90° inside corner 0.5·√2 − 0.3·√2
        /// = 0.28 m. Add the bake's own voxel of 0.10 m (<c>MapSceneBuilder</c> sets
        /// <c>voxelSize = agentRadius / 5</c>) and the legal worst case is about 0.4 m.
        /// </para>
        /// <para>
        /// <b>Lower bound: how far outside it a runner who has got THROUGH a wall must be.</b>
        /// A <c>Corridor_Straight_2m5</c> wall is 0.15 m thick, so the closest such a body can
        /// be to the corridor's own NavMesh is 0.30 (their radius) + 0.15 (the wall) + 0.50
        /// (the erosion on the far side) = 0.95 m, and 1.10 m where two tiles butt.
        /// </para>
        /// <para>
        /// 0.75 m sits between the two with room on both sides, and it is comfortably under
        /// half <see cref="StoreyPitchMetres"/> (1.875 m) — so a sample can never be answered
        /// by the floor of the storey below, which is the trap <c>MonsterKillTests</c> records
        /// having fallen into with a 4 m snap.
        /// </para>
        /// </summary>
        private const float FloorSnapMetres = 0.75f;

        /// <summary>
        /// How far the report will look for the nearest NavMesh once the verdict is already
        /// "off it", metres. Diagnostic only — it turns "outside" into "outside by 6.4 m",
        /// which is the difference between a marginal reading and a runner standing in the
        /// middle of the 개방 공간 between two bands. Four cells is far enough to cross the
        /// wall band that separates 외곽 from 중간.
        /// </summary>
        private const float WideProbeMetres = 4f * CellMetres;

        /// <summary>
        /// Metres above the storey's floor at which "the runner is on top of something"
        /// stops being an escape and becomes a plinth. A <c>DeadEnd_Cap</c> ships with one
        /// built in and <c>CharacterController.stepOffset</c> is 0.40 m
        /// (<see cref="GameConstants.PlayerStepOffsetMetres"/>), so a runner CAN climb onto
        /// it, and standing on it is 막힌 길 furniture rather than the outside of the
        /// building. Everything that is genuinely outside is at or below the floor: the zone
        /// slab <c>MapSceneBuilder.BuildFloorSlab</c> pours is 0.01 m under it, and the void
        /// past the slab is further down still.
        /// </summary>
        private const float ClimbedAboveFloorMetres = 0.25f;

        /// <summary>
        /// Seconds each outward shove lasts. At <see cref="GameConstants.RunnerSprintSpeed"/>
        /// that is 6.7 m — nearly three cells, so a runner who is through a wall is
        /// unambiguously through it rather than standing in the gap, and one who is not is
        /// pressed into it for a second and a bit with §05's fastest speed behind them.
        /// </summary>
        private const float ShoveSeconds = 1.2f;

        /// <summary>
        /// Seconds of the follow-up drive at the tower's axis, run only when the shove ended
        /// somewhere suspicious.
        /// <para>
        /// It is the exploit itself, and it is also what tells a real escape from a nook. A
        /// runner still inside the maze walks down a corridor and is on the NavMesh again;
        /// one who is out walks across the footprint §12's walls are standing on and gets
        /// further from every corridor with every metre. 1.5 s is 8.4 m — three cells and a
        /// half, enough to leave any recess and to cross the wall band between two rings.
        /// </para>
        /// </summary>
        private const float BreakInSeconds = 1.5f;

        /// <summary>
        /// How far off the NavMesh the shove has to end for the follow-up drive to be worth
        /// running, metres. Under the legal worst case worked out on
        /// <see cref="FloorSnapMetres"/>, so on a sealed map it fires rarely and on a broken
        /// one it fires exactly where the answer matters.
        /// </summary>
        private const float SuspiciousMetres = 0.45f;

        /// <summary>
        /// Points sampled per storey in the sweep. The whole surface is triangulated and
        /// deduplicated onto a <see cref="SampleGridMetres"/> grid first, so this is a stride
        /// over an already-even spread rather than a random draw; 160 against a storey of
        /// roughly 200 corridor cells is about one sample per cell.
        /// </summary>
        private const int SamplesPerStorey = 160;

        /// <summary>
        /// On top of the stride, the N points furthest from the tower's axis on each storey,
        /// always included. §12's alcoves are punched outward to Chebyshev radius 11 and are
        /// the furthest out anything is drawn, so they are the cells where a missing
        /// <c>DeadEndCap</c> opens onto nothing at all — never left to a stride.
        /// </summary>
        private const int OutermostAlwaysSampled = 24;

        /// <summary>
        /// Metres between two sample points. Half a cell, so a 2.5 m corridor cell gets a
        /// sample near each wall and one down the middle rather than one in the centre where
        /// nothing can be pushed through.
        /// </summary>
        private const float SampleGridMetres = CellMetres * 0.5f;

        /// <summary>
        /// Metres a sample point is lifted off the surface it was taken from. A fifth of
        /// <see cref="GameConstants.PlayerStepOffsetMetres"/>, so setting the capsule down never
        /// starts it interpenetrating the floor it is standing on and the first fixed step's
        /// gravity puts it back. Named rather than written twice because
        /// <see cref="FindOpenEnds"/> has to take it back off before it asks the bake a question
        /// with a 0.20 m radius on it.
        /// </summary>
        private const float SampleLiftMetres = 0.08f;

        // ==================================================================
        // 열린 끝 — the openings, found rather than stumbled on.
        // ==================================================================

        /// <summary>
        /// <b>Why the stride above is not the whole sample, and why this exists.</b>
        /// <para>
        /// The first version of this fixture reported 탈출 0회 on B1, B2, B4, B6 and B7 and
        /// found holes on B3, B5 and B8 — and the five open ends it found are, by the
        /// generator's own account, on all eight storeys. Nothing was sealed on the five
        /// quiet floors. <see cref="SamplesPerStorey"/> of a storey's several hundred
        /// walkable points is a stride, a stride starts where the sort leaves it, and on five
        /// storeys out of eight it simply stepped over the leaking cell. The report then read
        /// "탈출 0회" against those floors, which anybody would take for "sealed".
        /// <b>Absence of a hit was absence of a sample.</b>
        /// </para>
        /// <para>
        /// <b>Deliberate beats dense.</b> Raising the stride to cover a storey would multiply
        /// the run time by five and still leave the answer to luck. The opening itself is
        /// computable instead: walk every point of the storey's own walkable surface — all of
        /// it, before the stride throws any of it away — and ask, in eight directions,
        /// <i>does the floor stop here, and is there nothing standing where it stops?</i> A
        /// corridor end that never got its <c>DeadEndCap</c> answers yes; a corridor end with
        /// a wall on it answers no; and it answers the same on every storey that has one,
        /// because the question is asked of that storey's own geometry rather than of where a
        /// stride happened to land. Those points are then always shoved, on top of the rim and
        /// the stride, so nothing this fixture used to measure is measured less.
        /// </para>
        /// <para>
        /// Two things are asked of a direction and both have to hold. <b>The floor stops:</b>
        /// there is no NavMesh <see cref="OpenEndReachMetres"/> further out. <b>Nothing stands
        /// where it stops:</b> a ray <see cref="OpenEndWallReachMetres"/> long meets no
        /// collider that belongs to the building. The first alone is true beside every wall in
        /// the map, because the bake erodes half a metre off each one; the second alone is
        /// true in the middle of every corridor. Together they are the definition of an
        /// opening.
        /// </para>
        /// </summary>
        private const float OpenEndReachMetres = 0.7f;

        /// <summary>
        /// How near a probe point the walkable surface has to be to count as still being there,
        /// metres. Small, because the whole discrimination is in it: with
        /// <see cref="OpenEndReachMetres"/> at 0.7 m, this radius says that a point is worth
        /// attacking when the surface ends within 0.5 m of it in that direction, and leaves the
        /// middle of a corridor alone. <c>NavMesh.CalculateTriangulation</c> files triangle
        /// corners as well as centroids and a corner sits on the boundary itself, so every real
        /// edge in the building has points at 0 m from it in the pool.
        /// </summary>
        private const float OpenEndSampleRadiusMetres = 0.2f;

        /// <summary>
        /// How far a probe looks for the wall that ought to be there, metres.
        /// <para>
        /// A point flagged by <see cref="OpenEndReachMetres"/> is within 0.7 m of the edge of
        /// the walkable surface, and the bake erodes <c>agentRadius</c> 0.5 m off a wall, so
        /// the wall it is standing near is at most 1.2 m away. 1.4 m carries that with slack
        /// and stays under one <see cref="CellMetres"/>, so a ray cannot reach past the cell
        /// it started in and answer for somebody else's wall.
        /// </para>
        /// </summary>
        private const float OpenEndWallReachMetres = 1.4f;

        /// <summary>
        /// Heights above the floor a wall is looked for, metres. Two of them because one is a
        /// guess: a knee-high ledge and a chest-high gap are both "no wall" to a runner and
        /// only one of them is visible to a ray at either height. 0.30 m is under
        /// <see cref="GameConstants.PlayerStepOffsetMetres"/> — anything lower is furniture a
        /// runner walks over — and 1.40 m is the middle of the capsule.
        /// </summary>
        private static readonly float[] OpenEndProbeHeights = { 0.30f, 1.40f };

        /// <summary>
        /// The eight compass directions an opening is looked for in. Not four: the alcoves are
        /// punched outward on the diagonal at the corners of a square ring, and a corridor end
        /// that opens onto a corner is invisible to a ±X/±Z scan.
        /// </summary>
        private static readonly Vector3[] OpenEndDirections =
        {
            new Vector3(1f, 0f, 0f),
            new Vector3(-1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, -1f),
            new Vector3(0.70710678f, 0f, 0.70710678f),
            new Vector3(0.70710678f, 0f, -0.70710678f),
            new Vector3(-0.70710678f, 0f, 0.70710678f),
            new Vector3(-0.70710678f, 0f, -0.70710678f),
        };

        /// <summary>
        /// Most openings a storey contributes to the sweep. A cap and not a budget: a sealed
        /// storey finds a handful and a torn one finds hundreds, and the second case must
        /// still finish inside <see cref="TimeoutMilliseconds"/> rather than time the fixture
        /// out and report nothing at all. Whether it bound is printed, because a capped count
        /// is exactly the kind of number that reads as a measurement when it is a ceiling.
        /// </summary>
        private const int OpenEndsPerStorey = 96;

        /// <summary>
        /// Where <c>MapSceneBuilder</c> hangs the invisible boundary shell — its own
        /// <c>BoundaryRootName</c>, quoted for the same reason <see cref="StoreyPitchMetres"/>
        /// is: the generator lives in an editor assembly this one cannot reference.
        /// <para>
        /// It is excluded from "is there a wall here" on purpose. The shell is the net under
        /// §12's maze, not part of it: a corridor end backed by nothing but the shell is still
        /// an open end, and the runner who walks out of it is still on the plaza the maze
        /// stands on, skipping every gate. Counting the shell as a wall would let a net
        /// installed to stop people falling out of the world hide the holes it was installed
        /// around.
        /// </para>
        /// </summary>
        private const string BoundaryRootName = "Boundary";

        /// <summary>
        /// Bearings the sweep drives on, degrees off the outward normal.
        /// <para>
        /// 0 is straight at the nearest face of the building. ±30 and ±60 cover the diagonal,
        /// which is the outward direction at a corner of a square ring. <b>±80 is the seam
        /// attack and is the reason this list is not just {0}</b>: a capsule that will not
        /// pass through a gap head-on very often slides through it, because collide-and-slide
        /// converts the blocked component into travel along the wall and the next sweep
        /// starts from further along the seam.
        /// </para>
        /// </summary>
        private static readonly float[] ShoveBearings = { 0f, 30f, -30f, 60f, -60f, 80f, -80f };

        /// <summary>
        /// Frames between yields while sweeping. The engine needs one occasionally, and
        /// nothing in this test needs one at all —
        /// <see cref="CharacterController.Move(Vector3)"/> resolves against the physics scene
        /// on the call rather than at the next fixed step, and every wall in this building is
        /// static. Yielding per shove would cost nine thousand frames for no measurement.
        /// </summary>
        private const int ShovesPerFrame = 16;

        /// <summary>
        /// Whole-test cap, milliseconds. The sweep is on the order of half a million
        /// <c>CharacterController.Move</c> calls plus one 50 MB scene load; ten minutes is
        /// many times that, so reaching it means something hung rather than something was
        /// slow. Same figure and same reasoning as <c>DescentPlaythroughTests</c>.
        /// </summary>
        private const int TimeoutMilliseconds = 600000;

        /// <summary>
        /// Escapes written out in full in the failure message. The list is sorted deepest
        /// cause first and the counts are reported for all of them, but a message with nine
        /// thousand lines in it is a message nobody reads.
        /// </summary>
        private const int EscapesQuoted = 30;

        /// <summary>Seconds of pure gravity used to answer "and then what — do they land?".</summary>
        private const float FallWatchSeconds = 3f;

        // ------------------------------------------------------------------
        // The rig, resolved once per test.
        // ------------------------------------------------------------------

        private Transform? _player;
        private CharacterController? _controller;
        private PlayerMotor? _motor;
        private PlayerLook? _look;
        private PlayerStance? _stance;
        private MatchDirector? _director;
        private float _middleX;
        private float _middleZ;

        /// <summary>One runner's attempt to get out, recorded whether or not it worked.</summary>
        private sealed class Escape
        {
            public int Storey;
            public Vector3 From;
            public Vector3 To;
            public float BearingDegrees;

            /// <summary>
            /// The world direction the body was actually driven in, kept so the same attempt
            /// can be made again on the other seven storeys. A bearing on its own cannot do
            /// that: it is measured off whichever normal the attack started from.
            /// </summary>
            public Vector3 Heading;

            public string Attack = string.Empty;
            public float TravelledMetres;
            public float NavGapMetres;
            public int NavStorey = -1;
            public string Underfoot = string.Empty;
            public float UnderfootDropMetres;
            public float FellMetres;
            public bool CameToRest;
        }

        /// <summary>
        /// A place the floor stops and nothing stands where it stops — see
        /// <see cref="OpenEndReachMetres"/> for what those two clauses mean and why neither is
        /// enough on its own.
        /// </summary>
        private struct Opening
        {
            /// <summary>The walkable point the attempt starts from, just inside the hole.</summary>
            public Vector3 Inside;

            /// <summary>Which way the floor stopped. The bearing fan is swung around this.</summary>
            public Vector3 Outward;
        }

        /// <summary>
        /// Why each verdict came out the way it did, so a green run can be read rather than
        /// trusted.
        /// <para>
        /// The 발판 count is the one that matters and it is the one nobody read. A run of this
        /// fixture reported 발판 위에 올라선 <b>434/434</b> across the 투하구 sweep — every
        /// single verdict came from a body standing above its storey's floor, and not one from
        /// a body on it. That is arithmetically impossible on a building whose floors are where
        /// the map says: a runner who lands on B2 stands 0.09 m over B2's floor, which is under
        /// <see cref="ClimbedAboveFloorMetres"/>. A number like that is a defect announcing
        /// itself, so <see cref="Report"/> says out loud when a test's verdicts came entirely
        /// from one branch.
        /// </para>
        /// </summary>
        private sealed class Verdict
        {
            /// <summary>Judged contained because the body was above the floor, on furniture — or on something that should not be there.</summary>
            public int Climbed;

            /// <summary>Judged contained because the bake still has floor under the body. The healthy branch.</summary>
            public int OnTheFloor;

            /// <summary>Judged out.</summary>
            public int Out;

            /// <summary>Every verdict taken.</summary>
            public int Total
            {
                get { return Climbed + OnTheFloor + Out; }
            }
        }

        [SetUp]
        public void QuietenTheImporter()
        {
            // Loading the eight-storey scene re-emits Mirror's packaging complaint about an
            // immutable folder nobody is allowed to fix, and the Test Framework fails a test
            // on any unexpected LogError. Safe only because every step below is asserted.
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>
        /// Puts the building back. An eight-storey map left in the active scene has failed
        /// audio-occlusion tests that ran after it — see <c>InteractionPickupTests</c>, and
        /// four leaked scenes cost this project three false failures in one week.
        /// </summary>
        [UnityTearDown]
        public IEnumerator PutTheWorldBack()
        {
            _player = null;
            _controller = null;
            _motor = null;
            _look = null;
            _stance = null;
            _director = null;

            var solo = SceneManager.GetSceneByName(SoloScene);
            if (solo.IsValid() && solo.isLoaded)
            {
                var empty = SceneManager.CreateScene("EscapeTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(solo);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        // ==================================================================
        // 1 · The sweep: shove outward from everywhere, on every storey.
        // ==================================================================

        /// <summary>
        /// From hundreds of points on every storey's own walkable surface, sprint at the
        /// outside. Nobody ends up anywhere the map has no floor.
        /// <para>
        /// Three sources feed it, and the middle one is the fix for the reason this test used
        /// to report five sealed storeys that were not: the rim, always; every 열린 끝 the
        /// storey has, computed from its own surface and never left to a stride (see
        /// <see cref="OpenEndReachMetres"/>); and then the even stride over the rest. Then
        /// every attack that got anybody out anywhere is made again on all eight storeys, so a
        /// hole that exists on every floor is looked for on every floor by name.
        /// </para>
        /// </summary>
        [UnityTest]
        [Timeout(TimeoutMilliseconds)]
        public IEnumerator Sprinting_outward_from_every_part_of_every_storey_never_leaves_the_building()
        {
            yield return Prepare(stepTheMatch: false);

            var storeys = SampleTheBuilding(out var sampledTotal, out var deduped, out var walkable);
            Assert.That(sampledTotal, Is.GreaterThan(0),
                "NavMesh.CalculateTriangulation returned nothing that lands on a storey of this tower. Either the "
                + "scene loaded without its baked NavMesh asset, or the bake is of a different building — which is "
                + "B-009 exactly, and it would make every 'still inside' below true of nothing.");

            // Every open end on every storey, before a single body is put down. Measured off
            // the FULL walkable surface rather than off the 160 points the stride keeps, which
            // is the entire difference between this run and the one that called B1 sealed.
            var openings = FindOpenEnds(walkable, out var probed, out var capped);

            var escapes = new List<Escape>();
            var verdict = new Verdict();
            var shoves = 0;
            var attempted = new int[RaceState.Storeys];

            for (var storey = 0; storey < RaceState.Storeys; storey++)
            {
                // ── The 열린 끝 first, and each one attacked along its own normal ──
                // Not the tower's outward axis: a corridor end that opens west is walked out of
                // by going west, whichever side of the building it is on.
                var holes = openings[storey];
                if (holes != null)
                {
                    for (var h = 0; h < holes.Count; h++)
                    {
                        var hole = holes[h];
                        for (var b = 0; b < ShoveBearings.Length; b++)
                        {
                            var heading = Quaternion.Euler(0f, ShoveBearings[b], 0f) * hole.Outward;
                            attempted[storey]++;
                            TryOne(
                                escapes, verdict, storey, hole.Inside, heading, ShoveBearings[b],
                                "열린 끝으로 " + ShoveBearings[b].ToString("+0;-0;0") + "°");
                        }

                        if (++shoves % ShovesPerFrame == 0)
                        {
                            yield return null;
                        }
                    }
                }

                var points = storeys[storey];
                if (points == null || points.Count == 0)
                {
                    continue;
                }

                for (var i = 0; i < points.Count; i++)
                {
                    var from = points[i];
                    var outward = OutwardFrom(from);

                    for (var b = 0; b < ShoveBearings.Length; b++)
                    {
                        var heading = Quaternion.Euler(0f, ShoveBearings[b], 0f) * outward;
                        attempted[storey]++;
                        TryOne(
                            escapes, verdict, storey, from, heading, ShoveBearings[b],
                            "바깥으로 " + ShoveBearings[b].ToString("+0;-0;0") + "°");
                    }

                    if (++shoves % ShovesPerFrame == 0)
                    {
                        yield return null;
                    }
                }
            }

            // ── The same attack, on all eight floors ─────────────────────────────
            // The storeys of this tower are one column of the same generator's output, so a
            // hole is a shape and not a coordinate: if a runner walked out of (1,9) heading
            // west on B3, the honest next question is what (1,9) heading west does on the
            // other seven. Answering it by name is what turns "we found none on B1" from a
            // claim about B1 into a claim about how hard B1 was looked at.
            var replayed = new int[RaceState.Storeys];
            var replays = Signatures(escapes);
            for (var storey = 0; storey < RaceState.Storeys; storey++)
            {
                for (var r = 0; r < replays.Count; r++)
                {
                    var shape = replays[r];
                    if (shape.Storey == storey)
                    {
                        // Already tried, on the storey it was found on. Counting it again would
                        // inflate that storey's total with a repeat of itself and make the
                        // per-storey column mean two different things on two different rows.
                        continue;
                    }

                    var here = new Vector3(shape.From.x, FloorY(storey), shape.From.z);

                    // Only where that storey actually has floor at those coordinates. A replay
                    // started off the surface would be measuring the warp, not the wall.
                    if (!NavMesh.SamplePosition(here, out var hit, FloorSnapMetres, NavMesh.AllAreas))
                    {
                        continue;
                    }

                    attempted[storey]++;
                    replayed[storey]++;
                    TryOne(
                        escapes, verdict, storey, hit.position + (Vector3.up * SampleLiftMetres), shape.Heading,
                        shape.BearingDegrees, "다른 층에서 통한 수법 재현 — " + shape.Attack);
                }

                yield return null;
            }

            var summary = new StringBuilder();
            summary.AppendLine();
            summary.AppendLine("씬 " + SoloScene + " · 시드 " + Seed + " · NavMesh 표본 " + sampledTotal
                               + "개 (중복 제거 " + deduped + "개) · 열린 끝 탐침 " + probed + "회 · 밀어낸 횟수 "
                               + Total(attempted) + " · " + Branches(verdict));
            summary.AppendLine(HowHardWeLooked);
            for (var storey = 0; storey < RaceState.Storeys; storey++)
            {
                var points = storeys[storey];
                var holes = openings[storey];
                var pool = walkable[storey];
                var kept = points != null ? points.Count : 0;
                var floor = pool != null ? pool.Count : 0;

                summary.AppendLine(
                    "  B" + (storey + 1) + "  바닥 표본 " + floor + "개 중 " + kept + "개 ("
                    + (floor > 0 ? (100f * kept / floor).ToString("0.0") : "0.0") + "%) · 열린 끝 "
                    + (holes != null ? holes.Count : 0) + "곳" + (capped[storey] ? " (상한 "
                        + OpenEndsPerStorey + "에 걸렸다 — 세다 만 수다)" : " 전부") + " · 재현 " + replayed[storey]
                    + "회 · 시도 " + attempted[storey] + "회 · 탈출 " + CountOn(escapes, storey) + "회");
            }

            Report(escapes, summary,
                "§01 · 주자가 " + escapes.Count + "번 맵 밖으로 나갔다 — 각 층의 걸을 수 있는 바닥에서 "
                + "바깥쪽으로 " + GameConstants.RunnerSprintSpeed.ToString("0.0") + " m/s 로 "
                + ShoveSeconds.ToString("0.0") + "초 밀었을 뿐이다.",
                "[Test] §01 밖으로 못 나간다 — " + Total(attempted) + "번 밀어냈고 " + sampledTotal
                + "개 표본 전부 층 위에 남았다." + summary,
                verdict);
        }

        /// <summary>
        /// One attempt, start to verdict: the shove, the follow-up drive at the axis when the
        /// shove ended somewhere the surface does not answer for, and the record if it worked.
        /// <para>
        /// Lifted out of the sweep because there are now three places that make an attempt —
        /// the stride, the 열린 끝 and the replay — and three copies of the follow-up rule is
        /// three chances for one of them to quietly stop asking the second question.
        /// </para>
        /// </summary>
        private void TryOne(
            List<Escape> escapes, Verdict verdict, int storey, Vector3 from, Vector3 heading,
            float bearing, string attack)
        {
            var landed = Shove(from, heading, ShoveSeconds);

            // The follow-up only where the shove ended somewhere the surface does not answer
            // for. See BreakInSeconds — a runner still in the maze walks back onto the NavMesh,
            // one who is out gets further from it.
            if (Gap(landed) > SuspiciousMetres)
            {
                landed = Shove(landed, Inward(landed), BreakInSeconds);
                attack += " → 중심으로";
            }

            if (Contained(landed, storey, verdict))
            {
                return;
            }

            escapes.Add(Record(storey, from, landed, bearing, attack, heading));
        }

        /// <summary>
        /// Most attack shapes carried across to the other seven storeys. A ceiling on the cost
        /// of a badly torn map, not a judgement about how many are worth trying: 120 shapes is
        /// under a thousand extra shoves over the whole building, which is a tenth of what the
        /// stride already spends. They are taken in the order they were found, and the sweep
        /// walks each storey's own points furthest-from-the-axis first, so the shapes kept are
        /// the outermost ones — the holes that open onto the void rather than into the next
        /// band.
        /// </summary>
        private const int ReplayShapes = 120;

        /// <summary>
        /// The distinct shapes of attack that got somebody out, one per cell and heading — the
        /// list the replay pass walks. Deduplicated on the map's own cell grid rather than on
        /// the metre, because a hole is a cell and thirty escapes out of one cell are one hole
        /// found thirty times.
        /// </summary>
        private List<Escape> Signatures(List<Escape> escapes)
        {
            var shapes = new List<Escape>();
            var seen = new HashSet<long>();

            for (var i = 0; i < escapes.Count && shapes.Count < ReplayShapes; i++)
            {
                var e = escapes[i];

                // The heading rounded to 15°, so two escapes out of the same cell on
                // neighbouring bearings do not both buy a pass over the whole building.
                var facing = Mathf.RoundToInt(Mathf.Atan2(e.Heading.x, e.Heading.z) * Mathf.Rad2Deg / 15f);
                var gx = CellOf(e.From.x) + 4096;
                var gz = CellOf(e.From.z) + 4096;
                var key = ((long)(facing & 0xFF) << 44) | ((long)(gx & 0x3FFFFF) << 22) | (long)(gz & 0x3FFFFF);

                if (seen.Add(key))
                {
                    shapes.Add(e);
                }
            }

            return shapes;
        }

        // ==================================================================
        // 2 · The outermost dead ends, walked into and leant on.
        // ==================================================================

        /// <summary>
        /// The alcoves at the rim are the furthest out anything is drawn and each one is a
        /// blind cell facing the void. Walk into the cap, keep pushing, and hop at it.
        /// </summary>
        [UnityTest]
        [Timeout(TimeoutMilliseconds)]
        public IEnumerator The_outermost_dead_ends_hold_when_a_runner_leans_on_them()
        {
            yield return Prepare(stepTheMatch: false);

            var storeys = SampleTheBuilding(out var sampledTotal, out _, out var walkable);
            Assert.That(sampledTotal, Is.GreaterThan(0),
                "no NavMesh on any storey of this tower — see the same assertion in the sweep. Nothing below "
                + "measures the alcoves if the surface being sampled is not this building's.");

            var escapes = new List<Escape>();
            var verdict = new Verdict();
            var tried = 0;
            var cells = 0;
            var attacked = new int[RaceState.Storeys];
            var rimCells = new int[RaceState.Storeys];

            for (var storey = 0; storey < RaceState.Storeys; storey++)
            {
                var points = storeys[storey];
                if (points == null || points.Count == 0)
                {
                    continue;
                }

                // Furthest-out first. SampleTheBuilding puts OutermostAlwaysSampled points at
                // the head of every storey's list in descending Chebyshev order, so the head
                // IS the rim — the radius-11 alcoves, and the 외곽 band's own outer rail at 10
                // on the bearings where a storey has no alcove.
                var deepest = Mathf.Min(DeadEndsPerStorey, points.Count);
                for (var i = 0; i < deepest; i++)
                {
                    var from = points[i];
                    cells++;
                    rimCells[storey]++;

                    for (var b = 0; b < DeadEndBearings; b++)
                    {
                        var degrees = b * (360f / DeadEndBearings);
                        var heading = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;

                        // Twice: once walking into it, once hopping at it. §12 sizes the hop
                        // under stepOffset on purpose (GameConstants.JumpApexMetres 0.35 m
                        // against PlayerStepOffsetMetres 0.40) and PlayerMotor drops the frozen
                        // air velocity on any side contact for exactly this reason — a capsule
                        // has a rounded bottom and a hop into a low ledge used to carry a
                        // player up and over it. That argument is about crates; nobody has ever
                        // pointed it at the end wall of an alcove at the rim of the building.
                        for (var hop = 0; hop < 2; hop++)
                        {
                            var landed = Shove(from, heading, LeanSeconds, hop == 1);
                            tried++;
                            attacked[storey]++;

                            var attack = (hop == 1 ? "뛰면서 " : "걸어서 ") + degrees.ToString("000") + "°";
                            if (Gap(landed) > SuspiciousMetres)
                            {
                                landed = Shove(landed, Inward(landed), BreakInSeconds);
                                attack += " → 중심으로";
                            }

                            if (Contained(landed, storey, verdict))
                            {
                                continue;
                            }

                            escapes.Add(Record(storey, from, landed, degrees, attack, heading));
                        }
                    }

                    yield return null;
                }
            }

            var summary = new StringBuilder();
            summary.AppendLine();
            summary.AppendLine("씬 " + SoloScene + " · 시드 " + Seed + " · 가장 바깥 칸 " + cells + "개 (층당 최대 "
                               + DeadEndsPerStorey + "개) · " + DeadEndBearings + "방위 × 걷기/뛰기 = " + tried
                               + "회 · " + Branches(verdict));
            summary.AppendLine("한 번에 " + LeanSeconds.ToString("0.0") + "초, "
                               + (GameConstants.RunnerSprintSpeed * LeanSeconds).ToString("0.0")
                               + " m 어치를 벽에 붙여 밀었다.");
            summary.AppendLine(HowHardWeLooked);
            for (var storey = 0; storey < RaceState.Storeys; storey++)
            {
                var pool = walkable[storey];
                summary.AppendLine(
                    "  B" + (storey + 1) + "  바닥 표본 " + (pool != null ? pool.Count : 0) + "개 중 가장 바깥 "
                    + rimCells[storey] + "칸 · 시도 " + attacked[storey] + "회 · 탈출 "
                    + CountOn(escapes, storey) + "회");
            }

            Report(escapes, summary,
                "§12 · 막다른 골목 " + escapes.Count + "군데가 뚫려 있다. 반지름 11칸의 alcove는 건물에서 가장 "
                + "바깥이고, MapSketch가 DeadEndCap을 세 칸 안에 다른 cap이 있다는 이유로 포기하면 그 칸은 "
                + "Corridor_Straight_2m5 한 장이 된다 — 그 조각은 양쪽 끝이 열려 있다.",
                "[Test] §12 가장 바깥 막다른 길 " + cells + "칸, " + tried + "번 밀어도 전부 막혀 있다." + summary,
                verdict);
        }

        /// <summary>
        /// Outermost cells attacked per storey. Twelve against roughly forty cells on the rim
        /// band is the outer quarter of it, and the sample is taken furthest-out first so the
        /// twelve are the twelve that matter.
        /// </summary>
        private const int DeadEndsPerStorey = 12;

        /// <summary>
        /// Bearings tried at a dead end — every 30°, all the way round. Head-on is not the
        /// interesting one at a rim cell: the cell is blind on three sides and the corner
        /// between two of them is where a capsule wedges and slides.
        /// </summary>
        private const int DeadEndBearings = 12;

        /// <summary>
        /// Seconds leant on a dead-end wall. Twice <see cref="ShoveSeconds"/>: there is
        /// nowhere to go, so the whole 14 m of travel is spent pressed into whatever is
        /// there, which is what the owner was doing when they found this.
        /// </summary>
        private const float LeanSeconds = 2.5f;

        // ==================================================================
        // 3 · The 투하구, taken badly.
        // ==================================================================

        /// <summary>
        /// Sprint across a chute mouth off-centre, at every bearing and every offset out to
        /// the rim of the hole and past it. Either it takes you or you are still standing on
        /// the storey — never nowhere.
        /// </summary>
        [UnityTest]
        [Timeout(TimeoutMilliseconds)]
        public IEnumerator Sprinting_across_a_chute_mouth_off_centre_never_leaves_a_runner_nowhere()
        {
            yield return Prepare(stepTheMatch: true);

            var director = _director!;
            var chutes = Object.FindObjectsByType<Chute>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(chutes.Length, Is.GreaterThan(0),
                "§01's 투하구 are not bound on this map. MatchDirector.AttachChutes binds them at BeginMatch and "
                + "CheckChutes asks every one of them every tick; zero here means this test is sprinting across "
                + "bare floor and would pass without ever asking the question.");

            var escapes = new List<Escape>();
            var verdict = new Verdict();
            var runs = 0;
            var swallowed = 0;
            var swallowedDeadCentre = 0;
            var deadCentreRuns = 0;

            // §01 in one number: of the runners a 투하구 swallowed, how many were standing on
            // the floor of the storey below when the fall was over. It is the question the
            // 발판 count was silently answering "none" to — see Verdict — and it is worth
            // counting on its own, because "still on some floor somewhere" is a weaker claim
            // than "on the floor the chute drops onto".
            var landedBelow = 0;

            for (var c = 0; c < chutes.Length; c++)
            {
                var chute = chutes[c];
                if (chute == null)
                {
                    continue;
                }

                var mouth = chute.transform.position;
                var storey = StoreyOf(mouth.y);
                if (storey < 0 || storey >= RaceState.Storeys)
                {
                    continue;
                }

                for (var b = 0; b < ChuteBearings; b++)
                {
                    var degrees = b * (360f / ChuteBearings);
                    var heading = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
                    var sideways = Vector3.Cross(Vector3.up, heading);

                    for (var o = 0; o < ChuteOffsetsMetres.Length; o++)
                    {
                        var offset = ChuteOffsetsMetres[o];
                        var start = mouth - (heading * ChuteRunUpMetres) + (sideways * offset);

                        // Only from somewhere a runner could actually be standing. The 중심 is
                        // Chamber_Open_3x3, 7.5 m across, so most of this fits — but a run-up
                        // that starts inside the chamber's wall is not a run a person takes,
                        // and starting one would measure the warp rather than the chute.
                        if (!NavMesh.SamplePosition(start, out var hit, FloorSnapMetres, NavMesh.AllAreas))
                        {
                            continue;
                        }

                        // Checked before every run rather than once at the end. StepMatch is a
                        // no-op the moment EndMatch has run, so a match that stopped would
                        // hand back "not swallowed" for every remaining crossing — a green
                        // reading of a chute nobody asked. This is exactly the shape of green
                        // this repository has shipped before.
                        Assert.That(director.IsRunning, Is.True,
                            "the match stopped stepping after " + runs + " crossing(s), so nothing measured from "
                            + "here on is about a 투하구. MatchDirector.StepMatch returns on its first line once "
                            + "EndMatch has run — §02's 시간 초과 is the usual cause, and Prepare pushes it to "
                            + MatchTimeAllowanceSeconds.ToString("0") + " s precisely so this cannot be it. Read "
                            + "the [Match] or [Race] line logged above this failure for what closed it.");

                        var landed = Cross(director, hit.position, heading, ChuteCrossSeconds, out var taken);
                        runs++;
                        swallowed += taken ? 1 : 0;
                        if (Mathf.Approximately(offset, 0f))
                        {
                            deadCentreRuns++;
                            swallowedDeadCentre += taken ? 1 : 0;
                        }

                        // A runner the chute took belongs to the storey below now — that is the
                        // whole of §01 — so the same question is asked of the floor they were
                        // dropped onto rather than of the one they left.
                        var judgeOn = taken ? StoreyOf(landed.y) : storey;
                        if (judgeOn < 0 || judgeOn >= RaceState.Storeys)
                        {
                            judgeOn = storey;
                        }

                        // Counted before the follow-up drive moves anything: §01's claim is
                        // about where the fall PUT them, and one storey down standing on that
                        // storey's own floor is the whole of it.
                        if (taken && judgeOn == storey + 1
                            && landed.y <= FloorY(judgeOn) + ClimbedAboveFloorMetres)
                        {
                            landedBelow++;
                        }

                        if (Gap(landed) > SuspiciousMetres)
                        {
                            landed = Shove(landed, Inward(landed), BreakInSeconds);
                        }

                        if (Contained(landed, judgeOn, verdict))
                        {
                            continue;
                        }

                        escapes.Add(Record(
                            judgeOn, hit.position, landed, degrees,
                            (taken ? "삼켜진 뒤 " : "빗나간 뒤 ") + chute.name + " 중심에서 "
                            + offset.ToString("0.00") + " m",
                            heading));
                    }

                    yield return null;
                }
            }

            var summary = new StringBuilder();
            summary.AppendLine();
            summary.AppendLine("씬 " + SoloScene + " · 시드 " + Seed + " · 투하구 " + chutes.Length + "개 · 통과 "
                               + runs + "회 (" + ChuteBearings + "방위 × " + ChuteOffsetsMetres.Length
                               + "편차) · " + Branches(verdict));
            summary.AppendLine("입에 삼켜진 " + swallowed + "/" + runs + "회 — 정중앙으로만 세면 "
                               + swallowedDeadCentre + "/" + deadCentreRuns + "회. 입은 반지름 "
                               + Chute.MouthRadiusMetres.ToString("0.0") + " m 이고 질주 한 걸음은 "
                               + (GameConstants.RunnerSprintSpeed * GameConstants.FixedStep).ToString("0.00")
                               + " m 이라, 스쳐 지나가 놓치는 것은 있을 수 없다.");
            summary.AppendLine("삼켜진 뒤 바로 아래 층 바닥에 선 " + landedBelow + "/" + swallowed
                               + "회 — §01은 투하구가 아래 층 외곽에 내려놓는다고 말한다. "
                               + Chute.DropHeightMetres.ToString("0.0") + " m 를 떨어지고 "
                               + ChuteLandingSeconds.ToString("0.0")
                               + "초를 중력에 맡긴 뒤, 그 층 바닥에서 "
                               + ClimbedAboveFloorMetres.ToString("0.00") + " m 안에 있으면 센다.");

            Report(escapes, summary,
                "§01 · 투하구를 스쳐 지나간 주자 " + escapes.Count + "명이 층 위에도, 아래 층 착지에도 없다. "
                + "투하구는 MatchDirector.CheckChutes가 평면 거리로만 판정하는 방아쇠이고, 그 아래 바닥이 "
                + "뚫려 있다면 삼켜지지 않은 사람은 건물 밖으로 떨어진다.",
                "[Test] §01 투하구 " + chutes.Length + "개를 " + runs + "번 빗맞혀도 전부 어느 층 바닥엔가 남는다."
                + summary,
                verdict);
        }

        /// <summary>Bearings a chute mouth is crossed on — every 45°.</summary>
        private const int ChuteBearings = 8;

        /// <summary>
        /// Sideways offsets from the line through the mouth, metres. 0 is dead centre; 1.35
        /// and 1.5 straddle <see cref="Chute.MouthRadiusMetres"/> 1.4 m, which is the rim of
        /// the hole and the only offset at which "taken" and "missed" are both plausible; 2.0
        /// clears it entirely and asks whether the floor beside a hole is floor.
        /// </summary>
        private static readonly float[] ChuteOffsetsMetres = { 0f, 0.7f, 1.2f, 1.35f, 1.5f, 2.0f };

        /// <summary>Metres of run-up before the mouth. Beyond a cell, so the runner is at full speed on arrival.</summary>
        private const float ChuteRunUpMetres = 3f;

        /// <summary>
        /// Seconds of sprint across the mouth — 7.8 m, so the run starts a cell before it and
        /// ends two cells past it. A runner who is going to fall past the hole has done it
        /// well before the end, and one who is taken spends the rest of the run falling.
        /// </summary>
        private const float ChuteCrossSeconds = 1.4f;

        // ==================================================================
        // Setting the world up.
        // ==================================================================

        /// <summary>
        /// Loads the solo scene, starts the match, empties the building of §06 and resolves
        /// the rig — the same opening <c>DescentPlaythroughTests</c> and
        /// <c>MonsterKillTests</c> use, because it is the shipped path.
        /// </summary>
        /// <param name="stepTheMatch">
        /// Leave <c>MatchDirector</c> running. The 투하구 need it: <c>CheckChutes</c> is what
        /// swallows a runner and it only runs inside a step. The two geometry sweeps switch it
        /// off, and that is not a shortcut — with it on, a chute fires on any frame a shove
        /// ends near a mouth and teleports the body 25 m, so the thing being measured would be
        /// §01's own drop rather than whether a wall held. Nothing else in a headless step
        /// moves a player at all (<c>DescentPlaythroughTests.CarriedByTheMatch</c> is built on
        /// exactly that fact), so switching it off removes the drop and nothing besides.
        /// </param>
        private IEnumerator Prepare(bool stepTheMatch)
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

            Assert.That(director.RaceMode, Is.True,
                "§01's race is gated behind MatchDirector.RaceMode and it is off in this scene. What would be "
                + "measured below is the co-operative recovery map, which is not the tower this test is about.");

            // ── §06 out of the way, all of it ───────────────────────────────────
            // Same reason DescentPlaythroughTests destroys them and the same plural: §12-B③
            // stands one creature up in the middle of every storey, and this test puts a body
            // down on hundreds of points of every storey. Left alive they would kill the
            // runner mid-shove and the report would be "died on B5" instead of anything about
            // a wall. Destroyed rather than disabled, because MatchDirector calls
            // MonsterAgent.Simulate directly and a disabled component still answers a call.
            var creatures = Object.FindObjectsByType<MonsterAgent>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(creatures.Length, Is.GreaterThan(0),
                "no MonsterAgent in the scene at all. MatchDirector.PrepareCreatures builds one per "
                + "MatchMap.MonsterSpawns entry and refuses to begin the match if it cannot, so zero here means "
                + "the §06 half of this scene is not in the build and this test is running in a different world "
                + "from the one the owner played.");

            for (var i = 0; i < creatures.Length; i++)
            {
                if (creatures[i] != null)
                {
                    Object.Destroy(creatures[i].gameObject);
                }
            }

            yield return null;

            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            Assert.That(motor, Is.Not.Null, "no player rig in the scene, so nobody can walk into anything.");

            _motor = motor!;
            _player = _motor.transform;
            _controller = _player.GetComponent<CharacterController>();
            _look = _player.GetComponentInChildren<PlayerLook>();
            _stance = _player.GetComponent<PlayerStance>();
            _director = director;

            Assert.That(_controller, Is.Not.Null,
                "the player rig has no CharacterController. §05 moves the player with one and its radius, height "
                + "and stepOffset are the entire question of what fits through what — a rig without one cannot "
                + "answer whether a wall is a wall.");

            // §05 drives this from Update at frame rate. Handing it over means the only thing
            // that moves this body is a line in this file, so a shove is exactly as long as it
            // says it is and nothing drifts between two samples.
            _motor.SteppedExternally = true;
            _motor.MovementLocked = false;
            if (_stance != null)
            {
                _stance.ResetStance();
            }

            // ── Where the middle is, taken off §02 rather than written down ─────
            var race = director.GetComponent<RaceDirector>();
            Assert.That(race, Is.Not.Null,
                "MatchDirector has no RaceDirector on it — AttachRace never ran, so this test has no idea where "
                + "the middle of the tower is and 'outward' has no meaning.");
            Assert.That(race!.FinishFound, Is.True,
                "§02's 도착점 was not located, so RaceDirector.Finish is not the middle of anything. Every "
                + "outward bearing below would be measured from the world origin.");

            _middleX = race.Finish.x;
            _middleZ = race.Finish.z;

            // ── §02's clock, moved out of the way of a measurement that is not a race ──
            // RaceDirector.TimeoutSeconds defaults to ThreatCurve.FinalTierStartSeconds —
            // 1920 s, "§07이 마지막 단계를 지난다" — and CheckTimeout then retires the whole
            // field, RaceState goes Over, MatchDirector ends the match and StepMatch becomes a
            // no-op on its first line. The 투하구 sweep below spends nearly three seconds of
            // MATCH time on each of some hundreds of crossings, which lands squarely on that
            // number: the run would quietly stop swallowing anybody half way through and every
            // chute after that would be recorded as "missed" for a reason that has nothing to
            // do with the geometry. The property is public and settable and its own remarks
            // say it is meant to be overridden for playtests; this is that. The IsRunning
            // guard in the sweep is the belt to this braces — if the match stops anyway, the
            // test says so rather than reporting a number.
            race.TimeoutSeconds = MatchTimeAllowanceSeconds;

            director.enabled = stepTheMatch;

            // One step so §01's phase flags are the world's rather than the constructor's —
            // the trap MonsterKillTests hit first time out.
            director.StepMatch(GameConstants.FixedStep);
            yield return null;
        }

        // ==================================================================
        // Walking, and the two questions asked afterwards.
        // ==================================================================

        /// <summary>
        /// Puts the runner down and drives them at <paramref name="heading"/> with §05's
        /// fastest speed for <paramref name="seconds"/>, through the real
        /// <see cref="CharacterController"/> and the real colliders.
        /// <para>
        /// Not a warp and not a sweep the test does itself.
        /// <c>PlayerMotor.Step</c> is what the shipped game calls, so collide-and-slide,
        /// <c>stepOffset</c>, the slope limit, gravity and §05's speed table are all the
        /// ones the owner was playing with. The only thing this method decides is where to
        /// stand and which way to look.
        /// </para>
        /// <para>
        /// No <c>yield</c> inside it, on purpose.
        /// <see cref="CharacterController.Move(Vector3)"/> resolves against the physics scene
        /// on the call rather than at the next fixed step, and every wall in this building is
        /// static, so sixty steps in one frame produce the same path as sixty frames — and
        /// leave no frame in which anything else could move the body.
        /// </para>
        /// </summary>
        /// <param name="from">Where the attempt starts. On the NavMesh, so it is somewhere a runner could be.</param>
        /// <param name="heading">Flat direction to drive in. Normalised here.</param>
        /// <param name="seconds">How long to keep pushing.</param>
        /// <param name="hop">Ask <see cref="PlayerStance"/> for the jump on every step.</param>
        /// <returns>Where the body ended up.</returns>
        private Vector3 Shove(Vector3 from, Vector3 heading, float seconds, bool hop = false)
        {
            Stand();
            Place(from);
            Aim(heading);

            // §05 buys 질주 5.6 m/s with a twelve-second bar, and this test runs thousands of
            // shoves back to back. Refilled each time so every attempt is made at the same
            // speed — the fastest one the game grants — instead of the first few being sprints
            // and the rest being 달리기 4.5.
            _motor!.Stamina.Reset();

            var steps = Mathf.CeilToInt(seconds / GameConstants.FixedStep);
            var input = new MoveInput(1f, 0f, true);

            // The hop stops before the run does. §12 sizes the jump under the step offset —
            // GameConstants.JumpApexMetres 0.35 m against PlayerStepOffsetMetres 0.40 — so a
            // body caught at the apex is 0.35 m off the floor, which is inside
            // ClimbedAboveFloorMetres and would be read as "standing on the furniture" when it
            // is really "in the air". Landing first makes the verdict a verdict about the
            // floor. The apex takes 0.27 s to fall from; 0.6 s is over twice that.
            var lastHopStep = hop ? steps - Mathf.CeilToInt(HopLandingSeconds / GameConstants.FixedStep) : 0;

            for (var i = 0; i < steps; i++)
            {
                if (hop && _stance != null)
                {
                    // Ticked by hand because nothing else will: PlayerStance runs its cooldown
                    // and its crouch blend from its own Update, and this loop never gives a
                    // frame back.
                    _stance.Tick(GameConstants.FixedStep);
                    if (i < lastHopStep)
                    {
                        _stance.RequestJump();
                    }
                }

                _motor.Step(input, GameConstants.FixedStep);
            }

            return _player!.position;
        }

        /// <summary>Seconds at the end of a hopping run with no new jumps asked for, so the body is down when it is judged.</summary>
        private const float HopLandingSeconds = 0.6f;

        /// <summary>
        /// §05's gravity and nothing else, for <paramref name="seconds"/> — the answer to
        /// "and then what?". A body that stops has landed on something the map did not mean
        /// anyone to reach; one still moving at the end is falling out of the world, which is
        /// a different bug with a different fix, and the failure message says which.
        /// </summary>
        private Vector3 Fall(Vector3 from, float seconds)
        {
            Place(from);

            var steps = Mathf.CeilToInt(seconds / GameConstants.FixedStep);
            var still = new MoveInput(0f, 0f);
            for (var i = 0; i < steps; i++)
            {
                _motor!.Step(still, GameConstants.FixedStep);
            }

            return _player!.position;
        }

        /// <summary>
        /// One run at the same speed with the match stepping alongside it, so §01's 투하구
        /// gets exactly the 50 Hz sampling the shipped game gives it.
        /// </summary>
        /// <param name="director">The match, running.</param>
        /// <param name="from">Start of the run-up.</param>
        /// <param name="heading">Flat direction across the mouth.</param>
        /// <param name="seconds">How long to run.</param>
        /// <param name="taken">Set when the match moved the body — i.e. a chute swallowed them.</param>
        /// <returns>Where the body ended up.</returns>
        private Vector3 Cross(MatchDirector director, Vector3 from, Vector3 heading, float seconds, out bool taken)
        {
            Stand();
            Place(from);
            Aim(heading);
            _motor!.Stamina.Reset();

            var run = Mathf.CeilToInt(seconds / GameConstants.FixedStep);

            // Enough steps after a drop for §05's gravity to finish it. Chute.DropHeightMetres
            // is 3.0 m, which is 0.78 s of free fall — "a fall, not a teleport", and the last
            // half second of every storey in the shipped game. Judging the body before it
            // lands would measure the air.
            var settle = Mathf.CeilToInt(ChuteLandingSeconds / GameConstants.FixedStep);

            var forward = new MoveInput(1f, 0f, true);
            var still = new MoveInput(0f, 0f);
            taken = false;

            for (var i = 0; i < run + settle; i++)
            {
                _motor.Step(taken || i >= run ? still : forward, GameConstants.FixedStep);
                var afterOwnStep = _player!.position;

                director.StepMatch(GameConstants.FixedStep);

                // The only thing in a headless race step that moves a player is CheckChutes,
                // and it moves them from the middle of a storey to a corner of the one below —
                // 25 m in plan on this map. One metre in one step is nine times what §05's
                // fastest speed covers in a step and a small fraction of what the drop does,
                // so it separates the two cleanly. Same reading, same reason, as
                // DescentPlaythroughTests.CarriedByTheMatch.
                if ((_player.position - afterOwnStep).sqrMagnitude > 1f)
                {
                    taken = true;
                }
            }

            return _player!.position;
        }

        /// <summary>Seconds given to §05's gravity after a 투하구 fires, so the body is on the landing when it is judged.</summary>
        private const float ChuteLandingSeconds = 1.5f;

        /// <summary>
        /// Seconds of match time §02's 시간 초과 is pushed out to while the chutes are swept.
        /// Ten hours against the roughly half an hour the sweep spends — see the note in
        /// <see cref="Prepare"/>, which is where the reasoning lives.
        /// </summary>
        private const float MatchTimeAllowanceSeconds = 36000f;

        /// <summary>
        /// Whether the map still has floor for a body standing here — the same question
        /// <c>PlayerReachAudit</c> and the NavMesh audit ask, at the radius
        /// <see cref="FloorSnapMetres"/> argues for.
        /// <para>
        /// A runner standing above the floor on a <c>DeadEnd_Cap</c>'s plinth is counted in
        /// and tallied separately: they have climbed on the furniture, which is inside the
        /// building by definition, and their feet being 0.4 m up puts them further from the
        /// surface than a body on it. See <see cref="ClimbedAboveFloorMetres"/>.
        /// </para>
        /// </summary>
        private bool Contained(Vector3 where, int storey, Verdict verdict)
        {
            if (where.y > FloorY(storey) + ClimbedAboveFloorMetres)
            {
                verdict.Climbed++;
                return true;
            }

            if (NavMesh.SamplePosition(where, out _, FloorSnapMetres, NavMesh.AllAreas))
            {
                verdict.OnTheFloor++;
                return true;
            }

            verdict.Out++;
            return false;
        }

        /// <summary>
        /// Metres to the nearest NavMesh, or infinity past <see cref="FloorSnapMetres"/>.
        /// Cheap, and only used to decide whether pressing on is worth the steps — the
        /// verdict itself is <see cref="Contained"/>.
        /// </summary>
        private static float Gap(Vector3 where)
        {
            return NavMesh.SamplePosition(where, out var hit, FloorSnapMetres, NavMesh.AllAreas)
                ? Vector3.Distance(where, hit.position)
                : float.PositiveInfinity;
        }

        /// <summary>
        /// Notes one escape. Cheap on purpose — where it started, where it ended and what was
        /// done to it, and nothing that costs a physics query or a second of simulation.
        /// A broken map produces thousands of these and the forensics are only worth taking
        /// on the ones the report is going to print; see <see cref="Investigate"/>.
        /// </summary>
        private Escape Record(int storey, Vector3 from, Vector3 to, float bearing, string attack, Vector3 heading)
        {
            return new Escape
            {
                Storey = storey,
                From = from,
                To = to,
                BearingDegrees = bearing,
                Heading = heading,
                Attack = attack,
                TravelledMetres = Flat(to - from),
                NavGapMetres = float.PositiveInfinity,
            };
        }

        /// <summary>
        /// Everything the next person needs to go and look at the cell: how far the nearest
        /// floor really is, what the body is standing on, and whether it is standing at all.
        /// Run after the sort and only on the escapes the failure message quotes, so the cost
        /// is bounded at <see cref="EscapesQuoted"/> fall-watches however broken the map is.
        /// </summary>
        private void Investigate(Escape escape)
        {
            if (NavMesh.SamplePosition(escape.To, out var wide, WideProbeMetres, NavMesh.AllAreas))
            {
                escape.NavGapMetres = Vector3.Distance(escape.To, wide.position);
                escape.NavStorey = StoreyOf(wide.position.y);
            }

            escape.Underfoot = Underfoot(escape.To, out var drop);
            escape.UnderfootDropMetres = drop;

            // "And then what?" — see Fall.
            var restedAt = Fall(escape.To, FallWatchSeconds);
            escape.FellMetres = escape.To.y - restedAt.y;
            escape.CameToRest = _controller != null && _controller.isGrounded;
        }

        /// <summary>Room for every collider a downward ray can meet through eight storeys of building.</summary>
        private readonly RaycastHit[] _underfoot = new RaycastHit[32];

        /// <summary>
        /// What is directly under the runner, named by its place in the hierarchy. This is
        /// the line that says <em>which</em> defect was found: a name under a zone's
        /// <c>Tiles</c> is §12's own geometry, one under its <c>Floor</c> is the zone slab —
        /// which <c>MapSceneBuilder</c> pours across the storey's whole 57.5 m rectangle,
        /// "under the walls and under the solid ground between corridors", and deliberately
        /// keeps out of the NavMesh bake. Standing on that is standing on the plaza the maze
        /// was built on top of, and a runner there can walk to the middle in a straight line.
        /// </summary>
        private string Underfoot(Vector3 feet, out float dropMetres)
        {
            // Started half a metre up so the ray is not launched from the exact plane it is
            // looking for, and long enough to fall past all eight storeys.
            //
            // The runner's own body has to be skipped by name rather than by trusting PhysX to
            // drop a ray that begins inside a convex shape — the origin sits inside the
            // player's own 1.75 m capsule, and "the thing under me is me" is precisely the
            // shape of a diagnostic that sends somebody to the wrong cell.
            var from = feet + (Vector3.up * 0.5f);
            var reach = (RaceState.Storeys * StoreyPitchMetres) + 10f;
            var found = Physics.RaycastNonAlloc(
                from, Vector3.down, _underfoot, reach, ~0, QueryTriggerInteraction.Ignore);

            var nearest = -1;
            for (var i = 0; i < found; i++)
            {
                var t = _underfoot[i].collider != null ? _underfoot[i].collider.transform : null;
                if (t == null || (_player != null && t.IsChildOf(_player)))
                {
                    continue;
                }

                if (nearest < 0 || _underfoot[i].distance < _underfoot[nearest].distance)
                {
                    nearest = i;
                }
            }

            if (nearest < 0)
            {
                dropMetres = float.PositiveInfinity;
                return "아무것도 없다 (" + reach.ToString("0") + " m 아래까지)";
            }

            dropMetres = feet.y - _underfoot[nearest].point.y;
            return Path(_underfoot[nearest].collider != null ? _underfoot[nearest].collider.transform : null);
        }

        /// <summary>The last few links of an object's hierarchy path — enough to find it in the scene without a wall of text.</summary>
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

        // ==================================================================
        // Placing and aiming.
        // ==================================================================

        /// <summary>
        /// Puts a body somewhere.
        /// <para>
        /// A <c>CharacterController</c> ignores writes to <c>transform.position</c> while it
        /// is enabled, which is exactly the kind of silent no-op that makes a test pass
        /// against a player who never moved. <see cref="Physics.SyncTransforms"/> afterwards
        /// because the next thing that happens is a sweep from this position and the physics
        /// scene has to have been told about it.
        /// </para>
        /// </summary>
        private void Place(Vector3 feet)
        {
            var controller = _controller;
            if (controller == null)
            {
                _player!.position = feet;
                return;
            }

            controller.enabled = false;
            _player!.position = feet;
            controller.enabled = true;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// The same body at the start of every attempt: upright, feet down, no jump queued,
        /// <c>stepOffset</c> back at <see cref="GameConstants.PlayerStepOffsetMetres"/>.
        /// <para>
        /// <c>PlayerStance.ResetStance</c> is written for exactly this — "for §09's respawn
        /// and for a teleport, where the geometry the sweep would measure is not the geometry
        /// the player is about to be in". It also undoes the one piece of state a hopping
        /// attempt leaves behind: <c>ApplyStepOffset</c> zeroes the free step for the duration
        /// of a hop, and a body still holding <c>_airborne</c> when the next attempt starts
        /// would walk into the next wall with no step at all — an attempt made with a weaker
        /// runner than the game ships, which is the direction a containment test must never
        /// err in.
        /// </para>
        /// </summary>
        private void Stand()
        {
            if (_stance != null)
            {
                _stance.ResetStance();
            }
        }

        /// <summary>
        /// Points the body where it is about to run. §05 makes the mouse the definition of
        /// forward — <c>SpeedResolver.ResolveVelocity</c> takes a yaw and
        /// <c>PlayerMotor.Step</c> reads it off <see cref="PlayerLook"/> — so aiming the look
        /// and pressing W is what a player does, and it is the only way the runner gets
        /// §05's full 전진 100% multiplier rather than a strafe's 85%.
        /// </summary>
        private void Aim(Vector3 heading)
        {
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.000001f)
            {
                return;
            }

            // Yaw clockwise from +Z, the convention MathX.DirectionFromYaw and
            // SpeedResolver.ResolveVelocity both use.
            var yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;

            if (_look != null)
            {
                _look.SetLook(yaw, 0f);
                return;
            }

            // A rig with no PlayerLook falls back to its own rotation inside PlayerMotor.Step.
            _player!.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ==================================================================
        // Where the outside is.
        // ==================================================================

        /// <summary>
        /// The direction of the nearest outside from a point — the outward normal of the
        /// nearest face of the building.
        /// <para>
        /// Chebyshev and not radial, because <c>RadialStorey</c> builds square rings: the
        /// nearest way out of a 57.5 m square is straight at whichever wall is closest, which
        /// is the dominant axis. The diagonal a corner cell wants is covered by the ±60°
        /// bearings in <see cref="ShoveBearings"/>.
        /// </para>
        /// </summary>
        private Vector3 OutwardFrom(Vector3 point)
        {
            var dx = point.x - _middleX;
            var dz = point.z - _middleZ;

            // Inside the 중심 chamber both are near zero and "outward" has no direction. +X is
            // arbitrary and says so; the bearing fan sprays 160° of the compass from it anyway,
            // and a runner standing on the finish is not the case this test is about.
            if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dz) < 0.01f)
            {
                return Vector3.right;
            }

            return Mathf.Abs(dx) >= Mathf.Abs(dz)
                ? new Vector3(Mathf.Sign(dx), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(dz));
        }

        /// <summary>Straight at the tower's axis — the prize an escape is worth, and the drive that proves one.</summary>
        private Vector3 Inward(Vector3 point)
        {
            var toward = new Vector3(_middleX - point.x, 0f, _middleZ - point.z);
            return toward.sqrMagnitude < 0.000001f ? Vector3.right : toward.normalized;
        }

        // ==================================================================
        // Sampling the walkable surface.
        // ==================================================================

        /// <summary>
        /// Every storey's walkable surface, as points a body can be set down on, sorted
        /// furthest-from-the-axis first.
        /// <para>
        /// Taken off <see cref="NavMesh.CalculateTriangulation"/> rather than off the
        /// generator, and that is the whole reason this test survives somebody changing the
        /// tiler next month: the sample set is whatever the bake says is walkable in the
        /// scene on disk, so a new piece, a new band or a new alcove rule is sampled the day
        /// it lands without a line changing here. Triangle corners AND centroids, because a
        /// corner is the closest legal standing point to a wall — which is precisely where a
        /// runner leans when they are trying to get out — and a centroid covers the open
        /// middle a corner list would miss.
        /// </para>
        /// </summary>
        /// <param name="sampled">Points kept, over the whole building.</param>
        /// <param name="deduped">Points thrown away as duplicates of a <see cref="SampleGridMetres"/> cell.</param>
        /// <param name="pools">
        /// Every walkable point, per storey, before the stride throws any away — sorted
        /// furthest-from-the-axis first like the picked list. Handed back because
        /// <see cref="FindOpenEnds"/> has to see the whole surface: an opening the stride
        /// stepped over is exactly the defect that made this fixture call five storeys sealed,
        /// and looking for it in the survivors of the same stride would reproduce it. It is
        /// also what "표본 187/812개" in the report is measured against, so a zero can be read
        /// as a coverage rather than as a verdict.
        /// </param>
        private List<Vector3>?[] SampleTheBuilding(
            out int sampled, out int deduped, out List<Vector3>?[] pools)
        {
            var triangulation = NavMesh.CalculateTriangulation();
            var vertices = triangulation.vertices;
            var indices = triangulation.indices;

            pools = new List<Vector3>?[RaceState.Storeys];
            var seen = new HashSet<long>();
            deduped = 0;
            sampled = 0;

            // A scene loaded without its baked asset gives this back empty, and the callers
            // turn that into a named assertion. Falling over with a NullReferenceException
            // here instead would report the symptom two layers away from the cause.
            if (vertices == null || indices == null || indices.Length < 3)
            {
                return pools;
            }

            for (var t = 0; t + 2 < indices.Length; t += 3)
            {
                var a = vertices[indices[t]];
                var b = vertices[indices[t + 1]];
                var c = vertices[indices[t + 2]];

                Consider(pools, seen, a, ref deduped);
                Consider(pools, seen, b, ref deduped);
                Consider(pools, seen, c, ref deduped);
                Consider(pools, seen, (a + b + c) / 3f, ref deduped);
            }

            var picked = new List<Vector3>?[RaceState.Storeys];
            for (var storey = 0; storey < RaceState.Storeys; storey++)
            {
                var pool = pools[storey];
                if (pool == null || pool.Count == 0)
                {
                    continue;
                }

                // Furthest out first, and fully ordered so two runs of this test sample the
                // same points. NavMesh triangulation order is stable for a given asset but
                // "stable" is not "sorted", and a test that samples a different 160 points
                // each run reports a different set of escapes each run.
                pool.Sort((p, q) =>
                {
                    var byRadius = Radius(q).CompareTo(Radius(p));
                    if (byRadius != 0)
                    {
                        return byRadius;
                    }

                    var byX = p.x.CompareTo(q.x);
                    return byX != 0 ? byX : p.z.CompareTo(q.z);
                });

                var take = new List<Vector3>();
                var kept = new HashSet<int>();

                // The rim, always. See OutermostAlwaysSampled.
                var rim = Mathf.Min(OutermostAlwaysSampled, pool.Count);
                for (var i = 0; i < rim; i++)
                {
                    take.Add(pool[i]);
                    kept.Add(i);
                }

                // Then an even stride over the rest, so the middle of the storey is covered as
                // well as its edge — a dead end opening into the wall band between 중간 and
                // 외곽 is as fatal as one opening onto the sky, because both of them skip
                // §12-A's gates.
                var stride = Mathf.Max(1, pool.Count / Mathf.Max(1, SamplesPerStorey - rim));
                for (var i = 0; i < pool.Count && take.Count < SamplesPerStorey; i += stride)
                {
                    if (kept.Add(i))
                    {
                        take.Add(pool[i]);
                    }
                }

                picked[storey] = take;
                sampled += take.Count;
            }

            return picked;
        }

        /// <summary>
        /// Every 열린 끝 in the building: the places a storey's floor stops with nothing
        /// standing where it stops.
        /// <para>
        /// <b>This is the deliberate half of the sample, and it is why a hole on all eight
        /// storeys is now found on all eight.</b> It walks the whole of each storey's walkable
        /// surface — <paramref name="walkable"/>, before the stride keeps its 160 — and asks
        /// each point the two questions <see cref="OpenEndReachMetres"/> sets out, in the eight
        /// directions of <see cref="OpenEndDirections"/>. Nothing about it depends on where a
        /// stride happened to land or on which storey somebody got lucky on, so two storeys
        /// built the same way answer the same.
        /// </para>
        /// <para>
        /// Order matters and is furthest-from-the-axis first, because the pool it walks is
        /// already sorted that way: if <see cref="OpenEndsPerStorey"/> ever binds, what it keeps
        /// is the rim — where a hole opens onto the void — rather than an arbitrary 96.
        /// </para>
        /// </summary>
        /// <param name="walkable">Every walkable point per storey, from <see cref="SampleTheBuilding"/>.</param>
        /// <param name="probed">Point-and-direction pairs actually examined, over the whole building.</param>
        /// <param name="capped">Per storey: whether <see cref="OpenEndsPerStorey"/> bound, so the count is a ceiling.</param>
        private List<Opening>?[] FindOpenEnds(List<Vector3>?[] walkable, out int probed, out bool[] capped)
        {
            var found = new List<Opening>?[RaceState.Storeys];
            capped = new bool[RaceState.Storeys];
            probed = 0;

            for (var storey = 0; storey < RaceState.Storeys; storey++)
            {
                var pool = walkable[storey];
                if (pool == null || pool.Count == 0)
                {
                    continue;
                }

                var seen = new HashSet<long>();
                var here = new List<Opening>();

                for (var i = 0; i < pool.Count && here.Count < OpenEndsPerStorey; i++)
                {
                    var point = pool[i];

                    for (var d = 0; d < OpenEndDirections.Length && here.Count < OpenEndsPerStorey; d++)
                    {
                        var outward = OpenEndDirections[d];
                        probed++;

                        // One: does the floor stop? Asked at the surface's own height —
                        // SampleLiftMetres taken back off, because the probe radius is small
                        // enough that the 0.08 m a sample is lifted by would eat most of it and
                        // the answer would start depending on the lift rather than on the map.
                        // A tight radius on purpose: this has to mean "is there surface AT that
                        // point" rather than "is there surface somewhere near it", which is what
                        // FloorSnapMetres would ask and would make every point in the building
                        // look like it had floor all round it.
                        var reach = point + (outward * OpenEndReachMetres) - (Vector3.up * SampleLiftMetres);
                        if (NavMesh.SamplePosition(reach, out _, OpenEndSampleRadiusMetres, NavMesh.AllAreas))
                        {
                            continue;
                        }

                        // Two: is anything standing where it stopped?
                        if (Blocked(point, outward))
                        {
                            continue;
                        }

                        // One seed per cell per direction. A three-metre opening produces a
                        // point every 1.25 m along it otherwise, and seven bearings apiece —
                        // the cap would then be spent on one hole.
                        var gx = CellOf(point.x) + 4096;
                        var gz = CellOf(point.z) + 4096;
                        var key = ((long)d << 44) | ((long)(gx & 0x3FFFFF) << 22) | (long)(gz & 0x3FFFFF);
                        if (!seen.Add(key))
                        {
                            continue;
                        }

                        here.Add(new Opening { Inside = point, Outward = outward });
                    }
                }

                capped[storey] = here.Count >= OpenEndsPerStorey;
                found[storey] = here;
            }

            return found;
        }

        /// <summary>Room for every collider a probe can meet inside one cell.</summary>
        private readonly RaycastHit[] _outward = new RaycastHit[16];

        /// <summary>
        /// Is there something of the building's own standing between this point and the
        /// outside? The boundary shell does not count — see <see cref="BoundaryRootName"/> —
        /// and neither does the runner's own capsule, which the probe is fired from inside of.
        /// </summary>
        private bool Blocked(Vector3 from, Vector3 outward)
        {
            for (var h = 0; h < OpenEndProbeHeights.Length; h++)
            {
                var origin = from + (Vector3.up * OpenEndProbeHeights[h]);
                var hits = Physics.RaycastNonAlloc(
                    origin, outward, _outward, OpenEndWallReachMetres, ~0, QueryTriggerInteraction.Ignore);

                var wall = false;
                for (var i = 0; i < hits && !wall; i++)
                {
                    var t = _outward[i].collider != null ? _outward[i].collider.transform : null;
                    if (t == null || (_player != null && t.IsChildOf(_player)) || UnderTheBoundary(t))
                    {
                        continue;
                    }

                    wall = true;
                }

                // Open at ANY height is open. A runner ducks under a beam and vaults nothing:
                // §12's walls run floor to ceiling, so a height with no collider in it is a
                // height a body goes through.
                if (!wall)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether a collider belongs to the invisible net rather than to §12's maze. Walked up
        /// the hierarchy rather than matched on the leaf, because the shell's plates are named
        /// <c>Floor</c>, <c>Lid</c> and <c>Wall_XMin</c> — names the map's own tiles could
        /// plausibly carry one day.
        /// </summary>
        private static bool UnderTheBoundary(Transform? of)
        {
            var walk = of;
            while (walk != null)
            {
                if (string.Equals(walk.name, BoundaryRootName, System.StringComparison.Ordinal))
                {
                    return true;
                }

                walk = walk.parent;
            }

            return false;
        }

        /// <summary>Files one triangulation point under its storey, once per <see cref="SampleGridMetres"/> cell.</summary>
        private static void Consider(List<Vector3>?[] pools, HashSet<long> seen, Vector3 point, ref int deduped)
        {
            var storey = StoreyOf(point.y);
            if (storey < 0 || storey >= RaceState.Storeys)
            {
                return;
            }

            // Only the storey's own floor. §04's 갤러리 stands GalleryRiseMetres above the
            // floor it overlooks and a 계단 climbs a whole storey; neither is a place this
            // test's arithmetic about "the floor of B_n" is true of, and a sample taken on one
            // would be judged against the wrong height.
            if (Mathf.Abs(point.y - FloorY(storey)) > FloorSnapMetres)
            {
                return;
            }

            // Exact rather than a hash — the two coordinates are packed into their own 22-bit
            // fields with a bias, so two different cells cannot collide and be silently
            // dropped. The building is 57.5 m square, so ±4096 grid steps is four hundred
            // times the room it needs.
            var gx = Mathf.RoundToInt(point.x / SampleGridMetres) + 4096;
            var gz = Mathf.RoundToInt(point.z / SampleGridMetres) + 4096;
            var key = ((long)storey << 44) | ((long)(gx & 0x3FFFFF) << 22) | (long)(gz & 0x3FFFFF);

            if (!seen.Add(key))
            {
                deduped++;
                return;
            }

            // Lifted off the surface by a fifth of the step offset, so setting the capsule
            // down never starts it interpenetrating the floor it is standing on. The first
            // fixed step's gravity puts it back.
            (pools[storey] ??= new List<Vector3>()).Add(point + (Vector3.up * SampleLiftMetres));
        }

        // ==================================================================
        // The report.
        // ==================================================================

        /// <summary>
        /// Fails with the whole table if anybody got out, and logs the same table if nobody
        /// did. Not a chain of <c>Assert.That</c> inside the loops: the first one to fire
        /// would end the run and "B1 leaks at (12, 45)" would be the entire report of a
        /// building with eight storeys in it. One run therefore names every hole rather than
        /// the first.
        /// </summary>
        private void Report(
            List<Escape> escapes, StringBuilder summary, string headline, string greenLog, Verdict verdict)
        {
            // ── The 434/434 line, said out loud this time ────────────────────────
            // Every verdict this test took came from one branch of Contained, and that is a
            // reading about the building rather than about the test. It is carried alongside
            // the escape list rather than instead of it: a map can be both leaky and standing
            // on a plate, and a message that reported one of those and swallowed the other
            // would be the same mistake this round is here to fix.
            var oneBranch = verdict.Total > 0 && verdict.Climbed == verdict.Total
                ? "§01 · 판정 " + verdict.Total + "번이 전부 '발판 위에 올라섰다'로 끝났다. 이 건물에서 몸이 "
                  + "제 층 바닥 높이에 서 있었던 적이 한 번도 없다는 뜻이다.\n"
                  + "Contained has two branches: a body more than " + ClimbedAboveFloorMetres.ToString("0.00")
                  + " m over its storey's floor is standing on furniture and counts as inside, and anything at "
                  + "or below that is judged by whether the bake still has floor under it. A runner who lands on "
                  + "the storey below stands 0.09 m over its floor and takes the second branch; a runner walking "
                  + "a corridor takes the second branch. Taking the FIRST one every single time means every "
                  + "surface in this building is at least " + ClimbedAboveFloorMetres.ToString("0.00")
                  + " m above where the map says its floor is — the shape of a plate laid over the floors, and "
                  + "exactly what a Map/" + BoundaryRootName + " lid poured 0.25 m proud of every storey does.\n"
                  + "This is not a threshold to widen. While it holds, the NavMesh half of every 'still inside' "
                  + "verdict was never asked, and a green run of this fixture would be green for a reason that "
                  + "has nothing to do with walls."
                : string.Empty;

            if (escapes.Count == 0)
            {
                if (oneBranch.Length > 0)
                {
                    Assert.Fail(oneBranch + summary);
                }

                Debug.Log(greenLog);
                return;
            }

            // Deepest first: the storey, then the furthest out, so the top of the list is the
            // outer edge of the highest floor — the first hole a runner starting on B1's rim
            // would find. Fully ordered down to the bearing, so two runs of a broken map quote
            // the same thirty holes and a fix can be checked against the same list.
            escapes.Sort((p, q) =>
            {
                var byStorey = p.Storey.CompareTo(q.Storey);
                if (byStorey != 0)
                {
                    return byStorey;
                }

                var byRadius = Radius(q.To).CompareTo(Radius(p.To));
                if (byRadius != 0)
                {
                    return byRadius;
                }

                var byX = p.To.x.CompareTo(q.To.x);
                if (byX != 0)
                {
                    return byX;
                }

                var byZ = p.To.z.CompareTo(q.To.z);
                return byZ != 0 ? byZ : p.BearingDegrees.CompareTo(q.BearingDegrees);
            });

            var why = new StringBuilder();
            why.AppendLine(headline);
            why.AppendLine();
            why.AppendLine("§02는 중심까지 가는 경주이고, 그걸 어렵게 만드는 것이 미로 전부다 — 관문 4 → 2 → 1, "
                           + "문, 어둠, 창조물. 벽 밖으로 한 발 나간 주자는 그 전부를 건너뛰고 건물 바닥을 "
                           + "가로질러 중심으로 걸어간다. 나갈 수 있는 맵은 게임이 아니다.");
            why.AppendLine();
            why.AppendLine("나간 자리 " + escapes.Count + "군데 중 앞의 " + Mathf.Min(EscapesQuoted, escapes.Count)
                           + "개 — 층, 칸, 밀어낸 방향 순:");
            why.AppendLine();

            var quoted = Mathf.Min(EscapesQuoted, escapes.Count);
            for (var i = 0; i < quoted; i++)
            {
                var e = escapes[i];

                // The physics queries and the three seconds of falling, taken here rather than
                // when the escape happened: after the sort, so the thirty investigated are the
                // thirty printed, and only thirty of them however many holes there turn out to
                // be.
                Investigate(e);

                why.AppendLine(
                    "  B" + (e.Storey + 1) + "  칸 (" + CellOf(e.To.x) + "," + CellOf(e.To.z) + ")  "
                    + e.To.ToString("0.00"));
                why.AppendLine(
                    "      출발 " + e.From.ToString("0.00") + " 칸 (" + CellOf(e.From.x) + "," + CellOf(e.From.z)
                    + "), 중심에서 " + (Radius(e.From) / CellMetres).ToString("0.0") + "칸 — " + e.Attack
                    + ", " + e.TravelledMetres.ToString("0.0") + " m 이동");
                why.AppendLine(
                    "      NavMesh 없음 (반경 " + FloorSnapMetres.ToString("0.00") + " m). 가장 가까운 바닥은 "
                    + (float.IsInfinity(e.NavGapMetres)
                        ? WideProbeMetres.ToString("0") + " m 안에 아예 없다"
                        : e.NavGapMetres.ToString("0.00") + " m 떨어진 B" + (e.NavStorey + 1))
                    + ". 발밑 " + e.Underfoot
                    + (float.IsInfinity(e.UnderfootDropMetres)
                        ? string.Empty
                        : " (" + e.UnderfootDropMetres.ToString("0.00") + " m 아래)"));
                why.AppendLine(
                    "      " + FallWatchSeconds.ToString("0") + "초 놔두면 "
                    + (e.CameToRest
                        ? e.FellMetres.ToString("0.00") + " m 떨어져 멈춘다 — 밟을 것이 있다는 뜻이다"
                        : e.FellMetres.ToString("0.0") + " m 떨어지고도 계속 떨어진다 — 건물 밖이다"));
                why.AppendLine();
            }

            why.Append(summary);

            if (oneBranch.Length > 0)
            {
                why.AppendLine();
                why.AppendLine(oneBranch);
            }

            why.AppendLine();
            why.AppendLine("확인 방법: 위의 칸 좌표를 Map_FirstSketch_Solo 씬에서 찾아보라. 한 칸은 "
                           + CellMetres.ToString("0.0") + " m 이고 셀 (x,z)의 최소 모서리는 (x·"
                           + CellMetres.ToString("0.0") + ", z·" + CellMetres.ToString("0.0") + ") 이다.");

            Assert.Fail(why.ToString());
        }

        /// <summary>
        /// The sentence that has to sit over every per-storey table in this fixture, because
        /// the last run of it was misread exactly this way: five storeys reported 탈출 0회 and
        /// the report gave a reader nothing to weigh that against, so it read as "sealed" when
        /// it meant "the 160 points we happened to take did not include the leaking cell".
        /// </summary>
        private const string HowHardWeLooked =
            "탈출 0회는 '막혀 있다'가 아니라 '이만큼 찾아봤는데 못 찾았다'는 뜻이다 — 아래 줄의 표본 비율과 "
            + "열린 끝 개수가 그 '이만큼'이다.";

        /// <summary>
        /// Which branch every verdict came out of, as one clause for a summary line. Printed on
        /// green runs as well as red ones: <see cref="Verdict"/> explains why a lopsided split
        /// is a finding, and a finding nobody prints is a finding nobody reads.
        /// </summary>
        private static string Branches(Verdict verdict)
        {
            return "판정 " + verdict.Total + "회 — 발판 위에 올라선 " + verdict.Climbed + "회 · 바닥 위 "
                   + verdict.OnTheFloor + "회 · 밖 " + verdict.Out + "회";
        }

        // ==================================================================
        // Measurements.
        // ==================================================================

        /// <summary>Height of a storey's floor. 0 is B1, and it is the same rule <c>MapKitCatalogue.FloorY</c> uses.</summary>
        private static float FloorY(int storey)
        {
            return -storey * StoreyPitchMetres;
        }

        /// <summary>
        /// Which storey a height belongs to — the same rule <c>MatchDirector.AttachChutes</c>
        /// uses to bind a chute: the floor's own y over the storey pitch. 0 is B1.
        /// </summary>
        private static int StoreyOf(float y)
        {
            return Mathf.RoundToInt(-y / StoreyPitchMetres);
        }

        /// <summary>
        /// Square-ring distance from the tower's axis. <c>RadialStorey</c> lays its bands as
        /// square rings at Chebyshev radii, so "how far out is this" is a max and not a
        /// hypotenuse — a corner cell of the 외곽 고리 is on the rim even though it is 35 m
        /// from the middle as the crow flies.
        /// </summary>
        private float Radius(Vector3 point)
        {
            return Mathf.Max(Mathf.Abs(point.x - _middleX), Mathf.Abs(point.z - _middleZ));
        }

        /// <summary>The map's own cell index for a world coordinate — <c>MapCell.Min</c> is the cell index times the grid.</summary>
        private static int CellOf(float world)
        {
            return Mathf.FloorToInt(world / CellMetres);
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        private static int Total(int[] counts)
        {
            var sum = 0;
            for (var i = 0; i < counts.Length; i++)
            {
                sum += counts[i];
            }

            return sum;
        }

        private static int CountOn(List<Escape> escapes, int storey)
        {
            var n = 0;
            for (var i = 0; i < escapes.Count; i++)
            {
                n += escapes[i].Storey == storey ? 1 : 0;
            }

            return n;
        }
    }
}
#endif
