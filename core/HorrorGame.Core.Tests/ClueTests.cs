using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// §03 — the clue layer: confusion pairs, the narrowing chain, reading, and the
    /// host-only objective.
    /// <para>
    /// §03 is the section with the most load-bearing prose and the fewest numbers, so
    /// these tests are written against its sentences. "혼동쌍 — 의도적으로 심는다" means a
    /// pair must fire under its own condition; "이 게임의 주된 웃음이자 사망 원인" means it
    /// must fire often enough to matter; "단서를 가지고 나올 수 없다" means there must be
    /// no API that hands a clue's content to anyone who did not stand in front of it.
    /// Each of those is checkable, and each of them is a design decision that a
    /// well-meaning refactor would quietly undo.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ClueTests
    {
        // ====================================================================
        // Fixtures.
        // ====================================================================

        /// <summary>
        /// A random source with one fixed draw, so a test can pin the misread coin
        /// instead of hoping for it.
        /// <para>
        /// <see cref="Careless"/> misreads whenever §03's model allows it at all, which
        /// makes "this pair fires only under its condition" a statement about the model
        /// rather than about luck. <see cref="Careful"/> never misreads, which is the
        /// only way a test can learn what a clue actually says — there is deliberately
        /// no API that will tell it.
        /// </para>
        /// </summary>
        private sealed class FixedRandom : IRandomSource
        {
            private readonly float _value;

            public FixedRandom(float value) => _value = value;

            public float NextFloat() => _value;

            public float NextFloat(float min, float max) => min + ((max - min) * _value);

            public int NextInt(int minInclusive, int maxExclusive) =>
                maxExclusive <= minInclusive
                    ? minInclusive
                    : minInclusive + (int)((maxExclusive - minInclusive) * _value);

            public bool Chance(float probability) =>
                probability > 0f && (probability >= 1f || _value < probability);
        }

        private static IRandomSource Careless() => new FixedRandom(0f);

        private static IRandomSource Careful() => new FixedRandom(0.999f);

        /// <summary>A world that answers every query, optionally with nothing reachable.</summary>
        private sealed class FixtureProbe : IWorldProbe
        {
            public bool NothingReachable;

            public bool HasLineOfSight(Vec3 from, Vec3 to) => true;

            public float NavigableDistance(Vec3 from, Vec3 to) =>
                NothingReachable ? float.PositiveInfinity : Vec3.Distance(from, to);

            public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
            {
                next = to;
                return !NothingReachable;
            }

            public FloorMaterial SampleFloor(Vec3 position) => FloorMaterial.Concrete;

            public int ZoneIdAt(Vec3 position) => NothingReachable ? -1 : 0;

            public Vec3 SnapToNavigable(Vec3 desired) => desired;

            public bool IsAreaLit(Vec3 position) => false;
        }

        /// <summary>
        /// Which floor signs carry a distinguishing property, in the order the
        /// properties are handed out.
        /// <para>
        /// Fixture shape, not balance. The signs are chosen so that both members of
        /// both digit pairs — 1↔7 and 6↔9 — can appear as the objective floor's sign.
        /// A building whose signs do not span a pair cannot express that half of §03's
        /// table at all, which is itself pinned by
        /// <see cref="ShallowBasement_CannotExpressTheSixNinePair"/>.
        /// </para>
        /// </summary>
        private static readonly int[] FeaturedFloorSigns = { 1, 7, 6, 9, 2 };

        private static readonly FloorFeature[] Features =
        {
            FloorFeature.Water, FloorFeature.Machinery, FloorFeature.Cold,
            FloorFeature.Rust, FloorFeature.Collapse,
        };

        private static readonly ClueGlyph[] LabelNumbers =
        {
            ClueGlyph.Digit1, ClueGlyph.Digit6, ClueGlyph.Digit7, ClueGlyph.Digit9,
        };

        /// <summary>
        /// A basement of <paramref name="floorCount"/> floors, each with §12's zones
        /// and three candidate sites per zone.
        /// <para>
        /// Every site is signed with marks drawn from §03's pairs, so a misread lands
        /// on a real address rather than on a self-evidently wrong one. That is the
        /// harder case and the one the design wants: a team that walks confidently into
        /// the wrong room learns nothing until it arrives.
        /// </para>
        /// </summary>
        private static SiteCatalog BuildCatalog(int floorCount)
        {
            var zonesPerFloor = GameConstants.ZoneCountMin;
            var sitesPerZone = GameConstants.CandidateSitesPerZone;

            var floors = new List<FloorDescriptor>();
            var sites = new List<CandidateSite>();
            var siteId = 0;

            for (var level = 1; level <= floorCount; level++)
            {
                var slot = Array.IndexOf(FeaturedFloorSigns, level);
                var feature = slot >= 0 && slot < Features.Length ? Features[slot] : FloorFeature.None;

                var floorIndex = -level;
                floors.Add(new FloorDescriptor(floorIndex, ClueGlyphs.FromDigit(level), feature));

                for (var zone = 0; zone < zonesPerFloor; zone++)
                {
                    for (var slotInZone = 0; slotInZone < sitesPerZone; slotInZone++)
                    {
                        var ordinal = (zone * sitesPerZone) + slotInZone;
                        var group = ordinal / LabelNumbers.Length;

                        var label = new SiteLabel(
                            group % 2 == 0 ? ClueGlyph.WingMieum : ClueGlyph.WingIeung,
                            LabelNumbers[ordinal % LabelNumbers.Length],
                            group < 2 ? ClueGlyph.SideLeft : ClueGlyph.SideRight);

                        var zoneId = ((level - 1) * zonesPerFloor) + zone;

                        sites.Add(new CandidateSite(
                            siteId++, zoneId, floorIndex, new Vec3(zoneId, floorIndex, slotInZone), label));
                    }
                }
            }

            return new SiteCatalog(floors, sites);
        }

        /// <summary>Nine floors: deep enough for every §03 pair to appear on a stairwell sign.</summary>
        private static SiteCatalog DeepBasement() => BuildCatalog(9);

        private static ObjectiveResolver Resolve(SiteCatalog catalog, int seed, bool nothingReachable = false)
        {
            var probe = new FixtureProbe { NothingReachable = nothingReachable };
            return new ObjectiveResolver(catalog, probe, Vec3.Zero, new DeterministicRandom(seed));
        }

        private static int ObjectiveSiteOf(ObjectiveResolver resolver, SiteCatalog catalog)
        {
            for (var i = 0; i < catalog.Sites.Count; i++)
            {
                if (resolver.IsObjectiveSite(catalog.Sites[i].SiteId))
                {
                    return catalog.Sites[i].SiteId;
                }
            }

            return -1;
        }

        /// <summary>
        /// Plays the match the way §03 describes it: read every clue carefully, say it
        /// out loud, and keep the mapping that talks about the property you were told
        /// about. That last step is the only way a team can tell a decoy mapping from
        /// the real one, and it uses nothing a player would not have.
        /// </summary>
        private static ClueBelief ReadEverythingCarefully(ObjectiveResolver resolver)
        {
            var rng = Careful();
            var belief = ClueBelief.Empty;
            var mappings = new List<ClueReport>();
            var namedFeature = FloorFeature.None;

            foreach (var marker in resolver.Markers)
            {
                Assert.That(resolver.TryRead(marker.ClueId, ClueObservation.Ideal, rng, out var report), Is.True,
                    "A careful read of clue " + marker.ClueId + " should always be legible.");

                if (report.Layer == ClueLayer.FloorMapping)
                {
                    mappings.Add(report);
                    continue;
                }

                if (report.Layer == ClueLayer.Feature)
                {
                    namedFeature = report.Feature;
                }

                belief = belief.Absorb(report);
            }

            foreach (var mapping in mappings)
            {
                if (mapping.Feature == namedFeature)
                {
                    belief = belief.Absorb(mapping);
                }
            }

            return belief;
        }

        private static GlyphViewing ViewingFor(MisreadCondition condition)
        {
            switch (condition)
            {
                case MisreadCondition.InvertedAngle:
                    return GlyphViewing.Ideal.WithAngle(180f);
                case MisreadCondition.Handwriting:
                    return GlyphViewing.Ideal.AsHandwritten();
                case MisreadCondition.Blur:
                    return GlyphViewing.Ideal.WithBlur(MathX.Lerp(
                        GameConstants.ClueBlurConfusionThreshold, GameConstants.ClueIllegibleBlur, 0.5f));
                case MisreadCondition.Reflection:
                    return GlyphViewing.Ideal.AsReflection();
                default:
                    return GlyphViewing.Ideal;
            }
        }

        // ====================================================================
        // §03 혼동쌍 — the four pairs, and only the four pairs.
        // ====================================================================

        /// <summary>
        /// §03's table, verbatim: 6↔9 뒤집힌 각도 · 1↔7 손글씨체 · ㅁ↔ㅇ 흐릿할 때 ·
        /// 좌↔우 거울·반사면. A fifth pair, or a pair moved to another condition, changes
        /// what players learn to double-check — and §03 lists the combination method as
        /// fixed precisely so that learning is worth something.
        /// </summary>
        [Test]
        public void ConfusionPairs_AreExactlyTheFourInSection03()
        {
            var pairs = ClueGlyphs.Pairs;

            Assert.That(pairs.Count, Is.EqualTo(4), "§03's 혼동쌍 table has four rows.");

            void HasPair(ClueGlyph a, ClueGlyph b, MisreadCondition condition)
            {
                Assert.That(
                    pairs.Any(p => p.Condition == condition && p.Contains(a) && p.Contains(b)),
                    Is.True,
                    "§03 pairs " + ClueGlyphs.Render(a) + " with " + ClueGlyphs.Render(b) + " under " + condition + ".");
            }

            HasPair(ClueGlyph.Digit6, ClueGlyph.Digit9, MisreadCondition.InvertedAngle);
            HasPair(ClueGlyph.Digit1, ClueGlyph.Digit7, MisreadCondition.Handwriting);
            HasPair(ClueGlyph.WingMieum, ClueGlyph.WingIeung, MisreadCondition.Blur);
            HasPair(ClueGlyph.SideLeft, ClueGlyph.SideRight, MisreadCondition.Reflection);
        }

        /// <summary>
        /// Each of the four test viewings must present exactly one of §03's conditions,
        /// or the pair-isolation test below would be proving nothing.
        /// </summary>
        [Test]
        public void EachTestViewing_PresentsExactlyOneCondition()
        {
            foreach (var pair in ClueGlyphs.Pairs)
            {
                Assert.That(MisreadModel.ActiveConditions(ViewingFor(pair.Condition)), Is.EqualTo(pair.Condition));
            }

            Assert.That(MisreadModel.ActiveConditions(GlyphViewing.Ideal), Is.EqualTo(MisreadCondition.None),
                "Ideal viewing must present no condition at all — it is the reference every other case is measured "
                + "against.");
        }

        /// <summary>
        /// The heart of §03's confusion model: a mark collapses into its partner under
        /// its own condition and under no other.
        /// <para>
        /// Run with a random source that takes every misread the model offers, so a
        /// failure means the model offered one — not that the dice were unlucky. If a
        /// blurred 6 could read as a 9, players could not learn which marks to
        /// double-check, and §03's "기억해서 말로 전달" would degrade from a skill into a
        /// tax.
        /// </para>
        /// </summary>
        [Test]
        public void EachPair_CollapsesOnlyUnderItsOwnCondition()
        {
            foreach (var pair in ClueGlyphs.Pairs)
            {
                foreach (var glyph in new[] { pair.A, pair.B })
                {
                    foreach (var other in ClueGlyphs.Pairs)
                    {
                        var viewing = ViewingFor(other.Condition);
                        var perception = MisreadModel.Perceive(glyph, viewing, Careless());

                        var expected = other.Condition == pair.Condition ? pair.Other(glyph) : glyph;

                        Assert.That(perception.Perceived, Is.EqualTo(expected),
                            "§03: " + ClueGlyphs.Render(glyph) + " may only be mistaken for "
                            + ClueGlyphs.Render(pair.Other(glyph)) + " under " + pair.Condition
                            + ", but under " + other.Condition + " it read as "
                            + ClueGlyphs.Render(perception.Perceived) + ".");
                    }
                }
            }
        }

        /// <summary>
        /// A player who did everything right must be able to trust what they saw:
        /// printed, upright, unworn, fully lit, unhurried. Every misread in a match has
        /// to trace back to one of those six terms being worse, or the team has no way
        /// to earn reliability and §03's obstacles stop being obstacles.
        /// </summary>
        [Test]
        public void IdealViewing_NeverMisreadsAnything()
        {
            foreach (var glyph in AllGlyphs())
            {
                var perception = MisreadModel.Perceive(glyph, GlyphViewing.Ideal, Careless());

                Assert.That(perception.MisreadChance, Is.EqualTo(0f),
                    "Ideal viewing must leave no misread probability for " + ClueGlyphs.Render(glyph) + ".");
                Assert.That(perception.Perceived, Is.EqualTo(glyph));
                Assert.That(perception.Misread, Is.False);
            }
        }

        /// <summary>
        /// §03 plants four pairs and leaves the rest of the alphabet alone. The
        /// unpaired digits are the marks a team can report without hedging, and that
        /// asymmetry is what makes the paired ones frightening.
        /// </summary>
        [Test]
        public void MarksOutsideTheTable_ReadTrueUnderEveryCondition()
        {
            var unpaired = AllGlyphs().Where(ClueGlyphs.IsTrustworthy).ToArray();

            Assert.That(unpaired, Is.Not.Empty,
                "If every mark were confusable, reading a clue would be a coin flip.");

            foreach (var glyph in unpaired)
            {
                foreach (var pair in ClueGlyphs.Pairs)
                {
                    var perception = MisreadModel.Perceive(glyph, ViewingFor(pair.Condition), Careless());
                    Assert.That(perception.Perceived, Is.EqualTo(glyph),
                        ClueGlyphs.Render(glyph) + " belongs to no pair in §03, so nothing may turn it into "
                        + "another mark.");
                }
            }
        }

        /// <summary>
        /// §03 says "뒤집힌 각도에서", not "기울어진 각도에서". A tilted sign is still
        /// readable, and only real inversion collapses 6 into 9 — otherwise every
        /// glance from an awkward angle would be a coin flip and players would stop
        /// trusting any number.
        /// </summary>
        [Test]
        public void ATiltedSign_IsNotAnInvertedOne()
        {
            var tilted = GlyphViewing.Ideal.WithAngle(GameConstants.ClueInvertedViewAngle);
            var inverted = GlyphViewing.Ideal.WithAngle(180f);

            Assert.That(MisreadModel.MisreadChance(ClueGlyph.Digit6, tilted), Is.EqualTo(0f),
                "At the inversion threshold the mark is merely tilted.");
            Assert.That(MisreadModel.MisreadChance(ClueGlyph.Digit6, inverted), Is.GreaterThan(0f),
                "Upside down, a 6 is a 9. §03.");

            Assert.That(MisreadModel.Perceive(ClueGlyph.Digit6, inverted, Careless()).Perceived,
                Is.EqualTo(ClueGlyph.Digit9));
        }

        /// <summary>
        /// A reader who tilts their view to match an upside-down sign cancels the
        /// condition. §03 implies this and never says it: the pair fires on the angle
        /// between reader and mark, not on how the sign was hung, so the counter-play
        /// exists for a player who thinks of it.
        /// </summary>
        [Test]
        public void MatchingTheSignsAngle_ClosesTheInvertedPair()
        {
            // 180° of mounting plus 180° of reader equals upright.
            var cancelled = GlyphViewing.Ideal.WithAngle(180f + 180f);

            Assert.That(MisreadModel.MisreadChance(ClueGlyph.Digit6, cancelled), Is.EqualTo(0f));
        }

        /// <summary>
        /// §03: "여기에 읽기 방해 요소를 얹는다." A narrow beam and a hurried look are laid
        /// <em>on top of</em> a pair — they widen one that is already open and can never
        /// open one that is shut. Otherwise darkness alone would make every mark
        /// unreliable, and the flashlight would stop being a tool and become a tax.
        /// </summary>
        [Test]
        public void PoorLightAndHaste_WidenAnOpenPairButNeverOpenAShutOne()
        {
            var careful = GlyphViewing.Ideal.AsHandwritten();
            var dim = careful.WithLight(GameConstants.ClueMinReadableLightQuality);
            var hurried = careful.WithSeconds(GameConstants.ClueReadSeconds);

            Assert.That(MisreadModel.MisreadChance(ClueGlyph.Digit1, dim),
                Is.GreaterThan(MisreadModel.MisreadChance(ClueGlyph.Digit1, careful)),
                "§03's 손전등의 좁은 빛 must make a handwritten 1 harder, not easier.");
            Assert.That(MisreadModel.MisreadChance(ClueGlyph.Digit1, hurried),
                Is.GreaterThan(MisreadModel.MisreadChance(ClueGlyph.Digit1, careful)),
                "§03's 급하게 봐야 하는 상황 must cost something.");

            var darkAndRushed = GlyphViewing.Ideal
                .WithLight(GameConstants.ClueMinReadableLightQuality)
                .WithSeconds(GameConstants.ClueReadSeconds);

            foreach (var glyph in AllGlyphs())
            {
                Assert.That(MisreadModel.Perceive(glyph, darkAndRushed, Careless()).Perceived, Is.EqualTo(glyph),
                    "The worst legible viewing still presents none of §03's four conditions, so "
                    + ClueGlyphs.Render(glyph) + " must survive it intact.");
            }
        }

        /// <summary>
        /// Even at its worst a pair must stay below certainty, and even at its best it
        /// must stay above zero. The first keeps a clue from being a lie; the second is
        /// §03 planting the ambiguity in the mark, where care cannot reach it.
        /// </summary>
        [Test]
        public void AnOpenPair_IsNeitherCertainNorCurable()
        {
            var worst = GlyphViewing.Ideal
                .WithAngle(180f)
                .WithLight(GameConstants.ClueMinReadableLightQuality)
                .WithSeconds(GameConstants.ClueReadSeconds);

            var best = GlyphViewing.Ideal.WithAngle(180f);

            var worstChance = MisreadModel.MisreadChance(ClueGlyph.Digit6, worst);
            var bestChance = MisreadModel.MisreadChance(ClueGlyph.Digit6, best);

            Assert.That(worstChance, Is.EqualTo(GameConstants.ClueMisreadChanceMax).Within(1e-4f),
                "The 6↔9 row carries full weight, so the worst legible look should reach the cap exactly.");
            Assert.That(bestChance, Is.GreaterThan(0f),
                "§03 plants the pair in the mark. A perfect look cannot un-plant it.");
            Assert.That(bestChance, Is.LessThan(worstChance),
                "Care has to be worth something, or the reading obstacles are decoration.");
        }

        /// <summary>
        /// An unreadable look leaves the team with nothing rather than with something
        /// wrong. All three walls come from §03: no light is the objective's lock, a
        /// mark worn away has stopped being a mark, and a glance shorter than
        /// <see cref="GameConstants.ClueReadSeconds"/> never became a read.
        /// </summary>
        [Test]
        public void AnIllegibleLook_ProducesNoMarkRatherThanAWrongOne()
        {
            var cases = new[]
            {
                GlyphViewing.Ideal.WithLight(GameConstants.ClueMinReadableLightQuality * 0.5f),
                GlyphViewing.Ideal.WithBlur(GameConstants.ClueIllegibleBlur),
                GlyphViewing.Ideal.WithSeconds(GameConstants.ClueReadSeconds * 0.5f),
            };

            foreach (var viewing in cases)
            {
                Assert.That(MisreadModel.IsLegible(viewing), Is.False);

                var perception = MisreadModel.Perceive(ClueGlyph.Digit6, viewing, Careless());

                Assert.That(perception.Legible, Is.False);
                Assert.That(perception.Perceived, Is.EqualTo(ClueGlyph.Unreadable));
                Assert.That(perception.Misread, Is.False,
                    "A failed read must not count as a misread — the team knows it got nothing.");
            }
        }

        // ====================================================================
        // §03's narrowing chain — 물이 있는 층 → 지하 3층 → 그 지점.
        // ====================================================================

        /// <summary>
        /// §03's chain must land on exactly one site, every seed. Two survivors would
        /// mean a team that did everything right still has to guess; zero would mean a
        /// match that cannot be won. The test plays it as a team would: read carefully,
        /// keep the mapping that matches the property it was told about, then narrow.
        /// </summary>
        [Test]
        public void TheChain_AlwaysConvergesOnExactlyOneSite()
        {
            var catalog = DeepBasement();

            for (var seed = 1; seed <= 120; seed++)
            {
                var resolver = Resolve(catalog, seed);
                var belief = ReadEverythingCarefully(resolver);
                var result = ClueChain.Narrow(catalog, belief);

                Assert.That(result.Status, Is.EqualTo(NarrowingStatus.Pinned), "Seed " + seed + " failed to converge.");
                Assert.That(result.CandidateCount, Is.EqualTo(1));
                Assert.That(resolver.IsObjectiveSite(result.SiteId), Is.True,
                    "Seed " + seed + " converged on the wrong site.");
                Assert.That(resolver.VerifyChainConverges(), Is.True,
                    "The resolver's own invariant check disagrees with the chain for seed " + seed + ".");
            }
        }

        /// <summary>
        /// Each link has to be necessary, or §03's round trips are theatre. A property
        /// with no mapping resolves no floor; a mapping about the wrong property
        /// resolves no floor; a site sign with no floor is several rooms in the
        /// building, which is exactly why labels repeat across floors.
        /// </summary>
        [Test]
        public void NoSingleLink_PinsTheSiteOnItsOwn()
        {
            var catalog = DeepBasement();
            var resolver = Resolve(catalog, 4242);
            var full = ReadEverythingCarefully(resolver);

            var featureOnly = ClueBelief.Empty.WithFeature(full.Feature);
            var featureResult = ClueChain.Narrow(catalog, featureOnly);
            Assert.That(featureResult.FloorKnown, Is.False,
                "§03's first clue names a property, not a floor. Without the mapping it locates nothing.");
            Assert.That(featureResult.Status, Is.EqualTo(NarrowingStatus.Narrowed));

            var labelOnly = ClueBelief.Empty.WithLabel(full.Label);
            var labelResult = ClueChain.Narrow(catalog, labelOnly);
            Assert.That(labelResult.CandidateCount, Is.GreaterThan(1),
                "Site signs repeat across floors on purpose — otherwise the floor half of the chain would be "
                + "dead weight.");
            Assert.That(labelResult.Status, Is.EqualTo(NarrowingStatus.Narrowed));

            var floorOnly = ClueBelief.Empty.WithFeature(full.Feature)
                .WithMapping(full.MappedFeature, full.MappedFloorNumber);
            var floorResult = ClueChain.Narrow(catalog, floorOnly);
            Assert.That(floorResult.FloorKnown, Is.True);
            Assert.That(floorResult.CandidateCount,
                Is.EqualTo(GameConstants.ZoneCountMin * GameConstants.CandidateSitesPerZone),
                "Knowing the floor leaves §12's whole candidate set on it — three per zone.");
            Assert.That(floorResult.Status, Is.EqualTo(NarrowingStatus.Narrowed));

            Assert.That(ClueChain.Narrow(catalog, ClueBelief.Empty).Status,
                Is.EqualTo(NarrowingStatus.NoInformation));
        }

        /// <summary>
        /// A decoy mapping is a true sentence about the wrong floor. §03 needs it to be
        /// worthless without the first clue, because that is where "운이 나쁘면 5번" comes
        /// from — a team cannot recognise a dead end until it already holds the property.
        /// </summary>
        [Test]
        public void AMappingAboutAnotherProperty_ResolvesNothing()
        {
            var catalog = DeepBasement();

            var confused = ClueBelief.Empty
                .WithFeature(FloorFeature.Water)
                .WithMapping(FloorFeature.Machinery, ClueGlyphs.FromDigit(7));

            Assert.That(ClueChain.TryResolveFloor(catalog, confused, out _, out var impossible), Is.False);
            Assert.That(impossible, Is.False,
                "A decoy is not a contradiction — nothing about it tells the team they are stuck.");
            Assert.That(ClueChain.Narrow(catalog, confused).FloorKnown, Is.False);
        }

        /// <summary>
        /// §03's intended failure: one mark wrong and the team walks confidently into a
        /// real room that is not the room. The only acceptable outcomes are "a different
        /// site" and "no site at all" — never the right one, because a misread that
        /// still works would make the whole confusion table cosmetic.
        /// </summary>
        [Test]
        public void OneMisrememberedMark_NeverStillFindsTheObjective()
        {
            var catalog = DeepBasement();
            var wrongButPlausible = 0;
            var contradictions = 0;

            for (var seed = 1; seed <= 60; seed++)
            {
                var resolver = Resolve(catalog, seed);
                var truth = ReadEverythingCarefully(resolver);

                foreach (var corrupted in Corruptions(truth))
                {
                    var result = ClueChain.Narrow(catalog, corrupted);

                    Assert.That(result.Status, Is.Not.EqualTo(NarrowingStatus.NoInformation));

                    if (result.Status == NarrowingStatus.Pinned)
                    {
                        Assert.That(resolver.IsObjectiveSite(result.SiteId), Is.False,
                            "Seed " + seed + ": a misremembered mark still led to the objective.");
                        wrongButPlausible++;
                    }
                    else
                    {
                        contradictions++;
                    }
                }
            }

            Assert.That(wrongButPlausible, Is.GreaterThan(0),
                "§03 wants misreads that cost a walk to a real wrong room, not just impossible addresses.");
            Assert.That(contradictions, Is.GreaterThan(0),
                "Some misreads must be self-evident — that is the team's only free warning.");
        }

        /// <summary>
        /// Every way a single mark of a correct belief can be misremembered, using
        /// §03's pairs and nothing else.
        /// </summary>
        private static IEnumerable<ClueBelief> Corruptions(ClueBelief truth)
        {
            if (ClueGlyphs.TryGetConfusion(truth.MappedFloorNumber, out var otherFloor, out _))
            {
                yield return truth.WithMapping(truth.MappedFeature, otherFloor);
            }

            if (ClueGlyphs.TryGetConfusion(truth.Label.Wing, out var otherWing, out _))
            {
                yield return truth.WithLabel(new SiteLabel(otherWing, truth.Label.Number, truth.Label.Side));
            }

            if (ClueGlyphs.TryGetConfusion(truth.Label.Number, out var otherNumber, out _))
            {
                yield return truth.WithLabel(new SiteLabel(truth.Label.Wing, otherNumber, truth.Label.Side));
            }

            if (ClueGlyphs.TryGetConfusion(truth.Label.Side, out var otherSide, out _))
            {
                yield return truth.WithLabel(new SiteLabel(truth.Label.Wing, truth.Label.Number, otherSide));
            }
        }

        // ====================================================================
        // §03 랜덤화 — 목표물 위치 · 단서 위치 · 단서 내용 vary; 조합 방식 does not.
        // ====================================================================

        /// <summary>
        /// §03: "몇 번 왕복해야 하는지 매판 다르다. 운이 좋으면 2번, 나쁘면 5번. 이것이
        /// 랜덤성의 주 축이며 '이번 판 운 없네'라는 대화를 만든다." A count that never
        /// varies deletes that conversation, and one outside 2–5 breaks §07's pacing,
        /// which is tuned against it.
        /// </summary>
        [Test]
        public void RoundTripCount_StaysInsideSection03Range_AndActuallyVaries()
        {
            var catalog = DeepBasement();
            var seen = new List<int>();

            for (var seed = 1; seed <= 400; seed++)
            {
                var trips = Resolve(catalog, seed).PlannedRoundTrips;

                Assert.That(trips,
                    Is.InRange(GameConstants.ExpectedRoundTripsMin, GameConstants.ExpectedRoundTripsMax),
                    "Seed " + seed + " planned " + trips + " round trips, outside §03's 2–5.");

                seen.Add(trips);
            }

            var distinct = seen.Distinct().OrderBy(x => x).ToArray();

            Assert.That(distinct.Length, Is.GreaterThanOrEqualTo(3),
                "Only " + string.Join(",", distinct) + " ever occurs. §03 makes this the main axis of the "
                + "match's randomness.");
            Assert.That(distinct, Does.Contain(GameConstants.ExpectedRoundTripsMin),
                "§03's lucky match (2) requires the layout to put two clues within one descent. If this never "
                + "happens, the co-location step is not working.");
            Assert.That(distinct, Does.Contain(GameConstants.ExpectedRoundTripsMax),
                "§03's unlucky match (5) requires decoy mappings to be planted.");
        }

        /// <summary>
        /// §13 needs a seed to replay a match exactly, and this is the layout that
        /// matters most: a balance report saying "we spent nine minutes on the wrong
        /// floor" is worthless if the same seed puts the objective somewhere else.
        /// Compared through the public surface only — positions, plans, and what a
        /// careful read returns — because there is nothing else to compare.
        /// </summary>
        [Test]
        public void TheSameSeed_ReproducesTheLayoutExactly()
        {
            var catalog = DeepBasement();

            foreach (var seed in new[] { 1, 7, 20260730 })
            {
                var a = Resolve(catalog, seed);
                var b = Resolve(catalog, seed);

                Assert.That(b.ClueCount, Is.EqualTo(a.ClueCount));
                Assert.That(b.PlannedRoundTrips, Is.EqualTo(a.PlannedRoundTrips));

                for (var i = 0; i < a.ClueCount; i++)
                {
                    Assert.That(b.Markers[i].ClueId, Is.EqualTo(a.Markers[i].ClueId));
                    Assert.That(b.Markers[i].ZoneId, Is.EqualTo(a.Markers[i].ZoneId));
                    Assert.That(b.Markers[i].FloorIndex, Is.EqualTo(a.Markers[i].FloorIndex));
                    Assert.That(b.Markers[i].Position, Is.EqualTo(a.Markers[i].Position));
                }

                var beliefA = ReadEverythingCarefully(a);
                var beliefB = ReadEverythingCarefully(b);

                Assert.That(beliefB.Feature, Is.EqualTo(beliefA.Feature));
                Assert.That(beliefB.MappedFloorNumber, Is.EqualTo(beliefA.MappedFloorNumber));
                Assert.That(beliefB.Label, Is.EqualTo(beliefA.Label));

                foreach (var site in catalog.Sites)
                {
                    Assert.That(b.IsObjectiveSite(site.SiteId), Is.EqualTo(a.IsObjectiveSite(site.SiteId)));
                }
            }
        }

        /// <summary>
        /// §03: "목표물 위치 — 랜덤. 왕복 횟수가 매판 달라진다." A layout that parks the
        /// objective in the same place would let a team skip the chain entirely, which
        /// is the one thing §03 randomises against ("좌표 · 정답 공략 무력화").
        /// </summary>
        [Test]
        public void DifferentSeeds_MoveTheObjectiveAndTheClues()
        {
            var catalog = DeepBasement();
            var objectives = new List<int>();
            var cluePositions = new List<int>();

            for (var seed = 1; seed <= 200; seed++)
            {
                var resolver = Resolve(catalog, seed);
                objectives.Add(ObjectiveSiteOf(resolver, catalog));
                cluePositions.Add(resolver.Markers[0].ZoneId);
            }

            Assert.That(objectives.All(id => id >= 0), Is.True, "Every match must place the objective somewhere.");
            Assert.That(objectives.Distinct().Count(), Is.GreaterThan(5),
                "The objective barely moves across 200 seeds: " + objectives.Distinct().Count() + " distinct sites.");
            Assert.That(cluePositions.Distinct().Count(), Is.GreaterThan(3),
                "§03 randomises clue positions too, not just the objective.");
        }

        /// <summary>
        /// The objective may only sit on a floor §03's first clue can describe. A floor
        /// with no distinguishing property would leave the chain without a first link,
        /// so the resolver refuses the map instead of building an unwinnable match.
        /// </summary>
        [Test]
        public void ObjectiveOnlyLandsOnAFloorTheFirstClueCanDescribe()
        {
            var catalog = DeepBasement();

            for (var seed = 1; seed <= 80; seed++)
            {
                var resolver = Resolve(catalog, seed);
                var siteId = ObjectiveSiteOf(resolver, catalog);

                Assert.That(catalog.TryGetSite(siteId, out var site), Is.True);
                Assert.That(catalog.TryGetFloor(site.FloorIndex, out var floor), Is.True);
                Assert.That(floor.Feature, Is.Not.EqualTo(FloorFeature.None),
                    "Seed " + seed + " put the objective on a floor §03's first clue could say nothing about.");
            }
        }

        // ====================================================================
        // §03 reading — close, still, and lit for long enough.
        // ====================================================================

        /// <summary>
        /// §03: "오래 비춰야 읽힌다." The read takes
        /// <see cref="GameConstants.ClueReadSeconds"/> of unbroken light inside
        /// <see cref="GameConstants.ClueReadRange"/>, standing still. This is the rule
        /// that puts the objective and the danger on the same switch, so all three gates
        /// have to bite.
        /// </summary>
        [Test]
        public void AReadNeedsClosenessStillnessAndLight()
        {
            var reader = new ClueReader();

            reader.Tick(GameConstants.FixedStep, Lit(distance: GameConstants.ClueReadRange * 2f));
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Interrupted));
            Assert.That(reader.LastInterrupt, Is.EqualTo(ClueReadInterrupt.OutOfRange));

            reader.Cancel();
            reader.Tick(GameConstants.FixedStep, Lit(speed: GameConstants.ClueReadStillSpeedThreshold * 2f));
            Assert.That(reader.LastInterrupt, Is.EqualTo(ClueReadInterrupt.Moved),
                "A narrow beam cannot be held on a mark while walking. §03.");

            reader.Cancel();
            reader.Tick(GameConstants.FixedStep, Lit(light: 0f));
            Assert.That(reader.LastInterrupt, Is.EqualTo(ClueReadInterrupt.LightLost));

            reader.Cancel();
            var elapsed = HoldSteady(reader, GameConstants.ClueReadSeconds);
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Complete),
                "A full " + GameConstants.ClueReadSeconds + " s of light at " + elapsed + " s should finish the read.");
            Assert.That(reader.Progress, Is.EqualTo(1f).Within(1e-4f));
        }

        /// <summary>
        /// §03 hangs the objective on the light — "어둠 = 목표의 잠금장치" — and a lock
        /// that let you keep your progress would not be one. Losing the beam throws the
        /// read away, so a player who cannot afford
        /// <see cref="GameConstants.ClueReadSeconds"/> of continuous light cannot read at
        /// all, and "배터리가 떨어지면 단서를 읽을 수 없다" becomes literally true instead of
        /// merely inconvenient.
        /// </summary>
        [Test]
        public void LosingTheLightMidReadCancelsIt_AndTheProgressIsGone()
        {
            var reader = new ClueReader();

            HoldSteady(reader, GameConstants.ClueReadSeconds * 0.8f);
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Reading));
            Assert.That(reader.Progress, Is.GreaterThan(0.5f));

            reader.Tick(GameConstants.FixedStep, Lit(light: 0f));

            Assert.That(reader.State, Is.EqualTo(ClueReadState.Interrupted));
            Assert.That(reader.LastInterrupt, Is.EqualTo(ClueReadInterrupt.LightLost));
            Assert.That(reader.Progress, Is.EqualTo(0f), "Interrupted, not paused.");

            // Coming back with the light on must not inherit the lost seconds.
            HoldSteady(reader, GameConstants.ClueReadSeconds * 0.8f);
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Reading),
                "Relighting a clue restarts the read; a flicker at the end must not finish it.");
        }

        /// <summary>
        /// A read and its interruption landing in the same tick resolves as an
        /// interruption. At <see cref="GameConstants.FixedStep"/> the host samples the
        /// world 50 times a second, and crediting the tick that lost the light would
        /// hand out a free read every time a flicker and a completion coincided —
        /// exactly the case a dying battery produces most often.
        /// </summary>
        [Test]
        public void ALightLossOnTheCompletingTick_LosesTheRead()
        {
            var reader = new ClueReader();

            HoldSteady(reader, GameConstants.ClueReadSeconds * 0.9f);
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Reading));

            // Enough time in this one tick to finish twice over, and no light.
            reader.Tick(GameConstants.ClueReadSeconds * 2f, Lit(light: 0f));

            Assert.That(reader.State, Is.EqualTo(ClueReadState.Interrupted));
            Assert.That(reader.LastInterrupt, Is.EqualTo(ClueReadInterrupt.LightLost));
        }

        /// <summary>
        /// A zero or negative delta must be inert. The host steps at a fixed rate, but
        /// a paused frame, a re-entrant call or a clamped spike can all produce one, and
        /// a reader that treated it as progress — or as a rewind — would make reading
        /// depend on frame rate.
        /// </summary>
        [Test]
        public void ZeroAndNegativeDeltas_ChangeNothing()
        {
            var reader = new ClueReader();

            reader.Tick(0f, Lit());
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Reading));
            Assert.That(reader.HeldSeconds, Is.EqualTo(0f));

            HoldSteady(reader, GameConstants.ClueReadSeconds * 0.5f);
            var held = reader.HeldSeconds;

            reader.Tick(0f, Lit());
            reader.Tick(-GameConstants.ClueReadSeconds, Lit());

            Assert.That(reader.HeldSeconds, Is.EqualTo(held).Within(1e-4f),
                "A negative delta must not rewind a read.");
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Reading));
        }

        /// <summary>
        /// A frame spike must not step past the completion. §03 has the host act on a
        /// finished read — that is when the glyph is rendered for the reader — so
        /// <see cref="ClueReadState.Complete"/> latches until the host takes it, and a
        /// 100-second hitch cannot leave the state machine somewhere else.
        /// </summary>
        [Test]
        public void AFrameSpike_CompletesTheReadWithoutSkippingIt()
        {
            var reader = new ClueReader();

            reader.Tick(GameConstants.ClueReadSeconds * 40f, Lit());
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Complete));

            reader.Tick(GameConstants.FixedStep, Lit());
            reader.Tick(GameConstants.FixedStep, Lit());
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Complete),
                "The completed read must wait for the host, not decay.");

            // Even walking away holds it: the host consumes it, then cancels.
            reader.Tick(GameConstants.FixedStep, Lit(clueId: -1));
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Complete));

            reader.Cancel();
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Idle));
            Assert.That(reader.ClueId, Is.EqualTo(-1));
        }

        /// <summary>
        /// Looking at a different clue starts from nothing. Two clues in one room would
        /// otherwise let a player bank progress on one and spend it on the other.
        /// </summary>
        [Test]
        public void SwitchingClues_StartsFromNothing()
        {
            var reader = new ClueReader();

            HoldSteady(reader, GameConstants.ClueReadSeconds * 0.9f);
            reader.Tick(GameConstants.FixedStep, Lit(clueId: 1));

            Assert.That(reader.ClueId, Is.EqualTo(1));
            Assert.That(reader.HeldSeconds, Is.EqualTo(GameConstants.FixedStep).Within(1e-4f));
            Assert.That(reader.State, Is.EqualTo(ClueReadState.Reading));
        }

        /// <summary>
        /// The observation a completed read hands back reports the worst light it
        /// survived, not the average. §03's obstacle is the narrow beam, and averaging
        /// would let a long read spent mostly in the dark look like a careful one — so
        /// a wandering beam has to cost accuracy as well as time.
        /// </summary>
        [Test]
        public void TheObservation_ReportsTheWorstLightNotTheAverage()
        {
            var reader = new ClueReader();
            var dim = GameConstants.ClueMinReadableLightQuality;

            reader.Tick(GameConstants.ClueReadSeconds * 0.5f, Lit(light: dim));
            reader.Tick(GameConstants.ClueReadSeconds * 0.6f, Lit(light: 1f));

            Assert.That(reader.State, Is.EqualTo(ClueReadState.Complete));
            Assert.That(reader.Observation.WorstLightQuality, Is.EqualTo(dim).Within(1e-4f));
        }

        private static ClueReadContext Lit(
            int clueId = 0, float distance = 0f, float speed = 0f, float light = 1f)
        {
            return new ClueReadContext
            {
                ClueId = clueId,
                DistanceToClue = distance,
                ReaderSpeed = speed,
                LightQuality = light,
                ViewAngleDegrees = 0f,
                Blur = 0f,
            };
        }

        private static float HoldSteady(ClueReader reader, float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                reader.Tick(GameConstants.FixedStep, Lit());
                elapsed += GameConstants.FixedStep;
            }

            return elapsed;
        }

        // ====================================================================
        // §13 / ARCHITECTURE §4 — the answer never leaves the host.
        // ====================================================================

        /// <summary>
        /// §13: "단서 내용 · 목표물 위치 — 호스트만 보유." The rule is easy to keep in prose
        /// and easy to break in code, so it is checked structurally: nothing public on
        /// the resolver hands back a position, a site or a sign, and the clue table is
        /// not even a nameable type outside this assembly.
        /// <para>
        /// If a future change adds a convenient <c>ObjectivePosition</c> property, this
        /// test fails — which is the point. ARCHITECTURE §4: sending the answer and only
        /// showing it when the player is close is the same as sending the answer.
        /// </para>
        /// </summary>
        [Test]
        public void TheResolver_ExposesNoAccessorThatReturnsTheAnswer()
        {
            var forbidden = new[] { typeof(Vec3), typeof(SiteLabel), typeof(CandidateSite), typeof(FloorDescriptor) };

            var offenders = PublicMembers()
                .Where(m => ReturnTypeOf(m) != null && forbidden.Contains(ReturnTypeOf(m)))
                .Select(m => m.Name)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "These members hand the answer out as plain data: " + string.Join(", ", offenders)
                + ". The objective's position may only leave through TryPlaceObjective's callback.");

            var objectiveMembers = PublicMembers()
                .Where(m => m.Name.Contains("Objective"))
                .Where(m => ReturnTypeOf(m) != null && ReturnTypeOf(m) != typeof(bool))
                .Select(m => m.Name)
                .ToArray();

            Assert.That(objectiveMembers, Is.Empty,
                "Everything about the objective must be a predicate or a push, never a value: "
                + string.Join(", ", objectiveMembers));

            var assembly = typeof(ObjectiveResolver).Assembly;

            foreach (var name in new[] { "HorrorGame.Core.Clues.ClueDef", "HorrorGame.Core.Clues.ClueInscription" })
            {
                var type = assembly.GetType(name, throwOnError: true);
                Assert.That(type.IsPublic, Is.False,
                    name + " must stay internal. A type the Net assembly cannot name is a type a "
                    + "NetworkBehaviour cannot serialise.");
            }
        }

        /// <summary>
        /// §03: "단서를 가지고 나올 수 없다. 그 자리에서 보고, 기억해서, 말로 전달해야 한다."
        /// So clue content is only reachable by presenting an observation — proof that
        /// somebody stood in front of the mark with a light on — and reading the same
        /// clue twice may legitimately disagree, because nothing is stored. A stable
        /// answer would itself be the record §03 forbids.
        /// </summary>
        [Test]
        public void AClue_ProducesNoRecordThatCouldBeCarriedOut()
        {
            var contentProducers = PublicMembers()
                .OfType<MethodInfo>()
                .Where(m => m.ReturnType == typeof(ClueReport)
                            || m.GetParameters().Any(p => p.ParameterType == typeof(ClueReport).MakeByRefType()))
                .ToArray();

            Assert.That(contentProducers, Is.Not.Empty, "TryRead should be the one way content leaves the host.");

            foreach (var producer in contentProducers)
            {
                Assert.That(producer.GetParameters().Any(p => p.ParameterType == typeof(ClueObservation)), Is.True,
                    producer.Name + " returns clue content without requiring an observation, so something could "
                    + "read a clue without a player standing in front of it.");
            }

            var catalog = DeepBasement();
            var resolver = Resolve(catalog, 31337);
            var pin = resolver.Markers.First(
                m => resolver.TryRead(m.ClueId, ClueObservation.Ideal, Careful(), out var r)
                     && r.Layer == ClueLayer.SitePin);

            resolver.TryRead(pin.ClueId, ClueObservation.Ideal, Careful(), out var careful);
            resolver.TryRead(pin.ClueId, ClueObservation.Ideal, Careless(), out var careless);

            Assert.That(careful.Label, Is.Not.EqualTo(SiteLabel.Unreadable));
            Assert.That(careless.Label, Is.Not.EqualTo(SiteLabel.Unreadable));
            Assert.That(careless.Label, Is.Not.EqualTo(careful.Label),
                "Two reads of the same sign must be free to disagree — a clue that always reads the same way is a "
                + "record, and §03 allows no record.");
        }

        /// <summary>
        /// The objective's position leaves the host exactly once, into the world
        /// builder. A second call must not re-emit it: the narrower the channel, the
        /// fewer places a later refactor can widen.
        /// </summary>
        [Test]
        public void TheObjectivePosition_LeavesTheHostExactlyOnce()
        {
            var resolver = Resolve(DeepBasement(), 99);
            var received = new List<Vec3>();

            Assert.That(resolver.ObjectivePlaced, Is.False);
            Assert.That(resolver.TryPlaceObjective(p => received.Add(p)), Is.True);
            Assert.That(resolver.ObjectivePlaced, Is.True);
            Assert.That(received.Count, Is.EqualTo(1));

            Assert.That(resolver.TryPlaceObjective(p => received.Add(p)), Is.False);
            Assert.That(received.Count, Is.EqualTo(1), "The second call must not invoke the callback at all.");

            Assert.That(() => resolver.TryPlaceObjective(null), Throws.TypeOf<ArgumentNullException>());
        }

        /// <summary>
        /// §13's match summary counts misreads, and §03 wants that count to be non-zero
        /// — "이 게임의 주된 웃음이자 사망 원인". The count lives on the host: telling a
        /// reader they misread would delete the mechanic outright.
        /// </summary>
        [Test]
        public void MisreadsAreCountedOnTheHost_AndOnlyHappenWhenTheModelAllowsThem()
        {
            var catalog = DeepBasement();
            var carelessTotal = 0;

            for (var seed = 1; seed <= 40; seed++)
            {
                var careless = Resolve(catalog, seed);
                foreach (var marker in careless.Markers)
                {
                    careless.TryRead(marker.ClueId, ClueObservation.Ideal, Careless(), out _);
                }

                carelessTotal += careless.Misreads;

                var careful = Resolve(catalog, seed);
                foreach (var marker in careful.Markers)
                {
                    careful.TryRead(marker.ClueId, ClueObservation.Ideal, Careful(), out _);
                }

                Assert.That(careful.Misreads, Is.EqualTo(0),
                    "Seed " + seed + ": a reader who never takes the misread must never be given one.");
                Assert.That(careful.CluesRead, Is.EqualTo(careful.ClueCount));
            }

            Assert.That(carelessTotal, Is.GreaterThan(0),
                "Across 40 matches the planted confusion pairs never fired. §03 plants them deliberately, so "
                + "either RollInscription or the pair table has stopped working.");
        }

        /// <summary>
        /// A read that the conditions did not support must not count as a read, and must
        /// not touch the team's beliefs. §03's failed read costs a walk and a battery,
        /// nothing more.
        /// </summary>
        [Test]
        public void AnIllegibleRead_TellsTheTeamNothingAndCountsForNothing()
        {
            var resolver = Resolve(DeepBasement(), 5);
            var rushed = new ClueObservation(GameConstants.ClueReadSeconds * 0.5f, 1f, 0f, 0f);

            Assert.That(resolver.TryRead(resolver.Markers[0].ClueId, rushed, Careless(), out var report), Is.False);
            Assert.That(report.Legible, Is.False);
            Assert.That(resolver.CluesRead, Is.EqualTo(0));
            Assert.That(ClueBelief.Empty.Absorb(report).HasFeature, Is.False);

            Assert.That(resolver.TryRead(-1, ClueObservation.Ideal, Careful(), out var missing), Is.False);
            Assert.That(missing.Layer, Is.EqualTo(ClueLayer.None));
        }

        // ====================================================================
        // Ugly inputs.
        // ====================================================================

        /// <summary>
        /// A probe with no navigation data — a NavMesh that failed to bake, a simulator
        /// with an empty graph — must still produce a winnable layout. A match that
        /// refuses to start is worse than one laid out optimistically, so the fallback
        /// is loud rather than fatal.
        /// </summary>
        [Test]
        public void AProbeWithNothingReachable_StillProducesAWinnableLayout()
        {
            var catalog = DeepBasement();
            var resolver = Resolve(catalog, 11, nothingReachable: true);

            Assert.That(resolver.UsedUnreachableFallback, Is.True);
            Assert.That(resolver.ClueCount, Is.GreaterThanOrEqualTo(GameConstants.CluesRequiredToLocate));
            Assert.That(resolver.VerifyChainConverges(), Is.True);

            var belief = ReadEverythingCarefully(resolver);
            var result = ClueChain.Narrow(catalog, belief);

            Assert.That(result.Status, Is.EqualTo(NarrowingStatus.Pinned));
            Assert.That(resolver.IsObjectiveSite(result.SiteId), Is.True);
        }

        /// <summary>
        /// A map that cannot carry §03's chain must be rejected while it is being
        /// built, not discovered by four players in a basement. Each of these is a rule
        /// the chain depends on.
        /// </summary>
        [Test]
        public void ACatalogThatCannotCarryTheChain_IsRejected()
        {
            var floor = new FloorDescriptor(-1, ClueGlyph.Digit1, FloorFeature.Water);
            var label = new SiteLabel(ClueGlyph.WingMieum, ClueGlyph.Digit1, ClueGlyph.SideLeft);
            var site = new CandidateSite(0, 0, -1, Vec3.Zero, label);

            Assert.That(() => new SiteCatalog(new List<FloorDescriptor>(), new[] { site }),
                Throws.TypeOf<ArgumentException>(), "A building with no floors has nowhere to put the chain.");

            Assert.That(() => new SiteCatalog(new[] { floor }, new List<CandidateSite>()),
                Throws.TypeOf<ArgumentException>(), "§12 requires candidate sites; with none there is nothing to pin.");

            Assert.That(() => new SiteCatalog(
                    new[] { floor, new FloorDescriptor(-2, ClueGlyph.Digit2, FloorFeature.Water) },
                    new[] { site }),
                Throws.TypeOf<ArgumentException>(),
                "Two floors with the same property would make §03's second clue a statement with two answers.");

            Assert.That(() => new SiteCatalog(
                    new[] { floor },
                    new[] { site, new CandidateSite(1, 0, -1, Vec3.Zero, label) }),
                Throws.TypeOf<ArgumentException>(),
                "Two sites signed the same on one floor: §03's third clue could not pin either.");

            Assert.That(() => new SiteCatalog(
                    new[] { floor },
                    new[] { new CandidateSite(0, 0, -1, Vec3.Zero, new SiteLabel(
                        ClueGlyph.Digit2, ClueGlyph.Digit1, ClueGlyph.SideLeft)) }),
                Throws.TypeOf<ArgumentException>(),
                "A sign outside §03's alphabet would be the one address no confusion pair could touch.");

            Assert.That(() => new SiteCatalog(null, new[] { site }), Throws.TypeOf<ArgumentNullException>());
        }

        /// <summary>
        /// §03's first clue names a property of a floor, so a building where no floor
        /// has one cannot host the chain. Refusing it here turns a silently unwinnable
        /// match into a level-authoring error.
        /// </summary>
        [Test]
        public void ABuildingWithNoDistinguishingFloor_CannotHostTheObjective()
        {
            var floors = new[] { new FloorDescriptor(-1, ClueGlyph.Digit1, FloorFeature.None) };
            var sites = new[]
            {
                new CandidateSite(0, 0, -1, Vec3.Zero,
                    new SiteLabel(ClueGlyph.WingMieum, ClueGlyph.Digit1, ClueGlyph.SideLeft)),
            };

            var catalog = new SiteCatalog(floors, sites);

            Assert.That(
                () => new ObjectiveResolver(catalog, new FixtureProbe(), Vec3.Zero, new DeterministicRandom(1)),
                Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// A floor with a single candidate site still has to work. §12 asks for three
        /// per zone, but a prototype map (§14) or a hand-built test map may not have
        /// them yet, and the layout must degrade rather than throw.
        /// </summary>
        [Test]
        public void AMinimalBuilding_StillLaysOutAChain()
        {
            var floors = new[]
            {
                new FloorDescriptor(-1, ClueGlyph.Digit1, FloorFeature.Water),
                new FloorDescriptor(-2, ClueGlyph.Digit2, FloorFeature.Machinery),
            };

            var sites = new[]
            {
                new CandidateSite(0, 0, -1, Vec3.Zero,
                    new SiteLabel(ClueGlyph.WingMieum, ClueGlyph.Digit1, ClueGlyph.SideLeft)),
                new CandidateSite(1, 1, -2, Vec3.Forward,
                    new SiteLabel(ClueGlyph.WingMieum, ClueGlyph.Digit1, ClueGlyph.SideLeft)),
            };

            var catalog = new SiteCatalog(floors, sites);

            for (var seed = 1; seed <= 20; seed++)
            {
                var resolver = new ObjectiveResolver(
                    catalog, new FixtureProbe(), Vec3.Zero, new DeterministicRandom(seed));

                Assert.That(resolver.ClueCount, Is.GreaterThanOrEqualTo(GameConstants.CluesRequiredToLocate));
                Assert.That(resolver.VerifyChainConverges(), Is.True, "Seed " + seed + " built an unwinnable map.");
                Assert.That(resolver.PlannedRoundTrips,
                    Is.InRange(GameConstants.ExpectedRoundTripsMin, GameConstants.ExpectedRoundTripsMax));
            }
        }

        // ====================================================================
        // Findings — consequences the design document does not acknowledge.
        // See docs/BALANCE-FINDINGS.md.
        // ====================================================================

        /// <summary>
        /// §03 fixes the map so that skill can grow ("맵 구조 · 단서 조합 방식 — 고정.
        /// 학습 가능해야 실력이 성장한다") and then builds its middle clue out of fixed map
        /// structure: "물이 있는 층은 지하 3층이다" is true of the building forever. A team
        /// that has played the map once supplies that link from memory, so §03's
        /// three-clue chain becomes a two-clue chain on the second night and
        /// <see cref="GameConstants.CluesRequiredToLocate"/> stops describing the game
        /// being played.
        /// <para>
        /// Implemented as the document says, and pinned here: <see cref="ClueBelief"/>
        /// does not care whether a mapping came from a clue or from memory, because a
        /// player's memory does not either. If the design later randomises the
        /// property-to-floor assignment per match, this test fails and the decision
        /// gets made on purpose.
        /// </para>
        /// </summary>
        [Test]
        public void AVeteranTeam_NeedsOnlyTwoOfTheThreeClues()
        {
            var catalog = DeepBasement();

            for (var seed = 1; seed <= 40; seed++)
            {
                var resolver = Resolve(catalog, seed);
                var truth = ReadEverythingCarefully(resolver);

                // Map knowledge: the property-to-floor mapping is part of the fixed
                // building, so a returning player can fill it in without the clue.
                Assert.That(catalog.TryGetFloorOfFeature(truth.Feature, out var remembered), Is.True);

                var fromMemory = ClueBelief.Empty
                    .WithFeature(truth.Feature)
                    .WithMapping(truth.Feature, remembered.NumberGlyph)
                    .WithLabel(truth.Label);

                var result = ClueChain.Narrow(catalog, fromMemory);

                Assert.That(result.Status, Is.EqualTo(NarrowingStatus.Pinned));
                Assert.That(resolver.IsObjectiveSite(result.SiteId), Is.True,
                    "Seed " + seed + ": §03's middle clue is a statement about fixed map structure, so a team "
                    + "that knows the building skips it. See docs/BALANCE-FINDINGS.md.");
            }
        }

        /// <summary>
        /// §03's worked example puts the objective on 지하 3층, which implies a basement
        /// of about three floors. In such a building no stairwell sign reads 6 or 9, so
        /// the 6↔9 row of the confusion table can never fire on a floor number — and a
        /// 1↔7 misread names 지하 7층, a floor that does not exist, which the team
        /// detects for free the moment they reach the stairs.
        /// <para>
        /// The pairs only bite when both members exist as real addresses. §03 does not
        /// say this, and it is the difference between a misread that costs a match and
        /// one that costs nothing.
        /// </para>
        /// </summary>
        [Test]
        public void ShallowBasement_CannotExpressTheSixNinePair()
        {
            var shallow = BuildCatalog(3);

            Assert.That(shallow.TryGetFloorByNumber(ClueGlyph.Digit6, out _), Is.False);
            Assert.That(shallow.TryGetFloorByNumber(ClueGlyph.Digit9, out _), Is.False);

            var free = 0;
            var costly = 0;

            for (var seed = 1; seed <= 40; seed++)
            {
                var resolver = Resolve(shallow, seed);
                var truth = ReadEverythingCarefully(resolver);

                if (!ClueGlyphs.TryGetConfusion(truth.MappedFloorNumber, out var wrong, out _))
                {
                    // 지하 2층 is signed 2, which §03 pairs with nothing: that floor's
                    // mapping clue is immune, which is its own kind of luck.
                    continue;
                }

                var result = ClueChain.Narrow(shallow, truth.WithMapping(truth.MappedFeature, wrong));

                if (result.Status == NarrowingStatus.Contradiction)
                {
                    free++;
                }
                else
                {
                    costly++;
                }
            }

            Assert.That(free, Is.GreaterThan(0));
            Assert.That(costly, Is.EqualTo(0),
                "In a three-floor basement every floor-number misread names a floor that does not exist, so it "
                + "costs the team nothing. §03's confusion pairs need both members to be real addresses. See "
                + "docs/BALANCE-FINDINGS.md.");

            // The deep fixture is the opposite case, and the one §03 actually wants.
            var deep = DeepBasement();
            Assert.That(deep.TryGetFloorByNumber(ClueGlyph.Digit9, out _), Is.True);
        }

        /// <summary>
        /// All four of §03's pairs are pairs of <em>marks</em>, so the one clue written
        /// in words — "물이 있는 층" — is the one clue that cannot be misread. The chain's
        /// first link is therefore immune while the other two are not, which is not what
        /// a reader of §03's table would expect.
        /// </summary>
        [Test]
        public void TheFirstClue_CannotBeMisreadAtAll()
        {
            var catalog = DeepBasement();

            for (var seed = 1; seed <= 40; seed++)
            {
                var careless = Resolve(catalog, seed);
                var careful = Resolve(catalog, seed);

                foreach (var marker in careless.Markers)
                {
                    careless.TryRead(marker.ClueId, ClueObservation.Ideal, Careless(), out var sloppy);
                    careful.TryRead(marker.ClueId, ClueObservation.Ideal, Careful(), out var exact);

                    if (exact.Layer != ClueLayer.Feature)
                    {
                        continue;
                    }

                    Assert.That(sloppy.Feature, Is.EqualTo(exact.Feature),
                        "§03's four confusion pairs are all glyph-level, so a clue written as a word has no "
                        + "misread channel. See docs/BALANCE-FINDINGS.md.");
                    Assert.That(sloppy.Feature, Is.Not.EqualTo(FloorFeature.None));
                }
            }
        }

        /// <summary>
        /// §03 asks for 2–5 round trips but names no mechanism, so the layout builds
        /// one: co-locating two clues for a lucky match, planting true-but-irrelevant
        /// mappings for an unlucky one. Only the first is binding. A team that walks
        /// past the decoys beats the plan, so the observed distribution will sit below
        /// the planned one — which matters because §07's threat curve is tuned against
        /// the round-trip rhythm, not against the plan.
        /// </summary>
        [Test]
        public void DecoyMappings_OnlyCostRoundTripsIfATeamReadsThem()
        {
            var catalog = DeepBasement();
            var unlucky = Enumerable.Range(1, 400)
                .Select(seed => Resolve(catalog, seed))
                .First(r => r.PlannedRoundTrips > GameConstants.CluesRequiredToLocate);

            Assert.That(unlucky.ClueCount, Is.GreaterThan(GameConstants.CluesRequiredToLocate),
                "An unlucky match should carry decoy mappings.");

            // A team that only reads the three clues it needs still converges — the
            // decoys cost nothing to a team that never picks them up.
            var rng = Careful();
            var belief = ClueBelief.Empty;
            var namedFeature = FloorFeature.None;

            foreach (var marker in unlucky.Markers)
            {
                unlucky.TryRead(marker.ClueId, ClueObservation.Ideal, rng, out var report);

                if (report.Layer == ClueLayer.Feature)
                {
                    namedFeature = report.Feature;
                    belief = belief.Absorb(report);
                }
                else if (report.Layer == ClueLayer.SitePin)
                {
                    belief = belief.Absorb(report);
                }
            }

            foreach (var marker in unlucky.Markers)
            {
                unlucky.TryRead(marker.ClueId, ClueObservation.Ideal, rng, out var report);

                if (report.Layer == ClueLayer.FloorMapping && report.Feature == namedFeature)
                {
                    belief = belief.Absorb(report);
                    break;
                }
            }

            var result = ClueChain.Narrow(catalog, belief);

            Assert.That(result.Status, Is.EqualTo(NarrowingStatus.Pinned));
            Assert.That(unlucky.IsObjectiveSite(result.SiteId), Is.True,
                "The planned round-trip count is an upper bound on how much the decoys cost, not a floor. See "
                + "docs/BALANCE-FINDINGS.md.");
        }

        // ====================================================================
        // Helpers.
        // ====================================================================

        private static IEnumerable<ClueGlyph> AllGlyphs() =>
            Enum.GetValues(typeof(ClueGlyph)).Cast<ClueGlyph>().Where(g => g != ClueGlyph.Unreadable);

        private static IEnumerable<MemberInfo> PublicMembers() =>
            typeof(ObjectiveResolver).GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        private static Type? ReturnTypeOf(MemberInfo member)
        {
            switch (member)
            {
                case PropertyInfo property:
                    return property.PropertyType;
                case MethodInfo method:
                    return method.ReturnType;
                case FieldInfo field:
                    return field.FieldType;
                default:
                    return null;
            }
        }
    }
}
