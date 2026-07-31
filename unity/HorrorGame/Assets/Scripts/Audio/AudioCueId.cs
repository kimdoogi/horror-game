#nullable enable

namespace HorrorGame.Audio
{
    /// <summary>
    /// Every one-shot the game fires at a moment rather than plays continuously.
    /// <para>
    /// <b>Why an enum instead of a clip reference per caller.</b> The things that make
    /// these sounds — a door, a 손전등, §08's shop — live in four different assemblies
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
    /// </summary>
    public enum AudioCueId
    {
        /// <summary>Nothing. Lets a caller pass "no sound" without a nullable.</summary>
        None = 0,

        // ----------------------------------------------------------------
        // §03 · §08 — the flashlight and its cell. The round-trip clock.
        // ----------------------------------------------------------------

        /// <summary>flashlight_on_*. §05's F key.</summary>
        FlashlightOn = 1,

        /// <summary>flashlight_off_*.</summary>
        FlashlightOff = 2,

        /// <summary>
        /// battery_low_warning. §03 makes darkness the objective's lock, so the warning
        /// is the loudest piece of information the item family carries.
        /// </summary>
        BatteryLow = 3,

        /// <summary>battery_dead — the cell is spent and the light will not come back on.</summary>
        BatteryDead = 4,

        /// <summary>battery_insert_* — a fresh cell. §03's reason to walk back up.</summary>
        BatteryInsert = 5,

        // ----------------------------------------------------------------
        // §12 · §04's 정비공 — doors, the map's bottlenecks.
        // ----------------------------------------------------------------

        /// <summary>door_open_*. Raises §04's SelfNoiseDoor — "문을 열면 정보가 끊긴다".</summary>
        DoorOpen = 6,

        /// <summary>door_close_*. Same noise: §04 names the action, not the direction.</summary>
        DoorClose = 7,

        /// <summary>door_lock_* — 정비공 cutting a 순환로 (§12).</summary>
        DoorLock = 8,

        /// <summary>barricade_place_*.</summary>
        BarricadePlace = 9,

        /// <summary>barricade_break_* — the monster is through it.</summary>
        BarricadeBreak = 10,

        // ----------------------------------------------------------------
        // §08 — loot. Split by material because weight is the decision.
        // ----------------------------------------------------------------

        /// <summary>loot_pickup_metal_small_*.</summary>
        LootPickupMetal = 11,

        /// <summary>loot_pickup_glass_jewel_*.</summary>
        LootPickupGlass = 12,

        /// <summary>loot_pickup_paper_*.</summary>
        LootPickupPaper = 13,

        /// <summary>loot_pickup_wood_heavy_* — §08's heavy tier, and it sounds like it.</summary>
        LootPickupHeavy = 14,

        /// <summary>loot_sell_credit — the shared wallet moving. §08.</summary>
        LootSell = 15,

        /// <summary>safe_dial_turn_loop — the 금고, which only 정비공 opens.</summary>
        SafeDial = 16,

        /// <summary>safe_open.</summary>
        SafeOpen = 17,

        // ----------------------------------------------------------------
        // §03 — clues. Non-diegetic: the read happens in the player's head.
        // ----------------------------------------------------------------

        /// <summary>
        /// clue_read_success. §03 forbids writing the clue down, so this is the only
        /// confirmation that the beam was held long enough and the memorising starts now.
        /// </summary>
        ClueReadSuccess = 18,

        /// <summary>
        /// clue_read_failed. Fired on every §03 interrupt — light lost, moved, out of
        /// range — because the player has to know the progress is gone rather than paused.
        /// </summary>
        ClueReadFailed = 19,

        /// <summary>objective_found.</summary>
        ObjectiveFound = 20,

        /// <summary>objective_pickup — both hands, no flashlight (§03).</summary>
        ObjectivePickup = 21,

        // ----------------------------------------------------------------
        // §08 — the shop, on the surface.
        // ----------------------------------------------------------------

        /// <summary>shop_open.</summary>
        ShopOpen = 22,

        /// <summary>shop_close.</summary>
        ShopClose = 23,

        /// <summary>shop_purchase_confirm.</summary>
        ShopPurchase = 24,

        /// <summary>shop_denied — the shared wallet could not cover it. §08.</summary>
        ShopDenied = 25,

        // ----------------------------------------------------------------
        // §01 · §03 — the round trip, and the end of the match.
        // ----------------------------------------------------------------

        /// <summary>descend_basement — §03's 왕복 starting again.</summary>
        Descend = 26,

        /// <summary>surface_reached — §07's clock becomes readable here and only here.</summary>
        SurfaceReached = 27,

        /// <summary>escape_success — §02's win.</summary>
        EscapeSuccess = 28,

        /// <summary>match_failure_wipe — §02's loss.</summary>
        MatchFailure = 29,

        /// <summary>death_transition_* — §09, this player becoming a ghost.</summary>
        DeathTransition = 30,

        // ----------------------------------------------------------------
        // Items with their own hardware. §04's 정비공 and 섬광수.
        // ----------------------------------------------------------------

        /// <summary>flare_ignite. §08 prices its noise at FlareIgniteNoiseLevel.</summary>
        FlareIgnite = 31,

        /// <summary>flare_die.</summary>
        FlareDie = 32,

        /// <summary>noisetrap_arm.</summary>
        NoiseTrapArm = 33,

        /// <summary>noisetrap_trigger — §04's loudest object, saturating the noise scale.</summary>
        NoiseTrapTrigger = 34,

        /// <summary>breaker_throw — 정비공 lighting a zone so a clue can be read (§04).</summary>
        BreakerThrow = 35,

        /// <summary>detector_ping.</summary>
        DetectorPing = 36,

        /// <summary>chalk_mark_* — §03's only legal way to leave a note in the world.</summary>
        ChalkMark = 37,

        /// <summary>rope_deploy.</summary>
        RopeDeploy = 38,

        /// <summary>muffler_equip — §08's 소음 억제기, which mutes the buyer's own steps.</summary>
        MufflerEquip = 39,

        /// <summary>ghost_rattle_* — §09's dead player making the only noise they still can.</summary>
        GhostRattle = 40,

        /// <summary>ghost_rattle_ready — the §09 cooldown is up.</summary>
        GhostRattleReady = 41,

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
        /// <summary>How many ids exist, including <see cref="AudioCueId.None"/>. Array size for a cue table.</summary>
        public const int Count = (int)AudioCueId.VoiceOutOfRange + 1;

        /// <summary>
        /// The bus a cue mixes on.
        /// <para>
        /// The split is §-driven rather than folder-driven: a sound is on
        /// <see cref="AudioBus.Interface"/> exactly when it describes something that
        /// happened <em>to the player</em> rather than something that happened
        /// <em>in the room</em>. A clue read completing is a fact about a memory, and a
        /// door opening is a fact about a door — one of them must not be occluded or
        /// panned, and the other must.
        /// </para>
        /// </summary>
        public static AudioBus BusOf(AudioCueId cue)
        {
            switch (cue)
            {
                case AudioCueId.ClueReadSuccess:
                case AudioCueId.ClueReadFailed:
                case AudioCueId.ObjectiveFound:
                case AudioCueId.ShopOpen:
                case AudioCueId.ShopClose:
                case AudioCueId.ShopPurchase:
                case AudioCueId.ShopDenied:
                case AudioCueId.Descend:
                case AudioCueId.SurfaceReached:
                case AudioCueId.EscapeSuccess:
                case AudioCueId.MatchFailure:
                case AudioCueId.DeathTransition:
                case AudioCueId.LootSell:
                case AudioCueId.GhostRattleReady:
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
        /// The two levels the design states outright are read from
        /// <c>GameConstants</c>; the door comes from <see cref="AudioTuning"/>, which
        /// records why §04's unnumbered actions were given the levels they were.
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

                case AudioCueId.FlareIgnite:
                    return Core.GameConstants.FlareIgniteNoiseLevel;

                case AudioCueId.NoiseTrapTrigger:
                    return Core.GameConstants.EngineerTrapNoiseLevel;

                case AudioCueId.BarricadeBreak:
                case AudioCueId.SafeOpen:
                    // Loud, and louder than a door: §12 puts the 금고 and the barricade
                    // at the map's bottlenecks, where being heard costs the most.
                    return Core.GameConstants.FlareIgniteNoiseLevel;

                default:
                    return 0f;
            }
        }
    }
}
