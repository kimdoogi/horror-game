#nullable enable

using HorrorGame.Core.Math;
using Mirror;

namespace HorrorGame.Net
{
    /// <summary>
    /// Packs a camera's yaw and pitch into two bytes each.
    /// <para>
    /// §05 is explicit that both angles have to travel: "카메라 회전 (yaw + pitch)
    /// — 손전등 방향 = 정보. pitch까지 필요 — 바닥·천장을 비추는 것도 신호." A
    /// teammate sweeping the floor and a teammate sweeping the ceiling are saying
    /// two different things, and pitch is the only thing that distinguishes them.
    /// </para>
    /// <para>
    /// <b>The numbers here are not tuned values.</b> ARCHITECTURE §2 puts every
    /// tuned number in <c>GameConstants</c>; a quantiser's domain is not one. 360°
    /// of yaw and ±90° of pitch are the full range a camera can physically express,
    /// chosen so nothing is representable but unsendable. No design section could
    /// change them without changing what a camera is.
    /// </para>
    /// </summary>
    public static class NetViewCompression
    {
        /// <summary>Yaw wraps at a full turn. The wire covers all of it.</summary>
        public const float YawMinDegrees = 0f;

        /// <summary>Exclusive in spirit — 360 and 0 quantise to neighbouring steps, which is correct for a wrap.</summary>
        public const float YawMaxDegrees = 360f;

        /// <summary>Straight down.</summary>
        public const float PitchMinDegrees = -90f;

        /// <summary>Straight up.</summary>
        public const float PitchMaxDegrees = 90f;

        /// <summary>
        /// Resolution of both angles, in steps. A <c>ushort</c> gives ~0.0055° of
        /// yaw and ~0.0027° of pitch — far below what a player can aim, so the
        /// flashlight cone a teammate sees is the one its owner is pointing.
        /// </summary>
        public const ushort Steps = ushort.MaxValue;

        /// <summary>Wraps an angle into [0, 360).</summary>
        public static float NormalizeYaw(float degrees)
        {
            var wrapped = degrees % YawMaxDegrees;
            return wrapped < 0f ? wrapped + YawMaxDegrees : wrapped;
        }

        /// <summary>Clamps pitch to what a head can do. A camera past vertical is a bug upstream, not a signal.</summary>
        public static float ClampPitch(float degrees) =>
            MathX.Clamp(degrees, PitchMinDegrees, PitchMaxDegrees);

        /// <summary>Packs yaw for the wire.</summary>
        public static ushort PackYaw(float degrees) =>
            Compression.ScaleFloatToUShort(NormalizeYaw(degrees), YawMinDegrees, YawMaxDegrees, 0, Steps);

        /// <summary>Unpacks yaw.</summary>
        public static float UnpackYaw(ushort packed) =>
            Compression.ScaleUShortToFloat(packed, 0, Steps, YawMinDegrees, YawMaxDegrees);

        /// <summary>Packs pitch for the wire.</summary>
        public static ushort PackPitch(float degrees) =>
            Compression.ScaleFloatToUShort(ClampPitch(degrees), PitchMinDegrees, PitchMaxDegrees, 0, Steps);

        /// <summary>Unpacks pitch.</summary>
        public static float UnpackPitch(ushort packed) =>
            Compression.ScaleUShortToFloat(packed, 0, Steps, PitchMinDegrees, PitchMaxDegrees);
    }
}
