#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// Checks that the monster can actually reach the places the game sends it.
    /// <para>
    /// Core's <c>MapValidator</c> checks §12's rules against the map GRAPH, and it
    /// passes on maps whose baked NavMesh is in pieces — the graph says two rooms are
    /// joined, the navigation surface says otherwise, and nothing compares them. The
    /// symptom is not an error: the monster simply walks to a wall and stops, which
    /// reads as bad AI rather than a broken bake.
    /// </para>
    /// <para>
    /// That failure is fatal to the design. §06's chase, §12's whole S-corridor
    /// argument and §14's first verification question all assume the monster can
    /// follow you anywhere you can walk. A fragmented surface silently deletes the
    /// game's antagonist.
    /// </para>
    /// <para>
    /// So this measures reachability between every pair of places that matter — spawns,
    /// candidate sites, loot points, the exit — and reports the completion rate. A map
    /// that scores below <see cref="RequiredCompletionRate"/> is not shippable however
    /// good it looks.
    /// </para>
    /// <para>
    /// It lives in the scene-generation assembly rather than beside the batch entry
    /// point because generation has to be able to <em>fail</em> on it. A generated map
    /// that does not pass is not a map; it is a building with no antagonist, and
    /// writing it to disk anyway is how B-001 survived a rebuild.
    /// </para>
    /// </summary>
    public static class NavMeshConnectivity
    {
        /// <summary>
        /// Fraction of point pairs that must be fully connected.
        /// <para>
        /// Not 100%: a map may legitimately contain a locked-off area, and §12 wants
        /// 20–25% dead ends. But every spawn, site and exit should reach every other,
        /// so anything below this means the surface is in pieces.
        /// </para>
        /// </summary>
        public const float RequiredCompletionRate = 0.98f;

        /// <summary>How far a sample point may be from the navigable surface, metres.</summary>
        private const float SampleRadius = 4f;

        /// <summary>Measures reachability between every pair of gameplay-relevant points.</summary>
        public static Report Audit(Scene scene)
        {
            var points = CollectPoints(scene);
            var report = new Report { PointCount = points.Count };

            if (points.Count < 2)
            {
                report.Notes.Add("Fewer than two markers found — is this the generated map scene?");
                return report;
            }

            // Snap first and report separately: a marker that is not on the surface at
            // all is a different bug from two surfaces that do not join, and conflating
            // them sends whoever reads this to the wrong place.
            var snapped = new List<(string Name, Vector3 Position)>();
            foreach (var (name, position) in points)
            {
                if (NavMesh.SamplePosition(position, out var hit, SampleRadius, NavMesh.AllAreas))
                {
                    snapped.Add((name, hit.position));

                    // The snap radius is generous enough to cross a §12 grid cell, so a
                    // marker can pass this test by landing on the floor of somewhere
                    // else. Reported rather than failed, because the number that decides
                    // is how far the worst one moved.
                    var moved = Vector3.Distance(position, hit.position);
                    if (moved > report.WorstSnap)
                    {
                        report.WorstSnap = moved;
                        report.WorstSnapName = name;
                    }
                }
                else
                {
                    report.OffSurface.Add(name);
                }
            }

            // Union-find as the pairs are walked, rather than a second O(n²) sweep: the
            // island count is the same relation the pair loop is already computing, and
            // knowing which markers share an island is what turns "13 islands" into
            // something anyone can go and look at.
            var component = new int[snapped.Count];
            for (var i = 0; i < component.Length; i++)
            {
                component[i] = i;
            }

            var path = new NavMeshPath();
            for (var i = 0; i < snapped.Count; i++)
            {
                for (var j = i + 1; j < snapped.Count; j++)
                {
                    report.Pairs++;
                    NavMesh.CalculatePath(snapped[i].Position, snapped[j].Position, NavMesh.AllAreas, path);

                    switch (path.status)
                    {
                        case NavMeshPathStatus.PathComplete:
                            report.Complete++;
                            Union(component, i, j);
                            break;

                        case NavMeshPathStatus.PathPartial:
                            report.Partial++;
                            report.RecordBreak(snapped[i].Name, snapped[j].Name, "partial");
                            break;

                        default:
                            report.Invalid++;
                            report.RecordBreak(snapped[i].Name, snapped[j].Name, "invalid");
                            break;
                    }
                }
            }

            var members = new Dictionary<int, List<string>>();
            for (var i = 0; i < snapped.Count; i++)
            {
                var root = Find(component, i);
                if (!members.TryGetValue(root, out var list))
                {
                    list = new List<string>();
                    members[root] = list;
                }

                list.Add(snapped[i].Name);
            }

            report.Islands = members.Count;
            foreach (var island in members.Values.OrderByDescending(m => m.Count))
            {
                report.IslandMembers.Add(island);
            }

            MeasureMonsterReach(snapped, report);
            return report;
        }

        /// <summary>
        /// The question §06 actually asks: from where the monster starts, can it get to
        /// every player and every place the objective might be?
        /// <para>
        /// It is implied by a 98% completion rate but not stated by it, and it is the
        /// one line of this report that maps onto a design claim rather than onto a
        /// number — §14's first verification question is about a chase that has to
        /// start somewhere.
        /// </para>
        /// </summary>
        private static void MeasureMonsterReach(
            IReadOnlyList<(string Name, Vector3 Position)> snapped, Report report)
        {
            var origins = new List<int>();
            for (var i = 0; i < snapped.Count; i++)
            {
                if (snapped[i].Name.IndexOf("MonsterSpawn", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    origins.Add(i);
                }
            }

            if (origins.Count == 0)
            {
                report.Notes.Add("No MonsterSpawn marker, so §06's own question could not be asked of this scene.");
                return;
            }

            var path = new NavMeshPath();
            foreach (var origin in origins)
            {
                for (var i = 0; i < snapped.Count; i++)
                {
                    if (i == origin)
                    {
                        continue;
                    }

                    var name = snapped[i].Name;
                    var isTarget = name.IndexOf("PlayerSpawn", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("Candidate", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isTarget)
                    {
                        continue;
                    }

                    report.MonsterTargets++;
                    NavMesh.CalculatePath(
                        snapped[origin].Position, snapped[i].Position, NavMesh.AllAreas, path);
                    if (path.status == NavMeshPathStatus.PathComplete)
                    {
                        report.MonsterReached++;
                    }
                    else
                    {
                        report.MonsterUnreachable.Add(name);
                    }
                }
            }
        }

        /// <summary>
        /// Gathers the points the game actually navigates between: spawns, §12's
        /// candidate sites, loot points and the exit. Testing arbitrary geometry would
        /// flag decorative alcoves nobody walks into.
        /// </summary>
        private static List<(string Name, Vector3 Position)> CollectPoints(Scene scene)
        {
            var interesting = new[]
            {
                "PlayerSpawn", "MonsterSpawn", "Site", "Candidate", "Loot", "Exit", "Objective", "Clue",
            };

            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(includeInactive: true))
                .Where(t => t.childCount == 0)
                .Where(t => interesting.Any(k => t.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(t => (t.name, t.position))
                .ToList();
        }

        private static int Find(int[] component, int x)
        {
            while (component[x] != x)
            {
                component[x] = component[component[x]];
                x = component[x];
            }

            return x;
        }

        private static void Union(int[] component, int a, int b)
        {
            var ra = Find(component, a);
            var rb = Find(component, b);
            if (ra != rb)
            {
                component[ra] = rb;
            }
        }

        /// <summary>The measured state of a scene's navigation surface.</summary>
        public sealed class Report
        {
            /// <summary>Markers found.</summary>
            public int PointCount;

            /// <summary>Point pairs tested.</summary>
            public int Pairs;

            /// <summary>Pairs with a complete path.</summary>
            public int Complete;

            /// <summary>Pairs where the path stops short — the classic fragmented-surface signature.</summary>
            public int Partial;

            /// <summary>Pairs with no path at all.</summary>
            public int Invalid;

            /// <summary>Connected components. Anything above 1 means the surface is in pieces.</summary>
            public int Islands = 1;

            /// <summary>Metres the worst-placed marker had to move to land on the surface.</summary>
            public float WorstSnap;

            /// <summary>Which marker that was.</summary>
            public string WorstSnapName = string.Empty;

            /// <summary>Player spawns and §12 candidate sites tested from a MonsterSpawn.</summary>
            public int MonsterTargets;

            /// <summary>How many of those the monster can path to.</summary>
            public int MonsterReached;

            /// <summary>Markers that are not on the navigable surface at all.</summary>
            public readonly List<string> OffSurface = new List<string>();

            /// <summary>Targets §06's monster cannot reach from its spawn.</summary>
            public readonly List<string> MonsterUnreachable = new List<string>();

            /// <summary>Marker names grouped by island, largest first.</summary>
            public readonly List<List<string>> IslandMembers = new List<List<string>>();

            /// <summary>Free-text observations.</summary>
            public readonly List<string> Notes = new List<string>();

            private readonly List<string> _breaks = new List<string>();

            /// <summary>Fraction of pairs fully connected.</summary>
            public float CompletionRate => Pairs > 0 ? Complete / (float)Pairs : 0f;

            /// <summary>True when the surface is whole enough to ship.</summary>
            public bool Passed =>
                Pairs > 0 && CompletionRate >= RequiredCompletionRate && Islands == 1 && OffSurface.Count == 0;

            /// <summary>Records a broken pair, keeping only the first few for the report.</summary>
            public void RecordBreak(string from, string to, string kind)
            {
                if (_breaks.Count < 12)
                {
                    _breaks.Add($"{from} → {to} ({kind})");
                }
            }

            /// <summary>Human-readable summary.</summary>
            public string Describe()
            {
                var sb = new StringBuilder();
                sb.AppendLine("[NavMeshAudit] " + (Passed ? "PASS" : "FAIL"));
                sb.AppendLine($"  markers          {PointCount}");
                sb.AppendLine($"  pairs            {Pairs}");
                sb.AppendLine($"  complete         {Complete} ({CompletionRate:P1}, need {RequiredCompletionRate:P0})");
                sb.AppendLine($"  partial          {Partial}");
                sb.AppendLine($"  invalid          {Invalid}");
                sb.AppendLine($"  islands          {Islands}" + (Islands > 1 ? "  ← the surface is in pieces" : string.Empty));
                sb.AppendLine($"  worst snap       {WorstSnap:0.00} m"
                    + (string.IsNullOrEmpty(WorstSnapName) ? string.Empty : "  (" + WorstSnapName + ")"));

                if (MonsterTargets > 0)
                {
                    sb.AppendLine($"  monster reach    {MonsterReached}/{MonsterTargets} "
                        + "player spawns and 후보 지점 reachable from MonsterSpawn (§06)");
                }

                if (MonsterUnreachable.Count > 0)
                {
                    sb.AppendLine("  the monster cannot reach: " + string.Join(", ", MonsterUnreachable.Take(10)));
                }

                if (OffSurface.Count > 0)
                {
                    sb.AppendLine($"  off the surface  {OffSurface.Count}: {string.Join(", ", OffSurface.Take(10))}");
                }

                foreach (var note in Notes)
                {
                    sb.AppendLine("  note: " + note);
                }

                if (Islands > 1)
                {
                    sb.AppendLine("  islands, largest first:");
                    for (var i = 0; i < IslandMembers.Count && i < 12; i++)
                    {
                        sb.AppendLine("    [" + IslandMembers[i].Count + "] "
                            + string.Join(", ", IslandMembers[i].Take(6))
                            + (IslandMembers[i].Count > 6 ? ", …" : string.Empty));
                    }
                }

                if (_breaks.Count > 0)
                {
                    sb.AppendLine("  broken pairs:");
                    foreach (var b in _breaks)
                    {
                        sb.AppendLine("    " + b);
                    }
                }

                if (!Passed)
                {
                    sb.AppendLine();
                    sb.AppendLine("  The monster reaches players by NavMesh path, so a fragmented surface");
                    sb.AppendLine("  removes the antagonist without producing a single error. Usual causes:");
                    sb.AppendLine("    - stairs or ramps too steep for the agent, leaving each floor an island");
                    sb.AppendLine("    - a doorway narrower than the agent radius");
                    sb.AppendLine("    - pieces that touch visually but leave a gap the bake will not span");
                    sb.AppendLine("    - a landing at a different height with no link joining it");
                    sb.AppendLine("    - a prop or door leaf baked into a 2.2 m corridor, which erodes it shut");
                    sb.AppendLine("    - navmesh on a roof or a ceiling slab, which is an island by construction");
                    sb.AppendLine();
                    sb.AppendLine("  Do not reach for a NavMeshLink. This audit walks NavMesh.CalculatePath,");
                    sb.AppendLine("  which routes through a link, so a link turns this report green without");
                    sb.AppendLine("  moving the monster one metre: MonsterBrain steps along NavMeshPath.corners");
                    sb.AppendLine("  and a link is a gap with no corner to step onto. That is B-001. Fix the");
                    sb.AppendLine("  geometry — for a 계단, the landing has to be at least 4 x the agent radius");
                    sb.AppendLine("  deep with nothing standing on it (tools/blender/gen_mapkit.py).");
                }

                return sb.ToString();
            }
        }
    }
}
