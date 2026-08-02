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

        // §12's own table names five surfaces and five zones is what that allowed, which
        // capped the building at five storeys — the reason every floor read the same. The
        // three below extend the alphabet rather than the rule: each is a distinct thing
        // to stand on, each gets its own §04 clarity, and none of them is a re-skin.

        /// <summary>침수 — standing water. The loudest surface in the game: you cannot cross it unheard.</summary>
        Water = 6,

        /// <summary>파헤쳐진 흙 — a dug floor. Soft, and the second quietest.</summary>
        Earth = 7,

        /// <summary>카펫 — the Listener's blind spot, and the only reason to route the long way.</summary>
        Carpet = 8,
    }
}
