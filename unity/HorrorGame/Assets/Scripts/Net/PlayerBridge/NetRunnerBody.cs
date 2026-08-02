#nullable enable

using HorrorGame.Gameplay.Player;
using UnityEngine;

namespace HorrorGame.Net.PlayerBridge
{
    /// <summary>
    /// The body a networked runner is drawn with: a copy of the model the local player is
    /// already standing inside.
    /// <para>
    /// <b>Why a copy of the live rig and not a prefab.</b> <see cref="NetRunner"/> offers
    /// two ways to fill its seam — a <c>Resources</c> prefab at
    /// <see cref="NetRunner.VisualResourcePath"/>, or a factory — and the prefab is not
    /// available to this project today. The runner model is <c>Assets/Models/Player/Runner.fbx</c>
    /// and the only thing that turns it into a playable character is
    /// <c>PlayerFeelHarnessMenu.BuildRig</c>, which lives in an <c>includePlatforms:
    /// ["Editor"]</c> assembly and reads the FBX, its nine animation clips and forty
    /// footstep files through <c>AssetDatabase</c>. None of that exists in a player build.
    /// Making a prefab would mean either (a) hand-authoring a model-variant asset that no
    /// code produces and no test can regenerate, or (b) forking the character into a
    /// second, prefab-shaped runner that would drift from the one the harness builds — and
    /// the second is the failure this repo keeps repeating, dressed up as an asset.
    /// </para>
    /// <para>
    /// So the body is taken from the object that is guaranteed to be correct: the rig in
    /// the loaded scene, the one the person on this machine is looking out of. Every peer
    /// loads the same descent scene, so every peer has one. It carries the real materials,
    /// so a URP build cannot render it magenta — the scar <c>NetRunner</c> already
    /// documents from <c>GameObject.CreatePrimitive</c>.
    /// </para>
    /// <para>
    /// <b>What is copied and what is corrected.</b> Only the rig's <c>Visual</c> child —
    /// the FBX instance and its renderers. Not the camera, not the audio listener, not the
    /// character controller, not the input router: a remote runner is a body, and every
    /// one of those would be a second one of something the local player already has
    /// exactly one of. Two corrections are then applied, and both are bugs if they are
    /// forgotten:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>The body is drawn.</b> The source rig has had <see cref="PlayerFirstPersonView"/>
    /// applied with <c>IsOwner</c> true, which sets its torso and legs to
    /// <c>ShadowsOnly</c> — §05's "자기 몸은 안 보이므로". Copy that state onto a remote
    /// runner and every other player is an invisible shadow. That component's own summary
    /// anticipates this caller — "a future network spawner, which learns whose body this
    /// is" — so the fix is its own <c>IsOwner = false</c> path rather than a second policy
    /// written here.
    /// </description></item>
    /// <item><description>
    /// <b>The arms come off the fill light's layer.</b> <see cref="PlayerHandFill"/> puts
    /// the owner's arms on a private rendering layer so one small light can brighten them
    /// without paying for the building's darkness. A copy inherits that layer, and the
    /// owner's fill light would then also light every other player's forearms from across
    /// the map.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>What this does not do.</b> The copy is not animated. §05's remote animation
    /// needs a <c>PlayerAnimatorDriver</c> fed from the replicated speed and carry state,
    /// and the driver's nine clips are serialised fields that only the editor rig builder
    /// fills — so a remote runner today stands in the model's bind pose and slides. That
    /// is an honest, visible gap rather than a silent one, and it is written down in the
    /// report rather than papered over with a capsule.
    /// </para>
    /// </summary>
    public static class NetRunnerBody
    {
        /// <summary>
        /// Name of the rig child holding the model. <c>PlayerFeelHarnessMenu.BuildRig</c>
        /// names it, and <see cref="NetRunner.VisualChildName"/> happens to use the same
        /// word for the same reason — it is the model, hung under something that is not.
        /// </summary>
        public const string RigVisualChildName = "Visual";

        private static Transform? _template;

        /// <summary>
        /// Builds one body, or null when this machine has no rig to copy — a headless
        /// host, §11's lobby before the descent loads, a test scene with no player in it.
        /// Null is a runner with no renderer, which <see cref="NetRunner"/> states plainly
        /// is preferred to inventing one.
        /// </summary>
        public static GameObject? Build()
        {
            var template = ResolveTemplate();
            if (template == null)
            {
                return null;
            }

            var body = Object.Instantiate(template.gameObject);
            DrawWhole(body);
            return body;
        }

        /// <summary>
        /// Forgets the cached rig. For tests, and for anything that swaps the local player
        /// out mid-session.
        /// </summary>
        public static void Forget()
        {
            _template = null;
        }

        /// <summary>
        /// The model under the local player's rig.
        /// <para>
        /// Cached, because twenty runners would otherwise mean twenty scene-wide searches
        /// in one frame. The cache is safe against a scene load without any bookkeeping:
        /// a destroyed <c>Transform</c> compares equal to null under Unity's operator, so
        /// the next call re-resolves.
        /// </para>
        /// </summary>
        private static Transform? ResolveTemplate()
        {
            if (_template != null)
            {
                return _template;
            }

            var rig = Object.FindFirstObjectByType<PlayerMotor>();
            if (rig == null)
            {
                return null;
            }

            // By name first, because that is the contract the rig builder writes. The
            // fallback is what actually makes this robust: any child holding a skinned
            // renderer is the model, whatever it got called, and a rename would therefore
            // cost nothing rather than costing every player their body.
            var named = rig.transform.Find(RigVisualChildName);
            if (named != null)
            {
                _template = named;
                return _template;
            }

            var skinned = rig.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true);
            if (skinned == null)
            {
                return null;
            }

            var node = skinned.transform;
            while (node.parent != null && node.parent != rig.transform)
            {
                node = node.parent;
            }

            _template = node.parent == rig.transform ? node : null;
            return _template;
        }

        /// <summary>
        /// Turns an owner's body back into somebody else's. See the class remarks for why
        /// each half is not optional.
        /// </summary>
        private static void DrawWhole(GameObject body)
        {
            var renderers = body.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (var i = 0; i < renderers.Length; i++)
            {
                // Renderer.renderingLayerMask is a uint and Light.renderingLayerMask is an
                // int; PlayerHandFill's constant is the uint form, which is the one that
                // belongs here. No cast — a cast between the two is how the mask ends up
                // meaning a different thing on the light than on the mesh.
                renderers[i].renderingLayerMask = PlayerHandFill.DefaultRenderingLayerMask;
            }

            // Added to the copy rather than copied with it: PlayerFirstPersonView sits on
            // the rig ROOT, and only the model is being taken. Its Awake applies the owner
            // policy immediately — AddComponent on a live object runs Awake at once — so
            // IsOwner is corrected and Apply is called again straight after, which the
            // component is documented to be idempotent under.
            var view = body.AddComponent<PlayerFirstPersonView>();
            view.IsOwner = false;
            view.Apply();
        }
    }
}
