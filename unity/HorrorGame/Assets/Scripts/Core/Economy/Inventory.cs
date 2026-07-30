using System.Collections.Generic;
using System.Globalization;

namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// What one player is carrying, and what that costs them.
    /// <para>
    /// This is the type §08 turns into pressure: "욕심이 곧 속도 저하이고, 속도
    /// 저하가 곧 죽음이다." It owns three separate slots because the design treats
    /// them differently — pocketed loot (capacity-limited), an oversize piece held
    /// in the hands (§08's 2인 운반, tracked by <see cref="SharedLootCarry"/>), and
    /// the objective, which §03 forbids holding alongside any loot at all.
    /// </para>
    /// <para>
    /// Movement never learns about loot. It reads <see cref="SpeedMultiplier"/> as
    /// a float into <c>MovementContext.LoadMultiplier</c>, per the seam rule in
    /// ARCHITECTURE §3. Nothing in this class knows what a metre per second is.
    /// </para>
    /// </summary>
    public sealed class Inventory
    {
        private readonly List<LootId> _loot = new List<LootId>();
        private int _lootWeight;
        private int _handWeight;
        private bool _bagEquipped;
        private bool _carryingObjective;

        /// <summary>An empty-handed player. §08: "1차 잠입 전 — 맨몸으로 들어간다."</summary>
        public Inventory()
        {
        }

        /// <summary>
        /// Total weight units carried: pocketed loot, the share of any oversize
        /// piece in the hands, and the objective if held. This is the number §08's
        /// band table is indexed by.
        /// </summary>
        public int TotalWeight => _lootWeight + _handWeight + (_carryingObjective ? GameConstants.ObjectiveWeight : 0);

        /// <summary>
        /// The §08 load penalty, band times bag, ready to hand to movement as
        /// <c>MovementContext.LoadMultiplier</c>.
        /// <para>
        /// Includes the bag's −10%, because §08 lists the strap's cost beside the
        /// capacity it buys and nothing else in the codebase owns that pairing. A
        /// consumer that <em>also</em> applies a penalty from
        /// <c>MovementContext.BagEquipped</c> would charge it twice — use
        /// <see cref="BandMultiplier"/> there instead.
        /// </para>
        /// <para>
        /// Deliberately excludes the objective's own −20% (§03): movement applies
        /// that from <c>MovementContext.CarryingObjective</c>, and applying it here
        /// too would charge the carrier twice for the same pair of hands.
        /// </para>
        /// </summary>
        public float SpeedMultiplier => CarryLoad.Resolve(TotalWeight, _bagEquipped);

        /// <summary>
        /// The weight band alone, with no bag term. For a consumer that applies the
        /// bag itself, and for telemetry that wants to separate "carrying too much"
        /// from "wearing the bag at all" when answering §16-2.
        /// </summary>
        public float BandMultiplier => CarryLoad.BandMultiplier(TotalWeight);

        /// <summary>
        /// False once the load reaches §08's fourth band (weight ≥ 16), which the
        /// document applies to every role — "주자도".
        /// <para>
        /// Answers the weight question only. §03's separate ban on sprinting while
        /// holding the objective is movement's to enforce, for the same
        /// double-counting reason as <see cref="SpeedMultiplier"/>.
        /// </para>
        /// </summary>
        public bool CanSprint => CarryLoad.CanSprintAt(TotalWeight);

        /// <summary>Which §08 band the current load falls in, 0–3. For telemetry and the shop's load readout.</summary>
        public int WeightBand => CarryLoad.BandIndexOf(TotalWeight);

        /// <summary>Weight units this player can hold, including §08's "+5 수용" if a bag is equipped.</summary>
        public int Capacity => CarryLoad.CapacityFor(_bagEquipped);

        /// <summary>Capacity in use — loot plus any oversize share. The objective is held in the hands, not stowed, so it is excluded.</summary>
        public int UsedCapacity => _lootWeight + _handWeight;

        /// <summary>Weight units still available. Never negative.</summary>
        public int FreeCapacity
        {
            get
            {
                var free = Capacity - UsedCapacity;
                return free > 0 ? free : 0;
            }
        }

        /// <summary>The pieces currently pocketed, in pickup order. An oversize piece being co-carried is not in here — see <see cref="HandWeight"/>.</summary>
        public IReadOnlyList<LootId> Loot => _loot;

        /// <summary>How many pocketed pieces are being carried.</summary>
        public int LootCount => _loot.Count;

        /// <summary>Credits the pocketed loot would fetch at the vehicle. §08: value, not a negotiation.</summary>
        public int LootValue => LootCatalogue.ValueOf(_loot);

        /// <summary>Weight of the oversize piece share held in the hands, or 0. Set only through <see cref="SharedLootCarry"/>.</summary>
        public int HandWeight => _handWeight;

        /// <summary>§08's 가방: +5 capacity, −10% speed. One per player; §08 offers no stacking.</summary>
        public bool BagEquipped => _bagEquipped;

        /// <summary>
        /// True when both hands are free. §03 says the objective "양손을 쓴다", and
        /// §08's oversize piece is carried the same way, so one player can hold
        /// exactly one of the two at a time.
        /// </summary>
        public bool HandsFree => _handWeight == 0 && !_carryingObjective;

        /// <summary>
        /// Whether this player is carrying the objective. §03: no flashlight, no
        /// loot, and the last decision of the match is who takes it.
        /// </summary>
        public bool CarryingObjective => _carryingObjective;

        /// <summary>Whether one more piece would fit. Cheap enough to call from a pickup prompt every frame.</summary>
        public bool CanAdd(LootId id)
        {
            if (id == LootId.None)
            {
                return false;
            }

            if (_carryingObjective)
            {
                // §03: "전리품 동시 소지 불가 — 전리품은 그 전에 다 챙겨야 한다."
                return false;
            }

            var weight = LootCatalogue.WeightOf(id);
            return weight > 0 && weight <= FreeCapacity;
        }

        /// <summary>
        /// Pockets one piece. Returns false when it will not fit or when the
        /// objective is in hand — never partially adds, so a rejected pickup leaves
        /// the player exactly as they were.
        /// </summary>
        public bool TryAdd(LootId id)
        {
            if (!CanAdd(id))
            {
                return false;
            }

            _loot.Add(id);
            _lootWeight += LootCatalogue.WeightOf(id);
            return true;
        }

        /// <summary>Drops one piece of the given kind. False when none is held.</summary>
        public bool TryRemove(LootId id)
        {
            var index = _loot.LastIndexOf(id);
            if (index < 0)
            {
                return false;
            }

            _loot.RemoveAt(index);
            _lootWeight -= LootCatalogue.WeightOf(id);
            if (_lootWeight < 0)
            {
                _lootWeight = 0;
            }

            return true;
        }

        /// <summary>
        /// Empties the pockets and returns what was in them. §08's 사망자의 전리품
        /// rule — loot "떨어진다" on death and has to be walked back to — and the
        /// same call serves a voluntary dump when the monster is closing.
        /// <para>
        /// An oversize piece in the hands is not released here; that belongs to
        /// <see cref="SharedLootCarry"/>, which has to decide between transferring
        /// it to the other carrier and putting it on the floor.
        /// </para>
        /// </summary>
        public LootId[] DropAll()
        {
            var dropped = _loot.ToArray();
            _loot.Clear();
            _lootWeight = 0;
            return dropped;
        }

        /// <summary>
        /// Straps on a bought bag. False if one is already worn — §08 lists a single
        /// 가방 with a single +5, and stacking would let a player carry a band-four
        /// load that the document never priced.
        /// </summary>
        public bool TryEquipBag()
        {
            if (_bagEquipped)
            {
                return false;
            }

            _bagEquipped = true;
            return true;
        }

        /// <summary>
        /// Takes the bag off. Refused while the load needs the extra +5, because the
        /// alternative is silently destroying loot the team paid time for.
        /// </summary>
        public bool TryUnequipBag()
        {
            if (!_bagEquipped)
            {
                return false;
            }

            if (UsedCapacity > GameConstants.BaseCarryCapacity)
            {
                return false;
            }

            _bagEquipped = false;
            return true;
        }

        /// <summary>
        /// Picks up the objective. Refused unless the hands are free and the pockets
        /// are empty — §03: "전리품 동시 소지 불가", which is what forces the team to
        /// sell before the last descent rather than during it.
        /// </summary>
        public bool TryPickUpObjective()
        {
            if (_carryingObjective || !HandsFree || _loot.Count > 0)
            {
                return false;
            }

            _carryingObjective = true;
            return true;
        }

        /// <summary>Sets the objective down. Safe to call when not holding it.</summary>
        public void PutDownObjective() => _carryingObjective = false;

        /// <summary>
        /// Takes an oversize share into the hands. Internal because
        /// <see cref="SharedLootCarry"/> is the only thing allowed to decide the
        /// share — a public setter would let two carry objects double-book the same
        /// pair of hands.
        /// </summary>
        internal bool TryOccupyHands(int weight)
        {
            if (weight <= 0 || !HandsFree || weight > FreeCapacity)
            {
                return false;
            }

            _handWeight = weight;
            return true;
        }

        /// <summary>
        /// Grows the share already in the hands, when the other carrier lets go.
        /// Fails without changing anything if the extra weight will not fit.
        /// </summary>
        internal bool TryGrowHandLoad(int extraWeight)
        {
            if (_handWeight <= 0)
            {
                return false;
            }

            if (extraWeight <= 0)
            {
                return true;
            }

            if (extraWeight > FreeCapacity)
            {
                return false;
            }

            _handWeight += extraWeight;
            return true;
        }

        /// <summary>Shrinks the share in the hands when a second carrier takes part of it.</summary>
        internal void ShrinkHandLoad(int lessWeight)
        {
            if (lessWeight <= 0)
            {
                return;
            }

            _handWeight -= lessWeight;
            if (_handWeight < 0)
            {
                _handWeight = 0;
            }
        }

        /// <summary>Empties the hands of an oversize share.</summary>
        internal void ReleaseHands() => _handWeight = 0;

        /// <summary>Removes every pocketed piece without reporting it, for a sale that has already been credited.</summary>
        internal void ClearLootAfterSale()
        {
            _loot.Clear();
            _lootWeight = 0;
        }

        /// <summary>Pieces of one kind currently pocketed. Used by the shop's sale breakdown and by tests.</summary>
        public int CountOf(LootId id)
        {
            var n = 0;
            for (var i = 0; i < _loot.Count; i++)
            {
                if (_loot[i] == id)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// What the load multiplier would become if one more piece were pocketed —
        /// the number a pickup prompt needs to make §08's greed dilemma legible
        /// before the player commits. Returns the current multiplier when it would
        /// not fit.
        /// </summary>
        public float SpeedMultiplierAfterAdding(LootId id)
        {
            if (!CanAdd(id))
            {
                return SpeedMultiplier;
            }

            return CarryLoad.Resolve(TotalWeight + LootCatalogue.WeightOf(id), _bagEquipped);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Inventory(w{0}/{1} x{2:0.00}{3}{4})",
                TotalWeight,
                Capacity,
                SpeedMultiplier,
                _bagEquipped ? " bag" : string.Empty,
                _carryingObjective ? " objective" : string.Empty);
        }
    }
}
