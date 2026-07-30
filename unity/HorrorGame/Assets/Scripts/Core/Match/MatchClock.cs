using HorrorGame.Core.Threat;

namespace HorrorGame.Core.Match
{
    /// <summary>
    /// The match clock — §07's "시계 하나", and the only currency in the game.
    /// <para>
    /// §07 chose a clock over a descent counter and listed what that buys:
    /// surfacing stops being free, shopping becomes a decision, and pressure
    /// becomes continuous. All three follow from one property of this class:
    /// <see cref="Tick"/> advances time no matter where the team is. There is no
    /// pause, and adding one would delete §07.
    /// </para>
    /// <para>
    /// The partial reset of §03 lives here too. Surfacing clears the monster's
    /// chase state, its position and its aggro; it does not clear the clock, and
    /// the clock keeps running while the team stands at the vehicle arguing about
    /// what to buy — "나가는 것은 숨 돌리기이지 리셋이 아니다." This class does not
    /// know what a monster is, so it raises the request
    /// (<see cref="MonsterResetPending"/>) and the session layer performs it.
    /// </para>
    /// <para>
    /// Pull, not push: nothing here is a C# event. A Unity <c>MonoBehaviour</c>
    /// subscribing to a core object outlives its scene and leaks, and handler
    /// order would become part of the observable behaviour a seeded replay has to
    /// reproduce. The host polls <see cref="TryDequeueTierAdvance"/> and
    /// <see cref="ConsumeMonsterReset"/> from its fixed-step loop instead, which is
    /// deterministic and cannot leak.
    /// </para>
    /// </summary>
    public sealed class MatchClock
    {
        /// <summary>
        /// Elapsed time, accumulated in double.
        /// <para>
        /// The public API is float, but the accumulator is not: a 35-minute match at
        /// <see cref="GameConstants.FixedStep"/> is 105,000 additions, and in float
        /// those drift by the better part of a second — the boundary between 심야
        /// and 새벽 would land somewhere other than 24:00. §07 makes this value the
        /// currency of the whole game, and a currency must not leak.
        /// </para>
        /// </summary>
        private double _elapsedSeconds;

        /// <summary>Of <see cref="_elapsedSeconds"/>, how much was spent on the surface. Same reason for double.</summary>
        private double _surfaceSeconds;

        /// <summary>Last tier index handed out by <see cref="TryDequeueTierAdvance"/>.</summary>
        private int _observedTierIndex;

        private bool _teamOnSurface;
        private bool _monsterResetPending;
        private bool _pocketWatchOwned;
        private int _surfacingCount;

        /// <summary>
        /// Starts a clock at zero. §01 opens with the team above ground at the
        /// vehicle, so <paramref name="startOnSurface"/> defaults to true — which
        /// also means the opening 초저녁 reading is legible without a 회중시계, and
        /// that the first descent is what arms the first partial reset.
        /// </summary>
        public MatchClock(bool startOnSurface = true)
        {
            _teamOnSurface = startOnSurface;
        }

        /// <summary>
        /// Seconds since the match began. Never decreases, never pauses. §07.
        /// <para>
        /// This is host-authoritative truth and what every rule reads. It is *not*
        /// what the HUD may show — see <see cref="ReadableElapsedSeconds"/>.
        /// </para>
        /// </summary>
        public float ElapsedSeconds => (float)_elapsedSeconds;

        /// <summary>The current row of §07's table.</summary>
        public ThreatTier Tier => ThreatCurve.At(ElapsedSeconds);

        /// <summary>Index of the current row. §07.</summary>
        public int TierIndex => ThreatCurve.TierIndexAt(ElapsedSeconds);

        /// <summary>
        /// §07's continuous 0–1 pressure reading, for music and ambience. A rule
        /// that needs a number must read <see cref="Tier"/>; see
        /// <see cref="ThreatCurve"/> on why both exist.
        /// </summary>
        public float ThreatScalar => ThreatCurve.ThreatScalar(ElapsedSeconds);

        /// <summary>Fraction of the current tier elapsed, 0–1. Presentation only.</summary>
        public float TierProgress => ThreatCurve.TierProgress(ElapsedSeconds);

        /// <summary>Whether the team counts as being above ground. See <see cref="SetTeamOnSurface"/>.</summary>
        public bool IsTeamOnSurface => _teamOnSurface;

        /// <summary>
        /// Seconds the team has spent above ground. §07 claims surfacing has a
        /// price; this is the measurement that proves it, and the denominator for
        /// "how much of the night did shopping cost us".
        /// </summary>
        public float SurfaceSeconds => (float)_surfaceSeconds;

        /// <summary>Seconds spent below ground. Together with <see cref="SurfaceSeconds"/> this accounts for the whole match.</summary>
        public float BasementSeconds => (float)(_elapsedSeconds - _surfaceSeconds);

        /// <summary>
        /// Times the team has come back up. §03 expects 2–5 round trips per match,
        /// so this is the value telemetry compares against
        /// <see cref="GameConstants.ExpectedRoundTripsMin"/>.
        /// </summary>
        public int SurfacingCount => _surfacingCount;

        /// <summary>Whether the party is carrying a 회중시계. §08's 정보 item. Set by the economy.</summary>
        public bool TeamOwnsPocketWatch => _pocketWatchOwned;

        /// <summary>
        /// Whether the time can be read at all right now. §07: "시각은 지상에서만 알
        /// 수 있다" — above ground, or with the 회중시계 that exists precisely to lift
        /// that restriction.
        /// <para>
        /// This is a *rule*, not a UI convenience. Being unable to answer "지금 몇
        /// 시쯤이야?" without walking out is one of §07's reasons for the round trip
        /// and §08's reason for selling the watch.
        /// </para>
        /// </summary>
        public bool IsTimeReadable => _teamOnSurface || _pocketWatchOwned;

        /// <summary>
        /// The time as the players are allowed to know it, or null when nobody can
        /// read the clock. §07.
        /// <para>
        /// Nullable rather than a sentinel on purpose. A HUD handed 0 seconds would
        /// happily draw 초저녁 at 30 minutes, and §03's whole failure mode is
        /// players acting confidently on remembered-wrong information — the engine
        /// must not manufacture more of it. Null forces the caller to decide what
        /// "unknown" looks like.
        /// </para>
        /// </summary>
        public float? ReadableElapsedSeconds => IsTimeReadable ? (float?)ElapsedSeconds : null;

        /// <summary>
        /// The 시각 the players may be told, or null when the clock is unreadable.
        /// §07 — this, not a stopwatch, is what a player actually asks for.
        /// </summary>
        public NightPhase? ReadableNightPhase => IsTimeReadable ? (NightPhase?)Tier.Phase : null;

        /// <summary>
        /// True when a surfacing has happened that the session has not yet acted
        /// on. §03's 부분 리셋: the monster's chase state, position and aggro are
        /// cleared — the clock is not.
        /// <para>
        /// Stays set until <see cref="ConsumeMonsterReset"/> takes it, including if
        /// the team dives straight back down first. The reset is owed for having
        /// left; withdrawing it because they were quick would hand a fast dive a
        /// free monster-relocation.
        /// </para>
        /// </summary>
        public bool MonsterResetPending => _monsterResetPending;

        /// <summary>
        /// Advances the match by <paramref name="deltaSeconds"/>. Driven by the host
        /// at <see cref="GameConstants.FixedStep"/>; never reads a clock itself, so
        /// a seeded replay reproduces the match exactly.
        /// <para>
        /// Zero, negative, NaN and infinite deltas are ignored rather than clamped.
        /// Time in this game only moves forward, and a NaN delta that poisoned the
        /// accumulator would take §07's entire threat curve with it — every tier
        /// query downstream would resolve to 초저녁 for the rest of the match.
        /// </para>
        /// <para>
        /// A large delta from a frame spike is honoured in full: the night does not
        /// owe the team the seconds their machine dropped. Tier crossings are still
        /// reported one at a time, in order, so a spike cannot make a tier go
        /// unannounced — see <see cref="TryDequeueTierAdvance"/>.
        /// </para>
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            // NaN fails every relational test, so this single guard rejects NaN,
            // zero and negatives together.
            if (!(deltaSeconds > 0f) || float.IsPositiveInfinity(deltaSeconds))
            {
                return;
            }

            _elapsedSeconds += deltaSeconds;

            if (_teamOnSurface)
            {
                _surfaceSeconds += deltaSeconds;
            }
        }

        /// <summary>
        /// Reports where the team is. Pass true only when nobody is left below.
        /// <para>
        /// Team-level on purpose. §03 says leaving resets the monster, and the
        /// document never asks what happens when one player leaves while three are
        /// still down there — but if a single player stepping outside cleared aggro,
        /// standing in the doorway and tapping the exit would be a free, infinitely
        /// repeatable "monster, go away" with no cost at all, which is the opposite
        /// of every trade in §10. The whole party has to be out, which is also the
        /// only reading under which "숨 돌리기" describes anything.
        /// </para>
        /// <para>
        /// The rising edge — basement to surface — arms the partial reset and counts
        /// a round trip. Repeated calls with the same value do nothing, so a host
        /// that pushes state every frame does not accumulate resets.
        /// </para>
        /// </summary>
        public void SetTeamOnSurface(bool onSurface)
        {
            if (onSurface == _teamOnSurface)
            {
                return;
            }

            _teamOnSurface = onSurface;

            if (!onSurface)
            {
                return;
            }

            _surfacingCount++;
            _monsterResetPending = true;
        }

        /// <summary>
        /// Reports whether the party holds a 회중시계 (§08). The economy owns the
        /// item; this class only needs the boolean, which is the seam
        /// ARCHITECTURE.md asks for — the clock never learns what an inventory is.
        /// <para>
        /// Team-level, matching §08's 공용 지갑: the watch is bought with shared
        /// credits and its information is shared by voice the moment it is read.
        /// Which player is physically holding it is a HUD question, and the session
        /// layer knows the holder.
        /// </para>
        /// </summary>
        public void SetPocketWatchOwned(bool owned)
        {
            _pocketWatchOwned = owned;
        }

        /// <summary>
        /// Takes the pending partial reset, if there is one. Returns true exactly
        /// once per surfacing.
        /// <para>
        /// On true the session clears the monster's chase state, its position and
        /// its aggro (§03) and leaves this clock alone. Consuming is what makes the
        /// reset happen once instead of every frame the team spends shopping.
        /// </para>
        /// </summary>
        public bool ConsumeMonsterReset()
        {
            if (!_monsterResetPending)
            {
                return false;
            }

            _monsterResetPending = false;
            return true;
        }

        /// <summary>
        /// Takes the next tier the match has crossed into but not yet reported, in
        /// order. Returns false once the caller has caught up.
        /// <para>
        /// One at a time, oldest first, because a 20-second frame spike across a
        /// boundary must not swallow a tier: the audio stinger for 심야 has to play
        /// before the one for 새벽, and telemetry counting "reached 심야" cannot miss
        /// it. Tier 0 is never reported — the match starts there.
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
