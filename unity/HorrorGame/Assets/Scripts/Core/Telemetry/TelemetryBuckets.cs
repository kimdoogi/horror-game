using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using HorrorGame.Core.Race;
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
    /// <item><term>최고 도달 층</term><description>validates §12-A's gates and §01's eight storeys — how far the field actually gets.</description></item>
    /// <item><term>후진(S) 사용 시간 비율</term><description>validates §05's 65% backward multiplier.</description></item>
    /// <item><term>완주 / 탈락</term><description>§02's only two verdicts.</description></item>
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
        /// Histogram name for the deepest storey a race reached, 0–7 (B1 to B8).
        /// §01 makes descending the whole game, so this distribution is what says
        /// whether the building is the right length: piled at the shallow end and
        /// §06 or §12-A is eating the field before the race has a shape; piled at
        /// B8 and nothing the creature does matters.
        /// <para>
        /// REPLACES <c>round_trips</c>, which counted returns to a 지상 the race
        /// does not have.
        /// </para>
        /// </summary>
        public const string DeepestStoreyHistogram = "deepest_storey";

        /// <summary>
        /// Histogram name for the share of movement time spent holding S, 0–1.
        /// §13 uses it to validate §05's 65% backward multiplier: the peek dilemma
        /// is working when this stays low but never reaches zero.
        /// </summary>
        public const string BackpedalShareHistogram = "backpedal_share";

        // DELETED with §08: PurchasePrefix and the whole purchase_* counter family.
        // §13 counted which shop trades players accept; with no currency there is
        // nothing to buy and no event that could raise one of these. See
        // MatchRecorder for the matching removal.
        //
        // DELETED with §04: RolePickPrefix, RolePickUnassigned and the six
        // role_pick_* counters. They answered §11's "필수 직업이 있으면 풀이
        // 가짜가 된다" — a question the pivot answered by deleting 직업 outright.

        /// <summary>Prefix for §13's 완주 / 탈락 counters. §02.</summary>
        public const string OutcomePrefix = "outcome";

        /// <summary>§02's 완주 — reached the middle of B8 and took a place.</summary>
        public const string OutcomeFinished = OutcomePrefix + "_finished";

        /// <summary>
        /// §02's 탈락 — caught, out, unranked.
        /// <para>
        /// A distinct counter rather than a share of finishes, because §02's whole
        /// rule is that 탈락 has no place: it cannot be expressed as a rank, so it
        /// cannot be read off the finishing counter either.
        /// </para>
        /// </summary>
        public const string OutcomeEliminated = OutcomePrefix + "_eliminated";

        /// <summary>
        /// A race reported as still running. That is a caller bug, and it gets its
        /// own counter so the bug shows up as an implausible row rather than as
        /// inflated finishes.
        /// </summary>
        public const string OutcomeInProgress = OutcomePrefix + "_in_progress";

        /// <summary>An outcome value outside <see cref="RacerStatus"/>. Keeps the family total when an int is cast in from the network.</summary>
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

        /// <summary>Count bands carry no unit: §13's template gives <c>deepest_storey_3</c>.</summary>
        private const string UnitNone = "";

        /// <summary>Scale from seconds to the number written in a seconds band name.</summary>
        private const float ScaleSeconds = 1f;

        /// <summary>Scale from a 0–1 share to the number written in a percent band name.</summary>
        private const float ScalePercent = 100f;

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

        private static readonly ReadOnlyCollection<string> _deepestStoreyBuckets =
            Array.AsReadOnly(BuildStoreyBands());

        private static readonly ReadOnlyCollection<string> _outcomeCounters =
            Array.AsReadOnly(new[]
            {
                OutcomeFinished,
                OutcomeEliminated,
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

        /// <summary>
        /// The 최고 도달 층 counters: one per storey, B1 first. There is no open
        /// tail — <see cref="RaceState.Storeys"/> is the whole building, so the
        /// domain is closed and a tail band could only ever count a bug.
        /// </summary>
        public static IReadOnlyList<string> DeepestStoreyBuckets => _deepestStoreyBuckets;

        /// <summary>§13's 후진 사용 시간 비율 counters, lowest band first, open tail last.</summary>
        public static IReadOnlyList<string> BackpedalShareBuckets => _backpedalShareBuckets;

        /// <summary>§02's 완주 / 탈락, followed by the two diagnostic rows that keep the family total.</summary>
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
        /// Which band of the 최고 도달 층 histogram a race that reached
        /// <paramref name="storey"/> belongs to.
        /// <para>
        /// One band per storey, no ranges: eight is already the whole domain, and
        /// §12-A narrows the gates on every one of them, so collapsing two floors
        /// together would hide exactly the step where the field stops. Anything
        /// outside 0..<see cref="RaceState.Storeys"/>−1 is clamped into the nearest
        /// end so the family still totals the number of races — an impossible
        /// storey is a wiring bug, and it is legible as one because it lands on B1
        /// or B8 rather than in a band of its own.
        /// </para>
        /// </summary>
        public static string DeepestStorey(int storey)
        {
            if (storey <= 0)
            {
                return _deepestStoreyBuckets[0];
            }

            if (storey >= _deepestStoreyBuckets.Count)
            {
                return _deepestStoreyBuckets[_deepestStoreyBuckets.Count - 1];
            }

            return _deepestStoreyBuckets[storey];
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
        /// The §13 counter for how a race ended.
        /// <para>
        /// §02 has exactly two verdicts and they do not reduce to each other:
        /// 완주 carries a place, 탈락 carries none. <see cref="RacerStatus.Running"/>
        /// and any undefined value get diagnostic rows rather than being folded into
        /// either, so a caller bug reads as an implausible row instead of moving the
        /// finish rate.
        /// </para>
        /// </summary>
        public static string Outcome(RacerStatus outcome)
        {
            switch (outcome)
            {
                case RacerStatus.Finished:
                    return OutcomeFinished;
                case RacerStatus.Eliminated:
                    return OutcomeEliminated;
                case RacerStatus.Running:
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
                case DeepestStoreyHistogram:
                    return DeepestStorey(ToCount(value));
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

        /// <summary>
        /// One band per storey, named by the B-number a player would say out loud —
        /// storey index 0 is <c>deepest_storey_b1</c>. Reading a Steam Stats page
        /// against §12's floor plans should not need a mental −1.
        /// </summary>
        private static string[] BuildStoreyBands()
        {
            var names = new string[RaceState.Storeys];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = DeepestStoreyHistogram + "_b" + Digits(i + 1) + UnitNone;
            }

            return names;
        }

        private static string[] BuildAllCounters()
        {
            var all = new List<string>();
            all.AddRange(_aggroDurationBuckets);
            all.AddRange(_deepestStoreyBuckets);
            all.AddRange(_backpedalShareBuckets);
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
