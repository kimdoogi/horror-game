using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// The version and provenance stamped into a player: a human version string, and the
    /// git state that produced it.
    /// <para>
    /// §13's telemetry plan has no database — a balance report comes back as a Steam Stats
    /// bucket or a line in a log file, and the only way to know which build produced it is
    /// for the build to carry its own commit. A player that cannot say which commit it is
    /// makes every report unactionable.
    /// </para>
    /// </summary>
    public sealed class BuildPipelineVersion
    {
        private const string UnknownCommit = "unknown";

        private BuildPipelineVersion()
        {
        }

        /// <summary>The version stamped into <c>PlayerSettings.bundleVersion</c>, e.g. <c>0.1.0</c>.</summary>
        public string Version { get; private set; } = "0.0.0";

        /// <summary>Where <see cref="Version"/> came from, for the report.</summary>
        public string VersionSource { get; private set; } = string.Empty;

        /// <summary>Short commit hash, or a plain-language reason it is not available.</summary>
        public string Commit { get; private set; } = UnknownCommit;

        /// <summary>Branch name. Resolved with <c>symbolic-ref</c> so it also works before the first commit.</summary>
        public string Branch { get; private set; } = UnknownCommit;

        /// <summary>
        /// True when the working tree had uncommitted changes. A dirty release build is not
        /// reproducible, and the report says so rather than implying the commit is the whole story.
        /// </summary>
        public bool Dirty { get; private set; }

        /// <summary>
        /// Commit count, used as the macOS build number. It only ever increases along a
        /// branch, which is what the App Store field expects, and it is derivable from the
        /// repository rather than kept in a file somebody forgets to bump.
        /// </summary>
        public string BuildNumber { get; private set; } = "0";

        /// <summary>Commit plus a dirty marker — the form that goes in logs and the report.</summary>
        public string CommitDescription
        {
            get { return Dirty ? Commit + "-dirty" : Commit; }
        }

        /// <summary>
        /// Resolves the version from, in order: <paramref name="versionOverride"/>, the
        /// repository-root <c>VERSION</c> file, then <c>PlayerSettings.bundleVersion</c>.
        /// A file in the repository is the single source of truth so that a tag, a CI job and
        /// a local build cannot disagree about what 0.1.0 means.
        /// </summary>
        public static BuildPipelineVersion Resolve(string versionOverride)
        {
            var result = new BuildPipelineVersion();
            var repositoryRoot = BuildPipelinePaths.RepositoryRoot;

            if (!string.IsNullOrEmpty(versionOverride))
            {
                result.Version = versionOverride;
                result.VersionSource = "-buildVersion";
            }
            else
            {
                var versionFile = Path.Combine(repositoryRoot, BuildPipelineOptions.VersionFileName);
                if (File.Exists(versionFile))
                {
                    var contents = File.ReadAllText(versionFile).Trim();
                    if (contents.Length > 0)
                    {
                        result.Version = contents;
                        result.VersionSource = BuildPipelinePaths.Relative(versionFile);
                    }
                }

                if (result.VersionSource.Length == 0)
                {
                    result.Version = string.IsNullOrEmpty(PlayerSettings.bundleVersion)
                        ? "0.0.0"
                        : PlayerSettings.bundleVersion;
                    result.VersionSource = "PlayerSettings.bundleVersion (no "
                        + BuildPipelineOptions.VersionFileName + " at the repository root)";
                }
            }

            result.ReadGitState(repositoryRoot);
            return result;
        }

        /// <summary>
        /// Writes the version into the project so the running player reports it.
        /// <para>
        /// Deliberately not restored afterwards: the stamp <em>is</em> the point, and a
        /// project whose bundleVersion disagrees with the player sitting in <c>dist/</c> is
        /// the confusion this whole class exists to remove.
        /// </para>
        /// </summary>
        public void StampPlayerSettings()
        {
            PlayerSettings.bundleVersion = Version;
            var macStamped = TryStampMacBuildNumber();

            Debug.Log("[BuildPipeline] Version " + Version + " (" + VersionSource + "), build number "
                + BuildNumber + (macStamped ? string.Empty : " (macOS CFBundleVersion left alone)")
                + ", commit " + CommitDescription + " on " + Branch + ".");
        }

        /// <summary>
        /// Writes <see cref="BuildNumber"/> into the macOS bundle's CFBundleVersion when the
        /// editor exposes it.
        /// <para>
        /// Reflection, not <c>PlayerSettings.macOS.buildNumber</c>: 6000.3's scripting API
        /// documents <c>buildNumber</c> for iOS, tvOS and visionOS only, so naming the macOS
        /// one directly risks a compile error in the editor assembly — which would take the
        /// Windows build down with it over a cosmetic Info.plist field. The version that
        /// matters is <c>bundleVersion</c>, and that one is set unconditionally above.
        /// </para>
        /// </summary>
        private bool TryStampMacBuildNumber()
        {
            try
            {
                var nested = typeof(PlayerSettings).GetNestedType("macOS", BindingFlags.Public);
                var property = nested?.GetProperty("buildNumber", BindingFlags.Public | BindingFlags.Static);
                if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
                {
                    return false;
                }

                property.SetValue(null, BuildNumber);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ReadGitState(string repositoryRoot)
        {
            // symbolic-ref works in a repository with no commits, unlike rev-parse --abbrev-ref.
            if (TryRunGit("symbolic-ref --short HEAD", repositoryRoot, out var branch))
            {
                Branch = branch;
            }

            if (TryRunGit("rev-parse --short HEAD", repositoryRoot, out var commit))
            {
                Commit = commit;
            }
            else
            {
                // A fresh repository with zero commits is a real state — this one was in it while
                // the pipeline was written — and it must not be reported as a broken git install.
                Commit = TryRunGit("rev-parse --git-dir", repositoryRoot, out _)
                    ? "no-commits-yet"
                    : "git-unavailable";
            }

            if (TryRunGit("status --porcelain", repositoryRoot, out var status))
            {
                Dirty = status.Length > 0;
            }

            if (TryRunGit("rev-list --count HEAD", repositoryRoot, out var count) && count.Length > 0)
            {
                BuildNumber = count;
            }
        }

        /// <summary>
        /// Runs git and captures stdout. Failures are expected states, not exceptions: no git
        /// on PATH, no repository, no commits. Each one has a report line rather than a stack trace.
        /// </summary>
        private static bool TryRunGit(string arguments, string workingDirectory, out string output)
        {
            output = string.Empty;

            try
            {
                var startInfo = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return false;
                    }

                    var stdout = process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(15000))
                    {
                        process.Kill();
                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        return false;
                    }

                    output = stdout.Trim();
                    return true;
                }
            }
            catch (Exception)
            {
                // Win32Exception when git is not installed, IOException on a locked index.
                return false;
            }
        }
    }
}
