#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;
using Mirror;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// §11's lobby: four seats, five roles, exactly one role left on the table.
    /// <para>
    /// The rule lives in <see cref="RoleSelection"/> and stays there. This class owns
    /// no logic about which combinations are legal — it takes a claim, hands it to
    /// the core, and replicates whatever the core says the table now looks like.
    /// That is ARCHITECTURE §1's split: a duplicate of "no two players may take
    /// 정비공" written in a MonoBehaviour is a rule that <c>dotnet test</c> cannot
    /// see, and the one that would drift.
    /// </para>
    /// <para>
    /// The host is the only machine that mutates the selection (§13). Clients send
    /// intent and read the result; there is no optimistic local claim, because two
    /// players pressing 주자 in the same frame have to be resolved somewhere and the
    /// host is the only place that can.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [AddComponentMenu("HorrorGame/Net/Net Lobby")]
    public sealed class NetLobby : NetworkBehaviour
    {
        private readonly SyncList<NetLobbySeat> _seats = new SyncList<NetLobbySeat>();

        /// <summary>
        /// The rules-side table. Host-only — a client never has one, so a client can
        /// never answer a question about the lineup except by reading what arrived.
        /// </summary>
        private RoleSelection? _selection;

        /// <summary>
        /// The lobby currently in the scene, or null. There is one per process: §13's
        /// model is one host and four friends, never two lobbies at once.
        /// </summary>
        public static NetLobby? Instance { get; private set; }

        /// <summary>The four seats, in order. Read-only on every machine, including the host.</summary>
        public IReadOnlyList<NetLobbySeat> Seats => _seats;

        /// <summary>Raised on every machine whenever any seat changes.</summary>
        public event Action? SeatsChanged;

        /// <summary>
        /// Whether all four seats hold distinct roles. §11's start condition, and the
        /// only one — readiness is a courtesy, a complete lineup is a requirement.
        /// </summary>
        public bool LineupComplete
        {
            get
            {
                var taken = 0;
                for (var i = 0; i < _seats.Count; i++)
                {
                    if (_seats[i].Role != RoleId.None)
                    {
                        taken++;
                    }
                }

                return taken == GameConstants.PlayersPerMatch;
            }
        }

        /// <summary>Whether every occupied seat has readied.</summary>
        public bool EveryoneReady
        {
            get
            {
                var occupied = 0;
                for (var i = 0; i < _seats.Count; i++)
                {
                    if (!_seats[i].Occupied)
                    {
                        continue;
                    }

                    occupied++;
                    if (!_seats[i].Ready)
                    {
                        return false;
                    }
                }

                return occupied > 0;
            }
        }

        /// <summary>
        /// The role nobody took, or <see cref="RoleId.None"/> before the lineup is
        /// complete. §11 calls this the match's character, so it is computed on every
        /// machine from the seats rather than sent as its own field: a value that can
        /// disagree with the four it is derived from is a bug waiting for a lobby
        /// screen to show it.
        /// </summary>
        public RoleId MissingRole
        {
            get
            {
                if (!LineupComplete)
                {
                    return RoleId.None;
                }

                var all = RoleSelection.AllRoles;
                for (var i = 0; i < all.Count; i++)
                {
                    if (!IsRoleTaken(all[i]))
                    {
                        return all[i];
                    }
                }

                return RoleId.None;
            }
        }

        /// <summary>
        /// What the absent role costs this match and what can be bought instead.
        /// §11's 돈으로 메우기 column, resolved from public information — no host
        /// round trip needed, because nothing about it is secret.
        /// </summary>
        public RoleGap Gap => RoleGap.For(MissingRole);

        /// <summary>Whether anybody has claimed this role.</summary>
        public bool IsRoleTaken(RoleId role)
        {
            if (role == RoleId.None)
            {
                return false;
            }

            for (var i = 0; i < _seats.Count; i++)
            {
                if (_seats[i].Role == role)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The roles still free. Drives the lobby's buttons.</summary>
        public IReadOnlyList<RoleId> AvailableRoles()
        {
            var all = RoleSelection.AllRoles;
            var free = new List<RoleId>(all.Count);
            for (var i = 0; i < all.Count; i++)
            {
                if (!IsRoleTaken(all[i]))
                {
                    free.Add(all[i]);
                }
            }

            return free;
        }

        /// <summary>Index of the seat a connection holds, or -1.</summary>
        public int SeatIndexOf(int connectionId)
        {
            for (var i = 0; i < _seats.Count; i++)
            {
                if (_seats[i].ConnectionId == connectionId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Seats an arriving player, returning the seat index or -1 when the lobby is
        /// full. §11 fixes the party at <see cref="GameConstants.PlayersPerMatch"/>;
        /// a fifth arrival has nowhere to sit and is refused at the door rather than
        /// admitted to watch.
        /// </summary>
        [Server]
        public int TrySeat(int connectionId, string displayName)
        {
            EnsureSelection();

            var existing = SeatIndexOf(connectionId);
            if (existing >= 0)
            {
                return existing;
            }

            for (var i = 0; i < _seats.Count; i++)
            {
                if (_seats[i].Occupied)
                {
                    continue;
                }

                var seat = _seats[i];
                seat.ConnectionId = connectionId;
                seat.DisplayName = displayName ?? string.Empty;
                seat.Role = RoleId.None;
                seat.Ready = false;
                _seats[i] = seat;
                return i;
            }

            return -1;
        }

        /// <summary>Empties a seat and puts its role back on the table.</summary>
        [Server]
        public void Vacate(int connectionId)
        {
            EnsureSelection();

            var index = SeatIndexOf(connectionId);
            if (index < 0)
            {
                return;
            }

            _selection!.Release(index);
            _seats[index] = NetLobbySeat.Vacant;
        }

        /// <inheritdoc />
        public override void OnStartServer()
        {
            Instance = this;
            EnsureSelection();

            if (_seats.Count == 0)
            {
                for (var i = 0; i < GameConstants.PlayersPerMatch; i++)
                {
                    _seats.Add(NetLobbySeat.Vacant);
                }
            }
        }

        /// <inheritdoc />
        public override void OnStartClient()
        {
            Instance = this;
            _seats.OnChange += OnSeatsChanged;
            SeatsChanged?.Invoke();
        }

        /// <inheritdoc />
        public override void OnStopClient()
        {
            _seats.OnChange -= OnSeatsChanged;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// A player asks for a role. Refused when somebody else already has it: §11
        /// forbids duplicates because two 청음사 would make two roles absent and the
        /// "5개 중 4개" structure the section rests on would stop holding.
        /// <para>
        /// <c>requiresAuthority = false</c> because the lobby object is not owned by
        /// any player — it is the table everybody is sitting at.
        /// </para>
        /// </summary>
        [Command(requiresAuthority = false)]
        public void CmdClaimRole(RoleId role, NetworkConnectionToClient? sender = null) =>
            ServerClaimRole(sender, role);

        /// <summary>Puts a seat's role back on the table.</summary>
        [Command(requiresAuthority = false)]
        public void CmdReleaseRole(NetworkConnectionToClient? sender = null) => ServerReleaseRole(sender);

        /// <summary>Marks a seat ready, or not. A seat with no role cannot be ready.</summary>
        [Command(requiresAuthority = false)]
        public void CmdSetReady(bool ready, NetworkConnectionToClient? sender = null) =>
            ServerSetReady(sender, ready);

        /// <summary>
        /// The host's own side of a claim.
        /// <para>
        /// Split out from <see cref="CmdClaimRole"/> because the host is a player
        /// too, and Mirror's woven <c>Command</c> is a send stub that refuses to run
        /// on the server. Without this split, the host's own lobby button would have
        /// to take a different code path from everybody else's — and a rule enforced
        /// on two paths is a rule enforced on one of them.
        /// </para>
        /// </summary>
        /// <returns>False when the seat is unknown or §11 forbids the claim.</returns>
        [Server]
        public bool ServerClaimRole(NetworkConnectionToClient? sender, RoleId role)
        {
            if (sender == null)
            {
                return false;
            }

            EnsureSelection();

            var index = SeatIndexOf(sender.connectionId);
            if (index < 0)
            {
                return false;
            }

            if (!_selection!.TryClaim(index, role))
            {
                return false;
            }

            var seat = _seats[index];
            seat.Role = _selection.RoleOf(index);

            // A player who changes their mind is no longer ready. §11 makes the
            // lineup the match's premise, and agreeing to a premise that has since
            // changed is not agreement.
            seat.Ready = false;
            _seats[index] = seat;
            return true;
        }

        /// <summary>The host's own side of a release.</summary>
        [Server]
        public bool ServerReleaseRole(NetworkConnectionToClient? sender)
        {
            if (sender == null)
            {
                return false;
            }

            EnsureSelection();

            var index = SeatIndexOf(sender.connectionId);
            if (index < 0)
            {
                return false;
            }

            _selection!.Release(index);

            var seat = _seats[index];
            seat.Role = RoleId.None;
            seat.Ready = false;
            _seats[index] = seat;
            return true;
        }

        /// <summary>The host's own side of a readiness change.</summary>
        [Server]
        public bool ServerSetReady(NetworkConnectionToClient? sender, bool ready)
        {
            if (sender == null)
            {
                return false;
            }

            var index = SeatIndexOf(sender.connectionId);
            if (index < 0)
            {
                return false;
            }

            var seat = _seats[index];
            seat.Ready = ready && seat.Role != RoleId.None;
            _seats[index] = seat;
            return true;
        }

        /// <summary>
        /// The settled lineup, for the host to hand to <c>MatchState</c>. Null until
        /// every seat holds a distinct role.
        /// </summary>
        [Server]
        public RoleSelection? SettledSelection()
        {
            EnsureSelection();
            return _selection!.IsComplete ? _selection : null;
        }

        private void EnsureSelection()
        {
            _selection ??= new RoleSelection();
        }

        private void OnSeatsChanged(SyncList<NetLobbySeat>.Operation op, int index, NetLobbySeat item)
        {
            SeatsChanged?.Invoke();
        }
    }
}
