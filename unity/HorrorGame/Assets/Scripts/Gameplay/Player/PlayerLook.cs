#nullable enable

using HorrorGame.Core.Math;
using HorrorGame.Core.Movement;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// The mouse, which §05 makes the definition of "forward".
    /// <para>
    /// §05: "마우스 방향 = 이동 방향. 뒤를 보려고 마우스를 돌리면 이동 기준도 함께
    /// 돌아간다." So yaw is applied to the <em>body</em>, not to a camera hanging off it.
    /// If the camera could turn independently, a player could look behind at no cost and
    /// §05's entire speed table — the one thing §14's question 2 is about — would stop
    /// costing anything.
    /// </para>
    /// <para>
    /// Pitch is applied to a separate pivot and deliberately does <b>not</b> feed
    /// movement: <see cref="SpeedResolver.ResolveVelocity"/> ignores it, because looking
    /// up does not make a player walk into the ceiling. It still matters for two other
    /// things §05 names — it aims the flashlight, and §05 puts it on the wire precisely
    /// because "바닥·천장을 비추는 것도 신호".
    /// </para>
    /// <para>
    /// Look runs every frame at frame rate, not at the fixed step. Rules run on a fixed
    /// step; the camera is not a rule, and a 50 Hz camera on a 144 Hz monitor is the
    /// difference between a game that feels responsive and one that does not (§14 Q1/Q2
    /// are questions about feel).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-90)]
    public sealed class PlayerLook : MonoBehaviour
    {
        [Tooltip("Left empty, found on this object or its children.")]
        [SerializeField]
        private PlayerInputRouter? _input;

        [Tooltip("Transform that receives pitch. Usually the camera pivot; must be a child of this object.")]
        [SerializeField]
        private Transform? _pitchPivot;

        [Tooltip("Vertical limit in degrees. Geometry, not balance: past 90 the view rolls over.")]
        [SerializeField]
        [Range(0f, 89.9f)]
        private float _pitchLimitDegrees = 89f;

        private float _yawDegrees;
        private float _pitchDegrees;

        /// <summary>
        /// Yaw in degrees clockwise from +Z — the convention
        /// <see cref="MathX.DirectionFromYaw"/> and
        /// <see cref="SpeedResolver.ResolveVelocity"/> both use, and the same number §05
        /// requires on the wire.
        /// </summary>
        public float YawDegrees
        {
            get { return _yawDegrees; }
        }

        /// <summary>Pitch in degrees, positive looking down (Unity's sign). §05 syncs this too — it aims the beam.</summary>
        public float PitchDegrees
        {
            get { return _pitchDegrees; }
        }

        /// <summary>
        /// Where the player is looking, as a unit vector including pitch. This is what
        /// <c>FlashlightState.ConeAt</c> wants; movement wants <see cref="YawDegrees"/>.
        /// </summary>
        public Vec3 AimDirection
        {
            get { return (AimRotation * Vector3.forward).ToVec3(); }
        }

        /// <summary>Full look rotation, yaw and pitch. The beam's rotation, not the body's.</summary>
        public Quaternion AimRotation
        {
            get { return Quaternion.Euler(_pitchDegrees, _yawDegrees, 0f); }
        }

        /// <summary>
        /// The child transform that carries pitch. Settable so a rig assembled in code —
        /// the feel harness, a spawner, a test — can hand over its camera pivot without a
        /// prefab existing yet.
        /// </summary>
        public Transform? PitchPivot
        {
            get { return _pitchPivot; }
            set
            {
                _pitchPivot = value;
                Apply();
            }
        }

        /// <summary>
        /// Freezes the view. §05 is explicit that the <em>Observer</em> does not get this
        /// — "이동만 정지, 마우스룩 허용 — 화면이 얼면 조작감 최악" — so this is for death
        /// and menus only. Movement has its own lock on <see cref="PlayerMotor"/>.
        /// </summary>
        public bool LookLocked { get; set; }

        /// <summary>
        /// Points the view at a yaw and pitch without going through mouse input: spawn
        /// placement, and the receiving end of §05's camera-rotation sync.
        /// </summary>
        /// <param name="yawDegrees">Yaw clockwise from +Z.</param>
        /// <param name="pitchDegrees">Pitch, positive down; clamped to the vertical limit.</param>
        public void SetLook(float yawDegrees, float pitchDegrees)
        {
            if (float.IsNaN(yawDegrees) || float.IsInfinity(yawDegrees)
                || float.IsNaN(pitchDegrees) || float.IsInfinity(pitchDegrees))
            {
                return;
            }

            _yawDegrees = MathX.NormalizeAngle(yawDegrees);
            _pitchDegrees = MathX.Clamp(pitchDegrees, -_pitchLimitDegrees, _pitchLimitDegrees);
            Apply();
        }

        private void Reset()
        {
            _input = GetComponentInChildren<PlayerInputRouter>();
        }

        private void Awake()
        {
            if (_input == null)
            {
                _input = GetComponentInChildren<PlayerInputRouter>();
            }

            var euler = transform.rotation.eulerAngles;
            _yawDegrees = MathX.NormalizeAngle(euler.y);

            if (_pitchPivot != null)
            {
                _pitchDegrees = MathX.Clamp(
                    MathX.NormalizeAngle(_pitchPivot.localRotation.eulerAngles.x),
                    -_pitchLimitDegrees,
                    _pitchLimitDegrees);
            }

            Apply();
        }

        private void Update()
        {
            if (LookLocked || _input == null)
            {
                return;
            }

            var delta = _input.LookDeltaDegrees;

            _yawDegrees = MathX.NormalizeAngle(_yawDegrees + delta.x);

            // Unity's X rotation increases downwards, so a mouse pushed forward (positive
            // delta.y) has to decrease pitch for the view to rise.
            _pitchDegrees = MathX.Clamp(_pitchDegrees - delta.y, -_pitchLimitDegrees, _pitchLimitDegrees);

            Apply();
        }

        private void Apply()
        {
            transform.rotation = Quaternion.Euler(0f, _yawDegrees, 0f);

            if (_pitchPivot != null)
            {
                _pitchPivot.localRotation = Quaternion.Euler(_pitchDegrees, 0f, 0f);
            }
        }
    }
}
