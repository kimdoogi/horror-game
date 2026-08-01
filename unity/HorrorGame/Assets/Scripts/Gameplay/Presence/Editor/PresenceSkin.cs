#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HorrorGame.Gameplay.Presence;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.Gameplay.PresenceEditor
{
    /// <summary>
    /// Builds the 그늘's two URP Lit materials from what
    /// <c>tools/blender/gen_presence.py</c> wrote.
    /// <para>
    /// This exists because <b>FBX carries neither metallic nor emission</b>. The generator
    /// authors both on the Principled BSDF, the exporter drops both, and a figure imported
    /// without them is a mid-grey mannequin with no light coming off it — which is
    /// simultaneously too visible in a lit room and completely invisible in a dark one.
    /// The same failure took two art passes on the monster and is written up as defect
    /// 3.19 for every other prop in the game, so the values travel in a manifest beside
    /// the mesh and are rebuilt here.
    /// </para>
    /// <para>
    /// <b>The one number that decides whether this entity exists on screen.</b>
    /// <c>Presence_Grain</c>'s emission. The void core is authored at 0.013 albedo — an
    /// order of magnitude under the darkest §12 wall — precisely so it cannot be seen, and
    /// ART.md puts the unlit room's median luminance at 3–16. So the grain is not
    /// decoration on the silhouette: in the situation the 그늘 actually occurs in, which
    /// is a corridor with no light in it, the grain <em>is</em> the silhouette. Set the
    /// emission to zero and the entity is still simulated, still audible, still taking
    /// people's voices, and never once visible.
    /// </para>
    /// <para>
    /// Headless:
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod
    ///   HorrorGame.Gameplay.PresenceEditor.PresenceSkin.Build
    /// </code>
    /// </para>
    /// </summary>
    public static class PresenceSkin
    {
        /// <summary>Menu and batch entry point.</summary>
        [MenuItem("HorrorGame/Presence/Build 그늘 Materials")]
        public static void Build()
        {
            try
            {
                var built = BuildAll();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                foreach (var pair in built)
                {
                    Debug.Log("[PresenceSkin] " + Describe(pair.Value));
                }

                if (IsBatch())
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[PresenceSkin] " + ex);
                if (IsBatch())
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        /// <summary>Builds both materials and returns them by generator name.</summary>
        public static Dictionary<string, Material> BuildAll()
        {
            var entries = LoadManifestMaterials();
            EnsureFolder(PresenceAssets.MaterialRoot);

            var built = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                built[entry.Name] = BuildOne(entry);
            }

            foreach (var required in new[]
                     {
                         PresenceAssets.VoidMaterialName,
                         PresenceAssets.GrainMaterialName,
                         PresenceAssets.DustMaterialName,
                     })
            {
                if (!built.ContainsKey(required))
                {
                    throw new InvalidOperationException(
                        PresenceAssets.ManifestPath + " does not describe " + required
                        + ". PresenceRig binds by these names and a missing one leaves the figure "
                        + "rendering as Unity's default white, which is defect 3.19 again.");
                }
            }

            return built;
        }

        /// <summary>Loads a built material, or null before <see cref="Build"/> has run.</summary>
        public static Material? Load(string materialName) =>
            AssetDatabase.LoadAssetAtPath<Material>(
                PresenceAssets.MaterialRoot + "/" + materialName + ".mat");

        private static Material BuildOne(MaterialEntry entry)
        {
            var path = PresenceAssets.MaterialRoot + "/" + entry.Name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    throw new InvalidOperationException(
                        "Universal Render Pipeline/Lit is missing — the project's render pipeline is not URP.");
                }

                material = new Material(shader) { name = entry.Name };
                AssetDatabase.CreateAsset(material, path);
            }

            var colour = new Color(entry.R, entry.G, entry.B, 1f);

            // The manifest's numbers are linear, which is what the Blender BSDF was
            // authored in and what the shader wants. Reading them as sRGB would raise the
            // void's 0.013 to roughly 0.13 and the hole would become a grey suit.
            material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", colour);
            }

            material.SetFloat("_Smoothness", Mathf.Clamp01(1f - entry.Roughness));
            material.SetFloat("_Metallic", entry.Metallic);
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);

            // The void must not pick the room up out of the reflection probe. A 0.013
            // albedo surface with environment reflections on is still a mirror at grazing
            // angles, and a mirror is the one thing an absence must not be.
            var reflects = entry.Emission > 0f ? 1f : 0f;
            material.SetFloat("_EnvironmentReflections", reflects);
            material.SetFloat("_SpecularHighlights", reflects);

            ApplyEmission(material, colour, entry.Emission);
            ApplyAdditive(material, colour, entry);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ApplyEmission(Material material, Color colour, float strength)
        {
            if (strength <= 0f)
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                material.SetColor("_EmissionColor", Color.black);
                return;
            }

            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", colour * strength);

            // Without this the inspector shows the emission and the shader ignores it,
            // which is the same class of silent failure MonsterSkin records for its
            // texture keywords: assigned, visible in the UI, compiled out.
            material.SetFloat("_EmissiveExposureWeight", 0f);
        }

        /// <summary>
        /// Switches a material to URP's transparent surface with additive blending.
        /// <para>
        /// <b>This is the fix for the defect the third render round photographed:</b> the
        /// free motes read as hard white paper triangles floating in a corridor. An opaque
        /// emissive polygon a metre and a half from the camera has straight, aliased edges
        /// and no amount of scaling makes origami frightening — the eye reads the outline
        /// before it reads the brightness. Added to what is behind it instead, the same
        /// polygon has no outline at all, and what is left is the only thing that was
        /// wanted: a small amount of extra light where nothing should be.
        /// </para>
        /// <para>
        /// Written as raw render-state rather than through a shader variant because URP's
        /// Lit shader keys all of this off properties the material inspector normally
        /// writes: the <c>_Surface</c>/<c>_Blend</c> pair, the blend factors, the depth
        /// write, the render queue and the <c>_SURFACE_TYPE_TRANSPARENT</c> keyword.
        /// Setting the property and not the keyword — or the keyword and not the queue —
        /// produces a material that looks transparent in the inspector and renders opaque,
        /// which is the same silent class of failure <c>MonsterSkin</c> records for its
        /// texture keywords.
        /// </para>
        /// </summary>
        private static void ApplyAdditive(Material material, Color colour, MaterialEntry entry)
        {
            if (!entry.Additive)
            {
                return;
            }

            material.SetColor("_BaseColor", new Color(colour.r, colour.g, colour.b, entry.Alpha));
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", new Color(colour.r, colour.g, colour.b, entry.Alpha));
            }

            material.SetFloat("_Surface", 1f);   // Transparent
            material.SetFloat("_Blend", 1f);     // Additive
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            material.SetShaderPassEnabled("ShadowCaster", false);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static string Describe(Material material)
        {
            var emission = material.HasProperty("_EmissionColor")
                ? material.GetColor("_EmissionColor")
                : Color.black;
            var albedo = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.black;

            return material.name
                   + "  albedo " + albedo.r.ToString("0.000", CultureInfo.InvariantCulture)
                   + "  emission " + emission.maxColorComponent.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // ── Manifest ────────────────────────────────────────────────────────

        /// <summary>One material as the generator authored it.</summary>
        public readonly struct MaterialEntry
        {
            /// <summary>Generator name. <c>PresenceView</c> binds by this.</summary>
            public readonly string Name;

            /// <summary>Linear base colour.</summary>
            public readonly float R;

            /// <summary>Linear base colour.</summary>
            public readonly float G;

            /// <summary>Linear base colour.</summary>
            public readonly float B;

            /// <summary>Principled roughness, converted to URP smoothness at build time.</summary>
            public readonly float Roughness;

            /// <summary>Principled metallic.</summary>
            public readonly float Metallic;

            /// <summary>Principled emission strength. See the class remarks — this is the load-bearing one.</summary>
            public readonly float Emission;

            /// <summary>
            /// Whether this surface is added to the frame rather than drawn over it. See
            /// <see cref="ApplyAdditive"/> — it is what separates grain from litter.
            /// </summary>
            public readonly bool Additive;

            /// <summary>Base-colour alpha. Only read when <see cref="Additive"/>.</summary>
            public readonly float Alpha;

            /// <summary>Builds an entry.</summary>
            public MaterialEntry(string name, float r, float g, float b,
                float roughness, float metallic, float emission, bool additive, float alpha)
            {
                Name = name;
                R = r;
                G = g;
                B = b;
                Roughness = roughness;
                Metallic = metallic;
                Emission = emission;
                Additive = additive;
                Alpha = alpha;
            }
        }

        /// <summary>
        /// Reads the generator's manifest.
        /// <para>
        /// Hand-parsed rather than <c>JsonUtility</c>'d because the manifest carries nested
        /// arrays of objects with Korean prose in them, which <c>JsonUtility</c> handles by
        /// silently producing default values — and a default emission is zero, which is the
        /// one failure this whole file exists to prevent. Failing loudly on a malformed
        /// manifest is worth forty lines.
        /// </para>
        /// </summary>
        public static List<MaterialEntry> LoadManifestMaterials()
        {
            var full = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName, PresenceAssets.ManifestPath);
            if (!File.Exists(full))
            {
                throw new FileNotFoundException(
                    PresenceAssets.ManifestPath + " is missing. Run tools/blender/gen_presence.py first.");
            }

            var text = File.ReadAllText(full);
            var entries = new List<MaterialEntry>();

            var start = text.IndexOf("\"materials\"", StringComparison.Ordinal);
            if (start < 0)
            {
                throw new InvalidOperationException(PresenceAssets.ManifestPath + " has no materials array.");
            }

            var end = text.IndexOf("\"models\"", StringComparison.Ordinal);
            var section = end > start ? text.Substring(start, end - start) : text.Substring(start);

            var cursor = 0;
            while (true)
            {
                var nameAt = section.IndexOf("\"name\"", cursor, StringComparison.Ordinal);
                if (nameAt < 0)
                {
                    break;
                }

                var name = ReadString(section, nameAt);
                var colour = ReadNumbers(section, section.IndexOf("\"color\"", nameAt, StringComparison.Ordinal), 3);
                var roughness = ReadNumber(section, section.IndexOf("\"roughness\"", nameAt, StringComparison.Ordinal));
                var metallic = ReadNumber(section, section.IndexOf("\"metallic\"", nameAt, StringComparison.Ordinal));
                var emission = ReadNumber(section, section.IndexOf("\"emission\"", nameAt, StringComparison.Ordinal));
                var additive = ReadBool(section, section.IndexOf("\"additive\"", nameAt, StringComparison.Ordinal));
                var alpha = ReadNumber(section, section.IndexOf("\"alpha\"", nameAt, StringComparison.Ordinal));

                entries.Add(new MaterialEntry(name, colour[0], colour[1], colour[2],
                    roughness, metallic, emission, additive, alpha));

                cursor = nameAt + 6;
            }

            if (entries.Count == 0)
            {
                throw new InvalidOperationException(
                    PresenceAssets.ManifestPath + " describes no materials.");
            }

            return entries;
        }

        private static string ReadString(string text, int keyAt)
        {
            var colon = text.IndexOf(':', keyAt);
            var open = text.IndexOf('"', colon + 1);
            var close = text.IndexOf('"', open + 1);
            return text.Substring(open + 1, close - open - 1);
        }

        private static float ReadNumber(string text, int keyAt)
        {
            if (keyAt < 0)
            {
                throw new InvalidOperationException(
                    PresenceAssets.ManifestPath + " is missing a numeric field the skin needs.");
            }

            var colon = text.IndexOf(':', keyAt);
            var i = colon + 1;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\n' || text[i] == '\r' || text[i] == '\t'))
            {
                i++;
            }

            var j = i;
            while (j < text.Length && (char.IsDigit(text[j]) || text[j] == '.' || text[j] == '-' || text[j] == 'e'))
            {
                j++;
            }

            return float.Parse(text.Substring(i, j - i), CultureInfo.InvariantCulture);
        }

        private static bool ReadBool(string text, int keyAt)
        {
            if (keyAt < 0)
            {
                throw new InvalidOperationException(
                    PresenceAssets.ManifestPath + " is missing a boolean field the skin needs. "
                    + "Re-run tools/blender/gen_presence.py — the manifest predates the additive "
                    + "surface and the motes would ship as opaque polygons.");
            }

            var colon = text.IndexOf(':', keyAt);
            return text.IndexOf("true", colon, StringComparison.Ordinal) == colon + 1
                   || text.IndexOf("true", colon, StringComparison.Ordinal) == colon + 2;
        }

        private static float[] ReadNumbers(string text, int keyAt, int count)
        {
            if (keyAt < 0)
            {
                throw new InvalidOperationException(PresenceAssets.ManifestPath + " is missing a colour.");
            }

            var open = text.IndexOf('[', keyAt);
            var close = text.IndexOf(']', open + 1);
            var parts = text.Substring(open + 1, close - open - 1).Split(',');
            if (parts.Length < count)
            {
                throw new InvalidOperationException(PresenceAssets.ManifestPath + " has a short colour array.");
            }

            var result = new float[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = float.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            }

            return result;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetPath)!.Replace('\\', '/');
            var leaf = Path.GetFileName(assetPath);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static bool IsBatch()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg == "-batchmode")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
