using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A drop of water waiting in the level. The liquid blob soaks it up by flowing over it: the drop is drawn into the
    /// jelly and the blob keeps its water for the rest of the level, so it stretches farther and spans wider gaps (see
    /// <see cref="BlobSize"/>). The crystal can't take it in; it has to melt first. Until then the drop hovers, bobbing
    /// and turning, over a glowing floor marker.
    /// This object is the drop's centre; the model and marker are children.
    /// </summary>
    public class WaterDroplet : MonoBehaviour
    {
        [Tooltip("Water it adds to the blob, as a share of the blob the level starts with.")]
        [SerializeField] float volume = 0.3f;
        [Tooltip("Radius of the drop, m. It's soaked up once the jelly covers its centre or its underside.")]
        [SerializeField] float radius = 0.15f;
        [Tooltip("The drop's model: bobs and turns, and shrinks away into the blob.")]
        [SerializeField] Transform model;
        [Tooltip("Floor marker shown while the drop waits.")]
        [SerializeField] Transform marker;

        [Header("Look")]
        [Tooltip("How far the drop bobs up and down, m.")]
        [SerializeField] float bobHeight = 0.05f;
        [Tooltip("Bobs per second.")]
        [SerializeField] float bobRate = 0.6f;
        [Tooltip("Turning speed, degrees per second.")]
        [SerializeField] float spinRate = 60f;
        [Tooltip("Marker pulses per second.")]
        [SerializeField] float markerPulseRate = 0.7f;
        [Tooltip("How long the drop takes to be drawn into the blob, s.")]
        [SerializeField] float soakTime = 0.25f;

        Vector3 modelPosition;
        Vector3 modelScale;
        Vector3 markerScale;
        float phase;
        bool soaked;
        // The piece that soaked it up (gone if it flowed into the other half meanwhile), and where the drop flies to.
        BlobSize drinker;
        Vector3 target;
        Vector3 soakStart;
        float soakProgress;

        public float Volume => volume;
        public float Radius => radius;
        public bool IsSoaked => soaked;

        void Awake()
        {
            if (model != null)
            {
                modelPosition = model.localPosition;
                modelScale = model.localScale;
            }
            if (marker != null) markerScale = marker.localScale;
            // Drops side by side don't bob in step.
            phase = Mathf.Repeat(transform.position.x * 0.37f + transform.position.z * 0.61f, 1f);
        }

        void FixedUpdate()
        {
            if (soaked) return;
            Vector3 center = transform.position;
            Vector3 underside = center + Vector3.down * radius;
            for (int i = 0; i < BlobSize.All.Count; i++)
            {
                BlobSize piece = BlobSize.All[i];
                if (!piece.CanSoak || !(piece.Touches(center, radius) || piece.Skin.Contains(underside))) continue;
                Soak(piece);
                return;
            }
        }

        /// <summary>The water goes into the blob now, for good; the drop's model just flies in after it.</summary>
        void Soak(BlobSize piece)
        {
            soaked = true;
            drinker = piece;
            piece.Soak(volume);
            target = piece.Skin.ShapeCenter;
            soakStart = model != null ? model.position : transform.position;
            if (marker != null) marker.gameObject.SetActive(false);
        }

        void Update()
        {
            if (soaked)
            {
                FlyIn();
                return;
            }
            if (model != null)
            {
                float t = Time.time * bobRate + phase;
                model.localPosition = modelPosition + Vector3.up * (bobHeight * Mathf.Sin(t * 2f * Mathf.PI));
                model.localRotation = Quaternion.Euler(0f, (Time.time * spinRate + phase * 360f) % 360f, 0f);
            }
            if (marker != null)
                marker.localScale = markerScale * (1f + 0.08f * Mathf.Sin((Time.time * markerPulseRate + phase) * 2f * Mathf.PI));
        }

        void FlyIn()
        {
            soakProgress = Mathf.Min(1f, soakProgress + Time.deltaTime / Mathf.Max(0.01f, soakTime));
            if (drinker != null) target = drinker.Skin.ShapeCenter;
            if (model != null)
            {
                float k = soakProgress * soakProgress;
                model.position = Vector3.Lerp(soakStart, target, k);
                model.localScale = modelScale * (1f - k);
            }
            if (soakProgress >= 1f) gameObject.SetActive(false);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = soaked ? Color.gray : new Color(0.3f, 0.7f, 1f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
