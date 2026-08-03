using HorrorGame.Core.Math;

namespace HorrorGame.Core.Light
{
    /// <summary>
    /// A flashlight beam as a piece of geometry: where it starts, where it points, how
    /// far it reaches and how wide it opens.
    /// <para>
    /// Separated from <see cref="FlashlightState"/> so that anything asking what the beam
    /// is touching gets the same answer as anything asking where the beam is pointed.
    /// </para>
    /// <para>
    /// The beam is a full 3D cone, not a flat one. Camera pitch is on the wire —
    /// "바닥·천장을 비추는 것도 신호" — so pointing at the floor has to be a different beam
    /// from pointing down the corridor.
    /// </para>
    /// <para>
    /// <b>It has no consumer at the moment, and that is worth stating rather than
    /// hiding.</b> Its readers were §03's clue system ("is this mark lit enough to read")
    /// and a <c>LightRules.Visibility</c> term that fed the creature's perception. The
    /// clue chain is deleted and the creature has never actually read a light term — its
    /// <c>CanSee</c> is sight range, half-angle and line-of-sight, nothing more. This
    /// struct survives because it is the honest description of the torch the runner still
    /// carries, not because something calls it; if the creature is ever taught to see
    /// light, this is the shape that answer comes in.
    /// </para>
    /// </summary>
    public readonly struct LightCone
    {
        /// <summary>The runner's light, at eye height.</summary>
        public readonly Vec3 Origin;

        /// <summary>Unit aim direction. 마우스 방향 = 이동 방향, and it also aims the beam.</summary>
        public readonly Vec3 Forward;

        /// <summary>Reach in metres, after the time-of-night penalty.</summary>
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
        /// No beam. What a switched-off torch resolves to. It used to also be what a flat
        /// cell and a pair of hands full of 목표물 resolved to; both of those states are
        /// gone with the battery and the carry system, so the switch is the only way here
        /// now.
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

        // LitFootprint — the spherical-cap area this beam painted — was deleted with
        // LightRules. Its only reader was LightRules.BeamConspicuousness, which turned it
        // into a ratio against a reference footprint to price §08's 강화 손전등: buying
        // reach also bought being noticed from further away. There is no shop and no
        // upgrade, so there is no second beam size to compare against and nothing left for
        // the number to mean.

        /// <summary>True when a point falls inside the beam at all.</summary>
        public bool Contains(Vec3 point)
        {
            return IsLit && MathX.InCone(Origin, Forward, point, RangeMetres, HalfAngleDegrees);
        }

        /// <summary>
        /// Light landing on a point, 0–1.
        /// <para>
        /// Falls off linearly to zero at the edge of the cone in both directions — with
        /// distance, and with angle off the axis. Both terms are normalised by the beam's
        /// own numbers, so this introduces no tuned value of its own: the only inputs are
        /// §03's <see cref="GameConstants.FlashlightRange"/> and
        /// <see cref="GameConstants.FlashlightHalfAngle"/> as they arrive through the
        /// cone, and re-tuning either moves the readable envelope with it.
        /// </para>
        /// <para>
        /// The angular term is what makes "빛이 좁다" a real obstacle: the useful part of
        /// the beam is a good deal narrower than its nominal half-angle, so a runner has
        /// to aim the torch at the gate they are running for rather than stand in the
        /// corridor facing roughly the right way.
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
