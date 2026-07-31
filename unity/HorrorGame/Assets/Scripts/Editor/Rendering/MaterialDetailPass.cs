#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Rendering
{
    /// <summary>
    /// Binds the shared micro-normals from <c>tools/textures/gen_textures.py</c> onto
    /// the generated materials, which is the near-field detail the base maps cannot
    /// carry.
    /// <para>
    /// <b>Why this file exists separately.</b> ART.md §7.9 recorded detail normals as
    /// unreachable: the binder that creates these materials sets <c>_BaseMap</c>,
    /// <c>_BumpMap</c>, <c>_OcclusionMap</c> and <c>_MetallicGlossMap</c> and nothing
    /// else, and it belongs to another area. The materials themselves are a generated
    /// artefact, so a second pass over them is a legitimate way in — and it keeps the
    /// two concerns apart, because binding a detail map is a *rendering* decision
    /// about what the surface looks like from one metre away, not a decision about
    /// which texture goes on which kit slot.
    /// </para>
    /// <para>
    /// <b>Why it is needed at all, in numbers.</b> The base maps run 410–683 texels
    /// per metre, so their finest honest feature is 3–5 mm. §05 puts the camera at
    /// 1.63 m looking down at a floor about a metre away, and at one metre a
    /// 1920-wide frame at 90° FOV resolves roughly 1220 px/m — twice what the base
    /// map holds. Everything between 0.3 mm and 4 mm is missing from the surface the
    /// player spends the whole match looking at. One 512² normal repeated every 25–45
    /// cm restores that band for about 0.3 MB per family.
    /// </para>
    /// <para>
    /// Idempotent, and safe to run before or after
    /// <c>ProceduralTextureMaterials.Build</c> — it only ever adds properties that
    /// binder does not touch. It is called from <see cref="AtmosphereSetup.Configure"/>
    /// so a regenerated map cannot end up with the detail silently missing.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -nographics -projectPath . \
    ///   -executeMethod HorrorGame.EditorTools.Rendering.MaterialDetailPass.Batch
    /// </code>
    /// </summary>
    public static class MaterialDetailPass
    {
        private const string TextureRoot = "Assets/Textures";
        private const string ManifestPath = TextureRoot + "/Textures.manifest.json";

        /// <summary>
        /// Where the two binders put their materials. The five §12 floors live under
        /// the scene's own folder because the generated scene references them by GUID;
        /// everything else lives beside the textures. Both are searched rather than
        /// derived, so a material that moves is reported instead of skipped.
        /// </summary>
        private static readonly string[] MaterialRoots =
        {
            "Assets/Scenes/Generated/Materials",
            TextureRoot + "/Materials",
        };

        /// <summary>
        /// UV units the MapKit spans per metre of world — the same number, with the
        /// same caveat, as the one in the texture generator.
        /// <para>
        /// The manifest's <c>size_metres</c> is emitted already multiplied by the
        /// kit's real 0.5 UV/m, because the pre-existing binder divides its own
        /// constant of 1 into that field and would otherwise render everything at
        /// double scale. This file follows the same convention deliberately: if
        /// anyone ever corrects <c>ProceduralTextureMaterials.KitUvUnitsPerMetre</c>
        /// to 0.5, the generator's <c>KIT_UV_UNITS_PER_METRE</c> goes to 1.0 and both
        /// binders stay right. Correcting one alone doubles the error instead of
        /// fixing it.
        /// </para>
        /// </summary>
        private const float KitUvUnitsPerMetre = 1f;

        /// <summary>Binds every detail map named by the manifest. Menu entry point.</summary>
        [MenuItem("HorrorGame/Textures/Bind Detail Normals", priority = 21)]
        public static void Menu()
        {
            Debug.Log("[Detail] " + Apply());
        }

        /// <summary>Batch entry point. Exits non-zero if the manifest or a map is missing.</summary>
        public static void Batch()
        {
            try
            {
                Debug.Log("[Detail] " + Apply());
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Detail] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Applies every detail binding the manifest declares and returns a report.
        /// Throws if the manifest names a map that is not on disk — a missing detail
        /// map renders identically to a bound one, so it has to fail loudly.
        /// </summary>
        public static string Apply()
        {
            if (!File.Exists(ManifestPath))
            {
                throw new FileNotFoundException(
                    "No " + ManifestPath + ". Run tools/textures/gen_textures.py first.");
            }

            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest?.materials == null || manifest.materials.Length == 0)
            {
                throw new InvalidOperationException("The texture manifest has no materials in it.");
            }

            var bound = new List<string>();
            var skipped = new List<string>();

            foreach (var entry in manifest.materials)
            {
                var material = FindMaterial(entry.name);
                if (material == null)
                {
                    skipped.Add(entry.name + " (no material built yet)");
                    continue;
                }

                // A material that *loses* its detail in the manifest has to lose it on
                // the material too. Without this the declaration is one-way: removing a
                // detail map costs nothing, changes nothing, and the surface goes on
                // paying for two extra texture samples per pixel forever.
                if (entry.detail == null || string.IsNullOrEmpty(entry.detail.normal))
                {
                    if (material.IsKeywordEnabled("_DETAIL_MULX2")
                        || material.GetTexture("_DetailNormalMap") != null)
                    {
                        material.SetTexture("_DetailNormalMap", null);
                        material.DisableKeyword("_DETAIL_MULX2");
                        material.DisableKeyword("_DETAIL_SCALED");
                        EditorUtility.SetDirty(material);
                        skipped.Add(entry.name + " (detail cleared)");
                    }
                    else
                    {
                        skipped.Add(entry.name + " (none declared)");
                    }

                    continue;
                }

                var path = TextureRoot + "/" + entry.detail.normal;
                var normal = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (normal == null)
                {
                    throw new FileNotFoundException(
                        "Manifest names detail map " + path + " and it is not imported. A missing "
                        + "detail map is invisible rather than an error, so this fails instead.");
                }

                bound.Add(Bind(material, normal, entry.detail));
            }

            AssetDatabase.SaveAssets();

            var report = "bound " + bound.Count + " detail normal(s):\n  " + string.Join("\n  ", bound);
            if (skipped.Count > 0)
            {
                report += "\n  skipped: " + string.Join(", ", skipped);
            }

            return report;
        }

        /// <summary>
        /// Sets the four properties and the one keyword URP's Lit shader needs.
        /// <para>
        /// The keyword is the part that silently does nothing when it is missing —
        /// exactly the trap the base binder documents for <c>_NORMALMAP</c>. URP
        /// compiles the entire detail block out unless <c>_DETAIL_MULX2</c> or
        /// <c>_DETAIL_SCALED</c> is on, so the map binds, the inspector shows it, and
        /// the surface renders as though it were never assigned.
        /// </para>
        /// <para>
        /// The detail UVs come from <c>_DetailAlbedoMap</c>'s tiling even when only
        /// the normal is bound — that is URP's layout, not a choice here — so the
        /// scale is written to that property. No detail albedo is assigned: its
        /// default is linear grey, which <c>_DETAIL_MULX2</c> doubles to exactly 1
        /// and leaves the calibrated albedo untouched. The whole point is relief,
        /// and modulating albedo here would undo the generator's calibration.
        /// </para>
        /// </summary>
        private static string Bind(Material material, Texture normal, DetailEntry detail)
        {
            var size = detail.size_metres > 0.001f ? detail.size_metres : 0.15f;
            var scale = new Vector2(KitUvUnitsPerMetre / size, KitUvUnitsPerMetre / size);

            material.SetTexture("_DetailNormalMap", normal);
            material.SetFloat("_DetailNormalMapScale", 1f);
            material.SetFloat("_DetailAlbedoMapScale", 1f);
            material.SetTextureScale("_DetailAlbedoMap", scale);
            material.SetTextureOffset("_DetailAlbedoMap", Vector2.zero);
            material.EnableKeyword("_DETAIL_MULX2");
            material.DisableKeyword("_DETAIL_SCALED");
            EditorUtility.SetDirty(material);

            var perMetre = scale.x * 0.5f;
            return material.name + " ← " + detail.name
                   + "  repeat every " + (1f / Mathf.Max(perMetre, 1e-4f)).ToString("0.00") + " m";
        }

        private static Material? FindMaterial(string name)
        {
            foreach (var root in MaterialRoots)
            {
                var path = root + "/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null)
                {
                    return material;
                }
            }

            return null;
        }

        // JsonUtility needs concrete serialisable types and ignores every field it
        // has no member for, so these mirror only the part of the manifest this pass
        // reads. `detail` is written as `{}` for a material with none, which lands
        // here as an object with empty strings rather than as null.
#pragma warning disable CS8618
        [Serializable]
        private sealed class Manifest
        {
            public MaterialEntry[] materials;
        }

        [Serializable]
        private sealed class MaterialEntry
        {
            public string name;
            public DetailEntry detail;
        }

        [Serializable]
        private sealed class DetailEntry
        {
            public string name;
            public string normal;
            public float size_metres;
            public float authored_metres_per_tile;
        }
#pragma warning restore CS8618
    }
}
