#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.Tests.EditMode.Pivot
{
    /// <summary>
    /// The third instrument: the co-op game as it survives inside the scenes the game
    /// actually loads, read through Unity's object model rather than off the YAML.
    /// <para>
    /// <b>Why a third instrument, when there are already two.</b> R11's dressing pass put
    /// 526 <c>Clue_Face</c> surfaces into the map and neither existing guard said a word.
    /// <see cref="PivotTombstoneTests"/> could not: a scene is not in any assembly.
    /// <see cref="PivotAssetTombstoneTests"/> could not either, and the reason is worth
    /// writing down because it is not obvious — it reads the scene as text, and the scene
    /// does not contain the string 「Clue」 anywhere. <c>grep -c Clue
    /// Assets/Scenes/Map_FirstSketch_Solo.unity</c> is <b>0</b> over 1,015,765 lines. The
    /// 526 are there as four prefab kinds whose renderers are bound, by GUID, to
    /// <c>Assets/Models/Dressing/Materials/Clue_Face.mat</c>, and a GUID is 32 hex digits
    /// that tokenise into nothing.
    /// </para>
    /// <para>
    /// <b>The two things text cannot do, and this fixture does.</b>
    /// </para>
    /// <list type="number">
    /// <item><b>Follow a GUID.</b> <c>AssetDatabase.GetDependencies</c> is the same edge
    /// walk the player build does when it decides what to include, so an asset it reaches
    /// is an asset that ships. That is how 「씬 이름에 clue 가 없다」 stops being an
    /// argument: the scene does not have to say the word to ship the thing.</item>
    /// <item><b>Read a Korean name.</b> Unity serialises a name outside ASCII as a
    /// backslash-u escape. Every one of the map's <b>212</b> <c>m_Name</c> lines that holds
    /// Hangul is written that way — 「투하구 7북」 is six escapes and a digit on disk — and
    /// the file contains <b>zero</b> literal Hangul bytes, so
    /// <see cref="PivotAssetTombstoneTests"/>'s <c>[가-힣]+</c> run matches nothing at all
    /// in the one file that matters most. Every <c>korean|</c> row in the vocabulary is
    /// dead there. <c>GameObject.name</c> comes back decoded, so walking the loaded scene
    /// sees what the escape hides.
    /// <see cref="TheSceneSweep_ReachedTheMapAndReadItsKoreanNames"/> is the assertion that
    /// this is still true, and it doubles as the anchor.</item>
    /// </list>
    /// <para>
    /// <b>Cost, stated honestly.</b> This opens the map scenes. Each is 45 MB and
    /// 1,015,765 lines holding 7,577 serialised <c>GameObject</c> records and 9,494
    /// <c>PrefabInstance</c> records, and a prefab instance expands into more objects at
    /// load than it stores on disk — so the walk sees rather more than the file does. It
    /// is the slowest EditMode test in the project by a wide margin and
    /// that is the price of the only instrument that can see scene content. The walk is
    /// done once and shared between the tests below, and the elapsed time is printed so
    /// the price stays visible instead of being discovered later.
    /// </para>
    /// <para>
    /// <b>How to run it.</b>
    /// <code>
    /// $U -batchmode -nographics -silent-crashes -projectPath $P \
    ///    -runTests -testPlatform EditMode -testFilter "PivotSceneTombstoneTests" \
    ///    -testResults /tmp/pivot-scene.xml -logFile /tmp/pivot-scene.log
    /// </code>
    /// No <c>-quit</c> — docs/TESTING.md, the runner exits from its own callback and
    /// <c>-quit</c> produces exit 0 with no results, which looks exactly like green.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class PivotSceneTombstoneTests
    {
        /// <summary>
        /// The scene <c>Bootstrap</c> hands the player, named so an empty walk cannot pass
        /// for a clean one.
        /// </summary>
        private const string MapTheGameLoads = "Assets/Scenes/Map_FirstSketch_Solo.unity";

        /// <summary>
        /// A Hangul noun the race needs, used to prove the walk reads decoded names.
        /// <para>
        /// 투하구 is in <c>DeletedVocabulary.txt</c> as a <c>race|</c> row, so it is
        /// vocabulary the race keeps. Its presence proves nothing is wrong; its
        /// <em>readability</em> proves the instrument works. If this ever fails while the
        /// map still has 투하구 in it, the walk is reading escapes and every Korean absence
        /// it reports is worthless.
        /// </para>
        /// </summary>
        private const string KoreanNounThatProvesTheNamesAreDecoded = "투하구";

        /// <summary>
        /// This guard's own folder, left unwalked for the same reason
        /// <see cref="PivotAssetTombstoneTests"/> leaves it: the vocabulary beside this file
        /// spells every forbidden word out loud. It is a real hole — a 상점 prefab parked
        /// under <c>Assets/Tests</c> is invisible to all three instruments.
        /// </summary>
        private const string ExemptFolder = "Assets/Tests/";

        // ====================================================================
        // What the walk saw
        // ====================================================================

        /// <summary>One asset the closure reached that names a deleted design.</summary>
        private sealed class ReachedAsset
        {
            public string Path { get; }
            public string Guid { get; }
            public SortedSet<string> Words { get; } = new SortedSet<string>(StringComparer.Ordinal);
            public SortedSet<string> PulledInBy { get; } = new SortedSet<string>(StringComparer.Ordinal);

            public ReachedAsset(string path, string guid)
            {
                Path = path;
                Guid = guid;
            }
        }

        /// <summary>
        /// One live surface: a material asset a scene's renderers are actually bound to,
        /// counted, and split by the prefab each binding came from.
        /// <para>
        /// The count is the whole point. 「1 file」 is a fact nobody acts on; 「436 renderer
        /// slots across four sign kinds」 is the dressing pass's own §03 line read back out
        /// of the scene, and it is the number that makes the defect undeniable.
        /// </para>
        /// </summary>
        private sealed class Surface
        {
            public string Material { get; }
            public string Scene { get; }
            public SortedSet<string> Words { get; } = new SortedSet<string>(StringComparer.Ordinal);
            public Dictionary<string, int> BySource { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
            public int Total { get; set; }

            public Surface(string material, string scene)
            {
                Material = material;
                Scene = scene;
            }
        }

        /// <summary>One named object, with the hierarchy path that finds it again.</summary>
        private readonly struct NamedObject
        {
            public string Container { get; }
            public string HierarchyPath { get; }
            public string Words { get; }

            public NamedObject(string container, string hierarchyPath, string words)
            {
                Container = container;
                HierarchyPath = hierarchyPath;
                Words = words;
            }
        }

        /// <summary>Everything one walk of the project's content produced.</summary>
        private sealed class Walk
        {
            public List<string> Scenes { get; } = new List<string>();
            public List<string> Prefabs { get; } = new List<string>();
            public List<string> Unreadable { get; } = new List<string>();
            public Dictionary<string, ReachedAsset> Reached { get; } =
                new Dictionary<string, ReachedAsset>(StringComparer.Ordinal);
            public Dictionary<string, Surface> Surfaces { get; } =
                new Dictionary<string, Surface>(StringComparer.Ordinal);
            public List<NamedObject> Objects { get; } = new List<NamedObject>();
            public List<NamedObject> Components { get; } = new List<NamedObject>();
            public List<PivotVocabulary.Hit> Hits { get; } = new List<PivotVocabulary.Hit>();
            public int ObjectCount { get; set; }
            public int RendererSlotCount { get; set; }
            public int DependencyEdges { get; set; }
            public bool SawTheMap { get; set; }
            public bool ReadAKoreanName { get; set; }
            public string KoreanNameSeen { get; set; } = string.Empty;
            public long Milliseconds { get; set; }
        }

        private static Walk? _walk;
        private static string? _walkError;
        private static SceneSetup[]? _setupBeforeTheWalk;

        /// <summary>
        /// Remembers what the editor had open, because this fixture opens scenes over it,
        /// and throws away any walk left over from an earlier run.
        /// <para>
        /// A guard that leaves the editor on a different scene than it found is a guard
        /// that gets blamed for the next person's lost work and then gets deleted.
        /// </para>
        /// <para>
        /// The cache is cleared here rather than merely initialised once, and the reason is
        /// this project's own recurring bug in miniature: statics outlive a test run inside
        /// one editor domain, so somebody could go red, delete the offending prefab, press
        /// Run again without a recompile, and read a stale red — or, far worse, a stale
        /// green over content that had just changed underneath it. Once per fixture run is
        /// the correct scope, and the walk is expensive enough that per-test would not be.
        /// </para>
        /// </summary>
        [OneTimeSetUp]
        public void RememberWhatWasOpen()
        {
            _walk = null;
            _walkError = null;
            _setupBeforeTheWalk = EditorSceneManager.GetSceneManagerSetup();
        }

        /// <summary>Puts the editor back the way it was found.</summary>
        [OneTimeTearDown]
        public void PutTheEditorBack()
        {
            if (_setupBeforeTheWalk != null && _setupBeforeTheWalk.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(_setupBeforeTheWalk);
                return;
            }

            // Nothing was open — batch mode's usual state. Leaving the 46 MB map loaded
            // would make every later fixture in the same run pay for it.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // ====================================================================
        // The walk
        // ====================================================================

        private static PivotVocabulary.Table Table =>
            PivotVocabulary.Load(Path.Combine(Application.dataPath, PivotVocabulary.RelativePath));

        /// <summary>
        /// The shared walk, done once. Failures are captured rather than thrown so one
        /// broken scene produces one honest red test instead of tearing the run down.
        /// </summary>
        private static Walk TheWalk()
        {
            if (_walk != null)
            {
                return _walk;
            }

            if (_walkError != null)
            {
                Assert.Fail(_walkError);
            }

            try
            {
                _walk = WalkTheContent();
            }
            catch (Exception ex)
            {
                _walkError = "The scene walk itself threw: " + ex.GetType().Name + " — " + ex.Message
                             + ". Nothing was inspected, so no absence below means anything.";
                Assert.Fail(_walkError);
            }

            return _walk!;
        }

        /// <summary>
        /// Finds the content by GUID, follows every dependency edge, then opens each scene
        /// and reads the objects themselves.
        /// <para>
        /// <c>AssetDatabase.FindAssets</c> rather than a directory listing, because a GUID
        /// is what the scene stores and a path is only its current spelling. A file moved
        /// tomorrow keeps its GUID and stays in scope with no edit here.
        /// </para>
        /// </summary>
        private static Walk WalkTheContent()
        {
            var table = Table;
            var walk = new Walk();
            var clock = Stopwatch.StartNew();

            walk.Scenes.AddRange(OursByGuid("t:SceneAsset"));
            walk.Prefabs.AddRange(OursByGuid("t:Prefab"));

            foreach (var path in walk.Scenes.Concat(walk.Prefabs))
            {
                FollowDependencies(table, walk, path);
            }

            foreach (var prefab in walk.Prefabs)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                if (root == null)
                {
                    walk.Unreadable.Add(prefab + " (LoadAssetAtPath returned null)");
                    continue;
                }

                ReadHierarchy(table, walk, prefab, new[] { root });
            }

            foreach (var scenePath in walk.Scenes)
            {
                // Assigned in the try; `default` only so definite assignment is obvious to
                // a reader as well as to the compiler.
                var scene = default(Scene);
                try
                {
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                }
                catch (Exception ex)
                {
                    walk.Unreadable.Add(scenePath + " (" + ex.GetType().Name + ": " + ex.Message + ")");
                    continue;
                }

                if (!scene.IsValid())
                {
                    walk.Unreadable.Add(scenePath + " (opened but not valid)");
                    continue;
                }

                walk.SawTheMap |= string.Equals(scenePath, MapTheGameLoads, StringComparison.Ordinal);
                ReadHierarchy(table, walk, scenePath, scene.GetRootGameObjects());
            }

            clock.Stop();
            walk.Milliseconds = clock.ElapsedMilliseconds;
            return walk;
        }

        /// <summary>
        /// Content this project owns, addressed the way Unity addresses it.
        /// <para>
        /// Package content is excluded because it is not ours to delete from — the same
        /// judgement <see cref="PivotTombstoneTests"/> makes about package assemblies, for
        /// the same reason: a report nobody can act on is a report that gets muted.
        /// </para>
        /// </summary>
        private static IEnumerable<string> OursByGuid(string filter) =>
            AssetDatabase.FindAssets(filter)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal))
                .Where(p => !p.StartsWith(ExemptFolder, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal);

        /// <summary>
        /// Every asset reachable from one scene or prefab, matched by its own path.
        /// <para>
        /// Recursive on purpose. The scene points at <c>Dress_WallSign.fbx</c>, the fbx's
        /// renderer slot points at <c>Clue_Face.mat</c>, and only the transitive answer
        /// names the material. One hop would have reported four innocent-looking signs.
        /// </para>
        /// </summary>
        private static void FollowDependencies(PivotVocabulary.Table table, Walk walk, string path)
        {
            foreach (var dependency in AssetDatabase.GetDependencies(path, true))
            {
                if (string.Equals(dependency, path, StringComparison.Ordinal)
                    || !dependency.StartsWith("Assets/", StringComparison.Ordinal)
                    || dependency.StartsWith(ExemptFolder, StringComparison.Ordinal))
                {
                    continue;
                }

                walk.DependencyEdges++;

                var hits = PivotVocabulary.Match(table, dependency);
                if (hits.Count == 0)
                {
                    continue;
                }

                walk.Hits.AddRange(hits);

                if (!walk.Reached.TryGetValue(dependency, out var reached))
                {
                    reached = new ReachedAsset(dependency, AssetDatabase.AssetPathToGUID(dependency));
                    walk.Reached.Add(dependency, reached);
                }

                foreach (var word in PivotVocabulary.Distinct(hits))
                {
                    reached.Words.Add(word);
                }

                reached.PulledInBy.Add(path);
            }
        }

        /// <summary>
        /// Reads one hierarchy as C# sees it: decoded names, real component types, and the
        /// material each renderer slot is actually bound to.
        /// </summary>
        private static void ReadHierarchy(PivotVocabulary.Table table, Walk walk, string container,
            IReadOnlyList<GameObject> roots)
        {
            foreach (var root in roots)
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = transform.gameObject;
                    walk.ObjectCount++;

                    if (!walk.ReadAKoreanName
                        && go.name.IndexOf(KoreanNounThatProvesTheNamesAreDecoded, StringComparison.Ordinal) >= 0)
                    {
                        walk.ReadAKoreanName = true;
                        walk.KoreanNameSeen = go.name;
                    }

                    Record(table, walk, walk.Objects, container, transform, go.name);

                    foreach (var component in go.GetComponents<Component>())
                    {
                        if (component == null)
                        {
                            // A null entry is a component whose MonoScript is gone. Not a
                            // vocabulary matter, so it is not recorded as an offender — but
                            // see the blind-spot register: a ScriptableObject in the same
                            // state ships from Resources/ and no instrument here sees it.
                            continue;
                        }

                        var type = component.GetType();
                        Record(table, walk, walk.Components, container, transform,
                            type.FullName ?? type.Name);

                        if (component is Renderer renderer)
                        {
                            ReadRendererSlots(table, walk, container, renderer);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The materials one renderer is bound to, counted per source prefab.
        /// <para>
        /// <c>sharedMaterials</c> and not <c>materials</c>: the second one instantiates a
        /// copy per call, which in a scene this size would leak thousands of materials into
        /// the editor for the duration of the run, and would dirty the scene the fixture
        /// promised only to read.
        /// </para>
        /// </summary>
        private static void ReadRendererSlots(PivotVocabulary.Table table, Walk walk, string container,
            Renderer renderer)
        {
            var materials = renderer.sharedMaterials;

            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null)
                {
                    continue;
                }

                walk.RendererSlotCount++;

                var materialPath = AssetDatabase.GetAssetPath(material);
                var subject = string.IsNullOrEmpty(materialPath) ? material.name : materialPath;

                var hits = PivotVocabulary.Match(table, subject);
                if (hits.Count == 0)
                {
                    continue;
                }

                walk.Hits.AddRange(hits);

                var key = container + "|" + subject;
                if (!walk.Surfaces.TryGetValue(key, out var surface))
                {
                    surface = new Surface(subject, container);
                    walk.Surfaces.Add(key, surface);
                }

                foreach (var word in PivotVocabulary.Distinct(hits))
                {
                    surface.Words.Add(word);
                }

                surface.Total++;

                var source = SourceOf(renderer);
                surface.BySource[source] = surface.BySource.TryGetValue(source, out var n) ? n + 1 : 1;
            }
        }

        /// <summary>
        /// The asset a scene object came out of, so the report names the piece to delete
        /// rather than 436 anonymous transforms.
        /// </summary>
        private static string SourceOf(Renderer renderer)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
            if (source == null)
            {
                // Authored directly rather than instantiated — or this renderer is inside
                // the prefab asset itself, where there is no source to correspond to.
                return "(not a prefab instance)";
            }

            var path = AssetDatabase.GetAssetPath(source);
            return string.IsNullOrEmpty(path) ? "(prefab source has no asset path)" : path;
        }

        /// <summary>
        /// Matches one name and, only if it fires, works out where the object lives.
        /// <para>
        /// The <see cref="Transform"/> rather than a ready-made path, because building one
        /// walks to the root and allocates, and this runs tens of thousands of times per
        /// scene — once per object and once per component on it — while fewer than one call
        /// in a hundred produces a row. Cheap where it is hot.
        /// </para>
        /// </summary>
        private static void Record(PivotVocabulary.Table table, Walk walk, List<NamedObject> into,
            string container, Transform transform, string subject)
        {
            var hits = PivotVocabulary.Match(table, subject);
            if (hits.Count == 0)
            {
                return;
            }

            walk.Hits.AddRange(hits);
            into.Add(new NamedObject(container, HierarchyPath(transform) + "  [" + subject + "]",
                string.Join(" ", PivotVocabulary.Distinct(hits))));
        }

        private static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            for (var t = transform; t != null; t = t.parent)
            {
                parts.Add(t.name);
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        // ====================================================================
        // The tests
        // ====================================================================

        /// <summary>
        /// Nothing the game loads may reach an asset of the deleted co-op game by any
        /// number of GUID hops.
        /// <para>
        /// This is the test that would have caught R11 twelve minutes after the dressing
        /// pass ran, without anybody reading a generator log. It answers the question the
        /// build answers — 「이 씬을 빌드하면 무엇이 따라 들어가는가」 — instead of the
        /// question a text search answers, which is only 「이 파일에 그 낱말이 있는가」.
        /// </para>
        /// </summary>
        [Test]
        public void NothingTheGameLoads_ReachesAnAssetOfTheCoOpGame()
        {
            var walk = TheWalk();

            TestContext.WriteLine("Followed " + PivotVocabulary.N(walk.DependencyEdges)
                                  + " dependency edges out of " + walk.Scenes.Count + " scene(s) and "
                                  + walk.Prefabs.Count + " prefab(s).");

            if (walk.Reached.Count == 0)
            {
                Assert.Pass("0 co-op assets reachable from " + walk.Scenes.Count + " scene(s) and "
                            + walk.Prefabs.Count + " prefab(s).");
            }

            var report = new StringBuilder();
            report.AppendLine("DESCENT-PIVOT §3 — " + walk.Reached.Count
                              + " asset(s) of the co-operative game are reachable from what this game loads.");
            report.AppendLine("An asset AssetDatabase.GetDependencies reaches is an asset the player build ships,");
            report.AppendLine("whether or not any file spells its name.");
            report.AppendLine();

            foreach (var reached in walk.Reached.Values.OrderBy(r => r.Path, StringComparer.Ordinal))
            {
                report.AppendLine("  " + reached.Path);
                report.AppendLine("      guid   " + reached.Guid);
                report.AppendLine("      words  " + string.Join(" ", reached.Words));
                report.AppendLine("      pulled in by " + reached.PulledInBy.Count + ":");

                foreach (var consumer in reached.PulledInBy)
                {
                    report.AppendLine("        " + consumer);
                }
            }

            report.AppendLine();
            report.AppendLine("왜 이 낱말들이 금지인가 —");
            report.AppendLine(PivotVocabulary.Legend(walk.Hits));
            report.AppendLine();
            report.AppendLine("Delete the asset AND the pass that places it. Deleting only the asset leaves the");
            report.AppendLine("generator that put it there, and the next regeneration puts it back — which is");
            report.AppendLine("exactly what happened between R10 and R11.");

            Assert.Fail(report.ToString());
        }

        /// <summary>
        /// No object in a loaded scene, and no renderer slot in one, may be a piece of the
        /// deleted co-op game.
        /// <para>
        /// The counts are the deliverable. R11's log said 「526 Clue_Face surfaces placed」
        /// and it was the only place in the project that number existed; this reads it back
        /// out of the scene, per sign kind, so the number survives without the log.
        /// </para>
        /// </summary>
        [Test]
        public void NoObjectInASceneTheGameLoads_IsAPieceOfTheCoOpGame()
        {
            var walk = TheWalk();

            TestContext.WriteLine("Read " + PivotVocabulary.N(walk.ObjectCount) + " objects and "
                                  + PivotVocabulary.N(walk.RendererSlotCount) + " renderer slots in "
                                  + walk.Milliseconds + " ms.");

            var findings = walk.Surfaces.Count + walk.Objects.Count + walk.Components.Count;
            if (findings == 0)
            {
                Assert.Pass("0 co-op objects, components or bound materials across "
                            + walk.Scenes.Count + " scene(s) and " + walk.Prefabs.Count + " prefab(s).");
            }

            var report = new StringBuilder();
            report.AppendLine("DESCENT-PIVOT §3 — " + findings
                              + " finding(s) inside the content this game loads.");
            report.AppendLine("Read " + PivotVocabulary.N(walk.ObjectCount) + " objects and "
                              + PivotVocabulary.N(walk.RendererSlotCount) + " renderer slots.");
            report.AppendLine();

            if (walk.Surfaces.Count > 0)
            {
                report.AppendLine("BOUND MATERIALS — surfaces the game will actually draw");

                foreach (var surface in walk.Surfaces.Values
                             .OrderByDescending(s => s.Total)
                             .ThenBy(s => s.Material, StringComparer.Ordinal))
                {
                    report.AppendLine("  " + surface.Material + "   " + string.Join(" ", surface.Words));
                    report.AppendLine("      " + surface.Total + " renderer slot(s) in " + surface.Scene);

                    foreach (var pair in surface.BySource.OrderByDescending(p => p.Value)
                                 .ThenBy(p => p.Key, StringComparer.Ordinal))
                    {
                        report.AppendLine("        ×" + pair.Value.ToString().PadRight(5, ' ') + pair.Key);
                    }
                }

                report.AppendLine();
            }

            Section(report, "OBJECT NAMES — decoded, so \\uXXXX cannot hide a Korean one", walk.Objects);
            Section(report, "COMPONENTS", walk.Components);

            report.AppendLine("왜 이 낱말들이 금지인가 —");
            report.AppendLine(PivotVocabulary.Legend(walk.Hits));
            report.AppendLine();
            report.AppendLine("A bound material is not decoration: it is a surface §13 was going to stamp a");
            report.AppendLine("glyph onto. The race has no glyph to stamp, so the slot, the material and the");
            report.AppendLine("kit piece that carries it all go, and so does the scatter rule that places them.");

            Assert.Fail(report.ToString());
        }

        /// <summary>
        /// One section of the report: counted by word first, then a handful of examples.
        /// <para>
        /// A dressing pass places the same piece hundreds of times, so a flat list is 246
        /// lines that say one thing. The count is what argues — 「이 씬에 궤짝이 246개
        /// 있다」 — and six hierarchy paths are enough to go and look at one.
        /// </para>
        /// </summary>
        private const int ExamplesPerWord = 6;

        private static void Section(StringBuilder report, string title, IReadOnlyList<NamedObject> rows)
        {
            if (rows.Count == 0)
            {
                return;
            }

            report.AppendLine(title + "  (" + rows.Count + ")");

            foreach (var container in rows.OrderBy(r => r.Container, StringComparer.Ordinal)
                         .GroupBy(r => r.Container, StringComparer.Ordinal))
            {
                report.AppendLine("  " + container.Key);

                foreach (var byWord in container.GroupBy(r => r.Words, StringComparer.Ordinal)
                             .OrderByDescending(g => g.Count())
                             .ThenBy(g => g.Key, StringComparer.Ordinal))
                {
                    report.AppendLine("      ×" + byWord.Count().ToString().PadRight(6, ' ') + byWord.Key);

                    foreach (var row in byWord.OrderBy(r => r.HierarchyPath, StringComparer.Ordinal)
                                 .Take(ExamplesPerWord))
                    {
                        report.AppendLine("            " + row.HierarchyPath);
                    }

                    var extra = byWord.Count() - ExamplesPerWord;
                    if (extra > 0)
                    {
                        report.AppendLine("            … +" + extra + " more");
                    }
                }
            }

            report.AppendLine();
        }

        /// <summary>
        /// The walk reached the map the game loads, opened it, and read its Korean names as
        /// text rather than as escapes.
        /// <para>
        /// This is the half that cannot be satisfied by finding nothing, and it is where
        /// the instrument proves itself rather than the content. Three things have to hold
        /// at once: the map was among the scenes found by GUID; it opened; and a
        /// 「투하구」 came back out of it spelled in Hangul. Lose the third and every Korean
        /// absence this fixture reports is an artefact of the encoding, not a fact about
        /// the game.
        /// </para>
        /// </summary>
        [Test]
        public void TheSceneSweep_ReachedTheMapAndReadItsKoreanNames()
        {
            var walk = TheWalk();

            TestContext.WriteLine("scenes  : " + string.Join(", ", walk.Scenes));
            TestContext.WriteLine("prefabs : " + string.Join(", ", walk.Prefabs));
            TestContext.WriteLine("objects " + PivotVocabulary.N(walk.ObjectCount)
                                  + ", renderer slots " + PivotVocabulary.N(walk.RendererSlotCount)
                                  + ", dependency edges " + PivotVocabulary.N(walk.DependencyEdges)
                                  + ", " + walk.Milliseconds + " ms");

            Assert.That(walk.Unreadable, Is.Empty,
                "These pieces of content could not be read at all, so nothing this fixture says about "
                + "them means anything: " + string.Join("; ", walk.Unreadable));

            Assert.That(walk.Scenes, Is.Not.Empty,
                "AssetDatabase found no scenes under Assets/. Nothing was walked.");

            Assert.That(walk.Prefabs, Is.Not.Empty,
                "AssetDatabase found no prefabs under Assets/. Nothing was walked.");

            Assert.That(walk.SawTheMap, Is.True,
                "The walk never opened " + MapTheGameLoads + " — the scene the game actually loads. "
                + "Wherever it went, it was not the game, and its silence proves nothing.");

            Assert.That(walk.ObjectCount, Is.GreaterThan(0),
                "The scenes opened but held no objects. An empty hierarchy reports nothing wrong and "
                + "means nothing.");

            Assert.That(walk.RendererSlotCount, Is.GreaterThan(0),
                "Not one renderer slot was read, so the material half of this instrument — the half that "
                + "counts Clue_Face — was never exercised.");

            Assert.That(walk.ReadAKoreanName, Is.True,
                "The walk read " + PivotVocabulary.N(walk.ObjectCount) + " object names and not one of them "
                + "contained 「" + KoreanNounThatProvesTheNamesAreDecoded + "」. The map has 212 names Unity "
                + "escaped as \\uXXXX and zero literal Hangul bytes on disk, so either the map lost its "
                + "투하구 — the race is over if so — or this fixture is reading escapes and cannot see a "
                + "Korean name any better than the text guard it was written to replace.");

            TestContext.WriteLine("decoded name proof : " + walk.KoreanNameSeen);
        }

        // ====================================================================
        // The blind-spot register
        // ====================================================================

        /// <summary>
        /// One thing no instrument in this project can currently see.
        /// <para>
        /// Three fields, and the third is the one that matters. A blind spot without a
        /// 「대신 여기를 보라」 is a confession; with one it is an instruction.
        /// </para>
        /// </summary>
        private readonly struct BlindSpot
        {
            public string What { get; }
            public string WhyInvisible { get; }
            public string LookHereInstead { get; }

            public BlindSpot(string what, string whyInvisible, string lookHereInstead)
            {
                What = what;
                WhyInvisible = whyInvisible;
                LookHereInstead = lookHereInstead;
            }
        }

        /// <summary>
        /// What NEITHER the assembly sweep NOR the content sweeps can see, as of R11.
        /// <para>
        /// This exists because of the exact failure that produced this round: two rounds
        /// reported 「the co-op game is gone」 off a DLL sweep, and the DLL sweep was
        /// structurally incapable of seeing the 526 scene surfaces. The lesson generalises
        /// — every instrument has a shape, and the shape is where the next survivor will
        /// be. Writing the shapes down is cheaper than discovering them a third time.
        /// </para>
        /// </summary>
        private static readonly BlindSpot[] Register =
        {
            new BlindSpot(
                "The generator, as opposed to what it generated.",
                "The 526 Clue_Face surfaces were placed by HorrorGame.EditorTools.Dressing and designed in "
                + "tools/blender/gen_dressing.py. The first is an Editor assembly, so it is absent from "
                + "CompilationPipeline.GetAssemblies(AssembliesType.Player) by construction and "
                + "PivotTombstoneTests will never look at it. The second is outside Assets/ entirely, so no "
                + "Unity-side instrument opens it. The content guards see only the output, and only after "
                + "somebody regenerates — which is why R10 measured clean and R11 did not.",
                "Read Assets/Scripts/Editor/Dressing/*.cs and tools/blender/gen_dressing.py by hand. "
                + "DressingManifest.clue_faces and ScatterSession's §03 line are still there today."),

            new BlindSpot(
                "Code the editor does not compile.",
                "Reflection can only see the loaded domain. Anything inside #if !UNITY_EDITOR, an unmet "
                + "defineConstraint, or a versionDefine that is off today exists in the source and in some "
                + "player builds but in no Assembly this fixture can reach.",
                "grep the sources for the symbol, and compare the type count of a player build's DLL "
                + "against the editor's."),

            new BlindSpot(
                "Everything outside Assets/.",
                "ProjectSettings/ holds the input actions, the tag and layer names, and preloadedAssets — "
                + "an asset listed there ships with no scene and no prefab pointing at it, and the GUID "
                + "closure above starts from scenes and prefabs, so it would never be reached. It is [] "
                + "today. Packages/ can hold an embedded package whose code compiles into a player "
                + "assembly, and IsOurs() filters those out by source path. StreamingAssets is copied "
                + "verbatim in any format at all.",
                "ProjectSettings/ProjectSettings.asset (preloadedAssets), Packages/manifest.json, "
                + "Assets/StreamingAssets if one ever appears."),

            new BlindSpot(
                "Binary payloads.",
                "Mesh vertices, texture pixels and audio samples are not text and are not names. A 금고 "
                + "modelled and exported as Prop_Cabinet.fbx passes all three instruments, and so does a "
                + "상점 UI baked into a sprite atlas.",
                "Eyes on a render, at native brightness. No text instrument can do better."),

            new BlindSpot(
                "String content, as opposed to identifiers.",
                "All three instruments match names — type names, member names, asset paths, object names. "
                + "None reads the value of a const string, a serialised text field, or a localisation "
                + "table. §13's glyph table could live entirely in string literals and be invisible.",
                "The Constant table of the shipped DLLs, and any .json/.csv the UI reads."),

            new BlindSpot(
                "Addressables and AssetBundles.",
                "There are none in Packages/manifest.json today. If one arrives, its content ships because "
                + "a group asset lists it, not because a scene references it, and AssetDatabase."
                + "GetDependencies over scenes and prefabs would not reach it.",
                "The Addressables group assets, and the build report's list of included files."),

            new BlindSpot(
                "Which scenes actually ship.",
                "Nothing here reads EditorBuildSettings.scenes. A scene could be dropped from the build and "
                + "still pass every test above, or added and shipped without anybody reviewing it.",
                "EditorBuildSettings.scenes, against Assets/Scenes/."),

            new BlindSpot(
                "Behaviour, as opposed to vocabulary.",
                "A MatchDirector that awards a currency called 「기록」 and spends it on 「장비」 names "
                + "nothing on any list and is the co-op game. Renaming is the cheapest way past every "
                + "instrument in this project.",
                "The playthrough and escape sweeps, and playing it."),
        };

        /// <summary>
        /// Every blind spot on the register says where to look instead.
        /// <para>
        /// This test does almost nothing and is worth keeping anyway: it prints the register
        /// into every run's output, so the limits of the guard travel with its results
        /// instead of sitting in a comment nobody opens, and it refuses a row whose third
        /// field is missing — because 「우리는 이것을 못 본다」 without 「대신 여기를 보라」
        /// is how a known hole becomes an unknown one.
        /// </para>
        /// </summary>
        [Test]
        public void EveryBlindSpot_SaysWhereToLookInstead()
        {
            var report = new StringBuilder();
            report.AppendLine("이 세 계측기가 못 보는 것 — " + Register.Length + " 항목");

            foreach (var spot in Register)
            {
                report.AppendLine();
                report.AppendLine("  " + spot.What);
                report.AppendLine("      왜 안 보이나 : " + spot.WhyInvisible);
                report.AppendLine("      대신 여기를  : " + spot.LookHereInstead);
            }

            TestContext.WriteLine(report.ToString());

            Assert.That(Register, Is.Not.Empty,
                "The blind-spot register is empty, which would mean these instruments see everything. "
                + "They do not, and a guard that claims to be complete is the most dangerous kind.");

            var thin = Register
                .Where(s => s.What.Length < 12 || s.WhyInvisible.Length < 40 || s.LookHereInstead.Length < 12)
                .Select(s => s.What)
                .ToArray();

            Assert.That(thin, Is.Empty,
                "These register rows do not carry all three fields: " + string.Join(" / ", thin)
                + ". 못 보는 것을 적어 두는 값어치는 「대신 어디를 보라」에 있다.");
        }
    }
}
