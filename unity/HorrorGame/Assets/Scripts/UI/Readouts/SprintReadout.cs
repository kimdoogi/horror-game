#nullable enable

using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;

namespace HorrorGame.UI.Readouts
{
    /// <summary>
    /// The Runner's twelve seconds. §06.
    /// <para>
    /// §06 makes the bar the Runner's whole dilemma — <em>"처음부터 질주 → 거리는
    /// 벌지만 차단 지점 도달 전에 소진 / 아껴두면 → 그 사이에 잡힐 수 있음"</em> —
    /// and the decision is only playable if the player can see what is left while
    /// running for their life. So this is the one HUD element that stays up under
    /// pressure.
    /// </para>
    /// <para>
    /// <b>Only the Runner has one.</b> §05's network table says "스태미나 (주자) —
    /// 본인만 정확히, 남은 대략", so the precise bar belongs to its owner and nobody
    /// else. Building it from a role check rather than from a null stamina object
    /// means a future non-Runner sprint cannot silently inherit the readout.
    /// </para>
    /// <para>
    /// The lockout is shown, not hidden. <c>StaminaState</c> refuses to re-engage
    /// below a fraction of the bar precisely so that "주자도 스태미나가 끝나면
    /// 잡힌다" is true; a bar that showed a sliver of charge without saying it was
    /// unusable would read as a bug at the worst possible moment.
    /// </para>
    /// </summary>
    public readonly struct SprintReadout
    {
        /// <summary>Builds a readout directly. Prefer <see cref="From"/>.</summary>
        public SprintReadout(
            bool isRunner,
            float fraction,
            bool isSprinting,
            bool available,
            float secondsUntilAvailable,
            bool weightBlocked)
        {
            IsRunner = isRunner;
            Fraction = fraction;
            IsSprinting = isSprinting;
            Available = available;
            SecondsUntilAvailable = secondsUntilAvailable;
            WeightBlocked = weightBlocked;
        }

        /// <summary>Whether this player is §04's 주자. Nobody else gets a bar.</summary>
        public bool IsRunner { get; }

        /// <summary>What is left, 0–1.</summary>
        public float Fraction { get; }

        /// <summary>Whether the sprint is running right now.</summary>
        public bool IsSprinting { get; }

        /// <summary>Whether pressing Shift would do anything.</summary>
        public bool Available { get; }

        /// <summary>Seconds until it would, or 0 when it already would.</summary>
        public float SecondsUntilAvailable { get; }

        /// <summary>
        /// True when §08's weight bands are what is stopping the sprint rather than
        /// the bar. A different problem with a different fix — drop something — so it
        /// gets a different line.
        /// </summary>
        public bool WeightBlocked { get; }

        /// <summary>
        /// Drawn for the Runner whenever the bar is not both full and idle, and
        /// whenever greed has taken the sprint away.
        /// </summary>
        public bool IsVisible
        {
            get { return IsRunner && (IsSprinting || Fraction < 0.999f || WeightBlocked); }
        }

        /// <summary>True while the bar is charged but refusing — the state §06 needs the player to understand instantly.</summary>
        public bool IsLockedOut
        {
            get { return IsRunner && !Available && !WeightBlocked; }
        }

        /// <summary>
        /// Reads the Runner's bar.
        /// </summary>
        /// <param name="role">The player's §04 role. Anything but <see cref="RoleId.Runner"/> gets no readout.</param>
        /// <param name="stamina">The Runner's own state, or null for every other role.</param>
        /// <param name="canSprintAtWeight">
        /// <c>Inventory.CanSprint</c> — §08's fourth band, which bans the sprint
        /// outright and which §08 points out applies to "주자도".
        /// </param>
        public static SprintReadout From(RoleId role, StaminaState? stamina, bool canSprintAtWeight)
        {
            var isRunner = role == RoleId.Runner;
            if (!isRunner || stamina == null)
            {
                return new SprintReadout(isRunner, 1f, false, true, 0f, isRunner && !canSprintAtWeight);
            }

            return new SprintReadout(
                true,
                stamina.Fraction,
                stamina.IsSprinting,
                stamina.SprintAvailable && canSprintAtWeight,
                stamina.SecondsUntilSprintAvailable,
                !canSprintAtWeight);
        }
    }
}
