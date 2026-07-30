#nullable enable

#if HORRORGAME_STEAMWORKS

using System.Globalization;
using Steamworks;

namespace HorrorGame.Steam.SteamworksBackend
{
    /// <summary>
    /// §13's 계정 / 신원 row: the local SteamID64 and persona names, and nothing more.
    /// <para>
    /// §13 answers 계정 · 프로필 with "Steam ID" and no database, which is only true if
    /// the game is content with an id and a name. It is: §15 discarded between-match
    /// progression, so there is no profile to keep and nothing to look up.
    /// </para>
    /// </summary>
    public sealed class SteamworksIdentity : IUserIdentity
    {
        /// <summary>Captures the local identity at initialisation. Steam cannot change it mid-process.</summary>
        public SteamworksIdentity()
        {
            LocalId = new NetUserId(SteamUser.GetSteamID().m_SteamID);
            LocalName = SteamFriends.GetPersonaName();
        }

        /// <inheritdoc />
        public NetUserId LocalId { get; }

        /// <inheritdoc />
        public string LocalName { get; }

        /// <inheritdoc />
        public bool CanResolveNames => true;

        /// <summary>
        /// The persona name Steam has cached for <paramref name="id"/>, or a readable
        /// placeholder.
        /// <para>
        /// Steam only knows names it has reason to know — a friend, or someone in the same
        /// lobby — and returns an empty string otherwise. An empty string in the lobby list
        /// looks like a bug in the game, so it becomes the last digits of the id instead.
        /// </para>
        /// </summary>
        public string NameOf(NetUserId id)
        {
            if (!id.IsValid)
            {
                return "Unknown";
            }

            if (id == LocalId)
            {
                return LocalName;
            }

            var name = SteamFriends.GetFriendPersonaName(new CSteamID(id.Value));
            return string.IsNullOrWhiteSpace(name)
                ? "Player " + (id.Value % 10000UL).ToString(CultureInfo.InvariantCulture)
                : name;
        }
    }
}

#endif
