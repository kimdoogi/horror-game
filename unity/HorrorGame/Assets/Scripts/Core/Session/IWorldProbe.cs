using HorrorGame.Core.Map;
using HorrorGame.Core.Math;

namespace HorrorGame.Core.Session
{
    /// <summary>
    /// Everything the rules need to ask about the physical world.
    /// <para>
    /// This is the seam between the engine-agnostic core and whatever is hosting
    /// it. Unity answers with raycasts and the NavMesh; the headless simulator
    /// answers from a graph; tests answer with hand-written fixtures. Because the
    /// monster AI, the Listener and the Observer all reason through this interface
    /// alone, the same balance can be verified without an engine running.
    /// </para>
    /// <para>
    /// Implementations are called from the host's fixed-step loop and must be
    /// cheap and side-effect free. Never mutate world state from inside one.
    /// </para>
    /// </summary>
    public interface IWorldProbe
    {
        /// <summary>
        /// True when nothing opaque stands between the two points. Closed doors,
        /// barricades and level geometry all block; darkness does not — the
        /// monster's own perception rules decide what it does with the sight line.
        /// </summary>
        bool HasLineOfSight(Vec3 from, Vec3 to);

        /// <summary>
        /// Walking distance along the navigable path between two points, in
        /// metres, or <see cref="float.PositiveInfinity"/> when unreachable.
        /// <para>
        /// The monster must use this rather than straight-line distance: §12's
        /// whole S-corridor argument is that path length and sight line diverge,
        /// and that gap is where the Runner escapes.
        /// </para>
        /// </summary>
        float NavigableDistance(Vec3 from, Vec3 to);

        /// <summary>
        /// The next waypoint for something walking from <paramref name="from"/>
        /// toward <paramref name="to"/>. Returns false when no path exists.
        /// </summary>
        bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next);

        /// <summary>The surface underfoot at a position. Drives the Listener (§12).</summary>
        FloorMaterial SampleFloor(Vec3 position);

        /// <summary>
        /// Index of the zone containing a position, or -1 outside the map. Patrol
        /// scope is expressed in zones per §07.
        /// </summary>
        int ZoneIdAt(Vec3 position);

        /// <summary>
        /// A point the monster can stand on near <paramref name="desired"/>,
        /// snapped onto navigable ground. Used when placing patrol and search targets.
        /// </summary>
        Vec3 SnapToNavigable(Vec3 desired);

        /// <summary>
        /// True when the position is lit brightly enough to read a clue without a
        /// flashlight — a zone light or a burning flare. §03.
        /// </summary>
        bool IsAreaLit(Vec3 position);
    }
}
