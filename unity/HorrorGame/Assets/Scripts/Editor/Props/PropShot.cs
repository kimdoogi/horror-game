#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Match;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Props
{
    /// <summary>
    /// Photographs every interactable a match places, under the game's own torch, at
    /// the two distances a player meets them from.
    /// <para>
    /// <b>Why a rig of its own and not <c>SceneShot</c>.</b> That one frames rooms —
    /// spawn points and zone views — which is the right question for §12's geometry and
    /// the wrong one for a 2.2 cm ring lying on a floor. The question here is whether a
    /// player can tell one §08 row from another at a glance, which only a shot framed on
    /// the object can answer.
    /// </para>
    /// <para>
    /// Run WITHOUT <c>-nographics</c>, or every frame is black. The beam comes from
    /// <c>FlashlightBeam</c>, so what is photographed is lit the way the game lights it.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -silent-crashes -projectPath unity/HorrorGame \
    ///   -executeMethod HorrorGame.EditorTools.Props.PropShot.Batch -shotTag props
    /// </code>
    /// </summary>
    public static class PropShot
    {
        private const string OutputDir = "Shots";
        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>The two ranges §04's reach and §12's sightlines put a prop at.</summary>
        private static readonly float[] Distances = { 2f, 5f };

        /// <summary>Batch entry point.</summary>
        public static void Batch()
        {
            try
            {
                var tag = ArgValue("-shotTag") ?? "props";
                var written = Capture(tag);
                Debug.Log("[PropShot] wrote " + written.Count + " shot(s):\n  " + string.Join("\n  ", written));
                EditorApplication.Exit(written.Count > 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PropShot] " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Builds the solo scene, starts a match and photographs one of every prop kind.</summary>
        public static List<string> Capture(string tag)
        {
            var written = new List<string>();
            if (!SoloPlaytest.BuildScene())
            {
                throw new InvalidOperationException("the solo scene could not be built");
            }

            var director = UnityEngine.Object.FindFirstObjectByType<MatchDirector>();
            if (director == null || !director.BeginMatch(SoloPlaytest.PlaytestSeed))
            {
                throw new InvalidOperationException("no match to photograph");
            }

            var root = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDir);
            Directory.CreateDirectory(root);

            var rig = new GameObject("[PropShot Camera]");
            try
            {
                var camera = rig.AddComponent<Camera>();
                camera.fieldOfView = HorrorGame.Core.GameConstants.FovDefault;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 300f;
                camera.clearFlags = CameraClearFlags.Skybox;

                var beam = new GameObject("Flashlight").AddComponent<Light>();
                beam.transform.SetParent(rig.transform, false);
                HorrorGame.Rendering.FlashlightBeam.Apply(beam);

                foreach (var subject in Subjects())
                {
                    Debug.Log("[PropShot] " + subject.Name + " " + Describe(subject.Prop));

                    // The crosshair glow is half of what this pass added, so the second
                    // distance is shot targeted and the first is not — the pair answers
                    // "can it be told apart" and "is being aimed at obvious".
                    var highlight = subject.Prop.GetComponent<InteractableHighlight>();

                    for (var i = 0; i < Distances.Length; i++)
                    {
                        if (highlight != null)
                        {
                            highlight.SetTargeted(i == 0);
                        }

                        Frame(rig.transform, subject.Prop, Distances[i]);
                        var path = Path.Combine(root, tag + "_" + subject.Name + "_"
                            + Distances[i].ToString("0") + "m.png");
                        RenderTo(camera, path);
                        written.Add(path);
                    }

                    if (highlight != null)
                    {
                        highlight.SetTargeted(false);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rig);
            }

            return written;
        }

        /// <summary>One prop of each distinct model the match placed, plus the fixed set.</summary>
        private static List<(string Name, GameObject Prop)> Subjects()
        {
            var subjects = new List<(string, GameObject)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var spare = 0;

            foreach (var prop in UnityEngine.Object.FindObjectsByType<Interactable>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                     .OrderBy(p => p.GetInstanceID()))
            {
                // The model's own name, so two §08 rows that resolved to different
                // variants are both photographed and a repeat is not.
                var filter = prop.GetComponentInChildren<MeshFilter>();
                var model = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : prop.name;
                if (!seen.Add(model))
                {
                    continue;
                }

                subjects.Add((Sanitise(model), prop.gameObject));
            }

            // Anything an interactable can ask for that this seed happened not to place.
            // §08's row names are pairs and one draw shows one half of each, so without
            // this the 궤짝 is never photographed on a seed that drew the 초상화 — and
            // the 궤짝 is the piece §08 says the whole weight-5 row exists for.
            foreach (var key in PropModels.Required)
            {
                if (seen.Contains(key))
                {
                    continue;
                }

                var stand = InteractablePropLibrary.Instantiate(key);
                stand.name = "[PropShot] " + key;
                stand.transform.position = new Vector3(0f, 0f, spare * SpareSpacingMetres);
                spare++;
                seen.Add(key);
                subjects.Add((Sanitise(key), stand));
            }

            return subjects;
        }

        /// <summary>How far apart unplaced models are stood so no two share a frame.</summary>
        private const float SpareSpacingMetres = 4f;

        /// <summary>What a prop is made of, so a shot with nothing in it names its own cause.</summary>
        private static string Describe(GameObject prop)
        {
            var renderers = prop.GetComponentsInChildren<Renderer>(true);
            var parts = renderers.Select(r => r.name + " enabled=" + r.enabled
                + " active=" + r.gameObject.activeInHierarchy
                + " mats=[" + string.Join("|", r.sharedMaterials.Select(m => m == null ? "NULL"
                    : m.name + ":" + (m.shader == null ? "NOSHADER" : m.shader.name))) + "]"
                + " bounds=" + r.bounds.center.ToString("0.00") + " size=" + r.bounds.size.ToString("0.00"));

            return "at " + prop.transform.position.ToString("0.00")
                + " scale=" + prop.transform.lossyScale.ToString("0.00")
                + " renderers=" + renderers.Length + "  " + string.Join("  ", parts);
        }

        private static string Sanitise(string name)
        {
            var clean = name.Replace(' ', '_');
            foreach (var bad in Path.GetInvalidFileNameChars())
            {
                clean = clean.Replace(bad, '_');
            }

            return clean;
        }

        /// <summary>
        /// Puts the camera at eye height, the given distance away, looking at the middle
        /// of the prop — which is where a player who walked up to it would be.
        /// </summary>
        private static void Frame(Transform rig, GameObject prop, float distance)
        {
            var bounds = Bounds(prop);
            var target = bounds.center;

            // Measured from the prop's surface, not its middle. "Two metres from the
            // 차량" means standing two metres off its flank; from its centre it means
            // standing inside it, and the 6.7 m van photographed as a black wall.
            var reach = distance + (Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.1f);

            // Approached from whichever side has room, so a prop against a wall is not
            // photographed through it.
            var bearing = OpenBearing(target, reach);
            var eye = target + (bearing * reach);
            eye.y = prop.transform.position.y + EyeHeightMetres;

            rig.SetPositionAndRotation(eye, Quaternion.LookRotation((target - eye).normalized, Vector3.up));
        }

        private static Vector3 OpenBearing(Vector3 from, float distance)
        {
            var best = Vector3.forward;
            var bestClearance = -1f;

            for (var i = 0; i < 16; i++)
            {
                var angle = i * (Mathf.PI * 2f / 16f);
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var probe = from + (Vector3.up * 0.4f);
                var clearance = Physics.Raycast(probe, dir, out var hit, distance + 1f, ~0,
                    QueryTriggerInteraction.Ignore)
                    ? hit.distance
                    : distance + 1f;

                if (clearance > bestClearance)
                {
                    bestClearance = clearance;
                    best = dir;
                }
            }

            return best;
        }

        private static Bounds Bounds(GameObject prop)
        {
            var renderers = prop.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(prop.transform.position, Vector3.one * 0.3f);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static void RenderTo(Camera camera, string path)
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

        /// <summary>Where the player's eye is above their feet. Matches the harness rig.</summary>
        private const float EyeHeightMetres = 1.63f;
    }
}
