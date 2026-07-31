using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// The build. One code path, reachable from the editor menu and from
    /// <c>-executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine</c>.
    /// <para>
    /// Menu items exist so a developer can produce a playable build without remembering an
    /// invocation; the batch entry point exists so CI produces the same thing. They share
    /// everything below <see cref="Run"/> on purpose — a pipeline where the two diverge is a
    /// pipeline that is green locally and red in CI, or worse, the other way round.
    /// </para>
    /// <para>
    /// Output is <c>dist/&lt;platform&gt;/</c> and nothing else, per platform, wiped before
    /// each build. See <see cref="BuildPipelinePaths.CleanOutputDirectory"/> for why.
    /// </para>
    /// </summary>
    public static class BuildPipelineRunner
    {
        /// <summary>Everything asked for was produced.</summary>
        public const int ExitSuccess = 0;

        /// <summary>An exception nobody predicted. The log has the stack trace.</summary>
        public const int ExitUnexpected = 1;

        /// <summary>Bad or missing command-line arguments; nothing was built.</summary>
        public const int ExitBadArguments = 2;

        /// <summary>No scenes, or a scene listed in Build Settings is not on disk.</summary>
        public const int ExitSceneProblem = 3;

        /// <summary>Unity reported the player build as failed or cancelled.</summary>
        public const int ExitBuildFailed = 4;

        /// <summary><c>-buildRequireIl2cpp</c> was passed and this host cannot produce IL2CPP.</summary>
        public const int ExitIl2CppRequired = 5;

        /// <summary>
        /// The editor is not in a state where the produced player would match the sources:
        /// scripts failing to compile, still compiling, or play mode running.
        /// </summary>
        public const int ExitScriptCompilationFailed = 7;

        /// <summary>The target's build support module is not installed in this editor.</summary>
        public const int ExitTargetNotInstalled = 8;

        // 6 and 9 belong to BuildPipelineTestRunner (test failure / no tests found). The two
        // entry points share one numbering scheme so a CI script can switch on the code
        // without caring which step produced it.

        /// <summary>Written into <c>dist/</c> after every run, so the last result survives the log.</summary>
        private const string SummaryFileName = "last-build-summary.txt";

        [MenuItem("Horror/Build/Windows x64 — Development", priority = 20)]
        public static void MenuWindowsDevelopment()
        {
            RunFromMenu(BuildPipelineOptions.ForMenu(BuildPlatformId.WindowsX64, BuildConfigurationId.Development));
        }

        [MenuItem("Horror/Build/Windows x64 — Release", priority = 21)]
        public static void MenuWindowsRelease()
        {
            RunFromMenu(BuildPipelineOptions.ForMenu(BuildPlatformId.WindowsX64, BuildConfigurationId.Release));
        }

        [MenuItem("Horror/Build/macOS universal — Development", priority = 40)]
        public static void MenuMacDevelopment()
        {
            RunFromMenu(BuildPipelineOptions.ForMenu(BuildPlatformId.MacUniversal, BuildConfigurationId.Development));
        }

        [MenuItem("Horror/Build/macOS universal — Release", priority = 41)]
        public static void MenuMacRelease()
        {
            RunFromMenu(BuildPipelineOptions.ForMenu(BuildPlatformId.MacUniversal, BuildConfigurationId.Release));
        }

        [MenuItem("Horror/Build/Windows + macOS — Release", priority = 60)]
        public static void MenuAllRelease()
        {
            RunFromMenu(BuildPipelineOptions.ForMenu(
                new[] { BuildPlatformId.WindowsX64, BuildPlatformId.MacUniversal },
                BuildConfigurationId.Release));
        }

        /// <summary>
        /// Reports what this machine can and cannot build, without building anything. The
        /// first question about a failed build is always "what did it think it was doing",
        /// and this answers it in one menu click or one <c>-executeMethod</c>.
        /// </summary>
        [MenuItem("Horror/Build/Report Build Environment", priority = 80)]
        public static void ReportEnvironment()
        {
            var text = new StringBuilder();
            text.AppendLine("[BuildPipeline] Build environment");
            text.AppendLine("  unity            : " + Application.unityVersion);
            text.AppendLine("  host             : " + BuildPipelineBackend.HostDescription);
            text.AppendLine("  project root     : " + BuildPipelinePaths.Normalize(BuildPipelinePaths.ProjectRoot));
            text.AppendLine("  repository root  : " + BuildPipelinePaths.Normalize(BuildPipelinePaths.RepositoryRoot));
            text.AppendLine("  output root      : " + BuildPipelinePaths.Normalize(BuildPipelinePaths.DefaultOutputRoot));

            var version = BuildPipelineVersion.Resolve(string.Empty);
            text.AppendLine("  version          : " + version.Version + "  (" + version.VersionSource + ")");
            text.AppendLine("  git              : " + version.CommitDescription + " on " + version.Branch
                + ", build number " + version.BuildNumber);

            var options = BuildPipelineOptions.ForMenu(BuildPlatformId.WindowsX64, BuildConfigurationId.Release);
            text.AppendLine("  steam app id     : " + options.SteamAppId + "  (" + options.SteamAppIdSource + ")");
            text.AppendLine("  script compile   : "
                + (EditorUtility.scriptCompilationFailed ? "FAILING" : "ok"));

            foreach (var platform in new[]
                     {
                         BuildPlatformId.WindowsX64,
                         BuildPlatformId.MacUniversal,
                         BuildPlatformId.MacAppleSilicon,
                         BuildPlatformId.MacIntel,
                     })
            {
                var target = BuildPipelineTargets.ToBuildTarget(platform);
                var supported = UnityEditor.BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target);
                var il2cpp = BuildPipelineBackend.CanHostProduceIl2Cpp(platform);
                text.AppendLine("  " + BuildPipelineTargets.FolderName(platform).PadRight(17)
                    + ": module " + (supported ? "installed" : "MISSING  ")
                    + ", release backend " + (il2cpp ? "IL2CPP" : "Mono (fallback)"));
            }

            if (BuildPipelineScenes.TryCollect(out var scenes, out var sceneError))
            {
                text.AppendLine("  scenes           : " + scenes.Length + " (see the list logged above)");
            }
            else
            {
                text.AppendLine("  scenes           : NONE — " + sceneError);
            }

            Debug.Log(text.ToString());
        }

        /// <summary>Opens <c>dist/</c> in Finder or Explorer.</summary>
        [MenuItem("Horror/Build/Open Output Folder", priority = 81)]
        public static void OpenOutputFolder()
        {
            var root = BuildPipelinePaths.DefaultOutputRoot;
            Directory.CreateDirectory(root);
            EditorUtility.RevealInFinder(root);
        }

        /// <summary>
        /// Batch-mode entry point:
        /// <code>
        /// Unity -batchmode -nographics -projectPath unity/HorrorGame \
        ///       -executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine \
        ///       -buildPlatform win64 -buildConfig release -logFile -
        /// </code>
        /// <para>
        /// Exits the editor with a specific code — see the <c>Exit*</c> constants — because a
        /// CI step that cannot tell "built nothing" from "built everything" is decoration.
        /// Do not pass <c>-quit</c>: this method owns the exit code, and <c>-quit</c> would
        /// override a failure with 0.
        /// </para>
        /// </summary>
        public static void BuildFromCommandLine()
        {
            var exitCode = ExitUnexpected;
            try
            {
                if (!BuildPipelineOptions.TryParse(Environment.GetCommandLineArgs(), out var options, out var error))
                {
                    Debug.LogError("[BuildPipeline] " + error + "\n" + UsageText());
                    exitCode = ExitBadArguments;
                    return;
                }

                exitCode = Run(options);
            }
            catch (Exception exception)
            {
                Debug.LogError("[BuildPipeline] Unhandled exception:\n" + exception);
                exitCode = ExitUnexpected;
            }
            finally
            {
                Debug.Log("[BuildPipeline] Exiting with code " + exitCode + ".");
                EditorApplication.Exit(exitCode);
            }
        }

        /// <summary>
        /// Runs one request and returns the exit code. Public so a future editor window or a
        /// packaging step can call the pipeline without going through the command line.
        /// </summary>
        public static int Run(BuildPipelineOptions options)
        {
            var totalStopwatch = Stopwatch.StartNew();

            Debug.Log("[BuildPipeline] Starting: " + options.CommandLineEcho
                + "\n  configuration : " + options.Configuration
                + "\n  platforms     : " + string.Join(", ", DescribePlatforms(options))
                + "\n  output root   : " + BuildPipelinePaths.Normalize(options.OutputRoot)
                + "\n  clean         : " + options.Clean
                + "\n  unity         : " + Application.unityVersion
                + "\n  host          : " + BuildPipelineBackend.HostDescription);

            if (!Preflight(out var preflightError))
            {
                Debug.LogError("[BuildPipeline] " + preflightError);
                return ExitScriptCompilationFailed;
            }

            if (!BuildPipelineScenes.TryCollect(out var scenes, out var sceneError))
            {
                Debug.LogError("[BuildPipeline] " + sceneError);
                return ExitSceneProblem;
            }

            var version = BuildPipelineVersion.Resolve(options.VersionOverride);
            version.StampPlayerSettings();

            if (options.IsReleaseWithDevelopmentAppId)
            {
                Debug.LogWarning("[BuildPipeline] Release build carrying §13's development App ID ("
                    + BuildPipelineOptions.DefaultSteamAppId + ", Spacewar). Correct until §14 step 7 "
                    + "buys the real one; wrong the moment this is uploaded to a depot. Set it with "
                    + "-buildSteamAppId, STEAM_APP_ID or a steam_appid.txt at the repository root.");
            }

            // Checked for every platform before the first one is built. The answer is known at
            // second zero, and finding out after a fifteen-minute IL2CPP link that the second
            // platform was never possible wastes the whole run.
            if (options.RequireIl2Cpp)
            {
                foreach (var platform in options.Platforms)
                {
                    var check = BuildPipelineBackend.Decide(platform, options.Configuration);
                    if (!check.IsForcedMonoFallback)
                    {
                        continue;
                    }

                    Debug.LogError("[BuildPipeline] -buildRequireIl2cpp was passed and IL2CPP is not "
                        + "available for " + BuildPipelineTargets.DisplayName(platform) + " on "
                        + BuildPipelineBackend.HostDescription + ". Nothing was built.\n"
                        + check.ShippingWarning);
                    return ExitIl2CppRequired;
                }
            }

            var reports = new List<BuildPipelineReport>();
            var exitCode = ExitSuccess;

            foreach (var platform in options.Platforms)
            {
                var target = BuildPipelineTargets.ToBuildTarget(platform);
                if (!UnityEditor.BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                {
                    Debug.LogError("[BuildPipeline] Build support for " + target + " is not installed in this "
                        + "editor (" + Application.unityVersion + "). Install the module through Unity Hub: "
                        + "Installs > this version > Add modules.");
                    exitCode = ExitTargetNotInstalled;
                    break;
                }

                var decision = BuildPipelineBackend.Decide(platform, options.Configuration);
                BuildPipelineBackend.LogDecision(platform, decision);

                if (decision.IsForcedMonoFallback && options.RequireIl2Cpp)
                {
                    Debug.LogError("[BuildPipeline] -buildRequireIl2cpp was passed and IL2CPP is not "
                        + "available here, so nothing was built for "
                        + BuildPipelineTargets.DisplayName(platform) + ".");
                    exitCode = ExitIl2CppRequired;
                    break;
                }

                var report = BuildOne(options, platform, decision, scenes, version);
                reports.Add(report);

                if (!report.Succeeded)
                {
                    // Stop at the first failure. Windows is ordered first, so its player is
                    // already on disk when a macOS build fails, and the reverse never wastes a
                    // ten-minute IL2CPP link on a build nobody can use.
                    exitCode = ExitBuildFailed;
                    break;
                }
            }

            totalStopwatch.Stop();
            WriteRunSummary(options, reports, totalStopwatch.Elapsed, exitCode);
            return exitCode;
        }

        /// <summary>
        /// Builds one platform and writes its report, whether it succeeded or not.
        /// </summary>
        private static BuildPipelineReport BuildOne(
            BuildPipelineOptions options,
            BuildPlatformId platform,
            BuildBackendDecision decision,
            string[] scenes,
            BuildPipelineVersion version)
        {
            var outputDirectory = options.OutputDirectoryFor(platform);
            var playerPath = options.PlayerPathFor(platform);

            var report = new BuildPipelineReport
            {
                Platform = platform,
                Configuration = options.Configuration,
                BackendName = decision.BackendName,
                BackendReason = decision.Reason,
                MonoFallback = decision.IsForcedMonoFallback,
                ShippingWarning = decision.ShippingWarning,
                UnityVersion = Application.unityVersion,
                HostDescription = BuildPipelineBackend.HostDescription,
                Version = version.Version,
                VersionSource = version.VersionSource,
                BuildNumber = version.BuildNumber,
                GitCommit = version.Commit,
                GitBranch = version.Branch,
                GitDirty = version.Dirty,
                SteamAppId = options.SteamAppId,
                SteamAppIdSource = options.SteamAppIdSource,
                Scenes = scenes,
                OutputDirectory = outputDirectory,
                PlayerPath = playerPath,
                TimestampUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (options.Clean)
                {
                    BuildPipelinePaths.CleanOutputDirectory(outputDirectory, options.OutputRoot);
                }
                else
                {
                    Directory.CreateDirectory(outputDirectory);
                    report.Notes.Add("-buildNoClean: the output folder was not emptied, so files from a "
                        + "previous build may still be in it and the reported size may not be this build's.");
                }

                var target = BuildPipelineTargets.ToBuildTarget(platform);
                if (EditorUserBuildSettings.activeBuildTarget != target
                    && !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target))
                {
                    // Not fatal: BuildPlayer can still target it. But a mismatch means assets were
                    // imported for another platform, and the first symptom is a wrong-looking build.
                    report.Notes.Add("Could not switch the active build target to " + target
                        + "; assets may still be imported for "
                        + EditorUserBuildSettings.activeBuildTarget + ".");
                    Debug.LogWarning("[BuildPipeline] " + report.Notes[report.Notes.Count - 1]);
                }

                Debug.Log("[BuildPipeline] Building " + BuildPipelineTargets.DisplayName(platform) + " ("
                    + options.Configuration + ", " + decision.BackendName + ") to "
                    + BuildPipelinePaths.Normalize(playerPath));

                using (var scope = BuildPipelineSettingsScope.Apply(platform, options.Configuration, decision.Backend))
                {
                    var playerOptions = new BuildPlayerOptions
                    {
                        scenes = scenes,
                        locationPathName = playerPath,
                        target = target,
                        targetGroup = BuildTargetGroup.Standalone,
                        options = BuildPipelineSettingsScope.OptionsFor(options.Configuration),
                        extraScriptingDefines = DefinesFor(options.Configuration),
                    };

                    var unityReport = UnityEditor.BuildPipeline.BuildPlayer(playerOptions);
                    var summary = unityReport.summary;

                    // ReportBuildMessages runs below, after the summary fields are read.
                    // Without it this pipeline reported "Error building Player: 2 errors"
                    // and nothing else — the counts were recorded and the messages, which
                    // are the only part anyone can act on, were dropped on the floor.

                    report.Result = summary.result.ToString();
                    report.Succeeded = summary.result == BuildResult.Succeeded;
                    report.Errors = summary.totalErrors;
                    report.Warnings = summary.totalWarnings;
                    report.UnityReportedSizeBytes = summary.totalSize;
                    report.UnityReportedDuration = summary.totalTime;
                    report.MacArchitecture = scope.MacArchitectureApplied;

                    foreach (var note in scope.Notes)
                    {
                        report.Notes.Add(note);
                    }

                    report.FatalErrors = ReportBuildMessages(unityReport, report);

                    // This is the pipeline's own version of BuildOptions.StrictMode, kept
                    // because Unity's fails without naming what failed — see
                    // BuildPipelineSettingsScope.OptionsFor. An error that is not a named
                    // known defect fails the build even when Unity called it a success.
                    if (report.Succeeded && report.FatalErrors > 0)
                    {
                        report.Succeeded = false;
                        report.Result = "Succeeded with " + report.FatalErrors
                            + " error(s) — treated as failed";
                    }
                }
            }
            catch (Exception exception)
            {
                report.Result = "Exception";
                report.Succeeded = false;
                report.Notes.Add("Exception during the build: " + exception.Message);
                Debug.LogError("[BuildPipeline] " + BuildPipelineTargets.DisplayName(platform)
                    + " threw:\n" + exception);
            }

            stopwatch.Stop();
            report.Duration = stopwatch.Elapsed;

            if (report.Succeeded)
            {
                WriteSteamAppIdFile(options, outputDirectory, report);
            }

            report.SizeBytes = BuildPipelinePaths.DirectorySizeBytes(outputDirectory);
            report.OutputEntries.AddRange(BuildPipelinePaths.TopLevelEntries(outputDirectory));

            WriteFallbackMarker(report, outputDirectory);

            var reportPath = report.WriteTo(outputDirectory);
            Debug.Log((report.Succeeded ? "[BuildPipeline] Built " : "[BuildPipeline] FAILED ")
                + BuildPipelineTargets.DisplayName(platform) + " in "
                + report.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s, "
                + BuildPipelinePaths.ToMegabytes(report.SizeBytes).ToString("0.00", CultureInfo.InvariantCulture)
                + " MB. Report: " + BuildPipelinePaths.Normalize(reportPath));

            return report;
        }

        /// <summary>
        /// Refuses to build from a state where the produced player would not match the source.
        /// </summary>
        private static bool Preflight(out string error)
        {
            error = string.Empty;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            {
                error = "The editor is in play mode. Stop it first — building from play mode "
                    + "captures scene state that was modified at runtime.";
                return false;
            }

            if (EditorApplication.isCompiling)
            {
                error = "Scripts are still compiling. Nothing was built, because the player would "
                    + "have been made from whichever assemblies happened to be on disk.";
                return false;
            }

            if (EditorUtility.scriptCompilationFailed)
            {
                error = "Scripts do not compile. Fix the compile errors above; a player built from "
                    + "stale assemblies is worse than no player at all.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extra defines for this configuration, passed per build rather than written into
        /// <c>ProjectSettings.asset</c>. Gameplay code can gate developer-only affordances on
        /// <c>HORROR_DEV_BUILD</c> and be certain they are gone from a release player.
        /// </summary>
        private static string[] DefinesFor(BuildConfigurationId configuration)
        {
            return configuration == BuildConfigurationId.Development
                ? new[] { "HORROR_DEV_BUILD" }
                : new[] { "HORROR_RELEASE_BUILD" };
        }

        /// <summary>
        /// Writes <c>steam_appid.txt</c> beside a development player so it can initialise
        /// Steamworks when launched directly from <c>dist/</c> instead of through the client —
        /// which is how §14 steps 4 and 5 get tested.
        /// <para>
        /// Release builds deliberately do not get the file. Valve's own guidance is to ship
        /// without it: when it is present it overrides the App ID the client provides, so a
        /// stale copy in a depot points a released game at the wrong app.
        /// </para>
        /// </summary>
        private static void WriteSteamAppIdFile(
            BuildPipelineOptions options,
            string outputDirectory,
            BuildPipelineReport report)
        {
            if (options.Configuration != BuildConfigurationId.Development)
            {
                report.Notes.Add("No " + BuildPipelineOptions.SteamAppIdFileName + " was written: a release "
                    + "player must take its App ID from the Steam client, not from a file in the depot. "
                    + "The App ID above is recorded for reference only.");
                return;
            }

            var appIdPath = Path.Combine(outputDirectory, BuildPipelineOptions.SteamAppIdFileName);
            File.WriteAllText(appIdPath, options.SteamAppId + "\n");
            report.Notes.Add(BuildPipelineOptions.SteamAppIdFileName + " written with App ID "
                + options.SteamAppId + " (" + options.SteamAppIdSource + ") so the player can start "
                + "Steamworks outside the client.");
        }

        /// <summary>
        /// Drops the do-not-ship marker into the output folder on a forced Mono fallback, and
        /// removes a stale one otherwise. The marker is the last line of defence: whoever runs
        /// <c>steamcmd</c> sees a file named MONO-FALLBACK-DO-NOT-SHIP.txt in the depot root
        /// even if every log went unread.
        /// </summary>
        private static void WriteFallbackMarker(BuildPipelineReport report, string outputDirectory)
        {
            var markerPath = Path.Combine(outputDirectory, BuildPipelineBackend.FallbackMarkerFileName);

            if (!report.MonoFallback)
            {
                if (File.Exists(markerPath))
                {
                    File.Delete(markerPath);
                }

                return;
            }

            var text = new StringBuilder();
            text.AppendLine(report.ShippingWarning);
            text.AppendLine();
            text.AppendLine("build : " + report.Version + " (" + report.GitCommit + ")");
            text.AppendLine("when  : " + report.TimestampUtc);
            text.AppendLine("config: " + report.Configuration + " / " + report.BackendName);
            text.AppendLine();
            text.AppendLine("Delete this file only after replacing this folder with an IL2CPP build.");
            File.WriteAllText(markerPath, text.ToString());
        }

        /// <summary>
        /// Prints the per-platform table and writes it to <c>dist/</c>. The table is the thing
        /// a human reads after a ten-minute run, so it repeats the fallback warning rather
        /// than assuming anyone scrolled up.
        /// </summary>
        private static void WriteRunSummary(
            BuildPipelineOptions options,
            List<BuildPipelineReport> reports,
            TimeSpan total,
            int exitCode)
        {
            var text = new StringBuilder();
            text.AppendLine("HorrorGame build run — " + DateTime.UtcNow.ToString(
                "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            text.AppendLine("arguments : " + options.CommandLineEcho);
            text.AppendLine("unity     : " + Application.unityVersion + " on "
                + BuildPipelineBackend.HostDescription);
            text.AppendLine("total     : " + total.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");
            text.AppendLine("exit code : " + exitCode);
            text.AppendLine();

            if (reports.Count == 0)
            {
                text.AppendLine("Nothing was built.");
            }

            foreach (var report in reports)
            {
                text.AppendLine("  " + report.OneLineSummary);
            }

            foreach (var report in reports)
            {
                if (report.MonoFallback)
                {
                    text.AppendLine();
                    text.AppendLine(report.ShippingWarning);
                }
            }

            var summary = text.ToString();
            if (exitCode == ExitSuccess)
            {
                Debug.Log("[BuildPipeline]\n" + summary);
            }
            else
            {
                Debug.LogError("[BuildPipeline] Run failed.\n" + summary);
            }

            try
            {
                Directory.CreateDirectory(options.OutputRoot);
                File.WriteAllText(Path.Combine(options.OutputRoot, SummaryFileName), summary);
            }
            catch (IOException exception)
            {
                Debug.LogWarning("[BuildPipeline] Could not write " + SummaryFileName + ": " + exception.Message);
            }
        }

        /// <summary>
        /// Menu path: save modified scenes first, then run without touching the exit code —
        /// calling <see cref="EditorApplication.Exit"/> from a menu item would close the editor
        /// under the developer who clicked it.
        /// </summary>
        private static void RunFromMenu(BuildPipelineOptions options)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[BuildPipeline] Cancelled: unsaved scene changes were not saved, and an "
                    + "unsaved change is not in the build.");
                return;
            }

            var exitCode = Run(options);
            if (exitCode != ExitSuccess)
            {
                Debug.LogError("[BuildPipeline] Build finished with exit code " + exitCode
                    + ". In CI this run would have failed the job.");
            }
        }

        private static List<string> DescribePlatforms(BuildPipelineOptions options)
        {
            var names = new List<string>();
            foreach (var platform in options.Platforms)
            {
                names.Add(BuildPipelineTargets.FolderName(platform));
            }

            return names;
        }

        private static string UsageText()
        {
            return "usage: -executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine\n"
                + "         -buildPlatform win64|mac|mac-arm64|mac-x64|all   (comma-separated, repeatable)\n"
                + "         -buildConfig   development|release\n"
                + "        [-buildVersion 0.1.0]            override the repository VERSION file\n"
                + "        [-buildOutputRoot <abs path>]    default: <repo>/dist\n"
                + "        [-buildNoClean]                  keep the previous output folder contents\n"
                + "        [-buildRequireIl2cpp]            fail instead of falling back to Mono\n"
                + "        [-buildSteamAppId 480]           overrides STEAM_APP_ID and steam_appid.txt\n"
                + "\n"
                + "exit codes: 0 ok, 1 unexpected, 2 arguments, 3 scenes, 4 build failed,\n"
                + "            5 IL2CPP required, 7 scripts do not compile, 8 target module missing";
        }

        /// <summary>
        /// Logs the errors Unity attached to the build, keeps them in the report, and returns
        /// how many of them are this project's problem.
        /// <para>
        /// <c>BuildSummary</c> carries only counts. The messages live on
        /// <c>BuildReport.steps[].messages[]</c>, and if nobody reads them a failed
        /// build says "Error building Player: 2 errors" and nothing else — the counts
        /// are recorded and the only actionable part is discarded. That happened here,
        /// and diagnosing it meant reading Unity's raw log by hand.
        /// </para>
        /// <para>
        /// Errors <see cref="BuildPipelineKnownDefects.IsKnownThirdPartyDefect"/> recognises are
        /// separated out rather than silenced: they are printed as warnings with their
        /// explanation, listed in their own section of <c>build-report.txt</c>, and left out of
        /// the returned count. Everything else is fatal. The split is what lets the pipeline
        /// keep failing on real errors while a defect in a package it does not control — and
        /// cannot patch — stops taking the build down with it.
        /// </para>
        /// <para>
        /// Warnings are counted but not printed: a build routinely carries dozens and
        /// burying two errors in sixty-five warnings is how they get missed.
        /// </para>
        /// </summary>
        /// <returns>The number of errors that are not known third-party defects.</returns>
        private static int ReportBuildMessages(BuildReport unityReport, BuildPipelineReport report)
        {
            if (unityReport == null)
            {
                return 0;
            }

            var printed = 0;
            var fatal = 0;

            foreach (var step in unityReport.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type != LogType.Error && message.type != LogType.Exception
                        && message.type != LogType.Assert)
                    {
                        continue;
                    }

                    var content = message.content.Trim();
                    var line = "[" + message.type + "] " + step.name + ": " + content;

                    if (BuildPipelineKnownDefects.IsKnownThirdPartyDefect(content))
                    {
                        // Recorded and shown every time. If this ever stops appearing the entry
                        // in BuildPipelineKnownDefects should be deleted, and a build that never
                        // mentions it again is how anyone would find out.
                        report.KnownDefects.Add(line);
                        Debug.LogWarning("[BuildPipeline] tolerated " + line + "\n    "
                            + BuildPipelineKnownDefects.ExplanationFor(content));
                        continue;
                    }

                    fatal++;
                    report.Notes.Add(line);

                    // A handful is enough to diagnose; a broken shader can emit hundreds
                    // of identical lines and drown the console.
                    if (printed < 20)
                    {
                        Debug.LogError("[BuildPipeline] " + line);
                        printed++;
                    }
                }
            }

            if (printed == 0 && !report.Succeeded)
            {
                Debug.LogError(
                    "[BuildPipeline] The build failed and Unity attached no message to any step. "
                    + "Look for the cause above this line in the raw log — the usual candidates are a "
                    + "shader that will not compile for the target, an asset the scene references but "
                    + "cannot be included, or a script that compiles for the editor and not for the player.");
            }

            return fatal;
        }
    }
}
