#nullable enable

using HorrorGame.Audio;
using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// The two verbs §05's control table does not have: 웅크리기 and a hop.
    /// <para>
    /// <b>Why they live here and not in <see cref="PlayerMotor"/>.</b> The motor's own
    /// remarks say it "computes no multiplier of its own; it collects state, asks the
    /// core, and applies the answer". Crouching is state, and the shape of the body is
    /// state — collider height, headroom, a cooldown, whether the feet are on the floor
    /// because the player chose to leave it. This component owns all of it and hands the
    /// motor two numbers and two booleans, which is the same seam
    /// <see cref="PlayerLoadout"/> already uses for §08's weight.
    /// </para>
    /// <para>
    /// <b>Crouch is a toggle, not a hold, and that is a decision about §12 rather than
    /// about convention.</b> Hold is the horror default and it is the right default when
    /// a crouch lasts a few seconds. It does not last a few seconds here: §12 requires
    /// 은폐 지점 near the 출입구 for §07's 새벽 stage — the tier where the monster knows
    /// where the exit is — and the useful play is to sit in one and wait, or to cross a
    /// whole zone of a 100 × 100 m map without giving §04's channel anything to read.
    /// §05 already refuses to freeze a player's hands for §04's Observer on exactly this
    /// reasoning ("화면이 얼면 조작감 최악"), and a key held down for a minute while the
    /// other hand works W, A, S, D and the mouse is the same complaint. It is rebindable
    /// like everything else on the keyboard scheme, so a player who wants hold can bind
    /// it next to Shift and hold it.
    /// </para>
    /// <para>
    /// <b>Standing up is a request, not a command.</b> §12 builds ducts, low openings and
    /// hiding places; a stand that clipped the player through a ceiling would make every
    /// one of those a place to escape geometry from. <see cref="TryStand"/> sweeps the
    /// standing capsule first and refuses when anything is in it, so the player stays
    /// crouched and the toggle simply did not take.
    /// </para>
    /// <para>
    /// <b>The jump is bounded rather than tuned.</b>
    /// <see cref="GameConstants.JumpApexMetres"/> is below
    /// <see cref="GameConstants.PlayerStepOffsetMetres"/>, so anything it can reach is
    /// something the controller already walks onto — the constant's own remarks carry the
    /// argument. This component adds the two properties that keep it there: no air
    /// control at all (the horizontal velocity at take-off is the horizontal velocity for
    /// the whole hop) and a cooldown longer than the hop itself.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerStance : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Supplies the crouch and jump edges. Left empty, found on this object.")]
        [SerializeField]
        private PlayerInputRouter? _input;

        [Header("Headroom")]
        [Tooltip("What counts as a ceiling. Anything under the player's own hierarchy is ignored regardless.")]
        [SerializeField]
        private LayerMask _headroomMask = ~0;

        [Header("Feel")]
        [Tooltip("Seconds for the crouch blend the camera and the animator ride. The collider itself snaps.")]
        [SerializeField]
        private float _blendSeconds = 0.12f;

        /// <summary>
        /// Reused overlap buffer for the headroom sweep. Eight is generous for a body's
        /// worth of capsule, and the sweep runs on every stand attempt.
        /// </summary>
        private readonly Collider[] _overlaps = new Collider[8];

        private CharacterController? _controller;
        private NoiseMeter? _noise;

        private float _standingHeight = ViewMotionTuning.RigHeightMetres;
        private Vector3 _standingCenter = new Vector3(0f, ViewMotionTuning.RigHeightMetres * 0.5f, 0f);
        private float _groundedStepOffset = GameConstants.PlayerStepOffsetMetres;
        private bool _capturedStanding;

        private bool _crouched;
        private float _crouch01;
        private bool _jumpQueued;
        private float _cooldownRemaining;
        private bool _airborne;

        /// <summary>Crouched right now. What §05's other three players see, and what the speed and noise multipliers are keyed off.</summary>
        public bool IsCrouched
        {
            get { return _crouched; }
        }

        /// <summary>
        /// How far into the crouch the <em>presentation</em> is, 0–1. The collider and
        /// both multipliers snap with <see cref="IsCrouched"/>; this is the smoothed
        /// value the camera settle and the animator blend ride, so the rules never wait
        /// on an animation.
        /// </summary>
        public float Crouch01
        {
            get { return _crouch01; }
        }

        /// <summary>
        /// True from take-off until the feet are back on something. Set by
        /// <see cref="PlayerMotor"/>, which is the only thing that knows.
        /// </summary>
        public bool Airborne
        {
            get { return _airborne; }
        }

        /// <summary>Seconds until another jump is allowed; zero when one is.</summary>
        public float JumpCooldownRemaining
        {
            get { return _cooldownRemaining; }
        }

        /// <summary>
        /// §05's multiplier for the stance, to be composed into the same product §08's
        /// weight is — <see cref="PlayerMotor"/> multiplies it into the context's load
        /// term, because §08 states the composition rule for all of them at once:
        /// "§05 배율에 곱연산으로 적용된다."
        /// </summary>
        public float SpeedMultiplier
        {
            get { return _crouched ? GameConstants.CrouchSpeedMultiplier : 1f; }
        }

        /// <summary>
        /// §04's multiplier for the stance, applied to the continuous movement term of
        /// the player's own noise. 1 standing, <see cref="GameConstants.CrouchNoiseMultiplier"/>
        /// crouched. Transients — a door, a landing — are not scaled by it.
        /// </summary>
        public float NoiseMultiplier
        {
            get { return _crouched ? GameConstants.CrouchNoiseMultiplier : 1f; }
        }

        /// <summary>
        /// Take-off speed for a jump, m/s, derived from the gravity actually in force so
        /// that <see cref="GameConstants.JumpApexMetres"/> is the apex whatever a project
        /// setting says. §12's constraint is stated as a height, not as an impulse.
        /// </summary>
        public float TakeoffSpeed
        {
            get
            {
                var gravity = Mathf.Abs(Physics.gravity.y);
                if (gravity <= 0f)
                {
                    gravity = GameConstants.JumpGravity;
                }

                return Mathf.Sqrt(2f * gravity * GameConstants.JumpApexMetres);
            }
        }

        /// <summary>The standing capsule height this rig was built with, metres.</summary>
        public float StandingHeight
        {
            get
            {
                CaptureStanding();
                return _standingHeight;
            }
        }

        /// <summary>The crouched capsule height, metres. §12 — sized off the Crouch clip; see <see cref="GameConstants.CrouchHeightFraction"/>.</summary>
        public float CrouchedHeight
        {
            get { return StandingHeight * GameConstants.CrouchHeightFraction; }
        }

        /// <summary>
        /// Crouches. Always succeeds — there is nothing above a shrinking capsule that
        /// can refuse it.
        /// </summary>
        public void Crouch()
        {
            if (_crouched)
            {
                return;
            }

            _crouched = true;
            ApplyCapsule();
        }

        /// <summary>
        /// Stands up if there is room to.
        /// <para>
        /// The sweep is the point of the method. §12's ducts, gantry undersides and
        /// hiding places are all spaces a standing capsule does not fit in, and a
        /// <c>CharacterController</c> whose height is raised inside geometry is pushed
        /// out of it by PhysX on the next move — through the ceiling, onto the floor
        /// above, out of §12's map. So the capsule is only resized once its own volume
        /// has been measured empty.
        /// </para>
        /// </summary>
        /// <returns>False when something is in the way and the player is still crouched.</returns>
        public bool TryStand()
        {
            if (!_crouched)
            {
                return true;
            }

            if (!HasHeadroom())
            {
                return false;
            }

            _crouched = false;
            ApplyCapsule();
            return true;
        }

        /// <summary>Toggles, refusing to stand under a ceiling. What the key does.</summary>
        /// <returns>The stance after the toggle: true crouched.</returns>
        public bool Toggle()
        {
            if (_crouched)
            {
                TryStand();
            }
            else
            {
                Crouch();
            }

            return _crouched;
        }

        /// <summary>
        /// Whether the standing capsule would fit where the player is right now.
        /// Public because a hiding spot and the test suite both want to ask without
        /// changing anything.
        /// </summary>
        public bool HasHeadroom()
        {
            var controller = Controller;
            if (controller == null)
            {
                return true;
            }

            CaptureStanding();

            // Only the volume the body would *newly* occupy: from the top of the crouched
            // capsule to the top of the standing one. Sweeping the whole standing capsule
            // instead would put its bottom sphere a centimetre off the floor and make
            // every stand a coin toss against the controller's own skin width — the floor
            // is not a ceiling and must never be able to read as one.
            var radius = Mathf.Max(0.01f, controller.radius - 0.01f);
            var feet = transform.TransformPoint(new Vector3(
                _standingCenter.x,
                controller.center.y - (controller.height * 0.5f),
                _standingCenter.z));

            var lower = CrouchedHeight - controller.radius;
            var upper = _standingHeight - controller.radius;
            if (upper <= lower)
            {
                return true;
            }

            var bottom = feet + (Vector3.up * lower);
            var top = feet + (Vector3.up * upper);

            var count = Physics.OverlapCapsuleNonAlloc(
                bottom, top, radius, _overlaps, _headroomMask, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                var hit = _overlaps[i];
                if (hit == null || hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Asks for a jump. Held rather than acted on: only <see cref="PlayerMotor"/>
        /// knows whether the feet are on the floor, and only it owns the vertical
        /// velocity.
        /// <para>
        /// Pressing it while crouched stands the player up instead, if there is room.
        /// The key is therefore never dead, and a player who crouched into a duct and
        /// hammered it does not launch themselves into the ceiling.
        /// </para>
        /// </summary>
        public void RequestJump()
        {
            if (_crouched)
            {
                TryStand();
                return;
            }

            if (_cooldownRemaining > 0f)
            {
                return;
            }

            _jumpQueued = true;
        }

        /// <summary>
        /// Takes a queued jump, once. Called by <see cref="PlayerMotor"/> on a grounded
        /// step; starts the cooldown and clears the request either way.
        /// </summary>
        /// <returns>True when this step should launch.</returns>
        public bool ConsumeJump()
        {
            if (!_jumpQueued)
            {
                return false;
            }

            _jumpQueued = false;

            if (_crouched || _cooldownRemaining > 0f)
            {
                return false;
            }

            _cooldownRemaining = GameConstants.JumpCooldownSeconds;
            _airborne = true;
            ApplyStepOffset();
            return true;
        }

        /// <summary>
        /// The feet are back on something. Raises §04's landing transient on the same
        /// meter the crouch quietens, so the two are one channel with opposite signs.
        /// </summary>
        /// <param name="descentMetresPerSecond">How fast the body was falling; below the ignore threshold nothing is raised.</param>
        public void NotifyLanded(float descentMetresPerSecond)
        {
            var wasAirborne = _airborne;
            _airborne = false;
            ApplyStepOffset();

            if (!wasAirborne && descentMetresPerSecond < ViewMotionTuning.LandingIgnoredBelowMps)
            {
                // Walking down a §12 stair does not land. PlayerMotor keeps a step of
                // gravity pressed into the floor, so this is the difference between
                // arriving and simply being held down.
                return;
            }

            ResolveNoise();
            _noise?.AddTransient(GameConstants.PlayerLandingNoiseLevel);
        }

        /// <summary>
        /// Steps the cooldown and the presentation blend, and pushes §04's stance noise.
        /// Explicit delta for the same reason <c>PlayerMotor.Step</c> takes one: §13's
        /// host drives this at <see cref="GameConstants.FixedStep"/>.
        /// </summary>
        /// <param name="deltaSeconds">Step length. Zero or negative does nothing.</param>
        public void Tick(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || deltaSeconds <= 0f)
            {
                return;
            }

            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining -= deltaSeconds;
                if (_cooldownRemaining < 0f)
                {
                    _cooldownRemaining = 0f;
                }
            }

            var target = _crouched ? 1f : 0f;
            if (_blendSeconds <= 0f)
            {
                _crouch01 = target;
            }
            else
            {
                _crouch01 = target + ((_crouch01 - target) * Mathf.Exp(-deltaSeconds / _blendSeconds));
                if (Mathf.Abs(_crouch01 - target) < 0.001f)
                {
                    _crouch01 = target;
                }
            }

            ResolveNoise();
            if (_noise != null)
            {
                _noise.StanceNoiseMultiplier = NoiseMultiplier;
            }
        }

        /// <summary>
        /// Puts the body back upright without a sweep and clears every accumulator. For
        /// §09's respawn and for a teleport, where the geometry the sweep would measure
        /// is not the geometry the player is about to be in.
        /// </summary>
        public void ResetStance()
        {
            _crouched = false;
            _crouch01 = 0f;
            _jumpQueued = false;
            _cooldownRemaining = 0f;
            _airborne = false;
            ApplyCapsule();
            ApplyStepOffset();

            ResolveNoise();
            if (_noise != null)
            {
                _noise.StanceNoiseMultiplier = 1f;
            }
        }

        private CharacterController? Controller
        {
            get
            {
                if (_controller == null)
                {
                    // Resolved on first use rather than only in Awake, for the reason
                    // PlayerMotor spells out: editor tooling assembles a rig and steps it
                    // without ever entering play mode, and Unity does not run Awake then.
                    _controller = GetComponent<CharacterController>();
                }

                return _controller;
            }
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            CaptureStanding();

            if (_input == null)
            {
                _input = GetComponentInChildren<PlayerInputRouter>();
            }

            ResolveNoise();
        }

        private void Reset()
        {
            _input = GetComponentInChildren<PlayerInputRouter>();
        }

        private void OnDisable()
        {
            // A rig that is switched off mid-crouch must not leave the mix and §04's feed
            // believing it is still down there.
            if (_noise != null)
            {
                _noise.StanceNoiseMultiplier = 1f;
            }
        }

        private void Update()
        {
            if (_input != null)
            {
                if (_input.CrouchToggled)
                {
                    Toggle();
                }

                if (_input.JumpPressed)
                {
                    RequestJump();
                }
            }

            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Remembers the capsule the rig was built with, once. Everything else is derived
        /// from it, so a rig assembled at a different height crouches proportionally
        /// rather than to a number written here.
        /// </summary>
        private void CaptureStanding()
        {
            if (_capturedStanding)
            {
                return;
            }

            var controller = _controller != null ? _controller : GetComponent<CharacterController>();
            if (controller == null)
            {
                return;
            }

            _capturedStanding = true;
            _standingHeight = controller.height;
            _standingCenter = controller.center;
            _groundedStepOffset = controller.stepOffset;
        }

        /// <summary>
        /// Takes the free step away while the feet are off the floor, and gives it back on
        /// landing.
        /// <para>
        /// <b>This is the whole reason the jump is safe, and it was not obvious.</b>
        /// <c>GameConstants.JumpApexMetres</c> is deliberately below
        /// <c>PlayerStepOffsetMetres</c> so that a hop reaches a strict subset of what a
        /// walk reaches — but a <c>CharacterController</c> takes its step wherever it is,
        /// not only on the ground, so the two <em>add</em>. Measured on a 0.58 m box: a
        /// player who jumped and then pushed forward arrived on top of it, 0.35 m of apex
        /// plus 0.40 m of free step, and §12's crates, debris and the 차량's cargo deck
        /// all became climbable. Zeroing the offset for the duration of the hop is what
        /// makes the constant's claim true rather than merely stated.
        /// </para>
        /// </summary>
        private void ApplyStepOffset()
        {
            var controller = Controller;
            if (controller == null)
            {
                return;
            }

            CaptureStanding();
            controller.stepOffset = _airborne ? 0f : _groundedStepOffset;
        }

        private void ApplyCapsule()
        {
            var controller = Controller;
            if (controller == null)
            {
                return;
            }

            CaptureStanding();

            if (!_crouched)
            {
                controller.height = _standingHeight;
                controller.center = _standingCenter;
                return;
            }

            // The centre moves with the height so the feet stay where they are. Scaling
            // the authored centre rather than halving the height keeps a rig whose
            // collider was offset for some other reason offset by the same share.
            var scale = GameConstants.CrouchHeightFraction;
            controller.height = _standingHeight * scale;
            controller.center = new Vector3(
                _standingCenter.x, _standingCenter.y * scale, _standingCenter.z);
        }

        private void ResolveNoise()
        {
            if (_noise == null)
            {
                _noise = GetComponent<NoiseMeter>();
            }
        }
    }
}
