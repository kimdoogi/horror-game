#nullable enable

using System;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.TextureImport
{
    /// <summary>
    /// Import settings for everything <c>tools/textures/gen_textures.py</c> writes.
    /// <para>
    /// These are not cosmetic preferences — three of them are correctness. An
    /// albedo imported without sRGB is decoded twice and lands roughly 0.2 linear
    /// too dark, which is precisely the "the beam hits a wall and nothing happens"
    /// failure the whole texture pass exists to fix. A roughness or AO map
    /// imported *with* sRGB is a data channel bent through a display curve. And a
    /// normal map imported as a colour texture is not swizzled into Unity's
    /// DXT5nm layout, so every surface lights as though lit from the wrong side.
    /// </para>
    /// <para>
    /// Doing it here rather than in a checked-in <c>.meta</c> means a regenerated
    /// texture cannot arrive with the wrong settings: the generator can rewrite
    /// the PNGs freely and the import is re-derived from the file name.
    /// </para>
    /// </summary>
    public sealed class ProceduralTextureImport : AssetPostprocessor
    {
        /// <summary>Only files under here are ours to configure.</summary>
        public const string TextureRoot = "Assets/Textures/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureRoot, StringComparison.Ordinal))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            var kind = MapKindOf(assetPath);
            if (kind == MapKind.Unknown)
            {
                return;
            }

            importer.textureType = kind == MapKind.Normal
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;

            // Only the albedo carries display-referred colour. Everything else is
            // measurement — slope, roughness, occlusion, metallic — and a gamma
            // curve on a measurement is simply a wrong number.
            importer.sRGBTexture = kind == MapKind.Albedo;

            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.filterMode = FilterMode.Trilinear;

            // §05 is first person at 1.63 m, so floors are always seen at a
            // grazing angle. Without anisotropy the plank and tile lines blur to
            // mush two metres ahead, which is exactly where a player is looking.
            importer.anisoLevel = 8;

            if (kind == MapKind.MetallicSmoothness)
            {
                // Smoothness rides in alpha, so the alpha channel has to survive
                // import and must not be treated as transparency.
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
            }

            var settings = importer.GetDefaultPlatformTextureSettings();
            settings.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(settings);
        }

        /// <summary>What a generated map is, read off the suffix the generator writes.</summary>
        public enum MapKind
        {
            Unknown,
            Albedo,
            Normal,
            Roughness,
            Occlusion,
            MetallicSmoothness,
        }

        /// <summary>Classifies a generated texture by its file-name suffix.</summary>
        public static MapKind MapKindOf(string path)
        {
            if (path.EndsWith("_albedo.png", StringComparison.OrdinalIgnoreCase)) return MapKind.Albedo;
            if (path.EndsWith("_normal.png", StringComparison.OrdinalIgnoreCase)) return MapKind.Normal;
            if (path.EndsWith("_rough.png", StringComparison.OrdinalIgnoreCase)) return MapKind.Roughness;
            if (path.EndsWith("_ao.png", StringComparison.OrdinalIgnoreCase)) return MapKind.Occlusion;
            if (path.EndsWith("_ms.png", StringComparison.OrdinalIgnoreCase)) return MapKind.MetallicSmoothness;
            return MapKind.Unknown;
        }
    }
}
