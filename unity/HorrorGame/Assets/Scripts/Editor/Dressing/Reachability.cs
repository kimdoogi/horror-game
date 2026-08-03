#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text;
using HorrorGame.Core;
using HorrorGame.EditorTools.SceneGen;
using UnityEngine;
using UnityEngine.AI;

namespace HorrorGame.EditorTools.Dressing
{
    /// <summary>
    /// A before/after census of what the NavMesh can still reach.
    /// <para>
    /// This is the check that makes "avoids the NavMesh path" a claim rather than an
    /// intention. Dressing is placed against a geometric clearance rule, and a
    /// geometric rule cannot see a route the pathfinder has quietly lost — an agent
    /// radius wider than the gap left, a crate that bridged two cells, a stack that
    /// closed the only mouth of a 막힌 길. So every route is sampled before the pass
    /// runs and again after the rebake, and a route that used to complete and no
    /// longer does fails the scatter outright.
    /// </para>
    /// <para>
    /// The routes sampled are the ones §12 makes load-bearing: player spawn to player
    /// spawn (four players have to be able to regroup) and player spawn to every
    /// 후보 지점, because §12 requires every candidate site to have two escape routes
    /// and §03 puts the objective at one of them. A site the monster can reach and
    /// the players cannot is a match that cannot be won.
    /// </para>
    /// </summary>
    public readonly struct Reachability
    {
        /// <summary>Routes whose path completes.</summary>
        public readonly int Complete;

        /// <summary>Routes the pathfinder could only get partway along.</summary>
        public readonly int Partial;

        /// <summary>Routes with no path at all, including endpoints that are not on the NavMesh.</summary>
        public readonly int Invalid;

        /// <summary>Marker positions that could not be snapped onto the NavMesh at all.</summary>
        public readonly int Unsampled;

        private Reachability(int complete, int partial, int invalid, int unsampled)
        {
            Complete = complete;
            Partial = partial;
            Invalid = invalid;
            Unsampled = unsampled;
        }

        /// <summary>How far off the marker a NavMesh point may be and still count, metres.</summary>
        /// <remarks>
        /// One corridor half-width. A marker sits on a centre line, so anything further
        /// than this is not the same place.
        /// </remarks>
        private static float SnapRadius => MapKitCatalogue.CorridorClearWidth * 0.5f;

        /// <summary>Samples every route the map's markers describe.</summary>
        public static Reachability Sample(GameObject mapRoot)
        {
            var markers = mapRoot.transform.Find(MapSceneBuilder.MarkerRootName);
            if (markers == null)
            {
                return new Reachability(0, 0, 0, 0);
            }

            var spawns = Points(markers, "PlayerSpawns");
            // The group MapSceneBuilder writes is marker.Kind + "s", so 후보 지점's
            // "CandidateSites" became "ReachProbes" when the kind was renamed. Getting
            // this wrong does not throw — Points returns an empty list and this tool
            // silently measures zero routes and calls it fine.
            var sites = Points(markers, "ReachProbes");

            var complete = 0;
            var partial = 0;
            var invalid = 0;
            var unsampled = 0;

            var snappedSpawns = new List<Vector3>();
            foreach (var spawn in spawns)
            {
                if (NavMesh.SamplePosition(spawn, out var hit, SnapRadius, NavMesh.AllAreas))
                {
                    snappedSpawns.Add(hit.position);
                }
                else
                {
                    unsampled++;
                }
            }

            var snappedSites = new List<Vector3>();
            foreach (var site in sites)
            {
                if (NavMesh.SamplePosition(site, out var hit, SnapRadius, NavMesh.AllAreas))
                {
                    snappedSites.Add(hit.position);
                }
                else
                {
                    unsampled++;
                }
            }

            void Route(Vector3 from, Vector3 to)
            {
                var path = new NavMeshPath();
                NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path);
                switch (path.status)
                {
                    case NavMeshPathStatus.PathComplete:
                        complete++;
                        break;
                    case NavMeshPathStatus.PathPartial:
                        partial++;
                        break;
                    default:
                        invalid++;
                        break;
                }
            }

            for (var i = 0; i < snappedSpawns.Count; i++)
            {
                for (var j = i + 1; j < snappedSpawns.Count; j++)
                {
                    Route(snappedSpawns[i], snappedSpawns[j]);
                }

                foreach (var site in snappedSites)
                {
                    Route(snappedSpawns[i], site);
                }
            }

            return new Reachability(complete, partial, invalid, unsampled);
        }

        /// <summary>Whether this census lost nothing against an earlier one.</summary>
        public bool NoWorseThan(Reachability baseline) =>
            Complete >= baseline.Complete && Unsampled <= baseline.Unsampled;

        /// <summary>One-line summary.</summary>
        public string Describe() =>
            Complete + " complete, " + Partial + " partial, " + Invalid + " unreachable, "
            + Unsampled + " markers off-mesh";

        /// <summary>The before/after report line.</summary>
        public string Compare(Reachability after, bool rebaked)
        {
            var text = new StringBuilder();
            text.Append("NavMesh (").Append(rebaked ? "rebaked with the new cover" : "NOT rebaked — no NavMeshSurface found")
                .Append("): before ").Append(Describe()).Append("  →  after ").Append(after.Describe()).Append('\n');
            text.Append("  §12 routes sampled: every player spawn to every other spawn and to every 후보 지점, at an ")
                .Append(SnapRadius.ToString("0.00")).Append(" m snap radius.");
            return text.ToString();
        }

        private static IEnumerable<Vector3> Points(Transform markers, string groupName)
        {
            var group = markers.Find(groupName);
            if (group == null)
            {
                yield break;
            }

            foreach (var child in group.Cast<Transform>().OrderBy(t => t.name, System.StringComparer.Ordinal))
            {
                yield return child.position + (Vector3.up * 0.1f);
            }
        }
    }
}
