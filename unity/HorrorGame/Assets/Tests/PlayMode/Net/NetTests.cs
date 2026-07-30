#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Roles;
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
    /// The guard on §13's one hard rule.
    /// <para>
    /// §13: "단서 내용 · 목표물 위치 — 호스트만 보유. 클라이언트에 보내면 메모리에서
    /// 읽힌다." ARCHITECTURE §4 spells out why that is not a performance note:
    /// §03's whole structure — "그 자리에서 보고, 기억해서, 말로 전달해야 한다" —
    /// stops existing the moment the answer is in a client's memory, and sending it
    /// "but only showing it when close" is the same as sending it.
    /// </para>
    /// <para>
    /// So the objective's location is proved absent twice, on purpose, because the
    /// two proofs fail in different ways:
    /// </para>
    /// <list type="number">
    /// <item><b>Structurally.</b> <see cref="NetReplicationAudit"/> walks every
    /// replicated surface in the Net assembly and shows that no type reachable from
    /// one could carry an answer. This catches a leak the day somebody writes it,
    /// even if no test exercises the code path.</item>
    /// <item><b>Byte for byte.</b> A real Mirror server runs with a real connection,
    /// and every byte the transport is asked to send to that client is captured and
    /// searched for the objective's coordinates. This catches a leak that slips past
    /// the type system — a position packed into three floats in a message, say.</item>
    /// </list>
    /// </summary>
    public sealed class NetTests
    {
        /// <summary>
        /// Where the test's objective is put, in metres. Every component is non-zero
        /// and exactly representable, so its bytes are distinctive enough to search a
        /// packet stream for without matching padding by accident.
        /// </summary>
        private static readonly Vector3 ObjectiveTruth = new Vector3(-73.5f, -12.25f, 41.125f);

        /// <summary>
        /// Where the player stands: on the surface, further from every secret than any
        /// light in the game reaches. §03 makes walking there the cost of knowing.
        /// </summary>
        private static readonly Vector3 SurfaceSpawn = new Vector3(0f, 0f, 300f);

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private RecordingTransport? _transport;
        private GameObject? _rig;

        [SetUp]
        public void SetUp()
        {
            NetSession.ResetForTests();
            HostSecrets.Clear();

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
            HostSecrets.Clear();
            NetSession.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // §13 — the objective's location never leaves the host
        // ------------------------------------------------------------------

        /// <summary>
        /// No replicated surface in the Net assembly can name a type that carries
        /// §03's answers.
        /// <para>
        /// This is the proof that survives refactoring. A behavioural test only
        /// covers the code it runs; this covers every <c>[SyncVar]</c>,
        /// <c>[Command]</c>, <c>[ClientRpc]</c>, <c>[TargetRpc]</c> and sync
        /// collection that exists, including ones written next year by somebody who
        /// has not read §13.
        /// </para>
        /// </summary>
        [Test]
        public void NoReplicatedSurfaceCanCarryAnAnswer()
        {
            var violations = NetReplicationAudit.Scan();

            Assert.That(
                violations,
                Is.Empty,
                "§13: 단서 내용 · 목표물 위치 — 호스트만 보유. Every entry below is a way the answer could reach a "
                + "client, and §03's 'remember it and say it out loud' constraint does not survive any of them.\n"
                + string.Join("\n", violations));
        }

        /// <summary>
        /// The audit is not vacuous: a deliberately leaky assembly must fail it.
        /// <para>
        /// Without this, <see cref="NoReplicatedSurfaceCanCarryAnAnswer"/> would pass
        /// just as happily if the scanner were broken and found nothing anywhere,
        /// which is the failure mode a guard test is most likely to develop.
        /// </para>
        /// </summary>
        [Test]
        public void TheAuditCatchesALeakWhenThereIsOne()
        {
            var violations = NetReplicationAudit.Scan(typeof(LeakyForTesting).Assembly);

            Assert.That(
                violations,
                Is.Not.Empty,
                "The audit found nothing in an assembly that deliberately contains a leak, so a passing audit "
                + "of HorrorGame.Net would prove nothing.");

            var joined = string.Join("\n", violations);
            Assert.That(
                joined,
                Does.Contain(nameof(SiteLabel)).Or.Contain(nameof(ClueGlyph)).Or.Contain("ClueReport"),
                "The audit noticed something, but not the clue type that was planted:\n" + joined);
        }

        /// <summary>
        /// The bytes themselves. A real server, a real connection, a real spawn — and
        /// the objective's coordinates appear nowhere in what the client is sent.
        /// <para>
        /// The objective is placed at a position only the host ever learns, through
        /// <c>ObjectiveResolver</c>'s one-shot push. Its prop is then spawned into the
        /// world at that position, exactly as a real match would, with the player
        /// standing far away. Everything Mirror hands the transport for that
        /// connection is captured and searched for the three floats.
        /// </para>
        /// <para>
        /// This is the case ARCHITECTURE §4 warns about specifically: it is not
        /// enough for the UI to hide the objective until the player is close, because
        /// the bytes are readable either way. The interest manager's answer is that
        /// the object is not sent at all until a light could reach it (§03: "어둠 =
        /// 목표의 잠금장치").
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ObjectiveLocationIsAbsentFromEveryByteAClientReceives()
        {
            var authority = BuildHostAuthority(out var seed);
            Assert.That(HostSecrets.Install(authority, isServer: true), Is.True);

            NetworkServer.Listen(GameConstants.PlayersPerMatch);

            var conn = new NetworkConnectionToClient(1);
            NetworkServer.AddConnection(conn);
            conn.isAuthenticated = true;

            // The player is on the surface, nowhere near anything — the whole point of
            // §03's 왕복 is that they have to walk to the answer.
            var player = SpawnPlayer(conn, SurfaceSpawn);
            Assert.That(player, Is.Not.Null);

            // The host builds the level: the objective's position leaves the resolver
            // once, into the spawner, and is never stored anywhere else.
            var placedAt = Vector3.positiveInfinity;
            var placed = authority.TryPlaceObjective(where =>
            {
                placedAt = where;
                SpawnSecret("Objective", where);
            });

            Assert.That(placed, Is.True, "The host must be able to place the objective exactly once.");
            Assert.That(
                placedAt,
                Is.EqualTo(ObjectiveTruth),
                "The fixture places the objective at a known point so the byte search has something to look for. "
                + "Seed " + seed + ".");

            // Clue props too: §03 puts the chain in the dangerous places, and their
            // positions are as much a hint as the objective's.
            var clueIds = authority.ClueIds();
            for (var i = 0; i < clueIds.Length; i++)
            {
                if (authority.TryGetMarkerPosition(clueIds[i], out var markerPosition))
                {
                    SpawnSecret("Clue" + clueIds[i], markerPosition);
                }
            }

            // Let Mirror actually broadcast. Mirror's own player-loop hooks run the
            // server's late update, so yielding frames is what flushes the batcher —
            // and flushing through the real path is the point, since the test is
            // about what the transport is handed, not what a helper thinks it would
            // be handed.
            for (var frame = 0; frame < 20; frame++)
            {
                yield return null;
            }

            var sent = _transport!.BytesSentTo(conn.connectionId);
            Assert.That(sent.Count, Is.GreaterThan(0), "The test proves nothing if the server never sent anything.");

            // First, prove the search works. The client's own avatar is spawned to it,
            // so its position must be findable by exactly the method used below. Without
            // this, "not found" could just as easily mean "the detector is broken" —
            // which is how a guard test quietly stops guarding.
            AssertPresent(sent, SurfaceSpawn, "the client's own spawn position");

            AssertAbsent(sent, ObjectiveTruth, "the objective's position");

            for (var i = 0; i < clueIds.Length; i++)
            {
                if (authority.TryGetMarkerPosition(clueIds[i], out var markerPosition))
                {
                    AssertAbsent(sent, markerPosition, "clue " + clueIds[i] + "'s position");
                }
            }
        }

        /// <summary>
        /// The complement: a player standing in the light does receive it.
        /// <para>
        /// Without this, the previous test would also pass if interest management
        /// simply never replicated anything, which would be a broken game rather than
        /// a secure one. §03 locks the objective behind light — it does not delete it.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ObjectiveIsReplicatedOnceAPlayerIsCloseEnoughToLightIt()
        {
            NetworkServer.Listen(GameConstants.PlayersPerMatch);

            var far = new NetworkConnectionToClient(1) { isAuthenticated = true };
            var near = new NetworkConnectionToClient(2) { isAuthenticated = true };
            NetworkServer.AddConnection(far);
            NetworkServer.AddConnection(near);

            SpawnPlayer(far, SurfaceSpawn);
            SpawnPlayer(near, ObjectiveTruth + Vector3.forward);

            var objective = SpawnSecret("Objective", ObjectiveTruth);
            yield return null;

            var identity = objective.GetComponent<NetworkIdentity>();
            NetworkServer.RebuildObservers(identity, false);
            yield return null;

            Assert.That(
                identity.observers.ContainsKey(near.connectionId),
                Is.True,
                "A player standing next to the objective must be sent it — §03 locks it behind light, it does not "
                + "remove it from the world.");

            Assert.That(
                identity.observers.ContainsKey(far.connectionId),
                Is.False,
                "A player on the surface is outside every light in the game (§03's 어둠 = 목표의 잠금장치) and must "
                + "not be sent the objective.");

            Assert.That(
                NetInterestScope.SecretRange,
                Is.LessThan(NetInterestScope.PerceptionRange),
                "§03's lock is tighter than perception, or it is not a lock: a secret must go dark before a "
                + "teammate does.");
        }

        /// <summary>
        /// The host answers a read with a sentence and nothing else.
        /// <para>
        /// ARCHITECTURE §4: "the host replies with the rendered glyph for
        /// <em>that</em> clue only." The reply's type is checked as much as its
        /// content — a <c>string</c> cannot be recombined with another
        /// <c>string</c> into a location the way two <c>SiteLabel</c>s could, which is
        /// the property §03's 왕복 depends on.
        /// </para>
        /// </summary>
        [Test]
        public void TheHostAnswersAReadWithARenderedLineAndNothingStructured()
        {
            var authority = BuildHostAuthority(out _);

            var anyLegible = false;
            var ids = authority.ClueIds();

            for (var i = 0; i < ids.Length; i++)
            {
                // §03's ideal read: held long, well lit, straight on, unworn.
                var legible = authority.TryRenderRead(
                    ids[i],
                    GameConstants.ClueConfidentReadSeconds,
                    1f,
                    0f,
                    0f,
                    out var line);

                if (!legible)
                {
                    continue;
                }

                anyLegible = true;
                Assert.That(line, Is.Not.Empty);
                Assert.That(
                    line,
                    Does.Not.Contain(ObjectiveTruth.x.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    "A rendered clue line must never contain a coordinate. §03's clues narrow the search; they do "
                    + "not answer it.");
            }

            Assert.That(anyLegible, Is.True, "A perfect read of at least one clue must produce a line.");

            var reply = typeof(NetClueTerminal).GetMethod(
                "TargetLine",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.That(reply, Is.Not.Null, "NetClueTerminal must still have the reply this test is about.");

            var parameters = reply!.GetParameters();
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(NetworkConnection)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(int)), "clue id");
            Assert.That(
                parameters[2].ParameterType,
                Is.EqualTo(typeof(string)),
                "The clue reply must stay a rendered string. A structured type here is the leak §13 forbids.");
            Assert.That(parameters.Length, Is.EqualTo(3), "Nothing else may ride along with the reply.");
        }

        /// <summary>
        /// Installing the answers on a machine that is not the server is refused.
        /// <para>
        /// The classic accidental leak is not a SyncVar — it is a client that
        /// reconstructs the answer locally from a shared seed, which looks like a
        /// tidy bandwidth optimisation and is a total defeat of §03. This is the
        /// runtime tripwire for it.
        /// </para>
        /// </summary>
        [Test]
        public void AClientCannotInstallTheAnswers()
        {
            var authority = BuildHostAuthority(out _);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("호스트만 보유"));

            Assert.That(HostSecrets.Install(authority, isServer: false), Is.False);
            Assert.That(HostSecrets.Installed, Is.False);
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
        // §11 — four players, five roles, exactly one absent
        // ------------------------------------------------------------------

        /// <summary>
        /// §11's structure, enforced by the core and replicated by the lobby: four
        /// seats take four distinct roles and exactly one of §04's five is left over.
        /// </summary>
        [UnityTest]
        public IEnumerator TheLobbySeatsFourOfFiveRolesAndLeavesExactlyOneAbsent()
        {
            NetworkServer.Listen(GameConstants.PlayersPerMatch);

            var lobby = SpawnLobby();
            yield return null;

            Assert.That(lobby.Seats.Count, Is.EqualTo(GameConstants.PlayersPerMatch));
            Assert.That(GameConstants.RoleCount - GameConstants.PlayersPerMatch, Is.EqualTo(1),
                "§11's whole premise is that exactly one role is missing.");

            var picks = new[] { RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer };
            for (var i = 0; i < picks.Length; i++)
            {
                var conn = new NetworkConnectionToClient(10 + i) { isAuthenticated = true };
                NetworkServer.AddConnection(conn);

                var seat = lobby.TrySeat(conn.connectionId, "Player" + i);
                Assert.That(seat, Is.EqualTo(i));

                lobby.ServerClaimRole(conn, picks[i]);
            }

            Assert.That(lobby.LineupComplete, Is.True);
            Assert.That(
                lobby.MissingRole,
                Is.EqualTo(RoleId.Flasher),
                "Four roles claimed leaves §04's fifth on the table, and §11 makes that absence the match's "
                + "character.");

            Assert.That(
                lobby.Gap.CanBeCoveredWithCredits,
                Is.True,
                "§11 gives 섬광수 a 돈으로 메우기 row (섬광탄), so the lobby must be able to say so.");

            // A fifth arrival has nowhere to sit. §11 fixes the party at four.
            var fifth = new NetworkConnectionToClient(99) { isAuthenticated = true };
            NetworkServer.AddConnection(fifth);
            Assert.That(lobby.TrySeat(fifth.connectionId, "Fifth"), Is.EqualTo(-1));
        }

        /// <summary>
        /// §11 forbids duplicates: two 정비공 would leave two roles absent and the
        /// "5개 중 4개" structure would stop holding.
        /// </summary>
        [UnityTest]
        public IEnumerator TwoPlayersCannotTakeTheSameRole()
        {
            NetworkServer.Listen(GameConstants.PlayersPerMatch);

            var lobby = SpawnLobby();
            yield return null;

            var first = new NetworkConnectionToClient(21) { isAuthenticated = true };
            var second = new NetworkConnectionToClient(22) { isAuthenticated = true };
            NetworkServer.AddConnection(first);
            NetworkServer.AddConnection(second);

            lobby.TrySeat(first.connectionId, "A");
            lobby.TrySeat(second.connectionId, "B");

            lobby.ServerClaimRole(first, RoleId.Engineer);
            Assert.That(lobby.ServerClaimRole(second, RoleId.Engineer), Is.False);

            Assert.That(lobby.Seats[0].Role, Is.EqualTo(RoleId.Engineer));
            Assert.That(
                lobby.Seats[1].Role,
                Is.EqualTo(RoleId.None),
                "§11 allows no duplicates — the second claim must simply fail, leaving that player to choose again.");
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
            Assert.That(
                HostSecrets.Installed,
                Is.False,
                "A client never held the answers, and must not end up holding them on the way out either.");
        }

        // ------------------------------------------------------------------
        // fixtures
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a one-floor building whose only candidate site sits at
        /// <see cref="ObjectiveTruth"/>, so the objective's position is known to the
        /// test and to nobody on the wire.
        /// </summary>
        private static HostClueAuthority BuildHostAuthority(out int seed)
        {
            seed = 20260730;

            var floors = new List<FloorDescriptor>
            {
                // §03's worked example: "그것은 물이 있는 층에 있다."
                new FloorDescriptor(0, ClueGlyph.Digit3, FloorFeature.Water),
                new FloorDescriptor(1, ClueGlyph.Digit2, FloorFeature.Machinery),
            };

            var sites = new List<CandidateSite>
            {
                new CandidateSite(
                    0,
                    0,
                    0,
                    new Vec3(ObjectiveTruth.x, ObjectiveTruth.y, ObjectiveTruth.z),
                    new SiteLabel(ClueGlyph.WingMieum, ClueGlyph.Digit6, ClueGlyph.SideLeft)),
                // Non-zero, exactly representable components for the same reason the
                // objective has them: these positions are searched for byte by byte.
                new CandidateSite(
                    1,
                    1,
                    1,
                    new Vec3(11.375f, 3.5f, 12.625f),
                    new SiteLabel(ClueGlyph.WingIeung, ClueGlyph.Digit2, ClueGlyph.SideRight)),
                new CandidateSite(
                    2,
                    1,
                    1,
                    new Vec3(23.75f, 6.125f, -7.375f),
                    new SiteLabel(ClueGlyph.WingIeung, ClueGlyph.Digit5, ClueGlyph.SideLeft)),
            };

            var catalog = new SiteCatalog(floors, sites);
            var rng = new DeterministicRandom(seed);
            var resolver = new ObjectiveResolver(catalog, new FlatProbe(), Vec3.Zero, rng);

            Assert.That(
                resolver.VerifyChainConverges(),
                Is.True,
                "The fixture must be a winnable layout, or the test is exercising a match that could not be played.");

            return new HostClueAuthority(resolver, new DeterministicRandom(seed + 1));
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

        /// <summary>
        /// A deliberate leak, so <see cref="TheAuditCatchesALeakWhenThereIsOne"/> can
        /// prove the audit is not simply returning an empty list.
        /// <para>
        /// Deliberately <em>not</em> a <c>NetworkBehaviour</c>. Mirror's weaver would
        /// try to generate a reader and a writer for <c>SiteLabel</c> the moment this
        /// became one, and it could not — the type is a readonly struct — so the test
        /// assembly would fail to build. That failure is worth noticing rather than
        /// working around: the serialiser cannot be written for §03's answer types by
        /// accident, which is one more layer of the structural defence §13 asks for.
        /// The audit reads attributes, so a plain class with the same attributes is
        /// exactly the input it needs.
        /// </para>
        /// </summary>
        private sealed class LeakyForTesting
        {
            /// <summary>A site label on the wire is the objective's address, one misremembered mark aside.</summary>
            [SyncVar]
            public SiteLabel Leak;

            /// <summary>And the same leak wearing a remote call's clothes.</summary>
            [Command]
            public void CmdLeak(ClueGlyph glyph)
            {
                Leak = new SiteLabel(glyph, glyph, glyph);
            }
        }
    }
}
