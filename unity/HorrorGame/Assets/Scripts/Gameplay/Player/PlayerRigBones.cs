#nullable enable

using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Finds the attachment bones <c>Player.fbx</c> carries for §05 and §03.
    /// <para>
    /// The rig has four non-standard bones — <c>HeadCameraAnchor</c>,
    /// <c>FlashlightMount</c>, <c>ObjectiveMount</c>, <c>BackpackMount</c> — which are
    /// not part of Unity's humanoid map and therefore cannot be reached through
    /// <see cref="Animator.GetBoneTransform"/>. They have to be found by name.
    /// </para>
    /// <para>
    /// Name lookup is fragile, so the failure is loud: a missing mount means the
    /// flashlight is welded to the player's navel and §05's "손전등이 포인터가 된다" —
    /// other players reading a beam's direction — quietly stops being information.
    /// That is a bug nobody would file, so it is logged rather than tolerated.
    /// </para>
    /// </summary>
    public static class PlayerRigBones
    {
        /// <summary>The camera's eye position on the rig. §05 puts the view here, not at an arbitrary height.</summary>
        public const string HeadCameraAnchor = "HeadCameraAnchor";

        /// <summary>Where the flashlight hangs. §05: the beam is a pointing device other players read.</summary>
        public const string FlashlightMount = "FlashlightMount";

        /// <summary>Where the §03 objective sits while carried. Both hands are on it.</summary>
        public const string ObjectiveMount = "ObjectiveMount";

        /// <summary>Where §08's bag sits.</summary>
        public const string BackpackMount = "BackpackMount";

        /// <summary>
        /// Depth-first search for a descendant with this exact name, including
        /// <paramref name="root"/> itself.
        /// </summary>
        /// <param name="root">Where to start. Null returns null rather than throwing — an
        /// unassigned rig is a wiring mistake to report, not an exception to catch.</param>
        /// <param name="boneName">Exact transform name; the rig's names are stable and case-sensitive.</param>
        public static Transform? Find(Transform? root, string boneName)
        {
            if (root == null || string.IsNullOrEmpty(boneName))
            {
                return null;
            }

            if (root.name == boneName)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = Find(root.GetChild(i), boneName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// <see cref="Find"/> with a warning when the bone is absent, naming the owner so
        /// the message points at the prefab rather than at this file.
        /// </summary>
        /// <param name="root">Where to start.</param>
        /// <param name="boneName">Exact transform name.</param>
        /// <param name="owner">Component to blame in the log line.</param>
        public static Transform? FindOrWarn(Transform? root, string boneName, UnityEngine.Object owner)
        {
            var found = Find(root, boneName);
            if (found == null)
            {
                Debug.LogWarning(
                    "[Player] Bone '" + boneName + "' not found under "
                    + (root == null ? "(no rig assigned)" : root.name)
                    + ". Player.fbx must be imported with Optimize Game Objects off, or the "
                    + "avatar mapping drops the four §05/§03 attachment points.",
                    owner);
            }

            return found;
        }
    }
}
