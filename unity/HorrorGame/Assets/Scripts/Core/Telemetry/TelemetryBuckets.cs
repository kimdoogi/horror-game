using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Telemetry
{
    /// <summary>
    /// The §13 stage-1 counter names, and the rule that files a measurement into one of them.
    /// <para>
    /// §13's insight is that a histogram needs no database: define one counter per
    /// band and the distribution falls out of plain increments, which Steam Stats
    /// stores and globally aggregates for free. The document writes the template
    /// out literally —
    /// <c>aggro_duration_0_5s</c> / <c>5_10s</c> / <c>10_15s</c> / <c>15s_plus</c>
    /// — uniform bands with one open-ended tail, and every family below follows it.
    /// </para>
    /// <para>
    /// <b>Every counter here answers a specific design question.</b> §13's own
    /// table is the mapping, and it is reproduced per-family in the members below
    /// so that nobody deletes a counter that a decision is waiting on:
    /// </para>
    /// <list type="table">
    /// <item><term>어그로 지속 시간</term><description>validates §06's 12 m 해제 거리.</description></item>
    /// <item><term>왕복 횟수</term><description>validates §07's time curve (§03 expects 2–5).</description></item>
    /// <item><term>후진(S) 사용 시간 비율</term><description>validates §05's 65% backward multiplier.</description></item>
    /// <item><term>직업별 선택 카운터 5개</term><description>validates §11's "필수 직업이 있으면 풀이 가짜가 된다".</description></item>
    /// <item><term>아이템별 구매 카운터</term><description>validates §08's economy and its growth curve.</description></item>
    /// <item><term>클리어 / 전멸 / 포기</term><description>overall balance (§02).</description></item>
    /// </list>
    /// <para>
    /// <b>Bands are inclusive-exclusive and total.</b> A band covers
    /// <c>[low, high)</c>, so a value landing exactly on a boundary belongs to the
    /// band above it; the lowest band is open below and the highest open above.
    /// Totality is not tidiness — Steam Stats can only be read as a distribution
    /// if the bands sum to the number of observations, so a value that fell
    /// outside every band would make every derived percentage quietly wrong
    /// instead of visibly missing. Negatives, zero, ±infinity and NaN therefore
    /// all land somewhere: see <see cref="UniformBucketIndex"/>.
    /// </para>
    /// <para>
    /// <b>The names are a wire format.</b> Steam Stats are keyed by string and
    /// aggregate across the whole player base, so renaming a counter discards its
    /// history. That is why the role and item names are an explicit switch rather
    /// than <c>ToString()</c> — renaming a C# enum member must not silently start
    /// a new stat — and why the boundaries are asserted against the literal names
    /// in TelemetryTests.
    /// </para>
    /// </summary>
    public static class TelemetryBuckets
    {
        /// <summary>
        /// Histogram name for a single chase's length in seconds. §13 uses the
        /// distribution to validate §06's 12 m 해제 거리: if chases almost never
        /// end, the release condition is unreachable in the map §12 built.
        /// </summary>
        public const string AggroDurationHistogram = "aggro_duration";

        /// <summary>
        /// Histogram name for descents per match. §03 expects 2–5 and §13 uses the
        /// spread to check §07's curve — a match that needs six round trips has
        /// spent its night walking rather than deciding.
        /// </summary>
        public const string RoundTripsHistogram = "round_trips";

        /// <summary>
        /// Histogram name for the share of movement time spent holding S, 0–1.
        /// §13 uses it to validate §05's 65% backward multiplier: the peek dilemma
        /// is working when this stays low but never reaches zero.
        /// </summary>
        public const string BackpedalShareHistogram = "backpedal_share";

        /// <summary>Prefix for §13's five 직업별 선택 카운터. §11.</summary>
        public const string RolePickPrefix = "role_pick";

        /// <summary>Prefix for §13's 아이템별 구매 카운터. §08.</summary>
        public const string PurchasePrefix = "purchase";

        /// <summary>Prefix for §13's 클리어 / 전멸 / 포기 counters. §02.</summary>
        public const string OutcomePrefix = "outcome";

        /// <summary>§13's 클리어 — §02's 완전 승리 and 부분 승리 together, both being 목표물 회수.</summary>
        public const string OutcomeClear = OutcomePrefix + "_clear";

        /// <summary>§13's 전멸 — §02's 패배, where the match's loot, credits and knowledge are all lost.</summary>
        public const string OutcomeWipe = OutcomePrefix + "_wipe";

        /// <summary>§13's 포기 — the host left. §13 does not migrate hosts, so the session simply ends.</summary>
        public const string OutcomeAbandon = OutcomePrefix + "_abandon";

        /// <summary>
        /// §02's 생존 — escaped without the objective.
        /// <para>
        /// Not in §13's list. §13 names three outcome counters (클리어 / 전멸 / 포기)
        /// and §02 defines four results plus §13's own abandon, so 생존 — the
        /// outcome §02 singles out because "그 판의 정보는 남는다" — has no counter
        /// in the document. Folding it into 클리어 would overstate the clear rate
        /// and folding it into 전멸 would understate it, so it gets its own name
        /// and the disagreement is recorded in docs/BALANCE-FINDINGS.md instead of
        /// being papered over here.
        /// </para>
        /// </summary>
        public const string OutcomeSurvived = OutcomePrefix + "_survived";

        /// <summary>
        /// A match reported as still running. That is a caller bug, and it gets its
        /// own counter so the bug shows up as an implausible row rather than as
        /// inflated clears.
        /// </summary>
        public const string OutcomeInProgress = OutcomePrefix + "_in_progress";

        /// <summary>An outcome value outside <see cref="MatchOutcome"/>. Keeps the family total when an int is cast in from the network.</summary>
        public const string OutcomeUnknown = OutcomePrefix + "_unknown";

        /// <summary>
        /// Counter that absorbs an observation whose histogram nobody recognises.
        /// Appended to the caller's own name so the mistake is legible in the
        /// readback; dropping the observation instead would leave the histogram it
        /// was meant for looking merely unpopular.
        /// </summary>
        public const string UnbucketedSuffix = "_unbucketed";

        /// <summary>
        /// Counter that absorbs a null or blank counter name. Telemetry must never
        /// throw into gameplay (§13's summary is fire-and-forget), so a malformed
        /// name is counted somewhere visible rather than rejected.
        /// </summary>
        public const string InvalidCounterName = "telemetry_invalid_counter_name";

        /// <summary>Suffix marking the open-ended top band. §13 writes it as <c>15s_plus</c>.</summary>
        private const string OpenBandSuffix = "_plus";

        /// <summary>Unit written into a seconds band name — §13's <c>_0_5s</c>.</summary>
        private const string UnitSeconds = "s";

        /// <summary>Unit written into a share band name. Percent, because "0.05" is not a legal-looking stat name.</summary>
        private const string UnitPercent = "pct";

        /// <summary>Count bands carry no unit: §13's template gives <c>round_trips_3</c>.</summary>
        private const string UnitNone = "";

        /// <summary>Scale from seconds to the number written in a seconds band name.</summary>
        private const float ScaleSeconds = 1f;

        /// <summary>Scale from a 0–1 share to the number written in a percent band name.</summary>
        private const float ScalePercent = 100f;

        /// <summary>Role pick counter for a slot holding <see cref="RoleId.None"/> or an undefined value.</summary>
        private const string RolePickUnassigned = RolePickPrefix + "_unassigned";

        /// <summary>Purchase counter for <see cref="ShopItemId.None"/> or an undefined value.</summary>
        private const string PurchaseUnknown = PurchasePrefix + "_unknown";

        private static readonly ReadOnlyCollection<string> _aggroDurationBuckets =
            Array.AsReadOnly(BuildUniformBands(
                AggroDurationHistogram,
                GameConstants.TelemetryAggroBucketSeconds,
                GameConstants.TelemetryAggroBucketCount,
                ScaleSeconds,
                UnitSeconds));

        private static readonly ReadOnlyCollection<string> _backpedalShareBuckets =
            Array.AsReadOnly(BuildUniformBands(
                BackpedalShareHistogram,
                GameConstants.TelemetryBackpedalShareBucketFraction,
                GameConstants.TelemetryBackpedalShareBucketCount,
                ScalePercent,
                UnitPercent));

        private static readonly ReadOnlyCollection<string> _roundTripsBuckets =
            Array.AsReadOnly(BuildRoundTripBands());

        // Written out rather than derived from the enum: these strings outlive the
        // C# identifiers, and a refactor that renames RoleId.Flasher must not
        // orphan a year of role_pick_flasher history. RolePickUnassigned is last
        // because it is not one of §13's five — see RolePick.
        private static readonly ReadOnlyCollection<string> _rolePickCounters =
            Array.AsReadOnly(new[]
            {
                RolePickPrefix + "_listener",
                RolePickPrefix + "_observer",
                RolePickPrefix + "_runner",
                RolePickPrefix + "_engineer",
                RolePickPrefix + "_flasher",
                RolePickUnassigned,
            });

        private static readonly ReadOnlyCollection<string> _purchaseCounters =
            Array.AsReadOnly(new[]
            {
                PurchasePrefix + "_battery",
                PurchasePrefix + "_repair_materials",
                PurchasePrefix + "_first_aid_kit",
                PurchasePrefix + "_upgraded_flashlight",
                PurchasePrefix + "_flare",
                PurchasePrefix + "_chalk",
                PurchasePrefix + "_rope",
                PurchasePrefix + "_bag",
                PurchasePrefix + "_blueprint",
                PurchasePrefix + "_pocket_watch",
                PurchasePrefix + "_detector",
                PurchasePrefix + "_bait",
                PurchasePrefix + "_suppressor",
                PurchaseUnknown,
            });

        private static readonly ReadOnlyCollection<string> _outcomeCounters =
            Array.AsReadOnly(new[]
            {
                OutcomeClear,
                OutcomeWipe,
                OutcomeAbandon,
                OutcomeSurvived,
                OutcomeInProgress,
                OutcomeUnknown,
            });

        private static readonly ReadOnlyCollection<string> _allCounters = Array.AsReadOnly(BuildAllCounters());

        private static readonly HashSet<string> _knownCounters = new HashSet<string>(_allCounters, StringComparer.Ordinal);

        /// <summary>
        /// §13's 어그로 지속 시간 counters, lowest band first, open tail last.
        /// Provision exactly these as Steam Stats.
        /// </summary>
        public static IReadOnlyList<string> AggroDurationBuckets => _aggroDurationBuckets;

        /// <summary>§13's 왕복 횟수 counters: one per exact count up to §03's expected maximum, then an open tail.</summary>
        public static IReadOnlyList<string> RoundTripsBuckets => _roundTripsBuckets;

        /// <summary>§13's 후진 사용 시간 비율 counters, lowest band first, open tail last.</summary>
        public static IReadOnlyList<string> BackpedalShareBuckets => _backpedalShareBuckets;

        /// <summary>
        /// §13's 직업별 선택 카운터, in <see cref="RoleId"/> order, followed by the
        /// unassigned counter that keeps the family total. §11's check reads the
        /// first five against each other.
        /// </summary>
        public static IReadOnlyList<string> RolePickCounters => _rolePickCounters;

        /// <summary>§13's 아이템별 구매 카운터, in <see cref="ShopItemId"/> order, followed by the unknown counter.</summary>
        public static IReadOnlyList<string> PurchaseCounters => _purchaseCounters;

        /// <summary>§13's 클리어 / 전멸 / 포기, followed by the counters §02 needs and §13 omits. See <see cref="OutcomeSurvived"/>.</summary>
        public static IReadOnlyList<string> OutcomeCounters => _outcomeCounters;

        /// <summary>
        /// Every counter §13 stage 1 can emit. This is the provisioning list: a
        /// name missing from Steamworks is silently discarded at runtime, so the
        /// Steam Stats page and this list must agree exactly.
        /// </summary>
        public static IReadOnlyList<string> AllCounters => _allCounters;

        /// <summary>
        /// Which band of §13's 어그로 지속 시간 histogram a chase of
        /// <paramref name="seconds"/> belongs to.
        /// <para>
        /// One call per chase that ends, not per match: §13 wants the distribution
        /// of chase lengths, because that is what says whether §06's release
        /// condition (12 m *and* 3 s without a sight line) is reachable at all.
        /// </para>
        /// </summary>
        public static string AggroDuration(float seconds)
        {
            var index = UniformBucketIndex(
                seconds,
                GameConstants.TelemetryAggroBucketSeconds,
                GameConstants.TelemetryAggroBucketCount);
            return _aggroDurationBuckets[index];
        }

        /// <summary>
        /// Which band of §13's 왕복 횟수 histogram a match with
        /// <paramref name="roundTrips"/> descents belongs to.
        /// <para>
        /// Exact counts rather than ranges, because the whole range §03 cares
        /// about is 2–5 and collapsing it would delete the answer. Anything above
        /// §03's maximum shares the open tail: past six round trips the match has
        /// already failed §07's curve and the exact figure changes nothing.
        /// Negatives are impossible and are filed at zero.
        /// </para>
        /// </summary>
        public static string RoundTrips(int roundTrips)
        {
            if (roundTrips <= 0)
            {
                return _roundTripsBuckets[0];
            }

            if (roundTrips > GameConstants.ExpectedRoundTripsMax)
            {
                return _roundTripsBuckets[_roundTripsBuckets.Count - 1];
            }

            return _roundTripsBuckets[roundTrips];
        }

        /// <summary>
        /// Which band of §13's 후진 비율 histogram a share of
        /// <paramref name="share"/> (0–1) belongs to.
        /// <para>
        /// §13 asks for the share and §05 never states a target for it, so the
        /// bands are uniform per §13's own template and the tail is placed where
        /// the share stops changing the answer — see
        /// <see cref="GameConstants.TelemetryBackpedalShareBucketCount"/>. A share
        /// above 1 is impossible (backpedalling is movement) and lands in the
        /// tail; a negative share lands in the lowest band.
        /// </para>
        /// </summary>
        public static string BackpedalShare(float share)
        {
            var index = UniformBucketIndex(
                share,
                GameConstants.TelemetryBackpedalShareBucketFraction,
                GameConstants.TelemetryBackpedalShareBucketCount);
            return _backpedalShareBuckets[index];
        }

        /// <summary>
        /// The §13 counter for one player having taken <paramref name="role"/>.
        /// <para>
        /// Four increments per match against five counters, so a fair distribution
        /// puts each role at 80% of matches rather than 20% — §11's rule is checked
        /// by how far apart the five sit, and the discriminating figure is which
        /// role is *absent*. <see cref="RoleId.None"/> and any undefined value go
        /// to the unassigned counter, because the invariant that makes the five
        /// readable is "the family sums to four per match": if a lobby bug shipped
        /// an unset slot, that has to be visible rather than quietly deflating one
        /// role's share.
        /// </para>
        /// </summary>
        public static string RolePick(RoleId role)
        {
            switch (role)
            {
                case RoleId.Listener: return _rolePickCounters[0];
                case RoleId.Observer: return _rolePickCounters[1];
                case RoleId.Runner: return _rolePickCounters[2];
                case RoleId.Engineer: return _rolePickCounters[3];
                case RoleId.Flasher: return _rolePickCounters[4];
                default: return RolePickUnassigned;
            }
        }

        /// <summary>
        /// The §13 counter for one unit of <paramref name="item"/> being bought.
        /// <para>
        /// §08 is a set of §10 trades, and §13 measures which ones players
        /// actually take: a 강화 손전등 that nobody buys means its 대가 ("괴물이 2배
        /// 멀리서 본다") is priced too high, and a 소음기 that sells well means teams
        /// are voiding their own Listener. §11's role substitutes — 감지기, 미끼,
        /// 조명탄, 섬광탄 — are the other half of the answer to "is any role
        /// compulsory".
        /// </para>
        /// </summary>
        public static string Purchase(ShopItemId item)
        {
            switch (item)
            {
                case ShopItemId.Battery: return _purchaseCounters[0];
                case ShopItemId.RepairMaterials: return _purchaseCounters[1];
                case ShopItemId.FirstAidKit: return _purchaseCounters[2];
                case ShopItemId.UpgradedFlashlight: return _purchaseCounters[3];
                case ShopItemId.Flare: return _purchaseCounters[4];
                case ShopItemId.Chalk: return _purchaseCounters[5];
                case ShopItemId.Rope: return _purchaseCounters[6];
                case ShopItemId.Bag: return _purchaseCounters[7];
                case ShopItemId.Blueprint: return _purchaseCounters[8];
                case ShopItemId.PocketWatch: return _purchaseCounters[9];
                case ShopItemId.Detector: return _purchaseCounters[10];
                case ShopItemId.Bait: return _purchaseCounters[11];
                case ShopItemId.Suppressor: return _purchaseCounters[12];
                default: return PurchaseUnknown;
            }
        }

        /// <summary>
        /// The §13 counter for how a match ended.
        /// <para>
        /// §13 asks for 클리어 / 전멸 / 포기. 클리어 is both of §02's victories,
        /// since both are 목표물 회수 and §02 separates them only by how many got
        /// out — a number <see cref="MatchSummary.PlayersEscaped"/> already
        /// carries. §02's 생존 has no §13 counter at all; see
        /// <see cref="OutcomeSurvived"/>.
        /// </para>
        /// </summary>
        public static string Outcome(MatchOutcome outcome)
        {
            switch (outcome)
            {
                case MatchOutcome.FullVictory:
                case MatchOutcome.PartialVictory:
                    return OutcomeClear;
                case MatchOutcome.Wiped:
                    return OutcomeWipe;
                case MatchOutcome.Abandoned:
                    return OutcomeAbandon;
                case MatchOutcome.Survived:
                    return OutcomeSurvived;
                case MatchOutcome.InProgress:
                    return OutcomeInProgress;
                default:
                    return OutcomeUnknown;
            }
        }

        /// <summary>
        /// Resolves the raw measurement <paramref name="value"/> against the named
        /// histogram, which is what lets <see cref="ITelemetrySink.Observe"/> take
        /// a measurement rather than a band. An unrecognised histogram is not
        /// dropped — see <see cref="UnbucketedSuffix"/>.
        /// </summary>
        public static string Bucket(string? histogram, float value)
        {
            if (string.IsNullOrWhiteSpace(histogram))
            {
                return InvalidCounterName;
            }

            switch (histogram)
            {
                case AggroDurationHistogram:
                    return AggroDuration(value);
                case BackpedalShareHistogram:
                    return BackpedalShare(value);
                case RoundTripsHistogram:
                    return RoundTrips(ToCount(value));
                default:
                    return histogram + UnbucketedSuffix;
            }
        }

        /// <summary>
        /// True when <paramref name="name"/> is one of §13's stage-1 counters, so
        /// the Steam Stats provisioning list and the code cannot drift apart
        /// unnoticed. The diagnostic names
        /// (<see cref="InvalidCounterName"/>, <see cref="UnbucketedSuffix"/>) are
        /// deliberately not known counters: they exist to be noticed.
        /// </summary>
        public static bool IsKnownCounter(string? name) => name != null && _knownCounters.Contains(name);

        /// <summary>
        /// Index of the band <paramref name="value"/> falls into, for a family of
        /// <paramref name="bucketCount"/> bands of width
        /// <paramref name="bucketWidth"/> starting at zero, the last band being
        /// open-ended.
        /// <para>
        /// Total by construction, which is the property §13's arithmetic depends
        /// on. Every band above the first is entered by a single
        /// <c>value &gt;= bucketWidth * i</c> test, taken from the top down, and
        /// anything that passes none of them belongs to band 0. NaN fails every
        /// comparison and so lands at the bottom along with negatives and zero;
        /// +infinity passes the first test and lands in the open tail.
        /// </para>
        /// <para>
        /// The boundary is recomputed as <c>bucketWidth * i</c> rather than derived
        /// by dividing, because that is the identical expression the band's *name*
        /// is built from — so the value that files a measurement and the number
        /// printed in the counter it lands in are the same float, bit for bit. A
        /// division would round independently of the name, and a 0.25 share would
        /// then be one band off its own label roughly half the time the bands were
        /// retuned. With four to seven bands the scan costs nothing.
        /// </para>
        /// <para>
        /// Filing a negative or NaN measurement at the bottom rather than
        /// quarantining it is a deliberate trade: those values are impossible for
        /// every §13 measurement (durations, counts and shares are all accumulated
        /// from non-negative deltas), and keeping the family's sum equal to the
        /// number of observations matters more than isolating a bug that has
        /// already happened upstream.
        /// </para>
        /// </summary>
        public static int UniformBucketIndex(float value, float bucketWidth, int bucketCount)
        {
            if (bucketCount <= 1 || !(bucketWidth > 0f))
            {
                return 0;
            }

            for (var i = bucketCount - 1; i >= 1; i--)
            {
                if (value >= bucketWidth * i)
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// A non-negative count from a float measurement, for histograms whose
        /// domain is integral. NaN and negatives become zero, and an absurd
        /// magnitude saturates instead of wrapping through the int cast.
        /// </summary>
        public static int ToCount(float value)
        {
            if (!(value > 0f))
            {
                return 0;
            }

            if (value >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)MathF.Round(value);
        }

        private static string[] BuildUniformBands(string prefix, float width, int count, float scale, string unit)
        {
            var names = new string[count < 1 ? 1 : count];
            for (var i = 0; i < names.Length; i++)
            {
                var low = DisplayBound(width * i, scale);
                if (i == names.Length - 1)
                {
                    names[i] = prefix + "_" + Digits(low) + unit + OpenBandSuffix;
                }
                else
                {
                    var high = DisplayBound(width * (i + 1), scale);
                    names[i] = prefix + "_" + Digits(low) + "_" + Digits(high) + unit;
                }
            }

            return names;
        }

        private static string[] BuildRoundTripBands()
        {
            // One band per exact count 0..ExpectedRoundTripsMax, then the tail.
            var names = new string[GameConstants.ExpectedRoundTripsMax + 2];
            for (var i = 0; i < names.Length - 1; i++)
            {
                names[i] = RoundTripsHistogram + "_" + Digits(i);
            }

            names[names.Length - 1] =
                RoundTripsHistogram + "_" + Digits(GameConstants.ExpectedRoundTripsMax + 1) + UnitNone + OpenBandSuffix;
            return names;
        }

        private static string[] BuildAllCounters()
        {
            var all = new List<string>();
            all.AddRange(_aggroDurationBuckets);
            all.AddRange(_roundTripsBuckets);
            all.AddRange(_backpedalShareBuckets);
            all.AddRange(_rolePickCounters);
            all.AddRange(_purchaseCounters);
            all.AddRange(_outcomeCounters);
            return all.ToArray();
        }

        /// <summary>The integer written into a band name. Bands whose scaled bounds are not whole numbers would produce a lossy name; TelemetryTests pins that they are.</summary>
        private static int DisplayBound(float value, float scale) => (int)MathF.Round(value * scale);

        /// <summary>Invariant-culture digits. A stat name must be byte-identical on every machine that reports it.</summary>
        private static string Digits(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// §13's one hard privacy rule, enforced in code: "익명 세션 ID만 쓴다.
    /// 개인정보는 수집하지 않는다."
    /// <para>
    /// This lives beside the bucket scheme because the two together are the whole
    /// definition of what telemetry may contain: <see cref="TelemetryBuckets"/>
    /// says what is recorded, this says what may never be.
    /// </para>
    /// <para>
    /// <b>Whitelist, not blacklist.</b> A blacklist of things that "look like an
    /// identifier" cannot be finished — Steam IDs, SteamID3 brackets, profile
    /// URLs, e-mail addresses, IPs, Windows user paths, Discord snowflakes and
    /// plain nicknames are all identifiers, and the next one has not been invented
    /// yet. But the only session id the game ever legitimately reports is the one
    /// <see cref="NewSessionId"/> produces, so the shape of that token is the
    /// entire specification: <see cref="SessionIdLength"/> lowercase hex
    /// characters, nothing else. Everything on that list fails the shape without
    /// being enumerated.
    /// </para>
    /// <para>
    /// <b>Scrub, do not throw.</b> §13's telemetry is fire-and-forget — a summary
    /// sent at match end — so a bad session id must not be able to take a match
    /// down with it. The value is replaced by <see cref="RedactedSessionId"/>,
    /// which is also useful data: a spike of redacted sessions says a call site is
    /// passing something it should not, and every redacted row collapses onto the
    /// same string, so nothing is left to correlate on.
    /// </para>
    /// </summary>
    public static class TelemetryPrivacy
    {
        /// <summary>
        /// Characters in a legal session id. 64 bits is far more than §13 needs to
        /// tell two concurrent sessions apart, and it is short enough to paste into
        /// a bug report next to the seed.
        /// </summary>
        public const int SessionIdLength = 16;

        /// <summary>
        /// What a rejected session id becomes. Deliberately not empty and not null:
        /// the summary still says "there was a session", and the sink never has to
        /// branch on a missing field.
        /// </summary>
        public const string RedactedSessionId = "redacted";

        private const string HexDigits = "0123456789abcdef";

        /// <summary>
        /// True when <paramref name="candidate"/> has the exact shape
        /// <see cref="NewSessionId"/> produces. Lowercase only, because accepting
        /// mixed case would accept a hexadecimal-looking hash of something
        /// personal.
        /// </summary>
        public static bool IsAnonymousSessionId(string? candidate)
        {
            if (candidate == null || candidate.Length != SessionIdLength)
            {
                return false;
            }

            for (var i = 0; i < candidate.Length; i++)
            {
                var c = candidate[i];
                var isDigit = c >= '0' && c <= '9';
                var isLowerHex = c >= 'a' && c <= 'f';
                if (!isDigit && !isLowerHex)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// <paramref name="sessionId"/> if it is anonymous by shape, otherwise
        /// <see cref="RedactedSessionId"/>. Apply at every boundary where a
        /// summary is accepted from a caller, not once at the source — a
        /// <see cref="MatchSummary"/> is a plain struct anyone can assemble by
        /// hand, so the last gate before the data leaves the process has to be the
        /// one that holds.
        /// </summary>
        public static string Sanitize(string? sessionId)
        {
            // The ! is safe and unavoidable: IsAnonymousSessionId returns false for
            // null on its first line, so the true branch cannot be reached with
            // null — the compiler simply cannot see through the call.
            return IsAnonymousSessionId(sessionId) ? sessionId! : RedactedSessionId;
        }

        /// <summary>
        /// A fresh anonymous session id drawn from <paramref name="random"/>.
        /// <para>
        /// Takes an <see cref="IRandomSource"/> rather than reaching for a GUID so
        /// the core stays deterministic: replaying a seed reproduces the whole
        /// match including the id it reported, which is what makes a player's
        /// balance report reproducible here. Anonymity is unaffected — the seed
        /// identifies a match, never a person.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
        public static string NewSessionId(IRandomSource random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var chars = new char[SessionIdLength];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = HexDigits[random.NextInt(0, HexDigits.Length)];
            }

            return new string(chars);
        }
    }
}
