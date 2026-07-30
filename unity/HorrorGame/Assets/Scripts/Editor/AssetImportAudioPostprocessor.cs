using System;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Applies <see cref="AssetImportPolicy"/> to every clip under <c>Assets/Audio</c> as it
    /// imports.
    /// <para>
    /// This exists because of one sentence in ASSETS.md §1: Unity does not spatialise a
    /// stereo <c>AudioClip</c>. It does not error and it does not warn — a two-channel
    /// footstep on a 3D source simply plays at a constant level from nowhere. §04's 청음사
    /// has exactly one ability, reading the monster's 위치 · 거리 · 이동 방향 by ear; §13's
    /// proximity voice has no distance logic at all because the 3D source is the logic; §09's
    /// ghost has one channel, a rattle inside <c>GameConstants.GhostRattleRange</c>. All
    /// three are switched off by the same unchecked box, and none of them reports a fault.
    /// So force-to-mono is enforced here rather than trusted to a human.
    /// </para>
    /// <para>
    /// There are no <c>.meta</c> files in the repository yet, which means Unity has never
    /// imported these 166 clips and every setting is currently at a default. Two of those
    /// defaults are wrong for this project: <c>Force To Mono</c> is off, and
    /// <c>AudioSource.spatialBlend</c> starts at 2D. The first is fixed below. The second is
    /// not an import setting — see the remarks on <see cref="AssetImportPolicy"/> for where
    /// it is handled instead.
    /// </para>
    /// </summary>
    public sealed class AssetImportAudioPostprocessor : AssetPostprocessor
    {
        /// <summary>
        /// Reimports every governed clip when the policy version moves. Without this a rule
        /// change would only reach clips someone happened to touch afterwards, which is a
        /// slower version of not having a policy.
        /// </summary>
        public override uint GetVersion()
        {
            return AssetImportPolicy.PolicyVersion;
        }

        /// <summary>Runs late so this policy is the last writer, not the first.</summary>
        public override int GetPostprocessOrder()
        {
            return AssetImportPolicy.PostprocessOrder;
        }

        private void OnPreprocessAudio()
        {
            if (!AssetImportPolicy.IsManagedAudio(assetPath))
            {
                return;
            }

            var importer = assetImporter as AudioImporter;
            if (importer == null)
            {
                return;
            }

            if (AssetImportPolicy.IsExcluded(assetPath, importer))
            {
                return;
            }

            // The length comes from the shared seam, never from a local read, so the settings
            // written here are resolved from exactly the same input the validator checks them
            // against. See AssetImportPolicy.TryResolveAudioFromSource.
            if (!AssetImportPolicy.TryResolveAudioFromSource(assetPath, out var rule, out var header))
            {
                // Deliberately writes nothing. Load type and codec both key off length, and a
                // stand-in length is a decision made on a number nobody measured — the failure
                // that put nearly every clip in the wrong band while looking healthy. Leaving
                // the settings untouched keeps the asset visibly unpoliced, which is what the
                // validator reports on.
                Debug.LogError($"[AssetImport] Could not read a WAVE header from {assetPath}, so its load type "
                    + "and compression cannot be resolved. No import settings were written — the policy does not "
                    + "guess a length, because a guess is indistinguishable from a measurement once it is in the "
                    + ".meta. Re-export the clip as 48 kHz PCM WAVE.");
                return;
            }

            var sourceChannels = header.Channels;
            var sourceRate = header.SampleRate;

            // A positional clip that is stereo at source still imports mono because of the
            // line below, so playback is safe either way. It is still worth saying out loud:
            // the generator that produced it disagrees with the design, and ASSETS.md §4.3's
            // channel-policy audit is the place that should have caught it.
            if (rule.Positional && sourceChannels > 1)
            {
                Debug.LogWarning($"[AssetImport] {assetPath} is {sourceChannels}-channel at source but is a "
                    + $"{rule.Role} clip. Force To Mono will make it spatialise, but the generator should emit "
                    + "mono — ASSETS.md §4.3 check 5 is the audit that pins this.");
            }

            if (sourceRate > 0 && sourceRate != AssetImportPolicy.ExpectedSampleRate)
            {
                Debug.LogWarning($"[AssetImport] {assetPath} is {sourceRate} Hz; every generator emits "
                    + $"{AssetImportPolicy.ExpectedSampleRate} Hz. The rate is preserved as-is, so §12's "
                    + "material separation is being measured on a different clip than the audit measured.");
            }

            ApplyImporterFlags(importer, rule);
            ApplySampleSettings(importer, rule);
            ClearPlatformOverrides(importer);
            RecordUserData(importer, rule);
        }

        /// <summary>
        /// Confirms after the fact that the clip Unity actually produced is mono when the
        /// role needs it. <c>forceToMono</c> is a request; this is the receipt, and it is
        /// checked on every import rather than only when someone runs the validator.
        /// </summary>
        private void OnPostprocessAudio(AudioClip clip)
        {
            if (clip == null || !AssetImportPolicy.IsManagedAudio(assetPath))
            {
                return;
            }

            var importer = assetImporter as AudioImporter;
            if (importer == null || AssetImportPolicy.IsExcluded(assetPath, importer))
            {
                return;
            }

            // Role, not the full rule: this receipt is about channels, and channel policy
            // follows from role alone. Resolving a length here would mean a second length
            // source in the same file, which is the divergence PolicyVersion 2 removed.
            var role = AssetImportPolicy.ResolveRole(assetPath);
            if (AssetImportPolicy.IsPositionalRole(role) && clip.channels != 1)
            {
                Debug.LogError($"[AssetImport] {assetPath} imported with {clip.channels} channels and is a "
                    + $"{role} clip. Unity will not spatialise it: no attenuation, no panning, no direction. "
                    + "That disables §04's Listener, §13's proximity voice and §09's ghost rattle at once, "
                    + "silently. Fix the import settings before shipping anything that depends on hearing.");
            }
        }

        /// <summary>
        /// Sets the flags that live on the importer itself rather than in per-platform
        /// sample settings. Each assignment is guarded so an unchanged import does not mark
        /// the importer dirty and trigger another one.
        /// </summary>
        private static void ApplyImporterFlags(AudioImporter importer, AudioImportRule rule)
        {
            if (importer.forceToMono != rule.ForceToMono)
            {
                importer.forceToMono = rule.ForceToMono;
            }

            if (importer.loadInBackground != rule.LoadInBackground)
            {
                importer.loadInBackground = rule.LoadInBackground;
            }

            // Ambisonic decoding expects a B-format clip. None of these are, and enabling it
            // on a plain mono clip routes it through the ambisonic decoder instead of the
            // panner — another way to lose direction without an error.
            if (importer.ambisonic)
            {
                importer.ambisonic = false;
            }
        }

        private static void ApplySampleSettings(AudioImporter importer, AudioImportRule rule)
        {
            var settings = importer.defaultSampleSettings;
            var changed = false;

            if (settings.loadType != rule.LoadType)
            {
                settings.loadType = rule.LoadType;
                changed = true;
            }

            if (settings.compressionFormat != rule.Compression)
            {
                settings.compressionFormat = rule.Compression;
                changed = true;
            }

            // Quality is meaningless for PCM, but leaving a stale value behind makes the
            // Inspector read as if a codec were involved.
            var quality = rule.Compression == AudioCompressionFormat.PCM ? 1f : rule.Quality;
            if (!Mathf.Approximately(settings.quality, quality))
            {
                settings.quality = quality;
                changed = true;
            }

            // Preserve, never optimise. Unity's rate optimiser inspects the content and
            // decides; §12's gravel identity is a 6.2 kHz noise band whose separation from
            // concrete already measures 1.396× against a 1.4× floor (ASSETS.md §4.4), so
            // there is nothing left to spend on a heuristic.
            if (settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate)
            {
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                changed = true;
            }

            if (settings.preloadAudioData != rule.Preload)
            {
                settings.preloadAudioData = rule.Preload;
                changed = true;
            }

            if (changed)
            {
                importer.defaultSampleSettings = settings;
            }
        }

        /// <summary>
        /// Removes per-platform overrides so the shared policy is what ships.
        /// <para>
        /// An override is invisible unless the Inspector happens to be open on the right
        /// platform tab, which makes it the perfect hiding place for a stereo footstep. If
        /// one is genuinely wanted, the asset belongs behind the manual-import marker.
        /// </para>
        /// </summary>
        private static void ClearPlatformOverrides(AudioImporter importer)
        {
            foreach (var platform in AssetImportPolicy.AudioOverridePlatforms)
            {
                if (importer.ContainsSampleSettingsOverride(platform))
                {
                    importer.ClearSampleSettingOverride(platform);
                }
            }
        }

        /// <summary>
        /// Writes the two decisions the importer cannot enforce — spatial blend and loop —
        /// into the importer's user data, where they land in the <c>.meta</c> file and so
        /// into version control. Prefab and scene authoring reads them back through
        /// <c>AssetImportPolicy.ResolveAudio</c>; the recorded copy is what makes a
        /// mismatch reviewable in a diff.
        /// </summary>
        private static void RecordUserData(AudioImporter importer, AudioImportRule rule)
        {
            var desired = AssetImportPolicy.BuildAudioUserData(rule);
            if (!string.Equals(importer.userData, desired, StringComparison.Ordinal))
            {
                importer.userData = desired;
            }
        }
    }
}
