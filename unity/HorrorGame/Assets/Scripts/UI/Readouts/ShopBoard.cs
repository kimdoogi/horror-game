#nullable enable

using System.Collections.Generic;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Roles;

namespace HorrorGame.UI.Readouts
{
    /// <summary>
    /// One line of §08's 구매 목록 as the shop draws it.
    /// <para>
    /// Carries the 효과 and the 대가 together because §08's list only makes sense
    /// that way: "전부 §10 딜레마 원리를 따른다 — 얻는 게 있으면 대가가 있다." The
    /// flagship row is the 강화 손전등, which §08 calls "이 목록의 대표작" precisely
    /// because its drawback is its benefit — 밝으면 더 잘 보이지만 더 잘 보인다.
    /// </para>
    /// </summary>
    public readonly struct ShopRow
    {
        /// <summary>Builds a row. Produced by <see cref="ShopBoard"/>.</summary>
        public ShopRow(
            ShopItemId id,
            ShopCategory category,
            int cost,
            int stock,
            bool affordable,
            bool conflictsWithRoster,
            bool coversMissingRole)
        {
            Id = id;
            Category = category;
            Cost = cost;
            Stock = stock;
            Affordable = affordable;
            ConflictsWithRoster = conflictsWithRoster;
            CoversMissingRole = coversMissingRole;
        }

        /// <summary>Which item.</summary>
        public ShopItemId Id { get; }

        /// <summary>§08's 분류.</summary>
        public ShopCategory Category { get; }

        /// <summary>Credits for one.</summary>
        public int Cost { get; }

        /// <summary>How many the team already holds, unspent.</summary>
        public int Stock { get; }

        /// <summary>Whether the team's shared wallet covers one right now.</summary>
        public bool Affordable { get; }

        /// <summary>
        /// True for the 소음기 when a 청음사 is on the roster. §08 does not hedge:
        /// "청음사가 있는 팀은 사면 안 된다." The row stays buyable — the team is
        /// allowed to make the mistake, and §04 says accidents are content — but it is
        /// marked, because a player who did not know cannot argue about it.
        /// </summary>
        public bool ConflictsWithRoster { get; }

        /// <summary>True when §11 offers this item in place of the role this match is missing.</summary>
        public bool CoversMissingRole { get; }

        /// <summary>§08's 아이템 column.</summary>
        public string Name
        {
            get { return UiStrings.Item(Id); }
        }

        /// <summary>§08's 효과 column.</summary>
        public string Effect
        {
            get { return UiStrings.ItemEffect(Id); }
        }

        /// <summary>§08's 대가 column. Empty only where the document writes "—".</summary>
        public string Price
        {
            get { return UiStrings.ItemPrice(Id); }
        }
    }

    /// <summary>
    /// The shop at the vehicle. §08.
    /// <para>
    /// <b>One wallet, and no way to ask for another.</b> §08 is blunt about why —
    /// <em>"공용 지갑이 핵심 … 돈이 개인 것이면 협동 게임이 아니게 된다"</em> — and
    /// <c>Wallet</c> enforces it in Core by having no player parameter anywhere, with
    /// a reflection test guarding against a "just for the UI" convenience creeping
    /// back in. This class holds the same line on this side of the seam: nothing here
    /// takes a player index, a connection, a <c>NetUserId</c> or a name, and
    /// <see cref="TeamCredits"/> is the only balance that exists. A personal balance
    /// would have to arrive as a new parameter on one of these members, which is a
    /// visible change to a file whose entire job is to not have one.
    /// </para>
    /// <para>
    /// A class rather than a struct, refreshed in place: the board is rebuilt on
    /// every purchase and every sale, and a shop the team is standing in front of
    /// while the clock runs (§07) should not be allocating a table each time.
    /// </para>
    /// </summary>
    public sealed class ShopBoard
    {
        private readonly ShopRow[] _rows;

        /// <summary>Creates an empty board with one row per item in §08's list.</summary>
        public ShopBoard()
        {
            _rows = new ShopRow[ShopCatalogue.All.Count];
        }

        /// <summary>
        /// The team's credits. There is exactly one number, and it belongs to
        /// everybody — see the class remarks.
        /// </summary>
        public int TeamCredits { get; private set; }

        /// <summary>Everything banked this match, for the "우리 얼마 벌었지" question the shop always produces.</summary>
        public int TotalEarned { get; private set; }

        /// <summary>Everything spent this match.</summary>
        public int TotalSpent { get; private set; }

        /// <summary>The role this match is doing without, or <see cref="RoleId.None"/> in a full lobby. §11.</summary>
        public RoleId MissingRole { get; private set; }

        /// <summary>§08's list, in the document's order.</summary>
        public IReadOnlyList<ShopRow> Rows
        {
            get { return _rows; }
        }

        /// <summary>Whether the wallet covers the cheapest thing on the shelf. False is §08's opening position — "1차 잠입 전 구매력 0".</summary>
        public bool CanBuyAnything
        {
            get { return TeamCredits >= ShopCatalogue.CheapestCost; }
        }

        /// <summary>
        /// Rebuilds the board from the authoritative shop.
        /// </summary>
        /// <param name="shop">The team's shop. Null leaves an empty board rather than throwing.</param>
        /// <param name="roster">
        /// The four roles in play, for the 소음기 warning. Null means "we do not know
        /// yet", and nothing is flagged — a false conflict warning would be worse than
        /// none, because it would teach the team to ignore the marker.
        /// </param>
        /// <param name="missingRole">The absent role, so §11's stand-in can be picked out of the list.</param>
        public void Refresh(Shop? shop, IReadOnlyList<RoleId>? roster, RoleId missingRole)
        {
            MissingRole = missingRole;

            if (shop == null)
            {
                TeamCredits = 0;
                TotalEarned = 0;
                TotalSpent = 0;
                for (var i = 0; i < _rows.Length; i++)
                {
                    _rows[i] = default(ShopRow);
                }

                return;
            }

            var wallet = shop.Wallet;
            TeamCredits = wallet.Credits;
            TotalEarned = wallet.TotalEarned;
            TotalSpent = wallet.TotalSpent;

            var substitute = ShopCatalogue.SubstituteFor(missingRole);
            var ids = ShopCatalogue.All;

            for (var i = 0; i < _rows.Length && i < ids.Count; i++)
            {
                var id = ids[i];
                var definition = ShopCatalogue.Of(id);
                _rows[i] = new ShopRow(
                    id,
                    definition.Category,
                    definition.Cost,
                    shop.StockOf(id),
                    shop.CanAfford(id),
                    shop.ConflictsWithRoster(id, roster),
                    substitute != ShopItemId.None && id == substitute);
            }
        }
    }
}
