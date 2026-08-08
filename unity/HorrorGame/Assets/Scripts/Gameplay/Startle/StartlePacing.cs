#nullable enable

using HorrorGame.Core.Race;

namespace HorrorGame.Gameplay.Startle
{
    /// <summary>
    /// When a 깜짝 is allowed to fire, for one local player. Engine-free on purpose, so
    /// the PlayMode test can advance its clock by the exact seconds a rule names instead
    /// of waiting them out in real time.
    /// <para>
    /// <b>Everything here is per-player and only per-player.</b> The pivot's made
    /// decision (b): a startle is triggered and rendered on the player's own client with
    /// zero network traffic, so "per player" is simply "this instance" — there is one of
    /// these per <see cref="StartleDirector"/> and one director per client. Nothing in
    /// this class is host state and nothing about it may ever become host state.
    /// </para>
    /// <para>
    /// <b>And nothing here reaches the creature.</b> Decision (a): a 깜짝 never calls
    /// <c>MonsterAgent.ReportSound</c> and never informs §06's creature. §12 makes sound
    /// the map, so a placed noise is a forged footstep dropped by the only thing that can
    /// see the whole building — the exact reason the pivot deleted the last system that
    /// did this (GameConstants.cs's §09 block records it). This class owns no reference
    /// that could break that rule, which is the strongest form of keeping it.
    /// </para>
    /// </summary>
    public sealed class StartlePacing
    {
        /// <summary>
        /// Seconds after match start before any 깜짝 may fire — match start literally:
        /// the director only advances this clock while <c>MatchDirector.IsRunning</c>,
        /// so the grace measures race time from <c>BeginMatch</c>, not scene time from
        /// load (the lobby can sit on a loaded scene far longer than any grace). Only
        /// a scene with no MatchDirector at all falls back to the first startle frame,
        /// and this grace is what absorbs that gap — the fallback's documented cost.
        /// <para>
        /// Two-thirds of §01's fast-end storey time. §12-D's own arithmetic gives a match
        /// 12~20 minutes over <see cref="RaceState.Storeys"/> = 8 storeys, so the fast
        /// end gives a storey 12 × 60 ÷ 8 = 90 s. The grace must do two things at once:
        /// outlast the start-line bunching — §11 puts up to twenty runners on B1's one
        /// rim, and the race start is the only moment the design <em>guarantees</em> a
        /// crowd, which is the one situation decision (b)'s accepted per-client
        /// inconsistency would be naked (two runners shoulder to shoulder, one watching a
        /// cabinet leaf the other cannot see) — and still expire before a mid-pace runner
        /// leaves B1, or B1's markers are dead weight in every match. 60 s is two-thirds
        /// of the fast storey: the field has strung out, and even the 20-minute end still
        /// has 90 s of B1 left in which its markers are live.
        /// </para>
        /// </summary>
        public const float GraceSeconds = 12f * 60f / RaceState.Storeys * 2f / 3f;

        /// <summary>
        /// Seconds between two 깜짝 for the same player.
        /// <para>
        /// §01's fast-end storey time, exactly: 12 minutes ÷ <see cref="RaceState.Storeys"/>
        /// = 90 s. A leader descending at the fast end of §01's 12~20 minute band spends
        /// 90 s a storey, so this cooldown admits at most about one startle per storey
        /// for the fastest player there is; a slower descent cannot exceed that either,
        /// because every trigger fires once and a storey only carries
        /// <c>MapSceneBuilder</c>'s two. "About one a storey" is the dose: §16's horror
        /// budget is spent by the creature, and a corridor that springs twice a minute is
        /// a funhouse, which is the opposite of a basement.
        /// </para>
        /// </summary>
        public const float CooldownSeconds = 12f * 60f / RaceState.Storeys;

        /// <summary>
        /// Most 깜짝 one player is dealt in one match.
        /// <para>
        /// <see cref="RaceState.Storeys"/> − Storeys ÷ 4 = 8 − 2 = 6 — the same
        /// quarter-proportion <c>MapSceneBuilder.GunBandInset</c> derives, applied from
        /// the other end: the cooldown already limits the take to about one per storey,
        /// and this cap keeps roughly the last quarter of the tower's worth of them
        /// unspent. By B7~B8 §07 has the creature at its 새벽/동트기 전 speeds and §02's
        /// finish is in reach; the endgame's fear is the real thing at its worst, and a
        /// scripted fright beside it would be noise in both senses.
        /// </para>
        /// </summary>
        public const int CapPerMatch = RaceState.Storeys - (RaceState.Storeys / 4);

        private float _elapsed;
        private float _lastFiredAt = float.NegativeInfinity;

        /// <summary>
        /// Seconds this pacing has been advanced. The director advances it only while
        /// the match runs, so in a real scene this is seconds since <c>BeginMatch</c>;
        /// a test advancing it directly is advancing match time.
        /// </summary>
        public float Elapsed => _elapsed;

        /// <summary>How many 깜짝 have fired for this player this match.</summary>
        public int FiredCount { get; private set; }

        /// <summary>Whether the one-per-match glimpse has been spent.</summary>
        public bool GlimpseFired { get; private set; }

        /// <summary>Seconds until the cooldown opens again. Zero when it is open.</summary>
        public float CooldownRemaining
        {
            get
            {
                var remaining = (_lastFiredAt + CooldownSeconds) - _elapsed;
                return remaining > 0f ? remaining : 0f;
            }
        }

        /// <summary>Moves the clock. The director feeds it <c>Time.deltaTime</c>; a test feeds it a rule's own constant.</summary>
        /// <param name="deltaSeconds">Seconds to advance. Negative values are ignored.</param>
        public void Advance(float deltaSeconds)
        {
            if (deltaSeconds > 0f)
            {
                _elapsed += deltaSeconds;
            }
        }

        /// <summary>
        /// Whether a 깜짝 may fire right now. Pure — it changes nothing, so a refused
        /// trigger stays armed and simply asks again on a later pass.
        /// </summary>
        /// <param name="glimpse">True when the asker is the one-per-match glimpse.</param>
        /// <param name="refusal">Which gate said no: grace · cooldown · cap · glimpse. Empty on yes.</param>
        public bool CanFire(bool glimpse, out string refusal)
        {
            if (_elapsed < GraceSeconds)
            {
                refusal = "grace";
                return false;
            }

            if (_elapsed - _lastFiredAt < CooldownSeconds)
            {
                refusal = "cooldown";
                return false;
            }

            if (FiredCount >= CapPerMatch)
            {
                refusal = "cap";
                return false;
            }

            if (glimpse && GlimpseFired)
            {
                refusal = "glimpse";
                return false;
            }

            refusal = string.Empty;
            return true;
        }

        /// <summary>
        /// Records that one fired. Called only after the effect actually staged — a
        /// trigger whose staging refused (no geometry ahead, no bulb in reach) has cost
        /// the player nothing and must not start the cooldown.
        /// </summary>
        /// <param name="glimpse">True when the fired one was the glimpse.</param>
        public void MarkFired(bool glimpse)
        {
            _lastFiredAt = _elapsed;
            FiredCount++;
            if (glimpse)
            {
                GlimpseFired = true;
            }
        }
    }
}
