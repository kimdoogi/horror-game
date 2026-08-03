#nullable enable

using System.Collections;
using HorrorGame.Core;
using HorrorGame.Core.Movement;
using HorrorGame.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.PlayerRig
{
    /// <summary>
    /// What the camera is allowed to do on its own, and — more importantly — what it is
    /// not.
    /// <para>
    /// §14 says questions 1 and 2 decide the project and that 「직접 만져봐야 나온다」,
    /// so nothing here claims the chase is fun. What these tests can do is pin the
    /// properties that would make the answer meaningless if they broke: that the step
    /// you hear is the bottom of the step you feel, that §05's directional cost is
    /// analogue rather than four branches, that the eye stays inside a stated bound,
    /// that the camera never becomes a free bearing on the monster, and that turning it
    /// off leaves the game exactly as it was.
    /// </para>
    /// </summary>
    public sealed class PlayerViewMotionTests
    {
        private const float Step = 1f / 50f;

        private GameObject? _floor;
        private GameObject? _body;

        [TearDown]
        public void TearDown()
        {
            if (_body != null)
            {
                Object.DestroyImmediate(_body);
                _body = null;
            }

            if (_floor != null)
            {
                Object.DestroyImmediate(_floor);
                _floor = null;
            }
        }

        [UnityTest]
        public IEnumerator ViewMotion_AtZeroScale_LeavesTheCameraExactlyWhereTheRigPutIt()
        {
            var rig = BuildRig();
            yield return null;
            Settle(rig);

            var rest = rig.Camera.localPosition;
            var restRotation = rig.Camera.localRotation;

            rig.View.Scale = 0f;
            rig.View.Dread01 = 1f;
            rig.View.Flinch();
            Drive(rig, new MoveInput(1f, 0f, true), 2f);
            rig.View.Apply();

            Assert.That(rig.View.Translation, Is.EqualTo(Vector3.zero));
            Assert.That(Quaternion.Angle(Quaternion.identity, rig.View.Rotation), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Vector3.Distance(rig.Camera.localPosition, rest), Is.EqualTo(0f).Within(1e-5f),
                "Zero has to be exactly the game without this component. It is the answer given to a "
                + "player who feels sick, and a 'mostly off' would make that answer a lie.");
            Assert.That(Quaternion.Angle(rig.Camera.localRotation, restRotation), Is.EqualTo(0f).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator TheStrideIsAtItsLowestWhereTheFootstepPlays()
        {
            var rig = BuildRig();
            yield return null;
            Settle(rig);

            // Walk for a few seconds and find the frame the eye was lowest on. It has to
            // be one of the two phases SyncToFootfall pins the cycle to, or the sound and
            // the sensation are describing different steps.
            var lowest = float.MaxValue;
            var lowestPhase = -1f;

            for (var i = 0; i < 300; i++)
            {
                rig.Motor.Step(new MoveInput(1f, 0f, false), Step);
                rig.View.Tick(Step);

                if (rig.View.Translation.y < lowest)
                {
                    lowest = rig.View.Translation.y;
                    lowestPhase = rig.View.StridePhase;
                }
            }

            Assert.That(lowest, Is.LessThan(-ViewMotionTuning.StrideVerticalWalk * 0.8f),
                "the stride produced no dip at all at §06's 걷기 speed");
            Assert.That(
                Mathf.Min(Mathf.Abs(lowestPhase), Mathf.Abs(lowestPhase - 0.5f), Mathf.Abs(lowestPhase - 1f)),
                Is.LessThan(0.06f),
                "the eye bottomed out at phase " + lowestPhase + ", which is not where a foot lands");
        }

        [UnityTest]
        public IEnumerator AFootfallSnapsTheStrideToTheStepWhereverTheCycleHadDrifted()
        {
            var rig = BuildRig();
            yield return null;
            Settle(rig);

            // Ten different amounts of walking, so the phase is somewhere different each
            // time — which is the situation a real animator produces, because the clip's
            // period and the bob's are not the same number.
            for (var run = 1; run <= 10; run++)
            {
                Drive(rig, new MoveInput(1f, 0f, false), run * 0.07f);

                var before = rig.View.StridePhase;
                rig.View.SyncToFootfall();
                var after = rig.View.StridePhase;

                Assert.That(
                    Mathf.Min(Mathf.Abs(after), Mathf.Abs(after - 0.5f)),
                    Is.LessThan(1e-5f),
                    "a footfall at phase " + before + " left the stride at " + after
                    + "; it has to land on one of the cycle's two low points");

                // And on the nearer one — snapping to the far foot would put the eye at the
                // top of the step on the frame the step is heard, which is worse than drift.
                var expected = before < 0.25f || before >= 0.75f ? 0f : 0.5f;
                Assert.That(after, Is.EqualTo(expected).Within(1e-5f));
            }
        }

        [UnityTest]
        public IEnumerator Section05sCostIsContinuousInTheHeadingRatherThanFourBranches()
        {
            // §05 is explicit that the penalty is analogue: "이산적 선택이 아니라 아날로그
            // 조절이라는 점이 중요하다 — 마우스를 몇 도 돌릴지가 곧 실력이다." A lean derived
            // from which key is down would satisfy every documented row of the table and
            // still turn the 45° peek from a skill into a button.
            var rig = BuildRig();
            yield return null;

            var previous = float.NegativeInfinity;
            var largestJump = 0f;
            var forwardPitch = 0f;
            var backwardPitch = 0f;

            for (var degrees = 0f; degrees <= 180f; degrees += 15f)
            {
                Recentre(rig);

                var radians = degrees * Mathf.Deg2Rad;
                Drive(rig, new MoveInput(Mathf.Cos(radians), Mathf.Sin(radians), false), 2f);

                var drag = rig.View.Drag;
                Assert.That(drag, Is.GreaterThanOrEqualTo(previous - 1e-3f),
                    "the lean fell going from a smaller heading offset to " + degrees
                    + " degrees; §05's cost only ever rises with the angle");

                if (previous > float.NegativeInfinity)
                {
                    largestJump = Mathf.Max(largestJump, drag - previous);
                }

                if (degrees <= 0f)
                {
                    forwardPitch = -PitchOf(rig.View.Rotation);
                }

                if (degrees >= 180f)
                {
                    backwardPitch = -PitchOf(rig.View.Rotation);
                }

                previous = drag;
            }

            Assert.That(largestJump, Is.LessThan(0.2f),
                "a step of " + largestJump + " across 15 degrees of heading is a switch, not an "
                + "analogue control");
            Assert.That(previous, Is.GreaterThan(0.9f), "straight back should reach §05's worst row");

            // And the lean actually reaches the camera. §05's whole argument — "뒷걸음은
            // 괴물보다 느리다. 뒤를 보면 잡힌다" — costs the player 35 % of their speed and,
            // until this existed, felt like nothing at all.
            Assert.That(backwardPitch - forwardPitch,
                Is.GreaterThan(ViewMotionTuning.DragPitchDegrees * 0.6f),
                "backing away tilted the view by " + (backwardPitch - forwardPitch)
                + " degrees more than running forward, which is not enough to feel");
        }

        [UnityTest]
        public IEnumerator ALandingScalesWithTheDropAndIsGoneBeforeTheNextStep()
        {
            var rig = BuildRig();
            yield return null;
            Settle(rig);
            var shallow = DropAndPeek(rig, 0.3f);

            TearDown();
            rig = BuildRig();
            yield return null;
            Settle(rig);
            var deep = DropAndPeek(rig, 2.5f);

            Assert.That(shallow, Is.GreaterThan(0f),
                "§12 stacks five storeys and seven 계단; a step down that weighed nothing would make "
                + "most of the match weightless");
            Assert.That(deep, Is.GreaterThan(shallow * 1.5f),
                "a 2.5 m drop produced " + deep + " and a 0.3 m step produced " + shallow
                + "; one rule has to size both, or the stairs and the gantry feel the same");
            Assert.That(deep,
                Is.LessThanOrEqualTo(ViewMotionTuning.LandingDipMetres + ViewMotionTuning.BreathVerticalMetres));

            // And it is over quickly. A recovery that outlasted a stride would still be
            // springing back when the next foot lands.
            rig.View.Tick(ViewMotionTuning.LandingRecoverSeconds + Step);
            Assert.That(rig.View.Landing, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator TheBreathRisesWhenSection06sBarEmptiesAndOutlastsItsRefill()
        {
            var rig = BuildRig();
            yield return null;
            Settle(rig);

            var rested = rig.View.Breath;

            // §06 gives the Runner 12 s of 질주. Spend all of it.
            Drive(rig, new MoveInput(1f, 0f, true), GameConstants.SprintStaminaSeconds + 1f);

            Assert.That(rig.Motor.Stamina.Fraction, Is.LessThan(0.05f), "the bar should be spent");
            Assert.That(rig.View.Breath, Is.GreaterThan(0.5f),
                "§06's exhaustion is otherwise invisible: the bar empties, the Runner drops from 5.6 "
                + "to 4.5, and nothing in the world says so");
            Assert.That(rig.View.Breath, Is.GreaterThan(rested));

            // §06 makes the 20 s recovery the Runner's vulnerable window. The breath has to
            // still be there while the bar looks healthy again, or the body says "safe" at
            // the exact moment the rules say otherwise.
            Drive(rig, new MoveInput(0f, 0f, false), GameConstants.SprintStaminaRecoverySeconds * 0.5f);
            Assert.That(rig.Motor.Stamina.Fraction, Is.GreaterThan(0.4f), "the bar should be well into its refill");
            Assert.That(rig.View.Breath, Is.GreaterThan(0.15f),
                "the breath dropped with the bar instead of lagging it");

            Drive(rig, new MoveInput(0f, 0f, false), GameConstants.SprintStaminaRecoverySeconds);
            Assert.That(rig.View.Breath, Is.LessThan(0.1f), "and it does come back down");
        }

        [UnityTest]
        public IEnumerator TheAcquisitionFlinchIsOverBeforeSection06sFirstSightBreak()
        {
            var rig = BuildRig();
            yield return null;
            Settle(rig);

            Assert.That(ViewMotionTuning.FlinchSeconds,
                Is.LessThan(GameConstants.AggroReleaseLineOfSightBreak),
                "§06 gives the Runner 3 s of broken sight to release. A flinch still running at that "
                + "moment is a camera shaking while the player decides whether they lost it.");

            rig.View.Flinch();
            rig.View.Tick(Step);
            Assert.That(rig.View.Flinching, Is.True);

            var peak = 0f;
            for (var t = 0f; t < ViewMotionTuning.FlinchSeconds; t += Step)
            {
                rig.View.Tick(Step);
                peak = Mathf.Max(peak, PitchOf(rig.View.Rotation));
            }

            Assert.That(peak, Is.GreaterThan(ViewMotionTuning.FlinchPitchDegrees * 0.5f),
                "a flinch nobody sees is not an announcement");

            rig.View.Tick(Step);
            Assert.That(rig.View.Flinching, Is.False);
        }

        [UnityTest]
        public IEnumerator DreadMovesTheEyeWithoutTellingItWhichWayToLook()
        {
            var rig = BuildRig();
            yield return null;
            Settle(rig);

            rig.View.Dread01 = 1f;

            var sumX = 0f;
            var sumY = 0f;
            var worst = 0f;
            const int Samples = 2000;

            for (var i = 0; i < Samples; i++)
            {
                rig.View.Tick(Step);
                var offset = rig.View.Translation;
                sumX += offset.x;
                sumY += offset.y;
                worst = Mathf.Max(worst, offset.magnitude);
            }

            // §04 sells the monster's 위치 to the 청음사 and §11 makes the 관측자's read the one
            // thing that cannot be bought. A tremble with a bias would hand out a bearing.
            Assert.That(Mathf.Abs(sumX / Samples), Is.LessThan(ViewMotionTuning.DreadTrembleMetres * 0.5f),
                "the tremble leans sideways on average, which is a direction nobody paid for");
            Assert.That(worst, Is.LessThan(0.02f),
                "at full dread the eye moved " + worst + " m. DangerSense's argument for allowing a "
                + "proximity signal at all is that it is too blunt to navigate by, and that only "
                + "holds while this stays small.");

            // The vertical lane carries the breath too, so it is checked against the
            // breath's own amplitude rather than against zero.
            Assert.That(Mathf.Abs(sumY / Samples), Is.LessThan(ViewMotionTuning.BreathVerticalMetres));
        }

        [UnityTest]
        public IEnumerator TheEyeStaysInsideItsStatedBoundsAndNeverYaws()
        {
            var rig = BuildRig();
            yield return null;
            Settle(rig);

            rig.View.Dread01 = 1f;

            var worstTranslation = 0f;
            var worstRotation = 0f;
            var worstYaw = 0f;

            // Sprinting backwards, exhausted, flinching, landing off a two-metre drop. The
            // frame nobody previews: every component is bounded on its own and they add.
            for (var round = 0; round < 4; round++)
            {
                rig.Body.transform.position += new Vector3(0f, 2f, 0f);
                rig.View.Flinch();

                for (var i = 0; i < 200; i++)
                {
                    rig.Motor.Step(new MoveInput(-1f, 0.4f, true), Step);
                    rig.View.Tick(Step);

                    worstTranslation = Mathf.Max(worstTranslation, rig.View.Translation.magnitude);
                    worstRotation = Mathf.Max(worstRotation, Quaternion.Angle(Quaternion.identity, rig.View.Rotation));

                    // The camera's forward may pitch and the horizon may roll; it may not
                    // swing sideways, because §05 makes where a player points a promise to
                    // three other people — "남의 손전등 방향이 정보다".
                    worstYaw = Mathf.Max(worstYaw, Mathf.Abs((rig.View.Rotation * Vector3.forward).x));
                }
            }

            Assert.That(worstTranslation, Is.LessThanOrEqualTo(ViewMotionTuning.MaxTranslationMetres + 1e-4f),
                "the worst case has to be a stated number rather than an emergent one");
            Assert.That(worstRotation, Is.LessThanOrEqualTo(ViewMotionTuning.MaxRotationDegrees + 1e-2f));
            Assert.That(worstYaw, Is.LessThan(1e-4f), "the camera yawed, which moves the crosshair");
        }

        [Test]
        public void TheStrideLengthensWithSpeedSoASprintIsNotSevenStepsASecond()
        {
            // 150, 225 and 250 steps per minute are a real walk, run and sprint. A fixed
            // 0.8 m pace puts §04's 주자 at 420 — a sewing machine, not a person.
            var walk = Cadence(GameConstants.WalkSpeed);
            var run = Cadence(GameConstants.RunSpeed);
            var sprint = Cadence(GameConstants.RunnerSprintSpeed);

            Assert.That(walk, Is.EqualTo(150f).Within(15f), "걷기 cadence was " + walk + " steps/min");
            Assert.That(run, Is.EqualTo(225f).Within(20f), "달리기 cadence was " + run + " steps/min");
            Assert.That(sprint, Is.EqualTo(250f).Within(20f), "질주 cadence was " + sprint + " steps/min");
            Assert.That(sprint, Is.GreaterThan(run), "and sprinting is still quicker than running");
        }

        [Test]
        public void TheStrideKeepsGettingHeavierPastRunningSoASprintReads()
        {
            var walk = Vertical(GameConstants.WalkSpeed);
            var run = Vertical(GameConstants.RunSpeed);
            var sprint = Vertical(GameConstants.RunnerSprintSpeed);

            Assert.That(PlayerViewMotion.StrideAmplitudeAt(0f, 1f, 2f), Is.EqualTo(0f));
            Assert.That(walk, Is.EqualTo(ViewMotionTuning.StrideVerticalWalk).Within(1e-5f));
            Assert.That(run, Is.EqualTo(ViewMotionTuning.StrideVerticalRun).Within(1e-5f));
            Assert.That(sprint, Is.GreaterThan(run),
                "§04's 주자 질주 is the role's identity and it has to feel unlike a run");

            // Bounded even if physics hands it an absurd speed.
            Assert.That(Vertical(500f), Is.EqualTo(sprint).Within(1e-5f));
        }

        // ------------------------------------------------------------------- fixtures

        private static float Vertical(float speed)
        {
            return PlayerViewMotion.StrideAmplitudeAt(
                speed, ViewMotionTuning.StrideVerticalWalk, ViewMotionTuning.StrideVerticalRun);
        }

        private static float Cadence(float speed)
        {
            var step = ViewMotionTuning.StrideMetres * PlayerViewMotion.StepLengthFactor(speed);
            return speed / step * 60f;
        }

        private static float PitchOf(Quaternion rotation)
        {
            return Mathf.DeltaAngle(0f, rotation.eulerAngles.x);
        }

        /// <summary>
        /// Runs the motor and the camera together for a wall-clock duration, at the fixed
        /// step §13 puts the host on.
        /// </summary>
        private static void Drive(Rig rig, MoveInput input, float seconds)
        {
            var steps = Mathf.RoundToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                rig.Motor.Step(input, Step);
                rig.View.Tick(Step);
            }
        }

        /// <summary>Puts the body back on the origin with a full bar and a still camera.</summary>
        private static void Recentre(Rig rig)
        {
            Teleport(rig, new Vector3(0f, 0.1f, 0f));
            rig.Motor.Stamina.Reset();
            Settle(rig);
        }

        /// <summary>
        /// Moves the body with the <see cref="CharacterController"/> switched off across
        /// the change.
        /// <para>
        /// The controller caches its own position and re-imposes it on the next
        /// <c>Move</c>, so a bare <c>transform.position</c> assignment is undone on the
        /// following step — and undone as a single frame of travel, which the camera
        /// correctly reads as an enormous descent. A 0.3 m step and a 2.5 m drop both
        /// came out at a full-strength landing that way, which is a true statement about
        /// a teleport and a useless one about a fall. Production code that really does
        /// teleport a player — §09's respawn — calls
        /// <see cref="PlayerViewMotion.ResetMotion"/> for the same reason.
        /// </para>
        /// </summary>
        private static void Teleport(Rig rig, Vector3 position)
        {
            var controller = rig.Body.GetComponent<CharacterController>();
            controller.enabled = false;
            rig.Body.transform.position = position;
            controller.enabled = true;
        }

        /// <summary>Lifts the body, lets it fall, and returns how far the eye dropped.</summary>
        private static float DropAndPeek(Rig rig, float height)
        {
            Teleport(rig, rig.Body.transform.position + new Vector3(0f, height, 0f));

            var deepest = 0f;
            for (var i = 0; i < 200; i++)
            {
                rig.Motor.Step(new MoveInput(0f, 0f, false), Step);
                rig.View.Tick(Step);
                deepest = Mathf.Max(deepest, -rig.View.Translation.y);
            }

            return deepest;
        }

        private static void Settle(Rig rig)
        {
            for (var i = 0; i < 40; i++)
            {
                rig.Motor.Step(new MoveInput(0f, 0f, false), Step);
                rig.View.Tick(Step);
            }

            rig.View.ResetMotion();
        }

        private Rig BuildRig()
        {
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "ViewMotionFloor";
            _floor.transform.localScale = new Vector3(400f, 1f, 400f);
            _floor.transform.position = new Vector3(0f, -0.5f, 0f);

            _body = new GameObject("ViewMotionPlayer");
            _body.transform.position = new Vector3(0f, 0.1f, 0f);

            var controller = _body.AddComponent<CharacterController>();
            controller.height = 1.75f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.875f, 0f);

            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(_body.transform, false);
            pivot.localPosition = new Vector3(0f, 1.63f, 0f);

            var camera = new GameObject("Cam").AddComponent<Camera>();
            camera.transform.SetParent(pivot, false);

            var look = _body.AddComponent<PlayerLook>();
            look.PitchPivot = pivot;


            var motor = _body.AddComponent<PlayerMotor>();

            // No role is assigned, and that IS the assignment. DESCENT-PIVOT §7 step 7 ran
            // on 2026-08-03 and §04 has no 직업 left: 20 runners start with the same body,
            // and 질주 belongs to all of them (§04, 「질주는 남는다 — 전원에게, 체력으로」).
            // The line that used to stand here read `motor.Role = RoleId.Runner` and was
            // the difference between a rig that could sprint and one that could not.

            // Nothing is feeding input, so Update must not step either of them; every test
            // drives both with an explicit delta, which is also how §13's host will.
            motor.SteppedExternally = true;

            var view = _body.AddComponent<PlayerViewMotion>();
            view.TickedExternally = true;

            return new Rig(_body, motor, view, camera.transform);
        }

        private readonly struct Rig
        {
            internal Rig(GameObject body, PlayerMotor motor, PlayerViewMotion view, Transform camera)
            {
                Body = body;
                Motor = motor;
                View = view;
                Camera = camera;
            }

            internal GameObject Body { get; }

            internal PlayerMotor Motor { get; }

            internal PlayerViewMotion View { get; }

            internal Transform Camera { get; }
        }
    }
}
