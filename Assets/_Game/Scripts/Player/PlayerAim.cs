using BounJam.Core;
using UnityEngine;

namespace BounJam.Player
{
    /// <summary>Finds what the crosshair in the middle of the screen is pointing at.</summary>
    [DefaultExecutionOrder(-5)] // after PlayerLook, before LiquidGun reads AimPoint
    public class PlayerAim : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] float maxDistance = 60f;

        /// <summary>World point under the crosshair, or a point far along the view when nothing is there.</summary>
        public Vector3 AimPoint { get; private set; }

        // Uses the camera pose from the end of the previous frame, which Cinemachine sets in LateUpdate.
        void Update()
        {
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            // Start the ray level with the beaver so anything between it and the camera is ignored.
            ray.origin += ray.direction * Mathf.Max(0f, Vector3.Dot(transform.position - ray.origin, ray.direction));
            AimPoint = Physics.Raycast(ray, out RaycastHit hit, maxDistance, GameLayers.Shootable, QueryTriggerInteraction.Ignore)
                ? hit.point
                : ray.GetPoint(maxDistance);
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position + Vector3.up, AimPoint);
            Gizmos.DrawWireSphere(AimPoint, 0.15f);
        }
    }
}
