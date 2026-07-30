#nullable enable

using System;
using HorrorGame.Core;
using HorrorGame.Core.Monster;
using Mirror;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// The monster as clients are allowed to know it: a position, a facing and one
    /// of §06's five state names.
    /// <para>
    /// §13 runs the AI on the host — "권한 모델: 호스트 권한 — 괴물 AI를 호스트가
    /// 돌린다" — and the brain never leaves it. <see cref="MonsterBrain"/> is held in
    /// a plain field that is null on every client, and none of what it knows is
    /// replicated: not <c>ChaseTargetId</c>, not <c>LastSeenPosition</c>, not
    /// <c>Destination</c>, not <c>KnownZoneIds</c>. Those are the answers §04's
    /// 관측자 and 청음사 are paid to work out, and a client that could read them
    /// would make both roles decorative.
    /// </para>
    /// <para>
    /// The state enum is the exception, and it earns its place. §06 builds the game's
    /// best moment on it: 정지 makes no sound, "그러면 청음사가 위치를 잃고 ...
    /// 침묵이 가장 무서운 소리다." Silence has to be produced by an audio source
    /// deciding not to play, on the machine that owns the speakers, so the state has
    /// to arrive. What arrives is only the state — never why the monster is in it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [AddComponentMenu("HorrorGame/Net/Net Monster")]
    public sealed class NetMonster : NetworkBehaviour
    {
        [SyncVar]
        private Vector3 _position;

        [SyncVar]
        private ushort _facingYawQ;

        [SyncVar(hook = nameof(OnStateChanged))]
        private MonsterStateId _state = MonsterStateId.Patrol;

        /// <summary>
        /// §06's brain. Host-only, and structurally so: it is a class the weaver
        /// cannot serialise, it is never named in a SyncVar or a remote call, and
        /// <see cref="NetReplicationAudit"/> fails the build if that ever changes.
        /// </summary>
        private MonsterBrain? _brain;

        /// <summary>Replicated position. Clients interpolate toward it; they do not predict it.</summary>
        public Vector3 NetworkedPosition => _position;

        /// <summary>Replicated facing, in degrees of yaw.</summary>
        public float FacingYawDegrees => NetViewCompression.UnpackYaw(_facingYawQ);

        /// <summary>Which of §06's five states the monster is in.</summary>
        public MonsterStateId State => _state;

        /// <summary>
        /// Whether the monster is making noise. §06: every state but 정지 has
        /// 발소리 in the 소리 column. Derived on each machine from
        /// <see cref="State"/> rather than sent, because a separate "is audible" flag
        /// could drift out of step with the state and produce the one thing §06
        /// cannot afford — a monster that is silent while moving, or audible while
        /// still.
        /// </summary>
        public bool IsAudible => _state != MonsterStateId.Standstill;

        /// <summary>§06: 추격 adds 포효 to the footsteps.</summary>
        public bool IsRoaring => _state == MonsterStateId.Chase;

        /// <summary>Raised on every machine when §06's state changes. Audio and the animator listen.</summary>
        public event Action<MonsterStateId>? StateChanged;

        /// <summary>
        /// Hands the host's brain to this transform. Called once, by whatever built
        /// the level. Refused on a client, loudly: a client that has a brain has the
        /// answers, which is the failure §13 names.
        /// </summary>
        [Server]
        public void Bind(MonsterBrain brain)
        {
            _brain = brain ?? throw new ArgumentNullException(nameof(brain));
        }

        /// <summary>
        /// Tells one connection who the monster is hunting. §04's 관측자, and the
        /// only fact about the brain that ever leaves the host.
        /// <para>
        /// A <c>TargetRpc</c> rather than a SyncVar for the reason §04 gives: the
        /// 관측자 is "유일하게 살 수 없는 정보를 제공하는 직업" (§11), and information
        /// only one player has is only worth having if it reaches only that player.
        /// The payload is a network id the receiving client already has — a name for
        /// something it can see — not a position.
        /// </para>
        /// </summary>
        [Server]
        public void RevealFocusTo(NetworkConnectionToClient observer, uint focusedPlayerNetId)
        {
            if (observer == null)
            {
                return;
            }

            TargetFocus(observer, focusedPlayerNetId);
        }

        /// <inheritdoc />
        public override void OnStartServer()
        {
            _position = transform.position;
            syncInterval = 1f / GameConstants.NetworkSendRate;
        }

        /// <inheritdoc />
        public override void OnStartClient()
        {
            syncInterval = 1f / GameConstants.NetworkSendRate;

            if (!isServer)
            {
                transform.position = _position;
            }
        }

        /// <summary>
        /// Publishes what the brain decided. The brain itself is stepped by the
        /// gameplay layer at <see cref="GameConstants.FixedStep"/> — ARCHITECTURE §3
        /// says stateful core systems never read a clock — so this only reads.
        /// </summary>
        private void LateUpdate()
        {
            if (netId == 0)
            {
                return;
            }

            if (isServer)
            {
                PublishFromBrain();
            }
            else
            {
                ApplyReplicated(Time.deltaTime);
            }
        }

        [Server]
        private void PublishFromBrain()
        {
            var brain = _brain;
            if (brain == null)
            {
                return;
            }

            _position = new Vector3(brain.Position.X, brain.Position.Y, brain.Position.Z);
            _state = brain.State;

            var facing = brain.Facing;
            if (facing.SqrMagnitudeFlat > 0f)
            {
                _facingYawQ = NetViewCompression.PackYaw(Mathf.Atan2(facing.X, facing.Z) * Mathf.Rad2Deg);
            }

            transform.position = _position;
            transform.rotation = Quaternion.Euler(0f, FacingYawDegrees, 0f);
        }

        /// <summary>
        /// Moves the client-side monster toward its replicated position.
        /// <para>
        /// Interpolated, and capped at §06's fastest tier speed
        /// (<see cref="GameConstants.ThreatSpeedBeforeSunrise"/>) so a dropped
        /// snapshot cannot make it cross a wall in one frame. ARCHITECTURE §5 asks
        /// for exactly this case: "a frame spike must not teleport the monster
        /// through a wall."
        /// </para>
        /// </summary>
        private void ApplyReplicated(float deltaSeconds)
        {
            var reach = GameConstants.ThreatSpeedBeforeSunrise * deltaSeconds;
            var stepped = Vector3.MoveTowards(transform.position, _position, reach);

            // A gap larger than one search radius is a respawn or a scene change, not
            // a frame spike; sliding across it would draw the monster walking through
            // the building.
            if ((transform.position - _position).sqrMagnitude
                > GameConstants.SearchRadius * GameConstants.SearchRadius)
            {
                stepped = _position;
            }

            transform.position = stepped;

            var t = Mathf.Clamp01(deltaSeconds * GameConstants.NetworkSendRate);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.Euler(0f, FacingYawDegrees, 0f), t);
        }

        private void OnStateChanged(MonsterStateId previous, MonsterStateId current)
        {
            StateChanged?.Invoke(current);
        }

        [TargetRpc]
        private void TargetFocus(NetworkConnection target, uint focusedPlayerNetId)
        {
            FocusRevealed?.Invoke(focusedPlayerNetId);
        }

        /// <summary>
        /// Raised on the 관측자's machine only, carrying the network id of the player
        /// the monster is hunting. Nobody else's client ever raises it.
        /// </summary>
        public event Action<uint>? FocusRevealed;
    }
}
