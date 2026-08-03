using HorrorGame.Core.Threat;

namespace HorrorGame.Core.Match
{
    /// <summary>
    /// The race clock — §07's "시계 하나".
    /// <para>
    /// It answers two questions and no others. <b>How long has this runner been
    /// going?</b> — every entry in <c>RaceState</c> is stamped with
    /// <see cref="ElapsedSeconds"/>, so a finishing place, a 투하구 descent and a
    /// 탈락 are all timed off this one accumulator; two clocks would let the
    /// standings disagree with themselves. <b>How fast is the creature right now?</b>
    /// — <see cref="Tier"/> is §07's table, and the row it returns is what makes the
    /// eighth storey a different building from the first.
    /// </para>
    /// <para>
    /// <see cref="Tick"/> advances time no matter what the runner is doing. There is
    /// no pause, and adding one would delete §07: a race whose clock can be stopped
    /// has no reason for anybody to hurry.
    /// </para>
    /// <para>
    /// Pull, not push: nothing here is a C# event. A Unity <c>MonoBehaviour</c>
    /// subscribing to a core object outlives its scene and leaks, and handler order
    /// would become part of the observable behaviour a seeded replay has to
    /// reproduce. The host polls <see cref="TryDequeueTierAdvance"/> from its
    /// fixed-step loop instead, which is deterministic and cannot leak.
    /// </para>
    /// <para>
    /// <b>DELETED with the co-operative game</b> — the whole 지상/지하 half of this
    /// class, because the race has no 지상. There is no vehicle to come back to, no
    /// shopping trip to price and no 숨 돌리기: a runner starts on the rim of B1 and
    /// the only way out is down. So <c>SetTeamOnSurface</c>, <c>IsTeamOnSurface</c>,
    /// <c>SurfaceSeconds</c>, <c>BasementSeconds</c> and <c>SurfacingCount</c> went
    /// (§03's 2~5 round trips are not a thing that happens), with them
    /// <c>MonsterResetPending</c> / <c>ConsumeMonsterReset</c> (§03's 부분 리셋 was
    /// paid for by surfacing, and nobody surfaces), and finally
    /// <c>TeamOwnsPocketWatch</c> / <c>SetPocketWatchOwned</c> / <c>IsTimeReadable</c>
    /// / <c>ReadableElapsedSeconds</c> / <c>ReadableNightPhase</c> — §07's
    /// 「시각은 지상에서만 알 수 있다」 was a restriction the 회중시계 was sold to lift,
    /// and §08's shop is gone along with the currency that bought it. Race time is
    /// simply shown; <c>RaceHud</c> has drawn it from <see cref="ElapsedSeconds"/> all
    /// along. <c>ThreatScalar</c> and <c>TierProgress</c> went too: they only
    /// forwarded to <see cref="ThreatCurve"/>, and every caller that wanted the
    /// continuous reading already called <c>ThreatCurve</c> itself.
    /// </para>
    /// </summary>
    public sealed class MatchClock
    {
        /// <summary>
        /// Elapsed time, accumulated in double.
        /// <para>
        /// The public API is float, but the accumulator is not: a 35-minute race at
        /// <see cref="GameConstants.FixedStep"/> is 105,000 additions, and in float
        /// those drift by the better part of a second — the boundary between 심야 and
        /// 새벽 would land somewhere other than 24:00, and two runners finishing a
        /// second apart would be separated by the arithmetic rather than by the race.
        /// </para>
        /// </summary>
        private double _elapsedSeconds;

        /// <summary>Last tier index handed out by <see cref="TryDequeueTierAdvance"/>.</summary>
        private int _observedTierIndex;

        /// <summary>
        /// Seconds since the race began. Never decreases, never pauses. §07.
        /// <para>
        /// Host-authoritative truth, and the single source every timestamp in
        /// <c>RaceState</c> is taken from.
        /// </para>
        /// </summary>
        public float ElapsedSeconds => (float)_elapsedSeconds;

        /// <summary>The current row of §07's table — how fast the creature is, and whether it knows the way down.</summary>
        public ThreatTier Tier => ThreatCurve.At(ElapsedSeconds);

        /// <summary>Index of the current row. §07.</summary>
        public int TierIndex => ThreatCurve.TierIndexAt(ElapsedSeconds);

        /// <summary>
        /// Advances the race by <paramref name="deltaSeconds"/>. Driven by the host at
        /// <see cref="GameConstants.FixedStep"/>; never reads a clock itself, so a
        /// seeded replay reproduces the race exactly.
        /// <para>
        /// Zero, negative, NaN and infinite deltas are ignored rather than clamped.
        /// Time in this game only moves forward, and a NaN delta that poisoned the
        /// accumulator would take §07's entire threat curve with it — every tier query
        /// downstream would resolve to 초저녁 for the rest of the race, and the
        /// creature would never speed up again.
        /// </para>
        /// <para>
        /// A large delta from a frame spike is honoured in full: the night does not owe
        /// the runner the seconds their machine dropped. Tier crossings are still
        /// reported one at a time, in order, so a spike cannot make a tier go
        /// unannounced — see <see cref="TryDequeueTierAdvance"/>.
        /// </para>
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            // NaN fails every relational test, so this single guard rejects NaN, zero
            // and negatives together.
            if (!(deltaSeconds > 0f) || float.IsPositiveInfinity(deltaSeconds))
            {
                return;
            }

            _elapsedSeconds += deltaSeconds;
        }

        /// <summary>
        /// Takes the next tier the race has crossed into but not yet reported, in
        /// order. Returns false once the caller has caught up.
        /// <para>
        /// One at a time, oldest first, because a 20-second frame spike across a
        /// boundary must not swallow a tier: the audio stinger for 심야 has to play
        /// before the one for 새벽, and telemetry counting "reached 심야" cannot miss
        /// it. Tier 0 is never reported — the race starts there.
        /// </para>
        /// <para>
        /// Poll until false, once per fixed step. A caller that never polls costs
        /// nothing; the cursor simply stays behind.
        /// </para>
        /// </summary>
        public bool TryDequeueTierAdvance(out ThreatTier tier)
        {
            var current = TierIndex;
            if (_observedTierIndex >= current)
            {
                tier = ThreatCurve.Tier(current);
                return false;
            }

            _observedTierIndex++;
            tier = ThreatCurve.Tier(_observedTierIndex);
            return true;
        }
    }
}
