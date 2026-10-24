using System.Collections;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Marks a collectible as a seed. Carried in the crystal and dropped (by melting) onto a soil bed, it plants itself:
    /// it sinks into the soil and the bed's flower lift grows (see <see cref="Soil"/>). Dropped anywhere else, or on a
    /// bed that already has a flower, it just rests there like any other item.
    /// </summary>
    [RequireComponent(typeof(Collectible))]
    public class Seed : MonoBehaviour
    {
        [Tooltip("How long it takes to sink into the soil once planted, s.")]
        [SerializeField] float sinkTime = 0.6f;
        [Tooltip("How far it sinks while it shrinks away, m.")]
        [SerializeField] float sinkDepth = 0.12f;

        Collectible collectible;

        void Awake() => collectible = GetComponent<Collectible>();

        void OnEnable() => collectible.Landed += OnLanded;

        void OnDisable() => collectible.Landed -= OnLanded;

        void OnLanded(Collectible _, Collider ground)
        {
            Soil soil = ground.GetComponentInParent<Soil>();
            if (soil == null || !soil.TryPlant()) return;
            // Off the waiting list, so the blob can't catch it again while it sinks.
            collectible.enabled = false;
            StartCoroutine(Sink());
        }

        IEnumerator Sink()
        {
            Vector3 start = transform.position;
            Vector3 scale = transform.localScale;
            for (float t = 0f; t < sinkTime; t += Time.deltaTime)
            {
                float k = t / sinkTime;
                transform.position = start + Vector3.down * (sinkDepth * k);
                transform.localScale = scale * (1f - k * k);
                yield return null;
            }
            gameObject.SetActive(false);
        }
    }
}
