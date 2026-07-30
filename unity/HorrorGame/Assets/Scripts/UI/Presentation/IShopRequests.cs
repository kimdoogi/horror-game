#nullable enable

using HorrorGame.Core.Economy;

namespace HorrorGame.UI
{
    /// <summary>
    /// What a player standing at the vehicle can ask for. §08 · §13.
    /// <para>
    /// The shop screen never spends a credit itself. §13 gives the host authority,
    /// and a client that applied its own purchase would be predicting an answer that
    /// three other people are simultaneously trying to change — §08's whole point is
    /// that the wallet is contested ("강화 손전등 하나 살까, 배터리 3개 살까?"), so
    /// two players pressing buy in the same second is the normal case and not an edge
    /// case. The screen raises a request, the host resolves it against the one
    /// <c>Wallet</c>, and the board is redrawn from whatever the host says is true.
    /// </para>
    /// <para>
    /// No method takes a player. The host already knows who asked — it is the
    /// connection the request arrived on — and adding the parameter here is exactly
    /// how a per-player balance would first appear in the codebase. §08: "돈이 개인
    /// 것이면 협동 게임이 아니게 된다."
    /// </para>
    /// </summary>
    public interface IShopRequests
    {
        /// <summary>
        /// Asks to buy <paramref name="quantity"/> of an item. The host answers by
        /// refreshing the board; a refusal shows up as unchanged credits, which is
        /// also what the team sees when somebody else spent the money first.
        /// </summary>
        void RequestPurchase(ShopItemId item, int quantity);

        /// <summary>
        /// Asks to load whatever the requesting player is carrying into the vehicle.
        /// §08: "전리품을 차량에 실으면 → 가치만큼 크레딧" — and the credits land in
        /// the team wallet, not the seller's.
        /// </summary>
        void RequestSellCarriedLoot();
    }

    /// <summary>
    /// An <see cref="IShopRequests"/> that resolves against the shop directly.
    /// <para>
    /// For the host's own screen and for offline play (§14 steps 1–3, where the game
    /// runs before Steam exists). A remote client's screen is given a networked
    /// implementation from the Net layer instead; the screen cannot tell the
    /// difference, which is the point of the interface.
    /// </para>
    /// <para>
    /// <paramref name="sellerPockets"/> is whose <em>hands</em> are being emptied,
    /// never whose money it is: the proceeds go to <c>Shop.SellAll</c>, which deposits
    /// into the single team <c>Wallet</c>. §08 has one wallet and this class does not
    /// introduce a second.
    /// </para>
    /// </summary>
    public sealed class LocalShopRequests : IShopRequests
    {
        private readonly Shop _shop;
        private readonly Inventory? _sellerPockets;

        /// <summary>Wires a screen straight to the shop it is standing in front of.</summary>
        /// <param name="shop">The team's shop.</param>
        /// <param name="sellerPockets">The local player's inventory, emptied by <see cref="RequestSellCarriedLoot"/>.</param>
        public LocalShopRequests(Shop shop, Inventory? sellerPockets)
        {
            _shop = shop;
            _sellerPockets = sellerPockets;
        }

        /// <summary>The outcome of the last purchase attempt, so the screen can say why nothing happened.</summary>
        public PurchaseOutcome LastOutcome { get; private set; } = PurchaseOutcome.Purchased;

        /// <summary>Credits banked by the last sale. Zero is a legal answer — §03 makes loot optional.</summary>
        public int LastSaleValue { get; private set; }

        /// <inheritdoc />
        public void RequestPurchase(ShopItemId item, int quantity)
        {
            LastOutcome = _shop.TryBuy(item, quantity);
        }

        /// <inheritdoc />
        public void RequestSellCarriedLoot()
        {
            LastSaleValue = _shop.SellAll(_sellerPockets);
        }
    }
}
