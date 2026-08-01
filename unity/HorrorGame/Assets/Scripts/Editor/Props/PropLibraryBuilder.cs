#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using HorrorGame.Gameplay.Interaction;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Props
{
    /// <summary>
    /// Fills <c>Assets/Resources/InteractableProps.asset</c> from
    /// <c>Assets/Models/Props</c>, so every interactable can find its model at runtime.
    /// <para>
    /// <b>This is the step that puts the models in the build.</b> A model nothing
    /// references is not shipped, and <c>Resources.Load</c> only reaches a
    /// <c>Resources/</c> folder — the props deliberately live outside one, under the
    /// path <c>AssetImportPolicy</c> governs and <c>gen_props.py</c> writes. One
    /// referencing asset inside <c>Resources/</c> resolves both.
    /// </para>
    /// <para>
    /// It refuses rather than half-succeeding: a library missing one entry is a match
    /// with one grey box in it and no failure anywhere, which is the shape of every
    /// defect this pass was opened for.
    /// </para>
    /// </summary>
    public static class PropLibraryBuilder
    {
        private const string ResourceFolder = "Assets/Resources";
        private const string LibraryPath = ResourceFolder + "/" + InteractablePropLibrary.ResourceName + ".asset";

        /// <summary>Builds the materials and then the library. One batch entry for the whole kit.</summary>
        public static void BuildAllBatch()
        {
            var ok = true;
            try
            {
                PropMaterials.BuildOnly();
                Build(out var report);
                Debug.Log("[Props] " + report);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Props] " + ex);
                ok = false;
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Builds or refreshes the library asset.</summary>
        [MenuItem("HorrorGame/Props/Build Interactable Prop Library", priority = 31)]
        public static void BuildMenu()
        {
            try
            {
                Build(out var report);
                Debug.Log("[Props] " + report);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Props] " + ex);
            }
        }

        /// <summary>Rebuilds the library and throws when it cannot be completed.</summary>
        public static void Build(out string report)
        {
            if (!AssetDatabase.IsValidFolder(ResourceFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var library = AssetDatabase.LoadAssetAtPath<InteractablePropLibrary>(LibraryPath);
            var created = library == null;
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<InteractablePropLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var entries = new List<InteractablePropLibrary.Entry>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { PropMaterials.PropRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                {
                    continue;
                }

                entries.Add(new InteractablePropLibrary.Entry
                {
                    key = System.IO.Path.GetFileNameWithoutExtension(path),
                    model = model,
                });
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            library.SetEntries(entries.ToArray());

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var problems = library.Validate(PropModels.Required);
            if (!string.IsNullOrEmpty(problems))
            {
                throw new InvalidOperationException(
                    "The interactable prop library is incomplete. Every one of these is a §03 or §08 "
                    + "object that would spawn as a grey stand-in:\n" + problems
                    + "  Regenerate the kit: blender --background --factory-startup --python "
                    + "tools/blender/gen_props.py");
            }

            report = (created ? "Created " : "Refreshed ") + LibraryPath + " with " + entries.Count
                + " model(s); all " + PropModels.Required.Count + " required keys resolve ("
                + string.Join(", ", PropModels.Required.Take(4)) + ", …).";
        }
    }
}
