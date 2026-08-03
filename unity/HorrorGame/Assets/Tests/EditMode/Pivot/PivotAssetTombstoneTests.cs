#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace HorrorGame.Tests.EditMode.Pivot
{
    /// <summary>
    /// The second instrument: the co-op game as it survives in <em>content</em>.
    /// <para>
    /// <b>Why a second instrument at all.</b> <see cref="PivotTombstoneTests"/> reflects
    /// over assemblies, and an assembly is only code. Scenes, prefabs, materials,
    /// <c>.asset</c> files, imported FBXs and audio clips are not in any assembly, so
    /// none of them is visible to it — and last round the scene was the thing that was
    /// clean while the code was not. This round it is the other way about, which is
    /// exactly why one instrument is not enough: 「씬은 깨끗하다」 was true and the
    /// 전리품 was still on disk, in <c>Resources/</c>, shipped.
    /// </para>
    /// <para>
    /// <b>What it reads.</b> Two things, for two different ways content comes back.
    /// The <em>path</em> of every file under <c>Assets/</c>, because an asset that
    /// exists can be dragged into a scene tomorrow and because a <c>.wav</c> has no
    /// readable inside. And the <em>text</em> of Unity's serialised formats, because
    /// that is where a scene names a prefab, a <c>.asset</c> names a clip and an
    /// <c>.fbx.meta</c> names an imported clip or a material slot.
    /// </para>
    /// <para>
    /// <b>What it cannot read.</b> Binary payloads — mesh data, audio samples, texture
    /// pixels. A 금고 mesh renamed <c>Prop_Cabinet.fbx</c> passes this guard, and no
    /// text instrument can do better; that is a job for eyes on a render.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class PivotAssetTombstoneTests
    {
        /// <summary>
        /// Unity's text-serialised formats, plus the sidecars and manifests this project
        /// authors by hand. Anything not on the list is still checked by <em>path</em>;
        /// only its contents go unread.
        /// </summary>
        private static readonly HashSet<string> ReadableFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".unity", ".prefab", ".asset", ".mat", ".controller", ".overrideController",
            ".anim", ".physicMaterial", ".playable", ".inputactions", ".lighting",
            ".renderTexture", ".preset", ".shader", ".meta", ".json", ".txt",
        };

        /// <summary>
        /// The one directory left unread: this guard's own.
        /// <para>
        /// <c>DeletedVocabulary.txt</c> spells every forbidden word out loud, and the
        /// fixtures beside it quote co-op member names as probe cases. Reading them would
        /// make the guard report itself forever. Test content never reaches a player, so
        /// the exemption costs nothing — but it is a real hole: a 상점 prefab parked under
        /// <c>Assets/Tests</c> is invisible here.
        /// </para>
        /// </summary>
        private const string ExemptFolderName = "Tests";

        /// <summary>
        /// A scene that must be found, so an empty walk cannot pass for a clean one.
        /// </summary>
        private const string SceneThatProvesTheWalkRan = "Scenes/Map_FirstSketch.unity";

        /// <summary>
        /// Identifier-shaped runs, and Hangul runs. Everything else in a YAML file —
        /// GUIDs, floats, base64 — either falls out here or tokenises into nothing the
        /// vocabulary knows.
        /// </summary>
        private static readonly Regex Runs = new Regex(@"[A-Za-z0-9_]+|[가-힣]+", RegexOptions.Compiled);

        private static PivotVocabulary.Table Table =>
            PivotVocabulary.Load(Path.Combine(Application.dataPath, PivotVocabulary.RelativePath));

        /// <summary>One file, and everything about it that names a deleted design.</summary>
        private sealed class Finding
        {
            public string Path { get; }
            public SortedSet<string> ByPath { get; } = new SortedSet<string>(StringComparer.Ordinal);
            public SortedSet<string> ByText { get; } = new SortedSet<string>(StringComparer.Ordinal);

            public Finding(string path) => Path = path;
        }

        /// <summary>
        /// Nothing the project keeps under <c>Assets/</c> may be a piece of the deleted
        /// co-op game — not a model, not a clip, not a row in a <c>Resources</c> catalogue.
        /// <para>
        /// Content in <c>Assets/Resources/</c> deserves particular suspicion and gets it
        /// by accident here: it ships whether or not a scene references it, so "the scene
        /// has 0 전리품" says nothing at all about what the player build contains.
        /// </para>
        /// </summary>
        [Test]
        public void NoAssetTheProjectKeeps_IsAPieceOfTheCoOpGame()
        {
            var table = Table;
            var root = Application.dataPath;

            var findings = new Dictionary<string, Finding>(StringComparer.Ordinal);
            var hits = new List<PivotVocabulary.Hit>();
            var walked = 0;
            var read = 0;
            var sawTheScene = false;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/');

                if (relative.Split('/').Contains(ExemptFolderName, StringComparer.Ordinal))
                {
                    continue;
                }

                walked++;
                sawTheScene |= string.Equals(relative, SceneThatProvesTheWalkRan, StringComparison.Ordinal);

                var pathHits = PivotVocabulary.Match(table, relative);
                if (pathHits.Count > 0)
                {
                    hits.AddRange(pathHits);
                    var finding = Get(findings, relative);
                    foreach (var word in PivotVocabulary.Distinct(pathHits))
                    {
                        finding.ByPath.Add(word);
                    }
                }

                if (!ReadableFormats.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                read++;
                foreach (Match run in Runs.Matches(File.ReadAllText(file)))
                {
                    var textHits = PivotVocabulary.MatchAssetRun(table, run.Value);
                    if (textHits.Count == 0)
                    {
                        continue;
                    }

                    hits.AddRange(textHits);
                    var finding = Get(findings, relative);
                    foreach (var word in PivotVocabulary.Distinct(textHits))
                    {
                        finding.ByText.Add(word + " (" + run.Value + ")");
                    }
                }
            }

            TestContext.WriteLine("Walked " + PivotVocabulary.N(walked) + " files under Assets/, read "
                                  + PivotVocabulary.N(read) + " serialised ones.");

            Assert.That(walked, Is.GreaterThan(0),
                "The walk found no files under " + root + ". An empty walk reports nothing wrong and "
                + "means nothing — that is the failure this fixture exists to prevent.");

            Assert.That(sawTheScene, Is.True,
                "The walk never reached " + SceneThatProvesTheWalkRan + ", so wherever it went, it was "
                + "not the project. Its silence proves nothing.");

            if (findings.Count == 0)
            {
                return;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("DESCENT-PIVOT §3 — " + findings.Count
                              + " file(s) under Assets/ are still part of the co-operative game.");
            report.AppendLine("Walked " + PivotVocabulary.N(walked) + " files, read "
                              + PivotVocabulary.N(read) + " serialised ones.");
            report.AppendLine();

            foreach (var finding in findings.Values.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                report.AppendLine("  " + finding.Path);

                if (finding.ByPath.Count > 0)
                {
                    report.AppendLine("      name : " + string.Join(" ", finding.ByPath));
                }

                if (finding.ByText.Count > 0)
                {
                    report.AppendLine("      body : " + string.Join(", ", finding.ByText.Take(12))
                                      + (finding.ByText.Count > 12
                                          ? " … +" + (finding.ByText.Count - 12) + " more"
                                          : string.Empty));
                }
            }

            report.AppendLine();
            report.AppendLine("왜 이 낱말들이 금지인가 —");
            report.AppendLine(PivotVocabulary.Legend(hits));
            report.AppendLine();
            report.AppendLine("A file listed under `name` is deleted with the asset and its .meta. A file");
            report.AppendLine("listed under `body` is a scene, catalogue or importer that still *points* at");
            report.AppendLine("one — fix the pointer, not the name. Assets/Resources/ ships regardless of");
            report.AppendLine("what any scene references, so an entry there is in the player build today.");

            Assert.Fail(report.ToString());
        }

        private static Finding Get(Dictionary<string, Finding> findings, string path)
        {
            if (!findings.TryGetValue(path, out var finding))
            {
                finding = new Finding(path);
                findings.Add(path, finding);
            }

            return finding;
        }
    }
}
