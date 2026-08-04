#nullable enable

using System.Collections;
using System.Globalization;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Race;
using HorrorGame.Net;
using HorrorGame.UI.Shell;
using Mirror;
using UnityEngine;
using UnityEngine.Profiling;

namespace HorrorGame.Gameplay.Race
{
    /// <summary>
    /// §14 step 2's missing half — the side that READS <c>-horror-host</c> and
    /// <c>-horror-client</c> — and the instrument that makes a field of twenty readable.
    /// <para>
    /// <b>Why this file exists.</b> <c>LocalTwoInstance</c> has built a player and
    /// launched processes with those arguments since §14 was written, and its own
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
    /// <b>The host waits for the FIELD, not for §11's floor.</b> This is the change that
    /// turned a two-instance harness into a twenty-instance one, and skipping it makes a
    /// large run silently small. §11 refuses a field below
    /// <see cref="GameConstants.RaceRunnersMin"/>, so the old code polled until two
    /// runners existed and pressed 출발. On a twelve-process launch the second instance
    /// arrives about nine seconds in and the other ten arrive after the descent scene has
    /// already loaded — they are dropped, and the log still says "출발을 눌렀다" and the
    /// race still runs. It just runs with two people in it, which is precisely the number
    /// this whole exercise exists to get past. So the host is told the field size on its
    /// command line and waits for all of it, and a run that starts short says so at error
    /// level with the shortfall in it.
    /// </para>
    /// <para>
    /// <b>What it measures, and why measuring is this file's job.</b> Nothing else in the
    /// build can answer "what did the host cost with twenty runners on it" because
    /// nothing else knows a field is being run. Every instance reports its own frame-time
    /// distribution, its own Mirror byte counts, and — the one that matters — its own copy
    /// of §02's standings keyed by the host's frame number. Twenty machines logging
    /// <c>rows=</c> for frame 57 is a claim that can be checked by sorting: either every
    /// instance wrote the same string for that frame or §13's authority does not hold, and
    /// no amount of reading the code settles it either way.
    /// </para>
    /// </summary>
    public static class LocalTwoInstanceEntry
    {
        /// <summary>Launched as the host. Mirrors <c>LocalTwoInstance.HostArgument</c>.</summary>
        public const string HostArgument = "-horror-host";

        /// <summary>Launched as a client. Mirrors <c>LocalTwoInstance.ClientArgument</c>.</summary>
        public const string ClientArgument = "-horror-client";

        /// <summary>Optional address for the client. Defaults to loopback.</summary>
        public const string ConnectArgument = "-horror-connect";

        /// <summary>Which process this is: 0 is the host. Every line this file writes carries it.</summary>
        public const string InstanceArgument = "-horror-instance";

        /// <summary>How many runners the host waits for. Mirrors <c>LocalTwoInstance.FieldArgument</c>.</summary>
        public const string FieldArgument = "-horror-field";

        /// <summary>Seconds before this instance quits by itself. Absent means never.</summary>
        public const string SecondsArgument = "-horror-seconds";

        /// <summary>
        /// Where a client goes when nobody said otherwise. §14 step 2 is instances on
        /// one PC, so loopback is the case, not a fallback.
        /// </summary>
        public const string LoopbackAddress = "127.0.0.1";

        /// <summary>
        /// Seconds the host allows for the field to gather, before the per-runner term.
        /// <para>
        /// A cold Mono player on a busy machine has been seen to take twenty seconds to
        /// reach its first scene, and the machine is at its busiest exactly while the field
        /// is booting. This is the fixed part of the budget: the host's own start-up plus
        /// one slow straggler.
        /// </para>
        /// </summary>
        public const float HostWaitBaseSeconds = 45f;

        /// <summary>
        /// Seconds of extra patience per runner in the field.
        /// <para>
        /// <c>LocalTwoInstance.ClientStaggerMilliseconds</c> is 1.5 s, so the launcher alone
        /// spends 1.5 s per client before the instance has run a line of code; the rest of
        /// this budget is that instance's boot competing with everybody else's. Four times
        /// the stagger, which at twenty runners is 165 s of total patience — long enough
        /// that a straggler is waited for, short enough that a run with a broken client
        /// ends rather than hangs a terminal overnight.
        /// </para>
        /// </summary>
        public const float HostWaitPerRunnerSeconds = 6f;

        /// <summary>Seconds between polls of the roster. §11's own lobby polls no faster.</summary>
        public const float PollSeconds = 0.5f;

        /// <summary>
        /// Seconds a client gives one press of 참가 before pressing it again.
        /// <para>
        /// <b>Measured, and it cost a run.</b> The first twenty-instance field on this
        /// machine started with eighteen: instances 1 and 2 — the two launched
        /// <em>earliest</em>, 2.0 s and 3.5 s behind the host — dialled 127.0.0.1 before the
        /// host's KCP socket existed, and their logs end with 「참가: 127.0.0.1 에 붙었다」
        /// and then 0.0 kB/s in and out for the remaining 420 s. Both sat in the menu while
        /// the host counted 18/20 and started short. Nothing was wrong with either process;
        /// <see cref="RaceLobby.RequestJoin"/> is a button press, KCP is UDP so a dial at
        /// nobody takes ten seconds to fail, and there is nobody to press it again.
        /// </para>
        /// <para>
        /// Twelve seconds because KCP's own connect timeout is ten: shorter, and the retry
        /// fires while Mirror still has the previous attempt open, which
        /// <c>RequestJoin</c> refuses (<c>NetworkClient.active</c>) and the log fills with
        /// presses that did nothing.
        /// </para>
        /// </summary>
        public const float JoinAttemptSeconds = 12f;

        /// <summary>
        /// Seconds between measurement lines.
        /// <para>
        /// Ten, because the thing being measured is a distribution and a window has to hold
        /// enough frames to have one — at 60 Hz that is 600 samples, which makes a p95
        /// mean something. Faster would turn twenty logs into a wall of noise on the same
        /// disk the game is streaming from, which is itself a load this test is not trying
        /// to apply.
        /// </para>
        /// </summary>
        public const float ReportSeconds = 10f;

        /// <summary>
        /// Milliseconds in the top frame-time bucket. Anything slower is counted in the
        /// overflow and shows up in <c>max</c>, which is the number that matters when a
        /// frame goes badly.
        /// </summary>
        private const int FrameBuckets = 256;

        private static bool _armed;

        /// <summary>Which process this is. −1 until <see cref="Arm"/> reads the command line.</summary>
        private static int _instance = -1;

        /// <summary>How many runners this run is meant to have. §11's minimum until told otherwise.</summary>
        private static int _field = GameConstants.RaceRunnersMin;

        /// <summary>
        /// Reads the command line once the bootstrap scene is up, and drives the lobby if
        /// this process was launched as one instance of a local field.
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
                    "[Field] 이 프로세스는 " + HostArgument + " 와 " + ClientArgument
                    + " 를 둘 다 받았다. 한 프로세스는 한 쪽만 될 수 있다 — 아무것도 하지 않는다.");
                return;
            }

            _armed = true;
            _instance = ArgumentInt(InstanceArgument, host ? 0 : 1);
            _field = Mathf.Clamp(
                ArgumentInt(FieldArgument, GameConstants.RaceRunnersMin),
                GameConstants.RaceRunnersMin,
                GameConstants.RaceRunnersMax);

            var runner = new GameObject("LocalFieldEntry")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            Object.DontDestroyOnLoad(runner);

            var pump = runner.AddComponent<Pump>();
            pump.Begin(host ? Host() : Join());
            pump.BeginWatching(ArgumentFloat(SecondsArgument, 0f));

            Debug.Log(Tag() + "인스턴스 " + _instance + "/" + (_field - 1) + " · "
                + (host ? "호스트" : "클라이언트") + " · 필드 " + _field + "명 · "
                + Describe());
        }

        /// <summary>Host: open §11's lobby, take authority, and start once the whole field is in.</summary>
        private static IEnumerator Host()
        {
            yield return OpenTheLobby();

            var lobby = RaceLobby.Instance;
            if (lobby == null)
            {
                Debug.LogError(
                    Tag() + "호스트: 로비가 열리지 않았다. GameShell.BeginFromMenu 가 "
                    + "LobbyEntry.TryOpen 을 거치지 못했다는 뜻이다 — RaceLobby.Install 이 "
                    + "Intercept 를 걸었는지 보라.");
                yield break;
            }

            lobby.RequestHost();

            var patience = HostWaitBaseSeconds + (HostWaitPerRunnerSeconds * _field);
            Debug.Log(Tag() + "호스트: 호스트를 눌렀다. " + _field + "명이 다 모일 때까지 "
                      + patience.ToString("0") + "초 기다린다 (§11 최소 "
                      + GameConstants.RaceRunnersMin + "명).");

            var deadline = Time.realtimeSinceStartup + patience;
            var announced = 0;

            while (Time.realtimeSinceStartup < deadline)
            {
                // The roster, not NetworkServer.connections: a socket that has connected
                // but not yet seated itself is not a runner, and §11's floor is counted
                // in runners. RequestStart refuses below the floor and says so in the
                // lobby's own note, so pressing early would be harmless — but it would
                // also make the log a wall of refusals with the real start buried in it.
                var seated = lobby.Runners.Count;

                if (seated != announced)
                {
                    announced = seated;
                    Debug.Log(Tag() + "로비 " + seated + "/" + _field + "명 · "
                              + Time.realtimeSinceStartup.ToString("0") + "초");
                }

                if (seated >= _field)
                {
                    lobby.RequestStart();
                    Debug.Log(Tag() + "호스트: " + seated + "명 전원 — 출발을 눌렀다. 내려간다.");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(PollSeconds);
            }

            var arrived = lobby.Runners.Count;

            if (arrived >= GameConstants.RaceRunnersMin)
            {
                // Error level and not a warning, on purpose. A short field still races, and
                // the log still fills with a successful-looking descent — this is the one
                // line that says the twelve-instance run you are about to read the numbers
                // off was a nine-instance run. Reporting a field size that was never on the
                // rim is exactly the failure this project keeps having.
                Debug.LogError(Tag() + "짧은 필드 — " + _field + "명을 기다렸는데 "
                    + arrived + "명만 모였다 (" + (_field - arrived) + "명 실종). "
                    + "그래도 출발한다. 이 판의 모든 수치는 " + arrived + "명짜리다.");
                lobby.RequestStart();
                yield break;
            }

            Debug.LogError(
                Tag() + "호스트: " + patience.ToString("0") + "초 안에 §11의 최소 "
                + GameConstants.RaceRunnersMin + "명이 모이지 않았다 (" + arrived + "명). "
                + "다른 인스턴스가 뜨지 않았거나, 떴는데 접속하지 못했다 — 클라이언트 로그를 보라.");
        }

        /// <summary>
        /// Client: open §11's lobby, press 참가, and keep pressing it until the socket is
        /// actually up.
        /// <para>
        /// <b>Pressing it once is not joining.</b> The old version of this method called
        /// <see cref="RaceLobby.RequestJoin"/>, logged 「붙었다」 and returned — and 붙었다
        /// was a lie, because <c>RequestJoin</c> only asks Mirror to start dialling. The
        /// first full-field run lost two runners to exactly that: see
        /// <see cref="JoinAttemptSeconds"/>. A person watching a menu that says 「연결하는
        /// 중…」 for twelve seconds presses the button again, so that is what this does,
        /// and it says 「붙었다」 only once <c>NetworkClient.isConnected</c> agrees.
        /// </para>
        /// <para>
        /// The patience is the host's, deliberately: a client that is still dialling when
        /// the host has given up on the field is no use to anybody, and one that gives up
        /// first would be a straggler the host is still waiting for.
        /// </para>
        /// </summary>
        private static IEnumerator Join()
        {
            yield return OpenTheLobby();

            var lobby = RaceLobby.Instance;
            if (lobby == null)
            {
                Debug.LogError(Tag() + "참가: 로비가 열리지 않았다.");
                yield break;
            }

            var address = ArgumentValue(ConnectArgument) ?? LoopbackAddress;
            var deadline = Time.realtimeSinceStartup + HostWaitBaseSeconds + (HostWaitPerRunnerSeconds * _field);
            var attempt = 0;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (NetworkClient.isConnected)
                {
                    Debug.Log(Tag() + "참가: " + address + " 에 붙었다 (" + attempt + "번째 시도, "
                              + Time.realtimeSinceStartup.ToString("0") + "초). 호스트가 출발을 누르면 따라 내려간다.");
                    yield break;
                }

                // RequestJoin refuses while a previous attempt is still open, so a press
                // that lands mid-dial costs nothing but is worth neither logging nor
                // counting — the count is of dials that actually left.
                if (!NetworkClient.active)
                {
                    attempt++;
                    lobby.RequestJoin(address);
                    Debug.Log(Tag() + "참가 " + attempt + "번째 — " + address + " 에 거는 중 ("
                              + Time.realtimeSinceStartup.ToString("0") + "초).");

                    var wait = Time.realtimeSinceStartup + JoinAttemptSeconds;
                    while (Time.realtimeSinceStartup < wait && !NetworkClient.isConnected)
                    {
                        yield return new WaitForSecondsRealtime(PollSeconds);
                    }

                    continue;
                }

                yield return new WaitForSecondsRealtime(PollSeconds);
            }

            if (NetworkClient.isConnected)
            {
                yield break;
            }

            // Error level: this instance is about to sit in a menu looking exactly like a
            // process that is fine, while the host reports a field two short and every
            // number in the run is quietly of a smaller race.
            Debug.LogError(Tag() + "참가 실패 — " + address + " 에 " + attempt + "번 걸었는데 "
                + "한 번도 붙지 못했다. 이 인스턴스는 이 판에 없다. 호스트가 듣고 있는지, "
                + "호스트 프로세스가 아직 부팅 중이었는지 보라.");
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

            var deadline = Time.realtimeSinceStartup + HostWaitBaseSeconds;
            while (GameShell.Instance == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var shell = GameShell.Instance;
            if (shell == null)
            {
                Debug.LogError(
                    Tag() + "부트 씬에 GameShell 이 없다. 이 빌드의 첫 씬이 Bootstrap 이 "
                    + "맞는지 보라 — 아니면 메뉴도 로비도 없다.");
                yield break;
            }

            shell.BeginFromMenu();

            while (RaceLobby.Instance == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        /// <summary>
        /// What this process is running on, said once so a reading of the numbers below
        /// cannot be detached from the conditions that produced them.
        /// <para>
        /// The vSync and target-frame-rate values are here because without them a frame
        /// time is unreadable: 16.6 ms under a 60 Hz cap means the host had headroom to
        /// spare, and the same 16.6 ms uncapped means it was flat out.
        /// </para>
        /// </summary>
        private static string Describe()
        {
            var graphics = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null
                ? "렌더러 없음"
                : SystemInfo.graphicsDeviceType + " " + Screen.width + "×" + Screen.height;

            return graphics
                + " · vSync " + QualitySettings.vSyncCount
                + " · targetFrameRate " + Application.targetFrameRate
                // Read rather than assumed: a windowed host that stops simulating when it
                // loses focus stops the race for all twenty, and the symptom on the other
                // nineteen is nineteen frozen bodies with nothing in their own logs.
                + " · runInBackground " + (Application.runInBackground ? 1 : 0)
                + " · " + SystemInfo.processorCount + " core";
        }

        /// <summary>Prefix every line with the instance, so twenty logs can be concatenated and still read.</summary>
        private static string Tag()
        {
            return "[Field] i=" + _instance.ToString("00", CultureInfo.InvariantCulture) + " ";
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

        private static int ArgumentInt(string name, int fallback)
        {
            var raw = ArgumentValue(name);
            return raw != null
                   && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private static float ArgumentFloat(string name, float fallback)
        {
            var raw = ArgumentValue(name);
            return raw != null
                   && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        /// <summary>
        /// A MonoBehaviour that pumps the lobby coroutine and, once a second and once every
        /// <see cref="ReportSeconds"/>, writes down what this instance is costing and what
        /// it believes the standings are.
        /// </summary>
        private sealed class Pump : MonoBehaviour
        {
            /// <summary>Frame times in whole milliseconds, this window. Index 255 is "255 or worse".</summary>
            private readonly int[] _frames = new int[FrameBuckets];

            private readonly StringBuilder _rows = new StringBuilder(160);

            private int _frameCount;
            private float _frameSumMs;
            private float _frameWorstMs;
            private float _frameWorstEverMs;

            private long _outBytes;
            private long _inBytes;
            private int _outMessages;
            private int _inMessages;
            private long _outBytesTotal;
            private long _inBytesTotal;

            private float _nextReport;

            /// <summary>Wall clock at the start of the current window — see <see cref="ReportCost"/>.</summary>
            private float _windowStart;

            private float _quitAt;
            private uint _lastFrameLogged;
            private bool _subscribed;

            /// <summary>Starts the drive.</summary>
            /// <param name="routine">Host or client.</param>
            public void Begin(IEnumerator routine)
            {
                StartCoroutine(routine);
            }

            /// <summary>Starts measuring, and arms the auto-quit if one was asked for.</summary>
            /// <param name="secondsBeforeQuit">Seconds from now, or 0 to run until killed.</param>
            public void BeginWatching(float secondsBeforeQuit)
            {
                _windowStart = Time.realtimeSinceStartup;
                _nextReport = Time.realtimeSinceStartup + ReportSeconds;
                _quitAt = secondsBeforeQuit > 0f ? Time.realtimeSinceStartup + secondsBeforeQuit : 0f;

                if (_quitAt > 0f)
                {
                    Debug.Log(Tag() + secondsBeforeQuit.ToString("0") + "초 뒤에 스스로 종료한다.");
                }
            }

            private void Update()
            {
                Subscribe();
                Sample(Time.unscaledDeltaTime);

                var now = Time.realtimeSinceStartup;
                if (now >= _nextReport)
                {
                    _nextReport = now + ReportSeconds;
                    ReportCost(now);
                }

                ReportStandings();

                if (_quitAt > 0f && now >= _quitAt)
                {
                    _quitAt = 0f;
                    ReportCost(now);
                    Debug.Log(Tag() + "예정된 시간이 됐다 — 종료. 최악 프레임 "
                        + _frameWorstEverMs.ToString("0.0") + " ms, 보낸 바이트 " + _outBytesTotal
                        + ", 받은 바이트 " + _inBytesTotal + ".");
                    Application.Quit();
                }
            }

            /// <summary>
            /// Hooks Mirror's byte counters, from <c>Update</c> rather than from
            /// <c>Arm</c>.
            /// <para>
            /// <c>NetworkDiagnostics</c> clears both events from its own
            /// <c>[RuntimeInitializeOnLoadMethod]</c>, which is the same load phase
            /// <see cref="Arm"/> runs in and therefore has no defined order against it. A
            /// subscription made there is dropped roughly half the time and the run reports
            /// 0 bytes on the wire while the race plainly works — a zero that means "the
            /// handler was unhooked", read as "the game sends nothing". The first
            /// <c>Update</c> is unambiguously after every initialise-on-load method in the
            /// process.
            /// </para>
            /// </summary>
            private void Subscribe()
            {
                if (_subscribed)
                {
                    return;
                }

                _subscribed = true;
                NetworkDiagnostics.OutMessageEvent += OnOut;
                NetworkDiagnostics.InMessageEvent += OnIn;
            }

            private void OnDestroy()
            {
                if (!_subscribed)
                {
                    return;
                }

                NetworkDiagnostics.OutMessageEvent -= OnOut;
                NetworkDiagnostics.InMessageEvent -= OnIn;
            }

            /// <summary>
            /// One outgoing message. <c>bytes × count</c> and not <c>bytes</c>: Mirror
            /// raises this once for a message that went to every connection, and on a host
            /// with nineteen clients the difference between the two readings is a factor of
            /// nineteen.
            /// </summary>
            private void OnOut(NetworkDiagnostics.MessageInfo info)
            {
                var total = (long)info.bytes * info.count;
                _outBytes += total;
                _outBytesTotal += total;
                _outMessages += info.count;
            }

            private void OnIn(NetworkDiagnostics.MessageInfo info)
            {
                _inBytes += info.bytes;
                _inBytesTotal += info.bytes;
                _inMessages++;
            }

            private void Sample(float deltaSeconds)
            {
                var ms = deltaSeconds * 1000f;
                _frameCount++;
                _frameSumMs += ms;

                if (ms > _frameWorstMs)
                {
                    _frameWorstMs = ms;
                }

                if (ms > _frameWorstEverMs)
                {
                    _frameWorstEverMs = ms;
                }

                var bucket = Mathf.Clamp(Mathf.FloorToInt(ms), 0, FrameBuckets - 1);
                _frames[bucket]++;
            }

            /// <summary>
            /// Writes one window's frame time and bandwidth, then starts the next window.
            /// <para>
            /// The percentiles come out of the millisecond histogram rather than a sorted
            /// list of samples, so a ten-second window at 60 Hz costs one array of 256 ints
            /// instead of 600 floats and a sort. The resolution that buys is 1 ms, which is
            /// the resolution anybody reads a frame time at.
            /// </para>
            /// <para>
            /// <b><c>wall</c> and <c>acct</c> exist because the frame times lied once and
            /// nobody could prove it.</b> Three windows of the first full-field run read
            /// <c>frames=549 mean=8.33ms p50=8 p95=8 p99=8 max=8.3</c> — a flawless 120 Hz —
            /// while the window they were measured over was ten seconds long, which 549
            /// frames of 8.33 ms cannot fill. Somewhere in that window 5.4 s went by with
            /// no frame accounting for it, and a distribution built only from
            /// <c>unscaledDeltaTime</c> cannot show that: every frame Unity told us about
            /// was fast. <c>wall</c> is the real length of the window and <c>acct</c> is the
            /// share of it the frames add up to, so a host that stalls reads as
            /// <c>acct=46%</c> instead of as the best frame time in the run.
            /// </para>
            /// </summary>
            /// <param name="now">Realtime, so the window length is honest even when the game is paused.</param>
            private void ReportCost(float now)
            {
                if (_frameCount <= 0)
                {
                    return;
                }

                var mean = _frameSumMs / _frameCount;
                var p50 = Percentile(0.50f);
                var p95 = Percentile(0.95f);
                var p99 = Percentile(0.99f);
                var wall = Mathf.Max(0.001f, now - _windowStart);
                var accounted = _frameSumMs / 1000f / wall * 100f;

                Debug.Log(Tag() + "COST t=" + now.ToString("0") + " frames=" + _frameCount
                    + " mean=" + mean.ToString("0.00") + "ms p50=" + p50 + "ms p95=" + p95
                    + "ms p99=" + p99 + "ms max=" + _frameWorstMs.ToString("0.0")
                    + "ms worst-ever=" + _frameWorstEverMs.ToString("0.0") + "ms"
                    + " wall=" + wall.ToString("0.0") + "s acct=" + accounted.ToString("0") + "%"
                    + " fps=" + (_frameCount / wall).ToString("0")
                    + " mem=" + (Profiler.GetTotalReservedMemoryLong() / (1024L * 1024L)) + "MB");

                var window = ReportSeconds;
                Debug.Log(Tag() + "NET t=" + now.ToString("0")
                    + " out=" + (_outBytes / window / 1024f).ToString("0.0") + "kB/s"
                    + " in=" + (_inBytes / window / 1024f).ToString("0.0") + "kB/s"
                    + " outMsg=" + (_outMessages / window).ToString("0") + "/s"
                    + " inMsg=" + (_inMessages / window).ToString("0") + "/s"
                    + " totalOut=" + (_outBytesTotal / 1024f / 1024f).ToString("0.00") + "MB"
                    + " totalIn=" + (_inBytesTotal / 1024f / 1024f).ToString("0.00") + "MB"
                    + " conns=" + (NetworkServer.active ? NetworkServer.connections.Count : 0));

                System.Array.Clear(_frames, 0, _frames.Length);
                _windowStart = now;
                _frameCount = 0;
                _frameSumMs = 0f;
                _frameWorstMs = 0f;
                _outBytes = 0L;
                _inBytes = 0L;
                _outMessages = 0;
                _inMessages = 0;
            }

            private int Percentile(float fraction)
            {
                var target = Mathf.CeilToInt(_frameCount * fraction);
                var seen = 0;

                for (var bucket = 0; bucket < _frames.Length; bucket++)
                {
                    seen += _frames[bucket];
                    if (seen >= target)
                    {
                        return bucket;
                    }
                }

                return _frames.Length - 1;
            }

            /// <summary>
            /// Writes this instance's copy of §02's standings, once per host frame number.
            /// <para>
            /// <b>Keyed by the host's frame, which is what makes it checkable.</b> Every
            /// instance samples at a different moment, so two logs taken at the same wall
            /// clock have no reason to match and a disagreement would prove nothing. A
            /// <see cref="RaceStandingsMessage"/> frame number names one table that the host
            /// sent to everybody, so twenty instances writing <c>rows=</c> for frame 57 is a
            /// claim with exactly one right answer: <c>sort -u</c> over the frame's lines
            /// returns one line, or §13's authority is not holding.
            /// </para>
            /// <para>
            /// <b>The host logs the wire copy too.</b> Mirror's host mode delivers a
            /// broadcast to its own local connection, so the host has a <c>NetRace.Standings</c>
            /// like everybody else, and using it here keeps the compared string one code
            /// path on all N machines. The rule's own table is written on a separate
            /// <c>rule</c> line, which is how a host that agrees with the wire and a host
            /// that quietly does not are told apart.
            /// </para>
            /// </summary>
            private void ReportStandings()
            {
                var wire = NetRace.Standings;
                if (!wire.HasFrame || wire.Frame == _lastFrameLogged)
                {
                    return;
                }

                _lastFrameLogged = wire.Frame;

                Debug.Log(Tag() + "STANDINGS src=wire seat=" + RaceParty.LocalSeat
                    + " frame=" + wire.Frame
                    + " n=" + wire.RunnerCount
                    + " t=" + wire.ElapsedSeconds.ToString("0.0")
                    + " over=" + (wire.Over ? 1 : 0)
                    + " win=" + wire.WinnerId
                    + " rows=" + Digest(wire.RunnerCount, wire.RowOf));

                var rule = NetRace.Authority;
                if (rule == null || !rule.Started || !NetworkServer.active)
                {
                    return;
                }

                Debug.Log(Tag() + "STANDINGS src=rule seat=" + RaceParty.LocalSeat
                    + " frame=" + wire.Frame
                    + " n=" + rule.RunnerCount
                    + " t=" + rule.ElapsedSeconds.ToString("0.0")
                    + " over=" + (rule.Over ? 1 : 0)
                    + " win=" + rule.WinnerId
                    + " rows=" + Digest(rule.RunnerCount, rule.RowOf));
            }

            /// <summary>
            /// The whole table as one string, in seat order: status letter, storey, times
            /// caught, place.
            /// <para>
            /// Seat order and not standings order, because standings order is a view of the
            /// table and two machines agreeing about a view while disagreeing about the
            /// table is the bug worth catching. §06's 잡힘 count is in it for the same
            /// reason: a runner sent back to B1 and a runner who never left it are the same
            /// row without it.
            /// </para>
            /// </summary>
            /// <param name="count">Seats in the field.</param>
            /// <param name="rowOf">Where to get a seat's row.</param>
            private string Digest(int count, System.Func<int, Racer> rowOf)
            {
                _rows.Clear();

                for (var seat = 0; seat < count; seat++)
                {
                    if (seat > 0)
                    {
                        _rows.Append('|');
                    }

                    var row = rowOf(seat);
                    _rows.Append(Letter(row.Status));
                    _rows.Append(row.Storey);
                    _rows.Append('c');
                    _rows.Append(row.TimesCaught);
                    _rows.Append('p');
                    _rows.Append(row.Place);
                }

                return _rows.ToString();
            }

            private static char Letter(RacerStatus status)
            {
                switch (status)
                {
                    case RacerStatus.Finished:
                        return 'F';
                    case RacerStatus.Eliminated:
                        return 'X';
                    default:
                        return 'R';
                }
            }
        }
    }
}
