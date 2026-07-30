using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HorrorGame.Core;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Checks that every governed asset actually imported the way <see cref="AssetImportPolicy"/>
    /// says it should, and reports the ones that did not.
    /// <para>
    /// The post-processors make the settings right on import. This exists for the other case:
    /// settings that were right and stopped being right. A per-platform override added in the
    /// Inspector, a <c>.meta</c> merged badly, a clip regenerated into the wrong folder, a
    /// re-export that renamed a take — none of those produce an error, and the symptoms are
    /// all "the game feels off" rather than "something is broken". A stereo footstep in
    /// particular is inaudible <em>as a fault</em>: the clip plays, so nothing looks wrong,
    /// and §04's Listener simply stops being able to do the one thing the role does.
    /// </para>
    /// <para>
    /// So this turns an inaudible regression into a red line in the console, and in batch mode
    /// into a non-zero exit code that a build can refuse to continue past.
    /// </para>
    /// </summary>
    public static class AssetImportValidator
    {
        /// <summary>
        /// Collects findings so the whole picture is logged at once. A validator that stops at
        /// the first failure teaches you about one clip per run.
        /// </summary>
        private sealed class Report
        {
            private readonly List<string> _errors = new List<string>();
            private readonly List<string> _warnings = new List<string>();
            private readonly List<string> _notes = new List<string>();

            public int Inspected { get; private set; }
            public int Excluded { get; private set; }
            public bool Passed => _errors.Count == 0;

            public void CountInspected()
            {
                Inspected++;
            }

            public void CountExcluded()
            {
                Excluded++;
            }

            public void Fail(string assetPath, string message)
            {
                _errors.Add($"  {assetPath}\n      {message}");
            }

            public void Warn(string assetPath, string message)
            {
                _warnings.Add($"  {assetPath}\n      {message}");
            }

            public void Note(string message)
            {
                _notes.Add("  " + message);
            }

            public void Log(string title)
            {
                var summary = $"[AssetImport] {title}: {Inspected} inspected, {Excluded} excluded by marker, "
                    + $"{_errors.Count} failing, {_warnings.Count} warnings.";

                if (_notes.Count > 0)
                {
                    Debug.Log(summary + "\n" + string.Join("\n", _notes));
                }
                else
                {
                    Debug.Log(summary);
                }

                if (_warnings.Count > 0)
                {
                    Debug.LogWarning($"[AssetImport] {title} — {_warnings.Count} warning(s):\n"
                        + string.Join("\n", _warnings));
                }

                if (_errors.Count > 0)
                {
                    Debug.LogError($"[AssetImport] {title} — {_errors.Count} FAILING asset(s):\n"
                        + string.Join("\n", _errors));
                }
            }
        }

        // ── Menu entry points ───────────────────────────────────────────────────

        [MenuItem("Horror/Assets/Validate Audio Import Settings", priority = 20)]
        public static void ValidateAudioMenu()
        {
            var report = new Report();
            ValidateAudio(report);
            report.Log("Audio import settings");
        }

        [MenuItem("Horror/Assets/Validate Model Import Settings", priority = 21)]
        public static void ValidateModelsMenu()
        {
            var report = new Report();
            ValidateModels(report);
            report.Log("Model import settings");
        }

        [MenuItem("Horror/Assets/Validate All Asset Imports", priority = 22)]
        public static void ValidateAllMenu()
        {
            ValidateAll();
        }

        /// <summary>
        /// Runs both suites and returns whether everything passed, so a build script can gate
        /// on it without parsing the console.
        /// </summary>
        public static bool ValidateAll()
        {
            var audio = new Report();
            ValidateAudio(audio);
            audio.Log("Audio import settings");

            var models = new Report();
            ValidateModels(models);
            models.Log("Model import settings");

            return audio.Passed && models.Passed;
        }

        /// <summary>
        /// Batch-mode entry point for CI:
        /// <code>Unity -batchmode -quit -projectPath . -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch</code>
        /// <para>
        /// Exits non-zero on any failure. §13 ships this on Steam, and the settings this file
        /// guards are the ones whose breakage reaches players as "the audio is fine, the
        /// Listener just doesn't work".
        /// </para>
        /// </summary>
        public static void ValidateAllBatch()
        {
            try
            {
                EditorApplication.Exit(ValidateAll() ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[AssetImport] Validation threw: " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Forces a reimport of every governed asset. Needed after editing the policy without
        /// bumping <see cref="AssetImportPolicy.PolicyVersion"/>, and after removing an
        /// opt-out marker.
        /// </summary>
        [MenuItem("Horror/Assets/Reimport Audio And Models", priority = 40)]
        public static void ReimportAll()
        {
            var paths = ManagedAudioPaths().Concat(ManagedModelPaths()).ToArray();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in paths)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            Debug.Log($"[AssetImport] Reimported {paths.Length} asset(s) under {AssetImportPolicy.AudioRoot} "
                + $"and {AssetImportPolicy.ModelRoot}.");
        }

        /// <summary>
        /// Writes the manual-import marker onto the selected assets so the post-processors
        /// leave them alone. The alternative — commenting a rule out — would take every asset
        /// with it.
        /// </summary>
        [MenuItem("Horror/Assets/Mark Selection As Manual Import", priority = 60)]
        public static void MarkSelectionManual()
        {
            var touched = 0;
            foreach (var path in SelectedAssetPaths())
            {
                var importer = AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(importer.userData)
                    && importer.userData.IndexOf(AssetImportPolicy.ManualOverrideToken, StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                importer.userData = AssetImportPolicy.ManualOverrideToken;
                importer.SaveAndReimport();
                touched++;
            }

            Debug.Log($"[AssetImport] Marked {touched} asset(s) as manual. The policy will not touch them, and the "
                + "validator will report them as excluded rather than as broken. Remove the marker with "
                + "'Return Selection To Policy'.");
        }

        /// <summary>Removes the manual-import marker and reimports under the policy again.</summary>
        [MenuItem("Horror/Assets/Return Selection To Policy", priority = 61)]
        public static void ReturnSelectionToPolicy()
        {
            var touched = 0;
            foreach (var path in SelectedAssetPaths())
            {
                var importer = AssetImporter.GetAtPath(path);
                if (importer == null || string.IsNullOrEmpty(importer.userData))
                {
                    continue;
                }

                if (importer.userData.IndexOf(AssetImportPolicy.ManualOverrideToken, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                importer.userData = string.Empty;
                importer.SaveAndReimport();
                touched++;
            }

            Debug.Log($"[AssetImport] Returned {touched} asset(s) to the policy.");
        }

        // ── Audio ───────────────────────────────────────────────────────────────

        private static void ValidateAudio(Report report)
        {
            var paths = ManagedAudioPaths();
            if (paths.Count == 0)
            {
                report.Fail(AssetImportPolicy.AudioRoot,
                    "No .wav files found. ASSETS.md records 166 clips on disk, so either the project is not the "
                    + "one described or the audio has been moved.");
                return;
            }

            var positional = 0;
            var nonDiegetic = 0;
            var loops = 0;

            foreach (var path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                {
                    report.Fail(path, "Has no AudioImporter. The file did not import as audio at all.");
                    continue;
                }

                if (AssetImportPolicy.IsExcluded(path, importer))
                {
                    report.CountExcluded();
                    continue;
                }

                report.CountInspected();

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    report.Fail(path, "Did not produce an AudioClip. Nothing downstream can play it.");
                    continue;
                }

                // Resolved through the same seam the post-processor writes through, from the
                // same source header. Reading clip.length here instead is what let one broken
                // header parse on the writing side look like a library of broken assets on the
                // checking side: the two agreed on the rule and disagreed on the input.
                if (!AssetImportPolicy.TryResolveAudioFromSource(path, out var rule, out var header))
                {
                    report.Fail(path, "Its WAVE header could not be read, so no load type or codec can be "
                        + "resolved for it and the post-processor wrote nothing. Re-export it as 48 kHz PCM WAVE.");
                    continue;
                }

                CheckImportedLengthAgrees(report, path, header, clip);

                if (rule.Positional)
                {
                    positional++;
                }
                else
                {
                    nonDiegetic++;
                }

                if (rule.Loops)
                {
                    loops++;
                }

                CheckChannelPolicy(report, path, rule, clip, importer);
                CheckSampleSettings(report, path, rule, importer);
                CheckPlatformOverrides(report, path, importer);
                CheckRecordedIntent(report, path, rule, importer);

                if (clip.frequency != AssetImportPolicy.ExpectedSampleRate)
                {
                    report.Warn(path, $"Imported at {clip.frequency} Hz rather than "
                        + $"{AssetImportPolicy.ExpectedSampleRate} Hz. ASSETS.md §4.3's material separation matrix "
                        + "was measured at 48 kHz, so its 1.4× floor no longer describes this clip.");
                }

                var folder = AssetImportPolicy.FolderNameOf(path);
                if (!IsKnownAudioFolder(folder))
                {
                    report.Warn(path, $"Lives in '{folder}', which has no entry in AssetImportPolicy.ResolveRole, so "
                        + "it was defaulted to non-diegetic 2D. If it belongs in the world it is currently silent "
                        + "as a direction cue.");
                }
            }

            CheckDesignAnchors(report);

            report.Note($"{positional} positional clip(s), {nonDiegetic} non-diegetic, {loops} marked as loops. "
                + "ASSETS.md records 130 positional and 36 non-diegetic out of 166.");
        }

        /// <summary>
        /// Asserts that the clip Unity holds and the file on disk are the same length, to the
        /// only precision that matters here: whether they land in the same load-type band.
        /// <para>
        /// Every setting is now resolved from the source header, so the imported asset is no
        /// longer consulted about its own policy — which means a clip regenerated longer or
        /// shorter than the one that was imported would otherwise pass silently while the
        /// shipped asset carried the previous file's load type. Comparing bands rather than
        /// seconds keeps this off codec padding: Vorbis rounds a decoded length by a frame or
        /// two, and a check that fires on that is a check people learn to ignore.
        /// </para>
        /// </summary>
        private static void CheckImportedLengthAgrees(Report report, string path,
            AssetImportWavHeader header, AudioClip clip)
        {
            var sourceBand = AssetImportPolicy.LoadTypeFor(header.Seconds);
            var importedBand = AssetImportPolicy.LoadTypeFor(clip.length);
            if (sourceBand == importedBand)
            {
                return;
            }

            report.Fail(path, $"The file on disk is {header.Seconds:0.###} s but the imported clip is "
                + $"{clip.length:0.###} s, which puts them in different load-type bands ({sourceBand} versus "
                + $"{importedBand}). The policy resolves from the source file, so the asset in the project is "
                + "carrying settings chosen for a different clip. Reimport it.");
        }

        /// <summary>
        /// The check this whole file exists for. A positional clip that imported with two
        /// channels is not spatialised by Unity at all — no attenuation, no panning — and
        /// nothing reports it.
        /// </summary>
        private static void CheckChannelPolicy(Report report, string path, AudioImportRule rule,
            AudioClip clip, AudioImporter importer)
        {
            if (rule.Positional)
            {
                if (clip.channels != 1)
                {
                    report.Fail(path, $"Positional ({rule.Role}) but imported with {clip.channels} channels. "
                        + "Unity does not spatialise a stereo clip: it plays at a fixed level from no direction. "
                        + "That is §04's Listener unable to read 위치 · 거리 · 이동 방향, §13's proximity voice "
                        + "placing every teammate at zero metres, and §09's ghost rattle carrying no information — "
                        + "all without an error. Force To Mono must be on.");
                }

                if (!importer.forceToMono)
                {
                    report.Fail(path, "Positional but Force To Mono is off. The source happens to be mono today, so "
                        + "playback is correct — the guard is missing, and the next regeneration that emits stereo "
                        + "will disable the role silently.");
                }
            }
            else
            {
                if (importer.forceToMono)
                {
                    report.Fail(path, "Non-diegetic but Force To Mono is on. These are stereo on purpose: they are "
                        + "the only place the stereo image §05's mandatory headphones exist for is used.");
                }
            }

            if (importer.ambisonic)
            {
                report.Fail(path, "Ambisonic is on. None of these clips is B-format, and ambisonic decoding replaces "
                    + "the panner, so direction is lost the same way a stereo clip loses it.");
            }

            if (importer.loadInBackground != rule.LoadInBackground)
            {
                report.Warn(path, $"Load In Background is {importer.loadInBackground}, policy says "
                    + $"{rule.LoadInBackground}. For a decompress-on-load cue this decides whether the first play "
                    + "is on time.");
            }
        }

        private static void CheckSampleSettings(Report report, string path, AudioImportRule rule,
            AudioImporter importer)
        {
            var settings = importer.defaultSampleSettings;

            if (settings.loadType != rule.LoadType)
            {
                report.Fail(path, $"Load type is {settings.loadType}, policy says {rule.LoadType} for a "
                    + $"{rule.Role} clip of this length.");
            }

            if (settings.compressionFormat != rule.Compression)
            {
                var extra = rule.Compression == AudioCompressionFormat.PCM
                    ? " This family stays uncompressed: ASSETS.md §4.4 measures the worst §12 surface pair at "
                      + "1.396× separation through a wall against a 1.4× floor, so there is no margin to hand a "
                      + "lossy codec."
                    : string.Empty;
                report.Fail(path, $"Compression is {settings.compressionFormat}, policy says {rule.Compression}." + extra);
            }
            else if (rule.Compression != AudioCompressionFormat.PCM
                     && !Mathf.Approximately(settings.quality, rule.Quality))
            {
                report.Warn(path, $"Vorbis quality is {settings.quality:0.###}, policy says {rule.Quality:0.###}.");
            }

            if (settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate)
            {
                report.Fail(path, $"Sample rate setting is {settings.sampleRateSetting}. It must be "
                    + "PreserveSampleRate: the rate optimiser decides from content, and §12's gravel identity is a "
                    + "6.2 kHz noise band whose separation from concrete is already at the floor.");
            }

            if (settings.preloadAudioData != rule.Preload)
            {
                report.Warn(path, $"Preload Audio Data is {settings.preloadAudioData}, policy says {rule.Preload}. "
                    + "Nothing in the project streams, so the only thing not preloading buys is a first-play hitch.");
            }
        }

        private static void CheckPlatformOverrides(Report report, string path, AudioImporter importer)
        {
            foreach (var platform in AssetImportPolicy.AudioOverridePlatforms)
            {
                if (!importer.ContainsSampleSettingsOverride(platform))
                {
                    continue;
                }

                report.Fail(path, $"Has a per-platform override for '{platform}'. An override is invisible unless "
                    + "the Inspector happens to be open on that tab, which makes it the ideal hiding place for the "
                    + "settings this policy exists to pin. Use the manual-import marker instead, so the intent is "
                    + "recorded.");
            }
        }

        private static void CheckRecordedIntent(Report report, string path, AudioImportRule rule,
            AudioImporter importer)
        {
            var expected = AssetImportPolicy.BuildAudioUserData(rule);
            if (string.Equals(importer.userData, expected, StringComparison.Ordinal))
            {
                return;
            }

            if (!AssetImportPolicy.TryReadUserDataValue(importer.userData, "hgpolicy", out _))
            {
                report.Warn(path, "Carries no policy record in its .meta user data. Spatial blend and loop are "
                    + "AudioSource properties, not import settings, so the record is how that intent reaches "
                    + "prefab authoring and shows up in a diff. Reimport to write it.");
                return;
            }

            report.Warn(path, $"Policy record is '{importer.userData}' but the current policy resolves to "
                + $"'{expected}'. Reimport to bring the .meta up to date.");
        }

        /// <summary>
        /// Checks the handful of clips whose properties are pinned by a design number rather
        /// than by a family rule, so a retune on one side of the seam cannot pass unnoticed.
        /// </summary>
        private static void CheckDesignAnchors(Report report)
        {
            foreach (var name in new[] { "monster_stun_01", "monster_stun_02" })
            {
                var path = $"{AssetImportPolicy.AudioRoot}/Monster/{name}.wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    report.Fail(path, "Missing. ASSETS.md §2.2 maps it to §04's 섬광수 stun.");
                    continue;
                }

                if (Mathf.Abs(clip.length - GameConstants.FlashStunSeconds) > 0.02f)
                {
                    report.Fail(path, $"Runs {clip.length:0.###} s but GameConstants.FlashStunSeconds is "
                        + $"{GameConstants.FlashStunSeconds:0.###} s. ASSETS.md §2.2 mirrors the two on purpose: the "
                        + "clip is how long the stun sounds and the constant is how long it lasts, so a gap between "
                        + "them is the monster audibly recovering before or after it actually does. Regenerate with "
                        + "tools/audio/gen_monster_audio.py.");
                }
            }

            // §09 gives the ghost one channel and nothing else. Four rattles exist so a repeat
            // is not a tell, and they only carry information if the team can turn toward them.
            for (var i = 1; i <= 4; i++)
            {
                var path = $"{AssetImportPolicy.AudioRoot}/UI/ghost_rattle_{i:00}.wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    report.Fail(path, "Missing. §09 makes the rattle the ghost's only channel, on a "
                        + $"{GameConstants.GhostRattleCooldownSeconds:0} s cooldown inside "
                        + $"{GameConstants.GhostRattleRange:0} m.");
                    continue;
                }

                if (clip.channels != 1)
                {
                    report.Fail(path, $"Imported with {clip.channels} channels. §09's rattle is the ghost's only "
                        + $"channel and its range is {GameConstants.GhostRattleRange:0} m — a clip that does not "
                        + "attenuate over 4 m conveys nothing at all, and a dead player has no other way to speak.");
                }
            }
        }

        private static bool IsKnownAudioFolder(string folder)
        {
            return folder.Equals("Footsteps", StringComparison.Ordinal)
                || folder.Equals("Monster", StringComparison.Ordinal)
                || folder.Equals("Ambience", StringComparison.Ordinal)
                || folder.Equals("Items", StringComparison.Ordinal)
                || folder.Equals("UI", StringComparison.Ordinal);
        }

        // ── Models ──────────────────────────────────────────────────────────────

        private static void ValidateModels(Report report)
        {
            var paths = ManagedModelPaths();
            if (paths.Count == 0)
            {
                report.Fail(AssetImportPolicy.ModelRoot,
                    "No .fbx files found. ASSETS.md records 47 on disk.");
                return;
            }

            var manifest = LoadMapKitManifest(report);

            foreach (var path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    report.Fail(path, "Has no ModelImporter. The file did not import as a model.");
                    continue;
                }

                if (AssetImportPolicy.IsExcluded(path, importer))
                {
                    report.CountExcluded();
                    continue;
                }

                report.CountInspected();

                var rule = AssetImportPolicy.ResolveModel(path);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null)
                {
                    report.Fail(path, "Did not produce a GameObject. Nothing can be instantiated from it.");
                    continue;
                }

                CheckModelScale(report, path, rule, importer, root, manifest);
                CheckModelMeshSettings(report, path, rule, importer);
                CheckModelRig(report, path, rule, importer);
                CheckModelColliders(report, path, rule, root);
                CheckModelAnimations(report, path, rule, importer);
            }

            ReportPreviewCopies(report);
        }

        /// <summary>
        /// The loud one. §12's grid is 2.5 m, its storey 3.75 m, its corridor clear section
        /// 2.2 × 3.0 m; §06's speeds are 4.8 and 5.6 m/s and its aggro release is 12 m. All of
        /// that is metres, so a scale error does not break one rule — it breaks every rule at
        /// once, and the game reads as mistuned rather than as misconfigured.
        /// </summary>
        private static void CheckModelScale(Report report, string path, ModelImportRule rule,
            ModelImporter importer, GameObject root, MapKitManifest manifest)
        {
            if (!Mathf.Approximately(importer.globalScale, AssetImportPolicy.RequiredScaleFactor))
            {
                report.Fail(path, $"Scale factor is {importer.globalScale:0.######}, must be "
                    + $"{AssetImportPolicy.RequiredScaleFactor}. The exports are already 1 unit = 1 metre, so any "
                    + "scale factor other than 1 is a second opinion about the size of a metre.");
            }

            if (importer.useFileScale != AssetImportPolicy.RequiredUseFileScale)
            {
                report.Fail(path, "Convert Units is off. These FBX declare UnitScaleFactor 1.0 — centimetre units — "
                    + "and the exporter parked the unit conversion on the root node as a ×100 scale. The file scale "
                    + "of 0.01 is what cancels it, so switching Convert Units off leaves the ×100 uncancelled and "
                    + "every asset arrives a hundred times too large.");
            }

            var extents = SortedExtents(root, out var meshCount, out var emptyMeshCount);
            if (extents == null)
            {
                report.Warn(path, "Imported with no mesh, so its scale could not be measured.");
                return;
            }

            // Reported before the band check, and separately from it, because the two are
            // different faults with opposite fixes. A mesh that imported empty measures zero,
            // and zero is outside every band — so the band check alone accuses the FBX of a
            // unit-scale error when the FBX is correct and the import threw the geometry away.
            // That misdirection cost real time: see AssetImportPolicy.RequiredLightmapMarginMethod.
            if (emptyMeshCount > 0)
            {
                report.Fail(path, $"{emptyMeshCount} of {meshCount} mesh(es) imported with zero vertices. This is "
                    + "not a scale error and the source file is not the problem — Unity produced an empty mesh. The "
                    + "known cause is the lightmap UV unwrapper aborting: it logs \"Could not pack uvs, most likely "
                    + "Pack Margin is way too high. Aborting unwrap\" as a plain log line, not a warning, and then "
                    + "imports no geometry at all. Check that Generate Lightmap UVs is using the "
                    + $"{AssetImportPolicy.RequiredLightmapMarginMethod} margin method with a pack margin of "
                    + $"{AssetImportPolicy.LightmapUvPackMargin}, then reimport.");
                if (emptyMeshCount == meshCount)
                {
                    return;
                }
            }

            AssetImportPolicy.MetreScaleBand(rule.Category, out var min, out var max);
            if (extents[0] < min || extents[0] > max)
            {
                report.Fail(path, $"Measures {DescribeExtents(extents)}, and its largest extent is outside the "
                    + $"{min:0.000000}–{max:0.000000} m band for {rule.Category}. This is a unit-scale error, and it "
                    + "makes §12's grid, corridor section, straight-run cap and sightline spacing all wrong "
                    + $"simultaneously. Scale factor is {importer.globalScale:0.000000}, Convert Units is "
                    + $"{(importer.useFileScale ? "on" : "off")}, and the file reports a scale of "
                    + $"{importer.fileScale:0.000000}.");
                return;
            }

            if (rule.ExpectedTallestExtentMetres > 0f)
            {
                var expected = rule.ExpectedTallestExtentMetres;
                if (Mathf.Abs(extents[0] - expected) > expected * AssetImportPolicy.ScaleTolerance)
                {
                    report.Fail(path, $"Measures {DescribeExtents(extents)} against a scale anchor of "
                        + $"{expected:0.000000} m. The monster is authored 0.59 m taller than the player and both sit "
                        + "inside the 1–3 m sanity band; a drift here means §12's dimensions no longer describe the "
                        + "space this character occupies.");
                }
            }

            if (rule.Category == ModelCategory.MapKit && manifest != null)
            {
                CheckAgainstMapKitManifest(report, path, extents, manifest);
            }
        }

        /// <summary>
        /// Cross-checks a kit piece's measured bounds against <c>MapKit.manifest.json</c>, which
        /// carries the authored footprint and height for all 21 pieces.
        /// <para>
        /// This is the strongest metre-scale assertion available, because it is not a band — it
        /// is the number §12 derived, written down by the generator, compared against what
        /// Unity produced. It also regenerates with the models, so unlike a table in this file
        /// it cannot go stale.
        /// </para>
        /// </summary>
        private static void CheckAgainstMapKitManifest(Report report, string path, float[] extents,
            MapKitManifest manifest)
        {
            var fileName = Path.GetFileName(path);
            var piece = manifest.pieces?.FirstOrDefault(p =>
                p != null && string.Equals(p.file, fileName, StringComparison.OrdinalIgnoreCase));

            if (piece == null)
            {
                report.Warn(path, "Not listed in MapKit.manifest.json. §12's assembler reads the manifest rather "
                    + "than the folder, so a piece that is not in it does not exist as far as map generation is "
                    + "concerned.");
                return;
            }

            if (piece.footprint_metres == null || piece.footprint_metres.Length < 2)
            {
                report.Warn(path, "Manifest entry has no footprint, so its bounds cannot be cross-checked.");
                return;
            }

            var expected = new[] { piece.footprint_metres[0], piece.footprint_metres[1], piece.height_metres };
            Array.Sort(expected);
            Array.Reverse(expected);

            for (var i = 0; i < 3; i++)
            {
                var tolerance = Mathf.Max(expected[i] * 0.01f, 0.005f);
                if (Mathf.Abs(extents[i] - expected[i]) <= tolerance)
                {
                    continue;
                }

                report.Fail(path, $"Imported bounds sorted to {DescribeExtents(extents)} but MapKit.manifest.json "
                    + $"authored {DescribeExtents(expected)} "
                    + $"(footprint {piece.footprint_metres[0]:0.###} × {piece.footprint_metres[1]:0.###} m, "
                    + $"height {piece.height_metres:0.###} m). §12's pieces dock on a "
                    + $"{manifest.grid_metres:0.###} m grid; a piece that is not the size the manifest says it is "
                    + "will not dock, and every rule derived from its dimensions is off.");
                return;
            }
        }

        private static void CheckModelMeshSettings(Report report, string path, ModelImportRule rule,
            ModelImporter importer)
        {
            if (importer.meshCompression != rule.MeshCompression)
            {
                var extra = rule.Category == ModelCategory.MapKit
                    ? " The kit stays uncompressed on purpose: compression quantises vertex positions, and §12's "
                      + "pieces dock to each other — a sub-millimetre shift at a dock is a seam or a gap."
                    : string.Empty;
                report.Fail(path, $"Mesh compression is {importer.meshCompression}, policy says "
                    + $"{rule.MeshCompression}." + extra);
            }

            if (importer.isReadable)
            {
                report.Fail(path, "Read/Write is enabled. Nothing reads mesh data at runtime — NavMesh is baked, "
                    + "lightmap UVs are generated at import, colliders are cooked once — so this only keeps a "
                    + "second copy of every mesh in memory.");
            }

            if (importer.generateSecondaryUV != rule.GenerateLightmapUv)
            {
                report.Fail(path, $"Generate Lightmap UVs is {importer.generateSecondaryUV}, policy says "
                    + $"{rule.GenerateLightmapUv}. §03 makes darkness the lock on progress, so the static geometry "
                    + "has to be bakeable.");
            }

            // Only meaningful where UVs are actually generated, but checked unconditionally: a
            // stale Calculate left on a model whose lightmap UVs get switched on later would
            // empty it the moment they were, and the failure gives no hint of its own cause.
            if (importer.secondaryUVMarginMethod != AssetImportPolicy.RequiredLightmapMarginMethod)
            {
                report.Fail(path, $"Lightmap UV margin method is {importer.secondaryUVMarginMethod}, policy says "
                    + $"{AssetImportPolicy.RequiredLightmapMarginMethod}. Calculate sizes the margin for an object "
                    + "assumed to be about a unit across, and mesh-space in this project sits a hundred times below "
                    + "metres, so on the smallest props it asks for a margin wider than the UV square — the "
                    + "unwrapper aborts and Unity imports zero vertices without raising a warning.");
            }

            if (importer.secondaryUVPackMargin != AssetImportPolicy.LightmapUvPackMargin)
            {
                report.Warn(path, $"Lightmap UV pack margin is {importer.secondaryUVPackMargin}, policy says "
                    + $"{AssetImportPolicy.LightmapUvPackMargin}.");
            }

            if (importer.importCameras || importer.importLights)
            {
                report.Warn(path, "Imports cameras or lights. None of the exports contains either, so this can only "
                    + "add empty GameObjects to the hierarchy §12's assembler walks.");
            }
        }

        private static void CheckModelRig(Report report, string path, ModelImportRule rule, ModelImporter importer)
        {
            var expectedType = rule.Category == ModelCategory.CharacterHumanoid
                ? ModelImporterAnimationType.Human
                : rule.Category == ModelCategory.CharacterGeneric
                    ? ModelImporterAnimationType.Generic
                    : ModelImporterAnimationType.None;

            if (importer.animationType != expectedType)
            {
                report.Fail(path, $"Animation type is {importer.animationType}, policy says {expectedType}.");
            }

            if (rule.Category != ModelCategory.CharacterHumanoid && rule.Category != ModelCategory.CharacterGeneric)
            {
                return;
            }

            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar == null)
            {
                report.Fail(path, "Produced no Avatar, so nothing can be animated from it.");
                return;
            }

            if (!avatar.isValid)
            {
                report.Fail(path, "Its Avatar is invalid. §05 requires the other three players to be visible, which "
                    + "means their animator has to run — an invalid Avatar means it does not.");
            }

            if (rule.Category == ModelCategory.CharacterHumanoid)
            {
                if (!avatar.isHuman)
                {
                    report.Fail(path, "Imported without a humanoid Avatar. The player rig uses Unity's standard "
                        + "humanoid bone names and four players share one clip set, so retargeting is the whole "
                        + "point of the rig.");
                }

                CheckPlayerMountBones(report, path);
            }
            else if (avatar.isHuman)
            {
                report.Fail(path, "Imported as Humanoid. Its rig carries Jaw, Crest1..3, LeftScapulaSpur and "
                    + "Left/RightForearmExtra, and the forearm extras sit inside the arm chain. Unity's humanoid "
                    + "skeleton has no equivalent for the crest or the spur and does not retarget an extra link, so "
                    + "six bones' worth of animation is being dropped with no warning. It ships its own clips and "
                    + "never needs to retarget — it must be Generic.");
            }
        }

        /// <summary>
        /// The four player mount bones are not humanoid bones, so the Avatar does not protect
        /// them. They carry §05's flashlight-as-pointer, §03's objective carry, §08's 가방 and
        /// the first-person camera; losing one is losing a mechanic's attachment point.
        /// </summary>
        private static void CheckPlayerMountBones(Report report, string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
            {
                return;
            }

            var names = new HashSet<string>(
                root.GetComponentsInChildren<Transform>(true).Select(t => t.name),
                StringComparer.Ordinal);

            var missing = AssetImportPolicy.PlayerMountBones.Where(b => !names.Contains(b)).ToArray();
            if (missing.Length == 0)
            {
                return;
            }

            report.Fail(path, $"Missing attachment bone(s): {string.Join(", ", missing)}. These are not humanoid "
                + "bones, so the Avatar mapping does not keep them — Optimize Game Objects, or an avatar copied "
                + "from another model, strips exactly these. §05's flashlight-as-pointer, §03's objective carry and "
                + "§08's 가방 have nowhere to attach without them.");
        }

        private static void CheckModelColliders(Report report, string path, ModelImportRule rule, GameObject root)
        {
            var colliders = root.GetComponentsInChildren<MeshCollider>(true);

            if (rule.AddCollider && colliders.Length == 0)
            {
                report.Fail(path, "Policy asks for a mesh collider and none was generated. Level geometry with no "
                    + "collider is walked through, and §12's whole argument about path length versus sight line "
                    + "assumes walls stop people.");
                return;
            }

            if (!rule.AddCollider && colliders.Length > 0)
            {
                report.Warn(path, "Has a generated mesh collider but the policy does not ask for one. A character "
                    + "uses an authored capsule; a mesh collider on a skinned mesh would need re-cooking every "
                    + "frame.");
                return;
            }

            foreach (var collider in colliders)
            {
                if (collider.convex == rule.ConvexCollider)
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(path);
                if (rule.ConvexCollider)
                {
                    report.Fail(path, "Mesh collider is concave but the policy says convex. PhysX refuses a concave "
                        + "mesh collider on a non-kinematic Rigidbody, so anything §08 lets the player drop needs a "
                        + "convex hull to be droppable at all.");
                }
                else
                {
                    AssetImportPolicy.ConcaveProps.TryGetValue(name, out var reason);
                    report.Fail(path, "Mesh collider is convex but the policy says concave. "
                        + (reason ?? "Level geometry is concave by definition — a corridor is a hole, and a convex "
                            + "hull of a corridor is a solid block."));
                }
            }
        }

        private static void CheckModelAnimations(Report report, string path, ModelImportRule rule,
            ModelImporter importer)
        {
            if (!rule.ImportAnimation)
            {
                if (importer.importAnimation)
                {
                    report.Warn(path, "Imports animation although the policy does not ask for it.");
                }

                return;
            }

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
                if (clips == null || clips.Length == 0)
                {
                    report.Fail(path, "Has no animation clips. §05 needs the other players visible and animating; "
                        + "§06's state machine needs one clip per state.");
                    return;
                }

                report.Fail(path, "Its clip list is empty, so the takes import with Unity's defaults and every loop "
                    + "flag is whatever the default happens to be. Reimport under the policy.");
                return;
            }

            if (AssetImportPolicy.ExpectedAnimationClipCount.TryGetValue(path, out var expectedCount)
                && clips.Length != expectedCount)
            {
                report.Fail(path, $"Has {clips.Length} clip(s), expected {expectedCount}. A count that moved means "
                    + "a regenerated model gained or lost an animation, and §06 maps its clips one-to-one onto "
                    + "순찰 · 경계 · 추격 · 수색 · 정지 plus two events.");
            }

            foreach (var clip in clips)
            {
                var shortName = AssetImportModelPostprocessor.ShortClipName(clip.name);
                var shouldLoop = AssetImportPolicy.LoopingAnimationClips.Contains(shortName);
                var isOneShot = AssetImportPolicy.OneShotAnimationClips.Contains(shortName);

                if (!shouldLoop && !isOneShot)
                {
                    report.Warn(path, $"Clip '{clip.name}' is in neither the cycle list nor the one-shot list in "
                        + "AssetImportPolicy, so its loop flag is a guess.");
                    continue;
                }

                if (clip.loopTime == shouldLoop)
                {
                    continue;
                }

                if (shouldLoop)
                {
                    report.Fail(path, $"Clip '{clip.name}' is a cycle but Loop Time is off. It will freeze on its "
                        + "last frame while the state continues — for §06's 추격 that is a monster sliding toward "
                        + "the player with its legs stopped.");
                }
                else
                {
                    report.Fail(path, $"Clip '{clip.name}' is a one-shot but Loop Time is on. §09 makes death a "
                        + "persistent ghost state rather than an ending, so a looping Death replays for the rest of "
                        + "the match; a looping Stunned outlasts GameConstants.FlashStunSeconds "
                        + $"({GameConstants.FlashStunSeconds:0.#} s).");
                }
            }
        }

        /// <summary>
        /// Notes the two <c>.glb</c> preview copies. Unity 6 has no built-in glTF importer, so
        /// they import as opaque files and cannot produce a second Avatar for either character —
        /// harmless, but ASSETS.md §3.1 says they are for eyeballing in a viewer, so they are
        /// bytes in the repository rather than assets.
        /// </summary>
        private static void ReportPreviewCopies(Report report)
        {
            var previews = AssetImportPolicy.EnumerateAssetPaths(AssetImportPolicy.ModelRoot, "*.glb");
            if (previews.Count == 0)
            {
                return;
            }

            report.Note($"{previews.Count} .glb preview copy/copies present ({string.Join(", ", previews)}). Unity 6 "
                + "has no built-in glTF importer, so they produce no meshes and no Avatar — the pair of Avatars "
                + "ASSETS.md §3.1 warns about cannot happen. They are still shipped bytes with no consumer.");
        }

        // ── Shared helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Lists governed assets from disk rather than from <c>AssetDatabase.FindAssets</c>, so
        /// a file that failed to import is reported as failing instead of vanishing from the
        /// run — a missing asset and a broken asset look identical to a database query.
        /// </summary>
        private static List<string> ManagedAudioPaths()
        {
            return AssetImportPolicy.EnumerateAssetPaths(AssetImportPolicy.AudioRoot, "*.wav");
        }

        private static List<string> ManagedModelPaths()
        {
            return AssetImportPolicy.EnumerateAssetPaths(AssetImportPolicy.ModelRoot, "*.fbx");
        }

        private static IEnumerable<string> SelectedAssetPaths()
        {
            return Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
        }

        /// <summary>
        /// The model's bounding extents in metres, largest first. Sorted because the export
        /// leaves the Z-up to Y-up conversion as a −90° X rotation on the root child, which
        /// permutes two axes; a sorted triple is the same whichever way up the mesh data sits,
        /// so the comparison tests size without asserting an orientation.
        /// <para>
        /// <paramref name="emptyMeshCount"/> is reported separately rather than folded into the
        /// measurement, because a mesh that imported with no vertices measures zero and zero is
        /// indistinguishable from "wrong scale" once it is a single number. Two of the props
        /// were emptied by the lightmap unwrapper and reported as unit-scale errors for exactly
        /// that reason.
        /// </para>
        /// </summary>
        /// <param name="root">The imported model root.</param>
        /// <param name="meshCount">How many mesh assets the import produced.</param>
        /// <param name="emptyMeshCount">How many of those have no vertices.</param>
        private static float[] SortedExtents(GameObject root, out int meshCount, out int emptyMeshCount)
        {
            var extents = Vector3.zero;
            meshCount = 0;
            emptyMeshCount = 0;

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Accumulate(filter.sharedMesh, filter.transform, ref extents, ref meshCount, ref emptyMeshCount);
            }

            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Accumulate(skinned.sharedMesh, skinned.transform, ref extents, ref meshCount, ref emptyMeshCount);
            }

            if (meshCount == 0)
            {
                return null;
            }

            var sorted = new[] { extents.x, extents.y, extents.z };
            Array.Sort(sorted);
            Array.Reverse(sorted);
            return sorted;
        }

        /// <summary>
        /// Folds one mesh into the running extents. An empty mesh is counted and skipped rather
        /// than contributing a zero, so a model that imported one good mesh and one empty one
        /// still reports the good one's size alongside the emptiness.
        /// </summary>
        private static void Accumulate(Mesh mesh, Transform owner, ref Vector3 extents,
            ref int meshCount, ref int emptyMeshCount)
        {
            if (mesh == null)
            {
                return;
            }

            meshCount++;
            if (mesh.vertexCount == 0)
            {
                emptyMeshCount++;
                return;
            }

            var size = mesh.bounds.size;
            var scale = owner != null ? owner.lossyScale : Vector3.one;
            extents.x = Mathf.Max(extents.x, Mathf.Abs(size.x * scale.x));
            extents.y = Mathf.Max(extents.y, Mathf.Abs(size.y * scale.y));
            extents.z = Mathf.Max(extents.z, Mathf.Abs(size.z * scale.z));
        }

        /// <summary>
        /// Formats a measured triple at micrometre precision.
        /// <para>
        /// Six decimals, and all three axes rather than the largest alone. The message this
        /// replaces printed one number at four decimals, which rendered a §08 loot piece as
        /// "0 m" — a figure nobody could act on, and one that read as a scale error rather than
        /// as the empty mesh it was. A measurement is only evidence if its precision survives
        /// the smallest thing being measured, and the smallest thing here is a 2.16 cm ring.
        /// </para>
        /// </summary>
        private static string DescribeExtents(float[] extents)
        {
            return $"{extents[0]:0.000000} × {extents[1]:0.000000} × {extents[2]:0.000000} m";
        }

        private static MapKitManifest LoadMapKitManifest(Report report)
        {
            var path = AssetImportPolicy.ModelRoot + "/MapKit/MapKit.manifest.json";
            var absolute = AssetImportPolicy.ToAbsolutePath(path);
            if (absolute == null || !File.Exists(absolute))
            {
                report.Warn(path, "Missing, so kit pieces can only be checked against a plausibility band instead "
                    + "of against §12's authored footprints.");
                return null;
            }

            try
            {
                var manifest = JsonUtility.FromJson<MapKitManifest>(File.ReadAllText(absolute));
                if (manifest == null || manifest.pieces == null || manifest.pieces.Length == 0)
                {
                    report.Warn(path, "Parsed with no pieces. Kit bounds fall back to the plausibility band.");
                    return null;
                }

                if (!Mathf.Approximately(manifest.grid_metres, 2.5f))
                {
                    report.Warn(path, $"Declares a {manifest.grid_metres:0.###} m grid. §12's kit is authored on "
                        + "2.5 m, and every footprint in the manifest is a multiple of it.");
                }

                return manifest;
            }
            catch (Exception ex)
            {
                report.Warn(path, "Could not be parsed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Field names mirror the JSON keys in <c>MapKit.manifest.json</c> exactly, because
        /// <c>JsonUtility</c> matches on name. Unknown keys in the file are ignored, so this
        /// deliberately reads only the two things a bounds check needs.
        /// </summary>
        [Serializable]
        private sealed class MapKitManifest
        {
            public float grid_metres;
            public MapKitPiece[] pieces;
        }

        [Serializable]
        private sealed class MapKitPiece
        {
            public string file;
            public float[] footprint_metres;
            public float height_metres;
        }
    }
}
