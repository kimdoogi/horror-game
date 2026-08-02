#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Map;
using UnityEngine;
using UnityEngine.AI;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// A door a player can pull shut behind them, and the creature has to come through.
    /// <para>
    /// §12 has placed one or two lockable doors on every storey's bottleneck since the
    /// first sketch, and until now they were a validation fact: <c>MapSketch.Door()</c>
    /// marked an edge, <c>MapValidator</c> measured that shutting it would force a detour
    /// worth more than §06's release time, and nothing in the game could shut one.
    /// </para>
    /// <para>
    /// The rule is <see cref="DoorState"/>, in Core and tested. This is the body: a leaf
    /// that swings, a collider that blocks, and a <c>NavMeshObstacle</c> that carves so
    /// the creature's own pathing agrees with what the player can see. Carving rather
    /// than a plain obstacle on purpose — an uncarved obstacle makes the agent slide
    /// along the door looking for a way past, and §06's creature has to arrive at it and
    /// start working.
    /// </para>
    /// <para>
    /// <b>Held, not tapped.</b> §06 leaves a player one currency and it is time, so the
    /// 1.1 s of pulling a door shut is the price: standing still, facing the wrong way,
    /// with the thing that is faster than you getting closer. Letting go loses it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DoorInteractable : Interactable
    {
        [Tooltip("The leaf that swings. Left empty, the first child.")]
        [SerializeField]
        private Transform? _leaf;

        [Tooltip("Degrees the leaf swings open about its own up axis.")]
        [SerializeField]
        private float _openAngle = 96f;

        [Tooltip("Seconds the leaf takes to swing. Cosmetic — DoorState owns what it costs.")]
        [SerializeField]
        private float _swingSeconds = 0.35f;

        [Tooltip("Blocks the player when shut. Left empty, the first collider that is not a trigger.")]
        [SerializeField]
        private Collider? _blocker;

        [Tooltip("Carves the NavMesh when shut, so §06's pathing agrees with the geometry.")]
        [SerializeField]
        private NavMeshObstacle? _obstacle;

        private DoorState _state;
        private float _swing;
        private Quaternion _shutRotation;
        private bool _heldThisFrame;
        private bool _brokenThisFrame;

        /// <summary>The rule this body is showing. Read-only — everything goes through the methods.</summary>
        public DoorState State
        {
            get { return _state; }
        }

        /// <inheritdoc />
        public override string Title
        {
            get
            {
                switch (_state.Phase)
                {
                    case DoorPhase.Broken:
                        return "부서진 문";
                    case DoorPhase.Breaking:
                        return "문 — 무언가 밀고 있다";
                    case DoorPhase.Shut:
                        return "닫힌 문";
                    default:
                        return "문";
                }
            }
        }

        /// <inheritdoc />
        public override string Detail
        {
            get
            {
                switch (_state.Phase)
                {
                    case DoorPhase.Broken:
                        return "경첩이 나갔다. 다시 닫히지 않는다.";
                    case DoorPhase.Breaking:
                        return "버티는 중 — " + Mathf.RoundToInt(_state.BreakProgress01 * 100f) + "%";
                    case DoorPhase.Shut:
                        return "괴물은 "
                               + GameConstants.DoorBreakSeconds.ToString("0.#")
                               + "초면 부순다";
                    default:
                        return "닫는 데 " + GameConstants.DoorShutSeconds.ToString("0.#") + "초";
                }
            }
        }

        /// <inheritdoc />
        public override string Action
        {
            get { return _state.Phase == DoorPhase.Open ? "누르고 있기 — 닫기" : "열기"; }
        }

        /// <inheritdoc />
        public override bool AcceptsKey
        {
            get { return _state.CanBeUsed; }
        }

        /// <summary>Shutting is a hold; opening is a press. Only one of the two is a cost.</summary>
        public override bool NeedsHold
        {
            get { return _state.Phase == DoorPhase.Open; }
        }

        /// <inheritdoc />
        public override float HoldProgress01
        {
            get { return _state.ShutProgress01; }
        }

        /// <inheritdoc />
        public override void OnHeld(PlayerInteractor by, float deltaSeconds)
        {
            if (_state.Shut(deltaSeconds))
            {
                Apply();
            }
        }

        /// <inheritdoc />
        public override void OnHoldBroken()
        {
            _state.AbandonShut();
        }

        /// <inheritdoc />
        public override void OnPressed(PlayerInteractor by)
        {
            if (_state.Open())
            {
                Apply();
            }
        }

        /// <summary>
        /// The creature working on it. Called every tick something is pushing; not calling
        /// it lets the door recover, which is what a §04 섬광수 buys.
        /// </summary>
        /// <param name="deltaSeconds">Tick length.</param>
        /// <returns>True on the tick it comes down.</returns>
        public bool Push(float deltaSeconds)
        {
            _heldThisFrame = true;
            _brokenThisFrame = _state.Break(deltaSeconds);
            if (_brokenThisFrame)
            {
                Apply();
            }

            return _brokenThisFrame;
        }

        /// <summary>
        /// Hands the component the three things the generator built for it.
        /// <para>
        /// Public because the scene cannot: <c>MapSceneBuilder</c> is in an editor
        /// assembly that cannot reference this one, so it lays the geometry down and
        /// <c>MatchDirector</c> attaches and binds at match start.
        /// </para>
        /// </summary>
        public void Bind(Transform leaf, Collider? blocker, NavMeshObstacle? obstacle)
        {
            _leaf = leaf;
            _blocker = blocker;
            _obstacle = obstacle;
            _shutRotation = leaf.localRotation;
            if (_obstacle != null)
            {
                _obstacle.carving = true;
            }

            Apply();
        }

        private void Awake()
        {
            _leaf ??= transform.childCount > 0 ? transform.GetChild(0) : transform;
            _shutRotation = _leaf.localRotation;

            if (_blocker == null)
            {
                foreach (var found in GetComponentsInChildren<Collider>(true))
                {
                    if (!found.isTrigger)
                    {
                        _blocker = found;
                        break;
                    }
                }
            }

            if (_obstacle == null)
            {
                _obstacle = GetComponentInChildren<NavMeshObstacle>(true);
            }

            if (_obstacle != null)
            {
                _obstacle.carving = true;
            }

            Apply();
        }

        private void Update()
        {
            // Nothing pushed this frame, so the door recovers. The flag is cleared here
            // rather than by the pusher because §04's flash works by the creature simply
            // stopping — there is nobody left to tell the door it was let go.
            if (!_heldThisFrame)
            {
                _state.Relax(Time.deltaTime);
            }

            _heldThisFrame = false;

            var target = _state.Blocks ? _shutRotation : _shutRotation * Quaternion.Euler(0f, _openAngle, 0f);
            _swing = _swingSeconds <= 0f ? 1f : Mathf.MoveTowards(_swing, 1f, Time.deltaTime / _swingSeconds);
            _leaf!.localRotation = Quaternion.Slerp(_leaf.localRotation, target, _swing);
        }

        private void Apply()
        {
            _swing = 0f;

            if (_blocker != null)
            {
                _blocker.enabled = _state.Blocks;
            }

            if (_obstacle != null)
            {
                _obstacle.enabled = _state.Blocks;
            }
        }
    }
}
