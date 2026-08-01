#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Ghost;
using UnityEngine;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// The dead player's camera. §09's 시야 row — <em>"맵 전체를 자유롭게 본다 (벽
    /// 통과)"</em>.
    /// <para>
    /// <b>Through walls means no collision, and no collision means no physics.</b>
    /// This component has no <c>Rigidbody</c>, no <c>Collider</c>, no
    /// <c>CharacterController</c> and casts no ray: it writes the transform directly.
    /// That mirrors <c>GhostState</c>, which deliberately takes no <c>IWorldProbe</c>
    /// for the same reason — "taking one would imply that geometry could block a
    /// ghost's sight or its signal". A ghost that could be stopped by a wall would
    /// also be a ghost that could be lost in one, and §09's answer to "죽으면
    /// 지루하다" is that there is something to watch.
    /// </para>
    /// <para>
    /// <b>Speed is derived, not tuned.</b> §09 gives the camera no number and
    /// ARCHITECTURE §2 forbids inventing one here, so the ghost drifts at
    /// <see cref="GameConstants.RunSpeed"/> and at
    /// <see cref="GameConstants.RunnerSprintSpeed"/> while boosted. Those are the
    /// speeds the living move at, which makes the ghost able to follow the team it is
    /// watching without ever needing a value of its own.
    /// </para>
    /// <para>
    /// Every move is pushed into <see cref="GhostState.MoveTo"/>, because the core
    /// state is what decides whether a rattle is in range (§09's "근처 물건"). The
    /// camera does not get to have a second opinion about where the ghost is.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostFreeCamera : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Camera to fly. Falls back to the one on this object, then to Camera.main.")]
        private Camera? _camera;

        [SerializeField]
        [Tooltip("Read the legacy input axes directly. For offline testing before the gameplay layer owns input; leave off in a real match.")]
        private bool _selfDriven;

        private GhostState? _ghost;
        private float _yaw;
        private float _pitch;

        /// <summary>The ghost this camera speaks for, or null while the player is alive.</summary>
        public GhostState? Ghost
        {
            get { return _ghost; }
        }

        /// <summary>Whether the camera is currently flying a ghost.</summary>
        public bool IsActive
        {
            get { return _ghost != null; }
        }

        /// <summary>
        /// Takes over as a player dies. Starts at the body, facing the way the player
        /// was — §09's best moment for a ghost is the seconds right after it saw what
        /// killed it, and being spun to a fresh orientation would throw that away.
        /// </summary>
        public void Bind(GhostState ghost, Camera? camera)
        {
            var view = camera != null ? camera : ResolveCamera();
            var from = view != null ? view.transform.position : ghost.DeathPosition.ToUnity();
            var facing = view != null ? view.transform.rotation : Quaternion.identity;
            Bind(ghost, camera, from, facing);
        }

        /// <summary>
        /// Takes over from an explicit eye pose.
        /// <para>
        /// The pose matters and is not <see cref="GhostState.DeathPosition"/>. That
        /// position is the <em>body's</em> — §08 drops the loot on it and the ghost has to
        /// be able to see the pile from outside itself — and it is measured at the feet,
        /// so starting the camera there puts the first frame of §09 inside the floor. The
        /// caller passes the eye the player was already looking through, which is also
        /// what preserves the seconds §09 cares most about: the ones right after they saw
        /// what killed them.
        /// </para>
        /// </summary>
        /// <param name="ghost">The state minted where the player fell.</param>
        /// <param name="camera">Camera to fly. Null keeps whatever this component already resolved.</param>
        /// <param name="eyePosition">Where the view starts, in world space.</param>
        /// <param name="eyeRotation">Which way it starts facing. Roll is discarded — §05 gives the camera none.</param>
        public void Bind(GhostState ghost, Camera? camera, Vector3 eyePosition, Quaternion eyeRotation)
        {
            _ghost = ghost;

            if (camera != null)
            {
                _camera = camera;
            }

            var view = ResolveCamera();
            if (view == null)
            {
                return;
            }

            view.transform.SetPositionAndRotation(eyePosition, eyeRotation);

            var euler = eyeRotation.eulerAngles;
            _yaw = euler.y;
            _pitch = NormalizePitch(euler.x);

            // Flush against a wall is the normal case for something that passes
            // through them, so the near plane is pulled in until it stops mattering.
            view.nearClipPlane = 0.01f;

            // Core owns where the ghost is, and it decides §09's "근처 물건". Pushed here
            // as well as in Drive so a ghost that has not moved yet can still rattle the
            // thing beside the body.
            ghost.MoveTo(eyePosition.ToCore());
        }

        /// <summary>Releases the camera — the match ended, or the ghost is being handed to the end screen.</summary>
        public void Unbind()
        {
            _ghost = null;
        }

        /// <summary>
        /// Applies one frame of movement.
        /// <para>
        /// The gameplay layer owns input devices, so this takes already-resolved
        /// values. <paramref name="move"/> is in the camera's own space — x right, y
        /// up, z forward — which is what lets a ghost rise through a floor by pushing
        /// up rather than by finding a stairwell.
        /// </para>
        /// </summary>
        /// <param name="move">Desired direction, camera-relative. Magnitudes above one are normalised.</param>
        /// <param name="lookDelta">Mouse movement in pixels since the last frame.</param>
        /// <param name="boost">Whether to fly at the Runner's sprint speed instead of a run.</param>
        /// <param name="deltaSeconds">Frame time. Negative or non-finite values are ignored rather than teleporting the view.</param>
        public void Drive(Vector3 move, Vector2 lookDelta, bool boost, float deltaSeconds)
        {
            var ghost = _ghost;
            var view = ResolveCamera();
            if (ghost == null || view == null)
            {
                return;
            }

            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
            {
                return;
            }

            _yaw += lookDelta.x * UiStyle.GhostLookDegreesPerPixel;
            _pitch = Mathf.Clamp(
                _pitch - (lookDelta.y * UiStyle.GhostLookDegreesPerPixel),
                -UiStyle.GhostPitchLimit,
                UiStyle.GhostPitchLimit);

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            view.transform.rotation = rotation;

            if (move.sqrMagnitude > 1f)
            {
                move = move.normalized;
            }

            var speed = boost ? GameConstants.RunnerSprintSpeed : GameConstants.RunSpeed;
            var world = rotation * move;
            var next = view.transform.position + (world * (speed * deltaSeconds));

            view.transform.position = next;

            // Core owns where the ghost is; the transform is only how it is drawn.
            ghost.MoveTo(next.ToCore());
        }

        /// <summary>
        /// Optional legacy-input driving, for offline testing before the gameplay layer
        /// owns the bindings. Off by default and compiled out entirely when the project
        /// is configured for the new input backend, so it can never fight the real
        /// controller for the mouse.
        /// </summary>
        private void Update()
        {
            if (!_selfDriven || _ghost == null)
            {
                return;
            }

#if ENABLE_LEGACY_INPUT_MANAGER
            var move = new Vector3(
                Input.GetAxisRaw("Horizontal"),
                (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f),
                Input.GetAxisRaw("Vertical"));

            var look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            Drive(move, look, Input.GetKey(KeyCode.LeftShift), Time.deltaTime);
#endif
        }

        private Camera? ResolveCamera()
        {
            if (_camera != null)
            {
                return _camera;
            }

            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            return _camera;
        }

        private static float NormalizePitch(float eulerX)
        {
            var pitch = eulerX > 180f ? eulerX - 360f : eulerX;
            return Mathf.Clamp(pitch, -UiStyle.GhostPitchLimit, UiStyle.GhostPitchLimit);
        }
    }
}
