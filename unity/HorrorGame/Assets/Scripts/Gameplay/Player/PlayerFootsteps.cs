#nullable enable

using System;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Session;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// One surface's worth of footstep clips. Four variants each because
    /// <c>Assets/Audio/Footsteps</c> ships four, and because a two-clip alternation is
    /// audibly a loop — which reads as a machine rather than as a person walking.
    /// </summary>
    [Serializable]
    public sealed class FootstepClipSet
    {
        [Tooltip("§12 surface these clips belong to.")]
        public FloorMaterial Material = FloorMaterial.None;

        [Tooltip("step_{surface}_player_walk_01..04")]
        public AudioClip[] Walk = Array.Empty<AudioClip>();

        [Tooltip("step_{surface}_player_run_01..04")]
        public AudioClip[] Run = Array.Empty<AudioClip>();
    }

    /// <summary>
    /// Plays the right footstep for the surface actually underfoot, when the foot actually
    /// lands.
    /// <para>
    /// §12 hands §04's Listener one job — read the monster's 위치 · 거리 · 이동 방향 from
    /// sound — and one map requirement to make it possible: each zone gets a different
    /// floor. That makes the surface lookup a rules-grade query rather than a polish task.
    /// This component therefore refuses to guess: it asks the world probe if the map layer
    /// has supplied one, then a declared <see cref="IFloorMaterialSource"/> on the geometry
    /// it is standing on, and if neither answers it plays <b>nothing</b> and says so once.
    /// Silence is a bug somebody notices; a plausible-sounding wrong surface is a bug that
    /// ships and quietly makes a whole role unreliable.
    /// </para>
    /// <para>
    /// Timing comes from <see cref="PlayerAnimatorDriver.Footfall"/>, which reads the phase
    /// of the clip that is playing. §05's multipliers mean ground speed and gait speed are
    /// never the same number twice — 0.65 backing away, 0.80 with the objective, 0.70
    /// heavy — and anything driven off a fixed interval would drift away from the visible
    /// feet, which other players are watching.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(60)]
    public sealed class PlayerFootsteps : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField]
        private PlayerMotor? _motor;

        [SerializeField]
        private PlayerAnimatorDriver? _animator;

        [Tooltip("3D source at the feet. Left empty, one is added.")]
        [SerializeField]
        private AudioSource? _source;

        [Header("Clips")]
        [SerializeField]
        private FootstepClipSet[] _clipSets = Array.Empty<FootstepClipSet>();

        [Header("Surface probe")]
        [Tooltip("Layers that count as ground.")]
        [SerializeField]
        private LayerMask _groundMask = ~0;

        [Header("Fallback cadence")]
        [Tooltip("Metres between steps at §06's 걷기, when no animator is driving footfalls. Lengthened "
            + "with speed by PlayerViewMotion.StepLengthFactor so the sound and the camera's stride agree.")]
        [SerializeField]
        private float _fallbackStrideMetres = ViewMotionTuning.StrideMetres;

        [Header("Variation")]
        [SerializeField]
        [Range(0f, 1f)]
        private float _volume = 1f;

        [Tooltip("Random pitch spread. Keep small: §12 asks the Listener to identify a surface by timbre.")]
        [SerializeField]
        [Range(0f, 0.3f)]
        private float _pitchJitter = 0.06f;

        /// <summary>
        /// Reused hit buffer for the ground probe. Eight is generous for a downward ray of
        /// one body length; the surface query runs every frame and must not allocate.
        /// </summary>
        private readonly RaycastHit[] _hits = new RaycastHit[8];

        private CharacterController? _controller;
        private Transform _selfRoot = default!;
        private float _distanceSinceStep;
        private int _lastVariant = -1;
        private FloorMaterial _surface = FloorMaterial.None;
        private bool _warnedUnknownSurface;
        private bool _subscribed;

        /// <summary>
        /// The map layer's probe. When set it is the authority — it is the same
        /// <see cref="IWorldProbe.SampleFloor"/> the monster AI and §04's Listener read, so
        /// nobody can disagree about which zone a sound came from.
        /// </summary>
        public IWorldProbe? WorldProbe { get; set; }

        /// <summary>The surface under the player as of the last step. <see cref="FloorMaterial.None"/> when unknown.</summary>
        public FloorMaterial Surface
        {
            get { return _surface; }
        }

        /// <summary>Suppresses steps without disabling the component — §08's 소음 억제기, and dead players.</summary>
        public bool Muted { get; set; }

        private void Reset()
        {
            _motor = GetComponentInParent<PlayerMotor>();
            _animator = GetComponentInParent<PlayerAnimatorDriver>();
            _source = GetComponent<AudioSource>();
        }

        private void Awake()
        {
            if (_motor == null)
            {
                _motor = GetComponentInParent<PlayerMotor>();
            }

            if (_animator == null)
            {
                _animator = GetComponentInParent<PlayerAnimatorDriver>();
            }

            _controller = GetComponentInParent<CharacterController>();
            _selfRoot = _controller != null ? _controller.transform : transform;

            if (_source == null)
            {
                _source = GetComponent<AudioSource>();
            }

            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
            }

            ConfigureSource(_source);
        }

        private void OnEnable()
        {
            if (_animator != null && !_subscribed)
            {
                _animator.Footfall += OnFootfall;
                _subscribed = true;
            }
        }

        private void OnDisable()
        {
            if (_animator != null && _subscribed)
            {
                _animator.Footfall -= OnFootfall;
            }

            _subscribed = false;
        }

        private void Update()
        {
            _surface = SampleSurface();

            if (_subscribed || _motor == null)
            {
                return;
            }

            // No animator: fall back to distance travelled. Deliberately not a timer —
            // distance keeps the cadence honest through every §05 multiplier, which a
            // fixed interval would not.
            if (!_motor.IsGrounded || _motor.GroundSpeed <= GameConstants.StillSpeedThreshold)
            {
                _distanceSinceStep = 0f;
                return;
            }

            // The pace lengthens with speed rather than the cadence simply rising:
            // at a fixed 0.8 m a 주자 sprinting at 5.6 takes seven steps a second,
            // which is a machine and not a person. The factor is shared with the
            // camera's stride so the sound lands on the bottom of the step.
            var stride = _fallbackStrideMetres * PlayerViewMotion.StepLengthFactor(_motor.GroundSpeed);

            _distanceSinceStep += _motor.GroundSpeed * Time.deltaTime;
            if (stride > 0f && _distanceSinceStep >= stride)
            {
                _distanceSinceStep -= stride;
                OnFootfall();
            }
        }

        private void OnFootfall()
        {
            if (Muted || _source == null || _motor == null || !_motor.IsGrounded)
            {
                return;
            }

            var set = FindSet(_surface);
            if (set == null)
            {
                WarnOnceAboutSurface();
                return;
            }

            // §06's 걷기/달리기 split, using the same midpoint the animation uses so the
            // clip a listener hears matches the gait they see.
            var runThreshold = (GameConstants.WalkSpeed + GameConstants.RunSpeed) * 0.5f;
            var clips = _motor.GroundSpeed >= runThreshold ? set.Run : set.Walk;

            if (clips == null || clips.Length == 0)
            {
                clips = set.Walk;
            }

            var clip = PickVariant(clips);
            if (clip == null)
            {
                return;
            }

            _source.pitch = 1f + UnityEngine.Random.Range(-_pitchJitter, _pitchJitter);
            _source.PlayOneShot(clip, _volume);
        }

        private AudioClip? PickVariant(AudioClip[]? clips)
        {
            if (clips == null || clips.Length == 0)
            {
                return null;
            }

            if (clips.Length == 1)
            {
                return clips[0];
            }

            // Never the same variant twice running. Four clips exist so a walk does not
            // sound like a loop; letting the shuffle repeat throws away a quarter of that.
            var index = UnityEngine.Random.Range(0, clips.Length);
            if (index == _lastVariant)
            {
                index = (index + 1) % clips.Length;
            }

            _lastVariant = index;
            return clips[index];
        }

        private FootstepClipSet? FindSet(FloorMaterial material)
        {
            if (material == FloorMaterial.None || _clipSets == null)
            {
                return null;
            }

            for (var i = 0; i < _clipSets.Length; i++)
            {
                if (_clipSets[i] != null && _clipSets[i].Material == material)
                {
                    return _clipSets[i];
                }
            }

            return null;
        }

        private FloorMaterial SampleSurface()
        {
            var origin = transform.position;

            var probe = WorldProbe;
            if (probe != null)
            {
                return probe.SampleFloor(origin.ToVec3());
            }

            // Probe distances are derived from the collider rather than tuned: start half a
            // body up so a step onto a kerb is not missed, and reach a full body down so a
            // player mid-fall still reports the floor they are about to land on.
            var height = _controller != null ? _controller.height : 2f;
            var start = origin + (Vector3.up * (height * 0.5f));

            // The ray begins inside the player's own capsule, and PhysX answers a ray that
            // starts inside a primitive with a hit at distance zero. A single Raycast
            // therefore reports the player standing on themselves and every floor in the
            // game comes back None — which looks exactly like an unauthored map. Collecting
            // the hits and skipping our own hierarchy is the difference between §04's
            // Listener working and the whole role silently doing nothing.
            var count = Physics.RaycastNonAlloc(
                start, Vector3.down, _hits, height * 1.5f, _groundMask, QueryTriggerInteraction.Ignore);

            var nearest = float.MaxValue;
            IFloorMaterialSource? found = null;

            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(_selfRoot))
                {
                    continue;
                }

                if (hit.distance >= nearest)
                {
                    continue;
                }

                nearest = hit.distance;
                found = hit.collider.GetComponentInParent<IFloorMaterialSource>();
            }

            return found != null ? found.FloorMaterial : FloorMaterial.None;
        }

        private void WarnOnceAboutSurface()
        {
            if (_warnedUnknownSurface)
            {
                return;
            }

            _warnedUnknownSurface = true;
            Debug.LogWarning(
                "[Player] No footstep clip set for surface '" + _surface + "'. Steps are silent here. "
                + "§12 makes the floor material a system decision — the geometry needs an "
                + "IFloorMaterialSource, or this component needs an IWorldProbe, before §04's "
                + "Listener can be trusted.",
                this);
        }

        private void ConfigureSource(AudioSource source)
        {
            // ASSETS.md: every positional clip is mono and must play on a 3D source. A
            // stereo clip on a 3D source does not warn, it simply plays at a fixed level
            // from nowhere, and §04's Listener silently loses the only channel they have.
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.playOnAwake = false;
            source.loop = false;
            source.rolloffMode = AudioRolloffMode.Logarithmic;

            // §04 sizes the Listener's world at 40 m, so a footstep that stops carrying
            // before then has removed information the role is built on.
            source.maxDistance = GameConstants.AudibleRangeMetres;

            for (var i = 0; i < _clipSets.Length; i++)
            {
                WarnIfStereo(_clipSets[i]?.Walk);
                WarnIfStereo(_clipSets[i]?.Run);
            }
        }

        private void WarnIfStereo(AudioClip[]? clips)
        {
            if (clips == null)
            {
                return;
            }

            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].channels > 1)
                {
                    Debug.LogWarning(
                        "[Player] Footstep clip '" + clips[i].name + "' is not mono. Unity does not "
                        + "spatialise a stereo clip, so it will play from everywhere at once and §04's "
                        + "Listener will hear no direction at all (ASSETS.md §1).",
                        this);
                }
            }
        }
    }
}
