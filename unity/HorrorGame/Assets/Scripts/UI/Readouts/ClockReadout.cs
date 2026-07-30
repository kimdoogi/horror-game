#nullable enable

using HorrorGame.Core.Match;
using HorrorGame.Core.Threat;

namespace HorrorGame.UI.Readouts
{
    /// <summary>
    /// What the HUD is allowed to say about the time. §07.
    /// <para>
    /// §07 gates the clock: <em>"시각은 지상에서만 알 수 있다. 안에서는 시간 감각이
    /// 없다."</em> The way out is to walk up — which costs about a minute — or to
    /// have bought a 회중시계, and §07 names that purchase motive explicitly. So the
    /// restriction is not a detail of the fiction; it is one of the things the shop
    /// sells.
    /// </para>
    /// <para>
    /// <b>Why this struct has no seconds field.</b> The obvious HUD is a running
    /// timer that blanks underground, but a blanked timer is still a timer: a player
    /// reads the last value before descending, starts a stopwatch in their head, and
    /// §07's "안에서는 시간 감각이 없다" is gone. The only value carried out of here
    /// is a <see cref="NightPhase"/>, which is what <c>NightPhase</c>'s own remarks
    /// ask for — "무엇을 묻는가 하면 *지금 몇 시쯤이야?* — a phase, not a stopwatch
    /// reading". A caller that wants a number has to go back to <c>MatchClock</c>,
    /// which is host-side code, and explain itself there.
    /// </para>
    /// <para>
    /// <see cref="Phase"/> is nullable rather than defaulted, so "the team cannot
    /// know" is a state the compiler makes the caller handle instead of a sentinel
    /// value that renders as a plausible time.
    /// </para>
    /// </summary>
    public readonly struct ClockReadout
    {
        /// <summary>Builds a readout directly. Prefer <see cref="From"/>.</summary>
        public ClockReadout(NightPhase? phase, bool onSurface, bool pocketWatchOwned)
        {
            Phase = phase;
            OnSurface = onSurface;
            PocketWatchOwned = pocketWatchOwned;
        }

        /// <summary>The 시각 column of §07's table, or null when the team has no way to know it.</summary>
        public NightPhase? Phase { get; }

        /// <summary>Whether the team is at the vehicle. §07's first way of learning the hour.</summary>
        public bool OnSurface { get; }

        /// <summary>Whether the team bought §08's 회중시계 — the second way, and the reason the item exists.</summary>
        public bool PocketWatchOwned { get; }

        /// <summary>Whether anything at all is drawn. False is the normal state of a descent.</summary>
        public bool IsVisible
        {
            get { return Phase.HasValue; }
        }

        /// <summary>
        /// The phase as a word, or an empty string when it may not be shown.
        /// <para>
        /// Empty rather than "알 수 없음": a HUD element that persistently announces
        /// its own ignorance is a HUD element, and §01 wants the underground to feel
        /// like it has no instruments. The absence is the message.
        /// </para>
        /// </summary>
        public string PhaseLabel
        {
            get { return Phase.HasValue ? UiStrings.Phase(Phase.Value) : string.Empty; }
        }

        /// <summary>Where the reading came from — 지상 or 회중시계 — so the purchase visibly earns its keep.</summary>
        public string SourceLabel
        {
            get { return Phase.HasValue ? UiStrings.ClockSource(OnSurface, PocketWatchOwned) : string.Empty; }
        }

        /// <summary>§07's 추가 column for this phase, where it has one. Empty for the first two tiers.</summary>
        public string WarningLabel
        {
            get { return Phase.HasValue ? UiStrings.PhaseWarning(Phase.Value) : string.Empty; }
        }

        /// <summary>
        /// True once the night has reached a tier §07 attaches a consequence to.
        /// Used only to colour the line — the words come from <see cref="WarningLabel"/>.
        /// </summary>
        public bool IsLate
        {
            get { return Phase.HasValue && Phase.Value >= NightPhase.LateNight; }
        }

        /// <summary>
        /// Reads the clock through §07's gate.
        /// <para>
        /// Uses <c>MatchClock.ReadableNightPhase</c> rather than <c>Tier.Phase</c>,
        /// which is the whole point: the gate lives in Core, is covered by Core's
        /// tests, and this layer cannot get at the ungated value without naming a
        /// different property — a change somebody would have to justify in review.
        /// </para>
        /// </summary>
        public static ClockReadout From(MatchClock? clock)
        {
            if (clock == null)
            {
                return new ClockReadout(null, false, false);
            }

            return new ClockReadout(clock.ReadableNightPhase, clock.IsTeamOnSurface, clock.TeamOwnsPocketWatch);
        }
    }
}
