using HorrorGame.Core.Math;

namespace HorrorGame.Core.Presence
{
    /// <summary>
    /// One player's exposure to the 그늘 — how much has pooled on them, what it has
    /// taken, and how long they carry it.
    /// <para>
    /// The 그늘 is a condition rather than an antagonist, and this class is where that
    /// distinction is enforced: there is no position on it, nothing here moves, and the
    /// only input is a <see cref="PresenceReading"/> sampled wherever the player already
    /// is. A player cannot be approached by it, outrun it, break line of sight from it
    /// or hide from it — §06's whole vocabulary is inapplicable, which is the guarantee
    /// that §01's 「이길 수 없는 적 → 공포가 유지된다」 still has exactly one referent.
    /// </para>
    /// <para>
    /// <b>What the player is actually paying.</b> §03 makes the flashlight the lock on
    /// all progress and prices only one side of the switch: light on, the monster sees
    /// you and the battery drains. Light off has been free, so the dominant play is to
    /// travel unlit and flick the beam on to read. This class is the other price. It
    /// does not make the dark lethal — it makes the dark *expensive*, in the one
    /// currency §03 already made scarce, which is a person's ability to carry what they
    /// saw back to the other three.
    /// </para>
    /// </summary>
    public sealed class PresenceState
    {
        private float _pooling;
        private float _silenceRemaining;
        private float _recallRemaining;
        private bool _tollPending;
        private PresenceToll _toll;
        private PresenceReading _reading;

        /// <summary>Creates a clear state for one player.</summary>
        /// <param name="playerIndex">Index into the match's player list. Carried on the toll so the session knows whose voice to cut.</param>
        public PresenceState(int playerIndex)
        {
            PlayerIndex = playerIndex;
        }

        /// <summary>Which player this is.</summary>
        public int PlayerIndex { get; }

        /// <summary>
        /// How full the pool is, 0–1. Reaches 1 after
        /// <see cref="GameConstants.PresenceSaturationSeconds"/> of unbroken pitch dark
        /// at 동트기 전, and proportionally longer at every lighter or earlier moment.
        /// </summary>
        public float Pooling01
        {
            get { return _pooling; }
        }

        /// <summary>The last reading this state was ticked with. Presentation reads its terms; nothing else should.</summary>
        public PresenceReading Reading
        {
            get { return _reading; }
        }

        /// <summary>
        /// The stage the player is in — the only presence state anything outside these
        /// rules is allowed to branch on. See <see cref="PresenceStage"/>.
        /// </summary>
        public PresenceStage Stage
        {
            get
            {
                if (_silenceRemaining > 0f)
                {
                    return PresenceStage.Taken;
                }

                if (_pooling >= GameConstants.PresenceWarnPooling)
                {
                    return PresenceStage.Close;
                }

                return _pooling > 0f ? PresenceStage.Gathering : PresenceStage.Clear;
            }
        }

        /// <summary>Seconds of §13 silence left. Zero when the player can speak.</summary>
        public float SilenceRemainingSeconds
        {
            get { return _silenceRemaining; }
        }

        /// <summary>
        /// Whether §13's voice channel is open for this player, as far as the 그늘 is
        /// concerned.
        /// <para>
        /// The session layer must <c>&amp;&amp;</c> this with
        /// <c>PlayerState.MayTransmitVoice</c>, which answers the §09 half — a dead
        /// player is silent for a different and permanent reason. Two independent
        /// reasons to be silent, one channel, and neither may overwrite the other.
        /// </para>
        /// </summary>
        public bool MayTransmitVoice
        {
            get { return _silenceRemaining <= 0f; }
        }

        /// <summary>
        /// Extra misread-condition strength this player is currently carrying, 0–1.
        /// §03's confusion pairs get this added to their condition strength while it
        /// lasts; at zero the player reads exactly as §03 already specifies.
        /// <para>
        /// Fades linearly to zero over
        /// <see cref="GameConstants.PresenceRecallFadeSeconds"/>. Light does not clear
        /// it and neither does surfacing: §03's memory error is a thing that has already
        /// happened to a person, not a status effect on a room.
        /// </para>
        /// </summary>
        public float RecallSmear01
        {
            get
            {
                if (_recallRemaining <= 0f)
                {
                    return 0f;
                }

                return GameConstants.PresenceRecallSmear
                       * MathX.Clamp01(_recallRemaining / GameConstants.PresenceRecallFadeSeconds);
            }
        }

        /// <summary>Times the 그늘 has taken from this player this match. Telemetry, and §16's tuning input.</summary>
        public int TakenCount { get; private set; }

        /// <summary>
        /// True when the last reading says the monster is thinning the dark around this
        /// player — the cue every role gets and §07's 정지 row cannot silence.
        /// </summary>
        public bool ClearedByMonster
        {
            get { return _reading.ClearedByMonster; }
        }

        /// <summary>
        /// Steps one player's exposure.
        /// </summary>
        /// <param name="deltaSeconds">
        /// Step length. Zero or negative changes nothing. A frame spike cannot tunnel
        /// through a taking: the pool saturates, the toll is queued, and the residual
        /// starts again from the same step.
        /// </param>
        /// <param name="reading">Where the player is standing, sampled by <see cref="PresenceDensity.Sample"/>.</param>
        public void Tick(float deltaSeconds, in PresenceReading reading)
        {
            _reading = reading;

            if (float.IsNaN(deltaSeconds) || deltaSeconds <= 0f)
            {
                return;
            }

            if (_silenceRemaining > 0f)
            {
                _silenceRemaining = System.Math.Max(0f, _silenceRemaining - deltaSeconds);
            }

            if (_recallRemaining > 0f)
            {
                _recallRemaining = System.Math.Max(0f, _recallRemaining - deltaSeconds);
            }

            if (reading.Density > 0f)
            {
                _pooling = MathX.Clamp01(
                    _pooling + (reading.Density * deltaSeconds / GameConstants.PresenceSaturationSeconds));
            }
            else
            {
                _pooling = MathX.Clamp01(
                    _pooling - (deltaSeconds / GameConstants.PresenceDispersalSeconds));
            }

            // A player already inside a taking is not taken again. Without this a step
            // long enough to refill the residual pool would stack two silences into one,
            // and §01's "저지는 전부 일시적" cuts both ways — a toll that can be doubled
            // by a frame spike is not a fixed price.
            if (_pooling >= 1f && _silenceRemaining <= 0f)
            {
                Take();
            }
        }

        /// <summary>
        /// Pushes the pool back by an amount of light that did not come from standing in
        /// it — §04's 섬광, a 조명탄 struck at your feet, anything that is a flash rather
        /// than a place.
        /// <para>
        /// Charged in the same units as <see cref="GameConstants.PresenceDispersalSeconds"/>,
        /// so a one-second flash buys exactly what a second of standing in a lit room
        /// buys and no instantaneous verb can be worth more than a lamp.
        /// </para>
        /// </summary>
        /// <param name="lightSeconds">Seconds of equivalent readable light. Zero, negative and NaN do nothing.</param>
        public void Disperse(float lightSeconds)
        {
            if (float.IsNaN(lightSeconds) || lightSeconds <= 0f)
            {
                return;
            }

            _pooling = MathX.Clamp01(_pooling - (lightSeconds / GameConstants.PresenceDispersalSeconds));
        }

        /// <summary>
        /// Takes the toll owed to this player, once.
        /// <para>
        /// A one-shot handoff rather than a flag, matching <c>LightField</c>'s expired
        /// the host may tick and read at
        /// different rates, and a silence has to be applied exactly once. Missing it
        /// would leave a player who can still talk with a stage that says they cannot.
        /// </para>
        /// </summary>
        public bool TryTakeToll(out PresenceToll toll)
        {
            if (!_tollPending)
            {
                toll = default(PresenceToll);
                return false;
            }

            _tollPending = false;
            toll = _toll;
            return true;
        }

        /// <summary>
        /// Wipes this player's exposure for a new match.
        /// <para>
        /// Not for §03's 부분 리셋. That resets 괴물의 추격 상태 · 위치 · 어그로 and
        /// explicitly keeps 시간, and the 그늘 is neither of those — it is a condition on
        /// a person, cleared by light and by nothing else. A player who walks up to the
        /// van in the dark arrives with the pool they left the basement with, and it
        /// drains there because the 지상 is lit, not because they surfaced.
        /// </para>
        /// </summary>
        public void Clear()
        {
            _pooling = 0f;
            _silenceRemaining = 0f;
            _recallRemaining = 0f;
            _tollPending = false;
            _toll = default(PresenceToll);
            _reading = PresenceReading.None;
            TakenCount = 0;
        }

        private void Take()
        {
            TakenCount++;
            _pooling = GameConstants.PresenceResidualPooling;
            _silenceRemaining = GameConstants.PresenceSilenceSeconds;
            _recallRemaining = GameConstants.PresenceRecallFadeSeconds;

            _toll = new PresenceToll(
                PlayerIndex,
                GameConstants.PresenceSilenceSeconds,
                GameConstants.PresenceRecallSmear,
                TakenCount);
            _tollPending = true;
        }
    }
}
