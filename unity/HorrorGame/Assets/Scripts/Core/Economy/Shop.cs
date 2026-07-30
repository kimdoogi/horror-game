using System;
using System.Collections.Generic;
using HorrorGame.Core.Roles;

namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// Why a purchase did or did not happen.
    /// </summary>
    public enum PurchaseOutcome
    {
        /// <summary>Bought. Credits are gone and the stock is on the vehicle.</summary>
        Purchased = 0,

        /// <summary>The team cannot afford it. Nothing was taken — §08's negotiation happens before the money moves, not after.</summary>
        NotEnoughCredits = 1,

        /// <summary>No such item in §08's list.</summary>
        UnknownItem = 2,

        /// <summary>A quantity of zero or less. A no-op request is a caller bug, not a sale.</summary>
        InvalidQuantity = 3,
    }

    /// <summary>
    /// The surface vehicle: safe zone, shop and supply depot in one (§08).
    /// <para>
    /// It owns the team's stock and spends from the team's <see cref="Wallet"/>. No
    /// method here takes a player — §08's shared wallet means the shop cannot tell
    /// who is standing in front of it, and that is what forces the argument the
    /// section is built on.
    /// </para>
    /// <para>
    /// Nothing is stepped: buying and selling are instantaneous at the vehicle, and
    /// the only cost is the clock that §07 keeps running whether the team is inside
    /// or out — "나가서 쉰다 · 구매한다 → 시간이 흐른다" (§10).
    /// </para>
    /// </summary>
    public sealed class Shop
    {
        private const int SlotCount = (int)ShopItemId.Suppressor + 1;

        private readonly Wallet _wallet;
        private readonly int[] _stock = new int[SlotCount];

        /// <summary>Opens a shop over an existing team wallet.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="wallet"/> is null. A shop without a wallet would have to invent per-player money.</exception>
        public Shop(Wallet wallet)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        /// <summary>The team's wallet. There is exactly one, by §08's design.</summary>
        public Wallet Wallet => _wallet;

        /// <summary>
        /// True once the team owns a 소음기. §08: "자기도 못 듣게 됨 → 청음사 무효",
        /// and "청음사가 있는 팀은 사면 안 된다".
        /// <para>
        /// Team-level rather than per-wearer, for the same reason the wallet is: the
        /// purchase is the team's, so its consequence is too. The Listener's feed is
        /// the team's only reliable read on the monster, and §08 wanted an item that
        /// conflicts with a role rather than one that trades against it — so this
        /// flips the whole ability off, and the audio layer reads the bool without
        /// ever knowing what a credit is.
        /// </para>
        /// </summary>
        public bool ListenerDisabled => _stock[(int)ShopItemId.Suppressor] > 0;

        /// <summary>How many of an item the team is holding, unspent.</summary>
        public int StockOf(ShopItemId item)
        {
            var slot = (int)item;
            if (slot <= 0 || slot >= SlotCount)
            {
                return 0;
            }

            return _stock[slot];
        }

        /// <summary>Whether the team could buy this right now.</summary>
        public bool CanAfford(ShopItemId item, int quantity = 1) =>
            ShopCatalogue.Exists(item) && quantity > 0
            && _wallet.CanAfford(ShopCatalogue.CostOf(item) * quantity);

        /// <summary>
        /// Buys <paramref name="quantity"/> of an item, all or nothing.
        /// <para>
        /// A partial sale would quietly resolve §08's negotiation for the team ("we
        /// could only afford two of the three batteries"), so an unaffordable basket
        /// is refused whole and the credits stay where the players can argue over
        /// them.
        /// </para>
        /// </summary>
        public PurchaseOutcome TryBuy(ShopItemId item, int quantity = 1)
        {
            if (!ShopCatalogue.Exists(item))
            {
                return PurchaseOutcome.UnknownItem;
            }

            if (quantity <= 0)
            {
                return PurchaseOutcome.InvalidQuantity;
            }

            var total = ShopCatalogue.CostOf(item) * quantity;
            if (!_wallet.TrySpend(total))
            {
                return PurchaseOutcome.NotEnoughCredits;
            }

            _stock[(int)item] += quantity;
            return PurchaseOutcome.Purchased;
        }

        /// <summary>
        /// Spends one unit of stock — a battery going into a flashlight, a flare
        /// being lit. False when there is none left, which is §03's resource
        /// pressure arriving on schedule.
        /// </summary>
        public bool TryConsume(ShopItemId item)
        {
            var slot = (int)item;
            if (slot <= 0 || slot >= SlotCount || _stock[slot] <= 0)
            {
                return false;
            }

            _stock[slot]--;
            return true;
        }

        /// <summary>
        /// Straps a bought 가방 onto a player: +5 capacity, −10% speed (§08).
        /// <para>
        /// Consumes the stock because the bag is now on someone's back rather than in
        /// the van. Fails without consuming anything when there is no bag to give or
        /// the player already wears one.
        /// </para>
        /// </summary>
        public bool TryEquipBag(Inventory? wearer)
        {
            if (wearer == null || _stock[(int)ShopItemId.Bag] <= 0)
            {
                return false;
            }

            if (!wearer.TryEquipBag())
            {
                return false;
            }

            _stock[(int)ShopItemId.Bag]--;
            return true;
        }

        /// <summary>
        /// Loads a player's pockets into the vehicle. §08: "전리품을 차량에 실으면 →
        /// 가치만큼 크레딧". Returns the credits banked; an empty-handed player banks
        /// zero, which is a legal and common outcome (§03: loot is optional).
        /// </summary>
        public int SellAll(Inventory? seller)
        {
            if (seller == null)
            {
                return 0;
            }

            var value = seller.LootValue;
            seller.ClearLootAfterSale();
            if (value > 0)
            {
                _wallet.Deposit(value);
            }

            return value;
        }

        /// <summary>
        /// Loads an oversize piece two players walked out. Releases whoever is
        /// holding it, so the carriers get their speed back the moment it is sold.
        /// Returns the credits banked, or zero if the piece is already gone.
        /// </summary>
        public int SellCarried(SharedLootCarry? carry)
        {
            if (carry == null || !carry.TrySell())
            {
                return 0;
            }

            var value = carry.Value;
            if (value > 0)
            {
                _wallet.Deposit(value);
            }

            return value;
        }

        /// <summary>
        /// Loads a pile recovered from the floor — §08's 사망자의 전리품, cashed in.
        /// </summary>
        public int SellPile(IReadOnlyList<LootId>? pieces)
        {
            var value = LootCatalogue.ValueOf(pieces);
            if (value > 0)
            {
                _wallet.Deposit(value);
            }

            return value;
        }

        /// <summary>
        /// Whether buying this item would break one of the roster's own abilities.
        /// §08 states the case plainly for the 소음기 — "청음사가 있는 팀은 사면 안
        /// 된다" — so the shop can warn instead of silently deleting a player's role.
        /// A null or empty roster conflicts with nothing.
        /// </summary>
        public bool ConflictsWithRoster(ShopItemId item, IReadOnlyList<RoleId>? roster)
        {
            if (roster == null || !ShopCatalogue.Of(item).DisablesListener)
            {
                return false;
            }

            for (var i = 0; i < roster.Count; i++)
            {
                if (roster[i] == RoleId.Listener)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a role's ability is currently switched off by something the team
        /// bought. Only the Listener can be, and only by the 소음기 (§08).
        /// <para>
        /// This is the bool the ability layer reads. It deliberately does not tell
        /// the caller what disabled it: from the Listener's seat the world simply
        /// went quiet, and §08 wants that regret to belong to the team.
        /// </para>
        /// </summary>
        public bool IsRoleAbilityDisabled(RoleId role) => role == RoleId.Listener && ListenerDisabled;

        /// <summary>
        /// Everything the team is holding, cleared. §02: a wipe loses the lot, and
        /// the wallet is emptied by <see cref="Wallet.LoseEverything"/> beside it.
        /// </summary>
        public void ClearStock() => Array.Clear(_stock, 0, _stock.Length);
    }
}
