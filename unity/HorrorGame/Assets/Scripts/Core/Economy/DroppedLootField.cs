using System;
using System.Collections.Generic;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// Every pile of loot lying on the basement floor.
    /// <para>
    /// §08's 사망자의 전리품 rule: a dead player's loot "떨어진다", and "회수하려면
    /// 시체가 있는 곳으로 돌아가야 한다 — 위험을 감수할 또 하나의 이유". The same
    /// field holds a chest two players abandoned mid-corridor, because from the
    /// team's point of view those are the same problem: value they can see and
    /// cannot reach cheaply.
    /// </para>
    /// <para>
    /// Reachability is asked of <see cref="IWorldProbe"/> rather than measured
    /// straight-line, because §12's whole argument is that path length and sight
    /// line diverge — a pile 5 m away through a wall is a two-minute round trip.
    /// A probe that reports nothing reachable is a normal answer here, not an
    /// error: the body may be behind a door the Engineer locked.
    /// </para>
    /// </summary>
    public sealed class DroppedLootField
    {
        private readonly List<Vec3> _positions = new List<Vec3>();
        private readonly List<LootId[]> _piles = new List<LootId[]>();

        /// <summary>How many piles are on the floor.</summary>
        public int PileCount => _piles.Count;

        /// <summary>Credits currently lying on the floor. §08's second reason to go back in.</summary>
        public int TotalValue
        {
            get
            {
                var total = 0;
                for (var i = 0; i < _piles.Count; i++)
                {
                    total += LootCatalogue.ValueOf(_piles[i]);
                }

                return total;
            }
        }

        /// <summary>Where a pile is.</summary>
        /// <exception cref="ArgumentOutOfRangeException">No such pile.</exception>
        public Vec3 PositionOf(int pileIndex)
        {
            RequireIndex(pileIndex);
            return _positions[pileIndex];
        }

        /// <summary>What is in a pile.</summary>
        /// <exception cref="ArgumentOutOfRangeException">No such pile.</exception>
        public IReadOnlyList<LootId> PiecesOf(int pileIndex)
        {
            RequireIndex(pileIndex);
            return _piles[pileIndex];
        }

        /// <summary>What a pile is worth.</summary>
        /// <exception cref="ArgumentOutOfRangeException">No such pile.</exception>
        public int ValueOf(int pileIndex)
        {
            RequireIndex(pileIndex);
            return LootCatalogue.ValueOf(_piles[pileIndex]);
        }

        /// <summary>
        /// Puts pieces on the floor. Returns the new pile's index, or -1 when there
        /// was nothing to drop — an empty-handed death leaves no pile, so the team is
        /// not sent back for nothing.
        /// </summary>
        public int Drop(Vec3 position, IReadOnlyList<LootId>? pieces)
        {
            if (pieces == null || pieces.Count == 0)
            {
                return -1;
            }

            var copy = new LootId[pieces.Count];
            for (var i = 0; i < pieces.Count; i++)
            {
                copy[i] = pieces[i];
            }

            _positions.Add(position);
            _piles.Add(copy);
            return _piles.Count - 1;
        }

        /// <summary>
        /// Empties a player's pockets onto the floor where they fell. §08's death
        /// rule, and §09's cruelty: the ghost can see the pile and cannot say a word
        /// about it.
        /// </summary>
        public int DropFrom(Inventory? owner, Vec3 position)
        {
            if (owner == null)
            {
                return -1;
            }

            return Drop(position, owner.DropAll());
        }

        /// <summary>
        /// The nearest pile that can actually be walked to.
        /// <para>
        /// Returns false with <paramref name="pileIndex"/> −1 and
        /// <paramref name="distance"/> infinite when the floor is bare or when the
        /// probe can reach none of it. Ties go to the older pile, so the answer is
        /// stable across a seeded replay.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="probe"/> is null.</exception>
        public bool TryFindNearestRecoverable(IWorldProbe probe, Vec3 from, out int pileIndex, out float distance)
        {
            if (probe == null)
            {
                throw new ArgumentNullException(nameof(probe));
            }

            pileIndex = -1;
            distance = float.PositiveInfinity;

            for (var i = 0; i < _piles.Count; i++)
            {
                var d = probe.NavigableDistance(from, _positions[i]);
                if (float.IsNaN(d) || float.IsInfinity(d) || d < 0f)
                {
                    continue;
                }

                if (d < distance)
                {
                    distance = d;
                    pileIndex = i;
                }
            }

            if (pileIndex < 0)
            {
                distance = float.PositiveInfinity;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Picks a pile up whole, as far as the taker's capacity allows, and returns
        /// what would not fit. §08's greed rule does not stop applying at a corpse:
        /// recovering a dead player's haul can put the recoverer into the next
        /// weight band, and anything left over stays on the floor.
        /// </summary>
        public IReadOnlyList<LootId> Recover(int pileIndex, Inventory taker)
        {
            RequireIndex(pileIndex);
            if (taker == null)
            {
                throw new ArgumentNullException(nameof(taker));
            }

            var pile = _piles[pileIndex];
            var leftOver = new List<LootId>();
            for (var i = 0; i < pile.Length; i++)
            {
                if (!taker.TryAdd(pile[i]))
                {
                    leftOver.Add(pile[i]);
                }
            }

            if (leftOver.Count == 0)
            {
                _piles.RemoveAt(pileIndex);
                _positions.RemoveAt(pileIndex);
            }
            else
            {
                _piles[pileIndex] = leftOver.ToArray();
            }

            return leftOver;
        }

        /// <summary>Removes a pile without giving it to anyone — the match ended, or §02's wipe took everything.</summary>
        public void Clear()
        {
            _piles.Clear();
            _positions.Clear();
        }

        private void RequireIndex(int pileIndex)
        {
            if (pileIndex < 0 || pileIndex >= _piles.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pileIndex), pileIndex, "No such loot pile. PileCount is " + _piles.Count + ".");
            }
        }
    }
}
