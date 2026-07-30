using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HorrorGame.Core;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Session;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §08 — 전리품, 무게, 공용 지갑, 상점.
    /// <para>
    /// Two jobs. First, the weight bands and the wallet rules are the parts of §08
    /// that other systems consume as bare floats and bools, so the boundaries have
    /// to be pinned exactly where the document draws them. Second, §16-2 calls the
    /// price table the project's top open question and supplies no numbers — the
    /// prices in <see cref="GameConstants"/> are a proposal derived from §08's growth
    /// curve, and the tests below recompute that curve from the constants rather than
    /// trusting it. A retune that breaks the curve fails here.
    /// </para>
    /// </summary>
    [TestFixture]
    public class EconomyTests
    {
        // ====================================================================
        // §08 — the weight bands.
        //   ≤5 정상 · 6~10 −15% · 11~15 −30% · ≥16 질주 불가
        // ====================================================================

        /// <summary>
        /// Every boundary in §08's table, from both sides. The bands are closed at
        /// their upper bound, so 5 is still free and 6 is the first penalty — a
        /// one-off error here would silently move the moment greed starts costing.
        /// </summary>
        [Test]
        [TestCase(GameConstants.WeightFreeMax, 1f)]
        [TestCase(GameConstants.WeightFreeMax + 1, GameConstants.WeightMulLight)]
        [TestCase(GameConstants.WeightLightMax, GameConstants.WeightMulLight)]
        [TestCase(GameConstants.WeightLightMax + 1, GameConstants.WeightMulHeavy)]
        [TestCase(GameConstants.WeightHeavyMax, GameConstants.WeightMulHeavy)]
        [TestCase(GameConstants.WeightHeavyMax + 1, GameConstants.WeightMulOverloaded)]
        public void WeightBands_ChangeExactlyOnSection08sBoundaries(int totalWeight, float expected)
        {
            Assert.That(CarryLoad.BandMultiplier(totalWeight), Is.EqualTo(expected).Within(MathX.Epsilon),
                "§08's band table is what turns 욕심 into 속도 저하. Weight " + totalWeight
                + " must land in the band the document draws it in.");
        }

        /// <summary>§08 bans sprinting from weight 16 and says it applies to the Runner too.</summary>
        [Test]
        public void SprintBan_StartsExactlyAtSixteen()
        {
            Assert.That(CarryLoad.CanSprintAt(GameConstants.WeightHeavyMax), Is.True,
                "15 is the top of the −30% band, and §08 still allows a sprint there.");
            Assert.That(CarryLoad.CanSprintAt(GameConstants.WeightHeavyMax + 1), Is.False,
                "§08: 16 이상 질주 불가 (주자도).");
        }

        /// <summary>An empty-handed player must be completely unencumbered — §08's 1차 잠입 state.</summary>
        [Test]
        public void AnEmptyInventory_IsUnencumberedAndHarmless()
        {
            var inventory = new Inventory();

            Assert.That(inventory.TotalWeight, Is.Zero);
            Assert.That(inventory.SpeedMultiplier, Is.EqualTo(1f).Within(MathX.Epsilon));
            Assert.That(inventory.CanSprint, Is.True);
            Assert.That(inventory.LootValue, Is.Zero);
            Assert.That(inventory.LootCount, Is.Zero);
            Assert.That(inventory.HandsFree, Is.True);
            Assert.That(inventory.DropAll(), Is.Empty, "Nothing to drop must not throw — §08's 사망자 rule fires on empty hands too.");
            Assert.That(inventory.TryAdd(LootId.None), Is.False, "A malformed loot id must fail the pickup, not the match.");
            Assert.That(inventory.TryRemove(LootId.Trinket), Is.False);
            Assert.That(new Shop(new Wallet()).SellAll(inventory), Is.Zero,
                "§03 makes loot optional, so an empty sale is a normal outcome.");
        }

        /// <summary>
        /// The band a real inventory reports must match the pure function, at each
        /// boundary the players can actually reach without a bag.
        /// </summary>
        [Test]
        public void AnInventoryReportsTheSameBandAsTheTable()
        {
            foreach (var weight in new[]
                     {
                         GameConstants.WeightFreeMax,
                         GameConstants.WeightFreeMax + 1,
                         GameConstants.WeightLightMax,
                     })
            {
                var inventory = new Inventory();
                Fill(inventory, weight);

                Assert.That(inventory.TotalWeight, Is.EqualTo(weight));
                Assert.That(inventory.SpeedMultiplier, Is.EqualTo(CarryLoad.BandMultiplier(weight)).Within(MathX.Epsilon));
                Assert.That(inventory.WeightBand, Is.EqualTo(CarryLoad.BandIndexOf(weight)));
            }
        }

        // ====================================================================
        // §08 — the bag: +5 수용, 이동 속도 −10%.
        // ====================================================================

        /// <summary>
        /// The bag's two effects compose with the bands the way §08 says they do:
        /// "§05 배율에 곱연산으로 적용된다". Multiplicative, not additive — the
        /// difference at the first band is 0.765 against 0.75, which is the kind of
        /// gap that silently decides whether a chase is survivable.
        /// </summary>
        [Test]
        public void Bag_MultipliesTheBand_AndDoesNotAddToIt()
        {
            var inventory = new Inventory();
            Fill(inventory, GameConstants.WeightFreeMax + 1);
            Assert.That(inventory.TryEquipBag(), Is.True);

            var composed = inventory.SpeedMultiplier;
            Assert.That(composed,
                Is.EqualTo(GameConstants.WeightMulLight * GameConstants.BagSpeedMultiplier).Within(MathX.Epsilon),
                "§08: the bag is a second multiplier on the band, not a third band.");

            var additive = 1f - ((1f - GameConstants.WeightMulLight) + (1f - GameConstants.BagSpeedMultiplier));
            Assert.That(composed, Is.Not.EqualTo(additive).Within(MathX.Epsilon),
                "If these ever agree, the −15% and −10% are being summed and §08's 곱연산 rule has been lost.");

            Assert.That(inventory.TryEquipBag(), Is.False,
                "§08 lists one 가방 with one +5. Stacking would let a player reach a load the document never priced.");

            Assert.That(inventory.BandMultiplier, Is.EqualTo(GameConstants.WeightMulLight).Within(MathX.Epsilon),
                "The band alone must stay available: MovementContext carries a BagEquipped flag of its own, and a "
                + "consumer that applies the strap's −10% itself has to be able to avoid charging it twice.");
            Assert.That(composed,
                Is.EqualTo(inventory.BandMultiplier * GameConstants.BagSpeedMultiplier).Within(MathX.Epsilon));
        }

        /// <summary>
        /// §08 charges for the strap, not the contents: an empty bag is already −10%.
        /// Anything else would make buying one a free option until it was full.
        /// </summary>
        [Test]
        public void Bag_CostsItsTenPercentEvenWhenEmpty()
        {
            var inventory = new Inventory();
            Assert.That(inventory.TryEquipBag(), Is.True);

            Assert.That(inventory.TotalWeight, Is.Zero);
            Assert.That(inventory.SpeedMultiplier, Is.EqualTo(GameConstants.BagSpeedMultiplier).Within(MathX.Epsilon));
            Assert.That(inventory.WeightBand, Is.Zero, "The bag is not weight — it must not push anyone into a band.");
        }

        /// <summary>
        /// §08's capacity numbers and its band numbers pull against each other, which
        /// is §10's dilemma principle in one item: the +5 the bag grants sits entirely
        /// inside the −30% band, so its extra room can only be used by accepting the
        /// deepest penalty in the table — and then the Runner's sprint is worthless.
        /// </summary>
        [Test]
        public void Bag_ExtraCapacityIsOnlyUsableByAcceptingTheHeavyBand()
        {
            var inventory = new Inventory();
            Assert.That(inventory.TryEquipBag(), Is.True);
            Assert.That(inventory.Capacity,
                Is.EqualTo(GameConstants.BaseCarryCapacity + GameConstants.BagCapacityBonus));

            Fill(inventory, GameConstants.BaseCarryCapacity);
            Assert.That(inventory.WeightBand, Is.EqualTo(CarryLoad.BandIndexOf(GameConstants.WeightLightMax)));

            Fill(inventory, inventory.Capacity);
            Assert.That(inventory.TotalWeight, Is.EqualTo(GameConstants.BaseCarryCapacity + GameConstants.BagCapacityBonus));
            Assert.That(inventory.WeightBand, Is.EqualTo(CarryLoad.BandIndexOf(GameConstants.WeightHeavyMax)),
                "The bag's whole bonus lies inside 11~15, so using it means taking −30%.");
            Assert.That(inventory.SpeedMultiplier,
                Is.EqualTo(GameConstants.WeightMulHeavy * GameConstants.BagSpeedMultiplier).Within(MathX.Epsilon));
            Assert.That(inventory.TryAdd(LootId.Trinket), Is.False, "Capacity is a hard ceiling; §08 gives no second bag.");

            Assert.That(GameConstants.RunnerSprintSpeed * inventory.SpeedMultiplier,
                Is.LessThan(GameConstants.MonsterBaseSpeed),
                "§06/§08: a player who fills a bag has traded away the escape, whatever their role.");

            Assert.That(inventory.TryUnequipBag(), Is.False,
                "Dropping the bag would have to destroy loot the team spent time on, so it is refused while loaded.");
        }

        // ====================================================================
        // §08 — 공용 지갑. "돈이 개인 것이면 협동 게임이 아니게 된다."
        // ====================================================================

        /// <summary>
        /// A shared wallet that could go negative would let one player mortgage the
        /// other three, so every refused purchase has to leave the balance untouched.
        /// </summary>
        [Test]
        public void Wallet_CannotBeSpentBelowZero()
        {
            var wallet = new Wallet();
            Assert.That(wallet.Credits, Is.EqualTo(GameConstants.WalletStartingCredits),
                "§08: 1차 잠입 전 구매력 0 — 맨몸으로 들어간다.");

            Assert.That(wallet.TrySpend(ShopCatalogue.CheapestCost), Is.False);
            Assert.That(wallet.Credits, Is.Zero);

            wallet.Deposit(GameConstants.ShopCostBattery);
            Assert.That(wallet.TrySpend(GameConstants.ShopCostBattery + 1), Is.False,
                "One credit short is short.");
            Assert.That(wallet.Credits, Is.EqualTo(GameConstants.ShopCostBattery),
                "A failed spend must not take a partial payment.");

            Assert.That(wallet.TrySpend(GameConstants.ShopCostBattery), Is.True);
            Assert.That(wallet.Credits, Is.Zero);
            Assert.That(wallet.TotalSpent, Is.EqualTo(GameConstants.ShopCostBattery));
            Assert.That(wallet.TotalEarned, Is.EqualTo(GameConstants.ShopCostBattery));

            Assert.That(wallet.TrySpend(0), Is.True, "A free transaction is legal and changes nothing.");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => wallet.Deposit(-1), "A negative deposit is a spend in disguise, and §08 has no debt.");
            Assert.Throws<ArgumentOutOfRangeException>(() => wallet.TrySpend(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Wallet(-1));
        }

        /// <summary>
        /// §08's rule is structural, not a policy the UI applies: "크레딧은 팀 공용이다
        /// … 돈이 개인 것이면 협동 게임이 아니게 된다." So the wallet must have no way
        /// to name a player — no id parameter, no indexer, and no collection field
        /// that could hold a split. Asserted by reflection because the failure mode is
        /// someone adding a per-player convenience later and nothing noticing.
        /// </summary>
        [Test]
        public void Wallet_HasNoPerPlayerBalanceAtAll()
        {
            const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.Instance | BindingFlags.Static
                                          | BindingFlags.DeclaredOnly;

            var namedAfterAPlayer = typeof(Wallet).GetMembers(Declared)
                .Select(m => m.Name)
                .Where(n => MentionsAnIndividual(n))
                .ToArray();
            Assert.That(namedAfterAPlayer, Is.Empty,
                "These Wallet members name an individual: " + string.Join(", ", namedAfterAPlayer));

            var parameters = typeof(Wallet).GetMethods(Declared).Cast<MethodBase>()
                .Concat(typeof(Wallet).GetConstructors(Declared))
                .SelectMany(m => m.GetParameters())
                .Select(p => p.Name ?? string.Empty)
                .Where(MentionsAnIndividual)
                .ToArray();
            Assert.That(parameters, Is.Empty,
                "These Wallet parameters identify a player: " + string.Join(", ", parameters));

            Assert.That(typeof(Wallet).GetProperties(Declared).Any(p => p.GetIndexParameters().Length > 0), Is.False,
                "An indexer on the wallet is a per-player balance with extra steps.");

            var nonScalarFields = typeof(Wallet).GetFields(Declared)
                .Where(f => f.FieldType != typeof(int))
                .Select(f => f.Name + " : " + f.FieldType.Name)
                .ToArray();
            Assert.That(nonScalarFields, Is.Empty,
                "The wallet is a single integer. A collection field here could only be a split: "
                + string.Join(", ", nonScalarFields));
        }

        /// <summary>
        /// The shop spends the team's wallet and cannot tell who is standing at the
        /// counter — that anonymity is what forces §08's negotiation.
        /// </summary>
        [Test]
        public void Shop_BuysForTheTeam_NotForAPlayer()
        {
            var buy = typeof(Shop).GetMethod(nameof(Shop.TryBuy));
            Assert.That(buy, Is.Not.Null);
            Assert.That(buy!.GetParameters().Select(p => p.ParameterType),
                Is.EqualTo(new[] { typeof(ShopItemId), typeof(int) }),
                "TryBuy takes an item and a quantity. A player parameter would reintroduce private money.");

            var offenders = typeof(Shop).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters())
                .Select(p => p.Name ?? string.Empty)
                .Where(MentionsAnIndividual)
                .ToArray();
            Assert.That(offenders, Is.Empty,
                "These Shop parameters identify a player: " + string.Join(", ", offenders));
        }

        /// <summary>A refused basket must not be silently trimmed — that would resolve §08's argument for the team.</summary>
        [Test]
        public void Shop_RefusesAnUnaffordableBasketWhole()
        {
            var shop = new Shop(new Wallet(GameConstants.ShopCostBattery * 2));

            Assert.That(shop.TryBuy(ShopItemId.Battery, 3), Is.EqualTo(PurchaseOutcome.NotEnoughCredits));
            Assert.That(shop.StockOf(ShopItemId.Battery), Is.Zero, "No partial sale.");
            Assert.That(shop.Wallet.Credits, Is.EqualTo(GameConstants.ShopCostBattery * 2));

            Assert.That(shop.TryBuy(ShopItemId.Battery, 2), Is.EqualTo(PurchaseOutcome.Purchased));
            Assert.That(shop.Wallet.Credits, Is.Zero);
            Assert.That(shop.StockOf(ShopItemId.Battery), Is.EqualTo(2));

            Assert.That(shop.TryConsume(ShopItemId.Battery), Is.True);
            Assert.That(shop.TryConsume(ShopItemId.Battery), Is.True);
            Assert.That(shop.TryConsume(ShopItemId.Battery), Is.False, "§03's resource pressure arrives when the stock runs out.");

            Assert.That(shop.TryBuy(ShopItemId.None), Is.EqualTo(PurchaseOutcome.UnknownItem));
            Assert.That(shop.TryBuy(ShopItemId.Battery, 0), Is.EqualTo(PurchaseOutcome.InvalidQuantity));
            Assert.That(shop.TryBuy(ShopItemId.Battery, -4), Is.EqualTo(PurchaseOutcome.InvalidQuantity));
        }

        // ====================================================================
        // §08 전리품 table.
        // ====================================================================

        /// <summary>
        /// §08 asserts 회중시계·반지 is "효율 최고". That is a claim about the numbers,
        /// so it is computed here rather than trusted: no other piece may match or
        /// beat its credits per weight, or the table's advice becomes wrong.
        /// </summary>
        [Test]
        public void TheWatchIsStrictlyTheMostEfficientLoot()
        {
            Assert.That(LootCatalogue.MostEfficient, Is.EqualTo(LootId.Timepiece), "§08: 회중시계·반지 — 효율 최고.");

            var watch = LootCatalogue.Of(LootId.Timepiece);
            foreach (var id in LootCatalogue.All.Where(x => x != LootId.Timepiece))
            {
                Assert.That(LootCatalogue.Of(id).ValuePerWeight, Is.LessThan(watch.ValuePerWeight),
                    id + " matches or beats the watch on efficiency, which contradicts §08's table note.");
            }

            Assert.That(LootCatalogue.Of(LootId.LargePiece).Value, Is.GreaterThan(watch.Value),
                "The chest must still win on absolute value, or §08's two-person haul is never worth the risk.");
        }

        /// <summary>§08's 가치 column is an ordering — 낮음 &lt; 중간 &lt; 높음 &lt; 매우 높음.</summary>
        [Test]
        public void LootValues_FollowSection08sValueColumn()
        {
            Assert.That(GameConstants.LootValueTrinket, Is.LessThan(GameConstants.LootValueTimepiece));
            Assert.That(GameConstants.LootValueTimepiece, Is.LessThan(GameConstants.LootValueSafeDocument));
            Assert.That(GameConstants.LootValueSafeDocument, Is.LessThan(GameConstants.LootValueLargePiece));

            foreach (var id in LootCatalogue.All)
            {
                Assert.That(LootCatalogue.ValueOf(id), Is.GreaterThan(0), id + " is worth nothing, so nobody would carry it.");
                Assert.That(LootCatalogue.WeightOf(id), Is.GreaterThan(0), id + " weighs nothing, so §08's dilemma disappears.");
            }
        }

        /// <summary>
        /// §08's 무게 column, and the coincidence the whole section turns on: the
        /// large piece weighs exactly the free band, so one chest ends it.
        /// </summary>
        [Test]
        public void LootWeights_MatchSection08sTable()
        {
            Assert.That(GameConstants.LootWeightTrinket, Is.EqualTo(GameConstants.LootWeightTimepiece),
                "§08 gives both small pieces weight 1 — the choice between them is value, not load.");
            Assert.That(GameConstants.LootWeightSafeDocument, Is.GreaterThan(GameConstants.LootWeightTimepiece));
            Assert.That(GameConstants.LootWeightLargePiece, Is.EqualTo(GameConstants.WeightFreeMax),
                "§08: 대형 초상화·궤짝 weighs exactly the free band, which is why one piece uses it all up.");

            Assert.That(LootCatalogue.Of(LootId.Trinket).IsConspicuous, Is.True, "§08: 눈에 잘 보임 (유혹).");
            Assert.That(LootCatalogue.Of(LootId.SafeDocument).GatedBy, Is.EqualTo(RoleId.Engineer), "§08: 금고를 열어야 함 (정비공).");
            Assert.That(LootCatalogue.Of(LootId.LargePiece).AllowsSharedCarry, Is.True, "§08: 2인 운반.");
            Assert.That(LootCatalogue.Of(LootId.Timepiece).AllowsSharedCarry, Is.False,
                "A weight-1 piece has no second handle; co-carrying anything would erase §08's corridor scene.");
        }

        // ====================================================================
        // §08 — 2인 운반, and letting go.
        // ====================================================================

        /// <summary>
        /// Splitting a chest must never make weight vanish from the team's total, so
        /// the halves round up: 5 becomes 3 and 3, not 2 and 2.
        /// </summary>
        [Test]
        public void AChestSplitsBetweenTwoCarriers_RoundingUp()
        {
            var chest = new SharedLootCarry(LootId.LargePiece);
            var first = new Inventory();
            var second = new Inventory();

            Assert.That(chest.TryPickUp(first), Is.True);
            Assert.That(first.TotalWeight, Is.EqualTo(GameConstants.LootWeightLargePiece),
                "Alone, you carry all of it — §08's other option, 극심한 감속.");
            Assert.That(first.HandsFree, Is.False, "§03's 양손 model: an oversize piece fills the hands.");

            Assert.That(chest.TryPickUp(second), Is.True);
            Assert.That(chest.CarrierCount, Is.EqualTo(GameConstants.SharedCarryMaxCarriers));
            Assert.That(first.TotalWeight, Is.EqualTo(chest.WeightPerCarrier));
            Assert.That(second.TotalWeight, Is.EqualTo(chest.WeightPerCarrier));
            Assert.That(chest.WeightPerCarrier * GameConstants.SharedCarryMaxCarriers,
                Is.GreaterThanOrEqualTo(GameConstants.LootWeightLargePiece),
                "Rounding down would let the team carry weight the bands never charge for.");

            Assert.That(chest.TryPickUp(new Inventory()), Is.False, "§08 says 2인, and a third pair of hands would trivialise it.");
            Assert.That(chest.TryPickUp(first), Is.False, "One player cannot take both handles.");

            var shop = new Shop(new Wallet());
            Assert.That(shop.SellCarried(chest), Is.EqualTo(GameConstants.LootValueLargePiece),
                "§08: 전리품을 차량에 실으면 → 가치만큼 크레딧.");
            Assert.That(chest.IsSold, Is.True);
            Assert.That(first.HandsFree, Is.True, "Loading it into the van gives both carriers their hands back.");
            Assert.That(second.TotalWeight, Is.Zero);
            Assert.That(shop.SellCarried(chest), Is.Zero, "Selling one chest twice would be an infinite credit loop.");
            Assert.That(chest.TryPickUp(first), Is.False, "A sold piece is in the van, not on the floor.");
            Assert.That(shop.Wallet.Credits, Is.EqualTo(GameConstants.LootValueLargePiece));
        }

        /// <summary>
        /// The frame §08 wrote the row for. One carrier lets go while the pair is
        /// moving: the whole weight lands on the other player immediately, and their
        /// band recomputes on the same call — no easing, nothing a long frame could
        /// skip past, because the slowdown arriving late is the difference between
        /// escaping and not.
        /// </summary>
        [Test]
        public void OneCarrierLetsGo_TheOtherIsSlowedOnTheSameCall()
        {
            var holder = new Inventory();
            Assert.That(holder.TryAdd(LootId.Trinket), Is.True);
            var helper = new Inventory();

            var chest = new SharedLootCarry(LootId.LargePiece);
            Assert.That(chest.TryPickUp(holder), Is.True);
            Assert.That(chest.TryPickUp(helper), Is.True);

            var pairSpeed = chest.PairSpeedMultiplier;
            Assert.That(pairSpeed, Is.EqualTo(1f).Within(MathX.Epsilon),
                "Trinket + half a chest is 4 weight — still inside the free band while both carry it.");
            Assert.That(pairSpeed, Is.EqualTo(System.Math.Min(holder.SpeedMultiplier, helper.SpeedMultiplier)).Within(MathX.Epsilon),
                "A rigid object moves at its slowest carrier's pace.");

            Assert.That(chest.Release(helper), Is.EqualTo(CarryReleaseOutcome.TransferredToSoleCarrier));

            Assert.That(helper.TotalWeight, Is.Zero, "The player who let go is free instantly.");
            Assert.That(holder.TotalWeight,
                Is.EqualTo(GameConstants.LootWeightTrinket + GameConstants.LootWeightLargePiece),
                "The survivor takes the whole piece, not their old half.");
            Assert.That(holder.SpeedMultiplier, Is.LessThan(pairSpeed),
                "§08: letting go has to cost the other player speed on the spot.");
            Assert.That(holder.SpeedMultiplier, Is.EqualTo(GameConstants.WeightMulLight).Within(MathX.Epsilon));
            Assert.That(chest.CarrierCount, Is.EqualTo(1));
            Assert.That(chest.IsCarriedBy(holder), Is.True);
        }

        /// <summary>
        /// The other outcome: the survivor is already too full to take the rest, so
        /// the chest hits the floor and both players walk away light. Better than
        /// leaving one player holding a load the capacity rules forbid.
        /// </summary>
        [Test]
        public void OneCarrierLetsGo_AndAFullSurvivorLosesTheChestToTheFloor()
        {
            var chest = new SharedLootCarry(LootId.LargePiece);
            var share = chest.ShareFor(GameConstants.SharedCarryMaxCarriers);

            var holder = new Inventory();
            Fill(holder, GameConstants.BaseCarryCapacity - share);
            var ownLoad = holder.TotalWeight;
            var helper = new Inventory();

            // The helper takes it first: a player this loaded cannot lift the whole
            // piece alone, which is §08's 2인 운반 doing its real job.
            Assert.That(chest.TryPickUp(helper), Is.True);
            Assert.That(chest.TryPickUp(holder), Is.True);
            Assert.That(holder.FreeCapacity, Is.Zero, "Fixture: the survivor is exactly full while sharing.");

            Assert.That(chest.Release(helper), Is.EqualTo(CarryReleaseOutcome.DroppedTooHeavyForSoleCarrier));

            Assert.That(chest.IsOnFloor, Is.True);
            Assert.That(chest.CarrierCount, Is.Zero);
            Assert.That(holder.HandWeight, Is.Zero);
            Assert.That(holder.HandsFree, Is.True);
            Assert.That(holder.TotalWeight, Is.EqualTo(ownLoad), "Their own loot is untouched — only the shared piece is lost.");

            Assert.That(chest.TryPickUp(new Inventory()), Is.True,
                "§08: it is on the floor, not destroyed. Coming back for it is the next argument.");
        }

        /// <summary>
        /// Both carriers letting go on the same frame, and a stray second release.
        /// Simultaneous transitions have to resolve in a fixed order and must never
        /// refund the weight twice — a double release would hand a player free speed.
        /// </summary>
        [Test]
        public void BothCarriersLettingGoAtOnce_LeavesTheChestWhereItFell()
        {
            var chest = new SharedLootCarry(LootId.LargePiece);
            var first = new Inventory();
            var second = new Inventory();
            Assert.That(chest.TryPickUp(first), Is.True);
            Assert.That(chest.TryPickUp(second), Is.True);

            Assert.That(chest.Release(first), Is.EqualTo(CarryReleaseOutcome.TransferredToSoleCarrier));
            Assert.That(chest.Release(second), Is.EqualTo(CarryReleaseOutcome.LeftOnTheFloor));
            Assert.That(chest.Release(second), Is.EqualTo(CarryReleaseOutcome.NotACarrier),
                "Releasing twice must be a no-op, not a second refund.");
            Assert.That(chest.Release(new Inventory()), Is.EqualTo(CarryReleaseOutcome.NotACarrier));

            Assert.That(first.TotalWeight + second.TotalWeight, Is.Zero);
            Assert.That(chest.WeightPerCarrier, Is.Zero);
            Assert.That(chest.PairSpeedMultiplier, Is.EqualTo(1f).Within(MathX.Epsilon),
                "An uncarried piece must report a neutral multiplier so callers can multiply unconditionally.");

            chest.DropToFloor();
            Assert.That(chest.IsOnFloor, Is.True, "Dropping an already-dropped piece is idempotent — a death and a drop can land together.");
            Assert.Throws<ArgumentNullException>(() => chest.Release(null!));
            Assert.Throws<ArgumentNullException>(() => chest.TryPickUp(null!));
            Assert.Throws<ArgumentException>(
                () => new SharedLootCarry(LootId.Timepiece), "Only what §08 marks 2인 운반 can be shared.");
        }

        /// <summary>
        /// A player already holding a chest cannot also take the objective, and vice
        /// versa. §03: 목표물 "양손을 쓴다", and "전리품 동시 소지 불가" — which is what
        /// forces the team to finish looting before the last descent.
        /// </summary>
        [Test]
        public void HandsAndTheObjective_AreMutuallyExclusive()
        {
            var carrier = new Inventory();
            var chest = new SharedLootCarry(LootId.LargePiece);
            Assert.That(chest.TryPickUp(carrier), Is.True);
            Assert.That(carrier.TryPickUpObjective(), Is.False, "§03: the objective needs both hands.");

            chest.DropToFloor();
            Assert.That(carrier.TryPickUpObjective(), Is.True);
            Assert.That(carrier.TotalWeight, Is.EqualTo(GameConstants.ObjectiveWeight));
            Assert.That(carrier.TryAdd(LootId.Trinket), Is.False, "§03: 전리품 동시 소지 불가.");
            Assert.That(chest.TryPickUp(carrier), Is.False);

            carrier.PutDownObjective();
            Assert.That(carrier.TryAdd(LootId.Trinket), Is.True);
            Assert.That(carrier.TryPickUpObjective(), Is.False, "Loot in the pockets blocks the objective too.");
        }

        // ====================================================================
        // §08 / §04 — 금고 속 문서. The Engineer's own income.
        // ====================================================================

        /// <summary>
        /// §08 puts this loot behind a safe and §04 behind the Engineer, and §04's
        /// "즉석 사용 불가" means it costs real seconds in a dangerous room. Time is
        /// passed in explicitly, so a seeded replay opens it on the same tick.
        /// </summary>
        [Test]
        public void TheSafeNeedsTheEngineerAndTheFullEightSeconds()
        {
            var safe = new LootSafe();

            Assert.That(safe.Tick(GameConstants.FixedStep, RoleId.None), Is.EqualTo(SafeWorkResult.Idle));
            Assert.That(safe.Tick(GameConstants.FixedStep, RoleId.Runner), Is.EqualTo(SafeWorkResult.WrongRole),
                "§08: 금고를 열어야 함 (정비공). Anyone else standing there achieves nothing.");
            Assert.That(safe.Progress01, Is.Zero);

            Assert.That(safe.Tick(0f, RoleId.Engineer), Is.EqualTo(SafeWorkResult.Working));
            Assert.That(safe.Progress01, Is.Zero, "A zero-length frame cannot drill a safe.");
            Assert.That(safe.Tick(-5f, RoleId.Engineer), Is.EqualTo(SafeWorkResult.Working));
            Assert.That(safe.WorkedSeconds, Is.Zero, "Negative time must not un-drill it either.");

            var expectedSteps = (int)(GameConstants.EngineerSafeSeconds / GameConstants.FixedStep);
            var steps = 0;
            while (!safe.IsOpen && steps <= expectedSteps + 4)
            {
                safe.Tick(GameConstants.FixedStep, RoleId.Engineer);
                steps++;
            }

            Assert.That(safe.IsOpen, Is.True, "The safe never opened at the fixed step rate.");
            Assert.That(steps, Is.EqualTo(expectedSteps).Within(2),
                "§04: the Engineer pays EngineerSafeSeconds, no more and no less — the slack is float accumulation only.");
        }

        /// <summary>
        /// A frame spike must not tunnel through the open transition: the banked time
        /// is clamped and <see cref="SafeWorkResult.JustOpened"/> is reported exactly
        /// once, whether the frame was 20 ms or 20 s.
        /// </summary>
        [Test]
        public void AFrameSpikeCannotOverDrillOrDoubleOpenTheSafe()
        {
            var safe = new LootSafe();

            Assert.That(safe.Tick(GameConstants.EngineerSafeSeconds * 100f, RoleId.Engineer),
                Is.EqualTo(SafeWorkResult.JustOpened));
            Assert.That(safe.WorkedSeconds, Is.EqualTo(GameConstants.EngineerSafeSeconds).Within(MathX.Epsilon),
                "Overshoot must be clamped, or a hitch would bank credit toward the next safe.");
            Assert.That(safe.Progress01, Is.EqualTo(1f).Within(MathX.Epsilon));
            Assert.That(safe.Tick(GameConstants.EngineerSafeSeconds, RoleId.Engineer),
                Is.EqualTo(SafeWorkResult.AlreadyOpen), "JustOpened is a transition, so it happens once.");

            var full = new Inventory();
            Fill(full, GameConstants.BaseCarryCapacity);
            Assert.That(safe.TryTakeContents(full), Is.False, "No room, so the document waits in the safe rather than hitting the floor.");
            Assert.That(safe.IsEmptied, Is.False);

            var engineer = new Inventory();
            Assert.That(safe.TryTakeContents(engineer), Is.True);
            Assert.That(engineer.CountOf(LootId.SafeDocument), Is.EqualTo(1));
            Assert.That(safe.TryTakeContents(new Inventory()), Is.False, "One document per safe, not one per visitor.");
        }

        /// <summary>
        /// Interrupting the Engineer costs the time already spent and nothing more.
        /// Nothing in §04 or §08 says a half-drilled safe re-locks, and inventing a
        /// decay rate would invent a tuned number the document never priced.
        /// </summary>
        [Test]
        public void AnInterruptedSafeKeepsItsProgress()
        {
            var safe = new LootSafe();
            var half = GameConstants.EngineerSafeSeconds * 0.5f;

            Assert.That(safe.Tick(half, RoleId.Engineer), Is.EqualTo(SafeWorkResult.Working));
            Assert.That(safe.Tick(GameConstants.EngineerSafeSeconds, RoleId.None), Is.EqualTo(SafeWorkResult.Idle));
            Assert.That(safe.WorkedSeconds, Is.EqualTo(half).Within(MathX.Epsilon));
            Assert.That(safe.Tick(half, RoleId.Engineer), Is.EqualTo(SafeWorkResult.JustOpened));
        }

        // ====================================================================
        // §08 — 사망자의 전리품, and the world probe.
        // ====================================================================

        /// <summary>
        /// §08: a dead player's loot "떨어진다", and "회수하려면 시체가 있는 곳으로
        /// 돌아가야 한다". Reachability is asked of <see cref="IWorldProbe"/>, so a
        /// pile behind a locked door reports as unreachable rather than as a short
        /// trip — §12's whole argument is that sight line and path length diverge.
        /// </summary>
        [Test]
        public void DroppedLoot_IsOnlyWorthRecoveringIfThePathExists()
        {
            var field = new DroppedLootField();
            var sealedOff = new UnreachableProbe();

            Assert.That(field.TryFindNearestRecoverable(sealedOff, Vec3.Zero, out var noIndex, out var noDistance), Is.False,
                "An empty floor is not a recovery hint.");
            Assert.That(noIndex, Is.EqualTo(-1));
            Assert.That(float.IsPositiveInfinity(noDistance), Is.True);

            Assert.That(field.DropFrom(new Inventory(), Vec3.Zero), Is.EqualTo(-1),
                "An empty-handed death leaves no pile, so nobody is sent back for nothing.");

            var dead = new Inventory();
            Assert.That(dead.TryAdd(LootId.Timepiece), Is.True);
            Assert.That(dead.TryAdd(LootId.Trinket), Is.True);
            var lost = dead.LootValue;

            var body = new Vec3(20f, 0f, 0f);
            var farther = new Vec3(40f, 0f, 0f);
            Assert.That(field.DropFrom(dead, body), Is.Zero);
            Assert.That(dead.TotalWeight, Is.Zero, "§08: it drops. The corpse keeps nothing.");
            Assert.That(field.TotalValue, Is.EqualTo(lost));
            Assert.That(field.Drop(farther, new[] { LootId.Trinket }), Is.EqualTo(1));

            Assert.That(field.TryFindNearestRecoverable(sealedOff, Vec3.Zero, out _, out _), Is.False,
                "A body the team cannot walk to is not a risk they can choose to take.");

            var walkable = new StraightLineProbe();
            Assert.That(field.TryFindNearestRecoverable(walkable, Vec3.Zero, out var index, out var distance), Is.True);
            Assert.That(index, Is.Zero, "The nearer pile wins, and ties go to the older one so a replay is stable.");
            Assert.That(distance, Is.EqualTo(body.MagnitudeFlat).Within(MathX.Epsilon));

            var recoverer = new Inventory();
            Assert.That(field.Recover(index, recoverer), Is.Empty);
            Assert.That(recoverer.LootValue, Is.EqualTo(lost));
            Assert.That(field.PileCount, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => field.PositionOf(5));
        }

        /// <summary>
        /// Recovering someone else's haul is still subject to §08's greed rule: what
        /// does not fit stays on the floor, so a full player cannot rescue everything.
        /// </summary>
        [Test]
        public void RecoveringAPile_LeavesBehindWhatWillNotFit()
        {
            var field = new DroppedLootField();
            var pile = new List<LootId>();
            for (var i = 0; i < GameConstants.BaseCarryCapacity + 2; i++)
            {
                pile.Add(LootId.Trinket);
            }

            var index = field.Drop(new Vec3(1f, 0f, 1f), pile);
            var taker = new Inventory();
            var leftOver = field.Recover(index, taker);

            Assert.That(taker.TotalWeight, Is.EqualTo(GameConstants.BaseCarryCapacity));
            Assert.That(leftOver.Count, Is.EqualTo(2), "The overflow stays where it fell.");
            Assert.That(field.PileCount, Is.EqualTo(1));
            Assert.That(field.ValueOf(index), Is.EqualTo(GameConstants.LootValueTrinket * 2));
        }

        // ====================================================================
        // §08 구매 목록 — the list, its drawbacks, and §11's substitutes.
        // ====================================================================

        /// <summary>Every row of §08's 구매 목록 must be for sale, priced, and categorised.</summary>
        [Test]
        public void EverySection08PurchaseIsForSale()
        {
            var expected = new[]
            {
                ShopItemId.Battery, ShopItemId.RepairMaterials, ShopItemId.FirstAidKit,
                ShopItemId.UpgradedFlashlight, ShopItemId.Flare, ShopItemId.Chalk,
                ShopItemId.Rope, ShopItemId.Bag, ShopItemId.Blueprint,
                ShopItemId.PocketWatch, ShopItemId.Detector, ShopItemId.Bait, ShopItemId.Suppressor,
            };

            Assert.That(ShopCatalogue.All, Is.EquivalentTo(expected),
                "§08's table is the whole list — an item with no 대가 does not belong in it.");
            Assert.That(ShopCatalogue.CheapestCost, Is.GreaterThan(0),
                "A free item would break §08's 구매력 0 opening: the team could equip itself before descending.");

            foreach (var id in ShopCatalogue.All)
            {
                var item = ShopCatalogue.Of(id);
                Assert.That(item.Cost, Is.GreaterThan(0), id + " is free.");
                Assert.That(item.Category, Is.Not.EqualTo(ShopCategory.None), id + " has no §08 분류.");
            }

            Assert.That(ShopCatalogue.Exists(ShopItemId.None), Is.False);
            Assert.That(ShopCatalogue.CostOf(ShopItemId.None), Is.Zero);
        }

        /// <summary>
        /// §08 opens the table with "전부 §10 딜레마 원리를 따른다 — 얻는 게 있으면
        /// 대가가 있다", then names each 대가. Each named one is asserted here, because
        /// dropping a drawback is the easiest way to accidentally make an item strictly
        /// good.
        /// </summary>
        [Test]
        public void EveryItemKeepsTheDrawbackSection08GaveIt()
        {
            Assert.That(ShopCatalogue.Of(ShopItemId.UpgradedFlashlight).DrawsTheMonster, Is.True,
                "§08: 반경 2배 · 괴물이 2배 멀리서 본다 — the list's 대표작 cuts both ways.");
            Assert.That(GameConstants.UpgradedFlashlightDetectionMultiplier,
                Is.EqualTo(GameConstants.UpgradedFlashlightRangeMultiplier).Within(MathX.Epsilon),
                "§08 doubles both the light and the risk. If they diverge the item stops being a dilemma.");

            var flare = ShopCatalogue.Of(ShopItemId.Flare);
            Assert.That(flare.IsSingleUse && flare.MakesNoise, Is.True, "§08: 1회용 · 소리를 낸다.");
            Assert.That(ShopCatalogue.Of(ShopItemId.Chalk).DrawsTheMonster, Is.True, "§08: 괴물도 흔적을 따라온다.");
            Assert.That(ShopCatalogue.Of(ShopItemId.Rope).IsOneWay, Is.True, "§08: 편도만.");
            Assert.That(ShopCatalogue.Of(ShopItemId.Bait).IsSingleUse, Is.True, "§08: 1회용 · 비쌈.");
            Assert.That(ShopCatalogue.Of(ShopItemId.Detector).MakesNoise, Is.True, "§08: 작동 시 소리를 낸다.");
            Assert.That(ShopCatalogue.Of(ShopItemId.Suppressor).DisablesListener, Is.True, "§08: 자기도 못 듣게 됨 → 청음사 무효.");
            Assert.That(GameConstants.BagSpeedMultiplier, Is.LessThan(1f), "§08: 가방's 대가 is 이동 속도 −10%.");

            var average = ShopCatalogue.CostOfOneOfEverything / (float)ShopCatalogue.All.Count;
            Assert.That(GameConstants.ShopCostBlueprint, Is.GreaterThan(average), "§08 marks 건물 도면 비쌈.");
            Assert.That(GameConstants.ShopCostBait, Is.GreaterThan(average), "§08 marks 미끼 비쌈.");

            foreach (var id in ShopCatalogue.All)
            {
                var item = ShopCatalogue.Of(id);
                if (item.Category == ShopCategory.Consumable)
                {
                    Assert.That(item.IsSingleUse, Is.True, id + " is a 소모품 that is not consumed.");
                    Assert.That(item.IsCapabilityUpgrade, Is.False,
                        id + " is a 소모품 priced as a 강화 아이템, which breaks §08's growth table.");
                }
            }
        }

        /// <summary>
        /// §08's own summary: "소음기는 아이템이 직업과 충돌하는 사례다 — 청음사가 있는
        /// 팀은 사면 안 된다." So the shop still sells it (the team is allowed to make
        /// the mistake), and owning one switches the Listener off entirely rather than
        /// weakening it. No other role is touched.
        /// </summary>
        [Test]
        public void TheSuppressorSwitchesTheListenerOff_Entirely()
        {
            var shop = new Shop(new Wallet(GameConstants.ShopCostSuppressor));
            var roster = new[] { RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer };

            Assert.That(shop.ListenerDisabled, Is.False);
            Assert.That(shop.IsRoleAbilityDisabled(RoleId.Listener), Is.False);
            Assert.That(shop.ConflictsWithRoster(ShopItemId.Suppressor, roster), Is.True,
                "The shop must be able to warn: §08 says a team with a 청음사 should not buy this.");
            Assert.That(shop.ConflictsWithRoster(ShopItemId.Battery, roster), Is.False);
            Assert.That(shop.ConflictsWithRoster(ShopItemId.Suppressor, null), Is.False);

            Assert.That(shop.TryBuy(ShopItemId.Suppressor), Is.EqualTo(PurchaseOutcome.Purchased),
                "§08 wants the conflict to be purchasable — a blocked purchase is not a dilemma.");

            Assert.That(shop.ListenerDisabled, Is.True);
            Assert.That(shop.IsRoleAbilityDisabled(RoleId.Listener), Is.True,
                "§08: 청음사 무효. Not a range penalty, not a duty cycle — off.");

            foreach (RoleId role in Enum.GetValues(typeof(RoleId)))
            {
                if (role != RoleId.Listener)
                {
                    Assert.That(shop.IsRoleAbilityDisabled(role), Is.False,
                        "The 소음기 must conflict with exactly one role, or §11's roster maths changes: " + role);
                }
            }
        }

        /// <summary>
        /// §11's 돈으로 메우기 column, and its one deliberate hole: "관측자만 대체
        /// 수단이 없다 — 유일하게 살 수 없는 정보." Money must never cover that absence,
        /// or the Observer stops being a role and becomes a convenience.
        /// </summary>
        [Test]
        public void MoneyCoversEveryMissingRoleExceptTheObserver()
        {
            Assert.That(ShopCatalogue.SubstituteFor(RoleId.Listener), Is.EqualTo(ShopItemId.Detector), "§11: 청음사 → 감지기.");
            Assert.That(ShopCatalogue.SubstituteFor(RoleId.Runner), Is.EqualTo(ShopItemId.Bait), "§11: 주자 → 미끼.");
            Assert.That(ShopCatalogue.SubstituteFor(RoleId.Engineer), Is.EqualTo(ShopItemId.Flare), "§11: 정비공 → 조명탄.");
            Assert.That(ShopCatalogue.SubstituteFor(RoleId.Observer), Is.EqualTo(ShopItemId.None),
                "§11: the Observer is the one absence money cannot cover.");
            Assert.That(ShopCatalogue.SubstituteFor(RoleId.None), Is.EqualTo(ShopItemId.None));

            Assert.That(ShopCatalogue.SubstituteFor(RoleId.Flasher), Is.EqualTo(ShopItemId.None),
                "§11 promises 섬광탄 for a missing 섬광수, but §08's 구매 목록 does not sell it. "
                + "Implemented as §08 literally lists — see BALANCE-FINDINGS F-004.");
        }

        /// <summary>
        /// A shop that bought and sold the same object at the same price would be an
        /// infinite credit loop: sell watch, buy watch, repeat. §08 lists 회중시계 in
        /// both tables, so the vendor takes a margin.
        /// </summary>
        [Test]
        public void SellingAWatchAndBuyingItBack_LosesMoney()
        {
            Assert.That(GameConstants.ShopCostPocketWatch, Is.GreaterThan(GameConstants.LootValueTimepiece),
                "§08 lists 회중시계 as both loot and stock. Equal prices would be a money pump.");
            Assert.That(GameConstants.ShopCostPocketWatch,
                Is.EqualTo(GameConstants.LootValueTimepiece * GameConstants.ShopVendorMarkup));

            var shop = new Shop(new Wallet());
            var seller = new Inventory();
            Assert.That(seller.TryAdd(LootId.Timepiece), Is.True);
            Assert.That(shop.SellAll(seller), Is.EqualTo(GameConstants.LootValueTimepiece));
            Assert.That(shop.TryBuy(ShopItemId.PocketWatch), Is.EqualTo(PurchaseOutcome.NotEnoughCredits),
                "One watch sold must not buy one watch back.");
        }

        // ====================================================================
        // §08 성장 곡선 × §16-2 — the proposed price table, recomputed.
        //
        //   1차 잠입 전 | 0 — 맨몸으로 들어간다
        //   1차 귀환 후 | 소모품 몇 개
        //   2~3차 귀환 후 | 강화 아이템 1개
        //   후반        | 필요한 건 다 있는데 시간이 없다
        // ====================================================================

        /// <summary>§08's first row: buying power is zero, so the first descent happens 맨몸으로.</summary>
        [Test]
        public void GrowthCurve_BeforeTheFirstDescent_NothingIsAffordable()
        {
            var shop = new Shop(new Wallet());

            foreach (var id in ShopCatalogue.All)
            {
                Assert.That(shop.TryBuy(id), Is.EqualTo(PurchaseOutcome.NotEnoughCredits),
                    "§08: 1차 잠입 전 구매력 0. " + id + " must be out of reach at match start.");
            }

            Assert.That(shop.Wallet.Credits, Is.Zero);
            Assert.That(shop.Wallet.TotalSpent, Is.Zero);
        }

        /// <summary>
        /// §08's second row: "소모품 몇 개". The first haul must cover the team's
        /// restock — §01 has them buying 배터리 + 자재 — and then be spent, leaving
        /// less than the cheapest 강화 아이템. Both halves matter: too little and the
        /// team cannot re-light the basement, too much and §16-2's failure mode
        /// ("2차 잠입에 다 갖춰진다") starts one descent early.
        /// </summary>
        [Test]
        public void GrowthCurve_AfterTheFirstReturn_BuysConsumablesAndNothingMore()
        {
            var shop = new Shop(new Wallet());
            var haul = Descend(shop, GameConstants.WeightFreeMax, false, false);

            Assert.That(shop.Wallet.Credits, Is.EqualTo(haul));
            Assert.That(haul, Is.GreaterThanOrEqualTo(RestockCost),
                "§01 has the team buying 배터리 + 자재 after 1차. If the first haul cannot cover the restock, "
                + "§03's battery pressure ends the match instead of shaping it.");

            Assert.That(Restock(shop), Is.True);

            Assert.That(shop.Wallet.Credits, Is.LessThan(ShopCatalogue.CheapestUpgradeCost),
                "§08: after 1차 the team has 소모품 몇 개 and no 강화 아이템.");
            Assert.That(haul, Is.LessThan(GameConstants.ShopCostUpgradedFlashlight),
                "Even a team that skips the restock must not reach the flagship upgrade after one descent.");
        }

        /// <summary>
        /// §08's third row — and the argument the section quotes verbatim: "강화
        /// 손전등 하나 살까, 배터리 3개 살까?" After the second return the team must be
        /// able to afford the flagship <em>or</em> a restock, never both, and never
        /// two upgrades.
        /// </summary>
        [Test]
        public void GrowthCurve_AfterTheSecondReturn_ForcesSection08sOwnArgument()
        {
            var shop = new Shop(new Wallet());
            Descend(shop, GameConstants.WeightFreeMax, false, false);
            Assert.That(Restock(shop), Is.True);
            Descend(shop, GameConstants.WeightFreeMax, true, false);

            var credits = shop.Wallet.Credits;

            Assert.That(credits, Is.GreaterThanOrEqualTo(GameConstants.ShopCostUpgradedFlashlight),
                "§08 puts the 강화 아이템 at 2~3차 귀환 후, and §01's flow buys the flashlight after 2차.");
            Assert.That(credits, Is.LessThan(GameConstants.ShopCostUpgradedFlashlight + RestockCost),
                "§08's quoted negotiation only exists if the flashlight and the batteries cannot both be bought.");
            Assert.That(credits, Is.LessThan(GameConstants.ShopCostUpgradedFlashlight * 2),
                "§08: 강화 아이템 1개 — one, not a shopping spree.");

            Assert.That(shop.Wallet.TotalEarned, Is.LessThan(LateGameLoadoutCost),
                "§16-2's failure mode: if the whole late-game loadout were affordable by 2차, the curve "
                + "would peak before §07's threat does.");

            Assert.That(shop.TryBuy(ShopItemId.UpgradedFlashlight), Is.EqualTo(PurchaseOutcome.Purchased));
            Assert.That(shop.Wallet.Credits, Is.LessThan(RestockCost),
                "Having bought the light, the team goes back down short of batteries — which is the trade §08 wanted.");
        }

        /// <summary>
        /// §08's last row: "필요한 건 다 있는데 시간이 없다." By the fourth descent the
        /// wallet must stop being the constraint — the flagship, a bag, a rope and
        /// the dearest §11 role substitute all bought, with change left over — and it
        /// must take more than two descents to get there, or growth outruns §07's
        /// threat curve. The loadout and every price come out of
        /// <see cref="GameConstants"/>; nothing here is a hard-coded total.
        /// </summary>
        [Test]
        public void GrowthCurve_ByTheLateGame_CreditsStopBeingTheConstraint()
        {
            var shop = new Shop(new Wallet());
            var descents = 0;

            // 초저녁 — cautious haul, then §01's 배터리 + 자재.
            Descend(shop, GameConstants.WeightFreeMax, false, false);
            descents++;
            Assert.That(Restock(shop), Is.True);

            // 밤 — the 궤짝 of §01, then the flagship instead of a restock.
            Descend(shop, GameConstants.WeightFreeMax, true, false);
            descents++;
            Assert.That(shop.TryBuy(ShopItemId.UpgradedFlashlight), Is.EqualTo(PurchaseOutcome.Purchased));

            // 심야 — a confident haul now the route is known, plus the Engineer's safe.
            Descend(shop, GameConstants.WeightLightMax, false, true);
            descents++;
            Assert.That(Restock(shop), Is.True);
            Assert.That(shop.TryBuy(ShopItemId.Bag), Is.EqualTo(PurchaseOutcome.Purchased));
            Assert.That(shop.TryBuy(ShopItemId.Rope), Is.EqualTo(PurchaseOutcome.Purchased));

            // 새벽 — one more confident haul covers the last gap in the loadout.
            Descend(shop, GameConstants.WeightLightMax, false, false);
            descents++;
            Assert.That(Restock(shop), Is.True);
            var substitute = ShopCatalogue.SubstituteFor(RoleId.Listener);
            Assert.That(shop.TryBuy(substitute), Is.EqualTo(PurchaseOutcome.Purchased),
                "§11: whichever role is missing, the team must be able to cover it by the late game.");

            var hauler = new Inventory();
            Assert.That(shop.TryEquipBag(hauler), Is.True);
            Assert.That(hauler.Capacity, Is.EqualTo(GameConstants.BaseCarryCapacity + GameConstants.BagCapacityBonus));

            Assert.That(shop.StockOf(ShopItemId.UpgradedFlashlight), Is.EqualTo(1));
            Assert.That(shop.StockOf(ShopItemId.Rope), Is.EqualTo(1));
            Assert.That(shop.StockOf(substitute), Is.EqualTo(1));
            Assert.That(shop.Wallet.Credits, Is.GreaterThanOrEqualTo(ShopCatalogue.CheapestCost),
                "§08: 자원은 풍족해지지만 시간이 없어진다 — the wallet is no longer what stops them.");

            Assert.That(descents, Is.LessThanOrEqualTo(GameConstants.ExpectedRoundTripsMax),
                "§03 allows at most 5 round trips, and §08 says the curve completes inside the match.");
            Assert.That(descents, Is.GreaterThan(GameConstants.ExpectedRoundTripsMin),
                "§16-2: too cheap and the team is fully equipped by 2차, which flattens §07's pressure.");
            Assert.That(shop.Wallet.TotalEarned - shop.Wallet.TotalSpent, Is.EqualTo(shop.Wallet.Credits));
        }

        // ====================================================================
        // Findings. §08's own numbers, disagreeing with §08.
        // See docs/BALANCE-FINDINGS.md.
        // ====================================================================

        /// <summary>
        /// F-002. §08 gives 대형 초상화·궤짝 the rule "2인 운반 or 극심한 감속", but its
        /// band table makes weight 5 free — and the piece weighs exactly 5. So
        /// carrying a chest alone with empty pockets costs nothing at all, and the
        /// two-person haul is not faster than the solo one; what sharing actually buys
        /// is capacity, since a half share fits where a whole piece does not.
        /// <para>
        /// Implemented as the document literally states, because the alternative is
        /// inventing an oversize-solo multiplier §08 never priced. This test pins the
        /// consequence so a deliberate retune has to be deliberate.
        /// </para>
        /// </summary>
        [Test]
        public void Finding_SoloCarryingAChest_CostsNothing_ContradictingSection08()
        {
            var solo = new Inventory();
            var soloChest = new SharedLootCarry(LootId.LargePiece);
            Assert.That(soloChest.TryPickUp(solo), Is.True);

            Assert.That(solo.TotalWeight, Is.EqualTo(GameConstants.LootWeightLargePiece));
            Assert.That(solo.SpeedMultiplier, Is.EqualTo(1f).Within(MathX.Epsilon),
                "§08 promises 극심한 감속 for a solo chest, but LootWeightLargePiece == WeightFreeMax, so the "
                + "band table charges nothing. If this now fails, the design was retuned — update F-002.");
            Assert.That(solo.CanSprint, Is.True);

            var pairChest = new SharedLootCarry(LootId.LargePiece);
            Assert.That(pairChest.TryPickUp(new Inventory()), Is.True);
            Assert.That(pairChest.TryPickUp(new Inventory()), Is.True);
            Assert.That(pairChest.PairSpeedMultiplier, Is.EqualTo(solo.SpeedMultiplier).Within(MathX.Epsilon),
                "Sharing the chest is exactly as fast as carrying it alone, so §08's '2인 운반 or 극심한 감속' "
                + "is not the trade the table describes.");

            // What sharing does buy: room. A player already carrying loot can take a
            // half share where a whole piece would not fit.
            var loaded = new Inventory();
            Fill(loaded, GameConstants.BaseCarryCapacity - GameConstants.LootWeightLargePiece + 1);
            var shared = new SharedLootCarry(LootId.LargePiece);
            Assert.That(shared.TryPickUp(loaded), Is.False, "No room for the whole piece.");
            var partner = new Inventory();
            Assert.That(shared.TryPickUp(partner), Is.True);
            Assert.That(shared.TryPickUp(loaded), Is.True, "A half share fits, so 2인 운반's real benefit is capacity.");
        }

        /// <summary>
        /// F-003. §08's fourth band — "16 이상 질주 불가 (주자도)" — cannot be reached.
        /// Capacity is <see cref="GameConstants.BaseCarryCapacity"/> 10 plus
        /// <see cref="GameConstants.BagCapacityBonus"/> 5 = 15, §03 forbids holding
        /// loot and the objective together, and the objective alone weighs 4. So no
        /// legal configuration reaches 16 and the sprint ban never fires.
        /// <para>
        /// The band is not needed to make greed fatal — at maximum load a sprinting
        /// Runner is already slower than the monster — but three quarters of §08's
        /// table describing one reachable outcome is worth a decision. §16-2.
        /// </para>
        /// </summary>
        [Test]
        public void Finding_Section08sFourthWeightBand_IsUnreachable()
        {
            var maxCapacity = CarryLoad.CapacityFor(true);
            Assert.That(maxCapacity, Is.LessThanOrEqualTo(GameConstants.WeightHeavyMax),
                "BaseCarryCapacity + BagCapacityBonus must exceed WeightHeavyMax before the 16+ band can ever apply.");

            var heaviest = new Inventory();
            Assert.That(heaviest.TryEquipBag(), Is.True);
            var chest = new SharedLootCarry(LootId.LargePiece);
            Assert.That(chest.TryPickUp(heaviest), Is.True);
            Fill(heaviest, maxCapacity);

            Assert.That(heaviest.TotalWeight, Is.EqualTo(maxCapacity), "Fixture: this is the heaviest legal load in the game.");
            Assert.That(heaviest.TryAdd(LootId.Trinket), Is.False);
            Assert.That(heaviest.TryPickUpObjective(), Is.False, "§03 blocks the only other source of weight.");
            Assert.That(heaviest.CanSprint, Is.True,
                "§08 says sprinting is impossible from weight 16, but 16 cannot be reached — the rule is dead. "
                + "If this fails, capacity was raised and F-003 is resolved.");
            Assert.That(heaviest.WeightBand, Is.LessThan(3));

            Assert.That(GameConstants.RunnerSprintSpeed * heaviest.SpeedMultiplier,
                Is.LessThan(GameConstants.MonsterBaseSpeed),
                "The ban is redundant anyway: at maximum load the sprint is already slower than the monster.");
        }

        // ====================================================================
        // Fixtures.
        // ====================================================================

        /// <summary>The team's per-descent consumable bill in the §08 reference model: cells for two lights, plus one materials bundle.</summary>
        private static int RestockCost =>
            (GameConstants.EconomyReferenceCellsPerDescent * GameConstants.ShopCostBattery)
            + GameConstants.ShopCostRepairMaterials;

        /// <summary>
        /// What the team needs to own by §08's 후반: the flagship light, a bag, a
        /// rope, and whichever §11 substitute is dearest. Computed so a price change
        /// moves the target with it.
        /// </summary>
        private static int LateGameLoadoutCost
        {
            get
            {
                var dearestSubstitute = 0;
                foreach (var id in ShopCatalogue.All)
                {
                    var item = ShopCatalogue.Of(id);
                    if (item.SubstitutesFor != RoleId.None && item.Cost > dearestSubstitute)
                    {
                        dearestSubstitute = item.Cost;
                    }
                }

                return GameConstants.ShopCostUpgradedFlashlight
                       + GameConstants.ShopCostBag
                       + GameConstants.ShopCostRope
                       + dearestSubstitute;
            }
        }

        private static bool MentionsAnIndividual(string name)
        {
            var lowered = name.ToLowerInvariant();
            return lowered.Contains("player")
                   || lowered.Contains("owner")
                   || lowered.Contains("account")
                   || lowered.Contains("personal");
        }

        /// <summary>Loads an inventory with weight-1 pieces until it reaches the target load.</summary>
        private static void Fill(Inventory inventory, int targetWeight)
        {
            while (inventory.UsedCapacity < targetWeight && inventory.TryAdd(LootId.Trinket))
            {
            }

            Assert.That(inventory.UsedCapacity, Is.EqualTo(targetWeight),
                "Fixture could not build the intended load — capacity or weights changed.");
        }

        /// <summary>
        /// One hauler's load: alternating 은수저 and 회중시계 up to the target weight.
        /// Two haulers started on opposite pieces average exactly §08's mixed
        /// small-loot rate of (10 + 25) / 2 credits per weight, which is the figure
        /// the price derivation uses.
        /// </summary>
        private static Inventory Hauler(int targetWeight, bool startWithTrinket)
        {
            var inventory = new Inventory();
            var trinketNext = startWithTrinket;
            while (inventory.TotalWeight < targetWeight)
            {
                if (!inventory.TryAdd(trinketNext ? LootId.Trinket : LootId.Timepiece))
                {
                    break;
                }

                trinketNext = !trinketNext;
            }

            Assert.That(inventory.TotalWeight, Is.EqualTo(targetWeight), "Fixture: hauler did not reach the intended load.");
            return inventory;
        }

        /// <summary>
        /// One descent, sold at the vehicle. Returns the credits banked.
        /// <para>
        /// Models §01's flow: two of the four players haul while two read clues, so
        /// the chest and the safe document are carried by the clue readers — a hauler
        /// at capacity has no room for either.
        /// </para>
        /// </summary>
        private static int Descend(Shop shop, int haulWeightEach, bool includeChest, bool includeSafeDocument)
        {
            var banked = 0;
            for (var h = 0; h < GameConstants.EconomyReferenceHaulersPerDescent; h++)
            {
                banked += shop.SellAll(Hauler(haulWeightEach, h % 2 == 0));
            }

            if (includeChest)
            {
                var chest = new SharedLootCarry(LootId.LargePiece);
                Assert.That(chest.TryPickUp(new Inventory()), Is.True);
                Assert.That(chest.TryPickUp(new Inventory()), Is.True);
                banked += shop.SellCarried(chest);
            }

            if (includeSafeDocument)
            {
                var safe = new LootSafe();
                Assert.That(safe.Tick(GameConstants.EngineerSafeSeconds, RoleId.Engineer),
                    Is.EqualTo(SafeWorkResult.JustOpened));
                var engineer = new Inventory();
                Assert.That(safe.TryTakeContents(engineer), Is.True);
                banked += shop.SellAll(engineer);
            }

            return banked;
        }

        private static bool Restock(Shop shop)
        {
            return shop.TryBuy(ShopItemId.Battery, GameConstants.EconomyReferenceCellsPerDescent) == PurchaseOutcome.Purchased
                   && shop.TryBuy(ShopItemId.RepairMaterials) == PurchaseOutcome.Purchased;
        }

        /// <summary>A world where nothing can be walked to — a body behind a locked door, or a collapsed stair.</summary>
        private sealed class UnreachableProbe : IWorldProbe
        {
            public bool HasLineOfSight(Vec3 from, Vec3 to) => false;

            public float NavigableDistance(Vec3 from, Vec3 to) => float.PositiveInfinity;

            public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
            {
                next = from;
                return false;
            }

            public FloorMaterial SampleFloor(Vec3 position) => FloorMaterial.Concrete;

            public int ZoneIdAt(Vec3 position) => -1;

            public Vec3 SnapToNavigable(Vec3 desired) => desired;

            public bool IsAreaLit(Vec3 position) => false;
        }

        /// <summary>A world with no walls: path length equals flat distance.</summary>
        private sealed class StraightLineProbe : IWorldProbe
        {
            public bool HasLineOfSight(Vec3 from, Vec3 to) => true;

            public float NavigableDistance(Vec3 from, Vec3 to) => Vec3.DistanceFlat(from, to);

            public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
            {
                next = to;
                return true;
            }

            public FloorMaterial SampleFloor(Vec3 position) => FloorMaterial.Wood;

            public int ZoneIdAt(Vec3 position) => 0;

            public Vec3 SnapToNavigable(Vec3 desired) => desired;

            public bool IsAreaLit(Vec3 position) => false;
        }
    }
}
