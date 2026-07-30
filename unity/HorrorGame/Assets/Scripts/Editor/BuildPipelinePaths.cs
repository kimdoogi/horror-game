using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Locates the repository and the <c>dist/</c> output tree, and owns the only code in
    /// the pipeline that is allowed to delete anything.
    /// <para>
    /// Paths are derived from <see cref="Application.dataPath"/> rather than from the
    /// working directory: a batch-mode invocation can be launched from anywhere, and
    /// <c>-projectPath</c> does not change the process's cwd.
    /// </para>
    /// </summary>
    public static class BuildPipelinePaths
    {
        /// <summary>Output root, relative to the repository root. Already git-ignored.</summary>
        public const string DistFolderName = "dist";

        /// <summary>The Unity project folder — the one containing Assets/ and ProjectSettings/.</summary>
        public static string ProjectRoot
        {
            get
            {
                var parent = Path.GetDirectoryName(Application.dataPath);
                return string.IsNullOrEmpty(parent) ? Application.dataPath : parent;
            }
        }

        /// <summary>
        /// The repository root, found by walking up from the project folder looking for
        /// <c>.git</c>. Walking beats a hard-coded number of "..", because the same script has
        /// to work in a developer's clone, in a CI checkout and inside a worktree, where the
        /// depth is not guaranteed to be identical.
        /// </summary>
        public static string RepositoryRoot
        {
            get
            {
                var directory = new DirectoryInfo(ProjectRoot);
                while (directory != null)
                {
                    if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                        || File.Exists(Path.Combine(directory.FullName, ".git")))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }

                // No .git — a source drop or an exported archive. The layout of this repo is
                // <root>/unity/HorrorGame, so two levels up is the documented fallback.
                var guess = Path.GetFullPath(Path.Combine(ProjectRoot, "..", ".."));
                Debug.LogWarning("[BuildPipeline] No .git found above the project; assuming the "
                    + "repository root is " + guess + ". Pass -buildOutputRoot to be explicit.");
                return guess;
            }
        }

        /// <summary>The <c>dist/</c> root every output path is built from.</summary>
        public static string DefaultOutputRoot
        {
            get { return Path.Combine(RepositoryRoot, DistFolderName); }
        }

        /// <summary>
        /// Empties a platform's output folder so the directory holds exactly one build.
        /// <para>
        /// This is not tidiness. A development player leaves behind debug symbols and a
        /// crash handler; if a release build is then written over the top, the folder that
        /// gets uploaded to a depot contains a mixture nobody inspected. One build per
        /// folder also makes the reported size mean something.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// If the directory is not strictly inside <paramref name="outputRoot"/>. A recursive
        /// delete driven by a command-line argument gets exactly one guard, and this is it.
        /// </exception>
        public static void CleanOutputDirectory(string directory, string outputRoot)
        {
            var full = Path.GetFullPath(directory);
            var rootFull = Path.GetFullPath(outputRoot);
            var rootPrefix = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;

            if (!full.StartsWith(rootPrefix, StringComparison.Ordinal) || full.Length <= rootPrefix.Length)
            {
                throw new InvalidOperationException(
                    "[BuildPipeline] Refusing to delete '" + full + "': it is not inside the output root '"
                    + rootFull + "'.");
            }

            if (Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
            }

            Directory.CreateDirectory(full);
        }

        /// <summary>
        /// Total bytes on disk under <paramref name="directory"/>. Unity's own reported size
        /// counts what it packed, which is not the same number as what a player downloads —
        /// a macOS .app is a directory tree and the Windows player has a data folder beside
        /// the executable.
        /// </summary>
        public static long DirectorySizeBytes(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            long total = 0;
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // A symlink inside a .app bundle can dangle. Skipping it under-reports by
                    // a few bytes, which is better than failing a finished build over a stat.
                }
            }

            return total;
        }

        /// <summary>Bytes as megabytes, for report lines humans read.</summary>
        public static double ToMegabytes(long bytes)
        {
            return Math.Round(bytes / (1024.0 * 1024.0), 2);
        }

        /// <summary>
        /// Forward slashes in every report, on every host. A report is compared between a
        /// developer's machine and a CI runner often enough that backslashes become noise
        /// in the diff.
        /// </summary>
        public static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        /// <summary>
        /// <paramref name="path"/> relative to the repository root when it is underneath it,
        /// otherwise the absolute path. Keeps report lines short without hiding anything.
        /// </summary>
        public static string Relative(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(RepositoryRoot);
            if (full.StartsWith(root, StringComparison.Ordinal) && full.Length > root.Length)
            {
                return Normalize(full.Substring(root.Length).TrimStart('/', '\\'));
            }

            return Normalize(full);
        }

        /// <summary>Every file directly inside a folder, sorted, for the report's file list.</summary>
        public static List<string> TopLevelEntries(string directory)
        {
            var entries = new List<string>();
            if (!Directory.Exists(directory))
            {
                return entries;
            }

            foreach (var entry in Directory.GetFileSystemEntries(directory))
            {
                entries.Add(Path.GetFileName(entry));
            }

            entries.Sort(StringComparer.Ordinal);
            return entries;
        }
    }
}
