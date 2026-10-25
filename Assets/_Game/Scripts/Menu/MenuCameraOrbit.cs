using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The main menu's camera: it circles slowly around a point of the scene, swinging back and forth so the blob's
    /// stunt always stays in view, with a gentle rise and fall.
    /// </summary>
    public class MenuCameraOrbit : MonoBehaviour
    {
        [SerializeField] Transform pivot;
        [SerializeField] float distance = 15f;
        [SerializeField] float height = 6.5f;
        [Tooltip("Direction the camera looks from at the middle of its swing, degrees around Y (0 = from -Z).")]
        [SerializeField] float baseYaw = 0f;
        [Tooltip("How far it swings to each side, degrees.")]
        [SerializeField] float swing = 32f;
        [Tooltip("One swing there and back, s.")]
        [SerializeField] float period = 46f;
        [SerializeField] float bob = 0.6f;
        [SerializeField] float bobPeriod = 13f;
        [Tooltip("The camera looks this far above the pivot, which puts the pivot lower on screen (under the Play button).")]
        [SerializeField] float lookHeight = 2.6f;

        void LateUpdate()
        {
            if (pivot == null) return;
            float t = Time.unscaledTime;
            float yaw = baseYaw + swing * Mathf.Sin(t * Mathf.PI * 2f / period);
            float rise = height + bob * Mathf.Sin(t * Mathf.PI * 2f / bobPeriod);
            Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, rise, -distance);
            transform.position = pivot.position + offset;
            transform.rotation = Quaternion.LookRotation(pivot.position + Vector3.up * lookHeight - transform.position, Vector3.up);
        }
    }
}
