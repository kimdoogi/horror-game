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
    /// escape maths, and these constraints are enforced rather than hoped for:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>The building.</b> Every solid piece stays inside the clear
    /// section of the walkable floor. Run 11 anchored floor pieces — which are pivoted at
    /// their own footprint centre — on wall faces, so 969 of 2086 solid pieces reached
    /// past the wall into the ground between corridors, and the escape sweep went from
    /// 0 escapes to 17. See <see cref="DressingSpace.InsideTheClearBand"/>.</description></item>
    /// <item><description><b>The places the game puts a body.</b> The 착지 a 투하구 drops
    /// a runner onto, the mouth it drops them through, §01's 36 starts, §06's spawns and
    /// §12's door swings. Run 11 put <c>Dress_RubblePile_22_22</c> under B5's 투하구 and
    /// every runner who took it landed on 0.26 m of scenery. See
    /// <see cref="KeepOut"/>.</description></item>
    /// <item><description><b>The clear channel.</b> §02 puts twenty runners in one
    /// maze and they pass each other in it, and §06's creature walks the same corridors
    /// on a NavMesh that erodes its own agent radius off every crate. The width required
    /// is <em>derived from both bodies</em>, not declared, and no cell is allowed to drop
    /// below it. See <see cref="ClearChannel"/>.</description></item>
    /// <item><description><b>The NavMesh.</b> Reachability between every player spawn
    /// and every §12 candidate site is recorded before dressing goes in and checked
    /// again after the rebake. A regression fails the run.</description></item>
    /// <item><description><b>The 개방 공간 sight line.</b> §12 requires the hall to
    /// hold 15~25 m of sight, which <see cref="GameConstants.HallClearSightMin"/> is the
    /// floor of. (It used to cite §04's Observer; that role is deleted and the constant
    /// went with it — the 15 m is §12's, and always was.) The longest clear line across
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
        /// <returns>False when a §02 / §06 / §12 constraint could not be met; nothing is left half-done.</returns>
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

            var kit = DressingManifest.Load(out var kitError, out var tombstone);
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

            var carry = ClearChannel.Derive(out var carryNote);

            // Read before anything is placed, off the markers the map generator wrote:
            // the 착지 columns, the 투하구 mouths, every spawn and every 문's swing. See
            // KeepOut for why these cannot be derived from the walkable cell grid.
            var keepOut = KeepOut.Read(mapRoot);

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

            var session = new ScatterSession(space, kit, materials, root, carry, keepOut,
                new DeterministicRandom(seed));
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
            if (tombstone.Length > 0)
            {
                text.Append(tombstone).Append('\n');
            }

            if (keepOut.Note.Length > 0)
            {
                text.Append(keepOut.Note).Append('\n');
            }

            text.Append(session.Describe()).Append('\n');
            text.Append(sight.Describe()).Append('\n');
            text.Append(baseline.Compare(after, rebaked)).Append('\n');

            // Which surface the game will actually load, said out loud.
            //
            // This pass rebakes the NavMesh with the solid dressing in it — that is the
            // point of Rebake, so §06's creature walks round the new cover — and writes it
            // over the asset the scene references. The NavMesh audit and the reach audit
            // ran BEFORE this pass, in MapPipeline's layout stage, against the bake this
            // one replaced. Their numbers are true of a building with no crates in it.
            // Nobody reading a log with both in it should have to work that out; B-009 was
            // the same sentence about a different pass — 「the NavMesh being audited is not
            // the one just built」 — and this line is here so it does not have to be
            // learned a third time.
            if (rebaked)
            {
                text.Append("NOTE: the NavMesh asset the scene references is THIS pass's bake, taken with ")
                    .Append("the dressing in place. The NavMesh and reach audits printed earlier in this log ")
                    .Append("measured the layout stage's bake, before any of it existed — they are numbers ")
                    .Append("about a different surface. The comparison above is the only one taken on both.\n");
            }

            var failures = new List<string>();
            if (!after.NoWorseThan(baseline))
            {
                failures.Add("NavMesh reachability regressed: " + baseline.Describe() + " → " + after.Describe()
                    + ". Dressing has sealed a route §12's candidate sites depend on.");
            }

            if (sight.HallCells > 0 && sight.LongestHallLine < GameConstants.HallClearSightMin)
            {
                failures.Add("The 개방 공간's longest clear sight line is " + sight.LongestHallLine.ToString("0.0")
                    + " m, under §12's " + GameConstants.HallClearSightMin
                    + " m. §12 requires the hall to hold 15~25 m of sight; dressing has closed it.");
            }

            if (session.ChannelViolations > 0)
            {
                failures.Add(session.ChannelViolations + " placements were kept that narrow a cell below the "
                    + carry.Width.ToString("0.00") + " m clear channel §02 and the bake both need. This is a bug "
                    + "in the placement test, not a tuning problem.");
            }

            // The three defects run 11 shipped, each now a measurement rather than a
            // comment. All three are re-derived in ScatterSession.Verify from the placed
            // transforms, not from the bookkeeping that placed them.
            if (session.BandViolations > 0)
            {
                failures.Add(session.BandViolations + " solid pieces were kept that reach off the floor a runner "
                    + "stands on — worst " + session.WorstBandMetres.ToString("0.000") + " m ("
                    + session.WorstBandWhere + "). A solid collider outside the walkable clear band is a surface "
                    + "outside the maze for a capsule to be pushed onto, and §01's building has to hold: "
                    + "「맵밖으로 나갈수가있눈거같은데 이거 못나가게 막아야지」.");
            }

            if (session.KeepOutViolations > 0)
            {
                failures.Add(session.KeepOutViolations + " pieces were kept inside a keep-out volume — the "
                    + "tightest is " + session.TightestKeepOutMetres.ToString("0.000") + " m at "
                    + session.TightestKeepOutWhere + ". These are the places the game puts a body: a 착지 a "
                    + "runner falls onto, a 투하구 mouth, a start, a §12 문's swing. §01's only way down is "
                    + "the drop, and it may not land on scenery.");
            }

            if (session.ClueSurfaces > 0)
            {
                failures.Add(session.ClueSurfaces + " Clue_Face surfaces were placed. §03's clue chain is "
                    + "deleted — 「목적지가 처음부터 알려진 경주에서 성립하지 않아 통째로 걷어냈다」 — so this "
                    + "count can only be zero. A piece carrying one got past DressingManifest.Load.");
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
        /// The width a cell has to keep clear after the dressing is in it.
        /// <para>
        /// <b>TOMBSTONE — what this was, and why the yardstick changed.</b> Until R12 this
        /// was <c>CarryEnvelope</c>, measured off <c>Loot_LargePiece_Portrait.fbx</c> and
        /// <c>Loot_LargePiece_Chest.fbx</c>: §08 granted 2인 운반 to exactly 대형 초상화 and
        /// 궤짝, so the width that had to stay clear was whatever those two were as wide as
        /// (1.28 m). §08 is deleted, there is no loot in a race to the middle, and R12
        /// deleted the two FBXs — at which point <c>Measure</c> fell back to half a
        /// corridor, printed <c>1.10 m ASSUMED</c>, and the run <b>sealed the building</b>:
        /// 3482/3482 NavMesh pairs became 1547/3482 across 31 islands, because a 1.10 m gap
        /// leaves 0.10 m of surface once Recast has eroded the agent's radius off both
        /// sides of it. A fallback that keeps a pass running on a guessed number is worse
        /// than one that stops, and this is the run the old doc comment said would come.
        /// </para>
        /// <para>
        /// So the yardstick is now derived, not measured off an asset, and every term in it
        /// belongs to a system that is still alive:
        /// </para>
        /// <list type="bullet">
        /// <item><b>§02, twenty runners in one maze.</b> They pass each other in these
        /// corridors, so the gap must hold two bodies abreast: 4 × the runner capsule's
        /// radius (<see cref="PlayerTraversal.PlayerBody"/>).</item>
        /// <item><b>§06's creature has to still be able to walk it.</b> The creature paths
        /// on the NavMesh, and Recast erodes <c>agentRadius</c> off every collider before
        /// baking — so a gap of W metres leaves only W − 2·agentRadius of surface. What is
        /// left has to be a strip of real surface and not a hairline: Recast drops any
        /// region under <c>minRegionArea</c> (π·agentRadius², set in
        /// <c>MapSceneBuilder</c>) and rasterises at <c>voxelSize</c> = agentRadius/5, so
        /// over one <c>MapKitCatalogue.GridMetres</c> cell the strip must clear both of
        /// those, and it must be at least a body wide or nothing can stand on it.</item>
        /// </list>
        /// <para>
        /// The gap the dressing must leave is the larger of the two. Both are read from
        /// live values at run time — change the bake's agent or the runner's capsule and
        /// this follows, with no constant to update.
        /// </para>
        /// </summary>
        public readonly struct ClearChannel
        {
            /// <summary>Width that must stay clear across a cell, metres.</summary>
            public readonly float Width;

            /// <summary>Height below which an intrusion counts against the channel, metres.</summary>
            public readonly float Height;

            private ClearChannel(float width, float height)
            {
                Width = width;
                Height = height;
            }

            /// <summary>Derives the channel from the runner's body and the bake's agent.</summary>
            public static ClearChannel Derive(out string note)
            {
                var body = new PlayerTraversal.PlayerBody();

                // §02: two runners abreast, each capsule a full diameter of floor.
                var passing = 4f * body.Radius;

                var agent = AgentRadius(out var agentNote);
                var voxel = agent / 5f;                            // MapSceneBuilder.Rebake
                var minRegion = Mathf.PI * agent * agent;          // MapSceneBuilder.minRegionArea

                // The narrowest strip of baked surface that is still a surface: wide
                // enough to hold a body, to survive minRegionArea over one cell, and to
                // be more than rasterisation noise at this voxel size.
                var strip = Mathf.Max(
                    2f * body.Radius,
                    Mathf.Max(minRegion / MapKitCatalogue.GridMetres, 2f * voxel));

                var bakeSurvives = (2f * agent) + strip;
                var width = Mathf.Max(passing, bakeSurvives);

                // An intrusion counts against the channel if a runner would walk into it
                // rather than under it, which is anything below their own standing height.
                var height = body.Height;

                note = "Clear channel: " + width.ToString("0.00") + " m x " + height.ToString("0.00")
                    + " m, derived — §02's two runners abreast need " + passing.ToString("0.00")
                    + " m, and §06's creature needs " + bakeSurvives.ToString("0.00")
                    + " m (Recast erodes " + (2f * agent).ToString("0.00") + " m off a gap and the "
                    + strip.ToString("0.00") + " m left has to survive minRegionArea "
                    + minRegion.ToString("0.00") + " m² over a " + MapKitCatalogue.GridMetres.ToString("0.00")
                    + " m cell at " + voxel.ToString("0.00") + " m voxels). A corridor's clear section is "
                    + MapKitCatalogue.CorridorClearWidth.ToString("0.00") + " m, so dressing may take at most "
                    + (MapKitCatalogue.CorridorClearWidth - width).ToString("0.00") + " m of it. " + agentNote;
                return new ClearChannel(width, height);
            }

            /// <summary>
            /// The bake's own agent radius. Falls back to the runner's capsule only if the
            /// project has no NavMesh agent at all, and says so — the two are different
            /// bodies (0.50 m against 0.30 m) and quietly swapping one for the other is
            /// how a channel that looks derived stops being derived from anything.
            /// </summary>
            private static float AgentRadius(out string note)
            {
                if (UnityEngine.AI.NavMesh.GetSettingsCount() > 0)
                {
                    note = string.Empty;
                    return UnityEngine.AI.NavMesh.GetSettingsByIndex(0).agentRadius;
                }

                note = "The project declares no NavMesh agent, so the creature's radius fell back to the "
                    + "runner's capsule — this channel is NOT derived from the bake it is meant to protect.";
                return new PlayerTraversal.PlayerBody().Radius;
            }
        }
    }
}
