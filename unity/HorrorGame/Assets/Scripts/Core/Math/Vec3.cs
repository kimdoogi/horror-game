using System;
using System.Globalization;

namespace HorrorGame.Core.Math
{
    /// <summary>
    /// A three-component vector with no engine dependency.
    /// <para>
    /// The core assembly cannot reference <c>UnityEngine.Vector3</c> — that is the
    /// whole point of keeping the rules engine-agnostic, and it is what lets the
    /// balance tests and the headless simulator run outside Unity. The Unity
    /// adapter layer converts at the boundary with a pair of extension methods.
    /// </para>
    /// <para>
    /// Coordinates follow Unity's convention so the conversion is a straight
    /// component copy: X right, Y up, Z forward, left-handed.
    /// </para>
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        /// <summary>Right.</summary>
        public readonly float X;

        /// <summary>Up.</summary>
        public readonly float Y;

        /// <summary>Forward.</summary>
        public readonly float Z;

        /// <summary>Creates a vector from its components.</summary>
        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>The origin.</summary>
        public static Vec3 Zero => new Vec3(0f, 0f, 0f);

        /// <summary>Unit vector along +X.</summary>
        public static Vec3 Right => new Vec3(1f, 0f, 0f);

        /// <summary>Unit vector along +Y.</summary>
        public static Vec3 Up => new Vec3(0f, 1f, 0f);

        /// <summary>Unit vector along +Z.</summary>
        public static Vec3 Forward => new Vec3(0f, 0f, 1f);

        /// <summary>Squared length. Prefer this over <see cref="Magnitude"/> when comparing distances.</summary>
        public float SqrMagnitude => (X * X) + (Y * Y) + (Z * Z);

        /// <summary>Length.</summary>
        public float Magnitude => MathF.Sqrt(SqrMagnitude);

        /// <summary>
        /// Squared length ignoring <see cref="Y"/>. Most gameplay distances —
        /// aggro release, voice cutoff, Observer range — are horizontal, so
        /// standing on a stairwell must not read as extra distance.
        /// </summary>
        public float SqrMagnitudeFlat => (X * X) + (Z * Z);

        /// <summary>Length ignoring <see cref="Y"/>.</summary>
        public float MagnitudeFlat => MathF.Sqrt(SqrMagnitudeFlat);

        /// <summary>This vector scaled to unit length, or <see cref="Zero"/> if it is degenerate.</summary>
        public Vec3 Normalized
        {
            get
            {
                var m = Magnitude;
                return m > 1e-6f ? new Vec3(X / m, Y / m, Z / m) : Zero;
            }
        }

        /// <summary>This vector flattened onto the XZ plane and scaled to unit length.</summary>
        public Vec3 NormalizedFlat
        {
            get
            {
                var m = MagnitudeFlat;
                return m > 1e-6f ? new Vec3(X / m, 0f, Z / m) : Zero;
            }
        }

        /// <summary>This vector with <see cref="Y"/> zeroed.</summary>
        public Vec3 Flat => new Vec3(X, 0f, Z);

        /// <summary>Component-wise sum.</summary>
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        /// <summary>Component-wise difference.</summary>
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        /// <summary>Negation.</summary>
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);

        /// <summary>Scalar multiply.</summary>
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        /// <summary>Scalar multiply.</summary>
        public static Vec3 operator *(float s, Vec3 a) => a * s;

        /// <summary>Scalar divide.</summary>
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        /// <summary>Dot product.</summary>
        public static float Dot(Vec3 a, Vec3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        /// <summary>Cross product.</summary>
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));

        /// <summary>Distance between two points.</summary>
        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;

        /// <summary>
        /// Horizontal distance between two points. This is the distance every
        /// gameplay range check should use — see <see cref="SqrMagnitudeFlat"/>.
        /// </summary>
        public static float DistanceFlat(Vec3 a, Vec3 b) => (a - b).MagnitudeFlat;

        /// <summary>Linear interpolation, clamping <paramref name="t"/> to [0, 1].</summary>
        public static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            t = MathX.Clamp01(t);
            return new Vec3(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t));
        }

        /// <summary>Moves <paramref name="from"/> toward <paramref name="to"/> by at most <paramref name="maxDelta"/>.</summary>
        public static Vec3 MoveTowards(Vec3 from, Vec3 to, float maxDelta)
        {
            var delta = to - from;
            var dist = delta.Magnitude;
            if (dist <= maxDelta || dist < 1e-6f)
            {
                return to;
            }

            return from + (delta / dist * maxDelta);
        }

        /// <summary>This vector clamped to at most <paramref name="maxLength"/>.</summary>
        public Vec3 ClampMagnitude(float maxLength)
        {
            var sqr = SqrMagnitude;
            if (sqr <= maxLength * maxLength)
            {
                return this;
            }

            return Normalized * maxLength;
        }

        /// <inheritdoc />
        public bool Equals(Vec3 other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Vec3 other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        /// <summary>Exact component equality. For tolerant comparison use <see cref="MathX.Approximately(float, float, float)"/>.</summary>
        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);

        /// <summary>Exact component inequality.</summary>
        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", X, Y, Z);
    }
}
