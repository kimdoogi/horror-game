#nullable enable

using System.Globalization;
using HorrorGame.Audio;
using HorrorGame.Core;
using HorrorGame.Core.Ghost;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Audio;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using HorrorGame.UI;
using HorrorGame.UI.Screens;
using HorrorGame.UI.Shell;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Gameplay.Ghost
{
    /// <summary>
    /// The match seen from a dead player's seat. §09 — 사망 처리 — 유령.
    /// <para>
    /// <b>What this class is for.</b> §09's rules were already written — <c>GhostState</c>
    /// holds them, <c>GhostFreeCamera</c> flies, <c>GhostOverlay</c> draws — and nothing
    /// connected them, so a solo death went straight to §02's 패배 screen and the section
    /// did not exist in the game. This is the connection: it takes the body's camera away
    /// from the living rig, gives it to the ghost, and drives §09's one verb.
    /// </para>
    /// <para>
    /// <b>Four restrictions, and each is held by construction rather than by a check.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>말하기: 불가능.</b> There is no outbound path here of any kind — no channel
    /// opened, none closed, no key that would have transmitted. Core's <c>GhostState</c>
    /// has no method that accepts a message and <c>PlayerState.MayTransmitVoice</c> is
    /// already false for the dead; this class adds nothing that could be un-gated. §13
    /// settles the alternative for anyone who would rather gate it at the receiver:
    /// cutting a channel at the receiver is not cutting it.
    /// </description></item>
    /// <item><description>
    /// <b>탈출: 불가능.</b> Nothing here calls <c>MatchState.TryExtract</c>, and it would
    /// be refused if it did — <c>PlayerState.Extract</c> takes only a player standing on
    /// the surface, and a ghost stands in <c>PlayerStanding.Dead</c> for the rest of the
    /// match. <see cref="TryEndTheMatch"/> shows §02's verdict; it does not change it, and
    /// it does nothing at all while anybody is still alive.
    /// </description></item>
    /// <item><description>
    /// <b>신호: 쿨타임 45초.</b> The cooldown is Core's and is ticked by
    /// <c>MatchState.Tick</c> off the host's fixed step. This class never writes it: it
    /// calls <c>GhostState.TryRattle</c> and does what it is told, so a rattle cannot be
    /// bought by a long frame or a dropped one.
    /// </description></item>
    /// <item><description>
    /// <b>시야: 맵 전체 (벽 통과).</b> The camera is unparented from the rig and driven
    /// directly, with no collider and no cast. See <see cref="GhostViewGrade"/> for why
    /// the picture is lifted with exposure rather than with a light.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Solo is a special case of the four-player rule, not a mode.</b> Everything here
    /// is written per seat: it binds the local player's own <c>GhostState</c>, forgets
    /// that seat as a target for §06, and asks the host whether §02 has finished. With
    /// three teammates alive §02 has not finished, so the ghost simply watches — which is
    /// the section as designed. With nobody left it has, and the ghost is offered the end
    /// screen instead of being thrown at it. No branch in this file asks how many people
    /// are playing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class GhostSession : MonoBehaviour
    {
        /// <summary>
        /// §09's one verb, on the key the player already learned. Reusing
        /// <c>PlayerInteractor.InteractKey</c> rather than inventing a binding: the ghost
        /// has exactly one thing it can do to the world and it is the same gesture —
        /// look at a thing, press the key — that picked up every 전리품 it ever carried.
        /// </summary>
        public const Key RattleKey = PlayerInteractor.InteractKey;

        /// <summary>Rise. §09's ghost leaves a storey by going up through it rather than by finding a stairwell.</summary>
        public const Key AscendKey = Key.Space;

        /// <summary>Sink.</summary>
        public const Key DescendKey = Key.LeftCtrl;

        /// <summary>Fly at §04's 질주 instead of a run. §05's own Shift, so the hand does not have to relearn it.</summary>
        public const Key BoostKey = Key.LeftShift;

        /// <summary>
        /// Ask for §02's verdict once it has been reached. Held rather than pressed, and
        /// deliberately nowhere near <see cref="RattleKey"/>: <c>MatchDirector</c>'s own
        /// note on 탈출 says binding a terminal decision to a single press "would let one
        /// mistimed keystroke end four people's match."
        /// </summary>
        public const Key EndMatchKey = Key.Enter;

        /// <summary>
        /// How long <see cref="EndMatchKey"/> must be held, seconds.
        /// <para>
        /// <c>GameConstants.ClueReadSeconds</c> rather than a number of this file's own:
        /// §03 already decided how long this game makes a player hold something to prove
        /// they meant it, and a second answer to that question would be a second feel.
        /// </para>
        /// </summary>
        public static float EndMatchHoldSeconds
        {
            get { return GameConstants.ClueReadSeconds; }
        }

        /// <summary>
        /// §09's whole control scheme in one line, for a player who has never been dead
        /// before. Built from the key constants above rather than typed out, so a rebind
        /// cannot leave the overlay lying.
        /// </summary>
        public static string KeyLegend
        {
            get
            {
                return "WASD 이동 · " + AscendKey + "/" + DescendKey + " 상하 · "
                       + BoostKey + " 가속 · " + RattleKey + " 신호";
            }
        }

        /// <summary>
        /// How far a rattled object visibly moves, metres.
        /// <para>
        /// §09 says 아주 약하게 — the point of the scene is that the living are not sure
        /// («바람이겠지»), so a shove would be the wrong signal as surely as no movement
        /// at all. A centimetre for a third of a second is a drawing amplitude, not a
        /// tuned value; it is what <c>Interactable.FloorClearanceMetres</c> is to a
        /// dropped piece.
        /// </para>
        /// </summary>
        private const float RattleShakeMetres = 0.01f;

        /// <summary>Seconds the shake lasts. See <see cref="RattleShakeMetres"/>.</summary>
        private const float RattleShakeSeconds = 0.35f;

        /// <summary>Colliders one rattle search may consider. A ceiling on the work, not on the rule.</summary>
        private const int OverlapBudget = 64;

        private readonly Collider[] _overlap = new Collider[OverlapBudget];

        private GhostState? _ghost;
        private GhostFreeCamera? _fly;
        private GhostOverlay? _overlay;
        private GhostViewGrade? _grade;
        private MatchAudioRig? _audio;
        private MonsterAgent? _monster;

        private Camera? _eye;
        private Transform? _eyeParent;
        private Vector3 _eyeLocalPosition;
        private Quaternion _eyeLocalRotation = Quaternion.identity;
        private Transform? _bodyRoot;

        private PlayerInputRouter? _input;
        private PlayerLook? _look;
        private PlayerMotor? _motor;
        private PlayerCameraRig? _cameraRig;
        private PlayerViewMotion? _viewMotion;
        private PlayerInteractor? _interactor;
        private bool _restoreInputSuppressed;
        private bool _restoreCursorLocked;
        private bool _restoreLookLocked;
        private bool _restoreMovementLocked;
        private bool _restoreCameraRig;
        private bool _restoreViewMotion;
        private bool _restoreInteractor;

        private GhostRattleTarget _target;
        private Transform? _shaking;
        private Vector3 _shakeOrigin;
        private float _shakeRemaining;
        private float _endHeld;
        private bool _verdictReached;
        private bool _wasReadyToRattle = true;

        /// <summary>Whether the local player is dead and flying. §09.</summary>
        public bool IsActive
        {
            get { return _ghost != null; }
        }

        /// <summary>The dead player's core state, or null while they are alive.</summary>
        public GhostState? Ghost
        {
            get { return _ghost; }
        }

        /// <summary>
        /// The 물건 the ghost is beside as of the last frame, or
        /// <see cref="GhostRattleTarget.None"/>. Call <see cref="LookForSomethingToShake"/>
        /// if the ghost has just been moved by something other than a frame.
        /// </summary>
        public GhostRattleTarget Target
        {
            get { return _target; }
        }

        /// <summary>
        /// Whether §02 has reached a verdict that is waiting on this ghost. False while
        /// anybody is still in play, which is the four-player case and needs no branch.
        /// </summary>
        public bool VerdictIsWaiting
        {
            get { return _ghost != null && _verdictReached; }
        }

        /// <summary>How much of <see cref="EndMatchHoldSeconds"/> has been held, 0 to 1.</summary>
        public float EndMatchProgress01
        {
            get { return Mathf.Clamp01(_endHeld / Mathf.Max(EndMatchHoldSeconds, 0.0001f)); }
        }

        /// <summary>Raised when the ghost has asked for §02's verdict. The host shows the end screen.</summary>
        public System.Action? MatchEndRequested { get; set; }

        /// <summary>
        /// Takes the body's camera away from the living rig and gives it to §09.
        /// <para>
        /// The camera is <em>unparented</em> rather than merely driven, because
        /// <c>PlayerCameraRig</c> writes the eye's pivot every <c>LateUpdate</c> and
        /// <c>PlayerViewMotion</c> writes the eye's local pose after it. A ghost sharing a
        /// transform with two components that still believe they own it would drift back
        /// to the corpse a frame at a time. Detaching is also what keeps the
        /// <c>AudioListener</c> travelling with the view — <c>GameAudio.ListenerTransform</c>
        /// already anticipates exactly this, in its own words: "the camera is not always
        /// parented to the body (§09's ghost sees the whole map)."
        /// </para>
        /// </summary>
        /// <param name="ghost">The core state minted by <c>MatchState.TryKill</c>.</param>
        /// <param name="bodyRoot">The dead player's rig, so a ghost cannot rattle its own corpse.</param>
        /// <param name="monster">§06's antagonist, for the watch readout. Optional.</param>
        /// <returns>False when there is no camera to fly, in which case nothing was changed.</returns>
        public bool Begin(GhostState ghost, Transform? bodyRoot, MonsterAgent? monster)
        {
            if (ghost == null || _ghost != null)
            {
                return false;
            }

            _bodyRoot = bodyRoot;
            _monster = monster;
            ResolveRig(bodyRoot);

            var eye = _eye;
            if (eye == null)
            {
                Debug.LogError(
                    "[Ghost] §09 needs a camera to fly and the player rig has none, so the death "
                    + "would have been a black screen. The match was left alive.", this);
                return false;
            }

            _ghost = ghost;
            _verdictReached = false;
            _endHeld = 0f;
            _wasReadyToRattle = ghost.CanRattle;

            SuppressTheLiving(true);

            // Where the eye already is, so §09's best seconds — the ones right after it
            // watched the thing that killed it — are not thrown away by a respawn snap.
            var from = eye.transform.position;
            var facing = eye.transform.rotation;

            _eyeParent = eye.transform.parent;
            _eyeLocalPosition = eye.transform.localPosition;
            _eyeLocalRotation = eye.transform.localRotation;
            eye.transform.SetParent(null, worldPositionStays: true);

            Fly.Bind(ghost, eye, from, facing);
            Overlay.Bind(ghost);
            Overlay.SetKeys(RattleKey.ToString(), EndMatchKey.ToString(), KeyLegend);
            _grade = GhostViewGrade.Raise(transform);

            Debug.Log(
                "[Ghost] §09 유령 — 몸은 " + ghost.DeathPosition.ToVector3().ToString("F1", CultureInfo.InvariantCulture)
                + "에 남았다. 말할 수 없고 나갈 수 없다. ["
                + RattleKey + "] 신호 · 쿨타임 "
                + GameConstants.GhostRattleCooldownSeconds.ToString("0", CultureInfo.InvariantCulture) + "초.", this);

            return true;
        }

        /// <summary>
        /// Gives the camera back and stops drawing. Called when the match ends or a new
        /// one is laid out; §09 has no other exit — 탈출: 불가능.
        /// </summary>
        public void End()
        {
            if (_ghost == null)
            {
                return;
            }

            _ghost = null;
            _target = GhostRattleTarget.None;
            _verdictReached = false;
            _endHeld = 0f;
            EndShake();

            _fly?.Unbind();
            _overlay?.Unbind();
            _grade?.Lower();

            var eye = _eye;
            if (eye != null && _eyeParent != null)
            {
                // Back to the exact local pose it was taken from, not to identity: the
                // rig hangs the eye off a pitch pivot with an authored offset, and
                // zeroing it would move the living player's head.
                eye.transform.SetParent(_eyeParent, worldPositionStays: false);
                eye.transform.localPosition = _eyeLocalPosition;
                eye.transform.localRotation = _eyeLocalRotation;
            }

            _eyeParent = null;
            SuppressTheLiving(false);
        }

        /// <summary>
        /// Tells the ghost that §02 has finished counting. The verdict is <em>not</em>
        /// applied here — the host already computed it and this class cannot change it —
        /// only offered, so the player leaves when they are ready instead of being thrown
        /// at a screen mid-flight.
        /// </summary>
        public void NoteVerdictReached(bool reached)
        {
            if (!reached)
            {
                _verdictReached = false;
                _endHeld = 0f;
                return;
            }

            _verdictReached = true;
        }

        /// <summary>
        /// Searches now and returns what it found. <see cref="Update"/> calls this every
        /// frame; a test or a capture rig that has just flown the ghost somewhere has to
        /// call it itself, because neither of those runs frames.
        /// </summary>
        public GhostRattleTarget LookForSomethingToShake()
        {
            RefreshTarget();
            return _target;
        }

        /// <summary>
        /// Re-reads the world and redraws §09's overlay. Everything <see cref="Update"/>
        /// pushes into it — the 물건 under the verb, §06's state, §02's offer — and then
        /// the overlay's own draw.
        /// <para>
        /// Public for the same reason <c>GhostOverlay.Redraw</c> is: outside play mode
        /// neither <c>Update</c> nor <c>LateUpdate</c> fires, so a capture rig that did
        /// not call this would photograph a HUD that had never been told anything. The
        /// first frames taken of §09 were exactly that — a corridor with the title on it
        /// and every line that matters blank.
        /// </para>
        /// </summary>
        public void DrawOverlay()
        {
            RefreshTarget();
            DrawTheWatch();
            _overlay?.Redraw();
        }

        /// <summary>
        /// Shakes whatever the ghost is beside. §09's 신호 row, and the only thing that
        /// leaves a ghost.
        /// <para>
        /// Public so a test can press the verb without a keyboard, and because the shape
        /// of it is the balance argument: it takes no argument and returns a
        /// <c>GhostRattle</c>, which carries a place and nothing else. There is no
        /// overload that takes a payload.
        /// </para>
        /// </summary>
        public bool TryRattle(out GhostRattle rattle)
        {
            var ghost = _ghost;
            if (ghost == null)
            {
                rattle = new GhostRattle(false, Vector3.zero.ToVec3(), 0f, 0f, GhostSignalFailure.OutOfRange);
                return false;
            }

            RefreshTarget();

            var target = _target;
            if (!target.Found)
            {
                // Nothing to shake is 너무 멀다 from the player's side: §09 splits the two
                // failures because only this one is worth drifting somewhere to fix.
                rattle = new GhostRattle(
                    false, ghost.Position, float.PositiveInfinity, ghost.RattleCooldownRemaining,
                    ghost.CanRattle ? GhostSignalFailure.OutOfRange : GhostSignalFailure.OnCooldown);
                _overlay?.NoteRattle(rattle);
                return false;
            }

            var landed = ghost.TryRattle(target.Position.ToVec3(), out rattle);
            _overlay?.NoteRattle(rattle);

            if (!landed)
            {
                return false;
            }

            // The living end of §09. Positional, on the world bus, at the object rather
            // than at the ghost — the whole scene is somebody in another room turning
            // round and asking 「방금 뭔가 흔들렸어?」, and a sound that arrived at the
            // ghost would not put them in the right room.
            Audio?.PlayAt(AudioCueId.GhostRattle, target.Position);
            BeginShake(target);

            Debug.Log(
                "[Ghost] §09 신호 — " + target.Label + " at "
                + target.Position.ToString("F1", CultureInfo.InvariantCulture)
                + ", 다음 신호까지 " + GameConstants.GhostRattleCooldownSeconds.ToString("0", CultureInfo.InvariantCulture)
                + "초. 말할 수는 없다.", this);

            return true;
        }

        /// <summary>
        /// Asks for §02's already-decided verdict. Refused while §02 is still counting,
        /// which is what makes this safe in a four-player match: three living teammates
        /// mean there is no verdict to ask for.
        /// </summary>
        /// <returns>False when nothing was waiting.</returns>
        public bool TryEndTheMatch()
        {
            if (_ghost == null || !_verdictReached)
            {
                return false;
            }

            var request = MatchEndRequested;
            if (request == null)
            {
                return false;
            }

            request();
            return true;
        }

        private void Update()
        {
            if (_ghost == null)
            {
                return;
            }

            var delta = Time.unscaledDeltaTime;
            AdvanceShake(delta);

            if (MatchPause.IsPaused)
            {
                // A paused match must not be flown through. MatchPause stops the host's
                // FixedUpdate, so the 45 s is not running either and a rattle taken here
                // would be free.
                return;
            }

            DriveTheCamera(delta);
            RefreshTarget();
            ReadTheKeys(delta);
            DrawTheWatch();

            // The overlay's own LateUpdate does the draw; this method only fills it.
        }

        // ------------------------------------------------------------------
        // Flying.
        // ------------------------------------------------------------------

        private void DriveTheCamera(float deltaSeconds)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            var move = Vector3.zero;
            if (keyboard != null)
            {
                move = new Vector3(
                    Axis(keyboard, Key.D, Key.A) + Axis(keyboard, Key.RightArrow, Key.LeftArrow),
                    Axis(keyboard, AscendKey, DescendKey),
                    Axis(keyboard, Key.W, Key.S) + Axis(keyboard, Key.UpArrow, Key.DownArrow));
            }

            var look = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            var boost = keyboard != null && keyboard[BoostKey].isPressed;

            Fly.Drive(move, look, boost, deltaSeconds);
        }

        private static float Axis(Keyboard keyboard, Key positive, Key negative)
        {
            return (keyboard[positive].isPressed ? 1f : 0f) - (keyboard[negative].isPressed ? 1f : 0f);
        }

        // ------------------------------------------------------------------
        // §09's one verb, and the one request.
        // ------------------------------------------------------------------

        private void ReadTheKeys(float deltaSeconds)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            var ghost = _ghost;
            if (ghost != null)
            {
                // The moment the 45 s runs out, said out loud. §09 makes the wait the
                // experience; a wait whose end the player has to watch a bar for is a
                // wait spent looking at a bar instead of at the building.
                var ready = ghost.CanRattle;
                if (ready && !_wasReadyToRattle)
                {
                    Audio?.Play(AudioCueId.GhostRattleReady);
                }

                _wasReadyToRattle = ready;
            }

            if (keyboard[RattleKey].wasPressedThisFrame)
            {
                TryRattle(out _);
            }

            if (!_verdictReached)
            {
                _endHeld = 0f;
                return;
            }

            if (keyboard[EndMatchKey].isPressed || keyboard[Key.NumpadEnter].isPressed)
            {
                _endHeld += deltaSeconds;
                if (_endHeld >= EndMatchHoldSeconds)
                {
                    _endHeld = 0f;
                    TryEndTheMatch();
                }
            }
            else
            {
                _endHeld = 0f;
            }
        }

        private void RefreshTarget()
        {
            var ghost = _ghost;
            if (ghost == null)
            {
                _target = GhostRattleTarget.None;
                return;
            }

            _target = GhostRattleTarget.Find(
                ghost.Position.ToVector3(),
                _bodyRoot,
                _monster != null ? _monster.transform : null,
                _overlap);
        }

        private void BeginShake(GhostRattleTarget target)
        {
            EndShake();

            if (!target.Movable || target.Body == null)
            {
                return;
            }

            _shaking = target.Body;
            _shakeOrigin = target.Body.position;
            _shakeRemaining = RattleShakeSeconds;
        }

        private void AdvanceShake(float deltaSeconds)
        {
            var shaking = _shaking;
            if (shaking == null)
            {
                return;
            }

            _shakeRemaining -= deltaSeconds;
            if (_shakeRemaining <= 0f)
            {
                EndShake();
                return;
            }

            // A decaying wobble across the object's own axes. §09's 아주 약하게: the
            // living have to be able to miss it, which is the whole of 「바람이겠지」.
            var fade = Mathf.Clamp01(_shakeRemaining / RattleShakeSeconds);
            var phase = (RattleShakeSeconds - _shakeRemaining) * 60f;
            var offset = new Vector3(Mathf.Sin(phase), Mathf.Sin(phase * 0.7f) * 0.35f, Mathf.Cos(phase * 1.3f))
                         * (RattleShakeMetres * fade);

            shaking.position = _shakeOrigin + offset;
        }

        private void EndShake()
        {
            var shaking = _shaking;
            if (shaking != null)
            {
                shaking.position = _shakeOrigin;
            }

            _shaking = null;
            _shakeRemaining = 0f;
        }

        // ------------------------------------------------------------------
        // What a ghost is given to look at.
        // ------------------------------------------------------------------

        /// <summary>
        /// Pushes §06's state onto the overlay, as words rather than as objects.
        /// <para>
        /// <b>Why the dead get the monster's state at all.</b> Two reasons, and the second
        /// is why it is here rather than in an editor gizmo. §09 answers 죽으면 지루하다
        /// with 볼 게 있고 할 게 있다, and a monster you can follow through five storeys is
        /// the thing there is to watch. And it is free: the ghost cannot say a word about
        /// what it sees, which is the same argument §09 uses to give it the whole map.
        /// <c>MonsterDebugView</c> draws the same numbers with <c>Gizmos</c>, which exist
        /// only in the editor and only when the monster is selected — so the one question
        /// §14 says decides the project, 「추격이 재밌는가?」, could not be watched in a
        /// build at all.
        /// </para>
        /// <para>
        /// Formatted here and handed over as strings so the UI assembly never learns what
        /// a <c>MonsterAgent</c> is — it has no reference to the monster's assembly, and
        /// giving it one to draw a label would be the wrong direction of dependency.
        /// </para>
        /// </summary>
        private void DrawTheWatch()
        {
            var overlay = _overlay;
            var ghost = _ghost;
            if (overlay == null || ghost == null)
            {
                return;
            }

            overlay.SetRattleTarget(_target.Found ? _target.Label : string.Empty, _target.Distance);
            overlay.SetVerdictWait(_verdictReached, EndMatchProgress01);

            var monster = _monster;
            if (monster == null)
            {
                overlay.SetMonsterWatch(false, string.Empty, string.Empty, string.Empty);
                return;
            }

            var here = ghost.Position.ToVector3();
            var there = monster.transform.position;
            var separation = there - here;

            var tier = monster.Tier;
            var state = StateName(monster.State) + (monster.IsAudible ? string.Empty : " · 침묵");

            var where = separation.magnitude.ToString("0", CultureInfo.InvariantCulture) + "m "
                        + Compass(separation)
                        + (Mathf.Abs(separation.y) >= 1f
                            ? separation.y > 0f ? " 위" : " 아래"
                            : string.Empty);

            // The §07 phase name is gone with UiStrings.Phase: the five 초저녁/밤/심야/…
            // labels were §07's clock, which only ever read on the surface, and there is no
            // surface. The creature's speed is the half a spectator can act on — it is what
            // the tier actually does to the runners still in the race.
            var clock = tier.MonsterSpeed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s";

            overlay.SetMonsterWatch(true, state, where, clock);
        }

        /// <summary>§06's five states in the document's own words. Not in <c>UiStrings</c> because no living screen shows them — §04's 관측자 sees a cone, not a label.</summary>
        private static string StateName(MonsterStateId state)
        {
            switch (state)
            {
                case MonsterStateId.Patrol: return "순찰";
                case MonsterStateId.Alert: return "경계";
                case MonsterStateId.Chase: return "추격";
                case MonsterStateId.Search: return "수색";
                case MonsterStateId.Standstill: return "정지";
                default: return "?";
            }
        }

        /// <summary>Eight-point bearing on the horizontal plane. A ghost has no map, so it needs a heading to fly on.</summary>
        private static string Compass(Vector3 delta)
        {
            var flat = new Vector2(delta.x, delta.z);
            if (flat.sqrMagnitude < 0.0001f)
            {
                return "여기";
            }

            var degrees = Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg;
            var index = Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) / 45f) % 8;

            switch (index)
            {
                case 0: return "북";
                case 1: return "북동";
                case 2: return "동";
                case 3: return "남동";
                case 4: return "남";
                case 5: return "남서";
                case 6: return "서";
                default: return "북서";
            }
        }

        // ------------------------------------------------------------------
        // The living rig, put down and picked back up.
        // ------------------------------------------------------------------

        /// <summary>
        /// Stops the corpse playing the game, using each component's own public switch.
        /// <para>
        /// Switches rather than <c>enabled</c> wherever one exists — <c>MatchPause</c>
        /// makes the same choice for the same reason: "nothing here reaches into the
        /// player layer's private state." The two that have no switch,
        /// <c>PlayerCameraRig</c> and <c>PlayerViewMotion</c>, both write the eye every
        /// <c>LateUpdate</c> and are the two the ghost would otherwise fight for it, so
        /// their previous enabled state is remembered and restored rather than assumed.
        /// </para>
        /// </summary>
        private void SuppressTheLiving(bool suppressed)
        {
            if (suppressed)
            {
                _restoreInputSuppressed = _input != null && _input.InputSuppressed;
                _restoreCursorLocked = _input == null || _input.LockCursor;
                _restoreLookLocked = _look != null && _look.LookLocked;
                _restoreMovementLocked = _motor != null && _motor.MovementLocked;
                _restoreCameraRig = _cameraRig != null && _cameraRig.enabled;
                _restoreViewMotion = _viewMotion != null && _viewMotion.enabled;
                _restoreInteractor = _interactor != null && _interactor.enabled;
            }

            if (_input != null)
            {
                _input.InputSuppressed = suppressed || _restoreInputSuppressed;

                // The mouse stays captured while the ghost has it: §09's ghost flies with
                // it, and a cursor that reappeared over the world would be the first
                // thing a dead player did wrong.
                _input.LockCursor = suppressed || _restoreCursorLocked;
            }

            if (_look != null)
            {
                _look.LookLocked = suppressed || _restoreLookLocked;
            }

            if (_motor != null)
            {
                _motor.MovementLocked = suppressed || _restoreMovementLocked;
            }

            if (_cameraRig != null)
            {
                _cameraRig.enabled = suppressed ? false : _restoreCameraRig;
            }

            if (_viewMotion != null)
            {
                _viewMotion.enabled = suppressed ? false : _restoreViewMotion;
            }

            if (_interactor != null)
            {
                // §08's hands are empty and §03's crosshair belongs to the living. Left
                // running it would keep casting from a camera that is now somewhere else
                // in the building and offering to pick things up through walls.
                _interactor.enabled = suppressed ? false : _restoreInteractor;
            }
        }

        private void ResolveRig(Transform? bodyRoot)
        {
            var root = bodyRoot;
            if (root == null)
            {
                var motor = FindFirstObjectByType<PlayerMotor>();
                root = motor != null ? motor.transform : null;
            }

            if (root == null)
            {
                return;
            }

            _eye = root.GetComponentInChildren<Camera>();
            _input = root.GetComponentInChildren<PlayerInputRouter>();
            _look = root.GetComponentInChildren<PlayerLook>();
            _motor = root.GetComponent<PlayerMotor>();
            _cameraRig = root.GetComponentInChildren<PlayerCameraRig>();
            _viewMotion = root.GetComponentInChildren<PlayerViewMotion>();
            _interactor = root.GetComponentInChildren<PlayerInteractor>();
        }

        private GhostFreeCamera Fly
        {
            get
            {
                if (_fly == null)
                {
                    _fly = GetComponent<GhostFreeCamera>();
                    if (_fly == null)
                    {
                        _fly = gameObject.AddComponent<GhostFreeCamera>();
                    }
                }

                return _fly;
            }
        }

        private GhostOverlay Overlay
        {
            get
            {
                if (_overlay == null)
                {
                    _overlay = GetComponentInChildren<GhostOverlay>();
                    if (_overlay == null)
                    {
                        var child = new GameObject("GhostOverlay");
                        child.transform.SetParent(transform, worldPositionStays: false);
                        _overlay = child.AddComponent<GhostOverlay>();
                    }
                }

                return _overlay;
            }
        }

        private MatchAudioRig? Audio
        {
            get
            {
                if (_audio == null)
                {
                    _audio = FindFirstObjectByType<MatchAudioRig>();
                }

                return _audio;
            }
        }

        private void OnDestroy()
        {
            End();
        }
    }
}
