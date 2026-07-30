#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core.Telemetry;
using UnityEngine;

namespace HorrorGame.Steam
{
    /// <summary>
    /// §13 텔레메트리 1단계, wired up: the core's <see cref="ITelemetrySink"/> on one
    /// side, Steam Stats counters on the other, and no infrastructure in between.
    /// <para>
    /// §13's plan is a set of named integer counters that Steam stores and aggregates,
    /// so that <c>aggro_duration_0_5s</c> and its siblings become a histogram — 히스토그램이
    /// Steam Stats만으로 나온다. The core already decides every counter name and every
    /// band (<see cref="TelemetryBuckets"/>); this class does the one thing the core
    /// cannot, which is talk to the platform.
    /// </para>
    /// <para>
    /// Counters are buffered during the match and applied on <see cref="Flush"/>.
    /// Steam's only increment primitive is read-modify-write against a locally cached
    /// value, and <c>MatchRecorder</c> increments during play — including inside a chase.
    /// Buffering keeps the platform out of the frame loop entirely, and §13's data is a
    /// per-match summary anyway.
    /// </para>
    /// <para>
    /// This is also the seam that catches the mistake §13's approach is most exposed to.
    /// A Steam stat has to be declared on the partner site before it accepts writes, and
    /// an undeclared one fails <em>silently</em> — the balance data for a whole release
    /// would simply not exist. So every counter name is checked against
    /// <see cref="TelemetryBuckets.IsKnownCounter"/> and an unrecognised one is logged
    /// once, loudly. <c>Horror/Steam/Print Stats Provisioning List</c> prints what to
    /// declare.
    /// </para>
    /// </summary>
    public sealed class SteamStatsTelemetrySink : ITelemetrySink
    {
        private readonly IStatsService _stats;
        private readonly Dictionary<string, int> _pending = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnedNames = new HashSet<string>(StringComparer.Ordinal);

        private bool _storeOutstanding;

        /// <summary>
        /// Builds the sink over a stats service, defaulting to the running one. Works
        /// against the offline backend too, where the counters accumulate in memory and go
        /// nowhere — which is what §14 steps 1–3 want, since there is no App ID yet.
        /// </summary>
        public SteamStatsTelemetrySink(IStatsService? stats = null)
        {
            _stats = stats ?? SteamServices.Current.Stats;
        }

        /// <summary>Counters buffered and not yet applied.</summary>
        public int PendingCounters => _pending.Count;

        /// <summary>Increments rejected for being non-positive. A Steam stat that goes down cannot be aggregated globally, so a decrement is always a call-site bug.</summary>
        public int RejectedIncrements { get; private set; }

        /// <summary>Counter names Steam is not expected to know. Should be zero; anything else is data being lost.</summary>
        public int UnknownCounterNames => _warnedNames.Count;

        /// <summary>
        /// The last match summary handed over. §13 stage 1 has nowhere to put a summary —
        /// Steam Stats holds counters, not records — so it is kept for the debug overlay
        /// and for stage 2, which §13 describes as a JSONL line in object storage.
        /// </summary>
        public MatchSummary? LastSummary { get; private set; }

        /// <inheritdoc />
        public void Increment(string counter, int amount = 1)
        {
            if (amount <= 0)
            {
                RejectedIncrements++;
                return;
            }

            var key = string.IsNullOrWhiteSpace(counter) ? TelemetryBuckets.InvalidCounterName : counter;
            WarnIfUnknown(key);

            _pending.TryGetValue(key, out var current);
            _pending[key] = current > int.MaxValue - amount ? int.MaxValue : current + amount;
        }

        /// <summary>
        /// Files a measurement into its band. The band arithmetic belongs to the core —
        /// ARCHITECTURE §3 couples systems through primitives, and a second implementation
        /// of §13's bucket geometry here is how the counter a value lands in stops
        /// matching the counter's own name.
        /// </summary>
        public void Observe(string histogram, float value)
        {
            Increment(TelemetryBuckets.Bucket(histogram, value));
        }

        /// <inheritdoc />
        public void RecordMatchSummary(MatchSummary summary)
        {
            LastSummary = summary;
        }

        /// <summary>
        /// Applies buffered counters and uploads. Called at match end and on quit.
        /// <para>
        /// A counter is dropped from the buffer as soon as Steam accepts it, even if the
        /// upload then fails, because Steam's accepted value already includes the
        /// increment — retrying the add would double-count it. Only the upload is retried,
        /// which is why the next flush stores even with an empty buffer.
        /// </para>
        /// </summary>
        public void Flush()
        {
            if (_pending.Count == 0 && !_storeOutstanding)
            {
                return;
            }

            List<string>? applied = null;

            foreach (var pair in _pending)
            {
                if (_stats.AddToStat(pair.Key, pair.Value))
                {
                    applied ??= new List<string>(_pending.Count);
                    applied.Add(pair.Key);
                }
            }

            if (applied != null)
            {
                for (var i = 0; i < applied.Count; i++)
                {
                    _pending.Remove(applied[i]);
                }
            }

            _storeOutstanding = !_stats.Store();

            if (_storeOutstanding && _stats.IsAvailable)
            {
                Debug.LogWarning("[Steam] Stats upload failed; " + _pending.Count
                    + " counters still buffered. Retrying on the next flush.");
            }
        }

        private void WarnIfUnknown(string counter)
        {
            if (TelemetryBuckets.IsKnownCounter(counter) || !_warnedNames.Add(counter))
            {
                return;
            }

            Debug.LogWarning("[Steam] Counter '" + counter
                + "' is not one of §13's stage-1 names. Steam discards writes to a stat that was never "
                + "declared on the partner site, silently — declare it or fix the name, "
                + "or this match's data does not exist. See Horror/Steam/Print Stats Provisioning List.");
        }
    }
}
