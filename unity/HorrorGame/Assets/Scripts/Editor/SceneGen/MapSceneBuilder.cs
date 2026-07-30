using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// Turns a <see cref="MapSketchResult"/> into scene objects.
    /// <para>
    /// Nothing in here decides anything about the map. Every position, rotation and
    /// piece choice was settled by <see cref="MapSketch"/> against the graph that
    /// <see cref="MapValidator"/> already judged; this class only puts FBXs where the
    /// data says. That split is what makes the §12 checklist meaningful: if the
    /// builder could nudge a corridor to make it fit, the validated graph would stop
    /// describing the scene players walk in.
    /// </para>
    /// <para>
    /// Pieces are placed by matching their <em>world bounds</em> to the target cell
    /// rather than by computing where a rotation moves the FBX origin. The kit's
    /// origin convention (min-X / min-Y corner, floor at z = 0) is documented, but an
    /// import setting or a re-export can move it, and a scene assembled from a stale
    /// assumption looks almost right — which is far worse than looking wrong.
    /// </para>
    /// </summary>
    public static class MapSceneBuilder
    {
        /// <summary>Root object every generated map hangs from. The runtime finds the map by this name.</summary>
        public const string MapRootName = "Map";

        /// <summary>Child of the root holding spawn, site and loot markers.</summary>
        public const string MarkerRootName = "Markers";

        /// <summary>Prefix of a zone's root object. The suffix is the §12 surface, which is the Listener's answer.</summary>
        public const string ZonePrefix = "Zone_";

        /// <summary>Group under a zone whose lights the Engineer's 구역 조명 switches (§04).</summary>
        public const string ZoneLightGroupName = "ZoneLights";

        /// <summary>Y offset of a zone's floor slab below the corridor floor, metres. Just enough to stop z-fighting.</summary>
        private const float FloorSlabDepth = 0.01f;

        /// <summary>
        /// Builds the whole scene into the currently open scene.
        /// </summary>
        /// <param name="map">The validated map.</param>
        /// <param name="sceneName">Names the baked NavMesh asset. Passed in because the scene is not saved yet.</param>
        /// <returns>The map root object.</returns>
        public static GameObject Build(MapSketchResult map, string sceneName)
        {
            KitOrientation.Forget();
            var surfaces = SurfaceAssets.Create();

            var root = new GameObject(MapRootName);
            var zoneRoots = new GameObject[map.ZoneRects.Length];
            var zoneTileRoots = new GameObject[map.ZoneRects.Length];
            var zoneLightRoots = new GameObject[map.ZoneRects.Length];

            for (var i = 0; i < map.ZoneRects.Length; i++)
            {
                var rect = map.ZoneRects[i];
                zoneRoots[i] = Child(root, ZonePrefix + ZoneSlug(rect) + "_" + rect.Floor);
                zoneTileRoots[i] = Child(zoneRoots[i], "Tiles");
                zoneLightRoots[i] = Child(zoneRoots[i], ZoneLightGroupName);
                BuildFloorSlab(Child(zoneRoots[i], "Floor"), rect, surfaces);
            }

            var sharedRoot = Child(root, "Shared");

            foreach (var tile in map.Tiles)
            {
                var parent = tile.ZoneId >= 0 ? zoneTileRoots[tile.ZoneId] : sharedRoot;
                var go = Place(tile.Piece, parent, tile.YawDegrees);
                if (go == null)
                {
                    continue;
                }

                go.name = tile.Piece + "_" + tile.Origin.X + "_" + tile.Origin.Z;
                AlignMinCorner(go, tile.Origin.Min.X, 0f, tile.Origin.Min.Z);
                Finish(go, SurfaceOf(tile.Piece, tile.ZoneId, map), surfaces);
            }

            foreach (var prop in map.Props)
            {
                var parent = prop.ZoneId >= 0 ? Child(zoneRoots[prop.ZoneId], "Props") : sharedRoot;
                var go = Place(prop.Piece, parent, prop.YawDegrees);
                if (go == null)
                {
                    continue;
                }

                go.name = prop.Name;
                go.transform.position = ToUnity(prop.Position);
                Finish(go, SurfaceOf(prop.Piece, prop.ZoneId, map), surfaces);
            }

            BuildMarkers(root, map, zoneLightRoots);
            BuildAmbience();
            BakeNavMesh(root, sceneName);

            return root;
        }

        // ====================================================================
        // Markers — everything the runtime looks up by name.
        // ====================================================================

        private static void BuildMarkers(GameObject root, MapSketchResult map, GameObject[] zoneLightRoots)
        {
            var markerRoot = Child(root, MarkerRootName);
            var groups = new Dictionary<MapMarkerKind, GameObject>();

            foreach (var marker in map.Markers)
            {
                if (marker.Kind == MapMarkerKind.ZoneLight || marker.Kind == MapMarkerKind.EntranceLight)
                {
                    BuildLight(marker, map, zoneLightRoots);
                    continue;
                }

                if (!groups.TryGetValue(marker.Kind, out var group))
                {
                    group = Child(markerRoot, marker.Kind.ToString() + "s");
                    groups[marker.Kind] = group;
                }

                var go = Child(group, marker.Name);
                go.transform.position = ToUnity(marker.Position);

                // §13: the objective's location and a clue's contents exist only on the
                // host. Every candidate site is therefore an identical empty transform —
                // no component, no flag, nothing a client could read to learn which one
                // the host picked. A marker that carried "this is the real one" would
                // defeat §03's whole constraint before the match started.
                if (marker.Kind == MapMarkerKind.CandidateSite)
                {
                    go.transform.rotation = Quaternion.identity;
                }
            }
        }

        private static void BuildLight(MapMarkerPlacement marker, MapSketchResult map, GameObject[] zoneLightRoots)
        {
            var parent = marker.ZoneId >= 0 && marker.ZoneId < zoneLightRoots.Length
                ? zoneLightRoots[marker.ZoneId]
                : null;
            var go = new GameObject(marker.Name);
            if (parent != null)
            {
                go.transform.SetParent(parent.transform, false);
            }

            // Eye height is the wrong place for a ceiling fitting; the kit's corridors
            // are 3 m clear, so the fixture hangs just under that.
            go.transform.position = ToUnity(marker.Position) + (Vector3.up * (MapKitCatalogue.CorridorClearWidth + 0.6f));

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = GameConstants.ZoneLightRadius;
            light.shadows = LightShadows.None;

            if (marker.Kind == MapMarkerKind.EntranceLight)
            {
                // The way out stays lit. §07 새벽 turns the door into an ambush and §03
                // makes the building dark; a player who cannot find the exit at all is
                // not facing a dilemma, just a missing affordance.
                light.intensity = 1.2f;
                light.color = new Color(1.0f, 0.94f, 0.82f);
                light.enabled = true;
            }
            else
            {
                // §03: "어둠 = 목표의 잠금장치." Zone lights exist so the Engineer has
                // something to switch on (§04 구역 조명, 전기 패널 구역당 1개); they start
                // off, because a lit building would hand the objective over for free.
                light.intensity = 0.9f;
                light.color = new Color(0.85f, 0.88f, 1.0f);
                light.enabled = false;
            }
        }

        private static void BuildAmbience()
        {
            // Near-black ambient rather than black: §03 locks the objective behind
            // darkness, but a scene with literally no ambient term makes an unlit
            // corridor unreadable even with a flashlight on it, which reads as a broken
            // build rather than as dark.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.038f, 0.05f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.02f;
            RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.03f);
        }

        // ====================================================================
        // Floors — §12 청음사. "구역별로 바닥 재질이 달라야 청음사가 위치를 판별할 수 있다."
        // ====================================================================

        private static void BuildFloorSlab(GameObject parent, MapZoneRect rect, SurfaceAssets surfaces)
        {
            var piece = MapKitCatalogue.FloorTileFor(rect.Floor);
            var step = Mathf.RoundToInt(MapKitCatalogue.FloorTileMetres / MapKitCatalogue.GridMetres);

            for (var x = rect.CellX; x < rect.CellX + rect.CellsX; x += step)
            {
                for (var z = rect.CellZ; z < rect.CellZ + rect.CellsZ; z += step)
                {
                    var go = Place(piece, parent, 0f);
                    if (go == null)
                    {
                        return;
                    }

                    go.name = piece + "_" + x + "_" + z;
                    var cell = new MapCell(x, z);
                    AlignFloorTop(go, cell.Min.X, cell.Min.Z, -FloorSlabDepth);
                    Finish(go, rect.Floor, surfaces);
                }
            }
        }

        // ====================================================================
        // Placement.
        // ====================================================================

        private static GameObject Place(MapKitPiece piece, GameObject parent, float yawDegrees)
        {
            var assetPath = MapKitCatalogue.AssetPath(piece);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                Debug.LogError("[SceneGen] MapKit piece missing: " + assetPath
                    + ". The generator names pieces from MapKitCatalogue, so a rename in Assets/Models/MapKit "
                    + "has to be mirrored there rather than worked around here.");
                return null;
            }

            var go = PrefabUtility.InstantiatePrefab(asset, parent.transform) as GameObject;
            if (go == null)
            {
                return null;
            }

            go.transform.rotation = KitOrientation.Current.Rotation(piece, yawDegrees);
            return go;
        }

        /// <summary>
        /// Which way up the kit imported, and the rotation that fixes it.
        /// <para>
        /// The FBXs are authored in Blender, whose Z is up and whose Y is the
        /// footprint's depth. Unity's importer normally bakes that conversion into the
        /// mesh; when it does not, every piece arrives lying on its back — a corridor
        /// measures 2.5 × 10 × 3.3 with the 3.3 in Z instead of in Y. The generator
        /// probes one known piece rather than assuming either state, because both are
        /// possible and each needs a different dock table: turning a piece upright with
        /// a rotation about X flips its Y axis, which mirrors an L-corner, so the yaw
        /// that produces a south-and-east bend is not the same in the two cases.
        /// </para>
        /// <para>
        /// A wrong guess here is the worst kind of bug this generator can have: the
        /// graph still validates, the scene still opens, and the building is inside out.
        /// </para>
        /// </summary>
        private sealed class KitOrientation
        {
            private static KitOrientation _current;

            private KitOrientation(bool blenderZUp)
            {
                BlenderZUp = blenderZUp;
            }

            /// <summary>True when the kit arrived with height along Z and needs standing up.</summary>
            public bool BlenderZUp { get; }

            /// <summary>The probe result for this editor session.</summary>
            public static KitOrientation Current => _current ?? (_current = Probe());

            /// <summary>Discards the cached probe, so a re-import is noticed.</summary>
            public static void Forget() => _current = null;

            /// <summary>World rotation for a piece the sketch asked to yaw by <paramref name="yawDegrees"/>.</summary>
            public Quaternion Rotation(MapKitPiece piece, float yawDegrees)
            {
                if (!BlenderZUp)
                {
                    return Quaternion.Euler(0f, yawDegrees, 0f);
                }

                return Quaternion.Euler(0f, yawDegrees + DockOffset(piece), 0f) * Quaternion.Euler(-90f, 0f, 0f);
            }

            /// <summary>
            /// Extra yaw a piece needs once standing it up has mirrored its Y axis.
            /// <para>
            /// Pieces whose dock set survives the mirror need nothing: a straight docks
            /// on −Y and +Y, a T on −Y, +Y and +X, a cross on all four, and each of those
            /// sets maps to itself. An L docks on −Y and +X, which mirrors to +Y and +X —
            /// the same shape turned a quarter. A cap docks only on −Y, which mirrors to
            /// +Y, so its mouth points the opposite way.
            /// </para>
            /// </summary>
            private static float DockOffset(MapKitPiece piece)
            {
                switch (piece)
                {
                    case MapKitPiece.CorridorCornerL:
                        return 90f;

                    case MapKitPiece.DeadEndCap:
                    case MapKitPiece.ObservationPostBarredWindow:
                    case MapKitPiece.StairwellMetal:
                        return 180f;

                    default:
                        return 0f;
                }
            }

            private static KitOrientation Probe()
            {
                // Corridor_Straight_10m is the yardstick: 2.5 m wide, 10 m long, 3.3 m
                // tall, so whichever axis measures 10 m is the footprint's depth and
                // whichever measures 3.3 m is up.
                var path = MapKitCatalogue.AssetPath(MapKitPiece.CorridorStraight10m);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    Debug.LogError("[SceneGen] Cannot probe kit orientation: " + path + " is missing.");
                    return new KitOrientation(false);
                }

                var instance = UnityEngine.Object.Instantiate(asset);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var renderers = instance.GetComponentsInChildren<Renderer>();
                var zUp = false;
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    for (var i = 1; i < renderers.Length; i++)
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }

                    zUp = bounds.size.y > bounds.size.z;
                }

                UnityEngine.Object.DestroyImmediate(instance);

                Debug.Log("[SceneGen] MapKit orientation: " + (zUp
                    ? "Blender Z-up (pieces stood upright by the generator, dock tables mirrored). "
                      + "The import is what should be fixed — see AssetImportModelPostprocessor."
                    : "Unity Y-up, no correction applied."));

                return new KitOrientation(zUp);
            }
        }

        private static void AlignMinCorner(GameObject go, float minX, float minY, float minZ)
        {
            if (!TryBounds(go, out var bounds))
            {
                go.transform.position = new Vector3(minX, minY, minZ);
                return;
            }

            go.transform.position += new Vector3(minX - bounds.min.x, minY - bounds.min.y, minZ - bounds.min.z);
        }

        private static void AlignFloorTop(GameObject go, float minX, float minZ, float topY)
        {
            if (!TryBounds(go, out var bounds))
            {
                go.transform.position = new Vector3(minX, topY, minZ);
                return;
            }

            go.transform.position += new Vector3(minX - bounds.min.x, topY - bounds.max.y, minZ - bounds.min.z);
        }

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        /// <summary>
        /// Gives a placed piece its surface: the zone's floor material on any slot the
        /// kit named for a floor, a collider so the Listener can raycast onto it, and
        /// the matching physics material so that raycast answers §12's 발소리 table
        /// without a lookup table anywhere in the runtime.
        /// </summary>
        private static void Finish(GameObject go, FloorMaterial floor, SurfaceAssets surfaces)
        {
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            foreach (var renderer in renderers)
            {
                if (floor != FloorMaterial.None)
                {
                    var shared = renderer.sharedMaterials;
                    var changed = false;
                    for (var i = 0; i < shared.Length; i++)
                    {
                        if (shared[i] != null && shared[i].name.StartsWith("Floor", System.StringComparison.Ordinal))
                        {
                            shared[i] = surfaces.MaterialFor(floor);
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        renderer.sharedMaterials = shared;
                    }
                }

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                var collider = renderer.GetComponent<Collider>();
                if (collider == null)
                {
                    var mesh = renderer.gameObject.AddComponent<MeshCollider>();
                    mesh.sharedMesh = filter.sharedMesh;
                    collider = mesh;
                }

                if (floor != FloorMaterial.None)
                {
                    collider.sharedMaterial = surfaces.PhysicsFor(floor);
                }
            }
        }

        // ====================================================================
        // NavMesh.
        // ====================================================================

        private static void BakeNavMesh(GameObject root, string sceneName)
        {
            var go = Child(root, "NavMesh");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;

            // Render meshes rather than colliders: the kit's walls and floors are all
            // renderers, and colliders are added by this generator — collecting the
            // physics geometry would bake whatever ran first.
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();

            if (surface.navMeshData == null)
            {
                Debug.LogWarning("[SceneGen] NavMesh bake produced no data. The monster (§06) needs one to move; "
                    + "check that the map pieces have renderers.");
                return;
            }

            SceneGenPaths.EnsureFolder(SceneGenPaths.NavMeshRoot);
            var path = SceneGenPaths.NavMeshRoot + "/NavMesh_" + sceneName + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
        }

        // ====================================================================
        // Helpers.
        // ====================================================================

        private static GameObject Child(GameObject parent, string name)
        {
            var existing = parent.transform.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static Vector3 ToUnity(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>
        /// What a piece sounds like underfoot.
        /// <para>
        /// Normally the zone's surface, because §12 counts 바닥 재질 per zone and that
        /// is what lets the Listener name one. The stair is the documented exception:
        /// §12's own table gives 계단 its own row — 금속, 울림 — separately from A~D, and
        /// <see cref="MapZone.ClarityOf"/> is exposed precisely so a stairwell landing
        /// can answer without inventing a zone for itself. Letting the stair take zone
        /// D's 콘크리트 would delete the loudest surface in the building, which is the
        /// one that makes "지금 계단이야" a usable call.
        /// </para>
        /// </summary>
        private static FloorMaterial SurfaceOf(MapKitPiece piece, int zoneId, MapSketchResult map)
        {
            if (piece == MapKitPiece.StairwellMetal || piece == MapKitPiece.FloorTileMetal)
            {
                return FloorMaterial.Metal;
            }

            return zoneId >= 0 && zoneId < map.ZoneRects.Length ? map.ZoneRects[zoneId].Floor : FloorMaterial.None;
        }

        private static string ZoneSlug(MapZoneRect rect)
        {
            // The §12 label is "A 나무"; the scene wants a name a path can hold, and the
            // letter is the part every other document refers to.
            var name = rect.Name;
            var space = name.IndexOf(' ');
            return space > 0 ? name.Substring(0, space) : name;
        }

        /// <summary>
        /// The per-surface material and physics material a generated scene references.
        /// <para>
        /// The physics material is the load-bearing one. §04's Listener has to know
        /// what is underfoot, and the cheapest honest answer in an engine is "raycast
        /// down and read the collider's material name" — no registry, no component on
        /// every tile, nothing to keep in sync. The names match
        /// <see cref="FloorMaterial"/> exactly so the mapping back is an
        /// <c>Enum.Parse</c>.
        /// </para>
        /// </summary>
        private sealed class SurfaceAssets
        {
            private readonly Dictionary<FloorMaterial, Material> _materials = new Dictionary<FloorMaterial, Material>();
            private readonly Dictionary<FloorMaterial, PhysicsMaterial> _physics =
                new Dictionary<FloorMaterial, PhysicsMaterial>();

            public static SurfaceAssets Create()
            {
                SceneGenPaths.EnsureFolder(SceneGenPaths.MaterialRoot);
                var assets = new SurfaceAssets();
                foreach (FloorMaterial floor in System.Enum.GetValues(typeof(FloorMaterial)))
                {
                    if (floor == FloorMaterial.None)
                    {
                        continue;
                    }

                    assets._materials[floor] = LoadOrCreateMaterial(floor);
                    assets._physics[floor] = LoadOrCreatePhysics(floor);
                }

                return assets;
            }

            public Material MaterialFor(FloorMaterial floor) =>
                _materials.TryGetValue(floor, out var material) ? material : null;

            public PhysicsMaterial PhysicsFor(FloorMaterial floor) =>
                _physics.TryGetValue(floor, out var material) ? material : null;

            private static Material LoadOrCreateMaterial(FloorMaterial floor)
            {
                var path = SceneGenPaths.MaterialRoot + "/Floor_" + floor + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null)
                {
                    return existing;
                }

                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var material = new Material(shader) { name = "Floor_" + floor };
                material.color = ColourOf(floor);
                if (material.HasProperty("_Smoothness"))
                {
                    material.SetFloat("_Smoothness", SmoothnessOf(floor));
                }

                AssetDatabase.CreateAsset(material, path);
                return material;
            }

            private static PhysicsMaterial LoadOrCreatePhysics(FloorMaterial floor)
            {
                // ".asset" rather than ".physicsMaterial": Unity 6 warns that
                // CreateAsset must not mint a typed physics-material file and says the
                // warning becomes an exception in a later release.
                var path = SceneGenPaths.MaterialRoot + "/Surface_" + floor + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
                if (existing != null)
                {
                    return existing;
                }

                var material = new PhysicsMaterial(floor.ToString());
                AssetDatabase.CreateAsset(material, path);
                return material;
            }

            /// <summary>
            /// A placeholder colour per surface. Not an art decision — it is the cheapest
            /// way to see, in the editor, that the zone boundaries §12 demands ("재질
            /// 경계를 명확히 할 것") actually landed where the graph says they do.
            /// </summary>
            private static Color ColourOf(FloorMaterial floor)
            {
                switch (floor)
                {
                    case FloorMaterial.Wood: return new Color(0.42f, 0.29f, 0.17f);
                    case FloorMaterial.Tile: return new Color(0.72f, 0.73f, 0.70f);
                    case FloorMaterial.Gravel: return new Color(0.38f, 0.36f, 0.32f);
                    case FloorMaterial.Concrete: return new Color(0.48f, 0.48f, 0.48f);
                    case FloorMaterial.Metal: return new Color(0.55f, 0.57f, 0.60f);
                    default: return Color.magenta;
                }
            }

            private static float SmoothnessOf(FloorMaterial floor)
            {
                switch (floor)
                {
                    case FloorMaterial.Tile: return 0.6f;
                    case FloorMaterial.Metal: return 0.55f;
                    case FloorMaterial.Wood: return 0.3f;
                    case FloorMaterial.Concrete: return 0.2f;
                    default: return 0.1f;
                }
            }
        }
    }
}
