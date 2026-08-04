#nullable enable

using System;
using HorrorGame.Core;
using HorrorGame.Gameplay.Player;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HorrorGame.Net.PlayerBridge
{
    /// <summary>
    /// Animates a runner somebody else owns, from the only thing this machine has about
    /// them: §05's 위치 row arriving at <see cref="GameConstants.NetworkSendRate"/>.
    /// <para>
    /// <b>The defect this closes.</b> <see cref="NetRunnerBody"/> gave every remote runner
    /// the shipped model and its own summary admitted "The copy is not animated" — so in a
    /// twenty-runner race the thing a player looks at most was a mannequin gliding through
    /// the maze in the bind pose. The local rig has driven nine clips off <c>Runner.fbx</c>
    /// since <c>PlayerAnimatorDriver</c> existed; nothing drove them on anybody else.
    /// </para>
    /// <para>
    /// <b>One opinion about when somebody is running.</b> The pose is chosen by calling
    /// <see cref="PlayerAnimatorDriver.Resolve"/> and the clip is sped up by
    /// <see cref="PlayerAnimatorDriver.ReferenceSpeed"/> — the driver's own two public,
    /// pure functions, not a second copy of them. §06's 걷기/달리기 crossover therefore
    /// moves for a remote body the instant it moves for a local one, and a remote runner
    /// cannot be seen doing a different verb from the one their own screen shows. The only
    /// thing this class decides is <em>what number to hand Resolve</em>, because that is
    /// the one input it has to reconstruct rather than read.
    /// </para>
    /// <para>
    /// <b>Where the graph comes from, and why not the driver itself.</b>
    /// <c>PlayerAnimatorDriver.Update</c> reads <c>PlayerMotor.GroundSpeed</c> and has no
    /// seam for a speed from anywhere else, and a remote body has no motor — putting one
    /// on it would mean twenty extra <c>CharacterController</c>s stepping physics for
    /// bodies whose positions are already decided by the host. So the decisions are shared
    /// and only the plumbing is separate: a <c>PlayableGraph</c> of the same shape, fed by
    /// the wire instead of by legs. The clips are not re-loaded either — they are read off
    /// the local rig's own driver through <see cref="PlayerAnimatorDriver.ClipFor"/> (see
    /// <see cref="Bind"/>), for exactly the reason <see cref="NetRunnerBody"/> copies the
    /// live rig rather than a prefab: <c>Runner.fbx</c>'s clips are reachable only through
    /// <c>AssetDatabase</c>, which does not exist in a player build.
    /// </para>
    /// <para>
    /// <b>The send rate is not the frame rate.</b> §05's position arrives about thirty
    /// times a second and the body is drawn every frame, so the ground speed is
    /// differentiated once per <em>arrival</em> and then filtered; see
    /// <see cref="SpeedSmoothingSeconds"/> for the arithmetic and for what a wrong value
    /// costs. Differentiating per frame instead would read zero on every frame no snapshot
    /// landed on, and a runner alternating Idle and Run thirty times a second is the most
    /// broken thing a body can do.
    /// </para>
    /// <para>
    /// <b>A teleported runner does not sprint.</b> §01 drops a runner through a 투하구 onto
    /// the rim of the floor below and §06 sends a caught one back to their B1 cell, and both
    /// move a body tens of metres between two snapshots. Differentiated naively that is
    /// hundreds of metres per second and the body plays a sprint cycle across the map. See
    /// <see cref="TeleportSlackSeconds"/> for how such a step is told apart from running
    /// without any new byte on the wire.
    /// </para>
    /// <para>
    /// <b>Room for the gun.</b> The pose vocabulary is
    /// <see cref="PlayerAnimationState"/> and every table here is sized from it
    /// (<see cref="StateCount"/>), so GunIdle/GunWalk added to that enum grow this graph
    /// and the local driver's together with no edit here. What a held gun would still need
    /// is a sixth §05 row — a <c>bool</c> on <see cref="NetPlayer"/> shaped exactly like
    /// 손전등, since a gun in somebody's hands is information about them in the same way a
    /// beam is — and one more argument on <see cref="PlayerAnimatorDriver.Resolve"/>, which
    /// has taken and lost such arguments before (<c>carryingObjective</c>,
    /// <c>visiblyBurdened</c>). The single line to change here is
    /// <see cref="ChoosePose"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(60)]
    [AddComponentMenu("HorrorGame/Net/Net Runner Animation")]
    public sealed class NetRunnerAnimation : MonoBehaviour
    {
        /// <summary>
        /// One past the highest <see cref="PlayerAnimationState"/>, which is the width of
        /// the mixer.
        /// <para>
        /// Computed from the enum rather than written down, because the enum is
        /// deliberately sparse — 5, 6 and 7 were the three carry poses and
        /// <c>Death = 8</c> was left where it was so serialised scenes and mixer indices
        /// did not silently re-point. A hard-coded 9 here would have to be found and
        /// changed by whoever adds GunIdle; this does not.
        /// </para>
        /// </summary>
        public static readonly int StateCount = HighestStateIndex() + 1;

        /// <summary>
        /// Time constant of the ground-speed filter, seconds. Four send intervals
        /// (0.133 s at <see cref="GameConstants.NetworkSendRate"/> 30).
        /// <para>
        /// <b>Why filtering is needed at all.</b> A snapshot's displacement is exact; the
        /// interval it is divided by is not. Two 30 Hz clocks that are not phase-locked —
        /// <c>NetPlayer</c>'s own send accumulator and Mirror's <c>syncInterval</c> — plus
        /// the frame the arrival is noticed on, mean a step worth two intervals of walking
        /// can be measured over one. <see cref="MinimumSampleSeconds"/> caps that at a
        /// factor of about three.
        /// </para>
        /// <para>
        /// <b>What four buys.</b> One worst-case sample (3×) lifts a 걷기 2.0 m/s estimate
        /// to 2.0 + (1 − e^(−1/4)) × 4.0 = 2.88 m/s, and §06's 걷기/달리기 crossover —
        /// (2.0 + 4.5) / 2 = 3.25 — is above it, so a single bad snapshot cannot flip the
        /// pose. A real 걷기 → 달리기 crosses the same threshold in τ·ln 2 = 0.092 s, which
        /// is <em>shorter</em> than the 0.15 s crossfade a local body blends over
        /// (<see cref="BlendSeconds"/>), so the filter is never the slow part.
        /// </para>
        /// <para>
        /// <b>The cost of getting it wrong.</b> Too short and a walking runner flickers
        /// between Walk and Run at the snapshot rate — the single most broken-looking
        /// thing on screen, and worse than the slide it replaces, because a mannequin
        /// reads as a missing feature and a strobing one reads as a broken game. Too long
        /// and the body keeps walking for a beat after the player has begun to sprint,
        /// which §12 cares about: footstep cadence is the Listener's positioning channel
        /// and a stride that lags the position it belongs to is a lie about distance.
        /// </para>
        /// </summary>
        public static readonly float SpeedSmoothingSeconds = 4f / GameConstants.NetworkSendRate;

        /// <summary>
        /// Shortest interval two distinct wire positions may be divided by, seconds.
        /// <para>
        /// One send interval, and it is a fact rather than a tuning:
        /// <c>NetPlayer.AccumulateAndReport</c> will not issue a second
        /// <c>CmdReportView</c> inside one interval, so two different positions are always
        /// at least that far apart on the owner's clock however they are delivered here.
        /// Without this floor a pair of snapshots noticed on consecutive frames of a
        /// 144 Hz client would divide a walking step by 7 ms and read as 10 m/s.
        /// </para>
        /// </summary>
        public static readonly float MinimumSampleSeconds = 1f / GameConstants.NetworkSendRate;

        /// <summary>
        /// How long an unchanged position is tolerated before the runner is read as
        /// standing, seconds. Six send intervals — five consecutive missed snapshots.
        /// <para>
        /// A standing player produces <em>no</em> deliveries: Mirror only sends a SyncVar
        /// that changed, and a stationary runner reports the same <c>Vector3</c> every
        /// tick. So "no new sample" is the only evidence a remote body ever gets that
        /// somebody stopped, and without this the last speed would be held forever and
        /// every runner who stopped would jog on the spot for the rest of the race.
        /// </para>
        /// <para>
        /// <b>Six, and the number was measured rather than argued.</b> A delivery gap and
        /// a standstill are the same thing seen from here, so this constant is the line
        /// between them, and setting it too tight turns lost packets into the very flicker
        /// <see cref="SpeedSmoothingSeconds"/> exists to prevent. Simulated against a
        /// jittery 30 Hz wire at 30/60/120/144/240 fps, counting how often a
        /// <em>continuously sprinting</em> runner's pose crosses the 걷기/달리기 line, per
        /// minute:
        /// </para>
        /// <code>
        ///  grace |   0%     2%     5%    10%    20%    30%  packet loss | stop → Idle
        ///  100ms | 0.00   0.00   0.33   3.29  19.15  61.97              |   0.217 s
        ///  133ms | 0.00   0.00   0.00   0.16   3.86  18.25              |   0.267 s
        ///  200ms | 0.00   0.00   0.00   0.00   0.16   1.15              |   0.317 s
        ///  400ms | 0.00   0.00   0.00   0.00   0.00   0.00              |   0.533 s
        /// </code>
        /// <para>
        /// Six intervals is the first row with nothing at all through a 10% loss rate —
        /// a connection §13 would already call bad — and one flip per six minutes at 20%.
        /// The whole cost is 100 ms of settling: a runner who stops reaches
        /// <see cref="GameConstants.StillSpeedThreshold"/> in 0.317 s instead of 0.217 s,
        /// which on screen is one beat of deceleration rather than a snap, and is the
        /// error worth having — a body that takes a third of a second to stand still reads
        /// as a person, and a body that strobes between two gaits reads as a broken game.
        /// </para>
        /// </summary>
        public static readonly float StillGraceSeconds = 6f / GameConstants.NetworkSendRate;

        /// <summary>
        /// Time constant used to fall to a standstill once the position has stopped
        /// changing, seconds. One send interval.
        /// <para>
        /// Shorter than <see cref="SpeedSmoothingSeconds"/> on purpose, and the asymmetry
        /// is not a fudge: an arriving snapshot is a noisy measurement and deserves
        /// filtering, whereas <em>no</em> snapshot for a whole
        /// <see cref="StillGraceSeconds"/> is not noise, it is the wire stating the runner
        /// has not moved. Stop to <see cref="GameConstants.StillSpeedThreshold"/> from
        /// 걷기 therefore takes 0.200 + 0.033 × ln(2.0 / 0.05) = 0.317 s, measured, which
        /// is one beat of deceleration rather than a snap.
        /// </para>
        /// </summary>
        public static readonly float StillDecaySeconds = 1f / GameConstants.NetworkSendRate;

        /// <summary>
        /// Delivery slack allowed on top of the measured interval before a step is called
        /// a teleport, seconds. Two send intervals.
        /// <para>
        /// <b>How a teleport is seen from here, with no new byte on the wire.</b>
        /// <c>NetPlayer.CmdReportView</c> clamps every position a client reports with
        /// <c>MoveTowards(_position, position, RunnerSprintSpeed × elapsed)</c>. That makes
        /// <see cref="GameConstants.RunnerSprintSpeed"/> a hard ceiling on how fast the
        /// replicated position can move <em>by running</em>: nothing in §05's speed table
        /// is faster, and the host refuses to let the number climb faster than that even
        /// if a client claims otherwise. The only writer that is not clamped is
        /// <c>NetPlayer.TeleportTo</c>, which assigns <c>_position</c> outright and clears
        /// <c>_hasReported</c> so the next report is unclamped too. So a step that exceeds
        /// the ceiling is, by construction, not somebody running — it is §01's 투하구, §06
        /// sending a caught runner back to their B1 cell, or the lobby placing a runner on
        /// the rim.
        /// </para>
        /// <para>
        /// The slack covers the two unsynchronised 30 Hz clocks between the owner's legs
        /// and this component — <c>NetPlayer</c>'s send accumulator and Mirror's
        /// <c>syncInterval</c> — so a snapshot delivered late cannot be mistaken for a
        /// jump. A stall longer than that is covered by the measured interval itself,
        /// which grows with it: the ceiling is a speed, not a distance, and a client that
        /// freezes for a second is allowed 5.6 m of catch-up.
        /// </para>
        /// <para>
        /// Concretely the smallest ceiling is 5.6 × (0.033 + 0.067) = 0.56 m, and the
        /// smallest teleport in the game is §11's lobby-to-rim placement — 8.84 m from the
        /// origin to the nearest starting marker in this building, measured by
        /// <c>NetHumanRunnerTests</c>. Fifteen times the ceiling.
        /// </para>
        /// </summary>
        public static readonly float TeleportSlackSeconds = 2f / GameConstants.NetworkSendRate;

        /// <summary>
        /// Crossfade length between poses, seconds.
        /// <para>
        /// <b>Derived, not copied.</b> It reads <c>PlayerAnimatorDriver</c>'s own default,
        /// which the driver's <c>_blendSeconds</c> field is also initialised from — so
        /// there is one number and retuning it moves the local rig and every remote body
        /// together. It was a hand-copied 0.15 in the first draft, and the symptom of that
        /// drifting is a remote body snapping between poses while yours eases: two
        /// different characters doing the same thing, and nobody files it.
        /// </para>
        /// </summary>
        public const float BlendSeconds = PlayerAnimatorDriver.DefaultBlendSeconds;

        private Animator? _animator;
        private NetPlayer? _player;

        private AnimationClip?[]? _sources;
        private AnimationClipPlayable[]? _clips;
        private bool[]? _hasClip;
        private float[]? _weights;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private bool _graphBuilt;

        private PlayerAnimationState _state = PlayerAnimationState.Idle;
        private Vector3 _lastWire;
        private float _lastSampleTime;
        private bool _seeded;
        private bool _stale;
        private float _speed;

        /// <summary>The pose this body is playing. What another player can read off it.</summary>
        public PlayerAnimationState State
        {
            get { return _state; }
        }

        /// <summary>
        /// Horizontal speed reconstructed from §05's 위치 row, m/s — the number handed to
        /// <see cref="PlayerAnimatorDriver.Resolve"/>.
        /// <para>
        /// Horizontal, because that is what <c>PlayerMotor.GroundSpeed</c> means and
        /// <c>Resolve</c> is documented against it. It also keeps a fall out of the
        /// answer: §01's 투하구 drops a runner <c>Chute.DropHeightMetres</c> and a body that
        /// broke into a run because it was falling would be the same defect the teleport
        /// guard exists for, arriving by the other axis.
        /// </para>
        /// </summary>
        public float GroundSpeed
        {
            get { return _speed; }
        }

        /// <summary>
        /// How many wire positions have been differentiated into a speed.
        /// <para>
        /// Exposed so a test can distinguish "the body is Idle because the runner is
        /// standing" from "the body is Idle because nothing ever reached it" — the second
        /// is the defect and the first is the game, and they look identical from the pose
        /// alone.
        /// </para>
        /// </summary>
        public int SamplesTaken { get; private set; }

        /// <summary>
        /// How many steps were rejected as teleports rather than differentiated.
        /// <para>
        /// Counted for the same reason as <see cref="SamplesTaken"/>: an assertion that a
        /// teleported runner did not sprint is worthless unless it can also show the guard
        /// is what stopped it, rather than the snapshot never having arrived.
        /// </para>
        /// </summary>
        public int TeleportsIgnored { get; private set; }

        /// <summary>Whether the <c>PlayableGraph</c> exists and is driving the Animator.</summary>
        public bool IsPlaying
        {
            get { return _graphBuilt && _graph.IsValid(); }
        }

        /// <summary>Whether <see cref="Bind"/> found at least one clip to play.</summary>
        public bool HasClips { get; private set; }

        /// <summary>
        /// The clip bound to a pose on this body, or null when that slot was empty on the
        /// rig this body was copied from.
        /// </summary>
        /// <param name="state">The pose.</param>
        public AnimationClip? ClipFor(PlayerAnimationState state)
        {
            var index = (int)state;
            var sources = _sources;
            return sources != null && index >= 0 && index < sources.Length ? sources[index] : null;
        }

        /// <summary>
        /// The mixer weight a pose currently holds, 0–1.
        /// <para>
        /// The pose as the <em>graph</em> holds it, not as a field remembers it. A test
        /// that only read <see cref="State"/> would pass on a component that chose the
        /// right pose and never connected it to an Animator, which is this repo's
        /// signature failure written in animation.
        /// </para>
        /// </summary>
        /// <param name="state">The pose.</param>
        public float WeightOf(PlayerAnimationState state)
        {
            var index = (int)state;
            var weights = _weights;
            if (weights == null || index < 0 || index >= weights.Length)
            {
                return 0f;
            }

            var total = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                total += weights[i];
            }

            return total > 0f ? weights[index] / total : 0f;
        }

        /// <summary>
        /// Takes the clip set from the rig this body was copied from and starts playing.
        /// <para>
        /// <see cref="PlayerAnimatorDriver.ClipFor"/> rather than a second load of
        /// <c>Runner.fbx</c>: that class documents itself as the owner of the pose → clip
        /// mapping precisely so a second consumer does not match names again, and the
        /// clips are serialised into <c>Map_FirstSketch_Solo.unity</c> by
        /// <c>SoloPlaytest.WirePlayerAnimation</c>, which audits its own work by reading
        /// the saved scene back. Borrowing that wiring means a remote body is animated by
        /// exactly the assets the audit says are in the artefact.
        /// </para>
        /// <para>
        /// Called by <see cref="NetRunnerBody"/> immediately after the component is added.
        /// It cannot be done in <c>Awake</c>: <c>AddComponent</c> on a live object runs
        /// <c>Awake</c> at once, and at that instant the body is a bare copy with no idea
        /// which rig it came from.
        /// </para>
        /// </summary>
        /// <param name="source">The local rig's driver, holding the nine serialised slots.</param>
        public void Bind(PlayerAnimatorDriver source)
        {
            if (source == null)
            {
                return;
            }

            EnsureTables();

            var found = false;
            for (var i = 0; i < StateCount; i++)
            {
                // Values the enum does not define (5, 6 and 7 — the deleted carry poses)
                // fall through ClipFor's default and answer null, which is the same answer
                // an empty slot gives. Nothing here has to know which is which.
                var clip = source.ClipFor((PlayerAnimationState)i);
                _sources![i] = clip;
                found |= clip != null;
            }

            HasClips = found;

            DestroyGraph();
            BuildGraph();
        }

        private void Awake()
        {
            EnsureTables();

            // Under the body root rather than on it: NetRunnerBody copies the rig's
            // "Visual" child, and PlayerFeelHarnessMenu.BuildRig puts the Animator on the
            // FBX instance inside it — AssignSerialized(animator, "_animator",
            // visual.GetComponentInChildren<Animator>()) is the line that decides this.
            _animator = GetComponentInChildren<Animator>(true);
        }

        private void OnEnable()
        {
            // The differentiator forgets everything it knew. A body that was switched off
            // — NetLocalRunner does exactly that to the owner's own proxy — comes back
            // with a last-known position that may be a whole race out of date, and one
            // sample measured across that gap is the teleport case arriving through the
            // back door.
            _seeded = false;
            _stale = false;
            _speed = 0f;

            // Rebuilt rather than resumed, for the same reason: a spectator hand-off or a
            // re-parent would switch a body back on, and a graph destroyed in OnDisable
            // has to be able to come back from the clips alone.
            BuildGraph();
        }

        private void OnDisable()
        {
            DestroyGraph();
        }

        private void Update()
        {
            var player = ResolvePlayer();
            if (player == null || !_graphBuilt)
            {
                return;
            }

            // netId 0 is a runner Mirror has not spawned yet. Its SyncVar holds whatever
            // the constructor left, and differentiating against it would give every body
            // one bogus sample on the frame it is born — which, from the origin to a rim
            // marker, is the teleport case arriving as a false sprint.
            if (player.netId == 0)
            {
                return;
            }

            SampleTheWire(player);

            var next = ChoosePose();
            _state = next;

            AdvanceWeights(next, Time.deltaTime);
            ApplyPlaybackSpeed(next, _speed);
        }

        /// <summary>
        /// Turns §05's 위치 row into a ground speed.
        /// <para>
        /// <c>NetPlayer.NetworkedPosition</c> — the SyncVar — and deliberately not this
        /// object's <c>transform.position</c>. The transform is already smoothed toward
        /// the SyncVar by <c>NetPlayer.ApplyRemoteView</c>, so differentiating it would be
        /// filtering a filter, and worse: during a teleport the transform slides the whole
        /// distance over several frames at a genuinely enormous speed, which is precisely
        /// the sprint-across-the-map this class exists to prevent. The SyncVar shows the
        /// jump as one step, where it can be recognised.
        /// </para>
        /// <para>
        /// The clock is unscaled. The wire runs on wall time whatever the game's
        /// <c>timeScale</c> is — <c>NetLocalRunner</c> polls on the same clock for the same
        /// reason — and a paused game would otherwise divide a real displacement by zero.
        /// </para>
        /// </summary>
        private void SampleTheWire(NetPlayer player)
        {
            var wire = player.NetworkedPosition;
            var now = Time.unscaledTime;

            if (!_seeded)
            {
                _seeded = true;
                _lastWire = wire;
                _lastSampleTime = now;
                return;
            }

            if (wire == _lastWire)
            {
                if (now - _lastSampleTime > StillGraceSeconds)
                {
                    _stale = true;
                    _speed = Mathf.Lerp(_speed, 0f, Blend(Time.unscaledDeltaTime, StillDecaySeconds));
                }

                return;
            }

            var step = wire - _lastWire;
            var elapsed = Mathf.Max(now - _lastSampleTime, MinimumSampleSeconds);

            _lastWire = wire;
            _lastSampleTime = now;

            if (_stale)
            {
                // The first step out of a standstill is re-seeded rather than
                // differentiated. A player who stood for two seconds sent no snapshots at
                // all, so the interval measured here is two seconds while the displacement
                // is one tick of walking — dividing them would report sixty times too slow
                // and then take a third of a second to climb back out of it. One discarded
                // sample costs 33 ms of Idle at the start of a walk, which is a fifth of a
                // crossfade.
                _stale = false;
                return;
            }

            // The host's own clamp, read backwards. See TeleportSlackSeconds.
            var ceiling = GameConstants.RunnerSprintSpeed * (elapsed + TeleportSlackSeconds);
            if (step.magnitude > ceiling)
            {
                // Dropped rather than clamped, and the estimate is reset rather than held.
                // A body that has just been moved somewhere else has no continuity to
                // carry: it is standing on a rim it has not run to. One snapshot of Idle
                // costs 33 ms — under a quarter of a crossfade — and the sample after it
                // is measured against a position the body was actually at.
                TeleportsIgnored++;
                _speed = 0f;
                return;
            }

            // Horizontal only — PlayerMotor.GroundSpeed is
            // new Vector2(velocity.x, velocity.z).magnitude, and Resolve is documented
            // against that. See GroundSpeed.
            var ground = new Vector2(step.x, step.z).magnitude / elapsed;

            _speed = Mathf.Lerp(_speed, ground, Blend(elapsed, SpeedSmoothingSeconds));
            SamplesTaken++;
        }

        /// <summary>
        /// The pose, from the driver's own table.
        /// <para>
        /// <b>This is the line the gun changes.</b> §05 replicates five rows and stance is
        /// not one of them, so <c>crouching</c> is false — a crouched runner is drawn
        /// standing to everybody else today, which is a gap in §05's row table rather than
        /// one in this file. <c>dead</c> is false because nothing in this game kills
        /// anybody: §06's creature and §09's gun both call
        /// <c>RaceState.ReportCaught</c>, which leaves the runner Running and puts them
        /// back on B1. When GunIdle/GunWalk land, a <c>bool GunHeld</c> replicated the way
        /// 손전등 already is becomes one more argument to <see cref="PlayerAnimatorDriver.Resolve"/>
        /// and one more term here; nothing else in this class moves, because the clip
        /// table and the mixer are both sized from <see cref="PlayerAnimationState"/>.
        /// </para>
        /// </summary>
        private PlayerAnimationState ChoosePose()
        {
            return PlayerAnimatorDriver.Resolve(_speed, crouching: false, dead: false);
        }

        /// <summary>
        /// The <see cref="NetPlayer"/> this body belongs to, or null while it has none.
        /// <para>
        /// Resolved lazily and never in <c>Awake</c>: <c>NetRunner.AttachVisual</c> parents
        /// the body under the runner <em>after</em> the factory that built it has returned,
        /// so at <c>Awake</c> there is no parent to find. Null is a legal, quiet state — a
        /// body built for a render or a shot rig has no runner and simply stands still.
        /// </para>
        /// </summary>
        private NetPlayer? ResolvePlayer()
        {
            if (_player != null)
            {
                return _player;
            }

            _player = GetComponentInParent<NetPlayer>(includeInactive: true);
            return _player;
        }

        private void EnsureTables()
        {
            if (_sources != null)
            {
                return;
            }

            _sources = new AnimationClip?[StateCount];
            _clips = new AnimationClipPlayable[StateCount];
            _hasClip = new bool[StateCount];
            _weights = new float[StateCount];
        }

        private void BuildGraph()
        {
            if (_graphBuilt || _animator == null || !HasClips || _sources == null)
            {
                return;
            }

            // Root motion off, for the reason PlayerAnimatorDriver.BuildGraph gives: the
            // position of a remote runner is the host's, and a clip that also moved the
            // body would be a second opinion about where somebody is.
            _animator.applyRootMotion = false;

            // ----------------------------------------------------------------
            // AlwaysAnimate, and this is the line that decides whether any of the rest of
            // this class reaches a bone.
            //
            // The copy inherits the local rig's Animator settings, and that Animator is
            // set to CullUpdateTransforms — the right choice for a FIRST-PERSON body,
            // which is off-screen nearly all the time and whose pose nobody reads. It is
            // the wrong choice here for two reasons:
            //
            //  1. Culling is decided from the SkinnedMeshRenderer's BOUNDS, and this body
            //     is a runtime Instantiate of a template authored for one pose. A remote
            //     runner whose bounds have not caught up is culled while on screen — the
            //     "somebody T-poses at the edge of the frame" bug — and §05 makes reading
            //     what another runner is doing the entire reason this body exists.
            //  2. It is measured, not argued: with CullUpdateTransforms, a remote body's
            //     bones sweep 0.00° in a headless run — the graph is built, the mixer is
            //     weighted, the pose is Walk, and nothing moves. That is indistinguishable
            //     from the defect this class was written to fix, which means the defect
            //     could come back and no test could see it.
            //
            // The cost is twenty mixers evaluating off-screen at §11's ceiling. §13's
            // budget is bandwidth and this spends none of it.
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _graph = PlayableGraph.Create("NetRunnerAnimation:" + name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _mixer = AnimationMixerPlayable.Create(_graph, StateCount);

            for (var i = 0; i < StateCount; i++)
            {
                _weights![i] = 0f;
                _hasClip![i] = false;

                var clip = _sources[i];
                if (clip == null)
                {
                    continue;
                }

                _clips![i] = AnimationClipPlayable.Create(_graph, clip);
                _clips[i].SetApplyFootIK(false);
                _graph.Connect(_clips[i], 0, _mixer, i);
                _mixer.SetInputWeight(i, 0f);
                _hasClip[i] = true;
            }

            var output = AnimationPlayableOutput.Create(_graph, "NetRunnerAnimation", _animator);
            output.SetSourcePlayable(_mixer);

            var idle = (int)PlayerAnimationState.Idle;
            if (_hasClip![idle])
            {
                _weights![idle] = 1f;
                _mixer.SetInputWeight(idle, 1f);
            }

            _graph.Play();
            _graphBuilt = true;
        }

        private void DestroyGraph()
        {
            if (!_graphBuilt)
            {
                return;
            }

            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            for (var i = 0; i < StateCount; i++)
            {
                _hasClip![i] = false;
                _weights![i] = 0f;
            }

            _graphBuilt = false;
        }

        /// <summary>
        /// Crossfades toward the chosen pose. Same shape as
        /// <c>PlayerAnimatorDriver.AdvanceWeights</c>, including the normalisation: a
        /// mixer whose inputs do not sum to one scales the pose toward the bind position,
        /// which on a runner reads as a body sinking into the floor mid-blend.
        /// </summary>
        private void AdvanceWeights(PlayerAnimationState target, float deltaSeconds)
        {
            var targetIndex = (int)target;
            var rate = BlendSeconds > 0f ? deltaSeconds / BlendSeconds : 1f;
            var total = 0f;

            for (var i = 0; i < StateCount; i++)
            {
                if (!_hasClip![i])
                {
                    _weights![i] = 0f;
                    continue;
                }

                var want = i == targetIndex ? 1f : 0f;
                _weights![i] = Mathf.MoveTowards(_weights[i], want, rate);
                total += _weights[i];
            }

            if (total <= 0f)
            {
                return;
            }

            for (var i = 0; i < StateCount; i++)
            {
                _mixer.SetInputWeight(i, _hasClip![i] ? _weights![i] / total : 0f);
            }
        }

        /// <summary>
        /// Scales the locomotion clip by <c>groundSpeed / referenceSpeed</c>, which is the
        /// only thing that keeps a remote runner's feet on the ground at §05's several
        /// speeds. §12 makes the stride the Listener's distance cue, so feet that skate on
        /// somebody else's body are a lie about how far away they are.
        /// </summary>
        private void ApplyPlaybackSpeed(PlayerAnimationState state, float groundSpeed)
        {
            var index = (int)state;
            if (!_hasClip![index])
            {
                return;
            }

            var reference = PlayerAnimatorDriver.ReferenceSpeed(state);
            var speed = reference > 0f ? groundSpeed / reference : 1f;
            _clips![index].SetSpeed(speed > 0f ? speed : 0f);
        }

        /// <summary>
        /// Frame-rate independent exponential blend factor: the fraction of the remaining
        /// gap to close in <paramref name="deltaSeconds"/> at time constant
        /// <paramref name="tauSeconds"/>. Written as 1 − e^(−dt/τ) rather than a constant
        /// per-frame fraction so the filter behaves the same on a 30 Hz machine and a
        /// 240 Hz one — a remote runner who reads as sprinting only on fast hardware would
        /// be the worst kind of bug to be told about.
        /// </summary>
        private static float Blend(float deltaSeconds, float tauSeconds)
        {
            if (tauSeconds <= 0f || deltaSeconds <= 0f)
            {
                return deltaSeconds > 0f ? 1f : 0f;
            }

            return 1f - Mathf.Exp(-deltaSeconds / tauSeconds);
        }

        private static int HighestStateIndex()
        {
            var highest = 0;
            foreach (var value in Enum.GetValues(typeof(PlayerAnimationState)))
            {
                // Cast to the enum first and then to int. A boxed enum unboxed straight
                // to int throws InvalidCastException at run time and compiles perfectly.
                var index = (int)(PlayerAnimationState)value;
                if (index > highest)
                {
                    highest = index;
                }
            }

            return highest;
        }
    }
}
