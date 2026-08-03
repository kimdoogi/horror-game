#nullable enable

namespace HorrorGame.Audio
{
    /// <summary>
    /// Every one-shot the game fires at a moment rather than plays continuously.
    /// <para>
    /// <b>Why an enum instead of a clip reference per caller.</b> The things that make
    /// these sounds — a door, a 손전등, §09's 탈락 — live in four different assemblies
    /// and none of them may take a dependency on this one (ARCHITECTURE §3: systems
    /// couple through primitives). An int is a primitive. So a caller says "the
    /// flashlight clicked on" and this layer decides what that costs in the mix, what
    /// noise it makes to §04's Listener, and whether it is diegetic at all.
    /// </para>
    /// <para>
    /// <b>There is deliberately no monster cue here.</b> §06's vocalisations are a
    /// function of the state machine, not of an event, and <see cref="MonsterAudio"/>
    /// owns them precisely so that 정지 can be silent by construction — a cue id for a
    /// growl would be an invitation to fire one from a state that has no 소리 column.
    /// </para>
    /// <para>
    /// <b>The numbers are sparse and must stay that way.</b> The ids below are
    /// serialised as raw ints in <c>Assets/Audio/Resources/MatchAudioLibrary.asset</c>
    /// (one <c>cue:</c> line per authored bank). Renumbering the survivors after a
    /// deletion would silently re-point every clip in that file — open a door, hear the
    /// battery warning — and nothing would report it. So deleted rows leave their number
    /// behind as a hole, and a new cue takes the next free id at the end rather than
    /// filling one. <see cref="AudioCues.All"/> exists so nothing has to walk the gaps.
    /// </para>
    /// <para>
    /// <b>삭제됨 — 협동판의 32개 큐.</b> v0.5 had rows for the shop (4), 전리품 and the
    /// 금고 (7), §03's 단서 and 목표물 (4), 배터리 (3), §04's five roles and their
    /// hardware (9: 조명탄 2 · 소음 덫 2 · 배전반 · 청음기 · 분필 · 밧줄 · 소음 억제기),
    /// the barricade (2), 지상 도착 (1) and §09's ghost rattle (2). Every one of those
    /// systems is gone — <c>Core/Economy</c>, <c>Core/Clues</c>, <c>BatteryState</c>,
    /// <c>Flare</c>, the roles, and (this round) the rattle. A cue that resolves and can
    /// never be raised is a .wav that ships in the build for nobody, which is exactly
    /// what the shipped DLLs were found full of. See <c>docs/DESCENT-PIVOT.md</c> §9.
    /// </para>
    /// </summary>
    public enum AudioCueId
    {
        /// <summary>Nothing. Lets a caller pass "no sound" without a nullable.</summary>
        None = 0,

        // ----------------------------------------------------------------
        // §03 · §04 — the flashlight. The one thing anybody holds.
        // ----------------------------------------------------------------

        /// <summary>flashlight_on_*. §05's F key.</summary>
        FlashlightOn = 1,

        /// <summary>flashlight_off_*.</summary>
        FlashlightOff = 2,

        // ----------------------------------------------------------------
        // §12-B — doors. The race's only interaction with the world.
        // ----------------------------------------------------------------

        /// <summary>door_open_*. Raises §04's SelfNoiseDoor — "문을 열면 정보가 끊긴다".</summary>
        DoorOpen = 6,

        /// <summary>
        /// door_close_*. Same noise: §04 names the action, not the direction. In the race
        /// this is the sound of somebody shutting a 관문 behind them, which §11 makes the
        /// one thing a leading runner can do to the nineteen behind.
        /// </summary>
        DoorClose = 7,

        /// <summary>door_lock_*.</summary>
        DoorLock = 8,

        // ----------------------------------------------------------------
        // §01 · §02 — the descent, and how a runner's race ends.
        // ----------------------------------------------------------------

        /// <summary>
        /// descend_basement — 투하구에서 뛰어내리는 순간. Eight times a race, and it is
        /// the whole of §01's 층 이동: there are no stairs in this building.
        /// </summary>
        Descend = 26,

        /// <summary>
        /// escape_success — §02's 도착. The clip stem is co-op vocabulary (there is no
        /// 탈출 left to succeed at) and wants renaming with its file; the moment it marks
        /// is B8's 도착점, which is the only way to win.
        /// </summary>
        EscapeSuccess = 28,

        /// <summary>match_failure_wipe — §02's 시간 초과, where §07 runs out and nobody arrives.</summary>
        MatchFailure = 29,

        /// <summary>death_transition_* — §02 탈락, and the moment §09 takes the seat.</summary>
        DeathTransition = 30,

        // ----------------------------------------------------------------
        // §13 — proximity voice.
        // ----------------------------------------------------------------

        /// <summary>voice_activity_blip — §13's push-to-talk confirmation.</summary>
        VoiceActivity = 42,

        /// <summary>voice_out_of_range — §13 stops transmitting at VoiceCutoffDistance.</summary>
        VoiceOutOfRange = 43,
    }

    /// <summary>
    /// Helpers over <see cref="AudioCueId"/> that decide how a cue is treated in the
    /// mix. Static and engine-free so they can be asserted against without a scene.
    /// </summary>
    public static class AudioCues
    {
        /// <summary>
        /// One past the highest id. The size of an array indexed by <c>(int)cue</c>, and
        /// <em>not</em> a count of cues — the id space has holes in it on purpose, for
        /// the reason recorded on <see cref="AudioCueId"/>. Anything that wants to visit
        /// each live cue wants <see cref="All"/> instead; a loop over
        /// <c>1 .. Count</c> reports every hole as a missing sound.
        /// </summary>
        public const int Count = (int)AudioCueId.VoiceOutOfRange + 1;

        /// <summary>
        /// Every cue that exists, in id order and without the holes.
        /// <para>
        /// The authoritative list of what has to be authored. A library validator walks
        /// this; a raise site never needs it.
        /// </para>
        /// <para>
        /// Exposed as a read-only list rather than the array itself: this is the one
        /// place that says what the game's sounds are, and a caller that could sort or
        /// blank it would be editing that answer from outside.
        /// </para>
        /// </summary>
        public static readonly System.Collections.Generic.IReadOnlyList<AudioCueId> All = new[]
        {
            AudioCueId.FlashlightOn,
            AudioCueId.FlashlightOff,
            AudioCueId.DoorOpen,
            AudioCueId.DoorClose,
            AudioCueId.DoorLock,
            AudioCueId.Descend,
            AudioCueId.EscapeSuccess,
            AudioCueId.MatchFailure,
            AudioCueId.DeathTransition,
            AudioCueId.VoiceActivity,
            AudioCueId.VoiceOutOfRange,
        };

        /// <summary>
        /// The bus a cue mixes on.
        /// <para>
        /// The split is §-driven rather than folder-driven: a sound is on
        /// <see cref="AudioBus.Interface"/> exactly when it describes something that
        /// happened <em>to the player</em> rather than something that happened
        /// <em>in the room</em>. Being caught is a fact about your race, and a door
        /// opening is a fact about a door — one of them must not be occluded or panned,
        /// and the other must.
        /// </para>
        /// </summary>
        public static AudioBus BusOf(AudioCueId cue)
        {
            switch (cue)
            {
                case AudioCueId.Descend:
                case AudioCueId.EscapeSuccess:
                case AudioCueId.MatchFailure:
                case AudioCueId.DeathTransition:
                case AudioCueId.VoiceActivity:
                case AudioCueId.VoiceOutOfRange:
                    return AudioBus.Interface;

                default:
                    return AudioBus.Items;
            }
        }

        /// <summary>
        /// Whether a cue is placed in the world. The inverse of
        /// <see cref="BusOf"/> landing on <see cref="AudioBus.Interface"/> — kept as its
        /// own method so a caller reads the intent rather than the routing.
        /// </summary>
        public static bool IsPositional(AudioCueId cue)
        {
            return BusOf(cue) != AudioBus.Interface;
        }

        /// <summary>
        /// Noise this cue makes on the 0~1 scale
        /// <c>GameConstants.ListenerSelfNoiseThreshold</c> is quoted against, or 0.
        /// <para>
        /// <b>The sound and its consequence are one call.</b> §04 states the Listener's
        /// price as a rule about actions — "뛰거나 문을 열면 정보가 끊긴다" — so a door
        /// that plays a clip without raising noise is a door that is loud and harmless,
        /// which is the exact failure mode <see cref="WorldSoundEmitter"/>'s remarks
        /// warn about. Wiring them separately is what lets them drift apart, so they
        /// are not wired separately.
        /// </para>
        /// <para>
        /// <b>Only the door is left, and that is the design and not an omission.</b> The
        /// other entries here priced 조명탄, 소음 덫, 금고 and the barricade — all §04 ·
        /// §08 hardware, all deleted. §04 v1.1 leaves twenty identical runners holding
        /// one flashlight, so the only deliberate noise anybody can still make is a door,
        /// and the flashlight's own click is quiet by design: §03 charges for the light
        /// by being <em>seen</em>, not by being heard.
        /// </para>
        /// </summary>
        public static float NoiseOf(AudioCueId cue)
        {
            switch (cue)
            {
                case AudioCueId.DoorOpen:
                case AudioCueId.DoorClose:
                case AudioCueId.DoorLock:
                    return AudioTuning.SelfNoiseDoor;

                default:
                    return 0f;
            }
        }
    }
}
