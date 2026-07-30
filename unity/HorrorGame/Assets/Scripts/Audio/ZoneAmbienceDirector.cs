#nullable enable

using HorrorGame.Core.Map;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// Crossfades the room tone as the local player moves between §12's zones.
    /// <para>
    /// The bed is keyed on floor material rather than on zone id, and that is not a
    /// shortcut — §12 requires "구역별로 바닥 재질이 달라야" precisely so that surface
    /// and place are the same fact. Keying the tone on anything else would let a room
    /// sound like 나무 while its footsteps said 자갈, and the 청음사 would be reading a
    /// map that contradicts itself.
    /// </para>
    /// <para>
    /// Crossfaded rather than switched. §12 wants "재질 경계를 명확히" and a
    /// <see cref="AudioTuning.ZoneAmbienceCrossfadeSeconds"/> fade still delivers
    /// that — a second and a half is a boundary you can hear yourself cross — while a
    /// hard cut at a trigger volume reads as a bug and, worse, tells the player the
    /// exact metre the zone changes at, which is information §12 never promised.
    /// </para>
    /// <para>
    /// Also scatters §12's distant creaks and drips. They are not decoration: §06's
    /// 정지 makes silence the monster's weapon, and silence is only frightening if the
    /// room is otherwise alive. A basement with no incidental noise sounds switched
    /// off, and a player stops listening to it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Zone Ambience Director")]
    public sealed class ZoneAmbienceDirector : MonoBehaviour
    {
        [Tooltip("§12's five surfaces, including each one's room tone.")]
        [SerializeField]
        private SurfaceAudioLibrary? library;

        [Tooltip("amb_surface_vehicle_loop — played above ground, where §07 says the " +
                 "team can read the clock and §08 puts the shop.")]
        [SerializeField]
        private AudioClip? surfaceBed;

        [Tooltip("sfx_creak_distant_* and sfx_water_drip_*. Scattered underground only.")]
        [SerializeField]
        private AudioClipBank incidental = new AudioClipBank();

        [Tooltip("Shortest gap between two incidental noises, seconds.")]
        [SerializeField]
        private float incidentalMinSeconds = 9f;

        [Tooltip("Longest gap between two incidental noises, seconds.")]
        [SerializeField]
        private float incidentalMaxSeconds = 26f;

        [Tooltip("How far from the listener an incidental noise is placed, metres.")]
        [SerializeField]
        private float incidentalRadius = 12f;

        private CrossfadeBed? _bed;
        private AudioSource? _incidentalSource;
        private float _nextIncidental;

        /// <summary>
        /// Whether the team is above ground. §03's round trip — the surface is the
        /// safe half of the loop, and it must not sound like the basement.
        /// </summary>
        public bool IsOnSurface { get; set; }

        /// <summary>The surface whose tone is currently playing. §12.</summary>
        public FloorMaterial CurrentFloor { get; private set; } = FloorMaterial.None;

        private void Awake()
        {
            _bed = new CrossfadeBed(gameObject, "zone_ambience", AudioBus.Ambience, false);

            var child = new GameObject("zone_incidental");
            child.transform.SetParent(transform, false);
            _incidentalSource = child.AddComponent<AudioSource>();
            _incidentalSource.playOnAwake = false;
            _incidentalSource.loop = false;
            GameAudio.Configure(_incidentalSource, AudioBus.Ambience, true, AudioTuning.DefaultWorldAudibleRange);

            ScheduleIncidental();
        }

        private void OnDisable()
        {
            _bed?.Stop();
        }

        private void Update()
        {
            if (_bed == null)
            {
                return;
            }

            var dt = Time.deltaTime;

            _bed.FadeTo(SelectBed());
            _bed.Tick(dt, AudioTuning.ZoneAmbienceCrossfadeSeconds);

            TickIncidental(dt);
        }

        private AudioClip? SelectBed()
        {
            if (IsOnSurface)
            {
                CurrentFloor = FloorMaterial.None;
                return surfaceBed;
            }

            var ears = GameAudio.ListenerTransform;
            if (ears == null || library == null)
            {
                return null;
            }

            CurrentFloor = FloorSurfaces.Sample(ears.position);
            return library.Ambience(CurrentFloor);
        }

        private void TickIncidental(float deltaSeconds)
        {
            if (_incidentalSource == null || incidental.IsEmpty || IsOnSurface)
            {
                return;
            }

            _nextIncidental -= deltaSeconds;
            if (_nextIncidental > 0f)
            {
                return;
            }

            ScheduleIncidental();

            var ears = GameAudio.ListenerTransform;
            if (ears == null)
            {
                return;
            }

            var clip = incidental.Next();
            if (clip == null)
            {
                return;
            }

            // Placed on a ring around the player rather than at a fixed emitter, so it
            // arrives from somewhere and cannot be learned. §05 has the Listener
            // turning their body to place a sound; a creak that always came from the
            // same corner would teach them to stop bothering.
            var angle = Random.Range(0f, Mathf.PI * 2f);
            var offset = new Vector3(Mathf.Cos(angle), Random.Range(-0.3f, 0.6f), Mathf.Sin(angle)) * incidentalRadius;
            _incidentalSource.transform.position = ears.position + offset;

            GameAudio.ApplyVolume(_incidentalSource, AudioBus.Ambience, incidental.Gain);
            _incidentalSource.PlayOneShot(clip, 1f);
        }

        private void ScheduleIncidental()
        {
            var min = Mathf.Max(1f, incidentalMinSeconds);
            var max = Mathf.Max(min + 1f, incidentalMaxSeconds);
            _nextIncidental = Random.Range(min, max);
        }
    }
}
