#nullable enable

using System.Collections;
using HorrorGame.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.PlayerRig
{
    /// <summary>
    /// §05's two readings of the same arms, and the fact that they disagree.
    /// <para>
    /// The clips hold both forearms up across the chest because at 80° of vertical FOV an
    /// arm hanging at hip height puts the fist off the bottom of the owner's screen. From
    /// outside, that is a person standing with their elbows flared doing nothing.
    /// <see cref="PlayerWorldArms"/> resolves it after the Animator and per viewer.
    /// </para>
    /// <para>
    /// These build the arm chain by hand rather than loading <c>Player.fbx</c>: the thing
    /// under test is "does the hand end up lower on a body somebody else is looking at",
    /// and that has to be true of the bone names the humanoid rig exports whether or not
    /// the model has been rebuilt. The chain is laid out the way the generator lays it
    /// out — along +X on the left and −X on the right, at shoulder height — so a sign
    /// error shows up as one arm going the wrong way, which is the mistake this component
    /// is most likely to make.
    /// </para>
    /// </summary>
    public sealed class PlayerWorldArmsTests
    {
        // Unity is Y-up; the generator's rig is authored Z-up in Blender and the FBX
        // importer converts. Building this rig with 1.44 in Z put the whole arm on the
        // floor and every assertion measured the wrong axis.
        private const float ShoulderY = 1.44f;

        private GameObject? _rig;

        [TearDown]
        public void TearDown()
        {
            if (_rig != null)
            {
                Object.DestroyImmediate(_rig);
                _rig = null;
            }
        }

        [UnityTest]
        public IEnumerator A_body_somebody_else_is_looking_at_drops_both_hands()
        {
            var rig = BuildArmRig();
            var view = rig.GetComponent<PlayerFirstPersonView>();
            var worldArms = rig.GetComponent<PlayerWorldArms>();
            Assert.That(worldArms.Bound, Is.True, "Every arm bone has to be found or this does nothing.");

            view.IsOwner = false;
            var before = (Hand(rig, "LeftHand").position.y, Hand(rig, "RightHand").position.y);
            yield return null;

            var afterLeft = Hand(rig, "LeftHand").position.y;
            var afterRight = Hand(rig, "RightHand").position.y;

            Assert.That(afterLeft, Is.LessThan(before.Item1 - 0.05f),
                "The left hand has to end up lower than the clip left it, or nothing was applied.");
            Assert.That(afterRight, Is.LessThan(before.Item2 - 0.05f),
                "And the right, which is the sign the component is most likely to get wrong: "
                + "the arms leave the shoulders along opposite axes, so one world rotation "
                + "drops one and raises the other.");
        }

        [UnityTest]
        public IEnumerator The_owner_keeps_the_pose_section_05_asked_for()
        {
            var rig = BuildArmRig();
            var view = rig.GetComponent<PlayerFirstPersonView>();

            view.IsOwner = true;
            var before = Hand(rig, "LeftHand").position;
            yield return null;
            yield return null;

            Assert.That(Hand(rig, "LeftHand").position, Is.EqualTo(before).Using(new Vec3Comparer(1e-4f)),
                "§05 promises the owner sees their own hands. Dropping them for the person "
                + "inside the model would be the original defect with the sign flipped.");
        }

        [UnityTest]
        public IEnumerator Weight_zero_is_the_clip_pose_untouched()
        {
            var rig = BuildArmRig();
            rig.GetComponent<PlayerFirstPersonView>().IsOwner = false;
            rig.GetComponent<PlayerWorldArms>().Weight = 0f;

            var before = Hand(rig, "LeftHand").position;
            yield return null;

            Assert.That(Hand(rig, "LeftHand").position, Is.EqualTo(before).Using(new Vec3Comparer(1e-4f)),
                "The weight is a dial so a carry state can keep its arms up; at 0 it has to "
                + "be exactly the pose the Animator wrote.");
        }

        private static Transform Hand(GameObject rig, string name)
        {
            var found = PlayerRigBones.Find(rig.transform, name);
            Assert.That(found, Is.Not.Null, name + " is missing from the test rig.");
            return found!;
        }

        /// <summary>
        /// Shoulder → upper arm → forearm → hand on both sides, along ±X at shoulder
        /// height: the rest layout <c>gen_player_model.bone_specs</c> builds, with the
        /// clips' raised pose faked by rotating the forearm up.
        /// </summary>
        private GameObject BuildArmRig()
        {
            _rig = new GameObject("Rig");
            _rig.AddComponent<PlayerFirstPersonView>();

            foreach (var (side, sign) in new[] { ("Left", 1f), ("Right", -1f) })
            {
                var shoulder = Bone(side + "Shoulder", _rig.transform,
                    new Vector3(sign * 0.035f, ShoulderY, 0f));
                var upper = Bone(side + "UpperArm", shoulder, new Vector3(sign * 0.145f, 0f, 0f));
                var lower = Bone(side + "LowerArm", upper, new Vector3(sign * 0.265f, 0f, 0f));
                var hand = Bone(side + "Hand", lower, new Vector3(sign * 0.245f, 0f, 0f));

                // What the clips do: the forearm comes up across the chest. Without this
                // the test would be measuring a T-pose, which is not the pose anybody
                // complained about.
                lower.localRotation = Quaternion.AngleAxis(sign * 55f, Vector3.forward);
                Assert.That(hand.position.y, Is.GreaterThan(ShoulderY - 0.05f));
            }

            _rig.AddComponent<PlayerWorldArms>();
            return _rig;
        }

        private static Transform Bone(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        private sealed class Vec3Comparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            private readonly float _epsilon;

            public Vec3Comparer(float epsilon)
            {
                _epsilon = epsilon;
            }

            public bool Equals(Vector3 a, Vector3 b) => (a - b).magnitude <= _epsilon;

            public int GetHashCode(Vector3 v) => v.GetHashCode();
        }
    }
}
