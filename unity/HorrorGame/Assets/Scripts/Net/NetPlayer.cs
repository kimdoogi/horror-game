#nullable enable

using System;
using HorrorGame.Core;
using Mirror;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// One player's replicated presence: §05's 네트워크 동기화 항목 table, and
    /// nothing that is not on it.
    /// <para>
    /// The table has five rows — 위치 · 카메라 회전 (yaw + pitch) · 손전등 on/off ·
    /// 운반 상태 · 스태미나 — and every one of them is here for a stated reason
    /// rather than because it was convenient to send. Camera rotation is the one
    /// worth defending: §05 says "손전등이 포인터가 된다 ... 따라서 카메라 회전을
    /// 네트워크로 동기화해야 한다 — 남의 손전등 방향이 정보다", and it insists on
    /// pitch as well as yaw, because sweeping the floor and sweeping the ceiling are
    /// two different sentences.
    /// </para>
    /// <para>
    /// <b>Authority.</b> §13 puts the host in charge. The owner reports its own view
    /// (it is the only machine that has the mouse), the host sanity-clamps the
    /// position against the fastest speed in the game and then <em>the host's</em>
    /// value is what every observer receives. §13 also says 치팅 방어 거의 불필요 for
    /// a PvE game played with friends, so the clamp is there to survive a frame
    /// spike or a desync, not to fight an attacker.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [AddComponentMenu("HorrorGame/Net/Net Player")]
    public sealed class NetPlayer : NetworkBehaviour
    {
        /// <summary>
        /// How coarsely a teammate sees someone else's stamina: one step per second
        /// of sprint the bar holds.
        /// <para>
        /// §05: "스태미나 (주자) — 본인만 정확히, 남은 대략." The step size is derived
        /// from <see cref="GameConstants.SprintStaminaSeconds"/> rather than picked,
        /// so the sentence a teammate can form is "about four seconds of sprint left"
        /// — a judgement — and never "0.3712" — a readout. The exact float stays on
        /// the owner's machine and is never written to a SyncVar.
        /// </para>
        /// </summary>
        public static readonly int StaminaApproximationSteps =
            Mathf.CeilToInt(GameConstants.SprintStaminaSeconds);

        [SyncVar]
        private Vector3 _position;

        [SyncVar]
        private ushort _yawQ;

        [SyncVar]
        private ushort _pitchQ;

        [SyncVar(hook = nameof(OnFlashlightChanged))]
        private bool _flashlightOn;


        [SyncVar]
        private byte _staminaSteps;

        // DELETED with §04: the replicated _role SyncVar. Every runner is identical,
        // so there is nothing about a seat's identity worth a byte on the wire.

        [SyncVar]
        private int _seatIndex = -1;

        [Tooltip("HeadCameraAnchor on the player rig. Carries pitch; the body carries yaw. "
                 + "Leave empty and pitch is replicated but not shown.")]
        [SerializeField]
        private Transform? _headAnchor;

        [Tooltip("FlashlightMount on the player rig. Enabled and disabled from the replicated on/off flag.")]
        [SerializeField]
        private GameObject? _flashlightBeam;

        private INetPlayerViewSource? _source;
        private float _sendAccumulator;
        private double _lastReportTime;
        private bool _hasReported;

        /// <summary>
        /// Host-authoritative position. Remote avatars are moved toward it rather
        /// than snapped to it; see <see cref="Update"/>.
        /// </summary>
        public Vector3 NetworkedPosition => _position;

        /// <summary>Replicated yaw in degrees. §05's 카메라 회전.</summary>
        public float YawDegrees => NetViewCompression.UnpackYaw(_yawQ);

        /// <summary>
        /// Replicated pitch in degrees. §05 requires it: 바닥·천장을 비추는 것도
        /// 신호.
        /// </summary>
        public float PitchDegrees => NetViewCompression.UnpackPitch(_pitchQ);

        /// <summary>
        /// Whether this player's light is on. §05 lists it because it changes what
        /// the monster perceives (§06's <c>FlashlightNoticeDistance</c>), so it is a
        /// host input, not only a client visual.
        /// </summary>
        public bool FlashlightOn => _flashlightOn;

        /// <summary>§05's 운반 상태.</summary>

        /// <summary>Lobby seat this player occupies, or -1 before assignment.</summary>
        public int SeatIndex => _seatIndex;

        /// <summary>
        /// Stamina as everyone else sees it: quantised to
        /// <see cref="StaminaApproximationSteps"/>. §05's "남은 대략".
        /// </summary>
        public float ApproximateStaminaFraction =>
            StaminaApproximationSteps <= 0 ? 0f : _staminaSteps / (float)StaminaApproximationSteps;

        /// <summary>
        /// Stamina as <em>this</em> machine is entitled to see it: exact for the
        /// player who owns the legs, approximate for everyone else. §05's
        /// "본인만 정확히, 남은 대략" expressed as a single property, so a HUD cannot
        /// read the wrong one by mistake.
        /// </summary>
        public float StaminaFraction =>
            isOwned && _source != null ? MathfClamp01(_source.StaminaFraction) : ApproximateStaminaFraction;

        /// <summary>
        /// Whether a local controller is attached and this avatar is therefore reporting.
        /// <para>
        /// Exposed so <see cref="NetLocalRunner"/> can stop hunting for a rig once one is
        /// bound, and so a harness that binds its own source is not overwritten by the
        /// shipped binder a frame later. It is deliberately not "is this a real player" —
        /// a host with no window open owns a runner and reports nothing, and that is a
        /// legal state (§13 has no dedicated server, but it does have a headless one in
        /// CI).
        /// </para>
        /// </summary>
        public bool HasLocalSource => _source != null;

        /// <summary>
        /// Raised when this player's light goes on or off.
        /// <para>
        /// A discrete event rather than something to poll, because it has to be heard
        /// as well as seen: the click is an Items sound, and §06 turns the same flag
        /// into a change in what the monster notices at
        /// <see cref="GameConstants.FlashlightNoticeDistance"/>. Position and view
        /// angle get no equivalent event on purpose — they change every tick, and an
        /// event per tick is a poll with extra steps.
        /// </para>
        /// <para>
        /// Driven by a SyncVar hook, which fires on a client when the value arrives
        /// and on the host when it is set. §13 has no dedicated server — the host is
        /// one of the four players — so between them that is every machine in a
        /// session.
        /// </para>
        /// </summary>
        public event Action<bool>? FlashlightChanged;

        // DELETED with §08 and §03's 목표물: the 운반 상태 SyncVar `_carry`, the `Carry`
        // property, the `CarryChanged` event, the `NetCarryState` argument on
        // CmdReportView and its hook. §05 listed 운반 상태 as one of the five rows every
        // player replicates, for "애니메이션 + 속도".
        //
        // A runner carries a torch and nothing else, so the row was Empty on every
        // machine, on every tick, for the whole race — a SyncVar with one reachable
        // value. At twenty players that is the exact kind of per-tick traffic §16-1 is
        // now the top open question about, spent on nothing.

        /// <summary>
        /// Attaches the local controller that produces §05's five rows. Called by
        /// the first-person controller on the owning client; ignored anywhere else,
        /// because a non-owner has no business reporting a position.
        /// </summary>
        public void BindLocalSource(INetPlayerViewSource source)
        {
            if (!isOwned)
            {
                Debug.LogWarning("[Net] BindLocalSource on a NetPlayer this machine does not own. Ignored.");
                return;
            }

            _source = source;
        }

        /// <summary>Detaches the local controller. Safe to call when nothing is bound.</summary>
        public void UnbindLocalSource() => _source = null;

        /// <summary>
        /// Host-side assignment of the lobby seat this player runs from. Was
        /// <c>AssignRole(int, RoleId)</c>; §04 is deleted, so the seat index is the
        /// whole of a runner's identity — it is what §02's standings key on.
        /// </summary>
        [Server]
        public void AssignSeat(int seatIndex)
        {
            _seatIndex = seatIndex;
        }

        /// <summary>
        /// Moves the player without tripping the speed clamp: an extraction, a
        /// descent, a spawn. §03's 왕복 crosses between the surface and the basement
        /// in one step and must not be mistaken for a client claiming to have run
        /// there.
        /// </summary>
        [Server]
        public void TeleportTo(Vector3 position)
        {
            _position = position;
            _hasReported = false;
            transform.position = position;
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

        private void Update()
        {
            // Before the object is spawned there is nothing authoritative to send and
            // nothing authoritative to apply — and applying anyway would drag a
            // freshly instantiated avatar toward the origin for a frame, which reads
            // on screen as a teammate blinking across the map.
            if (netId == 0)
            {
                return;
            }

            if (isOwned && _source != null)
            {
                AccumulateAndReport(Time.deltaTime);
            }

            if (!isOwned)
            {
                ApplyRemoteView(Time.deltaTime);
            }
        }

        /// <summary>
        /// Sends §05's five rows at <see cref="GameConstants.NetworkSendRate"/>.
        /// <para>
        /// Rate-limited rather than sent per frame because §13's whole infrastructure
        /// argument rests on the traffic staying small enough that Steam's free relay
        /// carries it. A 144 Hz client and a 30 Hz client must put the same load on
        /// the host.
        /// </para>
        /// </summary>
        private void AccumulateAndReport(float deltaSeconds)
        {
            var source = _source;
            if (source == null)
            {
                return;
            }

            _sendAccumulator += deltaSeconds;
            var interval = 1f / GameConstants.NetworkSendRate;
            if (_sendAccumulator < interval)
            {
                return;
            }

            _sendAccumulator = 0f;

            var steps = (byte)Mathf.Clamp(
                Mathf.RoundToInt(MathfClamp01(source.StaminaFraction) * StaminaApproximationSteps),
                0,
                StaminaApproximationSteps);

            CmdReportView(
                source.Position,
                NetViewCompression.PackYaw(source.YawDegrees),
                NetViewCompression.PackPitch(source.PitchDegrees),
                source.FlashlightOn,
                steps);
        }

        /// <summary>
        /// The owner's report. Everything in the signature is a primitive §05 names;
        /// there is deliberately nothing here that could carry a rules decision, so
        /// the host stays the only machine that resolves one.
        /// </summary>
        [Command]
        private void CmdReportView(
            Vector3 position,
            ushort yawQ,
            ushort pitchQ,
            bool flashlightOn,
            byte staminaSteps)
        {
            var now = NetworkTime.localTime;

            if (_hasReported)
            {
                // §05's fastest row is the Runner's sprint. Nothing in the design
                // moves faster, so anything beyond it in one interval is a spike or a
                // desync — clamped rather than rejected, because dropping the update
                // would leave the avatar behind and §13 is explicit that this is a
                // PvE game among friends, not a contest with a cheater.
                var elapsed = (float)Math.Max(0d, now - _lastReportTime);
                var reach = GameConstants.RunnerSprintSpeed * elapsed;
                position = Vector3.MoveTowards(_position, position, reach);
            }

            _lastReportTime = now;
            _hasReported = true;

            _position = position;
            _yawQ = yawQ;
            _pitchQ = pitchQ;
            _flashlightOn = flashlightOn;
            _staminaSteps = (byte)Mathf.Clamp(staminaSteps, 0, StaminaApproximationSteps);

            // The host is also a player. Its own avatar has to move too.
            if (isServer)
            {
                transform.position = _position;
            }
        }

        private void OnFlashlightChanged(bool previous, bool current)
        {
            if (_flashlightBeam != null)
            {
                _flashlightBeam.SetActive(current);
            }

            FlashlightChanged?.Invoke(current);
        }

        /// <summary>
        /// Puts a remote player's replicated view onto its rig.
        /// <para>
        /// Smoothed toward the target rather than snapped, at a rate tied to
        /// <see cref="GameConstants.NetworkSendRate"/> so one snapshot's worth of
        /// motion is consumed in one snapshot's worth of time.
        /// </para>
        /// <para>
        /// Yaw turns the body; pitch turns the head anchor only. That split is not
        /// cosmetic — the flashlight hangs off the rig's mount, so if pitch were
        /// applied to the root the beam would still land in the right place but the
        /// body would lie face-down on the floor to put it there. §05 makes the beam
        /// the message, and a message delivered by a broken pose is one teammates
        /// stop trusting.
        /// </para>
        /// </summary>
        private void ApplyRemoteView(float deltaSeconds)
        {
            var t = Mathf.Clamp01(deltaSeconds * GameConstants.NetworkSendRate);
            transform.position = Vector3.Lerp(transform.position, _position, t);

            var bodyTarget = Quaternion.Euler(0f, YawDegrees, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, bodyTarget, t);

            if (_headAnchor != null)
            {
                var headTarget = Quaternion.Euler(-PitchDegrees, 0f, 0f);
                _headAnchor.localRotation = Quaternion.Slerp(_headAnchor.localRotation, headTarget, t);
            }

            if (_flashlightBeam != null && _flashlightBeam.activeSelf != _flashlightOn)
            {
                _flashlightBeam.SetActive(_flashlightOn);
            }
        }

        private static float MathfClamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
    }
}
