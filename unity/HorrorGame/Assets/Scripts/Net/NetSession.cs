#nullable enable

using System;

namespace HorrorGame.Net
{
    /// <summary>
    /// Where a session is in its life. §13 gives the host authority and does not
    /// migrate it, so this is a strictly forward machine: there is no state that
    /// means "waiting for a new host".
    /// </summary>
    public enum NetSessionPhase
    {
        /// <summary>Nothing running. Main menu.</summary>
        Offline,

        /// <summary>
        /// Four seats and five roles, nobody underground yet. §11's choice happens
        /// here and only here — the missing role is the match's character, so it
        /// cannot be changed once the clock starts (§07).
        /// </summary>
        Lobby,

        /// <summary>The match is running. Late joins are refused from this point.</summary>
        InMatch,

        /// <summary>
        /// The session is over and the connection is being torn down. §13's
        /// 호스트 이탈 → 세션 종료 lands here for everyone, host included.
        /// </summary>
        Ending,
    }

    /// <summary>
    /// Why a session stopped. The UI shows a different sentence for each, which is
    /// the whole reason they are distinguished: "the host left" and "you lost
    /// connection" are the same event from the socket's point of view and two very
    /// different things to be told.
    /// </summary>
    public enum NetSessionEndReason
    {
        /// <summary>Not ended.</summary>
        None,

        /// <summary>The local player asked to leave.</summary>
        LocalRequest,

        /// <summary>
        /// The host quit, crashed or lost its connection. §13: "호스트 이탈 —
        /// 세션 종료. 마이그레이션 하지 않음." There is no promotion path; every
        /// client is returned to the menu.
        /// </summary>
        HostLeft,

        /// <summary>The match reached one of §02's outcomes.</summary>
        MatchOver,

        /// <summary>The transport failed in a way the session could not continue through.</summary>
        TransportError,
    }

    /// <summary>
    /// The one place anything outside the Net layer learns that a session started,
    /// changed phase or ended.
    /// <para>
    /// Static because there is exactly one session per process — §13's model is one
    /// host, four players, no lobbies running in parallel — and because the UI that
    /// has to react to <see cref="NetSessionEndReason.HostLeft"/> is usually a scene
    /// away from the <see cref="HorrorGameNetworkManager"/> that noticed it.
    /// </para>
    /// </summary>
    public static class NetSession
    {
        private static NetSessionPhase _phase = NetSessionPhase.Offline;

        /// <summary>Current phase. Never null, never absent — <see cref="NetSessionPhase.Offline"/> is a real state.</summary>
        public static NetSessionPhase Phase => _phase;

        /// <summary>Why the last session ended, or <see cref="NetSessionEndReason.None"/>.</summary>
        public static NetSessionEndReason LastEndReason { get; private set; } = NetSessionEndReason.None;

        /// <summary>
        /// The seed §11's lobby agreed on, or 0 before one is agreed.
        /// <para>
        /// <b>Pushed in rather than read out.</b> <c>RaceLobby</c> owns the number and
        /// lives in the gameplay assembly, which this one cannot reference — the
        /// dependency only runs the other way. So the lobby hands it over at the moment
        /// it is settled, and the Net layer treats it as read-only.
        /// </para>
        /// <para>
        /// It is here for §01's starting line: the field is dealt from a shuffle of B1's
        /// ring, and every peer has to draw the same shuffle. The agreed seed is the only
        /// number all of them already share.
        /// </para>
        /// </summary>
        public static int AgreedSeed { get; private set; }

        /// <summary>Records the seed §11's lobby settled on. Host and client both call it.</summary>
        /// <param name="seed">The agreed seed. 0 means none.</param>
        public static void SetAgreedSeed(int seed)
        {
            AgreedSeed = seed;
        }

        /// <summary>Raised after <see cref="Phase"/> changes. Carries the new phase.</summary>
        public static event Action<NetSessionPhase>? PhaseChanged;

        /// <summary>
        /// Raised once when a session ends, before the phase returns to
        /// <see cref="NetSessionPhase.Offline"/>, so a listener can still read what
        /// was running.
        /// </summary>
        public static event Action<NetSessionEndReason>? Ended;

        /// <summary>Moves the session to a new phase. Idempotent.</summary>
        internal static void SetPhase(NetSessionPhase phase)
        {
            if (_phase == phase)
            {
                return;
            }

            _phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        /// <summary>
        /// Records the end of a session and notifies listeners. Called from both the
        /// host's own shutdown and a client's "the host went away" path, because §13
        /// makes those the same outcome.
        /// </summary>
        internal static void RaiseEnded(NetSessionEndReason reason)
        {
            LastEndReason = reason;
            SetPhase(NetSessionPhase.Ending);
            Ended?.Invoke(reason);
            SetPhase(NetSessionPhase.Offline);
        }

        /// <summary>
        /// Drops every listener and returns to <see cref="NetSessionPhase.Offline"/>.
        /// Tests need this: static event subscriptions outlive a PlayMode run and
        /// would otherwise carry one test's handlers into the next.
        /// </summary>
        public static void ResetForTests()
        {
            PhaseChanged = null;
            Ended = null;
            LastEndReason = NetSessionEndReason.None;
            _phase = NetSessionPhase.Offline;
        }
    }
}
