#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace HorrorGame.EditorTools.Rendering
{
    /// <summary>
    /// Places the marks a tiling material cannot carry: dirt where a prop meets a
    /// floor, damp running down a wall, wear through a route, a screed repair,
    /// standing water, soot above a bulb.
    /// <para>
    /// <b>Why decals rather than more texture.</b> Everything in a used building that
    /// says it has been used is registered to a *position*. A tiling material can say
    /// what a floor is made of; it cannot say that a crate stood here for ten years,
    /// because a tiling material puts that everywhere at once and it stops reading as
    /// history and starts reading as pattern. This was the largest remaining gap in
    /// the look: objects were placed rather than settled, and no amount of work on
    /// the base materials could have closed it.
    /// </para>
    /// <para>
    /// <b>Why it is here and not in the dressing pass.</b> ART.md §7.9 recorded decals
    /// as blocked on prop placement. They are not: they are blocked on prop placement
    /// having *happened*, which is a different thing. This runs at the end of the
    /// pipeline and reads the geometry the layout and dressing passes left behind —
    /// the baked NavMesh for where a person can walk, the renderers under
    /// <c>Map/Dressing</c> for where something stands, the point lights for where a
    /// bulb has been burning. Nothing here needs to change how a crate is scattered.
    /// </para>
    /// <para>
    /// <b>Mesh decals, not URP decal projectors.</b> A projector needs
    /// <c>DecalRendererFeature</c> on the renderer asset — another area's file — and
    /// costs a screen-space pass on every frame whether or not a decal is visible.
    /// These are quads lifted <see cref="LiftMetres"/> above the surface they sit on,
    /// merged into one mesh per kind per storey, and lit by URP's ordinary transparent
    /// path. They therefore respond to §03's beam, which is the whole point: a stain
    /// the flashlight does not find is not a clue.
    /// </para>
    /// <para>
    /// Deterministic in <paramref name="seed"/>, and idempotent — it deletes its own
    /// root before rebuilding, so running it twice cannot double the marks.
    /// </para>
    /// </summary>
    public static class ContactDecals
    {
        /// <summary>Child of the map root that holds every decal mesh.</summary>
        public const string RootName = "Decals";

        /// <summary>
        /// Metres a decal quad floats above the surface it belongs to.
        /// <para>
        /// Depth bias would be the tidier answer and is not available on URP's stock
        /// Lit shader, which is what these have to use to be lit by the flashlight. At
        /// 1.2 cm the parallax against the surface underneath is under a pixel beyond
        /// about two metres and invisible at grazing angles, while still clearing the
        /// depth quantisation of a 20 m room comfortably.
        /// </para>
        /// </summary>
        public const float LiftMetres = 0.012f;

        /// <summary>Fixes the scatter. Same seed, same marks in the same places.</summary>
        public const int DefaultSeed = 8801;

        private const string ManifestPath = "Assets/Textures/Decals.manifest.json";
        private const string TextureRoot = "Assets/Textures";
        private const string MaterialRoot = "Assets/Textures/Materials";

        // Densities, in square metres of walkable floor per mark. Chosen so the
        // building reads as used rather than as decorated: a player should be able to
        // walk a corridor and see two or three marks, not a gallery of them.
        private const float SquareMetresPerScuff = 20f;
        private const float SquareMetresPerPatch = 55f;
        private const float SquareMetresPerPuddle = 42f;
        private const float SquareMetresPerDrip = 95f;
        private const float SquareMetresPerWallStain = 30f;
        private const float SquareMetresPerRust = 46f;

        /// <summary>
        /// Metres of walkable floor per grime line at the foot of a wall.
        /// <para>
        /// Much the densest mark in the set, and the one that does the most work here.
        /// This map has ten loose props in it — every crate, panel and conduit run a
        /// frame shows is modelled into the kit piece — so "dirt under the thing that
        /// was standing there" had almost nothing to attach to. The wall/floor junction
        /// has hundreds of metres of it, it is where a brush never reaches in any real
        /// building, and it is the line the eye uses to decide whether a room has a
        /// floor or is a box with a texture on the bottom of it.
        /// </para>
        /// </summary>
        private const float SquareMetresPerWallBase = 22f;

        /// <summary>
        /// Global strength per kind, multiplied into the material's <c>_BaseColor</c>
        /// alpha.
        /// <para>
        /// The generator paints each decal at the density it would really have; this
        /// is the one dial that says how much of that this *game* wants, and it is
        /// here rather than in the generator because it is a judgement about the frame
        /// rather than about the material. Water is left at full strength — §03's
        /// first worked example of a clue is 물이 있는 층, so a puddle the player might
        /// not notice is a mechanic that does not fire.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, float> Strength =
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "Decal_Contact", 1.00f },
                { "Decal_WaterStain", 0.95f },
                { "Decal_Scuff", 0.85f },
                { "Decal_Patch", 0.80f },
                { "Decal_Puddle", 1.00f },
                { "Decal_Drip", 0.90f },
                { "Decal_Soot", 0.75f },
                { "Decal_Rust", 0.90f },
            };

        /// <summary>Menu entry — places decals in the open scene and leaves it dirty.</summary>
        [MenuItem("HorrorGame/Scene Gen/Place Contact Decals", priority = 12)]
        public static void Menu()
        {
            Debug.Log("[Decals] " + Place(DefaultSeed));
        }

        /// <summary>
        /// Batch entry point for placing decals into every <c>Map_</c> scene on its own.
        /// The pipeline normally reaches this through
        /// <see cref="AtmosphereSetup.ApplyEnvironmentToMapScenes"/>.
        /// </summary>
        public static void Batch()
        {
            try
            {
                var seed = DefaultSeed;
                var args = Environment.GetCommandLineArgs();
                for (var i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], "-decalSeed", StringComparison.Ordinal)
                        && int.TryParse(args[i + 1], out var parsed))
                    {
                        seed = parsed;
                    }
                }

                foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!Path.GetFileName(path).StartsWith("Map_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                        path, UnityEditor.SceneManagement.OpenSceneMode.Single);
                    Debug.Log("[Decals] " + path + " — " + Place(seed));
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                }

                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Decals] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Rebuilds every decal in the currently open scene and returns a report of
        /// what it placed and what it could not.
        /// </summary>
        public static string Place(int seed)
        {
            var mapRoot = GameObject.Find("Map");
            if (mapRoot == null)
            {
                return "no 'Map' root in the open scene — nothing to settle.";
            }

            var library = LoadLibrary();
            var random = new System.Random(seed);
            var batches = new Dictionary<string, DecalBatch>(StringComparer.Ordinal);

            Physics.SyncTransforms();

            var report = new List<string>();
            report.Add(PlaceContact(mapRoot, library, random, batches));
            report.Add(PlaceOnFloor(library, random, batches));
            report.Add(PlaceOnWalls(library, random, batches));
            report.Add(PlaceSoot(library, random, batches));

            var stale = mapRoot.transform.Find(RootName);
            if (stale != null)
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(mapRoot.transform, false);

            // The NavMesh in this project is baked from *render meshes*
            // (`MapSceneBuilder.BakeNavMesh`), so without this a floor decal would
            // raise the walkable surface by a centimetre and a wall decal would bake
            // as a floating ledge two metres up. The pipeline places these after the
            // last bake, so this is belt and braces — but a re-bake from the
            // inspector is one click away and would fail silently.
            var ignore = root.AddComponent<NavMeshModifier>();
            ignore.ignoreFromBuild = true;

            var built = 0;
            var quads = 0;
            foreach (var pair in batches.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Value.Quads.Count == 0)
                {
                    continue;
                }

                pair.Value.Build(root.transform);
                built++;
                quads += pair.Value.Quads.Count;
            }

            report.Add("merged into " + built + " mesh(es), " + quads + " quad(s) total, lifted "
                       + (LiftMetres * 100f).ToString("0.0") + " cm off their surfaces.");
            return string.Join("\n  ", report);
        }

        // ====================================================================
        // Placement rules.
        // ====================================================================

        /// <summary>
        /// One dirt mark under every prop that actually stands on a floor.
        /// <para>
        /// The filter is geometric rather than by name: a prop qualifies if its
        /// bounding box sits within <c>0.45 m</c> of a surface directly beneath it and
        /// its footprint is under 5 m across. That excludes the wall panels, ceiling
        /// beams and conduit runs the dressing pass hangs — which have no contact with
        /// a floor and would otherwise get a dirt ring floating in mid-air — without
        /// depending on the dressing kit's naming, which this pass does not own.
        /// </para>
        /// </summary>
        private static string PlaceContact(GameObject mapRoot, Library library, System.Random random,
                                           Dictionary<string, DecalBatch> batches)
        {
            var entry = library.Find("Decal_Contact");
            if (entry == null)
            {
                return "Decal_Contact missing from the manifest.";
            }

            var sources = new List<Transform>();
            foreach (Transform child in mapRoot.transform)
            {
                if (child.name == "Dressing" || child.name == "Shared")
                {
                    sources.Add(child);
                }
                else if (child.name.StartsWith("Zone_", StringComparison.Ordinal))
                {
                    var props = child.Find("Props");
                    if (props != null)
                    {
                        sources.Add(props);
                    }
                }
            }

            var placed = 0;
            var airborne = 0;
            foreach (var source in sources)
            {
                foreach (var renderer in source.GetComponentsInChildren<MeshRenderer>(false))
                {
                    var bounds = renderer.bounds;
                    var footprint = Mathf.Max(bounds.size.x, bounds.size.z);
                    if (footprint < 0.12f || footprint > 5f)
                    {
                        continue;
                    }

                    // Started inside the object's own volume, so its own collider has
                    // to be excluded explicitly: a crate's underside is the first thing
                    // a downward ray from inside it finds, and its normal points *down*,
                    // which reads as "this prop is standing on nothing".
                    var from = new Vector3(bounds.center.x, bounds.min.y + 0.25f, bounds.center.z);
                    if (!GroundBelow(renderer.transform, from, 0.9f, out var hit))
                    {
                        airborne++;
                        continue;
                    }

                    // Half again the object's own footprint: the dirt is what the
                    // brush never reached, so it stops a little outside the thing
                    // that was in the way.
                    var size = Mathf.Clamp(footprint * 1.55f, 0.5f, 3.4f);
                    Add(batches, entry, hit.point, hit.normal,
                        Yaw(random), size, size, Jitter(random, 0.12f));
                    placed++;
                }
            }

            return "contact dirt: " + placed + " prop(s) settled, " + airborne
                   + " skipped as not standing on anything.";
        }

        /// <summary>
        /// Wear, repairs, water and spatter, scattered over the baked walkable surface.
        /// <para>
        /// The NavMesh is the right source and not a convenience. It is, by
        /// construction, exactly the set of places a person can stand — so a wear mark
        /// taken from it is on a route by definition, and none of them land inside a
        /// wall, under a crate or on top of a stair nosing. Sampling is area-weighted
        /// across the triangulation so a 20 × 20 m hall gets its share and a corridor
        /// gets its share, rather than density following triangle count.
        /// </para>
        /// <para>
        /// Which mark goes where is decided by reading the material actually under the
        /// sample, because §12 makes the five floors mean something: a screed repair
        /// belongs on concrete and tile and nowhere else, and standing water on a
        /// timber floor would read as the wrong zone — ART.md §3.8c makes that point
        /// about the tiling materials and it is no less true here.
        /// </para>
        /// </summary>
        private static string PlaceOnFloor(Library library, System.Random random,
                                           Dictionary<string, DecalBatch> batches)
        {
            var triangulation = NavMesh.CalculateTriangulation();
            var indices = triangulation.indices;
            if (indices == null || indices.Length < 3)
            {
                return "no baked NavMesh in this scene, so no floor wear was placed.";
            }

            var vertices = triangulation.vertices;
            var cumulative = new List<float>(indices.Length / 3);
            var total = 0f;
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var a = vertices[indices[i]];
                var b = vertices[indices[i + 1]];
                var c = vertices[indices[i + 2]];
                total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                cumulative.Add(total);
            }

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var wanted = new (string Kind, float PerSquareMetre)[]
            {
                ("Decal_Scuff", SquareMetresPerScuff),
                ("Decal_Patch", SquareMetresPerPatch),
                ("Decal_Puddle", SquareMetresPerPuddle),
                ("Decal_Drip", SquareMetresPerDrip),
            };

            var rejected = 0;
            foreach (var (kind, perSquareMetre) in wanted)
            {
                var entry = library.Find(kind);
                if (entry == null)
                {
                    continue;
                }

                var target = Mathf.RoundToInt(total / perSquareMetre);
                var placed = 0;
                for (var attempt = 0; attempt < target * 4 && placed < target; attempt++)
                {
                    var point = SamplePoint(vertices, indices, cumulative, total, random, out _);
                    if (!Ground(point, out var hit, out var floor))
                    {
                        rejected++;
                        continue;
                    }

                    if ((float)random.NextDouble() >= FloorAffinity(kind, floor))
                    {
                        rejected++;
                        continue;
                    }

                    var scale = 0.75f + (float)random.NextDouble() * 0.6f;
                    var length = entry.size_metres * scale;
                    var width = kind == "Decal_Scuff" ? length * 0.42f : length;

                    Add(batches, entry, hit.point, hit.normal, Yaw(random), length, width,
                        Jitter(random, 0.10f));
                    placed++;
                }

                counts[kind] = placed;
            }

            return "floor: " + total.ToString("0") + " m² walkable → "
                   + string.Join(", ", counts.Select(p => p.Key.Replace("Decal_", "") + " " + p.Value))
                   + "  (" + rejected + " sample(s) rejected for the surface they landed on)";
        }

        /// <summary>
        /// Damp running down a wall, and rust bleeding out of a fixing.
        /// <para>
        /// Found by standing on the walkable surface and looking sideways, which is
        /// the only way to be sure the wall a stain lands on is one a player can see.
        /// A stain on the far face of a partition is invisible and still costs a draw.
        /// </para>
        /// </summary>
        private static string PlaceOnWalls(Library library, System.Random random,
                                           Dictionary<string, DecalBatch> batches)
        {
            var triangulation = NavMesh.CalculateTriangulation();
            var indices = triangulation.indices;
            if (indices == null || indices.Length < 3)
            {
                return "no baked NavMesh, so no wall stains were placed.";
            }

            var vertices = triangulation.vertices;
            var cumulative = new List<float>(indices.Length / 3);
            var total = 0f;
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var a = vertices[indices[i]];
                var b = vertices[indices[i + 1]];
                var c = vertices[indices[i + 2]];
                total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                cumulative.Add(total);
            }

            var placed = new Dictionary<string, int>(StringComparer.Ordinal);
            var baseGrime = library.Find("Decal_Contact");
            var grime = 0;
            var wanted = new (string Kind, float PerSquareMetre, float LowMetres, float HighMetres)[]
            {
                // Damp comes from above: a pipe joint, a slab penetration, the storey
                // over this one. So the stain starts high and runs down.
                ("Decal_WaterStain", SquareMetresPerWallStain, 1.35f, 2.35f),
                // Rust starts at whatever is bolted on, which in this building is
                // bracketry at about chest height.
                ("Decal_Rust", SquareMetresPerRust, 1.05f, 1.95f),
            };

            foreach (var (kind, perSquareMetre, low, high) in wanted)
            {
                var entry = library.Find(kind);
                if (entry == null)
                {
                    continue;
                }

                var target = Mathf.RoundToInt(total / perSquareMetre);
                var made = 0;
                for (var attempt = 0; attempt < target * 8 && made < target; attempt++)
                {
                    var point = SamplePoint(vertices, indices, cumulative, total, random, out _);
                    var height = low + (float)random.NextDouble() * (high - low);
                    var from = point + Vector3.up * height;
                    var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                    if (!Physics.Raycast(from, direction, out var hit, 4.5f, ~0,
                                         QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    if (Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) > 0.25f)
                    {
                        continue;
                    }

                    var scale = 0.8f + (float)random.NextDouble() * 0.5f;
                    Add(batches, entry, hit.point, hit.normal, 0f,
                        entry.size_metres * scale, entry.size_metres * scale,
                        Jitter(random, 0.08f), alignToGravity: true);
                    made++;
                }

                placed[kind] = made;
            }

            // Grime at the foot of a wall, on the floor, drawn out along it. Its own
            // pass with its own density rather than a rider on the stain loop, because
            // it is far and away the most common mark in a real basement and tying its
            // count to how much the walls happen to be weeping made no sense.
            if (baseGrime != null)
            {
                var target = Mathf.RoundToInt(total / SquareMetresPerWallBase);
                for (var attempt = 0; attempt < target * 6 && grime < target; attempt++)
                {
                    var point = SamplePoint(vertices, indices, cumulative, total, random, out _);
                    var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                    if (!Physics.Raycast(point + Vector3.up * 0.35f, direction, out var wall, 3.2f,
                                         ~0, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    if (Mathf.Abs(Vector3.Dot(wall.normal, Vector3.up)) > 0.25f)
                    {
                        continue;
                    }

                    // A quarter of a metre out from the skirting: the dirt collects in
                    // the angle, not against the face.
                    var foot = new Vector3(wall.point.x, point.y, wall.point.z)
                               + wall.normal * 0.26f;
                    if (!Ground(foot, out var floorHit, out _))
                    {
                        continue;
                    }

                    var along = Vector3.Cross(wall.normal, Vector3.up).normalized;
                    AddOriented(batches, baseGrime, floorHit.point, floorHit.normal, along,
                                1.6f + (float)random.NextDouble() * 1.4f, 0.80f);
                    grime++;
                }
            }

            return "walls: " + string.Join(", ",
                       placed.Select(p => p.Key.Replace("Decal_", "") + " " + p.Value))
                   + ", wall-base grime " + grime;
        }

        /// <summary>
        /// A burn halo on the soffit over every bare bulb.
        /// <para>
        /// §03 makes every fitting switchable and therefore worth looking at, and
        /// ART.md §3.6 already tints them per zone. This is the mark that says the
        /// fitting has been *burning for years* rather than being switched on for the
        /// render — and it lands on the ceiling directly above a light, which is
        /// otherwise the flattest and best-lit surface in any room in the building.
        /// </para>
        /// </summary>
        private static string PlaceSoot(Library library, System.Random random,
                                        Dictionary<string, DecalBatch> batches)
        {
            var entry = library.Find("Decal_Soot");
            if (entry == null)
            {
                return "Decal_Soot missing from the manifest.";
            }

            var placed = 0;
            var missed = 0;
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (light.type != LightType.Point)
                {
                    continue;
                }

                if (!Physics.Raycast(light.transform.position + Vector3.up * 0.05f, Vector3.up,
                                     out var hit, 2.5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    missed++;
                    continue;
                }

                // Soot spreads with distance from the filament, so a bulb hung close
                // under a soffit leaves a tight dark mark and one hung low leaves a
                // wide faint one.
                var spread = Mathf.Clamp(0.55f + hit.distance * 0.9f, 0.6f, 2.2f);
                Add(batches, entry, hit.point, hit.normal, Yaw(random), spread, spread,
                    Jitter(random, 0.05f));
                placed++;
            }

            return "soot: " + placed + " fitting(s) marked their soffit, " + missed
                   + " had no ceiling within 2.5 m.";
        }

        // ====================================================================
        // Geometry.
        // ====================================================================

        private static Vector3 SamplePoint(Vector3[] vertices, int[] indices, List<float> cumulative,
                                           float total, System.Random random, out Vector3 normal)
        {
            var pick = (float)random.NextDouble() * total;
            var lo = 0;
            var hi = cumulative.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (cumulative[mid] < pick)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            var a = vertices[indices[lo * 3]];
            var b = vertices[indices[lo * 3 + 1]];
            var c = vertices[indices[lo * 3 + 2]];

            // Square-root warp on the first barycentric weight: without it the sample
            // clusters toward one corner of every triangle, which on a NavMesh made of
            // long thin corridor slivers puts every mark against a wall.
            var u = Mathf.Sqrt((float)random.NextDouble());
            var v = (float)random.NextDouble();
            normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.y < 0f)
            {
                normal = -normal;
            }

            return a + u * ((1f - v) * (b - a) + v * (c - a));
        }

        /// <summary>
        /// Finds the real surface under a NavMesh sample and reads what it is made of.
        /// <para>
        /// The NavMesh sits a little above the geometry it was baked from, and a decal
        /// registered to the NavMesh rather than to the floor floats. So the point is
        /// re-found by raycast, and the same hit answers the second question for free:
        /// which of §12's five materials this is, read off the renderer rather than
        /// guessed from the zone the sample fell in — a corridor crossing a zone
        /// boundary carries the corridor's floor, not the zone's.
        /// </para>
        /// </summary>
        private static bool Ground(Vector3 point, out RaycastHit hit, out string floor)
        {
            floor = string.Empty;
            if (!Physics.Raycast(point + Vector3.up * 0.6f, Vector3.down, out hit, 1.6f, ~0,
                                 QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (Vector3.Dot(hit.normal, Vector3.up) < 0.8f)
            {
                return false;
            }

            var renderer = hit.collider != null ? hit.collider.GetComponent<MeshRenderer>() : null;
            var material = renderer != null ? renderer.sharedMaterial : null;
            floor = material != null ? material.name : string.Empty;
            return true;
        }

        /// <summary>
        /// First upward-facing surface below <paramref name="from"/> that does not
        /// belong to <paramref name="self"/>.
        /// </summary>
        private static bool GroundBelow(Transform self, Vector3 from, float distance, out RaycastHit hit)
        {
            hit = default;
            var hits = Physics.RaycastAll(from, Vector3.down, distance, ~0,
                                          QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var candidate in hits)
            {
                if (candidate.collider == null || candidate.collider.transform.IsChildOf(self)
                    || self.IsChildOf(candidate.collider.transform))
                {
                    continue;
                }

                if (Vector3.Dot(candidate.normal, Vector3.up) < 0.7f)
                {
                    continue;
                }

                hit = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// How readily a mark lands on a given §12 floor, 0–1, used as an acceptance
        /// probability.
        /// <para>
        /// This is a zone-identity lever as much as a plausibility one, and it is the
        /// cheapest one available: it costs nothing, it needs no new art, and it means
        /// the flooded 저수조 accumulates water, the 하역장 slab accumulates repairs and
        /// the 저탄장's ballast accumulates neither. §12 asks a player to tell the five
        /// zones apart, and a room's *history* separates places at least as strongly as
        /// its paint does.
        /// </para>
        /// <para>
        /// A probability rather than a yes/no because most of these are matters of
        /// degree — water does collect on a concrete slab, just far less readily than
        /// in the voids between ballast — and because a hard rule produces a floor
        /// where every mark of a kind stops dead at the zone boundary.
        /// </para>
        /// </summary>
        private static float FloorAffinity(string kind, string floor)
        {
            if (string.IsNullOrEmpty(floor))
            {
                // Unknown surface — a corridor tile whose renderer this pass could not
                // read. Only the marks that are true of any floor at all.
                return kind == "Decal_Scuff" || kind == "Decal_Drip" ? 0.6f : 0f;
            }

            switch (kind)
            {
                case "Decal_Patch":
                    // A screed repair is cut into a slab. There is nothing to cut in
                    // loose ballast and nothing to make good on a timber floor.
                    if (floor.Contains("Concrete")) return 1.0f;
                    if (floor.Contains("Tile")) return 0.45f;
                    return 0f;

                case "Decal_Puddle":
                    // §12 puts the flooded end on 자갈 and the tanks on 타일; ART.md
                    // §3.8c already argues both from where the water physically goes.
                    // Timber wicks damp out of its joints and holds none, and a lake on
                    // it would read as the wrong zone entirely.
                    if (floor.Contains("Gravel")) return 1.0f;
                    if (floor.Contains("Tile")) return 0.85f;
                    if (floor.Contains("Concrete")) return 0.45f;
                    if (floor.Contains("Metal")) return 0.30f;
                    return 0f;

                case "Decal_Scuff":
                    // Wear is polish, and polish needs something that takes a polish.
                    if (floor.Contains("Gravel")) return 0.12f;
                    if (floor.Contains("Metal")) return 0.75f;
                    return 1.0f;

                default:
                    return 1.0f;
            }
        }

        private static float Yaw(System.Random random) => (float)random.NextDouble() * 360f;

        private static float Jitter(System.Random random, float amount) =>
            1f + ((float)random.NextDouble() * 2f - 1f) * amount;

        /// <summary>
        /// Appends one quad, in world space, to the batch for its kind and storey.
        /// <para>
        /// Grouped by storey as well as by kind because the alternative extremes are
        /// both wrong: one mesh per decal is six hundred renderers to cull, and one
        /// mesh for the whole building is a five-storey bounding box that is never
        /// off-screen. A storey is the unit the camera is actually inside one of.
        /// </para>
        /// </summary>
        private static void Add(Dictionary<string, DecalBatch> batches, DecalEntry entry,
                                Vector3 point, Vector3 normal, float yawDegrees,
                                float length, float width, float sizeJitter,
                                bool alignToGravity = false)
        {
            var storey = Mathf.RoundToInt(point.y / 3.75f);
            var key = entry.name + "_L" + storey;
            if (!batches.TryGetValue(key, out var batch))
            {
                batch = new DecalBatch(entry, storey);
                batches[key] = batch;
            }

            // A wall stain has to run downhill, so its V axis is gravity projected onto
            // the wall rather than an arbitrary yaw. A floor mark has no such
            // constraint and gets a random one, which is most of what stops the set
            // from reading as stamped.
            Vector3 along;
            if (alignToGravity)
            {
                along = Vector3.ProjectOnPlane(Vector3.down, normal).normalized;
                if (along.sqrMagnitude < 1e-4f)
                {
                    along = Vector3.Cross(normal, Vector3.right).normalized;
                }
            }
            else
            {
                var seedAxis = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
                along = Vector3.Cross(normal, Vector3.Cross(seedAxis, normal)).normalized;
                along = Quaternion.AngleAxis(yawDegrees, normal) * along;
            }

            var across = Vector3.Cross(normal, along).normalized;
            var centre = point + normal * LiftMetres;

            // Per-instance variation lives in size and rotation, not in opacity.
            // URP's stock Lit shader reads no vertex colour, so the alternative would
            // be a second material per strength band and a second batch to draw it —
            // more draw calls than the variation is worth. Strength per *kind* is set
            // on the material's _BaseColor alpha in MaterialFor.
            var halfAlong = along * (length * sizeJitter * 0.5f);
            var halfAcross = across * (width * sizeJitter * 0.5f);

            batch.Quads.Add(new Quad
            {
                Centre = centre,
                Normal = normal,
                Along = halfAlong,
                Across = halfAcross,
            });
        }

        /// <summary>
        /// Places a quad whose long axis is dictated by geometry rather than chosen at
        /// random — a grime line has to run *along* the wall it collected against.
        /// </summary>
        private static void AddOriented(Dictionary<string, DecalBatch> batches, DecalEntry entry,
                                        Vector3 point, Vector3 normal, Vector3 along,
                                        float length, float width)
        {
            var storey = Mathf.RoundToInt(point.y / 3.75f);
            var key = entry.name + "_L" + storey;
            if (!batches.TryGetValue(key, out var batch))
            {
                batch = new DecalBatch(entry, storey);
                batches[key] = batch;
            }

            var flattened = Vector3.ProjectOnPlane(along, normal).normalized;
            if (flattened.sqrMagnitude < 1e-4f)
            {
                return;
            }

            batch.Quads.Add(new Quad
            {
                Centre = point + normal * LiftMetres,
                Normal = normal,
                Along = flattened * (length * 0.5f),
                Across = Vector3.Cross(normal, flattened).normalized * (width * 0.5f),
            });
        }

        private struct Quad
        {
            public Vector3 Centre;
            public Vector3 Normal;
            public Vector3 Along;
            public Vector3 Across;
        }

        private sealed class DecalBatch
        {
            public readonly List<Quad> Quads = new List<Quad>();
            private readonly DecalEntry _entry;
            private readonly int _storey;

            public DecalBatch(DecalEntry entry, int storey)
            {
                _entry = entry;
                _storey = storey;
            }

            public void Build(Transform parent)
            {
                var go = new GameObject(_entry.name + "_L" + _storey);
                go.transform.SetParent(parent, false);

                var vertices = new Vector3[Quads.Count * 4];
                var normals = new Vector3[Quads.Count * 4];
                var tangents = new Vector4[Quads.Count * 4];
                var uv = new Vector2[Quads.Count * 4];
                var triangles = new int[Quads.Count * 6];

                for (var i = 0; i < Quads.Count; i++)
                {
                    var q = Quads[i];
                    var v = i * 4;
                    vertices[v + 0] = q.Centre - q.Along - q.Across;
                    vertices[v + 1] = q.Centre - q.Along + q.Across;
                    vertices[v + 2] = q.Centre + q.Along + q.Across;
                    vertices[v + 3] = q.Centre + q.Along - q.Across;

                    // V runs along `Along`, which is what makes a wall stain's streaks
                    // point the way gravity does — see Add.
                    uv[v + 0] = new Vector2(0f, 0f);
                    uv[v + 1] = new Vector2(1f, 0f);
                    uv[v + 2] = new Vector2(1f, 1f);
                    uv[v + 3] = new Vector2(0f, 1f);

                    var tangent = new Vector4(q.Across.normalized.x, q.Across.normalized.y,
                                              q.Across.normalized.z, -1f);
                    for (var k = 0; k < 4; k++)
                    {
                        normals[v + k] = q.Normal;
                        tangents[v + k] = tangent;
                    }

                    var t = i * 6;
                    triangles[t + 0] = v + 0;
                    triangles[t + 1] = v + 1;
                    triangles[t + 2] = v + 2;
                    triangles[t + 3] = v + 0;
                    triangles[t + 4] = v + 2;
                    triangles[t + 5] = v + 3;
                }

                var mesh = new Mesh { name = go.name };
                mesh.indexFormat = vertices.Length > 65000
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16;
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.tangents = tangents;
                mesh.uv = uv;
                mesh.triangles = triangles;
                mesh.RecalculateBounds();

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = MaterialFor(_entry);

                // A decal has no volume, so it cannot cast a shadow of its own — and a
                // 1.2 cm-high quad that did would drop a hard black rectangle on the
                // floor under every fitting in the building.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.staticShadowCaster = false;
            }
        }

        // ====================================================================
        // Materials.
        // ====================================================================

        /// <summary>
        /// Builds (once) the transparent URP Lit material a decal kind renders with.
        /// <para>
        /// Lit rather than Unlit, deliberately and at a cost. §03 makes the flashlight
        /// the only reliable source of information in the building, so a mark that
        /// does not brighten when the beam finds it is not a mark the player can
        /// discover — it is a texture that was always there. That is the difference
        /// between a puddle that is a clue and a puddle that is decoration.
        /// </para>
        /// <para>
        /// The three properties that are not obvious: <c>_ZWrite = 0</c> because a
        /// transparent surface must not occlude what is behind it;
        /// <c>_SURFACE_TYPE_TRANSPARENT</c> because URP compiles the blend out
        /// otherwise and the decal renders as an opaque grey card; and the queue at
        /// <c>Geometry + 450</c>, in front of the opaque floor and behind the fog and
        /// the real transparencies.
        /// </para>
        /// </summary>
        private static Material MaterialFor(DecalEntry entry)
        {
            var path = MaterialRoot + "/" + entry.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("URP's Lit shader is missing.");
            }

            var material = existing;
            if (material == null)
            {
                if (!AssetDatabase.IsValidFolder(MaterialRoot))
                {
                    AssetDatabase.CreateFolder("Assets/Textures", "Materials");
                }

                material = new Material(shader) { name = entry.name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", Load(entry.maps.albedo));
            material.SetTexture("_BumpMap", Load(entry.maps.normal));
            material.SetTexture("_MetallicGlossMap", Load(entry.maps.metallic_smoothness));
            var strength = Strength.TryGetValue(entry.name, out var value) ? value : 1f;
            material.SetColor("_BaseColor", new Color(1f, 1f, 1f, strength));
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_Metallic", entry.metallic);
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.SetFloat("_SpecularHighlights", 1f);

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Back);

            // URP 12 added `_BlendModePreserveSpecular`, it defaults to **on**, and it
            // silently changes what "alpha blend" means: the blend factors become
            // One / OneMinusSrcAlpha and the shader is expected to premultiply the
            // colour by alpha itself, under `_ALPHAPREMULTIPLY_ON`.
            //
            // Setting SrcAlpha / OneMinusSrcAlpha here and turning that keyword off —
            // the obvious thing to do — produced neither: URP's material post-processor
            // re-derived the factors from `_Surface` on import and put One back, while
            // the keyword stayed off. The result is `dst*(1-a) + src*1`, which adds the
            // decal's full colour everywhere its alpha is zero. On screen that is a
            // bright rectangle exactly the size of the quad, on every wall in the
            // building, and it looks like a broken texture rather than a broken blend.
            //
            // So the flag is turned off explicitly and the factors are written to match
            // it. Both, because either one alone loses to the other depending on
            // whether the post-processor happens to run.
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)RenderQueue.Transparent - 100;

            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.enableInstancing = true;

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture? Load(string relative)
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
                    "Decal map missing: " + path + ". Run tools/textures/gen_textures.py.");
            }

            // A decal is placed once and clamped, never tiled: with Repeat, the soft
            // edge of the mark wraps round and paints a second copy of the opposite
            // edge along the boundary of the quad. Set here rather than in the shared
            // importer because that file belongs to another area and applies to the
            // tiling set, where Repeat is exactly right.
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && (importer.wrapMode != TextureWrapMode.Clamp
                                     || !importer.alphaIsTransparency))
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
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

            var library = JsonUtility.FromJson<Library>(File.ReadAllText(ManifestPath));
            if (library?.decals == null || library.decals.Length == 0)
            {
                throw new InvalidOperationException("The decal manifest is empty.");
            }

            return library;
        }

#pragma warning disable CS8618
        [Serializable]
        private sealed class Library
        {
            public DecalEntry[] decals;

            public DecalEntry? Find(string name) =>
                decals.FirstOrDefault(d => string.Equals(d.name, name, StringComparison.Ordinal));
        }

        [Serializable]
        private sealed class DecalEntry
        {
            public string name;
            public float size_metres;
            public float metallic;
            public DecalMaps maps;
        }

        [Serializable]
        private sealed class DecalMaps
        {
            public string albedo;
            public string normal;
            public string metallic_smoothness;
        }
#pragma warning restore CS8618
    }
}
