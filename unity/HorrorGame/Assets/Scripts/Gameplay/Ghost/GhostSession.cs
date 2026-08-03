#nullable enable

using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Core;
using HorrorGame.Core.Ghost;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using HorrorGame.Gameplay.Race;
using HorrorGame.UI.Screens;
using HorrorGame.UI.Shell;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Gameplay.Ghost
{
    /// <summary>
    /// The race seen from an eliminated runner's seat. §09 — 탈락 처리 — 유령.
    /// <para>
    /// <b>What this class is for.</b> §02 says 「잡힌다 → 탈락. 순위 없음」 and then says
    /// the match does not stop; §09 says the eliminated runner watches the rest of it.
    /// This is that seat: it takes the body's camera away from the living rig, gives it
    /// to the ghost, and lets the ghost choose where in the building to watch from.
    /// </para>
    /// <para>
    /// <b>삭제됨 — 신호(흔들기).</b> §09's table gave the ghost one outbound verb: shake a
    /// nearby object, 45 s cooldown. That was a co-operative consolation — a dead
    /// teammate who could still warn three living ones kept a four-player team whole —
    /// and it does not survive the race. Four reasons, in the design's own words:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// §11 already forbids it in one sentence — 「살아 있는 사람에게 개입할 수 없다 —
    /// 경주에서 죽은 사람이 산 사람을 도우면 그건 팀이다」. §09's own note argued the
    /// rattle was harmless because 「유령에게는 그럴 이유가 딱히 없다」, which is a claim
    /// about <em>motive</em>. A design can only control capability. The field is a Steam
    /// lobby (<see cref="RaceParty"/>) — twenty people who queued together — so the
    /// moment two of them are friends, the motive is there.
    /// </description></item>
    /// <item><description>
    /// It writes to the one channel the race reads. §12 makes 「소리 → 바닥 재질이
    /// 지도다」 and the pivot keeps 발소리 · 폐색 precisely so that 「남의 위치를 소리로
    /// 안다」. In this game position <em>is</em> sound. A placed noise from a ghost is a
    /// forged footstep, and it is placed by the one entity in the match with 맵 전체
    /// 시야. §09 bans speech because 「죽은 사람이 정보를 주면 경주가 망가진다」; a
    /// coordinate is information whether it arrives as a word or as a bang.
    /// </description></item>
    /// <item><description>
    /// The 45 s was priced for at most three ghosts. §11's design target is 11~20
    /// runners, and a race thins: ten ghosts at minute eight is one free noise somewhere
    /// in the building every 4.5 s, and they will cluster where the race is being
    /// decided — §11's 마지막 관문 하나. There is no cooldown that makes nineteen
    /// transmitters quiet, because the problem scales with the field and the cooldown
    /// does not.
    /// </description></item>
    /// <item><description>
    /// It reverses 탈락. §02: 「탈락에는 순위가 없다. 이것이 완주와 탈락을 가르는
    /// 전부다」. The point of unranked is that being caught takes you out of the result;
    /// a verb that lets the eliminated nudge somebody else's place puts them back into
    /// everyone's result but their own. <c>MatchDirector.EnterGhost</c> reached the same
    /// verdict and wrote down that the rattle was this file's to delete.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>What replaced it, and why the seat is not simply deleted.</b> 「탈락하면
    /// 지루하다」 is worse in a race than it ever was in co-op: the first runner caught
    /// can be caught two minutes into a twenty-minute match, and §02 keeps that match
    /// running without them. But free flight alone is not 「할 게 있다」 in an unlit
    /// eight-storey tower with no map — a spectator who cannot find anything is a
    /// spectator watching corridors. So the one key is now a <em>cut</em>: it moves the
    /// camera between the places the race is decided — every creature in the building
    /// (§14's Q1, 「추격이 재밌는가?」, which has no other instrument in a build) and B8's
    /// 도착점 (§02's only question) — and the ghost's own body, which is the one address
    /// in the tower that is theirs. Nothing it does reaches a living runner. It moves no
    /// object and plays no sound: after §09 takes the seat, this player is silent in the
    /// world for the rest of the match, by construction rather than by a check.
    /// </para>
    /// <para>
    /// <b>Solo is a special case of the twenty-player rule, not a mode.</b> Everything
    /// here is written per seat: it binds the local player's own <c>GhostState</c>,
    /// forgets that seat as a target for §06, and asks the host whether §02 has finished.
    /// While anybody is still running §02 has not finished, so the ghost simply watches.
    /// With nobody left it has, and the ghost is offered the end screen instead of being
    /// thrown at it. No branch in this file asks how many people are playing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class GhostSession : MonoBehaviour
    {
        /// <summary>
        /// §09's one verb, on the key the player already learned. Reusing
        /// <c>PlayerInteractor.InteractKey</c> rather than inventing a binding: the ghost
        /// has exactly one thing it can do and it is the same gesture — press the key,
        /// something happens to what you are looking at — that opened every 문 it ever
        /// went through. It no longer touches the world; it moves the eye.
        /// </summary>
        public const Key WatchKey = PlayerInteractor.InteractKey;

        /// <summary>Rise. §09's ghost leaves a storey by going up through it rather than by finding a 투하구.</summary>
        public const Key AscendKey = Key.Space;

        /// <summary>Sink.</summary>
        public const Key DescendKey = Key.LeftCtrl;

        /// <summary>Fly at §04's 질주 instead of a run. §05's own Shift, so the hand does not have to relearn it.</summary>
        public const Key BoostKey = Key.LeftShift;

        /// <summary>
        /// Ask for §02's verdict once it has been reached. Held rather than pressed, and
        /// deliberately nowhere near <see cref="WatchKey"/>: <c>MatchDirector</c>'s own
        /// note on the end of a match says binding a terminal decision to a single press
        /// "would let one mistimed keystroke end four people's match."
        /// </summary>
        public const Key EndMatchKey = Key.Enter;

        /// <summary>
        /// How long <see cref="EndMatchKey"/> must be held, seconds.
        /// <para>
        /// <b>What the hold is for.</b> It is the only exit from §09 — 복귀: 불가능, and
        /// the match does not restart — so the press has to be a decision rather than a
        /// keystroke. Nothing else about it is a feel question: the ghost is not moving,
        /// nothing is chasing it, and the standings on the other side of the key are
        /// already final.
        /// </para>
        /// <para>
        /// <b>What decides the number.</b> <c>GameConstants.DoorShutSeconds</c> — 1.1 s,
        /// §12-B. That is the interval this game has already decided is long enough to
        /// mean it: the beat a runner stands still for to shut a 관문 they can never
        /// re-open. Committing to something irreversible while standing still is the same
        /// act here, and a second answer to it would be a second feel.
        /// </para>
        /// <para>
        /// It used to read <c>GameConstants.ClueReadSeconds</c>. §03's clue chain is gone,
        /// and a race feature quoting a deleted system's constant is how the constant
        /// survives the system.
        /// </para>
        /// </summary>
        public static float EndMatchHoldSeconds
        {
            get { return GameConstants.DoorShutSeconds; }
        }

        /// <summary>
        /// §09's whole control scheme in one line, for a player who has never been
        /// eliminated before. Built from the key constants above rather than typed out,
        /// so a rebind cannot leave the overlay lying.
        /// </summary>
        public static string KeyLegend
        {
            get
            {
                return "WASD 이동 · " + AscendKey + "/" + DescendKey + " 상하 · "
                       + BoostKey + " 가속 · " + WatchKey + " 시점";
            }
        }

        /// <summary>
        /// How far a watched subject is framed from, metres.
        /// <para>
        /// <c>GameConstants.MonsterSightRange</c> — 20 m, §06's own reach. The frame is
        /// therefore exactly the danger: a runner who appears at the edge of the shot is
        /// a runner entering the radius inside which §06 can see them. A spectator
        /// distance chosen for looks would have had to be tuned against the map; this one
        /// is read off the only circle the design draws around a creature.
        /// </para>
        /// </summary>
        private const float WatchStandOffMetres = GameConstants.MonsterSightRange;

        /// <summary>
        /// Height the shot sits at above the subject's feet, metres.
        /// <para>
        /// The standing eye height every rig and capture tool in this project uses. The
        /// chase is watched from the height of the person being chased — an overhead
        /// vantage would show a maze solved, which is the one thing §12 spends its whole
        /// rule set making impossible to see.
        /// </para>
        /// </summary>
        private const float WatchEyeHeightMetres = 1.63f;

        /// <summary>
        /// Not watching anything — free flight. §09's default and the state a ghost
        /// returns to by pressing a movement key.
        /// </summary>
        private const int FreeFlight = -1;

        private readonly List<Vantage> _vantages = new List<Vantage>();

        private GhostState? _ghost;
        private GhostFreeCamera? _fly;
        private GhostOverlay? _overlay;
        private GhostViewGrade? _grade;
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

        private int _vantage = FreeFlight;
        private float _endHeld;
        private bool _verdictReached;

        /// <summary>Whether the local player is eliminated and flying. §09.</summary>
        public bool IsActive
        {
            get { return _ghost != null; }
        }

        /// <summary>The eliminated player's core state, or null while they are still running.</summary>
        public GhostState? Ghost
        {
            get { return _ghost; }
        }

        /// <summary>
        /// What the camera is locked onto, or <see cref="string.Empty"/> in free flight.
        /// The overlay and the capture rig both read it; nothing acts on it.
        /// </summary>
        public string WatchLabel
        {
            get
            {
                return _vantage >= 0 && _vantage < _vantages.Count ? _vantages[_vantage].Label : string.Empty;
            }
        }

        /// <summary>Whether a vantage is holding the camera rather than the player flying it.</summary>
        public bool IsWatching
        {
            get { return _vantage != FreeFlight; }
        }

        /// <summary>
        /// Whether §02 has reached a verdict that is waiting on this ghost. False while
        /// anybody is still in play, which needs no branch.
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
        /// <param name="bodyRoot">The eliminated runner's rig — the ghost's own address in the building.</param>
        /// <param name="monster">The creature on the storey they were caught on, for the watch readout. Optional.</param>
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
                    "[Ghost] §09 needs a camera to fly and the player rig has none, so 탈락 "
                    + "would have been a black screen. The race was left running.", this);
                return false;
            }

            _ghost = ghost;
            _verdictReached = false;
            _endHeld = 0f;
            _vantage = FreeFlight;
            _vantages.Clear();

            SuppressTheLiving(true);

            // Where the eye already is, so §09's best seconds — the ones right after it
            // watched the thing that caught it — are not thrown away by a camera snap.
            var from = eye.transform.position;
            var facing = eye.transform.rotation;

            _eyeParent = eye.transform.parent;
            _eyeLocalPosition = eye.transform.localPosition;
            _eyeLocalRotation = eye.transform.localRotation;
            eye.transform.SetParent(null, worldPositionStays: true);

            Fly.Bind(ghost, eye, from, facing);
            Overlay.Bind(ghost);
            Overlay.SetKeys(WatchKey.ToString(), EndMatchKey.ToString(), KeyLegend);
            _grade = GhostViewGrade.Raise(transform);

            Debug.Log(
                "[Ghost] §09 탈락 — 몸은 " + ghost.DeathPosition.ToVector3().ToString("F1", CultureInfo.InvariantCulture)
                + "에 남았다. 순위 없음, 말할 수 없고 나갈 수 없다. ["
                + WatchKey + "] 시점 전환 · 경주는 계속된다.", this);

            return true;
        }

        /// <summary>
        /// Gives the camera back and stops drawing. Called when the match ends or a new
        /// one is laid out; §09 has no other exit — 복귀: 불가능.
        /// </summary>
        public void End()
        {
            if (_ghost == null)
            {
                return;
            }

            _ghost = null;
            _verdictReached = false;
            _endHeld = 0f;
            _vantage = FreeFlight;
            _vantages.Clear();

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
        /// Cuts to the next vantage, wrapping through free flight. §09's one verb.
        /// <para>
        /// Public so a test or a capture rig can press it without a keyboard, and because
        /// the shape of it is the balance argument: it takes no argument, returns no
        /// handle on anything in the world, and the only thing it writes is this
        /// component's own camera. There is no overload that touches a 물건.
        /// </para>
        /// <para>
        /// The list is rebuilt on every press rather than cached. A creature can be
        /// destroyed, a storey's creature can be spawned late, and §02's 도착점 is bound
        /// after the map is laid out — a spectator holding a stale list would cut to a
        /// hole in the building.
        /// </para>
        /// </summary>
        /// <returns>False when there is no ghost, in which case nothing moved.</returns>
        public bool CutToNextVantage()
        {
            var ghost = _ghost;
            if (ghost == null)
            {
                return false;
            }

            CollectVantages();

            // Free flight is the wrap point rather than an entry in the list, so the key
            // always returns the camera to the player rather than trapping it in a
            // carousel of shots.
            _vantage = _vantage + 1 >= _vantages.Count ? FreeFlight : _vantage + 1;

            if (_vantage == FreeFlight)
            {
                Debug.Log("[Ghost] §09 시점 — 자유 비행.", this);
                return true;
            }

            HoldTheShot();

            Debug.Log(
                "[Ghost] §09 시점 — " + _vantages[_vantage].Label
                + ". 경주에는 아무것도 하지 않는다.", this);

            return true;
        }

        /// <summary>
        /// Re-reads the world and redraws §09's overlay. Everything <see cref="Update"/>
        /// pushes into it — §06's state, §02's offer — and then the overlay's own draw.
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
            DrawTheWatch();
            _overlay?.Redraw();
        }

        /// <summary>
        /// Asks for §02's already-decided verdict. Refused while §02 is still counting,
        /// which is what makes this safe in a twenty-player race: nineteen living runners
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

            if (MatchPause.IsPaused)
            {
                // A paused match must not be flown through. MatchPause stops the host's
                // FixedUpdate, so §06 is frozen and a shot taken here would be a
                // photograph rather than a watch.
                return;
            }

            var delta = Time.unscaledDeltaTime;

            DriveTheCamera(delta);
            ReadTheKeys(delta);
            DrawTheWatch();

            // The overlay's own LateUpdate does the draw; this method only fills it.
        }

        // ------------------------------------------------------------------
        // Flying, and the shots that hold the camera instead.
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

            if (_vantage != FreeFlight)
            {
                if (move.sqrMagnitude > 0f)
                {
                    // Touching the stick releases the shot, from exactly where the shot
                    // was. The alternative — a second key to let go — is a control a
                    // spectator has to be taught; this one teaches itself the first time
                    // somebody presses W.
                    _vantage = FreeFlight;
                }
                else
                {
                    HoldTheShot();
                    return;
                }
            }

            var look = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            var boost = keyboard != null && keyboard[BoostKey].isPressed;

            Fly.Drive(move, look, boost, deltaSeconds);
        }

        private static float Axis(Keyboard keyboard, Key positive, Key negative)
        {
            return (keyboard[positive].isPressed ? 1f : 0f) - (keyboard[negative].isPressed ? 1f : 0f);
        }

        /// <summary>
        /// Puts the camera on the current vantage for this frame.
        /// <para>
        /// Written through <c>GhostFreeCamera.Bind</c> rather than onto the transform,
        /// because the free camera keeps its own yaw and pitch: a shot that moved the
        /// transform behind its back would snap the view the instant the player pressed W
        /// to take over. Bind is the documented way to hand it an explicit eye pose, and
        /// a cut is exactly that.
        /// </para>
        /// </summary>
        private void HoldTheShot()
        {
            var ghost = _ghost;
            var eye = _eye;
            if (ghost == null || eye == null || _vantage < 0 || _vantage >= _vantages.Count)
            {
                return;
            }

            var vantage = _vantages[_vantage];
            var subject = vantage.Resolve();
            if (!subject.HasValue)
            {
                // The creature was destroyed, or the storey it was on was torn down. Fall
                // back to flying rather than to a shot of nothing.
                _vantage = FreeFlight;
                return;
            }

            var target = subject.Value + (Vector3.up * WatchEyeHeightMetres);
            var approach = vantage.Approach(target, eye.transform.position);
            var from = target + (approach * WatchStandOffMetres);

            Fly.Bind(ghost, eye, from, Quaternion.LookRotation(target - from, Vector3.up));
        }

        /// <summary>
        /// Rebuilds the list of places worth watching from. The order is the argument:
        /// the creatures first because §14 says 「추격이 재밌는가?」 is the question that
        /// decides this project, then §02's 도착점 because it is the only place the race
        /// can actually end, then the ghost's own body last because it is the only one of
        /// the three that is about the player rather than about the race.
        /// </summary>
        private void CollectVantages()
        {
            _vantages.Clear();

            var creatures = FindObjectsByType<MonsterAgent>(FindObjectsSortMode.InstanceID);
            for (var i = 0; i < creatures.Length; i++)
            {
                var creature = creatures[i];
                if (creature == null)
                {
                    continue;
                }

                _vantages.Add(Vantage.Following(
                    creature.transform,
                    creatures.Length > 1 ? "괴물 " + (i + 1) : "괴물"));
            }

            var race = Race;
            if (race != null && race.FinishFound)
            {
                _vantages.Add(Vantage.Fixed(race.Finish, "B8 도착점"));
            }

            var ghost = _ghost;
            if (ghost != null)
            {
                _vantages.Add(Vantage.Fixed(ghost.DeathPosition.ToVector3(), "내 시신"));
            }
        }

        // ------------------------------------------------------------------
        // §09's one request.
        // ------------------------------------------------------------------

        private void ReadTheKeys(float deltaSeconds)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard[WatchKey].wasPressedThisFrame)
            {
                CutToNextVantage();
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

        // ------------------------------------------------------------------
        // What a ghost is given to look at.
        // ------------------------------------------------------------------

        /// <summary>
        /// Pushes §06's state onto the overlay, as words rather than as objects.
        /// <para>
        /// <b>Why the eliminated get the creature's state at all.</b> Two reasons, and the
        /// second is why it is here rather than in an editor gizmo. §09 answers 탈락하면
        /// 지루하다 with 볼 게 있고 할 게 있다, and a creature you can follow through eight
        /// storeys is the thing there is to watch. And it is free: the ghost cannot say a
        /// word about what it sees and cannot touch a thing it sees, which is the same
        /// argument §09 uses to give it the whole map. <c>MonsterDebugView</c> draws the
        /// same numbers with <c>Gizmos</c>, which exist only in the editor and only when
        /// the creature is selected — so the one question §14 says decides the project,
        /// 「추격이 재밌는가?」, could not be watched in a build at all.
        /// </para>
        /// <para>
        /// The creature described is the one being watched, falling back to the one that
        /// caught this player. A readout naming a different creature from the one in
        /// frame is worse than no readout.
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

            overlay.SetVerdictWait(_verdictReached, EndMatchProgress01);

            // What the one key is currently holding. Empty while flying free, which is
            // the state the ghost returns to on any movement key.
            overlay.SetWatchSubject(WatchLabel);

            var monster = WatchedCreature();
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

        /// <summary>The creature in frame, or the one that caught this player when the camera is elsewhere.</summary>
        private MonsterAgent? WatchedCreature()
        {
            if (_vantage >= 0 && _vantage < _vantages.Count)
            {
                var followed = _vantages[_vantage].Follow;
                if (followed != null)
                {
                    var agent = followed.GetComponentInParent<MonsterAgent>();
                    if (agent != null)
                    {
                        return agent;
                    }
                }
            }

            return _monster;
        }

        /// <summary>§06's five states in the document's own words. Not in <c>UiStrings</c> because no living screen shows them — a runner sees a silhouette, not a label.</summary>
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
                // thing an eliminated player did wrong.
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
                // §12-B's doors belong to the living and so does the crosshair. Left
                // running it would keep casting from a camera that is now somewhere else
                // in the building and offering to shut a 관문 through a wall — which is
                // the single most direct way an eliminated runner could still decide
                // somebody else's place.
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

        /// <summary>
        /// §02's rules, for the one thing a spectator wants from them: where the 도착점
        /// is. Found rather than injected, the same way the audio rig used to be — the
        /// ghost is a passenger in a race that was already running when it arrived, and a
        /// constructor argument would make <c>MatchDirector</c> responsible for wiring a
        /// camera position.
        /// </summary>
        private RaceDirector? Race
        {
            get { return FindFirstObjectByType<RaceDirector>(); }
        }

        private void OnDestroy()
        {
            End();
        }

        /// <summary>
        /// One place the ghost can watch from: either a thing that moves, or a coordinate
        /// that does not.
        /// <para>
        /// Deliberately a pair of cases and not an interface. A vantage carries no verb
        /// and no payload — it is a position and a word — which is the whole of what §09
        /// is allowed to be after the rattle came out.
        /// </para>
        /// </summary>
        private readonly struct Vantage
        {
            private readonly Vector3 _point;
            private readonly bool _tracks;

            private Vantage(Transform? follow, Vector3 point, bool tracks, string label)
            {
                Follow = follow;
                _point = point;
                _tracks = tracks;
                Label = label;
            }

            /// <summary>The thing being followed, or null for a fixed coordinate.</summary>
            public Transform? Follow { get; }

            /// <summary>What to call it. Korean, because §09's overlay is.</summary>
            public string Label { get; }

            /// <summary>A vantage that tracks something as it moves. §06's creatures.</summary>
            public static Vantage Following(Transform body, string label)
            {
                return new Vantage(body, body.position, tracks: true, label);
            }

            /// <summary>A vantage on a place. §02's 도착점, and the ghost's own body.</summary>
            public static Vantage Fixed(Vector3 point, string label)
            {
                return new Vantage(null, point, tracks: false, label);
            }

            /// <summary>
            /// Where to look, or null when the followed thing has gone.
            /// <para>
            /// The two cases have to be told apart rather than collapsed: a destroyed
            /// <c>Transform</c> compares equal to null, so a tracking vantage that fell
            /// back to its stored point would keep framing the spot a creature spawned at
            /// half an hour ago and never say anything was wrong.
            /// </para>
            /// </summary>
            public Vector3? Resolve()
            {
                if (_tracks)
                {
                    return Follow != null ? (Vector3?)Follow.position : null;
                }

                return _point;
            }

            /// <summary>
            /// The unit direction the camera stands off in.
            /// <para>
            /// Behind a followed body, so the shot shows what it is walking into — a
            /// creature framed from the front shows a face and hides the corridor it is
            /// hunting down. For a fixed place, along the line the ghost is already on, so
            /// the cut reads as flying there rather than as being teleported to an
            /// arbitrary side of it.
            /// </para>
            /// </summary>
            public Vector3 Approach(Vector3 target, Vector3 cameraNow)
            {
                var follow = Follow;
                if (follow != null)
                {
                    var back = -follow.forward;
                    back.y = 0f;
                    if (back.sqrMagnitude > 0.0001f)
                    {
                        return back.normalized;
                    }
                }

                var away = cameraNow - target;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f)
                {
                    return away.normalized;
                }

                // Standing exactly on it. South, which the compass readout calls 북 from
                // the subject's side — a defined answer rather than a zero vector.
                return Vector3.back;
            }
        }
    }
}
