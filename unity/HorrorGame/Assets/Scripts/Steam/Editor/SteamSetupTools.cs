#nullable enable

using System.Collections.Generic;
using System.Text;
using HorrorGame.Core.Telemetry;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.Steam.EditorTools
{
    /// <summary>
    /// The two things about the Steam layer that are easier to check from a menu item than
    /// from code: which backend a build will get, and what has to be declared on the
    /// Steamworks partner site before §13's telemetry works at all.
    /// </summary>
    public static class SteamSetupTools
    {
        /// <summary>
        /// Reports which backend is compiled in and why.
        /// <para>
        /// Worth a menu item because the failure it diagnoses is silent: with the
        /// Steamworks.NET package missing, everything still compiles and runs — that is the
        /// requirement — and the only symptom is that invites and voice quietly do not
        /// exist. This prints the reason in one line instead of leaving someone to infer it.
        /// </para>
        /// </summary>
        [MenuItem("Horror/Steam/Report Backend", priority = 41)]
        private static void ReportBackend()
        {
            var lines = new List<string>
            {
                "[Steam] Backend report",
                "  App ID:                 " + SteamAppConfig.AppId
                    + (SteamAppConfig.IsDevelopmentAppId ? "  (development — Spacewar, §13)" : "  (release)"),
                "  steam_appid.txt:        " + (SteamAppIdFile.ShouldWrite
                    ? "written to " + SteamAppIdFile.ProjectRoot
                    : "not written (release App ID in a non-development build)"),
            };

#if HORRORGAME_STEAMWORKS
            lines.Add("  HORRORGAME_STEAMWORKS:  defined — the Steamworks backend assembly compiles");
#else
            lines.Add("  HORRORGAME_STEAMWORKS:  NOT defined — com.rlabrecque.steamworks.net is missing, "
                + "so the Steamworks assembly is skipped and every build is offline. "
                + "This is a supported configuration (§14 steps 1–3); install the package to change it.");
#endif

            lines.Add("  Registered backend:     " + (SteamBackendRegistry.BackendName ?? "none")
                + "   (registration happens at runtime init, so 'none' is expected outside play mode)");
            lines.Add("  Service now:            " + (SteamServices.Exists
                ? SteamServices.Current.BackendName + " / " + SteamServices.Current.State
                    + (SteamServices.Current.OfflineReason == null
                        ? string.Empty
                        : " — " + SteamServices.Current.OfflineReason)
                : "not created yet"));

            Debug.Log(string.Join("\n", lines));
        }

        /// <summary>
        /// Prints every stat name §13's stage-1 telemetry writes, for pasting into the
        /// Steamworks partner site.
        /// <para>
        /// This exists because of the one way §13's zero-infrastructure telemetry can fail
        /// completely and silently: Steam discards a write to a stat that was never
        /// declared. The names come from the core's own <see cref="TelemetryBuckets"/>, so
        /// the list cannot drift from what the game actually writes — and
        /// <c>SteamStatsTelemetrySink</c> warns at runtime about any name that is not on it.
        /// </para>
        /// </summary>
        [MenuItem("Horror/Steam/Print Stats Provisioning List", priority = 42)]
        private static void PrintStatsProvisioningList()
        {
            var counters = TelemetryBuckets.AllCounters;
            var text = new StringBuilder();

            text.Append("[Steam] Declare these ").Append(counters.Count)
                .Append(" stats on the Steamworks partner site (type INT, aggregated):\n");

            for (var i = 0; i < counters.Count; i++)
            {
                text.Append("  ").Append(counters[i]).Append('\n');
            }

            text.Append("\n§13: 통계 항목을 정의하면 Steam이 저장하고 글로벌 집계까지 해준다. ")
                .Append("A stat missing from this list is silently discarded, and that match's data is gone.");

            Debug.Log(text.ToString());
        }
    }
}
