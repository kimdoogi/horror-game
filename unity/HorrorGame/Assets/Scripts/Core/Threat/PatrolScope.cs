namespace HorrorGame.Core.Threat
{
    /// <summary>
    /// How much of the map the monster patrols in a given threat tier — the 순찰
    /// column of §07's table.
    /// <para>
    /// §07 writes that column in two different units: the first two rows are
    /// absolute ("1개 구역", "2개 구역") and the last three are proportions of the
    /// map ("절반", "전체"). That difference matters because §12 lets a map have
    /// 4–6 zones, so "절반" is 2 zones on one map and 3 on another while "2개
    /// 구역" is 2 everywhere. Collapsing both into a single integer would silently
    /// pick one map size and be wrong on the others, so the unit is kept
    /// alongside the number and resolved against the real map by
    /// <see cref="ThreatTier.PatrolZoneCountFor"/>.
    /// </para>
    /// </summary>
    public enum PatrolScope
    {
        /// <summary>
        /// An absolute number of zones, given by <see cref="ThreatTier.PatrolZoneCount"/>
        /// and independent of map size. §07's 초저녁 and 밤 rows.
        /// </summary>
        FixedZones = 0,

        /// <summary>절반 — half the map's zones, rounded up. §07's 심야 row.</summary>
        HalfTheMap = 1,

        /// <summary>전체 — every zone. §07's 새벽 and 동트기 전 rows.</summary>
        WholeMap = 2,
    }
}
