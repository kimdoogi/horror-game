namespace HorrorGame.Core.Threat
{
    /// <summary>
    /// One row of §07's 시간대별 위협 단계 table — everything the passage of time
    /// does to the monster, resolved for a moment in the match.
    /// <para>
    /// Consumers take this by value and read the field they care about. That is
    /// the seam ARCHITECTURE.md asks for: the monster brain never learns what a
    /// clock is, the light system never learns what a monster is, and both stay
    /// testable by handing them a tier.
    /// </para>
    /// <para>
    /// §07's 추가 column is treated as <b>cumulative</b>: a modifier introduced by
    /// a row stays on for every later row. Read strictly row-by-row the flashlight
    /// penalty would end at 24 min and the monster would forget the exit at
    /// 32 min, which contradicts both §07's own claim that pressure only
    /// increases and the 32 min row's text ("생존 불가 수준" — a summary of
    /// everything so far plus more, not a reset). The consequence is pinned by
    /// <c>ThreatTests</c> and recorded as a finding, because the table itself does
    /// not say the word "cumulative".
    /// </para>
    /// </summary>
    public readonly struct ThreatTier
    {
        /// <summary>
        /// Zones patrolled when <see cref="Scope"/> is
        /// <see cref="PatrolScope.FixedZones"/>. Meaningless for the proportional
        /// scopes, which derive their count from the map.
        /// </summary>
        private readonly int _fixedZoneCount;

        /// <summary>
        /// Builds a row of the table. Internal on purpose: the table in
        /// <see cref="ThreatCurve"/> is the only legitimate source of a tier, so a
        /// consumer cannot invent a sixth night or a 6.0 m/s monster and then
        /// reason about it as though §07 said so.
        /// </summary>
        internal ThreatTier(
            int index,
            NightPhase phase,
            float monsterSpeed,
            PatrolScope scope,
            int fixedZoneCount,
            float standstillChance,
            float flashlightRangeMultiplier,
            bool monsterKnowsExit)
        {
            Index = index;
            Phase = phase;
            MonsterSpeed = monsterSpeed;
            Scope = scope;
            _fixedZoneCount = fixedZoneCount;
            StandstillChance = standstillChance;
            FlashlightRangeMultiplier = flashlightRangeMultiplier;
            MonsterKnowsExit = monsterKnowsExit;
        }

        /// <summary>Row number in §07's table, 0-based. Ordering is the escalation order.</summary>
        public int Index { get; }

        /// <summary>The 시각 this row describes. §07 — the only threat state a player is ever shown.</summary>
        public NightPhase Phase { get; }

        /// <summary>
        /// Monster movement speed, m/s. §07.
        /// <para>
        /// <b>Absolute, not a multiplier.</b> This replaces
        /// <see cref="GameConstants.MonsterBaseSpeed"/> outright; multiplying the
        /// two would give a 21 m/s monster. §06's headline 4.8 is the 심야 row, so
        /// the monster is slower than the design's ladder for the first 16 minutes
        /// and faster than it for the last 8 — every margin §06 and §12 compute
        /// from 4.8 is a statement about 심야 alone. §05's directional multipliers
        /// still apply to the *players*, not to this.
        /// </para>
        /// </summary>
        public float MonsterSpeed { get; }

        /// <summary>
        /// Zones the monster patrols, resolved against the largest map §12 allows
        /// (<see cref="GameConstants.ZoneCountMax"/>).
        /// <para>
        /// This is the fixed cross-system signature, and it has to answer without
        /// being told the map — so it answers for the biggest legal map, making it
        /// an upper bound. Anything holding an actual map must call
        /// <see cref="PatrolZoneCountFor"/> instead; a smaller map would otherwise
        /// be asked for more zones than it has.
        /// </para>
        /// </summary>
        public int PatrolZoneCount => PatrolZoneCountFor(GameConstants.ZoneCountMax);

        /// <summary>
        /// Whether <see cref="PatrolZoneCount"/> is an absolute count or a share of
        /// the map. §07 mixes the two units; see <see cref="PatrolScope"/>.
        /// </summary>
        public PatrolScope Scope { get; }

        /// <summary>
        /// Probability that the monster takes a §06 standstill when its 15–30 s
        /// patrol timer fires. §07's 정지 column, 잦음 → 없음.
        /// <para>
        /// A standstill is silent, so this number is really "how often the
        /// Listener loses the monster". It falls to zero at 새벽, which is exactly
        /// when §01 has the team escorting the objective — the monster stops being
        /// findable-then-lost and simply never goes quiet again.
        /// </para>
        /// </summary>
        public float StandstillChance { get; }

        /// <summary>
        /// Multiplier on flashlight range for this tier — 1.0 until 심야, then
        /// 1 − <see cref="GameConstants.LateNightFlashlightPenalty"/> and it stays
        /// there. §07: 손전등 반경 −30%.
        /// <para>
        /// Multiply the *current* range by this, after §08's upgraded-flashlight
        /// bonus, so buying the upgrade and hitting 심야 compose instead of one
        /// overwriting the other.
        /// </para>
        /// </summary>
        public float FlashlightRangeMultiplier { get; }

        /// <summary>
        /// True once §07 grants the monster knowledge of the exit (새벽 onwards).
        /// §01's 새벽 line — "호송 … 괴물은 출입구를 안다" — is the scene this flag
        /// exists to create: the way out stops being a safe direction.
        /// <para>
        /// Never returns to false; knowledge is not something the monster can
        /// lose, and treating the 추가 column as per-row would have it forget the
        /// exit at 32 min.
        /// </para>
        /// </summary>
        public bool MonsterKnowsExit { get; }

        /// <summary>Seconds into the match at which this tier begins. §07's 8-minute bands.</summary>
        public float StartSeconds => Index * GameConstants.ThreatTierSeconds;

        /// <summary>
        /// Seconds at which this tier ends, or <see cref="float.PositiveInfinity"/>
        /// for the last one. §07 gives no upper bound for 동트기 전 — the night
        /// stops getting worse but never ends.
        /// </summary>
        public float EndSeconds => IsFinal
            ? float.PositiveInfinity
            : StartSeconds + GameConstants.ThreatTierSeconds;

        /// <summary>True for the open-ended last row of the table. §07.</summary>
        public bool IsFinal => Index >= GameConstants.ThreatTierCount - 1;

        /// <summary>
        /// Zones the monster patrols on a map with <paramref name="mapZoneCount"/>
        /// zones. §07's 순찰 column, resolved.
        /// <para>
        /// "절반" rounds <b>up</b>: §07's whole thesis is that pressure only
        /// increases, and rounding down would make 심야 patrol the same 2 zones as
        /// 밤 on a 5-zone map — an escalation step that escalates nothing. On a
        /// 4-zone map it is still 2 either way; that flat spot is a finding, not a
        /// bug in this method.
        /// </para>
        /// <para>
        /// Returns 0 for a map with no zones rather than a negative count, so a
        /// world probe that knows about nothing produces a monster that patrols
        /// nothing instead of an exception at match start.
        /// </para>
        /// </summary>
        public int PatrolZoneCountFor(int mapZoneCount)
        {
            if (mapZoneCount <= 0)
            {
                return 0;
            }

            switch (Scope)
            {
                case PatrolScope.WholeMap:
                    return mapZoneCount;

                case PatrolScope.HalfTheMap:
                    // Integer ceiling of half. See the rounding note above.
                    return (mapZoneCount + 1) / 2;

                default:
                    return _fixedZoneCount < mapZoneCount ? _fixedZoneCount : mapZoneCount;
            }
        }
    }
}
