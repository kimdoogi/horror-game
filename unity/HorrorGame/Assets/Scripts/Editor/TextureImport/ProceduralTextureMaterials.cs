#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.TextureImport
{
    /// <summary>
    /// Builds URP Lit materials from the generated PBR sets and binds them to the MapKit.
    /// <para>
    /// The measured baseline of this project was a black screen: untextured flat
    /// colours, albedo so low the flashlight had nothing to bounce off. §03 makes
    /// light the central mechanic, so a beam that lands invisibly is a broken
    /// mechanic rather than a look. This is the step that connects the generated
    /// maps to what the camera actually sees.
    /// </para>
    /// <para>
    /// Run from the menu, or headless:
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod
    ///   HorrorGame.EditorTools.TextureImport.ProceduralTextureMaterials.Build
    /// </code>
    /// </para>
    /// </summary>
    public static class ProceduralTextureMaterials
    {
        private const string TextureRoot = "Assets/Textures";
        private const string ManifestPath = TextureRoot + "/Textures.manifest.json";
        private const string OwnMaterialRoot = TextureRoot + "/Materials";
        private const string KitRoot = "Assets/Models/MapKit";

        /// <summary>
        /// Where the generated scene expects to find its floor materials.
        /// <para>
        /// <c>MapSceneBuilder</c> creates these on first run and the scene's prefab
        /// instances then reference them by GUID. So the floors have to be updated
        /// <em>in place</em> at this exact path — writing a new asset elsewhere
        /// would leave every floor in the existing scene still pointing at the flat
        /// grey placeholder. It is also why <see cref="LoadOrCreate"/> never
        /// deletes: deleting mints a new GUID and silently unbinds the scene.
        /// </para>
        /// </summary>
        private const string SceneMaterialRoot = "Assets/Scenes/Generated/Materials";

        /// <summary>
        /// Contractual floor names. §12 makes floor material a gameplay channel —
        /// §04's Listener reads the surface underfoot and the footstep audio matches
        /// on the same strings — so these are an interface, not a naming style.
        /// </summary>
        private static readonly string[] ContractualFloors =
        {
            "Floor_Wood", "Floor_Tile", "Floor_Gravel", "Floor_Concrete", "Floor_Metal",
        };

        /// <summary>Builds every material and binds it to the kit. Menu and batch entry point.</summary>
        [MenuItem("HorrorGame/Textures/Build Materials From Generated Maps")]
        public static void Build()
        {
            try
            {
                var manifest = LoadManifest();
                EnsureFolder(OwnMaterialRoot);

                var built = new Dictionary<string, Material>(StringComparer.Ordinal);
                foreach (var entry in manifest.materials)
                {
                    built[entry.name] = BuildOne(entry);
                }

                foreach (var name in ContractualFloors)
                {
                    if (!built.ContainsKey(name))
                    {
                        throw new InvalidOperationException(
                            "The generated set is missing " + name + ". §12 gives every zone a surface and "
                            + "§04's Listener matches on these names; a missing one is a silent zone.");
                    }
                }

                AssetDatabase.SaveAssets();
                var bound = BindToKit(manifest, built);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[Textures] Built " + built.Count + " material(s), bound " + bound
                          + " kit slot binding(s).\n  " + string.Join("\n  ",
                              built.Values.Select(Describe)));

                if (IsBatch())
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[Textures] " + ex);
                if (IsBatch())
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        private static string Describe(Material material)
        {
            var scale = material.GetTextureScale("_BaseMap");
            return material.name + "  tiling " + scale.x.ToString("0.##") + "×" + scale.y.ToString("0.##");
        }

        private static bool IsBatch() =>
            Environment.GetCommandLineArgs().Any(a => string.Equals(a, "-batchmode", StringComparison.Ordinal));

        // ====================================================================
        // Materials.
        // ====================================================================

        private static Material BuildOne(MaterialEntry entry)
        {
            var path = PathFor(entry.name);
            var material = LoadOrCreate(path, entry.name);

            var albedo = LoadMap(entry.maps.albedo);
            var normal = LoadMap(entry.maps.normal);
            var occlusion = LoadMap(entry.maps.occlusion);
            var mask = LoadMap(entry.maps.metallic_smoothness);

            material.SetTexture("_BaseMap", albedo);
            material.SetTexture("_BumpMap", normal);
            material.SetTexture("_OcclusionMap", occlusion);
            material.SetTexture("_MetallicGlossMap", mask);

            // White base tint. The generator already calibrated the albedo into
            // §03's readable band; multiplying by a colour here would silently
            // undo that calibration, which is how the baseline got dark in the
            // first place.
            material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }

            // URP multiplies the map's alpha by _Smoothness, so 1 means "use the
            // authored roughness exactly". _Metallic is ignored once the map is
            // bound, but is set consistently so the inspector does not mislead.
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_OcclusionStrength", 1f);
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.SetFloat("_SpecularHighlights", 1f);

            // Without these keywords URP's Lit shader compiles the sampling out
            // entirely: the maps are assigned, the inspector shows them, and the
            // surface renders as though none of them existed. It is the single
            // easiest way to do all of this work and see no change at all.
            SetKeyword(material, "_NORMALMAP", normal != null);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", mask != null);
            SetKeyword(material, "_OCCLUSIONMAP", occlusion != null);

            ApplyTiling(material, entry);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Sets UV tiling so one texture tile covers the real distance it was authored for.
        /// <para>
        /// The kit's meshes carry UVs in metres — the manifest's
        /// <c>world_size_metres</c> is what the generator drew, so the scale is
        /// simply metres-per-tile inverted. Getting this wrong is not subtle: a
        /// gravel tile stretched over five metres reads as smeared paint, and §12
        /// needs a player to recognise the surface they are standing on.
        /// </para>
        /// </summary>
        private static void ApplyTiling(Material material, MaterialEntry entry)
        {
            var size = entry.world_size_metres > 0.01f ? entry.world_size_metres : 2f;
            var scale = new Vector2(KitUvUnitsPerMetre / size, KitUvUnitsPerMetre / size);

            foreach (var property in new[] { "_BaseMap", "_BumpMap", "_OcclusionMap", "_MetallicGlossMap" })
            {
                if (material.HasProperty(property))
                {
                    material.SetTextureScale(property, scale);
                    material.SetTextureOffset(property, Vector2.zero);
                }
            }
        }

        /// <summary>
        /// UV units the MapKit's meshes span per metre of world.
        /// <para>
        /// Measured, not assumed: <see cref="ReportKitUvScale"/> reports
        /// <c>Corridor_Straight_5m</c> spanning uv −2.5..2.5 over its 5 m footprint
        /// and <c>Corridor_Straight_10m</c> spanning −5..5 over 10 m, so the kit was
        /// unwrapped with UVs in metres and this is 1.
        /// </para>
        /// <para>
        /// It is a constant rather than a per-mesh calculation because tiling is a
        /// material property and one material serves many pieces. If a kit re-export
        /// changes the convention, every surface in the game mis-tiles at once —
        /// which is why the probe stays in the file instead of the number becoming
        /// folklore.
        /// </para>
        /// </summary>
        private const float KitUvUnitsPerMetre = 1f;

        private static string PathFor(string name) =>
            ContractualFloors.Contains(name)
                ? SceneMaterialRoot + "/" + name + ".mat"
                : OwnMaterialRoot + "/" + name + ".mat";

        private static Material LoadOrCreate(string path, string name)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "URP's Lit shader is missing. Materials built against a fallback would "
                    + "not survive the render pipeline being restored.");
            }

            if (existing != null)
            {
                // Reused in place so the GUID survives — see SceneMaterialRoot.
                existing.shader = shader;
                return existing;
            }

            EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
            var created = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static Texture? LoadMap(string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }

            var path = TextureRoot + "/" + relative;
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (texture == null)
            {
                throw new FileNotFoundException(
                    "Generated map missing: " + path + ". Run tools/textures/gen_textures.py.");
            }

            return texture;
        }

        private static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        // ====================================================================
        // Kit binding.
        // ====================================================================

        /// <summary>
        /// Points every MapKit FBX material slot at the material generated for it.
        /// <para>
        /// The kit's meshes name their slots (<c>Wall_Structure</c>,
        /// <c>Ceiling_Structure</c>, <c>Trim_Steel</c>, <c>Door_Leaf</c>,
        /// <c>Floor_*</c>) and the scene is built from prefab instances of those
        /// FBXs, so remapping the importer is what actually re-textures the
        /// existing scene — there is no per-renderer assignment to do and nothing
        /// in the scene file to rewrite.
        /// </para>
        /// <para>
        /// Which material serves which slot comes from the generator's manifest, so
        /// the mapping exists once. A second copy here would drift the first time a
        /// material was renamed, and the failure mode — one untextured wall in one
        /// piece — is close to invisible in review.
        /// </para>
        /// </summary>
        private static int BindToKit(Manifest manifest, IReadOnlyDictionary<string, Material> built)
        {
            var bySlot = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var entry in manifest.materials)
            {
                if (entry.slots == null)
                {
                    continue;
                }

                foreach (var slot in entry.slots)
                {
                    bySlot[slot] = built[entry.name];
                }
            }

            var bound = 0;
            var unmatched = new SortedSet<string>(StringComparer.Ordinal);

            // Slot names come from the kit's own manifest rather than from the
            // importer. Unity exposes no API for "what material slots does this
            // FBX declare" before something has been remapped into them, and the
            // manifest is already the contract MapKitCatalogue validates against,
            // so it is the authoritative list rather than a convenient one.
            foreach (var piece in LoadKitManifest().pieces)
            {
                if (piece.material_slots == null || piece.material_slots.Length == 0)
                {
                    continue;
                }

                var path = KitRoot + "/" + piece.file;
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                {
                    Debug.LogWarning("[Textures] Kit manifest lists " + path + " but no model is imported there.");
                    continue;
                }

                var remapped = importer.GetExternalObjectMap();
                var changed = false;

                foreach (var slot in piece.material_slots)
                {
                    if (!bySlot.TryGetValue(slot, out var material))
                    {
                        unmatched.Add(slot);
                        continue;
                    }

                    var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), slot);
                    bound++;

                    if (remapped.TryGetValue(key, out var current) && ReferenceEquals(current, material))
                    {
                        continue;
                    }

                    importer.AddRemap(key, material);
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }

            if (unmatched.Count > 0)
            {
                Debug.LogWarning("[Textures] Kit slots with no generated material, still on the default "
                                 + "grey: " + string.Join(", ", unmatched));
            }

            return bound;
        }

        /// <summary>
        /// Prints the kit's UV density, which <see cref="KitUvUnitsPerMetre"/> encodes.
        /// <para>
        /// Kept because that constant is the one number here that cannot be derived
        /// from the manifest and would otherwise be folklore. If a kit re-export
        /// changes the UV convention, this is how it gets caught.
        /// </para>
        /// </summary>
        [MenuItem("HorrorGame/Textures/Report Kit UV Scale")]
        public static void ReportKitUvScale()
        {
            var lines = new List<string>();

            // Measured against the manifest's footprint, not against mesh.bounds:
            // mesh bounds are in file units, before the importer's scale factor,
            // so dividing by them reports a number that is off by 100 and looks
            // authoritative while being meaningless.
            foreach (var piece in LoadKitManifest().pieces.Take(8))
            {
                var path = KitRoot + "/" + piece.file;
                var metres = piece.footprint_metres != null && piece.footprint_metres.Length > 0
                    ? Mathf.Max(piece.footprint_metres)
                    : 0f;

                foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>())
                {
                    var uv = mesh.uv;
                    if (uv == null || uv.Length == 0)
                    {
                        lines.Add(piece.file + " / " + mesh.name + ": NO UVs");
                        continue;
                    }

                    var span = Mathf.Max(uv.Max(v => v.x) - uv.Min(v => v.x),
                                         uv.Max(v => v.y) - uv.Min(v => v.y));

                    lines.Add(string.Format(
                        "{0}: uv span {1:0.00} over {2:0.00} m footprint → {3:0.000} uv/m",
                        piece.file, span, metres, metres > 0.001f ? span / metres : 0f));
                }
            }

            Debug.Log("[Textures] Kit UV density:\n  " + string.Join("\n  ", lines));

            if (IsBatch())
            {
                EditorApplication.Exit(0);
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ====================================================================
        // Manifest.
        // ====================================================================

        private static Manifest LoadManifest()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            var json = asset != null ? asset.text : null;

            if (string.IsNullOrEmpty(json) && File.Exists(ManifestPath))
            {
                json = File.ReadAllText(ManifestPath);
            }

            if (string.IsNullOrEmpty(json))
            {
                throw new FileNotFoundException(
                    "No texture manifest at " + ManifestPath + ". Run tools/textures/gen_textures.py first.");
            }

            var manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest?.materials == null || manifest.materials.Length == 0)
            {
                throw new InvalidOperationException("Texture manifest lists no materials.");
            }

            return manifest;
        }

        private static KitManifest LoadKitManifest()
        {
            var path = KitRoot + "/MapKit.manifest.json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            var json = asset != null ? asset.text : (File.Exists(path) ? File.ReadAllText(path) : null);

            if (string.IsNullOrEmpty(json))
            {
                throw new FileNotFoundException("No MapKit manifest at " + path + ".");
            }

            var manifest = JsonUtility.FromJson<KitManifest>(json);
            if (manifest?.pieces == null || manifest.pieces.Length == 0)
            {
                throw new InvalidOperationException("MapKit manifest lists no pieces.");
            }

            return manifest;
        }

        /// <summary>The part of the kit manifest this needs: which slots each piece declares.</summary>
        [Serializable]
        private sealed class KitManifest
        {
            public KitPiece[] pieces = Array.Empty<KitPiece>();
        }

        [Serializable]
        private sealed class KitPiece
        {
            public string name = string.Empty;
            public string file = string.Empty;
            public string[] material_slots = Array.Empty<string>();
            public float[] footprint_metres = Array.Empty<float>();
        }

        /// <summary>Mirror of the JSON the Python generator writes. Field names must match it.</summary>
        [Serializable]
        private sealed class Manifest
        {
            public int resolution;
            public MaterialEntry[] materials = Array.Empty<MaterialEntry>();
        }

        [Serializable]
        private sealed class MaterialEntry
        {
            public string name = string.Empty;
            public string[] slots = Array.Empty<string>();
            public string note = string.Empty;
            public float world_size_metres = 2f;
            public MapPaths maps = new MapPaths();
        }

        [Serializable]
        private sealed class MapPaths
        {
            public string albedo = string.Empty;
            public string normal = string.Empty;
            public string roughness = string.Empty;
            public string occlusion = string.Empty;
            public string metallic_smoothness = string.Empty;
        }
    }
}
