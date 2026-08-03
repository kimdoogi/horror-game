#nullable enable

using System.Globalization;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Threat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// A single-player rig for answering §14's first two validation questions before any
    /// networking, map or monster exists.
    /// <para>
    /// §14 says of its five questions: "우선순위: 1·2번이 안 되면 나머지를 아무리 잘
    /// 만들어도 안 된다. 어그로 수치와 속도 배율에 게임이 걸려 있다. 그리고 이건 문서로
    /// 정할 수 없고 직접 만져봐야 나온다." The last clause is the whole reason this file
    /// exists — the answers cannot be derived, only felt. But "felt" is not the same as
    /// "guessed at", so everything the feeling depends on is on screen as a number while
    /// it is being felt: the resolved speed, the §05 directional multiplier, the gap to
    /// the monster, and the one derived figure that states §05's dilemma outright —
    /// <see cref="PlayerMotor.SafePeekAngleDegrees"/>, the furthest a player can turn and
    /// still pull away.
    /// </para>
    /// <para>
    /// The pursuer is a <b>pacer</b>, not a monster: it walks straight at the player at
    /// <c>ThreatCurve</c>'s speed for the selected §07 tier and nothing else. §06's state
    /// machine already exists and is tested (<c>MonsterBrain</c>), but driving it needs a
    /// Unity <c>IWorldProbe</c> that the map layer has not built yet. A dumb pacer is the
    /// honest stand-in: questions 1 and 2 are about the <em>speed relationship</em>, and a
    /// pacer gets that exactly right while being obviously not the real thing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFeelHarness : MonoBehaviour
    {
        /// <summary>Monster.fbx stands 2.34 m (ASSETS.md). The pacer is that size so the thing behind you is not a toy.</summary>
        private const float MonsterHeightMetres = 2.34f;

        /// <summary>§12's five zone surfaces, laid out in order along the test straight.</summary>
        private static readonly FloorMaterial[] SurfaceStrip =
        {
            FloorMaterial.Wood,
            FloorMaterial.Tile,
            FloorMaterial.Gravel,
            FloorMaterial.Concrete,
            FloorMaterial.Metal,
        };

        private static readonly Color[] SurfaceColours =
        {
            new Color(0.42f, 0.29f, 0.17f),
            new Color(0.72f, 0.73f, 0.75f),
            new Color(0.46f, 0.44f, 0.38f),
            new Color(0.32f, 0.32f, 0.33f),
            new Color(0.28f, 0.31f, 0.36f),
        };

        [Header("Wiring (left empty, built at Start)")]
        [SerializeField]
        private PlayerMotor? _motor;

        [SerializeField]
        private PlayerCameraRig? _cameraRig;

        [SerializeField]
        private PlayerFootsteps? _footsteps;

        [SerializeField]
        private PlayerViewMotion? _viewMotion;

        [SerializeField]
        private PlayerInputRouter? _input;

        [Header("Environment")]
        [Tooltip("Build the surface strip, sight blockers and the pacer if the scene has none.")]
        [SerializeField]
        private bool _buildEnvironment = true;

        [Tooltip("Player.fbx stands 1.75 m (ASSETS.md). Only used when no rig is supplied.")]
        [SerializeField]
        private float _capsuleHeightMetres = 1.75f;

        private Transform? _pacer;
        private float _elapsedSeconds;
        private int _tierOverride = -1;
        private bool _pacerPaused;
        private bool _showHelp = true;
        private float _lastCaughtAfterSeconds = -1f;
        private float _chaseSeconds;
        private float _maxGapMetres;
        private float _lineOfSightBrokenSeconds;
        private bool _pacerGaveUp;
        private Texture2D? _bar;
        private GUIStyle? _style;

        /// <summary>The §07 tier being simulated. Overridable so all five monster speeds can be felt in one sitting.</summary>
        public ThreatTier Tier
        {
            get
            {
                return _tierOverride >= 0 ? ThreatCurve.Tier(_tierOverride) : ThreatCurve.At(_elapsedSeconds);
            }
        }

        /// <summary>Straight-line distance to the pacer, metres, or infinity when there is none.</summary>
        public float GapMetres
        {
            get
            {
                if (_pacer == null || _motor == null)
                {
                    return float.PositiveInfinity;
                }

                var a = _motor.transform.position;
                var b = _pacer.position;
                return new Vector2(a.x - b.x, a.z - b.z).magnitude;
            }
        }

        private void Start()
        {
            if (_buildEnvironment)
            {
                BuildEnvironmentIfMissing();
            }

            EnsurePlayer();
            EnsurePacer();
        }

        private void Update()
        {
            _elapsedSeconds += Time.deltaTime;
            ReadHarnessKeys();
            StepPacer(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (_bar != null)
            {
                Destroy(_bar);
            }
        }

        // ---------------------------------------------------------------- environment

        private void BuildEnvironmentIfMissing()
        {
            if (transform.Find("Surfaces") != null)
            {
                return;
            }

            var surfaces = new GameObject("Surfaces").transform;
            surfaces.SetParent(transform, false);

            // Five §12 surfaces, each one §12's maximum straight corridor long, which puts
            // the whole strip at §12's map extent. Walking the length of it is the fastest
            // way to hear whether the Listener's five identities are actually distinct.
            var patchLength = GameConstants.MaxStraightCorridor;
            for (var i = 0; i < SurfaceStrip.Length; i++)
            {
                var patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                patch.name = "Floor_" + SurfaceStrip[i];
                patch.transform.SetParent(surfaces, false);
                patch.transform.localScale = new Vector3(GameConstants.ZoneDiagonalMin, 1f, patchLength);
                patch.transform.localPosition = new Vector3(0f, -0.5f, (i * patchLength) + (patchLength * 0.5f));
                patch.AddComponent<FloorSurfaceTag>().FloorMaterial = SurfaceStrip[i];
                Paint(patch, SurfaceColours[i]);
            }

            var blockers = new GameObject("SightBlockers").transform;
            blockers.SetParent(transform, false);

            // §12 spaces sight-breaking points 15–25 m apart so a 60 m sprint gets three or
            // four chances at §06's aggro release. Alternating the two bounds is the
            // cheapest way to feel both ends of that range.
            var z = GameConstants.LineOfSightBreakSpacingMin;
            var alternate = false;
            while (z < patchLength * SurfaceStrip.Length)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Blocker_" + z.ToString("0", CultureInfo.InvariantCulture) + "m";
                wall.transform.SetParent(blockers, false);
                wall.transform.localScale = new Vector3(GameConstants.SCorridorLegLength, 3f, 0.5f);
                wall.transform.localPosition = new Vector3(alternate ? 5f : -5f, 1.5f, z);
                Paint(wall, new Color(0.18f, 0.18f, 0.2f));

                z += alternate ? GameConstants.LineOfSightBreakSpacingMin : GameConstants.LineOfSightBreakSpacingMax;
                alternate = !alternate;
            }

            var sun = new GameObject("HarnessLight").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 0.35f;
            sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            RenderSettings.ambientLight = new Color(0.12f, 0.12f, 0.15f);
        }

        private static void Paint(GameObject target, Color colour)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = renderer.sharedMaterial != null ? renderer.sharedMaterial.shader : null;
            }

            if (shader == null)
            {
                return;
            }

            var material = new Material(shader);
            material.color = colour;
            renderer.material = material;
        }

        private void EnsurePlayer()
        {
            if (_motor != null)
            {
                Adopt(_motor.gameObject);
                return;
            }

            var existing = FindAnyObjectByType<PlayerMotor>();
            if (existing != null)
            {
                Adopt(existing.gameObject);
                return;
            }

            // No rig in the scene: a capsule is enough to answer §14's questions 1 and 2,
            // which are about speed and turning, not about what a player looks like.
            var body = new GameObject("HarnessPlayer");
            body.transform.position = new Vector3(0f, _capsuleHeightMetres, 0f);

            var controller = body.AddComponent<CharacterController>();
            controller.height = _capsuleHeightMetres;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, _capsuleHeightMetres * 0.5f, 0f);
            controller.slopeLimit = 50f;

            // §12's climbing constraint, and the ceiling the jump apex is held under.
            controller.stepOffset = GameConstants.PlayerStepOffsetMetres;

            var pivot = new GameObject("PitchPivot").transform;
            pivot.SetParent(body.transform, false);

            // Eye just under the crown. Derived from the rig height rather than tuned:
            // §05's peek is about where the camera points, not how tall it is.
            pivot.localPosition = new Vector3(0f, _capsuleHeightMetres * 0.93f, 0f);

            var camera = new GameObject("HarnessCamera").AddComponent<Camera>();
            camera.transform.SetParent(pivot, false);
            camera.nearClipPlane = 0.05f;
            camera.gameObject.AddComponent<AudioListener>();

            _input = body.AddComponent<PlayerInputRouter>();

            var look = body.AddComponent<PlayerLook>();

            // Before the motor: PlayerMotor's Awake looks for one, and AddComponent runs
            // Awake immediately, so the order here is the wiring.
            body.AddComponent<PlayerStance>();
            _motor = body.AddComponent<PlayerMotor>();
            _cameraRig = body.AddComponent<PlayerCameraRig>();
            body.AddComponent<PlayerFlashlight>();
            _footsteps = body.AddComponent<PlayerFootsteps>();
            _viewMotion = body.AddComponent<PlayerViewMotion>();

            // §01's race is played over the shoulder, so the camera that does it has to be
            // on the rig the moment the rig exists. It was written, committed, and attached
            // to nothing — the owner played a build I had called third-person and got first
            // person, because I checked the code instead of the scene. It ships off and
            // costs nothing until V is pressed.
            body.AddComponent<ThirdPersonCamera>();

            // AddComponent runs Awake immediately, so the pivot arrives through the public
            // setter rather than through the inspector field.
            look.PitchPivot = pivot;
            look.SetLook(0f, 0f);
            Adopt(body);
        }

        private void Adopt(GameObject body)
        {
            if (_motor == null)
            {
                _motor = body.GetComponent<PlayerMotor>();
            }

            if (_cameraRig == null)
            {
                _cameraRig = body.GetComponent<PlayerCameraRig>();
            }

            if (_footsteps == null)
            {
                _footsteps = body.GetComponentInChildren<PlayerFootsteps>();
            }

            if (_viewMotion == null)
            {
                _viewMotion = body.GetComponent<PlayerViewMotion>();
            }

            if (_input == null)
            {
                _input = body.GetComponent<PlayerInputRouter>();
            }

            // DELETED: _motor.Role = RoleId.Runner. The harness used to have to say which
            // of §04's five 직업 it was measuring, because only the Runner's Shift bought
            // 5.6 m/s and §14's questions 1 and 2 are written about that margin against the
            // creature's 4.8. Every runner has 질주 now, so a rig with no role IS the
            // measured one.
        }

        private void EnsurePacer()
        {
            if (_pacer != null || !_buildEnvironment)
            {
                return;
            }

            var pacer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            pacer.name = "Pacer (stand-in for §06 MonsterBrain)";

            // Monster.fbx is 2.34 m (ASSETS.md); the capsule primitive is 2 m tall, so the
            // scale makes the thing behind you the size it will actually be.
            pacer.transform.localScale = new Vector3(0.9f, MonsterHeightMetres / 2f, 0.9f);
            Paint(pacer, new Color(0.5f, 0.08f, 0.08f));

            var collider = pacer.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }

            _pacer = pacer.transform;
            ResetPacer();
        }

        // ------------------------------------------------------------------- the chase

        private void ResetPacer()
        {
            if (_pacer == null || _motor == null)
            {
                return;
            }

            // Starting at §06's release distance means the very first thing a player feels
            // is the exact gap they are trying to hold.
            var behind = _motor.transform.position - (_motor.transform.forward * GameConstants.AggroReleaseDistance);
            behind.y = MonsterHeightMetres * 0.5f;
            _pacer.position = behind;

            _chaseSeconds = 0f;
            _maxGapMetres = GameConstants.AggroReleaseDistance;
            _lineOfSightBrokenSeconds = 0f;
            _pacerGaveUp = false;

            // The pacer is a stand-in for §06 entering 추격, so it fires the same body
            // reaction the match layer fires on that edge. Without it §14's question 1
            // is asked in this harness without the moment the question is about.
            if (_viewMotion != null)
            {
                _viewMotion.Flinch();
            }
        }

        private void StepPacer(float deltaSeconds)
        {
            if (_pacer == null || _motor == null || _pacerPaused || _pacerGaveUp)
            {
                return;
            }

            var target = _motor.transform.position;
            var here = _pacer.position;
            var to = new Vector3(target.x - here.x, 0f, target.z - here.z);
            var gap = to.magnitude;

            _chaseSeconds += deltaSeconds;
            if (gap > _maxGapMetres)
            {
                _maxGapMetres = gap;
            }

            var eye = here + (Vector3.up * 1.5f);
            var seen = !Physics.Linecast(eye, target + (Vector3.up * 1.5f), out _, ~0, QueryTriggerInteraction.Ignore);
            _lineOfSightBrokenSeconds = seen ? 0f : _lineOfSightBrokenSeconds + deltaSeconds;

            // §06's release condition, measured rather than owned: the rule and its numbers
            // live in GameConstants and MonsterBrain. The pacer stops here so that opening
            // the gap has a payoff, which is the thing §14's question 1 is asking about.
            if (gap >= GameConstants.AggroReleaseDistance
                && _lineOfSightBrokenSeconds >= GameConstants.AggroReleaseLineOfSightBreak)
            {
                _pacerGaveUp = true;
                return;
            }

            // The blunt half of DangerSense, reproduced here because the harness has no
            // audio rig: a 0–1 that rises as the gap closes and carries no bearing. It
            // is the only proximity signal the camera is allowed — see PlayerViewMotion.
            if (_viewMotion != null)
            {
                _viewMotion.Dread01 = 1f - Mathf.Clamp01(gap / GameConstants.MonsterSightRange);
            }

            if (gap > 0.01f)
            {
                _pacer.position = here + (to / gap * Tier.MonsterSpeed * deltaSeconds);
                _pacer.rotation = Quaternion.LookRotation(to / gap, Vector3.up);
            }

            if (gap <= 1f)
            {
                _lastCaughtAfterSeconds = _chaseSeconds;
                ResetPacer();
            }
        }

        // ------------------------------------------------------------------ dev controls

        private void ReadHarnessKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                _showHelp = !_showHelp;
            }

            if (_cameraRig != null)
            {
                if (keyboard.leftBracketKey.wasPressedThisFrame)
                {
                    _cameraRig.FieldOfView -= 1f;
                }

                if (keyboard.rightBracketKey.wasPressedThisFrame)
                {
                    _cameraRig.FieldOfView += 1f;
                }
            }

            // DELETED with §08 and §03: the O / B / L keys. They toggled the 목표물 in
            // both hands, equipped and unequipped §08's 가방, and cycled 전리품 into the
            // inventory until it overloaded — the three levers this harness existed to
            // A/B carry weight against §05's speed table. A runner carries a torch, so
            // there is nothing left to weigh.

            if (_motor != null && keyboard.rKey.wasPressedThisFrame)
            {
                _motor.Stamina.Reset();
                ResetPacer();
            }

            if (keyboard.pKey.wasPressedThisFrame)
            {
                _pacerPaused = !_pacerPaused;
            }

            // A/B the camera without leaving play mode. §14 says questions 1 and 2
            // 「직접 만져봐야 나온다」, and the only honest way to judge whether view
            // motion helps the chase is to turn it off in the middle of one. Half is
            // on the cycle because it is the answer a nauseous tester actually wants.
            if (keyboard.vKey.wasPressedThisFrame && _viewMotion != null)
            {
                _viewMotion.Scale = _viewMotion.Scale > 0.75f ? 0.5f : (_viewMotion.Scale > 0.1f ? 0f : 1f);
            }

            if (keyboard.tabKey.wasPressedThisFrame && _input != null)
            {
                _input.LockCursor = !_input.LockCursor;
            }

            for (var i = 0; i < GameConstants.ThreatTierCount; i++)
            {
                if (keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                {
                    _tierOverride = _tierOverride == i ? -1 : i;
                }
            }
        }

        // ---------------------------------------------------------------------- readout

        private void OnGUI()
        {
            if (_motor == null)
            {
                return;
            }

            EnsureGuiResources();

            var tier = Tier;
            var monsterSpeed = tier.MonsterSpeed;
            var margin = _motor.MarginVersusMonster(monsterSpeed);
            var gap = GapMetres;
            var safeAngle = _motor.SafePeekAngleDegrees(monsterSpeed, _motor.Stamina.SprintAvailable);

            GUILayout.BeginArea(new Rect(12f, 12f, 430f, 560f), GUI.skin.box);

            Line("§05 FEEL HARNESS  —  §14 Q1 chase, Q2 peek", Color.white);
            Line(" ", Color.white);

            Line("speed        " + F(_motor.ResolvedSpeed) + " m/s"
                 + "   (base " + F(_motor.LastContext.BaseSpeed) + ")", Color.white);
            Line("direction    x" + F(_motor.DirectionalMultiplier)
                 + "   at " + F(_motor.HeadingOffsetDegrees) + " deg off forward", Color.cyan);
            // The load line is now the stance alone — 웅크리기 is the only thing that
            // multiplies a runner's speed. Kept rather than deleted because a load
            // multiplier that is silently not 1 is exactly the bug this readout catches.
            Line("load         x" + F(_motor.LastContext.LoadMultiplier), Color.white);
            Line("measured     " + F(_motor.GroundSpeed) + " m/s on " + Surface(), Color.grey);

            Line(" ", Color.white);
            Line("monster      " + F(monsterSpeed) + " m/s   tier " + tier.Index + " " + tier.Phase
                 + (_tierOverride >= 0 ? "  [held]" : string.Empty), Color.white);
            Line("margin       " + Signed(margin.MetresPerSecond) + " m/s",
                margin.IsLosingGround ? Color.red : Color.green);
            Line("gap          " + F(gap) + " m"
                 + (margin.IsLosingGround ? "   caught in " + F(margin.SecondsUntilCaught(gap)) + " s" : string.Empty),
                Color.white);
            Line("to open 12m  " + F(margin.SecondsToOpen(GameConstants.AggroReleaseDistance)) + " s", Color.white);

            Line(" ", Color.white);
            Line("SAFE PEEK    " + F(safeAngle) + " deg  <- turn further than this and it gains",
                safeAngle >= GameConstants.PeekAngleDegrees ? Color.green : Color.yellow);

            Line(" ", Color.white);
            Line("stamina      " + F(_motor.Stamina.Fraction * 100f) + "%   "
                 + F(_motor.Stamina.SecondsRemaining) + " s"
                 + (_motor.Stamina.SprintAvailable ? string.Empty : "   locked out "
                     + F(_motor.Stamina.SecondsUntilSprintAvailable) + " s"),
                _motor.Stamina.SprintAvailable ? Color.white : Color.yellow);
            Line("fov          " + F(_cameraRig != null ? _cameraRig.FieldOfView : GameConstants.FovDefault)
                 + "   (§05 clamp " + GameConstants.FovMin + "-" + GameConstants.FovMax + ")", Color.white);

            if (_viewMotion != null)
            {
                Line("view motion  " + F(_viewMotion.Scale * 100f) + "%"
                     + "   drag " + F(_viewMotion.Drag * 100f) + "%"
                     + "   breath " + F(_viewMotion.Breath * 100f) + "%"
                     + (_viewMotion.Flinching ? "   FLINCH" : string.Empty),
                    _viewMotion.Scale > 0f ? Color.white : Color.grey);
                Line("             eye " + F(_viewMotion.Translation.magnitude * 100f) + " cm off rest, "
                     + F(Quaternion.Angle(Quaternion.identity, _viewMotion.Rotation)) + " deg", Color.grey);
            }

            Line(" ", Color.white);
            Line("chase        " + F(_chaseSeconds) + " s   best gap " + F(_maxGapMetres) + " m"
                 + (_pacerGaveUp ? "   RELEASED" : string.Empty), _pacerGaveUp ? Color.green : Color.white);
            if (_lastCaughtAfterSeconds >= 0f)
            {
                Line("last caught  after " + F(_lastCaughtAfterSeconds) + " s", Color.red);
            }

            DrawMultiplierCurve(monsterSpeed);

            if (_showHelp)
            {
                Line(" ", Color.white);
                Line("WASD move · mouse look · Shift run · F light", Color.grey);
                Line("[ ] fov   O objective   B bag   L loot   R reset", Color.grey);
                Line("P pause pacer   V view motion 100/50/0   1-5 hold §07 tier", Color.grey);
                Line("Tab cursor   F1 help", Color.grey);
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// Draws §05's multiplier as the curve it is, with the player's current heading on
        /// it and the break-even against the monster marked.
        /// <para>
        /// The picture is the argument. §05 insists the table is "이산적 선택이 아니라
        /// 아날로그 조절" and a four-row table on screen would say the opposite; a line with
        /// a marker sliding along it as the mouse turns says exactly what the design means.
        /// </para>
        /// </summary>
        private void DrawMultiplierCurve(float monsterSpeed)
        {
            if (_bar == null || _motor == null)
            {
                return;
            }

            GUILayout.Space(6f);
            var rect = GUILayoutUtility.GetRect(400f, 90f);

            var context = _motor.LastContext;
            var contextMultiplier = SpeedResolver.ContextMultiplier(context);
            var baseSpeed = context.BaseSpeed > 0f ? context.BaseSpeed : GameConstants.RunnerSprintSpeed;

            var previous = GUI.color;

            for (var x = 0; x < (int)rect.width; x++)
            {
                var degrees = x / rect.width * GameConstants.BackwardAngleDegrees;
                var multiplier = SpeedResolver.MultiplierForHeading(degrees);

                // The vertical axis spans §05's own extremes — 0.65 to 1.00 — so the shape
                // of the curve is the shape of the table and nothing is off screen.
                var t = Mathf.InverseLerp(GameConstants.MulBackward, GameConstants.MulForward, multiplier);
                var height = t * rect.height;
                var beats = baseSpeed * multiplier * contextMultiplier > monsterSpeed;

                GUI.color = beats ? new Color(0.2f, 0.8f, 0.3f) : new Color(0.8f, 0.25f, 0.2f);
                GUI.DrawTexture(new Rect(rect.x + x, rect.yMax - height, 1f, height), _bar);
            }

            var headingX = _motor.HeadingOffsetDegrees / GameConstants.BackwardAngleDegrees * rect.width;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(rect.x + headingX, rect.y, 2f, rect.height), _bar);

            GUI.color = previous;
        }

        private string Surface()
        {
            if (_footsteps == null)
            {
                return "?";
            }

            return _footsteps.Surface.ToString();
        }

        private void EnsureGuiResources()
        {
            if (_bar == null)
            {
                _bar = new Texture2D(1, 1);
                _bar.SetPixel(0, 0, Color.white);
                _bar.Apply();
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label);
                _style.richText = false;
            }
        }

        private void Line(string text, Color colour)
        {
            if (_style == null)
            {
                return;
            }

            var previous = GUI.color;
            GUI.color = colour;
            GUILayout.Label(text, _style);
            GUI.color = previous;
        }

        private static string F(float value)
        {
            if (float.IsPositiveInfinity(value))
            {
                return "inf";
            }

            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Signed(float value)
        {
            return value.ToString("+0.00;-0.00", CultureInfo.InvariantCulture);
        }
    }
}
