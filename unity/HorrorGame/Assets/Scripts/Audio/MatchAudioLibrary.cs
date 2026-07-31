#nullable enable

using System;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// Everything a match sounds like that is not a footstep: §07's five tension beds,
    /// the heartbeat layers, §12's incidental creaks, the monster's own family, and
    /// every one-shot in <see cref="AudioCueId"/>.
    /// <para>
    /// <b>Why one asset instead of clips on components.</b> The 166 shipped WAVs have
    /// to reach a scene, and a scene is a serialised file nobody can review. Putting the
    /// clip set in an asset means the wiring is a build step that can be re-run, diffed
    /// and validated (<see cref="Validate"/>) instead of a hundred inspector drags that
    /// silently rot the next time the scene is regenerated — which
    /// <c>SoloPlaytest.BuildScene</c> does from scratch on every invocation.
    /// </para>
    /// <para>
    /// <b>There is no Standstill slot and there must never be one.</b> §06 gives 정지 a
    /// 소리 column of 없음 and spends a call-out box on why: the monster stops, the
    /// 청음사 loses it, and "침묵이 가장 무서운 소리다". The absence is enforced twice
    /// over — this asset has no field to put such a clip in, and
    /// <see cref="MonsterAudio.IsAudible"/> silences the whole component in that state.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "HorrorGame/Audio/Match Audio Library", fileName = "MatchAudioLibrary")]
    public sealed class MatchAudioLibrary : ScriptableObject
    {
        /// <summary>
        /// <see cref="Resources"/> name the wiring step writes this asset under.
        /// <para>
        /// A <c>Resources</c> folder rather than a scene reference, because the scene is
        /// not a reliable place to keep it: <c>SoloPlaytest.BuildScene</c> rewrites the
        /// solo scene from the generated map every time it runs, and anything that then
        /// forgets to re-wire produces a game that is silent and gives no error. Loading
        /// by name means the mix can find its clips with no help from whoever built the
        /// scene, and <c>Resources</c> is always in the build.
        /// </para>
        /// </summary>
        public const string ResourceName = "MatchAudioLibrary";

        /// <summary>Loads the wired library, or null when the wiring step has never been run.</summary>
        public static MatchAudioLibrary? LoadDefault()
        {
            return Resources.Load<MatchAudioLibrary>(ResourceName);
        }

        /// <summary>One <see cref="AudioCueId"/> and the variants it plays.</summary>
        [Serializable]
        public sealed class CueEntry
        {
            [Tooltip("Which moment this set answers for.")]
            public AudioCueId cue = AudioCueId.None;

            [Tooltip("The variants. One is fine; the generator ships two or three for most.")]
            public AudioClipBank clips = new AudioClipBank();
        }

        [Header("§12 — the floor alphabet")]
        [Tooltip("The five surfaces as footsteps and room tones. §04's only input.")]
        [SerializeField]
        private SurfaceAudioLibrary? surfaces;

        [Header("§07 — the night, as five beds")]
        [Tooltip("amb_tension_t1..t5 in §07's escalation order: 초저녁 · 밤 · 심야 · 새벽 · 동트기 전.")]
        [SerializeField]
        private AudioClip?[] tensionBeds = new AudioClip?[Core.GameConstants.ThreatTierCount];

        [Tooltip("UI/threat_*.wav. Only played when §07 says the team may know the hour.")]
        [SerializeField]
        private AudioClip?[] tierStingers = new AudioClip?[Core.GameConstants.ThreatTierCount];

        [Header("§03 — above ground")]
        [Tooltip("amb_surface_vehicle_loop. The safe half of §03's round trip must not sound like the basement.")]
        [SerializeField]
        private AudioClip? surfaceBed;

        [Tooltip("sfx_creak_distant_* and sfx_water_drip_*. §06's silence only frightens in a room that is otherwise alive.")]
        [SerializeField]
        private AudioClipBank incidental = new AudioClipBank();

        [Header("Landmarks — loops pinned to a place, not to the player")]
        [Tooltip("amb_generator_hum_loop. §03 puts a 발전기 at the entrance; it is the " +
                 "loudest thing on the surface and the audible edge of the safe half.")]
        [SerializeField]
        private AudioClip? generatorLoop;

        [Tooltip("zone_hum_loop. §12 gives every zone an electrical panel for 정비공; a " +
                 "hum at each one is a landmark a player can navigate by in the dark.")]
        [SerializeField]
        private AudioClip? zoneHumLoop;

        [Tooltip("flare_burn_loop. Played by whatever spawns §08's 조명탄 — this layer " +
                 "holds the clip so the prop does not have to know about the mix.")]
        [SerializeField]
        private AudioClip? flareBurnLoop;

        [Header("Heartbeat — the body, not the monster")]
        [SerializeField] private AudioClip? heartbeatLow;
        [SerializeField] private AudioClip? heartbeatMid;
        [SerializeField] private AudioClip? heartbeatHigh;

        [Header("§06 — the monster")]
        [SerializeField] private AudioClip? monsterPresenceBed;
        [SerializeField] private AudioClipBank monsterBreath = new AudioClipBank();
        [SerializeField] private AudioClipBank monsterGrowls = new AudioClipBank();
        [SerializeField] private AudioClipBank monsterRoars = new AudioClipBank();
        [SerializeField] private AudioClipBank monsterSearches = new AudioClipBank();
        [SerializeField] private AudioClipBank monsterStuns = new AudioClipBank();
        [SerializeField] private AudioClipBank monsterGrabs = new AudioClipBank();

        [Header("One-shots")]
        [SerializeField]
        private CueEntry[] cues = Array.Empty<CueEntry>();

        [NonSerialized]
        private AudioClipBank?[]? _cueIndex;

        /// <summary>§12's five surfaces. Null until the wiring step has run.</summary>
        public SurfaceAudioLibrary? Surfaces
        {
            get { return surfaces; }
            set { surfaces = value; }
        }

        /// <summary>§07's five tension beds, in escalation order.</summary>
        public AudioClip?[] TensionBeds => tensionBeds;

        /// <summary>§07's tier stingers. Gated on <c>MatchClock.IsTimeReadable</c> by the director.</summary>
        public AudioClip?[] TierStingers => tierStingers;

        /// <summary>The above-ground bed. §03.</summary>
        public AudioClip? SurfaceBed => surfaceBed;

        /// <summary>§12's scattered creaks and drips.</summary>
        public AudioClipBank Incidental => incidental;

        /// <summary>§03's 지상 발전기, as a loop pinned to the entrance.</summary>
        public AudioClip? GeneratorLoop => generatorLoop;

        /// <summary>§12's per-zone electrical panel, as a loop pinned to the zone.</summary>
        public AudioClip? ZoneHumLoop => zoneHumLoop;

        /// <summary>§08's 조명탄 while it burns. Held here for whoever spawns the prop.</summary>
        public AudioClip? FlareBurnLoop => flareBurnLoop;

        /// <summary>Quietest heartbeat layer.</summary>
        public AudioClip? HeartbeatLow => heartbeatLow;

        /// <summary>Middle heartbeat layer.</summary>
        public AudioClip? HeartbeatMid => heartbeatMid;

        /// <summary>Loudest heartbeat layer — 추격, or the monster in the same room.</summary>
        public AudioClip? HeartbeatHigh => heartbeatHigh;

        /// <summary>§06's presence bed.</summary>
        public AudioClip? MonsterPresenceBed => monsterPresenceBed;

        /// <summary>§06's close-range breath.</summary>
        public AudioClipBank MonsterBreath => monsterBreath;

        /// <summary>§06's 경계 growl.</summary>
        public AudioClipBank MonsterGrowls => monsterGrowls;

        /// <summary>§06's 추격 roar — the 소리 column reads 발소리+포효.</summary>
        public AudioClipBank MonsterRoars => monsterRoars;

        /// <summary>§06's 수색 vocalisation.</summary>
        public AudioClipBank MonsterSearches => monsterSearches;

        /// <summary>§04's 섬광수 landing a stun.</summary>
        public AudioClipBank MonsterStuns => monsterStuns;

        /// <summary>The kill. §06 has no state for it, so it is an event.</summary>
        public AudioClipBank MonsterGrabs => monsterGrabs;

        /// <summary>Every authored one-shot set. For tooling and the wiring step.</summary>
        public CueEntry[] Cues => cues;

        /// <summary>Replaces the one-shot table. Used by the wiring step, which builds it from disk.</summary>
        public void SetCues(CueEntry[] value)
        {
            cues = value ?? Array.Empty<CueEntry>();
            _cueIndex = null;
        }

        /// <summary>
        /// Replaces §07's beds and stingers. Both arrays are resized to
        /// <c>GameConstants.ThreatTierCount</c> whatever is passed — the table in §07 is
        /// the design, and a sixth bed would be a night nobody wrote.
        /// </summary>
        public void SetThreatBeds(AudioClip?[]? beds, AudioClip?[]? stingers)
        {
            tensionBeds = Resize(beds, Core.GameConstants.ThreatTierCount);
            tierStingers = Resize(stingers, Core.GameConstants.ThreatTierCount);
        }

        /// <summary>Replaces the loops that are not tied to §12's surfaces.</summary>
        public void SetBeds(AudioClip? surface, AudioClip? generator, AudioClip? zoneHum, AudioClip? flareBurn)
        {
            surfaceBed = surface;
            generatorLoop = generator;
            zoneHumLoop = zoneHum;
            flareBurnLoop = flareBurn;
        }

        /// <summary>Replaces the three heartbeat layers.</summary>
        public void SetHeartbeat(AudioClip? lowLayer, AudioClip? midLayer, AudioClip? highLayer)
        {
            heartbeatLow = lowLayer;
            heartbeatMid = midLayer;
            heartbeatHigh = highLayer;
        }

        /// <summary>
        /// Replaces §06's family. Note the absent parameter: there is no 정지 argument,
        /// because there is no 정지 sound — §06's 소리 column reads 없음 and the silence
        /// is the state's whole purpose.
        /// </summary>
        public void SetMonster(
            AudioClip? presence,
            AudioClip?[] breathClips,
            AudioClip?[] growlClips,
            AudioClip?[] roarClips,
            AudioClip?[] searchClips,
            AudioClip?[] stunClips,
            AudioClip?[] grabClips)
        {
            monsterPresenceBed = presence;
            monsterBreath.SetClips(breathClips);
            monsterGrowls.SetClips(growlClips);
            monsterRoars.SetClips(roarClips);
            monsterSearches.SetClips(searchClips);
            monsterStuns.SetClips(stunClips);
            monsterGrabs.SetClips(grabClips);
        }

        private static AudioClip?[] Resize(AudioClip?[]? source, int length)
        {
            var result = new AudioClip?[length];
            if (source == null)
            {
                return result;
            }

            for (var i = 0; i < length && i < source.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        /// <summary>
        /// The variants for a cue, or null when it was never authored.
        /// <para>
        /// Null rather than a substitute, for the same reason
        /// <see cref="SurfaceAudioLibrary.For"/> returns null: a missing sound is
        /// obvious in a playtest and a wrong one is not.
        /// </para>
        /// </summary>
        public AudioClipBank? For(AudioCueId cue)
        {
            var index = BuildIndex();
            var slot = (int)cue;
            return slot >= 0 && slot < index.Length ? index[slot] : null;
        }

        /// <summary>
        /// Everything that is missing, as one human-readable line per gap, plus the
        /// count of clips that did arrive.
        /// <para>
        /// Reported rather than thrown. The mix has to come up on a partial clip set —
        /// half a library is a playtest, no library is a silent build — and §12 already
        /// fails a map whose floors are unauthored, so this is the audio-side twin of
        /// that check rather than a second gate on the same thing.
        /// </para>
        /// </summary>
        public string Validate(out int clipCount, out int gapCount)
        {
            var report = new System.Text.StringBuilder();
            var clips = 0;
            var gaps = 0;

            CheckBed(surfaceBed, "amb_surface_vehicle_loop (§03's surface)", report, ref clips, ref gaps);
            CheckBed(heartbeatLow, "heartbeat_low", report, ref clips, ref gaps);
            CheckBed(heartbeatMid, "heartbeat_mid", report, ref clips, ref gaps);
            CheckBed(heartbeatHigh, "heartbeat_high", report, ref clips, ref gaps);
            CheckBed(monsterPresenceBed, "monster_presence_bed", report, ref clips, ref gaps);
            CheckBed(generatorLoop, "amb_generator_hum_loop (§03's 발전기)", report, ref clips, ref gaps);
            CheckBed(zoneHumLoop, "zone_hum_loop (§12's 전기 패널)", report, ref clips, ref gaps);
            CheckBed(flareBurnLoop, "flare_burn_loop (§08's 조명탄)", report, ref clips, ref gaps);

            for (var i = 0; i < Core.GameConstants.ThreatTierCount; i++)
            {
                CheckBed(
                    i < tensionBeds.Length ? tensionBeds[i] : null,
                    "amb_tension_t" + (i + 1) + " (§07 tier " + (i + 1) + ")",
                    report, ref clips, ref gaps);
            }

            CheckBank(incidental, "§12 incidental creaks and drips", report, ref clips, ref gaps);
            CheckBank(monsterBreath, "monster_breath_loop", report, ref clips, ref gaps);
            CheckBank(monsterGrowls, "monster_growl (§06 경계)", report, ref clips, ref gaps);
            CheckBank(monsterRoars, "monster_roar (§06 추격)", report, ref clips, ref gaps);
            CheckBank(monsterSearches, "monster_search (§06 수색)", report, ref clips, ref gaps);
            CheckBank(monsterStuns, "monster_stun", report, ref clips, ref gaps);
            CheckBank(monsterGrabs, "monster_grab", report, ref clips, ref gaps);

            for (var id = 1; id < AudioCues.Count; id++)
            {
                var bank = For((AudioCueId)id);
                if (bank == null || bank.IsEmpty)
                {
                    gaps++;
                    report.AppendLine("  missing cue: " + (AudioCueId)id);
                    continue;
                }

                clips += bank.Count;
            }

            if (surfaces == null)
            {
                gaps++;
                report.AppendLine("  missing SurfaceAudioLibrary — §12's floor alphabet, and §04 has no other input.");
            }
            else
            {
                for (var i = 0; i < surfaces.Surfaces.Length; i++)
                {
                    var entry = surfaces.Surfaces[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    CheckBank(entry.playerWalk, entry.material + " player walk", report, ref clips, ref gaps);
                    CheckBank(entry.playerRun, entry.material + " player run", report, ref clips, ref gaps);
                    CheckBank(entry.monsterStep, entry.material + " monster step", report, ref clips, ref gaps);
                    CheckBed(entry.ambienceBed, entry.material + " zone bed", report, ref clips, ref gaps);
                }
            }

            clipCount = clips;
            gapCount = gaps;
            return report.ToString();
        }

        private AudioClipBank?[] BuildIndex()
        {
            var index = _cueIndex;
            if (index != null && index.Length == AudioCues.Count)
            {
                return index;
            }

            index = new AudioClipBank?[AudioCues.Count];
            for (var i = 0; i < cues.Length; i++)
            {
                var entry = cues[i];
                if (entry == null || entry.cue == AudioCueId.None)
                {
                    continue;
                }

                var slot = (int)entry.cue;
                if (slot > 0 && slot < index.Length)
                {
                    index[slot] = entry.clips;
                }
            }

            _cueIndex = index;
            return index;
        }

        private static void CheckBed(
            AudioClip? clip, string label, System.Text.StringBuilder report, ref int clips, ref int gaps)
        {
            if (clip == null)
            {
                gaps++;
                report.AppendLine("  missing: " + label);
                return;
            }

            clips++;
        }

        private static void CheckBank(
            AudioClipBank? bank, string label, System.Text.StringBuilder report, ref int clips, ref int gaps)
        {
            if (bank == null || bank.IsEmpty)
            {
                gaps++;
                report.AppendLine("  missing: " + label);
                return;
            }

            clips += bank.Count;
        }
    }
}
