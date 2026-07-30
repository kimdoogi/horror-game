using System;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Abilities
{
    /// <summary>
    /// 주자 — the sprint, and the taunt that forces the monster's aggro. §04.
    /// <para>
    /// §06 puts the whole role in one line: 달리기 4.5 &lt; 괴물 4.8 &lt; 주자 질주 5.6.
    /// This object owns the two halves of that — how long the 5.6 lasts, and how the
    /// monster is made to care — and nothing else.
    /// </para>
    /// <para>
    /// It couples to movement through floats only, as §03 of ARCHITECTURE requires:
    /// <see cref="BaseSpeed"/> is what <c>MovementContext.BaseSpeed</c> takes, and the
    /// two constraints that can cancel a sprint arrive as bools — carrying the
    /// objective (§03: "주자가 들면 질주 불가") and the load bands (§08, via
    /// <c>Inventory.CanSprint</c>). No inventory or movement type appears here.
    /// </para>
    /// <para>
    /// Aggro *release* is deliberately absent. §06 makes it the monster's decision —
    /// 거리 12m + 시야 차단 3초 — and its consequence, "마지막 목격 위치로 이동", is why
    /// the Runner's direction of flight is a strategy rather than a detail. The brain
    /// tells this object when it happened via <see cref="NotifyAggroReleased"/>.
    /// </para>
    /// </summary>
    public sealed class RunnerAbility
    {
        private readonly IWorldProbe _probe;

        private float _staminaSeconds;

        /// <summary>
        /// Creates the ability against the world it will ask about reachability. §12
        /// makes the taunt a positional decision, so it needs the navigable graph.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="probe"/> is missing.</exception>
        public RunnerAbility(IWorldProbe probe)
        {
            if (probe == null)
            {
                throw new ArgumentNullException(nameof(probe));
            }

            _probe = probe;
            _staminaSeconds = GameConstants.SprintStaminaSeconds;
            BaseSpeed = GameConstants.WalkSpeed;
            Failure = AbilityFailure.None;
            TauntFailure = AbilityFailure.Inactive;
        }

        /// <summary>Sprint left, in seconds of sprinting. §06: a full bar is 12 s.</summary>
        public float StaminaSeconds
        {
            get { return _staminaSeconds; }
        }

        /// <summary>
        /// Sprint left as a fraction. §05 lists stamina as a networked value —
        /// exact for the owner, approximate for everyone else.
        /// </summary>
        public float Stamina01
        {
            get { return MathX.Clamp01(_staminaSeconds / GameConstants.SprintStaminaSeconds); }
        }

        /// <summary>True while the 5.6 m/s sprint is actually being delivered.</summary>
        public bool IsSprinting { get; private set; }

        /// <summary>
        /// Speed tier for <c>MovementContext.BaseSpeed</c>, m/s: the Runner's sprint
        /// while sprinting, the ordinary run when Shift is held but the sprint is
        /// denied (§05 gives every role 달리기 on Shift), and walking otherwise.
        /// <para>
        /// The denied case is the one that kills: 4.5 is below the monster's 4.8, so a
        /// Runner holding the objective or 16 weight of loot is caught like anyone else.
        /// </para>
        /// </summary>
        public float BaseSpeed { get; private set; }

        /// <summary>
        /// How much of the last tick was actually sprinted, seconds. On a frame spike
        /// this is less than <c>deltaSeconds</c>, because a 12 s bar cannot pay for a
        /// 30 s step — integrate the spike as this many seconds at
        /// <see cref="GameConstants.RunnerSprintSpeed"/> plus the remainder at
        /// <see cref="BaseSpeed"/>, or the Runner teleports through §12's cover spacing.
        /// </summary>
        public float LastTickSprintSeconds { get; private set; }

        /// <summary>
        /// True while the monster's aggro is pinned to this Runner. The monster brain
        /// reads this; §04 calls it 어그로 강제 획득 and §12 explains the point —
        /// "괴물을 끌어내 진입 창을 만든다."
        /// </summary>
        public bool AggroForced { get; private set; }

        /// <summary>Where the last successful taunt was made from. §06: aggro releases toward the last sighting, so this is where the monster will be sent.</summary>
        public Vec3 LastTauntPosition { get; private set; }

        /// <summary>Successful taunts this match. §13's bucket counters want the distribution.</summary>
        public int TauntCount { get; private set; }

        /// <summary>
        /// Why the sprint is not being delivered:
        /// <see cref="AbilityFailure.OutOfStamina"/>,
        /// <see cref="AbilityFailure.CarryingObjective"/> or
        /// <see cref="AbilityFailure.LoadTooHeavy"/>.
        /// <para>
        /// The two load reasons are reported whether or not Shift is held, because they
        /// say why the sprint is unavailable rather than why one attempt failed — that is
        /// what a greyed-out icon has to explain. Simply not holding Shift is
        /// <see cref="AbilityFailure.None"/>: nothing is wrong.
        /// </para>
        /// </summary>
        public AbilityFailure Failure { get; private set; }

        /// <summary>
        /// Why the last taunt attempt failed: <see cref="AbilityFailure.OutOfRange"/> or
        /// <see cref="AbilityFailure.MonsterUnreachable"/>. Separate from
        /// <see cref="Failure"/> because the two halves of the role fail for unrelated
        /// reasons and the HUD shows them in different places.
        /// </summary>
        public AbilityFailure TauntFailure { get; private set; }

        /// <summary>
        /// Steps the sprint and the stamina bar.
        /// </summary>
        /// <param name="deltaSeconds">Step length. A spike is clamped to the stamina available — see <see cref="LastTickSprintSeconds"/>.</param>
        /// <param name="sprintHeld">Shift. §05.</param>
        /// <param name="carryingObjective">§03: both hands are full, so there is no sprint.</param>
        /// <param name="loadPermitsSprint">§08: false at weight 16 or more. Comes from <c>Inventory.CanSprint</c> as a bool.</param>
        public void Tick(float deltaSeconds, bool sprintHeld, bool carryingObjective, bool loadPermitsSprint)
        {
            var dt = deltaSeconds > 0f ? deltaSeconds : 0f;
            LastTickSprintSeconds = 0f;

            var reengageFloor = GameConstants.SprintStaminaSeconds * GameConstants.SprintReengageStaminaFraction;
            var sprinting = false;
            var failure = AbilityFailure.None;

            if (carryingObjective)
            {
                failure = AbilityFailure.CarryingObjective;
            }
            else if (!loadPermitsSprint)
            {
                failure = AbilityFailure.LoadTooHeavy;
            }
            else if (!sprintHeld)
            {
                failure = AbilityFailure.None;
            }
            else if (IsSprinting ? _staminaSeconds > 0f : _staminaSeconds >= reengageFloor)
            {
                // Hysteresis, not decoration. Drain is 1 s/s and refill is 0.6 s/s, so
                // without a floor to re-engage from, holding Shift on an empty bar
                // would flicker the sprint every tick and hand out an unlimited 60%
                // sprint — §06's "주자도 스태미나가 끝나면 잡힌다" would mean nothing.
                sprinting = true;
            }
            else
            {
                failure = AbilityFailure.OutOfStamina;
            }

            if (sprinting)
            {
                var sprintSeconds = dt < _staminaSeconds ? dt : _staminaSeconds;
                LastTickSprintSeconds = sprintSeconds;
                _staminaSeconds -= sprintSeconds;

                if (_staminaSeconds <= 0f)
                {
                    _staminaSeconds = 0f;
                    sprinting = false;
                    failure = AbilityFailure.OutOfStamina;
                }

                // Whatever is left of a spike tick was spent running, not sprinting,
                // so it recovers. Skipping this would make one 30 s frame cost the
                // same as one 12 s frame.
                var remainder = dt - sprintSeconds;
                if (remainder > 0f)
                {
                    Recover(remainder);
                }
            }
            else
            {
                Recover(dt);
            }

            IsSprinting = sprinting;
            Failure = failure;
            BaseSpeed = sprinting
                ? GameConstants.RunnerSprintSpeed
                : (sprintHeld ? GameConstants.RunSpeed : GameConstants.WalkSpeed);
        }

        /// <summary>
        /// Forces the monster's aggro onto this Runner from
        /// <paramref name="runnerPosition"/>. §04.
        /// <para>
        /// Fails out of range, and fails when the probe reports no path between the
        /// two — a Runner behind a barricade or a locked door (§04, Engineer) can
        /// shout all it likes and pull nothing. §12's arithmetic is why the range is
        /// generous: "주자는 멀리서 어그로를 걸어야 한다", because a taunt at 3 m
        /// leaves the release unreachable.
        /// </para>
        /// </summary>
        /// <returns>True when aggro is now pinned to this Runner.</returns>
        public bool TryTaunt(Vec3 runnerPosition, in MonsterObservation monster, out AbilityFailure failure)
        {
            var distance = Vec3.DistanceFlat(runnerPosition, monster.Position);
            if (distance > GameConstants.RunnerTauntRange)
            {
                failure = AbilityFailure.OutOfRange;
                TauntFailure = failure;
                return false;
            }

            // Straight-line distance is not the question §12 asks. The monster has to
            // be able to walk here, and an unreachable probe answer is a legitimate
            // map state, not an error.
            var path = _probe.NavigableDistance(monster.Position, runnerPosition);
            if (float.IsNaN(path) || float.IsInfinity(path))
            {
                failure = AbilityFailure.MonsterUnreachable;
                TauntFailure = failure;
                return false;
            }

            AggroForced = true;
            LastTauntPosition = runnerPosition;
            TauntCount++;
            failure = AbilityFailure.None;
            TauntFailure = failure;
            return true;
        }

        /// <summary>
        /// Told by the monster brain that §06's release condition — 12 m plus 3 s of
        /// broken sight line — has been met. The Runner does not get to decide this,
        /// and the reason is §06's sting: the monster then heads for where it last saw
        /// this Runner, so breaking aggro near the team delivers the monster to them.
        /// </summary>
        public void NotifyAggroReleased()
        {
            AggroForced = false;
        }

        /// <summary>
        /// Full bar, no aggro, walking. §03 resets the monster's pursuit state when the
        /// team leaves the building; this is the Runner's half of that reset.
        /// </summary>
        public void Reset()
        {
            _staminaSeconds = GameConstants.SprintStaminaSeconds;
            IsSprinting = false;
            AggroForced = false;
            LastTickSprintSeconds = 0f;
            BaseSpeed = GameConstants.WalkSpeed;
            Failure = AbilityFailure.None;
            TauntFailure = AbilityFailure.Inactive;
        }

        private void Recover(float seconds)
        {
            // §06 fixes the ratio: 12 s of sprint, 20 s to refill from empty.
            var rate = GameConstants.SprintStaminaSeconds / GameConstants.SprintStaminaRecoverySeconds;
            _staminaSeconds += seconds * rate;
            if (_staminaSeconds > GameConstants.SprintStaminaSeconds)
            {
                _staminaSeconds = GameConstants.SprintStaminaSeconds;
            }
        }
    }
}
