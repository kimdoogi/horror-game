#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;
using UnityEngine;
using UnityEngine.AI;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// The Unity answer to <see cref="IWorldProbe"/>: raycasts for sight, the NavMesh
    /// for walking, and the map's zone table for what is underfoot.
    /// <para>
    /// The single most important line in this class is that
    /// <see cref="NavigableDistance"/> calls <see cref="NavMesh.CalculatePath"/> and
    /// sums the corners, rather than returning <c>Vector3.Distance</c>. §12's entire
    /// map argument is that walked length and sight line diverge — "S자 통로 20 m =
    /// 4.2초 &gt; 차단 3초" — and the Runner escapes through exactly that gap. A
    /// straight-line answer here would silently delete the gap on every map, and
    /// nothing downstream would report it: the monster would just always be there.
    /// </para>
    /// <para>
    /// The probe is deliberately not a <c>MonoBehaviour</c> and holds no reference to
    /// the monster. <see cref="IWorldProbe"/> is asked by the Listener and the map
    /// queries too (ARCHITECTURE §3), so nothing in here may assume the asker.
    /// </para>
    /// <para>
    /// Every query is side-effect free and allocation-free after construction, because
    /// <see cref="HorrorGame.Core.Monster.MonsterBrain"/> asks several of them per
    /// sub-step at <c>GameConstants.FixedStep</c> and a per-tick allocation would be
    /// 50 garbage collections a second.
    /// </para>
    /// </summary>
    public sealed class NavMeshWorldProbe : IWorldProbe
    {
        /// <summary>
        /// How far above and below a queried point the floor raycast searches, in
        /// body heights. A span, not a tuned value: one body height up clears any
        /// step the creature could be standing on, and two down still finds the floor
        /// from the top of a stair.
        /// </summary>
        private const float FloorProbeSpanInBodies = 2f;

        /// <summary>
        /// Corner buffer growth. <see cref="NavMeshPath.GetCornersNonAlloc"/> silently
        /// truncates, and a truncated path reads as a shorter walk — which would hand
        /// the monster a §12 shortcut that does not exist.
        /// </summary>
        private const int InitialCornerCapacity = 64;

        /// <summary>
        /// §12's five surfaces and the words that identify them in an asset name,
        /// built once. <see cref="FloorMaterial.None"/> is not in the table: it is the
        /// answer when nothing matched, never something to match against.
        /// </summary>
        private static readonly FloorMaterial[] FloorMaterials = BuildFloorMaterials();

        private static readonly string[] FloorMaterialNames = BuildFloorMaterialNames();

        private readonly NavMeshPath _path = new NavMeshPath();
        private readonly Dictionary<int, FloorMaterial> _floorByCollider = new Dictionary<int, FloorMaterial>();

        private readonly float _eyeHeight;
        private readonly float _targetSightHeight;
        private readonly float _bodyRadius;
        private readonly float _bodyHeight;
        private readonly float _sampleRadius;
        private readonly int _sightBlockers;
        private readonly int _floorLayers;
        private readonly int _areaMask;

        private Vector3[] _corners = new Vector3[InitialCornerCapacity];
        private int _cornerCount;

        // One-entry path memo. MonsterBrain asks IsReachable(destination) and then
        // TryGetNextPathPoint(position, destination) with the same pair inside one
        // sub-step, so a single slot removes half of the CalculatePath calls without
        // ever answering from a stale world: the memo is keyed on the exact query and
        // cleared by ClearCache() at the top of every tick.
        private bool _memoValid;
        private bool _memoComplete;
        private Vector3 _memoFrom;
        private Vector3 _memoTo;
        private Vector3 _memoSnappedTo;
        private float _memoLength;

        /// <summary>
        /// Builds a probe.
        /// </summary>
        /// <param name="eyeHeight">
        /// Height above the queried <c>from</c> point that sight is cast from. §06's
        /// perception is a creature looking, not a point on the floor looking: a ray
        /// along the ground is blocked by the first ramp and by nothing else.
        /// </param>
        /// <param name="targetSightHeight">
        /// Height above the queried <c>to</c> point that sight is cast at. Positions
        /// arrive at the feet, and a target is visible when its body is, not when the
        /// floor it stands on is.
        /// </param>
        /// <param name="bodyRadius">
        /// Half-width of a character. Both ends of the sight ray are trimmed by it so
        /// that the asker's own collider and the target's own collider cannot be
        /// mistaken for the wall between them — the failure mode that makes a monster
        /// permanently blind and looks exactly like "the AI is not finished".
        /// </param>
        /// <param name="bodyHeight">Creature height, used as the floor-probe span and the snap radius.</param>
        /// <param name="sightBlockers">Layers that stop sight. Doors and level geometry; never characters.</param>
        /// <param name="floorLayers">Layers the floor-material raycast may hit.</param>
        /// <param name="areaMask">NavMesh area mask for path queries.</param>
        public NavMeshWorldProbe(
            float eyeHeight,
            float targetSightHeight,
            float bodyRadius,
            float bodyHeight,
            int sightBlockers,
            int floorLayers,
            int areaMask)
        {
            _eyeHeight = eyeHeight > 0f ? eyeHeight : 0f;
            _targetSightHeight = targetSightHeight > 0f ? targetSightHeight : 0f;
            _bodyRadius = bodyRadius > 0f ? bodyRadius : 0f;
            _bodyHeight = bodyHeight > 0f ? bodyHeight : 1f;
            _sampleRadius = _bodyHeight;
            _sightBlockers = sightBlockers;
            _floorLayers = floorLayers;
            _areaMask = areaMask;
        }

        /// <summary>
        /// The §12 zone table, when the host has one. Zones and floor materials are
        /// authored map data, not physics: §12 makes "구역별로 바닥 재질이 달라야
        /// 청음사가 위치를 판별할 수 있다" a systems rule, and a graph answers it the
        /// same way the headless simulator does — so a balance result transfers.
        /// <para>
        /// Null is legal. Without a graph the probe falls back to a downward raycast
        /// for the material and reports no zone at all, which degrades §07's patrol
        /// scope to "wherever it is standing" rather than crashing.
        /// </para>
        /// </summary>
        public MapGraph? Map { get; set; }

        /// <summary>
        /// §03's "is it bright enough to read here", supplied by the light layer.
        /// <para>
        /// It is a delegate rather than a reference to the light system because
        /// ARCHITECTURE §3 couples systems through primitives: this class must not
        /// learn what a flare or a zone breaker is. Null means dark everywhere, which
        /// is §03's starting state and the safe answer.
        /// </para>
        /// </summary>
        public Func<Vector3, bool>? LitQuery { get; set; }

        /// <summary>
        /// Drops the one-entry path memo. The host calls this once per fixed step,
        /// before ticking anything, so a door closing between steps is never answered
        /// from a path computed while it was open.
        /// </summary>
        public void ClearCache()
        {
            _memoValid = false;
        }

        /// <inheritdoc />
        public bool HasLineOfSight(Vec3 from, Vec3 to)
        {
            var a = from.ToVector3() + (Vector3.up * _eyeHeight);
            var b = to.ToVector3() + (Vector3.up * _targetSightHeight);

            var delta = b - a;
            var distance = delta.magnitude;
            if (distance <= MathX.Epsilon)
            {
                return true;
            }

            var direction = delta / distance;

            // Trim a body radius off each end. Inside that shell the only thing the
            // ray can hit is the two creatures themselves, and "I can see you because
            // you are not standing inside your own chest" is not a sight rule.
            var span = distance - (_bodyRadius * 2f);
            if (span <= MathX.Epsilon)
            {
                return true;
            }

            return !Physics.Raycast(
                a + (direction * _bodyRadius),
                direction,
                span,
                _sightBlockers,
                QueryTriggerInteraction.Ignore);
        }

        /// <inheritdoc />
        public float NavigableDistance(Vec3 from, Vec3 to)
        {
            return EnsurePath(from.ToVector3(), to.ToVector3()) ? _memoLength : float.PositiveInfinity;
        }

        /// <inheritdoc />
        public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
        {
            next = from;

            var origin = from.ToVector3();
            if (!EnsurePath(origin, to.ToVector3()))
            {
                return false;
            }

            // Corner 0 is the point being left. Aiming at the next DISTINCT corner is
            // what walks the monster around the corner instead of through it, and it
            // is the reason a 4.8 m/s creature cannot cut the chord of §12's
            // S-corridor.
            //
            // "Distinct" is load-bearing, not defensive tidying. At the mouth of a
            // NavMeshLink — which is how the 계단 join the three storeys —
            // CalculatePath emits corners[0] and corners[1] at the same position.
            // Returning corner 1 unconditionally then hands the brain a waypoint it is
            // already standing on; MonsterBrain measures the distance as within its
            // tolerance, "arrives" without moving, and asks again. That deadlocked the
            // monster 95 m from the player on a path the audit reported as complete,
            // and it produced no error of any kind — see docs/BLOCKERS.md B-001.
            var advanced = false;
            for (var i = 1; i < _cornerCount; i++)
            {
                if ((_corners[i] - origin).sqrMagnitude > MinWaypointAdvanceSqr)
                {
                    next = _corners[i].ToVec3();
                    advanced = true;
                    break;
                }
            }

            if (!advanced)
            {
                // Every corner sits on top of us: either we are on the destination, or
                // the path is degenerate. Aim at the target so the brain makes progress
                // or fails honestly, rather than standing still forever.
                next = (_cornerCount > 0 ? _corners[_cornerCount - 1] : _memoSnappedTo).ToVec3();
            }

            return true;
        }

        /// <summary>
        /// Squared distance a waypoint must be from the mover to count as somewhere
        /// else, in m².
        /// <para>
        /// Comfortably above <c>MonsterBrain</c>'s arrival tolerance: a waypoint the
        /// brain would immediately consider reached is not a waypoint, it is a stall.
        /// </para>
        /// </summary>
        private const float MinWaypointAdvanceSqr = 0.09f;

        /// <inheritdoc />
        public FloorMaterial SampleFloor(Vec3 position)
        {
            var map = Map;
            if (map != null)
            {
                var authored = map.FloorAt(position);
                if (authored != FloorMaterial.None)
                {
                    return authored;
                }
            }

            return RaycastFloor(position.ToVector3());
        }

        /// <inheritdoc />
        public int ZoneIdAt(Vec3 position)
        {
            var map = Map;
            return map != null ? map.ZoneIdAt(position) : -1;
        }

        /// <inheritdoc />
        public Vec3 SnapToNavigable(Vec3 desired)
        {
            var point = desired.ToVector3();
            return TrySnap(point, out var snapped) ? snapped.ToVec3() : desired;
        }

        /// <inheritdoc />
        public bool IsAreaLit(Vec3 position)
        {
            var query = LitQuery;
            return query != null && query(position.ToVector3());
        }

        /// <summary>
        /// True when a point sits on the NavMesh within the snap radius. The host uses
        /// it to refuse a spawn that would leave the monster frozen off-mesh, which is
        /// otherwise invisible until someone notices it never moved.
        /// </summary>
        public bool IsOnNavMesh(Vector3 point)
        {
            return TrySnap(point, out _);
        }

        private bool TrySnap(Vector3 point, out Vector3 snapped)
        {
            if (NavMesh.SamplePosition(point, out var hit, _sampleRadius, _areaMask))
            {
                snapped = hit.position;
                return true;
            }

            snapped = point;
            return false;
        }

        private bool EnsurePath(Vector3 from, Vector3 to)
        {
            if (_memoValid && _memoFrom == from && _memoTo == to)
            {
                return _memoComplete;
            }

            _memoValid = true;
            _memoFrom = from;
            _memoTo = to;
            _memoComplete = false;
            _memoLength = float.PositiveInfinity;
            _memoSnappedTo = to;
            _cornerCount = 0;

            if (!TrySnap(from, out var start) || !TrySnap(to, out var end))
            {
                return false;
            }

            _memoSnappedTo = end;

            if (!NavMesh.CalculatePath(start, end, _areaMask, _path))
            {
                return false;
            }

            // PathPartial is the interesting one: it means "I can get some of the way
            // there". IWorldProbe defines unreachable as +∞ and MonsterBrain treats
            // that as a destination to discard, so reporting the partial length would
            // send the monster confidently to a wall and leave it there.
            if (_path.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            _cornerCount = ReadCorners();
            if (_cornerCount < 1)
            {
                return false;
            }

            // The walk from the real points onto the mesh is charged at both ends, so
            // that a noise made inside a wall is not free to reach. This matches
            // MapGraphProbe's arithmetic exactly — the simulator and the shipped game
            // have to agree about distance or a balance sweep means nothing.
            var length = Vector3.Distance(from, start) + Vector3.Distance(to, end);
            for (var i = 1; i < _cornerCount; i++)
            {
                length += Vector3.Distance(_corners[i - 1], _corners[i]);
            }

            _memoLength = length;
            _memoComplete = true;
            return true;
        }

        private int ReadCorners()
        {
            while (true)
            {
                var count = _path.GetCornersNonAlloc(_corners);
                if (count < _corners.Length)
                {
                    return count;
                }

                // A full buffer may be a truncation. Grow and ask again rather than
                // measure a path that was cut short.
                _corners = new Vector3[_corners.Length * 2];
            }
        }

        private FloorMaterial RaycastFloor(Vector3 position)
        {
            var origin = position + (Vector3.up * _bodyHeight);
            if (!Physics.Raycast(
                    origin,
                    Vector3.down,
                    out var hit,
                    _bodyHeight * FloorProbeSpanInBodies,
                    _floorLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return FloorMaterial.None;
            }

            var collider = hit.collider;
            if (collider == null)
            {
                return FloorMaterial.None;
            }

            var key = collider.GetInstanceID();
            if (_floorByCollider.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var resolved = ResolveFloor(collider);
            _floorByCollider[key] = resolved;
            return resolved;
        }

        private static FloorMaterial ResolveFloor(Collider collider)
        {
            // Order of trust: the physics material is an explicit authoring decision,
            // the object name is the MapKit convention (FloorTile_Wood,
            // Stairwell_Metal), the renderer material is the last resort.
            var physics = collider.sharedMaterial;
            if (physics != null)
            {
                var fromPhysics = MatchFloorName(physics.name);
                if (fromPhysics != FloorMaterial.None)
                {
                    return fromPhysics;
                }
            }

            for (var t = collider.transform; t != null; t = t.parent)
            {
                var fromName = MatchFloorName(t.name);
                if (fromName != FloorMaterial.None)
                {
                    return fromName;
                }
            }

            var renderer = collider.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = renderer.sharedMaterial;
                if (material != null)
                {
                    return MatchFloorName(material.name);
                }
            }

            return FloorMaterial.None;
        }

        /// <summary>
        /// Finds a <see cref="FloorMaterial"/> named inside an asset name.
        /// <para>
        /// The <em>right-most</em> match wins, and that is not arbitrary: the MapKit
        /// ships <c>FloorTile_Gravel</c>, in which a left-to-right search would find
        /// "Tile" and hand the Listener the wrong zone. §12 makes the surface a
        /// gameplay channel, so a mis-identified floor is a mis-identified location.
        /// </para>
        /// </summary>
        private static FloorMaterial MatchFloorName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return FloorMaterial.None;
            }

            var best = FloorMaterial.None;
            var bestIndex = -1;

            for (var i = 0; i < FloorMaterials.Length; i++)
            {
                var index = name!.LastIndexOf(FloorMaterialNames[i], StringComparison.OrdinalIgnoreCase);
                if (index > bestIndex)
                {
                    bestIndex = index;
                    best = FloorMaterials[i];
                }
            }

            return best;
        }

        private static FloorMaterial[] BuildFloorMaterials()
        {
            var all = (FloorMaterial[])Enum.GetValues(typeof(FloorMaterial));
            var kept = new List<FloorMaterial>(all.Length);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != FloorMaterial.None)
                {
                    kept.Add(all[i]);
                }
            }

            return kept.ToArray();
        }

        private static string[] BuildFloorMaterialNames()
        {
            var materials = FloorMaterials;
            var names = new string[materials.Length];
            for (var i = 0; i < materials.Length; i++)
            {
                names[i] = materials[i].ToString();
            }

            return names;
        }
    }
}
