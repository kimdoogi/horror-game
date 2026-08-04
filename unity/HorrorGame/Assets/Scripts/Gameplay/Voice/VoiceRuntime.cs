#nullable enable

using System.Collections.Generic;
using HorrorGame.UI;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// Installs 근접 음성 and keeps it wired to whatever Mirror is doing. The one object
    /// that makes the rest of this folder reachable from a running game.
    /// <para>
    /// <b>It installs itself, and that is the point.</b> This repository's signature
    /// failure is a system that reviews clean and is not in the artefact — a map generated
    /// into a scene the game does not load, a camera attached to nothing, a race recording
    /// descents into an object nothing read. A voice pipeline that needed a component
    /// dragged onto a prefab would be the next one: it would work in whatever scene the
    /// author tested and be absent from the shipped bootstrap, and no test would fail.
    /// A <c>[RuntimeInitializeOnLoadMethod]</c> cannot be forgotten — the same guarantee
    /// <c>MatchAudioBootstrap</c>, <c>RaceLobby.Install</c> and
    /// <c>NetPlayerBridge</c> already take for the same reason.
    /// </para>
    /// <para>
    /// <b>It costs nothing until there is a session.</b> Until <c>NetworkServer</c> or
    /// <c>NetworkClient</c> is up, <see cref="Update"/> is two boolean comparisons: no
    /// codec is chosen, no microphone is touched, no handler is registered and no
    /// <c>AudioSource</c> exists. A machine sitting in the main menu is not listening, and
    /// that is a property of this class rather than of anybody remembering to switch it
    /// off.
    /// </para>
    /// <para>
    /// <b>Handlers are registered per session, not once.</b> <c>NetworkClient.Shutdown</c>
    /// clears the handler table, so anything registered before a session is gone by the
    /// second one — the trap <c>RaceLobby.RegisterClientHandlers</c> and
    /// <c>HorrorGameNetworkManager.OnStartClient</c> both already document.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceRuntime : MonoBehaviour
    {
        /// <summary>Name of the object this installs itself on. Matches <c>MatchAudioBootstrap</c>'s convention.</summary>
        public const string HostName = "[Voice]";

        /// <summary>Seconds between sweeps for connections that left without the relay noticing.</summary>
        private const float PruneIntervalSeconds = 1f;

        private readonly List<int> _knownListeners = new List<int>();
        private readonly List<int> _departed = new List<int>();

        private NetVoicePositions? _shippedPositions;
        private IVoicePositions? _positions;
        private IVoiceLine? _line;
        private VoiceHostRelay? _relay;
        private VoicePlayback? _playback;
        private VoiceCapture? _capture;

        private bool _serverUp;
        private bool _clientUp;
        private float _nextPrune;

        /// <summary>The live runtime, or null before the first scene has loaded.</summary>
        public static VoiceRuntime? Instance { get; private set; }

        /// <summary>
        /// Test seam: the position source a session will be built with. Cleared by
        /// <see cref="ResetForTests"/>.
        /// <para>
        /// A fixture with a real socket has real connections and no character controllers,
        /// so it has to be able to say where the two of them are standing. Everything else
        /// in the path — the codec, the audience rule, the wire, the attenuation — stays
        /// the shipped code.
        /// </para>
        /// </summary>
        public static IVoicePositions? PositionsOverride { get; set; }

        /// <summary>The host's relay while a server is up, else null.</summary>
        public VoiceHostRelay? Relay => _relay;

        /// <summary>This machine's playback while a client is up, else null.</summary>
        public VoicePlayback? Playback => _playback;

        /// <summary>The local speaker while a session is up, else null.</summary>
        public VoiceCapture? Capture => _capture;

        /// <summary>The codec and microphone this session chose, else null.</summary>
        public IVoiceLine? Line => _line;

        /// <summary>Where everybody is, as this session answers it.</summary>
        public IVoicePositions? Positions => _positions;

        /// <summary>
        /// Whether the runner in §11 seat <paramref name="seatIndex"/> can be heard right
        /// now. What <c>RaceHud</c> asks; false in every state where there is no session,
        /// so a screen needs no null checks of its own.
        /// </summary>
        /// <param name="seatIndex">§02's <c>Racer.Id</c>.</param>
        public static bool IsSeatSpeaking(int seatIndex)
        {
            var instance = Instance;
            var playback = instance != null ? instance._playback : null;
            return playback != null && playback.IsSeatSpeaking(seatIndex);
        }

        /// <summary>Builds the runtime if it is not already there. Idempotent.</summary>
        public static VoiceRuntime EnsureInstalled()
        {
            var existing = Instance;
            if (existing != null)
            {
                return existing;
            }

            var host = new GameObject(HostName);
            DontDestroyOnLoad(host);
            return host.AddComponent<VoiceRuntime>();
        }

        /// <summary>Drops the seams a fixture set. Tests only.</summary>
        public static void ResetForTests()
        {
            PositionsOverride = null;
            VoiceLines.ResetForTests();

            var instance = Instance;
            if (instance != null)
            {
                instance.StopClientSide();
                instance.StopServerSide();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            EnsureInstalled();

            // §02's screen asks who can be heard, and this is the answer being handed to
            // it. Installed rather than called, because HorrorGame.Gameplay already
            // references HorrorGame.UI and a screen that named this class would close the
            // cycle — see RaceHud.SeatIsSpeaking. Assigned here rather than in Awake so it
            // survives a runtime that is torn down and rebuilt between sessions.
            RaceHud.SeatIsSpeaking = IsSeatSpeaking;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (Instance == this)
            {
                StopClientSide();
                StopServerSide();
                Instance = null;
            }
        }

        private void Update()
        {
            var server = NetworkServer.active;
            var client = NetworkClient.active;

            if (server != _serverUp)
            {
                if (server)
                {
                    StartServerSide();
                }
                else
                {
                    StopServerSide();
                }
            }

            if (client != _clientUp)
            {
                if (client)
                {
                    StartClientSide();
                }
                else
                {
                    StopClientSide();
                }
            }

            if (!_serverUp && !_clientUp)
            {
                return;
            }

            FollowTheEar();

            var now = Time.unscaledTimeAsDouble;
            _playback?.Step(now);
            PruneDepartedListeners();
        }

        /// <summary>
        /// Puts this object where the player's ears are, so <see cref="VoiceCapture"/>'s
        /// audience gate measures from the right place.
        /// <para>
        /// The capture lives here rather than on the player rig on purpose: the rig arrives
        /// with the descent's scene and can be replaced by one, while a session's voice has
        /// to survive both. See <c>NetVoicePositions</c> for why the ear and not the
        /// replicated proxy.
        /// </para>
        /// </summary>
        private void FollowTheEar()
        {
            if (_positions != null && _positions.TryGetLocalListenerPosition(out var ear))
            {
                transform.position = ear;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The AudioListener the last scene was resolved against is gone with it.
            _shippedPositions?.Forget();
        }

        private void StartServerSide()
        {
            _serverUp = true;
            EnsureSession();

            _relay = new VoiceHostRelay(_positions!);

            // Replace rather than Register: Mirror warns when a handler is registered
            // twice, and a server restarted inside one process would otherwise log it.
            NetworkServer.ReplaceHandler<VoiceUpstreamMessage>(OnUpstream, requireAuthentication: true);

            _capture?.Bind(_line!, _positions!, _relay);
        }

        private void StopServerSide()
        {
            if (!_serverUp)
            {
                return;
            }

            _serverUp = false;
            NetworkServer.UnregisterHandler<VoiceUpstreamMessage>();

            _relay?.Clear();
            _relay = null;

            // Rebound rather than left holding a dead relay: a client that outlives its
            // server must send upstream, not into an object nobody drains.
            if (_capture != null && _line != null && _positions != null)
            {
                _capture.Bind(_line, _positions, null);
            }

            TearDownIfIdle();
        }

        private void StartClientSide()
        {
            _clientUp = true;
            EnsureSession();

            _playback = new VoicePlayback(_positions!, _line!);
            NetworkClient.ReplaceHandler<VoiceDownstreamMessage>(OnDownstream, requireAuthentication: true);
        }

        private void StopClientSide()
        {
            if (!_clientUp)
            {
                return;
            }

            _clientUp = false;

            _playback?.Clear();
            _playback = null;

            TearDownIfIdle();
        }

        /// <summary>
        /// Chooses the codec and builds the local speaker, once per session.
        /// <para>
        /// Deferred until a session exists rather than done at install, because
        /// <see cref="VoiceLines.Choose"/> reaches into <c>SteamServices</c> and
        /// <c>Microphone.devices</c>, and a machine sitting in a menu should touch
        /// neither.
        /// </para>
        /// </summary>
        private void EnsureSession()
        {
            if (_positions == null)
            {
                _positions = PositionsOverride ?? ShippedPositions();
            }

            if (_line == null)
            {
                _line = VoiceLines.Choose();
                VoiceLines.Report(_line);
            }

            if (_capture == null)
            {
                _capture = gameObject.AddComponent<VoiceCapture>();

                // The keys come with the capture rather than being wired separately, for
                // the reason this whole class exists: a voice system whose input component
                // has to be remembered is a voice system that ships mute.
                gameObject.AddComponent<VoicePushToTalk>();
            }

            SetSpeakerEnabled(true);
            _capture.Bind(_line, _positions, _relay);
        }

        private void TearDownIfIdle()
        {
            if (_serverUp || _clientUp)
            {
                return;
            }

            // Disabled, not destroyed. Two reasons, and the second is the one that would
            // have cost an afternoon: there is exactly one of these objects for the life of
            // the process, so rebuilding the pair between sessions buys nothing — and
            // VoicePushToTalk [RequireComponent]s VoiceCapture, which makes destroying them
            // an ordering question Unity answers with a console error rather than an
            // exception. VoiceCapture.OnDisable already closes the microphone and reports
            // Silent to §06, which is the whole of what teardown has to mean here.
            SetSpeakerEnabled(false);

            _line?.StopCapture();
            _line = null;
            _positions = null;
        }

        /// <summary>Turns the local speaker and its keys on or off together.</summary>
        /// <param name="on">Whether a session is up.</param>
        private void SetSpeakerEnabled(bool on)
        {
            if (_capture != null)
            {
                _capture.enabled = on;
            }

            var keys = GetComponent<VoicePushToTalk>();
            if (keys != null)
            {
                keys.enabled = on;
            }
        }

        private NetVoicePositions ShippedPositions() => _shippedPositions ??= new NetVoicePositions();

        /// <summary>
        /// Releases the slots of connections that have gone away.
        /// <para>
        /// Swept rather than driven by <c>NetworkServer.OnDisconnectedEvent</c>, which
        /// <c>NetworkManager</c> <em>assigns</em> rather than subscribes to — hooking it
        /// here would either be overwritten by the manager or overwrite it, and losing
        /// <c>HorrorGameNetworkManager.OnServerDisconnect</c> would leave §11's seats
        /// occupied by people who left.
        /// </para>
        /// </summary>
        private void PruneDepartedListeners()
        {
            var relay = _relay;
            if (relay == null || Time.unscaledTime < _nextPrune)
            {
                return;
            }

            _nextPrune = Time.unscaledTime + PruneIntervalSeconds;

            _departed.Clear();
            for (var i = 0; i < _knownListeners.Count; i++)
            {
                if (!NetworkServer.connections.ContainsKey(_knownListeners[i]))
                {
                    _departed.Add(_knownListeners[i]);
                }
            }

            for (var i = 0; i < _departed.Count; i++)
            {
                relay.Forget(_departed[i]);
                _knownListeners.Remove(_departed[i]);
            }

            foreach (var id in NetworkServer.connections.Keys)
            {
                if (!_knownListeners.Contains(id))
                {
                    _knownListeners.Add(id);
                }
            }
        }

        private void OnUpstream(NetworkConnectionToClient conn, VoiceUpstreamMessage message)
        {
            var relay = _relay;
            if (relay == null || conn == null)
            {
                return;
            }

            // The speaker is the socket, never the payload. See VoiceUpstreamMessage.
            relay.Accept(
                conn.connectionId,
                VoiceWire.ToEffort(message.Effort),
                VoiceWire.ToCodec(message.Codec),
                message.Payload,
                Time.unscaledTimeAsDouble);
        }

        private void OnDownstream(VoiceDownstreamMessage message)
        {
            _playback?.Accept(message, Time.unscaledTimeAsDouble);
        }
    }
}
