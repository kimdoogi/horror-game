using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using HorrorGame.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// §14 step 2 — "Mirror 로컬 호스트 — 같은 PC 2인스턴스" — as one command, and §11's
    /// whole field as the same command with a bigger number.
    /// <para>
    /// One command, because the step exists to be run constantly. §14's verification
    /// questions are all about feel ("추격이 재밌는가", "곁눈질 딜레마가 작동하는가") and
    /// the document says outright that they cannot be settled on paper. Anything that
    /// makes a two-player check take more than one line gets run less, and the two
    /// questions the whole design hangs on are the ones that stop being asked.
    /// </para>
    /// <para>
    /// <b>Why it is no longer two.</b> Two was the largest field this game had ever been
    /// observed in. §11's ceiling is <see cref="GameConstants.RaceRunnersMax"/>, the map
    /// deals 28 distinct starting places, and the pitch is twenty people in one building —
    /// so every number between 2 and 20 was an assumption. Tests proved a 21st socket is
    /// refused; nothing had ever put twenty bodies on B1's rim at once. This launches
    /// <see cref="LaunchField(int, bool, float)"/> processes, not two, and the host is told
    /// how many to wait for so it does not press 출발 at the §11 minimum and leave the
    /// other eighteen in the lobby.
    /// </para>
    /// <para>
    /// Run from a terminal:
    /// </para>
    /// <code>
    /// /Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity \
    ///   -batchmode -quit -nographics -projectPath unity/HorrorGame \
    ///   -executeMethod HorrorGame.EditorTools.SceneGen.LocalTwoInstance.LaunchFromCommandLine \
    ///   -horror-field 12 -horror-seconds 180 -logFile /tmp/twoinstance.log
    /// </code>
    /// <para>
    /// Each process is told which side it is by <see cref="HostArgument"/> /
    /// <see cref="ClientArgument"/>, which index it is by <see cref="InstanceArgument"/>,
    /// and how big the field should get by <see cref="FieldArgument"/>. Reading those is
    /// the runtime's job — see <c>LocalTwoInstanceEntry</c> — this class only guarantees
    /// the arguments are there, because the alternative (N identical processes all waiting
    /// to be clicked) is exactly the friction the step is meant to remove.
    /// </para>
    /// </summary>
    public static class LocalTwoInstance
    {
        /// <summary>Argument the first instance is launched with. The Net layer reads it and starts a host.</summary>
        public const string HostArgument = "-horror-host";

        /// <summary>Argument every other instance is launched with. The Net layer reads it and connects to loopback.</summary>
        public const string ClientArgument = "-horror-client";

        /// <summary>
        /// How many runners the host should wait for before pressing 출발, host side.
        /// <para>
        /// Without this the host starts the moment §11's minimum of two is met, which on a
        /// twelve-process launch is roughly nine seconds in — the other ten instances then
        /// join a race that has already loaded its descent scene and are dropped. The
        /// defect is invisible in a log read from the top: the host says "출발을 눌렀다"
        /// and the race runs. It just runs with two people in it.
        /// </para>
        /// </summary>
        public const string FieldArgument = "-horror-field";

        /// <summary>Which process this is: 0 is the host, 1..N−1 the clients. Every log line carries it.</summary>
        public const string InstanceArgument = "-horror-instance";

        /// <summary>Seconds after which an instance quits by itself. A twelve-window launch nobody can close by hand.</summary>
        public const string SecondsArgument = "-horror-seconds";

        /// <summary>Where the local test player is built. Outside Assets, so it never ends up in the project.</summary>
        public const string BuildFolder = "Builds/LocalTwoInstance";

        /// <summary>
        /// Milliseconds between the host and the first client.
        /// <para>
        /// A stagger rather than a handshake: Mirror's host has to be listening before the
        /// client dials, and adding a real readiness probe here would mean this file knew
        /// about the transport, which is the Net layer's decision to make.
        /// </para>
        /// <para>
        /// <b>Two seconds cost the first full-field run two runners.</b> The host does not
        /// listen when its process starts — it listens when its player has booted Mono,
        /// loaded the bootstrap scene, built the menu and had 호스트 pressed on it, which on
        /// this machine with twenty players cold-starting on eight cores was measured at
        /// eleven to fifteen seconds after <c>open -n</c> returned. Instances 1 and 2 dialled
        /// into that gap, and because KCP is UDP a dial at nobody does not fail — it times
        /// out ten seconds later, silently, into a menu. Twelve seconds is under that
        /// measured floor on purpose: the client now presses 참가 again
        /// (<c>LocalTwoInstanceEntry.JoinAttemptSeconds</c>), so this only has to make the
        /// common case cost no retries, and buying the rest with a longer sleep would add
        /// dead time to every run.
        /// </para>
        /// </summary>
        public const int HostStaggerMilliseconds = 12000;

        /// <summary>
        /// Milliseconds between one client and the next.
        /// <para>
        /// Cold-starting a Mono player is the most CPU-hungry second of its life — it maps
        /// the assemblies, JITs the boot path and builds the first scene — and this machine
        /// has eight cores. Launching twenty of them in one burst puts twenty of those
        /// seconds on top of each other, and the process that loses is the host, whose
        /// socket is not listening yet. Staggering costs <c>N × 1.5</c> seconds of wall
        /// clock and buys every instance a core to boot on.
        /// </para>
        /// </summary>
        public const int ClientStaggerMilliseconds = 1500;

        /// <summary>
        /// Window width for a client. Small on purpose, and not a cosmetic choice.
        /// <para>
        /// <c>ProjectSettings</c> ships <c>fullscreenMode: 1</c> (FullScreenWindow) and
        /// <c>macRetinaSupport: 1</c>, so an instance launched with no screen arguments
        /// renders 1920×1080 at 2× — 3840×2160 — and twelve of those is a GPU wall long
        /// before it is a CPU or a memory one. What this test is for is the simulation and
        /// the wire, so the clients are given the smallest window that still runs a real
        /// renderer, and the host is given a realistic one (see
        /// <see cref="HostScreenWidth"/>) because the host is the machine being measured.
        /// </para>
        /// </summary>
        public const int ClientScreenWidth = 640;

        /// <summary>Window height for a client. 16:9 against <see cref="ClientScreenWidth"/>.</summary>
        public const int ClientScreenHeight = 360;

        /// <summary>
        /// Window width for the host. 1280×720 windowed rather than the shipped 1920×1080
        /// fullscreen, so the reported frame time is a real render of the real field at a
        /// size a reader can convert, instead of a headless number that flatters the game.
        /// </summary>
        public const int HostScreenWidth = 1280;

        /// <summary>Window height for the host.</summary>
        public const int HostScreenHeight = 720;

        /// <summary>Builds a player if needed and launches a host and one client — §14 step 2, unchanged.</summary>
        [MenuItem("HorrorGame/Play/Launch Two Instances (§14 step 2)", priority = 40)]
        public static void Launch()
        {
            LaunchField(2, headlessClients: false, secondsBeforeQuit: 0f);
        }

        /// <summary>
        /// Builds a player if needed and launches §11's whole field on this machine.
        /// <para>
        /// The clients are headless — <c>-batchmode -nographics</c>. Twenty renderers do
        /// not fit on one desktop and the thing worth seeing is the host's screen with
        /// nineteen other people on it, which is exactly what this gives you.
        /// </para>
        /// </summary>
        /// <summary>
        /// The size a run defaults to when nobody says otherwise. Four.
        /// <para>
        /// <b>Twenty is what the game is for and four is what a machine is for.</b> A
        /// twenty-instance field on this desk measured 2.4 GB of resident memory and a load
        /// average of 29, which is a machine that is doing nothing else for the next ten
        /// minutes — and every defect the big run found (a runner caught 245 times in 135
        /// seconds, two clients dialling before the host's socket existed) reproduces with
        /// four. Four is two clients plus a spare: enough for §11's floor of two to be
        /// cleared with somebody left over, so "the field started short" is still a thing
        /// that can happen and be seen.
        /// </para>
        /// <para>
        /// Twenty is still one command away — <see cref="LaunchFullField"/>, or
        /// <c>-horror-field 20</c> on the batch entry point. It is a deliberate act rather
        /// than the default, which is the right way round: the numbers a full field
        /// produces are worth having, and not worth having by accident.
        /// </para>
        /// </summary>
        public const int DefaultFieldSize = 4;

        /// <summary>
        /// A small field — <see cref="DefaultFieldSize"/> runners. The everyday one.
        /// </summary>
        [MenuItem("HorrorGame/Play/Launch a Small Field (4)", priority = 41)]
        public static void LaunchSmallField()
        {
            LaunchField(DefaultFieldSize, headlessClients: true, secondsBeforeQuit: 0f);
        }

        /// <summary>
        /// §11's ceiling, twenty. Costs the machine — see <see cref="DefaultFieldSize"/>.
        /// </summary>
        [MenuItem("HorrorGame/Play/Launch a Full Field (§11 · 20)", priority = 42)]
        public static void LaunchFullField()
        {
            LaunchField(GameConstants.RaceRunnersMax, headlessClients: true, secondsBeforeQuit: 0f);
        }

        /// <summary>
        /// Batch entry point, so the whole thing is one shell command.
        /// <para>
        /// Reads <see cref="FieldArgument"/>, <see cref="SecondsArgument"/> and
        /// <c>-horror-headless</c> off the <em>editor's</em> command line, so the size of
        /// the field is a shell argument and not a recompile.
        /// </para>
        /// </summary>
        public static void LaunchFromCommandLine()
        {
            // DefaultFieldSize, not 2 and not RaceRunnersMax. A run that did not say how
            // big it wanted to be gets the everyday size; twenty has to be asked for.
            var runners = EditorArgumentInt(FieldArgument, DefaultFieldSize);
            var seconds = EditorArgumentInt(SecondsArgument, 0);
            var headless = EditorHasArgument("-horror-headless");

            if (!LaunchField(runners, headless, seconds))
            {
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Builds the player without launching anything. Separate entry point because
        /// the build is the part that can fail and the part CI wants to check, while
        /// launching opens windows on somebody's desktop.
        /// </summary>
        public static void BuildOnlyFromCommandLine()
        {
            if (!TryBuild(out var playerPath, out var error))
            {
                Debug.LogError("[Field] " + error);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[Field] Built " + playerPath + ". Launch it N times with " + HostArgument
                + " and " + ClientArgument + ", or run LaunchFromCommandLine to do both.");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Builds if needed, then launches one host and <paramref name="runners"/> − 1
        /// clients, staggered, each with its own log.
        /// </summary>
        /// <param name="runners">Total processes, host included. Clamped to §11's 2~20.</param>
        /// <param name="headlessClients">Run the clients with no renderer at all.</param>
        /// <param name="secondsBeforeQuit">Quit every instance after this long. 0 leaves them running.</param>
        /// <returns>False if the player could not be built, in which case nothing was launched.</returns>
        public static bool LaunchField(int runners, bool headlessClients, float secondsBeforeQuit)
        {
            // Clamped rather than refused: §11's own range is the only field this game has,
            // and a caller who typed 30 wants the biggest legal race, not an error dialog.
            var field = Math.Max(GameConstants.RaceRunnersMin, Math.Min(GameConstants.RaceRunnersMax, runners));
            if (field != runners)
            {
                Debug.LogWarning("[Field] " + runners + "명은 §11의 " + GameConstants.RaceRunnersMin + "~"
                    + GameConstants.RaceRunnersMax + " 밖이다 — " + field + "명으로 돌린다.");
            }

            if (!TryBuild(out var playerPath, out var error))
            {
                Debug.LogError("[Field] " + error);
                return false;
            }

            // A field is already running: refuse, and this is the one refusal in this file
            // that is not about §11.
            //
            // It happened. Twenty instances were forty seconds from the end of a measured
            // run when a second LaunchField started on the same machine. ClearLogs deleted
            // all twenty logs out from under processes that were still writing to them, so
            // the evidence for the run went to an unlinked inode and the numbers already
            // read off it could not be reproduced; both fields then bid for the same
            // loopback port. Nothing in either log said any of that had happened — the
            // second run looked like a clean four-instance field, which is exactly what
            // makes it dangerous.
            //
            // Counted by process name rather than by a lock file because a lock file
            // outlives a crash and a process does not, and the failure mode of a stale lock
            // is refusing to run at all.
            var alive = CountRunningInstances(playerPath);
            if (alive > 0)
            {
                Debug.LogError("[Field] 이 기계에서 이미 " + alive + "개 인스턴스가 돌고 있다 — 띄우지 않는다. "
                    + "두 판을 겹쳐 돌리면 로그가 서로를 지우고 포트를 다툰다. 끝나기를 기다리거나 "
                    + "`pkill -f " + BuildFolder + "` 로 정리한 뒤에 다시 부르라.");
                return false;
            }

            ClearLogs(field);

            StartProcess(playerPath, 0, field, headless: false, secondsBeforeQuit);
            System.Threading.Thread.Sleep(HostStaggerMilliseconds);

            for (var instance = 1; instance < field; instance++)
            {
                StartProcess(playerPath, instance, field, headlessClients, secondsBeforeQuit);

                if (instance < field - 1)
                {
                    System.Threading.Thread.Sleep(ClientStaggerMilliseconds);
                }
            }

            Debug.Log("[Field] " + field + "개 인스턴스를 띄웠다 — 호스트 1 + 클라이언트 " + (field - 1)
                + (headlessClients ? " (렌더러 없음)" : " (창)") + ". 로그: " + LogPathFor(0)
                + " … " + LogPathFor(field - 1));
            Debug.Log("[Field] Launched " + field + " instance(s) of " + playerPath + ". The host waits for "
                + field + " runner(s) before it presses 출발 — see LocalTwoInstanceEntry.");
            return true;
        }

        /// <summary>
        /// How many copies of the built player are running right now.
        /// <para>
        /// The name comes from the executable this method is about to start, so a project
        /// renamed in Player Settings does not silently start counting nothing. Any failure
        /// reading the process table is answered with 0 — refusing to launch because the
        /// check itself broke would make the harness less usable than it was before the
        /// check existed.
        /// </para>
        /// </summary>
        /// <param name="playerPath">The built player, <c>.app</c> bundle or executable.</param>
        private static int CountRunningInstances(string playerPath)
        {
            var name = Path.GetFileNameWithoutExtension(playerPath);
            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }

            try
            {
                return Process.GetProcessesByName(name).Length;
            }
            catch (Exception)
            {
                return 0;
            }
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
                Debug.Log("[Field] Reusing " + playerPath + " — newer than every scene in it.");
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
                        "[Field] " + Path.GetFileName(dll) + " is newer than the built player — "
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
        /// Where each instance writes its log. One file per process, and this is the whole
        /// reason a field is readable at all.
        /// <para>
        /// Unity's default log path is per-product, so N copies of the same player write to
        /// the same <c>Player.log</c> and interleave mid-word — a host line and a client
        /// line spliced together at a byte boundary. That made every reading of the
        /// two-instance test ambiguous: two identical coordinates in one file could be one
        /// runner logged twice or two runners in one cell, and there was no way to tell.
        /// At twelve instances it is not ambiguity, it is noise: the same file would be
        /// twelve interleaved streams and no line could be attributed to anybody.
        /// </para>
        /// <para>
        /// Zero-padded so a shell glob sorts them in launch order rather than
        /// <c>1, 10, 11, 2</c>, which is how the first reading of a twelve-instance run got
        /// its instances the wrong way round.
        /// </para>
        /// </summary>
        /// <param name="instance">0 for the host, 1..N−1 for the clients.</param>
        public static string LogPathFor(int instance)
        {
            var name = instance == 0
                ? "horror-host.log"
                : "horror-client-" + instance.ToString("00", CultureInfo.InvariantCulture) + ".log";
            return Path.Combine(Path.GetTempPath(), name);
        }

        /// <summary>
        /// Deletes the logs this run is about to write, and any left by a bigger run before
        /// it.
        /// <para>
        /// The second half matters more than the first. A four-instance run after a
        /// twelve-instance one leaves <c>horror-client-04</c>..<c>11</c> on disk full of the
        /// previous race, and a reader globbing <c>horror-client-*.log</c> counts twelve
        /// runners in a field of four. Clearing the whole §11 range makes the files on disk
        /// the run that just happened, which is the only property a glob can rely on.
        /// </para>
        /// </summary>
        /// <param name="field">How many instances this run will start.</param>
        private static void ClearLogs(int field)
        {
            for (var instance = 0; instance < GameConstants.RaceRunnersMax; instance++)
            {
                var path = LogPathFor(instance);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception)
                {
                    // A log we could not clear is still a log; the timestamps inside it say
                    // which run is which.
                }
            }

            Debug.Log("[Field] Cleared logs 0.." + (GameConstants.RaceRunnersMax - 1) + " for a field of " + field + ".");
        }

        /// <summary>
        /// Starts one instance.
        /// <para>
        /// <b>Two launch paths, and each has a reason.</b> A windowed instance goes through
        /// <c>open -n</c>, because that is what starts a second copy of a macOS bundle and
        /// it is the path a person running §14 step 2 by hand wants — it registers with
        /// LaunchServices, so the window can be clicked and brought forward. A headless one
        /// goes straight to the executable inside the bundle through <c>sh</c>, because a
        /// <c>-batchmode -nographics</c> player has no window for LaunchServices to manage,
        /// and twenty <c>open</c> calls are twenty round trips through a system service that
        /// serialises them.
        /// </para>
        /// </summary>
        /// <param name="playerPath">The built player.</param>
        /// <param name="instance">0 for the host, 1..N−1 for the clients.</param>
        /// <param name="field">How many runners the host should wait for.</param>
        /// <param name="headless">Run with no renderer.</param>
        /// <param name="secondsBeforeQuit">Quit after this long. 0 leaves it running.</param>
        private static void StartProcess(
            string playerPath, int instance, int field, bool headless, float secondsBeforeQuit)
        {
            var logFile = LogPathFor(instance);
            var side = instance == 0 ? HostArgument : ClientArgument;

            var arguments = side
                + " " + InstanceArgument + " " + instance.ToString(CultureInfo.InvariantCulture)
                + " " + FieldArgument + " " + field.ToString(CultureInfo.InvariantCulture);

            if (secondsBeforeQuit > 0f)
            {
                arguments += " " + SecondsArgument + " "
                    + secondsBeforeQuit.ToString("0", CultureInfo.InvariantCulture);
            }

            if (headless)
            {
                arguments += " -batchmode -nographics";
            }
            else
            {
                var width = instance == 0 ? HostScreenWidth : ClientScreenWidth;
                var height = instance == 0 ? HostScreenHeight : ClientScreenHeight;

                // -screen-fullscreen 0 first: without it every instance obeys the shipped
                // FullScreenWindow mode and macOS gives each one its own Space.
                arguments += " -screen-fullscreen 0"
                    + " -screen-width " + width.ToString(CultureInfo.InvariantCulture)
                    + " -screen-height " + height.ToString(CultureInfo.InvariantCulture);
            }

            arguments += " -logFile \"" + logFile + "\"";

            try
            {
                ProcessStartInfo info;

                if (headless && playerPath.EndsWith(".app", StringComparison.Ordinal))
                {
                    var inner = Path.Combine(
                        playerPath, "Contents", "MacOS", Path.GetFileNameWithoutExtension(playerPath));

                    // nohup + & so the instance outlives this editor process. A batch editor
                    // exits the moment it has launched everything, and a child that shared
                    // its lifetime would die at the starting line.
                    var script = "nohup \"" + inner + "\" " + arguments + " </dev/null >/dev/null 2>&1 &";
                    info = new ProcessStartInfo("/bin/sh", "-c \"" + script.Replace("\"", "\\\"") + "\"")
                    {
                        UseShellExecute = false,
                    };
                }
                else if (playerPath.EndsWith(".app", StringComparison.Ordinal))
                {
                    // A macOS bundle is a folder; `open -n` is what starts a second copy of
                    // the same app, which is the entire point of this command.
                    info = new ProcessStartInfo("/usr/bin/open", "-n \"" + playerPath + "\" --args " + arguments)
                    {
                        UseShellExecute = false,
                    };
                }
                else
                {
                    info = new ProcessStartInfo(playerPath, arguments) { UseShellExecute = false };
                }

                Process.Start(info);
            }
            catch (Exception error)
            {
                Debug.LogError("[Field] Could not start instance " + instance + ": " + error.Message);
            }
        }

        /// <summary>Reads an integer off the editor's own command line, so the shell picks the field size.</summary>
        /// <param name="name">Argument name.</param>
        /// <param name="fallback">What to use when it is absent or unreadable.</param>
        private static int EditorArgumentInt(string name, int fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal)
                    && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }

            return fallback;
        }

        private static bool EditorHasArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
