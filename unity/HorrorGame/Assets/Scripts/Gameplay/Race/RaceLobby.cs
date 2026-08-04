#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Gameplay.Match;
using HorrorGame.Net;
using HorrorGame.Steam;
using HorrorGame.UI;
using HorrorGame.UI.Shell;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.Gameplay.Race
{
    /// <summary>
    /// One pre-baked building the lobby may send the party into. §13.
    /// <para>
    /// A building is a SEED and the SCENE that was baked from it. The seed alone is not
    /// enough at run time — the maze is geometry in a scene file and a player cannot
    /// build one — and the scene name alone is not enough either, because two builds can
    /// hold different geometry under one name. <see cref="Generation"/> is what tells
    /// those apart: <c>MapSceneGenerator</c> stamps every scene it commits with a named
    /// empty object, and that stamp is in the artefact rather than in anybody's notes.
    /// </para>
    /// </summary>
    public readonly struct DescentBuilding
    {
        /// <summary>Records one roster line. All four fields come from the manifest the baker wrote.</summary>
        public DescentBuilding(int seed, string sceneName, string generation)
        {
            Seed = seed;
            SceneName = sceneName ?? string.Empty;
            Generation = generation ?? string.Empty;
        }

        /// <summary>The seed <c>DescentMap.Build</c> was run at. Identifies the maze; nothing rebuilds it.</summary>
        public int Seed { get; }

        /// <summary>Scene the party loads. Must be in Build Settings or nobody can enter it.</summary>
        public string SceneName { get; }

        /// <summary>
        /// The generation stamp inside that scene — <c>gen-&lt;date&gt;-&lt;time&gt;-seed&lt;n&gt;</c>,
        /// with <c>-forced</c> appended if a gate was overridden. Compared against the
        /// stamp found in the loaded scene, so a stale build is caught by the building
        /// rather than by its file name.
        /// </summary>
        public string Generation { get; }

        /// <summary>False for the zero value — "no building", which is what an empty roster selects.</summary>
        public bool Exists
        {
            get { return !string.IsNullOrEmpty(SceneName); }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return SceneName + " (씨앗 " + Seed + ", " + Generation + ")";
        }
    }

    /// <summary>
    /// Every building this build can descend into, read out of the manifest the map
    /// pipeline wrote. §01 · §13.
    /// <para>
    /// <b>Why this exists.</b> §11's lobby has always agreed a seed, broadcast it and
    /// logged it — and the scene was pre-baked at one fixed seed, so the number decided
    /// only where runners start. Every match was the same building, which makes 「맵을 아는
    /// 사람이 유리하다」 a permanent asset rather than a per-match one: the twentieth match
    /// is the first one already solved. This is the list the agreed seed now picks from.
    /// </para>
    /// <para>
    /// <b>It is read from the artefact, not written down.</b> The manifest is produced by
    /// <c>MapPipeline.BakeRoster</c> from the slots it ACTUALLY published, after each one
    /// has been through the same three gates the shipped map goes through — §12's
    /// checklist, the NavMesh connectivity audit and the player-traversal audit, all
    /// re-run on the scene as it sits on disk with its dressing in it. A seed whose
    /// building came out with nine islands never reaches this file, so it cannot be
    /// selected. That is the whole enforcement: the roster is not a list of seeds
    /// somebody believes are good, it is a list of buildings that passed.
    /// </para>
    /// <para>
    /// <b>A text asset and not a ScriptableObject.</b> The baker has to write it, the
    /// player has to read it, and a hand-editable format is one a human can diff in a
    /// pull request and a fingerprint can be taken over. A ScriptableObject would put
    /// the roster in a binary nobody can read in a review and would still need the same
    /// generation stamps in it.
    /// </para>
    /// </summary>
    public static class DescentRoster
    {
        /// <summary>Name <c>Resources.Load</c> is given. The baker writes it under a Resources folder.</summary>
        public const string ManifestResourceName = "DescentRoster";

        /// <summary>
        /// First line of the manifest. Bumped only when the FORMAT changes; a build that
        /// does not recognise it refuses the whole file rather than half-reading it.
        /// </summary>
        public const string ManifestHeader = "하강 descent roster 1";

        /// <summary>
        /// Prefix <c>MapSceneGenerator</c> gives the empty object it stamps a committed
        /// scene with.
        /// <para>
        /// <b>Two files agree on this string and neither can see the other.</b> The
        /// generator is in an editor assembly and this is runtime, exactly as
        /// <c>MatchMap</c>'s marker names are. Mirrors
        /// <c>MapSceneGenerator.GenerationStampPrefix</c>, which is <c>private const</c>
        /// there; if it ever changes, <see cref="RaceLobby"/>'s stamp check stops finding
        /// a stamp and says so rather than passing.
        /// </para>
        /// </summary>
        public const string GenerationStampPrefix = "SceneGen_";

        /// <summary>
        /// Field separator. A tab, because a scene name may contain a space and a seed
        /// may not contain a tab — so a line can never be mis-split into a wrong seed.
        /// </summary>
        public const char Separator = '\t';

        /// <summary>Keyword every building line starts with.</summary>
        public const string SlotKeyword = "building";

        private static DescentBuilding[] _buildings = Array.Empty<DescentBuilding>();
        private static uint _fingerprint;
        private static string _note = string.Empty;
        private static bool _read;

        /// <summary>Every building, in the order the baker published them. Empty when there is no manifest.</summary>
        public static IReadOnlyList<DescentBuilding> Buildings
        {
            get
            {
                EnsureRead();
                return _buildings;
            }
        }

        /// <summary>How many buildings this build can descend into. Zero means the manifest is missing.</summary>
        public static int Count
        {
            get { return Buildings.Count; }
        }

        /// <summary>
        /// One number covering the whole roster — count, seeds, scene names and generation
        /// stamps.
        /// <para>
        /// §13 needs every machine in one building, and the only way to know two machines
        /// agree is to compare what they are choosing FROM. The host sends this with the
        /// lobby roster and again with the start, and a client whose own number differs
        /// refuses to descend: it is holding a different set of buildings, so the index
        /// the host chose does not mean the same thing on both machines.
        /// </para>
        /// </summary>
        public static uint Fingerprint
        {
            get
            {
                EnsureRead();
                return _fingerprint;
            }
        }

        /// <summary>Why the roster is the size it is. Shown in the lobby when it is empty.</summary>
        public static string Note
        {
            get
            {
                EnsureRead();
                return _note;
            }
        }

        /// <summary>
        /// The building a seed names, or the zero value when there is no roster.
        /// <para>
        /// <b>By position in the published list, not by a slot number written in the
        /// file.</b> The baker publishes only the slots that passed their gates, so a seed
        /// that regressed leaves a shorter roster rather than a hole — and a hole would
        /// have to be either skipped (changing which building every later seed selects,
        /// silently) or entered (which is the defect). The manifest IS the list, the
        /// fingerprint covers it whole, and two machines with the same fingerprint cannot
        /// disagree about what index 3 means.
        /// </para>
        /// </summary>
        public static DescentBuilding Select(int seed)
        {
            EnsureRead();
            if (_buildings.Length == 0)
            {
                return default;
            }

            return _buildings[IndexFor(seed, _buildings.Length)];
        }

        /// <summary>
        /// Which building a seed picks out of a roster of the given size.
        /// <para>
        /// Unsigned, so <c>int.MinValue</c> cannot come back negative — <c>%</c> on a
        /// negative int does in C#, and an index of −3 into the roster is an exception in
        /// the one code path that must not throw. <c>RaceLobby.NewSeed</c> only draws
        /// positives, but the seed also arrives over the wire.
        /// </para>
        /// </summary>
        public static int IndexFor(int seed, int count)
        {
            return count <= 0 ? 0 : (int)((uint)seed % (uint)count);
        }

        /// <summary>Reads the manifest again. Called by the baker after it writes one, and by tests.</summary>
        public static void Reload()
        {
            _read = false;
            EnsureRead();
        }

        /// <summary>
        /// Replaces the roster with a made-up one. Tests only — nothing in the game calls
        /// it, because the whole point of the manifest is that the roster describes what
        /// was baked.
        /// </summary>
        public static void UseForTests(IReadOnlyList<DescentBuilding>? buildings)
        {
            if (buildings == null)
            {
                _buildings = Array.Empty<DescentBuilding>();
                _note = "테스트: 로스터 없음.";
            }
            else
            {
                var copy = new DescentBuilding[buildings.Count];
                for (var i = 0; i < buildings.Count; i++)
                {
                    copy[i] = buildings[i];
                }

                _buildings = copy;
                _note = "테스트: " + copy.Length + "개 건물.";
            }

            _fingerprint = FingerprintOf(Format(_buildings));
            _read = true;
        }

        /// <summary>
        /// The manifest text for a set of buildings — the one canonical spelling, used to
        /// write the file AND to take the fingerprint of it.
        /// <para>
        /// One formatter and one parser in one file, so a roster that round-trips is
        /// proof that both ends agree. The fingerprint is taken over THIS text rather than
        /// over the file on disk, so a comment or a trailing newline cannot make two
        /// identical rosters look different.
        /// </para>
        /// </summary>
        public static string Format(IReadOnlyList<DescentBuilding> buildings)
        {
            var text = new System.Text.StringBuilder();
            text.Append(ManifestHeader).Append('\n');

            if (buildings != null)
            {
                for (var i = 0; i < buildings.Count; i++)
                {
                    text.Append(SlotKeyword).Append(Separator)
                        .Append(buildings[i].Seed.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Append(Separator).Append(buildings[i].SceneName)
                        .Append(Separator).Append(buildings[i].Generation)
                        .Append('\n');
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// Reads a manifest. Returns false with a reason rather than a partial roster: a
        /// roster that dropped a line it could not parse is a roster that disagrees with
        /// every other machine's, which is the one failure this whole file exists to stop.
        /// </summary>
        public static bool TryParse(string? text, out DescentBuilding[] buildings, out string failure)
        {
            buildings = Array.Empty<DescentBuilding>();

            if (string.IsNullOrWhiteSpace(text))
            {
                failure = "manifest is empty";
                return false;
            }

            var lines = text!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length == 0 || !string.Equals(lines[0].Trim(), ManifestHeader, StringComparison.Ordinal))
            {
                failure = "first line is '" + (lines.Length > 0 ? lines[0] : string.Empty)
                    + "', not '" + ManifestHeader + "'";
                return false;
            }

            var found = new List<DescentBuilding>();
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var fields = line.Split(Separator);
                if (fields.Length != 4 || !string.Equals(fields[0], SlotKeyword, StringComparison.Ordinal))
                {
                    failure = "line " + (i + 1) + " is not '" + SlotKeyword + "<tab>seed<tab>scene<tab>generation'";
                    return false;
                }

                if (!int.TryParse(
                        fields[1],
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var seed))
                {
                    failure = "line " + (i + 1) + " has '" + fields[1] + "' where a seed goes";
                    return false;
                }

                if (fields[2].Length == 0 || fields[3].Length == 0)
                {
                    failure = "line " + (i + 1) + " has an empty scene name or generation stamp";
                    return false;
                }

                found.Add(new DescentBuilding(seed, fields[2], fields[3]));
            }

            buildings = found.ToArray();
            failure = string.Empty;
            return true;
        }

        /// <summary>
        /// FNV-1a over the canonical manifest text.
        /// <para>
        /// Not a cryptographic hash and it does not need to be: this compares two copies
        /// of a file that came out of the same build pipeline, it is not defending against
        /// anybody. What it must be is IDENTICAL on every platform, which rules out
        /// <c>string.GetHashCode</c> — .NET randomises that per process, so two machines
        /// running the same build would disagree about the same roster.
        /// </para>
        /// </summary>
        public static uint FingerprintOf(string canonical)
        {
            unchecked
            {
                var hash = 2166136261u;
                if (canonical != null)
                {
                    for (var i = 0; i < canonical.Length; i++)
                    {
                        hash ^= canonical[i];
                        hash *= 16777619u;
                    }
                }

                return hash;
            }
        }

        private static void EnsureRead()
        {
            if (_read)
            {
                return;
            }

            _read = true;
            _buildings = Array.Empty<DescentBuilding>();
            _fingerprint = 0u;

            var asset = Resources.Load<TextAsset>(ManifestResourceName);
            if (asset == null)
            {
                _note = "이 빌드에는 건물 목록이 없다 — 지도 파이프라인의 로스터 굽기를 돌리지 않았다.";
                _fingerprint = FingerprintOf(Format(_buildings));
                return;
            }

            if (!TryParse(asset.text, out var parsed, out var failure))
            {
                _note = "건물 목록을 읽지 못했다: " + failure;
                _fingerprint = FingerprintOf(Format(_buildings));
                Debug.LogError(
                    "[Descent] " + ManifestResourceName + " could not be read (" + failure
                    + "), so this build has one building. Re-run the roster bake.");
                return;
            }

            _buildings = parsed;
            _fingerprint = FingerprintOf(Format(_buildings));
            _note = _buildings.Length + "개 건물 · 지문 " + _fingerprint.ToString("X8");
        }
    }

    /// <summary>
    /// The whole lobby, as the host sees it, sent to one connection.
    /// <para>
    /// <b>Public fields, on purpose.</b> Mirror's weaver generates the serialiser from a
    /// message's fields; properties are not written. This is the one place in the project
    /// where the house preference for documented properties has to give way, so every
    /// field carries its own line instead.
    /// </para>
    /// <para>
    /// <b>Sent per connection rather than broadcast.</b> Each machine needs one fact
    /// nobody else needs — which row is theirs — and at a ceiling of twenty a personal
    /// copy costs a few hundred bytes and removes the alternative entirely: an extra
    /// "your index" message that can arrive out of step with the list it indexes into.
    /// </para>
    /// </summary>
    public struct RaceLobbyRosterMessage : NetworkMessage
    {
        /// <summary>§13's seed. The host chose it; this is the only way anybody else learns it.</summary>
        public int Seed;

        /// <summary>Row holding the host, so a client can mark it without guessing that it is row 0.</summary>
        public int HostIndex;

        /// <summary>Row holding the machine this copy was sent to.</summary>
        public int YourIndex;

        /// <summary>Display names, in arrival order. Never longer than §11's cap.</summary>
        public string[] Names;

        /// <summary>
        /// <see cref="DescentRoster.Fingerprint"/> as the host has it.
        /// <para>
        /// Sent with the ROSTER and not only with the start, so a client holding a
        /// different set of buildings finds out while it is sitting in the lobby rather
        /// than at the moment twenty people are told to go. There is nothing to do about
        /// it during a race and everything to do about it before one.
        /// </para>
        /// </summary>
        public uint RosterFingerprint;

        /// <summary>How many buildings the host can choose between. Drawn in the lobby; also names the defect.</summary>
        public int BuildingCount;
    }

    /// <summary>
    /// The host has started. Everyone loads the descent on <see cref="Seed"/>.
    /// <para>
    /// Carries the seed again rather than relying on the last roster message: this is the
    /// message that decides which building twenty people spend fifteen minutes in, and it
    /// costs four bytes to make it self-contained.
    /// </para>
    /// </summary>
    public struct RaceLobbyBeginMessage : NetworkMessage
    {
        /// <summary>§13's seed, restated. §01 deals the starting ring from a shuffle of it.</summary>
        public int Seed;

        /// <summary>Which of <see cref="DescentRoster.Buildings"/> the host chose. −1 when the host has no roster.</summary>
        public int BuildingIndex;

        /// <summary>
        /// The seed that building was BAKED from.
        /// <para>
        /// Not the same number as <see cref="Seed"/> and it must not be: the match seed is
        /// drawn fresh every race and the building seed is one of a handful that have been
        /// through the audit. Sent because it is the actionable half of a disagreement —
        /// "you have 씨앗 41 at index 3 and I have 씨앗 908" is a sentence somebody can fix,
        /// where two fingerprints are not.
        /// </para>
        /// </summary>
        public int BuildingSeed;

        /// <summary>Scene the party loads. The client checks it against its own roster before loading anything.</summary>
        public string BuildingScene;

        /// <summary>The host's <see cref="DescentRoster.Fingerprint"/>. A client whose own differs does not descend.</summary>
        public uint RosterFingerprint;
    }

    /// <summary>
    /// §11's lobby: host or join, fill up to twenty, and drop everybody into the same
    /// building on the same seed.
    /// <para>
    /// <b>Why this exists.</b> The shell's 시작 used to load the match scene directly, so
    /// the shipped build was a twenty-player race you could only ever play alone — the
    /// first thing the owner hit. This sits in the gap: it is the only place where the
    /// field size is decided, the only place §13's authority is claimed, and the only
    /// place the seed comes from.
    /// </para>
    /// <para>
    /// <b>It fits the existing flow rather than replacing it.</b> <c>GameShell</c> still
    /// owns the menu, the loading screen and the scene load; <c>HorrorGameNetworkManager</c>
    /// still owns the transport choice (Steam when it is really there, KCP on localhost
    /// otherwise) and §13's no-migration rule. This class asks the manager to host or join,
    /// watches who turns up, and when the host says go it hands the flow straight back to
    /// the shell through <see cref="LobbyEntry"/>. It loads no scene of its own.
    /// </para>
    /// <para>
    /// <b>Messages, not a spawned lobby object.</b> <c>NetLobby</c> is a
    /// <c>NetworkBehaviour</c> and therefore needs a registered prefab with a
    /// <c>NetworkIdentity</c> on it; there is no such prefab in this project and a lobby
    /// is the one screen that has to work before anything has been spawned. Two message
    /// types and <c>NetworkServer.RegisterHandler</c> need no prefab, no identity and no
    /// spawn — and the direction of every byte is then visible in one file, which is what
    /// §13's "호스트가 정한다" should look like in code.
    /// </para>
    /// <para>
    /// <b>What it deliberately does not do.</b> It does not replicate players, positions
    /// or standings. Twenty runners actually racing each other is <c>MatchDirector</c>'s
    /// job and is not done yet; what this guarantees is the precondition — everyone in one
    /// session, holding one seed, entering one building.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    [AddComponentMenu("HorrorGame/Race/Race Lobby")]
    public sealed class RaceLobby : MonoBehaviour, ILobbyRequests
    {
        /// <summary>
        /// Name of the empty object the bootstrap scene leaves for the Net layer.
        /// <para>
        /// Spelled out rather than referenced: <c>BootstrapSceneGenerator.NetBootstrapName</c>
        /// lives in an editor-only assembly, and a runtime class that referenced it would
        /// stop compiling in a player build. The generator's own summary calls it "Empty
        /// anchor the Net layer hangs its NetworkManager on", which is exactly this.
        /// </para>
        /// </summary>
        public const string NetBootstrapObjectName = "NetBootstrap";

        /// <summary>
        /// Seconds between roster polls on the host.
        /// <para>
        /// Not a tuned game value. Mirror hands <c>NetworkServer.OnConnectedEvent</c> to
        /// the <c>NetworkManager</c> by assignment rather than subscription, so a second
        /// listener cannot be added without taking the manager's own away — see
        /// <c>NetworkManager.SetupServer</c>. Polling twenty dictionary entries five times
        /// a second costs nothing and cannot break the manager.
        /// </para>
        /// </summary>
        private const float PollSeconds = 0.2f;

        private readonly List<int> _connectionOrder = new List<int>(GameConstants.RaceRunnersMax);
        private readonly List<LobbyRunner> _runners = new List<LobbyRunner>(GameConstants.RaceRunnersMax);

        private LobbyScreen? _screen;
        private Action? _onBegin;

        private LobbyStage _stage = LobbyStage.Offline;
        private int _seed;
        private int _hostIndex = -1;
        private int _localIndex = -1;
        private string _note = string.Empty;
        private float _nextPoll;

        /// <summary>
        /// The host's roster fingerprint as this machine was told it, or 0 before any
        /// roster message. Compared against <see cref="DescentRoster.Fingerprint"/>.
        /// </summary>
        private uint _hostFingerprint;

        /// <summary>How many buildings the host said it has. Drawn in the lobby note; 0 until told.</summary>
        private int _hostBuildingCount;

        /// <summary>The one lobby in the process, or null before the bootstrap scene has come up.</summary>
        public static RaceLobby? Instance { get; private set; }

        /// <summary>
        /// The seed everybody agreed on, or 0.
        /// <para>
        /// Static and outliving the scene load because that is the whole point of it: the
        /// value is settled in the bootstrap scene and consumed in the match scene, and
        /// the object carrying it has to survive the load in between.
        /// </para>
        /// </summary>
        public static int AgreedSeed { get; private set; }

        /// <summary>Where the lobby currently is. Read by tests.</summary>
        public LobbyStage Stage
        {
            get { return _stage; }
        }

        /// <summary>Everyone connected, host first. Empty when offline.</summary>
        public IReadOnlyList<LobbyRunner> Runners
        {
            get { return _runners; }
        }

        /// <summary>True on the machine holding §13's authority.</summary>
        public bool IsHost
        {
            get { return NetworkServer.active; }
        }

        /// <summary>
        /// Installs the shell hook. Runs once, after the bootstrap scene's objects exist.
        /// <para>
        /// <see cref="LobbyEntry"/> is a one-way seam: the UI assembly cannot reference
        /// Mirror and therefore cannot reference this class, so the dependency has to be
        /// installed from this side. Doing it here rather than from a component means the
        /// bootstrap scene does not have to be regenerated to gain a lobby.
        /// </para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            LobbyEntry.Intercept = OpenFromShell;
        }

        /// <summary>
        /// Finds the lobby, creating it on the bootstrap scene's Net anchor if it is not
        /// there yet.
        /// </summary>
        public static RaceLobby EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var anchor = GameObject.Find(NetBootstrapObjectName) ?? new GameObject(NetBootstrapObjectName);
            var lobby = anchor.GetComponent<RaceLobby>();
            return lobby != null ? lobby : anchor.AddComponent<RaceLobby>();
        }

        /// <summary>
        /// Shows the lobby.
        /// </summary>
        /// <param name="onBegin">
        /// Run once the host has started and the seed is settled — normally the shell's own
        /// 시작, so the loading screen and the scene load stay where they already are.
        /// </param>
        public void Open(Action? onBegin)
        {
            _onBegin = onBegin;

            EnsureScreen();
            _screen?.Open(this, DefaultAddress());

            SetStage(LobbyStage.Offline, "호스트가 되거나, 호스트의 주소로 참가한다.");
        }

        /// <summary>Hides the lobby. Any session it started keeps running.</summary>
        public void Close()
        {
            _screen?.Close();
        }

        /// <inheritdoc />
        public void RequestHost()
        {
            if (NetworkServer.active || NetworkClient.active)
            {
                return;
            }

            // ── the roster, before §13's authority is claimed ────────────────────────
            //
            // A host that cannot load one of its own buildings would pick it anyway and
            // strand the whole field: LoadSceneAsync returns null for a scene outside
            // Build Settings, so the symptom is a loading screen that never ends on every
            // machine at once. Checked HERE rather than at start because refusing to host
            // is recoverable and refusing after twenty people have joined is not.
            //
            // This is also the run-time half of "a seed that produces nine islands must not
            // be selectable". The author-time half is that MapPipeline only publishes a
            // building that passed its gates; this half is that the lobby can only choose a
            // published one, and refuses to run at all if the build cannot open them.
            if (!VerifyRoster(out var rosterFailure))
            {
                SetStage(LobbyStage.Offline, rosterFailure);
                Debug.LogError("[Lobby] " + rosterFailure, this);
                return;
            }

            var manager = EnsureManager();

            // §13 — the seed comes from the host and nowhere else. Generated here, at the
            // moment authority is claimed, so there is no window in which a session exists
            // without one and no code path on a client that could produce one.
            _seed = NewSeed();

            manager.StartHost();

            if (!NetworkServer.active)
            {
                SetStage(LobbyStage.Offline, "호스트를 시작하지 못했다. 포트가 이미 쓰이고 있는지 확인한다.");
                return;
            }

            RegisterClientHandlers();
            _connectionOrder.Clear();
            _nextPoll = 0f;

            SetStage(LobbyStage.Waiting, string.Empty);
            RebuildRoster(force: true);
        }

        /// <inheritdoc />
        public void RequestJoin(string address)
        {
            if (NetworkServer.active || NetworkClient.active)
            {
                return;
            }

            var manager = EnsureManager();
            if (!string.IsNullOrWhiteSpace(address))
            {
                manager.networkAddress = address.Trim();
            }

            // A client never has a seed of its own. Zeroed on the way in so that a screen
            // showing a number is a screen that was told one by the host.
            _seed = 0;
            _hostIndex = -1;
            _localIndex = -1;
            _hostFingerprint = 0u;
            _hostBuildingCount = 0;
            _runners.Clear();

            manager.StartClient();
            RegisterClientHandlers();

            SetStage(LobbyStage.Connecting, manager.networkAddress + " 에 연결하는 중…");
        }

        /// <inheritdoc />
        public void RequestStart()
        {
            if (!NetworkServer.active || _stage != LobbyStage.Waiting)
            {
                return;
            }

            // §11's floor, restated on the machine that can act on it. RaceState throws
            // below two — "one runner is not a race" — and a lobby is a better place to
            // refuse than a constructor halfway through building the world.
            if (_runners.Count < GameConstants.RaceRunnersMin)
            {
                SetStage(LobbyStage.Waiting, "§11 · " + GameConstants.RaceRunnersMin + "명부터 출발할 수 있다. 혼자서는 경주가 되지 않는다.");
                return;
            }

            // ── which building, decided once, on the machine that has §13's authority ──
            //
            // The seed picks it, so the number the lobby has been showing all along is now
            // the building as well as the starting ring — 「씨앗 = 건물」, which is what the
            // lobby screen already told players it meant and, until this change, was only
            // half true. The host does NOT let every machine work it out from the seed on
            // its own: two builds with different rosters would compute different buildings
            // from the same number and neither would notice. It is chosen here and SENT.
            var building = DescentRoster.Select(_seed);
            var index = DescentRoster.Count == 0 ? -1 : DescentRoster.IndexFor(_seed, DescentRoster.Count);

            // Remotes first, then this machine. The host is the last one to leave the
            // lobby, so a client that never got the message is a client the host can still
            // see sitting in it.
            var begin = new RaceLobbyBeginMessage
            {
                Seed = _seed,
                BuildingIndex = index,
                BuildingSeed = building.Seed,
                BuildingScene = building.SceneName,
                RosterFingerprint = DescentRoster.Fingerprint,
            };

            foreach (var connection in NetworkServer.connections.Values)
            {
                if (connection == null || connection == NetworkServer.localConnection)
                {
                    continue;
                }

                connection.Send(begin);
            }

            // Deliberately does NOT move NetSession into InMatch, which is what would make
            // HorrorGameNetworkManager refuse late joins. NetSession.SetPhase is internal to
            // the Net assembly and the public way in — TryBeginMatch — still demands §11's
            // old four-role lineup and returns false without one. Until that method grows a
            // race path, a runner who connects mid-descent is admitted to a lobby that is no
            // longer there. Recorded rather than worked around: forcing the phase from here
            // would put a second author on §13's state machine.
            BeginDescent(_seed, building);
        }

        /// <inheritdoc />
        public void RequestLeave()
        {
            StopSession();
            SetStage(LobbyStage.Offline, "세션을 떠났다.");
        }

        /// <inheritdoc />
        public void RequestBack()
        {
            StopSession();
            Close();

            // Back to whatever opened this. The shell owns the menu and rebuilding it is
            // its own idempotent call, so there is nothing to hand over.
            GameShell.Instance?.ShowMenu();
        }

        private static bool OpenFromShell(Action onBegin)
        {
            EnsureInstance().Open(onBegin);
            return true;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            NetSession.Ended += OnSessionEnded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            NetSession.Ended -= OnSessionEnded;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (_stage == LobbyStage.Connecting && NetworkClient.isConnected)
            {
                SetStage(LobbyStage.Waiting, "호스트가 출발시키기를 기다린다.");
            }

            if (!NetworkServer.active || _stage == LobbyStage.Starting)
            {
                return;
            }

            if (Time.unscaledTime < _nextPoll)
            {
                return;
            }

            _nextPoll = Time.unscaledTime + PollSeconds;
            RebuildRoster(force: false);
        }

        /// <summary>
        /// Brings up the <see cref="NetworkManager"/> if the scene has none, and holds it
        /// to §11's field.
        /// </summary>
        private NetworkManager EnsureManager()
        {
            var manager = NetworkManager.singleton;
            if (manager == null)
            {
                // The project's own manager, not a bare one: it is what picks the transport
                // (FizzySteamworks when Steam is really up, KCP on localhost otherwise) and
                // what implements §13's 호스트 이탈 → 세션 종료. Added at runtime because the
                // bootstrap scene ships the anchor empty — see NetBootstrapObjectName.
                manager = gameObject.AddComponent<HorrorGameNetworkManager>();
            }

            // ---------------------------------------------------------------
            // §11's cap, enforced. Twenty is a MAP limit, not a network one: §12-A fixes
            // the gate counts at 4 → 2 → 1 and §11 refuses to scale them with the field
            // ("관문 수를 인원에 맞춰 늘리면 안 된다 — 늘리면 20인 판이 20개의 1인 판이
            // 된다"). So a twenty-first runner is not turned away because a socket ran out;
            // they are turned away because the last gate is one cell wide and the building
            // was drawn for twenty people to contest it. Raising this number without
            // redrawing the storey would make the bottleneck a queue instead of a race.
            //
            // HorrorGameNetworkManager.Awake still sets this to the co-operative game's
            // four; overwritten here rather than there because that file belongs to the
            // Net layer and §04's four-player match is what it was written for.
            // ---------------------------------------------------------------
            manager.maxConnections = GameConstants.RaceRunnersMax;

            // A lobby spawns nobody. §04 deleted the roles, so there is nothing to choose
            // and no reason to have a body before the descent; leaving this on would make
            // Mirror ask for a player prefab this project does not have.
            manager.autoCreatePlayer = false;

            return manager;
        }

        /// <summary>
        /// Registers the two client handlers.
        /// <para>
        /// After the connection is up rather than in <c>Awake</c>: <c>NetworkClient.Shutdown</c>
        /// clears the handler table, so anything registered before a session would be gone
        /// by the second one.
        /// </para>
        /// </summary>
        private void RegisterClientHandlers()
        {
            NetworkClient.RegisterHandler<RaceLobbyRosterMessage>(OnRoster);
            NetworkClient.RegisterHandler<RaceLobbyBeginMessage>(OnBegin);
        }

        /// <summary>
        /// Rebuilds the host's view of who is here and tells everybody.
        /// </summary>
        /// <param name="force">Send even when nothing changed — used once, right after the host starts.</param>
        private void RebuildRoster(bool force)
        {
            var changed = force;

            // Arrival order, kept by hand. Mirror's connection dictionary has no order and
            // a lobby list that reshuffles itself every poll is unreadable; ids that are
            // still here keep their place and new ones go on the end.
            for (var i = _connectionOrder.Count - 1; i >= 0; i--)
            {
                if (!NetworkServer.connections.ContainsKey(_connectionOrder[i]))
                {
                    _connectionOrder.RemoveAt(i);
                    changed = true;
                }
            }

            foreach (var id in NetworkServer.connections.Keys)
            {
                if (_connectionOrder.Contains(id))
                {
                    continue;
                }

                _connectionOrder.Add(id);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            _runners.Clear();
            _hostIndex = -1;
            _localIndex = -1;

            var names = new string[Mathf.Min(_connectionOrder.Count, GameConstants.RaceRunnersMax)];

            for (var i = 0; i < names.Length; i++)
            {
                if (!NetworkServer.connections.TryGetValue(_connectionOrder[i], out var connection) || connection == null)
                {
                    names[i] = UiStrings.Unknown;
                    _runners.Add(new LobbyRunner(names[i], false, false));
                    continue;
                }

                var isHost = connection == NetworkServer.localConnection;
                names[i] = NameFor(connection, isHost);

                if (isHost)
                {
                    _hostIndex = i;
                    _localIndex = i;
                }

                _runners.Add(new LobbyRunner(names[i], isHost, isHost));
            }

            for (var i = 0; i < names.Length; i++)
            {
                if (!NetworkServer.connections.TryGetValue(_connectionOrder[i], out var connection)
                    || connection == null
                    || connection == NetworkServer.localConnection)
                {
                    continue;
                }

                connection.Send(new RaceLobbyRosterMessage
                {
                    Seed = _seed,
                    HostIndex = _hostIndex,
                    YourIndex = i,
                    Names = names,
                    RosterFingerprint = DescentRoster.Fingerprint,
                    BuildingCount = DescentRoster.Count,
                });
            }

            RefreshScreen();
        }

        /// <summary>
        /// What to call somebody.
        /// <para>
        /// The local player is named from the platform identity. Everybody else is named
        /// by their transport address, which is <c>HorrorGameNetworkManager</c>'s existing
        /// rule and its reason holds here unchanged: §13 gets identity from Steam, and a
        /// name the client sends is an impersonation vector — worse in a race than in a
        /// co-op game, because the whole result is a list of names in an order.
        /// </para>
        /// </summary>
        private static string NameFor(NetworkConnectionToClient connection, bool isLocal)
        {
            if (isLocal)
            {
                var name = SteamServices.Current.Identity.LocalName;
                return string.IsNullOrEmpty(name) ? "호스트" : name;
            }

            var address = connection.address;
            return string.IsNullOrEmpty(address) ? "주자 " + connection.connectionId : address;
        }

        private void OnRoster(RaceLobbyRosterMessage message)
        {
            // The host already has all of this first-hand; the loopback copy would only
            // overwrite it with the same values a frame later.
            if (NetworkServer.active)
            {
                return;
            }

            _seed = message.Seed;
            _hostIndex = message.HostIndex;
            _localIndex = message.YourIndex;
            _hostFingerprint = message.RosterFingerprint;
            _hostBuildingCount = message.BuildingCount;

            _runners.Clear();
            var names = message.Names ?? Array.Empty<string>();
            for (var i = 0; i < names.Length && i < GameConstants.RaceRunnersMax; i++)
            {
                _runners.Add(new LobbyRunner(names[i] ?? string.Empty, i == _hostIndex, i == _localIndex));
            }

            if (_stage == LobbyStage.Connecting)
            {
                SetStage(LobbyStage.Waiting, "호스트가 출발시키기를 기다린다.");
                return;
            }

            RefreshScreen();
        }

        private void OnBegin(RaceLobbyBeginMessage message)
        {
            if (NetworkServer.active)
            {
                return;
            }

            // ── §13: one building, or no race ────────────────────────────────────────
            //
            // A client that descends into a DIFFERENT maze from everybody else is worse
            // than a client that does not descend at all, and it is worse specifically
            // because it looks fine: the runner sees a building, runs down it, and is
            // ranked against twenty people who were never in it. There is no in-game
            // symptom to notice. So the disagreement is caught here, before a scene is
            // loaded, and the answer is to leave rather than to guess.
            if (!AgreesWithHost(message, out var disagreement))
            {
                Debug.LogError("[Lobby] " + disagreement, this);
                StopSession();
                SetStage(LobbyStage.Offline, disagreement);
                return;
            }

            BeginDescent(message.Seed, BuildingFrom(message));
        }

        /// <summary>
        /// True when this machine's roster can honour the host's choice.
        /// <para>
        /// Three checks, narrowest last. The fingerprint covers the whole roster and is
        /// what actually decides; the index and the seed are checked as well because they
        /// are what a person can act on — 「index 3 is 씨앗 41 here and 씨앗 908 there」 names
        /// the file to rebake, where two eight-digit fingerprints name nothing.
        /// </para>
        /// <para>
        /// A host with NO roster (index −1) is allowed through: that is a build whose map
        /// pipeline has not baked a roster yet, it has exactly one building, and every
        /// machine in the session loads the same one. It is a race with no variety rather
        /// than a race in two buildings.
        /// </para>
        /// </summary>
        private static bool AgreesWithHost(RaceLobbyBeginMessage message, out string disagreement)
        {
            disagreement = string.Empty;

            if (message.BuildingIndex < 0)
            {
                if (DescentRoster.Count == 0)
                {
                    return true;
                }

                disagreement = "호스트에게는 건물 목록이 없고 이 빌드에는 " + DescentRoster.Count
                    + "개가 있다. §13 · 같은 건물이라는 보장이 없으므로 내려가지 않는다.";
                return false;
            }

            if (message.RosterFingerprint != DescentRoster.Fingerprint)
            {
                disagreement = "건물 목록이 호스트와 다르다 (호스트 "
                    + message.RosterFingerprint.ToString("X8") + " · 이쪽 "
                    + DescentRoster.Fingerprint.ToString("X8")
                    + "). §13 · 같은 건물로 내려갈 수 없으므로 판을 떠난다. 두 빌드가 같은 로스터를 구워야 한다.";
                return false;
            }

            if (message.BuildingIndex >= DescentRoster.Count)
            {
                disagreement = "호스트가 " + message.BuildingIndex + "번 건물을 골랐는데 이 빌드에는 "
                    + DescentRoster.Count + "개뿐이다. §13 · 내려가지 않는다.";
                return false;
            }

            var mine = DescentRoster.Buildings[message.BuildingIndex];
            if (mine.Seed != message.BuildingSeed
                || !string.Equals(mine.SceneName, message.BuildingScene, StringComparison.Ordinal))
            {
                disagreement = message.BuildingIndex + "번 건물이 호스트는 씨앗 " + message.BuildingSeed
                    + " · " + message.BuildingScene + ", 이쪽은 씨앗 " + mine.Seed + " · " + mine.SceneName
                    + ". §13 · 같은 건물이 아니므로 내려가지 않는다.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// The building the host named, taken from THIS machine's roster rather than from
        /// the message.
        /// <para>
        /// The generation stamp is the point: it is not on the wire, so the only place a
        /// client can get one is its own manifest, and comparing that against the stamp
        /// found in the scene it loads is what catches two builds holding different
        /// geometry under one scene name. Taking it from the message instead would make
        /// the check compare the host's claim with the host's claim.
        /// </para>
        /// </summary>
        private static DescentBuilding BuildingFrom(RaceLobbyBeginMessage message)
        {
            if (message.BuildingIndex < 0 || message.BuildingIndex >= DescentRoster.Count)
            {
                return default;
            }

            return DescentRoster.Buildings[message.BuildingIndex];
        }

        /// <summary>
        /// Settles the seed and hands the flow back to the shell.
        /// </summary>
        private void BeginDescent(int seed, DescentBuilding building)
        {
            _seed = seed;
            AgreedSeed = seed;

            // The Net layer deals §01's starting line from a shuffle of this number, and
            // it cannot reference this assembly to come and get it.
            NetSession.SetAgreedSeed(seed);

            SetStage(
                LobbyStage.Starting,
                building.Exists
                    ? "씨앗 " + seed + " · " + building.SceneName + " (건물 씨앗 " + building.Seed + ") 로 내려간다."
                    : "씨앗 " + seed + " · 같은 건물로 내려간다.");
            Close();

            // Taken before the load that is about to destroy the objects holding it. See
            // RaceParty for why it is three facts and not the whole roster.
            RaceParty.Settle(_localIndex, NetworkServer.active ? _connectionOrder : null);
            RaceParty.SettleBuilding(building);

            // §02's results screen reads names, and on a client they exist ONLY here — the
            // roster replicated them into _runners, and nothing after the scene load ever
            // sees this object again. Without this line every client draws "1번 … 20번"
            // for a field of people whose names it was told half a second ago.
            var seatNames = new string[_runners.Count];
            for (var i = 0; i < _runners.Count; i++)
            {
                seatNames[i] = _runners[i].Name;
            }

            RaceParty.SettleNames(seatNames);

            var kept = KeepBodiesAcrossTheLoad();

            Debug.Log(
                "[Lobby] Descending on seed " + seed + " with " + _runners.Count + " runner(s) — §11's field is "
                + GameConstants.RaceRunnersMin + "~" + GameConstants.RaceRunnersMax + ". "
                + kept + " runner body/bodies carried across the scene load. 건물: "
                + (building.Exists ? building.ToString() : "로스터 없음 — 이 빌드의 유일한 건물")
                + " · 로스터 지문 " + DescentRoster.Fingerprint.ToString("X8")
                + " (" + DescentRoster.Count + "개).", this);

            // §13's choice, handed to the shell through the seam it declares. It cannot be
            // a call: HorrorGame.UI does not reference this assembly and must not — see
            // LobbyEntry, which exists because the shell cannot reference Mirror. Null when
            // this build has no roster, which leaves the shell on its own default.
            LobbyEntry.MatchScene = building.Exists ? building.SceneName : null;

            // The shell's own 시작 is what runs next, and it is also what opened this
            // lobby. The latch stops that being a loop.
            LobbyEntry.PassNextThrough();

            if (_onBegin != null)
            {
                _onBegin();
                return;
            }

            GameShell.Instance?.StartMatch();
        }

        /// <summary>
        /// Moves every spawned runner out of the scene that is about to be thrown away.
        /// <para>
        /// <b>This is the link the race was missing.</b> The descent is entered with
        /// <c>SceneManager.LoadSceneAsync(..., LoadSceneMode.Single)</c>, which destroys
        /// every object in the old scene — and the runners
        /// <c>HorrorGameNetworkManager.OnServerReady</c> built are ordinary objects in it.
        /// On the host each one's <c>NetworkIdentity.OnDestroy</c> then calls
        /// <c>NetworkServer.Destroy</c>, which despawns it for everybody; on a client the
        /// same load destroys its copy. Nothing spawns them again, because a client sends
        /// <c>ReadyMessage</c> exactly once, at connect. So the party arrived in the same
        /// building on the same seed with nobody's body left in it — twenty people alone
        /// in twenty identical mazes, and no test failed, because every test that has two
        /// peers in it never loads a scene.
        /// </para>
        /// <para>
        /// <b>Why this and not <c>ServerChangeScene</c>.</b> Mirror's own answer is
        /// <c>NetworkManager.ServerChangeScene</c>, which tells clients to load and
        /// re-spawns afterwards. Taking it would move the scene load out of
        /// <c>GameShell</c> — the loading screen, the minimum-display floor, the Build
        /// Settings error path — and put a second author on the flow the shell owns, for a
        /// party that already agreed on its own scene through
        /// <see cref="RaceLobbyBeginMessage"/>. Keeping the bodies is three lines and
        /// leaves both owners where they are. The cost is that these objects now outlive
        /// any scene: <see cref="StopSession"/> and <see cref="OnSessionEnded"/> are what
        /// clear them, through Mirror's own shutdown.
        /// </para>
        /// <para>
        /// Both dictionaries are swept because host mode populates both with the same
        /// objects and a pure client only has the second; <c>DontDestroyOnLoad</c> is
        /// idempotent, so the overlap costs nothing.
        /// </para>
        /// </summary>
        /// <returns>How many distinct objects were carried over. Zero during a descent is a defect.</returns>
        private static int KeepBodiesAcrossTheLoad()
        {
            var kept = new HashSet<int>();

            KeepAll(NetworkServer.spawned.Values, kept);
            KeepAll(NetworkClient.spawned.Values, kept);

            return kept.Count;
        }

        private static void KeepAll(IEnumerable<NetworkIdentity> identities, HashSet<int> kept)
        {
            // Copied out first: DontDestroyOnLoad moves an object between scenes, and
            // enumerating Mirror's dictionary while the engine reparents its contents is
            // not a guarantee worth relying on.
            var batch = new List<NetworkIdentity>();
            foreach (var identity in identities)
            {
                if (identity != null)
                {
                    batch.Add(identity);
                }
            }

            for (var i = 0; i < batch.Count; i++)
            {
                var go = batch[i].gameObject;

                // DontDestroyOnLoad only accepts a root object. NetRunner.Build makes one,
                // but a body attached by anything else might not be, and the alternative
                // to detaching is a silent Unity warning and a destroyed runner.
                if (go.transform.parent != null)
                {
                    go.transform.SetParent(null, worldPositionStays: true);
                }

                UnityEngine.Object.DontDestroyOnLoad(go);
                kept.Add(go.GetInstanceID());
            }
        }

        /// <summary>
        /// Lays the match out on the agreed seed, once the descent's scene is up.
        /// <para>
        /// <c>sceneLoaded</c> runs after every <c>Awake</c> and before every <c>Start</c>,
        /// which is the only window where this works: <c>MatchDirector.Start</c> lays the
        /// world out from its own serialised seed unless a match is already running, so
        /// calling <c>BeginMatch</c> here both wins and stops the second one happening.
        /// </para>
        /// <para>
        /// Consumed rather than remembered. A director that reloads its own scene later
        /// (§02's next round) picks its own seed, and a solo playtest run afterwards must
        /// not silently inherit a seed from a session that has ended.
        /// </para>
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 메뉴로 나가기 in the middle of a race loads the menu scene without telling
            // anybody, and a session left running behind it is worse than it looks: the
            // host keeps a socket open, the runner bodies this class deliberately kept
            // alive now outlive every scene, and the next 시작 opens a lobby whose
            // RequestHost returns immediately because NetworkServer.active is still true.
            // Leaving is leaving — §13 does not migrate and there is nothing to come back
            // to.
            if (string.Equals(scene.name, GameShell.DefaultMenuScene, StringComparison.Ordinal))
            {
                if (NetworkServer.active || NetworkClient.active)
                {
                    Debug.Log("[Lobby] 지상으로 나가면서 세션을 닫는다. §13 · 판은 이관되지 않는다.", this);
                    StopSession();
                    SetStage(LobbyStage.Offline, "판을 떠났다.");
                }

                return;
            }

            if (AgreedSeed == 0)
            {
                return;
            }

            var director = FindFirstObjectByType<MatchDirector>();
            if (director == null)
            {
                return;
            }

            var seed = AgreedSeed;
            AgreedSeed = 0;

            // From RaceParty rather than from a second static here: one fact, one home.
            // It is NOT consumed the way the seed is — the seed has to be, because a solo
            // playtest afterwards must not inherit one, and a building that outlives its
            // race is only a label a later log can print.
            var building = RaceParty.Building;

            // ── is this the building §13 chose? asked of the scene, not of the plan ───
            //
            // Everything up to here is agreement about a NAME. This is the only check that
            // reads the artefact: MapSceneGenerator commits every scene with a named empty
            // object carrying its generation, that object is still in the assembled scene
            // (measured on the shipped pair — SceneGen_gen-20260804-201542-seed20260802 is
            // in Map_FirstSketch.unity AND in Map_FirstSketch_Solo.unity), and the roster
            // records the same string per building. So two machines whose Build Settings
            // hold the same scene NAME over different geometry — one of them built before a
            // rebake — are told apart here and nowhere else.
            if (building.Exists && !VerifyLoadedBuilding(scene, building, out var wrong))
            {
                Debug.LogError("[Lobby] " + wrong, this);
                return;
            }

            if (!director.BeginMatch(seed))
            {
                Debug.LogError(
                    "[Lobby] The descent refused to start on seed " + seed
                    + ", so the runners are not all in the same building. See the [Match] error above.", this);
                return;
            }

            // After BeginMatch and not before: the map is read out of the scene inside
            // that call, and MovePlayerToSpawn puts this machine's rig on PlayerSpawns[0]
            // — right for one player, and on twenty machines twenty people inside one
            // cell. RaceRunners waits for §13's own answer and moves the rig there.
            //
            // Run on this component because it is the only MonoBehaviour in the flow that
            // outlives the scene load, and it has to be a coroutine: on a client the
            // answer is a SyncVar that has not arrived yet.
            StartCoroutine(RaceRunners.TakeTheStartLine());
        }

        private void OnSessionEnded(NetSessionEndReason reason)
        {
            if (_stage == LobbyStage.Offline)
            {
                return;
            }

            // A join that never landed and a host that walked out arrive here as the same
            // event, and they are two very different things to be told — the first is
            // "check the address", the second is "the match is gone". The stage the lobby
            // was in is what tells them apart.
            var connecting = _stage == LobbyStage.Connecting;

            _runners.Clear();
            _connectionOrder.Clear();
            _seed = 0;
            _hostIndex = -1;
            _localIndex = -1;
            _hostFingerprint = 0u;
            _hostBuildingCount = 0;

            RaceParty.Clear();
            RaceRunners.Clear();

            string note;
            if (connecting)
            {
                note = "연결하지 못했다. 주소와 호스트가 켜져 있는지 확인한다.";
            }
            else if (reason == NetSessionEndReason.HostLeft)
            {
                // §13: there is no promotion and no reconnect window, because the host is
                // the only machine that ever held the seed. Saying so is the honest thing.
                note = "호스트가 나갔다. §13 · 세션은 이관되지 않는다.";
            }
            else
            {
                note = "세션이 끝났다.";
            }

            SetStage(LobbyStage.Offline, note);
        }

        private void StopSession()
        {
            if (NetworkServer.active)
            {
                NetworkManager.singleton?.StopHost();
            }
            else if (NetworkClient.active)
            {
                NetworkManager.singleton?.StopClient();
            }

            _runners.Clear();
            _connectionOrder.Clear();
            _seed = 0;
            _hostIndex = -1;
            _localIndex = -1;
            _hostFingerprint = 0u;
            _hostBuildingCount = 0;

            // Both of these outlive scenes on purpose — see KeepBodiesAcrossTheLoad — so
            // the session ending is the only thing that can clear them. A second race, or
            // a solo playtest afterwards, must not inherit the first one's seat number.
            RaceParty.Clear();
            RaceRunners.Clear();
        }

        private void SetStage(LobbyStage stage, string note)
        {
            _stage = stage;
            _note = note ?? string.Empty;
            RefreshScreen();
        }

        private void RefreshScreen()
        {
            if (_screen == null)
            {
                return;
            }

            var note = _note;

            // A roster this machine cannot honour is the most useful thing this screen can
            // say, so it outranks every other note: the alternative is a player sitting in
            // a lobby they are about to be silently dropped out of. Only a CLIENT can be in
            // this state — RequestHost refuses to open a session whose roster is unusable.
            if (_stage == LobbyStage.Waiting && !IsHost && _hostFingerprint != 0u
                && _hostFingerprint != DescentRoster.Fingerprint)
            {
                note = "§13 · 건물 목록이 호스트와 다르다 (호스트 " + _hostBuildingCount + "개 · 이쪽 "
                    + DescentRoster.Count + "개). 이대로는 같은 미로로 내려갈 수 없어 출발할 때 판을 떠나게 된다.";
            }
            else if (string.IsNullOrEmpty(note) && _stage == LobbyStage.Waiting)
            {
                note = _runners.Count < GameConstants.RaceRunnersMin
                    ? "§11 · " + GameConstants.RaceRunnersMin + "명부터 출발할 수 있다."
                    : IsHost
                        ? DescentRoster.Count > 1
                            ? "§01 · 씨앗이 건물을 고른다 — 이 빌드에는 " + DescentRoster.Count + "개가 있다."
                            : "§12-A · 관문은 4 → 2 → 1로 고정이다. 인원이 늘수록 마지막 한 칸이 좁아진다."
                        : "호스트가 출발시키기를 기다린다.";
            }

            _screen.Refresh(_stage, _runners, IsHost, _seed, note);
        }

        private void EnsureScreen()
        {
            if (_screen != null)
            {
                return;
            }

            var child = new GameObject("LobbyScreen");
            child.transform.SetParent(transform, worldPositionStays: false);
            _screen = child.AddComponent<LobbyScreen>();
        }

        /// <summary>
        /// What to pre-fill the join box with — whatever the transport already decided.
        /// </summary>
        private static string DefaultAddress()
        {
            var manager = NetworkManager.singleton;
            if (manager != null && !string.IsNullOrEmpty(manager.networkAddress))
            {
                return manager.networkAddress;
            }

            return SteamServices.Current.Transport.LocalAddress;
        }

        /// <summary>
        /// Can this build actually open every building it is offering? Run before §13's
        /// authority is claimed.
        /// <para>
        /// <c>LoadSceneAsync</c> answers a scene outside Build Settings with null rather
        /// than an exception, which is how the shipped game once had a 시작 button that did
        /// nothing. A roster that promises buildings a player cannot load is that defect
        /// multiplied by the field size — twenty people on a loading screen that never
        /// finishes — so it stops the host here, with the scene named. This is the one
        /// roster state in this method that is an error, and it is an error because it is
        /// the one that ends a session.
        /// </para>
        /// <para>
        /// <b>The build list is no longer maintained only by hand.</b>
        /// <c>MapSceneGenerator.RegisterScenes</c> rewrites Build Settings wholesale, and it
        /// used to name three hard-coded paths — so regenerating the map dropped every
        /// descent slot out of the build, routinely and silently. It now re-adds the slots
        /// that are NAMED IN THE MANIFEST, which is the same list this method walks. So
        /// this check is no longer the routine consequence of a map regeneration; it is
        /// what catches the two cases that remain — a manifest carried into a player build
        /// whose scenes were not, and a slot scene deleted off disk with its line left in
        /// the file.
        /// </para>
        /// </summary>
        /// <param name="failure">What to put on the lobby screen. Empty when the roster is usable.</param>
        private static bool VerifyRoster(out string failure)
        {
            failure = string.Empty;

            if (DescentRoster.Count == 0)
            {
                // ── one building is a LEGAL STATE, and this is a warning ──────────────
                //
                // Not a refusal, and — since 2026-08-05 — not an error either. The two are
                // told apart by one question: does this state break a match? An empty
                // roster does not. Every machine in the session loads the same scene, the
                // race is fair, the seed still deals §01's starting ring, and this is what
                // 하강 shipped for its whole life and what it ships tomorrow if a bake is
                // slow. It is also, deliberately, the state a build is in the moment the
                // manifest is missing from a player — Resources.Load returning null must
                // not be a red line in a shipped game's log.
                //
                // The state that IS an error is the one below: a roster naming a scene
                // Build Settings does not have. That one ends with twenty people on a
                // loading screen that never finishes, because LoadSceneAsync answers an
                // unlisted scene with null. An error should mean "this session will fail",
                // and only one of these two does.
                //
                // What is NOT given up is the record. The line still goes out on every
                // host, it still names what is lost — 「맵을 아는 사람이 유리하다」 stops being
                // a per-match skill and becomes a permanent asset, so the twentieth match
                // is the first one already solved — and it still carries DescentRoster.Note
                // verbatim, which is what distinguishes "no manifest in this build" from
                // "a manifest that published zero buildings", i.e. a bake that was never
                // run from a bake that ran and refused everything. Deleting the line would
                // be forgetting; lowering it is saying the right thing at the right level.
                //
                // The unreadable manifest stays an error, and it is not raised here:
                // DescentRoster.EnsureRead logs one when TryParse refuses a file that
                // exists, because that is a build whose roster disagrees with every other
                // build's — the one failure the fingerprint exists to catch.
                Debug.LogWarning(
                    "[Lobby] " + DescentRoster.Note + " 모든 판이 같은 건물이 된다 — §01의 "
                    + "「맵을 아는 사람이 유리하다」가 판마다가 아니라 영구적인 자산이 된다. "
                    + "판은 정상적으로 돌아간다: 모두 같은 씬으로 내려가고 씨앗은 여전히 출발 위치를 정한다. "
                    + "건물을 늘리려면 HorrorGame ▸ Scene Gen ▸ Bake Descent Roster 를 돌린다.");
                return true;
            }

            for (var i = 0; i < DescentRoster.Count; i++)
            {
                var building = DescentRoster.Buildings[i];
                if (!IsInBuild(building.SceneName))
                {
                    failure = "§13 · " + i + "번 건물 '" + building.SceneName
                        + "' 이(가) Build Settings 에 없다. 고르면 아무도 들어가지 못하므로 방을 열지 않는다. "
                        + "지도를 다시 생성하면 씬 목록이 통째로 덮어써진다 — 로스터를 다시 구워야 한다.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when a scene NAME is one the player can load.
        /// <para>
        /// Walked out of <c>SceneUtility</c> rather than asked of
        /// <c>Application.CanStreamedLevelBeLoaded</c>, for one reason: this runs in the
        /// editor too, where a scene can be open and playable without being in Build
        /// Settings — and Build Settings is exactly what the shipped player will and will
        /// not have. A check that passes in the editor and fails in a build is the shape of
        /// half this project's blockers.
        /// </para>
        /// </summary>
        private static bool IsInBuild(string sceneName)
        {
            var count = SceneManager.sceneCountInBuildSettings;
            for (var i = 0; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var slash = path.LastIndexOf('/');
                var dot = path.LastIndexOf('.');
                var name = dot > slash
                    ? path.Substring(slash + 1, dot - slash - 1)
                    : path.Substring(slash + 1);

                if (string.Equals(name, sceneName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks the scene that actually came up against the building §13 chose.
        /// <para>
        /// Two different failures, and telling them apart is the whole value of this
        /// method:
        /// </para>
        /// <list type="number">
        /// <item><b>A different scene NAME.</b> Today that means only one thing — the shell
        /// was never told which building to load, because the seam is not in the UI
        /// assembly yet. Every machine loads the same default, so the race is fair and only
        /// the variety is missing. Reported and allowed.</item>
        /// <item><b>The same name over a different GENERATION.</b> That is two builds
        /// holding different geometry under one file name, which is exactly the state in
        /// which a runner races down a maze nobody else is in and nothing looks wrong.
        /// Refused: the match does not start on this machine.</item>
        /// </list>
        /// </summary>
        private static bool VerifyLoadedBuilding(Scene scene, DescentBuilding building, out string wrong)
        {
            wrong = string.Empty;

            if (!string.Equals(scene.name, building.SceneName, StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    "[Lobby] §13 chose '" + building.SceneName + "' and the shell loaded '" + scene.name
                    + "'. Every machine in this session did the same thing, so the race is still fair — but the "
                    + "maze is the same one it was last match. GameShell owns the scene load and has no way to "
                    + "be told which building to open; see the diff in the report on this change.");
                return true;
            }

            var stamp = FindGenerationStamp(scene);
            if (stamp == null)
            {
                // A scene with no stamp was not committed by MapSceneGenerator — a
                // hand-made test scene, or one saved over by hand. Nothing to compare, and
                // refusing would fail every such scene, so it is reported and allowed.
                Debug.LogWarning(
                    "[Lobby] '" + scene.name + "' carries no " + DescentRoster.GenerationStampPrefix
                    + " stamp, so this machine cannot prove it is in the same building as everybody else. "
                    + "The roster expected " + building.Generation + ".");
                return true;
            }

            if (string.Equals(stamp, building.Generation, StringComparison.Ordinal))
            {
                return true;
            }

            wrong = "§13 · 같은 이름의 다른 건물이다. '" + scene.name + "' 의 세대는 " + stamp
                + " 인데 로스터는 " + building.Generation
                + " 를 기대한다. 다른 빌드와 같은 미로가 아니므로 이 판은 시작하지 않는다. 두 빌드를 맞춰야 한다.";
            return false;
        }

        /// <summary>
        /// The generation string of a loaded scene, or null when it carries no stamp.
        /// <para>
        /// The whole scene is walked rather than only its roots: the stamp is created as a
        /// root object by <c>MapSceneGenerator.Commit</c>, but <c>SoloPlaytest</c> assembles
        /// the playable scene from the map scene and nothing promises the object stays
        /// unparented through that. Once per descent, so the walk costs nothing worth
        /// trading a wrong answer for.
        /// </para>
        /// </summary>
        private static string? FindGenerationStamp(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var found = FindGenerationStamp(roots[i].transform);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string? FindGenerationStamp(Transform node)
        {
            if (node.name.StartsWith(DescentRoster.GenerationStampPrefix, StringComparison.Ordinal))
            {
                return node.name.Substring(DescentRoster.GenerationStampPrefix.Length);
            }

            for (var i = 0; i < node.childCount; i++)
            {
                var found = FindGenerationStamp(node.GetChild(i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// A fresh seed for a new race.
        /// <para>
        /// Positive, and held well below the ceiling on purpose: <c>DescentMap.Build</c>
        /// derives a per-storey stream as <c>seed + level * 7919</c>, so a seed within
        /// 55 433 of <see cref="int.MaxValue"/> would wrap on B8 and two machines given
        /// "the same" number could disagree about the bottom floor — the one storey where
        /// a disagreement decides the winner.
        /// </para>
        /// </summary>
        private static int NewSeed()
        {
            return new System.Random().Next(1, int.MaxValue - 100000);
        }
    }
}
