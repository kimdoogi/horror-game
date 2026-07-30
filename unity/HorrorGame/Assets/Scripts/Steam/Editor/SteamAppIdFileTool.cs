#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
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
    /// the only moment the built output's path is known. Both defer the decision of
    /// <em>whether</em> to write to <see cref="SteamAppIdFile.ShouldWrite"/>, so the day
    /// <see cref="SteamAppConfig.AppId"/> becomes the real App ID, release builds stop
    /// shipping the file — which is what Valve asks for, and one less thing to remember at
    /// §14 step 7.
    /// </para>
    /// </summary>
    public static class SteamAppIdFileTool
    {
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
        [PostProcessBuild(1)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (!SteamAppIdFile.ShouldWrite)
            {
                Debug.Log("[Steam] Release App ID in use, so " + SteamAppConfig.AppIdFileName
                    + " was deliberately not shipped with this build.");
                return;
            }

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
