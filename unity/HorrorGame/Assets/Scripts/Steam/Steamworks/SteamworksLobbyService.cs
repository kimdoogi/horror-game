#nullable enable

#if HORRORGAME_STEAMWORKS

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using Steamworks;
using UnityEngine;

namespace HorrorGame.Steam.SteamworksBackend
{
    /// <summary>
    /// §13's 로비 / 친구 초대 row on ISteamMatchmaking. This, plus Steam's overlay, is
    /// the entire matchmaking system the game ships with — no server, no room list, no
    /// account service.
    /// <para>
    /// Three ways in, and all three have to work or the feature is broken in the way
    /// players notice: the host creates a lobby; a friend joins from the overlay while the
    /// game is running (<c>GameLobbyJoinRequested_t</c>); and a friend accepts an invite
    /// while the game is <em>not</em> running, in which case Steam launches the game with
    /// <c>+connect_lobby &lt;id&gt;</c> on the command line and no callback ever fires.
    /// The third is the one that gets forgotten, so it is handled explicitly below.
    /// </para>
    /// <para>
    /// Lobby data carries only what a joining client needs to find the host. §13 keeps
    /// 단서 내용 · 목표물 위치 host-side because a client can read its own memory, and
    /// lobby data is readable by every member — putting §03's answers here would leak them
    /// just as effectively as sending them.
    /// </para>
    /// </summary>
    public sealed class SteamworksLobbyService : ILobbyService, IDisposable
    {
        private readonly SteamworksIdentity _identity;
        private readonly List<LobbyMember> _members = new List<LobbyMember>(GameConstants.PlayersPerMatch);
        private readonly Queue<Action> _deferred = new Queue<Action>();

        private readonly CallResult<LobbyCreated_t> _lobbyCreated;
        private readonly Callback<LobbyEnter_t> _lobbyEnter;
        private readonly Callback<LobbyChatUpdate_t> _lobbyChatUpdate;
        private readonly Callback<LobbyDataUpdate_t> _lobbyDataUpdate;
        private readonly Callback<GameLobbyJoinRequested_t> _joinRequestedCallback;

        private CSteamID _lobby;
        private int _requestedCapacity = GameConstants.PlayersPerMatch;
        private bool _disposed;

        /// <summary>Subscribes to the lobby callbacks and picks up a command-line invite if this launch came from one.</summary>
        public SteamworksLobbyService(SteamworksIdentity identity)
        {
            _identity = identity;

            _lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _lobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
            _joinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);

            QueueCommandLineInvite();
        }

        /// <summary>
        /// Whether a lobby is held. Tested against zero rather than with
        /// <c>CSteamID.IsValid()</c>, which is a structural check on account type and
        /// universe — a stricter question than "do I have a lobby", and not the one being
        /// asked here.
        /// </summary>
        public bool InLobby => _lobby.m_SteamID != 0UL;

        /// <inheritdoc />
        public NetUserId LobbyId => new NetUserId(_lobby.m_SteamID);

        /// <inheritdoc />
        public NetUserId HostId { get; private set; }

        /// <inheritdoc />
        public bool IsHost => InLobby && HostId == _identity.LocalId;

        /// <inheritdoc />
        public IReadOnlyList<LobbyMember> Members => _members;

        /// <inheritdoc />
        public int Capacity => InLobby ? SteamMatchmaking.GetLobbyMemberLimit(_lobby) : _requestedCapacity;

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

        /// <inheritdoc />
        public event Action<NetUserId>? JoinRequested;

        /// <inheritdoc />
        public bool CreateLobby(LobbyVisibility visibility, int capacity)
        {
            if (InLobby)
            {
                LeaveLobby();
            }

            _requestedCapacity = capacity < 1 ? GameConstants.PlayersPerMatch : capacity;

            var call = SteamMatchmaking.CreateLobby(ToLobbyType(visibility), _requestedCapacity);
            if (call == SteamAPICall_t.Invalid)
            {
                Defer(() => LobbyCreated?.Invoke(LobbyResult.Failed("Steam refused the lobby request")));
                return false;
            }

            _lobbyCreated.Set(call);
            return true;
        }

        /// <inheritdoc />
        public bool JoinLobby(NetUserId lobbyId)
        {
            if (!lobbyId.IsValid)
            {
                return false;
            }

            if (InLobby)
            {
                LeaveLobby();
            }

            // The answer arrives as a LobbyEnter_t callback, which is also how an
            // overlay-driven join reports itself — so both paths land in one handler and
            // cannot drift apart.
            SteamMatchmaking.JoinLobby(new CSteamID(lobbyId.Value));
            return true;
        }

        /// <inheritdoc />
        public void LeaveLobby()
        {
            if (!InLobby)
            {
                return;
            }

            SteamMatchmaking.LeaveLobby(_lobby);
            _lobby = CSteamID.Nil;
            HostId = NetUserId.None;
            _members.Clear();
            Defer(() => LobbyLeft?.Invoke());
        }

        /// <summary>
        /// Opens Steam's own invite dialog for the current lobby. Preferred over
        /// <see cref="InviteFriend"/>: the friend list, its search and its blocking rules
        /// are Steam's problem, and §13 has no interest in owning any of them.
        /// </summary>
        public bool OpenInviteOverlay()
        {
            if (!InLobby)
            {
                return false;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(_lobby);
            return true;
        }

        /// <inheritdoc />
        public bool InviteFriend(NetUserId friendId) =>
            InLobby && friendId.IsValid && SteamMatchmaking.InviteUserToLobby(_lobby, new CSteamID(friendId.Value));

        /// <inheritdoc />
        public bool SetJoinable(bool joinable) => InLobby && SteamMatchmaking.SetLobbyJoinable(_lobby, joinable);

        /// <inheritdoc />
        public bool SetVisibility(LobbyVisibility visibility) =>
            InLobby && SteamMatchmaking.SetLobbyType(_lobby, ToLobbyType(visibility));

        /// <inheritdoc />
        public string? GetLobbyData(string key)
        {
            if (!InLobby || string.IsNullOrEmpty(key))
            {
                return null;
            }

            var value = SteamMatchmaking.GetLobbyData(_lobby, key);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <inheritdoc />
        public bool SetLobbyData(string key, string value) =>
            InLobby && !string.IsNullOrEmpty(key) && SteamMatchmaking.SetLobbyData(_lobby, key, value ?? string.Empty);

        /// <summary>
        /// Raises the events queued since the last pump.
        /// <para>
        /// Steam's callbacks arrive during <c>SteamAPI.RunCallbacks</c>, which is called
        /// from a component's <c>Update</c> — so a subscriber that throws would otherwise
        /// unwind through Steamworks' native dispatch. Queueing puts our events on our own
        /// stack, where an exception can be caught and logged without the next callback
        /// being lost.
        /// </para>
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
                    Debug.LogException(ex);
                }
            }
        }

        /// <summary>Unsubscribes from Steam's callbacks and leaves any lobby.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            LeaveLobby();

            _lobbyCreated.Dispose();
            _lobbyEnter.Dispose();
            _lobbyChatUpdate.Dispose();
            _lobbyDataUpdate.Dispose();
            _joinRequestedCallback.Dispose();
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                var reason = ioFailure ? "no response from Steam" : result.m_eResult.ToString();
                Defer(() => LobbyCreated?.Invoke(LobbyResult.Failed("Could not create the lobby: " + reason)));
                return;
            }

            _lobby = new CSteamID(result.m_ulSteamIDLobby);
            HostId = _identity.LocalId;
            RefreshMembers();

            // The host's id is written into lobby data because that is what a joining
            // client hands to the transport as an address. It is public to the lobby,
            // which is fine: it is the one fact everyone in the match needs.
            SteamMatchmaking.SetLobbyData(_lobby, SteamAppConfig.LobbyKeys.HostId, HostId.ToString());
            SteamMatchmaking.SetLobbyData(_lobby, SteamAppConfig.LobbyKeys.Version, Application.version);

            var id = LobbyId;
            Defer(() => LobbyCreated?.Invoke(LobbyResult.Ok(id)));
        }

        private void OnLobbyEnter(LobbyEnter_t entered)
        {
            if (entered.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                var response = (EChatRoomEnterResponse)entered.m_EChatRoomEnterResponse;
                Defer(() => LobbyEntered?.Invoke(LobbyResult.Failed(DescribeEnterFailure(response))));
                return;
            }

            _lobby = new CSteamID(entered.m_ulSteamIDLobby);
            HostId = new NetUserId(SteamMatchmaking.GetLobbyOwner(_lobby).m_SteamID);
            RefreshMembers();

            var id = LobbyId;
            Defer(() => LobbyEntered?.Invoke(LobbyResult.Ok(id)));
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t update)
        {
            if (update.m_ulSteamIDLobby != _lobby.m_SteamID)
            {
                return;
            }

            var changed = new NetUserId(update.m_ulSteamIDUserChanged);
            var entered = (update.m_rgfChatMemberStateChange
                & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0;

            // The lobby owner can change when the previous owner leaves. §13 does not
            // migrate the host mid-match, but a lobby that has not started yet is a
            // different matter: re-reading the owner keeps IsHost honest either way.
            HostId = new NetUserId(SteamMatchmaking.GetLobbyOwner(_lobby).m_SteamID);
            RefreshMembers();

            if (entered)
            {
                var member = FindMember(changed) ?? new LobbyMember(changed, _identity.NameOf(changed), false);
                Defer(() => MemberJoined?.Invoke(member));
            }
            else
            {
                // Left, kicked, banned and disconnected are not distinguished: §13 ends the
                // session when someone is gone, and none of the four is recoverable.
                Defer(() => MemberLeft?.Invoke(changed));
            }
        }

        private void OnLobbyDataUpdate(LobbyDataUpdate_t update)
        {
            if (update.m_ulSteamIDLobby != _lobby.m_SteamID)
            {
                return;
            }

            RefreshMembers();
            Defer(() => LobbyDataChanged?.Invoke());
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t request)
        {
            var lobby = new NetUserId(request.m_steamIDLobby.m_SteamID);
            Defer(() => JoinRequested?.Invoke(lobby));
        }

        /// <summary>
        /// Picks up <c>+connect_lobby &lt;id&gt;</c> from the command line.
        /// <para>
        /// When a friend accepts an invite and the game is not running, Steam launches it
        /// with that argument and never sends a callback. Without this, "accept invite"
        /// silently starts the game at the main menu, which is the single most common
        /// broken-invite bug on Steam.
        /// </para>
        /// </summary>
        private void QueueCommandLineInvite()
        {
            string[] arguments;
            try
            {
                arguments = Environment.GetCommandLineArgs();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Steam] Could not read the command line: " + ex.Message);
                return;
            }

            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (!string.Equals(arguments[i], "+connect_lobby", StringComparison.Ordinal))
                {
                    continue;
                }

                if (NetUserId.TryParse(arguments[i + 1], out var lobby))
                {
                    // Deferred like every other event, so a UI that subscribes during its
                    // own start-up still receives it.
                    Defer(() => JoinRequested?.Invoke(lobby));
                }

                return;
            }
        }

        private void RefreshMembers()
        {
            _members.Clear();

            if (!InLobby)
            {
                return;
            }

            var owner = SteamMatchmaking.GetLobbyOwner(_lobby);
            var count = SteamMatchmaking.GetNumLobbyMembers(_lobby);

            // Host first, so UI can render the list in order without sorting it.
            _members.Add(new LobbyMember(
                new NetUserId(owner.m_SteamID),
                _identity.NameOf(new NetUserId(owner.m_SteamID)),
                true));

            for (var i = 0; i < count; i++)
            {
                var member = SteamMatchmaking.GetLobbyMemberByIndex(_lobby, i);
                if (member == owner)
                {
                    continue;
                }

                var id = new NetUserId(member.m_SteamID);
                _members.Add(new LobbyMember(id, _identity.NameOf(id), false));
            }
        }

        private LobbyMember? FindMember(NetUserId id)
        {
            for (var i = 0; i < _members.Count; i++)
            {
                if (_members[i].Id == id)
                {
                    return _members[i];
                }
            }

            return null;
        }

        private void Defer(Action action) => _deferred.Enqueue(action);

        private static ELobbyType ToLobbyType(LobbyVisibility visibility)
        {
            switch (visibility)
            {
                case LobbyVisibility.Public:
                    return ELobbyType.k_ELobbyTypePublic;
                case LobbyVisibility.InviteOnly:
                    return ELobbyType.k_ELobbyTypePrivate;
                default:
                    return ELobbyType.k_ELobbyTypeFriendsOnly;
            }
        }

        private static string DescribeEnterFailure(EChatRoomEnterResponse response)
        {
            switch (response)
            {
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull:
                    return "That match is full.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist:
                    return "That match has already ended.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed:
                    return "You were not invited to that match.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseMemberBlockedYou:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseYouBlockedMember:
                    return "You cannot join that match.";
                default:
                    return "Could not join that match (" + response + ").";
            }
        }
    }
}

#endif
