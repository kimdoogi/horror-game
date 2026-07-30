namespace HorrorGame.Core.Telemetry
{
    /// <summary>
    /// Where balance measurements go. §13.
    /// <para>
    /// §13 settled on bucket counters rather than a database, because the data is
    /// append-only and the questions are all distributions: "how long does aggro
    /// last", "how often is S held". Steam Stats can store and globally aggregate
    /// counters for free, so a histogram falls out of nothing but increments.
    /// </para>
    /// <para>
    /// Implementations must be safe to call from gameplay code every frame and
    /// must never block. The Unity implementation batches and flushes at match end.
    /// Only anonymous session identifiers are ever recorded.
    /// </para>
    /// </summary>
    public interface ITelemetrySink
    {
        /// <summary>Adds <paramref name="amount"/> to a named counter.</summary>
        void Increment(string counter, int amount = 1);

        /// <summary>
        /// Files <paramref name="value"/> into the right bucket of a histogram.
        /// The sink appends the bucket suffix, so callers pass the raw measurement.
        /// </summary>
        void Observe(string histogram, float value);

        /// <summary>
        /// Records a match-end summary. §13 stage 1 keeps this to counters; stages
        /// 2 and 3 attach the detail payload.
        /// </summary>
        void RecordMatchSummary(MatchSummary summary);

        /// <summary>Pushes everything buffered to the backing store. Called at match end and on quit.</summary>
        void Flush();
    }
}
