#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HorrorGame.Tests.EditMode.Pivot
{
    /// <summary>
    /// The deleted co-op vocabulary, and the only two operations the tombstone
    /// guards need: split an identifier into words, and decide whether any of those
    /// words belongs to a design DESCENT-PIVOT §3 threw away.
    /// <para>
    /// <b>Why a tokeniser and not a search.</b> The obvious guard greps names for
    /// substrings, and this repository has already watched that fail in both
    /// directions. 「van」 sits inside 「advance」 — the shipped assemblies hold eleven
    /// <c>Advance*</c> identifiers and every one of them is the gait cycle moving
    /// forward. 「clue」 sits inside 「include」. 「panel」 sits inside every UI
    /// <c>Panel</c>. A substring guard cries wolf until somebody bolts an exclusion
    /// list onto it, and an exclusion list is how a guard stops being believed.
    /// Splitting the identifier into words first removes the whole class of mistake:
    /// <c>Advance</c> is one word and that word is not 「van」.
    /// </para>
    /// <para>
    /// <b>Why the word list is nouns and not names.</b> A roll of the 72 members that
    /// survived the last pivot would be green the day it was written. These are the
    /// nouns of the deleted designs — 단서, 전리품, 상점, 목표물 — so the list goes
    /// stale only when the design does, and a member invented tomorrow under an old
    /// noun is caught with no edit here at all.
    /// </para>
    /// <para>
    /// The words themselves live in <c>DeletedVocabulary.txt</c> beside this file,
    /// one per line with the sentence that justifies it, because the dotnet-side
    /// guard in <c>core/HorrorGame.Core.Tests/PivotDeletionTests.cs</c> reads the
    /// same file and Unity compiles nothing outside <c>Assets/</c>.
    /// </para>
    /// </summary>
    public static class PivotVocabulary
    {
        /// <summary>Path of the shared table, relative to the Unity <c>Assets</c> folder.</summary>
        public const string RelativePath = "Tests/EditMode/Pivot/DeletedVocabulary.txt";

        /// <summary>A word the pivot deleted, and the sentence that justifies deleting it.</summary>
        public sealed class Word
        {
            /// <summary>The lowercase token, or for <see cref="IsKorean"/> the Hangul noun.</summary>
            public string Token { get; }

            /// <summary>
            /// Tokens that must appear beside <see cref="Token"/> for it to count.
            /// Empty when the word is proof on its own.
            /// </summary>
            public IReadOnlyCollection<string> Mates { get; }

            /// <summary>The design section that deleted it — 「§08 경제」 and so on.</summary>
            public string Section { get; }

            /// <summary>Why the race does not need it. This sentence is the real test.</summary>
            public string Reason { get; }

            /// <summary>Hangul nouns are matched by containment, not by token equality.</summary>
            public bool IsKorean { get; }

            internal Word(string token, IReadOnlyCollection<string> mates, string section, string reason, bool korean)
            {
                Token = token;
                Mates = mates;
                Section = section;
                Reason = reason;
                IsKorean = korean;
            }

            /// <summary>One line for a failure message's legend.</summary>
            public override string ToString() => Token + "  " + Section + " · " + Reason;
        }

        /// <summary>One matched word inside one identifier.</summary>
        public readonly struct Hit
        {
            /// <summary>The vocabulary entry that fired.</summary>
            public Word Word { get; }

            /// <summary>The token as it appeared, lowercased.</summary>
            public string Token { get; }

            internal Hit(Word word, string token)
            {
                Word = word;
                Token = token;
            }
        }

        /// <summary>A probe row: an identifier and the words it must — or must not — produce.</summary>
        public sealed class Probe
        {
            /// <summary>The identifier or raw text run under test.</summary>
            public string Input { get; }

            /// <summary>The tokens the input must produce, or the words it must match.</summary>
            public IReadOnlyList<string> Expected { get; }

            /// <summary>Why this probe is worth a line in the table.</summary>
            public string Why { get; }

            internal Probe(string input, IReadOnlyList<string> expected, string why)
            {
                Input = input;
                Expected = expected;
                Why = why;
            }

            /// <summary>NUnit prints this as the case name, so it has to carry the reason.</summary>
            public override string ToString() => Input + " → [" + string.Join(" ", Expected) + "]";
        }

        /// <summary>Everything parsed out of <c>DeletedVocabulary.txt</c>.</summary>
        public sealed class Table
        {
            /// <summary>Words whose presence is a defect.</summary>
            public IReadOnlyList<Word> Words { get; }

            /// <summary>Korean vocabulary the race needs, used to prove the Hangul nouns are safe.</summary>
            public IReadOnlyList<string> RaceNouns { get; }

            /// <summary>Cases pinning <see cref="Tokenize"/>.</summary>
            public IReadOnlyList<Probe> TokenProbes { get; }

            /// <summary>Cases pinning <see cref="Match"/> over code identifiers.</summary>
            public IReadOnlyList<Probe> MatchProbes { get; }

            /// <summary>Cases pinning <see cref="MatchAssetRun"/> over asset text.</summary>
            public IReadOnlyList<Probe> AssetProbes { get; }

            internal Table(IReadOnlyList<Word> words, IReadOnlyList<string> raceNouns,
                IReadOnlyList<Probe> tokenProbes, IReadOnlyList<Probe> matchProbes,
                IReadOnlyList<Probe> assetProbes)
            {
                Words = words;
                RaceNouns = raceNouns;
                TokenProbes = tokenProbes;
                MatchProbes = matchProbes;
                AssetProbes = assetProbes;
            }
        }

        // ====================================================================
        // Parsing
        // ====================================================================

        /// <summary>
        /// Reads the shared table.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The file is missing, unreadable, or holds no words. All three are treated as
        /// failures rather than as "nothing to check" — a guard whose rule book has gone
        /// missing must go red, not quiet. That is the exact shape of the bug this whole
        /// fixture exists to prevent.
        /// </exception>
        public static Table Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException(
                    "The pivot vocabulary is missing at '" + filePath + "'. Without it neither "
                    + "tombstone guard knows what it is guarding against, so this is a hard failure "
                    + "rather than an empty pass.");
            }

            var words = new List<Word>();
            var race = new List<string>();
            var tokenProbes = new List<Probe>();
            var matchProbes = new List<Probe>();
            var assetProbes = new List<Probe>();

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
                        matchProbes.Add(new Probe(f[1], ExpectedWords(f[2]), f[3]));
                        break;

                    case "assetprobe":
                        Require(f.Length == 4, filePath, lineNumber, "assetprobe|run|expected|why");
                        assetProbes.Add(new Probe(f[1], ExpectedWords(f[2]), f[3]));
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
                    + matchProbes.Count + " probes. An empty rule book passes everything, which is "
                    + "the failure this fixture exists to make impossible.");
            }

            return new Table(words, race, tokenProbes, matchProbes, assetProbes);
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

        /// <summary>A probe's expected column, where a lone dash means "nothing must match".</summary>
        private static string[] ExpectedWords(string value) =>
            value == "-" ? Array.Empty<string>() : Split(value);

        // ====================================================================
        // The tokeniser
        // ====================================================================

        /// <summary>
        /// Splits an identifier into lowercase words.
        /// <para>
        /// The boundaries are the ones C# naming actually produces:
        /// anything that is not a letter or digit (<c>_ . &lt; &gt; / -</c>, so
        /// <c>get_OpenedSafes</c> and <c>&lt;Foo&gt;d__8</c> both come apart);
        /// lower→upper (<c>LootKept</c>); the last capital of a run of capitals when a
        /// lowercase follows, so <c>IDValue</c> is <c>id value</c> and not
        /// <c>idvalue</c>; letter↔digit, so <c>Safe01</c> is <c>safe 01</c> and the
        /// digits cannot glue two words together; and ASCII↔non-ASCII, so
        /// <c>투하구Chute</c> is two tokens rather than one.
        /// </para>
        /// <para>
        /// Hangul has neither case nor an internal boundary, so a Korean run stays whole
        /// and <see cref="Match"/> handles it by containment instead.
        /// </para>
        /// </summary>
        public static IReadOnlyList<string> Tokenize(string identifier)
        {
            if (identifier == null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }

            var tokens = new List<string>();
            var current = new System.Text.StringBuilder();

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
                        || (IsAscii(c) != IsAscii(previous));

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

        private static bool IsAscii(char c) => c < 128;

        private static void Flush(List<string> tokens, System.Text.StringBuilder current)
        {
            if (current.Length == 0)
            {
                return;
            }

            tokens.Add(current.ToString().ToLowerInvariant());
            current.Length = 0;
        }

        // ====================================================================
        // The matcher
        // ====================================================================

        /// <summary>
        /// Every deleted word inside <paramref name="identifier"/>.
        /// </summary>
        /// <param name="table">The vocabulary.</param>
        /// <param name="identifier">A type name, member name or parameter name.</param>
        /// <param name="context">
        /// Extra tokens that count as neighbours for compound words — in practice the
        /// tokens of the declaring type. Without it <c>RoleSubstituteItem.Flare</c> would
        /// read as a bare 「flare」 with nothing beside it and slip through, while
        /// <c>MonsterAcquireTell._crestFlareDegrees</c> has to keep slipping through
        /// because the monster's crest really does flare.
        /// </param>
        public static IReadOnlyList<Hit> Match(Table table, string identifier, IReadOnlyList<string>? context = null)
        {
            var tokens = Tokenize(identifier);
            return MatchTokens(table, tokens, context);
        }

        /// <summary>
        /// The same decision over an already-tokenised identifier.
        /// </summary>
        public static IReadOnlyList<Hit> MatchTokens(Table table, IReadOnlyList<string> tokens,
            IReadOnlyList<string>? context = null)
        {
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
                            (hits ??= new List<Hit>()).Add(new Hit(word, token));
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

                    (hits ??= new List<Hit>()).Add(new Hit(word, token));
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

            if (context == null)
            {
                return false;
            }

            foreach (var t in context)
            {
                if (word.Mates.Contains(t))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The asset-side decision for one run of text lifted out of serialised YAML.
        /// <para>
        /// One extra rule applies there and nowhere else: a run beginning <c>m_</c> is
        /// Unity's own serialised key, not ours. Without it every scene in the project
        /// reports <c>m_ExtractAmbientOcclusion</c> as a surviving 회수 — four false
        /// positives before a single real one. Our own serialised fields are
        /// <c>_</c>-prefixed, so nothing of ours hides behind the rule; the code-side
        /// matcher deliberately does not have it, so a field literally named
        /// <c>m_Loot</c> is still caught where it is declared.
        /// </para>
        /// </summary>
        public static IReadOnlyList<Hit> MatchAssetRun(Table table, string run)
        {
            if (run.StartsWith("m_", StringComparison.Ordinal))
            {
                return Array.Empty<Hit>();
            }

            return Match(table, run);
        }

        /// <summary>The distinct words a set of hits fired, in a stable order.</summary>
        public static string[] Distinct(IReadOnlyList<Hit> hits) =>
            hits.Select(h => h.Word.Token).Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal).ToArray();

        /// <summary>
        /// The legend a failure message ends with: every word that fired, once, with the
        /// sentence that says why the race does not need it. A red tick tells you
        /// something is wrong; this tells you what to argue with.
        /// </summary>
        public static string Legend(IEnumerable<Hit> hits)
        {
            var lines = hits
                .Select(h => h.Word)
                .Distinct()
                .OrderBy(w => w.Section, StringComparer.Ordinal)
                .ThenBy(w => w.Token, StringComparer.Ordinal)
                .Select(w => "    " + w.Token.PadRight(12, ' ') + w.Section + " · " + w.Reason);

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>Formats a count with thin spacing so long reports stay scannable.</summary>
        public static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
