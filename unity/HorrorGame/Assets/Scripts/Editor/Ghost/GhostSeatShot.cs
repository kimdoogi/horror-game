#nullable enable

// WHY THIS FILE LIVES UNDER Assets/Scripts/Editor/ AND NOT NEXT TO WHAT IT WIRES
// ─────────────────────────────────────────────────────────────────────────────
// It used to sit in Assets/Scripts/Gameplay/{Audio,Ghost}/Editor/, which had no
// asmdef over it, so it landed in Assembly-CSharp-Editor. R12 gave Gameplay its
// own asmdef — the player was shipping nine PlayMode fixtures inside
// Assembly-CSharp because that folder had none — and an asmdef swallows the
// Editor special folders inside its scope. Giving those folders editor asmdefs of
// their own does not work either: this file calls SoloPlaytest, which is in
// Assembly-CSharp-Editor, and an asmdef assembly cannot reference a predefined
// one. The dependency only runs the other way.
//
// So it moves instead. Here it is still Assembly-CSharp-Editor, still sees
// SoloPlaytest, and still auto-references every autoReferenced asmdef including
// HorrorGame.Gameplay. Nothing about what it does changed; only which DLL it is
// compiled into, and it was always an editor tool.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Ghost;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Photographs §09's <em>seat</em>. What a dead player sees, from the eye they get.
    /// <para>
    /// <b>Not to be confused with <c>GhostMaterials</c>'s shot tool</b>, which photographs
    /// the 유령 model at 3 m and 15 m. That one asks whether the creature reads; this one
    /// asks whether the state does. They write different prefixes — <c>ghostseat_*</c>
    /// here, <c>ghost_*</c> there — because they were written in the same night by two
    /// passes that could not see each other, and a shared prefix would have had each one
    /// silently overwriting the other's evidence.
    /// </para>
    /// <para>
    /// <b>Why a picture is the only thing that can answer this.</b> Every other claim in
    /// this pass is testable — the ghost state is entered, there is no path to a voice
    /// channel or to the exit, nothing it does reaches a living runner — and none of them
    /// touches the question §09 actually asks, which is 탈락하면 지루하다 / 볼 게 있고 할
    /// 게 있다. An eliminated player flying through an unlit eight-storey tower with
    /// nothing on screen is a passing test and a black rectangle. STATUS.md §4.3 measures
    /// the building's zone views at a median of 4.2–8.4 of 255 with the practicals off, so
    /// that outcome is the default and not a hypothetical.
    /// </para>
    /// <para>
    /// So this walks the real path: begin a race, descend, get caught through
    /// <c>MatchDirector</c>'s own grab, and photograph the frames that follow — the moment
    /// after, the runner's own body, three presses of §09's verb (the cut between §06's
    /// creatures, §02's 도착점 and the body), up through the storeys, and over §06. It
    /// writes an <c>_asplayed</c> and a <c>_nograde</c> exposure of the same frame so
    /// <see cref="GhostViewGrade"/>'s stops can be judged rather than assumed:
    /// <c>tools/render/frame_stats.py</c> reads both against ART.md's bands.
    /// </para>
    /// <para>
    /// Run WITHOUT <c>-nographics</c>, or every frame is black for a much less
    /// interesting reason:
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath . \
    ///   -executeMethod HorrorGame.EditorTools.GhostSeatShot.Batch -shotTag ghostseat
    /// </code>
    /// </para>
    /// </summary>
    public static class GhostSeatShot
    {
        private const string OutputDir = "Shots";
        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>Batch entry point. Exits 0 when every frame was written, 1 otherwise.</summary>
        public static void Batch()
        {
            try
            {
                var tag = ArgValue("-shotTag") ?? "ghostseat";
                var sweep = ArgValue("-ghostExposureSweep");
                Debug.Log("[GhostSeatShot]\n" + Capture(tag, sweep != null));
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[GhostSeatShot] " + error);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Menu twin, so the same set can be made without a terminal.</summary>
        [MenuItem("HorrorGame/Play/Photograph a Ghost's Seat (§09)", priority = 26)]
        public static void Menu()
        {
            Debug.Log("[GhostSeatShot]\n" + Capture("ghostseat", sweepExposure: false));
        }

        /// <summary>
        /// Builds the solo scene, kills the player where they stand, and writes the frames
        /// §09 produces. Returns the report that goes under them.
        /// </summary>
        public static string Capture(string tag, bool sweepExposure)
        {
            if (!SoloPlaytest.BuildScene())
            {
                throw new InvalidOperationException(
                    "the solo scene could not be built, so there is nothing to photograph");
            }

            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDir);
            Directory.CreateDirectory(root);

            var director = UnityEngine.Object.FindFirstObjectByType<MatchDirector>();
            var motor = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
            var monster = UnityEngine.Object.FindFirstObjectByType<MonsterAgent>();
            if (director == null || motor == null)
            {
                throw new InvalidOperationException("the solo scene has no MatchDirector or no player rig");
            }

            if (!director.BeginMatch(SoloPlaytest.PlaytestSeed))
            {
                throw new InvalidOperationException("BeginMatch refused the playtest seed");
            }

            var report = new StringBuilder();
            report.AppendLine("§09 — what a dead player is given to look at");

            // §01 used to put everyone on the surface at t=0 and §09 only minted a ghost
            // below ground, so the walk down was part of the path being photographed. A
            // runner now starts on the rim of B1 with the whole tower below them, and there
            // is nowhere a death is refused — but the walk is kept, moved onto a deeper
            // storey, because a ghost photographed on the storey it entered on shows less
            // of the building than one photographed halfway down.
            var down = Descend(director, motor);
            report.AppendLine("  descended to  " + Fmt(down));

            // DELETED with §08: the Pocket() step. §09's consolation used to be half about
            // the pile — 「자기 물건이 어디 있는지 보이는데 말할 수 없다」 — so this put a
            // 전리품 in the runner's pockets before killing them, or the frame would have
            // been of the rule not firing. There is nothing to pick up, and a race ghost's
            // consolation is watching the other nineteen finish.
            //
            // The MatchState null-guard that stood here went with it. BeginMatch already
            // returned true above, which is the same question asked of the thing that
            // answers it, and holding a reference here only tied this shot rig to a Core
            // type it never used.

            // Through the host, not around it. Calling the kill directly would
            // photograph a ghost that never went through CheckGrab, which is the wiring
            // this whole pass is about.
            Kill(director, motor, monster);

            var ghosts = director.Ghosts;
            if (ghosts == null || !ghosts.IsActive)
            {
                throw new InvalidOperationException(
                    "the player died and §09 did not take over — GhostSession.IsActive is false");
            }

            var ghost = ghosts.Ghost!;
            var camera = ResolveGhostCamera(motor);
            if (camera == null)
            {
                throw new InvalidOperationException("§09 has no camera, so there is nothing to photograph");
            }

            PrepareCamera(camera);
            DisableOtherCameras(camera);

            report.AppendLine("  died at       " + Fmt(ghost.DeathPosition.ToVector3())
                + ", 말하기 " + (ghost.CanSpeak ? "가능 ⚠" : "불가능")
                + ", 탈출 " + (ghost.CanEscape ? "가능 ⚠" : "불가능"));

            // 00 — the first frame of §09, from where the eye already was.
            Settle(ghosts);
            Shoot(camera, root, tag, "00_the_moment_after", report, "the eye it kept, at the body");

            // 10 — turned round to look at its own body. §02's 탈락 has no 순위 and no way
            // back, and this is the frame that says so: the runner is lying there and the
            // race is still going on above them.
            LookAt(ghosts, camera, ghost.DeathPosition.ToVector3() + (Vector3.up * 0.9f), 3.5f);
            Shoot(camera, root, tag, "10_my_own_body", report, "순위 없음 · 복귀 불가능");

            // 20 · 25 — §09's one verb, pressed twice.
            //
            // DELETED with the rattle: the 20_beside_a_thing / 25_just_rattled pair, which
            // flew to the nearest Interactable and shook it. There is no outbound verb to
            // photograph any more — the argument is on GhostSession — so what is
            // photographed instead is the thing that replaced it: the cut. Driven through
            // CutToNextVantage rather than through a keyboard, because Update does not run
            // outside play mode and a key press here would photograph nothing happening.
            for (var cut = 0; cut < VantageCuts; cut++)
            {
                if (!ghosts.CutToNextVantage())
                {
                    report.AppendLine("  2" + cut + "_cut               skipped — the ghost refused the cut ⚠");
                    break;
                }

                Settle(ghosts);
                Shoot(camera, root, tag, "2" + cut + "_cut", report,
                    "[" + GhostSession.WatchKey + "] → "
                    + (ghosts.IsWatching ? ghosts.WatchLabel : "자유 비행"));
            }

            // 30 — straight up through the storeys. §09's 벽 통과, which is the row that
            // makes this a reward rather than a lock screen.
            var above = camera.transform.position + (Vector3.up * StoreysUpMetres);
            Fly(ghosts, camera, above, Quaternion.Euler(35f, camera.transform.eulerAngles.y, 0f));
            Shoot(camera, root, tag, "30_through_the_ceiling", report,
                StoreysUpMetres.ToString("0", CultureInfo.InvariantCulture) + " m up, through the floors");

            // 40 — over §06, which is the diagnostic this view exists to be.
            if (monster != null)
            {
                var at = monster.transform.position;
                LookAt(ghosts, camera, at + (Vector3.up * 1.2f), MonsterStandOffMetres);
                director.StepMatch(GameConstants.FixedStep);
                Settle(ghosts);
                Shoot(camera, root, tag, "40_watching_the_monster", report,
                    "§06 " + monster.State + " · §07 " + monster.Tier.Phase
                    + " · " + monster.Tier.MonsterSpeed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s"
                    + " · " + Vector3.Distance(camera.transform.position, at)
                        .ToString("0", CultureInfo.InvariantCulture) + "m");
            }

            // 50 — §02 has decided and is being held for the ghost.
            var race = director.Race;
            report.AppendLine("  §02 "
                + (race != null
                    ? race.ExitOf(director.LocalPlayerIndex).ToString()
                    : "no RaceDirector ⚠")
                + ", held for §09: " + (director.RaceVerdictHeldForGhost ? "yes" : "NO ⚠")
                + ", match running: " + (director.IsRunning ? "yes" : "NO ⚠"));

            Settle(ghosts);
            Shoot(camera, root, tag, "50_the_verdict_waits", report,
                "the end screen is not up; the ghost is still flying");

            if (sweepExposure)
            {
                report.AppendLine(SweepExposure(camera, root, tag));
            }

            return report.ToString();
        }

        // ------------------------------------------------------------------
        // Driving the ghost the way a player would.
        // ------------------------------------------------------------------

        /// <summary>
        /// Walks the rig down out of §01's 지상 so a death is legal. §09 only mints a
        /// ghost below ground, and the host's kill path refuses on the surface.
        /// </summary>
        private static Vector3 Descend(MatchDirector director, PlayerMotor motor)
        {
            var map = director.Map;
            if (map == null)
            {
                return motor.transform.position;
            }

            // The creature starts rather than §12's 후보 지점, which are deleted. Every
            // PlayerSpawn on this map is on B1, so picking the deepest of THOSE would leave
            // the runner where they began and make the descent a no-op. MonsterSpawns are
            // one per storey by construction, so the deepest is on B8 — and standing the
            // ghost where the creature starts is the frame this tool wants anyway.
            var chosen = motor.transform.position;
            var deepest = float.PositiveInfinity;

            for (var i = 0; i < map.MonsterSpawns.Count; i++)
            {
                var at = map.MonsterSpawns[i].position;
                if (at.y >= deepest)
                {
                    continue;
                }

                deepest = at.y;
                chosen = at;
            }

            Teleport(motor.gameObject, chosen + (Vector3.up * 0.2f));

            // One fixed step so the match sees the move before anything is photographed.
            director.StepMatch(GameConstants.FixedStep);
            return motor.transform.position;
        }

        /// <summary>
        /// Kills the player through <c>MatchDirector.CheckGrab</c> by standing §06's
        /// monster on them in 추격, which is the only path a real death takes.
        /// </summary>
        private static void Kill(MatchDirector director, PlayerMotor motor, MonsterAgent? monster)
        {
            if (monster == null)
            {
                throw new InvalidOperationException("no monster in the scene, so nothing can catch the player");
            }

            // The player is moved onto the monster rather than the other way round. The
            // monster's transform is written by its own NavMeshAgent every step, so a
            // position pushed in here is gone by the time §06's brain reads it; the
            // player's rig has no such owner while MovementLocked.
            //
            // And it is stood in FRONT of the monster first, not on top of it. §06's cone
            // is ±90° of the facing, so a player at zero separation is at an arbitrary
            // bearing and is very often behind — which is how the first version of this
            // spent 240 steps with the two bodies 0.14 m apart and the brain in 순찰.
            //
            // A footstep is reported with it. §06's table gives 순찰 exactly one
            // transition — 소리 감지 → 경계 — and only 경계 has 시야 확보 → 추격, so a
            // silent player standing in front of a patrolling monster is never seen. In a
            // real match PlayerFootsteps raises this from the rig's own movement; nothing
            // moves outside play mode, so the shot tool has to say it out loud.
            for (var i = 0; i < KillSteps && monster.State != MonsterStateId.Chase; i++)
            {
                var lure = InFrontOf(monster);
                Teleport(motor.gameObject, lure);
                monster.ReportSound(lure, GameConstants.MonsterSightRange);
                director.StepMatch(GameConstants.FixedStep);
            }

            // In 추격 the sight line no longer has to hold — §06 releases only on 3 s of
            // cover AND 12 m — so the two can now be put in contact, which is the
            // condition MatchDirector.CheckGrab measures.
            for (var i = 0; i < KillSteps && !director.LocalPlayerIsGhost; i++)
            {
                Teleport(motor.gameObject, monster.transform.position);
                director.StepMatch(GameConstants.FixedStep);
            }

            if (!director.LocalPlayerIsGhost)
            {
                var brain = monster.Brain;
                throw new InvalidOperationException(
                    "§06's monster stood on the player for " + KillSteps
                    + " steps and CheckGrab never fired, so §09 was never reached."
                    + "  state=" + monster.State
                    + "  player=" + Fmt(motor.transform.position)
                    + "  monster=" + Fmt(monster.transform.position)
                    + "  target=" + (brain != null ? brain.ChaseTargetId.ToString(CultureInfo.InvariantCulture) : "no brain")
                    + "  targets=" + monster.Targets.Count.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// A spot inside §06's cone with an unobstructed sight line to it. Searched around
        /// the monster's own facing because a wall 0.5 m ahead defeats
        /// <c>NavMeshWorldProbe.HasLineOfSight</c> as completely as standing behind it.
        /// </summary>
        private static Vector3 InFrontOf(MonsterAgent monster)
        {
            var eye = monster.transform.position + (Vector3.up * 1.5f);

            for (var offset = 0f; offset <= 75f; offset += 15f)
            {
                foreach (var sign in new[] { 1f, -1f })
                {
                    var heading = Quaternion.Euler(0f, offset * sign, 0f) * monster.transform.forward;
                    heading.y = 0f;
                    if (heading.sqrMagnitude < 0.0001f)
                    {
                        continue;
                    }

                    heading.Normalize();
                    if (Physics.Raycast(eye, heading, LureDistanceMetres + 0.5f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    return monster.transform.position + (heading * LureDistanceMetres);
                }
            }

            return monster.transform.position + (monster.transform.forward * LureDistanceMetres);
        }

        /// <summary>Flies the ghost to a pose, through the real component so Core's position follows.</summary>
        private static void Fly(GhostSession ghosts, Camera camera, Vector3 to, Quaternion facing)
        {
            camera.transform.SetPositionAndRotation(to, facing);

            // GhostFreeCamera pushes MoveTo from Drive; a zero-move drive with a real
            // delta is how the shot tool tells Core where the ghost now is without
            // inventing a second way to move it.
            var fly = ghosts.GetComponent<UI.Screens.GhostFreeCamera>();
            if (fly != null)
            {
                fly.Drive(Vector3.zero, Vector2.zero, false, GameConstants.FixedStep);
            }

            Settle(ghosts);
        }

        /// <summary>Puts the ghost <paramref name="standOff"/> metres from a point, looking at it.</summary>
        private static void LookAt(GhostSession ghosts, Camera camera, Vector3 at, float standOff)
        {
            // Backwards along the camera's own heading, so the framing keeps whatever the
            // previous shot established rather than snapping to a world axis.
            var back = camera.transform.forward;
            back.y = 0f;
            if (back.sqrMagnitude < 0.0001f)
            {
                back = Vector3.forward;
            }

            back.Normalize();

            var from = at - (back * standOff) + (Vector3.up * 0.6f);
            Fly(ghosts, camera, from, Quaternion.LookRotation((at - from).normalized, Vector3.up));
        }

        /// <summary>
        /// Runs the overlay's own fill and draw. Neither <c>Update</c> nor
        /// <c>LateUpdate</c> fires outside play mode, so without this every frame would be
        /// of an overlay that had never been told anything.
        /// </summary>
        private static void Settle(GhostSession ghosts)
        {
            Physics.SyncTransforms();
            ghosts.DrawOverlay();
            Canvas.ForceUpdateCanvases();
        }

        // ------------------------------------------------------------------
        // Frames.
        // ------------------------------------------------------------------

        /// <summary>
        /// The frame as §09 gets it, and the same frame with the ghost's stops taken away.
        /// The pair is what makes <see cref="GhostViewGrade.PostExposureEv"/> a
        /// measurement rather than a preference.
        /// </summary>
        private static void Shoot(Camera camera, string root, string tag, string suffix, StringBuilder report, string note)
        {
            // §09's overlay is a ScreenSpaceOverlay canvas, which Camera.Render does not
            // touch — an overlay canvas is composited by the engine after every camera has
            // finished. Photographed without this, every frame here is of the building
            // with no ghost HUD on it at all, which is the half of the pass that most
            // needs looking at.
            var restore = new List<CanvasState>();
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas)
                {
                    continue;
                }

                restore.Add(new CanvasState(canvas));
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane * 1.1f;
            }

            Canvas.ForceUpdateCanvases();

            try
            {
                RenderTo(camera, Path.Combine(root, tag + "_" + suffix + "_asplayed.png"));

                var grade = GhostViewGrade.Current;
                if (grade != null)
                {
                    grade.Lower();
                    RenderTo(camera, Path.Combine(root, tag + "_" + suffix + "_nograde.png"));
                    grade.RaiseAgain();
                }
            }
            finally
            {
                for (var i = 0; i < restore.Count; i++)
                {
                    restore[i].Restore();
                }

                Canvas.ForceUpdateCanvases();
            }

            report.AppendLine("  " + suffix.PadRight(24) + note);
        }

        /// <summary>How a canvas was configured before the shot, so it can be put back.</summary>
        private readonly struct CanvasState
        {
            private readonly Canvas _canvas;
            private readonly RenderMode _mode;
            private readonly Camera? _camera;
            private readonly float _plane;

            internal CanvasState(Canvas canvas)
            {
                _canvas = canvas;
                _mode = canvas.renderMode;
                _camera = canvas.worldCamera;
                _plane = canvas.planeDistance;
            }

            internal void Restore()
            {
                _canvas.renderMode = _mode;
                _canvas.worldCamera = _camera;
                _canvas.planeDistance = _plane;
            }
        }

        /// <summary>
        /// Writes one frame per candidate exposure, so the constant can be chosen against
        /// ART.md's legible band instead of by eye.
        /// </summary>
        private static string SweepExposure(Camera camera, string root, string tag)
        {
            var line = new StringBuilder("  exposure sweep: ");
            var grade = GhostViewGrade.Current;
            if (grade == null)
            {
                return line.Append("no grade in the scene").ToString();
            }

            foreach (var ev in SweepStops)
            {
                grade.Lower();
                var holder = new GameObject("[GhostSweep]");
                try
                {
                    var volume = holder.AddComponent<UnityEngine.Rendering.Volume>();
                    volume.isGlobal = true;
                    volume.priority = 300f;

                    var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                    volume.sharedProfile = profile;

                    var colour = profile.Add<ColorAdjustments>();
                    colour.active = true;
                    colour.postExposure.overrideState = true;
                    colour.postExposure.value = ev;

                    RenderTo(camera, Path.Combine(
                        root, tag + "_sweep_ev" + ev.ToString("0", CultureInfo.InvariantCulture) + ".png"));
                    UnityEngine.Object.DestroyImmediate(profile);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(holder);
                }

                line.Append(ev.ToString("0", CultureInfo.InvariantCulture)).Append("EV ");
            }

            grade.RaiseAgain();
            return line.ToString();
        }

        // 삭제됨 — NearestThing. It found the closest Interactable so the rig could fly the
        // ghost to something worth shaking. There is nothing to shake; the ghost's verb
        // moves the camera and the camera goes where CutToNextVantage puts it.

        private static Camera? ResolveGhostCamera(PlayerMotor motor)
        {
            // Unparented by GhostSession.Begin, so it is no longer under the rig. Found by
            // asking every camera in the scene which one is enabled — the same one the
            // player is looking through.
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (camera.enabled)
                {
                    return camera;
                }
            }

            return motor.GetComponentInChildren<Camera>();
        }

        private static void PrepareCamera(Camera camera)
        {
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 300f;

            var post = camera.GetComponent<UniversalAdditionalCameraData>();
            if (post == null)
            {
                post = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            post.renderPostProcessing = true;
            post.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            post.antialiasingQuality = AntialiasingQuality.High;
        }

        private static void DisableOtherCameras(Camera keep)
        {
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                camera.enabled = camera == keep;
            }
        }

        private static void Teleport(GameObject body, Vector3 position)
        {
            var controller = body.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            body.transform.position = position;

            if (controller != null)
            {
                controller.enabled = true;
            }

            Physics.SyncTransforms();
        }

        private static void RenderTo(Camera camera, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
            };

            var previousTarget = camera.targetTexture;
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
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static string Fmt(Vector3 v)
        {
            return v.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static string? ArgValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    continue;
                }

                return i + 1 < args.Length ? args[i + 1] : string.Empty;
            }

            return null;
        }

        /// <summary>Fixed steps §06 is given to notice a target in front of it. A budget, not a rule.</summary>
        private const int KillSteps = 240;

        /// <summary>How far in front of the creature the lure stands, metres. Inside §06's cone and well inside its sight range.</summary>
        private const float LureDistanceMetres = 2f;

        /// <summary>
        /// How far the 벽 통과 frame rises, metres. Two of §12's 3.75 m storeys plus the
        /// slab between them, so the frame is unambiguously through geometry rather than
        /// along a stairwell.
        /// </summary>
        private const float StoreysUpMetres = 8f;

        /// <summary>Distance the monster is photographed from, metres. Inside §04's 관측자 range so the creature reads.</summary>
        private const float MonsterStandOffMetres = 8f;

        /// <summary>
        /// How many times the shot rig presses §09's verb.
        /// <para>
        /// Three, because the vantage list on the playtest map is at least three long —
        /// one creature per storey, §02's 도착점, and the ghost's own body — so three
        /// presses photograph three different shots without assuming how many creatures
        /// the map happens to have. A ghost with fewer vantages simply wraps back to free
        /// flight, which is a frame worth having too.
        /// </para>
        /// </summary>
        private const int VantageCuts = 3;

        /// <summary>Candidate stops for <c>-ghostExposureSweep</c>. Read against ART.md's 30–75 % legible band.</summary>
        private static readonly float[] SweepStops = { 0f, 1f, 2f, 3f, 4f, 5f };
    }
}
