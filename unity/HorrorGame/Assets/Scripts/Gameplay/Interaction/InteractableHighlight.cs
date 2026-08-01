#nullable enable

using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// Lights a prop while the crosshair is on it. §03 · §10.
    /// <para>
    /// <b>Why the prop and not the HUD.</b> §03 refuses a map and refuses markers, so
    /// there is no screen-space affordance available; the only thing left is the object
    /// itself. And the object needs it: §08's efficient row is a 7 cm pocket watch and
    /// a 2.2 cm ring, deliberately "missable", lying on §12's floors in a 12 m cone.
    /// Without a targeting cue the difference between "the key is broken" and "I am
    /// aiming two centimetres to the left" is invisible, which is exactly how the
    /// missing prompt was reported: "there seems to be no key".
    /// </para>
    /// <para>
    /// <b>A property block, not a material swap.</b> Every prop material is a shared
    /// asset, so writing to it would light every silver spoon in the building at once,
    /// and instancing one per prop would allocate a material for each of the 38 objects
    /// a match places. <c>MaterialPropertyBlock</c> writes per-renderer with no
    /// allocation and no asset touched. It cannot enable shader keywords, which is why
    /// <c>PropMaterials</c> compiles <c>_EMISSION</c> into every prop material with a
    /// black colour — the keyword is the switch, this is the dial.
    /// </para>
    /// <para>
    /// The colour is a warm low-intensity add rather than an outline. §03 makes
    /// darkness the lock on progress and ART.md holds a &lt;0.5% blown-pixel ceiling; a
    /// rim-lit prop at 1 m under a torch is the case that breaks it. This raises the
    /// object just off its unlit value, which is enough to separate it from a floor
    /// that is being lit by the same beam. The value is photographed, not guessed — the
    /// first attempt was four times this and flattened every prop into a
    /// featureless tan cut-out with no shading left on it at all; this one lifts a
    /// 2.2 cm 반지 off a lit floor and still leaves the 금고's dial readable.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractableHighlight : MonoBehaviour
    {
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private Renderer[]? _renderers;
        private MaterialPropertyBlock? _block;
        private bool _targeted;

        /// <summary>Whether the crosshair is on this prop right now.</summary>
        public bool IsTargeted
        {
            get { return _targeted; }
        }

        /// <summary>Turns the targeting glow on or off. Cheap enough to call every frame.</summary>
        public void SetTargeted(bool on)
        {
            if (_targeted == on && _renderers != null)
            {
                return;
            }

            _targeted = on;
            Apply();
        }

        /// <summary>Re-reads the renderers after the model underneath was swapped.</summary>
        public void Rebind()
        {
            _renderers = null;
            Apply();
        }

        private void Awake()
        {
            Apply();
        }

        private void Apply()
        {
            if (_renderers == null)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
            }

            _block ??= new MaterialPropertyBlock();

            var colour = _targeted ? TargetedEmission : Color.black;
            for (var i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_block);
                _block.SetColor(EmissionColor, colour);
                renderer.SetPropertyBlock(_block);
            }
        }

        /// <summary>
        /// How hard a targeted prop glows. Presentation, not balance — it changes
        /// nothing a rule reads, and §03's read still needs the beam held on the mark
        /// for <c>GameConstants.ClueReadSeconds</c>. Warm, because every practical light
        /// in this building is a tungsten filament and a cold highlight reads as a
        /// user-interface element pasted onto the world.
        /// </summary>
        private static readonly Color TargetedEmission = new Color(0.10f, 0.073f, 0.035f, 1f);
    }
}
