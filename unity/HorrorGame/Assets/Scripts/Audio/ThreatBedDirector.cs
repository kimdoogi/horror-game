#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Threat;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// §07's night, as a bed that never stops changing.
    /// <para>
    /// §07 argues for a clock over a descent counter partly because the pressure
    /// becomes "연속적", and <c>ThreatCurve</c> answers that argument by splitting it in
    /// two: the mechanics step, and <c>ThreatScalar</c> is the continuous half, which
    /// its own documentation reserves for "music layers, ambience, HUD tint". This is
    /// that layer. It is the only place in the game where §07's curve is expressed as
    /// something continuous, and it is deliberately the only place a player can
    /// perceive the night advancing while they are underground.
    /// </para>
    /// <para>
    /// <b>Why the beds crossfade continuously instead of switching at the boundary.</b>
    /// §07 gates the actual time behind surfacing or a 회중시계 — "시각은 지상에서만 알
    /// 수 있다" — so a bed that changed audibly at 8:00 would hand out, for free, the
    /// information the section charges a shop item for. Five layers weighted by
    /// distance along the tier axis means the mix is always mid-transition: a player
    /// can tell it is worse than it was, and cannot tell what time it is. That is
    /// exactly the difference §07 draws.
    /// </para>
    /// <para>
    /// The elapsed time is pushed in rather than read from a <c>MatchClock</c>, because
    /// §13 makes the host authoritative and a client has a replicated number rather
    /// than the clock itself.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Threat Bed Director")]
    public sealed class ThreatBedDirector : MonoBehaviour
    {
        [Tooltip("amb_tension_t1..t5, in §07's escalation order: 초저녁 · 밤 · 심야 · 새벽 · 동트기 전.")]
        [SerializeField]
        private AudioClip?[] tierBeds = new AudioClip?[GameConstants.ThreatTierCount];

        [Tooltip("UI/threat_*.wav. Optional, and only played when §07 says the team " +
                 "may know the time — on the surface, or holding a 회중시계.")]
        [SerializeField]
        private AudioClip?[] tierStingers = new AudioClip?[GameConstants.ThreatTierCount];

        [Tooltip("Level of the bed at match start, before §07's curve lifts it.")]
        [SerializeField, Range(0f, 1f)]
        private float baseLevel = 0.75f;

        private AudioBedVoice[]? _layers;
        private AudioSource? _stingerSource;
        private int _lastAnnouncedTier = -1;

        /// <summary>
        /// Seconds since the match began. §07's only input. Set every frame by whatever
        /// owns the replicated clock.
        /// </summary>
        public float ElapsedSeconds { get; set; }

        /// <summary>Whether the bed plays at all. Cleared above ground and between matches.</summary>
        public bool Active { get; set; } = true;

        /// <summary>The continuous 0–1 reading of §07's pressure driving the mix right now. Presentation only.</summary>
        public float ThreatScalar => ThreatCurve.ThreatScalar(ElapsedSeconds);

        /// <summary>
        /// Current faded level of each of §07's five layers, 0 to 1. Presentation
        /// only, and the only way to check the crossfade from outside — §07 deliberately
        /// gives the player no number, so a test cannot read one either and has to look
        /// at the faders.
        /// </summary>
        public float LevelOfTier(int tierIndex)
        {
            var layers = _layers;
            if (layers == null || tierIndex < 0 || tierIndex >= layers.Length)
            {
                return 0f;
            }

            return layers[tierIndex].Level;
        }

        /// <summary>
        /// Wires the beds from code, for a scene assembled at runtime.
        /// <para>
        /// Re-applies to the live layers when called after <c>Awake</c>, which is the
        /// normal case for a rig that adds the component and then fills it.
        /// </para>
        /// </summary>
        /// <param name="beds">amb_tension_t1..t5, in §07's escalation order.</param>
        /// <param name="stingers">UI/threat_*, or nulls. Gated on <c>MatchClock.IsTimeReadable</c>.</param>
        public void Configure(AudioClip?[]? beds, AudioClip?[]? stingers)
        {
            tierBeds = beds ?? new AudioClip?[GameConstants.ThreatTierCount];
            tierStingers = stingers ?? new AudioClip?[GameConstants.ThreatTierCount];

            var layers = _layers;
            if (layers == null)
            {
                return;
            }

            for (var i = 0; i < layers.Length; i++)
            {
                layers[i].SetClip(i < tierBeds.Length ? tierBeds[i] : null);
            }
        }

        /// <summary>
        /// Plays §07's tier stinger, if one is authored and the team is allowed to know
        /// the time.
        /// <para>
        /// Gated on <paramref name="timeIsReadable"/> — <c>MatchClock.IsTimeReadable</c>
        /// — because §07 makes not knowing the hour a cost the team pays, and §08 sells
        /// a 회중시계 to remove it. An unconditional stinger would refund that purchase
        /// to every player in the game.
        /// </para>
        /// </summary>
        public void AnnounceTier(int tierIndex, bool timeIsReadable)
        {
            if (!timeIsReadable || _stingerSource == null || tierIndex == _lastAnnouncedTier)
            {
                return;
            }

            _lastAnnouncedTier = tierIndex;

            if (tierIndex < 0 || tierIndex >= tierStingers.Length)
            {
                return;
            }

            var clip = tierStingers[tierIndex];
            if (clip == null)
            {
                return;
            }

            GameAudio.ApplyVolume(_stingerSource, AudioBus.Interface, 1f);
            _stingerSource.PlayOneShot(clip, 1f);
        }

        private void Awake()
        {
            // Exactly §07's five rows, whatever the inspector was filled with. The table
            // is the design; an extra entry would be a sixth night nobody wrote.
            var count = GameConstants.ThreatTierCount;
            _layers = new AudioBedVoice[count];
            for (var i = 0; i < count; i++)
            {
                _layers[i] = new AudioBedVoice(gameObject, "tension_t" + (i + 1), AudioBus.Ambience, false);
                _layers[i].SetClip(i < tierBeds.Length ? tierBeds[i] : null);
            }

            var child = new GameObject("threat_stinger");
            child.transform.SetParent(transform, false);
            _stingerSource = child.AddComponent<AudioSource>();
            _stingerSource.playOnAwake = false;
            _stingerSource.loop = false;
            GameAudio.Configure(_stingerSource, AudioBus.Interface, false, 0f);
        }

        private void OnDisable()
        {
            if (_layers == null)
            {
                return;
            }

            foreach (var layer in _layers)
            {
                layer.Stop();
            }
        }

        private void Update()
        {
            if (_layers == null)
            {
                return;
            }

            var dt = Time.deltaTime;
            var weights = Weights(ElapsedSeconds, _layers.Length);

            // §07's continuous scalar lifts the whole bed as well as choosing between
            // layers. The two are different statements — which night it is, and how bad
            // it has got — and the tension of 동트기 전 is both.
            var level = Active
                ? Mathf.Lerp(baseLevel, 1f, ThreatCurve.ThreatScalar(ElapsedSeconds))
                : 0f;

            for (var i = 0; i < _layers.Length; i++)
            {
                _layers[i].Target = weights[i] * level;
                _layers[i].Tick(dt, Active
                    ? AudioTuning.ThreatBedCrossfadeSeconds
                    : AudioTuning.BedStopFadeSeconds);
            }
        }

        private readonly float[] _weightScratch = new float[GameConstants.ThreatTierCount + 1];

        /// <summary>
        /// Weight per tier bed, as a triangular window one tier wide sliding along the
        /// tier axis.
        /// <para>
        /// At an exact boundary the owning tier is at 1 and its neighbours at 0; halfway
        /// through a tier the current and next beds are at 0.5 each. So the mix is
        /// almost never showing a single tier cleanly, which is the point — see the
        /// class remarks on §07's "시각은 지상에서만".
        /// </para>
        /// <para>
        /// Saturates on the last tier, matching <c>ThreatCurve.TierProgress</c>, which
        /// returns 1 there because 동트기 전 has no end to be a fraction of.
        /// </para>
        /// </summary>
        private float[] Weights(float elapsedSeconds, int count)
        {
            var weights = _weightScratch.Length >= count ? _weightScratch : new float[count];
            for (var i = 0; i < count; i++)
            {
                weights[i] = 0f;
            }

            if (count == 0)
            {
                return weights;
            }

            var last = Mathf.Min(count, GameConstants.ThreatTierCount) - 1;
            if (last < 0)
            {
                return weights;
            }

            var seconds = elapsedSeconds > 0f ? elapsedSeconds : 0f;
            var position = Mathf.Clamp(seconds / GameConstants.ThreatTierSeconds, 0f, last);

            var lower = Mathf.FloorToInt(position);
            var upper = Mathf.Min(lower + 1, last);
            var blend = position - lower;

            if (lower == upper)
            {
                weights[lower] = 1f;
                return weights;
            }

            weights[lower] = 1f - blend;
            weights[upper] = blend;
            return weights;
        }
    }
}
