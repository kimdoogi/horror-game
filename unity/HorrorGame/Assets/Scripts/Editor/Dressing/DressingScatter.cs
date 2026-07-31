#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Session;
using HorrorGame.EditorTools.SceneGen;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace HorrorGame.EditorTools.Dressing
{
    /// <summary>
    /// Scatters the dressing kit through a generated map from a seed.
    /// <para>
    /// <b>Why this is a tool and not a decorated scene.</b> §12 opens with "맵은
    /// 아트가 아니라 시스템이다" and the map itself is generated from a seed for exactly
    /// that reason. Dressing placed by hand would be the one part of the level that
    /// could not be regenerated, so the first time the layout changed it would either
    /// be thrown away or start contradicting the geometry. Same seed, same layout —
    /// and a reported bad layout is reproducible here.
    /// </para>
    /// <para>
    /// <b>What it is allowed to break.</b> Nothing. Dressing is downstream of §12's
    /// escape maths, and three constraints are enforced rather than hoped for:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>The carry channel.</b> §08's two weight-5 pieces are
    /// carried by two players through corridors, and the document calls that its best
    /// moment. The width required is <em>measured off the loot FBXs</em>, not
    /// declared, and no cell is allowed to drop below it.</description></item>
    /// <item><description><b>The NavMesh.</b> Reachability between every player spawn
    /// and every §12 candidate site is recorded before dressing goes in and checked
    /// again after the rebake. A regression fails the run.</description></item>
    /// <item><description><b>The 개방 공간 sight line.</b> §12 requires the hall to
    /// hold 15~25 m of sight and §04's Observer needs
    /// <see cref="GameConstants.ObserverRange"/> of it. The longest clear line across
    /// the hall is measured after placement and must still make that number.</description></item>
    /// </list>
    /// <para>
    /// <b>What it is trying to achieve.</b> Empty corridors read as unfinished, and
    /// they also delete the cover §12 assumes: 어그로 해제 needs 3 s without line of
    /// sight and §12 spaces 시야 차단 지점 at 15~25 m, which bare walls only supply
    /// where the architecture happens to bend. Bulk dressing is that cover.
    /// </para>
    /// </summary>
    public static class DressingScatter
    {
        /// <summary>Root object all dressing hangs from, under the map root.</summary>
        public const string DressingRootName = "Dressing";

        /// <summary>Default seed. Distinct from the map seed so dressing can be re-rolled without moving a wall.</summary>
        public const int DefaultSeed = 4703;

        /// <summary>
        /// Runs the whole pass on the currently open scene.
        /// </summary>
        /// <param name="seed">Fixes the layout. The same seed always produces the same scatter.</param>
        /// <param name="report">Measured results, on success and on failure.</param>
        /// <returns>False when a §08 / §12 constraint could not be met; nothing is left half-done.</returns>
        public static bool Run(int seed, out string report)
        {
            var text = new StringBuilder();

            var mapRoot = GameObject.Find(MapSceneBuilder.MapRootName);
            if (mapRoot == null)
            {
                report = "No '" + MapSceneBuilder.MapRootName + "' object in the open scene. Generate the map first "
                    + "(HorrorGame ▸ Scene Gen ▸ Generate First Map).";
                return false;
            }

            var kit = DressingManifest.Load(out var kitError);
            if (kit == null)
            {
                report = kitError;
                return false;
            }

            if (Mathf.Abs(kit.grid_metres - MapKitCatalogue.GridMetres) > 0.001f)
            {
                report = "The dressing manifest was authored on a " + kit.grid_metres + " m grid but the MapKit is on "
                    + MapKitCatalogue.GridMetres + " m. Every 2.5 m run would leave a gap at each cell boundary.";
                return false;
            }

            Remove(mapRoot);
            DressingImport.Forget();
            DressingImport.Probe(kit);

            var space = DressingSpace.Read(mapRoot, out var spaceError);
            if (space == null)
            {
                report = spaceError;
                return false;
            }

            var carry = CarryEnvelope.Measure(out var carryNote);

            // Rebake *before* sampling the baseline. Removing the previous scatter takes
            // the objects out of the scene but leaves the NavMesh that was baked around
            // them, so a run that read the baseline straight after would be comparing
            // the new dressing against the old dressing's mesh. That is not a subtle
            // error: one run that sealed a route would lower the baseline permanently
            // and every later run would report "no regression" against it.
            Rebake(mapRoot);
            var baseline = Reachability.Sample(mapRoot);

            var materials = DressingMaterials.Build(kit);
            var root = new GameObject(DressingRootName);
            root.transform.SetParent(mapRoot.transform, false);

            var session = new ScatterSession(space, kit, materials, root, carry, new DeterministicRandom(seed));
            session.Scatter();

            Physics.SyncTransforms();
            var rebaked = Rebake(mapRoot);
            var after = Reachability.Sample(mapRoot);

            var sight = Sightlines.Measure(space);

            text.Append("Dressing scatter — seed ").Append(seed).Append('\n');
            text.Append("Kit: ").Append(kit.pieces.Length).Append(" pieces from ").Append(kit.generated_by)
                .Append(" (assumptions: eye ").Append(kit.assumptions.eye_height_metres).Append(" m, ceiling ")
                .Append(kit.assumptions.ceiling_clear_metres).Append(" m, ").Append(kit.assumptions.source)
                .Append(")\n");
            text.Append(carryNote).Append('\n');
            text.Append(session.Describe()).Append('\n');
            text.Append(sight.Describe()).Append('\n');
            text.Append(baseline.Compare(after, rebaked)).Append('\n');

            var failures = new List<string>();
            if (!after.NoWorseThan(baseline))
            {
                failures.Add("NavMesh reachability regressed: " + baseline.Describe() + " → " + after.Describe()
                    + ". Dressing has sealed a route §12's candidate sites depend on.");
            }

            if (sight.HallCells > 0 && sight.LongestHallLine < GameConstants.ObserverRange)
            {
                failures.Add("The 개방 공간's longest clear sight line is " + sight.LongestHallLine.ToString("0.0")
                    + " m, under §04's ObserverRange of " + GameConstants.ObserverRange
                    + " m. §12 requires the hall to hold 15~25 m of sight; dressing has closed it.");
            }

            if (session.ChannelViolations > 0)
            {
                failures.Add(session.ChannelViolations + " placements were kept that narrow a cell below the "
                    + carry.Width.ToString("0.00") + " m two-person carry channel. This is a bug in the placement "
                    + "test, not a tuning problem.");
            }

            if (failures.Count > 0)
            {
                foreach (var failure in failures)
                {
                    text.Append("FAIL ").Append(failure).Append('\n');
                }

                report = text.ToString();
                return false;
            }

            report = text.ToString();
            return true;
        }

        /// <summary>Deletes a previous scatter, so the pass is idempotent.</summary>
        public static void Remove(GameObject mapRoot)
        {
            var existing = mapRoot.transform.Find(DressingRootName);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
        }

        /// <summary>
        /// Rebuilds the map's NavMesh so the monster (§06) walks round the new cover
        /// instead of through it, and writes the data back to the asset the scene
        /// references.
        /// <para>
        /// Overwriting that asset is the established behaviour for it —
        /// <see cref="MapSceneBuilder"/> deletes and recreates the same path on every
        /// generate — and leaving it stale would be worse: the scene would reference a
        /// bake taken before the crates existed.
        /// </para>
        /// </summary>
        private static bool Rebake(GameObject mapRoot)
        {
            var surface = mapRoot.GetComponentInChildren<NavMeshSurface>();
            if (surface == null)
            {
                return false;
            }

            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                return false;
            }

            var scene = mapRoot.scene;
            var sceneName = string.IsNullOrEmpty(scene.name) ? "Map" : scene.name;
            SceneGenPaths.EnsureFolder(SceneGenPaths.NavMeshRoot);
            var path = SceneGenPaths.NavMeshRoot + "/NavMesh_" + sceneName + ".asset";
            if (!AssetDatabase.Contains(surface.navMeshData))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(surface.navMeshData, path);
            }
            else
            {
                EditorUtility.SetDirty(surface.navMeshData);
            }

            return true;
        }

        /// <summary>
        /// The volume two players carrying §08's weight-5 loot sweep down a corridor.
        /// <para>
        /// Measured off <c>Loot_LargePiece_*.fbx</c> rather than declared. §08 grants
        /// 2인 운반 to exactly 대형 초상화 and 궤짝, <c>gen_props.py</c> asserts their
        /// geometry earns it, and the width that has to stay clear is whatever those
        /// two pieces are actually as wide as — so a re-export that changes one changes
        /// this, and no constant needs updating anywhere.
        /// </para>
        /// </summary>
        public readonly struct CarryEnvelope
        {
            /// <summary>Width that must stay clear across a corridor, metres.</summary>
            public readonly float Width;

            /// <summary>Height below which an intrusion counts against the channel, metres.</summary>
            public readonly float Height;

            private CarryEnvelope(float width, float height)
            {
                Width = width;
                Height = height;
            }

            /// <summary>Measures the envelope from the shared-carry loot FBXs.</summary>
            public static CarryEnvelope Measure(out string note)
            {
                var names = new[] { "Loot_LargePiece_Portrait", "Loot_LargePiece_Chest" };
                var width = 0f;
                var height = 0f;
                var found = 0;

                foreach (var name in names)
                {
                    // Measured through the same upright correction the dressing gets.
                    // Without it the portrait is measured lying down and the "carry
                    // channel" becomes its 1.63 m *height* — which is wider than half a
                    // corridor and rejects almost every piece of cover in the kit.
                    if (!DressingImport.TryMeasure("Assets/Models/Props/" + name + ".fbx", out var bounds))
                    {
                        continue;
                    }

                    // Widest horizontal extent, which is how gen_props.py itself
                    // reasons about these two: a portrait goes through broadside and
                    // a chest is carried fore-and-aft but turns at every corner.
                    width = Mathf.Max(width, Mathf.Max(bounds.size.x, bounds.size.z));
                    height = Mathf.Max(height, bounds.size.y);
                    found++;
                }

                if (found == 0)
                {
                    // The kit is missing. Fall back to the corridor's own clear section
                    // minus a shoulder, and say so — silently dressing to a guessed
                    // channel is how §08's scene stops being playable.
                    var fallback = MapKitCatalogue.CorridorClearWidth * 0.5f;
                    note = "Carry channel: " + fallback.ToString("0.00")
                        + " m ASSUMED — Loot_LargePiece_*.fbx were not found, so §08's two-person "
                        + "pieces could not be measured. Run gen_props.py.";
                    return new CarryEnvelope(fallback, 1.7f);
                }

                note = "Carry channel: " + width.ToString("0.00") + " m x " + height.ToString("0.00")
                    + " m, measured off §08's " + found + " shared-carry piece(s). A corridor's clear section is "
                    + MapKitCatalogue.CorridorClearWidth.ToString("0.00") + " m, so dressing may take at most "
                    + (MapKitCatalogue.CorridorClearWidth - width).ToString("0.00") + " m of it.";
                return new CarryEnvelope(width, height);
            }
        }
    }
}
