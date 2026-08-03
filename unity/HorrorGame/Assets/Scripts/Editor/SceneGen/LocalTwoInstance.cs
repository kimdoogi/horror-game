using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// §14 step 2 — "Mirror 로컬 호스트 — 같은 PC 2인스턴스" — as one command.
    /// <para>
    /// One command, because the step exists to be run constantly. §14's verification
    /// questions are all about feel ("추격이 재밌는가", "곁눈질 딜레마가 작동하는가") and
    /// the document says outright that they cannot be settled on paper. Anything that
    /// makes a two-player check take more than one line gets run less, and the two
    /// questions the whole design hangs on are the ones that stop being asked.
    /// </para>
    /// <para>
    /// Run from a terminal:
    /// </para>
    /// <code>
    /// /Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity \
    ///   -batchmode -quit -nographics -projectPath unity/HorrorGame \
    ///   -executeMethod HorrorGame.EditorTools.SceneGen.LocalTwoInstance.Launch \
    ///   -logFile /tmp/twoinstance.log
    /// </code>
    /// <para>
    /// The two processes are told which side they are by
    /// <see cref="HostArgument"/> / <see cref="ClientArgument"/>. Reading those is the
    /// Net layer's job — this class only guarantees the argument is there, because the
    /// alternative (two identical processes both waiting to be clicked) is exactly the
    /// friction the step is meant to remove.
    /// </para>
    /// </summary>
    public static class LocalTwoInstance
    {
        /// <summary>Argument the first instance is launched with. The Net layer reads it and starts a host.</summary>
        public const string HostArgument = "-horror-host";

        /// <summary>Argument the second instance is launched with. The Net layer reads it and connects to loopback.</summary>
        public const string ClientArgument = "-horror-client";

        /// <summary>Where the local test player is built. Outside Assets, so it never ends up in the project.</summary>
        public const string BuildFolder = "Builds/LocalTwoInstance";

        /// <summary>Builds a player if needed and launches a host and a client on this machine.</summary>
        [MenuItem("HorrorGame/Play/Launch Two Instances (§14 step 2)", priority = 40)]
        public static void Launch()
        {
            if (!TryBuild(out var playerPath, out var error))
            {
                Debug.LogError("[TwoInstance] " + error);
                return;
            }

            StartProcess(playerPath, HostArgument);

            // A short stagger rather than a handshake: Mirror's host has to be
            // listening before the client dials, and adding a real readiness probe here
            // would mean this file knew about the transport, which is the Net layer's
            // decision to make.
            System.Threading.Thread.Sleep(2000);
            StartProcess(playerPath, ClientArgument);

            Debug.Log("[TwoInstance] 로그: 호스트 " + LogPathFor(HostArgument)
                      + " · 클라이언트 " + LogPathFor(ClientArgument));
            Debug.Log("[TwoInstance] Launched two instances of " + playerPath + " ("
                + HostArgument + " then " + ClientArgument + ").");
        }

        /// <summary>Batch entry point, so the whole thing is one shell command.</summary>
        public static void LaunchFromCommandLine()
        {
            if (!TryBuild(out var playerPath, out var error))
            {
                Debug.LogError("[TwoInstance] " + error);
                EditorApplication.Exit(1);
                return;
            }

            StartProcess(playerPath, HostArgument);
            System.Threading.Thread.Sleep(2000);
            StartProcess(playerPath, ClientArgument);
            Debug.Log("[TwoInstance] Launched two instances of " + playerPath + ".");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Builds the player without launching anything. Separate entry point because
        /// the build is the part that can fail and the part CI wants to check, while
        /// launching opens two windows on somebody's desktop.
        /// </summary>
        public static void BuildOnlyFromCommandLine()
        {
            if (!TryBuild(out var playerPath, out var error))
            {
                Debug.LogError("[TwoInstance] " + error);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[TwoInstance] Built " + playerPath + ". Launch it twice with "
                + HostArgument + " and " + ClientArgument + ", or run LaunchFromCommandLine to do both.");
            EditorApplication.Exit(0);
        }

        /// <summary>Builds the local test player. Reuses an existing one when it is newer than every scene.</summary>
        /// <param name="playerPath">Path of the executable to run.</param>
        /// <param name="error">Why the build failed, when it did.</param>
        public static bool TryBuild(out string playerPath, out string error)
        {
            error = string.Empty;
            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? ".";
            var folder = Path.Combine(projectRoot, BuildFolder);
            var target = EditorUserBuildSettings.activeBuildTarget;
            var executable = ExecutableName(target);
            playerPath = Path.Combine(folder, executable);

            var scenes = CollectScenes();
            if (scenes.Length == 0)
            {
                error = "No scenes to build. Generate them first: HorrorGame ▸ Scene Gen ▸ Generate Bootstrap Scene "
                    + "and Generate First Map.";
                return false;
            }

            if (IsUpToDate(playerPath, scenes))
            {
                Debug.Log("[TwoInstance] Reusing " + playerPath + " — newer than every scene in it.");
                return true;
            }

            Directory.CreateDirectory(folder);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = playerPath,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),

                // Development + script debugging: this player exists to be poked at,
                // never to be shipped, and §14's step 3 is "프로토타입 검증".
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                error = "Player build " + report.summary.result + " (" + report.summary.totalErrors + " errors).";
                return false;
            }

            return true;
        }

        private static string[] CollectScenes()
        {
            var paths = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path) && File.Exists(scene.path))
                {
                    paths.Add(scene.path);
                }
            }

            return paths.ToArray();
        }

        /// <summary>
        /// Whether the player on disk was built from what is in the project now.
        /// <para>
        /// <b>Scenes are not the only input, and treating them as one was a real bug.</b>
        /// This compared the built player against the scenes and nothing else, so a
        /// change to any <c>.cs</c> file left it "up to date": you fixed the game,
        /// re-ran the two-instance test, and watched the build from before the fix.
        /// Twice in one session a defect was declared un-fixed on that evidence.
        /// </para>
        /// <para>
        /// The compiled assemblies under <c>Library/ScriptAssemblies</c> are the honest
        /// stand-in for the code: Unity rewrites them on every recompile, so one of them
        /// being newer than the player means the player is running code that no longer
        /// exists. They are used rather than the <c>.cs</c> files themselves because a
        /// comment-only edit touches a source file and produces an identical assembly —
        /// and rebuilding a 500 MB player to change a comment is the kind of friction
        /// that stops the test being run at all, which is the thing §14 is most afraid
        /// of.
        /// </para>
        /// </summary>
        /// <param name="playerPath">The built player.</param>
        /// <param name="scenes">Scenes compiled into it.</param>
        private static bool IsUpToDate(string playerPath, string[] scenes)
        {
            if (!File.Exists(playerPath) && !Directory.Exists(playerPath))
            {
                return false;
            }

            var built = File.Exists(playerPath)
                ? File.GetLastWriteTimeUtc(playerPath)
                : Directory.GetLastWriteTimeUtc(playerPath);

            foreach (var scene in scenes)
            {
                if (File.GetLastWriteTimeUtc(scene) > built)
                {
                    return false;
                }
            }

            var assemblies = Path.Combine(
                Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? ".",
                "Library",
                "ScriptAssemblies");

            if (!Directory.Exists(assemblies))
            {
                // No assemblies to compare against is not evidence of freshness. Rebuild
                // rather than launch something that might be anything.
                return false;
            }

            foreach (var dll in Directory.GetFiles(assemblies, "*.dll"))
            {
                if (File.GetLastWriteTimeUtc(dll) > built)
                {
                    Debug.Log(
                        "[TwoInstance] " + Path.GetFileName(dll) + " is newer than the built player — "
                        + "rebuilding, because the alternative is testing code that is no longer in the project.");
                    return false;
                }
            }

            return true;
        }

        private static string ExecutableName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneOSX:
                    return "HorrorGame.app";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "HorrorGame.exe";
                default:
                    return "HorrorGame";
            }
        }

        /// <summary>
        /// Where each side writes its log. One file per side, because they would
        /// otherwise share one.
        /// <para>
        /// Unity's default log path is per-product, so two copies of the same player
        /// write to the same <c>Player.log</c> and interleave mid-word — a host line and
        /// a client line spliced together at a byte boundary. That made every reading of
        /// this test ambiguous: two identical coordinates in one file could be one runner
        /// logged twice or two runners in one cell, and there was no way to tell.
        /// </para>
        /// </summary>
        /// <param name="argument">Which side this is.</param>
        public static string LogPathFor(string argument)
        {
            var side = string.Equals(argument, HostArgument, StringComparison.Ordinal) ? "host" : "client";
            return Path.Combine(Path.GetTempPath(), "horror-" + side + ".log");
        }

        private static void StartProcess(string playerPath, string argument)
        {
            var fileName = playerPath;
            var logFile = LogPathFor(argument);
            var arguments = argument + " -logFile \"" + logFile + "\"";

            // Left over from the run before this one, so a launch that dies on start
            // leaves an empty file rather than the previous run's success.
            try
            {
                if (File.Exists(logFile))
                {
                    File.Delete(logFile);
                }
            }
            catch (Exception)
            {
                // A log we could not clear is still a log; the timestamps inside it say
                // which run is which.
            }

            if (playerPath.EndsWith(".app", StringComparison.Ordinal))
            {
                // A macOS bundle is a folder; `open -n` is what starts a second copy of
                // the same app, which is the entire point of this command.
                fileName = "/usr/bin/open";
                arguments = "-n \"" + playerPath + "\" --args " + argument
                            + " -logFile \"" + logFile + "\"";
            }

            try
            {
                var info = new ProcessStartInfo(fileName, arguments) { UseShellExecute = false };
                Process.Start(info);
            }
            catch (Exception error)
            {
                Debug.LogError("[TwoInstance] Could not start " + fileName + " " + arguments + ": " + error.Message);
            }
        }
    }
}
