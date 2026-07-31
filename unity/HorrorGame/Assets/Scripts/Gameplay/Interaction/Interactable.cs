#nullable enable

using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// Something the player can look at and act on. §03 · §08.
    /// <para>
    /// <b>It decides nothing.</b> Every subclass here is a handle on a rule that
    /// already exists in Core — <c>Inventory.TryAdd</c>, <c>SharedLootCarry.TryPickUp</c>,
    /// <c>LootSafe.Tick</c>, <c>MatchState.TryTakeObjective</c>. The component measures
    /// the world, calls the rule, and turns the rule's answer into a line of text.
    /// A refusal that the rule produced is <em>shown</em> rather than swallowed, which
    /// is the whole reason <see cref="Refusal"/> exists: §08 makes a 궤짝 need two
    /// people and §03 forbids loot in the hands that hold the objective, and a player
    /// who presses a key and sees nothing happen learns neither rule.
    /// </para>
    /// <para>
    /// Reach is a §-number, not a preference. §04 makes the Engineer's work hands-on at
    /// the site (<see cref="GameConstants.EngineerReachDistance"/>) and §03 gives
    /// reading its own, longer range (<see cref="GameConstants.ClueReadRange"/>), so
    /// each subclass answers with the one its section quotes.
    /// </para>
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        /// <summary>What the thing is, in one phrase. Drawn large.</summary>
        public abstract string Title { get; }

        /// <summary>
        /// What acting on it costs or requires, from the §-section that says so. §10's
        /// principle applied to a prompt: the trade has to be legible at the moment it
        /// is being made, not discovered afterwards.
        /// </summary>
        public abstract string Detail { get; }

        /// <summary>
        /// How close the player must stand. Defaults to §04's hands-on distance, which
        /// is the shortest reach any section quotes.
        /// </summary>
        public virtual float ReachMetres
        {
            get { return GameConstants.EngineerReachDistance; }
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

        /// <summary>The player stopped looking at this. Clears the refusal.</summary>
        public virtual void OnFocusLost()
        {
            Refusal = string.Empty;
        }

        /// <summary>
        /// Builds a prop the player can see and the crosshair can hit.
        /// <para>
        /// The collider is a trigger: a clue lying on a desk must not push the player
        /// around, and §12's corridor widths are computed for the character and the
        /// monster, not for scenery. The interaction raycast asks for triggers
        /// explicitly.
        /// </para>
        /// <para>
        /// The sizes callers pass are rig geometry — how big a paper or a chest looks —
        /// and not tuned game values, in the same sense as the player rig's 1.75 m.
        /// Nothing in §03 or §08 depends on them.
        /// </para>
        /// </summary>
        protected static GameObject CreateProp(
            string name, PrimitiveType shape, Vector3 position, Vector3 size, Color colour)
        {
            var prop = GameObject.CreatePrimitive(shape);
            prop.name = name;
            prop.transform.position = position;
            prop.transform.localScale = size;

            var collider = prop.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }

            var renderer = prop.GetComponent<Renderer>();
            if (renderer != null)
            {
                // A self-lit material, because §03 puts every one of these in the dark
                // and an unlit prop in an unlit corridor is invisible even in the beam.
                var material = renderer.sharedMaterial != null
                    ? new Material(renderer.sharedMaterial)
                    : new Material(Shader.Find("Standard"));
                material.color = colour;
                if (material.HasProperty("_EmissionColor"))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", colour * PropEmission);
                }

                renderer.sharedMaterial = material;
            }

            return prop;
        }

        /// <summary>
        /// Removes a prop from the world.
        /// <para>
        /// <c>Object.Destroy</c> is deferred to the end of a frame and refuses to run
        /// outside play mode, so a headless verification that drives a whole match from
        /// the editor would leave every "picked up" piece of loot standing there and
        /// log an error for each one. Both paths do the same thing to the world; only
        /// the timing differs.
        /// </para>
        /// </summary>
        public static void Despawn(GameObject? target)
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

        /// <summary>
        /// How strongly a prop glows. Presentation only: it exists so a player can find
        /// a paper in §03's dark without the HUD marking it, which §03's own "no map, no
        /// marker" stance rules out. Not a balance value — the beam still has to be held
        /// on the mark for <see cref="GameConstants.ClueReadSeconds"/> to read it.
        /// </summary>
        private const float PropEmission = 0.35f;
    }
}
