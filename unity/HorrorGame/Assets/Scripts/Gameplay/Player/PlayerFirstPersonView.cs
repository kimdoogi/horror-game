#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Decides what the person inside the rig sees of their own body.
    /// <para>
    /// §05 says it in one line — <b>"자기 몸은 안 보이므로 손만 있으면 된다"</b> — and both
    /// halves are requirements. The body is not drawn, because the model is parented under
    /// the rig and in first person its shoulders and chest sit across the near plane as a
    /// shape along the bottom of the screen. The hands <em>are</em> drawn, because a
    /// first-person game where nothing on screen belongs to you is a floating camera, and
    /// §03 asks the player to tell four states apart — empty hands, torch, loot, objective
    /// — with no HUD to read them off.
    /// </para>
    /// <para>
    /// <b>The shadow survives.</b> Everything hidden is set to
    /// <see cref="ShadowCastingMode.ShadowsOnly"/>, never disabled. In a game where §03
    /// makes a narrow beam the only light, a player's own shadow thrown down a corridor is
    /// free atmosphere and a cue for where the beam is pointing — and it is cast from the
    /// whole body, not from the arms alone, because the body still exists to the renderer.
    /// This is the property the fix that caused the defect got right and must not lose:
    /// the previous version set <em>every</em> renderer to ShadowsOnly, which solved the
    /// occlusion and took the arms with it.
    /// </para>
    /// <para>
    /// <b>Per owner, not per rig.</b> §13 makes the other three players visible to each
    /// other, so a remote copy of this model must be drawn whole. That is what
    /// <see cref="IsOwner"/> selects; call <see cref="Apply"/> after changing it. Until
    /// the network layer spawns remote bodies every rig built is the local one, which is
    /// why the default is true.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-90)]
    public sealed class PlayerFirstPersonView : MonoBehaviour
    {
        [Tooltip("Rig root to search for renderers. Left empty, this transform.")]
        [SerializeField]
        private Transform? _rigRoot;

        [Tooltip("False for a remote player's body (§13): everything is drawn normally.")]
        [SerializeField]
        private bool _isOwner = true;

        [Tooltip("Log the classification once. A regenerated model that merged its meshes is otherwise silent.")]
        [SerializeField]
        private bool _report = true;

        private readonly List<SkinnedMeshRenderer> _bodyParts = new List<SkinnedMeshRenderer>();
        private readonly List<SkinnedMeshRenderer> _armParts = new List<SkinnedMeshRenderer>();
        private readonly List<SkinnedMeshRenderer> _handProps = new List<SkinnedMeshRenderer>();
        private bool _collected;

        // Stowed until something says otherwise. §10 makes switching the light on a
        // decision with a cost, and a rig that arrived with the torch already in the fist
        // would show the player they had made it before they had.
        private bool _handPropVisible;
        private bool _handPropApplied;

        /// <summary>
        /// Whether this rig belongs to the player looking through its camera. Setting it
        /// does not re-apply on its own — call <see cref="Apply"/>, so a spawner can set
        /// several fields and pay for one pass.
        /// </summary>
        public bool IsOwner
        {
            get { return _isOwner; }
            set { _isOwner = value; }
        }

        /// <summary>
        /// The renderers riding a single hand — §05's flashlight today. Whoever owns the
        /// item's state decides whether it is in hand; see <see cref="PlayerFlashlight"/>.
        /// </summary>
        public IReadOnlyList<SkinnedMeshRenderer> HandProps
        {
            get
            {
                Collect();
                return _handProps;
            }
        }

        /// <summary>How many renderers were classified as the owner's own hands. Zero means §05 is not being met.</summary>
        public int ArmRendererCount
        {
            get
            {
                Collect();
                return _armParts.Count;
            }
        }

        /// <summary>
        /// Applies the policy to every renderer under the rig.
        /// <para>
        /// Public and idempotent because three different callers need it: the editor rig
        /// builder, which bakes the result into a scene; a future network spawner, which
        /// learns whose body this is a frame after it exists; and a shot rig, which has to
        /// photograph the same decision the player gets rather than a re-implementation of
        /// it.
        /// </para>
        /// </summary>
        public void Apply()
        {
            Collect();

            var hidden = _isOwner ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            for (var i = 0; i < _bodyParts.Count; i++)
            {
                _bodyParts[i].shadowCastingMode = hidden;
            }

            for (var i = 0; i < _armParts.Count; i++)
            {
                // Drawn and casting. The arms are part of the same silhouette on the
                // corridor wall as the body, and a shadow missing its arms reads as a
                // rendering fault rather than as a person.
                _armParts[i].shadowCastingMode = ShadowCastingMode.On;
            }

            for (var i = 0; i < _handProps.Count; i++)
            {
                // Off, not On: the spot light sits on FlashlightMount, a hand's width from
                // this mesh, and a 0.2 m object between a light and everything else throws
                // a shadow across the whole corridor. The body's shadow is the one that
                // carries the atmosphere; the torch's would only be a bug.
                _handProps[i].shadowCastingMode = ShadowCastingMode.Off;
            }

            // Whatever the item state is, pushed through the same path that maintains it.
            SetHandPropVisible(_handPropVisible);

            if (_report)
            {
                _report = false;
                Report();
            }
        }

        /// <summary>
        /// Shows or hides everything classified as a held item.
        /// <para>
        /// Separate from <see cref="Apply"/> because it changes many times a match and the
        /// classification does not. §03 takes the torch away the instant the objective is
        /// lifted ("양손을 쓴다 → 손전등을 들 수 없다") and §10 has the player switching it
        /// off whenever the monster might be looking, so this is the most-toggled thing on
        /// the rig.
        /// </para>
        /// </summary>
        /// <param name="visible">True while the item is in hand.</param>
        public void SetHandPropVisible(bool visible)
        {
            Collect();

            // Guarded rather than written blind: the caller polls a state every frame,
            // and assigning Renderer.enabled dirties the culling entry whether or not the
            // value changed.
            if (_handPropVisible == visible && _handPropApplied)
            {
                return;
            }

            _handPropVisible = visible;
            _handPropApplied = true;

            for (var i = 0; i < _handProps.Count; i++)
            {
                _handProps[i].enabled = visible;
            }
        }

        private void Awake()
        {
            Apply();
        }

        private void Reset()
        {
            _rigRoot = transform;
        }

        private void Collect()
        {
            if (_collected)
            {
                return;
            }

            _collected = true;
            var root = _rigRoot != null ? _rigRoot : transform;

            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                switch (PlayerRigParts.Classify(renderer))
                {
                    case PlayerRigPart.Arms:
                        _armParts.Add(renderer);
                        break;
                    case PlayerRigPart.HandProp:
                        _handProps.Add(renderer);
                        break;
                    default:
                        // Body and Unknown together. An unrecognised renderer is hidden
                        // rather than drawn: a chest across the near plane is a defect the
                        // player cannot play around, and a missing hand is one they can.
                        _bodyParts.Add(renderer);
                        break;
                }
            }
        }

        private void Report()
        {
            // The per-renderer breakdown is in both the warning and the ordinary log on
            // purpose. When this goes wrong it goes wrong at the import — a mesh that
            // arrived bound to more bones than it is weighted to, or a split that was lost
            // — and the only cheap way to tell those apart is to see what the importer
            // actually produced.
            var detail = new System.Text.StringBuilder();
            Describe(detail, "arms", _armParts);
            Describe(detail, "hidden", _bodyParts);
            Describe(detail, "held", _handProps);

            if (_armParts.Count == 0)
            {
                Debug.LogWarning(
                    "[Player] No renderer under this rig reads as the owner's hands, so they will "
                    + "see nothing of themselves. §05 asks for 손. Player.fbx must export "
                    + "Player_Body, Player_Arms and Player_Torch as separate meshes — check "
                    + "MESH_SPLIT in the gen_player_model.py output." + detail,
                    this);
                return;
            }

            Debug.Log(
                "[Player] first-person view: " + _armParts.Count + " arm renderer(s) drawn, "
                + _bodyParts.Count + " hidden but still casting, " + _handProps.Count
                + " hand prop(s). Owner=" + _isOwner + "." + detail,
                this);
        }

        private static void Describe(
            System.Text.StringBuilder into, string label, List<SkinnedMeshRenderer> renderers)
        {
            for (var i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                into.Append("\n  ").Append(label).Append(": ").Append(renderer.name)
                    .Append(" bones=").Append(renderer.bones.Length)
                    .Append(" weighted=").Append(PlayerRigParts.DescribeBinding(renderer))
                    .Append(" materials=").Append(renderer.sharedMaterials.Length);
            }
        }
    }
}
