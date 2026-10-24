using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A soil bed: the one place a seed can be planted. Its flower lift waits switched off under it until a dropped seed
    /// lands on the bed, then sprouts (see <see cref="Seed"/> and <see cref="FlowerLift"/>). One flower per bed. Turn the
    /// bed so its forward (blue) axis points at the upper tile: that's where the flower's petal ramps unfold.
    /// </summary>
    public class Soil : MonoBehaviour
    {
        // The open flower's leaf is about this high above the bed: level with a tile one step up, m.
        const float LeafHeight = 3f;

        [SerializeField] FlowerLift flower;
        [Tooltip("The flower is already grown when the level starts, waiting at the bottom; no seed needed.")]
        [SerializeField] bool startGrown;

        public bool IsPlanted { get; private set; }

        void Awake()
        {
            if (flower == null) return;
            IsPlanted = startGrown;
            flower.gameObject.SetActive(startGrown);
            if (startGrown) flower.Grow(sprout: false);
        }

        /// <summary>Plants a seed here, unless the bed already has a flower. True if it was planted.</summary>
        public bool TryPlant()
        {
            if (IsPlanted || flower == null) return false;
            IsPlanted = true;
            flower.gameObject.SetActive(true);
            flower.Grow(sprout: true);
            return true;
        }

        void OnDrawGizmosSelected()
        {
            // Up to where the leaf stops, then toward the side the ramps unfold to.
            Vector3 top = transform.position + transform.up * LeafHeight;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, top);
            Gizmos.DrawLine(top, top + transform.forward * 1.5f);
        }
    }
}
