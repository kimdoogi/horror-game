using System;
using UnityEditor;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// The players this project ships. Kept as an explicit enum rather than raw
    /// <see cref="BuildTarget"/> values because macOS needs one target and three
    /// architectures, and "which architecture" is the part a human gets wrong.
    /// </summary>
    public enum BuildPlatformId
    {
        /// <summary>
        /// §13 ships on Steam, where the audience is overwhelmingly Windows. This is the
        /// build that matters; everything else is a convenience.
        /// </summary>
        WindowsX64,

        /// <summary>One .app that runs natively on Apple silicon and on Intel.</summary>
        MacUniversal,

        /// <summary>Apple silicon only — half the size, useful for local playtesting.</summary>
        MacAppleSilicon,

        /// <summary>Intel only. Kept for the pre-2020 Macs that are still in the wild.</summary>
        MacIntel,
    }

    /// <summary>
    /// Development keeps everything that makes a problem diagnosable; Release removes
    /// everything that makes a shipped player slower or easier to pull apart. There is
    /// deliberately no third option — "release with a profiler" is how a debug build
    /// reaches a store page.
    /// </summary>
    public enum BuildConfigurationId
    {
        Development,
        Release,
    }

    /// <summary>
    /// Everything platform-specific about an output: where it goes, what the player file
    /// is called, and which macOS architecture string the editor wants.
    /// <para>
    /// Output folder names are lowercase and hyphenated so the same path works when it is
    /// typed into <c>steamcmd</c> depot scripts, a CI artefact upload and a shell on three
    /// operating systems.
    /// </para>
    /// </summary>
    public static class BuildPipelineTargets
    {
        /// <summary>
        /// The player's file name, without extension. Passed explicitly as part of
        /// <c>locationPathName</c> so the output name is fixed by this pipeline rather than
        /// by <c>PlayerSettings.productName</c>, which another area of the project owns and
        /// may rename at any time.
        /// </summary>
        public const string PlayerFileStem = "HorrorGame";

        /// <summary>The single folder under <c>dist/</c> that this platform ever writes to.</summary>
        public static string FolderName(BuildPlatformId platform)
        {
            switch (platform)
            {
                case BuildPlatformId.WindowsX64: return "windows-x64";
                case BuildPlatformId.MacUniversal: return "macos-universal";
                case BuildPlatformId.MacAppleSilicon: return "macos-arm64";
                case BuildPlatformId.MacIntel: return "macos-x64";
                default: throw new ArgumentOutOfRangeException(nameof(platform));
            }
        }

        /// <summary>Human-readable name for logs and the build report.</summary>
        public static string DisplayName(BuildPlatformId platform)
        {
            switch (platform)
            {
                case BuildPlatformId.WindowsX64: return "Windows x64";
                case BuildPlatformId.MacUniversal: return "macOS universal (Apple silicon + Intel)";
                case BuildPlatformId.MacAppleSilicon: return "macOS Apple silicon";
                case BuildPlatformId.MacIntel: return "macOS Intel";
                default: throw new ArgumentOutOfRangeException(nameof(platform));
            }
        }

        /// <summary>The editor's build target. All four platforms live in the Standalone group.</summary>
        public static BuildTarget ToBuildTarget(BuildPlatformId platform)
        {
            return platform == BuildPlatformId.WindowsX64
                ? BuildTarget.StandaloneWindows64
                : BuildTarget.StandaloneOSX;
        }

        /// <summary>
        /// The leaf of <c>locationPathName</c>. Unity derives the produced player's name from
        /// this path, so it is what makes the output deterministic.
        /// </summary>
        public static string PlayerFileName(BuildPlatformId platform)
        {
            return platform == BuildPlatformId.WindowsX64
                ? PlayerFileStem + ".exe"
                : PlayerFileStem + ".app";
        }

        /// <summary>True when the platform is one of the three macOS variants.</summary>
        public static bool IsMac(BuildPlatformId platform)
        {
            return platform != BuildPlatformId.WindowsX64;
        }

        /// <summary>
        /// The name of the <c>UnityEditor.OSXStandalone.OSXArchitecture</c> member to select,
        /// or an empty string for Windows. Matched by name rather than by referencing the enum,
        /// because the type only exists when the macOS build module is installed and a hard
        /// reference would stop the whole editor assembly from compiling on a Windows CI box.
        /// </summary>
        public static string MacArchitectureName(BuildPlatformId platform)
        {
            switch (platform)
            {
                case BuildPlatformId.MacUniversal: return "x64ARM64";
                case BuildPlatformId.MacAppleSilicon: return "ARM64";
                case BuildPlatformId.MacIntel: return "x64";
                default: return string.Empty;
            }
        }
    }
}
