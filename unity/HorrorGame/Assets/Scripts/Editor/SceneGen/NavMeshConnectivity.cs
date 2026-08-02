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
    /// <b>Two measures, and they are different questions.</b> The pair sweep asks whether a
    /// storey is one piece of walkable surface. <see cref="MeasureMonsterReach"/> asks §06's
    /// own question — can the creature get to a runner — and asks it of EVERY storey, naming
    /// the ones that have no creature on them rather than reporting the average of the one
    /// that does. On §01's tower those two used to disagree without anybody noticing.
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
            //
            // Both positions are kept. The SNAPPED one is what NavMesh.CalculatePath is
            // asked about, because a query has to start on the surface. The AUTHORED one is
            // what says which storey the marker belongs to, and on §01's tower those are
            // two different facts: the snap radius (4 m) is wider than the storey pitch
            // (MapKitCatalogue.StoreyMetres, 3.75 m), so a marker whose own floor is missing
            // under it lands on the floor above or below and, judged by where it ended up,
            // changes storey. Measured on seed 20260802: worst snap 3.90 m, and the island
            // list came back with B5, B6 and B7 markers sharing one island — which cannot
            // happen for markers judged on their own floor, because no pair across storeys
            // is ever unioned.
            var snapped = new List<(string Name, Vector3 Authored, Vector3 Position)>();
            foreach (var (name, position) in points)
            {
                if (NavMesh.SamplePosition(position, out var hit, SampleRadius, NavMesh.AllAreas))
                {
                    snapped.Add((name, position, hit.position));

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

                    // Named, not failed. A marker standing on another storey's floor is a
                    // hole in ITS OWN storey — the surface under it is absent or walled off —
                    // and that is a geometry report, not a snapping policy. It is printed
                    // because everything else this audit says about that marker (which floor,
                    // which island, whether §06's creature can reach it) is now being decided
                    // on its authored storey while the path query runs from somewhere else.
                    if (Mathf.Abs(hit.position.y - position.y) >= MapKitCatalogue.StoreyMetres * 0.5f)
                    {
                        report.SnapCrossedStorey.Add(
                            name + " " + position.ToString("0.0") + " → " + hit.position.ToString("0.0"));
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

            // One island per storey is legal, because cross-storey pairs are no longer
            // unioned — see the pair loop. Counting the distinct floor heights rather than
            // taking a number from the caller keeps the rule with the thing it describes: a
            // five-storey co-operative map allows five, an eight-storey tower eight, and a
            // single-floor test scene still allows exactly one.
            //
            // Counted on the AUTHORED height. A storey is a decision the generator made, so
            // reading it back off a snapped position asks the bake how many floors it thinks
            // it built — and the bake is the thing under test.
            var storeys = new List<float>();
            foreach (var point in snapped)
            {
                if (!storeys.Exists(y => Mathf.Abs(y - point.Authored.y) < MapKitCatalogue.StoreyMetres * 0.5f))
                {
                    storeys.Add(point.Authored.y);
                }
            }

            // Deepest last, so index 0 is the floor the runners start on and the label this
            // report prints matches the B1…B8 everyone says out loud. §01: the tower descends.
            storeys.Sort((a, b) => b.CompareTo(a));

            report.IslandsAllowed = Mathf.Max(1, storeys.Count);

            var path = new NavMeshPath();
            for (var i = 0; i < snapped.Count; i++)
            {
                for (var j = i + 1; j < snapped.Count; j++)
                {
                    // Only markers on the SAME STOREY are asked to reach each other. §01's
                    // tower joins its floors with 투하구 — one-way holes — and a hole is not
                    // walkable surface, so a cross-storey pair is unreachable by construction
                    // and counting it measures the design rather than a defect. Whole-building
                    // it reported 14.5% complete on a map with nothing wrong with it.
                    //
                    // On a map whose storeys ARE joined by stairs this changes nothing: the
                    // stairwell is walkable, so those pairs were complete anyway and every
                    // pair inside a floor is still judged. What is no longer asserted is that
                    // you can WALK from B1 to B8, which in this game you cannot, on purpose.
                    //
                    // Judged on where the generator PUT the markers, not on where the snap
                    // left them. Deciding it on snapped positions let a marker that fell
                    // through its own missing floor be paired with — and unioned into — the
                    // floor below, which merges two islands and hides the hole that put it
                    // there. That is why B5, B6 and B7 markers were sharing an island on seed
                    // 20260802 while this loop was supposedly never crossing a storey.
                    if (!SameStorey(snapped[i].Authored, snapped[j].Authored))
                    {
                        continue;
                    }

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

            MeasureMonsterReach(snapped, storeys, report);
            return report;
        }

        /// <summary>
        /// §06's own question, asked once of every storey: from where a creature starts,
        /// can it reach everywhere on its floor that this map has evidence of?
        /// <para>
        /// <b>Why per storey, and why "everywhere on its floor".</b> §12-B lists 「괴물이
        /// 안쪽을 순찰한다」 as one of the four things that make the way to a floor's middle
        /// hard, and it is written about the ring structure EVERY storey has — 외곽 is safe,
        /// 중심 is dangerous, and 1등을 하려면 위험을 지나야 한다. DESCENT-PIVOT §2③ says the
        /// same thing in the same shape: 「괴물은 중심에서 시작해 안쪽 두 고리를 돈다」. Neither
        /// is a claim about one floor of eight; both describe a hazard a runner meets on the
        /// floor they are running.
        /// </para>
        /// <para>
        /// <b>It cannot follow anybody down.</b> §12-C: the 투하구 are the only vertical
        /// connection and 「낙하는 되돌릴 수 없다」. §12-D's new validation table adds no link,
        /// ladder or stair. So a cross-storey reach query measures the design, not a defect —
        /// whole-building it reported 0 of 90 on a map with nothing wrong with it. And the
        /// remedy that would make it true — an off-mesh link across a chute — is the exact
        /// shape of B-001: <c>NavMesh.CalculatePath</c> routes through a link and
        /// <c>MonsterBrain</c> steps along <c>NavMeshPath.corners</c>, so the link turns this
        /// report green without moving the creature one metre. See <see cref="Report.Describe"/>.
        /// </para>
        /// <para>
        /// <b>What the design does NOT say is how many creatures there are</b>, and this
        /// audit does not decide it. §06, §07, §11 and §12 never give a count;
        /// <c>DescentMap.PlaceStarts</c> places exactly one, at the middle of B5, for a
        /// reason of its own ("the descent gets more dangerous rather than starting that
        /// way") that leaves seven storeys — including B6, B7 and B8, where §01 says the
        /// race is decided — with no antagonist at all, which is what §12-B's third lever
        /// says a floor must have. That contradiction is a design gap. So every storey is
        /// asked, and a storey with no MonsterSpawn is NAMED in the report rather than
        /// averaged away: the previous version filtered targets to the creature's own floor
        /// and then printed "3/3 player spawns and 후보 지점 reachable", on a scene whose 65
        /// PlayerSpawn markers are all on B1 (y 0.0) and whose only creature is on B5
        /// (y −15.0) — three 후보 지점 and ZERO player spawns, under a line that claimed both.
        /// B-008 in docs/BLOCKERS.md is the same shape: "monster reach 19/19" on a building
        /// four fifths of which no player could stand in. Both numbers were true; neither
        /// answered the question printed beside it.
        /// </para>
        /// <para>
        /// <b>Targets are every other marker on the storey</b>, not just spawns and 후보
        /// 지점. In a race the runner crosses the whole floor, and <c>DescentMap.MarkPlaces</c>
        /// says out loud that a storey's 후보 지점 and its 전리품 are the ONLY probes this
        /// audit collects there — so anything narrower measures less of the floor than the
        /// map has evidence for.
        /// </para>
        /// </summary>
        /// <param name="snapped">Markers, each carrying where it was authored and where it snapped.</param>
        /// <param name="storeys">Distinct authored floor heights, deepest last.</param>
        /// <param name="report">Filled in.</param>
        private static void MeasureMonsterReach(
            IReadOnlyList<(string Name, Vector3 Authored, Vector3 Position)> snapped,
            IReadOnlyList<float> storeys,
            Report report)
        {
            var path = new NavMeshPath();
            var origins = new List<int>();
            var targets = new List<int>();

            for (var s = 0; s < storeys.Count; s++)
            {
                var label = StoreyLabel(s, storeys[s]);
                origins.Clear();
                targets.Clear();

                for (var i = 0; i < snapped.Count; i++)
                {
                    if (!SameStorey(snapped[i].Authored.y, storeys[s]))
                    {
                        continue;
                    }

                    if (snapped[i].Name.IndexOf("MonsterSpawn", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        origins.Add(i);
                    }
                    else
                    {
                        targets.Add(i);
                    }
                }

                if (origins.Count == 0)
                {
                    report.StoreysWithoutCreature.Add(label + ", " + targets.Count + " markers");
                    continue;
                }

                report.StoreysWithCreature++;

                foreach (var origin in origins)
                {
                    for (var t = 0; t < targets.Count; t++)
                    {
                        report.MonsterTargets++;
                        NavMesh.CalculatePath(
                            snapped[origin].Position, snapped[targets[t]].Position, NavMesh.AllAreas, path);
                        if (path.status == NavMeshPathStatus.PathComplete)
                        {
                            report.MonsterReached++;
                        }
                        else
                        {
                            report.MonsterUnreachable.Add(label + " " + snapped[targets[t]].Name);
                        }
                    }
                }
            }

            if (report.StoreysWithCreature == 0)
            {
                report.Notes.Add(
                    "No MonsterSpawn marker on any storey, so §06's own question could not be asked of this scene.");
            }
        }

        /// <summary>
        /// What to call a storey in the report. Index 0 is the highest floor, which on
        /// §01's tower is B1 — the rim the twenty runners start on.
        /// <para>
        /// The height is printed beside it because the label is derived, not read: a scene
        /// this audit has never seen may not be a 하강 tower at all, and "B3 (y −7.5)" can be
        /// checked against the scene view while "B3" alone has to be trusted.
        /// </para>
        /// </summary>
        private static string StoreyLabel(int index, float y) =>
            "B" + (index + 1) + " (y " + y.ToString("0.0") + ")";

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

        /// <summary>
        /// True when two points are on the same storey.
        /// <para>
        /// Half a storey of separation, so a ramp, a kerb or a sunken bay stays on its own
        /// floor and only a real level change reads as one. <c>MapKitCatalogue.StoreyMetres</c>
        /// is 3.75 m.
        /// </para>
        /// </summary>
        private static bool SameStorey(Vector3 a, Vector3 b) => SameStorey(a.y, b.y);

        /// <summary>True when two heights are on the same storey. See <see cref="SameStorey(Vector3,Vector3)"/>.</summary>
        private static bool SameStorey(float a, float b) =>
            Mathf.Abs(a - b) < MapKitCatalogue.StoreyMetres * 0.5f;

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

            /// <summary>
            /// Markers tested from a MonsterSpawn on their own storey — every marker on a
            /// storey that has a creature, excluding the creature's own marker.
            /// </summary>
            public int MonsterTargets;

            /// <summary>How many of those the monster can path to.</summary>
            public int MonsterReached;

            /// <summary>Storeys that have at least one MonsterSpawn on them.</summary>
            public int StoreysWithCreature;

            /// <summary>Markers that are not on the navigable surface at all.</summary>
            public readonly List<string> OffSurface = new List<string>();

            /// <summary>Targets §06's monster cannot reach from a spawn on the same storey.</summary>
            public readonly List<string> MonsterUnreachable = new List<string>();

            /// <summary>
            /// Storeys with no MonsterSpawn, and how many markers each of them holds.
            /// <para>
            /// Reported, not failed. §12-B wants a creature patrolling every floor's inner
            /// rings and no section of the design says how many creatures a building has, so
            /// this audit refuses to decide it — see <see cref="MeasureMonsterReach"/>. What
            /// it will not do is print a reach figure that quietly excluded them.
            /// </para>
            /// </summary>
            public readonly List<string> StoreysWithoutCreature = new List<string>();

            /// <summary>
            /// Markers whose snap moved them onto another storey's surface, authored → snapped.
            /// <para>
            /// A hole in the marker's OWN floor: <see cref="SampleRadius"/> is wider than
            /// <c>MapKitCatalogue.StoreyMetres</c>, so a marker with no surface under it finds
            /// the floor above or below instead. Everything else this report says about such a
            /// marker is measured from a position on the wrong floor.
            /// </para>
            /// </summary>
            public readonly List<string> SnapCrossedStorey = new List<string>();

            /// <summary>Marker names grouped by island, largest first.</summary>
            public readonly List<List<string>> IslandMembers = new List<List<string>>();

            /// <summary>Free-text observations.</summary>
            public readonly List<string> Notes = new List<string>();

            private readonly List<string> _breaks = new List<string>();

            /// <summary>Fraction of pairs fully connected.</summary>
            public float CompletionRate => Pairs > 0 ? Complete / (float)Pairs : 0f;

            /// <summary>
            /// True when the surface is whole enough to ship.
            /// <para>
            /// <b>One island was the right test for a building joined by stairs. It is the
            /// wrong test for §01's tower.</b> The storeys are joined by 투하구 — one-way
            /// holes — and a hole is not walkable surface. Eight floors with nothing to walk
            /// between them are eight islands BY CONSTRUCTION, and demanding one would mean
            /// putting stairs back into a game whose whole shape is that you cannot climb.
            /// </para>
            /// <para>
            /// So the requirement is per storey: every floor whole, and the creature able to
            /// reach everything on ITS OWN floor. That is also the honest reading of §06 in a
            /// race — the creature guards a level and you escape it by falling past it, which
            /// is why <c>DescentMap</c> starts it halfway down rather than at the finish.
            /// </para>
            /// <para>
            /// <see cref="IslandsAllowed"/> is set by the caller from the storey count. A map
            /// that leaves it at 1 gets the old test, so nothing about the co-operative map
            /// changed.
            /// </para>
            /// <para>
            /// <b><see cref="MonsterUnreachable"/> is part of the test, because the paragraph
            /// above says it is.</b> It said "the creature able to reach everything on ITS OWN
            /// floor" and then did not check it: the §06 figure was printed and dropped, so an
            /// audit could pass with the creature walled into a corner of its own storey. That
            /// is the same class of defect as the figure itself measuring nothing. A storey
            /// with no creature contributes no targets and so cannot fail this — it is named in
            /// <see cref="StoreysWithoutCreature"/> instead, because how many creatures a
            /// building has is a design question and not this audit's to answer.
            /// </para>
            /// </summary>
            public bool Passed =>
                Pairs > 0
                && CompletionRate >= RequiredCompletionRate
                && Islands <= IslandsAllowed
                && OffSurface.Count == 0
                && MonsterUnreachable.Count == 0;

            /// <summary>
            /// How many disconnected pieces of surface are legal. One unless the map joins
            /// its storeys with something that is not walkable — see <see cref="Passed"/>.
            /// </summary>
            public int IslandsAllowed { get; set; } = 1;

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

                // Spelled out rather than shortened, because the line this replaced read
                // "3/3 player spawns and 후보 지점 reachable from MonsterSpawn" on a scene with
                // no player spawn on the creature's storey at all. Say which floors were asked
                // and how many markers were on them, and the same mistake is unmissable.
                var storeysTotal = StoreysWithCreature + StoreysWithoutCreature.Count;
                if (MonsterTargets > 0)
                {
                    sb.AppendLine($"  monster reach    {MonsterReached}/{MonsterTargets} markers reachable from a "
                        + $"MonsterSpawn on the SAME storey, over {StoreysWithCreature} of {storeysTotal} "
                        + "storeys (§06)");
                }

                if (StoreysWithoutCreature.Count > 0)
                {
                    sb.AppendLine($"  no creature on   {StoreysWithoutCreature.Count} of {storeysTotal} storeys, "
                        + "so §06 was not asked of them: " + string.Join("; ", StoreysWithoutCreature.Take(10)));
                    sb.AppendLine("                   §12-B wants a creature patrolling every floor's inner rings "
                        + "and no section gives a count — DescentMap places one. Design gap, not a bake fault.");
                }

                if (MonsterUnreachable.Count > 0)
                {
                    sb.AppendLine("  the monster cannot reach: " + string.Join(", ", MonsterUnreachable.Take(10)));
                }

                if (SnapCrossedStorey.Count > 0)
                {
                    sb.AppendLine($"  snapped onto another storey  {SnapCrossedStorey.Count}: "
                        + string.Join(", ", SnapCrossedStorey.Take(6)));
                    sb.AppendLine("                   each of these has no walkable surface within "
                        + (MapKitCatalogue.StoreyMetres * 0.5f).ToString("0.00")
                        + " m of where it was authored — a hole in its OWN floor.");
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
