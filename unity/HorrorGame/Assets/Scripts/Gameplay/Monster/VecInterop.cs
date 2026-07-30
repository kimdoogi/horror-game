#nullable enable

using HorrorGame.Core.Math;
using UnityEngine;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// Translates between Core's <see cref="Vec3"/> and <see cref="Vector3"/>.
    /// <para>
    /// ARCHITECTURE §1 forbids Core from referencing <c>UnityEngine</c>, so
    /// <see cref="HorrorGame.Core.Monster.MonsterBrain"/> speaks <see cref="Vec3"/>
    /// while the <see cref="UnityEngine.AI.NavMeshAgent"/> driving the same creature
    /// speaks <see cref="Vector3"/>. Both are Y-up, Z-forward and left-handed, so the
    /// conversion is a component copy — and stating that once, here, is what keeps a
    /// mistaken axis swap from becoming a bug that only shows up as a monster walking
    /// sideways into a wall.
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
