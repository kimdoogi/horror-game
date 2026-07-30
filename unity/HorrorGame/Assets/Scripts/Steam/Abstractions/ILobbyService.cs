#nullable enable

using System;
using System.Collections.Generic;

namespace HorrorGame.Steam
{
    /// <summary>
    /// Who may join, and how they find the lobby.
    /// <para>
    /// §13's 나중에 서버가 필요해질 수 있는 경우 table says 공개 매칭 would need a
    /// reporting and filtering system we are not building, so
    /// <see cref="FriendsOnly"/> is the shipping default and
    /// <see cref="Public"/> exists for internal playtests only.
    /// </para>
    /// </summary>
    public enum LobbyVisibility
    {
        /// <summary>Only invitees, and not visible to friends. Used while a match is being set up.</summary>
        InviteOnly,

        /// <summary>Friends can see and join it. §13's 로비 / 친구 초대 case, and the one the game ships with.</summary>
        FriendsOnly,

        /// <summary>Anyone can find it. Playtest use.</summary>
        Public,
    }

    /// <summary>One occupant of the lobby.</summary>
    public readonly struct LobbyMember
    {
        /// <summary>Platform id.</summary>
        public readonly NetUserId Id;

        /// <summary>Display name at the time the list was built.</summary>
        public readonly string Name;

        /// <summary>
        /// True for the lobby owner, who is also the game host: §13 gives the host
        /// authority and does not migrate it, so ownership and authority are the same
        /// fact and are not tracked separately.
        /// </summary>
        public readonly bool IsHost;

        /// <summary>Builds a member row.</summary>
        public LobbyMember(NetUserId id, string name, bool isHost)
        {
            Id = id;
            Name = name;
            IsHost = isHost;
        }
    }

    /// <summary>
    /// The outcome of an asynchronous lobby request.
    /// <para>
    /// Lobby creation and joining are round trips to Steam's servers, so they cannot
    /// return their answer. Both backends deliver it through an event on a later
    /// <see cref="ISteamService.RunCallbacks"/> — including the offline one, which
    /// could answer immediately but deliberately does not.
    /// </para>
    /// </summary>
    public readonly struct LobbyResult
    {
        /// <summary>The lobby the request was about. <see cref="NetUserId.None"/> on failure.</summary>
        public readonly NetUserId LobbyId;

        /// <summary>Whether the request succeeded.</summary>
        public readonly bool Success;

        /// <summary>Why it failed, for the UI. Null on success.</summary>
        public readonly string? Error;

        /// <summary>Builds a result.</summary>
        public LobbyResult(NetUserId lobbyId, bool success, string? error)
        {
            LobbyId = lobbyId;
            Success = success;
            Error = error;
        }

        /// <summary>A success result.</summary>
        public static LobbyResult Ok(NetUserId lobbyId) => new LobbyResult(lobbyId, true, null);

        /// <summary>A failure result carrying a message fit to show a player.</summary>
        public static LobbyResult Failed(string error) => new LobbyResult(NetUserId.None, false, error);
    }

    /// <summary>
    /// §13's 로비 / 친구 초대, which is the entire matchmaking system: four friends,
    /// one host, no server. ISteamMatchmaking underneath, a local table offline.
    /// <para>
    /// The lobby's only job is to get everyone to agree on a host and then hand its
    /// address to the transport. It is not a game-state channel — §13's 아키텍처
    /// table keeps 단서 내용 · 목표물 위치 host-side, and lobby data is readable by
    /// every member, so putting anything from §03 in here would defeat the design's
    /// central constraint just as thoroughly as sending it over the network.
    /// </para>
    /// </summary>
    public interface ILobbyService
    {
        /// <summary>Whether the local player is currently in a lobby.</summary>
        bool InLobby { get; }

        /// <summary>The current lobby, or <see cref="NetUserId.None"/>.</summary>
        NetUserId LobbyId { get; }

        /// <summary>The host of the current lobby, or <see cref="NetUserId.None"/>.</summary>
        NetUserId HostId { get; }

        /// <summary>True when the local player owns the lobby, and therefore hosts the match.</summary>
        bool IsHost { get; }

        /// <summary>
        /// Current occupants, host first. Rebuilt on membership and data changes
        /// rather than per access, so it is cheap enough for a UI to read every frame.
        /// </summary>
        IReadOnlyList<LobbyMember> Members { get; }

        /// <summary>Seats, including the host. §11 and <c>GameConstants.PlayersPerMatch</c> put this at four.</summary>
        int Capacity { get; }

        /// <summary>
        /// Asks for a new lobby with the local player as host. The answer arrives via
        /// <see cref="LobbyCreated"/>. Returns false if the request could not even be
        /// issued.
        /// </summary>
        bool CreateLobby(LobbyVisibility visibility, int capacity);

        /// <summary>
        /// Joins an existing lobby, answering through <see cref="LobbyEntered"/>. The
        /// id comes from <see cref="JoinRequested"/> or from a friend's invite; there
        /// is no browser, by §13's choice.
        /// </summary>
        bool JoinLobby(NetUserId lobbyId);

        /// <summary>
        /// Leaves the current lobby. Idempotent. For the host this ends the session:
        /// §13's 호스트 이탈 row is 세션 종료 — 마이그레이션 하지 않음.
        /// </summary>
        void LeaveLobby();

        /// <summary>
        /// Opens the platform's own invite dialog. Preferred over
        /// <see cref="InviteFriend"/> because the friend list, its search and its
        /// blocking rules are all the platform's problem, not ours.
        /// </summary>
        bool OpenInviteOverlay();

        /// <summary>Invites one known player directly, for a "re-invite" button.</summary>
        bool InviteFriend(NetUserId friendId);

        /// <summary>
        /// Closes or reopens the lobby to new arrivals. The host closes it when the
        /// match starts: §07 makes the clock the source of pressure and a player
        /// dropped into hour two of someone else's match has no game to play.
        /// </summary>
        bool SetJoinable(bool joinable);

        /// <summary>Changes who can see the lobby while it exists.</summary>
        bool SetVisibility(LobbyVisibility visibility);

        /// <summary>
        /// Reads shared lobby metadata. Keys come from
        /// <see cref="SteamAppConfig.LobbyKeys"/>. Returns null when unset.
        /// </summary>
        string? GetLobbyData(string key);

        /// <summary>Writes shared lobby metadata. Host only on every platform.</summary>
        bool SetLobbyData(string key, string value);

        /// <summary>Result of <see cref="CreateLobby"/>.</summary>
        event Action<LobbyResult>? LobbyCreated;

        /// <summary>Result of <see cref="JoinLobby"/>, and of accepting an invite.</summary>
        event Action<LobbyResult>? LobbyEntered;

        /// <summary>Raised after the local player leaves or is dropped.</summary>
        event Action? LobbyLeft;

        /// <summary>Someone arrived.</summary>
        event Action<LobbyMember>? MemberJoined;

        /// <summary>Someone left, was kicked or disconnected. The three are not distinguished: §13 ends the session either way.</summary>
        event Action<NetUserId>? MemberLeft;

        /// <summary>Lobby metadata changed. UI re-reads whatever it displays.</summary>
        event Action? LobbyDataChanged;

        /// <summary>
        /// A friend's invite was accepted from outside the game — the overlay, or the
        /// command line when the game was not running. Carries the lobby to join.
        /// Ignoring this event is how a game acquires the bug report "the invite
        /// button does nothing".
        /// </summary>
        event Action<NetUserId>? JoinRequested;
    }
}
