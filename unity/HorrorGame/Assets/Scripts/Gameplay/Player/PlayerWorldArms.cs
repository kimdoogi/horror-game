#nullable enable

using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Lowers the arms on the copy of a player that <em>other people</em> see.
    /// <para>
    /// <b>The problem.</b> §05 promises the owner sees their own hands, and at the 80°
    /// vertical FOV §05 specifies an arm hanging at hip height puts the fist 22° below
    /// the bottom edge of the frame. So <c>gen_player_model</c>'s <c>FREE_ARM</c> and
    /// <c>FLASHLIGHT_ARM</c> hold both forearms up across the chest in every clip, and
    /// <c>verify_first_person</c> pins them there. That is correct for the one person
    /// inside the model and wrong for the three looking at it: from outside, the figure
    /// stands with both elbows flared and its hands up, permanently, doing nothing.
    /// </para>
    /// <para>
    /// <b>Why this is not solved in the rig.</b> The obvious fix is a second arm chain —
    /// a viewmodel — posed for the camera while the humanoid arms hang. <c>Player.fbx</c>
    /// imports as <b>Humanoid</b> (<c>animationType: 3</c>), and <c>AssetImportPolicy</c>
    /// states the consequence in its own words: a Humanoid avatar drops the animation of
    /// bones with no humanoid equivalent, <em>silently</em>. A <c>ViewLeftUpperArm</c>
    /// driven by clip curves would therefore freeze at its rest pose — the same failure
    /// that makes <c>Monster.fbx</c> Generic. Changing the player to Generic is possible
    /// and is a different decision: §05 wants four players sharing one clip set, which is
    /// the reason the policy gives for Humanoid in the first place.
    /// </para>
    /// <para>
    /// <b>So the split happens after the Animator, per viewer.</b> The clip writes the
    /// pose §05 needs; on a rig that is not the local player's, this rotates the four arm
    /// bones toward hanging in <c>LateUpdate</c>. It is applied as an <em>offset</em>,
    /// not as an absolute pose, so everything the clips do to the arms survives it — a
    /// walk still swings, a carry still lifts, the strain in <c>CarryHeavy</c> still
    /// reads. What is removed is the constant part: the held-up-for-the-camera component
    /// that was never meant for this viewer.
    /// </para>
    /// <para>
    /// Owner rigs are untouched — <see cref="PlayerFirstPersonView.IsOwner"/> is the same
    /// switch that decides which meshes are drawn, so the two cannot disagree about whose
    /// body this is.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerFirstPersonView))]
    [DefaultExecutionOrder(120)]
    public sealed class PlayerWorldArms : MonoBehaviour
    {
        /// <summary>Humanoid bone names this reaches. Left and right are handled together.</summary>
        public static readonly string[] ArmBones =
        {
            "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
        };

        [Tooltip("Rig root searched for the arm bones. Left empty, this transform.")]
        [SerializeField]
        private Transform? _rigRoot;

        [Tooltip("Degrees the upper arm is rotated down toward hanging. The clip's own swing is kept on top of this.")]
        [SerializeField]
        private float _upperArmDrop = 46f;

        [Tooltip("Degrees the forearm is unfolded. FREE_ARM's lo_down is -46, i.e. 46 degrees ABOVE horizontal — this is what takes it back down.")]
        [SerializeField]
        private float _forearmDrop = 62f;

        [Tooltip("0 leaves the clip pose alone, 1 applies the full drop. A dial rather than a bool so a carry state can keep its arms up.")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _weight = 1f;

        private PlayerFirstPersonView? _view;
        private readonly Transform?[] _bones = new Transform?[8];
        private bool _found;

        /// <summary>How much of the drop is applied, 0~1. A carry state can dial it down.</summary>
        public float Weight
        {
            get { return _weight; }
            set { _weight = Mathf.Clamp01(value); }
        }

        /// <summary>True when every arm bone was found. False means this is doing nothing.</summary>
        public bool Bound
        {
            get
            {
                Collect();
                for (var i = 0; i < _bones.Length; i++)
                {
                    if (_bones[i] == null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private void LateUpdate()
        {
            // After the Animator, which is the whole point: it has to overwrite a pose
            // that has already been written this frame, and DefaultExecutionOrder(120)
            // is what puts it there.
            Collect();

            _view ??= GetComponent<PlayerFirstPersonView>();
            if (_view == null || _view.IsOwner || _weight <= 0f)
            {
                return;
            }

            for (var side = 0; side < 2; side++)
            {
                // +1 on the left, −1 on the right. The arms leave the shoulders along ±X,
                // so the same world rotation drops one and raises the other; the sign is
                // what makes "down" mean down on both.
                var sign = side == 0 ? 1f : -1f;
                Drop(_bones[side * 4], _upperArmDrop * 0.18f, sign);   // shoulder
                Drop(_bones[side * 4 + 1], _upperArmDrop, sign);       // upper arm
                Drop(_bones[side * 4 + 2], _forearmDrop, sign);        // forearm
            }
        }

        /// <summary>
        /// Rotates one bone toward hanging, in world space.
        /// <para>
        /// World space rather than local on purpose: these bones are authored along ±X
        /// with per-bone rest orientations, and a local-space delta means a different
        /// direction on each one. Writing <c>Transform.rotation</c> lets Unity do the
        /// parent conversion, so what this file has to get right is one axis and one
        /// sign rather than eight rest frames.
        /// </para>
        /// <para>
        /// It composes with whatever the Animator wrote rather than replacing it, which
        /// is what keeps the walk's swing and the carry's strain.
        /// </para>
        /// </summary>
        private void Drop(Transform? bone, float degrees, float sign)
        {
            if (bone == null || degrees == 0f)
            {
                return;
            }

            // Rotating +X about +forward by +90° gives +up, so a NEGATIVE angle is the
            // one that takes the left arm down. The right arm points −X and needs the
            // opposite, which is the sign.
            var delta = Quaternion.AngleAxis(-degrees * _weight * sign, transform.forward);
            bone.rotation = delta * bone.rotation;
        }

        private void Collect()
        {
            if (_found)
            {
                return;
            }

            _found = true;
            var root = _rigRoot != null ? _rigRoot : transform;
            for (var i = 0; i < ArmBones.Length; i++)
            {
                _bones[i] = PlayerRigBones.Find(root, ArmBones[i]);
            }
        }
    }
}
