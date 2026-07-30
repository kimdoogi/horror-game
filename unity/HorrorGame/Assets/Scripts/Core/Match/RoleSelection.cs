using System;
using System.Collections.Generic;
using HorrorGame.Core.Roles;

namespace HorrorGame.Core.Match
{
    /// <summary>
    /// §11's 돈으로 메우기 column — the item that stands in for the role nobody took.
    /// <para>
    /// Every one of them is deliberately worse than the role: §11's point is that
    /// "약점을 돈으로 메울 수 있게 되면서 조합의 다양성이 실제로 성립하고, 전부
    /// 열등한 대체재이므로 직업의 가치는 유지된다."
    /// </para>
    /// <para>
    /// Its own enum rather than §08's <c>ShopItemId</c>, and not by accident: one of
    /// the five entries below has no row in §08's 구매 목록 at all. Naming §11's
    /// column separately is what lets that gap be reported instead of quietly
    /// rounded off — see <see cref="RoleGap.IsInShopList"/>.
    /// </para>
    /// </summary>
    public enum RoleSubstituteItem
    {
        /// <summary>
        /// Nothing covers it. §11 for the 관측자: "불가능" — "관측자만 대체 수단이
        /// 없다 — 유일하게 살 수 없는 정보를 제공하는 직업이다."
        /// </summary>
        None = 0,

        /// <summary>감지기, for a missing 청음사. §11 notes it makes noise doing its job, and calls that absence "공포 최대".</summary>
        Detector = 1,

        /// <summary>미끼, for a missing 주자. §11: "위치를 미리 정해야" — the pull happens where you planned, not where you need it.</summary>
        Bait = 2,

        /// <summary>조명탄, for a missing 정비공. §11: "1회용, 구역 하나".</summary>
        Flare = 3,

        /// <summary>섬광탄, for a missing 섬광수. §11: "소모품, 수량 제한".</summary>
        Flashbang = 4,
    }

    /// <summary>
    /// The role this match does without, and what money can do about it. §11.
    /// <para>
    /// §11 treats the gap as content — "매판 하나가 빠지고, 그게 그 판의 성격이
    /// 된다" — so it is modelled as a value with an answer, not as a missing feature.
    /// </para>
    /// </summary>
    public readonly struct RoleGap
    {
        /// <summary>The role nobody took, or <see cref="RoleId.None"/> when the lineup is not settled yet.</summary>
        public readonly RoleId MissingRole;

        /// <summary>What partially covers it. §11's 돈으로 메우기 column.</summary>
        public readonly RoleSubstituteItem Substitute;

        /// <summary>Builds a gap. Prefer <see cref="For"/>, which reads §11's table.</summary>
        public RoleGap(RoleId missingRole, RoleSubstituteItem substitute)
        {
            MissingRole = missingRole;
            Substitute = substitute;
        }

        /// <summary>
        /// Whether §11 says credits can paper over this absence. False only for the
        /// 관측자, whose information §11 calls the one thing that cannot be bought.
        /// </summary>
        public bool CanBeCoveredWithCredits
        {
            get { return Substitute != RoleSubstituteItem.None; }
        }

        /// <summary>
        /// What §08 charges for the substitute, or null when §08's 구매 목록 does not
        /// contain it.
        /// <para>
        /// Null for two different reasons, which is the point of keeping the value
        /// nullable rather than zero: the 관측자 has no substitute to price, and the
        /// 섬광탄 that §11 promises for a missing 섬광수 is absent from §08's list
        /// entirely. A missing price is not a free item.
        /// </para>
        /// </summary>
        public int? CreditCost
        {
            get
            {
                switch (Substitute)
                {
                    case RoleSubstituteItem.Detector:
                        return GameConstants.ShopCostDetector;
                    case RoleSubstituteItem.Bait:
                        return GameConstants.ShopCostBait;
                    case RoleSubstituteItem.Flare:
                        return GameConstants.ShopCostFlare;
                    default:
                        return null;
                }
            }
        }

        /// <summary>
        /// Whether the substitute is actually on §08's shelf. Where this is false but
        /// <see cref="CanBeCoveredWithCredits"/> is true, §11 and §08 disagree — see
        /// docs/BALANCE-FINDINGS.md.
        /// </summary>
        public bool IsInShopList
        {
            get { return CreditCost.HasValue; }
        }

        /// <summary>
        /// §11's table, verbatim: 청음사 → 감지기 · 관측자 → 불가능 · 주자 → 미끼 ·
        /// 정비공 → 조명탄 · 섬광수 → 섬광탄.
        /// </summary>
        public static RoleGap For(RoleId missingRole)
        {
            return new RoleGap(missingRole, SubstituteFor(missingRole));
        }

        /// <summary>The 돈으로 메우기 entry for one role. <see cref="RoleSubstituteItem.None"/> for the 관측자 and for <see cref="RoleId.None"/>.</summary>
        public static RoleSubstituteItem SubstituteFor(RoleId missingRole)
        {
            switch (missingRole)
            {
                case RoleId.Listener:
                    return RoleSubstituteItem.Detector;
                case RoleId.Runner:
                    return RoleSubstituteItem.Bait;
                case RoleId.Engineer:
                    return RoleSubstituteItem.Flare;
                case RoleId.Flasher:
                    return RoleSubstituteItem.Flashbang;
                default:
                    // 관측자, and "nothing is missing".
                    return RoleSubstituteItem.None;
            }
        }
    }

    /// <summary>
    /// Who took what. §11 — "4인 플레이 + 5개 중 4개 선택."
    /// <para>
    /// The class exists to hold one invariant: four distinct roles out of five, so
    /// exactly one is absent. Everything §11 builds on top — the missing role as the
    /// match's character, the substitute item, the "no compulsory role" check —
    /// starts from that count.
    /// </para>
    /// </summary>
    public sealed class RoleSelection
    {
        /// <summary>§04's five, in the document's order. Static so the order is one thing, not five copies of a decision.</summary>
        private static readonly RoleId[] AllRolesArray =
        {
            RoleId.Listener,
            RoleId.Observer,
            RoleId.Runner,
            RoleId.Engineer,
            RoleId.Flasher,
        };

        private readonly RoleId[] _slots;

        /// <summary>An empty lobby: <see cref="GameConstants.PlayersPerMatch"/> slots, nobody chosen.</summary>
        public RoleSelection()
        {
            _slots = new RoleId[GameConstants.PlayersPerMatch];
            for (var i = 0; i < _slots.Length; i++)
            {
                _slots[i] = RoleId.None;
            }
        }

        /// <summary>The five roles of §04, in the document's order. Length is <see cref="GameConstants.RoleCount"/>.</summary>
        public static IReadOnlyList<RoleId> AllRoles
        {
            get { return AllRolesArray; }
        }

        /// <summary>Party size. §11: four.</summary>
        public int SlotCount
        {
            get { return _slots.Length; }
        }

        /// <summary>What each slot picked, indexed by player. <see cref="RoleId.None"/> for an undecided slot.</summary>
        public IReadOnlyList<RoleId> Slots
        {
            get { return _slots; }
        }

        /// <summary>True when every slot holds a distinct role — that is, when the match can start.</summary>
        public bool IsComplete
        {
            get
            {
                for (var i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i] == RoleId.None)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// The role nobody took, or <see cref="RoleId.None"/> until the lineup is
        /// complete. §11 makes this the match's headline: "그게 그 판의 성격이 된다."
        /// </summary>
        public RoleId MissingRole
        {
            get
            {
                if (!IsComplete)
                {
                    return RoleId.None;
                }

                for (var i = 0; i < AllRolesArray.Length; i++)
                {
                    if (!IsTaken(AllRolesArray[i]))
                    {
                        return AllRolesArray[i];
                    }
                }

                // Unreachable while PlayersPerMatch is exactly one less than
                // RoleCount: four distinct roles out of five always leave one over.
                return RoleId.None;
            }
        }

        /// <summary>The absence and its §11 substitute, ready to show in the lobby.</summary>
        public RoleGap Gap
        {
            get { return RoleGap.For(MissingRole); }
        }

        /// <summary>What this lineup can still finish, and what it will have to buy. §11's 절대 규칙.</summary>
        public CompletabilityReport Completability
        {
            get { return RoleCompletability.Evaluate(MissingRole); }
        }

        /// <summary>What a slot picked.</summary>
        /// <exception cref="ArgumentOutOfRangeException">No such slot.</exception>
        public RoleId RoleOf(int playerIndex)
        {
            RequireSlot(playerIndex);
            return _slots[playerIndex];
        }

        /// <summary>Whether somebody already has this role.</summary>
        public bool IsTaken(RoleId role)
        {
            if (role == RoleId.None)
            {
                return false;
            }

            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == role)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The roles still on the table. Four of five are taken, so this is never empty before the last pick.</summary>
        public IReadOnlyList<RoleId> AvailableRoles()
        {
            var free = new List<RoleId>(AllRolesArray.Length);
            for (var i = 0; i < AllRolesArray.Length; i++)
            {
                if (!IsTaken(AllRolesArray[i]))
                {
                    free.Add(AllRolesArray[i]);
                }
            }

            return free;
        }

        /// <summary>
        /// Claims a role for a slot. §11 allows no duplicates — two 정비공 would make
        /// two roles absent, and the "5개 중 4개" structure the whole section rests on
        /// would stop holding.
        /// </summary>
        /// <returns>
        /// False when the role is <see cref="RoleId.None"/> (use <see cref="Release"/>)
        /// or already held by somebody else. Re-claiming what this slot already holds
        /// succeeds, so a host that pushes lobby state every frame is idempotent.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">No such slot.</exception>
        public bool TryClaim(int playerIndex, RoleId role)
        {
            RequireSlot(playerIndex);

            if (role == RoleId.None)
            {
                return false;
            }

            if (_slots[playerIndex] == role)
            {
                return true;
            }

            if (IsTaken(role))
            {
                return false;
            }

            _slots[playerIndex] = role;
            return true;
        }

        /// <summary>Puts a slot's role back on the table. False when it was already empty.</summary>
        /// <exception cref="ArgumentOutOfRangeException">No such slot.</exception>
        public bool Release(int playerIndex)
        {
            RequireSlot(playerIndex);

            if (_slots[playerIndex] == RoleId.None)
            {
                return false;
            }

            _slots[playerIndex] = RoleId.None;
            return true;
        }

        /// <summary>
        /// Builds a settled lineup from four distinct roles.
        /// </summary>
        /// <returns>
        /// False for a null or wrong-sized list, for <see cref="RoleId.None"/>, for a
        /// value outside §04's five, or for any duplicate. A rejected lineup yields
        /// null rather than a half-filled selection.
        /// </returns>
        public static bool TryCreate(IReadOnlyList<RoleId>? roles, out RoleSelection? selection)
        {
            selection = null;

            if (roles == null || roles.Count != GameConstants.PlayersPerMatch)
            {
                return false;
            }

            var candidate = new RoleSelection();
            for (var i = 0; i < roles.Count; i++)
            {
                if (!IsRealRole(roles[i]) || !candidate.TryClaim(i, roles[i]))
                {
                    return false;
                }
            }

            selection = candidate;
            return true;
        }

        /// <summary>Builds a settled lineup, or throws. For tests and for the host, which has already validated the lobby.</summary>
        /// <exception cref="ArgumentException">The roles are not four distinct entries from §04's five.</exception>
        public static RoleSelection FromRoles(params RoleId[] roles)
        {
            RoleSelection? selection;
            if (!TryCreate(roles, out selection) || selection == null)
            {
                throw new ArgumentException(
                    "§11 needs exactly " + GameConstants.PlayersPerMatch
                    + " distinct roles chosen from the five in §04.", nameof(roles));
            }

            return selection;
        }

        /// <summary>
        /// Every lineup §11 allows: five of them, one per absent role.
        /// <para>
        /// This is the section's own verification method — "5가지 조합을 전부
        /// 돌려본다. 성립하지 않는 조합이 하나라도 있으면 그 직업이 과하게
        /// 필수적이다." Generated rather than listed so that adding a sixth role
        /// cannot leave the check testing four of six.
        /// </para>
        /// </summary>
        public static IReadOnlyList<RoleSelection> AllLineups()
        {
            var lineups = new List<RoleSelection>(AllRolesArray.Length);

            for (var missing = 0; missing < AllRolesArray.Length; missing++)
            {
                var selection = new RoleSelection();
                var slot = 0;
                for (var i = 0; i < AllRolesArray.Length && slot < selection.SlotCount; i++)
                {
                    if (i == missing)
                    {
                        continue;
                    }

                    selection.TryClaim(slot, AllRolesArray[i]);
                    slot++;
                }

                lineups.Add(selection);
            }

            return lineups;
        }

        private static bool IsRealRole(RoleId role)
        {
            for (var i = 0; i < AllRolesArray.Length; i++)
            {
                if (AllRolesArray[i] == role)
                {
                    return true;
                }
            }

            return false;
        }

        private void RequireSlot(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= _slots.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerIndex), playerIndex, "§11 seats " + _slots.Length + " players.");
            }
        }
    }

    /// <summary>
    /// Something a match needs somebody to be able to do. §11's 절대 규칙 — "필수
    /// 직업이 있으면 풀이 가짜가 된다" — can only be checked against a list of these,
    /// so this is that list.
    /// </summary>
    public enum MatchCapability
    {
        /// <summary>Getting light onto a clue long enough to read it. §03's "어둠 = 목표의 잠금장치".</summary>
        ClueLighting = 1,

        /// <summary>Walking the objective out with somebody lighting the way. §03's "사실상 2인 1조 호송".</summary>
        ObjectiveEscort = 2,

        /// <summary>Knowing where the monster is. §04's 청음사.</summary>
        MonsterLocation = 3,

        /// <summary>Knowing who the monster is coming for. §04's 관측자.</summary>
        TargetIdentity = 4,

        /// <summary>Pulling the monster off the team on purpose. §04's 주자.</summary>
        AggroPull = 5,

        /// <summary>Buying somebody a few seconds. §04's 섬광수; §01 insists every form of it stays temporary.</summary>
        TemporaryDenial = 6,

        /// <summary>Opening a 금고. §04's 정비공, and §08's 금고 속 문서 behind it.</summary>
        SafeAccess = 7,
    }

    /// <summary>How a capability is being met in a given lineup. §11.</summary>
    public enum CapabilityCoverage
    {
        /// <summary>Nobody can do it and nothing can be bought for it.</summary>
        Uncovered = 0,

        /// <summary>The role that owns it is in the party. §04.</summary>
        Role = 1,

        /// <summary>
        /// Covered by something every party has regardless of roles — §11's
        /// "손전등을 전원 기본 지급했다", §01's "단서 풀이는 전원 공동 작업", the
        /// four-player structure itself.
        /// </summary>
        UniversalIssue = 2,

        /// <summary>Covered, worse, by §11's 돈으로 메우기 item.</summary>
        PurchasedSubstitute = 3,
    }

    /// <summary>One row of §11's completability check.</summary>
    public readonly struct CapabilityStatus
    {
        /// <summary>What this row is about.</summary>
        public readonly MatchCapability Capability;

        /// <summary>The role that does it properly, or <see cref="RoleId.None"/> when no role owns it.</summary>
        public readonly RoleId Provider;

        /// <summary>
        /// Whether a match literally cannot be finished without it. §02 defines
        /// finishing as recovering the objective and getting somebody out, so this is
        /// true for very few rows — and that is exactly why §11's 절대 규칙 holds.
        /// </summary>
        public readonly bool RequiredToFinish;

        /// <summary>How it is met here.</summary>
        public readonly CapabilityCoverage Coverage;

        /// <summary>
        /// The §11 item that stands in for <i>this</i> capability, or
        /// <see cref="RoleSubstituteItem.None"/> when nothing on §08's shelf does.
        /// <para>
        /// Per capability, not per role, and the difference matters: a missing 정비공
        /// costs a party both clue lighting and 금고 access, and §11's 조명탄 covers
        /// only the first. Populated whether or not the item is currently the cover —
        /// when <see cref="Coverage"/> is <see cref="CapabilityCoverage.UniversalIssue"/>
        /// the item is an upgrade over the fallback rather than the thing holding the
        /// match together.
        /// </para>
        /// </summary>
        public readonly RoleSubstituteItem Substitute;

        /// <summary>Builds a row. Produced by <see cref="RoleCompletability"/>.</summary>
        public CapabilityStatus(
            MatchCapability capability,
            RoleId provider,
            bool requiredToFinish,
            CapabilityCoverage coverage,
            RoleSubstituteItem substitute)
        {
            Capability = capability;
            Provider = provider;
            RequiredToFinish = requiredToFinish;
            Coverage = coverage;
            Substitute = substitute;
        }

        /// <summary>True when this row alone makes the match unfinishable. §11 fails a lineup on this.</summary>
        public bool BlocksCompletion
        {
            get { return RequiredToFinish && Coverage == CapabilityCoverage.Uncovered; }
        }

        /// <summary>
        /// True when the party is doing this the hard way — a bought stand-in, or a
        /// universal fallback where a role would have done it properly. §11 calls the
        /// resulting texture "그 판의 난제".
        /// </summary>
        public bool IsDegraded
        {
            get
            {
                if (Coverage == CapabilityCoverage.PurchasedSubstitute || Coverage == CapabilityCoverage.Uncovered)
                {
                    return true;
                }

                return Coverage == CapabilityCoverage.UniversalIssue && Provider != RoleId.None;
            }
        }
    }

    /// <summary>
    /// The answer to §11's 검증법 for one lineup: can this four-of-five party still
    /// finish, and what will it be short of.
    /// </summary>
    public sealed class CompletabilityReport
    {
        private readonly CapabilityStatus[] _capabilities;

        /// <summary>Builds a report. Produced by <see cref="RoleCompletability.Evaluate(RoleId)"/>.</summary>
        public CompletabilityReport(RoleId missingRole, CapabilityStatus[] capabilities)
        {
            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            MissingRole = missingRole;
            _capabilities = capabilities;
        }

        /// <summary>The role this lineup does without.</summary>
        public RoleId MissingRole { get; }

        /// <summary>Every capability and how it is met.</summary>
        public IReadOnlyList<CapabilityStatus> Capabilities
        {
            get { return _capabilities; }
        }

        /// <summary>
        /// True when nothing required is uncovered — §11's "성립한다". A false here
        /// means the missing role is compulsory, which the section forbids outright.
        /// </summary>
        public bool IsCompletable
        {
            get { return !Blocker.HasValue; }
        }

        /// <summary>The first required-and-uncovered capability, or null when the lineup works.</summary>
        public MatchCapability? Blocker
        {
            get
            {
                for (var i = 0; i < _capabilities.Length; i++)
                {
                    if (_capabilities[i].BlocksCompletion)
                    {
                        return _capabilities[i].Capability;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// How many capabilities this party does the hard way. §11 wants this above
        /// zero for every lineup — if an absence cost nothing, the choice would be
        /// cosmetic.
        /// </summary>
        public int DegradedCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _capabilities.Length; i++)
                {
                    if (_capabilities[i].IsDegraded)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>How one capability is met here.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Not one of the rows in this report.</exception>
        public CapabilityStatus StatusOf(MatchCapability capability)
        {
            for (var i = 0; i < _capabilities.Length; i++)
            {
                if (_capabilities[i].Capability == capability)
                {
                    return _capabilities[i];
                }
            }

            throw new ArgumentOutOfRangeException(nameof(capability), capability, "No such capability row.");
        }
    }

    /// <summary>
    /// §11's 절대 규칙, as a function: "필수 직업이 있으면 풀이 가짜가 된다."
    /// <para>
    /// The section supplies both the rule and the way to check it — "검증법: 5가지
    /// 조합을 전부 돌려본다. 성립하지 않는 조합이 하나라도 있으면 그 직업이 과하게
    /// 필수적이다." <see cref="VerifyAllLineups"/> is that check, and it is a
    /// computation over the table below rather than a hard-coded "yes": the table
    /// says which capabilities a match cannot be finished without, and the check
    /// asks whether any of them depends on one role. Mark a role-owned capability
    /// required and the check starts failing, which is the behaviour §11 wants —
    /// making a role compulsory should break the build, not the players' evening.
    /// </para>
    /// <para>
    /// Why so few rows are required, with the document's own reasoning:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Reading clues</b> is required — §03 makes darkness the objective's lock —
    /// and is covered universally, because §11 explicitly issued a flashlight to
    /// everyone for this reason: "정비공은 효율이지 자격이 아니다."
    /// </description></item>
    /// <item><description>
    /// <b>Escorting the objective</b> is required — §03's carrier cannot hold a
    /// light — and is covered by party size
    /// (<see cref="GameConstants.ObjectiveEscortMinPlayers"/>), not by a role.
    /// </description></item>
    /// <item><description>
    /// <b>Nothing else is required.</b> §01 removes any kill requirement ("괴물을
    /// 죽일 수 없다") and makes every form of interference temporary, so no denial
    /// ability can be a prerequisite. §03 makes loot — and therefore the 금고 the
    /// Engineer opens — optional currency: "안 챙겨도 클리어된다."
    /// </description></item>
    /// </list>
    /// </summary>
    public static class RoleCompletability
    {
        /// <summary>
        /// §11's check for one absence. Pass <see cref="RoleId.None"/> to ask about a
        /// party missing nothing, which is not a §11 lineup but is the right answer
        /// for the query.
        /// </summary>
        public static CompletabilityReport Evaluate(RoleId missingRole)
        {
            var escortCoveredByPartySize =
                GameConstants.PlayersPerMatch >= GameConstants.ObjectiveEscortMinPlayers;

            var rows = new[]
            {
                // §03's lock, §11's key: everyone carries a flashlight, so the
                // 조명탄 is the Engineer's efficiency rather than the party's licence.
                Row(MatchCapability.ClueLighting, RoleId.Engineer, true, true, RoleSubstituteItem.Flare, missingRole),

                // §03's "사실상 2인 1조 호송" — covered by there being four of them.
                Row(MatchCapability.ObjectiveEscort, RoleId.None, true, escortCoveredByPartySize, RoleSubstituteItem.None, missingRole),

                Row(MatchCapability.MonsterLocation, RoleId.Listener, false, false, RoleSubstituteItem.Detector, missingRole),

                // §11: "관측자만 대체 수단이 없다." Uncovered, and allowed to be,
                // because knowing the monster's target is not how a match is finished.
                Row(MatchCapability.TargetIdentity, RoleId.Observer, false, false, RoleSubstituteItem.None, missingRole),

                Row(MatchCapability.AggroPull, RoleId.Runner, false, false, RoleSubstituteItem.Bait, missingRole),
                Row(MatchCapability.TemporaryDenial, RoleId.Flasher, false, false, RoleSubstituteItem.Flashbang, missingRole),

                // §08 sells nothing that opens a 금고, so a party without the Engineer
                // simply does without 금고 속 문서 — which §03 permits, since 전리품 is
                // optional currency.
                Row(MatchCapability.SafeAccess, RoleId.Engineer, false, false, RoleSubstituteItem.None, missingRole),
            };

            return new CompletabilityReport(missingRole, rows);
        }

        /// <summary>§11's check for a lineup.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="selection"/> is null.</exception>
        public static CompletabilityReport Evaluate(RoleSelection selection)
        {
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }

            return Evaluate(selection.MissingRole);
        }

        /// <summary>
        /// §11's 검증법 in full: one report per possible absence, five in total.
        /// </summary>
        public static IReadOnlyList<CompletabilityReport> VerifyAllLineups()
        {
            var lineups = RoleSelection.AllLineups();
            var reports = new List<CompletabilityReport>(lineups.Count);
            for (var i = 0; i < lineups.Count; i++)
            {
                reports.Add(Evaluate(lineups[i]));
            }

            return reports;
        }

        /// <summary>
        /// True when every one of §11's five combinations can still finish — the
        /// section's pass condition, and a precondition of the role pool meaning
        /// anything at all.
        /// </summary>
        public static bool EveryLineupCompletes()
        {
            var reports = VerifyAllLineups();
            for (var i = 0; i < reports.Count; i++)
            {
                if (!reports[i].IsCompletable)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Resolves one row: the role covers it if the role is present, otherwise the
        /// universal issue, otherwise §11's bought stand-in, otherwise nothing.
        /// <para>
        /// The universal fallback is preferred over the substitute on purpose. §11's
        /// argument for completability is the flashlight everyone already has, not the
        /// 조명탄 somebody might afford — a party that has earned nothing yet (§08's
        /// "1차 잠입 전 구매력 0") must still be able to finish.
        /// </para>
        /// </summary>
        private static CapabilityStatus Row(
            MatchCapability capability,
            RoleId provider,
            bool requiredToFinish,
            bool universallyCovered,
            RoleSubstituteItem substitute,
            RoleId missingRole)
        {
            var providerPresent = provider != RoleId.None && provider != missingRole;

            CapabilityCoverage coverage;
            if (providerPresent)
            {
                coverage = CapabilityCoverage.Role;
            }
            else if (universallyCovered)
            {
                coverage = CapabilityCoverage.UniversalIssue;
            }
            else if (substitute != RoleSubstituteItem.None)
            {
                coverage = CapabilityCoverage.PurchasedSubstitute;
            }
            else
            {
                coverage = CapabilityCoverage.Uncovered;
            }

            return new CapabilityStatus(capability, provider, requiredToFinish, coverage, substitute);
        }
    }
}
