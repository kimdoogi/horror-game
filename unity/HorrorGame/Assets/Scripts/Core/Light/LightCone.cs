using HorrorGame.Core.Math;

namespace HorrorGame.Core.Light
{
    /// <summary>
    /// A flashlight beam as a piece of geometry: where it starts, where it points, how
    /// far it reaches and how wide it opens.
    /// <para>
    /// Separated from <see cref="FlashlightState"/> so that both halves of §03's switch
    /// read the same beam. The clue system asks "is this mark inside it and how brightly"
    /// and the perception system asks "how much of the room is this painting" — if those
    /// were two structs, §03's "목표와 위험이 같은 스위치에 걸린다" would survive as a
    /// sentence and die as code.
    /// </para>
    /// <para>
    /// The beam is a full 3D cone, not a flat one. §05 requires camera pitch on the wire
    /// — "바닥·천장을 비추는 것도 신호" — so pointing at the floor has to be a different
    /// beam from pointing down the corridor.
    /// </para>
    /// </summary>
    public readonly struct LightCone
    {
        /// <summary>The player's light, at eye height.</summary>
        public readonly Vec3 Origin;

        /// <summary>Unit aim direction. §05: 마우스 방향 = 이동 방향, and it also aims the beam.</summary>
        public readonly Vec3 Forward;

        /// <summary>Reach in metres, after §08's upgrade and §07's 심야 penalty.</summary>
        public readonly float RangeMetres;

        /// <summary>Half-angle in degrees. §03: 빛이 좁다.</summary>
        public readonly float HalfAngleDegrees;

        /// <summary>
        /// Builds a beam. A degenerate aim direction, a non-positive range or a
        /// non-positive half-angle all produce an unlit cone rather than an exception —
        /// a player carrying the objective has no flashlight at all (§03), and that is a
        /// normal state to be asked about.
        /// </summary>
        public LightCone(Vec3 origin, Vec3 forward, float rangeMetres, float halfAngleDegrees)
        {
            Origin = origin;
            Forward = forward.Normalized;
            RangeMetres = float.IsNaN(rangeMetres) || rangeMetres <= 0f ? 0f : rangeMetres;
            HalfAngleDegrees = float.IsNaN(halfAngleDegrees) || halfAngleDegrees <= 0f ? 0f : halfAngleDegrees;
        }

        /// <summary>
        /// No beam. §03's 목표물 운반 rule — "양손을 쓴다 · 손전등을 들 수 없다" — and a
        /// dead battery both resolve to this, and both must produce the same darkness.
        /// </summary>
        public static LightCone None
        {
            get { return default(LightCone); }
        }

        /// <summary>True when this beam is actually emitting.</summary>
        public bool IsLit
        {
            get { return RangeMetres > 0f && HalfAngleDegrees > 0f && Forward.SqrMagnitude > 0f; }
        }

        /// <summary>
        /// Surface this beam paints, in square metres up to a constant factor: the area
        /// of the spherical cap it cuts at its own range.
        /// <para>
        /// This is the honest measure of "how much light is loose in the room", and it is
        /// what <see cref="LightRules.BeamConspicuousness"/> turns into §03's "괴물이 잘
        /// 본다". It grows with reach and with spread, which is why §08 can sell reach as
        /// a price as well as a reward, and why §07's 심야 penalty makes a player
        /// slightly less conspicuous as well as slightly blinder.
        /// </para>
        /// <para>
        /// The 2π of the real cap area is dropped: every use is a ratio against
        /// <see cref="LightRules.ReferenceBeamFootprint"/>, so the constant cancels and
        /// leaving it out keeps a squared metre count from overflowing into nonsense on a
        /// misconfigured beam.
        /// </para>
        /// </summary>
        public float LitFootprint
        {
            get
            {
                if (!IsLit)
                {
                    return 0f;
                }

                var spread = 1f - System.MathF.Cos(HalfAngleDegrees * MathX.Deg2Rad);
                return RangeMetres * RangeMetres * spread;
            }
        }

        /// <summary>True when a point falls inside the beam at all.</summary>
        public bool Contains(Vec3 point)
        {
            return IsLit && MathX.InCone(Origin, Forward, point, RangeMetres, HalfAngleDegrees);
        }

        /// <summary>
        /// Light landing on a point, 0–1, for
        /// <c>ClueReadContext.LightQuality</c>.
        /// <para>
        /// Falls off linearly to zero at the edge of the cone in both directions — with
        /// distance, and with angle off the axis. Both terms are normalised by the beam's
        /// own numbers, so this introduces no tuned value of its own: the only inputs are
        /// §03's <see cref="GameConstants.FlashlightRange"/> and
        /// <see cref="GameConstants.FlashlightHalfAngle"/> as they arrive through the
        /// cone, and re-tuning either moves the readable envelope with it.
        /// </para>
        /// <para>
        /// The angular term is what makes §03's "빛이 좁다" a real obstacle. A mark at
        /// 3 m — §03's <see cref="GameConstants.ClueReadRange"/> — is readable within
        /// about 17° of the axis on an issued flashlight and unreadable beyond it, so a
        /// player has to aim rather than stand in the room facing roughly the right way.
        /// </para>
        /// </summary>
        public float QualityAt(Vec3 point)
        {
            if (!IsLit)
            {
                return 0f;
            }

            var to = point - Origin;
            var distance = to.Magnitude;
            if (distance > RangeMetres)
            {
                return 0f;
            }

            // Standing on the lamp: the direction is degenerate, so there is no
            // meaningful angle and the point is fully lit.
            if (distance <= MathX.Epsilon)
            {
                return 1f;
            }

            var angle = MathX.AngleBetween(Forward, to);
            if (angle > HalfAngleDegrees)
            {
                return 0f;
            }

            var radial = 1f - (distance / RangeMetres);
            var angular = 1f - (angle / HalfAngleDegrees);
            return MathX.Clamp01(radial * angular);
        }
    }
}
