#nullable enable

using System.Collections.Generic;
using HorrorGame.Net;
using Mirror;
using UnityEngine;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// Where everybody is standing, asked separately from how the voice gets there.
    /// <para>
    /// Split out because the two halves are answered by different machines. The host
    /// resolves <see cref="TryGetSpeakerPosition"/> and <see cref="CopyListeners"/> from
    /// its own authoritative copies — the ones <c>NetPlayer.CmdReportView</c> already
    /// speed-clamps — and a listener resolves <see cref="TryGetLocalListenerPosition"/>
    /// from where its own ears are. Neither is a question about voice, which is why it is
    /// an interface: a fixture with a real socket and no character controllers can answer
    /// them without the voice path having a test branch in it.
    /// </para>
    /// </summary>
    public interface IVoicePositions
    {
        /// <summary>Host: where the runner on <paramref name="connectionId"/> is, authoritatively.</summary>
        /// <param name="connectionId">Mirror's id for that connection.</param>
        /// <param name="position">Receives the position.</param>
        bool TryGetSpeakerPosition(int connectionId, out Vector3 position);

        /// <summary>Host: every connection that could be somebody's audience, this tick.</summary>
        /// <param name="into">Cleared and filled with connection ids.</param>
        void CopyListeners(List<int> into);

        /// <summary>
        /// Host: the §11 lobby seat behind a connection, or −1. Only <c>RaceHud</c> reads
        /// it — §02's <c>Racer.Id</c> is a seat index, so this is what joins a voice to a
        /// name on screen.
        /// </summary>
        /// <param name="connectionId">Mirror's id for that connection.</param>
        int SeatIndexOf(int connectionId);

        /// <summary>Listener: where this machine's ears are.</summary>
        /// <param name="position">Receives the position.</param>
        bool TryGetLocalListenerPosition(out Vector3 position);

        /// <summary>
        /// Speaker: whether anybody other than this machine's own runner is inside
        /// <paramref name="radiusMetres"/> of <paramref name="from"/>.
        /// <para>
        /// The speaker's own gate — the one that decides whether the microphone opens at
        /// all. It answers from whatever this machine can see, which on a client is what
        /// <c>HorrorInterestManagement</c> replicated, so it is advisory by construction.
        /// <see cref="VoiceHostRelay"/> asks the same question again with authoritative
        /// positions before a single byte reaches anybody.
        /// </para>
        /// </summary>
        /// <param name="from">Where the speaker is standing.</param>
        /// <param name="radiusMetres">Clear-air range of the effort they are using.</param>
        bool AnyListenerWithin(Vector3 from, float radiusMetres);
    }

    /// <summary>
    /// The shipped answer: Mirror's connections and <c>NetPlayer</c> on the host, the
    /// scene's <c>AudioListener</c> on a listener.
    /// <para>
    /// <b>The listener is the <c>AudioListener</c> and not the local <c>NetPlayer</c>,</b>
    /// and the difference is not pedantic. <c>NetLocalRunner</c> switches off the body on
    /// the copy this machine owns, and <c>NetPlayer</c> only writes its transform on the
    /// server — so on a client the owned proxy's transform is whatever it was spawned at,
    /// usually the origin. Using it would compute every distance from the middle of B1.
    /// <c>GameAudio</c> resolves the ear the same way, by
    /// <c>FindFirstObjectByType&lt;AudioListener&gt;</c>, and voice agreeing with the rest
    /// of the mix about where the player is standing is worth more than a faster lookup.
    /// </para>
    /// </summary>
    public sealed class NetVoicePositions : IVoicePositions
    {
        /// <summary>
        /// Seconds between searches for the scene's <c>AudioListener</c> while there is not
        /// one.
        /// <para>
        /// <c>FindFirstObjectByType</c> walks every object in every loaded scene, and this
        /// is asked once a frame by <c>VoiceRuntime</c>. In a lobby, in a menu, and in every
        /// PlayMode fixture that brings a server up there is no listener to find, so an
        /// unthrottled search would be a full scene walk per frame for the whole of it —
        /// paid by everybody, to answer a question whose answer is "no". Half a second is
        /// the same shape of interval <c>NetLocalRunner</c> uses to poll for a rig, and a
        /// listener that has just arrived with a scene is not needed inside one frame.
        /// </para>
        /// </summary>
        private const float EarSearchIntervalSeconds = 0.5f;

        private Transform? _ear;
        private float _nextEarSearch;

        /// <inheritdoc />
        public bool TryGetSpeakerPosition(int connectionId, out Vector3 position)
        {
            position = default;

            if (!NetworkServer.connections.TryGetValue(connectionId, out var conn) || conn == null)
            {
                return false;
            }

            var identity = conn.identity;
            if (identity == null)
            {
                // A connection that is ready but has no body yet. Normal in §11's lobby,
                // and the honest answer is "nowhere" rather than the origin — a runner
                // placed at (0,0,0) would be audible to whoever is standing in the middle
                // of B1.
                return false;
            }

            if (!identity.TryGetComponent(out NetPlayer player))
            {
                return false;
            }

            position = player.NetworkedPosition;
            return true;
        }

        /// <inheritdoc />
        public void CopyListeners(List<int> into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();

            // Includes the host's own local connection (id 0) when the host is playing,
            // which §13 says it always is — there is no dedicated server.
            foreach (var id in NetworkServer.connections.Keys)
            {
                into.Add(id);
            }
        }

        /// <inheritdoc />
        public int SeatIndexOf(int connectionId)
        {
            if (!NetworkServer.connections.TryGetValue(connectionId, out var conn) || conn == null)
            {
                return -1;
            }

            var identity = conn.identity;
            if (identity == null || !identity.TryGetComponent(out NetPlayer player))
            {
                return -1;
            }

            return player.SeatIndex;
        }

        /// <inheritdoc />
        public bool TryGetLocalListenerPosition(out Vector3 position)
        {
            var ear = _ear;
            if (ear == null)
            {
                if (Time.unscaledTime < _nextEarSearch)
                {
                    position = default;
                    return false;
                }

                _nextEarSearch = Time.unscaledTime + EarSearchIntervalSeconds;

                var listener = Object.FindFirstObjectByType<AudioListener>();
                ear = listener != null ? listener.transform : null;
                _ear = ear;
            }

            if (ear == null)
            {
                position = default;
                return false;
            }

            position = ear.position;
            return true;
        }

        /// <inheritdoc />
        public bool AnyListenerWithin(Vector3 from, float radiusMetres)
        {
            if (radiusMetres <= 0f)
            {
                return false;
            }

            var radiusSquared = radiusMetres * radiusMetres;

            if (NetworkServer.active)
            {
                // The host can answer exactly, from the same positions the relay will use.
                foreach (var pair in NetworkServer.connections)
                {
                    if (pair.Key == VoiceCapture.HostSpeakerConnectionId)
                    {
                        continue;
                    }

                    if (TryGetSpeakerPosition(pair.Key, out var at)
                        && (at - from).sqrMagnitude < radiusSquared)
                    {
                        return true;
                    }
                }

                return false;
            }

            // A client answers from what it has been sent. NetInterestScope.PerceptionRange
            // is at least GameConstants.VoiceCutoffDistance, so a runner inside shouting
            // range has been replicated — but a runner who has NOT been replicated is
            // simply absent here rather than assumed near, which fails toward a closed
            // microphone. That is the right direction to fail in for a cutoff whose purpose
            // is 도청 방지.
            foreach (var identity in NetworkClient.spawned.Values)
            {
                if (identity == null || identity.isOwned)
                {
                    continue;
                }

                if (!identity.TryGetComponent(out NetPlayer player))
                {
                    continue;
                }

                if ((player.NetworkedPosition - from).sqrMagnitude < radiusSquared)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Forgets the cached ear. Called when a scene loads and the old one is gone.</summary>
        public void Forget()
        {
            _ear = null;

            // The new scene's listener is worth looking for immediately rather than up to
            // EarSearchIntervalSeconds later: a descent that began with voice muted for
            // half a second is the kind of thing players report as "it did not work".
            _nextEarSearch = 0f;
        }
    }
}
