#nullable enable

using System;
using HorrorGame.Core;
using HorrorGame.Net.Host;
using Mirror;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// One player's channel for asking the host "am I reading a clue?".
    /// <para>
    /// ARCHITECTURE §4 specifies the shape exactly: "a client asks the host 'am I
    /// reading a clue?', and the host replies with the rendered glyph for
    /// <em>that</em> clue only." This is that channel, and the shape is enforced by
    /// what the two messages can carry:
    /// </para>
    /// <list type="bullet">
    /// <item>Up: a clue id and four floats describing the reader's own circumstances
    /// — light, angle, wear. Nothing about the clue, because the client does not
    /// know anything about the clue.</item>
    /// <item>Down: one <c>string</c> for one clue id. Not a <c>ClueReport</c>, not a
    /// <c>SiteLabel</c>, not a <c>ClueGlyph</c>. A sentence a player could have read
    /// off a wall and could repeat out loud, which is precisely §03's channel:
    /// "그 자리에서 보고, 기억해서, 말로 전달해야 한다."</item>
    /// </list>
    /// <para>
    /// One terminal per player, living on the player prefab. That is what makes the
    /// reply naturally private — a <c>TargetRpc</c> to the owner — and what makes the
    /// request naturally authenticated, since Mirror only lets the owning client
    /// issue a <c>Command</c> on an object it owns.
    /// </para>
    /// <para>
    /// <b>Time is measured by the host.</b> The client says when it starts and when
    /// it stops; the host subtracts. §03 makes read duration matter — a hurried look
    /// misreads where a held one does not (<c>ClueConfidentReadSeconds</c>) — so a
    /// client-supplied duration would be the one number worth lying about, and it is
    /// the one number the host can measure for itself.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [AddComponentMenu("HorrorGame/Net/Net Clue Terminal")]
    public sealed class NetClueTerminal : NetworkBehaviour
    {
        private NetPlayer? _player;

        // Host-side read session. Not replicated, and not replicable: the id is the
        // clue this player is standing at, which the host already knows because it
        // spawned the prop there.
        private int _serverClueId = -1;
        private double _serverReadStartTime;

        /// <summary>The last line this client was told, or empty. Client-side display state.</summary>
        public string LastLine { get; private set; } = string.Empty;

        /// <summary>The clue <see cref="LastLine"/> belongs to, or -1.</summary>
        public int LastClueId { get; private set; } = -1;

        /// <summary>
        /// Raised on the reading client with the clue id and the rendered line. An
        /// empty line means the read failed — §03's "낡아서 지워진 부분" — which is
        /// itself worth telling the player, so they know to come back rather than
        /// assume they have it.
        /// </summary>
        public event Action<int, string>? LineReceived;

        /// <inheritdoc />
        public override void OnStartClient()
        {
            _player = GetComponent<NetPlayer>();
        }

        /// <inheritdoc />
        public override void OnStartServer()
        {
            _player = GetComponent<NetPlayer>();
        }

        /// <summary>
        /// Tells the host this player has started reading. Owner only.
        /// </summary>
        public void BeginRead(int clueId)
        {
            if (!isOwned)
            {
                return;
            }

            CmdBeginRead(clueId);
        }

        /// <summary>
        /// Tells the host the read finished, with the conditions it happened under.
        /// Owner only.
        /// </summary>
        /// <param name="worstLightQuality">
        /// Darkest the mark got while being read, 0–1. §03: "어둠 = 목표의
        /// 잠금장치" — this is the lock, expressed as a number.
        /// </param>
        /// <param name="viewAngleDegrees">Angle the mark was read from. 180° is §03's upside-down sign.</param>
        /// <param name="blur">How worn the mark is, 0–1.</param>
        public void CompleteRead(float worstLightQuality, float viewAngleDegrees, float blur)
        {
            if (!isOwned)
            {
                return;
            }

            CmdCompleteRead(worstLightQuality, viewAngleDegrees, blur);
        }

        /// <summary>Abandons a read — the beam moved, the monster arrived. Owner only.</summary>
        public void CancelRead()
        {
            if (!isOwned)
            {
                return;
            }

            CmdCancelRead();
        }

        [Command]
        private void CmdBeginRead(int clueId)
        {
            var authority = HostSecrets.Clues;
            if (authority == null)
            {
                return;
            }

            if (!WithinReach(authority, clueId))
            {
                return;
            }

            _serverClueId = clueId;
            _serverReadStartTime = NetworkTime.localTime;
        }

        [Command]
        private void CmdCancelRead()
        {
            _serverClueId = -1;
        }

        [Command]
        private void CmdCompleteRead(float worstLightQuality, float viewAngleDegrees, float blur)
        {
            var authority = HostSecrets.Clues;
            if (authority == null || _serverClueId < 0)
            {
                return;
            }

            var clueId = _serverClueId;
            _serverClueId = -1;

            // Walking away ends the read. §03 puts the clue in the dangerous place on
            // purpose — "위험 구역에 들어가야 한다 · 안전한 곳에서 해결 불가" — so the
            // range check is not anti-cheat, it is the mechanic.
            if (!WithinReach(authority, clueId))
            {
                TargetLine(connectionToClient, clueId, string.Empty);
                return;
            }

            var secondsHeld = (float)Math.Max(0d, NetworkTime.localTime - _serverReadStartTime);

            var legible = authority.TryRenderRead(
                clueId,
                secondsHeld,
                Mathf.Clamp01(worstLightQuality),
                viewAngleDegrees,
                Mathf.Clamp01(blur),
                out var line);

            TargetLine(connectionToClient, clueId, legible ? line : string.Empty);
        }

        /// <summary>
        /// The reply. One clue, one sentence, one connection.
        /// <para>
        /// The signature is the guarantee. There is no structured payload here for a
        /// client to accumulate and cross-reference — a client that logs every line
        /// it ever receives has a transcript of what its own player read, which is
        /// exactly what that player could have written on a notepad next to the
        /// keyboard.
        /// </para>
        /// </summary>
        [TargetRpc]
        private void TargetLine(NetworkConnection target, int clueId, string line)
        {
            LastClueId = clueId;
            LastLine = line;
            LineReceived?.Invoke(clueId, line);
        }

        /// <summary>
        /// Whether this player is close enough to be reading that clue, measured
        /// against the host's copy of their position.
        /// </summary>
        [Server]
        private bool WithinReach(HostClueAuthority authority, int clueId)
        {
            if (!authority.TryGetMarkerPosition(clueId, out var marker))
            {
                return false;
            }

            var where = _player != null ? _player.NetworkedPosition : transform.position;
            return (where - marker).sqrMagnitude
                   <= GameConstants.ClueReadRange * GameConstants.ClueReadRange;
        }
    }
}
