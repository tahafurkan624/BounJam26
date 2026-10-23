using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Marks a collectible as iron. While the blob carries one, active magnets pull it (see <see cref="Magnet"/>).
    /// </summary>
    [RequireComponent(typeof(Collectible))]
    public class IronBall : MonoBehaviour
    {
    }
}
