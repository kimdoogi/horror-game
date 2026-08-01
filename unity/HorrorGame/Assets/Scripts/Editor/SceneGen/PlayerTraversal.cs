#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// Checks that a <em>player</em> can walk the building, by sweeping the real
    /// capsule through it.
    /// <para>
    /// <see cref="NavMeshConnectivity"/> is the same check for the monster, and the two
    /// are not interchangeable. The NavMesh agent this project bakes climbs
    /// <c>agentClimb</c> 0.75 m; the player's <c>CharacterController</c> climbs
    /// <c>stepOffset</c> 0.40 m. Any surface between those two numbers is a stair only
    /// the antagonist can use, and every gate in the project reads green while it is
    /// there: the audit reports 100 % with one island because the audit is measuring
    /// the other body.
    /// </para>
    /// <para>
    /// That is B-001's shape a second time, and it is the reason this file does not
    /// consult the NavMesh at all. It floods a grid of capsule positions outward from
    /// the 출입구 using <see cref="Physics"/> sweeps, which is the same collide-and-slide
    /// the controller does at runtime, and answers one question: from the door a player
    /// walks in through, which 후보 지점, which 전리품 spawn and which storey can that
    /// player's body actually get to? Trusting the baked surface here would defeat the
    /// entire point, because the whole premise is that the two disagree.
    /// </para>
    /// <para>
    /// Height is measured as well as step, because it fails the same way. The capsule
    /// is <c>ViewMotionTuning.RigHeightMetres</c> tall; a beam at 1.60 m stops it dead,
    /// produces no error, bakes a perfectly good NavMesh underneath itself — the agent
    /// is 2.00 m but Recast only needs the <em>agent's</em> clearance — and reads to a
    /// player as a corridor that mysteriously will not let them through.
    /// </para>
    /// <para>
    /// Like <see cref="NavMeshConnectivity"/> this lives in the scene-generation
    /// assembly so that <see cref="MapSceneGenerator"/> can refuse to write a map that
    /// fails it. A building four of whose five storeys the player cannot reach is not a
    /// level, and it must not be possible to save one.
    /// </para>
    /// </summary>
    public static class PlayerTraversal
    {
        /// <summary>
        /// Horizontal sample pitch, metres.
        /// <para>
        /// Under <c>STAIR_GOING</c> (0.30 m in <c>tools/blender/gen_mapkit.py</c>) so
        /// that every tread of a 계단 gets at least one sample of its own — a pitch
        /// equal to the going could stride a whole flight in one hop and never test a
        /// single riser.
        /// </para>
        /// </summary>
        public const float SampleMetres = 0.25f;

        /// <summary>
        /// How much of the controller's <c>stepOffset</c> this check will actually
        /// spend, metres.
        /// <para>
        /// A step at exactly <c>stepOffset</c> is not reliably climbable: the controller
        /// resolves the move against the surface normal after the sweep, so a nosing, a
        /// bevel or a millimetre of float error turns a step that measures 0.400 m into
        /// one the player catches on. Geometry that only just fits is geometry that
        /// fails on someone's machine, so the gate spends
        /// <c>stepOffset − <see cref="ClimbMarginMetres"/></c> and the modelled riser
        /// (0.2344 m) clears that with room to spare.
        /// </para>
        /// </summary>
        public const float ClimbMarginMetres = 0.05f;

        /// <summary>
        /// How far a target marker may sit from the nearest place the capsule can
        /// stand, metres, horizontally.
        /// <para>
        /// Markers are authored at cell centres and the sample grid is
        /// <see cref="SampleMetres"/>, so this only has to cover half a cell plus the
        /// capsule radius. Deliberately far tighter than
        /// <c>NavMeshConnectivity</c>'s 4 m snap: that radius crosses a §12 grid cell,
        /// which is how a marker can pass by landing on the floor of somewhere else.
        /// </para>
        /// </summary>
        public const float ReachRadiusMetres = 1.25f;

        /// <summary>Vertical slack on <see cref="ReachRadiusMetres"/>, metres — a marker may sit on a plinth.</summary>
        public const float ReachHeightMetres = 1.6f;

        /// <summary>Guard against flooding an unbounded scene. Far above a five-storey map.</summary>
        private const int MaxNodes = 900_000;

        /// <summary>Clearance left under the capsule so a sweep does not graze its own floor, metres.</summary>
        private const float Skin = 0.03f;

        private static readonly Regex ZoneStorey = new Regex(@"^Zone_.*_B(\d+)_", RegexOptions.Compiled);
        private static readonly Regex TileStorey = new Regex(@"_L(\d+)_-?\d+_-?\d+$", RegexOptions.Compiled);

        /// <summary>
        /// Where along a step the capsule is allowed to come to rest, metres.
        /// <para>
        /// Spans more than one <c>STAIR_GOING</c> either way so that a flight is never
        /// judged by whether the sample grid happens to align with its treads. See
        /// <see cref="TryStep"/>.
        /// </para>
        /// </summary>
        private static readonly float[] Settle =
        {
            0f, -0.05f, 0.05f, -0.10f, 0.10f, -0.15f, 0.15f, -0.20f, 0.20f,
            -0.25f, 0.25f, -0.30f, 0.30f, -0.35f, 0.35f,
        };

        private static readonly Vector3[] Steps =
        {
            new Vector3(SampleMetres, 0f, 0f),
            new Vector3(-SampleMetres, 0f, 0f),
            new Vector3(0f, 0f, SampleMetres),
            new Vector3(0f, 0f, -SampleMetres),
        };

        /// <summary>Sweeps the player's capsule out from the 출입구 and reports what it can reach.</summary>
        public static Report Audit(Scene scene)
        {
            var report = new Report();
            report.Body = PlayerBody.Measure(report);

            Physics.SyncTransforms();

            var markers = CollectMarkers(scene, report);
            if (report.Start == null)
            {
                report.Notes.Add(
                    "No 출입구 found. §02 makes leaving the building the win condition, so a scene without one "
                    + "cannot be measured from the door a player comes in through — is this the generated map?");
                return report;
            }

            foreach (var storey in StoreysIn(scene))
            {
                report.Storeys[storey] = new StoreyResult();
            }

            var reached = Flood(report.Start.Value, report.Body, report);
            report.NodeCount = reached.Count;

            Evaluate(markers, reached, report);
            MeasureHeadroom(reached, report.Body, report);
            MeasureStairs(scene, reached, report);
            return report;
        }

        /// <summary>
        /// Every 계단, and how far down it the capsule actually got.
        /// <para>
        /// A stairwell is the only vertical route in the building — §12 makes changing
        /// storey a deliberate act and the generator emits no <c>NavMeshLink</c> — so
        /// "which storeys can the player reach" is nearly always answered by one of
        /// these, and a per-shaft trace turns "B2 unreachable" into a height and a
        /// coordinate someone can go and stand next to.
        /// </para>
        /// </summary>
        private static void MeasureStairs(Scene scene, List<Vector3> reached, Report report)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (!transform.name.StartsWith("StairwellMetal", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var renderers = transform.GetComponentsInChildren<Renderer>(includeInactive: true);
                    if (renderers.Length == 0)
                    {
                        continue;
                    }

                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers)
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }

                    var shaft = new Shaft(transform.name, bounds);
                    foreach (var node in reached)
                    {
                        if (bounds.Contains(node))
                        {
                            shaft.Note(node);
                        }
                    }

                    shaft.Worst = report.WorstBlockIn(bounds, shaft.Frontier);
                    report.Shafts.Add(shaft);
                }
            }
        }

        // ====================================================================
        // The flood. This is the whole measurement.
        // ====================================================================

        /// <summary>
        /// Every foot position the capsule can occupy, walking out from
        /// <paramref name="start"/>.
        /// <para>
        /// The move rule is the controller's, not the pathfinder's: sweep the capsule
        /// horizontally; if that is blocked, lift it by the usable step and sweep again;
        /// then drop onto whatever is under the destination and require the capsule to
        /// stand there. A move is only taken when it works in both directions — a rise
        /// and a fall are both held to the same limit — so what comes back is a route a
        /// player can walk <em>and</em> walk back, which is what §02's escape needs.
        /// </para>
        /// </summary>
        private static List<Vector3> Flood(Vector3 start, PlayerBody body, Report report)
        {
            var usable = Mathf.Max(0.05f, body.StepOffset - ClimbMarginMetres);
            var nodes = new List<Vector3>();
            var seen = new Dictionary<long, int>(1 << 16);
            var queue = new Queue<int>();

            if (!TryStand(start, body, out var footing))
            {
                report.Notes.Add(
                    "The 출입구 at " + start.ToString("F2") + " is not a place the player's capsule can stand, "
                    + "so nothing could be measured from it. That is itself the defect.");
                return nodes;
            }

            seen[Key(footing)] = 0;
            nodes.Add(footing);
            queue.Enqueue(0);

            while (queue.Count > 0)
            {
                var from = nodes[queue.Dequeue()];

                foreach (var step in Steps)
                {
                    if (TryStep(from, step, body, usable, seen, out var to, out var block))
                    {
                        var key = Key(to);

                        if (nodes.Count >= MaxNodes)
                        {
                            report.Notes.Add(
                                "Stopped at " + MaxNodes + " sample points. The scene is larger than this check "
                                + "was sized for, so the result below is a lower bound, not a verdict.");
                            report.Truncated = true;
                            queue.Clear();
                            break;
                        }

                        seen[key] = nodes.Count;
                        nodes.Add(to);
                        queue.Enqueue(nodes.Count - 1);
                        report.NoteClimb(to.y - from.y);
                    }
                    else
                    {
                        report.RecordBlock(block);
                    }
                }
            }

            return nodes;
        }

        /// <summary>
        /// One capsule move. False with <paramref name="block"/> describing why, which
        /// is the half of this file anyone reading a failure actually needs.
        /// <para>
        /// The move is tried at a range of distances rather than at one, and that is
        /// not a fudge — it is what the controller does. <c>CharacterController.Move</c>
        /// sweeps, resolves against the surface it hit and depenetrates, so the body a
        /// player ends a step in is not the body a fixed sample grid would put there.
        /// On a 계단 it decides everything: a tread is <c>STAIR_GOING</c> deep and the
        /// capsule only fits in the <c>PLAYER_TREAD_CLEAR</c> of it nearest the nose,
        /// so a grid whose pitch does not divide the going lands inside the next riser
        /// on most treads and would report a perfectly good flight as a wall. The
        /// search spans more than one going either way, so a standing place is found
        /// whenever one exists along the step.
        /// </para>
        /// </summary>
        private static bool TryStep(
            Vector3 from, Vector3 offset, PlayerBody body, float usable,
            Dictionary<long, int> seen, out Vector3 to, out Block block)
        {
            to = default;
            block = Block.Wall(from + offset, string.Empty);
            var held = false;

            var direction = offset.normalized;
            var nominal = offset.magnitude;

            foreach (var nudge in Settle)
            {
                var travel = nominal + nudge;
                if (travel < SampleMetres * 0.25f)
                {
                    continue;
                }

                // Sweep for this distance specifically. Sweeping the nominal distance
                // once and then pulling the capsule back is what a first version of
                // this did, and it fails on exactly the geometry it exists to measure:
                // the lifted capsule overshoots into the riser above and the whole move
                // is refused before the settle can rescue it.
                var lift = 0f;
                if (!SweepClear(from, 0f, direction, travel, body, out _))
                {
                    if (!SweepClear(from, usable, direction, travel, body, out var struck))
                    {
                        Keep(ref block, ref held, Classify(from, direction * travel, body, usable, struck));
                        continue;
                    }

                    lift = usable;
                }

                var target = from + (direction * travel);

                // A step longer than the sample pitch must not vault a hole. The sweep
                // only proves the air is clear; this proves there is floor under the
                // middle of it too.
                if (travel > SampleMetres && !Supported(from, direction, travel * 0.5f, body, usable))
                {
                    Keep(ref block, ref held, Block.Void(from + (direction * travel * 0.5f)));
                    continue;
                }

                // Drop onto whatever is under the destination. Started from the lifted
                // height so a step up is found, and carried one usable step below the
                // origin so a step down is found too.
                var probe = target + (Vector3.up * (lift + Skin));
                if (!Physics.Raycast(probe, Vector3.down, out var ground, lift + Skin + usable + Skin,
                        ~0, QueryTriggerInteraction.Ignore))
                {
                    Keep(ref block, ref held, Block.Void(target));
                    continue;
                }

                var rise = ground.point.y - from.y;
                if (rise > usable)
                {
                    Keep(ref block, ref held, Block.Step(ground.point, rise, NameOf(ground.collider)));
                    continue;
                }

                if (rise < -usable)
                {
                    Keep(ref block, ref held, Block.Drop(ground.point, -rise, NameOf(ground.collider)));
                    continue;
                }

                var pitch = Vector3.Angle(ground.normal, Vector3.up);
                if (pitch > body.SlopeLimit)
                {
                    Keep(ref block, ref held, Block.Slope(ground.point, pitch, NameOf(ground.collider)));
                    continue;
                }

                if (!TryStand(ground.point + (Vector3.up * Skin), body, out to))
                {
                    // (falls through to the classification below)
                    // The capsule reached the destination but cannot stand up in it.
                    // That is a soffit only when there is genuinely less than a body of
                    // air over the spot; otherwise it is an ordinary obstruction beside
                    // the feet, and calling it headroom would send whoever reads the
                    // report to look at a ceiling that is fine.
                    var clearance = ClearanceAt(ground.point, body);
                    Keep(ref block, ref held, clearance < body.Height
                        ? Block.Headroom(ground.point, clearance, CeilingAt(ground.point, body))
                        : Block.Wall(ground.point, CrowdingAt(ground.point, body)));
                    continue;
                }

                // A settle that lands somewhere already flooded is not progress, and
                // accepting it here would end the move: the caller would see a place it
                // has been and drop the step, never trying the longer candidates that
                // reach the next tread. This is what silently confined the player to
                // one flight of every 계단 — the capsule shuffled 0.15 m on the tread it
                // was already standing on and the descent was abandoned.
                if (seen.ContainsKey(Key(to)))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>Whether there is floor within a step of the origin's height part way along a move.</summary>
        private static bool Supported(Vector3 from, Vector3 direction, float distance, PlayerBody body, float usable)
        {
            var at = from + (direction * distance) + (Vector3.up * (usable + Skin));
            return Physics.Raycast(at, Vector3.down, out var hit, usable + Skin + usable + Skin,
                       ~0, QueryTriggerInteraction.Ignore)
                   && Mathf.Abs(hit.point.y - from.y) <= usable + Skin;
        }

        /// <summary>
        /// Keeps the first real reason a settle attempt failed. A later attempt that
        /// merely fell off the edge of the world should not overwrite "a 0.63 m step".
        /// </summary>
        private static void Keep(ref Block block, ref bool held, Block candidate)
        {
            if (held && candidate.Kind == BlockKind.Void)
            {
                return;
            }

            if (!held || (block.Kind == BlockKind.Void && candidate.Kind != BlockKind.Void))
            {
                block = candidate;
                held = true;
            }
        }

        /// <summary>Why a blocked move was blocked, measured rather than guessed.</summary>
        private static Block Classify(
            Vector3 from, Vector3 offset, PlayerBody body, float usable, string struck)
        {
            var destination = from + offset;
            var head = from + (Vector3.up * body.Height);

            if (Physics.Raycast(new Vector3(destination.x, head.y, destination.z), Vector3.down,
                    out var ground, body.Height + usable, ~0, QueryTriggerInteraction.Ignore))
            {
                var rise = ground.point.y - from.y;
                if (rise > usable)
                {
                    return Block.Step(ground.point, rise, NameOf(ground.collider));
                }

                var clearance = ClearanceAt(ground.point, body);
                if (clearance < body.Height)
                {
                    return Block.Headroom(ground.point, clearance, struck);
                }
            }

            return Block.Wall(destination, struck);
        }

        /// <summary>
        /// Capsule sweep from a foot position lifted by <paramref name="lift"/>.
        /// <paramref name="struck"/> names whatever stopped it, because "something is in
        /// the way" is not a bug report and the name of a scene object is.
        /// </summary>
        private static bool SweepClear(
            Vector3 foot, float lift, Vector3 direction, float distance, PlayerBody body, out string struck)
        {
            var bottom = foot + (Vector3.up * (lift + body.Radius + Skin));
            var top = foot + (Vector3.up * (lift + body.Height - body.Radius));
            if (Physics.CapsuleCast(bottom, top, body.Radius, direction, out var hit, distance,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                struck = NameOf(hit.collider);
                return false;
            }

            struck = string.Empty;
            return true;
        }

        /// <summary>A collider's name, prefixed by its piece, which is what a reader needs to find it.</summary>
        private static string NameOf(Collider? collider)
        {
            if (collider == null)
            {
                return "nothing";
            }

            var owner = collider.transform;
            var piece = owner.parent == null ? owner : owner.parent;
            return piece.name + "/" + owner.name;
        }

        /// <summary>Whether the capsule fits with its feet on <paramref name="foot"/>, and where that puts it.</summary>
        private static bool TryStand(Vector3 foot, PlayerBody body, out Vector3 standing)
        {
            standing = foot;

            // Settle onto the surface first: a marker or a start point is authored at
            // floor level and a millimetre either way decides whether the capsule is
            // intersecting the floor or hovering over it.
            if (Physics.Raycast(foot + (Vector3.up * body.Height), Vector3.down, out var ground,
                    body.Height + 1.0f, ~0, QueryTriggerInteraction.Ignore))
            {
                standing = ground.point;
            }

            var bottom = standing + (Vector3.up * (body.Radius + Skin));
            var top = standing + (Vector3.up * (body.Height - body.Radius));
            return !Physics.CheckCapsule(bottom, top, body.Radius, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>What is over a foot position, by name.</summary>
        private static string CeilingAt(Vector3 foot, PlayerBody body) =>
            Physics.Raycast(foot + (Vector3.up * Skin), Vector3.up, out var hit, body.Height + 1.5f,
                ~0, QueryTriggerInteraction.Ignore)
                ? NameOf(hit.collider)
                : "nothing";

        /// <summary>What the capsule overlaps when it tries to stand on a foot position, by name.</summary>
        private static string CrowdingAt(Vector3 foot, PlayerBody body)
        {
            var bottom = foot + (Vector3.up * (body.Radius + Skin));
            var top = foot + (Vector3.up * (body.Height - body.Radius));
            var hits = Physics.OverlapCapsule(bottom, top, body.Radius, ~0, QueryTriggerInteraction.Ignore);
            return hits.Length == 0 ? "nothing" : NameOf(hits[0]);
        }

        /// <summary>Metres of clear air over a foot position, capped where it stops mattering.</summary>
        private static float ClearanceAt(Vector3 foot, PlayerBody body)
        {
            var from = foot + (Vector3.up * Skin);
            var limit = body.Height + 1.5f;
            return Physics.Raycast(from, Vector3.up, out var hit, limit, ~0, QueryTriggerInteraction.Ignore)
                ? hit.distance + Skin
                : limit;
        }

        // ====================================================================
        // Targets, storeys and the verdict.
        // ====================================================================

        /// <summary>
        /// Everywhere §12 and §08 send a player: 후보 지점, 전리품 spawns and the spawns
        /// they start on. The monster's own markers are deliberately not here — this is
        /// the other actor.
        /// </summary>
        private static List<Marker> CollectMarkers(Scene scene, Report report)
        {
            var markers = new List<Marker>();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    var name = transform.name;

                    if (name.StartsWith("EntranceLight", StringComparison.Ordinal))
                    {
                        // The fitting hangs under the soffit; the 출입구 itself is the
                        // floor below it. BuildLight is the one place that offset is
                        // applied, so it is undone here rather than re-derived.
                        var floor = transform.position
                            - (Vector3.up * (MapKitCatalogue.CorridorClearWidth + 0.6f));
                        report.Start ??= floor;
                        report.StartName = name;
                        continue;
                    }

                    if (transform.childCount != 0)
                    {
                        continue;
                    }

                    if (name.StartsWith("CandidateSite", StringComparison.Ordinal))
                    {
                        markers.Add(new Marker(name, transform.position, MarkerKind.CandidateSite));
                    }
                    else if (name.StartsWith("LootSpawn", StringComparison.Ordinal))
                    {
                        markers.Add(new Marker(name, transform.position, MarkerKind.LootSpawn));
                    }
                    else if (name.StartsWith("PlayerSpawn", StringComparison.Ordinal))
                    {
                        markers.Add(new Marker(name, transform.position, MarkerKind.PlayerSpawn));
                    }
                }
            }

            return markers;
        }

        /// <summary>
        /// Which storeys the building has. Read off the scene rather than assumed,
        /// because a storey with no marker on it still has to be reachable — that is
        /// exactly the failure this file exists for.
        /// </summary>
        private static SortedSet<int> StoreysIn(Scene scene)
        {
            var storeys = new SortedSet<int>();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    var zone = ZoneStorey.Match(transform.name);
                    if (zone.Success
                        && int.TryParse(zone.Groups[1].Value, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var basement))
                    {
                        storeys.Add(basement - 1);
                        continue;
                    }

                    var tile = TileStorey.Match(transform.name);
                    if (tile.Success
                        && int.TryParse(tile.Groups[1].Value, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var level))
                    {
                        storeys.Add(level);
                    }
                }
            }

            return storeys;
        }

        /// <summary>Storey a world position belongs to. <see cref="MapKitCatalogue.FloorY"/>, inverted.</summary>
        private static int StoreyOf(float y) => Mathf.RoundToInt(-y / MapKitCatalogue.StoreyMetres);

        private static void Evaluate(List<Marker> markers, List<Vector3> reached, Report report)
        {
            foreach (var node in reached)
            {
                var storey = StoreyOf(node.y);
                if (!report.Storeys.TryGetValue(storey, out var result))
                {
                    result = new StoreyResult();
                    report.Storeys[storey] = result;
                }

                result.Standing++;
            }

            foreach (var marker in markers)
            {
                var storey = StoreyOf(marker.Position.y);
                if (!report.Storeys.TryGetValue(storey, out var result))
                {
                    result = new StoreyResult();
                    report.Storeys[storey] = result;
                }

                result.Targets++;

                var best = float.PositiveInfinity;
                foreach (var node in reached)
                {
                    var dy = Mathf.Abs(node.y - marker.Position.y);
                    if (dy > ReachHeightMetres)
                    {
                        continue;
                    }

                    var dx = node.x - marker.Position.x;
                    var dz = node.z - marker.Position.z;
                    var flat = Mathf.Sqrt((dx * dx) + (dz * dz));
                    if (flat < best)
                    {
                        best = flat;
                    }
                }

                if (best <= ReachRadiusMetres)
                {
                    result.Reached++;
                    report.Count(marker.Kind, true);
                    if (best > report.WorstReachedGap)
                    {
                        report.WorstReachedGap = best;
                        report.WorstReachedName = marker.Name;
                    }
                }
                else
                {
                    report.Count(marker.Kind, false);
                    report.Unreachable.Add(new Unreachable(marker, best, report.NearestBlockTo(marker.Position)));
                }
            }
        }

        /// <summary>
        /// The other way a 1.75 m body is stopped without an error. Reported over
        /// everywhere the player <em>can</em> stand, so a corridor that only just
        /// admits them shows up before someone finds it with their face.
        /// </summary>
        private static void MeasureHeadroom(List<Vector3> reached, PlayerBody body, Report report)
        {
            report.MinClearance = float.PositiveInfinity;

            for (var i = 0; i < reached.Count; i += 4)
            {
                var clearance = ClearanceAt(reached[i], body);
                if (clearance < report.MinClearance)
                {
                    report.MinClearance = clearance;
                    report.MinClearanceAt = reached[i];
                }
            }

            if (reached.Count == 0)
            {
                report.MinClearance = 0f;
            }
        }

        private static long Key(Vector3 p)
        {
            var ix = Mathf.RoundToInt(p.x / SampleMetres) + 0x40000;
            var iz = Mathf.RoundToInt(p.z / SampleMetres) + 0x40000;
            var iy = Mathf.RoundToInt(p.y / 0.10f) + 0x40000;
            return ((long)ix << 40) | ((long)iz << 20) | (uint)iy;
        }

        // ====================================================================
        // Types.
        // ====================================================================

        /// <summary>What kind of place a marker is, for counting the report by category.</summary>
        public enum MarkerKind
        {
            /// <summary>§12 후보 지점.</summary>
            CandidateSite,

            /// <summary>§08 전리품 spawn.</summary>
            LootSpawn,

            /// <summary>Where a player's body starts the match.</summary>
            PlayerSpawn,
        }

        /// <summary>Why the capsule could not take a step.</summary>
        public enum BlockKind
        {
            /// <summary>A wall. Expected everywhere and never reported.</summary>
            Wall,

            /// <summary>A surface higher than the controller's usable step. The failure this file was built for.</summary>
            Step,

            /// <summary>A soffit, beam or slab lower than the capsule is tall.</summary>
            Headroom,

            /// <summary>A fall further than the player can step back up.</summary>
            Drop,

            /// <summary>Steeper than the controller's slope limit.</summary>
            Slope,

            /// <summary>Nothing underneath at all.</summary>
            Void,
        }

        /// <summary>A blocked move, with the number that explains it.</summary>
        public readonly struct Block
        {
            private Block(BlockKind kind, Vector3 at, float metric, string what)
            {
                Kind = kind;
                At = at;
                Metric = metric;
                What = what;
            }

            /// <summary>The scene object that stopped the capsule.</summary>
            public string What { get; }

            /// <summary>What stopped the capsule.</summary>
            public BlockKind Kind { get; }

            /// <summary>Where, in world space.</summary>
            public Vector3 At { get; }

            /// <summary>Metres of rise, metres of clearance, metres of fall, or degrees of slope.</summary>
            public float Metric { get; }

            internal static Block Wall(Vector3 at, string what) => new Block(BlockKind.Wall, at, 0f, what);

            internal static Block Step(Vector3 at, float rise, string what) =>
                new Block(BlockKind.Step, at, rise, what);

            internal static Block Headroom(Vector3 at, float clearance, string what) =>
                new Block(BlockKind.Headroom, at, clearance, what);

            internal static Block Drop(Vector3 at, float fall, string what) =>
                new Block(BlockKind.Drop, at, fall, what);

            internal static Block Slope(Vector3 at, float degrees, string what) =>
                new Block(BlockKind.Slope, at, degrees, what);

            internal static Block Void(Vector3 at) => new Block(BlockKind.Void, at, 0f, "nothing");

            /// <summary>One line naming the surface and its measurement.</summary>
            public string Describe()
            {
                var where = " at " + At.ToString("F2")
                    + (string.IsNullOrEmpty(What) ? string.Empty : " (" + What + ")");

                switch (Kind)
                {
                    case BlockKind.Step:
                        return "a " + Metric.ToString("0.000", CultureInfo.InvariantCulture) + " m step" + where;
                    case BlockKind.Headroom:
                        return "a soffit leaving " + Metric.ToString("0.00", CultureInfo.InvariantCulture)
                            + " m of headroom" + where;
                    case BlockKind.Drop:
                        return "a " + Metric.ToString("0.00", CultureInfo.InvariantCulture) + " m drop" + where;
                    case BlockKind.Slope:
                        return "a " + Metric.ToString("0.0", CultureInfo.InvariantCulture) + "° slope" + where;
                    case BlockKind.Void:
                        return "no floor at all" + where;
                    default:
                        return "an obstruction" + where;
                }
            }
        }

        /// <summary>A place the game sends a player.</summary>
        public readonly struct Marker
        {
            internal Marker(string name, Vector3 position, MarkerKind kind)
            {
                Name = name;
                Position = position;
                Kind = kind;
            }

            /// <summary>Scene object name.</summary>
            public string Name { get; }

            /// <summary>World position.</summary>
            public Vector3 Position { get; }

            /// <summary>What the marker is for.</summary>
            public MarkerKind Kind { get; }
        }

        /// <summary>A marker the capsule could not get to, and the nearest reason why.</summary>
        public readonly struct Unreachable
        {
            internal Unreachable(Marker marker, float gap, Block? blame)
            {
                Marker = marker;
                Gap = gap;
                Blame = blame;
            }

            /// <summary>The marker.</summary>
            public Marker Marker { get; }

            /// <summary>Metres from the marker to the nearest place the capsule can stand.</summary>
            public float Gap { get; }

            /// <summary>The blocking surface nearest it, when one was measured.</summary>
            public Block? Blame { get; }
        }

        /// <summary>One 계단 and how much of its climb the capsule covered.</summary>
        public sealed class Shaft
        {
            internal Shaft(string name, Bounds bounds)
            {
                Name = name;
                Bounds = bounds;
            }

            /// <summary>Scene object name.</summary>
            public string Name { get; }

            /// <summary>World bounds of the piece.</summary>
            public Bounds Bounds { get; }

            /// <summary>Standing places inside the shaft.</summary>
            public int Standing { get; private set; }

            /// <summary>Highest of them.</summary>
            public float TopY { get; private set; } = float.NegativeInfinity;

            /// <summary>Lowest of them.</summary>
            public float BottomY { get; private set; } = float.PositiveInfinity;

            /// <summary>The lowest place the capsule got to inside the shaft.</summary>
            public Vector3 Bottom { get; private set; }

            /// <summary>The highest place the capsule got to inside the shaft.</summary>
            public Vector3 Top { get; private set; }

            /// <summary>
            /// The end of the climb the capsule stalled at — where to go and look.
            /// Whichever of its two landings is further from what was covered.
            /// </summary>
            public Vector3 Frontier =>
                Standing == 0
                    ? Bounds.center
                    : (TopY - (Bounds.min.y + MapKitCatalogue.StoreyMetres)) < (Bounds.min.y - BottomY)
                        ? Top
                        : Bottom;

            /// <summary>The most informative blocked move inside the shaft.</summary>
            public Block? Worst { get; internal set; }

            /// <summary>Metres of the climb the capsule covered.</summary>
            public float Covered => Standing == 0 ? 0f : TopY - BottomY;

            /// <summary>True when the capsule walked the whole storey the piece climbs.</summary>
            public bool Traversed => Covered >= MapKitCatalogue.StoreyMetres - 0.5f;

            internal void Note(Vector3 node)
            {
                Standing++;
                if (node.y > TopY)
                {
                    TopY = node.y;
                    Top = node;
                }

                if (node.y < BottomY)
                {
                    BottomY = node.y;
                    Bottom = node;
                }
            }
        }

        /// <summary>Per-storey tally.</summary>
        public sealed class StoreyResult
        {
            /// <summary>Sample points the capsule can occupy on this storey.</summary>
            public int Standing;

            /// <summary>Markers on this storey.</summary>
            public int Targets;

            /// <summary>How many of them the capsule can get to.</summary>
            public int Reached;

            /// <summary>A storey with nowhere to stand is a storey the player is locked out of.</summary>
            public bool Walkable => Standing > 0;
        }

        /// <summary>
        /// The player's body, read off the rig the game actually builds.
        /// <para>
        /// <c>PlayerFeelHarnessMenu.BuildRig</c> is the one place a player is assembled
        /// — the solo playtest scene builder calls it too — so the numbers are taken
        /// from a real <see cref="CharacterController"/> rather than restated here. A
        /// restated <c>stepOffset</c> is how a check ends up certifying a body nobody
        /// plays.
        /// </para>
        /// </summary>
        public sealed class PlayerBody
        {
            /// <summary>Capsule height, metres.</summary>
            public float Height { get; private set; } = 1.75f;

            /// <summary>Capsule radius, metres.</summary>
            public float Radius { get; private set; } = 0.3f;

            /// <summary>Step the controller climbs, metres.</summary>
            public float StepOffset { get; private set; } = 0.4f;

            /// <summary>Slope the controller walks, degrees.</summary>
            public float SlopeLimit { get; private set; } = 50f;

            /// <summary>Where the numbers came from, so a fallback is never silent.</summary>
            public string Source { get; private set; } = "documented defaults";

            /// <summary>Builds the real rig, reads its controller, and throws the rig away.</summary>
            internal static PlayerBody Measure(Report? report)
            {
                var body = new PlayerBody();
                GameObject? rig = null;

                try
                {
                    rig = HorrorGame.Gameplay.PlayerEditor.PlayerFeelHarnessMenu.BuildRig();
                    var controller = rig == null ? null : rig.GetComponent<CharacterController>();
                    if (controller != null)
                    {
                        body.Height = controller.height;
                        body.Radius = controller.radius;
                        body.StepOffset = controller.stepOffset;
                        body.SlopeLimit = controller.slopeLimit;
                        body.Source = "PlayerFeelHarnessMenu.BuildRig()";
                        return body;
                    }
                }
                catch (Exception error)
                {
                    report?.Notes.Add("The player rig could not be built (" + error.GetType().Name
                        + "), so the capsule below is this file's own copy of the controller's numbers. "
                        + "Two copies of a body drift; fix the rig rather than trusting this run.");
                }
                finally
                {
                    if (rig != null)
                    {
                        UnityEngine.Object.DestroyImmediate(rig);
                    }
                }

                report?.Notes.Add(
                    "PlayerFeelHarnessMenu.BuildRig() returned no CharacterController — usually a missing "
                    + "Assets/Models/Characters/Player.fbx. The capsule below is this file's own copy of the "
                    + "controller's numbers and can drift away from the one players use.");
                return body;
            }

            /// <summary>One line for the report header.</summary>
            public string Describe() =>
                "height " + Height.ToString("0.00", CultureInfo.InvariantCulture)
                + " m · radius " + Radius.ToString("0.00", CultureInfo.InvariantCulture)
                + " m · stepOffset " + StepOffset.ToString("0.00", CultureInfo.InvariantCulture)
                + " m · slopeLimit " + SlopeLimit.ToString("0", CultureInfo.InvariantCulture)
                + "°  (" + Source + ")";
        }

        /// <summary>What the capsule found.</summary>
        public sealed class Report
        {
            private readonly List<Block> _blocks = new List<Block>();
            private readonly List<Block> _walls = new List<Block>();

            /// <summary>The body that was swept.</summary>
            public PlayerBody Body { get; internal set; } = new PlayerBody();

            /// <summary>Where the sweep started — the 출입구 floor.</summary>
            public Vector3? Start { get; internal set; }

            /// <summary>Which object gave the start position.</summary>
            public string StartName { get; internal set; } = string.Empty;

            /// <summary>Foot positions the capsule can occupy.</summary>
            public int NodeCount { get; internal set; }

            /// <summary>True when the flood hit its own limit rather than the building's edges.</summary>
            public bool Truncated { get; internal set; }

            /// <summary>Tallies per storey, keyed by <see cref="MapKitCatalogue.FloorY"/>'s level index.</summary>
            public readonly SortedDictionary<int, StoreyResult> Storeys = new SortedDictionary<int, StoreyResult>();

            /// <summary>Markers the capsule cannot get to.</summary>
            public readonly List<Unreachable> Unreachable = new List<Unreachable>();

            /// <summary>Every 계단 in the scene and how far down it the capsule got.</summary>
            public readonly List<Shaft> Shafts = new List<Shaft>();

            /// <summary>Free-text observations.</summary>
            public readonly List<string> Notes = new List<string>();

            /// <summary>The tallest step the capsule actually climbed, metres.</summary>
            public float TallestClimb { get; private set; }

            /// <summary>Metres from the worst reachable marker to the nearest standing place.</summary>
            public float WorstReachedGap { get; internal set; }

            /// <summary>Which marker that was.</summary>
            public string WorstReachedName { get; internal set; } = string.Empty;

            /// <summary>Tightest headroom anywhere the player can stand, metres.</summary>
            public float MinClearance { get; internal set; }

            /// <summary>Where that was.</summary>
            public Vector3 MinClearanceAt { get; internal set; }

            /// <summary>후보 지점 reached / found.</summary>
            public int SitesReached { get; private set; }

            /// <summary>후보 지점 found.</summary>
            public int Sites { get; private set; }

            /// <summary>전리품 spawns reached / found.</summary>
            public int LootReached { get; private set; }

            /// <summary>전리품 spawns found.</summary>
            public int Loot { get; private set; }

            /// <summary>Player spawns reached.</summary>
            public int SpawnsReached { get; private set; }

            /// <summary>Player spawns found.</summary>
            public int Spawns { get; private set; }

            /// <summary>Storeys with somewhere the player can stand.</summary>
            public int StoreysWalkable
            {
                get
                {
                    var count = 0;
                    foreach (var storey in Storeys.Values)
                    {
                        if (storey.Walkable)
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }

            /// <summary>True when a player can walk to everywhere the game sends one.</summary>
            public bool Passed =>
                Start != null
                && !Truncated
                && NodeCount > 0
                && Storeys.Count > 0
                && StoreysWalkable == Storeys.Count
                && Sites > 0
                && Unreachable.Count == 0;

            internal void NoteClimb(float rise)
            {
                if (rise > TallestClimb)
                {
                    TallestClimb = rise;
                }
            }

            internal void Count(MarkerKind kind, bool reached)
            {
                switch (kind)
                {
                    case MarkerKind.CandidateSite:
                        Sites++;
                        if (reached)
                        {
                            SitesReached++;
                        }

                        break;
                    case MarkerKind.LootSpawn:
                        Loot++;
                        if (reached)
                        {
                            LootReached++;
                        }

                        break;
                    default:
                        Spawns++;
                        if (reached)
                        {
                            SpawnsReached++;
                        }

                        break;
                }
            }

            /// <summary>
            /// Keeps blocked moves worth naming. Plain walls are kept apart and in far
            /// smaller number: every corridor in the building produces thousands of
            /// them and they would crowd out the one 0.6 m step that matters, but when
            /// a 계단 turns out to be blocked by an object rather than by a height, the
            /// name of that object is the entire answer.
            /// </summary>
            internal void RecordBlock(Block block)
            {
                if (block.Kind == BlockKind.Wall)
                {
                    if (_walls.Count < 60000)
                    {
                        _walls.Add(block);
                    }

                    return;
                }

                if (_blocks.Count < 4000)
                {
                    _blocks.Add(block);
                }
            }

            /// <summary>The blocking surface nearest a marker — the thing to go and look at.</summary>
            internal Block? NearestBlockTo(Vector3 position)
            {
                Block? best = null;
                var bestDistance = float.PositiveInfinity;

                foreach (var block in _blocks)
                {
                    var distance = Vector3.Distance(block.At, position);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = block;
                    }
                }

                return best;
            }

            /// <summary>
            /// The blocked move inside <paramref name="bounds"/> most worth naming: the
            /// tallest step, or failing that the tightest soffit, or failing that
            /// anything at all.
            /// </summary>
            internal Block? WorstBlockIn(Bounds bounds, Vector3 frontier)
            {
                Block? step = null;
                Block? headroom = null;
                Block? other = null;
                var otherDistance = float.PositiveInfinity;

                foreach (var block in _blocks)
                {
                    if (!bounds.Contains(block.At))
                    {
                        continue;
                    }

                    if (block.Kind == BlockKind.Step)
                    {
                        if (step == null || block.Metric > step.Value.Metric)
                        {
                            step = block;
                        }
                    }
                    else if (block.Kind == BlockKind.Headroom)
                    {
                        if (headroom == null || block.Metric < headroom.Value.Metric)
                        {
                            headroom = block;
                        }
                    }
                    else
                    {
                        var distance = Vector3.Distance(block.At, frontier);
                        if (distance < otherDistance)
                        {
                            otherDistance = distance;
                            other = block;
                        }
                    }
                }

                if (step != null || headroom != null || other != null)
                {
                    return step ?? headroom ?? other;
                }

                // Only walls left. The one that matters is the one where the capsule
                // stopped, not the first one the flood happened to touch.
                Block? nearest = null;
                var best = float.PositiveInfinity;
                foreach (var wall in _walls)
                {
                    if (!bounds.Contains(wall.At))
                    {
                        continue;
                    }

                    var distance = Vector3.Distance(wall.At, frontier);
                    if (distance < best)
                    {
                        best = distance;
                        nearest = wall;
                    }
                }

                return nearest;
            }

            /// <summary>The tallest step the sweep refused anywhere, which is the number to fix.</summary>
            public Block? TallestRefusedStep()
            {
                Block? worst = null;
                foreach (var block in _blocks)
                {
                    if (block.Kind == BlockKind.Step && (worst == null || block.Metric > worst.Value.Metric))
                    {
                        worst = block;
                    }
                }

                return worst;
            }

            /// <summary>Human-readable summary.</summary>
            public string Describe()
            {
                var sb = new StringBuilder();
                sb.AppendLine("[PlayerReach] " + (Passed ? "PASS" : "FAIL"));
                sb.AppendLine("  body             " + Body.Describe());
                sb.AppendLine("  usable step      "
                    + Mathf.Max(0.05f, Body.StepOffset - ClimbMarginMetres).ToString("0.00", CultureInfo.InvariantCulture)
                    + " m (stepOffset less a " + ClimbMarginMetres.ToString("0.00", CultureInfo.InvariantCulture)
                    + " m margin)");
                sb.AppendLine("  from 출입구       "
                    + (Start == null ? "not found" : Start.Value.ToString("F2") + "  (" + StartName + ")"));
                sb.AppendLine("  standing places  " + NodeCount.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("  storeys          " + StoreysWalkable + "/" + Storeys.Count
                    + (StoreysWalkable == Storeys.Count ? string.Empty : "  ← the player is locked out of a floor"));

                foreach (var pair in Storeys)
                {
                    sb.AppendLine("    B" + (pair.Key + 1) + "  "
                        + (pair.Value.Walkable ? "walkable" : "UNREACHABLE").PadRight(11)
                        + pair.Value.Standing.ToString(CultureInfo.InvariantCulture).PadLeft(7) + " places  "
                        + pair.Value.Reached + "/" + pair.Value.Targets + " markers");
                }

                sb.AppendLine("  후보 지점         " + SitesReached + "/" + Sites);
                sb.AppendLine("  전리품 spawns     " + LootReached + "/" + Loot);
                sb.AppendLine("  player spawns    " + SpawnsReached + "/" + Spawns);
                sb.AppendLine("  tallest climb    "
                    + TallestClimb.ToString("0.000", CultureInfo.InvariantCulture) + " m");
                sb.AppendLine("  tightest headroom "
                    + MinClearance.ToString("0.00", CultureInfo.InvariantCulture) + " m at "
                    + MinClearanceAt.ToString("F2"));

                if (WorstReachedGap > 0f)
                {
                    sb.AppendLine("  worst reach gap  "
                        + WorstReachedGap.ToString("0.00", CultureInfo.InvariantCulture)
                        + " m  (" + WorstReachedName + ")");
                }

                var tallest = TallestRefusedStep();
                if (tallest != null)
                {
                    sb.AppendLine("  tallest refused  " + tallest.Value.Describe());
                }

                if (Shafts.Count > 0)
                {
                    var walked = 0;
                    foreach (var shaft in Shafts)
                    {
                        if (shaft.Traversed)
                        {
                            walked++;
                        }
                    }

                    sb.AppendLine("  계단              " + walked + "/" + Shafts.Count + " walked end to end");
                    foreach (var shaft in Shafts)
                    {
                        if (shaft.Traversed)
                        {
                            continue;
                        }

                        sb.AppendLine("    " + shaft.Name + "  climbs "
                            + shaft.Bounds.min.y.ToString("0.00", CultureInfo.InvariantCulture) + " → "
                            + (shaft.Bounds.min.y + MapKitCatalogue.StoreyMetres).ToString("0.00", CultureInfo.InvariantCulture)
                            + " m; the player covered "
                            + shaft.Covered.ToString("0.00", CultureInfo.InvariantCulture) + " m of it ("
                            + shaft.Standing + " places"
                            + (shaft.Standing == 0
                                ? string.Empty
                                : ", " + shaft.BottomY.ToString("0.00", CultureInfo.InvariantCulture) + " → "
                                    + shaft.TopY.ToString("0.00", CultureInfo.InvariantCulture)
                                    + ", stalled at " + shaft.Frontier.ToString("F2"))
                            + ")"
                            + (shaft.Worst == null ? string.Empty : "; stopped by " + shaft.Worst.Value.Describe()));
                    }
                }

                foreach (var note in Notes)
                {
                    sb.AppendLine("  note: " + note);
                }

                if (Unreachable.Count > 0)
                {
                    sb.AppendLine("  the player cannot reach:");
                    for (var i = 0; i < Unreachable.Count && i < 16; i++)
                    {
                        var miss = Unreachable[i];
                        sb.AppendLine("    " + miss.Marker.Name + " at " + miss.Marker.Position.ToString("F2")
                            + " — nearest footing "
                            + (float.IsPositiveInfinity(miss.Gap)
                                ? "nowhere on this storey"
                                : miss.Gap.ToString("0.0", CultureInfo.InvariantCulture) + " m away")
                            + (miss.Blame == null ? string.Empty : "; first blocking surface is " + miss.Blame.Value.Describe()));
                    }

                    if (Unreachable.Count > 16)
                    {
                        sb.AppendLine("    … and " + (Unreachable.Count - 16) + " more");
                    }
                }

                if (!Passed)
                {
                    sb.AppendLine();
                    sb.AppendLine("  A NavMesh audit cannot see this. The baked agent climbs agentClimb (0.75 m)");
                    sb.AppendLine("  and stands 2.00 m; the player climbs stepOffset (0.40 m) and stands 1.75 m.");
                    sb.AppendLine("  Anything between those numbers is a route only the monster can take, and it");
                    sb.AppendLine("  reads as 100 % connectivity with one island while a human is stuck. Usual");
                    sb.AppendLine("  causes, in the order they are worth checking:");
                    sb.AppendLine("    - a 계단 riser, landing edge or dock lip over "
                        + Mathf.Max(0.05f, Body.StepOffset - ClimbMarginMetres).ToString("0.00", CultureInfo.InvariantCulture)
                        + " m (tools/blender/gen_mapkit.py, build_stairwell)");
                    sb.AppendLine("    - two pieces docked at different levels, so the seam is a step");
                    sb.AppendLine("    - a beam, duct or slab soffit under 1.75 m over a corridor");
                    sb.AppendLine("    - a prop dropped in a 2.2 m corridor, which the capsule cannot pass");
                    sb.AppendLine("  Do NOT fix it by raising stepOffset. A player who can climb 0.65 m can climb");
                    sb.AppendLine("  crates, debris and the van, and §12's escape geometry assumes they cannot.");
                }

                return sb.ToString();
            }
        }
    }
}
