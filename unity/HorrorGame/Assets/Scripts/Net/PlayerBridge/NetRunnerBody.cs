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
    /// exactly one of. Three corrections are then applied, and every one of them is a bug
    /// if it is forgotten:
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
    /// <item><description>
    /// <b>The body is animated.</b> This used to read "the copy is not animated", and it
    /// was the most visible defect in the multiplayer game: the local runner plays nine
    /// clips off <c>Runner.fbx</c> and everybody else was a mannequin gliding through the
    /// maze in the bind pose. <see cref="NetRunnerAnimation"/> is added here and handed
    /// the <em>source rig's own</em> <see cref="PlayerAnimatorDriver"/>, which is where
    /// those clips already live as serialised references — the same argument as the whole
    /// class: the object guaranteed to be correct is the one in the loaded scene, because
    /// <c>AssetDatabase</c> does not exist in a player build. The clips are read through
    /// <c>ClipFor</c>, so the pose → clip mapping stays owned by the one class that
    /// documents itself as owning it.
    /// </description></item>
    /// </list>
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
        /// The local rig's driver, which holds <c>Runner.fbx</c>'s clips as serialised
        /// references. Resolved with <see cref="_template"/> and from the same rig, so the
        /// body and the clips that move it can never come from two different players.
        /// </summary>
        private static PlayerAnimatorDriver? _clipSource;

        private static bool _reportedNoClips;

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
            Animate(body, _clipSource);
            return body;
        }

        /// <summary>
        /// Forgets the cached rig. For tests, and for anything that swaps the local player
        /// out mid-session.
        /// </summary>
        public static void Forget()
        {
            _template = null;
            _clipSource = null;
            _reportedNoClips = false;
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

            // Taken from the same rig, in the same pass, so the two cannot disagree about
            // which player was copied. GetComponentInChildren includes the object it is
            // called on, which is where PlayerFeelHarnessMenu.BuildRig puts the driver —
            // searching the children as well costs nothing and survives it being moved
            // onto the model, which is where a reader would look for it.
            _clipSource = rig.GetComponentInChildren<PlayerAnimatorDriver>(includeInactive: true);

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

        /// <summary>
        /// Gives the copy the clips the original is already playing.
        /// <para>
        /// Added and bound in two steps because <c>AddComponent</c> on a live object runs
        /// <c>Awake</c> and <c>OnEnable</c> immediately, and at that instant the component
        /// is a bare copy that does not know which rig it came from —
        /// <see cref="NetRunnerAnimation.Bind"/> is what turns it into this player's body
        /// and starts the graph.
        /// </para>
        /// <para>
        /// A rig with no driver, or a driver whose clip slots are empty, leaves the body
        /// standing in the bind pose and <em>says so</em>, once. That state is exactly the
        /// defect this work closed and it has a known cause with a known fix —
        /// <c>SoloPlaytest.AuditAnimatorWiring</c> reads the saved scene back and names
        /// which of the three it is — so a silent null here would put a sliding runner
        /// back in the build with a green suite over it.
        /// </para>
        /// </summary>
        private static void Animate(GameObject body, PlayerAnimatorDriver? source)
        {
            var animation = body.AddComponent<NetRunnerAnimation>();
            if (source != null)
            {
                animation.Bind(source);
            }

            if (animation.HasClips || _reportedNoClips)
            {
                return;
            }

            _reportedNoClips = true;
            Debug.LogWarning(
                "[Net] 원격 주자의 몸에 넣을 클립이 없다 — "
                + (source == null
                    ? "the local rig has no PlayerAnimatorDriver at all"
                    : "the local rig's PlayerAnimatorDriver has every clip slot empty")
                + ". Every other player will stand in Runner.fbx's bind pose and slide, which is the most "
                + "visible defect this game can ship. Run HorrorGame.EditorTools.SoloPlaytest.BuildBatch — its "
                + "read-back audit names which of the three causes it is (no driver / import wrong / names "
                + "wrong) rather than leaving it to be guessed.");
        }
    }
}
