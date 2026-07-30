#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Steam.Offline
{
    /// <summary>
    /// The offline stand-in for the whole platform: identity, lobbies, transport,
    /// voice, stats and cloud, all working locally.
    /// <para>
    /// This is a supported way to run the game, not a degraded error path. §14's plan
    /// reaches step 3 — 프로토타입 검증 — before Steam appears at all, with steps 1–3
    /// listing no App ID and step 3 using 디스코드 for voice. So the first playable
    /// build of this game runs entirely on this class, and it will keep being how CI
    /// builds, how a contributor without a Steam install works, and how the project
    /// would port to a platform §13 has not chosen yet.
    /// </para>
    /// <para>
    /// Nothing here throws and nothing here silently pretends: a created lobby really
    /// exists locally and can be joined by a second process on the same machine via
    /// loopback, identity returns a stable id, cloud writes land in the persistent data
    /// path, and stats are dropped on the floor while saying so. The one capability
    /// with no local equivalent is the voice codec, because there is no codec without
    /// Steamworks — see <see cref="SilentVoiceBackend"/>.
    /// </para>
    /// </summary>
    public sealed class NullSteamService : ISteamService
    {
        private readonly OfflineLobbyService _lobbies;

        /// <summary>
        /// Builds the stand-in. The reason is shown to the player, so it should name
        /// which of the several ordinary causes applies — built without the DLLs, Steam
        /// not running, or Steam refused to initialise.
        /// </summary>
        /// <param name="reason">Why the platform is not in use.</param>
        public NullSteamService(string reason)
        {
            OfflineReason = reason;

            var identity = new OfflineIdentity();
            Identity = identity;
            _lobbies = new OfflineLobbyService(identity);
            Lobbies = _lobbies;
            Transport = new LoopbackTransportProvider();
            Voice = new SilentVoiceBackend();
            Stats = new NullStatsService();
            Cloud = new LocalFileCloudSave();
        }

        /// <inheritdoc />
        public string BackendName => "Offline";

        /// <inheritdoc />
        public SteamBackendState State { get; private set; } = SteamBackendState.NotInitialized;

        /// <inheritdoc />
        public bool IsOnline => false;

        /// <inheritdoc />
        public string? OfflineReason { get; }

        /// <inheritdoc />
        public IUserIdentity Identity { get; }

        /// <inheritdoc />
        public ILobbyService Lobbies { get; }

        /// <inheritdoc />
        public IP2PTransportProvider Transport { get; }

        /// <inheritdoc />
        public IVoiceBackend Voice { get; }

        /// <inheritdoc />
        public IStatsService Stats { get; }

        /// <inheritdoc />
        public ICloudSaveService Cloud { get; }

        /// <inheritdoc />
        public bool Initialize()
        {
            State = SteamBackendState.Offline;
            Debug.Log("[Steam] Offline backend: " + OfflineReason
                + ". Local hosting, no invites, no in-game voice — §14 step 3 plays this way on purpose.");
            return true;
        }

        /// <inheritdoc />
        public void RunCallbacks()
        {
            // Deferred lobby results are delivered here rather than from inside the call
            // that requested them. Steam cannot answer synchronously, so neither does
            // this: code that subscribes after calling CreateLobby must work on both
            // backends, and the only way to be sure of that is to give both the same
            // timing.
            _lobbies.PumpDeferredEvents();
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            _lobbies.LeaveLobby();
            State = SteamBackendState.NotInitialized;
        }
    }

    /// <summary>
    /// A stable local identity. §13 answers 계정 · 프로필 with "Steam ID and no
    /// database", and the offline equivalent has to keep the one property host-authority
    /// code depends on: an id that does not change while the process lives.
    /// </summary>
    public sealed class OfflineIdentity : IUserIdentity
    {
        private readonly Dictionary<NetUserId, string> _names = new Dictionary<NetUserId, string>();

        /// <summary>
        /// Reserved high bit, marking an id as locally minted.
        /// <para>
        /// Steam's own ids are structured (universe, account type, instance) and never
        /// use this range, so an offline id can never collide with a real one — which
        /// matters the moment a log or a save file from an offline session is read by a
        /// build that does have Steam.
        /// </para>
        /// </summary>
        public const ulong OfflineIdMarker = 0x4F46_4C49_4E45_0000UL;

        /// <summary>Mints a local identity for this process.</summary>
        public OfflineIdentity()
        {
            // Stable for the life of the process and different between the two instances
            // §14 step 2 runs side by side on one PC — which is the whole requirement,
            // since a roster keyed by id cannot have both players answering to the same
            // one. Randomised rather than derived from the machine on purpose: nothing
            // identifying goes into an id that ends up in logs (§13 — 개인정보는
            // 수집하지 않는다).
            var salt = (ulong)(uint)Guid.NewGuid().GetHashCode();
            LocalId = new NetUserId(OfflineIdMarker | (salt & 0xFFFFUL));
            LocalName = "Player " + (LocalId.Value & 0xFFFUL).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _names[LocalId] = LocalName;
        }

        /// <inheritdoc />
        public NetUserId LocalId { get; }

        /// <inheritdoc />
        public string LocalName { get; }

        /// <inheritdoc />
        public bool CanResolveNames => false;

        /// <inheritdoc />
        public string NameOf(NetUserId id)
        {
            if (_names.TryGetValue(id, out var known))
            {
                return known;
            }

            // A readable placeholder rather than an empty string: this lands in the
            // lobby list, and a blank row reads as a bug in the game rather than as a
            // missing platform.
            return "Player " + (id.Value & 0xFFFFUL).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Remembers a name for a peer learned from the network, so the lobby list is
        /// not four rows of "Player 1234" during local testing.
        /// </summary>
        public void Remember(NetUserId id, string name)
        {
            if (id.IsValid && !string.IsNullOrWhiteSpace(name))
            {
                _names[id] = name;
            }
        }
    }

    /// <summary>
    /// A working lobby that exists only on this machine.
    /// <para>
    /// It answers asynchronously through <see cref="ISteamService.RunCallbacks"/> like
    /// the real one, tracks membership, holds lobby data, and refuses the operations that
    /// genuinely require a platform — invites — with a reason instead of a crash. §14
    /// step 2's 같은 PC 2인스턴스 does not need a lobby service at all: the second
    /// instance connects to <see cref="LoopbackTransportProvider.LocalAddress"/>
    /// directly. This exists so the lobby UI is developed and exercised before step 4.
    /// </para>
    /// </summary>
    public sealed class OfflineLobbyService : ILobbyService
    {
        private readonly OfflineIdentity _identity;
        private readonly List<LobbyMember> _members = new List<LobbyMember>();
        private readonly Dictionary<string, string> _data = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Queue<Action> _deferred = new Queue<Action>();

        private bool _joinable = true;

        /// <summary>Builds the local lobby service.</summary>
        public OfflineLobbyService(OfflineIdentity identity)
        {
            _identity = identity;
        }

        /// <inheritdoc />
        public bool InLobby => LobbyId.IsValid;

        /// <inheritdoc />
        public NetUserId LobbyId { get; private set; }

        /// <inheritdoc />
        public NetUserId HostId { get; private set; }

        /// <inheritdoc />
        public bool IsHost => InLobby && HostId == _identity.LocalId;

        /// <inheritdoc />
        public IReadOnlyList<LobbyMember> Members => _members;

        /// <inheritdoc />
        public int Capacity { get; private set; } = GameConstants.PlayersPerMatch;

        /// <inheritdoc />
        public event Action<LobbyResult>? LobbyCreated;

        /// <inheritdoc />
        public event Action<LobbyResult>? LobbyEntered;

        /// <inheritdoc />
        public event Action? LobbyLeft;

        /// <inheritdoc />
        public event Action<LobbyMember>? MemberJoined;

        /// <inheritdoc />
        public event Action<NetUserId>? MemberLeft;

        /// <inheritdoc />
        public event Action? LobbyDataChanged;

        /// <summary>
        /// Never raised offline: an invite has to arrive from somewhere, and without a
        /// platform there is no overlay and no <c>+connect_lobby</c> on the command line.
        /// Empty accessors rather than a field-like event, so no subscriber is retained to
        /// be called by nothing.
        /// </summary>
        public event Action<NetUserId>? JoinRequested
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public bool CreateLobby(LobbyVisibility visibility, int capacity)
        {
            if (InLobby)
            {
                LeaveLobby();
            }

            Capacity = capacity < 1 ? GameConstants.PlayersPerMatch : capacity;
            Visibility = visibility;

            // The lobby id is the host's id. Locally that is unambiguous, and it keeps
            // the invariant the transport relies on: the lobby names its host.
            LobbyId = _identity.LocalId;
            HostId = _identity.LocalId;
            _joinable = true;

            _members.Clear();
            _members.Add(new LobbyMember(_identity.LocalId, _identity.LocalName, true));
            _data[SteamAppConfig.LobbyKeys.HostId] = HostId.ToString();

            var id = LobbyId;
            _deferred.Enqueue(() =>
            {
                LobbyCreated?.Invoke(LobbyResult.Ok(id));
                LobbyEntered?.Invoke(LobbyResult.Ok(id));
            });

            return true;
        }

        /// <inheritdoc />
        public bool JoinLobby(NetUserId lobbyId)
        {
            // A lobby on another machine cannot be reached without a platform. Failing
            // through the same event the success path uses keeps the UI's state machine
            // identical on both backends.
            _deferred.Enqueue(() => LobbyEntered?.Invoke(LobbyResult.Failed(
                "Offline: lobby " + lobbyId + " is on another machine and that needs Steam. "
                + "Connect to a local host instead.")));
            return false;
        }

        /// <inheritdoc />
        public void LeaveLobby()
        {
            if (!InLobby)
            {
                return;
            }

            LobbyId = NetUserId.None;
            HostId = NetUserId.None;
            _members.Clear();
            _data.Clear();
            _deferred.Enqueue(() => LobbyLeft?.Invoke());
        }

        /// <inheritdoc />
        public bool OpenInviteOverlay() => false;

        /// <inheritdoc />
        public bool InviteFriend(NetUserId friendId) => false;

        /// <inheritdoc />
        public bool SetJoinable(bool joinable)
        {
            _joinable = joinable;
            return true;
        }

        /// <inheritdoc />
        public bool SetVisibility(LobbyVisibility visibility)
        {
            Visibility = visibility;
            return true;
        }

        /// <inheritdoc />
        public string? GetLobbyData(string key) =>
            key != null && _data.TryGetValue(key, out var value) ? value : null;

        /// <inheritdoc />
        public bool SetLobbyData(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            _data[key] = value ?? string.Empty;
            _deferred.Enqueue(() => LobbyDataChanged?.Invoke());
            return true;
        }

        /// <summary>Whether the lobby currently accepts arrivals.</summary>
        public bool IsJoinable => _joinable;

        /// <summary>Current visibility. Meaningless locally, but tracked so UI can display it.</summary>
        public LobbyVisibility Visibility { get; private set; } = LobbyVisibility.FriendsOnly;

        /// <summary>
        /// Adds a peer that arrived over the loopback transport, so §14 step 2's second
        /// instance shows up in the lobby list.
        /// </summary>
        public void AddLocalMember(NetUserId id, string name)
        {
            if (!InLobby || !id.IsValid)
            {
                return;
            }

            for (var i = 0; i < _members.Count; i++)
            {
                if (_members[i].Id == id)
                {
                    return;
                }
            }

            _identity.Remember(id, name);
            var member = new LobbyMember(id, name, false);
            _members.Add(member);
            _deferred.Enqueue(() => MemberJoined?.Invoke(member));
        }

        /// <summary>Removes a peer that disconnected from the loopback transport.</summary>
        public void RemoveLocalMember(NetUserId id)
        {
            for (var i = 0; i < _members.Count; i++)
            {
                if (_members[i].Id == id)
                {
                    _members.RemoveAt(i);
                    _deferred.Enqueue(() => MemberLeft?.Invoke(id));
                    return;
                }
            }
        }

        /// <summary>
        /// Raises whatever was queued. Drains a bounded number of events per call so a
        /// handler that queues more cannot spin the frame loop forever.
        /// </summary>
        public void PumpDeferredEvents()
        {
            var budget = _deferred.Count;
            while (budget-- > 0 && _deferred.Count > 0)
            {
                var action = _deferred.Dequeue();
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // A subscriber's exception must not take the pump down with it, or one
                    // broken UI panel stops every future lobby event.
                    Debug.LogException(ex);
                }
            }
        }
    }
}
