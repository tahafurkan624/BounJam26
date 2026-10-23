using System;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The goal: a marked zone the ship has to rest in. Once the ship has stayed inside, slow enough, for the dwell
    /// time it counts as delivered. It doesn't matter whether the ship is still on the blob or set down. The pad
    /// glows brighter as the dwell fills up, and leaving the zone drains it again.
    /// </summary>
    public class ShipDock : MonoBehaviour
    {
        [SerializeField] ShipBuoyancy ship;
        [Tooltip("The zone the ship's centre has to be in, m, centred on this object and rising from it.")]
        [SerializeField] Vector3 zoneSize = new Vector3(2.4f, 2f, 2.4f);
        [Tooltip("How long the ship has to stay in the zone, s.")]
        [SerializeField] float dwellTime = 1f;
        [Tooltip("The ship has to be moving slower than this to count as resting, m/s.")]
        [SerializeField] float maxShipSpeed = 1.5f;

        [Header("Look")]
        [SerializeField] Renderer padRenderer;
        [SerializeField] Color idleColor = new Color(0.2f, 0.7f, 1f);
        [SerializeField] Color deliveredColor = new Color(0.3f, 1f, 0.4f);
        [SerializeField] float idleGlow = 0.6f;
        [SerializeField] float deliveredGlow = 3f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        Material padMaterial;
        float dwell;

        /// <summary>0 = empty, 1 = delivered.</summary>
        public float Progress => dwellTime > 0f ? Mathf.Clamp01(dwell / dwellTime) : 1f;
        public bool IsDelivered { get; private set; }
        public event Action Delivered;

        void Awake()
        {
            if (ship == null) ship = FindAnyObjectByType<ShipBuoyancy>();
            if (padRenderer != null) padMaterial = padRenderer.material;
            ApplyLook();
        }

        void OnDestroy()
        {
            if (padMaterial != null) Destroy(padMaterial);
        }

        void FixedUpdate()
        {
            if (IsDelivered || ship == null) return;

            bool resting = ShipInZone() && ship.Body.linearVelocity.magnitude < maxShipSpeed;
            dwell = resting ? dwell + Time.fixedDeltaTime : Mathf.Max(0f, dwell - 2f * Time.fixedDeltaTime);
            ApplyLook();

            if (dwell >= dwellTime)
            {
                IsDelivered = true;
                Delivered?.Invoke();
            }
        }

        bool ShipInZone()
        {
            Vector3 local = transform.InverseTransformPoint(ship.Body.worldCenterOfMass);
            return Mathf.Abs(local.x) <= zoneSize.x * 0.5f
                && Mathf.Abs(local.z) <= zoneSize.z * 0.5f
                && local.y >= -0.2f && local.y <= zoneSize.y;
        }

        void ApplyLook()
        {
            if (padMaterial == null) return;
            Color color = Color.Lerp(idleColor, deliveredColor, Progress);
            padMaterial.SetColor(BaseColor, color);
            padMaterial.SetColor(EmissionColor, color * Mathf.Lerp(idleGlow, deliveredGlow, Progress));
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * (zoneSize.y * 0.5f), zoneSize);
        }
    }
}
