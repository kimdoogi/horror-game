#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HorrorGame.Core;
using HorrorGame.Core.Presence;
using HorrorGame.Gameplay.Presence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HorrorGame.Gameplay.PresenceEditor
{
    /// <summary>
    /// Photographs the 그늘 in the situation it actually happens in, and measures whether
    /// it is there.
    /// <para>
    /// Whether something is frightening is not a property any test can assert, so this is
    /// the equivalent of one — the same argument <c>MonsterShot</c> makes. But this rig
    /// has to answer a question the monster's never did, and it is the harder one.
    /// <b>The 그늘 only exists where there is no light.</b> §03's beam removes it, a
    /// 구역 조명 removes it, a 조명탄 removes it. So the frame that decides this entity is
    /// the frame with the torch <em>off</em>, which is also the frame where the room
    /// itself is at ART.md's floor — median luminance 3–16 and a third of the pixels
    /// crushed. If the figure does not read there it does not read anywhere, and every
    /// flattering alternative — lit from behind, silhouetted against a doorway, shot with
    /// the beam on it — answers a different question.
    /// </para>
    /// <para>
    /// So every distance is shot twice, torch on and torch off, and the reading that
    /// matters is the second one. Everything else about the setup is <c>MonsterShot</c>'s,
    /// deliberately: the same §12 corridor section, the same darkest wall material, the
    /// same altitude above the real map so the scene's fog, ambient and grading are the
    /// ones the atmosphere pass wrote.
    /// </para>
    /// <para>
    /// Run WITHOUT <c>-nographics</c>, or every frame is black:
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod
    ///   HorrorGame.Gameplay.PresenceEditor.PresenceShot.Batch -shotTag pre1
    /// </code>
    /// </para>
    /// </summary>
    public static class PresenceShot
    {
        private const string OutputDir = "Shots";
        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>Where the stage is built, above the building. Out of the way; changes no global.</summary>
        private const float StageAltitude = 400f;

        /// <summary>§12's corridor clear section.</summary>
        private const float CorridorWidth = 2.2f;

        /// <summary>§12's corridor height.</summary>
        private const float CorridorHeight = 3.0f;

        /// <summary>Camera height, matching <c>MonsterShot</c> so the two sets are comparable.</summary>
        private const float EyeHeight = 1.63f;

        /// <summary>Gap between the figure and the wall behind it, metres.</summary>
        private const float BackWallGap = 1.5f;

        /// <summary>The map's darkest wall. The 그늘 has to survive the least helpful background too.</summary>
        private const string StageWallMaterial = "Assets/Textures/Materials/Wall_Brick_Painted.mat";

        private const string StageFloorMaterial = "Assets/Scenes/Generated/Materials/Floor_Concrete.mat";

        private const string StageCeilingMaterial = "Assets/Textures/Materials/Ceiling_Concrete_Formed.mat";

        /// <summary>
        /// Distances the 형상 is photographed at, metres.
        /// <para>
        /// Bracketing <c>PresenceView</c>'s own placement band rather than §03's beam:
        /// 4 m is the nearest it may stand, 8 m is the middle, and 12 m is
        /// <see cref="GameConstants.FlashlightRange"/>, which is the furthest. Nothing
        /// outside 3.5–12 m can ever occur, so nothing outside it is worth measuring.
        /// </para>
        /// </summary>
        private static readonly float[] Distances = { 4f, 8f, 12f };

        /// <summary>
        /// Distances actually used by a run — <see cref="Distances"/> unless
        /// <c>-shotDistances 3,8,15</c> overrides it.
        /// <para>
        /// The override exists so a reviewer can photograph the 형상 <b>outside</b> the band
        /// it may stand in, which is the only way to see what the near and far clamps are
        /// protecting against. Anything below <see cref="PresenceView.FigureNearMetres"/> or
        /// above <see cref="PresenceView.FigureFarMetres"/> cannot occur in a match, so such
        /// a frame is a diagnostic and never evidence about the shipped game; the report
        /// marks those rows <c>out-of-band</c> and the batch gate ignores them for that
        /// reason.
        /// </para>
        /// </summary>
        private static float[] RequestedDistances()
        {
            var raw = ArgValue("-shotDistances");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Distances;
            }

            var parts = raw.Split(',');
            var parsed = new List<float>(parts.Length);
            foreach (var part in parts)
            {
                if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var metres)
                    && metres > 0f)
                {
                    parsed.Add(metres);
                }
            }

            return parsed.Count > 0 ? parsed.ToArray() : Distances;
        }

        /// <summary>True when a distance is one the 형상 can never actually stand at.</summary>
        internal static bool OutOfBand(float distance)
        {
            return distance < PresenceView.FigureNearMetres || distance > PresenceView.FigureFarMetres;
        }

        /// <summary>
        /// Mean luminance separation between the figure and the wall around it that counts
        /// as "a person can tell something is standing there".
        /// <para>
        /// <c>MonsterShot</c> derives 0.015 and this rig holds itself to the same number,
        /// because it is a claim about human vision and a dark frame rather than about
        /// which creature is in it. Anything else would be this rig grading its own
        /// homework more gently than the one next door.
        /// </para>
        /// </summary>
        private const float PassContrast = 0.015f;

        /// <summary>A pixel counts as changed when it moved by more than twice 8-bit rounding.</summary>
        private const float ChangedEpsilon = 2f / 255f;

        /// <summary>Batch entry point.</summary>
        public static void Batch()
        {
            try
            {
                var scenePath = ArgValue("-shotScene") ?? "Assets/Scenes/Map_FirstSketch.unity";
                var tag = ArgValue("-shotTag") ?? "pre";

                if (!File.Exists(scenePath))
                {
                    Debug.LogError("[PresenceShot] No scene at " + scenePath);
                    EditorApplication.Exit(2);
                    return;
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var readings = Capture(scene, tag);

                Report(readings);
                EditorApplication.Exit(readings.Exists(r => r.Gated && !r.Pass) ? 3 : 0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PresenceShot] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Menu entry point. Same frames, no exit.</summary>
        [MenuItem("HorrorGame/Presence/Photograph the 그늘")]
        public static void Menu()
        {
            var scene = SceneManager.GetActiveScene();
            Report(Capture(scene, "pre_menu"));
        }

        /// <summary>Renders the set and returns one reading per frame.</summary>
        public static List<Reading> Capture(Scene scene, string tag)
        {
            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDir);
            Directory.CreateDirectory(root);

            var prefab = PresenceRig.LoadViewPrefab();
            if (prefab == null)
            {
                throw new FileNotFoundException(
                    PresenceRig.ViewPrefabPath + " is missing. Run PresenceRig.Build first.");
            }

            var readings = new List<Reading>();
            var distances = RequestedDistances();

            var stage = new GameObject("[PresenceShot Stage]");
            var rig = new GameObject("[PresenceShot Camera]");
            var presence = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var doused = DouseLights(scene);

            try
            {
                var origin = new Vector3(0f, StageAltitude, 0f);
                BuildStage(stage, origin);

                var camera = rig.AddComponent<Camera>();
                camera.fieldOfView = GameConstants.FovDefault;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 300f;
                camera.clearFlags = CameraClearFlags.Skybox;
                rig.transform.SetPositionAndRotation(origin + (Vector3.up * EyeHeight), Quaternion.identity);

                var post = rig.AddComponent<UniversalAdditionalCameraData>();
                post.renderPostProcessing = true;
                post.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                post.antialiasingQuality = AntialiasingQuality.High;

                // §03's shipped beam, not one built here. HorrorGame.Rendering.FlashlightBeam
                // exists because the art was once reviewed under a dimmer beam than the game
                // uses, and this rig switches it on and off as its whole experiment.
                var beam = new GameObject("Flashlight").AddComponent<Light>();
                beam.transform.SetParent(rig.transform, false);
                HorrorGame.Rendering.FlashlightBeam.Apply(beam);

                var view = presence.GetComponent<PresenceView>();
                if (view == null)
                {
                    throw new InvalidOperationException(PresenceRig.ViewPrefabPath + " has no PresenceView.");
                }

                view.Eye = rig.transform;

                foreach (var torchOn in new[] { true, false })
                {
                    beam.enabled = torchOn;
                    view.BeamActive = torchOn;
                    var suffix = torchOn ? "_lit" : "_dark";

                    foreach (var distance in distances)
                    {
                        MoveBackWall(stage, origin, distance);

                        // The clean plate for THIS wall position and THIS beam state. A
                        // plate shared across either would difference the figure against a
                        // frame it is not standing in, and the biggest number in the result
                        // would be the wall or the torch.
                        view.HideFigure();
                        view.SetStageOverride(PresenceStage.Clear, 0f);
                        view.LayOutMotes(0f);
                        var plate = RenderPixels(camera);
                        WritePng(Path.Combine(root, tag + "_plate_" + Name(distance) + suffix + ".png"), plate);

                        // 임박 — the figure standing at that distance, pool just past the
                        // warning, which is the state a player actually meets it in.
                        view.SetStageOverride(PresenceStage.Close, GameConstants.PresenceWarnPooling + 0.05f);
                        view.LayOutMotes(GameConstants.PresenceWarnPooling + 0.05f);
                        view.PlaceFigureAt(origin + new Vector3(0f, 0f, distance));

                        var name = tag + "_close_" + Name(distance) + suffix;
                        var pixels = RenderPixels(camera);
                        WritePng(Path.Combine(root, name + ".png"), pixels);
                        // Out-of-band distances are photographed on request but never gated:
                        // the 형상 cannot stand there, so a contrast floor written for the
                        // shipped band would be judging a frame the game cannot produce.
                        readings.Add(Measure(
                            name, distance, pixels, plate, view.MotesInFrame(camera),
                            gated: !torchOn && !OutOfBand(distance)));
                    }
                }

                // The two frames with no figure in them at all: the gathering, and the
                // moment the monster's twenty metres arrive and it all withdraws. Ungated —
                // they are looked at rather than measured against a floor, because "fewer
                // motes than the frame before" is not a contrast claim.
                MoveBackWall(stage, origin, 12f);
                view.HideFigure();

                foreach (var torchOn in new[] { true, false })
                {
                    beam.enabled = torchOn;
                    view.BeamActive = torchOn;
                    var suffix = torchOn ? "_lit" : "_dark";

                    view.SetStageOverride(PresenceStage.Clear, 0f);
                    view.LayOutMotes(0f);
                    var plate = RenderPixels(camera);

                    foreach (var (label, pooling) in new[]
                             {
                                 ("gathering", 0.30f),
                                 ("gathering_deep", GameConstants.PresenceWarnPooling - 0.05f),
                                 ("cleared_by_the_monster", 0.06f),
                             })
                    {
                        view.SetStageOverride(PresenceStage.Gathering, pooling);
                        view.LayOutMotes(pooling);

                        var name = tag + "_" + label + suffix;
                        var pixels = RenderPixels(camera);
                        WritePng(Path.Combine(root, name + ".png"), pixels);

                        // Ungated. "Fewer motes than the frame before" is a comparison
                        // between two of these rows, not a contrast claim against a floor,
                        // and the floor is derived for a 2 m silhouette rather than for
                        // 3 cm flakes.
                        readings.Add(Measure(name, 0f, pixels, plate, view.MotesInFrame(camera), gated: false));
                    }
                }

                UnityEngine.Object.DestroyImmediate(beam.gameObject);
            }
            finally
            {
                Restore(doused);
                UnityEngine.Object.DestroyImmediate(presence);
                UnityEngine.Object.DestroyImmediate(rig);
                UnityEngine.Object.DestroyImmediate(stage);
            }

            return readings;
        }

        /// <summary>One frame's numbers.</summary>
        public readonly struct Reading
        {
            /// <summary>File stem.</summary>
            public readonly string Name;

            /// <summary>Metres to the 형상, or 0 for a frame with no figure in it.</summary>
            public readonly float Distance;

            /// <summary>Mean luminance separation between the changed pixels and the plate underneath them.</summary>
            public readonly float Contrast;

            /// <summary>Brightest changed pixel, 0–1. The grain's own reading.</summary>
            public readonly float Peak;

            /// <summary>Share of the frame the 그늘 occupies, 0–1.</summary>
            public readonly float Coverage;

            /// <summary>Motes inside the frustum when the frame was taken — placement, not legibility.</summary>
            public readonly int Motes;

            /// <summary>Whether this frame is held to <see cref="PassContrast"/>. Only the torch-off ones are.</summary>
            public readonly bool Gated;

            /// <summary>Builds a reading.</summary>
            public Reading(string name, float distance, float contrast, float peak, float coverage,
                int motes, bool gated)
            {
                Name = name;
                Distance = distance;
                Contrast = contrast;
                Peak = peak;
                Coverage = coverage;
                Motes = motes;
                Gated = gated;
            }

            /// <summary>Whether the frame clears the floor. Meaningless when <see cref="Gated"/> is false.</summary>
            public bool Pass => Contrast >= PassContrast && Coverage > 0f;
        }

        private static Reading Measure(
            string name, float distance, Color[] pixels, Color[] plate, int motes, bool gated)
        {
            var changed = 0;
            var sum = 0.0;
            var peak = 0f;

            for (var i = 0; i < pixels.Length; i++)
            {
                var now = Luminance(pixels[i]);
                var before = Luminance(plate[i]);
                var delta = Mathf.Abs(now - before);

                if (delta <= ChangedEpsilon)
                {
                    continue;
                }

                changed++;
                sum += delta;
                if (now > peak)
                {
                    peak = now;
                }
            }

            var contrast = changed > 0 ? (float)(sum / changed) : 0f;
            var coverage = (float)changed / pixels.Length;
            return new Reading(name, distance, contrast, peak, coverage, motes, gated);
        }

        private static void Report(List<Reading> readings)
        {
            var text = "[PresenceShot] 그늘 readings  (torch-off frames gated: contrast >= "
                       + PassContrast.ToString("0.000", CultureInfo.InvariantCulture) + ")\n"
                       + "  frame                                    dist   contrast     peak  coverage  inFrame  verdict\n";

            foreach (var r in readings)
            {
                text += "  " + r.Name.PadRight(40)
                        + (r.Distance > 0f ? r.Distance.ToString("0").PadLeft(4) + "m" : "     ")
                        + r.Contrast.ToString("0.0000", CultureInfo.InvariantCulture).PadLeft(11)
                        + r.Peak.ToString("0.0000", CultureInfo.InvariantCulture).PadLeft(9)
                        + r.Coverage.ToString("0.0000", CultureInfo.InvariantCulture).PadLeft(10)
                        + r.Motes.ToString(CultureInfo.InvariantCulture).PadLeft(7)
                        + "  " + (!r.Gated
                            ? (r.Distance > 0f && OutOfBand(r.Distance) ? "look (out-of-band)" : "look")
                            : r.Pass ? "PASS" : "FAIL") + "\n";
            }

            text += "\n  The torch-off rows are the measurement. §03's beam removes the 그늘, so a\n"
                    + "  frame with the light on is a picture of it going away — informative, and not\n"
                    + "  the situation this entity occurs in.\n"
                    + "  out-of-band rows sit outside PresenceView's "
                    + PresenceView.FigureNearMetres.ToString("0.0", CultureInfo.InvariantCulture) + "–"
                    + PresenceView.FigureFarMetres.ToString("0.0", CultureInfo.InvariantCulture)
                    + " m placement band: the 형상 cannot stand there in a match, so they are\n"
                    + "  diagnostics of what the clamps prevent and never evidence about the shipped game.";

            Debug.Log(text);
        }

        private static float Luminance(Color c) => (0.2126f * c.r) + (0.7152f * c.g) + (0.0722f * c.b);

        private static string Name(float distance) =>
            distance.ToString("0", CultureInfo.InvariantCulture) + "m";

        private static void BuildStage(GameObject stage, Vector3 origin)
        {
            var wall = RequireMaterial(StageWallMaterial);
            var floor = RequireMaterial(StageFloorMaterial);
            var ceiling = RequireMaterial(StageCeilingMaterial);

            const float length = 32f;
            const float behind = 2f;
            var mid = (length - behind) * 0.5f;

            Slab(stage, "Floor", origin + new Vector3(0f, -0.1f, mid),
                new Vector3(CorridorWidth, 0.2f, length), floor);
            Slab(stage, "Ceiling", origin + new Vector3(0f, CorridorHeight + 0.1f, mid),
                new Vector3(CorridorWidth, 0.2f, length), ceiling);
            Slab(stage, "Wall_L", origin + new Vector3((-CorridorWidth * 0.5f) - 0.1f, CorridorHeight * 0.5f, mid),
                new Vector3(0.2f, CorridorHeight, length), wall);
            Slab(stage, "Wall_R", origin + new Vector3((CorridorWidth * 0.5f) + 0.1f, CorridorHeight * 0.5f, mid),
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

            // The colliders stay, unlike MonsterShot's. PresenceView places the figure and
            // the motes with raycasts against real geometry, and a stage with no colliders
            // would make every placement fall through the floor — which is a rig that
            // photographs nothing and reports it as a contrast failure.
        }

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
            foreach (var light in doused)
            {
                if (light != null)
                {
                    light.enabled = true;
                }
            }
        }

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

        private static string? ArgValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
