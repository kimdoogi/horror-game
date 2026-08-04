using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// What a clip is <em>for</em>, which is what decides its import settings.
    /// <para>
    /// Load type and compression follow from length, but the one setting that can end a
    /// role — mono versus stereo — follows from role alone, so role is the primary key.
    /// </para>
    /// </summary>
    public enum AudioRole
    {
        /// <summary>
        /// §12's floor-material alphabet. The Listener reads the monster's 위치 · 거리 ·
        /// 이동 방향 out of these five spectral identities (§04), so this family is a
        /// gameplay channel and gets the most protective settings in the project.
        /// </summary>
        FootstepChannel,

        /// <summary>
        /// §06's growl / roar / search / grab / stun one-shots. The growl-to-roar centroid
        /// ratio is a designed read (≥1.4×, ASSETS.md §2.2), so these are treated as
        /// information rather than as decoration.
        /// </summary>
        MonsterCue,

        /// <summary>
        /// Diegetic world one-shot: item interactions, water drips, distant creaks, and
        /// §09's four ghost rattles. Mono and short.
        /// </summary>
        PositionalOneShot,

        /// <summary>
        /// A diegetic bed that loops and is crossfaded by distance — the monster presence
        /// bed and breath loops (§06) and the surface generator hum (§03).
        /// </summary>
        PositionalBed,

        /// <summary>
        /// §07's five threat tiers, §12's four zone beds, the stairwell and the surface
        /// vehicle. Stereo on purpose: 2D beds are the only place the stereo image
        /// §05's mandatory headphones exist for is free to be used.
        /// </summary>
        AmbienceBed,

        /// <summary>
        /// Non-diegetic interface audio, including the two shop clips that live in the
        /// Items folder for authoring reasons but are not in the world.
        /// </summary>
        InterfaceCue,
    }

    /// <summary>Which model policy a file falls under. Keyed off folder, then filename.</summary>
    public enum ModelCategory
    {
        /// <summary>Not under a managed folder — the post-processor leaves it alone.</summary>
        Unmanaged,

        /// <summary>
        /// A rig that must retarget. §05: "1인칭이어도 캐릭터 모델이 필요하다. 협동
        /// 게임에서는 다른 3명이 보여야 한다." Four players share one clip set, so the
        /// player rig has to be Humanoid.
        /// </summary>
        CharacterHumanoid,

        /// <summary>
        /// A rig that ships its own clips and must <em>not</em> retarget, because parts of
        /// it have no humanoid equivalent and a Humanoid avatar drops them silently.
        /// </summary>
        CharacterGeneric,

        /// <summary>§12's kit. Every dimension is a rule, so precision beats file size.</summary>
        MapKit,

        /// <summary>Scene dressing, loot and interactables. The bulk of the object count.</summary>
        Prop,
    }

    /// <summary>The resolved audio import decision for one clip.</summary>
    public readonly struct AudioImportRule
    {
        public AudioImportRule(AudioRole role, bool positional, bool forceToMono, bool loops,
            AudioClipLoadType loadType, AudioCompressionFormat compression, float quality,
            bool preload, bool loadInBackground)
        {
            Role = role;
            Positional = positional;
            ForceToMono = forceToMono;
            Loops = loops;
            LoadType = loadType;
            Compression = compression;
            Quality = quality;
            Preload = preload;
            LoadInBackground = loadInBackground;
        }

        public AudioRole Role { get; }

        /// <summary>
        /// True when the clip carries positional information a player is meant to act on.
        /// The importer cannot set spatial blend — that lives on <c>AudioSource</c> — so
        /// this flag is the seam: whatever builds the prefab reads it and sets
        /// <c>spatialBlend = 1</c>. See <see cref="AssetImportPolicy"/> remarks.
        /// </summary>
        public bool Positional { get; }

        public bool ForceToMono { get; }

        /// <summary>
        /// Whether the clip is meant to play as a loop. Unity has no loop flag on
        /// <c>AudioImporter</c>, so this is recorded in the importer's user data and read
        /// back when an <c>AudioSource</c> is authored.
        /// </summary>
        public bool Loops { get; }

        public AudioClipLoadType LoadType { get; }
        public AudioCompressionFormat Compression { get; }

        /// <summary>Vorbis quality, 0..1. Ignored when <see cref="Compression"/> is PCM.</summary>
        public float Quality { get; }

        public bool Preload { get; }
        public bool LoadInBackground { get; }
    }

    /// <summary>The resolved model import decision for one file.</summary>
    public readonly struct ModelImportRule
    {
        public ModelImportRule(ModelCategory category, bool importAnimation, bool generateLightmapUv,
            ModelImporterMeshCompression meshCompression, bool addCollider, bool convexCollider,
            float expectedTallestExtentMetres)
        {
            Category = category;
            ImportAnimation = importAnimation;
            GenerateLightmapUv = generateLightmapUv;
            MeshCompression = meshCompression;
            AddCollider = addCollider;
            ConvexCollider = convexCollider;
            ExpectedTallestExtentMetres = expectedTallestExtentMetres;
        }

        public ModelCategory Category { get; }
        public bool ImportAnimation { get; }
        public bool GenerateLightmapUv { get; }
        public ModelImporterMeshCompression MeshCompression { get; }
        public bool AddCollider { get; }
        public bool ConvexCollider { get; }

        /// <summary>
        /// The measured largest bounding extent in metres, or 0 when only the category
        /// band applies. Non-zero for the two characters, whose heights are the project's
        /// scale anchors.
        /// </summary>
        public float ExpectedTallestExtentMetres { get; }
    }

    /// <summary>
    /// The single place that decides how every generated asset imports, shared by the two
    /// post-processors and the validator so a check can never drift from the setting it
    /// checks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is code and not a click-through.</b> ASSETS.md §1 states the failure this
    /// file exists to prevent: Unity does not spatialise a stereo <c>AudioClip</c>. A
    /// two-channel clip on a 3D <c>AudioSource</c> plays at a fixed level with no distance
    /// attenuation and no panning. It does not error and it does not warn. That single
    /// wrong checkbox takes out §04's 청음사 (whose entire ability is reading the monster's
    /// 위치 · 거리 · 이동 방향 by ear), §13's proximity voice (which has no distance logic of
    /// its own — the 3D source *is* the logic), and §09's ghost, whose only channel is a
    /// rattle inside <c>GameConstants.GhostRattleRange</c> that the team must be able to
    /// turn toward. Three roles, one silent checkbox. Import settings are therefore part of
    /// the rules, and rules live in source control.
    /// </para>
    /// <para>
    /// <b>What the importer cannot do.</b> Two of the settings ASSETS.md §1 lists are not
    /// import settings at all. Checked against the shipped Unity 6000.3.21f1 assemblies rather
    /// than from memory: <c>UnityEngine.AudioClip</c> exposes <c>channels</c>, <c>frequency</c>,
    /// <c>length</c>, <c>samples</c>, <c>loadType</c>, <c>loadInBackground</c>,
    /// <c>preloadAudioData</c>, <c>ambisonic</c> and <c>loadState</c> — and no loop flag and no
    /// spatial blend. Both of those are <c>AudioSource.loop</c> and
    /// <c>AudioSource.spatialBlend</c>. <c>AudioImporter</c> does still carry public
    /// <c>loopable</c> and <c>threeD</c> properties, but they are Unity 4 vestiges with no
    /// counterpart on the runtime clip, so nothing can read what they write; they are
    /// deliberately not used, because a setting that looks like it satisfies the requirement
    /// and does not is worse than an obvious gap.
    /// </para>
    /// <para>
    /// So this class splits the job. What the importer owns — force-to-mono, load type,
    /// compression, sample rate, preload — is enforced by
    /// <c>AssetImportAudioPostprocessor</c>. What only a prefab can own is published two ways:
    /// as <see cref="ResolveAudio"/>, which prefab and scene authoring calls to get
    /// <c>Positional</c> and <c>Loops</c>, and as a key/value record written into the
    /// importer's <c>userData</c> so the intent is visible in the <c>.meta</c> file and shows
    /// up in a diff.
    /// </para>
    /// <para>
    /// <b>Opting out.</b> Two markers, both checked before anything is written:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// Per asset — the token <c>HG_MANUAL</c> anywhere in the asset's importer
    /// <c>userData</c>. Set it from <c>Horror/Assets/Mark Selection As Manual Import</c>,
    /// or by hand in the <c>.meta</c>. The post-processors then touch nothing on that
    /// asset and the validator reports it as excluded rather than as broken.
    /// </description></item>
    /// <item><description>
    /// Per folder — a file named <c>.hg-import-optout</c> in the folder or any ancestor
    /// up to <c>Assets/</c>. Unity ignores dot-prefixed files, so the marker costs no
    /// <c>.meta</c> of its own. Use it while iterating on a family by hand.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Numbers.</b> ARCHITECTURE.md §2 puts every tuned game number in
    /// <c>GameConstants</c>. The thresholds below are not game numbers — they are pipeline
    /// budgets, they change nothing a player can observe, and <c>GameConstants</c> lives in
    /// Core, which may not reference <c>UnityEngine</c>. They stay here. The numbers that
    /// <em>are</em> design numbers are read from <c>GameConstants</c> by the validator.
    /// </para>
    /// </remarks>
    public static class AssetImportPolicy
    {
        /// <summary>
        /// Bumped whenever a rule below changes. Unity reimports every asset whose
        /// post-processor version moved, which is the only way a policy change reaches
        /// assets that are already imported.
        /// <para>
        /// 2 — two import defects, both needing every governed asset reimported.
        /// The RIFF chunk walk in <see cref="AssetImportWavHeader"/> stopped at the
        /// <c>fmt </c> chunk and never reached <c>data</c>, so every clip imported on a
        /// fabricated fallback length and took the wrong load type and, for the transients,
        /// the wrong codec. And lightmap UV packing used Unity's <c>Calculate</c> margin,
        /// which emptied the two smallest props outright — see
        /// <see cref="RequiredLightmapMarginMethod"/>.
        /// </para>
        /// </summary>
        public const uint PolicyVersion = 2;

        /// <summary>Runs after third-party post-processors so this policy is the one that lands.</summary>
        public const int PostprocessOrder = 1000;

        /// <summary>Substring in importer <c>userData</c> that excludes an asset from the policy.</summary>
        public const string ManualOverrideToken = "HG_MANUAL";

        /// <summary>Dot-prefixed so Unity ignores it and it needs no <c>.meta</c> of its own.</summary>
        public const string FolderOptOutFileName = ".hg-import-optout";

        public const string AudioRoot = "Assets/Audio";
        public const string ModelRoot = "Assets/Models";

        /// <summary>
        /// Above this length a clip streams from disk instead of living in memory. No clip
        /// in the project reaches it — the longest is a 30 s ambience bed — so today the
        /// rule fires on nothing. It is here because the first 60 s menu track added later
        /// must not silently become a 20 MB resident decompression.
        /// </summary>
        public const float StreamingSeconds = 60f;

        /// <summary>
        /// Above this length a clip stays compressed in memory rather than decompressing
        /// on load. Chosen so §07's twelve 30 s ambience beds are compressed (≈0.5 MB each
        /// instead of ≈5.8 MB) while every cue a player reacts to — all 60 footsteps, all
        /// 13 monster one-shots, every item sound, all four ghost rattles — decompresses,
        /// because a decode on first play is a hitch and the first footstep is exactly the
        /// one that must not be late.
        /// </summary>
        public const float CompressedInMemorySeconds = 5f;

        /// <summary>
        /// Below this length Vorbis is skipped in favour of PCM. A clip this short is
        /// mostly transient, a Vorbis analysis window is a large fraction of it, and the
        /// storage saved is a few kilobytes. The 0.07 s flashlight click is the case.
        /// </summary>
        public const float PcmTransientSeconds = 0.30f;

        /// <summary>
        /// Vorbis quality for everything that is compressed, from ASSETS.md §1. ADPCM is
        /// not an option at any length: it smears the high band, and §12's gravel identity
        /// <em>is</em> a 6.2 kHz noise band.
        /// </summary>
        public const float VorbisQuality = 0.70f;

        /// <summary>
        /// The generators emit 48 kHz throughout and <c>verify_audio.py</c> enforces it.
        /// The importer preserves it rather than letting Unity optimise: ASSETS.md §4.4
        /// records the worst §12 surface pair at 1.396× separation against a 1.4× floor,
        /// so there is no headroom to spend on a resampler's judgement.
        /// </summary>
        public const int ExpectedSampleRate = 48000;

        /// <summary>
        /// The scale policy, as a pair, because neither half means anything alone: Scale Factor
        /// 1.0 and Convert Units <em>on</em>.
        /// <para>
        /// Read out of the FBX files rather than assumed. They declare <c>UnitScaleFactor 1.0</c>
        /// — one file unit is one centimetre — and the exporter put the unit conversion on the
        /// root node as <c>Lcl Scaling 100</c>, which is what Blender's <c>FBX_SCALE_NONE</c>
        /// mode does. Unity therefore reports a file scale of 0.01, and the 0.01 is precisely
        /// what cancels the node's ×100. A vertex at 1.75 arrives at 1.75 m; the measured player
        /// height is 1.750 m and the corridor kit's grid steps are exactly 2.5 m.
        /// </para>
        /// <para>
        /// So there is no arithmetic invariant to assert on the importer — <c>globalScale ×
        /// fileScale</c> is 0.01 here and that is correct. The end-to-end assertion is on
        /// measured bounds, which is the only place the node scaling and the scale multiplier
        /// can be read together. Turning Convert Units off is the failure this documents: it
        /// leaves the ×100 uncancelled and every asset imports a hundred times too large.
        /// </para>
        /// </summary>
        public const float RequiredScaleFactor = 1.0f;

        /// <summary>Companion to <see cref="RequiredScaleFactor"/>: Convert Units must be on.</summary>
        public const bool RequiredUseFileScale = true;

        /// <summary>
        /// Lightmap UV charts are packed with a margin this policy pins, not one Unity derives.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unity's <c>Calculate</c> margin method sizes the gap between charts from two
        /// assumptions — <c>secondaryUVMinLightmapResolution</c> texels per unit across an
        /// object of <c>secondaryUVMinObjectScale</c> units. The second assumption is false for
        /// every file in this project, and not by a little. The note on
        /// <see cref="RequiredScaleFactor"/> explains why: the exporter parks the unit
        /// conversion on the root node as <c>Lcl Scaling 100</c> and Unity cancels it with a
        /// file scale of 0.01, but it cancels it <em>on the Transform</em> — the vertex data
        /// itself keeps the 0.01 and nothing else. So mesh-space in this project is a hundred
        /// times below metres: a correct 6.2 m corridor piece is 0.062 in its own mesh data and
        /// a correct 2.16 cm ring is 0.000216. Against an assumed object scale of 1, Calculate
        /// asks for a margin far wider than the UV square and the unwrapper gives up.
        /// </para>
        /// <para>
        /// <b>What "gives up" means, and why this is a hard policy setting rather than a knob.</b>
        /// The unwrapper logs <c>"Could not pack uvs, most likely Pack Margin is way too high.
        /// Aborting unwrap"</c> — a plain log line, not a warning and not an error — and Unity
        /// then imports the model with <em>no vertices at all</em>. Measured, not inferred:
        /// <c>Loot_Timepiece_PocketWatch</c> and <c>Loot_Timepiece_Ring</c> both imported at
        /// <c>vertexCount 0</c> with zero bounds, which is how a §08 loot piece that is 7 cm and
        /// 2 cm across came to be reported as a unit-scale error. The FBX were never wrong: with
        /// the margin pinned they measure 0.072959 × 0.074000 × 0.016739 m and
        /// 0.021600 × 0.021600 × 0.016502 m, matching what Blender measures to the micrometre.
        /// </para>
        /// <para>
        /// Nothing is relaxed by this. Lightmap UVs stay on for every prop and every kit piece —
        /// §03 makes darkness the lock on progress, so the static geometry has to bake. Only the
        /// margin's <em>derivation</em> changes, from a computation whose premise does not hold
        /// here to Unity's own long-standing default of four texels. Verified against the assets:
        /// with the method switched, <c>Loot_SafeDocument</c> imports to the same 284 vertices
        /// and the same bounds it always did, so nothing that was already packing is repacked.
        /// Lowering the Calculate inputs instead was tried and is strictly worse — at
        /// <c>minObjectScale</c> 0.01 the margin grows and <c>Loot_SafeDocument</c> is emptied too.
        /// </para>
        /// </remarks>
        public const ModelImporterSecondaryUVMarginMethod RequiredLightmapMarginMethod =
            ModelImporterSecondaryUVMarginMethod.Manual;

        /// <summary>
        /// Texels of padding between lightmap UV charts. Unity's default, restated here because
        /// <see cref="RequiredLightmapMarginMethod"/> makes it a value this policy owns rather
        /// than one Unity derives.
        /// </summary>
        public const int LightmapUvPackMargin = 4;

        /// <summary>
        /// Player height in metres, measured from <c>Player.fbx</c>. §12's corridor clear
        /// section is 2.2 × 3.0 m and its grid is 2.5 m; those numbers only mean anything
        /// against a player of this size, so this is a scale anchor rather than a stat.
        /// </summary>
        public const float PlayerHeightMetres = 1.750f;

        /// <summary>
        /// Monster height in metres, measured from <c>Monster.fbx</c>. Deliberately 0.59 m
        /// over the player (ASSETS.md §3.1); both sit inside the 1–3 m sanity band, which
        /// is what makes a unit-scale error detectable at all.
        /// </summary>
        public const float MonsterHeightMetres = 2.336f;

        /// <summary>Fraction of an expected extent a measured extent may differ by before it fails.</summary>
        public const float ScaleTolerance = 0.02f;

        /// <summary>
        /// Attachment points on the player rig that are not humanoid bones and therefore
        /// have no avatar mapping to protect them. They carry §05's flashlight-as-pointer,
        /// §03's objective carry, §08's 가방 and the first-person camera, so the validator
        /// asserts all four survive the Humanoid import.
        /// </summary>
        public static readonly string[] PlayerMountBones =
        {
            "HeadCameraAnchor",
            "FlashlightMount",
            "ObjectiveMount",
            "BackpackMount",
        };

        /// <summary>
        /// Animation clips that are cycles and must have loop time set. Keyed by the clip
        /// name as it appears in the take, without any <c>Rig|</c> prefix.
        /// <para>
        /// The monster's five entries are §06's state machine — 순찰 · 경계 · 추격 · 수색 ·
        /// 정지 — and 정지 is on the list on purpose: ASSETS.md §3.1 notes it moves while
        /// making no sound, so it has to keep moving for as long as the state lasts.
        /// </para>
        /// </summary>
        public static readonly HashSet<string> LoopingAnimationClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "Walk", "Run", "Crouch", "CrouchWalk",

            // §01's 총, both cycles. GunWalk loops because it IS Walk's legs —
            // gen_runner.py asserts the two cadences are identical (CADENCE_WON 16f /
            // 2.013 m/s each) — and GunIdle loops for the same reason Idle does: it is a
            // stance, not an event. Carry/CarryHeavy/CarryIdle left with §03's 목표물 and
            // §08's 대형 전리품; the takes are no longer in Runner.fbx, and a name listed
            // here that no take carries is silently harmless while a take NOT listed is
            // reported by AssetImportModelPostprocessor as an unclassified clip.
            "GunIdle", "GunWalk",

            "Patrol", "Alert", "Chase", "Search", "Standstill",

            // §09's 유령. Drift is the hover it is always in: there is no such thing as a
            // ghost standing still, and there is no other looping state because §09 gives
            // it 「탈출 불가능」 and no way to travel of its own.
            "Drift",
        };

        /// <summary>
        /// Animation clips that are events and must <em>not</em> loop. A looping death or a
        /// looping 섬광수 stun would repeat for as long as the state lasts, which for
        /// <c>Stunned</c> is <c>GameConstants.MonsterStunSeconds</c> and for <c>Death</c> is
        /// the rest of the match (§09 — death is the ghost state, not an ending).
        /// </summary>
        public static readonly HashSet<string> OneShotAnimationClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Death", "Grab", "Stunned",

            // §09's two events. Rattle is 「근처 물건을 아주 약하게 흔든다」 on its 45 s
            // cooldown — the only thing a ghost can do — and Wail is the thing it cannot:
            // 「말하기 불가능」, attempted. Neither loops, and the cooldown is the reason
            // Rattle must not: a signal that repeats is not a signal that costs 45 seconds.
            "Rattle", "Wail",
        };

        /// <summary>
        /// Expected clip counts per character file, from the takes actually present in the
        /// FBX. A count that drifts means a regenerated model gained or lost an animation,
        /// which is worth a hard failure rather than a shrug.
        /// </summary>
        public static readonly Dictionary<string, int> ExpectedAnimationClipCount = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "Assets/Models/Characters/Player.fbx", 9 },
            { "Assets/Models/Characters/Monster.fbx", 7 },
        };

        /// <summary>
        /// Props whose collider must stay concave, and why. Everything else gets a convex
        /// hull, which is both cheaper and mandatory for anything that will ever sit on a
        /// non-kinematic <c>Rigidbody</c> — PhysX refuses a concave mesh collider there, so
        /// every <c>Loot_*</c> piece must be convex to be droppable at all (§08).
        /// </summary>
        public static readonly Dictionary<string, string> ConcaveProps = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "HidingSpot_Locker", "§12's 은폐 지점 — the player stands inside it. A convex hull seals the opening and the hiding spot stops existing." },
            { "Safe_Open", "§08's 금고. The open door and the cavity holding the 문서 are the whole model; a hull fills both." },
            { "Shelving", "§12 sightline blocker. The gaps between shelves are what makes it usable cover rather than a wall." },
            { "Vehicle", "§08's 지상 차량 — 6.7 m long, and the shop is approached from around it. A hull becomes a solid block across the safe zone." },
            { "Pipes", "A 2.4 m run of separated pipes. A hull fills the space between them and closes the corridor." },
            { "Debris", "A scattered pile. A hull turns it into a solid mound the player cannot cross." },
        };

        // DELETED with §08 and §09.
        //
        // NonDiegeticInItems held shop_purchase_confirm and loot_sell_credit — two
        // clips that lived in the positional Items folder but belonged to the shop
        // UI at the surface vehicle. There is no shop and no vehicle.
        //
        // PositionalInUi held ghost_rattle_01..04, "the only positional clips in the
        // UI folder", because a rattle was only information if it localised. §11's
        // 탈락자 rule deleted the channel, so the UI folder now has NO positional
        // clips at all and the branch that asked went with the set: every clip under
        // Audio/UI is an interface cue by construction.

        /// <summary>
        /// Positional beds that loop without saying so in their filename. ASSETS.md §2.2
        /// requires the presence bed be crossfaded by distance only — never by state —
        /// because a state-gated bed would leak the position §06's 정지 exists to hide.
        /// </summary>
        private static readonly HashSet<string> UnnamedLoops = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "monster_presence_bed",
        };

        /// <summary>Platforms whose per-platform overrides are cleared so the shared policy applies.</summary>
        public static readonly string[] AudioOverridePlatforms =
        {
            "Standalone", "WebGL", "Android", "iPhone",
        };

        /// <summary>
        /// True when the asset has been deliberately taken out of the policy's hands, by
        /// either marker. Callers must check this before writing a single setting.
        /// </summary>
        public static bool IsExcluded(string assetPath, AssetImporter importer)
        {
            if (importer != null && !string.IsNullOrEmpty(importer.userData)
                && importer.userData.IndexOf(ManualOverrideToken, StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            return HasFolderOptOut(assetPath);
        }

        /// <summary>
        /// Walks from the asset's folder up to <c>Assets/</c> looking for the folder marker.
        /// Uses <c>System.IO</c> rather than <c>AssetDatabase</c> so it is safe to call during
        /// a pre-process callback, when the database is mid-refresh.
        /// </summary>
        public static bool HasFolderOptOut(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var dir = Path.GetDirectoryName(assetPath);
            while (!string.IsNullOrEmpty(dir))
            {
                var marker = ToAbsolutePath(dir + "/" + FolderOptOutFileName);
                if (marker != null && File.Exists(marker))
                {
                    return true;
                }

                if (dir.Replace('\\', '/').Equals("Assets", StringComparison.Ordinal))
                {
                    return false;
                }

                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.Ordinal))
                {
                    return false;
                }

                dir = parent;
            }

            return false;
        }

        /// <summary>
        /// Turns a project-relative asset path into an absolute one via
        /// <c>Application.dataPath</c>. Deliberately not via the working directory: that is
        /// the project root when the editor is driven from its own UI, but nothing guarantees
        /// it in batch mode, and a silently wrong path here would make every opt-out marker
        /// invisible.
        /// </summary>
        public static string ToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath);
                if (projectRoot == null)
                {
                    return null;
                }

                return Path.Combine(projectRoot.FullName,
                    assetPath.Replace('/', Path.DirectorySeparatorChar));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Lists project-relative paths of every file under an asset folder matching a
        /// pattern, read from disk rather than from <c>AssetDatabase</c>. A file that failed to
        /// import is missing from a database query but present here, which is the difference
        /// between "not checked" and "reported as broken".
        /// </summary>
        public static List<string> EnumerateAssetPaths(string assetFolder, string searchPattern)
        {
            var result = new List<string>();
            var absoluteFolder = ToAbsolutePath(assetFolder);
            if (absoluteFolder == null || !Directory.Exists(absoluteFolder))
            {
                return result;
            }

            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                return result;
            }

            foreach (var file in Directory.GetFiles(absoluteFolder, searchPattern, SearchOption.AllDirectories))
            {
                result.Add(file.Substring(projectRoot.FullName.Length)
                    .TrimStart(Path.DirectorySeparatorChar, '/')
                    .Replace('\\', '/'));
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>Whether this path is one the audio post-processor governs.</summary>
        public static bool IsManagedAudio(string assetPath)
        {
            return assetPath != null
                && assetPath.StartsWith(AudioRoot + "/", StringComparison.Ordinal)
                && assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether this path is one the model post-processor governs.</summary>
        public static bool IsManagedModel(string assetPath)
        {
            return assetPath != null
                && assetPath.StartsWith(ModelRoot + "/", StringComparison.Ordinal)
                && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves a clip's role from its folder and filename, exactly reproducing the two
        /// lists in ASSETS.md §1. Verified against the files on disk: this split puts 130
        /// clips in the positional group and 36 in the non-diegetic group, and 130 is the
        /// count ASSETS.md gives for clips that ship mono.
        /// </summary>
        public static AudioRole ResolveRole(string assetPath)
        {
            var name = Path.GetFileNameWithoutExtension(assetPath) ?? string.Empty;
            var folder = FolderNameOf(assetPath);

            if (folder.Equals("Footsteps", StringComparison.Ordinal))
            {
                return AudioRole.FootstepChannel;
            }

            if (folder.Equals("Monster", StringComparison.Ordinal))
            {
                return IsLoopByName(name) ? AudioRole.PositionalBed : AudioRole.MonsterCue;
            }

            if (folder.Equals("Items", StringComparison.Ordinal))
            {
                // Short item loops stay one-shots for load-type purposes: a 3 s loop
                // gains nothing from staying compressed and must not stutter at the seam.
                return AudioRole.PositionalOneShot;
            }

            if (folder.Equals("Ambience", StringComparison.Ordinal))
            {
                if (name.StartsWith("sfx_", StringComparison.OrdinalIgnoreCase))
                {
                    return AudioRole.PositionalOneShot;
                }

                // §03's generator is the one Ambience bed that is a place rather than a
                // mood: it is the sound the player walks toward when the flashlight dies.
                if (name.Equals("amb_generator_hum_loop", StringComparison.OrdinalIgnoreCase))
                {
                    return AudioRole.PositionalBed;
                }

                return AudioRole.AmbienceBed;
            }

            if (folder.Equals("UI", StringComparison.Ordinal))
            {
                return AudioRole.InterfaceCue;
            }

            // An unrecognised folder under Assets/Audio is treated as non-diegetic, which
            // is the setting that cannot break a role by being wrong. The validator reports
            // it so the omission gets a policy entry instead of a default.
            return AudioRole.InterfaceCue;
        }

        /// <summary>True for the roles that carry information a player acts on by direction.</summary>
        public static bool IsPositionalRole(AudioRole role)
        {
            return role == AudioRole.FootstepChannel
                || role == AudioRole.MonsterCue
                || role == AudioRole.PositionalOneShot
                || role == AudioRole.PositionalBed;
        }

        /// <summary>
        /// True when the clip is meant to play looped. Every generated loop says so in its
        /// filename; <see cref="UnnamedLoops"/> covers the one bed that does not.
        /// </summary>
        public static bool IsLoopByName(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                return false;
            }

            return clipName.IndexOf("_loop", StringComparison.OrdinalIgnoreCase) >= 0
                || UnnamedLoops.Contains(clipName);
        }

        /// <summary>
        /// The full import decision for one clip, from a path and a length.
        /// <para>
        /// Pure: the same two arguments always produce the same rule. Callers that have an
        /// asset on disk must not supply their own length — they call
        /// <see cref="TryResolveAudioFromSource"/>, which is the one place the length comes
        /// from. See its remarks for why.
        /// </para>
        /// </summary>
        public static AudioImportRule ResolveAudio(string assetPath, float seconds)
        {
            var role = ResolveRole(assetPath);
            var positional = IsPositionalRole(role);
            var name = Path.GetFileNameWithoutExtension(assetPath) ?? string.Empty;

            var loadType = LoadTypeFor(seconds);

            // The footstep family and the monster's one-shots stay uncompressed. This is a
            // deliberate departure from ASSETS.md §1's blanket "Vorbis, quality ~70", and
            // the arithmetic is in ASSETS.md §4.4: the worst §12 surface pair already
            // measures 1.396× separation through a wall against a 1.4× floor. There is no
            // margin left to hand a lossy codec, and the whole family is 2.79 MB — the
            // cheapest insurance in the project. Everything the Listener is asked to tell
            // apart therefore reaches the mixer bit-exact.
            var listenerChannel = role == AudioRole.FootstepChannel || role == AudioRole.MonsterCue;
            var compression = listenerChannel || seconds < PcmTransientSeconds
                ? AudioCompressionFormat.PCM
                : AudioCompressionFormat.Vorbis;

            // Nothing streams, so everything can preload and no clip pays a first-play
            // decode. ASSETS.md §1 asks for preload on footsteps specifically; granting it
            // project-wide costs roughly 15 MB and removes the whole class of hitch.
            var preload = loadType != AudioClipLoadType.Streaming;

            // Background loading is only safe where a late clip is not a bug. A footstep
            // that arrives after the step has been taken is §04's channel dropping frames,
            // so the decompress-on-load families load synchronously.
            var loadInBackground = loadType != AudioClipLoadType.DecompressOnLoad;

            return new AudioImportRule(
                role,
                positional,
                positional,
                IsLoopByName(name),
                loadType,
                compression,
                VorbisQuality,
                preload,
                loadInBackground);
        }

        /// <summary>
        /// The full import decision for a clip that exists on disk, resolved from the source
        /// WAVE header. This is the only supported way to get a rule for an asset path.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why the length may not come from an <c>AudioClip</c>.</b> The post-processor has
        /// to decide load type in <c>OnPreprocessAudio</c>, before the clip exists, so it can
        /// only read the source file. The validator runs afterwards and <em>could</em> read
        /// <c>clip.length</c> instead. It must not, and that is the bug this method exists to
        /// make impossible: when the writer and the checker take their length from two
        /// different places, one broken reader turns almost the whole library into clips whose
        /// settings disagree with the policy that supposedly wrote them, and the disagreement is
        /// reported as if the assets were wrong. Role was never the problem — <see cref="ResolveRole"/> has
        /// always been the single source for that, and the <c>.meta</c> files recorded
        /// <c>role=PositionalOneShot</c> for the <c>sfx_</c> clips correctly throughout. The
        /// length was the divergence.
        /// </para>
        /// <para>
        /// <b>Why there is no fallback.</b> A previous version answered an unreadable header
        /// with <see cref="CompressedInMemorySeconds"/> — a length that is not a measurement,
        /// chosen to be memory-safe. It is memory-safe and it is also indistinguishable from a
        /// real 5-second clip, so the parse failure never surfaced; it just quietly moved every
        /// clip into the wrong load-type band. A length we cannot read is reported as a
        /// failure by both callers instead. A guess that cannot be told apart from a
        /// measurement is worse than no answer.
        /// </para>
        /// </remarks>
        /// <param name="assetPath">Project-relative path, e.g. <c>Assets/Audio/UI/click.wav</c>.</param>
        /// <param name="rule">The resolved rule; untouched when this returns false.</param>
        /// <param name="header">The parsed source header, for callers that also audit channels or rate.</param>
        public static bool TryResolveAudioFromSource(string assetPath, out AudioImportRule rule,
            out AssetImportWavHeader header)
        {
            rule = default;
            header = default;

            var absolute = ToAbsolutePath(assetPath);
            if (absolute == null || !AssetImportWavHeader.TryRead(absolute, out header))
            {
                return false;
            }

            rule = ResolveAudio(assetPath, header.Seconds);
            return true;
        }

        /// <summary>
        /// Which load-type band a length falls in. Exposed so the validator can ask whether the
        /// imported clip and the source file agree on the band without duplicating the
        /// thresholds — the same comparison written twice is the shape of the bug this file
        /// just had.
        /// </summary>
        public static AudioClipLoadType LoadTypeFor(float seconds)
        {
            if (seconds >= StreamingSeconds)
            {
                return AudioClipLoadType.Streaming;
            }

            return seconds >= CompressedInMemorySeconds
                ? AudioClipLoadType.CompressedInMemory
                : AudioClipLoadType.DecompressOnLoad;
        }

        /// <summary>Resolves which model policy a path falls under.</summary>
        public static ModelCategory ResolveModelCategory(string assetPath)
        {
            if (!IsManagedModel(assetPath))
            {
                return ModelCategory.Unmanaged;
            }

            var folder = FolderNameOf(assetPath);
            if (folder.Equals("MapKit", StringComparison.Ordinal))
            {
                return ModelCategory.MapKit;
            }

            if (folder.Equals("Props", StringComparison.Ordinal))
            {
                return ModelCategory.Prop;
            }

            if (folder.Equals("Characters", StringComparison.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(assetPath) ?? string.Empty;

                // Monster.fbx is Generic on purpose. Its 29-bone rig carries Jaw, Crest1..3,
                // LeftScapulaSpur and Left/RightForearmExtra, and the forearm extras sit
                // *inside* the arm chain (UpperArm → LowerArm → ForearmExtra → Hand). Unity's
                // humanoid skeleton has no equivalent for the crest or the spur and does not
                // retarget an extra link in a chain, so a Humanoid avatar would drop six
                // bones' worth of animation without a warning — the same failure mode as a
                // stereo footstep. The monster ships its own seven clips and never needs to
                // retarget, so Generic costs nothing. Verified against the FBX: bone names
                // and takes read directly out of the file, and tools/blender/gen_monster_ai.py
                // says the same in as many words.
                //
                // Exact match, and it is the reason there is exactly one monster in
                // Assets/Models/Characters. Anything else that landed in this folder would
                // be graded against the *player's* humanoid policy and reported as broken on
                // every validator run — which is what two unadopted monster variants were
                // doing here, four failures a run, until they were retired. A generator that
                // wants to publish a variant writes it to artifacts/, not to Assets/.
                if (name.Equals("Monster", StringComparison.OrdinalIgnoreCase))
                {
                    return ModelCategory.CharacterGeneric;
                }

                return ModelCategory.CharacterHumanoid;
            }

            return ModelCategory.Prop;
        }

        /// <summary>The full import decision for one model.</summary>
        public static ModelImportRule ResolveModel(string assetPath)
        {
            var category = ResolveModelCategory(assetPath);
            var name = Path.GetFileNameWithoutExtension(assetPath) ?? string.Empty;

            switch (category)
            {
                case ModelCategory.CharacterHumanoid:
                case ModelCategory.CharacterGeneric:
                    // No lightmap UVs: a skinned mesh is never baked. No collider: §05's
                    // capsule is authored on the prefab, and a mesh collider on a
                    // SkinnedMeshRenderer would have to be re-cooked every frame.
                    // No mesh compression: quantising positions on bone-weighted vertices
                    // shows up as a wobble around the joints.
                    return new ModelImportRule(category, true, false,
                        ModelImporterMeshCompression.Off, false, false,
                        category == ModelCategory.CharacterGeneric ? MonsterHeightMetres : PlayerHeightMetres);

                case ModelCategory.MapKit:
                    // Mesh compression stays off here, against the general "compression on"
                    // rule, because §12's pieces dock to each other on a 2.5 m grid and
                    // compression quantises vertex positions. A sub-millimetre shift at a
                    // dock is a visible seam or a gap the monster's NavMesh path threads
                    // through. The whole kit is 0.63 MB and 12,166 triangles, so the saving
                    // being declined is a fraction of a megabyte.
                    //
                    // Colliders are per-triangle: level geometry is concave by definition —
                    // a corridor is a hole, and a convex hull of a corridor is a solid block.
                    return new ModelImportRule(category, false, true,
                        ModelImporterMeshCompression.Off, true, false, 0f);

                case ModelCategory.Prop:
                    // Low, not Medium or High. Props are 200–2,500 triangles and none of
                    // them docks to anything, so the lowest compression tier is real
                    // savings at a precision cost nothing depends on.
                    return new ModelImportRule(category, false, true,
                        ModelImporterMeshCompression.Low, true, IsConvexProp(name), 0f);

                default:
                    return new ModelImportRule(ModelCategory.Unmanaged, false, false,
                        ModelImporterMeshCompression.Off, false, false, 0f);
            }
        }

        /// <summary>
        /// Whether a prop's mesh collider may be convex. Convex is the default because it
        /// is cheaper and because PhysX rejects a concave mesh collider on a non-kinematic
        /// <c>Rigidbody</c> — which every <c>Loot_*</c> piece needs the moment §08's
        /// drop-and-pick-up loop touches it.
        /// </summary>
        public static bool IsConvexProp(string modelName)
        {
            return !ConcaveProps.ContainsKey(modelName ?? string.Empty);
        }

        /// <summary>
        /// The plausible size band for a category, in metres, used to catch a unit-scale
        /// error. A 100× mistake — the one that happens when scale factor and unit
        /// conversion fight — lands orders of magnitude outside these, which is the point:
        /// §12's every distance is wrong at once when this is wrong, so it must be loud.
        /// </summary>
        public static void MetreScaleBand(ModelCategory category, out float minMetres, out float maxMetres)
        {
            switch (category)
            {
                case ModelCategory.CharacterHumanoid:
                case ModelCategory.CharacterGeneric:
                    minMetres = 1.0f;
                    maxMetres = 3.0f;
                    break;

                case ModelCategory.MapKit:
                    // The largest piece is Hall_Open_20x20 at 20 m; the smallest extent in
                    // the kit is a 0.15 m floor tile lip.
                    minMetres = 0.10f;
                    maxMetres = 25.0f;
                    break;

                case ModelCategory.Prop:
                    // Loot_Timepiece_Ring is 0.022 m across; Vehicle is 6.7 m long.
                    minMetres = 0.010f;
                    maxMetres = 8.0f;
                    break;

                default:
                    minMetres = 0f;
                    maxMetres = float.MaxValue;
                    break;
            }
        }

        /// <summary>
        /// The record written into importer <c>userData</c>. Deliberately a flat key/value
        /// string rather than JSON so it stays one readable line in the <c>.meta</c> and a
        /// reviewer can see a policy change in a diff.
        /// </summary>
        public static string BuildAudioUserData(AudioImportRule rule)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "hgpolicy={0};role={1};spatial={2};loop={3}",
                PolicyVersion,
                rule.Role,
                rule.Positional ? "3D" : "2D",
                rule.Loops ? 1 : 0);
        }

        /// <summary>
        /// Reads back a value written by <see cref="BuildAudioUserData"/>. Returns false when
        /// the key is absent, which is how the validator notices a <c>.meta</c> that predates
        /// the policy or was hand-edited.
        /// </summary>
        public static bool TryReadUserDataValue(string userData, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(userData))
            {
                return false;
            }

            foreach (var pair in userData.Split(';'))
            {
                var split = pair.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                if (pair.Substring(0, split).Trim().Equals(key, StringComparison.Ordinal))
                {
                    value = pair.Substring(split + 1).Trim();
                    return true;
                }
            }

            return false;
        }

        /// <summary>The immediate folder name of an asset path, or empty for a root-level asset.</summary>
        public static string FolderNameOf(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir))
            {
                return string.Empty;
            }

            return Path.GetFileName(dir.Replace('\\', '/')) ?? string.Empty;
        }
    }

    /// <summary>
    /// Just enough RIFF/WAVE parsing to answer "how long is this, and how many channels".
    /// <para>
    /// Load type has to be chosen in <c>OnPreprocessAudio</c>, before the <c>AudioClip</c>
    /// exists, so the length cannot come from Unity. Reading the source header is the only
    /// way to key an import setting off the content, and it costs one file handle per clip.
    /// </para>
    /// </summary>
    public struct AssetImportWavHeader
    {
        public int Channels;
        public int SampleRate;
        public int BitsPerSample;
        public float Seconds;

        /// <summary>
        /// Reads the format and data chunk sizes. Returns false for anything that is not a
        /// PCM WAVE file, in which case the caller must fall back to a setting that is safe
        /// without knowing the length.
        /// </summary>
        public static bool TryRead(string absolutePath, out AssetImportWavHeader header)
        {
            header = default;

            try
            {
                using (var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length < 12)
                    {
                        return false;
                    }

                    // Chunk ids are read as raw bytes rather than through BinaryReader.ReadChars,
                    // which decodes through an encoding and can consume more than four bytes on
                    // a malformed file — desynchronising the whole chunk walk.
                    if (!TagEquals(reader.ReadBytes(4), "RIFF"))
                    {
                        return false;
                    }

                    reader.ReadUInt32();
                    if (!TagEquals(reader.ReadBytes(4), "WAVE"))
                    {
                        return false;
                    }

                    var channels = 0;
                    var sampleRate = 0;
                    var bits = 0;
                    long dataBytes = -1;

                    while (stream.Position + 8 <= stream.Length)
                    {
                        // The next chunk's offset is derived from where *this* chunk's header
                        // began, never from the read position. Deriving it from the position
                        // is what made this walk stop at 'fmt ': the branch below consumes
                        // exactly the 16 bytes a canonical fmt chunk declares, which leaves the
                        // position sitting precisely on the next chunk — and a "did we make
                        // progress" test written as `next <= position` then reads that as a
                        // malformed file and breaks out. Every clip in the project is canonical
                        // 16-byte fmt, so 'data' was never reached, the length was never
                        // learned, and every clip silently took the unknown-length fallback.
                        // Chunk sizes are unsigned and the header is eight bytes, so `next` is
                        // always strictly past `chunkStart`; the walk cannot stall and needs no
                        // progress test at all.
                        var chunkStart = stream.Position;
                        var chunkId = reader.ReadBytes(4);
                        var chunkSize = reader.ReadUInt32();
                        var bodyStart = chunkStart + 8;
                        var next = bodyStart + chunkSize + (chunkSize % 2);

                        if (TagEquals(chunkId, "fmt ") && chunkSize >= 16)
                        {
                            reader.ReadUInt16();
                            channels = reader.ReadUInt16();
                            sampleRate = (int)reader.ReadUInt32();
                            reader.ReadUInt32();
                            reader.ReadUInt16();
                            bits = reader.ReadUInt16();
                        }
                        else if (TagEquals(chunkId, "data"))
                        {
                            // Clamped to what the file actually holds. A declared size larger
                            // than the file is a truncated write, and trusting it would report
                            // a clip longer than the audio that exists — which is exactly the
                            // kind of wrong-but-plausible length that decides a load type.
                            dataBytes = Math.Min((long)chunkSize, stream.Length - bodyStart);
                        }

                        if (next > stream.Length)
                        {
                            break;
                        }

                        stream.Position = next;
                    }

                    // bits < 8 would make the bytes-per-frame divisor zero.
                    if (channels <= 0 || sampleRate <= 0 || bits < 8 || dataBytes < 0)
                    {
                        return false;
                    }

                    header.Channels = channels;
                    header.SampleRate = sampleRate;
                    header.BitsPerSample = bits;
                    header.Seconds = (float)(dataBytes / (double)(channels * (bits / 8)) / sampleRate);
                    return true;
                }
            }
            // IOException covers EndOfStreamException, which is what a truncated file throws.
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Compares a four-byte RIFF chunk id against an ASCII tag.</summary>
        private static bool TagEquals(byte[] read, string tag)
        {
            if (read == null || read.Length != 4 || tag.Length != 4)
            {
                return false;
            }

            for (var i = 0; i < 4; i++)
            {
                if (read[i] != (byte)tag[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
