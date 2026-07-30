#nullable enable

using HorrorGame.Core.Math;
using UnityEngine;

namespace HorrorGame.Steam.Voice
{
    /// <summary>
    /// Connects one player character to the voice pipeline: publishes its position to
    /// the roster and registers its audio output as that player's voice.
    /// <para>
    /// Every character carries one, on every machine. That is what makes the host's
    /// copy of the roster authoritative — the host runs the same component for all four
    /// players and therefore has its own positions for all of them, which is exactly
    /// what <see cref="VoiceRelay"/> needs in order not to have to believe a client
    /// about where it is standing.
    /// </para>
    /// <para>
    /// The local player registers an output too, even though nobody routes voice to
    /// their own ears: <see cref="VoiceAudience.Select"/> excludes the speaker from
    /// their own audience, so the only thing that ever plays through it is
    /// <see cref="LoopbackVoiceTransport"/>'s deliberate echo during a microphone check.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoicePlayerLink : MonoBehaviour
    {
        private AudioSourceVoiceOutput? _output;
        private NetUserId _id;
        private bool _bound;

        /// <summary>Which player this character belongs to.</summary>
        public NetUserId PlayerId => _id;

        /// <summary>Whether this is the local player's character.</summary>
        public bool IsLocalPlayer { get; private set; }

        /// <summary>
        /// Binds the character to a player id. Called by the Net layer on spawn, since
        /// the id comes from the connection and not from anything in the scene.
        /// </summary>
        public void Bind(NetUserId id, bool isLocalPlayer)
        {
            Unbind();

            _id = id;
            IsLocalPlayer = isLocalPlayer;

            if (!_id.IsValid)
            {
                return;
            }

            _output = GetComponent<AudioSourceVoiceOutput>();
            if (_output == null)
            {
                _output = gameObject.AddComponent<AudioSourceVoiceOutput>();
            }

            VoiceRoster.Shared.Bind(_id, _output);
            VoiceRoster.Shared.SetPosition(_id, ToVec3(transform.position));

            if (isLocalPlayer)
            {
                VoiceRoster.Shared.SetLocalId(_id);
            }

            _bound = true;
        }

        /// <summary>
        /// Sets whether this player may receive voice. §09's ghost state decides it; the
        /// voice layer only carries the flag.
        /// </summary>
        public void SetCanReceive(bool canReceive)
        {
            if (_bound)
            {
                VoiceRoster.Shared.SetCanReceive(_id, canReceive);
            }
        }

        /// <summary>Removes the character from the roster and silences it.</summary>
        public void Unbind()
        {
            if (!_bound)
            {
                return;
            }

            VoiceRoster.Shared.Unbind(_id);
            _bound = false;
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void Update()
        {
            if (_bound)
            {
                // Every frame rather than on a timer: four transforms is nothing, and a
                // stale position on the host is a wrong answer to "who can hear this",
                // which is the one question §13 does not allow to be approximate.
                VoiceRoster.Shared.SetPosition(_id, ToVec3(transform.position));
            }
        }

        private static Vec3 ToVec3(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
