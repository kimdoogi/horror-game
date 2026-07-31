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
    /// <b>What it deliberately does not do.</b> It does not host, join, pick roles or
    /// step a match. §13 gives the host authority and §14 puts 매칭 · 로비 outside the
    /// prototype; <c>MatchDirector</c> is the host and starts itself when its scene comes
    /// up. This class chooses which scene that is and gets out of the way, which is why
    /// it compiles without Mirror and without the Steam layer.
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

            _menu?.Open(StartMatch, OpenSettingsFromMenu, Quit);
        }

        /// <summary>§14 step 1 — build a match and go down.</summary>
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
            _menu?.Open(StartMatch, OpenSettingsFromMenu, Quit);
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

            var sceneName = string.IsNullOrEmpty(_matchScene) ? DefaultMatchScene : _matchScene;
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
