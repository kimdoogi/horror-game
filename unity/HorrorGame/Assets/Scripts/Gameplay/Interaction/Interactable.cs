#nullable enable

using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// Something the runner can look at and act on.
    /// <para>
    /// <b>It decides nothing.</b> A subclass is a handle on a rule that already exists in
    /// Core — <see cref="DoorInteractable"/> is a handle on <c>DoorState</c>. The
    /// component measures the world, calls the rule, and turns the rule's answer into a
    /// line of text. A refusal the rule produced is <em>shown</em> rather than swallowed,
    /// which is why <see cref="Refusal"/> exists: a runner who presses a key and sees
    /// nothing happen learns nothing.
    /// </para>
    /// <para>
    /// <b>There is exactly one subclass left, and that is the point of this round.</b>
    /// There used to be six more — the 전리품 piece, the 궤짝, the 금고, the 단서, the
    /// 목표물 and the 지상 차량 that opened the shop. All six were the co-operative
    /// looting game; 하강 is 선착순 미로탈출, and the owner's complaint was exactly this
    /// list («단서를 찾고 불을 밝히고 이러고있어»). What survives is the door, because a
    /// race genuinely needs one: pulling a door shut behind you costs time you are racing
    /// with, the creature has to break it, and a broken door stays broken for everyone
    /// behind you. That is a decision about the race. Picking up a 1.28 m chest for
    /// credits that no longer exist was not.
    /// </para>
    /// <para>
    /// Reach is a design number, not a preference:
    /// <see cref="GameConstants.InteractReachMetres"/> is arm's length at the thing
    /// being worked on, and it is the default because it is the shortest reach anything
    /// quotes.
    /// </para>
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        private InteractableHighlight? _highlight;

        /// <summary>What the thing is, in one phrase. Drawn large.</summary>
        public abstract string Title { get; }

        /// <summary>
        /// What acting on it costs or requires, from the §-section that says so. §10's
        /// principle applied to a prompt: the trade has to be legible at the moment it
        /// is being made, not discovered afterwards.
        /// </summary>
        public abstract string Detail { get; }

        /// <summary>
        /// The verb the key performs, with no key name in it — <see cref="PlayerInteractor"/>
        /// prefixes the binding. Empty means the key does nothing here, and the prompt
        /// then says why instead of showing a key the player can press to no effect.
        /// </summary>
        public virtual string Action
        {
            get { return string.Empty; }
        }

        /// <summary>
        /// How close the player must stand. Defaults to §04's hands-on distance, which
        /// is the shortest reach any section quotes.
        /// </summary>
        public virtual float ReachMetres
        {
            get { return GameConstants.InteractReachMetres; }
        }

        /// <summary>Whether the interact key does anything at all right now.</summary>
        public virtual bool AcceptsKey
        {
            get { return true; }
        }

        /// <summary>Whether the key has to be held down rather than tapped (§04's timed work).</summary>
        public virtual bool NeedsHold
        {
            get { return false; }
        }

        /// <summary>Progress of a held interaction, 0–1. Drawn as a bar.</summary>
        public virtual float HoldProgress01
        {
            get { return 0f; }
        }

        /// <summary>
        /// Whether the prop should light up while the crosshair is on it.
        /// <para>
        /// Overridable because it used to be false for anything already in the runner's
        /// hands — a carried piece was held in front of the eye and a glow at that range
        /// is a lamp rather than a cue. Nothing is carried any more, so nothing overrides
        /// it today.
        /// </para>
        /// </summary>
        protected virtual bool GlowsWhenTargeted
        {
            get { return true; }
        }

        /// <summary>
        /// Why the last attempt did nothing, or empty. Cleared when the player looks
        /// away, so a refusal is attached to the attempt that caused it.
        /// </summary>
        public string Refusal { get; protected set; } = string.Empty;

        /// <summary>The interact key went down while this was in the crosshair.</summary>
        public virtual void OnPressed(PlayerInteractor by)
        {
        }

        /// <summary>The interact key is being held. Only called when <see cref="NeedsHold"/>.</summary>
        public virtual void OnHeld(PlayerInteractor by, float deltaSeconds)
        {
        }

        /// <summary>The key came up, or the player looked away mid-hold.</summary>
        public virtual void OnHoldBroken()
        {
        }

        /// <summary>
        /// The crosshair landed on this. Lights the prop.
        /// <para>
        /// §03 rules out a HUD marker ("맵 없음 · 마커 없음"), so the only honest way to
        /// tell a player that the thing in front of them is the thing the key will act
        /// on is to change the thing itself. A ring on a stone floor is 2 cm across and
        /// otherwise indistinguishable from grit.
        /// </para>
        /// </summary>
        public virtual void OnFocusGained()
        {
            SetGlow(GlowsWhenTargeted);
        }

        /// <summary>The player stopped looking at this. Clears the refusal and the glow.</summary>
        public virtual void OnFocusLost()
        {
            Refusal = string.Empty;
            SetGlow(false);
        }

        /// <summary>Turns the targeting glow on or off, finding the component on first use.</summary>
        protected void SetGlow(bool on)
        {
            if (_highlight == null)
            {
                _highlight = GetComponent<InteractableHighlight>();
            }

            if (_highlight != null)
            {
                _highlight.SetTargeted(on);
            }
        }

        // ── The prop-construction half of this class was deleted ──────────────
        //
        // CreateProp / SwapModel / Settle / FitTrigger / LocalBounds, plus the two
        // public constants MinimumTargetMetres (0.30 m) and FloorClearanceMetres
        // (0.02 m), all lived here. They existed to stand §08's loose 전리품 up in a
        // corridor and make it findable with a crosshair: a box trigger grown upward
        // from the model's base and floored at a hand's width, because §08's smallest
        // piece was a 2.2 cm 반지 that no honest mesh collider could be aimed at, and a
        // downward settle onto the 자갈 because a 1.7 cm piece placed on the nominal
        // floor plane sank into the relief and vanished.
        //
        // Every one of the props it built — 전리품, 궤짝, 금고, 단서, 목표물, 지상 차량 —
        // is deleted. The door, the one interactable left, is not built here: the scene
        // generator lays the leaf, the blocker and the NavMeshObstacle down and
        // MatchDirector.AttachDoors adds the component on top of them at match start.
        // So this is not dead-by-accident, it is dead-by-design, and it goes.
        //
        // The hard-won lesson inside it is worth keeping even though the code is not:
        // never call GameObject.CreatePrimitive for anything that ships. URP answers
        // RenderPipelineAsset.defaultMaterial in the editor and returns null in a
        // player, Unity falls back to a Standard-shader material that is not in a URP
        // build's shader set, and the result is a build that renders correctly for
        // every reviewer and as error magenta for the owner. For the same reason,
        // nothing may resolve a shader by name at runtime: a shader no material asset
        // references is stripped from the build and Shader.Find returns null silently.

        /// <summary>
        /// Removes a prop, or any other object, from the world.
        /// <para>
        /// <c>Object.Destroy</c> is deferred to the end of a frame and refuses to run
        /// outside play mode, so a headless verification that drives a whole match from
        /// the editor would leave everything it tore down standing there and log an error
        /// for each one. Both paths do the same thing to the world; only the timing
        /// differs.
        /// </para>
        /// <para>
        /// Kept although every prop this class used to build is deleted:
        /// <c>MatchDirector</c> tears the world root and the dead bodies down through it
        /// on the race path, and that is a live caller.
        /// </para>
        /// </summary>
        public static void Despawn(Object? target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
