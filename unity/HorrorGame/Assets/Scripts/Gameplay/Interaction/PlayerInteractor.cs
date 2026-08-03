#nullable enable

using HorrorGame.Core;
using HorrorGame.Gameplay.Match;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// One runner's hands and eyes.
    /// <para>
    /// <b>Look at a thing and hold a key</b> — one component, one prompt, and a single
    /// crosshair ray that every interaction in the game goes through. The mouse is the
    /// definition of forward ("마우스 방향 = 이동 방향"), so where the runner is looking
    /// is the only sensible way to say which thing they mean, and a separate proximity
    /// trigger per object would let someone shut a door they are facing away from.
    /// </para>
    /// <para>
    /// <b>It measures; it does not rule.</b> Nothing in this file decides whether an
    /// interaction succeeded — it finds what the crosshair is on, routes the key to it,
    /// and draws what the thing says about itself.
    /// </para>
    /// <para>
    /// <b>What was cut out of it, and where the line was drawn.</b> This used to be the
    /// hands of a co-operative looting game as well: it carried an <c>Inventory</c>, held
    /// a fallback focus so a 궤짝 in both hands could still be put down, flipped the
    /// oversize-carry pose flag, and pushed a per-frame <c>ClueReadContext</c> —
    /// distance, reader speed, light on the mark, camera roll — into the match so §03's
    /// read gate could run. It also swallowed the interact key whenever §08's shop was
    /// covering the screen.
    /// </para>
    /// <para>
    /// All of that is deleted. The line is <b>the crosshair and the key stay; the hands
    /// go.</b> 하강 is a footrace and a race still needs to interact with a door — 1.1 s
    /// to pull one shut, 4.5 s for the creature to break it, and a broken door stays
    /// broken for everyone behind you — so the mechanism that lets a runner say "that
    /// one, now" survives intact. What does not survive is anything that existed so the
    /// runner could <em>hold</em> something: there is nothing to pick up, nothing to
    /// drop, no pockets to put it in and no shop to sell it at.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        /// <summary>
        /// The interactor a prompt is being drawn for. Single-player only: each runner
        /// gets its own component, so anything shipped reads its own reference instead of
        /// this. It exists so an <see cref="Interactable"/> can quote the looker in its
        /// prompt without every object holding a back-reference.
        /// </summary>
        public static PlayerInteractor? Active { get; private set; }

        [Header("Wiring")]
        [SerializeField]
        [Tooltip("The match this runner is in. Left empty, the one in the scene is found.")]
        private MatchDirector? _director;

        [SerializeField]
        [Tooltip("Eye transform the crosshair ray is cast from. Left empty, the first camera in children.")]
        private Transform? _eye;

        [SerializeField]
        [Tooltip("Prompt screen. Left empty, one is created on this object.")]
        private InteractionPromptScreen? _prompt;

        private Interactable? _focus;
        private bool _holding;

        /// <summary>The match. Interactables ask it for anything that is a rule.</summary>
        public MatchDirector? Director
        {
            get { return _director; }
        }

        /// <summary>
        /// The transform the crosshair is cast from.
        /// <para>
        /// Resolved on demand rather than only in <see cref="Awake"/>, because
        /// <c>Awake</c> does not run outside play mode and the editor builds and drives
        /// whole matches: <c>SoloPlaytest.BuildScene</c> assembles this rig and the
        /// editor loop tests play a match through it with no frames. With the eye left
        /// null there, reach was measured from the feet — a rig that behaves one way in a
        /// build and another in the checks that are supposed to cover the build.
        /// </para>
        /// </summary>
        private Transform? Eye
        {
            get
            {
                if (_eye == null)
                {
                    var camera = GetComponentInChildren<Camera>();
                    _eye = camera != null ? camera.transform : transform;
                }

                return _eye;
            }
        }

        /// <summary>What the crosshair is on, or null.</summary>
        public Interactable? Focus
        {
            get { return _focus; }
        }

        /// <summary>
        /// The one prompt this interactor draws. Exposed so a regression test can assert
        /// that a targeted thing is actually announced — a working key with no prompt is
        /// indistinguishable from a missing one, which is how that went unnoticed until a
        /// human played it.
        /// </summary>
        public InteractionPromptScreen? Prompt
        {
            get { return _prompt; }
        }

        /// <summary>
        /// Points the interactor at a match. Used by the scene builder and by tests.
        /// <para>
        /// It used to take the motor, the loadout and the flashlight as well, because it
        /// needed a role for §08's 금고, pockets for 전리품 and a beam for §03's read
        /// gate. None of those exist; the match is the only thing left to bind to.
        /// </para>
        /// </summary>
        public void Bind(MatchDirector director)
        {
            _director = director;
        }

        private void Awake()
        {
            Active = this;

            if (_director == null)
            {
                _director = FindFirstObjectByType<MatchDirector>();
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

                // There is no HUD marker, so the object itself is the only place a "this
                // is what the key will act on" cue can live.
                if (found != null)
                {
                    found.OnFocusGained();
                }
            }

            HandleKey();
            DrawPrompt();
        }

        /// <summary>
        /// Casts the crosshair. Triggers count, because interactable volumes are
        /// triggers — scenery must not shove the runner around the maze's corridors.
        /// <para>
        /// The cast length used to be the longer of the read range and the hands-on
        /// range, because a mark could be read from further away than a breaker could be
        /// worked on. With the clue chain gone the door is the only interactable and
        /// <see cref="GameConstants.EngineerReachDistance"/> is its reach, so that is how
        /// far the ray needs to travel. Each candidate is still re-checked against its
        /// own <see cref="Interactable.ReachMetres"/>, so a subclass with a shorter reach
        /// is honoured without shortening the cast for everything else.
        /// </para>
        /// </summary>
        private Interactable? Probe()
        {
            var eye = Eye;
            if (eye == null)
            {
                return null;
            }

            var hits = Physics.RaycastAll(
                eye.position,
                eye.forward,
                GameConstants.EngineerReachDistance,
                ~0,
                QueryTriggerInteraction.Collide);

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
                    // Opaque geometry. You cannot reach through a wall, so anything
                    // nearer than the best candidate ends the search there.
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

            return best;
        }

        private void HandleKey()
        {
            var keyboard = Keyboard.current;
            var key = keyboard != null ? keyboard[InteractKey] : null;
            if (key == null)
            {
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

                // A held interaction can finish mid-hold — a door comes shut — and the
                // next press is what re-opens it, so the press path still runs.
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
        /// Empty when the thing takes no key at all, because a key drawn on something
        /// that ignores it teaches the runner that the key does not work, which is the
        /// belief this line exists to prevent.
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
        /// The control scheme names 마우스 · WASD · Shift · F and nothing else, so the
        /// interact key is not a documented binding. E is the convention every game in
        /// this genre uses; it is here rather than in <c>GameConstants</c> because a key
        /// is not a balance value.
        /// <para>
        /// Public because two other things have to agree with it and neither may guess:
        /// the guidance overlay prints it to playtesters, and the interaction regression
        /// test presses it. It used to be private and <c>GuidanceBindings</c> read it by
        /// reflection, which is a binding that breaks silently when the field is renamed.
        /// </para>
        /// </summary>
        public const Key InteractKey = Key.E;

        /// <summary>How the interact key is written on screen and in the guidance overlay.</summary>
        public const string InteractKeyLabel = "E";
    }
}
