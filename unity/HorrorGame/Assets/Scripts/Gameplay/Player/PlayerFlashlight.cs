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

        [Tooltip("The spot light. Left empty, found in children.")]
        [SerializeField]
        private Light? _spot;

        [Tooltip("Rig root searched for FlashlightMount. Left empty, this transform.")]
        [SerializeField]
        private Transform? _rigRoot;

        [Tooltip("Draws the torch in the fist. Left empty, found on this object. Optional.")]
        [SerializeField]
        private PlayerFirstPersonView? _view;

        [Header("Presentation")]
        [Tooltip("Beam brightness. A look value: §03's numbers are reach and cone, not lumens. "
            + "Defaults to HorrorGame.Rendering.FlashlightBeam, which is also what the review "
            + "screenshots render — change it here and the shots stop describing the game.")]
        [SerializeField]
        private float _intensity = HorrorGame.Rendering.FlashlightBeam.Intensity;

        [SerializeField]
        private Color _colour = new Color(1f, 0.96f, 0.87f);

        private FlashlightState? _state;
        private Transform? _mount;
        private bool _resolved;

        /// <summary>
        /// Name of the transform this component creates when the rig carries no
        /// <c>FlashlightMount</c> bone. Deliberately the exact bone name: every consumer
        /// that finds the mount by name — the hands shot's torch-viewport column, a future
        /// prop mounter — then agrees with the beam about where the torch is, and the
        /// normal <see cref="PlayerRigBones.Find"/> lookup re-discovers this transform on a
        /// scene reload instead of stacking a second one.
        /// </summary>
        public const string DerivedMountName = PlayerRigBones.FlashlightMount;

        /// <summary>
        /// Where the torch hand sits in view space when the rig has no mount bone:
        /// metres right, up and forward of the pitch pivot (the eye).
        /// <para>
        /// <b>Why it exists.</b> Runner.fbx has 13 bones and none of them is
        /// <c>FlashlightMount</c> (Player.fbx had one; the model swap dropped it, and the
        /// arms-split that restores a real mount is its own task). With the lookup failing,
        /// <see cref="SnapBeamToMount"/> never moved the light, so the beam emitted from
        /// where the rig builder authored it — the rig root, i.e. <b>the floor between the
        /// player's boots</b> — and <see cref="Cone"/> told the perception queries the same
        /// lie. Every review frame of a lit torch was a frame of a light nobody was holding.
        /// </para>
        /// <para>
        /// <b>Each number's derivation.</b> z = 0.45 m is a bent-arm fist ahead of the
        /// chest: the rig's arm bone is 0.667 m (shoulder (±0.240, 1.439) to hand tail
        /// (±0.284, 0.774), measured in the FBX), and an elbow at ~90° puts the fist about
        /// two-thirds of that ahead of the sternum; it is also 9× the camera's 0.05 near
        /// plane, so the torch mesh that will one day sit here cannot clip. x = +0.25 and
        /// y = −0.22 put the hand atan(0.25/0.45) = 29.1° right and atan(0.22/0.45) = 26.1°
        /// below the view axis — inside the frame at §05's whole FOV clamp (the tightest
        /// legal frame, 70° vertical at 16:9, reaches 35.0° down and 51.2° right), entering
        /// from the bottom-right the way a held torch does.
        /// </para>
        /// <para>
        /// <b>Under the pivot, not the camera.</b> <c>PlayerViewMotion</c> bobs the camera
        /// beneath the pivot; §05 makes the beam a pointing device other players read
        /// ("남의 손전등 방향이 정보다") and the view-motion component's own contract is that
        /// the beam never rides the bob. Direction already comes from <see cref="PlayerLook"/>;
        /// hanging the origin off the pivot keeps the position bob-free too.
        /// </para>
        /// </summary>
        public static readonly Vector3 DerivedMountViewOffset = new Vector3(0.25f, -0.22f, 0.45f);

        /// <summary>
        /// The rules object: on, or off. That is the whole of it now — the cell behind it
        /// and §08's 강화 손전등 upgrade are deleted. Everything that needs to know whether
        /// this player is visible asks this, not the <see cref="Light"/>.
        /// </summary>
        public FlashlightState State
        {
            get
            {
                if (_state == null)
                {
                    _state = new FlashlightState();
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

        /// <summary>
        /// Whether the torch is out of its pocket and in the hand — which is what the
        /// player sees, and the only cue that separates two of §03's four states from the
        /// inside.
        /// <para>
        /// Taken out whenever it is switched on, and put away when it is not. That is a
        /// design claim, not a convenience: §10 lists "손전등을 켠다 → 괴물이 본다 · 배터리를
        /// 쓴다" as the most repeated trade in the game, and a trade the player can see
        /// themselves making — a torch arriving in and leaving their own hand — is worth
        /// more than one that only changes the lighting.
        /// </para>
        /// <para>
        /// <c>IsOn</c> rather than <see cref="IsLit"/> on purpose: a dead cell leaves the
        /// torch in the hand and the corridor dark, which is the §03 resource pressure
        /// stated exactly. A torch that vanished when the battery ran out would read as
        /// having been dropped.
        /// </para>
        /// </summary>
        public bool InHand
        {
            get { return State.IsOn; }
        }

        private void Reset()
        {
            _input = GetComponentInChildren<PlayerInputRouter>();
            _look = GetComponentInChildren<PlayerLook>();
            _spot = GetComponentInChildren<Light>();
            _rigRoot = transform;
        }

        private void Awake()
        {
            ResolveWiring();
        }

        /// <summary>
        /// Finds the input, the look, the loadout, the spot and the mount. Idempotent, and
        /// reachable from <see cref="RefreshPresentation"/> because a capture rig builds
        /// this component outside play mode where <c>Awake</c> never runs.
        /// </summary>
        private void ResolveWiring()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;

            if (_input == null)
            {
                _input = GetComponentInChildren<PlayerInputRouter>();
            }

            if (_look == null)
            {
                _look = GetComponentInChildren<PlayerLook>();
            }

            if (_spot == null)
            {
                _spot = GetComponentInChildren<Light>();
            }

            if (_rigRoot == null)
            {
                _rigRoot = transform;
            }

            if (_view == null)
            {
                _view = GetComponentInChildren<PlayerFirstPersonView>();
            }

            _mount = PlayerRigBones.Find(_rigRoot, PlayerRigBones.FlashlightMount);
            if (_mount == null)
            {
                _mount = BuildDerivedMount();
            }

            if (_spot != null)
            {
                _spot.type = LightType.Spot;
                _spot.color = _colour;
                _spot.intensity = _intensity;
                _spot.shadows = LightShadows.Hard;
                _spot.enabled = false;
            }
        }

        /// <summary>
        /// Builds the stand-in mount at <see cref="DerivedMountViewOffset"/> under the
        /// pitch pivot, for a rig whose skeleton has no <c>FlashlightMount</c> bone.
        /// <para>
        /// The pivot is taken from <see cref="PlayerLook"/> first — the rig builder and the
        /// test fixtures both assign it explicitly — and from the first camera's parent
        /// when the look has none. A rig with neither (a bare capsule harness, a remote
        /// body with no camera) gets no derived mount and keeps the old behaviour: the
        /// beam stays where its scene author put it. That is deliberate — inventing an eye
        /// for a rig that does not have one would aim §05's pointing device from a place
        /// no player is looking.
        /// </para>
        /// <para>
        /// Reuses an existing <see cref="DerivedMountName"/> child rather than stacking a
        /// second: the rig builder saves its result into the solo scene, so a transform
        /// created before the save arrives again on load.
        /// </para>
        /// </summary>
        private Transform? BuildDerivedMount()
        {
            var pivot = _look != null ? _look.PitchPivot : null;
            if (pivot == null)
            {
                var camera = GetComponentInChildren<Camera>(true);
                if (camera != null)
                {
                    pivot = camera.transform.parent;
                }
            }

            if (pivot == null)
            {
                return null;
            }

            var existing = pivot.Find(DerivedMountName);
            if (existing != null)
            {
                return existing;
            }

            var mount = new GameObject(DerivedMountName).transform;
            mount.SetParent(pivot, worldPositionStays: false);
            mount.localPosition = DerivedMountViewOffset;
            mount.localRotation = Quaternion.identity;
            return mount;
        }

        // DELETED with the carry system and the light economy:
        //
        //   _battery / State.Tick(deltaTime)  §03's 왕복 clock — charge, switch-on cost,
        //                                     idle drain, spare cells. Tick was removed
        //                                     from FlashlightState rather than emptied,
        //                                     so there is now NO code path that can turn
        //                                     this beam off by itself. That was the point:
        //                                     a half-deleted torch that is always dead is
        //                                     the failure mode to avoid.
        //   HandsFull / EnforceCarryRules     §03's 「양손을 쓴다 · 손전등을 들 수 없다」 and
        //                                     §08's two-person 궤짝. A runner's hands hold
        //                                     a torch and nothing else, so there was
        //                                     nothing left that could take it away.
        //
        // The whole of Update is now: if the key was pressed, flip the switch.

        private void Update()
        {
            if (_input != null && _input.FlashlightToggled)
            {
                State.Toggle();
            }

            RefreshPresentation();
        }

        /// <summary>
        /// Pushes the current <see cref="FlashlightState"/> onto everything the player can
        /// see: the beam, and whether the torch is in the fist.
        /// <para>
        /// Public for the same reason <c>PlayerMotor.Step</c> and
        /// <c>PlayerViewMotion.Tick</c> are — a capture rig runs outside play mode, where
        /// <c>Update</c> never fires, and a shot that set the renderer itself would be
        /// photographing the shot tool rather than the game. This is the exact call
        /// <c>Update</c> makes.
        /// </para>
        /// </summary>
        public void RefreshPresentation()
        {
            ResolveWiring();
            ApplyToLight();

            if (_view != null)
            {
                _view.SetHandPropVisible(InHand);
            }
        }

        private void LateUpdate()
        {
            // After the animator has posed the hand this frame, for the same reason the
            // camera follows the head in LateUpdate: a beam a frame behind the body is a
            // beam that points somewhere the player did not point.
            SnapBeamToMount();
        }

        /// <summary>
        /// Puts the beam on <c>FlashlightMount</c> for the pose the rig is in right now,
        /// aimed where the player is aiming. What <c>LateUpdate</c> does every frame.
        /// <para>
        /// Public for the capture rig, which runs outside play mode: without it the light
        /// stays at the rig's origin — on the floor between the player's boots — and every
        /// review shot is of a torch nobody is holding. §05 makes the beam a pointing
        /// device other players read, so where it comes from is not a detail.
        /// </para>
        /// </summary>
        public void SnapBeamToMount()
        {
            ResolveWiring();

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
