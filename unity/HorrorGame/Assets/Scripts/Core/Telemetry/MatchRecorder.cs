using System;
using HorrorGame.Core.Match;
using HorrorGame.Core.Race;

namespace HorrorGame.Core.Telemetry
{
    /// <summary>
    /// Accumulates a <see cref="MatchSummary"/> from small incremental events, so
    /// gameplay code reports what just happened instead of assembling the struct by
    /// hand at match end. §13.
    /// <para>
    /// The struct is flat and public, which means anything could fill it in — and
    /// that is exactly the failure mode this class exists to prevent. Twenty-odd
    /// fields assembled at the last moment by whichever system happens to own the
    /// end-of-match screen is how a summary ends up with the aggro that was still
    /// running when everyone died missing from it, or with
    /// <see cref="MatchSummary.BackpedalSeconds"/> larger than
    /// <see cref="MatchSummary.TotalMovingSeconds"/>, which makes
    /// <see cref="MatchSummary.BackpedalRatio"/> a number above 1 that no reader
    /// would question. Here every invariant the derived properties depend on is
    /// maintained as the events arrive.
    /// </para>
    /// <para>
    /// <b>What is emitted when.</b> Per-event measurements go to the sink as they
    /// happen (a chase that ended, a storey arrived on), so a race that never
    /// reaches an ending still contributes what it observed. Per-match figures — the
    /// outcome, the deepest storey reached, the backpedal share —
    /// cannot exist until the match is over and are emitted once, from
    /// <see cref="Complete"/>, together with the summary and a flush.
    /// </para>
    /// <para>
    /// <b>No clock, no randomness.</b> Time arrives only as an explicit
    /// <c>deltaSeconds</c>, so a seeded replay produces a
    /// byte-identical summary — which is what makes a balance report from a player
    /// reproducible here (§13). Accumulators are <c>double</c> for the same reason
    /// <see cref="MatchClock"/>'s are: a 35-minute match is over 100,000 additions
    /// at <see cref="GameConstants.FixedStep"/>, and in float the total drifts by
    /// most of a second.
    /// </para>
    /// </summary>
    public sealed class MatchRecorder
    {
        private readonly ITelemetrySink _sink;
        private readonly int _seed;
        private readonly string _sessionId;
        private readonly string? _mapId;

        private double _durationSeconds;
        private double _totalAggroSeconds;
        private double _backpedalSeconds;
        private double _totalMovingSeconds;
        private double _currentAggroSeconds;

        private float _longestAggroSeconds;

        private int _aggroEvents;
        private int _deepestStorey;
        private int _runnersFinished;
        private int _runnersEliminated;

        private bool _aggroActive;
        private bool _completed;
        private MatchSummary _finalSummary;

        /// <summary>
        /// Starts recording a match.
        /// <para>
        /// <paramref name="seed"/> is the one field that makes everything else
        /// actionable: §13's whole diagnosis loop is "player reports a match felt
        /// wrong, we replay the seed". <paramref name="sessionId"/> is passed through
        /// <see cref="TelemetryPrivacy.Sanitize"/> here rather than trusted, because
        /// §13 permits an anonymous session id and nothing else — see
        /// <see cref="TelemetryPrivacy"/> for why the check is a shape test rather
        /// than a list of forbidden formats. Use
        /// <see cref="TelemetryPrivacy.NewSessionId"/> to make a legal one.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="sink"/> is null.</exception>
        public MatchRecorder(ITelemetrySink sink, int seed, string? sessionId = null, string? mapId = null)
        {
            // Thrown, unlike everything else here: a missing sink is a wiring
            // mistake at construction time, not a gameplay event, so failing loudly
            // costs nobody a match and silently recording into nothing would hide
            // the loss of a whole build's telemetry.
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _seed = seed;
            _sessionId = TelemetryPrivacy.Sanitize(sessionId);
            _mapId = mapId;
        }

        /// <summary>The sanitised, anonymous session id this match reports under. §13.</summary>
        public string SessionId => _sessionId;

        /// <summary>True once <see cref="Complete"/> has run. Further events are ignored.</summary>
        public bool IsComplete => _completed;

        /// <summary>Whether a chase is currently open. See <see cref="SetAggroActive"/>.</summary>
        public bool IsAggroActive => _aggroActive;

        /// <summary>
        /// Seconds the currently open chase has been running, or zero when there is
        /// none. Deliberately not folded into <see cref="Snapshot"/>: see the note
        /// there on why an unclosed chase must not reach
        /// <see cref="MatchSummary.TotalAggroSeconds"/>.
        /// </summary>
        public float CurrentAggroSeconds => (float)_currentAggroSeconds;

        /// <summary>
        /// Advances the match by <paramref name="deltaSeconds"/>, and with it any
        /// open chase. Driven by the host at
        /// <see cref="GameConstants.FixedStep"/>.
        /// <para>
        /// Zero, negative, NaN and infinite deltas are ignored rather than clamped.
        /// A NaN that reached these accumulators would make every field of the
        /// summary NaN for the rest of the match and the loss would only be
        /// noticeable once the data had already been aggregated with everyone
        /// else's.
        /// </para>
        /// <para>
        /// A huge delta from a frame spike is taken in full. There is no threshold
        /// here for it to tunnel through — chase transitions are reported by the
        /// caller, never inferred from elapsed time, and a chase's band is decided
        /// from its total when it closes — so a 20-second spike lengthens the chase
        /// it happened during instead of losing it.
        /// </para>
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (_completed || !IsUsableDelta(deltaSeconds))
            {
                return;
            }

            _durationSeconds += deltaSeconds;

            if (_aggroActive)
            {
                _currentAggroSeconds += deltaSeconds;
            }
        }

        // DELETED with §04: SetRoster(RoleId, RoleId, RoleId, RoleId) and the four
        // _role slots behind it. It existed to feed §13's 직업별 선택 카운터, whose
        // question was §11's "필수 직업이 있으면 풀이 가짜가 된다". The pivot answered
        // that question by deleting roles — 「캐릭터는 다 똑같이 생겨도되지」 — so
        // there is no spread left to measure and the four-picks-per-match invariant
        // the counters were readable through does not exist. Do not re-add without
        // re-adding roles.

        /// <summary>
        /// Opens or closes a chase. §06's 추격 state, seen from the outside.
        /// <para>
        /// Edge-triggered and idempotent, so a host that pushes the monster's state
        /// every frame does not count a chase per frame. Closing files the chase's
        /// length into §13's 어그로 지속 시간 histogram and counts one
        /// <see cref="MatchSummary.AggroEvents"/>.
        /// </para>
        /// <para>
        /// A close immediately followed by an open inside one step is two chases,
        /// and the first of them may be zero seconds long. That is correct rather
        /// than tidy: <see cref="MatchSummary.AggroEvents"/> counts acquisitions,
        /// and the monster acquiring a target and losing it in the same step is
        /// still an acquisition — §06's whole question is how easily aggro ends.
        /// </para>
        /// </summary>
        public void SetAggroActive(bool active)
        {
            if (_completed || active == _aggroActive)
            {
                return;
            }

            _aggroActive = active;

            if (active)
            {
                _currentAggroSeconds = 0d;
                return;
            }

            var closed = (float)_currentAggroSeconds;
            _currentAggroSeconds = 0d;
            FileAggroEvent(closed);
        }

        /// <summary>
        /// Records a chase whose length the caller already knows, for the simulator
        /// and for hosts that time chases themselves. Equivalent to opening a chase,
        /// ticking it and closing it.
        /// </summary>
        public void RecordAggroEvent(float durationSeconds)
        {
            if (_completed)
            {
                return;
            }

            FileAggroEvent(durationSeconds);
        }

        /// <summary>
        /// Records one player-step of movement: whether they moved at all, and
        /// whether they were holding S. §05 / §13.
        /// <para>
        /// Call once per moving player per step. The totals are therefore
        /// player-seconds, which is the right denominator for §13's question — it
        /// asks for the share of movement *time* spent backwards, and four players
        /// each backpedalling a quarter of the time is the same finding as one
        /// player doing it constantly.
        /// </para>
        /// <para>
        /// <paramref name="backward"/> forces <paramref name="moving"/>: whatever
        /// the caller's threshold for "moving" is, someone holding S is moving, and
        /// letting backward time exceed movement time would push
        /// <see cref="MatchSummary.BackpedalRatio"/> above 1 — a value no consumer
        /// of a share would think to check.
        /// </para>
        /// </summary>
        public void RecordMovement(float deltaSeconds, bool moving, bool backward)
        {
            if (_completed || !IsUsableDelta(deltaSeconds))
            {
                return;
            }

            if (!moving && !backward)
            {
                return;
            }

            _totalMovingSeconds += deltaSeconds;

            if (backward)
            {
                _backpedalSeconds += deltaSeconds;
            }
        }

        /// <summary>
        /// Records a runner arriving on a storey. §01 — going down IS the game, so
        /// how deep the field got is the measurement the whole building is judged by.
        /// <para>
        /// Takes the storey rather than counting drops, and keeps the deepest ever
        /// seen. A count of 투하구 falls would say the same thing only while nobody
        /// ever fell twice onto the same floor, and the §09 spectator flies through
        /// all eight. Out-of-range values are ignored rather than clamped: a storey
        /// index the map cannot produce is a wiring bug, and inventing a B8 arrival
        /// out of one would make the deepest band the place bugs go to hide.
        /// </para>
        /// <para>
        /// REPLACES <c>RecordRoundTrip()</c>, which counted returns to the surface.
        /// A race has no 지상 to return to and no van to carry anything back to.
        /// </para>
        /// </summary>
        public void RecordStoreyReached(int storey)
        {
            if (_completed || storey < 0 || storey >= RaceState.Storeys)
            {
                return;
            }

            if (storey > _deepestStorey)
            {
                _deepestStorey = storey;
            }
        }

        // DELETED in the 상점/전리품/단서 제거 round: RecordLootSold(int) and
        // RecordPurchase(ShopItemId, int, int), with the _creditsEarned /
        // _creditsSpent / _lootSold accumulators behind them. §13's job was to
        // answer §16-2 — "is §08's growth curve real" — by counting which trades
        // players accept. There is no currency in a race, nothing to sell and
        // nothing to buy, so the counters had no event that could ever raise them.
        // A telemetry field that no code path can increment is a zero that reads
        // like a measurement; that is the trap this deletion closes. Do not
        // re-add these without re-adding a shop, which is the thing the pivot
        // deleted.
        //
        // DELETED with them in the 하강 pivot, for the same reason:
        //   RecordClueRead(bool)        — §03's 단서 chain; nothing is read in a race.
        //   RecordBatteryUsed(int)      — §08's battery economy; the torch is free now
        //                                 and its only price is being seen.
        //   RecordObjectiveRecovered()  — §03's 목표물; there is nothing to carry out.

        /// <summary>
        /// Records one runner caught. §02 탈락 — out and unranked. Clamped at
        /// <see cref="GameConstants.RaceRunnersMax"/>; the recorder does not
        /// otherwise police §02, because a summary that quietly disagrees with the
        /// race is more useful than one this class has corrected.
        /// </summary>
        public void RecordRunnerEliminated()
        {
            if (_completed || _runnersEliminated >= GameConstants.RaceRunnersMax)
            {
                return;
            }

            _runnersEliminated++;
        }

        /// <summary>
        /// Records one runner reaching the middle of B8. §02 완주.
        /// <para>
        /// Not just the winner: §02 records a place for everybody who arrives,
        /// which is what stops second position being worth the same as last.
        /// </para>
        /// </summary>
        public void RecordRunnerFinished()
        {
            if (_completed || _runnersFinished >= GameConstants.RaceRunnersMax)
            {
                return;
            }

            _runnersFinished++;
        }

        /// <summary>
        /// The summary as it stands, without emitting anything. For a HUD, the
        /// simulator's mid-match assertions and tests.
        /// <para>
        /// Reflects closed chases only. Folding an open chase's seconds into
        /// <see cref="MatchSummary.TotalAggroSeconds"/> without also counting an
        /// event would make <see cref="MatchSummary.AverageAggroSeconds"/> — a
        /// quotient of the two — wrong in a way nobody would spot, and counting the
        /// event early would double it when the chase actually ends. The live figure
        /// is <see cref="CurrentAggroSeconds"/>.
        /// </para>
        /// <para>
        /// Before <see cref="Complete"/> the outcome reads
        /// <see cref="RacerStatus.Running"/>; afterwards this returns the same
        /// summary that was sent.
        /// </para>
        /// </summary>
        public MatchSummary Snapshot()
        {
            if (_completed)
            {
                return _finalSummary;
            }

            return Build(RacerStatus.Running);
        }

        /// <summary>
        /// Closes the match: files any chase still running, emits §13's per-match
        /// counters, hands the summary to the sink and flushes.
        /// <para>
        /// The open chase is closed first, and that ordering is the point. A 탈락
        /// happens *during* a chase, so a recorder that discarded the unclosed chase
        /// would systematically drop the longest ones — precisely the population
        /// §06's 12 m release distance has to be judged against — and the surviving
        /// data would say chases are short and easily escaped.
        /// </para>
        /// <para>
        /// Runs once. A second call returns the same summary and emits nothing:
        /// §13 sends one summary per race, and "the local runner was caught" racing
        /// "the last finisher arrived" is an ordinary way to arrive here twice. A double-counted match
        /// would bias every global average, and a Steam Stats counter cannot be
        /// decremented back.
        /// </para>
        /// </summary>
        /// <param name="outcome">
        /// How it ended, per §02. The session layer owns that decision — telemetry
        /// records the verdict rather than inferring one, so a disagreement between
        /// the outcome and the counts stays visible in the data.
        /// </param>
        public MatchSummary Complete(RacerStatus outcome)
        {
            if (_completed)
            {
                return _finalSummary;
            }

            SetAggroActive(false);

            _finalSummary = Build(outcome);
            _completed = true;

            _sink.Increment(TelemetryBuckets.Outcome(outcome));

            // DELETED with §04: four RolePick increments, one per seat. §13's
            // 직업별 선택 카운터 measured a spread that no longer exists.

            // Observe, not Increment: the sink owns the banding (ITelemetrySink), so
            // a re-banding is one edit in TelemetryBuckets rather than a hunt
            // through every call site.
            _sink.Observe(TelemetryBuckets.DeepestStoreyHistogram, _finalSummary.DeepestStorey);
            _sink.Observe(TelemetryBuckets.BackpedalShareHistogram, _finalSummary.BackpedalRatio);

            _sink.RecordMatchSummary(_finalSummary);
            _sink.Flush();

            return _finalSummary;
        }

        /// <summary>
        /// Counts a closed chase and bands its length.
        /// <para>
        /// A non-finite or negative length is counted as a zero-second chase. The
        /// event itself is real — the monster did acquire a target — but adding NaN
        /// or infinity to <see cref="MatchSummary.TotalAggroSeconds"/> would poison
        /// <see cref="MatchSummary.AverageAggroSeconds"/> and
        /// <see cref="MatchSummary.LongestAggroSeconds"/> for the whole match, and
        /// then the aggregate. One implausibly short chase costs a data point; a
        /// poisoned accumulator costs the match and is invisible.
        /// </para>
        /// </summary>
        private void FileAggroEvent(float durationSeconds)
        {
            var duration = durationSeconds;
            if (!(duration > 0f) || float.IsPositiveInfinity(duration))
            {
                duration = 0f;
            }

            _aggroEvents = AddClamped(_aggroEvents, 1);
            _totalAggroSeconds += duration;

            if (duration > _longestAggroSeconds)
            {
                _longestAggroSeconds = duration;
            }

            _sink.Observe(TelemetryBuckets.AggroDurationHistogram, duration);
        }

        private MatchSummary Build(RacerStatus outcome)
        {
            return new MatchSummary
            {
                Seed = _seed,
                SessionId = _sessionId,
                MapId = _mapId,
                Outcome = outcome,
                DurationSeconds = (float)_durationSeconds,
                DeepestStorey = _deepestStorey,
                RunnersFinished = _runnersFinished,
                RunnersEliminated = _runnersEliminated,
                AggroEvents = _aggroEvents,
                TotalAggroSeconds = (float)_totalAggroSeconds,
                LongestAggroSeconds = _longestAggroSeconds,
                BackpedalSeconds = (float)_backpedalSeconds,
                TotalMovingSeconds = (float)_totalMovingSeconds,
            };
        }

        /// <summary>
        /// One test that rejects NaN, zero, negatives and +infinity together: NaN
        /// fails every comparison, so <c>!(d &gt; 0f)</c> covers it and the other two.
        /// </summary>
        private static bool IsUsableDelta(float deltaSeconds) =>
            deltaSeconds > 0f && !float.IsPositiveInfinity(deltaSeconds);

        /// <summary>Saturating add. A wrapped count reads as a plausible small number; a stuck maximum is obviously broken.</summary>
        private static int AddClamped(int current, int amount) =>
            current > int.MaxValue - amount ? int.MaxValue : current + amount;
    }
}
