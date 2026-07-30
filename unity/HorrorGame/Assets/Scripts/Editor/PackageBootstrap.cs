using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Adds the Unity packages the project needs, letting the editor pick versions.
    /// <para>
    /// <c>Packages/manifest.json</c> deliberately lists only third-party dependencies,
    /// where the version is a real decision (Mirror, Steamworks.NET, FizzySteamworks).
    /// Unity's own packages are added from here <em>without</em> a version, so the
    /// editor resolves whatever is correct for itself.
    /// </para>
    /// <para>
    /// Hand-pinning Unity package versions in the manifest is how a project ends up
    /// refusing to open: the version that is current when the manifest is written is
    /// not necessarily the one this editor supports, and Package Manager treats an
    /// unsatisfiable constraint as a hard error rather than picking something close.
    /// Adding by name sidesteps the whole class of problem.
    /// </para>
    /// </summary>
    public static class PackageBootstrap
    {
        /// <summary>
        /// The packages to install, and why each one is needed. Nothing goes in this
        /// list without a gameplay reason — every package is import time, build size
        /// and another thing that can break on an editor upgrade.
        /// </summary>
        private static readonly (string Name, string Why)[] Required =
        {
            ("com.unity.inputsystem",
                "§05's control scheme. The old Input Manager cannot express the "
                + "analogue peek cleanly, and it has no per-device rebinding."),

            ("com.unity.ai.navigation",
                "§06's monster pathing. NavMesh moved out of the core engine into a "
                + "package, and §12's whole S-corridor argument depends on the monster "
                + "following path length rather than the sight line."),

            ("com.unity.test-framework",
                "EditMode and PlayMode tests. The core rules are covered by dotnet "
                + "test; this covers the adapter layer that dotnet cannot reach."),

            ("com.unity.render-pipelines.universal",
                "Lighting is the central mechanic, not a look. §03 makes darkness the "
                + "lock on progress, and a match can have four flashlights, a zone "
                + "light and a burning flare live at once. URP's Forward+ path handles "
                + "many real-time lights per object; the built-in pipeline's forward "
                + "path runs out of per-object light slots exactly when the game gets "
                + "interesting."),
        };

        /// <summary>Installs any missing packages. Safe to run repeatedly.</summary>
        [MenuItem("Horror/Setup/Install Required Packages", priority = 10)]
        public static void InstallRequired()
        {
            var missing = FindMissing();
            if (missing.Count == 0)
            {
                Debug.Log("[PackageBootstrap] All required packages are already installed.");
                return;
            }

            Debug.Log("[PackageBootstrap] Adding: " + string.Join(", ", missing));

            // Added by name with no version so the editor resolves a compatible one.
            var request = Client.AddAndRemove(missing.ToArray(), Array.Empty<string>());
            while (!request.IsCompleted)
            {
                System.Threading.Thread.Sleep(100);
            }

            if (request.Status == StatusCode.Failure)
            {
                Debug.LogError("[PackageBootstrap] Failed: " + request.Error?.message);
                return;
            }

            Debug.Log("[PackageBootstrap] Done. Installed: "
                + string.Join(", ", request.Result.Select(p => p.name + "@" + p.version)));
        }

        /// <summary>
        /// Batch-mode entry point, for CI or a scripted first-time setup:
        /// <code>Unity -batchmode -quit -projectPath . -executeMethod HorrorGame.EditorTools.PackageBootstrap.InstallRequiredBatch</code>
        /// </summary>
        public static void InstallRequiredBatch()
        {
            try
            {
                InstallRequired();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PackageBootstrap] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Logs what is installed and what is missing, without changing anything.</summary>
        [MenuItem("Horror/Setup/Report Package Status", priority = 11)]
        public static void ReportStatus()
        {
            var list = Client.List(offlineMode: false, includeIndirectDependencies: false);
            while (!list.IsCompleted)
            {
                System.Threading.Thread.Sleep(100);
            }

            if (list.Status == StatusCode.Failure)
            {
                Debug.LogError("[PackageBootstrap] Could not list packages: " + list.Error?.message);
                return;
            }

            var installed = list.Result.ToDictionary(p => p.name, p => p.version);
            var lines = new List<string> { "[PackageBootstrap] Package status:" };

            foreach (var (name, why) in Required)
            {
                var mark = installed.TryGetValue(name, out var version) ? version : "MISSING";
                lines.Add($"  {name,-46} {mark}");
                if (mark == "MISSING")
                {
                    lines.Add($"      needed for: {why}");
                }
            }

            foreach (var thirdParty in new[]
                     {
                         "com.mirrornetworking.mirror",
                         "com.rlabrecque.steamworks.net",
                         "com.mirror.steamworks.net",
                     })
            {
                var mark = installed.TryGetValue(thirdParty, out var version) ? version : "MISSING";
                lines.Add($"  {thirdParty,-46} {mark}   (pinned in manifest.json)");
            }

            Debug.Log(string.Join("\n", lines));
        }

        private static List<string> FindMissing()
        {
            var list = Client.List(offlineMode: false, includeIndirectDependencies: false);
            while (!list.IsCompleted)
            {
                System.Threading.Thread.Sleep(100);
            }

            if (list.Status == StatusCode.Failure)
            {
                // Assume nothing is present rather than skipping the install: adding a
                // package that is already there is a no-op, while skipping one that is
                // missing fails much later with a wall of compile errors.
                Debug.LogWarning("[PackageBootstrap] Could not list packages, attempting to add all: "
                    + list.Error?.message);
                return Required.Select(r => r.Name).ToList();
            }

            var installed = new HashSet<string>(list.Result.Select(p => p.name), StringComparer.Ordinal);
            return Required.Where(r => !installed.Contains(r.Name)).Select(r => r.Name).ToList();
        }
    }
}
