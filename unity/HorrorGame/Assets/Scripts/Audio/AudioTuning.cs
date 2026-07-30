#nullable enable

namespace HorrorGame.Audio
{
    /// <summary>
    /// Mix constants: bus trims, filter corners, crossfade times, rolloff shape.
    /// <para>
    /// <b>Why these are not in <c>GameConstants</c>.</b> ARCHITECTURE §2 puts every
    /// tuned number there, and the design numbers this layer needs <em>are</em> read
    /// from there and only from there — <c>ListenerHearingRange</c>,
    /// <c>ListenerSelfNoiseThreshold</c>, <c>VoiceCutoffDistance</c>,
    /// <c>ThreatTierCount</c>. What is below is a different kind of number: it is
    /// gain staging, measured off the shipped WAVs, and it changes how loud the game
    /// is rather than what happens in it. This follows the precedent
    /// <c>HorrorGame.Steam.Voice.VoiceTuning</c> set for the same reason, including
    /// its escape clause — if one of these ever becomes a balance dial, it moves to
    /// <c>GameConstants</c> with a § citation at that point.
    /// </para>
    /// <para>
    /// <b>Two of them are arguably already balance dials</b> and are called out where
    /// they are declared: <see cref="ListenerChannelOcclusionFloorHz"/> and the
    /// self-noise levels. Both come out of open findings (F-002, F-003) whose
    /// resolution is the designer's call, and both are recorded here rather than in
    /// <c>GameConstants</c> only because this layer does not own that file.
    /// </para>
    /// <para>
    /// <b>Every level below is measured, not chosen.</b> The source is the shipped
    /// clip set under <c>Assets/Audio</c>, A-weighted RMS in dBFS, averaged across
    /// the representative clips of each family:
    /// </para>
    /// <code>
    ///   Footsteps (monster)   -28.1 dBA      Monster cues          -23.5 dBA
    ///   Footsteps (player)    -38.0 dBA      Monster presence bed  -47.7 dBA
    ///   Ambience zone beds    -38.9 dBA      Interface one-shots   -36.8 dBA
    ///   Ambience tension beds -51.4 dBA      Heartbeat layers      -53.0 dBA
    ///   Items                 -25.3 dBA
    /// </code>
    /// <para>
    /// A trim is therefore always <c>target − measured</c>, and the target is stated
    /// where the trim is declared. That is what makes a retune reviewable: the
    /// argument is about the target, not about a magic decibel figure.
    /// </para>
    /// </summary>
    public static class AudioTuning
    {
        // ====================================================================
        // Bus trims, dB. target − measured; see the class remarks for the
        // measurements and each member for its target and why.
        // ====================================================================

        /// <summary>
        /// Footsteps bus trim, dB. Target −12 dBFS at the reference distance.
        /// <para>
        /// The largest trim in the mix, because §04 hands the 청음사 nothing but this
        /// family and §12 makes the five surfaces a map. A monster footstep has to
        /// stay above the zone bed at the range the role is used at; the arithmetic
        /// that fixes the target is in <see cref="ListenerChannelOcclusionFloorHz"/>.
        /// </para>
        /// </summary>
        public const float TrimFootstepsDb = 16.0f;

        /// <summary>
        /// Monster bus trim, dB. Target −14 dBFS for the growl/roar one-shots, which
        /// puts the presence bed at about −38 dBFS in the same bus — a bed you feel
        /// before you identify, which is what §06's 추격 escalation needs.
        /// </summary>
        public const float TrimMonsterDb = 9.5f;

        /// <summary>
        /// Ambience bus trim, dB. Target −34 dBFS for the zone beds. Deliberately
        /// under the footstep channel: §12's material map only works if a footstep
        /// wins against the room it is in.
        /// </summary>
        public const float TrimAmbienceDb = 5.0f;

        /// <summary>Items bus trim, dB. Target −16 dBFS — under the footstep channel, over the bed.</summary>
        public const float TrimItemsDb = 9.5f;

        /// <summary>
        /// Interface bus trim, dB. Target −18 dBFS. Non-diegetic audio is the one
        /// family that must never be mistaken for something in the room, so it sits
        /// low and gets no occlusion or rolloff at all.
        /// </summary>
        public const float TrimInterfaceDb = 19.0f;

        /// <summary>
        /// Voice bus trim, dB. Unity, because the level arrives from a microphone
        /// rather than from an asset — there is nothing to measure and normalising
        /// it here would fight the codec's own gain.
        /// </summary>
        public const float TrimVoiceDb = 0f;

        /// <summary>Master trim, dB. Unity. The player's slider is a linear scale on top of it.</summary>
        public const float TrimMasterDb = 0f;

        // --------------------------------------------------------------------
        // Within-family trims. Applied by the director that owns the clips, not
        // by the bus, because they correct for how the clips were authored
        // rather than for what family they belong to.
        // --------------------------------------------------------------------

        /// <summary>
        /// Extra gain on §07's tension beds so they meet the zone beds, dB.
        /// −38.9 measured for zone against −51.4 for tension.
        /// </summary>
        public const float TensionBedMatchDb = 12.5f;

        /// <summary>
        /// Extra gain on the heartbeat layers, dB. −53.0 measured against the
        /// −38.9 zone bed. It is matched to the bed rather than placed above it:
        /// a heartbeat you notice is a heartbeat that has stopped working.
        /// </summary>
        public const float HeartbeatMatchDb = 14.0f;

        /// <summary>
        /// Extra gain on the monster presence and breath beds, dB. Lands them
        /// roughly 10 dB under the growl one-shots that share the bus, so the bed
        /// is the floor the cues punch through.
        /// </summary>
        public const float MonsterBedMatchDb = 14.0f;

        // ====================================================================
        // Occlusion. See OcclusionProfile and docs/BALANCE-FINDINGS.md F-002/F-003.
        // ====================================================================

        /// <summary>
        /// Lowest low-pass corner the Listener's channel may be pushed to, hertz.
        /// <b>This is the F-003 answer, and it is measured.</b>
        /// <para>
        /// F-003 requires every pair of §12's five surfaces to stay 1.4× apart in
        /// spectral centroid or the Listener cannot name the floor. Sweeping the
        /// corner frequency across the shipped monster footsteps, through a model of
        /// Unity's own <c>AudioLowPassFilter</c> (one biquad, Q = 1, 12 dB/oct):
        /// </para>
        /// <code>
        ///   corner    monster    walk    run     (worst pair, geometric mean of 4 variants)
        ///   dry        2.030    1.981   2.100
        ///   3000 Hz    1.894    1.720   1.707
        ///   2000 Hz    1.781    1.834   1.829
        ///   1000 Hz    1.555    1.888   1.653
        ///    800 Hz    1.476    1.844   1.597   ← floor
        ///    700 Hz    1.436    1.836   1.588
        ///    600 Hz    1.402    1.861   1.609
        ///    500 Hz    1.382    1.952   1.693   ← fails
        /// </code>
        /// <para>
        /// Separation is not monotonic in the corner — a filter can accidentally
        /// re-separate a pair on the way down — so the floor is the lowest corner
        /// from which <em>every</em> higher corner also passes, with a 5% margin so
        /// measurement noise cannot flip it. That is 796 Hz; 800 is the round number
        /// above it.
        /// </para>
        /// <para>
        /// <b>The number F-003 reports is not wrong, it is a different filter.</b>
        /// <c>tools/audio/verify_audio.py</c> measures through
        /// <c>butter(order=2) + filtfilt</c>, which is zero-phase and therefore
        /// ~24 dB/oct — twice the slope of the engine's. The same 800 Hz corner reads
        /// 1.396× there and 1.476× here. Under the steeper model the floor is 873 Hz.
        /// Neither is a bug; the engine is what the player hears, so the mix is built
        /// on the engine's curve, and the verifier's number is the conservative one.
        /// </para>
        /// <para>
        /// Arguably a balance dial rather than a mix value, since it decides at what
        /// range §04's role stops working. It lives here because Core is owned
        /// elsewhere; if F-003 is resolved by option 1 or 2 it belongs in
        /// <c>GameConstants</c> with a §04/§12 citation.
        /// </para>
        /// </summary>
        public const float ListenerChannelOcclusionFloorHz = 800f;

        /// <summary>
        /// Lowest corner for occluded speech, hertz. Higher than the Listener's floor
        /// on purpose: the consonants that carry intelligibility live between 2 and
        /// 4 kHz, and §03 makes a whole mechanic out of a spoken number surviving the
        /// trip from one player's memory to another's — "6이었나 9였나". A muffled
        /// teammate must still be a legible teammate.
        /// </summary>
        public const float VoiceOcclusionFloorHz = 2000f;

        /// <summary>
        /// Lowest corner for everything else, hertz. Nothing in §04 or §12 depends on
        /// a door hinge being identifiable through a wall, so this family gets the
        /// physically heavier filter that makes the building feel solid.
        /// </summary>
        public const float WorldOcclusionFloorHz = 500f;

        /// <summary>
        /// Corner used when nothing is in the way, hertz. Above the audible band, so
        /// the filter is transparent rather than switched off — an
        /// <c>AudioLowPassFilter</c> that toggles enabled state clicks.
        /// </summary>
        public const float OcclusionOpenCutoffHz = 22000f;

        /// <summary>
        /// Broadband attenuation added at full occlusion for the Listener's channel,
        /// dB. Small, because the low-pass alone already removes a measured 7.3 dB
        /// A-weighted on average — and 25.8 dB from 자갈, which is F-002's whole
        /// story. Stacking a realistic wall loss on top of that would delete the role
        /// §04 describes.
        /// </summary>
        public const float ListenerChannelOcclusionAttenuationDb = -4f;

        /// <summary>Broadband attenuation added at full occlusion for voice, dB. §13 wants a wall to cost something without ending a conversation.</summary>
        public const float VoiceOcclusionAttenuationDb = -9f;

        /// <summary>Broadband attenuation added at full occlusion for world sound, dB. The realistic figure; nothing reads it as information.</summary>
        public const float WorldOcclusionAttenuationDb = -14f;

        /// <summary>
        /// Seconds for an occlusion change to take effect. Long enough that walking
        /// past a doorway does not machine-gun the filter, short enough that stepping
        /// out from behind a pillar is heard as it happens.
        /// </summary>
        public const float OcclusionSmoothingSeconds = 0.18f;

        /// <summary>
        /// Seconds between occlusion raycasts. At §06's fastest monster (5.2 m/s) a
        /// tenth of a second is half a metre, which is well inside the smoothing
        /// above; probing every frame would cost five raycasts per emitter per frame
        /// for no audible gain.
        /// </summary>
        public const float OcclusionProbeIntervalSeconds = 0.1f;

        /// <summary>
        /// Rays cast per probe, the direct one included. Four offsets around the
        /// direct path turn occlusion into a fraction rather than a boolean, which is
        /// what stops a doorway from sounding like a wall.
        /// </summary>
        public const int OcclusionRayCount = 5;

        /// <summary>Radius, metres, of the ring the offset rays are spread over. About a doorway's half-width.</summary>
        public const float OcclusionRaySpreadRadius = 0.6f;

        // ====================================================================
        // Distance rolloff.
        // ====================================================================

        /// <summary>
        /// Distance, metres, inside which a positional source plays at full volume.
        /// Small so the distance cue starts working immediately — §05 has the
        /// Listener turning their body to triangulate, which needs a gradient near to
        /// hand as well as far away.
        /// </summary>
        public const float RolloffReferenceDistance = 2f;

        /// <summary>
        /// Exponent of the amplitude rolloff: amplitude ∝ (reference / distance)^p.
        /// <para>
        /// Unity's Logarithmic mode is p = 1 — free-field inverse distance, 6 dB per
        /// doubling. A basement is not a free field: corridors guide sound and rooms
        /// return it, so measured indoor decay is much shallower. It also has to be
        /// shallower here, because <c>GameConstants.ListenerHearingRange</c> is 40 m
        /// and at p = 1 a monster footstep arrives 22 dB under the zone bed there —
        /// §04's headline range would be silent.
        /// </para>
        /// <para>
        /// At p = 0.6 (3.6 dB per doubling) the measured chain — footstep −28.1 dBA,
        /// +16 dB trim, −7.3 dB from the 800 Hz occlusion floor, −13.2 dB of rolloff
        /// at 25 m — lands 1.3 dB <em>above</em> the trimmed zone bed, and 1.1 dB
        /// under it at 40 m. That is precisely the shape F-003 asks for: the cue
        /// keeps its identity to the edge of hearing and loses its audibility there,
        /// so the Listener stops being able to place the monster before they stop
        /// being able to name the floor.
        /// </para>
        /// </summary>
        public const float RolloffExponent = 0.6f;

        /// <summary>
        /// Fraction of the audible range over which the curve is taken to silence.
        /// Unity holds the last curve value beyond <c>maxDistance</c>, so a curve that
        /// ends above zero never actually goes quiet; a short taper at the end is what
        /// makes the range limit a fade rather than a cliff.
        /// </summary>
        public const float RolloffSilenceTaperFraction = 0.2f;

        /// <summary>Knots used to bake the custom rolloff curve. Enough for a smooth read, few enough to stay cheap.</summary>
        public const int RolloffCurveSamples = 24;

        /// <summary>
        /// Audible radius, metres, for a world one-shot with no design range of its
        /// own. Matches §12's widest legal gap between cover
        /// (<c>LineOfSightBreakSpacingMax</c>), so a prop is heard exactly as far as
        /// the map guarantees a sight line — a distance the level is built around
        /// instead of a number picked here.
        /// </summary>
        public const float DefaultWorldAudibleRange = 25f;

        // ====================================================================
        // Crossfades.
        // ====================================================================

        /// <summary>
        /// Seconds to cross a zone ambience bed over. §12 wants "재질 경계를 명확히",
        /// but a hard cut at a threshold reads as a bug rather than as a boundary, so
        /// this is short enough to be a step and long enough not to click.
        /// </summary>
        public const float ZoneAmbienceCrossfadeSeconds = 1.5f;

        /// <summary>
        /// Seconds to cross §07's tension beds over.
        /// <para>
        /// Long on purpose. §07 argues the pressure is "연속적" and
        /// <c>ThreatCurve</c> answers by keeping mechanics stepped and giving
        /// presentation a continuous scalar; an eight-second bed crossfade is what
        /// turns a tier boundary into something a player feels without ever being
        /// able to point at the moment it happened — which is the entire brief:
        /// players should feel the curve without reading a number.
        /// </para>
        /// </summary>
        public const float ThreatBedCrossfadeSeconds = 8f;

        /// <summary>Seconds to cross heartbeat layers over. Fast: danger arrives faster than the night does.</summary>
        public const float HeartbeatCrossfadeSeconds = 1.2f;

        /// <summary>Seconds to cross the monster presence bed in and out over.</summary>
        public const float MonsterBedCrossfadeSeconds = 0.9f;

        /// <summary>
        /// Seconds a bed takes to fall silent when its director is switched off.
        /// Separate from the crossfade because stopping is not a transition to
        /// anything — leaving the basement should not sound like entering a room.
        /// </summary>
        public const float BedStopFadeSeconds = 2.5f;

        // ====================================================================
        // Danger, heartbeat and presence.
        // ====================================================================

        /// <summary>
        /// Distance, metres, at which the monster starts contributing to danger.
        /// Read as a fraction of <c>GameConstants.ListenerHearingRange</c> would be
        /// tidier, but the two answer different questions — this is "when does the
        /// body react", and 25 m is where §12 guarantees the monster could already
        /// see you down a legal sight line.
        /// </summary>
        public const float DangerProximityRange = 25f;

        /// <summary>Danger contributed by the monster being in 추격 (§06). Saturates the scale: being hunted is the maximum.</summary>
        public const float DangerChaseWeight = 1f;

        /// <summary>Danger contributed by 경계 — it has heard something and is coming. §06.</summary>
        public const float DangerAlertWeight = 0.6f;

        /// <summary>Danger contributed by 수색 — it is sweeping the area you were last in. §06.</summary>
        public const float DangerSearchWeight = 0.45f;

        /// <summary>
        /// Danger contributed by 정지. Low but not zero: §06 calls the silence the
        /// game's weapon, and a heartbeat that drops to nothing the instant the
        /// monster goes quiet would hand the player the information the state exists
        /// to take away.
        /// </summary>
        public const float DangerStandstillWeight = 0.25f;

        /// <summary>Danger contributed by 순찰 — it is out there, and that is all. §06.</summary>
        public const float DangerPatrolWeight = 0.2f;

        /// <summary>
        /// Share of a state's danger that survives at maximum distance. Above zero
        /// because §06's 추격 is frightening from across a hall — the monster driving
        /// at you is the situation, and how far away it currently is is only how long
        /// you have. Distance scales the rest.
        /// </summary>
        public const float DangerDistanceFloorShare = 0.4f;

        /// <summary>
        /// Seconds for danger to rise to a new value. Fast — the reaction is the
        /// point.
        /// </summary>
        public const float DangerAttackSeconds = 0.35f;

        /// <summary>
        /// Seconds for danger to fall back. Much slower than the rise, because
        /// relief is not instant and because a monster that has just gone quiet is
        /// the most dangerous thing in §06's table.
        /// </summary>
        public const float DangerReleaseSeconds = 4.0f;

        /// <summary>Danger below which only the low heartbeat layer plays.</summary>
        public const float HeartbeatLowThreshold = 0.15f;

        /// <summary>Danger at which the mid heartbeat layer is fully in.</summary>
        public const float HeartbeatMidThreshold = 0.55f;

        /// <summary>Danger at which the high heartbeat layer is fully in. Reached only in 추격 or very close quarters.</summary>
        public const float HeartbeatHighThreshold = 0.85f;

        /// <summary>
        /// Playback rate multiplier applied to the heartbeat at full danger. A pulse
        /// that only gets louder reads as a volume change; one that also gets faster
        /// reads as a body.
        /// </summary>
        public const float HeartbeatMaxRateScale = 1.35f;

        // ====================================================================
        // §04 self-noise. The Listener's own price.
        // ====================================================================

        /// <summary>
        /// Self-noise of walking, on the 0~1 scale
        /// <c>GameConstants.ListenerSelfNoiseThreshold</c> (0.35) is quoted against.
        /// Below the threshold: §04 blinds the Listener for 뛰거나 문을 열면, and
        /// walking is neither.
        /// <para>
        /// §04 names the actions but no levels, so these three are derived from the
        /// two levels §04-adjacent sections <em>do</em> give — a 조명탄 at 0.70 and a
        /// 소음 함정 at 1.0 — by requiring the same ordering. They belong in
        /// <c>GameConstants</c> next to those two; they are here only because this
        /// layer does not own that file.
        /// </para>
        /// </summary>
        public const float SelfNoiseWalk = 0.20f;

        /// <summary>Self-noise of running. Above the threshold — §04: "뛰거나 … 정보가 끊긴다".</summary>
        public const float SelfNoiseRun = 0.55f;

        /// <summary>Self-noise of a sprint (§06's 주자). Above running, below a 조명탄's 0.70.</summary>
        public const float SelfNoiseSprint = 0.65f;

        /// <summary>Self-noise of opening or closing a door. Above the threshold — §04: "…문을 열면 정보가 끊긴다".</summary>
        public const float SelfNoiseDoor = 0.60f;

        /// <summary>
        /// Seconds a transient self-noise (a door, a dropped crate) keeps blinding the
        /// Listener. §04 cuts the feed while the noise lasts; a one-frame impulse
        /// would cut it for one frame, which nobody would ever perceive as a cost.
        /// </summary>
        public const float SelfNoiseTransientSeconds = 0.6f;

        /// <summary>
        /// Seconds a transient self-noise takes to decay away once its hold expires.
        /// A ramp rather than a switch, so the feed returns the way a room goes quiet.
        /// </summary>
        public const float SelfNoiseDecaySeconds = 0.5f;

        /// <summary>
        /// Attenuation, dB, applied to the Footsteps and Monster buses while the
        /// Listener is self-blinded. §04's constraint made audible: the reason you
        /// cannot hear the monster is that you are the loudest thing here, so the
        /// world gets quieter rather than the HUD getting an error message.
        /// </summary>
        public const float SelfNoiseDuckDb = -9f;

        /// <summary>
        /// Low-pass corner applied to those buses while self-blinded, hertz. Near the
        /// Listener's own occlusion floor, so being loud costs you the same top end a
        /// wall would — the two prices of §04 sound like the same price.
        /// </summary>
        public const float SelfNoiseDuckCutoffHz = 900f;

        /// <summary>Seconds the self-noise duck takes to engage and release.</summary>
        public const float SelfNoiseDuckSeconds = 0.25f;

        // ====================================================================
        // Footsteps.
        // ====================================================================

        /// <summary>Metres of travel between footsteps at <c>GameConstants.WalkSpeed</c>. A stride.</summary>
        public const float FootstepStrideMetres = 0.85f;

        /// <summary>
        /// Shortest gap between two footsteps, seconds. At §06's sprint (5.6 m/s) the
        /// stride above would fire every 0.15 s; this stops a frame spike or a
        /// teleport from firing a burst.
        /// </summary>
        public const float FootstepMinIntervalSeconds = 0.18f;

        /// <summary>Random pitch spread on a footstep, ± this fraction. Breaks the machine-gun repetition four variants alone cannot.</summary>
        public const float FootstepPitchJitter = 0.06f;

        /// <summary>Random volume spread on a footstep, ± this fraction.</summary>
        public const float FootstepVolumeJitter = 0.12f;

        // ====================================================================
        // Voice.
        // ====================================================================

        /// <summary>
        /// Seconds of continuous playback before a speaker counts as "talking" for
        /// the HUD blip. Short, but long enough that a single jitter-buffer frame
        /// does not flicker an indicator.
        /// </summary>
        public const float VoiceActivityHoldSeconds = 0.25f;

        // ====================================================================
        // Service.
        // ====================================================================

        /// <summary>Seconds a bus volume change is smoothed over. Prevents a settings slider from clicking.</summary>
        public const float BusVolumeSmoothingSeconds = 0.08f;

        /// <summary>
        /// Volume below which a source is treated as silent and stopped. −60 dB.
        /// Keeping voices and beds running at inaudible gain wastes voices, and Unity
        /// has a hard cap on those.
        /// </summary>
        public const float SilenceThreshold = 0.001f;
    }
}
