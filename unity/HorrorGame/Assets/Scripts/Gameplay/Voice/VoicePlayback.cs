#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Audio;
using HorrorGame.Core.Voice;
using UnityEngine;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// The listener's half: §13's steps ③ 압축해제 and ④ 3D 오디오 소스, with
    /// <see cref="VoiceRules"/> applied here and only here.
    /// <para>
    /// <b>The receiver owns the rule.</b> The host gated on clear-air range from
    /// authoritative positions, which is a superset; this machine knows two things the
    /// host does not cheaply know — exactly where its own ears are, and whether there is a
    /// wall in the way — so it is where distance, occlusion and the three efforts turn
    /// into a number. Nothing is re-derived: <see cref="VoiceRules.Gain"/> is asked, and
    /// what it returns is the gain.
    /// </para>
    /// <para>
    /// <b>Unity is told to contribute no roll-off of its own.</b> §01's rule is a LINEAR
    /// fall to zero at the effort's range, and it says at length why inverse-square — what
    /// <c>AudioRolloffMode.Logarithmic</c> gives — would destroy the band this game is
    /// made of, the one where you can hear that somebody is near without knowing where. So
    /// the gain is multiplied into the samples in <see cref="VoiceCodec.Decode"/> and the
    /// <c>AudioSource</c> gets a flat custom curve. It is still spatialised, because
    /// direction is information and this system exists so that two runners at a gate can
    /// tell which side of it the other one is on; it is only the <em>level</em> the engine
    /// is not allowed to have an opinion about.
    /// </para>
    /// <para>
    /// <b>Occlusion is one line cast, not five.</b> <c>SoundOccluder</c> spreads several
    /// rays because it needs a continuous 0~1 to drive a low-pass. <see cref="VoiceRules"/>
    /// asks a yes/no question — <c>occluded</c> — so a single cast against
    /// <c>GameAudio.OccluderMask</c> answers it exactly, at
    /// <see cref="OcclusionProbeSeconds"/> per speaker rather than per frame.
    /// </para>
    /// </summary>
    public sealed class VoicePlayback
    {
        /// <summary>
        /// Seconds between occlusion probes for one speaker.
        /// <para>
        /// A wall does not appear between two people in less time than it takes to walk
        /// round a corner. At §05's sprint the pair can close 1.1 m in this interval, which
        /// against <see cref="VoiceRules.WhisperRange"/> — the shortest range in the game —
        /// is a quarter of the distance, so the answer cannot be a whole range stale. Ten
        /// probes a second against fifty frames a second is a fifth of the physics queries
        /// a per-frame test would cost.
        /// </para>
        /// </summary>
        public const float OcclusionProbeSeconds = 0.1f;

        /// <summary>
        /// Metres of slack at each end of the occlusion line.
        /// <para>
        /// The line is cast between two people's positions, and a runner's own collider is
        /// sitting on one of them. Without the inset every probe would hit the speaker or
        /// the listener and report that everybody is permanently behind a wall.
        /// </para>
        /// </summary>
        public const float OcclusionInsetMetres = 0.35f;

        private readonly IVoicePositions _positions;
        private readonly IVoiceLine _line;
        private readonly Dictionary<int, Speaker> _speakers = new Dictionary<int, Speaker>();
        private readonly List<int> _expired = new List<int>();

        private float[] _pcm = new float[VoiceCodec.FrameSamples * 4];

        /// <summary>
        /// The arriving frame, copied out of Mirror's receive buffer to index 0. See the
        /// remarks at the copy in <see cref="Accept"/> for why this exists.
        /// </summary>
        private byte[] _frame = new byte[VoiceCodec.FrameBytes];
        private Transform? _root;
        private bool _reportedForeignCodec;

        /// <summary>Builds playback over a position source and this machine's codec.</summary>
        /// <param name="positions">Where this machine's ears are.</param>
        /// <param name="line">The codec that decodes arriving frames.</param>
        public VoicePlayback(IVoicePositions positions, IVoiceLine line)
        {
            _positions = positions ?? throw new ArgumentNullException(nameof(positions));
            _line = line ?? throw new ArgumentNullException(nameof(line));
        }

        /// <summary>Frames this machine was sent.</summary>
        public int FramesReceived { get; private set; }

        /// <summary>Frames that decoded and went into a buffer.</summary>
        public int FramesPlayed { get; private set; }

        /// <summary>
        /// Frames dropped because the rule made them inaudible after all — the listener's
        /// wall, which the host could not see. Not an error; it is <c>OccludedFraction</c>
        /// doing its job.
        /// </summary>
        public int DroppedInaudible { get; private set; }

        /// <summary>Frames dropped because this machine has no codec for them.</summary>
        public int DroppedForeignCodec { get; private set; }

        /// <summary>
        /// Frames dropped because this machine could not say where its own ears are, so
        /// there was no distance to apply the rule at.
        /// <para>
        /// A headless host, or a frame that landed mid scene-load — both legitimate. It is
        /// counted rather than ignored because "received &gt; 0, played 0, both other
        /// counters 0" is an unreadable state, and the first time it happened the obvious
        /// suspect was the decoder.
        /// </para>
        /// </summary>
        public int DroppedNoListener { get; private set; }

        /// <summary>Frames whose effort byte decoded to <c>Silent</c> — a wire mismatch, not a quiet speaker.</summary>
        public int DroppedSilentEffort { get; private set; }

        /// <summary>Frames with an empty or oversized payload.</summary>
        public int DroppedMalformed { get; private set; }

        /// <summary>Frames the codec accepted and produced no samples from.</summary>
        public int DroppedDecodedEmpty { get; private set; }

        /// <summary>How many people this machine can hear right now.</summary>
        public int AudibleCount => _speakers.Count;

        /// <summary>
        /// Takes one downstream frame. Called from Mirror's handler on the main thread.
        /// </summary>
        /// <param name="message">The frame. Its payload is borrowed and is not kept.</param>
        /// <param name="now">Seconds on a monotonic clock.</param>
        /// <returns>True when it became audible.</returns>
        public bool Accept(in VoiceDownstreamMessage message, double now)
        {
            FramesReceived++;

            var codec = VoiceWire.ToCodec(message.Codec);
            if (codec == VoiceCodecId.None || codec != _line.Codec)
            {
                DroppedForeignCodec++;

                if (!_reportedForeignCodec)
                {
                    _reportedForeignCodec = true;

                    // Once. Both ends of a session normally agree, because the transport
                    // and the codec are chosen by the same test — see VoiceLines. If this
                    // fires, they did not, and every voice in the match is silent rather
                    // than distorted, which is the failure worth naming out loud.
                    Debug.LogWarning("[Voice] A frame arrived in codec " + codec + " and this machine decodes "
                                     + _line.Codec + ". Nobody will be audible until both ends agree — the "
                                     + "transport and the codec are supposed to be chosen by the same test "
                                     + "(VoiceLines.Choose / HorrorGameNetworkManager.AttachTransport).");
                }

                return false;
            }

            var effort = VoiceWire.ToEffort(message.Effort);
            if (effort == VoiceEffort.Silent)
            {
                // Counted: nobody transmits Silent, so this is the effort byte failing to
                // round-trip, and untracked it looks exactly like a decoder that ate the
                // frame.
                DroppedSilentEffort++;
                return false;
            }

            // Before Resolve, so a malformed or empty packet cannot mint an AudioSource and
            // a GameObject for a speaker who never said anything.
            if (message.Payload.Array == null
                || message.Payload.Count <= 0
                || message.Payload.Count > VoiceHostRelay.MaxAcceptedFrameBytes)
            {
                DroppedMalformed++;
                return false;
            }

            if (!_positions.TryGetLocalListenerPosition(out var ear))
            {
                // No AudioListener in the scene — a headless host, or a frame that arrived
                // during a scene load. There is nothing to hear it with.
                //
                // COUNTED, because it used to be the one exit between FramesReceived and
                // FramesPlayed that left no trace: a session in this state reports frames
                // arriving, nothing playing, and zero on both drop counters, which reads
                // as "the decoder ate them". It cost an afternoon once.
                DroppedNoListener++;
                return false;
            }

            var speaker = Resolve(message.SpeakerConnectionId, message.SpeakerSeatIndex);
            speaker.Seat = message.SpeakerSeatIndex;
            speaker.Position = message.SpeakerPosition;
            speaker.Effort = effort;
            speaker.LastFrameTime = now;

            var distance = Vector3.Distance(ear, message.SpeakerPosition);

            if (now - speaker.LastProbeTime >= OcclusionProbeSeconds)
            {
                speaker.LastProbeTime = now;
                speaker.Occluded = IsOccluded(ear, message.SpeakerPosition, distance);
            }

            var gain = VoiceRules.Gain(effort, distance, speaker.Occluded);
            speaker.Gain = gain;
            speaker.DistanceMetres = distance;

            if (gain <= 0f)
            {
                DroppedInaudible++;
                return false;
            }

            EnsurePcmCapacity(message.Payload.Count);

            // ----------------------------------------------------------------
            // Copied to index 0 before decoding, and this line is the difference between
            // a working voice channel and a silent one.
            //
            // IVoiceLine.Decode takes (byte[] frame, int frameBytes) — a frame that STARTS
            // AT INDEX 0. What arrives here is an ArraySegment over Mirror's own receive
            // buffer, and Mirror's ReadArraySegmentAndSize hands back a segment with a
            // NON-ZERO Offset. Passing .Array straight in therefore read the datagram's
            // header as the voice frame: VoiceCodec.Decode checks frame[3] against
            // FrameVersion, that byte belonged to Mirror, the check failed, and Decode
            // returned 0 for every frame ever sent. Measured: 8 frames received, 8
            // decoded to zero samples, nothing audible, and no exception anywhere.
            //
            // The copy is also what makes the "borrowed and not kept" contract on this
            // method true: Mirror reuses that buffer the moment the handler returns.
            EnsureFrameCapacity(message.Payload.Count);
            Array.Copy(
                message.Payload.Array!,
                message.Payload.Offset,
                _frame,
                0,
                message.Payload.Count);

            var written = _line.Decode(
                _frame,
                message.Payload.Count,
                _pcm,
                0,
                gain);

            if (written <= 0)
            {
                DroppedDecodedEmpty++;
                return false;
            }

            speaker.Stream.Write(_pcm, 0, written);
            speaker.Place();

            FramesPlayed++;
            return true;
        }

        /// <summary>
        /// Tears down speakers who have stopped, and keeps the live ones' sources on top
        /// of them. Called once a frame.
        /// </summary>
        /// <param name="now">Seconds on a monotonic clock.</param>
        public void Step(double now)
        {
            if (_speakers.Count == 0)
            {
                return;
            }

            _expired.Clear();

            foreach (var pair in _speakers)
            {
                if (now - pair.Value.LastFrameTime > VoiceSpeakerStream.IdleTimeoutSeconds)
                {
                    _expired.Add(pair.Key);
                    continue;
                }

                pair.Value.Place();
            }

            for (var i = 0; i < _expired.Count; i++)
            {
                if (_speakers.TryGetValue(_expired[i], out var speaker))
                {
                    speaker.Destroy();
                    _speakers.Remove(_expired[i]);
                }
            }
        }

        /// <summary>
        /// Whether the runner in §11 seat <paramref name="seatIndex"/> is audible right
        /// now. What <c>RaceHud</c> asks so it can mark a standings row.
        /// </summary>
        /// <param name="seatIndex">§02's <c>Racer.Id</c>.</param>
        public bool IsSeatSpeaking(int seatIndex)
        {
            if (seatIndex < 0)
            {
                return false;
            }

            foreach (var pair in _speakers)
            {
                if (pair.Value.Seat == seatIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The most recent samples this machine decoded for one speaker, with the rule's
        /// gain already in them. Diagnostic, and the measurement a test makes.
        /// </summary>
        /// <param name="speakerConnectionId">The speaker's connection id.</param>
        /// <param name="destination">Receives the samples, newest last.</param>
        /// <param name="count">How many to take.</param>
        /// <returns>How many were copied, or 0 when that speaker is not audible.</returns>
        public int PeekSamples(int speakerConnectionId, float[] destination, int count) =>
            _speakers.TryGetValue(speakerConnectionId, out var speaker)
                ? speaker.Stream.PeekLatest(destination, count)
                : 0;

        /// <summary>The gain the rule last returned for one speaker, or 0.</summary>
        /// <param name="speakerConnectionId">The speaker's connection id.</param>
        public float GainOf(int speakerConnectionId) =>
            _speakers.TryGetValue(speakerConnectionId, out var speaker) ? speaker.Gain : 0f;

        /// <summary>The distance the gain was computed from, or −1.</summary>
        /// <param name="speakerConnectionId">The speaker's connection id.</param>
        public float DistanceOf(int speakerConnectionId) =>
            _speakers.TryGetValue(speakerConnectionId, out var speaker) ? speaker.DistanceMetres : -1f;

        /// <summary>Whether the rule last found a wall between this machine and one speaker.</summary>
        /// <param name="speakerConnectionId">The speaker's connection id.</param>
        public bool OccludedFrom(int speakerConnectionId) =>
            _speakers.TryGetValue(speakerConnectionId, out var speaker) && speaker.Occluded;

        /// <summary>Silences everybody and destroys the sources. Called when the client stops.</summary>
        public void Clear()
        {
            foreach (var pair in _speakers)
            {
                pair.Value.Destroy();
            }

            _speakers.Clear();

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root.gameObject);
                _root = null;
            }

            FramesReceived = 0;
            FramesPlayed = 0;
            DroppedInaudible = 0;
            DroppedForeignCodec = 0;
            DroppedNoListener = 0;
            DroppedSilentEffort = 0;
            DroppedMalformed = 0;
            DroppedDecodedEmpty = 0;
        }

        private static bool IsOccluded(Vector3 ear, Vector3 mouth, float distance)
        {
            if (distance <= OcclusionInsetMetres * 2f)
            {
                // Close enough that the inset would invert the line. Two people standing on
                // each other are not behind a wall.
                return false;
            }

            var direction = (mouth - ear) / distance;
            var from = ear + (direction * OcclusionInsetMetres);
            var length = distance - (OcclusionInsetMetres * 2f);

            return Physics.Raycast(
                from,
                direction,
                length,
                GameAudio.OccluderMask,
                QueryTriggerInteraction.Ignore);
        }

        private Speaker Resolve(int connectionId, int seatIndex)
        {
            if (_speakers.TryGetValue(connectionId, out var existing))
            {
                return existing;
            }

            if (_root == null)
            {
                var holder = new GameObject("Voice");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                _root = holder.transform;
            }

            var speaker = new Speaker(connectionId, seatIndex, _line.SampleRate, _root);
            _speakers[connectionId] = speaker;
            return speaker;
        }

        /// <summary>
        /// Grows the scratch the arriving frame is copied into. Bounded by
        /// <see cref="VoiceHostRelay.MaxAcceptedFrameBytes"/>, which <see cref="Accept"/>
        /// has already refused anything larger than.
        /// </summary>
        /// <param name="frameBytes">Bytes in the frame.</param>
        private void EnsureFrameCapacity(int frameBytes)
        {
            if (_frame.Length >= frameBytes)
            {
                return;
            }

            _frame = new byte[Mathf.Min(frameBytes, VoiceHostRelay.MaxAcceptedFrameBytes)];
        }

        private void EnsurePcmCapacity(int compressedBytes)
        {
            // ADPCM decodes to exactly two samples per byte and Steam's codec to rather more;
            // ×16 covers both with room. The ceiling is 65536 samples — four seconds at
            // VoiceCodec.SampleRate, against a frame that is supposed to be twenty
            // milliseconds — because growth exists to absorb a backend whose frame size we
            // sized wrong, not to let a malformed packet talk this into an unbounded
            // allocation. VoiceHostRelay.MaxAcceptedFrameBytes caps the input as well.
            var wanted = Mathf.Min(compressedBytes * 16, 1 << 16);
            if (_pcm.Length >= wanted)
            {
                return;
            }

            _pcm = new float[wanted];
        }

        /// <summary>One person this machine can currently hear.</summary>
        private sealed class Speaker
        {
            private readonly GameObject _object;
            private readonly AudioSource _source;

            internal Speaker(int connectionId, int seatIndex, int sampleRate, Transform root)
            {
                Seat = seatIndex;
                Stream = new VoiceSpeakerStream(sampleRate);

                _object = new GameObject("Voice [conn=" + connectionId + "]");
                _object.transform.SetParent(root, false);

                var rate = sampleRate > 0 ? sampleRate : VoiceCodec.SampleRate;

                // A streaming clip rather than a queue of one-shots: the callback runs on
                // the audio thread and pulls exactly what the mixer needs, so a network
                // hiccup shortens the buffer instead of adding a gap between two clips.
                var clip = AudioClip.Create(
                    "Voice",
                    rate,
                    1,
                    rate,
                    true,
                    Stream.Read);

                _source = _object.AddComponent<AudioSource>();
                _source.clip = clip;
                _source.loop = true;
                _source.playOnAwake = false;

                // Direction yes, level no. See the class remarks.
                _source.spatialBlend = 1f;
                _source.rolloffMode = AudioRolloffMode.Custom;
                _source.SetCustomCurve(
                    AudioSourceCurveType.CustomRolloff,
                    AnimationCurve.Constant(0f, 1f, 1f));
                _source.minDistance = 1f;
                _source.maxDistance = VoiceRules.ShoutRange;

                // A runner tops out at §05's sprint. Doppler at that speed is inaudible as
                // pitch and audible as wobble, which makes a voice harder to understand for
                // no information gained.
                _source.dopplerLevel = 0f;

                GameAudio.ApplyVolume(_source, AudioBus.Voice, 1f);
                _source.Play();
            }

            /// <summary>The decoded ring this speaker's clip pulls from.</summary>
            internal VoiceSpeakerStream Stream { get; }

            /// <summary>§11 seat, or −1. What <c>RaceHud</c> matches against.</summary>
            internal int Seat { get; set; }

            /// <summary>Where the host last said they were.</summary>
            internal Vector3 Position { get; set; }

            /// <summary>How hard they were last speaking.</summary>
            internal VoiceEffort Effort { get; set; }

            /// <summary>What the rule last returned.</summary>
            internal float Gain { get; set; }

            /// <summary>What that gain was computed from.</summary>
            internal float DistanceMetres { get; set; }

            /// <summary>Whether a wall stood between them at the last probe.</summary>
            internal bool Occluded { get; set; }

            /// <summary>Monotonic seconds at the last frame.</summary>
            internal double LastFrameTime { get; set; }

            /// <summary>Monotonic seconds at the last occlusion cast.</summary>
            internal double LastProbeTime { get; set; }

            /// <summary>Puts the source where the host says the mouth is.</summary>
            internal void Place()
            {
                if (_object != null)
                {
                    _object.transform.position = Position;
                }
            }

            /// <summary>Stops and destroys the source.</summary>
            internal void Destroy()
            {
                if (_source != null)
                {
                    _source.Stop();
                }

                if (_object != null)
                {
                    UnityEngine.Object.Destroy(_object);
                }
            }
        }
    }
}
