#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using UnityEngine;

namespace HorrorGame.Gameplay.Match
{
    /// <summary>
    /// The generated building, read back out of the loaded scene as §01's race needs it:
    /// where the runners start, and where the creatures start.
    /// <para>
    /// <b>Why this class exists at all.</b> The scene generator writes §12's markers as
    /// bare transforms and deliberately puts nothing on them — <c>MapSceneBuilder</c> lives
    /// in an editor assembly that cannot reference a runtime component, so the only thing
    /// joining the two is the marker's NAME. This file is the reader half of that
    /// agreement, and the <c>const string</c>s below are the agreement itself.
    /// </para>
    /// <para>
    /// <b>What this class used to be, and why that went.</b> Until the pivot it
    /// reconstructed §03's <c>SiteCatalog</c> — a floor per §12 zone, a signed room per
    /// 후보 지점, a 혼동쌍 on every label — so that <c>ObjectiveResolver</c> could lay out a
    /// clue chain over it, and it dropped every marker inside §01's 지상 apron so that a
    /// clue read at the van would not be free. DESCENT-PIVOT §3 버린다 deletes all of it:
    /// 「§03 단서 3층위 … 목적지가 처음부터 알려져 있다: 아래」 and 「§08 전리품 · 크레딧 ·
    /// 판매 … 통화가 없다」. A race narrows over nothing, sells nothing and has no 지상 to
    /// stand safely on, so <c>CandidateSites</c>, <c>LootSpawns</c>, <c>Catalog</c>,
    /// <c>Entrance</c>, <c>HasSurface</c>, <c>IsOnSurface</c>, <c>SurfaceRadius</c>,
    /// <c>TeamEntryPoint</c> and <c>ZoneCount</c> are gone rather than gated. Nobody should
    /// re-add them: the 출입구 is now §02's FINISH at the middle of B8 and
    /// <c>RaceDirector.LocateFinish</c> reads it straight off the scene, from
    /// <see cref="EntranceLightPrefix"/>, without an opinion from here.
    /// </para>
    /// <para>
    /// <b>The 후보 지점 and 전리품 markers may still be in the scene; nothing at run time
    /// reads them.</b> They were also, accidentally, the NavMesh audit's reachability probe
    /// — 152 LootSpawn + 24 CandidateSite of the 220 markers it pairs up. That job is the
    /// editor's and is done by scanning the scene for marker NAMES
    /// (<c>NavMeshConnectivity</c>, <c>PlayerTraversal</c>, <c>Reachability</c>), not
    /// through this class, so removing the runtime accessors cannot move the audit. If the
    /// generator stops WRITING those markers the probe has to be re-declared under its own
    /// name — see the report on this change.
    /// </para>
    /// </summary>
    public sealed class MatchMap
    {
        /// <summary>Root object the scene generator hangs the map from. Mirrors <c>MapSceneBuilder.MapRootName</c>.</summary>
        public const string MapRootName = "Map";

        /// <summary>Child of the root holding every marker group. Mirrors <c>MapSceneBuilder.MarkerRootName</c>.</summary>
        public const string MarkerRootName = "Markers";

        /// <summary>
        /// Marker group of §01's start line — the outer rim of B1, one marker per runner.
        /// <para>
        /// §11 seats up to <see cref="GameConstants.RaceRunnersMax"/>, and
        /// <c>DescentMap.PlaceStarts</c> spreads that many over the outer band's own cell
        /// list so nobody begins beside anybody. A scene with none of these is refused by
        /// <see cref="TryRead"/>: a race whose runners have nowhere to stand is not a
        /// degraded race, it is no race.
        /// </para>
        /// </summary>
        public const string PlayerSpawnGroup = "PlayerSpawns";

        /// <summary>
        /// Marker group holding §07's monster starts — one per storey on §01's tower.
        /// <para>
        /// Plural since the descent: §12-B③ writes 「괴물이 안쪽을 순찰한다」 about every
        /// floor, <see cref="GameConstants.MonstersPerStorey"/> is the count that follows
        /// from it, and a 투하구 is a fall rather than a path — so a creature can never
        /// leave the floor it starts on and eight floors need eight starts. See
        /// <see cref="MonsterSpawns"/>.
        /// </para>
        /// </summary>
        public const string MonsterSpawnGroup = "MonsterSpawns";

        /// <summary>
        /// The name the primary §07 start carries, bare. Mirrors
        /// <c>MapSketch.PrimaryMonsterSpawnName</c>.
        /// <para>
        /// <b>Two files agree on this string and neither can see the other.</b> The sketch is
        /// in an editor assembly and this is runtime, so the only thing joining them is the
        /// marker name written into the scene. <c>MapSketch</c> gives the last-declared start
        /// this exact name and every other start <c>MonsterSpawn_&lt;zone&gt;_&lt;node&gt;</c>,
        /// so the bare one is the primary and <c>MonsterShot.IsAnchor</c> — which matches this
        /// string exactly — keeps pointing at the creature a screenshot is supposed to be of.
        /// </para>
        /// <para>
        /// See <see cref="MonsterSpawn"/> for why the primary matters at all once every start
        /// carries a creature.
        /// </para>
        /// </summary>
        public const string PrimaryMonsterSpawnName = "MonsterSpawn";

        /// <summary>
        /// Name prefix of the light the generator leaves burning over §02's 도착점.
        /// <para>
        /// It used to be the 출입구 — the door a co-operative team walked back out of. On
        /// §01's tower the only <c>MapNodeKind.Entrance</c> is the middle of B8, 26 m under
        /// the start line, and 「나가는 유일한 길은 아래」. So this prefix now marks the
        /// FINISH, and <c>RaceDirector.LocateFinish</c> is the one thing that reads it:
        /// it takes the DEEPEST light carrying this prefix. Kept here rather than moved
        /// there because the generator and the reader have to agree on one string and this
        /// class is where every other such string lives. See <c>MapSceneBuilder.BuildLight</c>.
        /// </para>
        /// </summary>
        public const string EntranceLightPrefix = "EntranceLight";

        private readonly Transform[] _playerSpawns;
        private readonly Transform[] _monsterSpawns;

        private MatchMap(
            Transform[] playerSpawns,
            Transform[] monsterSpawns,
            Transform? primaryMonsterSpawn)
        {
            _playerSpawns = playerSpawns;
            _monsterSpawns = monsterSpawns;
            MonsterSpawn = primaryMonsterSpawn;
        }

        /// <summary>
        /// §01's start line, sorted by name. Twenty of these on a shipped descent; the local
        /// runner takes one and <c>RaceRunners</c> hands the rest out over §13.
        /// </summary>
        public IReadOnlyList<Transform> PlayerSpawns
        {
            get { return _playerSpawns; }
        }

        /// <summary>
        /// Every §07 monster start the scene carries, one per storey on §01's tower.
        /// <para>
        /// <b>A list rather than a point, because a creature cannot change floors.</b>
        /// §12-C makes the 투하구 the only vertical join and makes it one-way, and the
        /// NavMesh bakes accordingly — the audit's own line is <em>islands 8</em>, one per
        /// storey. So the creature that starts on B5 patrols B5 for the whole match and
        /// seven of eight floors have no §06 in them at all unless the map declares a start
        /// on each. <see cref="GameConstants.MonstersPerStorey"/> is that count and this is
        /// where the runtime reads it.
        /// </para>
        /// <para>
        /// <b>Sorted by name, like every other marker group.</b> §13's replay guarantee
        /// wants the same building on every machine, and the order this list is read in
        /// decides which creature is which — <c>MatchDirector</c> stands one agent up per
        /// entry. <see cref="MonsterSpawn"/> is the exception and says why.
        /// </para>
        /// <para>
        /// <b>Empty is a legitimate answer and is not silently repaired.</b> A scene with
        /// no MonsterSpawn group is a map with no hazard; the host logs the count it
        /// found and runs that many creatures, so the log and the game agree. Inventing a
        /// spawn here would be the failure this repo keeps finding — a number that is right
        /// in the report and absent from the build.
        /// </para>
        /// </summary>
        public IReadOnlyList<Transform> MonsterSpawns
        {
            get { return _monsterSpawns; }
        }

        /// <summary>
        /// Distinct storeys the monster starts are spread over — and therefore, on this
        /// building, §07's zone count.
        /// <para>
        /// <b>This is the audit's own number, computed from the object the game reads.</b>
        /// <c>NavMeshConnectivity</c> prints 「over N of 8 storeys (§06)」 by grouping
        /// MonsterSpawn markers by authored height, in the editor, before a match exists.
        /// This asks the identical question of the identical markers at run time, so a
        /// build whose audit says eight and whose match runs one has two numbers that
        /// disagree in the same log. <c>MatchDirector</c> prints it at every
        /// <c>BeginMatch</c> beside the number of creatures it actually stood up.
        /// </para>
        /// <para>
        /// <b>It is also what §07's 순찰 column is measured in, and that is not a pun.</b>
        /// §07 writes patrol scope in ZONES — 1개 구역, 2개 구역, 절반, 전체 — and on §01's
        /// tower a zone IS a storey: <c>DescentMap.Build</c> calls <c>AddZone</c> once per
        /// level. It used to be read off the 후보 지점 catalog, which is gone, so
        /// <c>MatchDirector.StepCreatures</c> feeds this to <c>MonsterAgent.SetMapZoneCount</c>
        /// instead. The two agree by construction on a descent map — eight levels, eight
        /// zones, eight starts — and a map that declares no starts answers 0, which
        /// <c>MonsterAgent</c> already documents as "unknown" and answers with §12's upper
        /// bound rather than with a monster that patrols nothing.
        /// </para>
        /// <para>
        /// Grouped by <see cref="MapGraph.StoreyChangeMetres"/> — 1.8 m, "the vertical
        /// separation above which two places are on different storeys and nothing sees
        /// between them" — because that is already this project's answer to "same floor?"
        /// and <c>MatchDirector.OnSameStorey</c> asks it with the same constant. Two spawns
        /// on one floor therefore count once, which is what makes this a storey count rather
        /// than a rename of <c>MonsterSpawns.Count</c>.
        /// </para>
        /// </summary>
        public int MonsterStoreyCount
        {
            get
            {
                var storeys = 0;
                for (var i = 0; i < _monsterSpawns.Length; i++)
                {
                    var counted = false;
                    for (var j = 0; j < i && !counted; j++)
                    {
                        counted = Mathf.Abs(_monsterSpawns[i].position.y - _monsterSpawns[j].position.y)
                                  < MapGraph.StoreyChangeMetres;
                    }

                    if (!counted)
                    {
                        storeys++;
                    }
                }

                return storeys;
            }
        }

        /// <summary>
        /// The primary §07 monster start — the one a single-creature reader means.
        /// <para>
        /// <b>The marker named bare <see cref="PrimaryMonsterSpawnName"/></b>, falling back
        /// to the last one the sketch declared. Both channels point at the same marker and
        /// both are the map's own statement: <c>MapSketch.BuildMonsterSpawns</c> names the
        /// last declaration bare, and <c>DescentMap.PlaceStarts</c> declares B5's middle
        /// last on purpose, so that a build which can carry only one creature carries it
        /// half way down, "where §12-B wants the descent to turn dangerous rather than start
        /// that way". <see cref="PrimaryMonsterStart"/> holds the rule.
        /// </para>
        /// <para>
        /// With one marker in the group this is that marker, so a scene from before the
        /// per-storey starts reads exactly as it did.
        /// </para>
        /// <para>
        /// It is <em>not</em> what the match hunts with. <c>MatchDirector</c> stands up one
        /// agent per entry of <see cref="MonsterSpawns"/>; this only decides which of them
        /// is the scene's authored rig and which the presentation layers follow when they
        /// can only follow one.
        /// </para>
        /// </summary>
        public Transform? MonsterSpawn { get; }

        /// <summary>
        /// Reads the map out of a loaded scene.
        /// </summary>
        /// <param name="map">The building, or null on failure.</param>
        /// <param name="failure">
        /// Why it could not be read, in words a person can act on. A match is far better
        /// off refusing to start than putting twenty runners into a building §01 cannot
        /// describe.
        /// </param>
        /// <returns>True when the scene carried a start line.</returns>
        public static bool TryRead(out MatchMap? map, out string failure)
        {
            map = null;

            var root = GameObject.Find(MapRootName);
            if (root == null)
            {
                failure = "No '" + MapRootName + "' object in the scene. Run HorrorGame ▸ Scene Gen ▸ Generate First Map.";
                return false;
            }

            var markers = root.transform.Find(MarkerRootName);
            if (markers == null)
            {
                failure = "'" + MapRootName + "' has no '" + MarkerRootName + "' child; the scene predates the marker groups.";
                return false;
            }

            var playerSpawns = ChildrenOf(markers, PlayerSpawnGroup);
            if (playerSpawns.Length == 0)
            {
                // The one condition that makes a scene unraceable. It replaces the old
                // refusal — "no CandidateSite markers, so §03 has nothing to narrow to" —
                // which was about a clue chain this game no longer has.
                failure = "No " + PlayerSpawnGroup + " markers under '" + MarkerRootName
                          + "'. §01 starts the field on the rim of B1 and there is nowhere to put them.";
                return false;
            }

            map = new MatchMap(
                playerSpawns,
                ChildrenOf(markers, MonsterSpawnGroup),
                PrimaryMonsterStart(markers));

            failure = string.Empty;
            return true;
        }

        /// <summary>
        /// Picks the primary §07 start out of the group, by the two channels a sketch has
        /// for naming one — in the order the sketch itself declares them.
        /// <list type="number">
        /// <item><description><b>The bare <see cref="PrimaryMonsterSpawnName"/>.</b>
        /// <c>MapSketch.BuildMonsterSpawns</c> gives that name to the primary and only to the
        /// primary; every other start is <c>MonsterSpawn_&lt;zone&gt;_&lt;node&gt;</c>. It is
        /// an explicit statement rather than an inference, so it is asked first.</description></item>
        /// <item><description><b>The last child the group was BUILT with.</b>
        /// <c>MapSceneBuilder.BuildMarkers</c> walks the sketch's marker list in order and
        /// parents each object as it goes, so sibling order is declaration order — and
        /// <c>DescentMap.PlaceStarts</c> declares B5's middle LAST on purpose, so that a
        /// reader which can only carry one creature carries it half way down. Sibling order
        /// is serialised into the scene, so this is stable across machines.</description></item>
        /// </list>
        /// <para>
        /// The two agree on every map the generator writes today; they are both here because
        /// they are written down in two different files that cannot see each other, and a
        /// reader that honoured only one of them would silently move the shipped creature
        /// the first time the other changed.
        /// </para>
        /// </summary>
        private static Transform? PrimaryMonsterStart(Transform markers)
        {
            var group = markers.Find(MonsterSpawnGroup);
            if (group == null || group.childCount == 0)
            {
                return null;
            }

            for (var i = 0; i < group.childCount; i++)
            {
                var child = group.GetChild(i);
                if (string.Equals(child.name, PrimaryMonsterSpawnName, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return group.GetChild(group.childCount - 1);
        }

        private static Transform[] ChildrenOf(Transform markers, string groupName)
        {
            var group = markers.Find(groupName);
            if (group == null)
            {
                return Array.Empty<Transform>();
            }

            var children = new Transform[group.childCount];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = group.GetChild(i);
            }

            Array.Sort(children, CompareByName);
            return children;
        }

        private static int CompareByName(Transform a, Transform b)
        {
            return string.CompareOrdinal(a.name, b.name);
        }
    }
}
