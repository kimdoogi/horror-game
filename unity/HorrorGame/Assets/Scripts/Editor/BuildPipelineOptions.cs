using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// One build request: what to build, how, where to, and with which Steam App ID.
    /// <para>
    /// Every field can come from the command line, because the editor menu and CI must run
    /// the same code path. A menu item that quietly takes a different route is a menu item
    /// that passes while CI fails.
    /// </para>
    /// </summary>
    public sealed class BuildPipelineOptions
    {
        /// <summary>
        /// §13's development App ID — Spacewar. Lobbies, P2P and voice all work against it
        /// before the store page exists.
        /// <para>
        /// <b>This is the only place the number is written.</b> The real App ID has not been
        /// bought yet ($100, §13's "인프라가 아니라 행정" table), so when it arrives it is one
        /// edit here — or, without touching code at all, a <c>steam_appid.txt</c> at the
        /// repository root, <c>STEAM_APP_ID</c> in the environment, or
        /// <c>-buildSteamAppId</c> on the command line.
        /// </para>
        /// </summary>
        public const string DefaultSteamAppId = "480";

        /// <summary>File read for the App ID, and written next to the player so it can launch outside Steam.</summary>
        public const string SteamAppIdFileName = "steam_appid.txt";

        /// <summary>Repository-root file holding the version. One line, e.g. <c>0.1.0</c>.</summary>
        public const string VersionFileName = "VERSION";

        /// <summary>
        /// Windows is ordered first by <see cref="Normalize"/>: it is the platform §13's
        /// audience actually plays, so on a multi-platform run it must be on disk before a
        /// macOS failure can end the run.
        /// </summary>
        public List<BuildPlatformId> Platforms { get; private set; } = new List<BuildPlatformId>();

        /// <summary>Development or Release. Defaults to Development — the safe default is the one that cannot ship.</summary>
        public BuildConfigurationId Configuration { get; private set; } = BuildConfigurationId.Development;

        /// <summary>Explicit version override; empty means "read the VERSION file".</summary>
        public string VersionOverride { get; private set; } = string.Empty;

        /// <summary>Absolute path of the <c>dist/</c> tree the platform folders live under.</summary>
        public string OutputRoot { get; private set; } = string.Empty;

        /// <summary>Wipe the platform folder before building. On by default; see <see cref="BuildPipelinePaths.CleanOutputDirectory"/>.</summary>
        public bool Clean { get; private set; } = true;

        /// <summary>
        /// Turn an IL2CPP-impossible release build into a hard failure instead of a warned
        /// Mono fallback. CI's shipping job should pass this; a developer's local run should not.
        /// </summary>
        public bool RequireIl2Cpp { get; private set; }

        /// <summary>The App ID stamped beside the player.</summary>
        public string SteamAppId { get; private set; } = DefaultSteamAppId;

        /// <summary>Where <see cref="SteamAppId"/> came from, so the report can prove which one shipped.</summary>
        public string SteamAppIdSource { get; private set; } = "default (§13 Spacewar)";

        /// <summary>Human-readable echo of the arguments, logged so a CI run is reproducible by hand.</summary>
        public string CommandLineEcho { get; private set; } = string.Empty;

        /// <summary>
        /// Builds options for an in-editor menu invocation. Steam App ID and version still
        /// resolve from disk and the environment, so a menu build and a CI build stamp the
        /// same values.
        /// </summary>
        public static BuildPipelineOptions ForMenu(BuildPlatformId platform, BuildConfigurationId configuration)
        {
            var options = new BuildPipelineOptions
            {
                Platforms = Normalize(new List<BuildPlatformId> { platform }),
                Configuration = configuration,
                OutputRoot = BuildPipelinePaths.DefaultOutputRoot,
                CommandLineEcho = "(editor menu)",
            };

            options.ResolveSteamAppId(string.Empty);
            return options;
        }

        /// <summary>
        /// Builds options for an in-editor menu invocation covering several platforms.
        /// </summary>
        public static BuildPipelineOptions ForMenu(IEnumerable<BuildPlatformId> platforms, BuildConfigurationId configuration)
        {
            var options = new BuildPipelineOptions
            {
                Platforms = Normalize(new List<BuildPlatformId>(platforms)),
                Configuration = configuration,
                OutputRoot = BuildPipelinePaths.DefaultOutputRoot,
                CommandLineEcho = "(editor menu)",
            };

            options.ResolveSteamAppId(string.Empty);
            return options;
        }

        /// <summary>
        /// Parses the batch-mode arguments. Recognised switches, all prefixed <c>-build</c> so
        /// they can never collide with an editor argument Unity adds in a future version:
        /// <list type="bullet">
        /// <item><c>-buildPlatform win64|mac|mac-arm64|mac-x64|all</c> (comma-separated, repeatable)</item>
        /// <item><c>-buildConfig development|release</c></item>
        /// <item><c>-buildVersion 0.1.0</c></item>
        /// <item><c>-buildOutputRoot &lt;absolute path&gt;</c></item>
        /// <item><c>-buildNoClean</c></item>
        /// <item><c>-buildRequireIl2cpp</c></item>
        /// <item><c>-buildSteamAppId 480</c></item>
        /// </list>
        /// </summary>
        /// <returns>False with a message in <paramref name="error"/>; the caller exits non-zero.</returns>
        public static bool TryParse(string[] argv, out BuildPipelineOptions options, out string error)
        {
            options = new BuildPipelineOptions();
            error = string.Empty;

            var platforms = new List<BuildPlatformId>();
            var configuration = BuildConfigurationId.Development;
            var sawConfiguration = false;
            var version = string.Empty;
            var outputRoot = string.Empty;
            var clean = true;
            var requireIl2Cpp = false;
            var appIdArgument = string.Empty;

            for (var i = 0; i < argv.Length; i++)
            {
                var argument = argv[i];
                switch (argument)
                {
                    case "-buildPlatform":
                        if (!TryTakeValue(argv, ref i, out var platformValue))
                        {
                            error = "-buildPlatform needs a value (win64, mac, mac-arm64, mac-x64, all).";
                            return false;
                        }

                        foreach (var token in platformValue.Split(','))
                        {
                            if (!TryParsePlatform(token.Trim(), platforms, out error))
                            {
                                return false;
                            }
                        }

                        break;

                    case "-buildConfig":
                        if (!TryTakeValue(argv, ref i, out var configValue))
                        {
                            error = "-buildConfig needs a value (development or release).";
                            return false;
                        }

                        if (!TryParseConfiguration(configValue, out configuration, out error))
                        {
                            return false;
                        }

                        sawConfiguration = true;
                        break;

                    case "-buildVersion":
                        if (!TryTakeValue(argv, ref i, out version))
                        {
                            error = "-buildVersion needs a value, e.g. 0.1.0.";
                            return false;
                        }

                        break;

                    case "-buildOutputRoot":
                        if (!TryTakeValue(argv, ref i, out outputRoot))
                        {
                            error = "-buildOutputRoot needs an absolute path.";
                            return false;
                        }

                        break;

                    case "-buildNoClean":
                        clean = false;
                        break;

                    case "-buildRequireIl2cpp":
                        requireIl2Cpp = true;
                        break;

                    case "-buildSteamAppId":
                        if (!TryTakeValue(argv, ref i, out appIdArgument))
                        {
                            error = "-buildSteamAppId needs a numeric App ID.";
                            return false;
                        }

                        break;
                }
            }

            if (platforms.Count == 0)
            {
                error = "No -buildPlatform given. Nothing was built, on purpose: guessing a "
                    + "platform is how a CI job produces the wrong player for a month.";
                return false;
            }

            if (!sawConfiguration)
            {
                error = "No -buildConfig given. It must be stated explicitly, because the "
                    + "difference between the two is whether the player can ship.";
                return false;
            }

            if (version.Length > 0 && !IsPlausibleVersion(version))
            {
                error = "-buildVersion '" + version + "' does not look like a version "
                    + "(expected digits and dots, optionally a -suffix).";
                return false;
            }

            if (outputRoot.Length > 0 && !Path.IsPathRooted(outputRoot))
            {
                error = "-buildOutputRoot must be absolute; got '" + outputRoot + "'.";
                return false;
            }

            if (appIdArgument.Length > 0 && !IsNumeric(appIdArgument))
            {
                error = "-buildSteamAppId '" + appIdArgument + "' is not a number.";
                return false;
            }

            options.Platforms = Normalize(platforms);
            options.Configuration = configuration;
            options.VersionOverride = version;
            options.OutputRoot = outputRoot.Length > 0
                ? Path.GetFullPath(outputRoot)
                : BuildPipelinePaths.DefaultOutputRoot;
            options.Clean = clean;
            options.RequireIl2Cpp = requireIl2Cpp;
            options.CommandLineEcho = string.Join(" ", argv);
            options.ResolveSteamAppId(appIdArgument);
            return true;
        }

        /// <summary>The absolute folder this platform's player is written to.</summary>
        public string OutputDirectoryFor(BuildPlatformId platform)
        {
            return Path.Combine(OutputRoot, BuildPipelineTargets.FolderName(platform));
        }

        /// <summary>The absolute path passed to Unity as <c>locationPathName</c>.</summary>
        public string PlayerPathFor(BuildPlatformId platform)
        {
            return Path.Combine(OutputDirectoryFor(platform), BuildPipelineTargets.PlayerFileName(platform));
        }

        /// <summary>
        /// True when a release build is about to be stamped with Spacewar's App ID. Not fatal —
        /// it is legitimate while §14 step 7 has not happened — but it belongs in the report,
        /// because a store build talking to App ID 480 authenticates nobody.
        /// </summary>
        public bool IsReleaseWithDevelopmentAppId
        {
            get { return Configuration == BuildConfigurationId.Release && SteamAppId == DefaultSteamAppId; }
        }

        /// <summary>
        /// Resolution order: command line, then <c>STEAM_APP_ID</c>, then a repository-root
        /// <c>steam_appid.txt</c>, then §13's development default. The order puts the most
        /// specific caller first and never silently invents a value.
        /// </summary>
        private void ResolveSteamAppId(string commandLineValue)
        {
            if (!string.IsNullOrEmpty(commandLineValue))
            {
                SteamAppId = commandLineValue;
                SteamAppIdSource = "-buildSteamAppId";
                return;
            }

            var fromEnvironment = Environment.GetEnvironmentVariable("STEAM_APP_ID");
            if (!string.IsNullOrEmpty(fromEnvironment) && IsNumeric(fromEnvironment.Trim()))
            {
                SteamAppId = fromEnvironment.Trim();
                SteamAppIdSource = "STEAM_APP_ID environment variable";
                return;
            }

            var rootFile = Path.Combine(BuildPipelinePaths.RepositoryRoot, SteamAppIdFileName);
            if (File.Exists(rootFile))
            {
                var contents = File.ReadAllText(rootFile).Trim();
                if (IsNumeric(contents))
                {
                    SteamAppId = contents;
                    SteamAppIdSource = BuildPipelinePaths.Relative(rootFile);
                    return;
                }
            }

            SteamAppId = DefaultSteamAppId;
            SteamAppIdSource = "default (§13 Spacewar)";
        }

        /// <summary>
        /// De-duplicates and orders the platform list, Windows first. Ordering here rather
        /// than at the call sites means every entry point gets the same priority.
        /// </summary>
        private static List<BuildPlatformId> Normalize(List<BuildPlatformId> requested)
        {
            var ordered = new List<BuildPlatformId>();
            var all = new[]
            {
                BuildPlatformId.WindowsX64,
                BuildPlatformId.MacUniversal,
                BuildPlatformId.MacAppleSilicon,
                BuildPlatformId.MacIntel,
            };

            foreach (var candidate in all)
            {
                if (requested.Contains(candidate))
                {
                    ordered.Add(candidate);
                }
            }

            return ordered;
        }

        private static bool TryParsePlatform(string token, List<BuildPlatformId> into, out string error)
        {
            error = string.Empty;
            switch (token.ToLowerInvariant())
            {
                case "win":
                case "win64":
                case "windows":
                case "windows-x64":
                    into.Add(BuildPlatformId.WindowsX64);
                    return true;

                case "mac":
                case "macos":
                case "osx":
                case "mac-universal":
                case "macos-universal":
                    into.Add(BuildPlatformId.MacUniversal);
                    return true;

                case "mac-arm64":
                case "macos-arm64":
                case "arm64":
                case "apple-silicon":
                    into.Add(BuildPlatformId.MacAppleSilicon);
                    return true;

                case "mac-x64":
                case "macos-x64":
                case "mac-intel":
                case "intel":
                    into.Add(BuildPlatformId.MacIntel);
                    return true;

                case "all":
                    into.Add(BuildPlatformId.WindowsX64);
                    into.Add(BuildPlatformId.MacUniversal);
                    return true;

                default:
                    error = "Unknown -buildPlatform '" + token
                        + "'. Valid: win64, mac, mac-arm64, mac-x64, all.";
                    return false;
            }
        }

        private static bool TryParseConfiguration(string token, out BuildConfigurationId configuration, out string error)
        {
            error = string.Empty;
            switch (token.ToLowerInvariant())
            {
                case "dev":
                case "development":
                case "debug":
                    configuration = BuildConfigurationId.Development;
                    return true;

                case "release":
                case "ship":
                case "shipping":
                    configuration = BuildConfigurationId.Release;
                    return true;

                default:
                    configuration = BuildConfigurationId.Development;
                    error = "Unknown -buildConfig '" + token + "'. Valid: development, release.";
                    return false;
            }
        }

        /// <summary>
        /// Reads the next argument as a value. Rejects a following switch, so a missing value
        /// fails loudly instead of consuming <c>-buildConfig</c> as if it were a version.
        /// </summary>
        private static bool TryTakeValue(string[] argv, ref int index, out string value)
        {
            value = string.Empty;
            if (index + 1 >= argv.Length)
            {
                return false;
            }

            var candidate = argv[index + 1];
            if (candidate.StartsWith("-", StringComparison.Ordinal))
            {
                return false;
            }

            value = candidate;
            index++;
            return true;
        }

        private static bool IsNumeric(string value)
        {
            return Regex.IsMatch(value, "^[0-9]+$");
        }

        private static bool IsPlausibleVersion(string value)
        {
            return Regex.IsMatch(value, "^[0-9]+(\\.[0-9]+)*([-.][A-Za-z0-9.]+)?$");
        }
    }
}
