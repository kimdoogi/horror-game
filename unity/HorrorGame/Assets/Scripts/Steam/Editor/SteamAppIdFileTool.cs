#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HorrorGame.Steam.EditorTools
{
    /// <summary>
    /// Puts <c>steam_appid.txt</c> where Steamworks needs it, in the editor and in every
    /// build.
    /// <para>
    /// §13 develops against App ID <c>480</c> long before the real one exists, and a
    /// process Steam did not launch cannot discover its App ID any other way — so without
    /// this file, Steam simply refuses to initialise and the offline backend takes over,
    /// which is a confusing way to find out that a one-line setup step was missed.
    /// </para>
    /// <para>
    /// The editor case is handled on load; the player case in a post-build step, which is
    /// the only moment the built output's path is known.
    /// </para>
    /// <para>
    /// The two cases cannot share one rule, and once did, to this project's cost.
    /// <see cref="SteamAppIdFile.ShouldWrite"/> asks <c>Debug.isDebugBuild</c>, which at
    /// runtime is the running player's own configuration — exactly right there. Inside a
    /// post-build callback it is the <em>editor</em> that is answering, and the editor is
    /// always a debug build, so the rule said "write" for every build ever made, including
    /// the release ones Valve asks to be clean. The build being processed is the only thing
    /// worth asking, and <see cref="BuildReport"/> carries it, so this takes a
    /// <see cref="IPostprocessBuildWithReport"/> instead of the bare callback.
    /// </para>
    /// </summary>
    public class SteamAppIdFileTool : IPostprocessBuildWithReport
    {
        /// <summary>
        /// After Unity's own post-build work, before the build pipeline's report is written —
        /// which then scans the output and would catch this class doing the wrong thing.
        /// </summary>
        public int callbackOrder => 1;

        [InitializeOnLoadMethod]
        private static void WriteForEditor()
        {
            if (!SteamAppIdFile.ShouldWrite)
            {
                return;
            }

            // The editor's working directory is the project root, which is where its
            // Steamworks will look. Written on every domain reload because it is cheap and
            // because the alternative is a stale file after the App ID changes.
            SteamAppIdFile.TryWriteTo(SteamAppIdFile.ProjectRoot);
        }

        /// <summary>
        /// Writes the file next to a freshly built player.
        /// <para>
        /// macOS gets two copies: one beside the <c>.app</c> and one inside
        /// <c>Contents/MacOS</c> next to the actual binary. A bundle launched from Finder
        /// starts with its working directory at <c>/</c>, so neither location alone is
        /// reliably the one Steamworks reads, and both together cost 4 bytes.
        /// </para>
        /// </summary>
        public void OnPostprocessBuild(BuildReport report)
        {
            var summary = report.summary;
            var isDevelopmentBuild = (summary.options & BuildOptions.Development) != 0;

            if (!isDevelopmentBuild)
            {
                Debug.Log("[Steam] Release build, so " + SteamAppConfig.AppIdFileName
                    + " was deliberately not shipped with it: Steam tells a released game its own"
                    + " App ID, and a file in the depot would override that with a stale one.");
                return;
            }

            var target = summary.platform;
            var pathToBuiltProject = summary.outputPath;

            if (string.IsNullOrEmpty(pathToBuiltProject))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(pathToBuiltProject));
                var wrote = SteamAppIdFile.TryWriteTo(directory);

                if (target == BuildTarget.StandaloneOSX
                    && pathToBuiltProject.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    var macOsDirectory = Path.Combine(pathToBuiltProject, "Contents", "MacOS");
                    wrote |= SteamAppIdFile.TryWriteTo(macOsDirectory);
                }

                Debug.Log(wrote
                    ? "[Steam] Wrote " + SteamAppConfig.AppIdFileName + " (App ID " + SteamAppConfig.AppId
                        + ") beside the build at " + pathToBuiltProject
                    : "[Steam] Could not write " + SteamAppConfig.AppIdFileName + " beside the build at "
                        + pathToBuiltProject + "; Steam will not initialise unless it launches the game.");
            }
            catch (Exception ex)
            {
                // A post-build step must never fail a build that otherwise succeeded.
                Debug.LogWarning("[Steam] Post-build " + SteamAppConfig.AppIdFileName + " step failed: " + ex.Message);
            }
        }

        [MenuItem("Horror/Steam/Write steam_appid.txt", priority = 40)]
        private static void WriteNow()
        {
            if (SteamAppIdFile.TryWriteTo(SteamAppIdFile.ProjectRoot))
            {
                Debug.Log("[Steam] " + SteamAppConfig.AppIdFileName + " = " + SteamAppConfig.AppId + " at "
                    + SteamAppIdFile.ProjectRoot);
                return;
            }

            Debug.LogError("[Steam] Could not write " + SteamAppConfig.AppIdFileName + " to "
                + SteamAppIdFile.ProjectRoot);
        }
    }
}
