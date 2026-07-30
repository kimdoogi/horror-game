using System;

namespace HorrorGame.Core.Math
{
    /// <summary>
    /// Scalar helpers the core needs but cannot borrow from <c>UnityEngine.Mathf</c>.
    /// </summary>
    public static class MathX
    {
        /// <summary>Tolerance used by <see cref="Approximately(float, float)"/>. Chosen for metres and seconds, not for unit-scale values.</summary>
        public const float Epsilon = 1e-4f;

        /// <summary>Degrees per radian.</summary>
        public const float Rad2Deg = 57.29578f;

        /// <summary>Radians per degree.</summary>
        public const float Deg2Rad = 0.01745329f;

        /// <summary>Clamps <paramref name="v"/> to [0, 1].</summary>
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>Clamps <paramref name="v"/> to [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        /// <summary>Clamps <paramref name="v"/> to [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        /// <summary>Linear interpolation with <paramref name="t"/> clamped to [0, 1].</summary>
        public static float Lerp(float a, float b, float t) => a + ((b - a) * Clamp01(t));

        /// <summary>Linear interpolation without clamping.</summary>
        public static float LerpUnclamped(float a, float b, float t) => a + ((b - a) * t);

        /// <summary>
        /// Where <paramref name="v"/> falls between <paramref name="a"/> and
        /// <paramref name="b"/>, as a value in [0, 1]. Returns 0 for a degenerate range.
        /// </summary>
        public static float InverseLerp(float a, float b, float v)
        {
            if (System.Math.Abs(b - a) < 1e-9f)
            {
                return 0f;
            }

            return Clamp01((v - a) / (b - a));
        }

        /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="maxDelta"/>.</summary>
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (System.Math.Abs(target - current) <= maxDelta)
            {
                return target;
            }

            return current + (System.Math.Sign(target - current) * maxDelta);
        }

        /// <summary>True when the two values differ by less than <see cref="Epsilon"/>.</summary>
        public static bool Approximately(float a, float b) => System.Math.Abs(a - b) < Epsilon;

        /// <summary>True when the two values differ by less than <paramref name="tolerance"/>.</summary>
        public static bool Approximately(float a, float b, float tolerance) => System.Math.Abs(a - b) < tolerance;

        /// <summary>Wraps an angle in degrees to (-180, 180].</summary>
        public static float NormalizeAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
            {
                degrees -= 360f;
            }
            else if (degrees <= -180f)
            {
                degrees += 360f;
            }

            return degrees;
        }

        /// <summary>Shortest signed rotation in degrees from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static float DeltaAngle(float from, float to) => NormalizeAngle(to - from);

        /// <summary>
        /// Compass yaw in degrees of a direction, measured clockwise from +Z.
        /// Matches Unity's Y-axis Euler rotation so the value can be handed
        /// straight to a transform.
        /// </summary>
        public static float YawOf(Vec3 direction) => MathF.Atan2(direction.X, direction.Z) * Rad2Deg;

        /// <summary>Unit direction on the XZ plane for a compass yaw in degrees.</summary>
        public static Vec3 DirectionFromYaw(float yawDegrees)
        {
            var r = yawDegrees * Deg2Rad;
            return new Vec3(MathF.Sin(r), 0f, MathF.Cos(r));
        }

        /// <summary>
        /// Unsigned angle in degrees between two directions. Returns 0 if either
        /// is degenerate.
        /// </summary>
        public static float AngleBetween(Vec3 a, Vec3 b)
        {
            var denom = a.Magnitude * b.Magnitude;
            if (denom < 1e-6f)
            {
                return 0f;
            }

            return MathF.Acos(Clamp(Vec3.Dot(a, b) / denom, -1f, 1f)) * Rad2Deg;
        }

        /// <summary>
        /// True when <paramref name="target"/> lies inside a cone of half-angle
        /// <paramref name="halfAngleDegrees"/> around <paramref name="forward"/>,
        /// within <paramref name="range"/>. Used for the flashlight cone, the
        /// flash stun and the monster's vision.
        /// </summary>
        public static bool InCone(Vec3 origin, Vec3 forward, Vec3 target, float range, float halfAngleDegrees)
        {
            var to = target - origin;
            if (to.SqrMagnitude > range * range)
            {
                return false;
            }

            return AngleBetween(forward, to) <= halfAngleDegrees;
        }

        /// <summary>Smoothstep easing of <paramref name="t"/> over [0, 1].</summary>
        public static float SmoothStep01(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - (2f * t));
        }
    }
}
