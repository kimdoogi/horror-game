#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;
using HorrorGame.Net;
using HorrorGame.Net.Host;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Net
{
    /// <summary>
    /// §13's network layer: what survives a real socket, and who is allowed to decide
    /// what.
    /// <para>
    /// <b>This fixture used to be built around one rule, and that rule is deleted.</b>
    /// §13 said "단서 내용 · 목표물 위치 — 호스트만 보유. 클라이언트에 보내면 메모리에서
    /// 읽힌다", and §03's whole structure — 「그 자리에서 보고, 기억해서, 말로 전달해야
    /// 한다」 — stopped existing the moment the answer was in a client's memory. So the
    /// objective's location was proved absent twice, structurally through
    /// <c>NetReplicationAudit</c> (deleted) walking every replicated surface, and byte for byte
    /// through a real Mirror server whose outgoing packets were searched for three
    /// floats.
    /// </para>
    /// <para>
    /// Six tests, the audit itself, the host-secret store and a deliberately leaky
    /// test double all went with §03's clue chain. <b>A race has no answer to hide</b>:
    /// it announces its destination — the middle of B8 — to all twenty runners at the
    /// start, on purpose, because the game is who gets there rather than who knows
    /// where it is. Hiding nothing is not a weakened guarantee; it is a different game.
    /// </para>
    /// <para>
    /// What is left is what a race actually asks of the wire: that a player's view
    /// survives it (pitch, and stamina only approximately for everybody else), that
    /// §11's lobby seats people, and that the host leaving ends the session for
    /// everyone.
    /// </para>
    /// </summary>
    public sealed class NetTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private RecordingTransport? _transport;
        private GameObject? _rig;

        [SetUp]
        public void SetUp()
        {
            NetSession.ResetForTests();

            _rig = new GameObject("NetTestRig");
            _transport = _rig.AddComponent<RecordingTransport>();
            Transport.active = _transport;

            // The game's own interest rules, not Mirror's default of "everyone sees
            // everything". Adding it here rather than per test is deliberate: a test
            // that forgot it would be testing a configuration the game never ships.
            _rig.AddComponent<HorrorInterestManagement>();
        }

        [TearDown]
        public void TearDown()
        {
            if (NetworkServer.active)
            {
                NetworkServer.Shutdown();
            }

            NetworkClient.Shutdown();
            NetworkServer.aoi = null;
            NetworkClient.aoi = null;
            NetworkManager.ResetStatics();

            for (var i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();

            if (_rig != null)
            {
                UnityEngine.Object.DestroyImmediate(_rig);
                _rig = null;
            }

            Transport.active = null;
            NetSession.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }
        // ------------------------------------------------------------------
        // §05 — what the table says to sync, and only that
        // ------------------------------------------------------------------

        /// <summary>
        /// §05 insists on pitch as well as yaw — "바닥·천장을 비추는 것도 신호" —
        /// so a beam aimed at the floor and one aimed at the ceiling must survive the
        /// round trip as different values.
        /// </summary>
        [Test]
        public void PitchSurvivesTheWireBecauseFloorAndCeilingAreDifferentSentences()
        {
            var floor = NetViewCompression.UnpackPitch(NetViewCompression.PackPitch(-60f));
            var ceiling = NetViewCompression.UnpackPitch(NetViewCompression.PackPitch(60f));

            Assert.That(floor, Is.LessThan(0f));
            Assert.That(ceiling, Is.GreaterThan(0f));

            // §05 calls the 45° peek a skill expressed in degrees of mouse movement.
            // The wire has to resolve far finer than that or the skill stops reading.
            var peek = NetViewCompression.UnpackYaw(NetViewCompression.PackYaw(GameConstants.PeekAngleDegrees));
            Assert.That(
                Mathf.Abs(peek - GameConstants.PeekAngleDegrees),
                Is.LessThan(1f),
                "§05 makes 몇 도 돌릴지가 곧 실력 — a wire that rounds the peek angle to the nearest degree would "
                + "quantise the skill away.");
        }

        /// <summary>
        /// §05: "스태미나 (주자) — 본인만 정확히, 남은 대략." The wire carries the
        /// approximation, not the number.
        /// </summary>
        [Test]
        public void OthersSeeStaminaOnlyApproximately()
        {
            Assert.That(
                NetPlayer.StaminaApproximationSteps,
                Is.EqualTo(Mathf.CeilToInt(GameConstants.SprintStaminaSeconds)),
                "The coarseness is derived from §05's sprint budget, not invented: one step per second of sprint.");

            Assert.That(
                NetPlayer.StaminaApproximationSteps,
                Is.LessThan(100),
                "§05 says teammates see 대략. A resolution fine enough to read a percentage off is not 대략.");
        }

        // ------------------------------------------------------------------
        // §11 — the seats, and only the seats
        // ------------------------------------------------------------------

        /// <summary>
        /// The lobby seats players and refuses one too many. That is the whole of what
        /// it decides now.
        /// <para>
        /// REPLACES TheLobbySeatsFourOfFiveRolesAndLeavesExactlyOneAbsent and
        /// TwoPlayersCannotTakeTheSameRole. Both were about §04's five 직업 — that four
        /// seats took four distinct ones and the fifth's absence "became the match's
        /// character", and that a duplicate claim was refused. §04 v1.1 deleted 직업:
        /// 「캐릭터는 다 똑같이 생겨도되지」. Twenty identical runners have nothing to
        /// claim and nothing to duplicate.
        /// </para>
        /// <para>
        /// The readiness assertion below is not decoration. <c>ServerSetReady</c> gated
        /// readiness on <c>seat.Role != RoleId.None</c>, and with roles deleted every
        /// seat held None for ever — so a lobby that compiled fine could never start a
        /// race. This is the test that would have caught that.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheLobbySeatsPlayersAndARunnerCanReadyWithoutPickingAnything()
        {
            NetworkServer.Listen(GameConstants.PlayersPerMatch);

            var lobby = SpawnLobby();
            yield return null;

            Assert.That(lobby.Seats.Count, Is.EqualTo(GameConstants.PlayersPerMatch));

            var conns = new NetworkConnectionToClient[GameConstants.PlayersPerMatch];
            for (var i = 0; i < conns.Length; i++)
            {
                conns[i] = new NetworkConnectionToClient(10 + i) { isAuthenticated = true };
                NetworkServer.AddConnection(conns[i]);

                Assert.That(lobby.TrySeat(conns[i].connectionId, "Player" + i), Is.EqualTo(i));
            }

            Assert.That(lobby.ServerSetReady(conns[0], true), Is.True);
            Assert.That(lobby.Seats[0].Ready, Is.True,
                "a runner readied up without picking anything, because there is nothing to pick. If this "
                + "fails, readiness is still gated on a §04 role and no race can ever start.");

            Assert.That(lobby.EveryoneReady, Is.False, "three seats have not readied.");

            for (var i = 1; i < conns.Length; i++)
            {
                lobby.ServerSetReady(conns[i], true);
            }

            Assert.That(lobby.EveryoneReady, Is.True);

            // One too many has nowhere to sit.
            var extra = new NetworkConnectionToClient(99) { isAuthenticated = true };
            NetworkServer.AddConnection(extra);
            Assert.That(lobby.TrySeat(extra.connectionId, "Extra"), Is.EqualTo(-1));
        }

        // ------------------------------------------------------------------
        // §13 — the host leaving ends the session
        // ------------------------------------------------------------------

        /// <summary>
        /// §13: "호스트 이탈 — 세션 종료. 마이그레이션 하지 않음." A client whose host
        /// vanishes is told the session is over, not put in a reconnect queue.
        /// </summary>
        [Test]
        public void TheHostLeavingEndsTheSessionForEveryone()
        {
            var reasons = new List<NetSessionEndReason>();
            NetSession.Ended += reasons.Add;

            // Bringing up a NetworkManager also brings up the platform layer, and a
            // machine with no Steam client running says so through the log. That is
            // the supported offline path (§14 steps 1–3), not a test failure.
            LogAssert.ignoreFailingMessages = true;

            var managerObject = new GameObject("Manager");
            _spawned.Add(managerObject);
            var manager = managerObject.AddComponent<HorrorGameNetworkManager>();

            // A client, not a host: NetworkServer is not active, so this is the
            // "the other machine went away" path.
            Assert.That(NetworkServer.active, Is.False);
            manager.OnClientDisconnect();

            Assert.That(reasons, Does.Contain(NetSessionEndReason.HostLeft));
            Assert.That(NetSession.Phase, Is.EqualTo(NetSessionPhase.Offline));
            // The HostSecrets assertion that stood here — "a client never held the answers,
            // and must not end up holding them on the way out either" — is deleted with the
            // store itself. There are no answers.
        }
        /// <summary>
        /// Builds a networked object the way Mirror needs it built.
        /// <para>
        /// The object starts inactive and is activated only once every component is
        /// on it. That is not tidiness: <c>NetworkIdentity.Awake</c> caches the
        /// <c>NetworkBehaviour</c>s it finds, and on an active object it runs the
        /// instant the identity is added — before any behaviour added after it
        /// exists. A behaviour missed that way still compiles, still ticks, and has a
        /// null <c>netIdentity</c>, which is a confusing crash rather than a clear
        /// one. A prefab never has this problem; a test that builds objects in code
        /// does.
        /// </para>
        /// </summary>
        private GameObject NewNetworkedObject(string name, Vector3 position, params Type[] behaviours)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.position = position;
            _spawned.Add(go);

            for (var i = 0; i < behaviours.Length; i++)
            {
                go.AddComponent(behaviours[i]);
            }

            go.AddComponent<NetworkIdentity>();
            go.SetActive(true);
            return go;
        }

        private GameObject SpawnPlayer(NetworkConnectionToClient conn, Vector3 position)
        {
            var go = NewNetworkedObject("Player" + conn.connectionId, position, typeof(NetPlayer));
            NetInterestScope.Apply(go, NetInterestClass.Perception);

            NetworkServer.AddPlayerForConnection(conn, go);
            return go;
        }

        private GameObject SpawnSecret(string name, Vector3 position)
        {
            var go = NewNetworkedObject(name, position);
            NetInterestScope.Apply(go, NetInterestClass.Secret);

            NetworkServer.Spawn(go);
            return go;
        }

        private NetLobby SpawnLobby()
        {
            var go = NewNetworkedObject("Lobby", Vector3.zero, typeof(NetLobby));
            NetworkServer.Spawn(go);
            return go.GetComponent<NetLobby>();
        }

        /// <summary>
        /// Fails if a position's three floats appear anywhere in what the client was
        /// sent, in any byte alignment.
        /// <para>
        /// Searching for the raw little-endian float bytes rather than parsing the
        /// protocol on purpose: the point is not "Mirror's spawn message does not
        /// contain it", it is "nothing at all does", including a message format
        /// nobody has written yet.
        /// </para>
        /// </summary>
        private static void AssertAbsent(IReadOnlyList<byte[]> sent, Vector3 secret, string what)
        {
            var needles = new List<byte[]>();

            // The whole vector, contiguous, as Mirror's WriteVector3 lays it out.
            var triple = new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(secret.x), 0, triple, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(secret.y), 0, triple, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(secret.z), 0, triple, 8, 4);
            needles.Add(triple);

            // And each axis on its own, so a leak that reorders, splits or repacks the
            // components is caught too. Zero is skipped — its four zero bytes occur in
            // any protocol and would make this assertion meaningless rather than
            // strict.
            AddIfDistinctive(needles, secret.x);
            AddIfDistinctive(needles, secret.y);
            AddIfDistinctive(needles, secret.z);

            for (var packet = 0; packet < sent.Count; packet++)
            {
                for (var needle = 0; needle < needles.Count; needle++)
                {
                    Assert.That(
                        IndexOf(sent[packet], needles[needle]),
                        Is.LessThan(0),
                        "§13: 클라이언트에 보내면 메모리에서 읽힌다. " + what + " (" + secret
                        + ") was found in packet " + packet + " of " + sent.Count + " (" + sent[packet].Length
                        + " bytes) sent to the client. §03's 'see it, remember it, say it out loud' does not survive "
                        + "this: the answer is now in client memory whether or not any UI shows it.\n"
                        + Hex(sent[packet]));
                }
            }
        }

        /// <summary>
        /// The control case: a position that <em>is</em> legitimately sent must be
        /// found by the same search that must not find the objective.
        /// </summary>
        private static void AssertPresent(IReadOnlyList<byte[]> sent, Vector3 expected, string what)
        {
            var triple = new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(expected.x), 0, triple, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(expected.y), 0, triple, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(expected.z), 0, triple, 8, 4);

            for (var packet = 0; packet < sent.Count; packet++)
            {
                if (IndexOf(sent[packet], triple) >= 0)
                {
                    return;
                }
            }

            Assert.Fail(
                "Could not find " + what + " (" + expected + ") in any of the " + sent.Count
                + " packets sent to the client. The byte search therefore proves nothing about the objective "
                + "either — fix the search, not the assertion.");
        }

        private static void AddIfDistinctive(ICollection<byte[]> needles, float component)
        {
            if (Mathf.Approximately(component, 0f))
            {
                return;
            }

            needles.Add(BitConverter.GetBytes(component));
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i + needle.Length <= haystack.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 3);
            for (var i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("X2")).Append(' ');
            }

            return builder.ToString();
        }

        /// <summary>
        /// A world with no navigation data: every point is reachable, nothing is
        /// occluded, nothing is lit. ARCHITECTURE §5 asks for the ugly cases — this is
        /// the "probe that reports nothing useful" one, and the layout has to survive
        /// it rather than refuse to build.
        /// </summary>
        private sealed class FlatProbe : IWorldProbe
        {
            public bool HasLineOfSight(Vec3 from, Vec3 to) => true;

            public float NavigableDistance(Vec3 from, Vec3 to) => Vec3.Distance(from, to);

            public bool TryGetNextPathPoint(Vec3 from, Vec3 to, out Vec3 next)
            {
                next = to;
                return true;
            }

            public FloorMaterial SampleFloor(Vec3 position) => FloorMaterial.Concrete;

            public int ZoneIdAt(Vec3 position) => 0;

            public Vec3 SnapToNavigable(Vec3 desired) => desired;

            public bool IsAreaLit(Vec3 position) => false;
        }

        /// <summary>
        /// A transport that connects nothing and remembers everything.
        /// <para>
        /// Standing in for a socket rather than opening one, because the test's
        /// subject is what Mirror decides to send, and a real transport would only
        /// add a way for the test to be flaky.
        /// </para>
        /// </summary>
        private sealed class RecordingTransport : Transport
        {
            private readonly Dictionary<int, List<byte[]>> _sent = new Dictionary<int, List<byte[]>>();

            public IReadOnlyList<byte[]> BytesSentTo(int connectionId) =>
                _sent.TryGetValue(connectionId, out var packets) ? packets : new List<byte[]>();

            public override bool Available() => true;

            public override bool ClientConnected() => false;

            public override void ClientConnect(string address)
            {
            }

            public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable)
            {
            }

            public override void ClientDisconnect()
            {
            }

            public override Uri ServerUri() => new Uri("memory://test");

            public override bool ServerActive() => true;

            public override void ServerStart()
            {
            }

            public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable)
            {
                if (!_sent.TryGetValue(connectionId, out var packets))
                {
                    packets = new List<byte[]>();
                    _sent[connectionId] = packets;
                }

                var copy = new byte[segment.Count];
                Array.Copy(segment.Array!, segment.Offset, copy, 0, segment.Count);
                packets.Add(copy);
            }

            public override void ServerDisconnect(int connectionId)
            {
            }

            public override string ServerGetClientAddress(int connectionId) => "test";

            public override void ServerStop()
            {
            }

            public override int GetMaxPacketSize(int channelId = Channels.Reliable) => 1200;

            public override void Shutdown()
            {
            }
        }

    }
}
