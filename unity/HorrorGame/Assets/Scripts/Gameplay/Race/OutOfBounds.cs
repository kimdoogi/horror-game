#nullable enable

using HorrorGame.Core;
using UnityEngine;
using UnityEngine.AI;

namespace HorrorGame.Gameplay.Race
{
    /// <summary>
    /// Why a runner was put back. Reported so the log names the failure rather than the
    /// symptom — the two get here down different paths and mean different things about the
    /// building.
    /// </summary>
    public enum OutOfBoundsReason
    {
        /// <summary>Nothing was wrong.</summary>
        None,

        /// <summary>
        /// No baked floor within <see cref="OutOfBounds.FloorSearchRadiusMetres"/> of the
        /// runner's feet, for long enough that it is not a sampling artefact. This is a
        /// seam in the shell: they walked, or were pushed, through something.
        /// </summary>
        NoFloorUnderfoot,

        /// <summary>
        /// The runner is a whole storey below the last place they were provably standing.
        /// This is a fall — they are between the slabs and accelerating, and waiting out
        /// the ordinary timer would let them do it for twenty metres.
        /// </summary>
        BelowTheBuilding,
    }

    /// <summary>
    /// §01's safety net: notices a runner who is outside the building or falling out of it,
    /// and says where to put them back.
    /// <para>
    /// <b>Why a recovery exists at all when a shell is being built.</b> §02 is a race to the
    /// MIDDLE and the whole game is the maze that makes reaching it hard. A runner outside
    /// the walls crosses the footprint in a straight line, ignores every gate, every door
    /// and every creature, and wins — so an escapable map is not a cosmetic defect, it is
    /// the absence of a game. A physical boundary is the right primary fix and it will not
    /// be perfect: a <c>CharacterController</c> with <c>GameConstants.RunnerSprintSpeed</c>
    /// under it tunnels through thin colliders, depenetration has seams, and twenty players
    /// will find shapes nobody modelled. Every game that can be escaped ships a recovery.
    /// </para>
    /// <para>
    /// <b>"Outside" is asked of the NavMesh, because the NavMesh is the only honest answer
    /// this project already has.</b> <c>NavMeshWorldProbe.TrySnap</c> — the call
    /// <c>MonsterBrain</c> reaches the world through — is
    /// <c>NavMesh.SamplePosition(point, out hit, radius, NavMesh.AllAreas)</c>, and
    /// <see cref="SampleFloor"/> is the same query. It is not a second definition of where
    /// the world is: it is the same one, asked at the runner's feet. Only the radius
    /// differs, and it has to — the probe's is the creature's own body height (2.3 m), sized
    /// to snap a creature onto its floor on a map that used to have one, and on this tower
    /// the storeys are <see cref="StoreyPitchMetres"/> apart so a 2.3 m radius answers a
    /// question about B5 with the floor of B4. <c>DescentPlaythroughTests.NavSnapMetres</c>
    /// names the same trap and for the same reason.
    /// </para>
    /// <para>
    /// <b>Nothing here goes into the scene.</b> This class creates no <c>GameObject</c>,
    /// adds no component, instantiates nothing and holds no collider or renderer, so there
    /// is nothing for <c>NavMeshSurface</c> to collect and the bake cannot change:
    /// <c>MapSceneBuilder.KeepOutOfNavMeshBake</c> exists for objects that DO exist in the
    /// scene, and the strongest version of it is not to add one. The measurement that would
    /// refute this is the audit that already runs — 220 markers, 3482 pairs, 8 islands — and
    /// it reads a baked asset that this file has no way to touch.
    /// </para>
    /// <para>
    /// <b>The rule takes numbers, not a world.</b> <see cref="Tick"/> is handed a
    /// <see cref="FloorSample"/> and returns a verdict, the way ARCHITECTURE §3 shapes every
    /// stateful system in this project: the engine query is one static call the host makes,
    /// so the timers, the anchor and the grace can be driven by a test with no scene, no
    /// bake and no player in it.
    /// </para>
    /// </summary>
    public sealed class OutOfBounds
    {
        /// <summary>
        /// How far from a runner's feet the guard will look for baked floor before calling
        /// them outside, metres.
        /// <para>
        /// Bounded from below by how far a legally-standing runner can be from the mesh.
        /// <c>ProjectSettings/NavMeshAreas.asset</c> bakes agent 0 at <c>agentRadius 0.5</c>,
        /// so Recast erodes the walkable surface 0.50 m back from every wall face, while the
        /// player's <c>CharacterController</c> — radius 0.30, skin 0.08 — can put its centre
        /// 0.38 m from that face. A runner jammed into the corner of a dead end is therefore
        /// √2 × (0.50 − 0.38) = 0.17 m off the mesh while doing nothing wrong. 1.5 m is
        /// nearly nine times that.
        /// </para>
        /// <para>
        /// Bounded from above by <see cref="StoreyPitchMetres"/> ÷ 2 = 1.875 m: every storey
        /// of this tower sits directly on top of the last, so a radius past half the pitch
        /// would answer "is there floor under me on B5" with the floor of B4 and the guard
        /// would go quiet exactly where it is needed. 1.5 m keeps 0.375 m of that margin.
        /// </para>
        /// <para>
        /// Erring generous is deliberate. A false negative here costs a few tenths of a
        /// second before <see cref="OutsideGraceSeconds"/> would have fired anyway, and the
        /// falling case does not depend on this radius at all — see
        /// <see cref="OutOfBoundsReason.BelowTheBuilding"/>. A false POSITIVE teleports a
        /// runner who is standing still in a race decided by seconds.
        /// </para>
        /// </summary>
        public const float FloorSearchRadiusMetres = 1.5f;

        /// <summary>
        /// How close the feet must be to baked floor for that position to be banked as an
        /// honest footprint, metres.
        /// <para>
        /// Strictly tighter than <see cref="FloorSearchRadiusMetres"/>, and the gap between
        /// the two is what stops a recovery loop: a runner put back at an anchor banked at
        /// 1.4 m from the mesh would still be outside on the very next step and would be put
        /// back again, forever. At 0.5 m an anchor is at least a metre inside the line that
        /// flags anybody.
        /// </para>
        /// <para>
        /// It is also what stops the anchor being banked on the WRONG SIDE of a wall.
        /// <c>NavMesh.SamplePosition</c> returns the nearest polygon, and the nearest polygon
        /// to a runner standing on top of a corridor wall is whichever corridor is closer —
        /// which may be one they have never legally been in. Half a metre is 3× the 0.17 m
        /// worst case above and well under the 1.1 m half-width of a §12 corridor, so the
        /// point banked is floor they could have stepped onto.
        /// </para>
        /// </summary>
        public const float AnchorReachMetres = 0.5f;

        /// <summary>
        /// How long a runner may be off the map before being put back, seconds. 100 fixed
        /// steps at <see cref="GameConstants.FixedStep"/>.
        /// <para>
        /// <b>Chosen long on purpose, because the two errors are not symmetric.</b> Waiting
        /// costs the cheat nothing: the recovery returns them to their own last footprint,
        /// so every metre walked outside is forfeited whether the guard fires at 0.2 s or at
        /// 2.0 s. All the timer buys them is their own wasted time. Firing early, on a
        /// pocket the bake happened to erase, snatches a runner who is standing on real
        /// floor — in a race, mid-descent. When one error is free and the other is a stolen
        /// place, wait.
        /// </para>
        /// <para>
        /// The number it must beat: at <see cref="GameConstants.RunnerSprintSpeed"/> 5.6 m/s
        /// a runner crosses 11.2 m in two seconds, 4.5 cells of §12's 2.5 m grid — and lands
        /// back where they started regardless.
        /// </para>
        /// </summary>
        public const float OutsideGraceSeconds = 2.0f;

        /// <summary>
        /// Metres between two floors. <c>MapKitCatalogue.StoreyMetres</c> is the authority
        /// and it lives in an editor assembly this one cannot reference, so it is quoted here
        /// in the same shape and for the same reason <c>MatchDirector.AttachChutes</c> and
        /// <c>DescentPlaythroughTests.StoreyPitchMetres</c> quote it. Measured in the
        /// artefact: the 착지 markers sit at 0, −3.75 … −26.25.
        /// </summary>
        public const float StoreyPitchMetres = 3.75f;

        /// <summary>
        /// How long the guard stays silent after a 투하구 swallows a runner, seconds.
        /// <para>
        /// <b>A runner in a 투하구 IS falling, legitimately, and this is the whole of the
        /// answer to telling that apart from falling out of the world.</b> Not a heuristic
        /// about speed or height: <c>MatchDirector.CheckChutes</c> knows the exact step a
        /// chute fires on, so it says so — <see cref="Descended"/>. Time and distance would
        /// both have had to guess; the chute does not have to.
        /// </para>
        /// <para>
        /// Arithmetic rather than a tuned value, the way <c>GameConstants.JumpAirtimeSeconds</c>
        /// is: a free fall of <see cref="Chute.DropHeightMetres"/> 1.226 m at
        /// <see cref="GameConstants.JumpGravity"/> 9.81 m/s² takes √(2 × 1.226 ÷ 9.81) =
        /// 0.500 s, doubled for the controller's own settle onto a landing that may not be
        /// perfectly flat. 1.000 s.
        /// <para>
        /// It read 3.0 m / 0.782 s / 1.564 s until 2026-08-03, when Chute.DropHeightMetres
        /// was derived from §01's own half second instead of typed in. The VALUE here has
        /// always followed the constant; only this prose was ever a copy, which is exactly
        /// how a comment ends up describing a build nobody is running.
        /// </para>
        /// </para>
        /// <para>
        /// <b>The cap almost never matters, because the grace ends early.</b> The first step
        /// on which the runner is within <see cref="AnchorReachMetres"/> of floor clears it —
        /// so this only runs to its end when the landing itself is broken, and in that case
        /// spending an extra 0.8 s before saying so is not a cost anybody can measure.
        /// </para>
        /// </summary>
        public static readonly float ChuteGraceSeconds =
            2f * Mathf.Sqrt(2f * Chute.DropHeightMetres / GameConstants.JumpGravity);

        private Vector3 _anchor;
        private Vector3 _recovery;
        private bool _hasAnchor;
        private float _secondsOutside;
        private float _chuteGrace;
        private int _recoveries;
        private OutOfBoundsReason _reason;

        /// <summary>
        /// What the bake says is under a point. <see cref="SampleFloor"/> fills it in; a
        /// test fills it in by hand, which is the only reason it is a value and not a bool.
        /// </summary>
        public readonly struct FloorSample
        {
            /// <summary>Builds a sample.</summary>
            /// <param name="found">Whether any floor was within the search radius.</param>
            /// <param name="onFloor">The point the bake calls floor. The queried point when nothing was found.</param>
            /// <param name="gapMetres">Distance from the queried point to it. +∞ when nothing was found.</param>
            public FloorSample(bool found, Vector3 onFloor, float gapMetres)
            {
                Found = found;
                OnFloor = onFloor;
                GapMetres = gapMetres;
            }

            /// <summary>True when the bake has floor within <see cref="FloorSearchRadiusMetres"/>.</summary>
            public bool Found { get; }

            /// <summary>
            /// The nearest point on the NavMesh. Banked as the anchor rather than the feet
            /// themselves, because the bake is eroded 0.50 m from every wall — so a point on
            /// it is a point a 0.30 m capsule cannot be re-inserted inside geometry at.
            /// </summary>
            public Vector3 OnFloor { get; }

            /// <summary>How far the feet were from it, metres.</summary>
            public float GapMetres { get; }
        }

        /// <summary>How many times this match has had to put the local runner back.</summary>
        public int Recoveries
        {
            get { return _recoveries; }
        }

        /// <summary>Why the last <see cref="Tick"/> returned true. <see cref="OutOfBoundsReason.None"/> otherwise.</summary>
        public OutOfBoundsReason Reason
        {
            get { return _reason; }
        }

        /// <summary>Where to put the runner back. Only meaningful on a step <see cref="Tick"/> returned true.</summary>
        public Vector3 Recovery
        {
            get { return _recovery; }
        }

        /// <summary>The last place the runner was provably standing on this building.</summary>
        public Vector3 Anchor
        {
            get { return _anchor; }
        }

        /// <summary>
        /// Whether the guard has ever seen the runner stand somewhere legal.
        /// <para>
        /// <b>This is the property that makes the guard safe to run everywhere.</b> Until it
        /// is true there is nowhere to put anybody back, so <see cref="Tick"/> cannot fire —
        /// which means a scene with no bake at all (a rig assembled by a test, a map still
        /// being generated) gets a guard that watches, never acts, and cannot break a
        /// measurement it does not understand.
        /// </para>
        /// </summary>
        public bool HasAnchor
        {
            get { return _hasAnchor; }
        }

        /// <summary>How long the runner has been off the map, seconds. 0 while they are on it.</summary>
        public float SecondsOutside
        {
            get { return _secondsOutside; }
        }

        /// <summary>True while §01's own falling is in progress and the guard is holding its tongue.</summary>
        public bool InChuteDescent
        {
            get { return _chuteGrace > 0f; }
        }

        /// <summary>
        /// Forgets everything. Called at the start of a match: the anchor is a footprint in a
        /// building, and a footprint from the last building would put a runner back inside a
        /// map that no longer exists.
        /// </summary>
        public void Reset()
        {
            _anchor = Vector3.zero;
            _recovery = Vector3.zero;
            _hasAnchor = false;
            _secondsOutside = 0f;
            _chuteGrace = 0f;
            _recoveries = 0;
            _reason = OutOfBoundsReason.None;
        }

        /// <summary>
        /// Called on a step the runner is not in play — a ghost, §01's 지상, no rig. Clears
        /// the timer without touching the anchor, so a seat that comes back does not arrive
        /// holding a stopwatch that was already 1.9 s into a count nobody was watching.
        /// </summary>
        public void Idle()
        {
            _secondsOutside = 0f;
            _reason = OutOfBoundsReason.None;
        }

        /// <summary>
        /// A 투하구 has taken the runner. Arms <see cref="ChuteGraceSeconds"/> and moves the
        /// anchor to the landing.
        /// <para>
        /// <b>The anchor moves as the chute fires, and that ordering is the whole of it.</b>
        /// For the 0.78 s of the drop there is no floor within reach of the runner and no
        /// sample can bank one; if the anchor were still the middle of the storey ABOVE, a
        /// guard that fired mid-fall would put a runner back up a floor — undoing a descent
        /// §02 has already recorded. The landing is a known-good point by the map's own
        /// construction: <c>DescentMap.HangChutes</c> picks it off the rim band's own cell
        /// list, and <c>DescentPlaythroughTests</c> asserts every one of the seven lands one
        /// storey down and outside <c>RadialStorey</c>'s d = 8 wall.
        /// </para>
        /// </summary>
        /// <param name="landing">Where the chute set the runner down — <c>Chute.Landing</c>, not <c>DropPoint</c>.</param>
        public void Descended(Vector3 landing)
        {
            _anchor = landing;
            _hasAnchor = true;
            _secondsOutside = 0f;
            _chuteGrace = ChuteGraceSeconds;
            _reason = OutOfBoundsReason.None;
        }

        /// <summary>
        /// One fixed step of watching. Returns true when the runner should be put back, and
        /// <see cref="Recovery"/> says where.
        /// <para>
        /// <b>Where they go back to is a race decision, not a convenience.</b> Three places
        /// were available and only one of them is neutral:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <b>The rim of their storey.</b> A real penalty — 118 m of maze, measured by §12-D
        /// on this generator — and therefore a real disaster the first time the guard is
        /// wrong. It punishes a physics seam, and physics seams are the reason this file
        /// exists.
        /// </description></item>
        /// <item><description>
        /// <b>The nearest point on the mesh.</b> The one option that is definitely wrong:
        /// step out at the rim, sprint 25 m across the footprint, and the nearest legal point
        /// is beside the 투하구. That is the exploit, granted by the guard meant to stop it.
        /// </description></item>
        /// <item><description>
        /// <b>The last honest footprint.</b> Chosen. It hands back exactly zero metres — the
        /// point returned is one the runner had already legitimately reached — so §01's
        /// "a runner who cheats the maze should not profit" is satisfied in full, while a
        /// false fire costs a metre and a couple of seconds instead of a storey. §01 asks
        /// that a cheat not PROFIT; deterrence is the shell's job, and this is the net.
        /// </description></item>
        /// </list>
        /// </summary>
        /// <param name="stepSeconds"><see cref="GameConstants.FixedStep"/>. The host drives this at 50 Hz.</param>
        /// <param name="feet">The runner's root, which is their feet — the controller's centre is (0, 0.875, 0).</param>
        /// <param name="floor">What the bake has under them. <see cref="SampleFloor"/>.</param>
        public bool Tick(float stepSeconds, Vector3 feet, FloorSample floor)
        {
            _reason = OutOfBoundsReason.None;

            if (_chuteGrace > 0f)
            {
                _chuteGrace -= stepSeconds;
            }

            // ── Standing on it. Bank the footprint and forget everything else ──
            // The mesh point rather than the feet: see FloorSample.OnFloor.
            if (floor.Found && floor.GapMetres <= AnchorReachMetres)
            {
                _anchor = floor.OnFloor;
                _hasAnchor = true;
                _secondsOutside = 0f;

                // A descent that reached a floor is over, whatever the clock says. This is
                // what makes ChuteGraceSeconds a ceiling rather than a window: the guard is
                // live again the instant the runner has somewhere to be put back to.
                _chuteGrace = 0f;
                return false;
            }

            // ── Nothing to put them back to ───────────────────────────────────
            if (!_hasAnchor)
            {
                _secondsOutside = 0f;
                return false;
            }

            // ── §01's own falling ─────────────────────────────────────────────
            if (_chuteGrace > 0f)
            {
                _secondsOutside = 0f;
                return false;
            }

            // ── Under the building ────────────────────────────────────────────
            // A storey is flat — RadialStorey lays its corridor tiles on one plane — so the
            // only legitimate way to be a whole StoreyPitchMetres below the floor you were
            // last standing on is a 투하구, and a 투하구 moves the anchor as it fires. This
            // is checked before the timer because a fall does not need waiting out: at 9.81
            // m/s² the runner passes 3.75 m in 0.87 s and 20 m in two seconds.
            if (_anchor.y - feet.y > StoreyPitchMetres)
            {
                _reason = OutOfBoundsReason.BelowTheBuilding;
                return PutBack();
            }

            // ── No floor anywhere near ────────────────────────────────────────
            if (!floor.Found)
            {
                _secondsOutside += stepSeconds;
                if (_secondsOutside < OutsideGraceSeconds)
                {
                    return false;
                }

                _reason = OutOfBoundsReason.NoFloorUnderfoot;
                return PutBack();
            }

            // ── Floor is near but not underfoot ───────────────────────────────
            // Between AnchorReachMetres and FloorSearchRadiusMetres: a lip, a crate, the
            // last metre of a fall, a runner pressed into a corner of a dead end. Not
            // banked, and not counted either — the timer is FROZEN rather than cleared, so
            // somebody bobbing in and out of the shell around the mesh still accumulates
            // instead of resetting the count on every other step.
            return false;
        }

        /// <summary>
        /// Asks the bake what is under a point.
        /// <para>
        /// <c>NavMesh.AllAreas</c> because the question is "is there floor here", not "may a
        /// creature walk here" — an area a §06 agent is forbidden is still ground a runner is
        /// standing on. One query per fixed step, 50 a second, next to the eight creatures'
        /// <c>CalculatePath</c> calls in the same step; the radius is small enough that
        /// Unity's warning about wide samples does not apply.
        /// </para>
        /// </summary>
        /// <param name="feet">Where to look. The runner's root transform.</param>
        public static FloorSample SampleFloor(Vector3 feet)
        {
            if (NavMesh.SamplePosition(feet, out var hit, FloorSearchRadiusMetres, NavMesh.AllAreas))
            {
                return new FloorSample(true, hit.position, Vector3.Distance(feet, hit.position));
            }

            return new FloorSample(false, feet, float.PositiveInfinity);
        }

        private bool PutBack()
        {
            _recovery = _anchor;
            _recoveries++;
            _secondsOutside = 0f;

            // Cleared so a recovery that lands in a broken 착지 is measured as a fresh fall
            // rather than being swallowed by the grace it started with. The runner then
            // loops — recovered, falls, recovered — which is loud, appears in the log with a
            // rising count, and is the correct behaviour: a map whose landing is in the void
            // should be impossible to miss, not survivable in silence.
            _chuteGrace = 0f;
            return true;
        }
    }
}
