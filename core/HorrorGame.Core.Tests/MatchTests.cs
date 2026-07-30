using System;
using System.Linq;
using System.Reflection;
using HorrorGame.Core;
using HorrorGame.Core.Ghost;
using HorrorGame.Core.Match;
using HorrorGame.Core.Math;
using HorrorGame.Core.Roles;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §02 (승패 조건), §09 (사망 처리 — 유령) and §11 (인원 구조), asserted against the
    /// arguments those sections make rather than against the code that implements them.
    /// <para>
    /// The three sections are load-bearing in a way that is easy to erode. §02's value
    /// is not that it has four rows but that 생존 and 패배 differ in what the team
    /// keeps — flatten that into a win flag and "지금 나갈까?" stops being an argument.
    /// §09's value is a ghost that cannot talk; give it any channel wider than one
    /// rattle every 45 s and the design's own note says the balance collapses. §11's
    /// value is that no role is compulsory, and the section names the exact check.
    /// These tests are those three claims.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchTests
    {
        // ====================================================================
        // Fixtures.
        // ====================================================================

        /// <summary>A §11 lineup with one role deliberately absent.</summary>
        private static RoleSelection LineupWithout(RoleId missing)
        {
            var chosen = RoleSelection.AllRoles.Where(r => r != missing).ToArray();
            return RoleSelection.FromRoles(chosen);
        }

        /// <summary>A match that has not started moving yet: four players at the vehicle (§01).</summary>
        private static MatchState NewMatch(RoleId missing)
        {
            return new MatchState(LineupWithout(missing));
        }

        /// <summary>The ghost of a player who must already be dead.</summary>
        private static GhostState GhostOf(MatchState match, int playerIndex)
        {
            var ghost = match.PlayerAt(playerIndex).Ghost;
            Assert.That(ghost, Is.Not.Null, "Player " + playerIndex + " is not dead, so §09 has nothing to say about them.");
            return ghost!;
        }

        // ====================================================================
        // §02 — the four outcomes.
        // ====================================================================

        /// <summary>§02: 완전 승리 = 목표물 회수 + 4인 전원 탈출.</summary>
        [Test]
        public void Section02_FullVictory_NeedsObjectiveAndEveryoneOut()
        {
            var match = NewMatch(RoleId.Observer);

            Assert.That(match.PlayerAt(0).TryDescend(), Is.True);
            Assert.That(match.TryTakeObjective(0), Is.True);
            Assert.That(match.PlayerAt(0).TrySurface(), Is.True);

            for (var i = 0; i < GameConstants.PlayersPerMatch; i++)
            {
                Assert.That(match.TryExtract(i), Is.True, "Player " + i + " is on the surface and alive.");
            }

            var resolution = match.Resolution;
            Assert.That(resolution.Outcome, Is.EqualTo(MatchOutcome.FullVictory));
            Assert.That(resolution.ObjectiveRecovered, Is.True, "The carrier walked out with it, which is §02's 회수.");
            Assert.That(resolution.PlayersEscaped, Is.EqualTo(GameConstants.PlayersPerMatch));
            Assert.That(resolution.EveryoneGotOut, Is.True);
            Assert.That(resolution.PlayersLost, Is.Zero);
        }

        /// <summary>§02: 부분 승리 = 목표물 회수 + 일부 탈출. The objective is out; somebody is not.</summary>
        [Test]
        public void Section02_PartialVictory_ObjectiveOutButSomebodyLost()
        {
            var match = NewMatch(RoleId.Runner);

            Assert.That(match.PlayerAt(3).TryDescend(), Is.True);
            Assert.That(match.TryKill(3, new Vec3(12f, -6f, 4f)), Is.True);

            Assert.That(match.TryTakeObjective(0), Is.True);
            Assert.That(match.TryExtract(0), Is.True);
            Assert.That(match.TryExtract(1), Is.True);
            Assert.That(match.TryExtract(2), Is.True);

            var resolution = match.Resolution;
            Assert.That(resolution.Outcome, Is.EqualTo(MatchOutcome.PartialVictory));
            Assert.That(resolution.ObjectiveRecovered, Is.True);
            Assert.That(resolution.PlayersEscaped, Is.EqualTo(GameConstants.PlayersPerMatch - 1));
            Assert.That(resolution.PlayersLost, Is.EqualTo(1));
            Assert.That(resolution.EveryoneGotOut, Is.False,
                "§02 keeps 완전 승리 and 부분 승리 apart on exactly this: one death is the whole difference.");
        }

        /// <summary>
        /// §02: 생존 = 목표물 없이 탈출 — "그 판의 정보는 남는다". The row that exists so
        /// that leaving early is a real option rather than a concession.
        /// </summary>
        [Test]
        public void Section02_Survived_KeepsTheMatchsInformation()
        {
            var match = NewMatch(RoleId.Engineer);

            for (var i = 1; i < GameConstants.PlayersPerMatch; i++)
            {
                Assert.That(match.PlayerAt(i).TryDescend(), Is.True);
                Assert.That(match.TryKill(i, new Vec3(i, -3f, 0f)), Is.True);
            }

            Assert.That(match.TryExtract(0), Is.True);

            var resolution = match.Resolution;
            Assert.That(resolution.Outcome, Is.EqualTo(MatchOutcome.Survived));
            Assert.That(resolution.ObjectiveRecovered, Is.False);
            Assert.That(resolution.PlayersEscaped, Is.EqualTo(1));
            Assert.That(resolution.InformationKept, Is.True,
                "§02: 한 명이라도 탈출하면 그 판에서 알아낸 정보가 보존된다.");
            Assert.That(resolution.LootKept, Is.True);
            Assert.That(resolution.CreditsKept, Is.True);
            Assert.That(resolution.NothingSurvived, Is.False);
        }

        /// <summary>§02: 패배 = 전멸 — "그 판의 모든 것을 잃는다."</summary>
        [Test]
        public void Section02_Wipe_LosesEverythingFromThatMatch()
        {
            var match = NewMatch(RoleId.Flasher);

            for (var i = 0; i < GameConstants.PlayersPerMatch; i++)
            {
                Assert.That(match.PlayerAt(i).TryDescend(), Is.True);
                Assert.That(match.TryKill(i, new Vec3(i, -9f, i)), Is.True);
            }

            var resolution = match.Resolution;
            Assert.That(resolution.Outcome, Is.EqualTo(MatchOutcome.Wiped));
            Assert.That(resolution.PlayersEscaped, Is.Zero);
            Assert.That(resolution.PlayersLost, Is.EqualTo(GameConstants.PlayersPerMatch));
            Assert.That(resolution.InformationKept, Is.False,
                "§02: 전멸하면 전리품 · 크레딧 · 정보가 전부 사라진다.");
            Assert.That(resolution.LootKept, Is.False);
            Assert.That(resolution.CreditsKept, Is.False);
            Assert.That(resolution.NothingSurvived, Is.True);
        }

        /// <summary>
        /// The §02 asymmetry itself, side by side: one survivor and no survivors differ
        /// in what is kept, not merely in a label.
        /// <para>
        /// This is the test that fails if somebody ever reduces the outcome to a
        /// boolean — 생존 and 패배 are both "not a victory", and the design's central
        /// lever lives entirely in the gap between them.
        /// </para>
        /// </summary>
        [Test]
        public void Section02_OneSurvivorVersusAWipe_IsTheDesignsCentralAsymmetry()
        {
            var oneOut = OutcomeEvaluator.Evaluate(1, GameConstants.PlayersPerMatch - 1, 0, false);
            var noneOut = OutcomeEvaluator.Evaluate(0, GameConstants.PlayersPerMatch, 0, false);

            Assert.That(oneOut.Outcome, Is.EqualTo(MatchOutcome.Survived));
            Assert.That(noneOut.Outcome, Is.EqualTo(MatchOutcome.Wiped));

            Assert.That(oneOut.InformationKept, Is.True);
            Assert.That(noneOut.InformationKept, Is.False);
            Assert.That(oneOut.PlayersLost, Is.EqualTo(noneOut.PlayersLost - 1),
                "Three deaths versus four is the entire difference, and it decides whether the match "
                + "was worth playing. §02 calls that gap the reason \"지금 나갈까?\" is a real conflict.");
        }

        /// <summary>
        /// §02 is undecided while anyone is still alive in the building. Without this
        /// the last player standing would be told the match was over while they were
        /// still walking, and §02's "누군가는 살아서 나가야 한다" would have no moment.
        /// </summary>
        [Test]
        public void Section02_StaysInProgress_WhileSomebodyIsStillAlive()
        {
            var match = NewMatch(RoleId.Listener);

            for (var i = 1; i < GameConstants.PlayersPerMatch; i++)
            {
                Assert.That(match.PlayerAt(i).TryDescend(), Is.True);
                Assert.That(match.TryKill(i, Vec3.Zero), Is.True);
            }

            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.InProgress));
            Assert.That(match.Resolution.IsFinal, Is.False);
            Assert.That(match.StillInPlayCount, Is.EqualTo(1));

            Assert.That(match.TryExtract(0), Is.True);
            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.Survived));
        }

        /// <summary>
        /// A wipe stays a wipe even if the objective is reported recovered. §02's 패배
        /// row carries no exception, and the combination is unreachable in play — the
        /// objective cannot leave the basement without a carrier leaving with it — so
        /// this pins the defensive ordering rather than a game situation.
        /// </summary>
        [Test]
        public void Section02_WipeOutranksARecoveredObjective()
        {
            var resolution = OutcomeEvaluator.Evaluate(0, GameConstants.PlayersPerMatch, 0, true);

            Assert.That(resolution.Outcome, Is.EqualTo(MatchOutcome.Wiped));
            Assert.That(resolution.NothingSurvived, Is.True,
                "§02: 전멸하면 그 판의 모든 것을 잃는다 — there is no objective clause in that row.");
        }

        /// <summary>
        /// The outcome is a function of the tally, so two transitions landing in the
        /// same tick cannot resolve differently depending on the host's update order.
        /// </summary>
        [Test]
        public void Section02_SimultaneousDeathAndEscape_ResolveTheSameEitherWay()
        {
            var deathFirst = NewMatch(RoleId.Observer);
            var escapeFirst = NewMatch(RoleId.Observer);

            foreach (var match in new[] { deathFirst, escapeFirst })
            {
                Assert.That(match.PlayerAt(2).TryDescend(), Is.True);
                Assert.That(match.TryKill(2, Vec3.Zero), Is.True);
                Assert.That(match.PlayerAt(3).TryDescend(), Is.True);
                Assert.That(match.TryKill(3, Vec3.Zero), Is.True);
                Assert.That(match.PlayerAt(1).TryDescend(), Is.True);
            }

            Assert.That(deathFirst.TryKill(1, Vec3.Zero), Is.True);
            Assert.That(deathFirst.TryExtract(0), Is.True);

            Assert.That(escapeFirst.TryExtract(0), Is.True);
            Assert.That(escapeFirst.TryKill(1, Vec3.Zero), Is.True);

            Assert.That(deathFirst.Outcome, Is.EqualTo(MatchOutcome.Survived));
            Assert.That(escapeFirst.Outcome, Is.EqualTo(deathFirst.Outcome));
            Assert.That(escapeFirst.Resolution.InformationKept, Is.EqualTo(deathFirst.Resolution.InformationKept));
        }

        /// <summary>An empty party is not a 전멸. Nobody played, so §02 has nothing to rule on.</summary>
        [Test]
        public void OutcomeEvaluator_EmptyParty_IsNotAWipe()
        {
            var resolution = OutcomeEvaluator.Evaluate(0, 0, 0, false);

            Assert.That(resolution.Outcome, Is.EqualTo(MatchOutcome.InProgress));
            Assert.That(resolution.PlayerCount, Is.Zero);
        }

        /// <summary>Negative counts are a caller bug, not a match state.</summary>
        [Test]
        public void OutcomeEvaluator_NegativeCounts_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OutcomeEvaluator.Evaluate(-1, 0, 0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => OutcomeEvaluator.Evaluate(0, -1, 0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => OutcomeEvaluator.Evaluate(0, 0, -1, false));
        }

        /// <summary>
        /// §13 does not migrate hosts, so the host leaving ends the session — but §02's
        /// rule about what survives does not care why the match stopped.
        /// </summary>
        [Test]
        public void Abandoning_EndsTheSession_AndStillHonoursSection02Spoils()
        {
            var match = NewMatch(RoleId.Runner);
            Assert.That(match.TryExtract(0), Is.True);

            Assert.That(match.TryAbandon(), Is.True);
            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.Abandoned));
            Assert.That(match.Resolution.InformationKept, Is.True,
                "Somebody had already walked out; §02 preserves what they left with.");
            Assert.That(match.TryAbandon(), Is.False, "Abandoning twice is not a second event.");
        }

        /// <summary>A match §02 has already decided cannot be retroactively abandoned — a wipe must stay a wipe.</summary>
        [Test]
        public void Abandoning_IsRefused_OnceTheMatchHasResolved()
        {
            var match = NewMatch(RoleId.Engineer);
            for (var i = 0; i < GameConstants.PlayersPerMatch; i++)
            {
                Assert.That(match.PlayerAt(i).TryDescend(), Is.True);
                Assert.That(match.TryKill(i, Vec3.Zero), Is.True);
            }

            Assert.That(match.TryAbandon(), Is.False);
            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.Wiped));
        }

        // ====================================================================
        // §11 — 4인, 5개 중 4개.
        // ====================================================================

        /// <summary>§11's structure: the party is one smaller than the role pool, so exactly one role is always absent.</summary>
        [Test]
        public void Section11_PartyIsOneSmallerThanTheRolePool()
        {
            Assert.That(GameConstants.RoleCount, Is.EqualTo(GameConstants.PlayersPerMatch + 1),
                "§11 is \"4인 플레이 + 5개 중 4개 선택\". If these stop differing by one, either every "
                + "role is always present or two are missing, and the section's whole premise goes.");
            Assert.That(RoleSelection.AllRoles.Count, Is.EqualTo(GameConstants.RoleCount));
            Assert.That(RoleSelection.AllRoles.Distinct().Count(), Is.EqualTo(GameConstants.RoleCount));
            Assert.That(RoleSelection.AllRoles, Has.No.Member(RoleId.None));
        }

        /// <summary>Every lineup names its absent role, and the five lineups name five different ones.</summary>
        [Test]
        public void Section11_EveryLineup_HasExactlyOneMissingRole()
        {
            var lineups = RoleSelection.AllLineups();

            Assert.That(lineups.Count, Is.EqualTo(GameConstants.RoleCount),
                "§11's 검증법 is to run all five combinations.");

            var missing = lineups.Select(l => l.MissingRole).ToArray();
            Assert.That(missing.Distinct().Count(), Is.EqualTo(GameConstants.RoleCount));
            Assert.That(missing, Is.EquivalentTo(RoleSelection.AllRoles));

            foreach (var lineup in lineups)
            {
                Assert.That(lineup.IsComplete, Is.True);
                Assert.That(lineup.Slots.Distinct().Count(), Is.EqualTo(GameConstants.PlayersPerMatch));
                Assert.That(lineup.IsTaken(lineup.MissingRole), Is.False);
            }
        }

        /// <summary>Two players cannot take the same role: that would leave two roles absent and break §11's shape.</summary>
        [Test]
        public void Section11_RoleSelection_RefusesDuplicatesAndHalfPicks()
        {
            var selection = new RoleSelection();

            Assert.That(selection.MissingRole, Is.EqualTo(RoleId.None), "Nothing is 'missing' until the lineup is settled.");
            Assert.That(selection.IsComplete, Is.False);

            Assert.That(selection.TryClaim(0, RoleId.Listener), Is.True);
            Assert.That(selection.TryClaim(0, RoleId.Listener), Is.True, "Re-pushing the same pick is idempotent.");
            Assert.That(selection.TryClaim(1, RoleId.Listener), Is.False, "§11 allows one of each.");
            Assert.That(selection.TryClaim(1, RoleId.None), Is.False, "Use Release, not a None claim.");
            Assert.That(selection.AvailableRoles().Count, Is.EqualTo(GameConstants.RoleCount - 1));

            Assert.That(selection.Release(0), Is.True);
            Assert.That(selection.Release(0), Is.False);
            Assert.That(selection.TryClaim(1, RoleId.Listener), Is.True, "Released roles go back on the table.");

            Assert.Throws<ArgumentOutOfRangeException>(() => selection.TryClaim(GameConstants.PlayersPerMatch, RoleId.Runner));
            Assert.Throws<ArgumentOutOfRangeException>(() => selection.RoleOf(-1));

            RoleSelection? built;
            Assert.That(RoleSelection.TryCreate(null, out built), Is.False);
            Assert.That(built, Is.Null);
            Assert.That(RoleSelection.TryCreate(new[] { RoleId.Listener, RoleId.Runner, RoleId.Engineer }, out built), Is.False,
                "Three picks is not a §11 party.");
            Assert.That(RoleSelection.TryCreate(
                new[] { RoleId.Listener, RoleId.Listener, RoleId.Runner, RoleId.Engineer }, out built), Is.False);
            Assert.That(RoleSelection.TryCreate(
                new[] { RoleId.None, RoleId.Listener, RoleId.Runner, RoleId.Engineer }, out built), Is.False);
            Assert.Throws<ArgumentException>(() => RoleSelection.FromRoles(RoleId.Listener, RoleId.Listener, RoleId.Runner, RoleId.Engineer));
        }

        /// <summary>
        /// §11's 돈으로 메우기 column, verbatim: 청음사 → 감지기 · 주자 → 미끼 ·
        /// 정비공 → 조명탄 · 섬광수 → 섬광탄 · 관측자 → 불가능.
        /// </summary>
        [Test]
        [TestCase(RoleId.Listener, RoleSubstituteItem.Detector)]
        [TestCase(RoleId.Runner, RoleSubstituteItem.Bait)]
        [TestCase(RoleId.Engineer, RoleSubstituteItem.Flare)]
        [TestCase(RoleId.Flasher, RoleSubstituteItem.Flashbang)]
        [TestCase(RoleId.Observer, RoleSubstituteItem.None)]
        public void Section11_SubstituteTable_MatchesTheDocument(RoleId missing, RoleSubstituteItem expected)
        {
            var match = NewMatch(missing);

            Assert.That(match.MissingRole, Is.EqualTo(missing));
            Assert.That(match.Gap.MissingRole, Is.EqualTo(missing));
            Assert.That(match.Gap.Substitute, Is.EqualTo(expected));
        }

        /// <summary>
        /// §11: "관측자만 대체 수단이 없다 — 유일하게 살 수 없는 정보를 제공하는
        /// 직업이다." The other four absences can be papered over; that one cannot.
        /// </summary>
        [Test]
        public void Section11_OnlyTheObserverHasNoSubstitute()
        {
            var withoutSubstitute = RoleSelection.AllRoles
                .Where(r => !RoleGap.For(r).CanBeCoveredWithCredits)
                .ToArray();

            Assert.That(withoutSubstitute, Is.EqualTo(new[] { RoleId.Observer }),
                "If any other role loses its stand-in, §11's argument that 조합의 다양성이 실제로 성립한다 "
                + "stops holding; if the Observer gains one, the section's one irreplaceable role is gone.");

            foreach (var role in RoleSelection.AllRoles.Where(r => r != RoleId.Observer))
            {
                Assert.That(RoleGap.For(role).Substitute, Is.Not.EqualTo(RoleSubstituteItem.None),
                    "§11 gives " + role + " a 돈으로 메우기 entry.");
            }
        }

        /// <summary>
        /// The three substitutes §08 actually prices are priced there and nowhere else,
        /// and the dearest is the 감지기 — §11 calls the Listener's absence "공포 최대".
        /// </summary>
        [Test]
        public void Section11_PricedSubstitutes_ReadTheirCostFromSection08()
        {
            Assert.That(RoleGap.For(RoleId.Listener).CreditCost, Is.EqualTo(GameConstants.ShopCostDetector));
            Assert.That(RoleGap.For(RoleId.Runner).CreditCost, Is.EqualTo(GameConstants.ShopCostBait));
            Assert.That(RoleGap.For(RoleId.Engineer).CreditCost, Is.EqualTo(GameConstants.ShopCostFlare));
            Assert.That(RoleGap.For(RoleId.Observer).CreditCost, Is.Null, "Nothing to price. §11: 불가능.");

            Assert.That(GameConstants.ShopCostDetector,
                Is.GreaterThan(GameConstants.ShopCostBait).And.GreaterThan(GameConstants.ShopCostFlare),
                "§11 calls a missing 청음사 \"공포 최대\", so its stand-in is the dearest of them.");
        }

        /// <summary>
        /// FINDING — §11 promises 섬광탄 for a missing 섬광수, and §08's 구매 목록 has no
        /// such row.
        /// <para>
        /// §11's economic argument is that four of the five absences can be softened
        /// with credits; §08 supplies a price for three. The 섬광탄 is described in §11
        /// with a shop item's shape ("소모품, 수량 제한") but never appears on the
        /// shelf, so as written a party without the 섬광수 cannot buy the thing the
        /// document says covers them. See docs/BALANCE-FINDINGS.md.
        /// </para>
        /// <para>
        /// This test pins the gap. When §08 gets a 섬광탄 row, it fails, and the
        /// finding gets closed in the same commit.
        /// </para>
        /// </summary>
        [Test]
        public void Finding_FlashbangIsPromisedBySection11ButAbsentFromSection08()
        {
            var gap = RoleGap.For(RoleId.Flasher);

            Assert.That(gap.Substitute, Is.EqualTo(RoleSubstituteItem.Flashbang),
                "§11's table names 섬광탄 as the cover for a missing 섬광수.");
            Assert.That(gap.CanBeCoveredWithCredits, Is.True, "§11 puts it in the 돈으로 메우기 column.");
            Assert.That(gap.IsInShopList, Is.False,
                "§08's 구매 목록 has no 섬광탄 row and GameConstants therefore has no price for one. If this "
                + "now passes, §08 gained the item — update docs/BALANCE-FINDINGS.md and give RoleGap its cost.");
            Assert.That(gap.CreditCost, Is.Null);

            var promisedButUnpriced = RoleSelection.AllRoles
                .Select(RoleGap.For)
                .Where(g => g.CanBeCoveredWithCredits && !g.IsInShopList)
                .Select(g => g.MissingRole)
                .ToArray();

            Assert.That(promisedButUnpriced, Is.EqualTo(new[] { RoleId.Flasher }),
                "Exactly one row of §11's table has no §08 price. If that count changes, the two sections "
                + "have drifted further apart.");
        }

        // ====================================================================
        // §11 — 절대 규칙: 필수 직업이 있으면 풀이 가짜가 된다.
        // ====================================================================

        /// <summary>
        /// §11's 검증법, run: "5가지 조합을 전부 돌려본다. 성립하지 않는 조합이 하나라도
        /// 있으면 그 직업이 과하게 필수적이다."
        /// </summary>
        [Test]
        public void Section11_AllFiveLineups_AreCompletable()
        {
            var reports = RoleCompletability.VerifyAllLineups();

            Assert.That(reports.Count, Is.EqualTo(GameConstants.RoleCount));
            Assert.That(RoleCompletability.EveryLineupCompletes(), Is.True);

            foreach (var report in reports)
            {
                Assert.That(report.IsCompletable, Is.True,
                    "§11's 절대 규칙: a match without the " + report.MissingRole + " must still be finishable, "
                    + "or that role is compulsory and the pool of five is a fiction. Blocker: " + report.Blocker);
                Assert.That(report.Blocker, Is.Null);
            }
        }

        /// <summary>
        /// Every absence has to cost something, or the choice would be cosmetic. §11
        /// makes the missing role "그 판의 난제".
        /// </summary>
        [Test]
        public void Section11_EveryLineup_PaysForItsMissingRole()
        {
            foreach (var report in RoleCompletability.VerifyAllLineups())
            {
                Assert.That(report.DegradedCount, Is.GreaterThan(0),
                    "Losing the " + report.MissingRole + " costs this party nothing, so §11's \"그게 그 판의 "
                    + "성격이 된다\" would describe nothing.");
            }
        }

        /// <summary>
        /// Why the check passes: the two things a match cannot be finished without are
        /// covered by what every party has, never by a role. §11 says so outright —
        /// "그래서 손전등을 전원 기본 지급했다 — 정비공은 효율이지 자격이 아니다."
        /// </summary>
        [Test]
        public void Section11_NothingRequiredToFinish_DependsOnASingleRole()
        {
            foreach (var report in RoleCompletability.VerifyAllLineups())
            {
                foreach (var status in report.Capabilities.Where(c => c.RequiredToFinish))
                {
                    Assert.That(status.Coverage, Is.Not.EqualTo(CapabilityCoverage.Uncovered),
                        "Without the " + report.MissingRole + ", " + status.Capability
                        + " has nothing covering it — and it is required, so the match is unfinishable.");
                    Assert.That(status.Coverage, Is.Not.EqualTo(CapabilityCoverage.PurchasedSubstitute),
                        "§08 starts the team at 0 credits — \"1차 잠입 전 구매력 0\" — so a required capability "
                        + "cannot be something they have to buy first.");
                }
            }

            // The sharper form of the same claim: take the role that owns a required
            // capability out of the party, and the capability still has to stand.
            var required = RoleCompletability.Evaluate(RoleId.None).Capabilities
                .Where(c => c.RequiredToFinish)
                .ToArray();

            Assert.That(required, Is.Not.Empty, "A match that requires nothing to finish is not a match.");

            foreach (var status in required)
            {
                var withoutItsProvider = RoleCompletability.Evaluate(status.Provider).StatusOf(status.Capability);

                Assert.That(withoutItsProvider.Coverage, Is.EqualTo(CapabilityCoverage.UniversalIssue),
                    "§11: a required capability must survive its own role's absence through something every "
                    + "party has. " + status.Capability + " falls over when the " + status.Provider + " is away.");
            }
        }

        /// <summary>
        /// A missing 정비공 makes clue reading worse, not impossible — §03's darkness
        /// lock is opened by the flashlight everyone carries, and §11 issued it for
        /// exactly this reason.
        /// </summary>
        [Test]
        public void Section11_WithoutTheEngineer_ClueReadingIsDegradedButNotBlocked()
        {
            var report = RoleCompletability.Evaluate(RoleId.Engineer);
            var lighting = report.StatusOf(MatchCapability.ClueLighting);

            Assert.That(lighting.RequiredToFinish, Is.True, "§03: 어둠 = 목표의 잠금장치.");
            Assert.That(lighting.Coverage, Is.EqualTo(CapabilityCoverage.UniversalIssue));
            Assert.That(lighting.IsDegraded, Is.True, "§11: 단서 읽기가 훨씬 위험해진다.");
            Assert.That(lighting.Substitute, Is.EqualTo(RoleSubstituteItem.Flare));
            Assert.That(report.IsCompletable, Is.True);

            var safe = report.StatusOf(MatchCapability.SafeAccess);
            Assert.That(safe.Coverage, Is.EqualTo(CapabilityCoverage.Uncovered),
                "§08 sells nothing that opens a 금고.");
            Assert.That(safe.RequiredToFinish, Is.False,
                "§03 makes 전리품 optional currency — \"안 챙겨도 클리어된다\" — so a locked safe cannot "
                + "block the match.");
        }

        /// <summary>
        /// A missing 관측자 leaves a capability with no cover at all, and the match is
        /// still completable. That combination is §11's point: the irreplaceable role
        /// is not a required one.
        /// </summary>
        [Test]
        public void Section11_WithoutTheObserver_ACapabilityIsUncoveredAndTheMatchStillWorks()
        {
            var report = RoleCompletability.Evaluate(RoleId.Observer);
            var target = report.StatusOf(MatchCapability.TargetIdentity);

            Assert.That(target.Coverage, Is.EqualTo(CapabilityCoverage.Uncovered));
            Assert.That(target.Substitute, Is.EqualTo(RoleSubstituteItem.None));
            Assert.That(target.RequiredToFinish, Is.False);
            Assert.That(report.IsCompletable, Is.True,
                "§11: 관측자 없이도 판은 성립한다 — 다만 누가 표적인지 모른다.");
        }

        /// <summary>
        /// §03's escort is covered by the party size, which is what makes it
        /// role-independent. The two constants have to stay in that relationship or
        /// §11's completability argument loses its footing.
        /// </summary>
        [Test]
        public void Section11_ObjectiveEscort_IsCoveredByPartySizeNotByARole()
        {
            Assert.That(GameConstants.ObjectiveEscortMinPlayers, Is.GreaterThan(1),
                "§03: the carrier cannot hold a flashlight, so somebody else must — \"사실상 2인 1조 호송\".");
            Assert.That(GameConstants.ObjectiveEscortMinPlayers, Is.LessThanOrEqualTo(GameConstants.PlayersPerMatch),
                "If an escort needed more people than a match has, §02 would be unwinnable by construction.");

            var escort = RoleCompletability.Evaluate(RoleId.Observer).StatusOf(MatchCapability.ObjectiveEscort);
            Assert.That(escort.Provider, Is.EqualTo(RoleId.None));
            Assert.That(escort.RequiredToFinish, Is.True);
            Assert.That(escort.Coverage, Is.EqualTo(CapabilityCoverage.UniversalIssue));
        }

        // ====================================================================
        // §09 — 사망 처리 — 유령.
        // ====================================================================

        /// <summary>§09: 신호 — "근처 물건을 아주 약하게 흔든다 (쿨타임 45초)". Twice inside 45 s is the thing it must not do.</summary>
        [Test]
        public void Section09_Ghost_CannotRattleTwiceInsideTheCooldown()
        {
            var match = NewMatch(RoleId.Observer);
            Assert.That(match.PlayerAt(0).TryDescend(), Is.True);
            Assert.That(match.TryKill(0, Vec3.Zero), Is.True);

            var ghost = GhostOf(match, 0);
            var nearby = new Vec3(GameConstants.GhostRattleRange - 0.5f, 0f, 0f);

            GhostRattle first;
            Assert.That(ghost.TryRattle(nearby, out first), Is.True);
            Assert.That(first.Occurred, Is.True);
            Assert.That(first.Failure, Is.EqualTo(GhostSignalFailure.None));
            Assert.That(ghost.RattleCooldownRemaining, Is.EqualTo(GameConstants.GhostRattleCooldownSeconds).Within(1e-4f));

            GhostRattle second;
            Assert.That(ghost.TryRattle(nearby, out second), Is.False);
            Assert.That(second.Occurred, Is.False);
            Assert.That(second.Failure, Is.EqualTo(GhostSignalFailure.OnCooldown));

            // 99% of the way there is not there. Stepped in halves and hundredths of
            // the constant rather than in absolute seconds, so the test says something
            // about §09's rule and not about float rounding at 45.
            match.Tick(GameConstants.GhostRattleCooldownSeconds * 0.5f);
            Assert.That(ghost.CanRattle, Is.False);
            match.Tick(GameConstants.GhostRattleCooldownSeconds * 0.49f);
            Assert.That(ghost.CanRattle, Is.False, "§09's 45 초 is the whole reason the ghost's anguish lands.");

            GhostRattle third;
            Assert.That(ghost.TryRattle(nearby, out third), Is.False);
            Assert.That(third.Failure, Is.EqualTo(GhostSignalFailure.OnCooldown));

            match.Tick(GameConstants.GhostRattleCooldownSeconds);
            Assert.That(ghost.CanRattle, Is.True);
            Assert.That(ghost.TryRattle(nearby, out third), Is.True);
            Assert.That(ghost.RattleCount, Is.EqualTo(2));
        }

        /// <summary>
        /// A failed attempt is free. §09's penalty is the wait itself, so charging a
        /// fresh 45 s for reaching at something out of range would stack a trap on top
        /// of a punishment.
        /// </summary>
        [Test]
        public void Section09_Ghost_FailedAttempt_DoesNotBurnTheCooldown()
        {
            var ghost = new GhostState(Vec3.Zero);
            var tooFar = new Vec3(GameConstants.GhostRattleRange + 5f, 0f, 0f);

            GhostRattle attempt;
            Assert.That(ghost.TryRattle(tooFar, out attempt), Is.False);
            Assert.That(attempt.Failure, Is.EqualTo(GhostSignalFailure.OutOfRange));
            Assert.That(ghost.CanRattle, Is.True, "Nothing was spent.");
            Assert.That(ghost.RattleCount, Is.Zero);

            Assert.That(ghost.CanRattleAt(tooFar), Is.False);
            Assert.That(ghost.CanRattleAt(new Vec3(0f, 0f, GameConstants.GhostRattleRange - 0.1f)), Is.True);

            // A NaN coordinate must read as unreachable rather than slipping through
            // every comparison.
            GhostRattle nonsense;
            Assert.That(ghost.TryRattle(new Vec3(float.NaN, 0f, 0f), out nonsense), Is.False);
            Assert.That(nonsense.Failure, Is.EqualTo(GhostSignalFailure.OutOfRange));
        }

        /// <summary>
        /// A frame spike may finish the wait but must not hand out two signals, and a
        /// zero, negative, NaN or infinite delta must not advance it at all.
        /// </summary>
        [Test]
        public void Section09_Ghost_CooldownSurvivesUglyDeltas()
        {
            var ghost = new GhostState(Vec3.Zero);
            var nearby = new Vec3(1f, 0f, 0f);

            GhostRattle rattle;
            Assert.That(ghost.TryRattle(nearby, out rattle), Is.True);

            ghost.Tick(0f);
            ghost.Tick(-30f);
            ghost.Tick(float.NaN);
            ghost.Tick(float.PositiveInfinity);
            Assert.That(ghost.RattleCooldownRemaining,
                Is.EqualTo(GameConstants.GhostRattleCooldownSeconds).Within(1e-4f),
                "A garbage delta must not shorten §09's 45 초.");
            Assert.That(ghost.CanRattle, Is.False);

            // A 10-minute spike: the wait finishes, and stays finished — it does not
            // bank negative time that would make the next cooldown shorter.
            ghost.Tick(600f);
            Assert.That(ghost.RattleCooldownRemaining, Is.Zero);
            Assert.That(ghost.RattleCharge01, Is.EqualTo(1f).Within(1e-4f));

            Assert.That(ghost.TryRattle(nearby, out rattle), Is.True);
            Assert.That(ghost.TryRattle(nearby, out rattle), Is.False,
                "The spike paid for one rattle, not for a stock of them.");
            Assert.That(ghost.RattleCount, Is.EqualTo(2));
        }

        /// <summary>
        /// §09: 말하기 — 불가능. Structural, not a muted microphone: nothing on the ghost
        /// accepts or returns a message, and the living side is gated at the sender the
        /// way §13 requires ("전부 받아놓고 볼륨만 0으로 재생하면 클라이언트 조작으로 다
        /// 들린다").
        /// </summary>
        [Test]
        public void Section09_Ghost_CannotSpeakAtAll()
        {
            var match = NewMatch(RoleId.Runner);
            Assert.That(match.PlayerAt(2).TryDescend(), Is.True);

            Assert.That(match.PlayerAt(2).MayTransmitVoice, Is.True, "Alive players talk; that is the whole game.");
            Assert.That(match.TryKill(2, new Vec3(4f, -3f, 8f)), Is.True);
            Assert.That(match.PlayerAt(2).MayTransmitVoice, Is.False,
                "§09 cuts the voice at the sender, because anything cut at the receiver is not cut (§13).");

            var ghost = GhostOf(match, 2);
            Assert.That(ghost.CanSpeak, Is.False);

            var declared = typeof(GhostState).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.That(declared.Any(m => m.ReturnType == typeof(string)), Is.False,
                "§09's balance note is \"죽은 사람이 정보를 주면 밸런스 붕괴\". A ghost that can produce text "
                + "is a ghost that can talk.");
            Assert.That(declared.Any(m => m.GetParameters().Any(p => p.ParameterType == typeof(string))), Is.False,
                "Nothing a ghost does may take a message. Its entire outbound bandwidth is one rattle every "
                + GameConstants.GhostRattleCooldownSeconds + " s.");
        }

        /// <summary>§09: 탈출 — 불가능. "사망 페널티가 명확해진다", and §02's wipe row depends on it.</summary>
        [Test]
        public void Section09_Ghost_CannotEscapeOrMoveBetweenSurfaceAndBasement()
        {
            var match = NewMatch(RoleId.Flasher);
            Assert.That(match.PlayerAt(1).TryDescend(), Is.True);
            Assert.That(match.TryKill(1, new Vec3(-2f, -9f, 3f)), Is.True);

            var dead = match.PlayerAt(1);
            Assert.That(dead.IsGhost, Is.True);
            Assert.That(dead.IsAlive, Is.False);
            Assert.That(GhostOf(match, 1).CanEscape, Is.False);

            Assert.That(match.TryExtract(1), Is.False, "§09: a ghost cannot leave.");
            Assert.That(dead.Standing, Is.EqualTo(PlayerStanding.Dead));
            Assert.That(dead.TrySurface(), Is.False);
            Assert.That(dead.TryDescend(), Is.False);
            Assert.That(match.TryKill(1, Vec3.Zero), Is.False, "Dying twice would mint a second ghost.");
            Assert.That(match.Resolution.PlayersEscaped, Is.Zero);
        }

        /// <summary>
        /// §09: 시야 — "맵 전체를 자유롭게 본다 (벽 통과)". The ghost's sight asks nothing
        /// of the world, which is why an <c>IWorldProbe</c> that reports nothing
        /// reachable changes nothing for it — and why the living, who do ask, may be
        /// unable to reach what the ghost is staring at.
        /// </summary>
        [Test]
        public void Section09_Ghost_SeesThroughGeometry()
        {
            var ghost = new GhostState(new Vec3(30f, -12f, 40f));

            Assert.That(ghost.SeesEntireMap, Is.True);
            Assert.That(ghost.Position, Is.EqualTo(ghost.DeathPosition), "A ghost starts where it died.");

            ghost.MoveTo(new Vec3(-50f, 20f, -50f));
            Assert.That(ghost.Position, Is.EqualTo(new Vec3(-50f, 20f, -50f)),
                "No collision, no navmesh: §09 grants free movement.");

            ghost.MoveTo(new Vec3(float.NaN, 0f, 0f));
            Assert.That(ghost.Position, Is.EqualTo(new Vec3(-50f, 20f, -50f)),
                "A poisoned position would turn every later range check into NaN.");
        }

        /// <summary>
        /// §08 → §09: "유령이 된 본인은 자기 물건이 어디 있는지 보이는데 말할 수 없다."
        /// The pile is where the body is, the ghost knows the coordinate exactly, and
        /// the only way to point at it is to go and shake something next to it.
        /// </summary>
        [Test]
        public void Section09_Ghost_SeesItsOwnLootAndCannotSayWhereItIs()
        {
            var deathSpot = new Vec3(18f, -6f, -22f);
            var match = NewMatch(RoleId.Listener);
            Assert.That(match.PlayerAt(3).TryDescend(), Is.True);
            match.PlayerAt(3).SetLootState(true, false);
            Assert.That(match.TryKill(3, deathSpot), Is.True);

            var ghost = GhostOf(match, 3);
            Assert.That(match.PlayerAt(3).DeathPosition, Is.EqualTo(deathSpot),
                "§08: 사망자의 전리품은 떨어진다 — 회수하려면 시체가 있는 곳으로 돌아가야 한다.");
            Assert.That(match.PlayerAt(3).HoldsLoot, Is.False, "The hands are empty; the floor is not.");

            Assert.That(ghost.SeesOwnDroppedLoot, Is.False, "Nothing is confirmed on the floor yet.");
            ghost.NoteOwnLootDropped(GameConstants.LootValueLargePiece);
            Assert.That(ghost.SeesOwnDroppedLoot, Is.True);
            Assert.That(ghost.OwnDroppedLootPosition, Is.EqualTo(deathSpot));
            Assert.That(ghost.OwnDroppedLootValue, Is.EqualTo(GameConstants.LootValueLargePiece));

            // Twenty metres away it can see the pile perfectly and can do nothing at
            // all about it. Walking over and rattling something is the only "pointing"
            // the design allows, once every 45 s.
            ghost.MoveTo(deathSpot + new Vec3(20f, 0f, 0f));
            Assert.That(ghost.DistanceToOwnDroppedLoot, Is.EqualTo(20f).Within(1e-3f));
            Assert.That(ghost.CanRattleAt(deathSpot), Is.False);

            ghost.MoveTo(deathSpot + new Vec3(1f, 0f, 0f));
            GhostRattle rattle;
            Assert.That(ghost.TryRattle(deathSpot, out rattle), Is.True);
            Assert.That(rattle.Position, Is.EqualTo(deathSpot),
                "The signal means \"here\" and nothing else — that is the whole vocabulary §09 allows.");

            ghost.NoteOwnLootRecovered();
            Assert.That(ghost.SeesOwnDroppedLoot, Is.False, "The team came back for it; the ghost watched.");
            Assert.That(ghost.OwnDroppedLootPosition, Is.Null);
            Assert.That(ghost.DistanceToOwnDroppedLoot, Is.EqualTo(float.PositiveInfinity));

            ghost.NoteOwnLootDropped(0);
            Assert.That(ghost.SeesOwnDroppedLoot, Is.False,
                "§08's reason for the rule is \"위험을 감수할 또 하나의 이유\"; a worthless pile is not one.");
        }

        // ====================================================================
        // §03 — carrying, injury, and the round trip.
        // ====================================================================

        /// <summary>
        /// §03: the objective takes both hands, so the carrier has no flashlight and no
        /// sprint — "누가 들 것인가" is the last decision of the match.
        /// </summary>
        [Test]
        public void Section03_ObjectiveCarrier_HasNoLightAndNoSprint()
        {
            var match = NewMatch(RoleId.Observer);
            var runner = match.Players.First(p => p.Role == RoleId.Runner);

            Assert.That(runner.CanHoldFlashlight, Is.True);
            Assert.That(runner.CanSprint, Is.True);

            Assert.That(match.TryTakeObjective(runner.Index), Is.True);
            Assert.That(runner.IsCarryingObjective, Is.True);
            Assert.That(runner.HandsFree, Is.False);
            Assert.That(runner.CanHoldFlashlight, Is.False, "§03: 양손을 쓴다 → 누군가 비춰줘야 한다.");
            Assert.That(runner.CanSprint, Is.False, "§03: 주자가 들면 질주 불가.");

            Assert.That(match.TryTakeObjective(0), Is.False, "One objective, one pair of hands.");
            Assert.That(match.ObjectiveCarrierIndex, Is.EqualTo(runner.Index));
        }

        /// <summary>§03: 전리품 동시 소지 불가 — "전리품은 그 전에 다 챙겨야 한다."</summary>
        [Test]
        public void Section03_LootAndObjective_CannotBeHeldTogether()
        {
            var match = NewMatch(RoleId.Engineer);
            var player = match.PlayerAt(0);

            player.SetLootState(true, false);
            Assert.That(player.CanTakeObjective, Is.False);
            Assert.That(match.TryTakeObjective(0), Is.False);

            player.SetLootState(false, false);
            Assert.That(match.TryTakeObjective(0), Is.True);

            var oversizeHolder = match.PlayerAt(1);
            oversizeHolder.SetLootState(false, true);
            Assert.That(oversizeHolder.HoldsLoot, Is.True,
                "§08's oversize piece is 전리품 too, whichever way it is carried.");
            Assert.That(oversizeHolder.HandsFree, Is.False);
            Assert.That(oversizeHolder.CanHoldFlashlight, Is.False);
        }

        /// <summary>
        /// A dead carrier drops the objective where they fell — the same rule §08 gives
        /// 전리품, and the only reading that leaves §02 winnable without letting a ghost
        /// carry anything out (§09).
        /// </summary>
        [Test]
        public void Section03_ObjectiveDropsWhereTheCarrierDied()
        {
            var deathSpot = new Vec3(-7f, -12f, 33f);
            var match = NewMatch(RoleId.Flasher);

            Assert.That(match.PlayerAt(0).TryDescend(), Is.True);
            Assert.That(match.TryTakeObjective(0), Is.True);
            Assert.That(match.TryKill(0, deathSpot), Is.True);

            Assert.That(match.ObjectiveInHands, Is.False);
            Assert.That(match.ObjectiveCarrierIndex, Is.EqualTo(-1));
            Assert.That(match.ObjectiveRecovered, Is.False, "It never left the building.");
            Assert.That(match.ObjectiveRestingPosition, Is.EqualTo(deathSpot));
            Assert.That(match.PlayerAt(0).IsCarryingObjective, Is.False);

            // Recoverable at a price, which is what keeps §02's question honest.
            Assert.That(match.PlayerAt(1).TryDescend(), Is.True);
            Assert.That(match.TryTakeObjective(1), Is.True);
            Assert.That(match.ObjectiveRestingPosition, Is.Null);
            Assert.That(match.PlayerAt(1).TrySurface(), Is.True);
            Assert.That(match.TryExtract(1), Is.True);
            Assert.That(match.ObjectiveRecovered, Is.True);
        }

        /// <summary>Putting the objective down is the only way to pick anything else up. §03.</summary>
        [Test]
        public void Section03_ObjectiveCanBeSetDownAndHandedOver()
        {
            var match = NewMatch(RoleId.Runner);
            var floor = new Vec3(3f, -3f, 3f);

            Assert.That(match.TryDropObjective(floor), Is.False, "Nobody is holding it.");
            Assert.That(match.TryTakeObjective(2), Is.True);
            Assert.That(match.TryDropObjective(floor), Is.True);
            Assert.That(match.ObjectiveRestingPosition, Is.EqualTo(floor));
            Assert.That(match.PlayerAt(2).IsCarryingObjective, Is.False);
            Assert.That(match.PlayerAt(2).CanHoldFlashlight, Is.True);

            Assert.That(match.TryTakeObjective(3), Is.True, "§03's last decision can be reversed until they leave.");
            Assert.That(match.ObjectiveCarrierIndex, Is.EqualTo(3));
        }

        /// <summary>
        /// §03 lists 부상 as a round-trip cost with one replenishment: "지상에서만".
        /// Being able to heal underground would delete one of the two reasons the
        /// document gives for coming back up at all.
        /// </summary>
        [Test]
        public void Section03_Injury_HealsOnlyOnTheSurface()
        {
            var match = NewMatch(RoleId.Observer);
            var player = match.PlayerAt(0);

            Assert.That(player.TryDescend(), Is.True);
            Assert.That(player.TryInjure(), Is.True, "§03: 잡혔다 탈출 시.");
            Assert.That(player.IsInjured, Is.True);
            Assert.That(player.TryInjure(), Is.False, "§03 has one 부상, not a severity ladder.");

            Assert.That(player.CanTreatInjury, Is.False);
            Assert.That(player.TryTreatInjury(), Is.False, "Underground, nothing can be done about it.");
            Assert.That(player.IsInjured, Is.True);

            Assert.That(player.TrySurface(), Is.True);
            Assert.That(player.CanTreatInjury, Is.True);
            Assert.That(player.TryTreatInjury(), Is.True);
            Assert.That(player.IsInjured, Is.False);
            Assert.That(player.TryTreatInjury(), Is.False, "Nothing left to treat.");
        }

        /// <summary>The injury survives the trip up. §03: "나가는 것은 숨 돌리기이지 리셋이 아니다."</summary>
        [Test]
        public void Section03_Injury_SurvivesTheRoundTripUntilTreated()
        {
            var match = NewMatch(RoleId.Listener);
            var player = match.PlayerAt(1);

            Assert.That(player.TryDescend(), Is.True);
            Assert.That(player.TryInjure(), Is.True);
            Assert.That(player.TrySurface(), Is.True, "They walk out hurt.");
            Assert.That(player.IsInjured, Is.True, "Surfacing does not heal by itself — treating does.");

            Assert.That(player.TryDescend(), Is.True);
            Assert.That(player.IsInjured, Is.True);
            Assert.That(player.TryInjure(), Is.False, "Still the same one 부상.");
        }

        /// <summary>A ghost is past being hurt or patched up. §09 replaces both.</summary>
        [Test]
        public void Section09_Ghost_CannotBeInjuredOrTreated()
        {
            var match = NewMatch(RoleId.Engineer);
            Assert.That(match.PlayerAt(0).TryDescend(), Is.True);
            Assert.That(match.TryKill(0, Vec3.Zero), Is.True);

            Assert.That(match.PlayerAt(0).TryInjure(), Is.False);
            Assert.That(match.PlayerAt(0).CanTreatInjury, Is.False);
            Assert.That(match.PlayerAt(0).TryTreatInjury(), Is.False);
        }

        // ====================================================================
        // Standing transitions and the ugly cases.
        // ====================================================================

        /// <summary>
        /// Escaping is terminal and out of reach: §02's escapee has left the building,
        /// so nothing that happens inside can take their escape back.
        /// </summary>
        [Test]
        public void Escaping_IsTerminal_AndOutOfTheMonstersReach()
        {
            var match = NewMatch(RoleId.Flasher);

            Assert.That(match.TryExtract(0), Is.True);
            Assert.That(match.PlayerAt(0).HasEscaped, Is.True);
            Assert.That(match.PlayerAt(0).IsAlive, Is.True, "Out is not dead.");
            Assert.That(match.PlayerAt(0).IsStillInPlay, Is.False);

            Assert.That(match.TryExtract(0), Is.False);
            Assert.That(match.TryKill(0, Vec3.Zero), Is.False);
            Assert.That(match.PlayerAt(0).TryDescend(), Is.False, "There is no going back in.");
            Assert.That(match.PlayerAt(0).Ghost, Is.Null);
            Assert.That(match.Resolution.PlayersEscaped, Is.EqualTo(1));
        }

        /// <summary>
        /// §02's 탈출 happens at the vehicle, which §08 puts on the surface — so the
        /// objective has to be walked up before it can leave, which is exactly the trip
        /// §03 says needs two people.
        /// </summary>
        [Test]
        public void Extracting_RequiresBeingOnTheSurface()
        {
            var match = NewMatch(RoleId.Observer);
            var player = match.PlayerAt(0);

            Assert.That(player.TryDescend(), Is.True);
            Assert.That(match.TryExtract(0), Is.False, "Nobody escapes from the basement floor.");
            Assert.That(player.Standing, Is.EqualTo(PlayerStanding.Underground));

            Assert.That(player.TrySurface(), Is.True);
            Assert.That(player.TrySurface(), Is.False, "Already up.");
            Assert.That(match.TryExtract(0), Is.True);
        }

        /// <summary>
        /// §03's escort needs two people alive. The rules do not forbid a lone survivor
        /// from trying it in the dark — §02's 부분 승리 row admits exactly one escapee
        /// with the objective — so this reports the situation rather than blocking it.
        /// See docs/BALANCE-FINDINGS.md.
        /// </summary>
        [Test]
        public void Finding_LoneSurvivorMayCarryTheObjectiveOutThoughSection03SaysItTakesTwo()
        {
            var match = NewMatch(RoleId.Listener);

            for (var i = 1; i < GameConstants.PlayersPerMatch; i++)
            {
                Assert.That(match.PlayerAt(i).TryDescend(), Is.True);
                Assert.That(match.TryKill(i, new Vec3(i, -4f, i)), Is.True);
            }

            Assert.That(match.StillInPlayCount, Is.EqualTo(1));
            Assert.That(match.ObjectiveEscortStillPossible, Is.False,
                "§03: \"사실상 2인 1조 호송\" — one player cannot both carry it and light the way.");

            Assert.That(match.PlayerAt(0).TryDescend(), Is.True);
            Assert.That(match.TryTakeObjective(0), Is.True, "Not forbidden: §03's escort is practice, not a rule.");
            Assert.That(match.PlayerAt(0).TrySurface(), Is.True, "In the dark, with both hands full.");
            Assert.That(match.TryExtract(0), Is.True);

            var resolution = match.Resolution;
            Assert.That(resolution.Outcome, Is.EqualTo(MatchOutcome.PartialVictory),
                "§02's 부분 승리 row is \"목표물 회수 + 일부 탈출\", and one is 일부. If the escort ever "
                + "becomes a hard requirement, this outcome becomes unreachable and both sections need a "
                + "decision — see docs/BALANCE-FINDINGS.md.");
            Assert.That(resolution.PlayersEscaped, Is.EqualTo(1));
            Assert.That(resolution.PlayersLost, Is.EqualTo(GameConstants.PlayersPerMatch - 1));
        }

        /// <summary>
        /// Nobody dies at the vehicle. §01 labels the surface "안전 · 상점" and §08
        /// "지상 차량 = 안전 지대", and §03 leaves the monster in the building when the
        /// team walks out — so a death has to happen below ground, or "나가서 쉰다"
        /// (§10) would buy the team nothing.
        /// </summary>
        [Test]
        public void Surface_IsSafe_SoDeathHappensBelowGroundOnly()
        {
            var match = NewMatch(RoleId.Observer);
            var player = match.PlayerAt(0);

            Assert.That(player.Standing, Is.EqualTo(PlayerStanding.Surface));
            Assert.That(match.TryKill(0, Vec3.Zero), Is.False, "§08: 지상 차량 = 안전 지대.");
            Assert.That(player.IsAlive, Is.True);
            Assert.That(player.Ghost, Is.Null);

            Assert.That(player.TryDescend(), Is.True);
            Assert.That(match.TryKill(0, Vec3.Zero), Is.True, "Below ground, the monster is the rule.");
        }

        /// <summary>A match cannot start from an unsettled lobby, and it cannot start from nothing.</summary>
        [Test]
        public void MatchState_RequiresASettledLineup()
        {
            Assert.Throws<ArgumentNullException>(() => new MatchState(null!));

            var halfPicked = new RoleSelection();
            Assert.That(halfPicked.TryClaim(0, RoleId.Runner), Is.True);
            Assert.Throws<ArgumentException>(() => new MatchState(halfPicked));

            var match = NewMatch(RoleId.Observer);
            Assert.That(match.Players.Count, Is.EqualTo(GameConstants.PlayersPerMatch));
            Assert.Throws<ArgumentOutOfRangeException>(() => match.PlayerAt(GameConstants.PlayersPerMatch));
            Assert.Throws<ArgumentOutOfRangeException>(() => match.TryKill(-1, Vec3.Zero));
        }

        /// <summary>
        /// Ticking a match with nobody dead in it does nothing, and every player starts
        /// at the vehicle — §01 opens above ground with an empty wallet.
        /// </summary>
        [Test]
        public void MatchState_StartsAtTheVehicle_AndTickingWithoutGhostsIsHarmless()
        {
            var match = NewMatch(RoleId.Runner);

            foreach (var player in match.Players)
            {
                Assert.That(player.Standing, Is.EqualTo(PlayerStanding.Surface));
                Assert.That(player.IsOnSurface, Is.True);
                Assert.That(player.Ghost, Is.Null);
                Assert.That(player.Role, Is.Not.EqualTo(RoleId.None));
            }

            Assert.DoesNotThrow(() => match.Tick(GameConstants.FixedStep));
            Assert.DoesNotThrow(() => match.Tick(0f));
            Assert.DoesNotThrow(() => match.Tick(float.NaN));
            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.InProgress));
            Assert.That(match.GhostCount, Is.Zero);
            Assert.That(match.EscapedCount, Is.Zero);
        }

        /// <summary>
        /// A ghost is counted as being below ground. §09 keeps it in the building —
        /// watching the team walk the wrong way is the entire content of the state.
        /// </summary>
        [Test]
        public void Section09_GhostCountsAsUnderground()
        {
            var match = NewMatch(RoleId.Flasher);
            Assert.That(match.PlayerAt(2).TryDescend(), Is.True);
            Assert.That(match.TryKill(2, new Vec3(0f, -20f, 0f)), Is.True);

            var dead = match.PlayerAt(2);
            Assert.That(dead.IsUnderground, Is.True);
            Assert.That(dead.IsOnSurface, Is.False);
            Assert.That(dead.CanSprint, Is.False);
            Assert.That(dead.CanHoldFlashlight, Is.False);
            Assert.That(dead.CanTakeObjective, Is.False);
        }
    }
}
