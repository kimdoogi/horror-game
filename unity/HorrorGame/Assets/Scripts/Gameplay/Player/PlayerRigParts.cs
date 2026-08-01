#nullable enable

using System;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Which part of the player a renderer belongs to. §05 splits <c>Player.fbx</c> into
    /// three meshes because the owner must see one of them and must not see another.
    /// </summary>
    public enum PlayerRigPart
    {
        /// <summary>Not recognisable as part of the rig. Treated as body, which is the safe answer.</summary>
        Unknown = 0,

        /// <summary>Head, neck, torso, legs. Sits across the owner's near plane, so the owner never sees it drawn.</summary>
        Body = 1,

        /// <summary>Shoulders to fingertips. The one thing §05 promises the owner can see.</summary>
        Arms = 2,

        /// <summary>Something rigid in one hand — today, the flashlight. Drawn only while it is actually held.</summary>
        HandProp = 3,
    }

    /// <summary>
    /// Tells the three meshes of <c>Player.fbx</c> apart <b>by their material slots</b>, so
    /// the first-person policy in <see cref="PlayerFirstPersonView"/> never has to match a
    /// mesh name.
    /// <para>
    /// <b>Why material slots and not bone weights.</b> Bone weights were the first choice
    /// and they do not work, for a reason worth writing down: a Humanoid import binds the
    /// <em>whole avatar skeleton</em> to every skinned mesh in the file. Measured on this
    /// model — all three meshes arrive with <c>bones.Length == 26</c> and every one of them
    /// lists <c>Head</c>, including the torch, which is weighted to a single hand. The
    /// binding says what the mesh may be posed by, not what it is made of. Material slots
    /// do say what it is made of, and this project already treats material names as a
    /// contract: <c>blendkit.MaterialSpec</c> says so outright, the floor lookup matches
    /// <c>Floor_*</c> by name, and §04's role swap is defined as "slot 0 of the renderer".
    /// </para>
    /// <para>
    /// <b>Why not the mesh name.</b> A mesh called <c>Player_Arms</c> is a string in a
    /// Blender script that nothing else in the project reads, so nothing else breaks when
    /// it changes — which is exactly what makes it a bad key. The materials are read by the
    /// importer, by the role swap and by the texture manifest; a rename that broke this
    /// would break those first and loudly.
    /// </para>
    /// <para>
    /// <b>What survives a regeneration.</b> The three-way split is asserted in
    /// <c>gen_player_model.py</c> (<c>verify_mesh_split</c>) against the same three
    /// statements this class tests for: equipment lives on the body, skin and cloth on the
    /// arms, and a held item is made of gear and nothing else. A regeneration that violates
    /// one fails in Blender rather than arriving silently in Unity.
    /// </para>
    /// </summary>
    public static class PlayerRigParts
    {
        /// <summary>
        /// Belt, boots, knee pads, the shoulder radio — and the flashlight. Equipment is on
        /// the body and on things held in the hand, never on the arms themselves.
        /// </summary>
        public const string GearMaterial = "Player_Gear";

        /// <summary>Face and hands. Present on the body (head) and on the arms (hands), never on a held item.</summary>
        public const string SkinMaterial = "Player_Skin";

        /// <summary>Humanoid name of the left hand, used by callers that need the bone rather than the mesh.</summary>
        public const string LeftHandBone = "LeftHand";

        /// <summary>Humanoid name of the right hand — the one §05's flashlight hangs from.</summary>
        public const string RightHandBone = "RightHand";

        /// <summary>
        /// Decides which part a renderer is from the materials it carries.
        /// <para>
        /// The order of the tests is the safety argument. A held item is the narrowest case
        /// — gear and nothing else — and is checked first. Everything with gear on it after
        /// that is a body, because gear on a mesh that also has skin or cloth means boots
        /// and a belt, which means legs and a torso. Only a mesh with skin and no equipment
        /// on it at all is a pair of arms. An unrecognised renderer comes back
        /// <see cref="PlayerRigPart.Unknown"/> and <see cref="PlayerFirstPersonView"/>
        /// hides it: the failure mode is the old bug — no hands — never a torso drawn
        /// across the owner's view, which is a defect they cannot play around.
        /// </para>
        /// </summary>
        /// <param name="renderer">A renderer under the player rig. Null returns <see cref="PlayerRigPart.Unknown"/>.</param>
        public static PlayerRigPart Classify(Renderer? renderer)
        {
            if (renderer == null)
            {
                return PlayerRigPart.Unknown;
            }

            var materials = renderer.sharedMaterials;
            var hasGear = false;
            var hasSkin = false;
            var slots = 0;

            for (var i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                {
                    continue;
                }

                slots++;
                hasGear |= Is(materials[i], GearMaterial);
                hasSkin |= Is(materials[i], SkinMaterial);
            }

            if (slots == 0)
            {
                return PlayerRigPart.Unknown;
            }

            if (hasGear && slots == 1)
            {
                return PlayerRigPart.HandProp;
            }

            if (hasGear)
            {
                return PlayerRigPart.Body;
            }

            return hasSkin ? PlayerRigPart.Arms : PlayerRigPart.Unknown;
        }

        /// <summary>
        /// A one-line summary of what a renderer is made of, for the diagnostic
        /// <see cref="PlayerFirstPersonView"/> writes when the split does not survive an
        /// import. Names the materials the classification turns on rather than all of them.
        /// </summary>
        /// <param name="renderer">The renderer to describe. Null returns "none".</param>
        public static string DescribeBinding(Renderer? renderer)
        {
            if (renderer == null)
            {
                return "none";
            }

            var materials = renderer.sharedMaterials;
            var gear = false;
            var skin = false;

            for (var i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                {
                    continue;
                }

                gear |= Is(materials[i], GearMaterial);
                skin |= Is(materials[i], SkinMaterial);
            }

            return (gear ? "gear " : "") + (skin ? "skin " : "")
                + materials.Length + " slots -> " + Classify(renderer);
        }

        /// <summary>
        /// Name match tolerant of Unity's runtime "<c>ExampleName (Instance)</c>" suffix,
        /// which appears the moment anything touches <c>Renderer.materials</c> — §04's role
        /// swap will, so a plain equality here would work until the first role is assigned.
        /// </summary>
        private static bool Is(Material material, string expected)
        {
            var name = material.name;
            return string.Equals(name, expected, StringComparison.Ordinal)
                || name.StartsWith(expected + " (", StringComparison.Ordinal);
        }
    }
}
