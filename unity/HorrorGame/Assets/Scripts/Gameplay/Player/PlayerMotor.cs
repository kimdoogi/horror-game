#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Movement;
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

        [Tooltip("Owns 웅크리기 and the hop. Optional — a rig without one stands up and never leaves the floor.")]
        [SerializeField]
        private PlayerStance? _stance;

        // DELETED: _loadout (§08's carry weight) and _role (§04's five 직업).
        //
        // Both were gates on 질주. §04 gave Shift's 5.6 m/s to the Runner alone and §08
        // took it away again at weight ≥ 16, so a rig's top speed depended on a role it
        // was assigned and a bag it was carrying. Twenty identical runners race down the
        // same building carrying nothing, so both gates answer the same way for everyone,
        // every frame — and a gate that never varies is not a gate, it is an opportunity
        // to be wrong. SprintUnlocked is now unconditional and the load multiplier is
        // pinned at 1.
        //
        // ARCHITECTURE §3's seam is what made this a deletion rather than a rewrite:
        // MovementContext takes a float and StaminaState takes a bool, so the motor never
        // imported the economy in the first place. Removing the economy changed the
        // arguments, not the movement.

        private CharacterController? _controller;
        private readonly StaminaState _stamina = new StaminaState();

        private MoveInput _lastInput;
        private MovementContext _lastContext = MovementContext.Unloaded(GameConstants.WalkSpeed);
        private float _resolvedSpeed;
        private float _verticalVelocity;
        private Vector3 _worldVelocity;
        private Vector3 _airHorizontalVelocity;
        private bool _airborne;
        private float _descentAtLastStep;

        /// <summary>
        /// The Runner's bar (§06). Exposed because the HUD shows it, §05 syncs it, and
        /// §09's respawn resets it — nothing outside may drain it.
        /// </summary>
        public StaminaState Stamina
        {
            get { return _stamina; }
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

        /// <summary>Standing on something.</summary>
        public bool IsGrounded
        {
            get
            {
                var controller = Controller;
                return controller != null && controller.isGrounded;
            }
        }

        /// <summary>
        /// Off the floor because the player asked to be — <see cref="PlayerStance"/>'s
        /// hop, not a stairwell or a ledge. What suspends steering; see
        /// <see cref="Step"/>.
        /// </summary>
        public bool IsJumping
        {
            get { return _airborne; }
        }

        /// <summary>Vertical velocity, m/s, up positive. Exposed so a test can watch the hop leave and return.</summary>
        public float VerticalVelocity
        {
            get { return _verticalVelocity; }
        }

        /// <summary>The stance component, if this rig has one. Null on a bare test capsule.</summary>
        public PlayerStance? Stance
        {
            get { return _stance; }
            set { _stance = value; }
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
                    ConfigureController(_controller);
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
        /// Whether Shift can buy 질주 right now. Always true.
        /// <para>
        /// It used to be «§04 gives it to the Runner alone, §08 takes it away at weight
        /// ≥ 16, §03 takes it away while the objective is in hand» — three gates, all of
        /// whose subjects are deleted. Every one of twenty runners has 질주 and nothing to
        /// carry, so the only thing still rationing sprint is
        /// <see cref="StaminaState"/>'s twelve seconds, which is where §06 wanted the
        /// decision all along.
        /// </para>
        /// <para>
        /// Kept as a property rather than folded into the call sites because it is the
        /// sentence «can this player sprint», and the next thing that wants to say no —
        /// exhaustion, a shut door, an injury — should have somewhere to say it.
        /// </para>
        /// </summary>
        public bool SprintUnlocked
        {
            get { return true; }
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
                new MoveInput(1f, 0f, true), SprintUnlocked, sprintGranted);

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

            if (_stance != null && _stance.IsCrouched)
            {
                // Shift is dropped before the rules see it rather than after. §06's ladder
                // is 걷기 · 달리기 · 질주 and a crouch is none of them, so the base speed
                // has to come out as 걷기 — and the bar must not be asked for a sprint it
                // is not going to be paid for, because §06 sizes those twelve seconds by
                // the distance they cover.
                sanitized = new MoveInput(sanitized.Forward, sanitized.Strafe, false);
            }

            var context = BuildContext();
            var sprintUnlocked = SprintUnlocked;

            // The guard survives even though sprintUnlocked is now constant: the bar is
            // only asked for a sprint that could actually be granted. It mattered when a
            // non-Runner asking would drain 12 seconds that bought them nothing; it is
            // kept so that the next thing to close SprintUnlocked does not have to
            // rediscover that StaminaState honours any request it can afford.
            _stamina.SprintRequested = sanitized.SprintHeld && !sanitized.IsIdle && sprintUnlocked;
            _stamina.Tick(deltaSeconds);

            // LastSprintSeconds, not SprintAvailable: the bar reports what it actually
            // paid for this step, and speed should be granted for exactly that. Reading
            // availability after the tick would drop a player to 4.5 on the same step the
            // bar spent its last slice at 5.6.
            var sprintGranted = _stamina.LastSprintSeconds > 0f;

            context.BaseSpeed = SpeedResolver.SelectBaseSpeed(sanitized, sprintUnlocked, sprintGranted);

            var yaw = _look != null ? _look.YawDegrees : transform.rotation.eulerAngles.y;
            var velocity = SpeedResolver.ResolveVelocity(sanitized, context, yaw).ToVector3();

            _lastInput = sanitized;
            _lastContext = context;
            _resolvedSpeed = SpeedResolver.Resolve(sanitized, context);

            var groundedBefore = controller.isGrounded;
            ApplyGravity(deltaSeconds);

            // The jump is taken after gravity, not before: ApplyGravity's whole job on a
            // grounded step is to press a step of gravity into the floor, and it would
            // overwrite an impulse written ahead of it.
            if (_stance != null && groundedBefore && _stance.ConsumeJump())
            {
                // Half a step of gravity taken off the impulse. Integrating v then p — the
                // order every controller uses — overshoots the true apex by v·Δt/2, which
                // at 50 Hz measured 0.374 m against §12's 0.350 and at 20 Hz would have
                // been 0.415, i.e. past PlayerStepOffsetMetres. The bound that keeps the
                // map's geometry meaningful must not depend on the frame rate, so the
                // launch speed is corrected instead of the constant being shaved.
                var gravity = Mathf.Abs(Physics.gravity.y);
                _verticalVelocity = Mathf.Max(0f, _stance.TakeoffSpeed - (0.5f * gravity * deltaSeconds));
                _airborne = true;
                _airHorizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            }

            if (_airborne)
            {
                // The fall speed the hop reached, kept as it happens: by the time the
                // controller reports isGrounded the speed that mattered has already been
                // thrown away, which is the same reason PlayerViewMotion sizes its landing
                // dip from the previous frame.
                var falling = -_verticalVelocity;
                if (falling > _descentAtLastStep)
                {
                    _descentAtLastStep = falling;
                }

                if (groundedBefore && _verticalVelocity <= 0f)
                {
                    // Back on something. Only a hop reports a landing — walking down one
                    // of §12's seven stairwells must not fire §04's transient on every
                    // riser, and the player did not choose to make that noise.
                    _airborne = false;
                    _airHorizontalVelocity = Vector3.zero;

                    if (_stance != null)
                    {
                        _stance.NotifyLanded(_descentAtLastStep);
                    }

                    _descentAtLastStep = 0f;
                }
                else
                {
                    // No air control worth the name. §06's chase arithmetic and §12's
                    // geometry are both about a route walked on the ground; steering in
                    // mid-air is the property that turns a hop into a movement technique,
                    // so the horizontal velocity is frozen at whatever left the floor. The
                    // player can still look — §05 keeps the view and the feet separate,
                    // and freezing the view would be the "화면이 얼면 조작감 최악" §05
                    // rejects for §04's Observer.
                    velocity.x = _airHorizontalVelocity.x;
                    velocity.z = _airHorizontalVelocity.z;
                }
            }

            velocity.y = _verticalVelocity;

            var before = transform.position;
            var flags = controller.Move(velocity * deltaSeconds);
            _worldVelocity = (transform.position - before) / deltaSeconds;

            if (_airborne && (flags & CollisionFlags.Sides) != 0)
            {
                // Hit something in mid-air: the hop stops pushing.
                //
                // This is the second half of keeping §12's geometry meaningful, and it was
                // found by measurement rather than by reasoning. Zeroing the step offset
                // for the duration of the hop is not enough on its own, because a capsule
                // has a rounded bottom: a player pressed against a 0.58 m crate who jumps
                // 0.35 m gets the bottom sphere level with the crate's top edge, and
                // collide-and-slide then carries them up and over it. Measured — they
                // arrived on top of the box and jumped again from there. Dropping the
                // frozen velocity on contact means a hop into a ledge is a hop into a
                // ledge: the player stops and falls, which is what §12 assumes about
                // anything taller than a step.
                _airHorizontalVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Clears <c>CharacterController.minMoveDistance</c>.
        /// <para>
        /// Unity ships it at 1 mm and <b>silently discards any step shorter than that</b>,
        /// which turns into a player who does not move at all when the per-frame distance
        /// falls under it. §05's slowest speed used to be 걷기 2.0 m/s; it is now a crouch
        /// at half of that, and §08's 대형 전리품 takes another 30 % off, so the frame rate
        /// at which the game stops responding halves and halves again. It was measured
        /// here rather than reasoned about: at batch-mode frame rates a crouched player
        /// pressing W resolved 1.000 m/s out of the rules and travelled 0.000 m in a
        /// second, every frame, for five thousand frames.
        /// </para>
        /// </summary>
        private static void ConfigureController(CharacterController? controller)
        {
            if (controller != null && controller.minMoveDistance > 0f)
            {
                controller.minMoveDistance = 0f;
            }
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            ConfigureController(_controller);

            if (_input == null)
            {
                _input = GetComponentInChildren<PlayerInputRouter>();
            }

            if (_look == null)
            {
                _look = GetComponentInChildren<PlayerLook>();
            }

            if (_stance == null)
            {
                _stance = GetComponent<PlayerStance>();
            }

            if (_stance == null)
            {
                // Added rather than left null, the same way PlayerFootsteps adds its own
                // AudioSource. Every rig in the game is assembled by
                // PlayerFeelHarnessMenu.BuildRig, which wires one — but a rig is also
                // *saved into a scene*, and a scene written before this component existed
                // is a player with no crouch and no jump, in a build, with 617 green
                // tests. That is the exact shape of the defect that hid a broken pickup
                // key for a day, and it costs one AddComponent to make impossible.
                _stance = gameObject.AddComponent<PlayerStance>();
            }
        }

        private void Reset()
        {
            _input = GetComponentInChildren<PlayerInputRouter>();
            _look = GetComponentInChildren<PlayerLook>();
            _stance = GetComponent<PlayerStance>();
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
            // The load term is now the stance alone. It used to be
            // Inventory.SpeedMultiplier × stance, where the inventory term was
            // CarryLoad.Resolve(weight, bag) — §08's carry-weight bands. With nothing to
            // carry that factor is 1 at every moment of every race, so the stance is the
            // whole multiplier and 웅크리기 is the only thing that slows a runner down.
            //
            // The composition rule it rode in on is unchanged and is why the crouch is
            // still multiplied in here rather than branched around: §08 stated it for
            // every penalty at once — "§05 배율에 곱연산으로 적용된다" — and
            // SpeedResolver.ContextMultiplier is exactly that product. A player crouching
            // backwards still pays 후진's 65 % on top.
            //
            // The carryingObjective and bagEquipped arguments are gone rather than passed
            // false: MovementContext no longer has the fields. Stance is the only thing
            // left that can make one runner slower than another.
            return new MovementContext(GameConstants.WalkSpeed, StanceMultiplier());
        }

        /// <summary>§05's stance multiplier, or the identity on a rig with no stance component.</summary>
        private float StanceMultiplier()
        {
            return _stance != null ? _stance.SpeedMultiplier : 1f;
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
}
