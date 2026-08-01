#nullable enable

using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Gameplay.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace HorrorGame.Gameplay.InteractionEditor
{
    /// <summary>
    /// Where §08's dropped 전리품 actually ends up, on every surface §12 builds out of.
    /// <para>
    /// <b>Why this is a geometry test and not a match test.</b> The defect was a piece
    /// hanging in the air at the height it was released, and the thing that decides that
    /// is one cast against whatever collision is under the player. A synthetic floor, a
    /// wall, a 계단 step, a ramp and a crate reproduce every case §12 can present in
    /// under a second and with no scene to load, and each one can be checked against a
    /// surface height the test itself chose — which the real map cannot offer, because
    /// nothing there knows how high its own floor is meant to be.
    /// </para>
    /// <para>
    /// It answers "does the piece land on the right surface". It does not answer "can a
    /// player then pick it up" — that needs the crosshair ray and a real key, and it is
    /// in <c>InteractionDropTests</c> in PlayMode. Both are needed: this file would stay
    /// green if the piece landed correctly and the collider went with it, which is the
    /// exact shape of the bug that made the pick-up key look broken for a day.
    /// </para>
    /// <para>
    /// It lives in <c>Assembly-CSharp-Editor</c> because <see cref="DropPlacement"/>
    /// lives in the predefined <c>Assembly-CSharp</c>, and an <c>.asmdef</c> test
    /// assembly cannot reference a predefined one — the same reason
    /// <c>SoloMatchLoopTests</c> is where it is.
    /// </para>
    /// </summary>
    public sealed class DropPlacementTests
    {
        private readonly List<GameObject> _built = new List<GameObject>();

        /// <summary>Removes the synthetic world. EditMode leaves whatever a test made in the open scene.</summary>
        [TearDown]
        public void ClearTheWorld()
        {
            for (var i = 0; i < _built.Count; i++)
            {
                if (_built[i] != null)
                {
                    Object.DestroyImmediate(_built[i]);
                }
            }

            _built.Clear();
            Physics.SyncTransforms();
        }

        /// <summary>
        /// §12's five 바닥 재질, one per zone, each at its own height. They are different
        /// sounds and the same collision, and this states that: a released piece comes to
        /// rest on whichever one is under it, clear of it by
        /// <see cref="Interactable.FloorClearanceMetres"/> and no more.
        /// </summary>
        [TestCase("A 나무", 0f)]
        [TestCase("B 타일", -3.75f)]
        [TestCase("C 자갈", -7.5f)]
        [TestCase("D 콘크리트", -11.25f)]
        [TestCase("계단 금속", -1.9f)]
        public void A_piece_released_at_chest_height_lands_on_the_floor_under_it(string zone, float floorY)
        {
            var origin = Origin();
            Floor("Floor_" + zone, origin + new Vector3(0f, floorY, 0f));

            var feet = origin + new Vector3(0f, floorY, 0f);
            var landed = DropPlacement.Resolve(Hands(feet), feet, null);

            Assert.That(landed.y, Is.EqualTo(floorY + Interactable.FloorClearanceMetres).Within(Tolerance),
                zone + ": the piece did not come to rest on its floor. " + Report(landed, feet));
            Assert.That(Flat(landed - feet).magnitude, Is.GreaterThan(0.3f),
                zone + ": the piece was put down inside the player rather than in front of them. "
                + Report(landed, feet));
        }

        /// <summary>
        /// §08's whole point: it is <em>not</em> where the hands let go. Chest height is
        /// 1.63 m up, and a piece that stays there is the defect the owner reported.
        /// </summary>
        [Test]
        public void The_landing_height_is_the_floors_and_not_the_hands()
        {
            var origin = Origin();
            Floor("Floor", origin);

            var landed = DropPlacement.Resolve(Hands(origin), origin, null);

            Assert.That(landed.y, Is.LessThan(origin.y + 0.1f),
                "the piece stayed at the height it was released from — " + Report(landed, origin));
        }

        /// <summary>
        /// A wall in front. §12 puts 전리품 in dead ends and corners, so this is the
        /// common case and not the exotic one: the piece must end up on this side of the
        /// wall, still reachable, rather than buried in the geometry the crosshair cannot
        /// see through.
        /// </summary>
        [Test]
        public void A_piece_released_against_a_wall_stays_on_the_players_side_of_it()
        {
            var origin = Origin();
            Floor("Floor", origin);

            // Face of the wall 0.5 m in front — closer than the 1.05 m a 궤짝 is carried at.
            const float wallFace = 0.5f;
            Block("Wall", origin + new Vector3(0f, 1.5f, wallFace + 0.5f), new Vector3(6f, 3f, 1f));

            var landed = DropPlacement.Resolve(Hands(origin), origin, null);

            Assert.That(landed.z - origin.z, Is.LessThan(wallFace),
                "the piece went through the wall — " + Report(landed, origin));
            Assert.That(landed.y, Is.EqualTo(origin.y + Interactable.FloorClearanceMetres).Within(Tolerance),
                "the piece did not reach the floor beside the wall — " + Report(landed, origin));
        }

        /// <summary>
        /// Standing on a crate, which §12's rooms are full of. Released over the crate it
        /// stays on the crate; released past its edge it goes all the way down, because
        /// that is what "떨어진다" means and the alternative is a piece hanging over thin
        /// air one metre up.
        /// </summary>
        [Test]
        public void A_piece_dropped_from_a_crate_lands_on_whatever_is_actually_under_it()
        {
            var origin = Origin();
            Floor("Floor", origin);

            const float crateTop = 1f;
            Block("Crate", origin + new Vector3(0f, crateTop * 0.5f, 0f), new Vector3(4f, crateTop, 4f));

            var feet = origin + new Vector3(0f, crateTop, 0f);

            var onTop = DropPlacement.Resolve(Hands(feet), feet, null);
            Assert.That(onTop.y, Is.EqualTo(crateTop + Interactable.FloorClearanceMetres).Within(Tolerance),
                "a piece put down on the crate did not land on the crate — " + Report(onTop, feet));

            // Over the edge: the hands are 3 m out, past the crate's 2 m half-width.
            var reaching = feet + new Vector3(0f, EyeHeightMetres, 3f);
            var offEdge = DropPlacement.Resolve(reaching, feet, null);
            Assert.That(offEdge.y, Is.EqualTo(origin.y + Interactable.FloorClearanceMetres).Within(Tolerance),
                "a piece put down past the crate's edge hung in the air instead of falling to the floor — "
                + Report(offEdge, feet));
        }

        /// <summary>
        /// A ramp and a 계단 step. Both are answered by the surface itself rather than by
        /// the storey's nominal floor plane, which is the only way a piece left on a
        /// staircase sits on a step.
        /// </summary>
        [Test]
        public void A_slope_and_a_step_are_answered_by_the_surface_and_not_by_the_storey()
        {
            var origin = Origin();
            Floor("Floor", origin);

            // 20° ramp running along +Z, so the height under the piece depends on how far
            // in front of the player it is put down.
            var ramp = Block("Ramp", origin + new Vector3(0f, 0.5f, 2f), new Vector3(6f, 1f, 8f));
            ramp.transform.rotation = Quaternion.Euler(-20f, 0f, 0f);
            Physics.SyncTransforms();

            var landed = DropPlacement.Resolve(Hands(origin), origin, null);
            var under = SurfaceUnder(landed);

            Assert.That(under, Is.Not.Null, "nothing was under the landing point at all — " + Report(landed, origin));
            Assert.That(landed.y, Is.EqualTo(under!.Value + Interactable.FloorClearanceMetres).Within(Tolerance),
                "the piece did not sit on the ramp it landed on — " + Report(landed, origin));
            Assert.That(landed.y, Is.GreaterThan(origin.y + Interactable.FloorClearanceMetres + Tolerance),
                "the piece sank through the ramp to the storey's floor — " + Report(landed, origin));
        }

        /// <summary>
        /// The bug this file was written for, in one case. Every interactable's collider
        /// is a trigger (<see cref="Interactable"/> says why), so a floor search that
        /// took the project's default trigger handling landed a released piece on the
        /// <em>box of the prop beside it</em> and left it floating there.
        /// </summary>
        [Test]
        public void Another_interactables_trigger_box_is_not_a_floor()
        {
            var origin = Origin();
            Floor("Floor", origin);

            var ghost = Block("NeighbouringProp", origin + new Vector3(0f, 0.5f, 0.75f), new Vector3(2f, 1f, 2f));
            ghost.GetComponent<BoxCollider>().isTrigger = true;
            Physics.SyncTransforms();

            var landed = DropPlacement.Resolve(Hands(origin), origin, null);

            Assert.That(landed.y, Is.EqualTo(origin.y + Interactable.FloorClearanceMetres).Within(Tolerance),
                "the piece came to rest on a trigger — " + Report(landed, origin));
        }

        /// <summary>
        /// The dropper's own <c>CharacterController</c> is solid, stands over the fallback
        /// point and blocks the sweep that looks for room. Counting it would put every
        /// dropped piece at 1.8 m — on top of the player's own head.
        /// </summary>
        [Test]
        public void The_dropper_is_neither_a_wall_nor_a_floor()
        {
            var origin = Origin();
            Floor("Floor", origin);

            var body = new GameObject("Dropper");
            _built.Add(body);
            body.transform.position = origin;

            var capsule = body.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            Physics.SyncTransforms();

            var landed = DropPlacement.Resolve(Hands(origin), origin, body.transform);

            Assert.That(landed.y, Is.EqualTo(origin.y + Interactable.FloorClearanceMetres).Within(Tolerance),
                "the piece landed on the player who dropped it — " + Report(landed, origin));
            Assert.That(Flat(landed - origin).magnitude, Is.GreaterThan(0.3f),
                "the player's own capsule was read as a wall and the piece fell at their feet — "
                + Report(landed, origin));
        }

        /// <summary>
        /// Over a hole with nothing below. The piece goes to the dropper's feet, which is
        /// the one point in the world known to be standing on something — better than
        /// dropping §08's 궤짝 out of the map for good.
        /// </summary>
        [Test]
        public void With_nothing_below_the_piece_goes_to_the_droppers_feet()
        {
            var origin = Origin();

            var landed = DropPlacement.Resolve(Hands(origin), origin, null);

            Assert.That((landed - origin).magnitude, Is.LessThan(Tolerance),
                "nothing was under the drop point, and the piece did not fall back to the feet — "
                + Report(landed, origin));
        }

        /// <summary>
        /// A carried piece hangs off the eye, so its world rotation carries §05's pitch.
        /// Put down unchanged it lies over at whatever angle the player was looking at,
        /// and the fitted trigger box goes over with it.
        /// </summary>
        [Test]
        public void A_piece_is_set_down_standing_up_and_still_facing_the_way_it_was()
        {
            var held = Quaternion.Euler(-38f, 117f, 4f);

            var upright = DropPlacement.Upright(held);
            var angles = upright.eulerAngles;

            Assert.That(Mathf.DeltaAngle(0f, angles.x), Is.EqualTo(0f).Within(0.01f), "the piece kept its pitch");
            Assert.That(Mathf.DeltaAngle(0f, angles.z), Is.EqualTo(0f).Within(0.01f), "the piece kept its roll");
            Assert.That(Mathf.DeltaAngle(117f, angles.y), Is.EqualTo(0f).Within(0.01f), "the piece lost its facing");
        }

        // ------------------------------------------------------------------
        // The synthetic world.
        // ------------------------------------------------------------------

        /// <summary>
        /// A patch of nowhere to build a case in. Kilometres from the world origin,
        /// because an EditMode test runs against whatever scene happens to be open and
        /// the five-storey map is one of them; one case at a time is standing, since
        /// <see cref="ClearTheWorld"/> runs between every one.
        /// </summary>
        private static Vector3 Origin()
        {
            return new Vector3(8000f, 0f, 8000f);
        }

        /// <summary>Where the hands hold a piece: eye height, one carry offset in front.</summary>
        private static Vector3 Hands(Vector3 feet)
        {
            return feet + new Vector3(0f, EyeHeightMetres, CarryOffsetMetres);
        }

        private GameObject Floor(string name, Vector3 topAt)
        {
            return Block(name, topAt - new Vector3(0f, 0.5f, 0f), new Vector3(40f, 1f, 40f));
        }

        private GameObject Block(string name, Vector3 centre, Vector3 size)
        {
            var block = new GameObject(name);
            _built.Add(block);
            block.transform.position = centre;

            var box = block.AddComponent<BoxCollider>();
            box.size = size;

            Physics.SyncTransforms();
            return block;
        }

        /// <summary>The height of the solid surface under a point, as the world reports it.</summary>
        private static float? SurfaceUnder(Vector3 point)
        {
            return Physics.Raycast(point + (Vector3.up * 0.5f), Vector3.down, out var hit, 4f, ~0,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : (float?)null;
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        private static string Report(Vector3 landed, Vector3 feet)
        {
            return "landed " + landed.ToString("F3", CultureInfo.InvariantCulture)
                   + ", feet " + feet.ToString("F3", CultureInfo.InvariantCulture)
                   + ", " + (landed.y - feet.y).ToString("F3", CultureInfo.InvariantCulture) + " m above them.";
        }

        /// <summary>The harness rig's eye, metres. Quoted so the release point is the real one.</summary>
        private const float EyeHeightMetres = 1.63f;

        /// <summary>How far in front of the eye §08's 궤짝 is carried. <c>OversizeLootInteractable</c>'s figure.</summary>
        private const float CarryOffsetMetres = 1.05f;

        /// <summary>Slack on a height comparison, metres. Below the clearance it is checking.</summary>
        private const float Tolerance = 0.005f;
    }
}
