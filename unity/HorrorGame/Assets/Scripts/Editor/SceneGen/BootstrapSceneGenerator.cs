using HorrorGame.Core;
using HorrorGame.Core.Roles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// Builds the scene the player actually launches into: main menu, lobby, role
    /// select.
    /// <para>
    /// It is generated rather than hand-built for the same reason the map is — the
    /// role list, the player count and the objective escort minimum all come from
    /// <see cref="GameConstants"/> and <see cref="RoleId"/>, so a sixth role or a
    /// change to §11's 인원 구조 rebuilds the screen instead of leaving a stale panel
    /// nobody noticed. §14 puts 매칭 · 로비 outside the 2-week prototype, so this is
    /// deliberately structure without behaviour: named objects, correct counts, and
    /// no logic.
    /// </para>
    /// <para>
    /// Nothing here references Mirror or the Steam layer. The bootstrap object is a
    /// named, empty anchor (<see cref="NetBootstrapName"/>) that the Net layer attaches
    /// its <c>NetworkManager</c> to; wiring a transport from the Editor assembly would
    /// put a networking decision in the scene generator, which is the wrong layer and
    /// would make this file un-buildable whenever the Net layer is mid-change.
    /// </para>
    /// </summary>
    public static class BootstrapSceneGenerator
    {
        /// <summary>Empty anchor the Net layer hangs its <c>NetworkManager</c> on.</summary>
        public const string NetBootstrapName = "NetBootstrap";

        /// <summary>Root of the main menu panel — the only one active at load.</summary>
        public const string MainMenuName = "MainMenu";

        /// <summary>Root of the lobby panel.</summary>
        public const string LobbyName = "Lobby";

        /// <summary>Root of the role-select panel.</summary>
        public const string RoleSelectName = "RoleSelect";

        /// <summary>Builds and saves the bootstrap scene.</summary>
        [MenuItem("HorrorGame/Scene Gen/Generate Bootstrap Scene", priority = 23)]
        public static void GenerateMenu()
        {
            Generate();
            Debug.Log("[SceneGen] Wrote " + SceneGenPaths.BootstrapScene + ".");
        }

        /// <summary>Batch entry point.</summary>
        public static void GenerateFromCommandLine()
        {
            Generate();
            EditorApplication.Exit(0);
        }

        /// <summary>Builds the scene and writes it to <see cref="SceneGenPaths.BootstrapScene"/>.</summary>
        public static bool Generate()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneGenPaths.EnsureFolder(SceneGenPaths.SceneRoot);

            var camera = new GameObject("MenuCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 1f, -10f);
            camera.gameObject.AddComponent<AudioListener>();

            var bootstrap = new GameObject(NetBootstrapName);
            bootstrap.transform.position = Vector3.zero;

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            events.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            BuildMainMenu(canvasGo);
            BuildLobby(canvasGo);
            BuildRoleSelect(canvasGo);

            var saved = EditorSceneManager.SaveScene(scene, SceneGenPaths.BootstrapScene);
            if (saved)
            {
                AssetDatabase.SaveAssets();
                MapSceneGenerator.RegisterScenes();
            }

            return saved;
        }

        private static void BuildMainMenu(GameObject canvas)
        {
            var panel = Panel(canvas, MainMenuName, true);
            Label(panel, "Title", "HORROR GAME", 72, new Vector2(0f, 260f));
            Label(panel, "Subtitle", "4인 비대칭 협동 — 프로토타입", 28, new Vector2(0f, 190f));

            // §14 step 2 is "Mirror 로컬 호스트 — 같은 PC 2인스턴스", so Host and Join are
            // the only two entries that have to exist before there is any matchmaking.
            Button(panel, "HostButton", "호스트로 시작 (Host)", new Vector2(0f, 60f));
            Button(panel, "JoinButton", "참가 (Join)", new Vector2(0f, -20f));
            Button(panel, "QuitButton", "종료", new Vector2(0f, -100f));

            Label(panel, "BuildNote",
                "§14: 음성은 디스코드. 게임 내 근접 음성은 5단계.", 20, new Vector2(0f, -280f));
        }

        private static void BuildLobby(GameObject canvas)
        {
            var panel = Panel(canvas, LobbyName, false);
            Label(panel, "Title", "로비", 56, new Vector2(0f, 300f));

            // §11 인원 구조: four players per match, five roles to choose from — the
            // asymmetry is that somebody always goes without.
            for (var i = 0; i < GameConstants.PlayersPerMatch; i++)
            {
                var y = 160f - (i * 90f);
                var slot = Panel(panel, "PlayerSlot_" + i, true);
                Label(slot, "Name", "빈 자리 " + (i + 1), 32, new Vector2(-200f, y));
                Label(slot, "Role", "—", 28, new Vector2(220f, y));
            }

            Label(panel, "EscortNote",
                "§03: 목표물 운반에는 " + GameConstants.ObjectiveEscortMinPlayers + "명이 필요하다.",
                22, new Vector2(0f, -220f));
            Button(panel, "ReadyButton", "준비", new Vector2(-160f, -320f));
            Button(panel, "BackButton", "뒤로", new Vector2(160f, -320f));
        }

        private static void BuildRoleSelect(GameObject canvas)
        {
            var panel = Panel(canvas, RoleSelectName, false);
            Label(panel, "Title", "직업 선택", 56, new Vector2(0f, 320f));

            // Generated from RoleId so the screen cannot drift from §04. RoleId.None is
            // not a choice — it is the absence of one.
            var roles = (RoleId[])System.Enum.GetValues(typeof(RoleId));
            var index = 0;
            foreach (var role in roles)
            {
                if (role == RoleId.None)
                {
                    continue;
                }

                var y = 180f - (index * 90f);
                Button(panel, "Role_" + role, KoreanName(role) + "  (" + role + ")", new Vector2(0f, y));
                index++;
            }

            Label(panel, "CountNote",
                "§11: 직업 " + GameConstants.RoleCount + "개, 한 판 " + GameConstants.PlayersPerMatch + "명 — 하나는 항상 빠진다.",
                22, new Vector2(0f, -280f));
            Button(panel, "ConfirmButton", "확정", new Vector2(0f, -360f));
        }

        /// <summary>§04's names, so the screen reads the way the design document does.</summary>
        private static string KoreanName(RoleId role)
        {
            switch (role)
            {
                case RoleId.Listener: return "청음사";
                case RoleId.Observer: return "관측자";
                case RoleId.Runner: return "주자";
                case RoleId.Engineer: return "정비공";
                case RoleId.Flasher: return "섬광수";
                default: return role.ToString();
            }
        }

        // ====================================================================
        // uGUI plumbing. No behaviour: §14 puts 로비 outside the prototype, so these
        // are named anchors for the UI layer rather than a half-written menu.
        // ====================================================================

        private static GameObject Panel(GameObject parent, string name, bool active)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.SetActive(active);
            return go;
        }

        private static Text Label(GameObject parent, string name, string content, int size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(900f, size + 24f);

            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.88f, 0.88f, 0.9f);
            text.font = DefaultFont();
            return text;
        }

        private static Button Button(GameObject parent, string name, string content, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(560f, 68f);
            go.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.16f);

            Label(go, "Text", content, 30, Vector2.zero);
            return go.GetComponent<Button>();
        }

        private static Font DefaultFont()
        {
            // LegacyRuntime.ttf is the built-in uGUI font in Unity 6; Arial.ttf was
            // removed. Falling through to null would render every label invisible,
            // which looks like a broken generator rather than a missing font.
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return font;
        }
    }
}
