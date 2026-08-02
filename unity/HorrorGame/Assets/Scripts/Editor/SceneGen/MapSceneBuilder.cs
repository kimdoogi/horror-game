using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

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

        /// <summary>
        /// Child of the root holding the per-storey volumes that keep the NavMesh off
        /// the roofs. Named so a re-bake from the inspector reproduces the same surface.
        /// </summary>
        public const string NavigationBandRootName = "NavMeshBands";

        /// <summary>
        /// The object a generated map must <em>not</em> contain.
        /// <para>
        /// A previous generation bridged each 계단's two flights with a
        /// <c>NavMeshLink</c> under a child of this name. That is B-001:
        /// <see cref="NavMesh.CalculatePath"/> routes through a link, so the
        /// connectivity audit passed, while §06's monster — which steps along
        /// <see cref="NavMeshPath.corners"/> — found nothing to stand on and stopped.
        /// The stairs are geometry now; the name is kept so
        /// <see cref="ForbidStairLinks"/> can say precisely what it deleted if a scene
        /// generated before the fix is ever rebuilt on top of.
        /// </para>
        /// </summary>
        public const string StairLinkRootName = "StairLinks";

        /// <summary>Y offset of a zone's floor slab below the corridor floor, metres. Just enough to stop z-fighting.</summary>
        private const float FloorSlabDepth = 0.01f;

        /// <summary>
        /// The area index Unity reserves for "not walkable" — a hole in the NavMesh.
        /// <para>
        /// Used rather than <see cref="NavMeshModifier.ignoreFromBuild"/> wherever the
        /// geometry still has to be <em>there</em>: a surface marked not-walkable is
        /// still voxelised, so it still denies headroom to whatever is underneath it.
        /// That is what stops a storey's ceiling slabs from becoming a floor for the
        /// storey above (§06 — the monster paths on this surface, and a monster that
        /// can stand on the roof of B2 is not in the building).
        /// </para>
        /// </summary>
        private const int NotWalkableArea = 1;

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
            var shafts = ShaftCells(map);
            var zoneRoots = new GameObject[map.ZoneRects.Length];
            var zoneTileRoots = new GameObject[map.ZoneRects.Length];
            var zoneLightRoots = new GameObject[map.ZoneRects.Length];

            for (var i = 0; i < map.ZoneRects.Length; i++)
            {
                var rect = map.ZoneRects[i];
                zoneRoots[i] = Child(root, ZonePrefix + ZoneSlug(rect) + "_B" + (rect.Level + 1) + "_" + rect.Floor);
                zoneTileRoots[i] = Child(zoneRoots[i], "Tiles");
                zoneLightRoots[i] = Child(zoneRoots[i], ZoneLightGroupName);

                var floorRoot = Child(zoneRoots[i], "Floor");
                var ceilingRoot = Child(zoneRoots[i], "Ceiling");
                BuildFloorSlab(floorRoot, rect, shafts, surfaces);
                BuildCeilingCaps(ceilingRoot, rect, map, shafts, surfaces);

                // Neither of these is anywhere you can walk, and both used to be baked
                // as if they were. The slab is poured across the zone's whole rectangle
                // — under the walls and under the solid ground between corridors — one
                // slab-thickness below the kit's own floors, so the bake read it as a
                // storey-wide open plane with the real corridors sitting on top of it
                // as separate raised basins. The caps are the outside of the roof.
                // Together they were most of B-001: §12's walls stopped meaning
                // anything, and every marker snapped to whichever of the two surfaces
                // happened to be nearer.
                //
                // Out of the bake entirely rather than merely not walkable, and that
                // distinction is the rest of B-001. A zone's rectangle includes the
                // cells a 계단 rises through, so the slab is a plate lying across the
                // top of every stairwell, 0.16 m under the landing it seals off. Left
                // in the bake as an obstruction it stops the flight dead — measured,
                // every one of the six stairs climbed about 1.5 m and ended, which is
                // one island per storey and no antagonist. Nothing is lost by removing
                // them: the storey bands below already deny the heights these sit at,
                // and their colliders still answer §04's 발소리 raycast.
                KeepOutOfNavMeshBake(floorRoot);
                KeepOutOfNavMeshBake(ceilingRoot);
            }

            var sharedRoot = Child(root, "Shared");
            var climbs = new List<VerticalRoute>();

            foreach (var tile in map.Tiles)
            {
                var parent = tile.ZoneId >= 0 ? zoneTileRoots[tile.ZoneId] : sharedRoot;
                var go = Place(tile.Piece, parent, tile.YawDegrees);
                if (go == null)
                {
                    continue;
                }

                go.name = tile.Piece + "_L" + tile.Origin.Level + "_" + tile.Origin.X + "_" + tile.Origin.Z;

                // Y comes from the cell's own storey. A piece placed at 0 regardless
                // would stack every floor of the building into one, which validates
                // perfectly — §12's rules are all horizontal — and is unplayable.
                AlignMinCorner(go, tile.Origin.Min.X, tile.Origin.Min.Y, tile.Origin.Min.Z);
                Finish(go, SurfaceOf(tile.Piece, tile.ZoneId, map), surfaces);

                // The two pieces whose walking surface deliberately leaves its own
                // storey's floor: a 계단 climbs a whole StoreyMetres and §04's 갤러리
                // stands GalleryRiseMetres above the hall it overlooks. Everything else
                // that high is a roof — see BuildNavigationBands, which needs to know
                // the difference and cannot get it from the height alone.
                if (ClimbsBetweenStoreys(tile.Piece) && TryBounds(go, out var climbBounds))
                {
                    climbs.Add(new VerticalRoute(tile.Piece, tile.Origin.Level, climbBounds, go.name));
                }
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
                KeepOutOfNavMeshBake(go);
            }

            BuildMarkers(root, map, zoneLightRoots);
            BuildAmbience();
            BuildNavigationBands(root, map, climbs);
            BakeNavMesh(root, map, sceneName);
            ForbidStairLinks(root);
            VerifyStairwellsAreWalkable(climbs);

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

                if (marker.Kind == MapMarkerKind.LockableDoor)
                {
                    BuildDoor(marker, markerRoot);
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

        /// <summary>
        /// Builds §12's lockable door: a frame, a leaf that swings, a blocking collider
        /// and a carving obstacle, with <c>DoorInteractable</c> on top.
        /// <para>
        /// <b>The kit has had these pieces since the first pass and the scene never
        /// instantiated one.</b> <c>MapSketch.Door()</c> marked a cell, <c>MapValidator</c>
        /// measured that shutting it would force a detour worth more than §06's release
        /// time, and then nothing built a door — the rule was checked against geometry
        /// that did not exist. Everything below is the missing half.
        /// </para>
        /// <para>
        /// The leaf is a child so it can swing about its own hinge edge rather than about
        /// the doorway's centre, which is what makes it read as a door instead of a
        /// turnstile. The obstacle carves, so the creature's own pathing agrees with what
        /// the player can see: an uncarved obstacle leaves the agent sliding along a shut
        /// door looking for a gap, and §06 needs it to arrive and start working.
        /// </para>
        /// </summary>
        private static void BuildDoor(MapMarkerPlacement marker, GameObject markerRoot)
        {
            var hinge = Child(markerRoot, marker.Name);
            hinge.transform.position = ToUnity(marker.Position);

            var frame = Place(MapKitPiece.DoorwayFrame, hinge, 0f);
            if (frame != null)
            {
                frame.name = "Frame";
                frame.transform.localPosition = Vector3.zero;
            }

            // The hinge sits at one jamb, not in the middle, so the leaf sweeps the
            // doorway the way a door does.
            var pivot = Child(hinge, "Hinge");
            pivot.transform.localPosition = new Vector3(-MapKitCatalogue.CorridorClearWidth * 0.5f, 0f, 0f);

            var leaf = Place(MapKitPiece.DoorPanelLockable, pivot, 0f);
            if (leaf != null)
            {
                leaf.name = "Leaf";
                leaf.transform.localPosition = new Vector3(MapKitCatalogue.CorridorClearWidth * 0.5f, 0f, 0f);
            }

            var blocker = pivot.AddComponent<BoxCollider>();
            blocker.size = new Vector3(MapKitCatalogue.CorridorClearWidth, 2.4f, 0.14f);
            blocker.center = new Vector3(MapKitCatalogue.CorridorClearWidth * 0.5f, 1.2f, 0f);

            var obstacle = pivot.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.size = blocker.size;
            obstacle.center = blocker.center;
            obstacle.carving = true;

            // The reach a player needs to take hold of it, and the box the interactor
            // raycasts against. A trigger so it never blocks anybody by itself — the
            // blocking is the collider above, which the component switches.
            var grab = hinge.AddComponent<BoxCollider>();
            grab.isTrigger = true;
            grab.size = new Vector3(MapKitCatalogue.CorridorClearWidth, 2.2f, 0.9f);
            grab.center = new Vector3(0f, 1.1f, 0f);

            // No DoorInteractable here. SceneGen is its own asmdef and the component is
            // in Assembly-CSharp, so the reference only runs one way — the same boundary
            // that keeps MonsterAgent from seeing a door. MatchDirector adds it at match
            // start by finding this group, which is how GhostSession is attached too and
            // means a door needs no scene authoring at all.
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
            // Defers to the same table §07's runtime director reads, rather than
            // holding a second opinion about what a night looks like.
            //
            // This used to write its own near-black flat ambient, which was a
            // reasonable placeholder and a bad neighbour: the generator runs after
            // the atmosphere pass as often as before it, and whichever ran last
            // won. Worse, a flat term plus Unity's default daytime skybox left in
            // RenderSettings meant a freshly generated map rendered under a bright
            // procedural sky — the single largest error in any frame, and one that
            // no test could see.
            HorrorGame.Rendering.NightAtmosphere.ApplyEnvironment(
                HorrorGame.Rendering.NightAtmosphere.ForTier(0));

            // The skybox is an asset, so it cannot live in the runtime table. A
            // basement scene with the default sky is wrong even before it is graded:
            // every opening onto the courtyard becomes a lightbox.
            var nightSky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/HorrorGame_NightSky.mat");
            if (nightSky != null)
            {
                RenderSettings.skybox = nightSky;
            }
        }

        // ====================================================================
        // Floors — §12 청음사. "구역별로 바닥 재질이 달라야 청음사가 위치를 판별할 수 있다."
        // ====================================================================

        private static void BuildFloorSlab(
            GameObject parent, MapZoneRect rect, HashSet<MapCell> shafts, SurfaceAssets surfaces)
        {
            var piece = MapKitCatalogue.FloorTileFor(rect.Floor);
            var step = Mathf.RoundToInt(MapKitCatalogue.FloorTileMetres / MapKitCatalogue.GridMetres);

            for (var x = rect.CellX; x < rect.CellX + rect.CellsX; x += step)
            {
                for (var z = rect.CellZ; z < rect.CellZ + rect.CellsZ; z += step)
                {
                    // A slab poured at this storey's floor level lands 0.01 m under the
                    // head of any 계단 climbing into it, which is a lid on the stairs.
                    // See SealsAShaft — this is the player half of B-001.
                    if (SealsAShaft(shafts, rect.Level + 1, x, z, step))
                    {
                        continue;
                    }

                    var go = Place(piece, parent, 0f);
                    if (go == null)
                    {
                        return;
                    }

                    go.name = piece + "_L" + rect.Level + "_" + x + "_" + z;
                    var cell = new MapCell(x, z, rect.Level);
                    AlignFloorTop(go, cell.Min.X, cell.Min.Z, cell.Min.Y - FloorSlabDepth);
                    Finish(go, rect.Floor, surfaces);
                }
            }
        }

        /// <summary>
        /// Roofs the parts of a lower storey that nothing is standing on top of.
        /// <para>
        /// A storey's ceiling is normally the floor slab of the storey above it, and
        /// that works wherever the two footprints overlap. They do not always overlap:
        /// §12 asks for 4~6 zones and 30~40 m diagonals but says nothing about
        /// stacking them, so the generator is free to put a B3 zone somewhere no B2
        /// zone reaches — and it does. Those cells got no ceiling from anything, and
        /// the result was a room seven and a half metres underground with the night
        /// sky visible above the walls. It is the single most fiction-breaking thing
        /// in any frame of this map, and no validator could see it: §12's checklist is
        /// entirely horizontal, and the graph it runs against has no notion of a
        /// ceiling at all.
        /// </para>
        /// <para>
        /// Concrete regardless of the zone's own §12 surface. The 청음사 rule is about
        /// what is underfoot — a ceiling is never walked on, so giving it the zone's
        /// floor material would put 자갈 on a soffit for no gain.
        /// </para>
        /// </summary>
        private static void BuildCeilingCaps(
            GameObject parent, MapZoneRect rect, MapSketchResult map,
            HashSet<MapCell> shafts, SurfaceAssets surfaces)
        {
            // Level 0 is the top storey; what is above it is the building, which is
            // not this generator's problem.
            if (rect.Level <= 0)
            {
                return;
            }

            var piece = MapKitCatalogue.FloorTileFor(FloorMaterial.Concrete);
            var step = Mathf.RoundToInt(MapKitCatalogue.FloorTileMetres / MapKitCatalogue.GridMetres);

            for (var x = rect.CellX; x < rect.CellX + rect.CellsX; x += step)
            {
                for (var z = rect.CellZ; z < rect.CellZ + rect.CellsZ; z += step)
                {
                    if (CoveredFromAbove(map, rect.Level, x, z, step))
                    {
                        continue;
                    }

                    // A cap goes exactly where the storey above's floor slab would
                    // have gone, so it seals a 계단 rising out of *this* storey for
                    // exactly the same reason and at exactly the same height.
                    if (SealsAShaft(shafts, rect.Level, x, z, step))
                    {
                        continue;
                    }

                    var go = Place(piece, parent, 0f);
                    if (go == null)
                    {
                        return;
                    }

                    go.name = "CeilingCap_L" + rect.Level + "_" + x + "_" + z;

                    // Placed exactly where the storey above's floor slab would have
                    // gone, so a capped cell and an overlapped one are the same height
                    // and the join is invisible.
                    var above = new MapCell(x, z, rect.Level - 1);
                    AlignFloorTop(go, above.Min.X, above.Min.Z, above.Min.Y - FloorSlabDepth);
                    Finish(go, FloorMaterial.Concrete, surfaces);
                }
            }
        }

        /// <summary>
        /// The grid cells every 계단 rises through, keyed by the storey the shaft
        /// itself stands on.
        /// <para>
        /// <see cref="MapKitPiece.StairwellMetal"/> is two cells square and climbs a
        /// whole <see cref="MapKitCatalogue.StoreyMetres"/>, so a shaft standing on
        /// level L has its head, its top landing and its upper dock inside level L−1.
        /// Nothing may be poured at that height across those cells.
        /// </para>
        /// </summary>
        private static HashSet<MapCell> ShaftCells(MapSketchResult map)
        {
            var cells = new HashSet<MapCell>();
            var span = Mathf.RoundToInt(MapKitCatalogue.FloorTileMetres / MapKitCatalogue.GridMetres);

            foreach (var tile in map.Tiles)
            {
                if (tile.Piece != MapKitPiece.StairwellMetal)
                {
                    continue;
                }

                for (var dx = 0; dx < span; dx++)
                {
                    for (var dz = 0; dz < span; dz++)
                    {
                        cells.Add(new MapCell(tile.Origin.X + dx, tile.Origin.Z + dz, tile.Origin.Level));
                    }
                }
            }

            return cells;
        }

        /// <summary>
        /// Whether a plate covering the tile at <paramref name="cellX"/>,
        /// <paramref name="cellZ"/> would lie across the head of a 계단 standing on
        /// <paramref name="shaftLevel"/>.
        /// <para>
        /// This is B-001 measured against the other body, and it is the reason a
        /// player could not leave the entrance storey of a map that audited at
        /// 1830/1830 with one island. The zone floor slabs and the ceiling caps are
        /// already kept out of the NavMesh bake — <see cref="KeepOutOfNavMeshBake"/>,
        /// and the comment there says in as many words that they lie "0.16 m under the
        /// landing they seal off". What was fixed then was the bake. The
        /// <see cref="MeshCollider"/> stayed, so the monster walked down a stair that
        /// was, to a <see cref="CharacterController"/>, floored over 0.01 m below its
        /// top landing. Every gate in the project reads the surface the monster paths
        /// on; none of them touched the surface a player stands on.
        /// </para>
        /// <para>
        /// So the plate is not poured at all rather than merely excluded from the bake.
        /// Over the shaft's own footprint nothing is lost: the 계단 is a fully enclosed
        /// piece with its own floor, walls and ceiling, and a stairwell is supposed to
        /// be a hole between two storeys. <see cref="PlayerTraversal"/> is the check
        /// that keeps it that way.
        /// </para>
        /// </summary>
        private static bool SealsAShaft(
            HashSet<MapCell> shafts, int shaftLevel, int cellX, int cellZ, int step)
        {
            for (var x = cellX; x < cellX + step; x++)
            {
                for (var z = cellZ; z < cellZ + step; z++)
                {
                    if (shafts.Contains(new MapCell(x, z, shaftLevel)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether any zone one storey up covers the whole tile starting at
        /// <paramref name="cellX"/>, <paramref name="cellZ"/>.
        /// <para>
        /// Whole tile, not just its origin corner: a tile is two grid cells across and
        /// a partial overlap would leave a slot of open sky along the edge, which
        /// looks like a lighting bug rather than a missing ceiling and is therefore
        /// harder to find than no ceiling at all.
        /// </para>
        /// </summary>
        private static bool CoveredFromAbove(MapSketchResult map, int level, int cellX, int cellZ, int step)
        {
            for (var x = cellX; x < cellX + step; x++)
            {
                for (var z = cellZ; z < cellZ + step; z++)
                {
                    var covered = false;
                    foreach (var other in map.ZoneRects)
                    {
                        if (other.Level != level - 1)
                        {
                            continue;
                        }

                        if (x >= other.CellX && x < other.CellX + other.CellsX
                            && z >= other.CellZ && z < other.CellZ + other.CellsZ)
                        {
                            covered = true;
                            break;
                        }
                    }

                    // A kit piece standing on the cell one storey up counts as covered
                    // too, and this clause is load-bearing for more than tidiness: a
                    // stairwell rising out of this storey lands in a corridor above,
                    // and corridors are tiles rather than zone rects. Without this the
                    // cap would be poured straight across the top of the stairs and
                    // seal the only vertical route §03's clue chain has between floors
                    // — a NavMesh island, invisible in every screenshot.
                    if (!covered)
                    {
                        foreach (var tile in map.Tiles)
                        {
                            if (tile.Origin.Level == level - 1
                                && tile.Origin.X == x && tile.Origin.Z == z)
                            {
                                covered = true;
                                break;
                            }
                        }
                    }

                    if (!covered)
                    {
                        return false;
                    }
                }
            }

            return true;
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
            /// Extra yaw a piece needs once the import and the stand-up rotation have
            /// been applied.
            /// <para>
            /// These are measured against the imported kit, not derived. The FBXs arrive
            /// with X negated — a corridor authored over X ∈ [0, 2.5] imports over
            /// [−2.5, 0] — so a dock the kit puts on +X is on −X by the time the
            /// generator sees it, and that is a reflection rather than a turn. Reasoning
            /// about it in the abstract is how the two values below were wrong: a piece
            /// whose docks are symmetric survives either way, so straights, crosses and
            /// doorways looked fine and hid the fact that the asymmetric ones did not.
            /// </para>
            /// <para>
            /// What it cost: every L-corner stood a quarter-turn out and every T-junction
            /// a half-turn, so each of them offered a wall where §12's graph said there
            /// was a passage. The map still validated 17/17 — the checklist reads the
            /// graph — and the bake, correctly, refused to path through the walls. That
            /// is most of B-001's island count.
            /// </para>
            /// <para>
            /// Verified by placing every asymmetric piece at each of the four yaws and
            /// measuring which sides open. If the kit is re-exported, re-measure: the
            /// symptom of a wrong value here is a NavMesh audit failure, never a
            /// compile error and never anything visible in a screenshot.
            /// </para>
            /// </summary>
            private static float DockOffset(MapKitPiece piece)
            {
                switch (piece)
                {
                    case MapKitPiece.CorridorCornerL:
                    case MapKitPiece.JunctionT:
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

        /// <summary>
        /// Bakes the surface §06's monster paths on.
        /// <para>
        /// Everything here is set <em>on the component</em> rather than passed to a
        /// hand-rolled <see cref="NavMeshBuilder"/> call, and that is deliberate: the
        /// dressing pass re-bakes this surface with <c>surface.BuildNavMesh()</c> after
        /// it scatters cover, and the inspector's Bake button does the same. A bake
        /// configuration that lived in this method would be silently replaced by
        /// whichever of those ran last — the exact class of invisible failure B-001 was.
        /// </para>
        /// </summary>
        private static void BakeNavMesh(GameObject root, MapSketchResult map, string sceneName)
        {
            var go = Child(root, "NavMesh");
            var surface = go.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = go.AddComponent<NavMeshSurface>();
            }

            // Render meshes rather than colliders: the kit's walls and floors are all
            // renderers, and colliders are added by this generator — collecting the
            // physics geometry would bake whatever ran first.
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;

            // The whole scene, and the roofs are excluded by the storey bands
            // BuildNavigationBands lays down rather than by a bounding volume. A volume
            // was tried first and does not do it: NavMeshSurface's volume decides which
            // objects are *collected*, and a corridor is one merged mesh whose ceiling
            // comes along with its floor however the box is cut.
            surface.collectObjects = CollectObjects.All;

            // The area of the agent's own footprint: a region smaller than the monster
            // is not a place it can be sent to. Deliberately not larger — Recast culls
            // regions per tile, so a threshold near the width of a corridor can delete
            // the sliver where a corridor crosses a tile boundary and cut the map in
            // half for a reason nothing in the scene explains.
            var agent = NavMesh.GetSettingsByID(surface.agentTypeID);
            surface.minRegionArea = Mathf.PI * agent.agentRadius * agent.agentRadius;

            // Finer than Unity's default of one third the agent radius, because this
            // building has places where the vertical clearance is barely more than the
            // agent's own height and the default throws that margin away in rounding.
            // §12's 20 m hall is 6.3 m tall against a 3.75 m storey, so a hall on B2
            // pushes its roof up into B1 and leaves the corridor over it 2.10 m of
            // headroom against a 2.00 m agent. At radius/3 the voxel column loses up to
            // 0.17 m of that to rounding and the corridor does not bake at all — two
            // whole rows of B1, including both 계단 landings on the 하역장 side, which
            // is why B1 was cut in half.
            surface.overrideVoxelSize = true;
            surface.voxelSize = agent.agentRadius / 5f;

            WarnIfAgentDoesNotFit(agent, surface.agentTypeID);

            // Clear the GLOBAL NavMesh before baking, and this is B-009.
            //
            // NavMeshSurface.BuildNavMesh writes into this surface's own navMeshData.
            // NavMesh.CalculatePath and NavMesh.SamplePosition — which is all NavMeshAudit
            // and NavMeshConnectivity use — read the GLOBAL mesh, which is the union of
            // every NavMeshData anyone has added this session. A surface left over from a
            // previously opened scene is still in it, and the audit happily walks it.
            //
            // Measured: three regenerations with genuinely different geometry — 29 caps,
            // then caps spaced three cells apart, then NO CAPS AT ALL — produced
            // byte-identical audits. 8717 pairs complete, 93.5%, 17 islands, the creature
            // reaching 0 of 3, every time. The markers were fresh, so the marker names in
            // the island dump were from the new map; the surface underneath them was not.
            //
            // This matters well beyond one map. NavMeshAudit is the gate that decides
            // whether a level ships, and until now it could pass or fail on a surface that
            // was not the one just built.
            var before = NavMesh.CalculateTriangulation().vertices.Length;
            NavMesh.RemoveAllNavMeshData();

            surface.BuildNavMesh();

            // BuildNavMesh registers this surface's data, but say so out loud rather than
            // trusting it: the whole defect above was an assumption about what was
            // registered.
            var after = NavMesh.CalculateTriangulation().vertices.Length;
            if (after == 0)
            {
                Debug.LogError(
                    "[SceneGen] The global NavMesh is empty after baking, so every audit that "
                    + "follows would measure nothing and report it as a pass. B-009.");
            }
            else if (before == after && before > 0)
            {
                Debug.LogError(
                    "[SceneGen] The global NavMesh has the same " + after + " vertices it had "
                    + "BEFORE this bake. It is stale — the audits about to run are measuring "
                    + "someone else's surface. B-009.");
            }
            else
            {
                // The tile count beside the vertex count, and this pairing is the point.
                // B-009b: four regenerations with demonstrably different tile lists produced
                // a byte-identical audit — 6863 pairs, 98.1%, 11 islands, every time. Either
                // the geometry is not reaching the scene or the audit is not reading it, and
                // no amount of staring at the generator distinguishes those. Two numbers that
                // move together prove the chain; two that do not name the broken link.
                var meshes = 0;
                var tris = 0;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                    {
                        continue;
                    }

                    meshes++;
                    tris += filter.sharedMesh.triangles.Length / 3;
                }

                Debug.Log("[SceneGen] NavMesh baked: " + after + " vertices (was " + before
                          + " before the clear), from " + meshes + " meshes / " + tris
                          + " triangles in the scene. §06 paths on this and nothing else. "
                          + "If the triangle count moves between runs and the audit does not, "
                          + "the audit is not reading this surface — B-009b.");
            }

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

        /// <summary>
        /// Deletes any off-mesh link left in the scene, and says which.
        /// <para>
        /// The generator builds into a fresh empty scene, so this normally finds
        /// nothing. It exists because a link is the one thing that can make every gate
        /// in this project agree that a broken map is fine: <c>NavMeshAudit</c>,
        /// <see cref="NavMeshConnectivity"/> and <see cref="NavMesh.CalculatePath"/> all
        /// walk through one, and only §06's monster — stepping corner to corner along
        /// <see cref="NavMeshPath.corners"/> — ever discovers there is nothing there.
        /// A route the antagonist cannot take and a player cannot take is not a route,
        /// so the map is not allowed to contain one at all.
        /// </para>
        /// <para>
        /// If a link is ever genuinely wanted — §12 does make changing storey a
        /// deliberate act, and a ladder or a drop is a reasonable thing to want — then
        /// <c>MonsterBrain</c> has to learn to traverse one first. Today it steps to the
        /// next path corner and a link has none. That is a design decision and a
        /// movement feature at once, not something the scene generator may introduce on
        /// its own.
        /// </para>
        /// </summary>
        private static void ForbidStairLinks(GameObject root)
        {
            var links = root.GetComponentsInChildren<NavMeshLink>(true);
            foreach (var link in links)
            {
                Debug.LogWarning("[SceneGen] Removed a NavMeshLink at " + link.transform.position.ToString("F2")
                    + " (" + link.name + "). §06's monster steps along NavMeshPath.corners and a link has "
                    + "no corners to step on — that is B-001, and the audit cannot see it.");
                Object.DestroyImmediate(link);
            }

            var stale = root.transform.Find(StairLinkRootName);
            if (stale != null)
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }

        /// <summary>
        /// Measures every 계단 as one surface and fails the build when it is two.
        /// <para>
        /// This used to bridge the gap with a <see cref="NavMeshLink"/> instead, and
        /// that is the whole of B-001. <see cref="MapKitPiece.StairwellMetal"/>'s spine
        /// ran the full depth of the mid-landing and died into the back wall, so the
        /// two flights baked as two disconnected surfaces inside one shaft. A link
        /// across the landing made <see cref="NavMesh.CalculatePath"/> answer yes and
        /// the connectivity audit went green — while §06's monster, which does not walk
        /// by <c>CalculatePath</c> but steps along <see cref="NavMeshPath.corners"/> one
        /// corner at a time, stood at the link mouth for the whole match. A link is a
        /// gap with nothing to step onto, and a player cannot use one at all.
        /// </para>
        /// <para>
        /// So: no links, and this instead. The stair is now walkable geometry — the
        /// spine stops at the head of the flights and continues below the landing as
        /// its support — and the job here is to keep it that way. A kit re-export that
        /// closes the landing again fails the generation with the measurement in the
        /// message rather than shipping a map whose antagonist cannot leave B3.
        /// </para>
        /// <para>
        /// The test is deliberately the monster's question and not the audit's: every
        /// patch of baked surface inside the shaft has to be reachable from every
        /// other, <em>and</em> the shaft has to have surface at both ends of the climb.
        /// A stair whose upper flight never baked at all would satisfy the first
        /// condition on its own.
        /// </para>
        /// </summary>
        private static void VerifyStairwellsAreWalkable(List<VerticalRoute> climbs)
        {
            var stairs = 0;

            foreach (var climb in climbs)
            {
                if (climb.Piece != MapKitPiece.StairwellMetal)
                {
                    continue;
                }

                stairs++;
                var floorY = MapKitCatalogue.FloorY(climb.Level);
                var top = floorY + MapKitCatalogue.StoreyMetres;
                var footholds = FootholdsIn(climb.Bounds, floorY);

                // Half a storey either side of the two ends, so a sample that landed on
                // the second tread still counts as "the bottom baked".
                var margin = MapKitCatalogue.StoreyMetres * 0.5f;
                var atBottom = 0;
                var atTop = 0;
                foreach (var point in footholds)
                {
                    if (point.y < floorY + margin)
                    {
                        atBottom++;
                    }
                    else if (point.y > top - margin)
                    {
                        atTop++;
                    }
                }

                if (atBottom == 0 || atTop == 0)
                {
                    Debug.LogError("[SceneGen] " + climb.Name + " baked " + footholds.Count
                        + " patches of surface, " + atBottom + " at the foot and " + atTop
                        + " at the head. A 계단 with nothing to stand on at one end is not a route between "
                        + "storeys, and §03's clue chain narrows by storey first — fix build_stairwell in "
                        + "tools/blender/gen_mapkit.py.");
                    continue;
                }

                // The monster's question: is this one surface? Anything the first
                // foothold cannot walk to is a second island inside a single stairwell.
                var stranded = 0;
                var worst = Vector3.zero;
                var path = new NavMeshPath();
                foreach (var point in footholds)
                {
                    NavMesh.CalculatePath(footholds[0], point, NavMesh.AllAreas, path);
                    if (path.status != NavMeshPathStatus.PathComplete)
                    {
                        stranded++;
                        worst = point;
                    }
                }

                if (stranded > 0)
                {
                    Debug.LogError("[SceneGen] " + climb.Name + ": " + stranded + " of " + footholds.Count
                        + " patches of baked surface inside the shaft cannot be walked to from "
                        + footholds[0].ToString("F2") + " — the worst at " + worst.ToString("F2")
                        + ". The two flights do not meet on the landing. That is B-001: it used to be "
                        + "bridged with a NavMeshLink, which made the audit pass and left §06's monster "
                        + "standing at the link mouth for the whole match. Fix build_stairwell in "
                        + "tools/blender/gen_mapkit.py — the mid-landing has to be at least "
                        + (4f * NavMesh.GetSettingsByID(0).agentRadius).ToString("0.00")
                        + " m deep with nothing standing on it.");
                }
            }

            if (stairs > 0)
            {
                Debug.Log("[SceneGen] " + stairs + " 계단 verified as single walkable surfaces, no NavMeshLink "
                    + "anywhere in the map. §06's monster steps along NavMeshPath.corners, so every storey "
                    + "boundary it crosses has to be geometry it can stand on.");
            }
        }

        /// <summary>
        /// Every distinct patch of baked surface inside one stairwell, as points.
        /// <para>
        /// Sampled rather than derived, because the question is what the bake produced
        /// and not what the model intended. The vertical steps are one agent-step
        /// apart, so a flight is caught at least once per few treads.
        /// </para>
        /// </summary>
        private static List<Vector3> FootholdsIn(Bounds shaft, float floorY)
        {
            var found = new List<Vector3>();
            var step = MapKitCatalogue.GridMetres * 0.2f;

            for (var x = shaft.min.x + step; x < shaft.max.x; x += step)
            {
                for (var z = shaft.min.z + step; z < shaft.max.z; z += step)
                {
                    for (var h = 0.2f; h <= MapKitCatalogue.StoreyMetres + 0.4f; h += 0.5f)
                    {
                        if (!NavMesh.SamplePosition(
                            new Vector3(x, floorY + h, z), out var hit, 0.3f, NavMesh.AllAreas))
                        {
                            continue;
                        }

                        // Only what is inside this shaft: a sample near the mouth can
                        // otherwise snap onto the landing corridor outside it, and a
                        // link anchored there would bridge nothing.
                        if (hit.position.x < shaft.min.x || hit.position.x > shaft.max.x
                            || hit.position.z < shaft.min.z || hit.position.z > shaft.max.z)
                        {
                            continue;
                        }

                        var duplicate = false;
                        foreach (var other in found)
                        {
                            if ((other - hit.position).sqrMagnitude < 0.04f)
                            {
                                duplicate = true;
                                break;
                            }
                        }

                        if (!duplicate)
                        {
                            found.Add(hit.position);
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>Whether a piece's walking surface deliberately leaves its own storey's floor.</summary>
        private static bool ClimbsBetweenStoreys(MapKitPiece piece) =>
            piece == MapKitPiece.StairwellMetal || piece == MapKitPiece.ObservationPostGallery;

        /// <summary>
        /// Marks the height band above each storey's floor as not walkable, so the
        /// building's roofs stop being a second map.
        /// <para>
        /// This is the other half of B-001. A kit piece is a single merged mesh, so its
        /// ceiling slab arrives with its floor and no amount of marking objects can
        /// separate them; the bake put navmesh on every one of those slabs and on the
        /// roofs of the 20 m halls, which stand 6.3 m tall and therefore poke clean
        /// through the storey above. Measured before this ran: 1137 m² of navigation
        /// surface on roofs, against 1165 m² on all three real floors combined. None of
        /// it is reachable, so every patch of it was its own island, and any marker
        /// whose own floor was missing snapped up onto it instead.
        /// </para>
        /// <para>
        /// The cut-off is <see cref="MapKitCatalogue.CorridorClearHeight"/> minus the
        /// agent's height rather than a chosen number: under a 3 m soffit, a surface
        /// higher than that has less than the agent's own height above it, so §06's
        /// monster could not stand there even if the bake said it could.
        /// </para>
        /// <para>
        /// 계단 and 갤러리 are cut out of the band, because those two are exactly the
        /// pieces whose job is to leave the floor — §12 gives stairs their own 금속
        /// surface precisely because changing storey is a designed act. Each is spared
        /// only in its <em>own</em> storey's band: a flight climbs one
        /// <see cref="MapKitCatalogue.StoreyMetres"/> and lands below the next band's
        /// floor, so the shaft's own roof, two storeys up, still gets marked.
        /// </para>
        /// </summary>
        private static void BuildNavigationBands(GameObject root, MapSketchResult map, List<VerticalRoute> climbs)
        {
            var footprint = FootprintOf(root);
            var agent = NavMesh.GetSettingsByID(0);
            var standing = Mathf.Max(MapKitCatalogue.GridMetres * 0.1f,
                MapKitCatalogue.CorridorClearHeight - agent.agentHeight);

            var deepest = 0;
            for (var i = 0; i < map.Tiles.Length; i++)
            {
                deepest = Mathf.Max(deepest, map.Tiles[i].Origin.Level);
            }

            for (var i = 0; i < map.ZoneRects.Length; i++)
            {
                deepest = Mathf.Max(deepest, map.ZoneRects[i].Level);
            }

            var bandRoot = Child(root, NavigationBandRootName);
            var count = 0;

            // Level −1 is the sky: everything above where a storey above B1 would put
            // its floor, with nothing spared, which is what catches a stairwell's own
            // roof two storeys up and the 6.3 m halls that poke out of the top of B2.
            //
            // A band stops at the next storey's floor line and not one metre above it.
            // Getting that wrong is not subtle and not visible either: bands that
            // overshoot swallow the floor below them, and the first attempt at this
            // deleted every walkable surface in the building except one stairwell.
            for (var level = -1; level <= deepest; level++)
            {
                var low = MapKitCatalogue.FloorY(level) + standing;
                var high = level < 0
                    ? MapKitCatalogue.FloorY(level) + (MapKitCatalogue.StoreyMetres * 8f)
                    : MapKitCatalogue.FloorY(level - 1);

                foreach (var box in BandBoxes(footprint, climbs, level))
                {
                    var go = Child(bandRoot, "NavBand_L" + level + "_" + count);
                    count++;
                    go.transform.position = new Vector3(box.center.x, (low + high) * 0.5f, box.center.z);

                    var volume = go.GetComponent<NavMeshModifierVolume>();
                    if (volume == null)
                    {
                        volume = go.AddComponent<NavMeshModifierVolume>();
                    }

                    volume.center = Vector3.zero;
                    volume.size = new Vector3(box.size.x, high - low, box.size.z);
                    volume.area = NotWalkableArea;
                }
            }
        }

        /// <summary>
        /// The footprint of one storey's band, minus the 계단 and 갤러리 that pass
        /// through it, as a handful of rectangles rather than one per cell.
        /// <para>
        /// Cells rather than metres so a spared shaft lines up with the grid the piece
        /// was placed on; runs merged along Z so a map with a couple of stairwells
        /// costs a couple of boxes instead of a thousand.
        /// </para>
        /// </summary>
        private static List<Bounds> BandBoxes(Bounds footprint, List<VerticalRoute> climbs, int level)
        {
            var grid = MapKitCatalogue.GridMetres;
            var x0 = Mathf.FloorToInt(footprint.min.x / grid) - 1;
            var x1 = Mathf.CeilToInt(footprint.max.x / grid) + 1;
            var z0 = Mathf.FloorToInt(footprint.min.z / grid) - 1;
            var z1 = Mathf.CeilToInt(footprint.max.z / grid) + 1;

            bool Spared(int cellX, int cellZ)
            {
                var minX = cellX * grid;
                var minZ = cellZ * grid;
                foreach (var climb in climbs)
                {
                    if (climb.Level != level)
                    {
                        continue;
                    }

                    // Half-open overlap: a shaft that ends exactly on a cell boundary
                    // must not claim the corridor on the other side of its wall, whose
                    // ceiling is the thing this band exists to mark.
                    if (minX + 0.01f < climb.Bounds.max.x && minX + grid - 0.01f > climb.Bounds.min.x
                        && minZ + 0.01f < climb.Bounds.max.z && minZ + grid - 0.01f > climb.Bounds.min.z)
                    {
                        return true;
                    }
                }

                return false;
            }

            var boxes = new List<Bounds>();
            for (var x = x0; x < x1; x++)
            {
                var runStart = int.MinValue;
                for (var z = z0; z <= z1; z++)
                {
                    var open = z < z1 && !Spared(x, z);
                    if (open && runStart == int.MinValue)
                    {
                        runStart = z;
                    }
                    else if (!open && runStart != int.MinValue)
                    {
                        var centreZ = ((runStart + z) * 0.5f) * grid;
                        boxes.Add(new Bounds(
                            new Vector3((x + 0.5f) * grid, 0f, centreZ),
                            new Vector3(grid, 0f, (z - runStart) * grid)));
                        runStart = int.MinValue;
                    }
                }
            }

            return Merge(boxes);
        }

        /// <summary>Joins columns that share a Z span, so the band is a few slabs rather than one per cell.</summary>
        private static List<Bounds> Merge(List<Bounds> boxes)
        {
            var merged = new List<Bounds>();
            foreach (var box in boxes)
            {
                var joined = false;
                for (var i = 0; i < merged.Count; i++)
                {
                    var other = merged[i];
                    if (Mathf.Abs(other.center.z - box.center.z) > 0.01f
                        || Mathf.Abs(other.size.z - box.size.z) > 0.01f)
                    {
                        continue;
                    }

                    if (Mathf.Abs((other.max.x + (box.size.x * 0.5f)) - box.center.x) > 0.01f)
                    {
                        continue;
                    }

                    other.SetMinMax(
                        new Vector3(other.min.x, other.min.y, other.min.z),
                        new Vector3(box.max.x, other.max.y, other.max.z));
                    merged[i] = other;
                    joined = true;
                    break;
                }

                if (!joined)
                {
                    merged.Add(box);
                }
            }

            return merged;
        }

        /// <summary>World bounds of everything placed so far. Used for the map's extent, not for its height.</summary>
        private static Bounds FootprintOf(GameObject root)
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(MapKitCatalogue.GridMetres, 0f, MapKitCatalogue.GridMetres));
            var seen = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (!seen)
                {
                    bounds = renderer.bounds;
                    seen = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }

        /// <summary>A placed piece whose walking surface climbs out of its own storey.</summary>
        private readonly struct VerticalRoute
        {
            public VerticalRoute(MapKitPiece piece, int level, Bounds bounds, string name)
            {
                Piece = piece;
                Level = level;
                Bounds = bounds;
                Name = name;
            }

            /// <summary>Which kit piece it is — only a 계단 gets bridged.</summary>
            public MapKitPiece Piece { get; }

            /// <summary>Storey the piece stands on — the only band it is spared in.</summary>
            public int Level { get; }

            /// <summary>World bounds of the placed piece.</summary>
            public Bounds Bounds { get; }

            /// <summary>Scene name of the placed object, so a report can name the stair.</summary>
            public string Name { get; }
        }

        /// <summary>
        /// Says so when the agent type this surface bakes for is not the monster.
        /// <para>
        /// The bake reads its radius, height, step and slope from the project's agent
        /// type, and the runtime <c>NavMeshAgent</c> and <see cref="NavMesh.CalculatePath"/>
        /// both query by that same id — so the id has to stay as it is, and the numbers
        /// behind it are the ones that decide whether the surface the audit measures is
        /// the surface §06's monster can use. A mismatch is not visible in any
        /// screenshot and produces no error, so it is stated in the generation log.
        /// </para>
        /// </summary>
        private static void WarnIfAgentDoesNotFit(NavMeshBuildSettings agent, int agentTypeID)
        {
            Debug.Log("[SceneGen] NavMesh agent " + agentTypeID
                + ": radius " + agent.agentRadius + " m, height " + agent.agentHeight
                + " m, step " + agent.agentClimb + " m, slope " + agent.agentSlope + "°.");

            // §12 requires two players to carry a chest side by side through every
            // doorway, and the kit's openings are CorridorClearWidth across. An agent
            // wider than the opening bakes no path through it at all — the doorway
            // simply is not there for the monster, which reads as bad AI.
            var clear = MapKitCatalogue.CorridorClearWidth - (2f * agent.agentRadius);
            if (clear < agent.agentRadius)
            {
                Debug.LogError("[SceneGen] Agent radius " + agent.agentRadius + " m leaves only "
                    + clear.ToString("0.00") + " m of navigable width in a " + MapKitCatalogue.CorridorClearWidth
                    + " m corridor. §12's doorways will not bake through and §06's chase cannot happen.");
            }

            if (agent.agentHeight > MapKitCatalogue.CorridorClearHeight)
            {
                Debug.LogError("[SceneGen] Agent height " + agent.agentHeight + " m exceeds the kit's "
                    + MapKitCatalogue.CorridorClearHeight + " m corridor clear height, so nothing will bake.");
            }
        }

        // ====================================================================
        // Helpers.
        // ====================================================================

        /// <summary>
        /// Takes an object out of the bake entirely.
        /// <para>
        /// For the props §12 asks for, and for one reason: they are gameplay fixtures
        /// standing in the middle of a 2.2 m corridor, and the bake erodes
        /// <c>agentRadius</c> around everything it collects. A 0.62 m 전기 패널 leaves
        /// 0.79 m either side of it, which is less than the 1.0 m the agent needs, so
        /// baking it severs the corridor outright; a 1.1 m 문짝 leaf severs it twice
        /// over. §04 gives the 정비공 a door to <em>lock</em> — a door that is shut in
        /// the bake deletes §12's 순환로 for the whole match and nobody can open it
        /// again. If a locked door should stop the monster, that is a runtime
        /// <c>NavMeshObstacle</c>, not geometry.
        /// </para>
        /// </summary>
        private static void KeepOutOfNavMeshBake(GameObject go)
        {
            var modifier = go.GetComponent<NavMeshModifier>();
            if (modifier == null)
            {
                modifier = go.AddComponent<NavMeshModifier>();
            }

            modifier.ignoreFromBuild = true;
            modifier.applyToChildren = true;
            modifier.overrideArea = false;
        }

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
                    case FloorMaterial.Water: return new Color(0.13f, 0.17f, 0.19f);
                    case FloorMaterial.Earth: return new Color(0.26f, 0.21f, 0.16f);
                    case FloorMaterial.Carpet: return new Color(0.24f, 0.15f, 0.14f);
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
                    // Standing water is the only mirror in the building, and a torch
                    // coming down a flooded corridor arrives twice.
                    case FloorMaterial.Water: return 0.92f;
                    case FloorMaterial.Earth: return 0.05f;
                    case FloorMaterial.Carpet: return 0.04f;
                    default: return 0.1f;
                }
            }
        }
    }
}
