using UnityEngine;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// A rounded uGUI shape drawn by the "BounJam/UI/Liquid Glass" shader: frosted glass over the scene, or a solid
    /// fill (meter bars, icon discs). The shape is an SDF in the shader, so it needs no sprite; this component packs
    /// size, radius and look into the vertex data. The quad is grown by the shadow margin so the drop shadow has room.
    /// The canvas must pass TexCoord1 and TexCoord2 (GlassUI sets them).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class GlassGraphic : MaskableGraphic
    {
        public enum Mode { Glass = 0, Solid = 1 }

        [SerializeField] Mode mode = Mode.Glass;
        [Tooltip("Corner radius in canvas units. Anything over half the short side makes a pill.")]
        [SerializeField] float cornerRadius = 28f;
        [Tooltip("Glass only: how much the tint colour covers the frosted scene.")]
        [Range(0f, 1f)]
        [SerializeField] float tintAmount = 0.35f;
        [Tooltip("Rim light, sheen and gradient strength.")]
        [Range(0f, 1f)]
        [SerializeField] float highlight = 1f;
        [Range(0f, 1f)]
        [SerializeField] float shadow = 0.22f;
        [Tooltip("Room around the shape for the shadow, canvas units.")]
        [SerializeField] float shadowMargin = 36f;
        [Tooltip("Glass only: blur radius multiplier.")]
        [SerializeField] float blur = 1f;

        public Mode ShapeMode { get => mode; set { mode = value; SetVerticesDirty(); } }
        public float CornerRadius { get => cornerRadius; set { cornerRadius = value; SetVerticesDirty(); } }
        public float TintAmount { get => tintAmount; set { tintAmount = value; SetVerticesDirty(); } }
        public float Highlight { get => highlight; set { highlight = value; SetVerticesDirty(); } }
        public float Shadow { get => shadow; set { shadow = value; SetVerticesDirty(); } }
        public float Blur { get => blur; set { blur = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            Vector2 half = rect.size * 0.5f;
            float margin = shadow > 0f ? shadowMargin : 2f;
            Vector4 shape = new Vector4(half.x, half.y, cornerRadius, (float)mode);
            Vector4 look = new Vector4(shadow, highlight, tintAmount, blur);
            // The graphic's own alpha travels in uv0.z, so the shader can tell a see-through fill from a fade.
            Color32 rgb = color;
            rgb.a = 255;

            for (int i = 0; i < 4; i++)
            {
                float sx = i == 1 || i == 2 ? 1f : -1f;
                float sy = i >= 2 ? 1f : -1f;
                Vector2 offset = new Vector2(sx * (half.x + margin), sy * (half.y + margin));
                var vertex = UIVertex.simpleVert;
                vertex.position = center + offset;
                vertex.color = rgb;
                vertex.uv0 = new Vector4(offset.x, offset.y, color.a, 0f);
                vertex.uv1 = shape;
                vertex.uv2 = look;
                vh.AddVert(vertex);
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
