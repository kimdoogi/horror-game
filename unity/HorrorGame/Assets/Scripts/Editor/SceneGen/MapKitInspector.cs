using System;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// Reports what each MapKit piece actually measures once Unity has imported it.
    /// <para>
    /// The generator docks pieces by aligning their world bounds to a grid cell, which
    /// is robust to where the FBX pivot ended up but <em>not</em> to a piece whose
    /// mesh is bigger than the footprint the manifest claims: a wall that overhangs by
    /// 0.15 m would silently shift every neighbouring tile by the same amount, and the
    /// map would look right until a player walked into a seam. This prints the numbers
    /// so that assumption is checkable instead of assumed.
    /// </para>
    /// <para>
    /// It is also the fastest way to catch an axis-conversion change on import: a kit
    /// re-exported without Blender's Z-up conversion reads here as a piece 3.3 m deep
    /// and 2.5 m tall rather than the other way round.
    /// </para>
    /// </summary>
    public static class MapKitInspector
    {
        /// <summary>Prints every piece's imported size next to the grid it has to fit.</summary>
        [MenuItem("HorrorGame/Scene Gen/Report MapKit Sizes", priority = 24)]
        public static void ReportMenu()
        {
            Debug.Log("[SceneGen]\n" + Report());
        }

        /// <summary>Batch entry point.</summary>
        public static void ReportFromCommandLine()
        {
            Debug.Log("[SceneGen]\n" + Report());
            EditorApplication.Exit(0);
        }

        /// <summary>Every piece's world bounds after import, one line each.</summary>
        public static string Report()
        {
            var text = new StringBuilder();
            text.Append("MapKit imported sizes (grid ")
                .Append(MapKitCatalogue.GridMetres.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(" m). size = renderer bounds, cells = size / grid:\n");

            foreach (MapKitPiece piece in Enum.GetValues(typeof(MapKitPiece)))
            {
                var path = MapKitCatalogue.AssetPath(piece);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    text.Append("  ").Append(piece).Append(": MISSING at ").Append(path).Append('\n');
                    continue;
                }

                var instance = UnityEngine.Object.Instantiate(asset);
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    text.Append("  ").Append(piece).Append(": no renderers\n");
                    UnityEngine.Object.DestroyImmediate(instance);
                    continue;
                }

                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                text.Append("  ").Append(piece.ToString().PadRight(30))
                    .Append(" size ").Append(Vec(bounds.size))
                    .Append("  cells ").Append(Cells(bounds.size))
                    .Append("  pivot→min ").Append(Vec(instance.transform.position - bounds.min))
                    .Append('\n');

                UnityEngine.Object.DestroyImmediate(instance);
            }

            return text.ToString();
        }

        private static string Vec(Vector3 v) =>
            "(" + v.x.ToString("0.##", CultureInfo.InvariantCulture) + ", "
                + v.y.ToString("0.##", CultureInfo.InvariantCulture) + ", "
                + v.z.ToString("0.##", CultureInfo.InvariantCulture) + ")";

        private static string Cells(Vector3 size) =>
            "(" + (size.x / MapKitCatalogue.GridMetres).ToString("0.##", CultureInfo.InvariantCulture) + " x "
                + (size.z / MapKitCatalogue.GridMetres).ToString("0.##", CultureInfo.InvariantCulture) + ")";
    }
}
