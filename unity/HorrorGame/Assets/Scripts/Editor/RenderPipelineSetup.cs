#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Creates the URP render pipeline asset and makes it the active pipeline.
    /// <para>
    /// Installing the URP package is only half the job. Until a
    /// <see cref="UniversalRenderPipelineAsset"/> is assigned in Graphics Settings the
    /// project still renders through the built-in pipeline, and anything using a URP
    /// shader draws as flat magenta — Unity's marker for "this shader cannot run in
    /// the active pipeline". The package being present is what makes the failure
    /// confusing: <c>Shader.Find("Universal Render Pipeline/Lit")</c> succeeds, so the
    /// material looks fine in the inspector and renders as an error in the game view.
    /// </para>
    /// <para>
    /// URP is the right pipeline for this game rather than a preference. §03 makes
    /// darkness the lock on progress, and a match can have four flashlights, a zone
    /// light and a burning flare live at once. URP's Forward+ path handles many
    /// real-time lights per object; the built-in forward path runs out of per-object
    /// light slots exactly when the game gets interesting.
    /// </para>
    /// </summary>
    public static class RenderPipelineSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string PipelineAssetPath = SettingsFolder + "/HorrorGame_URP.asset";
        private const string RendererDataPath = SettingsFolder + "/HorrorGame_URP_Renderer.asset";

        /// <summary>Creates the pipeline asset if needed and assigns it everywhere.</summary>
        [MenuItem("Horror/Setup/Configure URP Render Pipeline", priority = 12)]
        public static void Configure()
        {
            var asset = LoadOrCreatePipelineAsset();

            GraphicsSettings.defaultRenderPipeline = asset;

            // Every quality level needs it too. A level left on the built-in pipeline
            // silently overrides the graphics default whenever the player switches
            // quality, which produces a magenta scene that "only happens sometimes".
            var previous = QualitySettings.GetQualityLevel();
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                QualitySettings.renderPipeline = asset;
            }

            QualitySettings.SetQualityLevel(previous, applyExpensiveChanges: false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[RenderPipeline] URP is now the active pipeline (" + PipelineAssetPath + ").\n"
                + "  If anything is still magenta, its material is on a built-in shader — run\n"
                + "  Horror ▸ Setup ▸ Upgrade Materials To URP.");
        }

        /// <summary>
        /// Repoints every material in Assets/ that still uses a built-in shader at the
        /// matching URP shader, preserving the colour and texture it already had.
        /// </summary>
        [MenuItem("Horror/Setup/Upgrade Materials To URP", priority = 13)]
        public static void UpgradeMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");

            if (lit == null)
            {
                Debug.LogError("[RenderPipeline] URP shaders not found. Is the package installed?");
                return;
            }

            // Built-in shader name → the URP shader that replaces it.
            var replacements = new Dictionary<string, Shader>
            {
                { "Standard", lit },
                { "Standard (Specular setup)", lit },
                { "Autodesk Interactive", lit },
                { "Legacy Shaders/Diffuse", lit },
                { "Legacy Shaders/Specular", lit },
                { "Legacy Shaders/Bumped Diffuse", lit },
                { "Legacy Shaders/Bumped Specular", lit },
                { "Unlit/Color", unlit },
                { "Unlit/Texture", unlit },
            };

            var upgraded = 0;
            var skipped = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null)
                {
                    continue;
                }

                var name = material.shader.name;
                if (name.StartsWith("Universal Render Pipeline/", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (!replacements.TryGetValue(name, out var target) || target == null)
                {
                    skipped.Add(Path.GetFileName(path) + " (" + name + ")");
                    continue;
                }

                // Read the properties worth keeping before the shader swap discards
                // any that the new shader does not declare.
                var colour = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                var mainTex = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                var metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
                var smoothness = material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : 0.5f;

                material.shader = target;

                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", colour);
                }

                if (mainTex != null && material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", mainTex);
                }

                if (material.HasProperty("_Metallic"))
                {
                    material.SetFloat("_Metallic", metallic);
                }

                if (material.HasProperty("_Smoothness"))
                {
                    material.SetFloat("_Smoothness", smoothness);
                }

                EditorUtility.SetDirty(material);
                upgraded++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[RenderPipeline] Upgraded " + upgraded + " material(s) to URP."
                + (skipped.Count > 0
                    ? "\n  Left alone (unrecognised shader, check by hand): " + string.Join(", ", skipped.Take(12))
                    : string.Empty));
        }

        /// <summary>Reports the current pipeline without changing anything.</summary>
        [MenuItem("Horror/Setup/Report Render Pipeline", priority = 14)]
        public static void Report()
        {
            var active = GraphicsSettings.currentRenderPipeline;
            var builtin = active == null;

            var magenta = AssetDatabase.FindAssets("t:Material", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Material>)
                .Where(m => m != null && m.shader != null)
                .Count(m => builtin
                    ? m.shader.name.StartsWith("Universal Render Pipeline/", System.StringComparison.Ordinal)
                    : !m.shader.name.StartsWith("Universal Render Pipeline/", System.StringComparison.Ordinal)
                      && m.shader.name != "Skybox/Procedural");

            Debug.Log("[RenderPipeline] active: " + (builtin ? "Built-in" : active!.name)
                + "\n  materials that will render magenta under this pipeline: " + magenta
                + (magenta > 0 ? "\n  Fix with Horror ▸ Setup ▸ Configure URP Render Pipeline, then Upgrade Materials To URP." : string.Empty));
        }

        private static UniversalRenderPipelineAsset LoadOrCreatePipelineAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (existing != null)
            {
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererDataPath);
            }

            var asset = UniversalRenderPipelineAsset.Create(rendererData);
            asset.name = "HorrorGame_URP";

            // Forward+ is the reason URP was chosen here — see the class summary.
            // Classic Forward caps the lights that can affect one object, and this
            // game routinely has four flashlights plus a zone light in one corridor.
            asset.supportsHDR = true;
            asset.shadowDistance = 45f;   // §12 sights are 15–25 m; beyond that shadows are wasted.
            asset.msaaSampleCount = 2;

            AssetDatabase.CreateAsset(asset, PipelineAssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[RenderPipeline] Created " + PipelineAssetPath);
            return asset;
        }
    }
}
