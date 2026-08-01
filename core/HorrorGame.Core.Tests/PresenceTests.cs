using HorrorGame.Core;
using HorrorGame.Core.Presence;
using HorrorGame.Core.Threat;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// 그늘 — the second thing in the building — asserted against the argument it has to
    /// win rather than against its implementation.
    /// <para>
    /// The argument is §10's: 「얻으려면 위험을 만들어야 한다」. §03 makes the flashlight
    /// the lock on all progress and charges for exactly one position of the switch —
    /// light on, 괴물이 잘 본다, 배터리를 쓴다. Light off has always been free, so the
    /// dominant play is to travel unlit and flick the beam on to read, and §03's central
    /// dilemma is a dilemma in one direction only. The 그늘 is the other price.
    /// </para>
    /// <para>
    /// Every test here is really one of three tests, and the second is the one that
    /// decides whether this belongs in the game at all:
    /// </para>
    /// <list type="number">
    /// <item><description><b>The dark costs something.</b> Standing in less light than
    /// §03 needs to read by fills a pool, and a full pool takes the two things §03 makes
    /// the game out of — saying what you saw and being sure of it.</description></item>
    /// <item><description><b>It is not a second monster.</b> §01 keeps its horror with
    /// one 이길 수 없는 적. There is no 그늘 anywhere the monster can see you, so §06's
    /// chase, §06's aggro release and §04's 관측자 all happen in places the 그늘 cannot
    /// reach — asserted here as arithmetic, not as intent.</description></item>
    /// <item><description><b>It scales with the building.</b> §07's patrol table names a
    /// number of zones and therefore shrank to a fifth of the map when the building went
    /// from one storey to five. The 그늘 is a predicate on a place, so it has no number
    /// that could shrink.</description></item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class PresenceTests
    {
        private const float Step = GameConstants.FixedStep;

        /// <summary>Pitch dark, monster nowhere near, underground, alive.</summary>
        private static PresenceTickInput InTheDark(int playerIndex = 0) =>
            new PresenceTickInput(playerIndex, 0f, 1000f, false, true);

        /// <summary>Full readable light on the player, everything else the same.</summary>
        private static PresenceTickInput InTheLight(int playerIndex = 0) =>
            new PresenceTickInput(playerIndex, 1f, 1000f, false, true);

        /// <summary>Steps one player for a wall-clock duration at the fixed step, at a fixed moment of §07's night.</summary>
        private static void Run(PresenceField field, PresenceTickInput input, float seconds, float matchElapsedSeconds)
        {
            var steps = (int)System.Math.Round(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                field.Tick(Step, matchElapsedSeconds, input);
            }
        }

        /// <summary>Seconds of the given situation before the 그늘 takes something, or -1 if it never does inside the cap.</summary>
        private static float SecondsUntilTaken(
            PresenceField field, PresenceTickInput input, float matchElapsedSeconds, float capSeconds = 600f)
        {
            var elapsed = 0f;
            var steps = (int)(capSeconds / Step);
            for (var i = 0; i < steps; i++)
            {
                field.Tick(Step, matchElapsedSeconds, input);
                elapsed += Step;

                if (field.StateOf(input.PlayerIndex).Stage == PresenceStage.Taken)
                {
                    return elapsed;
                }
            }

            return -1f;
        }

        /// <summary>동트기 전 — §07's last row, where the 그늘 is at full strength.</summary>
        private static float BeforeSunrise => GameConstants.ThreatTierSeconds * (GameConstants.ThreatTierCount - 1);

        // ====================================================================
        // 1 · The dark costs something.
        // ====================================================================

        /// <summary>
        /// The headline. §10 wants every gain paid for, and the gain here is the one §03
        /// never charged for: moving with the light off, which hides you from the monster
        /// and saves the battery §08 sells.
        /// </summary>
        [Test]
        public void MovingUnlit_FillsThePool_AndTheDarkTakesSomething()
        {
            var field = new PresenceField(1);

            var taken = SecondsUntilTaken(field, InTheDark(), BeforeSunrise);

            Assert.That(taken, Is.GreaterThan(0f), "the 그늘 never took anything from a player standing in pitch dark");
            Assert.That(taken, Is.EqualTo(GameConstants.PresenceSaturationSeconds).Within(Step * 2f),
                "at 동트기 전 the pool must fill in exactly PresenceSaturationSeconds of unbroken dark");
        }

        /// <summary>
        /// §07 owns the night and the 그늘 only reads its row number. 초저녁 is half
        /// strength, so §01's 맨몸 first descent is about learning the building.
        /// </summary>
        [Test]
        public void TheDarkIsHalfAsBoldAtDusk_AndExactlyFullOnTheLastRow()
        {
            Assert.That(PresenceDensity.BoldnessAt(0f), Is.EqualTo(GameConstants.PresenceBoldnessFloor).Within(1e-5f));
            Assert.That(PresenceDensity.BoldnessAt(BeforeSunrise), Is.EqualTo(1f).Within(1e-5f),
                "§07's last row is 생존 불가 수준 — the 그늘 has to arrive there and not before it");

            var dusk = SecondsUntilTaken(new PresenceField(1), InTheDark(), 0f);
            var last = SecondsUntilTaken(new PresenceField(1), InTheDark(), BeforeSunrise);

            Assert.That(dusk, Is.EqualTo(last / GameConstants.PresenceBoldnessFloor).Within(Step * 4f),
                "the whole of §07's effect on the 그늘 is the boldness multiplier, so the times must be in that ratio");
        }

        /// <summary>
        /// §03's threshold, used three ways. Light enough to read a clue by is light
        /// enough to be safe in, and it is the same number — a player never has to learn
        /// a second brightness.
        /// </summary>
        [Test]
        public void TheSafeBrightness_IsExactlyTheBrightnessAClueNeeds()
        {
            Assert.That(PresenceDensity.SafeLightQuality, Is.EqualTo(GameConstants.ClueMinReadableLightQuality));

            Assert.That(PresenceDensity.DarknessFrom(GameConstants.ClueMinReadableLightQuality), Is.EqualTo(0f));
            Assert.That(PresenceDensity.DarknessFrom(1f), Is.EqualTo(0f));
            Assert.That(PresenceDensity.DarknessFrom(0f), Is.EqualTo(1f));
            Assert.That(PresenceDensity.DarknessFrom(GameConstants.ClueMinReadableLightQuality * 0.5f),
                Is.EqualTo(0.5f).Within(1e-5f), "the §03 term is linear between pitch dark and readable");

            Assert.That(PresenceDensity.DarknessFrom(float.NaN), Is.EqualTo(1f),
                "a host that cannot say how lit a player is must not be able to make them safe");
        }

        /// <summary>
        /// A player standing in readable light accrues nothing at all, at any hour. This
        /// is the "light on" half of §03's switch staying exactly as §03 wrote it.
        /// </summary>
        [Test]
        public void StandingInReadableLight_AccruesNothing_AtEveryHourOfTheNight()
        {
            for (var tier = 0; tier < GameConstants.ThreatTierCount; tier++)
            {
                var field = new PresenceField(1);
                Run(field, InTheLight(), 300f, tier * GameConstants.ThreatTierSeconds);

                Assert.That(field.StateOf(0).Pooling01, Is.EqualTo(0f),
                    "the 그늘 pooled on a lit player at tier " + tier);
                Assert.That(field.StateOf(0).Stage, Is.EqualTo(PresenceStage.Clear));
            }
        }

        /// <summary>
        /// One second of light buys back three seconds of dark, and not more. An instant
        /// reset would price §03's whole dilemma at one <c>BatterySwitchOnCost</c>.
        /// </summary>
        [Test]
        public void OneSecondOfLight_BuysBackThreeSecondsOfDark()
        {
            var field = new PresenceField(1);
            Run(field, InTheDark(), 30f, BeforeSunrise);

            var filled = field.StateOf(0).Pooling01;
            Assert.That(filled, Is.GreaterThan(0.5f));

            Run(field, InTheLight(), 1f, BeforeSunrise);
            var afterOneSecond = field.StateOf(0).Pooling01;

            var clearedPerSecond = (filled - afterOneSecond) * GameConstants.PresenceSaturationSeconds;
            Assert.That(clearedPerSecond, Is.EqualTo(3f).Within(0.05f),
                "PresenceSaturationSeconds / PresenceDispersalSeconds is the exchange rate between dark and light");

            Run(field, InTheLight(), GameConstants.PresenceDispersalSeconds, BeforeSunrise);
            Assert.That(field.StateOf(0).Pooling01, Is.EqualTo(0f),
                "a full pool must clear in PresenceDispersalSeconds of light");
        }

        /// <summary>
        /// §04's 섬광 and §08's 조명탄 are flashes rather than places, and they are worth
        /// exactly what the same number of seconds of lamp is worth. No instantaneous verb
        /// may beat standing in a lit room.
        /// </summary>
        [Test]
        public void AFlash_IsWorthExactlyItsOwnSecondsOfLamp()
        {
            var flashed = new PresenceField(1);
            var stood = new PresenceField(1);

            Run(flashed, InTheDark(), 30f, BeforeSunrise);
            Run(stood, InTheDark(), 30f, BeforeSunrise);

            flashed.StateOf(0).Disperse(2f);
            Run(stood, InTheLight(), 2f, BeforeSunrise);

            Assert.That(flashed.StateOf(0).Pooling01, Is.EqualTo(stood.StateOf(0).Pooling01).Within(1e-4f));
        }

        // ====================================================================
        // 2 · What it takes — §03's two currencies, and nothing else.
        // ====================================================================

        /// <summary>
        /// §03 forbids carrying a clue out of the building so that what you saw has to
        /// travel through a person: "그 자리에서 보고, 기억해서, 말로 전달해야 한다." The
        /// toll is charged in exactly those two things, and in nothing else — there is no
        /// health here, no damage, and no second way to die.
        /// </summary>
        [Test]
        public void TheTollIsVoiceAndCertainty_AndItIsOneSprintLong()
        {
            var field = new PresenceField(1);
            SecondsUntilTaken(field, InTheDark(), BeforeSunrise);

            Assert.That(field.TryTakeToll(out var toll), Is.True, "the taking produced no toll");
            Assert.That(toll.PlayerIndex, Is.EqualTo(0));
            Assert.That(toll.Ordinal, Is.EqualTo(1));

            Assert.That(toll.SilenceSeconds, Is.EqualTo(GameConstants.SprintStaminaSeconds),
                "§06 already fixes 12 s as the longest unbroken bad moment the design asks a player to survive");
            Assert.That(toll.RecallSmear, Is.EqualTo(1f - GameConstants.ClueMisreadFocusedFraction).Within(1e-5f),
                "§03: the 그늘 may take back the benefit of a careful look and no more");

            var state = field.StateOf(0);
            Assert.That(state.MayTransmitVoice, Is.False);
            Assert.That(state.Stage, Is.EqualTo(PresenceStage.Taken));
            Assert.That(state.RecallSmear01, Is.GreaterThan(0f));
        }

        /// <summary>
        /// The voice comes back before the certainty does, so the player says the number
        /// while still unsure of it. §03: "6이었나 9였나…"
        /// </summary>
        [Test]
        public void TheVoiceComesBackBeforeTheCertaintyDoes()
        {
            var field = new PresenceField(1);
            SecondsUntilTaken(field, InTheDark(), BeforeSunrise);

            // Out of the dark the instant it happens, so only the toll is being measured.
            Run(field, InTheLight(), GameConstants.PresenceSilenceSeconds + Step, BeforeSunrise);

            var state = field.StateOf(0);
            Assert.That(state.MayTransmitVoice, Is.True, "the silence must be exactly PresenceSilenceSeconds long");
            Assert.That(state.RecallSmear01, Is.GreaterThan(0f),
                "certainty must still be smeared when the player is finally able to speak");

            Run(field, InTheLight(), GameConstants.PresenceRecallFadeSeconds, BeforeSunrise);
            Assert.That(field.StateOf(0).RecallSmear01, Is.EqualTo(0f));
        }

        /// <summary>
        /// A toll is handed out once. The host may tick and read at different rates, and a
        /// silence applied twice — or never — is worse than one that does not exist.
        /// </summary>
        [Test]
        public void ATollIsHandedOutExactlyOnce()
        {
            var field = new PresenceField(1);
            SecondsUntilTaken(field, InTheDark(), BeforeSunrise);

            Assert.That(field.TryTakeToll(out _), Is.True);
            Assert.That(field.TryTakeToll(out _), Is.False);
        }

        /// <summary>
        /// A frame spike cannot stack two silences into one. §01's "저지는 전부 일시적"
        /// cuts both ways: a toll a hitch can double is not a fixed price.
        /// </summary>
        [Test]
        public void AFrameSpikeCannotDoubleTheSilence()
        {
            var field = new PresenceField(1);
            var input = InTheDark();

            // One step long enough to fill the pool three times over from empty.
            field.Tick(GameConstants.PresenceSaturationSeconds * 3f, BeforeSunrise, input);

            Assert.That(field.StateOf(0).TakenCount, Is.EqualTo(1),
                "one step, however long, may take at most once");
            Assert.That(field.StateOf(0).SilenceRemainingSeconds,
                Is.EqualTo(GameConstants.PresenceSilenceSeconds).Within(1e-4f));

            // And a player already inside a taking is not taken again while it lasts,
            // however fast the pool refills underneath them.
            Run(field, input, GameConstants.PresenceSilenceSeconds * 0.5f, BeforeSunrise);

            Assert.That(field.StateOf(0).TakenCount, Is.EqualTo(1));
            Assert.That(field.StateOf(0).SilenceRemainingSeconds,
                Is.LessThanOrEqualTo(GameConstants.PresenceSilenceSeconds));
        }

        /// <summary>
        /// §01: 저지는 전부 일시적. Half the pool survives a taking, and it survives below
        /// the warning, so the second one is announced from the beginning again rather
        /// than arriving out of a state the player was never shown leaving.
        /// </summary>
        [Test]
        public void WhatSurvivesATaking_SitsBelowTheWarning()
        {
            var field = new PresenceField(1);
            SecondsUntilTaken(field, InTheDark(), BeforeSunrise);

            Assert.That(field.StateOf(0).Pooling01, Is.EqualTo(GameConstants.PresenceResidualPooling).Within(0.02f));
            Assert.That(GameConstants.PresenceResidualPooling, Is.LessThan(GameConstants.PresenceWarnPooling));
        }

        /// <summary>
        /// The warning is a real one: past it there is still long enough left to walk to
        /// the nearest place §12 guarantees exists to switch a light on in.
        /// </summary>
        [Test]
        public void TheWarningLeavesEnoughTimeToWalkToCover()
        {
            var secondsLeftAfterTheWarning =
                (1f - GameConstants.PresenceWarnPooling) * GameConstants.PresenceSaturationSeconds;
            var secondsToWalkToCover = GameConstants.LineOfSightBreakSpacingMax / GameConstants.WalkSpeed;

            Assert.That(secondsLeftAfterTheWarning, Is.GreaterThan(secondsToWalkToCover),
                "§12 puts cover inside 25 m; the warning has to outlast the walk to it or it is not a warning");

            var field = new PresenceField(1);
            Run(field, InTheDark(), 1f, BeforeSunrise);
            Assert.That(field.StateOf(0).Stage, Is.EqualTo(PresenceStage.Gathering));

            Run(field, InTheDark(), GameConstants.PresenceSaturationSeconds * GameConstants.PresenceWarnPooling,
                BeforeSunrise);
            Assert.That(field.StateOf(0).Stage, Is.EqualTo(PresenceStage.Close),
                "the 그늘 has to announce itself before it takes anything");
        }

        // ====================================================================
        // 3 · It is not a second monster. §01 — 이길 수 없는 적 → 공포가 유지된다.
        // ====================================================================

        /// <summary>
        /// <b>The test this entity exists or dies on.</b> Everywhere the monster can see
        /// you, there is no 그늘 at all. A second unkillable thing that also caught you
        /// would not double the fear — it would halve the first, because a player stops
        /// modelling either. So the two are never pressure on the same player at the same
        /// moment, and that is arithmetic rather than intent.
        /// </summary>
        [Test]
        public void ThereIsNo그늘AnywhereTheMonsterCanAlreadySeeYou()
        {
            for (var metres = 0f; metres <= GameConstants.MonsterSightRange; metres += 0.5f)
            {
                var reading = PresenceDensity.Sample(0f, metres, BeforeSunrise, false);
                Assert.That(reading.Density, Is.EqualTo(0f),
                    "the 그늘 reached a player " + metres + " m from the monster, inside §06's own sight range");
            }

            Assert.That(PresenceDensity.MonsterClearRadius, Is.EqualTo(GameConstants.MonsterSightRange));
            Assert.That(PresenceDensity.MonsterClearanceFrom(float.NaN), Is.EqualTo(0f),
                "a host that cannot place the monster must remove the second pressure, not add it");
        }

        /// <summary>
        /// §06's chase is untouched. Aggro releases at 12 m and a chase needs a sight
        /// line, so every metre of every chase the design measures happens inside the
        /// radius the 그늘 is absent from. <c>MonsterChaseTests</c> cannot move.
        /// </summary>
        [Test]
        public void AChaseAndAnAggroRelease_BothHappenWhereThe그늘IsNot()
        {
            Assert.That(GameConstants.PresenceMonsterClearRadius,
                Is.GreaterThan(GameConstants.AggroReleaseDistance));

            var field = new PresenceField(1);
            var beingChased = new PresenceTickInput(0, 0f, GameConstants.AggroReleaseDistance, false, true);

            Run(field, beingChased, GameConstants.PresenceSaturationSeconds * 4f, BeforeSunrise);

            Assert.That(field.StateOf(0).Pooling01, Is.EqualTo(0f));
            Assert.That(field.StateOf(0).TakenCount, Is.EqualTo(0),
                "a 주자 running for an S-corridor in the dark was silenced mid-chase");
        }

        /// <summary>
        /// §04's 관측자 is structurally immune while doing its job. The role stands still
        /// for 3 s within 15 m of the monster with no light — otherwise the exact profile
        /// the 그늘 punishes — and §11 gives it the one weakness that cannot be bought
        /// back, so "discouraged" would not be good enough.
        /// </summary>
        [Test]
        public void TheObserverIsImmuneWhileDoingItsJob()
        {
            Assert.That(GameConstants.PresenceMonsterClearRadius, Is.GreaterThan(GameConstants.ObserverRange));

            var field = new PresenceField(1);
            var observing = new PresenceTickInput(0, 0f, GameConstants.ObserverRange, false, true);

            Run(field, observing, GameConstants.ObserverStillSeconds * 20f, BeforeSunrise);

            Assert.That(field.StateOf(0).Stage, Is.EqualTo(PresenceStage.Clear));
        }

        /// <summary>
        /// §04's 청음사 is the role the 그늘 charges rather than removes. It reads the
        /// monster by ear and its stated cost is its own noise; the 그늘 adds a second
        /// cost to the same preferred play — dark and slow — and takes nothing off the
        /// channel, because it makes no sound in the world and has no direction to hear.
        /// <para>
        /// Asserted as the structural fact underneath that claim: nothing the 그늘
        /// produces is positional. There is no position on any type in this namespace.
        /// </para>
        /// </summary>
        [Test]
        public void The그늘HasNoPositionAnywhereInItsApi_SoItCannotCompeteWithTheListener()
        {
            var types = typeof(PresenceField).Assembly.GetTypes();
            foreach (var type in types)
            {
                if (type.Namespace != "HorrorGame.Core.Presence")
                {
                    continue;
                }

                foreach (var member in type.GetProperties())
                {
                    Assert.That(member.PropertyType, Is.Not.EqualTo(typeof(HorrorGame.Core.Math.Vec3)),
                        type.Name + "." + member.Name + " gives the 그늘 a position. It is a condition, not a "
                        + "pursuer, and a position is the first thing that would let §04's 청음사 be asked to "
                        + "localise it — which is the channel §04 gives that role and nothing else.");
                }

                foreach (var field in type.GetFields())
                {
                    Assert.That(field.FieldType, Is.Not.EqualTo(typeof(HorrorGame.Core.Math.Vec3)),
                        type.Name + "." + field.Name + " gives the 그늘 a position.");
                }
            }
        }

        /// <summary>
        /// The tell, and the reason the 그늘 gives §04 more than it takes. §07 turns the
        /// monster's 정지 into silence and the 청음사 loses it; the dark withdrawing is
        /// the one cue that still says "it is close", and §01 makes it available to all
        /// four — 네 사람은 같은 것을 보고 같은 것을 듣는다.
        /// </summary>
        [Test]
        public void TheDarkWithdrawingIsTheTell_AndOnlyInTheDark()
        {
            var closing = PresenceDensity.Sample(0f, 22f, BeforeSunrise, false);
            Assert.That(closing.ClearedByMonster, Is.True);
            Assert.That(closing.Density, Is.GreaterThan(0f).And.LessThan(1f),
                "the withdrawal has to be gradual to be readable");

            var farAway = PresenceDensity.Sample(0f, 40f, BeforeSunrise, false);
            Assert.That(farAway.ClearedByMonster, Is.False);
            Assert.That(farAway.Density, Is.EqualTo(1f).Within(1e-5f));

            var litAndClose = PresenceDensity.Sample(1f, 22f, BeforeSunrise, false);
            Assert.That(litAndClose.ClearedByMonster, Is.False,
                "a player standing in their own beam sees no withdrawal, because there was nothing to withdraw — "
                + "the tell has to be paid for by being in the dark to read it");
        }

        /// <summary>The withdrawal is monotone in distance, so it can be read as an approach rather than as noise.</summary>
        [Test]
        public void TheWithdrawalIsMonotoneInDistance()
        {
            var previous = -1f;
            for (var metres = GameConstants.PresenceMonsterClearRadius;
                 metres <= GameConstants.PresenceMonsterFringeRadius;
                 metres += 0.25f)
            {
                var clearance = PresenceDensity.MonsterClearanceFrom(metres);
                Assert.That(clearance, Is.GreaterThanOrEqualTo(previous));
                previous = clearance;
            }

            Assert.That(PresenceDensity.MonsterClearanceFrom(GameConstants.PresenceMonsterFringeRadius),
                Is.EqualTo(1f));
        }

        // ====================================================================
        // 4 · Where it is — and why that does not go stale.
        // ====================================================================

        /// <summary>
        /// The mistake §07's patrol table made, not repeated. §07 names an absolute number
        /// of zones for its first two rows, so when the building went from one storey to
        /// five the monster's 초저녁 patrol became one fifth of it. The 그늘 is a predicate
        /// on a place — unlit, underground, away from the monster — so growing the
        /// building grows it by construction and there is no number here that could
        /// shrink.
        /// </summary>
        [Test]
        public void TheDarkCoversEveryZone_WhereThePatrolTableCoversOne()
        {
            var dusk = ThreatCurve.At(0f);

            for (var zoneCount = GameConstants.ZoneCountMin; zoneCount <= GameConstants.ZoneCountMax; zoneCount++)
            {
                Assert.That(dusk.PatrolZoneCountFor(zoneCount),
                    Is.EqualTo(GameConstants.ThreatPatrolZonesEarlyEvening),
                    "§07's 초저녁 row is an absolute count, so it does not grow with the map — this is the "
                    + "defect being avoided, asserted so the comparison below means something");

                // Every unlit place in every zone, at any hour, with the monster elsewhere.
                for (var zone = 0; zone < zoneCount; zone++)
                {
                    Assert.That(PresenceDensity.IsPresent(0f, 1000f, false), Is.True);
                }
            }

            // And the same predicate is false for exactly three reasons, all of them
            // things a player or the design did on purpose.
            Assert.That(PresenceDensity.IsPresent(1f, 1000f, false), Is.False, "somebody switched a light on");
            Assert.That(PresenceDensity.IsPresent(0f, 5f, false), Is.False, "the monster is here instead");
            Assert.That(PresenceDensity.IsPresent(0f, 1000f, true), Is.False, "§01: 지상 is the 안전 지대");
        }

        /// <summary>
        /// §01 makes the ground around the 출입구 the 안전 지대 and §08 puts the shop, the
        /// sale and the 보급소 on it. Being silenced while standing at the van is not a
        /// dilemma, only a nuisance.
        /// </summary>
        [Test]
        public void TheSurfaceIsSafeFromThisToo()
        {
            var field = new PresenceField(1);
            var atTheVanInTheDark = new PresenceTickInput(0, 0f, 1000f, true, true);

            Run(field, atTheVanInTheDark, GameConstants.PresenceSaturationSeconds * 4f, BeforeSunrise);

            Assert.That(field.StateOf(0).Stage, Is.EqualTo(PresenceStage.Clear));
        }

        /// <summary>
        /// §09's ghost and §02's escapee are out of reach. §09 already takes both halves
        /// of this toll permanently — 말하기 불가능, and a ghost has nothing left to
        /// misremember — so charging them again would be charging for something gone.
        /// </summary>
        [Test]
        public void AGhostAndAnEscapeeAreOutOfItsReach()
        {
            var field = new PresenceField(1);
            var outOfPlay = new PresenceTickInput(0, 0f, 1000f, false, false);

            Run(field, outOfPlay, GameConstants.PresenceSaturationSeconds * 4f, BeforeSunrise);

            Assert.That(field.StateOf(0).Stage, Is.EqualTo(PresenceStage.Clear));
            Assert.That(field.StateOf(0).TakenCount, Is.EqualTo(0));
        }

        /// <summary>
        /// §03's escort, given a reason. A 목표물 carrier uses both hands and cannot hold
        /// a torch, so their light is whatever a teammate puts on them — which is why the
        /// input is light on the <em>player</em> and not light on what they are pointing
        /// at. §03 already says 누군가 비춰줘야 한다 and gives no consequence for failing
        /// to; this is the consequence.
        /// </summary>
        [Test]
        public void ACarrierWithNobodyLightingThem_IsTakenOnTheWalkOut()
        {
            var field = new PresenceField(2);

            var carrier = new PresenceTickInput(0, 0f, 1000f, false, true);
            var escort = new PresenceTickInput(1, 1f, 1000f, false, true);

            // 새벽 — §01 puts the escort here.
            var preDawn = GameConstants.ThreatTierSeconds * 3f;
            for (var i = 0; i < (int)(GameConstants.PresenceSaturationSeconds * 2f / Step); i++)
            {
                field.Tick(Step, preDawn, carrier);
                field.Tick(Step, preDawn, escort);
            }

            Assert.That(field.StateOf(0).TakenCount, Is.GreaterThanOrEqualTo(1),
                "an unlit carrier walked out of the building with nothing happening to them");
            Assert.That(field.StateOf(1).Stage, Is.EqualTo(PresenceStage.Clear),
                "the escort holding the light is fine, which is the whole shape of §03's 2인 1조 호송");
        }

        /// <summary>Each player is charged for their own dark. Four states, four pools, one field.</summary>
        [Test]
        public void EachPlayerCarriesTheirOwnDark()
        {
            var field = new PresenceField(4);

            for (var i = 0; i < (int)(GameConstants.PresenceSaturationSeconds / Step); i++)
            {
                field.Tick(Step, BeforeSunrise, new PresenceTickInput(0, 0f, 1000f, false, true));
                field.Tick(Step, BeforeSunrise, new PresenceTickInput(1, 1f, 1000f, false, true));
                field.Tick(Step, BeforeSunrise, new PresenceTickInput(2, 0f, 5f, false, true));
                field.Tick(Step, BeforeSunrise, new PresenceTickInput(3, 0f, 1000f, true, true));
            }

            Assert.That(field.StateOf(0).Stage, Is.EqualTo(PresenceStage.Taken), "unlit, alone, underground");
            Assert.That(field.StateOf(1).Stage, Is.EqualTo(PresenceStage.Clear), "carrying light");
            Assert.That(field.StateOf(2).Stage, Is.EqualTo(PresenceStage.Clear), "standing next to the monster");
            Assert.That(field.StateOf(3).Stage, Is.EqualTo(PresenceStage.Clear), "on §01's 지상");

            Assert.That(field.CountAtLeast(PresenceStage.Close), Is.EqualTo(1));
        }

        // ====================================================================
        // 5 · The constants, and the relationships they have to keep.
        // ====================================================================

        /// <summary>
        /// Every 그늘 number is bounded by a number that already existed. This asserts the
        /// two that decide whether the entity has a job at all — if the dark filled slower
        /// than a battery empties, never switching the light on would be strictly safer
        /// than switching it on, which is the one-way switch this whole thing exists to
        /// close.
        /// </summary>
        [Test]
        public void TheDarkFillsFasterThanABatteryEmpties_AndSlowerThanTheBuildingIsCrossed()
        {
            Assert.That(GameConstants.PresenceSaturationSeconds, Is.LessThan(GameConstants.BatterySecondsPerCell));

            var threeCoverCrossings = 3f * GameConstants.LineOfSightBreakSpacingMax / GameConstants.WalkSpeed;
            Assert.That(GameConstants.PresenceSaturationSeconds, Is.GreaterThan(threeCoverCrossings));

            Assert.That(GameConstants.PresenceSilenceSeconds, Is.GreaterThan(GameConstants.ClueReadSeconds),
                "a silence a player can read and speak through costs nothing");

            Assert.DoesNotThrow(GameConstants.Validate);
        }

        /// <summary>
        /// §08's 강화 손전등 is "이 목록의 대표작" because brighter cuts both ways, and the
        /// 그늘 gives it a second meaning without a line of new code: twice the radius is
        /// twice the ground the dark cannot pool on, at the price §08 already charges —
        /// 괴물이 2배 멀리서 본다.
        /// </summary>
        [Test]
        public void TheUpgradedFlashlightBuysTwiceTheGroundTheDarkCannotHave()
        {
            var standing = GameConstants.FlashlightRange;
            var upgraded = GameConstants.FlashlightRange * GameConstants.UpgradedFlashlightRangeMultiplier;

            Assert.That(upgraded, Is.EqualTo(standing * 2f));
            Assert.That(GameConstants.UpgradedFlashlightDetectionMultiplier, Is.EqualTo(2f),
                "the price §08 charges for it is unchanged, which is the point — the item got better and no more "
                + "expensive because the world got a new cost, not because the item was buffed");
        }
    }
}
