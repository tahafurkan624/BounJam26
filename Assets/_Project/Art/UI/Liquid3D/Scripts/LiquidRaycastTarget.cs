// BlobShip - an invisible Graphic that only catches pointer events (used by LiquidSlab when there is no Image).
using UnityEngine;
using UnityEngine.UI;

namespace BlobShip.UI.Liquid3D
{
    // Graphic itself doesn't require one (Image and Text do it themselves); the raycaster reads canvasRenderer.cull.
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("UI/BlobShip/Liquid Raycast Target")]
    public class LiquidRaycastTarget : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) { vh.Clear(); }
    }
}
