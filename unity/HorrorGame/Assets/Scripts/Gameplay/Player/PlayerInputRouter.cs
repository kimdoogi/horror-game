#nullable enable

using HorrorGame.Core.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// §05's control scheme, turned into the one struct the rules understand.
    /// <para>
    /// §05's table is short — 마우스 for the view, W/S/A/D relative to it, Shift to run,
    /// F for the flashlight — and this component is the only place in the game that
    /// knows a key is involved. Everything downstream reads a
    /// <see cref="MoveInput"/>, which is exactly what the Mirror layer will deserialise
    /// from a client, so local play and networked play feed the same rules through the
    /// same door (ARCHITECTURE §3, §4).
    /// </para>
    /// <para>
    /// Mouse look and stick look are separate actions on purpose. A mouse reports a
    /// delta that is already the movement since the last frame, so multiplying it by
    /// <see cref="Time.deltaTime"/> would make sensitivity depend on frame rate — the
    /// classic bug that makes a controller feel different on two machines. A stick
    /// reports a position, which must be integrated over time. One action for both
    /// would have to guess which it is holding.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInputRouter : MonoBehaviour
    {
        /// <summary>Resources path of the shipped action asset, so a rig with nothing wired still has §05's scheme.</summary>
        public const string DefaultAssetResourcePath = "PlayerControls";

        private const string PlayerMapName = "Player";
        private const string MoveActionName = "Move";
        private const string LookActionName = "Look";
        private const string LookStickActionName = "LookStick";
        private const string SprintActionName = "Sprint";
        private const string FlashlightActionName = "Flashlight";

        [Header("Bindings")]
        [Tooltip("§05's scheme. Left empty, the copy under Gameplay/Player/Resources is loaded.")]
        [SerializeField]
        private InputActionAsset? _actions;

        [Header("Look sensitivity")]
        [Tooltip("Degrees of yaw per unit of mouse delta. A comfort preference, not a §05 balance value — "
                 + "unlike FOV, turning speed does not change what a player can see at once.")]
        [SerializeField]
        private float _mouseDegreesPerCount = 0.06f;

        [Tooltip("Degrees per second at full stick deflection.")]
        [SerializeField]
        private float _stickDegreesPerSecond = 220f;

        [Tooltip("Invert the vertical axis. A preference; some people cannot play without it.")]
        [SerializeField]
        private bool _invertLookY;

        [Header("Cursor")]
        [Tooltip("Lock and hide the cursor while this component owns input. Off for the editor harness on a second monitor.")]
        [SerializeField]
        private bool _lockCursor = true;

        private InputActionAsset? _runtimeActions;
        private InputAction? _move;
        private InputAction? _look;
        private InputAction? _lookStick;
        private InputAction? _sprint;
        private InputAction? _flashlight;

        private MoveInput _moveInput;
        private Vector2 _lookDeltaDegrees;
        private bool _flashlightToggled;

        /// <summary>
        /// This frame's movement intent, already sanitised. Camera-local: §05 makes the
        /// mouse the definition of forward, so nothing here knows a world direction.
        /// </summary>
        public MoveInput CurrentMove
        {
            get { return _moveInput; }
        }

        /// <summary>
        /// Yaw and pitch to apply this frame, in degrees, sensitivity already folded in.
        /// X is yaw, Y is pitch with up positive.
        /// </summary>
        public Vector2 LookDeltaDegrees
        {
            get { return _lookDeltaDegrees; }
        }

        /// <summary>`F` was pressed this frame. An edge, not a level — §03 charges per switch-on.</summary>
        public bool FlashlightToggled
        {
            get { return _flashlightToggled; }
        }

        /// <summary>Shift is down. Whether it buys 달리기 or 질주 is <see cref="SpeedResolver.SelectBaseSpeed"/>'s call.</summary>
        public bool SprintHeld
        {
            get { return _moveInput.SprintHeld; }
        }

        /// <summary>
        /// Suppresses every input without disabling the actions, so a paused or dead
        /// player stops moving but the bindings stay live for a menu.
        /// </summary>
        public bool InputSuppressed { get; set; }

        /// <summary>
        /// Whether the cursor should be captured. Setting it applies immediately, which
        /// is what a pause menu wants.
        /// </summary>
        public bool LockCursor
        {
            get { return _lockCursor; }
            set
            {
                _lockCursor = value;
                ApplyCursorState();
            }
        }

        private void Awake()
        {
            var source = _actions != null ? _actions : Resources.Load<InputActionAsset>(DefaultAssetResourcePath);
            if (source == null)
            {
                Debug.LogError(
                    "[Player] No InputActionAsset. Expected one assigned in the inspector or at "
                    + "Resources/" + DefaultAssetResourcePath + ". §05's control scheme is not optional.",
                    this);
                return;
            }

            // Cloned rather than used directly: enabling the shared asset would tie every
            // player object in the process to one set of action states, which is wrong the
            // moment a host runs a server and a client in the same editor session.
            _runtimeActions = Instantiate(source);
            _runtimeActions.name = source.name;

            var map = _runtimeActions.FindActionMap(PlayerMapName, false);
            if (map == null)
            {
                Debug.LogError(
                    "[Player] Action map '" + PlayerMapName + "' missing from " + _runtimeActions.name + ".",
                    this);
                return;
            }

            _move = map.FindAction(MoveActionName, false);
            _look = map.FindAction(LookActionName, false);
            _lookStick = map.FindAction(LookStickActionName, false);
            _sprint = map.FindAction(SprintActionName, false);
            _flashlight = map.FindAction(FlashlightActionName, false);

            WarnIfMissing(_move, MoveActionName);
            WarnIfMissing(_look, LookActionName);
            WarnIfMissing(_sprint, SprintActionName);
            WarnIfMissing(_flashlight, FlashlightActionName);
        }

        private void OnEnable()
        {
            _runtimeActions?.Enable();
            ApplyCursorState();
        }

        private void OnDisable()
        {
            _runtimeActions?.Disable();
            _moveInput = new MoveInput(0f, 0f);
            _lookDeltaDegrees = Vector2.zero;
            _flashlightToggled = false;
        }

        private void OnDestroy()
        {
            if (_runtimeActions != null)
            {
                Destroy(_runtimeActions);
            }
        }

        private void Update()
        {
            if (InputSuppressed)
            {
                _moveInput = new MoveInput(0f, 0f);
                _lookDeltaDegrees = Vector2.zero;
                _flashlightToggled = false;
                return;
            }

            var move = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;

            // Sanitized because a stick, a remapped binding or (later) a network client
            // can all deliver something outside -1..1 (§13: the host checks, it does not
            // trust). Doing it here means every reader downstream gets a clean struct.
            _moveInput = new MoveInput(move.y, move.x, _sprint != null && _sprint.IsPressed()).Sanitized;

            var mouse = _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;
            var stick = _lookStick != null ? _lookStick.ReadValue<Vector2>() : Vector2.zero;

            var yaw = (mouse.x * _mouseDegreesPerCount) + (stick.x * _stickDegreesPerSecond * Time.deltaTime);
            var pitch = (mouse.y * _mouseDegreesPerCount) + (stick.y * _stickDegreesPerSecond * Time.deltaTime);

            _lookDeltaDegrees = new Vector2(yaw, _invertLookY ? -pitch : pitch);
            _flashlightToggled = _flashlight != null && _flashlight.WasPressedThisFrame();
        }

        private void ApplyCursorState()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            Cursor.lockState = _lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_lockCursor;
        }

        private void WarnIfMissing(InputAction? action, string actionName)
        {
            if (action == null)
            {
                Debug.LogWarning(
                    "[Player] Action '" + actionName + "' missing from the '" + PlayerMapName
                    + "' map; that part of §05's scheme is dead.", this);
            }
        }
    }
}
