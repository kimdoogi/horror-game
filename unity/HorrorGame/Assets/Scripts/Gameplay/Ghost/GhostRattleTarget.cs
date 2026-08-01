#nullable enable

using HorrorGame.Core;
using HorrorGame.Gameplay.Interaction;
using UnityEngine;

namespace HorrorGame.Gameplay.Ghost
{
    /// <summary>
    /// The 물건 a ghost is close enough to shake, and the search that finds it. §09's
    /// 신호 row — <em>"근처 물건을 아주 약하게 흔든다 (쿨타임 45초)"</em>.
    /// <para>
    /// <b>Why the search is fussy about what counts as a 물건.</b> §09 says the ghost
    /// shakes an <em>object</em>, and the cheapest implementation — the nearest collider
    /// — would hand it the floor slab it is hovering over, because a floor is a collider
    /// and it is always the closest one. A rattle that comes from the floor is not the
    /// scene §09 is built around: that scene needs the living to turn round and look at
    /// something, and 「방금 뭔가 흔들렸어?」 「바람이겠지」 only works if there is a
    /// <em>thing</em> there to have moved.
    /// </para>
    /// <para>
    /// So the filter is two rules, both derived rather than invented:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// An <see cref="Interactable"/> wins outright. §03's 단서, §08's 전리품 · 금고 ·
    /// 차량 are the things this game has already decided are objects a hand can be put
    /// on, and something the living are going to walk up to anyway is the best possible
    /// place for a noise to come from.
    /// </description></item>
    /// <item><description>
    /// Anything else has to be smaller than <see cref="GameConstants.GhostRattleRange"/>
    /// across. That bound is not a taste call: an object the ghost cannot get around
    /// without leaving rattle range is scenery — a wall run, a floor slab, a stair
    /// flight — and §09's ghost is standing <em>beside</em> the thing it shakes.
    /// </description></item>
    /// </list>
    /// <para>
    /// The living and the monster are excluded outright. A ghost shaking the body of a
    /// teammate would be a second channel with far more bandwidth than §09 allows, and
    /// shaking the monster is not a signal, it is a weapon.
    /// </para>
    /// </summary>
    public readonly struct GhostRattleTarget
    {
        /// <summary>Nothing in range. <see cref="Found"/> is false.</summary>
        public static readonly GhostRattleTarget None = default(GhostRattleTarget);

        /// <summary>Builds a result. Produced by <see cref="Find"/>.</summary>
        private GhostRattleTarget(Transform body, Vector3 position, string label, float distance, bool movable)
        {
            Body = body;
            Position = position;
            Label = label;
            Distance = distance;
            Movable = movable;
        }

        /// <summary>The object itself, so a successful rattle can visibly move it. Null when nothing was found.</summary>
        public Transform? Body { get; }

        /// <summary>Where the noise comes from — the object's centre, not its pivot, so a floor-standing crate rattles at its middle.</summary>
        public Vector3 Position { get; }

        /// <summary>What to call it on the ghost's overlay. §09 gives the ghost one verb and it should know what it is about to touch.</summary>
        public string Label { get; }

        /// <summary>Straight-line metres from the ghost. §09's ghost passes through walls, so straight is the honest measure.</summary>
        public float Distance { get; }

        /// <summary>
        /// Whether the transform may be nudged. False for anything marked static: moving
        /// a batched, lightmapped mesh is a rendering defect rather than a signal, and
        /// §09's 아주 약하게 is carried by the sound in that case.
        /// </summary>
        public bool Movable { get; }

        /// <summary>Whether anything was found at all.</summary>
        public bool Found
        {
            get { return Body != null; }
        }

        /// <summary>
        /// The best thing to shake within <see cref="GameConstants.GhostRattleRange"/> of
        /// <paramref name="from"/>, or <see cref="None"/>.
        /// </summary>
        /// <param name="from">Where the ghost is.</param>
        /// <param name="ignoreRoot">The dead player's own rig, so a ghost cannot rattle its own corpse.</param>
        /// <param name="monsterRoot">§06's antagonist. Not a signalling device.</param>
        /// <param name="scratch">Reused overlap buffer, so a per-frame search allocates nothing.</param>
        public static GhostRattleTarget Find(
            Vector3 from, Transform? ignoreRoot, Transform? monsterRoot, Collider[] scratch)
        {
            if (scratch == null || scratch.Length == 0)
            {
                return None;
            }

            var count = Physics.OverlapSphereNonAlloc(
                from, GameConstants.GhostRattleRange, scratch, ~0, QueryTriggerInteraction.Collide);

            var best = None;
            var bestScore = float.PositiveInfinity;

            for (var i = 0; i < count; i++)
            {
                var collider = scratch[i];
                if (collider == null)
                {
                    continue;
                }

                var body = collider.transform;
                if (IsUnder(body, ignoreRoot) || IsUnder(body, monsterRoot))
                {
                    continue;
                }

                var interactable = collider.GetComponentInParent<Interactable>();
                var bounds = MeasuredBounds(collider);
                var extent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));

                if (interactable == null && extent > GameConstants.GhostRattleRange)
                {
                    // Scenery. See the class remarks — an object a ghost cannot stand
                    // beside is not the 근처 물건 §09 means.
                    continue;
                }

                var centre = bounds.size.sqrMagnitude > 0f ? bounds.center : body.position;
                var distance = Vector3.Distance(from, centre);
                if (distance > GameConstants.GhostRattleRange)
                {
                    // OverlapSphere answers about the collider's shell; §09's range is to
                    // the object, and Core measures the same centre when it re-checks.
                    continue;
                }

                // Interactables sort ahead of everything else at any distance inside the
                // range, because §09 wants a thing the living recognise, not the nearest
                // thing. Within a class, nearer wins.
                var score = distance + (interactable != null ? 0f : GameConstants.GhostRattleRange);
                if (score >= bestScore)
                {
                    continue;
                }

                var root = interactable != null ? interactable.transform : body;
                bestScore = score;
                best = new GhostRattleTarget(
                    root,
                    centre,
                    interactable != null ? interactable.Title : Readable(root.name),
                    distance,
                    !root.gameObject.isStatic);
            }

            return best;
        }

        /// <summary>
        /// The object's own extents. A renderer is asked first because a collider on a
        /// map-kit piece is often a single box over a whole wall run while the mesh under
        /// it is one panel, and the size test above is about the thing the player sees.
        /// </summary>
        private static Bounds MeasuredBounds(Collider collider)
        {
            var renderer = collider.GetComponent<Renderer>();
            if (renderer == null)
            {
                renderer = collider.GetComponentInChildren<Renderer>();
            }

            return renderer != null ? renderer.bounds : collider.bounds;
        }

        private static bool IsUnder(Transform body, Transform? root)
        {
            if (root == null)
            {
                return false;
            }

            return body == root || body.IsChildOf(root);
        }

        /// <summary>
        /// A scene object's name, tidied for a readout. Generated map-kit pieces carry
        /// their grid cell in the name — <c>FloorTileConcrete_L0_16_29</c> — and the ghost
        /// needs the noun, not the address.
        /// </summary>
        private static string Readable(string name)
        {
            var cut = name.IndexOf('_');
            var head = cut > 0 ? name.Substring(0, cut) : name;
            return string.IsNullOrEmpty(head) ? "물건" : head;
        }
    }
}
