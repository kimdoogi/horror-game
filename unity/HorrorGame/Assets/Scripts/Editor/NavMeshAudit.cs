#nullable enable

using System;
using HorrorGame.EditorTools.SceneGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// The front door to the NavMesh connectivity gate: a menu item, and the batch
    /// entry point CI runs.
    /// <para>
    /// The measurement itself is <see cref="NavMeshConnectivity"/>, in the scene
    /// generation assembly, because the generator has to be able to fail on it — a
    /// generated map the monster cannot use is not a map, and B-001 survived several
    /// rebuilds precisely because nothing stopped one being written to disk. This file
    /// stays where the docs and CI point at it and delegates.
    /// </para>
    /// <para>
    /// Read <see cref="NavMeshConnectivity"/> for why a fragmented surface is fatal
    /// rather than cosmetic.
    /// </para>
    /// </summary>
    public static class NavMeshAudit
    {
        /// <summary>Fraction of point pairs that must be fully connected. See <see cref="NavMeshConnectivity"/>.</summary>
        public const float RequiredCompletionRate = NavMeshConnectivity.RequiredCompletionRate;

        /// <summary>Runs the audit on the open scene and logs the report.</summary>
        [MenuItem("Horror/Map/Audit NavMesh Connectivity", priority = 30)]
        public static void AuditOpenScene()
        {
            var report = Audit(SceneManager.GetActiveScene());
            if (report.Passed)
            {
                Debug.Log(report.Describe());
            }
            else
            {
                Debug.LogError(report.Describe());
            }
        }

        /// <summary>
        /// Batch entry point. Opens the scene named by <c>-auditScene</c> and exits
        /// non-zero when the surface is fragmented.
        /// </summary>
        public static void AuditBatch()
        {
            try
            {
                var path = ArgValue("-auditScene") ?? "Assets/Scenes/Map_FirstSketch.unity";
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var report = Audit(scene);

                Debug.Log(report.Describe());
                EditorApplication.Exit(report.Passed ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[NavMeshAudit] " + ex);
                EditorApplication.Exit(2);
            }
        }

        /// <summary>Measures reachability between every pair of gameplay-relevant points.</summary>
        public static NavMeshConnectivity.Report Audit(Scene scene) => NavMeshConnectivity.Audit(scene);

        private static string? ArgValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
