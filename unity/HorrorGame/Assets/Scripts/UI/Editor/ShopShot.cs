#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using HorrorGame.Core;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;
using HorrorGame.UI.Screens;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HorrorGame.UI.EditorTools
{
    /// <summary>
    /// Photographs §08's shop in the states the design cares about, so "can four
    /// people read this in a dark room and argue about it?" is answered by looking.
    /// <para>
    /// <b>Why a tool of its own.</b> The whole-match photographer reaches the shop by
    /// driving a player to the vehicle, which means the picture depends on the
    /// interaction layer, the map, and whichever key currently opens the panel. Every
    /// question this screen raises — is the 대가 as loud as the 효과, does §11's hole
    /// have a visible answer, does §07's night read as a cost — is a question about
    /// the screen and not about the match, and the match is the part that keeps
    /// changing. So this builds the screens the way their owner does (one child object
    /// per screen, the HUD bound first) and opens the shop directly.
    /// </para>
    /// <para>
    /// It is a photograph, not a test: it proves what the pixels look like. What the
    /// screen <em>does</em> is pinned in <c>UiTests</c>, and that the panel appears in
    /// a real match at all is the whole-match photographer's job.
    /// </para>
    /// <code>
    /// Unity -batchmode -silent-crashes -projectPath . \
    ///   -executeMethod HorrorGame.UI.EditorTools.ShopShot.Batch -shotTag shop
    /// </code>
    /// Never with <c>-nographics</c>: without a graphics device every frame is black.
    /// </summary>
    public static class ShopShot
    {
        private const string ShotFolder = "Shots";

        /// <summary>1080p, which is also <c>UiStyle.ReferenceResolution</c> — so the canvas scales 1:1 and the frame is what a player sees.</summary>
        private const int Width = 1920;

        /// <summary>See <see cref="Width"/>.</summary>
        private const int Height = 1080;

        /// <summary>Renders every state and writes them into <c>Shots/</c>.</summary>
        [MenuItem("HorrorGame/Play/Photograph the Shop", priority = 24)]
        public static void Menu()
        {
            Debug.Log("[ShopShot] " + Capture(Tag(), new List<string>()));
        }

        /// <summary>Batch entry point. Exits non-zero when no frame could be rendered.</summary>
        public static void Batch()
        {
            try
            {
                // Counted, not grepped. Testing the report for "0 frames" made a
                // ten-frame run exit 1, because "10 frames" ends in it — a red on a run
                // that did everything asked of it, which is the same class of lie as a
                // green on a run that did nothing.
                var written = new List<string>();
                var report = Capture(Tag(), written);
                Debug.Log("[ShopShot] " + report);
                EditorApplication.Exit(written.Count > 0 ? 0 : 1);
            }
            catch (Exception error)
            {
                Debug.LogError("[ShopShot] " + error);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// The states worth a frame each. Every one of them is a sentence from the
        /// design that the screen either says or fails to say.
        /// </summary>
        /// <param name="tag">Filename prefix for this batch of frames.</param>
        /// <param name="written">Filled with the frames written, so the caller can decide on a count rather than on a string.</param>
        private static string Capture(string tag, List<string> written)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(ShotFolder);

            // §08's opening position: "1차 잠입 전 구매력 0." Nothing is affordable and
            // every row has to say how far short the team is. §11's absence here is the
            // 섬광수, whose 섬광탄 §08 does not stock — the case that used to render as
            // an empty line.
            written.Add(Shoot(tag + "_broke", 0, Lineup(RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer), 0f, null));

            // Mid-match, mid-wallet, and the 청음사 is on the roster — so §08's 소음기
            // is the row that must not read as a plain upgrade.
            written.Add(Shoot(
                tag + "_listener",
                GameConstants.ShopCostBlueprint,
                Lineup(RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer),
                GameConstants.ThreatTierSeconds * 1.4f,
                null));

            // §11's 청음사 hole with the money to answer it: 감지기 marked, 심야 running.
            written.Add(Shoot(
                tag + "_detector",
                GameConstants.ShopCostUpgradedFlashlight * 2,
                Lineup(RoleId.Observer, RoleId.Runner, RoleId.Engineer, RoleId.Flasher),
                (GameConstants.ThreatTierSeconds * 2f) + 60f,
                null));

            // §11's one absence money cannot touch: "관측자만 대체 수단이 없다."
            written.Add(Shoot(
                tag + "_observer",
                GameConstants.ShopCostBattery * 3,
                Lineup(RoleId.Listener, RoleId.Runner, RoleId.Engineer, RoleId.Flasher),
                GameConstants.ThreatTierSeconds * 0.6f,
                null));

            // A refusal, spoken. The row the wallet does not cover is pressed.
            written.Add(Shoot(
                tag + "_refused",
                GameConstants.ShopCostBattery,
                Lineup(RoleId.Observer, RoleId.Runner, RoleId.Engineer, RoleId.Flasher),
                GameConstants.ThreatTierSeconds * 0.3f,
                screen => Press(screen, ShopItemId.UpgradedFlashlight)));

            // §08's 소음기 pressed with a 청음사 on the roster: warned, not blocked.
            written.Add(Shoot(
                tag + "_confirm",
                GameConstants.ShopCostSuppressor * 2,
                Lineup(RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Engineer),
                GameConstants.ThreatTierSeconds * 3.2f,
                screen => Press(screen, ShopItemId.Suppressor)));

            // A purchase that went through, with its 대가 restated as it is paid (§10).
            written.Add(Shoot(
                tag + "_bought",
                GameConstants.ShopCostUpgradedFlashlight * 2,
                Lineup(RoleId.Observer, RoleId.Runner, RoleId.Engineer, RoleId.Flasher),
                GameConstants.ThreatTierSeconds * 1.9f,
                screen => Press(screen, ShopItemId.UpgradedFlashlight)));

            // The keyboard cursor, moved without a mouse.
            written.Add(Shoot(
                tag + "_keyboard",
                GameConstants.ShopCostBlueprint * 2,
                Lineup(RoleId.Listener, RoleId.Observer, RoleId.Engineer, RoleId.Flasher),
                GameConstants.ThreatTierSeconds * 2.5f,
                screen => screen.MoveSelection(9)));

            // §07's pressure with the rung about to turn: forty seconds of deliberation
            // against the last minute of 밤. Same forty seconds as the frame above, and
            // it has to look like a different amount of money.
            written.Add(Shoot(
                tag + "_pressure",
                GameConstants.ShopCostUpgradedFlashlight,
                Lineup(RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Flasher),
                (GameConstants.ThreatTierSeconds * 2f) - 55f,
                screen => screen.TickVisit(40f)));

            // The same deliberation, early in the night, where it costs almost nothing.
            written.Add(Shoot(
                tag + "_unhurried",
                GameConstants.ShopCostUpgradedFlashlight,
                Lineup(RoleId.Listener, RoleId.Observer, RoleId.Runner, RoleId.Flasher),
                30f,
                screen => screen.TickVisit(40f)));

            return written.Count + " frames: " + string.Join(", ", written);
        }

        /// <summary>
        /// Builds the screens the way the match's own HUD holder does — one child
        /// object per screen, the HUD bound before the shop opens — so the shop finds
        /// §07's clock the same way it does in a match.
        /// </summary>
        private static string Shoot(
            string name, int credits, IReadOnlyList<RoleId> roster, float elapsedSeconds, Action<ShopScreen>? act)
        {
            var host = new GameObject("ShopShotHud");
            var camera = BuildCamera();

            try
            {
                var clock = new MatchClock(startOnSurface: true);
                clock.Tick(elapsedSeconds);

                var shop = new Shop(new Wallet(credits));
                Stock(shop, credits);

                var hudObject = new GameObject("Hud");
                hudObject.transform.SetParent(host.transform, false);
                var hud = hudObject.AddComponent<HudScreen>();
                hud.Bind(RoleId.Runner, clock, null, null, null);

                var shopObject = new GameObject("Shop");
                shopObject.transform.SetParent(host.transform, false);
                var screen = shopObject.AddComponent<ShopScreen>();

                screen.Open(shop, new LocalShopRequests(shop, null), roster, Missing(roster));

                if (act != null)
                {
                    act(screen);
                }

                var path = Path.Combine(ShotFolder, name + ".png");
                Render(camera, path);
                return name + ".png";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>
        /// Presses a row the way a mouse does — through the button's own click event —
        /// so the frame shows what a press actually produces rather than what a direct
        /// call to the handler would.
        /// </summary>
        private static void Press(ShopScreen screen, ShopItemId item)
        {
            var button = screen.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < button.Length; i++)
            {
                if (button[i].name == "Row_" + item)
                {
                    button[i].onClick.Invoke();
                    return;
                }
            }

            Debug.LogWarning("[ShopShot] no row for " + item);
        }

        /// <summary>Puts a little of §08's stock on the vehicle, so the 보유 column is not always blank in a photograph.</summary>
        private static void Stock(Shop shop, int credits)
        {
            if (credits < GameConstants.ShopCostBattery * 4)
            {
                return;
            }

            shop.TryBuy(ShopItemId.Battery, 2);
            shop.TryBuy(ShopItemId.RepairMaterials, 1);
        }

        private static IReadOnlyList<RoleId> Lineup(RoleId a, RoleId b, RoleId c, RoleId d)
        {
            return new[] { a, b, c, d };
        }

        /// <summary>The one of §04's five nobody in this lineup took.</summary>
        private static RoleId Missing(IReadOnlyList<RoleId> roster)
        {
            var all = RoleSelection.AllRoles;
            for (var i = 0; i < all.Count; i++)
            {
                var found = false;
                for (var j = 0; j < roster.Count; j++)
                {
                    if (roster[j] == all[i])
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return all[i];
                }
            }

            return RoleId.None;
        }

        private static Camera BuildCamera()
        {
            var go = new GameObject("ShopShotCamera", typeof(Camera));
            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;

            // Not black, and deliberately neutral. The panel is nearly opaque black
            // itself and a black backdrop would hide whether its edges land where they
            // are meant to — but a tinted one bleeds through the panel's remaining 6%
            // and would have every colour judgement made off a photograph the game
            // never produces.
            camera.backgroundColor = new Color(0.10f, 0.10f, 0.10f, 1f);
            camera.nearClipPlane = 0.05f;

            // Post-processing off. In a real session these canvases are Screen Space
            // Overlay and are composited by the display, so URP's grade never touches
            // them — leaving it on here put a blue cast on every glyph and would have
            // had the panel's contrast judged from a photograph the game never
            // produces.
            var urp = camera.GetUniversalAdditionalCameraData();
            if (urp != null)
            {
                urp.renderPostProcessing = false;
            }

            return camera;
        }

        /// <summary>
        /// Renders every canvas in the scene through one camera.
        /// <para>
        /// A Screen Space Overlay canvas is composited by the display and never appears
        /// in <c>Camera.Render</c>, so each one is switched to camera space against a
        /// single plane — which leaves <c>sortingOrder</c> deciding what covers what,
        /// exactly as it does on a display.
        /// </para>
        /// </summary>
        private static void Render(Camera camera, string path)
        {
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < canvases.Length; i++)
            {
                if (!canvases[i].isRootCanvas)
                {
                    continue;
                }

                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = camera;
                canvases[i].planeDistance = camera.nearClipPlane * 1.1f;
            }

            Canvas.ForceUpdateCanvases();

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
            };

            var previousActive = RenderTexture.active;

            try
            {
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();

                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static string Tag()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-shotTag")
                {
                    return args[i + 1];
                }
            }

            return "shop";
        }
    }
}
