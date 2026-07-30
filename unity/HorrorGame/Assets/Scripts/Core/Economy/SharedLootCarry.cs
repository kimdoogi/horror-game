using System;
using System.Collections.Generic;

namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// What happened to an oversize piece when a carrier let go.
    /// <para>
    /// §08 builds its "최고의 순간" on two players walking a 궤짝 down a narrow
    /// corridor while the monster arrives, so the interesting frame is the one
    /// where someone lets go mid-stride. These are the only four things that can
    /// happen, and each one is a different sentence over voice chat.
    /// </para>
    /// </summary>
    public enum CarryReleaseOutcome
    {
        /// <summary>That player was not holding it. Nothing changed.</summary>
        NotACarrier = 0,

        /// <summary>
        /// The other carrier took the whole weight. Their band recomputes on the
        /// same frame, so the slowdown lands while they are still moving.
        /// </summary>
        TransferredToSoleCarrier = 1,

        /// <summary>
        /// The remaining carrier had no room for the rest of it, so it hit the
        /// floor. §08's greed dilemma from the other side: the player who was
        /// already full loses the piece the moment their partner steps away.
        /// </summary>
        DroppedTooHeavyForSoleCarrier = 2,

        /// <summary>The last carrier let go. It is on the floor where it was dropped, waiting for another trip.</summary>
        LeftOnTheFloor = 3,
    }

    /// <summary>
    /// One oversize 전리품 being carried by one or two players.
    /// <para>
    /// §08 gives 대형 초상화·궤짝 the rule "2인 운반 or 극심한 감속" and states the
    /// row exists to produce a specific scene. That makes the carry a small state
    /// machine rather than an inventory flag: the weight has to move between two
    /// players' loads as they join and leave, and a partner letting go must resolve
    /// on the frame it happens — no interpolation, nothing that could be missed by
    /// a slow tick.
    /// </para>
    /// <para>
    /// Carriers are identified by their <see cref="Inventory"/> instance rather than
    /// by a player id. The economy has no notion of a player (§08's wallet rule
    /// removes it), and reference identity is exactly the "these hands" question
    /// being asked.
    /// </para>
    /// </summary>
    public sealed class SharedLootCarry
    {
        private readonly List<Inventory> _carriers;
        private int _shareEach;

        /// <summary>
        /// Puts a piece on the floor, uncarried.
        /// </summary>
        /// <param name="loot">The piece. §08 allows co-carrying only what its table marks "2인 운반".</param>
        /// <exception cref="ArgumentException">The piece is not co-carryable, so the caller has confused it with pocketable loot.</exception>
        public SharedLootCarry(LootId loot)
        {
            var definition = LootCatalogue.Of(loot);
            if (!definition.AllowsSharedCarry)
            {
                throw new ArgumentException(
                    "Only loot that §08 marks '2인 운반' can be shared — " + loot
                    + " is pocketable, so it belongs in Inventory.TryAdd.",
                    nameof(loot));
            }

            Loot = loot;
            TotalWeight = definition.Weight;
            Value = definition.Value;
            _carriers = new List<Inventory>(GameConstants.SharedCarryMaxCarriers);
        }

        /// <summary>The piece being carried.</summary>
        public LootId Loot { get; }

        /// <summary>Its full weight — <see cref="GameConstants.LootWeightLargePiece"/> for §08's chest.</summary>
        public int TotalWeight { get; }

        /// <summary>Credits it fetches at the vehicle, whoever walked it out.</summary>
        public int Value { get; }

        /// <summary>How many players have hold of it right now, 0–<see cref="GameConstants.SharedCarryMaxCarriers"/>.</summary>
        public int CarrierCount => _carriers.Count;

        /// <summary>Whether anyone is holding it.</summary>
        public bool IsCarried => _carriers.Count > 0;

        /// <summary>Whether it is sitting on the floor, waiting for a trip that may never happen.</summary>
        public bool IsOnFloor => _carriers.Count == 0 && !IsSold;

        /// <summary>
        /// Whether it has been loaded into the vehicle and paid for. A sold piece
        /// cannot be picked up or sold again — §08 pays "가치만큼" once, and a second
        /// payment for the same chest would be an infinite credit loop.
        /// </summary>
        public bool IsSold { get; private set; }

        /// <summary>
        /// Weight each carrier is bearing. The full weight when one player has it,
        /// and the halves rounded up when two do — rounding up rather than down so
        /// splitting a piece can never make weight disappear from the team's total.
        /// </summary>
        public int WeightPerCarrier => _shareEach;

        /// <summary>
        /// The speed both carriers are limited to: the slower of the two.
        /// <para>
        /// A rigid object between two people cannot move faster than whoever is
        /// struggling most, and §08's corridor scene depends on that — the pair is
        /// as slow as its most loaded member, which is why the decision of <em>who</em>
        /// takes the other handle matters. Returns 1 when nobody is carrying, so a
        /// caller can multiply unconditionally.
        /// </para>
        /// </summary>
        public float PairSpeedMultiplier
        {
            get
            {
                if (_carriers.Count == 0)
                {
                    return 1f;
                }

                var slowest = float.PositiveInfinity;
                for (var i = 0; i < _carriers.Count; i++)
                {
                    var m = _carriers[i].SpeedMultiplier;
                    if (m < slowest)
                    {
                        slowest = m;
                    }
                }

                return slowest;
            }
        }

        /// <summary>Whether these particular hands are on it.</summary>
        public bool IsCarriedBy(Inventory carrier) => carrier != null && _carriers.Contains(carrier);

        /// <summary>
        /// Takes hold of the piece, alone or as the second pair of hands.
        /// <para>
        /// Refused when the piece already has its two carriers, when this player is
        /// already one of them, when their hands are full (§03's 양손 rule — the
        /// objective and an oversize piece cannot share a player), or when their
        /// remaining capacity cannot take the share. A refusal changes nothing at
        /// all: the piece stays exactly as it was.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="carrier"/> is null.</exception>
        public bool TryPickUp(Inventory carrier)
        {
            if (carrier == null)
            {
                throw new ArgumentNullException(nameof(carrier));
            }

            if (IsSold || _carriers.Count >= GameConstants.SharedCarryMaxCarriers || _carriers.Contains(carrier))
            {
                return false;
            }

            var newShare = ShareFor(_carriers.Count + 1);
            if (!carrier.TryOccupyHands(newShare))
            {
                return false;
            }

            // Existing carriers get lighter, never heavier, so this cannot fail
            // partway and leave the piece half-held.
            for (var i = 0; i < _carriers.Count; i++)
            {
                _carriers[i].ShrinkHandLoad(_shareEach - newShare);
            }

            _carriers.Add(carrier);
            _shareEach = newShare;
            return true;
        }

        /// <summary>
        /// Lets go. This is the frame §08 wrote the row for, so the outcome is
        /// explicit rather than a bool.
        /// <para>
        /// When a partner remains, the whole weight moves onto them immediately —
        /// their <see cref="Inventory.SpeedMultiplier"/> and
        /// <see cref="Inventory.CanSprint"/> are correct on the same frame, with no
        /// easing that a large frame delta could skip past. If it no longer fits,
        /// the piece is on the floor and both players are free; a survivor is never
        /// left holding an impossible load.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="carrier"/> is null.</exception>
        public CarryReleaseOutcome Release(Inventory carrier)
        {
            if (carrier == null)
            {
                throw new ArgumentNullException(nameof(carrier));
            }

            var index = _carriers.IndexOf(carrier);
            if (index < 0)
            {
                return CarryReleaseOutcome.NotACarrier;
            }

            carrier.ReleaseHands();
            _carriers.RemoveAt(index);

            if (_carriers.Count == 0)
            {
                _shareEach = 0;
                return CarryReleaseOutcome.LeftOnTheFloor;
            }

            var remaining = _carriers[0];
            var newShare = ShareFor(_carriers.Count);
            if (!remaining.TryGrowHandLoad(newShare - _shareEach))
            {
                remaining.ReleaseHands();
                _carriers.Clear();
                _shareEach = 0;
                return CarryReleaseOutcome.DroppedTooHeavyForSoleCarrier;
            }

            _shareEach = newShare;
            return CarryReleaseOutcome.TransferredToSoleCarrier;
        }

        /// <summary>
        /// Everyone lets go at once — the monster is here and nobody is negotiating.
        /// Idempotent, so a death and a manual drop landing on the same frame cannot
        /// double-release the weight.
        /// </summary>
        public void DropToFloor()
        {
            for (var i = 0; i < _carriers.Count; i++)
            {
                _carriers[i].ReleaseHands();
            }

            _carriers.Clear();
            _shareEach = 0;
        }

        /// <summary>
        /// Hands the piece over to the vehicle. Internal because <see cref="Shop"/>
        /// owns the credit side of §08 — and because the guard against paying twice
        /// for one chest has to live with the piece, not with whoever asked.
        /// </summary>
        internal bool TrySell()
        {
            if (IsSold)
            {
                return false;
            }

            DropToFloor();
            IsSold = true;
            return true;
        }

        /// <summary>
        /// What each carrier would bear if <paramref name="carrierCount"/> players
        /// held it. Public because the pickup prompt has to show the second player
        /// what joining would cost them before they commit.
        /// </summary>
        public int ShareFor(int carrierCount)
        {
            if (carrierCount <= 0)
            {
                return 0;
            }

            if (carrierCount > GameConstants.SharedCarryMaxCarriers)
            {
                carrierCount = GameConstants.SharedCarryMaxCarriers;
            }

            // Ceiling division: two players sharing weight 5 bear 3 each, never 2,
            // so the team's total load never shrinks by being split.
            return ((TotalWeight - 1) / carrierCount) + 1;
        }
    }
}
