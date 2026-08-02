#nullable enable

using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// The one component that puts a human on the wire.
    /// <para>
    /// <b>What was missing.</b> <see cref="NetPlayer"/> has always known how to send
    /// §05's five rows, and <see cref="INetPlayerViewSource"/> has always described what
    /// to send — but nothing in shipped code ever called
    /// <see cref="NetPlayer.BindLocalSource"/>. Every test that proved a runner
    /// replicates supplied its own scripted view, so the suite was green and a real
    /// person moving their character sent nothing at all. This class is the caller.
    /// </para>
    /// <para>
    /// <b>Two jobs, both keyed on ownership.</b> On the copy this machine owns it binds
    /// the local rig as the report source, and it switches that copy's body off — §01's
    /// local player is already standing in the scene with the camera on their head, so a
    /// second body on the replicated proxy would be a motionless duplicate of themselves
    /// at their own spawn marker. On a copy this machine does <em>not</em> own it does
    /// nothing whatsoever: that copy is somebody else, its position arrives as a SyncVar,
    /// and its body is the whole point of it being drawn.
    /// </para>
    /// <para>
    /// <b>Polling, and why that is not laziness.</b> Ownership is set from Mirror's spawn
    /// message and the rig can arrive much later than the runner does — a body is created
    /// when a connection reports ready, which on the shipped path is while the party is
    /// still in §11's lobby, several seconds before the descent's scene is loaded. There
    /// is no single event that means "the owner now has legs", so this asks once per send
    /// interval (<see cref="GameConstants.NetworkSendRate"/>). That cadence is derived,
    /// not chosen: there is no point looking for a driver more often than the wire could
    /// carry what it produces.
    /// </para>
    /// <para>
    /// A plain <c>MonoBehaviour</c> rather than a second <c>NetworkBehaviour</c>. It has
    /// no SyncVar, no Command and no ClientRpc, so a <c>NetworkBehaviour</c> would buy
    /// only <c>OnStartAuthority</c> and would cost a second entry in
    /// <c>NetworkIdentity.NetworkBehaviours</c> — an ordering that has to agree byte for
    /// byte between host and client. Both ends build a runner from
    /// <see cref="NetRunner.Build"/> so they would agree, but "would agree" is a promise
    /// about a future edit rather than a property of this one.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetPlayer))]
    [AddComponentMenu("HorrorGame/Net/Net Local Runner")]
    public sealed class NetLocalRunner : MonoBehaviour
    {
        /// <summary>
        /// Seconds between attempts to find the local rig. One send interval — see the
        /// class remarks.
        /// </summary>
        public static readonly float SearchIntervalSeconds = 1f / GameConstants.NetworkSendRate;

        private NetPlayer? _player;
        private float _nextSearchTime;
        private bool _bodyResolved;
        private bool _reportedMissingFactory;
        private bool _reportedBound;

        /// <summary>
        /// The source this component attached, or null while nothing is bound. Read by
        /// tests so a failure can say <em>which</em> half broke — no factory, no rig, or
        /// a bound rig whose values are not crossing the wire.
        /// </summary>
        public INetPlayerViewSource? BoundSource { get; private set; }

        private void Awake()
        {
            _player = GetComponent<NetPlayer>();
        }

        private void Update()
        {
            var player = _player;
            if (player == null || player.netId == 0)
            {
                // Not spawned yet. isOwned is not meaningful before the identity has a
                // netId, and acting on it here would switch the body off on the copy that
                // is about to be told it belongs to somebody else.
                return;
            }

            if (!player.isOwned)
            {
                return;
            }

            ResolveOwnBody();
            TryBind(player);
        }

        /// <summary>
        /// Switches off the body on the copy this machine owns.
        /// <para>
        /// Deactivated rather than destroyed, because ownership is a fact about this
        /// machine and not about the object: a spectator hand-off or a re-parent would
        /// want it back, and a destroyed hierarchy cannot be given back. Done once —
        /// <see cref="_bodyResolved"/> — so a scene view is not fighting the component
        /// every frame.
        /// </para>
        /// </summary>
        private void ResolveOwnBody()
        {
            if (_bodyResolved)
            {
                return;
            }

            _bodyResolved = true;

            var visual = transform.Find(NetRunner.VisualChildName);
            if (visual != null)
            {
                visual.gameObject.SetActive(false);
            }
        }

        private void TryBind(NetPlayer player)
        {
            if (BoundSource != null || player.HasLocalSource)
            {
                return;
            }

            if (Time.unscaledTime < _nextSearchTime)
            {
                return;
            }

            _nextSearchTime = Time.unscaledTime + SearchIntervalSeconds;

            var factory = NetRunner.LocalViewSourceFactory;
            if (factory == null)
            {
                if (!_reportedMissingFactory)
                {
                    _reportedMissingFactory = true;

                    // Once, and loudly. This is the exact state the project shipped in:
                    // a wire that works with nobody on it. A silent null here would put
                    // it straight back.
                    Debug.LogWarning(
                        "[Net] This machine owns a runner and nothing installed "
                        + "NetRunner.LocalViewSourceFactory, so §05's five rows will never be sent — a human "
                        + "moving their character is invisible to everybody else. HorrorGame.Net.PlayerBridge "
                        + "installs it from a [RuntimeInitializeOnLoadMethod]; check that assembly is in the "
                        + "build.",
                        this);
                }

                return;
            }

            var source = factory();
            if (source == null)
            {
                // No rig on this machine yet. Normal in §11's lobby and normal on a
                // headless host, so it is not worth a line in the log.
                return;
            }

            player.BindLocalSource(source);
            BoundSource = source;

            if (!_reportedBound)
            {
                _reportedBound = true;
                Debug.Log(
                    "[Net] Local runner bound to " + source.GetType().Name + " — §05's 위치 · 카메라 회전 · "
                    + "손전등 · 운반 상태 · 스태미나 are now leaving this machine.",
                    this);
            }
        }
    }
}
