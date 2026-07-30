#nullable enable

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// Where a speaker's decompressed audio goes: step ④ of §13's pipeline,
    /// 송신자 캐릭터 위치의 3D 오디오 소스로.
    /// <para>
    /// §13 calls this the 핵심 트릭 and it is worth restating, because it is the
    /// reason there is no distance code in the playback path at all:
    /// 음성을 3D 오디오 소스로 재생하면 근접 음성이 자동으로 된다 —
    /// 거리 계산 로직이 필요 없다. 엔진의 3D 오디오가 감쇠와 벽 차폐를 처리한다.
    /// Attenuation and occlusion are the engine's job; the only distance decision the
    /// game makes is §13's transmission cutoff, and that happens before a frame ever
    /// reaches an implementation of this interface.
    /// </para>
    /// <para>
    /// An interface rather than a concrete <c>AudioSource</c> user because §13's
    /// 기술 스택 table leaves the door open — 오디오: Unity 기본 → 필요시 FMOD, judged
    /// on whether §04's 청음사 can tell direction and distance apart. Swapping to FMOD
    /// then means one new implementation of this, and no change to capture, transport,
    /// gating or the codec.
    /// </para>
    /// </summary>
    public interface IPositionalVoiceOutput
    {
        /// <summary>
        /// Prepares for audio at <paramref name="sampleRate"/> hertz, mono. Called
        /// before the first submission and again if the rate ever changes; cheap enough
        /// to call repeatedly with the same value.
        /// </summary>
        void Configure(int sampleRate);

        /// <summary>
        /// Hands over one decompressed frame: 16-bit signed mono PCM,
        /// <paramref name="byteCount"/> bytes of <paramref name="pcm"/>. The buffer is
        /// borrowed and must be consumed or copied before returning.
        /// </summary>
        void SubmitPcm(byte[] pcm, int byteCount);

        /// <summary>
        /// Drops anything buffered and stops. Used when a speaker leaves, dies, or
        /// walks out of §13's cutoff — a stale tail restarting later would play words
        /// from a position the speaker no longer occupies.
        /// </summary>
        void ResetOutput();
    }
}
