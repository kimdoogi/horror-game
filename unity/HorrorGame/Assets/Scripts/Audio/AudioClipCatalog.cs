#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core.Map;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// The shipped clip set's naming convention, as code — the one place that knows
    /// <c>step_gravel_monster_step_03.wav</c> is 자갈, the monster, and variant three.
    /// <para>
    /// <b>Why the filenames are the authority.</b> The 166 WAVs are generated
    /// (<c>tools/audio</c>) and verified (<c>verify_audio.py</c>) against exactly these
    /// names, and §12 makes the surface a rules-grade fact rather than a dressing
    /// choice. Deriving the wiring from the names means a regenerated clip set is
    /// re-wired by re-running the step, and a renamed clip fails loudly in
    /// <see cref="MatchAudioLibrary.Validate"/> instead of silently unhooking one of
    /// §04's five letters.
    /// </para>
    /// <para>
    /// <b>It takes a loader rather than calling <c>AssetDatabase</c>.</b> The editor
    /// wiring step and the PlayMode test have to build the same libraries from the same
    /// rules; if this file imported <c>UnityEditor</c> the test could only ever check a
    /// second implementation. The delegate is the seam (ARCHITECTURE §3).
    /// </para>
    /// <para>
    /// <b>Standstill has no entry.</b> §06 gives 정지 a 소리 column of 없음 and the
    /// generator left the slot deliberately empty. There is no name here to load,
    /// because there is no file, because there is no sound.
    /// </para>
    /// </summary>
    public static class AudioClipCatalog
    {
        /// <summary>Where the shipped clips live, relative to the project.</summary>
        public const string Root = "Assets/Audio";

        /// <summary>How many variants the generator ships per footstep set. ASSETS.md §2.1.</summary>
        public const int FootstepVariants = 4;

        /// <summary>
        /// §12's floor table, as the filename token each surface uses.
        /// <code>
        ///   A 나무   wood       B 타일 tile       C 자갈 gravel
        ///   D 콘크리트 concrete  계단 금속 metal
        /// </code>
        /// </summary>
        private static readonly (FloorMaterial Material, string Token, string ZoneBed)[] SurfaceTable =
        {
            (FloorMaterial.Wood, "wood", "amb_zone_a_wood_loop"),
            (FloorMaterial.Tile, "tile", "amb_zone_b_tile_loop"),
            (FloorMaterial.Gravel, "gravel", "amb_zone_c_gravel_loop"),
            (FloorMaterial.Concrete, "concrete", "amb_zone_d_concrete_loop"),

            // §12 gives stairs their own material because moving between floors is a
            // designed act, and the stairwell belongs to no zone — hence its own bed.
            (FloorMaterial.Metal, "metal", "amb_stairwell_metal_loop"),

            // The three deep storeys. Without these rows TokenOf returns empty, which
            // makes FootstepStem return empty, which makes every clip on three whole
            // floors silently fail to load — the player walks B6, B7 and B8 in silence
            // and nothing anywhere reports a problem. That is the failure mode this
            // table has: it is a lookup that degrades to nothing instead of to an error.
            (FloorMaterial.Carpet, "carpet", "amb_zone_e_carpet_loop"),
            (FloorMaterial.Water, "water", "amb_zone_f_water_loop"),
            (FloorMaterial.Earth, "earth", "amb_zone_g_earth_loop"),
        };

        /// <summary>§07's five rows, as bed filenames in escalation order.</summary>
        private static readonly string[] TensionBeds =
        {
            "amb_tension_t1_dusk_loop",
            "amb_tension_t2_night_loop",
            "amb_tension_t3_deepnight_loop",
            "amb_tension_t4_dawn_loop",
            "amb_tension_t5_predawn_loop",
        };

        /// <summary>
        /// §07's tier stingers, indexed by tier.
        /// <para>
        /// Tier 0 (초저녁) has none and needs none: it is where the match starts, and a
        /// stinger announces a change. The remaining four line up one-to-one with §07's
        /// four transitions, which is why the shipped set has four files and not five.
        /// </para>
        /// </summary>
        private static readonly string?[] TierStingers =
        {
            null,
            "threat_night",
            "threat_late_night",
            "threat_pre_dawn",
            "threat_before_sunrise",
        };

        /// <summary>Every one-shot, as the cue it answers and the file stems it draws from.</summary>
        private static readonly (AudioCueId Cue, string Folder, string[] Stems)[] CueTable =
        {
            // §03's one held object. The torch is free now; what it costs is being seen.
            (AudioCueId.FlashlightOn, "Items", new[] { "flashlight_on_01", "flashlight_on_02" }),
            (AudioCueId.FlashlightOff, "Items", new[] { "flashlight_off_01", "flashlight_off_02" }),

            // §12-B. The race's only interaction with the building.
            (AudioCueId.DoorOpen, "Items", new[] { "door_open_01", "door_open_02" }),
            (AudioCueId.DoorClose, "Items", new[] { "door_close_01", "door_close_02" }),
            (AudioCueId.DoorLock, "Items", new[] { "door_lock_01", "door_lock_02" }),

            // 투하구, eight times a race — the whole of 층 이동 (§01).
            (AudioCueId.Descend, "UI", new[] { "descend_basement" }),

            // §02's two verdicts and the transition into §09.
            (AudioCueId.EscapeSuccess, "UI", new[] { "escape_success" }),
            (AudioCueId.MatchFailure, "UI", new[] { "match_failure_wipe" }),
            // §06's catch. One sting, not two variants: it fires several times in a race
            // and always means the same thing, so variation is the wrong economy.
            // tools/audio/gen_caught.py writes it and asserts it lands inside
            // CaughtScreen's 0.20 s blackout. death_transition_01/02 were §09's transition
            // into the ghost, and the ghost is deleted.
            (AudioCueId.Caught, "UI", new[] { "caught_sent_home" }),

            // §13 근접 음성.
            (AudioCueId.VoiceActivity, "UI", new[] { "voice_activity_blip" }),
            (AudioCueId.VoiceOutOfRange, "UI", new[] { "voice_out_of_range" }),

            // DELETED — 32 rows for systems that no longer exist. Battery ×3 (§08's
            // cell), Barricade ×2 / NoiseTrap ×2 / BreakerThrow / ChalkMark /
            // RopeDeploy / MufflerEquip / DetectorPing (§04 직업), Loot ×5 and Safe ×2
            // and Shop ×4 (§08 경제), Clue ×2 and Objective ×2 (§03), Flare ×2,
            // SurfaceReached (지상), GhostRattle ×2 (§09's 신호). Their .wav files are
            // listed in the same commit's report as orphans; AudioCueId keeps the
            // numbering sparse on purpose so the surviving rows do not silently
            // re-point at the wrong clip in MatchAudioLibrary.asset.
        };

        /// <summary>Loads a clip from a project-relative asset path, or null.</summary>
        public delegate AudioClip? ClipLoader(string assetPath);

        /// <summary>
        /// What a population run found.
        /// </summary>
        public readonly struct Report
        {
            /// <summary>Builds a report.</summary>
            public Report(int loaded, IReadOnlyList<string> missing)
            {
                Loaded = loaded;
                Missing = missing;
            }

            /// <summary>How many clips were resolved.</summary>
            public int Loaded { get; }

            /// <summary>Asset paths the loader could not answer for. Empty on a clean run.</summary>
            public IReadOnlyList<string> Missing { get; }

            /// <summary>True when every expected file was found.</summary>
            public bool Complete => Missing.Count == 0;

            /// <summary>One line for a log.</summary>
            public string Summary =>
                Loaded + " clips wired, " + Missing.Count + " missing"
                + (Missing.Count > 0 ? " (first: " + Missing[0] + ")" : string.Empty);
        }

        /// <summary>
        /// Fills both libraries from the shipped clip set.
        /// <para>
        /// Idempotent — it replaces the tables rather than appending to them, so
        /// re-running the wiring step after regenerating the audio does not accumulate
        /// stale entries.
        /// </para>
        /// </summary>
        /// <param name="surfaces">§12's five floors. Rebuilt in full.</param>
        /// <param name="library">Everything else. Rebuilt in full.</param>
        /// <param name="load">Resolves a project-relative <c>.wav</c> path to a clip.</param>
        public static Report Populate(SurfaceAudioLibrary surfaces, MatchAudioLibrary library, ClipLoader load)
        {
            if (surfaces == null)
            {
                throw new ArgumentNullException(nameof(surfaces));
            }

            if (library == null)
            {
                throw new ArgumentNullException(nameof(library));
            }

            if (load == null)
            {
                throw new ArgumentNullException(nameof(load));
            }

            var missing = new List<string>();
            var loaded = 0;

            PopulateSurfaces(surfaces, load, missing, ref loaded);
            library.Surfaces = surfaces;

            PopulateLibrary(library, load, missing, ref loaded);

            return new Report(loaded, missing);
        }

        /// <summary>The filename token §12's surface uses. Empty for <see cref="FloorMaterial.None"/>.</summary>
        public static string TokenOf(FloorMaterial material)
        {
            for (var i = 0; i < SurfaceTable.Length; i++)
            {
                if (SurfaceTable[i].Material == material)
                {
                    return SurfaceTable[i].Token;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// The clip filename one footstep should have — the naming rule as a function,
        /// so a test can assert the mix picked the right surface without knowing how
        /// the wiring stores it.
        /// </summary>
        /// <param name="material">§12's surface.</param>
        /// <param name="actor">Who is walking. §04 reads the monster's, not the player's.</param>
        /// <param name="variant">1-based, 1..<see cref="FootstepVariants"/>.</param>
        public static string FootstepStem(FloorMaterial material, FootstepActor actor, int variant)
        {
            var token = TokenOf(material);
            if (token.Length == 0)
            {
                return string.Empty;
            }

            return "step_" + token + "_" + ActorToken(actor) + "_" + variant.ToString("00");
        }

        private static string ActorToken(FootstepActor actor)
        {
            switch (actor)
            {
                case FootstepActor.MonsterStep:
                    return "monster_step";
                case FootstepActor.PlayerRun:
                    return "player_run";
                default:
                    return "player_walk";
            }
        }

        private static void PopulateSurfaces(
            SurfaceAudioLibrary surfaces, ClipLoader load, List<string> missing, ref int loaded)
        {
            var entries = new SurfaceAudioLibrary.SurfaceEntry[SurfaceTable.Length];

            for (var i = 0; i < SurfaceTable.Length; i++)
            {
                var row = SurfaceTable[i];
                var entry = new SurfaceAudioLibrary.SurfaceEntry { material = row.Material };

                entry.playerWalk.SetClips(LoadFootsteps(row.Material, FootstepActor.PlayerWalk, load, missing, ref loaded));
                entry.playerRun.SetClips(LoadFootsteps(row.Material, FootstepActor.PlayerRun, load, missing, ref loaded));
                entry.monsterStep.SetClips(LoadFootsteps(row.Material, FootstepActor.MonsterStep, load, missing, ref loaded));
                entry.ambienceBed = LoadOne("Ambience", row.ZoneBed, load, missing, ref loaded);

                entries[i] = entry;
            }

            surfaces.SetSurfaces(entries);
        }

        private static AudioClip?[] LoadFootsteps(
            FloorMaterial material, FootstepActor actor, ClipLoader load, List<string> missing, ref int loaded)
        {
            var clips = new AudioClip?[FootstepVariants];
            for (var variant = 1; variant <= FootstepVariants; variant++)
            {
                clips[variant - 1] = LoadOne(
                    "Footsteps", FootstepStem(material, actor, variant), load, missing, ref loaded);
            }

            return Compact(clips);
        }

        private static void PopulateLibrary(
            MatchAudioLibrary library, ClipLoader load, List<string> missing, ref int loaded)
        {
            var beds = new AudioClip?[Core.GameConstants.ThreatTierCount];
            var stingers = new AudioClip?[Core.GameConstants.ThreatTierCount];

            for (var i = 0; i < Core.GameConstants.ThreatTierCount; i++)
            {
                beds[i] = i < TensionBeds.Length
                    ? LoadOne("Ambience", TensionBeds[i], load, missing, ref loaded)
                    : null;

                var stinger = i < TierStingers.Length ? TierStingers[i] : null;
                stingers[i] = stinger != null
                    ? LoadOne("UI", stinger, load, missing, ref loaded)
                    : null;
            }

            library.SetThreatBeds(beds, stingers);

            // DELETED: library.SetBeds(...). It loaded amb_surface_vehicle_loop,
            // amb_generator_hum_loop and zone_hum_loop — §01's 지상, §03's 발전기 and
            // §12's 전기 패널. All three clips are gone from Assets/Audio with the
            // systems that placed them.

            library.SetHeartbeat(
                LoadOne("UI", "heartbeat_low", load, missing, ref loaded),
                LoadOne("UI", "heartbeat_mid", load, missing, ref loaded),
                LoadOne("UI", "heartbeat_high", load, missing, ref loaded));

            var incidental = new List<AudioClip?>();
            for (var i = 1; i <= 5; i++)
            {
                incidental.Add(LoadOne("Ambience", "sfx_creak_distant_" + i.ToString("00"), load, missing, ref loaded));
            }

            for (var i = 1; i <= 4; i++)
            {
                incidental.Add(LoadOne("Ambience", "sfx_water_drip_" + i.ToString("00"), load, missing, ref loaded));
            }

            library.Incidental.SetClips(Compact(incidental.ToArray()));

            library.SetMonster(
                LoadOne("Monster", "monster_presence_bed", load, missing, ref loaded),
                LoadMany("Monster", new[] { "monster_breath_loop_01", "monster_breath_loop_02" }, load, missing, ref loaded),
                LoadMany("Monster", new[] { "monster_growl_01", "monster_growl_02", "monster_growl_03" }, load, missing, ref loaded),
                LoadMany("Monster", new[] { "monster_roar_01", "monster_roar_02", "monster_roar_03" }, load, missing, ref loaded),
                LoadMany("Monster", new[] { "monster_search_01", "monster_search_02" }, load, missing, ref loaded),
                LoadMany("Monster", new[] { "monster_stun_01", "monster_stun_02" }, load, missing, ref loaded),
                LoadMany("Monster", new[] { "monster_grab_01", "monster_grab_02" }, load, missing, ref loaded));

            var cues = new List<MatchAudioLibrary.CueEntry>(CueTable.Length);
            for (var i = 0; i < CueTable.Length; i++)
            {
                var row = CueTable[i];
                var entry = new MatchAudioLibrary.CueEntry { cue = row.Cue };
                entry.clips.SetClips(LoadMany(row.Folder, row.Stems, load, missing, ref loaded));
                cues.Add(entry);
            }

            library.SetCues(cues.ToArray());
        }

        private static AudioClip?[] LoadMany(
            string folder, string[] stems, ClipLoader load, List<string> missing, ref int loaded)
        {
            var clips = new AudioClip?[stems.Length];
            for (var i = 0; i < stems.Length; i++)
            {
                clips[i] = LoadOne(folder, stems[i], load, missing, ref loaded);
            }

            return Compact(clips);
        }

        private static AudioClip? LoadOne(
            string folder, string stem, ClipLoader load, List<string> missing, ref int loaded)
        {
            var path = Root + "/" + folder + "/" + stem + ".wav";
            var clip = load(path);

            if (clip == null)
            {
                missing.Add(path);
                return null;
            }

            loaded++;
            return clip;
        }

        /// <summary>
        /// Drops the nulls. A bank with a hole in it would hand
        /// <see cref="AudioClipBank.Next"/> a null every few picks, which sounds exactly
        /// like a dropped footstep — and §04 reads dropped footsteps as information.
        /// </summary>
        private static AudioClip?[] Compact(AudioClip?[] clips)
        {
            var kept = 0;
            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    kept++;
                }
            }

            if (kept == clips.Length)
            {
                return clips;
            }

            var result = new AudioClip?[kept];
            var next = 0;
            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    result[next++] = clips[i];
                }
            }

            return result;
        }
    }
}
