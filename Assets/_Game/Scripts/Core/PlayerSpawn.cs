using UnityEngine;

namespace BounJam.Core
{
    /// <summary>Where the player starts the level, facing this transform's forward. One per level.</summary>
    [DisallowMultipleComponent]
    public class PlayerSpawn : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Vector3 chest = transform.position + Vector3.up * 0.5f;
            Gizmos.color = new Color(0.3f, 0.7f, 1f);
            Gizmos.DrawWireSphere(chest, 0.5f);
            Gizmos.DrawLine(chest, chest + transform.forward);
        }
    }
}
