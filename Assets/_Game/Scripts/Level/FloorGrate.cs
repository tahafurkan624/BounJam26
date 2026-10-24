using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A drain under a floor grate: the box of this object's transform (a unit cube moved and scaled; turning it about
    /// the vertical axis is fine), with the top of the grate at its top face. The grate is a LiquidPassable plate over
    /// it, so the frozen blob, the ship and crates cross on it, but the liquid drips straight through: its jelly can't
    /// span the drain either (<see cref="IsOver"/>), and a piece of the blob that sinks into it is lost down it, which
    /// loses the level. Like <see cref="Water"/> it has no collider of its own.
    /// </summary>
    public class FloorGrate : MonoBehaviour
    {
        [SerializeField] string blobMessage = "Down the drain!";
        [Tooltip("How far below the top of the grate the middle of a piece of the blob has to sink before it's lost, m.")]
        [SerializeField] float lostDepth = 0.4f;

        static readonly List<FloorGrate> active = new List<FloorGrate>();

        LevelManager level;

        /// <summary>World height of the top of the grate.</summary>
        public float TopHeight => transform.TransformPoint(0f, 0.5f, 0f).y;

        // Fast play mode keeps statics between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearRegistry() => active.Clear();

        /// <summary>
        /// Is the point over a drain, at most <paramref name="height"/> above its grate, or down in it? The liquid's jelly
        /// finds nothing to hold on to there.
        /// </summary>
        public static bool IsOver(Vector3 point, float height)
        {
            foreach (FloorGrate grate in active)
                if (grate.Contains(point) && point.y <= grate.TopHeight + height) return true;
            return false;
        }

        void Awake() => level = FindAnyObjectByType<LevelManager>();

        void OnEnable() => active.Add(this);

        void OnDisable() => active.Remove(this);

        void FixedUpdate()
        {
            if (level == null || level.Current != LevelManager.State.Playing) return;
            IReadOnlyList<BlobSplit> pieces = BlobSplit.All;
            for (int i = 0; i < pieces.Count; i++)
            {
                // A pipe carries its piece along wherever the pipe runs.
                if (pieces[i].InPipe) continue;
                Vector3 middle = pieces[i].Body.worldCenterOfMass;
                if (Contains(middle) && middle.y < TopHeight - lostDepth)
                {
                    level.Fail(blobMessage);
                    return;
                }
            }
        }

        /// <summary>Is the point inside the drain's footprint, no lower than its bottom?</summary>
        bool Contains(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f && local.y >= -0.5f;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.9f, 0.4f, 0.2f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
