#nullable enable

using System;
using HorrorGame.Core.Monster;
using UnityEngine;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// One state cue: the clips for a §06 state and whether it keeps sounding.
    /// <para>
    /// A set rather than a clip because a repeated identical sound reads as a looping
    /// audio cue instead of as a creature, and §04 asks the Listener to extract real
    /// information from exactly this channel.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class MonsterStateCue
    {
        [SerializeField]
        [Tooltip("Variants for this state. See Assets/Audio/Monster/monster_audio.manifest.json.")]
        private AudioClip[] _clips = Array.Empty<AudioClip>();

        [SerializeField]
        [Tooltip("Keep sounding for as long as the state lasts, rather than once on entry.")]
        private bool _repeatWhileInState;

        [SerializeField]
        [Tooltip("Silence between repeats, seconds. 0 plays them back to back.")]
        private float _repeatGapSeconds;

        /// <summary>True when this state has anything to say at all.</summary>
        public bool HasClips => _clips != null && _clips.Length > 0;

        /// <summary>Whether the cue keeps sounding while the state lasts. §06's 소리 column for 추격 is continuous.</summary>
        public bool RepeatWhileInState => _repeatWhileInState;

        /// <summary>Silence between repeats, seconds.</summary>
        public float RepeatGapSeconds => _repeatGapSeconds > 0f ? _repeatGapSeconds : 0f;

        /// <summary>
        /// A variant, avoiding the one played last.
        /// <para>
        /// Deliberately <see cref="UnityEngine.Random"/> and not the match's
        /// <see cref="HorrorGame.Core.Session.IRandomSource"/>: drawing from the seeded
        /// stream to pick a growl would shift every later draw the brain makes, and
        /// §13's replay guarantee would then depend on how many times the monster
        /// happened to snarl.
        /// </para>
        /// </summary>
        public AudioClip? Pick(ref int lastIndex)
        {
            if (!HasClips)
            {
                return null;
            }

            if (_clips.Length == 1)
            {
                lastIndex = 0;
                return _clips[0];
            }

            var index = UnityEngine.Random.Range(0, _clips.Length);
            if (index == lastIndex)
            {
                index = (index + 1) % _clips.Length;
            }

            lastIndex = index;
            return _clips[index];
        }
    }

    /// <summary>
    /// §06's 소리 column, played.
    /// <para>
    /// <b>There is no Standstill cue and there must never be one.</b> §06 makes the
    /// silence the game's weapon: when the monster stops, the Listener loses it and the
    /// team says "어디 갔어? 방금 여기 있었는데" — then relaxes and walks into it. The
    /// audio family was generated with that slot deliberately empty
    /// (<c>monster_audio.manifest.json</c>: "Do not add a clip here"), and this class
    /// has no field to put one in. The guard in <see cref="OnStateEntered"/> is the
    /// second lock on the same door.
    /// </para>
    /// <para>
    /// The proximity bed (<c>monster_presence_bed</c>, <c>monster_breath_loop_*</c>) is
    /// deliberately <em>not</em> here. It crossfades by distance only, never by state —
    /// a state-gated bed would leak the position 정지 exists to hide — so it belongs to
    /// the audio layer, which does not know what state the monster is in.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterAudioDriver : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField]
        [Tooltip("3D source for state cues. Separate from footsteps so a roar never cuts a step.")]
        private AudioSource? _voiceSource;

        [Header("§06 state cues")]
        [SerializeField]
        [Tooltip("경계 — monster_growl_01..03. One per entry: the state only lasts AlertGiveUpSeconds.")]
        private MonsterStateCue _alert = new MonsterStateCue();

        [SerializeField]
        [Tooltip("추격 — monster_roar_01..03. §06's 소리 column reads 발소리+포효, so this one repeats.")]
        private MonsterStateCue _chase = new MonsterStateCue();

        [SerializeField]
        [Tooltip("수색 — monster_search_01..02, over the 15 s sweep of the last known position.")]
        private MonsterStateCue _search = new MonsterStateCue();

        [Header("Events")]
        [SerializeField]
        [Tooltip("§04's flash — monster_stun_01..02, authored to MonsterStunSeconds.")]
        private MonsterStateCue _stunned = new MonsterStateCue();

        [SerializeField]
        [Tooltip("The kill — monster_grab_01..02.")]
        private MonsterStateCue _grab = new MonsterStateCue();

        private MonsterStateCue? _current;
        private float _repeatTimer;
        private int _lastAlertIndex = -1;
        private int _lastChaseIndex = -1;
        private int _lastSearchIndex = -1;
        private int _lastStunIndex = -1;
        private int _lastGrabIndex = -1;

        /// <summary>
        /// Switches the voice to a newly entered §06 state.
        /// <para>
        /// 순찰 and 정지 both fall through to silence here, for different reasons: 순찰
        /// is carried by footsteps alone (the manifest gives it no clips), and 정지 is
        /// the mechanic.
        /// </para>
        /// </summary>
        public void OnStateEntered(MonsterStateId state)
        {
            if (state == MonsterStateId.Standstill)
            {
                // 정지 | 멈춰서 듣는다 | 없음. Not "quieter" — none. The voice is cut
                // rather than left to ring out, because a tail is still a bearing.
                Silence();
                return;
            }

            var cue = CueFor(state);
            _current = cue;
            _repeatTimer = 0f;

            if (cue == null)
            {
                Silence();
                return;
            }

            PlayFrom(cue, state);
        }

        /// <summary>§04's 기절 landing. Fired once per stun, not once per flash.</summary>
        public void OnStunned()
        {
            PlayFrom(_stunned, null);
        }

        /// <summary>The kill. Raised by the host, since §06's table has no state for it.</summary>
        public void OnGrab()
        {
            PlayFrom(_grab, null);
        }

        private void Awake()
        {
            if (_voiceSource == null)
            {
                _voiceSource = GetComponent<AudioSource>();
            }
        }

        private void Update()
        {
            var cue = _current;
            if (cue == null || !cue.RepeatWhileInState || !cue.HasClips)
            {
                return;
            }

            _repeatTimer -= Time.deltaTime;
            if (_repeatTimer > 0f)
            {
                return;
            }

            PlayFrom(cue, null);
        }

        private ref int LastIndexRefFor(MonsterStateCue cue)
        {
            if (ReferenceEquals(cue, _alert))
            {
                return ref _lastAlertIndex;
            }

            if (ReferenceEquals(cue, _chase))
            {
                return ref _lastChaseIndex;
            }

            if (ReferenceEquals(cue, _search))
            {
                return ref _lastSearchIndex;
            }

            if (ReferenceEquals(cue, _stunned))
            {
                return ref _lastStunIndex;
            }

            return ref _lastGrabIndex;
        }

        private MonsterStateCue? CueFor(MonsterStateId state)
        {
            switch (state)
            {
                case MonsterStateId.Alert:
                    return _alert;
                case MonsterStateId.Chase:
                    return _chase;
                case MonsterStateId.Search:
                    return _search;

                // 순찰 has no voice — footsteps are its whole 소리 column — and 정지 is
                // handled before this call ever happens. Neither is an oversight.
                default:
                    return null;
            }
        }

        private void PlayFrom(MonsterStateCue cue, MonsterStateId? state)
        {
            if (state == MonsterStateId.Standstill)
            {
                return;
            }

            var clip = cue.Pick(ref LastIndexRefFor(cue));
            Play(clip);

            // The next repeat is timed from the clip's own length rather than polled
            // from AudioSource.isPlaying, whose answer for a PlayOneShot voice has never
            // been contractual. A cue that overlaps itself would double the monster.
            _repeatTimer = (clip != null ? clip.length : 0f) + cue.RepeatGapSeconds;
        }

        private void Play(AudioClip? clip)
        {
            var source = _voiceSource;
            if (clip == null || source == null)
            {
                return;
            }

            source.PlayOneShot(clip);
        }

        private void Silence()
        {
            _current = null;
            _repeatTimer = 0f;

            var source = _voiceSource;
            if (source != null)
            {
                source.Stop();
            }
        }
    }
}
