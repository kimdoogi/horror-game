#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
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

        /// <summary>Marker group holding the single §07 초저녁 monster start.</summary>
        public const string MonsterSpawnGroup = "MonsterSpawns";

        /// <summary>
        /// Name prefix of the light the generator leaves burning at the 출입구. It is the
        /// only object in the scene that marks the way out, so it is how the surface is
        /// located. See <c>MapSceneBuilder.BuildLight</c>.
        /// </summary>
        public const string EntranceLightPrefix = "EntranceLight";

        private readonly Transform[] _candidateSites;
        private readonly Transform[] _lootSpawns;
        private readonly Transform[] _playerSpawns;

        private MatchMap(
            SiteCatalog catalog,
            Transform[] candidateSites,
            Transform[] lootSpawns,
            Transform[] playerSpawns,
            Transform? monsterSpawn,
            Vector3 entrance,
            int zoneCount)
        {
            Catalog = catalog;
            _candidateSites = candidateSites;
            _lootSpawns = lootSpawns;
            _playerSpawns = playerSpawns;
            MonsterSpawn = monsterSpawn;
            Entrance = entrance;
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

        /// <summary>Where §07's 초저녁 monster starts — the point furthest by walking from the 출입구.</summary>
        public Transform? MonsterSpawn { get; }

        /// <summary>
        /// The 출입구. §01's loop turns here and §02's win condition is reaching it, so
        /// the whole surface half of the game is measured from this point.
        /// </summary>
        public Vector3 Entrance { get; }

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
        public bool IsOnSurface(Vector3 position)
        {
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

            var sites = OutsideTheApron(entrance, ChildrenOf(markers, CandidateSiteGroup), CandidateSiteGroup);
            var loot = OutsideTheApron(entrance, ChildrenOf(markers, LootSpawnGroup), LootSpawnGroup);

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
                monsterSpawns.Length > 0 ? monsterSpawns[0] : null,
                entrance,
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
        /// </summary>
        private static Transform[] OutsideTheApron(Vector3 entrance, Transform[] markers, string groupName)
        {
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
        /// </summary>
        private static Vector3 FindEntrance(Transform root, Transform[] playerSpawns)
        {
            var lights = root.GetComponentsInChildren<Light>(includeInactive: true);
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i].name.StartsWith(EntranceLightPrefix, StringComparison.Ordinal))
                {
                    var position = lights[i].transform.position;
                    return new Vector3(position.x, FloorHeight(playerSpawns, position.y), position.z);
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

        private static float FloorHeight(Transform[] playerSpawns, float fallback)
        {
            return playerSpawns.Length > 0 ? playerSpawns[0].position.y : fallback;
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
