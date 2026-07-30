using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HorrorGame.Core.Telemetry
{
    /// <summary>
    /// A complete <see cref="ITelemetrySink"/> that keeps everything in memory and
    /// hands it back, for the tests and the headless simulator.
    /// <para>
    /// §13 stage 1 is a set of counters with no database behind it, which means the
    /// only way to know the counters are right is to read them back. The Steam
    /// implementation cannot be read back — Steam Stats aggregates server-side and
    /// returns nothing useful for a day — so this is the sink the balance suite and
    /// <c>horrorsim</c> assert against, and the recorder's whole contract is defined
    /// in terms of what shows up here.
    /// </para>
    /// <para>
    /// <b>Nothing is ever dropped.</b> A bad counter name, a non-positive
    /// increment, an unknown histogram: each one is counted somewhere visible
    /// rather than discarded. §13's counters are only readable as a distribution if
    /// the bands sum to the number of observations, so a silent drop turns every
    /// derived percentage subtly wrong, which is far worse than a stray diagnostic
    /// row. Nothing here throws either — telemetry must not be able to end a match.
    /// </para>
    /// <para>
    /// Single-threaded, matching the host's fixed-step loop. It is not a
    /// concurrent collection and does not pretend to be.
    /// </para>
    /// </summary>
    public sealed class InMemoryTelemetrySink : ITelemetrySink
    {
        private readonly Dictionary<string, int> _counters = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<float>> _observations =
            new Dictionary<string, List<float>>(StringComparer.Ordinal);
        private readonly List<MatchSummary> _summaries = new List<MatchSummary>();

        private static readonly ReadOnlyCollection<float> _noObservations =
            Array.AsReadOnly(Array.Empty<float>());

        private int _flushCount;
        private int _rejectedIncrements;

        /// <summary>
        /// Every counter that has been touched, with its total. Keys are the §13
        /// names from <see cref="TelemetryBuckets"/> plus any diagnostic rows.
        /// </summary>
        public IReadOnlyDictionary<string, int> Counters => _counters;

        /// <summary>
        /// Match summaries handed over, oldest first, with
        /// <see cref="MatchSummary.SessionId"/> already sanitised. §13 sends one per
        /// match, so more than one entry means a recorder was reused or a match was
        /// completed twice.
        /// </summary>
        public IReadOnlyList<MatchSummary> Summaries => _summaries;

        /// <summary>The most recent summary, or null when no match has ended yet.</summary>
        public MatchSummary? LastSummary =>
            _summaries.Count == 0 ? (MatchSummary?)null : _summaries[_summaries.Count - 1];

        /// <summary>Times <see cref="Flush"/> has been called. §13 flushes at match end and on quit, so a match should produce exactly one.</summary>
        public int FlushCount => _flushCount;

        /// <summary>
        /// Increments rejected for being non-positive. Steam Stats aggregates
        /// counters globally and cannot represent one that goes down, so a
        /// decrement is always a call-site bug; this makes it countable instead of
        /// invisible.
        /// </summary>
        public int RejectedIncrements => _rejectedIncrements;

        /// <inheritdoc />
        public void Increment(string counter, int amount = 1)
        {
            if (amount <= 0)
            {
                _rejectedIncrements++;
                return;
            }

            var key = string.IsNullOrWhiteSpace(counter) ? TelemetryBuckets.InvalidCounterName : counter;

            if (_counters.TryGetValue(key, out var current))
            {
                // Saturate rather than wrap. A wrapped counter reads as a small
                // plausible number, and a balance decision taken on it would be
                // wrong with no way to tell; a stuck maximum is obviously broken.
                _counters[key] = current > int.MaxValue - amount ? int.MaxValue : current + amount;
                return;
            }

            _counters[key] = amount;
        }

        /// <inheritdoc />
        public void Observe(string histogram, float value)
        {
            // The raw measurement is kept as well as the band. §13 ships only the
            // bands, but a test that can see the input is the only way to prove the
            // banding, and the simulator uses the raw stream to check a proposed
            // re-banding against matches already run.
            var rawKey = string.IsNullOrWhiteSpace(histogram) ? TelemetryBuckets.InvalidCounterName : histogram;
            if (!_observations.TryGetValue(rawKey, out var values))
            {
                values = new List<float>();
                _observations[rawKey] = values;
            }

            values.Add(value);

            Increment(TelemetryBuckets.Bucket(histogram, value));
        }

        /// <inheritdoc />
        public void RecordMatchSummary(MatchSummary summary)
        {
            // §13: 익명 세션 ID만 쓴다. MatchSummary is a plain struct, so anyone can
            // fill in SessionId by hand; the sink is the last thing between that
            // struct and the outside world and therefore has to be a gate, not a
            // pipe. Sanitising here means a caller that skipped MatchRecorder still
            // cannot leak a Steam ID.
            summary.SessionId = TelemetryPrivacy.Sanitize(summary.SessionId);
            _summaries.Add(summary);
        }

        /// <inheritdoc />
        public void Flush()
        {
            // Counting rather than clearing: a real sink pushes and forgets, but a
            // test asserts after the fact, so the readback has to survive the
            // flush. Whether the flush happened is itself worth asserting — §13
            // loses the whole match if it does not.
            _flushCount++;
        }

        /// <summary>Total on one counter, or zero when it was never touched.</summary>
        public int Count(string? counter)
        {
            if (counter == null)
            {
                return 0;
            }

            return _counters.TryGetValue(counter, out var value) ? value : 0;
        }

        /// <summary>
        /// The raw measurements passed to <see cref="Observe"/> for a histogram, in
        /// order. Empty when the histogram was never observed.
        /// </summary>
        public IReadOnlyList<float> Observations(string? histogram)
        {
            if (histogram != null && _observations.TryGetValue(histogram, out var values))
            {
                return values;
            }

            return _noObservations;
        }

        /// <summary>
        /// Sum over a family of counters — the totality check §13's arithmetic
        /// rests on. The bands of a histogram must sum to the number of
        /// observations; if they do not, a measurement fell outside every band and
        /// every percentage taken from that family is wrong.
        /// </summary>
        public int TotalIn(IEnumerable<string> counters)
        {
            if (counters == null)
            {
                return 0;
            }

            var total = 0;
            foreach (var name in counters)
            {
                total += Count(name);
            }

            return total;
        }

        /// <summary>
        /// Counter names that are not in <see cref="TelemetryBuckets.AllCounters"/>.
        /// A name here is either a diagnostic row or a stat that was never
        /// provisioned in Steamworks — and an unprovisioned stat is silently
        /// discarded in the shipped build, so this list should be empty.
        /// </summary>
        public IReadOnlyList<string> UnknownCounterNames()
        {
            var unknown = new List<string>();
            foreach (var pair in _counters)
            {
                if (!TelemetryBuckets.IsKnownCounter(pair.Key))
                {
                    unknown.Add(pair.Key);
                }
            }

            return new ReadOnlyCollection<string>(unknown);
        }

        /// <summary>Drops everything recorded, so one sink can serve a sweep of matches.</summary>
        public void Clear()
        {
            _counters.Clear();
            _observations.Clear();
            _summaries.Clear();
            _flushCount = 0;
            _rejectedIncrements = 0;
        }
    }
}
