#nullable enable

using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// One looping layer of a bed, with a level it fades toward.
    /// <para>
    /// The unit every continuous sound in the game is built from — zone tone, §07's
    /// tension bed, the heartbeat, the monster's presence. It owns its own
    /// <see cref="AudioSource"/> on a child object so a director can hold several at
    /// once and cross them without any of them knowing about the others.
    /// </para>
    /// <para>
    /// It stops itself when it reaches silence. Unity caps simultaneous voices, and a
    /// match has four players, a monster, five tension tiers and a bed per zone in
    /// flight; layers that idle at zero volume forever would spend that budget on
    /// nothing.
    /// </para>
    /// </summary>
    public sealed class AudioBedVoice
    {
        private readonly AudioSource _source;
        private readonly SoundOccluder? _occluder;
        private readonly AudioBus _bus;

        private float _level;
        private float _target;

        /// <summary>
        /// Creates a layer as a child of <paramref name="host"/>.
        /// </summary>
        /// <param name="host">Object the layer's child is parented to.</param>
        /// <param name="name">Child object name. Shows up in the profiler and the hierarchy.</param>
        /// <param name="bus">Family this layer mixes on.</param>
        /// <param name="positional">False for a 2D bed, true for one that lives in the world.</param>
        /// <param name="maxDistance">Audible radius when positional, metres.</param>
        /// <param name="occluded">Whether geometry between the layer and the ears should muffle it.</param>
        public AudioBedVoice(
            GameObject host,
            string name,
            AudioBus bus,
            bool positional,
            float maxDistance = 0f,
            bool occluded = false)
        {
            _bus = bus;

            var child = new GameObject(name);
            child.transform.SetParent(host.transform, false);

            _source = child.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.volume = 0f;

            GameAudio.Configure(_source, bus, positional, maxDistance);

            if (positional && occluded)
            {
                _occluder = child.AddComponent<SoundOccluder>();
                _occluder.SetBus(bus);
                _occluder.BaseVolume = 0f;
            }
        }

        /// <summary>The clip this layer loops, or null.</summary>
        public AudioClip? Clip => _source.clip;

        /// <summary>Current faded level, 0 to 1.</summary>
        public float Level => _level;

        /// <summary>Level this layer is fading toward, 0 to 1.</summary>
        public float Target
        {
            get { return _target; }
            set { _target = Mathf.Clamp01(value); }
        }

        /// <summary>True while the layer is producing sound.</summary>
        public bool IsPlaying => _source.isPlaying;

        /// <summary>
        /// Playback rate. Used by the heartbeat, where a pulse that speeds up reads as
        /// a body and a pulse that only gets louder reads as a fader move. Left at 1
        /// for every other bed: a room tone that changed pitch would be a §12 zone
        /// changing its identity.
        /// </summary>
        public float Pitch
        {
            get { return _source.pitch; }
            set { _source.pitch = value; }
        }

        /// <summary>
        /// Swaps the clip. Does not fade — a director that wants a crossfade uses two
        /// layers, which is what <see cref="CrossfadeBed"/> is.
        /// </summary>
        public void SetClip(AudioClip? clip)
        {
            if (_source.clip == clip)
            {
                return;
            }

            _source.Stop();
            _source.clip = clip;
            _level = 0f;
            Apply();
        }

        /// <summary>
        /// Advances the fade. <paramref name="seconds"/> is the time constant, so a
        /// director can cross slowly and stop quickly with the same layer.
        /// </summary>
        public void Tick(float deltaSeconds, float seconds)
        {
            _level = GameAudio.Approach(_level, _target, deltaSeconds, seconds);

            if (_level > AudioTuning.SilenceThreshold)
            {
                if (!_source.isPlaying && _source.clip != null)
                {
                    // Start at a random offset. Four players hearing the same zone bed
                    // phase-locked would comb-filter into an obvious loop, and §12 puts
                    // the whole team in one zone for most of a descent.
                    _source.time = Random.Range(0f, Mathf.Max(0.01f, _source.clip.length));
                    _source.Play();
                }
            }
            else if (_source.isPlaying)
            {
                _source.Stop();
                _level = 0f;
            }

            Apply();
        }

        /// <summary>Silences and stops the layer immediately. For a scene teardown, not a transition.</summary>
        public void Stop()
        {
            _target = 0f;
            _level = 0f;
            _source.Stop();
            Apply();
        }

        private void Apply()
        {
            if (_occluder != null)
            {
                // The occluder owns source.volume; handing it the level keeps distance,
                // occlusion and bus gain in one place.
                _occluder.BaseVolume = _level;
                return;
            }

            GameAudio.ApplyVolume(_source, _bus, _level);
        }
    }

    /// <summary>
    /// Two layers that hand over to each other — the shape every "which bed is playing
    /// now" question in the game takes.
    /// <para>
    /// A pair rather than one source with a swapped clip, because a swap is a cut, and
    /// §12 asks for material boundaries that are clear without being abrupt while §07
    /// asks for pressure that is felt without being noticed. Both are crossfades; only
    /// the time constant differs.
    /// </para>
    /// </summary>
    public sealed class CrossfadeBed
    {
        private readonly AudioBedVoice _a;
        private readonly AudioBedVoice _b;
        private bool _bIsFront;
        private float _level = 1f;

        /// <summary>Creates the pair as children of <paramref name="host"/>. Arguments mirror <see cref="AudioBedVoice"/>.</summary>
        public CrossfadeBed(
            GameObject host,
            string name,
            AudioBus bus,
            bool positional,
            float maxDistance = 0f,
            bool occluded = false)
        {
            _a = new AudioBedVoice(host, name + "_a", bus, positional, maxDistance, occluded);
            _b = new AudioBedVoice(host, name + "_b", bus, positional, maxDistance, occluded);
        }

        /// <summary>The clip currently being faded up, or null when the bed is idle.</summary>
        public AudioClip? Current => Front.Clip;

        /// <summary>
        /// Overall level of whatever is playing, 0 to 1. Multiplies the crossfade, so a
        /// director can duck the whole bed without disturbing a transition in progress.
        /// </summary>
        public float Level
        {
            get { return _level; }
            set { _level = Mathf.Clamp01(value); }
        }

        private AudioBedVoice Front => _bIsFront ? _b : _a;

        private AudioBedVoice Back => _bIsFront ? _a : _b;

        /// <summary>
        /// Crossfades to a clip. Passing the clip that is already playing does nothing,
        /// so this is safe to call every frame with the current answer — which is how
        /// the directors use it.
        /// </summary>
        public void FadeTo(AudioClip? clip)
        {
            if (Front.Clip == clip)
            {
                return;
            }

            if (Back.Clip == clip && Back.Clip != null)
            {
                // Turned back before the previous fade finished. Reuse the layer that
                // still has the audio rather than restarting it, so walking in and out
                // of a doorway does not chop the room tone into pieces.
                _bIsFront = !_bIsFront;
                return;
            }

            _bIsFront = !_bIsFront;
            Front.SetClip(clip);
        }

        /// <summary>Advances both layers. <paramref name="seconds"/> is the crossfade time constant.</summary>
        public void Tick(float deltaSeconds, float seconds)
        {
            Front.Target = Front.Clip != null ? _level : 0f;
            Back.Target = 0f;

            _a.Tick(deltaSeconds, seconds);
            _b.Tick(deltaSeconds, seconds);
        }

        /// <summary>Fades everything out over <paramref name="seconds"/>, keeping the clips loaded.</summary>
        public void FadeOut(float deltaSeconds, float seconds)
        {
            _a.Target = 0f;
            _b.Target = 0f;
            _a.Tick(deltaSeconds, seconds);
            _b.Tick(deltaSeconds, seconds);
        }

        /// <summary>Stops both layers immediately.</summary>
        public void Stop()
        {
            _a.Stop();
            _b.Stop();
        }
    }
}
