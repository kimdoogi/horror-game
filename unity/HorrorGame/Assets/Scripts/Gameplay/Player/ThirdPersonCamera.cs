#nullable enable

using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// PUBG식 어깨너머 3인칭 — a boom that hangs behind and above the runner's shoulder,
    /// pulled in by the wall when there is no room for it.
    /// <para>
    /// <b>Why a race wants this and a co-op recovery game did not.</b> DESCENT-PIVOT §2①
    /// is the whole design in one diagram: the gates narrow 4 → 2 → 1 on the way to the
    /// middle, and 「마지막 관문은 하나」 — twenty people funnel through one opening. Your
    /// rank is decided by which gate you commit to while somebody else is committing to
    /// the same one. That is a question about who is <em>beside</em> you and who is
    /// <em>behind</em> you, and first person cannot answer either. At §05's 80° vertical
    /// FOV on 16:9 the horizontal frame is 112°, so a runner level with your shoulder sits
    /// 34° outside the picture and a runner on your heels does not exist at all. You learn
    /// about them when they overtake, which is after the decision.
    /// </para>
    /// <para>
    /// With the boom at <see cref="_boomMetres"/> = 2.6 m the same camera shows roughly
    /// 7.8 m of width at the depth the player is standing at — three corridors abreast —
    /// and everything within 2.6 m behind. That is exactly the information the race runs
    /// on and nothing else: it does not see round corners, it does not reach further down
    /// the corridor, and it does not brighten anything, so §03's darkness and §12's
    /// sightline rules are untouched.
    /// </para>
    /// <para>
    /// <b>The identical bodies argue for it, not against it.</b> DESCENT-PIVOT §5 makes
    /// every runner the same model and gives 「1인칭이다. 자기 몸을 볼 일이 거의 없다」 as
    /// one reason it costs nothing. That is a premise, not a requirement, and this makes it
    /// a mode rather than a fact. What survives the change is the part that mattered:
    /// 「똑같이 생긴 스무 명이 어두운 복도에서 같은 방향으로 움직이는 것」 is the intended
    /// horror, and in third person you are one of the twenty rather than a disembodied
    /// witness to them. There is no role colour left to leak (§04's jobs are gone), so the
    /// body on screen tells the owner nothing their rivals cannot already see.
    /// </para>
    /// <para>
    /// <b>First person stays.</b> §05's whole control argument is built on it, §03 asks the
    /// player to read a clue up close, and every PlayMode test in the project assumes the
    /// eye is at <c>HeadCameraAnchor</c>. So this component ships <em>off</em>
    /// (<see cref="_startInThirdPerson"/> defaults false), writes nothing at all while it
    /// is off, and puts everything back the way it found it in <c>OnDisable</c>.
    /// </para>
    /// <para>
    /// <b>What it does not touch: the FOV.</b> §05 clamps it to 70–90 because a wider view
    /// makes the 45° peek cheap. Third person hands over more than any slider inside that
    /// clamp could, which is a decision the design took on purpose — but it is one
    /// decision, and quietly widening the FOV on top of it would be a second one nobody
    /// asked for. <see cref="PlayerCameraRig.FieldOfView"/> keeps its clamp.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        /// <summary>
        /// Smallest probe sphere the collision cast will ever use, metres.
        /// <para>
        /// The cast radius is normally derived from the near plane
        /// (<see cref="ProbeRadiusMetres"/>), but a rig with a freak FOV or a camera that
        /// has not resolved yet could compute something near zero, and a zero-radius cast
        /// slips between two wall panels that meet at a corner and drops the camera into
        /// the void behind them.
        /// </para>
        /// </summary>
        public const float MinimumProbeRadiusMetres = 0.12f;

        [Header("Wiring")]
        [Tooltip("Supplies the camera and the pitch pivot. Left empty, found on this object or its children.")]
        [SerializeField]
        private PlayerCameraRig? _rig;

        [Tooltip("Its Translation/Rotation are re-applied about the boom rather than overwritten. Left empty, found on this object.")]
        [SerializeField]
        private PlayerViewMotion? _viewMotion;

        [Tooltip("Decides what the owner sees of their own body. Left empty, found on this object.")]
        [SerializeField]
        private PlayerFirstPersonView? _view;

        [Tooltip("Rebuilt or torn down when the body becomes visible. Left empty, found on this object. Optional.")]
        [SerializeField]
        private PlayerHandFill? _handFill;

        [Tooltip("Only used to honour InputSuppressed, so a dead or paused player cannot toggle. Left empty, found on this object. Optional.")]
        [SerializeField]
        private PlayerInputRouter? _input;

        [Header("Boom — where the shoulder camera sits")]
        [Tooltip("Metres right of the spine. 0.45 keeps the figure out of the middle of the frame and still leaves 0.65 m to the wall in the kit's 2.2 m corridor.")]
        [SerializeField]
        private float _shoulderRightMetres = 0.45f;

        [Tooltip("Metres above the eye. 0.28 tips the view down enough that the floor two metres ahead is in frame — in a descent race that is what you trip over.")]
        [SerializeField]
        private float _shoulderUpMetres = 0.28f;

        [Tooltip("Metres behind the eye. At §05's 80 degree FOV this frames about 7.8 m of width at the player's own depth, and shows anyone within 2.6 m behind.")]
        [SerializeField]
        private float _boomMetres = 2.6f;

        [Header("Wall avoidance — the part that is always wrong the first time")]
        [Tooltip("What the boom is allowed to hit. Triggers are always ignored: a chute volume or an interaction bubble must never shove the camera.")]
        [SerializeField]
        private LayerMask _blockers = ~0;

        [Tooltip("Metres the sphere stops short of the surface it hit, so the near plane never grazes a sloped wall.")]
        [SerializeField]
        private float _wallPadMetres = 0.04f;

        [Tooltip("Below this remaining boom length the view falls back to first person rather than sitting inside the player's own shoulders. Metres.")]
        [SerializeField]
        private float _minimumBoomMetres = 1.0f;

        [Tooltip("Extra clearance needed to come back OUT of the first-person fallback, metres. Without it a doorframe flickers the whole view on and off.")]
        [SerializeField]
        private float _releaseHysteresisMetres = 0.35f;

        [Tooltip("Time constant for the boom EXTENDING again, seconds. Retracting is not smoothed at all — see the class remarks on why.")]
        [SerializeField]
        private float _extendSeconds = 0.30f;

        [Tooltip("Near clip while in third person, metres. 0.08 buys a 0.16 m probe sphere; Unity's 0.3 default would need 0.60 m, which cannot pass a 2.2 m corridor.")]
        [SerializeField]
        private float _nearClipMetres = 0.08f;

        [Header("Smoothing")]
        [Tooltip("Time constant for the boom origin's HEIGHT, seconds. This is what stops the animated head bone shaking the whole world from 2.6 m away.")]
        [SerializeField]
        private float _heightSmoothSeconds = 0.18f;

        [Tooltip("Time constant for the boom origin following the body, seconds. Small: this is weight, not drift.")]
        [SerializeField]
        private float _followSeconds = 0.05f;

        [Tooltip("Hard cap on how far the origin may trail the body, metres. At 5.6 m/s the raw lag would be 0.28 m; the cap bites above 3.6 m/s, which is where lag is wanted and nowhere else.")]
        [SerializeField]
        private float _maximumLagMetres = 0.18f;

        [Header("Input")]
        [Tooltip("Toggles first/third person. V, because that is where every player of this genre already reaches.")]
        [SerializeField]
        private Key _toggleKey = Key.V;

        [Tooltip("Start the match in third person. Off by default: §05's first person is the documented view and every PlayMode test assumes it.")]
        [SerializeField]
        private bool _startInThirdPerson;

        // Eight is plenty: the cast is 2.6 m long inside a corridor whose walls, floor and
        // ceiling are four colliders, and an overflow only costs one frame of a slightly
        // longer boom rather than a wrong one — the nearest hit is still among the eight.
        private readonly RaycastHit[] _hits = new RaycastHit[8];

        private Camera? _camera;
        private Transform? _pivot;

        private Quaternion _restLocalRotation = Quaternion.identity;
        private float _restNearClip = 0.3f;
        private bool _restCaptured;

        private bool _requested;
        private bool _effective;
        private bool _bodyShown;
        private bool _ownerAuthored = true;

        private Vector3 _origin;
        private float _originHeight;
        private float _distance;
        private bool _snap = true;

        /// <summary>
        /// Whether the player has asked for third person. Setting it is the same as
        /// pressing the key, so a settings screen, a spectator tool or §02's results
        /// screen can drive the view without knowing which key it is bound to.
        /// </summary>
        public bool ThirdPersonRequested
        {
            get { return _requested; }
            set { SetThirdPerson(value); }
        }

        /// <summary>
        /// Whether the third-person view is actually on screen right now. Differs from
        /// <see cref="ThirdPersonRequested"/> whenever the boom has been squeezed below
        /// <see cref="MinimumBoomMetres"/> — pressed into a corner, the view is first
        /// person no matter what was asked for, because the alternative is the owner's own
        /// chest across the frame.
        /// </summary>
        public bool ThirdPersonActive
        {
            get { return _effective; }
        }

        /// <summary>
        /// Current boom length in metres, after the wall pulled it in. Zero while first
        /// person. Exposed for a test and for the feel harness's readout — this number is
        /// the one that tells you whether a corridor is too tight for the mode.
        /// </summary>
        public float BoomDistanceMetres
        {
            get { return _effective ? _distance : 0f; }
        }

        /// <summary>The boom length asked for before anything was hit, metres — the length of the shoulder offset vector.</summary>
        public float DesiredBoomMetres
        {
            get { return LocalOffset().magnitude; }
        }

        /// <summary>
        /// Shortest boom the mode will tolerate, metres. Below it the view is first person
        /// instead. Public so a map validator can ask whether a corridor supports the mode
        /// without re-deriving the number.
        /// </summary>
        public float MinimumBoomMetres
        {
            get { return _minimumBoomMetres; }
        }

        /// <summary>
        /// Radius of the sphere swept out to the camera, metres.
        /// <para>
        /// Derived, not authored: it is the radius of the sphere that encloses the near
        /// clip <em>rectangle</em>, because a smaller sphere lets a corner of the near
        /// plane cross the wall while the camera's centre is still outside it — which
        /// looks exactly like the camera being inside the wall, since it is. With
        /// Unity's 0.3 m default near plane at §05's 80° FOV that sphere is 0.60 m across
        /// the radius, and 0.60 m does not fit down a 2.2 m corridor beside a 0.45 m
        /// shoulder offset. That is why this component pulls the near plane in to
        /// <see cref="_nearClipMetres"/> while it is running and puts it back afterwards.
        /// </para>
        /// </summary>
        public float ProbeRadiusMetres
        {
            get
            {
                var camera = _camera;
                if (camera == null)
                {
                    return MinimumProbeRadiusMetres;
                }

                var near = Mathf.Max(0.01f, camera.nearClipPlane);
                var halfHeight = near * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                var halfWidth = halfHeight * Mathf.Max(0.1f, camera.aspect);
                var corner = Mathf.Sqrt((near * near) + (halfHeight * halfHeight) + (halfWidth * halfWidth));
                return Mathf.Max(MinimumProbeRadiusMetres, corner);
            }
        }

        /// <summary>
        /// Switches the view. Idempotent, and safe before <c>Awake</c> has run.
        /// <para>
        /// Entering third person snaps the boom to its full clear length rather than
        /// dollying out. A dolly would spend a third of a second with the camera inside
        /// the body it is about to show, and in a race the player pressed the key because
        /// they wanted to see something <em>now</em>.
        /// </para>
        /// </summary>
        /// <param name="thirdPerson">True for the shoulder camera, false for §05's eye.</param>
        public void SetThirdPerson(bool thirdPerson)
        {
            if (_requested == thirdPerson)
            {
                return;
            }

            _requested = thirdPerson;
            _snap = true;
        }

        /// <summary>
        /// Forgets where the camera was and rebuilds the boom from the body's current
        /// position on the next frame.
        /// <para>
        /// For §02's chutes and for a respawn. Without it the origin's lag and the boom's
        /// extend both interpolate across the teleport, which for a drop between storeys
        /// means a camera that sweeps through eight metres of concrete on the way down.
        /// </para>
        /// </summary>
        public void Snap()
        {
            _snap = true;
        }

        private void Reset()
        {
            _rig = GetComponentInChildren<PlayerCameraRig>();
            _viewMotion = GetComponent<PlayerViewMotion>();
            _view = GetComponent<PlayerFirstPersonView>();
            _handFill = GetComponent<PlayerHandFill>();
            _input = GetComponentInChildren<PlayerInputRouter>();
        }

        private void Awake()
        {
            // Order 300, so PlayerCameraRig (100) has already resolved its camera and
            // pivot and this can take them rather than repeat the HeadCameraAnchor search.
            _rig ??= GetComponentInChildren<PlayerCameraRig>();
            _viewMotion ??= GetComponent<PlayerViewMotion>();
            _view ??= GetComponent<PlayerFirstPersonView>();
            _handFill ??= GetComponent<PlayerHandFill>();
            _input ??= GetComponentInChildren<PlayerInputRouter>();

            if (_rig != null)
            {
                _camera = _rig.FirstPersonCamera;
                _pivot = _rig.PitchPivot;
            }

            if (_camera == null)
            {
                _camera = GetComponentInChildren<Camera>();
            }

            if (_pivot == null && _camera != null)
            {
                _pivot = _camera.transform.parent != null ? _camera.transform.parent : _camera.transform;
            }

            if (_view != null)
            {
                // Remembered rather than assumed true: §13 will spawn remote bodies with
                // this false, and returning them to "owner" on the way out of third person
                // would draw someone else's hands and hide their body.
                _ownerAuthored = _view.IsOwner;
            }

            CaptureRest();
            _requested = _startInThirdPerson;
            _snap = true;
        }

        private void OnEnable()
        {
            _snap = true;
        }

        private void OnDisable()
        {
            // Everything back. PlayerCameraRig and PlayerViewMotion both write the eye
            // every LateUpdate, so the camera transform needs no restoring — but the near
            // clip and the body's visibility are ours alone, and a rig left drawing its
            // owner's chest because a component was switched off is the kind of defect
            // that survives a whole project (PlayerViewMotion.OnDisable makes the same
            // argument about two centimetres of height).
            RestoreNearClip();
            ShowBody(false);
            _effective = false;
            _distance = 0f;
            _snap = true;
        }

        private void Update()
        {
            // Read in Update, not LateUpdate: wasPressedThisFrame is only true for the
            // frame the key went down, and PlayerInputRouter (order −100) has already
            // decided whether input counts at all this frame.
            if (_input != null && _input.InputSuppressed)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                // A build with no keyboard, or a test. The public switch still works.
                return;
            }

            if (keyboard[_toggleKey].wasPressedThisFrame)
            {
                SetThirdPerson(!_requested);
            }
        }

        private void LateUpdate()
        {
            var camera = _camera;
            var pivot = _pivot;

            if (camera == null || pivot == null)
            {
                return;
            }

            // §09's ghost unparents the eye from the body and flies it round the map. Its
            // own remarks say why: "A ghost sharing a transform with two components that
            // still believe they own it would drift back to the corpse a frame at a time."
            // This is the third such component, and this line is how it stops believing.
            if (!camera.transform.IsChildOf(pivot))
            {
                if (_effective)
                {
                    RestoreNearClip();
                    ShowBody(false);
                    _effective = false;
                }

                return;
            }

            var deltaSeconds = Time.deltaTime;
            var origin = TrackOrigin(pivot, deltaSeconds);
            var rotation = pivot.rotation;

            var offset = rotation * LocalOffset();
            var full = offset.magnitude;
            if (full <= 0.0001f)
            {
                Present(false, camera, rotation, origin, Vector3.zero);
                return;
            }

            var direction = offset / full;
            var clear = ClearDistance(origin, direction, full);

            if (_snap)
            {
                _distance = clear;
            }
            else if (clear <= _distance)
            {
                // Retracting is NOT smoothed, and this is the whole trick. An eased
                // pull-in is a camera that spends its easing time inside the wall — at
                // 2.6 m and a 0.06 s time constant, the first frame after a corner still
                // leaves two metres of boom in the concrete, which is four frames of
                // looking at the inside of a corridor at 60 Hz. The sweep already knows
                // exactly where the free space ends, so the camera goes there this frame.
                // What is smoothed is coming back out, below: in a maze of 2.2 m corridors
                // the boom is freed several times a second, and an instant push-out reads
                // as the camera being kicked every time you pass a doorway.
                _distance = clear;
            }
            else
            {
                _distance = Mathf.Min(clear, Approach(_distance, clear, deltaSeconds, _extendSeconds));
            }

            _snap = false;

            Present(_requested && HasRoom(), camera, rotation, origin, direction);
        }

        /// <summary>
        /// Whether the boom is currently long enough to be worth showing, with hysteresis.
        /// <para>
        /// The threshold exists because the pull-in has nowhere to stop: pressed into a
        /// corner the sweep will happily report 0.15 m of clearance, and a camera 0.15 m
        /// behind the eye is inside the skull it is meant to be behind. Below
        /// <see cref="_minimumBoomMetres"/> the honest picture is §05's own eye, so that is
        /// what the player gets.
        /// </para>
        /// <para>
        /// The hysteresis is not decoration. The kit's corridors are 2.2 m wide and the
        /// gates that DESCENT-PIVOT §2① narrows to a single opening are doorframes, so the
        /// boom crosses this threshold twice per doorway at a run. Without a release band
        /// the whole view — camera, body, hand fill — would flicker on every frame that a
        /// doorframe wobbles across the sweep.
        /// </para>
        /// </summary>
        private bool HasRoom()
        {
            var floor = _effective
                ? _minimumBoomMetres
                : _minimumBoomMetres + Mathf.Max(0f, _releaseHysteresisMetres);

            return _distance >= floor;
        }

        /// <summary>
        /// The boom origin: a point over the runner's shoulder that is deliberately
        /// <em>not</em> the animated head bone.
        /// <para>
        /// <see cref="PlayerCameraRig"/> welds the pitch pivot to <c>HeadCameraAnchor</c>
        /// every frame so the eye follows whatever the animation does to the head. At the
        /// eye that is correct and invisible. From 2.6 m behind it is the entire world
        /// shaking twice per stride, because a rotation-free camera translated by the
        /// head's ±3 cm moves everything on screen by ±3 cm of parallax. So the height is
        /// low-passed. 0.18 s is chosen against the two signals it has to tell apart: a
        /// run's stride is about 2.5 Hz and comes through at a gain of 0.33 — ±2.9 cm
        /// becomes ±1.0 cm, which reads as breath rather than as a fault — while a crouch
        /// and a stairwell both take a third of a second or more and arrive essentially
        /// whole. A longer constant would take the last centimetre and start eating the
        /// crouch with it.
        /// </para>
        /// <para>
        /// The horizontal comes from the body root, which <see cref="PlayerMotor"/> moves
        /// smoothly, and is then given a small capped lag. That lag is the "camera has
        /// weight" term: it settles a hand's width back as the runner accelerates and
        /// catches up as they stop. It is capped rather than tuned low because an uncapped
        /// exponential lag at §04's 5.6 m/s 질주 would sit 0.28 m behind the body, and the
        /// far end of that lag is a wall.
        /// </para>
        /// </summary>
        private Vector3 TrackOrigin(Transform pivot, float deltaSeconds)
        {
            var root = transform;
            var height = Vector3.Dot(pivot.position - root.position, root.up);

            _originHeight = _snap
                ? height
                : Approach(_originHeight, height, deltaSeconds, _heightSmoothSeconds);

            var ideal = root.position + (root.up * _originHeight);

            if (_snap)
            {
                _origin = ideal;
                return _origin;
            }

            var lagged = new Vector3(
                Approach(_origin.x, ideal.x, deltaSeconds, _followSeconds),
                Approach(_origin.y, ideal.y, deltaSeconds, _followSeconds),
                Approach(_origin.z, ideal.z, deltaSeconds, _followSeconds));

            var lag = lagged - ideal;
            var cap = Mathf.Max(0f, _maximumLagMetres);
            if (lag.sqrMagnitude > cap * cap)
            {
                lagged = ideal + (lag.normalized * cap);
            }

            _origin = lagged;
            return _origin;
        }

        /// <summary>
        /// Sweeps the probe sphere from the shoulder out to where the camera wants to be
        /// and returns how far it actually got, metres.
        /// </summary>
        /// <param name="origin">Boom origin in world space.</param>
        /// <param name="direction">Unit vector toward the desired camera position.</param>
        /// <param name="full">Desired boom length, metres.</param>
        private float ClearDistance(Vector3 origin, Vector3 direction, float full)
        {
            var radius = ProbeRadiusMetres;

            var count = Physics.SphereCastNonAlloc(
                origin, radius, direction, _hits, full, _blockers, QueryTriggerInteraction.Ignore);

            var nearest = full;
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                var collider = hit.collider;
                if (collider == null)
                {
                    continue;
                }

                // The runner's own capsule, the model and anything else hanging off the rig.
                // PlayerStance's headroom probe rejects self-hits the same way and for the
                // same reason: the sweep starts inside the body it belongs to.
                if (collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                // Unity reports a collider that already overlaps the sphere at the start of
                // the sweep with distance 0 and no usable normal. Skipped rather than
                // obeyed: taking it would collapse the boom to nothing every time the
                // shoulder brushed a door leaf, and the origin is inside the character
                // controller's own free space by construction, so a genuine zero here is a
                // degenerate frame rather than a wall.
                if (hit.distance <= 0f)
                {
                    continue;
                }

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                }
            }

            if (nearest >= full)
            {
                return full;
            }

            return Mathf.Max(0f, nearest - Mathf.Max(0f, _wallPadMetres));
        }

        /// <summary>
        /// The boom in the pitch pivot's own frame: right, up, back.
        /// <para>
        /// Taken as one vector rather than as three independent axes because the wall
        /// sweep scales the <em>whole</em> offset when it pulls in. A pull-in that shrank
        /// only the backward component would slide the camera sideways along the wall it
        /// just hit and end up looking at the runner's ear; scaling the vector converges
        /// the camera on the eye instead, which is the same place the first-person
        /// fallback puts it — so the two never disagree about which way is out.
        /// </para>
        /// </summary>
        private Vector3 LocalOffset()
        {
            return new Vector3(_shoulderRightMetres, _shoulderUpMetres, -_boomMetres);
        }

        /// <summary>
        /// Writes the frame — or deliberately writes nothing, which is what first person is.
        /// <para>
        /// <b>How this avoids fighting <see cref="PlayerViewMotion"/>.</b> That component
        /// owns the camera's <em>local</em> pose and writes it every <c>LateUpdate</c> at
        /// order 200; this one runs at 300 and would trivially win, which is exactly the
        /// mistake to avoid — clobbering it would delete the stride, the landing dip, §05's
        /// directional lean and §06's breathing from third person, and its author's whole
        /// argument is that those are what make §14's first two questions answerable. So
        /// nothing is clobbered: the two published read-only values
        /// <see cref="PlayerViewMotion.Translation"/> and
        /// <see cref="PlayerViewMotion.Rotation"/> are re-applied here about the boom's
        /// position instead of about the eye's. The translation is in the pivot's local
        /// frame, so it maps to world through the pivot's rotation exactly as it did when
        /// the camera was a child of it. The player's comfort <c>Scale</c> is already
        /// folded into both, which means setting it to zero still gives an exactly steady
        /// camera in third person, and that property is what makes it honest advice.
        /// </para>
        /// <para>
        /// In first person this writes nothing whatsoever. It does not restore the camera,
        /// because there is nothing to restore: <see cref="PlayerCameraRig"/> and
        /// <see cref="PlayerViewMotion"/> have both already written the eye this frame, and
        /// they will do it again next frame. Silence is the correct handover.
        /// </para>
        /// </summary>
        private void Present(bool thirdPerson, Camera camera, Quaternion rotation, Vector3 origin, Vector3 direction)
        {
            if (thirdPerson != _effective)
            {
                _effective = thirdPerson;
                if (thirdPerson)
                {
                    ApplyNearClip(camera);
                }
                else
                {
                    RestoreNearClip();
                }

                ShowBody(thirdPerson);
            }

            if (!thirdPerson)
            {
                return;
            }

            CaptureRest();

            var bob = _viewMotion != null && _viewMotion.isActiveAndEnabled
                ? _viewMotion.Translation
                : Vector3.zero;
            var sway = _viewMotion != null && _viewMotion.isActiveAndEnabled
                ? _viewMotion.Rotation
                : Quaternion.identity;

            camera.transform.SetPositionAndRotation(
                origin + (direction * _distance) + (rotation * bob),
                rotation * _restLocalRotation * sway);
        }

        /// <summary>
        /// Shows or hides the owner's own body, through the mechanism that already exists
        /// rather than a second one.
        /// <para>
        /// <see cref="PlayerFirstPersonView.IsOwner"/> is the switch, because being in
        /// third person is the same situation §13's remote bodies are in — someone is
        /// looking at this model from outside it — and every consequence follows for free
        /// and consistently: the body's renderers go from <c>ShadowsOnly</c> back to
        /// <c>On</c>, <see cref="PlayerWorldArms"/> stops holding the forearms up across
        /// the chest (a pose authored for a camera that is no longer at the eye), and
        /// <see cref="PlayerHandFill"/> tears down its 0.62 m lamp, which from behind would
        /// be a body glowing from the inside. Inventing a per-renderer toggle here would
        /// have got the first of those three and silently missed the other two.
        /// </para>
        /// <para>
        /// The hand fill is re-applied explicitly because it only builds itself in
        /// <c>Start</c>; the view and the arms both re-read ownership on their own.
        /// </para>
        /// </summary>
        /// <param name="visible">True while the body should be drawn to its own player.</param>
        private void ShowBody(bool visible)
        {
            if (_bodyShown == visible)
            {
                return;
            }

            _bodyShown = visible;

            var view = _view;
            if (view == null)
            {
                return;
            }

            view.IsOwner = _ownerAuthored && !visible;
            view.Apply();

            if (_handFill != null)
            {
                _handFill.Apply();
            }
        }

        private void ApplyNearClip(Camera camera)
        {
            CaptureRest();
            camera.nearClipPlane = Mathf.Max(0.01f, _nearClipMetres);
        }

        private void RestoreNearClip()
        {
            if (!_restCaptured || _camera == null)
            {
                return;
            }

            _camera.nearClipPlane = _restNearClip;
        }

        private void CaptureRest()
        {
            if (_restCaptured || _camera == null)
            {
                return;
            }

            _restCaptured = true;

            // The camera's authored local rotation, not identity: if a rig ever authors a
            // small tilt on the eye, third person should keep it rather than quietly
            // straighten it. PlayerViewMotion captures the same thing for the same reason.
            _restLocalRotation = _camera.transform.localRotation;
            _restNearClip = _camera.nearClipPlane;
        }

        /// <summary>
        /// Exponential approach, frame-rate independent — the same helper
        /// <see cref="PlayerViewMotion"/> keeps for the same reason: a per-frame lerp makes
        /// the camera settle at a different time on a 60 Hz machine than on a 144 Hz one,
        /// and §14's questions are asked on whatever machine the tester has.
        /// </summary>
        private static float Approach(float current, float target, float deltaSeconds, float seconds)
        {
            if (seconds <= 0f || deltaSeconds <= 0f)
            {
                return target;
            }

            return target + ((current - target) * Mathf.Exp(-deltaSeconds / seconds));
        }
    }
}
