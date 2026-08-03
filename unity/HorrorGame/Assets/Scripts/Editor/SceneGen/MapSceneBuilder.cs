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
        /// Child of the root holding the per-storey boundary shells — the box each
        /// storey is sealed inside so a runner cannot get out of the building.
        /// <para>
        /// ASCII, and every object under it is ASCII, on purpose. Unity escapes Korean
        /// in <c>m_Name</c> as <c>\uXXXX</c>, so a name like 경계 cannot be counted with
        /// a grep of the written scene and a zero would read as "the shells are missing"
        /// when it means "the grep was wrong". Everything about this object is meant to
        /// be checkable from the <c>.unity</c> file without opening Unity.
        /// </para>
        /// </summary>
        public const string BoundaryRootName = "Boundary";

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
        /// How far below a storey's own floor its shell starts, metres, and therefore
        /// where one storey's shell stops and the next one's begins.
        /// <para>
        /// It is a window, not a taste. The split has to fall below the deepest thing the
        /// storey builds downward — the zone floor slab, whose underside is
        /// <see cref="FloorSlabDepth"/> plus the tile's own 0.154 m, so 0.164 m below the
        /// floor — and it has to leave the tallest thing the storey builds upward inside
        /// the same shell, because the shells tile the tower with no gap: a shell's
        /// inside is exactly one <see cref="MapKitCatalogue.StoreyMetres"/> tall, so
        /// giving the bottom this much takes the same amount off the top, and a corridor
        /// piece is 3.3 m against a 3.75 m storey. That leaves 0.164 &lt; x &lt; 0.45, and
        /// 0.25 is the middle of it. <see cref="BuildStoreyShells"/> re-derives both
        /// walls from <see cref="MapKitCatalogue"/> and says so if a kit re-export ever
        /// closes the window.
        /// </para>
        /// <para>
        /// <b>It no longer decides where any horizontal plate goes, and that is the fix
        /// this round.</b> It used to: every shell grew a floor plate down from this plane
        /// and a lid up from it, which put a 1.0 m slab across the seam and a plate's top
        /// face 0.25 m proud of the floor above. Now it settles one thing only — which
        /// storey's shell owns which slice of the wall curtain. The curtain is continuous
        /// either way, because shell L's walls run from <c>FloorY(L) − this</c> to
        /// <c>FloorY(L−1) − this</c> and consecutive walls therefore meet face to face all
        /// the way down the tower.
        /// </para>
        /// </summary>
        private const float ShellUnderfloorMetres = 0.25f;

        /// <summary>
        /// Thickness of a boundary WALL, metres.
        /// <para>
        /// Not about tunnelling: the runner is a <see cref="CharacterController"/> and
        /// <c>Move</c> sweeps, so it cannot pass through a wall of any thickness. It is
        /// about leaving a wall that is still unambiguously solid when something else
        /// meets it, and the fastest thing that can meet a wall is a runner sprinting
        /// straight into it — <see cref="GameConstants.RunnerSprintSpeed"/> 5.6 m/s, which
        /// is 0.112 m in one <see cref="GameConstants.FixedStep"/>. Half a metre is four
        /// and a half of those. It is also a fifth of the kit's
        /// <see cref="MapKitCatalogue.GridMetres"/> cell, and every wall grows OUTWARD from
        /// the footprint it encloses, so the boundary never claims so much as a cell of
        /// ground beyond the building.
        /// </para>
        /// </summary>
        private const float ShellWallMetres = 0.5f;

        /// <summary>
        /// Thickness of a horizontal boundary plate, metres — derived, not chosen.
        /// <para>
        /// A plate is a different job from a wall and it gets a different number, because
        /// the fastest thing that can meet one is far slower. <b>Nothing falls onto a
        /// plate.</b> The band plates are laid flush with the floor they continue, so a
        /// runner steps onto one without dropping at all; the only vertical travel in the
        /// building is a 투하구, and that lands on the storey's own slab, not on the
        /// boundary. What is left is a jump — <see cref="GameConstants.JumpApexMetres"/>
        /// 0.35 m, so <see cref="GameConstants.JumpTakeoffSpeed"/> 2.62 m/s, or 0.052 m in
        /// one <see cref="GameConstants.FixedStep"/>. Three of those — the shortest run of
        /// steps this project is willing to call unambiguous — is 0.157 m, which lands
        /// within a millimetre of the kit's own 0.154 m floor tile. That is a pleasing check
        /// and not the reason; the reason is the jump.
        /// </para>
        /// <para>
        /// It matters that this is small rather than generous. A plate's top face is flush
        /// with a floor, so every millimetre of its thickness hangs into the room below it;
        /// 0.157 m out of a 3.75 m storey is the same order as the slab that already hangs
        /// there, and half a metre would not be.
        /// </para>
        /// </summary>
        private static readonly float ShellPlateMetres =
            3f * GameConstants.JumpTakeoffSpeed * GameConstants.FixedStep;

        /// <summary>
        /// Metres above its 착지 a 투하구 puts a runner down — the runtime's own
        /// <c>Chute.DropHeightMetres</c>.
        /// <para>
        /// Restated rather than referenced because this class lives in an editor assembly
        /// that does not reference the runtime's Race assembly (see the asmdef's list),
        /// which is the same reason <c>DescentPlaythroughTests</c> keeps its own copy of
        /// the storey pitch. It is used for one thing only: to prove, at generation time,
        /// that no boundary plate stands in the 3.0 m of air a runner falls through. If
        /// the runtime ever changes the drop, this number is wrong in the safe direction
        /// only by luck — so <see cref="VerifyChutesDropIntoOpenAir"/> prints it, and
        /// <c>DescentPlaythroughTests</c> measures the real one against the real geometry.
        /// </para>
        /// </summary>
        private const float ChuteDropHeightMetres = 3.0f;

        /// <summary>
        /// Slack the shell's own containment check allows at a wall, metres.
        /// <para>
        /// The map touches its boundary exactly: a corridor on the zone's first cell has
        /// its outer wall face ON the shell's inner wall face, so the honest margin there
        /// is zero and the check is comparing two floats that came from different
        /// arithmetic — <see cref="AlignMinCorner"/> nudges a transform by
        /// <c>min − bounds.min</c> and then the bounds are recomputed from the moved
        /// transform. A centimetre is four orders of magnitude under the 0.6 m a 0.30 m
        /// capsule needs to pass, so nothing a player can use hides inside it, and it
        /// keeps the check from reporting sixty-four rim pieces as escapes because of a
        /// rounding bit.
        /// </para>
        /// </summary>
        private const float ShellSeamToleranceMetres = 0.01f;

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

            // Last of the geometry, and after everything that owns a renderer, because
            // the shells check themselves against it: nothing the generator wrote may
            // lie outside the box its storey is sealed in.
            BuildStoreyShells(root, map, zoneRoots);
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
        /// Thickness of the box that stands in the opening while the door is shut, metres.
        /// <para>
        /// The leaf itself is 0.12 m of door — <c>build_door_panel</c> in
        /// tools/blender/gen_mapkit.py lays a 0.06 m core between two 0.03 m faces — with
        /// hardware standing off to 0.175 m. 0.14 m is the door and its stop bead and
        /// nothing else: thick enough that a runner cannot be pushed through it by a frame
        /// of physics, thin enough that carving it never reaches the jambs, which would
        /// widen a shut door into a wall the creature routes around from further away than
        /// the player can see the reason for.
        /// </para>
        /// </summary>
        private const float DoorLeafThickness = 0.14f;

        /// <summary>
        /// Builds §12's lockable door: a leaf that swings, a blocking collider and a
        /// carving obstacle, with <c>DoorInteractable</c> on top.
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
        /// <para>
        /// <b>No Doorway_Frame, and that is B-010.</b> A 문 is a PASSAGE plus a PANEL, and
        /// only the panel may ever be in the way. This method used to hang a whole
        /// <see cref="MapKitPiece.DoorwayFrame"/> — a 2.5 m walled cell — off the marker,
        /// and three separate things about that sealed the maze:
        /// <list type="number">
        /// <item>It was baked. <see cref="BakeNavMesh"/> collects render meshes and nothing
        /// took this group out of the bake, so the frame's partition, jambs and lintel were
        /// static geometry. <c>MapSketch</c> had already measured that piece and refused to
        /// tile with it — its opening does not survive Recast's erosion at agentRadius
        /// 0.5 m — and lays <see cref="MapKitPiece.CorridorStraight2m5"/> at every 문 cell
        /// instead (MapSketch.BuildTiles). The frame arrived by the marker path, where that
        /// workaround never reached, and put the rejected piece back on top of the
        /// replacement.</item>
        /// <item>It was in the wrong place. <see cref="Place"/> sets rotation only, so the
        /// frame's pivot landed on the cell CENTRE. Measured off the imported FBX rather
        /// than deduced — ChamberDockProbe, /tmp/probe1.log: the marker for Door_(12,10@L0)
        /// is at (31.25, 0.00, 26.25) and the frame it hung came out min (28.75, −0.15,
        /// 23.75) max (31.25, 3.15, 26.25). The pivot is the footprint's MAX corner in X
        /// and Z once the kit is imported, and 0.15 m above its own base, so
        /// <c>localPosition = Vector3.zero</c> put a 2.5 m cell a half-cell diagonally out
        /// and a floor thickness down. Both claims about the pivot are true of different
        /// spaces: the kit authors from the min corner in Blender, and the importer negates
        /// X while the stand-up rotation sends Blender +Y to Unity −Z, so the authored min
        /// corner arrives as the Unity max corner. Anything hung off a marker must be
        /// <see cref="AlignMinCorner"/>'d exactly like a tile, which is the one thing
        /// markers never were.</item>
        /// <item>It was always at yaw 0. The piece's passage runs one way and the sketch
        /// puts doors on whichever axis the gate is on, so on half the storeys its two side
        /// walls stood across the corridor.</item>
        /// </list>
        /// It is not restored aligned, because it cannot be: <c>build_doorway_frame</c> and
        /// <c>build_straight</c> pour the same floor over the same 2.5 m cell, and dress the
        /// same wall faces at x ∈ [0, 0.15] and [2.35, 2.5]. Aligned, the frame is coplanar
        /// with the corridor tile underneath it on the floor, both walls and the ceiling —
        /// z-fighting over every surface of eight cells — and the kit exports one merged
        /// mesh per piece, so the lintel cannot be kept while the duplicated shell is
        /// dropped. The doorway's clear width is now the corridor's own 2.20 m with nothing
        /// standing in it, which is the widest a 문 can be and still be at a 병목.
        /// </para>
        /// <para>
        /// <b>A door is authored OPEN.</b> <see cref="DoorState"/>'s default phase is Open
        /// (DoorStateTests.A_door_starts_open_and_lets_everything_through) and §04 locks a
        /// door at RUNTIME, so the blocker and the obstacle are laid down disabled — exactly
        /// the state <c>DoorInteractable.Apply</c> puts them in the moment
        /// <c>MatchDirector</c> binds them. Left enabled they are a shut door in the bake:
        /// the collider stands across the corridor before any match starts, and a carving
        /// obstacle cuts the surface the audit is about to measure. Shutting one enables
        /// both again, which is §04's mechanic unchanged — see
        /// DoorStateTests.Shutting_one_takes_the_time_it_says_and_then_blocks for the rule
        /// and MapTests.LockingADoor_LengthensTheWayRoundByAtLeastAnAggroRelease for what
        /// blocking it is worth.
        /// </para>
        /// </summary>
        private static void BuildDoor(MapMarkerPlacement marker, GameObject markerRoot)
        {
            var hinge = Child(markerRoot, marker.Name);
            hinge.transform.position = ToUnity(marker.Position);

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

            // The opening, not the leaf: one Door_Panel_Lockable is half of the kit's
            // double door (LEAF_W = 1.10 m), and what a shut 문 has to stop is everything
            // in the 2.20 m the corridor is clear. Height from the kit rather than typed
            // in, so a re-export that changes the leaf moves the box with it.
            var leafHeight = MapKitCatalogue.HeightMetres(MapKitPiece.DoorPanelLockable);
            var blocker = pivot.AddComponent<BoxCollider>();
            blocker.size = new Vector3(MapKitCatalogue.CorridorClearWidth, leafHeight, DoorLeafThickness);
            blocker.center = new Vector3(MapKitCatalogue.CorridorClearWidth * 0.5f, leafHeight * 0.5f, 0f);
            blocker.enabled = false;

            var obstacle = pivot.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.size = blocker.size;
            obstacle.center = blocker.center;
            obstacle.carving = true;
            obstacle.enabled = false;

            // The reach a player needs to take hold of it, and the box the interactor
            // raycasts against. A trigger so it never blocks anybody by itself — the
            // blocking is the collider above, which the component switches.
            //
            // It stays enabled while the door is open on purpose: MatchDirector.PushDoors
            // finds a door with Physics.OverlapSphere, and the blocker is disabled on an
            // open one, so this trigger is the only thing the creature can find a 문 by.
            var grab = hinge.AddComponent<BoxCollider>();
            grab.isTrigger = true;
            grab.size = new Vector3(MapKitCatalogue.CorridorClearWidth, 2.2f, 0.9f);
            grab.center = new Vector3(0f, 1.1f, 0f);

            // The whole door group out of the bake, and this is the other half of B-010.
            // §04 locks a door at runtime; the surface §06's monster paths on is baked once,
            // in this method's own editor session, and anything of a door's that reaches it
            // is a door that is shut for the whole match with nobody able to open it. Every
            // MapSketch prop already leaves the bake in Build's prop loop for that reason,
            // and KeepOutOfNavMeshBake's own remarks name the 문짝 leaf as the case that
            // severs a 2.2 m corridor twice over — it was the one thing never actually kept
            // out. applyToChildren carries it to the leaf, the only renderer left under here.
            KeepOutOfNavMeshBake(hinge);

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
        // The boundary — §01's building had none, and a race with no walls is not a race.
        // ====================================================================

        /// <summary>
        /// Seals every storey inside an invisible box: four walls round the tower's
        /// footprint, and — where a storey's shell reaches past its own floor slab — a
        /// plate laid flush with that slab to floor the band between them.
        /// <para>
        /// <b>The building has never had an outside.</b> <see cref="MapKitPiece.FloorBoundarySplit"/>
        /// is in the catalogue and no generator has ever placed one; grep the tree and it
        /// appears exactly twice, in the enum and in the file-name switch. Beyond the last
        /// drawn cell there was nothing at all — and, worse, INSIDE the last drawn cell
        /// there was a 57.5 m square of bare floor slab. <see cref="BuildFloorSlab"/>
        /// pours a tile over every cell of a zone's rectangle, and §01's radial storeys
        /// only stand corridors on 213 of a storey's 529 cells; the other 316 are poured
        /// concrete with nothing on them, 0.01 m below the corridor floors, which is a
        /// step a 0.40 m <c>stepOffset</c> does not even notice.
        /// </para>
        /// <para>
        /// <b>What was measured before this was written, seed 20260802, all eight
        /// storeys.</b> A corridor piece is open at both ends and an end with no
        /// neighbour is supposed to be closed by <see cref="MapKitPiece.DeadEndCap"/>.
        /// 152 cells on the map have exactly one corridor neighbour. 104 got a cap and 8
        /// got a barred window, so <b>40 did not</b> — five on every storey, because
        /// <c>MapSketch.BuildTiles</c> drops a cap that would stand within three cells of
        /// another one, and the cell then falls through to the greedy straight tiler,
        /// which lays a <see cref="MapKitPiece.CorridorStraight2m5"/>: a piece with walls
        /// on its long sides and <em>nothing at either end</em>. Each of those 40 is a
        /// 2.2 m doorway out of the maze. Two per storey are on the rim at Chebyshev
        /// radius 11 and one of those, (1,9), points at cell 0 — off the edge of the slab
        /// itself, which is a fall with nothing under it for the whole depth of the
        /// building, since every storey's slab has the same footprint.
        /// </para>
        /// <para>
        /// <b>This is why the shell is a box round the footprint and not a fence at the
        /// rim.</b> The 40 holes are at radius 4, 8 and 11 — the two inner ones open into
        /// the empty ring the gates are punched through, and flooding that void from an
        /// open end reaches radii 4 through 12 on every storey. A wall only at the rim
        /// would have left all of that. A box round the whole footprint catches every one
        /// of them, because there is nowhere outside it to be.
        /// </para>
        /// <para>
        /// <b>And it is why the footprint is measured off the placed objects.</b> The
        /// first version of this sized the box from the zone rectangle the sketch
        /// declares, which is the obvious reading of "sized from the zone the storey
        /// occupies" and is wrong twice over — see <see cref="Widen"/>, and the 19
        /// dead-end caps that were left standing outside it.
        /// </para>
        /// <para>
        /// <b>It does not restore the gates, and that must not be read as if it did.</b>
        /// A runner who steps out at radius 8 is still standing on open slab that runs
        /// from outside the 외곽 band to just outside the 안쪽 one, so §12-A's 4 → 2 → 1
        /// narrowing can still be walked round. The shell keeps them in the building;
        /// closing the 40 ends is a change to <c>MapSketch.BuildTiles</c> and is written
        /// up in the report beside this change. Both are needed and only one is here.
        /// </para>
        /// <para>
        /// <b>The first version of this put a plate under every storey and a lid over
        /// every storey, and both were wrong. This is what the artefact said.</b> Read out
        /// of the written scene: <c>StoreyShell_B1/Floor</c> spanned y [−0.750, −0.250] and
        /// <c>StoreyShell_B2/Lid</c> spanned y [−0.250, +0.250], so the two of them made a
        /// continuous 1.0 m slab across the whole 62.5 × 62.5 m footprint, straddling the
        /// seam. That is where a 투하구 drops a runner: 착지 is <c>FloorY(level)</c> and
        /// <c>Chute.DropPoint</c> is 착지 + 3.0 m, which for B2's 착지 at y = −3.750 is
        /// y = −0.750 — the underside of that slab, with a 1.75 m capsule standing up
        /// through all of it. Same arithmetic at all seven seams. And at the other end the
        /// lid's top face stood 0.25 m proud of the floor above it across the entire
        /// footprint, which is what a runner on B1..B7 was actually standing on: measured,
        /// the reach audit's tallest climb went 0.045 → 0.237 m and its headroom probe
        /// reported a standing place at y −11.00, which is <c>StoreyShell_B5/Lid</c>, not
        /// B4's floor at −11.25.
        /// </para>
        /// <para>
        /// <b>So the shell has no plate under the map at all now.</b> The map already has a
        /// floor — <see cref="BuildFloorSlab"/> pours one over every cell of a zone's
        /// rectangle — and the only place the shell reaches past it is the band between the
        /// slab's edge and the wall, which exists because <see cref="Widen"/> sizes the box
        /// round dead-end caps that stand at cell 0, outside the zone. That band is the
        /// only thing left to floor, and it is floored with its top face on the slab's own
        /// top plane, <c>FloorY(L) − FloorSlabDepth</c>. Three things follow, and each was
        /// a defect before: nothing stands proud of anything, so the reach audit's climb is
        /// the map's own again; no boundary collider is under the map, so a 발소리 raycast
        /// and a headroom probe find the floor the player can see; and the 3.0 m of air
        /// under every 투하구 is empty of boundary — measured at generation by
        /// <see cref="VerifyChutesDropIntoOpenAir"/>, not asserted here.
        /// </para>
        /// <para>
        /// <b>The tower's own two ends keep plates, and they are genuinely one-sided.</b>
        /// A lid over B1 closes the top of the wall curtain; nothing in the building can
        /// reach it (<c>GameConstants.JumpApexMetres</c> 0.35 m is pinned below the 0.40 m
        /// step offset, so a jump reaches a strict subset of what walking reaches) and it
        /// is there so "sealed" means a closed surface rather than an argument. A plate
        /// under B8 hangs beneath that storey's slab — below it is outside the building,
        /// and a hairline between two poured tiles there is a fall with nothing under it
        /// for thirty metres. Neither is above a floor, so neither can be stood on.
        /// </para>
        /// <para>
        /// <b>One footprint for the whole tower, and that is a fix too.</b> Measured per
        /// storey, B4, B6 and B7 came out 60.0 × 62.5 m against everybody else's 62.5 ×
        /// 62.5 — their <c>Wall_XMin</c> stood at x [2.000, 2.500] — because those three
        /// storeys happen to have no <see cref="MapKitPiece.DeadEndCap"/> at cell x = 0,
        /// while the other five do. Nothing was outside, and the log still said "Each
        /// inside is 62.5 x 62.5 m" for all eight. §01's building is ONE column; a wall
        /// that steps in by a cell on three floors is a building nobody drew, it leaves the
        /// band above it unfloored for two storeys at a stretch, and it makes the
        /// containment check below compare a storey against a box that is not its own. The
        /// footprint is still MEASURED, storey by storey, off the objects that were placed
        /// — it is then unioned, and every storey is sealed in the union.
        /// </para>
        /// <para>
        /// <b>It does not close the 40 open ends, and the band is still walkable.</b> A
        /// runner who finds one of them at the rim steps out onto the band and can follow
        /// it along the side of the building. That was true when the band was a ledge 0.25 m
        /// down and it is true now that it is flush; what changed is only that stepping
        /// back is no longer a climb. The band is inside the walls, so it is not an escape —
        /// but it is a corridor §12 never drew, and the fix for it is
        /// <c>MapSketch.BuildTiles</c>, not this file.
        /// </para>
        /// <para>
        /// <b>One consequence that is real.</b> A creature's 1.4 m
        /// <c>MatchDirector.PushDoors</c> sphere can now find a band plate or a wall: one
        /// more collider in a 32-slot buffer that a corridor fills to about eight, and it
        /// is filtered out by the <c>GetComponentInParent&lt;DoorInteractable&gt;</c> the
        /// loop already does. §09's <c>GhostRattleTarget</c> discards it outright — it has
        /// no <c>Interactable</c> and its extent is far over the rattle range, which is the
        /// scenery test that class already carries.
        /// </para>
        /// </summary>
        private static void BuildStoreyShells(GameObject root, MapSketchResult map, GameObject[] zoneRoots)
        {
            // Both walls of ShellUnderfloorMetres' window, re-derived from the kit rather
            // than restated, so a re-export that thickens a floor tile or raises a
            // corridor says so here instead of shipping a shell that clips one of them.
            var slabUnderside = FloorSlabDepth
                + MapKitCatalogue.HeightMetres(MapKitPiece.FloorTileConcrete);
            var roofHeadroom = MapKitCatalogue.StoreyMetres
                - MapKitCatalogue.HeightMetres(MapKitPiece.CorridorStraight2m5);
            if (ShellUnderfloorMetres <= slabUnderside || ShellUnderfloorMetres >= roofHeadroom)
            {
                Debug.LogError("[SceneGen] ShellUnderfloorMetres is " + ShellUnderfloorMetres
                    + " m and the kit now demands more than " + slabUnderside.ToString("0.000")
                    + " m and less than " + roofHeadroom.ToString("0.000")
                    + " m. Below the floor the shell would cut the zone's own slab; above it the "
                    + "shell would cut the corridor roofs, because the shells tile the tower with "
                    + "no gap and what the bottom takes the top loses.");
            }

            var boundary = Child(root, BoundaryRootName);
            var step = Mathf.RoundToInt(MapKitCatalogue.FloorTileMetres / MapKitCatalogue.GridMetres);
            var interiors = new List<Bounds>();
            var built = new List<PlacedPlate>();

            // Per STOREY, not per zone, and the difference is a latent bug rather than a
            // preference. §12 asks for 4~6 zones and says nothing about how they stack;
            // §01's descent happens to put exactly one on each of its eight floors and so
            // does FirstMapSketch, which is why a loop over ZoneRects looks correct. Give
            // one storey two zones and that loop asks Child() for "StoreyShell_B3" twice,
            // gets the same object back both times, and moves the first zone's plates onto
            // the second zone's footprint — leaving half the storey open with a shell in
            // the scene saying it is sealed. Grouping by level makes the name unique by
            // construction and makes the box the union of what the storey holds.
            var levels = new List<int>();
            for (var i = 0; i < map.ZoneRects.Length; i++)
            {
                if (!levels.Contains(map.ZoneRects[i].Level))
                {
                    levels.Add(map.ZoneRects[i].Level);
                }
            }

            levels.Sort();

            // ── Pass 1: measure ──────────────────────────────────────────────
            // Every number below is read off the objects this run placed. Nothing here
            // decides a footprint; it finds one, and pass 2 builds to what was found.
            var ownFootprint = new Dictionary<int, Plan>();
            var floored = new Dictionary<int, Plan>();
            var slabBottom = new Dictionary<int, float>();

            foreach (var level in levels)
            {
                var box = Plan.Empty;
                var slab = Plan.Empty;
                var pouredArea = 0f;
                var underside = float.PositiveInfinity;

                for (var i = 0; i < map.ZoneRects.Length; i++)
                {
                    var rect = map.ZoneRects[i];
                    if (rect.Level != level)
                    {
                        continue;
                    }

                    var zoneRoot = i < zoneRoots.Length ? zoneRoots[i] : null;
                    Widen(rect, zoneRoot, step, ref box);

                    var poured = SlabRectangle(rect, step);
                    slab.Encapsulate(poured);
                    pouredArea += poured.Area;
                    underside = Mathf.Min(underside, SlabUndersideOf(zoneRoot, rect, slabUnderside));
                }

                // The band plates are the footprint MINUS this rectangle, so a rectangle
                // that claims ground no tile was poured on is a hole in the floor that
                // nothing will cover. One zone per storey makes the two areas equal by
                // construction; two zones that do not tile their own bounding box do not,
                // and this is the only place that would ever know.
                if (slab.Exists && Mathf.Abs(pouredArea - slab.Area) > 0.01f)
                {
                    Debug.LogError("[SceneGen] B" + (level + 1) + "'s zones pour "
                        + pouredArea.ToString("0.0") + " m² of floor but their bounding rectangle is "
                        + slab.Area.ToString("0.0") + " m². The boundary floors only the band OUTSIDE "
                        + "that rectangle, because the storey's own slab floors the inside — so the "
                        + (slab.Area - pouredArea).ToString("0.0") + " m² difference is unfloored ground "
                        + "inside the shell, which is a fall to the bottom of the tower.");
                }

                ownFootprint[level] = box;
                floored[level] = slab;
                slabBottom[level] = underside;
            }

            // One column, one footprint. Measured per storey and then unioned: B4, B6 and
            // B7 measured 60.0 × 62.5 m against everybody else's 62.5 × 62.5 because those
            // three have no DeadEndCap at cell x = 0, and sealing each storey in its own
            // measurement is what let the containment check compare a storey against a box
            // that was not its own. Any storey whose own measurement is smaller is named,
            // so widening it is a fact in the log rather than a silent generosity.
            var inside = Plan.Empty;
            foreach (var level in levels)
            {
                inside.Encapsulate(ownFootprint[level]);
            }

            var widened = new List<string>();
            foreach (var level in levels)
            {
                if (!Plan.Same(ownFootprint[level], inside, ShellSeamToleranceMetres))
                {
                    widened.Add("B" + (level + 1) + " " + ownFootprint[level].Describe());
                }
            }

            // ── Pass 2: build ────────────────────────────────────────────────
            foreach (var level in levels)
            {
                // One storey pitch exactly, so consecutive shells meet face to face with no
                // gap and no overlap: this storey's walls stop on the plane the storey
                // above starts its own, all the way down the tower.
                var bottom = MapKitCatalogue.FloorY(level) - ShellUnderfloorMetres;
                var top = bottom + MapKitCatalogue.StoreyMetres;
                var interior = new Bounds(
                    new Vector3((inside.MinX + inside.MaxX) * 0.5f, (bottom + top) * 0.5f,
                        (inside.MinZ + inside.MaxZ) * 0.5f),
                    new Vector3(inside.Width, top - bottom, inside.Depth));
                interiors.Add(interior);

                // One object per storey rather than one for the building, so a storey that
                // is ever moved off the tower's axis takes its own box with it — the same
                // reason DescentMap.SeedCreature measures from the floor's own recorded
                // middle instead of from the Centre constant.
                var shell = Child(boundary, "StoreyShell_B" + (level + 1));
                var w = ShellWallMetres;
                var mid = interior.center;
                var height = interior.size.y;

                // Each wall grows OUTWARD from the interior face, so the inside of the box
                // is exactly the storey and no wall stands in it. The ±X walls run the full
                // depth plus a thickness at each end, which is what closes the four
                // vertical edges — a 0.30 m capsule needs only a 0.6 m gap, and a corner is
                // where a box assembled from independent faces leaves one.
                Plate(built, shell, "Wall_XMin",
                    new Vector3(inside.MinX - (w * 0.5f), mid.y, mid.z),
                    new Vector3(w, height, interior.size.z + (2f * w)));
                Plate(built, shell, "Wall_XMax",
                    new Vector3(inside.MaxX + (w * 0.5f), mid.y, mid.z),
                    new Vector3(w, height, interior.size.z + (2f * w)));
                Plate(built, shell, "Wall_ZMin",
                    new Vector3(mid.x, mid.y, inside.MinZ - (w * 0.5f)),
                    new Vector3(interior.size.x, height, w));
                Plate(built, shell, "Wall_ZMax",
                    new Vector3(mid.x, mid.y, inside.MaxZ + (w * 0.5f)),
                    new Vector3(interior.size.x, height, w));

                BuildBandPlates(built, shell, inside, floored[level],
                    MapKitCatalogue.FloorY(level) - FloorSlabDepth);

                // The tower's two ends, and only its ends. Above B1 and below B8 there is
                // no storey to be sealed by, so these two are the closed surface; every
                // other horizontal plane in the building is the map's own floor.
                if (level == levels[0])
                {
                    Plate(built, shell, "Lid",
                        new Vector3(mid.x, top + (ShellPlateMetres * 0.5f), mid.z),
                        new Vector3(interior.size.x + (2f * w), ShellPlateMetres,
                            interior.size.z + (2f * w)));
                }

                if (level == levels[levels.Count - 1])
                {
                    // Hung under the slab this storey actually poured, measured, so it is
                    // below every floor a runner can stand on and cannot be one.
                    var hang = slabBottom[level];
                    Plate(built, shell, "Floor",
                        new Vector3(mid.x, hang - (ShellPlateMetres * 0.5f), mid.z),
                        new Vector3(interior.size.x + (2f * w), ShellPlateMetres,
                            interior.size.z + (2f * w)));
                }
            }

            // The second of the two reasons none of this reaches the NavMesh. The first is
            // that a plate has no MeshFilter and BakeNavMesh collects
            // NavMeshCollectGeometry.RenderMeshes, so CollectObjects.All finds nothing here
            // to collect at all. This is belt and braces and it is also what writes
            // m_IgnoreFromBuild: 1 into the scene, where it can be counted without Unity.
            KeepOutOfNavMeshBake(boundary);

            VerifyChutesDropIntoOpenAir(map, built);
            VerifyNothingIsOutsideTheShells(root, boundary, interiors, built, widened);
        }

        /// <summary>
        /// Floors the band between a storey's own slab and its shell wall — and floors
        /// nothing else.
        /// <para>
        /// Four strips rather than one plate, and the difference is the whole point: a
        /// plate under the storey would lie under the map, and the map already has a floor.
        /// Whatever the boundary puts there competes with it — 0.164 m below and a runner
        /// who steps off the slab has a step to climb back (measured: the reach audit's
        /// tallest climb was 0.045 m and became 0.237 m); flush with it and two coplanar
        /// surfaces answer the same downward raycast, which is how §04's 발소리 lookup and
        /// the reach audit's headroom probe come to name a boundary plate as the floor.
        /// Outside the slab there is no competition, because there is nothing there.
        /// </para>
        /// <para>
        /// <paramref name="topY"/> is the slab's own top plane,
        /// <c>FloorY(L) − FloorSlabDepth</c>, so the band and the slab are the same height
        /// and stepping between them is not a step. The strips grow DOWN from it by
        /// <see cref="ShellPlateMetres"/> and outward into the wall, so there is no hairline
        /// at the join for a sweep to find.
        /// </para>
        /// <para>
        /// The ±X strips run the full depth and the ±Z strips only the slab's width, the
        /// same overlap rule the walls use, so the four corners are covered exactly once
        /// and no seam runs corner to corner. A strip with no width is not built: on this
        /// map the slab reaches the footprint's own +X and +Z edges, so six of the eight
        /// storeys build two strips and none builds four.
        /// </para>
        /// </summary>
        private static void BuildBandPlates(
            List<PlacedPlate> built, GameObject shell, Plan inside, Plan slab, float topY)
        {
            if (!slab.Exists)
            {
                return;
            }

            var w = ShellWallMetres;
            var t = ShellPlateMetres;
            var centreY = topY - (t * 0.5f);

            Strip(built, shell, "Band_XMin", inside.MinX - w, slab.MinX,
                inside.MinZ - w, inside.MaxZ + w, centreY, t);
            Strip(built, shell, "Band_XMax", slab.MaxX, inside.MaxX + w,
                inside.MinZ - w, inside.MaxZ + w, centreY, t);
            Strip(built, shell, "Band_ZMin", slab.MinX, slab.MaxX,
                inside.MinZ - w, slab.MinZ, centreY, t);
            Strip(built, shell, "Band_ZMax", slab.MinX, slab.MaxX,
                slab.MaxZ, inside.MaxZ + w, centreY, t);
        }

        /// <summary>One band strip, or nothing at all when the slab already reaches the wall.</summary>
        private static void Strip(
            List<PlacedPlate> built, GameObject shell, string name,
            float minX, float maxX, float minZ, float maxZ, float centreY, float thickness)
        {
            // A strip that is only the wall's own overhang wide IS the wall, not a floor.
            // Every strip is built with a wall thickness of overhang on the side it meets
            // the wall, so this is the same test on either axis: what is left over after
            // the overhang has to be a band with real width in it.
            if (maxX - minX <= ShellWallMetres + ShellSeamToleranceMetres
                || maxZ - minZ <= ShellWallMetres + ShellSeamToleranceMetres)
            {
                return;
            }

            Plate(built, shell, name,
                new Vector3((minX + maxX) * 0.5f, centreY, (minZ + maxZ) * 0.5f),
                new Vector3(maxX - minX, thickness, maxZ - minZ));
        }

        /// <summary>
        /// The underside of the floor this zone actually poured, metres.
        /// <para>
        /// Measured off the placed slab rather than computed from the piece table, for the
        /// same reason <see cref="Widen"/> reads the drawn bounds: the table says a floor
        /// tile is 0.154 m and the FBX is the artefact. Falls back to the table, saying so,
        /// only when the zone has no floor to measure — which is itself worth knowing.
        /// </para>
        /// </summary>
        private static float SlabUndersideOf(GameObject zoneRoot, MapZoneRect rect, float fromTheTable)
        {
            var fallback = MapKitCatalogue.FloorY(rect.Level) - fromTheTable;
            if (zoneRoot == null)
            {
                return fallback;
            }

            var floorRoot = zoneRoot.transform.Find("Floor");
            if (floorRoot == null || !TryBounds(floorRoot.gameObject, out var poured))
            {
                Debug.LogWarning("[SceneGen] B" + (rect.Level + 1) + " has no measurable floor slab under "
                    + zoneRoot.name + "/Floor, so the plate under the tower is placed from the piece "
                    + "table (" + fallback.ToString("0.000") + " m) instead of from what was poured.");
                return fallback;
            }

            return poured.min.y;
        }

        /// <summary>
        /// Grows a storey's footprint so it holds one of that storey's zones.
        /// <para>
        /// Three terms, and the third is the one that was missing when this was first
        /// written and measured. The zone rectangle the sketch declares is not even the
        /// edge of that zone's own floor: a 5 m tile is two cells and
        /// <see cref="BuildFloorSlab"/> walks the rectangle in those steps, so a 23-cell
        /// zone is floored out to cell 24. And a two-cell <see cref="MapKitPiece.DeadEndCap"/>
        /// is placed at the lower of the blind cell and the cell BEYOND it, so a blind
        /// cell on the zone's first row puts a cap at cell 0, outside the zone entirely.
        /// Measured on §01's descent, seed 20260802: 19 caps stand at x = 0 or z = 0, each
        /// putting 2.50 m of dressed geometry past a wall drawn on the rectangle. A box
        /// sized from the declaration would have left nineteen ledges outside the
        /// building — the bug being fixed, inside the fix.
        /// </para>
        /// <para>
        /// So the third term is read off the objects that were actually placed rather than
        /// derived from the piece table, which is the same choice this class makes when it
        /// docks a piece by its world bounds instead of working out where a rotation moves
        /// an FBX origin. Whatever the kit turns out to be, the box is round it.
        /// </para>
        /// </summary>
        private static void Widen(MapZoneRect rect, GameObject zoneRoot, int step, ref Plan box)
        {
            // The floor this zone pours, from BuildFloorSlab's own arithmetic, so the slab
            // and the wall that has to contain it cannot drift apart.
            box.Encapsulate(SlabRectangle(rect, step));

            if (zoneRoot == null || !TryBounds(zoneRoot, out var drawn))
            {
                return;
            }

            // Snapped outward onto the kit's own 2.5 m grid, with a centimetre of slack
            // first so a wall face that is one float bit short of a grid line does not push
            // the shell out by a whole cell. The snap is not tidiness: the band this leaves
            // outside the slab is floored, so a wall left a few millimetres proud of the
            // geometry is a few millimetres of standing room outside the maze with floor
            // under it — and a band narrower than a wall's own thickness is not built at
            // all, which is the other half of the same guard.
            box.Encapsulate(new Plan
            {
                MinX = SnapDown(drawn.min.x),
                MinZ = SnapDown(drawn.min.z),
                MaxX = SnapUp(drawn.max.x),
                MaxZ = SnapUp(drawn.max.z),
            });
        }

        /// <summary>
        /// The rectangle <see cref="BuildFloorSlab"/> actually pours for one zone, metres.
        /// <para>
        /// Its own loop, not a restatement of it: a 5 m tile is two cells and the loop steps
        /// in twos from the zone's first cell, so a 23-cell zone is floored out to cell 24
        /// and the slab is wider than the rectangle §12 declared. Two callers need this and
        /// they must not disagree — <see cref="Widen"/> puts the wall outside it, and
        /// <see cref="BuildBandPlates"/> floors only what is between them.
        /// </para>
        /// </summary>
        private static Plan SlabRectangle(MapZoneRect rect, int step)
        {
            var lastTileX = rect.CellX + (((rect.CellsX - 1) / step) * step);
            var lastTileZ = rect.CellZ + (((rect.CellsZ - 1) / step) * step);
            return new Plan
            {
                MinX = rect.CellX * MapKitCatalogue.GridMetres,
                MinZ = rect.CellZ * MapKitCatalogue.GridMetres,
                MaxX = (lastTileX + step) * MapKitCatalogue.GridMetres,
                MaxZ = (lastTileZ + step) * MapKitCatalogue.GridMetres,
            };
        }

        /// <summary>Grid line at or below a coordinate, with <see cref="ShellSeamToleranceMetres"/> slack.</summary>
        private static float SnapDown(float metres) =>
            Mathf.Floor((metres + ShellSeamToleranceMetres) / MapKitCatalogue.GridMetres)
            * MapKitCatalogue.GridMetres;

        /// <summary>Grid line at or above a coordinate, with <see cref="ShellSeamToleranceMetres"/> slack.</summary>
        private static float SnapUp(float metres) =>
            Mathf.Ceil((metres - ShellSeamToleranceMetres) / MapKitCatalogue.GridMetres)
            * MapKitCatalogue.GridMetres;

        /// <summary>
        /// One face of a shell: an empty object carrying a single <see cref="BoxCollider"/>.
        /// <para>
        /// No <see cref="MeshFilter"/> and no <see cref="Renderer"/> — not a disabled one,
        /// none at all. That is what makes it invisible, and it is also the load-bearing
        /// half of keeping it out of the bake, because <see cref="BakeNavMesh"/> collects
        /// render meshes. A face with a disabled renderer would still be a mesh in the
        /// scene for anything that walks <c>GetComponentsInChildren</c> with
        /// <c>includeInactive</c>, which is how <see cref="FootprintOf"/> and the bake's
        /// own triangle count read the map.
        /// </para>
        /// <para>
        /// Named in ASCII and one object per face rather than six colliders on one, so
        /// that when <c>PlayerTraversal</c> refuses a move it can print
        /// <c>StoreyShell_B3/Wall_XMin</c> and the reader knows which face and which
        /// storey. "Something is in the way" is not a bug report.
        /// </para>
        /// </summary>
        private static void Plate(
            List<PlacedPlate> built, GameObject parent, string name, Vector3 centre, Vector3 size)
        {
            var go = Child(parent, name);
            go.transform.SetPositionAndRotation(centre, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            var box = go.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = go.AddComponent<BoxCollider>();
            }

            box.center = Vector3.zero;
            box.size = size;
            box.isTrigger = false;

            // Kept as a plain box rather than re-read off the collider later, so the checks
            // below measure the same numbers this method wrote and a later reparenting
            // cannot quietly move what they measure.
            built.Add(new PlacedPlate(parent.name + "/" + name, new Bounds(centre, size)));
        }

        /// <summary>A boundary box this run wrote, and the name a failure should print.</summary>
        private readonly struct PlacedPlate
        {
            public PlacedPlate(string name, Bounds box)
            {
                Name = name;
                Box = box;
            }

            /// <summary>Scene path relative to the boundary root, e.g. <c>StoreyShell_B3/Wall_XMin</c>.</summary>
            public string Name { get; }

            /// <summary>World bounds of the collider.</summary>
            public Bounds Box { get; }
        }

        /// <summary>
        /// An axis-aligned rectangle in plan, metres.
        /// <para>
        /// A rectangle rather than a <see cref="Bounds"/> because every question the shells
        /// ask is horizontal and a <c>Bounds</c> would carry a Y that means nothing here —
        /// which is exactly how the first version came to compare a storey's footprint
        /// against the tower's height and call it containment.
        /// </para>
        /// </summary>
        private struct Plan
        {
            public float MinX;
            public float MinZ;
            public float MaxX;
            public float MaxZ;

            /// <summary>The rectangle that grows to hold the first thing put into it.</summary>
            public static Plan Empty => new Plan
            {
                MinX = float.PositiveInfinity,
                MinZ = float.PositiveInfinity,
                MaxX = float.NegativeInfinity,
                MaxZ = float.NegativeInfinity,
            };

            public bool Exists => MaxX > MinX && MaxZ > MinZ;

            public float Width => MaxX - MinX;

            public float Depth => MaxZ - MinZ;

            public float Area => Exists ? Width * Depth : 0f;

            /// <summary>True when two rectangles agree on all four edges within <paramref name="slack"/>.</summary>
            public static bool Same(Plan a, Plan b, float slack) =>
                Mathf.Abs(a.MinX - b.MinX) <= slack && Mathf.Abs(a.MinZ - b.MinZ) <= slack
                && Mathf.Abs(a.MaxX - b.MaxX) <= slack && Mathf.Abs(a.MaxZ - b.MaxZ) <= slack;

            public void Encapsulate(Plan other)
            {
                MinX = Mathf.Min(MinX, other.MinX);
                MinZ = Mathf.Min(MinZ, other.MinZ);
                MaxX = Mathf.Max(MaxX, other.MaxX);
                MaxZ = Mathf.Max(MaxZ, other.MaxZ);
            }

            public string Describe() =>
                "x [" + MinX.ToString("0.0") + ", " + MaxX.ToString("0.0")
                + "] z [" + MinZ.ToString("0.0") + ", " + MaxZ.ToString("0.0") + "]";
        }

        /// <summary>
        /// Proves, on the geometry this run just wrote, that no boundary collider stands in
        /// the air a 투하구 drops a runner through.
        /// <para>
        /// <b>This is the check that was missing, and its absence shipped a map where every
        /// drop landed inside a plate.</b> The reasoning in the first version was that a
        /// 투하구 is not a hole — <c>Chute.Swallows</c> is a plan test and
        /// <c>Chute.DropPoint</c> is a reposition — and that reasoning is correct and
        /// entirely beside the point: the runner is repositioned to 착지 + 3.0 m and the
        /// boundary had put a 1.0 m slab exactly there. A sentence in a doc comment cannot
        /// find that. An overlap test can, and it costs nothing at generation time.
        /// </para>
        /// <para>
        /// The body is the capsule's bounding box, which is conservative in the right
        /// direction: it claims the corners a capsule does not fill, so a plate it calls
        /// clear is clear for the capsule too. Its height and radius are
        /// <see cref="PlayerTraversal.PlayerBody"/>'s documented numbers, deliberately not
        /// measured off a rig here — measuring means building
        /// <c>PlayerFeelHarnessMenu.BuildRig()</c> into the scene that is about to be saved.
        /// The reach audit builds the real rig and would say so if the two ever parted.
        /// </para>
        /// <para>
        /// It measures the boundary only. The map's own floor IS inside this capsule and
        /// always has been — a 3.0 m drop plus a 1.75 m body is 4.75 m against a 3.75 m
        /// storey — and calling that a defect would be measuring the building rather than
        /// the shell. What the shell owes is that it adds nothing.
        /// </para>
        /// </summary>
        private static void VerifyChutesDropIntoOpenAir(MapSketchResult map, List<PlacedPlate> built)
        {
            var body = new PlayerTraversal.PlayerBody();
            var landings = 0;
            var blocked = 0;
            var tightest = float.PositiveInfinity;
            var tightestWhere = "nothing";
            var worst = string.Empty;

            foreach (var marker in map.Markers)
            {
                if (marker.Kind != MapMarkerKind.ChuteLanding)
                {
                    continue;
                }

                landings++;

                // Feet, not centre. CharacterController.center is (0, height/2, 0) on every
                // rig this project builds, so the transform a chute assigns is the sole of
                // the runner's foot and the body stands up from it.
                var feet = ToUnity(marker.Position) + (Vector3.up * ChuteDropHeightMetres);
                var capsule = new Bounds(
                    feet + new Vector3(0f, body.Height * 0.5f, 0f),
                    new Vector3(body.Radius * 2f, body.Height, body.Radius * 2f));

                var nearest = float.PositiveInfinity;
                var nearestName = "nothing";
                foreach (var plate in built)
                {
                    var gap = Separation(capsule, plate.Box);
                    if (gap < nearest)
                    {
                        nearest = gap;
                        nearestName = plate.Name;
                    }
                }

                if (nearest < 0f)
                {
                    blocked++;
                    if (worst.Length == 0)
                    {
                        worst = marker.Name + " at " + feet.ToString("0.00") + " is inside "
                            + nearestName + " by " + (-nearest).ToString("0.000") + " m";
                    }
                }

                if (nearest < tightest)
                {
                    tightest = nearest;
                    tightestWhere = marker.Name + " → " + nearestName;
                }
            }

            if (landings == 0)
            {
                return;
            }

            if (blocked > 0)
            {
                Debug.LogError("[SceneGen] " + blocked + " of " + landings
                    + " 투하구 drop a runner INTO the boundary — " + worst
                    + ". Chute.DropPoint is 착지 + " + ChuteDropHeightMetres.ToString("0.0")
                    + " m and the body stands " + body.Height.ToString("0.00")
                    + " m up from there; a runner teleported inside a collider is pushed out the "
                    + "short way, which at a storey seam is UP, back onto the floor they just left, "
                    + "after MatchDirector has already recorded the descent. §01's only way down is "
                    + "this drop.");
                return;
            }

            Debug.Log("[SceneGen] 투하구: " + landings + " landings, all in open air. Tightest boundary "
                + "clearance " + tightest.ToString("0.000") + " m (" + tightestWhere
                + "), measured on the capsule's own box — " + body.Radius.ToString("0.00")
                + " m radius by " + body.Height.ToString("0.00") + " m, " + body.Source
                + " — standing at 착지 + " + ChuteDropHeightMetres.ToString("0.0")
                + " m. This says nothing about the map's own floor, which the capsule is "
                + "inside at every drop and always has been: 3.0 m + 1.75 m is 4.75 m against "
                + "a 3.75 m storey.");
        }

        /// <summary>
        /// Metres between two boxes: negative when they overlap, and then the depth of the
        /// shallowest overlap. The largest per-axis gap is the answer, because two boxes are
        /// apart the moment one axis separates them.
        /// </summary>
        private static float Separation(Bounds a, Bounds b)
        {
            var x = Mathf.Max(b.min.x - a.max.x, a.min.x - b.max.x);
            var y = Mathf.Max(b.min.y - a.max.y, a.min.y - b.max.y);
            var z = Mathf.Max(b.min.z - a.max.z, a.min.z - b.max.z);
            return Mathf.Max(x, Mathf.Max(y, z));
        }

        /// <summary>
        /// Measures the shells against what the generator actually wrote, and says the
        /// numbers out loud.
        /// <para>
        /// Building a box and asserting in a comment that everything is inside it is the
        /// failure this repo keeps finding — a green number nobody measured. So the check
        /// is the artefact's: every <see cref="Renderer"/> under the map root has to sit
        /// inside some storey's shell in plan, and inside the tower's total height.
        /// </para>
        /// <para>
        /// Plan and height are checked separately on purpose. Horizontal containment is
        /// the property this whole change exists for and it admits no exception: geometry
        /// outside a wall is geometry a runner could be standing on outside the building.
        /// Vertical containment is deliberately checked against the whole tower rather
        /// than one storey, because crossing a storey line is legal and normal — a
        /// storey's ceiling caps are poured at the plane of the floor above and therefore
        /// live in the shell above, a 계단 climbs a whole storey by definition, and §12's
        /// 6.3 m hall is taller than the 3.75 m it stands in.
        /// </para>
        /// <para>
        /// <b>Against the shells that cover the object's own height, not against the best
        /// of them, and that was a hole big enough to hide the defect it was written to
        /// catch.</b> The first version took <c>Mathf.Max</c> over every interior — "the
        /// best any shell does" — which is only sound while the shells share a footprint,
        /// and they did not: B4, B6 and B7 came out 60.0 m wide against 62.5 for the rest,
        /// so a renderer on B4 at x = 1.0 was checked against B1's wider box and passed.
        /// The rule here is the physical one. At any height the enclosure is whichever
        /// shell covers that height, so a piece has to be inside every shell its own
        /// height reaches into — a ceiling cap poured at the plane of the floor above is
        /// therefore judged by the shell above, which is where it is. A piece that reaches
        /// into none of them is out of the tower and is judged by the nearest.
        /// </para>
        /// </summary>
        private static void VerifyNothingIsOutsideTheShells(
            GameObject root, GameObject boundary, List<Bounds> interiors,
            List<PlacedPlate> built, List<string> widened)
        {
            if (interiors.Count == 0)
            {
                Debug.LogError("[SceneGen] No storey shell was built. A map with no boundary can be walked "
                    + "out of, and §02's race to the middle is then a straight line across the footprint.");
                return;
            }

            var visible = boundary.GetComponentsInChildren<Renderer>(true).Length;
            if (visible > 0)
            {
                Debug.LogError("[SceneGen] " + visible + " renderer(s) under Map/" + BoundaryRootName
                    + ". The boundary must be invisible — a player must never see a grey box — and a "
                    + "renderer here is also geometry the NavMesh bake collects, which would move every "
                    + "audit number in the report below.");
            }

            var towerBottom = float.PositiveInfinity;
            var towerTop = float.NegativeInfinity;
            foreach (var interior in interiors)
            {
                towerBottom = Mathf.Min(towerBottom, interior.min.y);
                towerTop = Mathf.Max(towerTop, interior.max.y);
            }

            var counted = 0;
            var loose = 0;
            var overhead = 0;
            var worstOvershoot = 0f;
            var worstName = "nothing";
            var tightest = float.PositiveInfinity;
            var tightestName = "nothing";
            var overheadName = "nothing";
            var overheadReach = 0f;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.transform.IsChildOf(boundary.transform))
                {
                    continue;
                }

                counted++;
                var b = renderer.bounds;

                // Every shell this object's own height reaches into, and the WORST of
                // them. A storey it does not reach cannot contain it and cannot excuse it.
                var margin = float.PositiveInfinity;
                var judged = false;
                foreach (var interior in interiors)
                {
                    if (b.max.y < interior.min.y - ShellSeamToleranceMetres
                        || b.min.y > interior.max.y + ShellSeamToleranceMetres)
                    {
                        continue;
                    }

                    judged = true;
                    margin = Mathf.Min(margin, PlanMargin(b, interior));
                }

                if (!judged)
                {
                    // Above the lid or under the floor: still measured, against the shell
                    // it is nearest to, so an escape at the top of the tower is not filed
                    // as "no shell applies" and dropped.
                    var nearest = float.PositiveInfinity;
                    foreach (var interior in interiors)
                    {
                        var away = Mathf.Max(interior.min.y - b.max.y, b.min.y - interior.max.y);
                        if (away < nearest)
                        {
                            nearest = away;
                            margin = PlanMargin(b, interior);
                        }
                    }
                }

                var name = renderer.transform.parent == null
                    ? renderer.name
                    : renderer.transform.parent.name + "/" + renderer.name;

                if (margin < -ShellSeamToleranceMetres)
                {
                    loose++;
                    if (-margin > worstOvershoot)
                    {
                        worstOvershoot = -margin;
                        worstName = name;
                    }
                }
                else if (margin < tightest)
                {
                    tightest = margin;
                    tightestName = name;
                }

                var above = b.max.y - towerTop;
                var below = towerBottom - b.min.y;
                var escaped = Mathf.Max(above, below);
                if (escaped > ShellSeamToleranceMetres)
                {
                    overhead++;

                    // Named, because a bare count is a fact nobody can act on. This has
                    // read 1 since the shells were first built and nothing said which one.
                    if (escaped > overheadReach)
                    {
                        overheadReach = escaped;
                        overheadName = name + (above > below
                            ? " reaches y " + b.max.y.ToString("0.00")
                            : " reaches down to y " + b.min.y.ToString("0.00"));
                    }
                }
            }

            if (loose > 0)
            {
                Debug.LogError("[SceneGen] " + loose + " of " + counted
                    + " renderers stand outside every storey shell in plan — worst "
                    + worstOvershoot.ToString("0.00") + " m at " + worstName
                    + ". Anything outside the boundary is somewhere a runner can be outside the "
                    + "building, which is the defect the shells exist for. A shell is sized from its "
                    + "zone rectangle, its floor slab and everything drawn under Zone_*, so what lands "
                    + "here is what none of those three see: a prop, a 문 leaf under Markers, or a tile "
                    + "the sketch left in Shared because it straddles two zones.");
            }

            if (overhead > 0)
            {
                Debug.LogError("[SceneGen] " + overhead + " of " + counted + " renderers reach above y "
                    + towerTop.ToString("0.00") + " or below y " + towerBottom.ToString("0.00")
                    + " — outside the tower's lid or its floor, furthest " + overheadName
                    + ". Crossing ONE storey line is legal (a ceiling cap is poured at the plane of the "
                    + "floor above it, a 계단 climbs a whole storey); leaving the building at the top or "
                    + "the bottom is not.");
            }

            var narrower = widened.Count == 0
                ? "Every storey measured the same footprint."
                : widened.Count + " storey(s) measured smaller and were widened to it: "
                    + string.Join(", ", widened) + ".";

            Debug.Log("[SceneGen] 경계: " + interiors.Count + " storey shells / " + built.Count
                + " box colliders / " + visible + " renderers under Map/" + BoundaryRootName
                + ". Each inside is " + interiors[0].size.x.ToString("0.0") + " x "
                + interiors[0].size.z.ToString("0.0") + " m by " + interiors[0].size.y.ToString("0.00")
                + " m tall; together they seal y " + towerBottom.ToString("0.00") + " .. "
                + towerTop.ToString("0.00") + " with no gap between storeys. " + narrower
                + " No plate lies under the map — the storey's own slab is its floor, and the boundary "
                + "floors only the band outside it, flush. Out of the NavMesh bake twice over — no "
                + "MeshFilter for CollectObjects.All + NavMeshCollectGeometry.RenderMeshes to find, and "
                + "NavMeshModifier.ignoreFromBuild with applyToChildren on the root, which is the "
                + "m_IgnoreFromBuild: 1 the scene carries. Checked " + counted + " renderers against the "
                + "shells covering their own height: " + loose + " outside a wall, " + overhead
                + " above the lid or under the floor, tightest clearance to a wall "
                + tightest.ToString("0.000") + " m at " + tightestName + ".");
        }

        /// <summary>
        /// How far inside a shell's four walls a box sits, metres — negative when it is out.
        /// The tightest of the four sides, because a piece is outside the moment one wall
        /// is behind it.
        /// </summary>
        private static float PlanMargin(Bounds b, Bounds interior) =>
            Mathf.Min(
                Mathf.Min(b.min.x - interior.min.x, interior.max.x - b.max.x),
                Mathf.Min(b.min.z - interior.min.z, interior.max.z - b.max.z));

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
