#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// Makes the monster <em>resolve</em> out of the dark instead of switching on.
    /// <para>
    /// §03's beam is a spot with a hard end: <see cref="GameConstants.FlashlightRange"/>
    /// is 12 m and URP's attenuation reaches zero there. Without this, the creature
    /// crossing that boundary goes from an unlit shape to a fully detailed one in the
    /// width of one step, which is the worst possible reveal — the player's eye is
    /// drawn by the change rather than by the thing.
    /// </para>
    /// <para>
    /// The fix is deliberately <b>not</b> a brightness ramp. Fading the albedo up as it
    /// approaches would add a second switch on top of the beam's own and make the pop
    /// worse; it would also undo the albedo calibration the texture pipeline exists to
    /// enforce. What ramps instead is <em>detail</em>: normal strength, occlusion depth
    /// and smoothness. Far away the creature is a matte, clean mass — a silhouette,
    /// which is the information §12's corridors need the player to act on. Close in it
    /// is wet, deeply shadowed and pitted. The outline never changes, so nothing pops;
    /// the surface arrives.
    /// </para>
    /// <para>
    /// The secondary reason is not aesthetic. A carapace at smoothness 0.74 covering
    /// twelve pixels at 15 m aliases: the specular lobe lands on some pixels and not
    /// others and crawls frame to frame, which reads as sparkle on the one thing in
    /// the room that must read as solid. Rolling smoothness off with distance is the
    /// standard cure and it happens to be exactly the reveal we want.
    /// </para>
    /// <para>
    /// It also drives the one thing that makes the creature visible at all past the beam:
    /// the Fresnel rim in <c>Assets/Shaders/Monster/MonsterSkin.shader</c>. See
    /// <see cref="RimAt"/> for why that ramps <em>up</em> with distance and why it
    /// replaced the flat ambient fill that used to live here.
    /// </para>
    /// <para>
    /// Presentation only. Nothing here is read by <c>MonsterBrain</c>, and a scene with
    /// no camera or no renderer degrades to the material's authored values.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterBeamResolve : MonoBehaviour
    {
        private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int OcclusionStrengthId = Shader.PropertyToID("_OcclusionStrength");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");

        [SerializeField]
        [Tooltip("Left empty, every renderer under this object is driven.")]
        private Renderer[] _renderers = System.Array.Empty<Renderer>();

        [Header("Resolve curve")]
        [SerializeField]
        [Tooltip("Fraction of FlashlightRange at which detail is fully resolved. Presentation, not a §-number.")]
        [Range(0.05f, 1f)]
        private float _fullDetailFraction = 0.25f;

        [SerializeField]
        [Tooltip("Detail multiplier at and beyond the beam's end. 0 = flat matte silhouette.")]
        [Range(0f, 1f)]
        private float _distantDetail = 0.45f;

        [SerializeField]
        [Tooltip("Smoothness multiplier at the far end of the ramp, as a fraction of the near value.")]
        [Range(0.1f, 1f)]
        private float _distantSmoothness = 0.7f;

        [Header("Material values at full detail")]
        [SerializeField]
        [Tooltip("Normal strength up close. Above 1 on purpose — see MonsterSkin.")]
        private float _nearBumpScale = 1.6f;

        [SerializeField]
        private float _nearOcclusionStrength = 1f;

        [SerializeField]
        [Tooltip("Multiplies the metallic/smoothness map's authored smoothness.")]
        private float _nearSmoothness = 1f;

        [Header("Beyond the beam")]
        [SerializeField]
        [Tooltip("Rim strength at ObserverRange. Measured against a dark wall — see RimAt.")]
        [Range(0f, 1f)]
        private float _rimStrength = 0.09f;

        private MaterialPropertyBlock? _block;
        private float _applied = -1f;
        private float _fill;

        /// <summary>
        /// How resolved the creature currently is, 0 (a bare silhouette) to 1 (fully
        /// surfaced). Exposed so a capture tool can assert the reveal rather than
        /// eyeball it.
        /// </summary>
        public float Resolve { get; private set; }

        /// <summary>
        /// Drives the reveal from one observer.
        /// <para>
        /// Public and camera-argued rather than self-driving so the batch capture rig
        /// exercises this exact code path. A shot taken with the resolve left at its
        /// authored value would be a photograph of a monster that is not in the game.
        /// </para>
        /// </summary>
        /// <param name="observer">
        /// The camera whose flashlight matters. §05 mounts the beam on the camera —
        /// "손전등이 포인터가 된다" — so camera distance is the beam's distance.
        /// </param>
        public void Apply(Camera? observer)
        {
            if (observer == null)
            {
                return;
            }

            var distance = Vector3.Distance(observer.transform.position, CentreOfMass());
            Apply(DetailAt(distance), RimAt(distance));
        }

        /// <summary>
        /// Drives the reveal directly: <paramref name="resolve"/> is surface detail,
        /// <paramref name="fill"/> is the rim ramp. Both are clamped to 0–1.
        /// </summary>
        public void Apply(float resolve, float fill = 0f)
        {
            Resolve = Mathf.Clamp01(resolve);
            var wantedFill = Mathf.Clamp01(fill);

            // A MaterialPropertyBlock push is cheap but not free, and the monster is on
            // screen for whole minutes at a time. A hundredth of either ramp is far below
            // what any of these properties shows.
            if (Mathf.Abs(Resolve - _applied) < 0.01f && Mathf.Abs(wantedFill - _fill) < 0.01f)
            {
                return;
            }

            _applied = Resolve;
            _fill = wantedFill;
            Push();
        }

        /// <summary>
        /// How much rim the creature carries at a viewing distance, 0–1 of
        /// <see cref="_rimStrength"/>.
        /// <para>
        /// This is the one place the creature is given light that is not in the world, and
        /// it is here because the design requires it rather than because it flatters the
        /// model.
        /// </para>
        /// <para>
        /// §04's 관측자 reads the monster's gaze — "누가 표적인지" — from
        /// <see cref="GameConstants.ObserverRange"/>, 15 m. §03's beam reaches
        /// <see cref="GameConstants.FlashlightRange"/>, 12 m. The three metres between
        /// those two numbers are a design requirement to see the creature where no light
        /// of the player's can reach it, and §11 calls the Observer the one role that
        /// cannot be replaced by an item — so "you cannot see it out there" is not an
        /// available answer. §12 asks for 1~2 관측 지점 per zone that give exactly that
        /// sight, and says without them 관측자는 죽으러 가야 한다.
        /// </para>
        /// <para>
        /// <b>Why a rim and not the ambient fill this replaced.</b> The fill raised the
        /// whole body's emission, and the measurement says plainly what that bought: at
        /// 15 m against a dark wall the body came back 0.0128 out of 1.0 <em>brighter</em>
        /// than the wall — three code values, which is invisible — and at 8 m, 0.0040
        /// darker, which is also invisible. Lifting a body that is hazed by the same fog
        /// and lit by the same ambient as the wall behind it cannot separate the two,
        /// because the term being added is small compared to what they already share. A
        /// rim puts its light where the wall has none: on the outline.
        /// </para>
        /// <para>
        /// It ramps up rather than down with distance, which is backwards for a physical
        /// light and correct for this one. Up close §03's own beam is doing the work and a
        /// rim would be a halo on a creature standing in front of the player; past the
        /// beam there is nothing else, and that is the range §04 is defined at.
        /// </para>
        /// <para>
        /// The ramp starts at a quarter of the beam's range and not at half, and the
        /// correction came from the measurement. A spot light falls off with the square of
        /// distance, so §03's <em>useful</em> reach is a long way short of its nominal 12 m:
        /// at half the range the beam has an eighth of the illuminance it has at 3 m. A
        /// hand-over starting at 6 m therefore left a hole at 8 m where the beam had
        /// already stopped delivering and the rim had barely started, and 8 m measured as
        /// the <em>worst</em> distance in the set — worse than 20 m — at a contrast of
        /// 0.0140 against a floor of 0.015. Starting at 3 m closes it, and the rim at 3 m
        /// is still exactly zero, so nothing was traded for it.
        /// </para>
        /// </summary>
        public float RimAt(float distanceMetres)
        {
            var beam = GameConstants.FlashlightRange;
            var observer = GameConstants.ObserverRange;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(beam * 0.25f, observer, distanceMetres));
        }

        /// <summary>
        /// Detail fraction for a viewing distance, in metres.
        /// <para>
        /// Anchored to <see cref="GameConstants.FlashlightRange"/> rather than to a
        /// tuned distance of its own: the reveal exists because the beam ends, so if
        /// §03's range moves the reveal has to move with it or the ramp lands somewhere
        /// the light does not.
        /// </para>
        /// </summary>
        public float DetailAt(float distanceMetres)
        {
            var far = GameConstants.FlashlightRange;
            var near = far * _fullDetailFraction;
            var t = Mathf.InverseLerp(far, near, distanceMetres);

            // Smoothstep, not linear: a linear ramp has a visible corner at each end,
            // and a corner in a reveal is a small pop, which is the thing being fixed.
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(_distantDetail, 1f, t);
        }

        private void Awake()
        {
            if (_renderers == null || _renderers.Length == 0)
            {
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            }
        }

        private void LateUpdate()
        {
            Apply(Camera.main);
        }

        /// <summary>
        /// Roughly the chest, in world space.
        /// <para>
        /// Renderer bounds rather than the transform origin, which sits on the floor:
        /// measuring the reveal from the feet makes a creature leaning over a player
        /// read as further away than the head they are looking at.
        /// </para>
        /// </summary>
        private Vector3 CentreOfMass()
        {
            var renderers = ResolveRenderers();
            if (renderers.Count == 0)
            {
                return transform.position;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Count; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds.center;
        }

        private void Push()
        {
            var renderers = ResolveRenderers();
            if (renderers.Count == 0)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();

            for (var i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                // Per material slot, not per renderer, and that is not a detail.
                // MonsterAcquireTell writes the maw's emission through a per-slot block,
                // and a per-slot block replaces the renderer-level one for that slot
                // outright — so a renderer-level write here would be silently discarded
                // on the maw the moment the creature acquired someone. Both components
                // read the slot's existing block and add to it, so neither can erase the
                // other whatever order they run in.
                var materials = renderer.sharedMaterials;
                for (var slot = 0; slot < materials.Length; slot++)
                {
                    renderer.GetPropertyBlock(_block, slot);
                    _block.SetFloat(BumpScaleId, _nearBumpScale * Resolve);
                    _block.SetFloat(OcclusionStrengthId, _nearOcclusionStrength * Resolve);

                    // Every slot, including the maw and the eyes, and there is no longer
                    // an exclusion list because the components no longer share a channel.
                    // This writes _RimStrength; MonsterAcquireTell writes _EmissionColor
                    // on the maw and the eye material carries its own authored emission
                    // that nothing writes at all. Ownership is by property, which cannot
                    // be got wrong by adding a material.
                    _block.SetFloat(RimStrengthId, _rimStrength * _fill);

                    // Smoothness barely rolls off, and that is a correction. The first
                    // version dropped it to 0.45 of the authored value at range to stop
                    // the carapace's specular aliasing on a twelve-pixel creature, and it
                    // worked — it also took away the wet highlight that was most of what
                    // made the creature visible at 8 m at all. Contrast against the
                    // corridor measured 0.003 there. The highlight is the signal; the
                    // aliasing is the price, and 0.7 is where both are tolerable.
                    _block.SetFloat(SmoothnessId,
                        Mathf.Lerp(_nearSmoothness * _distantSmoothness, _nearSmoothness, Resolve));
                    renderer.SetPropertyBlock(_block, slot);
                }
            }
        }

        private List<Renderer> ResolveRenderers()
        {
            if (_renderers == null || _renderers.Length == 0)
            {
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            }

            var live = new List<Renderer>(_renderers.Length);
            for (var i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    live.Add(_renderers[i]);
                }
            }

            return live;
        }
    }
}
