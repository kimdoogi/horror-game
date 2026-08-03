using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HorrorGame.Core;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// The headstone for §03's 단서 사슬 and §08's 경제, kept executable so it cannot
    /// rot into a comment nobody reads.
    /// <para>
    /// DESCENT-PIVOT §3 「버린다」 drops both outright. 단서 went because
    /// 「목적지가 처음부터 알려져 있다」 — a race announces the finish at the start, the
    /// middle of B8, and a chain that narrows a search cannot exist where there is no
    /// search. 경제 went because 「통화가 없다」 — there is no resupply in a race and no
    /// second descent to spend between, so the wallet, the shop, the loot catalogue, the
    /// safe, the shared carry and the weight-to-speed bands went with it.
    /// </para>
    ///
    /// <para><b>WHY THIS FILE WAS REWRITTEN — read this before trusting it again.</b></para>
    /// <para>
    /// The previous version of this fixture was five green tests over a build that still
    /// contained about seventy-two members of the co-operative game. It was blind twice,
    /// and both blindnesses are worth naming because both are easy to write again:
    /// </para>
    /// <list type="number">
    /// <item><b>It filtered on <c>t.Namespace</c> and on type names.</b> A member on a
    /// surviving type was invisible to it, and a member on a surviving type is exactly
    /// what survived — <c>MatchResolution.LootKept</c>, <c>RoleGap.CreditCost</c>, seventy
    /// <c>GameConstants</c> fields, fourteen <c>AudioCueId</c> rows. Deleting the type
    /// <c>Wallet</c> and keeping <c>WalletStartingCredits</c> passed.</item>
    /// <item><b>It read <c>typeof(GameConstants).Assembly</c> and nothing else.</b>
    /// <c>Assembly-CSharp</c>, <c>HorrorGame.Audio</c>, the gameplay assemblies,
    /// <c>HorrorGame.Net</c> and <c>HorrorGame.UI</c> were never inspected at all, and
    /// most of the survivors lived there.</item>
    /// </list>
    /// <para>
    /// The second one cannot be fixed here: this project runs headless, and the
    /// Unity-side assemblies are only loaded inside the editor. It is fixed in
    /// <c>unity/HorrorGame/Assets/Tests/EditMode/Pivot/PivotTombstoneTests.cs</c>, which
    /// walks <c>CompilationPipeline.GetAssemblies(AssembliesType.Player)</c> — Unity's own
    /// answer to "what does this game ship", so no list is maintained anywhere — plus a
    /// second instrument beside it for the scenes, prefabs, <c>.asset</c> files and audio
    /// clips that live in no assembly at all.
    /// <see cref="TheEditorSideGuard_IsStillThere"/> below fails if either goes missing,
    /// so this fast suite cannot be green while the only instrument that sees the other
    /// thirteen assemblies has been deleted.
    /// </para>
    /// <para>
    /// The first one is fixed here, by
    /// <see cref="TheRulesAssembly_NamesNoDeletedCoOpDesign"/>: every type, field, enum
    /// row, property, method, constructor, event and parameter name in the rules assembly,
    /// matched against the shared vocabulary in
    /// <c>Assets/Tests/EditMode/Pivot/DeletedVocabulary.txt</c>.
    /// </para>
    /// <para>
    /// If you are here because this fixture is red, the question to answer first is not
    /// "how do I make it pass" but "what currency did I just add to a game that has none".
    /// </para>
    /// </summary>
    [TestFixture]
    public class PivotDeletionTests
    {
        /// <summary>
        /// The namespaces the pivot removed. Matched as namespace prefixes, so a
        /// <c>HorrorGame.Core.Economy.Something</c> smuggled back in under any name is
        /// caught without this list having to enumerate types.
        /// <para>
        /// This stays a list because a namespace is structural, not lexical: there are two
        /// of them, they are the folders that were deleted, and the set cannot grow
        /// without somebody re-creating a folder on purpose.
        /// </para>
        /// </summary>
        private static readonly string[] DeletedNamespaces =
        {
            "HorrorGame.Core.Clues",
            "HorrorGame.Core.Economy",
        };

        /// <summary>
        /// The directories those namespaces lived in, relative to Scripts/Core. Checked
        /// separately from the namespaces because a file can be restored to disk with its
        /// namespace changed, and the intent would still be the system coming back.
        /// </summary>
        private static readonly string[] DeletedDirectories = { "Clues", "Economy" };

        /// <summary>
        /// The EditMode guard, relative to the Unity <c>Assets</c> folder. Both files, so
        /// that neither half — the assemblies or the content — can go quietly.
        /// </summary>
        private static readonly string[] EditorSideGuardFiles =
        {
            "Tests/EditMode/Pivot/DeletedVocabulary.txt",
            "Tests/EditMode/Pivot/PivotVocabulary.cs",
            "Tests/EditMode/Pivot/PivotTombstoneTests.cs",
            "Tests/EditMode/Pivot/PivotAssetTombstoneTests.cs",
        };

        // ====================================================================
        // The vocabulary
        // ====================================================================

        /// <summary>The Unity <c>Assets</c> folder, derived from the core source root.</summary>
        private static DirectoryInfo AssetsRoot()
        {
            // Scripts/Core → Scripts → Assets.
            var assets = CoreSourceRootAttribute.Resolve().Parent?.Parent;

            if (assets == null || !assets.Exists)
            {
                throw new InvalidOperationException(
                    "Could not walk from the core source root up to the Unity Assets folder. "
                    + "The project layout moved; fix CoreSourceRoot in the csproj and this hop together.");
            }

            return assets;
        }

        private static DeletedVocabulary.Table? _table;
        private static string? _loadError;

        /// <summary>
        /// The shared table, parsed once. The failure is captured rather than thrown
        /// because NUnit builds <see cref="TestCaseSourceAttribute"/> cases during
        /// discovery, and an exception there takes the whole run down instead of
        /// producing one honest red test.
        /// </summary>
        private static DeletedVocabulary.Table? TryTable()
        {
            if (_table != null || _loadError != null)
            {
                return _table;
            }

            try
            {
                _table = DeletedVocabulary.Load(
                    Path.Combine(AssetsRoot().FullName, DeletedVocabulary.RelativePath));
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
            }

            return _table;
        }

        private static DeletedVocabulary.Table Table =>
            TryTable() ?? throw new InvalidOperationException(_loadError);

        // ====================================================================
        // The member-deep sweep — the blindness this file was rewritten for
        // ====================================================================

        /// <summary>
        /// Nothing in the rules assembly may name a design the pivot deleted, at any depth.
        /// <para>
        /// Types, fields, enum rows, properties, methods, constructors, events and
        /// parameter names — not just namespaces and type names, which is what the old
        /// version looked at and why it missed seventy-two members. The failure message
        /// names every survivor with its type and member and ends with the sentence that
        /// says why each word is forbidden, because a guard that reports "1 failure" hands
        /// the next person a red tick and a guard that hands them a work list gets the
        /// work done.
        /// </para>
        /// <para>
        /// Scope is deliberately one assembly. The Unity-side guard sweeps all fourteen;
        /// this one runs in two seconds without an editor, so it is the one that catches a
        /// mistake before it is committed.
        /// </para>
        /// </summary>
        [Test]
        public void TheRulesAssembly_NamesNoDeletedCoOpDesign()
        {
            var table = Table;
            var assembly = typeof(GameConstants).Assembly;

            var offenders = new List<(string Type, string Kind, string Member, string Words)>();
            var hits = new List<DeletedVocabulary.Hit>();
            var types = 0;
            var members = 0;

            foreach (var type in assembly.GetTypes()
                         .Where(t => !IsGenerated(t.Name))
                         .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false))
                         .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                types++;
                var full = type.FullName ?? type.Name;

                var namespaceTokens = type.Namespace == null
                    ? Array.Empty<string>()
                    : (IReadOnlyList<string>)DeletedVocabulary.Tokenize(type.Namespace);

                Record(offenders, hits, full, "type", string.Empty,
                    DeletedVocabulary.Match(table, full, namespaceTokens));

                // The declaring type's own words count as neighbours for a compound word,
                // so RoleSubstituteItem.Flare reads as a shop item and not as a lone verb.
                var typeTokens = DeletedVocabulary.Tokenize(type.Name);

                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                                                       | BindingFlags.Instance | BindingFlags.Static
                                                       | BindingFlags.DeclaredOnly))
                {
                    if (member.MemberType == MemberTypes.NestedType
                        || IsGenerated(member.Name)
                        || member.IsDefined(typeof(CompilerGeneratedAttribute), false))
                    {
                        continue;
                    }

                    // Accessors and operators restate a member checked in its own right.
                    if (member is MethodBase special && special.IsSpecialName)
                    {
                        continue;
                    }

                    members++;
                    Record(offenders, hits, full, KindOf(member), member.Name,
                        DeletedVocabulary.Match(table, member.Name, typeTokens));

                    if (member is MethodBase callable)
                    {
                        foreach (var parameter in callable.GetParameters().Where(p => p.Name != null))
                        {
                            members++;
                            Record(offenders, hits, full, "param",
                                member.Name + "(" + parameter.Name + ")",
                                DeletedVocabulary.Match(table, parameter.Name!, typeTokens));
                        }
                    }
                }
            }

            TestContext.WriteLine($"Swept {types} types and {members} members of {assembly.GetName().Name}.");

            if (offenders.Count == 0)
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"DESCENT-PIVOT §3 — {offenders.Count} member(s) of the co-operative game "
                              + $"are still in {assembly.GetName().Name}.");
            report.AppendLine($"Swept {types} types, {members} members.");
            report.AppendLine();

            foreach (var byType in offenders
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
            report.AppendLine("왜 이 낱말들이 금지인가 —");
            report.AppendLine(DeletedVocabulary.Legend(hits));
            report.AppendLine();
            report.AppendLine("DELETE, DO NOT STUB. If a caller breaks, delete the caller. A no-op left to");
            report.AppendLine("satisfy a call site is how the last three rounds of this leaked back in.");
            report.AppendLine("This assembly is one of fourteen — run the EditMode guard for the rest.");

            Assert.Fail(report.ToString());
        }

        private static void Record(List<(string Type, string Kind, string Member, string Words)> offenders,
            List<DeletedVocabulary.Hit> hits, string type, string kind, string member,
            IReadOnlyList<DeletedVocabulary.Hit> found)
        {
            if (found.Count == 0)
            {
                return;
            }

            hits.AddRange(found);
            offenders.Add((type, kind, member, string.Join(" ", DeletedVocabulary.Distinct(found))));
        }

        private static bool IsGenerated(string name) => name.IndexOf('<') >= 0 || name.IndexOf('>') >= 0;

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
        // The other blindness — this suite can only see one assembly
        // ====================================================================

        /// <summary>
        /// The editor-side guard must still exist.
        /// <para>
        /// This suite can only reach <c>HorrorGame.Core</c>. Thirteen other assemblies and
        /// the whole of <c>Assets/</c> are visible only from inside the editor, and the
        /// files listed in <see cref="EditorSideGuardFiles"/> are the only thing that looks
        /// at them. Deleting one of them would restore the exact hole this rewrite closed,
        /// and it would restore it silently — a green dotnet run over a build with the
        /// co-op game back in it. So the fast suite refuses to be green without them.
        /// </para>
        /// </summary>
        [Test]
        public void TheEditorSideGuard_IsStillThere()
        {
            var assets = AssetsRoot().FullName;

            var missing = EditorSideGuardFiles
                .Where(f => !File.Exists(Path.Combine(assets, f.Replace('/', Path.DirectorySeparatorChar))))
                .ToArray();

            Assert.That(missing, Is.Empty,
                "These files are the only instruments that inspect the thirteen assemblies and the "
                + "Assets/ tree this suite cannot see, and they are gone: " + string.Join(", ", missing)
                + ". Without them a green run here means nothing about what the game ships.");
        }

        // ====================================================================
        // The source tree
        // ====================================================================

        /// <summary>
        /// The source directories must be gone too, not merely empty of types. A folder
        /// that still exists is an invitation to refill it.
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
        /// No core source may declare a deleted namespace, whatever folder it sits in.
        /// Catches the reintroduction that moves the file rather than the type.
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

        // TOMBSTONE — CoreSources_NameNoDeletedType went here.
        //
        // It held a hardcoded roll of twenty-eight deleted type names and regex-matched the
        // core sources for them. It passed for months over a build with seventy-two co-op
        // members in it, because a list of yesterday's names cannot see today's. Worse, it
        // taught the wrong lesson: when something slipped through, the reflex was to add a
        // name to the list rather than to ask why the guard could not see it.
        //
        // TheRulesAssembly_NamesNoDeletedCoOpDesign replaces it and is strictly stronger:
        // it reads the compiled artefact instead of the text, it sees members and not only
        // types, and it matches the NOUNS of the deleted designs rather than a snapshot of
        // their spellings — so a type invented tomorrow under an old noun is caught with no
        // edit anywhere. Do not bring the list back.

        // ====================================================================
        // The guard's own instruments
        // ====================================================================

        /// <summary>
        /// The rule book parses and is not empty. An empty vocabulary passes every other
        /// test in this file, which is precisely the shape of the bug being guarded
        /// against.
        /// </summary>
        [Test]
        public void TheVocabularyTable_Loads()
        {
            Assert.That(TryTable(), Is.Not.Null,
                DeletedVocabulary.RelativePath + " did not parse: " + _loadError
                + ". Both tombstone guards read that file, so neither is guarding anything until it does.");

            TestContext.WriteLine($"{Table.Words.Count} deleted words, {Table.RaceNouns.Count} race nouns, "
                                  + $"{Table.TokenProbes.Count + Table.MatchProbes.Count} probes.");
        }

        private static IEnumerable<DeletedVocabulary.Probe> TokenProbes() =>
            TryTable()?.TokenProbes ?? Enumerable.Empty<DeletedVocabulary.Probe>();

        private static IEnumerable<DeletedVocabulary.Probe> MatchProbes() =>
            TryTable()?.MatchProbes ?? Enumerable.Empty<DeletedVocabulary.Probe>();

        /// <summary>
        /// The tokeniser splits identifiers where the shared table says it does.
        /// <para>
        /// The editor-side guard runs these same rows against its own copy of the
        /// tokeniser. Unity compiles nothing outside <c>Assets/</c> and this project
        /// references nothing inside it, so there are necessarily two copies; sharing the
        /// probe rows is what stops them drifting apart in silence.
        /// </para>
        /// </summary>
        [TestCaseSource(nameof(TokenProbes))]
        public void TheTokeniser_SplitsWhereThePinnedCasesSay(DeletedVocabulary.Probe probe)
        {
            Assert.That(DeletedVocabulary.Tokenize(probe.Input), Is.EqualTo(probe.Expected),
                $"'{probe.Input}' must tokenise to [{string.Join(" ", probe.Expected)}]. Every decision "
                + "this guard makes rests on the split being right.");
        }

        /// <summary>
        /// The matcher fires on exactly the pinned identifiers and no others.
        /// <para>
        /// The negative rows are the point of the whole design. 「van」 sits inside
        /// 「advance」 and this project ships eleven <c>Advance*</c> identifiers; 「clue」
        /// sits inside 「include」; 「panel」 sits inside every UI <c>Panel</c>; 「safe」
        /// means 안전한 three times over in <c>Presence</c> and <c>PlayerMotor</c>. A
        /// substring guard reports all of those, somebody bolts an exclusion list onto it,
        /// and from that day nobody believes it. Tokenising first removes the class of
        /// mistake instead of listing its instances.
        /// </para>
        /// </summary>
        [TestCaseSource(nameof(MatchProbes))]
        public void TheMatcher_DecidesAsThePinnedCasesSay(DeletedVocabulary.Probe probe)
        {
            var got = DeletedVocabulary.Distinct(DeletedVocabulary.Match(Table, probe.Input));

            Assert.That(got, Is.EquivalentTo(probe.Expected), probe.Input + " — " + probe.Why);
        }

        // ====================================================================
        // The deletion must not have taken the race with it
        // ====================================================================

        /// <summary>
        /// A guard that only asserts absences passes just as happily on an empty assembly,
        /// which is exactly the "green for the wrong reason" this repo keeps being bitten
        /// by. These namespaces are the race, and their absence would mean the sweep above
        /// found nothing because there was nothing to find.
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

    /// <summary>
    /// The dotnet-side copy of the pivot vocabulary reader.
    /// <para>
    /// The words themselves are not here — they are in
    /// <c>unity/HorrorGame/Assets/Tests/EditMode/Pivot/DeletedVocabulary.txt</c>, one per
    /// line with the sentence that justifies it, and the editor-side guard reads the same
    /// file. Only the machinery is duplicated, because Unity compiles nothing outside
    /// <c>Assets/</c> and this project references nothing inside it. The duplication is
    /// self-detecting: both sides execute the <c>token|</c> and <c>probe|</c> rows from
    /// that file, so a copy that drifts turns its own side red.
    /// </para>
    /// <para>
    /// To collapse the duplication, link the editor-side file into this project with one
    /// line in <c>HorrorGame.Core.Tests.csproj</c> and delete this class:
    /// <c>&lt;Compile Include="$(MSBuildThisFileDirectory)../../unity/HorrorGame/Assets/Tests/EditMode/Pivot/PivotVocabulary.cs" /&gt;</c>.
    /// </para>
    /// </summary>
    public static class DeletedVocabulary
    {
        /// <summary>Path of the shared table, relative to the Unity <c>Assets</c> folder.</summary>
        public const string RelativePath = "Tests/EditMode/Pivot/DeletedVocabulary.txt";

        /// <summary>A word the pivot deleted, and the sentence that justifies deleting it.</summary>
        public sealed class Word
        {
            /// <summary>The lowercase token, or for <see cref="IsKorean"/> the Hangul noun.</summary>
            public string Token { get; }

            /// <summary>Tokens that must appear beside it. Empty when the word is proof alone.</summary>
            public IReadOnlyCollection<string> Mates { get; }

            /// <summary>The design section that deleted it.</summary>
            public string Section { get; }

            /// <summary>Why the race does not need it. This sentence is the real test.</summary>
            public string Reason { get; }

            /// <summary>Hangul nouns are matched by containment, not token equality.</summary>
            public bool IsKorean { get; }

            internal Word(string token, IReadOnlyCollection<string> mates, string section, string reason, bool korean)
            {
                Token = token;
                Mates = mates;
                Section = section;
                Reason = reason;
                IsKorean = korean;
            }
        }

        /// <summary>One matched word inside one identifier.</summary>
        public readonly struct Hit
        {
            /// <summary>The vocabulary entry that fired.</summary>
            public Word Word { get; }

            internal Hit(Word word) => Word = word;
        }

        /// <summary>A probe row: an identifier and the words it must — or must not — produce.</summary>
        public sealed class Probe
        {
            /// <summary>The identifier under test.</summary>
            public string Input { get; }

            /// <summary>The tokens it must produce, or the words it must match.</summary>
            public IReadOnlyList<string> Expected { get; }

            /// <summary>Why this probe is worth a line in the table.</summary>
            public string Why { get; }

            internal Probe(string input, IReadOnlyList<string> expected, string why)
            {
                Input = input;
                Expected = expected;
                Why = why;
            }

            /// <summary>NUnit prints this as the case name, so it carries the split.</summary>
            public override string ToString() => Input + " → [" + string.Join(" ", Expected) + "]";
        }

        /// <summary>Everything parsed out of the shared table.</summary>
        public sealed class Table
        {
            /// <summary>Words whose presence is a defect.</summary>
            public IReadOnlyList<Word> Words { get; }

            /// <summary>Korean vocabulary the race needs.</summary>
            public IReadOnlyList<string> RaceNouns { get; }

            /// <summary>Cases pinning <see cref="Tokenize"/>.</summary>
            public IReadOnlyList<Probe> TokenProbes { get; }

            /// <summary>Cases pinning <see cref="Match"/>.</summary>
            public IReadOnlyList<Probe> MatchProbes { get; }

            internal Table(IReadOnlyList<Word> words, IReadOnlyList<string> race,
                IReadOnlyList<Probe> tokenProbes, IReadOnlyList<Probe> matchProbes)
            {
                Words = words;
                RaceNouns = race;
                TokenProbes = tokenProbes;
                MatchProbes = matchProbes;
            }
        }

        /// <summary>
        /// Reads the shared table. A missing, unreadable or empty file is a hard failure
        /// and never "nothing to check" — a guard whose rule book has gone missing must go
        /// red, not quiet.
        /// </summary>
        public static Table Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException(
                    "The pivot vocabulary is missing at '" + filePath + "'. Neither tombstone guard "
                    + "knows what it is guarding against without it, so this is a hard failure rather "
                    + "than an empty pass.");
            }

            var words = new List<Word>();
            var race = new List<string>();
            var tokenProbes = new List<Probe>();
            var matchProbes = new List<Probe>();
            var lineNumber = 0;

            foreach (var raw in File.ReadAllLines(filePath))
            {
                lineNumber++;
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var f = line.Split('|').Select(x => x.Trim()).ToArray();
                switch (f[0])
                {
                    case "solo":
                        Require(f.Length == 4, filePath, lineNumber, "solo|word|section|reason");
                        words.Add(new Word(f[1].ToLowerInvariant(), Array.Empty<string>(), f[2], f[3], false));
                        break;

                    case "compound":
                        Require(f.Length == 5, filePath, lineNumber, "compound|word|mates|section|reason");
                        words.Add(new Word(f[1].ToLowerInvariant(), Split(f[2]), f[3], f[4], false));
                        break;

                    case "korean":
                        Require(f.Length == 4, filePath, lineNumber, "korean|noun|section|reason");
                        words.Add(new Word(f[1], Array.Empty<string>(), f[2], f[3], true));
                        break;

                    case "race":
                        Require(f.Length == 2, filePath, lineNumber, "race|noun");
                        race.Add(f[1]);
                        break;

                    case "token":
                        Require(f.Length == 3, filePath, lineNumber, "token|input|expected tokens");
                        tokenProbes.Add(new Probe(f[1], Split(f[2]), "tokeniser"));
                        break;

                    case "probe":
                        Require(f.Length == 4, filePath, lineNumber, "probe|identifier|expected|why");
                        matchProbes.Add(new Probe(f[1], Expected(f[2]), f[3]));
                        break;

                    case "assetprobe":
                        // Read and ignored: the asset-side rule about Unity's m_ keys only
                        // applies to the editor-side guard, which is the only one that
                        // walks Assets/. Parsed here so an unknown-kind error still means
                        // an unknown kind.
                        Require(f.Length == 4, filePath, lineNumber, "assetprobe|run|expected|why");
                        break;

                    default:
                        throw new InvalidOperationException(
                            filePath + ":" + lineNumber + " — unknown row kind '" + f[0]
                            + "'. The header of that file lists every kind it accepts.");
                }
            }

            if (words.Count == 0 || tokenProbes.Count == 0 || matchProbes.Count == 0)
            {
                throw new InvalidOperationException(
                    filePath + " parsed to " + words.Count + " words and " + tokenProbes.Count + "/"
                    + matchProbes.Count + " probes. An empty rule book passes everything, which is the "
                    + "failure this fixture exists to make impossible.");
            }

            return new Table(words, race, tokenProbes, matchProbes);
        }

        private static void Require(bool ok, string file, int line, string shape)
        {
            if (!ok)
            {
                throw new InvalidOperationException(file + ":" + line + " — expected '" + shape + "'.");
            }
        }

        private static string[] Split(string value) =>
            value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.ToLowerInvariant())
                .ToArray();

        private static string[] Expected(string value) => value == "-" ? Array.Empty<string>() : Split(value);

        /// <summary>
        /// Splits an identifier into lowercase words. The boundaries are the ones C#
        /// naming actually produces: any non-alphanumeric; lower→upper; the last capital
        /// of a run when a lowercase follows, so <c>IDValue</c> is <c>id value</c>;
        /// letter↔digit, so <c>Safe01</c> is <c>safe 01</c>; and ASCII↔non-ASCII, so
        /// <c>투하구Chute</c> is two tokens. Hangul runs stay whole and are matched by
        /// containment instead, because Korean has no boundary to split on.
        /// </summary>
        public static IReadOnlyList<string> Tokenize(string identifier)
        {
            if (identifier == null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }

            var tokens = new List<string>();
            var current = new StringBuilder();

            for (var i = 0; i < identifier.Length; i++)
            {
                var c = identifier[i];

                if (!char.IsLetterOrDigit(c))
                {
                    Flush(tokens, current);
                    continue;
                }

                if (current.Length > 0)
                {
                    var previous = current[current.Length - 1];
                    var next = i + 1 < identifier.Length ? identifier[i + 1] : '\0';

                    var boundary =
                        (char.IsUpper(c) && char.IsLower(previous))
                        || (char.IsUpper(c) && char.IsUpper(previous) && char.IsLower(next))
                        || (char.IsDigit(c) != char.IsDigit(previous))
                        || ((c < 128) != (previous < 128));

                    if (boundary)
                    {
                        Flush(tokens, current);
                    }
                }

                current.Append(c);
            }

            Flush(tokens, current);
            return tokens;
        }

        private static void Flush(List<string> tokens, StringBuilder current)
        {
            if (current.Length == 0)
            {
                return;
            }

            tokens.Add(current.ToString().ToLowerInvariant());
            current.Length = 0;
        }

        /// <summary>
        /// Every deleted word inside <paramref name="identifier"/>. <paramref name="context"/>
        /// supplies extra neighbour tokens — in practice the declaring type's — so
        /// <c>RoleSubstituteItem.Flare</c> reads as a shop item while
        /// <c>MonsterAcquireTell._crestFlareDegrees</c> stays a crest that flares.
        /// </summary>
        public static IReadOnlyList<Hit> Match(Table table, string identifier,
            IReadOnlyList<string>? context = null)
        {
            var tokens = Tokenize(identifier);
            List<Hit>? hits = null;

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                foreach (var word in table.Words)
                {
                    if (word.IsKorean)
                    {
                        if (token.IndexOf(word.Token, StringComparison.Ordinal) >= 0)
                        {
                            (hits ??= new List<Hit>()).Add(new Hit(word));
                        }

                        continue;
                    }

                    if (!string.Equals(token, word.Token, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (word.Mates.Count > 0 && !HasMate(word, tokens, i, context))
                    {
                        continue;
                    }

                    (hits ??= new List<Hit>()).Add(new Hit(word));
                }
            }

            return (IReadOnlyList<Hit>?)hits ?? Array.Empty<Hit>();
        }

        private static bool HasMate(Word word, IReadOnlyList<string> tokens, int index,
            IReadOnlyList<string>? context)
        {
            if (index > 0 && word.Mates.Contains(tokens[index - 1]))
            {
                return true;
            }

            if (index + 1 < tokens.Count && word.Mates.Contains(tokens[index + 1]))
            {
                return true;
            }

            return context != null && context.Any(t => word.Mates.Contains(t));
        }

        /// <summary>The distinct words a set of hits fired, in a stable order.</summary>
        public static string[] Distinct(IReadOnlyList<Hit> hits) =>
            hits.Select(h => h.Word.Token).Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal).ToArray();

        /// <summary>
        /// The legend a failure message ends with: every word that fired, once, with the
        /// sentence that says why the race does not need it.
        /// </summary>
        public static string Legend(IEnumerable<Hit> hits) =>
            string.Join(Environment.NewLine, hits
                .Select(h => h.Word)
                .Distinct()
                .OrderBy(w => w.Section, StringComparer.Ordinal)
                .ThenBy(w => w.Token, StringComparer.Ordinal)
                .Select(w => "    " + w.Token.PadRight(12, ' ') + w.Section + " · " + w.Reason));
    }
}
