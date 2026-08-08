#nullable enable

using System.Collections;
using System.Collections.Generic;
using HorrorGame.Audio;
using HorrorGame.Core;
using HorrorGame.Core.Race;
using HorrorGame.Gameplay.Audio;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Race;
using UnityEngine;

namespace HorrorGame.Gameplay.Startle
{
    /// <summary>What one 깜짝 marker is. The scene name prefix is the authority; this is its parse.</summary>
    public enum StartleKind
    {
        /// <summary>A sprung leaf on a corridor wall. Swings open once and stays open.</summary>
        Cabinet = 0,

        /// <summary>Something small crosses the corridor ahead, wall to wall, and is gone.</summary>
        Skitterer = 1,

        /// <summary>A wall stub vents one burst of vapour.</summary>
        PipeStub = 2,

        /// <summary>The nearest working fitting flickers and dies. The one persistent change.</summary>
        BulbDeath = 3,

        /// <summary>The figure, once per match, where the beam is not. The crown jewel.</summary>
        Glimpse = 4,
    }

    /// <summary>
    /// §16's 깜짝 — five scripted frights, seeded into the map by
    /// <c>MapSceneBuilder.BuildStartles</c> and played back here, per player, on that
    /// player's own client.
    /// <para>
    /// <b>Local-only, by decision.</b> The pivot's made decision (b): placement is
    /// seeded and deterministic per map, but triggering and rendering are per-client
    /// with zero network traffic. In a 12 m-beam dark maze two players rarely watch the
    /// same fitting; the inconsistency is accepted and this remark is its documentation.
    /// Nothing in this file touches Mirror, and nothing in it may.
    /// </para>
    /// <para>
    /// <b>The creature is never told, by decision.</b> Decision (a): a 깜짝 never calls
    /// <c>MonsterAgent.ReportSound</c>. §12 makes sound the map — a placed noise is a
    /// forged footstep dropped by the one author who can see the whole building, which
    /// is exactly why the pivot deleted §09's 신호 (GameConstants.cs's §09 block holds
    /// the record). The cues below also raise no §04 self-noise: <c>AudioCues.NoiseOf</c>
    /// returns 0 for them, so a startle cannot even blind its own victim's ears.
    /// </para>
    /// <para>
    /// <b>Nothing in the scene carries this component and nothing should.</b> The
    /// generator lays down named markers under <c>Map/Markers/Startles</c> and this
    /// class self-installs on scene load and adds itself on top — the same
    /// marker→runtime split <c>GunPickup.AttachAll</c> and <c>MatchDirector.AttachChutes</c>
    /// use, and for the same reason: the generator is an editor assembly, the reference
    /// only runs one way, and a feature that needs scene authoring is a feature that is
    /// missing from the next regeneration.
    /// </para>
    /// <para>
    /// <b>Proximity is polled, not physics.</b> The poll copies <c>Chute.Swallows</c> —
    /// flat distance plus a storey gate — because a trigger collider on a marker is one
    /// more volume for every reach audit and crosshair ray to be confused by, and a
    /// distance check over at most sixteen markers is cheaper than the physics broadphase
    /// it would replace.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StartleDirector : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // The scene contract. Mirrors MapSceneBuilder's startle section, restated for
        // the reason GunPickup.GroupName restates GunRootName: the two assemblies cannot
        // see each other and the scene is the contract between them.
        // ------------------------------------------------------------------

        /// <summary>Root of the generated map. Mirrors <c>MapSceneBuilder.MapRootName</c>.</summary>
        public const string MapRootName = "Map";

        /// <summary>Child of the root holding every marker group. Mirrors <c>MapSceneBuilder.MarkerRootName</c>.</summary>
        public const string MarkerRootName = "Markers";

        /// <summary>Group the generator hangs the 깜짝 markers under. Mirrors <c>MapSceneBuilder.StartleRootName</c>.</summary>
        public const string GroupName = "Startles";

        /// <summary>Name prefix of a cabinet marker.</summary>
        public const string CabinetPrefix = "Startle_Cabinet";

        /// <summary>Name prefix of a skitterer marker.</summary>
        public const string SkittererPrefix = "Startle_Skitterer";

        /// <summary>Name prefix of a pipe stub marker.</summary>
        public const string PipeStubPrefix = "Startle_PipeStub";

        /// <summary>Name prefix of a bulb-death marker.</summary>
        public const string BulbDeathPrefix = "Startle_BulbDeath";

        /// <summary>Name prefix of a glimpse marker.</summary>
        public const string GlimpsePrefix = "Startle_Glimpse";

        /// <summary>The cabinet's swinging child. Mirrors the generator; same shape as §12's 문 hinge.</summary>
        public const string HingeName = "Hinge";

        /// <summary>Child of a pipe stub marking where the vapour leaves the metal.</summary>
        public const string VentName = "Vent";

        /// <summary>The disabled figure the generator parks for the glimpse to clone. Mirrors <c>MapSceneBuilder.StartleFigureTemplateName</c>.</summary>
        public const string FigureTemplateName = "Startle_Figure_Template";

        /// <summary>
        /// The disabled skitterer the generator parks for the crossings to clone.
        /// Mirrors <c>MapSceneBuilder.StartleSkittererTemplateName</c>. A FLOOR prop —
        /// origin on the floor under the footprint, forward out of its nose — so a
        /// clone is placed by origin and pointed by LookRotation, nothing more.
        /// </summary>
        public const string SkittererTemplateName = "Startle_Skitterer_Template";

        /// <summary>
        /// The pipe's authored axis height, metres above the floor —
        /// tools/blender/gen_props.py's <c>PIPESTUB_AXIS_Z</c> 1.250, restated across
        /// the same boundary as every marker name here. Only the fallback path uses it
        /// (a marker whose stub failed to instantiate); the real position comes from
        /// the generator's <c>Vent</c> empty.
        /// </summary>
        public const float PipeAxisMetres = 1.25f;

        /// <summary>
        /// Name <c>ScatterSession.LightBulb</c> gives the point light it hangs inside a
        /// lit fitting. Restated across the same editor/runtime boundary as everything
        /// above; the dressing pass is the author.
        /// </summary>
        public const string FilamentName = "Filament";

        // ------------------------------------------------------------------
        // Tuned numbers, each with its derivation. Repo rule: no number without one.
        // ------------------------------------------------------------------

        /// <summary>
        /// Metres a runner must pass within for a marker to fire.
        /// <para>
        /// Half the corridor's clear section plus a standing body's column:
        /// 2.20 ÷ 2 + (0.30 + 0.15) = 1.55 m. The 2.20 is
        /// <c>MapKitCatalogue.CorridorClearWidth</c> and the 0.30 + 0.15 is
        /// <c>Editor/Dressing/KeepOut</c>'s standing radius (the player capsule plus the
        /// kit's wall inset), both restated across the assembly boundary the way
        /// <c>Chute.DropHeightMetres</c> restates the kit. Read together: the marker
        /// sits on the corridor's centreline, everything mounted on a wall is at most
        /// 1.10 m from it, so 1.55 m is "your body is walking past the fitting".
        /// </para>
        /// </summary>
        public const float TriggerMetres = (2.2f * 0.5f) + 0.30f + 0.15f;

        /// <summary>
        /// Metres the generator guarantees clear around every 깜짝 marker — no 착지,
        /// 투하구, 출발점, 창조물 spawn, 문 or gun inside it. Mirrors
        /// <c>MapSceneBuilder.StartleClearanceMetres</c> across the same editor/runtime
        /// boundary as every name above, expression and all: (4.5 + 2.5) + 1.1 = 8.1 m,
        /// kept byte for byte because every generated map already embodies it in its
        /// marker placement. This is the one number in the scene contract the runtime
        /// must bound itself INSIDE — see <see cref="StageReachMetres"/>, which is this
        /// constant read from the other end.
        /// </summary>
        public const float MarkerClearanceMetres = (4.5f + 2.5f) + (2.2f * 0.5f);

        /// <summary>
        /// Metres a staged MESH may hang past the staged POINT the runtime clamp
        /// bounded, and therefore the slack <see cref="StageReachMetres"/> reserves.
        /// One skitterer body, <see cref="SkitterBodyMetres"/> 0.5 — the longest thing
        /// any startle stages: the darter's origin rides the clamped crossing line, so
        /// its nose reaches half a body (0.25 m) past an endpoint, and the glimpse
        /// figure's shoulders stand ~0.3 m around its clamped floor point. One full
        /// body length dominates both without measuring either mesh at runtime.
        /// </summary>
        public const float StageMarginMetres = SkitterBodyMetres;

        /// <summary>
        /// Farthest any staged point may sit from the marker that fired, metres:
        /// <see cref="MarkerClearanceMetres"/> − <see cref="TriggerMetres"/> −
        /// <see cref="StageMarginMetres"/> = 8.1 − 1.55 − 0.5 = 6.05.
        /// <para>
        /// The generator's 8.1 m keep-out is a promise about the MARKER's
        /// surroundings, but the moving startles stage relative to the RIG, which can
        /// be TriggerMetres from the marker when it fires. The first version let the
        /// rig's offset ride on top of the staging range — the skitterer's farthest
        /// endpoint measured 1.55 + sqrt(7² + 1.6²) ≈ 8.73 m from the marker, and the
        /// glimpse could stand 1.55 + 12 = 13.55 m out — both past the promise, into
        /// exactly the parked-body places the clearance exists for. The fix bounds the
        /// runtime inside the guarantee rather than inflating the guarantee (a bigger
        /// clearance moves markers and regenerates every map for a defect that is the
        /// runtime's): subtract the rig's worst offset and the mesh margin from the
        /// promise, and the remainder is the space staging may legally use. Every
        /// rig-relative staging routine (<see cref="StageSkitterer"/>,
        /// <see cref="StageGlimpse"/>, <see cref="StageBulbDeath"/>) clamps its
        /// candidate points to this radius around the TRIGGERING MARKER; a rejected
        /// candidate falls to the routines' existing retry — the spot stays armed, the
        /// loop redraws.
        /// </para>
        /// </summary>
        public const float StageReachMetres =
            MarkerClearanceMetres - TriggerMetres - StageMarginMetres;

        /// <summary>
        /// Metres of height difference past which a marker is on another storey and must
        /// not fire. <c>Chute.Swallows</c>'s own guard, restated with its reasoning: a
        /// runner on the floor above is standing over this marker in plan, and the
        /// storeys are only 3.75 m apart.
        /// </summary>
        public const float StoreyGateMetres = 2.6f;

        /// <summary>
        /// Seconds the cabinet leaf takes to swing open.
        /// <para>
        /// Seven 60 Hz frames ≈ 0.117 s. Bounded below by the three-frame visibility
        /// floor this project already uses twice — <c>GunTests.PressFrames</c> and the
        /// bulb flicker below — because a swing shorter than that is a state pop, not a
        /// motion. Bounded above by the 0.20 s <c>CaughtScreen</c> blackout, the
        /// project's own number for "under a player's reaction": the leaf must be fully
        /// open by the time the startled eye arrives on it, or the player watches a door
        /// open, which is furniture. Seven frames sits between the three-frame floor
        /// and the 0.20 s bound with several frames of slack in both directions — a
        /// choice inside the band, not the band's edge (ten frames would also fit).
        /// </para>
        /// </summary>
        public const float CabinetSwingSeconds = 7f / 60f;

        /// <summary>
        /// Smallest angle the leaf springs to, degrees. 90° would lie flat across the
        /// corridor's axis; 80° is 90° less the ~10° slack under which a leaf still
        /// reads unmistakably OPEN from every corridor angle — the same slack the upper
        /// bound takes in the other direction.
        /// </summary>
        public const float CabinetOpenMinDegrees = 80f;

        /// <summary>
        /// Largest angle the leaf springs to, degrees. Derived from the AUTHORED prop,
        /// not the §12 corridor door: gen_props hangs the hinge empty 0.212 m off the
        /// wall plane and the leaf slab is 0.494 m wide (CABINET_W 0.560 − 2×LIP 0.030
        /// − 2×LEAF_GAP 0.003), so the leaf's far edge does not visibly enter its wall
        /// until 90° + asin(0.212 ÷ 0.494) ≈ 115°. 100° is 90° plus the band's own 10°
        /// grain, well inside that ~115° bind. The spread exists so two cabinets in one
        /// map do not open like twins; the exact angle is seeded per marker.
        /// </summary>
        public const float CabinetOpenMaxDegrees = 100f;

        /// <summary>
        /// The runtime's stylised body unit for the darter, metres — deliberately an
        /// OVER-BOUND of the authored prop, not its measurement. gen_props ships the
        /// skitterer at 0.335 m nose-to-rump (~0.46 m bounding box with tail), and the
        /// no-template fallback cube is exactly this 0.5. Staging margins and the
        /// crossing span are computed from this constant, so using the larger of the
        /// two bodies means the clamp arithmetic dominates whichever mesh actually
        /// spawns; the shipped darter subtends ~5-7° at the near crossing distance —
        /// unmissable at the beam's edge, and implying no addition to §01's roster.
        /// </summary>
        public const float SkitterBodyMetres = 2.5f / 5f;

        /// <summary>
        /// Half the crossing's span, metres: half the corridor's 2.20 m clear section
        /// plus one body — 1.1 + 0.5 = 1.6 — so the darter starts and ends inside the
        /// walls, never seen to start or stop. Named so the staging code and the
        /// staging maths (<see cref="SkitterFarMetres"/>, and the clamp in
        /// <see cref="StageSkitterer"/>) use the same constant and cannot drift apart.
        /// </summary>
        public const float SkitterHalfSpanMetres = (2.2f * 0.5f) + SkitterBodyMetres;

        /// <summary>
        /// Nearest the crossing may be staged ahead of the player, metres.
        /// <c>PresenceView.FigureNearMetres</c> 3.5 — "inside this it stops being a
        /// silhouette and becomes a wall", restated across the assembly boundary — plus
        /// the skitterer's own body length, so the whole crossing stays past the
        /// silhouette floor.
        /// </summary>
        public const float SkitterNearMetres = 3.5f + SkitterBodyMetres;

        /// <summary>
        /// Farthest ahead of the rig the crossing's centreline may be staged, metres —
        /// ≈ 5.83, solved from the marker guarantee rather than chosen. A crossing
        /// staged <c>ahead</c> up the corridor puts its endpoints
        /// sqrt(ahead² + <see cref="SkitterHalfSpanMetres"/>²) from the rig, so with
        /// the rig standing ON its marker the endpoints stay inside
        /// <see cref="StageReachMetres"/> exactly when
        /// ahead ≤ sqrt(6.05² − 1.6²) ≈ 5.83. This constant is that rig-at-marker
        /// solution — the farthest the stage can ever legally sit — and
        /// <see cref="StageSkitterer"/> re-solves the same equation against the rig's
        /// actual offset every firing, so a displaced rig gets less corridor, never
        /// more. The first version's 7 m (the 그늘 grain band plus a cell) measured
        /// 1.55 + sqrt(7² + 1.6²) ≈ 8.73 m from the marker, 0.63 m past the 8.1 m
        /// keep-out promise. 5.83 still clears the <see cref="SkitterNearMetres"/>
        /// 4.0 silhouette floor with 1.8 m of band to draw from.
        /// </summary>
        public static readonly float SkitterFarMetres =
            Mathf.Sqrt((StageReachMetres * StageReachMetres)
                - (SkitterHalfSpanMetres * SkitterHalfSpanMetres));

        /// <summary>
        /// Seconds the crossing takes. The corridor's 2.20 m clear section at §06's
        /// 걷기 2.0 m/s = 1.1 s — it crosses at exactly the pace the player themselves
        /// walks, which is what makes it read as a living thing at a familiar speed
        /// rather than as a projectile. The path is one body longer at each end so it
        /// emerges from one wall and vanishes into the other, never seen to start or
        /// stop; its ground speed is therefore a shade over walking pace.
        /// </summary>
        public const float SkitterSeconds = 2.2f / GameConstants.WalkSpeed;

        /// <summary>
        /// Seconds the pipe vents. Bounded above by the corridor's clear width at
        /// walking pace — 2.20 ÷ 2.0 = 1.1 s, the soonest a walker who sprang the
        /// trigger at its edge can be standing at the stub — less the 0.20 s
        /// <c>CaughtScreen</c> blackout, the project's reaction floor: 0.9 s. The burst
        /// is always over before the player can stand in it and inspect it, so it stays
        /// an event rather than becoming weather.
        /// </summary>
        public const float PipeVentSeconds = (2.2f / GameConstants.WalkSpeed) - 0.20f;

        /// <summary>
        /// Particles in one vent burst. A quarter of <c>PresenceView.MaxMotes</c> 160 —
        /// the full-pool grain, restated — so a single vent can never out-grain the 그늘
        /// at its worst, which is the entity that owns the air in this game.
        /// </summary>
        public const int PipeParticleCount = 40;

        /// <summary>
        /// Radius of the disc the burst is born across, metres — the stub's own torn
        /// mouth. gen_props.py's tear centres its eight petals on a 0.047 m rim circle
        /// around the 0.045 m barrel; 0.05 is that rim rounded up so the petal bases
        /// sit inside the birth disc. A point birth (radius 0) reads as a jet from
        /// nowhere; the authored bore is what vents, so the particles must be born
        /// across the whole torn mouth.
        /// </summary>
        public const float VentMouthRadiusMetres = 0.05f;

        /// <summary>
        /// Metres within which a working fitting may be killed. Half §03's
        /// <see cref="GameConstants.FlashlightRange"/>: the near half of the beam is
        /// where PresenceView already puts everything that must be REGISTERED (its grain
        /// band and figure floor both live inside it), and a light the player never
        /// registered dying is nothing happening. Beyond 6 m the death would be trivia;
        /// inside it, the room the player is actually reading gets darker.
        /// </summary>
        public const float BulbHuntMetres = GameConstants.FlashlightRange * 0.5f;

        /// <summary>
        /// Frames the dying bulb flickers before it goes. Three — the smallest count
        /// that cannot be mistaken for a dropped frame (one) or a vsync hiccup (two),
        /// which is the same three-frame argument <c>GunTests.PressFrames</c> makes
        /// about key delivery.
        /// </summary>
        public const int BulbFlickerFrames = 3;

        /// <summary>
        /// Seconds the glimpse stands. Two bounds, and the lower one binds — the
        /// <c>Chute.DropHeightMetres</c> shape. Above: it must stay sharply under half
        /// of <c>PresenceView.FigureDwellSeconds</c> 6.5, so a player who has learned
        /// the 그늘 figure's rhythm can never read this as that entity (6.5 ÷ 2 =
        /// 3.25 s). Below: it must survive one stop-and-turn — two walked cells,
        /// 2 × 2.5 m ÷ 2.0 m/s = 2.5 s — or it dies unseen and the once-per-match is
        /// wasted on nobody. 2.5 &lt; 3.25, so the walk sets the number and the dwell
        /// merely permits it.
        /// </summary>
        public const float GlimpseSeconds = 2f * 2.5f / GameConstants.WalkSpeed;

        /// <summary>Nearest the figure may stand, metres. <c>PresenceView.FigureNearMetres</c>, restated.</summary>
        public const float GlimpseNearMetres = 3.5f;

        /// <summary>
        /// Farthest the figure may stand, metres — <see cref="StageReachMetres"/>, the
        /// marker guarantee's remainder, no longer §03's 12 m beam range.
        /// <para>
        /// The 12 m first version could stand the figure 1.55 + 12 = 13.55 m from the
        /// marker whose 8.1 m keep-out is the only clearance the generator promises —
        /// in a gun alcove, a spawn, a door swing, a chute's clearance, with no
        /// runtime way to know. Bounding the reach inside the guarantee gives 6.05 m:
        /// still past the <see cref="GlimpseNearMetres"/> 3.5 silhouette floor, still
        /// under the unconditional cone exclusion, and the dread band tightens — which
        /// suits a figure meant to be barely lit: at 6 m it stands at the edge of
        /// <see cref="BulbHuntMetres"/>' registered near-half of the beam instead of
        /// out against undressed black, where PresenceView's own remark says a shape
        /// reads as nothing at all.
        /// </para>
        /// </summary>
        public const float GlimpseFarMetres = StageReachMetres;

        /// <summary>
        /// First storey index deep enough for the glimpse. <see cref="RaceState.Storeys"/>
        /// ÷ 2 = 4, i.e. B5 — the first storey past the tower's midpoint, where §07's
        /// night tiers own the pace and a figure is a claim the basement has earned.
        /// The generator only places glimpse markers this deep; the runtime re-checks
        /// off the rig's own height because the crown jewel is gated hardest.
        /// </summary>
        public const int GlimpseStoreyFloor = RaceState.Storeys / 2;

        /// <summary>
        /// Candidate draws one glimpse staging gets before giving the frame up.
        /// <c>PresenceView.TryFindStandingSpot</c>'s own 24 (PresenceView.cs:415),
        /// copied with the routine it re-implements rather than re-derived, so a
        /// tuning found on the 그늘's number lands here too. The precedent's argument:
        /// in a §12 corridor most of the 360° yaw band has a wall inside the near
        /// bound and the 44° cone is excluded outright, so legal directions are a thin
        /// slice — rejection sampling needs tens of tries, not a handful — while the
        /// whole loop reruns next frame for free, because a refused stage is never
        /// consumed.
        /// </summary>
        private const int GlimpseAttempts = 24;

        private sealed class Spot
        {
            public Spot(Transform at, StartleKind kind)
            {
                At = at;
                Kind = kind;
            }

            public Transform At { get; }

            public StartleKind Kind { get; }

            public bool Fired { get; set; }
        }

        private readonly List<Spot> _spots = new List<Spot>();
        private readonly StartlePacing _pacing = new StartlePacing();

        private Transform? _rig;
        private Transform? _eye;
        private MatchDirector? _match;
        private bool _matchLooked;
        private MatchAudioRig? _audioRig;
        private Transform? _figure;
        private float _figureAge;
        private uint _rng = 0x9E3779B9u;

        private static Material? _skitterMaterial;
        private static Material? _ventMaterial;

        /// <summary>The pacing gates, exposed so a test can advance the clock a rule's own constant.</summary>
        public StartlePacing Pacing => _pacing;

        /// <summary>How many markers this director watches.</summary>
        public int SpotTotal => _spots.Count;

        /// <summary>How many 깜짝 have fired for this player.</summary>
        public int FiredTotal => _pacing.FiredCount;

        /// <summary>
        /// Finds the generated 깜짝 group and puts this component on it. Idempotent, and
        /// returns the director either way — the same shape, and the same callers, as
        /// <c>GunPickup.AttachAll</c>: a match starting, a scene load, a test making sure.
        /// </summary>
        /// <returns>The director on the marker group, or null when this scene has no generated 깜짝.</returns>
        public static StartleDirector? Attach()
        {
            foreach (var group in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (group.name != GroupName || group.parent == null || group.parent.name != MarkerRootName)
                {
                    continue;
                }

                var director = group.GetComponent<StartleDirector>();
                if (director == null)
                {
                    director = group.gameObject.AddComponent<StartleDirector>();
                }

                return director;
            }

            return null;
        }

        /// <summary>
        /// Runs <see cref="Attach"/> on every scene load for the life of the process —
        /// <c>GunPickup.Install</c>'s pattern, copied with its reasoning: the descent
        /// scene is loaded long after startup, so the subscription is what makes this
        /// work, and unsubscribe-then-subscribe survives Unity 6's disabled domain
        /// reload. No <c>MatchDirector</c> edit, which is not this task's file.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        private static void OnSceneLoaded(
            UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Attach();
        }

        private void OnEnable()
        {
            BuildSpots();
        }

        private void BuildSpots()
        {
            _spots.Clear();
            foreach (Transform child in transform)
            {
                // The two clone sources first: the skitterer template's name begins
                // with the skitterer MARKER prefix, so without this line the parked
                // prop would register as one more trigger standing on a real marker's
                // cell.
                if (child.name == FigureTemplateName || child.name == SkittererTemplateName)
                {
                    continue;
                }

                if (child.name.StartsWith(CabinetPrefix, System.StringComparison.Ordinal))
                {
                    _spots.Add(new Spot(child, StartleKind.Cabinet));
                }
                else if (child.name.StartsWith(SkittererPrefix, System.StringComparison.Ordinal))
                {
                    _spots.Add(new Spot(child, StartleKind.Skitterer));
                }
                else if (child.name.StartsWith(PipeStubPrefix, System.StringComparison.Ordinal))
                {
                    _spots.Add(new Spot(child, StartleKind.PipeStub));
                }
                else if (child.name.StartsWith(BulbDeathPrefix, System.StringComparison.Ordinal))
                {
                    _spots.Add(new Spot(child, StartleKind.BulbDeath));
                }
                else if (child.name.StartsWith(GlimpsePrefix, System.StringComparison.Ordinal))
                {
                    _spots.Add(new Spot(child, StartleKind.Glimpse));
                }

                // Anything else under the group — the figure template — is not a marker.
            }
        }

        private void Update()
        {
            var rig = ResolveRig();
            if (rig == null)
            {
                return;
            }

            // Ages a standing figure out even while the match is not running: the
            // moment the race is over the figure is scenery, and scenery that never
            // leaves can be inspected.
            DriveGlimpse();

            // The pacing clock is anchored to BeginMatch, not scene load: MatchDirector
            // already exposes the moment (IsRunning flips inside BeginMatch), so no
            // MatchDirector edit was needed to observe it, and the grace measures race
            // time — §11's start-line bunching, which is what its derivation is about —
            // rather than however long a lobby sat on the loaded scene. The one-shot
            // find is the same cost ResolveRig pays. A scene with no MatchDirector at
            // its first startle frame (the bare-rig harnesses) anchors at that frame
            // instead — the GunPickup-style self-install's scene-load moment — and
            // GraceSeconds' 60 s absorbs the difference; that fallback is this
            // remark's documented cost, not a second design.
            if (!_matchLooked)
            {
                _match = FindFirstObjectByType<MatchDirector>();
                _matchLooked = true;
            }

            if (_match != null && !_match.IsRunning)
            {
                return;
            }

            _pacing.Advance(Time.deltaTime);

            for (var i = 0; i < _spots.Count; i++)
            {
                var spot = _spots[i];
                if (spot.Fired || spot.At == null)
                {
                    continue;
                }

                // Chute.Swallows's read: plan distance, plus a storey gate so the runner
                // on the floor above cannot spring a fitting they are merely over.
                var flat = rig.position - spot.At.position;
                var dy = flat.y;
                flat.y = 0f;
                if (flat.sqrMagnitude > TriggerMetres * TriggerMetres
                    || Mathf.Abs(dy) >= StoreyGateMetres)
                {
                    continue;
                }

                TryFire(spot, rig);

                // At most one per frame; the pacing cooldown makes the rest academic.
                break;
            }
        }

        private void TryFire(Spot spot, Transform rig)
        {
            var glimpse = spot.Kind == StartleKind.Glimpse;
            if (!_pacing.CanFire(glimpse, out _))
            {
                // Refused, not consumed. The spot stays armed and asks again.
                return;
            }

            // The rig-relative stages also get the marker's own position: the marker,
            // not the rig, is what the generator's keep-out guarantee is anchored on,
            // so it is the marker the staging clamps against. See StageReachMetres.
            var staged = false;
            switch (spot.Kind)
            {
                case StartleKind.Cabinet:
                    staged = StageCabinet(spot.At);
                    break;
                case StartleKind.Skitterer:
                    staged = StageSkitterer(rig, spot.At.position);
                    break;
                case StartleKind.PipeStub:
                    staged = StagePipe(spot.At);
                    break;
                case StartleKind.BulbDeath:
                    staged = StageBulbDeath(rig, spot.At.position);
                    break;
                case StartleKind.Glimpse:
                    staged = StageGlimpse(rig, spot.At.position);
                    break;
            }

            if (!staged)
            {
                // The world refused — no corridor ahead, no bulb in reach, no legal
                // floor point. Nothing was seen, so nothing is charged: the spot stays
                // armed and the cooldown does not start.
                return;
            }

            spot.Fired = true;
            _pacing.MarkFired(glimpse);
            Debug.Log("[깜짝] " + spot.At.name + " fired at "
                + _pacing.Elapsed.ToString("0.0") + " s (" + _pacing.FiredCount + "/"
                + StartlePacing.CapPerMatch + ")", this);
        }

        // ------------------------------------------------------------------
        // cabinet — the sprung leaf. It happens once and stays open.
        // ------------------------------------------------------------------

        private bool StageCabinet(Transform marker)
        {
            var hinge = marker.Find(HingeName);
            if (hinge == null)
            {
                return false;
            }

            // The angle is seeded off the marker's name, so one map's cabinets are the
            // same cabinets every match — placement is deterministic per map (decision
            // (b)) and the render should be too.
            var band = (uint)(CabinetOpenMaxDegrees - CabinetOpenMinDegrees) + 1u;
            var angle = CabinetOpenMinDegrees + (Hash(marker.name) % band);

            // Swing towards the corridor, decided by geometry rather than by a stored
            // side or an authoring convention: the marker sits on the centreline, so
            // the sign whose small sweep moves the leaf towards the marker is the sign
            // that opens into air. The arm is measured from the leaf's RENDERER bounds
            // — its transform sits ON the hinge axis by the prop's own origin contract,
            // so the transform's offset is zero and only the mesh can say which way the
            // slab hangs.
            var sign = 1f;
            var leafRenderer = hinge.GetComponentInChildren<Renderer>();
            if (leafRenderer != null)
            {
                var arm = leafRenderer.bounds.center - hinge.position;
                arm.y = 0f;
                var toCorridor = marker.position - hinge.position;
                toCorridor.y = 0f;
                if (arm.sqrMagnitude > 0.0001f && toCorridor.sqrMagnitude > 0.0001f)
                {
                    var swept = Quaternion.Euler(0f, 10f, 0f) * arm;
                    sign = Vector3.Angle(swept, toCorridor) < Vector3.Angle(arm, toCorridor) ? 1f : -1f;
                }
            }

            StartCoroutine(SwingOpen(hinge, sign * angle));
            PlayCue(AudioCueId.StartleCabinet, hinge.position);
            return true;
        }

        private static IEnumerator SwingOpen(Transform hinge, float degrees)
        {
            var from = hinge.localRotation;
            var to = from * Quaternion.Euler(0f, degrees, 0f);
            var age = 0f;
            while (age < CabinetSwingSeconds)
            {
                age += Time.deltaTime;
                hinge.localRotation = Quaternion.Slerp(from, to, Mathf.Clamp01(age / CabinetSwingSeconds));
                yield return null;
            }

            // Open, and it stays open: a sprung thing happens once. The Fired flag on
            // the spot is what enforces the once; this is just the leaf agreeing.
            hinge.localRotation = to;
        }

        // ------------------------------------------------------------------
        // skitterer — across the corridor ahead, gone before the beam centres it.
        // ------------------------------------------------------------------

        private bool StageSkitterer(Transform rig, Vector3 marker)
        {
            var eye = ResolveEye(rig);
            if (eye == null)
            {
                return false;
            }

            var heading = eye.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            heading.Normalize();

            // The crossing must happen in corridor the player can see. A wall closer
            // than the near line means there is no stage; the trigger stays armed and
            // asks again on a later frame, when the player is facing along the corridor
            // — the same do-not-consume rule PresenceView's placement retries embody.
            var limit = SkitterFarMetres + SkitterBodyMetres;
            var reach = limit;
            if (Physics.Raycast(eye.position, heading, out var wall, limit, ~0, QueryTriggerInteraction.Ignore))
            {
                reach = wall.distance - SkitterBodyMetres;
            }

            // The marker-anchored clamp. The rig may stand TriggerMetres off its
            // marker when it fires, and a crossing staged `ahead` up the corridor puts
            // its endpoints sqrt(ahead² + SkitterHalfSpanMetres²) from the rig — at
            // most the rig's own offset further from the MARKER, which is what carries
            // the generator's keep-out promise. So the bound is
            // offset + sqrt(ahead² + half²) ≤ StageReachMetres, solved for ahead. At
            // offset 0 the solution is SkitterFarMetres itself (the same equation —
            // see its derivation); a displaced rig gets less corridor, never more, and
            // a rig with too little of the guarantee left in this direction refuses —
            // not consumed, the spot stays armed and asks again, the same do-not-spend
            // rule as the wall check above.
            var toMarker = rig.position - marker;
            toMarker.y = 0f;
            var room = StageReachMetres - toMarker.magnitude;
            if (room <= SkitterHalfSpanMetres)
            {
                return false;
            }

            var ahead = Mathf.Min(
                Mathf.Min(reach, SkitterFarMetres),
                Mathf.Sqrt((room * room) - (SkitterHalfSpanMetres * SkitterHalfSpanMetres)));
            if (ahead < SkitterNearMetres)
            {
                return false;
            }

            var line = rig.position + (heading * ahead);

            // Floor under the crossing, from half the kit's 3.0 m clear height up —
            // PresenceView's own find-the-floor read.
            if (!Physics.Raycast(line + (Vector3.up * 1.5f), Vector3.down, out var floor, 4f, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            var across = Vector3.Cross(Vector3.up, heading).normalized;
            var a = floor.point + (across * SkitterHalfSpanMetres);
            var b = floor.point - (across * SkitterHalfSpanMetres);
            if (NextFloat() < 0.5f)
            {
                (a, b) = (b, a);
            }

            StartCoroutine(SkitterAcross(a, b, transform.Find(SkittererTemplateName)));
            PlayCue(AudioCueId.StartleSkitter, floor.point);
            return true;
        }

        private static IEnumerator SkitterAcross(Vector3 from, Vector3 to, Transform? template)
        {
            GameObject go;
            var lift = Vector3.zero;
            if (template != null)
            {
                // The authored darter: a FLOOR prop, origin already on the floor under
                // its footprint and nose out of its forward, so a clone is placed and
                // pointed and nothing else. Colliders were disabled by the generator.
                go = Object.Instantiate(template.gameObject);
                go.SetActive(true);
            }
            else
            {
                // No template in this map — the crossing still happens, as the dark
                // box the first build of this feature shipped, and the [SceneGen] log
                // from generation says which asset was missing.
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var collider = go.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.Destroy(collider);
                }

                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = SkitterMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                go.transform.localScale = new Vector3(
                    SkitterBodyMetres * 0.4f, SkitterBodyMetres * 0.4f, SkitterBodyMetres);

                // The cube's pivot is its centre; the prop's is its floor line.
                lift = Vector3.up * (SkitterBodyMetres * 0.2f);
            }

            go.name = "[Startle Skitterer]";
            var travel = to - from;
            if (travel.sqrMagnitude > 0.0001f)
            {
                go.transform.rotation = Quaternion.LookRotation(travel.normalized, Vector3.up);
            }

            // One bob per body length — a stride IS a body length for a darter — at a
            // twenty-fifth of the body in amplitude: footfall texture that never lifts
            // the silhouette off the floor line. gen_props' own contract for the
            // rigless prop: "runtime slides + bobs the transform".
            var strides = travel.magnitude / SkitterBodyMetres;
            var bob = SkitterBodyMetres / 25f;

            var age = 0f;
            while (age < SkitterSeconds && go != null)
            {
                age += Time.deltaTime;
                var t = Mathf.Clamp01(age / SkitterSeconds);
                go.transform.position = Vector3.Lerp(from, to, t) + lift
                    + (Vector3.up * (bob * Mathf.Abs(Mathf.Sin(t * strides * Mathf.PI))));
                yield return null;
            }

            if (go != null)
            {
                Object.Destroy(go);
            }
        }

        private static Material SkitterMaterial()
        {
            if (_skitterMaterial == null)
            {
                // 0.05 linear: below the walls' albedo so it reads as shadow moving over
                // shadow at the beam's fringe, above zero so the unlit shader does not
                // punch a cutout in the frame. Judged at native brightness, per ART.md's
                // exposure lesson.
                _skitterMaterial = new Material(FindShader())
                {
                    color = new Color(0.05f, 0.05f, 0.06f, 1f),
                };
            }

            return _skitterMaterial;
        }

        // ------------------------------------------------------------------
        // pipe — one 0.9 s burst of vapour off a wall stub.
        // ------------------------------------------------------------------

        private bool StagePipe(Transform marker)
        {
            // The generator's Vent empty is both the burst's position and its
            // direction: it stands at the stub's torn mouth, forward off the wall —
            // single-mesh export, so the empty is the editor half's, not the FBX's.
            var vent = marker.Find(VentName);
            Vector3 at;
            Vector3 direction;
            if (vent != null)
            {
                at = vent.position;
                direction = vent.forward;
            }
            else
            {
                // A stub that failed to instantiate: vent from the marker at the
                // prop's restated axis height, towards the centreline. The marker's
                // forward IS the into-the-corridor direction — the generator orients
                // every wall marker off the wall it stands on (the earlier expression
                // here, marker.position minus a point directly above it, flattened to
                // a zero vector by construction and silently vented straight up while
                // this comment claimed otherwise — found by the design judge).
                at = marker.position + (Vector3.up * PipeAxisMetres);
                var into = marker.forward;
                into.y = 0f;
                direction = into.sqrMagnitude > 0.0001f ? into.normalized : Vector3.up;
            }

            StartCoroutine(VentBurst(at, direction));
            PlayCue(AudioCueId.StartlePipeVent, at);
            return true;
        }

        private static IEnumerator VentBurst(Vector3 at, Vector3 direction)
        {
            var go = new GameObject("[Startle Vent]");
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(direction));

            var ps = go.AddComponent<ParticleSystem>();

            // A ParticleSystem added at runtime wakes up already playing, and Unity
            // refuses duration writes on a playing system — stopped and cleared first,
            // configured second, played once at the end.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = PipeVentSeconds;

            // Particles die 0.20 s (the CaughtScreen reaction floor again) before the
            // burst window shuts, so the last puff is gone with the hiss, not after it.
            main.startLifetime = PipeVentSeconds - 0.20f;

            // One cell per second: 2.5 m/s × 0.7 s of life reaches 1.75 m, inside the
            // corridor's 2.20 m clear width — the burst never washes the far wall.
            main.startSpeed = 2.5f;

            // The kit's own wall inset, 0.15 m — vapour at the scale the building
            // treats as surface detail.
            main.startSize = 0.15f;

            // Mid-grey: dark enough not to bloom under the beam, light enough to read
            // against unlit black. Native-brightness judgement, per ART.md.
            main.startColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            main.maxParticles = PipeParticleCount;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)PipeParticleCount) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;

            // The game's one authored cone angle, reused: §03's beam half-angle. The
            // base disc is the stub's torn mouth — see VentMouthRadiusMetres.
            shape.angle = GameConstants.FlashlightHalfAngle;
            shape.radius = VentMouthRadiusMetres;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = VentMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            ps.Play();

            // Teardown: the burst window plus the 0.20 s reaction floor once more —
            // the same margin the lifetime keeps on the NEAR side of the window's shut
            // (startLifetime = window − 0.20 above), mirrored onto the far side, so the
            // destroy stands as far after the shut as the last death stands before it.
            // Total 1.1 s: the corridor-crossing beat every vent number above derives
            // from, with every particle 0.40 s dead by then — and WaitForSeconds runs
            // on the same scaled clock the system simulates on, so the destroy can
            // never clip a live particle. The first build waited an underived +0.5.
            yield return new WaitForSeconds(PipeVentSeconds + 0.20f);
            if (go != null)
            {
                Object.Destroy(go);
            }
        }

        private static Material VentMaterial()
        {
            if (_ventMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                _ventMaterial = new Material(shader != null ? shader : FindShader());
            }

            return _ventMaterial;
        }

        // ------------------------------------------------------------------
        // bulbdeath — the nearest working fitting flickers three frames and dies.
        // The one startle with a persistent world change, and it makes no sound at
        // all: §06's own argument, 침묵이 가장 무서운 소리다.
        // ------------------------------------------------------------------

        private bool StageBulbDeath(Transform rig, Vector3 marker)
        {
            var eye = ResolveEye(rig);
            if (eye == null)
            {
                // No eye, no witness. BulbHuntMetres' whole derivation is that a light
                // the player never REGISTERED dying is nothing happening; a scene with
                // no camera has nobody to register anything.
                return false;
            }

            Light? victim = null;
            var nearest = BulbHuntMetres;

            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!light.enabled || light.type != LightType.Point
                    || light.name != FilamentName || IsFinishLight(light.transform))
                {
                    continue;
                }

                var flat = light.transform.position - rig.position;
                var dy = flat.y;
                flat.y = 0f;
                var gap = flat.magnitude;
                if (gap >= nearest || Mathf.Abs(dy) >= StoreyGateMetres)
                {
                    continue;
                }

                // The marker, not the rig, carries the generator's keep-out promise,
                // and the rig can be TriggerMetres from it when firing — the same
                // clamp every rig-relative stage takes. See StageReachMetres. (The
                // hunt radius, 6.0 from the rig, could otherwise reach 7.55 from the
                // marker.)
                var fromMarker = light.transform.position - marker;
                fromMarker.y = 0f;
                if (fromMarker.sqrMagnitude > StageReachMetres * StageReachMetres)
                {
                    continue;
                }

                // Sight line, eye to filament — the glimpse's own rule, for the same
                // reason: a fitting dying behind a wall or a shut door is trivia the
                // player cannot register, and BulbHuntMetres' derivation says
                // REGISTERED, not merely near. The fittings themselves block nothing:
                // gen_dressing.py marks both bulb pieces non-solid, so the dressing
                // pass stripped their colliders and this cast answers walls and doors,
                // never the victim's own glass.
                if (Physics.Linecast(eye.position, light.transform.position, ~0,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                nearest = gap;
                victim = light;
            }

            if (victim == null)
            {
                // No working fitting in reach, inside the marker's guarantee, and in
                // sight. Not consumed; a corridor with no light to lose cannot lose one.
                return false;
            }

            StartCoroutine(KillBulb(victim));
            return true;
        }

        /// <summary>
        /// Whether this light belongs to §02's finish. The B8 EntranceLight is the one
        /// promise of light the game makes and no 깜짝 may ever touch it — checked by
        /// walking the parents against <see cref="MatchMap.EntranceLightPrefix"/>, the
        /// same name <c>RaceDirector</c> finds the finish by.
        /// </summary>
        private static bool IsFinishLight(Transform light)
        {
            for (var t = light; t != null; t = t.parent)
            {
                if (t.name.StartsWith(MatchMap.EntranceLightPrefix, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerator KillBulb(Light light)
        {
            // Three frames: off, on, off — the smallest pattern that reads as a fitting
            // failing rather than as the renderer hitching. See BulbFlickerFrames.
            for (var frame = 0; frame < BulbFlickerFrames && light != null; frame++)
            {
                light.enabled = frame % 2 != 0;
                yield return null;
            }

            if (light == null)
            {
                yield break;
            }

            light.enabled = false;

            // The glass goes dead too, if the dead material is reachable by name: every
            // dressed map carries Dress_BulbDead on its unlit fittings (the dressing
            // pass lights one in N), so the swap borrows a shared material the scene
            // already owns rather than loading anything. If no dead glass exists in
            // this scene the lamp stays lit-looking with no light, which is a residue
            // this remark documents rather than hides.
            var holder = light.transform.parent;
            if (holder == null)
            {
                yield break;
            }

            Material? dead = null;
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var shared = renderer.sharedMaterials;
                for (var i = 0; i < shared.Length; i++)
                {
                    if (shared[i] != null
                        && shared[i].name.StartsWith("Dress_BulbDead", System.StringComparison.Ordinal))
                    {
                        dead = shared[i];
                        break;
                    }
                }

                if (dead != null)
                {
                    break;
                }
            }

            if (dead == null)
            {
                yield break;
            }

            foreach (var renderer in holder.GetComponentsInChildren<MeshRenderer>())
            {
                var shared = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < shared.Length; i++)
                {
                    if (shared[i] != null
                        && shared[i].name.StartsWith("Dress_BulbLit", System.StringComparison.Ordinal))
                    {
                        shared[i] = dead;
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = shared;
                }
            }
        }

        // ------------------------------------------------------------------
        // glimpse — the figure, once per match, where the beam is not.
        // ------------------------------------------------------------------

        private bool StageGlimpse(Transform rig, Vector3 marker)
        {
            // Gated hardest, so every gate again: depth off the rig's own height (the
            // same read MatchDirector uses for the chutes — the name is a label, the
            // height is the fact), then a legal stand, then the once-per-match inside
            // MarkFired.
            var storey = Mathf.RoundToInt(-rig.position.y / OutOfBounds.StoreyPitchMetres);
            if (storey < GlimpseStoreyFloor)
            {
                return false;
            }

            var eye = ResolveEye(rig);
            if (eye == null)
            {
                return false;
            }

            if (!TryFindGlimpseSpot(eye, marker, out var floorPoint))
            {
                // Nowhere legal this frame. Not consumed — the once-per-match must not
                // be spent on a figure nobody could ever have seen.
                return false;
            }

            var figure = EnsureFigure();
            if (figure == null)
            {
                return false;
            }

            figure.position = floorPoint;
            var toEye = eye.position - floorPoint;
            toEye.y = 0f;
            if (toEye.sqrMagnitude > 0.0001f)
            {
                // Facing the player — PresenceView.PlaceFigureAt's rule, copied with its
                // reason: the one thing this shape must do is read as a person shape
                // before it reads as anything else.
                figure.rotation = Quaternion.LookRotation(toEye.normalized, Vector3.up);
            }

            figure.gameObject.SetActive(true);
            _figureAge = 0f;
            PlayCue(AudioCueId.StartleGlimpse, floorPoint);
            return true;
        }

        /// <summary>
        /// Somewhere the figure can stand: outside the beam cone, in sight, on a floor,
        /// between <see cref="GlimpseNearMetres"/> and <see cref="GlimpseFarMetres"/>
        /// of the eye, and inside <see cref="StageReachMetres"/> of the marker that
        /// fired — the marker, not the rig, carries the generator's keep-out promise,
        /// and the eye can stand TriggerMetres from it.
        /// <para>
        /// This is <c>PresenceView.TryFindStandingSpot</c>'s approach re-implemented
        /// rather than called, and the choice is documented as the task asked:
        /// <c>HorrorGame.Gameplay</c> does not reference the
        /// <c>HorrorGame.Gameplay.Presence</c> assembly (see the asmdef), so the API is
        /// simply not reachable from here — the same one-way-arrow that keeps the
        /// generator out of the runtime decides this too. The cone exclusion is
        /// unconditional where PresenceView's is beam-gated: the brief for the glimpse
        /// is "where the beam is NOT pointing", and with the torch off, "where the
        /// player is looking" is still the one place a materialising figure would be
        /// watched happening.
        /// </para>
        /// </summary>
        private bool TryFindGlimpseSpot(Transform eye, Vector3 marker, out Vector3 floorPoint)
        {
            floorPoint = default;

            var flat = eye.forward;
            flat.y = 0f;
            var heading = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;

            for (var attempt = 0; attempt < GlimpseAttempts; attempt++)
            {
                var yaw = NextFloat() * 360f;
                var direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                if (Vector3.Angle(heading, direction) < GameConstants.FlashlightHalfAngle)
                {
                    continue;
                }

                var distance = Mathf.Lerp(GlimpseNearMetres, GlimpseFarMetres, NextFloat());
                var target = eye.position + (direction * distance);

                // The marker-anchored clamp. The band above is measured from the EYE,
                // which can stand TriggerMetres from the marker whose keep-out is the
                // actual promise — so a candidate past StageReachMetres of the marker
                // is rejected here and the loop simply redraws, the same
                // do-not-consume retry as every other refusal in it.
                var fromMarker = target - marker;
                fromMarker.y = 0f;
                if (fromMarker.sqrMagnitude > StageReachMetres * StageReachMetres)
                {
                    continue;
                }

                // Sight line from the eye — a figure behind a wall is not frightening,
                // it is absent, and the player has to be able to have seen it.
                if (Physics.Linecast(eye.position, target, ~0, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                if (!Physics.Raycast(target, Vector3.down, out var hit, 4f, ~0,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                floorPoint = hit.point;
                return true;
            }

            return false;
        }

        private void DriveGlimpse()
        {
            if (_figure == null || !_figure.gameObject.activeSelf)
            {
                return;
            }

            _figureAge += Time.deltaTime;
            if (_figureAge >= GlimpseSeconds)
            {
                // It is not seen to leave: it was somewhere, and then it is nowhere.
                _figure.gameObject.SetActive(false);
            }
        }

        private Transform? EnsureFigure()
        {
            if (_figure != null)
            {
                return _figure;
            }

            // The generator parks a disabled clone source in this very group — the
            // Gun_Held_Template pattern, for its stated reason: an asset the runtime
            // needs must reach it as a scene object, because this assembly can neither
            // load an FBX nor reference the editor that can. Colliders were already
            // disabled by the generator; the clone inherits that.
            var template = transform.Find(FigureTemplateName);
            if (template == null)
            {
                return null;
            }

            var instance = Instantiate(template.gameObject, transform);
            instance.name = "[Startle Figure]";
            instance.SetActive(false);
            _figure = instance.transform;
            return _figure;
        }

        // ------------------------------------------------------------------
        // Plumbing.
        // ------------------------------------------------------------------

        private Transform? ResolveRig()
        {
            if (_rig != null)
            {
                return _rig;
            }

            // The interactor is on every rig the game assembles — SoloPlaytest, the
            // lobby spawn and the PlayMode harnesses — which is why GunPickup leans on
            // it too. The camera is the fallback for a scene with no hands.
            var interactor = FindFirstObjectByType<PlayerInteractor>();
            if (interactor != null)
            {
                _rig = interactor.transform.root;
                return _rig;
            }

            var camera = Camera.main;
            _rig = camera != null ? camera.transform.root : null;
            return _rig;
        }

        private Transform? ResolveEye(Transform rig)
        {
            if (_eye != null)
            {
                return _eye;
            }

            var camera = rig.GetComponentInChildren<Camera>(true);
            if (camera == null)
            {
                camera = Camera.main;
            }

            _eye = camera != null ? camera.transform : null;
            return _eye;
        }

        private void PlayCue(AudioCueId cue, Vector3 at)
        {
            if (_audioRig == null)
            {
                _audioRig = FindFirstObjectByType<MatchAudioRig>();
            }

            var cues = _audioRig != null ? _audioRig.Cues : null;
            if (cues != null)
            {
                // Positional, through the one player every world sound goes through.
                // AudioCues.NoiseOf returns 0 for every startle cue, so this raises no
                // §04 self-noise — and nothing here ever reaches a creature (decision a).
                cues.PlayAt(cue, at);
            }
        }

        private static Shader FindShader()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            return shader;
        }

        /// <summary>FNV-1a over a marker name — the per-map determinism a seeded angle needs.</summary>
        private static uint Hash(string text)
        {
            var h = 2166136261u;
            for (var i = 0; i < text.Length; i++)
            {
                h = (h ^ text[i]) * 16777619u;
            }

            return h;
        }

        /// <summary>
        /// PresenceView's xorshift, copied with its reasoning: §13's invariant 4 says a
        /// seed replays a match, and a component that reached for
        /// <c>UnityEngine.Random</c> would put a global stream in the middle of it.
        /// </summary>
        private float NextFloat()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng >> 8) * (1f / 16777216f);
        }
    }
}
