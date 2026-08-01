#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Roles;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// One player's hands and eyes. §03 · §05 · §08.
    /// <para>
    /// <b>Look at a thing and hold a key</b> — one component, one prompt, and a single
    /// crosshair ray that every interaction in the game goes through. §05 makes the
    /// mouse the definition of forward ("마우스 방향 = 이동 방향"), so where the player
    /// is looking is the only sensible way to say which thing they mean, and a
    /// separate proximity trigger per object would let a player pick up a chest they
    /// are facing away from.
    /// </para>
    /// <para>
    /// <b>It measures; it does not rule.</b> Distance, speed, light quality and the
    /// view angle are read off the world here and handed to <c>MatchDirector</c>, which
    /// steps Core's <c>ClueReader</c> with them. §03's gate — close, still, lit, for
    /// <see cref="GameConstants.ClueReadSeconds"/>, and gone the moment any of those
    /// stops being true — is entirely inside that core object. Nothing in this file
    /// decides whether a read succeeded.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        /// <summary>
        /// The interactor a prompt is being drawn for. Single-player only: §13 gives one
        /// host four players and each gets its own component, so anything shipped reads
        /// its own reference instead of this. It exists so an <see cref="Interactable"/>
        /// can quote the looker's own capacity in its prompt without every prop holding
        /// a back-reference.
        /// </summary>
        public static PlayerInteractor? Active { get; private set; }

        [Header("Wiring")]
        [SerializeField]
        [Tooltip("The match this player is in. Left empty, the one in the scene is found.")]
        private MatchDirector? _director;

        [SerializeField]
        [Tooltip("Eye transform the crosshair ray is cast from. Left empty, the first camera in children.")]
        private Transform? _eye;

        [SerializeField]
        private PlayerMotor? _motor;

        [SerializeField]
        private PlayerLoadout? _loadout;

        [SerializeField]
        private PlayerFlashlight? _flashlight;

        [SerializeField]
        [Tooltip("Prompt screen. Left empty, one is created on this object.")]
        private InteractionPromptScreen? _prompt;

        private Interactable? _focus;
        private Interactable? _carriedFocus;
        private bool _holding;

        /// <summary>The match. Interactables ask it for anything that is a rule.</summary>
        public MatchDirector? Director
        {
            get { return _director; }
        }

        /// <summary>This player's §08 pockets, or null on a rig with no loadout.</summary>
        public Inventory? Pockets
        {
            get { return _loadout != null ? _loadout.Inventory : null; }
        }

        /// <summary>Which of §04's five this player is. §08's 금고 asks.</summary>
        public RoleId Role
        {
            get { return _motor != null ? _motor.Role : RoleId.None; }
        }

        /// <summary>Where a carried object is held — in front of the eye, since §03's carrier cannot see past it.</summary>
        public Transform? CarryAnchor
        {
            get { return _eye; }
        }

        /// <summary>What the crosshair is on, or null.</summary>
        public Interactable? Focus
        {
            get { return _focus; }
        }

        /// <summary>
        /// The one prompt this interactor draws. Exposed so the pick-up regression test
        /// can assert that a targeted piece is actually announced — a working key with
        /// no prompt is indistinguishable from a missing one, which is how this went
        /// unnoticed until a human played it.
        /// </summary>
        public InteractionPromptScreen? Prompt
        {
            get { return _prompt; }
        }

        /// <summary>Points the interactor at a match. Used by the scene builder and by tests.</summary>
        public void Bind(MatchDirector director, PlayerMotor? motor, PlayerLoadout? loadout, PlayerFlashlight? flashlight)
        {
            _director = director;
            if (motor != null)
            {
                _motor = motor;
            }

            if (loadout != null)
            {
                _loadout = loadout;
            }

            if (flashlight != null)
            {
                _flashlight = flashlight;
            }
        }

        /// <summary>
        /// Makes a carried object the fallback focus. Its collider is off while it is in
        /// the hands, so the crosshair cannot find it — but §03's carry has to be
        /// reversible ("다시 누르면 내려놓는다"), and this is what keeps the key bound to
        /// it while both hands are full.
        /// </summary>
        public void SetCarriedFocus(Interactable carried)
        {
            _carriedFocus = carried;
        }

        /// <summary>Drops the fallback focus once the thing is out of the hands.</summary>
        public void ClearCarriedFocus(Interactable carried)
        {
            if (ReferenceEquals(_carriedFocus, carried))
            {
                _carriedFocus = null;
            }
        }

        /// <summary>
        /// Records §08's oversize piece being taken up or let go. The weight itself is
        /// already in <c>Inventory</c> — <c>SharedLootCarry</c> put it there — so this
        /// only sets the pose flag movement reads and keeps the fallback focus in step.
        /// </summary>
        public void SetOversizeCarry(OversizeLootInteractable piece, bool carrying)
        {
            if (_loadout != null)
            {
                _loadout.SetCarryingOversizePiece(carrying);
            }

            var collider = piece.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = !carrying;
            }

            if (carrying)
            {
                SetCarriedFocus(piece);
            }
            else
            {
                ClearCarriedFocus(piece);
            }
        }

        /// <summary>Whatever is in the hands right now, so the match can make it fall on death.</summary>
        public Interactable? CarriedFocus
        {
            get { return _carriedFocus; }
        }

        private void Awake()
        {
            Active = this;

            if (_director == null)
            {
                _director = FindFirstObjectByType<MatchDirector>();
            }

            if (_motor == null)
            {
                _motor = GetComponentInChildren<PlayerMotor>();
            }

            if (_loadout == null)
            {
                _loadout = GetComponentInChildren<PlayerLoadout>();
            }

            if (_flashlight == null)
            {
                _flashlight = GetComponentInChildren<PlayerFlashlight>();
            }

            if (_eye == null)
            {
                var camera = GetComponentInChildren<Camera>();
                _eye = camera != null ? camera.transform : transform;
            }

            if (_prompt == null)
            {
                _prompt = gameObject.AddComponent<InteractionPromptScreen>();
            }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }
        }

        private void Update()
        {
            var found = Probe();
            if (!ReferenceEquals(found, _focus))
            {
                if (_focus != null)
                {
                    if (_holding)
                    {
                        _focus.OnHoldBroken();
                    }

                    _focus.OnFocusLost();
                }

                _holding = false;
                _focus = found;

                // §03 forbids a HUD marker, so the object itself is the only place a
                // "this is what the key will act on" cue can live.
                if (found != null)
                {
                    found.OnFocusGained();
                }
            }

            PushClueContext();
            HandleKey();
            DrawPrompt();
        }

        /// <summary>
        /// Casts §05's crosshair. Triggers count, because every prop in
        /// <see cref="Interactable.CreateProp"/> is one — scenery must not shove the
        /// player around §12's corridors.
        /// </summary>
        private Interactable? Probe()
        {
            var eye = _eye;
            if (eye == null)
            {
                return _carriedFocus;
            }

            var reach = Mathf.Max(GameConstants.ClueReadRange, GameConstants.EngineerReachDistance);
            var hits = Physics.RaycastAll(
                eye.position, eye.forward, reach, ~0, QueryTriggerInteraction.Collide);

            Interactable? best = null;
            var bestDistance = float.PositiveInfinity;

            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.distance >= bestDistance)
                {
                    continue;
                }

                var candidate = hit.collider.GetComponentInParent<Interactable>();
                if (candidate == null)
                {
                    // Opaque geometry. §03 does not let a mark be read through a wall, so
                    // anything nearer than the best candidate ends the search there.
                    if (!hit.collider.isTrigger && !hit.collider.transform.IsChildOf(transform))
                    {
                        bestDistance = hit.distance;
                        best = null;
                    }

                    continue;
                }

                if (hit.distance > candidate.ReachMetres)
                {
                    continue;
                }

                bestDistance = hit.distance;
                best = candidate;
            }

            return best != null ? best : _carriedFocus;
        }

        /// <summary>
        /// Hands §03's three conditions to the match, every frame, whether or not a clue
        /// is in view — a frame with no clue is what tells <c>ClueReader</c> the player
        /// looked away.
        /// </summary>
        private void PushClueContext()
        {
            var director = _director;
            if (director == null)
            {
                return;
            }

            var clue = _focus as CluePropInteractable;
            var context = default(ClueReadContext);
            context.ClueId = clue != null ? clue.ClueId : -1;

            if (clue == null)
            {
                director.SetClueContext(context);
                return;
            }

            var mark = clue.transform.position;
            var eye = _eye != null ? _eye : transform;

            context.DistanceToClue = Vector3.Distance(eye.position, mark);
            context.ReaderSpeed = _motor != null ? _motor.GroundSpeed : 0f;

            // The strongest source on the mark, which is what §03's single 0–1 quality
            // means: a beam held on it, or a zone that is already lit.
            var beam = _flashlight != null ? _flashlight.Cone.QualityAt(mark.ToVec3()) : 0f;
            context.LightQuality = director.IsAreaLit(mark) ? 1f : beam;

            // How far out of upright the reader's view is. §05 gives the camera no roll
            // control, so this is all but always zero — but it is read off the camera
            // rather than assumed, because it is the one term that can cancel §03's
            // 6↔9 condition and a hard zero would quietly delete that counter-play.
            context.ViewAngleDegrees = Vector3.SignedAngle(Vector3.up, eye.up, eye.forward);

            // Zero. §03 lists the reading obstacles it wants — the narrow beam, the worn
            // patch, the hurry — and distance is not among them; the worn patch is the
            // clue's own, and it is applied inside ClueInscription. Adding a
            // distance-blur term here would be inventing a §03 obstacle.
            context.Blur = 0f;

            director.SetClueContext(context);
        }

        private void HandleKey()
        {
            var keyboard = Keyboard.current;
            var key = keyboard != null ? keyboard[InteractKey] : null;
            if (key == null)
            {
                return;
            }

            // §08's shop covers the screen and takes the mouse, so the same key that
            // opened it puts it away — and it is handled here, before anything in the
            // crosshair, so that dismissing a panel can never also press whatever the
            // player happened to be facing when it went up.
            var director = _director;
            if (director != null && director.ShopOpen)
            {
                _holding = false;
                if (key.wasPressedThisFrame)
                {
                    director.CloseShopScreen();
                }

                return;
            }

            var focus = _focus;
            if (focus == null || !focus.AcceptsKey)
            {
                _holding = false;
                return;
            }

            if (focus.NeedsHold)
            {
                if (key.isPressed)
                {
                    _holding = true;
                    focus.OnHeld(this, Time.deltaTime);
                }
                else if (_holding)
                {
                    _holding = false;
                    focus.OnHoldBroken();
                }

                // A held interaction can finish mid-hold — §04's safe pops open — and the
                // next press is what empties it, so the press path still runs.
            }

            if (key.wasPressedThisFrame)
            {
                focus.OnPressed(this);
            }
        }

        private void DrawPrompt()
        {
            var prompt = _prompt;
            if (prompt == null)
            {
                return;
            }

            var focus = _focus;
            if (focus == null)
            {
                prompt.Clear();
                return;
            }

            prompt.Show(
                focus.Title,
                focus.Detail,
                ActionLine(focus),
                focus.Refusal,
                focus.NeedsHold ? focus.HoldProgress01 : -1f);
        }

        /// <summary>
        /// The key line for whatever is in the crosshair: the binding, then the verb.
        /// <para>
        /// Empty when the thing takes no key at all — §03's marks are read by standing
        /// still with a beam on them and have no press — because a key drawn on
        /// something that ignores it teaches the player that the key does not work,
        /// which is the belief this whole line exists to prevent.
        /// </para>
        /// </summary>
        private static string ActionLine(Interactable focus)
        {
            if (!focus.AcceptsKey)
            {
                return string.Empty;
            }

            var verb = focus.Action;
            return string.IsNullOrEmpty(verb)
                ? "[" + InteractKeyLabel + "]"
                : "[" + InteractKeyLabel + "]  " + verb;
        }

        /// <summary>
        /// §05's scheme names 마우스 · WASD · Shift · F and nothing else, so the interact
        /// key is not a documented binding. E is the convention every game in this genre
        /// uses; it is here rather than in <c>GameConstants</c> because a key is not a
        /// balance value.
        /// <para>
        /// Public because two other things have to agree with it and neither may guess:
        /// §14's guidance overlay prints it to playtesters, and the pick-up regression
        /// test presses it. It used to be private and <c>GuidanceBindings</c> read it by
        /// reflection, which is a binding that breaks silently when the field is renamed.
        /// </para>
        /// </summary>
        public const Key InteractKey = Key.E;

        /// <summary>How the interact key is written on screen and in §14's guidance.</summary>
        public const string InteractKeyLabel = "E";
    }
}
