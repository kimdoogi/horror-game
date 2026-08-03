#nullable enable

using System;
using System.IO;
using System.Text;
using HorrorGame.EditorTools.Audio;
using HorrorGame.EditorTools.Dressing;
using HorrorGame.EditorTools.SceneGen;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HorrorGame.EditorTools.Playtest
{
    /// <summary>
    /// One menu item that gets a person who has never seen this project into a playable
    /// match. §14.
    /// <para>
    /// <b>The problem this solves is not technical.</b> Every piece of §01's descent works
    /// and has for a while; what stops it being played is that reaching it takes three
    /// menu items in an order nobody wrote down, and the failure when they are run in the
    /// wrong order is a scene that loads and then does nothing. §14 says the two questions
    /// that decide the project "문서로 정할 수 없고 직접 만져봐야 나온다" — so the distance
    /// between opening the editor and touching it has to be one click, and every failure
    /// on the way has to name itself instead of dropping the tester into a dead scene.
    /// </para>
    /// <para>
    /// <b>Four steps, each of which can refuse.</b> The map is regenerated when it is
    /// missing or older than the kit it is built from; the solo scene is assembled from
    /// it; the audio mix is wired in, because §14's fifth question is answered entirely
    /// by ear; and the finished scene is inspected for the three components a match needs
    /// before Play mode is entered. A step that fails reports which one it was and what to
    /// do about it, and Play mode is never entered after one.
    /// </para>
    /// </summary>
    public static class StartPlaytest
    {
        private const string MapScenePath = SceneGenPaths.MapScene;
        private const string SoloScenePath = "Assets/Scenes/Map_FirstSketch_Solo.unity";
        private const string MapKitFolder = "Assets/Models/MapKit";

        /// <summary>§07 row baked into the generated scene when this has to regenerate it. Tier 0 is where §01's first descent happens.</summary>
        private const int AtmosphereTier = 0;

        /// <summary>
        /// Regenerates whatever is missing, builds the solo scene, and presses Play.
        /// </summary>
        [MenuItem("HorrorGame/Play/▶ START PLAYTEST", priority = 0)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (!Prepare(out var step, out var report))
            {
                Debug.LogError("[StartPlaytest] " + step + " 실패\n" + report);
                EditorUtility.DisplayDialog("▶ START PLAYTEST — " + step + " 실패", report, "확인");
                return;
            }

            Debug.Log("[StartPlaytest]\n" + report);
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// Batch entry point. Does everything <see cref="Run"/> does except enter Play
        /// mode, so the whole chain can be proved from a terminal on a cold project.
        /// Exits non-zero on the first step that refuses.
        /// </summary>
        public static void PrepareBatch()
        {
            try
            {
                var ok = Prepare(out var step, out var report);
                if (!ok)
                {
                    Debug.LogError("[StartPlaytest] " + step + " 실패\n" + report);
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log("[StartPlaytest]\n" + report);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[StartPlaytest] " + error);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Everything up to Play mode. Never throws for a foreseeable failure — the step
        /// that refused is named in <paramref name="step"/> and explained in
        /// <paramref name="report"/>.
        /// </summary>
        /// <param name="step">The Korean name of the step that failed, or 준비 on success.</param>
        /// <param name="report">What happened, step by step, in the order it happened.</param>
        public static bool Prepare(out string step, out string report)
        {
            var log = new StringBuilder();

            // ---------------------------------------------------------------
            // 1 · 지도 — §12's 첫 맵 스케치, regenerated only when it has to be.
            // ---------------------------------------------------------------
            step = "지도 생성";
            var reason = MapNeedsRegenerating();
            if (reason.Length > 0)
            {
                log.AppendLine("지도  재생성 — " + reason);
                if (!MapPipeline.Regenerate(
                        DescentMap.DefaultSeed, DressingScatter.DefaultSeed, AtmosphereTier, out var mapReport))
                {
                    report = Explain(
                        log,
                        "§12가 지도를 거부했거나 배치 패스가 실패했습니다. 아래 보고서의 실패한 규칙을 보고, "
                        + "HorrorGame ▸ Scene Gen ▸ Report Map Quality 로 같은 등급을 다시 확인하세요.",
                        mapReport);
                    return false;
                }

                log.AppendLine("      " + FirstLine(mapReport));
            }
            else
            {
                log.AppendLine("지도  " + MapScenePath + " 그대로 사용");
            }

            // The map on disk is the thing every later step reads, so it is checked as a
            // map rather than as a file: MatchMap is what MatchDirector.BeginMatch calls,
            // and a scene that fails it produces a match that refuses to start with an
            // error in the console and a tester staring at a corridor.
            step = "지도 확인";
            if (!ReadableMap(out var mapFailure))
            {
                log.AppendLine("지도  구조 확인 실패 — 재생성합니다");
                if (!MapPipeline.Regenerate(
                        DescentMap.DefaultSeed, DressingScatter.DefaultSeed, AtmosphereTier, out var retry))
                {
                    report = Explain(log, "지도를 다시 만들 수 없습니다: " + mapFailure, retry);
                    return false;
                }

                if (!ReadableMap(out mapFailure))
                {
                    report = Explain(
                        log,
                        "지도를 다시 만들었는데도 MatchMap이 읽지 못합니다: " + mapFailure
                        + "\n이 상태로는 판이 시작되지 않습니다.",
                        string.Empty);
                    return false;
                }
            }

            log.AppendLine("      MatchMap 읽기 성공");

            // ---------------------------------------------------------------
            // 2 · 장면 — the throwaway solo scene: runner, creature, MatchDirector. No screen
            // is added here any more; MatchDirector builds RaceHud, which is the race's own.
            // ---------------------------------------------------------------
            step = "장면 조립";
            if (!SoloPlaytest.BuildScene())
            {
                report = Explain(
                    log,
                    "솔로 장면을 만들지 못했습니다. 콘솔의 [SoloPlaytest] 줄에 이유가 있습니다.",
                    string.Empty);
                return false;
            }

            log.AppendLine("장면  " + SoloScenePath + " 조립 완료");

            // ---------------------------------------------------------------
            // 3 · 소리 — §14 Q5 is answered by ear and by nothing else.
            // ---------------------------------------------------------------
            step = "오디오 배선";
            var scene = EditorSceneManager.GetActiveScene();
            var rig = AudioSceneWiring.Wire(scene);
            if (rig == null)
            {
                // Not fatal. A silent match still answers §14's questions 1 to 4, and
                // stopping here would hand back nothing at all.
                //
                // The parenthesis used to read (청음사). §14 Q5 is 「발소리의 방향·거리를
                // 구별할 수 있는가」 and it is asked of everybody — it was §04's 청음사 only
                // while one 직업 had hearing as an ability, and there are no 직업.
                log.AppendLine(
                    "소리  배선 실패 — 무음으로 진행합니다. §14 Q5(발소리의 방향·거리)는 이 판에서 답할 수 없습니다.");
                Debug.LogWarning("[StartPlaytest] 오디오 라이브러리를 만들지 못했습니다. "
                    + "HorrorGame ▸ Audio ▸ Rebuild Audio Libraries 를 실행해 보세요.");
            }
            else
            {
                log.AppendLine("소리  " + rig.name + " 배선 완료");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SoloScenePath, saveAsCopy: false);
            AssetDatabase.SaveAssets();

            // ---------------------------------------------------------------
            // 4 · 점검 — never hand over a scene that cannot play.
            // ---------------------------------------------------------------
            step = "장면 점검";
            if (!Inspect(out var missing))
            {
                report = Explain(
                    log,
                    "조립된 장면에 " + missing + " 이(가) 없습니다. 이 상태로 Play를 누르면 아무 일도 일어나지 않습니다.",
                    string.Empty);
                return false;
            }

            log.AppendLine("점검  MatchDirector · 플레이어 리그 · 괴물 셋 다 있습니다");
            log.AppendLine();

            // DELETED with the co-op tutor, and this is the second half of the same removal
            // as Inspect()'s 안내 화면 check. These two lines read:
            //
            //     "Play 시작. 화면 아래 한 줄이 다음에 할 일을 알려줍니다."
            //     "F1  조작 카드 다시 보기      F2  §14 검증 질문 다섯 개"
            //
            // Both were PlaytestGuidanceScreen: the bottom line was the next step of §01's
            // 왕복 (내려가라 · 단서를 읽어라 · 목표물을 들고 올라와라 · 팔아라), and F1/F2 were
            // its two overlays. The screen is gone, so the bottom line does not exist and
            // neither key does anything. Telling a tester to press a dead key is worse than
            // telling them nothing: they press it, see nothing, and start debugging the
            // build. What replaces it is a description of 하강 itself, and the storey count
            // and the interact key in it are read from the generator and the input map so
            // that this cannot drift the way the last one did.
            log.AppendLine("Play 시작 — 하강. 선착순 미로탈출이고, 그게 전부다.");
            log.AppendLine("경주  B1 외곽에서 출발 → 층 한가운데로 파고들어 투하구에 들어가면 아래층 외곽에 떨어진다.");
            log.AppendLine("      " + DescentMap.Storeys + "개 층, 관문은 4 → 2 → 1로 좁아진다. B"
                + DescentMap.Storeys + " 한가운데에 제일 먼저 닿으면 이긴다.");
            log.AppendLine("괴물  층마다 한 마리, 자기 층에서 나가지 않는다. 잡히면 탈락 — 순위 없이 끝이고 경주는 계속된다.");
            log.AppendLine("조작  WASD 이동 · 마우스 시야 · Shift 질주 · C 웅크리기 · Space 도약 · F 손전등 · "
                + PlayerInteractor.InteractKeyLabel + " 문(길게 누르면 닫는다)");
            log.AppendLine("화면  MatchDirector가 띄우는 RaceHud 하나뿐이다 — 깊이 게이지 · 순위 · 경과 시간.");

            step = "준비";
            report = log.ToString();
            return true;
        }

        /// <summary>
        /// Why the map has to be rebuilt, or an empty string when it does not.
        /// <para>
        /// Two reasons only, and both are things a cold checkout actually hits: there is
        /// no scene, or the MapKit meshes the scene is assembled from have been rewritten
        /// since it was built. The second one matters because the generators are run from
        /// Blender by a separate script — a regenerated kit leaves a scene on disk that
        /// still loads and quietly references the old meshes.
        /// </para>
        /// </summary>
        private static string MapNeedsRegenerating()
        {
            if (!File.Exists(MapScenePath))
            {
                return "지도가 없습니다 (처음 여는 프로젝트)";
            }

            var sceneWritten = File.GetLastWriteTimeUtc(MapScenePath);
            var kit = NewestWriteUnder(MapKitFolder);

            if (kit > sceneWritten)
            {
                return "MapKit이 지도보다 새것입니다 (" + MapKitFolder + ")";
            }

            return string.Empty;
        }

        private static DateTime NewestWriteUnder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return DateTime.MinValue;
            }

            var newest = DateTime.MinValue;
            foreach (var file in Directory.GetFiles(folder, "*.fbx", SearchOption.AllDirectories))
            {
                var written = File.GetLastWriteTimeUtc(file);
                if (written > newest)
                {
                    newest = written;
                }
            }

            return newest;
        }

        /// <summary>Opens the generated map and asks <c>MatchMap</c> whether a match could be laid out on it.</summary>
        private static bool ReadableMap(out string failure)
        {
            if (!File.Exists(MapScenePath))
            {
                failure = MapScenePath + " 가 없습니다.";
                return false;
            }

            EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Single);
            return MatchMap.TryRead(out _, out failure);
        }

        /// <summary>
        /// The three things the scene has to carry. Checked by type on the open scene
        /// rather than trusted, because two broken hand-overs have already happened here.
        /// <para>
        /// It was four while <c>PlaytestGuidanceScreen</c> existed. See the tombstone below
        /// for why the fourth went, and <see cref="Prepare"/> for the two lines of printed
        /// guidance that went with it.
        /// </para>
        /// </summary>
        private static bool Inspect(out string missing)
        {
            if (UnityEngine.Object.FindFirstObjectByType<MatchDirector>() == null)
            {
                missing = "MatchDirector";
                return false;
            }

            if (UnityEngine.Object.FindFirstObjectByType<PlayerMotor>() == null)
            {
                missing = "플레이어 리그(PlayerMotor)";
                return false;
            }

            if (UnityEngine.Object.FindFirstObjectByType<MonsterAgent>() == null)
            {
                missing = "괴물(MonsterAgent)";
                return false;
            }

            // DELETED with the co-op tutor: the PlaytestGuidanceScreen check. The scene
            // no longer carries one — it drew the next step of §01's 왕복, the clue count,
            // the wallet and §02's four outcomes. MatchDirector builds RaceHud, which is
            // the race's own screen.

            missing = string.Empty;
            return true;
        }

        private static string Explain(StringBuilder log, string what, string detail)
        {
            log.AppendLine();
            log.AppendLine(what);

            if (detail.Length > 0)
            {
                log.AppendLine();
                log.AppendLine(detail);
            }

            return log.ToString();
        }

        private static string FirstLine(string text)
        {
            var end = text.IndexOf('\n');
            return end < 0 ? text : text.Substring(0, end);
        }
    }
}
