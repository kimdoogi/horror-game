#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HorrorGame.Core;
using HorrorGame.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// Builds the scene the player actually launches into: the main menu, standing in a
    /// corridor.
    /// <para>
    /// It is generated rather than hand-built for the same reason the map is — the
    /// backdrop is made of <c>MapKit</c> pieces and the camera runs at
    /// <see cref="GameConstants.FovDefault"/> under
    /// <see cref="NightAtmosphere"/>'s own fog and grade, so re-exporting the kit or
    /// re-tuning the night rebuilds the menu instead of leaving a stale photograph of a
    /// game that no longer looks like that.
    /// </para>
    /// <para>
    /// <b>The backdrop is the point.</b> A menu over a flat colour is the cheapest thing
    /// to build and the most expensive thing to ship: §13 lists the store page as a
    /// deliverable and a store page opens on the menu. What is behind the four words
    /// here is the real renderer — ART.md §3.4's fog solved to half-visibility at 25 m,
    /// §3.5's beam at its real intensity, §3.6's practicals at 5.5 m — drifting slowly
    /// under <c>MenuBackdrop</c>.
    /// </para>
    /// <para>
    /// <b>This assembly does not reference the UI assembly, deliberately.</b> The same
    /// argument the Net layer already gets in this file: a scene generator that imported
    /// the interface would stop building whenever the interface was mid-change, and the
    /// Editor layer is not where a UI decision belongs. So the two shell components are
    /// attached by name, and a missing type is a loud error rather than a scene that
    /// opens with no menu on it.
    /// </para>
    /// </summary>
    public static class BootstrapSceneGenerator
    {
        /// <summary>Empty anchor the Net layer hangs its <c>NetworkManager</c> on.</summary>
        public const string NetBootstrapName = "NetBootstrap";

        /// <summary>The object carrying <c>GameShell</c> — menu, settings, pause, loading.</summary>
        public const string ShellName = "GameShell";

        /// <summary>Root of the corridor the menu is drawn over.</summary>
        public const string BackdropName = "MenuBackdrop";

        /// <summary>
        /// The playable scene 시작 loads. Assembled by <c>SoloPlaytest</c>.
        /// <para>
        /// Aliases <see cref="SceneGenPaths.MatchScene"/> so the build-list writer and
        /// the menu cannot disagree about which scene that is — they did, and the map
        /// generator silently unregistered it.
        /// </para>
        /// </summary>
        public const string MatchScenePath = SceneGenPaths.MatchScene;

        private const string ShellTypeName = "HorrorGame.UI.Shell.GameShell, HorrorGame.UI";
        private const string DriftTypeName = "HorrorGame.UI.Shell.MenuBackdrop, HorrorGame.UI";
        private const string KitFolder = "Assets/Models/MapKit/";

        /// <summary>Eye height, metres. The same figure <c>SceneShot</c> frames the game at.</summary>
        private const float EyeHeight = 1.63f;

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
            try
            {
                var ok = Generate();
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception error)
            {
                Debug.LogError("[SceneGen] " + error);
                EditorApplication.Exit(2);
            }
        }

        /// <summary>Builds the scene and writes it to <see cref="SceneGenPaths.BootstrapScene"/>.</summary>
        public static bool Generate()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneGenPaths.EnsureFolder(SceneGenPaths.SceneRoot);

            BuildBackdrop(out var corridorCentreX, out var corridorEndZ);
            var sightLine = SightLine(corridorCentreX, corridorEndZ);
            BuildCamera(corridorCentreX);
            BuildAtmosphere();

            var bootstrap = new GameObject(NetBootstrapName);
            bootstrap.transform.position = Vector3.zero;

            BuildShell();

            var saved = EditorSceneManager.SaveScene(scene, SceneGenPaths.BootstrapScene);
            if (saved)
            {
                AssetDatabase.SaveAssets();
                RegisterScenes();
            }

            Debug.Log(
                "[SceneGen] Bootstrap: corridor " + corridorEndZ.ToString("0.0") + " m of kit, sight line "
                + sightLine.ToString("0.0") + " m from the camera, centre x " + corridorCentreX.ToString("0.00")
                + ", eye height " + EyeHeight.ToString("0.00") + " m at " + GameConstants.FovDefault.ToString("0")
                + "° (§05), fog half-visibility " + GameConstants.LineOfSightBreakSpacingMax.ToString("0")
                + " m (ART.md §3.4).");

            return saved;
        }

        /// <summary>
        /// Puts the three scenes into Build Settings, bootstrap first.
        /// <para>
        /// <c>MapSceneGenerator.RegisterScenes</c> registers the bootstrap and the raw
        /// map. The raw map is geometry with no player, no monster and no
        /// <c>MatchDirector</c> in it, so 시작 cannot load it and a player who reached it
        /// would stand in an empty building. The playable scene is the solo one, and it
        /// has to be in the list or <c>SceneManager.LoadSceneAsync</c> returns null and
        /// the menu button does nothing.
        /// </para>
        /// </summary>
        public static void RegisterScenes()
        {
            MapSceneGenerator.RegisterScenes();

            if (!File.Exists(MatchScenePath))
            {
                Debug.LogWarning(
                    "[SceneGen] " + MatchScenePath + " does not exist, so 시작 has nothing to load. "
                    + "Run HorrorGame ▸ Play ▸ ▶ START PLAYTEST once to assemble it.");
                return;
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => string.Equals(s.path, MatchScenePath, StringComparison.Ordinal)))
            {
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(MatchScenePath, enabled: true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ====================================================================
        // The corridor.
        // ====================================================================

        /// <summary>
        /// Lays a straight run of kit down the +Z axis: ten metres, a doorway, ten more,
        /// and a dead end.
        /// <para>
        /// <b>Placed by measurement rather than by arithmetic.</b> The kit is authored in
        /// Blender, where Z is up and the footprint's depth is Y, and the importer does
        /// not always bake that conversion — <c>MapSceneBuilder.KitOrientation</c>
        /// documents what a wrong guess costs (every L-corner a quarter-turn out, every
        /// T-junction a half-turn, and most of B-001's island count). Rather than
        /// reproduce that reasoning for a backdrop, each piece is instantiated, its world
        /// bounds are read, and it is then translated so those bounds land where the
        /// composition wants them. Nothing here depends on knowing which way up the kit
        /// arrived.
        /// </para>
        /// <para>
        /// The composition is a §12 corridor and not a diorama: 2.2 m of clear width,
        /// one 병목 partway down, and a 막힌 길 at the end so the frame has a far wall to
        /// haze instead of a hole with the night sky in it.
        /// </para>
        /// </summary>
        private static void BuildBackdrop(out float centreX, out float endZ)
        {
            var root = new GameObject(BackdropName);
            centreX = 0f;
            endZ = 0f;

            var pieces = new[]
            {
                "Corridor_Straight_10m",
                "Doorway_Frame",
                "Corridor_Straight_10m",
                "DeadEnd_Cap",
            };

            var standUp = Quaternion.identity;
            var probed = false;
            var z = 0f;
            var minX = 0f;
            var maxX = 0f;
            var placed = 0;

            foreach (var name in pieces)
            {
                var go = Instantiate(name, root.transform);
                if (go == null)
                {
                    continue;
                }

                if (!probed)
                {
                    standUp = StandUpRotation(go);
                    probed = true;
                }

                // Multiplied onto whatever the import left, never assigned over it. The
                // FBX prefabs currently arrive with a −90° X on the root — already
                // upright — and assigning identity here laid all four of them back down,
                // producing a 13.2 m corridor made of four 3.3 m ceiling-to-ceiling
                // slices instead of a 27.5 m one.
                go.transform.rotation = standUp * go.transform.rotation;

                if (!TryBounds(go, out var bounds))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    continue;
                }

                // Butt the piece's near face against the running Z cursor and sit its
                // floor on y = 0. The offset is the gap between where the object's
                // pivot is and where its geometry actually starts, which is the whole
                // point of measuring rather than assuming.
                go.transform.position += new Vector3(0f, -bounds.min.y, z - bounds.min.z);

                TryBounds(go, out bounds);
                z = bounds.max.z;
                minX = placed == 0 ? bounds.min.x : Mathf.Min(minX, bounds.min.x);
                maxX = placed == 0 ? bounds.max.x : Mathf.Max(maxX, bounds.max.x);
                placed++;
            }

            centreX = (minX + maxX) * 0.5f;
            endZ = z;

            CloseTheFarEnd(root, centreX, endZ);
            BuildPracticals(root, centreX, endZ);
        }

        /// <summary>
        /// Makes sure the corridor ends in a wall.
        /// <para>
        /// The 막힌 길 piece has exactly one dock, so half the time it is laid down open
        /// end first and the menu looks down a tube at the night sky. Rather than reason
        /// about which, the finished corridor is probed with a ray at eye height: if it
        /// escapes, the cap is turned round and the ray is cast again. This is the only
        /// orientation question in the backdrop and it is answered by measurement, like
        /// the rest.
        /// </para>
        /// </summary>
        private static void CloseTheFarEnd(GameObject root, float centreX, float endZ)
        {
            Physics.SyncTransforms();

            var eye = new Vector3(centreX, EyeHeight, 0.5f);
            if (Physics.Raycast(eye, Vector3.forward, endZ + 1f))
            {
                return;
            }

            var cap = root.transform.Cast<Transform>()
                .LastOrDefault(t => t.name.StartsWith("DeadEnd_Cap", StringComparison.Ordinal));

            if (cap == null)
            {
                Debug.LogWarning("[SceneGen] The menu corridor has no far wall and no cap to turn round.");
                return;
            }

            if (!TryBounds(cap.gameObject, out var before))
            {
                return;
            }

            cap.rotation = Quaternion.AngleAxis(180f, Vector3.up) * cap.rotation;
            TryBounds(cap.gameObject, out var after);

            cap.position += new Vector3(
                before.center.x - after.center.x,
                before.min.y - after.min.y,
                before.min.z - after.min.z);

            Physics.SyncTransforms();

            if (!Physics.Raycast(eye, Vector3.forward, endZ + 1f))
            {
                Debug.LogWarning(
                    "[SceneGen] The menu corridor still has no far wall after turning the cap round. "
                    + "The frame will show sky at the end of the corridor.");
            }
        }

        /// <summary>
        /// How far the eye can see down the finished corridor. Reported because it is the
        /// one number that decides whether the menu frame has depth: ART.md §3.4 solves
        /// the fog to half-visibility at <c>LineOfSightBreakSpacingMax</c>, so a wall
        /// nearer than that lands before the haze does any work.
        /// </summary>
        private static float SightLine(float centreX, float endZ)
        {
            Physics.SyncTransforms();
            var eye = new Vector3(centreX, EyeHeight, 0.5f);
            return Physics.Raycast(eye, Vector3.forward, out var hit, endZ + 40f) ? hit.distance : -1f;
        }

        /// <summary>
        /// ART.md §3.6's practicals: 5.5 m range, 1.1 intensity, tinted per §12 zone.
        /// <para>
        /// Three, at a third and four fifths of the run plus one behind the camera.
        /// Depth is ART.md's third target — "a frame must have a near, a middle and a
        /// far" — and the near one is there because the first version had none: the
        /// corridor's clear width is 2.2 m, so at eye height the side walls are barely a
        /// metre away, entirely outside §03's 22° beam cone, and they measured as pure
        /// black. 57 % of the first frame of the game was below 2/255 against ART.md's
        /// 10–40 % band, and the black was all of it the player's own elbows.
        /// </para>
        /// <para>
        /// The near fitting is dimmer than the other two. Its job is to put a falloff on
        /// a wall a metre away, and at full intensity that close it is the brightest
        /// thing in the frame — which is the failure ART.md §3.1 records for the metal
        /// floors, arriving by another route.
        /// </para>
        /// </summary>
        private static void BuildPracticals(GameObject root, float centreX, float endZ)
        {
            AddPractical(root, new Vector3(centreX, 2.9f, -0.9f), new Color(1f, 0.86f, 0.68f), 0.45f);
            AddPractical(root, new Vector3(centreX, 2.9f, endZ * 0.30f), new Color(1f, 0.83f, 0.62f), 1.1f);
            AddPractical(root, new Vector3(centreX, 2.9f, endZ * 0.78f), new Color(0.78f, 0.92f, 0.85f), 1.1f);
        }

        private static void AddPractical(GameObject root, Vector3 position, Color tint, float intensity)
        {
            var go = new GameObject("Practical");
            go.transform.SetParent(root.transform, worldPositionStays: false);
            go.transform.position = position;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 5.5f;
            light.intensity = intensity;
            light.color = tint;
            light.shadows = LightShadows.None;
        }

        // ====================================================================
        // The camera.
        // ====================================================================

        /// <summary>
        /// One perspective camera at §05's own field of view, standing in the corridor.
        /// <para>
        /// Perspective and 80°, not an orthographic placeholder: the whole argument for
        /// putting a corridor behind the menu is that the picture is the game's picture,
        /// and a different lens would make it a picture of something else. It carries
        /// §03's flashlight for the same reason <c>SceneShot</c> does — nobody is ever in
        /// this basement without one.
        /// </para>
        /// </summary>
        private static void BuildCamera(float centreX)
        {
            var go = new GameObject("MenuCamera");
            go.transform.SetPositionAndRotation(
                new Vector3(centreX, EyeHeight, 1.2f), Quaternion.Euler(2.5f, 0f, 0f));

            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = GameConstants.FovDefault;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 300f;

            go.AddComponent<AudioListener>();

            var beam = new GameObject("Flashlight");
            beam.transform.SetParent(go.transform, worldPositionStays: false);
            FlashlightBeam.Apply(beam.AddComponent<Light>());

            AddByName(go, DriftTypeName, "the menu camera will not drift");
        }

        /// <summary>
        /// §07's 초저녁 air and grade, plus the night sky.
        /// <para>
        /// <c>AtmosphereSetup.ApplyEnvironmentToMapScenes</c> only touches scenes named
        /// <c>Map_*</c>, so the bootstrap scene has always been left with Unity's default
        /// daytime skybox and no fog. That is exactly the failure ART.md §2.3 describes —
        /// nothing errors, the sky is simply the brightest thing in the frame — and it
        /// would have been the first thing anybody saw.
        /// </para>
        /// </summary>
        private static void BuildAtmosphere()
        {
            NightAtmosphere.ApplyEnvironment(NightAtmosphere.ForTier(0));

            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/HorrorGame_NightSky.mat");
            if (sky != null)
            {
                RenderSettings.skybox = sky;
            }

            new GameObject("[Atmosphere]").AddComponent<ThreatAtmosphereDirector>();
        }

        private static void BuildShell()
        {
            var go = new GameObject(ShellName);
            AddByName(go, ShellTypeName, "the menu will not come up and 시작 will do nothing");
        }

        // ====================================================================
        // Plumbing.
        // ====================================================================

        private static GameObject? Instantiate(string pieceName, Transform parent)
        {
            var path = KitFolder + pieceName + ".fbx";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogError("[SceneGen] MapKit piece missing: " + path + ". The menu backdrop needs it.");
                return null;
            }

            var go = PrefabUtility.InstantiatePrefab(asset, parent) as GameObject;
            if (go != null)
            {
                go.name = pieceName;
            }

            return go;
        }

        /// <summary>
        /// Any extra rotation a kit piece needs on top of what the import left it at.
        /// Probed on the 10 m corridor, which is the first piece laid.
        /// <para>
        /// That piece measures 2.5 wide × 10 deep × 3.3 tall, so the two possible import
        /// states are told apart by which of the last two axes is the long one: upright,
        /// the depth is in Z and Z &gt; Y; lying on its back — the state Blender's Z-up
        /// leaves it in when the importer does not bake the conversion — the depth is in
        /// Y and Y &gt; Z. Measured on the instance as instantiated, so it reports what is
        /// actually there rather than what the importer was configured to do.
        /// </para>
        /// <para>
        /// Probed rather than assumed for the reason <c>MapSceneBuilder</c> gives: both
        /// states are possible, and a wrong guess produces a scene that still opens.
        /// </para>
        /// </summary>
        private static Quaternion StandUpRotation(GameObject piece)
        {
            if (!TryBounds(piece, out var bounds))
            {
                return Quaternion.identity;
            }

            return bounds.size.y > bounds.size.z ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
        }

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers.Length == 0)
            {
                bounds = new Bounds(go.transform.position, Vector3.zero);
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        /// <summary>
        /// Attaches a component from the UI assembly by name.
        /// <para>
        /// See the type remarks: this assembly does not reference the interface. The
        /// error is deliberately specific about what stops working, because a scene that
        /// saves successfully with a component silently missing is the exact failure the
        /// rest of this project builds screens in code to avoid.
        /// </para>
        /// </summary>
        private static Component? AddByName(GameObject go, string assemblyQualifiedName, string consequence)
        {
            var type = Type.GetType(assemblyQualifiedName, throwOnError: false);
            if (type == null)
            {
                Debug.LogError(
                    "[SceneGen] '" + assemblyQualifiedName + "' was not found, so " + consequence
                    + ". Either the UI assembly failed to compile or the type was renamed.");
                return null;
            }

            return go.AddComponent(type);
        }

        // ====================================================================
        // Verification.
        // ====================================================================

        /// <summary>
        /// Checks that the saved bootstrap scene can actually bring a menu up.
        /// <code>
        /// Unity -batchmode -quit -nographics -projectPath . \
        ///   -executeMethod HorrorGame.EditorTools.SceneGen.BootstrapSceneGenerator.VerifyBatch
        /// </code>
        /// <para>
        /// <b>What it proves and what it does not.</b> It is a structural check: the
        /// scene has a camera, a beam, fog, a shell component that resolved by name, and
        /// a match scene that is genuinely in Build Settings. It does not press 시작 —
        /// that needs play mode, and the assertion that would matter (does the map load
        /// and does <c>MatchDirector</c> start) is a person or a PlayMode test.
        /// </para>
        /// <para>
        /// It exists because of the one design risk in this file: the shell components
        /// are attached by name, so a rename in the UI assembly compiles cleanly on both
        /// sides and produces a bootstrap scene with no menu in it. That failure is
        /// invisible until somebody launches the game.
        /// </para>
        /// </summary>
        public static void VerifyBatch()
        {
            try
            {
                var problems = new List<string>();

                if (!File.Exists(SceneGenPaths.BootstrapScene))
                {
                    Debug.LogError("[SceneGen] " + SceneGenPaths.BootstrapScene + " does not exist. Generate it first.");
                    EditorApplication.Exit(2);
                    return;
                }

                EditorSceneManager.OpenScene(SceneGenPaths.BootstrapScene, OpenSceneMode.Single);

                var shell = GameObject.Find(ShellName);
                if (shell == null || shell.GetComponent(ResolveType(ShellTypeName)!) == null)
                {
                    problems.Add("no GameShell — the menu never comes up and 시작 does nothing");
                }

                var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
                if (camera == null)
                {
                    problems.Add("no camera");
                }
                else
                {
                    if (camera.orthographic)
                    {
                        problems.Add("the menu camera is orthographic, so the backdrop is not the game's own lens");
                    }

                    if (!Mathf.Approximately(camera.fieldOfView, GameConstants.FovDefault))
                    {
                        problems.Add("the menu camera is at " + camera.fieldOfView.ToString("0")
                            + "°, not §05's default " + GameConstants.FovDefault.ToString("0") + "°");
                    }

                    if (camera.GetComponentInChildren<Light>() == null)
                    {
                        problems.Add("the menu camera has no flashlight, so the backdrop is not a frame of this game (§03)");
                    }

                    var drift = ResolveType(DriftTypeName);
                    if (drift == null || camera.GetComponent(drift) == null)
                    {
                        problems.Add("no MenuBackdrop — the first screenshot is a still");
                    }
                }

                if (!RenderSettings.fog)
                {
                    problems.Add("no fog: ART.md §3.4's depth cue is off in the one frame every player sees first");
                }

                if (UnityEngine.Object.FindFirstObjectByType<HorrorGame.Rendering.ThreatAtmosphereDirector>() == null)
                {
                    problems.Add("no atmosphere director, so the menu renders ungraded");
                }

                var registered = EditorBuildSettings.scenes
                    .Any(s => s.enabled && string.Equals(s.path, MatchScenePath, StringComparison.Ordinal));

                if (!registered)
                {
                    problems.Add(MatchScenePath + " is not an enabled scene in Build Settings, so LoadSceneAsync returns "
                        + "null and 시작 bounces back to the menu");
                }

                var corridorPieces = GameObject.Find(BackdropName);
                var renderers = corridorPieces != null ? corridorPieces.GetComponentsInChildren<Renderer>().Length : 0;
                if (renderers == 0)
                {
                    problems.Add("the backdrop has no geometry in it");
                }

                if (problems.Count > 0)
                {
                    Debug.LogError("[SceneGen] Bootstrap scene FAIL:\n  " + string.Join("\n  ", problems));
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log(
                    "[SceneGen] Bootstrap scene PASS — GameShell present, "
                    + renderers + " backdrop renderers, camera at " + GameConstants.FovDefault.ToString("0")
                    + "° with §03's beam, fog on, and " + MatchScenePath + " registered for 시작.");

                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[SceneGen] " + error);
                EditorApplication.Exit(3);
            }
        }

        private static Type? ResolveType(string assemblyQualifiedName)
        {
            return Type.GetType(assemblyQualifiedName, throwOnError: false);
        }

        // ====================================================================
        // Review shots — see ART.md §2.4. Run WITHOUT -nographics.
        // ====================================================================

        /// <summary>
        /// Renders the menu, settings and pause screens to <c>Shots/</c>.
        /// <code>
        /// Unity -batchmode -quit -silent-crashes -projectPath . \
        ///   -executeMethod HorrorGame.EditorTools.SceneGen.BootstrapSceneGenerator.ShotBatch -shotTag menu
        /// </code>
        /// <para>
        /// The screens build their own hierarchy in code, so a shot needs no play mode:
        /// the component is added, <c>SetVisible(true)</c> builds it, and the canvas is
        /// re-pointed at the shot camera. That last step is the one compromise —
        /// a screen-space-overlay canvas draws straight to the back buffer and cannot be
        /// captured through a <c>RenderTexture</c>, so the canvases are switched to
        /// screen-space-camera for the render. It changes one thing about the picture and
        /// the log says so: the interface goes through the grade with the world instead
        /// of being composited after it, so these shots show the UI slightly darker and
        /// less saturated than the game will.
        /// </para>
        /// </summary>
        public static void ShotBatch()
        {
            try
            {
                var tag = ArgValue("-shotTag") ?? "menu";

                if (!File.Exists(SceneGenPaths.BootstrapScene))
                {
                    Generate();
                }

                var scene = EditorSceneManager.OpenScene(SceneGenPaths.BootstrapScene, OpenSceneMode.Single);
                var written = CaptureScreens(scene, tag);

                Debug.Log("[SceneGen] menu shots (UI is graded with the world in these — see ShotBatch):\n  "
                    + string.Join("\n  ", written));
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[SceneGen] " + error);
                EditorApplication.Exit(1);
            }
        }

        private static List<string> CaptureScreens(Scene scene, string tag)
        {
            const int Width = 1920;
            const int Height = 1080;

            var written = new List<string>();
            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Shots");
            System.IO.Directory.CreateDirectory(root);

            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                Debug.LogError("[SceneGen] The bootstrap scene has no camera to render from.");
                return written;
            }

            var screens = new[]
            {
                new KeyValuePair<string, string>("main", "HorrorGame.UI.Screens.MainMenuScreen, HorrorGame.UI"),
                new KeyValuePair<string, string>("settings", "HorrorGame.UI.Screens.SettingsScreen, HorrorGame.UI"),
                new KeyValuePair<string, string>("pause", "HorrorGame.UI.Screens.PauseScreen, HorrorGame.UI"),
                new KeyValuePair<string, string>("loading", "HorrorGame.UI.Screens.LoadingScreen, HorrorGame.UI"),
            };

            written.Add(RenderTo(camera, Path.Combine(root, tag + "_backdrop.png"), Width, Height));

            foreach (var screen in screens)
            {
                var host = new GameObject("[Shot] " + screen.Key);
                try
                {
                    var component = AddByName(host, screen.Value, "that screen cannot be photographed");
                    if (component == null)
                    {
                        continue;
                    }

                    var setVisible = component.GetType().GetMethod("SetVisible");
                    setVisible?.Invoke(component, new object[] { true });

                    foreach (var canvas in host.GetComponentsInChildren<Canvas>(includeInactive: true))
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = camera;
                        canvas.planeDistance = 0.1f;
                    }

                    Canvas.ForceUpdateCanvases();
                    written.Add(RenderTo(camera, Path.Combine(root, tag + "_" + screen.Key + ".png"), Width, Height));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }

            return written;
        }

        private static string RenderTo(Camera camera, string path, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            try
            {
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }

            return path;
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
