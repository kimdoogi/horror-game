#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace HorrorGame.Gameplay.Match
{
    /// <summary>
    /// §01's 지상, given an edge a player can see. Built at runtime around
    /// <see cref="MatchMap.Entrance"/>, at exactly <see cref="MatchMap.SurfaceRadius"/>.
    /// <para>
    /// <b>Why this exists.</b> §01 makes the ground at the 출입구 the 안전 지대, §08 puts
    /// the shop, the sale and the 보급소 on it, and §02 ends the match when the objective
    /// crosses into it. Until this class, none of that had any physical presence: the
    /// apron was a <c>sqrMagnitude</c> test against a light, on the same unbroken
    /// concrete as the rest of B1 하역장, and the only thing that told a player which
    /// side of it they were standing on was one line of §14 guidance text. The owner
    /// played the build and asked <i>앞마당이 어디야</i> — where is the front yard — which
    /// is the correct question to ask about a boundary that carries this much meaning
    /// and cannot be seen.
    /// </para>
    /// <para>
    /// <b>Paint, not a marker.</b> §03's whole design is that the player builds a mental
    /// map — "맵 없음 · 마커 없음", see one, remember it, say it out loud — so a floating
    /// waypoint would undercut the section it was meant to serve, and would still be
    /// invisible to somebody looking at the floor. What goes down instead is what a real
    /// 하역장 has: a painted bay line, bollards where the line crosses somewhere you can
    /// walk, and a lamp over each of those crossings. It is read from the floor, at
    /// walking speed, with no HUD.
    /// </para>
    /// <para>
    /// <b>The line is the rule, not a picture of it.</b> Every stripe is laid at
    /// <see cref="MatchMap.SurfaceRadius"/> from the same <see cref="MatchMap.Entrance"/>
    /// that <see cref="MatchMap.IsOnSurface"/> measures against, so the paint cannot
    /// drift from the boundary it draws. Crossing the stripe and the match changing
    /// phase are the same event.
    /// </para>
    /// <para>
    /// <b>Warm inside, cold out.</b> §07 cools the grade as the night advances and §12's
    /// zone lights are a cold blue that starts switched off. The apron is given the
    /// opposite: tungsten lamps over the crossings, a bay lamp over the van, and the
    /// van's own headlamps and rear work lamp. §01 calls surfacing 숨 돌리기, and warmth
    /// is how a room says that without a caption.
    /// </para>
    /// <para>
    /// <b>Built at runtime rather than baked.</b> Three reasons, in order of weight.
    /// The apron is defined against the 출입구 the <em>runtime</em> found, so anything
    /// baked would be a second opinion about where it is. <c>MapSceneGenerator.Generate</c>
    /// is gated on a §12 checklist that currently fails (B-007), so nothing can be
    /// written into the scene at all. And <c>SoloPlaytest.BuildScene</c> rewrites the
    /// solo scene from the map, so hand-placed geometry is wiring that is silently lost
    /// — the same argument <c>MatchAudioBridge.PlaceLandmarks</c> makes for the
    /// generator hum.
    /// </para>
    /// <para>
    /// <b>Nothing here is on the NavMesh.</b> The paint is 2.5 cm of kerb and the posts
    /// carry no collider, so neither §06's agent nor §12's baked surface can see any of
    /// it. That is deliberate: the entrance is the one place a stray obstacle would
    /// island, and <c>NavMeshAudit</c>'s 1830 pairs are measured on the saved scene,
    /// which this never touches.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceApron : MonoBehaviour
    {
        /// <summary>Name the apron root takes in the hierarchy, so a generated match reads.</summary>
        public const string RootName = "SurfaceApron";

        private readonly List<Light> _apronLamps = new List<Light>();
        private readonly List<Material> _owned = new List<Material>();
        private readonly List<float> _restIntensity = new List<float>();

        private static Mesh? _cubeMesh;

        private Light? _beacon;
        private float _beaconRest;
        private float _beat;
        private bool _beatIsWarm;

        /// <summary>Stripes actually painted. Fewer than <see cref="StripeCount"/> wherever the ring left the floor.</summary>
        public int PaintedStripes { get; private set; }

        /// <summary>Crossings found — stretches of the ring a player can walk over. Each gets posts and a lamp.</summary>
        public int Gates { get; private set; }

        /// <summary>Metres of the ring that are walkable. §12's 출입구 is in a corner, so this is far short of the circumference.</summary>
        public float WalkableMetres { get; private set; }

        /// <summary>Practicals hung inside the ring, between the van and the threshold. The apron's warmth is mostly these.</summary>
        public int InnerLamps { get; private set; }

        /// <summary>
        /// Lays the apron down around a match's 출입구.
        /// </summary>
        /// <param name="entrance">§01's 출입구, floor plane. <see cref="MatchMap.Entrance"/>.</param>
        /// <param name="radius">§01's 지상. Always <see cref="MatchMap.SurfaceRadius"/>; taken as a parameter so a test can shrink it.</param>
        /// <param name="vehicle">§08's 차량, already spawned, so its lamps can be hung on it. Optional.</param>
        /// <param name="parent">The match's world root, so <c>ClearWorld</c> takes all of this with it.</param>
        /// <returns>The apron, or null when no shader could be resolved to paint with.</returns>
        public static SurfaceApron? Build(Vector3 entrance, float radius, GameObject? vehicle, Transform? parent)
        {
            var shader = ResolveShader(vehicle);
            if (shader == null)
            {
                Debug.LogError(
                    "[Apron] No shader could be resolved from anything already in the scene, so §01's 지상 "
                    + "cannot be painted. The render pipeline is not set up: run "
                    + "Horror ▸ Setup ▸ Configure URP Render Pipeline.");
                return null;
            }

            var root = new GameObject(RootName);
            root.transform.position = entrance;
            if (parent != null)
            {
                root.transform.SetParent(parent, worldPositionStays: true);
            }

            // The van was spawned and settled onto the floor a moment ago and every
            // stripe below is placed by raycast. Unity does not push a transform into the
            // physics scene until something asks, so without this the first match after a
            // rebuild paints against last frame's world.
            Physics.SyncTransforms();

            var apron = root.AddComponent<SurfaceApron>();
            apron.PaintTheLine(entrance, radius, shader);
            apron.LightTheBay(entrance);
            apron.LightTheVehicle(vehicle);
            apron.Rest();

            Debug.Log(
                "[Apron] §01 지상 painted at " + radius.ToString("0.#") + " m — "
                + apron.PaintedStripes + " of " + StripeCount + " stripes found floor, "
                + apron.Gates + " walkable crossing(s) over "
                + apron.WalkableMetres.ToString("0.#") + " m of the ring, "
                + apron.InnerLamps + " practical(s) inside it, "
                + apron._apronLamps.Count + " warm source(s) in all.", apron);

            return apron;
        }

        /// <summary>
        /// §01's two crossings, as something the eye catches.
        /// <para>
        /// Descending is the section's commitment and surfacing is its relief, and
        /// neither used to register as anything at all — the phase flipped, the clock
        /// carried on, and no pixel moved. The lamps over the threshold swell on the way
        /// in and dip on the way out, over
        /// <see cref="BeatSeconds"/>, which is short enough to read as an event and long
        /// enough to see while walking through it. It is cosmetic by construction: the
        /// only thing it writes is <c>Light.intensity</c> on lamps this class created,
        /// so §13's replay cannot notice it happened.
        /// </para>
        /// </summary>
        /// <param name="onSurface">Whether the player has just arrived on §01's 지상.</param>
        public void SetOnSurface(bool onSurface)
        {
            _beat = 1f;
            _beatIsWarm = onSurface;
        }

        private void Update()
        {
            if (_beacon != null)
            {
                // §08's 차량 is a works van and it is the thing a surfacing player has to
                // find first. A slow amber turn on the roof is the one cue that reaches
                // down a corridor and around a corner — it is on the object, so it is not
                // the §03 marker this class exists to avoid.
                var phase = Mathf.Sin(Time.time * BeaconRadiansPerSecond) * 0.5f + 0.5f;
                _beacon.intensity = _beaconRest * Mathf.Lerp(BeaconDim, BeaconBright, phase * phase);
            }

            if (_beat <= 0f)
            {
                return;
            }

            _beat = Mathf.Max(0f, _beat - (Time.deltaTime / BeatSeconds));
            Rest();
        }

        /// <summary>Applies the crossing envelope to every apron lamp. At rest the factor is exactly 1.</summary>
        private void Rest()
        {
            var factor = 1f + ((_beatIsWarm ? BeatSwell : BeatDip) * _beat);

            for (var i = 0; i < _apronLamps.Count && i < _restIntensity.Count; i++)
            {
                var lamp = _apronLamps[i];
                if (lamp != null)
                {
                    lamp.intensity = _restIntensity[i] * factor;
                }
            }
        }

        // ------------------------------------------------------------------
        // The line.
        // ------------------------------------------------------------------

        /// <summary>
        /// Walks the ring and paints every step of it that has floor underneath.
        /// <para>
        /// Each stripe is tested rather than assumed. §12 put the 출입구 in the north-west
        /// corner of B1 하역장, so most of a 15 m circle around it is outside the
        /// building altogether, and a ring drawn without asking would run through walls
        /// and hang in the dark. What is left is the arc that crosses the 하역 베이 — the
        /// one 개방 공간 on the storey, with §12's 20 m sight line, which is the best
        /// place in the building to be able to see a line from.
        /// </para>
        /// </summary>
        private void PaintTheLine(Vector3 entrance, float radius, Shader shader)
        {
            var mesh = CubeMesh();
            if (mesh == null)
            {
                Debug.LogError("[Apron] No cube mesh, so §01's 지상 cannot be painted.", this);
                return;
            }

            var amber = Own(Painted(shader, AmberPaint));
            var bone = Own(Painted(shader, BonePaint));
            var arc = 2f * Mathf.PI * radius / StripeCount;

            var walkable = new bool[StripeCount];
            var placed = new Vector3[StripeCount];

            for (var i = 0; i < StripeCount; i++)
            {
                var angle = Mathf.PI * 2f * i / StripeCount;
                var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var at = entrance + (outward * radius);

                if (!TryFloorUnder(at, entrance.y, out var floor))
                {
                    continue;
                }

                placed[i] = floor;
                walkable[i] = OnNavMesh(floor);

                var stripe = Slab("Apron_Stripe_" + i.ToString("00"), mesh, (i % 2) == 0 ? amber : bone);

                // Local Z points out of the circle, so local X runs along the tangent and
                // the box's length is the arc it covers. Grown a hair so two neighbours
                // meet rather than leaving a hairline of bare floor between them.
                stripe.transform.SetPositionAndRotation(
                    floor + (Vector3.up * (StripeRiseMetres * 0.5f)),
                    Quaternion.LookRotation(outward, Vector3.up));
                stripe.transform.localScale = new Vector3(arc * 1.04f, StripeRiseMetres, StripeWidthMetres);
                stripe.transform.SetParent(transform, worldPositionStays: true);

                PaintedStripes++;
            }

            WalkableMetres = 0f;
            for (var i = 0; i < StripeCount; i++)
            {
                if (walkable[i])
                {
                    WalkableMetres += arc;
                }
            }

            LightTheWalk(entrance, BuildGates(walkable, placed, entrance, arc, shader));
        }

        /// <summary>
        /// Frames every stretch of the ring a player can actually walk over.
        /// <para>
        /// §12's checklist asks for 구역 간 진입점 2~3 and the apron is no different: the
        /// boundary matters at the handful of places it can be crossed, and nowhere else.
        /// A run of consecutive walkable stripes is one of those places, so it gets a
        /// bollard at each jamb and a warm lamp over it — the lit doorway a player sees
        /// from the far side of the bay and walks towards.
        /// </para>
        /// </summary>
        private List<Vector3> BuildGates(bool[] walkable, Vector3[] placed, Vector3 entrance, float arc, Shader shader)
        {
            var centres = new List<Vector3>();

            var mesh = CubeMesh();
            if (mesh == null)
            {
                return centres;
            }

            var post = Own(Painted(shader, PostPaint));
            var cap = Own(Painted(shader, AmberPaint));

            var i = 0;
            while (i < StripeCount)
            {
                if (!walkable[i])
                {
                    i++;
                    continue;
                }

                var start = i;
                while (i < StripeCount && walkable[i])
                {
                    i++;
                }

                var run = i - start;
                if (run * arc < GateMinimumMetres)
                {
                    continue;
                }

                var index = Gates++;
                var group = new GameObject("Apron_Gate_" + index);
                group.transform.SetParent(transform, worldPositionStays: false);

                Debug.Log(
                    "[Apron] gate " + index + " — " + (run * arc).ToString("0.#") + " m wide, from "
                    + placed[start].ToString("0.0") + " to " + placed[i - 1].ToString("0.0"), this);

                Bollard(group.transform, placed[start], entrance, index + "A", mesh, post, cap);
                Bollard(group.transform, placed[i - 1], entrance, index + "B", mesh, post, cap);

                // One lamp per GateLampSpacingMetres of opening, so a two-metre doorway
                // gets one and the 하역 베이's whole mouth gets a lit run rather than a
                // single hotspot with dark shoulders.
                var lamps = Mathf.Clamp(Mathf.RoundToInt(run * arc / GateLampSpacingMetres), 1, MaxLampsPerGate);
                for (var lamp = 0; lamp < lamps; lamp++)
                {
                    var at = placed[start + Mathf.RoundToInt((run - 1) * (lamps == 1 ? 0.5f : (float)lamp / (lamps - 1)))];
                    AddLamp(
                        group.transform,
                        "Apron_GateLamp_" + index + "_" + lamp,
                        at + (Vector3.up * GateLampHeightMetres),
                        GateLampColour,
                        GateLampIntensity,
                        GateLampRangeMetres);
                }

                centres.Add(placed[start + (run / 2)]);
            }

            return centres;
        }

        /// <summary>One bollard: a post at the jamb with an amber cap, leaning nothing, colliding with nothing.</summary>
        private void Bollard(
            Transform parent, Vector3 at, Vector3 entrance, string suffix, Mesh mesh, Material post, Material cap)
        {
            var inward = entrance - at;
            inward.y = 0f;
            var facing = inward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(inward.normalized, Vector3.up)
                : Quaternion.identity;

            var body = Slab("Apron_Post_" + suffix, mesh, post);
            body.transform.SetPositionAndRotation(at + (Vector3.up * (PostHeightMetres * 0.5f)), facing);
            body.transform.localScale = new Vector3(PostSideMetres, PostHeightMetres, PostSideMetres);
            body.transform.SetParent(parent, worldPositionStays: true);

            var band = Slab("Apron_PostCap_" + suffix, mesh, cap);
            band.transform.SetPositionAndRotation(
                at + (Vector3.up * (PostHeightMetres - (PostCapMetres * 0.5f))), facing);
            band.transform.localScale = new Vector3(PostSideMetres * 1.18f, PostCapMetres, PostSideMetres * 1.18f);
            band.transform.SetParent(parent, worldPositionStays: true);
        }

        // ------------------------------------------------------------------
        // The warmth.
        // ------------------------------------------------------------------

        /// <summary>
        /// Hangs the bay lamp over the 출입구.
        /// <para>
        /// The generator already leaves one <c>EntranceLight</c> burning so the way out
        /// is findable at all; this is the second half of the same idea, and it is what
        /// makes the apron read as a <em>room</em> rather than as a lit spot. It is
        /// deliberately warmer and lower than the generator's fitting, so the floor
        /// inside the line is a different colour from the floor outside it — which is
        /// the cue that survives a player who never looks down.
        /// </para>
        /// </summary>
        private void LightTheBay(Vector3 entrance)
        {
            AddLamp(transform, "Apron_BayLamp", entrance + (Vector3.up * BayLampHeightMetres),
                BayLampColour, BayLampIntensity, BayLampRangeMetres);
        }

        /// <summary>
        /// Hangs practicals along the walk from the van to each threshold.
        /// <para>
        /// <b>Along the route, not on a circle.</b> A lamp at the middle and a lamp at
        /// the rim leave the walk between them dark, and that band is most of the apron
        /// — it is where the van is parked, where §08's sale happens and where a player
        /// stands to shop. Measured, not assumed: the first pass lit the centre and the
        /// threshold and the frame taken from the spawn was an ordinary cold corridor
        /// with two headlamps at the far end of it. A second pass tried a ring of
        /// practicals at 0.62 of the radius and fitted two of sixteen, because §12's
        /// apron here is a 2.2 m service corridor and a circle drawn through it is
        /// mostly wall.
        /// </para>
        /// <para>
        /// The NavMesh already knows the shape of the walk, so the lamps follow the path
        /// §06's own agent would take out of the door. That is also why it cannot light
        /// a place a player never goes.
        /// </para>
        /// </summary>
        private void LightTheWalk(Vector3 entrance, IReadOnlyList<Vector3> gateCentres)
        {
            if (!NavMesh.SamplePosition(entrance, out var doorHit, NavMeshSnapMetres * 3f, NavMesh.AllAreas))
            {
                return;
            }

            var path = new NavMeshPath();
            var fitted = 0;

            for (var g = 0; g < gateCentres.Count; g++)
            {
                if (!NavMesh.SamplePosition(gateCentres[g], out var gateHit, NavMeshSnapMetres, NavMesh.AllAreas))
                {
                    continue;
                }

                if (!NavMesh.CalculatePath(doorHit.position, gateHit.position, NavMesh.AllAreas, path)
                    || path.corners.Length < 2)
                {
                    continue;
                }

                var walked = 0f;
                var nextAt = WalkLampSpacingMetres;

                for (var c = 1; c < path.corners.Length; c++)
                {
                    var from = path.corners[c - 1];
                    var to = path.corners[c];
                    var leg = Vector3.Distance(from, to);

                    while (nextAt <= walked + leg)
                    {
                        var along = Vector3.Lerp(from, to, (nextAt - walked) / Mathf.Max(leg, 0.0001f));
                        nextAt += WalkLampSpacingMetres;

                        // The bay lamp owns the first few metres and the gate lamp owns
                        // the last few; doubling up there only blows the floor out.
                        if (Vector3.Distance(along, doorHit.position) < WalkLampSpacingMetres * 0.6f
                            || Vector3.Distance(along, gateHit.position) < WalkLampSpacingMetres * 0.6f)
                        {
                            continue;
                        }

                        if (!TryFloorUnder(along, entrance.y, out var floor))
                        {
                            continue;
                        }

                        AddLamp(
                            transform,
                            "Apron_Practical_" + g + "_" + fitted.ToString("00"),
                            floor + (Vector3.up * WalkLampHeightMetres),
                            WalkLampColour,
                            WalkLampIntensity,
                            WalkLampRangeMetres);

                        fitted++;
                    }

                    walked += leg;
                }
            }

            InnerLamps = fitted;
        }

        /// <summary>
        /// Turns §08's 차량 into the brightest thing on the apron.
        /// <para>
        /// §08 makes it the 안전 지대, the 상점 and the 보급소 in one object and the team
        /// returns to it 2.94 times a match, so it should be what a surfacing player sees
        /// first. It is 6.69 m long and was, until now, an unlit shape in a dark corner.
        /// The headlamps are aimed down whichever half of its own long axis has more room
        /// — the prop is parked at identity because a yaw would push a 6.69 m body through
        /// §12's geometry, so which way it is facing is not something this can assume.
        /// </para>
        /// </summary>
        private void LightTheVehicle(GameObject? vehicle)
        {
            if (vehicle == null)
            {
                return;
            }

            // Oriented, not axis-aligned. Park gives the van a yaw, and a world AABB of a
            // yawed 6.69 m body reports a square footprint — the headlamps would end up
            // hanging off a corner of a box the vehicle does not occupy.
            if (!TryLocalBounds(vehicle, out var local))
            {
                return;
            }

            var body = vehicle.transform;
            var centre = body.TransformPoint(local.center);
            var longwaysIsZ = local.size.z >= local.size.x;

            var lengthways = longwaysIsZ ? body.forward : body.right;
            var half = (longwaysIsZ ? local.extents.z : local.extents.x)
                       * (longwaysIsZ ? body.lossyScale.z : body.lossyScale.x);

            lengthways.y = 0f;
            lengthways = lengthways.sqrMagnitude > 0.0001f ? lengthways.normalized : Vector3.forward;

            var ahead = ClearerWay(centre, lengthways);
            var nose = centre + (ahead * half);
            var tail = centre - (ahead * half);
            var across = Vector3.Cross(Vector3.up, ahead);

            var bounds = new Bounds(centre, Vector3.zero);
            bounds.Encapsulate(body.TransformPoint(local.min));
            bounds.Encapsulate(body.TransformPoint(local.max));

            for (var side = -1; side <= 1; side += 2)
            {
                var head = new GameObject(side < 0 ? "Vehicle_Headlamp_L" : "Vehicle_Headlamp_R");
                head.transform.SetParent(vehicle.transform, worldPositionStays: true);
                head.transform.position = nose
                    + (across * (side * HeadlampSpreadMetres))
                    + new Vector3(0f, HeadlampHeightMetres - bounds.center.y + bounds.min.y, 0f);
                head.transform.rotation = Quaternion.LookRotation(
                    Quaternion.AngleAxis(HeadlampPitchDegrees, across) * ahead, Vector3.up);

                var light = head.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = HeadlampColour;
                light.intensity = HeadlampIntensity;
                light.range = HeadlampRangeMetres;
                light.spotAngle = HeadlampConeDegrees;
                light.shadows = LightShadows.None;
            }

            // §08's shop end, and the lamp stands off the tail rather than on it. Hung at
            // the body it sat inside the load box and lit the inside of a closed van:
            // photographed from ten metres the whole vehicle was a black silhouette with
            // two headlamps in it. A metre clear of the rear doors is where a depot puts
            // one and where it lights the thing a player is standing at.
            AddLamp(vehicle.transform, "Vehicle_WorkLamp",
                tail - (ahead * WorkLampStandOffMetres)
                    + new Vector3(0f, WorkLampHeightMetres - bounds.center.y + bounds.min.y, 0f),
                WorkLampColour, WorkLampIntensity, WorkLampRangeMetres);

            // And one over the bay, off to the side, so the van is modelled rather than
            // lit flat. §08 sends the team back here 2.94 times a match; it has to look
            // like a vehicle, not like a hole in the room.
            AddLamp(vehicle.transform, "Vehicle_BayLamp",
                bounds.center + (across * BayFillOffsetMetres)
                    + new Vector3(0f, (bounds.max.y - bounds.center.y) + BayFillRiseMetres, 0f),
                BayFillColour, BayFillIntensity, BayFillRangeMetres);

            // §12's corridors are 3 m clear and the van is 2.94 m tall, so the roof is not
            // always where a beacon fits. Measured rather than assumed: a light socketed
            // inside the ceiling slab lights the void above it and nothing a player sees.
            var rise = BeaconRiseMetres;
            if (Physics.Raycast(
                    new Vector3(bounds.center.x, bounds.max.y, bounds.center.z), Vector3.up, out var ceiling,
                    BeaconRiseMetres + 0.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                rise = Mathf.Max(0f, ceiling.distance - 0.05f);
            }

            var roof = new GameObject("Vehicle_Beacon");
            roof.transform.SetParent(vehicle.transform, worldPositionStays: true);
            roof.transform.position = new Vector3(bounds.center.x, bounds.max.y + rise, bounds.center.z);

            var beacon = roof.AddComponent<Light>();
            beacon.type = LightType.Point;
            beacon.color = BeaconColour;
            beacon.intensity = BeaconIntensity;
            beacon.range = BeaconRangeMetres;
            beacon.shadows = LightShadows.None;

            _beacon = beacon;
            _beaconRest = BeaconIntensity;
        }

        /// <summary>
        /// Parks §08's 차량 somewhere its own body actually fits, inside §01's 지상.
        /// <para>
        /// <b>It was parked in a wall.</b> <c>MatchDirector</c> puts the vehicle on
        /// <see cref="MatchMap.Entrance"/>, which §12 marks on the 출입구 stairwell cell in
        /// the north-west corner of B1 하역장 — a service corridor with
        /// <c>MapKitCatalogue.CorridorClearWidth</c> of 2.2 m between its walls. The van
        /// is <b>2.81 m</b> wide and 6.69 m long, so it stood 0.3 m inside the brickwork
        /// on both sides and 3.4 m of it was up the stairwell. Photographed from the
        /// corridor it was not a vehicle at all: an unlit black slab spanning the passage
        /// with two headlamps in it, because the corridor walls it was embedded in kept
        /// every light off its flanks. §08 makes this object the 안전 지대, the 상점 and
        /// the 보급소 and the team returns to it 2.94 times a match; it has to read as a
        /// van.
        /// </para>
        /// <para>
        /// <b>Measured, not authored.</b> The nearest point to the 출입구 that is inside
        /// the apron, on §06's NavMesh, and whose box sweep comes back empty for the
        /// van's own footprint. On §12's first map that is the 20 × 20 m 하역 베이, 11.5 m
        /// away — the bay the lorries came down to, which is where a van belongs and the
        /// one place on the storey with §12's 20 m sight line to see it from. Falls back
        /// to the 출입구 with a warning rather than refusing to place a shop.
        /// </para>
        /// </summary>
        /// <param name="vehicle">The spawned prop. Moved in place; its own <c>Settle</c> already found the floor.</param>
        /// <param name="entrance">§01's 출입구. The search starts here and stays within reach of it.</param>
        /// <param name="radius">§01's 지상. The van must stay well inside it so a player can walk round the thing and still be on the surface.</param>
        public static void Park(GameObject? vehicle, Vector3 entrance, float radius)
        {
            if (vehicle == null)
            {
                return;
            }

            // The prop was spawned and settled a moment ago; the sweeps below are physics
            // queries and Unity does not push a transform into the physics scene until
            // something asks.
            Physics.SyncTransforms();

            if (!TryWorldBounds(vehicle, out var bounds))
            {
                return;
            }

            // The prop is spawned at identity, so the world box is the body's own
            // footprint and the yaw below rotates the sweep and the van together.
            var size = bounds.size;
            var halfHeight = Mathf.Max((size.y * 0.5f) - 0.1f, 0.1f);
            var reach = Mathf.Max(radius - ParkingKeepInsideMetres, ParkingStartMetres);

            // Three passes of shrinking clearance rather than one verdict. §12's 하역 베이
            // is dressed — crates, pallets, conduit — and a 7.7 m sweep that insists on
            // half a metre all round fails on a single crate it could have been parked
            // beside. The van's own body is the only hard constraint; everything above it
            // is politeness, and politeness is what gets given up first.
            for (var pass = 0; pass < ParkingClearances.Length; pass++)
            {
                var clearance = ParkingClearances[pass];
                var half = new Vector3(
                    (size.x * 0.5f) + clearance, halfHeight, (size.z * 0.5f) + clearance);

                for (var outward = ParkingStartMetres; outward <= reach; outward += ParkingStepMetres)
                {
                    for (var step = 0; step < ParkingBearings; step++)
                    {
                        var angle = Mathf.PI * 2f * step / ParkingBearings;
                        var at = entrance + (new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * outward);

                        if (!NavMesh.SamplePosition(at, out var hit, NavMeshSnapMetres, NavMesh.AllAreas)
                            || !TryFloorUnder(hit.position, entrance.y, out var floor))
                        {
                            continue;
                        }

                        for (var yaw = 0; yaw < 180; yaw += 45)
                        {
                            var facing = Quaternion.Euler(0f, yaw, 0f);
                            var centre = floor + (Vector3.up * (halfHeight + 0.12f));

                            if (Physics.CheckBox(centre, half, facing, ~0, QueryTriggerInteraction.Ignore))
                            {
                                continue;
                            }

                            vehicle.transform.SetPositionAndRotation(floor, facing);
                            Debug.Log(
                                "[Apron] §08 차량 parked at " + floor.ToString("0.0") + ", yaw " + yaw + "°, "
                                + outward.ToString("0.#") + " m from the 출입구 with "
                                + clearance.ToString("0.00") + " m of clearance — inside §01's "
                                + radius.ToString("0.#") + " m 지상. Its body is " + size.x.ToString("0.0")
                                + " × " + size.z.ToString("0.0") + " m and the 출입구 corridor is "
                                + MapCorridorClearWidthMetres.ToString("0.0") + " m wide, which is why it "
                                + "could not stay where §12 marks the door.", vehicle);
                            return;
                        }
                    }
                }
            }

            Debug.LogWarning(
                "[Apron] §08's 차량 (" + size.x.ToString("0.0") + " × " + size.z.ToString("0.0")
                + " m) does not fit anywhere within " + reach.ToString("0.#")
                + " m of the 출입구, so it is left standing in the corridor with its body inside the walls. "
                + "§12's 하역 베이 is what it is supposed to be parked in; if that has gone, the map no "
                + "longer has room for the shop §08 sends the team back to 2.94 times a match.", vehicle);
        }

        /// <summary>
        /// Which way along an axis has more room, measured rather than guessed. Used to
        /// decide which end of the van is its nose.
        /// </summary>
        private static Vector3 ClearerWay(Vector3 from, Vector3 axis)
        {
            var forward = Clearance(from, axis);
            var back = Clearance(from, -axis);
            return forward >= back ? axis : -axis;
        }

        private static float Clearance(Vector3 from, Vector3 direction)
        {
            return Physics.Raycast(from, direction, out var hit, ClearanceProbeMetres, ~0, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : ClearanceProbeMetres;
        }

        private void AddLamp(Transform parent, string name, Vector3 at, Color colour, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = at;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;

            _apronLamps.Add(light);
            _restIntensity.Add(intensity);
        }

        // ------------------------------------------------------------------
        // World queries.
        // ------------------------------------------------------------------

        /// <summary>
        /// The floor under a point on the ring, or nothing when the ring has left the
        /// building. Three conditions, and all three are load-bearing: a hit at all, a
        /// surface facing up rather than a wall face or a stair riser, and a height
        /// within one step of the 출입구 so the paint cannot walk down a stairwell.
        /// </summary>
        private static bool TryFloorUnder(Vector3 at, float entranceY, out Vector3 floor)
        {
            floor = at;

            var from = new Vector3(at.x, entranceY + ProbeRiseMetres, at.z);
            if (!Physics.Raycast(from, Vector3.down, out var hit, ProbeRiseMetres + ProbeDropMetres, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (hit.normal.y < MinimumFloorNormalY)
            {
                return false;
            }

            if (Mathf.Abs(hit.point.y - entranceY) > StepToleranceMetres)
            {
                return false;
            }

            // A stripe inside a wall is worse than no stripe: it is a line the player can
            // see the end of and cannot reach, which reads as a bug rather than a border.
            if (Physics.CheckSphere(hit.point + (Vector3.up * ClearHeadMetres), ClearRadiusMetres, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            floor = hit.point;
            return true;
        }

        private static bool OnNavMesh(Vector3 at)
        {
            return NavMesh.SamplePosition(at, out _, NavMeshSnapMetres, NavMesh.AllAreas);
        }

        /// <summary>
        /// The prop's renderers as one box in the prop's own space, so a yawed body
        /// reports its real footprint. Mirrors <c>Interactable.LocalBounds</c>, which is
        /// private to the type that sizes the crosshair trigger from it.
        /// </summary>
        private static bool TryLocalBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);

            var toProp = go.transform.worldToLocalMatrix;
            var started = false;

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var local = renderer.localBounds;
                var matrix = toProp * renderer.transform.localToWorldMatrix;

                for (var corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z);

                    var point = matrix.MultiplyPoint3x4(offset);
                    if (!started)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        started = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return started;
        }

        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds(go.transform.position, Vector3.zero);
            var started = false;

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!started)
                {
                    bounds = renderer.bounds;
                    started = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return started;
        }

        // ------------------------------------------------------------------
        // Materials.
        // ------------------------------------------------------------------

        /// <summary>
        /// A shader that is certainly in this build, taken from something already
        /// rendering.
        /// <para>
        /// <b>Not <c>Shader.Find</c>, and that is the whole point of this method.</b>
        /// <see cref="HorrorGame.Gameplay.Interaction.Interactable"/> records what the
        /// alternative cost: a shader no material asset references is stripped from a
        /// player build and <c>Shader.Find</c> then returns null with no error, which is
        /// how every interactable in the game shipped as Unity's error magenta while the
        /// editor looked correct. Borrowing the shader off a renderer that is already in
        /// the scene cannot have that failure mode — if it were missing there would be
        /// nothing to borrow it from and nothing to see anyway.
        /// </para>
        /// </summary>
        private static Shader? ResolveShader(GameObject? vehicle)
        {
            if (vehicle != null)
            {
                var fromProp = ShaderOn(vehicle);
                if (fromProp != null)
                {
                    return fromProp;
                }
            }

            // Anything the map itself is drawn with. The floor slabs are the largest
            // renderers in the scene and are never absent from a scene that has a match.
            var renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            for (var i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i].sharedMaterial;
                if (material != null && material.shader != null)
                {
                    return material.shader;
                }
            }

            return null;
        }

        private static Shader? ShaderOn(GameObject go)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var material = renderer.sharedMaterial;
                if (material != null && material.shader != null)
                {
                    return material.shader;
                }
            }

            return null;
        }

        /// <summary>
        /// One coat of paint. Emissive at a fraction of its own colour, so a stripe still
        /// reads where a lamp does not reach without becoming a light source of its own —
        /// §03's darkness is the objective's lock and this must not pick it.
        /// </summary>
        private static Material Painted(Shader shader, Color colour)
        {
            var material = new Material(shader) { name = "ApronPaint" };

            if (material.HasProperty(BaseColourId))
            {
                material.SetColor(BaseColourId, colour);
            }
            else
            {
                material.color = colour;
            }

            if (material.HasProperty(SmoothnessId))
            {
                material.SetFloat(SmoothnessId, PaintSmoothness);
            }

            if (material.HasProperty(MetallicId))
            {
                material.SetFloat(MetallicId, 0f);
            }

            if (material.HasProperty(EmissionId))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionId, colour * PaintEmission);

                // No GI contribution. There is no realtime GI in a match and this object
                // is created after the scene has loaded, so anything else here is a
                // lightmapper flag with nothing to say to it.
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            return material;
        }

        private Material Own(Material material)
        {
            _owned.Add(material);
            return material;
        }

        /// <summary>
        /// One painted box: a renderer and nothing else.
        /// <para>
        /// Deliberately not <c>GameObject.CreatePrimitive</c>, which arrives with a
        /// <see cref="BoxCollider"/> attached. §12's corridor widths are computed for the
        /// character and the monster, and a bollard that pushed either of them around at
        /// the one doorway every round trip funnels through would be a gameplay change
        /// wearing a decoration's clothes. Removing the collider afterwards would work
        /// and would leave it standing for the rest of the frame — <c>Destroy</c> is
        /// deferred — which is exactly long enough for a physics step to notice it.
        /// </para>
        /// </summary>
        private static GameObject Slab(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return go;
        }

        /// <summary>
        /// Unity's built-in cube, borrowed once and cached.
        /// <para>
        /// <c>Resources.GetBuiltinResource</c> is the direct way to ask for it and is not
        /// dependable in a stripped player build, so it is taken off a primitive that is
        /// deactivated before it can be drawn and thrown away immediately. Static because
        /// the mesh belongs to the engine and outlives every match.
        /// </para>
        /// </summary>
        private static Mesh? CubeMesh()
        {
            if (_cubeMesh != null)
            {
                return _cubeMesh;
            }

            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            probe.SetActive(false);

            var filter = probe.GetComponent<MeshFilter>();
            _cubeMesh = filter != null ? filter.sharedMesh : null;

            if (Application.isPlaying)
            {
                Destroy(probe);
            }
            else
            {
                DestroyImmediate(probe);
            }

            return _cubeMesh;
        }

        private void OnDestroy()
        {
            for (var i = 0; i < _owned.Count; i++)
            {
                var material = _owned[i];
                if (material == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(material);
                }
                else
                {
                    DestroyImmediate(material);
                }
            }

            _owned.Clear();
        }

        // ------------------------------------------------------------------
        // Rig geometry. Not §-numbers — the only tuned value this file uses is
        // MatchMap.SurfaceRadius, which it is handed.
        // ------------------------------------------------------------------

        /// <summary>Steps taken around the ring. 120 puts a stripe every 0.79 m at a 15 m radius.</summary>
        private const int StripeCount = 120;

        /// <summary>Across the line, metres. A painted bay marking, read at walking speed from several metres.</summary>
        private const float StripeWidthMetres = 0.40f;

        /// <summary>How proud of the floor the paint stands, metres. Enough to clear §12's gravel relief without becoming a kerb to trip on.</summary>
        private const float StripeRiseMetres = 0.05f;

        /// <summary>Shortest run of walkable ring that counts as a crossing rather than a gap in a wall, metres.</summary>
        private const float GateMinimumMetres = 1.6f;

        /// <summary>One gate lamp per this much opening, metres.</summary>
        private const float GateLampSpacingMetres = 5f;

        /// <summary>Cap on lamps over one crossing, so the 하역 베이's 20 m mouth does not become a runway.</summary>
        private const int MaxLampsPerGate = 4;

        private const float GateLampHeightMetres = 2.45f;
        private const float GateLampIntensity = 1.55f;
        private const float GateLampRangeMetres = 9f;

        private const float BayLampHeightMetres = 3.3f;
        private const float BayLampIntensity = 1.7f;
        private const float BayLampRangeMetres = 16f;

        /// <summary>One practical per this much of the walk out, metres. Close enough that the pools overlap into a lit run rather than a row of spots.</summary>
        private const float WalkLampSpacingMetres = 4.5f;

        private const float WalkLampHeightMetres = 2.65f;
        private const float WalkLampIntensity = 1.25f;
        private const float WalkLampRangeMetres = 9f;

        private const float PostHeightMetres = 1.05f;
        private const float PostSideMetres = 0.16f;
        private const float PostCapMetres = 0.16f;

        private const float HeadlampSpreadMetres = 0.85f;
        private const float HeadlampHeightMetres = 0.80f;
        private const float HeadlampPitchDegrees = 9f;
        private const float HeadlampConeDegrees = 68f;
        private const float HeadlampIntensity = 2.6f;
        private const float HeadlampRangeMetres = 24f;

        private const float WorkLampHeightMetres = 2.35f;
        private const float WorkLampIntensity = 1.6f;
        private const float WorkLampRangeMetres = 9f;

        /// <summary>How far clear of the rear doors the work lamp stands, metres. Inside the load box it lit nothing.</summary>
        private const float WorkLampStandOffMetres = 1.1f;

        private const float BayFillOffsetMetres = 2.4f;
        private const float BayFillRiseMetres = 1.5f;
        private const float BayFillIntensity = 1.5f;
        private const float BayFillRangeMetres = 12f;

        private const float BeaconRiseMetres = 0.22f;
        private const float BeaconIntensity = 1.05f;
        private const float BeaconRangeMetres = 14f;
        private const float BeaconRadiansPerSecond = 3.4f;
        private const float BeaconDim = 0.35f;
        private const float BeaconBright = 1.7f;

        /// <summary>How long a crossing reads for, seconds. Long enough to see while walking through it.</summary>
        private const float BeatSeconds = 1.4f;

        /// <summary>§01's 숨 돌리기, as a fraction the apron lamps rise by on arrival.</summary>
        private const float BeatSwell = 0.55f;

        /// <summary>§01's 잠입, as the fraction the same lamps fall by on the way out. Negative — the warmth draws back.</summary>
        private const float BeatDip = -0.45f;

        private const float ProbeRiseMetres = 2.0f;
        private const float ProbeDropMetres = 2.0f;
        private const float MinimumFloorNormalY = 0.6f;
        private const float StepToleranceMetres = 1.25f;
        private const float ClearHeadMetres = 0.9f;
        private const float ClearRadiusMetres = 0.22f;
        private const float NavMeshSnapMetres = 0.8f;
        private const float ClearanceProbeMetres = 20f;

        /// <summary>Room asked for round the parked van, metres, most generous first. The last is the body itself.</summary>
        private static readonly float[] ParkingClearances = { 0.55f, 0.30f, 0.10f };

        /// <summary>Closest the van is allowed to the 출입구, metres. Inside this it is in the stairwell mouth.</summary>
        private const float ParkingStartMetres = 4f;

        /// <summary>How far inside §01's 지상 the van's centre has to stay, metres, so a player standing at the shop end is on the surface.</summary>
        private const float ParkingKeepInsideMetres = 2f;

        private const float ParkingStepMetres = 1.25f;
        private const int ParkingBearings = 24;

        /// <summary>
        /// <c>MapKitCatalogue.CorridorClearWidth</c>, quoted for a log line. Not
        /// referenced: that type is editor-only and this runs in a player.
        /// </summary>
        private const float MapCorridorClearWidthMetres = 2.2f;

        private const float PaintSmoothness = 0.28f;
        /// <summary>
        /// How much of its own colour the paint gives back where no lamp reaches. Read off
        /// a frame taken 25 m out across the 하역 베이: at 0.30 the far half of the arc
        /// disappeared into the floor, and the whole point is that it is visible before
        /// you are standing on it.
        /// </summary>
        private const float PaintEmission = 0.45f;

        private static readonly Color AmberPaint = new Color(0.95f, 0.62f, 0.10f);
        private static readonly Color BonePaint = new Color(0.86f, 0.83f, 0.74f);
        private static readonly Color PostPaint = new Color(0.78f, 0.76f, 0.70f);

        private static readonly Color GateLampColour = new Color(1.00f, 0.80f, 0.55f);
        private static readonly Color BayLampColour = new Color(1.00f, 0.85f, 0.63f);
        private static readonly Color WalkLampColour = new Color(1.00f, 0.83f, 0.60f);
        private static readonly Color HeadlampColour = new Color(1.00f, 0.95f, 0.86f);
        private static readonly Color WorkLampColour = new Color(1.00f, 0.82f, 0.56f);
        private static readonly Color BayFillColour = new Color(1.00f, 0.86f, 0.66f);
        private static readonly Color BeaconColour = new Color(1.00f, 0.58f, 0.14f);

        private static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    }
}
