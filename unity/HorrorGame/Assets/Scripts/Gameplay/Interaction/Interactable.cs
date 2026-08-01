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
        /// Whether the prop should light up while the crosshair is on it. False for
        /// anything already in the player's hands: §03 holds a carried piece in front
        /// of the eye, and a glow at that range is a lamp rather than a cue.
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

        /// <summary>
        /// Builds a prop the player can see and the crosshair can hit, from the model
        /// <c>tools/blender/gen_props.py</c> generated for it.
        /// <para>
        /// <b>It used to be a primitive, and that shipped a magenta build.</b>
        /// <c>GameObject.CreatePrimitive</c> takes its material from
        /// <c>RenderPipelineAsset.defaultMaterial</c>, and URP only answers that in the
        /// editor — in a player it returns null and Unity falls back to the built-in
        /// <c>Default-Material</c>, whose <c>Standard</c> shader is not in a URP build's
        /// shader set. Every interactable therefore rendered correctly in the editor and
        /// as Unity's error magenta in the build the owner played. Nothing here may
        /// resolve a shader by name at runtime for the same reason: a shader no material
        /// asset references is stripped from the build, and <c>Shader.Find</c> then
        /// returns null with no error.
        /// </para>
        /// <para>
        /// <b>The collider is a box, not the model's mesh.</b> Three reasons, in order
        /// of weight. §08's smallest piece is a 2.2 cm ring and its mesh is a hopeless
        /// crosshair target, so the box is floored at
        /// <see cref="MinimumTargetMetres"/> — the aim tolerance is deliberate, and it
        /// is what makes a pocket watch on a stone floor pickable at all. A trigger
        /// mesh collider must be convex, and two of the props the importer imports
        /// concave on purpose (the 차량 and the open 금고) could not be triggers at all.
        /// And a box is a fraction of the query cost of a 1,500-triangle hull on a ray
        /// that is cast every frame by every player.
        /// </para>
        /// <para>
        /// The collider is a trigger: a clue lying on a desk must not push the player
        /// around, and §12's corridor widths are computed for the character and the
        /// monster, not for scenery. The interaction raycast asks for triggers
        /// explicitly.
        /// </para>
        /// </summary>
        /// <param name="name">Scene-hierarchy name, so a generated match is readable.</param>
        /// <param name="modelKey">A file name in <c>Assets/Models/Props</c>, without the extension.</param>
        /// <param name="position">Where the prop's foot goes. Every prop's pivot is on its floor plane.</param>
        /// <param name="rotation">
        /// How it is turned. Identity for anything big enough to reach a wall — the
        /// 6.7 m 차량 and the 금고 are placed against §12's geometry and a yaw would push
        /// them through it. Loose 전리품 passes a yaw so a corridor of it is not a parade.
        /// </param>
        protected static GameObject CreateProp(
            string name, string modelKey, Vector3 position, Quaternion rotation)
        {
            // A plain container that carries the placement, with the model hung under
            // it untouched. The model's own transform is not free real estate: Unity's
            // FBX import parks the Blender Z-up → Y-up conversion on the root as a −90°
            // X rotation and the unit conversion as a ×100 scale, and writing a position
            // and rotation onto it lays every prop on its back at a hundredth of its
            // size. Measured the hard way — a 6.7 m 차량 came out 7 cm long and lying
            // down. It also gives SwapModel something stable to swap under.
            var prop = new GameObject(name);
            prop.transform.SetPositionAndRotation(Settle(position), rotation);

            var model = InteractablePropLibrary.Instantiate(modelKey);
            model.name = ModelChildName;
            model.transform.SetParent(prop.transform, worldPositionStays: false);

            FitTrigger(prop);
            prop.AddComponent<InteractableHighlight>();
            return prop;
        }

        /// <summary>Builds a prop facing down the world's Z axis. The common case.</summary>
        protected static GameObject CreateProp(string name, string modelKey, Vector3 position)
        {
            return CreateProp(name, modelKey, position, Quaternion.identity);
        }

        /// <summary>
        /// Swaps a prop's model in place, keeping its transform, collider and glow —
        /// §08's 금고 becomes the open one when it pops.
        /// </summary>
        protected static void SwapModel(GameObject prop, string modelKey)
        {
            for (var i = prop.transform.childCount - 1; i >= 0; i--)
            {
                Despawn(prop.transform.GetChild(i).gameObject);
            }

            var replacement = InteractablePropLibrary.Instantiate(modelKey);
            replacement.name = ModelChildName;
            replacement.transform.SetParent(prop.transform, worldPositionStays: false);

            FitTrigger(prop);

            var highlight = prop.GetComponent<InteractableHighlight>();
            if (highlight != null)
            {
                highlight.Rebind();
            }
        }

        /// <summary>
        /// Drops a prop onto the surface actually under it, with a hair of clearance.
        /// <para>
        /// §08's thinnest pieces are 1.7 cm — the 회중시계 and the 반지 — and §12's floors
        /// are not flat: the gravel surface carries real relief and a piece placed on the
        /// nominal floor plane sinks into it and disappears. Measured, not assumed: a set
        /// of silver spoons photographed under the beam at 2 m showed nothing but gravel.
        /// </para>
        /// <para>
        /// Conservative on purpose. A hit further than
        /// <see cref="SettleSearchMetres"/> from where the layout asked for the piece is
        /// ignored, so a prop over a stairwell or inside a wall stays where §12's
        /// generator put it rather than being teleported a storey down.
        /// </para>
        /// </summary>
        private static Vector3 Settle(Vector3 position)
        {
            var from = position + (Vector3.up * SettleSearchMetres);
            if (!Physics.Raycast(from, Vector3.down, out var hit, SettleSearchMetres * 2f, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return position;
            }

            if (Mathf.Abs(hit.point.y - position.y) > SettleSearchMetres)
            {
                return position;
            }

            return new Vector3(position.x, hit.point.y + FloorClearanceMetres, position.z);
        }

        /// <summary>Sizes the single box trigger to whatever the prop's renderers occupy.</summary>
        private static void FitTrigger(GameObject prop)
        {
            // The importer puts a mesh collider on every prop (AssetImportPolicy's Prop
            // rule) because §08's loose pieces have to be droppable. On an interactable
            // it is the wrong shape and cannot always be a trigger — see the remarks on
            // CreateProp — so it goes.
            foreach (var existing in prop.GetComponentsInChildren<Collider>(true))
            {
                Despawn(existing);
            }

            var bounds = LocalBounds(prop);

            // Grown to the aim minimum from the model's own base, never around its
            // centre. A 1.8 cm silver spoon centred in a 30 cm box would put half the
            // volume the crosshair tests against below the floor it is lying on, and the
            // ray would have to be aimed under the world to find it.
            // The aim minimum is metres, and this box is in the prop's own space — which
            // is a hundredth of a metre per unit under this project's FBX scale policy
            // (see InteractablePropLibrary.Instantiate). Converted rather than assumed:
            // a 0.30 taken literally here would be a 30 m trigger.
            var scale = prop.transform.lossyScale;
            var floor = new Vector3(
                MinimumTargetMetres / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                MinimumTargetMetres / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                MinimumTargetMetres / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));

            var size = Vector3.Max(bounds.size, floor);
            var centre = bounds.center;
            centre.y = bounds.min.y + (size.y * 0.5f);

            var box = prop.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = prop.AddComponent<BoxCollider>();
            }

            box.isTrigger = true;
            box.center = centre;
            box.size = size;
        }

        /// <summary>
        /// The prop's renderers as one box in the prop's own space.
        /// <para>
        /// Not <c>Renderer.bounds</c>, which is a world-axis-aligned box: a yawed 1.14 m
        /// portrait would report a 1.4 m square footprint and a yawed 6.7 m van a 6.7 m
        /// one, and the trigger would swallow the corridor beside it.
        /// </para>
        /// </summary>
        private static Bounds LocalBounds(GameObject prop)
        {
            var toProp = prop.transform.worldToLocalMatrix;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var started = false;

            foreach (var renderer in prop.GetComponentsInChildren<Renderer>(true))
            {
                var local = renderer.localBounds;
                var matrix = toProp * renderer.transform.localToWorldMatrix;

                for (var corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z);

                    var point = matrix.MultiplyPoint3x4(offset);
                    if (!started)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        started = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return bounds;
        }

        /// <summary>
        /// Removes a prop, or any other object, from the world.
        /// <para>
        /// <c>Object.Destroy</c> is deferred to the end of a frame and refuses to run
        /// outside play mode, so a headless verification that drives a whole match from
        /// the editor would leave every "picked up" piece of loot standing there and
        /// log an error for each one. Both paths do the same thing to the world; only
        /// the timing differs.
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

        /// <summary>
        /// The shortest side any interactable's trigger box may have, metres.
        /// <para>
        /// Aim tolerance, not rig geometry. §08's 반지 is 2.2 cm across and its 회중시계
        /// is 7 cm; a crosshair on a floor two metres away covers a few centimetres, so
        /// a collider that matched the mesh would make the two most efficient pieces in
        /// the economy unpickable in practice. The visual stays true to §08's sizes —
        /// what the player sees is what the piece is — and only the volume the ray tests
        /// against is widened. It is the width of a hand, which is the thing being
        /// simulated.
        /// </para>
        /// <para>
        /// Public because <see cref="DropPlacement"/> sizes its
        /// clearance sweep from it: the volume a released piece is given to land in and
        /// the volume the crosshair later tests against have to be the same number, or a
        /// piece can be put somewhere it cannot be picked up from.
        /// </para>
        /// </summary>
        public const float MinimumTargetMetres = 0.30f;

        /// <summary>What the model hangs under, so a swap has one thing to remove.</summary>
        private const string ModelChildName = "Model";

        /// <summary>How far above and below the asked-for point a floor is looked for.</summary>
        private const float SettleSearchMetres = 0.5f;

        /// <summary>
        /// Gap left between a settled prop and the surface, metres. Rig geometry: enough
        /// to clear §12's gravel relief, small enough that a 1.7 cm 반지 does not float.
        /// <para>
        /// Public so <see cref="DropPlacement"/> sets a dropped piece
        /// down at exactly the height the spawn path puts one at. Two clearances would
        /// drift, and the one nobody was looking at would be the one that sank into the
        /// 자갈.
        /// </para>
        /// </summary>
        public const float FloorClearanceMetres = 0.02f;
    }
}
