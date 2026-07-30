#nullable enable

using Mirror.FizzySteam;
using UnityEngine;

namespace HorrorGame.Net.SteamTransport
{
    /// <summary>
    /// Registers FizzySteamworks as the platform transport. §13's 네트워킹 row:
    /// "Mirror + FizzySteamworks — 자료·커뮤니티 최다 — 막혔을 때 검색이 된다."
    /// <para>
    /// This whole assembly is skipped when the FizzySteamworks package is absent —
    /// its <c>defineConstraints</c> is satisfied only by the <c>versionDefines</c>
    /// entry that fires on <c>com.mirror.steamworks.net</c> — so <c>HorrorGame.Net</c>
    /// never has to reference a package that might not be installed, and never
    /// inherits FizzySteamworks' editor-and-standalone platform restriction. It is
    /// the same shape <c>HorrorGame.Steam.SteamworksBackend</c> uses for
    /// Steamworks.NET, and for the same reason: §14 develops steps 1–3 with no Steam
    /// at all.
    /// </para>
    /// </summary>
    public static class FizzyTransportBackend
    {
        /// <summary>
        /// Announces the transport to <see cref="NetTransportRegistry"/> before
        /// anything can ask for one. Nothing calls this; Unity does, at
        /// <c>AfterAssembliesLoaded</c>, which is what keeps the dependency pointing
        /// in the safe direction.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Register()
        {
            NetTransportRegistry.Register("FizzySteamworks", owner =>
            {
                var transport = owner.GetComponent<FizzySteamworks>();
                if (transport == null)
                {
                    transport = owner.AddComponent<FizzySteamworks>();
                }

                // §13's decisive free lunch: "릴레이가 무료인 것이 결정적이다."
                // Steam Datagram Relay is what makes two players behind NAT reach
                // each other without a relay server on our side, and it is the one
                // line item that would otherwise cost real bandwidth money.
                transport.AllowSteamRelay = true;

                return transport;
            });
        }
    }
}
