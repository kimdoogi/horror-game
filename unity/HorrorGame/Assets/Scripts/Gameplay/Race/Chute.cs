#nullable enable

using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Gameplay.Race
{
    /// <summary>
    /// §01's 투하구 — the one-way drop from the middle of a storey to the rim of the one
    /// below.
    /// <para>
    /// <b>This is the game.</b> Everything else on a floor is there to make reaching this
    /// hard: the gates narrowing 4 → 2 → 1, the doors, the dark, the creature. Stepping in
    /// is the only thing a runner is trying to do, eight times, and the only thing that
    /// cannot be undone.
    /// </para>
    /// <para>
    /// <b>A fall, not a teleport.</b> The runner is dropped at the landing's height plus
    /// <see cref="DropHeightMetres"/> and left to the controller's own gravity, so the last
    /// half second of every storey is falling in the dark towards a floor you have not seen
    /// yet. A teleport would be one frame of black and would cost the descent the only
    /// moment in it that is not navigation.
    /// </para>
    /// <para>
    /// <b>Landing on the RIM is the whole structure.</b> §01: if a chute dropped you at the
    /// middle below, one runner reaching a centre once would fall eight storeys and the game
    /// would contain a single maze. The landing is set by the map — see
    /// <c>DescentMap.HangChutes</c> — and this component only carries it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Chute : MonoBehaviour
    {
        /// <summary>
        /// How long the fall lasts, seconds — §01's own sentence: 「the last half second of
        /// every storey is falling in the dark towards a floor you have not seen yet」. It is
        /// the only free number in the drop; everything else about it is derived.
        /// </summary>
        public const float FallSeconds = 0.5f;

        /// <summary>
        /// Metres above the landing a runner appears — DERIVED, and it was a typed-in 3.0 m.
        /// <para>
        /// The kit's corridor is 3.00 m clear (<c>MapKitCatalogue.CorridorClearHeight</c>)
        /// and a runner is 1.75 m tall, so feet at 착지 + 3.0 put the whole body inside the
        /// ceiling slab with the head above the soffit. <see cref="CharacterController"/>
        /// pushes a body out of a collider the SHORT way, and at a storey seam the short way
        /// is UP — back onto the floor the runner just left. The measured symptom was a
        /// playthrough log reading 「착지 위 천장까지 0.13 m (투하 높이 3.0 m)」: 13 cm of room
        /// for a 175 cm body.
        /// </para>
        /// <para>
        /// Two facts bound the number. <b>Headroom:</b> the body must fit under the soffit,
        /// so the feet can be at most 3.00 − 1.75 = 1.250 m up. <b>The fall:</b> ½gt² with
        /// <see cref="GameConstants.JumpGravity"/> 9.81 m/s² and <see cref="FallSeconds"/> is
        /// 1.226 m. <b>The fall binds</b> — 1.226 &lt; 1.250 — so the fiction sets the number
        /// and the ceiling merely permits it, with 24 mm to spare over a standing body. Had
        /// the ceiling been the binding one the honest fix would have been the ceiling: a
        /// fall shortened to fit a room is a room deciding the fiction.
        /// </para>
        /// <para>
        /// Still a fall and not a step: 1.23 m at 9.81 m/s² arrives at 4.9 m/s, above §05's
        /// 달리기, and §05 has no fall damage, so it costs only the half second it is for.
        /// <c>MapSceneBuilder.VerifyChutesDropIntoOpenAir</c> re-derives this in the editor
        /// assembly (which cannot reference this one) and measures it against the kit on
        /// every generation — the two MUST agree, and the run that caught this had the
        /// generator reporting 1.226 while the runtime still dropped 3.0.
        /// <c>OutOfBounds.ChuteGraceSeconds</c> follows this constant and becomes
        /// 2 × 0.5 = 1.000 s.
        /// </para>
        /// </summary>
        public const float DropHeightMetres =
            0.5f * GameConstants.JumpGravity * FallSeconds * FallSeconds;

        /// <summary>Radius the mouth swallows a runner within, metres. A cell is 2.5 m.</summary>
        public const float MouthRadiusMetres = 1.4f;

        private Vector3 _landing;
        private int _storeyBelow;
        private bool _bound;

        /// <summary>Which storey this drops onto. 0 is B1.</summary>
        public int StoreyBelow
        {
            get { return _storeyBelow; }
        }

        /// <summary>Where it puts a runner down, before the drop height is added.</summary>
        public Vector3 Landing
        {
            get { return _landing; }
        }

        /// <summary>
        /// Hands the chute the landing the map chose for it.
        /// <para>
        /// Public and called at match start rather than serialised, for the same reason
        /// <c>DoorInteractable.Bind</c> is: the scene generator lives in an editor assembly
        /// that cannot reference this one, so it lays the markers down and the director wires
        /// them up. It also means a chute works in any scene the generator writes with
        /// nothing to remember.
        /// </para>
        /// </summary>
        /// <param name="landing">World position on the rim below.</param>
        /// <param name="storeyBelow">Storey index of the landing.</param>
        public void Bind(Vector3 landing, int storeyBelow)
        {
            _landing = landing;
            _storeyBelow = storeyBelow;
            _bound = true;
        }

        /// <summary>
        /// True when <paramref name="position"/> is inside the mouth.
        /// </summary>
        /// <param name="position">Where the runner is.</param>
        public bool Swallows(Vector3 position)
        {
            if (!_bound)
            {
                return false;
            }

            var flat = position - transform.position;
            flat.y = 0f;

            // Height matters as well as distance: a runner on the floor ABOVE is standing
            // over this chute's own mouth in plan and must not be taken by it.
            return flat.sqrMagnitude <= MouthRadiusMetres * MouthRadiusMetres
                   && Mathf.Abs(position.y - transform.position.y) < 2.6f;
        }

        /// <summary>Where a runner appears after stepping in.</summary>
        public Vector3 DropPoint()
        {
            return _landing + (Vector3.up * DropHeightMetres);
        }
    }
}
