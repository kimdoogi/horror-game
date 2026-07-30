#nullable enable

using System.Collections.Generic;
using HorrorGame.Core.Math;
using UnityEngine;

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// Steps ① and ② of §13's voice pipeline on the local player: capture, and send —
    /// but only when someone is close enough to hear.
    /// <para>
    /// Put this on the local player's character. It never sends to anyone the roster
    /// does not place within <c>GameConstants.VoiceCutoffDistance</c>, and when that
    /// set is empty it does not merely skip the send — it <b>closes the microphone</b>.
    /// §13's cutoff is 도청 방지, and the sender's contribution to that is refusing to
    /// produce the data at all. It has a second, human benefit: the operating
    /// system's microphone indicator goes dark when nobody could receive anything, so
    /// the game is visibly not listening while a player is alone.
    /// </para>
    /// <para>
    /// The host's relay repeats the same test (<see cref="VoiceRelay"/>) because this
    /// half runs on the speaker's machine and is therefore advisory. Both halves call
    /// <see cref="VoiceAudience.Select"/>; there is no second copy of the rule.
    /// </para>
    /// <para>
    /// Transmission is gated on <see cref="TransmitRequested"/>, which defaults to
    /// open microphone and is meant to be driven by whatever the UI layer decides
    /// push-to-talk is. No input package is referenced here on purpose: §05's controls
    /// are the Input layer's business, and a voice component that reads keys directly
    /// would make voice depend on the project's input handling mode.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceTransmitter : MonoBehaviour
    {
        [Tooltip("Open microphone when true. Set false and drive SetTransmitting() from the UI for push-to-talk.")]
        [SerializeField]
        private bool _openMicrophone = true;

        private readonly List<VoiceListener> _listeners = new List<VoiceListener>();
        private readonly List<NetUserId> _audience = new List<NetUserId>();

        private byte[] _compressed = new byte[VoiceTuning.CompressedFrameCapacity];
        private IVoiceBackend? _backend;
        private IVoiceTransport? _transport;
        private IVoiceRoster? _roster;
        private NetUserId _speaker;
        private float _audienceAge;
        private bool _warnedUnavailable;

        /// <summary>Whether the player is asking to talk right now.</summary>
        public bool TransmitRequested { get; private set; }

        /// <summary>
        /// Whether the microphone is open. False whenever
        /// <see cref="AudienceCount"/> is zero, which is the visible form of §13's
        /// sender-side cutoff.
        /// </summary>
        public bool IsCapturing => _backend != null && _backend.IsCapturing;

        /// <summary>How many players are currently in range to hear this speaker.</summary>
        public int AudienceCount => _audience.Count;

        /// <summary>Frames handed to the transport. Diagnostic for the debug overlay.</summary>
        public int SentFrames { get; private set; }

        /// <summary>
        /// Frames captured and then discarded because the audience emptied between the
        /// capture and the send. Rare, and not an error: it is the cutoff winning a
        /// race.
        /// </summary>
        public int DroppedNoAudience { get; private set; }

        /// <summary>
        /// Wires the transmitter. Called by the Net layer when the local character
        /// spawns, because only it knows the local id and which transport this session
        /// uses.
        /// </summary>
        public void Bind(NetUserId speaker, IVoiceTransport transport, IVoiceRoster? roster = null)
        {
            _speaker = speaker;
            _transport = transport;
            _roster = roster ?? VoiceRoster.Shared;
            _backend = SteamServices.Current.Voice;
            _audience.Clear();
            _audienceAge = float.MaxValue;
        }

        /// <summary>Sets push-to-talk state. Ignored while the open microphone flag is set.</summary>
        public void SetTransmitting(bool transmitting)
        {
            TransmitRequested = transmitting;
        }

        private void OnDisable()
        {
            StopCapture();
            _audience.Clear();
        }

        private void Update()
        {
            var backend = _backend;
            var transport = _transport;
            var roster = _roster;

            if (backend == null || transport == null || roster == null)
            {
                return;
            }

            if (!backend.IsAvailable)
            {
                // §14 step 3 plays with 음성은 디스코드, so an offline build has no
                // codec at all. Say so once and stop touching it.
                if (!_warnedUnavailable)
                {
                    _warnedUnavailable = true;
                    Debug.Log("[Voice] No voice codec on this backend (" + SteamServices.Current.BackendName
                        + "). In-game voice is off; §14 step 3 uses Discord until step 5.");
                }

                return;
            }

            if (!transport.IsReady)
            {
                StopCapture();
                return;
            }

            var speaker = _speaker.IsValid ? _speaker : roster.LocalId;
            RefreshAudience(roster, speaker);

            var wanted = (_openMicrophone || TransmitRequested) && _audience.Count > 0;

            if (!wanted)
            {
                // Not "skip the send" — the microphone closes. Nothing is captured, so
                // there is nothing an attacker could later ask a peer to forward.
                StopCapture();
                return;
            }

            if (!backend.IsCapturing)
            {
                backend.StartCapture();
            }

            DrainCapture(backend, transport, speaker);
        }

        private void RefreshAudience(IVoiceRoster roster, NetUserId speaker)
        {
            _audienceAge += Time.unscaledDeltaTime;
            if (_audienceAge < VoiceTuning.AudienceRefreshSeconds)
            {
                return;
            }

            _audienceAge = 0f;

            // The speaker's own transform, not the roster's copy of it: this component
            // lives on the local character, so the transform is the freshest position
            // available and is not waiting on a network update.
            var position = new Vec3(transform.position.x, transform.position.y, transform.position.z);

            roster.CopyListenersTo(_listeners);
            VoiceAudience.Select(speaker, position, _listeners, _audience);
        }

        private void DrainCapture(IVoiceBackend backend, IVoiceTransport transport, NetUserId speaker)
        {
            // Bounded loop: the codec can have several frames pending after a hitch,
            // and an unbounded drain would let a stall in the audio thread turn into a
            // stall in the frame loop.
            for (var i = 0; i < 4; i++)
            {
                var result = backend.ReadCompressedFrame(_compressed, out var byteCount);

                if (result == VoiceReadResult.BufferTooSmall)
                {
                    if (!GrowCompressedBuffer())
                    {
                        // Already at the ceiling: retrying would read the same oversized
                        // frame every iteration. Give up on this frame instead of spinning.
                        return;
                    }

                    continue;
                }

                if (result != VoiceReadResult.Frame || byteCount <= 0)
                {
                    return;
                }

                if (_audience.Count == 0)
                {
                    DroppedNoAudience++;
                    return;
                }

                SentFrames++;
                transport.SendCapturedFrame(new VoiceFrame(speaker, _compressed, byteCount), _audience);
            }
        }

        /// <summary>Doubles the capture buffer, or returns false at the ceiling.</summary>
        private bool GrowCompressedBuffer()
        {
            if (_compressed.Length >= VoiceTuning.CompressedFrameCapacityLimit)
            {
                return false;
            }

            _compressed = new byte[_compressed.Length * 2];
            Debug.LogWarning("[Voice] Compressed frame buffer grew to " + _compressed.Length
                + " bytes; §13 expects 초당 2~8KB, so a persistent grow means the codec settings changed.");
            return true;
        }

        private void StopCapture()
        {
            if (_backend != null && _backend.IsCapturing)
            {
                _backend.StopCapture();
            }
        }
    }
}
