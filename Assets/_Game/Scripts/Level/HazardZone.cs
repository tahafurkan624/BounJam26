using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A trigger that loses the level when the ship (or, if enabled, the blob) touches it, e.g. a hot plate that
    /// burns paper. The blob can cross it safely by default, so the danger is dropping the ship there.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HazardZone : MonoBehaviour
    {
        [SerializeField] bool losesShip = true;
        [SerializeField] string shipMessage = "The ship burned!";
        [SerializeField] bool losesBlob;
        [SerializeField] string blobMessage = "Ouch!";

        LevelManager level;

        void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            level = FindAnyObjectByType<LevelManager>();
        }

        void OnTriggerEnter(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (body == null || level == null) return;
            if (losesShip && body.TryGetComponent(out ShipBuoyancy _)) level.Fail(shipMessage);
            else if (losesBlob && body.TryGetComponent(out BlobMotor _)) level.Fail(blobMessage);
        }
    }
}
