using HorrorGame.Core.Math;

namespace HorrorGame.Core.Movement
{
    /// <summary>
    /// The Runner's sprint bar: 12 s of sprint, 20 s to refill (§06).
    /// <para>
    /// §06 gives the bar one job — "주자도 스태미나가 끝나면 잡힌다. 무한 도주 방지" —
    /// and one dilemma, §06's "질주를 언제 쓸 것인가": spend it early and it runs out
    /// before the next line-of-sight break, save it and the monster is already on
    /// you. Both only work if the bar is a real resource, which means a partial
    /// sprint must cost exactly what it used and no tapping pattern may be better
    /// than committing.
    /// </para>
    /// <para>
    /// Two rules exist that §06 does not state, because without them the bar is not
    /// a resource at all:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Recovery delay</b> (<see cref="GameConstants.SprintRecoveryDelaySeconds"/>).
    /// Refilling does not start the instant Shift is released. Otherwise a player
    /// alternating Shift every frame drains and refills in the same breath and holds
    /// the bar at a constant level forever; with the delay, a burst shorter than the
    /// delay returns nothing, so tapping is strictly worse than committing.
    /// </description></item>
    /// <item><description>
    /// <b>Restart hysteresis</b> (<see cref="GameConstants.SprintReengageStaminaFraction"/>).
    /// A sprint already running continues down to zero, but a new one cannot begin
    /// until the bar is back to that fraction. Otherwise an exhausted Runner spends
    /// each sliver of returning stamina the frame it arrives and never actually
    /// reaches the "잡힌다" that §06 promises.
    /// </description></item>
    /// </list>
    /// <para>
    /// Neither rule closes the gap recorded in docs/BALANCE-FINDINGS.md: a Runner
    /// who cycles the bar patiently, running straight ahead and never looking back,
    /// still averages faster than the monster indefinitely. That is a §06 tuning
    /// question, not something this class should quietly fix.
    /// </para>
    /// <para>Stepped by the host at <see cref="GameConstants.FixedStep"/>; it never reads a clock.</para>
    /// </summary>
    public sealed class StaminaState
    {
        private float _fraction;
        private float _recoveryDelayRemaining;

        /// <summary>A full bar, the state a player spawns with.</summary>
        public StaminaState()
        {
            Reset();
        }

        /// <summary>
        /// A bar at a chosen level, clamped to 0..1. Used by the simulator to sweep
        /// steady-state sprint cycling without waiting out a first full bar.
        /// </summary>
        /// <param name="initialFraction">Starting level, 0..1.</param>
        public StaminaState(float initialFraction)
        {
            Reset();
            _fraction = float.IsNaN(initialFraction) ? 0f : MathX.Clamp01(initialFraction);
        }

        /// <summary>
        /// Whether the player is asking to sprint. Set it before
        /// <see cref="Tick(float)"/>; the request may be refused, which is what
        /// <see cref="SprintAvailable"/> reports.
        /// </summary>
        public bool SprintRequested { get; set; }

        /// <summary>How much bar is left, 0..1. §05 syncs this exactly to its owner and roughly to everyone else.</summary>
        public float Fraction => _fraction;

        /// <summary>Sprint left, in seconds. §06's 12 s bar expressed the way a player thinks about it.</summary>
        public float SecondsRemaining => _fraction * GameConstants.SprintStaminaSeconds;

        /// <summary>
        /// Whether sprint was granted during the most recent step of non-zero
        /// length. A zero-length step observes nothing and so changes nothing.
        /// </summary>
        public bool IsSprinting { get; private set; }

        /// <summary>
        /// Seconds of sprint actually granted inside the last <see cref="Tick(float)"/>,
        /// which is <em>not</em> always the step length: a frame spike longer than
        /// the remaining bar gets only what the bar held. The movement layer must
        /// integrate with this rather than the frame's delta, or a stutter would
        /// hand out sprint that was never paid for.
        /// </summary>
        public float LastSprintSeconds { get; private set; }

        /// <summary>
        /// The bar hit zero during the last <see cref="Tick(float)"/>. Reported
        /// separately because a long enough frame spike can empty the bar and then
        /// refill part of it inside the same step — a listener watching only the
        /// end-of-step level would miss the exhaustion entirely.
        /// </summary>
        public bool ExhaustedThisTick { get; private set; }

        /// <summary>
        /// Whether a sprint request would be granted right now. Pass this to
        /// <see cref="SpeedResolver.SelectBaseSpeed"/>. Continuing a sprint only
        /// needs a non-empty bar; starting one needs
        /// <see cref="GameConstants.SprintReengageStaminaFraction"/>.
        /// </summary>
        public bool SprintAvailable =>
            IsSprinting ? _fraction > 0f : _fraction >= GameConstants.SprintReengageStaminaFraction;

        /// <summary>Sprint is refused: either empty, or recovering below the restart threshold.</summary>
        public bool IsLockedOut => !SprintAvailable;

        /// <summary>Seconds before refilling resumes. Reset on every granted sprint step.</summary>
        public float RecoveryDelayRemaining => _recoveryDelayRemaining;

        /// <summary>
        /// Seconds until sprint becomes available again, 0 if it already is. The HUD
        /// shows this instead of a bare empty bar, because §06's dilemma requires the
        /// player to know when the option returns.
        /// </summary>
        public float SecondsUntilSprintAvailable
        {
            get
            {
                if (SprintAvailable)
                {
                    return 0f;
                }

                var missing = GameConstants.SprintReengageStaminaFraction - _fraction;
                if (missing < 0f)
                {
                    missing = 0f;
                }

                return _recoveryDelayRemaining + (missing * GameConstants.SprintStaminaRecoverySeconds);
            }
        }

        /// <summary>
        /// Advances the bar by one step. Drains while sprint is granted, refills
        /// otherwise once the recovery delay has elapsed.
        /// <para>
        /// A step of zero or negative length is not a state transition and does
        /// nothing: the host ticks paused and loading frames, and a stall must not
        /// cost stamina. A step longer than the remaining bar is split — the sprint
        /// takes what is there, exhaustion is recorded, and the leftover time goes
        /// to the recovery delay and then to refilling. That is what keeps a frame
        /// spike from either granting free sprint or freezing the bar.
        /// </para>
        /// </summary>
        /// <param name="deltaSeconds">Step length, seconds. Normally <see cref="GameConstants.FixedStep"/>.</param>
        public void Tick(float deltaSeconds)
        {
            LastSprintSeconds = 0f;
            ExhaustedThisTick = false;

            if (float.IsNaN(deltaSeconds) || deltaSeconds <= 0f)
            {
                return;
            }

            var remaining = float.IsInfinity(deltaSeconds) ? float.MaxValue : deltaSeconds;

            if (SprintRequested && SprintAvailable)
            {
                var available = SecondsRemaining;
                var used = remaining < available ? remaining : available;

                _fraction -= used / GameConstants.SprintStaminaSeconds;
                if (_fraction < 0f)
                {
                    _fraction = 0f;
                }

                LastSprintSeconds = used;
                remaining -= used;
                _recoveryDelayRemaining = GameConstants.SprintRecoveryDelaySeconds;
                ExhaustedThisTick = _fraction <= 0f;
                IsSprinting = _fraction > 0f;
            }
            else
            {
                IsSprinting = false;
            }

            if (remaining <= 0f)
            {
                return;
            }

            if (_recoveryDelayRemaining > 0f)
            {
                var waited = remaining < _recoveryDelayRemaining ? remaining : _recoveryDelayRemaining;
                _recoveryDelayRemaining -= waited;
                remaining -= waited;
            }

            if (remaining <= 0f || _fraction >= 1f)
            {
                return;
            }

            _fraction += remaining / GameConstants.SprintStaminaRecoverySeconds;
            if (_fraction > 1f)
            {
                _fraction = 1f;
            }
        }

        /// <summary>
        /// Reads the sprint request off an input and advances one step.
        /// <para>
        /// Sprint is only requested when the player is actually moving. §06 sizes
        /// the bar by the distance it covers ("최대 이동 60m"), so standing still with
        /// Shift held must not burn the escape — and a player who has stopped is
        /// hiding, which is the opposite of sprinting.
        /// </para>
        /// </summary>
        /// <param name="deltaSeconds">Step length, seconds.</param>
        /// <param name="input">This frame's intent.</param>
        public void Tick(float deltaSeconds, MoveInput input)
        {
            SprintRequested = input.SprintHeld && !input.IsIdle;
            Tick(deltaSeconds);
        }

        /// <summary>Back to a full bar with no request pending. Match start, and after a §09 respawn.</summary>
        public void Reset()
        {
            _fraction = 1f;
            _recoveryDelayRemaining = 0f;
            IsSprinting = false;
            LastSprintSeconds = 0f;
            ExhaustedThisTick = false;
            SprintRequested = false;
        }
    }
}
