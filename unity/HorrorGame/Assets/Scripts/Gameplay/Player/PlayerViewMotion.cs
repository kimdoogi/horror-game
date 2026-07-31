#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Movement;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Gives the first-person camera a body: a stride that lands on the footstep, a
    /// floor that hits back, §05's directional cost as a lean, §06's stamina as
    /// breathing, and a flinch when the creature commits.
    /// <para>
    /// <b>Why this exists at all.</b> §14 names five validation questions and says
    /// the first two decide the project — 「추격이 재밌는가」 and 「곁눈질 딜레마가
    /// 작동하는가」 — and then says 「이건 문서로 정할 수 없고 직접 만져봐야 나온다」.
    /// Both are questions about what the game feels like in the hands. Until this
    /// component existed the eye was welded to a rail: walking, sprinting, backing
    /// away at §05's 65 % and dropping four storeys down §12's stairwells all
    /// produced exactly the same picture, a smooth glide at a constant height. Every
    /// number in §05's table was correct and none of them could be felt.
    /// </para>
    /// <para>
    /// <b>What it is allowed to touch.</b> The camera transform, and nothing else. It
    /// is deliberately downstream of everything: <see cref="PlayerLook"/> yaws the
    /// body and pitches the pivot, <see cref="PlayerCameraRig"/> places the pivot on
    /// the head bone, and this writes a local offset onto the camera underneath both.
    /// So no rule can read it by accident. In particular
    /// <see cref="PlayerFlashlight"/> takes the beam's rotation from
    /// <c>PlayerLook</c> rather than from the camera, which means the beam does
    /// <em>not</em> bob — §05 makes beam direction a channel a teammate reads
    /// ("남의 손전등 방향이 정보다"), and a light that swayed on its own would be a
    /// teammate reading this component instead of a person.
    /// </para>
    /// <para>
    /// <b>What it must never become.</b> A sixth sense. §04 sells the monster's
    /// position to the 청음사 and §11 calls the 관측자's read the one thing that
    /// cannot be bought, so a camera that reacted sharply to proximity would hand
    /// both roles out for free. The only proximity term here is
    /// <see cref="Dread01"/>, which is the same blunt 0–1 the heartbeat already
    /// runs on, carries no bearing, and moves the eye by four millimetres.
    /// </para>
    /// <para>
    /// <b>Shape of the API.</b> <see cref="Tick"/> takes an explicit delta and
    /// <see cref="Apply"/> writes the transform, for the same reason
    /// <c>PlayerMotor.Step</c> and <c>MonsterAcquireTell.ApplyProgress</c> do: a
    /// test can drive a hundred simulated seconds in a frame, and a capture rig can
    /// pose the camera at an exact moment rather than photographing whatever frame
    /// it caught.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class PlayerViewMotion : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Supplies §05's speed, heading and §06's stamina. Left empty, found on this object.")]
        [SerializeField]
        private PlayerMotor? _motor;

        [Tooltip("Its Footfall event pins the stride phase. Left empty, found on this object. Optional.")]
        [SerializeField]
        private PlayerAnimatorDriver? _animator;

        [Tooltip("The transform the offset is written to. Left empty, the first camera in children.")]
        [SerializeField]
        private Transform? _cameraTransform;

        [Header("Comfort")]
        [Tooltip("Player's camera-motion preference, 0..1. Zero is exact identity — a supported way to play.")]
        [SerializeField]
        [Range(0f, 1f)]
        private float _scale = ViewMotionTuning.ScaleDefault;

        private Vector3 _restPosition;
        private Quaternion _restRotation = Quaternion.identity;
        private bool _restCaptured;
        private bool _subscribed;

        private float _stridePhase;
        private float _breathPhase;
        private float _breath;
        private float _drag;

        private float _landingStrength;
        private float _landingElapsed = float.MaxValue;
        private float _landingCooldown;
        private float _previousDescent;

        private float _flinchElapsed = float.MaxValue;
        private float _trembleTime;

        private Vector3 _translation;
        private Quaternion _rotation = Quaternion.identity;

        /// <summary>
        /// The player's comfort preference, 0–1, from the settings screen.
        /// <para>
        /// At zero every offset is exactly zero and the camera is where it would be
        /// with this component absent — which is the property that makes turning it
        /// off safe advice rather than a degraded mode. Nothing in this file is
        /// load-bearing for any rule, and
        /// <c>PlayerViewMotionTests.ViewMotion_AtZeroScale_LeavesTheCameraExactlyWhereTheRigPutIt</c>
        /// pins that.
        /// </para>
        /// </summary>
        public float Scale
        {
            get { return _scale; }
            set { _scale = Mathf.Clamp01(float.IsNaN(value) ? ViewMotionTuning.ScaleDefault : value); }
        }

        /// <summary>
        /// How much trouble the local player is in, 0–1. Pushed in as a float rather
        /// than read, so this assembly never imports the monster or the audio layer
        /// (ARCHITECTURE §3) — the same shape as
        /// <c>PlayerFlashlight.TierRangeMultiplier</c>.
        /// <para>
        /// It must be <c>DangerSense.Danger01</c> or something equally blunt. That
        /// type's remarks are the authority for why a proximity signal is allowed at
        /// all, and the argument only holds while the signal is too coarse to
        /// navigate by.
        /// </para>
        /// </summary>
        public float Dread01 { get; set; }

        /// <summary>
        /// True when something else calls <see cref="Tick"/>, so <c>LateUpdate</c> must
        /// not step a second time. The same flag <c>PlayerMotor.SteppedExternally</c> is,
        /// and for the same two consumers: a test driving a hundred simulated seconds in
        /// one frame, and a capture rig posing an exact moment.
        /// </summary>
        public bool TickedExternally { get; set; }

        /// <summary>Where in the two-pace cycle the stride is, 0–1. Zero and one half are the two footfalls.</summary>
        public float StridePhase
        {
            get { return _stridePhase; }
        }

        /// <summary>
        /// §05's directional cost as this component sees it, 0–1: 0 running straight
        /// ahead, 1 straight back. Smoothed, so it is not the instantaneous
        /// multiplier — the raw one is <c>PlayerMotor.DirectionalMultiplier</c>.
        /// </summary>
        public float Drag
        {
            get { return _drag; }
        }

        /// <summary>How winded the player is, 0–1, lagging §06's bar. What the breathing is scaled by.</summary>
        public float Breath
        {
            get { return _breath; }
        }

        /// <summary>Strength of the landing currently springing back, 0–1, or zero between landings.</summary>
        public float Landing
        {
            get { return _landingElapsed >= ViewMotionTuning.LandingRecoverSeconds ? 0f : _landingStrength; }
        }

        /// <summary>Whether §06's acquisition flinch is still running.</summary>
        public bool Flinching
        {
            get { return _flinchElapsed < ViewMotionTuning.FlinchSeconds; }
        }

        /// <summary>The offset written to the camera this step, in the pivot's local space, metres.</summary>
        public Vector3 Translation
        {
            get { return _translation; }
        }

        /// <summary>The rotation written to the camera this step, local. Pitch and roll only — never yaw.</summary>
        public Quaternion Rotation
        {
            get { return _rotation; }
        }

        /// <summary>
        /// The body's reaction to §06's brain entering 추격.
        /// <para>
        /// Fired by the match layer on the state edge, not on proximity: §06 makes
        /// the transition into Chase the most consequential event in a match and
        /// already announces it twice — the creature's crest and maw flare
        /// (<c>MonsterAcquireTell</c>) and it roars. The person being chased is
        /// usually running and facing the wrong way, so they may get neither. This
        /// is the third announcement, on the one surface they are definitely
        /// looking at.
        /// </para>
        /// <para>
        /// It carries no direction on purpose. Knowing that it has committed is
        /// free; knowing <em>where from</em> is §04's 관측자 and must stay bought.
        /// </para>
        /// </summary>
        public void Flinch()
        {
            _flinchElapsed = 0f;
        }

        /// <summary>
        /// Clears every accumulator without moving the camera. For §09's respawn and
        /// for a teleport: a landing impulse that survived a scene change would fire
        /// the frame after the player appeared somewhere else.
        /// </summary>
        public void ResetMotion()
        {
            _stridePhase = 0f;
            _breathPhase = 0f;
            _breath = 0f;
            _drag = 0f;
            _landingStrength = 0f;
            _landingElapsed = float.MaxValue;
            _landingCooldown = 0f;
            _previousDescent = 0f;
            _flinchElapsed = float.MaxValue;
            _translation = Vector3.zero;
            _rotation = Quaternion.identity;
        }

        /// <summary>
        /// Advances every accumulator and recomputes the offset. Does not write the
        /// transform — <see cref="Apply"/> does, so a test can inspect the numbers
        /// without a camera existing.
        /// </summary>
        /// <param name="deltaSeconds">Step length, seconds. Zero or negative does nothing.</param>
        public void Tick(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || deltaSeconds <= 0f)
            {
                return;
            }

            var speed = _motor != null ? _motor.GroundSpeed : 0f;
            var descent = _motor != null ? Mathf.Max(0f, -_motor.WorldVelocity.y) : 0f;
            var grounded = _motor == null || _motor.IsGrounded;

            // Feet on the floor, as the stride understands it. Not `isGrounded` alone:
            // PlayerMotor's own gravity comment records that the flag flickers on slopes
            // and stairs, and §12 builds seven stairwells across five storeys — a stride
            // that stopped and started down every flight would be the most visible thing
            // in the game. So the flag is taken as sufficient and a genuine descent is
            // taken as the disqualifier, which agrees with it everywhere except the
            // flicker.
            var onFoot = grounded || descent <= ViewMotionTuning.LandingIgnoredBelowMps;

            AdvanceLanding(descent, deltaSeconds);
            AdvanceStride(speed, onFoot, deltaSeconds);
            AdvanceDrag(speed, deltaSeconds);
            AdvanceBreath(deltaSeconds);

            _flinchElapsed += deltaSeconds;
            _trembleTime += deltaSeconds;

            Compose(speed, onFoot);
        }

        /// <summary>
        /// Writes the offset onto the camera. Separate from <see cref="Tick"/> so a
        /// capture rig can pose an exact moment, and so the whole component can be
        /// exercised headlessly.
        /// </summary>
        public void Apply()
        {
            var target = _cameraTransform;
            if (target == null)
            {
                return;
            }

            CaptureRest();
            target.localPosition = _restPosition + _translation;
            target.localRotation = _restRotation * _rotation;
        }

        /// <summary>
        /// The distance between two footfalls at a given ground speed, as a multiple
        /// of <see cref="ViewMotionTuning.StrideMetres"/>.
        /// <para>
        /// People do not run by taking walking steps faster; they lengthen the step
        /// as well, and step length rises roughly with the square root of speed. It
        /// matters here because the alternative — a fixed 0.8 m pace — puts §04's
        /// 주자 at seven footfalls a second while sprinting, which sounds like a
        /// machine and not like a person. With this factor §06's three speeds land
        /// at about 150, 225 and 250 steps per minute, which are the real figures for
        /// a walk, a run and a sprint.
        /// </para>
        /// <para>
        /// Public and static because <see cref="PlayerFootsteps"/>'s no-animator
        /// cadence has to agree with the stride the camera is riding, or the sound
        /// arrives off the bottom of the step.
        /// </para>
        /// </summary>
        /// <param name="groundSpeed">Horizontal speed, m/s. Below §06's 걷기 the factor is 1.</param>
        public static float StepLengthFactor(float groundSpeed)
        {
            if (float.IsNaN(groundSpeed) || groundSpeed <= GameConstants.WalkSpeed)
            {
                return 1f;
            }

            return Mathf.Sqrt(groundSpeed / GameConstants.WalkSpeed);
        }

        /// <summary>
        /// Interpolates a stride half-amplitude for a ground speed: zero at a
        /// standstill, <paramref name="walkAmplitude"/> at §06's 걷기 2.0 and
        /// <paramref name="runAmplitude"/> at its 달리기 4.5, and it keeps climbing
        /// past that so §04's 주자 질주 is heavier than a run rather than identical
        /// to it.
        /// </summary>
        /// <param name="groundSpeed">Horizontal speed, m/s.</param>
        /// <param name="walkAmplitude">Value at §06's 걷기, in whatever unit the caller is working in.</param>
        /// <param name="runAmplitude">Value at §06's 달리기, same unit.</param>
        public static float StrideAmplitudeAt(float groundSpeed, float walkAmplitude, float runAmplitude)
        {
            if (float.IsNaN(groundSpeed) || groundSpeed <= 0f)
            {
                return 0f;
            }

            // Two ramps rather than one, so the curve passes exactly through §06's
            // two named speeds instead of near them.
            var toWalk = Mathf.Clamp01(groundSpeed / GameConstants.WalkSpeed);

            // Divided out by hand rather than with Mathf.InverseLerp, which clamps to
            // 0..1 and would therefore report §04's 질주 5.6 as identical to 달리기
            // 4.5 — the one comparison this function exists to make. 5.6 is 1.44 of
            // the way from 걷기 to 달리기 and the extra weight is the point; the
            // clamp at that ratio is so a physics glitch cannot drive the camera
            // through the ceiling.
            var span = GameConstants.RunSpeed - GameConstants.WalkSpeed;
            if (span <= 0f)
            {
                return walkAmplitude * toWalk;
            }

            var sprintRatio = (GameConstants.RunnerSprintSpeed - GameConstants.WalkSpeed) / span;
            var pastWalk = Mathf.Clamp((groundSpeed - GameConstants.WalkSpeed) / span, 0f, sprintRatio);

            return (walkAmplitude * toWalk) + ((runAmplitude - walkAmplitude) * pastWalk);
        }

        private void Reset()
        {
            _motor = GetComponent<PlayerMotor>();
            _animator = GetComponent<PlayerAnimatorDriver>();
            var camera = GetComponentInChildren<Camera>();
            _cameraTransform = camera != null ? camera.transform : null;
        }

        private void Awake()
        {
            if (_motor == null)
            {
                _motor = GetComponentInParent<PlayerMotor>();
            }

            if (_animator == null)
            {
                _animator = GetComponentInParent<PlayerAnimatorDriver>();
            }

            if (_cameraTransform == null)
            {
                var camera = GetComponentInChildren<Camera>();
                _cameraTransform = camera != null ? camera.transform : null;
            }

            CaptureRest();
        }

        private void OnEnable()
        {
            if (_animator != null && !_subscribed)
            {
                _animator.Footfall += OnFootfall;
                _subscribed = true;
            }
        }

        private void OnDisable()
        {
            if (_animator != null && _subscribed)
            {
                _animator.Footfall -= OnFootfall;
            }

            _subscribed = false;

            // Put the camera back where the rig authored it. Leaving it two
            // centimetres low when the component is switched off is the kind of
            // defect that survives a whole project.
            ResetMotion();
            Apply();
        }

        private void LateUpdate()
        {
            // LateUpdate, and after PlayerCameraRig's execution order: the rig moves
            // the pivot onto the head bone this frame, and an offset applied before
            // it would be an offset against last frame's head.
            if (!TickedExternally)
            {
                Tick(Time.deltaTime);
            }

            Apply();
        }

        /// <summary>
        /// Pins the stride to the sound: snaps the cycle to whichever of its two low
        /// points is nearer.
        /// <para>
        /// This is the whole reason the phase is a field rather than a function of
        /// distance travelled. The animator fires <c>Footfall</c> off the phase of
        /// the clip that is playing, which is what <see cref="PlayerFootsteps"/>
        /// plays the step on; snapping the bob means the eye reaches the bottom of
        /// the step at the instant the foot is heard landing. Left unsynchronised the
        /// two drift apart within a few seconds — both are periodic and neither is
        /// exactly the other's period — and a footstep that lands on the way up is
        /// the single most legible way to make a first-person game feel wrong.
        /// </para>
        /// <para>
        /// Public because the animator is not the only possible source: §13 will
        /// replicate a remote player's gait, and a rig with no <c>Animator</c> at all
        /// still has a cadence.
        /// </para>
        /// </summary>
        public void SyncToFootfall()
        {
            _stridePhase = _stridePhase < 0.25f || _stridePhase >= 0.75f ? 0f : 0.5f;
        }

        private void OnFootfall()
        {
            SyncToFootfall();
        }

        private void AdvanceStride(float speed, bool onFoot, float deltaSeconds)
        {
            if (!onFoot || speed <= ViewMotionTuning.StrideIdleSpeed)
            {
                // The phase is left where it is rather than reset. Amplitude already
                // scales to zero at a standstill, so the cycle stops in place and
                // resumes from the same foot instead of jumping to the other one.
                return;
            }

            var cycle = ViewMotionTuning.StrideCycleMetres * StepLengthFactor(speed);
            if (cycle <= 0f)
            {
                return;
            }

            _stridePhase = Mathf.Repeat(_stridePhase + (speed * deltaSeconds / cycle), 1f);
        }

        private void AdvanceDrag(float speed, float deltaSeconds)
        {
            var multiplier = _motor != null
                ? SpeedResolver.DirectionalMultiplier(_motor.LastInput)
                : GameConstants.MulForward;

            // Normalised against §05's own two ends, so a retune of MulBackward moves
            // the lean with it and the two can never disagree about what "worst" is.
            var span = GameConstants.MulForward - GameConstants.MulBackward;
            var raw = span > 0f ? Mathf.Clamp01((GameConstants.MulForward - multiplier) / span) : 0f;

            // Leaning against a direction you are not travelling in is a tilt, not
            // effort, so the lean fades in with the motion that pays for it — and it
            // is measured against the speed this heading is *entitled* to, not
            // against §06's 걷기. Dividing by the walk speed made §05's worst row its
            // own excuse: backing away is 65 % of a walk by construction, so the lean
            // for it came out at 65 % of full at the exact heading the design says is
            // fatal. Against the afforded speed, a player pressed into a wall still
            // gets nothing and a player actually backing away gets all of it.
            var afforded = GameConstants.WalkSpeed * multiplier;
            var target = afforded > 0f ? raw * Mathf.Clamp01(speed / afforded) : 0f;
            _drag = Approach(_drag, target, deltaSeconds, ViewMotionTuning.DragFollowSeconds);
        }

        private void AdvanceLanding(float descent, float deltaSeconds)
        {
            _landingElapsed += deltaSeconds;
            _landingCooldown -= deltaSeconds;

            // A landing is descent being arrested, which is why the previous frame's
            // figure is the one that sizes it: by the time the controller reports
            // isGrounded the speed that mattered has already been thrown away.
            var arrested = _previousDescent > ViewMotionTuning.LandingIgnoredBelowMps
                && descent < _previousDescent * 0.5f;

            if (arrested && _landingCooldown <= 0f)
            {
                _landingStrength = Mathf.Clamp01(Mathf.InverseLerp(
                    ViewMotionTuning.LandingIgnoredBelowMps,
                    ViewMotionTuning.LandingFullMps,
                    _previousDescent));
                _landingElapsed = 0f;
                _landingCooldown = ViewMotionTuning.LandingRefractorySeconds;
            }

            _previousDescent = descent;
        }

        private void AdvanceBreath(float deltaSeconds)
        {
            // §06's bar, inverted: a full bar is a person who has not run yet.
            // Non-Runners never drain it, so they never get more than the resting
            // share — which is correct, because §04 gives 질주 to the 주자 alone.
            var spent = _motor != null ? 1f - Mathf.Clamp01(_motor.Stamina.Fraction) : 0f;
            _breath = Approach(_breath, spent, deltaSeconds, ViewMotionTuning.BreathFollowSeconds);

            var rate = Mathf.Lerp(ViewMotionTuning.BreathRateRestedHz, ViewMotionTuning.BreathRateSpentHz, _breath);
            _breathPhase = Mathf.Repeat(_breathPhase + (rate * deltaSeconds), 1f);
        }

        private void Compose(float speed, bool onFoot)
        {
            if (_scale <= 0f)
            {
                _translation = Vector3.zero;
                _rotation = Quaternion.identity;
                return;
            }

            var strideGain = 1f + (ViewMotionTuning.DragStrideGain * _drag);
            var moving = onFoot && speed > ViewMotionTuning.StrideIdleSpeed;

            var vertical = 0f;
            var lateral = 0f;
            var pitch = 0f;
            var roll = 0f;

            if (moving)
            {
                var theta = _stridePhase * 2f * Mathf.PI;

                // Vertical dips twice per cycle — once under each foot — and lateral
                // sways once, which is the figure-eight a walking head traces. The
                // minima of the vertical term sit at phase 0 and 0.5, which is where
                // OnFootfall pins the phase.
                var verticalAmp = StrideAmplitudeAt(
                    speed, ViewMotionTuning.StrideVerticalWalk, ViewMotionTuning.StrideVerticalRun) * strideGain;
                var lateralAmp = StrideAmplitudeAt(
                    speed, ViewMotionTuning.StrideLateralWalk, ViewMotionTuning.StrideLateralRun) * strideGain;
                var rollAmp = StrideAmplitudeAt(
                    speed, ViewMotionTuning.StrideRollWalkDegrees, ViewMotionTuning.StrideRollRunDegrees);

                vertical -= verticalAmp * Mathf.Cos(2f * theta);
                lateral += lateralAmp * Mathf.Sin(theta);
                roll -= rollAmp * Mathf.Sin(theta);
            }

            // §05's cost. Positive pitch is downward in Unity, so a lean away from
            // the direction of travel is negative.
            pitch -= ViewMotionTuning.DragPitchDegrees * _drag;

            var landing = Landing;
            if (landing > 0f)
            {
                var shape = Impulse(_landingElapsed / ViewMotionTuning.LandingRecoverSeconds, 0.18f) * landing;
                vertical -= ViewMotionTuning.LandingDipMetres * shape;
                pitch += ViewMotionTuning.LandingPitchDegrees * shape;
            }

            if (Flinching)
            {
                var shape = Impulse(_flinchElapsed / ViewMotionTuning.FlinchSeconds, 0.10f);
                vertical -= ViewMotionTuning.FlinchDipMetres * shape;
                pitch += ViewMotionTuning.FlinchPitchDegrees * shape;
            }

            var breathAmount = Mathf.Lerp(ViewMotionTuning.BreathRestedShare, 1f, _breath);
            var breathWave = Mathf.Sin(_breathPhase * 2f * Mathf.PI);
            vertical += ViewMotionTuning.BreathVerticalMetres * breathAmount * breathWave;
            pitch += ViewMotionTuning.BreathPitchDegrees * breathAmount * breathWave;

            var dread = Mathf.Clamp01(Dread01);
            if (dread > 0f)
            {
                var t = _trembleTime * ViewMotionTuning.DreadTrembleHz;
                var amplitude = ViewMotionTuning.DreadTrembleMetres * dread;

                // Two decorrelated Perlin lanes rather than a sine: a periodic
                // tremble reads as a mechanism, and this one has to read as a hand.
                lateral += (Mathf.PerlinNoise(t, 0.137f) - 0.5f) * 2f * amplitude;
                vertical += (Mathf.PerlinNoise(0.511f, t) - 0.5f) * 2f * amplitude;
            }

            var translation = new Vector3(lateral, vertical, 0f) * _scale;
            var magnitude = translation.magnitude;
            if (magnitude > ViewMotionTuning.MaxTranslationMetres)
            {
                translation *= ViewMotionTuning.MaxTranslationMetres / magnitude;
            }

            // Yaw is deliberately never written. It would move the crosshair, and
            // §05 makes where the player is pointing a promise to three other people.
            var euler = new Vector3(pitch, 0f, roll) * _scale;
            euler = Vector3.ClampMagnitude(euler, ViewMotionTuning.MaxRotationDegrees);

            _translation = translation;
            _rotation = Quaternion.Euler(euler);
        }

        private void CaptureRest()
        {
            if (_restCaptured || _cameraTransform == null)
            {
                return;
            }

            _restPosition = _cameraTransform.localPosition;
            _restRotation = _cameraTransform.localRotation;
            _restCaptured = true;
        }

        /// <summary>
        /// A hard attack and a squared decay, 0–1. The same shape
        /// <c>MonsterAcquireTell</c> uses and for the same reason: a symmetric pulse
        /// reads as decoration and an asymmetric one reads as a reaction.
        /// </summary>
        private static float Impulse(float t, float attack)
        {
            if (t <= 0f)
            {
                return 0f;
            }

            if (t >= 1f)
            {
                return 0f;
            }

            if (t <= attack)
            {
                return attack > 0f ? t / attack : 1f;
            }

            var decay = Mathf.Clamp01((t - attack) / Mathf.Max(1e-4f, 1f - attack));
            return (1f - decay) * (1f - decay);
        }

        /// <summary>
        /// Exponential approach, frame-rate independent. A per-frame lerp would make
        /// the lean arrive at a different time on a 60 Hz machine than on a 144 Hz
        /// one, and §14's questions are asked on whatever machine the tester has.
        /// </summary>
        private static float Approach(float current, float target, float deltaSeconds, float seconds)
        {
            if (seconds <= 0f)
            {
                return target;
            }

            return target + ((current - target) * Mathf.Exp(-deltaSeconds / seconds));
        }
    }
}
