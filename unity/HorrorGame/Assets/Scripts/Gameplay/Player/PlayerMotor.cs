#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Moves the player at whatever speed §05 says they have earned this frame.
    /// <para>
    /// Every number here comes out of <see cref="SpeedResolver"/> and
    /// <see cref="StaminaState"/>. That is not modesty about layering — it is the only way
    /// §05's central claim survives contact with an engine. §05 says the directional
    /// penalty is <em>analogue</em>: "플레이어가 정보량과 속도를 연속적으로 조절할 수
    /// 있다. 이산적 선택이 아니라 아날로그 조절이라는 점이 중요하다 — 마우스를 몇 도 돌릴지가
    /// 곧 실력이다." A controller that re-derived the table as four <c>if</c> branches
    /// would turn the 45° peek from a skill into a button, and would do it silently,
    /// because all four documented values would still be correct. So this class computes
    /// no multiplier of its own; it collects state, asks the core, and applies the answer
    /// to a <see cref="CharacterController"/>.
    /// </para>
    /// <para>
    /// The step is a public method taking an explicit delta rather than something
    /// <see cref="Update"/> keeps to itself, because §13 puts the host in charge: the
    /// Mirror layer will call <see cref="Step"/> at <see cref="GameConstants.FixedStep"/>
    /// with an input that arrived over the wire, and the local player has to travel the
    /// same path or client and host will disagree about §05's table.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField]
        private PlayerInputRouter? _input;

        [SerializeField]
        private PlayerLook? _look;

        [Tooltip("Supplies §08's load multiplier. Left empty, found on this object.")]
        [SerializeField]
        private PlayerLoadout? _loadout;

        [Header("Role")]
        [Tooltip("§04. Only the Runner's Shift buys 질주 5.6; everyone else gets 달리기 4.5.")]
        [SerializeField]
        private RoleId _role = RoleId.Runner;

        private CharacterController? _controller;
        private readonly StaminaState _stamina = new StaminaState();

        private MoveInput _lastInput;
        private MovementContext _lastContext = MovementContext.Unloaded(GameConstants.WalkSpeed);
        private float _resolvedSpeed;
        private float _verticalVelocity;
        private Vector3 _worldVelocity;

        /// <summary>
        /// The Runner's bar (§06). Exposed because the HUD shows it, §05 syncs it, and
        /// §09's respawn resets it — nothing outside may drain it.
        /// </summary>
        public StaminaState Stamina
        {
            get { return _stamina; }
        }

        /// <summary>§04 role. Set by the match layer at spawn; changing it mid-match is a §11 violation, not a feature.</summary>
        public RoleId Role
        {
            get { return _role; }
            set { _role = value; }
        }

        /// <summary>The intent the last step ran on, sanitised. What the Mirror layer sends and what telemetry buckets.</summary>
        public MoveInput LastInput
        {
            get { return _lastInput; }
        }

        /// <summary>The §05/§08 context the last step ran on, base speed included.</summary>
        public MovementContext LastContext
        {
            get { return _lastContext; }
        }

        /// <summary>Speed the rules granted last step, m/s. The single number §14's questions 1 and 2 are about.</summary>
        public float ResolvedSpeed
        {
            get { return _resolvedSpeed; }
        }

        /// <summary>
        /// The §05 multiplier alone for the last step: 1.00 running straight ahead, 0.65
        /// straight back, and everything in between actually in between.
        /// </summary>
        public float DirectionalMultiplier
        {
            get { return SpeedResolver.DirectionalMultiplier(_lastInput); }
        }

        /// <summary>How far the movement heading sits from straight ahead, 0–180°. 45 is §05's peek.</summary>
        public float HeadingOffsetDegrees
        {
            get { return _lastInput.HeadingOffsetDegrees; }
        }

        /// <summary>
        /// Measured world velocity, m/s, from the position actually reached. Not the
        /// requested velocity: a player pressed into a wall requests 5.6 and travels 0,
        /// and §04's Observer ("이동 정지 3초") and the footsteps both need the truth.
        /// </summary>
        public Vector3 WorldVelocity
        {
            get { return _worldVelocity; }
        }

        /// <summary>Horizontal ground speed, m/s. What the animation and the footstep cadence read.</summary>
        public float GroundSpeed
        {
            get { return new Vector2(_worldVelocity.x, _worldVelocity.z).magnitude; }
        }

        /// <summary>Standing on something. No jump exists — §05's scheme has no key for it.</summary>
        public bool IsGrounded
        {
            get
            {
                var controller = Controller;
                return controller != null && controller.isGrounded;
            }
        }

        /// <summary>
        /// The controller, resolved on first use rather than only in <c>Awake</c>.
        /// <para>
        /// Editor tooling assembles a rig and steps it without ever entering play mode —
        /// <c>SoloPlaytest.BuildScene</c> and <c>ViewMotionShot</c> both do — and Unity
        /// does not run <c>Awake</c> for a component added outside play mode. A motor
        /// that quietly refused to move in that case looked exactly like a broken §05
        /// speed table, which cost an evening once already.
        /// </para>
        /// </summary>
        private CharacterController? Controller
        {
            get
            {
                if (_controller == null)
                {
                    _controller = GetComponent<CharacterController>();
                }

                return _controller;
            }
        }

        /// <summary>
        /// Stops the feet without touching the view. §05 decides this explicitly for §04's
        /// Observer: "이동만 정지, 마우스룩 허용 — 화면이 얼면 조작감 최악. 발이 묶인 채
        /// 둘러보는 게 더 무섭다."
        /// </summary>
        public bool MovementLocked { get; set; }

        /// <summary>
        /// True when something else — the Mirror layer — is calling <see cref="Step"/>, so
        /// <see cref="Update"/> must not step a second time. §13: one authority, one step.
        /// </summary>
        public bool SteppedExternally { get; set; }

        /// <summary>
        /// Whether Shift can buy 질주 right now: §04 gives it to the Runner alone, §08
        /// takes it away at weight ≥ 16, and §03 takes it away while the objective is in
        /// hand.
        /// </summary>
        public bool SprintUnlocked
        {
            get
            {
                var load = ResolveLoad();
                return _role == RoleId.Runner && load.CanSprint && !load.CarryingObjective;
            }
        }

        /// <summary>
        /// Whether this player is currently beating the monster, and by how much — §05's
        /// argument as one comparison.
        /// </summary>
        /// <param name="monsterSpeed">The monster's speed from the §07 threat tier, m/s. Not the §06 base: §07 scales it.</param>
        public ChaseMargin MarginVersusMonster(float monsterSpeed)
        {
            return new ChaseMargin(_resolvedSpeed, monsterSpeed);
        }

        /// <summary>
        /// The largest heading offset, in degrees, that still outruns
        /// <paramref name="monsterSpeed"/> under the current load and base speed — the
        /// exact size of §05's peek budget.
        /// <para>
        /// §05's skill table is a claim about this number: 전진 100% is +0.8 on the
        /// monster and 곁눈질 95% is +0.52, so the answer sits somewhere past 45° and a
        /// player who finds it is playing well. Returning it makes §14's question 2
        /// measurable instead of a matter of opinion, and it is computed by sampling
        /// <see cref="SpeedResolver.MultiplierForHeading"/> rather than by inverting the
        /// table by hand, so a retune of any of §05's four rows moves it automatically.
        /// </para>
        /// </summary>
        /// <param name="monsterSpeed">Monster speed from the §07 tier, m/s.</param>
        /// <param name="sprintGranted">Evaluate at 질주 speed rather than at the speed being used right now.</param>
        /// <returns>0 when even running straight ahead loses ground; 180 when nothing does.</returns>
        public float SafePeekAngleDegrees(float monsterSpeed, bool sprintGranted)
        {
            var context = BuildContext();
            context.BaseSpeed = SpeedResolver.SelectBaseSpeed(
                new MoveInput(1f, 0f, true), context, SprintUnlocked, sprintGranted);

            var contextMultiplier = SpeedResolver.ContextMultiplier(context);
            if (contextMultiplier <= 0f || context.BaseSpeed <= 0f)
            {
                return 0f;
            }

            // A degree-at-a-time scan. The curve is monotonically decreasing in the
            // heading angle by construction (SpeedResolver's remarks explain why a smooth
            // spline was rejected for exactly this property), so the first failure is the
            // boundary and there is no second crossing to miss.
            var last = 0f;
            for (var degrees = 0f; degrees <= GameConstants.BackwardAngleDegrees; degrees += 1f)
            {
                var speed = context.BaseSpeed * SpeedResolver.MultiplierForHeading(degrees) * contextMultiplier;
                if (speed <= monsterSpeed)
                {
                    return last;
                }

                last = degrees;
            }

            return GameConstants.BackwardAngleDegrees;
        }

        /// <summary>
        /// Advances movement by one step.
        /// <para>
        /// Public and delta-taking so the host can drive it at
        /// <see cref="GameConstants.FixedStep"/> from a replicated input while the local
        /// player drives it from <see cref="Update"/> at frame rate. Both go through
        /// exactly the same arithmetic, which is the only way client prediction and host
        /// authority can agree (§13).
        /// </para>
        /// </summary>
        /// <param name="input">Camera-local intent. Sanitised here, so an untrusted one is safe to pass.</param>
        /// <param name="deltaSeconds">Step length. Zero or negative does nothing.</param>
        public void Step(MoveInput input, float deltaSeconds)
        {
            var controller = Controller;
            if (controller == null || float.IsNaN(deltaSeconds) || deltaSeconds <= 0f)
            {
                return;
            }

            var sanitized = input.Sanitized;
            if (MovementLocked)
            {
                // §04's Observer keeps the mouse but loses the feet. Zeroing the intent
                // rather than skipping the step keeps gravity and the stamina bar running,
                // so standing still for three seconds still refills the bar.
                sanitized = new MoveInput(0f, 0f, false);
            }

            var context = BuildContext();
            var sprintUnlocked = _role == RoleId.Runner && !context.CarryingObjective && CanSprintUnderLoad();

            // The bar is only asked for sprint when a sprint could actually be granted.
            // StaminaState honours any request it can afford, so letting a non-Runner ask
            // would drain 12 seconds of a bar that buys them nothing — and §06 sizes that
            // bar by the distance it covers.
            _stamina.SprintRequested = sanitized.SprintHeld && !sanitized.IsIdle && sprintUnlocked;
            _stamina.Tick(deltaSeconds);

            // LastSprintSeconds, not SprintAvailable: the bar reports what it actually
            // paid for this step, and speed should be granted for exactly that. Reading
            // availability after the tick would drop a player to 4.5 on the same step the
            // bar spent its last slice at 5.6.
            var sprintGranted = _stamina.LastSprintSeconds > 0f;

            context.BaseSpeed = SpeedResolver.SelectBaseSpeed(sanitized, context, sprintUnlocked, sprintGranted);

            var yaw = _look != null ? _look.YawDegrees : transform.rotation.eulerAngles.y;
            var velocity = SpeedResolver.ResolveVelocity(sanitized, context, yaw).ToVector3();

            _lastInput = sanitized;
            _lastContext = context;
            _resolvedSpeed = SpeedResolver.Resolve(sanitized, context);

            ApplyGravity(deltaSeconds);
            velocity.y = _verticalVelocity;

            var before = transform.position;
            controller.Move(velocity * deltaSeconds);
            _worldVelocity = (transform.position - before) / deltaSeconds;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            if (_input == null)
            {
                _input = GetComponentInChildren<PlayerInputRouter>();
            }

            if (_look == null)
            {
                _look = GetComponentInChildren<PlayerLook>();
            }

            if (_loadout == null)
            {
                _loadout = GetComponentInChildren<PlayerLoadout>();
            }

        }

        private void Reset()
        {
            _input = GetComponentInChildren<PlayerInputRouter>();
            _look = GetComponentInChildren<PlayerLook>();
            _loadout = GetComponentInChildren<PlayerLoadout>();
        }

        private void Update()
        {
            if (SteppedExternally)
            {
                return;
            }

            Step(_input != null ? _input.CurrentMove : new MoveInput(0f, 0f), Time.deltaTime);
        }

        private MovementContext BuildContext()
        {
            var load = ResolveLoad();

            // bagEquipped stays false on purpose. Inventory.SpeedMultiplier is
            // CarryLoad.Resolve(weight, bag), which has already applied §08's −10% strap
            // penalty; setting the flag here would apply it a second time and quietly cost
            // the player another 10% of every speed in §05's table.
            return new MovementContext(
                GameConstants.WalkSpeed,
                load.SpeedMultiplier,
                load.CarryingObjective,
                bagEquipped: false);
        }

        private bool CanSprintUnderLoad()
        {
            return ResolveLoad().CanSprint;
        }

        private IPlayerLoad ResolveLoad()
        {
            return _loadout != null ? (IPlayerLoad)_loadout : UnloadedPlayerLoad.Instance;
        }

        private void ApplyGravity(float deltaSeconds)
        {
            var controller = Controller;
            if (controller == null)
            {
                return;
            }

            if (controller.isGrounded && _verticalVelocity <= 0f)
            {
                // One step of gravity rather than zero, so the controller keeps a little
                // downward push into the floor. A hard zero makes isGrounded flicker on
                // slopes and stairs, and a flickering ground check reads as the footsteps
                // stuttering — which §12 turns into false information for the Listener.
                _verticalVelocity = Physics.gravity.y * deltaSeconds;
                return;
            }

            _verticalVelocity += Physics.gravity.y * deltaSeconds;
        }
    }

    /// <summary>
    /// The load of a player carrying nothing. Used when no <see cref="PlayerLoadout"/> is
    /// wired, so a bare test rig moves at §05's quoted speeds instead of at zero — a
    /// default-constructed <c>MovementContext</c> has a load multiplier of 0 and does not
    /// move at all, which is a deliberate trap in the core and a bad first impression in a
    /// harness.
    /// </summary>
    internal sealed class UnloadedPlayerLoad : IPlayerLoad
    {
        internal static readonly UnloadedPlayerLoad Instance = new UnloadedPlayerLoad();

        private UnloadedPlayerLoad()
        {
        }

        /// <inheritdoc />
        public float SpeedMultiplier
        {
            // 1 is the identity, not a tuned value: empty hands are §05's table unmodified.
            get { return 1f; }
        }

        /// <inheritdoc />
        public bool CanSprint
        {
            get { return true; }
        }

        /// <inheritdoc />
        public bool CarryingObjective
        {
            get { return false; }
        }

        /// <inheritdoc />
        public bool CarryingOversizePiece
        {
            get { return false; }
        }
    }
}
