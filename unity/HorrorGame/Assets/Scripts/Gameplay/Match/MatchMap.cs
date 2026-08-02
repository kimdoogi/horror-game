#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Gameplay.Monster;
using UnityEngine;

namespace HorrorGame.Gameplay.Match
{
    /// <summary>
    /// The generated map, read back as the building §03's clue chain narrows over.
    /// <para>
    /// <b>Why this class exists at all.</b> The scene generator writes §12's markers as
    /// bare transforms and deliberately puts nothing on them — ARCHITECTURE §4 and §13
    /// forbid the scene from carrying a hint of which candidate site is live, so every
    /// one of them is an identical empty. That leaves the host to reconstruct the
    /// <see cref="SiteCatalog"/> from names and positions before
    /// <see cref="ObjectiveResolver"/> can draw a layout, and that reconstruction is
    /// this file.
    /// </para>
    /// <para>
    /// <b>Zones stand in for §03's floors.</b> §03's chain is a property, then a
    /// number, then a label — "물이 있는 층" → "물이 있는 층은 지하 3층이다" → "ㅁ-6 좌".
    /// §12's 첫 맵 스케치 is one storey with four zones, so the four zones are what the
    /// chain narrows over; the shape of the reasoning is unchanged and the code that
    /// runs it (<see cref="ClueChain"/>) never learns the difference. A multi-storey
    /// map later supplies real floors and this mapping goes away.
    /// </para>
    /// <para>
    /// <b>Signage is fixed, not seeded.</b> §03's randomisation table puts 목표물 위치,
    /// 단서 위치 · 내용 and 전리품 배치 in the random column and 맵 구조 in the fixed one,
    /// because "학습 가능해야 실력이 성장한다". A room's sign is part of the building, so
    /// the label a site carries is derived from the marker order and is the same every
    /// match — which is what lets a veteran hear "ㅁ-6 좌" and already know where to run.
    /// </para>
    /// </summary>
    public sealed class MatchMap
    {
        /// <summary>Root object the scene generator hangs the map from. Mirrors <c>MapSceneBuilder.MapRootName</c>.</summary>
        public const string MapRootName = "Map";

        /// <summary>Child of the root holding every marker group. Mirrors <c>MapSceneBuilder.MarkerRootName</c>.</summary>
        public const string MarkerRootName = "Markers";

        /// <summary>Marker group of §12's 후보 지점 — where a clue or the objective can be.</summary>
        public const string CandidateSiteGroup = "CandidateSites";

        /// <summary>Marker group of §08's 전리품 drops.</summary>
        public const string LootSpawnGroup = "LootSpawns";

        /// <summary>Marker group of §01's entry points.</summary>
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
        /// Name prefix of the light the generator leaves burning at the 출입구. It is the
        /// only object in the scene that marks the way out, so it is how the surface is
        /// located. See <c>MapSceneBuilder.BuildLight</c>.
        /// </summary>
        public const string EntranceLightPrefix = "EntranceLight";

        private readonly Transform[] _candidateSites;
        private readonly Transform[] _lootSpawns;
        private readonly Transform[] _playerSpawns;
        private readonly Transform[] _monsterSpawns;

        private MatchMap(
            SiteCatalog catalog,
            Transform[] candidateSites,
            Transform[] lootSpawns,
            Transform[] playerSpawns,
            Transform[] monsterSpawns,
            Transform? primaryMonsterSpawn,
            Vector3 entrance,
            bool hasSurface,
            int zoneCount)
        {
            Catalog = catalog;
            _candidateSites = candidateSites;
            _lootSpawns = lootSpawns;
            _playerSpawns = playerSpawns;
            _monsterSpawns = monsterSpawns;
            MonsterSpawn = primaryMonsterSpawn;
            Entrance = entrance;
            HasSurface = hasSurface;
            ZoneCount = zoneCount;
        }

        /// <summary>The building, as §03's narrowing sees it.</summary>
        public SiteCatalog Catalog { get; }

        /// <summary>§12's 후보 지점, in the same order as <see cref="SiteCatalog.Sites"/>.</summary>
        public IReadOnlyList<Transform> CandidateSites
        {
            get { return _candidateSites; }
        }

        /// <summary>§08's 전리품 drops. §12 puts one at every 막힌 길.</summary>
        public IReadOnlyList<Transform> LootSpawns
        {
            get { return _lootSpawns; }
        }

        /// <summary>Where §01's players come in.</summary>
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
        /// no MonsterSpawn group is a map with no antagonist; the host logs the count it
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
        /// Distinct storeys the monster starts are spread over.
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
        /// Grouped by <see cref="MapGraph.StoreyChangeMetres"/> — 1.8 m, "the vertical
        /// separation above which two places are on different storeys and nothing sees
        /// between them" — because that is already this project's answer to "same floor?"
        /// and <see cref="IsOnSurface"/> asks it with the same constant. Two spawns on one
        /// floor therefore count once, which is what makes this a storey count rather than
        /// a rename of <c>MonsterSpawns.Count</c>.
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
        /// The 출입구, wherever the building keeps it.
        /// <para>
        /// On the co-operative map it is the door: §01's loop turns here, §02's win condition
        /// is reaching it, and the whole surface half of that game is measured from this
        /// point. On §01's tower there is no door — <c>DescentMap.MarkPlaces</c> marks
        /// exactly one <c>MapNodeKind.Entrance</c>, the middle of B8, "because that is the
        /// marker §12's rules are written against… and the only way out of this building is
        /// down". There it is §02's FINISH, 26 m below the runners, and
        /// <see cref="HasSurface"/> is false.
        /// </para>
        /// <para>
        /// It is not what decides §02. <c>RaceDirector.LocateFinish</c> looks the finish up
        /// itself, from the same EntranceLight, so the race does not inherit this property's
        /// opinion about height.
        /// </para>
        /// </summary>
        public Vector3 Entrance { get; }

        /// <summary>
        /// Whether this building has a §01 지상 at all.
        /// <para>
        /// <b>A race has none.</b> DESCENT-PIVOT §3 버린다 lists 「§01 왕복 · 지상 상점」 with the
        /// reason 「경주에 재보급이 없다」, §08 is deleted outright, and §01's flow is 출발 → 도착
        /// with no 귀환 in it. There is no van, no shop, no safe ground and no way out but down.
        /// </para>
        /// <para>
        /// <b>Read off the building, not off a checkbox.</b> True unless the 출입구 is below
        /// every player start — which is the same argument <c>RaceDirector</c> makes for
        /// finding the finish: "a building whose exit is upstairs is the old co-operative
        /// map". The generator hangs an EntranceLight from the ceiling of its own floor
        /// (<c>MapSceneBuilder.BuildLight</c>, +2.8 m), so on the co-op map the light sits
        /// ABOVE the spawns it is surrounded by and this stays true; on the tower it is 23.45 m
        /// under them. Deriving it here rather than taking <c>MatchDirector._raceMode</c> means
        /// the editor tools and tests that read a map without a director get the same answer.
        /// </para>
        /// </summary>
        public bool HasSurface { get; }

        /// <summary>Zones §12 divided the map into. Fed to §07's 순찰 column.</summary>
        public int ZoneCount { get; }

        /// <summary>
        /// Where §03's 왕복 is measured from — the first PlayerSpawn, which is literally
        /// where the team walks in.
        /// <para>
        /// Not <see cref="Entrance"/>, which marks the 출입구 itself. On §12's first map
        /// the door is a stairwell whose landing sits 2 m above the floor and is a
        /// separate NavMesh island, so every path query from it comes back
        /// <c>PathPartial</c> and <c>ObjectiveResolver</c> concludes that nothing in the
        /// building is reachable. The spawn markers are on the connected surface, so
        /// asking "can the team walk from here to that site" from one of them is both
        /// answerable and the question §03 actually means.
        /// </para>
        /// </summary>
        public Vector3 TeamEntryPoint
        {
            get { return _playerSpawns.Length > 0 ? _playerSpawns[0].position : Entrance; }
        }

        /// <summary>
        /// Radius around <see cref="Entrance"/> that counts as §01's 지상.
        /// <para>
        /// Not an invented number: the generator scatters the four PlayerSpawn markers
        /// over <c>graph.NodesWithinWalk(entrance, LineOfSightBreakSpacingMin)</c>, so
        /// this is exactly the ground the map already treats as "at the door". Using
        /// anything larger would let a player shop from inside the building; anything
        /// smaller would put a spawn marker outside the safe zone it was placed in.
        /// </para>
        /// </summary>
        public static float SurfaceRadius
        {
            get { return GameConstants.LineOfSightBreakSpacingMin; }
        }

        /// <summary>Whether a world position is on §01's 지상 — inside the 출입구's apron.</summary>
        /// <remarks>
        /// <b>On a race there is no 지상, so this is false everywhere.</b> See
        /// <see cref="HasSurface"/>. Adding a height guard to the circle was not enough and
        /// was in fact the harm: it correctly exempted B2–B8 and thereby planted the whole
        /// 15 m apron on the middle of B1 — the one cell both of B1's 투하구 sit in. Standing
        /// on the way down made §06 forget you (<c>MatchDirector.StepMonster</c> drops the
        /// target when <c>_onSurface</c>) and fired §01's 귀환 in a game that has none, because
        /// <c>UpdatePhase</c> runs in race mode too. A race's safe zone is the one thing §01
        /// deleted, and putting it over the exit is the worst place to leave it.
        /// <para>
        /// <b>Height is still part of the question for the map that does have a 지상.</b> A
        /// plan-only circle was harmless while the co-operative building sprawled sideways —
        /// no two floors sat on the same square metre — and 하강 is what broke it. The vertical
        /// tolerance is <see cref="MapGraph.StoreyChangeMetres"/> because that is already the
        /// project's answer to "is this the same floor": the graph uses it to decide an edge
        /// crosses a storey. Reusing it means 지상 and 층 cannot disagree.
        /// </para>
        /// </remarks>
        public bool IsOnSurface(Vector3 position)
        {
            if (!HasSurface)
            {
                return false;
            }

            if (Mathf.Abs(position.y - Entrance.y) >= MapGraph.StoreyChangeMetres)
            {
                return false;
            }

            var flat = position - Entrance;
            flat.y = 0f;
            return flat.sqrMagnitude <= SurfaceRadius * SurfaceRadius;
        }

        /// <summary>The world transform of a site, by the id <see cref="Catalog"/> knows it as.</summary>
        public Transform? SiteTransform(int siteId)
        {
            return siteId >= 0 && siteId < _candidateSites.Length ? _candidateSites[siteId] : null;
        }

        /// <summary>
        /// Reads the map out of a loaded scene.
        /// </summary>
        /// <param name="map">The reconstructed building, or null on failure.</param>
        /// <param name="failure">
        /// Why it could not be read, in words a person can act on. A match is far better
        /// off refusing to start than sending a player down into a building §03's chain
        /// cannot describe — the same argument <see cref="ObjectiveResolver.VerifyChainConverges"/>
        /// makes for the layout.
        /// </param>
        /// <returns>True when the scene carried everything §03 and §12 need.</returns>
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
            var monsterSpawns = ChildrenOf(markers, MonsterSpawnGroup);
            var entrance = FindEntrance(root.transform, playerSpawns);
            var hasSurface = !IsBelowEveryStart(entrance.y, playerSpawns);

            var sites = OutsideTheApron(entrance, hasSurface, ChildrenOf(markers, CandidateSiteGroup), CandidateSiteGroup);
            var loot = OutsideTheApron(entrance, hasSurface, ChildrenOf(markers, LootSpawnGroup), LootSpawnGroup);

            if (sites.Length == 0)
            {
                failure = "No " + CandidateSiteGroup + " markers outside the 출입구 apron. §12 requires "
                          + GameConstants.CandidateSitesPerZone + " per zone and §03 has nothing to narrow to without them.";
                return false;
            }

            SiteCatalog? catalog;
            Transform[] ordered;
            int zoneCount;
            if (!TryBuildCatalog(sites, out catalog, out ordered, out zoneCount, out failure) || catalog == null)
            {
                return false;
            }

            map = new MatchMap(
                catalog,
                ordered,
                loot,
                playerSpawns,
                monsterSpawns,
                PrimaryMonsterStart(markers),
                entrance,
                hasSurface,
                zoneCount);

            failure = string.Empty;
            return true;
        }

        /// <summary>
        /// Drops the markers that fall inside §01's 지상 apron.
        /// <para>
        /// §01 and §08 make the ground around the vehicle a 안전 지대, and §03 builds the
        /// entire clue economy on reading being dangerous — "위험 구역에 들어가야 한다.
        /// 안전한 곳에서 해결 불가". A 후보 지점 the team can stand on while the monster is
        /// forbidden to hunt them would be a clue read for free, and a 전리품 there would
        /// be credits for free. §12's first map does put one candidate site nine metres
        /// from the door, so this is a real case rather than a defensive one.
        /// </para>
        /// <para>
        /// Dropped rather than moved: where §12's sites go is the map's decision, and a
        /// runtime that nudged one would make the validated graph stop describing the
        /// scene players walk in.
        /// </para>
        /// <para>
        /// <b>Nothing is dropped when the building has no 지상.</b> The whole argument above
        /// is that the apron is a 안전 지대; where there is no apron there is nothing to be
        /// free inside. And the test below is deliberately flat — the co-operative map is one
        /// storey and Y would only add noise — which on a tower means a circle round the
        /// middle punches through all eight floors at once. Measured on
        /// <c>Map_FirstSketch_Solo</c>, seed 20260802: 29 of 199 LootSpawn markers were being
        /// dropped, 3 or 4 on every storey including B8, because they lie within 15 m in plan
        /// of a door that is 26 m below them.
        /// </para>
        /// </summary>
        /// <param name="entrance">§01's 출입구. Ignored when <paramref name="hasSurface"/> is false.</param>
        /// <param name="hasSurface">Whether this building has a 지상 at all. See <see cref="HasSurface"/>.</param>
        /// <param name="markers">The group's markers.</param>
        /// <param name="groupName">What to call the group if any are dropped.</param>
        private static Transform[] OutsideTheApron(
            Vector3 entrance, bool hasSurface, Transform[] markers, string groupName)
        {
            if (!hasSurface)
            {
                return markers;
            }

            var kept = new List<Transform>(markers.Length);
            var dropped = 0;

            for (var i = 0; i < markers.Length; i++)
            {
                var flat = markers[i].position - entrance;
                flat.y = 0f;

                if (flat.sqrMagnitude <= SurfaceRadius * SurfaceRadius)
                {
                    dropped++;
                    continue;
                }

                kept.Add(markers[i]);
            }

            if (dropped > 0)
            {
                Debug.LogWarning(
                    "[Match] " + dropped + " " + groupName + " marker(s) sit inside the " + SurfaceRadius
                    + " m 출입구 apron and were dropped. §01 makes that ground a 안전 지대, so a clue or a "
                    + "전리품 there would be free — §12's 후보 지점 rules want them out in the building.");
            }

            return kept.ToArray();
        }

        /// <summary>
        /// Turns the candidate-site markers into §03's catalog: one floor per §12 zone,
        /// one signed room per marker.
        /// </summary>
        private static bool TryBuildCatalog(
            Transform[] sites, out SiteCatalog? catalog, out Transform[] ordered, out int zoneCount, out string failure)
        {
            catalog = null;
            ordered = System.Array.Empty<Transform>();
            zoneCount = 0;

            // Grouped by the zone letter the generator writes into every marker name
            // ("CandidateSite_A 나무_3"). Sorted so the catalog a seed is laid out over is
            // the same one on every machine — §13's replay guarantee starts here.
            var byZone = new SortedDictionary<string, List<Transform>>(StringComparer.Ordinal);
            for (var i = 0; i < sites.Length; i++)
            {
                var key = ZoneKeyOf(sites[i].name);
                if (!byZone.TryGetValue(key, out var bucket))
                {
                    bucket = new List<Transform>();
                    byZone[key] = bucket;
                }

                bucket.Add(sites[i]);
            }

            var features = DistinguishingFeatures();
            var floors = new List<FloorDescriptor>(byZone.Count);
            var catalogSites = new List<CandidateSite>(sites.Length);
            var inCatalogOrder = new List<Transform>(sites.Length);

            var floorIndex = 0;
            foreach (var pair in byZone)
            {
                var glyph = ClueGlyphs.FromDigit(floorIndex + 1);
                if (glyph == ClueGlyph.Unreadable)
                {
                    failure = "The map has more than " + ClueGlyphs.Digits.Count + " zones. §03 signs a floor with a "
                              + "single digit, so it cannot name this many.";
                    return false;
                }

                // A floor past the end of the feature list gets none. §03 allows that
                // explicitly — such a floor holds clues and loot but never the objective,
                // because the first clue would have nothing to say about it.
                var feature = floorIndex < features.Length ? features[floorIndex] : FloorFeature.None;
                floors.Add(new FloorDescriptor(floorIndex, glyph, feature));

                pair.Value.Sort(CompareByName);

                for (var i = 0; i < pair.Value.Count; i++)
                {
                    var siteId = catalogSites.Count;
                    catalogSites.Add(new CandidateSite(
                        siteId,
                        floorIndex,
                        floorIndex,
                        pair.Value[i].position.ToVec3(),
                        SignFor(siteId, i)));
                    inCatalogOrder.Add(pair.Value[i]);
                }

                floorIndex++;
            }

            try
            {
                catalog = new SiteCatalog(floors, catalogSites);
            }
            catch (ArgumentException error)
            {
                failure = "The generated map cannot carry §03's chain: " + error.Message;
                return false;
            }

            // Handed back in catalog order, so site id N and CandidateSites[N] are the
            // same room. Everything downstream — placement, the objective spawn,
            // IsObjectiveSite — relies on that one identity.
            ordered = inCatalogOrder.ToArray();
            zoneCount = floors.Count;
            failure = string.Empty;
            return true;
        }

        /// <summary>
        /// Signs one room. §03's label is three marks — a wing, a number and a side — and
        /// the shape exists so each of the four 혼동쌍 has somewhere to live.
        /// <para>
        /// The number rotates through the digits by site id, which does two things at
        /// once: it keeps labels unique within a floor (<see cref="SiteCatalog"/> refuses
        /// a map where they are not), and it puts §03's 6↔9 and 1↔7 pairs on rooms that
        /// really exist. A player who misremembers the number therefore walks confidently
        /// into the wrong room rather than into a wall, which is the failure §03 says it
        /// is designed around.
        /// </para>
        /// </summary>
        private static SiteLabel SignFor(int siteId, int indexOnFloor)
        {
            var digits = ClueGlyphs.Digits;
            return new SiteLabel(
                indexOnFloor % 2 == 0 ? ClueGlyph.WingMieum : ClueGlyph.WingIeung,
                digits[siteId % digits.Count],
                (indexOnFloor / 2) % 2 == 0 ? ClueGlyph.SideLeft : ClueGlyph.SideRight);
        }

        /// <summary>
        /// §03's floor properties, in the order the enum declares them. Read from the
        /// enum rather than listed here so a new property added to Core cannot leave a
        /// zone silently featureless.
        /// </summary>
        private static FloorFeature[] DistinguishingFeatures()
        {
            var all = (FloorFeature[])Enum.GetValues(typeof(FloorFeature));
            var kept = new List<FloorFeature>(all.Length);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != FloorFeature.None)
                {
                    kept.Add(all[i]);
                }
            }

            return kept.ToArray();
        }

        /// <summary>
        /// The zone letter out of a generated marker name. The generator writes
        /// <c>&lt;kind&gt;_&lt;zone name&gt;_&lt;node&gt;</c> and §12's zone names are
        /// "A 나무", "B 타일" and so on, so the letter is everything between the first
        /// underscore and the first space.
        /// </summary>
        private static string ZoneKeyOf(string markerName)
        {
            var underscore = markerName.IndexOf('_');
            if (underscore < 0 || underscore + 1 >= markerName.Length)
            {
                return markerName;
            }

            var rest = markerName.Substring(underscore + 1);
            var space = rest.IndexOf(' ');
            return space > 0 ? rest.Substring(0, space) : rest;
        }

        /// <summary>
        /// Locates §01's way out. The burning EntranceLight is the generator's own mark
        /// for it ("the way out stays lit"); a scene without one falls back to the
        /// average of the player spawns, which the generator placed around that door.
        /// <para>
        /// <b>The light's height is dropped to the players' floor only when that is the
        /// light's own floor.</b> A fitting hangs from a ceiling — <c>MapSceneBuilder.BuildLight</c>
        /// puts it 2.8 m up — so its Y is a light's Y and not a place to stand, and on the
        /// co-operative map, where every spawn is scattered round this same door
        /// (<c>MapSketch</c> uses <c>NodesWithinWalk(entrance, LineOfSightBreakSpacingMin)</c>),
        /// the spawn floor is exactly the correction that wants making.
        /// </para>
        /// <para>
        /// On §01's tower it was a lie that moved the building. The only 출입구 mark is §02's
        /// finish at the middle of B8 — <c>EntranceLight_B8 굴착층_910</c> at
        /// (31.25, −23.45, 31.25) — and the 65 runners start eight storeys above it at y 0,
        /// so taking the light's X and Z with the spawns' Y resolved the 출입구 to
        /// (31.25, 0.0, 31.25): <b>the middle of B1</b>, the cell both of B1's 투하구 sit in.
        /// Keep the mark's own height and that cannot happen; see <see cref="HasSurface"/>
        /// for what the height then tells us.
        /// </para>
        /// </summary>
        private static Vector3 FindEntrance(Transform root, Transform[] playerSpawns)
        {
            var lights = root.GetComponentsInChildren<Light>(includeInactive: true);
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i].name.StartsWith(EntranceLightPrefix, StringComparison.Ordinal))
                {
                    var position = lights[i].transform.position;
                    return IsBelowEveryStart(position.y, playerSpawns)
                        ? position
                        : new Vector3(position.x, FloorHeight(playerSpawns, position.y), position.z);
                }
            }

            if (playerSpawns.Length == 0)
            {
                return Vector3.zero;
            }

            var sum = Vector3.zero;
            for (var i = 0; i < playerSpawns.Length; i++)
            {
                sum += playerSpawns[i].position;
            }

            return sum / playerSpawns.Length;
        }

        /// <summary>
        /// True when a height is under every player start — the shape of a building whose
        /// way out is down.
        /// <para>
        /// No tolerance and no threshold, because none is needed and any would be invented.
        /// The generator's EntranceLight hangs 2.8 m ABOVE its own floor, so on a map where
        /// the door and the starts share a storey this is false by 2.8 m; on §01's tower it
        /// is true by 23.45 m. There is nothing in between to tune.
        /// </para>
        /// <para>
        /// A scene with no player starts answers false — it has no descent to be at the
        /// bottom of, and the co-operative behaviour is what nothing-known should fall back to.
        /// </para>
        /// </summary>
        private static bool IsBelowEveryStart(float height, Transform[] playerSpawns)
        {
            for (var i = 0; i < playerSpawns.Length; i++)
            {
                if (height >= playerSpawns[i].position.y)
                {
                    return false;
                }
            }

            return playerSpawns.Length > 0;
        }

        private static float FloorHeight(Transform[] playerSpawns, float fallback)
        {
            return playerSpawns.Length > 0 ? playerSpawns[0].position.y : fallback;
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
