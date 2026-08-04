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
        /// <summary>§13's seed, restated. <c>DescentMap.Build</c> turns it into the tower.</summary>
        public int Seed;
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

            // Remotes first, then this machine. The host is the last one to leave the
            // lobby, so a client that never got the message is a client the host can still
            // see sitting in it.
            var begin = new RaceLobbyBeginMessage { Seed = _seed };
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
            BeginDescent(_seed);
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

            BeginDescent(message.Seed);
        }

        /// <summary>
        /// Settles the seed and hands the flow back to the shell.
        /// </summary>
        private void BeginDescent(int seed)
        {
            _seed = seed;
            AgreedSeed = seed;

            // The Net layer deals §01's starting line from a shuffle of this number, and
            // it cannot reference this assembly to come and get it.
            NetSession.SetAgreedSeed(seed);

            SetStage(LobbyStage.Starting, "씨앗 " + seed + " · 같은 건물로 내려간다.");
            Close();

            // Taken before the load that is about to destroy the objects holding it. See
            // RaceParty for why it is two fields and not the whole roster.
            RaceParty.Settle(_localIndex, NetworkServer.active ? _connectionOrder : null);

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
                + kept + " runner body/bodies carried across the scene load.", this);

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
            if (string.IsNullOrEmpty(note) && _stage == LobbyStage.Waiting)
            {
                note = _runners.Count < GameConstants.RaceRunnersMin
                    ? "§11 · " + GameConstants.RaceRunnersMin + "명부터 출발할 수 있다."
                    : IsHost
                        ? "§12-A · 관문은 4 → 2 → 1로 고정이다. 인원이 늘수록 마지막 한 칸이 좁아진다."
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
