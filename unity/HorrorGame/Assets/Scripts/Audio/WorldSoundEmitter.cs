#nullable enable

using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// A one-shot emitter for everything that is neither a footstep nor the monster:
    /// doors, the flashlight, a 조명탄, a 소음 함정, loot, the safe, the shop.
    /// <para>
    /// It exists so the rest of the game never has to know how this layer works. A
    /// door calls <see cref="Play(int)"/>; routing, occlusion, distance and the bus
    /// trim happen here. It also raises the noise those actions make (§04's
    /// "문을 열면 정보가 끊긴다"), so the sound and its consequence cannot be wired up
    /// separately and then drift apart — which is the failure mode that ends up with a
    /// door that is loud but harmless.
    /// </para>
    /// <para>
    /// Positional by default and occluded, so §12's geometry applies to props exactly
    /// as it applies to the monster. Non-positional is for the interface, where a
    /// sound that appeared to come from somewhere in the room would be worse than
    /// useless.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("HorrorGame/Audio/World Sound Emitter")]
    public sealed class WorldSoundEmitter : MonoBehaviour
    {
        [Tooltip("Which family this emitter belongs to.")]
        [SerializeField]
        private AudioBus bus = AudioBus.Items;

        [Tooltip("Off for interface cues, which must not appear to come from the room.")]
        [SerializeField]
        private bool positional = true;

        [Tooltip("Audible radius, metres. Ignored when not positional.")]
        [SerializeField]
        private float audibleRange = AudioTuning.DefaultWorldAudibleRange;

        [Tooltip("The variant sets this emitter can play, indexed by the caller.")]
        [SerializeField]
        private AudioClipBank[] banks = new AudioClipBank[0];

        [Tooltip("Noise each bank makes on the 0~1 scale GameConstants.ListenerSelfNoiseThreshold " +
                 "is quoted against, or 0 for silent. §04 / §08's 조명탄 / §04's 소음 함정.")]
        [SerializeField]
        private float[] bankNoise = new float[0];

        [Tooltip("Optional. Noise raised here blinds this player's own Listener feed (§04).")]
        [SerializeField]
        private NoiseMeter? noiseMeter;

        private AudioSource? _source;
        private SoundOccluder? _occluder;

        /// <summary>How many variant sets this emitter carries.</summary>
        public int BankCount => banks != null ? banks.Length : 0;

        /// <summary>
        /// Plays one variant from bank <paramref name="index"/> and raises its noise.
        /// Out-of-range indices are ignored rather than throwing — a missing clip is a
        /// content bug, and a content bug must not stop a door from opening.
        /// </summary>
        public void Play(int index)
        {
            Play(index, 1f);
        }

        /// <summary>Plays a variant at a scaled level, 0..1. The noise is raised at full strength regardless — §04's rule is about the action, not the mix.</summary>
        public void Play(int index, float level01)
        {
            if (_source == null || banks == null || index < 0 || index >= banks.Length)
            {
                return;
            }

            var bank = banks[index];
            if (bank == null || bank.IsEmpty)
            {
                return;
            }

            var clip = bank.Next();
            if (clip == null)
            {
                return;
            }

            _source.PlayOneShot(clip, bank.Gain * Mathf.Clamp01(level01));

            if (noiseMeter != null && bankNoise != null && index < bankNoise.Length && bankNoise[index] > 0f)
            {
                noiseMeter.AddTransient(bankNoise[index]);
            }
        }

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;

            GameAudio.Configure(_source, bus, positional, audibleRange);

            if (!positional)
            {
                return;
            }

            _occluder = GetComponent<SoundOccluder>();
            if (_occluder == null)
            {
                _occluder = gameObject.AddComponent<SoundOccluder>();
            }

            _occluder.SetBus(bus);
            _occluder.BaseVolume = 1f;
        }
    }
}
