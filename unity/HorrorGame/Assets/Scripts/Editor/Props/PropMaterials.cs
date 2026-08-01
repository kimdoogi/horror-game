#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Props
{
    /// <summary>
    /// Rebuilds the §08 / §03 prop kit's materials as URP Lit assets and binds them to
    /// the FBX importers.
    /// <para>
    /// <b>Why this is needed even though the FBX already import "fine".</b> FBX cannot
    /// carry a PBR material: the format has a Lambert/Phong slot, so Unity sees a
    /// diffuse colour and a shininess and nothing else. Measured on the shipped files —
    /// every one of the 21 prop materials arrives at <c>metallic 0</c>. §08 spends a
    /// row on 은수저 being "눈에 잘 보임 (유혹)" and <c>gen_props.py</c> builds it
    /// mirror-grade for that reason; imported, it is grey plastic. The manifest carries
    /// the values the Principled BSDF was actually built with and this class rebuilds
    /// them, exactly as <c>DressingMaterials</c> does for the dressing kit.
    /// </para>
    /// <para>
    /// <b>And it pins the shader.</b> The importer picks a shader from whatever
    /// pipeline happens to be active when the asset is imported. A material that ends
    /// up on a built-in shader renders magenta under URP and there is no warning; this
    /// pass makes the choice explicit and <em>fails</em> rather than substituting a
    /// fallback, because a silent fallback is what put white primitives and a magenta
    /// build in front of the owner in the first place.
    /// </para>
    /// <para>
    /// <b>Every prop material carries the emission keyword.</b> Not to glow — all but
    /// two are black — but because <c>InteractableHighlight</c> drives
    /// <c>_EmissionColor</c> through a <c>MaterialPropertyBlock</c> when the crosshair
    /// is on a prop, and a property block cannot enable a shader keyword. Without the
    /// keyword compiled in, the highlight assigns a colour that is never sampled and
    /// the player gets no feedback at all.
    /// </para>
    /// </summary>
    public static class PropMaterials
    {
        /// <summary>The kit these materials belong to.</summary>
        public const string PropRoot = "Assets/Models/Props";

        /// <summary>Written by <c>tools/blender/gen_props.py</c>. The authored PBR values.</summary>
        public const string ManifestPath = PropRoot + "/Props.manifest.json";

        /// <summary>Where the generated materials live. Inside the kit, so their origin is unambiguous.</summary>
        public const string MaterialRoot = PropRoot + "/Materials";

        /// <summary>
        /// The roughest a prop may be authored smoother than. ART.md §7.6. See the
        /// smoothness note in <see cref="BuildOne"/> for why the generator's value is
        /// capped rather than trusted.
        /// </summary>
        private const float RoughnessFloor = 0.40f;

        /// <summary>Builds every prop material and binds it to the kit. Menu entry point.</summary>
        [MenuItem("HorrorGame/Props/Build Prop Materials", priority = 30)]
        public static void Build()
        {
            try
            {
                BuildOnly();
            }
            catch (Exception ex)
            {
                Debug.LogError("[PropMaterials] " + ex);
            }
        }

        /// <summary>The work, with no exception handling — the batch driver reports failure itself.</summary>
        public static void BuildOnly()
        {
            var manifest = LoadManifest();
            var built = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var spec in manifest.materials)
            {
                built[spec.name] = BuildOne(spec);
            }

            AssetDatabase.SaveAssets();
            var report = Bind(built);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[PropMaterials] Built " + built.Count + " URP material(s); " + report);
        }

        // ====================================================================
        // Materials.
        // ====================================================================

        private static Material BuildOne(MaterialSpec spec)
        {
            var path = MaterialRoot + "/" + spec.name + ".mat";
            var material = LoadOrCreate(path, spec.name);

            // Blender's Principled BSDF base colour is linear, and so is a Color handed
            // to SetColor in a linear project. Passed straight through, as the dressing
            // kit does — the importer's own guess is a full stop brighter because it
            // reads the same number as sRGB.
            var colour = new Color(spec.Red, spec.Green, spec.Blue, 1f);
            material.SetColor("_BaseColor", colour);
            material.color = colour;

            // URP's smoothness is the complement of Blender's roughness. Backwards, the
            // one mirror in §08's kit — the 은수저 — becomes the dullest thing in it.
            //
            // And it is floored, which is the one place this pass overrides the
            // generator. §05 holds the torch at the eye, so light and view arrive from
            // the same direction; a piece of loot lying flat on a floor is then seen at
            // a grazing angle and a near-mirror throws its whole lobe away from the
            // camera. Measured under the game's own beam at 2 m: mirror-grade silver
            // spoons (roughness 0.16) rendered as a black smudge on lit gravel, which is
            // the exact opposite of §08's "은수저 · 잡동사니 … 눈에 잘 보임 (유혹)". The
            // floor is ART.md §7.6's own number, used there for the opposite problem —
            // it is the roughness at which this building's materials behave under this
            // torch. The right long-term fix is a reflection probe indoors; until there
            // is one, a metal with nothing to reflect must not be a mirror.
            material.SetFloat("_Smoothness", Mathf.Clamp01(1f - Mathf.Max(spec.roughness, RoughnessFloor)));
            material.SetFloat("_Metallic", Mathf.Clamp01(spec.metallic));
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_SpecularHighlights", 1f);

            // There is no reflection probe inside this building, so URP's environment
            // term falls back to a dusk skybox. On a metal that is what makes it read as
            // metal at all under a moving beam; the dressing kit reached the same
            // conclusion and kept it for metals. Left on for everything here because
            // §08's kit has no puddles.
            material.SetFloat("_EnvironmentReflections", 1f);

            // See the class remarks: the keyword is what makes the crosshair highlight
            // possible at all. Colour is black unless the generator authored a glow.
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor(
                "_EmissionColor",
                spec.emission > 0f ? colour * (spec.emission * 0.25f) : Color.black);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreate(string path, string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "URP's Lit shader is missing, so every prop would be built on a fallback and "
                    + "render magenta the moment the pipeline is restored. Install "
                    + "com.unity.render-pipelines.universal, then run "
                    + "Horror ▸ Setup ▸ Configure URP Render Pipeline.");
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                // Reused in place so the GUID survives. Deleting mints a new one and
                // silently unbinds every FBX importer that already points here.
                existing.shader = shader;
                return existing;
            }

            EnsureFolder(MaterialRoot);
            var created = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        // ====================================================================
        // Binding.
        // ====================================================================

        /// <summary>
        /// Points every material slot on every prop FBX at the material built for it.
        /// <para>
        /// Slot names are read off the imported prefab's renderers rather than from the
        /// manifest, because that is the only list Unity guarantees is current, and it
        /// keeps working after a remap: the generated assets are named for the slots
        /// they serve, so the second run reads back the same names as the first.
        /// </para>
        /// </summary>
        private static string Bind(IReadOnlyDictionary<string, Material> built)
        {
            var bound = 0;
            var reimported = 0;
            var unmatched = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { PropRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                {
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                var slots = prefab.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null)
                    .Select(m => m.name)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                var remapped = importer.GetExternalObjectMap();
                var changed = false;

                foreach (var slot in slots)
                {
                    if (!built.TryGetValue(slot, out var material))
                    {
                        unmatched.Add(slot);
                        continue;
                    }

                    bound++;
                    var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), slot);
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
                    reimported++;
                }
            }

            var report = "bound " + bound + " slot(s) across the kit, reimported " + reimported + " model(s)";
            if (unmatched.Count > 0)
            {
                // Loud, not silent: an unbound slot keeps whatever the importer guessed,
                // which is the failure this whole pass exists to end.
                Debug.LogError("[PropMaterials] Prop material slots with no manifest entry, still on the "
                    + "importer's guess: " + string.Join(", ", unmatched)
                    + ". Re-run tools/blender/gen_props.py -- --manifest-only.");
                report += ", " + unmatched.Count + " UNBOUND";
            }

            return report;
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
            var json = asset != null ? asset.text : (File.Exists(ManifestPath) ? File.ReadAllText(ManifestPath) : null);

            if (string.IsNullOrEmpty(json))
            {
                throw new FileNotFoundException(
                    "No prop manifest at " + ManifestPath + ". Write one with:\n"
                    + "  blender --background --factory-startup --python tools/blender/gen_props.py -- --manifest-only");
            }

            var manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest?.materials == null || manifest.materials.Length == 0)
            {
                throw new InvalidOperationException("The prop manifest lists no materials.");
            }

            return manifest;
        }

        /// <summary>Mirror of the JSON <c>gen_props.py</c> writes. Field names must match it.</summary>
        [Serializable]
        private sealed class Manifest
        {
            public MaterialSpec[] materials = Array.Empty<MaterialSpec>();
            public PropSpec[] props = Array.Empty<PropSpec>();
        }

        [Serializable]
        private sealed class MaterialSpec
        {
            public string name = string.Empty;
            public float[] color = Array.Empty<float>();
            public float roughness = 0.7f;
            public float metallic;
            public float emission;

            public float Red { get { return color.Length > 0 ? color[0] : 1f; } }

            public float Green { get { return color.Length > 1 ? color[1] : 1f; } }

            public float Blue { get { return color.Length > 2 ? color[2] : 1f; } }
        }

        [Serializable]
        private sealed class PropSpec
        {
            public string name = string.Empty;
            public string file = string.Empty;
            public string category = string.Empty;
            public string mount = string.Empty;
            public string note = string.Empty;
        }
    }
}
