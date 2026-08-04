#nullable enable

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// A microphone and a codec: §13's steps ① 캡처 and ③ 압축해제, and deliberately
    /// nothing else.
    /// <para>
    /// <b>The proximity rule is not here.</b> §13's pipeline is four steps — 캡처 → 압축
    /// 전송 → 압축해제 → 3D 오디오 소스 — and only the first and third are platform work.
    /// Who can hear whom is <see cref="HorrorGame.Core.Voice.VoiceRules"/>, applied by
    /// <see cref="VoiceHostRelay"/> and <see cref="VoicePlayback"/>. Keeping the rule out
    /// of the line is what stops a second platform from having to re-implement the one
    /// part of the pipeline that is a game decision — the same split
    /// <c>IVoiceBackend</c> already makes on the Steam side.
    /// </para>
    /// <para>
    /// Buffers are supplied by the caller and reused, for the reason
    /// <see cref="VoiceCodec"/> gives.
    /// </para>
    /// </summary>
    public interface IVoiceLine
    {
        /// <summary>What to call this in a log line. "Microphone", "Steam", "Injected".</summary>
        string Name { get; }

        /// <summary>Which codec <see cref="ReadEncodedFrame"/> produces and <see cref="Decode"/> accepts.</summary>
        VoiceCodecId Codec { get; }

        /// <summary>
        /// Whether this machine can actually capture and encode. False on a headless host
        /// with no input device, and false on an offline build with no Steam codec — in
        /// both cases voice is genuinely off rather than broken, and the caller says so
        /// once and stops asking.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Sample rate of the PCM <see cref="Decode"/> produces, hertz. Zero when unavailable.</summary>
        int SampleRate { get; }

        /// <summary>Whether the microphone is open right now.</summary>
        bool IsCapturing { get; }

        /// <summary>
        /// RMS, 0~1, of the PCM the last <see cref="ReadEncodedFrame"/> encoded, or −1 when
        /// this line cannot say.
        /// <para>
        /// It exists so <see cref="VoiceCapture"/>'s silence gate can be honest. The gate's
        /// first version read the predictor out of the frame header — the encoder's
        /// starting sample — which is a defensible proxy for a level and is wrong for
        /// exactly the signal voice is made of: a periodic waveform crosses zero twice a
        /// cycle, so a 300 Hz vowel framed on a zero crossing would be measured as silence
        /// and dropped. A line that has already touched the PCM can report its level for
        /// nothing; one that never sees PCM (Steam's codec is opaque, and suppresses
        /// silence itself) returns −1 and the gate stands aside.
        /// </para>
        /// </summary>
        float LastFrameRms { get; }

        /// <summary>
        /// Opens the microphone. Idempotent. Called only when somebody is close enough to
        /// hear — see <see cref="VoiceCapture"/> — which is also why the operating system's
        /// recording indicator stays dark while a runner is alone in a corridor.
        /// </summary>
        void StartCapture();

        /// <summary>Closes the microphone. Idempotent.</summary>
        void StopCapture();

        /// <summary>
        /// Drains at most one encoded frame into <paramref name="destination"/>.
        /// </summary>
        /// <param name="destination">Receives the frame. Must hold at least <see cref="VoiceCodec.FrameBytes"/>.</param>
        /// <returns>Bytes written, or 0 when there is nothing pending.</returns>
        int ReadEncodedFrame(byte[] destination);

        /// <summary>
        /// Decodes one frame to −1~1 float PCM at <see cref="SampleRate"/>, multiplying
        /// every sample by <paramref name="gain"/>.
        /// </summary>
        /// <param name="frame">Encoded bytes.</param>
        /// <param name="frameBytes">Valid length within <paramref name="frame"/>.</param>
        /// <param name="destination">Receives the samples.</param>
        /// <param name="destinationOffset">First sample index to write.</param>
        /// <param name="gain">Linear gain from <c>VoiceRules.Gain</c>. See <see cref="VoiceCodec.Decode"/> for why it is applied here.</param>
        /// <returns>Samples written, or 0 when the frame could not be decoded.</returns>
        int Decode(byte[] frame, int frameBytes, float[] destination, int destinationOffset, float gain);
    }
}
