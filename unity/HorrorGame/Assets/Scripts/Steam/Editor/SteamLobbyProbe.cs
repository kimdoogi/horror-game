#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.Steam.EditorTools
{
    /// <summary>
    /// Asks Steam one question: can this App ID actually create a lobby?
    /// <para>
    /// <b>Why this exists.</b> <see cref="SteamAppConfig"/> states as fact that
    /// <c>480</c> (Spacewar) "already allows 로비 · P2P · 음성 테스트", and the whole
    /// join flow in <c>SteamworksLobbyService</c> is built on that: create a lobby,
    /// publish the host id into lobby data, let a friend join through the overlay. If
    /// lobby creation is refused on the borrowed App ID, none of that path can be
    /// tested and the symptom is silence — <c>CreateLobby</c> returns true, the call
    /// is accepted, and the result callback simply never says OK.
    /// </para>
    /// <para>
    /// That is not an idle worry. FizzySteamworks — the transport this project
    /// actually ships — has an open report of <c>CreateLobby</c> answering
    /// <c>k_EResultAccessDenied</c> on exactly App ID 480, and several engine
    /// communities describe the same restriction. Valve's own documentation says
    /// nothing either way, so the disagreement cannot be settled by reading. It can be
    /// settled by asking Steam once, which is all this does.
    /// </para>
    /// <para>
    /// <b>It is a measurement, not a feature.</b> Nothing in the game calls it. It
    /// creates a lobby, reports what came back, and leaves it again.
    /// </para>
    /// </summary>
    public static class SteamLobbyProbe
    {
        /// <summary>How long to wait for one lobby result before calling it a timeout.</summary>
        private const int TimeoutMs = 20_000;

        /// <summary>How often to pump Steam's callbacks while waiting.</summary>
        private const int PollMs = 50;

        /// <summary>Exit code: a lobby was created, so this App ID permits matchmaking.</summary>
        private const int ExitLobbiesWork = 0;

        /// <summary>Exit code: every attempt was refused. The join flow cannot be tested on this App ID.</summary>
        private const int ExitLobbiesRefused = 1;

        /// <summary>Exit code: nothing was measured — Steam was not reachable, so the question stands open.</summary>
        private const int ExitNotMeasured = 2;

        /// <summary>
        /// The three visibilities the game uses, in the order that matters. §13 ships
        /// <see cref="LobbyVisibility.FriendsOnly"/>, so that one decides the answer;
        /// the other two are attempted anyway because a restriction that applies to one
        /// lobby type and not another is worth knowing rather than guessing.
        /// </summary>
        private static readonly LobbyVisibility[] Visibilities =
        {
            LobbyVisibility.FriendsOnly,
            LobbyVisibility.Public,
            LobbyVisibility.InviteOnly,
        };

        /// <summary>Runs the probe from the editor and prints the report to the console.</summary>
        [MenuItem("Horror/Steam/Probe Lobby Creation", priority = 43)]
        private static void Probe()
        {
            Run(false);
        }

        /// <summary>
        /// Batch entry point. Exits <see cref="ExitLobbiesWork"/> when a lobby was
        /// created, <see cref="ExitLobbiesRefused"/> when every attempt was refused, and
        /// <see cref="ExitNotMeasured"/> when Steam could not be reached at all — which
        /// is a third outcome on purpose, because "no answer" and "no" are different
        /// facts and only one of them justifies paying $100.
        /// </summary>
        public static void ProbeBatch()
        {
            EditorApplication.Exit(Run(true));
        }

        private static int Run(bool batch)
        {
            var report = new List<string>
            {
                "[LobbyProbe] App ID " + SteamAppConfig.AppId
                    + (SteamAppConfig.IsDevelopmentAppId ? "  (Spacewar — the borrowed one)" : "  (real)"),
            };

            EnsureBackendRegistered(report);

            var service = SteamServices.Current;
            report.Add("  backend:  " + service.BackendName + " / " + service.State);

            if (!service.IsOnline)
            {
                report.Add("  online:   NO — " + (service.OfflineReason ?? "unknown"));
                report.Add(string.Empty);
                report.Add("  NOT MEASURED. Steam has to be running and signed in for this to mean");
                report.Add("  anything. Start the Steam client, then run this again.");
                Emit(report, batch, LogType.Warning);
                return ExitNotMeasured;
            }

            report.Add("  online:   yes — " + service.Identity.LocalName + " (" + service.Identity.LocalId + ")");
            report.Add(string.Empty);

            var anySucceeded = false;
            var lobbies = service.Lobbies;

            foreach (var visibility in Visibilities)
            {
                var outcome = AttemptOne(service, lobbies, visibility);
                report.Add("  " + visibility.ToString().PadRight(12) + " → " + outcome.Line);
                anySucceeded |= outcome.Succeeded;
            }

            report.Add(string.Empty);
            report.AddRange(anySucceeded ? VerdictWorks() : VerdictRefused());

            Emit(report, batch, anySucceeded ? LogType.Log : LogType.Warning);
            SteamServices.ShutdownAndReset();
            return anySucceeded ? ExitLobbiesWork : ExitLobbiesRefused;
        }

        /// <summary>
        /// One create-and-leave round trip.
        /// <para>
        /// The wait is a blocking pump rather than a coroutine because this runs from
        /// <c>-executeMethod</c>, where there is no play mode and therefore no frame
        /// loop to hang a coroutine on. <see cref="ISteamService.RunCallbacks"/> both
        /// pumps Steam and drains the lobby service's own deferred-event queue, so this
        /// is the same delivery path the game uses — not a shortcut around it.
        /// </para>
        /// </summary>
        private static Outcome AttemptOne(ISteamService service, ILobbyService lobbies, LobbyVisibility visibility)
        {
            LobbyResult? answer = null;
            void OnCreated(LobbyResult result) => answer = result;

            lobbies.LobbyCreated += OnCreated;
            try
            {
                if (!lobbies.CreateLobby(visibility, HorrorGame.Core.GameConstants.PlayersPerMatch))
                {
                    return new Outcome(false, "the request could not even be issued");
                }

                var waited = 0;
                while (answer == null && waited < TimeoutMs)
                {
                    service.RunCallbacks();
                    Thread.Sleep(PollMs);
                    waited += PollMs;
                }

                if (answer == null)
                {
                    return new Outcome(false, "NO ANSWER after " + (TimeoutMs / 1000) + "s — the callback never fired");
                }

                var result = answer.Value;
                if (!result.Success)
                {
                    return new Outcome(false, "REFUSED — " + (result.Error ?? "no reason given"));
                }

                lobbies.LeaveLobby();
                service.RunCallbacks();
                return new Outcome(true, "created, id " + result.LobbyId);
            }
            finally
            {
                lobbies.LobbyCreated -= OnCreated;
            }
        }

        /// <summary>
        /// Wakes the backend by hand.
        /// <para>
        /// <c>SteamworksBackendInstaller</c> registers from a
        /// <c>RuntimeInitializeOnLoadMethod</c>, which the editor runs on entering play
        /// mode and never in a <c>-executeMethod</c> batch. Without this the probe would
        /// measure <c>NullSteamService</c> and cheerfully report that lobbies are
        /// refused — the wrong answer, arrived at convincingly, which is the worst kind.
        /// </para>
        /// <para>
        /// Reflection because the installer is <c>internal</c> in an assembly this one
        /// deliberately does not reference: naming it here would give the editor tools a
        /// hard dependency on the Steamworks package, which is the exact coupling
        /// <see cref="SteamBackendRegistry"/> exists to prevent.
        /// </para>
        /// </summary>
        private static void EnsureBackendRegistered(List<string> report)
        {
            if (SteamBackendRegistry.HasBackend)
            {
                return;
            }

#if HORRORGAME_STEAMWORKS
            const string TypeName =
                "HorrorGame.Steam.SteamworksBackend.SteamworksBackendInstaller, HorrorGame.Steam.SteamworksBackend";

            try
            {
                var installer = Type.GetType(TypeName);
                var install = installer?.GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static);

                if (install == null)
                {
                    report.Add("  NOTE: could not reach SteamworksBackendInstaller.Install — "
                        + "it was renamed or moved, so this probe is measuring the offline backend.");
                    return;
                }

                install.Invoke(null, null);
            }
            catch (Exception ex)
            {
                report.Add("  NOTE: waking the Steamworks backend threw — " + ex.Message);
            }
#else
            report.Add("  NOTE: HORRORGAME_STEAMWORKS is not defined, so there is no Steamworks "
                + "backend to measure. Install com.rlabrecque.steamworks.net first.");
#endif
        }

        private static IEnumerable<string> VerdictWorks()
        {
            yield return "  VERDICT: lobbies work on this App ID. SteamAppConfig's comment is correct,";
            yield return "  the lobby join flow can be built and tested as designed, and the $100 App ID";
            yield return "  is a release requirement rather than a testing one.";
        }

        private static IEnumerable<string> VerdictRefused()
        {
            yield return "  VERDICT: lobby creation is refused on this App ID.";
            yield return string.Empty;
            yield return "  SteamAppConfig says 480 allows 로비 테스트. This measurement says otherwise,";
            yield return "  which matches the FizzySteamworks report of k_EResultAccessDenied on 480.";
            yield return "  Fix the comment, and pick one:";
            yield return "    · join by pasted host SteamID64 — no lobby, transport already supports it";
            yield return "    · pay the $100 Steam Direct fee and put the real App ID in SteamAppConfig";
        }

        private static void Emit(List<string> report, bool batch, LogType level)
        {
            var text = string.Join("\n", report);

            // In batch mode the console is a log file nobody greps twice, so the whole
            // report goes out as one entry rather than as lines that interleave with
            // Unity's own noise.
            if (level == LogType.Warning && !batch)
            {
                Debug.LogWarning(text);
                return;
            }

            Debug.Log(text);
        }

        /// <summary>What one attempt came back with.</summary>
        private readonly struct Outcome
        {
            /// <summary>Whether a lobby actually existed at the end of it.</summary>
            public readonly bool Succeeded;

            /// <summary>One line, fit for the report.</summary>
            public readonly string Line;

            public Outcome(bool succeeded, string line)
            {
                Succeeded = succeeded;
                Line = line;
            }
        }
    }
}
