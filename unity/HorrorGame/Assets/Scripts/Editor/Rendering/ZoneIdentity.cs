#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Rendering
{
    /// <summary>
    /// Gives each §12 zone its own walls, so a still frame taken above knee height can
    /// answer "which floor am I on".
    /// <para>
    /// <b>The defect this closes.</b> ART.md §7.3: "Five zones, five floors, and the
    /// floors genuinely do read now. Everything above knee height does not: the same
    /// brick wall, the same square hall, the same single central pillar." §12 makes
    /// telling the zones apart a *gameplay* requirement — 구역별로 바닥 재질이 달라야
    /// 청음사가 위치를 판별할 수 있다 — and the floor alone only delivers it to a player
    /// who is looking down, which is the one direction a player being chased is not
    /// looking.
    /// </para>
    /// <para>
    /// <b>Why tint and not new textures.</b> A second full wall set per zone is five
    /// times the texture memory to solve a problem that is mostly about *hue*: a room
    /// is recognised as a place long before its masonry bond is legible, and in a
    /// building lit by one torch the bond is never legible past two metres. One zone —
    /// 하역장, the loading bay — does get structurally different walls, because
    /// <c>Wall_Concrete_Bare</c> was already generated and sitting unbound in the
    /// manifest with the note "awaiting a per-zone wall slot".
    /// </para>
    /// <para>
    /// <b>Keyed on the floor material, not on the zone letter.</b> Zone letters come
    /// from the layout seed and move when it changes; §12's five 바닥 재질 are a
    /// contract that does not. So 저탄장 stays the coal-blackened one whichever letter
    /// this seed happens to call it.
    /// </para>
    /// <para>
    /// Only zone rooms are re-tinted. Corridors live under <c>Map/Shared</c> and keep
    /// the base brick deliberately: they are the connective tissue between places, and
    /// a building where the corridors also change colour has no places in it, only
    /// gradients.
    /// </para>
    /// </summary>
    public static class ZoneIdentity
    {
        private const string SourceRoot = "Assets/Textures/Materials";
        private const string VariantRoot = SourceRoot + "/Zones";

        /// <summary>
        /// The materials a zone is allowed to re-skin, and what it re-skins them from.
        /// Floors are deliberately absent: they are §04's Listener channel and §12's
        /// checklist rule, and nothing here may touch them.
        /// </summary>
        private static readonly string[] Skinnable =
        {
            "Wall_Brick_Painted", "Wall_Plaster_Stained", "Ceiling_Concrete_Formed",
        };

        /// <summary>What each §12 zone's rooms are made of and what has happened to them.</summary>
        private readonly struct ZoneLook
        {
            public ZoneLook(string floor, string place, Color tint, float smoothness,
                            float bump, float occlusion, string wallOverride, string why)
            {
                Floor = floor;
                Place = place;
                Tint = tint;
                Smoothness = smoothness;
                Bump = bump;
                Occlusion = occlusion;
                WallOverride = wallOverride;
                Why = why;
            }

            public string Floor { get; }
            public string Place { get; }
            public Color Tint { get; }
            public float Smoothness { get; }
            public float Bump { get; }
            public float Occlusion { get; }

            /// <summary>A different wall material entirely, or "" to tint the brick.</summary>
            public string WallOverride { get; }

            public string Why { get; }
        }

        /// <summary>
        /// One row per §12 floor material.
        /// <para>
        /// Luminance moves by at most +22 % and −3 %, and that asymmetry is not timidity. ART.md
        /// holds every zone view to 10–40 % of the frame crushed and 30–75 % legible,
        /// and three of the five were outside that band on the low side before any of
        /// this. So the two zones whose character is "pale" pay for the identity and the
        /// two whose character is "dark" are not allowed to: a tint that made 저탄장
        /// properly black would be the right decision about coal and the wrong one about
        /// a game whose entire difficulty is reading shape in the dark. Hue carries the
        /// identity in both directions; luminance only ever moves upward.
        /// </para>
        /// </summary>
        private static readonly ZoneLook[] Looks =
        {
            new ZoneLook(
                "Wood", "기록보관소", new Color(1.22f, 1.13f, 0.98f), 0.80f, 1.0f, 0.92f, "",
                "A dry paper store, limewashed cream and chalky. The palest zone in the "
                + "building on purpose: it measured the darkest of the five (40.6 % crushed, "
                + "25.9 % legible against a 30 % floor) and warm distemper over brick is what "
                + "an archive is actually finished in."),
            new ZoneLook(
                "Tile", "저수조", new Color(0.98f, 1.11f, 1.05f), 1.00f, 1.0f, 1.0f, "",
                "Institutional glazed green over the water tanks — the one wall in the "
                + "building that is *wiped down*, so it keeps its gloss and takes a "
                + "specular streak off §03's beam."),
            new ZoneLook(
                "Gravel", "저탄장", new Color(1.10f, 1.12f, 1.21f), 0.85f, 0.95f, 0.92f, "",
                "Damp limewash over brick, gone cold and blue — the flooded end of the coal "
                + "store, where the walls were painted white to get some light back and the "
                + "water has been at them ever since.\n"
                + "Arrived at by measuring three times, and the first two are worth keeping "
                + "because they were both the *right* decision about coal and the wrong one "
                + "about this game. Darkened 12 % with 15 % more occlusion: 39.5 % crushed → "
                + "49.8 %, 26.2 % legible → 20.1 %. Pulled back to 3 % darker: 42.3 % and "
                + "23.7 % — still worse than leaving it alone. This is the darkest zone view "
                + "in the building and it was already the one nearest ART.md's 40 % crushed "
                + "ceiling, so it is the one zone that cannot spend anything on looking like "
                + "what it is. Its identity is 45 mm of ballast underfoot, which is "
                + "unmistakable at any brightness; the walls now go the other way and pay "
                + "into the band instead of out of it."),
            new ZoneLook(
                "Concrete", "하역장", new Color(1.00f, 1.00f, 1.03f), 0.90f, 1.0f, 1.0f,
                "Wall_Concrete_Bare",
                "The only zone that gets a different wall rather than a different colour. "
                + "Board-formed concrete with snap-tie holes was generated for exactly this "
                + "and had no slot; the loading bay, nearest the surface and built to take "
                + "vehicles, is where a building stops being masonry."),
            new ZoneLook(
                "Metal", "기계실", new Color(1.08f, 0.99f, 0.85f), 1.00f, 1.10f, 1.05f, "",
                "Plant room: hard yellow lamp light on oil-stained walls. The warmest zone, "
                + "against 저수조's green two storeys down — ART.md §7.8 notes the building "
                + "is monotone, and these two are the poles it now runs between."),
        };

        /// <summary>
        /// Re-skins every zone room in the open scene and returns what changed.
        /// Idempotent: it swaps by source name, and a variant never matches one.
        /// </summary>
        public static string Apply()
        {
            var mapRoot = GameObject.Find("Map");
            if (mapRoot == null)
            {
                return "no 'Map' root in the open scene — no zones to tell apart.";
            }

            EnsureFolder();

            // Rebuild every variant asset first, unconditionally.
            //
            // This was a real bug and a quiet one. `Reskin` only calls `Variant` when it
            // finds a renderer still pointing at a *source* material, which after the
            // first run is never — that is what makes the pass idempotent. It also made
            // it impossible to re-tune: a changed tint in `Looks` produced a run that
            // reported "0 renderer material(s) re-skinned", exited 0, saved the scene and
            // rendered a frame identical to the previous one, down to the last decimal
            // of every measure. Two rounds of grading were spent before the identical
            // numbers were believed rather than explained away.
            var cache = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var look in Looks)
            {
                foreach (var source in Skinnable)
                {
                    Variant(cache, source == "Wall_Brick_Painted" && !string.IsNullOrEmpty(look.WallOverride)
                        ? look.WallOverride
                        : source, look);
                }
            }

            var lines = new List<string>();
            foreach (Transform zone in mapRoot.transform)
            {
                if (!zone.name.StartsWith("Zone_", StringComparison.Ordinal))
                {
                    continue;
                }

                var floor = zone.name.Substring(zone.name.LastIndexOf('_') + 1);
                var look = Looks.FirstOrDefault(l => string.Equals(l.Floor, floor, StringComparison.Ordinal));
                if (string.IsNullOrEmpty(look.Floor))
                {
                    lines.Add(zone.name + ": no look defined for floor '" + floor + "', left alone.");
                    continue;
                }

                var swapped = Reskin(zone, look, cache);
                lines.Add(zone.name + " → " + look.Place + ": " + swapped + " renderer material(s) re-skinned"
                          + (string.IsNullOrEmpty(look.WallOverride)
                              ? ""
                              : ", walls replaced with " + look.WallOverride));
            }

            AssetDatabase.SaveAssets();
            return "zone identity: " + cache.Count + " variant material(s) rebuilt\n    "
                   + string.Join("\n    ", lines);
        }

        private static int Reskin(Transform zone, ZoneLook look, Dictionary<string, Material> cache)
        {
            var swapped = 0;

            foreach (var renderer in zone.GetComponentsInChildren<MeshRenderer>(true))
            {
                var shared = renderer.sharedMaterials;
                var changed = false;

                for (var i = 0; i < shared.Length; i++)
                {
                    var current = shared[i];
                    if (current == null || !Skinnable.Contains(current.name))
                    {
                        continue;
                    }

                    var source = current.name;
                    if (source == "Wall_Brick_Painted" && !string.IsNullOrEmpty(look.WallOverride))
                    {
                        source = look.WallOverride;
                    }

                    var variant = Variant(cache, source, look);
                    if (variant == null || variant == current)
                    {
                        continue;
                    }

                    shared[i] = variant;
                    changed = true;
                    swapped++;
                }

                if (changed)
                {
                    renderer.sharedMaterials = shared;
                }
            }

            return swapped;
        }

        /// <summary>
        /// Loads or builds the zone's copy of one material.
        /// <para>
        /// A copy of the asset rather than a per-renderer property block, because the
        /// scene has to survive being saved and reopened and a block does not. It costs
        /// one material asset per zone per skinnable surface — fifteen in total — and
        /// nothing at runtime: every copy points at the same textures, so the SRP
        /// batcher still batches them and no extra texture memory is used at all.
        /// </para>
        /// </summary>
        private static Material? Variant(Dictionary<string, Material> cache, string source, ZoneLook look)
        {
            var key = source + "_" + look.Floor;
            if (cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var origin = AssetDatabase.LoadAssetAtPath<Material>(SourceRoot + "/" + source + ".mat");
            if (origin == null)
            {
                return null;
            }

            var path = VariantRoot + "/" + key + ".mat";
            var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (variant == null)
            {
                variant = new Material(origin) { name = key };
                AssetDatabase.CreateAsset(variant, path);
            }
            else
            {
                // Rebuilt from the source every run so a change to the base material —
                // a regenerated texture, a new detail normal — reaches the zones too.
                // Copying in place keeps the GUID, which the saved scene references.
                variant.CopyPropertiesFromMaterial(origin);
                variant.name = key;
            }

            variant.SetColor("_BaseColor", look.Tint);
            variant.SetFloat("_Smoothness", look.Smoothness);
            variant.SetFloat("_BumpScale", look.Bump);
            variant.SetFloat("_OcclusionStrength", look.Occlusion);
            EditorUtility.SetDirty(variant);

            cache[key] = variant;
            return variant;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(VariantRoot))
            {
                AssetDatabase.CreateFolder(SourceRoot, "Zones");
            }
        }
    }
}
