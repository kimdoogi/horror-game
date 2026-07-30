#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HorrorGame.Steam
{
    /// <summary>
    /// Writes <c>steam_appid.txt</c> where Steamworks will look for it.
    /// <para>
    /// A process that Steam did not launch has no way to know which App ID it
    /// belongs to, and <c>SteamAPI_InitEx</c> fails outright without that answer.
    /// The Unity editor is always such a process, and so is a build a contributor
    /// double-clicks out of a folder — which is every build until §14 step 7 puts a
    /// depot on Steam. So the file has to be written for both, and it is written
    /// from code rather than committed as an asset so that it can never disagree
    /// with <see cref="SteamAppConfig.AppId"/>.
    /// </para>
    /// <para>
    /// Valve asks that the file <em>not</em> ship in a released build: with the real
    /// App ID it is redundant (Steam launches the game and tells it), and a stale
    /// copy is a support ticket that reads "the game says it is Spacewar". Both
    /// entry points here therefore refuse to write once
    /// <see cref="SteamAppConfig.IsDevelopmentAppId"/> is false, unless it is a
    /// development build.
    /// </para>
    /// <para>
    /// Nothing here throws. A read-only directory is a reason to log and carry on
    /// with the offline backend (§14 step 3 plays that way deliberately), not a
    /// reason to take down the game on startup.
    /// </para>
    /// </summary>
    public static class SteamAppIdFile
    {
        /// <summary>
        /// True when this build should have the file on disk beside it: while the
        /// project is still on §13's 480, or in any development build.
        /// </summary>
        public static bool ShouldWrite => SteamAppConfig.IsDevelopmentAppId || Debug.isDebugBuild;

        /// <summary>
        /// Writes the file everywhere Steamworks might look, and returns true if at
        /// least one location took it. Called by the Steamworks backend immediately
        /// before <c>SteamAPI_InitEx</c>, so the ordering is not something a scene
        /// or a script execution order can get wrong.
        /// <para>
        /// Two locations, because "beside the executable" and "the working
        /// directory" are the same folder for a Windows build launched from its
        /// folder and different for almost everything else — a macOS
        /// <c>.app</c> launched from Finder starts with the working directory at
        /// <c>/</c>, and the editor's working directory is the project root.
        /// Writing both costs a few bytes and removes an entire class of "works on
        /// my machine".
        /// </para>
        /// </summary>
        public static bool EnsureWritten()
        {
            if (!ShouldWrite)
            {
                return false;
            }

            var wroteSomething = false;
            foreach (var directory in CandidateDirectories())
            {
                if (TryWriteTo(directory))
                {
                    wroteSomething = true;
                }
            }

            if (!wroteSomething)
            {
                Debug.LogWarning("[Steam] Could not write " + SteamAppConfig.AppIdFileName
                    + " anywhere. Steam will only initialise if it launched this process.");
            }

            return wroteSomething;
        }

        /// <summary>
        /// Writes the file into <paramref name="directory"/>, overwriting an
        /// existing one only when its contents differ — the editor calls this on
        /// every domain reload and there is no reason to keep dirtying a file the
        /// OS is watching.
        /// </summary>
        public static bool TryWriteTo(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            var expected = SteamAppConfig.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var path = Path.Combine(directory, SteamAppConfig.AppIdFileName);

            try
            {
                if (File.Exists(path) && string.Equals(File.ReadAllText(path).Trim(), expected, StringComparison.Ordinal))
                {
                    return true;
                }

                if (!Directory.Exists(directory))
                {
                    return false;
                }

                // No trailing newline: the SDK parses the whole file as the number,
                // and a BOM or stray byte is the classic cause of a mystery
                // k_ESteamAPIInitResult_FailedGeneric.
                File.WriteAllText(path, expected, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Steam] Could not write " + path + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The directory holding the running executable, which is where Steamworks
        /// documents the file as belonging.
        /// <para>
        /// <see cref="Application.dataPath"/> points somewhere different on every
        /// platform, so the mapping is spelled out rather than guessed: a Windows or
        /// Linux player keeps its data folder next to the executable, while a macOS
        /// player buries it in <c>Contents/Resources/Data</c> with the binary over
        /// in <c>Contents/MacOS</c>. In the editor the "executable" that matters is
        /// the editor process, whose working directory is the project root.
        /// </para>
        /// </summary>
        public static string ExecutableDirectory
        {
            get
            {
#if UNITY_EDITOR
                return ProjectRoot;
#elif UNITY_STANDALONE_OSX
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "MacOS"));
#else
                return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
#endif
            }
        }

        /// <summary>
        /// The Unity project root — the folder containing <c>Assets</c>, which is
        /// also the editor's working directory and therefore where the editor's
        /// Steamworks looks. Also correct in a player build, where it resolves
        /// alongside the data folder; callers that care use
        /// <see cref="ExecutableDirectory"/> instead.
        /// </summary>
        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static IEnumerable<string> CandidateDirectories()
        {
            var executableDirectory = ExecutableDirectory;
            yield return executableDirectory;

            string? workingDirectory = null;
            try
            {
                workingDirectory = Directory.GetCurrentDirectory();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Steam] Could not read the working directory: " + ex.Message);
            }

            if (workingDirectory != null
                && !string.Equals(Path.GetFullPath(workingDirectory), executableDirectory, StringComparison.Ordinal))
            {
                yield return workingDirectory;
            }
        }
    }
}
