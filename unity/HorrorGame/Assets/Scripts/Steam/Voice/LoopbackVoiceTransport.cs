#nullable enable

using System;
using System.Collections.Generic;

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// A voice transport for a session with no network: the offline and single-machine
    /// case from §14 steps 2 and 3.
    /// <para>
    /// It is a real implementation, not a null object. There is genuinely nowhere for a
    /// frame to go when the local player is the only peer in the process, so the honest
    /// behaviour is to consume frames and count them — and that is enough to exercise
    /// the whole gate: the audience is computed, the microphone opens and closes with
    /// range, and <see cref="SentFrames"/> shows whether §13's cutoff is doing anything.
    /// </para>
    /// <para>
    /// <see cref="EchoToSelf"/> turns it into a microphone check by delivering the
    /// local player's own frames back as received frames. That is the one case where
    /// hearing yourself is correct, and it is opt-in so it can never be mistaken for
    /// normal behaviour.
    /// </para>
    /// <para>
    /// Note what it still refuses to do: <see cref="SendCapturedFrame"/> ignores an
    /// empty audience, and <see cref="RelayFrame"/> only echoes when the local player is
    /// actually on the recipient list. The stand-in obeys the same rule as the real
    /// transport, so code developed against it cannot acquire a habit that leaks voice
    /// once Steam is wired in.
    /// </para>
    /// </summary>
    public sealed class LoopbackVoiceTransport : IVoiceTransport
    {
        private readonly IVoiceRoster _roster;

        /// <summary>Builds a loopback transport over the shared roster unless one is supplied.</summary>
        public LoopbackVoiceTransport(IVoiceRoster? roster = null)
        {
            _roster = roster ?? VoiceRoster.Shared;
        }

        /// <summary>
        /// Whether the local player's own frames are played back locally. Off by
        /// default; a microphone-check screen turns it on.
        /// </summary>
        public bool EchoToSelf { get; set; }

        /// <summary>Always ready: there is no connection to wait for.</summary>
        public bool IsReady => true;

        /// <summary>
        /// Always the host. §13's model has the host running the match, and in a
        /// single-process session the local player is trivially it.
        /// </summary>
        public bool IsHost => true;

        /// <summary>Frames accepted for sending. Zero for a whole session means the gate never opened.</summary>
        public int SentFrames { get; private set; }

        /// <summary>Frames refused because the audience was empty — §13's cutoff, counted.</summary>
        public int RefusedNoAudience { get; private set; }

        /// <inheritdoc />
        public event Action<VoiceFrame>? FrameReceived;

        /// <summary>
        /// Never raised. A loopback session has no clients, so no frame ever needs the
        /// host's re-gate; the event exists to satisfy the interface and stays silent so
        /// that a <see cref="VoiceRelay"/> attached to it is harmlessly idle.
        /// <para>
        /// Written with empty accessors rather than as a field-like event because it can
        /// never fire: storing the handler would keep a subscriber alive to be called by
        /// nothing, and the compiler is right to warn about the field-like form here.
        /// </para>
        /// </summary>
        public event Action<VoiceFrame>? FrameNeedsRelay
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public void SendCapturedFrame(VoiceFrame frame, IReadOnlyList<NetUserId> audience)
        {
            if (!frame.HasAudio || audience == null || audience.Count == 0)
            {
                RefusedNoAudience++;
                return;
            }

            SentFrames++;

            if (EchoToSelf)
            {
                FrameReceived?.Invoke(frame);
            }
        }

        /// <inheritdoc />
        public void RelayFrame(VoiceFrame frame, IReadOnlyList<NetUserId> recipients)
        {
            if (!frame.HasAudio || recipients == null)
            {
                return;
            }

            if (!EchoToSelf)
            {
                return;
            }

            var local = _roster.LocalId;
            for (var i = 0; i < recipients.Count; i++)
            {
                if (recipients[i] == local)
                {
                    FrameReceived?.Invoke(frame);
                    return;
                }
            }
        }
    }
}
