#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Presence;
using UnityEngine;

namespace HorrorGame.Gameplay.Presence
{
    /// <summary>
    /// What the 그늘 sounds like. Two beds that rise with the pool and two one-shots for
    /// the taking and the release.
    /// <para>
    /// <b>Every source here is 2D, and that is a design decision rather than a shortcut.</b>
    /// §04 gives the 청음사 exactly one ability — reading the monster's 위치 · 거리 ·
    /// 이동 방향 by ear — and ASSETS.md §1 records that the entire audio import policy
    /// exists because a single wrong checkbox silently deletes it. A second world-space
    /// emitter with its own direction and distance would compete for that channel and take
    /// the role apart with nothing reporting a fault. The 그늘 has no position in Core
    /// either (<c>PresenceTests</c> asserts it), so a sound with no direction is not a
    /// compromise, it is the same fact expressed in the mix: this is not a thing in the
    /// room, it is a thing that is happening to you.
    /// </para>
    /// <para>
    /// It also stays quiet. The beds are authored at −19 and −13 dBFS and are ducked
    /// further here, because a footstep is §04's whole channel and masking one would take
    /// the role away by volume instead of by direction.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PresenceAudio : MonoBehaviour
    {
        /// <summary>
        /// Loudest the 고임 bed gets, at a full pool. Under the 임박 layer by design — the
        /// gathering is something a player notices having been there, not arriving.
        /// </summary>
        public const float GatheringMaxVolume = 0.38f;

        /// <summary>Loudest the 임박 layer gets. This is the warning, so it is the loudest thing the 그늘 has.</summary>
        public const float CloseMaxVolume = 0.72f;

        /// <summary>Seconds the beds take to reach a new level. Slow, so a torch flick does not click the mix.</summary>
        public const float FadeSeconds = 1.8f;

        [Header("Clips")]
        [SerializeField]
        private AudioClip? _gathering;

        [SerializeField]
        private AudioClip? _close;

        [SerializeField]
        private AudioClip? _taken;

        [SerializeField]
        private AudioClip? _returning;

        [Header("Mix")]
        [SerializeField]
        [Tooltip("Optional bus. Left empty the sources go to the default mixer group.")]
        private UnityEngine.Audio.AudioMixerGroup? _bus;

        private AudioSource? _gatheringSource;
        private AudioSource? _closeSource;
        private AudioSource? _oneShotSource;

        private PresenceState? _state;
        private PresenceStage _lastStage = PresenceStage.Clear;
        private float _pooling;
        private bool _overridden;

        /// <summary>Binds the state this mix follows.</summary>
        public void Bind(PresenceState? state)
        {
            _state = state;
            _overridden = false;
        }

        /// <summary>Drives the mix directly, for a rig with no match running.</summary>
        public void SetStageOverride(PresenceStage stage, float pooling01)
        {
            _overridden = true;
            _pooling = Mathf.Clamp01(pooling01);
            ApplyStage(stage);
            _lastStage = stage;
        }

        /// <summary>
        /// Plays the taking. Called from <c>PresenceDirector.Taken</c> for the local player,
        /// or by a rig.
        /// </summary>
        public void PlayTaken()
        {
            if (_oneShotSource != null && _taken != null)
            {
                _oneShotSource.PlayOneShot(_taken);
            }
        }

        /// <summary>Plays the voice coming back — §03's certainty deliberately does not come with it.</summary>
        public void PlayReturn()
        {
            if (_oneShotSource != null && _returning != null)
            {
                _oneShotSource.PlayOneShot(_returning);
            }
        }

        private void Awake()
        {
            _gatheringSource = BuildSource("Gathering", _gathering, loop: true);
            _closeSource = BuildSource("Close", _close, loop: true);
            _oneShotSource = BuildSource("OneShot", null, loop: false);
        }

        private void Update()
        {
            if (!_overridden && _state != null)
            {
                _pooling = _state.Pooling01;

                var stage = _state.Stage;
                if (stage != _lastStage)
                {
                    if (stage == PresenceStage.Taken)
                    {
                        PlayTaken();
                    }
                    else if (_lastStage == PresenceStage.Taken)
                    {
                        PlayReturn();
                    }

                    ApplyStage(stage);
                    _lastStage = stage;
                }
            }

            Fade(_gatheringSource, TargetGathering());
            Fade(_closeSource, TargetClose());
        }

        private void ApplyStage(PresenceStage stage)
        {
            // The beds keep running through a taking and are simply pulled down. Stopping
            // and restarting a loop puts a transient on the one moment the mix is supposed
            // to be emptying out.
            if (_gatheringSource != null && !_gatheringSource.isPlaying)
            {
                _gatheringSource.Play();
            }

            if (_closeSource != null && !_closeSource.isPlaying)
            {
                _closeSource.Play();
            }
        }

        private float TargetGathering()
        {
            if (_lastStage == PresenceStage.Taken)
            {
                return 0f;
            }

            // Rises from nothing and is already at full by the time the warning starts, so
            // the 임박 layer arrives on top of something rather than out of silence.
            return GatheringMaxVolume
                   * Mathf.Clamp01(_pooling / Mathf.Max(0.01f, GameConstants.PresenceWarnPooling));
        }

        private float TargetClose()
        {
            if (_lastStage == PresenceStage.Taken)
            {
                return 0f;
            }

            if (_pooling < GameConstants.PresenceWarnPooling)
            {
                return 0f;
            }

            var through = Mathf.InverseLerp(GameConstants.PresenceWarnPooling, 1f, _pooling);
            return CloseMaxVolume * Mathf.Lerp(0.45f, 1f, through);
        }

        private void Fade(AudioSource? source, float target)
        {
            if (source == null)
            {
                return;
            }

            source.volume = Mathf.MoveTowards(
                source.volume, target, Time.deltaTime / Mathf.Max(0.01f, FadeSeconds));
        }

        private AudioSource BuildSource(string name, AudioClip? clip, bool loop)
        {
            var host = new GameObject("[Presence " + name + "]");
            host.transform.SetParent(transform, worldPositionStays: false);

            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.volume = 0f;

            // 0 is fully 2D. See the class remarks — this is the line that keeps §04's
            // 청음사 channel to itself.
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;

            if (_bus != null)
            {
                source.outputAudioMixerGroup = _bus;
            }

            return source;
        }
    }
}
