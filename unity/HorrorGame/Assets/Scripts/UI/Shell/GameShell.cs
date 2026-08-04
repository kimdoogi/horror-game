#nullable enable

using System.Collections;
using HorrorGame.UI.Screens;
using HorrorGame.UI.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HorrorGame.UI.Shell
{
    /// <summary>
    /// Everything around the match: the menu, the settings, the load, the pause, and the
    /// way back out.
    /// <para>
    /// <b>One object owns the flow, and it survives the scene load.</b> The menu is in
    /// <c>Bootstrap</c> and the match is in another scene; a flow spread across the two
    /// would have to hand its state over at exactly the moment the objects carrying it
    /// are being destroyed. So this is a single <c>DontDestroyOnLoad</c> component that
    /// builds all four screens in code — the pattern the rest of this layer already uses
    /// (<see cref="UiFactory"/>), and for the same reason: there are no UI prefabs here,
    /// and a missing prefab reference is a menu that fails silently.
    /// </para>
    /// <para>
    /// <b>What it deliberately does not do.</b> It does not host, join or step a match,
    /// and it still compiles without Mirror and without the Steam layer. §13 gives the
    /// host authority and <c>MatchDirector</c> starts itself when its scene comes up;
    /// this class chooses which scene that is and gets out of the way.
    /// </para>
    /// <para>
    /// <b>But it does ask whether anyone wants the flow first.</b> 시작 goes through
    /// <see cref="LobbyEntry"/> — a one-way seam in this same assembly that the gameplay
    /// layer installs itself into — so that a build with §11's lobby in it stops at the
    /// lobby and a build without one descends alone, from the same button and with no
    /// reference to Mirror on this side of the seam. The seam has to be one-way and it
    /// has to be here: <c>RaceLobby</c>, the only caller of <c>StartHost</c> /
    /// <c>StartClient</c> in the project, lives in <c>Assembly-CSharp</c> (there is no
    /// asmdef under <c>Scripts/Gameplay/</c>), and no assembly definition can reference
    /// the default assembly. Adding Mirror to <c>HorrorGame.UI.asmdef</c> would not help;
    /// only the gameplay layer reaching back can, which is what
    /// <c>RaceLobby.Install</c>'s <c>RuntimeInitializeOnLoadMethod</c> does.
    /// </para>
    /// <para>
    /// <b>The menu also carries <see cref="StartMatch"/> directly, and it has to.</b>
    /// Once <c>RaceLobby</c> is installed — which it always is in a real build, from a
    /// <c>RuntimeInitializeOnLoadMethod</c> that no scene has to opt into — 시작 can only
    /// ever end at the lobby, and the lobby refuses to start below
    /// <c>GameConstants.RaceRunnersMin</c>. A player on their own therefore had no button
    /// on this menu that reached the building at all. 혼자 내려가기 is that button. It is
    /// deliberately not a second way into a race: it loads the same scene with one runner
    /// in it, which is a practice descent and is labelled as one.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    [AddComponentMenu("HorrorGame/UI/Game Shell")]
    public sealed class GameShell : MonoBehaviour
    {
        /// <summary>The playable scene, as assembled by the editor's solo playtest tool.</summary>
        public const string DefaultMatchScene = "Map_FirstSketch_Solo";

        /// <summary>The scene holding the menu. Scene 0 of the build.</summary>
        public const string DefaultMenuScene = "Bootstrap";

        /// <summary>
        /// Shortest time the loading screen stays up, seconds.
        /// <para>
        /// Not a tuned game value. It is a perception floor: a screen that appears and
        /// vanishes inside two frames reads as a flicker or a glitch, and on a machine
        /// with the scene already in the page cache that is exactly what happens.
        /// </para>
        /// </summary>
        private const float MinimumLoadingSeconds = 1.1f;

        [SerializeField]
        [Tooltip("Scene loaded by 시작. Left empty, Map_FirstSketch_Solo is used.")]
        private string _matchScene = DefaultMatchScene;

        [SerializeField]
        [Tooltip("Scene holding this menu, returned to by 메뉴로 나가기.")]
        private string _menuScene = DefaultMenuScene;

        private MainMenuScreen? _menu;
        private SettingsScreen? _settings;
        private PauseScreen? _pause;
        private LoadingScreen? _loading;

        private ShellState _state = ShellState.Menu;
        private bool _settingsCameFromPause;

        /// <summary>The single shell, or null before the bootstrap scene has come up.</summary>
        public static GameShell? Instance { get; private set; }

        /// <summary>Where the flow currently is. Read by tests and by the pause key.</summary>
        public ShellState State
        {
            get { return _state; }
        }

        /// <summary>Which screen the shell is showing.</summary>
        public enum ShellState
        {
            /// <summary>Main menu over the drifting corridor.</summary>
            Menu = 0,

            /// <summary>Settings, opened from the menu or from the pause screen.</summary>
            Settings = 1,

            /// <summary>The match scene is loading.</summary>
            Loading = 2,

            /// <summary>A match is being played.</summary>
            Match = 3,

            /// <summary>A match is stopped and the pause menu is up.</summary>
            Paused = 4,
        }

        /// <summary>Builds the four screens and shows the menu. Called by the scene, or by a test.</summary>
        public void ShowMenu()
        {
            EnsureScreens();

            MatchPause.Clear();
            _state = ShellState.Menu;

            _pause?.Hide();
            _settings?.SetVisible(false);
            _loading?.Close();

            _menu?.Open(BeginFromMenu, StartMatch, OpenSettingsFromMenu, Quit);
        }

        /// <summary>
        /// 시작, as the menu means it: ask §11's lobby first, descend alone only if there
        /// is nothing listening.
        /// <para>
        /// Separate from <see cref="StartMatch"/> on purpose, and the separation is the
        /// whole design. <c>StartMatch</c> keeps meaning exactly one thing — load the
        /// match scene now — because three callers depend on that meaning: the editor's
        /// solo playtest, <c>UiFlowTests</c>, and the lobby itself once the host has said
        /// go. This method is the only one that asks a question, and it is the only one
        /// the button is wired to.
        /// </para>
        /// <para>
        /// It passes <em>itself</em> as the lobby's "now descend" callback rather than
        /// <see cref="StartMatch"/>, which looks like a loop and is the opposite. The
        /// lobby calls <c>LobbyEntry.PassNextThrough()</c> immediately before invoking
        /// the callback, so the second visit falls through to the scene load and the
        /// latch is consumed on the way past. Handing it <c>StartMatch</c> instead would
        /// leave that latch armed, and the next 시작 after a race would silently skip
        /// the lobby and start a solo run.
        /// </para>
        /// </summary>
        public void BeginFromMenu()
        {
            if (_state == ShellState.Loading)
            {
                return;
            }

            EnsureScreens();

            // Gone either way: the lobby draws over the whole screen and so does the
            // loading screen. 뒤로 in the lobby calls ShowMenu(), which rebuilds it.
            _menu?.Close();

            if (LobbyEntry.TryOpen(BeginFromMenu))
            {
                return;
            }

            StartMatch();
        }

        /// <summary>§14 step 1 — build a match and go down. No lobby, no question asked.</summary>
        public void StartMatch()
        {
            if (_state == ShellState.Loading)
            {
                return;
            }

            StartCoroutine(LoadMatchRoutine());
        }

        /// <summary>Stops the match and puts the pause menu up.</summary>
        public void PauseMatch()
        {
            if (_state != ShellState.Match)
            {
                return;
            }

            EnsureScreens();
            _state = ShellState.Paused;
            _pause?.Open(OnResumed, OpenSettingsFromPause, QuitToMenu);
        }

        /// <summary>Abandons the match and goes back to the menu scene.</summary>
        public void QuitToMenu()
        {
            StartCoroutine(QuitToMenuRoutine());
        }

        /// <summary>Leaves the game. In the editor this stops play mode instead.</summary>
        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A second Bootstrap load — 메뉴로 나가기 — brings another of these with
                // it. The first one owns the screens and the flow state.
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            SettingsService.Initialize();
            EnsureEventSystem();
            EnsureScreens();
        }

        private void Start()
        {
            if (_state == ShellState.Menu)
            {
                ShowMenu();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!EscapePressed())
            {
                return;
            }

            switch (_state)
            {
                case ShellState.Match:
                    PauseMatch();
                    break;

                case ShellState.Paused:
                    _pause?.Resume();
                    break;

                case ShellState.Settings:
                    _settings?.Close();
                    break;
            }
        }

        private static bool EscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private void EnsureScreens()
        {
            if (_menu == null)
            {
                _menu = Attach<MainMenuScreen>("MainMenu");
            }

            if (_settings == null)
            {
                _settings = Attach<SettingsScreen>("Settings");
            }

            if (_pause == null)
            {
                _pause = Attach<PauseScreen>("Pause");
            }

            if (_loading == null)
            {
                _loading = Attach<LoadingScreen>("Loading");
            }
        }

        private T Attach<T>(string name) where T : UiScreen
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, worldPositionStays: false);
            return child.AddComponent<T>();
        }

        /// <summary>
        /// The module that turns a mouse into a click.
        /// <para>
        /// <c>UiFactory.EnsureEventSystem</c> deliberately does nothing under the new
        /// input backend — which module reads the mouse depends on how the project is
        /// configured, and the UI assembly used not to know. It does now, because
        /// rebinding needs the Input System anyway, so the shell supplies the module for
        /// the menu exactly as <c>MatchHud</c> does for the match.
        /// </para>
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                return;
            }

            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            DontDestroyOnLoad(go);
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        private void OpenSettingsFromMenu()
        {
            EnsureScreens();
            _settingsCameFromPause = false;
            _state = ShellState.Settings;
            _menu?.Close();
            _settings?.Open(CloseSettings);
        }

        private void OpenSettingsFromPause()
        {
            EnsureScreens();
            _settingsCameFromPause = true;
            _state = ShellState.Settings;
            _pause?.Hide();
            _settings?.Open(CloseSettings);
        }

        private void CloseSettings()
        {
            if (_settingsCameFromPause)
            {
                _state = ShellState.Paused;
                _pause?.Show();
                return;
            }

            _state = ShellState.Menu;
            _menu?.Open(BeginFromMenu, StartMatch, OpenSettingsFromMenu, Quit);
        }

        private void OnResumed()
        {
            _state = ShellState.Match;
        }

        private IEnumerator LoadMatchRoutine()
        {
            EnsureScreens();

            _state = ShellState.Loading;
            _menu?.Close();
            _settings?.SetVisible(false);
            _pause?.Hide();
            _loading?.Open("내려가는 중");

            // One frame, so the loading screen is actually presented before the loader
            // starts stalling the main thread on asset decompression.
            yield return null;

            // §13 · the lobby's chosen building outranks the serialised default. TAKEN
            // rather than read: a building left lying in the seam would be loaded by the
            // next solo 시작 as well, which is the same defect as a seat number surviving
            // a session (see RaceParty.Clear).
            var chosen = LobbyEntry.MatchScene;
            LobbyEntry.MatchScene = null;
            var sceneName = !string.IsNullOrEmpty(chosen)
                ? chosen!
                : string.IsNullOrEmpty(_matchScene) ? DefaultMatchScene : _matchScene;
            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            if (load == null)
            {
                Debug.LogError(
                    "[Shell] '" + sceneName + "' is not in Build Settings, so 시작 has nowhere to go. "
                    + "Run HorrorGame ▸ Scene Gen ▸ Generate Bootstrap Scene, which registers it.", this);

                _loading?.Close();
                ShowMenu();
                yield break;
            }

            load.allowSceneActivation = false;

            var startedAt = Time.realtimeSinceStartup;
            while (load.progress < 0.9f)
            {
                _loading?.SetLoadProgress(load.progress);
                yield return null;
            }

            _loading?.SetProgress(1f);

            while (Time.realtimeSinceStartup - startedAt < MinimumLoadingSeconds)
            {
                yield return null;
            }

            load.allowSceneActivation = true;
            while (!load.isDone)
            {
                yield return null;
            }

            // After activation: the rig the field of view belongs to only exists now.
            SettingsService.Apply();

            _loading?.Close();
            _state = ShellState.Match;
        }

        private IEnumerator QuitToMenuRoutine()
        {
            EnsureScreens();

            MatchPause.Clear();
            _state = ShellState.Loading;
            _pause?.Hide();
            _settings?.SetVisible(false);
            _loading?.Open("지상으로");

            yield return null;

            var sceneName = string.IsNullOrEmpty(_menuScene) ? DefaultMenuScene : _menuScene;
            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            if (load == null)
            {
                Debug.LogError("[Shell] '" + sceneName + "' is not in Build Settings.", this);
                _loading?.Close();
                _state = ShellState.Match;
                yield break;
            }

            while (!load.isDone)
            {
                _loading?.SetLoadProgress(load.progress);
                yield return null;
            }

            _loading?.Close();
            ShowMenu();
        }
    }
}
