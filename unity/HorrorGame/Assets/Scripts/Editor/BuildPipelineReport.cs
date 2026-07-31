using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// What one build actually produced, written next to the player itself.
    /// <para>
    /// The report exists because the interesting questions about a build are asked days
    /// later, by somebody looking at a folder: which commit is this, was it IL2CPP, is this
    /// the Spacewar App ID, why is it 400 MB. A CI log has scrolled away by then. Both a
    /// human-readable and a JSON copy are written — the JSON so a later step can gate on
    /// <c>shippable</c> without parsing prose.
    /// </para>
    /// </summary>
    public sealed class BuildPipelineReport
    {
        /// <summary>Human-readable copy, dropped in the platform's output folder.</summary>
        public const string TextFileName = "build-report.txt";

        /// <summary>Machine-readable copy for CI gates and depot scripts.</summary>
        public const string JsonFileName = "build-report.json";

        public BuildPlatformId Platform { get; set; }
        public BuildConfigurationId Configuration { get; set; }

        /// <summary>"IL2CPP" or "Mono".</summary>
        public string BackendName { get; set; } = string.Empty;

        /// <summary>Why that backend, in one sentence.</summary>
        public string BackendReason { get; set; } = string.Empty;

        /// <summary>True when IL2CPP was wanted and the host could not produce it.</summary>
        public bool MonoFallback { get; set; }

        /// <summary>The full fallback warning, repeated here so the folder carries it.</summary>
        public string ShippingWarning { get; set; } = string.Empty;

        public string UnityVersion { get; set; } = string.Empty;
        public string HostDescription { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;
        public string VersionSource { get; set; } = string.Empty;
        public string BuildNumber { get; set; } = string.Empty;
        public string GitCommit { get; set; } = string.Empty;
        public string GitBranch { get; set; } = string.Empty;
        public bool GitDirty { get; set; }

        public string SteamAppId { get; set; } = string.Empty;
        public string SteamAppIdSource { get; set; } = string.Empty;

        /// <summary>Empty on Windows; the applied macOS architecture otherwise.</summary>
        public string MacArchitecture { get; set; } = string.Empty;

        public string[] Scenes { get; set; } = Array.Empty<string>();
        public string OutputDirectory { get; set; } = string.Empty;
        public string PlayerPath { get; set; } = string.Empty;

        /// <summary>Wall-clock time for this platform, including scene collection and the clean.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>What Unity itself reported for the build step alone.</summary>
        public TimeSpan UnityReportedDuration { get; set; }

        /// <summary>Bytes on disk under <see cref="OutputDirectory"/> — the download size.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Unity's own packed-size figure, kept because the two differ and both are useful.</summary>
        public ulong UnityReportedSizeBytes { get; set; }

        public int Errors { get; set; }
        public int Warnings { get; set; }

        /// <summary>
        /// Errors that are this project's problem: <see cref="Errors"/> minus the ones
        /// <see cref="BuildPipelineKnownDefects"/> recognises. This is the number the build
        /// is failed on, and the difference between the two is <see cref="KnownDefects"/>.
        /// </summary>
        public int FatalErrors { get; set; }

        /// <summary>
        /// Errors from third-party packages that are understood, harmless and unfixable from
        /// this repository — listed in full, with their explanation, so tolerating them stays a
        /// visible decision rather than a silence.
        /// </summary>
        public List<string> KnownDefects { get; } = new List<string>();

        /// <summary>Unity's <c>BuildResult</c> as text: Succeeded, Failed, Cancelled or Unknown.</summary>
        public string Result { get; set; } = "Unknown";

        public bool Succeeded { get; set; }

        /// <summary>Everything the pipeline could not do, verbatim, so a gap is never silent.</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>Top-level names in the output folder, so a truncated build is visible in the report.</summary>
        public List<string> OutputEntries { get; } = new List<string>();

        /// <summary>UTC, ISO 8601. Local time in a report is unreadable across a CI runner and a laptop.</summary>
        public string TimestampUtc { get; set; } = string.Empty;

        /// <summary>
        /// The one flag a release process should gate on: a succeeded Release build on IL2CPP.
        /// Everything else is a build you can play, not a build you can publish.
        /// </summary>
        public bool ShippableOnSteam
        {
            get { return Succeeded && Configuration == BuildConfigurationId.Release && !MonoFallback; }
        }

        /// <summary>One line for the end-of-run summary table.</summary>
        public string OneLineSummary
        {
            get
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-42} {1,-11} {2,-6} {3,-9} {4,8:0.00} MB  {5:0}s",
                    BuildPipelineTargets.DisplayName(Platform),
                    Configuration,
                    BackendName,
                    Succeeded ? "OK" : "FAILED",
                    BuildPipelinePaths.ToMegabytes(SizeBytes),
                    Duration.TotalSeconds);
            }
        }

        /// <summary>
        /// Writes both copies into <paramref name="directory"/> and returns the text path.
        /// Called for failed builds too — a failed build's report is the one worth reading.
        /// </summary>
        public string WriteTo(string directory)
        {
            Directory.CreateDirectory(directory);

            var textPath = Path.Combine(directory, TextFileName);
            File.WriteAllText(textPath, BuildText());
            File.WriteAllText(Path.Combine(directory, JsonFileName), BuildJson());
            return textPath;
        }

        private string BuildText()
        {
            var text = new StringBuilder();
            text.AppendLine("HorrorGame build report");
            text.AppendLine("=======================");
            text.AppendLine();
            Line(text, "result", Result + (Succeeded ? string.Empty : "  <-- BUILD FAILED"));
            Line(text, "platform", BuildPipelineTargets.DisplayName(Platform)
                + " (" + BuildPipelineTargets.FolderName(Platform) + ")");
            if (MacArchitecture.Length > 0)
            {
                Line(text, "architecture", MacArchitecture);
            }

            Line(text, "configuration", Configuration.ToString());
            Line(text, "scripting backend", BackendName);
            Line(text, "backend reason", BackendReason);
            Line(text, "shippable on Steam", ShippableOnSteam ? "yes" : "no — " + NotShippableReason());
            text.AppendLine();
            Line(text, "version", Version + "   (" + VersionSource + ")");
            Line(text, "build number", BuildNumber);
            Line(text, "git commit", GitCommit + (GitDirty ? "  (working tree dirty)" : string.Empty));
            Line(text, "git branch", GitBranch);
            Line(text, "unity version", UnityVersion);
            Line(text, "host", HostDescription);
            Line(text, "built at (UTC)", TimestampUtc);
            text.AppendLine();
            Line(text, "duration", FormatDuration(Duration)
                + (UnityReportedDuration > TimeSpan.Zero
                    ? "   (unity build step: " + FormatDuration(UnityReportedDuration) + ")"
                    : string.Empty));
            // Measured before this report is written, so the number is the player's size and
            // does not drift by a few kilobytes every time the report grows.
            Line(text, "size on disk", BuildPipelinePaths.ToMegabytes(SizeBytes).ToString("0.00", CultureInfo.InvariantCulture)
                + " MB   (" + SizeBytes.ToString(CultureInfo.InvariantCulture) + " bytes, this report excluded)");
            if (UnityReportedSizeBytes > 0)
            {
                Line(text, "size (unity)", BuildPipelinePaths.ToMegabytes((long)UnityReportedSizeBytes)
                    .ToString("0.00", CultureInfo.InvariantCulture) + " MB");
            }

            Line(text, "errors / warnings", Errors + " / " + Warnings
                + (KnownDefects.Count > 0
                    ? "   (" + FatalErrors + " this project's, " + KnownDefects.Count
                      + " known third-party defect(s), listed below)"
                    : string.Empty));
            text.AppendLine();
            Line(text, "output", BuildPipelinePaths.Normalize(OutputDirectory));
            Line(text, "player", BuildPipelinePaths.Normalize(PlayerPath));
            Line(text, "steam app id", SteamAppId + "   (" + SteamAppIdSource + ")");
            text.AppendLine();

            text.AppendLine("scenes (" + Scenes.Length + ", in load order)");
            for (var i = 0; i < Scenes.Length; i++)
            {
                text.AppendLine("  " + i + ": " + Scenes[i]);
            }

            if (OutputEntries.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("output folder contents (listed before this report was added)");
                foreach (var entry in OutputEntries)
                {
                    text.AppendLine("  " + entry);
                }
            }

            if (Notes.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("notes");
                foreach (var note in Notes)
                {
                    text.AppendLine("  * " + note);
                }
            }

            if (KnownDefects.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("known third-party defects (reported, tolerated, did NOT fail the build)");
                foreach (var defect in KnownDefects)
                {
                    text.AppendLine("  * " + defect);
                }

                // Once, after the list. Unity re-logs the same defect in several build steps,
                // and repeating the paragraph per occurrence buries the list it explains.
                // The stored lines contain the original message verbatim, so the same
                // predicate that classified them also finds the explanation.
                foreach (var explanation in DistinctExplanations())
                {
                    text.AppendLine();
                    text.AppendLine("  why this is tolerated: " + explanation);
                }
            }

            if (ShippingWarning.Length > 0)
            {
                text.AppendLine();
                text.AppendLine(ShippingWarning);
            }

            return text.ToString();
        }

        private string BuildJson()
        {
            var json = new StringBuilder();
            json.AppendLine("{");
            JsonLine(json, "result", Result, true);
            JsonBool(json, "succeeded", Succeeded, true);
            JsonBool(json, "shippableOnSteam", ShippableOnSteam, true);
            JsonLine(json, "platform", BuildPipelineTargets.DisplayName(Platform), true);
            JsonLine(json, "platformFolder", BuildPipelineTargets.FolderName(Platform), true);
            JsonLine(json, "macArchitecture", MacArchitecture, true);
            JsonLine(json, "configuration", Configuration.ToString(), true);
            JsonLine(json, "scriptingBackend", BackendName, true);
            JsonLine(json, "backendReason", BackendReason, true);
            JsonBool(json, "monoFallback", MonoFallback, true);
            JsonLine(json, "version", Version, true);
            JsonLine(json, "versionSource", VersionSource, true);
            JsonLine(json, "buildNumber", BuildNumber, true);
            JsonLine(json, "gitCommit", GitCommit, true);
            JsonLine(json, "gitBranch", GitBranch, true);
            JsonBool(json, "gitDirty", GitDirty, true);
            JsonLine(json, "unityVersion", UnityVersion, true);
            JsonLine(json, "host", HostDescription, true);
            JsonLine(json, "builtAtUtc", TimestampUtc, true);
            JsonNumber(json, "durationSeconds", Math.Round(Duration.TotalSeconds, 2), true);
            JsonNumber(json, "sizeBytes", SizeBytes, true);
            JsonNumber(json, "sizeMegabytes", BuildPipelinePaths.ToMegabytes(SizeBytes), true);
            JsonNumber(json, "unityReportedSizeBytes", (double)UnityReportedSizeBytes, true);
            JsonNumber(json, "errors", Errors, true);
            JsonNumber(json, "fatalErrors", FatalErrors, true);
            JsonNumber(json, "warnings", Warnings, true);
            JsonLine(json, "outputDirectory", BuildPipelinePaths.Normalize(OutputDirectory), true);
            JsonLine(json, "playerPath", BuildPipelinePaths.Normalize(PlayerPath), true);
            JsonLine(json, "steamAppId", SteamAppId, true);
            JsonLine(json, "steamAppIdSource", SteamAppIdSource, true);
            JsonArray(json, "scenes", Scenes, true);
            JsonArray(json, "notes", Notes.ToArray(), true);
            JsonArray(json, "knownThirdPartyDefects", KnownDefects.ToArray(), true);
            JsonLine(json, "shippingWarning", ShippingWarning, false);
            json.AppendLine("}");
            return json.ToString();
        }

        /// <summary>
        /// The explanations for <see cref="KnownDefects"/>, each once, in first-seen order.
        /// </summary>
        private List<string> DistinctExplanations()
        {
            var seen = new List<string>();
            foreach (var defect in KnownDefects)
            {
                var explanation = BuildPipelineKnownDefects.ExplanationFor(defect);
                if (explanation.Length > 0 && !seen.Contains(explanation))
                {
                    seen.Add(explanation);
                }
            }

            return seen;
        }

        private string NotShippableReason()
        {
            if (!Succeeded)
            {
                return "the build failed";
            }

            if (Configuration != BuildConfigurationId.Release)
            {
                return "this is a Development build (debug symbols and profiler are in it)";
            }

            return "IL2CPP was unavailable on this host, so it is a Mono build";
        }

        private static void Line(StringBuilder text, string key, string value)
        {
            text.AppendLine((key + ":").PadRight(22) + value);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalSeconds < 60
                ? duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
                : ((int)duration.TotalMinutes) + "m " + duration.Seconds + "s";
        }

        private static void JsonLine(StringBuilder json, string key, string value, bool comma)
        {
            json.AppendLine("  \"" + key + "\": \"" + Escape(value) + "\"" + (comma ? "," : string.Empty));
        }

        private static void JsonBool(StringBuilder json, string key, bool value, bool comma)
        {
            json.AppendLine("  \"" + key + "\": " + (value ? "true" : "false") + (comma ? "," : string.Empty));
        }

        private static void JsonNumber(StringBuilder json, string key, double value, bool comma)
        {
            json.AppendLine("  \"" + key + "\": " + value.ToString("0.##", CultureInfo.InvariantCulture)
                + (comma ? "," : string.Empty));
        }

        private static void JsonArray(StringBuilder json, string key, string[] values, bool comma)
        {
            if (values.Length == 0)
            {
                json.AppendLine("  \"" + key + "\": []" + (comma ? "," : string.Empty));
                return;
            }

            json.AppendLine("  \"" + key + "\": [");
            for (var i = 0; i < values.Length; i++)
            {
                json.AppendLine("    \"" + Escape(values[i]) + "\""
                    + (i < values.Length - 1 ? "," : string.Empty));
            }

            json.AppendLine("  ]" + (comma ? "," : string.Empty));
        }

        /// <summary>
        /// Escapes for JSON by hand. <see cref="JsonUtility"/> would need a serialisable mirror
        /// class and would not give a stable key order, and a report that reorders its keys
        /// between builds is useless in a diff.
        /// </summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var escaped = new StringBuilder(value.Length + 16);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': escaped.Append("\\\""); break;
                    case '\\': escaped.Append("\\\\"); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default:
                        if (character < ' ')
                        {
                            escaped.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            escaped.Append(character);
                        }

                        break;
                }
            }

            return escaped.ToString();
        }
    }
}
