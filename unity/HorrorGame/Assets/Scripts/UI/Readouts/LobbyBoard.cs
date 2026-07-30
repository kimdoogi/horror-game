#nullable enable

using System.Collections.Generic;
using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;

namespace HorrorGame.UI.Readouts
{
    /// <summary>One of §04's five, and whether anybody has taken it.</summary>
    public readonly struct RoleOption
    {
        /// <summary>Builds an option. Produced by <see cref="LobbyBoard"/>.</summary>
        public RoleOption(RoleId role, int takenBySlot, bool takenByLocalPlayer)
        {
            Role = role;
            TakenBySlot = takenBySlot;
            TakenByLocalPlayer = takenByLocalPlayer;
        }

        /// <summary>Which role.</summary>
        public RoleId Role { get; }

        /// <summary>The slot holding it, or −1 when it is free.</summary>
        public int TakenBySlot { get; }

        /// <summary>Whether this is the local player's own pick.</summary>
        public bool TakenByLocalPlayer { get; }

        /// <summary>Whether anybody has it.</summary>
        public bool Taken
        {
            get { return TakenBySlot >= 0; }
        }

        /// <summary>§04's name.</summary>
        public string Name
        {
            get { return UiStrings.Role(Role); }
        }

        /// <summary>§04's 능력 row.</summary>
        public string Ability
        {
            get { return UiStrings.RoleAbility(Role); }
        }

        /// <summary>§04's 제약 row — shown with the ability, because §10 says a gain without its cost is not information.</summary>
        public string Limit
        {
            get { return UiStrings.RoleLimit(Role); }
        }
    }

    /// <summary>
    /// The lobby. §11 — <em>"4인 플레이 + 5개 중 4개 선택. 매판 하나가 빠지고, 그게
    /// 그 판의 성격이 된다."</em>
    /// <para>
    /// The absence is the headline, not a footnote. §11 treats the missing role as
    /// content — it has a table of what each absence costs and what money can do
    /// about it — so the lobby leads with the gap and its stand-in, and the team
    /// argues about which hole they want before the descent instead of discovering it
    /// underground.
    /// </para>
    /// <para>
    /// <b>The 관측자 has none, and the board says so out loud.</b> §11: "관측자만
    /// 대체 수단이 없다 — 유일하게 살 수 없는 정보를 제공하는 직업이다." That absence
    /// is a designed asymmetry, so <see cref="SubstituteName"/> reads "불가능" rather
    /// than being blanked — a blank looks like missing data, and this is the one row
    /// where "nothing" is the answer.
    /// </para>
    /// <para>
    /// <b>The 섬광탄 is reported as unpriced, not as free.</b> §11 promises it for a
    /// missing 섬광수 but §08's 구매 목록 does not contain it, which is a disagreement
    /// between two sections rather than something this layer may resolve
    /// (ARCHITECTURE §6). <c>RoleGap.IsInShopList</c> is the Core-side flag; the
    /// lobby surfaces it instead of inventing a price.
    /// </para>
    /// </summary>
    public sealed class LobbyBoard
    {
        private readonly RoleOption[] _options;

        /// <summary>Creates an empty board with one option per role in §04.</summary>
        public LobbyBoard()
        {
            _options = new RoleOption[RoleSelection.AllRoles.Count];
        }

        /// <summary>§04's five, with who holds each.</summary>
        public IReadOnlyList<RoleOption> Options
        {
            get { return _options; }
        }

        /// <summary>Whether every slot has picked — that is, whether the match can start. §11.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>The role nobody took, or <see cref="RoleId.None"/> until the lineup settles.</summary>
        public RoleId MissingRole { get; private set; }

        /// <summary>§11's 그 판의 난제 for this absence. Empty until the lineup settles.</summary>
        public string AbsenceProblem { get; private set; } = string.Empty;

        /// <summary>What §11 offers instead — or "불가능" for the 관측자.</summary>
        public string SubstituteName { get; private set; } = string.Empty;

        /// <summary>Why that stand-in is worse than the role. §11 keeps every one of them inferior on purpose.</summary>
        public string SubstituteLimit { get; private set; } = string.Empty;

        /// <summary>Whether §11 says credits can cover this absence at all. False only for the 관측자.</summary>
        public bool SubstituteExists { get; private set; }

        /// <summary>§08's price for the stand-in, or null when §08's list does not carry it.</summary>
        public int? SubstituteCost { get; private set; }

        /// <summary>
        /// True when §11 names a stand-in that §08 does not sell — currently the
        /// 섬광탄. Shown as an explicit "가격 미정", because pretending the item is
        /// on the shelf would hide a real gap between two sections.
        /// </summary>
        public bool SubstituteMissingFromShop
        {
            get { return SubstituteExists && !SubstituteCost.HasValue; }
        }

        /// <summary>
        /// §11's 절대 규칙 checked against this lineup. Null until the lineup settles.
        /// <para>
        /// Displayed because it is the answer to the question a new player asks in the
        /// lobby — "can we even do this without a 정비공?" — and §11's answer is yes,
        /// by construction: "손전등을 전원 기본 지급했다 … 정비공은 효율이지
        /// 자격이 아니다."
        /// </para>
        /// </summary>
        public CompletabilityReport? Completability { get; private set; }

        /// <summary>Roles nobody has claimed yet. One entry once three slots are filled, two before that.</summary>
        public IReadOnlyList<RoleId> Unclaimed { get; private set; } = new RoleId[0];

        /// <summary>
        /// Rebuilds from the authoritative selection.
        /// </summary>
        /// <param name="selection">The lobby's picks. Null leaves an empty board.</param>
        /// <param name="localPlayerIndex">Which slot is this client's, so its own pick can be marked. Out-of-range values simply mark nothing.</param>
        public void Refresh(RoleSelection? selection, int localPlayerIndex)
        {
            if (selection == null)
            {
                for (var i = 0; i < _options.Length; i++)
                {
                    _options[i] = new RoleOption(RoleSelection.AllRoles[i], -1, false);
                }

                IsComplete = false;
                MissingRole = RoleId.None;
                AbsenceProblem = string.Empty;
                SubstituteName = string.Empty;
                SubstituteLimit = string.Empty;
                SubstituteExists = false;
                SubstituteCost = null;
                Completability = null;
                Unclaimed = new RoleId[0];
                return;
            }

            var roles = RoleSelection.AllRoles;
            for (var i = 0; i < _options.Length && i < roles.Count; i++)
            {
                var role = roles[i];
                var slot = SlotHolding(selection, role);
                _options[i] = new RoleOption(role, slot, slot >= 0 && slot == localPlayerIndex);
            }

            IsComplete = selection.IsComplete;
            Unclaimed = selection.AvailableRoles();
            MissingRole = selection.MissingRole;

            var gap = selection.Gap;
            AbsenceProblem = UiStrings.RoleAbsenceProblem(MissingRole);
            SubstituteName = UiStrings.Substitute(gap.Substitute);
            SubstituteLimit = UiStrings.SubstituteLimit(gap.Substitute);
            SubstituteExists = gap.CanBeCoveredWithCredits;
            SubstituteCost = gap.CreditCost;
            Completability = IsComplete ? selection.Completability : null;
        }

        private static int SlotHolding(RoleSelection selection, RoleId role)
        {
            var slots = selection.Slots;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i] == role)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
