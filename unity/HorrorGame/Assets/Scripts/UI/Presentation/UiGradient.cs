#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI
{
    /// <summary>
    /// Fades a graphic's alpha horizontally, in the mesh.
    /// <para>
    /// <b>Why this exists.</b> The main menu needs a scrim that darkens the left of the
    /// frame enough for the title to read and then lets go of the corridor, and this
    /// layer loads no assets — there is no gradient sprite to reach for, and there is
    /// not going to be one, because a texture is a thing that can go missing between a
    /// scene generator and a screen. A flat <c>Image</c> gives a hard vertical seam
    /// instead, and a seam straight down the middle of the one picture the store page
    /// opens on is worse than no scrim at all.
    /// </para>
    /// <para>
    /// Stacking a dozen bands of decreasing alpha was the other option and is what this
    /// replaces: it costs a dozen draw calls, still bands visibly on a dark wall, and
    /// puts the ramp in the layout rather than in one place. A
    /// <see cref="BaseMeshEffect"/> writes the ramp into the vertex colours of the quad
    /// that already exists.
    /// </para>
    /// <para>
    /// Not a tuned game value in sight: a menu the player is not in cannot change what
    /// happens in a match (ARCHITECTURE §2).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/UI/Horizontal Fade")]
    public sealed class UiGradient : BaseMeshEffect
    {
        [Tooltip("Alpha multiplier at the left edge of the rect.")]
        [SerializeField]
        [Range(0f, 1f)]
        private float _left = 1f;

        [Tooltip("Alpha multiplier at the right edge of the rect.")]
        [SerializeField]
        [Range(0f, 1f)]
        private float _right;

        /// <summary>Sets the two ends of the ramp and redraws.</summary>
        public void Set(float left, float right)
        {
            _left = Mathf.Clamp01(left);
            _right = Mathf.Clamp01(right);

            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }
        }

        /// <inheritdoc />
        public override void ModifyMesh(VertexHelper helper)
        {
            if (!IsActive() || helper == null)
            {
                return;
            }

            var rect = ((RectTransform)transform).rect;
            if (rect.width <= 0f)
            {
                return;
            }

            var vertices = new List<UIVertex>(helper.currentVertCount);
            helper.GetUIVertexStream(vertices);

            for (var i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];

                // Smoothstep rather than linear: a linear alpha ramp over a nearly black
                // wall still shows its two ends as creases, because the eye finds the
                // discontinuity in the *slope*, not in the value.
                var t = Mathf.Clamp01((vertex.position.x - rect.xMin) / rect.width);
                var eased = t * t * (3f - (2f * t));

                var colour = vertex.color;
                colour.a = (byte)Mathf.RoundToInt(colour.a * Mathf.Lerp(_left, _right, eased));
                vertex.color = colour;
                vertices[i] = vertex;
            }

            helper.Clear();
            helper.AddUIVertexTriangleStream(vertices);
        }
    }
}
