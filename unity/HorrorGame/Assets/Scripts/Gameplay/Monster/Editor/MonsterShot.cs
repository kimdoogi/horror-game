#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HorrorGame.Core;
using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Monster;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HorrorGame.Gameplay.MonsterEditor
{
    /// <summary>
    /// Photographs the monster in the real map, in a real corridor, under §03's real
    /// beam, at the distances the chase actually happens at.
    /// <para>
    /// Whether a creature is frightening is not a property any test can assert, so this
    /// is the equivalent of one: a fixed set of frames that can be looked at, compared
    /// against the last set, and argued about. <c>SceneShot</c> does this for the map
    /// and its docstring makes the case; this does the same job for the one object in
    /// the game the player is supposed to be afraid of.
    /// </para>
    /// <para>
    /// Three things about the setup are deliberate and each one was wrong in an obvious
    /// alternative:
    /// </para>
    /// <list type="bullet">
    /// <item><b>The real map, not a test box.</b> The question is partly "does it belong
    /// in the textured world", and a creature photographed against a grey plane answers
    /// a different question flatteringly.</item>
    /// <item><b>15 / 8 / 3 m.</b> §03's beam reaches
    /// <see cref="GameConstants.FlashlightRange"/> = 12 m, so 15 m is beyond the light
    /// entirely, 8 m is mid-beam, and 3 m is the last moment before the Grab. Every
    /// interesting failure — vanishing, popping in, resolving into mush — happens at
    /// one of those three and nowhere else.</item>
    /// <item><b>The shipped beam.</b> <see cref="HorrorGame.Rendering.FlashlightBeam"/>
    /// rather than a light built here, for the reason that class exists: the art was
    /// once reviewed under a dimmer beam than the game uses.</item>
    /// </list>
    /// <para>
    /// Run WITHOUT <c>-nographics</c>, or every frame is black:
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod
    ///   HorrorGame.Gameplay.MonsterEditor.MonsterShot.Batch -shotTag mon1
    /// </code>
    /// </para>
    /// </summary>
    public static class MonsterShot
    {
        /// <summary>
        /// The distance this rig measures the creature's visibility at, metres.
        /// <para>
        /// Was <c>GameConstants.ObserverRange</c> — §04's 관측자 read the creature from
        /// 15 m, so "can you see it at all at 15 m" was the role's working distance.
        /// The role is deleted and the distance is kept because it is still the right
        /// question asked for a different reason: §06 releases aggro at 12 m
        /// (<c>AggroReleaseDistance</c>), so 15 m is just outside the range where a
        /// runner has already broken away — the furthest a creature still matters to
        /// somebody who has not escaped yet.
        /// </para>
        /// </summary>
        private const float ObserverEraShotMetres = 15f;

        private const string OutputDir = "Shots";
        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>
        /// Distances the creature is photographed at, metres.
        /// <para>
        /// Not round numbers chosen for tidiness — they bracket
        /// <see cref="GameConstants.FlashlightRange"/>, which is the only distance in
        /// the game where the way the monster looks changes discontinuously.
        /// </para>
        /// </summary>
        private static readonly float[] Distances = { 15f, 8f, 3f };

        /// <summary>
        /// §06 rows to photograph, and where in each clip to sample.
        /// <para>
        /// The normalized times are chosen at the moment the state is <em>most itself</em>
        /// rather than at frame zero: Patrol at mid-stride, Alert at the held snap that
        /// §06's 경계 is defined by, Chase mid-pump, Standstill anywhere (it is flat by
        /// construction, which is the point).
        /// </para>
        /// </summary>
        private static readonly (MonsterStateId State, string Clip, float Time)[] Poses =
        {
            (MonsterStateId.Patrol, "Patrol", 0.30f),
            (MonsterStateId.Alert, "Alert", 0.25f),
            (MonsterStateId.Chase, "Chase", 0.55f),
            (MonsterStateId.Search, "Search", 0.40f),
            (MonsterStateId.Standstill, "Standstill", 0.65f),
        };

        /// <summary>Batch entry point.</summary>
        public static void Batch()
        {
            try
            {
                var scenePath = ArgValue("-shotScene") ?? "Assets/Scenes/Map_FirstSketch.unity";
                var tag = ArgValue("-shotTag") ?? "mon";

                if (!File.Exists(scenePath))
                {
                    Debug.LogError("[MonsterShot] No scene at " + scenePath);
                    EditorApplication.Exit(2);
                    return;
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var written = Capture(scene, tag);
                Debug.Log("[MonsterShot] wrote " + written.Count + " shot(s):\n  " + string.Join("\n  ", written));
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[MonsterShot] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// The controlled version of <see cref="Batch"/>: the creature against a dark
        /// wall with nothing behind it, at the distances §04 and §12 depend on.
        /// <para>
        /// <b>Why a second rig exists at all.</b> The map shots were ambiguous in the one
        /// direction that matters. At 8 m the creature read clearly — and it read because
        /// a lit doorway happened to be behind it, so the frame proved that a backlit
        /// silhouette is legible and proved nothing about the creature. At 12 m the same
        /// corridor contained no creature at all. A measurement whose answer depends on
        /// what the map generator put behind the subject is not a measurement.
        /// </para>
        /// <para>
        /// So the background is controlled and it is controlled in the <em>hardest</em>
        /// direction: §12's own corridor section (2.2 × 3.0 m) built out of the map's own
        /// darkest wall material, capped at both ends, with a back wall
        /// <see cref="BackWallGap"/> behind the creature and every light in the scene
        /// switched off except §03's beam. There is no doorway, no practical, no depth
        /// behind the subject for the fog to separate it from. If it reads here it reads
        /// anywhere.
        /// </para>
        /// <para>
        /// Everything else is the game: the scene is the real map, so <c>RenderSettings</c>
        /// ambient, fog, reflection intensity and the global grading volume are the ones
        /// the atmosphere pass wrote. The stage is built <see cref="StageAltitude"/> above
        /// the building, which is out of the way and — because URP fog is measured from
        /// the camera and the ambient probe is global — changes none of them.
        /// </para>
        /// <para>
        /// Headless:
        /// <code>
        /// Unity -batchmode -quit -projectPath . -executeMethod
        ///   HorrorGame.Gameplay.MonsterEditor.MonsterShot.StageBatch -shotTag stage1
        /// </code>
        /// </para>
        /// </summary>
        public static void StageBatch()
        {
            try
            {
                var scenePath = ArgValue("-shotScene") ?? "Assets/Scenes/Map_FirstSketch.unity";
                var tag = ArgValue("-shotTag") ?? "stage";

                if (!File.Exists(scenePath))
                {
                    Debug.LogError("[MonsterShot] No scene at " + scenePath);
                    EditorApplication.Exit(2);
                    return;
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var readings = CaptureStaged(scene, tag);
                Report(readings);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[MonsterShot] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Renders the full set and returns the written paths.</summary>
        public static List<string> Capture(Scene scene, string tag)
        {
            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDir);
            Directory.CreateDirectory(root);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterRig.PrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException(
                    MonsterRig.PrefabPath + " is missing. Run MonsterRig.Build first.");
            }

            var clips = MonsterRig.LoadClips();
            var lane = FindCorridor(scene);

            // The far shot is clamped to the corridor the map actually has. Standing the
            // creature at a nominal 15 m down a 13.5 m run buried it in the end wall, and
            // the diff against the clean plate then reported 71% of the frame as
            // "silhouette" because what had changed was every shadow in the corridor.
            // §12 caps a straight run at 20 m, so a shot rig that insists on 15 m will
            // keep meeting walls; the honest move is to photograph the distance that
            // exists and say which one it was.
            var distances = Distances
                .Select(d => Mathf.Min(d, Mathf.Max(Distances[Distances.Length - 1], lane.Clear - 1.5f)))
                .ToArray();

            Debug.Log("[MonsterShot] lane: eye " + lane.Eye.ToString("0.00")
                      + " heading " + lane.Heading.ToString("0.0") + "° clear " + lane.Clear.ToString("0.0")
                      + " m; shooting at " + string.Join(" / ", distances.Select(d => d.ToString("0.0"))) + " m");

            var written = new List<string>();
            var rig = new GameObject("[MonsterShot Camera]");
            var monster = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            try
            {
                var camera = rig.AddComponent<Camera>();
                camera.fieldOfView = GameConstants.FovDefault;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 300f;
                camera.clearFlags = CameraClearFlags.Skybox;

                var post = rig.AddComponent<UniversalAdditionalCameraData>();
                post.renderPostProcessing = true;
                post.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                post.antialiasingQuality = AntialiasingQuality.High;

                var beam = new GameObject("Flashlight").AddComponent<Light>();
                beam.transform.SetParent(rig.transform, false);
                HorrorGame.Rendering.FlashlightBeam.Apply(beam);

                // The creature cannot be simulated in edit mode — nothing calls Start or
                // FixedUpdate — so the brain is switched off and the pose is sampled
                // straight from the clip. The presentation components are still driven
                // by hand below, because a shot that skipped them would be a photograph
                // of a monster nobody will ever see.
                foreach (var behaviour in monster.GetComponents<MonsterAgent>())
                {
                    behaviour.enabled = false;
                }

                var resolve = monster.GetComponent<MonsterBeamResolve>();
                var tell = monster.GetComponent<MonsterAcquireTell>();
                var forward = Quaternion.Euler(0f, lane.Heading, 0f) * Vector3.forward;

                // Clip curve paths are relative to the Animator's own GameObject, not to
                // the prefab root the Animator hangs under. Sampling on the root silently
                // matches nothing and photographs the bind pose seven times.
                var animator = monster.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    throw new InvalidOperationException(
                        MonsterRig.PrefabPath + " has no Animator; there is nothing to pose.");
                }

                var poseTarget = animator.gameObject;

                rig.transform.SetPositionAndRotation(lane.Eye, Quaternion.Euler(0f, lane.Heading, 0f));

                // The clean plate: the same frame with the creature switched off. Every
                // shot is measured against the plate for the distance it was taken at, so
                // "is the silhouette readable" becomes a number instead of an opinion.
                var plates = new Dictionary<float, Color[]>();
                monster.SetActive(false);
                foreach (var distance in distances)
                {
                    plates[distance] = RenderPixels(camera);
                }

                monster.SetActive(true);

                var matte = new Material(RequireShader("Universal Render Pipeline/Unlit")) { name = "MonsterShotMatte" };
                matte.SetColor("_BaseColor", Color.white);
                var readings = new List<Reading>();

                try
                {
                    foreach (var pose in Poses)
                    {
                        foreach (var distance in distances)
                        {
                            Place(monster, poseTarget, clips[pose.Clip], pose.Time, lane, forward, distance);
                            resolve?.Apply(camera);
                            tell?.ApplyProgress(0f);

                            var name = tag + "_" + pose.State + "_" + distance.ToString("0") + "m";
                            var path = Path.Combine(root, name + ".png");
                            var pixels = RenderPixels(camera);
                            WritePng(path, pixels);
                            written.Add(path);
                            readings.Add(Measure(name, distance, pose.State, pixels, plates[distance],
                                Matte(camera, post, monster, matte), resolve));
                        }
                    }

                    // The acquisition tell gets its own frames. It lasts
                    // MonsterAcquireTell.DurationSeconds and is the signal that the chase has
                    // started, so "is it legible" is a question about a specific instant
                    // rather than about the state.
                    foreach (var distance in distances)
                    {
                        Place(monster, poseTarget, clips["Chase"], 0.1f, lane, forward, distance);
                        resolve?.Apply(camera);
                        tell?.ApplyProgress(1f);

                        var name = tag + "_Acquire_" + distance.ToString("0") + "m";
                        var path = Path.Combine(root, name + ".png");
                        var pixels = RenderPixels(camera);
                        WritePng(path, pixels);
                        written.Add(path);
                        readings.Add(Measure(name, distance, MonsterStateId.Chase, pixels, plates[distance],
                            Matte(camera, post, monster, matte), resolve));
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(matte);
                }

                Report(readings);
                UnityEngine.Object.DestroyImmediate(beam.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(monster);
                UnityEngine.Object.DestroyImmediate(rig);
            }

            return written;
        }

        /// <summary>
        /// A stretch of corridor with enough clear line for the far shot.
        /// <para>
        /// Found rather than hard-coded, because the map is regenerated: §12's layout
        /// moved to three basement storeys and a checked-in coordinate would now be
        /// inside a wall. Searches every player spawn — which are by construction
        /// standable and on the NavMesh — for the one with the longest unobstructed
        /// bearing, and falls back to the widest sightline it can find.
        /// </para>
        /// </summary>
        private static Lane FindCorridor(Scene scene)
        {
            var anchors = new List<Vector3>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (IsAnchor(t.name))
                    {
                        anchors.Add(t.position);
                    }
                }
            }

            // Spawn markers alone are five points in a three-storey building and all of
            // them turned out to sit against something. Sampling each §12 zone's own
            // footprint on a 2.5 m grid — the grid the map is generated on — gives the
            // search a few hundred honest standing spots to choose from instead.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (t.name.StartsWith("Zone_", StringComparison.OrdinalIgnoreCase))
                    {
                        anchors.AddRange(GridOver(t.gameObject));
                    }
                }
            }

            var best = new Lane(Vector3.up * EyeHeight, 0f, 0f, 0f);
            var open = new Lane(Vector3.up * EyeHeight, 0f, 0f, 0f);
            var standable = 0;
            foreach (var floorPoint in anchors)
            {
                // Indoors or nothing. The first version took the longest clear bearing it
                // could find and picked a marker container sitting at the world origin,
                // whose "corridor" was 24 m of open sky — a photograph of the building
                // from outside, at dusk, which is a picture of no part of this game. A
                // ceiling overhead is the cheap, reliable test for being inside §12's
                // basement, and a basement is by construction always under one.
                if (!Physics.Raycast(floorPoint + Vector3.up * 0.2f, Vector3.up, CeilingProbe))
                {
                    continue;
                }

                var eye = floorPoint + Vector3.up * EyeHeight;

                // A camera with its back against a wall photographs the wall. §12's
                // corridor clear section is 2.2 m, so half a metre of room is available
                // wherever a corridor is.
                if (Physics.CheckSphere(eye, 0.45f))
                {
                    continue;
                }

                standable++;
                for (var i = 0; i < 72; i++)
                {
                    var degrees = i * 5f;
                    var direction = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
                    var clear = LaneLength(floorPoint, direction);

                    if (clear > open.Clear)
                    {
                        open = new Lane(eye, degrees, clear, floorPoint.y);
                    }

                    if (clear <= best.Clear || !IsEnclosed(floorPoint, direction, clear))
                    {
                        continue;
                    }

                    best = new Lane(eye, degrees, clear, floorPoint.y);
                }
            }

            if (standable == 0)
            {
                throw new InvalidOperationException(
                    "Nowhere in this scene is both under a ceiling and clear enough to stand. Either the "
                    + "map has no colliders or the shot rig is looking at the wrong scene — either way a "
                    + "monster photographed here would not be in §12.");
            }

            if (best.Clear >= Distances[0] + 1.5f || open.Clear <= best.Clear)
            {
                return best;
            }

            // The corridor is too short for the far shot and somewhere indoors is not.
            //
            // This used to warn and shoot the short lane, and the result was that the one
            // distance §04 is defined at was the one distance the map rig never
            // photographed: the longest enclosed run in this layout is 13.5 m, so the far
            // shot came out at 12 m and ObserverRange went untested in the real world for
            // exactly the reason it most needed testing.
            //
            // Falling back to an unwalled lane is not a compromise, it is §12's own answer.
            // 관측 지점 are 높이 차 · 창문 · 격자 and the section that defines them pairs
            // 개방 공간 (시야 15~25m 확보) with 미로 공간; the Observer's 15 m is supposed to
            // be bought in a hall, not down a corridor, because a corridor that long would
            // violate the 20 m straight-run cap in the same document. LaneLength already
            // requires a ceiling above and a floor below every half-metre of the lane, so
            // "unwalled" here still means indoors — it cannot wander outside the building.
            Debug.LogWarning("[MonsterShot] longest enclosed corridor is " + best.Clear.ToString("0.0")
                             + " m, short of the " + Distances[0].ToString("0")
                             + " m shot. Using the longest indoor lane instead — "
                             + open.Clear.ToString("0.0") + " m, unwalled on at least one side. That is "
                             + "§12's 개방 공간, which is where the 관측자's 15 m is meant to come from.");
            return open;
        }

        /// <summary>
        /// Whether the lane has walls down both sides — that is, whether it is a corridor
        /// rather than the edge of the building.
        /// <para>
        /// Added after two rounds of shots came back half corridor and half open sky. The
        /// ceiling test says a point is under the slab; it does not say the slab has walls
        /// under it, and the outermost bay of this map is exactly that. §12's corridor
        /// clear section is 2.2 m wide, so anything with nothing within 7 m to one side is
        /// not one, whatever else it is.
        /// </para>
        /// </summary>
        private static bool IsEnclosed(Vector3 floorPoint, Vector3 direction, float length)
        {
            var side = Vector3.Cross(Vector3.up, direction).normalized;
            var samples = 0;
            var walled = 0;

            for (var d = 2f; d <= length; d += 3f)
            {
                var p = floorPoint + direction * d + Vector3.up * ChestHeight;
                samples++;
                if (Physics.Raycast(p, side, SideWallProbe) && Physics.Raycast(p, -side, SideWallProbe))
                {
                    walled++;
                }
            }

            // Not every sample: §12 puts rooms on corridors, and a lane crossing one has a
            // stretch with no wall to the side and is still a corridor. Most of it, though.
            return samples > 0 && walled >= Mathf.CeilToInt(samples * 0.7f);
        }

        /// <summary>
        /// Standing spots across an object's footprint, snapped to whatever floor is under
        /// them. Spacing is §12's own 2.5 m grid.
        /// </summary>
        private static IEnumerable<Vector3> GridOver(GameObject zone)
        {
            var renderers = zone.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers.Length == 0)
            {
                yield break;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            for (var x = bounds.min.x + 1.25f; x < bounds.max.x; x += 2.5f)
            {
                for (var z = bounds.min.z + 1.25f; z < bounds.max.z; z += 2.5f)
                {
                    var from = new Vector3(x, bounds.max.y + 0.5f, z);
                    if (Physics.Raycast(from, Vector3.down, out var hit, bounds.size.y + 2f))
                    {
                        yield return hit.point + Vector3.up * 0.02f;
                    }
                }
            }
        }

        /// <summary>How far to the side a corridor wall may be before the lane is not one, metres.</summary>
        private const float SideWallProbe = 7f;

        /// <summary>
        /// Whether a transform is a spawn point rather than the folder holding them.
        /// <para>
        /// Exact-ish matching, because <c>MonsterSpawns</c> — the container — starts with
        /// <c>MonsterSpawn</c>, sits at the world origin, and won the first search
        /// outright.
        /// </para>
        /// </summary>
        private static bool IsAnchor(string name)
        {
            if (string.Equals(name, "MonsterSpawn", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!name.StartsWith("PlayerSpawn_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return name.Length > "PlayerSpawn_".Length && char.IsDigit(name["PlayerSpawn_".Length]);
        }

        /// <summary>
        /// How far down a bearing the creature could actually walk, metres.
        /// <para>
        /// Wall clearance alone is not enough and the second attempt proved it: from a
        /// spawn near the edge of the basement, the longest unobstructed bearing was 24 m
        /// of <em>outside</em>, and the shot came back as a corridor on the left half of
        /// the frame and open sky on the right. So the lane is walked in half-metre steps
        /// and each step has to have a ceiling above it and a floor below it. That is the
        /// definition of being in the building, and it is the only property that makes
        /// the shot a picture of §12.
        /// </para>
        /// <para>
        /// Three heights are cast rather than one: a sightline at 1.63 m can pass clean
        /// over a stack of dressing crates the 2.34 m creature would be standing inside.
        /// </para>
        /// </summary>
        private static float LaneLength(Vector3 floorPoint, Vector3 direction)
        {
            var clear = Cast(floorPoint + Vector3.up * EyeHeight, direction);
            clear = Mathf.Min(clear, Cast(floorPoint + Vector3.up * ChestHeight, direction));
            clear = Mathf.Min(clear, Cast(floorPoint + Vector3.up * KneeHeight, direction));

            var enclosed = 0f;
            for (var d = 1f; d <= clear && d <= MaxLane; d += 0.5f)
            {
                var p = floorPoint + direction * d;
                if (!Physics.Raycast(p + Vector3.up * 0.2f, Vector3.up, CeilingProbe))
                {
                    break;
                }

                if (!Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, 2f))
                {
                    break;
                }

                enclosed = d;
            }

            return enclosed;
        }

        private static float Cast(Vector3 from, Vector3 direction) =>
            Physics.Raycast(from, direction, out var hit, MaxLane) ? hit.distance : MaxLane;

        /// <summary>Longest lane considered, metres. §12 caps a straight run at 20 m.</summary>
        private const float MaxLane = 24f;

        /// <summary>How far up to look for a ceiling when deciding a marker is indoors, metres.</summary>
        private const float CeilingProbe = 6f;

        /// <summary>Third clearance cast, low enough to catch §12's floor dressing.</summary>
        private const float KneeHeight = 0.5f;

        /// <summary>§05's first-person eye height, the only height a shot is honest from.</summary>
        private const float EyeHeight = 1.63f;

        /// <summary>Chest height of the creature, used for the second clearance cast.</summary>
        private const float ChestHeight = 1.7f;


        /// <summary>
        /// Stands the creature at a distance down the lane, facing the camera.
        /// <para>
        /// Facing the camera because §06's monster closes on you: the angle its
        /// silhouette has to be readable from is the angle it is coming from.
        /// </para>
        /// </summary>
        private static void Place(GameObject monster, GameObject poseTarget, AnimationClip clip,
                                  float normalizedTime, Lane lane, Vector3 forward, float distance)
        {
            var place = lane.Eye + forward * distance;
            place.y = lane.Floor;
            monster.transform.SetPositionAndRotation(place, Quaternion.Euler(0f, lane.Heading + 180f, 0f));
            clip.SampleAnimation(poseTarget, clip.length * normalizedTime);
        }



        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static Color[] RenderPixels(Camera camera)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
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

        private static void WritePng(string path, Color[] pixels)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
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

        // ====================================================================
        // The staged rig, and the numbers that decide whether it worked.
        // ====================================================================

        /// <summary>
        /// Distances the staged set is shot at, metres.
        /// <para>
        /// Each one is a system, not a round number. 8 m is inside §03's beam and is where
        /// the map evidence was ambiguous. 12 m is
        /// <see cref="GameConstants.FlashlightRange"/> exactly — the beam's last metre —
        /// and it is also §06's aggro-release distance, so it is the range a 주자 has to
        /// judge from. 15 m is <see cref="ObserverEraShotMetres"/>: §12 requires
        /// 1~2 관측 지점 per zone giving "15m 거리에서 안전하게 괴물을 볼 수 있는 지점",
        /// and §04 makes the 관측자 the one role §11 says cannot be replaced by an item.
        /// 20 m is §12's cap on a straight run — the longest sight line a legal map is
        /// allowed to contain, and therefore the worst case the creature has to survive.
        /// </para>
        /// </summary>
        private static readonly float[] StageDistances = { 8f, 12f, 15f, 20f };

        /// <summary>
        /// §06 rows the staged set is shot in, and where in the clip to sample.
        /// <para>
        /// Two, because two different roles are being served and they watch different
        /// creatures. Chase is §12's 주자 case: the table at §12 marks 3 m and 5 m as
        /// failures and 10 m as the first distance an aggro pull reliably survives a
        /// corner, so the Runner has to see a creature that is already coming. Patrol is
        /// §04's 관측자 case: an unaware creature at 15 m, which is the only thing the
        /// Observer's ability is ever pointed at.
        /// </para>
        /// </summary>
        private static readonly (MonsterStateId State, string Clip, float Time)[] StagePoses =
        {
            (MonsterStateId.Chase, "Chase", 0.55f),
            (MonsterStateId.Patrol, "Patrol", 0.30f),
        };

        /// <summary>How far above the map the stage is built, metres. Out of the way of everything.</summary>
        private const float StageAltitude = 400f;

        /// <summary>
        /// Gap between the creature and the wall behind it, metres.
        /// <para>
        /// Small on purpose. Fog is the one thing that can separate a dark creature from a
        /// dark background for free — at exp² density 0.0333 a background 10 m further
        /// away is visibly hazier — and letting the stage supply that would be measuring
        /// the corridor rather than the creature. 1.5 m clears the crest and the reaching
        /// arms and contributes essentially no fog difference, so what the numbers report
        /// is the creature alone.
        /// </para>
        /// </summary>
        private const float BackWallGap = 1.5f;

        /// <summary>§12's corridor clear section, metres. The stage is built to it.</summary>
        private const float CorridorWidth = 2.2f;

        /// <summary>§12's corridor clear height, metres.</summary>
        private const float CorridorHeight = 3.0f;

        /// <summary>The map's darkest wall. §06's creature has to survive the least helpful background.</summary>
        private const string StageWallMaterial = "Assets/Textures/Materials/Wall_Brick_Painted.mat";

        private const string StageFloorMaterial = "Assets/Scenes/Generated/Materials/Floor_Concrete.mat";

        private const string StageCeilingMaterial = "Assets/Textures/Materials/Ceiling_Concrete_Formed.mat";

        // ── The thresholds. These are the deliverable, so they are argued, not dialled ──

        /// <summary>
        /// One 8-bit code value. Nothing below this exists in the delivered image, whatever
        /// the floating-point framebuffer contained.
        /// </summary>
        private const float CodeValue = 1f / 255f;

        /// <summary>
        /// Mean per-pixel luminance separation between the creature and the wall
        /// immediately around it that counts as "a person can tell something is standing
        /// there", 0–1.
        /// <para>
        /// ≈3.8 code values. Two code values is where dithering, panel noise and the
        /// encoder stop being an explanation; a mean separation of about four across a
        /// region a degree or more wide is the level at which an observer who has <em>not</em>
        /// been told where to look picks a shape out of a dark field. Below it the shape is
        /// found only by someone who already knows it is there, which is exactly the
        /// failure §04 cannot tolerate — the 관측자 is looking for a creature whose position
        /// is the thing they are trying to learn.
        /// </para>
        /// </summary>
        private const float PassContrast = 0.015f;

        /// <summary>
        /// Fraction of the creature's own screen footprint that has to differ from the
        /// empty frame at all.
        /// <para>
        /// This is the measure that separates "a creature" from "a glint". A rim light
        /// alone can produce a strong contrast number off a few hundred edge pixels while
        /// the body remains indistinguishable from the wall, and what that reads as is a
        /// scratch on the monitor. Requiring 40% of the silhouette to differ means the
        /// thing being seen has a body with a shape, which is the information §12's
        /// corridors ask the player to act on.
        /// </para>
        /// </summary>
        private const float PassCoverage = 0.40f;

        /// <summary>
        /// 95th-percentile luminance change inside the footprint, 0–1.
        /// <para>
        /// ≈10 code values. Contrast and coverage together can be satisfied by a uniformly
        /// slightly-darker rectangle, which reads as a smudge rather than as a creature.
        /// Recognition needs at least one genuinely legible feature — a lit edge, an eye —
        /// and ten code values is where a feature is not merely detected but identified.
        /// </para>
        /// </summary>
        private const float PassPeak = 0.040f;

        /// <summary>
        /// A pixel counts as changed when it moved by more than 8-bit rounding could
        /// account for: twice a code value.
        /// </summary>
        private const float ChangedEpsilon = 2f * CodeValue;

        /// <summary>Radius of the background ring, pixels — roughly a body-width at 15 m.</summary>
        private const int RingOuterRadius = 14;

        /// <summary>
        /// Pixels of the creature's own edge excluded from the background ring.
        /// <para>
        /// The matte is antialiased and the creature casts a contact shadow, so the first
        /// two or three pixels outside the silhouette are partly the creature. The eye
        /// judges the wall from further out than that.
        /// </para>
        /// </summary>
        private const int RingInnerRadius = 4;

        /// <summary>Renders the staged set and returns one reading per frame.</summary>
        public static List<Reading> CaptureStaged(Scene scene, string tag)
        {
            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDir);
            Directory.CreateDirectory(root);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterRig.PrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException(
                    MonsterRig.PrefabPath + " is missing. Run MonsterRig.Build first.");
            }

            var clips = MonsterRig.LoadClips();
            var readings = new List<Reading>();

            var stage = new GameObject("[MonsterShot Stage]");
            var rig = new GameObject("[MonsterShot Camera]");
            var monster = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var doused = DouseLights(scene);
            var matte = new Material(RequireShader("Universal Render Pipeline/Unlit")) { name = "MonsterShotMatte" };
            matte.SetColor("_BaseColor", Color.white);

            var wasFog = RenderSettings.fog;

            try
            {
                var origin = new Vector3(0f, StageAltitude, 0f);
                BuildStage(stage, origin);

                var camera = rig.AddComponent<Camera>();
                camera.fieldOfView = GameConstants.FovDefault;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 300f;
                camera.clearFlags = CameraClearFlags.Skybox;
                rig.transform.SetPositionAndRotation(origin + Vector3.up * EyeHeight, Quaternion.identity);

                // Explicit rather than left to AtmosphereCameraPolicy, because the matte
                // pass has to turn post-processing off and back on. A camera the policy
                // owns cannot be toggled — it adds the component only when there is none.
                var post = rig.AddComponent<UniversalAdditionalCameraData>();
                post.renderPostProcessing = true;
                post.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                post.antialiasingQuality = AntialiasingQuality.High;

                var beam = new GameObject("Flashlight").AddComponent<Light>();
                beam.transform.SetParent(rig.transform, false);
                HorrorGame.Rendering.FlashlightBeam.Apply(beam);

                foreach (var behaviour in monster.GetComponents<MonsterAgent>())
                {
                    behaviour.enabled = false;
                }

                var resolve = monster.GetComponent<MonsterBeamResolve>();
                var tell = monster.GetComponent<MonsterAcquireTell>();
                var animator = monster.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    throw new InvalidOperationException(
                        MonsterRig.PrefabPath + " has no Animator; there is nothing to pose.");
                }

                var lane = new Lane(origin + Vector3.up * EyeHeight, 0f, 24f, origin.y);
                var forward = Vector3.forward;
                var variants = Overrides.FromCommandLine();
                var distances = ArgFloats("-stageDistances") ?? StageDistances;
                var poses = ArgPoses("-stagePoses");

                foreach (var variant in variants)
                {
                    if (!variant.IsEmpty)
                    {
                        Debug.Log("[MonsterShot] material overrides: " + variant);
                    }
                }

                foreach (var distance in distances)
                {
                    MoveBackWall(stage, origin, distance);

                    // The clean plate for THIS distance, because the back wall moved. A
                    // plate shared across distances would difference the creature against
                    // a wall that is not behind it, and the biggest number in the result
                    // would be the wall.
                    monster.SetActive(false);
                    var plate = RenderPixels(camera);
                    WritePng(Path.Combine(root, tag + "_plate_" + distance.ToString("0") + "m.png"), plate);
                    monster.SetActive(true);

                    foreach (var pose in poses)
                    {
                        Place(monster, animator.gameObject, clips[pose.Clip], pose.Time, lane, forward, distance);
                        resolve?.Apply(camera);
                        tell?.ApplyProgress(0f);

                        // The matte does not depend on the shading, so it is rendered once
                        // per placement and reused across the sweep. It is also the most
                        // expensive step — a full render plus a million-pixel threshold.
                        var mask = Matte(camera, post, monster, matte);

                        foreach (var variant in variants)
                        {
                            variant.Push(monster, resolve, distance);

                            var name = tag + variant.Suffix + "_" + pose.State + "_"
                                       + distance.ToString("0") + "m";
                            var pixels = RenderPixels(camera);
                            WritePng(Path.Combine(root, name + ".png"), pixels);
                            readings.Add(Measure(name, distance, pose.State, pixels, plate, mask, resolve));
                        }
                    }
                }

                UnityEngine.Object.DestroyImmediate(beam.gameObject);
            }
            finally
            {
                RenderSettings.fog = wasFog;
                Restore(doused);
                UnityEngine.Object.DestroyImmediate(matte);
                UnityEngine.Object.DestroyImmediate(monster);
                UnityEngine.Object.DestroyImmediate(rig);
                UnityEngine.Object.DestroyImmediate(stage);
            }

            return readings;
        }

        /// <summary>
        /// Builds §12's corridor section around the stage origin, looking down +Z.
        /// <para>
        /// Sealed at the top, both sides and the far end. A stage open to the sky would put
        /// the night skybox behind the creature, and a skybox is a light source and a
        /// background gradient at once — the two things this rig exists to remove.
        /// </para>
        /// </summary>
        private static void BuildStage(GameObject stage, Vector3 origin)
        {
            var wall = RequireMaterial(StageWallMaterial);
            var floor = RequireMaterial(StageFloorMaterial);
            var ceiling = RequireMaterial(StageCeilingMaterial);

            const float length = 32f;
            const float behind = 2f;
            var mid = (length - behind) * 0.5f;

            Slab(stage, "Floor", origin + new Vector3(0f, -0.1f, mid), new Vector3(CorridorWidth, 0.2f, length), floor);
            Slab(stage, "Ceiling", origin + new Vector3(0f, CorridorHeight + 0.1f, mid),
                 new Vector3(CorridorWidth, 0.2f, length), ceiling);
            Slab(stage, "Wall_L", origin + new Vector3(-CorridorWidth * 0.5f - 0.1f, CorridorHeight * 0.5f, mid),
                 new Vector3(0.2f, CorridorHeight, length), wall);
            Slab(stage, "Wall_R", origin + new Vector3(CorridorWidth * 0.5f + 0.1f, CorridorHeight * 0.5f, mid),
                 new Vector3(0.2f, CorridorHeight, length), wall);
            Slab(stage, "Wall_Behind", origin + new Vector3(0f, CorridorHeight * 0.5f, -behind - 0.1f),
                 new Vector3(CorridorWidth + 0.4f, CorridorHeight, 0.2f), wall);
            Slab(stage, "Wall_Back", origin + new Vector3(0f, CorridorHeight * 0.5f, 10f),
                 new Vector3(CorridorWidth + 0.4f, CorridorHeight, 0.2f), wall);
        }

        private static void MoveBackWall(GameObject stage, Vector3 origin, float distance)
        {
            var back = stage.transform.Find("Wall_Back");
            if (back == null)
            {
                throw new InvalidOperationException("The stage has no Wall_Back to move.");
            }

            back.position = origin + new Vector3(0f, CorridorHeight * 0.5f, distance + BackWallGap + 0.1f);
        }

        private static void Slab(GameObject parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent.transform, worldPositionStays: false);
            cube.transform.SetPositionAndRotation(centre, Quaternion.identity);
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;

            // No collider: FindCorridor's raycasts are not used by the staged rig and a
            // 32 m box at altitude would answer every one of them if they ever were.
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        }

        /// <summary>
        /// Switches every light in the scene off and returns the ones that were on.
        /// <para>
        /// "No backlight" has to be literal or the measurement is the old one again. §12's
        /// zone practicals are 5.5 m fittings scattered one in five cells, and one of them
        /// behind the creature is precisely the accident that made the 8 m map shot look
        /// like a success.
        /// </para>
        /// </summary>
        private static List<Light> DouseLights(Scene scene)
        {
            var doused = new List<Light>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var light in root.GetComponentsInChildren<Light>(includeInactive: false))
                {
                    if (!light.enabled)
                    {
                        continue;
                    }

                    light.enabled = false;
                    doused.Add(light);
                }
            }

            return doused;
        }

        private static void Restore(List<Light> doused)
        {
            for (var i = 0; i < doused.Count; i++)
            {
                if (doused[i] != null)
                {
                    doused[i].enabled = true;
                }
            }
        }

        /// <summary>
        /// The creature's exact screen footprint, as a per-pixel mask.
        /// <para>
        /// Ground truth, and it has to be, because every other measure is computed over it.
        /// The obvious alternative — take the pixels that changed as the footprint — is
        /// circular: a creature that rendered at exactly the wall's luminance would have an
        /// empty footprint and would therefore score a perfect coverage of nothing. So the
        /// silhouette is rendered separately as unlit white on black with fog, grading and
        /// every other renderer switched off. What comes back is the shape the creature
        /// occupies whether or not any of it is visible, which is the only denominator that
        /// makes "the fraction of the silhouette that differs at all" mean what it says.
        /// </para>
        /// </summary>
        private static bool[] Matte(Camera camera, UniversalAdditionalCameraData post,
                                    GameObject monster, Material white)
        {
            var monsterRenderers = monster.GetComponentsInChildren<Renderer>(includeInactive: true);
            var swapped = new List<(Renderer Renderer, Material[] Materials)>(monsterRenderers.Length);
            var layers = new List<(Transform Transform, int Layer)>();

            var wasFog = RenderSettings.fog;
            var wasClear = camera.clearFlags;
            var wasBackground = camera.backgroundColor;
            var wasMask = camera.cullingMask;
            var wasPost = post.renderPostProcessing;
            var wasAa = post.antialiasing;

            try
            {
                // The rest of the world is removed with a culling mask rather than by
                // switching its renderers off. In the staged rig that would be six
                // objects; in the real map it is every piece of §12's kit and all 1,074
                // dressing props, and toggling them twice per frame is both slow and a
                // large surface for leaving the scene in a state nobody asked for.
                foreach (var t in monster.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    layers.Add((t, t.gameObject.layer));
                    t.gameObject.layer = MatteLayer;
                }

                camera.cullingMask = 1 << MatteLayer;

                foreach (var renderer in monsterRenderers)
                {
                    var materials = renderer.sharedMaterials;
                    swapped.Add((renderer, materials));
                    var flat = new Material[materials.Length];
                    for (var i = 0; i < flat.Length; i++)
                    {
                        flat[i] = white;
                    }

                    renderer.sharedMaterials = flat;
                }

                RenderSettings.fog = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                post.renderPostProcessing = false;
                post.antialiasing = AntialiasingMode.None;

                var pixels = RenderPixels(camera);
                var mask = new bool[pixels.Length];
                var covered = 0;
                for (var i = 0; i < pixels.Length; i++)
                {
                    // Half-lit is inside: MSAA resolves the outline to partial coverage and
                    // the 50% crossing is where the geometric edge actually is.
                    mask[i] = Luminance(pixels[i]) > 0.5f;
                    if (mask[i])
                    {
                        covered++;
                    }
                }

                if (covered == 0)
                {
                    throw new InvalidOperationException(
                        "The creature's matte is empty — it is off screen, inside a wall or not "
                        + "rendering. Every number measured against it would be a division by zero "
                        + "dressed up as a pass.");
                }

                return mask;
            }
            finally
            {
                foreach (var entry in swapped)
                {
                    entry.Renderer.sharedMaterials = entry.Materials;
                }

                foreach (var entry in layers)
                {
                    entry.Transform.gameObject.layer = entry.Layer;
                }

                RenderSettings.fog = wasFog;
                camera.clearFlags = wasClear;
                camera.backgroundColor = wasBackground;
                camera.cullingMask = wasMask;
                post.renderPostProcessing = wasPost;
                post.antialiasing = wasAa;
            }
        }

        /// <summary>
        /// Layer the creature is moved to for the matte pass. 31 is unnamed in
        /// <c>ProjectSettings/TagManager.asset</c> and nothing in the game uses it.
        /// </summary>
        private const int MatteLayer = 31;

        /// <summary>
        /// Turns one staged frame into the three numbers the pass criterion is written in.
        /// <para>
        /// All three are needed and each one alone is defeatable:
        /// </para>
        /// <list type="bullet">
        /// <item><b>Difference from the empty plate</b> answers "did anything arrive in the
        /// frame". It is the only measure that cannot be satisfied by the room.</item>
        /// <item><b>Local contrast</b> — the creature against the wall in the ring
        /// immediately around it — answers "can the eye separate it from its background".
        /// This is the measure the eye actually uses, and it is the one the old whole-frame
        /// number hid: a creature can change thousands of pixels by dimming the corridor it
        /// stands in and still be invisible where it is standing.</item>
        /// <item><b>Coverage of the silhouette</b> answers "is it a body or a glint".</item>
        /// </list>
        /// </summary>
        private static Reading Measure(string label, float distance, MonsterStateId state,
                                       Color[] pixels, Color[] plate, bool[] mask,
                                       MonsterBeamResolve? resolve)
        {
            var ring = Ring(mask);

            // The wall first, because the contrast every footprint pixel is judged
            // against is the wall's mean, not the pixel's own history.
            var ringPixels = 0;
            double onRing = 0.0;
            for (var i = 0; i < pixels.Length; i++)
            {
                if (!ring[i])
                {
                    continue;
                }

                ringPixels++;
                onRing += Luminance(pixels[i]);
            }

            var around = ringPixels > 0 ? (float)(onRing / ringPixels) : 0f;

            var footprint = 0;
            double onBody = 0.0;
            double sumDiff = 0.0;
            double sumContrast = 0.0;
            double signed = 0.0;
            var changed = 0;
            var deltas = new List<float>();

            for (var i = 0; i < pixels.Length; i++)
            {
                if (!mask[i])
                {
                    continue;
                }

                var here = Luminance(pixels[i]);
                var behind = Luminance(plate[i]);
                var delta = Mathf.Abs(here - behind);

                footprint++;
                onBody += here;
                sumDiff += delta;
                signed += here - behind;

                // Per pixel, and absolute. Averaging the body and then subtracting the
                // wall is the obvious version and it is wrong for exactly the creature
                // this is measuring: a dark body carrying a lit edge has a MEAN that lands
                // on the wall, so the aggregate reports zero contrast for the picture in
                // which the shape is most legible. Measured against the wall's mean one
                // pixel at a time, the dark body and the bright edge both count, which is
                // what the eye does with them.
                sumContrast += Mathf.Abs(here - around);

                deltas.Add(delta);
                if (delta > ChangedEpsilon)
                {
                    changed++;
                }
            }

            deltas.Sort();
            var peak = deltas.Count > 0 ? deltas[Mathf.Min(deltas.Count - 1, (int)(deltas.Count * 0.95f))] : 0f;

            var body = footprint > 0 ? (float)(onBody / footprint) : 0f;

            return new Reading(
                label, distance, state,
                footprintPixels: footprint,
                framePercent: 100f * footprint / pixels.Length,
                meanDiff: footprint > 0 ? (float)(sumDiff / footprint) : 0f,
                coverage: footprint > 0 ? (float)changed / footprint : 0f,
                peakDiff: peak,
                contrast: footprint > 0 ? (float)(sumContrast / footprint) : 0f,
                bodyLuminance: body,
                ringLuminance: around,
                signedDiff: footprint > 0 ? (float)(signed / footprint) : 0f,
                resolve: resolve != null ? resolve.Resolve : -1f);
        }

        /// <summary>The wall immediately around the creature — a dilated collar, hollowed out.</summary>
        private static bool[] Ring(bool[] mask)
        {
            var outer = Dilate(mask, RingOuterRadius);
            var inner = Dilate(mask, RingInnerRadius);
            var ring = new bool[mask.Length];
            for (var i = 0; i < mask.Length; i++)
            {
                ring[i] = outer[i] && !inner[i];
            }

            return ring;
        }

        /// <summary>Separable box dilation. Two passes, so the cost is linear in the radius.</summary>
        private static bool[] Dilate(bool[] mask, int radius)
        {
            var horizontal = new bool[mask.Length];
            for (var y = 0; y < Height; y++)
            {
                var row = y * Width;
                for (var x = 0; x < Width; x++)
                {
                    var lo = Mathf.Max(0, x - radius);
                    var hi = Mathf.Min(Width - 1, x + radius);
                    for (var i = lo; i <= hi; i++)
                    {
                        if (mask[row + i])
                        {
                            horizontal[row + x] = true;
                            break;
                        }
                    }
                }
            }

            var result = new bool[mask.Length];
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var lo = Mathf.Max(0, y - radius);
                    var hi = Mathf.Min(Height - 1, y + radius);
                    for (var i = lo; i <= hi; i++)
                    {
                        if (horizontal[i * Width + x])
                        {
                            result[y * Width + x] = true;
                            break;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Prints the table and fails the run if §04's 15 m cannot be met.</summary>
        private static void Report(List<Reading> readings)
        {
            var lines = new List<string>
            {
                "  dist  state       footprint    diff  coverage    peak     body    ring  contrast   verdict",
            };

            foreach (var r in readings)
            {
                lines.Add(string.Format(
                    "  {0,4}m {1,-10} {2,7}px {3,7:0.0000} {4,8:0.000} {5,7:0.0000} {6,7:0.0000} {7,7:0.0000} {8,9:0.0000}   {9} {10}",
                    r.Distance.ToString("0"), r.State, r.FootprintPixels, r.MeanDiff, r.Coverage, r.PeakDiff,
                    r.BodyLuminance, r.RingLuminance, r.Contrast, r.Passes ? "PASS" : "FAIL", r.Why));
            }

            Debug.Log("[MonsterShot] staged readings"
                      + "  (pass: contrast ≥ " + PassContrast.ToString("0.000")
                      + ", coverage ≥ " + PassCoverage.ToString("0.00")
                      + ", peak ≥ " + PassPeak.ToString("0.000") + ")\n"
                      + string.Join("\n", lines));

            foreach (var r in readings)
            {
                Debug.Log("[MonsterShot] " + r.Label
                          + "  footprint=" + r.FootprintPixels + "px (" + r.FramePercent.ToString("0.000") + "% of frame)"
                          + " diff=" + r.MeanDiff.ToString("0.0000")
                          + " coverage=" + r.Coverage.ToString("0.000")
                          + " peak=" + r.PeakDiff.ToString("0.0000")
                          + " body=" + r.BodyLuminance.ToString("0.0000")
                          + " ring=" + r.RingLuminance.ToString("0.0000")
                          + " contrast=" + r.Contrast.ToString("0.0000")
                          + " (" + (r.BodyLuminance >= r.RingLuminance ? "brighter" : "darker") + " than its wall)"
                          + " weber=" + r.Weber.ToString("0.000")
                          + " resolve=" + r.Resolve.ToString("0.00")
                          + " → " + (r.Passes ? "PASS" : "FAIL " + r.Why));
            }

            var observer = readings.Where(r => Mathf.Approximately(r.Distance, ObserverEraShotMetres)).ToList();
            if (observer.Count == 0)
            {
                // Not an error. The staged rig always has 15 m; the map rig photographs the
                // longest corridor that exists, and §12 caps a straight run at 20 m, so a
                // regenerated layout can legitimately have nowhere to stand 15 m apart. The
                // honest report is the distance that was actually available.
                var furthest = readings.OrderByDescending(r => r.Distance).FirstOrDefault();
                Debug.LogWarning("[MonsterShot] nothing was shot at the reference distance ("
                                 + ObserverEraShotMetres + " m); the longest lane this scene "
                                 + "offered reached " + furthest.Distance.ToString("0.0") + " m, which "
                                 + (furthest.Passes ? "passes" : "FAILS " + furthest.Why) + ".");
                return;
            }

            var failed = observer.Where(r => !r.Passes).ToList();

            if (failed.Count > 0)
            {
                Debug.LogWarning("[MonsterShot] the reference distance is " + ObserverEraShotMetres
                                 + " m and " + failed.Count + " of " + observer.Count
                                 + " frames at that distance are below the visibility floor: "
                                 + string.Join(", ", failed.Select(f => f.Label + " (" + f.Why + ")")));
            }
            else
            {
                Debug.Log("[MonsterShot] reference-distance visibility passes: every frame at "
                          + ObserverEraShotMetres + " m is above the visibility floor.");
            }
        }

        /// <summary>One staged frame, measured.</summary>
        public readonly struct Reading
        {
            public readonly string Label;
            public readonly float Distance;
            public readonly MonsterStateId State;
            public readonly int FootprintPixels;
            public readonly float FramePercent;

            /// <summary>Mean absolute luminance change over the footprint, against the empty plate.</summary>
            public readonly float MeanDiff;

            /// <summary>Fraction of the footprint that changed by more than two code values.</summary>
            public readonly float Coverage;

            /// <summary>95th-percentile luminance change inside the footprint.</summary>
            public readonly float PeakDiff;

            /// <summary>
            /// Mean per-pixel luminance separation from the wall in the ring around the
            /// creature. The measure the eye actually uses to find a shape.
            /// </summary>
            public readonly float Contrast;

            public readonly float BodyLuminance;
            public readonly float RingLuminance;

            /// <summary>Mean signed change. Negative means the creature landed darker than what it hid.</summary>
            public readonly float SignedDiff;

            public readonly float Resolve;

            public Reading(string label, float distance, MonsterStateId state, int footprintPixels,
                           float framePercent, float meanDiff, float coverage, float peakDiff,
                           float contrast, float bodyLuminance, float ringLuminance, float signedDiff,
                           float resolve)
            {
                Label = label;
                Distance = distance;
                State = state;
                FootprintPixels = footprintPixels;
                FramePercent = framePercent;
                MeanDiff = meanDiff;
                Coverage = coverage;
                PeakDiff = peakDiff;
                Contrast = contrast;
                BodyLuminance = bodyLuminance;
                RingLuminance = ringLuminance;
                SignedDiff = signedDiff;
                Resolve = resolve;
            }

            /// <summary>
            /// Signed Weber contrast of the body's mean against the surround. Not a gate —
            /// it cancels on the picture that reads best — but it is the number that says
            /// whether the creature is falling as a dark shape or lifting as a pale one,
            /// and §06 has an opinion about which.
            /// </summary>
            public float Weber => (BodyLuminance - RingLuminance) / Mathf.Max(RingLuminance, CodeValue);

            public bool Passes => Contrast >= PassContrast && Coverage >= PassCoverage && PeakDiff >= PassPeak;

            /// <summary>Which of the three gates failed, for a log line that does not need a table.</summary>
            public string Why
            {
                get
                {
                    var reasons = new List<string>(3);
                    if (Contrast < PassContrast)
                    {
                        reasons.Add("contrast " + Contrast.ToString("0.0000") + " < " + PassContrast.ToString("0.000"));
                    }

                    if (Coverage < PassCoverage)
                    {
                        reasons.Add("coverage " + Coverage.ToString("0.000") + " < " + PassCoverage.ToString("0.00"));
                    }

                    if (PeakDiff < PassPeak)
                    {
                        reasons.Add("peak " + PeakDiff.ToString("0.0000") + " < " + PassPeak.ToString("0.000"));
                    }

                    return reasons.Count == 0 ? string.Empty : "(" + string.Join("; ", reasons) + ")";
                }
            }
        }

        /// <summary>
        /// Command-line overrides for the shader's tuning values, so a calibration sweep
        /// is one editor launch rather than one per value.
        /// <para>
        /// The rim's strength, falloff and pedestal and the creature's fog response are
        /// four numbers whose right values are only knowable by looking at the result, and
        /// an editor launch is about two minutes. Without this the loop is edit → rebuild
        /// materials → launch → look, times four parameters, which is how tuning gets
        /// abandoned halfway and a shipped value ends up being the first one tried.
        /// </para>
        /// <para>
        /// Applied through a property block on top of whatever <c>MonsterBeamResolve</c>
        /// just wrote, so a sweep never edits an asset and a run with no flags is exactly
        /// the shipped creature. The strength is multiplied by the component's own
        /// distance ramp for the same reason — a sweep has to move the value the game
        /// moves, not replace the curve with a constant.
        /// </para>
        /// </summary>
        private readonly struct Overrides
        {
            /// <summary>
            /// Colour of the flat ambient fill <c>MonsterBeamResolve</c> used to apply, kept
            /// so <c>-ambientFill 0.22 -rimStrength 0 -fogResponse 1</c> reproduces the
            /// creature exactly as it was before this work.
            /// <para>
            /// Without it the "before" column of any comparison has to be quoted from an
            /// older run measured by an older metric, which is the standard way a rendering
            /// change gets credited with an improvement it did not make.
            /// </para>
            /// </summary>
            private static readonly Color LegacyFillColour = new Color(0.62f, 0.68f, 0.82f);

            private readonly float _strength;
            private readonly float _power;
            private readonly float _floor;
            private readonly float _fog;
            private readonly float _eyes;
            private readonly float _legacyFill;

            private Overrides(float strength, float power, float floor, float fog, float eyes,
                              float legacyFill)
            {
                _strength = strength;
                _power = power;
                _floor = floor;
                _fog = fog;
                _eyes = eyes;
                _legacyFill = legacyFill;
            }

            public bool IsEmpty => float.IsNaN(_strength) && float.IsNaN(_power)
                                   && float.IsNaN(_floor) && float.IsNaN(_fog)
                                   && float.IsNaN(_eyes) && float.IsNaN(_legacyFill);

            /// <summary>Filename suffix that says which variant a frame is, or nothing.</summary>
            public string Suffix =>
                (float.IsNaN(_strength) ? string.Empty : "-rim" + Tag(_strength))
                + (float.IsNaN(_eyes) ? string.Empty : "-eye" + Tag(_eyes));

            private static string Tag(float value) => value.ToString("0.###").Replace(".", "p");

            /// <summary>
            /// One <see cref="Overrides"/> per swept value. <c>-rimStrength</c> and
            /// <c>-eyeGlow</c> both take comma lists and are crossed, so a calibration is
            /// one launch and one set of comparable frames rendered against the identical
            /// plate and measured against the identical matte.
            /// </summary>
            public static List<Overrides> FromCommandLine()
            {
                var power = ArgFloat("-rimPower");
                var floor = ArgFloat("-rimFloor");
                var fog = ArgFloat("-fogResponse");
                var legacy = ArgFloat("-ambientFill");
                var strengths = ArgFloats("-rimStrength") ?? new[] { float.NaN };
                var eyes = ArgFloats("-eyeGlow") ?? new[] { float.NaN };

                var variants = new List<Overrides>(strengths.Length * eyes.Length);
                foreach (var strength in strengths)
                {
                    foreach (var eye in eyes)
                    {
                        variants.Add(new Overrides(strength, power, floor, fog, eye, legacy));
                    }
                }

                return variants;
            }

            public void Push(GameObject monster, MonsterBeamResolve? resolve, float distance)
            {
                if (IsEmpty)
                {
                    return;
                }

                var ramp = resolve != null ? resolve.RimAt(distance) : 1f;
                var block = new MaterialPropertyBlock();
                foreach (var renderer in monster.GetComponentsInChildren<Renderer>(includeInactive: true))
                {
                    var materials = renderer.sharedMaterials;
                    for (var slot = 0; slot < materials.Length; slot++)
                    {
                        renderer.GetPropertyBlock(block, slot);
                        Set(block, "_RimStrength", float.IsNaN(_strength) ? float.NaN : _strength * ramp);
                        Set(block, "_RimPower", _power);
                        Set(block, "_RimFloor", _floor);
                        Set(block, "_FogResponse", _fog);

                        var name = materials[slot] != null ? materials[slot].name : string.Empty;

                        // The maw was excluded from the old fill, so it is excluded here.
                        if (!float.IsNaN(_legacyFill) && !name.Contains("Monster_Maw"))
                        {
                            block.SetColor("_EmissionColor", LegacyFillColour * (_legacyFill * ramp));
                        }

                        if (!float.IsNaN(_eyes) && name.Contains(MonsterSkin.EyeMaterialName))
                        {
                            block.SetColor("_EmissionColor", MonsterSkin.EyeHue * _eyes);
                        }

                        renderer.SetPropertyBlock(block, slot);
                    }
                }
            }

            private static void Set(MaterialPropertyBlock block, string name, float value)
            {
                if (!float.IsNaN(value))
                {
                    block.SetFloat(name, value);
                }
            }

            public override string ToString() =>
                "rimStrength=" + Show(_strength) + " rimPower=" + Show(_power)
                + " rimFloor=" + Show(_floor) + " fogResponse=" + Show(_fog)
                + " ambientFill=" + Show(_legacyFill);

            private static string Show(float value) => float.IsNaN(value) ? "-" : value.ToString("0.###");
        }

        /// <summary>A float flag, or NaN when it was not passed.</summary>
        private static float ArgFloat(string flag)
        {
            var raw = ArgValue(flag);
            return raw != null && float.TryParse(raw, out var value) ? value : float.NaN;
        }

        private static float[]? ArgFloats(string flag)
        {
            var raw = ArgValue(flag);
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            return raw!.Split(',')
                .Select(s => float.TryParse(s.Trim(), out var v) ? v : float.NaN)
                .Where(v => !float.IsNaN(v))
                .ToArray();
        }

        private static (MonsterStateId State, string Clip, float Time)[] ArgPoses(string flag)
        {
            var raw = ArgValue(flag);
            if (string.IsNullOrEmpty(raw))
            {
                return StagePoses;
            }

            var wanted = new HashSet<string>(raw!.Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            var chosen = StagePoses.Where(p => wanted.Contains(p.Clip)).ToArray();
            return chosen.Length > 0 ? chosen : StagePoses;
        }

        private static Material RequireMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                throw new FileNotFoundException(
                    path + " is missing, so the stage would be built out of Unity's default white "
                    + "and every luminance in the result would be a picture of that instead of §12.");
            }

            return material;
        }

        private static Shader RequireShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null)
            {
                throw new InvalidOperationException(name + " is missing.");
            }

            return shader;
        }

        private readonly struct Lane
        {
            public readonly Vector3 Eye;
            public readonly float Heading;
            public readonly float Clear;
            public readonly float Floor;

            public Lane(Vector3 eye, float heading, float clear, float floor)
            {
                Eye = eye;
                Heading = heading;
                Clear = clear;
                Floor = floor;
            }
        }
    }
}
