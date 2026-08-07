using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Prints every enabled light in a map scene with its WORLD position and storey,
    /// straight from a loaded scene rather than from the YAML.
    /// <para>
    /// Exists because this repository has now mis-counted scene contents from the
    /// text three times in one day: Unity escapes Korean names and then quotes the
    /// whole value (a bare prefix grep finds the one ASCII name of eight), prefab
    /// instances keep their name and position in modification lists rather than in
    /// plain documents, and a hand-rolled parent walk silently stops at a prefab
    /// boundary and loses every ancestor offset above it — which made 82 evenly
    /// spread filaments read as "all on B1". A loaded scene has none of those traps:
    /// <c>Transform.position</c> is the world truth by construction. When the
    /// question is "what is in the scene", ask the scene.
    /// </para>
    /// </summary>
    public static class LightCensus
    {
        [MenuItem("HorrorGame/Diagnostics/Light Census", priority = 400)]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Map_FirstSketch.unity", OpenSceneMode.Single);
            var byStorey = new SortedDictionary<int, List<string>>();
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!light.enabled || !light.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var y = light.transform.position.y;
                var storey = Mathf.FloorToInt(y / 3.75f + 0.5f);
                if (!byStorey.TryGetValue(storey, out var list))
                {
                    byStorey[storey] = list = new List<string>();
                }

                list.Add(light.name + " y=" + y.ToString("0.00") + " r=" + light.range.ToString("0.0"));
            }

            foreach (var pair in byStorey.Reverse())
            {
                var label = pair.Key >= 1 ? "위(+" + pair.Key + ")" : "B" + (1 - pair.Key);
                Debug.Log("[LightCensus] " + label + ": " + pair.Value.Count + " enabled — "
                    + string.Join(" · ", pair.Value.Take(4)) + (pair.Value.Count > 4 ? " …" : string.Empty));
            }

            Debug.Log("[LightCensus] total enabled: " + byStorey.Sum(p => p.Value.Count));
        }

        public static void RunFromCommandLine()
        {
            try
            {
                Run();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[LightCensus] " + ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
