#nullable enable

using HorrorGame.Core.Map;

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
    /// <para>
    /// <b>The component that implements it moved out of this file, and that was a bug
    /// fix rather than tidying.</b> <c>FloorSurfaceTag</c> is a <c>MonoBehaviour</c>, and
    /// Unity keys a <c>MonoScript</c> on the FILE — a behaviour whose class name does not
    /// match its file name cannot be given a real
    /// <c>m_Script: {fileID: 11500000, guid: …}</c> reference when a scene is saved. The
    /// generator wrote 32 tags into Map_FirstSketch.unity, all 32 serialised as
    /// <c>m_Script: {fileID: 1894526129}</c> — a dangling in-file id — and 28 of them came
    /// back from disk as missing scripts. §12's room tone was null in the shipped solo
    /// scene for exactly that reason, and no test could see it because the count in the
    /// generation log was taken before the save.
    /// </para>
    /// </summary>
    public interface IFloorMaterialSource
    {
        /// <summary>The §12 surface here. <see cref="FloorMaterial.None"/> means "not authored yet", not "silent".</summary>
        FloorMaterial FloorMaterial { get; }
    }
}
