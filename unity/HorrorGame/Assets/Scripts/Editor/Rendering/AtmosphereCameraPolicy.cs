#nullable enable

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.EditorTools.Rendering
{
    /// <summary>
    /// Makes bare cameras render the game's post-processing stack, so a shot taken
    /// in batch mode is a picture of the game rather than of its raw framebuffer.
    /// <para>
    /// URP decides per camera whether to run post processing, and it reads that
    /// from <see cref="UniversalAdditionalCameraData.renderPostProcessing"/>, which
    /// defaults to <c>false</c> — and a camera with no
    /// <see cref="UniversalAdditionalCameraData"/> at all is treated as <c>false</c>
    /// too. <c>SceneShot</c> builds its rig from a plain
    /// <see cref="Camera"/>, so without this every review render would come out
    /// ungraded, unfogged and untonemapped while the project itself was configured
    /// correctly. That failure is invisible: the shots look like the art is broken,
    /// and the art is fine.
    /// </para>
    /// <para>
    /// Editor only, and it never touches a camera somebody has configured — the
    /// presence of the component is taken as "this camera has an owner". The
    /// component it adds is marked <see cref="HideFlags.DontSave"/>, so reviewing a
    /// scene cannot quietly commit a camera change to it.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    internal static class AtmosphereCameraPolicy
    {
        static AtmosphereCameraPolicy()
        {
            // Subscribed on every domain reload, so unsubscribe first: a duplicate
            // handler would add the component twice and log an error per frame.
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        /// <summary>
        /// Runs once per frame before any camera's render data is built, which is
        /// the last moment the decision can still be changed.
        /// </summary>
        private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            for (var i = 0; i < cameras.Count; i++)
            {
                var camera = cameras[i];

                // Scene-view cameras follow the Scene view's own post-processing
                // toggle, and preview cameras render thumbnails where a vignette
                // would be actively unhelpful.
                if (camera == null || camera.cameraType != CameraType.Game)
                {
                    continue;
                }

                if (camera.TryGetComponent<UniversalAdditionalCameraData>(out _))
                {
                    continue;
                }

                var data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.hideFlags = HideFlags.DontSave;
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
        }
    }
}
