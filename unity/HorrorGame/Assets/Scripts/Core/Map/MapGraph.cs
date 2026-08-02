using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Map
{
    /// <summary>
    /// A straight walkable segment between two <see cref="MapNode"/>s.
    /// <para>
    /// Edges are straight by definition — a corridor that bends is two edges — which
    /// is what turns §12's "20m 넘는 직선 통로가 없다" and "S자 통로(10m×2)" from
    /// intentions into measurements. Every bend in the building is therefore a node,
    /// and the angle between two edges meeting at a node is the only thing that
    /// decides whether a sight line survives (§12 · <see
    /// cref="GameConstants.MapSightBreakingBendDegrees"/>).
    /// </para>
    /// </summary>
    public readonly struct MapEdge
    {
        /// <summary>Builds an edge. Prefer <see cref="MapGraphBuilder.Connect"/>, which measures the length from the node positions.</summary>
        /// <param name="id">Index into <see cref="MapGraph.Edges"/>.</param>
        /// <param name="a">One endpoint node id.</param>
        /// <param name="b">The other endpoint node id.</param>
        /// <param name="length">Walking length in metres. Never below the straight-line distance between the endpoints.</param>
        /// <param name="hasLockableDoor">
        /// True when the Engineer can lock this passage. §12 정비공: "순환로의 목에 문
        /// 하나 → 잠그면 순환이 끊김" — the door is a property of the passage, not of a
        /// room, because what it does is remove an edge from the graph.
        /// </param>
        /// <param name="name">Optional label, quoted verbatim in validator failures.</param>
        public MapEdge(int id, int a, int b, float length, bool hasLockableDoor, string? name)
        {
            Id = id;
            A = a;
            B = b;
            Length = length;
            HasLockableDoor = hasLockableDoor;
            Name = name;
        }

        /// <summary>Index into <see cref="MapGraph.Edges"/>.</summary>
        public int Id { get; }

        /// <summary>One endpoint. Edges are undirected; <see cref="A"/> and <see cref="B"/> are storage order only.</summary>
        public int A { get; }

        /// <summary>The other endpoint.</summary>
        public int B { get; }

        /// <summary>
        /// Walking length in metres. §12's every rule is a distance divided by a
        /// speed from §06, so this is the quantity the whole section is written in.
        /// </summary>
        public float Length { get; }

        /// <summary>
        /// True when this passage carries a lockable door. §12 caps these at
        /// <see cref="GameConstants.LockableDoorsPerZoneMax"/> per zone: "많으면
        /// 정비공이 만능이 된다."
        /// </summary>
        public bool HasLockableDoor { get; }

        /// <summary>Optional label used in validator and runner-test messages.</summary>
        public string? Name { get; }

        /// <summary>The endpoint at the far end from <paramref name="nodeId"/>, or -1 when the node is not an endpoint.</summary>
        public int Other(int nodeId)
        {
            if (nodeId == A)
            {
                return B;
            }

            return nodeId == B ? A : -1;
        }

        /// <summary>True when <paramref name="nodeId"/> is one of this edge's endpoints.</summary>
        public bool Touches(int nodeId) => nodeId == A || nodeId == B;

        /// <inheritdoc />
        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append('e').Append(Id);
            if (!string.IsNullOrEmpty(Name))
            {
                text.Append(' ').Append(Name);
            }

            text.Append(" (").Append(A).Append('–').Append(B).Append(", ")
                .Append(Length.ToString("0.#", CultureInfo.InvariantCulture)).Append(" m");
            if (HasLockableDoor)
            {
                text.Append(", 문");
            }

            text.Append(')');
            return text.ToString();
        }
    }

    /// <summary>
    /// The map as a graph of places and passages — the form in which §12 is
    /// executable.
    /// <para>
    /// §12 opens with "맵은 아트가 아니라 시스템이다", and this type is that sentence
    /// taken literally: every rule the section states is a query over this graph, so
    /// a map that breaks one fails <see cref="MapValidator"/> instead of failing a
    /// playtest. Nothing here is about appearance; a node is a point, an edge is a
    /// straight run, and a zone is a box with a floor.
    /// </para>
    /// <para>
    /// It is also the data an <see cref="IWorldProbe"/> can be built from — see
    /// <see cref="MapGraphProbe"/> — which is what lets the headless simulator run
    /// §06's monster against a real §12 map instead of against an open plane.
    /// </para>
    /// <para>
    /// Immutable once built. Door state is runtime, not map data, and lives on the
    /// probe (<see cref="MapGraphProbe.SetEdgeBlocked"/>) so that locking a door is a
    /// change to the world and not a change to the level.
    /// </para>
    /// </summary>
    public sealed class MapGraph
    {
        /// <summary>
        /// Vertical separation, metres, above which two places are on different storeys
        /// and nothing sees between them. Half of the kit's 3.75 m storey — far above
        /// the centimetres a ramp or a sunken bay contributes, far below a floor.
        /// </summary>
        private const float StoreyChangeMetres = 1.8f;

        private readonly MapZone[] _zones;
        private readonly MapNode[] _nodes;
        private readonly MapEdge[] _edges;

        // Adjacency as edge ids per node. Edge ids rather than neighbour ids so that
        // a caller can reach Length and HasLockableDoor without a second search,
        // and so parallel edges (two doorways between the same pair of rooms — §12
        // asks for 2~3 진입점 between zones) stay distinguishable.
        private readonly int[][] _incident;

        /// <summary>
        /// Assembles a graph. Prefer <see cref="MapGraphBuilder"/>; this constructor
        /// exists for a generator that already has the arrays.
        /// </summary>
        /// <param name="zones">Zones, indexed by <see cref="MapZone.Id"/>.</param>
        /// <param name="nodes">Nodes, indexed by <see cref="MapNode.Id"/>.</param>
        /// <param name="edges">Edges, indexed by <see cref="MapEdge.Id"/>.</param>
        /// <param name="name">Label for reports. Optional.</param>
        /// <exception cref="ArgumentNullException">Any array is null.</exception>
        /// <exception cref="ArgumentException">An id does not match its index, or an edge names a node that does not exist.</exception>
        public MapGraph(MapZone[] zones, MapNode[] nodes, MapEdge[] edges, string? name)
        {
            if (zones == null)
            {
                throw new ArgumentNullException(nameof(zones));
            }

            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            if (edges == null)
            {
                throw new ArgumentNullException(nameof(edges));
            }

            for (var i = 0; i < zones.Length; i++)
            {
                if (zones[i] == null || zones[i].Id != i)
                {
                    throw new ArgumentException(
                        "Zone at index " + i + " must be non-null and carry Id " + i
                        + ". Ids are array indices — every per-zone rule in §12 counts by index.",
                        nameof(zones));
                }
            }

            for (var i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Id != i)
                {
                    throw new ArgumentException(
                        "Node at index " + i + " carries Id " + nodes[i].Id + ". Ids are array indices.",
                        nameof(nodes));
                }
            }

            var counts = new int[nodes.Length];
            for (var i = 0; i < edges.Length; i++)
            {
                var e = edges[i];
                if (e.Id != i)
                {
                    throw new ArgumentException(
                        "Edge at index " + i + " carries Id " + e.Id + ". Ids are array indices.", nameof(edges));
                }

                if (e.A < 0 || e.A >= nodes.Length || e.B < 0 || e.B >= nodes.Length)
                {
                    throw new ArgumentException(
                        "Edge " + i + " names node " + e.A + "–" + e.B + ", outside 0.." + (nodes.Length - 1) + ".",
                        nameof(edges));
                }

                if (e.A == e.B)
                {
                    throw new ArgumentException(
                        "Edge " + i + " is a self-loop at node " + e.A
                        + ". A passage from a place to itself is not a 순환로 — §12's loops need two ways round.",
                        nameof(edges));
                }

                counts[e.A]++;
                counts[e.B]++;
            }

            _zones = zones;
            _nodes = nodes;
            _edges = edges;
            Name = name;

            _incident = new int[nodes.Length][];
            for (var i = 0; i < nodes.Length; i++)
            {
                _incident[i] = new int[counts[i]];
                counts[i] = 0;
            }

            for (var i = 0; i < edges.Length; i++)
            {
                var e = edges[i];
                _incident[e.A][counts[e.A]++] = i;
                _incident[e.B][counts[e.B]++] = i;
            }
        }

        /// <summary>Label for reports. Appears in <see cref="MapValidator"/> output.</summary>
        public string? Name { get; }

        /// <summary>Zones, indexed by id. §07 patrols in these and §12 counts almost everything per one.</summary>
        public MapZone[] Zones => _zones;

        /// <summary>Nodes, indexed by id.</summary>
        public MapNode[] Nodes => _nodes;

        /// <summary>Edges, indexed by id.</summary>
        public MapEdge[] Edges => _edges;

        /// <summary>Number of passages meeting at a node. §12's 막힌 길 is derived from this and nothing else.</summary>
        public int Degree(int nodeId) => _incident[nodeId].Length;

        /// <summary>Edge ids meeting at a node, in insertion order.</summary>
        public int[] IncidentEdges(int nodeId) => _incident[nodeId];

        /// <summary>
        /// True when this node is a 막힌 길: one way in and the same way out, and not
        /// the map exit.
        /// <para>
        /// Derived from topology rather than read from a flag on purpose. §12's
        /// 20~25% band is a statement about the shape that was actually built, so a
        /// designer who accidentally leaves a corridor unconnected must see the ratio
        /// move. <see cref="MapNodeKind.Entrance"/> is excluded because the way out
        /// of the building is a leaf that is the opposite of a dead end — demanding
        /// 막힌 길 보상 at the front door would be nonsense.
        /// </para>
        /// </summary>
        public bool IsDeadEnd(int nodeId) =>
            Degree(nodeId) <= 1 && !_nodes[nodeId].HasAny(MapNodeKind.Entrance);

        /// <summary>Node ids belonging to a zone, ascending.</summary>
        public int[] NodesInZone(int zoneId)
        {
            var found = new List<int>();
            for (var i = 0; i < _nodes.Length; i++)
            {
                if (_nodes[i].ZoneId == zoneId)
                {
                    found.Add(i);
                }
            }

            return found.ToArray();
        }

        /// <summary>
        /// Index of the zone whose box contains a position, or -1 outside the map.
        /// Total rather than ambiguous because <see cref="MapValidator"/> rejects
        /// overlapping zones (§12 "재질 경계를 명확히 할 것"); the first match wins so
        /// that an invalid map still answers instead of throwing.
        /// </summary>
        public int ZoneIdAt(Vec3 position)
        {
            for (var i = 0; i < _zones.Length; i++)
            {
                if (_zones[i].Contains(position))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// The surface underfoot at a position, or <see cref="FloorMaterial.None"/>
        /// off the map. Drives the Listener's whole ability (§12 청음사).
        /// </summary>
        public FloorMaterial FloorAt(Vec3 position)
        {
            var zone = ZoneIdAt(position);
            return zone < 0 ? FloorMaterial.None : _zones[zone].Floor;
        }

        /// <summary>
        /// Id of the node nearest a position, measured horizontally, or -1 on an
        /// empty graph. Height is ignored so that standing on a stairwell landing
        /// does not read as extra distance (§12's rules are all horizontal).
        /// </summary>
        public int NearestNode(Vec3 position)
        {
            var best = -1;
            var bestSqr = float.PositiveInfinity;
            for (var i = 0; i < _nodes.Length; i++)
            {
                var sqr = (_nodes[i].Position - position).SqrMagnitudeFlat;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Edge ids that cross between two zones. §12 caps these at
        /// <see cref="GameConstants.ZoneEntryPointsMax"/> so that §07's patrol radius
        /// means something at zone granularity.
        /// </summary>
        public int[] EdgesBetweenZones(int zoneA, int zoneB)
        {
            var found = new List<int>();
            for (var i = 0; i < _edges.Length; i++)
            {
                var za = _nodes[_edges[i].A].ZoneId;
                var zb = _nodes[_edges[i].B].ZoneId;
                if ((za == zoneA && zb == zoneB) || (za == zoneB && zb == zoneA))
                {
                    found.Add(i);
                }
            }

            return found.ToArray();
        }

        /// <summary>
        /// Turn, in degrees, that something walking in through <paramref name="inEdge"/>
        /// makes to leave along <paramref name="outEdge"/>. 0 is straight on, 180 is
        /// a reversal. Compared against
        /// <see cref="GameConstants.MapSightBreakingBendDegrees"/> to decide whether
        /// a sight line survives the corner.
        /// </summary>
        /// <exception cref="ArgumentException">Either edge does not touch the node.</exception>
        public float BendDegrees(int nodeId, int inEdge, int outEdge)
        {
            var from = _edges[inEdge].Other(nodeId);
            var to = _edges[outEdge].Other(nodeId);
            if (from < 0 || to < 0)
            {
                throw new ArgumentException(
                    "Edges " + inEdge + " and " + outEdge + " must both touch node " + nodeId + ".");
            }

            // A 계단 is a complete sight break and nothing else needs measuring.
            //
            // Every sight rule in this class asks this method, and it worked on .Flat —
            // it dropped Y. That was invisible while the storeys spiralled, because a
            // stair never continued a corridor's compass direction. Stacking three new
            // floors under B5 made two landings collinear with the passages either side,
            // and `straight-corridor` promptly reported a 25 m sight line running from
            // 병동 down into 수몰층 — through a concrete slab, past the switchback, and
            // out the other side. MapKitPiece.StairwellMetal is a 2 x 2 switchback with a
            // spine wall between the flights and MapSketch.Stair has said in its own
            // documentation from the beginning that "a stair always breaks a sight line".
            // It was true of the geometry and untrue of the measurement.
            //
            // 180° rather than merely clearing MapSightBreakingBendDegrees: this is not a
            // sharp corner, it is a floor. Nothing sees through it from any angle.
            var arrivingFull = _nodes[nodeId].Position - _nodes[from].Position;
            var leavingFull = _nodes[to].Position - _nodes[nodeId].Position;
            if (System.Math.Abs(arrivingFull.Y) > StoreyChangeMetres
                || System.Math.Abs(leavingFull.Y) > StoreyChangeMetres)
            {
                return 180f;
            }

            return MathX.AngleBetween(arrivingFull.Flat, leavingFull.Flat);
        }

        // ====================================================================
        // Distances. Dijkstra over nodes — dense, because a §12-legal map is
        // 100 × 100 m of hand-built rooms and never has enough nodes for a heap
        // to pay for itself.
        // ====================================================================

        /// <summary>
        /// Walking distance in metres between two nodes, or
        /// <see cref="float.PositiveInfinity"/> when no route exists.
        /// <para>
        /// <paramref name="excludedEdge"/> removes one passage from the graph, which
        /// is how §12's "잠그면 순환이 끊김" is measured: the value of a door is the
        /// detour it forces.
        /// </para>
        /// </summary>
        public float PathLength(int fromNode, int toNode, int excludedEdge)
        {
            if (fromNode < 0 || toNode < 0)
            {
                return float.PositiveInfinity;
            }

            if (fromNode == toNode)
            {
                return 0f;
            }

            var distance = Dijkstra(fromNode, excludedEdge, null);
            return distance[toNode];
        }

        /// <summary>Walking distance in metres between two nodes with the whole graph available.</summary>
        public float PathLength(int fromNode, int toNode) => PathLength(fromNode, toNode, -1);

        /// <summary>
        /// The shortest route between two nodes as a node-id list, or null when
        /// unreachable. Used by <see cref="RunnerTest"/> to walk a candidate escape
        /// and by a probe to hand out the next waypoint.
        /// </summary>
        public int[]? ShortestPath(int fromNode, int toNode, int excludedEdge)
        {
            if (fromNode < 0 || toNode < 0)
            {
                return null;
            }

            if (fromNode == toNode)
            {
                return new[] { fromNode };
            }

            var previous = new int[_nodes.Length];
            var distance = Dijkstra(fromNode, excludedEdge, previous);
            if (float.IsPositiveInfinity(distance[toNode]))
            {
                return null;
            }

            var reversed = new List<int>();
            for (var at = toNode; at >= 0; at = previous[at])
            {
                reversed.Add(at);
                if (at == fromNode)
                {
                    break;
                }
            }

            reversed.Reverse();
            return reversed.ToArray();
        }

        private float[] Dijkstra(int source, int excludedEdge, int[]? previous)
        {
            var distance = new float[_nodes.Length];
            var settled = new bool[_nodes.Length];
            for (var i = 0; i < distance.Length; i++)
            {
                distance[i] = float.PositiveInfinity;
                if (previous != null)
                {
                    previous[i] = -1;
                }
            }

            distance[source] = 0f;

            for (var step = 0; step < distance.Length; step++)
            {
                var current = -1;
                var best = float.PositiveInfinity;
                for (var i = 0; i < distance.Length; i++)
                {
                    if (!settled[i] && distance[i] < best)
                    {
                        best = distance[i];
                        current = i;
                    }
                }

                if (current < 0)
                {
                    break;
                }

                settled[current] = true;
                var incident = _incident[current];
                for (var k = 0; k < incident.Length; k++)
                {
                    var edgeId = incident[k];
                    if (edgeId == excludedEdge)
                    {
                        continue;
                    }

                    var next = _edges[edgeId].Other(current);
                    var candidate = distance[current] + _edges[edgeId].Length;
                    if (candidate < distance[next])
                    {
                        distance[next] = candidate;
                        if (previous != null)
                        {
                            previous[next] = current;
                        }
                    }
                }
            }

            return distance;
        }

        /// <summary>True when every node can be walked to from every other. A map in two pieces is two maps.</summary>
        public bool IsConnected => ConnectedComponentCount <= 1;

        /// <summary>
        /// Number of separately walkable pieces. Zero for an empty graph. Needed by
        /// <see cref="IndependentLoopCount"/>, because the cycle count of a forest is
        /// only zero if the components are counted correctly.
        /// </summary>
        public int ConnectedComponentCount => CountComponents(null, -1);

        private int CountComponents(bool[]? include, int excludedEdge)
        {
            var seen = new bool[_nodes.Length];
            var components = 0;
            var stack = new Stack<int>();

            for (var start = 0; start < _nodes.Length; start++)
            {
                if (seen[start] || (include != null && !include[start]))
                {
                    continue;
                }

                components++;
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var at = stack.Pop();
                    var incident = _incident[at];
                    for (var k = 0; k < incident.Length; k++)
                    {
                        if (incident[k] == excludedEdge)
                        {
                            continue;
                        }

                        var next = _edges[incident[k]].Other(at);
                        if (seen[next] || (include != null && !include[next]))
                        {
                            continue;
                        }

                        seen[next] = true;
                        stack.Push(next);
                    }
                }
            }

            return components;
        }

        // ====================================================================
        // §12 순환로 — "트리 구조는 사형선고."
        // ====================================================================

        /// <summary>
        /// Number of independent 순환로 in the whole map: the dimension of the graph's
        /// cycle space, <c>E − V + components</c>.
        /// <para>
        /// This is the honest count, not a heuristic. §12 stakes the map on it — "트리
        /// 구조는 사형선고" — because a tree gives the monster no wrong guess to make,
        /// and a tree is exactly a connected graph whose cycle space is empty. The
        /// formula is the rank of the cycle space of the graph, so it counts loops
        /// that are genuinely independent: adding one passage that closes a ring adds
        /// exactly one, and a figure-of-eight counts as two rather than three even
        /// though three closed walks exist in it.
        /// </para>
        /// </summary>
        public int IndependentLoopCount =>
            _nodes.Length == 0 ? 0 : _edges.Length - _nodes.Length + ConnectedComponentCount;

        /// <summary>
        /// Independent 순환로 wholly inside one zone. §12 asks for
        /// <see cref="GameConstants.LoopsPerZoneMin"/> per zone as well as
        /// <see cref="GameConstants.LoopsTotalMin"/> map-wide, so a map that piles
        /// every loop into the hall must still fail.
        /// <para>
        /// Only edges with both ends in the zone are counted: a ring that leaves the
        /// zone and comes back is a map-wide loop, and the per-zone rule exists so
        /// that a Runner cornered <em>inside</em> a zone has somewhere to go.
        /// </para>
        /// </summary>
        public int IndependentLoopCountInZone(int zoneId)
        {
            var include = new bool[_nodes.Length];
            var vertices = 0;
            for (var i = 0; i < _nodes.Length; i++)
            {
                if (_nodes[i].ZoneId == zoneId)
                {
                    include[i] = true;
                    vertices++;
                }
            }

            if (vertices == 0)
            {
                return 0;
            }

            var interior = 0;
            for (var i = 0; i < _edges.Length; i++)
            {
                if (include[_edges[i].A] && include[_edges[i].B])
                {
                    interior++;
                }
            }

            var components = CountComponentsWithin(include);
            return interior - vertices + components;
        }

        private int CountComponentsWithin(bool[] include)
        {
            var seen = new bool[_nodes.Length];
            var components = 0;
            var stack = new Stack<int>();

            for (var start = 0; start < _nodes.Length; start++)
            {
                if (seen[start] || !include[start])
                {
                    continue;
                }

                components++;
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var at = stack.Pop();
                    var incident = _incident[at];
                    for (var k = 0; k < incident.Length; k++)
                    {
                        var next = _edges[incident[k]].Other(at);
                        if (!include[next] || seen[next])
                        {
                            continue;
                        }

                        seen[next] = true;
                        stack.Push(next);
                    }
                }
            }

            return components;
        }

        /// <summary>
        /// Finds one actual 순환로 and returns it as a closed node ring (first node
        /// repeated at the end), or null when the map is a forest.
        /// <para>
        /// <see cref="IndependentLoopCount"/> answers "how many"; this answers
        /// "where", which is what a designer needs when the count is short. It is a
        /// DFS back-edge search, so the ring it returns is a real walk the monster
        /// could be sent the wrong way round.
        /// </para>
        /// </summary>
        public int[]? FindLoop()
        {
            var parentNode = new int[_nodes.Length];
            var parentEdge = new int[_nodes.Length];
            var visited = new bool[_nodes.Length];
            for (var i = 0; i < _nodes.Length; i++)
            {
                parentNode[i] = -1;
                parentEdge[i] = -1;
            }

            for (var root = 0; root < _nodes.Length; root++)
            {
                if (visited[root])
                {
                    continue;
                }

                visited[root] = true;
                var stack = new Stack<int>();
                stack.Push(root);

                while (stack.Count > 0)
                {
                    var at = stack.Pop();
                    var incident = _incident[at];
                    for (var k = 0; k < incident.Length; k++)
                    {
                        var edgeId = incident[k];
                        if (edgeId == parentEdge[at])
                        {
                            continue;
                        }

                        var next = _edges[edgeId].Other(at);
                        if (!visited[next])
                        {
                            visited[next] = true;
                            parentNode[next] = at;
                            parentEdge[next] = edgeId;
                            stack.Push(next);
                            continue;
                        }

                        // A second route to an already-visited node closes a ring.
                        // Walk both nodes back to their common ancestor to name it.
                        var ring = RingThrough(at, next, parentNode);
                        if (ring != null)
                        {
                            return ring;
                        }
                    }
                }
            }

            return null;
        }

        private static int[]? RingThrough(int from, int to, int[] parentNode)
        {
            var ancestorsOfFrom = new HashSet<int>();
            for (var at = from; at >= 0; at = parentNode[at])
            {
                ancestorsOfFrom.Add(at);
                if (ancestorsOfFrom.Count > parentNode.Length)
                {
                    return null;
                }
            }

            var tail = new List<int>();
            for (var at = to; at >= 0; at = parentNode[at])
            {
                tail.Add(at);
                if (ancestorsOfFrom.Contains(at))
                {
                    var meet = at;
                    var ring = new List<int>();
                    for (var walk = from; walk != meet; walk = parentNode[walk])
                    {
                        ring.Add(walk);
                    }

                    ring.Add(meet);
                    for (var i = tail.Count - 2; i >= 0; i--)
                    {
                        ring.Add(tail[i]);
                    }

                    // Three entries is the shortest honest ring: two rooms joined by
                    // two separate doorways, written closed as a–b–a. §12 asks for
                    // 2~3 진입점 between zones, so that shape is legal and must be
                    // reported rather than skipped.
                    ring.Add(from);
                    return ring.Count >= 3 ? ring.ToArray() : null;
                }

                if (tail.Count > parentNode.Length + 1)
                {
                    return null;
                }
            }

            return null;
        }

        // ====================================================================
        // §12 직선 통로 · S자 통로 · 시야.
        // ====================================================================

        /// <summary>
        /// The longest run of corridor a sight line survives, in metres, together
        /// with the node chain that makes it.
        /// <para>
        /// A run continues through a node whenever some passage leaves it turning by
        /// less than <see cref="GameConstants.MapSightBreakingBendDegrees"/> — a
        /// T-junction does not break a sight line that carries straight on through
        /// it. §12 caps this at <see cref="GameConstants.MaxStraightCorridor"/>:
        /// "넘으면 주자가 죽는다", because a Runner in a longer straight cannot reach
        /// cover before the monster closes.
        /// </para>
        /// </summary>
        /// <param name="chain">Node ids of the longest run, or an empty array when the map has no edges.</param>
        /// <returns>Length of that run in metres; 0 when there are no edges.</returns>
        public float LongestStraightRun(out int[] chain)
        {
            var best = 0f;
            int[] bestChain = Array.Empty<int>();

            for (var edgeId = 0; edgeId < _edges.Length; edgeId++)
            {
                for (var flip = 0; flip < 2; flip++)
                {
                    var start = flip == 0 ? _edges[edgeId].A : _edges[edgeId].B;
                    var head = _edges[edgeId].Other(start);
                    var used = new bool[_edges.Length];
                    used[edgeId] = true;
                    var path = new List<int> { start, head };
                    ExtendStraightRun(head, edgeId, _edges[edgeId].Length, path, used, ref best, ref bestChain);
                }
            }

            chain = bestChain;
            return best;
        }

        private void ExtendStraightRun(
            int at,
            int arrivedBy,
            float accumulated,
            List<int> path,
            bool[] used,
            ref float best,
            ref int[] bestChain)
        {
            if (accumulated > best)
            {
                best = accumulated;
                bestChain = path.ToArray();
            }

            // Once the run is already illegal there is nothing left to learn by
            // extending it — the validator only needs one witness, and stopping here
            // is what keeps the search finite on a densely looped map (§12 requires
            // loops, so a near-straight ring would otherwise be walked forever).
            if (accumulated > GameConstants.MaxStraightCorridor)
            {
                return;
            }

            var incident = _incident[at];
            for (var k = 0; k < incident.Length; k++)
            {
                var edgeId = incident[k];
                if (used[edgeId])
                {
                    continue;
                }

                if (BendDegrees(at, arrivedBy, edgeId) >= GameConstants.MapSightBreakingBendDegrees)
                {
                    continue;
                }

                var next = _edges[edgeId].Other(at);
                used[edgeId] = true;
                path.Add(next);
                ExtendStraightRun(next, edgeId, accumulated + _edges[edgeId].Length, path, used, ref best, ref bestChain);
                path.RemoveAt(path.Count - 1);
                used[edgeId] = false;
            }
        }

        /// <summary>
        /// True when a sight line runs from one node to another: a chain of passages
        /// with no bend that reaches
        /// <see cref="GameConstants.MapSightBreakingBendDegrees"/>.
        /// <para>
        /// This is the graph's answer to <see cref="IWorldProbe.HasLineOfSight"/>.
        /// §12's entire S-corridor argument is that path length and sight line
        /// diverge; modelling sight as "straight-run reachability" is what makes that
        /// divergence exist in the simulator instead of having to be faked.
        /// </para>
        /// </summary>
        public bool HasStraightSightLine(int fromNode, int toNode) =>
            HasStraightSightLine(fromNode, toNode, -1);

        /// <summary>
        /// As <see cref="HasStraightSightLine(int, int)"/>, with one passage treated
        /// as blocked — a locked door or a barricade (§04).
        /// </summary>
        public bool HasStraightSightLine(int fromNode, int toNode, int blockedEdge)
        {
            if (fromNode < 0 || toNode < 0)
            {
                return false;
            }

            if (fromNode == toNode)
            {
                return true;
            }

            // State is (node arrived at, edge arrived by): the same node reached
            // along a different corridor continues the line in a different direction.
            var seen = new bool[_edges.Length * 2];
            var queue = new Queue<int>();

            var startEdges = _incident[fromNode];
            for (var k = 0; k < startEdges.Length; k++)
            {
                var edgeId = startEdges[k];
                if (edgeId == blockedEdge)
                {
                    continue;
                }

                var next = _edges[edgeId].Other(fromNode);
                if (next == toNode)
                {
                    return true;
                }

                var state = (edgeId * 2) + (next == _edges[edgeId].B ? 0 : 1);
                if (!seen[state])
                {
                    seen[state] = true;
                    queue.Enqueue(state);
                }
            }

            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                var arrivedBy = state / 2;
                var at = (state % 2) == 0 ? _edges[arrivedBy].B : _edges[arrivedBy].A;

                var incident = _incident[at];
                for (var k = 0; k < incident.Length; k++)
                {
                    var edgeId = incident[k];
                    if (edgeId == arrivedBy || edgeId == blockedEdge)
                    {
                        continue;
                    }

                    if (BendDegrees(at, arrivedBy, edgeId) >= GameConstants.MapSightBreakingBendDegrees)
                    {
                        continue;
                    }

                    var next = _edges[edgeId].Other(at);
                    if (next == toNode)
                    {
                        return true;
                    }

                    var nextState = (edgeId * 2) + (next == _edges[edgeId].B ? 0 : 1);
                    if (!seen[nextState])
                    {
                        seen[nextState] = true;
                        queue.Enqueue(nextState);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Finds an S자 통로 inside a zone and returns its four node ids, or null when
        /// the zone has none.
        /// <para>
        /// §12 specifies the structure numerically — "10m 구간 2개 · 2회 굽음 · 총 20m
        /// · 통과 4.2초 &gt; 차단 3초" — so that is what is searched for: a path whose
        /// two outer legs are each at least
        /// <see cref="GameConstants.SCorridorLegLength"/>, joined by a connector, with
        /// a qualifying bend at <em>both</em> interior nodes. Two bends is what makes
        /// it an S rather than a single corner, and a single corner is precisely the
        /// structure §12 proves insufficient.
        /// </para>
        /// <para>
        /// Searched inside a zone because that is how §12 requires it — one per zone —
        /// so a chain that wanders into the next zone does not satisfy the rule for
        /// either.
        /// </para>
        /// </summary>
        public int[]? FindSCorridor(int zoneId)
        {
            for (var firstLeg = 0; firstLeg < _edges.Length; firstLeg++)
            {
                if (_edges[firstLeg].Length < GameConstants.SCorridorLegLength)
                {
                    continue;
                }

                for (var flip = 0; flip < 2; flip++)
                {
                    var n0 = flip == 0 ? _edges[firstLeg].A : _edges[firstLeg].B;
                    var n1 = _edges[firstLeg].Other(n0);
                    if (_nodes[n0].ZoneId != zoneId || _nodes[n1].ZoneId != zoneId)
                    {
                        continue;
                    }

                    var atN1 = _incident[n1];
                    for (var a = 0; a < atN1.Length; a++)
                    {
                        var connector = atN1[a];
                        if (connector == firstLeg)
                        {
                            continue;
                        }

                        var n2 = _edges[connector].Other(n1);
                        if (n2 == n0 || _nodes[n2].ZoneId != zoneId)
                        {
                            continue;
                        }

                        if (BendDegrees(n1, firstLeg, connector) < GameConstants.MapSightBreakingBendDegrees)
                        {
                            continue;
                        }

                        var atN2 = _incident[n2];
                        for (var b = 0; b < atN2.Length; b++)
                        {
                            var secondLeg = atN2[b];
                            if (secondLeg == connector || secondLeg == firstLeg)
                            {
                                continue;
                            }

                            if (_edges[secondLeg].Length < GameConstants.SCorridorLegLength)
                            {
                                continue;
                            }

                            var n3 = _edges[secondLeg].Other(n2);
                            if (n3 == n1 || n3 == n0 || _nodes[n3].ZoneId != zoneId)
                            {
                                continue;
                            }

                            if (BendDegrees(n2, connector, secondLeg) < GameConstants.MapSightBreakingBendDegrees)
                            {
                                continue;
                            }

                            var total = _edges[firstLeg].Length + _edges[connector].Length + _edges[secondLeg].Length;
                            if (total + MathX.Epsilon < GameConstants.SCorridorLegLength * 2f)
                            {
                                continue;
                            }

                            return new[] { n0, n1, n2, n3 };
                        }
                    }
                }
            }

            return null;
        }

        // ====================================================================
        // §12 정비공 — 병목.
        // ====================================================================

        /// <summary>
        /// Walking distance between a passage's two ends when that passage is shut, in
        /// metres, or <see cref="float.PositiveInfinity"/> when shutting it separates
        /// them entirely.
        /// </summary>
        public float DetourWithout(int edgeId) =>
            PathLength(_edges[edgeId].A, _edges[edgeId].B, edgeId);

        /// <summary>
        /// True when a passage is a 병목 — somewhere a door is worth hanging.
        /// <para>
        /// §12 says a door belongs "순환로의 목에 … 잠그면 순환이 끊김", and the value of
        /// cutting the loop is the time the detour costs. So the threshold comes
        /// straight out of §06 rather than being invented: the way round must be at
        /// least <see cref="GameConstants.SingleCornerMinDistance"/> longer than the
        /// passage itself — 14.4 m, which is
        /// <see cref="GameConstants.AggroReleaseLineOfSightBreak"/> at
        /// <see cref="GameConstants.MonsterBaseSpeed"/>. Below that, locking the door
        /// does not even buy the 3 s of cover an aggro release needs, and the
        /// Engineer has spent 3.5 s and a material on nothing. A passage whose loss
        /// disconnects the map qualifies outright.
        /// </para>
        /// </summary>
        public bool IsBottleneck(int edgeId)
        {
            var detour = DetourWithout(edgeId);
            if (float.IsPositiveInfinity(detour))
            {
                return true;
            }

            return detour - _edges[edgeId].Length >= GameConstants.SingleCornerMinDistance;
        }

        /// <summary>
        /// True when locking a passage removes a 순환로 — §12's "잠그면 순환이 끊김".
        /// Reported alongside <see cref="IsBottleneck"/> so a designer can tell a
        /// loop-cutting door from a corridor-sealing one.
        /// </summary>
        public bool CutsALoop(int edgeId) => !float.IsPositiveInfinity(DetourWithout(edgeId));

        // ====================================================================
        // Node queries the §12 checklist counts.
        // ====================================================================

        /// <summary>Node ids in a zone carrying every flag in <paramref name="kind"/>.</summary>
        public int[] NodesOfKindInZone(int zoneId, MapNodeKind kind)
        {
            var found = new List<int>();
            for (var i = 0; i < _nodes.Length; i++)
            {
                if (_nodes[i].ZoneId == zoneId && _nodes[i].Has(kind))
                {
                    found.Add(i);
                }
            }

            return found.ToArray();
        }

        /// <summary>Node ids anywhere on the map carrying every flag in <paramref name="kind"/>.</summary>
        public int[] NodesOfKind(MapNodeKind kind)
        {
            var found = new List<int>();
            for (var i = 0; i < _nodes.Length; i++)
            {
                if (_nodes[i].Has(kind))
                {
                    found.Add(i);
                }
            }

            return found.ToArray();
        }

        /// <summary>Passages carrying a lockable door whose two ends are both in a zone, plus those leading out of it.</summary>
        public int[] LockableDoorsInZone(int zoneId)
        {
            var found = new List<int>();
            for (var i = 0; i < _edges.Length; i++)
            {
                if (!_edges[i].HasLockableDoor)
                {
                    continue;
                }

                if (_nodes[_edges[i].A].ZoneId == zoneId || _nodes[_edges[i].B].ZoneId == zoneId)
                {
                    found.Add(i);
                }
            }

            return found.ToArray();
        }

        /// <summary>
        /// Every node id whose horizontal distance from <paramref name="nodeId"/> is
        /// within <paramref name="radius"/> metres by walking, itself excluded. Used
        /// for §12's "근처에 시야 차단 지점" and "출입구 근처에 은폐 지점".
        /// </summary>
        public int[] NodesWithinWalk(int nodeId, float radius)
        {
            var distance = Dijkstra(nodeId, -1, null);
            var found = new List<int>();
            for (var i = 0; i < distance.Length; i++)
            {
                if (i != nodeId && distance[i] <= radius)
                {
                    found.Add(i);
                }
            }

            return found.ToArray();
        }

        /// <summary>
        /// True when this node is somewhere a sight line can be broken: a bend of at
        /// least <see cref="GameConstants.MapSightBreakingBendDegrees"/> between two
        /// of its passages, or a junction a pursuer has to guess at. §12's 시야 차단
        /// 지점, derived from geometry rather than declared.
        /// </summary>
        public bool IsSightBreakingCorner(int nodeId)
        {
            var incident = _incident[nodeId];
            for (var i = 0; i < incident.Length; i++)
            {
                for (var j = i + 1; j < incident.Length; j++)
                {
                    if (BendDegrees(nodeId, incident[i], incident[j]) >= GameConstants.MapSightBreakingBendDegrees)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Axis-aligned footprint the zones occupy, as (min, max) in metres. §12 pins
        /// the map at <see cref="GameConstants.MapExtent"/> square: big enough for a
        /// Runner to cross two or three zones, small enough that it can.
        /// </summary>
        public void Footprint(out Vec3 min, out Vec3 max)
        {
            if (_zones.Length == 0)
            {
                min = Vec3.Zero;
                max = Vec3.Zero;
                return;
            }

            var lo = _zones[0].Min;
            var hi = _zones[0].Max;
            for (var i = 1; i < _zones.Length; i++)
            {
                var zMin = _zones[i].Min;
                var zMax = _zones[i].Max;
                lo = new Vec3(
                    zMin.X < lo.X ? zMin.X : lo.X,
                    zMin.Y < lo.Y ? zMin.Y : lo.Y,
                    zMin.Z < lo.Z ? zMin.Z : lo.Z);
                hi = new Vec3(
                    zMax.X > hi.X ? zMax.X : hi.X,
                    zMax.Y > hi.Y ? zMax.Y : hi.Y,
                    zMax.Z > hi.Z ? zMax.Z : hi.Z);
            }

            min = lo;
            max = hi;
        }

        /// <inheritdoc />
        public override string ToString() =>
            (string.IsNullOrEmpty(Name) ? "map" : Name)
            + ": " + _zones.Length + " zones, " + _nodes.Length + " nodes, "
            + _edges.Length + " edges, " + IndependentLoopCount + " loops";
    }

    /// <summary>
    /// Fluent construction of a <see cref="MapGraph"/>.
    /// <para>
    /// It exists because §12's 첫 맵 스케치 has to be expressible in a test. §14 puts
    /// the prototype at "B + A + S자 통로", which means the first thing anybody builds
    /// is a hand-made map — so the fixture API is not a convenience, it is how the
    /// design's own example gets checked before a level exists.
    /// </para>
    /// <para>
    /// <see cref="Connect"/> measures edge length from the node positions, so a
    /// designer cannot declare a 10 m leg and draw a 40 m one. That is deliberate:
    /// every §12 rule is a distance, and a length that disagrees with the geometry
    /// would let a map pass validation and still kill the Runner.
    /// </para>
    /// </summary>
    public sealed class MapGraphBuilder
    {
        private readonly List<MapZone> _zones = new List<MapZone>();
        private readonly List<MapNode> _nodes = new List<MapNode>();
        private readonly List<MapEdge> _edges = new List<MapEdge>();
        private string? _name;

        /// <summary>Names the map. Appears in every validator report about it.</summary>
        public MapGraphBuilder Named(string name)
        {
            _name = name;
            return this;
        }

        /// <summary>Zones added so far, so a caller can address the one it just made.</summary>
        public int ZoneCount => _zones.Count;

        /// <summary>Nodes added so far.</summary>
        public int NodeCount => _nodes.Count;

        /// <summary>Edges added so far.</summary>
        public int EdgeCount => _edges.Count;

        /// <summary>
        /// Adds a zone and returns its id.
        /// </summary>
        /// <param name="name">§12's sketch labels them A/B/C/D; the label is quoted in failures.</param>
        /// <param name="floor">Must be distinct per zone — §12 청음사.</param>
        /// <param name="center">Centre in metres.</param>
        /// <param name="size">Full extent in metres. The XZ diagonal must land in §12's 30~40 m band.</param>
        public int AddZone(string name, FloorMaterial floor, Vec3 center, Vec3 size)
        {
            var id = _zones.Count;
            _zones.Add(new MapZone(id, name, floor, center, size));
            return id;
        }

        /// <summary>
        /// Adds a node and returns its id.
        /// </summary>
        /// <param name="zoneId">Owning zone. Must already exist.</param>
        /// <param name="position">Metres, world space.</param>
        /// <param name="kind">Which §12 requirements this place answers.</param>
        /// <param name="name">Optional label.</param>
        /// <param name="deadEndRewardValue">
        /// §08 credits waiting here. §12 requires this above zero on every 막힌 길 —
        /// "위험을 감수할 이유".
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">The zone does not exist.</exception>
        public int AddNode(int zoneId, Vec3 position, MapNodeKind kind, string? name, int deadEndRewardValue)
        {
            if (zoneId < 0 || zoneId >= _zones.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(zoneId), zoneId,
                    "Add the zone before its nodes — every per-zone rule in §12 counts by zone id.");
            }

            var id = _nodes.Count;
            _nodes.Add(new MapNode(id, zoneId, position, kind, deadEndRewardValue, name));
            return id;
        }

        /// <summary>Adds a plain junction with no reward and no declared purpose.</summary>
        public int AddNode(int zoneId, Vec3 position, MapNodeKind kind, string? name) =>
            AddNode(zoneId, position, kind, name, 0);

        /// <summary>Adds an unnamed junction.</summary>
        public int AddNode(int zoneId, Vec3 position) =>
            AddNode(zoneId, position, MapNodeKind.None, null, 0);

        /// <summary>
        /// Joins two nodes with a straight passage and returns its edge id. Length is
        /// measured horizontally from the node positions — see the type remarks.
        /// </summary>
        /// <param name="a">One endpoint.</param>
        /// <param name="b">The other endpoint.</param>
        /// <param name="hasLockableDoor">§12 정비공: 1~2 per zone, and only at a 병목.</param>
        /// <param name="name">Optional label.</param>
        /// <exception cref="ArgumentOutOfRangeException">A node does not exist.</exception>
        /// <exception cref="ArgumentException">The two nodes are the same.</exception>
        public int Connect(int a, int b, bool hasLockableDoor, string? name)
        {
            if (a < 0 || a >= _nodes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(a), a, "No such node.");
            }

            if (b < 0 || b >= _nodes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(b), b, "No such node.");
            }

            if (a == b)
            {
                throw new ArgumentException(
                    "A passage from a place to itself is not a 순환로 (§12) — loops need two distinct ways round.",
                    nameof(b));
            }

            var length = Vec3.DistanceFlat(_nodes[a].Position, _nodes[b].Position);
            var id = _edges.Count;
            _edges.Add(new MapEdge(id, a, b, length, hasLockableDoor, name));
            return id;
        }

        /// <summary>Joins two nodes with an ordinary passage.</summary>
        public int Connect(int a, int b) => Connect(a, b, false, null);

        /// <summary>
        /// Adds a chain of nodes through the given positions, joining each to the
        /// next, and returns their ids. The way an S-corridor or a ring is written:
        /// the bends are the intermediate positions.
        /// </summary>
        /// <param name="zoneId">Zone every node in the chain belongs to.</param>
        /// <param name="kind">Applied to every node in the chain.</param>
        /// <param name="positions">Two or more positions in metres.</param>
        /// <exception cref="ArgumentException">Fewer than two positions.</exception>
        public int[] AddChain(int zoneId, MapNodeKind kind, params Vec3[] positions)
        {
            if (positions == null || positions.Length < 2)
            {
                throw new ArgumentException("A chain needs at least two positions.", nameof(positions));
            }

            var ids = new int[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                ids[i] = AddNode(zoneId, positions[i], kind, null, 0);
                if (i > 0)
                {
                    Connect(ids[i - 1], ids[i]);
                }
            }

            return ids;
        }

        /// <summary>Freezes the graph.</summary>
        public MapGraph Build() => new MapGraph(_zones.ToArray(), _nodes.ToArray(), _edges.ToArray(), _name);
    }

    /// <summary>
    /// An <see cref="IWorldProbe"/> answered from a <see cref="MapGraph"/>.
    /// <para>
    /// This is what lets the headless simulator run §06's monster over a §12 map:
    /// path length comes from Dijkstra along the passages, and a sight line survives
    /// only a run of corridor with no real bend in it. Those two answers disagreeing
    /// is exactly the gap §12 builds S-corridors out of — "통과 시간 = 20 / 4.8 =
    /// 4.2초 ≥ 3초" — so a monster driven through this probe loses its target for the
    /// same reason it would in the shipped game.
    /// </para>
    /// <para>
    /// Blocked passages are runtime state: locking a door (§04) is a change to the
    /// world, not to the level, so it lives here and not in the graph.
    /// </para>
    /// </summary>
    public sealed class MapGraphProbe : IWorldProbe
    {
        private readonly MapGraph _graph;
        private readonly bool[] _blocked;
        private readonly bool[] _litZones;

        /// <summary>Wraps a graph. Every passage starts open and every zone starts dark (§03).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is null.</exception>
        public MapGraphProbe(MapGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _blocked = new bool[graph.Edges.Length];
            _litZones = new bool[graph.Zones.Length];
        }

        /// <summary>The graph being answered from, for callers that want a map query rather than a probe query.</summary>
        public MapGraph Graph => _graph;

        /// <summary>
        /// Shuts or reopens a passage — the Engineer's locked door or barricade (§04).
        /// A shut passage stops both walking and sight, which is what makes §12's
        /// "잠그면 순환이 끊김" a real change to the map's topology.
        /// </summary>
        public void SetEdgeBlocked(int edgeId, bool blocked) => _blocked[edgeId] = blocked;

        /// <summary>True when a passage is currently shut.</summary>
        public bool IsEdgeBlocked(int edgeId) => _blocked[edgeId];

        /// <summary>Brings a zone up or takes it down (§04 정비공 · §03 zone light).</summary>
        public void SetZoneLit(int zoneId, bool lit) => _litZones[zoneId] = lit;

        /// <inheritdoc />
        public bool HasLineOfSight(Vec3 from, Vec3 to)
        {
            var a = _graph.NearestNode(from);
            var b = _graph.NearestNode(to);
            if (a < 0 || b < 0)
            {
                return false;
            }

            return _graph.HasStraightSightLine(a, b, FirstBlockedEdge());
        }

        /// <inheritdoc />
        public float NavigableDistance(Vec3 from, Vec3 to)
        {
            var a = _graph.NearestNode(from);
            var b = _graph.NearestNode(to);
            if (a < 0 || b < 0)
            {
                return float.PositiveInfinity;
            }

            var along = _graph.PathLength(a, b, FirstBlockedEdge());
            if (float.IsPositiveInfinity(along))
            {
                return float.PositiveInfinity;
            }

            // Add the walk from the real positions onto the graph at both ends,
            // so that standing halfway down a corridor is not free.
            return along
                   + Vec3.DistanceFlat(from, _graph.Nodes[a].Position)
                   + Vec3.DistanceFlat(to, _graph.Nodes[b].Position);
        }

        /// <inheritdoc />
        public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
        {
            next = from;
            var a = _graph.NearestNode(from);
            var b = _graph.NearestNode(to);
            if (a < 0 || b < 0)
            {
                return false;
            }

            var route = _graph.ShortestPath(a, b, FirstBlockedEdge());
            if (route == null)
            {
                return false;
            }

            // The first hop is the node being left; aim at the one after it, or at
            // the destination itself once there is nothing left to round.
            if (route.Length >= 2)
            {
                next = _graph.Nodes[route[1]].Position;
                return true;
            }

            next = to;
            return true;
        }

        /// <inheritdoc />
        public FloorMaterial SampleFloor(Vec3 position) => _graph.FloorAt(position);

        /// <inheritdoc />
        public int ZoneIdAt(Vec3 position) => _graph.ZoneIdAt(position);

        /// <inheritdoc />
        public Vec3 SnapToNavigable(Vec3 desired)
        {
            var node = _graph.NearestNode(desired);
            return node < 0 ? desired : _graph.Nodes[node].Position;
        }

        /// <inheritdoc />
        public bool IsAreaLit(Vec3 position)
        {
            var zone = _graph.ZoneIdAt(position);
            return zone >= 0 && _litZones[zone];
        }

        // The graph queries take a single excluded edge, which covers the case that
        // matters — one door on the neck of one loop (§12). More than one shut
        // passage at once is a §04 barricade scenario the simulator does not need
        // yet; returning the lowest shut edge keeps the answer conservative and
        // deterministic instead of silently ignoring all of them.
        private int FirstBlockedEdge()
        {
            for (var i = 0; i < _blocked.Length; i++)
            {
                if (_blocked[i])
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
