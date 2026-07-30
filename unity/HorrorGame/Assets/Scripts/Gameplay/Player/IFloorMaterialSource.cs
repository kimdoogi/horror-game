#nullable enable

using HorrorGame.Core.Map;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Declares what a piece of level geometry sounds like underfoot.
    /// <para>
    /// §12 is unusually blunt that this is not decoration: "구역별로 바닥 재질이 달라야
    /// 청음사가 위치를 판별할 수 있다. 아트 결정이 아니라 <b>시스템 결정이다.</b> 재질
    /// 경계를 명확히 할 것." A floor whose material is guessed from a texture name is a
    /// floor that will eventually lie, and a lying floor does not merely sound wrong — it
    /// tells §04's Listener that the monster is in zone C when it is in zone D.
    /// </para>
    /// <para>
    /// An interface rather than a concrete component so the map layer can answer from
    /// whatever it already knows — a zone id, a generated tile, its own
    /// <c>IWorldProbe</c> — without this file learning what a zone is (ARCHITECTURE §3).
    /// </para>
    /// </summary>
    public interface IFloorMaterialSource
    {
        /// <summary>The §12 surface here. <see cref="FloorMaterial.None"/> means "not authored yet", not "silent".</summary>
        FloorMaterial FloorMaterial { get; }
    }

    /// <summary>
    /// The simplest possible <see cref="IFloorMaterialSource"/>: a material stated on a
    /// collider. Enough for the feel harness and for hand-built test geometry; the
    /// generated map is expected to answer through its own probe instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloorSurfaceTag : MonoBehaviour, IFloorMaterialSource
    {
        [Tooltip("§12's zone surface. Leaving this None is a validation failure, not a default.")]
        [SerializeField]
        private FloorMaterial _material = FloorMaterial.None;

        /// <inheritdoc />
        public FloorMaterial FloorMaterial
        {
            get { return _material; }
            set { _material = value; }
        }
    }
}
