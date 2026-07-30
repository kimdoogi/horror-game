#nullable enable

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// Buffer sizes and timings for the voice pipeline.
    /// <para>
    /// ARCHITECTURE §2 puts every tuned number in <c>GameConstants</c>, and §13's one
    /// design number here — the 30 m cutoff — is taken from there and only from there
    /// (see <see cref="VoiceAudience.CutoffDistance"/>). The values below are not
    /// design numbers: they are buffer capacities and a poll interval, derived from
    /// the codec's data rate rather than chosen for how the game feels. Nobody
    /// balances a match by changing a ring-buffer length. If one of them ever does
    /// become a tuning knob — a jitter setting exposed to players, say — it moves to
    /// <c>GameConstants</c> with a § citation at that point.
    /// </para>
    /// </summary>
    public static class VoiceTuning
    {
        /// <summary>
        /// Capacity of a compressed-frame buffer, bytes.
        /// <para>
        /// §13 measures the compressed stream at 초당 2~8KB, so 8 KB holds a full
        /// second of the worst case and a single read can never be the thing that
        /// overflows. Sized generously on purpose: one buffer per speaker is 8 KB, and
        /// four players is a rounding error against a 30 s reconnect timeout's worth of
        /// dropped audio.
        /// </para>
        /// </summary>
        public const int CompressedFrameCapacity = 8 * 1024;

        /// <summary>
        /// Ceiling on compressed-buffer growth, bytes. A frame that will not fit in
        /// eight times §13's per-second figure is not a large frame, it is a bug, and
        /// doubling forever would turn it into an out-of-memory crash instead of a log
        /// line.
        /// </summary>
        public const int CompressedFrameCapacityLimit = 64 * 1024;

        /// <summary>
        /// Initial capacity of a decompressed PCM buffer, bytes. 16-bit mono at
        /// Steam's optimal rate is roughly 48 KB/s, so this is about a third of a
        /// second — comfortably more than one frame, and the buffer grows itself if a
        /// backend ever disagrees.
        /// </summary>
        public const int PcmFrameCapacity = 16 * 1024;

        /// <summary>
        /// Hard ceiling on PCM buffer growth, bytes. Growth exists to absorb a codec
        /// whose frame size we guessed wrong, not to let a malformed packet talk us
        /// into an unbounded allocation.
        /// </summary>
        public const int PcmFrameCapacityLimit = 512 * 1024;

        /// <summary>
        /// Playback ring-buffer length, seconds. Long enough to ride out a network
        /// hiccup, short enough that recovering from one does not leave a speaker
        /// permanently talking half a second in the past — which would break §04's
        /// 청음사 and the whole 말로 전달 loop in §03.
        /// </summary>
        public const float PlaybackBufferSeconds = 0.5f;

        /// <summary>
        /// Audio to accumulate before a speaker's clip starts playing, seconds. Without
        /// it, playback starts on the first frame and underruns on the second, which
        /// sounds like clicking rather than like speech.
        /// </summary>
        public const float PlaybackPrimeSeconds = 0.08f;

        /// <summary>
        /// Silence after which a speaker's playback stops and its buffer is dropped,
        /// seconds. Also how the cutoff sounds when someone walks out of range: their
        /// frames stop arriving and the tail plays out.
        /// </summary>
        public const float PlaybackIdleTimeoutSeconds = 0.5f;

        /// <summary>
        /// Radius within which a speaker plays at full volume, metres. Beyond it the
        /// engine's rolloff takes over — §13's 핵심 트릭. Small, because a value close
        /// to the cutoff would flatten the distance cue that §04's 청음사 and every
        /// "where are you?" exchange depend on.
        /// </summary>
        public const float PlaybackFullVolumeRadius = 1.5f;

        /// <summary>
        /// How often the sender recomputes who can hear it, seconds. Every frame would
        /// be free at four players; a fixed interval is used anyway so the cost stays
        /// flat if a future mode adds spectators to the roster. At §05's top speed
        /// (RunnerSprintSpeed) a 100 ms interval is under a metre of movement, far
        /// inside the 30 m cutoff.
        /// </summary>
        public const float AudienceRefreshSeconds = 0.1f;
    }
}
