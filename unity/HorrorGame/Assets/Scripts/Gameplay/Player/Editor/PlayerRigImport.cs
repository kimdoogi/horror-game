#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.Gameplay.PlayerEditor
{
    /// <summary>
    /// Re-derives <c>Player.fbx</c>'s Humanoid avatar from the skeleton actually in the file,
    /// and reports which bones the mapping found.
    /// <para>
    /// <b>Why this has to exist.</b> A model imported as Humanoid stores its bone-to-human
    /// mapping <em>in the .meta</em>, and Unity keeps that stored mapping across re-imports —
    /// it matches by bone name and silently ignores anything the stored description does not
    /// mention. So when <c>gen_player_ai.py</c> added twenty finger bones to a rig whose meta
    /// listed twenty-two, the fingers arrived in the hierarchy, deformed the mesh correctly in
    /// the bind pose, and had **every one of their animation curves dropped**: a Humanoid clip
    /// is muscle-space, and a bone outside the avatar has no muscles. The hands would have
    /// frozen half-open in all nine clips with nothing logged anywhere.
    /// </para>
    /// <para>
    /// Clearing the description and re-importing makes Unity run its own auto-mapper over the
    /// skeleton in the file, which is the only thing that knows what is in there. The report
    /// is the point as much as the fix: §03 asks a player to tell four carry states apart
    /// from their hands alone, and this prints whether the ten bones per hand that carry
    /// those states are mapped.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -nographics -projectPath unity/HorrorGame -executeMethod \
    ///   HorrorGame.Gameplay.PlayerEditor.PlayerRigImport.RemapBatch
    /// </code>
    /// </summary>
    public static class PlayerRigImport
    {
        /// <summary>The model this operates on. One player, one path — see gen_player_ai.py.</summary>
        public const string PlayerModelPath = "Assets/Models/Characters/Player.fbx";

        /// <summary>Batch entry point. Exits 0 when every finger bone is mapped, 1 otherwise.</summary>
        public static void RemapBatch()
        {
            try
            {
                var mapped = Remap(PlayerModelPath);
                EditorApplication.Exit(mapped ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PlayerRigImport] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Menu twin of <see cref="RemapBatch"/>, for when the editor is open.</summary>
        [MenuItem("HorrorGame/Player/Re-derive Humanoid Avatar")]
        public static void RemapMenu()
        {
            Remap(PlayerModelPath);
        }

        /// <summary>
        /// Clears the stored human description, re-imports, and reports the result.
        /// </summary>
        /// <param name="path">Project-relative path to the model.</param>
        /// <returns>True when every finger bone in the file reached the avatar.</returns>
        public static bool Remap(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[PlayerRigImport] No ModelImporter at " + path);
                return false;
            }

            // Cleared, not edited. Unity's auto-mapper runs when the description is empty and
            // the rig is Humanoid; leaving the old twenty-two entries in place is exactly what
            // caused the fingers to be dropped, and adding twenty more by hand would put this
            // file in the business of knowing the skeleton, which is the generator's job.
            var description = importer.humanDescription;
            description.human = Array.Empty<HumanBone>();
            description.skeleton = Array.Empty<SkeletonBone>();
            importer.humanDescription = description;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            // §05's four mount bones are not humanoid bones, so the avatar does not protect
            // them; optimising the hierarchy would delete them and the camera would end up at
            // the rig's origin. AssetImportValidator checks this too — it is restated here
            // because this method is the one that rewrites the importer.
            importer.optimizeGameObjects = false;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            return Report(path);
        }

        /// <summary>
        /// Prints what the re-derived avatar mapped, and whether the fingers survived.
        /// </summary>
        private static bool Report(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var human = importer.humanDescription.human ?? Array.Empty<HumanBone>();
            var byHuman = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var bone in human)
            {
                byHuman[bone.humanName] = bone.boneName;
            }

            var fingers = human.Where(b => IsFinger(b.humanName)).ToArray();
            var missing = ExpectedFingerBones()
                .Where(name => !byHuman.Values.Contains(name, StringComparer.Ordinal))
                .ToArray();

            Debug.Log(
                "[PlayerRigImport] " + path + ": avatar maps " + human.Length + " human bone(s), "
                + fingers.Length + " of them fingers.\n  fingers: "
                + string.Join(", ", fingers.Select(b => b.humanName + "=" + b.boneName)));

            if (missing.Length > 0)
            {
                Debug.LogError(
                    "[PlayerRigImport] " + missing.Length + " finger bone(s) in Player.fbx did not "
                    + "reach the avatar: " + string.Join(", ", missing) + ". A Humanoid clip is "
                    + "muscle-space, so their curves are dropped and the hands play frozen — "
                    + "which takes §03's four carry states down to one shape.");
                return false;
            }

            return true;
        }

        /// <summary>The twenty bone names gen_player_ai.py writes, in Unity's own spelling.</summary>
        private static IEnumerable<string> ExpectedFingerBones()
        {
            foreach (var side in new[] { "Left", "Right" })
            {
                foreach (var digit in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    yield return side + digit + "Proximal";
                    yield return side + digit + "Intermediate";
                }
            }
        }

        private static bool IsFinger(string humanName)
        {
            return humanName.Contains("Thumb", StringComparison.Ordinal)
                || humanName.Contains("Index", StringComparison.Ordinal)
                || humanName.Contains("Middle", StringComparison.Ordinal)
                || humanName.Contains("Ring", StringComparison.Ordinal)
                || humanName.Contains("Little", StringComparison.Ordinal);
        }
    }
}
