#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Match;
using Mirror;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// §11's lobby: the seats, who is in them, and who has said they are ready.
    /// <para>
    /// It owns no rule about who may play — a race has no lineup to be legal or
    /// illegal, only seats that are occupied or vacant. That is ARCHITECTURE §1's
    /// split holding by having nothing left to split: what used to live here was a
    /// mirror of §11's role table, and a duplicate rule in a MonoBehaviour is one
    /// that <c>dotnet test</c> cannot see and the one that drifts.
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

        /// <summary>
        /// The lobby currently in the scene, or null. There is one per process: §13's
        /// model is one host and four friends, never two lobbies at once.
        /// </summary>
        public static NetLobby? Instance { get; private set; }

        /// <summary>The four seats, in order. Read-only on every machine, including the host.</summary>
        public IReadOnlyList<NetLobbySeat> Seats => _seats;

        /// <summary>Raised on every machine whenever any seat changes.</summary>
        public event Action? SeatsChanged;

        // DELETED with §04: LineupComplete, MissingRole, Gap, IsRoleTaken,
        // AvailableRoles, CmdClaimRole, CmdReleaseRole, ServerClaimRole,
        // ServerReleaseRole, SettledSelection, EnsureSelection and the RoleSelection
        // field behind all of them.
        //
        // §11's lobby was "four seats, five roles, exactly one role left on the
        // table", and LineupComplete — not readiness — was the start condition. A
        // race starts 2~20 identical runners: 「캐릭터는 다 똑같이 생겨도되지」.
        // There is nothing to claim, so there is no claim to refuse, no absent role
        // to price, and no lineup that could be incomplete.
        //
        // This was also a LIVE BUG, not just dead weight: ServerSetReady gated
        // readiness on `seat.Role != RoleId.None`, so once the role buttons went a
        // seat could never ready up and the lobby could never start a race.

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
                seat.Ready = false;
                _seats[i] = seat;
                return i;
            }

            return -1;
        }

        /// <summary>Empties a seat.</summary>
        [Server]
        public void Vacate(int connectionId)
        {
            var index = SeatIndexOf(connectionId);
            if (index < 0)
            {
                return;
            }

            _seats[index] = NetLobbySeat.Vacant;
        }

        /// <inheritdoc />
        public override void OnStartServer()
        {
            Instance = this;

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

        /// <summary>Marks a seat ready, or not.</summary>
        [Command(requiresAuthority = false)]
        public void CmdSetReady(bool ready, NetworkConnectionToClient? sender = null) =>
            ServerSetReady(sender, ready);

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

            // Was `ready && seat.Role != RoleId.None`. With §04 deleted every seat
            // held RoleId.None for ever, so that clause made readiness unreachable
            // and the lobby unable to start a race at all.
            seat.Ready = ready;
            _seats[index] = seat;
            return true;
        }


        private void OnSeatsChanged(SyncList<NetLobbySeat>.Operation op, int index, NetLobbySeat item)
        {
            SeatsChanged?.Invoke();
        }
    }
}
