#nullable enable

using System;

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// The receive and relay halves of §13's voice pipeline for one match, wired
    /// together so the Net layer has a single thing to start and stop.
    /// <para>
    /// §13 closes by claiming that abstracting voice now makes a later port
    /// 코드 몇 줄 차이. This class is where that claim is cashed: standing voice up for
    /// a session is <c>new VoiceSession(transport, isHost).Start()</c>, and the only
    /// platform-specific object in sight is the transport. Changing platform changes
    /// the transport and the backend registered in <see cref="SteamServices"/>, and
    /// nothing here.
    /// </para>
    /// <para>
    /// The host gets a <see cref="VoiceRelay"/> as well as a
    /// <see cref="VoiceReceiver"/>, because §13 makes the host the authority and
    /// therefore the only participant whose distance check cannot be tampered with.
    /// Clients get a receiver only — they have nothing to forward.
    /// </para>
    /// </summary>
    public sealed class VoiceSession
    {
        private readonly IVoiceTransport _transport;
        private readonly IVoiceRoster _roster;

        /// <summary>Builds a session over a transport. Uses the shared roster unless one is supplied.</summary>
        public VoiceSession(IVoiceTransport transport, IVoiceRoster? roster = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _roster = roster ?? VoiceRoster.Shared;

            Receiver = new VoiceReceiver(_transport, _roster);
            Relay = _transport.IsHost ? new VoiceRelay(_transport, _roster) : null;
        }

        /// <summary>Playback of frames addressed to this peer.</summary>
        public VoiceReceiver Receiver { get; }

        /// <summary>
        /// The host's re-gate, or null on a client. Null is not a missing feature: a
        /// client has no authority to forward anyone's voice.
        /// </summary>
        public VoiceRelay? Relay { get; }

        /// <summary>Whether the session is running.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Starts receiving, and relaying if this peer is the host. Idempotent.</summary>
        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            Receiver.Attach();
            Relay?.Attach();
            IsRunning = true;
        }

        /// <summary>
        /// Stops everything and silences every registered output. §13 ends the session
        /// on host loss rather than migrating, so a stopped session leaves nothing
        /// buffered to resume from.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            Receiver.Detach();
            Relay?.Detach();
            IsRunning = false;

            if (_roster is VoiceRoster roster)
            {
                roster.Clear();
            }
        }
    }
}
