#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Map;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// Plays §12's floor alphabet under a moving character.
    /// <para>
    /// This is the single most load-bearing sound in the project. §04 gives the 청음사
    /// 소리로 괴물의 위치 · 거리 · 이동 방향을 파악 and nothing else; §12 answers "how"
    /// with 구역별로 바닥 재질이 달라야 하고, so a footstep is not an effect that
    /// accompanies movement, it is the channel one of five roles plays the game
    /// through. Everything here follows from that:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// The surface comes from <see cref="FloorSurfaces"/>, which is the same answer the
    /// rules get. A mix that disagreed with <c>ListenerAbility</c> would be worse than
    /// no cue at all.
    /// </description></item>
    /// <item><description>
    /// Cadence is driven by distance travelled rather than by an animation event, so
    /// §05's directional multipliers and §08's weight bands are audible without this
    /// component knowing either exists — a loaded player's steps slow down because the
    /// player slows down.
    /// </description></item>
    /// <item><description>
    /// Pitch jitter is small (<see cref="AudioTuning.FootstepPitchJitter"/>). Wider
    /// variation would hide the repetition better and shift the spectral centroid the
    /// Listener identifies the surface by, which is the one thing that must not move.
    /// </description></item>
    /// </list>
    /// <para>
    /// Silence is a first-class state. §06's 정지 makes no sound at all, and the
    /// monster's driver expresses that by not calling <see cref="Step"/> — there is no
    /// quiet footstep for a stopped monster, because "침묵이 가장 무서운 소리다".
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("HorrorGame/Audio/Footstep Audio")]
    public sealed class FootstepAudio : MonoBehaviour
    {
        [Tooltip("§12's five surfaces as clips.")]
        [SerializeField]
        private SurfaceAudioLibrary? library;

        [Tooltip("Whether this is a player or the monster. Chooses the clip set and the bus.")]
        [SerializeField]
        private FootstepActor defaultActor = FootstepActor.PlayerWalk;

        [Tooltip("Above this speed a player's steps switch to the run set, m/s. " +
                 "Defaults to halfway between GameConstants.WalkSpeed and RunSpeed.")]
        [SerializeField]
        private float runThresholdSpeed = (GameConstants.WalkSpeed + GameConstants.RunSpeed) * 0.5f;

        [Tooltip("Optional. Reports this character's noise so §04's self-blinding works.")]
        [SerializeField]
        private NoiseMeter? noiseMeter;

        private AudioSource? _source;
        private SoundOccluder? _occluder;

        private Vector3 _lastPosition;
        private float _travelSinceStep;
        private float _timeSinceStep;
        private float _speed;
        private bool _hasPosition;

        /// <summary>
        /// Whether steps play at all. The monster's driver clears this in §06's 정지;
        /// a corpse or a §09 ghost clears it permanently.
        /// </summary>
        public bool Audible { get; set; } = true;

        /// <summary>The surface the last step was played from. §12. Useful to a HUD and to tests.</summary>
        public FloorMaterial CurrentFloor { get; private set; } = FloorMaterial.None;

        /// <summary>Measured horizontal speed, m/s, from frame-to-frame movement.</summary>
        public float Speed => _speed;

        /// <summary>Which clip set is being used right now — walk, run or monster.</summary>
        public FootstepActor Actor
        {
            get
            {
                if (defaultActor == FootstepActor.MonsterStep)
                {
                    return FootstepActor.MonsterStep;
                }

                return _speed >= runThresholdSpeed ? FootstepActor.PlayerRun : FootstepActor.PlayerWalk;
            }
        }

        /// <summary>
        /// Plays one step immediately, whatever the cadence says. For an animation
        /// event or a scripted moment; the automatic cadence covers ordinary movement.
        /// </summary>
        public void Step()
        {
            PlayStep(Actor);
        }

        private void Awake()
        {
            _source = GetComponent<AudioSource>();

            // Added rather than required, because occlusion is not optional here: §04
            // makes hearing the monster through a wall the 청음사's entire ability, and
            // an unfiltered footstep would make every room in §12's map sound like the
            // one the player is standing in.
            _occluder = GetComponent<SoundOccluder>();
            if (_occluder == null)
            {
                _occluder = gameObject.AddComponent<SoundOccluder>();
            }

            _source.playOnAwake = false;
            _source.loop = false;

            GameAudio.Configure(_source, AudioBus.Footsteps, true, GameConstants.ListenerHearingRange);

            _occluder.SetBus(AudioBus.Footsteps);
            _occluder.BaseVolume = 1f;
        }

        private void OnEnable()
        {
            _hasPosition = false;
            _travelSinceStep = 0f;
            _timeSinceStep = AudioTuning.FootstepMinIntervalSeconds;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            _timeSinceStep += dt;

            var position = transform.position;
            if (!_hasPosition)
            {
                _lastPosition = position;
                _hasPosition = true;
                return;
            }

            var delta = position - _lastPosition;
            _lastPosition = position;

            // Horizontal only: §04's own reading measures distance flat so a stairwell
            // does not read as extra range, and a step is a step whichever floor it
            // lands on.
            delta.y = 0f;
            var travelled = delta.magnitude;
            _speed = dt > 0f ? travelled / dt : 0f;

            if (noiseMeter != null)
            {
                noiseMeter.ReportMovementSpeed(_speed);
            }

            if (!Audible || travelled <= 0f)
            {
                return;
            }

            _travelSinceStep += travelled;

            if (_travelSinceStep < AudioTuning.FootstepStrideMetres)
            {
                return;
            }

            if (_timeSinceStep < AudioTuning.FootstepMinIntervalSeconds)
            {
                // A frame spike or a teleport banked several strides at once. Spend one
                // and drop the rest: a burst of four footsteps is a worse artefact than
                // a missing one, and §12's alphabet is read one step at a time.
                _travelSinceStep = 0f;
                return;
            }

            _travelSinceStep = 0f;
            PlayStep(Actor);
        }

        private void PlayStep(FootstepActor actor)
        {
            if (_source == null || library == null)
            {
                return;
            }

            CurrentFloor = FloorSurfaces.Sample(transform.position);

            var bank = library.Steps(CurrentFloor, actor);
            if (bank == null || bank.IsEmpty)
            {
                // An unauthored surface plays nothing. §12 fails a map that has one, so
                // this is a wiring bug being made audible rather than papered over with
                // the wrong floor.
                return;
            }

            var clip = bank.Next();
            if (clip == null)
            {
                return;
            }

            _timeSinceStep = 0f;

            _source.pitch = 1f + Random.Range(-AudioTuning.FootstepPitchJitter, AudioTuning.FootstepPitchJitter);
            var level = bank.Gain * (1f + Random.Range(-AudioTuning.FootstepVolumeJitter, AudioTuning.FootstepVolumeJitter));

            _source.PlayOneShot(clip, Mathf.Clamp(level, 0f, 2f));
        }
    }
}
