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
    /// The front door to the player-traversability gate: a menu item, and the batch
    /// entry point CI runs.
    /// <para>
    /// The sibling of <see cref="NavMeshAudit"/>, and it exists because that one is not
    /// enough. There are two actors in this game and the NavMesh describes one of them:
    /// the baked agent climbs <c>agentClimb</c> 0.75 m and stands 2.00 m, the player's
    /// <c>CharacterController</c> climbs <c>stepOffset</c> 0.40 m and stands 1.75 m.
    /// A building can therefore score 1830/1830 with a single island — a perfect
    /// antagonist surface — while a human cannot get off the ground floor, and nothing
    /// in the project noticed until someone played it.
    /// </para>
    /// <para>
    /// Run both. The measurement is <see cref="PlayerTraversal"/>, in the scene
    /// generation assembly, so <see cref="MapSceneGenerator"/> can refuse to write a
    /// map that fails it — the same arrangement, and for the same reason, as
    /// <see cref="NavMeshConnectivity"/>.
    /// </para>
    /// </summary>
    public static class PlayerReachAudit
    {
        /// <summary>Runs the sweep on the open scene and logs the report.</summary>
        [MenuItem("Horror/Map/Audit Player Reachability", priority = 31)]
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
        /// non-zero when a player's capsule cannot reach every storey, every 후보 지점
        /// and every 전리품 spawn from the 출입구.
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
                Debug.LogError("[PlayerReach] " + ex);
                EditorApplication.Exit(2);
            }
        }

        /// <summary>Sweeps the real player capsule out from the 출입구.</summary>
        public static PlayerTraversal.Report Audit(Scene scene) => PlayerTraversal.Audit(scene);

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
