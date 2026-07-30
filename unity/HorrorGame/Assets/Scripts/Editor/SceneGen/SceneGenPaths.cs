using System.IO;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// Where generated things go.
    /// <para>
    /// Everything the generator writes lives under <see cref="SceneRoot"/> and is
    /// treated as disposable: regenerating from the same seed must be able to
    /// overwrite all of it without a merge. Nothing hand-edited belongs here, which
    /// is why the folder is named for the tool rather than for the level.
    /// </para>
    /// </summary>
    public static class SceneGenPaths
    {
        /// <summary>Folder holding every generated scene.</summary>
        public const string SceneRoot = "Assets/Scenes";

        /// <summary>Folder holding assets a generated scene references — materials, NavMesh data.</summary>
        public const string GeneratedRoot = "Assets/Scenes/Generated";

        /// <summary>Surface materials, one per §12 floor.</summary>
        public const string MaterialRoot = "Assets/Scenes/Generated/Materials";

        /// <summary>Baked NavMesh data.</summary>
        public const string NavMeshRoot = "Assets/Scenes/Generated/NavMesh";

        /// <summary>The playable map. §12's 첫 맵 스케치.</summary>
        public const string MapScene = "Assets/Scenes/Map_FirstSketch.unity";

        /// <summary>
        /// Entry point. Named so that <c>BuildPipelineScenes</c>'s bootstrap-first
        /// ordering finds it: a player whose scene 0 is the map would come up with no
        /// menu and no transport.
        /// </summary>
        public const string BootstrapScene = "Assets/Scenes/Bootstrap.unity";

        /// <summary>Creates a folder and every missing parent, the way <c>AssetDatabase</c> insists on.</summary>
        public static void EnsureFolder(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetFolderPath)?.Replace('\\', '/');
            var leaf = Path.GetFileName(assetFolderPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                Debug.LogError("[SceneGen] Cannot create folder '" + assetFolderPath + "'.");
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
