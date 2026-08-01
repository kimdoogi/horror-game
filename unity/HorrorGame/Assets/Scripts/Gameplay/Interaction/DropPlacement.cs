#nullable enable

using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// Where a released prop comes to rest. §08.
    /// <para>
    /// <b>Why this exists.</b> §08 makes dropping a live decision — the weight bands
    /// buy speed back — and it makes a dead player's 전리품 "떨어진다. 회수하려면 시체가
    /// 있는 곳으로 돌아가야 한다". Both readings need the piece to be <em>on the floor</em>
    /// afterwards. A carried piece hangs off the eye transform, so simply unparenting it
    /// leaves it at eye height in mid-air, which is what the owner saw. Every path that
    /// opens a pair of hands comes through here so there is one answer to "where did it
    /// land", not one per call site.
    /// </para>
    /// <para>
    /// <b>Why a cast and not a Rigidbody.</b> Letting PhysX settle the piece is the more
    /// obvious answer and it was rejected for three reasons, in order of weight.
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>Every interactable's collider is a trigger on purpose.</b>
    /// <see cref="Interactable.CreateProp"/> says why: §12's corridor widths are computed
    /// for the character and the monster, not for scenery. A Rigidbody only falls if its
    /// collider is solid, so a physics drop means a solid 1.28 m 궤짝 standing in a
    /// corridor §12 sized without it — and standing on a NavMesh that was baked without
    /// it, so the monster walks through the thing the player just used to block a door.
    /// Loot that can barricade §12 is a mechanic §08 does not have; §04's 정비 자재 is.
    /// </description></item>
    /// <item><description>
    /// <b>A settled Rigidbody moves the collider out from under the crosshair.</b> The
    /// box <see cref="Interactable"/> fits is grown <em>upward from the model's base</em>
    /// and floored at the aim minimum precisely so a 2.2 cm 반지 is pickable; a piece that
    /// tips and sleeps on its side puts that grown volume sideways through the floor and
    /// the aim tolerance the box exists to provide is gone. Placing the piece upright,
    /// on the surface, reproduces the pose the spawn path already proves pickable — the
    /// dropped piece is pickable by construction rather than by luck.
    /// </description></item>
    /// <item><description>
    /// <b>It is checkable without a frame.</b> Casts answer in EditMode, so the five §12
    /// floor materials, the stairs, the slope and the crate are all covered by a test
    /// that costs a second. A settle needs FixedUpdate ticks and returns a different
    /// answer on a hitching host.
    /// </description></item>
    /// </list>
    /// <para>
    /// The cost of the choice, stated: a piece does not tumble, roll or bounce, and one
    /// released over a stairwell lands directly below rather than skittering down the
    /// steps. That is a smaller lie than a 궤짝 floating at chest height.
    /// </para>
    /// </summary>
    public static class DropPlacement
    {
        /// <summary>
        /// The point a released piece should be put at: in front of the dropper if there
        /// is room, at their feet if there is not, and on whatever surface is under it.
        /// </summary>
        /// <param name="releasedAt">Where the piece is at the instant the hands open — in front of the eye.</param>
        /// <param name="standingAt">The dropper's own feet. The fallback, because it is by definition on a surface.</param>
        /// <param name="dropper">
        /// The dropper's root. Its colliders are skipped: the player's own
        /// <c>CharacterController</c> is solid, sits directly over the fallback point and
        /// would otherwise be reported as both the wall in front and the floor below.
        /// </param>
        public static Vector3 Resolve(Vector3 releasedAt, Vector3 standingAt, Transform? dropper)
        {
            var target = ClearOfGeometry(releasedAt, standingAt, dropper);
            return OnTheSurface(target, releasedAt, standingAt, dropper);
        }

        /// <summary>
        /// The pose a released piece is set down in: the way it was facing, standing up.
        /// <para>
        /// A carried piece is parented to the eye, so its world rotation carries §05's
        /// pitch — put down unchanged, a 궤짝 sits at whatever angle the player happened
        /// to be looking. Only the yaw survives, which is also what keeps the fitted
        /// trigger box axis-aligned with its own model and therefore the size the
        /// crosshair was measured against.
        /// </para>
        /// </summary>
        public static Quaternion Upright(Quaternion held)
        {
            return Quaternion.Euler(0f, held.eulerAngles.y, 0f);
        }

        /// <summary>
        /// How far in front of the dropper the piece may actually go.
        /// <para>
        /// A sphere the width of the crosshair's own aim tolerance is swept from over the
        /// dropper's feet, at the height the hands are holding the piece, out to where
        /// the hands are. Whatever it touches first is where the room runs out — so a
        /// player facing a wall puts the piece down at their feet instead of through it,
        /// and a player facing a 궤짝 puts it down against it. Using the aim minimum as
        /// the radius is not arbitrary: it is the width of the volume the crosshair tests
        /// against, so a gap this sweep fits through is a gap the piece can be found in.
        /// </para>
        /// </summary>
        private static Vector3 ClearOfGeometry(Vector3 releasedAt, Vector3 standingAt, Transform? dropper)
        {
            var reach = releasedAt - standingAt;
            reach.y = 0f;

            var distance = reach.magnitude;
            if (distance <= ClearanceRadiusMetres)
            {
                // Looking at their own feet. There is nothing to sweep and nowhere to put
                // it but here.
                return standingAt;
            }

            // At the hands' height, but never below the feet: a sweep that starts inside
            // §12's floor reports a hit at zero distance and would refuse every drop.
            var height = Mathf.Max(releasedAt.y, standingAt.y + ClearanceRadiusMetres);
            var from = new Vector3(standingAt.x, height, standingAt.z);
            var direction = reach / distance;

            var free = distance;
            var hits = Physics.SphereCastAll(
                from, ClearanceRadiusMetres, direction, distance, ~0, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hits.Length; i++)
            {
                if (IsPartOf(hits[i].collider, dropper))
                {
                    continue;
                }

                // SphereCast reports the distance the sphere's centre travelled, so this
                // point already leaves a full radius between the piece and the wall. A
                // sweep that starts overlapping reports zero, which lands the piece at
                // the dropper's feet — the correct answer for someone stood in a doorway.
                if (hits[i].distance < free)
                {
                    free = hits[i].distance;
                }
            }

            return standingAt + (direction * Mathf.Max(free, 0f));
        }

        /// <summary>
        /// Drops the point onto the first solid surface under it.
        /// <para>
        /// The search starts above both the hands and the feet, so it finds the crate the
        /// player is standing on, the step of a 계단, the pitch of a slope and the table
        /// the piece was held over — whichever is topmost — and it ignores triggers, so
        /// another interactable's box is never mistaken for ground. §12's five floor
        /// materials need no special case: they are all just collision.
        /// </para>
        /// <para>
        /// Nothing below at all means the point is over a hole, and the piece goes to the
        /// dropper's feet rather than into the void.
        /// </para>
        /// </summary>
        private static Vector3 OnTheSurface(
            Vector3 target, Vector3 releasedAt, Vector3 standingAt, Transform? dropper)
        {
            var top = Mathf.Max(releasedAt.y, standingAt.y) + ProbeRiseMetres;
            var from = new Vector3(target.x, top, target.z);

            var hits = Physics.RaycastAll(
                from, Vector3.down, ProbeRiseMetres + ProbeFallMetres, ~0, QueryTriggerInteraction.Ignore);

            var nearest = float.PositiveInfinity;
            var surfaceY = 0f;

            for (var i = 0; i < hits.Length; i++)
            {
                if (hits[i].distance >= nearest || IsPartOf(hits[i].collider, dropper))
                {
                    continue;
                }

                nearest = hits[i].distance;
                surfaceY = hits[i].point.y;
            }

            return float.IsPositiveInfinity(nearest)
                ? standingAt
                : new Vector3(target.x, surfaceY + Interactable.FloorClearanceMetres, target.z);
        }

        /// <summary>
        /// Whether a hit belongs to the dropper. The same test <c>PlayerInteractor.Probe</c>
        /// uses on its own rig, for the same reason: a player is not scenery.
        /// </summary>
        private static bool IsPartOf(Collider collider, Transform? dropper)
        {
            return dropper != null && collider.transform.IsChildOf(dropper);
        }

        /// <summary>
        /// Radius of the sweep that looks for room in front of the dropper, metres. Tied
        /// to <see cref="Interactable.MinimumTargetMetres"/> rather than chosen: the
        /// crosshair tests against a box no narrower than that, so a piece that fits this
        /// sweep fits the aim tolerance that makes it findable again.
        /// </summary>
        private const float ClearanceRadiusMetres = Interactable.MinimumTargetMetres * 0.5f;

        /// <summary>
        /// How far above the hands the floor search starts, metres. Probe geometry: a
        /// cast that begins level with the piece can begin inside the very surface it is
        /// resting against and report nothing.
        /// </summary>
        private const float ProbeRiseMetres = 0.25f;

        /// <summary>
        /// How far below the hands the floor search runs, metres. Deliberately deeper
        /// than the generated building is tall, so the search never runs out <em>inside</em>
        /// the map — running out sends the piece back to the dropper's feet, which on a
        /// balcony is the one outcome that reads as the bug this class fixes.
        /// </summary>
        private const float ProbeFallMetres = 30f;
    }
}
