using Unity.AI.Navigation;
using UnityEngine;

namespace BounJam.Level
{
    /// <summary>
    /// Bakes the level's nav mesh from its colliders when the level loads, so levels never need a manual
    /// bake. Fire Souls switch their agents on in Start, after this has run.
    /// </summary>
    [RequireComponent(typeof(NavMeshSurface))]
    public class RuntimeNavMesh : MonoBehaviour
    {
        void Awake() => GetComponent<NavMeshSurface>().BuildNavMesh();
    }
}
