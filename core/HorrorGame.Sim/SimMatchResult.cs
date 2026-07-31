using HorrorGame.Core.Match;
using HorrorGame.Core.Telemetry;

namespace HorrorGame.Sim
{
    /// <summary>
    /// One simulated match: §13's <see cref="MatchSummary"/> plus the handful of
    /// measurements §16-2 needs that a shipped build would never send home.
    /// <para>
    /// The split is deliberate. Everything a real player's client could report is in
    /// <see cref="Summary"/> and reaches the sink through <see cref="MatchRecorder"/>,
    /// so the simulator exercises exactly the buckets §13 defines. The extra fields
    /// here are host-side truth — how much loot was left on the floor, what the team
    /// could have afforded — which is legitimate for a designer's tool and would be a
    /// privacy problem in a telemetry payload.
    /// </para>
    /// </summary>
    public struct SimMatchResult
    {
        /// <summary>The seed. Replays this match exactly.</summary>
        public int Seed;

        /// <summary>§13's payload, as sent.</summary>
        public MatchSummary Summary;

        /// <summary>§02's verdict.</summary>
        public MatchOutcome Outcome;

        /// <summary>Wall-clock length of the match in seconds. §01 wants 25–35 minutes.</summary>
        public float DurationSeconds;

        /// <summary>§03's 왕복 count. Wanted between 2 and 5.</summary>
        public int RoundTrips;

        /// <summary>Round trips the layout was built to need, before any misread. §03.</summary>
        public int PlannedRoundTrips;

        /// <summary>Credits in the shared wallet before the first descent. §08 says this must be 0.</summary>
        public int CreditsBeforeFirstDescent;

        /// <summary>Credits after the first return, when §08 expects "소모품 몇 개".</summary>
        public int CreditsAfterFirstReturn;

        /// <summary>The most the team ever held at once.</summary>
        public int PeakCredits;

        /// <summary>Credits still unspent when the match ended — §08's "필요한 건 다 있는데 시간이 없다".</summary>
        public int CreditsUnspent;

        /// <summary>Everything the shop sold this match, at §08's prices.</summary>
        public int CreditsSpent;

        /// <summary>Total banked. Against <c>ShopCatalogue.CostOfOneOfEverything</c> this is §16-2's ratio.</summary>
        public int CreditsEarned;

        /// <summary>Match seconds elapsed when the first 강화 아이템 was bought, or -1.</summary>
        public float FirstUpgradeSeconds;

        /// <summary>Descent index the first 강화 아이템 was bought before. §08's curve names 2~3차.</summary>
        public int FirstUpgradeDescent;

        /// <summary>Pieces of 전리품 sold. §03 calls loot optional; this says how optional it looked.</summary>
        public int LootSold;

        /// <summary>Pieces still lying in the building when it ended.</summary>
        public int LootLeftBehind;

        /// <summary>Highest §08 weight band any agent reached (0–3).</summary>
        public int PeakWeightBand;

        /// <summary>Chases the monster opened on the 주자.</summary>
        public int RunnerChases;

        /// <summary>Of those, the ones the 주자 broke. §06's "주자만 도망칠 수 있다".</summary>
        public int RunnerEscapes;

        /// <summary>Chases opened on a 주자 already in §08's band 2 or worse — F-001's population.</summary>
        public int RunnerChasesLoaded;

        /// <summary>Of those, the ones the 주자 broke. This is the number F-001 option 2 is about.</summary>
        public int RunnerEscapesLoaded;

        /// <summary>Chases opened on anybody, and the ones that ended without a death.</summary>
        public int Chases;

        /// <summary>Chases that ended in a release rather than a catch. §06.</summary>
        public int ChaseEscapes;

        /// <summary>Threat tier the match ended on. §07.</summary>
        public int FinalTierIndex;

        /// <summary>Whether the team ever pinned the objective's site through §03's chain.</summary>
        public bool ObjectivePinned;

        /// <summary>Times the team walked to a site the chain pinned and found nothing. §03's misread cost.</summary>
        public int WrongSiteVisits;

        /// <summary>Whether the sim's own guard rail ended it — §07's table ran out before §02 did.</summary>
        public bool HitTimeCap;

        /// <summary>
        /// Whether the run ended because every light was dead and the wallet could not
        /// buy another cell — §03's "배터리가 떨어지면 단서를 읽을 수 없다" taken to its
        /// conclusion.
        /// <para>
        /// Separated out because it is a different failure from losing: §02 files it as
        /// 생존, the team walks out alive, and the match simply stops. It became worth
        /// counting when the simulator moved onto the five-storey building — a first
        /// descent that spends its whole battery walking to 후보 지점 can surface with
        /// nothing to sell, and a team with nothing to sell cannot fund a second
        /// descent. Any read of §01's match length has to know what share of the
        /// population ended this way (docs/BALANCE-FINDINGS.md F-006).
        /// </para>
        /// </summary>
        public bool EndedOutOfLight;
    }
}
