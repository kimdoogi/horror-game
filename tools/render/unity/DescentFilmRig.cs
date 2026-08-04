#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HorrorGame.Core;
using HorrorGame.Gameplay.Player;
using HorrorGame.Gameplay.PlayerEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.EditorTools.Film
{
    /// <summary>
    /// Films the descent as a PNG sequence for <c>ffmpeg</c> — the moving half of what
    /// <c>StoreShotRig</c> does for stills.
    /// <para>
    /// <b>Why this replaces <c>PartyFilmRig</c>.</b> That rig staged four bodies and
    /// painted each one a §04 role colour. §04's roles, and the four-player co-op game
    /// they belonged to, were deleted on 2026-08-02. Its output,
    /// <c>docs/store/party.mp4</c>, is 1280×720 / 24 fps / 3.00 s against Valve's
    /// 1920×1080 / 30 or 60 fps / 5,000+ Kbps — so it fails the format on three axes
    /// while photographing a game that no longer exists.
    /// </para>
    /// <para>
    /// <b>No camera coordinate is written in the shot book, and that is the point.</b>
    /// The previous spec files (<c>trailer_frames.json</c>, <c>store_shots.json</c>) hold
    /// world coordinates probed on seed 1204's five-storey sanatorium. That map is not
    /// generated any more, so every frame in them is unshootable and nothing says so
    /// until a run comes back black — checklist.md records it as B-3. Here a shot names a
    /// <b>scene object</b> — <c>투하구 4남</c>, <c>Gun_B3</c>, <c>PlayerSpawn_12</c> — and
    /// the rig resolves it against the scene it just opened. A regenerated map moves the
    /// camera with it; a renamed or deleted anchor is a named hard error before the first
    /// frame rather than a black one after the last.
    /// </para>
    /// <para>
    /// <b>Everything is edit-mode, and the footage has to be described as what it is.</b>
    /// No play mode, so no <c>MatchDirector</c>, no physics, no AI and no networking: the
    /// geometry, the lighting, the materials, the models and the animation clips are the
    /// shipped ones, and the movement through them is authored here. This rig cannot
    /// produce a second <i>player</i> — only a second <i>body</i>. See docs/store/trailer.md
    /// §4 for which beats that disqualifies.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath . \
    ///   -executeMethod HorrorGame.EditorTools.Film.DescentFilmRig.Film -filmSpec /tmp/film.json
    /// </code>
    /// <para>
    /// Never <c>-nographics</c>: it disables the graphics device and every frame comes out
    /// black.
    /// </para>
    /// </summary>
    public static class DescentFilmRig
    {
        /// <summary>
        /// The runner model the clips are read off.
        /// <para>
        /// <b>This path is load-bearing and was wrong until 2026-08-04.</b> It read
        /// <c>Assets/Models/Characters/Player.fbx</c>, which does not exist — that folder
        /// holds <c>Monster.fbx</c> and the runner lives under <c>Assets/Models/Player/</c>.
        /// <see cref="ClipsOf"/> asks <c>AssetDatabase</c> for the clips at a missing path,
        /// which is not an error: it returns an empty set. Every body then failed its clip
        /// lookup and stood in its bind pose, and the 2026-08-04 14:13 take put two T-posed
        /// runners on B1's start line — the shot the whole cut exists to make.
        /// </para>
        /// <para>
        /// It is verified in <see cref="RequireRunnerClips"/> before a single frame is
        /// rendered, because that is the difference between a two-second failure and a
        /// four-minute one that produces footage nobody can use.
        /// </para>
        /// </summary>
        private const string PlayerModelPath = "Assets/Models/Player/Runner.fbx";

        /// <summary>
        /// Eye height for §05's first-person view, matching <c>StoreShotRig</c> and
        /// <c>SceneShot</c>. A frame taken from a different height is not comparable with
        /// the store stills, which is the only reason this is a constant and not a taste.
        /// </summary>
        private const float EyeHeight = 1.63f;

        /// <summary>Unity's built-in UI layer, excluded so developer canvases never reach a frame.</summary>
        private const int UiLayer = 5;

        /// <summary>
        /// Metres a <see cref="ShotSpec.standOff"/> keeps between the eye and the wall it
        /// backed into. The near clip plane is well under this; the number that decides it
        /// is <c>ReportClearance</c>'s own 0.35 m inside-geometry sphere, which this has to
        /// clear with room to spare or the clamp lands the camera exactly on the threshold
        /// it exists to avoid.
        /// </summary>
        private const float StandOffMargin = 0.6f;

        /// <summary>
        /// How finely <see cref="StandOffFrom"/> walks in from the requested distance.
        /// A quarter of a metre is a tenth of a §12 cell (2.5 m), so the search cannot step
        /// over a doorway it could have stood in.
        /// </summary>
        private const float StandOffStep = 0.25f;

        /// <summary>
        /// Least room a self-aiming shot may have in front of it. A §12 cell is 2.5 m, so
        /// under 1.5 m the camera is inside the cell it is looking out of and the frame is
        /// the far wall. Only applies where the rig picked the bearing — see
        /// <see cref="ReportClearance"/>.
        /// </summary>
        private const float MinRoomAheadMetres = 1.5f;

        // ------------------------------------------------------------------ spec

        /// <summary>One body in a shot. A body, not a player — see the class remarks.</summary>
        [Serializable]
        public sealed class RunnerSpec
        {
            /// <summary>Anchor it stands on. Same vocabulary as <see cref="ShotSpec.from"/>.</summary>
            public string at = string.Empty;

            /// <summary>Metres from the anchor, world axes.</summary>
            public Vector3 offset;

            /// <summary><c>inward</c>, <c>outward</c>, <c>value</c> or <c>at</c>.</summary>
            public string face = "inward";

            /// <summary>Used when <see cref="face"/> is <c>value</c>.</summary>
            public float yaw;

            /// <summary>Used when <see cref="face"/> is <c>at</c>.</summary>
            public string faceTarget = string.Empty;

            /// <summary>Clip out of Player.fbx: Run, Walk, Idle…</summary>
            public string clip = "Run";

            /// <summary>0~1 into the cycle, so two bodies are not one body copied.</summary>
            public float phase;

            /// <summary>Metres travelled along its own facing over the whole shot.</summary>
            public float travel;

            /// <summary>
            /// Metres down the camera's own bearing, instead of at an anchor.
            /// <para>
            /// Non-zero wins over <see cref="at"/>. This is how a body is put "12 m up the
            /// corridor the camera is looking down" without anybody knowing which corridor
            /// that is: the bearing was measured by raycast a moment earlier, so the
            /// placement survives a re-seeded storey that would strand a literal coordinate
            /// inside a wall.
            /// </para>
            /// </summary>
            public float aheadMetres;

            /// <summary>Metres to the right of that line. Two bodies abreast are not one body.</summary>
            public float lateralMetres;

            /// <summary>§03's beam, lit. A body in a §03 corridor with no beam is a silhouette in black.</summary>
            public bool torch = true;
        }

        /// <summary>§06's creature in a shot.</summary>
        [Serializable]
        public sealed class MonsterSpec
        {
            public string at = string.Empty;
            public Vector3 offset;
            public string face = "outward";
            public float yaw;
            public string faceTarget = string.Empty;
            public string clip = "Chase";

            /// <summary>Metres travelled along its own facing over the shot.</summary>
            public float travel;

            /// <summary>Metres down the camera's bearing. See <see cref="RunnerSpec.aheadMetres"/>.</summary>
            public float aheadMetres;

            /// <summary>Metres right of that line.</summary>
            public float lateralMetres;

            /// <summary>§06's acquisition tell, the instant the chase starts.</summary>
            public bool tell;
        }

        /// <summary>One continuous camera move. Cuts happen between shots, never inside one.</summary>
        [Serializable]
        public sealed class ShotSpec
        {
            public string name = "shot";

            /// <summary>Length on screen. Frames are <c>seconds × fps</c>.</summary>
            public float seconds = 2f;

            /// <summary>Anchor the camera starts on — a scene object name or a keyword.</summary>
            public string from = string.Empty;
            public Vector3 fromOffset;

            /// <summary>
            /// Metres to back away from <see cref="from"/> before shooting, along the
            /// clearest bearing measured <em>at the anchor</em>.
            /// <para>
            /// <b>This exists because a world-axis offset cannot frame a dead end.</b> The
            /// gun sits at the end of a 막힌 길 whose direction is a property of the seed;
            /// the first cut asked for the camera 3.5 m along −Z and the 2026-08-04 14:14
            /// take put it inside a wall (the rig said <c>INSIDE GEOMETRY</c> and shot the
            /// 72 frames anyway). Backing off along the corridor that actually leaves the
            /// cell is the same instruction — "stand in the corridor and look at the thing"
            /// — written so it survives a re-seed.
            /// </para>
            /// <para>
            /// Pair it with <c>"aim": "at"</c> on the same anchor, or the camera backs away
            /// and then looks somewhere else.
            /// </para>
            /// </summary>
            public float standOff;

            /// <summary>Anchor it ends on. Empty means it holds on <see cref="from"/>.</summary>
            public string to = string.Empty;
            public Vector3 toOffset;

            /// <summary>
            /// The eye is meant to start inside collision — set only on the 투하구 fall,
            /// which begins in the mouth of the hole. Everywhere else this is the signature
            /// of a framing that will render a wall, so the rig treats it as fatal.
            /// </summary>
            public bool insideGeometryExpected;

            /// <summary>
            /// Metres travelled down the camera's own bearing over the shot, when no
            /// <see cref="to"/> anchor is given.
            /// <para>
            /// A dolly in a maze cannot be written as a destination coordinate — which
            /// corridor leaves this cell depends on the seed. Moving along the bearing the
            /// rig measured by raycast is the same instruction expressed in a way that
            /// re-derives itself, and it is why this shot book has no coordinates in it.
            /// </para>
            /// </summary>
            public float dolly;

            /// <summary>Metres above the anchor's own floor. Eye height unless a shot says otherwise.</summary>
            public float eye = EyeHeight;

            /// <summary>
            /// <c>inward</c> (toward this storey's middle), <c>outward</c>, <c>at</c>,
            /// <c>yaw</c>, or <c>clearest</c> (the bearing with the most room in front of
            /// it, measured by raycast — the only mode that survives a re-seeded maze).
            /// </summary>
            public string aim = "clearest";

            public string aimTarget = string.Empty;
            public float aimYawFrom;
            public float aimYawTo;

            /// <summary>Degrees below the horizon at the first and last frame.</summary>
            public float pitchFrom;
            public float pitchTo;

            /// <summary>0 means §05's own <c>GameConstants.FovDefault</c>, which is what a player sees.</summary>
            public float fov;

            /// <summary>
            /// Ease the camera down the real chute curve instead of lerping.
            /// <para>
            /// <c>Chute.DropHeightMetres</c> is <c>0.5 × JumpGravity × FallSeconds²</c> =
            /// <b>1.226 m</b> over <b>0.5 s</b>, left to the controller's own gravity. A
            /// linear drop reads as an elevator; <c>t²</c> is what the player feels.
            /// </para>
            /// </summary>
            public bool fall;

            public bool flashlight = true;

            /// <summary>Survey light instead of the torch. Not a game frame — used for the tower shot only.</summary>
            public bool survey;
            public float surveyIntensity = 1.4f;
            public float surveyPitch = 72f;
            public float surveyYaw = 30f;

            public List<RunnerSpec> runners = new List<RunnerSpec>();

            /// <summary>Empty <see cref="MonsterSpec.at"/> means no creature in this shot.</summary>
            public MonsterSpec monster = new MonsterSpec();
        }

        /// <summary>The whole cut, as one JSON document.</summary>
        [Serializable]
        public sealed class FilmBook
        {
            public string scene = "Assets/Scenes/Map_FirstSketch_Solo.unity";
            public string outputDir = string.Empty;
            public int width = 1920;
            public int height = 1080;

            /// <summary>Valve accepts 30/29.97 and 60/59.94 and nothing between them.</summary>
            public int fps = 30;

            public List<ShotSpec> shots = new List<ShotSpec>();
        }

        // ------------------------------------------------------------------ entry

        /// <summary>Batch entry point. Writes <c>&lt;shot&gt;/frame_0000.png</c>… and exits 0 or non-zero.</summary>
        public static void Film()
        {
            try
            {
                var specPath = ArgValue("-filmSpec");
                if (specPath == null || !File.Exists(specPath))
                {
                    Debug.LogError("[DescentFilm] -filmSpec names no file: " + (specPath ?? "(unset)"));
                    EditorApplication.Exit(2);
                    return;
                }

                var book = JsonUtility.FromJson<FilmBook>(File.ReadAllText(specPath));
                if (book == null || book.shots.Count == 0)
                {
                    Debug.LogError("[DescentFilm] the shot book is empty.");
                    EditorApplication.Exit(2);
                    return;
                }

                if (!File.Exists(book.scene))
                {
                    Debug.LogError("[DescentFilm] no scene at " + book.scene);
                    EditorApplication.Exit(2);
                    return;
                }

                EditorSceneManager.OpenScene(book.scene, OpenSceneMode.Single);
                Index();

                var written = Shoot(book);
                Debug.Log("[DescentFilm] wrote " + written + " frame(s) over " + book.shots.Count
                          + " shot(s) at " + book.width + "x" + book.height + " " + book.fps + " fps.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[DescentFilm] " + error);
                EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------ the map, as the scene has it

        private static readonly Dictionary<string, Transform> ByName =
            new Dictionary<string, Transform>(StringComparer.Ordinal);

        /// <summary>Floor Y of each storey, B1 first. Read off the scene, never assumed.</summary>
        private static float[] _storeyY = Array.Empty<float>();

        /// <summary>Middle of each storey, from that storey's own creature seed.</summary>
        private static Vector3[] _storeyCentre = Array.Empty<Vector3>();

        /// <summary>
        /// Reads the building out of the open scene.
        /// <para>
        /// <b>The storey table is derived, not declared.</b> <c>DescentMap</c> puts one
        /// creature at the recorded middle of every storey, so the set of
        /// <c>MonsterSpawn*</c> transforms IS the set of storeys and each one's Y is that
        /// storey's floor. Deriving it here means a building that grows a ninth floor films
        /// nine, and a building that silently loses one fails loudly on the count rather
        /// than filming seven floors and reporting eight — which is the shape of every
        /// defect this repository keeps finding.
        /// </para>
        /// </summary>
        private static void Index()
        {
            ByName.Clear();

            var all = UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                .GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(includeInactive: true))
                .ToArray();

            foreach (var t in all)
            {
                // First wins: the generator's names are unique, and a duplicate is worth
                // knowing about rather than silently resolving to the later one.
                if (!ByName.ContainsKey(t.name))
                {
                    ByName.Add(t.name, t);
                }
            }

            var seeds = all
                .Where(t => t.name.StartsWith("MonsterSpawn", StringComparison.Ordinal)
                            && !t.name.Equals("MonsterSpawns", StringComparison.Ordinal))
                .OrderByDescending(t => t.position.y)
                .ToArray();

            _storeyY = seeds.Select(t => t.position.y).ToArray();
            _storeyCentre = seeds.Select(t => t.position).ToArray();

            Debug.Log("[DescentFilm] indexed " + ByName.Count + " transform(s); "
                      + _storeyY.Length + " storey/storeys at y "
                      + string.Join(", ", _storeyY.Select(y => y.ToString("F2", CultureInfo.InvariantCulture))));

            if (_storeyY.Length != DescentMapStoreys)
            {
                // Not fatal — a shot book that never says centre: still films — but this is
                // the one number a reader of the log has to see disagree.
                Debug.LogWarning("[DescentFilm] expected " + DescentMapStoreys + " storeys, found "
                                 + _storeyY.Length + ". Anchors of the form centre:<n> will be wrong.");
            }
        }

        /// <summary>§12 gives eight surfaces and no more, so the building is eight storeys.</summary>
        private const int DescentMapStoreys = 8;

        /// <summary>
        /// Turns an anchor into a world point.
        /// <para>
        /// Either a scene object's exact name, or one of the keywords below. Everything
        /// resolves against the scene that is open; nothing is a literal coordinate.
        /// </para>
        /// <list type="bullet">
        /// <item><c>centre:&lt;n&gt;</c> — the middle of storey n (1-based, B1 = 1)</item>
        /// <item><c>chute:&lt;n&gt;:&lt;south|north&gt;</c> — a 투하구 mouth on storey n</item>
        /// <item><c>landing:&lt;n&gt;:&lt;south|north&gt;</c> — where it puts you, on storey n</item>
        /// <item><c>gun:&lt;n&gt;</c>, <c>door:&lt;n&gt;</c>, <c>start:&lt;i&gt;</c></item>
        /// </list>
        /// </summary>
        private static Vector3 Resolve(string anchor)
        {
            if (string.IsNullOrEmpty(anchor))
            {
                throw new InvalidOperationException("[DescentFilm] an empty anchor cannot be resolved.");
            }

            var parts = anchor.Split(':');
            if (parts.Length > 1)
            {
                var n = int.Parse(parts[1], CultureInfo.InvariantCulture);
                switch (parts[0])
                {
                    case "centre":
                        return CentreOf(n);
                    case "chute":
                        return Named("투하구 " + n + (Side(parts) == "north" ? "북" : "남"));
                    case "landing":
                        return Named("투하구 " + n + (Side(parts) == "north" ? "북" : "남") + " 착지");
                    case "gun":
                        return Named("Gun_B" + n);
                    case "door":
                        return Named("Door_(11,7@L" + (n - 1) + ")");
                    case "start":
                        return Named("PlayerSpawn_" + n);
                }
            }

            return Named(anchor);
        }

        private static string Side(string[] parts) => parts.Length > 2 ? parts[2] : "south";

        private static Vector3 CentreOf(int oneBasedStorey)
        {
            var index = oneBasedStorey - 1;
            if (index < 0 || index >= _storeyCentre.Length)
            {
                throw new InvalidOperationException(
                    "[DescentFilm] storey " + oneBasedStorey + " is outside the " + _storeyCentre.Length
                    + " this scene has.");
            }

            return _storeyCentre[index];
        }

        private static Vector3 Named(string name)
        {
            if (ByName.TryGetValue(name, out var t))
            {
                return t.position;
            }

            // Named, not "an anchor". The whole point of resolving against the scene is
            // that the failure says which object went missing.
            throw new InvalidOperationException(
                "[DescentFilm] the scene has no object called '" + name
                + "'. The shot book is describing a building this scene is not.");
        }

        /// <summary>Which storey a Y belongs to, 1-based. Used to aim <c>inward</c>.</summary>
        private static int StoreyAt(float y)
        {
            var best = 1;
            var bestGap = float.MaxValue;
            for (var i = 0; i < _storeyY.Length; i++)
            {
                var gap = Mathf.Abs(_storeyY[i] - y);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = i + 1;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ shooting

        private static int Shoot(FilmBook book)
        {
            Directory.CreateDirectory(book.outputDir);

            // §14's guidance overlays are developer instrumentation and Valve rejects
            // footage carrying non-game text. Same removal StoreShotRig performs.
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour != null && behaviour.GetType().Name == "PlaytestGuidanceScreen")
                {
                    behaviour.gameObject.SetActive(false);
                }
            }

            HideScenePlayer();

            var rig = new GameObject("[DescentFilm Camera]");
            var surveyRig = new GameObject("[DescentFilm Survey]");
            var bodies = new List<GameObject>();
            GameObject? monster = null;
            var written = 0;

            try
            {
                var camera = rig.AddComponent<Camera>();
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 400f;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.cullingMask = ~(1 << UiLayer);

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

                var playerClips = ClipsOf(PlayerModelPath);
                RequireRunnerClips(book, playerClips);

                foreach (var shot in book.shots)
                {
                    // JsonUtility leaves an absent object field alone, but a shot book
                    // written by hand can also say "monster": null outright.
                    shot.monster = shot.monster ?? new MonsterSpec();
                    shot.runners = shot.runners ?? new List<RunnerSpec>();

                    var frames = Mathf.Max(1, Mathf.RoundToInt(shot.seconds * book.fps));
                    var folder = Path.Combine(book.outputDir, shot.name);
                    Directory.CreateDirectory(folder);

                    var anchor = Resolve(shot.from) + shot.fromOffset + new Vector3(0f, shot.eye, 0f);

                    var start = shot.standOff > 0f ? StandOffFrom(anchor, shot.standOff) : anchor;

                    camera.fieldOfView = shot.fov > 0f ? shot.fov : GameConstants.FovDefault;
                    beam.enabled = shot.flashlight && !shot.survey;
                    survey.enabled = shot.survey;
                    survey.intensity = shot.surveyIntensity;
                    surveyRig.transform.rotation = Quaternion.Euler(shot.surveyPitch, shot.surveyYaw, 0f);

                    // The bearing is measured from where the camera STARTS, and the dolly
                    // then runs along it. Measuring it again at the end would let a camera
                    // that has dollied into a doorway swing to a different corridor
                    // mid-shot, which reads as the lens being yanked.
                    var yawFrom = YawFor(shot, start, shot.aimYawFrom);
                    var forward = Quaternion.Euler(0f, yawFrom, 0f) * Vector3.forward;

                    var end = !string.IsNullOrEmpty(shot.to)
                        ? Resolve(shot.to) + shot.toOffset + new Vector3(0f, shot.eye, 0f)
                        : start + (forward * shot.dolly);

                    // A clearest-bearing shot must never re-measure at the far end: the
                    // dolly lands in a different cell, the raycast sweep finds a different
                    // corridor, and the lens appears to be yanked mid-take.
                    var yawTo = shot.aim == "yaw" ? shot.aimYawTo
                        : shot.aim == "clearest" ? yawFrom
                        : YawFor(shot, end, shot.aimYawTo);

                    // What the camera can actually see, before a frame is spent on it. A
                    // camera inside a wall renders a black rectangle and the log is silent
                    // about it, which is how the previous trailer folder filled up with
                    // frames nobody could reproduce.
                    ReportClearance(shot.name, start, yawFrom, shot.insideGeometryExpected,
                                    shot.aim == "at");

                    var cast = StageCast(shot, playerClips, bodies, ref monster, start, forward);

                    for (var f = 0; f < frames; f++)
                    {
                        var t = frames <= 1 ? 0f : f / (float)(frames - 1);
                        var ease = shot.fall ? t * t : t;

                        camera.transform.SetPositionAndRotation(
                            Vector3.Lerp(start, end, ease),
                            Quaternion.Euler(
                                Mathf.Lerp(shot.pitchFrom, shot.pitchTo, t),
                                Mathf.LerpAngle(yawFrom, yawTo, t),
                                0f));

                        PoseCast(cast, t, frames, book.fps, camera);

                        if (f == 0)
                        {
                            // Rendered and thrown away, once per shot. Measured on the store
                            // stills: the first render after a camera or light change comes
                            // back materially different — 16.2 % legible against 25.3 % — because
                            // URP resolves shadow atlases, SSAO history and streamed mips on
                            // the frame that first needs them. A cut whose first frame is that
                            // resolve happening is a visible flash at every edit.
                            RenderPixels(camera, book.width, book.height);
                        }

                        var path = Path.Combine(
                            folder, "frame_" + f.ToString("0000", CultureInfo.InvariantCulture) + ".png");
                        WritePng(path, RenderPixels(camera, book.width, book.height), book.width, book.height);
                        written++;
                    }

                    Debug.Log("[DescentFilm] " + shot.name + ": " + frames + " frame(s), "
                              + shot.seconds.ToString("F2", CultureInfo.InvariantCulture) + " s, "
                              + cast.Count + " body/bodies"
                              + (string.IsNullOrEmpty(shot.monster.at) ? string.Empty : ", creature")
                              + ".");
                }

                if (AnimationMode.InAnimationMode())
                {
                    AnimationMode.StopAnimationMode();
                }
            }
            finally
            {
                if (AnimationMode.InAnimationMode())
                {
                    AnimationMode.StopAnimationMode();
                }

                foreach (var body in bodies)
                {
                    if (body != null)
                    {
                        UnityEngine.Object.DestroyImmediate(body);
                    }
                }

                if (monster != null)
                {
                    UnityEngine.Object.DestroyImmediate(monster);
                }

                UnityEngine.Object.DestroyImmediate(surveyRig);
                UnityEngine.Object.DestroyImmediate(rig);
            }

            return written;
        }

        /// <summary>One posed thing in a shot: where it starts, where it ends, and what it is playing.</summary>
        private sealed class Cast
        {
            public GameObject Rig = null!;
            public Vector3 Start;
            public Vector3 End;
            public float Yaw;
            public AnimationClip? Clip;
            public float Phase;
            public bool IsMonster;
            public bool Tell;
        }

        private static List<Cast> StageCast(
            ShotSpec shot,
            Dictionary<string, AnimationClip> playerClips,
            List<GameObject> pool,
            ref GameObject? monster,
            Vector3 eye,
            Vector3 forward)
        {
            var cast = new List<Cast>();
            var right = Vector3.Cross(Vector3.up, forward).normalized;

            // Where "12 m up the corridor the camera is looking down" lands. The floor is
            // taken from the camera's own storey rather than from the eye, or every body
            // would stand 1.63 m in the air.
            Vector3 Along(float ahead, float lateral) => new Vector3(
                eye.x + (forward.x * ahead) + (right.x * lateral),
                _storeyY.Length > 0 ? _storeyY[StoreyAt(eye.y) - 1] : eye.y - EyeHeight,
                eye.z + (forward.z * ahead) + (right.z * lateral));

            // Bodies are pooled across shots. Building a humanoid rig is the slowest thing
            // in this file and a shot book has twenty-odd shots in it.
            while (pool.Count < shot.runners.Count)
            {
                var built = PlayerFeelHarnessMenu.BuildRig()
                            ?? throw new FileNotFoundException(PlayerModelPath + " is missing.");
                StripOwnerOnlyParts(built);

                var view = built.GetComponent<PlayerFirstPersonView>();
                if (view != null)
                {
                    // Not the local player: draw the whole body and let PlayerWorldArms put
                    // the arms down. Filming the owner's raised first-person arms on a body
                    // seen from outside is the exact pose that component exists to remove.
                    view.IsOwner = false;
                    view.Apply();
                }

                pool.Add(built);
            }

            for (var i = 0; i < pool.Count; i++)
            {
                pool[i].SetActive(i < shot.runners.Count);
            }

            for (var i = 0; i < shot.runners.Count; i++)
            {
                var spec = shot.runners[i];
                var body = pool[i];
                var at = Mathf.Abs(spec.aheadMetres) > 0.001f || Mathf.Abs(spec.lateralMetres) > 0.001f
                    ? Along(spec.aheadMetres, spec.lateralMetres) + spec.offset
                    : Resolve(spec.at) + spec.offset;
                var yaw = FaceYaw(spec.face, spec.yaw, spec.faceTarget, at, eye);

                LightTheTorch(body, spec.torch);
                playerClips.TryGetValue(spec.clip, out var clip);

                cast.Add(new Cast
                {
                    Rig = body,
                    Start = at,
                    End = at + (Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * spec.travel),
                    Yaw = yaw,
                    Clip = clip,
                    Phase = spec.phase,
                });

                if (clip == null)
                {
                    // RequireRunnerClips throws on this before the first frame, so reaching
                    // here means a clip was named somewhere it does not scan. Still fatal:
                    // a bind-posed body in a trailer is the defect, not the warning.
                    throw new InvalidDataException(
                        "[DescentFilm] " + PlayerModelPath + " has no clip called '"
                        + spec.clip + "'; a body with no clip stands in its bind pose.");
                }
            }

            if (!string.IsNullOrEmpty(shot.monster.at) || Mathf.Abs(shot.monster.aheadMetres) > 0.001f)
            {
                monster = monster != null ? monster : StageMonster();
                monster.SetActive(true);

                var at = Mathf.Abs(shot.monster.aheadMetres) > 0.001f
                         || Mathf.Abs(shot.monster.lateralMetres) > 0.001f
                    ? Along(shot.monster.aheadMetres, shot.monster.lateralMetres) + shot.monster.offset
                    : Resolve(shot.monster.at) + shot.monster.offset;
                var yaw = FaceYaw(shot.monster.face, shot.monster.yaw, shot.monster.faceTarget, at, eye);
                HorrorGame.Gameplay.MonsterEditor.MonsterRig.LoadClips()
                    .TryGetValue(shot.monster.clip, out var clip);

                cast.Add(new Cast
                {
                    Rig = monster,
                    Start = at,
                    End = at + (Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * shot.monster.travel),
                    Yaw = yaw,
                    Clip = clip,
                    IsMonster = true,
                    Tell = shot.monster.tell,
                });
            }
            else if (monster != null)
            {
                monster.SetActive(false);
            }

            return cast;
        }

        private static void PoseCast(List<Cast> cast, float t, int frames, int fps, Camera camera)
        {
            if (cast.Count == 0)
            {
                return;
            }

            // One sampling block for the whole frame, never one per rig. BeginSampling
            // reverts every property recorded since the last block, so a block per body
            // means sampling the second undoes the first and only the last one holds a
            // pose — which shipped once as bodies sliding down a corridor with their legs
            // welded to them.
            BeginFrame();

            foreach (var member in cast)
            {
                member.Rig.transform.SetPositionAndRotation(
                    Vector3.Lerp(member.Start, member.End, t),
                    Quaternion.Euler(0f, member.Yaw, 0f));

                if (member.Clip == null)
                {
                    continue;
                }

                var cycle = member.Clip.length <= 0f ? 1f : member.Clip.length;
                var time = ((t * frames / fps) + (member.Phase * cycle)) % cycle;

                // AnimationMode.SampleAnimationClip, not AnimationClip.SampleAnimation: the
                // latter goes through the legacy path and does nothing useful to the
                // Humanoid rig Player.fbx imports as.
                var animator = member.Rig.GetComponentInChildren<Animator>();
                AnimationMode.SampleAnimationClip(
                    animator != null ? animator.gameObject : member.Rig, member.Clip, time);
            }

            EndFrame();

            foreach (var member in cast)
            {
                if (!member.IsMonster)
                {
                    // LateUpdate never fires in edit mode, so the component that lowers a
                    // non-owner's arms has to be called by hand, after the sample.
                    var arms = member.Rig.GetComponent<PlayerWorldArms>();
                    if (arms != null)
                    {
                        arms.Apply();
                    }

                    continue;
                }

                // §3.8 of ART.md: the rim and the eye lenses are what make the creature
                // readable past the beam, and both are driven per-frame rather than baked.
                var resolve = member.Rig.GetComponent<HorrorGame.Gameplay.Monster.MonsterBeamResolve>();
                if (resolve != null)
                {
                    resolve.Apply(camera);
                }

                var tell = member.Rig.GetComponent<HorrorGame.Gameplay.Monster.MonsterAcquireTell>();
                if (tell != null)
                {
                    tell.ApplyProgress(member.Tell ? 1f : 0f);
                }
            }
        }

        // ------------------------------------------------------------------ aiming

        private static float YawFor(ShotSpec shot, Vector3 eye, float explicitYaw)
        {
            switch (shot.aim)
            {
                case "yaw":
                    return explicitYaw;
                case "at":
                    return Bearing(eye, Resolve(shot.aimTarget));
                case "inward":
                    return Bearing(eye, CentreOf(StoreyAt(eye.y)));
                case "outward":
                    return Bearing(eye, CentreOf(StoreyAt(eye.y))) + 180f;
                default:
                    return ClearestBearing(eye);
            }
        }

        private static float FaceYaw(string mode, float yaw, string target, Vector3 at, Vector3 eye)
        {
            switch (mode)
            {
                case "value":
                    return yaw;
                case "at":
                    return Bearing(at, Resolve(target));
                case "camera":
                    // Facing the lens. §06's creature coming up the corridor at you is the
                    // only framing in which the model's front is the subject.
                    return Bearing(at, eye);
                case "away":
                    return Bearing(eye, at);
                case "outward":
                    return Bearing(at, CentreOf(StoreyAt(at.y))) + 180f;
                default:
                    return Bearing(at, CentreOf(StoreyAt(at.y)));
            }
        }

        private static float Bearing(Vector3 from, Vector3 to)
        {
            var flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
            return flat.sqrMagnitude < 1e-6f ? 0f : Quaternion.LookRotation(flat, Vector3.up).eulerAngles.y;
        }

        /// <summary>
        /// The bearing with the most room in front of it, by raycast.
        /// <para>
        /// §12 caps a straight run at 20 m, so this is the question a framing in a maze is
        /// actually built from, and it is the only aim mode that still points down a
        /// corridor after the storey is re-seeded.
        /// </para>
        /// </summary>
        private static float ClearestBearing(Vector3 eye)
        {
            var best = 0f;
            var bestClear = -1f;

            for (var i = 0; i < 72; i++)
            {
                var degrees = i * 5f;
                var direction = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
                var clear = Physics.Raycast(eye, direction, out var hit, 60f) ? hit.distance : 60f;

                if (clear > bestClear)
                {
                    bestClear = clear;
                    best = degrees;
                }
            }

            return best;
        }

        /// <summary>
        /// What the camera can see, checked before a frame is spent on it.
        /// <para>
        /// <b>An unusable framing now stops the run.</b> It used to warn and shoot anyway,
        /// and the 2026-08-04 14:14 take spent 72 frames on a gun shot the rig had already
        /// called a wall. A warning in a 88,000-line batch log is not a result anybody
        /// reads; a non-zero exit is.
        /// </para>
        /// <para>
        /// <see cref="ShotSpec.insideGeometryExpected"/> is the opt-out, and exactly one
        /// shot legitimately needs it: the 투하구 fall starts in the mouth of the hole, so
        /// the eye really is inside the rim for the first frames.
        /// </para>
        /// </summary>
        /// <summary>
        /// Backs away from a subject as far as it can while still standing in open floor
        /// and still being able to see it.
        /// <para>
        /// <b>Why this samples instead of raycasting once.</b> The first version cast one
        /// ray from the anchor along the clearest bearing and trusted the distance. A door
        /// anchor sits <em>inside</em> the door leaf, so the ray left through the doorway
        /// without hitting anything, reported 4 m of clear air, and put the eye 4 m away
        /// inside the wall beside the corridor — the ray threaded a gap the camera does not
        /// fit through. <c>door:5</c> at 4.5 m survived that and <c>door:3</c> at 4.0 m did
        /// not, which is the kind of difference no constant can be tuned around.
        /// </para>
        /// <para>
        /// So the test is run on the <em>destination</em>, which is what
        /// <see cref="ReportClearance"/> checks too: walk in from the requested distance and
        /// take the furthest point that is (a) not inside collision and (b) has unbroken
        /// line of sight back to the subject — the pair of conditions that make a
        /// stand-off framing a picture of the thing rather than a picture of a wall.
        /// Returns the anchor itself if nothing qualifies, which then fails the clearance
        /// check by name instead of silently rendering.
        /// </para>
        /// </summary>
        private static Vector3 StandOffFrom(Vector3 anchor, float requested)
        {
            var away = Quaternion.Euler(0f, ClearestBearing(anchor), 0f) * Vector3.forward;

            for (var d = requested; d >= StandOffMargin; d -= StandOffStep)
            {
                var candidate = anchor + (away * d);

                if (Physics.CheckSphere(candidate, StandOffMargin))
                {
                    continue;
                }

                // Line of sight back to the subject. Without this the camera can stand in a
                // legal cell on the far side of a wall from the thing it is aimed at.
                var toAnchor = anchor - candidate;
                if (Physics.Raycast(candidate, toAnchor.normalized, toAnchor.magnitude - StandOffMargin))
                {
                    continue;
                }

                return candidate;
            }

            return anchor;
        }

        private static void ReportClearance(
            string shot, Vector3 eye, float yaw, bool insideExpected, bool aimedAtSubject)
        {
            var inside = Physics.CheckSphere(eye, 0.35f);
            var direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var ahead = Physics.Raycast(eye, direction, out var hit, 60f) ? hit.distance : 60f;

            var line = "[DescentFilm] " + shot + ": eye " + eye.ToString("F2")
                       + " yaw " + yaw.ToString("F1", CultureInfo.InvariantCulture)
                       + " — " + ahead.ToString("F1", CultureInfo.InvariantCulture) + " m of room ahead"
                       + (inside ? ", INSIDE GEOMETRY" : string.Empty) + ".";

            if (inside && !insideExpected)
            {
                throw new InvalidDataException(
                    line + " This shot would be a wall. Move the anchor, give it a standOff, "
                    + "or set insideGeometryExpected if the eye belongs inside the geometry.");
            }

            // The room-ahead floor asks "is the camera pointed down somewhere it can see?",
            // which is only a question for a shot that chose its own bearing. A shot aimed
            // AT something is supposed to have that thing close in front of it: 21_door
            // frames a door leaf 1.3 m away and that is the shot working, not failing.
            // Applying the floor to those shots rejected a correct framing, which is the
            // failure mode a gate has to avoid more carefully than the one it catches.
            if (!aimedAtSubject && ahead < MinRoomAheadMetres)
            {
                throw new InvalidDataException(
                    line + " Under " + MinRoomAheadMetres.ToString("0.0", CultureInfo.InvariantCulture)
                    + " m of room reads as a wall — give it a bearing with somewhere to go.");
            }

            Debug.Log(line + (inside ? " (inside expected)" : string.Empty));
        }

        // ------------------------------------------------------------------ staging

        private static void HideScenePlayer()
        {
            var motor = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>();
            if (motor != null)
            {
                // It is a body standing in the middle of the shot with its first-person
                // arms across the lens.
                motor.gameObject.SetActive(false);
            }
        }

        private static GameObject StageMonster()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HorrorGame.Gameplay.MonsterEditor.MonsterRig.PrefabPath);

            if (prefab == null)
            {
                throw new FileNotFoundException(
                    HorrorGame.Gameplay.MonsterEditor.MonsterRig.PrefabPath
                    + " is missing. Run MonsterRig.Build first.");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "[DescentFilm Creature]";

            // Nothing calls Start or FixedUpdate in edit mode, so an enabled agent would
            // sit in its bind pose and drift.
            foreach (var agent in instance.GetComponents<HorrorGame.Gameplay.Monster.MonsterAgent>())
            {
                agent.enabled = false;
            }

            return instance;
        }

        /// <summary>
        /// Switches §03's beam on and points it where the body faces.
        /// <para>
        /// <c>BuildRig</c> leaves the torch stowed on purpose — §10 makes switching it on a
        /// decision with a cost. In footage that reasoning inverts: a body in a §03 corridor
        /// with no beam is a black rectangle.
        /// </para>
        /// </summary>
        private static void LightTheTorch(GameObject rig, bool on)
        {
            foreach (var light in rig.GetComponentsInChildren<Light>(true))
            {
                if (!light.gameObject.name.Contains("Flashlight"))
                {
                    continue;
                }

                light.enabled = on;
                light.gameObject.SetActive(on);
                if (!on)
                {
                    continue;
                }

                // Chest height, angled a few degrees down: where a carried torch points, and
                // what puts the pool of light on the floor ahead of the boots.
                light.transform.localPosition = new Vector3(0.16f, 1.28f, 0.22f);
                light.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
            }
        }

        /// <summary>
        /// Every built rig carries a camera and an AudioListener for the owner. Left on,
        /// Unity renders whichever camera it likes and warns once a frame about the
        /// listeners — both are noise in a batch log that has to stay readable.
        /// </summary>
        private static void StripOwnerOnlyParts(GameObject rig)
        {
            foreach (var listener in rig.GetComponentsInChildren<AudioListener>(true))
            {
                UnityEngine.Object.DestroyImmediate(listener);
            }

            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
        }

        /// <summary>
        /// Fails the whole run if the shot book asks for a runner clip the model has not got.
        /// <para>
        /// <b>Why this throws instead of warning.</b> It used to warn, and the body then
        /// rendered in its bind pose — a T-posed, arms-out figure standing on B1's start
        /// line, in the one shot the cut exists to make. The take completed, the frame
        /// count was right, the exit code was zero, and nothing except looking at the PNGs
        /// said anything was wrong. That is the repository's most expensive recurring
        /// defect (a rigged FBX imported with animation switched off, 32 floor tags that
        /// serialised as a dangling reference), so the missing clip is now fatal and it is
        /// fatal <em>before</em> the first frame rather than after the last.
        /// </para>
        /// <para>
        /// It also names the clips the model does have, because the failure this catches is
        /// always a spelling or a path, and the fix is always in the list.
        /// </para>
        /// </summary>
        private static void RequireRunnerClips(FilmBook book, Dictionary<string, AnimationClip> clips)
        {
            var wanted = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var shot in book.shots)
            {
                if (shot.runners == null)
                {
                    continue;
                }

                foreach (var runner in shot.runners)
                {
                    if (!string.IsNullOrEmpty(runner.clip))
                    {
                        wanted.Add(runner.clip);
                    }
                }
            }

            if (wanted.Count == 0)
            {
                return;
            }

            var have = string.Join(", ", new SortedSet<string>(clips.Keys, StringComparer.Ordinal));
            if (clips.Count == 0)
            {
                throw new FileNotFoundException(
                    "[DescentFilm] no AnimationClip at all under " + PlayerModelPath
                    + ". AssetDatabase returns an empty set for a path that does not exist, "
                    + "so check the path before the import settings.");
            }

            var missing = new List<string>();
            foreach (var clip in wanted)
            {
                if (!clips.ContainsKey(clip))
                {
                    missing.Add(clip);
                }
            }

            if (missing.Count > 0)
            {
                throw new InvalidDataException(
                    "[DescentFilm] " + PlayerModelPath + " has no clip called '"
                    + string.Join("', '", missing) + "'. It has: " + have
                    + ". A body with no clip stands in its bind pose, which is not footage.");
            }

            Debug.Log("[DescentFilm] runner clips OK — asked for "
                      + string.Join(", ", wanted) + "; " + PlayerModelPath + " has " + have + ".");
        }

        private static Dictionary<string, AnimationClip> ClipsOf(string modelPath)
        {
            var found = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    // FBX clip names arrive as "Player_Rig|Walk".
                    var bar = clip.name.LastIndexOf('|');
                    found[bar >= 0 ? clip.name.Substring(bar + 1) : clip.name] = clip;
                }
            }

            return found;
        }

        private static void BeginFrame()
        {
            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }

            AnimationMode.BeginSampling();
        }

        private static void EndFrame() => AnimationMode.EndSampling();

        // ------------------------------------------------------------------ rendering

        private static Color[] RenderPixels(Camera camera, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
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
