#nullable enable

using System.Collections;
using HorrorGame.Core;
using HorrorGame.UI.Shell;
using UnityEngine;

namespace HorrorGame.Gameplay.Race
{
    /// <summary>
    /// §14 step 2's missing half — the side that READS <c>-horror-host</c> and
    /// <c>-horror-client</c>.
    /// <para>
    /// <b>Why this file exists.</b> <c>LocalTwoInstance</c> has built a player and
    /// launched two processes with those arguments since §14 was written, and its own
    /// doc comment says "Reading those is the Net layer's job". Nothing ever did the
    /// job. So the one command that was supposed to make a two-player check take one
    /// line launched two identical single-player menus, and the two questions §14 says
    /// the whole design hangs on — 「추격이 재밌는가」, 「곁눈질 딜레마가 작동하는가」 —
    /// could not be asked at all. Twenty-player networking was written, tested against
    /// real sockets, and never once seen by a person.
    /// </para>
    /// <para>
    /// <b>It drives the buttons, it does not shortcut them.</b> Every step goes through
    /// the path a human takes: <see cref="GameShell.BeginFromMenu"/> asks §11's lobby,
    /// then <see cref="RaceLobby.RequestHost"/> or <see cref="RaceLobby.RequestJoin"/>,
    /// then <see cref="RaceLobby.RequestStart"/>. A harness that reached past the menu
    /// into <c>NetworkManager.StartHost</c> would prove the transport works and prove
    /// nothing about the game — which is the shape of every networking test this project
    /// had before this one.
    /// </para>
    /// <para>
    /// <b>The host waits and then starts by itself.</b> §11 refuses a field below
    /// <see cref="GameConstants.RaceRunnersMin"/>, so a host that pressed 출발 the moment
    /// it existed would always be refused. Polling until the second runner arrives is
    /// what turns two launched processes into two runners standing on B1's rim, which is
    /// the only state worth looking at.
    /// </para>
    /// </summary>
    public static class LocalTwoInstanceEntry
    {
        /// <summary>Launched as the host. Mirrors <c>LocalTwoInstance.HostArgument</c>.</summary>
        public const string HostArgument = "-horror-host";

        /// <summary>Launched as the client. Mirrors <c>LocalTwoInstance.ClientArgument</c>.</summary>
        public const string ClientArgument = "-horror-client";

        /// <summary>Optional address for the client. Defaults to loopback.</summary>
        public const string ConnectArgument = "-horror-connect";

        /// <summary>
        /// Where a client goes when nobody said otherwise. §14 step 2 is two instances on
        /// one PC, so loopback is the case, not a fallback.
        /// </summary>
        public const string LoopbackAddress = "127.0.0.1";

        /// <summary>
        /// Seconds the host waits for §11's minimum field before giving up on starting by
        /// itself. Long enough for the second process to build its first scene — the
        /// stagger in <c>LocalTwoInstance</c> is seconds, and a cold Mono player on a busy
        /// machine has been seen to take twenty — and short enough that a run with a
        /// broken client ends rather than hangs a terminal.
        /// </summary>
        public const float HostWaitSeconds = 60f;

        /// <summary>Seconds between polls of the roster. §11's own lobby polls no faster.</summary>
        public const float PollSeconds = 0.5f;

        private static bool _armed;

        /// <summary>
        /// Reads the command line once the bootstrap scene is up, and drives the lobby if
        /// this process was launched as one side of a two-instance test.
        /// <para>
        /// <c>AfterSceneLoad</c> for the same reason <c>RaceLobby.Install</c> uses it: the
        /// shell's objects have to exist before anything can press a button on them. The
        /// coroutine host is hidden and marked <c>DontDestroyOnLoad</c> because pressing
        /// 출발 loads the descent scene, and the wait for it has to survive that load.
        /// </para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Arm()
        {
            if (_armed)
            {
                return;
            }

            var host = HasArgument(HostArgument);
            var client = HasArgument(ClientArgument);

            if (!host && !client)
            {
                return;
            }

            if (host && client)
            {
                Debug.LogError(
                    "[TwoInstance] 이 프로세스는 " + HostArgument + " 와 " + ClientArgument
                    + " 를 둘 다 받았다. 한 프로세스는 한 쪽만 될 수 있다 — 아무것도 하지 않는다.");
                return;
            }

            _armed = true;

            var runner = new GameObject("TwoInstanceEntry")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            Object.DontDestroyOnLoad(runner);
            runner.AddComponent<Pump>().Begin(host ? Host() : Join());
        }

        /// <summary>Host: open §11's lobby, take authority, and start once the field is legal.</summary>
        private static IEnumerator Host()
        {
            yield return OpenTheLobby();

            var lobby = RaceLobby.Instance;
            if (lobby == null)
            {
                Debug.LogError(
                    "[TwoInstance] 호스트: 로비가 열리지 않았다. GameShell.BeginFromMenu 가 "
                    + "LobbyEntry.TryOpen 을 거치지 못했다는 뜻이다 — RaceLobby.Install 이 "
                    + "Intercept 를 걸었는지 보라.");
                yield break;
            }

            lobby.RequestHost();
            Debug.Log("[TwoInstance] 호스트: 호스트를 눌렀다. §11 최소 "
                      + GameConstants.RaceRunnersMin + "명을 기다린다.");

            var deadline = Time.realtimeSinceStartup + HostWaitSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                // The roster, not NetworkServer.connections: a socket that has connected
                // but not yet seated itself is not a runner, and §11's floor is counted
                // in runners. RequestStart refuses below the floor and says so in the
                // lobby's own note, so pressing early would be harmless — but it would
                // also make the log a wall of refusals with the real start buried in it.
                if (lobby.Runners.Count >= GameConstants.RaceRunnersMin)
                {
                    lobby.RequestStart();
                    Debug.Log(
                        "[TwoInstance] 호스트: " + lobby.Runners.Count + "명 — 출발을 눌렀다. 내려간다.");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(PollSeconds);
            }

            Debug.LogError(
                "[TwoInstance] 호스트: " + HostWaitSeconds.ToString("0") + "초 안에 §11의 최소 "
                + GameConstants.RaceRunnersMin + "명이 모이지 않았다. 두 번째 인스턴스가 "
                + "뜨지 않았거나, 떴는데 접속하지 못했다 — 클라이언트 쪽 Player.log 를 보라.");
        }

        /// <summary>Client: open §11's lobby and join the address it was given.</summary>
        private static IEnumerator Join()
        {
            yield return OpenTheLobby();

            var lobby = RaceLobby.Instance;
            if (lobby == null)
            {
                Debug.LogError("[TwoInstance] 참가: 로비가 열리지 않았다.");
                yield break;
            }

            var address = ArgumentValue(ConnectArgument) ?? LoopbackAddress;
            lobby.RequestJoin(address);
            Debug.Log("[TwoInstance] 참가: " + address + " 에 붙었다. 호스트가 출발을 누르면 따라 내려간다.");
        }

        /// <summary>
        /// Presses 시작 and waits for §11's lobby to appear.
        /// <para>
        /// A frame first, because <c>AfterSceneLoad</c> runs before the shell's own
        /// <c>Start</c> has built the menu it is about to be asked for.
        /// </para>
        /// </summary>
        private static IEnumerator OpenTheLobby()
        {
            yield return null;

            var deadline = Time.realtimeSinceStartup + HostWaitSeconds;
            while (GameShell.Instance == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var shell = GameShell.Instance;
            if (shell == null)
            {
                Debug.LogError(
                    "[TwoInstance] 부트 씬에 GameShell 이 없다. 이 빌드의 첫 씬이 Bootstrap 이 "
                    + "맞는지 보라 — 아니면 메뉴도 로비도 없다.");
                yield break;
            }

            shell.BeginFromMenu();

            while (RaceLobby.Instance == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static bool HasArgument(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? ArgumentValue(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, System.StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        /// <summary>A MonoBehaviour only because a coroutine needs one.</summary>
        private sealed class Pump : MonoBehaviour
        {
            /// <summary>Starts the drive.</summary>
            /// <param name="routine">Host or client.</param>
            public void Begin(IEnumerator routine)
            {
                StartCoroutine(routine);
            }
        }
    }
}
