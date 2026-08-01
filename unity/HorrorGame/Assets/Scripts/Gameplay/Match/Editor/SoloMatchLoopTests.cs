#nullable enable

using System;
using System.Globalization;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;
using HorrorGame.EditorTools;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.Gameplay.MatchEditor
{
    /// <summary>What one headless run of §01's loop found.</summary>
    public readonly struct SoloMatchLoopReport
    {
        /// <summary>Builds a report.</summary>
        public SoloMatchLoopReport(bool passed, string summary)
        {
            Passed = passed;
            Summary = summary;
        }

        /// <summary>Whether every step of §01's loop ran.</summary>
        public bool Passed { get; }

        /// <summary>Every step, in order, with the numbers it produced.</summary>
        public string Summary { get; }
    }

    /// <summary>
    /// Drives a whole match with no frames and no Play mode, and checks that §01's loop
    /// actually happens. §14.
    /// <para>
    /// <b>Why this is possible at all.</b> Every stateful system in this project takes
    /// an explicit delta and never reads a clock (ARCHITECTURE §3), so a match can be
    /// stepped from a loop that is not Unity's. That is not a testing trick — it is the
    /// same property §13's seeded replay depends on, and this file is the cheapest
    /// proof that it still holds.
    /// </para>
    /// <para>
    /// <b>What it deliberately does not test.</b> It calls the interactables directly
    /// rather than through <c>PlayerInteractor</c>'s crosshair ray, because a raycast
    /// needs a rendered frame's worth of physics state; and it moves the player by
    /// setting a transform rather than by driving <c>PlayerMotor</c>, because §05's
    /// movement has its own PlayMode suite. So this answers "does the loop run", not
    /// "does it feel right" — §14 asks both, and only the second one needs a human.
    /// </para>
    /// <para>
    /// This lives in <c>Assembly-CSharp-Editor</c>. It is the only assembly that can
    /// see the match (a predefined assembly), the player rig (behind an asmdef) and
    /// <c>EditorSceneManager</c> at once; an <c>.asmdef</c> test assembly cannot
    /// reference a predefined one, so there is no other place to put it.
    /// </para>
    /// </summary>
    public sealed class SoloMatchLoopTests
    {
        /// <summary>Builds the solo scene, plays a whole match, and fails with the step that broke.</summary>
        [Test]
        public void Solo_match_runs_the_whole_round_trip()
        {
            // Building the scene reimports assets, and that makes Unity re-emit a
            // packaging complaint from Mirror's OpenUPM package:
            //
            //   Asset Packages/com.mirrornetworking.mirror/Mirror/Assets has no meta
            //   file, but it's in an immutable folder. The asset will be ignored.
            //
            // The Test Framework fails a test on any unexpected LogError, so that one
            // line failed this test — a test about §01's match loop — for a defect in
            // a third-party package, inside a folder Unity itself calls immutable and
            // will not let anyone fix.
            //
            // Suppressing the check is safe here only because this test proves nothing
            // by the absence of errors: RunAll asserts every step of the loop
            // explicitly and returns the one that broke. Do not copy this into a test
            // that relies on "nothing logged an error".
            LogAssert.ignoreFailingMessages = true;

            try
            {
                var report = RunAll();
                Assert.That(report.Passed, Is.True, report.Summary);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>
        /// The same run, without NUnit, so a menu item and a batch <c>-executeMethod</c>
        /// can use it. Never throws.
        /// </summary>
        public static SoloMatchLoopReport RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("§01 solo loop verification");

            try
            {
                Execute(log);
                log.AppendLine("PASS — §01's loop ran end to end.");
                return new SoloMatchLoopReport(true, log.ToString());
            }
            catch (StepFailure failure)
            {
                log.AppendLine("FAIL — " + failure.Message);
                return new SoloMatchLoopReport(false, log.ToString());
            }
            catch (Exception error)
            {
                log.AppendLine("FAIL — unexpected " + error.GetType().Name + ": " + error.Message);
                log.AppendLine(error.StackTrace);
                return new SoloMatchLoopReport(false, log.ToString());
            }
        }

        private static void Execute(StringBuilder log)
        {
            Require(SoloPlaytest.BuildScene(), "the solo scene could not be built");

            var director = UnityEngine.Object.FindFirstObjectByType<MatchDirector>();
            Require(director != null, "the built scene has no MatchDirector");

            var motor = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
            Require(motor != null, "the built scene has no player rig");

            var player = motor!.transform;
            var loadout = player.GetComponentInChildren<PlayerLoadout>();
            var interactor = player.GetComponentInChildren<PlayerInteractor>();
            var monster = UnityEngine.Object.FindFirstObjectByType<MonsterAgent>();
            Require(loadout != null && interactor != null, "the player rig is missing a loadout or an interactor");

            // ---------------------------------------------------------------
            // §03 랜덤화 — "목표물 위치 · 단서 위치 랜덤, 맵 구조 고정."
            // ---------------------------------------------------------------
            Require(director!.BeginMatch(SoloPlaytest.PlaytestSeed), "BeginMatch refused the first seed");
            var firstObjective = ObjectivePosition(director);
            var firstClues = CluePositions();

            Require(director.BeginMatch(SoloPlaytest.PlaytestSeed + 1), "BeginMatch refused the second seed");
            var secondObjective = ObjectivePosition(director);
            var secondClues = CluePositions();

            Require(
                firstObjective != secondObjective || firstClues != secondClues,
                "§03 wants the layout to vary per seed, but two seeds produced an identical objective and clue set");
            log.AppendLine("  §03 layout varies per seed: objective moved "
                + (firstObjective != secondObjective ? "yes" : "no")
                + ", clue set changed " + (firstClues != secondClues ? "yes" : "no"));

            // ---------------------------------------------------------------
            // The match under test.
            // ---------------------------------------------------------------
            Require(director.BeginMatch(SoloPlaytest.PlaytestSeed), "BeginMatch refused");

            var map = director.Map;
            Require(map != null, "the match has no map");

            var clues = UnityEngine.Object.FindObjectsByType<CluePropInteractable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Require(
                clues.Length == director.ClueCount,
                "placed " + clues.Length + " clue props for " + director.ClueCount + " clues");
            Require(
                director.ClueCount >= GameConstants.CluesRequiredToLocate,
                "§03 needs at least " + GameConstants.CluesRequiredToLocate + " clues; the layout has " + director.ClueCount);
            Require(director.ObjectiveProp != null, "§03's objective was never placed");
            Require(
                director.PlannedRoundTrips >= GameConstants.ExpectedRoundTripsMin
                && director.PlannedRoundTrips <= GameConstants.ExpectedRoundTripsMax,
                "§03 puts the round trips at " + GameConstants.ExpectedRoundTripsMin + "–"
                + GameConstants.ExpectedRoundTripsMax + "; the layout planned " + director.PlannedRoundTrips);

            var lootProps = UnityEngine.Object.FindObjectsByType<LootPropInteractable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var oversize = UnityEngine.Object.FindObjectsByType<OversizeLootInteractable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var safes = UnityEngine.Object.FindObjectsByType<LootSafeInteractable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Require(lootProps.Length > 0, "§08's table put no pocketable loot on the map");
            Require(oversize.Length == 1, "§01 names one 대형 전리품 per match; the map has " + oversize.Length);
            Require(safes.Length == 1, "§01 names one 금고 per match; the map has " + safes.Length);

            log.AppendLine("  placed " + clues.Length + " clues, 1 objective, "
                + lootProps.Length + " pocketable loot, " + oversize.Length + " oversize, " + safes.Length + " safe"
                + "   (planned round trips " + director.PlannedRoundTrips + ")");

            // ---------------------------------------------------------------
            // §08 — 대형 전리품 goes into the hands, and comes back out onto the floor.
            // "사망자의 전리품 — 떨어진다": a piece let go has to end up on a surface, not
            // at the height the hands were at. It hung in mid-air in the build the owner
            // played, which reads as broken even when every rule underneath is right.
            // Deliberately no Step between the teleports: stepping here would run §01's
            // phase transition and spend the 귀환 the rest of this file is counting.
            // ---------------------------------------------------------------
            var crate = oversize[0];
            var crateFloor = crate.transform.position.y;
            Teleport(player, crate.transform.position);

            crate.OnPressed(interactor!);
            Require(
                crate.Carry != null && crate.Carry!.CarrierCount == 1,
                "§08's 대형 전리품 could not be taken up at all (" + crate.Refusal + ")");

            var heldAt = crate.transform.position;
            Require(
                heldAt.y > crateFloor + 0.5f,
                "§08's 궤짝 is not being held anywhere near the hands; this run proves nothing about dropping it");

            crate.OnPressed(interactor!);
            Require(
                crate.Carry!.CarrierCount == 0,
                "§08's 대형 전리품 could not be put down again (" + crate.Refusal + ")");

            var restingAt = crate.transform.position;
            Require(
                restingAt.y < heldAt.y - 0.5f,
                "§08's 궤짝 stayed at the height it was released from (" + restingAt.ToString("F2") + "), "
                + "which is the piece hanging in the air");
            Require(
                Physics.Raycast(restingAt + (Vector3.up * DropCheckRiseMetres), Vector3.down, out var under,
                    DropCheckRiseMetres + DropCheckReachMetres, ~0, QueryTriggerInteraction.Ignore),
                "nothing solid is under §08's dropped 궤짝 at " + restingAt.ToString("F2"));
            Require(
                Mathf.Abs(restingAt.y - under.point.y) <= DropCheckReachMetres,
                "§08's dropped 궤짝 is floating " + (restingAt.y - under.point.y).ToString("F3")
                + " m above the surface under it");

            var pitch = Quaternion.Angle(crate.transform.rotation, Quaternion.Euler(0f, crate.transform.eulerAngles.y, 0f));
            Require(
                pitch < 0.5f,
                "§08's 궤짝 was put down tilted by " + pitch.ToString("F1") + "°; it inherited §05's camera pitch");

            log.AppendLine("  §08 대형 전리품 dropped from " + heldAt.y.ToString("F2", CultureInfo.InvariantCulture)
                + " m to " + restingAt.y.ToString("F2", CultureInfo.InvariantCulture)
                + " m, resting " + (restingAt.y - under.point.y).ToString("F3", CultureInfo.InvariantCulture)
                + " m above the surface");

            Teleport(player, map!.Entrance);

            // ---------------------------------------------------------------
            // §08 — a piece of loot goes in the pockets.
            // ---------------------------------------------------------------
            var pockets = loadout!.Inventory;
            var weightBefore = pockets.TotalWeight;
            lootProps[0].OnPressed(interactor!);
            Require(
                pockets.LootCount == 1 && pockets.TotalWeight > weightBefore,
                "§08 loot pick-up did not reach Inventory (" + lootProps[0].Refusal + ")");
            log.AppendLine("  §08 picked up " + LootText.NameOf(lootProps[0].Loot)
                + " — weight " + pockets.TotalWeight + "/" + pockets.Capacity
                + ", speed ×" + pockets.SpeedMultiplier.ToString("0.00", CultureInfo.InvariantCulture));

            // ---------------------------------------------------------------
            // §08 · §04 — the safe is the Engineer's, and nobody else's.
            // ---------------------------------------------------------------
            var safe = safes[0];
            motor.Role = RoleId.Runner;
            safe.OnHeld(interactor!, GameConstants.EngineerSafeSeconds);
            Require(!safe.Safe.IsOpen, "§08's 금고 opened for a 주자; it is the 정비공's alone");
            Require(safe.Refusal.Length > 0, "§08's 금고 refused a 주자 silently");

            motor.Role = RoleId.Engineer;
            safe.OnHeld(interactor!, GameConstants.EngineerSafeSeconds);
            Require(safe.Safe.IsOpen, "§04's " + GameConstants.EngineerSafeSeconds + "s of Engineer work did not open the 금고");
            safe.OnPressed(interactor!);
            Require(
                pockets.CountOf(LootId.SafeDocument) == 1,
                "§08's 금고 속 문서 did not reach the pockets (" + safe.Refusal + ")");
            log.AppendLine("  §04 safe: refused 주자, opened for 정비공, 문서 taken");

            // ---------------------------------------------------------------
            // §03 — no 전리품 in the hands that hold the objective.
            // ---------------------------------------------------------------
            Require(
                !director.TryTakeObjective(out var carryRefusal),
                "§03 forbids 전리품 while carrying the objective, but the pick-up was allowed");
            Require(carryRefusal.Length > 0, "the objective refused a full-handed player silently");
            log.AppendLine("  §03 objective refused while holding loot: " + carryRefusal);

            // ---------------------------------------------------------------
            // §03 — the read needs light, and loses everything without it.
            // ---------------------------------------------------------------
            var clue = clues[0];
            PushRead(director, clue.ClueId, 1f, GameConstants.ClueReadSeconds * 0.5f);
            Require(director.ClueReader.Progress > 0f, "§03's read never started under a full beam");

            PushRead(director, clue.ClueId, 0f, GameConstants.FixedStep);
            Require(
                director.ClueReader.State == ClueReadState.Interrupted
                && director.ClueReader.LastInterrupt == ClueReadInterrupt.LightLost,
                "§03 makes darkness a lock, but losing the light did not interrupt the read");
            Require(director.ClueReader.Progress == 0f, "§03 throws the progress away; it was only paused");
            log.AppendLine("  §03 read cancels when the light goes — progress reset to zero");

            var hud = UnityEngine.Object.FindFirstObjectByType<MatchHud>();
            Require(hud != null, "the match built no HUD");

            PushRead(director, clue.ClueId, 1f, GameConstants.ClueReadSeconds * 1.2f);
            Require(
                director.ClueReader.State == ClueReadState.Complete,
                "§03's read did not complete after " + GameConstants.ClueReadSeconds + "s of unbroken beam");
            Require(hud!.Clue != null && hud.Clue!.Current.IsRevealed, "the clue overlay never showed the mark");
            Require(hud.Hud != null && hud.Hud!.IsVisible, "the HUD was never instantiated");
            log.AppendLine("  §03 read completed and the overlay drew: \"" + hud.Clue!.Current.MarkText + "\"");

            // Look away. §03 keeps no record.
            PushRead(director, -1, 0f, GameConstants.FixedStep * 2f);
            Require(!hud.Clue!.Current.IsRevealed, "§03 forbids a clue log; the mark survived looking away");

            // ---------------------------------------------------------------
            // §01 — descend.
            // ---------------------------------------------------------------
            var deep = SafeUndergroundSite(map!);
            Require(!map!.IsOnSurface(deep), "every candidate site is inside the 출입구 apron; the map is unusable");

            Teleport(player, deep);
            Step(director, GameConstants.FixedStep);
            Require(!director.LocalPlayerOnSurface, "walking away from the 출입구 did not descend");
            Require(!director.Clock.IsTeamOnSurface, "§07's clock still thinks the team is on the surface");
            Require(director.State!.PlayerAt(0).IsUnderground, "§01's standing did not follow the player down");
            Require(director.Clock.ReadableElapsedSeconds == null, "§07: the hour must not be readable underground");
            log.AppendLine("  §01 descended — §07 clock hidden, " + director.Clock.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s elapsed");

            Step(director, GameConstants.ClueReadSeconds);
            var clockUnderground = director.Clock.ElapsedSeconds;
            var monsterBefore = monster != null ? monster.transform.position : Vector3.zero;

            // ---------------------------------------------------------------
            // §01 · §08 — surface, sell, restock. §03's partial reset.
            // ---------------------------------------------------------------
            Teleport(player, map.Entrance);
            Step(director, GameConstants.FixedStep);

            Require(director.LocalPlayerOnSurface, "walking into the apron did not surface");
            Require(director.Clock.IsTeamOnSurface, "§07's clock did not learn the team surfaced");
            Require(director.Clock.SurfacingCount == 1, "§07 counted " + director.Clock.SurfacingCount + " surfacings, expected 1");
            Require(
                director.Clock.ElapsedSeconds >= clockUnderground,
                "§07: 나가는 것은 숨 돌리기이지 리셋이 아니다 — the clock went backwards");
            Require(
                director.Shop.Wallet.Credits > 0,
                "§08: loot did not sell into the shared wallet on arrival at the vehicle");
            Require(pockets.LootCount == 0, "§08: the pockets were not emptied into the vehicle");
            Require(director.Clock.ReadableElapsedSeconds != null, "§07: the hour must be readable on the surface");

            if (monster != null && map.MonsterSpawn != null)
            {
                var back = Vector3.Distance(monster.transform.position, map.MonsterSpawn.position);
                Require(
                    back < NavSnapTolerance,
                    "§03's partial reset did not put the monster back at its spawn (" + back.ToString("0.00", CultureInfo.InvariantCulture) + " m away)");
                Require(!director.Clock.MonsterResetPending, "the partial reset was armed but never consumed");
                log.AppendLine("  §03 partial reset — monster back at spawn (was "
                    + Vector3.Distance(monsterBefore, map.MonsterSpawn.position).ToString("0.0", CultureInfo.InvariantCulture)
                    + " m away), clock untouched");
            }

            log.AppendLine("  §08 sold on arrival — team wallet " + director.Shop.Wallet.Credits + " credits");

            // §08's shop is the reason to come up — and arriving is not asking for it.
            // Walking into the apron is a movement; §01 makes surfacing a deliberate act
            // and the shop is the one screen that takes the mouse, so a panel that put
            // itself up on a position test read as 갑자기 상점이 열림. Selling is the half
            // that stays automatic (asserted above); opening is the half that is now a key.
            Require(
                !hud.ShopOpen,
                "§08's shop opened by itself on arrival at the vehicle, which takes the mouse away from a "
                + "player who only walked into the apron");

            var vehicle = UnityEngine.Object.FindFirstObjectByType<SurfaceVehicleInteractable>();
            Require(vehicle != null, "§08's 지상 차량 was never placed, so there is nothing to open the shop with");
            Require(
                vehicle!.Action.Length > 0,
                "the 차량 draws no verb, so the prompt cannot tell the player which key opens the shop");

            vehicle.OnPressed(interactor!);
            Require(hud.ShopOpen, "pressing the key at the 차량 did not open §08's shop (" + vehicle.Refusal + ")");

            var cheapest = ShopCatalogue.CheapestCost;
            if (director.Shop.Wallet.Credits >= cheapest)
            {
                Require(
                    director.Shop.TryBuy(ShopItemId.Battery) == PurchaseOutcome.Purchased
                    || director.Shop.Wallet.Credits < GameConstants.ShopCostBattery,
                    "§08's shop refused a purchase the wallet could afford");
            }

            log.AppendLine("  §08 shop stayed shut on arrival and opened on ["
                + PlayerInteractor.InteractKeyLabel + "] at the 차량 — cheapest item " + cheapest + " credits");

            // And it closes, or the mouse never comes back.
            director.CloseShopScreen();
            Require(!hud.ShopOpen, "§08's shop would not close, so §05's camera never comes back");

            // ---------------------------------------------------------------
            // §03 · §02 — take the objective and carry it out.
            // ---------------------------------------------------------------
            var objectiveProp = director.ObjectiveProp;
            Require(objectiveProp != null, "the objective vanished before it could be taken");

            var objectiveAt = objectiveProp!.transform.position;
            Require(!map.IsOnSurface(objectiveAt), "the objective was placed inside the 출입구 apron");

            Teleport(player, objectiveAt);
            Step(director, GameConstants.FixedStep);

            objectiveProp.OnPressed(interactor!);
            Require(objectiveProp.IsCarried, "§03's objective was not taken (" + objectiveProp.Refusal + ")");
            Require(director.State!.PlayerAt(0).IsCarryingObjective, "the match does not think anybody has the objective");
            Require(!director.State!.PlayerAt(0).CanHoldFlashlight, "§03: both hands are used, so there is no flashlight");
            Require(!pockets.CanAdd(LootId.Trinket), "§03: no 전리품 while the objective is in the hands");
            log.AppendLine("  §03 objective taken — no flashlight, no loot, speed ×"
                + pockets.SpeedMultiplier.ToString("0.00", CultureInfo.InvariantCulture));

            Teleport(player, map.Entrance);
            Step(director, GameConstants.FixedStep);

            var resolution = director.Resolution;
            Require(director.State!.ObjectiveRecovered, "§02: carrying the objective to the exit did not recover it");
            Require(
                resolution.Outcome == MatchOutcome.FullVictory,
                "§02 expected 완전 승리 (objective recovered, nobody lost); got " + resolution.Outcome);
            Require(resolution.InformationKept && resolution.LootKept && resolution.CreditsKept,
                "§02's 완전 승리 keeps everything; this one did not");
            Require(!director.IsRunning, "the match kept running after §02 decided it");
            Require(hud.End != null && hud.End!.IsVisible, "§02's end screen was never shown");

            log.AppendLine("  §02 " + resolution.Outcome + " — escaped " + resolution.PlayersEscaped
                + ", lost " + resolution.PlayersLost
                + ", clock " + director.Clock.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");

            // §03's carry ends with the match that recovered it. Taking the prop out of
            // the world is only half of it: the carry is a flag on Inventory, and
            // SpeedResolver reads it through MovementContext.CarryingObjective, so a
            // deactivated GameObject and a pair of hands that are still charged
            // ObjectiveWeight is exactly the disagreement §03 is enforced in two objects
            // to avoid. Checked here rather than after the next BeginMatch, because a
            // teardown that empties the hands would hide it.
            Require(
                !loadout!.CarryingObjective && !pockets.CarryingObjective,
                "§02 회수 took the objective out of the world but left it in the player's hands");
            Require(
                pockets.TotalWeight == 0,
                "§02 회수 left " + pockets.TotalWeight + " weight units in hands that hold nothing "
                + "(§03 charges the objective " + GameConstants.ObjectiveWeight + ")");
            log.AppendLine("  §02 회수 released the objective — load " + pockets.TotalWeight);

            // ---------------------------------------------------------------
            // §02's other terminal row: out without it. "그 판의 정보는 남는다."
            // ---------------------------------------------------------------
            Require(director.BeginMatch(SoloPlaytest.PlaytestSeed), "BeginMatch refused the 생존 run");

            // Pocket something first, so the reset below has 전리품 to fail to clear.
            // Nothing sells it on the way out: §08 pays at the vehicle on a 귀환
            // transition, and this run never leaves the surface it started on.
            var survivorLoot = UnityEngine.Object.FindObjectsByType<LootPropInteractable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Require(survivorLoot.Length > 0, "the 생존 run placed no pocketable loot");
            survivorLoot[0].OnPressed(interactor!);
            Require(
                pockets.LootCount > 0,
                "§08 loot pick-up did not reach Inventory on the 생존 run (" + survivorLoot[0].Refusal + ")");

            Require(director.TryLeaveForGood(out var leaveRefusal), "§02: leaving without the objective was refused — " + leaveRefusal);

            var survived = director.Resolution;
            Require(
                survived.Outcome == MatchOutcome.Survived,
                "§02 expected 생존 (out, no objective); got " + survived.Outcome);
            Require(survived.InformationKept, "§02: 생존 keeps what the team learned, and this one did not");
            Require(!survived.ObjectiveRecovered, "§02: nothing was carried out, yet the objective reads as recovered");
            log.AppendLine("  §02 " + survived.Outcome + " — information kept without the objective");

            // ---------------------------------------------------------------
            // §13 — a match replays from its seed, not from the last one's pockets.
            // ---------------------------------------------------------------
            // The end screen's 계속 button calls BeginMatch, so a match that ended with
            // 전리품 in the pockets is a normal-play path into the next one. §08 sells
            // whatever is in the pockets on the first 귀환 without asking which match
            // earned it, which would pay the team for loot they never carried out — and
            // §02 already priced a lost match at losing the loot.
            var carriedOver = pockets.LootCount;
            Require(carriedOver > 0, "the 생존 run ended empty-handed, so the reset below would prove nothing");

            Require(director.BeginMatch(SoloPlaytest.PlaytestSeed + 1), "BeginMatch refused the reset run");
            Require(
                pockets.LootCount == 0,
                "a new match started holding " + carriedOver + " piece(s) of the last match's 전리품, "
                + "which §08 would sell at the vehicle on its first 귀환");
            Require(
                pockets.TotalWeight == 0,
                "a new match started at load " + pockets.TotalWeight + "; §08 opens 맨몸으로 들어간다");
            Require(!loadout!.CarryingObjective, "a new match started still carrying §03's objective");
            Require(!loadout!.CarryingOversizePiece, "a new match started still holding §08's 대형 전리품");
            log.AppendLine("  §13 second BeginMatch — empty hands, load 0 (dropped "
                + carriedOver + " carried-over piece(s))");
        }

        /// <summary>
        /// Feeds <c>ClueReader</c> §03's three conditions for a stretch of match time.
        /// Distance and speed are the ideal case; the light is the variable, because it
        /// is the one §03 makes a lock rather than a penalty.
        /// </summary>
        private static void PushRead(MatchDirector director, int clueId, float lightQuality, float seconds)
        {
            var steps = Mathf.CeilToInt(seconds / GameConstants.FixedStep);
            for (var i = 0; i < steps; i++)
            {
                var context = default(ClueReadContext);
                context.ClueId = clueId;
                context.DistanceToClue = 0f;
                context.ReaderSpeed = 0f;
                context.LightQuality = lightQuality;
                context.ViewAngleDegrees = 0f;
                context.Blur = 0f;

                director.SetClueContext(context);
                director.StepMatch(GameConstants.FixedStep);
            }
        }

        private static void Step(MatchDirector director, float seconds)
        {
            var steps = Mathf.CeilToInt(seconds / GameConstants.FixedStep);
            for (var i = 0; i < steps; i++)
            {
                var context = default(ClueReadContext);
                context.ClueId = -1;
                director.SetClueContext(context);
                director.StepMatch(GameConstants.FixedStep);
            }
        }

        private static void Teleport(Transform player, Vector3 target)
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            player.position = target;

            if (controller != null)
            {
                controller.enabled = true;
            }

            Physics.SyncTransforms();
        }

        /// <summary>
        /// A 후보 지점 that is outside §01's apron and as far as possible from the
        /// monster's start. The distance matters: this run stands still for seconds at a
        /// time to read a clue, and §06's monster would legitimately walk up and end the
        /// match — which would be correct behaviour and a useless test.
        /// </summary>
        private static Vector3 SafeUndergroundSite(MatchMap map)
        {
            var best = map.Entrance;
            var bestDistance = -1f;
            var monsterStart = map.MonsterSpawn != null ? map.MonsterSpawn.position : map.Entrance;

            for (var i = 0; i < map.CandidateSites.Count; i++)
            {
                var site = map.CandidateSites[i].position;
                if (map.IsOnSurface(site))
                {
                    continue;
                }

                var distance = Vector3.Distance(site, monsterStart);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = site;
                }
            }

            return best;
        }

        private static Vector3 ObjectivePosition(MatchDirector director)
        {
            var prop = director.ObjectiveProp;
            return prop != null ? prop.transform.position : Vector3.zero;
        }

        /// <summary>
        /// A fingerprint of where every clue ended up. Positions only — this file must
        /// not be able to name what a clue says (ARCHITECTURE §4).
        /// </summary>
        private static string CluePositions()
        {
            var props = UnityEngine.Object.FindObjectsByType<CluePropInteractable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Array.Sort(props, (a, b) => a.ClueId.CompareTo(b.ClueId));

            var text = new StringBuilder();
            for (var i = 0; i < props.Length; i++)
            {
                text.Append(props[i].transform.position.ToString("F2")).Append('|');
            }

            return text.ToString();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new StepFailure(message);
            }
        }

        /// <summary>
        /// How far a NavMesh snap may move a spawn point, metres. Navigation geometry
        /// rather than balance: <c>NavMesh.SamplePosition</c> returns the nearest point
        /// on the baked surface, which is centimetres away on a flat floor.
        /// </summary>
        private const float NavSnapTolerance = 1.5f;

        /// <summary>
        /// How far above a dropped piece this file starts looking for the floor, metres.
        /// Probe geometry: a ray that starts level with the piece can start inside the
        /// surface it is resting on and report nothing.
        /// </summary>
        private const float DropCheckRiseMetres = 0.1f;

        /// <summary>
        /// How far a dropped piece may sit above the surface under it, metres. Generous
        /// against <c>Interactable.FloorClearanceMetres</c> on purpose — this is asking
        /// "is it on the floor or in the air", and the exact clearance has its own test.
        /// </summary>
        private const float DropCheckReachMetres = 0.1f;

        private sealed class StepFailure : Exception
        {
            internal StepFailure(string message)
                : base(message)
            {
            }
        }
    }
}
