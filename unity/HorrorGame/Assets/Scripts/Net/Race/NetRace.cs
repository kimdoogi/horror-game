#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Race;
using Mirror;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// §02's rule, as much of it as the Net layer is allowed to see. §13.
    /// <para>
    /// <b>Why an interface and not <c>RaceDirector</c> itself.</b> The director lives in
    /// <c>HorrorGame.Gameplay</c>, which references this assembly; the arrow does not run
    /// the other way and must not start to, or Mirror ends up in the layer that decides
    /// who won. So the director implements this and installs itself into
    /// <see cref="NetRace.Authority"/>, exactly as <c>RaceLobby</c> installs itself into
    /// <c>LobbyEntry.Intercept</c> and for the same reason.
    /// </para>
    /// <para>
    /// <b>The two accept methods are the only things on it that change anything, and both
    /// return a verdict.</b> That is the shape §02 asks for: a client's report is a
    /// <em>request</em>, the host's rule is the answer, and the wire layer never learns why
    /// an answer was no — it only counts it.
    /// </para>
    /// </summary>
    public interface IRaceAuthority
    {
        /// <summary>True once a field has been sized. Nothing is broadcast before that.</summary>
        bool Started { get; }

        /// <summary>How many seats §11 sized this race for.</summary>
        int RunnerCount { get; }

        /// <summary>Seconds since the field left the rim of B1, on this machine's clock — which on the host is the clock.</summary>
        float ElapsedSeconds { get; }

        /// <summary>§02 승리 — the winner's seat, or −1.</summary>
        int WinnerId { get; }

        /// <summary>True once nobody is still running.</summary>
        bool Over { get; }

        /// <summary>One seat's standing.</summary>
        /// <param name="seat">Seat index.</param>
        Racer RowOf(int seat);

        /// <summary>The live runners in the rule's own order — deepest first. What <see cref="RaceStandingsMessage.LiveOrder"/> is built from.</summary>
        IReadOnlyList<Racer> Standings { get; }

        /// <summary>
        /// A client says it fell through a 투하구 onto <paramref name="storey"/>.
        /// </summary>
        /// <param name="seat">The seat the host assigned to the connection that said so. Never a number the client chose.</param>
        /// <param name="storey">The storey claimed. 0 is B1.</param>
        /// <returns>True if the rule moved them.</returns>
        bool AcceptDescent(int seat, int storey);

        /// <summary>
        /// A client says §06's creature caught it.
        /// </summary>
        /// <param name="seat">The seat the host assigned to the connection that said so.</param>
        /// <returns>True if the rule sent them back to B1.</returns>
        bool AcceptCaught(int seat);

        /// <summary>
        /// A client says it fired §01's 총 at another runner.
        /// <para>
        /// <b>The distance is deliberately not a parameter.</b> A descent and a catch are
        /// facts about the reporter's own seat, and the worst a liar can do with either is
        /// waste their own race. A shot is the one report in this game that costs somebody
        /// ELSE eight storeys, so nothing in the payload may decide whether it lands: the
        /// shooter's seat is read off the <c>NetPlayer</c> the connection owns, and the
        /// range is measured by the host between the two bodies it tracks. A client that
        /// sends <c>metresApart = 0</c> for a target on another floor gets the same answer
        /// as one that sends the truth.
        /// </para>
        /// </summary>
        /// <param name="shooterSeat">The seat the host assigned to the connection that fired.</param>
        /// <param name="targetSeat">Who the shooter's crosshair was on. Checked, not trusted.</param>
        /// <returns>True if the rule sent the target back to B1.</returns>
        bool AcceptShot(int shooterSeat, int targetSeat);
    }

    /// <summary>
    /// The wire §02 runs on: a client's report going up, and the host's standings coming
    /// back down. §13 — 「순위 · 도착 판정 — 호스트만」.
    /// <para>
    /// <b>The defect this closes.</b> Until now nothing about §02 crossed the wire at all.
    /// <see cref="NetPlayer"/> replicated position, view angles, the flashlight and stamina;
    /// descents, finishes and catches were recorded by each machine's own
    /// <c>MatchDirector</c> into its own <c>RaceState</c>. Twenty machines in one match held
    /// twenty scoreboards, none of them authoritative, and every one of them said the local
    /// player was seat 0. §13 is explicit that this is the one thing in a race worth lying
    /// about, and it was the one thing not implemented.
    /// </para>
    /// <para>
    /// <b>Both directions, and what each one is allowed to carry.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Up</b> — <c>NetPlayer.CmdReportDescent</c> and
    /// <c>CmdReportCaught</c>. A <c>[Command]</c> and not a <c>NetworkMessage</c>, because
    /// Mirror will only deliver one from the connection that <em>owns</em> the object it was
    /// called on: the seat is then a host-assigned field of the very object that proves who
    /// sent it, and there is no seat number in the payload for a client to put somebody
    /// else's in. That is the difference between "I was caught" and "he was caught", and the
    /// second one costs its victim eight storeys.</description></item>
    /// <item><description><b>Down</b> — <see cref="RaceStandingsMessage"/>, broadcast to
    /// every ready connection on every accepted change and once a second besides. See that
    /// type for why it is a message rather than a <c>SyncList</c>.</description></item>
    /// </list>
    /// <para>
    /// <b>Static, like <see cref="NetSession"/> and for the same reason.</b> There is one
    /// race per process, the host's director is a component in the match scene, and the
    /// thing that has to hand a report to it — a <see cref="NetPlayer"/> spawned one scene
    /// earlier — has no path to it that does not go through a static or a scene search. A
    /// scene search would resolve to a different object between the lobby and the descent.
    /// </para>
    /// </summary>
    public static class NetRace
    {
        /// <summary>
        /// Seconds between standings frames when nothing has happened.
        /// <para>
        /// One second, and derived from what the frame is for rather than picked: the only
        /// value on it that changes continuously is the race clock, <c>RaceHud</c> prints
        /// that clock as mm:ss, and one second is exactly its resolution. Faster would send
        /// bytes that redraw the same string; slower and a runner's clock would visibly lag
        /// the person sitting next to them.
        /// </para>
        /// <para>
        /// The heartbeat is not how a descent reaches a screen — that is broadcast the
        /// instant the rule accepts it. It exists so that a client whose race is quiet still
        /// has a live clock and still learns that a frame it never received was superseded.
        /// </para>
        /// </summary>
        public const float HeartbeatSeconds = 1f;

        /// <summary>The last standings the host sent. Written only by <see cref="OnClientStandings"/>.</summary>
        private static readonly NetRaceStandings _standings = new NetRaceStandings();

        /// <summary>Scratch for the row array, reused so a broadcast allocates nothing per frame.</summary>
        private static NetRacerRow[] _rows = Array.Empty<NetRacerRow>();

        /// <summary>Scratch for the order array, same reason.</summary>
        private static byte[] _order = Array.Empty<byte>();

        private static uint _frame;

        /// <summary>
        /// §02's rule on this machine, or null. Set by <c>RaceDirector</c> when it sizes a
        /// field and cleared when the race ends.
        /// <para>
        /// Non-null on a client too, and that is deliberate rather than sloppy: the director
        /// installs itself wherever it runs, and <see cref="ReportDescent"/> below refuses to
        /// touch it unless <c>NetworkServer.active</c>. A seam that were null on a client
        /// would look safer and would hide the one thing worth asserting — that the guard is
        /// the server check and not an accident of which machine happened to set a field.
        /// </para>
        /// </summary>
        public static IRaceAuthority? Authority { get; set; }

        /// <summary>The standings as this machine was last told them. Empty until a frame arrives.</summary>
        public static NetRaceStandings Standings
        {
            get { return _standings; }
        }

        /// <summary>
        /// Whether this machine is allowed to decide §02 — arrivals, the timeout, and when
        /// the race closes.
        /// <para>
        /// <b>"Not a remote client", not "is a server".</b> A solo playtest and every
        /// PlayMode fixture run with no server and no client at all, and gating §02 on
        /// <c>NetworkServer.active</c> alone would mean nobody ever finishes offline. Host
        /// mode reads true on both flags and is a host. The only configuration this excludes
        /// is the one it must: a client with a host on the other end of the wire.
        /// </para>
        /// <para>
        /// <b><c>isConnected</c> and not <c>active</c>, and it is a real difference.</b>
        /// <c>NetworkClient.active</c> goes true the instant <c>Connect</c> is called and
        /// stays true through a failed handshake and until <c>Shutdown</c>; a fixture that
        /// leaked one, or a menu that tried to join and could not, would leave every later
        /// offline race unable to judge its own finish — a green suite and an unwinnable
        /// game, which is this repository's signature failure. <c>isConnected</c> is
        /// <c>connectState == Connected</c>, which is exactly "somebody else is the host".
        /// </para>
        /// </summary>
        public static bool ThisMachineJudges
        {
            get { return NetworkServer.active || !NetworkClient.isConnected; }
        }

        /// <summary>Frames the host has broadcast this session. A test's evidence that the host answered.</summary>
        public static int FramesBroadcast { get; private set; }

        /// <summary>Descent reports the rule accepted. §12's chutes are one-way, so this is also the count of storeys the field has fallen.</summary>
        public static int DescentsAccepted { get; private set; }

        /// <summary>
        /// Descent reports the rule turned down.
        /// <para>
        /// Not an error counter. A refusal is the normal outcome of the two things that are
        /// supposed to be refused — a claim that skips a storey, and a duplicate report from
        /// a runner already recorded there — so this rising during a match is the guard
        /// working, and it rising to the same number as <see cref="DescentsAccepted"/> is a
        /// client that has stopped agreeing with the host about where it is.
        /// </para>
        /// </summary>
        public static int DescentsRefused { get; private set; }

        /// <summary>Catch reports the rule accepted. §02 — back to B1, still racing.</summary>
        public static int CatchesAccepted { get; private set; }

        /// <summary>Catch reports the rule turned down — a seat that had already finished or left.</summary>
        public static int CatchesRefused { get; private set; }

        /// <summary>§01's 총 shots the host's rule accepted — each one is a runner sent back to B1's rim.</summary>
        public static int ShotsLanded { get; private set; }

        /// <summary>
        /// Shots the rule turned down: out of range as the HOST measured it, no such seat,
        /// a runner who is not Running, or the shooter shooting themselves. Like
        /// <see cref="DescentsRefused"/> this is the guard working, not an error — most
        /// shots in a race are meant to miss.
        /// </summary>
        public static int ShotsRefused { get; private set; }

        /// <summary>
        /// Teaches this machine's client to take the host's standings.
        /// <para>
        /// Registered from <c>HorrorGameNetworkManager.OnStartClient</c> rather than once at
        /// load, because <c>NetworkClient.Shutdown</c> clears the handler table — anything
        /// registered before a session is gone by the second one. <c>NetRunner</c>'s spawn
        /// handler and <c>RaceLobby.RegisterClientHandlers</c> already carry the same scar.
        /// </para>
        /// </summary>
        public static void InstallClient()
        {
            _standings.Clear();
            NetworkClient.ReplaceHandler<RaceStandingsMessage>(OnClientStandings);
        }

        /// <summary>Forgets the handler and the last standings. A session ending, and tests between fixtures.</summary>
        public static void UninstallClient()
        {
            NetworkClient.UnregisterHandler<RaceStandingsMessage>();
            _standings.Clear();
        }

        /// <summary>
        /// Starts a session's frame numbering. Called when the server comes up.
        /// <para>
        /// There is no server-side <em>handler</em> to register: everything a client says
        /// about §02 arrives as a <c>[Command]</c> on the runner it owns, which Mirror
        /// dispatches from the identity rather than from a table. That asymmetry is the
        /// authority model showing through — a client may only speak about the one object
        /// the host gave it.
        /// </para>
        /// </summary>
        public static void InstallServer()
        {
            _frame = 0u;
            FramesBroadcast = 0;
            DescentsAccepted = 0;
            DescentsRefused = 0;
            CatchesAccepted = 0;
            CatchesRefused = 0;
            ShotsLanded = 0;
            ShotsRefused = 0;
        }

        /// <summary>
        /// A client's descent report, arriving on the host. Host only.
        /// <para>
        /// The server check is written out rather than delegated to Mirror's
        /// <c>[Server]</c> attribute, and that is not a preference. The weaver's injected
        /// guard returns <c>default</c> — <c>false</c> here, which happens to be right —
        /// after a <c>Debug.LogWarning</c>, and a warning is a PlayMode test failure in this
        /// project. A caller that is not the host is not a defect worth a log line; it is a
        /// client, and the answer it wants is "no".
        /// </para>
        /// </summary>
        /// <param name="seat">The seat the host assigned to the reporting connection.</param>
        /// <param name="storey">The storey claimed. 0 is B1.</param>
        /// <returns>True if the rule moved them, in which case the standings have already been broadcast.</returns>
        public static bool ReportDescent(int seat, int storey)
        {
            var authority = Authority;
            if (!NetworkServer.active || authority == null || !authority.Started)
            {
                return false;
            }

            if (!authority.AcceptDescent(seat, storey))
            {
                DescentsRefused++;
                return false;
            }

            DescentsAccepted++;
            Broadcast();
            return true;
        }

        /// <summary>
        /// A client's catch report, arriving on the host. Host only — see
        /// <see cref="ReportDescent"/> for why the guard is written out.
        /// </summary>
        /// <param name="seat">The seat the host assigned to the reporting connection.</param>
        /// <returns>True if the rule sent them back to B1.</returns>
        public static bool ReportCaught(int seat)
        {
            var authority = Authority;
            if (!NetworkServer.active || authority == null || !authority.Started)
            {
                return false;
            }

            if (!authority.AcceptCaught(seat))
            {
                CatchesRefused++;
                return false;
            }

            CatchesAccepted++;
            Broadcast();
            return true;
        }

        /// <summary>§01's 총 hits the host's rule and lands in the standings. Host side.</summary>
        /// <param name="shooterSeat">Read off the NetPlayer the command arrived on — never a number the client chose.</param>
        /// <param name="targetSeat">Who the shooter says they hit. The host checks the range itself.</param>
        /// <returns>True if the rule sent the target back to B1.</returns>
        public static bool ReportShot(int shooterSeat, int targetSeat)
        {
            var authority = Authority;
            if (!NetworkServer.active || authority == null || !authority.Started)
            {
                return false;
            }

            if (!authority.AcceptShot(shooterSeat, targetSeat))
            {
                ShotsRefused++;
                return false;
            }

            ShotsLanded++;
            Broadcast();
            return true;
        }

        /// <summary>
        /// Sends this machine's own shot to the host. Client side; see
        /// <see cref="SendDescent"/> for why it routes through the local player object.
        /// </summary>
        /// <param name="targetSeat">Who the crosshair was on, or −1 for a miss.</param>
        /// <returns>False if this machine has no runner of its own to report through.</returns>
        public static bool SendShot(int targetSeat)
        {
            var local = NetworkClient.localPlayer;
            if (local == null || !local.TryGetComponent(out NetPlayer player))
            {
                return false;
            }

            return player.ReportShotToHost(targetSeat);
        }

        /// <summary>
        /// Sends this machine's own descent to the host. Client side.
        /// <para>
        /// Routed through <c>NetworkClient.localPlayer</c> rather than through a message of
        /// its own, because that identity is the one object Mirror will accept a
        /// <c>[Command]</c> from on this connection — see <see cref="NetPlayer"/>. A machine
        /// with no runner spawned yet cannot report, and returning false is the honest
        /// answer: §11's lobby is one phase before the descent, so there is a real window in
        /// which this is null.
        /// </para>
        /// </summary>
        /// <param name="storey">The storey landed on. 0 is B1.</param>
        /// <returns>False if this machine has no runner of its own to report through.</returns>
        public static bool SendDescent(int storey)
        {
            var local = NetworkClient.localPlayer;
            if (local == null || !local.TryGetComponent(out NetPlayer player))
            {
                return false;
            }

            return player.ReportDescentToHost(storey);
        }

        /// <summary>
        /// Lets the host's own runner report the position it was just moved to, without
        /// §05's speed clamp dragging their avatar there over several seconds.
        /// <para>
        /// The mirror image of the <c>_hasReported = false</c> inside
        /// <c>NetPlayer.CmdReportDescent</c>. A client's descent and catch are commands and
        /// forgive themselves on acceptance; the host takes neither path — its
        /// <c>RaceDirector</c> is the rule — so the host's own body would be the only one
        /// in the session that crawled. A no-op off the wire, which is every solo playtest.
        /// </para>
        /// </summary>
        public static void ForgiveLocalClamp()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            var local = NetworkClient.localPlayer;
            if (local != null && local.TryGetComponent(out NetPlayer player))
            {
                player.ForgiveTheNextReport();
            }
        }

        /// <summary>
        /// Sends this machine's own catch to the host. Client side; see
        /// <see cref="SendDescent"/>.
        /// </summary>
        /// <returns>False if this machine has no runner of its own to report through.</returns>
        public static bool SendCaught()
        {
            var local = NetworkClient.localPlayer;
            if (local == null || !local.TryGetComponent(out NetPlayer player))
            {
                return false;
            }

            return player.ReportCaughtToHost();
        }

        /// <summary>
        /// Sends the standings to everybody. Host only; a no-op anywhere else.
        /// <para>
        /// <c>SendToReady</c> and not <c>SendToAll</c>: a connection that has not sent
        /// <c>ReadyMessage</c> has no spawned objects and no HUD to draw this on, and Mirror
        /// treats ready as the point a connection may be told about the world.
        /// </para>
        /// </summary>
        public static void Broadcast()
        {
            if (!NetworkServer.active || !TryBuildFrame(out var frame))
            {
                return;
            }

            NetworkServer.SendToReady(frame);
            FramesBroadcast++;
        }

        /// <summary>
        /// Sends the current standings to one connection — a runner who has just become
        /// ready and would otherwise see nothing until the next heartbeat.
        /// </summary>
        /// <param name="conn">The connection to catch up.</param>
        public static void SendTo(NetworkConnectionToClient? conn)
        {
            if (conn == null || !NetworkServer.active || !TryBuildFrame(out var frame))
            {
                return;
            }

            conn.Send(frame);
            FramesBroadcast++;
        }

        /// <summary>Clears the seam, the standings and the counters. Session teardown, and tests.</summary>
        public static void ResetForTests()
        {
            Authority = null;
            _standings.Clear();
            _frame = 0u;
            FramesBroadcast = 0;
            DescentsAccepted = 0;
            DescentsRefused = 0;
            CatchesAccepted = 0;
            CatchesRefused = 0;
            ShotsLanded = 0;
            ShotsRefused = 0;
        }

        /// <summary>
        /// Packs the rule's current state into a frame.
        /// <para>
        /// The whole table every time — see <see cref="RaceStandingsMessage"/> for the
        /// arithmetic. The scratch arrays are reused and handed straight to Mirror's
        /// serialiser, which copies them into the write buffer before this returns, so
        /// there is no window in which a caller could hold one.
        /// </para>
        /// </summary>
        private static bool TryBuildFrame(out RaceStandingsMessage frame)
        {
            frame = default;

            var authority = Authority;
            if (authority == null || !authority.Started)
            {
                return false;
            }

            var count = authority.RunnerCount;
            if (count < GameConstants.RaceRunnersMin || count > GameConstants.RaceRunnersMax)
            {
                return false;
            }

            if (_rows.Length != count)
            {
                _rows = new NetRacerRow[count];
            }

            for (var seat = 0; seat < count; seat++)
            {
                var racer = authority.RowOf(seat);
                _rows[seat] = new NetRacerRow
                {
                    Status = (byte)racer.Status,
                    Storey = (byte)Mathf.Clamp(racer.Storey, 0, RaceState.Storeys - 1),
                    Place = (byte)Mathf.Clamp(racer.Place, 0, byte.MaxValue),

                    // Saturating, not wrapping. A count that rolled over to 0 would report
                    // a run that was caught 256 times as a clean one, which is the one
                    // reading of this field nobody could tell was wrong.
                    TimesCaught = (byte)Mathf.Clamp(racer.TimesCaught, 0, byte.MaxValue),
                    ElapsedSeconds = racer.ElapsedSeconds,
                };
            }

            var live = authority.Standings;
            var ordered = live.Count < count ? live.Count : count;

            if (_order.Length != ordered)
            {
                _order = new byte[ordered];
            }

            for (var i = 0; i < ordered; i++)
            {
                _order[i] = (byte)Mathf.Clamp(live[i].Id, 0, count - 1);
            }

            // Wraps at 2^32 frames. At the heartbeat that is 136 years of one session, and
            // NetRaceStandings.Apply treats frame 0 as "unnumbered" rather than as stale, so
            // even the wrap resolves to one accepted frame rather than a stuck client.
            _frame++;

            frame = new RaceStandingsMessage
            {
                Frame = _frame,
                ElapsedSeconds = authority.ElapsedSeconds,
                WinnerId = authority.WinnerId,
                Over = authority.Over,
                Rows = _rows,
                LiveOrder = _order,
            };

            return true;
        }

        /// <summary>
        /// A frame arriving on a client. The one line in the project that writes the
        /// standings on a machine that is not the host.
        /// </summary>
        private static void OnClientStandings(RaceStandingsMessage frame)
        {
            _standings.Apply(frame);
        }
    }
}
