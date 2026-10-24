using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Two eyes riding on the jelly, looking where the blob is heading. Every frame each eye goes where a ray from the
    /// middle of the skin, turned left or right of the heading, leaves the rendered surface, and faces out along it,
    /// so the eyes wobble, squash and drape with the liquid. While frozen they stay put and tumble with the crystal.
    /// </summary>
    [DefaultExecutionOrder(100)] // after BlobSkin has shaped this frame's mesh
    [RequireComponent(typeof(BlobSkin))]
    public class BlobEyes : MonoBehaviour
    {
        [Tooltip("Children of the skin, pivot at the back of the eye, looking along +Z.")]
        [SerializeField] Transform leftEye;
        [SerializeField] Transform rightEye;
        [Tooltip("How far each eye sits to the side of the heading, degrees.")]
        [SerializeField] float spread = 17f;
        [Tooltip("Eye height above the middle of the skin at rest, m. It shrinks as the blob flattens.")]
        [SerializeField] float raise = 0.03f;
        [Tooltip("How far the eyes climb onto the top of the flat standing puddle, degrees of tilt up from the heading; " +
                 "straight ahead they'd hang off its thin rim.")]
        [SerializeField] float puddleClimb = 8f;
        [Tooltip("How deep the back of each eye sits in the jelly, m.")]
        [SerializeField] float sink = 0.012f;
        [Tooltip("How quickly the eyes turn to follow the surface, 1/s. Smooths over the skin's facets.")]
        [SerializeField] float turnRate = 25f;

        delegate bool SurfaceFinder(Vector3 origin, Vector3 direction, out Vector3 point, out Vector3 normal);

        BlobSkin skin;
        SurfaceFinder skinSurface;

        void Awake()
        {
            skin = GetComponent<BlobSkin>();
            skinSurface = skin.TryFindSurface;
        }

        // The skin has no rotation while liquid, so world directions are its local ones.
        void LateUpdate()
        {
            if (skin.IsFrozen || !skin.IsReady) return;
            Vector3 up = skin.Up;
            Vector3 origin = skin.LocalCenter + up * (raise * (1f - skin.Flatten));
            float turn = 1f - Mathf.Exp(-turnRate * Time.deltaTime);
            float climb = puddleClimb * skin.Spread;
            Place(leftEye, -spread, climb, origin, skin.Forward, up, turn, skinSurface);
            Place(rightEye, spread, climb, origin, skin.Forward, up, turn, skinSurface);
        }

        /// <summary>
        /// Puts the eyes on a still mesh given in the skin's local space, looking along <paramref name="forward"/>:
        /// the pose the editor shows before play.
        /// </summary>
        public void PlaceOn(Mesh mesh, Vector3 forward)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            bool Surface(Vector3 origin, Vector3 direction, out Vector3 point, out Vector3 normal)
                => MeshRay.Exit(vertices, triangles, origin, direction, out point, out normal);

            Vector3 center = mesh.bounds.center + Vector3.up * raise;
            Place(leftEye, -spread, 0f, center, forward, Vector3.up, 1f, Surface);
            Place(rightEye, spread, 0f, center, forward, Vector3.up, 1f, Surface);
        }

        void Place(Transform eye, float angle, float climb, Vector3 origin, Vector3 forward, Vector3 up, float turn,
                   SurfaceFinder findSurface)
        {
            if (eye == null) return;
            Vector3 direction = Quaternion.AngleAxis(angle, up) * forward;
            Vector3 right = Vector3.Cross(up, direction);
            direction = Quaternion.AngleAxis(-climb, right) * direction;
            if (!findSurface(origin, direction, out Vector3 point, out Vector3 normal)) return;

            eye.localPosition = point - normal * sink;
            // Tipped back with the surface rather than toward world up: on top of the puddle the eye looks up, its top
            // toward the middle.
            Vector3 eyeUp = Vector3.Cross(normal, right);
            eye.localRotation = Quaternion.Slerp(eye.localRotation, Quaternion.LookRotation(normal, eyeUp), turn);
        }
    }
}
