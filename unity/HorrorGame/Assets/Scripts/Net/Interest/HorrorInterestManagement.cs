#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using Mirror;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// Decides who is sent what. Two radii, from <see cref="NetInterestScope"/>.
    /// <para>
    /// Interest management in a four-player game is not a bandwidth optimisation —
    /// §13 is blunt that "4인 게임에서 성능 한계에 부딪힐 일은 없다". It is here for
    /// the other reason: what a client is never sent, a client cannot read out of
    /// memory. Culling the objective and the clue props to the radius a light reaches
    /// (§03's 어둠 = 목표의 잠금장치) is the network expression of §13's
    /// "클라이언트에 보내면 메모리에서 읽힌다", and it is doing security work that
    /// happens to also save traffic.
    /// </para>
    /// <para>
    /// Distance is straight-line, not path length. §12's S-corridors mean the two
    /// diverge, and a path query per object per connection per rebuild would cost far
    /// more than it saves at this party size — but note the consequence honestly: a
    /// player one wall away from the objective is inside the radius. The wall is the
    /// renderer's problem, not the network's, and §03's constraint survives it,
    /// because being one wall away is already being there.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Net/Horror Interest Management")]
    public sealed class HorrorInterestManagement : InterestManagement
    {
        private readonly Dictionary<NetworkIdentity, NetInterestScope> _scopes =
            new Dictionary<NetworkIdentity, NetInterestScope>();

        private double _lastRebuildTime;

        /// <summary>
        /// How often the observer sets are rebuilt, in seconds.
        /// <para>
        /// One network tick. Anything slower and an object could be a snapshot late
        /// to appear, which for the monster is the difference between hearing it
        /// arrive and finding it already there. Anything faster is wasted: nothing is
        /// sent between ticks anyway. The cost is
        /// <see cref="GameConstants.PlayersPerMatch"/> distance checks per spawned
        /// object — four — which is why the design can afford the simplest possible
        /// answer here.
        /// </para>
        /// </summary>
        public static float RebuildInterval => 1f / GameConstants.NetworkSendRate;

        /// <inheritdoc />
        [ServerCallback]
        public override void ResetState()
        {
            _lastRebuildTime = 0d;
            _scopes.Clear();
        }

        /// <inheritdoc />
        public override void OnSpawned(NetworkIdentity identity)
        {
            if (identity != null && identity.TryGetComponent(out NetInterestScope scope))
            {
                _scopes[identity] = scope;
            }
        }

        /// <inheritdoc />
        public override void OnDestroyed(NetworkIdentity identity)
        {
            if (identity != null)
            {
                _scopes.Remove(identity);
            }
        }

        /// <inheritdoc />
        public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient newObserver)
        {
            if (identity == null || newObserver == null || newObserver.identity == null)
            {
                return false;
            }

            return WithinRange(identity, newObserver.identity.transform.position);
        }

        /// <inheritdoc />
        public override void OnRebuildObservers(
            NetworkIdentity identity, HashSet<NetworkConnectionToClient> newObservers)
        {
            if (identity == null)
            {
                return;
            }

            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn == null || !conn.isAuthenticated || conn.identity == null)
                {
                    continue;
                }

                if (WithinRange(identity, conn.identity.transform.position))
                {
                    newObservers.Add(conn);
                }
            }
        }

        [ServerCallback]
        private void LateUpdate()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            if (NetworkTime.localTime - _lastRebuildTime < RebuildInterval)
            {
                return;
            }

            _lastRebuildTime = NetworkTime.localTime;
            RebuildAll();
        }

        /// <summary>
        /// Squared-distance test against the identity's own class radius. A spawned
        /// object with no <see cref="NetInterestScope"/> is treated as
        /// <see cref="NetInterestClass.Perception"/> — the safe default, because
        /// forgetting the component on a player makes them pop in, while forgetting
        /// it on a secret would be silent.
        /// </summary>
        private bool WithinRange(NetworkIdentity identity, Vector3 observerPosition)
        {
            var interestClass = _scopes.TryGetValue(identity, out var scope)
                ? scope.InterestClass
                : NetInterestClass.Perception;

            var range = NetInterestScope.RangeFor(interestClass);
            return (identity.transform.position - observerPosition).sqrMagnitude <= range * range;
        }
    }
}
