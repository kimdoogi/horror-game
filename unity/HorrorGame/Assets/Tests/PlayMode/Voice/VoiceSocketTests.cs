#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HorrorGame.Core;
using HorrorGame.Core.Voice;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Voice;
using HorrorGame.Net;
using kcp2k;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Voice
{
    /// <summary>
    /// Somebody speaks, and somebody else on the far side of a UDP socket hears them at
    /// the loudness <see cref="VoiceRules"/> says.
    /// <para>
    /// <b>Why this file is shaped like <c>NetSocketTests</c>.</b> That fixture is the
    /// standard this project set for a networked claim: it opens a real socket, counts
    /// bytes at <c>KcpTransport</c>'s own send hooks, and asserts
    /// <c>NetworkClient.activeHost</c> is false so that no assertion in it can be satisfied
    /// by Mirror's in-process host wire. Voice is exactly the kind of feature that gets
    /// "proved" with two objects in one heap and ships mute, so it is held to the same
    /// bar — the peers here are a host and a genuinely remote client, and the audio is
    /// measured after it has been through the codec, the audience rule, a datagram and the
    /// listener's own attenuation.
    /// </para>
    /// <para>
    /// <b>What is faked, exactly.</b> Two things, both stated rather than hidden. The
    /// microphone: a headless Unity has no capture device, so
    /// <see cref="InjectedVoiceLine"/> — shipped code, not a fixture class — puts a known
    /// waveform where the driver's samples would land, and everything downstream of that
    /// point is the real path. And the positions: this fixture has real connections and no
    /// character controllers, so <see cref="ScriptedPositions"/> answers where the two of
    /// them are standing. <see cref="TheShippedPositionSourceReadsNetPlayer"/> covers the
    /// production answer separately, so neither half is left unmeasured.
    /// </para>
    /// <para>
    /// <b>Why the host is the speaker and the client the listener.</b> One process holds
    /// one <c>NetworkClient</c>, so the only arrangement in which audio crosses a socket
    /// <em>into</em> something that then applies the rule is host → client. The host's own
    /// frames skip the upstream hop by design (§13 has no dedicated server; the host is one
    /// of the runners), so what this measures is
    /// <see cref="VoiceHostRelay"/> → <c>conn.Send</c> → transport → <c>NetworkClient</c>
    /// → <see cref="VoicePlayback"/>, which is the second and harder half of the pipeline.
    /// <see cref="AClientsFrameReachesTheHostsRelayOverTheSocket"/> covers the first half in
    /// the other direction.
    /// </para>
    /// </summary>
    public sealed class VoiceSocketTests
    {
        /// <summary>Loopback as an IPv4 literal, for the reason <c>NetSocketTests</c> gives.</summary>
        private const string Loopback = "127.0.0.1";

        /// <summary>IPv4 only on both sockets. <c>NetSocketTests</c> explains why DualMode is a variable worth removing.</summary>
        private const bool DualMode = false;

        /// <summary>
        /// Port for this fixture. Distinct from <c>NetSocketTests</c>' 47701/47702 so the
        /// two can never race for a bind, and far from kcp2k's default 7777 so a developer
        /// with the game running does not turn this red.
        /// </summary>
        private const ushort VoicePort = 47711;

        /// <summary>Port for the upstream test, so the two tests in this file never collide either.</summary>
        private const ushort UpstreamPort = 47712;

        /// <summary>Wall-clock seconds for a loopback handshake. Two orders of magnitude of headroom.</summary>
        private const float ConnectSecondsBudget = 10f;

        /// <summary>Wall-clock seconds allowed for audio to cross once the connection is up.</summary>
        private const float AudioSecondsBudget = 5f;

        /// <summary>
        /// How many 20 ms frames to push. Eight is 160 ms — long enough that the stream
        /// primes (<see cref="VoiceSpeakerStream.PrimeSeconds"/> is 80 ms) and short enough
        /// that it cannot overflow <see cref="InjectedVoiceLine.QueueCapacityFrames"/>
        /// between two drains.
        /// </summary>
        private const int FramesToPush = 8;

        /// <summary>
        /// Distance between the two runners for the audible test, metres.
        /// <para>
        /// Chosen so the expected gain is a number a human can check by hand rather than a
        /// number the test happens to agree with itself about: <c>VoiceRules</c> is linear,
        /// <c>TalkRange</c> is 12 m, so 4 m clear-air is exactly 1 − 4/12 = <b>2/3</b>. It is
        /// also inside <c>WhisperRange</c>'s 4 m by construction, which is what makes
        /// <see cref="AWhisperDoesNotCarryAndTheMicrophoneCloses"/>'s 20 m the same shape of
        /// number in the other direction.
        /// </para>
        /// </summary>
        private const float AudibleDistanceMetres = 4f;

        /// <summary>
        /// Distance for the out-of-range test, metres. Beyond <c>WhisperRange</c> (4 m) and
        /// beyond <c>TalkRange</c> (12 m), so no effort short of a shout reaches it.
        /// </summary>
        private const float OutOfRangeMetres = 20f;

        /// <summary>
        /// Floor the received waveform's correlation with the injected one must clear.
        /// <para>
        /// Not a guess. The codec's round trip was measured on this exact waveform before
        /// the test was written: normalised cross-correlation 0.9999 and RMS within 0.1%,
        /// at 39.0 dB SNR (see <c>VoiceCodec.SeedIndex</c>, which is where those numbers
        /// come from). 0.98 sits far below the measurement and far above anything silence,
        /// noise or a different signal could reach — an unrelated waveform correlates near
        /// zero, so this assertion cannot be satisfied by "something arrived".
        /// </para>
        /// </summary>
        private const float CorrelationFloor = 0.98f;

        /// <summary>
        /// Tolerance on the received amplitude as a fraction of the expected one. The codec
        /// was measured to preserve RMS to within 0.1%; 3% leaves room for a frame boundary
        /// landing inside the window without letting a wrong gain through — the gains this
        /// test distinguishes differ by tens of percent.
        /// </summary>
        private const float AmplitudeTolerance = 0.03f;

        /// <summary>
        /// The waveform pushed into the microphone's place: a 200 Hz fundamental with two
        /// harmonics, at a peak of 0.32.
        /// <para>
        /// <b>Every one of those numbers is doing work.</b> 200 Hz is a human male
        /// fundamental and the harmonics put energy where speech has it, so the codec is
        /// being asked the question it exists for rather than an easy one. And 200, 400 and
        /// 800 Hz all divide <see cref="VoiceCodec.FrameSamples"/> exactly at 16 kHz —
        /// periods of 80, 40 and 20 samples into a 320-sample frame — which makes every
        /// captured frame byte-for-byte identical to every other. That is what lets the far
        /// side's buffer be compared against one reference frame at zero lag: a frame lost
        /// on the unreliable channel cannot misalign the comparison, so the test measures
        /// fidelity and never accidentally measures packet loss.
        /// </para>
        /// </summary>
        private static readonly float[] Reference = BuildReference();

        private GameObject? _rig;
        private KcpTransport? _transport;
        private HorrorGameNetworkManager? _manager;
        private GameObject? _ear;
        private GameObject? _wall;
        private GameObject? _directorHost;
        private InjectedVoiceLine? _line;
        private ScriptedPositions? _positions;
        private int _restoreTargetFrameRate;

        private long _serverUnreliableBytes;
        private long _clientUnreliableBytes;

        [SetUp]
        public void SetUp()
        {
            NetSession.ResetForTests();

            _restoreTargetFrameRate = Application.targetFrameRate;

            NetworkServer.aoi = null;
            NetworkClient.aoi = null;

            _serverUnreliableBytes = 0;
            _clientUnreliableBytes = 0;

            _line = new InjectedVoiceLine();
            _positions = new ScriptedPositions();

            // The two seams, set before anything builds a session. VoiceRuntime reads them
            // in EnsureSession, which runs on the first frame a server or client is up.
            VoiceLines.Override = _line;
            VoiceRuntime.PositionsOverride = _positions;

            // A listener has to exist for a listener's position to mean anything on the
            // shipped path; ScriptedPositions answers instead, but the runtime also moves
            // itself onto the ear each frame and an object to look at keeps that honest.
            _ear = new GameObject("VoiceTests_Ear");
        }

        [TearDown]
        public void TearDown()
        {
            // Order matters for the same reason NetSocketTests states: NetworkServer.Shutdown
            // calls Transport.active.ServerStop(), so the transport must still be alive and
            // still be Transport.active when it runs, or the UDP socket stays bound for the
            // rest of the editor session and every later test fails to bind.
            if (NetworkServer.active)
            {
                NetworkServer.Shutdown();
            }

            NetworkClient.Shutdown();

            // After the network comes down, so the runtime tears its own handlers, relay and
            // AudioSources down through the shipped path — ResetForTests calls the same
            // StopServerSide/StopClientSide the transitions would — and the next fixture
            // starts from an unbound runtime rather than one holding this one's seams.
            VoiceRuntime.ResetForTests();

            NetworkServer.aoi = null;
            NetworkClient.aoi = null;
            NetworkManager.ResetStatics();

            DestroyIfAny(ref _rig);
            DestroyIfAny(ref _ear);
            DestroyIfAny(ref _wall);
            DestroyIfAny(ref _directorHost);

            _transport = null;
            _manager = null;
            _line = null;
            _positions = null;

            Transport.active = null;
            Application.targetFrameRate = _restoreTargetFrameRate;
            NetSession.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // The deliverable: audio from one peer to another, attenuated by the rule
        // ------------------------------------------------------------------

        /// <summary>
        /// A host speaks, the bytes leave through a real UDP socket, and a remote client
        /// decodes the same waveform at exactly <c>VoiceRules.Gain</c> for the distance.
        /// <para>
        /// This is the assertion the project did not have. It fails if the codec is broken,
        /// if the audience rule refuses somebody it should admit, if the frame never
        /// reaches <c>Transport.ServerSend</c>, if the message id is not registered on the
        /// far side, if the listener applies the wrong range for the effort, or if it
        /// applies Unity's roll-off instead of §01's — and not one of those could be caught
        /// by a fixture that handed a buffer to an object in the same heap.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AVoiceCrossesARealSocketAndArrivesAttenuatedByTheRule()
        {
            const VoiceEffort effort = VoiceEffort.Talk;

            _positions!.Place(VoiceCapture.HostSpeakerConnectionId, Vector3.zero);
            _positions.PlaceEar(new Vector3(AudibleDistanceMetres, 0f, 0f));

            yield return BringUpTwoPeers(VoicePort);

            AssertTheWireIsReal();

            var runtime = VoiceRuntime.Instance!;
            var relay = runtime.Relay;
            var playback = runtime.Playback;

            Assert.That(relay, Is.Not.Null,
                "A server is up and VoiceRuntime built no relay. Nothing would forward a voice frame, and the "
                + "install is a [RuntimeInitializeOnLoadMethod] precisely so this cannot be a scene that forgot to "
                + "add a component.");

            Assert.That(playback, Is.Not.Null,
                "A client is up and VoiceRuntime built no playback, so nothing on this machine could hear anybody.");

            // The remote connection is where the frame has to end up. Placing it at the
            // listener's distance is what makes the expected gain a fact about the rule
            // rather than about this test.
            var listenerId = FirstConnection().connectionId;
            _positions.Place(listenerId, new Vector3(AudibleDistanceMetres, 0f, 0f));

            yield return Speak(effort);

            // --- it actually left through the transport ---

            Assert.That(relay!.FramesForwarded, Is.GreaterThan(0),
                "The relay forwarded nothing. Either VoiceCapture never handed it a frame — check that the "
                + "microphone opened, which needs an audience — or VoiceRules.ClearRange(" + effort + ") = "
                + VoiceRules.ClearRange(effort) + " m did not admit a listener at " + AudibleDistanceMetres + " m.");

            // Waited for rather than sampled. The two counters are incremented at different
            // points in the same pipeline: BytesForwarded the moment the relay hands a
            // payload to conn.Send, _serverUnreliableBytes when KcpTransport actually puts
            // a datagram on the wire. KCP batches, so a straight comparison races its send
            // queue — measured 1014 of 1312 bytes flushed at the instant of the assert,
            // with the remaining 298 in the queue. The assertion below is unchanged and
            // still fails if the bytes never reach a socket; this only stops it failing
            // because they had not reached one YET.
            yield return WaitFor(
                () => _serverUnreliableBytes >= relay.BytesForwarded, AudioSecondsBudget);

            Assert.That(_serverUnreliableBytes, Is.GreaterThanOrEqualTo(relay.BytesForwarded),
                "KcpTransport.ServerSend carried " + _serverUnreliableBytes + " bytes on the unreliable channel and "
                + "the relay claims to have forwarded " + relay.BytesForwarded + " bytes of voice. Whatever the relay "
                + "did, it did not reach a socket.");

            // --- and the far side heard it ---

            Assert.That(playback!.FramesReceived, Is.GreaterThan(0),
                "The client's VoiceDownstreamMessage handler never ran. With NetworkClient.activeHost false there is "
                + "no in-process path, so this means the datagram never arrived or the handler is not registered — "
                + "NetworkClient.Shutdown clears the handler table, which is why VoiceRuntime registers per session.");

            Assert.That(playback.FramesPlayed, Is.GreaterThan(0),
                "Frames arrived and none became audible. " + playback.DroppedForeignCodec + " were refused for a "
                + "codec mismatch, " + playback.DroppedInaudible + " were judged inaudible by the rule, "
                + playback.DroppedSilentEffort + " carried an effort byte that decoded to Silent, "
                + playback.DroppedMalformed + " had an empty or oversized payload, "
                + playback.DroppedNoListener + " arrived with no local ear to measure a distance from, and "
                + playback.DroppedDecodedEmpty + " decoded to zero samples.");

            // --- at exactly the gain the rule says ---

            var expectedGain = VoiceRules.Gain(effort, AudibleDistanceMetres, false);

            Assert.That(expectedGain, Is.EqualTo(2f / 3f).Within(1e-4f),
                "VoiceRules is linear and TalkRange is " + VoiceRules.TalkRange + " m, so " + AudibleDistanceMetres
                + " m has to be 1 − 4/12 = 2/3. If this fails the rule changed and this test's arithmetic has to "
                + "be re-derived rather than the tolerance widened.");

            Assert.That(playback.DistanceOf(VoiceCapture.HostSpeakerConnectionId),
                Is.EqualTo(AudibleDistanceMetres).Within(1e-3f),
                "The listener measured the wrong distance to the speaker. The speaker's position rides on the packet "
                + "(see VoiceDownstreamMessage) precisely so that this does not depend on the speaker having been "
                + "replicated to this client yet.");

            Assert.That(playback.OccludedFrom(VoiceCapture.HostSpeakerConnectionId), Is.False,
                "There is no collider between these two and the listener found a wall. The occlusion probe casts "
                + "against GameAudio.OccluderMask with both ends inset by "
                + VoicePlayback.OcclusionInsetMetres + " m.");

            Assert.That(playback.GainOf(VoiceCapture.HostSpeakerConnectionId), Is.EqualTo(expectedGain).Within(1e-4f),
                "The listener applied a different gain than VoiceRules.Gain(" + effort + ", " + AudibleDistanceMetres
                + " m, occluded: false) = " + expectedGain + ".");

            // --- and the samples in the buffer are the waveform, at that gain ---

            var heard = new float[VoiceCodec.FrameSamples];
            var got = playback.PeekSamples(VoiceCapture.HostSpeakerConnectionId, heard, heard.Length);

            Assert.That(got, Is.EqualTo(VoiceCodec.FrameSamples),
                "The far side's buffer held " + got + " samples where a whole 20 ms frame is "
                + VoiceCodec.FrameSamples + ".");

            var correlation = Correlation(Reference, heard);
            Assert.That(correlation, Is.GreaterThanOrEqualTo(CorrelationFloor),
                "The far side is holding " + got + " samples that do not correlate with what was spoken "
                + "(r = " + correlation.ToString("0.0000") + ", floor " + CorrelationFloor + "). Silence and noise "
                + "both land near zero here, so this is the assertion that says the audio is the audio.");

            var expectedRms = Rms(Reference) * expectedGain;
            var heardRms = Rms(heard);

            Assert.That(heardRms, Is.EqualTo(expectedRms).Within(expectedRms * AmplitudeTolerance),
                "The waveform arrived but at the wrong level: expected " + expectedRms.ToString("0.00000")
                + " (source " + Rms(Reference).ToString("0.00000") + " × gain " + expectedGain.ToString("0.0000")
                + ") and measured " + heardRms.ToString("0.00000") + ". A ratio of "
                + (heardRms / Rms(Reference)).ToString("0.0000") + " against an expected " + expectedGain.ToString("0.0000")
                + " is what a second roll-off on top of the rule's would look like — VoicePlayback gives the "
                + "AudioSource a flat custom curve for exactly that reason.");
        }

        /// <summary>
        /// The same wire, the other direction: a remote client speaks and the host's relay
        /// receives it having stamped the speaker from the socket rather than the payload.
        /// <para>
        /// The headline test measures host → client because that is the direction in which a
        /// listener exists in one process. This one measures client → host, which is the
        /// hop that carries §13's authority argument: <c>VoiceUpstreamMessage</c> has no
        /// speaker field, so the identity on every frame the relay handles came from
        /// <c>NetworkConnectionToClient</c> and cannot be claimed.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AClientsFrameReachesTheHostsRelayOverTheSocket()
        {
            const VoiceEffort effort = VoiceEffort.Talk;

            yield return BringUpTwoPeers(UpstreamPort);

            AssertTheWireIsReal();

            var runtime = VoiceRuntime.Instance!;
            var relay = runtime.Relay!;
            var capture = runtime.Capture!;

            var listenerId = FirstConnection().connectionId;
            _positions!.Place(VoiceCapture.HostSpeakerConnectionId, Vector3.zero);
            _positions.Place(listenerId, new Vector3(AudibleDistanceMetres, 0f, 0f));
            _positions.PlaceEar(new Vector3(AudibleDistanceMetres, 0f, 0f));

            // Force the client path on a machine that is also the host: this is the only
            // place in the suite that exercises NetworkClient.Send for voice, and without it
            // the upstream half would be code nothing runs.
            var before = relay.FramesReceived;
            var frame = new byte[VoiceCodec.FrameBytes];
            VoiceCodec.Encode(Reference, 0, frame);

            NetworkClient.Send(
                new VoiceUpstreamMessage
                {
                    Codec = (byte)VoiceCodecId.Adpcm,
                    Effort = (byte)effort,
                    Payload = new ArraySegment<byte>(frame, 0, frame.Length),
                },
                VoiceWire.Channel);

            yield return WaitFor(() => relay.FramesReceived > before, AudioSecondsBudget);

            Assert.That(relay.FramesReceived, Is.GreaterThan(before),
                "The host's VoiceUpstreamMessage handler never ran. It is registered with requireAuthentication: "
                + "true, which the shipped HorrorGameNetworkManager satisfies on connect — a server started without "
                + "one would silently drop every voice frame, which is the failure this arrangement exists to catch.");

            Assert.That(_clientUnreliableBytes, Is.GreaterThan(0),
                "KcpTransport.ClientSend put nothing on the unreliable channel, so whatever the host received did "
                + "not travel through a socket.");

            Assert.That(capture, Is.Not.Null,
                "VoiceRuntime built no VoiceCapture for the local runner, so a human at this keyboard has no way "
                + "to speak at all.");
        }

        // ------------------------------------------------------------------
        // The cutoff, the wall, and §06
        // ------------------------------------------------------------------

        /// <summary>
        /// Out of range, nothing is sent — and the microphone is not merely skipped, it is
        /// closed.
        /// <para>
        /// This is the answer to "twenty people", tested rather than asserted: the cheapest
        /// bandwidth in a twenty-runner race is the bandwidth nobody spends. It is also
        /// §13's 도청 방지, whose sender-side half is refusing to produce the data at all,
        /// and the reason a player alone in a corridor can watch their operating system's
        /// recording indicator stay dark.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AWhisperDoesNotCarryAndTheMicrophoneCloses()
        {
            const VoiceEffort effort = VoiceEffort.Whisper;

            yield return BringUpTwoPeers(VoicePort);

            var runtime = VoiceRuntime.Instance!;
            var relay = runtime.Relay!;
            var playback = runtime.Playback!;
            var capture = runtime.Capture!;

            var listenerId = FirstConnection().connectionId;
            _positions!.Place(VoiceCapture.HostSpeakerConnectionId, Vector3.zero);
            _positions.Place(listenerId, new Vector3(OutOfRangeMetres, 0f, 0f));
            _positions.PlaceEar(new Vector3(OutOfRangeMetres, 0f, 0f));

            yield return Speak(effort);

            Assert.That(VoiceRules.ClearRange(effort), Is.LessThan(OutOfRangeMetres),
                "This test only means anything while " + OutOfRangeMetres + " m is outside a whisper's "
                + VoiceRules.ClearRange(effort) + " m.");

            Assert.That(capture.HasAudience, Is.False,
                "The speaker's own gate thinks somebody is in range at " + OutOfRangeMetres + " m of a "
                + VoiceRules.ClearRange(effort) + " m whisper.");

            Assert.That(capture.IsCapturing, Is.False,
                "Nobody can hear this runner and the microphone is still open. §13's sender-side cutoff is to stop "
                + "producing the data, not to produce it and drop it — a frame that exists is a frame an attacker "
                + "could ask a peer to forward, and an open microphone is a lit recording indicator.");

            Assert.That(capture.SentFrames, Is.EqualTo(0),
                "Frames were sent to nobody. At §11's twenty runners this is the difference between 0 B/s and "
                + VoiceCodec.BytesPerSecondPerStream + " B/s per silent-but-transmitting runner.");

            Assert.That(relay.FramesForwarded, Is.EqualTo(0),
                "The host forwarded a whisper " + OutOfRangeMetres + " m to somebody. The relay is the gate an "
                + "attacker cannot edit, so this failing means the rule is not being applied host-side at all.");

            Assert.That(playback.FramesPlayed, Is.EqualTo(0),
                "Something became audible at " + OutOfRangeMetres + " m of a whisper.");
        }

        /// <summary>
        /// A wall between two runners costs the voice the rule's <c>OccludedFraction</c>,
        /// and it is the receiver that finds it.
        /// <para>
        /// The host gates on clear-air range and cannot see the listener's wall; §01 says a
        /// voice through a wall survives at 0.62 of its range and loses level as well. Both
        /// halves have to be visible from the far side or the wall is decoration.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AWallBetweenThemCostsTheRulesOcclusionAndNotTheEnginesRolloff()
        {
            const VoiceEffort effort = VoiceEffort.Talk;

            _positions!.Place(VoiceCapture.HostSpeakerConnectionId, Vector3.zero);
            _positions.PlaceEar(new Vector3(AudibleDistanceMetres, 0f, 0f));

            // Half way between them, and thick enough that a line cast inset by
            // VoicePlayback.OcclusionInsetMetres at each end still has to pass through it.
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "VoiceTests_Wall";
            _wall.transform.position = new Vector3(AudibleDistanceMetres * 0.5f, 0f, 0f);
            _wall.transform.localScale = new Vector3(0.5f, 4f, 4f);

            // A collider with no renderer: a lit cube in the middle of a headless test is
            // a draw call nobody looks at.
            var renderer = _wall.GetComponent<Renderer>();
            if (renderer != null)
            {
                UnityEngine.Object.DestroyImmediate(renderer);
            }

            yield return BringUpTwoPeers(VoicePort);

            var runtime = VoiceRuntime.Instance!;
            var playback = runtime.Playback!;

            var listenerId = FirstConnection().connectionId;
            _positions.Place(listenerId, new Vector3(AudibleDistanceMetres, 0f, 0f));

            // Where this machine's own ears are. Without it VoicePlayback has no distance
            // to apply the rule at and drops every frame — which used to be the one exit
            // between FramesReceived and FramesPlayed that incremented no counter.
            _positions.PlaceEar(new Vector3(AudibleDistanceMetres, 0f, 0f));

            yield return Speak(effort);

            Assert.That(playback.FramesPlayed, Is.GreaterThan(0),
                "Nothing was audible through the wall at all. A wall costs a voice range and level — "
                + "OccludedFraction is " + VoiceRules.OccludedFraction + " — and " + AudibleDistanceMetres
                + " m is still well inside a talk's occluded range of "
                + VoiceRules.RangeMetres(effort, true) + " m.");

            Assert.That(playback.OccludedFrom(VoiceCapture.HostSpeakerConnectionId), Is.True,
                "There is a collider squarely between the two of them and the listener's probe missed it.");

            var occludedGain = VoiceRules.Gain(effort, AudibleDistanceMetres, true);
            var clearGain = VoiceRules.Gain(effort, AudibleDistanceMetres, false);

            Assert.That(occludedGain, Is.LessThan(clearGain),
                "The rule says a wall costs level as well as range; if these are equal the rule changed.");

            Assert.That(playback.GainOf(VoiceCapture.HostSpeakerConnectionId), Is.EqualTo(occludedGain).Within(1e-4f),
                "Through a wall the listener applied " + playback.GainOf(VoiceCapture.HostSpeakerConnectionId)
                + " where VoiceRules.Gain(" + effort + ", " + AudibleDistanceMetres + " m, occluded: true) is "
                + occludedGain + ". Clear air would have been " + clearGain + ".");

            var heard = new float[VoiceCodec.FrameSamples];
            playback.PeekSamples(VoiceCapture.HostSpeakerConnectionId, heard, heard.Length);

            var expectedRms = Rms(Reference) * occludedGain;
            Assert.That(Rms(heard), Is.EqualTo(expectedRms).Within(expectedRms * AmplitudeTolerance),
                "The samples in the far side's buffer are not at the occluded level.");
        }

        /// <summary>
        /// Speaking reaches §06, and it does so whether or not anybody is in range to hear
        /// it.
        /// <para>
        /// <b>This is the half that would ship silently broken.</b> <c>VoiceRules</c> is
        /// explicit that the reason to have voice in this game at all is that a shout
        /// carries to the creature 1.4× further than it carries to the person you are
        /// shouting at. A build where the frames worked and this line did not would be half
        /// the mechanic, and no audio test would notice.
        /// </para>
        /// <para>
        /// The "no audience" half is deliberate and is asserted rather than assumed: a
        /// runner who shouts alone in a corridor has still made a noise in a corridor.
        /// Gating the report on having listeners would make the safest place to shout the
        /// place where nobody can hear you, which inverts the design.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator SpeakingIsReportedToTheCreatureEvenWithNobodyInRange()
        {
            const VoiceEffort effort = VoiceEffort.Shout;

            var director = BuildQuietDirector();

            yield return BringUpTwoPeers(VoicePort);

            var runtime = VoiceRuntime.Instance!;
            var capture = runtime.Capture!;

            var listenerId = FirstConnection().connectionId;
            _positions!.Place(VoiceCapture.HostSpeakerConnectionId, Vector3.zero);

            // Far enough that even a shout does not reach: the audience is empty, and the
            // creature must still be told.
            var beyondAShout = VoiceRules.ShoutRange * 3f;
            _positions.Place(listenerId, new Vector3(beyondAShout, 0f, 0f));
            _positions.PlaceEar(new Vector3(beyondAShout, 0f, 0f));

            capture.SetEffort(effort);

            yield return WaitFor(() => capture.Director != null, AudioSecondsBudget);
            yield return null;

            Assert.That(capture.Director, Is.Not.Null,
                "VoiceCapture never found a MatchDirector, so the creature is never told anybody spoke. It searches "
                + "with FindFirstObjectByType at NetworkSendRate, the same way NetLocalRunner polls for a rig.");

            Assert.That(capture.HasAudience, Is.False,
                "This test is only interesting while nobody is in range; at " + beyondAShout + " m nobody is.");

            Assert.That(director.VoiceEffort, Is.EqualTo(effort),
                "MatchDirector.VoiceEffort reads " + director.VoiceEffort + " while the player is holding "
                + effort + ". MatchDirector.TakeVoiceCue turns this into a NoiseCue for §06, so a voice system "
                + "that never sets it does not make you findable — which is half the reason voice is in this game.");

            // And the numbers that make an effort a decision rather than a louder microphone.
            //
            // Metal, not concrete, and the difference is §12 rather than a convenient
            // choice: the creature's range is the clear range times 1.4 times the SURFACE's
            // clarity, so a stairwell (clarity 1.00) carries a shout 42 m to something that
            // only carries 30 m to a person, while concrete (0.50) carries the same shout
            // 21 m and is therefore the safer place to be loud. That the floor decides is
            // the point; asserting the headline number on the floor it is quoted on keeps
            // this test honest about which one it is.
            var floor = HorrorGame.Core.Map.FloorMaterial.Metal;
            var toPlayers = VoiceRules.ClearRange(effort);
            var toCreature = VoiceRules.MonsterHearingRangeMetres(effort, floor);

            Assert.That(toCreature, Is.GreaterThan(toPlayers),
                "On " + floor + " a shout is supposed to reach the creature from further than it reaches the people "
                + "you are shouting at (" + toCreature + " m against " + toPlayers + " m). If it does not, shouting "
                + "is a free upgrade to talking and the effort ladder means nothing.");

            Assert.That(
                VoiceRules.MonsterHearingRangeMetres(VoiceEffort.Whisper, floor),
                Is.EqualTo(0f),
                "A whisper has to be inaudible to the creature on every surface, by construction. It is the only "
                + "way to say something that is not also a way to be found, and without it players stop speaking "
                + "at all and the system loses the thing it is for.");

            Assert.That(
                toCreature,
                Is.GreaterThan(VoiceRules.MonsterHearingRangeMetres(VoiceEffort.Talk, floor)),
                "Shouting has to cost more than talking or there is no ladder.");

            capture.SetEffort(VoiceEffort.Silent);
            yield return null;

            Assert.That(director.VoiceEffort, Is.EqualTo(VoiceEffort.Silent),
                "Releasing the key left the creature being told this runner is still shouting.");
        }

        // ------------------------------------------------------------------
        // Twenty runners
        // ------------------------------------------------------------------

        /// <summary>
        /// Twenty runners in one chamber cost three streams each, not nineteen.
        /// <para>
        /// The arithmetic is in <see cref="VoiceSlots"/> and this is where it is held to.
        /// Uncapped, §11's full field all speaking at once is 20 × 19 × 8.2 kB/s = 3.1 MB/s
        /// off the host's uplink; capped it is 20 × 3 × 8.2 kB/s = 492 kB/s. The middle of
        /// B8 is where all twenty are trying to stand, so this is a reachable state and not
        /// a hypothetical.
        /// </para>
        /// </summary>
        [Test]
        public void TwentyRunnersInOneChamberCostThreeStreamsEach()
        {
            var slots = new VoiceSlots();
            var admitted = new List<int>();

            // Speaker n stands n metres away, so the nearest are the low ids and the
            // eviction order is knowable by hand.
            for (var speaker = 1; speaker < GameConstants.RaceRunnersMax; speaker++)
            {
                if (slots.TryAdmit(0, speaker, speaker, 0d))
                {
                    admitted.Add(speaker);
                }
            }

            Assert.That(admitted.Count, Is.EqualTo(VoiceSlots.PerListener),
                "Nineteen speakers were offered to one listener and " + admitted.Count + " were admitted where the "
                + "cap is " + VoiceSlots.PerListener + ". Uncapped, §11's field costs the host "
                + (GameConstants.RaceRunnersMax * (GameConstants.RaceRunnersMax - 1)
                   * (long)VoiceCodec.BytesPerSecondPerStream / 1000) + " kB/s.");

            Assert.That(admitted, Is.EqualTo(new List<int> { 1, 2, 3 }),
                "The slots went to " + string.Join(", ", admitted) + " rather than the three nearest. Distance is "
                + "what the rule already calls loudness, so admitting a further speaker over a nearer one reads as "
                + "the game muting the person standing beside you.");

            // A nearer arrival evicts the furthest holder — the case that matters when
            // somebody runs up to you mid-conversation.
            Assert.That(slots.TryAdmit(0, 99, 0.5f, 0d), Is.True,
                "Somebody standing half a metre away was refused while three people further off held the slots.");

            Assert.That(slots.Evictions, Is.EqualTo(1),
                "The nearer speaker was admitted without anybody being evicted, which means the cap is not a cap.");

            // The claim in the class docs, restated as arithmetic the test would fail on.
            var cappedBytesPerSecond =
                (long)GameConstants.RaceRunnersMax * VoiceSlots.PerListener * VoiceCodec.BytesPerSecondPerStream;

            Assert.That(cappedBytesPerSecond, Is.LessThan(600_000L),
                "The host's worst-case voice egress is " + (cappedBytesPerSecond / 1000) + " kB/s ("
                + (cappedBytesPerSecond * 8 / 1_000_000d).ToString("0.0") + " Mbit/s). VoiceSlots.PerListener and "
                + "VoiceCodec.BytesPerSecondPerStream are the only two numbers in that product; if this fails, one "
                + "of them moved and the budget argument in VoiceSlots has to be rewritten rather than the "
                + "threshold raised.");
        }

        /// <summary>
        /// One stream costs what the codec's own constants say it costs.
        /// <para>
        /// Every bandwidth sentence in this system multiplies
        /// <see cref="VoiceCodec.BytesPerSecondPerStream"/> by something, so if that number
        /// is wrong every one of them is. It is checked here against the frame that is
        /// actually produced rather than against itself.
        /// </para>
        /// </summary>
        [Test]
        public void OneStreamCostsWhatTheCodecSaysItCosts()
        {
            var frame = new byte[VoiceCodec.FrameBytes];
            var written = VoiceCodec.Encode(Reference, 0, frame);

            Assert.That(written, Is.EqualTo(VoiceCodec.FrameBytes),
                "A 20 ms frame encoded to " + written + " bytes where the constant says " + VoiceCodec.FrameBytes + ".");

            Assert.That(VoiceCodec.FrameBytes * VoiceCodec.FramesPerSecond,
                Is.EqualTo(VoiceCodec.BytesPerSecondPerStream));

            Assert.That(VoiceCodec.AudioBytesPerSecondPerStream, Is.EqualTo(8000),
                "The audio itself is 16000 samples × 4 bits, which is §13's 초당 2~8KB ceiling exactly. Everything "
                + "over that is header.");

            // Stated as the real figure rather than as "inside the budget", because it is
            // not inside: 8200 B/s is 2.4% over 8000 and 0.1% over 8 KiB. The overage is
            // the self-contained frame header, which VoiceCodec's remarks defend and price.
            // A test that asserted "≤ 8 KB" here would have to round to pass, and a green
            // number that needed rounding is the shape of claim this repository distrusts.
            Assert.That(VoiceCodec.BytesPerSecondPerStream, Is.EqualTo(8200),
                "One stream costs " + VoiceCodec.BytesPerSecondPerStream + " B/s: "
                + VoiceCodec.AudioBytesPerSecondPerStream + " of audio plus "
                + (VoiceCodec.BytesPerSecondPerStream - VoiceCodec.AudioBytesPerSecondPerStream)
                + " of per-frame header. If this moved, every bandwidth sentence in VoiceSlots moved with it.");

            var headerShare = 1d - (VoiceCodec.AudioBytesPerSecondPerStream
                                    / (double)VoiceCodec.BytesPerSecondPerStream);

            Assert.That(headerShare, Is.LessThan(0.03d),
                "The self-contained frame header is " + (headerShare * 100d).ToString("0.0") + "% of the stream. "
                + "It buys a lost datagram costing exactly the 20 ms it contained instead of desynchronising the "
                + "decoder for the rest of the talkspurt, which is worth a few percent and would not be worth ten.");

            // Round trip at unity gain: the fidelity the headline test's tolerances rest on.
            var decoded = new float[VoiceCodec.FrameSamples];
            var samples = VoiceCodec.Decode(frame, written, decoded, 0, 1f);

            Assert.That(samples, Is.EqualTo(VoiceCodec.FrameSamples));
            Assert.That(Correlation(Reference, decoded), Is.GreaterThanOrEqualTo(CorrelationFloor),
                "The codec's own round trip does not clear the floor the network test uses, so a failure over there "
                + "could not be told apart from a failure in here.");
        }

        // ------------------------------------------------------------------
        // The production position source
        // ------------------------------------------------------------------

        /// <summary>
        /// The shipped position source reads <c>NetPlayer</c>'s host-authoritative position
        /// and the scene's <c>AudioListener</c>.
        /// <para>
        /// The socket tests script the positions, because a fixture with real connections
        /// has no character controllers. That would leave the production answer unmeasured,
        /// which is this repository's exact failure mode — a green number nobody verified.
        /// So the real one is exercised here.
        /// </para>
        /// </summary>
        [Test]
        public void TheShippedPositionSourceReadsNetPlayer()
        {
            var positions = new NetVoicePositions();

            var listener = _ear!.AddComponent<AudioListener>();
            _ear.transform.position = new Vector3(3f, 1.6f, -2f);

            Assert.That(positions.TryGetLocalListenerPosition(out var ear), Is.True,
                "NetVoicePositions could not find the scene's AudioListener, so every distance this machine "
                + "computes would be from nowhere. GameAudio resolves the ear the same way.");

            Assert.That(ear, Is.EqualTo(_ear.transform.position),
                "The ear is not where the AudioListener is. NetLocalRunner switches the owned NetPlayer proxy's "
                + "body off and NetPlayer only writes its transform on the server, so the replicated proxy is the "
                + "wrong answer on a client — usually the origin.");

            Assert.That(listener, Is.Not.Null);

            Assert.That(positions.SeatIndexOf(4242), Is.EqualTo(-1),
                "A connection that does not exist reported a seat. RaceHud keys on §02's Racer.Id, so a wrong seat "
                + "would mark the wrong runner as talking.");

            Assert.That(positions.TryGetSpeakerPosition(4242, out _), Is.False,
                "A connection that does not exist reported a position. Answering (0,0,0) would make every unseated "
                + "runner audible to whoever is standing in the middle of B1.");
        }

        // ------------------------------------------------------------------
        // rig
        // ------------------------------------------------------------------

        /// <summary>
        /// Brings up the shipped manager as a server, connects a genuinely remote client to
        /// it, and installs the voice runtime.
        /// <para>
        /// <c>StartServer</c> then <c>StartClient</c> rather than <c>StartHost</c>, for the
        /// reason <c>NetSocketTests</c> gives: <c>StartHost</c> would wire an in-process
        /// <c>LocalConnectionToServer</c> and never open a socket, and every assertion in
        /// this file would then be about memory.
        /// </para>
        /// </summary>
        private IEnumerator BringUpTwoPeers(ushort port)
        {
            _rig = new GameObject("VoiceSocketRig");
            _rig.SetActive(false);

            // Inactive while the transport is configured: KcpTransport.Awake freezes its
            // KcpConfig from the serialised fields, so a port assigned after AddComponent on
            // a live object is remembered by the inspector and ignored by the socket.
            _transport = _rig.AddComponent<KcpTransport>();
            _transport.port = port;
            _transport.DualMode = DualMode;

            _manager = _rig.AddComponent<HorrorGameNetworkManager>();
            _manager.transport = _transport;
            _manager.autoCreatePlayer = false;

            _rig.SetActive(true);
            Transport.active = _transport;

            // The transport's own send hooks, filtered to voice's channel. Bytes counted
            // here left through KcpTransport.ServerSend/ClientSend and nowhere else.
            _transport.OnServerDataSent += (_, segment, channelId) =>
            {
                if (channelId == VoiceWire.Channel)
                {
                    _serverUnreliableBytes += segment.Count;
                }
            };

            _transport.OnClientDataSent += (segment, channelId) =>
            {
                if (channelId == VoiceWire.Channel)
                {
                    _clientUnreliableBytes += segment.Count;
                }
            };

            yield return null;

            _manager.networkAddress = Loopback;
            _manager.StartServer();
            _manager.StartClient();

            yield return WaitFor(
                () => NetworkClient.isConnected && NetworkServer.connections.Count == 1,
                ConnectSecondsBudget);

            Assert.That(NetworkClient.isConnected, Is.True,
                "No client reached Connected on " + Loopback + ":" + port + " within " + ConnectSecondsBudget
                + " s, so there is no wire to carry a voice.");

            Assert.That(NetworkServer.connections.Count, Is.EqualTo(1),
                "The client thinks it is connected and the server holds no connection for it.");

            // After the peers are up, so the runtime's first Update sees both transitions
            // and builds relay and playback in the same frame the game would.
            VoiceRuntime.EnsureInstalled();

            yield return WaitFor(
                () => VoiceRuntime.Instance != null
                      && VoiceRuntime.Instance.Relay != null
                      && VoiceRuntime.Instance.Playback != null
                      && VoiceRuntime.Instance.Capture != null,
                ConnectSecondsBudget);
        }

        /// <summary>The four assertions a host-mode fake could not satisfy. Lifted from <c>NetSocketTests</c>.</summary>
        private static void AssertTheWireIsReal()
        {
            Assert.That(NetworkClient.activeHost, Is.False,
                "NetworkClient.activeHost means the connection is a LocalConnectionToServer — Mirror's in-process "
                + "host wire, which never touches the transport. Every audio assertion in this file is worthless "
                + "if it is true.");

            Assert.That(NetworkServer.localConnection, Is.Null,
                "A local connection on the server is the same fake seen from the other end.");

            var conn = FirstConnection();

            Assert.That(conn, Is.Not.InstanceOf<LocalConnectionToClient>(),
                "The server's connection has to be a remote one for a datagram to be involved.");

            Assert.That(conn.address, Does.Contain(Loopback),
                "The server's idea of where this client is comes from KcpTransport.ServerGetClientAddress, which "
                + "reads the IPEndPoint a datagram actually arrived from. Got: '" + conn.address + "'.");
        }

        /// <summary>
        /// Holds the effort down and pushes the reference waveform into the microphone's
        /// place until the far side has something, or the budget runs out.
        /// </summary>
        private IEnumerator Speak(VoiceEffort effort)
        {
            var runtime = VoiceRuntime.Instance!;
            var capture = runtime.Capture!;
            var playback = runtime.Playback!;

            capture.SetEffort(effort);

            // One frame for the audience refresh and the microphone to open: Push is ignored
            // while the line is closed, which is what a real device does and is the thing
            // AWhisperDoesNotCarryAndTheMicrophoneCloses relies on.
            yield return null;

            var deadline = Time.realtimeSinceStartup + AudioSecondsBudget;
            var pushed = 0;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (pushed < FramesToPush)
                {
                    pushed += _line!.Push(Reference, 0, Reference.Length);
                }

                if (pushed >= FramesToPush && playback.FramesPlayed > 0)
                {
                    // One more frame so the last arrival is written and Step has run.
                    yield return null;
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// A <see cref="MatchDirector"/> that exists to be told things and does not start a
        /// match.
        /// <para>
        /// Its <c>_autoStart</c> is on by default, and a director that reached
        /// <c>BeginMatch</c> with no map would spend this test logging about a building
        /// that is not there. The flag is a serialised private, so it is written by
        /// reflection in the one window where it still matters — after <c>AddComponent</c>
        /// on an inactive object and before <c>Start</c> — rather than by disabling the
        /// component, which would leave it findable or not depending on how Unity treats a
        /// disabled behaviour in <c>FindFirstObjectByType</c>. The component stays enabled,
        /// so <see cref="VoiceCapture"/> finds it exactly as it would in a real descent.
        /// </para>
        /// </summary>
        private MatchDirector BuildQuietDirector()
        {
            _directorHost = new GameObject("VoiceTests_MatchDirector");
            _directorHost.SetActive(false);

            var director = _directorHost.AddComponent<MatchDirector>();

            var field = typeof(MatchDirector).GetField("_autoStart", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null,
                "MatchDirector._autoStart was renamed. This fixture needs a director that does not try to begin a "
                + "match in an empty scene; find the new name rather than deleting the guard.");

            field!.SetValue(director, false);

            _directorHost.SetActive(true);
            return director;
        }

        private static NetworkConnectionToClient FirstConnection()
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                return conn;
            }

            throw new InvalidOperationException("No connection on the server.");
        }

        private static void DestroyIfAny(ref GameObject? target)
        {
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }

            target = null;
        }

        /// <summary>
        /// Runs frames until <paramref name="condition"/> holds or the budget expires. Never
        /// asserts — the caller does, so the failure message can say what was waited for.
        /// </summary>
        private static IEnumerator WaitFor(Func<bool> condition, float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        /// <summary>One 20 ms frame of the test waveform. See <see cref="Reference"/>.</summary>
        private static float[] BuildReference()
        {
            var samples = new float[VoiceCodec.FrameSamples];

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (double)VoiceCodec.SampleRate;

                samples[i] = (float)(0.45d * (
                    (0.62d * Math.Sin(2d * Math.PI * 200d * t))
                    + (0.28d * Math.Sin(2d * Math.PI * 400d * t))
                    + (0.10d * Math.Sin(2d * Math.PI * 800d * t))));
            }

            return samples;
        }

        private static float Rms(float[] samples) => VoiceCodec.Rms(samples, 0, samples.Length);

        /// <summary>
        /// Normalised cross-correlation at zero lag, −1~1.
        /// <para>
        /// Zero lag is legitimate here only because <see cref="Reference"/> was chosen so
        /// that every captured frame is identical — see its remarks. It is scale-free by
        /// construction, so it says "this is the same waveform" without saying anything
        /// about the level; the level is a separate assertion, and separating them is what
        /// lets a failure name which of the two broke.
        /// </para>
        /// </summary>
        private static float Correlation(float[] a, float[] b)
        {
            var count = Math.Min(a.Length, b.Length);
            if (count == 0)
            {
                return 0f;
            }

            var dot = 0d;
            var normA = 0d;
            var normB = 0d;

            for (var i = 0; i < count; i++)
            {
                dot += a[i] * (double)b[i];
                normA += a[i] * (double)a[i];
                normB += b[i] * (double)b[i];
            }

            var denominator = Math.Sqrt(normA * normB);
            return denominator <= 0d ? 0f : (float)(dot / denominator);
        }

        /// <summary>
        /// Where everybody is, for a fixture that has real sockets and no bodies.
        /// <para>
        /// It answers <see cref="IVoicePositions"/> and nothing else — the codec, the
        /// audience rule, the wire and the attenuation are all the shipped path. The
        /// production implementation is covered by
        /// <see cref="TheShippedPositionSourceReadsNetPlayer"/>.
        /// </para>
        /// </summary>
        private sealed class ScriptedPositions : IVoicePositions
        {
            private readonly Dictionary<int, Vector3> _placed = new Dictionary<int, Vector3>();

            private Vector3 _ear;
            private bool _hasEar;

            /// <summary>Puts a connection somewhere.</summary>
            internal void Place(int connectionId, Vector3 position) => _placed[connectionId] = position;

            /// <summary>Puts this machine's ears somewhere.</summary>
            internal void PlaceEar(Vector3 position)
            {
                _ear = position;
                _hasEar = true;
            }

            /// <summary>
            /// Where the local speaker's own gate measures from.
            /// <para>
            /// <b>This is the one place the fixture cannot be literal, and it is worth
            /// being explicit about.</b> In the shipped game the local mouth and the local
            /// ear are the same head, so <c>VoiceRuntime.FollowTheEar</c> putting
            /// <see cref="VoiceCapture"/> on the <c>AudioListener</c> is exactly right. In
            /// one process the host is the speaker and the client is the listener, and they
            /// are two different logical runners standing in two different places — so the
            /// capture's transform is at the client's ear and answering its gate from there
            /// would ask "can the listener hear themselves", which is trivially yes and
            /// would make <see cref="AWhisperDoesNotCarryAndTheMicrophoneCloses"/> pass for
            /// the wrong reason. So the gate is answered from the speaker's placed position
            /// instead. Everything the gate is being tested for — that it asks
            /// <c>VoiceRules.ClearRange</c> for the effort and compares it against a real
            /// distance to a real other runner — is unchanged.
            /// </para>
            /// </summary>
            private Vector3 SpeakerOrigin =>
                _placed.TryGetValue(VoiceCapture.HostSpeakerConnectionId, out var at) ? at : Vector3.zero;

            /// <inheritdoc />
            public bool TryGetSpeakerPosition(int connectionId, out Vector3 position) =>
                _placed.TryGetValue(connectionId, out position);

            /// <inheritdoc />
            public void CopyListeners(List<int> into)
            {
                into.Clear();
                foreach (var id in _placed.Keys)
                {
                    into.Add(id);
                }
            }

            /// <inheritdoc />
            public int SeatIndexOf(int connectionId) => connectionId;

            /// <inheritdoc />
            public bool TryGetLocalListenerPosition(out Vector3 position)
            {
                position = _ear;
                return _hasEar;
            }

            /// <inheritdoc />
            public bool AnyListenerWithin(Vector3 from, float radiusMetres)
            {
                // `from` is deliberately ignored — see SpeakerOrigin for the one-process
                // reason, which is stated rather than hidden because ignoring an argument
                // is exactly the kind of thing that makes a test agree with itself.
                var origin = SpeakerOrigin;
                var radiusSquared = radiusMetres * radiusMetres;

                foreach (var pair in _placed)
                {
                    if (pair.Key == VoiceCapture.HostSpeakerConnectionId)
                    {
                        continue;
                    }

                    if ((pair.Value - origin).sqrMagnitude < radiusSquared)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
