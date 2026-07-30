using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Decides which scenes go into the player, and refuses to build when that answer is
    /// empty or wrong.
    /// <para>
    /// A player built with no scenes launches to a black window and exits zero. That is the
    /// single most expensive silent failure in a Unity CI job, so scene collection is a
    /// separate, loud step rather than a field somebody forgets to fill in.
    /// </para>
    /// </summary>
    public static class BuildPipelineScenes
    {
        /// <summary>Searched when Build Settings is empty, so a generated scene is still findable.</summary>
        private const string SceneSearchFolder = "Assets/Scenes";

        /// <summary>
        /// Sorted first when falling back to disk discovery: scene 0 is what the player loads,
        /// and this project's entry point is the bootstrap scene that brings up Mirror and the
        /// §13 Steam transport before anything else exists.
        /// </summary>
        private const string BootstrapMarker = "bootstrap";

        /// <summary>
        /// Collects the scene list, in load order.
        /// <para>
        /// <c>EditorBuildSettings</c> is the authority. When it is empty — which it is until
        /// scene generation has run — the scenes on disk are used instead, with a warning,
        /// because a build that works by accident today breaks the moment a second scene appears.
        /// </para>
        /// </summary>
        /// <returns>False with a message in <paramref name="error"/>; the caller exits non-zero.</returns>
        public static bool TryCollect(out string[] scenePaths, out string error)
        {
            scenePaths = Array.Empty<string>();
            error = string.Empty;

            var fromBuildSettings = new List<string>();
            var disabled = 0;
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled)
                {
                    disabled++;
                    continue;
                }

                fromBuildSettings.Add(scene.path);
            }

            var source = "EditorBuildSettings";
            var collected = fromBuildSettings;

            if (collected.Count == 0)
            {
                collected = DiscoverOnDisk();
                source = SceneSearchFolder + " (discovered)";

                if (collected.Count > 0)
                {
                    Debug.LogWarning("[BuildPipeline] Build Settings has no enabled scenes; falling back to "
                        + collected.Count + " scene(s) discovered under " + SceneSearchFolder + ", ordered "
                        + "bootstrap-first then by name. Populate Build Settings — load order is a design "
                        + "decision and discovery is only a stopgap.\n  "
                        + string.Join("\n  ", collected));
                }
            }

            if (collected.Count == 0)
            {
                error = "No scenes to build. Build Settings is empty and no .unity file exists under "
                    + SceneSearchFolder + ", so the player would launch to a black window. "
                    + "Generate or add a scene first (§14 step 1);"
                    + (disabled > 0 ? " note that " + disabled + " scene(s) are listed but disabled." : string.Empty);
                return false;
            }

            var missing = new List<string>();
            var projectRoot = BuildPipelinePaths.ProjectRoot;
            foreach (var path in collected)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(Path.Combine(projectRoot, path)))
                {
                    missing.Add(string.IsNullOrEmpty(path) ? "(empty path)" : path);
                }
            }

            if (missing.Count > 0)
            {
                // A stale entry survives in EditorBuildSettings after a scene is renamed or
                // deleted, and Unity builds the rest without complaining.
                error = "Scene(s) listed in " + source + " do not exist on disk:\n  "
                    + string.Join("\n  ", missing)
                    + "\nFix the list rather than the build: a player missing scene "
                    + "0 fails at runtime, where nobody is watching a log.";
                return false;
            }

            Debug.Log("[BuildPipeline] " + collected.Count + " scene(s) from " + source + ":\n  "
                + string.Join("\n  ", collected));

            scenePaths = collected.ToArray();
            return true;
        }

        /// <summary>
        /// Scenes under <see cref="SceneSearchFolder"/>, bootstrap first then ordinal by path.
        /// Ordinal rather than culture-aware, so the same clone on a Korean and an English
        /// machine produces the same player.
        /// </summary>
        private static List<string> DiscoverOnDisk()
        {
            var found = new List<string>();
            if (!AssetDatabase.IsValidFolder(SceneSearchFolder))
            {
                return found;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { SceneSearchFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".unity", StringComparison.Ordinal))
                {
                    found.Add(path);
                }
            }

            found.Sort((left, right) =>
            {
                var leftBootstrap = IsBootstrap(left);
                var rightBootstrap = IsBootstrap(right);
                if (leftBootstrap != rightBootstrap)
                {
                    return leftBootstrap ? -1 : 1;
                }

                return string.Compare(left, right, StringComparison.Ordinal);
            });

            return found;
        }

        private static bool IsBootstrap(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            return name != null && name.ToLowerInvariant().Contains(BootstrapMarker);
        }
    }
}
