#nullable enable

using System;
using HorrorGame.Core.Map;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// Anything that can say which of §12's surfaces it is. Implement it on a collider
    /// the player can stand on.
    /// </summary>
    public interface IFloorSurface
    {
        /// <summary>The surface underfoot. §12.</summary>
        FloorMaterial Floor { get; }
    }

    /// <summary>
    /// Marks a collider as one of §12's five floors.
    /// <para>
    /// A placeholder in the honest sense: whoever owns the map is expected to answer
    /// this question from <c>MapGraph</c> instead, by assigning
    /// <see cref="FloorSurfaces.Probe"/>. This component exists so the audio layer can
    /// be stood up and heard on hand-built geometry before that lands, and so a
    /// stairwell landing that belongs to no zone can still be tagged 금속.
    /// </para>
    /// <para>
    /// <b>DO NOT put this one in a saved scene, and prefer
    /// <c>HorrorGame.Gameplay.Player.FloorSurfaceTag</c> — which implements
    /// <see cref="IFloorSurface"/> too and answers both layers off one field.</b> This
    /// class is a <c>MonoBehaviour</c> declared in a file named for something else, and
    /// Unity keys a <c>MonoScript</c> on the FILE: saved into a scene it serialises as a
    /// dangling <c>m_Script: {fileID: …}</c> with no guid and comes back as a missing
    /// script. That is not a theory — the generator's 32 tags were lost that way and §12's
    /// room tone was null in the shipped solo scene until the other one moved into
    /// <c>FloorSurfaceTag.cs</c>. It stays here only because hand-built test geometry adds
    /// it at runtime, where the rule does not bite. The <c>[AddComponentMenu]</c> below is
    /// the trap: adding it from the inspector and saving loses it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Floor Surface Tag")]
    public sealed class FloorSurfaceTag : MonoBehaviour, IFloorSurface
    {
        [Tooltip("§12: 나무 · 타일 · 자갈 · 콘크리트 for zones A–D, 금속 for stairs.")]
        [SerializeField]
        private FloorMaterial floor = FloorMaterial.None;

        /// <inheritdoc />
        public FloorMaterial Floor => floor;
    }

    /// <summary>
    /// Answers "what am I standing on" for the mix.
    /// <para>
    /// <b>The answer must be the same one the rules get.</b> §12's material map is what
    /// <c>ListenerAbility</c> computes its error radius from, so if the HUD says 자갈
    /// and the speakers say 콘크리트 the role is worse than useless — it teaches the
    /// player to distrust it, which F-002 already flags as the failure mode that ends
    /// the 청음사. Hence <see cref="Probe"/>: the layer that owns <c>MapGraph</c>
    /// assigns it once, and from then on the ears and the rules read one source.
    /// </para>
    /// <para>
    /// Until it is assigned, a downward raycast onto an <see cref="IFloorSurface"/>
    /// stands in. That is a fallback, not a design — it can disagree with the map, and
    /// it costs a raycast per footstep.
    /// </para>
    /// </summary>
    public static class FloorSurfaces
    {
        /// <summary>
        /// The authoritative sampler, normally <c>IWorldProbe.SampleFloor</c> bridged
        /// to world space. Null until the map layer supplies one.
        /// </summary>
        public static Func<Vector3, FloorMaterial>? Probe { get; set; }

        /// <summary>How far down to look for ground, metres. A step's worth, plus a stair riser.</summary>
        private const float GroundProbeDistance = 2.5f;

        /// <summary>How far above the sample point the ray starts, metres.</summary>
        private const float GroundProbeLift = 0.5f;

        // Eight, matching PlayerFootsteps, which says why: RaycastNonAlloc does NOT sort,
        // so a buffer smaller than the number of colliders on the ray drops an arbitrary
        // subset — and the dressing pass leaves solid props standing ON the floor
        // (ScatterSession.Finish keeps their colliders). At four, a floor tile under three
        // crates could be the hit that never arrives, and the symptom is a room that goes
        // silent underfoot only where it has been dressed.
        private static readonly RaycastHit[] Scratch = new RaycastHit[8];

        /// <summary>
        /// The surface at a world position, or <see cref="FloorMaterial.None"/> when
        /// nothing answers. Callers treat <c>None</c> as "play nothing" — see
        /// <see cref="SurfaceAudioLibrary.For"/>.
        /// </summary>
        public static FloorMaterial Sample(Vector3 position)
        {
            var probe = Probe;
            if (probe != null)
            {
                return probe(position);
            }

            var count = Physics.RaycastNonAlloc(
                position + (Vector3.up * GroundProbeLift),
                Vector3.down,
                Scratch,
                GroundProbeDistance,
                GameAudio.OccluderMask,
                QueryTriggerInteraction.Ignore);

            var nearest = float.PositiveInfinity;
            var result = FloorMaterial.None;

            for (var i = 0; i < count; i++)
            {
                var hit = Scratch[i];
                if (hit.collider == null || hit.distance >= nearest)
                {
                    continue;
                }

                var surface = hit.collider.GetComponentInParent<IFloorSurface>();
                if (surface == null)
                {
                    continue;
                }

                nearest = hit.distance;
                result = surface.Floor;
            }

            return result;
        }
    }
}
