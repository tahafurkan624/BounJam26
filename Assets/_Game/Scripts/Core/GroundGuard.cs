using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Hard safety net: a body never ends up inside or under the floor it was standing on. Every physics step it
    /// remembers the surface under it. If the body's underside, straight below its centre of mass, is ever found
    /// below that surface (pressed into it, squeezed through it, pushed out of its underside), the body is put back
    /// on top and stops falling. Walking or rolling off an edge is untouched: past the edge the remembered surface
    /// isn't under the body any more, so there's nothing to be put back onto.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GroundGuard : MonoBehaviour
    {
        [Tooltip("Surfaces that count as floor (the static level and moving platforms).")]
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("How far the underside may sink below the floor before it's put back, m. Above a hard landing's one-step dip, below any real sinking.")]
        [SerializeField] float tolerance = 0.15f;
        [Tooltip("How long a floor is remembered after last standing on it, s; covers being squeezed through it.")]
        [SerializeField] float memory = 1f;

        // Small gap left between the floor and the underside after a rescue, m.
        const float Clearance = 0.01f;

        Rigidbody body;
        Collider[] colliders;
        Collider lastGround;
        float lastGroundTime = float.NegativeInfinity;

        /// <summary>How many times the body has been put back on the floor (for testing).</summary>
        public int Rescues { get; private set; }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>(true);
        }

        void FixedUpdate()
        {
            Vector3 center = body.worldCenterOfMass;
            if (!TryGetLowestPoint(out float lowest)) return;

            // Remember the floor we're standing on. (From inside a sunk floor this ray can't see it; the memory
            // keeps it known for the rescue below.)
            float reach = center.y - lowest + 0.15f;
            if (Physics.Raycast(center, Vector3.down, out RaycastHit standing, reach, groundMask, QueryTriggerInteraction.Ignore)
                && standing.normal.y > 0.5f)
            {
                lastGround = standing.collider;
                lastGroundTime = Time.time;
            }

            if (lastGround == null || !lastGround.enabled || Time.time - lastGroundTime > memory) return;

            // Top of that floor straight above or below the centre; no hit means we're past its edge.
            Bounds floor = lastGround.bounds;
            var down = new Ray(new Vector3(center.x, floor.max.y + 1f, center.z), Vector3.down);
            if (!lastGround.Raycast(down, out RaycastHit top, floor.size.y + 2f)) return;

            // Our own underside in the same column, found by looking up from well below.
            var up = new Ray(new Vector3(center.x, top.point.y - 5f, center.z), Vector3.up);
            float underside = float.PositiveInfinity;
            foreach (Collider own in colliders)
                if (own != null && own.enabled && !own.isTrigger && own.Raycast(up, out RaycastHit hit, 10f))
                    underside = Mathf.Min(underside, hit.point.y);
            if (float.IsPositiveInfinity(underside)) return;

            float sink = top.point.y - underside;
            if (sink <= tolerance) return;

            Vector3 lift = Vector3.up * (sink + Clearance);
            body.position += lift;
            transform.position += lift;
            Vector3 velocity = body.linearVelocity;
            if (velocity.y < 0f) body.linearVelocity = new Vector3(velocity.x, 0f, velocity.z);
            Rescues++;
        }

        bool TryGetLowestPoint(out float lowest)
        {
            lowest = float.PositiveInfinity;
            foreach (Collider own in colliders)
                if (own != null && own.enabled && !own.isTrigger)
                    lowest = Mathf.Min(lowest, own.bounds.min.y);
            return !float.IsPositiveInfinity(lowest);
        }
    }
}
