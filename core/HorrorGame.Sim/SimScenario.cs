using System;
using HorrorGame.Core;

namespace HorrorGame.Sim
{
    /// <summary>
    /// The what-ifs F-006's options are stated in — time the game charges that this
    /// simulator does not, plus the two structural knobs the finding's options name.
    /// <para>
    /// <b>Why this is not <see cref="BalanceOverrides"/>.</b> That struct shadows a
    /// <see cref="GameConstants"/> value, and its self-check exists to prove the shadow
    /// reproduces Core exactly at the shipped number. Nothing here shadows a constant.
    /// These are seconds the *design* spends and the *simulator* does not: §07 prices
    /// 전리품 하나 더 줍기 at ~40 s and 상점에서 고민 at ~30 s, and
    /// <see cref="MatchSimulator"/> charges one fixed step for the first and zero for
    /// the second. Putting them in <c>BalanceOverrides</c> would make the self-check
    /// meaningless, because there is no constant to check them against — the constant
    /// does not exist yet, which is the finding.
    /// </para>
    /// <para>
    /// <b>Every field defaults to today's simulator.</b> <see cref="IsDefault"/> is
    /// true for an ordinary run, and a run with no scenario flags reproduces the
    /// population in docs/BALANCE-FINDINGS.md F-006 byte for byte. That is the
    /// property that makes a sweep across these axes readable: the zero point is the
    /// measured game.
    /// </para>
    /// </summary>
    public readonly struct SimScenario
    {
        private SimScenario(
            float siteSearchSeconds,
            float lootPickupSeconds,
            float surfaceTransitSeconds,
            float shopSeconds,
            float threatTierScale,
            int startingSpareCells,
            float startSeconds)
        {
            SiteSearchSeconds = Positive(siteSearchSeconds);
            LootPickupSeconds = Positive(lootPickupSeconds);
            SurfaceTransitSeconds = Positive(surfaceTransitSeconds);
            ShopSeconds = Positive(shopSeconds);
            ThreatTierScale = threatTierScale > 0f ? threatTierScale : 1f;
            StartingSpareCells = startingSpareCells > 0 ? startingSpareCells : 0;
            StartSeconds = Positive(startSeconds);
        }

        /// <summary>
        /// Seconds an agent stands at a 후보 지점 before it learns whether a clue is
        /// written there. F-006 option 3's "required dwell time", and §07's
        /// 한 층 더 탐색 ~3분 stated per site rather than per storey.
        /// <para>
        /// The simulator resolves this in one fixed step: the instant an agent's
        /// position reaches the node, <see cref="MatchSimulator"/> knows what is on it.
        /// A human has to look. Zero is today's simulator.
        /// </para>
        /// </summary>
        public float SiteSearchSeconds { get; }

        /// <summary>
        /// Seconds an agent stands over a piece of 전리품 before it is in the bag.
        /// §07 prices this at ~40 s — "전리품 하나 더 줍기" — and the simulator charges
        /// one fixed step, 0.02 s. Zero is today's simulator.
        /// </summary>
        public float LootPickupSeconds { get; }

        /// <summary>
        /// Seconds the whole team spends on the climb, each way, between the door and
        /// the surface. §07's 나가서 배터리 교체 ~1분 and 시각 확인하러 나가기 ~1분 are
        /// both this leg; the simulator teleports it —
        /// <c>PlayerState.TrySurface</c> and <c>SimAgent.PlaceAtEntrance</c> both cost
        /// nothing — and charges only the flat walk between the door and the vehicle.
        /// Zero is today's simulator.
        /// </summary>
        public float SurfaceTransitSeconds { get; }

        /// <summary>
        /// Seconds the team stands at the vehicle deciding what to buy. §07's
        /// 상점에서 고민 ~30초, and §08's 공용 지갑 makes it a negotiation between four
        /// people rather than a menu. The simulator's shop resolves inside a single
        /// step. Zero is today's simulator.
        /// </summary>
        public float ShopSeconds { get; }

        /// <summary>
        /// How much faster §07's clock runs than the match does — F-006 option 2,
        /// "compress the threat curve to fit the real match length".
        /// <para>
        /// A tier is <see cref="GameConstants.ThreatTierSeconds"/> of *clock*, so
        /// running the clock at 2× makes every tier arrive in half the wall-clock time,
        /// which is exactly a table of 4-minute bands. Expressed as a rate rather than
        /// as a band length because <see cref="GameConstants.ThreatTierSeconds"/> is a
        /// <c>const</c> that Core reads directly from
        /// <c>ThreatTier.StartSeconds</c> — there is no seam to shadow it at, and
        /// scaling the tick reproduces the compressed table exactly without one.
        /// </para>
        /// <para>1.0 is §07 as written. See <see cref="TierMinutes"/>.</para>
        /// </summary>
        public float ThreatTierScale { get; }

        /// <summary>
        /// Spare cells each player carries in on 1차 잠입 — the bootstrap grubstake.
        /// <para>
        /// 40.6% of matches end because every light is dead and the wallet cannot buy
        /// another cell, and §08's growth curve never starts for them. §08 puts
        /// 구매력 at 0 before the first descent, which this does not violate: a spare
        /// cell is equipment carried in, not something bought. Zero is today's
        /// simulator and §08's literal 맨몸.
        /// </para>
        /// </summary>
        public int StartingSpareCells { get; }

        /// <summary>
        /// Seconds of §07's clock wound forward before the first descent, so a scenario
        /// can begin at 심야 instead of 초저녁. A designer's what-if: §06's margins are
        /// stated against a 4.8 m/s monster, which §07 does not produce until tier 2,
        /// so a question about 심야 cannot be answered by matches that end in 초저녁.
        /// </summary>
        public float StartSeconds { get; }

        /// <summary>The simulator as F-006 measured it. Every knob off.</summary>
        public static SimScenario Default =>
            new SimScenario(0f, 0f, 0f, 0f, 1f, 0, 0f);

        /// <summary>
        /// §07's action-cost table, taken literally and charged as dwell. This is
        /// F-006 option 3 at the design document's own numbers, and it is also the
        /// human-overhead estimate — §07's costs are what the designer already believes
        /// a person spends, so charging them is not a guess.
        /// <para>
        /// 한 층 더 탐색 ~3분 is not charged here: it is a *route*, and the simulator
        /// already walks routes. What it does not charge is the search at the end of
        /// one, which is <see cref="SiteSearchSeconds"/> and is set from the same row
        /// divided by the 후보 지점 a storey holds (15 sites over 5 storeys = 3 per
        /// storey, so 180 s ÷ 3 = 60 s of searching per site).
        /// </para>
        /// </summary>
        public static SimScenario SevenTable =>
            Default
                .WithSiteSearchSeconds(60f)
                .WithLootPickupSeconds(40f)
                .WithSurfaceTransitSeconds(60f)
                .WithShopSeconds(30f);

        /// <summary>True when nothing is charged and the run is F-006's measured population.</summary>
        public bool IsDefault =>
            SiteSearchSeconds == 0f
            && LootPickupSeconds == 0f
            && SurfaceTransitSeconds == 0f
            && ShopSeconds == 0f
            && ThreatTierScale == 1f
            && StartingSpareCells == 0
            && StartSeconds == 0f;

        /// <summary>True when any dwell is charged, so the ledger has something to report.</summary>
        public bool ChargesDwell =>
            SiteSearchSeconds > 0f || LootPickupSeconds > 0f
            || SurfaceTransitSeconds > 0f || ShopSeconds > 0f;

        /// <summary>
        /// The length of one §07 tier under <see cref="ThreatTierScale"/>, in minutes.
        /// §07 as written is 8.
        /// </summary>
        public float TierMinutes => GameConstants.ThreatTierSeconds / 60f / ThreatTierScale;

        /// <summary>Same scenario with §07's tiers <paramref name="minutes"/> long instead of 8.</summary>
        /// <param name="minutes">Band length. Must be positive; anything else leaves the scale at 1.</param>
        public SimScenario WithTierMinutes(float minutes) =>
            minutes > 0f
                ? WithThreatTierScale(GameConstants.ThreatTierSeconds / 60f / minutes)
                : this;

        /// <summary>Same scenario at a different §07 clock rate. See <see cref="ThreatTierScale"/>.</summary>
        public SimScenario WithThreatTierScale(float scale) =>
            new SimScenario(SiteSearchSeconds, LootPickupSeconds, SurfaceTransitSeconds,
                ShopSeconds, scale, StartingSpareCells, StartSeconds);

        /// <summary>Same scenario with a different 후보 지점 search cost.</summary>
        public SimScenario WithSiteSearchSeconds(float seconds) =>
            new SimScenario(seconds, LootPickupSeconds, SurfaceTransitSeconds,
                ShopSeconds, ThreatTierScale, StartingSpareCells, StartSeconds);

        /// <summary>Same scenario with a different 전리품 pickup cost.</summary>
        public SimScenario WithLootPickupSeconds(float seconds) =>
            new SimScenario(SiteSearchSeconds, seconds, SurfaceTransitSeconds,
                ShopSeconds, ThreatTierScale, StartingSpareCells, StartSeconds);

        /// <summary>Same scenario with a different climb cost, each way.</summary>
        public SimScenario WithSurfaceTransitSeconds(float seconds) =>
            new SimScenario(SiteSearchSeconds, LootPickupSeconds, seconds,
                ShopSeconds, ThreatTierScale, StartingSpareCells, StartSeconds);

        /// <summary>Same scenario with a different 상점에서 고민 cost.</summary>
        public SimScenario WithShopSeconds(float seconds) =>
            new SimScenario(SiteSearchSeconds, LootPickupSeconds, SurfaceTransitSeconds,
                seconds, ThreatTierScale, StartingSpareCells, StartSeconds);

        /// <summary>Same scenario with a different bootstrap grubstake.</summary>
        public SimScenario WithStartingSpareCells(int cells) =>
            new SimScenario(SiteSearchSeconds, LootPickupSeconds, SurfaceTransitSeconds,
                ShopSeconds, ThreatTierScale, cells, StartSeconds);

        /// <summary>Same scenario starting further into §07's night.</summary>
        public SimScenario WithStartSeconds(float seconds) =>
            new SimScenario(SiteSearchSeconds, LootPickupSeconds, SurfaceTransitSeconds,
                ShopSeconds, ThreatTierScale, StartingSpareCells, seconds);

        /// <summary>
        /// Every dwell cost multiplied, §07's table held in proportion. The axis
        /// F-006 option 3 is swept along: 0 is today's simulator, 1 is §07's table as
        /// written, 2 is a team twice as slow as the designer expected.
        /// </summary>
        /// <param name="factor">Multiplier on all four dwell costs. Negative is read as zero.</param>
        public SimScenario ScaledDwell(float factor)
        {
            var scale = factor > 0f ? factor : 0f;
            return new SimScenario(
                SiteSearchSeconds * scale,
                LootPickupSeconds * scale,
                SurfaceTransitSeconds * scale,
                ShopSeconds * scale,
                ThreatTierScale,
                StartingSpareCells,
                StartSeconds);
        }

        /// <summary>A one-line description for a sweep table's row label.</summary>
        public override string ToString()
        {
            if (IsDefault)
            {
                return "shipped";
            }

            return "search " + Seconds(SiteSearchSeconds)
                + " · loot " + Seconds(LootPickupSeconds)
                + " · climb " + Seconds(SurfaceTransitSeconds)
                + " · shop " + Seconds(ShopSeconds)
                + " · tier " + TierMinutes.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " min"
                + " · cells " + StartingSpareCells;
        }

        private static string Seconds(float value) =>
            value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "s";

        // NaN fails every relational test, so this one guard rejects NaN and negatives
        // together. A NaN dwell would freeze an agent for the rest of the match and
        // report it as a longer game, which is the exact failure this whole finding is
        // about not making twice.
        private static float Positive(float value) =>
            value > 0f && !float.IsPositiveInfinity(value) ? value : 0f;
    }
}
