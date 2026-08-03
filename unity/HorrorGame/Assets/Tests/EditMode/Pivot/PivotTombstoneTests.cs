#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;
using CompiledAssembly = System.Reflection.Assembly;

namespace HorrorGame.Tests.EditMode.Pivot
{
    /// <summary>
    /// The tombstone for the co-operative game, read off the compiled artefact rather
    /// than off the source tree.
    /// <para>
    /// <b>Why this fixture exists.</b> The previous guard,
    /// <c>core/HorrorGame.Core.Tests/PivotDeletionTests.cs</c>, was five green tests
    /// over a build that still contained about seventy-two co-op members. It was blind
    /// twice over. It filtered on <c>t.Namespace</c> and on type names, so a member on a
    /// surviving type — <c>MatchResolution.LootKept</c>, <c>RoleGap.CreditCost</c>,
    /// seventy <c>GameConstants</c> fields, fourteen <c>AudioCueId</c> rows — was
    /// invisible to it; and it read <c>typeof(GameConstants).Assembly</c> and nothing
    /// else, so <c>Assembly-CSharp</c>, <c>HorrorGame.Audio</c>, the gameplay assemblies,
    /// <c>HorrorGame.Net</c> and <c>HorrorGame.UI</c> were never looked at at all. Most
    /// of the survivors lived in exactly those.
    /// </para>
    /// <para>
    /// <b>What identifies "the assemblies this game ships".</b> Not a list anybody
    /// maintains — <c>CompilationPipeline.GetAssemblies(AssembliesType.Player)</c>. That
    /// is Unity's own answer, derived from the <c>.asmdef</c> files on disk, so a new
    /// assembly is guarded the moment it exists and a renamed one never falls out of
    /// scope. Editor-only assemblies are absent from it by construction, which is the
    /// correct scope: this guard is about what reaches a player.
    /// </para>
    /// <para>
    /// <b>Why the guard references none of the code it guards.</b> The asmdef beside this
    /// file lists no game assembly. Every type here is reached by reflection over the
    /// editor's loaded domain, so deleting a co-op type can never break the fixture that
    /// is supposed to notice the deletion — and the guard cannot be silenced by making it
    /// stop compiling.
    /// </para>
    /// <para>
    /// <b>A third blindness, found and closed after the first version of this file.</b>
    /// The first version skipped any assembly that referenced NUnit, calling it a test
    /// assembly. That is true of every real test assembly and it is also true of
    /// <c>Assembly-CSharp</c> — because nine PlayMode test files under
    /// <c>Assets/Tests/PlayMode/{Race,Ghost,Match,Interaction}</c> have no asmdef and land
    /// there, dragging NUnit in with them. So the rule skipped the one assembly holding
    /// <c>Gameplay/Race</c>, <c>Gameplay/Match</c>, <c>Gameplay/Ghost</c>,
    /// <c>Gameplay/Interaction</c>, <c>Gameplay/Playtest</c> and two of
    /// <c>Gameplay/Audio</c> — 18 source files, and the whole of the race the game is
    /// about. <see cref="TheSweep_ReachedEveryAssemblyThePlayerShips"/> was honestly red
    /// about it, saying it could not find <c>Chute</c> or <c>RaceDirector</c>.
    /// </para>
    /// <para>
    /// <b>Why the rule is now per-type and not per-assembly.</b> An assembly's NUnit
    /// reference says one of its files is a test; it does not say all of them are. So an
    /// assembly is skipped only when <em>every</em> source file lives under
    /// <c>Assets/Tests/</c>, and inside a mixed assembly it is the individual TYPE that is
    /// excluded, by carrying — itself, on a member, or on an enclosing type — an attribute
    /// from <c>NUnit.Framework</c> or <c>UnityEngine.TestTools</c>. Measured offline
    /// against <c>Library/ScriptAssemblies/Assembly-CSharp.dll</c> through
    /// <c>System.Reflection.Metadata</c>, that rule sweeps 39 types and 803 members, skips
    /// 11 test types (6 fixtures and 5 of their nested helpers), reaches both
    /// <c>HorrorGame.Gameplay.Race.Chute</c> and <c>...RaceDirector</c>, and reports 0
    /// survivors. The blindness was the whole of the difference.
    /// </para>
    /// <para>
    /// The mixture itself is still a defect, and
    /// <see cref="NoAssemblyThePlayerShips_MixesTestCodeWithGameCode"/> reports it in its
    /// own right rather than hiding it inside a passing sweep: those fixtures are in a
    /// non-test assembly, so they are excluded from this SWEEP but not from the BUILD.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class PivotTombstoneTests
    {
        /// <summary>
        /// Types that must be found while sweeping, one per shipped assembly family.
        /// <para>
        /// A guard that only asserts absences passes just as happily over an empty sweep,
        /// and "green because we looked at nothing" is this project's signature failure.
        /// These are the race itself — the storey graph, the brain that hunts, the state
        /// that ranks 주자, the 투하구, the cue table and the HUD — so keeping them true
        /// costs nothing and their absence means the sweep missed an assembly.
        /// </para>
        /// </summary>
        private static readonly string[] RaceTypesThatProveTheSweepRan =
        {
            "HorrorGame.Core.Map.MapGraph",
            "HorrorGame.Core.Monster.MonsterBrain",
            "HorrorGame.Core.Movement.SpeedResolver",
            "HorrorGame.Core.Race.RaceState",
            "HorrorGame.Gameplay.Race.Chute",
            "HorrorGame.Gameplay.Race.RaceDirector",
            "HorrorGame.Audio.AudioCueId",
            "HorrorGame.UI.RaceHud",
        };

        private static PivotVocabulary.Table? _table;
        private static string? _loadError;

        /// <summary>
        /// The shared word list, parsed once per run.
        /// <para>
        /// The failure is captured rather than thrown because NUnit builds
        /// <see cref="TestCaseSourceAttribute"/> cases during discovery, and an exception
        /// there tears down the whole EditMode run instead of producing one honest red
        /// test. <see cref="TheVocabularyTable_Loads"/> is where a broken table reports.
        /// </para>
        /// </summary>
        private static PivotVocabulary.Table? TryTable()
        {
            if (_table != null || _loadError != null)
            {
                return _table;
            }

            try
            {
                _table = PivotVocabulary.Load(
                    Path.Combine(Application.dataPath, PivotVocabulary.RelativePath));
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
            }

            return _table;
        }

        /// <summary>The table, for the tests that cannot run without it.</summary>
        private static PivotVocabulary.Table Table =>
            TryTable() ?? throw new InvalidOperationException(_loadError);

        // ====================================================================
        // The sweep
        // ====================================================================

        /// <summary>One member, or type, that names something the pivot deleted.</summary>
        private readonly struct Offender
        {
            public string Assembly { get; }
            public string Type { get; }
            public string Kind { get; }
            public string Member { get; }
            public string Words { get; }

            public Offender(string assembly, string type, string kind, string member, string words)
            {
                Assembly = assembly;
                Type = type;
                Kind = kind;
                Member = member;
                Words = words;
            }
        }

        /// <summary>
        /// An assembly that ships game code and test code in the same DLL.
        /// <para>
        /// Reported rather than tolerated, because the exclusion this fixture makes is a
        /// sweep-side one and the build makes no such exclusion.
        /// </para>
        /// </summary>
        private readonly struct Mixture
        {
            public string Assembly { get; }
            public int GameFiles { get; }
            public int TestFiles { get; }

            public Mixture(string assembly, int gameFiles, int testFiles)
            {
                Assembly = assembly;
                GameFiles = gameFiles;
                TestFiles = testFiles;
            }
        }

        /// <summary>What one sweep saw, so the guard can prove it looked.</summary>
        private sealed class Sweep
        {
            public List<Offender> Offenders { get; } = new List<Offender>();
            public List<PivotVocabulary.Hit> Hits { get; } = new List<PivotVocabulary.Hit>();
            public List<string> Inspected { get; } = new List<string>();
            public List<string> SkippedTestAssemblies { get; } = new List<string>();
            public List<string> SkippedTestTypes { get; } = new List<string>();
            public List<Mixture> Mixed { get; } = new List<Mixture>();
            public List<string> Unresolved { get; } = new List<string>();
            public HashSet<string> TypeNames { get; } = new HashSet<string>(StringComparer.Ordinal);
            public int TypeCount { get; set; }
            public int MemberCount { get; set; }
        }

        /// <summary>
        /// Walks every assembly the player build ships and records everything that still
        /// names a deleted design.
        /// </summary>
        private static Sweep Run()
        {
            var table = Table;
            var sweep = new Sweep();

            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .GroupBy(a => a.GetName().Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var shipped in CompilationPipeline.GetAssemblies(AssembliesType.Player)
                         .Where(IsOurs)
                         .OrderBy(a => a.name, StringComparer.Ordinal))
            {
                if (!loaded.TryGetValue(shipped.name, out var assembly))
                {
                    sweep.Unresolved.Add(shipped.name);
                    continue;
                }

                // A test assembly is one whose every source file lives under Assets/Tests/.
                // NOT one that merely references NUnit: Assembly-CSharp does, because nine
                // PlayMode test files have no asmdef and land in it, and skipping on that
                // basis is what hid Gameplay/Race, Match, Ghost, Interaction and Playtest
                // from this sweep entirely. The path is the honest question — 「이 어셈블리는
                // 테스트만 담고 있는가」 — and it excludes this fixture too, which
                // necessarily spells every forbidden word out loud.
                var testFiles = shipped.sourceFiles.Count(IsUnderTests);

                if (testFiles == shipped.sourceFiles.Length)
                {
                    sweep.SkippedTestAssemblies.Add(shipped.name);
                    continue;
                }

                if (testFiles > 0)
                {
                    sweep.Mixed.Add(new Mixture(shipped.name,
                        shipped.sourceFiles.Length - testFiles, testFiles));
                }

                sweep.Inspected.Add(shipped.name);
                Inspect(table, assembly, sweep);
            }

            return sweep;
        }

        /// <summary>
        /// True when the assembly is compiled from source this project owns.
        /// <para>
        /// Mirror, Unity.Burst, Unity.InputSystem and the rest of the package set are
        /// Player assemblies and they DO ship, but they are not ours to delete from, and
        /// their vocabulary is not this design's: <c>Bitwise.ExtractBits</c>,
        /// <c>mm256_extract_epi32</c>, <c>HIDDeviceDescriptor.vendorId</c> and
        /// <c>CommonUsages.BatteryStrength</c> are all real matches and all meaningless
        /// here. Left in, they buried nine genuine survivors under 257 rows nobody could
        /// act on — and a guard whose output cannot be acted on is a guard that gets
        /// muted. Judged by where the source lives, not by a list of package names, so a
        /// new package is excluded the moment it arrives.
        /// </para>
        /// </summary>
        private static bool IsOurs(UnityEditor.Compilation.Assembly assembly)
        {
            foreach (var file in assembly.sourceFiles)
            {
                return file.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal);
            }

            return false;
        }

        /// <summary>Where this project keeps test source. One place, checked by path.</summary>
        private const string TestSourceRoot = "Assets/Tests/";

        private static bool IsUnderTests(string sourceFile) =>
            sourceFile.Replace('\\', '/').StartsWith(TestSourceRoot, StringComparison.Ordinal);

        /// <summary>
        /// Attribute namespaces that mark a type as test scaffolding rather than game code.
        /// <para>
        /// Both are needed. NUnit's <c>[Test]</c>, <c>[TestFixture]</c> and <c>[SetUp]</c>
        /// live in the first; Unity's <c>[UnityTest]</c> and <c>[UnitySetUp]</c> live in
        /// the second, and a coroutine fixture can carry nothing but those.
        /// </para>
        /// </summary>
        private static readonly string[] TestAttributeNamespaces = { "NUnit.Framework", "UnityEngine.TestTools" };

        /// <summary>
        /// True when this type is a test fixture or lives inside one.
        /// <para>
        /// Judged by attribute and not by a name ending in <c>Tests</c>, because a name is
        /// a convention and an attribute is what the runner itself reads. The walk up
        /// <see cref="Type.DeclaringType"/> is what covers the helper classes nested inside
        /// a fixture — <c>DescentPlaythroughTests.Leg</c>, <c>EscapeTests.Verdict</c> —
        /// which carry no attribute of their own and would otherwise be swept as game code.
        /// </para>
        /// <para>
        /// The exclusion is from the SWEEP only. A fixture in a non-test assembly still
        /// compiles into the player build; that is
        /// <see cref="NoAssemblyThePlayerShips_MixesTestCodeWithGameCode"/>'s business.
        /// </para>
        /// </summary>
        private static bool IsTestScaffolding(Type type)
        {
            for (var t = type; t != null; t = t.DeclaringType)
            {
                if (CarriesATestAttribute(t))
                {
                    return true;
                }

                MemberInfo[] members;
                try
                {
                    members = t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                                           | BindingFlags.Instance | BindingFlags.Static
                                           | BindingFlags.DeclaredOnly);
                }
                catch (Exception)
                {
                    // An unloadable member list cannot prove this is a test, and guessing
                    // "test" here would silently shrink the sweep. Sweep it instead.
                    return false;
                }

                foreach (var member in members)
                {
                    if (CarriesATestAttribute(member))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Reads attributes without binding to them.
        /// <para>
        /// <c>GetCustomAttributesData</c> rather than <c>GetCustomAttributes</c>: the first
        /// reads metadata, the second constructs the attribute. This asmdef deliberately
        /// references no game assembly, so an attribute it cannot construct must not become
        /// an exception that ends the run.
        /// </para>
        /// </summary>
        private static bool CarriesATestAttribute(MemberInfo member)
        {
            try
            {
                foreach (var attribute in member.GetCustomAttributesData())
                {
                    var space = attribute.AttributeType.Namespace;
                    if (space == null)
                    {
                        continue;
                    }

                    foreach (var candidate in TestAttributeNamespaces)
                    {
                        if (space.Equals(candidate, StringComparison.Ordinal)
                            || space.StartsWith(candidate + ".", StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static void Inspect(PivotVocabulary.Table table, CompiledAssembly assembly, Sweep sweep)
        {
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Partial load is still worth sweeping; the missing part is reported.
                types = ex.Types;
                sweep.Unresolved.Add(assembly.GetName().Name + " (partial: "
                                     + ex.LoaderExceptions.Length + " loader errors)");
            }

            var name = assembly.GetName().Name;

            foreach (var type in types)
            {
                if (type == null || IsGenerated(type.Name) || type.IsDefined(typeof(CompilerGeneratedAttribute), false))
                {
                    continue;
                }

                var full = type.FullName ?? type.Name;

                if (IsTestScaffolding(type))
                {
                    sweep.SkippedTestTypes.Add(name + " :: " + full);
                    continue;
                }

                sweep.TypeCount++;
                sweep.TypeNames.Add(full);

                var namespaceTokens = type.Namespace == null
                    ? Array.Empty<string>()
                    : (IReadOnlyList<string>)PivotVocabulary.Tokenize(type.Namespace);

                Record(sweep, name, full, "type", string.Empty,
                    PivotVocabulary.Match(table, full, namespaceTokens));

                // The declaring type's own words count as neighbours for a compound
                // word, so RoleSubstituteItem.Flare reads as a shop item rather than as
                // a lone verb.
                var typeTokens = PivotVocabulary.Tokenize(type.Name);

                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                                                       | BindingFlags.Instance | BindingFlags.Static
                                                       | BindingFlags.DeclaredOnly))
                {
                    // Nested types come back from GetTypes() in their own right.
                    if (member.MemberType == MemberTypes.NestedType)
                    {
                        continue;
                    }

                    if (IsGenerated(member.Name) || member.IsDefined(typeof(CompilerGeneratedAttribute), false))
                    {
                        continue;
                    }

                    // Property and event accessors, and operators, restate a member that
                    // is checked in its own right.
                    if (member is MethodBase method && method.IsSpecialName)
                    {
                        continue;
                    }

                    sweep.MemberCount++;
                    Record(sweep, name, full, KindOf(member), member.Name,
                        PivotVocabulary.Match(table, member.Name, typeTokens));

                    if (member is MethodBase callable)
                    {
                        foreach (var parameter in callable.GetParameters())
                        {
                            if (parameter.Name == null)
                            {
                                continue;
                            }

                            sweep.MemberCount++;
                            Record(sweep, name, full, "param", member.Name + "(" + parameter.Name + ")",
                                PivotVocabulary.Match(table, parameter.Name, typeTokens));
                        }
                    }
                }
            }
        }

        private static void Record(Sweep sweep, string assembly, string type, string kind, string member,
            IReadOnlyList<PivotVocabulary.Hit> hits)
        {
            if (hits.Count == 0)
            {
                return;
            }

            sweep.Hits.AddRange(hits);
            sweep.Offenders.Add(new Offender(assembly, type, kind, member,
                string.Join(" ", PivotVocabulary.Distinct(hits))));
        }

        /// <summary>Compiler plumbing — display classes, backing fields, iterator states.</summary>
        private static bool IsGenerated(string name) =>
            name.IndexOf('<') >= 0 || name.IndexOf('>') >= 0;

        private static string KindOf(MemberInfo member)
        {
            switch (member.MemberType)
            {
                case MemberTypes.Field: return member.DeclaringType?.IsEnum == true ? "enum" : "field";
                case MemberTypes.Property: return "prop";
                case MemberTypes.Method: return "method";
                case MemberTypes.Constructor: return "ctor";
                case MemberTypes.Event: return "event";
                default: return member.MemberType.ToString().ToLowerInvariant();
            }
        }

        // ====================================================================
        // The tests
        // ====================================================================

        /// <summary>
        /// Nothing the player build ships may name a design DESCENT-PIVOT §3 deleted —
        /// at any depth. Types, fields, enum rows, properties, methods, constructors,
        /// events and parameter names, in every shipped assembly.
        /// <para>
        /// The failure message names every survivor with its assembly, type and member,
        /// and ends with the sentence that says why each word is forbidden. That is
        /// deliberate: a guard that reports "1 failure" hands the next person a red tick,
        /// and a guard that hands them a work list gets the work done.
        /// </para>
        /// </summary>
        [Test]
        public void NothingTheGameShips_NamesADeletedCoOpDesign()
        {
            var sweep = Run();

            TestContext.WriteLine("Inspected " + sweep.Inspected.Count + " shipped assemblies, "
                                  + PivotVocabulary.N(sweep.TypeCount) + " types, "
                                  + PivotVocabulary.N(sweep.MemberCount) + " members.");
            TestContext.WriteLine("  " + string.Join(", ", sweep.Inspected));
            TestContext.WriteLine("  " + sweep.SkippedTestTypes.Count + " type(s) inside those were skipped as "
                                  + "test scaffolding; NoAssemblyThePlayerShips_MixesTestCodeWithGameCode "
                                  + "names them and says why that exclusion is itself a defect.");

            if (sweep.Offenders.Count == 0)
            {
                Assert.Pass("0 co-op survivors across " + sweep.Inspected.Count + " shipped assemblies.");
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("DESCENT-PIVOT §3 — " + sweep.Offenders.Count
                              + " member(s) of the co-operative game are still in what this project ships.");
            report.AppendLine("Swept " + sweep.Inspected.Count + " assemblies, "
                              + PivotVocabulary.N(sweep.TypeCount) + " types, "
                              + PivotVocabulary.N(sweep.MemberCount) + " members.");
            report.AppendLine();

            foreach (var byAssembly in sweep.Offenders
                         .OrderBy(o => o.Assembly, StringComparer.Ordinal)
                         .GroupBy(o => o.Assembly, StringComparer.Ordinal))
            {
                report.AppendLine(byAssembly.Key + "  (" + byAssembly.Count() + ")");

                foreach (var byType in byAssembly
                             .OrderBy(o => o.Type, StringComparer.Ordinal)
                             .GroupBy(o => o.Type, StringComparer.Ordinal))
                {
                    report.AppendLine("  " + byType.Key);

                    foreach (var offender in byType
                                 .OrderBy(o => o.Kind, StringComparer.Ordinal)
                                 .ThenBy(o => o.Member, StringComparer.Ordinal))
                    {
                        var subject = offender.Member.Length == 0 ? "(the type itself)" : offender.Member;
                        report.AppendLine("    " + offender.Kind.PadRight(7, ' ')
                                          + subject.PadRight(44, ' ') + offender.Words);
                    }
                }

                report.AppendLine();
            }

            report.AppendLine("왜 이 낱말들이 금지인가 —");
            report.AppendLine(PivotVocabulary.Legend(sweep.Hits));
            report.AppendLine();
            report.AppendLine("DELETE, DO NOT STUB. If a caller breaks, delete the caller; a no-op left to");
            report.AppendLine("satisfy a call site is how the last three rounds of this leaked back in.");

            Assert.Fail(report.ToString());
        }

        /// <summary>
        /// The sweep must have reached every assembly the player build ships, and must
        /// have found the race inside them.
        /// <para>
        /// This is the half of the guard that cannot be satisfied by finding nothing. An
        /// assembly Unity says it ships but the editor domain has not loaded is a hole in
        /// the sweep, not a clean result, so it fails here rather than being skipped in
        /// silence.
        /// </para>
        /// </summary>
        [Test]
        public void TheSweep_ReachedEveryAssemblyThePlayerShips()
        {
            var sweep = Run();

            TestContext.WriteLine("shipped and inspected : " + string.Join(", ", sweep.Inspected));
            TestContext.WriteLine("skipped (all source under " + TestSourceRoot + ") : "
                                  + string.Join(", ", sweep.SkippedTestAssemblies));
            TestContext.WriteLine("skipped test types    : " + sweep.SkippedTestTypes.Count);
            TestContext.WriteLine("types " + PivotVocabulary.N(sweep.TypeCount)
                                  + ", members " + PivotVocabulary.N(sweep.MemberCount));

            Assert.That(sweep.Unresolved, Is.Empty,
                "Unity lists these as player assemblies but the editor domain has no loaded copy, so "
                + "the sweep never read them: " + string.Join(", ", sweep.Unresolved)
                + ". A guard with a hole in it is worse than no guard — it is the reason this "
                + "fixture was written.");

            Assert.That(sweep.Inspected, Is.Not.Empty,
                "CompilationPipeline reported no player assemblies at all. Nothing was swept.");

            var missing = RaceTypesThatProveTheSweepRan
                .Where(t => !sweep.TypeNames.Contains(t))
                .ToArray();

            Assert.That(missing, Is.Empty,
                "The sweep did not see " + string.Join(", ", missing) + ". Either the race lost a piece "
                + "of itself, or — far more likely — the sweep silently stopped reaching an assembly and "
                + "every absence it reports is meaningless. That is not hypothetical: it is what this "
                + "test said for one round, because the skip rule was 「references NUnit」 and "
                + "Assembly-CSharp does.");
        }

        /// <summary>
        /// No assembly the player ships may hold game code and test code at once.
        /// <para>
        /// This is the defect the per-type filter above works AROUND rather than fixes, and
        /// it is reported separately so that working around it never reads as fixing it.
        /// The filter keeps a fixture's names out of the sweep; nothing keeps a fixture's
        /// IL out of the build, because <c>Assembly-CSharp</c> is not a test assembly and
        /// carries no <c>UNITY_INCLUDE_TESTS</c> constraint.
        /// </para>
        /// <para>
        /// <b>The fix, and why it is three files and not one.</b> Give
        /// <c>Assets/Scripts/Gameplay</c> an asmdef. That moves 18 runtime files out of
        /// <c>Assembly-CSharp</c> and leaves it holding only the nine test files, which
        /// still compile because a predefined assembly automatically references every
        /// <c>autoReferenced</c> asmdef — so no test needs an asmdef of its own and no
        /// existing reference breaks. But an asmdef makes Unity ignore the <c>Editor</c>
        /// special folder inside its scope, and two folders under
        /// <c>Assets/Scripts/Gameplay</c> rely on it —
        /// <c>Gameplay/Audio/Editor/{AudioSceneWiring,AudioSourceReport}.cs</c> and
        /// <c>Gameplay/Ghost/Editor/GhostSeatShot.cs</c>, all three <c>using
        /// UnityEditor</c>. Landing the Gameplay asmdef without an Editor asmdef beside
        /// each of those swallows editor-only code into a runtime assembly and breaks the
        /// player build. Every other <c>Editor</c> folder already inside an asmdef's scope
        /// in this project — Player, Monster, Presence, Steam — has exactly such a file,
        /// which is the project's own evidence for the rule.
        /// </para>
        /// </summary>
        [Test]
        public void NoAssemblyThePlayerShips_MixesTestCodeWithGameCode()
        {
            var sweep = Run();

            if (sweep.Mixed.Count == 0)
            {
                Assert.Pass("Every shipped assembly is all game or all test.");
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine(sweep.Mixed.Count + " assembly(ies) the player ships hold game code and test "
                              + "code in the same DLL.");
            report.AppendLine();

            foreach (var mixture in sweep.Mixed.OrderBy(m => m.Assembly, StringComparer.Ordinal))
            {
                report.AppendLine("  " + mixture.Assembly + "  —  " + mixture.GameFiles
                                  + " game source file(s) + " + mixture.TestFiles + " under " + TestSourceRoot);
            }

            report.AppendLine();
            report.AppendLine("The sweep excluded " + sweep.SkippedTestTypes.Count
                              + " test type(s) from these by NUnit/UnityEngine.TestTools attribute:");

            foreach (var skipped in sweep.SkippedTestTypes.OrderBy(t => t, StringComparer.Ordinal))
            {
                report.AppendLine("    " + skipped);
            }

            report.AppendLine();
            report.AppendLine("That exclusion is the SWEEP's, not the BUILD's. These fixtures are in a");
            report.AppendLine("non-test assembly with no UNITY_INCLUDE_TESTS constraint, so their IL is in");
            report.AppendLine("what the player gets, and their vocabulary is judged by nothing at all.");
            report.AppendLine();
            report.AppendLine("FIX — three files, and they land together or not at all:");
            report.AppendLine("  1. Assets/Scripts/Gameplay/HorrorGame.Gameplay.asmdef");
            report.AppendLine("  2. Assets/Scripts/Gameplay/Audio/Editor/HorrorGame.Gameplay.Audio.Editor.asmdef");
            report.AppendLine("  3. Assets/Scripts/Gameplay/Ghost/Editor/HorrorGame.Gameplay.Ghost.Editor.asmdef");
            report.AppendLine("(1) alone swallows the three editor-only files under (2) and (3) into a runtime");
            report.AppendLine("assembly and breaks the player build: an asmdef makes Unity ignore the Editor");
            report.AppendLine("special folder inside its own scope.");

            Assert.Fail(report.ToString());
        }

        // ====================================================================
        // The guard's own instruments
        // ====================================================================

        /// <summary>
        /// The rule book itself parses, and is not empty.
        /// <para>
        /// An empty vocabulary passes every other test in this file. That is precisely the
        /// shape of the bug being guarded against, so the table's own health is asserted
        /// rather than assumed.
        /// </para>
        /// </summary>
        [Test]
        public void TheVocabularyTable_Loads()
        {
            Assert.That(TryTable(), Is.Not.Null,
                "Assets/" + PivotVocabulary.RelativePath + " did not parse: " + _loadError
                + ". Both tombstone guards read that file, so neither is guarding anything until it does.");

            TestContext.WriteLine(Table.Words.Count + " deleted words, "
                                  + Table.RaceNouns.Count + " race nouns, "
                                  + (Table.TokenProbes.Count + Table.MatchProbes.Count + Table.AssetProbes.Count)
                                  + " probes.");
        }

        /// <summary>Cases from the shared table, so both guards prove the same tokeniser.</summary>
        private static IEnumerable<PivotVocabulary.Probe> TokenProbes() =>
            TryTable()?.TokenProbes ?? Enumerable.Empty<PivotVocabulary.Probe>();

        /// <summary>Cases from the shared table for the code-side matcher.</summary>
        private static IEnumerable<PivotVocabulary.Probe> MatchProbes() =>
            TryTable()?.MatchProbes ?? Enumerable.Empty<PivotVocabulary.Probe>();

        /// <summary>Cases from the shared table for the asset-side matcher.</summary>
        private static IEnumerable<PivotVocabulary.Probe> AssetProbes() =>
            TryTable()?.AssetProbes ?? Enumerable.Empty<PivotVocabulary.Probe>();

        /// <summary>
        /// The tokeniser splits identifiers where the pinned cases say it does.
        /// <para>
        /// <c>core/HorrorGame.Core.Tests/PivotDeletionTests.cs</c> runs the same rows
        /// against its own copy, which is what keeps the two copies honest.
        /// </para>
        /// </summary>
        [TestCaseSource(nameof(TokenProbes))]
        public void TheTokeniser_SplitsWhereThePinnedCasesSay(PivotVocabulary.Probe probe)
        {
            Assert.That(PivotVocabulary.Tokenize(probe.Input), Is.EqualTo(probe.Expected),
                "'" + probe.Input + "' must tokenise to [" + string.Join(" ", probe.Expected) + "]. "
                + "Every decision this guard makes rests on the split being right.");
        }

        /// <summary>
        /// The matcher fires on exactly the pinned identifiers and no others.
        /// <para>
        /// The negative rows are the point. 「van」 inside 「advance」, 「clue」 inside
        /// 「include」, 「panel」 inside a UI <c>Panel</c>, 「safe」 meaning 안전한 — all
        /// four have already produced a false alarm in this repository, and a guard that
        /// cries wolf is a guard somebody eventually switches off.
        /// </para>
        /// </summary>
        [TestCaseSource(nameof(MatchProbes))]
        public void TheMatcher_DecidesAsThePinnedCasesSay(PivotVocabulary.Probe probe)
        {
            var got = PivotVocabulary.Distinct(PivotVocabulary.Match(Table, probe.Input));

            Assert.That(got, Is.EquivalentTo(probe.Expected), probe.Input + " — " + probe.Why);
        }

        /// <summary>
        /// The asset-side matcher agrees with its own pinned cases, including the one
        /// extra rule it has: Unity's <c>m_</c> keys are the engine's, not ours.
        /// </summary>
        [TestCaseSource(nameof(AssetProbes))]
        public void TheAssetMatcher_DecidesAsThePinnedCasesSay(PivotVocabulary.Probe probe)
        {
            var got = PivotVocabulary.Distinct(PivotVocabulary.MatchAssetRun(Table, probe.Input));

            Assert.That(got, Is.EquivalentTo(probe.Expected), probe.Input + " — " + probe.Why);
        }

        /// <summary>
        /// The Hangul nouns are matched by containment, because a Korean identifier has no
        /// internal boundary to split on. Containment is the one place a substring search
        /// survives in this guard, so it is fenced.
        /// <para>
        /// The fence runs one way, and the direction is the whole point. A false alarm
        /// happens when a word the race needs <em>contains</em> a forbidden noun — that is
        /// 「advance」 ⊃ 「van」 written in Hangul, and it is forbidden here. The reverse
        /// nesting is harmless and the table has one on purpose: 「목표물」 contains
        /// 「목표」, and since the matcher looks for 목표물 inside the text, a scene that
        /// says 목표 지점 does not fire. Keeping that pair visible is more useful than
        /// renaming one of them to make a symmetric rule pass.
        /// </para>
        /// </summary>
        [Test]
        public void TheKoreanNouns_CannotSwallowTheRaceOwnVocabulary()
        {
            var korean = Table.Words.Where(w => w.IsKorean).ToArray();

            Assert.That(korean, Is.Not.Empty, "The table lists no Korean nouns; the fence has nothing to guard.");
            Assert.That(Table.RaceNouns, Is.Not.Empty,
                "The table lists no race| vocabulary, so the containment fence proves nothing.");

            var collisions = (from deleted in korean
                              from raceNoun in Table.RaceNouns
                              where raceNoun.Contains(deleted.Token)
                              select raceNoun + " ⊃ " + deleted.Token).ToArray();

            Assert.That(collisions, Is.Empty,
                "These words the race needs contain a deleted Korean noun: "
                + string.Join(", ", collisions)
                + ". Containment cannot tell them apart, so the guard would report the race itself — "
                + "one of the two has to be renamed before it can be believed.");
        }

        /// <summary>
        /// Every word in the table carries the sentence that justifies it.
        /// <para>
        /// This is the only defence against the list rotting. A word whose reason nobody
        /// could write is a word nobody can argue with when the guard goes red, and an
        /// argument nobody can have is how an exclusion list gets added instead.
        /// </para>
        /// </summary>
        [Test]
        public void EveryDeletedWord_CarriesTheReasonTheRaceDoesNotNeedIt()
        {
            var thin = Table.Words
                .Where(w => w.Reason.Length < 12 || w.Section.Length == 0)
                .Select(w => w.Token)
                .ToArray();

            Assert.That(thin, Is.Empty,
                "These vocabulary rows have no usable reason: " + string.Join(", ", thin)
                + ". 「이 경주는 이것이 필요 없다, 왜냐하면 ...」 — 문장을 못 끝내면 그 낱말은 목록에 있으면 안 된다.");

            var duplicates = Table.Words
                .GroupBy(w => w.Token, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            Assert.That(duplicates, Is.Empty,
                "Duplicated vocabulary rows: " + string.Join(", ", duplicates)
                + ". Two rows for one word means two reasons, and the guard reports whichever it read first.");
        }
    }
}
