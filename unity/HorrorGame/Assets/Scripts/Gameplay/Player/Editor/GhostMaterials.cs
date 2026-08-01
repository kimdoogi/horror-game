#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.Gameplay.PlayerEditor
{
    /// <summary>
    /// Builds §09's 유령 materials as URP Lit assets and binds them to <c>Ghost.fbx</c>.
    /// <para>
    /// <b>This file is the reason the ghost looks like anything.</b> FBX carries a
    /// Lambert/Phong slot and nothing else — no emission at all — and emission is what
    /// this model is made of. Left to the importer the ghost arrives as six flat white
    /// plastic surfaces with an albedo of 0.03 linear, which in a §12 corridor is an
    /// invisible black outline. Every value here comes from
    /// <c>Assets/Textures/Ghost.textures.json</c>, written by
    /// <c>tools/blender/gen_ghost.py</c>, so the look lives in the generator and not in
    /// two places that can disagree.
    /// </para>
    /// <para>
    /// <b>The shader is pinned and a miss is fatal.</b> ART.md §7.11: a material that
    /// lands on a built-in shader renders magenta in a URP build with no warning in the
    /// editor, and the props shipped that way for weeks.
    /// </para>
    /// <para>
    /// <b>Emission has to be baked-and-realtime, not None.</b> A material whose
    /// <c>globalIlluminationFlags</c> stay at Unity's default for a new material is
    /// treated as emissive-black by the lightmapper and, worse, has its emission stripped
    /// by the build when nothing marks it as used. The ghost is the one object in the
    /// game whose entire read is emission.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -nographics -projectPath unity/HorrorGame -executeMethod \
    ///   HorrorGame.Gameplay.PlayerEditor.GhostMaterials.BuildBatch
    /// </code>
    /// </summary>
    public static class GhostMaterials
    {
        /// <summary>Written by <c>tools/blender/gen_ghost.py</c>.</summary>
        public const string ManifestPath = "Assets/Textures/Ghost.textures.json";

        /// <summary>Where the built materials go. Beside the model they dress.</summary>
        public const string MaterialRoot = "Assets/Models/Characters/Materials";

        /// <summary>The model whose importer gets remapped onto them.</summary>
        public const string ModelPath = "Assets/Models/Characters/Ghost.fbx";

        /// <summary>
        /// The albedo ceiling §09's design rests on, in linear.
        /// <para>
        /// The darkest wall in a §12 corridor is 0.21 linear and <c>gen_monster_ai.py</c>
        /// holds the creature's hide under that at 0.17 so it does not announce itself.
        /// The ghost is an order of magnitude below the creature: a torch put on it
        /// returns almost nothing, so it is the one object in a game built entirely on
        /// 「어둠 = 목표의 잠금장치」 that does not answer to the beam. Asserted here as
        /// well as in the generator because this is the last place the numbers are still
        /// numbers.
        /// </para>
        /// </summary>
        private const float MaximumAlbedoLinear = 0.06f;

        /// <summary>Batch entry point. Exits 0 on success, 1 on any failure.</summary>
        public static void BuildBatch()
        {
            try
            {
                EditorApplication.Exit(Build() ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[GhostMaterials] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Menu twin of <see cref="BuildBatch"/>.</summary>
        [MenuItem("HorrorGame/Player/Rebuild Ghost Materials")]
        public static void BuildMenu()
        {
            Build();
        }

        /// <summary>Builds every material and binds the model to them.</summary>
        public static bool Build()
        {
            var full = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, ManifestPath);
            if (!File.Exists(full))
            {
                Debug.LogError("[GhostMaterials] no manifest at " + ManifestPath
                    + ". Run tools/blender/gen_ghost.py.");
                return false;
            }

            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(full));
            if (manifest?.materials == null || manifest.materials.Length == 0)
            {
                Debug.LogError("[GhostMaterials] " + ManifestPath + " has no materials.");
                return false;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[GhostMaterials] URP/Lit is not in this project. A material "
                    + "built on a built-in shader renders magenta in a player and nothing warns.");
                return false;
            }

            Directory.CreateDirectory(
                Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, MaterialRoot));

            var built = new Dictionary<string, Material>(StringComparer.Ordinal);
            var lines = new List<string>();
            foreach (var entry in manifest.materials)
            {
                if (entry.base_color_linear == null || entry.base_color_linear.Length < 3
                    || entry.emission_linear == null || entry.emission_linear.Length < 3)
                {
                    Debug.LogError("[GhostMaterials] " + entry.name + " is missing a colour.");
                    return false;
                }

                var albedo = new Color(entry.base_color_linear[0], entry.base_color_linear[1],
                                       entry.base_color_linear[2], 1f);
                var brightest = Mathf.Max(albedo.r, Mathf.Max(albedo.g, albedo.b));
                if (brightest > MaximumAlbedoLinear)
                {
                    Debug.LogError(string.Format(CultureInfo.InvariantCulture,
                        "[GhostMaterials] {0} has an albedo of {1:F3} linear, over {2:F2}. "
                        + "Past that a §03 torch starts to find it, and a ghost that answers "
                        + "to the beam is read the way every other object in the game is read "
                        + "— point the light at it and find out. That is a monster.",
                        entry.name, brightest, MaximumAlbedoLinear));
                    return false;
                }

                var material = Load(shader, entry.name);
                material.SetColor("_BaseColor", albedo);
                material.SetFloat("_WorkflowMode", 1f);
                material.SetFloat("_Metallic", Mathf.Clamp01(entry.metallic));
                material.SetFloat("_Smoothness", Mathf.Clamp01(1f - entry.roughness));
                material.SetFloat("_SmoothnessTextureChannel", 0f);

                // Reflections off: there is no reflection probe indoors (ART.md §7.9), so
                // the only thing a reflective ghost can mirror is the skybox — which is
                // outdoors, and would put a patch of night sky on something standing in a
                // basement.
                material.SetFloat("_EnvironmentReflections", 0f);
                material.SetFloat("_SpecularHighlights", 1f);

                var emission = new Color(entry.emission_linear[0], entry.emission_linear[1],
                                         entry.emission_linear[2], 1f);
                material.SetColor("_EmissionColor", emission);
                if (entry.emission_strength > 0f)
                {
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    // The maw. The one unlit surface on the model, and the contrast is the
                    // whole face — every other face is lit from within, so this reads as an
                    // absence in something that is itself barely present.
                    material.DisableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }

                EditorUtility.SetDirty(material);
                built[entry.name] = material;
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}: albedo {1:F4} lin, emission {2:F2}", entry.name, brightest,
                    entry.emission_strength));
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[GhostMaterials] §09's 유령\n  " + string.Join("\n  ", lines));
            return Remap(built);
        }

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

        private static bool Remap(Dictionary<string, Material> built)
        {
            if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
            {
                Debug.LogError("[GhostMaterials] no ModelImporter at " + ModelPath
                    + ". Run tools/blender/gen_ghost.py.");
                return false;
            }

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            foreach (var pair in built)
            {
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            }

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (root == null)
            {
                Debug.LogError("[GhostMaterials] " + ModelPath + " did not reimport.");
                return false;
            }

            var stray = new List<string>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    var name = material == null ? "<null>" : material.name;
                    if (!built.ContainsKey(name))
                    {
                        stray.Add(renderer.name + "." + name);
                    }
                }
            }

            if (stray.Count > 0)
            {
                Debug.LogError("[GhostMaterials] after the remap these renderer slots are still "
                    + "not one of the built materials: " + string.Join(", ", stray)
                    + ". A slot Unity did not match by name keeps its embedded white default, "
                    + "and a white ghost is a lamp.");
                return false;
            }

            Debug.Log("[GhostMaterials] bound " + built.Count + " material(s) to " + ModelPath);
            return true;
        }

        [Serializable]
        private sealed class Manifest
        {
            public ManifestMaterial[]? materials;
        }

        [Serializable]
        private sealed class ManifestMaterial
        {
            public string name = string.Empty;
            public float[]? base_color_linear;
            public float[]? emission_linear;
            public float emission_strength;
            public float roughness = 0.86f;
            public float metallic;
        }
    }
}
