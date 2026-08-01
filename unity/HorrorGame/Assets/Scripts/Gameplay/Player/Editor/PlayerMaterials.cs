#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.Gameplay.PlayerEditor
{
    /// <summary>
    /// Rebuilds the player's eight materials as URP Lit assets and binds them to
    /// <c>Player.fbx</c>.
    /// <para>
    /// <b>Without this the player is white.</b> FBX cannot carry a PBR material — the
    /// format has a Lambert/Phong slot and nothing else — so Unity imports eight embedded
    /// materials with a default albedo and no maps at all. <c>gen_player_ai.py</c> writes
    /// five 1024² maps per neutral surface into <c>Assets/Textures/Player_*</c> and
    /// <b>nothing in the project referenced any of them</b>: the coverall, the skin, the
    /// boots and all five §04 role colours rendered as the same flat white plastic. It is
    /// the same defect ART.md §7.11 recorded for the props — <i>"every object the player
    /// touches is an untextured white primitive"</i> — and it had never been recorded for
    /// the player, because the player is the one model nobody photographs from outside.
    /// </para>
    /// <para>
    /// <b>Every value comes from the manifest.</b> <c>Player.textures.json</c> carries what
    /// the Principled BSDF was actually authored with, including the five role RGBs, so
    /// §04's colour contract lives in the generator and not in two places. The tiling is
    /// derived the same way <c>gen_player_model.surface_tiling</c> derives it: a map painted
    /// at <c>world_size_metres</c> has to repeat <c>1 / (world_size × uv_units_per_metre)</c>
    /// times across an unwrap of that density, and left at 1 the coverall's weave stretches
    /// over two metres of cloth and reads as bare plastic.
    /// </para>
    /// <para>
    /// <b>The shader is pinned and a miss is fatal.</b> A material that lands on a built-in
    /// shader renders magenta in a URP build with no warning in the editor — §7.11's own
    /// lesson, and the reason the props shipped white for weeks.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -nographics -projectPath unity/HorrorGame -executeMethod \
    ///   HorrorGame.Gameplay.PlayerEditor.PlayerMaterials.BuildBatch
    /// </code>
    /// </summary>
    public static class PlayerMaterials
    {
        /// <summary>Written by <c>tools/blender/gen_player_ai.py</c>.</summary>
        public const string ManifestPath = "Assets/Textures/Player.textures.json";

        /// <summary>Where the maps live, one folder per material, named after it.</summary>
        public const string TextureRoot = "Assets/Textures";

        /// <summary>Where the built materials go. Beside the model they dress.</summary>
        public const string MaterialRoot = "Assets/Models/Characters/Materials";

        /// <summary>The model whose importer gets remapped onto them.</summary>
        public const string ModelPath = "Assets/Models/Characters/Player.fbx";

        /// <summary>
        /// §04's role colours must stay this far apart in Rec.709 luma. §05 makes the game
        /// dark and hue discrimination collapses at low light, so value is what separates
        /// one teammate from another down a 12 m beam. Asserted here as well as in the
        /// generator because this is the last place the numbers are still numbers.
        /// </summary>
        private const float MinimumRoleLumaGap = 0.05f;

        /// <summary>Batch entry point. Exits 0 on success, 1 on any failure.</summary>
        public static void BuildBatch()
        {
            try
            {
                EditorApplication.Exit(Build() ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PlayerMaterials] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Menu twin of <see cref="BuildBatch"/>.</summary>
        [MenuItem("HorrorGame/Player/Rebuild Player Materials")]
        public static void BuildMenu()
        {
            Build();
        }

        /// <summary>Builds every material and binds the model to them.</summary>
        /// <returns>True when all eight were built and remapped.</returns>
        public static bool Build()
        {
            var text = File.ReadAllText(
                Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, ManifestPath));
            var manifest = JsonUtility.FromJson<Manifest>(text);
            if (manifest == null || manifest.materials == null || manifest.roles == null)
            {
                Debug.LogError("[PlayerMaterials] " + ManifestPath + " has no materials or no "
                    + "roles. Re-run tools/blender/gen_player_ai.py.");
                return false;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[PlayerMaterials] URP/Lit is not in this project. A material "
                    + "built on a built-in shader renders magenta in a player and nothing warns.");
                return false;
            }

            Directory.CreateDirectory(
                Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, MaterialRoot));

            var built = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var entry in manifest.materials)
            {
                var material = BuildSurface(shader, entry, manifest.uv_units_per_metre);
                if (material == null)
                {
                    return false;
                }

                built[entry.name] = material;
            }

            if (!CheckRoleSeparation(manifest.roles))
            {
                return false;
            }

            foreach (var role in manifest.roles)
            {
                built[role.name] = BuildRole(shader, role);
            }

            AssetDatabase.SaveAssets();
            return Remap(built);
        }

        /// <summary>One textured surface: skin, coverall or gear.</summary>
        private static Material? BuildSurface(Shader shader, ManifestMaterial entry,
                                              float uvUnitsPerMetre)
        {
            var material = Load(shader, entry.name);

            // Linear, not sRGB. The project is a linear one (ART.md §3.7) and Unity's
            // SetColor takes the value through unchanged, so the generator's linear albedo
            // means the shader multiplies the map by 1 — the map already carries the colour.
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_Metallic", Mathf.Clamp01(entry.metallic_mean));
            material.SetFloat("_Smoothness", Mathf.Clamp01(1f - entry.roughness_mean));
            // Smoothness from the metallic map's ALPHA, which is where write_surface puts
            // it. Left on albedo-alpha (the default) the shader samples an opaque 1.0 and
            // every surface on the player becomes a mirror.
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.SetFloat("_SpecularHighlights", 1f);

            var maps = entry.maps;
            if (maps == null)
            {
                Debug.LogError("[PlayerMaterials] " + entry.name + " has no maps in the manifest.");
                return null;
            }

            var tiling = SurfaceTiling(entry.world_size_metres, uvUnitsPerMetre);
            if (!Bind(material, "_BaseMap", maps.albedo, srgb: true)
                || !Bind(material, "_BumpMap", maps.normal, srgb: false)
                || !Bind(material, "_MetallicGlossMap", maps.metallic_smoothness, srgb: false)
                || !Bind(material, "_OcclusionMap", maps.occlusion, srgb: false))
            {
                return null;
            }

            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_OcclusionStrength", 1f);
            material.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            material.SetTextureScale("_BumpMap", new Vector2(tiling, tiling));
            material.SetTextureScale("_MetallicGlossMap", new Vector2(tiling, tiling));
            material.SetTextureScale("_OcclusionMap", new Vector2(tiling, tiling));

            EditorUtility.SetDirty(material);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[PlayerMaterials] {0}: albedo {1:F3} lin, rough {2:F3}, tile x{3:F2} "
                + "({4:F2} m per repeat at {5:F3} uv/m)",
                entry.name, entry.albedo_mean_linear, entry.roughness_mean, tiling,
                entry.world_size_metres, uvUnitsPerMetre));
            return material;
        }

        /// <summary>One §04 role colour: flat, untextured, and deliberately so.</summary>
        private static Material BuildRole(Shader shader, ManifestRole role)
        {
            var material = Load(shader, role.name);
            var colour = new Color(role.base_color_linear[0], role.base_color_linear[1],
                                   role.base_color_linear[2], 1f);
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", Mathf.Clamp01(1f - role.roughness));
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Loads or creates the material asset for a slot name.</summary>
        private static Material Load(Shader shader, string name)
        {
            var path = MaterialRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        /// <summary>Assigns one map, failing loudly when it is missing rather than silently.</summary>
        private static bool Bind(Material material, string property, string relative, bool srgb)
        {
            if (string.IsNullOrEmpty(relative))
            {
                // Only occlusion is optional, and the manifest empties it when the map
                // carries no relief worth sampling.
                return property == "_OcclusionMap";
            }

            var path = TextureRoot + "/" + relative;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogError("[PlayerMaterials] missing map " + path
                    + " — the manifest names it and it is not in the project.");
                return false;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                var wanted = property == "_BumpMap"
                    ? TextureImporterType.NormalMap
                    : TextureImporterType.Default;
                if (importer.textureType != wanted || importer.sRGBTexture != srgb)
                {
                    importer.textureType = wanted;
                    importer.sRGBTexture = srgb;
                    importer.SaveAndReimport();
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
            }

            material.SetTexture(property, texture);
            return true;
        }

        /// <summary>
        /// How many times a map repeats across the unwrap. Mirrors
        /// <c>gen_player_model.surface_tiling</c>; the two must agree or the .glb and the
        /// game show different fabric.
        /// </summary>
        public static float SurfaceTiling(float worldSizeMetres, float uvUnitsPerMetre)
        {
            return 1f / Mathf.Max(worldSizeMetres * uvUnitsPerMetre, 1e-6f);
        }

        /// <summary>§05's value separation, checked on the numbers that will be rendered.</summary>
        private static bool CheckRoleSeparation(ManifestRole[] roles)
        {
            var sorted = roles.OrderBy(r => r.luma).ToArray();
            for (var i = 1; i < sorted.Length; i++)
            {
                var gap = sorted[i].luma - sorted[i - 1].luma;
                if (gap < MinimumRoleLumaGap)
                {
                    Debug.LogError(string.Format(CultureInfo.InvariantCulture,
                        "[PlayerMaterials] {0} and {1} are {2:F3} apart in luma, under {3:F2}. "
                        + "§05 makes the game dark, hue goes first, and two teammates the beam "
                        + "cannot tell apart is §04's whole role system gone.",
                        sorted[i - 1].name, sorted[i].name, gap, MinimumRoleLumaGap));
                    return false;
                }
            }

            Debug.Log("[PlayerMaterials] role luma "
                + string.Join(" < ", sorted.Select(r =>
                    string.Format(CultureInfo.InvariantCulture, "{0} {1:F3}", r.name, r.luma))));
            return true;
        }

        /// <summary>Points the model's material slots at the built assets.</summary>
        private static bool Remap(Dictionary<string, Material> built)
        {
            if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
            {
                Debug.LogError("[PlayerMaterials] no ModelImporter at " + ModelPath);
                return false;
            }

            // Import them, then override them. Leaving materialImportMode off would give the
            // renderer no slots to remap onto at all.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;

            var bound = 0;
            foreach (var pair in built)
            {
                var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key);
                importer.AddRemap(key, pair.Value);
                bound++;
            }

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            var missing = Missing(built);
            if (missing.Length > 0)
            {
                Debug.LogError("[PlayerMaterials] after the remap these renderer slots are still "
                    + "not one of the built materials: " + string.Join(", ", missing)
                    + ". A slot Unity did not match by name keeps its embedded white default.");
                return false;
            }

            Debug.Log("[PlayerMaterials] bound " + bound + " material(s) to " + ModelPath
                + "; every renderer slot resolves to a built asset.");
            return true;
        }

        /// <summary>Slots on the imported model that did not land on a built material.</summary>
        private static string[] Missing(Dictionary<string, Material> built)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                return new[] { "<the model would not load>" };
            }

            var names = new HashSet<string>(built.Values.Select(m => m.name), StringComparer.Ordinal);
            var stray = new List<string>();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        stray.Add(renderer.name + "/<null>");
                    }
                    else if (!names.Contains(material.name))
                    {
                        stray.Add(renderer.name + "/" + material.name);
                    }
                }
            }

            return stray.Distinct().ToArray();
        }

        // ── The manifest, in the shape JsonUtility can read ──────────────────

        [Serializable]
        private sealed class Manifest
        {
            public float uv_units_per_metre;
            public ManifestRole[]? roles;
            public ManifestMaterial[]? materials;
        }

        [Serializable]
        private sealed class ManifestRole
        {
            public string name = string.Empty;
            public float[] base_color_linear = new float[3];
            public float roughness;
            public float luma;
        }

        [Serializable]
        private sealed class ManifestMaterial
        {
            public string name = string.Empty;
            public float albedo_mean_linear;
            public float roughness_mean;
            public float metallic_mean;
            public float world_size_metres;
            public ManifestMaps? maps;
        }

        [Serializable]
        private sealed class ManifestMaps
        {
            public string albedo = string.Empty;
            public string normal = string.Empty;
            public string roughness = string.Empty;
            public string occlusion = string.Empty;
            public string metallic_smoothness = string.Empty;
        }
    }
}
