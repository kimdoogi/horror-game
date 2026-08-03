#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.Net
{
    /// <summary>
    /// §01's starting line, handed to Mirror: twenty runners on the rim of B1, one per
    /// marker, none of them inside anybody else.
    /// <para>
    /// <b>What this replaces.</b> <c>HorrorGameNetworkManager.TryAddRunner</c> has always
    /// called <c>GetStartPosition()</c>, and that method has always returned null, because
    /// <c>NetworkManager.startPositions</c> is filled by <c>NetworkStartPosition</c>
    /// components and this project has never had one — the map is generated, and the
    /// generator writes bare transforms. So every runner spawned at the origin: twenty
    /// people standing inside each other at (0, 0, 0), in a game whose first sentence is
    /// that they start spread round a ring.
    /// </para>
    /// <para>
    /// <b>Names, spelled out.</b> <c>MatchMap.MapRootName</c>, <c>MarkerRootName</c> and
    /// <c>PlayerSpawnGroup</c> hold these three strings, and that class lives in the
    /// predefined assembly, which an <c>.asmdef</c> cannot reference. They are quoted here
    /// for the same reason <c>RaceLobby.NetBootstrapObjectName</c> quotes the bootstrap
    /// anchor's name, and the cost is the same: a rename in the generator has to be made
    /// in two places. <see cref="Rescan"/> returning zero is what that mistake looks like,
    /// and it says so in the log rather than silently piling everybody on the origin
    /// again.
    /// </para>
    /// <para>
    /// <b>Duplicates are dropped, and that is a measured decision.</b>
    /// <c>MapSketch.BuildMarkers</c> writes one PlayerSpawn marker per rim <em>cell</em>
    /// but positions it at the <em>graph node</em> the cell resolves to, and several
    /// adjacent cells share a node. Measured on <c>Map_FirstSketch_Solo.unity</c>: 36
    /// markers, 28 distinct positions, eight of them doubled — and the first two in
    /// sibling order, <c>PlayerSpawn_10</c> and <c>PlayerSpawn_11</c>, are the same point
    /// to the millimetre. Round-robin over the raw list therefore puts runners 1 and 2
    /// exactly 0.00 m apart: the origin bug again, moved to the rim. After the drop the
    /// closest pair among the first twenty is 2.50 m — one map cell — which is four times
    /// the width of two player capsules.
    /// </para>
    /// <para>
    /// <b><c>Vector3</c> equality, deliberately, and no tolerance of this file's own.</b>
    /// Two markers that resolved to one graph node carry identical positions; nothing
    /// rounds on the way here. Unity's <c>==</c> already allows about 10 µm, which is five
    /// orders of magnitude under the smallest real gap in this building (one cell, 2.5 m)
    /// and five orders over the largest fake one (zero). Inventing a threshold in between
    /// would be a tuned number with nothing to tune it against.
    /// </para>
    /// </summary>
    public static class NetRaceStartPoints
    {
        /// <summary>Root object the scene generator hangs the map from. Mirrors <c>MatchMap.MapRootName</c>.</summary>
        public const string MapRootName = "Map";

        /// <summary>Child of the root holding every marker group. Mirrors <c>MatchMap.MarkerRootName</c>.</summary>
        public const string MarkerRootName = "Markers";

        /// <summary>Marker group of §01's entry points. Mirrors <c>MatchMap.PlayerSpawnGroup</c>.</summary>
        public const string PlayerSpawnGroupName = "PlayerSpawns";

        private static bool _installed;

        /// <summary>
        /// How many distinct starting points the last <see cref="Rescan"/> registered.
        /// Below <see cref="GameConstants.RaceRunnersMax"/> means §11's field cannot all
        /// be given a place of their own.
        /// </summary>
        public static int Registered { get; private set; }

        /// <summary>
        /// How many markers the last <see cref="Rescan"/> found before duplicates were
        /// dropped. Reported so the gap between this and <see cref="Registered"/> is
        /// visible rather than inferred — it is a defect in the generator, not in the
        /// network layer, and hiding it here would be how it survives.
        /// </summary>
        public static int Found { get; private set; }

        /// <summary>
        /// Starts watching for the map. Called from
        /// <c>HorrorGameNetworkManager.OnStartServer</c>.
        /// <para>
        /// A scene-load hook and not a one-off scan, because on the shipped path the
        /// server starts in the bootstrap scene: §11's lobby claims §13's authority
        /// several seconds before <c>DescentMap</c> has built anything, and runners are
        /// spawned in between (a client sends <c>ReadyMessage</c> exactly once, at
        /// connect). So the rim does not exist when the first runner does, and the
        /// placement has to happen twice — once when the markers appear, and once per
        /// runner that arrives afterwards.
        /// </para>
        /// <para>
        /// It also scans immediately, for the case where the map is already up: a server
        /// started from inside the match scene, a tool, or a test.
        /// </para>
        /// </summary>
        public static void Install()
        {
            if (!_installed)
            {
                _installed = true;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            Rescan();
        }

        /// <summary>Stops watching and forgets the markers. Called from <c>HorrorGameNetworkManager.OnStopServer</c>.</summary>
        public static void Uninstall()
        {
            if (_installed)
            {
                _installed = false;
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }

            Clear();
        }

        /// <summary>
        /// Drops every registered start position.
        /// <para>
        /// <c>NetworkManager.startPositions</c> is a static list that outlives a session,
        /// and the transforms in it belong to a scene that has been unloaded. Mirror's own
        /// <c>GetStartPosition</c> prunes destroyed entries, but only destroyed ones — a
        /// second match on a second layout would otherwise start with the first
        /// building's ring still in the list.
        /// </para>
        /// </summary>
        public static void Clear()
        {
            NetworkManager.startPositions.Clear();
            NetworkManager.startPositionIndex = 0;
            Registered = 0;
            Found = 0;
        }

        /// <summary>
        /// Reads the loaded scene's PlayerSpawn markers and registers the distinct ones.
        /// </summary>
        /// <returns>How many distinct starting points are now registered.</returns>
        public static int Rescan()
        {
            Clear();

            var markers = FindMarkerGroup();
            if (markers == null)
            {
                return 0;
            }

            var accepted = new List<Vector3>(markers.childCount);

            for (var i = 0; i < markers.childCount; i++)
            {
                var marker = markers.GetChild(i);
                Found++;

                if (IsDuplicate(accepted, marker.position))
                {
                    continue;
                }

                accepted.Add(marker.position);

                // Mirror sorts the list by sibling index on every insert, so the order
                // runners are handed out in is the map's own hierarchy order and is the
                // same on every machine. Nothing here depends on that — a start position
                // is only ever chosen on the host — but a deterministic ring is what makes
                // a bug report about "where I started" reproducible.
                NetworkManager.RegisterStartPosition(marker);
            }

            Registered = accepted.Count;

            if (Registered < GameConstants.RaceRunnersMax)
            {
                Debug.LogWarning(
                    "[Net] The map offers " + Registered + " distinct starting points and §11's field is "
                    + GameConstants.RaceRunnersMax + ". Mirror will wrap round and two runners will share a "
                    + "marker. DescentMap.PlaceStarts offers the whole rim precisely so this cannot happen, so "
                    + "this is a map that was generated wrong or a group that was renamed.");
            }
            else
            {
                Debug.Log(
                    "[Net] §01's starting line: " + Registered + " distinct points from " + Found
                    + " markers on B1's rim, for a field of up to " + GameConstants.RaceRunnersMax + ".");
            }

            return Registered;
        }

        /// <summary>
        /// Moves every runner already spawned onto a starting point. Host only.
        /// <para>
        /// This is the other half of the lobby's timing problem.
        /// <c>HorrorGameNetworkManager.OnServerReady</c> gives a connection a body the
        /// moment it arrives, which is before the descent's scene exists, so those bodies
        /// stand at the origin holding a position nobody has corrected. When the map
        /// finally loads, the ring appears and this puts them on it.
        /// </para>
        /// <para>
        /// Through <see cref="NetPlayer.TeleportTo"/> and not by writing the transform:
        /// that method exists exactly for a move the player did not run
        /// (§03's 왕복, §01's 투하구, a spawn), and it clears the report clock so the
        /// host's own speed clamp does not spend the next second dragging the avatar back
        /// toward the origin at 5.6 m/s.
        /// </para>
        /// </summary>
        /// <returns>How many runners were placed.</returns>
        /// <summary>
        /// Metres within which two starting places are one place.
        /// <para>
        /// Half a 2.5 m cell: two runners closer than this are standing in the same
        /// corridor cell, whatever their coordinates say. Matches
        /// <c>RaceRunners.SamePlaceMetres</c>, which asks the same question of the same
        /// markers on the other side of the wire — the two must not disagree about what
        /// counts as one place, or the host says the field is spread while a client
        /// reports a pile.
        /// </para>
        /// </summary>
        private const float SamePlaceMetres = 1.25f;

        public static int PlaceSpawnedRunners()
        {
            if (!NetworkServer.active || Registered == 0)
            {
                return 0;
            }

            var manager = NetworkManager.singleton;
            if (manager == null)
            {
                return 0;
            }

            var placed = 0;
            var bodiless = 0;
            var unplaced = 0;
            var taken = new List<Vector3>();
            var collisions = 0;

            foreach (var connection in NetworkServer.connections.Values)
            {
                var identity = connection?.identity;
                if (identity == null || !identity.TryGetComponent(out NetPlayer player))
                {
                    bodiless++;
                    continue;
                }

                var start = manager.GetStartPosition();
                if (start == null)
                {
                    unplaced++;
                    continue;
                }

                // §11 gives twenty runners twenty places, and this is where that either
                // happens or quietly does not. Two real instances once put both runners
                // on one cell and the only symptom was two identical coordinates in two
                // log files nobody was diffing — so the placement counts its own
                // collisions and says so, rather than leaving it to be noticed.
                for (var i = 0; i < taken.Count; i++)
                {
                    var apart = taken[i] - start.position;
                    apart.y = 0f;
                    if (apart.sqrMagnitude <= SamePlaceMetres * SamePlaceMetres)
                    {
                        collisions++;
                        break;
                    }
                }

                taken.Add(start.position);
                player.TeleportTo(start.position);

                // Rotation is set on the host's copy only, and that is not an oversight:
                // §05's replicated view angle is the owner's yaw, reported by
                // CmdReportView, so a facing the host chose would be overwritten by the
                // player's own mouse within one tick anyway. It is written here so the
                // host's own avatar and anything that reads a transform before the first
                // report — a camera, a screenshot tool — see the marker's facing rather
                // than identity.
                identity.transform.rotation = start.rotation;
                placed++;
            }

            if (collisions > 0)
            {
                Debug.LogError(
                    "[Net] §11 · " + collisions + "명이 다른 주자와 같은 자리에 놓였다. GetStartPosition 이 "
                    + "같은 표식을 두 번 돌려줬다는 뜻이다 — playerSpawnMethod 가 RoundRobin 인지, "
                    + "등록된 자리가 " + Registered + "곳뿐이 아닌지 보라. 스무 명이면 스무 명이 겹친다.");
            }

            if (bodiless > 0 || unplaced > 0)
            {
                Debug.LogError(
                    "[Net] §01 출발선: 몸이 없는 연결 " + bodiless + "개, 자리를 못 받은 주자 "
                    + unplaced + "명. 몸이 없으면 OnServerReady 가 그 연결에 주자를 못 만든 것이고, "
                    + "자리를 못 받았으면 등록된 표식이 " + Registered + "곳뿐이다.");
            }

            if (placed > 0)
            {
                Debug.Log(
                    "[Net] Placed " + placed + " runner(s) on B1's rim now that the building exists — "
                    + taken.Count + "곳에 서로 다르게 (" + collisions + "명 겹침).");
            }

            return placed;
        }

        /// <summary>
        /// The marker group in whichever scene is loaded, or null.
        /// <para>
        /// <c>GameObject.Find</c> on the root and <c>Transform.Find</c> below it rather
        /// than a path in one call: the root is the only name that has to be unique, and a
        /// path would also match a differently-shaped hierarchy that happened to spell the
        /// same three words.
        /// </para>
        /// </summary>
        private static Transform? FindMarkerGroup()
        {
            var root = GameObject.Find(MapRootName);
            if (root == null)
            {
                return null;
            }

            var markers = root.transform.Find(MarkerRootName);
            if (markers == null)
            {
                Debug.LogWarning(
                    "[Net] '" + MapRootName + "' has no '" + MarkerRootName + "' child, so §01's starting line "
                    + "cannot be read and every runner will spawn at the origin.");
                return null;
            }

            var group = markers.Find(PlayerSpawnGroupName);
            if (group == null)
            {
                Debug.LogWarning(
                    "[Net] No '" + PlayerSpawnGroupName + "' group under " + MapRootName + "/" + MarkerRootName
                    + ". §01 starts the field on the rim of B1 and there is nothing here to start them on.");
            }

            return group;
        }

        private static bool IsDuplicate(List<Vector3> accepted, Vector3 candidate)
        {
            for (var i = 0; i < accepted.Count; i++)
            {
                if (accepted[i] == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Only the host ever reads a start position — GetStartPosition is called from
            // AddPlayerForConnection and nowhere else — so a client loading the descent
            // has nothing to do here. The guard also keeps a subscription that outlived
            // its session (a fixture that calls NetworkServer.Shutdown directly never
            // reaches OnStopServer) from touching a later scene's markers.
            if (!NetworkServer.active)
            {
                return;
            }

            if (Rescan() > 0)
            {
                PlaceSpawnedRunners();
            }
        }
    }
}
