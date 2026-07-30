#nullable enable

namespace HorrorGame.Steam
{
    /// <summary>
    /// Who the local player is, and how to render someone else's name.
    /// <para>
    /// §13's DB가 필요할 것 같은 것들 table answers 계정 · 프로필 with "Steam ID" and
    /// no database, which is only true if the game never needs more than an id and a
    /// display name. So that is all this exposes. Avatars are deliberately absent:
    /// they would drag a texture-loading path and a callback into the seam for a
    /// lobby of four friends who already know each other.
    /// </para>
    /// <para>
    /// The offline stand-in returns a stable synthetic id, so host-authority code
    /// that keys players by <see cref="NetUserId"/> works identically in §14 step 2's
    /// two-instances-on-one-PC setup.
    /// </para>
    /// </summary>
    public interface IUserIdentity
    {
        /// <summary>
        /// The local player's id. Valid even offline — code that keys dictionaries by
        /// player must never have to special-case the backend.
        /// </summary>
        NetUserId LocalId { get; }

        /// <summary>The local player's display name.</summary>
        string LocalName { get; }

        /// <summary>
        /// A display name for <paramref name="id"/>, or a readable placeholder built
        /// from the id when the platform has never heard of them. Never null and
        /// never empty: this string goes straight into the lobby list and a blank row
        /// looks like a bug in the game.
        /// </summary>
        string NameOf(NetUserId id);

        /// <summary>
        /// True when the platform can name arbitrary players. False offline, which is
        /// how UI knows that <see cref="NameOf"/> is producing placeholders.
        /// </summary>
        bool CanResolveNames { get; }
    }
}
