using HorrorGame.Core.Math;

namespace HorrorGame.Core.Movement
{
    /// <summary>
    /// One frame of movement intent, expressed in camera-local terms. §05.
    /// <para>
    /// §05 makes the mouse the definition of "forward": "마우스 방향 = 이동 방향.
    /// 뒤를 보려고 마우스를 돌리면 이동 기준도 함께 돌아간다." So this type carries no
    /// world direction at all — only how far the player is pushing away from
    /// wherever they are currently looking. The camera yaw that turns it into a
    /// world velocity arrives separately (see
    /// <see cref="SpeedResolver.ResolveVelocity"/>), which is what lets the whole
    /// speed table be reasoned about, tested and swept without a camera existing.
    /// </para>
    /// <para>
    /// Fields rather than properties, and deliberately mutable: the Unity input
    /// adapter fills one of these per frame and the Mirror layer deserialises one
    /// per client message. Both want a plain struct. Because a client can send
    /// anything at all, the host must run <see cref="Sanitized"/> over any input it
    /// receives before feeding it to the rules (§13 host authority).
    /// </para>
    /// </summary>
    public struct MoveInput
    {
        /// <summary>-1..1, +1 = W. Forward is whatever the camera is facing (§05).</summary>
        public float Forward;

        /// <summary>-1..1, +1 = D.</summary>
        public float Strafe;

        /// <summary>
        /// Shift. §06 makes this "달리기" for every role and "질주" for the Runner —
        /// which of the two it buys is decided by
        /// <see cref="SpeedResolver.SelectBaseSpeed"/>, not here, because stamina
        /// and carry state can both refuse it.
        /// </summary>
        public bool SprintHeld;

        /// <summary>Builds an input directly. Component order matches the field order.</summary>
        /// <param name="forward">-1..1, +1 = W.</param>
        /// <param name="strafe">-1..1, +1 = D.</param>
        /// <param name="sprintHeld">Whether Shift is down.</param>
        public MoveInput(float forward, float strafe, bool sprintHeld = false)
        {
            Forward = forward;
            Strafe = strafe;
            SprintHeld = sprintHeld;
        }

        /// <summary>
        /// The intent as a camera-local vector: X = <see cref="Strafe"/> (right),
        /// Z = <see cref="Forward"/>. Not normalised — its length is the analogue
        /// deflection, which <see cref="Deflection"/> reads.
        /// </summary>
        public Vec3 Direction => new Vec3(Strafe, 0f, Forward);

        /// <summary>
        /// True when both axes are real numbers. A client-supplied or
        /// mis-configured axis can be NaN or infinite; §13's host authority means
        /// the host checks rather than trusts.
        /// </summary>
        public bool IsFinite =>
            !float.IsNaN(Forward) && !float.IsInfinity(Forward)
            && !float.IsNaN(Strafe) && !float.IsInfinity(Strafe);

        /// <summary>
        /// This input with both axes forced into -1..1 and any non-finite value
        /// dropped to zero. Every accessor here and everything in
        /// <see cref="SpeedResolver"/> already applies it, so calling it again is
        /// harmless; call it explicitly when storing an input that arrived over the
        /// network.
        /// </summary>
        public MoveInput Sanitized
        {
            get
            {
                if (!IsFinite)
                {
                    return new MoveInput(0f, 0f, SprintHeld);
                }

                return new MoveInput(MathX.Clamp(Forward, -1f, 1f), MathX.Clamp(Strafe, -1f, 1f), SprintHeld);
            }
        }

        /// <summary>
        /// How hard the player is pushing, 0..1. Keyboard input is always 0 or 1
        /// (W+D has length √2 and clamps to 1, so the diagonal is not secretly
        /// faster), while a stick at half deflection gives half speed — §05 asks for
        /// analogue control of speed, and this is the half of it that is not about
        /// heading.
        /// </summary>
        public float Deflection
        {
            get
            {
                var m = Sanitized.Direction.MagnitudeFlat;
                return m > 1f ? 1f : m;
            }
        }

        /// <summary>
        /// How far the movement heading sits from straight ahead, 0..180 degrees.
        /// This single number drives the whole §05 table: 0 = W, 45 = the peek,
        /// 90 = pure strafe, 180 = S. Idle input reports 0 — it has no heading, and
        /// <see cref="Deflection"/> zeroes the resulting speed anyway.
        /// </summary>
        public float HeadingOffsetDegrees
        {
            get
            {
                var direction = Sanitized.Direction;
                if (direction.SqrMagnitudeFlat < 1e-12f)
                {
                    return 0f;
                }

                return MathX.Clamp(
                    MathX.AngleBetween(Vec3.Forward, direction), 0f, GameConstants.BackwardAngleDegrees);
            }
        }

        /// <summary>No movement requested. Mouselook is unaffected — §05 keeps the two separate.</summary>
        public bool IsIdle => Deflection <= 0f;

        /// <summary>
        /// True when the heading has any backward component. The speed cost is
        /// continuous, so this is not a rule input; it exists because §13's
        /// telemetry counts "seconds spent holding S"
        /// (<c>MatchSummary.BackpedalSeconds</c>) to check whether §05's peek
        /// dilemma is actually being felt.
        /// </summary>
        public bool IsBackpedalling => Sanitized.Forward < 0f && !IsIdle;
    }
}
