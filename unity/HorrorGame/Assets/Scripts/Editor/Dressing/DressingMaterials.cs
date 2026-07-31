#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Dressing
{
    /// <summary>
    /// Builds the URP materials the dressing kit is rendered with, from the values
    /// the Blender generator actually authored.
    /// <para>
    /// This exists because FBX does not carry a PBR material. Blender writes a
    /// Principled BSDF with a base colour, a roughness and a metallic value; the FBX
    /// format has a Lambert/Phong slot, so Unity's importer sees a diffuse colour and
    /// nothing else. Every dressing piece would arrive at roughness ~0.5, metallic 0,
    /// no texture — which is exactly the "every surface is flat colour" complaint this
    /// pass exists to answer. So the manifest carries the real numbers and this class
    /// rebuilds them on the Unity side, bound by material name.
    /// </para>
    /// <para>
    /// It also generates the kit's only textures. §03 lights the building with a 12 m
    /// cone in near-black ambient, and an untextured surface under a moving spot light
    /// reads as a flat gradient — there is no high-frequency detail for the specular
    /// term to break up, so a steel drum and a wooden crate differ only in hue. Two
    /// deterministic value-noise maps (a grime mask and a normal derived from the same
    /// height field) at a per-material tiling give the beam something to catch. They
    /// are generated rather than authored so the repo stays script-reproducible, which
    /// is the rule <c>tools/blender/blendkit.py</c> sets for every other asset here.
    /// </para>
    /// </summary>
    public static class DressingMaterials
    {
        /// <summary>Where generated material and texture assets go. Inside the kit's own folder, so it is unambiguously this pass's output.</summary>
        public const string MaterialRoot = DressingManifest.KitRoot + "/Materials";

        private const string GrimeTexturePath = MaterialRoot + "/Dressing_Grime.png";
        private const string NormalTexturePath = MaterialRoot + "/Dressing_GrimeNormal.png";

        /// <summary>Side of the generated noise maps, texels. 256 is enough at the tiling used and costs 200 KB.</summary>
        private const int TextureSize = 256;

        /// <summary>
        /// Seed for the noise. Fixed, because the textures are checked in as assets and
        /// a regenerated file that differs byte-for-byte is a spurious diff.
        /// </summary>
        private const int NoiseSeed = 20250731;

        /// <summary>Creates (or refreshes) every material named by the manifest and returns them by name.</summary>
        public static Dictionary<string, Material> Build(DressingKit kit)
        {
            EnsureFolder();
            var grime = LoadOrCreateGrime();
            var normal = LoadOrCreateNormal();

            var byName = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var spec in kit.materials)
            {
                byName[spec.name] = LoadOrCreate(spec, grime, normal);
            }

            return byName;
        }

        /// <summary>
        /// Rebinds a placed piece's renderers to the generated materials.
        /// <para>
        /// Matched by name against the FBX's own slots. A slot with no match is left
        /// alone rather than blanked: a missing material is a visible mistake, and a
        /// magenta prop is easier to notice than a silently re-skinned one.
        /// </para>
        /// </summary>
        /// <returns>How many slots were rebound.</returns>
        public static int Apply(GameObject instance, IReadOnlyDictionary<string, Material> byName)
        {
            var bound = 0;
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var shared = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == null)
                    {
                        continue;
                    }

                    if (byName.TryGetValue(shared[i].name, out var replacement) && replacement != null)
                    {
                        shared[i] = replacement;
                        changed = true;
                        bound++;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = shared;
                }
            }

            return bound;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(MaterialRoot))
            {
                AssetDatabase.CreateFolder(DressingManifest.KitRoot, "Materials");
            }
        }

        private static Material LoadOrCreate(DressingMaterial spec, Texture2D? grime, Texture2D? normal)
        {
            var path = MaterialRoot + "/" + spec.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var created = false;
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = spec.name };
                created = true;
            }

            var colour = new Color(spec.r, spec.g, spec.b, 1f);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", colour);
            }

            material.color = colour;

            // URP's smoothness is the complement of Blender's roughness. Getting this
            // backwards turns the kit's one mirror — standing water, §03's "물이 있는
            // 층" — into the roughest surface in the building.
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", Mathf.Clamp01(1f - spec.roughness));
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", Mathf.Clamp01(spec.metallic));
            }

            // Water is the one place a real specular highlight has to survive a rough
            // normal map: a puddle only reads as water because it mirrors the beam.
            // Water skips both maps. A grime mask on a puddle is dirt floating on the
            // surface and a normal map on one is ripples that never move; both kill the
            // single thing the material is for, which is mirroring the flashlight.
            var wet = spec.roughness <= 0.25f;

            if (wet && material.HasProperty("_EnvironmentReflections"))
            {
                // Environment reflections off, direct specular on. There is no reflection
                // probe inside this building, so URP falls back to the skybox — and the
                // skybox is a dusk sky. Every puddle became a flat white cut-out of it at
                // the grazing angle a standing player sees a floor from, which reads as a
                // hole in the level rather than as water. With the fallback off, a pool
                // is black until a flashlight or a working bulb crosses it and then it is
                // the brightest thing in the corridor — which is exactly the read §03's
                // "그것은 물이 있는 층에 있다" needs, and it is the beam doing the work.
                // Metals keep their environment term: the skybox is wrong for them too,
                // but it is what currently makes a galvanised pipe visible at 8 m, and
                // removing it would take the corridor's only depth cue with it. That one
                // is a note for whoever adds the probe, not something to fix here.
                material.SetFloat("_EnvironmentReflections", 0f);
                material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
            if (grime != null && material.HasProperty("_BaseMap") && !wet)
            {
                material.SetTexture("_BaseMap", grime);
                var tiling = TilingFor(spec.name);
                material.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            }

            if (normal != null && material.HasProperty("_BumpMap") && !wet)
            {
                material.EnableKeyword("_NORMALMAP");
                material.SetTexture("_BumpMap", normal);
                var tiling = TilingFor(spec.name);
                material.SetTextureScale("_BumpMap", new Vector2(tiling, tiling));
                // Metal keeps a strong normal — that is where the beam's highlight
                // breaks up. Cloth and paper get almost none; a bumpy dust sheet reads
                // as a rock.
                material.SetFloat("_BumpScale", spec.metallic > 0.5f ? 0.6f : 0.4f);
            }

            if (spec.emission > 0f && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                // Blender's emission strength is a multiplier on the colour, and URP's
                // is the colour's own intensity. §03 makes darkness the lock, so a
                // fitting that is merely *visible* is right and one that lights the
                // corridor is a design bug — the strength is divided down rather than
                // passed through.
                material.SetColor("_EmissionColor", colour * (spec.emission * 0.25f));
            }
            else if (material.HasProperty("_EmissionColor"))
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }

            if (created)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>
        /// How many times the shared noise repeats across a piece.
        /// <para>
        /// Derived from the material name so it is stable across runs and different per
        /// surface. One shared texture at one tiling on every material is only slightly
        /// better than no texture: the eye finds the repeat immediately, and a corridor
        /// of props that all shimmer in step looks worse than flat colour.
        /// </para>
        /// <para>
        /// The band is 4–10 rather than 1–3, and the reason is worth writing down.
        /// Blender's smart-project scales a joined mesh's UVs so the whole piece fits
        /// roughly in 0..1, which on a 2.5 m pipe run puts one UV unit at about 3 m. At
        /// tiling 1–3 the grime repeats every metre, and a metre-scale blotch on a
        /// 14 cm pipe does not read as dirt — it reads as a badly painted lump, which is
        /// exactly how the first pass looked in a beam. Four to ten repeats puts the
        /// pattern at 30–75 cm, which is grime.
        /// </para>
        /// </summary>
        private static float TilingFor(string name)
        {
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < name.Length; i++)
                {
                    hash = (hash * 31) + name[i];
                }

                return 4f + (Mathf.Abs(hash) % 7);
            }
        }

        /// <summary>
        /// Writes the grime map.
        /// <para>
        /// Regenerated on every run rather than reused when present. The recipe lives a
        /// few lines above the file it produces, so a cached PNG is a silent way for the
        /// two to disagree — and the symptom, dressing that is subtly blotchier than the
        /// code says, is exactly the kind of thing nobody thinks to blame on a stale
        /// asset. Two 256² maps cost milliseconds.
        /// </para>
        /// </summary>
        private static Texture2D? LoadOrCreateGrime()
        {
            var height = Heightfield();
            var pixels = new Color32[TextureSize * TextureSize];
            for (var i = 0; i < pixels.Length; i++)
            {
                // Stays near white and only darkens. The map multiplies the base colour,
                // so anything that brightens would push the kit's pale surfaces past 1.0
                // and flatten exactly the highlights it is meant to create.
                var v = Mathf.Lerp(0.78f, 1.0f, height[i]);
                var t = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
                pixels[i] = new Color32(t, t, t, 255);
            }

            return WritePng(GrimeTexturePath, pixels, isNormal: false);
        }

        private static Texture2D? LoadOrCreateNormal()
        {
            var height = Heightfield();
            var pixels = new Color32[TextureSize * TextureSize];
            const float Strength = 1.6f;

            for (var y = 0; y < TextureSize; y++)
            {
                for (var x = 0; x < TextureSize; x++)
                {
                    var l = height[Index(x - 1, y)];
                    var r = height[Index(x + 1, y)];
                    var d = height[Index(x, y - 1)];
                    var u = height[Index(x, y + 1)];
                    var n = new Vector3((l - r) * Strength, (d - u) * Strength, 1f).normalized;
                    pixels[Index(x, y)] = new Color32(
                        (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f),
                        255);
                }
            }

            return WritePng(NormalTexturePath, pixels, isNormal: true);
        }

        private static Texture2D? WritePng(string path, Color32[] pixels, bool isNormal)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            var bytes = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);

            var full = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.sRGBTexture = !isNormal;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// A deterministic multi-octave value-noise height field in [0,1].
        /// <para>
        /// Value noise rather than Unity's Perlin because <c>Mathf.PerlinNoise</c> is
        /// not contractual across Unity versions, and these textures are committed
        /// assets — a regenerated file that differs is a diff nobody can explain.
        /// </para>
        /// </summary>
        private static float[] Heightfield()
        {
            var field = new float[TextureSize * TextureSize];
            var min = float.MaxValue;
            var max = float.MinValue;

            for (var y = 0; y < TextureSize; y++)
            {
                for (var x = 0; x < TextureSize; x++)
                {
                    var sum = 0f;
                    var amplitude = 1f;
                    var frequency = 4;
                    for (var octave = 0; octave < 5; octave++)
                    {
                        sum += ValueNoise(x, y, frequency, octave) * amplitude;
                        amplitude *= 0.5f;
                        frequency *= 2;
                    }

                    field[Index(x, y)] = sum;
                    min = Mathf.Min(min, sum);
                    max = Mathf.Max(max, sum);
                }
            }

            var span = Mathf.Max(1e-5f, max - min);
            for (var i = 0; i < field.Length; i++)
            {
                field[i] = (field[i] - min) / span;
            }

            return field;
        }

        private static float ValueNoise(int x, int y, int cells, int octave)
        {
            var step = (float)TextureSize / cells;
            var fx = x / step;
            var fy = y / step;
            var x0 = Mathf.FloorToInt(fx);
            var y0 = Mathf.FloorToInt(fy);
            var tx = Smooth(fx - x0);
            var ty = Smooth(fy - y0);

            var a = Lattice(x0, y0, cells, octave);
            var b = Lattice(x0 + 1, y0, cells, octave);
            var c = Lattice(x0, y0 + 1, cells, octave);
            var d = Lattice(x0 + 1, y0 + 1, cells, octave);

            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        private static float Lattice(int x, int y, int cells, int octave)
        {
            // Wrapped so the map tiles: the pieces this lands on are repeated all over
            // the building and a visible seam per prop would be worse than no texture.
            x = ((x % cells) + cells) % cells;
            y = ((y % cells) + cells) % cells;

            unchecked
            {
                var h = NoiseSeed + (x * 374761393) + (y * 668265263) + (octave * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private static int Index(int x, int y)
        {
            x = ((x % TextureSize) + TextureSize) % TextureSize;
            y = ((y % TextureSize) + TextureSize) % TextureSize;
            return (y * TextureSize) + x;
        }
    }
}
