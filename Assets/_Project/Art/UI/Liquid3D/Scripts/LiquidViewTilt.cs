// BlobShip - tilts a UI group back a little so the camera looks at it from slightly below: the 3D buttons
// show their thick underside. Straight on otherwise (no turn to the left or right).
using UnityEngine;

namespace BlobShip.UI.Liquid3D
{
    [ExecuteAlways]
    [AddComponentMenu("UI/BlobShip/Liquid View Tilt")]
    public class LiquidViewTilt : MonoBehaviour
    {
        [Range(0f, 25f)] public float tilt = 9f;

        void OnEnable() { Apply(); }
        void OnValidate() { Apply(); }
        void Apply() { transform.localRotation = Quaternion.Euler(tilt, 0f, 0f); }
    }
}
