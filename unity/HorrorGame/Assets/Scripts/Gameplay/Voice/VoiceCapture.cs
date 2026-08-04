#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Voice;
using HorrorGame.Gameplay.Match;
using Mirror;
using UnityEngine;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// The speaker's half: §13's steps ① 캡처 and ② 압축 전송, plus the line that makes
    /// speaking a game action instead of a chat feature.
    /// <para>
    /// <b>Two things leave this class and only one of them is audio.</b> The frames go to
    /// the host, and <see cref="VoiceEffort"/> goes to <see cref="MatchDirector"/>, which
    /// hands it to §06's creature as a noise. <see cref="VoiceRules"/> is explicit that
    /// this is the reason to have voice in this game at all — a shout reaches the creature
    /// from 1.4× further than it reaches the person you are shouting at — so a build where
    /// the frames worked and this line did not would be half the mechanic, working
    /// silently. The report is asserted in <c>VoiceTests</c> for exactly that reason.
    /// </para>
    /// <para>
    /// <b>§06 hears the key, not the socket.</b> The effort reported to the director is
    /// what the player is asking for, whether or not anybody is in range and whether or
    /// not this machine even has a microphone. That is not a shortcut: a runner who shouts
    /// alone in a corridor has still made a noise in a corridor, and the creature is
    /// standing in the same building. Gating it on having an audience would mean the
    /// safest place to shout is where nobody can hear you, which inverts the design.
    /// </para>
    /// <para>
    /// <b>Push to talk, and the default is Silent.</b> There is no open microphone. §01's
    /// three efforts are a decision the player makes every time — <c>MatchDirector</c>'s
    /// own note says "Silent when nobody is holding the key" — and an open microphone
    /// would report <c>Talk</c> for the whole descent and keep the creature permanently
    /// alerted. <see cref="VoicePushToTalk"/> drives it from the keyboard; anything else
    /// can call <see cref="SetEffort"/>, and no input package is referenced here on
    /// purpose, because §05's controls belong to the input layer.
    /// </para>
    /// <para>
    /// <b>Nobody in range means the microphone closes.</b> Not "skip the send" — §13's
    /// cutoff is 도청 방지 and the sender's contribution to it is refusing to produce the
    /// data at all. There is a second, human benefit: the operating system's recording
    /// indicator goes dark whenever nobody could receive anything, so the game is visibly
    /// not listening while a runner is alone. This half runs on the speaker's machine and
    /// is therefore advisory; <see cref="VoiceHostRelay"/> repeats the test with
    /// authoritative positions.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Voice/Voice Capture")]
    public sealed class VoiceCapture : MonoBehaviour
    {
        /// <summary>
        /// Mirror's reserved id for the host's own local connection, and therefore the
        /// speaker id a host stamps on its own frames.
        /// <para>
        /// Zero is not a guess: <c>NetSocketTests</c> asserts that a remote client is never
        /// given it — "Connection id 0 is Mirror's reserved id for the host's own local
        /// connection" — so a frame stamped 0 is unambiguously the host's, including on a
        /// server that is running without a window open.
        /// </para>
        /// </summary>
        public const int HostSpeakerConnectionId = 0;

        /// <summary>
        /// Seconds between recomputing who is close enough to hear.
        /// <para>
        /// Every frame would be affordable at §11's twenty, but a fixed interval keeps the
        /// cost flat if the field ever grows. At §05's top speed this is under 6 cm of
        /// movement against <see cref="VoiceRules.WhisperRange"/>'s four metres, so the
        /// answer is never a meaningful distance stale.
        /// </para>
        /// </summary>
        public const float AudienceRefreshSeconds = 0.1f;

        /// <summary>
        /// Loudness below which a frame is not sent, 0~1 RMS.
        /// <para>
        /// A push-to-talk key held through a pause still captures room tone, and sending it
        /// costs the full <see cref="VoiceCodec.BytesPerSecondPerStream"/> to say nothing.
        /// The threshold is set below a whisper at arm's length on a consumer headset
        /// (roughly 0.01 RMS) and above the noise floor of the same hardware, so it drops
        /// silence and never clips the first syllable of a word.
        /// </para>
        /// </summary>
        public const float SilenceRmsThreshold = 0.004f;

        private readonly byte[] _frame = new byte[MaxFrameBytes];

        /// <summary>
        /// Ceiling on one encoded frame, bytes. §13 budgets 초당 2~8KB, so 8 KB holds a
        /// whole second of the worst case and a single read can never be what overflows.
        /// </summary>
        private const int MaxFrameBytes = 8 * 1024;

        private IVoiceLine? _line;
        private IVoicePositions? _positions;
        private VoiceHostRelay? _relay;
        private MatchDirector? _director;

        private float _audienceAge = float.MaxValue;
        private float _nextDirectorSearch;
        private bool _hasAudience;
        private bool _reportedUnavailable;

        /// <summary>What the player is asking for. Set by input; <see cref="VoiceEffort.Silent"/> by default.</summary>
        public VoiceEffort RequestedEffort { get; private set; } = VoiceEffort.Silent;

        /// <summary>Whether the microphone is open right now.</summary>
        public bool IsCapturing => _line != null && _line.IsCapturing;

        /// <summary>Whether anybody was in range at the last refresh. The visible form of the cutoff.</summary>
        public bool HasAudience => _hasAudience;

        /// <summary>
        /// Frames handed to the wire or to the relay <em>this session</em>.
        /// <para>
        /// Session-scoped, and <see cref="Bind"/> is where it is zeroed. There is exactly
        /// one <c>VoiceCapture</c> for the life of the process —
        /// <c>VoiceRuntime.TearDownIfIdle</c> disables the speaker rather than destroying
        /// it, and says why — so without that reset this counter is a process total wearing
        /// the name of a session statistic. It is quoted as a bandwidth claim, and a
        /// bandwidth claim measured over "every match since the game launched" cannot be
        /// acted on by anybody.
        /// </para>
        /// </summary>
        public int SentFrames { get; private set; }

        /// <summary>Payload bytes this machine has sent upstream this session. The bandwidth claim, measured.</summary>
        public long SentBytes { get; private set; }

        /// <summary>Frames captured and dropped as silence this session. See <see cref="SilenceRmsThreshold"/>.</summary>
        public int DroppedSilent { get; private set; }

        /// <summary>The <see cref="MatchDirector"/> being told about §06, or null while none is in the scene.</summary>
        public MatchDirector? Director => _director;

        /// <summary>The chosen line, or null before <see cref="Bind"/>.</summary>
        public IVoiceLine? Line => _line;

        /// <summary>
        /// Wires the capture. Called by <see cref="VoiceRuntime"/>, which is the only thing
        /// that knows which line this session chose and which relay the host is running.
        /// </summary>
        /// <param name="line">The codec and microphone.</param>
        /// <param name="positions">Who is where.</param>
        /// <param name="relay">The host's relay, on a host. Null on a client.</param>
        public void Bind(IVoiceLine line, IVoicePositions positions, VoiceHostRelay? relay)
        {
            _line = line;
            _positions = positions;
            _relay = relay;
            _audienceAge = float.MaxValue;

            // A new session, so the meters read zero. See SentFrames for why this is not
            // tidying: the component outlives every session it serves.
            SentFrames = 0;
            SentBytes = 0;
            DroppedSilent = 0;
        }

        /// <summary>
        /// Sets how hard the player is speaking. <see cref="VoiceEffort.Silent"/> releases
        /// the key.
        /// </summary>
        /// <param name="effort">§01's three, or Silent.</param>
        public void SetEffort(VoiceEffort effort)
        {
            RequestedEffort = effort;
        }

        private void OnDisable()
        {
            _line?.StopCapture();
            _hasAudience = false;
            ReportToDirector(VoiceEffort.Silent);
        }

        private void Update()
        {
            var effort = RequestedEffort;

            // §06 first, and unconditionally. See the class remarks: the creature hears the
            // room, not the network, so this line must not be behind any of the gates below.
            ReportToDirector(effort);

            var line = _line;
            var positions = _positions;
            if (line == null || positions == null)
            {
                return;
            }

            if (!line.IsAvailable)
            {
                if (!_reportedUnavailable)
                {
                    _reportedUnavailable = true;
                    VoiceLines.Report(line);
                }

                return;
            }

            if (effort == VoiceEffort.Silent)
            {
                CloseMicrophone();
                return;
            }

            RefreshAudience(positions, VoiceRules.ClearRange(effort));

            if (!_hasAudience)
            {
                CloseMicrophone();
                return;
            }

            if (!line.IsCapturing)
            {
                line.StartCapture();
            }

            Drain(line, effort);
        }

        /// <summary>
        /// Asks whether anybody is close enough, at <see cref="AudienceRefreshSeconds"/>.
        /// </summary>
        private void RefreshAudience(IVoicePositions positions, float clearRange)
        {
            _audienceAge += Time.unscaledDeltaTime;
            if (_audienceAge < AudienceRefreshSeconds)
            {
                return;
            }

            _audienceAge = 0f;

            if (clearRange <= 0f)
            {
                _hasAudience = false;
                return;
            }

            // This component lives on the local rig, so its own transform is the freshest
            // position available and is not waiting on a network update.
            _hasAudience = positions.AnyListenerWithin(transform.position, clearRange);
        }

        /// <summary>
        /// Drains the line and puts frames on the wire.
        /// <para>
        /// Bounded, for the reason <see cref="MicrophoneVoiceLine.MaxFramesPerDrain"/>
        /// gives: a stall in the audio device must not become a stall in the frame loop
        /// while somebody is being chased.
        /// </para>
        /// </summary>
        private void Drain(IVoiceLine line, VoiceEffort effort)
        {
            for (var i = 0; i < MicrophoneVoiceLine.MaxFramesPerDrain; i++)
            {
                var bytes = line.ReadEncodedFrame(_frame);
                if (bytes <= 0)
                {
                    return;
                }

                if (IsSilent(line))
                {
                    DroppedSilent++;
                    continue;
                }

                var payload = new System.ArraySegment<byte>(_frame, 0, bytes);

                if (NetworkServer.active)
                {
                    // The host does not send itself a datagram. §13 has no dedicated
                    // server, so the host is one of the runners and its own frames enter
                    // the relay directly — one hop instead of two, and the same gate.
                    _relay?.Accept(
                        HostSpeakerConnectionId,
                        effort,
                        line.Codec,
                        payload,
                        Time.unscaledTimeAsDouble);
                }
                else if (NetworkClient.isConnected)
                {
                    NetworkClient.Send(
                        new VoiceUpstreamMessage
                        {
                            Codec = (byte)line.Codec,
                            Effort = (byte)effort,
                            Payload = payload,
                        },
                        VoiceWire.Channel);
                }
                else
                {
                    return;
                }

                SentFrames++;
                SentBytes += bytes;
            }
        }

        /// <summary>
        /// Whether the frame just read is quiet enough to skip.
        /// <para>
        /// The level comes from <see cref="IVoiceLine.LastFrameRms"/>, measured by the line
        /// while the PCM was already in hand. A line that cannot say — Steam's, whose
        /// frames are opaque — returns −1 and this stands aside rather than guessing; see
        /// that property for the version of this gate that was wrong and why.
        /// </para>
        /// </summary>
        private static bool IsSilent(IVoiceLine line)
        {
            var level = line.LastFrameRms;
            return level >= 0f && level < SilenceRmsThreshold;
        }

        private void CloseMicrophone()
        {
            _hasAudience = false;
            _line?.StopCapture();
        }

        /// <summary>
        /// Hands the effort to <see cref="MatchDirector"/>, finding one if it has not
        /// already.
        /// <para>
        /// Polled rather than resolved once, because the director arrives with the
        /// descent's scene and this component can outlive a scene load.
        /// <c>FindFirstObjectByType</c> is the same search <c>MatchDirector.ResolveWiring</c>
        /// and <c>NetPlayerBridge</c> already use, at the same cadence
        /// <c>NetLocalRunner</c> polls for a rig.
        /// </para>
        /// </summary>
        private void ReportToDirector(VoiceEffort effort)
        {
            var director = _director;

            if (director == null)
            {
                if (Time.unscaledTime < _nextDirectorSearch)
                {
                    return;
                }

                _nextDirectorSearch = Time.unscaledTime + (1f / GameConstants.NetworkSendRate);
                director = FindFirstObjectByType<MatchDirector>();
                _director = director;

                if (director == null)
                {
                    return;
                }
            }

            director.VoiceEffort = effort;
        }
    }
}
