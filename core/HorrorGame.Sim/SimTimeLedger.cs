namespace HorrorGame.Sim
{
    /// <summary>
    /// Where a match's seconds went, in agent-seconds, and how many of each §07 action
    /// bought them.
    /// <para>
    /// <b>Why this exists.</b> F-006 asks a question no share-of-population figure can
    /// answer: §07 writes down what an action is supposed to cost — 한 층 더 탐색 ~3분,
    /// 나가서 배터리 교체 ~1분, 전리품 하나 더 줍기 ~40초, 상점에서 고민 ~30초 — and
    /// nothing in the project had ever measured what the simulator actually charges for
    /// the same four things. Without that denominator "simulated agents do not hesitate"
    /// is a caveat; with it, it is a ratio.
    /// </para>
    /// <para>
    /// <b>Agent-seconds, not match seconds.</b> Four players act at once, so the
    /// buckets sum to roughly four times the match length rather than to it. That is
    /// the right unit for a per-action cost — the question is what one player spends to
    /// pick one thing up — and it is why <see cref="TotalSeconds"/> is not the match
    /// duration and must not be quoted as one.
    /// </para>
    /// <para>
    /// Every bucket is filled in <c>MatchSimulator.StepAgents</c>, once per living
    /// player per fixed step, from that player's <see cref="AgentIntent"/> and whether
    /// it moved. A step lands in exactly one bucket, so nothing is double-counted and
    /// nothing is dropped.
    /// </para>
    /// </summary>
    public struct SimTimeLedger
    {
        /// <summary>Agent-seconds walking towards a 후보 지점, a clue, or the objective. §03.</summary>
        public float ClueWalkSeconds;

        /// <summary>
        /// Agent-seconds standing at one: searching it, reading what is written there
        /// (§03's <c>ClueReadSeconds</c>), or lifting the objective. §03.
        /// </summary>
        public float ClueStandSeconds;

        /// <summary>Agent-seconds walking towards 전리품. §08 · §12's 막힌 길.</summary>
        public float LootWalkSeconds;

        /// <summary>Agent-seconds standing over it, or turning a 금고. §08.</summary>
        public float LootStandSeconds;

        /// <summary>
        /// Agent-seconds walking back out — to the door, and the flat leg from the door
        /// to the vehicle. §03's 왕복.
        /// </summary>
        public float ExitWalkSeconds;

        /// <summary>Agent-seconds being chased or backing away from the monster. §06.</summary>
        public float FleeSeconds;

        /// <summary>
        /// Agent-seconds standing at the vehicle: §08's shop, and the climb either side
        /// of it once <see cref="SimScenario.SurfaceTransitSeconds"/> charges one.
        /// </summary>
        public float VehicleSeconds;

        /// <summary>후보 지점 walked up to and searched. The denominator for §07's 한 층 더 탐색.</summary>
        public int SiteSearches;

        /// <summary>Pieces of 전리품 actually lifted, 금고 문서 included. The denominator for §07's 전리품 하나 더 줍기.</summary>
        public int LootPickups;

        /// <summary>Clues read to completion, misreads included. §03.</summary>
        public int ClueReads;

        /// <summary>Visits to the shop. The denominator for §07's 상점에서 고민, and for 나가서 배터리 교체.</summary>
        public int ShopVisits;

        /// <summary>
        /// Match seconds — not agent-seconds — with nobody left below ground. §07 prices
        /// 나가서 배터리 교체 and 상점에서 고민 in wall-clock with the team present, so
        /// this is the figure those two rows have to be compared against;
        /// <see cref="VehicleSeconds"/> counts the same stretch four times over and also
        /// counts the first player standing at the vehicle waiting for the last.
        /// </summary>
        public float TeamSurfaceSeconds;

        /// <summary>All seven buckets. Roughly four times the match length — see the type's remarks.</summary>
        public float TotalSeconds =>
            ClueWalkSeconds + ClueStandSeconds + LootWalkSeconds + LootStandSeconds
            + ExitWalkSeconds + FleeSeconds + VehicleSeconds;

        /// <summary>Agent-seconds spent per 전리품 lifted — walk to it plus stand over it. Zero if none was.</summary>
        public float SecondsPerLootPiece =>
            LootPickups == 0 ? 0f : (LootWalkSeconds + LootStandSeconds) / LootPickups;

        /// <summary>Agent-seconds spent per 후보 지점 searched — walk plus search plus read. Zero if none was.</summary>
        public float SecondsPerSiteSearch =>
            SiteSearches == 0 ? 0f : (ClueWalkSeconds + ClueStandSeconds) / SiteSearches;

        /// <summary>Agent-seconds spent per round trip — the walk out and the time at the vehicle. Zero if none was made.</summary>
        public float SecondsPerRoundTrip =>
            ShopVisits == 0 ? 0f : (ExitWalkSeconds + VehicleSeconds) / ShopVisits;
    }
}
