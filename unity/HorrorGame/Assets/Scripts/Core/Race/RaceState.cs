using System;
using System.Collections.Generic;

namespace HorrorGame.Core.Race
{
    /// <summary>What a runner is doing.</summary>
    public enum RacerStatus
    {
        /// <summary>Still descending.</summary>
        Running = 0,

        /// <summary>Reached the middle of the deepest storey. §02 승리 or 완주.</summary>
        Finished = 1,

        /// <summary>
        /// Out of the race for good — a seat that emptied, not a runner the creature
        /// caught.
        /// <para>
        /// Being caught used to land here. It no longer does: §06's creature sends a
        /// runner back to their starting place on B1 and they keep running (see
        /// <see cref="RaceState.ReportCaught"/>). Nothing in the game eliminates a
        /// player any more; this is what a disconnect resolves to, so
        /// <see cref="RaceState.Over"/> can still come true when somebody quits.
        /// </para>
        /// </summary>
        Eliminated = 2,
    }

    /// <summary>One runner's standing.</summary>
    public readonly struct Racer
    {
        /// <summary>Builds a standing.</summary>
        public Racer(int id, RacerStatus status, int storey, float elapsedSeconds, int place, int timesCaught = 0)
        {
            Id = id;
            Status = status;
            Storey = storey;
            ElapsedSeconds = elapsedSeconds;
            Place = place;
            TimesCaught = timesCaught;
        }

        /// <summary>Seat index. §13 — the host owns this list and clients only read it.</summary>
        public int Id { get; }

        /// <summary>Running, finished or out.</summary>
        public RacerStatus Status { get; }

        /// <summary>Deepest storey reached. 0 is B1 and <see cref="RaceState.Storeys"/> − 1 is the bottom.</summary>
        public int Storey { get; }

        /// <summary>Seconds from the start to whatever ended this runner's race, or to now.</summary>
        public float ElapsedSeconds { get; }

        /// <summary>1 for the winner, 2 for the next finisher, and so on. 0 while still running or if eliminated.</summary>
        public int Place { get; }

        /// <summary>
        /// How many times §06's creature has sent this runner back to B1.
        /// <para>
        /// Not a penalty — the eight storeys they have to descend again are the penalty.
        /// It is here so the standings can say why somebody who was on B6 a minute ago is
        /// on B1 now, which otherwise reads as a bug, and so a match report can tell a
        /// clean run apart from one that was caught four times and still won.
        /// </para>
        /// </summary>
        public int TimesCaught { get; }
    }

    /// <summary>
    /// §02, as the only thing that decides who won.
    /// <para>
    /// <b>Why this is a rule and not a scoreboard.</b> §13 makes the host authoritative, and a
    /// race is the shape of game where that matters most: "I touched it first" is the single
    /// most attractive thing in the build to lie about. So arrival is a call into this type,
    /// on the host, in the order the host processed it — and every client renders a standing
    /// it was told rather than one it computed.
    /// </para>
    /// <para>
    /// <b>Finishing is not the same as winning, and that is deliberate.</b> §02 records a place
    /// for everybody who reaches the bottom. Without it, second position is worth exactly as
    /// much as last and a player who loses the lead on B3 has five storeys of nothing to play
    /// for. With it, third can still take second by shutting a door, which is the behaviour
    /// the whole map is built to make possible.
    /// </para>
    /// <para>
    /// <b>Elimination has no place.</b> A runner who is caught is out, not ranked — §01 makes
    /// the creature the reason to be afraid rather than a scoring element, and a game that
    /// ranked corpses by how deep they got would reward dying in the right order.
    /// </para>
    /// <para>
    /// Engine-free and deterministic, so the simulator can run ten thousand of these.
    /// </para>
    /// </summary>
    public sealed class RaceState
    {
        private readonly Racer[] _racers;
        private int _finishers;

        /// <summary>Storeys in the building. The bottom one carries the finish.</summary>
        public const int Storeys = 8;

        /// <summary>Starts a race with <paramref name="runners"/> on the rim of B1.</summary>
        /// <param name="runners">2~20. §11.</param>
        /// <exception cref="ArgumentOutOfRangeException">Fewer than two, or more than the cap.</exception>
        public RaceState(int runners)
        {
            if (runners < GameConstants.RaceRunnersMin || runners > GameConstants.RaceRunnersMax)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(runners),
                    runners,
                    "§11 puts the field at " + GameConstants.RaceRunnersMin + "~" + GameConstants.RaceRunnersMax
                    + ". One runner is not a race, and the map's gate counts are fixed rather than scaled, "
                    + "so a field over the cap would queue at the inner gate rather than contest it.");
            }

            _racers = new Racer[runners];
            for (var i = 0; i < runners; i++)
            {
                _racers[i] = new Racer(i, RacerStatus.Running, 0, 0f, 0);
            }
        }

        /// <summary>How many started.</summary>
        public int Count
        {
            get { return _racers.Length; }
        }

        /// <summary>How many have reached the bottom.</summary>
        public int Finishers
        {
            get { return _finishers; }
        }

        /// <summary>True once nobody is still running — everyone has finished or is out.</summary>
        public bool Over
        {
            get
            {
                for (var i = 0; i < _racers.Length; i++)
                {
                    if (_racers[i].Status == RacerStatus.Running)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>The winner's id, or −1 if nobody has finished.</summary>
        public int WinnerId
        {
            get
            {
                for (var i = 0; i < _racers.Length; i++)
                {
                    if (_racers[i].Place == 1)
                    {
                        return _racers[i].Id;
                    }
                }

                return -1;
            }
        }

        /// <summary>Reads a standing.</summary>
        /// <param name="id">Seat index.</param>
        public Racer this[int id]
        {
            get { return _racers[id]; }
        }

        /// <summary>
        /// A runner has dropped to <paramref name="storey"/>.
        /// <para>
        /// Monotonic: §12's chutes are one-way and a runner can only ever be deeper than they
        /// were. A report that goes backwards is dropped rather than trusted, because on the
        /// host the only thing that can produce one is a client that should not be believed.
        /// </para>
        /// </summary>
        /// <param name="id">Seat index.</param>
        /// <param name="storey">Storey they landed on.</param>
        /// <param name="elapsedSeconds">Seconds since the start.</param>
        /// <returns>True if this moved them.</returns>
        public bool ReportDescent(int id, int storey, float elapsedSeconds)
        {
            var racer = _racers[id];
            if (racer.Status != RacerStatus.Running || storey <= racer.Storey || storey >= Storeys)
            {
                return false;
            }

            _racers[id] = new Racer(id, RacerStatus.Running, storey, elapsedSeconds, 0, racer.TimesCaught);
            return true;
        }

        /// <summary>
        /// A runner has touched the finish in the middle of the deepest storey.
        /// </summary>
        /// <param name="id">Seat index.</param>
        /// <param name="elapsedSeconds">Seconds since the start. Becomes their time.</param>
        /// <returns>Their place — 1 for the winner — or 0 if the arrival was not accepted.</returns>
        public int ReportFinish(int id, float elapsedSeconds)
        {
            var racer = _racers[id];
            if (racer.Status != RacerStatus.Running)
            {
                return 0;
            }

            // You have to have got there. Accepting a finish from somebody who never reached
            // the bottom storey would make the whole descent skippable by one bad packet, and
            // this is the one call in the game worth lying about.
            if (racer.Storey != Storeys - 1)
            {
                return 0;
            }

            _finishers++;
            _racers[id] = new Racer(id, RacerStatus.Finished, racer.Storey, elapsedSeconds, _finishers, racer.TimesCaught);
            return _finishers;
        }

        /// <summary>
        /// §06's creature caught a runner. They go back to where they started and keep
        /// running.
        /// <para>
        /// <b>This replaces elimination, and the reason is the whole shape of the game.</b>
        /// A first-past-the-post race that removes people leaks players: caught on B2 of a
        /// twenty-minute match and you have nothing to do but watch, so the design grew a
        /// spectator seat to make being out bearable. Sending a runner back to B1 costs
        /// them everything they had — eight storeys, every gate, all of it — without
        /// taking the game away, which is a bigger punishment and a smaller one at the
        /// same time.
        /// </para>
        /// <para>
        /// <b>It is also the answer to a creature nobody feared.</b> §12's 주자 테스트
        /// measured every place on the map as escapable, and while being caught was
        /// elimination that produced a monster people simply avoided and forgot. A hazard
        /// you can outrun is only frightening if being caught costs something you can
        /// still lose — and after B6 that is a very great deal.
        /// </para>
        /// <para>
        /// <b>The storey goes back to 0, not down by one.</b> §01 puts the 투하구 at every
        /// middle and lands you on the rim below, so a floor is only ever entered from its
        /// own rim. There is no way back UP a chute, so a runner returned to B4 would be
        /// standing somewhere the map has no route to. B1's rim is the one place in the
        /// building that a runner is allowed to be without having fallen into it.
        /// </para>
        /// <para>
        /// Resetting <see cref="Racer.Storey"/> is also what lets them descend again:
        /// <see cref="ReportDescent"/> is monotonic and would refuse every drop from B2
        /// down if the record still said B6.
        /// </para>
        /// </summary>
        /// <param name="id">Seat index.</param>
        /// <param name="elapsedSeconds">Seconds since the start.</param>
        /// <returns>
        /// True if this sent them back; false if they had already finished or left, both of
        /// which are past caring what the creature does.
        /// </returns>
        public bool ReportCaught(int id, float elapsedSeconds)
        {
            var racer = _racers[id];
            if (racer.Status != RacerStatus.Running)
            {
                return false;
            }

            _racers[id] = new Racer(
                id,
                RacerStatus.Running,
                0,
                elapsedSeconds,
                0,
                racer.TimesCaught + 1);

            return true;
        }

        /// <summary>
        /// A seat emptied — somebody quit or dropped. Out, and unranked.
        /// <para>
        /// No longer what happens when the creature catches you; see
        /// <see cref="ReportCaught"/>. It survives because <see cref="Over"/> counts
        /// runners who are still Running, and a seat nobody is sitting in would keep a
        /// finished race open forever.
        /// </para>
        /// </summary>
        /// <param name="id">Seat index.</param>
        /// <param name="elapsedSeconds">Seconds since the start.</param>
        /// <returns>True if this eliminated them; false if they were already out or home.</returns>
        public bool ReportEliminated(int id, float elapsedSeconds)
        {
            var racer = _racers[id];
            if (racer.Status != RacerStatus.Running)
            {
                return false;
            }

            _racers[id] = new Racer(id, RacerStatus.Eliminated, racer.Storey, elapsedSeconds, 0, racer.TimesCaught);
            return true;
        }

        /// <summary>
        /// The standings a HUD shows: everyone still running, deepest first, then by how long
        /// they have been on that storey — which is the closest thing to a live position a
        /// race down a maze has, because two runners on the same floor may be nowhere near
        /// each other and neither of them knows it.
        /// </summary>
        public IReadOnlyList<Racer> Standings()
        {
            var live = new List<Racer>(_racers.Length);
            for (var i = 0; i < _racers.Length; i++)
            {
                if (_racers[i].Status == RacerStatus.Running)
                {
                    live.Add(_racers[i]);
                }
            }

            live.Sort((a, b) =>
            {
                var byStorey = b.Storey.CompareTo(a.Storey);
                return byStorey != 0 ? byStorey : a.ElapsedSeconds.CompareTo(b.ElapsedSeconds);
            });

            return live;
        }

        /// <summary>The finishers in order, winner first.</summary>
        public IReadOnlyList<Racer> Results()
        {
            var done = new List<Racer>(_finishers);
            for (var i = 0; i < _racers.Length; i++)
            {
                if (_racers[i].Status == RacerStatus.Finished)
                {
                    done.Add(_racers[i]);
                }
            }

            done.Sort((a, b) => a.Place.CompareTo(b.Place));
            return done;
        }
    }
}
