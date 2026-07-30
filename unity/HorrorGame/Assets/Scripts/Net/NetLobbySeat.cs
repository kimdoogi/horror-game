#nullable enable

using HorrorGame.Core.Roles;

namespace HorrorGame.Net
{
    /// <summary>
    /// One of §11's four seats, as everybody in the lobby sees it.
    /// <para>
    /// Everything in here is public by design. §11's premise is that the table is
    /// face-up — "매판 하나가 빠지고, 그게 그 판의 성격이 된다" only works if the
    /// four players can see which one is going to be missing while they still have a
    /// choice about it. There is nothing here worth hiding, and — checked by
    /// <see cref="NetReplicationAudit"/> — nothing here that could hold something
    /// that is.
    /// </para>
    /// </summary>
    public struct NetLobbySeat
    {
        /// <summary>Transport connection filling the seat, or -1 while it is empty.</summary>
        public int ConnectionId;

        /// <summary>Platform display name, from <c>IUserIdentity</c>. Empty for an unoccupied seat.</summary>
        public string DisplayName;

        /// <summary>The seat's pick, or <see cref="RoleId.None"/>. §04's five.</summary>
        public RoleId Role;

        /// <summary>Whether this player has said they are ready to descend.</summary>
        public bool Ready;

        /// <summary>True when somebody is sitting here.</summary>
        public bool Occupied => ConnectionId >= 0;

        /// <summary>An empty seat.</summary>
        public static NetLobbySeat Vacant =>
            new NetLobbySeat { ConnectionId = -1, DisplayName = string.Empty, Role = RoleId.None, Ready = false };
    }
}
