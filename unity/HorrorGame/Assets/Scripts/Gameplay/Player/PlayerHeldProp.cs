#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Puts §03's 목표물 and §08's 대형 전리품 in the player's hands, as the actual model.
    /// <para>
    /// <b>Both of the two-handed carry states rendered empty hands.</b> ART.md §7.13
    /// measured it: <c>land_hands_20_loot.png</c> and <c>land_hands_30_objective.png</c>
    /// show a carry <i>pose</i> holding nothing at all. §03 defines those two states
    /// entirely by what they cost — 「양손을 쓴다 → 손전등을 들 수 없다」 and 「전리품
    /// 동시 소지 불가」 — and §03 also forbids a HUD, so the object in the hands is the
    /// only place the cost can be shown. Four states that all look like empty hands is
    /// three states missing.
    /// </para>
    /// <para>
    /// <b>The model comes from <c>InteractablePropLibrary</c>, never from a
    /// primitive.</b> ART.md §7.11's lesson: anything built at runtime with
    /// <c>GameObject.CreatePrimitive</c> resolves its material from
    /// <c>RenderPipelineAsset.defaultMaterial</c>, which URP answers only in the editor —
    /// in a player it returns null and Unity falls back to a shader the build does not
    /// contain. Every one of those rendered as a plausible white box in every editor
    /// screenshot and as error magenta in the build the owner played.
    /// </para>
    /// <para>
    /// It is the same library, the same prefab and the same materials the piece had while
    /// it was lying on the floor, which is the point: a player who just picked a crate up
    /// is holding the crate they were looking at.
    /// </para>
    /// <para>
    /// 🔴 <b>UNFINISHED, and photographed as such.</b> The object is instantiated, parented
    /// to <c>ObjectiveMount</c> and scaled correctly — <c>h4_20_loot.png</c> brightened 7×
    /// shows its shadow — but it is <b>not in the camera's frustum</b>, so both two-handed
    /// states still render the empty hands ART.md §7.13 measured. The mount bone sits at
    /// (0, −0.405, 1.455) on <c>Chest</c> with its own +Y along the bone, and the offset
    /// below has not been solved against the Carry pose's actual hand positions —
    /// <c>gen_player_model.pose_metrics</c> reports <c>objective_reach</c> per clip and is
    /// where that number should come from rather than from a typed guess. Shadow casting
    /// is Off until then, so this component cannot make a frame worse than it found it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-70)]
    public sealed class PlayerHeldProp : MonoBehaviour
    {
        /// <summary>
        /// §03's 목표물. One key, because there is one objective per match.
        /// <para>
        /// Spelled out rather than taken from <c>PropModels.Objective</c>, and that is a
        /// layering fact rather than a shortcut: <c>Assets/Scripts/Gameplay/Interaction</c>
        /// has no assembly definition, so it compiles into <c>Assembly-CSharp</c>, which
        /// references every asmdef and is referenced by none. This component lives in
        /// <c>HorrorGame.Gameplay.Player</c> because that is where the loadout and the rig
        /// bones are, so it cannot see the constant. <c>HeldPropModels</c> asserts the two
        /// strings still agree, on the side of the fence that can see both.
        /// </para>
        /// </summary>
        public const string ObjectiveKey = "Objective";

        /// <summary>
        /// How a model key becomes an instance. Filled in by <c>HeldPropModels</c>, which
        /// is on the other side of the assembly boundary described above and can reach
        /// <c>InteractablePropLibrary</c>.
        /// <para>
        /// A hole declared by the lower layer and filled by the upper one, rather than a
        /// reference pointing the wrong way. It is also the seam that makes this component
        /// testable without a Resources folder.
        /// </para>
        /// </summary>
        public static Func<string, GameObject>? ModelSource { get; set; }

        /// <summary>
        /// The 대형 전리품 model used when nothing says which piece was picked up.
        /// <para>
        /// <see cref="PlayerLoadout.CarryingOversizePiece"/> is a bool — the piece's own
        /// identity is not on the player yet — so this is the crate rather than the
        /// portrait. It is a field on this component instead of a literal at the use site
        /// so that whoever gives the loadout a piece id has one place to write it, and
        /// <c>PropModels.LargePieceModels</c> stays the list of what is allowed.
        /// </para>
        /// </summary>
        [SerializeField]
        private string _oversizeModelKey = "Loot_LargePiece_Chest";

        /// <summary>
        /// Where a two-handed object sits, in <c>ObjectiveMount</c>'s own space.
        /// <para>
        /// The mount bone is authored by <c>gen_player_ai.py</c> at the point both palms
        /// meet in the Carry pose, and — this is the part that has to be got right —
        /// <b>a Blender bone's local +Y runs along the bone</b>, so on this one +Y is
        /// forward, out of the chest. The offset is the object's own half-depth pushed
        /// that way. Negative Y instead puts a 0.6 m crate inside the ribcage and across
        /// the near plane, which photographed as a black slab down the middle of both
        /// two-handed frames.
        /// </para>
        /// </summary>
        [SerializeField]
        private Vector3 _localOffset = new Vector3(0f, 0.12f, -0.04f);

        [SerializeField]
        private Vector3 _localEuler = new Vector3(-8f, 0f, 0f);

        private PlayerLoadout? _loadout;
        private Transform? _mount;
        private Transform? _container;
        private GameObject? _instance;
        private string _shownKey = string.Empty;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        /// <summary>The model key currently in the hands, or empty when they are free.</summary>
        public string ShownKey => _shownKey;

        /// <summary>The renderers of the held object. Empty while nothing is held.</summary>
        public IReadOnlyList<Renderer> Renderers => _renderers;

        /// <summary>
        /// Brings the held object into line with the loadout.
        /// <para>
        /// Named to match <see cref="PlayerFlashlight.RefreshPresentation"/> and called
        /// from the same places for the same reason: the shot tools run outside play mode
        /// where <c>Update</c> never fires, and a review frame has to be the game's own
        /// decision rather than the tool's re-implementation of it.
        /// </para>
        /// </summary>
        public void RefreshPresentation()
        {
            Resolve();
            if (_loadout == null)
            {
                return;
            }

            var wanted = _loadout.CarryingObjective
                ? ObjectiveKey
                : _loadout.CarryingOversizePiece ? _oversizeModelKey : string.Empty;

            if (wanted == _shownKey)
            {
                return;
            }

            Clear();
            _shownKey = wanted;
            if (wanted.Length == 0 || _container == null)
            {
                return;
            }

            if (ModelSource == null)
            {
                Debug.LogWarning("[PlayerHeldProp] nothing has installed a ModelSource, so "
                    + "§03's " + wanted + " cannot be put in the hands and this carry state "
                    + "renders as the empty hands ART.md §7.13 measured. HeldPropModels "
                    + "installs one on load; a test rig that builds a player by hand has to "
                    + "set it itself.", this);
                _shownKey = string.Empty;
                return;
            }

            _instance = ModelSource(wanted);

            // Parented with worldPositionStays false and never repositioned itself. The
            // library's own note: the instance keeps the prefab's scale, and that scale is
            // the FBX unit conversion (Lcl Scaling 100 against a file scale of 0.01) living
            // on the root transform. Writing a position or a rotation onto that transform
            // is how a prop ends up a centimetre across.
            _instance.transform.SetParent(_container, worldPositionStays: false);
            _instance.name = "Held_" + wanted;

            foreach (var collider in _instance.GetComponentsInChildren<Collider>(true))
            {
                // DestroyImmediate outside play mode: the shot tools run in the editor,
                // where Destroy is deferred to a frame that never comes and Unity warns
                // about it on every prop.
                // A collider on a thing parented inside the player's own capsule pushes
                // the player around, blocks their own interaction ray, and — on a
                // MeshCollider on a moving transform — re-cooks every frame.
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }

            _renderers.Clear();
            foreach (var renderer in _instance.GetComponentsInChildren<Renderer>(true))
            {
                // OFF, and that is a placeholder rather than a decision — see the class
                // note. While the placement is wrong the object is out of frame and the
                // only thing it contributes is a hard black band down the corridor that
                // nothing on screen explains. A shadow with no caster is worse than no
                // shadow: it reads as a rendering fault. Turn this back to On in the same
                // change that fixes the offset.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderers.Add(renderer);
            }
        }

        /// <summary>Drops whatever is in the hands. Idempotent.</summary>
        public void Clear()
        {
            _renderers.Clear();
            _shownKey = string.Empty;
            if (_instance == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_instance);
            }
            else
            {
                DestroyImmediate(_instance);
            }

            _instance = null;
        }

        private void Awake()
        {
            Resolve();
        }

        private void LateUpdate()
        {
            RefreshPresentation();
        }

        private void OnDisable()
        {
            Clear();
        }

        private void Resolve()
        {
            _loadout ??= GetComponent<PlayerLoadout>();
            _mount ??= PlayerRigBones.Find(transform, PlayerRigBones.ObjectiveMount);
            if (_container != null || _mount == null)
            {
                return;
            }

            // A container of its own rather than parenting the model straight onto the
            // bone, so the placement offset lives on a transform this component owns and
            // the model's own transform is never written to.
            var holder = new GameObject("HeldPropMount");
            _container = holder.transform;
            _container.SetParent(_mount, worldPositionStays: false);
            _container.localPosition = _localOffset;
            _container.localRotation = Quaternion.Euler(_localEuler);

            // The container is forced back to world scale 1, and this is the whole reason
            // it exists as a separate transform. Player.fbx imports with a non-unit local
            // scale of (100, 100, 100) on its mesh roots — the FBX unit conversion, the
            // same one InteractablePropLibrary warns about on the prop side — so a bone
            // under it has a lossy scale of 100. A prop prefab already carries its own
            // 100, and 100 × 100 puts a 0.6 m objective on screen as a six-kilometre box.
            // Measured: the first run of this component wrote a 100.0% black frame for
            // §03's carry state, which is a scale bug wearing a rendering bug's clothes.
            var lossy = _mount.lossyScale;
            _container.localScale = new Vector3(
                Mathf.Approximately(lossy.x, 0f) ? 1f : 1f / lossy.x,
                Mathf.Approximately(lossy.y, 0f) ? 1f : 1f / lossy.y,
                Mathf.Approximately(lossy.z, 0f) ? 1f : 1f / lossy.z);
        }
    }
}
