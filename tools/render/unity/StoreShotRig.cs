#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HorrorGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HorrorGame.EditorTools.Store
{
    /// <summary>
    /// Renders the store page's screenshots: arbitrary camera positions, at 1920×1080,
    /// out of the real scene.
    /// <para>
    /// <b>Why this exists next to <c>SceneShot</c>.</b> <c>SceneShot</c> answers "is the
    /// grade right" and is built for that — 1280×720, viewpoints derived from the scene
    /// so a regenerated map still frames itself. A store screenshot answers a different
    /// question: §13 lists 상점 페이지 자산 as a shipping item and Valve's own asset
    /// guidelines put the floor at 1920×1080, 16:9, gameplay only. Neither the
    /// resolution nor the framing is negotiable, and neither is derivable — "the corridor
    /// and the beam" is a chosen frame, not a computed one.
    /// </para>
    /// <para>
    /// <b>This file is staged, not resident.</b> It lives in <c>tools/render/unity/</c>
    /// and is copied into the project by <c>tools/render/store_shots.py</c> for the
    /// duration of one batch run, then removed. Nothing it does is saved: no scene is
    /// written, and every object it creates is destroyed before the editor exits.
    /// </para>
    /// <para>
    /// Run <b>without</b> <c>-nographics</c>. That flag disables the graphics device and
    /// every frame comes out black — the same trap <c>SceneShot</c> documents.
    /// </para>
    /// </summary>
    public static class StoreShotRig
    {
        /// <summary>Unity's built-in UI layer. The UI pass renders this and nothing else.</summary>
        private const int UiLayer = 5;

        /// <summary>
        /// Camera height for §05's first-person view.
        /// <para>
        /// Not in <c>GameConstants</c> — no game rule reads it. It is a property of the
        /// review rigs, and the same figure is already a private constant in
        /// <c>SceneShot</c> and <c>MonsterShot</c>. A store frame taken from a different
        /// height than the review frames would not be comparable with either.
        /// </para>
        /// </summary>
        private const float EyeHeight = 1.63f;

        // ------------------------------------------------------------------ probe

        /// <summary>
        /// A place in the scene worth pointing a camera at, with the direction that has
        /// the most room in front of it.
        /// <para>
        /// The bearing and clear distance are what make a frame choosable from outside
        /// Unity: §12 caps a straight run at 20 m, so "which of these 61 markers has a
        /// 15 m corridor leading away from it" is the question a screenshot list is
        /// actually built from, and it cannot be answered from the scene file.
        /// </para>
        /// </summary>
        [Serializable]
        public sealed class Landmark
        {
            public string name = string.Empty;
            public string kind = string.Empty;
            public Vector3 position;
            public Vector3 eye;

            /// <summary>Which way the object faces. A §03 clue plate is wall-mounted and has a back.</summary>
            public Vector3 forward;
            public float floorY;
            public float bearing;
            public float clear;
            public float secondBearing;
            public float secondClear;
        }

        /// <summary>Everything <see cref="Probe"/> knows, as one JSON document.</summary>
        [Serializable]
        public sealed class ProbeBook
        {
            public string scene = string.Empty;
            public Vector3 boundsMin;
            public Vector3 boundsMax;
            public List<Landmark> landmarks = new List<Landmark>();
        }

        /// <summary>
        /// Dumps the scene's landmarks to JSON so camera positions can be chosen outside
        /// the editor, then re-run deterministically.
        /// <code>
        /// Unity -batchmode -quit -silent-crashes -projectPath . \
        ///   -executeMethod HorrorGame.EditorTools.Store.StoreShotRig.Probe \
        ///   -storeScene Assets/Scenes/Map_FirstSketch_Solo.unity -storeOut /tmp/probe.json
        /// </code>
        /// </summary>
        public static void Probe()
        {
            try
            {
                var scenePath = ArgValue("-storeScene") ?? "Assets/Scenes/Map_FirstSketch_Solo.unity";
                var outPath = ArgValue("-storeOut") ?? "/tmp/store_probe.json";

                if (!File.Exists(scenePath))
                {
                    Debug.LogError("[StoreShot] No scene at " + scenePath);
                    EditorApplication.Exit(2);
                    return;
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var book = new ProbeBook { scene = scenePath };

                // §03's clues, §08's loot and the vehicle do not exist until a match
                // begins — MatchDirector spawns them under MatchWorld. A screenshot list
                // that wants "a clue being read" therefore cannot be written from the
                // saved scene at all, so the probe starts a match on the same seed the
                // shot book will use and reports what came out.
                var seed = IntArg("-storeBeginMatch");
                if (seed.HasValue)
                {
                    var director = UnityEngine.Object.FindFirstObjectByType<HorrorGame.Gameplay.Match.MatchDirector>();
                    if (director != null && director.BeginMatch(seed.Value))
                    {
                        for (var i = 0; i < 20; i++)
                        {
                            director.StepMatch(GameConstants.FixedStep);
                        }

                        foreach (var interactable in UnityEngine.Object.FindObjectsByType<
                                     HorrorGame.Gameplay.Interaction.Interactable>(
                                     FindObjectsInactive.Include, FindObjectsSortMode.None))
                        {
                            book.landmarks.Add(Measure(
                                interactable.name, interactable.GetType().Name,
                                interactable.transform.position, interactable.transform.forward));
                        }

                        var map = director.Map;
                        if (map != null)
                        {
                            book.landmarks.Add(Measure("MatchMap.Entrance", "entrancePoint", map.Entrance));
                            book.landmarks.Add(Measure("MatchMap.TeamEntryPoint", "entrancePoint", map.TeamEntryPoint));
                        }
                    }
                }

                var bounds = SceneBounds(scene);
                book.boundsMin = bounds.min;
                book.boundsMax = bounds.max;

                var all = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<Transform>(includeInactive: true))
                    .ToArray();

                foreach (var t in all)
                {
                    var kind = KindOf(t.name);
                    if (kind == null)
                    {
                        continue;
                    }

                    book.landmarks.Add(Measure(t.name, kind, t.position, t.forward));
                }

                foreach (var zone in all.Where(t => t.name.StartsWith("Zone_", StringComparison.OrdinalIgnoreCase)))
                {
                    var zb = ObjectBounds(zone.gameObject);
                    book.landmarks.Add(Measure(zone.name, "zone", new Vector3(zb.center.x, zb.min.y, zb.center.z)));
                }

                File.WriteAllText(outPath, JsonUtility.ToJson(book, prettyPrint: true));
                Debug.Log("[StoreShot] probed " + book.landmarks.Count + " landmark(s) → " + outPath);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[StoreShot] " + error);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Which landmarks are worth photographing from. Everything else is noise.</summary>
        private static string? KindOf(string name)
        {
            if (name.StartsWith("PlayerSpawn_", StringComparison.OrdinalIgnoreCase)) return "spawn";
            if (name.StartsWith("CandidateSite_", StringComparison.OrdinalIgnoreCase)) return "site";
            if (name.Equals("MonsterSpawn", StringComparison.OrdinalIgnoreCase)) return "monsterspawn";
            if (name.StartsWith("Stair", StringComparison.OrdinalIgnoreCase)) return "stair";
            if (name.IndexOf("Entrance", StringComparison.OrdinalIgnoreCase) >= 0) return "entrance";
            if (name.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0) return "vehicle";
            if (name.IndexOf("Van", StringComparison.OrdinalIgnoreCase) == 0) return "vehicle";
            return null;
        }

        /// <summary>
        /// Finds a spot a player could stand at this landmark and the two bearings with
        /// the most room in front of them.
        /// <para>
        /// Two rather than one because the best bearing is often a doorway the camera is
        /// standing in; the runner-up is usually the corridor. Both are reported and the
        /// choice is made outside.
        /// </para>
        /// </summary>
        private static Landmark Measure(string name, string kind, Vector3 position, Vector3 forward = default)
        {
            var eye = ClearStandingSpot(position + new Vector3(0f, EyeHeight, 0f));

            var best = (bearing: 0f, clear: -1f);
            var second = (bearing: 0f, clear: -1f);

            for (var i = 0; i < 72; i++)
            {
                var degrees = i * 5f;
                var direction = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
                var clear = Physics.Raycast(eye, direction, out var hit, 60f) ? hit.distance : 60f;

                if (clear > best.clear)
                {
                    second = best;
                    best = (degrees, clear);
                }
                else if (clear > second.clear && Mathf.Abs(Mathf.DeltaAngle(degrees, best.bearing)) > 40f)
                {
                    second = (degrees, clear);
                }
            }

            return new Landmark
            {
                name = name,
                kind = kind,
                position = position,
                eye = eye,
                forward = forward,
                floorY = position.y,
                bearing = best.bearing,
                clear = best.clear,
                secondBearing = second.bearing,
                secondClear = second.clear,
            };
        }

        // ------------------------------------------------------------------ shoot

        /// <summary>One frame: where the camera is, what is switched on, and what is in it.</summary>
        [Serializable]
        public sealed class ShotSpec
        {
            public string name = "shot";
            public Vector3 position;
            public Vector3 euler;

            /// <summary>
            /// Aim at a world point instead of at an Euler angle. §03's clue plates and
            /// §08's vehicle are objects with a known position and no useful bearing, so
            /// "point at this" is the natural way to write those frames down.
            /// </summary>
            public bool useLookAt;
            public Vector3 lookAt;

            /// <summary>0 means §05's default 80°, which is what a player sees.</summary>
            public float fov;

            public bool flashlight = true;

            /// <summary>Survey light instead of the torch. Not a game frame — see the map-scale shot.</summary>
            public bool survey;
            public float surveyIntensity = 1.4f;
            public float surveyPitch = 72f;
            public float surveyYaw = 30f;

            /// <summary><c>none</c>, <c>hud</c> or <c>shop</c>. §08's shop is a real screen, not an overlay.</summary>
            public string ui = "none";

            public bool monster;
            public Vector3 monsterPosition;
            public float monsterYaw;
            public string monsterClip = "Chase";
            public float monsterPose = 0.35f;

            /// <summary>§06's acquisition tell, the instant the chase starts.</summary>
            public bool monsterTell;

            /// <summary>
            /// Teleports §01's player before the frame is taken. Needed because
            /// <c>MatchDirector.OpenShopScreen</c> refuses unless the team is at the
            /// vehicle — the shop is a place, not a key.
            /// </summary>
            public bool movePlayer;
            public Vector3 playerPosition;

            /// <summary>Extra lamp for a frame the map's own fittings do not reach. Off by default.</summary>
            public bool fill;
            public Vector3 fillPosition;
            public float fillRange = 8f;
            public float fillIntensity = 1.1f;
        }

        /// <summary>The whole shot list, as one JSON document.</summary>
        [Serializable]
        public sealed class ShotBook
        {
            public string scene = "Assets/Scenes/Map_FirstSketch_Solo.unity";
            public string outputDir = string.Empty;
            public int width = 1920;
            public int height = 1080;
            public int seed = 1204;
            public List<ShotSpec> shots = new List<ShotSpec>();
        }

        /// <summary>
        /// Renders every frame in the book named by <c>-storeSpec</c>.
        /// <code>
        /// Unity -batchmode -quit -silent-crashes -projectPath . \
        ///   -executeMethod HorrorGame.EditorTools.Store.StoreShotRig.Shoot -storeSpec /tmp/shots.json
        /// </code>
        /// </summary>
        public static void Shoot()
        {
            try
            {
                var specPath = ArgValue("-storeSpec");
                if (specPath == null || !File.Exists(specPath))
                {
                    Debug.LogError("[StoreShot] -storeSpec names no file: " + (specPath ?? "(unset)"));
                    EditorApplication.Exit(2);
                    return;
                }

                var book = JsonUtility.FromJson<ShotBook>(File.ReadAllText(specPath));
                if (book == null || book.shots.Count == 0)
                {
                    Debug.LogError("[StoreShot] the shot book is empty.");
                    EditorApplication.Exit(2);
                    return;
                }

                if (!File.Exists(book.scene))
                {
                    Debug.LogError("[StoreShot] No scene at " + book.scene);
                    EditorApplication.Exit(2);
                    return;
                }

                Directory.CreateDirectory(book.outputDir);
                EditorSceneManager.OpenScene(book.scene, OpenSceneMode.Single);

                var written = Capture(book);
                Debug.Log("[StoreShot] wrote " + written.Count + " frame(s):\n  " + string.Join("\n  ", written));
                EditorApplication.Exit(written.Count == book.shots.Count ? 0 : 3);
            }
            catch (Exception error)
            {
                Debug.LogError("[StoreShot] " + error);
                EditorApplication.Exit(1);
            }
        }

        private static List<string> Capture(ShotBook book)
        {
            var written = new List<string>();

            // §14's guidance overlays are developer instrumentation. Valve rejects
            // screenshots carrying non-game text, and these carry F-006 in red.
            foreach (var guidance in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (guidance != null && guidance.GetType().Name == "PlaytestGuidanceScreen")
                {
                    guidance.gameObject.SetActive(false);
                }
            }

            var rig = new GameObject("[StoreShot Camera]");
            var uiRig = new GameObject("[StoreShot UI Camera]");
            var surveyRig = new GameObject("[StoreShot Survey]");
            var fillRig = new GameObject("[StoreShot Fill]");
            GameObject? monster = null;

            try
            {
                var camera = rig.AddComponent<Camera>();
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 400f;
                camera.clearFlags = CameraClearFlags.Skybox;

                // The store frame is the only place this game is judged before it is
                // bought, so it gets the antialiasing the review rig does not: a 1 px
                // conduit against a dark wall aliases into a dotted line at 1920 wide.
                var post = rig.AddComponent<UniversalAdditionalCameraData>();
                post.renderPostProcessing = true;
                post.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                post.antialiasingQuality = AntialiasingQuality.High;

                var beam = new GameObject("Flashlight").AddComponent<Light>();
                beam.transform.SetParent(rig.transform, false);
                HorrorGame.Rendering.FlashlightBeam.Apply(beam);

                var survey = surveyRig.AddComponent<Light>();
                survey.type = LightType.Directional;
                survey.color = new Color(0.86f, 0.90f, 1f);
                survey.shadows = LightShadows.Soft;
                survey.enabled = false;

                var fill = fillRig.AddComponent<Light>();
                fill.type = LightType.Point;
                fill.color = new Color(1f, 0.86f, 0.66f);
                fill.shadows = LightShadows.Hard;
                fill.enabled = false;

                var uiCamera = uiRig.AddComponent<Camera>();
                uiCamera.clearFlags = CameraClearFlags.SolidColor;
                uiCamera.cullingMask = 1 << UiLayer;
                uiCamera.nearClipPlane = 0.01f;
                uiCamera.farClipPlane = 10f;
                uiCamera.enabled = false;

                var uiPost = uiRig.AddComponent<UniversalAdditionalCameraData>();
                uiPost.renderPostProcessing = false;
                uiPost.antialiasing = AntialiasingMode.None;
                uiPost.renderShadows = false;

                camera.cullingMask = ~(1 << UiLayer);

                var director = PrepareMatch(book);

                foreach (var shot in book.shots)
                {
                    camera.fieldOfView = shot.fov > 0f ? shot.fov : GameConstants.FovDefault;
                    var aim = shot.useLookAt
                        ? Quaternion.LookRotation((shot.lookAt - shot.position).normalized, Vector3.up)
                        : Quaternion.Euler(shot.euler);
                    rig.transform.SetPositionAndRotation(shot.position, aim);

                    beam.enabled = shot.flashlight && !shot.survey;
                    survey.enabled = shot.survey;
                    survey.intensity = shot.surveyIntensity;
                    surveyRig.transform.rotation = Quaternion.Euler(shot.surveyPitch, shot.surveyYaw, 0f);

                    fill.enabled = shot.fill;
                    fill.range = shot.fillRange;
                    fill.intensity = shot.fillIntensity;
                    fillRig.transform.position = shot.fillPosition;

                    if (shot.monster)
                    {
                        monster = monster != null ? monster : StageMonster();
                        PoseMonster(monster, camera, shot);
                    }
                    else if (monster != null)
                    {
                        monster.SetActive(false);
                    }

                    if (shot.movePlayer)
                    {
                        MovePlayer(shot.playerPosition);
                    }

                    ApplyUiState(director, shot.ui);

                    var path = Path.Combine(book.outputDir, shot.name + ".png");

                    // Rendered twice, and the first one is thrown away.
                    //
                    // Measured, not assumed: the first frame of a batch comes back
                    // materially different from a second render of the same camera at
                    // the same position — 16.2% legible against 25.3% in one pass, and
                    // an untextured, differently-lit corridor in another. URP resolves
                    // shadow atlases, SSAO history and streamed mip levels on the frame
                    // that first needs them, and a one-shot Camera.Render in batch mode
                    // photographs that resolve happening. A store page built on the
                    // first frame would be a page of pictures nobody can reproduce.
                    RenderPixels(camera, book.width, book.height);
                    var world = RenderPixels(camera, book.width, book.height);

                    if (!string.Equals(shot.ui, "none", StringComparison.Ordinal))
                    {
                        world = CompositeUi(uiCamera, world, book.width, book.height);
                    }

                    WritePng(path, world, book.width, book.height);
                    written.Add(path);
                }
            }
            finally
            {
                if (monster != null)
                {
                    UnityEngine.Object.DestroyImmediate(monster);
                }

                UnityEngine.Object.DestroyImmediate(fillRig);
                UnityEngine.Object.DestroyImmediate(surveyRig);
                UnityEngine.Object.DestroyImmediate(uiRig);
                UnityEngine.Object.DestroyImmediate(rig);
            }

            return written;
        }

        /// <summary>
        /// Starts a match if the scene has one, so §08's shop and the HUD have real
        /// numbers in them rather than zeroes. A store frame showing an empty wallet and
        /// a stopped clock is a screenshot of a menu, not of a game.
        /// </summary>
        private static MonoBehaviour? PrepareMatch(ShotBook book)
        {
            var director = UnityEngine.Object.FindFirstObjectByType<HorrorGame.Gameplay.Match.MatchDirector>();
            if (director == null)
            {
                return null;
            }

            if (!director.BeginMatch(book.seed))
            {
                Debug.LogWarning("[StoreShot] BeginMatch refused; UI frames will be empty.");
                return director;
            }

            // A handful of steps so §07's clock is off zero and §08's wallet has been
            // credited by the arrival sale the solo loop performs.
            for (var i = 0; i < 20; i++)
            {
                director.StepMatch(GameConstants.FixedStep);
            }

            return director;
        }

        /// <summary>
        /// Puts the player rig somewhere, the way <c>GuidanceShot</c> does: the
        /// <c>CharacterController</c> owns the transform and will snap it back unless it
        /// is switched off across the move.
        /// </summary>
        private static void MovePlayer(Vector3 target)
        {
            var motor = UnityEngine.Object.FindFirstObjectByType<HorrorGame.Gameplay.Player.PlayerMotor>();
            if (motor == null)
            {
                return;
            }

            var controller = motor.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            motor.transform.position = target;

            if (controller != null)
            {
                controller.enabled = true;
            }

            Physics.SyncTransforms();
        }

        private static void ApplyUiState(MonoBehaviour? director, string ui)
        {
            var match = director as HorrorGame.Gameplay.Match.MatchDirector;
            if (match == null)
            {
                return;
            }

            if (string.Equals(ui, "shop", StringComparison.Ordinal))
            {
                match.OpenShopScreen();
            }
            else
            {
                match.CloseShopScreen();
            }

            match.StepMatch(GameConstants.FixedStep);
        }

        // ------------------------------------------------------------------ monster

        /// <summary>
        /// Instantiates §06's monster with its brain switched off.
        /// <para>
        /// Nothing calls <c>Start</c> or <c>FixedUpdate</c> in edit mode, so the agent
        /// would sit in its bind pose and drift; the pose is sampled from the clip
        /// instead, exactly as <c>MonsterShot</c> does it.
        /// </para>
        /// </summary>
        private static GameObject StageMonster()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HorrorGame.Gameplay.MonsterEditor.MonsterRig.PrefabPath);

            if (prefab == null)
            {
                throw new FileNotFoundException(
                    HorrorGame.Gameplay.MonsterEditor.MonsterRig.PrefabPath + " is missing. Run MonsterRig.Build first.");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            foreach (var agent in instance.GetComponents<HorrorGame.Gameplay.Monster.MonsterAgent>())
            {
                agent.enabled = false;
            }

            return instance;
        }

        private static void PoseMonster(GameObject monster, Camera camera, ShotSpec shot)
        {
            monster.SetActive(true);
            monster.transform.SetPositionAndRotation(shot.monsterPosition, Quaternion.Euler(0f, shot.monsterYaw, 0f));

            var animator = monster.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                var clips = HorrorGame.Gameplay.MonsterEditor.MonsterRig.LoadClips();
                if (clips.TryGetValue(shot.monsterClip, out var clip) && clip != null)
                {
                    // Curve paths are relative to the Animator's own GameObject, not to
                    // the prefab root; sampling on the root matches nothing and
                    // photographs the bind pose.
                    clip.SampleAnimation(animator.gameObject, clip.length * Mathf.Clamp01(shot.monsterPose));
                }
            }

            // §3.8 of ART.md: the rim and the eye lenses are what make the creature
            // readable past the beam, and both are driven per-frame rather than baked.
            var resolve = monster.GetComponent<HorrorGame.Gameplay.Monster.MonsterBeamResolve>();
            if (resolve != null)
            {
                resolve.Apply(camera);
            }

            var tell = monster.GetComponent<HorrorGame.Gameplay.Monster.MonsterAcquireTell>();
            if (tell != null)
            {
                tell.ApplyProgress(shot.monsterTell ? 1f : 0f);
            }
        }

        // ------------------------------------------------------------------ rendering

        /// <summary>
        /// Renders the game's own screens over the world frame without letting the grade
        /// touch them.
        /// <para>
        /// In a session these canvases are Screen Space Overlay and are composited by the
        /// display, so URP's tonemap, vignette and chromatic aberration never reach a
        /// glyph. Rendering them through the graded camera would put colour fringing on
        /// §08's price list and a dark corner on the clock — a picture of something the
        /// player never sees.
        /// </para>
        /// <para>
        /// The UI is rendered twice, over black and over white, and matted. With
        /// premultiplied compositing <c>C = F + (1−α)B</c>, so the black pass gives
        /// <c>F</c> and the difference between the passes gives <c>(1−α)</c> directly —
        /// <c>out = C₀ + (C₁ − C₀)·world</c>. No alpha channel has to survive the render
        /// target, which is the part that is not reliable across pipelines.
        /// </para>
        /// </summary>
        private static Color[] CompositeUi(Camera uiCamera, Color[] world, int width, int height)
        {
            var restore = new List<(Canvas canvas, RenderMode mode, Camera? worldCamera, float plane)>();
            var layers = new List<(Transform transform, int layer)>();

            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas)
                {
                    continue;
                }

                restore.Add((canvas, canvas.renderMode, canvas.worldCamera, canvas.planeDistance));
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCamera;
                canvas.planeDistance = uiCamera.nearClipPlane * 1.1f;

                // UiFactory does not assign a layer, so every screen in this game is on
                // Default — and a UI camera masked to the UI layer would photograph an
                // empty frame. Moving the hierarchy for the duration of the pass is what
                // makes "render the UI and nothing else" mean anything.
                foreach (var child in canvas.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    layers.Add((child, child.gameObject.layer));
                    child.gameObject.layer = UiLayer;
                }
            }

            Canvas.ForceUpdateCanvases();

            try
            {
                uiCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                var overBlack = RenderPixels(uiCamera, width, height);

                uiCamera.backgroundColor = new Color(1f, 1f, 1f, 1f);
                var overWhite = RenderPixels(uiCamera, width, height);

                var result = new Color[world.Length];
                for (var i = 0; i < world.Length; i++)
                {
                    var b = overBlack[i];
                    var w = overWhite[i];
                    result[i] = new Color(
                        b.r + (w.r - b.r) * world[i].r,
                        b.g + (w.g - b.g) * world[i].g,
                        b.b + (w.b - b.b) * world[i].b,
                        1f);
                }

                return result;
            }
            finally
            {
                foreach (var entry in layers)
                {
                    entry.transform.gameObject.layer = entry.layer;
                }

                foreach (var entry in restore)
                {
                    entry.canvas.renderMode = entry.mode;
                    entry.canvas.worldCamera = entry.worldCamera;
                    entry.canvas.planeDistance = entry.plane;
                }

                Canvas.ForceUpdateCanvases();
            }
        }

        private static Color[] RenderPixels(Camera camera, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
            };

            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            try
            {
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                var texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                var pixels = texture.GetPixels();
                UnityEngine.Object.DestroyImmediate(texture);
                return pixels;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static void WritePng(string path, Color[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Nudges a point out of whatever it is standing inside — the same problem
        /// <c>SceneShot</c> documents: the dressing pass puts hundreds of pieces in this
        /// map and the middle of a room is where the big ones go.
        /// </summary>
        private static Vector3 ClearStandingSpot(Vector3 wanted)
        {
            const float Radius = 0.5f;

            if (!Physics.CheckSphere(wanted, Radius))
            {
                return wanted;
            }

            for (var step = 1; step <= 6; step++)
            {
                var distance = step * 1.25f;
                for (var turn = 0; turn < 12; turn++)
                {
                    var angle = turn * Mathf.PI / 6f;
                    var candidate = wanted + new Vector3(
                        Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

                    if (!Physics.CheckSphere(candidate, Radius))
                    {
                        return candidate;
                    }
                }
            }

            return wanted;
        }

        private static Bounds SceneBounds(Scene scene)
        {
            var renderers = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Renderer>(includeInactive: false))
                .ToArray();

            return Combine(renderers);
        }

        private static Bounds ObjectBounds(GameObject go) =>
            Combine(go.GetComponentsInChildren<Renderer>(includeInactive: false));

        private static Bounds Combine(IReadOnlyList<Renderer> renderers)
        {
            if (renderers.Count == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * 10f);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Count; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static int? IntArg(string flag)
        {
            var raw = ArgValue(flag);
            return raw != null && int.TryParse(raw, out var value) ? value : (int?)null;
        }

        private static string? ArgValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
