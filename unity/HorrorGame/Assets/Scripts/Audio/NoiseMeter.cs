#nullable enable

using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// How loud this player is, on the 0~1 scale
    /// <c>GameConstants.ListenerSelfNoiseThreshold</c> is quoted against.
    /// <para>
    /// §04 gives the 청음사 one price and states it as a rule about actions rather than
    /// about a number: "자기가 소리를 내면 못 듣는다. 뛰거나 문을 열면 정보가 끊긴다."
    /// This turns that into the float <c>ListenerAbility.Tick</c> takes. Two things
    /// follow from the wording:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// Walking must stay under the threshold and running must clear it, or §10's
    /// entry — 소리를 듣는다 / 자기가 조용해야 한다 — prices nothing. So the continuous
    /// term is interpolated across §06's own speed ladder rather than bucketed, which
    /// also makes a heavily loaded player (§08) quieter for free.
    /// </description></item>
    /// <item><description>
    /// A door is an instant, not a state. §04 counts it anyway, so a transient is held
    /// for <see cref="AudioTuning.SelfNoiseTransientSeconds"/> and then decays — a
    /// one-frame impulse would cut the feed for one frame, which no player would ever
    /// experience as a cost.
    /// </description></item>
    /// </list>
    /// <para>
    /// This reports; it does not decide. <c>ListenerAbility</c> owns the rule and
    /// <see cref="GameAudio"/> owns the matching duck. The same number also feeds
    /// whatever raises §06's <c>MonsterSoundCue</c>, so the noise that blinds you and
    /// the noise that draws the monster are one quantity — which is what makes §10's
    /// dilemma one dilemma instead of two.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Audio/Noise Meter")]
    public sealed class NoiseMeter : MonoBehaviour
    {
        [Tooltip("Whether this meter drives the local mix. Only the local player's " +
                 "noise may duck the local player's ears.")]
        [SerializeField]
        private bool isLocalPlayer;

        private float _movementNoise;
        private float _transient;
        private float _transientHold;

        /// <summary>
        /// The current level, 0 to 1. The louder of the movement term and any live
        /// transient — noise does not add up here, because §04's rule is about whether
        /// you are audible at all, and a running player who also opens a door is not
        /// twice as blind.
        /// </summary>
        public float Noise01 => Mathf.Clamp01(Mathf.Max(_movementNoise, _transient));

        /// <summary>
        /// True when this player is loud enough to lose the Listener feed. §04.
        /// The same comparison <c>ListenerAbility</c> makes, so the HUD, the mix and
        /// the rule can never disagree about whose fault the silence is.
        /// </summary>
        public bool BlindsListener => Noise01 > GameConstants.ListenerSelfNoiseThreshold;

        /// <summary>Whether this meter drives the local mix.</summary>
        public bool IsLocalPlayer
        {
            get { return isLocalPlayer; }
            set { isLocalPlayer = value; }
        }

        /// <summary>
        /// Feeds the continuous term from measured speed, m/s. Called by
        /// <see cref="FootstepAudio"/>, which is already measuring it.
        /// </summary>
        public void ReportMovementSpeed(float speedMetresPerSecond)
        {
            _movementNoise = NoiseForSpeed(speedMetresPerSecond);
        }

        /// <summary>
        /// Raises a one-off noise — a door, a dropped crate, a 조명탄 (§08's
        /// <c>FlareIgniteNoiseLevel</c>), a 소음 함정 (§04's
        /// <c>EngineerTrapNoiseLevel</c>). Louder transients replace quieter ones and
        /// restart the hold; a quieter one is ignored rather than cutting the tail off
        /// a louder one.
        /// </summary>
        public void AddTransient(float level01)
        {
            var level = Mathf.Clamp01(level01);
            if (level <= _transient)
            {
                return;
            }

            _transient = level;
            _transientHold = AudioTuning.SelfNoiseTransientSeconds;
        }

        /// <summary>Raises the noise of a door being opened or closed. §04 names this action specifically.</summary>
        public void ReportDoor()
        {
            AddTransient(AudioTuning.SelfNoiseDoor);
        }

        /// <summary>
        /// The noise a character moving at this speed makes, 0 to 1.
        /// <para>
        /// Anchored on §06's ladder: <c>WalkSpeed</c> → walk, <c>RunSpeed</c> → run,
        /// <c>RunnerSprintSpeed</c> → sprint. Reading the anchors from
        /// <c>GameConstants</c> rather than hard-coding 2.0 / 4.5 / 5.6 means a §06
        /// retune moves this curve with it, which matters because §04's threshold is
        /// stated in terms of the actions, not the speeds.
        /// </para>
        /// </summary>
        public static float NoiseForSpeed(float speedMetresPerSecond)
        {
            var speed = Mathf.Max(0f, speedMetresPerSecond);

            if (speed <= 0.05f)
            {
                return 0f;
            }

            if (speed <= GameConstants.WalkSpeed)
            {
                return Mathf.Lerp(0f, AudioTuning.SelfNoiseWalk, speed / GameConstants.WalkSpeed);
            }

            if (speed <= GameConstants.RunSpeed)
            {
                var t = Mathf.InverseLerp(GameConstants.WalkSpeed, GameConstants.RunSpeed, speed);
                return Mathf.Lerp(AudioTuning.SelfNoiseWalk, AudioTuning.SelfNoiseRun, t);
            }

            var sprint = Mathf.InverseLerp(GameConstants.RunSpeed, GameConstants.RunnerSprintSpeed, speed);
            return Mathf.Lerp(AudioTuning.SelfNoiseRun, AudioTuning.SelfNoiseSprint, sprint);
        }

        private void Update()
        {
            var dt = Time.deltaTime;

            if (_transientHold > 0f)
            {
                _transientHold -= dt;
            }
            else if (_transient > 0f)
            {
                _transient = GameAudio.Approach(_transient, 0f, dt, AudioTuning.SelfNoiseDecaySeconds);
                if (_transient < 0.01f)
                {
                    _transient = 0f;
                }
            }

            if (isLocalPlayer)
            {
                GameAudio.ReportSelfNoise(Noise01);
            }
        }

        private void OnDisable()
        {
            _movementNoise = 0f;
            _transient = 0f;
            _transientHold = 0f;

            if (isLocalPlayer)
            {
                // Dying or unspawning must not leave the mix ducked forever.
                GameAudio.ReportSelfNoise(0f);
            }
        }
    }
}
