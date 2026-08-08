#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HorrorGame.Audio;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using HorrorGame.Gameplay.Startle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Startles
{
    /// <summary>
    /// §16's 깜짝, driven through the real path: the generated building, the markers
    /// <c>MapSceneBuilder.BuildStartles</c> seeded into it, the self-installed
    /// <c>StartleDirector</c>, and a player rig standing where a runner would stand.
    /// <para>
    /// <c>StartlePacing</c>'s gates could be unit-tested in isolation, and mostly are
    /// not: the defect this repository keeps finding is the thing that was generated
    /// into no scene at all, so the headline test loads the artefact and swings a real
    /// leaf. It fails, correctly and loudly, on a map written before the startle pass —
    /// that is a regeneration that has not happened, not a bug in the test (the exact
    /// stance <c>GunTests</c> documents).
    /// </para>
    /// <para>
    /// Lives in the predefined test assembly like <c>Tests/PlayMode/Race</c>, and
    /// compiles out of a player build on <c>UNITY_INCLUDE_TESTS</c>.
    /// </para>
    /// </summary>
    public sealed class StartleTests
    {
        /// <summary>The generated eight-storey building, as the solo playtest loads it.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>Seed the solo scene's own match is begun with when it has not begun one.</summary>
        private const int Seed = 20260804;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator PutTheWorldBack()
        {
            for (var i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();

            // The scene unload is not housekeeping — GunTests records the three audio
            // tests an abandoned eight-storey building once failed.
            var solo = SceneManager.GetSceneByName(SoloScene);
            if (solo.IsValid() && solo.isLoaded)
            {
                var empty = SceneManager.CreateScene("StartleTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(solo);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // 1. The real path: a cabinet fires once, and the cooldown holds the line.
        // ------------------------------------------------------------------

        /// <summary>
        /// Walking into a cabinet marker's trigger swings the leaf open and it stays
        /// open; a second marker inside the cooldown refuses without being consumed.
        /// </summary>
        [UnityTest]
        public IEnumerator The_cabinet_springs_once_and_the_cooldown_refuses_the_next()
        {
            yield return LoadMap();

            var director = StartleDirector.Attach();
            Assert.That(director, Is.Not.Null,
                "no '" + StartleDirector.GroupName + "' group under " + StartleDirector.MarkerRootName
                + ". Either MapSceneBuilder.BuildStartles did not run or this scene was written "
                + "before it existed — regenerate the map (지도 생성) and re-open the solo scene. "
                + "The 깜짝 system is wired end to end and there is nothing in the building to fire.");
            Assert.That(director!.SpotTotal, Is.GreaterThanOrEqualTo(2),
                "the map carries " + director.SpotTotal + " startle markers; the placement quota is "
                + "two per storey, so even a building of one storey should offer two.");

            // The shallowest cabinet, so the teleport stays near the rig's own storey
            // and no out-of-bounds rule mistakes it for a fall. The kind rotation
            // guarantees cabinets exist: (storey + slot) mod 4 deals one to B1 and B5
            // on §01's tower unless clearance rejected the whole floor.
            Transform? cabinet = null;
            foreach (Transform child in director.transform)
            {
                if (!child.name.StartsWith(StartleDirector.CabinetPrefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (cabinet == null || child.position.y > cabinet.position.y)
                {
                    cabinet = child;
                }
            }

            Assert.That(cabinet, Is.Not.Null,
                "the map has startle markers but no cabinet. The kind rotation in "
                + "MapSceneBuilder.BuildStartles deals every kind into every 4-storey band, so a map "
                + "with none has had every cabinet slot rejected by clearance — regenerate and read "
                + "the [SceneGen] 깜짝 log line.");

            var hinge = cabinet!.Find("Hinge");
            Assert.That(hinge, Is.Not.Null, cabinet.name + " has no Hinge child; the editor half "
                + "did not build the leaf assembly.");
            Assert.That(hinge!.childCount, Is.GreaterThan(0),
                cabinet.name + "/Hinge carries no leaf. Startle_CabinetLeaf.fbx failed to instantiate — "
                + "the kit FBX is missing or Place() refused it.");

            var before = hinge.localRotation;

            var rig = FindRig();
            Teleport(rig, cabinet.position);

            // The grace gate is real time and the test is not: it is skipped by
            // advancing the pacing clock its own constant, which is the reason
            // StartlePacing is a pure class.
            director.Pacing.Advance(StartlePacing.GraceSeconds);

            var waited = 0;
            while (director.FiredTotal < 1 && waited++ < 120)
            {
                yield return null;
            }

            Assert.That(director.FiredTotal, Is.EqualTo(1),
                "standing on " + cabinet.name + " for " + waited + " frames fired nothing. The "
                + "trigger radius is " + StartleDirector.TriggerMetres.ToString("0.00") + " m and the "
                + "rig is at the marker itself.");

            yield return new WaitForSeconds(StartleDirector.CabinetSwingSeconds + 0.2f);

            var swung = Quaternion.Angle(before, hinge.localRotation);
            Assert.That(swung, Is.GreaterThanOrEqualTo(StartleDirector.CabinetOpenMinDegrees - 0.5f),
                "the leaf moved " + swung.ToString("0.0") + "°; a sprung cabinet opens "
                + StartleDirector.CabinetOpenMinDegrees + "~" + StartleDirector.CabinetOpenMaxDegrees
                + "°.");

            // Cooldown: any second marker, walked into immediately, must refuse — and
            // refuse without consuming, so it can still fire later in the match.
            Transform? second = null;
            foreach (Transform child in director.transform)
            {
                if (child == cabinet || child.name == StartleDirector.FigureTemplateName
                    || child.name == StartleDirector.SkittererTemplateName)
                {
                    continue;
                }

                if (child.name.StartsWith("Startle_", System.StringComparison.Ordinal))
                {
                    second = child;
                    break;
                }
            }

            Assert.That(second, Is.Not.Null, "the map holds only one startle marker in total.");

            Teleport(rig, second!.position);
            for (var frame = 0; frame < 15; frame++)
            {
                yield return null;
            }

            Assert.That(director.FiredTotal, Is.EqualTo(1),
                "a second 깜짝 fired " + director.Pacing.Elapsed.ToString("0.0") + " s into the "
                + "match, inside the " + StartlePacing.CooldownSeconds + " s cooldown. About one per "
                + "storey is the dose; two in one corridor walk is a funhouse.");
            Assert.That(director.Pacing.CooldownRemaining, Is.GreaterThan(0f),
                "FiredTotal held but the cooldown is not running, so the refusal was luck.");
            Assert.That(director.Pacing.CanFire(false, out var why), Is.False,
                "the pacing says a startle may fire during another startle's cooldown.");
            Assert.That(why, Is.EqualTo("cooldown"), "refused, but for the wrong reason: " + why);

            // And the leaf stayed open: a sprung thing happens once.
            Assert.That(Quaternion.Angle(before, hinge.localRotation),
                Is.GreaterThanOrEqualTo(StartleDirector.CabinetOpenMinDegrees - 0.5f),
                "the leaf closed itself after the swing.");
        }

        // ------------------------------------------------------------------
        // 2. The moving startles stage inside the marker's keep-out guarantee.
        // ------------------------------------------------------------------

        /// <summary>
        /// A staged crossing and a staged figure must measure, in plan, within
        /// <c>MarkerClearanceMetres − TriggerMetres</c> of the marker that fired them.
        /// The generator's 8.1 m keep-out is a promise about the MARKER's
        /// surroundings; the first build staged relative to the rig — which can stand
        /// 1.55 m from the marker when firing — and measured up to 8.73 m (skitterer)
        /// and 13.55 m (glimpse) from the marker, inside gun alcoves and door swings
        /// the clearance exists to protect. The runtime now clamps every staged point
        /// to <c>StageReachMetres</c> of the marker; this test measures the ARTEFACT —
        /// the clone's actual positions, frame by frame — and the clamp's
        /// <c>StageMarginMetres</c> is the headroom between what it clamps and what
        /// this test asserts.
        /// </summary>
        [UnityTest]
        public IEnumerator The_moving_startles_stage_inside_the_marker_guarantee()
        {
            yield return LoadMap();

            var director = StartleDirector.Attach();
            Assert.That(director, Is.Not.Null,
                "no '" + StartleDirector.GroupName + "' group under "
                + StartleDirector.MarkerRootName + " — regenerate the map (지도 생성) "
                + "and re-open the solo scene; see the headline test's message.");

            var bound = StartleDirector.MarkerClearanceMetres - StartleDirector.TriggerMetres;
            var rig = FindRig();

            // Grace is real time and the test is not — the pacing is pure so the test
            // can advance match time by the rule's own constant.
            director!.Pacing.Advance(StartlePacing.GraceSeconds);

            // -- the skitterer: sample the clone across its whole crossing. --
            var skitterers = MarkersOf(director, StartleDirector.SkittererPrefix);
            Assert.That(skitterers, Is.Not.Empty,
                "the map holds no skitterer marker. The kind rotation in "
                + "MapSceneBuilder.BuildStartles deals every kind into every 4-storey "
                + "band unless clearance rejected every slot — regenerate and read the "
                + "[SceneGen] 깜짝 log line.");

            Transform? firedAt = null;
            foreach (var marker in skitterers)
            {
                // Facing along the corridor, either way: the stage needs SkitterNear
                // metres of open corridor ahead of the eye, and a straight-piece
                // marker's forward is its corridor's axis by generation.
                foreach (var sign in new[] { 1f, -1f })
                {
                    var before = director.FiredTotal;
                    Stand(rig, marker.position, marker.forward * sign);

                    var waited = 0;
                    while (director.FiredTotal == before && waited++ < 90)
                    {
                        yield return null;
                    }

                    if (director.FiredTotal > before)
                    {
                        firedAt = marker;
                        break;
                    }
                }

                if (firedAt != null)
                {
                    break;
                }
            }

            Assert.That(firedAt, Is.Not.Null,
                "no skitterer marker could stage a crossing facing either way along "
                + "its corridor within 90 frames. Either every skitterer marker lacks "
                + StartleDirector.SkitterNearMetres.ToString("0.0") + " m of clear run "
                + "both ways, or the staging refused for a new reason — regenerate and "
                + "read the [SceneGen] 깜짝 log line.");

            var seen = 0;
            var worst = 0f;
            for (var frame = 0; frame < 120; frame++)
            {
                var crossing = GameObject.Find("[Startle Skitterer]");
                if (crossing == null)
                {
                    if (seen > 0)
                    {
                        // The crossing finished; the whole path has been sampled.
                        break;
                    }
                }
                else
                {
                    seen++;
                    worst = Mathf.Max(worst, PlanMetres(crossing.transform.position, firedAt!.position));
                }

                yield return null;
            }

            Assert.That(seen, Is.GreaterThan(0),
                "the skitterer fired but no [Startle Skitterer] clone was ever found by name.");
            Assert.That(worst, Is.LessThanOrEqualTo(bound),
                "a crossing point measured " + worst.ToString("0.00") + " m in plan from "
                + firedAt!.name + "; the marker's keep-out guarantee only covers "
                + bound.ToString("0.00") + " m past the trigger (MarkerClearanceMetres − "
                + "TriggerMetres). The StageReachMetres clamp is not holding.");

            // -- the glimpse: the figure's stand, on a deep storey. --
            director.Pacing.Advance(StartlePacing.CooldownSeconds);

            var glimpses = MarkersOf(director, StartleDirector.GlimpsePrefix);
            Assert.That(glimpses, Is.Not.Empty,
                "the map holds no glimpse marker; every storey B"
                + (StartleDirector.GlimpseStoreyFloor + 1) + "+ gets one in its second "
                + "slot unless clearance rejected it — regenerate and read the "
                + "[SceneGen] 깜짝 log line.");

            Transform? glimpsedAt = null;
            foreach (var marker in glimpses)
            {
                foreach (var sign in new[] { 1f, -1f })
                {
                    var before = director.FiredTotal;
                    Stand(rig, marker.position, marker.forward * sign);

                    var waited = 0;
                    while (director.FiredTotal == before && waited++ < 240)
                    {
                        yield return null;
                    }

                    if (director.FiredTotal > before)
                    {
                        glimpsedAt = marker;
                        break;
                    }
                }

                if (glimpsedAt != null)
                {
                    break;
                }
            }

            Assert.That(glimpsedAt, Is.Not.Null,
                "no glimpse marker could stage the figure in 240 frames of redrawn "
                + "attempts per facing. In a corridor the legal stand is usually BEHIND "
                + "the player (ahead of the eye is inside the excluded cone), so a map "
                + "where every glimpse marker lacks "
                + StartleDirector.GlimpseNearMetres.ToString("0.0") + " m of open "
                + "corridor both ways would refuse — regenerate and read the "
                + "[SceneGen] 깜짝 log line.");

            var figure = GameObject.Find("[Startle Figure]");
            Assert.That(figure, Is.Not.Null,
                "the glimpse fired but no [Startle Figure] clone is active.");

            var standAt = PlanMetres(figure!.transform.position, glimpsedAt!.position);
            Assert.That(standAt, Is.LessThanOrEqualTo(bound),
                "the figure stands " + standAt.ToString("0.00") + " m in plan from "
                + glimpsedAt.name + "; the marker's keep-out guarantee only covers "
                + bound.ToString("0.00") + " m past the trigger (MarkerClearanceMetres − "
                + "TriggerMetres). The StageReachMetres clamp is not holding.");
        }

        // ------------------------------------------------------------------
        // 3. The tombstone vocabulary is absent from every name this feature adds.
        // ------------------------------------------------------------------

        /// <summary>
        /// Reflects over the startle namespace and the new cue ids, tokenises every
        /// identifier the way the pivot guards do, and holds them against
        /// DeletedVocabulary.txt. The two tombstone suites walk the shipped assemblies
        /// anyway; this test exists so the walk that fails is the one INSIDE the
        /// feature's own folder, with the offending identifier in the message, before
        /// the integrator ever runs the full suite.
        /// </summary>
        [Test]
        public void The_new_names_hold_no_tombstoned_vocabulary()
        {
            var rows = File.ReadAllLines(Path.Combine(
                Application.dataPath, "Tests/EditMode/Pivot/DeletedVocabulary.txt"));

            var solo = new HashSet<string>();
            var korean = new List<string>();
            var compounds = new List<(string Word, string[] Mates)>();
            foreach (var row in rows)
            {
                if (row.StartsWith("solo|", System.StringComparison.Ordinal))
                {
                    solo.Add(row.Split('|')[1]);
                }
                else if (row.StartsWith("korean|", System.StringComparison.Ordinal))
                {
                    korean.Add(row.Split('|')[1]);
                }
                else if (row.StartsWith("compound|", System.StringComparison.Ordinal))
                {
                    var parts = row.Split('|');
                    compounds.Add((parts[1], parts[2].Split(' ')));
                }
            }

            Assert.That(solo.Count, Is.GreaterThan(20), "the vocabulary file did not parse.");

            var names = new List<string>();
            var assembly = typeof(StartleDirector).Assembly;
            var sawStartleTypes = 0;
            foreach (var type in assembly.GetTypes())
            {
                if (type.Namespace == null
                    || !type.Namespace.StartsWith("HorrorGame.Gameplay.Startle",
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                sawStartleTypes++;
                names.Add(type.Name);
                foreach (var member in type.GetMembers(
                             BindingFlags.Public | BindingFlags.NonPublic
                             | BindingFlags.Instance | BindingFlags.Static
                             | BindingFlags.DeclaredOnly))
                {
                    names.Add(member.Name);
                }
            }

            Assert.That(sawStartleTypes, Is.GreaterThanOrEqualTo(2),
                "reflection found " + sawStartleTypes + " types in HorrorGame.Gameplay.Startle; the "
                + "namespace holds at least StartleDirector and StartlePacing, so the filter is broken "
                + "and this test is checking nothing.");

            foreach (var cue in System.Enum.GetNames(typeof(AudioCueId)))
            {
                if (cue.StartsWith("Startle", System.StringComparison.Ordinal))
                {
                    names.Add(cue);
                }
            }

            names.Add(nameof(StartleTests));

            foreach (var name in names)
            {
                var tokens = Tokenise(name);

                foreach (var token in tokens)
                {
                    Assert.That(solo.Contains(token), Is.False,
                        "'" + name + "' tokenises to a deleted word: '" + token + "'. Read the "
                        + "reason column in DeletedVocabulary.txt before renaming around it.");
                }

                foreach (var (word, mates) in compounds)
                {
                    if (!tokens.Contains(word))
                    {
                        continue;
                    }

                    foreach (var mate in mates)
                    {
                        Assert.That(tokens.Contains(mate), Is.False,
                            "'" + name + "' contains compound word '" + word + "' beside its mate '"
                            + mate + "'.");
                    }
                }

                foreach (var noun in korean)
                {
                    Assert.That(name.Contains(noun), Is.False,
                        "'" + name + "' contains the deleted Korean noun '" + noun + "'.");
                }
            }
        }

        // ------------------------------------------------------------------
        // Scaffolding.
        // ------------------------------------------------------------------

        /// <summary>
        /// The tokeniser the pivot guards pin with their <c>token|</c> rows, restated:
        /// split on everything that is not a letter, then on lower→Upper boundaries and
        /// letter→digit boundaries, lowercase the lot.
        /// </summary>
        private static HashSet<string> Tokenise(string identifier)
        {
            var tokens = new HashSet<string>();
            var current = new System.Text.StringBuilder();

            void Take()
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString().ToLowerInvariant());
                    current.Clear();
                }
            }

            for (var i = 0; i < identifier.Length; i++)
            {
                var c = identifier[i];
                if (!char.IsLetter(c))
                {
                    Take();
                    continue;
                }

                if (current.Length > 0 && char.IsUpper(c)
                    && (char.IsLower(identifier[i - 1])
                        || (i + 1 < identifier.Length && char.IsLower(identifier[i + 1]))))
                {
                    Take();
                }

                current.Append(c);
            }

            Take();
            return tokens;
        }

        private IEnumerator LoadMap()
        {
            // Loading the solo scene re-emits Mirror's packaging complaint about a
            // folder Unity itself calls immutable — GunTests' suppression, copied with
            // its condition: every step below is asserted explicitly.
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var director = Object.FindFirstObjectByType<MatchDirector>();
            if (director != null && director.Map == null)
            {
                director.BeginMatch(Seed);
            }

            yield return null;

            foreach (var agent in Object.FindObjectsByType<MonsterAgent>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                agent.enabled = false;
            }

            // The match's own polling — chutes swallowing the rig, the out-of-bounds
            // recovery — is switched off so this test can stand the rig at a marker on
            // any storey without the director interpreting the teleport as a fall. The
            // same liberty as disabling the creatures above: this test drives the
            // startle path, and the match path has its own suites.
            if (director != null)
            {
                director.enabled = false;
            }
        }

        private static Transform FindRig()
        {
            var interactor = Object.FindFirstObjectByType<PlayerInteractor>();
            Assert.That(interactor, Is.Not.Null, "the solo scene has no player rig.");
            return interactor!.transform.root;
        }

        /// <summary>Markers of one kind under the director, shallowest storey first — the cabinet test's teleport-near-your-own-storey caution, reused.</summary>
        private static List<Transform> MarkersOf(StartleDirector director, string prefix)
        {
            var found = new List<Transform>();
            foreach (Transform child in director.transform)
            {
                // The skitterer template's name begins with the skitterer MARKER
                // prefix — the same trap BuildSpots documents.
                if (child.name == StartleDirector.FigureTemplateName
                    || child.name == StartleDirector.SkittererTemplateName)
                {
                    continue;
                }

                if (child.name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    found.Add(child);
                }
            }

            found.Sort((a, b) => b.position.y.CompareTo(a.position.y));
            return found;
        }

        /// <summary>Plan (x·z) distance, metres — the geometry the keep-out guarantee is measured in (MapSceneBuilder.StartleCells checks dx·dz, never dy).</summary>
        private static float PlanMetres(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>
        /// Teleports the rig to a point and aims it down a heading. The moving stages
        /// read the EYE's forward, and <c>PlayerLook</c> re-applies its stored yaw
        /// every frame, so the aim must go through <c>SetLook</c> — a rotation written
        /// to the transform is undone one frame later. The bare-transform fallback
        /// covers a rig with no look component, where nothing fights the write.
        /// </summary>
        private static void Stand(Transform rig, Vector3 at, Vector3 facing)
        {
            Teleport(rig, at);

            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var yaw = Quaternion.LookRotation(facing.normalized, Vector3.up).eulerAngles.y;
            var look = rig.GetComponentInChildren<PlayerLook>(true);
            if (look != null)
            {
                look.SetLook(yaw, 0f);
            }
            else
            {
                rig.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        private static void Teleport(Transform rig, Vector3 to)
        {
            var controller = rig.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            rig.position = to;

            if (controller != null)
            {
                controller.enabled = true;
            }

            Physics.SyncTransforms();
        }
    }
}
#endif
