using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// B-010's decisive measurement: does the 중심 chamber's doorway join the corridor
    /// outside it, and if not, what exactly is standing in the gap?
    /// <para>
    /// Written because every previous answer to B-010 was a deduction. The chamber is
    /// authored with a 2.5 m opening in the middle cell of each edge, the corridor's
    /// clear width is 2.20 m and the dock cell outside it is tiled as a straight with
    /// both ends open — so they should meet, and they do not. Floor height, wall overlap
    /// and doorway width each fail differently, and the only way to tell them apart is
    /// to sample the surface that was actually baked.
    /// </para>
    /// <para>
    /// It builds the map in memory exactly as <see cref="MapSceneGenerator.Generate"/>
    /// does — same sketch, same <see cref="MapSceneBuilder"/>, same bake — then walks a
    /// line outward from the middle of the chamber in 0.10 m steps, printing where the
    /// collider under the line is and where <see cref="NavMesh.SamplePosition"/> finds
    /// walkable surface. Then it hides the 문 geometry, re-bakes and walks the same
    /// line again, which is what turns "something is in the way" into a name.
    /// </para>
    /// </summary>
    public static class ChamberDockProbe
    {
        /// <summary>Steps along the probe line, metres. Finer than the agentRadius/5 voxel.</summary>
        private const float Step = 0.10f;

        /// <summary>How far outward from the chamber's middle to walk — past the dock cell and the band.</summary>
        private const float Reach = 9.0f;

        /// <summary>Vertical slack for <see cref="NavMesh.SamplePosition"/>. Wider than any camber or slab.</summary>
        private const float SampleRadius = 0.6f;

        /// <summary>Runs the probe from the menu.</summary>
        [MenuItem("HorrorGame/Scene Gen/Probe the chamber doorway (B-010)", priority = 40)]
        public static void RunMenu() => Debug.Log(Run(DescentMap.DefaultSeed));

        /// <summary>Runs the probe from the command line and exits.</summary>
        public static void RunFromCommandLine()
        {
            Debug.Log(Run(DescentMap.DefaultSeed));
            EditorApplication.Exit(0);
        }

        /// <summary>Builds, bakes and measures — as built, then with the 문 geometry hidden.</summary>
        /// <param name="seed">Map seed. The same seed gives the same tower.</param>
        public static string Run(int seed)
        {
            var map = DescentMap.Build(seed);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = MapSceneBuilder.Build(map, "ChamberDockProbe");

            var sb = new StringBuilder();
            sb.AppendLine("[ChamberProbe] seed " + seed);
            sb.AppendLine("  navmesh " + NavMesh.CalculateTriangulation().vertices.Length + " vertices");

            // AlignMinCorner pins a piece's renderer-bounds MINIMUM to the cell's floor Y,
            // so two pieces whose lowest geometry differs get floors at different heights.
            // That is one of the three ways a doorway can fail and it is invisible in the
            // sketch, so the bounds are printed before anything is sampled.
            ReportBounds(sb, root, "ChamberOpen3x3_L0_");
            ReportBounds(sb, root, "CorridorStraight2m5_L0_");
            ReportBounds(sb, root, "JunctionT_L0_");
            ReportDoors(sb, root);

            Measure(sb, map, "as built");
            Headline(sb, scene, "as built");

            // Pass 2: the 문. MapSceneBuilder.BuildDoor hangs a whole Doorway_Frame CELL
            // and a Door_Panel_Lockable leaf off the marker, and adds a carving
            // NavMeshObstacle — none of which is kept out of the bake the way the props and
            // the zone slabs are. If the gap closes when they are hidden, that is the
            // answer; if it does not, they are cleared and the geometry is at fault.
            var hidden = HideDoors(root);
            sb.AppendLine("  hid " + hidden + " door objects, re-baking");
            ReBake(root);
            sb.AppendLine("  navmesh " + NavMesh.CalculateTriangulation().vertices.Length + " vertices");
            Measure(sb, map, "문 hidden");
            Headline(sb, scene, "문 hidden");

            return sb.ToString();
        }

        /// <summary>
        /// The three numbers B-010 is judged on, taken from the same audit the generator
        /// gates on. Printed for both passes so the A/B is on the headline rather than on
        /// a probe of my own invention.
        /// </summary>
        private static void Headline(StringBuilder sb, Scene scene, string label)
        {
            sb.AppendLine("  ══ headline (" + label + ")");
            sb.AppendLine(NavMeshConnectivity.Audit(scene).Describe());
        }

        /// <summary>Prints the world bounds of the first tile whose name starts with the prefix.</summary>
        private static void ReportBounds(StringBuilder sb, GameObject root, string prefix)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(prefix, System.StringComparison.Ordinal) || !TryBounds(t.gameObject, out var b))
                {
                    continue;
                }

                sb.AppendLine("  " + t.name + "  min " + b.min.ToString("F3") + "  max " + b.max.ToString("F3"));
                return;
            }

            sb.AppendLine("  " + prefix + "*  — no such tile");
        }

        /// <summary>Prints every 문 on L0 with the world bounds of the frame hung on it.</summary>
        private static void ReportDoors(StringBuilder sb, GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.IndexOf("문", System.StringComparison.Ordinal) < 0
                    && t.name.IndexOf("Door", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (t.parent == null || t.parent.name != "Markers")
                {
                    continue;
                }

                var line = new StringBuilder("  door " + t.name + " at " + t.position.ToString("F2"));
                var frame = t.Find("Frame");
                if (frame != null && TryBounds(frame.gameObject, out var fb))
                {
                    line.Append("  frame min ").Append(fb.min.ToString("F2")).Append(" max ").Append(fb.max.ToString("F2"));
                }

                sb.AppendLine(line.ToString());
            }
        }

        /// <summary>Disables every renderer and obstacle the 문 hangs in the passage.</summary>
        private static int HideDoors(GameObject root)
        {
            var hidden = 0;
            foreach (var obstacle in root.GetComponentsInChildren<NavMeshObstacle>(true))
            {
                obstacle.enabled = false;
                hidden++;
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Frame" && t.name != "Leaf")
                {
                    continue;
                }

                t.gameObject.SetActive(false);
                hidden++;
            }

            return hidden;
        }

        private static void ReBake(GameObject root)
        {
            var surface = root.GetComponentInChildren<NavMeshSurface>(true);
            if (surface == null)
            {
                return;
            }

            NavMesh.RemoveAllNavMeshData();
            surface.BuildNavMesh();
        }

        private static void Measure(StringBuilder sb, MapSketchResult map, string label)
        {
            sb.AppendLine("  ══ " + label);
            for (var level = 0; level < DescentMap.Storeys; level++)
            {
                ProbeStorey(sb, map, level);
            }
        }

        private static void ProbeStorey(StringBuilder sb, MapSketchResult map, int level)
        {
            var cx = DescentMap.Centre;
            var cz = DescentMap.Centre;
            var floorY = MapKitCatalogue.FloorY(level);

            var middle = new MapCell(cx, cz, level).Centre;
            var from = new Vector3(middle.X, floorY + 0.5f, middle.Z);
            var haveFrom = NavMesh.SamplePosition(from, out var fromHit, SampleRadius, NavMesh.AllAreas);

            sb.AppendLine("  ── L" + level + " floor y " + floorY.ToString("F3")
                + "  middle " + (haveFrom
                    ? "on navmesh at y " + fromHit.position.y.ToString("F3")
                    : "NOT ON NAVMESH — the chamber itself did not bake"));

            // Only the bearing the single inner gate was punched on has corridor outside
            // it; the other three doorways open onto solid ground and are expected to stop
            // at the piece's own edge, so only the gate bearing is worth walking.
            var dirs = new[] { (Dx: 0, Dz: -1), (Dx: 0, Dz: 1), (Dx: -1, Dz: 0), (Dx: 1, Dz: 0) };
            foreach (var d in dirs)
            {
                var dock = new MapCell(cx + (d.Dx * 2), cz + (d.Dz * 2), level);
                if (!HasTile(map, dock))
                {
                    continue;
                }

                var line = new StringBuilder("     gate ");
                line.Append(d.Dz == -1 ? "S" : d.Dz == 1 ? "N" : d.Dx == -1 ? "W" : "E");
                line.Append(" via ").Append(dock).Append("  ");

                var gapStart = float.NaN;
                var gapEnd = float.NaN;
                var lastFloor = float.NaN;
                var stepAt = float.NaN;
                var stepSize = 0f;
                for (var t = 0f; t <= Reach; t += Step)
                {
                    var p = new Vector3(middle.X + (d.Dx * t), floorY + 0.5f, middle.Z + (d.Dz * t));

                    var haveFloor = Physics.Raycast(p + (Vector3.up * 2f), Vector3.down, out var rayHit, 6f);
                    if (haveFloor)
                    {
                        if (!float.IsNaN(lastFloor) && Mathf.Abs(rayHit.point.y - lastFloor) > Mathf.Abs(stepSize))
                        {
                            stepSize = rayHit.point.y - lastFloor;
                            stepAt = t;
                        }

                        lastFloor = rayHit.point.y;
                    }

                    if (!NavMesh.SamplePosition(p, out _, SampleRadius, NavMesh.AllAreas) && haveFloor)
                    {
                        if (float.IsNaN(gapStart))
                        {
                            gapStart = t;
                        }

                        gapEnd = t;
                    }
                }

                line.Append("floor-but-no-navmesh ").Append(float.IsNaN(gapStart)
                    ? "none"
                    : gapStart.ToString("F2") + "~" + gapEnd.ToString("F2") + " m out");
                line.Append("   worst floor step ").Append(float.IsNaN(stepAt)
                    ? "none"
                    : stepSize.ToString("F3") + " m at " + stepAt.ToString("F2") + " m out");

                var band = new MapCell(cx + (d.Dx * 3), cz + (d.Dz * 3), level).Centre;
                var to = new Vector3(band.X, floorY + 0.5f, band.Z);
                if (NavMesh.SamplePosition(from, out var a, SampleRadius, NavMesh.AllAreas)
                    && NavMesh.SamplePosition(to, out var b, SampleRadius, NavMesh.AllAreas))
                {
                    var path = new NavMeshPath();
                    NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
                    line.Append("   middle→band ").Append(path.status);
                }
                else
                {
                    line.Append("   middle→band — one end has no navmesh");
                }

                sb.AppendLine(line.ToString());
            }
        }

        private static bool HasTile(MapSketchResult map, MapCell cell)
        {
            foreach (var tile in map.Tiles)
            {
                if (tile.Origin.Equals(cell))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
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
    }
}
