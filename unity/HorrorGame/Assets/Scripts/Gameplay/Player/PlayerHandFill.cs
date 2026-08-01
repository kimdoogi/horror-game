#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// A light that reaches the owner's own hands and nothing else in the building.
    /// <para>
    /// <b>Why this exists.</b> §05 asks for 손 and <see cref="PlayerFirstPersonView"/>
    /// delivers them: the arms are drawn, in frame, and correctly textured. Measured off
    /// a shipped frame, they are also at <b>3.5 of 255</b> mean luminance against a floor
    /// at 16.0 — geometrically present and visually absent. The owner reported it as
    /// "물건을 들면 손이 안 보임", and the answer they asked for is the right one:
    /// "1인칭이여도 손이나 이런것들은 힐끔힐끔 보이게" — glimpsed, not lit.
    /// </para>
    /// <para>
    /// <b>What keeps it off the building.</b> §03 is built on "어둠 = 목표의 잠금장치":
    /// a lamp near the camera that spilled into the corridor would unlock the objective
    /// for free. What confines this one is its <b>range</b> — 0.62 m reaches the arms and
    /// stops short of the floor. Measured, with the light on and off:
    /// <code>
    ///                   left arm   right arm   far floor   near floor
    ///   before               3.9         3.5        52.7          8.3
    ///   after               22.3        31.5        52.9          9.2
    /// </code>
    /// The arms move 6~9×, the floor moves 0.2 of 255, and nothing past 1 m moves at all.
    /// </para>
    /// <para>
    /// <b>The rendering layers below are set and currently do nothing. Do not trust them
    /// as the confinement.</b> URP honours a <em>light's</em> <c>renderingLayers</c>, but
    /// it only reads a <em>renderer's</em> <c>renderingLayerMask</c> when Rendering Layers
    /// are enabled on the renderer asset, and they are not enabled in
    /// <c>HorrorGame_URP_Renderer.asset</c>. Measured: with this light restricted to the
    /// arms' layer alone the arms went back to 3.5 — the light matched no renderer at all.
    /// They are set anyway so that turning that project setting on makes the intent real
    /// rather than requiring this file to be found again.
    /// </para>
    /// <para>
    /// <b>Why it is not emission on the material.</b> Emission would make the hands equally
    /// bright in the beam and in the dark, which reads as a decal rather than as a body.
    /// A light keeps the arms' own normals doing the work, so a hand still turns dark as it
    /// rotates away and still brightens when the torch comes up: the fill sets a floor
    /// under the exposure, it does not replace it.
    /// </para>
    /// <para>
    /// Owner-only. §13 draws the other three players whole and a remote body carrying its
    /// own private lamp would be lit from inside.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerFirstPersonView))]
    [DefaultExecutionOrder(-80)]
    public sealed class PlayerHandFill : MonoBehaviour
    {
        /// <summary>
        /// Rendering layer the fill and the arms share, as a bit index.
        /// <para>
        /// 1 rather than 0: layer 0 is URP's "Default" and every light and renderer in the
        /// project sits on it. Nothing else in the project sets a rendering layer, so 1 is
        /// free. Inert until Rendering Layers are enabled on the renderer asset — see the
        /// class comment.
        /// </para>
        /// </summary>
        public const int ArmRenderingLayerBit = 1;

        /// <summary>Bit mask form of <see cref="ArmRenderingLayerBit"/>.</summary>
        public const uint ArmRenderingLayerMask = 1u << ArmRenderingLayerBit;

        /// <summary>URP's "Default" rendering layer, which every other renderer keeps.</summary>
        public const uint DefaultRenderingLayerMask = 1u;

        [Tooltip("Camera or eye pivot the fill hangs from. Left empty, the rig's PlayerCameraRig is asked.")]
        [SerializeField]
        private Transform? _anchor;

        [Tooltip("Metres forward, right and up of the eye. Below and ahead reads as bounce off whatever is being carried.")]
        [SerializeField]
        private Vector3 _offset = new Vector3(0f, -0.28f, 0.34f);

        [Tooltip("Not linear near here: 2.2 and 0.40 both saturate the arms at 79~92 of 255. 0.055 lands them at 31.5. Sweep by an order of magnitude, not by a factor of two.")]
        [SerializeField]
        private float _intensity = 0.055f;

        [Tooltip("Metres, and this is the confinement §03 depends on — not the rendering layer. 0.62 m reaches the arms and leaves the far floor at 52.9 against 52.7 unlit.")]
        [SerializeField]
        private float _range = 0.62f;

        [Tooltip("Warm, because the alternative reads as moonlight on a hand two feet from your face indoors.")]
        [SerializeField]
        private Color _colour = new Color(1f, 0.86f, 0.72f, 1f);

        [Tooltip("Log the mask and intensity once. A rendering layer that silently reverts is otherwise invisible.")]
        [SerializeField]
        private bool _report = true;

        private Light? _light;

        /// <summary>The light this component owns, once it has built one. Null before <see cref="Start"/>.</summary>
        public Light? Fill
        {
            get { return _light; }
        }

        /// <summary>
        /// Puts the arms on <see cref="ArmRenderingLayerMask"/> and builds the fill. Public
        /// and idempotent for the same three callers <see cref="PlayerFirstPersonView.Apply"/>
        /// has: the rig builder, a network spawner and the shot rigs.
        /// </summary>
        public void Apply()
        {
            var view = GetComponent<PlayerFirstPersonView>();
            if (view == null)
            {
                return;
            }

            // A remote body is lit by the room like everything else. Tear the fill down
            // rather than dimming it, so ownership changing twice cannot leave two.
            if (!view.IsOwner)
            {
                MarkArms(view, DefaultRenderingLayerMask);
                if (_light != null)
                {
                    DestroyImmediate(_light.gameObject);
                    _light = null;
                }

                return;
            }

            // Default | Arm, not Arm alone: the arms must still be lit by §03's torch and
            // by every zone light, which sit on Default. Adding a bit widens what can
            // reach them; it never narrows it.
            MarkArms(view, DefaultRenderingLayerMask | ArmRenderingLayerMask);

            if (_light == null)
            {
                var host = new GameObject("HandFill");
                host.transform.SetParent(Anchor(), worldPositionStays: false);
                _light = host.AddComponent<Light>();
            }

            _light.transform.SetParent(Anchor(), worldPositionStays: false);
            _light.transform.localPosition = _offset;
            _light.transform.localRotation = Quaternion.identity;

            _light.type = LightType.Point;
            _light.color = _colour;
            _light.intensity = _intensity;
            _light.range = _range;

            // No shadows. The only thing in this light's range is the pair of arms it
            // exists for, and a hand shadowing the other hand from a lamp that is not in
            // the world is a shadow with no cause the player can see.
            _light.shadows = LightShadows.None;

            // Default | Arm on the light too, not Arm alone. See the class comment: a
            // renderer's mask is ignored until Rendering Layers are switched on for the
            // renderer asset, so a light restricted to Arm alone matches nothing and the
            // arms measured 3.5 again. Keeping Default in the mask is what makes the
            // light work today; keeping Arm in it is what makes the restriction take
            // effect the day that setting is turned on.
            var mask = DefaultRenderingLayerMask | ArmRenderingLayerMask;
            _light.renderingLayerMask = (int)mask;

            var urp = _light.GetComponent<UniversalAdditionalLightData>();
            if (urp == null)
            {
                urp = _light.gameObject.AddComponent<UniversalAdditionalLightData>();
            }

            urp.renderingLayers = new RenderingLayerMask { value = mask };
            urp.shadowRenderingLayers = new RenderingLayerMask { value = mask };

            if (_report)
            {
                var arms = view.ArmRenderers;
                var masks = new System.Text.StringBuilder();
                for (var i = 0; i < arms.Count; i++)
                {
                    masks.Append(' ').Append(arms[i].name).Append('=').Append(arms[i].renderingLayerMask);
                }

                Debug.Log(
                    "[HandFill] light mask urp=" + (uint)urp.renderingLayers
                    + " builtin=" + _light.renderingLayerMask
                    + " intensity=" + _light.intensity.ToString("0.##")
                    + " range=" + _light.range.ToString("0.##")
                    + " · arm renderer masks" + masks,
                    this);
            }
        }

        private void Start()
        {
            Apply();
        }

        private void OnDestroy()
        {
            if (_light != null)
            {
                Destroy(_light.gameObject);
                _light = null;
            }
        }

        private Transform Anchor()
        {
            if (_anchor != null)
            {
                return _anchor;
            }

            var rig = GetComponentInChildren<PlayerCameraRig>();
            if (rig != null)
            {
                if (rig.PitchPivot != null)
                {
                    _anchor = rig.PitchPivot;
                    return _anchor;
                }

                if (rig.FirstPersonCamera != null)
                {
                    _anchor = rig.FirstPersonCamera.transform;
                    return _anchor;
                }
            }

            // Falling back to the rig root keeps the fill pointing where the body faces
            // rather than where the eye looks. Worse, and still better than no hands.
            _anchor = transform;
            return _anchor;
        }

        private static void MarkArms(PlayerFirstPersonView view, uint mask)
        {
            var arms = view.ArmRenderers;
            for (var i = 0; i < arms.Count; i++)
            {
                arms[i].renderingLayerMask = mask;
            }

            var held = view.HandProps;
            for (var i = 0; i < held.Count; i++)
            {
                // What is in the hand is part of the same reading. §03 asks a player to
                // tell four states apart with no HUD, and three of the four differ only by
                // what the hands are holding.
                held[i].renderingLayerMask = mask;
            }
        }
    }
}
