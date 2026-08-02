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
        /// Metres above the landing a runner appears. Enough to be a fall and not enough to
        /// hurt: §05 has no fall damage, and a longer drop would just be dead time in a race.
        /// </summary>
        public const float DropHeightMetres = 3.0f;

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
