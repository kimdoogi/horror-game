#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Ghost;
using HorrorGame.Core.Light;
using HorrorGame.Core.Match;
using HorrorGame.Core.Math;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;
using HorrorGame.UI;
using HorrorGame.UI.Readouts;
using HorrorGame.UI.Screens;
using NUnit.Framework;

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
            var types = new[] { typeof(ShopBoard), typeof(ShopRow), typeof(IShopRequests), typeof(LocalShopRequests) };

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
