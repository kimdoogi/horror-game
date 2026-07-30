using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Runs the EditMode and PlayMode suites headlessly and exits non-zero when anything
    /// failed, so CI can gate on Unity's tests the same way it gates on <c>dotnet test</c>.
    /// <para>
    /// This lives in its own assembly (see the .asmdef beside this file) for two reasons.
    /// First, <c>UnityEditor.TestRunner</c> is not auto-referenced, so a script in the default
    /// editor assembly cannot see <see cref="TestRunnerApi"/> at all. Second, the assembly is
    /// constrained to <c>UNITY_INCLUDE_TESTS</c>: without it, a clone that has not installed
    /// <c>com.unity.test-framework</c> yet would fail to compile the whole editor assembly,
    /// which would take down <see cref="BuildPipelineRunner"/> and the very
    /// <c>PackageBootstrap</c> menu item that installs the package. That is a deadlock, and
    /// the constraint is what prevents it.
    /// </para>
    /// <para>
    /// The run is a state machine in <see cref="SessionState"/> rather than a loop, because
    /// entering play mode reloads the domain and destroys every registered callback and local
    /// variable. <c>SessionState</c> survives the reload, and
    /// <see cref="ReattachAfterDomainReload"/> re-registers the callbacks on the far side.
    /// </para>
    /// </summary>
    public static class BuildPipelineTestRunner
    {
        /// <summary>Every suite that ran, passed.</summary>
        public const int ExitSuccess = 0;

        /// <summary>An exception nobody predicted.</summary>
        public const int ExitUnexpected = 1;

        /// <summary>At least one test failed. Matches the numbering in <see cref="BuildPipelineRunner"/>.</summary>
        public const int ExitTestsFailed = 6;

        /// <summary><c>-testRequireTests</c> was passed and no test ran at all.</summary>
        public const int ExitNoTests = 9;

        private const string KeyPrefix = "HorrorGame.TestRunner.";
        private const string RemainingKey = KeyPrefix + "Remaining";
        private const string CurrentKey = KeyPrefix + "Current";
        private const string ResultsDirectoryKey = KeyPrefix + "ResultsDirectory";
        private const string BatchKey = KeyPrefix + "Batch";
        private const string RequireTestsKey = KeyPrefix + "RequireTests";
        private const string PassedKey = KeyPrefix + "Passed";
        private const string FailedKey = KeyPrefix + "Failed";
        private const string SkippedKey = KeyPrefix + "Skipped";
        private const string InconclusiveKey = KeyPrefix + "Inconclusive";
        private const string SummaryKey = KeyPrefix + "Summary";
        private const string StartedKey = KeyPrefix + "StartedUtcTicks";

        private const string EditModeSuite = "editmode";
        private const string PlayModeSuite = "playmode";

        /// <summary>Guards against registering the callbacks twice inside one domain, which would double-count.</summary>
        private static bool _callbacksRegistered;

        [MenuItem("Horror/Test/Run EditMode + PlayMode", priority = 100)]
        public static void MenuRunAll()
        {
            Start(EditModeSuite + "," + PlayModeSuite, DefaultResultsDirectory(), batch: false, requireTests: false);
        }

        [MenuItem("Horror/Test/Run EditMode Only", priority = 101)]
        public static void MenuRunEditMode()
        {
            Start(EditModeSuite, DefaultResultsDirectory(), batch: false, requireTests: false);
        }

        [MenuItem("Horror/Test/Run PlayMode Only", priority = 102)]
        public static void MenuRunPlayMode()
        {
            Start(PlayModeSuite, DefaultResultsDirectory(), batch: false, requireTests: false);
        }

        /// <summary>
        /// Batch-mode entry point:
        /// <code>
        /// Unity -batchmode -projectPath unity/HorrorGame -logFile - \
        ///       -executeMethod HorrorGame.EditorTools.BuildPipelineTestRunner.RunFromCommandLine \
        ///       -testSuites editmode,playmode
        /// </code>
        /// <para>
        /// Arguments: <c>-testSuites</c> (default <c>editmode,playmode</c>),
        /// <c>-testResultsDir &lt;path&gt;</c> (default <c>dist/test-results</c>),
        /// <c>-testRequireTests</c> (treat "no tests ran" as a failure).
        /// </para>
        /// <para>
        /// <b>Do not pass <c>-quit</c>.</b> A test run is asynchronous and finishes in a
        /// callback long after this method returns; <c>-quit</c> would close the editor
        /// mid-run and report success. The exit code comes from <see cref="Finish"/>.
        /// </para>
        /// </summary>
        public static void RunFromCommandLine()
        {
            try
            {
                var argv = Environment.GetCommandLineArgs();
                var suites = ReadArgument(argv, "-testSuites", EditModeSuite + "," + PlayModeSuite);
                var resultsDirectory = ReadArgument(argv, "-testResultsDir", DefaultResultsDirectory());
                var requireTests = HasFlag(argv, "-testRequireTests");

                var normalized = NormalizeSuites(suites);
                if (normalized.Length == 0)
                {
                    Debug.LogError("[TestRunner] -testSuites '" + suites + "' names no known suite. "
                        + "Valid: editmode, playmode (comma-separated).");
                    EditorApplication.Exit(ExitUnexpected);
                    return;
                }

                Start(string.Join(",", normalized), resultsDirectory, batch: true, requireTests: requireTests);
            }
            catch (Exception exception)
            {
                Debug.LogError("[TestRunner] Unhandled exception starting the run:\n" + exception);
                EditorApplication.Exit(ExitUnexpected);
            }
        }

        /// <summary>
        /// Re-registers the callbacks after a domain reload. Entering play mode reloads the
        /// domain, so without this the PlayMode suite would run to completion and its
        /// <c>RunFinished</c> would arrive at nobody — the process would then sit in batch mode
        /// until the CI timeout killed it, having reported nothing.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ReattachAfterDomainReload()
        {
            if (SessionState.GetString(CurrentKey, string.Empty).Length == 0
                && SessionState.GetString(RemainingKey, string.Empty).Length == 0)
            {
                return;
            }

            EnsureCallbacks();
        }

        private static void Start(string suites, string resultsDirectory, bool batch, bool requireTests)
        {
            SessionState.SetString(RemainingKey, suites);
            SessionState.SetString(CurrentKey, string.Empty);
            SessionState.SetString(ResultsDirectoryKey, resultsDirectory);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(RequireTestsKey, requireTests);
            SessionState.SetInt(PassedKey, 0);
            SessionState.SetInt(FailedKey, 0);
            SessionState.SetInt(SkippedKey, 0);
            SessionState.SetInt(InconclusiveKey, 0);
            SessionState.SetString(SummaryKey, string.Empty);
            SessionState.SetString(StartedKey, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));

            Directory.CreateDirectory(resultsDirectory);

            Debug.Log("[TestRunner] Suites: " + suites
                + "\n  results : " + resultsDirectory.Replace('\\', '/')
                + "\n  batch   : " + batch
                + "\n  unity   : " + Application.unityVersion);

            RunNextSuite();
        }

        private static void RunNextSuite()
        {
            var remaining = SessionState.GetString(RemainingKey, string.Empty);
            if (remaining.Length == 0)
            {
                Finish();
                return;
            }

            var parts = remaining.Split(',');
            var suite = parts[0];
            SessionState.SetString(RemainingKey, parts.Length > 1
                ? string.Join(",", parts, 1, parts.Length - 1)
                : string.Empty);
            SessionState.SetString(CurrentKey, suite);

            var mode = suite == PlayModeSuite ? TestMode.PlayMode : TestMode.EditMode;
            EnsureCallbacks();

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();

            // Ask what exists before executing. A filter that matches nothing does not
            // reliably produce a RunFinished callback, and in batch mode "no callback" means
            // the process hangs until CI kills it — indistinguishable from a slow test.
            api.RetrieveTestList(mode, root =>
            {
                var count = root == null ? 0 : root.TestCaseCount;
                if (count == 0)
                {
                    Append(suite + ": no tests found (0 test cases). Nothing was run.");
                    Debug.LogWarning("[TestRunner] " + suite + " has no tests. Assets/Tests/"
                        + (mode == TestMode.PlayMode ? "PlayMode" : "EditMode")
                        + " is empty or its assembly definition is missing. Pass -testRequireTests "
                        + "to make this a failure.");
                    SessionState.SetString(CurrentKey, string.Empty);
                    EditorApplication.delayCall += RunNextSuite;
                    return;
                }

                Debug.Log("[TestRunner] Running " + suite + ": " + count + " test case(s).");
                var runner = ScriptableObject.CreateInstance<TestRunnerApi>();
                runner.Execute(new ExecutionSettings(new Filter { testMode = mode }));
            });
        }

        private static void EnsureCallbacks()
        {
            if (_callbacksRegistered)
            {
                return;
            }

            _callbacksRegistered = true;
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
        }

        /// <summary>
        /// Records one suite's result, writes its NUnit XML, and moves on. Called from
        /// <see cref="Callbacks.RunFinished"/>.
        /// </summary>
        private static void OnSuiteFinished(ITestResultAdaptor result)
        {
            var suite = SessionState.GetString(CurrentKey, string.Empty);
            if (suite.Length == 0)
            {
                // A run somebody started from the Test Runner window, not from here.
                return;
            }

            SessionState.SetString(CurrentKey, string.Empty);

            var passed = result.PassCount;
            var failed = result.FailCount;
            var skipped = result.SkipCount;
            var inconclusive = result.InconclusiveCount;

            SessionState.SetInt(PassedKey, SessionState.GetInt(PassedKey, 0) + passed);
            SessionState.SetInt(FailedKey, SessionState.GetInt(FailedKey, 0) + failed);
            SessionState.SetInt(SkippedKey, SessionState.GetInt(SkippedKey, 0) + skipped);
            SessionState.SetInt(InconclusiveKey, SessionState.GetInt(InconclusiveKey, 0) + inconclusive);

            var xmlPath = WriteResultsXml(suite, result);

            Append(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-9} {1,4} passed  {2,4} failed  {3,4} skipped  {4,4} inconclusive  in {5:0.0}s  -> {6}",
                suite,
                passed,
                failed,
                skipped,
                inconclusive,
                result.Duration,
                xmlPath.Length == 0 ? "(no xml)" : xmlPath.Replace('\\', '/')));

            EditorApplication.delayCall += RunNextSuite;
        }

        private static void Finish()
        {
            var failed = SessionState.GetInt(FailedKey, 0);
            var passed = SessionState.GetInt(PassedKey, 0);
            var skipped = SessionState.GetInt(SkippedKey, 0);
            var inconclusive = SessionState.GetInt(InconclusiveKey, 0);
            var requireTests = SessionState.GetBool(RequireTestsKey, false);
            var batch = SessionState.GetBool(BatchKey, false);
            var total = passed + failed + skipped + inconclusive;

            var elapsed = TimeSpan.Zero;
            if (long.TryParse(SessionState.GetString(StartedKey, string.Empty), out var ticks))
            {
                elapsed = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            }

            var summary = new StringBuilder();
            summary.AppendLine("[TestRunner] Summary");
            summary.AppendLine(SessionState.GetString(SummaryKey, string.Empty).TrimEnd());
            summary.AppendLine("  total: " + total + " (" + passed + " passed, " + failed + " failed, "
                + skipped + " skipped, " + inconclusive + " inconclusive) in "
                + elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");

            var exitCode = ExitSuccess;
            if (failed > 0)
            {
                exitCode = ExitTestsFailed;
            }
            else if (total == 0 && requireTests)
            {
                // "No tests ran" passing quietly is the same class of failure as a Mono build
                // shipping quietly: green CI that verified nothing.
                exitCode = ExitNoTests;
                summary.AppendLine("  -testRequireTests was passed and no test ran anywhere.");
            }
            else if (total == 0)
            {
                summary.AppendLine("  WARNING: no test ran. Unity's suites are empty; the rules are "
                    + "still covered by dotnet test against Assets/Scripts/Core.");
            }

            ClearState();

            if (exitCode == ExitSuccess)
            {
                Debug.Log(summary.ToString());
            }
            else
            {
                Debug.LogError(summary.ToString());
            }

            if (batch)
            {
                Debug.Log("[TestRunner] Exiting with code " + exitCode + ".");
                EditorApplication.Exit(exitCode);
            }
        }

        /// <summary>
        /// Writes the suite's NUnit XML next to the other build artefacts, so a CI job can
        /// publish per-test results instead of a single red cross.
        /// </summary>
        private static string WriteResultsXml(string suite, ITestResultAdaptor result)
        {
            var directory = SessionState.GetString(ResultsDirectoryKey, DefaultResultsDirectory());
            var path = Path.Combine(directory, suite + "-results.xml");

            try
            {
                Directory.CreateDirectory(directory);

                // TNode.ToString() is the inherited object.ToString() — it yields the literal
                // string "NUnit.Framework.Interfaces.TNode", not markup. Writing that produced
                // a 32-byte file that every XML consumer parsed as zero tests and reported as
                // a green run, which is the one failure mode a test report must not have.
                // OuterXml is the serialiser.
                File.WriteAllText(path, result.ToXml().OuterXml);
                return path;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[TestRunner] Could not write " + path + ": " + exception.Message);
                return string.Empty;
            }
        }

        private static void Append(string line)
        {
            var existing = SessionState.GetString(SummaryKey, string.Empty);
            SessionState.SetString(SummaryKey, existing + "  " + line + "\n");
        }

        private static void ClearState()
        {
            SessionState.EraseString(RemainingKey);
            SessionState.EraseString(CurrentKey);
            SessionState.EraseString(ResultsDirectoryKey);
            SessionState.EraseBool(BatchKey);
            SessionState.EraseBool(RequireTestsKey);
            SessionState.EraseInt(PassedKey);
            SessionState.EraseInt(FailedKey);
            SessionState.EraseInt(SkippedKey);
            SessionState.EraseInt(InconclusiveKey);
            SessionState.EraseString(SummaryKey);
            SessionState.EraseString(StartedKey);
        }

        private static string[] NormalizeSuites(string raw)
        {
            var accepted = new List<string>();
            foreach (var token in raw.Split(','))
            {
                switch (token.Trim().ToLowerInvariant())
                {
                    case "edit":
                    case "editmode":
                        accepted.Add(EditModeSuite);
                        break;

                    case "play":
                    case "playmode":
                        accepted.Add(PlayModeSuite);
                        break;

                    case "all":
                        accepted.Add(EditModeSuite);
                        accepted.Add(PlayModeSuite);
                        break;
                }
            }

            // EditMode first: it is seconds rather than minutes, and a broken adapter there
            // usually explains the PlayMode failures too.
            var ordered = new List<string>();
            if (accepted.Contains(EditModeSuite))
            {
                ordered.Add(EditModeSuite);
            }

            if (accepted.Contains(PlayModeSuite))
            {
                ordered.Add(PlayModeSuite);
            }

            return ordered.ToArray();
        }

        /// <summary>
        /// <c>&lt;repository&gt;/dist/test-results</c>.
        /// <para>
        /// The path is derived here rather than by calling <c>BuildPipelinePaths</c>: this is a
        /// separate assembly, and an .asmdef cannot reference the default editor assembly the
        /// rest of the pipeline compiles into. Duplicating six lines is cheaper than moving
        /// the build pipeline into an assembly definition another area of the project would
        /// then have to reference.
        /// </para>
        /// </summary>
        private static string DefaultResultsDirectory()
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                    || File.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    return Path.Combine(directory.FullName, "dist", "test-results");
                }

                directory = directory.Parent;
            }

            return Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")),
                "dist", "test-results");
        }

        private static string ReadArgument(string[] argv, string name, string fallback)
        {
            for (var i = 0; i < argv.Length - 1; i++)
            {
                if (argv[i] == name && !argv[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    return argv[i + 1];
                }
            }

            return fallback;
        }

        private static bool HasFlag(string[] argv, string name)
        {
            foreach (var argument in argv)
            {
                if (argument == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Bridges the test framework's callbacks into the <see cref="SessionState"/> state
        /// machine. Only failures are logged per test: a passing suite printing every name
        /// buries the one line that matters.
        /// </summary>
        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log("[TestRunner] Run started: " + (testsToRun == null ? 0 : testsToRun.TestCaseCount)
                    + " test case(s).");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                OnSuiteFinished(result);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.Test.IsSuite || result.TestStatus == TestStatus.Passed)
                {
                    return;
                }

                var text = new StringBuilder();
                text.Append("[TestRunner] ").Append(result.TestStatus).Append(": ")
                    .Append(result.Test.FullName);

                if (!string.IsNullOrEmpty(result.Message))
                {
                    text.Append("\n  ").Append(result.Message.Replace("\n", "\n  "));
                }

                if (!string.IsNullOrEmpty(result.StackTrace))
                {
                    text.Append("\n  ").Append(result.StackTrace.Replace("\n", "\n  "));
                }

                if (result.TestStatus == TestStatus.Failed)
                {
                    Debug.LogError(text.ToString());
                }
                else
                {
                    Debug.LogWarning(text.ToString());
                }
            }
        }
    }
}
