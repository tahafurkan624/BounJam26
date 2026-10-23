using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A crate only the solid form can push. It stays locked in place horizontally (it can still fall) unless the
    /// solid blob, or another crate that is itself being pushed, is pressing on it; the liquid just squishes
    /// against it. Rotation is always locked so crates slide upright and line up cleanly on buttons.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Pushable : MonoBehaviour
    {
        [Tooltip("How long the crate stays free after the last push, s. Smooths over contact flicker.")]
        [SerializeField] float pushGrace = 0.15f;

        const RigidbodyConstraints Locked = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        const RigidbodyConstraints Free = RigidbodyConstraints.FreezeRotation;

        Rigidbody body;
        int solidLayer;
        float freeUntil = float.NegativeInfinity;

        /// <summary>True while something solid is pushing it (it can slide).</summary>
        public bool IsFree { get; private set; }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            solidLayer = LayerMask.NameToLayer("PlayerSolid");
            SetFree(false);
        }

        void FixedUpdate()
        {
            bool free = Time.time < freeUntil;
            if (free != IsFree) SetFree(free);
        }

        void OnCollisionStay(Collision collision)
        {
            bool pushedBySolid = collision.gameObject.layer == solidLayer;
            bool pushedByCrate = collision.rigidbody != null
                                 && collision.rigidbody.TryGetComponent(out Pushable other) && other.IsFree;
            if (pushedBySolid || pushedByCrate) freeUntil = Time.time + pushGrace;
        }

        void SetFree(bool free)
        {
            IsFree = free;
            body.constraints = free ? Free : Locked;
            if (free) body.WakeUp();
        }
    }
}
