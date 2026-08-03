using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HorrorGame.Core;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// The headstone for §03's 단서 사슬 and §08's 경제, kept executable so it cannot
    /// rot into a comment nobody reads.
    /// <para>
    /// DESCENT-PIVOT §3 「버린다」 drops both outright, and §7 step 7
    /// 「상점/전리품/단서 제거」 is the step that finally ran. The reasons, in the
    /// document's own words:
    /// </para>
    /// <list type="bullet">
    /// <item><b>단서 (§03의 층 → 구역 → 지점)</b> — 「목적지가 처음부터 알려져 있다」.
    /// A race announces where the finish is at the start: the middle of B8. A chain
    /// that narrows a search cannot exist when there is no search, so
    /// <c>HorrorGame.Core.Clues</c> went whole — glyphs, misreads, the reader, the
    /// objective resolver, and the <c>SiteCatalog</c> whose entire constructor was a
    /// proof that the chain could converge.</item>
    /// <item><b>경제 (§08의 전리품 · 크레딧 · 판매)</b> — 「통화가 없다」. There is no
    /// resupply in a race and no second descent to spend between, so the wallet, the
    /// shop, the loot catalogue, the safe, the shared carry and the weight-to-speed
    /// bands went with it.</item>
    /// </list>
    /// <para>
    /// This fixture exists because both systems were already <em>gated out</em> of
    /// the race path once and leaked back anyway — a Shop lazily constructed on the
    /// first race tick, loot state synced every fixed step. Gating is reversible by
    /// accident; a failing test is not. If you are here because this fixture is
    /// red, the question to answer first is not "how do I make it pass" but "what
    /// currency did I just add to a game that has none".
    /// </para>
    /// </summary>
    [TestFixture]
    public class PivotDeletionTests
    {
        /// <summary>
        /// The namespaces the pivot removed. Matched as namespace prefixes, so a
        /// <c>HorrorGame.Core.Economy.Something</c> smuggled back in under any name
        /// is caught without this list having to enumerate types.
        /// </summary>
        private static readonly string[] DeletedNamespaces =
        {
            "HorrorGame.Core.Clues",
            "HorrorGame.Core.Economy",
        };

        /// <summary>
        /// The directories those namespaces lived in, relative to Scripts/Core.
        /// Checked separately from the namespaces because a file can be restored
        /// to disk with its namespace changed, and the intent would still be the
        /// system coming back.
        /// </summary>
        private static readonly string[] DeletedDirectories = { "Clues", "Economy" };

        /// <summary>
        /// No type in the compiled rules assembly may live in a deleted namespace.
        /// <para>
        /// This is the check that actually binds, because it reads the artefact —
        /// the assembly Unity and the simulator both load — rather than the source
        /// tree. A type reintroduced in a different folder still fails here.
        /// </para>
        /// </summary>
        [Test]
        public void CoreAssembly_HasNoClueOrEconomyTypes()
        {
            var assembly = typeof(GameConstants).Assembly;

            var offenders = assembly.GetTypes()
                .Where(t => t.Namespace != null
                            && DeletedNamespaces.Any(ns =>
                                t.Namespace.Equals(ns, StringComparison.Ordinal)
                                || t.Namespace.StartsWith(ns + ".", StringComparison.Ordinal)))
                .Select(t => t.FullName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "DESCENT-PIVOT §3 deleted the clue chain and the economy, and these types put them "
                + "back into the rules assembly: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// The source directories must be gone too, not merely empty of types.
        /// A folder that still exists is an invitation to refill it.
        /// </summary>
        [Test]
        public void CoreSources_HaveNoClueOrEconomyDirectories()
        {
            var root = CoreSourceRootAttribute.Resolve();

            var present = DeletedDirectories
                .Where(d => Directory.Exists(Path.Combine(root.FullName, d)))
                .ToArray();

            Assert.That(present, Is.Empty,
                "These directories under Scripts/Core were deleted by the pivot and have come back: "
                + string.Join(", ", present)
                + ". The race has no currency and no search to narrow — see DESCENT-PIVOT §3.");
        }

        /// <summary>
        /// No core source may declare a deleted namespace, whatever folder it sits
        /// in. Catches the reintroduction that moves the file rather than the type.
        /// </summary>
        [Test]
        public void CoreSources_DeclareNoClueOrEconomyNamespace()
        {
            var offenders = CoreSourceRootAttribute.EnumerateCoreSources()
                .Select(f => (f.Name, Code: CSharpSource.StripCommentsAndLiterals(File.ReadAllText(f.FullName))))
                .Where(x => DeletedNamespaces.Any(ns =>
                    x.Code.Contains("namespace " + ns, StringComparison.Ordinal)))
                .Select(x => x.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "These core files declare a namespace the pivot deleted: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// Nothing in the rules assembly may expose a member typed on the deleted
        /// systems' vocabulary.
        /// <para>
        /// The named types are the ones that had call sites outside their own
        /// folder, so they are the ones a partial revert would drag back in first:
        /// <c>ShopItemId</c> reached §13 telemetry, <c>SiteCatalog</c> and
        /// <c>SiteLabel</c> reached the map reader, <c>LootCatalogue</c> reached the
        /// map generator's 막힌 길 rewards. A method signature mentioning any of them
        /// means a consumer was reconnected rather than deleted.
        /// </para>
        /// </summary>
        [Test]
        public void CoreSources_NameNoDeletedType()
        {
            string[] deletedTypes =
            {
                "ClueChain", "ClueReader", "ClueReport", "ClueGlyph", "ClueGlyphs", "ClueBelief",
                "MisreadModel", "GlyphViewing", "ObjectiveResolver",
                "SiteCatalog", "SiteLabel", "FloorDescriptor", "FloorFeature",
                "Wallet", "Shop", "ShopItemId", "ShopCatalogue", "ShopItemDefinition",
                "LootId", "LootCatalogue", "LootDefinition", "LootSafe",
                "Inventory", "CarryLoad", "SharedLootCarry", "DroppedLootField",
            };

            var offenders = CoreSourceRootAttribute.EnumerateCoreSources()
                .Select(f => (f.Name, Code: CSharpSource.StripCommentsAndLiterals(File.ReadAllText(f.FullName))))
                .SelectMany(x => deletedTypes
                    .Where(t => System.Text.RegularExpressions.Regex.IsMatch(x.Code, @"\b" + t + @"\b"))
                    .Select(t => x.Name + " -> " + t))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "These core files still name a type the pivot deleted: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// The deletion must not have taken the race with it. A guard that only
        /// asserts absences passes just as happily on an empty assembly, which is
        /// exactly the "green for the wrong reason" this repo keeps being bitten by.
        /// </summary>
        [Test]
        public void TheRulesAssembly_StillHoldsTheRace()
        {
            var assembly = typeof(GameConstants).Assembly;
            var namespaces = assembly.GetTypes()
                .Select(t => t.Namespace)
                .Where(n => n != null)
                .Distinct()
                .ToArray();

            foreach (var kept in new[]
                     {
                         "HorrorGame.Core.Map",
                         "HorrorGame.Core.Monster",
                         "HorrorGame.Core.Movement",
                         "HorrorGame.Core.Race",
                     })
            {
                Assert.That(namespaces, Does.Contain(kept),
                    kept + " is part of the race and must survive the pivot's deletions.");
            }
        }
    }
}
