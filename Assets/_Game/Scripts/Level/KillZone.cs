using BounJam.Core;
using UnityEngine;

namespace BounJam.Level
{
    /// <summary>
    /// A trigger volume, usually at the bottom of a pit: the beaver entering it fails the level, and
    /// pushable objects falling in are removed. Shown as a red box in the Scene view.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class KillZone : MonoBehaviour
    {
        [SerializeField] string failMessage = "You fell into a pit.";

        void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == GameLayers.Player)
            {
                if (LevelManager.Current != null)
                    LevelManager.Current.Fail(failMessage);
            }
            else if (other.gameObject.layer == GameLayers.Pushable && other.attachedRigidbody != null)
            {
                Destroy(other.attachedRigidbody.gameObject);
            }
        }

        void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.25f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.8f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
