#nullable enable

using HorrorGame.Core.Light;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// The `F` key of §05, and the spot light behind it.
    /// <para>
    /// §05 makes the beam a <b>pointing device</b>: "근접 음성과 결합하면 '저기! 저기 봐!'
    /// + 손전등으로 비추는 동작이 말과 빛으로 동시에 위치를 전달한다 … 따라서 카메라 회전을
    /// 네트워크로 동기화해야 한다 — 남의 손전등 방향이 정보다." That one sentence decides
    /// the two non-obvious things this component does.
    /// </para>
    /// <para>
    /// First, the beam takes its <em>position</em> from the rig's <c>FlashlightMount</c>
    /// bone but its <em>rotation</em> from <see cref="PlayerLook"/>. If the hand animation
    /// aimed the light, the beam would sway on its own and a teammate reading it would be
    /// reading the animator rather than the player. §03's constraint — the answer has to
    /// be spoken and pointed at, not shared — only works if pointing is precise.
    /// </para>
    /// <para>
    /// Second, all the state is <see cref="FlashlightState"/>'s. §03 charges "시간 경과 +
    /// 켤 때마다" and puts the round trip on that clock; a component that flipped a
    /// <c>Light.enabled</c> and drained a float of its own would be a second, disagreeing
    /// copy of the rule that decides when a team has to walk back to the surface.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(20)]
    public sealed class PlayerFlashlight : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField]
        private PlayerInputRouter? _input;

        [SerializeField]
        private PlayerLook? _look;

        [SerializeField]
        private PlayerLoadout? _loadout;

        [Tooltip("The spot light. Left empty, found in children.")]
        [SerializeField]
        private Light? _spot;

        [Tooltip("Rig root searched for FlashlightMount. Left empty, this transform.")]
        [SerializeField]
        private Transform? _rigRoot;

        [Header("Presentation")]
        [Tooltip("Beam brightness. A look value: §03's numbers are reach, cone and battery, not lumens. "
            + "Defaults to HorrorGame.Rendering.FlashlightBeam, which is also what the review "
            + "screenshots render — change it here and the shots stop describing the game.")]
        [SerializeField]
        private float _intensity = HorrorGame.Rendering.FlashlightBeam.Intensity;

        [SerializeField]
        private Color _colour = new Color(1f, 0.96f, 0.87f);

        private readonly BatteryState _battery = new BatteryState();
        private FlashlightState? _state;
        private Transform? _mount;

        /// <summary>
        /// The rules object: on/off, the cell behind it, and §08's upgrade. Everything that
        /// needs to know whether this player is visible or can read a clue asks this, not
        /// the <see cref="Light"/>.
        /// </summary>
        public FlashlightState State
        {
            get
            {
                if (_state == null)
                {
                    _state = new FlashlightState(_battery);
                }

                return _state;
            }
        }

        /// <summary>
        /// §07's time-of-night reach penalty, pushed in as a float so this component never
        /// imports the threat system (ARCHITECTURE §3). 1 until 심야, then 0.7.
        /// </summary>
        public float TierRangeMultiplier { get; set; } = 1f;

        /// <summary>
        /// The beam as geometry for the clue and perception queries: eye position, look
        /// direction including pitch, §08's reach and §03's half-angle.
        /// <see cref="LightCone.None"/> whenever the light is not actually emitting.
        /// </summary>
        public LightCone Cone
        {
            get
            {
                var origin = _mount != null ? _mount.position : transform.position;
                var aim = _look != null ? _look.AimDirection : transform.forward.ToVec3();
                return State.ConeAt(origin.ToVec3(), aim, TierRangeMultiplier);
            }
        }

        /// <summary>Light is coming out right now. The test for "the monster can see me" (§03).</summary>
        public bool IsLit
        {
            get { return State.IsLit; }
        }

        private void Reset()
        {
            _input = GetComponentInChildren<PlayerInputRouter>();
            _look = GetComponentInChildren<PlayerLook>();
            _loadout = GetComponentInChildren<PlayerLoadout>();
            _spot = GetComponentInChildren<Light>();
            _rigRoot = transform;
        }

        private void Awake()
        {
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

            if (_spot == null)
            {
                _spot = GetComponentInChildren<Light>();
            }

            if (_rigRoot == null)
            {
                _rigRoot = transform;
            }

            _mount = PlayerRigBones.Find(_rigRoot, PlayerRigBones.FlashlightMount);

            if (_spot != null)
            {
                _spot.type = LightType.Spot;
                _spot.color = _colour;
                _spot.intensity = _intensity;
                _spot.shadows = LightShadows.Hard;
                _spot.enabled = false;
            }
        }

        private void Update()
        {
            var carryingObjective = _loadout != null && _loadout.CarryingObjective;

            if (carryingObjective)
            {
                // §03: "양손을 쓴다 · 손전등을 들 수 없다 → 누군가 비춰줘야 한다." The light
                // is taken away rather than the key ignored, so the carrier goes dark the
                // instant they lift it and the escort has to be arranged, not assumed.
                State.TurnOff();
            }
            else if (_input != null && _input.FlashlightToggled)
            {
                State.Toggle();
            }

            State.Tick(Time.deltaTime);
            ApplyToLight();
        }

        private void LateUpdate()
        {
            // After the animator has posed the hand this frame, for the same reason the
            // camera follows the head in LateUpdate: a beam a frame behind the body is a
            // beam that points somewhere the player did not point.
            if (_spot == null)
            {
                return;
            }

            if (_mount != null)
            {
                _spot.transform.position = _mount.position;
            }

            if (_look != null)
            {
                _spot.transform.rotation = _look.AimRotation;
            }
        }

        private void ApplyToLight()
        {
            if (_spot == null)
            {
                return;
            }

            var state = State;
            _spot.enabled = state.IsLit;

            if (!state.IsLit)
            {
                return;
            }

            // FlashlightState.HalfAngleDegrees is the half-angle §03 calls "빛이 좁다";
            // Unity's spotAngle is the full cone, hence the doubling. Getting this wrong
            // halves or doubles the readable envelope of every clue in the game.
            _spot.spotAngle = state.HalfAngleDegrees * 2f;
            _spot.range = state.RangeFor(TierRangeMultiplier);

            // Zero inner angle so brightness falls off from the axis outwards, which is the
            // shape LightCone.QualityAt models. A hard-edged cone would let a player read a
            // clue at the rim exactly as well as dead centre, and §03 wants aiming to matter.
            _spot.innerSpotAngle = 0f;
        }
    }
}
