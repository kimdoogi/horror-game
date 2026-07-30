#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Monster;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// Everything the monster sounds like: §06's presence bed, its breath, and the
    /// vocalisations its state machine produces.
    /// <para>
    /// <b>The whole component turns off in 정지.</b> §06's state table gives exactly one
    /// state a 소리 column of 없음, and the section spends a call-out box on why: a
    /// stopped monster makes no sound, the 청음사 loses it, and "침묵이 가장 무서운
    /// 소리다". <c>ListenerAbility</c> already returns nothing for it. If the presence
    /// bed kept humming through a standstill, the ability's blind spot would be
    /// contradicted by the speakers and §06's best weapon would be defused by an
    /// atmosphere loop. So the bed, the breath and the footsteps all stop — see
    /// <see cref="IsAudible"/>.
    /// </para>
    /// <para>
    /// The bed and the breath are diegetic, positional and occluded, which is what
    /// makes them usable rather than decorative: they attenuate with distance and
    /// muffle through walls on the same curve as the footsteps, so a player who hears
    /// the bed knows roughly where and roughly how far, exactly like §04 describes.
    /// The dread layer that is <em>not</em> diegetic — the heartbeat — lives on the
    /// player instead, because it is about the player rather than about the monster.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Monster Audio")]
    public sealed class MonsterAudio : MonoBehaviour
    {
        [Tooltip("Monster/monster_presence_bed.wav — the low bed you feel before you identify it.")]
        [SerializeField]
        private AudioClip? presenceBed;

        [Tooltip("Monster/monster_breath_loop_*.wav — close-range only.")]
        [SerializeField]
        private AudioClipBank breath = new AudioClipBank();

        [Tooltip("Monster/monster_growl_*.wav — 경계, and periodically during 추격.")]
        [SerializeField]
        private AudioClipBank growls = new AudioClipBank();

        [Tooltip("Monster/monster_roar_*.wav — the moment 추격 begins. §06: 발소리+포효.")]
        [SerializeField]
        private AudioClipBank roars = new AudioClipBank();

        [Tooltip("Monster/monster_search_*.wav — 수색.")]
        [SerializeField]
        private AudioClipBank searches = new AudioClipBank();

        [Tooltip("Monster/monster_stun_*.wav — §04's 섬광수.")]
        [SerializeField]
        private AudioClipBank stuns = new AudioClipBank();

        [Tooltip("Monster/monster_grab_*.wav — it caught someone.")]
        [SerializeField]
        private AudioClipBank grabs = new AudioClipBank();

        [Tooltip("Optional. Its Audible flag is cleared in 정지 along with everything else.")]
        [SerializeField]
        private FootstepAudio? footsteps;

        [Tooltip("Distance at which the breath loop is fully in, metres. Same room, and close.")]
        [SerializeField]
        private float breathFullDistance = 4f;

        [Tooltip("Distance at which the breath loop is inaudible, metres.")]
        [SerializeField]
        private float breathFadeDistance = 12f;

        [Tooltip("Seconds between vocalisations while chasing or searching.")]
        [SerializeField]
        private float vocalisationIntervalSeconds = 6f;

        private AudioBedVoice? _presence;
        private AudioBedVoice? _breath;
        private AudioSource? _oneShots;
        private SoundOccluder? _oneShotOccluder;

        private MonsterStateId _state = MonsterStateId.Patrol;
        private float _sinceVocalisation;
        private float _bedMatch = 1f;

        /// <summary>
        /// The monster's §06 state. Setting it fires the transition's vocalisation and,
        /// for 정지, silences everything.
        /// </summary>
        public MonsterStateId State
        {
            get { return _state; }
            set
            {
                if (_state == value)
                {
                    return;
                }

                var previous = _state;
                _state = value;
                OnStateChanged(previous, value);
            }
        }

        /// <summary>
        /// Whether the monster makes any sound at all. False exactly in
        /// <see cref="MonsterStateId.Standstill"/> — §06's 소리 column, verbatim.
        /// Matches <c>MonsterBrain.IsAudible</c> and <c>MonsterObservation.IsSilent</c>,
        /// which is what keeps the mix and <c>ListenerAbility</c> telling one story.
        /// </summary>
        public bool IsAudible => _state != MonsterStateId.Standstill;

        /// <summary>Plays the grab. §06 has no attack state, so this is an event rather than a state.</summary>
        public void PlayGrab()
        {
            PlayOneShot(grabs);
        }

        /// <summary>Plays the flash-stun reaction. §04's 섬광수 — <c>MonsterBrain.Stun</c> suspends the state rather than replacing it.</summary>
        public void PlayStun()
        {
            PlayOneShot(stuns);
        }

        private void Awake()
        {
            _bedMatch = AudioOcclusion.DecibelsToLinear(AudioTuning.MonsterBedMatchDb);

            _presence = new AudioBedVoice(
                gameObject, "monster_presence", AudioBus.Monster, true, GameConstants.ListenerHearingRange, true);
            _presence.SetClip(presenceBed);

            _breath = new AudioBedVoice(
                gameObject, "monster_breath", AudioBus.Monster, true, breathFadeDistance, true);
            _breath.SetClip(breath.Next());

            var child = new GameObject("monster_voice");
            child.transform.SetParent(transform, false);
            _oneShots = child.AddComponent<AudioSource>();
            _oneShots.playOnAwake = false;
            _oneShots.loop = false;
            GameAudio.Configure(_oneShots, AudioBus.Monster, true, GameConstants.ListenerHearingRange);

            _oneShotOccluder = child.AddComponent<SoundOccluder>();
            _oneShotOccluder.SetBus(AudioBus.Monster);
            _oneShotOccluder.BaseVolume = 1f;
        }

        private void OnDisable()
        {
            _presence?.Stop();
            _breath?.Stop();
        }

        private void Update()
        {
            if (_presence == null || _breath == null)
            {
                return;
            }

            var dt = Time.deltaTime;

            if (footsteps != null)
            {
                footsteps.Audible = IsAudible;
            }

            var audible = IsAudible;
            var ears = GameAudio.ListenerTransform;
            var distance = ears != null ? Flat(transform.position - ears.position) : float.PositiveInfinity;

            // The bed is crossfaded by danger, not just by distance: a patrolling
            // monster ten metres away and a chasing one ten metres away are not the
            // same event, and §06's whole escalation is in the difference.
            var danger = DangerSense.Evaluate(_state, distance);

            _presence.Target = audible ? danger * _bedMatch : 0f;

            var closeness = 1f - Mathf.Clamp01(
                Mathf.InverseLerp(breathFullDistance, breathFadeDistance, distance));
            _breath.Target = audible ? closeness * _bedMatch : 0f;

            _presence.Tick(dt, AudioTuning.MonsterBedCrossfadeSeconds);
            _breath.Tick(dt, AudioTuning.MonsterBedCrossfadeSeconds);

            TickVocalisation(dt, audible);
        }

        private void TickVocalisation(float deltaSeconds, bool audible)
        {
            _sinceVocalisation += deltaSeconds;

            if (!audible || _sinceVocalisation < vocalisationIntervalSeconds)
            {
                return;
            }

            // Reset before the switch, so an unauthored bank costs one silent interval
            // rather than a retry every frame for the rest of the match.
            _sinceVocalisation = 0f;

            switch (_state)
            {
                case MonsterStateId.Chase:
                    // §06: 추격's 소리 column is 발소리+포효. The roar is not only the
                    // transition — while it is on you, it keeps telling you so.
                    PlayOneShot(growls);
                    break;

                case MonsterStateId.Search:
                    PlayOneShot(searches);
                    break;

                default:
                    // 순찰 and 경계 carry themselves on footsteps alone. Adding an idle
                    // vocalisation would give the Listener a second, easier channel and
                    // make §12's floor materials optional.
                    break;
            }
        }

        private void OnStateChanged(MonsterStateId previous, MonsterStateId current)
        {
            _sinceVocalisation = 0f;

            switch (current)
            {
                case MonsterStateId.Chase:
                    PlayOneShot(roars);
                    break;

                case MonsterStateId.Alert:
                    // Only from a calmer state. 추격 → 경계 does not happen in §06's
                    // table, but 수색 → 경계 does, and growling every time it re-hears
                    // the same footstep would turn a hunt into a noise loop.
                    if (previous == MonsterStateId.Patrol || previous == MonsterStateId.Standstill)
                    {
                        PlayOneShot(growls);
                    }

                    break;

                case MonsterStateId.Search:
                    PlayOneShot(searches);
                    break;
            }
        }

        private void PlayOneShot(AudioClipBank bank)
        {
            if (_oneShots == null || bank.IsEmpty)
            {
                return;
            }

            var clip = bank.Next();
            if (clip == null)
            {
                return;
            }

            _sinceVocalisation = 0f;
            _oneShots.PlayOneShot(clip, bank.Gain);
        }

        private static float Flat(Vector3 delta)
        {
            delta.y = 0f;
            return delta.magnitude;
        }
    }
}
