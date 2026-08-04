#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Race;

namespace HorrorGame.Net
{
    /// <summary>
    /// The last standings the host sent, on a machine that is not the host. §02 · §13.
    /// <para>
    /// <b>It computes nothing.</b> Every value on this class was read out of a
    /// <see cref="RaceStandingsMessage"/>: the rows, the order the rows are drawn in, the
    /// clock, the winner and whether the race is over. There is deliberately no method here
    /// that advances anybody — no <c>ReportDescent</c>, no <c>ReportFinish</c>, nothing a
    /// client could call to move itself down a storey. §02: "도착 판정을 클라이언트가 내리면
    /// 경주 게임에서 가장 먼저 조작되는 값이 된다." The only way a row changes is a frame
    /// arriving over the wire.
    /// </para>
    /// <para>
    /// <b>Why it is not a <c>RaceState</c>.</b> <c>RaceState</c> is the rule — it decides
    /// places, refuses a backwards descent and counts finishers — and there must be exactly
    /// one of those in a match, on the host. Handing a client a second one would give it a
    /// thing that can answer §02's questions on its own, which is precisely the shape of
    /// bug this whole task exists to remove. This holds <see cref="Racer"/> structs, which
    /// are readings, and nothing that produces them.
    /// </para>
    /// <para>
    /// <b>Frames are dropped rather than merged.</b> A frame older than the newest one seen
    /// is ignored outright — see <see cref="RaceStandingsMessage.Frame"/>. There is no
    /// per-field merge because there are no per-field updates: the host sends the whole
    /// table, so the newest frame is the standings and an older one has nothing to add.
    /// </para>
    /// </summary>
    public sealed class NetRaceStandings
    {
        /// <summary>Seat-indexed rows, exactly as the last frame carried them.</summary>
        private Racer[] _rows = Array.Empty<Racer>();

        /// <summary>The live runners in the host's order, rebuilt on each frame so a read allocates nothing.</summary>
        private readonly List<Racer> _live = new List<Racer>(GameConstants.RaceRunnersMax);

        /// <summary>The newest frame number applied. 0 means nothing has arrived.</summary>
        public uint Frame { get; private set; }

        /// <summary>
        /// How many frames this machine has applied.
        /// <para>
        /// Public because a test needs to be able to say "the standings on this machine
        /// changed because a message arrived", and a count is the only evidence of that
        /// which cannot also be produced by something writing the rows directly. Nothing in
        /// the game reads it.
        /// </para>
        /// </summary>
        public int FramesApplied { get; private set; }

        /// <summary>How many frames were dropped for being older than one already applied.</summary>
        public int FramesDropped { get; private set; }

        /// <summary>True once the host has sent anything at all.</summary>
        public bool HasFrame
        {
            get { return Frame != 0u; }
        }

        /// <summary>How many started. §11's 2~20, as the host sized it.</summary>
        public int RunnerCount
        {
            get { return _rows.Length; }
        }

        /// <summary>Seconds since the start, on the host's clock.</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>§02 승리 — the winner's seat, or −1.</summary>
        public int WinnerId { get; private set; } = -1;

        /// <summary>True once nobody is still running.</summary>
        public bool Over { get; private set; }

        /// <summary>
        /// The live runners in the host's own order — deepest first. The list is reused
        /// between frames, so a HUD may read it every refresh and must not hold it across
        /// one. <c>RaceDirector.Standings</c> already carries the same warning for the same
        /// reason.
        /// </summary>
        public IReadOnlyList<Racer> Standings
        {
            get { return _live; }
        }

        /// <summary>
        /// One seat's row. Returns a default <see cref="Racer"/> for a seat outside the
        /// field, which is what a machine with no seat in this race has to read.
        /// </summary>
        /// <param name="seat">Seat index.</param>
        public Racer RowOf(int seat)
        {
            return seat >= 0 && seat < _rows.Length ? _rows[seat] : default;
        }

        /// <summary>
        /// Takes a frame from the host.
        /// <para>
        /// The rows are trusted verbatim — they came from the one machine §13 says decides —
        /// but the <em>shape</em> is not: a length past §11's ceiling, an order entry naming
        /// a seat that is not in the field, or a null array are all refused rather than
        /// indexed with. That is not distrust of the host; it is that a malformed frame
        /// would otherwise throw inside Mirror's message pump, and an exception there takes
        /// the whole client's message handling down rather than one standing.
        /// </para>
        /// </summary>
        /// <param name="frame">The message as it arrived.</param>
        /// <returns>False if the frame was stale or malformed, in which case nothing changed.</returns>
        public bool Apply(RaceStandingsMessage frame)
        {
            var rows = frame.Rows;
            if (rows == null || rows.Length < GameConstants.RaceRunnersMin
                || rows.Length > GameConstants.RaceRunnersMax)
            {
                return false;
            }

            if (frame.Frame != 0u && frame.Frame <= Frame)
            {
                FramesDropped++;
                return false;
            }

            if (_rows.Length != rows.Length)
            {
                _rows = new Racer[rows.Length];
            }

            for (var seat = 0; seat < rows.Length; seat++)
            {
                var row = rows[seat];
                _rows[seat] = new Racer(
                    seat,
                    StatusOf(row.Status),
                    row.Storey,
                    row.ElapsedSeconds,
                    row.Place,
                    row.TimesCaught);
            }

            _live.Clear();

            var order = frame.LiveOrder;
            if (order != null)
            {
                for (var i = 0; i < order.Length; i++)
                {
                    var seat = order[i];
                    if (seat < _rows.Length)
                    {
                        _live.Add(_rows[seat]);
                    }
                }
            }

            Frame = frame.Frame;
            ElapsedSeconds = frame.ElapsedSeconds;
            WinnerId = frame.WinnerId;
            Over = frame.Over;
            FramesApplied++;
            return true;
        }

        /// <summary>Forgets the race. A session ending, and tests between fixtures.</summary>
        public void Clear()
        {
            _rows = Array.Empty<Racer>();
            _live.Clear();
            Frame = 0u;
            FramesApplied = 0;
            FramesDropped = 0;
            ElapsedSeconds = 0f;
            WinnerId = -1;
            Over = false;
        }

        /// <summary>
        /// A status byte back to §02's word for it. Anything the host does not have a name
        /// for reads as Running, which is the only value that keeps a runner on the board:
        /// a row this build cannot interpret must not be quietly turned into an elimination.
        /// </summary>
        private static RacerStatus StatusOf(byte status)
        {
            switch (status)
            {
                case (byte)RacerStatus.Finished:
                    return RacerStatus.Finished;

                case (byte)RacerStatus.Eliminated:
                    return RacerStatus.Eliminated;

                default:
                    return RacerStatus.Running;
            }
        }
    }
}
