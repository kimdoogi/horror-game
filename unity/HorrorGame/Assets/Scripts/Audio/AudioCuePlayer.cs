#nullable enable

using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// Plays <see cref="AudioCueId"/>s. One per local player; everything in the game
    /// that makes a noise at a moment goes through here.
    /// <para>
    /// <b>Two emitters, not one.</b> §12's geometry has to apply to a door and must not
    /// apply to a clue read — one happened in a room and the other happened in the
    /// player's head. <see cref="AudioCues.BusOf"/> decides which, so a caller never
    /// picks, and a cue cannot be moved between the two by accident: changing where a
    /// sound lives is a change to that table with the § reasoning next to it.
    /// </para>
    /// <para>
    /// <b>It raises the noise as well as the sound.</b> §04's price is stated as a rule
    /// about actions — "뛰거나 문을 열면 정보가 끊긴다" — and a door that is loud but
    /// costs the Listener nothing is the failure mode <see cref="WorldSoundEmitter"/>
    /// already names. <see cref="AudioCues.NoiseOf"/> and the clip are one call, so they
    /// cannot be wired up separately and drift.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Audio Cue Player")]
    public sealed class AudioCuePlayer : MonoBehaviour
    {
        [Tooltip("Every one-shot in the game, from the wired clip set.")]
        [SerializeField]
        private MatchAudioLibrary? library;

        [Tooltip("Optional. Noise raised here blinds this player's own Listener feed (§04).")]
        [SerializeField]
        private NoiseMeter? noiseMeter;

        private AudioSource? _world;
        private AudioSource? _interface;

        /// <summary>The last cue played, or <see cref="AudioCueId.None"/>. For a HUD debug line and for tests.</summary>
        public AudioCueId LastCue { get; private set; } = AudioCueId.None;

        /// <summary>The clip the last cue actually produced, or null when it was unauthored.</summary>
        public AudioClip? LastClip { get; private set; }

        /// <summary>How many cues have been played. A test's proof that an edge fired exactly once.</summary>
        public int PlayedCount { get; private set; }

        /// <summary>The clip set in use. Null until wired, in which case every cue is silent.</summary>
        public MatchAudioLibrary? Library => library;

        /// <summary>
        /// Wires the player from code, for a scene assembled at runtime. Safe after
        /// <c>Awake</c>.
        /// </summary>
        public void Configure(MatchAudioLibrary? clips, NoiseMeter? meter)
        {
            library = clips;
            noiseMeter = meter;
        }

        /// <summary>
        /// Plays a cue at this player's own position, and raises whatever noise §04
        /// charges for it.
        /// </summary>
        /// <returns>True when a clip actually came out. False means it was never authored.</returns>
        public bool Play(AudioCueId cue)
        {
            return PlayAt(cue, transform.position);
        }

        /// <summary>
        /// Plays a cue at a world position — a door somewhere else, a 소음 함정 across
        /// the zone. Ignored for a non-positional cue, which by definition did not
        /// happen anywhere.
        /// </summary>
        /// <returns>True when a clip actually came out.</returns>
        public bool PlayAt(AudioCueId cue, Vector3 position)
        {
            LastCue = cue;
            LastClip = null;

            if (cue == AudioCueId.None || library == null)
            {
                return false;
            }

            var noise = AudioCues.NoiseOf(cue);
            if (noise > 0f && noiseMeter != null)
            {
                noiseMeter.AddTransient(noise);
            }

            var bank = library.For(cue);
            if (bank == null || bank.IsEmpty)
            {
                // An unauthored cue is silent and says nothing. MatchAudioLibrary.Validate
                // is where a missing clip is reported, once, rather than per press.
                return false;
            }

            var clip = bank.Next();
            if (clip == null)
            {
                return false;
            }

            var bus = AudioCues.BusOf(cue);
            var source = AudioCues.IsPositional(cue) ? _world : _interface;
            if (source == null)
            {
                return false;
            }

            if (AudioCues.IsPositional(cue))
            {
                source.transform.position = position;
            }

            LastClip = clip;
            PlayedCount++;

            source.PlayOneShot(clip, bank.Gain);

            // Non-positional sources have no occluder to own their volume, so the bus
            // gain has to be applied here or the Interface trim would never reach them.
            if (!AudioCues.IsPositional(cue))
            {
                GameAudio.ApplyVolume(source, bus, 1f);
            }

            return true;
        }

        private void Awake()
        {
            var world = new GameObject("cue_world");
            world.transform.SetParent(transform, false);
            _world = world.AddComponent<AudioSource>();
            _world.playOnAwake = false;
            _world.loop = false;
            GameAudio.Configure(_world, AudioBus.Items, true, AudioTuning.DefaultWorldAudibleRange);

            // The occluder owns this source's volume from here on — §12's geometry has to
            // apply to a door exactly as it applies to a footstep, or a wall would be a
            // rule the world sounds are exempt from.
            var occluder = world.AddComponent<SoundOccluder>();
            occluder.SetBus(AudioBus.Items);
            occluder.BaseVolume = 1f;

            var ui = new GameObject("cue_interface");
            ui.transform.SetParent(transform, false);
            _interface = ui.AddComponent<AudioSource>();
            _interface.playOnAwake = false;
            _interface.loop = false;
            GameAudio.Configure(_interface, AudioBus.Interface, false, 0f);
            GameAudio.ApplyVolume(_interface, AudioBus.Interface, 1f);
        }
    }
}
