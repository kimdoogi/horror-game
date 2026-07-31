using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Applies the player settings one build needs, then puts every one of them back.
    /// <para>
    /// Scripting backend, stripping level and macOS architecture are <em>project</em>
    /// settings, not build arguments — Unity has no per-invocation override for them. A
    /// pipeline that sets them and walks away leaves the next person's editor configured for
    /// whatever CI last built, and leaves a diff in <c>ProjectSettings.asset</c> that looks
    /// like somebody's decision. So this is a scope: set, build, restore.
    /// </para>
    /// <para>
    /// Deliberately <em>not</em> restored: <c>bundleVersion</c>, which
    /// <see cref="BuildPipelineVersion.StampPlayerSettings"/> owns.
    /// </para>
    /// </summary>
    public sealed class BuildPipelineSettingsScope : IDisposable
    {
        /// <summary>
        /// Mirror and Steamworks.NET both resolve types by name at runtime — Mirror's
        /// generated serialisers and Steam's callback dispatch — and managed stripping above
        /// Low removes exactly those. Low still strips the unused engine and BCL surface,
        /// which is where the size actually is, without needing a <c>link.xml</c> nobody has
        /// validated against a live 4-player session yet.
        /// </summary>
        private const ManagedStrippingLevel ReleaseStrippingLevel = ManagedStrippingLevel.Low;

        private readonly List<string> _notes = new List<string>();

        private ScriptingImplementation _previousBackend;
        private Il2CppCompilerConfiguration _previousCompilerConfiguration;
        private ManagedStrippingLevel _previousStrippingLevel;
        private bool _previousStripEngineCode;

        private PropertyInfo _macArchitectureProperty;
        private object _previousMacArchitecture;
        private PropertyInfo _xcodeProjectProperty;
        private object _previousCreateXcodeProject;
        private MethodInfo _codeGenerationSetter;
        private object _previousCodeGeneration;

        private bool _disposed;

        private BuildPipelineSettingsScope()
        {
        }

        /// <summary>What the reflective settings actually managed to do, for the build report.</summary>
        public IReadOnlyList<string> Notes
        {
            get { return _notes; }
        }

        /// <summary>The macOS architecture that was applied, or empty on Windows / on failure.</summary>
        public string MacArchitectureApplied { get; private set; } = string.Empty;

        /// <summary>
        /// Captures the current settings and applies the ones this build needs.
        /// </summary>
        public static BuildPipelineSettingsScope Apply(
            BuildPlatformId platform,
            BuildConfigurationId configuration,
            ScriptingImplementation backend)
        {
            var scope = new BuildPipelineSettingsScope();
            var standalone = NamedBuildTarget.Standalone;

            scope._previousBackend = PlayerSettings.GetScriptingBackend(standalone);
            scope._previousCompilerConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(standalone);
            scope._previousStrippingLevel = PlayerSettings.GetManagedStrippingLevel(standalone);
            scope._previousStripEngineCode = PlayerSettings.stripEngineCode;

            PlayerSettings.SetScriptingBackend(standalone, backend);

            if (configuration == BuildConfigurationId.Release)
            {
                PlayerSettings.SetIl2CppCompilerConfiguration(standalone, Il2CppCompilerConfiguration.Release);
                PlayerSettings.SetManagedStrippingLevel(standalone, ReleaseStrippingLevel);
                PlayerSettings.stripEngineCode = true;
                scope.TryApplyCodeGeneration("OptimizeSpeed");
            }
            else
            {
                // Debug compiler configuration keeps IL2CPP's generated C++ debuggable, and
                // stripping nothing means a stack trace names the method it happened in.
                PlayerSettings.SetIl2CppCompilerConfiguration(standalone, Il2CppCompilerConfiguration.Debug);
                PlayerSettings.SetManagedStrippingLevel(standalone, ManagedStrippingLevel.Disabled);
                PlayerSettings.stripEngineCode = false;
            }

            if (BuildPipelineTargets.IsMac(platform))
            {
                scope.TryApplyMacArchitecture(BuildPipelineTargets.MacArchitectureName(platform));
            }

            return scope;
        }

        /// <summary>
        /// The <see cref="BuildOptions"/> for a configuration.
        /// <para>
        /// <see cref="BuildOptions.StrictMode"/> is deliberately <em>not</em> set, and this is
        /// the one setting here worth reading the reason for.
        /// </para>
        /// <para>
        /// StrictMode fails the build when any error was logged during it. That sounds like
        /// exactly what a pipeline wants, and the way Unity implements it makes it worse than
        /// useless: the failure is reported as
        /// </para>
        /// <code>
        /// Failed to process scene before export: 'Assets/Scenes/Map_FirstSketch_Solo.unity'
        /// Error building Player: 2 errors
        /// </code>
        /// <para>
        /// — naming a scene that is not the problem and never naming the error that was. It
        /// cost a day. Every scene in this project failed that way, including the near-empty
        /// bootstrap menu, because the error being counted was Mirror's packaging defect
        /// (see <see cref="BuildPipelineKnownDefects"/>) logged before any scene was touched.
        /// A gate that blames an innocent scene and withholds the cause is not a safety net.
        /// </para>
        /// <para>
        /// Nothing is lost by dropping it, because the pipeline enforces the same rule itself
        /// and can say what happened:
        /// </para>
        /// <list type="bullet">
        /// <item><description><c>BuildPipelineRunner.Preflight</c> refuses to build at all when
        /// scripts do not compile, so the "player built from the last successful compile" case
        /// never reaches <c>BuildPlayer</c>.</description></item>
        /// <item><description><c>BuildPipelineRunner.ReportBuildMessages</c> reads every message
        /// off the <c>BuildReport</c>, prints it, and fails the build on any error that is not a
        /// named known defect — with the message in the log and in <c>build-report.txt</c>.</description></item>
        /// </list>
        /// </summary>
        public static BuildOptions OptionsFor(BuildConfigurationId configuration)
        {
            if (configuration == BuildConfigurationId.Development)
            {
                // Development | AllowDebugging gives the debug symbols and lets a managed
                // debugger attach; ConnectWithProfiler is what makes the Profiler find the
                // player at all — §12's monster pathing and §13's voice both need to be
                // measured in a real session rather than in the editor.
                return BuildOptions.Development
                    | BuildOptions.AllowDebugging
                    | BuildOptions.ConnectWithProfiler
                    | BuildOptions.CompressWithLz4;
            }

            // No Development flag: that single bit is what strips the debug symbols, removes
            // the profiler listener and stops the player advertising itself on the network.
            // LZ4HC costs build time and gives the smaller download and faster load.
            return BuildOptions.CompressWithLz4HC;
        }

        /// <summary>Puts every captured setting back, in the reverse order it was applied.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var standalone = NamedBuildTarget.Standalone;

            TryRestore(_macArchitectureProperty, _previousMacArchitecture, "macOS architecture");
            TryRestore(_xcodeProjectProperty, _previousCreateXcodeProject, "createXcodeProject");

            if (_codeGenerationSetter != null && _previousCodeGeneration != null)
            {
                try
                {
                    _codeGenerationSetter.Invoke(null, new[] { (object)standalone, _previousCodeGeneration });
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[BuildPipeline] Could not restore IL2CPP code generation: "
                        + exception.Message);
                }
            }

            PlayerSettings.stripEngineCode = _previousStripEngineCode;
            PlayerSettings.SetManagedStrippingLevel(standalone, _previousStrippingLevel);
            PlayerSettings.SetIl2CppCompilerConfiguration(standalone, _previousCompilerConfiguration);
            PlayerSettings.SetScriptingBackend(standalone, _previousBackend);
        }

        /// <summary>
        /// Selects the macOS architecture through reflection on
        /// <c>UnityEditor.OSXStandalone.UserBuildSettings</c>.
        /// <para>
        /// Reflection rather than a direct reference because that type ships with the macOS
        /// build support module. A <c>using UnityEditor.OSXStandalone</c> on a Windows CI
        /// runner without that module is a compile error in the editor assembly, which takes
        /// down the Windows build too — the one build that matters most.
        /// </para>
        /// </summary>
        private void TryApplyMacArchitecture(string architectureName)
        {
            if (string.IsNullOrEmpty(architectureName))
            {
                return;
            }

            var settingsType = FindType("UnityEditor.OSXStandalone.UserBuildSettings");
            if (settingsType == null)
            {
                _notes.Add("macOS architecture not set: UnityEditor.OSXStandalone.UserBuildSettings is "
                    + "absent, so the macOS build support module is not installed. The produced .app "
                    + "will be whatever the editor defaults to — verify before distributing it.");
                Debug.LogWarning("[BuildPipeline] " + _notes[_notes.Count - 1]);
                return;
            }

            _macArchitectureProperty = settingsType.GetProperty(
                "architecture", BindingFlags.Public | BindingFlags.Static);
            if (_macArchitectureProperty == null || !_macArchitectureProperty.CanWrite)
            {
                _notes.Add("macOS architecture not set: UserBuildSettings.architecture is missing or "
                    + "read-only in this editor version.");
                Debug.LogWarning("[BuildPipeline] " + _notes[_notes.Count - 1]);
                _macArchitectureProperty = null;
                return;
            }

            try
            {
                _previousMacArchitecture = _macArchitectureProperty.GetValue(null);
                var value = Enum.Parse(_macArchitectureProperty.PropertyType, architectureName, ignoreCase: true);
                _macArchitectureProperty.SetValue(null, value);
                MacArchitectureApplied = value.ToString();
                Debug.Log("[BuildPipeline] macOS architecture: " + MacArchitectureApplied);
            }
            catch (Exception exception)
            {
                _notes.Add("macOS architecture '" + architectureName + "' was rejected: " + exception.Message);
                Debug.LogWarning("[BuildPipeline] " + _notes[_notes.Count - 1]);
                _macArchitectureProperty = null;
            }

            // A left-over "generate an Xcode project" setting turns the build into a project
            // instead of an .app, and the pipeline's output path would then point at nothing.
            _xcodeProjectProperty = settingsType.GetProperty(
                "createXcodeProject", BindingFlags.Public | BindingFlags.Static);
            if (_xcodeProjectProperty != null && _xcodeProjectProperty.CanWrite)
            {
                try
                {
                    _previousCreateXcodeProject = _xcodeProjectProperty.GetValue(null);
                    _xcodeProjectProperty.SetValue(null, false);
                }
                catch (Exception)
                {
                    _xcodeProjectProperty = null;
                }
            }
            else
            {
                _xcodeProjectProperty = null;
            }
        }

        /// <summary>
        /// Asks IL2CPP to optimise for speed rather than size. Reflective because the
        /// <c>Il2CppCodeGeneration</c> setter has moved between <c>EditorUserBuildSettings</c>
        /// and <c>PlayerSettings</c> across versions, and the build is worth more than the setting.
        /// </summary>
        private void TryApplyCodeGeneration(string valueName)
        {
            var enumType = FindType("UnityEditor.Build.Il2CppCodeGeneration");
            if (enumType == null)
            {
                return;
            }

            var setter = typeof(PlayerSettings).GetMethod(
                "SetIl2CppCodeGeneration",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(NamedBuildTarget), enumType },
                null);
            var getter = typeof(PlayerSettings).GetMethod(
                "GetIl2CppCodeGeneration",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(NamedBuildTarget) },
                null);

            if (setter == null || getter == null)
            {
                _notes.Add("IL2CPP code generation left at the editor default: "
                    + "PlayerSettings.(Get|Set)Il2CppCodeGeneration is not present in this version.");
                return;
            }

            try
            {
                _previousCodeGeneration = getter.Invoke(null, new object[] { NamedBuildTarget.Standalone });
                var value = Enum.Parse(enumType, valueName, ignoreCase: true);
                setter.Invoke(null, new[] { (object)NamedBuildTarget.Standalone, value });
                _codeGenerationSetter = setter;
            }
            catch (Exception exception)
            {
                _notes.Add("IL2CPP code generation could not be set to " + valueName + ": " + exception.Message);
                _codeGenerationSetter = null;
            }
        }

        private void TryRestore(PropertyInfo property, object previous, string label)
        {
            if (property == null || previous == null)
            {
                return;
            }

            try
            {
                property.SetValue(null, previous);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BuildPipeline] Could not restore " + label + ": " + exception.Message);
            }
        }

        /// <summary>
        /// Finds a type by full name across the loaded assemblies. <see cref="Type.GetType(string)"/>
        /// alone fails here because the assembly holding the type is not this one and its
        /// simple name differs between editor versions.
        /// </summary>
        private static Type FindType(string fullName)
        {
            var direct = Type.GetType(fullName);
            if (direct != null)
            {
                return direct;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var found = assembly.GetType(fullName, throwOnError: false);
                    if (found != null)
                    {
                        return found;
                    }
                }
                catch (Exception)
                {
                    // A dynamic or partially loaded assembly can throw on GetType; the next one
                    // in the list is the one that matters.
                }
            }

            return null;
        }
    }
}
