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

        /// <summary>Caught. §02 탈락 — no revival, and §09 turns them into a spectator.</summary>
        Eliminated = 2,
    }

    /// <summary>One runner's standing.</summary>
    public readonly struct Racer
    {
        /// <summary>Builds a standing.</summary>
        public Racer(int id, RacerStatus status, int storey, float elapsedSeconds, int place)
        {
            Id = id;
            Status = status;
            Storey = storey;
            ElapsedSeconds = elapsedSeconds;
            Place = place;
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

            _racers[id] = new Racer(id, RacerStatus.Running, storey, elapsedSeconds, 0);
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
            _racers[id] = new Racer(id, RacerStatus.Finished, racer.Storey, elapsedSeconds, _finishers);
            return _finishers;
        }

        /// <summary>
        /// A runner has been caught. §02 탈락 — out, and unranked.
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

            _racers[id] = new Racer(id, RacerStatus.Eliminated, racer.Storey, elapsedSeconds, 0);
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
