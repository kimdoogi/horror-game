#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.Gameplay.MonsterEditor
{
    /// <summary>
    /// Builds the monster's URP Lit materials from the maps
    /// <c>tools/blender/gen_monster_ai.py</c> writes.
    /// <para>
    /// The monster shipped with three flat colours while every wall, floor and prop
    /// around it carried a full PBR set. §03 makes the flashlight the game's central
    /// mechanic, and a flat surface gives that beam nothing to do: no normal to catch
    /// its grazing edge, no roughness variation to break its hotspot. The creature
    /// therefore rendered as a matte cut-out laid over a textured room — the one thing
    /// in the frame that visibly did not belong in it.
    /// </para>
    /// <para>
    /// Every number here comes out of <c>Assets/Textures/Monster.textures.json</c>.
    /// The generator measured the UV density of its own unwrap and the metres each
    /// map was drawn for, so the tiling is derived rather than dialled in; a
    /// re-unwrap that repacks the islands changes the manifest and the materials
    /// follow it.
    /// </para>
    /// <para>
    /// Headless:
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod
    ///   HorrorGame.Gameplay.MonsterEditor.MonsterSkin.Build
    /// </code>
    /// </para>
    /// </summary>
    public static class MonsterSkin
    {
        /// <summary>Where the generator writes its maps and manifest.</summary>
        public const string TextureRoot = "Assets/Textures";

        /// <summary>The generator's manifest — the single description of the skin.</summary>
        public const string ManifestPath = TextureRoot + "/Monster.textures.json";

        /// <summary>Where the built materials live. The prefab binds them by asset reference.</summary>
        public const string MaterialRoot = "Assets/Materials/Monster";

        /// <summary>Builds every monster material. Menu and batch entry point.</summary>
        [MenuItem("HorrorGame/Monster/Build Skin Materials")]
        public static void Build()
        {
            try
            {
                var built = BuildAll();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[MonsterSkin] built " + built.Count + " material(s):\n  "
                          + string.Join("\n  ", built.Values.Select(Describe)));

                if (IsBatch())
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[MonsterSkin] " + ex);
                if (IsBatch())
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        /// <summary>Builds the materials and returns them by generator name.</summary>
        public static Dictionary<string, Material> BuildAll()
        {
            var manifest = LoadManifest();
            EnsureFolder(MaterialRoot);

            var built = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var entry in manifest.materials)
            {
                built[entry.name] = BuildOne(entry, manifest.uv_units_per_metre);
            }

            return built;
        }

        private static string Describe(Material material)
        {
            var scale = material.GetTextureScale("_BaseMap");
            return material.name + "  tiling " + scale.x.ToString("0.00") + "×" + scale.y.ToString("0.00");
        }

        private static Material BuildOne(MaterialEntry entry, float uvUnitsPerMetre)
        {
            var material = LoadOrCreate(MaterialRoot + "/" + entry.name + ".mat", entry.name);

            var albedo = LoadMap(entry.maps.albedo);
            var normal = LoadMap(entry.maps.normal);
            var occlusion = LoadMap(entry.maps.occlusion);
            var mask = LoadMap(entry.maps.metallic_smoothness);

            material.SetTexture("_BaseMap", albedo);
            material.SetTexture("_BumpMap", normal);
            material.SetTexture("_OcclusionMap", occlusion);
            material.SetTexture("_MetallicGlossMap", mask);

            // White tint. The generator already landed the albedo on its intended mean
            // inside gen_textures.py's band; multiplying by a colour here would undo
            // that calibration silently, which is exactly how the world went black
            // before the texture pass.
            material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }

            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_OcclusionStrength", 1f);
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.SetFloat("_SpecularHighlights", 1f);

            // Relief is pushed past 1 on the creature and only on the creature. The
            // maps are 8–12 mm deep over a body seen at 3 m under a hard-shadowed
            // spot, and at that distance a physically honest bump strength reads as a
            // smooth doll. §12's walls are seen at every distance and must stay honest;
            // the monster is a close-up prop and is allowed to be pushed.
            material.SetFloat("_BumpScale", 1.6f);

            // Without these the Lit shader compiles the sampling out entirely: the maps
            // are assigned, the inspector shows them, and the surface renders as though
            // none of them existed.
            SetKeyword(material, "_NORMALMAP", normal != null);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", mask != null);
            SetKeyword(material, "_OCCLUSIONMAP", occlusion != null);

            ApplyEmission(material, entry, albedo);
            ApplyRim(material);
            ApplyTiling(material, entry, uvUnitsPerMetre);

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Colour of the rim, and of nothing else the creature does.
        /// <para>
        /// Cold and slightly blue, the same value <c>MonsterBeamResolve</c> used for the
        /// ambient fill it replaced, and matching the grade
        /// <see cref="HorrorGame.Rendering.NightAtmosphere"/> puts on the basement. That
        /// is the whole claim the term is making: this is the room's own light finding an
        /// edge, not the creature producing light. Warm here would read as the maw, and
        /// the maw means §06 has decided on you.
        /// </para>
        /// </summary>
        public static readonly Color RimColour = new Color(0.60f, 0.67f, 0.82f);

        /// <summary>
        /// Fresnel exponent. Lower is a wider band of edge.
        /// <para>
        /// This was authored at 2.2 on the argument that at
        /// <see cref="GameConstants.ObserverRange"/> the creature is only about forty
        /// pixels tall, so a tight rim would be sub-pixel and average away against the
        /// wall. Plausible, and the measurement says the opposite at every distance. A
        /// broad Fresnel does not only light the edge: its tail reaches the chest and the
        /// face, which are the surfaces pointing at the player, and lifting those toward
        /// the wall's luminance costs more contrast than the wider edge buys. At 15 m,
        /// moving 2.2 → 4.5 took the silhouette's coverage from 0.872 to 0.947 and its
        /// contrast from 0.0353 to 0.0446 — and turned the body from very slightly
        /// brighter than its wall into 0.10 <em>darker</em>, which is the shape §06 wants.
        /// </para>
        /// <para>
        /// The sub-pixel worry was real and is answered by strength rather than by width:
        /// a narrower band carrying more light per pixel still resolves, because what
        /// survives downsampling is the integral.
        /// </para>
        /// </summary>
        public const float RimPower = 4.5f;

        /// <summary>
        /// Rim added to surfaces facing the camera dead on, as a fraction of the edge value.
        /// <para>
        /// Zero, and it is zero because the measurement said so rather than because nobody
        /// tried. The argument for a pedestal is reasonable — a pure Fresnel lights the
        /// outline and leaves the interior at the wall's luminance, which sounds like a
        /// wire figure — and it is wrong, because the interior is not at the wall's
        /// luminance. With <see cref="FogResponse"/> holding the haze off, the creature's
        /// chest and face sit <em>below</em> the corridor behind them, and every one of
        /// those pixels is contrast. A pedestal spends that: at 15 m a floor of 0.08 moved
        /// the body from 0.053 above the wall to 0.211 above it and took the silhouette's
        /// coverage from 0.875 down to 0.739. Lifting the interior does not add a body, it
        /// erases one.
        /// </para>
        /// </summary>
        public const float RimFloor = 0f;

        /// <summary>
        /// How much of the room's fog the creature takes, 0–1.
        /// <para>
        /// See the note in <c>MonsterSkinForwardPass.hlsl</c>: URP hazes the creature and
        /// the wall behind it by the same fraction, so the haze lands on the difference
        /// between them and cancels it. 0.45 keeps the creature in the air — it still
        /// desaturates and still loses contrast with range, so it is not a sticker — while
        /// letting the corridor lift away from it. §12 caps a straight sight line at 20 m,
        /// so this never has to hold up beyond that.
        /// </para>
        /// </summary>
        public const float FogResponse = 0.45f;

        /// <summary>
        /// Arms the rim and leaves its strength at zero.
        /// <para>
        /// Strength is a runtime value: <c>MonsterBeamResolve</c> ramps it with viewing
        /// distance, because a rim that is on at 2 m is a creature with a halo standing in
        /// front of the player. Authored at zero so a prefab dropped in a scene with no
        /// camera renders the creature the shader's other terms describe rather than a
        /// lit-up one.
        /// </para>
        /// </summary>
        private static void ApplyRim(Material material)
        {
            material.SetColor("_RimColor", RimColour);
            material.SetFloat("_RimStrength", 0f);
            material.SetFloat("_RimPower", RimPower);
            material.SetFloat("_RimFloor", RimFloor);
            material.SetFloat("_FogResponse", FogResponse);
        }

        /// <summary>
        /// Arms every surface's emission channel and leaves it black, except the eyes.
        /// <para>
        /// One runtime component drives this channel — <c>MonsterAcquireTell</c>, which
        /// lights the maw for §06's acquisition — and it is black here because the state a
        /// creature is in most of the time is "not announcing anything".
        /// <c>MonsterBeamResolve</c> used to write this channel too, with a flat ambient
        /// fill; it now writes <c>_RimStrength</c> instead, so the two components no
        /// longer share a property and cannot overwrite each other by accident.
        /// </para>
        /// <para>
        /// The eyes are the exception and are constant. See <see cref="EyeGlow"/>.
        /// </para>
        /// <para>
        /// The keyword has to be enabled at build time regardless. URP compiles emission
        /// out of the shader variant when <c>_EMISSION</c> is off, so a material left
        /// dark-and-disabled ignores the runtime property silently — the components run,
        /// the values change, and nothing happens.
        /// </para>
        /// <para>
        /// The emission map is each surface's own albedo, so the light takes the shape of
        /// the material: the maw glows along its folds rather than flooding the polygon,
        /// and the fill picks out the hide's structure instead of flattening the creature
        /// into a paper cut-out at exactly the distance it most needs to read as a body.
        /// </para>
        /// </summary>
        /// <summary>The generator's name for the eye material. Two lenses, nothing else.</summary>
        public const string EyeMaterialName = "Monster_Eyes";

        /// <summary>
        /// The eyes' constant emission.
        /// <para>
        /// The single most effective distance cue available, and the only one that
        /// survives the creature being forty pixels tall: two separated points read as a
        /// face and as a <em>facing</em> at a range where the body is a smudge. It serves
        /// §04 directly — the 관측자's ability is "괴물의 시야를 본다 → 누가 표적인지",
        /// and where the eyes point is that information. Until now the creature had no
        /// eyes and the 관측자 had nothing to read but a crest.
        /// </para>
        /// <para>
        /// Faint, and the number is bounded from both ends. Each lens is 5 cm, which is
        /// two pixels across at <see cref="GameConstants.ObserverRange"/>, so it has to
        /// beat the corridor's own luminance by enough to survive being averaged with the
        /// wall — that is the floor. The ceiling is §03: a creature carrying a lamp is a
        /// creature that unlocks the room it walks into, and the last review specifically
        /// criticised the model for rendering brighter than the corridor. 2.6 puts the
        /// lenses at roughly the luminance of a §12 practical seen from across a zone —
        /// unmistakably a light, unmistakably not a torch.
        /// </para>
        /// <para>
        /// Cold gold-green, not the maw's blood orange. The two must never be confused:
        /// the maw firing means §06 has entered 추격 and someone has about three seconds
        /// to find a corner, while the eyes mean only that the creature exists. Separated
        /// by hue and by the fact that the eyes never change.
        /// </para>
        /// <para>
        /// A property and not a <c>static readonly</c> field, which is not style. Written as
        /// a field initialised from <see cref="EyeHue"/> it was declared <em>above</em> the
        /// hue it multiplies, and C# runs static field initialisers in declaration order —
        /// so the hue was still <c>default(Color)</c> and the eyes were built black. Nothing
        /// failed: the material had an emission channel, the keyword was on, the map was
        /// bound, and the creature shipped with two unlit lenses. It was caught by reading
        /// <c>Monster_Eyes.mat</c> and noticing the alpha was 0 rather than 1.
        /// </para>
        /// </summary>
        public static Color EyeGlow => EyeHue * EyeIntensity;

        /// <summary>The eyes' hue at unit intensity. Split out so a capture sweep can scale it.</summary>
        public static readonly Color EyeHue = new Color(0.74f, 0.86f, 0.55f);

        /// <summary>
        /// Multiplier on <see cref="EyeHue"/>. Calibrated against the staged frames — see
        /// the note on <see cref="EyeGlow"/> for the two ends it is bounded by.
        /// </summary>
        public const float EyeIntensity = 2.6f;

        private static void ApplyEmission(Material material, MaterialEntry entry, Texture? albedo)
        {
            var isEyes = entry.name.Contains(EyeMaterialName);
            material.SetColor("_EmissionColor", isEyes ? EyeGlow : Color.black);
            material.SetTexture("_EmissionMap", albedo);
            SetKeyword(material, "_EMISSION", true);

            // Realtime, not baked: both drivers fire mid-match, and a baked contribution
            // would be a lightmap of a moving creature — wrong twice.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        /// <summary>
        /// Sets tiling so one texture tile covers the metres it was drawn for.
        /// <para>
        /// Two measured numbers meet here. The generator smart-projects the body into
        /// the 0–1 square, so how many UV units a metre of hide spans depends on how
        /// the islands packed — it measured that as <c>uv_units_per_metre</c>. Each map
        /// separately declares the <c>world_size_metres</c> its structure was drawn at.
        /// One tile therefore has to span <c>uv × world</c> UV units, and the scale is
        /// the reciprocal. Guessing either number produces hide with 30 cm pores or a
        /// carapace sanded flat, and both look like a bad model rather than a bad
        /// setting.
        /// </para>
        /// </summary>
        private static void ApplyTiling(Material material, MaterialEntry entry, float uvUnitsPerMetre)
        {
            if (uvUnitsPerMetre <= 0f || entry.world_size_metres <= 0f)
            {
                throw new InvalidOperationException(
                    "Monster.textures.json carries a non-positive UV density or tile size for "
                    + entry.name + ". Re-run tools/blender/gen_monster_ai.py.");
            }

            var factor = 1f / (uvUnitsPerMetre * entry.world_size_metres);
            var scale = new Vector2(factor, factor);

            foreach (var property in new[] { "_BaseMap", "_BumpMap", "_OcclusionMap", "_MetallicGlossMap", "_EmissionMap" })
            {
                if (material.HasProperty(property))
                {
                    material.SetTextureScale(property, scale);
                    material.SetTextureOffset(property, Vector2.zero);
                }
            }
        }

        /// <summary>
        /// The creature's own shader. URP Lit's lighting plus the two terms §04 needs.
        /// <para>
        /// Every property name it declares is Lit's, so the maps, the tiling and both
        /// runtime property-block writers work against either one. That is deliberate:
        /// the shader adds terms, it does not replace a pipeline.
        /// </para>
        /// </summary>
        public const string ShaderName = "HorrorGame/MonsterSkin";

        private static Material LoadOrCreate(string path, string name)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    ShaderName + " is missing (Assets/Shaders/Monster/MonsterSkin.shader). Falling "
                    + "back to URP Lit here would build materials with no rim and no fog response "
                    + "and nothing would fail — the creature would simply be invisible past about "
                    + "10 m again, which is the defect this shader exists to fix.");
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                // Reused in place so the GUID survives — the prefab references it.
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
                    "Generated map missing: " + path + ". Run tools/blender/gen_monster_ai.py.");
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

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static bool IsBatch() =>
            Environment.GetCommandLineArgs().Any(a => string.Equals(a, "-batchmode", StringComparison.Ordinal));

        // ====================================================================
        // Manifest. Shapes match the generator's JSON exactly; JsonUtility is
        // strict about that and silently leaves anything it cannot match at its
        // default, which is why these are asserted after loading.
        // ====================================================================

        /// <summary>Reads and sanity-checks the generator's manifest.</summary>
        public static Manifest LoadManifest()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (asset == null)
            {
                throw new FileNotFoundException(
                    ManifestPath + " is missing. Run tools/blender/gen_monster_ai.py.");
            }

            var manifest = JsonUtility.FromJson<Manifest>(asset.text);
            if (manifest == null || manifest.materials == null || manifest.materials.Length == 0)
            {
                throw new InvalidOperationException(ManifestPath + " lists no materials.");
            }

            return manifest;
        }

        [Serializable]
        public sealed class Manifest
        {
            public int resolution;
            public float uv_units_per_metre;
            public MaterialEntry[] materials = Array.Empty<MaterialEntry>();
        }

        [Serializable]
        public sealed class MaterialEntry
        {
            public string name = string.Empty;
            public string note = string.Empty;
            public float world_size_metres;
            public float albedo_mean_linear;
            public float roughness_mean;
            public float relief_mm;
            public MapPaths maps = new MapPaths();
        }

        [Serializable]
        public sealed class MapPaths
        {
            public string albedo = string.Empty;
            public string normal = string.Empty;
            public string roughness = string.Empty;
            public string occlusion = string.Empty;
            public string metallic_smoothness = string.Empty;
        }
    }
}
