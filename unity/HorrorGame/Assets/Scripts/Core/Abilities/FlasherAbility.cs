using System;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Abilities
{
    /// <summary>
    /// What one flash did. Firing and stunning are separate answers on purpose — the
    /// light goes off whether or not the monster was in it, and the HUD has to be able
    /// to say which of those happened.
    /// </summary>
    public readonly struct FlashResult
    {
        /// <summary>True when the flash actually went off — that is, it was off cooldown.</summary>
        public readonly bool Fired;

        /// <summary>True when the monster was caught by it.</summary>
        public readonly bool Stunned;

        /// <summary>Stun handed to the monster, seconds. Zero when nothing was caught.</summary>
        public readonly float StunSeconds;

        /// <summary>Distance to the monster when the flash went off, metres.</summary>
        public readonly float Distance;

        /// <summary>Angle between the aim and the monster, degrees.</summary>
        public readonly float AngleDegrees;

        /// <summary>
        /// Why the monster was not stunned: <see cref="AbilityFailure.OutOfRange"/>,
        /// <see cref="AbilityFailure.OutsideCone"/>,
        /// <see cref="AbilityFailure.LineOfSightBlocked"/>,
        /// <see cref="AbilityFailure.OnCooldown"/> when it never fired, or
        /// <see cref="AbilityFailure.None"/> when it landed.
        /// </summary>
        public readonly AbilityFailure Failure;

        /// <summary>Builds a result. Produced by <see cref="FlasherAbility"/>.</summary>
        public FlashResult(bool fired, bool stunned, float stunSeconds, float distance, float angleDegrees, AbilityFailure failure)
        {
            Fired = fired;
            Stunned = stunned;
            StunSeconds = stunSeconds;
            Distance = distance;
            AngleDegrees = angleDegrees;
            Failure = failure;
        }
    }

    /// <summary>
    /// 섬광수 — a weak, reusable stun. §04.
    /// <para>
    /// §04 states the trade directly: "효과가 약하다. 대신 재사용 가능 (쿨타임)." §01
    /// explains why it must stay weak — 살상 수단이 없다, and every form of interference
    /// is temporary, because an enemy you can beat stops being frightening. The numbers
    /// live in §16-3 as an open question, so they are held in
    /// <see cref="GameConstants.FlashStunSeconds"/> and
    /// <see cref="GameConstants.FlashCooldownSeconds"/> for the simulator to sweep.
    /// </para>
    /// <para>
    /// Two rules are worth stating because they are decisions rather than physics:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// A missed flash still burns the cooldown. Pressing the button is what costs; §10's
    /// principle is that a benefit is paid for, and a free retry until the angle is right
    /// would make the aim meaningless.
    /// </description></item>
    /// <item><description>
    /// It does not work through geometry. This is light, not sound, which is what makes
    /// §12's "좁은 공간" the Flasher's stage — "넓은 곳에서는 괴물이 우회한다."
    /// </description></item>
    /// </list>
    /// <para>
    /// The stun is handed over once, through <see cref="TryConsumeStun"/>, so the monster
    /// brain applies it exactly once no matter how the host orders its updates.
    /// </para>
    /// </summary>
    public sealed class FlasherAbility
    {
        private readonly IWorldProbe _probe;

        private float _cooldownRemaining;
        private float _pendingStunSeconds;

        /// <summary>
        /// Creates the ability against the world it will ask for a sight line.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="probe"/> is missing.</exception>
        public FlasherAbility(IWorldProbe probe)
        {
            if (probe == null)
            {
                throw new ArgumentNullException(nameof(probe));
            }

            _probe = probe;
            Failure = AbilityFailure.None;
            LastFlashFailure = AbilityFailure.Inactive;
        }

        /// <summary>Seconds until it can fire again.</summary>
        public float CooldownRemaining
        {
            get { return _cooldownRemaining; }
        }

        /// <summary>True when the flash will fire.</summary>
        public bool IsReady
        {
            get { return _cooldownRemaining <= 0f; }
        }

        /// <summary>
        /// Readiness as a fraction, 0 just after firing and 1 when ready. §04 makes
        /// reusability the role's whole compensation, so the recharge is the thing the
        /// HUD has to show.
        /// </summary>
        public float Charge01
        {
            get
            {
                return MathX.Clamp01(1f - (_cooldownRemaining / GameConstants.FlashCooldownSeconds));
            }
        }

        /// <summary>
        /// Why it cannot fire right now: <see cref="AbilityFailure.OnCooldown"/> or
        /// <see cref="AbilityFailure.None"/>.
        /// </summary>
        public AbilityFailure Failure { get; private set; }

        /// <summary>
        /// Why the last flash failed to stun. Distinct from <see cref="Failure"/>: after a
        /// good hit the ability is on cooldown, so the two answers are different and the
        /// HUD needs both — "재장전 중" beside "각도를 놓쳤다".
        /// </summary>
        public AbilityFailure LastFlashFailure { get; private set; }

        /// <summary>Stun waiting to be handed to the monster, seconds. Zero when there is none.</summary>
        public float PendingStunSeconds
        {
            get { return _pendingStunSeconds; }
        }

        /// <summary>Flashes fired this match, hits and misses alike. §13's counters.</summary>
        public int FlashCount { get; private set; }

        /// <summary>Flashes that actually stunned the monster. The ratio is what §16-3 needs from playtests.</summary>
        public int StunCount { get; private set; }

        /// <summary>Runs the cooldown down. A frame spike clamps at ready rather than banking negative time.</summary>
        public void Tick(float deltaSeconds)
        {
            var dt = deltaSeconds > 0f ? deltaSeconds : 0f;

            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining -= dt;
                if (_cooldownRemaining < 0f)
                {
                    _cooldownRemaining = 0f;
                }
            }

            Failure = _cooldownRemaining > 0f ? AbilityFailure.OnCooldown : AbilityFailure.None;
        }

        /// <summary>
        /// Fires the flash along <paramref name="aimDirection"/>.
        /// </summary>
        /// <param name="origin">Where the Flasher is.</param>
        /// <param name="aimDirection">
        /// Where it is pointed — the camera's forward, including pitch, since §05
        /// networks pitch precisely because what someone is lighting up is information.
        /// A degenerate direction cannot catch anything.
        /// </param>
        /// <param name="monster">This tick's monster snapshot.</param>
        /// <param name="result">What happened, including why nothing was stunned.</param>
        /// <returns>True when the flash went off. False means it was still on cooldown.</returns>
        public bool TryFlash(Vec3 origin, Vec3 aimDirection, in MonsterObservation monster, out FlashResult result)
        {
            var toMonster = monster.Position - origin;
            var distance = toMonster.Magnitude;
            var angle = MathX.AngleBetween(aimDirection, toMonster);

            if (_cooldownRemaining > 0f)
            {
                Failure = AbilityFailure.OnCooldown;
                result = new FlashResult(false, false, 0f, distance, angle, AbilityFailure.OnCooldown);
                return false;
            }

            _cooldownRemaining = GameConstants.FlashCooldownSeconds;
            FlashCount++;
            Failure = AbilityFailure.OnCooldown;

            var why = AbilityFailure.None;
            if (aimDirection.SqrMagnitude < 1e-12f)
            {
                // No aim, no cone. Treated as a miss rather than as a hit on everything
                // in range, which is what a zero-length forward vector would otherwise mean.
                why = AbilityFailure.OutsideCone;
            }
            else if (distance > GameConstants.FlashRange)
            {
                why = AbilityFailure.OutOfRange;
            }
            else if (angle > GameConstants.FlashConeHalfAngle)
            {
                why = AbilityFailure.OutsideCone;
            }
            else if (!_probe.HasLineOfSight(origin, monster.Position))
            {
                why = AbilityFailure.LineOfSightBlocked;
            }

            LastFlashFailure = why;

            if (why != AbilityFailure.None)
            {
                result = new FlashResult(true, false, 0f, distance, angle, why);
                return true;
            }

            _pendingStunSeconds = GameConstants.FlashStunSeconds;
            StunCount++;
            result = new FlashResult(true, true, GameConstants.FlashStunSeconds, distance, angle, AbilityFailure.None);
            return true;
        }

        /// <summary>
        /// Hands the stun to the monster system, once. §04 gives the Flasher its purpose
        /// through this value — it is what protects the person reading a clue or carrying
        /// the objective (§03), and it is intentionally shorter than the sight-line break
        /// §06 requires to release aggro, so a flash buys a moment and never an escape.
        /// </summary>
        /// <returns>False when there is no stun waiting.</returns>
        public bool TryConsumeStun(out float stunSeconds)
        {
            if (_pendingStunSeconds <= 0f)
            {
                stunSeconds = 0f;
                return false;
            }

            stunSeconds = _pendingStunSeconds;
            _pendingStunSeconds = 0f;
            return true;
        }

        /// <summary>Ready, with nothing pending. Call on death or when the team leaves the building (§03).</summary>
        public void Reset()
        {
            _cooldownRemaining = 0f;
            _pendingStunSeconds = 0f;
            Failure = AbilityFailure.None;
            LastFlashFailure = AbilityFailure.Inactive;
        }
    }
}
