#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HorrorGame.Audio;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Ghost;
using HorrorGame.Core.Light;
using HorrorGame.Core.Match;
using HorrorGame.Core.Math;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Threat;
using HorrorGame.Gameplay.Player;
using HorrorGame.UI;
using HorrorGame.UI.Readouts;
using HorrorGame.UI.Screens;
using HorrorGame.UI.Settings;
using HorrorGame.UI.Shell;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HorrorGame.Tests.EditMode.UI
{
    /// <summary>
    /// Assertions about what the interface is forbidden to do.
    /// <para>
    /// Most of this file is structural rather than behavioural, and deliberately so.
    /// The design's load-bearing UI rules are all <em>absences</em> — no clock
    /// underground (§07), no personal balance (§08), no clue log (§03), no voice for
    /// the dead (§09) — and an absence cannot be verified by exercising a feature.
    /// So these tests assert over the shape of the types: a clue log would have to
    /// introduce a collection, a private wallet would have to introduce a player
    /// parameter, a ghost's microphone would have to introduce a member with a name.
    /// Each of those is caught here, at build time, rather than in a playtest six
    /// months later.
    /// </para>
    /// </summary>
    public sealed class UiTests
    {
        private const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // ====================================================================
        // §07 — 시각은 지상에서만 알 수 있다.
        // ====================================================================

        [Test]
        public void Clock_IsUnreadableUnderground_WithoutAPocketWatch()
        {
            var clock = new MatchClock();
            clock.SetTeamOnSurface(false);
            clock.Tick(GameConstants.ThreatTierSeconds + 1f);

            var readout = ClockReadout.From(clock);

            Assert.That(readout.IsVisible, Is.False,
                "§07: '안에서는 시간 감각이 없다.' A HUD that keeps a clock underground removes the reason to surface and the reason to buy a 회중시계.");
            Assert.That(readout.PhaseLabel, Is.Empty,
                "§07: an underground clock that renders a placeholder is still a clock in the corner of the screen.");
        }

        [Test]
        public void Clock_BecomesReadableUnderground_OnlyBecauseOfThePurchase()
        {
            var clock = new MatchClock();
            clock.SetTeamOnSurface(false);
            clock.Tick(GameConstants.ThreatTierSeconds + 1f);

            Assert.That(ClockReadout.From(clock).IsVisible, Is.False);

            clock.SetPocketWatchOwned(true);
            var bought = ClockReadout.From(clock);

            Assert.That(bought.IsVisible, Is.True,
                "§07 names the 회중시계 as the way to read the hour underground; if the HUD ignores it the item has no effect at all.");
            Assert.That(bought.SourceLabel, Is.EqualTo("회중시계"),
                "§08 wants a purchase to be visibly earning its keep, so the readout says which of §07's two routes it came from.");
        }

        [Test]
        public void Clock_CarriesAPhaseAndNeverAStopwatch()
        {
            foreach (var member in ValueTypesOf(typeof(ClockReadout)))
            {
                Assert.That(
                    member.Value == typeof(float) || member.Value == typeof(double) || member.Value == typeof(TimeSpan),
                    Is.False,
                    "§07: '" + member.Key + "' would let a player read the elapsed time off the HUD, start a stopwatch in their head "
                    + "before descending, and keep the clock §07 took away. The readout may carry a NightPhase and nothing finer.");
            }
        }

        // ====================================================================
        // §08 — 공용 지갑. "돈이 개인 것이면 협동 게임이 아니게 된다."
        // ====================================================================

        [Test]
        public void Shop_HasNoPerPlayerVocabularyAnywhere()
        {
            var banned = new[] { "player", "personal", "private", "share", "split", "wallet", "purse", "balanceof" };
            var types = new[]
            {
                typeof(ShopBoard), typeof(ShopRow), typeof(IShopRequests), typeof(LocalShopRequests), typeof(ShopScreen),
            };

            foreach (var type in types)
            {
                foreach (var name in NamesOf(type))
                {
                    var lower = name.ToLowerInvariant();
                    foreach (var word in banned)
                    {
                        Assert.That(lower.Contains(word), Is.False,
                            "§08: '" + type.Name + "." + name + "' reads like a per-player balance. The section is explicit — "
                            + "'돈이 개인 것이면 협동 게임이 아니게 된다' — and Core's Wallet has no player parameter by design. "
                            + "The shop UI must not reintroduce one on this side of the seam.");
                    }
                }
            }
        }

        [Test]
        public void Shop_ShowsOneBalance_SoSpendingIsContested()
        {
            var wallet = new Wallet(GameConstants.ShopCostUpgradedFlashlight + GameConstants.ShopCostBattery);
            var shop = new Shop(wallet);
            var board = new ShopBoard();

            board.Refresh(shop, null, RoleId.None);
            var before = board.TeamCredits;

            Assert.That(shop.TryBuy(ShopItemId.UpgradedFlashlight), Is.EqualTo(PurchaseOutcome.Purchased));
            board.Refresh(shop, null, RoleId.None);

            Assert.That(board.TeamCredits, Is.EqualTo(before - GameConstants.ShopCostUpgradedFlashlight),
                "§08's negotiation ('강화 손전등 하나 살까, 배터리 3개 살까?') only exists because one player's purchase "
                + "is visibly subtracted from everybody's money.");
            Assert.That(RowFor(board, ShopItemId.Detector).Affordable, Is.False,
                "§08: after the flagship purchase the shared purse must no longer cover the dearest §11 substitute — that is the trade.");
        }

        [Test]
        public void Shop_ShowsEveryItemsCost_BecauseTheCostIsTheItem()
        {
            var shop = new Shop(new Wallet(0));
            var board = new ShopBoard();
            board.Refresh(shop, null, RoleId.None);

            Assert.That(board.Rows.Count, Is.EqualTo(ShopCatalogue.All.Count),
                "§08's list is closed; a shop that shows a subset is hiding a choice the team is supposed to argue about.");

            // §08 writes a 대가 for every row it does not mark "—". The flagship is the
            // one the section calls "이 목록의 대표작", and its drawback is its benefit.
            Assert.That(RowFor(board, ShopItemId.UpgradedFlashlight).Price, Is.Not.Empty,
                "§08: rendering 반경 2배 without '괴물이 2배 멀리서 본다' turns a §10 dilemma into a straight upgrade.");
            Assert.That(RowFor(board, ShopItemId.Bag).Price, Is.Not.Empty, "§08: the 가방's −10% is the whole reason it is a decision.");
            Assert.That(RowFor(board, ShopItemId.Suppressor).Price, Is.Not.Empty, "§08: the 소음기 deletes a role; it may never be shown as a plain upgrade.");
        }

        [Test]
        public void Shop_FlagsTheSuppressor_WhenTheTeamHasAListener()
        {
            var shop = new Shop(new Wallet(GameConstants.ShopCostSuppressor));
            var board = new ShopBoard();
            var roster = new List<RoleId> { RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer };

            board.Refresh(shop, roster, RoleId.Flasher);

            Assert.That(RowFor(board, ShopItemId.Suppressor).ConflictsWithRoster, Is.True,
                "§08 states it outright: '청음사가 있는 팀은 사면 안 된다.' The team may still make the mistake — §04 calls "
                + "accidents content — but not without being told.");
            Assert.That(RowFor(board, ShopItemId.Suppressor).Affordable, Is.True,
                "§08 warns; it does not forbid. Removing the option would remove the accident the design wants.");
        }

        // ====================================================================
        // §08 · §11 · §07 — the shop as it is drawn, not as it is computed.
        //
        // Every one of these goes through the built hierarchy and reads the Text
        // components a player looks at, and every purchase goes through the row's
        // own click event. The screen this replaces had a board that knew about
        // §11's substitute and a screen that drew nothing for it — a board-level
        // assertion was green throughout. A readout nobody renders is not a
        // feature, and this is the seam where that keeps happening.
        // ====================================================================

        [Test]
        public void ShopScreen_PutsEveryCostBesideItsBenefit_NotUnderneathIt()
        {
            var host = OpenShop(GameConstants.ShopCostUpgradedFlashlight * 4, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), out var screen, out _, null);

            try
            {
                foreach (var id in ShopCatalogue.All)
                {
                    var effect = RowChild(screen, id, "Effect");
                    var price = RowChild(screen, id, "Price");

                    Assert.That(effect.text, Is.EqualTo(UiStrings.ItemEffect(id)));

                    var expected = UiStrings.ItemPrice(id);
                    Assert.That(price.text, Is.EqualTo(string.IsNullOrEmpty(expected) ? UiStrings.Unknown : expected),
                        "§08 gives every row a 대가 or writes '—'. " + id + " must render one of those and never a blank, "
                        + "because a blank is indistinguishable from a row that failed to draw.");

                    Assert.That(price.fontSize, Is.EqualTo(effect.fontSize),
                        "§08: '전부 §10 딜레마 원리를 따른다 — 얻는 게 있으면 대가가 있다.' A 대가 set smaller than its 효과 is "
                        + "a shopping list with footnotes, which is exactly what the section is not.");

                    var effectRect = (RectTransform)effect.transform;
                    var priceRect = (RectTransform)price.transform;
                    Assert.That(priceRect.anchoredPosition.y, Is.EqualTo(effectRect.anchoredPosition.y).Within(0.01f),
                        "§08 calls the 강화 손전등 '이 목록의 대표작' because its drawback is its benefit. The two have to be "
                        + "read in one glance, so they sit on one line.");
                    Assert.That(priceRect.anchoredPosition.x, Is.GreaterThan(effectRect.anchoredPosition.x));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_NamesTheStandInForTheRoleThisMatchLacks()
        {
            // §11: "청음사 → 감지기." The absence is the character of the match, and §08's
            // economy is what stops it making a role compulsory — so the answer has to be
            // on the screen, not merely in the board.
            var host = OpenShop(GameConstants.ShopCostDetector, Lineup(
                RoleId.Observer, RoleId.Runner, RoleId.Engineer, RoleId.Flasher), out var screen, out _, null);

            try
            {
                var gap = PanelChild(screen, "Gap");
                Assert.That(gap.text, Does.Contain(UiStrings.Role(RoleId.Listener)));
                Assert.That(gap.text, Does.Contain(UiStrings.RoleAbsenceProblem(RoleId.Listener)),
                    "§11 gives every absence a 난제 and the shop is where the team decides what to do about it.");
                Assert.That(gap.text, Does.Contain("감지기"));
                Assert.That(gap.text, Does.Contain(GameConstants.ShopCostDetector.ToString(CultureInfo.InvariantCulture)),
                    "§08's price is what makes §11's answer a decision rather than a suggestion.");
                Assert.That(gap.text, Does.Contain(UiStrings.SubstituteLimit(RoleSubstituteItem.Detector)),
                    "§11 keeps roles worth picking by making every stand-in worse. Hiding that sells the substitute as an equal.");

                Assert.That(RowChild(screen, ShopItemId.Detector, "Note").text, Does.Contain(UiStrings.Role(RoleId.Listener)),
                    "The row §11 points at has to be findable in a list of thirteen.");
                Assert.That(RowChild(screen, ShopItemId.Bait, "Note").text, Does.Not.Contain("※"),
                    "Only one row answers this match's absence; marking more would make the mark mean nothing.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_SaysTheFlashbangIsNotStocked_RatherThanShowingNothing()
        {
            // The defect this test exists for: ShopCatalogue.SubstituteFor returns None
            // for a missing 섬광수 — §08 does not stock the 섬광탄 §11 promises — so the
            // screen marked no row and said nothing at all. A match with a hole and a
            // match with no hole rendered identically.
            var host = OpenShop(0, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), out var screen, out _, null);

            try
            {
                var gap = PanelChild(screen, "Gap");
                Assert.That(gap.text, Does.Contain(UiStrings.Role(RoleId.Flasher)));
                Assert.That(gap.text, Does.Contain(UiStrings.Substitute(RoleSubstituteItem.Flashbang)),
                    "§11 promises a 섬광탄 for a missing 섬광수. ARCHITECTURE §6 says a layer that finds two sections "
                    + "disagreeing reports it rather than picking a side.");
                Assert.That(gap.text, Does.Contain("가격 미정"),
                    "§08's 구매 목록 has no 섬광탄, and a missing price is not a free item.");
                Assert.That(screen.Board.SubstituteMissingFromShop, Is.True);
                Assert.That(screen.Board.SubstituteItem, Is.EqualTo(ShopItemId.None));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_SaysTheObserverCannotBeBought()
        {
            var host = OpenShop(GameConstants.ShopCostBlueprint, Lineup(
                RoleId.Listener, RoleId.Runner, RoleId.Engineer, RoleId.Flasher), out var screen, out _, null);

            try
            {
                Assert.That(PanelChild(screen, "Gap").text, Does.Contain("돈으로 메울 수 없다"),
                    "§11: '관측자만 대체 수단이 없다 — 유일하게 살 수 없는 정보를 제공하는 직업이다.' That asymmetry is the "
                    + "point of the row, and a shop that stayed silent would flatten it into the other four.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_RefusesOutLoud_WithTheNumberTheTeamNeeds()
        {
            // Pressed through the row's own click event. The screen this replaces made an
            // unaffordable row uninteractable, which answers "can I?" and never answers
            // "how much more?" — and §08's argument is entirely the second question.
            var host = OpenShop(GameConstants.ShopCostBattery, Lineup(
                RoleId.Observer, RoleId.Runner, RoleId.Engineer, RoleId.Flasher), out var screen, out var shop, null);

            try
            {
                Row(screen, ShopItemId.UpgradedFlashlight).onClick.Invoke();

                var shortfall = GameConstants.ShopCostUpgradedFlashlight - GameConstants.ShopCostBattery;
                Assert.That(screen.Message, Does.Contain(UiStrings.Item(ShopItemId.UpgradedFlashlight)));
                Assert.That(screen.Message, Does.Contain(UiStrings.Purchase(PurchaseOutcome.NotEnoughCredits)));
                Assert.That(screen.Message, Does.Contain(shortfall.ToString(CultureInfo.InvariantCulture)),
                    "§08's negotiation is arithmetic — '강화 손전등 하나 살까, 배터리 3개 살까?' — so a refusal has to carry "
                    + "how far short the shared purse is, which is the sentence that sends the team back down.");
                Assert.That(shop.Wallet.Credits, Is.EqualTo(GameConstants.ShopCostBattery),
                    "A refused purchase takes nothing. §08 puts the negotiation before the money moves.");
                Assert.That(RowChild(screen, ShopItemId.UpgradedFlashlight, "Note").text,
                    Does.Contain(shortfall.ToString(CultureInfo.InvariantCulture)),
                    "And the row says it too, so four people can read the whole list without pressing anything.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_MakesDeletingTheListenerTakeTwoPresses()
        {
            var host = OpenShop(GameConstants.ShopCostSuppressor * 2, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), out var screen, out var shop, null);

            try
            {
                Row(screen, ShopItemId.Suppressor).onClick.Invoke();

                Assert.That(shop.ListenerDisabled, Is.False,
                    "§08: '청음사가 있는 팀은 사면 안 된다.' The one item in the list that removes a teammate's ability "
                    + "may not go through on a stray click.");
                Assert.That(screen.Message, Does.Contain("청음사"),
                    "And the refusal has to say what was about to happen — the player who did not know cannot argue about it.");

                Row(screen, ShopItemId.Suppressor).onClick.Invoke();

                Assert.That(shop.ListenerDisabled, Is.True,
                    "§08 warns; it does not forbid, and §04 counts the team's own accidents as content. A second press "
                    + "is a decision, so it goes through.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_ForgetsTheWarning_WhenTheCursorMovesAway()
        {
            var host = OpenShop(GameConstants.ShopCostSuppressor * 2, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), out var screen, out var shop, null);

            try
            {
                Row(screen, ShopItemId.Suppressor).onClick.Invoke();
                screen.MoveSelection(-1);
                Row(screen, ShopItemId.Suppressor).onClick.Invoke();

                Assert.That(shop.ListenerDisabled, Is.False,
                    "A confirmation the cursor has left is not a confirmation. §08's 소음기 may be bought by mistake, "
                    + "but not by a keystroke aimed at something else.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_SaysWhatWasBought_AndWhatItCost()
        {
            var host = OpenShop(GameConstants.ShopCostUpgradedFlashlight, Lineup(
                RoleId.Observer, RoleId.Runner, RoleId.Engineer, RoleId.Flasher), out var screen, out var shop, null);

            try
            {
                Row(screen, ShopItemId.UpgradedFlashlight).onClick.Invoke();

                Assert.That(shop.StockOf(ShopItemId.UpgradedFlashlight), Is.EqualTo(1));
                Assert.That(screen.Message, Does.Contain(UiStrings.ItemPrice(ShopItemId.UpgradedFlashlight)),
                    "§10 wants the trade legible at the moment it is made. The flagship's 대가 is said again as the "
                    + "credits leave, which is the last point at which anybody can object.");
                Assert.That(PanelChild(screen, "Credits").text, Does.Contain("0"),
                    "§08's one balance, visibly emptied — that subtraction is what four people are arguing over.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_DrawsTheNightItIsStandingIn_BecauseItCoversTheHudsClock()
        {
            // §07 charges "상점에서 고민 ~30초" and §10 lists shopping as a dilemma against
            // the clock. The panel is drawn over the HUD, so before this the one screen
            // that costs time was also the one screen with no clock on it.
            var clock = new MatchClock(startOnSurface: true);
            clock.Tick((GameConstants.ThreatTierSeconds * 2f) + 60f);

            var host = OpenShop(0, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), out var screen, out _, clock);

            try
            {
                var night = PanelChild(screen, "Night");
                Assert.That(night.gameObject.activeSelf, Is.True);
                Assert.That(night.text, Does.Contain(UiStrings.Phase(NightPhase.LateNight)),
                    "§07: the vehicle is the surface, and '시각은 지상에서만 알 수 있다' means the hour is legible exactly here.");
                Assert.That(night.text, Does.Contain(UiStrings.Phase(NightPhase.PreDawn)),
                    "The pressure is the next rung of §07's ladder, not the current one — a team that knows it is 심야 "
                    + "still needs to know how long that lasts.");
                Assert.That(PanelChild(screen, "NightWarning").text,
                    Is.EqualTo(UiStrings.PhaseWarning(NightPhase.LateNight)),
                    "§07's 추가 column is what the hour costs: '손전등 반경이 좁아졌다.'");

                Assert.That(screen.Board.SecondsUntilNightWorsens, Is.Not.Null);
                Assert.That(screen.Board.SecondsUntilNightWorsens!.Value,
                    Is.EqualTo((GameConstants.ThreatTierSeconds * 3f) - clock.ElapsedSeconds).Within(0.5f),
                    "Derived from §07's own eight-minute rung rather than from a second copy of the threat curve.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_ChargesTheSameThirtySecondsDifferently_DependingOnTheHour()
        {
            // §07 prices "상점에서 고민" at about thirty seconds and §10 calls shopping a
            // dilemma against the clock. Thirty seconds is not the same cost at every
            // point in the night, and the screen has to be able to say so without this
            // layer writing a deadline of its own down — every term comes from the clock
            // and from GameConstants.ThreatTierSeconds.
            var early = new MatchClock(startOnSurface: true);
            early.Tick(30f);

            var late = new MatchClock(startOnSurface: true);
            late.Tick((GameConstants.ThreatTierSeconds * 2f) - 40f);

            var unhurried = OpenShop(0, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Flasher), out var calm, out _, early);
            var hurried = OpenShop(0, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Flasher), out var pressed, out _, late);

            try
            {
                calm.TickVisit(30f);
                pressed.TickVisit(30f);

                Assert.That(calm.Board.TimePressure01, Is.LessThan(0.1f),
                    "Half a minute at the van seven minutes before the next rung is close to free, and §08's growth curve "
                    + "depends on early trips being unhurried — '1차 귀환 후: 소모품 몇 개'.");
                Assert.That(pressed.Board.TimePressure01, Is.GreaterThan(0.7f),
                    "The same half minute with forty seconds left before 심야 is most of what the team had.");

                var calmVisit = PanelChild(calm, "Visit");
                var pressedVisit = PanelChild(pressed, "Visit");
                Assert.That(calmVisit.text, Is.EqualTo(pressedVisit.text),
                    "The number is the same — thirty seconds is thirty seconds. It is what it costs that differs.");
                Assert.That(pressedVisit.color, Is.Not.EqualTo(calmVisit.color),
                    "§07: '시간이 유일한 통화다.' A shop that looked identical either way would be the safe pause §10 says "
                    + "shopping is not.");
                Assert.That(pressedVisit.color.g, Is.LessThan(calmVisit.color.g),
                    "And it has to get louder in the direction every other meter in this game does — Calm → Trade → Spent.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(unhurried);
                UnityEngine.Object.DestroyImmediate(hurried);
            }
        }

        [Test]
        public void ShopScreen_CountsTheVisitEvenWithNoClockToChargeItAgainst()
        {
            var host = OpenShop(0, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), out var screen, out _, null);

            try
            {
                Assert.That(PanelChild(screen, "Visit").text, Does.Contain("0"));

                screen.TickVisit(12f);

                Assert.That(screen.Board.SecondsAtTheVehicle, Is.EqualTo(12f).Within(0.01f));
                Assert.That(PanelChild(screen, "Visit").text, Does.Contain("12"),
                    "§10 charges for the visit itself — ' 나가서 쉰다 · 구매한다 / 시간이 흐른다' — and that is true whether or "
                    + "not §07 will tell this team what the hour is.");
                Assert.That(PanelChild(screen, "Night").gameObject.activeSelf, Is.False,
                    "How long a panel has been open is not the hour of the night, so counting it is not a §07 leak.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_ShowsNoHourItIsNotEntitledTo()
        {
            // The gate is Core's, and this is the assertion that keeps it Core's: a shop
            // that drew a plausible hour of its own would undo §07's 회중시계 in one line.
            var clock = new MatchClock(startOnSurface: false);
            clock.Tick(GameConstants.ThreatTierSeconds + 30f);

            var host = OpenShop(0, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), out var screen, out _, clock);

            try
            {
                Assert.That(PanelChild(screen, "Night").gameObject.activeSelf, Is.False,
                    "§07: '안에서는 시간 감각이 없다.' The strip goes away rather than showing a placeholder — a persistent "
                    + "'시각 불명' is still a clock in the corner of the screen.");
                Assert.That(screen.Board.SecondsUntilNightWorsens, Is.Null);
                Assert.That(screen.Board.NextPhase, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_KeepsEveryRowPressable_SoARefusalCanBeSpoken()
        {
            var host = OpenShop(0, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), out var screen, out _, null);

            try
            {
                foreach (var id in ShopCatalogue.All)
                {
                    var button = Row(screen, id);
                    Assert.That(button.interactable, Is.True,
                        "A greyed-out row is an affordance and not an answer. §08's shop has to be able to say why, and a "
                        + "disabled Button swallows the press that would have asked.");
                    Assert.That(button.navigation.mode, Is.EqualTo(UnityEngine.UI.Navigation.Mode.None),
                        "This screen draws its own cursor so the keyboard and the mouse agree which row is live; uGUI's "
                        + "navigation would run a second, disagreeing one.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_MovesItsCursorWithoutAMouse_AndClampsRatherThanWraps()
        {
            var host = OpenShop(GameConstants.ShopCostBlueprint * 2, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Engineer, RoleId.Flasher), out var screen, out _, null);

            try
            {
                var last = ShopCatalogue.All.Count - 1;

                Assert.That(screen.SelectedIndex, Is.EqualTo(0));
                Assert.That(Cursor(screen, ShopCatalogue.All[0]).activeSelf, Is.True,
                    "The cursor is the only thing telling a keyboard user which row Enter will buy, and §01's rooms are dark.");

                screen.MoveSelection(3);
                Assert.That(screen.SelectedIndex, Is.EqualTo(3));
                Assert.That(Cursor(screen, ShopCatalogue.All[0]).activeSelf, Is.False);
                Assert.That(Cursor(screen, ShopCatalogue.All[3]).activeSelf, Is.True);

                screen.MoveSelection(-99);
                Assert.That(screen.SelectedIndex, Is.EqualTo(0),
                    "Clamped, not wrapped: a list that jumps to the far end loses somebody's place mid-argument.");

                screen.MoveSelection(99);
                Assert.That(screen.SelectedIndex, Is.EqualTo(last));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_DrawsAffordabilityInTheRowItself()
        {
            // No 청음사 on this roster, so the last row — the 소음기 — is an ordinary
            // unaffordable one rather than a warned one, and the comparison below is
            // about affordability alone.
            var host = OpenShop(GameConstants.ShopCostChalk, Lineup(
                RoleId.Observer, RoleId.Runner, RoleId.Engineer, RoleId.Flasher), out var screen, out _, null);

            try
            {
                // The cursor starts on row 0, so affordability is read off rows the
                // cursor is not on.
                screen.MoveSelection(ShopCatalogue.All.Count - 1);

                var affordable = Row(screen, ShopItemId.Chalk).targetGraphic as Image;
                var beyond = Row(screen, ShopItemId.Blueprint).targetGraphic as Image;

                Assert.That(affordable, Is.Not.Null);
                Assert.That(beyond, Is.Not.Null);
                Assert.That(affordable!.color, Is.EqualTo(UiStyle.RowAffordable),
                    "§08's negotiation starts from four people seeing at a glance which rows the shared purse covers.");
                Assert.That(beyond!.color, Is.EqualTo(UiStyle.RowDisabled));
                Assert.That(RowChild(screen, ShopItemId.Chalk, "Cost").color, Is.EqualTo(UiStyle.InkStrong));
                Assert.That(RowChild(screen, ShopItemId.Blueprint, "Cost").color, Is.EqualTo(UiStyle.SpentStrong),
                    "Refusals on this panel are set at panel weight. Measured on a rendered frame, UiStyle.Spent's red "
                    + "reaches 2.7:1 at the size §08's shortfall is set in — a number nobody can read is not an answer.");

                // And the cursor must not outrank the wallet. It moved to the last row,
                // which the team cannot afford; that row stays darker than one it can.
                var selected = Row(screen, ShopCatalogue.All[ShopCatalogue.All.Count - 1]).targetGraphic as Image;
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.color.r, Is.LessThan(affordable.color.r),
                    "Selection lifts a row; it does not relabel it. A cursor resting on a 250-credit item must not make "
                    + "that item look like the affordable one — §08's argument is about which rows the shared purse covers.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShopScreen_KeepsEightsListWhole_AndGroupedByItsOwnCategories()
        {
            var host = OpenShop(0, Lineup(
                RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), out var screen, out _, null);

            try
            {
                foreach (var id in ShopCatalogue.All)
                {
                    Assert.That(Row(screen, id), Is.Not.Null,
                        "§08's list is closed; a shop that draws a subset is hiding a choice the team is supposed to argue about.");
                }

                var headings = new List<string>();
                foreach (var text in screen.GetComponentsInChildren<Text>(true))
                {
                    if (text.transform.parent != null && text.transform.parent.name.StartsWith("Section_"))
                    {
                        headings.Add(text.text);
                    }
                }

                foreach (var category in new[]
                {
                    ShopCategory.Consumable, ShopCategory.Exploration, ShopCategory.Information, ShopCategory.Danger,
                })
                {
                    Assert.That(headings, Does.Contain(UiStrings.Category(category)),
                        "§08's 분류 is how the section groups the list, and a wall of thirteen identical rows is not "
                        + "readable at a glance in a dark room.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        // ====================================================================
        // §03 — 단서를 가지고 나올 수 없다.
        // ====================================================================

        [Test]
        public void ClueUi_ContainsNoCollectionOfAnyKind()
        {
            foreach (var type in new[] { typeof(ClueReadingView), typeof(ClueReadingOverlay) })
            {
                foreach (var member in ValueTypesOf(type))
                {
                    Assert.That(IsCollection(member.Value), Is.False,
                        "§03: '" + type.Name + "." + member.Key + "' is a container, and a clue log needs exactly one container. "
                        + "The section's whole information economy — forced talking, '6이었나 9였나', the revisit when the team "
                        + "remembered wrong — rests on the reader having no copy. §14's fourth verification question dies with it.");
                }
            }
        }

        [Test]
        public void ClueUi_StoresNoStructuredClueContent()
        {
            foreach (var member in ValueTypesOf(typeof(ClueReadingView)))
            {
                var ns = member.Value.Namespace ?? string.Empty;
                var structured = ns == "HorrorGame.Core.Clues"
                    && member.Value != typeof(ClueReadState)
                    && member.Value != typeof(ClueReadInterrupt)
                    && member.Value != typeof(ClueLayer);

                Assert.That(structured, Is.False,
                    "§03 / ARCHITECTURE §4: '" + member.Key + "' keeps clue content in a comparable form. The mark is flattened to "
                    + "a string on arrival so nothing survives the frame that showed it — a SiteLabel a later feature could diff "
                    + "against is a clue log with extra steps.");
            }
        }

        [Test]
        public void ClueRead_LosesItsProgress_WhenTheLightGoes()
        {
            var reader = new ClueReader();
            var lit = Context(1f);

            reader.Tick(GameConstants.ClueReadSeconds * 0.6f, lit);
            var midway = ClueReadingView.Reading(reader, ClueLayer.SitePin);
            Assert.That(midway.InProgress, Is.True);
            Assert.That(midway.Progress, Is.GreaterThan(0f));

            reader.Tick(GameConstants.FixedStep, Context(0f));
            var dark = ClueReadingView.Reading(reader, ClueLayer.SitePin);

            Assert.That(dark.LightWasLost, Is.True,
                "§03 makes darkness the lock rather than a penalty, and the overlay has to say which of the read's conditions broke.");
            Assert.That(dark.Progress, Is.EqualTo(0f),
                "§03: '어둠 = 목표의 잠금장치.' Progress that survived the dark would make the battery — and with it the entire "
                + "round-trip structure — stop mattering.");
        }

        [Test]
        public void ClueUi_ForgetsTheMark_AsSoonAsTheReadEnds()
        {
            var revealed = ClueReadingView.Revealed(
                new ClueReport(7, ClueLayer.SitePin, true,
                    FloorFeature.None, ClueGlyph.Unreadable,
                    new SiteLabel(ClueGlyph.WingMieum, ClueGlyph.Digit6, ClueGlyph.SideLeft)));

            Assert.That(revealed.MarkText, Is.EqualTo("ㅁ-6 좌"),
                "§03 plants its confusion pairs in the glyphs, so the player is shown the marks and never a sentence describing them.");

            Assert.That(ClueReadingView.None.MarkText, Is.Empty,
                "§03: the only storage is the player's memory. Looking away has to take the mark with it.");
            Assert.That(ClueReadingView.None.IsVisible, Is.False);
        }

        // ====================================================================
        // §09 — 말하기: 불가능.
        // ====================================================================

        [Test]
        public void GhostUi_HasNoVoiceWidgetToDisable()
        {
            var banned = new[] { "voice", "mic", "speak", "talk", "mute", "chat", "radio", "push" };

            foreach (var type in new[] { typeof(GhostReadout), typeof(GhostOverlay) })
            {
                foreach (var name in NamesOf(type))
                {
                    var lower = name.ToLowerInvariant();
                    foreach (var word in banned)
                    {
                        Assert.That(lower.Contains(word), Is.False,
                            "§09: '" + type.Name + "." + name + "' is a voice affordance. A muted icon or a greyed-out "
                            + "push-to-talk describes a control that exists in some other state and sends the player looking "
                            + "for it. The silence is structural — Core's GhostState has no method that takes a message — and "
                            + "§13 settles it for anyone who would rather mute a live channel: cutting at the receiver is not cutting.");
                    }
                }
            }
        }

        [Test]
        public void Ghost_ShowsTheWait_BecauseTheWaitIsTheExperience()
        {
            var ghost = new GhostState(new Vec3(0f, 0f, 0f));

            GhostRattle rattle;
            Assert.That(ghost.TryRattle(new Vec3(1f, 0f, 0f), out rattle), Is.True,
                "§09 starts a ghost off cooldown: the seconds right after it watched itself die are the most informative it will ever have.");

            var justRattled = GhostReadout.From(ghost, rattle.Failure);
            Assert.That(justRattled.CanRattle, Is.False);
            Assert.That(justRattled.CooldownSecondsLeft, Is.EqualTo((int)GameConstants.GhostRattleCooldownSeconds),
                "§09's 45 초 is the ghost's entire outbound bandwidth, so the countdown is the largest thing on its overlay.");

            ghost.Tick(GameConstants.GhostRattleCooldownSeconds);
            Assert.That(GhostReadout.From(ghost, GhostSignalFailure.None).CanRattle, Is.True);
        }

        [Test]
        public void Ghost_TellsTheTwoFailuresApart()
        {
            var ghost = new GhostState(new Vec3(0f, 0f, 0f));

            GhostRattle first;
            ghost.TryRattle(new Vec3(1f, 0f, 0f), out first);

            GhostRattle again;
            ghost.TryRattle(new Vec3(1f, 0f, 0f), out again);
            Assert.That(GhostReadout.From(ghost, again.Failure).FailureLabel, Is.EqualTo("아직 흔들 수 없다"));

            ghost.Tick(GameConstants.GhostRattleCooldownSeconds);
            GhostRattle far;
            ghost.TryRattle(new Vec3(GameConstants.GhostRattleRange * 4f, 0f, 0f), out far);

            Assert.That(GhostReadout.From(ghost, far.Failure).FailureLabel, Is.EqualTo("너무 멀다"),
                "§09 keeps the two apart because only one of them — being too far — is worth drifting somewhere to fix.");
        }

        // ====================================================================
        // §11 — 5개 중 4개. 관측자만 대체 수단이 없다.
        // ====================================================================

        [Test]
        public void Lobby_SaysTheObserverCannotBeBought()
        {
            var board = new LobbyBoard();
            board.Refresh(
                RoleSelection.FromRoles(RoleId.Listener, RoleId.Runner, RoleId.Engineer, RoleId.Flasher), 0);

            Assert.That(board.MissingRole, Is.EqualTo(RoleId.Observer));
            Assert.That(board.SubstituteExists, Is.False,
                "§11: '관측자만 대체 수단이 없다 — 유일하게 살 수 없는 정보를 제공하는 직업이다.' Offering anything here would "
                + "flatten the one absence the section makes different from the other four.");
            Assert.That(board.SubstituteName, Is.EqualTo("불가능"),
                "§11's answer for this row is a word, not a blank — a blank reads as data the lobby failed to load.");
        }

        [Test]
        public void Lobby_NamesASubstituteForEveryOtherAbsence()
        {
            foreach (var lineup in RoleSelection.AllLineups())
            {
                var board = new LobbyBoard();
                board.Refresh(lineup, 0);

                if (board.MissingRole == RoleId.Observer)
                {
                    continue;
                }

                Assert.That(board.SubstituteExists, Is.True,
                    "§11's 돈으로 메우기 column has an entry for " + UiStrings.Role(board.MissingRole)
                    + ", and the economy is what makes '필수 직업 금지' hold — the lobby has to show it.");
                Assert.That(board.AbsenceProblem, Is.Not.Empty,
                    "§11 makes the absence the match's character ('그게 그 판의 성격이 된다'), so the lobby leads with what it costs.");
            }
        }

        [Test]
        public void Lobby_ReportsTheFlashbangAsUnpriced_RatherThanInventingAPrice()
        {
            var board = new LobbyBoard();
            board.Refresh(
                RoleSelection.FromRoles(RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), 0);

            Assert.That(board.MissingRole, Is.EqualTo(RoleId.Flasher));
            Assert.That(board.SubstituteMissingFromShop, Is.True,
                "§11 promises a 섬광탄 for a missing 섬광수 and §08's 구매 목록 does not stock one. ARCHITECTURE §6 says a "
                + "layer that finds two sections disagreeing records it instead of quietly picking a side, so the lobby says "
                + "'가격 미정' rather than making a number up.");
        }

        [Test]
        public void Lobby_ConfirmsEveryLineupCanStillFinish()
        {
            foreach (var lineup in RoleSelection.AllLineups())
            {
                var board = new LobbyBoard();
                board.Refresh(lineup, 0);

                Assert.That(board.Completability, Is.Not.Null);
                Assert.That(board.Completability!.IsCompletable, Is.True,
                    "§11's 절대 규칙: '필수 직업이 있으면 풀이 가짜가 된다.' If the lobby ever has to tell a party its lineup "
                    + "cannot finish, a role has become compulsory and the pool of five is a lie.");
            }
        }

        // ====================================================================
        // §02 — 네 개의 결말은 서로 바꿔 놓을 수 없다.
        // ====================================================================

        [Test]
        public void Endings_DifferInWhatSurvives_NotJustInAWord()
        {
            var full = EndScreenReadout.From(OutcomeEvaluator.Evaluate(4, 0, 0, true));
            var partial = EndScreenReadout.From(OutcomeEvaluator.Evaluate(2, 2, 0, true));
            var survived = EndScreenReadout.From(OutcomeEvaluator.Evaluate(2, 2, 0, false));
            var wiped = EndScreenReadout.From(OutcomeEvaluator.Evaluate(0, 4, 0, false));

            Assert.That(full.Headline, Is.EqualTo("완전 승리"));
            Assert.That(partial.Headline, Is.EqualTo("부분 승리"));
            Assert.That(survived.Headline, Is.EqualTo("생존"));
            Assert.That(wiped.Headline, Is.EqualTo("패배"));

            var epilogues = new HashSet<string> { full.Epilogue, partial.Epilogue, survived.Epilogue, wiped.Epilogue };
            Assert.That(epilogues.Count, Is.EqualTo(4),
                "§02's four rows are four different things that happened; a shared closing line would make them interchangeable.");

            Assert.That(survived.Information.Kept, Is.True,
                "§02: '한 명이라도 탈출하면 그 판에서 알아낸 정보가 보존된다.' This single line is what makes '지금 나갈까?' "
                + "an argument instead of an optimisation.");
            Assert.That(survived.Objective.Kept, Is.False);

            Assert.That(wiped.IsTotalLoss, Is.True,
                "§02: '전멸하면 전리품 · 크레딧 · 정보가 전부 사라진다.'");
            Assert.That(wiped.Information.Kept, Is.False);
            Assert.That(wiped.Loot.Kept, Is.False);
            Assert.That(wiped.Credits.Kept, Is.False);

            Assert.That(survived.IsTotalLoss, Is.False,
                "§02 measures every other outcome against 패배; if 생존 also read as a total loss the asymmetry the design "
                + "is built on would be invisible on the one screen that reports it.");
        }

        [Test]
        public void EndScreen_NeverRecapsAClue()
        {
            foreach (var member in ValueTypesOf(typeof(EndScreenReadout)))
            {
                Assert.That(member.Value.Namespace, Is.Not.EqualTo("HorrorGame.Core.Clues"),
                    "§03: '" + member.Key + "' would put clue content on the results screen. That is the clue log arriving one "
                    + "screen late — the team learns to stop reading carefully and wait for the recap.");
            }
        }

        // ====================================================================
        // §01 · §10 — the HUD shows a trade while it is being paid, and otherwise nothing.
        // ====================================================================

        [Test]
        public void Hud_IsBlank_ForAnUnburdenedPlayerUnderground()
        {
            var clock = new MatchClock();
            clock.SetTeamOnSurface(false);

            var readout = HudReadout.For(
                RoleId.Engineer,
                clock,
                new Inventory(),
                // §08's opening position: one cell, no spares, wallet at zero.
                new FlashlightState(new BatteryState(1f, 0)),
                null);

            Assert.That(readout.IsEmpty, Is.True,
                "§01 is a horror game and the beam is meant to be where the player is looking. On the first descent — "
                + "'맨몸으로 들어간다' — a player walking dark with empty hands must see nothing at all; a HUD element that is "
                + "on when nothing is happening is a light source in the corner of the eye.");
        }

        [Test]
        public void Hud_ShowsTheLoadPenalty_AtTheMomentItIsPaid()
        {
            var inventory = new Inventory();
            for (var i = 0; i <= GameConstants.WeightFreeMax; i++)
            {
                Assert.That(inventory.TryAdd(LootId.Trinket), Is.True);
            }

            var load = LoadReadout.From(inventory);

            Assert.That(load.IsVisible, Is.True);
            Assert.That(load.Band, Is.EqualTo(1),
                "§08's first penalty band opens one unit past " + GameConstants.WeightFreeMax + ".");
            Assert.That(load.PenaltyPercent, Is.EqualTo(15),
                "§08 writes the band as −15%, and §10 lists '전리품을 챙긴다 / 시간을 쓰고 느려진다' as a dilemma — which it "
                + "only is if the player can see the second half.");
        }

        [Test]
        public void Hud_WarnsBeforeTheNextPieceCosts_NotAfter()
        {
            var inventory = new Inventory();
            for (var i = 0; i < GameConstants.WeightFreeMax; i++)
            {
                Assert.That(inventory.TryAdd(LootId.Trinket), Is.True);
            }

            var load = LoadReadout.From(inventory);

            Assert.That(load.IsPenalised, Is.False, "Still inside §08's free band.");
            Assert.That(load.OneMoreCosts, Is.True,
                "§10 wants the trade legible at the moment of choosing, and that moment is standing over the loot — not "
                + "discovering the speed loss afterwards.");
        }

        [Test]
        public void Hud_GivesTheSprintBarToTheRunnerAlone()
        {
            var stamina = new StaminaState(0.5f);

            Assert.That(SprintReadout.From(RoleId.Runner, stamina, true).IsVisible, Is.True);

            foreach (var role in RoleSelection.AllRoles)
            {
                if (role == RoleId.Runner)
                {
                    continue;
                }

                Assert.That(SprintReadout.From(role, stamina, true).IsVisible, Is.False,
                    "§05's network table gives the precise bar to its owner and nobody else — '스태미나 (주자) 본인만 정확히' "
                    + "— and §06 makes the twelve seconds the Runner's identity, not a universal resource.");
            }
        }

        [Test]
        public void Hud_SaysWhatTheObjectiveTakesAway()
        {
            var carrying = new ObjectiveCarryReadout(true, true);

            Assert.That(carrying.LightSurrendered, Is.True, "§03: '양손을 쓴다 → 손전등을 들 수 없다.'");
            Assert.That(carrying.LootBlocked, Is.True, "§03: '전리품 동시 소지 불가.'");
            Assert.That(carrying.SprintSurrendered, Is.True, "§03: '주자가 들면 질주 불가.'");

            Assert.That(new ObjectiveCarryReadout(true, false).SprintSurrendered, Is.False,
                "Only the 주자 loses a sprint, so only the 주자 is told about it — the HUD does not invent losses.");
            Assert.That(new ObjectiveCarryReadout(false, true).IsVisible, Is.False);
        }

        [Test]
        public void Hud_ShowsTheBattery_WhileItIsBeingSpent_AndWhenItIsGone()
        {
            var full = new FlashlightState(new BatteryState(1f, 2));
            Assert.That(LightReadout.From(full).IsVisible, Is.False,
                "Light off with charge left: nothing is being spent, so there is nothing to say. The rule is 'spending it or "
                + "out of it' rather than a percentage, because a percentage would be a tuned number with no § behind it.");

            Assert.That(full.TryTurnOn(), Is.True);
            Assert.That(LightReadout.From(full).IsVisible, Is.True,
                "§10: '손전등을 켠다 / 괴물이 본다 · 배터리를 쓴다.' The cost is shown while it is being paid.");

            var dead = new FlashlightState(new BatteryState(0f, 0));
            Assert.That(LightReadout.From(dead).IsVisible, Is.True,
                "§03: '배터리가 떨어지면 단서를 읽을 수 없다.' The lock closing is the one thing the HUD must never hide.");
            Assert.That(LightReadout.From(dead).IsDead, Is.True);

            Assert.That(LightReadout.From(null).IsVisible, Is.False,
                "An objective carrier has no flashlight at all (§03), and an empty battery bar would be a lie about why.");
        }

        // ====================================================================
        // Settings — §05's clamp, §03's lock, and a file that has to survive a restart.
        // ====================================================================

        [SetUp]
        public void RedirectSettingsToAScratchFolder()
        {
            // Never the real persistentDataPath: a test that wrote there would overwrite
            // the settings of whoever ran it and would then pass or fail depending on
            // what they happened to have configured.
            _settingsScratch = Path.Combine(Path.GetTempPath(), "HorrorGameSettingsTests", Guid.NewGuid().ToString("N"));
            SettingsStore.OverrideDirectory(_settingsScratch);
        }

        [TearDown]
        public void RestoreTheRealSettingsFolder()
        {
            SettingsStore.OverrideDirectory(null);

            if (!string.IsNullOrEmpty(_settingsScratch) && Directory.Exists(_settingsScratch))
            {
                Directory.Delete(_settingsScratch, true);
            }
        }

        private string _settingsScratch = string.Empty;

        [Test]
        public void Settings_StartAtTheDesignsOwnNumbers()
        {
            var settings = new GameSettings();

            Assert.That(settings.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovDefault),
                "§05: '기본 80.' A settings screen that shipped a different default would be quietly re-tuning "
                + "the peek every new player learns the game on.");

            Assert.That(SettingsLimits.BrightnessExposure(settings.Brightness01), Is.EqualTo(0f).Within(0.0001f),
                "The default has to be the picture ART.md graded — every luminance number in that document was "
                + "measured with no player offset applied, so a non-zero default would make them describe a frame nobody sees.");

            Assert.That(settings.VolumeMaster, Is.EqualTo(1f));
            Assert.That(settings.VolumeSfx, Is.EqualTo(1f));
            Assert.That(settings.InvertLookY, Is.False);

            Assert.That(settings.ViewMotion, Is.EqualTo(ViewMotionTuning.ScaleDefault),
                "A new player gets the whole camera. §14's questions 1 and 2 are about how the chase feels, "
                + "and shipping them a still camera by default would answer them against a game nobody made.");
        }

        [Test]
        public void ViewMotion_IsTheOneRowWithNoBalanceConsequence()
        {
            var settings = new GameSettings();

            // Every other gameplay-adjacent row on this screen is clamped to a window a
            // player cannot escape, because escaping it is an advantage: §05's field of
            // view, §03's brightness. This one goes to zero and back, because nothing in
            // ViewMotionTuning reaches a rule — the offsets land on the camera transform,
            // §05's speed table is untouched and the beam still points where PlayerLook
            // says. A player made ill by head bob has to be able to switch it off without
            // switching off the game.
            settings.ViewMotion = 0f;
            Assert.That(settings.ViewMotion, Is.EqualTo(0f),
                "zero has to be reachable, or the accessibility answer is 'stop playing'");

            settings.ViewMotion = 1f;
            Assert.That(settings.ViewMotion, Is.EqualTo(1f));

            settings.ViewMotion = 4f;
            Assert.That(settings.ViewMotion, Is.EqualTo(1f), "and it cannot be pushed past the authored amount");

            settings.ViewMotion = -3f;
            Assert.That(settings.ViewMotion, Is.EqualTo(0f));

            settings.ViewMotion = float.NaN;
            Assert.That(settings.ViewMotion, Is.EqualTo(ViewMotionTuning.ScaleDefault),
                "a NaN out of a corrupt file must not be able to produce a camera that renders nowhere");
        }

        [Test]
        public void Settings_ClampFieldOfView_ToSection05sWindow()
        {
            var settings = new GameSettings();

            settings.FieldOfViewDegrees = 130f;
            Assert.That(settings.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovMax),
                "§05: '95+ 곁눈질이 너무 쉬워 딜레마가 약화된다.' The 45° peek is the skill the whole chase is built on; "
                + "a player who can set 130 has bought it at a discount and §10's central trade stops being a trade.");

            settings.FieldOfViewDegrees = 10f;
            Assert.That(settings.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovMin),
                "§05 caps the bottom too — at 60~70 '곁눈질이 거의 안 됨', and a player cannot opt out of a technique "
                + "the map's escape arithmetic assumes they have.");

            settings.FieldOfViewDegrees = float.NaN;
            Assert.That(settings.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovDefault),
                "A NaN field of view renders nothing at all. A corrupt file must not be able to produce a black "
                + "window with no way out of it.");
        }

        [Test]
        public void Brightness_CannotUnlockWhatSection03Locked()
        {
            Assert.That(SettingsLimits.BrightnessGainSpan, Is.EqualTo(GameConstants.ClueMinReadableLightQuality),
                "§03's lock is the only threshold the design puts on light, so the display preference is derived from it "
                + "rather than invented. If this stops being a derivation, the number has no § behind it and "
                + "ARCHITECTURE §2 has been broken quietly.");

            var brightest = SettingsLimits.BrightnessExposure(1f);
            var darkest = SettingsLimits.BrightnessExposure(0f);

            Assert.That(Mathf.Pow(2f, brightest), Is.LessThan(2f),
                "§03: '어둠 = 목표의 잠금장치.' A slider that could double the light in the room would let a player read "
                + "a corridor without the flashlight — and with it the battery, the trip back up and §01's whole round "
                + "trip stop being necessary.");

            Assert.That(Mathf.Pow(2f, darkest), Is.GreaterThan(0.5f),
                "The floor matters for the same reason the ceiling does: a player who halves the light has hidden the "
                + "monster ART.md §3.8 spent a shader budget making visible at 15 m.");

            Assert.That(brightest, Is.GreaterThan(0f));
            Assert.That(darkest, Is.LessThan(0f));
        }

        [Test]
        public void Settings_SurviveARestart()
        {
            var written = new GameSettings();
            written.FieldOfViewDegrees = 74f;
            written.MouseSensitivity = 2.5f;
            written.InvertLookY = true;
            written.ViewMotion = 0.35f;
            written.VolumeMaster = 0.4f;
            written.VolumeSfx = 0.9f;
            written.VolumeAmbience = 0.1f;
            written.VolumeVoice = 0.75f;
            written.Brightness01 = 0.2f;
            written.ResolutionWidth = 2560;
            written.ResolutionHeight = 1440;
            written.RefreshRateHz = 144;
            written.FullScreenMode = FullScreenMode.Windowed;
            written.VSyncCount = 0;
            written.QualityLevel = 2;
            written.HeadphoneNoticeSeen = true;
            written.BindingOverridesJson = "{\"bindings\":[{\"action\":\"Sprint\",\"path\":\"<Keyboard>/leftCtrl\"}]}";

            Assert.That(SettingsStore.Save(written), Is.True, "The settings file could not be written to " + SettingsStore.Path);
            Assert.That(SettingsStore.Exists(), Is.True);

            // A fresh read of the bytes on disk — the same thing the next launch does.
            var read = SettingsStore.Load();

            Assert.That(read.FieldOfViewDegrees, Is.EqualTo(74f).Within(0.001f));
            Assert.That(read.MouseSensitivity, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(read.InvertLookY, Is.True);
            Assert.That(read.ViewMotion, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(read.VolumeMaster, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(read.VolumeSfx, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(read.VolumeAmbience, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(read.VolumeVoice, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(read.Brightness01, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(read.ResolutionWidth, Is.EqualTo(2560));
            Assert.That(read.ResolutionHeight, Is.EqualTo(1440));
            Assert.That(read.RefreshRateHz, Is.EqualTo(144));
            Assert.That(read.FullScreenMode, Is.EqualTo(FullScreenMode.Windowed));
            Assert.That(read.VSyncCount, Is.EqualTo(0));
            Assert.That(read.QualityLevel, Is.EqualTo(2));
            Assert.That(read.HeadphoneNoticeSeen, Is.True);

            Assert.That(read.BindingOverridesJson, Is.EqualTo(written.BindingOverridesJson),
                "The key rebinds are the one setting a player cannot re-derive by looking at the game. Losing them "
                + "between sessions is the difference between a settings screen and a settings screen that works.");

            Assert.That(read.Matches(written), Is.True,
                "Every field has to survive the round trip, not merely the ones this test happens to list.");
        }

        [Test]
        public void Settings_ClampAFileNobodyHereWrote()
        {
            // A hand-edited file, a file from a future build, a file from a mod. All the
            // same thing to the loader: bytes it did not produce.
            Directory.CreateDirectory(SettingsStore.Directory);
            File.WriteAllText(SettingsStore.Path,
                "{\"_schema\":1,\"_fieldOfViewDegrees\":400.0,\"_mouseSensitivity\":9999.0,"
                + "\"_volumeMaster\":12.0,\"_brightness01\":8.0,\"_vSyncCount\":97,\"_fullScreenMode\":41}");

            var read = SettingsStore.Load();

            Assert.That(read.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovMax),
                "§05's window is enforced on arrival, not on the slider. A player who edits the file to 400 has found "
                + "the cheat the clamp exists to prevent, and the clamp has to be somewhere they cannot reach.");
            Assert.That(read.MouseSensitivity, Is.EqualTo(SettingsLimits.MouseSensitivityMax));
            Assert.That(read.VolumeMaster, Is.EqualTo(1f));
            Assert.That(read.Brightness01, Is.EqualTo(1f), "0..1, and 1 is §03's own +20 % — not eight stops.");
            Assert.That(read.VSyncCount, Is.InRange(0, 2), "Unity accepts 0, 1 and 2. Anything else throws at apply time.");
            Assert.That(
                read.FullScreenMode == FullScreenMode.ExclusiveFullScreen
                || read.FullScreenMode == FullScreenMode.FullScreenWindow
                || read.FullScreenMode == FullScreenMode.MaximizedWindow
                || read.FullScreenMode == FullScreenMode.Windowed,
                Is.True);
        }

        [Test]
        public void Settings_RecoverFromAFileThatIsNotSettingsAtAll()
        {
            Directory.CreateDirectory(SettingsStore.Directory);
            File.WriteAllText(SettingsStore.Path, "this is not json {{{");

            var read = SettingsStore.Load();

            Assert.That(read.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovDefault),
                "A settings file is the one piece of state that survives a crash, so it is also the one that can poison "
                + "every subsequent launch. Defaults are the only safe answer.");
            Assert.That(File.Exists(SettingsStore.Path + ".broken"), Is.True,
                "The bytes are kept rather than deleted: the player is losing their bindings either way, and the file "
                + "is occasionally the whole bug report.");
        }

        [Test]
        public void Settings_NaNInTheFileCannotBlackTheScreen()
        {
            Directory.CreateDirectory(SettingsStore.Directory);
            File.WriteAllText(SettingsStore.Path, "{\"_schema\":1,\"_fieldOfViewDegrees\":NaN,\"_brightness01\":NaN,\"_volumeMaster\":NaN}");

            var read = SettingsStore.Load();

            Assert.That(float.IsNaN(read.FieldOfViewDegrees), Is.False,
                "Mathf.Clamp passes NaN straight through, so a clamp alone is not a guard. A NaN field of view is a "
                + "camera that renders nothing and a player who cannot get back to the menu to fix it.");
            Assert.That(float.IsNaN(read.Brightness01), Is.False);
            Assert.That(float.IsNaN(read.VolumeMaster), Is.False);
        }

        [Test]
        public void Settings_GiveEveryAudioBusASlider()
        {
            var settings = new GameSettings();
            settings.VolumeMaster = 0.5f;
            settings.VolumeSfx = 0.25f;
            settings.VolumeAmbience = 0.75f;
            settings.VolumeVoice = 1f;

            foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus)))
            {
                Assert.That(settings.VolumeFor(bus), Is.InRange(0f, 1f),
                    "Every bus GameAudio knows about has to resolve to something, or one family of sound is left at "
                    + "whatever the scene happened to author.");
            }

            Assert.That(settings.VolumeFor(AudioBus.Master), Is.EqualTo(0.5f));
            Assert.That(settings.VolumeFor(AudioBus.Ambience), Is.EqualTo(0.75f));
            Assert.That(settings.VolumeFor(AudioBus.Voice), Is.EqualTo(1f));

            Assert.That(settings.VolumeFor(AudioBus.Footsteps), Is.EqualTo(0.25f),
                "§04 gives the 청음사 nothing but sound and §12 makes the five floor surfaces their whole alphabet, so "
                + "the effects slider is the one control on this screen that can switch a role off. It follows the "
                + "slider the player moved — the screen warns rather than pretending the connection is not there.");
            Assert.That(settings.VolumeFor(AudioBus.Monster), Is.EqualTo(0.25f));
        }

        [Test]
        public void Settings_CloneIsIndependent_SoAScreenCanBeAbandoned()
        {
            var original = new GameSettings();
            original.FieldOfViewDegrees = 88f;

            var copy = original.Clone();
            copy.FieldOfViewDegrees = 72f;

            Assert.That(original.FieldOfViewDegrees, Is.EqualTo(88f));
            Assert.That(copy.FieldOfViewDegrees, Is.EqualTo(72f));
            Assert.That(original.Matches(copy), Is.False);
        }

        [Test]
        public void Bars_ActuallyShowTheirFraction()
        {
            // Regression. Every meter in this game was an Image with type Filled and no
            // sprite, and Image.OnPopulateMesh falls through to Graphic's when the sprite
            // is null — so fillAmount was ignored and every bar drew full at every value.
            // §03's battery, §06's twelve seconds and §03's read progress all looked
            // plausible, because a full bar is what most of them show most of the time.
            var host = new UnityEngine.GameObject("BarTest", typeof(RectTransform));
            try
            {
                var bar = UiFactory.CreateBar("Bar", host.transform, 200f, 4f);
                var fill = bar.Root.Find("Fill") as RectTransform;
                Assert.That(fill, Is.Not.Null);

                bar.SetFill(0.25f);
                Assert.That(fill!.anchorMax.x, Is.EqualTo(0.25f).Within(0.0001f),
                    "A quarter-full battery has to draw a quarter of a bar. §03 makes the battery the lock on the "
                    + "objective, and a meter that cannot report 'nearly out' is the one HUD element §01 says must "
                    + "never be hidden.");

                bar.SetFill(0f);
                Assert.That(fill.anchorMax.x, Is.EqualTo(0f).Within(0.0001f));

                bar.SetFill(1f);
                Assert.That(fill.anchorMax.x, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(fill.offsetMax, Is.EqualTo(UnityEngine.Vector2.zero),
                    "Moving an anchor does not move the offsets with it, so a bar that shrank and grew again would "
                    + "otherwise draw a few pixels past its own end.");

                bar.SetFill(2f);
                Assert.That(fill.anchorMax.x, Is.EqualTo(1f).Within(0.0001f), "Clamped rather than rejected — a meter fed a bad number should look wrong, not throw mid-chase.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Pause_ActuallyStopsTheClock()
        {
            var scaleBefore = Time.timeScale;
            try
            {
                Assert.That(MatchPause.IsPaused, Is.False);

                MatchPause.Pause();

                Assert.That(Time.timeScale, Is.EqualTo(0f),
                    "§07's clock is the game's currency and MatchDirector steps the whole match — clock, monster, clue "
                    + "read, battery — from FixedUpdate. Unity does not run FixedUpdate at zero scale, so this single "
                    + "switch is what makes the pause menu's promise true. A paused clock that keeps running charges a "
                    + "player for the time they spent reading their own key bindings.");
                Assert.That(AudioListener.pause, Is.True,
                    "The mix runs on unscaled time, so stopping the clock alone would leave the building audible behind a "
                    + "stopped game — and §04's Listener localises the monster by ear.");
                Assert.That(MatchPause.IsPaused, Is.True);

                MatchPause.Resume();

                Assert.That(Time.timeScale, Is.EqualTo(scaleBefore),
                    "Resuming restores the scale it found rather than assuming 1, because §07's night must continue from "
                    + "where it stopped and not from a value this class invented.");
                Assert.That(AudioListener.pause, Is.False);
                Assert.That(MatchPause.IsPaused, Is.False);
            }
            finally
            {
                MatchPause.Clear();
                Time.timeScale = scaleBefore;
            }
        }

        [Test]
        public void Instantiate_DoesNotCarryBindingOverrides()
        {
            // The reason InputBindings listens to the Input System instead of just
            // writing the shipped asset. Measured, not assumed: this is the difference
            // between a rebind that reaches the player's hands and one that only reaches
            // the settings screen that shows it.
            var asset = InputBindings.Asset;
            if (asset == null)
            {
                Assert.Fail(
                    "§05's scheme ships at Resources/" + PlayerInputRouter.DefaultAssetResourcePath
                    + ". Without it there is nothing to rebind and PlayerInputRouter logs an error of its own.");
                return;
            }

            var entries = InputBindings.Rebindable();
            Assert.That(entries.Count, Is.GreaterThan(0), "§05's keyboard scheme has W/A/S/D, Shift and F on it.");

            InputActionAsset? clone = null;
            try
            {
                var entry = entries[0];
                entry.Action.ApplyBindingOverride(entry.BindingIndex, "<Keyboard>/numpad7");

                clone = UnityEngine.Object.Instantiate(asset);
                var clonedAction = FindClonedAction(clone, entry.Action.name);

                Assert.That(clonedAction.bindings[entry.BindingIndex].effectivePath, Is.Not.EqualTo("<Keyboard>/numpad7"),
                    "If Object.Instantiate ever starts carrying binding overrides, the re-apply-on-enable machinery in "
                    + "InputBindings is redundant and should be deleted rather than left running. Until then it is the "
                    + "only thing standing between a player's rebind and a control scheme that ignores it.");
            }
            finally
            {
                asset.RemoveAllBindingOverrides();
                if (clone != null)
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
        }

        [Test]
        public void Rebinding_ReachesTheCopyThePlayerRigActuallyUses()
        {
            var asset = InputBindings.Asset;
            if (asset == null)
            {
                Assert.Fail("§05's scheme is missing from Resources.");
                return;
            }

            InputActionAsset? clone = null;
            try
            {
                var entries = InputBindings.Rebindable();
                var entry = entries[0];
                entry.Action.ApplyBindingOverride(entry.BindingIndex, "<Keyboard>/numpad7");

                var settings = new GameSettings();
                settings.BindingOverridesJson = InputBindings.CurrentOverridesJson();
                Assert.That(settings.BindingOverridesJson, Is.Not.Empty);

                // Exactly what PlayerInputRouter.Awake does: clone the shipped asset, then
                // enable it. Nothing tells this clone about the player's settings.
                clone = UnityEngine.Object.Instantiate(asset);
                clone.name = asset.name;

                InputBindings.Apply(settings);
                clone.Enable();

                var clonedAction = FindClonedAction(clone, entry.Action.name);
                Assert.That(clonedAction.bindings[entry.BindingIndex].effectivePath, Is.EqualTo("<Keyboard>/numpad7"),
                    "§05's scheme is 'the only place in the game that knows a key is involved', and the copy the player's "
                    + "hands are wired to is the clone — not the asset the settings screen reads. A rebind that lands only "
                    + "on the shipped asset shows the new key on screen and leaves the old one under the player's fingers, "
                    + "with nothing logged. That is the failure this listener exists to make impossible.");
            }
            finally
            {
                InputBindings.ResetToDefaults();
                if (clone != null)
                {
                    clone.Disable();
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
        }

        private static InputAction FindClonedAction(InputActionAsset clone, string actionName)
        {
            var map = clone.FindActionMap(InputBindings.PlayerMap, false);
            Assert.That(map, Is.Not.Null, "The clone lost §05's action map.");

            var action = map!.FindAction(actionName, false);
            Assert.That(action, Is.Not.Null, "The clone lost the '" + actionName + "' action.");
            return action!;
        }

        [Test]
        public void Rebinding_LeavesTheLookAxisAlone()
        {
            foreach (var entry in InputBindings.Rebindable())
            {
                Assert.That(entry.Action.name, Is.Not.EqualTo(InputBindings.LookAction),
                    "§05 makes the 45° peek '이산적 선택이 아니라 아날로그 조절' — a continuous rotation the player meters "
                    + "with the mouse. Offering it as a key would let somebody bind away the one skill the chase is built "
                    + "on, and leave a rig that cannot look at anything in between.");
            }
        }

        [Test]
        public void Sensitivity_ScalesTheLookAxis_AndInvertOnlyFlipsPitch()
        {
            var asset = InputBindings.Asset;
            if (asset == null)
            {
                Assert.Fail("§05's action asset is missing; there is no look axis to scale.");
                return;
            }

            try
            {
                InputBindings.ApplyLookScaling(2f, invertY: true);

                var map = asset.FindActionMap(InputBindings.PlayerMap, false);
                Assert.That(map, Is.Not.Null);

                var look = map!.FindAction(InputBindings.LookAction, false);
                Assert.That(look, Is.Not.Null);

                var processors = string.Empty;
                for (var i = 0; i < look!.bindings.Count; i++)
                {
                    if (look.bindings[i].groups.Contains(InputBindings.KeyboardScheme))
                    {
                        processors = look.bindings[i].effectiveProcessors;
                    }
                }

                Assert.That(processors, Does.Contain("x=2"),
                    "Sensitivity rides on the binding rather than on a field in the rig, so it survives the same clone "
                    + "the rebinds do and composes with them instead of racing them.");
                Assert.That(processors, Does.Contain("y=-2"),
                    "§05 gives yaw and pitch different jobs — yaw is 'forward' and pitch aims the beam — so invert must "
                    + "flip one axis and not both. Flipping yaw as well would reverse the definition of forward.");

                Assert.That(look.bindings[0].effectivePath, Does.Contain("Mouse"),
                    "ApplyBindingOverride clears any field the override leaves null, so carrying the path through is "
                    + "what stops a sensitivity change from unbinding the mouse entirely.");
            }
            finally
            {
                asset.RemoveAllBindingOverrides();
            }
        }

        // ====================================================================
        // Helpers.
        // ====================================================================

        private static ClueReadContext Context(float lightQuality)
        {
            var context = default(ClueReadContext);
            context.ClueId = 1;
            context.DistanceToClue = GameConstants.ClueReadRange * 0.5f;
            context.ReaderSpeed = 0f;
            context.LightQuality = lightQuality;
            context.ViewAngleDegrees = 0f;
            context.Blur = 0f;
            return context;
        }

        /// <summary>§04's five, minus one — the shape §11 says every match has.</summary>
        private static IReadOnlyList<RoleId> Lineup(RoleId a, RoleId b, RoleId c, RoleId d)
        {
            return new[] { a, b, c, d };
        }

        /// <summary>
        /// Builds the shop the way a match does and opens it.
        /// <para>
        /// The two screens are siblings under one object and the HUD is bound first,
        /// because that is exactly what <c>MatchHud</c> does — and it is how the shop
        /// finds §07's clock. Assembling it any other way here would test a shape the
        /// game does not build.
        /// </para>
        /// </summary>
        private static UnityEngine.GameObject OpenShop(
            int credits,
            IReadOnlyList<RoleId> roster,
            out ShopScreen screen,
            out Shop shop,
            MatchClock? clock)
        {
            var host = new UnityEngine.GameObject("MatchHud");

            var hudObject = new UnityEngine.GameObject("Hud");
            hudObject.transform.SetParent(host.transform, false);
            hudObject.AddComponent<HudScreen>().Bind(RoleId.Runner, clock, null, null, null);

            var shopObject = new UnityEngine.GameObject("Shop");
            shopObject.transform.SetParent(host.transform, false);
            screen = shopObject.AddComponent<ShopScreen>();

            shop = new Shop(new Wallet(credits));
            screen.Open(shop, new LocalShopRequests(shop, null), roster, MissingFrom(roster));
            return host;
        }

        private static RoleId MissingFrom(IReadOnlyList<RoleId> roster)
        {
            foreach (var role in RoleSelection.AllRoles)
            {
                var found = false;
                for (var i = 0; i < roster.Count; i++)
                {
                    if (roster[i] == role)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return role;
                }
            }

            return RoleId.None;
        }

        /// <summary>One drawn shop row's button, found the way a click finds it — by the object it lives on.</summary>
        private static UnityEngine.UI.Button Row(ShopScreen screen, ShopItemId item)
        {
            foreach (var button in screen.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (button.name == "Row_" + item)
                {
                    return button;
                }
            }

            Assert.Fail("§08's list is missing a drawn row for " + item + ".");
            return null!;
        }

        /// <summary>A named <c>Text</c> inside one shop row — "Name", "Effect", "Price", "Cost", "Note", "Stock".</summary>
        private static Text RowChild(ShopScreen screen, ShopItemId item, string child)
        {
            var found = Row(screen, item).transform.Find(child);
            Assert.That(found, Is.Not.Null, "Row " + item + " has no '" + child + "'.");

            var text = found!.GetComponent<Text>();
            Assert.That(text, Is.Not.Null, "'" + child + "' on row " + item + " is not a Text.");
            return text!;
        }

        /// <summary>The keyboard cursor's marker on one row.</summary>
        private static UnityEngine.GameObject Cursor(ShopScreen screen, ShopItemId item)
        {
            var found = Row(screen, item).transform.Find("Cursor");
            Assert.That(found, Is.Not.Null, "Row " + item + " has no cursor marker.");
            return found!.gameObject;
        }

        /// <summary>A named <c>Text</c> on the shop panel itself — "Credits", "Night", "Gap", "Message".</summary>
        private static Text PanelChild(ShopScreen screen, string name)
        {
            foreach (var text in screen.GetComponentsInChildren<Text>(true))
            {
                if (text.name == name && text.transform.parent != null && text.transform.parent.name == "Panel")
                {
                    return text;
                }
            }

            Assert.Fail("The shop panel has no '" + name + "'.");
            return null!;
        }

        private static ShopRow RowFor(ShopBoard board, ShopItemId item)
        {
            for (var i = 0; i < board.Rows.Count; i++)
            {
                if (board.Rows[i].Id == item)
                {
                    return board.Rows[i];
                }
            }

            Assert.Fail("§08's list is missing " + item + ".");
            return default(ShopRow);
        }

        /// <summary>Declared field and property names, so a base class's members are not blamed on this layer.</summary>
        private static IEnumerable<string> NamesOf(Type type)
        {
            foreach (var field in type.GetFields(Declared))
            {
                yield return field.Name;
            }

            foreach (var property in type.GetProperties(Declared))
            {
                yield return property.Name;
            }

            foreach (var method in type.GetMethods(Declared))
            {
                yield return method.Name;
                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.Name;
                }
            }

            foreach (var constructor in type.GetConstructors(Declared))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    yield return parameter.Name;
                }
            }
        }

        /// <summary>Declared members paired with the type they carry.</summary>
        private static IEnumerable<KeyValuePair<string, Type>> ValueTypesOf(Type type)
        {
            foreach (var field in type.GetFields(Declared))
            {
                yield return new KeyValuePair<string, Type>(field.Name, field.FieldType);
            }

            foreach (var property in type.GetProperties(Declared))
            {
                yield return new KeyValuePair<string, Type>(property.Name, property.PropertyType);
            }
        }

        /// <summary>
        /// Whether a type is a container. Deliberately checks the generic and indexed
        /// interfaces rather than plain <see cref="IEnumerable"/>: Unity's
        /// <c>Transform</c> implements the non-generic one so its children can be
        /// iterated, and a <c>RectTransform</c> field is not a clue log.
        /// </summary>
        private static bool IsCollection(Type type)
        {
            if (type == typeof(string))
            {
                return false;
            }

            if (type.IsArray || typeof(ICollection).IsAssignableFrom(type))
            {
                return true;
            }

            foreach (var contract in type.GetInterfaces())
            {
                if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
