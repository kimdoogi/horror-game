#nullable enable

using System.Collections;
using System.Text.RegularExpressions;
using HorrorGame.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.PlayerRig
{
    /// <summary>
    /// §05 from the inside: <b>"자기 몸은 안 보이므로 손만 있으면 된다"</b> — the body is
    /// hidden from its owner, the hands are not, and the whole thing still casts a shadow.
    /// <para>
    /// These build the rig out of skinned renderers carrying the material slots
    /// <c>Player.fbx</c> exports, because that is exactly what the policy keys off. A test
    /// that handed <see cref="PlayerFirstPersonView"/> a ready-made list of renderers and
    /// asserted it set flags on them would prove the loop and not the decision, and the
    /// decision — "which of these three meshes is a hand" — is the part that was wrong.
    /// </para>
    /// <para>
    /// The last test pins the failure direction, which matters more than the others. If a
    /// regenerated model ever comes back as one merged mesh the classifier must fall back
    /// to hiding it — no hands, which is a disappointment — rather than to drawing it,
    /// which is a chest across the middle of the screen and unplayable.
    /// </para>
    /// </summary>
    public sealed class PlayerFirstPersonViewTests
    {
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
        public IEnumerator The_owner_sees_their_hands_and_not_their_chest()
        {
            var rig = BuildSplitRig();
            yield return null;

            Assert.That(Mode(rig, "Player_Body"), Is.EqualTo(ShadowCastingMode.ShadowsOnly),
                "§05: the torso sits across the near plane in first person and must not be drawn.");
            Assert.That(Renderer(rig, "Player_Arms").shadowCastingMode, Is.EqualTo(ShadowCastingMode.On),
                "§05 asks for 손. The arms are the only thing on screen that belongs to the player.");
            Assert.That(Renderer(rig, "Player_Arms").enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator The_hidden_body_still_casts_its_shadow()
        {
            var rig = BuildSplitRig();
            yield return null;

            // ShadowsOnly and not disabled, on purpose. §03 makes a narrow beam the only
            // light in the building, so a player's own shadow thrown down a corridor is
            // free atmosphere and a cue for where they are pointing. Disabling the
            // renderer would be the cheaper fix and would take that with it.
            Assert.That(Renderer(rig, "Player_Body").enabled, Is.True,
                "The body renderer must stay enabled — ShadowsOnly is how it stops being drawn.");
            Assert.That(Mode(rig, "Player_Body"), Is.Not.EqualTo(ShadowCastingMode.Off));
        }

        [UnityTest]
        public IEnumerator A_remote_players_body_is_drawn_whole()
        {
            var rig = BuildSplitRig();
            var view = rig.GetComponent<PlayerFirstPersonView>();
            yield return null;

            view.IsOwner = false;
            view.Apply();

            Assert.That(Mode(rig, "Player_Body"), Is.EqualTo(ShadowCastingMode.On),
                "§13 puts the other three players on screen — 협동 게임에서는 다른 3명이 보여야 한다.");
        }

        [UnityTest]
        public IEnumerator The_torch_is_drawn_only_while_it_is_in_hand()
        {
            var rig = BuildSplitRig();
            var view = rig.GetComponent<PlayerFirstPersonView>();
            yield return null;

            view.SetHandPropVisible(false);
            Assert.That(Renderer(rig, "Player_Torch").enabled, Is.False,
                "§03 takes the light away while both hands are on the objective; the player has to see that.");

            view.SetHandPropVisible(true);
            Assert.That(Renderer(rig, "Player_Torch").enabled, Is.True);
            Assert.That(Renderer(rig, "Player_Torch").shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off),
                "The spot light sits a hand's width away; a 0.2 m object casting from there darkens the corridor.");
        }

        [UnityTest]
        public IEnumerator Pressing_F_puts_the_torch_in_the_hand()
        {
            // The path a player takes, not a call to the renderer: the key toggles
            // FlashlightState, PlayerFlashlight.Update reads it, and the mesh follows.
            var rig = BuildSplitRig();
            var flashlight = rig.AddComponent<PlayerFlashlight>();
            yield return null;

            Assert.That(Renderer(rig, "Player_Torch").enabled, Is.False,
                "A torch nobody has switched on is in a pocket, not in a fist.");

            flashlight.State.Toggle();
            yield return null;

            Assert.That(flashlight.InHand, Is.True);
            Assert.That(Renderer(rig, "Player_Torch").enabled, Is.True,
                "§10's most repeated trade — 손전등을 켠다 → 괴물이 본다 — has to be visible in the "
                + "player's own hands, not only in the lighting.");
        }

        [UnityTest]
        public IEnumerator A_model_that_lost_its_split_hides_everything_rather_than_showing_a_torso()
        {
            var rig = new GameObject("Player");
            _rig = rig;

            // One mesh carrying every slot in the file — what a merged regeneration looks
            // like. Equipment and skin together says "body", and no arrangement of
            // materials on a single renderer can draw half of it, which is the whole
            // reason the mesh is split.
            AddSkinned(rig, "Player_Merged", "Role_Listener",
                PlayerRigParts.SkinMaterial, "Player_Coverall", PlayerRigParts.GearMaterial);

            // Expected before the component exists: the warning is written from Awake, and
            // it is half the point — a silent fallback is how this defect survived.
            LogAssert.Expect(LogType.Warning, new Regex("No renderer under this rig"));
            var view = rig.AddComponent<PlayerFirstPersonView>();
            yield return null;

            Assert.That(view.ArmRendererCount, Is.EqualTo(0));
            Assert.That(Mode(rig, "Player_Merged"), Is.EqualTo(ShadowCastingMode.ShadowsOnly),
                "The safe failure is the old bug — no hands — never a body drawn into the owner's face.");
        }

        [Test]
        public void A_role_swap_does_not_make_the_arms_unrecognisable()
        {
            var rig = BuildSplitRig();

            // Touching Renderer.materials instantiates every slot and Unity renames them
            // "X (Instance)". §04 swaps slot 0 per RoleId, so this happens to the arms in
            // every real match; a classifier matching names exactly would work until the
            // first role was assigned and then quietly hide the hands.
            var arms = Renderer(rig, "Player_Arms");
            var instanced = arms.materials;
            Assert.That(instanced[0].name, Does.Contain("(Instance)"));

            Assert.That(PlayerRigParts.Classify(arms), Is.EqualTo(PlayerRigPart.Arms));
        }

        /// <summary>
        /// The three meshes <c>Player.fbx</c> exports, with the material composition
        /// <c>verify_mesh_split</c> asserts on the other side: equipment and skin on the
        /// body, skin and cloth with no equipment on the arms, equipment alone in the hand.
        /// </summary>
        private GameObject BuildSplitRig()
        {
            var rig = new GameObject("Player");
            _rig = rig;

            AddSkinned(rig, "Player_Body", "Role_Listener", PlayerRigParts.SkinMaterial,
                "Player_Coverall", PlayerRigParts.GearMaterial);
            AddSkinned(rig, "Player_Arms", "Role_Listener", PlayerRigParts.SkinMaterial, "Player_Coverall");
            AddSkinned(rig, "Player_Torch", PlayerRigParts.GearMaterial);

            rig.AddComponent<PlayerFirstPersonView>();
            return rig;
        }

        /// <summary>
        /// A renderer with the named material slots and no mesh. No vertex data on purpose:
        /// <c>Player.fbx</c> imports with Read/Write off, so a classifier that needed mesh
        /// data would work in the editor and fail in a shipped player, and a test that
        /// supplied it would hide that.
        /// </summary>
        private static void AddSkinned(GameObject rig, string name, params string[] materials)
        {
            var child = new GameObject(name);
            child.transform.SetParent(rig.transform, false);

            var slots = new Material[materials.Length];
            for (var i = 0; i < materials.Length; i++)
            {
                slots[i] = new Material(Shader.Find("Unlit/Color")) { name = materials[i] };
            }

            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMaterials = slots;
        }

        private static SkinnedMeshRenderer Renderer(GameObject rig, string name)
        {
            var child = rig.transform.Find(name);
            Assert.That(child, Is.Not.Null, "no renderer called " + name + " under the rig");
            return child!.GetComponent<SkinnedMeshRenderer>();
        }

        private static ShadowCastingMode Mode(GameObject rig, string name)
        {
            return Renderer(rig, name).shadowCastingMode;
        }
    }
}
