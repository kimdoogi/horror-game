using System;
using HorrorGame.Core.Math;

namespace HorrorGame.Core.Movement
{
    /// <summary>
    /// The §05 speed table, as a continuous function of where the player is
    /// pushing relative to where they are looking.
    /// <para>
    /// §05 states the four rows — 전진 100%, 대각 95%, 측면 90%, 후진 65% — and then
    /// says explicitly what they are: "플레이어가 정보량과 속도를 연속적으로 조절할 수
    /// 있다. 이산적 선택이 아니라 아날로그 조절이라는 점이 중요하다 — 마우스를 몇 도
    /// 돌릴지가 곧 실력이다." Four <c>if</c> branches would contradict that
    /// sentence: a player who turns 10° would pay either nothing or the full
    /// diagonal cost, and the 45° peek would stop being a skill and become a button.
    /// So the table is treated as four knots on a curve and everything between them
    /// is interpolated by heading angle. The four documented values still come out
    /// exactly at pure W, W+D, D and S — see MovementTests.
    /// </para>
    /// <para>
    /// The curve is piecewise linear in degrees rather than a smooth spline. A
    /// spline through these four points overshoots — it would make some headings
    /// faster than a nearer-to-forward one, which would let a player buy
    /// information for free and destroy the trade §05 is built on. Monotonicity
    /// matters more than a continuous derivative; a kink in cost-per-degree is not
    /// something a player can feel, but a pocket of free turning is something they
    /// would find.
    /// </para>
    /// <para>Everything here is a pure function. Stamina, the one piece of state, lives in <see cref="StaminaState"/>.</para>
    /// </summary>
    public static class SpeedResolver
    {
        /// <summary>
        /// The player's speed this frame in m/s: base speed × the §05 directional
        /// multiplier × the stance multiplier × analogue deflection.
        /// <para>
        /// This is a scalar. Multiply it by a <em>unit</em> direction to get a
        /// velocity — the deflection is already accounted for here, so scaling the
        /// raw input vector instead would count it twice and make diagonals faster.
        /// <see cref="ResolveVelocity"/> does it correctly.
        /// </para>
        /// </summary>
        /// <param name="input">This frame's intent, camera-local.</param>
        /// <param name="context">Base speed and the stance multiplier.</param>
        public static float Resolve(MoveInput input, MovementContext context)
        {
            var baseSpeed = context.BaseSpeed;
            if (float.IsNaN(baseSpeed) || baseSpeed <= 0f)
            {
                return 0f;
            }

            if (float.IsInfinity(baseSpeed))
            {
                return 0f;
            }

            return baseSpeed * DirectionalMultiplier(input) * ContextMultiplier(context) * input.Deflection;
        }

        /// <summary>
        /// The §05 multiplier alone, for tests and telemetry: 1.00 straight ahead
        /// falling to 0.65 straight back, interpolated in between.
        /// </summary>
        /// <param name="input">This frame's intent; only its heading is read.</param>
        public static float DirectionalMultiplier(MoveInput input) =>
            MultiplierForHeading(input.HeadingOffsetDegrees);

        /// <summary>
        /// The §05 multiplier for an arbitrary heading offset in degrees, so the
        /// curve can be sampled without building an input — the simulator sweeps it
        /// and the HUD wants to show the cost of a turn the player has not made yet.
        /// <para>
        /// Signed and out-of-range angles are folded into 0..180: turning left and
        /// right cost the same, which is what makes the peek a choice of angle
        /// rather than a choice of side.
        /// </para>
        /// </summary>
        /// <param name="degreesFromForward">Heading offset from straight ahead, any sign, any magnitude.</param>
        public static float MultiplierForHeading(float degreesFromForward)
        {
            if (float.IsNaN(degreesFromForward))
            {
                return GameConstants.MulForward;
            }

            if (float.IsInfinity(degreesFromForward))
            {
                return GameConstants.MulBackward;
            }

            var angle = MathF.Abs(MathX.NormalizeAngle(degreesFromForward));

            if (angle <= GameConstants.PeekAngleDegrees)
            {
                return MathX.Lerp(
                    GameConstants.MulForward,
                    GameConstants.MulDiagonal,
                    angle / GameConstants.PeekAngleDegrees);
            }

            if (angle <= GameConstants.StrafeAngleDegrees)
            {
                return MathX.Lerp(
                    GameConstants.MulDiagonal,
                    GameConstants.MulStrafe,
                    (angle - GameConstants.PeekAngleDegrees)
                    / (GameConstants.StrafeAngleDegrees - GameConstants.PeekAngleDegrees));
            }

            return MathX.Lerp(
                GameConstants.MulStrafe,
                GameConstants.MulBackward,
                (angle - GameConstants.StrafeAngleDegrees)
                / (GameConstants.BackwardAngleDegrees - GameConstants.StrafeAngleDegrees));
        }

        /// <summary>
        /// The non-directional half of the product — everything that scales speed
        /// without regard to which way the player is facing. In the race that is the
        /// stance and nothing else: <c>PlayerMotor.BuildContext</c> fills
        /// <see cref="MovementContext.LoadMultiplier"/> from
        /// <c>PlayerStance.SpeedMultiplier</c>, so this returns 1 standing and
        /// <see cref="GameConstants.CrouchSpeedMultiplier"/> crouched.
        /// <para>
        /// Kept as its own step rather than folded into <see cref="Resolve"/> because
        /// §08 stated the composition rule for every penalty at once — "§05 배율에
        /// 곱연산으로 적용된다" — and this is that product. A player crouching backwards
        /// pays 후진's 65% on top of the crouch, and anything the race adds later
        /// (a limp, mud, a 투하구 landing) multiplies in here without touching §05.
        /// </para>
        /// <para>
        /// <b>Two co-op factors used to be multiplied in here and are gone.</b>
        /// <c>CarryingObjective</c> applied <c>ObjectiveCarrySpeedMultiplier</c> and
        /// <c>BagEquipped</c> applied <c>BagSpeedMultiplier</c>. Both flags are
        /// hard-coded to <c>false</c> by the only caller that builds a context, so both
        /// branches were unreachable — a dead objective and a dead shop item costing
        /// two compares on every movement resolve of a footrace. Nobody carries
        /// anything in 하강; do not reintroduce a factor here without a race reason.
        /// </para>
        /// <para>
        /// A negative or NaN multiplier resolves to 0 rather than to backwards or
        /// undefined movement: whatever feeds this number, a bug there must not become
        /// a player walking in reverse.
        /// </para>
        /// </summary>
        /// <param name="context">The context whose non-directional multiplier to sanitise.</param>
        public static float ContextMultiplier(MovementContext context)
        {
            var load = context.LoadMultiplier;
            if (float.IsNaN(load) || load < 0f)
            {
                return 0f;
            }

            if (float.IsInfinity(load))
            {
                return 0f;
            }

            return load;
        }

        /// <summary>
        /// Picks the §06 base speed for an input: walk without Shift, run with it,
        /// sprint only when the stamina bar allows it.
        /// <para>
        /// Takes bools rather than a role so movement keeps its distance from that
        /// system. Callers pass <paramref name="staminaReady"/> =
        /// <see cref="StaminaState.SprintAvailable"/>.
        /// </para>
        /// <para>
        /// <b>The <c>MovementContext</c> parameter is gone.</b> It was read for one
        /// thing — <c>CarryingObjective</c>, which capped a carrier at
        /// <see cref="GameConstants.RunSpeed"/> because §03 said "주자가 들면 질주 불가".
        /// Nothing is carried in the race and the flag is hard-coded to <c>false</c>,
        /// so the branch was unreachable and the parameter fed only it. Base speed in
        /// 하강 is a function of the key held and the bar, and of nothing else.
        /// </para>
        /// </summary>
        /// <param name="input">This frame's intent; only <see cref="MoveInput.SprintHeld"/> is read.</param>
        /// <param name="sprintUnlocked">Whether this player may sprint at all.</param>
        /// <param name="staminaReady">Whether the stamina bar would grant a sprint right now.</param>
        public static float SelectBaseSpeed(MoveInput input, bool sprintUnlocked, bool staminaReady)
        {
            if (!input.SprintHeld)
            {
                return GameConstants.WalkSpeed;
            }

            return sprintUnlocked && staminaReady ? GameConstants.RunnerSprintSpeed : GameConstants.RunSpeed;
        }

        /// <summary>
        /// Whether this input and context beat the monster right now, and by how
        /// much. The cleanest statement of §05's argument, and the shape the HUD's
        /// chase readout and §13's telemetry both consume.
        /// </summary>
        /// <param name="input">This frame's intent.</param>
        /// <param name="context">Base speed and the stance multiplier.</param>
        /// <param name="monsterSpeed">The monster's current speed from the §07 threat tier, m/s.</param>
        public static ChaseMargin MarginVersusMonster(MoveInput input, MovementContext context, float monsterSpeed) =>
            new ChaseMargin(Resolve(input, context), monsterSpeed);

        /// <summary>
        /// The world-space velocity for an input, given where the camera is looking.
        /// §05: "마우스 방향 = 이동 방향" — the yaw that defines forward is the same yaw
        /// §05 requires to be network-synced, because it also aims the flashlight.
        /// <para>
        /// Pitch is ignored on purpose: looking up does not make a player walk into
        /// the ceiling.
        /// </para>
        /// </summary>
        /// <param name="input">This frame's intent, camera-local.</param>
        /// <param name="context">Base speed and the stance multiplier.</param>
        /// <param name="cameraYawDegrees">Camera yaw, degrees clockwise from +Z (see <see cref="MathX.DirectionFromYaw"/>).</param>
        public static Vec3 ResolveVelocity(MoveInput input, MovementContext context, float cameraYawDegrees)
        {
            var speed = Resolve(input, context);
            if (speed <= 0f || float.IsNaN(cameraYawDegrees) || float.IsInfinity(cameraYawDegrees))
            {
                return Vec3.Zero;
            }

            var local = input.Sanitized.Direction.NormalizedFlat;
            if (local.SqrMagnitudeFlat < 1e-12f)
            {
                return Vec3.Zero;
            }

            var forward = MathX.DirectionFromYaw(cameraYawDegrees);
            var right = new Vec3(forward.Z, 0f, -forward.X);
            return ((right * local.X) + (forward * local.Z)) * speed;
        }
    }
}
