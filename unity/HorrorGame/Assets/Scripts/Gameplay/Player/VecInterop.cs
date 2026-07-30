#nullable enable

using HorrorGame.Core.Math;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Translates between Core's <see cref="Vec3"/> and <see cref="Vector3"/>.
    /// <para>
    /// ARCHITECTURE §1 forbids Core from referencing <c>UnityEngine</c>, so the rules
    /// speak <see cref="Vec3"/> and the engine speaks <see cref="Vector3"/> and exactly
    /// one place is allowed to know they are the same three floats. Keeping the
    /// conversion here — rather than writing <c>new Vector3(v.X, v.Y, v.Z)</c> at each
    /// call site — means a future change to either type is a single edit rather than a
    /// hunt, and it keeps the axis convention (both are Y-up, Z-forward, left-handed)
    /// stated once.
    /// </para>
    /// </summary>
    internal static class VecInterop
    {
        /// <summary>A rules vector as an engine vector.</summary>
        internal static Vector3 ToVector3(this Vec3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        /// <summary>An engine vector as a rules vector.</summary>
        internal static Vec3 ToVec3(this Vector3 value)
        {
            return new Vec3(value.x, value.y, value.z);
        }
    }
}
