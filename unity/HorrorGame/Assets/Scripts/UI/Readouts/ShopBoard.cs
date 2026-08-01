#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Match;
using HorrorGame.Core.Math;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Threat;

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
            bool coversMissingRole,
            int shortfall)
        {
            Id = id;
            Category = category;
            Cost = cost;
            Stock = stock;
            Affordable = affordable;
            ConflictsWithRoster = conflictsWithRoster;
            CoversMissingRole = coversMissingRole;
            Shortfall = shortfall;
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

        /// <summary>
        /// Credits still needed for one, or zero when the shared purse already covers
        /// it. §08's negotiation is arithmetic — a greyed-out row says "no" and this
        /// says "how much more loot", which is the sentence that sends the team back
        /// down.
        /// </summary>
        public int Shortfall { get; }

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
    /// <b>§11's hole is part of the board, not a footnote on it.</b> §11 makes the
    /// missing role the character of the match and then says money can partly cover
    /// it — "약점을 돈으로 메울 수 있게 되면서 조합의 다양성이 실제로 성립한다". So
    /// the absent role, its 난제 and its stand-in are read here from
    /// <see cref="RoleGap"/> rather than from <see cref="ShopCatalogue"/> alone: the
    /// catalogue can only answer for items it stocks, and two of §11's five answers
    /// are not items on the shelf — the 관측자 has none by design, and the 섬광탄 §11
    /// promises for a missing 섬광수 is absent from §08's list. Asking the catalogue
    /// returns "nothing" for both, and the screen then says nothing at all about the
    /// one row the match most needs.
    /// </para>
    /// <para>
    /// <b>§07 is running while they shop.</b> §07 prices "상점에서 고민" at about
    /// thirty seconds and §10 lists shopping as a dilemma in its own right — "나가서
    /// 쉰다 · 구매한다 / 시간이 흐른다". The night therefore has a place on this
    /// board. It arrives through <see cref="ClockReadout"/> so §07's other rule holds
    /// automatically: the hour is legible here only because the team is standing at
    /// the vehicle, and Core's gate — not this class — is what decides that.
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

        /// <summary>
        /// §11's absence and its answer as one line — see
        /// <see cref="UiStrings.RoleGapLine"/> for the three cases. Empty in a full
        /// lobby, which is the only time the shop has nothing to say about the roster.
        /// </summary>
        public string RoleGapLine { get; private set; } = string.Empty;

        /// <summary>
        /// The item §11 points at for this match's absence, or
        /// <see cref="ShopItemId.None"/> when §08 does not stock one. Used to mark the
        /// row rather than to reorder the list — §08's order is the document's.
        /// </summary>
        public ShopItemId SubstituteItem { get; private set; }

        /// <summary>
        /// True when §11 names a stand-in that §08's 구매 목록 does not carry — the
        /// 섬광탄. The shop says so instead of quietly offering nothing, because a
        /// silent screen is indistinguishable from a match with no gap at all.
        /// </summary>
        public bool SubstituteMissingFromShop { get; private set; }

        /// <summary>§07's 시각, gated by Core: present only because the team is standing on the surface at the vehicle.</summary>
        public ClockReadout Night { get; private set; }

        /// <summary>
        /// Seconds until §07's next 위협 단계, or null in the open-ended last tier and
        /// whenever the hour may not be read. This is the number that makes the shop
        /// feel like a cost rather than a pause.
        /// </summary>
        public float? SecondsUntilNightWorsens { get; private set; }

        /// <summary>The 시각 the night is about to become, or null when there is no next row of §07's table.</summary>
        public NightPhase? NextPhase { get; private set; }

        /// <summary>
        /// How far through the current §07 tier the match is, 0–1. Drawn as a meter so
        /// the night visibly advances while nobody presses anything.
        /// </summary>
        public float TierProgress { get; private set; }

        /// <summary>Seconds this visit to the vehicle has taken so far. §07 prices the deliberation itself.</summary>
        public float SecondsAtTheVehicle { get; private set; }

        /// <summary>
        /// How much of the time left before §07's next 위협 단계 this visit has eaten,
        /// 0–1.
        /// <para>
        /// §07 prices "상점에서 고민" at about thirty seconds, but thirty seconds is not
        /// the same cost at every point in the night: early on it is nothing, and with
        /// a minute left before 심야 it is half of what the team had. Expressing the
        /// deliberation against the rung it is eating is what lets the screen get
        /// louder without this layer inventing a deadline of its own — every term comes
        /// from <c>MatchClock</c> and <see cref="GameConstants.ThreatTierSeconds"/>.
        /// </para>
        /// <para>
        /// One on §07's last rung, where there is no next step and "생존 불가 수준"
        /// means every second already costs the most it can.
        /// </para>
        /// </summary>
        public float TimePressure01 { get; private set; }

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
            Refresh(shop, roster, missingRole, null, 0f);
        }

        /// <summary>
        /// Rebuilds the board, with §07's night on it.
        /// </summary>
        /// <param name="shop">The team's shop. Null leaves an empty board rather than throwing.</param>
        /// <param name="roster">The four roles in play, for the 소음기 warning. Null flags nothing — see the other overload.</param>
        /// <param name="missingRole">The absent role, so §11's stand-in can be picked out of the list.</param>
        /// <param name="clock">
        /// The match clock. Null draws no night at all, which is also what §07's gate
        /// produces underground — the shop is never open there, but a board that
        /// invented a plausible hour would be the exact failure §07 forbids.
        /// </param>
        /// <param name="secondsAtTheVehicle">How long this visit has lasted. §07 charges for the deliberation itself.</param>
        public void Refresh(
            Shop? shop, IReadOnlyList<RoleId>? roster, RoleId missingRole, MatchClock? clock, float secondsAtTheVehicle)
        {
            MissingRole = missingRole;
            RefreshNight(clock, secondsAtTheVehicle);
            RefreshGap(missingRole);

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

            var substitute = SubstituteItem;
            var ids = ShopCatalogue.All;

            for (var i = 0; i < _rows.Length && i < ids.Count; i++)
            {
                var id = ids[i];
                var definition = ShopCatalogue.Of(id);
                var affordable = shop.CanAfford(id);
                _rows[i] = new ShopRow(
                    id,
                    definition.Category,
                    definition.Cost,
                    shop.StockOf(id),
                    affordable,
                    shop.ConflictsWithRoster(id, roster),
                    substitute != ShopItemId.None && id == substitute,
                    affordable ? 0 : definition.Cost - TeamCredits);
            }
        }

        /// <summary>
        /// Advances §07's half of the board and nothing else.
        /// <para>
        /// The night moves every frame and the shelf does not, so the frame-rate path
        /// is separated from the purchase path: a full <see cref="Refresh"/> walks
        /// thirteen rows and composes §11's sentence, and doing that sixty times a
        /// second to move one bar would allocate a string per frame in front of a
        /// player who is standing still.
        /// </para>
        /// </summary>
        /// <param name="clock">The match clock, or null to draw no night.</param>
        /// <param name="secondsAtTheVehicle">How long this visit has lasted.</param>
        public void TickNight(MatchClock? clock, float secondsAtTheVehicle)
        {
            RefreshNight(clock, secondsAtTheVehicle);
        }

        /// <summary>
        /// Reads §11's table for this match's absence.
        /// <para>
        /// <see cref="RoleGap"/> rather than <see cref="ShopCatalogue.SubstituteFor"/>
        /// because the catalogue's answer for a missing 섬광수 and for a missing 관측자
        /// is the same value — nothing — and §11 means two different things by them.
        /// The catalogue is still what marks the row, since only it knows which
        /// <see cref="ShopItemId"/> is on the shelf.
        /// </para>
        /// </summary>
        private void RefreshGap(RoleId missingRole)
        {
            var gap = RoleGap.For(missingRole);
            SubstituteItem = ShopCatalogue.SubstituteFor(missingRole);
            SubstituteMissingFromShop = gap.CanBeCoveredWithCredits && !gap.IsInShopList;
            RoleGapLine = UiStrings.RoleGapLine(missingRole, gap.Substitute, gap.CreditCost);
        }

        /// <summary>
        /// Reads §07's night through Core's gate.
        /// <para>
        /// The countdown is derived from <c>MatchClock.TierProgress</c> and
        /// <see cref="GameConstants.ThreatTierSeconds"/> rather than from a number of
        /// its own: §07's ladder is eight minutes a rung and that figure already
        /// exists, so a shop that timed itself differently would be a second, quietly
        /// disagreeing copy of the threat curve.
        /// </para>
        /// </summary>
        private void RefreshNight(MatchClock? clock, float secondsAtTheVehicle)
        {
            SecondsAtTheVehicle = secondsAtTheVehicle < 0f ? 0f : secondsAtTheVehicle;
            Night = ClockReadout.From(clock);

            if (clock == null || !Night.IsVisible)
            {
                SecondsUntilNightWorsens = null;
                NextPhase = null;
                TierProgress = 0f;
                TimePressure01 = 0f;
                return;
            }

            TierProgress = clock.TierProgress;

            var nextIndex = clock.TierIndex + 1;
            if (nextIndex >= ThreatCurve.TierCount)
            {
                // §07's last row is open-ended — "생존 불가 수준" is a state, not a
                // deadline, and TierProgress saturates at 1 to say so.
                SecondsUntilNightWorsens = null;
                NextPhase = null;
                TimePressure01 = 1f;
                return;
            }

            var remaining = (1f - TierProgress) * GameConstants.ThreatTierSeconds;
            SecondsUntilNightWorsens = remaining;
            NextPhase = ThreatCurve.Tier(nextIndex).Phase;
            TimePressure01 = remaining <= 0f ? 1f : MathX.Clamp01(SecondsAtTheVehicle / remaining);
        }
    }
}
