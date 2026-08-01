#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Math;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// The first-person camera: where the eye sits on the rig, and how much of the world
    /// it can see at once.
    /// <para>
    /// §05 states plainly that FOV is a balance item, not a comfort slider: "60~70 …
    /// 곁눈질이 거의 안 됨 / 75~85 ✅ / 95+ 곁눈질이 너무 쉬워 딜레마가 약화된다. 기본 80,
    /// 조절 범위 70~90." Fixing it produces motion-sickness complaints; opening it fully
    /// hands an advantage to whoever sets it widest, because a wider view means the 45°
    /// peek reveals the monster at a smaller — and therefore cheaper — turn. The clamp is
    /// the compromise, so it is enforced here rather than trusted to a settings screen.
    /// </para>
    /// <para>
    /// The eye position comes from the rig's <c>HeadCameraAnchor</c> bone rather than from
    /// a height constant, so the camera follows whatever the animation is doing to the
    /// head. Only the <em>position</em> is taken: rotation stays with
    /// <see cref="PlayerLook"/>, because an animation that rotated the view would be an
    /// animation that aimed the flashlight, and §05 makes beam direction information the
    /// player is responsible for.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        [Tooltip("The first-person camera. Left empty, found in children.")]
        [SerializeField]
        private Camera? _camera;

        [Tooltip("Receives pitch from PlayerLook. Left empty, the camera's parent is used.")]
        [SerializeField]
        private Transform? _pitchPivot;

        [Tooltip("Rig root to search for HeadCameraAnchor. Left empty, this transform.")]
        [SerializeField]
        private Transform? _rigRoot;

        [Tooltip("Follow the rig's HeadCameraAnchor bone. Off pins the eye to the pivot's authored height.")]
        [SerializeField]
        private bool _followHeadAnchor = true;

        [Tooltip("§05: default 80, clamped 70-90. Wider makes the 45 degree peek cheap and weakens the dilemma.")]
        [SerializeField]
        [Range(GameConstants.FovMin, GameConstants.FovMax)]
        private float _fieldOfView = GameConstants.FovDefault;

        private Transform? _headAnchor;
        private bool _resolved;

        /// <summary>
        /// Vertical field of view in degrees, always inside §05's 70–90 window. Assigning
        /// outside it clamps rather than throws: the value arrives from a settings slider
        /// and from saved preferences, and neither is worth crashing over.
        /// </summary>
        public float FieldOfView
        {
            get { return _fieldOfView; }
            set
            {
                _fieldOfView = ClampFov(value);
                ApplyFov();
            }
        }

        /// <summary>The camera this rig owns, once resolved.</summary>
        public Camera? FirstPersonCamera
        {
            get { return _camera; }
        }

        /// <summary>The transform carrying pitch, for <see cref="PlayerLook"/> to drive.</summary>
        public Transform? PitchPivot
        {
            get { return _pitchPivot; }
        }

        /// <summary>The eye position in world space — the origin of §03's light cone and of every clue read.</summary>
        public Vec3 EyePosition
        {
            get { return (_camera != null ? _camera.transform.position : transform.position).ToVec3(); }
        }

        /// <summary>
        /// §05's clamp as a function, so a settings screen can show the range without
        /// duplicating the numbers.
        /// </summary>
        /// <param name="fov">Requested vertical FOV in degrees; NaN falls back to the default.</param>
        public static float ClampFov(float fov)
        {
            if (float.IsNaN(fov))
            {
                return GameConstants.FovDefault;
            }

            return MathX.Clamp(fov, GameConstants.FovMin, GameConstants.FovMax);
        }

        private void Reset()
        {
            _camera = GetComponentInChildren<Camera>();
            _rigRoot = transform;
        }

        private void Awake()
        {
            ResolveWiring();
            ApplyFov();
        }

        /// <summary>
        /// Finds the camera, the pivot and the head bone. Idempotent, and called from
        /// <see cref="SnapToHeadAnchor"/> as well as from <c>Awake</c> because a capture
        /// rig assembles this component outside play mode, where <c>Awake</c> never runs.
        /// </summary>
        private void ResolveWiring()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;

            if (_camera == null)
            {
                _camera = GetComponentInChildren<Camera>();
            }

            if (_pitchPivot == null && _camera != null)
            {
                _pitchPivot = _camera.transform.parent != null ? _camera.transform.parent : _camera.transform;
            }

            if (_rigRoot == null)
            {
                _rigRoot = transform;
            }

            if (_followHeadAnchor)
            {
                _headAnchor = PlayerRigBones.Find(_rigRoot, PlayerRigBones.HeadCameraAnchor);
                if (_headAnchor == null)
                {
                    // Not an error: the feel harness and every automated test run on a
                    // capsule with no rig at all, and they still need a working eye.
                    Debug.Log(
                        "[Player] No " + PlayerRigBones.HeadCameraAnchor
                        + " under the rig; the camera stays at its authored local position.",
                        this);
                }
            }
        }

        private void OnValidate()
        {
            _fieldOfView = ClampFov(_fieldOfView);
            ApplyFov();
        }

        private void LateUpdate()
        {
            // LateUpdate, after the animation has posed the head for this frame. Reading
            // the bone in Update would show the previous frame's pose, which reads as the
            // camera lagging the body by exactly one frame.
            SnapToHeadAnchor();
        }

        /// <summary>
        /// Puts the eye on the rig's <c>HeadCameraAnchor</c> for the pose the rig is in
        /// right now. What <c>LateUpdate</c> does every frame.
        /// <para>
        /// Public so a capture rig can photograph the eye the player actually gets. A shot
        /// tool runs outside play mode, where no <c>LateUpdate</c> fires, and one that
        /// left the camera at the pivot's authored height would be photographing a
        /// different camera from the game's — 7 cm higher in a walk, 39 cm in a crouch,
        /// and the hands would sit somewhere else in the frame in every shot.
        /// </para>
        /// </summary>
        public void SnapToHeadAnchor()
        {
            ResolveWiring();

            if (_headAnchor == null || _pitchPivot == null)
            {
                return;
            }

            _pitchPivot.position = _headAnchor.position;
        }

        private void ApplyFov()
        {
            if (_camera == null)
            {
                return;
            }

            _camera.usePhysicalProperties = false;
            _camera.fieldOfView = _fieldOfView;
        }
    }
}
