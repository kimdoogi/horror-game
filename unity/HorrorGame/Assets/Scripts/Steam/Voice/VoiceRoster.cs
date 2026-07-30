#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Math;

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// The positions and playback sinks the voice pipeline needs, as read by the
    /// sender's gate and the host's relay.
    /// <para>
    /// Read-only on purpose: everything that computes an audience consumes this and
    /// nothing else, so there is exactly one place a wrong position can come from.
    /// </para>
    /// </summary>
    public interface IVoiceRoster
    {
        /// <summary>The local player's id, or <see cref="NetUserId.None"/> before spawn.</summary>
        NetUserId LocalId { get; }

        /// <summary>
        /// Copies the current listeners into <paramref name="destination"/>, which is
        /// cleared first. A copy rather than a live view because the audience
        /// computation runs while gameplay may be mutating the roster.
        /// </summary>
        void CopyListenersTo(List<VoiceListener> destination);

        /// <summary>Position of one player. False when unknown — a player who has not spawned yet.</summary>
        bool TryGetPosition(NetUserId id, out Vec3 position);

        /// <summary>The playback sink registered for a speaker, or null when they have no character in the scene.</summary>
        IPositionalVoiceOutput? GetOutput(NetUserId id);
    }

    /// <summary>
    /// The one voice participant table for a session: who is where, who may hear, and
    /// which audio source belongs to whom.
    /// <para>
    /// It is a plain class with a shared instance rather than a
    /// <c>MonoBehaviour</c> singleton, because the host's relay
    /// (<see cref="VoiceRelay"/>) is not a component and the audience rule
    /// (<see cref="VoiceAudience"/>) is a pure function — keeping the table
    /// engine-free is what lets both be exercised without a scene, and it is the same
    /// reason ARCHITECTURE §1 keeps the rules out of <c>UnityEngine</c>.
    /// </para>
    /// <para>
    /// Positions are written by <see cref="VoicePlayerLink"/> on every player
    /// character, host and client alike. On the host they are the authoritative
    /// positions, which is precisely what <see cref="VoiceRelay"/> needs: §13 gives
    /// the host authority, so the host must never take a client's word for where it
    /// is when deciding who may hear it.
    /// </para>
    /// </summary>
    public sealed class VoiceRoster : IVoiceRoster
    {
        private readonly Dictionary<NetUserId, Entry> _entries = new Dictionary<NetUserId, Entry>();
        private readonly List<NetUserId> _order = new List<NetUserId>(GameConstants.PlayersPerMatch);

        /// <summary>
        /// The session's roster. A single shared instance keeps the wiring trivial for
        /// components that are spawned by the network layer and cannot be handed
        /// dependencies through a constructor.
        /// </summary>
        public static VoiceRoster Shared { get; } = new VoiceRoster();

        /// <inheritdoc />
        public NetUserId LocalId { get; private set; }

        /// <summary>Players currently registered.</summary>
        public int Count => _order.Count;

        /// <summary>
        /// Declares which id is the local player. Set by the Net layer when the local
        /// character spawns; the sender's gate needs it to exclude itself from its own
        /// audience.
        /// </summary>
        public void SetLocalId(NetUserId id)
        {
            LocalId = id;
        }

        /// <summary>
        /// Adds or updates a participant. <paramref name="output"/> may be null for a
        /// player with no audible character — a ghost (§09), or a player whose
        /// character has not been spawned locally yet.
        /// </summary>
        public void Bind(NetUserId id, IPositionalVoiceOutput? output)
        {
            if (!id.IsValid)
            {
                return;
            }

            if (_entries.TryGetValue(id, out var existing))
            {
                existing.Output = output;
                _entries[id] = existing;
                return;
            }

            _entries[id] = new Entry { Output = output, Position = Vec3.Zero, CanReceive = true };
            _order.Add(id);
        }

        /// <summary>Updates a participant's position. Called every frame per character.</summary>
        public void SetPosition(NetUserId id, Vec3 position)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                entry.Position = position;
                _entries[id] = entry;
            }
        }

        /// <summary>
        /// Sets whether a participant may receive voice. §09's ghost state is the
        /// intended caller; the rule itself is a gameplay decision, not a voice one.
        /// </summary>
        public void SetCanReceive(NetUserId id, bool canReceive)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                entry.CanReceive = canReceive;
                _entries[id] = entry;
            }
        }

        /// <summary>
        /// Removes a participant and silences their output. Called when a character is
        /// destroyed or a player disconnects.
        /// </summary>
        public void Unbind(NetUserId id)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                entry.Output?.ResetOutput();
                _entries.Remove(id);
                _order.Remove(id);
            }
        }

        /// <summary>Forgets everyone. §13 ends the session on host loss, so a fresh session starts empty.</summary>
        public void Clear()
        {
            foreach (var entry in _entries.Values)
            {
                entry.Output?.ResetOutput();
            }

            _entries.Clear();
            _order.Clear();
            LocalId = NetUserId.None;
        }

        /// <inheritdoc />
        public void CopyListenersTo(List<VoiceListener> destination)
        {
            destination.Clear();
            for (var i = 0; i < _order.Count; i++)
            {
                var id = _order[i];
                if (_entries.TryGetValue(id, out var entry))
                {
                    destination.Add(new VoiceListener(id, entry.Position, entry.CanReceive));
                }
            }
        }

        /// <inheritdoc />
        public bool TryGetPosition(NetUserId id, out Vec3 position)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                position = entry.Position;
                return true;
            }

            position = Vec3.Zero;
            return false;
        }

        /// <inheritdoc />
        public IPositionalVoiceOutput? GetOutput(NetUserId id) =>
            _entries.TryGetValue(id, out var entry) ? entry.Output : null;

        private struct Entry
        {
            public IPositionalVoiceOutput? Output;
            public Vec3 Position;
            public bool CanReceive;
        }
    }
}
