using UnityEngine;

namespace BounJam.Core
{
    /// <summary>
    /// Spawns the player rig (beaver, cameras and HUD) here when the level starts, facing this
    /// transform's forward. Place exactly one in each level.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerSpawn : MonoBehaviour
    {
        [SerializeField] GameObject playerRig;

        void Awake()
        {
            if (playerRig == null)
            {
                Debug.LogError("PlayerSpawn has no player rig prefab assigned.", this);
                return;
            }
            Instantiate(playerRig, transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        }

        void OnDrawGizmos()
        {
            Vector3 chest = transform.position + Vector3.up * 0.5f;
            Gizmos.color = new Color(0.3f, 0.7f, 1f);
            Gizmos.DrawWireSphere(chest, 0.5f);
            Gizmos.DrawLine(chest, chest + transform.forward);
        }
    }
}
