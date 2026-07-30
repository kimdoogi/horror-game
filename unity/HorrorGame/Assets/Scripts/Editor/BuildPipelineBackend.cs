using System;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// The scripting backend chosen for one build, and whether that choice is shippable.
    /// </summary>
    public readonly struct BuildBackendDecision
    {
        public BuildBackendDecision(
            ScriptingImplementation backend,
            bool isForcedMonoFallback,
            string reason,
            string shippingWarning)
        {
            Backend = backend;
            IsForcedMonoFallback = isForcedMonoFallback;
            Reason = reason;
            ShippingWarning = shippingWarning;
        }

        /// <summary>IL2CPP or Mono.</summary>
        public ScriptingImplementation Backend { get; }

        /// <summary>
        /// True only when IL2CPP was wanted and the host cannot produce it. A development
        /// build on Mono is a choice, not a fallback, and must not raise this flag — otherwise
        /// the warning fires on every iteration build and stops being read.
        /// </summary>
        public bool IsForcedMonoFallback { get; }

        /// <summary>One line for the report explaining why this backend was used.</summary>
        public string Reason { get; }

        /// <summary>The full multi-line warning, or empty when the build is shippable.</summary>
        public string ShippingWarning { get; }

        /// <summary>Backend name as it appears in reports and log lines.</summary>
        public string BackendName
        {
            get { return Backend == ScriptingImplementation.IL2CPP ? "IL2CPP" : "Mono"; }
        }
    }

    /// <summary>
    /// Picks the scripting backend from the host OS and the configuration, and — when it has
    /// to fall back — makes that impossible to miss.
    /// <para>
    /// IL2CPP compiles the game's IL to C++ and then invokes the <em>target platform's</em>
    /// native toolchain. That toolchain is not portable: Windows players need MSVC, macOS
    /// players need Xcode. So Unity cannot cross-compile IL2CPP, and a macOS workstation
    /// physically cannot produce the IL2CPP Windows player that §13's Steam audience will
    /// download. Mono is the only backend available for that combination.
    /// </para>
    /// <para>
    /// The failure this class exists to prevent is not the fallback — it is the fallback
    /// going unnoticed. A Mono player ships its managed assemblies as ordinary .NET DLLs
    /// that decompile in seconds, which in this game means §04's tuning and §13's
    /// "단서 내용 · 목표물 위치는 호스트만 보유" host-side logic are readable by anyone who
    /// unzips the depot. So the fallback is logged at error level, restated in the report,
    /// and marked with a file in the output folder.
    /// </para>
    /// </summary>
    public static class BuildPipelineBackend
    {
        /// <summary>
        /// Dropped into the output folder on a forced fallback. A file in the depot root is
        /// seen by whoever uploads the build even if nobody read the CI log.
        /// </summary>
        public const string FallbackMarkerFileName = "MONO-FALLBACK-DO-NOT-SHIP.txt";

        /// <summary>The host OS, named the way the report and the warning name it.</summary>
        public static string HostDescription
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.OSXEditor: return "macOS (OSXEditor)";
                    case RuntimePlatform.WindowsEditor: return "Windows (WindowsEditor)";
                    case RuntimePlatform.LinuxEditor: return "Linux (LinuxEditor)";
                    default: return Application.platform.ToString();
                }
            }
        }

        /// <summary>
        /// Chooses the backend.
        /// <list type="bullet">
        /// <item>Development is always Mono: it links in seconds instead of minutes, the
        /// managed debugger attaches, and §14's whole prototype loop is iteration speed.</item>
        /// <item>Release wants IL2CPP, and gets it only when the host OS matches the target's
        /// native toolchain. Otherwise Mono, loudly.</item>
        /// </list>
        /// </summary>
        public static BuildBackendDecision Decide(BuildPlatformId platform, BuildConfigurationId configuration)
        {
            if (configuration == BuildConfigurationId.Development)
            {
                return new BuildBackendDecision(
                    ScriptingImplementation.Mono2x,
                    isForcedMonoFallback: false,
                    reason: "development builds use Mono on purpose: it links in seconds and a "
                        + "managed debugger can attach to it.",
                    shippingWarning: string.Empty);
            }

            if (CanHostProduceIl2Cpp(platform))
            {
                return new BuildBackendDecision(
                    ScriptingImplementation.IL2CPP,
                    isForcedMonoFallback: false,
                    reason: "release build on a matching host, so the native toolchain for this "
                        + "target is available.",
                    shippingWarning: string.Empty);
            }

            return new BuildBackendDecision(
                ScriptingImplementation.Mono2x,
                isForcedMonoFallback: true,
                reason: "IL2CPP is unavailable for " + BuildPipelineTargets.DisplayName(platform)
                    + " on " + HostDescription + "; fell back to Mono.",
                shippingWarning: BuildWarning(platform));
        }

        /// <summary>
        /// True when the editor's host OS can run the target's native compiler and linker.
        /// The rule is simply "host family equals target family" — there is no supported
        /// cross-compilation path for either direction.
        /// </summary>
        public static bool CanHostProduceIl2Cpp(BuildPlatformId platform)
        {
            var host = Application.platform;
            if (platform == BuildPlatformId.WindowsX64)
            {
                return host == RuntimePlatform.WindowsEditor;
            }

            return host == RuntimePlatform.OSXEditor;
        }

        /// <summary>
        /// Logs the decision. The forced fallback goes through <see cref="Debug.LogError"/>
        /// deliberately: batch-mode logs are read by grepping for errors, and a warning in a
        /// 4,000-line Unity log is the same as no message at all.
        /// </summary>
        public static void LogDecision(BuildPlatformId platform, BuildBackendDecision decision)
        {
            if (decision.IsForcedMonoFallback)
            {
                Debug.LogError(decision.ShippingWarning);
                return;
            }

            Debug.Log("[BuildPipeline] Scripting backend for " + BuildPipelineTargets.DisplayName(platform)
                + ": " + decision.BackendName + " — " + decision.Reason);
        }

        /// <summary>
        /// The fallback text. Written once and reused by the log, the report and the marker
        /// file so all three say exactly the same thing.
        /// </summary>
        private static string BuildWarning(BuildPlatformId platform)
        {
            var toolchain = platform == BuildPlatformId.WindowsX64
                ? "MSVC on Windows"
                : "Xcode on macOS";

            return "============================================================\n"
                + " IL2CPP UNAVAILABLE — THIS IS A MONO BUILD, NOT SHIPPABLE\n"
                + "------------------------------------------------------------\n"
                + " target : " + BuildPipelineTargets.DisplayName(platform) + "\n"
                + " host   : " + HostDescription + "\n"
                + "\n"
                + " IL2CPP translates the game to C++ and then calls the target\n"
                + " platform's own toolchain (" + toolchain + "). That step cannot\n"
                + " cross-compile, so this host can only produce a Mono player\n"
                + " for this target.\n"
                + "\n"
                + " Why this matters for THIS game:\n"
                + "  * §13 ships on Steam and that audience is overwhelmingly\n"
                + "    Windows, so the Windows player is the product.\n"
                + "  * A Mono player ships plain managed assemblies. They\n"
                + "    decompile in seconds, which exposes §04's tuning and the\n"
                + "    host-only clue and objective logic §13 relies on.\n"
                + "  * Mono is measurably slower than IL2CPP, and the host runs\n"
                + "    the monster AI for four players.\n"
                + "\n"
                + " To ship: run this build on a Windows machine or a Windows CI\n"
                + " runner (GitHub Actions windows-latest, §13's optional build\n"
                + " automation row). Pass -buildRequireIl2cpp to turn this into a\n"
                + " hard failure instead of a fallback.\n"
                + "============================================================";
        }
    }
}
