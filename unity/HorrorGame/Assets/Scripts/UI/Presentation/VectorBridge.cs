#nullable enable

using HorrorGame.Core.Math;
using UnityEngine;

namespace HorrorGame.UI
{
    /// <summary>
    /// Converts between the core's <see cref="Vec3"/> and Unity's <c>Vector3</c>.
    /// <para>
    /// Core cannot reference <c>UnityEngine</c> at all (ARCHITECTURE §1), so the
    /// conversion has to live on this side of the line. Keeping it in one place means
    /// the axis convention — X right, Y up, Z forward, identical in both — is stated
    /// once rather than assumed at every call site.
    /// </para>
    /// </summary>
    public static class VectorBridge
    {
        /// <summary>A core position as a Unity one.</summary>
        public static Vector3 ToUnity(this Vec3 v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }

        /// <summary>A Unity position as a core one.</summary>
        public static Vec3 ToCore(this Vector3 v)
        {
            return new Vec3(v.x, v.y, v.z);
        }
    }
}
