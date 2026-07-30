namespace HorrorGame.Core.Map
{
    /// <summary>
    /// Floor surfaces from §12. Each zone gets a distinct one so the Listener can
    /// tell <em>where</em> the monster is, not just how far.
    /// <para>
    /// §12 is explicit that this is a systems decision rather than an art one:
    /// "구역별로 바닥 재질이 달라야 청음사가 위치를 판별할 수 있다." Changing a zone's
    /// material changes what the Listener role can do, so it is validated by
    /// <see cref="MapValidator"/> rather than left to level dressing.
    /// </para>
    /// </summary>
    public enum FloorMaterial
    {
        /// <summary>Unassigned — a map with this anywhere fails validation.</summary>
        None = 0,

        /// <summary>Zone A — 나무. A creak. §12.</summary>
        Wood = 1,

        /// <summary>Zone B — 타일. Hard, with reverb. §12.</summary>
        Tile = 2,

        /// <summary>Zone C — 자갈. A rustle. §12.</summary>
        Gravel = 3,

        /// <summary>Zone D — 콘크리트. Dull. §12.</summary>
        Concrete = 4,

        /// <summary>Stairwells — 금속. Rings. §12.</summary>
        Metal = 5,
    }
}
