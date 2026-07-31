#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HorrorGame.EditorTools.Rendering
{
    /// <summary>
    /// Gives every fitting in the building a visible source: a filament halo at the
    /// bulb, and a dust-lit shaft hanging under it.
    /// <para>
    /// <b>Why this is a mechanic and not decoration.</b> §03 makes darkness the lock on
    /// the objective and light the key, and §04 sells zone lighting to the 정비공 as an
    /// ability with a material cost. Both of those assume a player can look down a
    /// corridor and see *that there is a light there*. A point light on its own cannot
    /// say that: it puts a disc on the floor and leaves the source invisible, so the
    /// room reads as lit by nothing and a fitting that could be switched on looks
    /// identical to one that could not.
    /// </para>
    /// <para>
    /// <b>Why geometry.</b> URP 17 has no volumetric fog, so there is no setting that
    /// makes a beam visible in air (ART.md §7.10). The alternatives are a light cookie —
    /// which URP wants as a *cubemap* for a point light, and every fitting here is a
    /// point light — or geometry. This is geometry: three crossed quads at the filament
    /// and two below it, additively blended, textured with the two sprites the texture
    /// generator writes for exactly this purpose.
    /// </para>
    /// <para>
    /// <b>Additive, and the falloff is in RGB.</b> An additive blend is
    /// <c>src + dst</c> and never looks at alpha, so a sprite carrying its shape in the
    /// alpha channel renders as a solid rectangle of light. The generator therefore
    /// writes the falloff into the colour channels — see <c>build_glow_point</c>.
    /// </para>
    /// <para>
    /// Only lights that are actually on get one. A dark bulb that glows is worse than
    /// no bulb at all: §04's ability is "pay to turn this zone on", and it is
    /// meaningless if the zone already looks lit.
    /// </para>
    /// </summary>
    public static class PracticalGlow
    {
        /// <summary>Child of the map root that holds every glow mesh.</summary>
        public const string RootName = "Practicals";

        private const string ManifestPath = "Assets/Textures/Decals.manifest.json";
        private const string TextureRoot = "Assets/Textures";
        private const string MaterialRoot = "Assets/Textures/Materials";

        /// <summary>
        /// How hard the filament halo is driven, as a multiple of the fitting's own
        /// colour.
        /// <para>
        /// Above the bloom threshold on purpose — <c>AtmosphereSetup</c> sets it to
        /// 0.55 — because the halo is what makes a bare bulb read as a *source* rather
        /// than as a bright patch of ceiling, and bloom is the lens doing the last part
        /// of that. Kept low enough that the sprite's own core is the only part that
        /// clips: the frame budget in ART.md allows under 0.5 % of pixels above 250,
        /// and a hundred and fifteen fittings can spend that quickly.
        /// </para>
        /// </summary>
        private const float HaloGain = 1.35f;

        /// <summary>
        /// How hard the shaft is driven. Two orders of restraint below the halo.
        /// <para>
        /// A shaft is *air*, and air lit by a 1.1-intensity bulb at three metres is
        /// barely there. The failure mode of every fake volumetric is a solid cone, and
        /// a solid cone is worse than no cone because it reads as a modelling error
        /// rather than as light. It should be findable and never obvious.
        /// <br/>
        /// 0.16 → 0.30, measured against a render with §04's zone lighting forced on
        /// (<c>-litZones</c>): at 0.16 the shafts were placed, counted and reported, and
        /// could not be found in the frame at all. Invisible is the other failure mode
        /// and it costs the same number of triangles.
        /// </para>
        /// </summary>
        private const float ShaftGain = 0.30f;

        /// <summary>Places or replaces every glow in the open scene, and reports.</summary>
        public static string Place()
        {
            var mapRoot = GameObject.Find("Map");
            if (mapRoot == null)
            {
                return "no 'Map' root in the open scene — no fittings to light.";
            }

            var library = LoadLibrary();
            var halo = library.Find("Glow_Point");
            var shaft = library.Find("Glow_Shaft");
            if (halo == null || shaft == null)
            {
                return "the decal manifest carries no light sprites; run gen_textures.py.";
            }

            var stale = mapRoot.transform.Find(RootName);
            if (stale != null)
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }

            Physics.SyncTransforms();

            var batches = new Dictionary<string, GlowBatch>(StringComparer.Ordinal);
            var haloes = 0;
            var shafts = 0;
            var dark = 0;

            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (light.type != LightType.Point)
                {
                    continue;
                }

                if (!light.enabled || !light.gameObject.activeInHierarchy)
                {
                    dark++;
                    continue;
                }

                var position = light.transform.position;
                var colour = light.color;

                // A big fitting is a big glass, so the halo is sized off the light's
                // reach rather than being one number for the building. That is also
                // what keeps §04's zone lights from looking like the same bulb as the
                // caged filaments the dressing hangs.
                var size = Mathf.Clamp(light.range * 0.075f, 0.26f, 0.72f);
                Crossed(batches, halo, colour * HaloGain, position, size);
                haloes++;

                // Only where there is room for one to be seen. A shaft in a 30 cm gap
                // between a fitting and a crate is two quads of overdraw and nothing
                // else.
                var drop = Physics.Raycast(position + Vector3.down * 0.05f, Vector3.down,
                                           out var floor, 4f, ~0, QueryTriggerInteraction.Ignore)
                    ? floor.distance
                    : 0f;

                if (drop >= 1.6f)
                {
                    var height = Mathf.Clamp(Mathf.Min(drop * 0.85f, light.range * 0.45f), 1.2f, 2.6f);
                    Shaft(batches, shaft, colour * ShaftGain, position, height, height * 0.9f);
                    shafts++;
                }
            }

            if (batches.Count == 0)
            {
                return "no lit fittings found (" + dark + " switched off).";
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(mapRoot.transform, false);

            var meshes = 0;
            foreach (var pair in batches.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                pair.Value.Build(root.transform);
                meshes++;
            }

            return "practicals: " + haloes + " filament halo(es), " + shafts + " shaft(s), "
                   + dark + " fitting(s) left dark because they are switched off; "
                   + meshes + " mesh(es).";
        }

        // ====================================================================
        // Geometry.
        // ====================================================================

        /// <summary>
        /// Three quads on the three principal planes, sharing a centre.
        /// <para>
        /// A billboard would be one quad and would need a component running every frame
        /// to face the camera — a runtime script, in an assembly this pass does not own,
        /// on a hundred and fifteen objects. Three static crossed quads cost two extra
        /// triangles each and are indistinguishable from a billboard for a radially
        /// symmetric sprite, which is what a filament halo is.
        /// </para>
        /// </summary>
        private static void Crossed(Dictionary<string, GlowBatch> batches, GlowEntry entry,
                                    Color colour, Vector3 centre, float size)
        {
            var half = size * 0.5f;
            Quad(batches, entry, colour, centre, Vector3.right * half, Vector3.up * half);
            Quad(batches, entry, colour, centre, Vector3.forward * half, Vector3.up * half);
            Quad(batches, entry, colour, centre, Vector3.right * half, Vector3.forward * half);
        }

        /// <summary>
        /// Two crossed quads hanging below a fitting, with the sprite's V axis pointing
        /// down the shaft so its fade lands at the bottom.
        /// </summary>
        private static void Shaft(Dictionary<string, GlowBatch> batches, GlowEntry entry,
                                  Color colour, Vector3 top, float height, float width)
        {
            var centre = top + Vector3.down * (height * 0.5f);
            var down = Vector3.down * (height * 0.5f);
            Quad(batches, entry, colour, centre, Vector3.right * (width * 0.5f), down);
            Quad(batches, entry, colour, centre, Vector3.forward * (width * 0.5f), down);
        }

        private static void Quad(Dictionary<string, GlowBatch> batches, GlowEntry entry,
                                 Color colour, Vector3 centre, Vector3 across, Vector3 along)
        {
            // Batched by colour as well as by kind and storey, because §12 tints the
            // fittings per zone (ART.md §3.6) and one merged mesh can only carry one
            // material. Rounding to two decimals is what keeps five zone tints from
            // becoming a hundred and fifteen materials.
            var key = entry.name + "_L" + Mathf.RoundToInt(centre.y / 3.75f)
                      + "_" + Mathf.RoundToInt(colour.r * 100f)
                      + "_" + Mathf.RoundToInt(colour.g * 100f)
                      + "_" + Mathf.RoundToInt(colour.b * 100f);

            if (!batches.TryGetValue(key, out var batch))
            {
                batch = new GlowBatch(entry, colour, key);
                batches[key] = batch;
            }

            batch.Quads.Add((centre, across, along));
        }

        private sealed class GlowBatch
        {
            public readonly List<(Vector3 Centre, Vector3 Across, Vector3 Along)> Quads =
                new List<(Vector3, Vector3, Vector3)>();

            private readonly GlowEntry _entry;
            private readonly Color _colour;
            private readonly string _key;

            public GlowBatch(GlowEntry entry, Color colour, string key)
            {
                _entry = entry;
                _colour = colour;
                _key = key;
            }

            public void Build(Transform parent)
            {
                var go = new GameObject(_key);
                go.transform.SetParent(parent, false);

                var vertices = new Vector3[Quads.Count * 4];
                var normals = new Vector3[Quads.Count * 4];
                var uv = new Vector2[Quads.Count * 4];
                var triangles = new int[Quads.Count * 6];

                for (var i = 0; i < Quads.Count; i++)
                {
                    var (centre, across, along) = Quads[i];
                    var v = i * 4;
                    vertices[v + 0] = centre - across + along;
                    vertices[v + 1] = centre + across + along;
                    vertices[v + 2] = centre + across - along;
                    vertices[v + 3] = centre - across - along;

                    // V = 0 at the end the sprite calls "the source". For the shaft that
                    // is the top; for the halo it does not matter, because the sprite is
                    // radially symmetric.
                    uv[v + 0] = new Vector2(0f, 0f);
                    uv[v + 1] = new Vector2(1f, 0f);
                    uv[v + 2] = new Vector2(1f, 1f);
                    uv[v + 3] = new Vector2(0f, 1f);

                    var normal = Vector3.Cross(across, along).normalized;
                    for (var k = 0; k < 4; k++)
                    {
                        normals[v + k] = normal;
                    }

                    var t = i * 6;
                    triangles[t + 0] = v + 0;
                    triangles[t + 1] = v + 1;
                    triangles[t + 2] = v + 2;
                    triangles[t + 3] = v + 0;
                    triangles[t + 4] = v + 2;
                    triangles[t + 5] = v + 3;
                }

                var mesh = new Mesh { name = _key };
                mesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.uv = uv;
                mesh.triangles = triangles;
                mesh.RecalculateBounds();

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = MaterialFor(_entry, _colour);

                // Light in air occludes nothing and is lit by nothing.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        // ====================================================================
        // Materials.
        // ====================================================================

        /// <summary>
        /// One unlit additive material per sprite and zone tint, created on demand.
        /// <para>
        /// Unlit here and Lit for the decals, and the difference is the point: a stain
        /// is a surface and has to be found by the beam, whereas a halo *is* light and
        /// must not be lit by anything. Double cull so a shaft reads from both sides,
        /// and depth writing off so two crossed quads do not clip each other.
        /// </para>
        /// </summary>
        private static Material MaterialFor(GlowEntry entry, Color colour)
        {
            var name = entry.name + "_" + Mathf.RoundToInt(colour.r * 100f)
                       + "_" + Mathf.RoundToInt(colour.g * 100f)
                       + "_" + Mathf.RoundToInt(colour.b * 100f);
            var path = MaterialRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                throw new InvalidOperationException("URP's Unlit shader is missing.");
            }

            if (material == null)
            {
                if (!AssetDatabase.IsValidFolder(MaterialRoot))
                {
                    AssetDatabase.CreateFolder("Assets/Textures", "Materials");
                }

                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", Load(entry.map));
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");

            // In front of the decals and behind nothing: a halo is the last thing drawn
            // in the room it lights.
            material.renderQueue = (int)RenderQueue.Transparent + 10;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture Load(string relative)
        {
            var path = TextureRoot + "/" + relative;
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (texture == null)
            {
                throw new FileNotFoundException(
                    "Light sprite missing: " + path + ". Run tools/textures/gen_textures.py.");
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.wrapMode != TextureWrapMode.Clamp)
            {
                // Clamped for the same reason the decals are, and it matters more here:
                // a repeating halo paints a grid of bulbs across the whole quad.
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            }

            return texture;
        }

        private static Library LoadLibrary()
        {
            if (!File.Exists(ManifestPath))
            {
                throw new FileNotFoundException(
                    "No " + ManifestPath + ". Run tools/textures/gen_textures.py first.");
            }

            return JsonUtility.FromJson<Library>(File.ReadAllText(ManifestPath));
        }

#pragma warning disable CS8618
        [Serializable]
        private sealed class Library
        {
            public GlowEntry[] glows;

            public GlowEntry? Find(string name) =>
                glows?.FirstOrDefault(g => string.Equals(g.name, name, StringComparison.Ordinal));
        }

        [Serializable]
        private sealed class GlowEntry
        {
            public string name;
            public string map;
        }
#pragma warning restore CS8618
    }
}
