using HorrorGame.Core.Math;

namespace HorrorGame.Core.Clues
{
    /// <summary>Where one player's attempt to read one clue has got to. §03.</summary>
    public enum ClueReadState
    {
        /// <summary>Not reading anything.</summary>
        Idle = 0,

        /// <summary>Close enough, still enough, lit enough — and counting. §03: "오래 비춰야 읽힌다".</summary>
        Reading = 1,

        /// <summary>
        /// The beam was held long enough. Latches until the host takes the read or
        /// the player looks at something else, so a frame spike cannot step straight
        /// past it.
        /// </summary>
        Complete = 2,

        /// <summary>A condition broke. Progress is gone, not paused. §03.</summary>
        Interrupted = 3,
    }

    /// <summary>Why a read stopped. §03's reading obstacles, as outcomes.</summary>
    public enum ClueReadInterrupt
    {
        /// <summary>Nothing has gone wrong.</summary>
        None = 0,

        /// <summary>The player stopped looking at a clue.</summary>
        NoTarget = 1,

        /// <summary>
        /// The light went. §03's "어둠 = 목표의 잠금장치" is a lock, and a lock that
        /// let you keep the progress you had would not be one — the batteries would
        /// stop mattering and with them the whole round-trip structure.
        /// </summary>
        LightLost = 2,

        /// <summary>Further than <see cref="GameConstants.ClueReadRange"/>.</summary>
        OutOfRange = 3,

        /// <summary>The player moved. A narrow beam cannot be held on a mark while walking.</summary>
        Moved = 4,
    }

    /// <summary>
    /// One tick of one player's situation in front of one clue, as the host measures
    /// it.
    /// <para>
    /// Everything here is a primitive, and none of it is clue content: the reader is
    /// the same code whether it is grinding through a real clue or a decoy, and it
    /// never learns which. Light arrives as a single 0–1 quality so that a flashlight
    /// beam, a zone light and a flare are one rule — the host takes the strongest
    /// source, folding <see cref="Session.IWorldProbe.IsAreaLit"/> in on the way.
    /// </para>
    /// </summary>
    public struct ClueReadContext
    {
        /// <summary>The clue being looked at, or -1 for none.</summary>
        public int ClueId;

        /// <summary>Distance to it, metres. Compared against §03's <see cref="GameConstants.ClueReadRange"/>.</summary>
        public float DistanceToClue;

        /// <summary>The reader's translation speed, m/s. §03 needs the feet still; the head is free.</summary>
        public float ReaderSpeed;

        /// <summary>
        /// Light on the mark, 0–1, from the strongest source. Below
        /// <see cref="GameConstants.ClueMinReadableLightQuality"/> the read is
        /// interrupted, and above it the value still decides how badly the reader
        /// misreads.
        /// </summary>
        public float LightQuality;

        /// <summary>How far out of upright the reader's view is, degrees.</summary>
        public float ViewAngleDegrees;

        /// <summary>Blur the reader contributes — distance, haze, a shaking hand. 0–1.</summary>
        public float Blur;
    }

    /// <summary>
    /// The §03 reading gate: close, still, and lit for
    /// <see cref="GameConstants.ClueReadSeconds"/> without a break.
    /// <para>
    /// This is the system that makes a clue dangerous. §03 hangs the objective and
    /// the danger on the same switch — "목표와 위험이 같은 스위치에 걸린다" — and the
    /// mechanism is exactly this: the beam that makes a mark readable is the beam the
    /// monster sees best (§08), and it has to stay on for seconds while its owner
    /// stands still.
    /// </para>
    /// <para>
    /// Interruption throws the progress away rather than pausing it. That is what
    /// turns a battery from a resource into a threat: flicking the light on for the
    /// last half second does not finish a read, so a player who cannot afford
    /// <see cref="GameConstants.ClueReadSeconds"/> of light cannot read at all, and
    /// §03's "배터리가 떨어지면 단서를 읽을 수 없다" becomes literally true.
    /// </para>
    /// <para>
    /// Holds no clue content, so a client may run one locally to draw its own
    /// progress ring. All it ever learns is how long its own player stood still.
    /// </para>
    /// </summary>
    public sealed class ClueReader
    {
        private int _clueId = -1;
        private float _heldSeconds;
        private float _worstLight = 1f;
        private float _viewAngleDegrees;
        private float _blur;

        /// <summary>Where the current attempt has got to.</summary>
        public ClueReadState State { get; private set; } = ClueReadState.Idle;

        /// <summary>Why the last attempt stopped. Kept after an interrupt so the UI can say why.</summary>
        public ClueReadInterrupt LastInterrupt { get; private set; } = ClueReadInterrupt.None;

        /// <summary>The clue being read, or -1.</summary>
        public int ClueId => _clueId;

        /// <summary>Unbroken seconds held on the mark.</summary>
        public float HeldSeconds => _heldSeconds;

        /// <summary>Progress toward <see cref="GameConstants.ClueReadSeconds"/>, 0–1.</summary>
        public float Progress => MathX.Clamp01(_heldSeconds / GameConstants.ClueReadSeconds);

        /// <summary>
        /// The reader's half of a completed read, for
        /// <see cref="ObjectiveResolver.TryRead"/>.
        /// <para>
        /// Light is reported as the worst the read had to survive, not the average.
        /// A beam that slipped off the mark and came back cost the reader those
        /// frames, and §03's obstacle is the narrow beam itself — averaging would let
        /// a long, mostly-dark read look like a careful one.
        /// </para>
        /// </summary>
        public ClueObservation Observation =>
            new ClueObservation(_heldSeconds, _worstLight, _viewAngleDegrees, _blur);

        /// <summary>
        /// Advances the attempt.
        /// <para>
        /// The gates are checked before any time is credited, so the tick that loses
        /// the light never counts — a read and its interruption arriving in the same
        /// tick resolves as an interruption. That ordering is deliberate: at
        /// <see cref="GameConstants.FixedStep"/> a host can only sample the world 50
        /// times a second, and the alternative would hand players a free read every
        /// time a flicker and a completion landed on the same frame.
        /// </para>
        /// <para>
        /// Conditions are tested in a fixed order — target, light, range, movement —
        /// so a player who is standing in the dark at the far end of the corridor is
        /// always told about the light first. §03 makes darkness the lock; it is the
        /// most useful thing to say.
        /// </para>
        /// <para>
        /// A huge <paramref name="deltaSeconds"/> from a frame spike completes a read
        /// that was otherwise valid, and cannot skip past
        /// <see cref="ClueReadState.Complete"/>, because that state latches until the
        /// host consumes it. A negative delta is treated as zero rather than rewinding
        /// progress.
        /// </para>
        /// </summary>
        public void Tick(float deltaSeconds, ClueReadContext context)
        {
            if (deltaSeconds < 0f || float.IsNaN(deltaSeconds))
            {
                deltaSeconds = 0f;
            }

            if (context.ClueId < 0)
            {
                if (State == ClueReadState.Reading)
                {
                    Fail(ClueReadInterrupt.NoTarget);
                }
                else if (State != ClueReadState.Complete)
                {
                    // An interrupt the player has walked away from becomes Idle; a
                    // completed read is kept until the host takes it.
                    State = ClueReadState.Idle;
                    _clueId = -1;
                    ResetAccumulators();
                }

                return;
            }

            if (context.ClueId != _clueId)
            {
                // A new target always starts from nothing, including when the player
                // switches away from a finished read.
                _clueId = context.ClueId;
                State = ClueReadState.Reading;
                LastInterrupt = ClueReadInterrupt.None;
                ResetAccumulators();
            }
            else if (State == ClueReadState.Complete)
            {
                return;
            }

            if (context.LightQuality < GameConstants.ClueMinReadableLightQuality)
            {
                Fail(ClueReadInterrupt.LightLost);
                return;
            }

            if (context.DistanceToClue > GameConstants.ClueReadRange)
            {
                Fail(ClueReadInterrupt.OutOfRange);
                return;
            }

            if (context.ReaderSpeed > GameConstants.ClueReadStillSpeedThreshold)
            {
                Fail(ClueReadInterrupt.Moved);
                return;
            }

            if (State != ClueReadState.Reading)
            {
                // Resuming after an interrupt. Progress was thrown away when it
                // happened, so this starts the count again from zero.
                State = ClueReadState.Reading;
                LastInterrupt = ClueReadInterrupt.None;
            }

            _heldSeconds += deltaSeconds;
            _worstLight = System.Math.Min(_worstLight, MathX.Clamp01(context.LightQuality));
            _viewAngleDegrees = context.ViewAngleDegrees;
            _blur = System.Math.Max(_blur, MathX.Clamp01(context.Blur));

            if (_heldSeconds >= GameConstants.ClueReadSeconds)
            {
                State = ClueReadState.Complete;
            }
        }

        /// <summary>
        /// Abandons the attempt, or acknowledges a completed one. The host calls this
        /// after taking a read so the next look starts clean.
        /// </summary>
        public void Cancel()
        {
            State = ClueReadState.Idle;
            LastInterrupt = ClueReadInterrupt.None;
            _clueId = -1;
            ResetAccumulators();
        }

        private void Fail(ClueReadInterrupt reason)
        {
            State = ClueReadState.Interrupted;
            LastInterrupt = reason;
            ResetAccumulators();
        }

        private void ResetAccumulators()
        {
            _heldSeconds = 0f;
            _worstLight = 1f;
            _viewAngleDegrees = 0f;
            _blur = 0f;
        }
    }
}
