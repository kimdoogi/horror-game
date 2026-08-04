#nullable enable

using HorrorGame.Core.Map;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// States, on a piece of level geometry, which of §12's surfaces it is.
    /// <para>
    /// <b>Its own file, and that is load-bearing.</b> This class used to live at the
    /// bottom of <c>IFloorMaterialSource.cs</c>. Unity keys a <c>MonoScript</c> on the
    /// file, so a <c>MonoBehaviour</c> whose class name does not match its file name gets
    /// no resolvable script reference when a scene is saved: the 32 tags
    /// <c>MapSceneBuilder.TagFloorSurface</c> wrote into Map_FirstSketch.unity all came
    /// out as <c>m_Script: {fileID: 1894526129}</c>, and 28 of the 32 loaded back as
    /// missing scripts. The generator's own count was taken in memory before the save and
    /// therefore reported 32 either way. Measured on the shipped solo scene:
    /// <c>FloorSurfaces.Sample</c> at three PlayerSpawn markers returned <c>None</c> with
    /// the concrete tile right under the ray, and <c>ZoneAmbienceDirector.CurrentBed</c>
    /// was null. Do not fold it back in beside the interface.
    /// </para>
    /// <para>
    /// <b>Two interfaces, one field, on purpose.</b> F-002 names a mix that disagrees with
    /// the rules as the thing that ends §04, and two components would be two places to
    /// forget: §06's <c>NavMeshWorldProbe</c> and §12's <c>FloorSurfaces</c> both walk the
    /// parent chain from a collider, and they must arrive at the same answer.
    /// </para>
    /// <para>
    /// It is read through <c>GetComponentInParent</c>, so one tag on a group answers for
    /// everything under it — which is also why <c>Physics.RaycastNonAlloc</c>'s unsorted
    /// buffer cannot change the answer where the dressing pass has left solid props
    /// standing on the same floor.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloorSurfaceTag : MonoBehaviour, IFloorMaterialSource, HorrorGame.Audio.IFloorSurface
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

        /// <summary>The same fact under the audio layer's name. See the remarks on this class.</summary>
        public FloorMaterial Floor
        {
            get { return _material; }
        }
    }
}
