#nullable enable

using System;
using System.Globalization;

namespace HorrorGame.Steam
{
    /// <summary>
    /// A player's platform identity, reduced to the one thing every platform
    /// agrees on: a 64-bit number that is stable for the length of a session.
    /// <para>
    /// On Steam this holds a SteamID64 (§13's 계정 / 신원 row). Nothing outside the
    /// Steamworks backend is allowed to know that, which is what makes §13's claim
    /// — 음성 · 네트워킹을 인터페이스로 추상화해두면 ... 코드 몇 줄 차이다 — true rather
    /// than aspirational: a second platform introduces one more backend that mints
    /// these, and every consumer keeps compiling.
    /// </para>
    /// <para>
    /// Wrapped in a struct instead of passed as a bare <c>ulong</c> because it
    /// travels next to lobby ids, seeds and stat values, and a swapped argument
    /// between two <c>ulong</c>s is a bug the compiler can catch for free.
    /// </para>
    /// </summary>
    public readonly struct NetUserId : IEquatable<NetUserId>
    {
        /// <summary>The raw platform id. Zero means "nobody".</summary>
        public readonly ulong Value;

        /// <summary>Wraps a raw platform id.</summary>
        public NetUserId(ulong value)
        {
            Value = value;
        }

        /// <summary>The absent id, used for "no lobby" and "no host" rather than a nullable.</summary>
        public static NetUserId None => new NetUserId(0UL);

        /// <summary>False for <see cref="None"/>, which is the only reserved value.</summary>
        public bool IsValid => Value != 0UL;

        /// <inheritdoc />
        public bool Equals(NetUserId other) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is NetUserId other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Value equality.</summary>
        public static bool operator ==(NetUserId a, NetUserId b) => a.Equals(b);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(NetUserId a, NetUserId b) => !a.Equals(b);

        /// <summary>
        /// The decimal form. This is also the wire form: it is what goes into lobby
        /// data under <see cref="SteamAppConfig.LobbyKeys.HostId"/> and what a
        /// transport hands to Mirror as a network address, so it must stay
        /// culture-invariant.
        /// </summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses the form produced by <see cref="ToString"/>. Returns false rather
        /// than throwing, because the input is usually lobby data written by another
        /// machine — possibly a different game version.
        /// </summary>
        public static bool TryParse(string? text, out NetUserId id)
        {
            if (!string.IsNullOrWhiteSpace(text)
                && ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var raw))
            {
                id = new NetUserId(raw);
                return id.IsValid;
            }

            id = None;
            return false;
        }
    }
}
