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
    /// Checks that a <em>player</em> can get from where they start to where they win, by
    /// sweeping the real capsule through the building.
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
    /// consult the NavMesh at all. It floods a grid of capsule positions using
    /// <see cref="Physics"/> sweeps, which is the same collide-and-slide the controller
    /// does at runtime. Trusting the baked surface here would defeat the entire point,
    /// because the whole premise is that the two disagree.
    /// </para>
    /// <para>
    /// <b>The question is a race's question, and a race's question is DIRECTED.</b> This
    /// file used to flood outward from the 출입구 and ask what a player could walk to from
    /// the door. On §01's descent map the 출입구 <em>is</em> the finish — the middle of B8,
    /// twenty-six metres down — and the only way down is a 투하구, which a runner falls
    /// through and cannot climb back up. Flooding from the finish therefore measured a
    /// journey nobody makes, in a direction the building does not permit: it reached one
    /// storey of eight, nought of sixty-five starts, and reported the other seven floors
    /// as "nearest footing nowhere on this storey" while the map was in fact fine. A map
    /// that only reaches disk under <c>-forceWrite</c> is not a map that shipped.
    /// </para>
    /// <para>
    /// So the question asked here is the one §01 and §02 actually pose: <b>can a runner
    /// get from where they start to where they win?</b> The capsule is flooded from every
    /// PlayerSpawn; each pocket of connected floor the flood finds is recorded; each
    /// 투하구 contributes a ONE-WAY edge from the pocket its mouth is in to the pocket its
    /// landing is in, paired mouth-to-landing by name exactly as
    /// <c>MatchDirector.AttachChutes</c> does; and the verdict is the directed closure of
    /// that graph. "Connected" is no longer symmetric anywhere in this file, and where a
    /// number depends on the direction the word "reach" now says which way.
    /// </para>
    /// <para>
    /// <b>How it tells a race from a co-op building: it does not have to, and that is the
    /// point.</b> One algorithm answers both, because a building with no one-way route is
    /// the special case where the directed closure and the undirected one coincide. The
    /// co-op map (<see cref="FirstMapSketch"/>) has four PlayerSpawns ringing its 출입구,
    /// stairwells for its vertical routes and no 투하구 at all, so the closure from the
    /// spawns is the same set the old flood from the door produced and every rule it
    /// enforced still bites. What the report does is <em>measure</em> which kind of
    /// building it was handed rather than guess: the "chute-blind" line re-asks the whole
    /// question with the one-way edges deleted. If every start still reaches the finish
    /// without them the building is symmetric and says so; if none does, the descent is
    /// load-bearing and this gate fails the moment a 투하구 breaks. That line is the
    /// falsification probe — it is how a reader knows the finish check is not decorative.
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
    /// fails it. A building seven of whose eight storeys no runner can reach is not a
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

        /// <summary>
        /// Guard against flooding an unbounded scene.
        /// <para>
        /// Sized against the artefact rather than guessed. Measured on seed 20260802
        /// (/tmp/r3_gen.log): flooding B8 alone produced <b>30 585</b> standing places, and
        /// §01's tower is eight storeys built by the same generator, so a whole descent is
        /// ≈ 245 000. This is ~5× that, which leaves room for a ninth floor and for
        /// <see cref="SampleMetres"/> to be halved, and is still small enough that hitting
        /// it means something is wrong rather than that the map grew.
        /// </para>
        /// <para>
        /// Hitting it sets <see cref="Report.Truncated"/> and FAILS the gate, because a
        /// truncated flood cannot tell "unreachable" from "not looked at yet" and a gate
        /// that cannot tell those apart is worse than no gate.
        /// </para>
        /// </summary>
        private const int MaxNodes = 1_200_000;

        /// <summary>Clearance left under the capsule so a sweep does not graze its own floor, metres.</summary>
        private const float Skin = 0.03f;

        /// <summary>
        /// How a §01 투하구 mouth is named in the scene.
        /// <para>
        /// The name is the pairing, and the pairing is the runtime's:
        /// <c>MatchDirector.AttachChutes</c> matches "투하구 3북" to "투하구 3북 착지" by
        /// trimming this suffix, and <c>MapSketch</c> emits the pair that way because
        /// <c>MapMarkerPlacement</c> carries a position and a name and nothing else.
        /// </para>
        /// <para>
        /// Restated here rather than referenced because <c>Chute</c> and
        /// <c>MatchDirector</c> live in Assembly-CSharp and this file is inside
        /// <c>HorrorGame.EditorTools.SceneGen.asmdef</c>, which cannot reference it. The
        /// compiler therefore cannot catch a drift; <c>grep -n '착지' </c> over
        /// <c>MatchDirector.cs</c>, <c>MapSketch.cs</c> and this file is what can, and all
        /// three must agree. If they ever stop agreeing this gate reports
        /// <c>one-way routes 0</c> on a map with 28 투하구 markers in it, which is the
        /// symptom to look for.
        /// </para>
        /// </summary>
        private const string ChuteMouthPrefix = "투하구 ";

        /// <summary>The landing half of the pair. See <see cref="ChuteMouthPrefix"/>.</summary>
        private const string ChuteLandingSuffix = " 착지";

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

        /// <summary>
        /// Sweeps the player's capsule out from every start and reports whether a runner
        /// can get from there to the finish.
        /// <para>
        /// Six passes, in this order, and the order is load-bearing:
        /// </para>
        /// <list type="number">
        /// <item>flood from every PlayerSpawn — §01's rim of B1, or §12's ring round the
        /// 출입구 on the co-op map;</item>
        /// <item>flood from every 투하구 <em>landing</em>, whether or not its mouth turns
        /// out to be reachable;</item>
        /// <item>locate every 투하구 mouth in whatever pocket it fell in;</item>
        /// <item>locate the finish;</item>
        /// <item>resolve the directed closure over the pockets;</item>
        /// <item>score markers, headroom and 계단 against what a runner can get into.</item>
        /// </list>
        /// <para>
        /// Step 2 is the one worth defending, and it is where this departs from the fix
        /// that was proposed for it. Seeding a landing only once its mouth has been
        /// reached — iterating to a fixpoint — cannot distinguish <em>"no runner can get
        /// to this 투하구"</em> from <em>"this 투하구 drops runners into rock"</em>, because
        /// the second failure is never even looked at when it sits under the first. §01's
        /// own note on <c>DescentMap.HangChutes</c> calls a landing in rock "a floor nobody
        /// could finish", so it has to be a measurement and not a consequence. Flooding
        /// every landing unconditionally costs nothing extra — the storey below has to be
        /// walked anyway for the per-storey tally — and it turns the fixpoint into a BFS
        /// over a graph with one node per pocket, which is where a fixpoint belongs.
        /// </para>
        /// </summary>
        public static Report Audit(Scene scene)
        {
            var report = new Report();
            report.Body = PlayerBody.Measure(report);

            Physics.SyncTransforms();

            var markers = CollectMarkers(scene, report);
            if (report.Finish == null)
            {
                report.Notes.Add(
                    "No 출입구 found. §02 makes reaching it the win condition — on the co-op map it is the door "
                    + "you leave by, on §01's descent it is the middle of B8 — so a scene without one has no "
                    + "finish to measure a runner against. Is this the generated map?");
                return report;
            }

            foreach (var storey in StoreysIn(scene))
            {
                report.Storeys[storey] = new StoreyResult();
            }

            var world = new Walkable(report.Body, report);

            // 1. Every start. All of them, not one: §01 puts twenty runners on a
            //    sixty-five-cell rim and a start in a pocket of its own is a player who
            //    cannot play, which is invisible if the flood begins somewhere else.
            var starts = new List<Marker>();
            foreach (var marker in markers)
            {
                if (marker.Kind == MarkerKind.PlayerSpawn)
                {
                    starts.Add(marker);
                }
            }

            if (starts.Count == 0)
            {
                // Both shipped sketches emit spawns, so this is a scene nobody generated.
                // Falling back to the 출입구 reproduces the pre-race behaviour exactly, and
                // says so, rather than reporting 0/0 starts as a pass.
                report.Notes.Add(
                    "No PlayerSpawn marker in this scene, so the flood started at the 출입구 instead and the "
                    + "start→finish question could not be asked. Every generated map has spawns; a scene "
                    + "without them is not one this gate can certify.");
                starts.Add(new Marker(report.FinishName, report.Finish.Value, MarkerKind.PlayerSpawn));
                report.StartsSynthesised = true;
            }

            foreach (var start in starts)
            {
                var pocket = world.PocketAt(start.Position, out var gap);
                report.StartPockets.Add(pocket);
                if (pocket < 0)
                {
                    report.Notes.Add(
                        start.Name + " at " + start.Position.ToString("F2") + " is not a place the player's "
                        + "capsule can stand and no floor it can stand on is within "
                        + ReachRadiusMetres.ToString("0.00", CultureInfo.InvariantCulture)
                        + " m of it (nearest " + (float.IsPositiveInfinity(gap) ? "none at all" :
                            gap.ToString("0.00", CultureInfo.InvariantCulture) + " m")
                        + "). Whoever spawns there starts the match inside the building.");
                }
            }

            // 2. Every landing, unconditionally. See the remark above.
            foreach (var route in report.Routes)
            {
                route.LandingPocket = world.PocketAt(route.Landing, out var gap);
                route.LandingGap = gap;
            }

            // 3. Every mouth. PocketAt rather than a plain lookup so a 투하구 hanging over
            //    floor nothing else touches still gets its pocket measured and named.
            foreach (var route in report.Routes)
            {
                route.MouthPocket = world.PocketAt(route.Mouth, out var gap);
                route.MouthGap = gap;
            }

            // 4. The finish.
            report.FinishPocket = world.PocketAt(report.Finish.Value, out var finishGap);
            report.FinishGap = finishGap;

            // 5. Pocket ids handed out before a later flood merged two pockets are stale by
            //    now, so every stored id is put back through the union-find before anything
            //    compares two of them. Skipping this is how "the finish is in pocket 3" and
            //    "the starts reach pocket 0" quietly stop being about the same floor.
            world.Settle(report);

            // 6. The verdict, and the chute-blind control that proves it is load-bearing.
            //    Sized by PocketIds, not PocketCount — see the remark on PocketIds.
            report.ResolveReach(world.PocketIds);

            report.NodeCount = world.CountRunnerReachable(report);
            report.NodeCountFlooded = world.Nodes.Count;
            report.PocketCount = world.PocketCount;

            Evaluate(markers, world, report);
            MeasureHeadroom(world, report);
            MeasureStairs(scene, world, report);
            return report;
        }

        /// <summary>
        /// Every 계단, and how far down it the capsule actually got.
        /// <para>
        /// A building has exactly two kinds of vertical route and this measures one of
        /// them. §12 makes changing storey a deliberate act and the generator emits no
        /// <c>NavMeshLink</c>, so the only ways between floors are a 계단, which is walked
        /// and is two-way, and a §01 투하구, which is fallen down and is <em>not</em>. The
        /// co-op map has 4 계단 and no 투하구; §01's descent has 14 투하구 and no 계단
        /// (<c>grep -ci stair</c> on <c>DescentMap.cs</c> is 0). Both are counted, each by
        /// the instrument that suits it — a 계단 by walking it, a 투하구 by
        /// <see cref="OneWayRoute"/> — and neither substitutes for the other.
        /// </para>
        /// <para>
        /// A per-shaft trace turns "B2 unreachable" into a height and a coordinate someone
        /// can go and stand next to, which is why this survives on a map that has none:
        /// deleting it would delete the only diagnosis the co-op building has.
        /// </para>
        /// <para>
        /// Only nodes a <em>runner</em> can get into are counted. A flight flooded from its
        /// far end because a landing was seeded down there is not a flight the player
        /// walked, and counting it would report a stairwell as covered on the strength of a
        /// journey nobody can make.
        /// </para>
        /// </summary>
        private static void MeasureStairs(Scene scene, Walkable world, Report report)
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
                    for (var i = 0; i < world.Nodes.Count; i++)
                    {
                        if (report.RunnerCanEnter(world.PocketOfNode(i)) && bounds.Contains(world.Nodes[i]))
                        {
                            shaft.Note(world.Nodes[i]);
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
        /// Every foot position the player's capsule can occupy, grouped into the
        /// <em>pockets</em> that walking alone connects.
        /// <para>
        /// The move rule is the controller's, not the pathfinder's: sweep the capsule
        /// horizontally; if that is blocked, lift it by the usable step and sweep again;
        /// then drop onto whatever is under the destination and require the capsule to
        /// stand there. A rise and a fall are held to the same limit, so a walked move is
        /// two-way <em>by construction</em> and a pocket is an undirected component.
        /// </para>
        /// <para>
        /// <b>That symmetry is a claim about walking, not about the building, and it is
        /// only nearly true even of walking.</b> The lifted sweep starts at the origin's
        /// height and <see cref="Supported"/> measures the mid-point against the origin's
        /// height, so a move from A to B and the same move from B to A are not literally
        /// the same test when A and B sit at different heights. It is measured rather than
        /// assumed: whenever a flood settles onto a foot position another pocket already
        /// claimed, the two are united and <see cref="Report.PocketMerges"/> counts it. A
        /// non-zero count in the report is that asymmetry showing itself; nought means the
        /// seeds alone separated the building, which is what these two maps do.
        /// </para>
        /// <para>
        /// <b>Vertical routes are NOT in here.</b> A §01 투하구 is one-way — you fall down
        /// it — so it cannot be a flood move without making the pocket relation directed
        /// and destroying the one property the merge argument above depends on. Chutes are
        /// edges <em>between</em> pockets, resolved in <see cref="Report.ResolveReach"/>.
        /// </para>
        /// <para>
        /// One flood explores a whole pocket, which is why <see cref="Walkable.PocketAt"/>
        /// can decide a later seed's pocket by proximity alone and never has to flood twice
        /// into the same place: if a new seed were connected to an existing pocket, that
        /// pocket's own flood would already have walked to within
        /// <see cref="ReachRadiusMetres"/> of it.
        /// </para>
        /// </summary>
        private sealed class Walkable
        {
            private readonly PlayerBody _body;
            private readonly Report _report;
            private readonly float _usable;
            private readonly Dictionary<long, int> _seen = new Dictionary<long, int>(1 << 18);
            private readonly List<Vector3> _nodes = new List<Vector3>();

            /// <summary>Pocket id per node, before union-find. Read through <see cref="PocketOfNode"/>.</summary>
            private readonly List<int> _pocketOf = new List<int>();

            /// <summary>Union-find parent per pocket id.</summary>
            private readonly List<int> _parent = new List<int>();

            internal Walkable(PlayerBody body, Report report)
            {
                _body = body;
                _report = report;
                _usable = Mathf.Max(0.05f, body.StepOffset - ClimbMarginMetres);
            }

            /// <summary>Every foot position found, in every pocket.</summary>
            internal List<Vector3> Nodes => _nodes;

            /// <summary>How many distinct pockets of floor the building turned out to have.</summary>
            internal int PocketCount
            {
                get
                {
                    var count = 0;
                    for (var i = 0; i < _parent.Count; i++)
                    {
                        if (Root(i) == i)
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }

            /// <summary>
            /// How many pocket ids were ever handed out — the size any array indexed by
            /// pocket has to be.
            /// <para>
            /// NOT <see cref="PocketCount"/>. A merge retires an id without renumbering the
            /// survivors, so after one merge the ids in use are 0 and 2 while the count is
            /// 2, and an array sized by the count silently drops pocket 2 — which reads as
            /// "no runner can get in there" and fails a map that is fine.
            /// </para>
            /// </summary>
            internal int PocketIds => _parent.Count;

            /// <summary>Which pocket a foot position belongs to.</summary>
            internal int PocketOfNode(int index) => Root(_pocketOf[index]);

            /// <summary>Union-find root, path-compressed.</summary>
            internal int Root(int pocket)
            {
                if (pocket < 0)
                {
                    return -1;
                }

                while (_parent[pocket] != pocket)
                {
                    _parent[pocket] = _parent[_parent[pocket]];
                    pocket = _parent[pocket];
                }

                return pocket;
            }

            /// <summary>
            /// The pocket a point is in, flooding it if nothing has been there yet.
            /// −1 when the capsule cannot stand there and no pocket comes within
            /// <see cref="ReachRadiusMetres"/> of it.
            /// </summary>
            /// <param name="gap">Metres to the nearest foot position anywhere, for the report.</param>
            internal int PocketAt(Vector3 at, out float gap)
            {
                var known = Nearest(at, null, out gap);
                if (known >= 0)
                {
                    return known;
                }

                if (!TryStand(at, _body, out var footing))
                {
                    return -1;
                }

                // Belt and braces. Nearest should already have found any node sharing this
                // foot position's cell — Key quantises to 0.25 m across and 0.10 m up, far
                // inside ReachRadiusMetres — but starting a flood on a key that is already
                // taken would overwrite the index in _seen and leave two nodes claiming one
                // cell in two different pockets, which no later pass could untangle.
                if (_seen.TryGetValue(Key(footing), out var standing))
                {
                    gap = 0f;
                    return PocketOfNode(standing);
                }

                var id = _parent.Count;
                _parent.Add(id);
                Flood(footing, id);
                gap = 0f;
                return Root(id);
            }

            /// <summary>
            /// The pocket nearest a point, or −1 when the nearest foot position is further
            /// than <see cref="ReachRadiusMetres"/> away. <paramref name="allowed"/> limits
            /// the answer to pockets a runner can actually get into, which is how a marker
            /// standing on perfectly good floor in a sealed pocket is still counted as
            /// unreachable.
            /// </summary>
            /// <param name="gap">Metres to that foot position, whether or not it was close enough.</param>
            internal int Nearest(Vector3 at, bool[]? allowed, out float gap)
            {
                var best = float.PositiveInfinity;
                var pocket = -1;

                for (var i = 0; i < _nodes.Count; i++)
                {
                    var node = _nodes[i];
                    if (Mathf.Abs(node.y - at.y) > ReachHeightMetres)
                    {
                        continue;
                    }

                    var root = Root(_pocketOf[i]);
                    if (allowed != null && (root < 0 || root >= allowed.Length || !allowed[root]))
                    {
                        continue;
                    }

                    var dx = node.x - at.x;
                    var dz = node.z - at.z;
                    var flat = (dx * dx) + (dz * dz);
                    if (flat < best)
                    {
                        best = flat;
                        pocket = root;
                    }
                }

                gap = pocket < 0 ? float.PositiveInfinity : Mathf.Sqrt(best);
                return gap <= ReachRadiusMetres ? pocket : -1;
            }

            /// <summary>
            /// Puts every pocket id — the nodes' and the report's — back through the
            /// union-find.
            /// <para>
            /// A pocket id handed out at step 1 can be merged into another at step 2, and
            /// then two ids that name the same floor compare unequal. Called once, after
            /// all flooding and before anything compares two of them.
            /// </para>
            /// <para>
            /// Flattening the nodes matters for speed as well as correctness: everything
            /// after this walks the node list once per marker, which on an eight-storey
            /// descent is ~288 × 245 000 lookups, and a lookup that has to climb a
            /// union-find chain would turn a scoring pass into a visible pause.
            /// </para>
            /// </summary>
            internal void Settle(Report report)
            {
                for (var i = 0; i < _pocketOf.Count; i++)
                {
                    _pocketOf[i] = Root(_pocketOf[i]);
                }

                for (var i = 0; i < report.StartPockets.Count; i++)
                {
                    report.StartPockets[i] = Root(report.StartPockets[i]);
                }

                foreach (var route in report.Routes)
                {
                    route.MouthPocket = Root(route.MouthPocket);
                    route.LandingPocket = Root(route.LandingPocket);
                }

                report.FinishPocket = Root(report.FinishPocket);
            }

            /// <summary>Foot positions a runner can get into, which is the number that matters.</summary>
            internal int CountRunnerReachable(Report report)
            {
                var count = 0;
                for (var i = 0; i < _nodes.Count; i++)
                {
                    if (report.RunnerCanEnter(PocketOfNode(i)))
                    {
                        count++;
                    }
                }

                return count;
            }

            /// <summary>Walks one pocket to its edges, tagging everything it finds with <paramref name="id"/>.</summary>
            private void Flood(Vector3 footing, int id)
            {
                var queue = new Queue<int>();

                _seen[Key(footing)] = _nodes.Count;
                _nodes.Add(footing);
                _pocketOf.Add(id);
                queue.Enqueue(_nodes.Count - 1);

                while (queue.Count > 0)
                {
                    var from = _nodes[queue.Dequeue()];

                    foreach (var step in Steps)
                    {
                        var moved = TryStep(from, step, _body, _usable, _seen, out var to, out var touched, out var block);

                        // The capsule settled somewhere another pocket already owns. Under
                        // a perfectly symmetric move rule this cannot happen across
                        // pockets; that it is checked rather than asserted is the point.
                        if (touched >= 0)
                        {
                            var other = Root(_pocketOf[touched]);
                            if (other != Root(id))
                            {
                                _parent[other] = Root(id);
                                _report.PocketMerges++;
                            }
                        }

                        if (!moved)
                        {
                            _report.RecordBlock(block);
                            continue;
                        }

                        if (_nodes.Count >= MaxNodes)
                        {
                            // Once, not once per seed. Every later PocketAt starts a flood
                            // that trips this on its first move, and ninety-four copies of
                            // the same sentence would bury the report that explains it.
                            if (!_report.Truncated)
                            {
                                _report.Notes.Add(
                                    "Stopped at " + MaxNodes + " sample points. The scene is larger than this "
                                    + "check was sized for, so every number below is a lower bound, not a "
                                    + "verdict, and the gate fails on that ground alone.");
                                _report.Truncated = true;
                            }

                            queue.Clear();
                            break;
                        }

                        _seen[Key(to)] = _nodes.Count;
                        _nodes.Add(to);
                        _pocketOf.Add(id);
                        queue.Enqueue(_nodes.Count - 1);
                        _report.NoteClimb(to.y - from.y);
                    }
                }
            }
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
        /// <param name="touched">
        /// The index of the first already-flooded foot position this move settled onto, or
        /// −1. Not a failure and not a success — it is the evidence
        /// <see cref="Walkable"/> unites two pockets on, and without it a move that lands
        /// on known ground looks identical to a move that lands on nothing.
        /// </param>
        private static bool TryStep(
            Vector3 from, Vector3 offset, PlayerBody body, float usable,
            Dictionary<long, int> seen, out Vector3 to, out int touched, out Block block)
        {
            to = default;
            touched = -1;
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
                if (seen.TryGetValue(Key(to), out var already))
                {
                    if (touched < 0)
                    {
                        touched = already;
                    }

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
        /// <para>
        /// It also picks up the two things the race needs and the loot sweep does not: the
        /// finish, and every §01 투하구.
        /// </para>
        /// </summary>
        private static List<Marker> CollectMarkers(Scene scene, Report report)
        {
            var markers = new List<Marker>();

            // Mouth-to-landing pairing, by name. NOT a second opinion about how a 투하구 is
            // put together: MatchDirector.AttachChutes pairs "투하구 3북" with "투하구 3북 착지"
            // by trimming the suffix, and a gate that paired them any other way could
            // certify a descent the runtime does not have. The suffix is 3 chars — " 착지".
            var landings = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            var mouths = new List<KeyValuePair<string, Vector3>>();

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
                        //
                        // §02 calls this the way out and §12 marks it 출입구; on §01's
                        // descent map DescentMap.MarkPlaces puts the same mark on the
                        // middle of B8 and names it 도착점, "because the only way out of
                        // this building is down". Either way it is the FINISH — the place
                        // a run ends — and that is what this file now calls it.
                        var floor = transform.position
                            - (Vector3.up * (MapKitCatalogue.CorridorClearWidth + 0.6f));
                        if (report.Finish == null)
                        {
                            report.Finish = floor;
                            report.FinishName = name;
                        }
                        else
                        {
                            report.ExtraFinishes++;
                        }

                        continue;
                    }

                    if (transform.childCount != 0)
                    {
                        continue;
                    }

                    // A landing is checked FIRST because "투하구 1북 착지" satisfies both
                    // tests, and it is required to carry the prefix as well as the suffix:
                    // AttachChutes collects every " 착지" object and then looks each one up
                    // by a mouth's name, so a stray 착지 elsewhere in the scene is harmless
                    // to it, and a gate that turned that stray into a failed pairing would
                    // refuse a map the runtime is perfectly happy with.
                    if (name.StartsWith(ChuteMouthPrefix, StringComparison.Ordinal)
                        && name.EndsWith(ChuteLandingSuffix, StringComparison.Ordinal))
                    {
                        landings[name.Substring(0, name.Length - ChuteLandingSuffix.Length)] = transform.position;
                    }
                    else if (name.StartsWith(ChuteMouthPrefix, StringComparison.Ordinal))
                    {
                        mouths.Add(new KeyValuePair<string, Vector3>(name, transform.position));
                    }
                    else if (name.StartsWith("CandidateSite", StringComparison.Ordinal))
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

            foreach (var mouth in mouths)
            {
                if (landings.TryGetValue(mouth.Key, out var landing))
                {
                    report.Routes.Add(new OneWayRoute(mouth.Key, mouth.Value, landing));
                    landings.Remove(mouth.Key);
                }
                else
                {
                    // AttachChutes skips exactly this case at runtime, silently: a mouth
                    // with no landing never gets a Chute component, so it is a hole a
                    // runner stands in and nothing happens. Nothing else in the project
                    // would ever say so.
                    report.OrphanRoutes.Add(mouth.Key + " has no landing named '" + mouth.Key
                        + ChuteLandingSuffix + "', so MatchDirector.AttachChutes will skip it and standing in "
                        + "it will do nothing");
                }
            }

            foreach (var stranded in landings.Keys)
            {
                report.OrphanRoutes.Add("'" + stranded + ChuteLandingSuffix + "' is a landing with no 투하구 named '"
                    + stranded + "', so nothing can ever drop a runner onto it");
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

        /// <summary>
        /// Scores every marker against what a <em>runner</em> can get into, and tallies the
        /// storeys the same way.
        /// <para>
        /// Two gaps are measured for each marker, not one, and the difference between them
        /// is the diagnosis. <c>Gap</c> is the distance to the nearest foot position a
        /// runner can reach; <c>GapAnywhere</c> is the distance to the nearest foot
        /// position at all. When the first is infinite and the second is 0.2 m the marker
        /// is standing on perfectly good floor that no runner can get into — a sealed
        /// pocket — and telling someone "nearest footing nowhere on this storey" would send
        /// them to look for a hole in the floor that is not there. That is precisely the
        /// sentence this gate printed 245 times while the map was fine.
        /// </para>
        /// </summary>
        private static void Evaluate(List<Marker> markers, Walkable world, Report report)
        {
            for (var i = 0; i < world.Nodes.Count; i++)
            {
                var result = StoreyAt(report, StoreyOf(world.Nodes[i].y));
                if (report.RunnerCanEnter(world.PocketOfNode(i)))
                {
                    result.Standing++;
                }
                else
                {
                    result.Sealed++;
                }
            }

            foreach (var marker in markers)
            {
                var result = StoreyAt(report, StoreyOf(marker.Position.y));
                result.Targets++;

                var pocket = world.Nearest(marker.Position, report.RunnerPockets, out var gap);

                if (pocket >= 0)
                {
                    result.Reached++;
                    report.Count(marker.Kind, true);
                    if (gap > report.WorstReachedGap)
                    {
                        report.WorstReachedGap = gap;
                        report.WorstReachedName = marker.Name;
                    }
                }
                else
                {
                    // Only now, and only for the handful that failed: a second sweep of a
                    // quarter-million nodes per marker is the difference between a scoring
                    // pass a human waits through and one they do not notice.
                    report.Count(marker.Kind, false);
                    world.Nearest(marker.Position, null, out var anywhere);
                    report.Unreachable.Add(
                        new Unreachable(marker, gap, anywhere, report.NearestBlockTo(marker.Position)));
                }
            }
        }

        /// <summary>The tally for a storey, created on demand — a storey with no zone still has markers.</summary>
        private static StoreyResult StoreyAt(Report report, int storey)
        {
            if (!report.Storeys.TryGetValue(storey, out var result))
            {
                result = new StoreyResult();
                report.Storeys[storey] = result;
            }

            return result;
        }

        /// <summary>
        /// The other way a 1.75 m body is stopped without an error. Reported over
        /// everywhere a runner <em>can</em> stand, so a corridor that only just admits
        /// them shows up before someone finds it with their face.
        /// <para>
        /// Sealed pockets are skipped: a soffit over floor no runner reaches is not a
        /// hazard, and letting it set the number would bury the one that is.
        /// </para>
        /// </summary>
        private static void MeasureHeadroom(Walkable world, Report report)
        {
            report.MinClearance = float.PositiveInfinity;
            var sampled = 0;

            for (var i = 0; i < world.Nodes.Count; i += 4)
            {
                if (!report.RunnerCanEnter(world.PocketOfNode(i)))
                {
                    continue;
                }

                sampled++;
                var clearance = ClearanceAt(world.Nodes[i], report.Body);
                if (clearance < report.MinClearance)
                {
                    report.MinClearance = clearance;
                    report.MinClearanceAt = world.Nodes[i];
                }
            }

            if (sampled == 0)
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

        /// <summary>A marker no runner could get to, and the nearest reason why.</summary>
        public readonly struct Unreachable
        {
            internal Unreachable(Marker marker, float gap, float gapAnywhere, Block? blame)
            {
                Marker = marker;
                Gap = gap;
                GapAnywhere = gapAnywhere;
                Blame = blame;
            }

            /// <summary>The marker.</summary>
            public Marker Marker { get; }

            /// <summary>Metres to the nearest place the capsule can stand <em>and a runner can get into</em>.</summary>
            public float Gap { get; }

            /// <summary>
            /// Metres to the nearest place the capsule can stand at all, reachable or not.
            /// Finite while <see cref="Gap"/> is not means the marker is in a sealed pocket
            /// — good floor, no way in — which is a different defect from a hole.
            /// </summary>
            public float GapAnywhere { get; }

            /// <summary>The blocking surface nearest it, when one was measured.</summary>
            public Block? Blame { get; }

            /// <summary>Whether the marker stands on floor that exists but nothing leads to.</summary>
            public bool InSealedPocket =>
                float.IsPositiveInfinity(Gap) && GapAnywhere <= ReachRadiusMetres;
        }

        /// <summary>
        /// A §01 투하구: a one-way edge from the pocket its mouth is in to the pocket its
        /// landing is in.
        /// <para>
        /// "One-way" is the whole reason this type exists rather than the flood simply
        /// stepping through it. A runner falls down a 투하구 and cannot climb back, so
        /// reachability across one is DIRECTED and the pockets it joins are not
        /// interchangeable — which is exactly the fact the old gate, flooding from the
        /// finish, was unable to represent.
        /// </para>
        /// <para>
        /// Both ends are resolved with the same <see cref="ReachRadiusMetres"/> the rest of
        /// this file uses for "a marker is reached". For the mouth that is deliberately
        /// TIGHTER than the runtime trigger — <c>Chute.MouthRadiusMetres</c> is 1.40 m —
        /// because the only dangerous direction is a gate that accepts a mouth the runtime
        /// would not. If that constant is ever lowered below 1.25 m this check starts
        /// over-accepting and no compiler will say so (see <see cref="ChuteMouthPrefix"/>
        /// on the assembly boundary).
        /// </para>
        /// </summary>
        public sealed class OneWayRoute
        {
            internal OneWayRoute(string name, Vector3 mouth, Vector3 landing)
            {
                Name = name;
                Mouth = mouth;
                Landing = landing;
            }

            /// <summary>Scene object name of the mouth, e.g. "투하구 3북".</summary>
            public string Name { get; }

            /// <summary>Where a runner steps in.</summary>
            public Vector3 Mouth { get; }

            /// <summary>Where the storey below catches them — §01: 착지는 다음 층의 외곽.</summary>
            public Vector3 Landing { get; }

            /// <summary>Pocket the mouth is in, or −1.</summary>
            public int MouthPocket { get; internal set; } = -1;

            /// <summary>Pocket the landing is in, or −1.</summary>
            public int LandingPocket { get; internal set; } = -1;

            /// <summary>Metres from the mouth to the nearest footing.</summary>
            public float MouthGap { get; internal set; } = float.PositiveInfinity;

            /// <summary>Metres from the landing to the nearest footing.</summary>
            public float LandingGap { get; internal set; } = float.PositiveInfinity;

            /// <summary>
            /// True when both ends are somewhere a player's body can be. A route with a
            /// landing in rock still "works" in the sense that MatchDirector will move a
            /// runner along it — it will move them into the geology.
            /// </summary>
            public bool Usable => MouthPocket >= 0 && LandingPocket >= 0;

            /// <summary>One line naming which end is broken and by how much.</summary>
            public string Describe()
            {
                if (MouthPocket < 0 && LandingPocket < 0)
                {
                    return Name + ": neither its mouth " + Mouth.ToString("F2") + " nor its landing "
                        + Landing.ToString("F2") + " is anywhere a player's body fits";
                }

                if (MouthPocket < 0)
                {
                    return Name + ": its mouth at " + Mouth.ToString("F2")
                        + " is not floor a player can stand on (nearest footing " + Distance(MouthGap)
                        + "), so nothing can step into it";
                }

                return Name + ": its landing at " + Landing.ToString("F2")
                    + " is not floor a player can stand on (nearest footing " + Distance(LandingGap)
                    + ") — a runner who jumps in arrives inside the geometry";
            }

            private static string Distance(float metres) =>
                float.IsPositiveInfinity(metres)
                    ? "none within reach"
                    : metres.ToString("0.00", CultureInfo.InvariantCulture) + " m away";
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
            /// <summary>Sample points on this storey a <em>runner</em> can get into.</summary>
            public int Standing;

            /// <summary>
            /// Sample points on this storey the capsule fits in but no runner can get into.
            /// <para>
            /// Counted separately and never added to <see cref="Standing"/>. On a descent
            /// map every storey is flooded — the 투하구 landings are seeded whether or not
            /// they can be arrived at — so "the flood found floor here" stopped being the
            /// same claim as "a player can walk here" the moment the seeds changed, and a
            /// storey below a broken 투하구 would otherwise read as walkable.
            /// </para>
            /// </summary>
            public int Sealed;

            /// <summary>Markers on this storey.</summary>
            public int Targets;

            /// <summary>How many of them a runner can get to.</summary>
            public int Reached;

            /// <summary>A storey no runner can set foot on is a storey they are locked out of.</summary>
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

            /// <summary>
            /// Where a run ends: §12's 출입구 floor.
            /// <para>
            /// On the co-op map that is the door players come in and leave by; on §01's
            /// descent it is the middle of B8, because <c>DescentMap.MarkPlaces</c> puts the
            /// same mark there — "the only way out of this building is down". One name, two
            /// buildings, and in both of them it is the thing a runner has to arrive at.
            /// </para>
            /// </summary>
            public Vector3? Finish { get; internal set; }

            /// <summary>Which object gave the finish position.</summary>
            public string FinishName { get; internal set; } = string.Empty;

            /// <summary>Further 출입구 markers beyond the first — ambiguity worth naming, not an error.</summary>
            public int ExtraFinishes { get; internal set; }

            /// <summary>Metres from the finish marker to the nearest footing.</summary>
            public float FinishGap { get; internal set; } = float.PositiveInfinity;

            /// <summary>Pocket the finish is in, or −1.</summary>
            public int FinishPocket { get; internal set; } = -1;

            /// <summary>Pocket each start is in, in the order the starts were collected. −1 for a start in a wall.</summary>
            public readonly List<int> StartPockets = new List<int>();

            /// <summary>Every §01 투하구 in the scene, paired mouth to landing.</summary>
            public readonly List<OneWayRoute> Routes = new List<OneWayRoute>();

            /// <summary>Chute markers with no opposite number, which the runtime silently ignores.</summary>
            public readonly List<string> OrphanRoutes = new List<string>();

            /// <summary>True when there were no PlayerSpawn markers and the 출입구 stood in for one.</summary>
            public bool StartsSynthesised { get; internal set; }

            /// <summary>
            /// Pockets a runner can get into: the forward closure of the starts over the
            /// one-way routes. Null until <see cref="ResolveReach"/> has run.
            /// </summary>
            public bool[]? RunnerPockets { get; private set; }

            /// <summary>How many pockets of floor the building turned out to be in.</summary>
            public int PocketCount { get; internal set; }

            /// <summary>
            /// Times a flood settled onto ground another pocket already claimed.
            /// <para>
            /// Should be 0. Non-zero means the walked-move relation is not quite symmetric
            /// somewhere — see <see cref="Walkable"/> — and the pockets involved were
            /// united rather than left split, which is the safe direction but is a fact the
            /// report has to state rather than swallow.
            /// </para>
            /// </summary>
            public int PocketMerges { get; internal set; }

            /// <summary>Starts from which the finish is reachable, walking and falling.</summary>
            public int StartsThatFinish { get; private set; }

            /// <summary>
            /// Starts from which the finish is reachable <em>with every one-way route
            /// deleted</em> — the chute-blind control.
            /// <para>
            /// This is the falsification probe. On a building with no 투하구 it equals
            /// <see cref="Starts"/>: reachability is symmetric, the door is in the runners'
            /// own pocket, and this gate is asking the co-op question it always asked. On
            /// §01's descent it is 0: without the chutes nobody can win, which is the proof
            /// that <see cref="StartsThatFinish"/> is measuring the descent and not simply
            /// agreeing with itself. A descent map that reported a non-zero number here
            /// would mean the finish is reachable on foot from the rim of B1 — the tower
            /// has a hole in it — and a co-op map that reported 0 would mean its own door
            /// is walled off from its spawns.
            /// </para>
            /// </summary>
            public int StartsBlind { get; private set; }

            /// <summary>Foot positions a runner can get into.</summary>
            public int NodeCount { get; internal set; }

            /// <summary>Foot positions found at all, including sealed pockets.</summary>
            public int NodeCountFlooded { get; internal set; }

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

            /// <summary>Storeys with somewhere a runner can stand.</summary>
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

            /// <summary>How many starting positions the map has.</summary>
            public int Starts => StartPockets.Count;

            /// <summary>§01 투하구 with a player-shaped place at both ends.</summary>
            public int RoutesUsable
            {
                get
                {
                    var count = 0;
                    foreach (var route in Routes)
                    {
                        if (route.Usable)
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }

            /// <summary>True when the finish itself is somewhere a runner can arrive.</summary>
            public bool FinishReached => FinishPocket >= 0 && RunnerCanEnter(FinishPocket);

            /// <summary>
            /// True when this building's one-way routes are load-bearing — measured, by
            /// deleting them and asking again. See <see cref="StartsBlind"/>.
            /// </summary>
            public bool Directed => Starts > 0 && StartsBlind < Starts;

            /// <summary>Whether a runner can get into a pocket. False for −1 and before the closure is resolved.</summary>
            public bool RunnerCanEnter(int pocket) =>
                pocket >= 0 && RunnerPockets != null && pocket < RunnerPockets.Length && RunnerPockets[pocket];

            /// <summary>
            /// True when every runner can get from their own start to §02's finish, and to
            /// everywhere else the game sends one on the way.
            /// <para>
            /// Each clause earns its place:
            /// <list type="bullet">
            /// <item><c>Finish</c> — §02 needs somewhere to win. Without it there is no
            /// question to ask.</item>
            /// <item><c>!Truncated</c> — a flood that ran out of budget cannot tell
            /// "unreachable" from "not looked at".</item>
            /// <item><c>StoreysWalkable == Storeys.Count</c> — a floor no runner can set
            /// foot on is a floor that should not have been built.</item>
            /// <item><c>StartsThatFinish == Starts</c> — §01's actual promise. The one
            /// clause the old gate could not express, and the reason it had to be forced.
            /// </item>
            /// <item><c>!StartsSynthesised</c> — a scene with no PlayerSpawn was measured
            /// from the 출입구, which is the old question. It may be worth reading; it is
            /// not a pass.</item>
            /// <item><c>Sites &gt; 0</c> — §12 counts 후보 지점 and a map with none is a map
            /// whose per-storey evidence does not exist.</item>
            /// <item><c>Unreachable.Count == 0</c> — every 후보 지점 and 전리품 probe is on
            /// ground a runner can get into. §12's 막힌 길 are a fifth of each floor and
            /// these markers are the only probes on them.</item>
            /// <item><c>RoutesUsable == Routes.Count</c> and no orphans — a 투하구 that drops
            /// a runner into rock is not caught by any clause above, because both its
            /// storeys stay reachable through the other chute of the pair. §01 hangs two
            /// per floor precisely so that one of them failing is survivable, which is what
            /// makes it invisible; this is the clause that sees it.</item>
            /// </list>
            /// </para>
            /// <para>
            /// <see cref="StartsBlind"/> is deliberately NOT a clause. It is a control, not
            /// a rule: it says what kind of building this is, and both answers are legal.
            /// </para>
            /// </summary>
            public bool Passed =>
                Finish != null
                && !Truncated
                && NodeCount > 0
                && Storeys.Count > 0
                && StoreysWalkable == Storeys.Count
                && Starts > 0
                && !StartsSynthesised
                && StartsThatFinish == Starts
                && FinishReached
                && Sites > 0
                && Unreachable.Count == 0
                && RoutesUsable == Routes.Count
                && OrphanRoutes.Count == 0;

            /// <summary>
            /// Works out who can get where, over a graph with one node per pocket of floor
            /// and one DIRECTED edge per 투하구.
            /// <para>
            /// Two closures and a control:
            /// </para>
            /// <list type="bullet">
            /// <item>forwards from every start — everywhere a runner can get to, which is
            /// what markers, storeys, headroom and 계단 are all scored against;</item>
            /// <item>backwards from the finish — every pocket that can still win from where
            /// it stands, which answers "can THIS start finish" for all of them in one
            /// pass instead of one BFS per runner;</item>
            /// <item>the chute-blind control, which is the same backward question with the
            /// edges deleted and therefore collapses to "is this start already standing in
            /// the finish's pocket".</item>
            /// </list>
            /// <para>
            /// The expensive part of this file is the capsule sweep. This is a BFS over a
            /// graph with one node per pocket — eight of them on §01's tower — so the
            /// fixpoint the descent needs costs nothing at all once the geometry has been
            /// walked once.
            /// </para>
            /// </summary>
            /// <param name="pockets">
            /// How many pocket <em>ids</em> were handed out, so the closures can be arrays
            /// indexed by pocket. Not the number of surviving pockets.
            /// </param>
            internal void ResolveReach(int pockets)
            {
                var size = 0;
                foreach (var pocket in StartPockets)
                {
                    size = Mathf.Max(size, pocket + 1);
                }

                foreach (var route in Routes)
                {
                    size = Mathf.Max(size, Mathf.Max(route.MouthPocket, route.LandingPocket) + 1);
                }

                size = Mathf.Max(size, Mathf.Max(FinishPocket + 1, pockets));

                RunnerPockets = Closure(StartPockets, size, downward: true);

                var winners = Closure(
                    FinishPocket >= 0 ? new List<int> { FinishPocket } : new List<int>(), size, downward: false);

                StartsThatFinish = 0;
                StartsBlind = 0;
                foreach (var pocket in StartPockets)
                {
                    if (pocket < 0 || FinishPocket < 0)
                    {
                        continue;
                    }

                    if (winners[pocket])
                    {
                        StartsThatFinish++;
                    }

                    // The control: with no one-way edge, "can reach the finish" degenerates
                    // to "is already in the finish's pocket". Written out rather than run as
                    // a second BFS because a BFS over an edgeless graph is that expression.
                    if (pocket == FinishPocket)
                    {
                        StartsBlind++;
                    }
                }
            }

            /// <summary>
            /// Pockets reachable from <paramref name="seeds"/>, following each 투하구 the way
            /// a runner can use it: <paramref name="downward"/> mouth→landing for "where can
            /// I get to", landing→mouth for "who can get to me".
            /// </summary>
            private bool[] Closure(List<int> seeds, int size, bool downward)
            {
                var reached = new bool[Mathf.Max(size, 1)];
                var queue = new Queue<int>();

                foreach (var seed in seeds)
                {
                    if (seed >= 0 && seed < reached.Length && !reached[seed])
                    {
                        reached[seed] = true;
                        queue.Enqueue(seed);
                    }
                }

                while (queue.Count > 0)
                {
                    var pocket = queue.Dequeue();
                    foreach (var route in Routes)
                    {
                        if (!route.Usable)
                        {
                            continue;
                        }

                        var from = downward ? route.MouthPocket : route.LandingPocket;
                        var to = downward ? route.LandingPocket : route.MouthPocket;
                        if (from != pocket || to < 0 || to >= reached.Length || reached[to])
                        {
                            continue;
                        }

                        reached[to] = true;
                        queue.Enqueue(to);
                    }
                }

                return reached;
            }

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
                // THE line. Storeys, starts and the finish, in the order a human asks for
                // them: can they all play, can they all win, and did anybody get there.
                sb.AppendLine("  runner reach     storeys " + StoreysWalkable + "/" + Storeys.Count
                    + " · starts " + StartsThatFinish + "/" + Starts + " reach the finish · finish "
                    + (FinishReached ? "REACHED" : "UNREACHED") + " (" + FinishName + ")"
                    + (Passed ? string.Empty : "  ← " + WhyNot()));

                sb.AppendLine("  finish (§02)     "
                    + (Finish == null ? "not found" : Finish.Value.ToString("F2"))
                    + (ExtraFinishes > 0
                        ? "  (and " + ExtraFinishes + " more 출입구 — the first was used)"
                        : string.Empty)
                    + (FinishPocket >= 0
                        ? string.Empty
                        : "  ← the capsule cannot stand there and the nearest floor it can is "
                            + (float.IsPositiveInfinity(FinishGap)
                                ? "nowhere near it"
                                : FinishGap.ToString("0.00", CultureInfo.InvariantCulture) + " m away")));
                sb.AppendLine("  starts (§01)     " + Starts + " marker" + (Starts == 1 ? string.Empty : "s")
                    + " in " + DistinctStartPockets() + " pocket" + (DistinctStartPockets() == 1 ? string.Empty : "s")
                    + (StartsSynthesised ? "  ← SYNTHESISED from the 출입구; this scene has no PlayerSpawn" : string.Empty));

                // The control. Both answers are legal; which one comes back is how this
                // gate knows whether it is looking at a race or at a co-op building.
                sb.AppendLine("  one-way routes   " + RoutesUsable + "/" + Routes.Count + " 투하구 usable"
                    + (Shafts.Count > 0 ? "  ·  " + Shafts.Count + " 계단" : "  ·  no 계단"));
                sb.AppendLine("  chute-blind      " + StartsBlind + "/" + Starts
                    + " starts reach the finish with the one-way routes deleted — "
                    + (Routes.Count == 0
                        ? "there are none, so this building is SYMMETRIC and start→finish is the question it "
                            + "always was"
                        : Directed
                            ? "the 투하구 are DIRECTED and load-bearing, so this gate fails the moment one breaks"
                            : "the 투하구 are NOT load-bearing — a runner can walk to the finish without them, "
                                + "which on a descent map means the tower has a hole in it"));

                sb.AppendLine("  standing places  " + NodeCount.ToString(CultureInfo.InvariantCulture)
                    + " a runner can get into, of " + NodeCountFlooded.ToString(CultureInfo.InvariantCulture)
                    + " found in " + PocketCount + " pocket" + (PocketCount == 1 ? string.Empty : "s"));
                sb.AppendLine("  storeys          " + StoreysWalkable + "/" + Storeys.Count
                    + (StoreysWalkable == Storeys.Count ? string.Empty : "  ← a runner is locked out of a floor"));

                foreach (var pair in Storeys)
                {
                    sb.AppendLine("    B" + (pair.Key + 1) + "  "
                        + (pair.Value.Walkable ? "walkable" : "UNREACHABLE").PadRight(11)
                        + pair.Value.Standing.ToString(CultureInfo.InvariantCulture).PadLeft(7) + " places  "
                        + pair.Value.Reached + "/" + pair.Value.Targets + " markers"
                        + (pair.Value.Sealed > 0
                            ? "  (+" + pair.Value.Sealed + " places walled off from every start)"
                            : string.Empty));
                }

                sb.AppendLine("  후보 지점         " + SitesReached + "/" + Sites);
                sb.AppendLine("  전리품 spawns     " + LootReached + "/" + Loot);

                // Not a tautology even though the flood is seeded from these. A start seeded
                // from a spot the capsule cannot stand in has no pocket, so it contributes
                // nothing to the closure and is counted here as unreached — which is the
                // only way "spawns 65/65" and "starts 65/65 reach the finish" can disagree,
                // and both are printed so that they can.
                sb.AppendLine("  player spawns    " + SpawnsReached + "/" + Spawns
                    + " stand on ground a runner can get into");
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

                if (RoutesUsable < Routes.Count || OrphanRoutes.Count > 0)
                {
                    sb.AppendLine("  broken 투하구:");
                    foreach (var route in Routes)
                    {
                        if (!route.Usable)
                        {
                            sb.AppendLine("    " + route.Describe());
                        }
                    }

                    foreach (var orphan in OrphanRoutes)
                    {
                        sb.AppendLine("    " + orphan);
                    }
                }

                if (PocketMerges > 0)
                {
                    sb.AppendLine("  note: " + PocketMerges + " time(s) a flood settled onto floor another pocket "
                        + "already held, so the two were united. A walked move is meant to be two-way and this is "
                        + "the file measuring that rather than assuming it; the pockets are joined, which is the "
                        + "safe direction, but a large number here means the step or the settle is direction-"
                        + "dependent somewhere and the geometry is worth a look.");
                }

                foreach (var note in Notes)
                {
                    sb.AppendLine("  note: " + note);
                }

                if (Unreachable.Count > 0)
                {
                    sb.AppendLine("  no runner can reach:");
                    for (var i = 0; i < Unreachable.Count && i < 16; i++)
                    {
                        var miss = Unreachable[i];
                        sb.AppendLine("    " + miss.Marker.Name + " at " + miss.Marker.Position.ToString("F2")
                            + " — " + (miss.InSealedPocket
                                ? "it stands on floor the capsule fits on ("
                                    + miss.GapAnywhere.ToString("0.0", CultureInfo.InvariantCulture)
                                    + " m away) that NO 투하구 and no corridor leads into — a sealed pocket, not a hole"
                                : "nearest footing a runner can get into is "
                                    + (float.IsPositiveInfinity(miss.Gap)
                                        ? "nowhere on this storey"
                                        : miss.Gap.ToString("0.0", CultureInfo.InvariantCulture) + " m away"))
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
                    if (Routes.Count > 0 && (!FinishReached || StartsThatFinish < Starts))
                    {
                        // On a descent map the first thing to suspect is never a riser. It
                        // is one of fourteen holes in the ground, and the report above names
                        // which storey the closure stopped on.
                        sb.AppendLine("  This is a DESCENT map (" + Routes.Count + " 투하구, " + Shafts.Count
                            + " 계단), so a runner who cannot finish has usually lost a 투하구, not met a step.");
                        sb.AppendLine("  The deepest walkable storey above is where the closure stopped. Check, in order:");
                        sb.AppendLine("    - a 투하구 whose landing is not on the rim below (DescentMap.HangChutes picks");
                        sb.AppendLine("      from the outer band's own cell list; a landing off it lands in rock)");
                        sb.AppendLine("    - a mouth or landing renamed out of its pair — MatchDirector.AttachChutes and");
                        sb.AppendLine("      this file both split on '" + ChuteLandingSuffix.Trim() + "' and must agree; this run paired "
                            + Routes.Count + " and left " + OrphanRoutes.Count + " unpaired");
                        sb.AppendLine("    - a gate wall sealing a storey's middle off from its rim, which shows above as");
                        sb.AppendLine("      places 'walled off from every start' on that floor");
                        sb.AppendLine("  A NavMesh audit cannot see any of it: the surface is one island per storey either");
                        sb.AppendLine("  way, because the monster cannot use a 투하구 either.");
                    }
                    else
                    {
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
                }

                return sb.ToString();
            }

            /// <summary>
            /// How many separate pockets the starting positions are spread over. More than
            /// one means the field is split before the race begins — legal, but it decides
            /// whether "65/65 reach the finish" is one fact or several.
            /// </summary>
            private int DistinctStartPockets()
            {
                var seen = new HashSet<int>();
                foreach (var pocket in StartPockets)
                {
                    if (pocket >= 0)
                    {
                        seen.Add(pocket);
                    }
                }

                return seen.Count;
            }

            /// <summary>
            /// The first failing clause of <see cref="Passed"/>, in the order a reader
            /// should act on them, so the headline line says what to go and look at rather
            /// than only that something is wrong.
            /// </summary>
            private string WhyNot()
            {
                if (Finish == null)
                {
                    return "no 출입구, so §02 has nowhere to win";
                }

                if (Truncated)
                {
                    return "the flood ran out of budget, so nothing below is a verdict";
                }

                if (Starts == 0 || StartsSynthesised)
                {
                    return "no PlayerSpawn markers, so there is no start→finish question to ask";
                }

                if (!FinishReached)
                {
                    return "nobody can win: the finish is in a pocket no runner reaches";
                }

                if (StartsThatFinish < Starts)
                {
                    return (Starts - StartsThatFinish) + " runners cannot reach the finish from where they spawn";
                }

                if (StoreysWalkable < Storeys.Count)
                {
                    return (Storeys.Count - StoreysWalkable) + " storeys no runner can set foot on";
                }

                if (RoutesUsable < Routes.Count || OrphanRoutes.Count > 0)
                {
                    return "a 투하구 is broken at one end";
                }

                if (Sites == 0)
                {
                    return "no 후보 지점, so §12 has no per-storey evidence";
                }

                return Unreachable.Count + " markers no runner can reach";
            }
        }
    }
}
