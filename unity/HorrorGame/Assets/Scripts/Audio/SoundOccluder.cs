#nullable enable

using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// Muffles and attenuates a source when geometry stands between it and the ears.
    /// <para>
    /// Attach next to any positional <see cref="AudioSource"/>. It adds and drives an
    /// <see cref="AudioLowPassFilter"/>, and scales the source's volume by the
    /// occlusion gain — see <see cref="AudioOcclusion"/> for the curve and for why the
    /// filter corner is clamped rather than swept to zero.
    /// </para>
    /// <para>
    /// <b>Sound is not blocked, only filtered.</b> That is a design constraint, not an
    /// approximation: <c>ListenerAbility</c> never asks
    /// <c>IWorldProbe.HasLineOfSight</c> because §04 makes hearing through walls the
    /// entire point of the 청음사, and the mix has to agree with the rule. An occlusion
    /// implementation that silenced blocked sources would make the HUD and the ears
    /// contradict each other in the one place the design cannot afford it.
    /// </para>
    /// <para>
    /// The probe is staggered — <see cref="AudioTuning.OcclusionProbeIntervalSeconds"/>
    /// apart, with a random phase per instance — so a room full of emitters does not
    /// spike on the same frame. Between probes the result is interpolated, which is
    /// also what stops a doorway from chattering.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("HorrorGame/Audio/Sound Occluder")]
    public sealed class SoundOccluder : MonoBehaviour
    {
        private static readonly RaycastHit[] HitScratch = new RaycastHit[8];

        [Tooltip("Which family this source belongs to. Decides how hard it may be filtered.")]
        [SerializeField]
        private AudioBus bus = AudioBus.Items;

        [Tooltip("Base volume of this source before occlusion and bus gain, 0..1.")]
        [SerializeField, Range(0f, 1f)]
        private float baseVolume = 1f;

        private AudioSource? _source;
        private AudioLowPassFilter? _filter;
        private OcclusionProfile _profile;

        private float _occlusion;
        private float _occlusionTarget;
        private float _nextProbeTime;

        /// <summary>Current occlusion, 0 (clear) to 1 (fully blocked). Smoothed.</summary>
        public float Occlusion => _occlusion;

        /// <summary>Base volume before occlusion and bus gain, 0..1. The owner sets this; the occluder scales it.</summary>
        public float BaseVolume
        {
            get { return baseVolume; }
            set { baseVolume = Mathf.Clamp01(value); }
        }

        /// <summary>
        /// Re-points the occluder at a different family. Used by
        /// <see cref="ProximityVoiceAudio"/>, which discovers the source it is
        /// occluding at runtime.
        /// </summary>
        public void SetBus(AudioBus value)
        {
            bus = value;
            _profile = OcclusionProfile.For(bus);
        }

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _filter = GetComponent<AudioLowPassFilter>();
            if (_filter == null)
            {
                _filter = gameObject.AddComponent<AudioLowPassFilter>();
            }

            _filter.cutoffFrequency = AudioTuning.OcclusionOpenCutoffHz;
            _profile = OcclusionProfile.For(bus);

            // A random phase, not a random interval: every emitter still probes at the
            // same rate, they simply do not all pick the same frame to do it on.
            _nextProbeTime = Time.time + Random.Range(0f, AudioTuning.OcclusionProbeIntervalSeconds);
        }

        private void LateUpdate()
        {
            if (_source == null || _filter == null)
            {
                return;
            }

            var ears = GameAudio.ListenerTransform;
            if (ears == null)
            {
                // No listener yet — a loading scene, or the local player has not
                // spawned. Stay open rather than muffled: silence is a worse default
                // than a missing effect.
                _occlusionTarget = 0f;
            }
            else if (Time.time >= _nextProbeTime)
            {
                _nextProbeTime = Time.time + AudioTuning.OcclusionProbeIntervalSeconds;
                _occlusionTarget = Probe(ears.position);
            }

            _occlusion = GameAudio.Approach(
                _occlusion, _occlusionTarget, Time.deltaTime, AudioTuning.OcclusionSmoothingSeconds);

            var cutoff = Mathf.Min(
                AudioOcclusion.CutoffHz(_profile, _occlusion),
                GameAudio.CutoffCeilingHz(bus));

            _filter.cutoffFrequency = cutoff;

            GameAudio.ApplyVolume(_source, bus, baseVolume * AudioOcclusion.Gain(_profile, _occlusion));
        }

        /// <summary>
        /// Casts the direct ray plus a ring of offsets and turns the result into an
        /// occlusion fraction.
        /// <para>
        /// The offsets are spread on the plane facing the listener rather than around
        /// the source in world space, so a doorway reads as a doorway from every angle
        /// — §12 makes 구역 간 진입점 the map's basic connective tissue, and there are
        /// two or three of them between any two zones.
        /// </para>
        /// </summary>
        private float Probe(Vector3 earPosition)
        {
            var origin = transform.position;
            var toEar = earPosition - origin;
            var distance = toEar.magnitude;
            if (distance < 0.05f)
            {
                return 0f;
            }

            var direction = toEar / distance;
            var mask = GameAudio.OccluderMask;
            var directBlocked = Blocked(origin, direction, distance, mask);

            var offsets = Mathf.Max(0, AudioTuning.OcclusionRayCount - 1);
            if (offsets == 0)
            {
                return AudioOcclusion.FromRays(directBlocked, 0, 0);
            }

            var right = Vector3.Cross(direction, Vector3.up);
            if (right.sqrMagnitude < 1e-4f)
            {
                right = Vector3.Cross(direction, Vector3.forward);
            }

            right.Normalize();
            var up = Vector3.Cross(right, direction).normalized;

            var blocked = 0;
            for (var i = 0; i < offsets; i++)
            {
                var angle = (Mathf.PI * 2f * i) / offsets;
                var offset = ((right * Mathf.Cos(angle)) + (up * Mathf.Sin(angle)))
                             * AudioTuning.OcclusionRaySpreadRadius;

                var from = origin + offset;
                var to = earPosition + offset;
                var delta = to - from;
                var length = delta.magnitude;
                if (length < 0.05f)
                {
                    continue;
                }

                if (Blocked(from, delta / length, length, mask))
                {
                    blocked++;
                }
            }

            return AudioOcclusion.FromRays(directBlocked, blocked, offsets);
        }

        /// <summary>
        /// True when something opaque is on the segment.
        /// <para>
        /// Triggers are ignored — a zone volume, a clue trigger or an aggro region is
        /// not a wall — and the emitter's own colliders are skipped, because a monster
        /// large enough to have a body would otherwise permanently occlude its own
        /// footsteps.
        /// </para>
        /// </summary>
        private bool Blocked(Vector3 origin, Vector3 direction, float distance, LayerMask mask)
        {
            var count = Physics.RaycastNonAlloc(
                origin, direction, HitScratch, distance, mask, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                var hit = HitScratch[i];
                if (hit.collider == null)
                {
                    continue;
                }

                if (hit.collider.transform.IsChildOf(transform) || transform.IsChildOf(hit.collider.transform))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
