#nullable enable

using System;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Dressing
{
    /// <summary>
    /// Which way up the kit imported, and the rotation that fixes it.
    /// <para>
    /// The same problem <see cref="SceneGen.MapSceneBuilder"/> probes for on the
    /// MapKit, and it bites harder here. The FBXs are authored in Blender, whose Z is
    /// up; Unity's importer normally bakes that conversion into the mesh, and when it
    /// does not, every piece arrives lying on its back. A shelving bay measures
    /// 1.10 × 0.42 × 1.97 with the 1.97 in Z instead of Y — so it is 1.97 m *deep*,
    /// it fails §08's carry-channel test in every corridor in the building, and the
    /// scatter quietly produces a level with almost no cover in it. That is precisely
    /// what happened on the first run of this tool: one bulk piece placed out of 265
    /// cells, with no error anywhere.
    /// </para>
    /// <para>
    /// So the state is measured rather than assumed, once per editor session, against
    /// a piece whose authored profile makes the answer unambiguous.
    /// </para>
    /// </summary>
    public static class DressingImport
    {
        private static Quaternion? _correction;

        /// <summary>
        /// Rotation to apply <em>before</em> a piece's yaw so that it stands upright and
        /// its authored front (Blender −Y) becomes Unity +Z.
        /// </summary>
        public static Quaternion Correction => _correction ?? Quaternion.identity;

        /// <summary>Whether the kit arrived with height along Z and needs standing up.</summary>
        public static bool BlenderZUp { get; private set; }

        /// <summary>Discards the cached probe, so a re-import is noticed.</summary>
        public static void Forget() => _correction = null;

        /// <summary>
        /// Probes the kit and caches the answer.
        /// <para>
        /// Uses the manifest's own measurements to choose the yardstick: any piece
        /// whose authored height is at least twice its authored depth answers the
        /// question by itself, and picking it from the data means a piece being
        /// renamed or dropped cannot silently disable the check.
        /// </para>
        /// </summary>
        public static void Probe(DressingKit kit)
        {
            _correction = Quaternion.identity;
            BlenderZUp = false;

            DressingPiece? yardstick = null;
            foreach (var piece in kit.pieces)
            {
                if (piece.size_z > piece.size_y * 2f && piece.size_z > 1.0f)
                {
                    if (yardstick == null || piece.size_z > yardstick.size_z)
                    {
                        yardstick = piece;
                    }
                }
            }

            if (yardstick == null)
            {
                Debug.LogWarning("[Dressing] No piece in the manifest is tall enough to probe the import "
                    + "orientation with. Assuming Unity Y-up; if the dressing arrives lying down, that is why.");
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(DressingManifest.KitRoot + "/" + yardstick.file);
            if (asset == null)
            {
                return;
            }

            var instance = UnityEngine.Object.Instantiate(asset);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = instance.GetComponentsInChildren<Renderer>();
            var zUp = false;
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                zUp = bounds.size.z > bounds.size.y;
            }

            UnityEngine.Object.DestroyImmediate(instance);

            BlenderZUp = zUp;

            // −90° about X sends Blender +Z to Unity +Y and Blender −Y to Unity +Z,
            // which is the same frame the importer would have produced. Every other
            // assumption in this tool — that a piece's front is local +Z, that a
            // cobweb's open faces are local −X and +Z — therefore holds unchanged.
            _correction = zUp ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;

            Debug.Log("[Dressing] Kit orientation probed on " + yardstick.name + ": " + (zUp
                ? "Blender Z-up — pieces are stood upright by the scatterer. The import is what should be "
                  + "fixed; see AssetImportModelPostprocessor."
                : "Unity Y-up, no correction applied."));
        }

        /// <summary>The world rotation for a piece the scatterer wants yawed by <paramref name="yawDegrees"/>.</summary>
        public static Quaternion Rotation(float yawDegrees) =>
            Quaternion.Euler(0f, yawDegrees, 0f) * Correction;

        /// <summary>
        /// Combined bounds of an asset instantiated with the correction applied.
        /// <para>
        /// Used to measure §08's carry envelope off the loot FBXs, which come out of
        /// the same exporter and land the same way up.
        /// </para>
        /// </summary>
        public static bool TryMeasure(string assetPath, out Bounds bounds)
        {
            bounds = default;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                return false;
            }

            var instance = UnityEngine.Object.Instantiate(asset);
            instance.transform.SetPositionAndRotation(Vector3.zero, Correction);
            var renderers = instance.GetComponentsInChildren<Renderer>();
            var found = renderers.Length > 0;
            if (found)
            {
                bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            UnityEngine.Object.DestroyImmediate(instance);
            return found;
        }
    }
}
