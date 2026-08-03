#nullable enable

using System.Collections;
using System.Collections.Generic;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Player;
using HorrorGame.Net;
using Mirror;
using UnityEngine;

namespace HorrorGame.Gameplay.Race
{
    /// <summary>
    /// Puts the person at the keyboard where §13 says their runner is standing.
    /// <para>
    /// <b>The one link between the two halves of the starting line.</b>
    /// <c>NetRaceStartPoints</c> registers B1's rim markers as Mirror start positions and
    /// teleports every connection's <c>NetPlayer</c> onto its own one, round-robin, the
    /// moment the descent's scene loads — that is the host's authoritative answer to
    /// "where is each runner". <c>MatchDirector.MovePlayerToSpawn</c> moves the local
    /// first-person rig to <c>map.PlayerSpawns[0]</c> — the same call on every machine,
    /// with no idea a party exists. The two run in the same frame and disagree, and the
    /// rig wins: <c>NetLocalRunner</c> binds the rig as the owner's view source, so within
    /// one send interval (1/30 s) the owner reports the rig's position and the avatar the
    /// host so carefully placed is dragged back to spawn 0. Twenty runners, one cell,
    /// nobody's fault and nothing red.
    /// </para>
    /// <para>
    /// <b>So the rig follows the host, not a seat number.</b> Deriving a marker locally
    /// from <c>RaceParty.LocalSeat</c> would be a second opinion about the same question,
    /// and the two would agree only for as long as both sides indexed the de-duplicated
    /// marker list identically — a coupling nobody would notice breaking. §13 already
    /// makes the host the authority and <c>NetPlayer</c> already replicates the answer, so
    /// this waits for it, moves the rig there, and gets out of the way. The seat is used
    /// for one thing only: a fallback when no answer arrives, so a broken session is a
    /// spread-out race rather than a pile.
    /// </para>
    /// <para>
    /// <b>Why a wait and not a call.</b> On the host the two <c>sceneLoaded</c> handlers
    /// run in one frame and <c>RaceLobby</c>'s is first — it subscribed when 시작 was
    /// pressed, before <c>OnStartServer</c> installed the other — so reading the position
    /// straight away reads the value from before the placement. On a client the answer is
    /// a SyncVar and arrives over a wire. One code path covers both by waiting for the
    /// answer to look like an answer: a position that is on a starting marker.
    /// </para>
    /// </summary>
    public static class RaceRunners
    {
        /// <summary>
        /// How near a marker the replicated position has to be before it counts as §13's
        /// answer, metres.
        /// <para>
        /// One map cell (<c>MapKitCatalogue.GridMetres</c> is 2.5 m) and no more: the
        /// closest pair of distinct rim markers in the generated building is exactly one
        /// cell apart — <c>NetRaceStartPoints</c> measured 2.50 m after dropping the
        /// duplicates — so a wider radius could not distinguish "placed on my marker" from
        /// "placed on my neighbour's", and this test only has to distinguish "placed" from
        /// "still at the origin", which is tens of metres away.
        /// </para>
        /// </summary>
        /// <summary>
        /// Metres a replicated position may sit from a marker and still count as standing
        /// on it.
        /// <para>
        /// It covers the quantised round trip a position takes over the wire, not the
        /// distance between two places — the markers are one 2.5 m cell apart, so this is
        /// deliberately under half of that. It used to be a full 2.5 m, which meant a
        /// runner was "on" its own marker and both of its neighbours at once; paired with
        /// a first-match loop that put two runners on one cell. Nearest-match makes the
        /// value less load-bearing, and keeping it small keeps a runner who is genuinely
        /// nowhere near the ring from being reported as on it.
        /// </para>
        /// </summary>
        private const float OnTheLineMetres = 1.0f;

        /// <summary>
        /// How long to wait for the host's placement before starting anyway, seconds.
        /// <para>
        /// Generous on purpose. The cost of waiting is that the player looks at the rim
        /// for another moment before they can run; the cost of giving up early is that
        /// they start somewhere the rest of the field does not think they are. Two seconds
        /// is sixty send intervals at <c>GameConstants.NetworkSendRate</c>, so a client
        /// that has not heard by then has a problem this class cannot fix.
        /// </para>
        /// </summary>
        private const float AnswerSecondsBudget = 2f;

        /// <summary>
        /// Metres within which two spawn markers are the same place.
        /// <para>
        /// A cell is 2.5 m, so half of one: two markers closer than this are inside the
        /// same corridor cell, and a runner put on either is standing where a runner put
        /// on the other is standing. Bigger would merge neighbouring cells and shrink the
        /// ring; smaller would keep counting a pile as a spread.
        /// </para>
        /// </summary>
        private const float SamePlaceMetres = 1.25f;

        /// <summary>Where the local rig was actually started, once <see cref="TakeTheStartLine"/> has finished.</summary>
        public static Vector3 LocalStart { get; private set; }

        /// <summary>
        /// Whether the rig was started on §13's answer rather than on the fallback. False
        /// after a descent means this machine and the rest of the field disagree about
        /// where its runner began.
        /// </summary>
        public static bool StartedOnTheHostsAnswer { get; private set; }

        /// <summary>Clears the last descent's result. Called when a session ends.</summary>
        public static void Clear()
        {
            LocalStart = Vector3.zero;
            StartedOnTheHostsAnswer = false;
        }

        /// <summary>
        /// Waits for §13's answer and moves the local rig onto it.
        /// <para>
        /// Driven by <c>RaceLobby</c> because it is a coroutine and needs a
        /// <c>MonoBehaviour</c> that outlives the scene load, which the lobby already is —
        /// adding a second <c>DontDestroyOnLoad</c> object to run one wait would be a
        /// second thing to tear down at the end of a session.
        /// </para>
        /// </summary>
        public static IEnumerator TakeTheStartLine()
        {
            Clear();

            if (!NetworkServer.active && !NetworkClient.active)
            {
                // A solo playtest or a test. MatchDirector's single-player placement is
                // complete on its own and there is no party to agree with.
                yield break;
            }

            if (!MatchMap.TryRead(out var map, out var failure) || map == null)
            {
                Debug.LogError(
                    "[Race] §01의 출발선을 읽지 못했다: " + failure
                    + " 이 기계의 주자는 다른 사람들이 보는 위치와 다른 곳에서 출발한다.");
                yield break;
            }

            var spawns = map.PlayerSpawns;
            var rig = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
            if (rig == null || spawns.Count == 0)
            {
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + AnswerSecondsBudget;
            var answer = Vector3.zero;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (TryReadHostsAnswer(spawns, out answer))
                {
                    StartedOnTheHostsAnswer = true;
                    break;
                }

                yield return null;
            }

            // The wait spans frames, and a player who pressed 나가기 during it has taken
            // the whole match scene with them. Re-checked rather than assumed, because a
            // MissingReferenceException out of a coroutine kills the coroutine silently.
            if (rig == null)
            {
                yield break;
            }

            if (!StartedOnTheHostsAnswer)
            {
                // §11's seat, used for the only thing a seat is any good for here: giving
                // a machine that heard nothing a different marker from everybody else's,
                // so a broken session is a spread-out race rather than a pile.
                var seat = RaceParty.Settled ? RaceParty.LocalSeat : 0;

                // De-duplicated first, and that is the whole of this fix. The generator
                // offers B1's entire outer ring rather than exactly twenty places
                // (DescentMap.PlaceStarts: "a cell only becomes a graph node if it is a
                // bend, a junction or an end"), and markers land on top of each other —
                // NetRaceStartPoints reports "28 distinct points from 36 markers" on the
                // shipped map. Indexing the raw 36 by seat therefore hands two seats the
                // same cell whenever they straddle a duplicated pair, which is exactly
                // what two real instances did: seat 0 and seat 1 both stood at
                // (23.8, 0.0, 6.3). The seat is here to spread a broken session out; a
                // list with duplicates in it defeats the one thing it is for.
                var distinct = DistinctPlaces(spawns);
                var fallback = distinct.Count > 0
                    ? distinct[((seat % distinct.Count) + distinct.Count) % distinct.Count]
                    : rig.transform.position;
                answer = fallback;

                Debug.LogWarning(
                    "[Race] §13의 배치가 " + AnswerSecondsBudget + "초 안에 오지 않았다 — 좌석 " + seat
                    + "번의 자리로 대신 출발한다 (표식 " + spawns.Count + "개 중 서로 다른 자리 "
                    + distinct.Count + "곳). 남들이 보는 이 주자의 위치와 어긋날 수 있다.");
            }

            PlaceRig(rig, answer);
            LocalStart = answer;

            Debug.Log(
                "[Race] §01 출발선 — 이 기계의 주자는 " + answer.ToString("0.0") + " 에서 출발한다"
                + (StartedOnTheHostsAnswer ? " (호스트가 정한 자리)." : " (대체 자리)."));

            ReportStartLine();
        }

        /// <summary>
        /// §13's answer for this machine, if it has arrived.
        /// <para>
        /// "Arrived" is a positive test — the replicated position is standing on one of the
        /// building's starting markers — rather than "is no longer the origin". The origin
        /// is where <c>NetRunner.Build</c> leaves a runner, and a negative test would also
        /// pass the instant anything else nudged the value.
        /// </para>
        /// </summary>
        private static bool TryReadHostsAnswer(IReadOnlyList<Transform> spawns, out Vector3 answer)
        {
            answer = Vector3.zero;

            var local = NetworkClient.localPlayer;
            if (local == null || !local.TryGetComponent(out NetPlayer player))
            {
                return false;
            }

            var candidate = player.NetworkedPosition;

            // NEAREST, not first-within-tolerance, and that distinction was worth two
            // runners. The rim's markers are one cell apart, the tolerance is one cell,
            // so a runner standing on marker #7 is also inside the tolerance of #6 and
            // #8 — and the old loop returned whichever came first in scene order. Two
            // real instances placed one cell apart both snapped to the same marker and
            // both reported (23.8, 0.0, 6.3), while the host's own placement log said
            // "2곳에 서로 다르게 (0명 겹침)". The placement was right; the reading was not.
            var bestIndex = -1;
            var bestSqr = float.PositiveInfinity;

            for (var i = 0; i < spawns.Count; i++)
            {
                var marker = spawns[i];
                if (marker == null)
                {
                    continue;
                }

                var flat = marker.position - candidate;
                flat.y = 0f;

                var sqr = flat.sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0 || bestSqr > OnTheLineMetres * OnTheLineMetres)
            {
                return false;
            }

            // The marker's own position and not the replicated one: the marker is where
            // the floor is, and the replicated value has been through a quantised round
            // trip. A rig dropped a few centimetres under the floor is a rig the
            // character controller spends its first step depenetrating out of.
            answer = spawns[bestIndex].position;
            return true;
        }

        /// <summary>
        /// Moves the rig, and tells the two things that own its placement that it moved.
        /// <para>
        /// The controller has to be off while the transform is written or it overwrites
        /// the move on its own next step — <c>MatchDirector.MovePlayerToSpawn</c> carries
        /// the same three lines for the same reason.
        /// </para>
        /// <para>
        /// Position only. <c>PlayerLook</c> writes <c>transform.rotation</c> from its own
        /// yaw every frame, so a heading set here would last exactly one frame — and there
        /// is no heading worth setting: <c>MapSketch</c> writes the rim markers with
        /// identity rotation, so turning the camera would be inventing a direction rather
        /// than restoring one.
        /// </para>
        /// </summary>
        private static void PlaceRig(PlayerMotor rig, Vector3 position)
        {
            var root = rig.transform;
            var controller = root.GetComponent<CharacterController>();

            if (controller != null)
            {
                controller.enabled = false;
            }

            root.position = position;

            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        /// <summary>
        /// Says out loud whether §11's field still has a body each. Host only.
        /// <para>
        /// <b>A count, because the failure is silent.</b>
        /// <c>NetRaceStartPoints.PlaceSpawnedRunners</c> skips a connection whose
        /// <c>identity</c> is null and reports only what it placed, so a party whose
        /// bodies did not survive the descent's scene load places <em>zero</em> runners
        /// and logs nothing at all — which is precisely the state this whole path was in
        /// before <c>RaceLobby.KeepBodiesAcrossTheLoad</c>. Counting the seats that still
        /// have something in them turns a silent zero into one line that names the number.
        /// </para>
        /// <para>
        /// It counts bodies rather than distinct positions, and the difference matters: by
        /// the time this runs every owner has been reporting for a moment, so live
        /// positions describe where people have walked to rather than where they were put.
        /// The distinct-marker count is <c>NetRaceStartPoints</c>' own measurement and is
        /// quoted from it rather than re-derived, so the two can never disagree.
        /// </para>
        /// </summary>
        /// <summary>
        /// The spawn markers, one per place rather than one per marker.
        /// <para>
        /// Two markers within <see cref="SamePlaceMetres"/> of each other are one place:
        /// a runner put on either is standing in the same cell as a runner put on the
        /// other, so handing them to two seats is handing two people one spot. Kept in
        /// marker order so the answer is stable across machines — every peer reads the
        /// same scene and must agree on which seat gets which place, or the fallback
        /// spreads the field on one machine and piles it on another.
        /// </para>
        /// </summary>
        /// <param name="spawns">§01's PlayerSpawn markers, in scene order.</param>
        private static List<Vector3> DistinctPlaces(IReadOnlyList<Transform> spawns)
        {
            var places = new List<Vector3>();

            for (var i = 0; i < spawns.Count; i++)
            {
                if (spawns[i] == null)
                {
                    continue;
                }

                var at = spawns[i].position;
                var seen = false;

                for (var j = 0; j < places.Count; j++)
                {
                    if ((places[j] - at).sqrMagnitude <= SamePlaceMetres * SamePlaceMetres)
                    {
                        seen = true;
                        break;
                    }
                }

                if (!seen)
                {
                    places.Add(at);
                }
            }

            return places;
        }

        private static void ReportStartLine()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            var seats = RaceParty.SeatConnectionIds;
            var bodiless = 0;
            var embodied = 0;

            for (var seat = 0; seat < seats.Length; seat++)
            {
                if (!NetworkServer.connections.TryGetValue(seats[seat], out var connection)
                    || connection == null
                    || connection.identity == null)
                {
                    bodiless++;
                    continue;
                }

                embodied++;
            }

            if (bodiless > 0)
            {
                Debug.LogError(
                    "[Race] §01 출발선이 완성되지 않았다 — " + seats.Length + "석 중 " + bodiless
                    + "명에게 몸이 없다. 씬 로드가 주자를 지웠다는 뜻이고, 그 사람들은 아무에게도 보이지 않는다. "
                    + "RaceLobby.KeepBodiesAcrossTheLoad 를 보라.");
                return;
            }

            Debug.Log(
                "[Race] §01 출발선 — " + embodied + "명 전원이 몸을 가지고 B1 외곽 고리에 섰다. "
                + "표식 " + NetRaceStartPoints.Found + "개 중 서로 다른 자리는 "
                + NetRaceStartPoints.Registered + "곳 (NetRaceStartPoints).");
        }
    }
}
