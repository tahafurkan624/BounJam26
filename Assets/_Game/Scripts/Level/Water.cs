using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A body of water: the box of this object's transform (a unit cube moved and scaled; turning it about the
    /// vertical axis is fine), with its surface at the top face. It has no collider, so nothing bumps into it;
    /// whatever cares about water asks <see cref="TryGetDepth"/>. The liquid blob dissolves in it, the frozen blob
    /// floats and swims (both in <see cref="BlobWater"/>), and the paper ship gets soaked, which loses the level.
    /// Every box is also shared with the water shader, which drops the faces where two boxes touch so bodies of water
    /// laid side by side draw as one (in edit mode too, hence ExecuteAlways).
    /// </summary>
    [ExecuteAlways]
    public class Water : MonoBehaviour
    {
        [Tooltip("The ship is lost once its centre of mass is under the surface: the paper soaks through.")]
        [SerializeField] bool soaksShip = true;
        [SerializeField] string shipMessage = "The ship got soaked!";

        // Matches MAX_WATER_BOXES in Water.shader.
        const int MaxShaderBoxes = 64;
        static readonly int BoxesId = Shader.PropertyToID("_WaterBoxes");
        static readonly int BoxCountId = Shader.PropertyToID("_WaterBoxCount");
        static readonly Matrix4x4[] shaderBoxes = new Matrix4x4[MaxShaderBoxes];

        static readonly List<Water> active = new List<Water>();

        LevelManager level;
        ShipBuoyancy ship;

        /// <summary>World height of the water surface.</summary>
        public float SurfaceHeight => transform.TransformPoint(0f, 0.5f, 0f).y;

        // Fast play mode keeps statics between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearRegistry() => active.Clear();

        /// <summary>
        /// How deep a point is in water, m: positive under the surface, negative above it. False when the point isn't
        /// over any water, or is below its bottom. Where bodies of water overlap, the deepest counts.
        /// </summary>
        public static bool TryGetDepth(Vector3 point, out float depth)
        {
            depth = float.NegativeInfinity;
            foreach (Water water in active)
            {
                Vector3 local = water.transform.InverseTransformPoint(point);
                if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.z) > 0.5f || local.y < -0.5f) continue;
                depth = Mathf.Max(depth, water.SurfaceHeight - point.y);
            }
            return !float.IsNegativeInfinity(depth);
        }

        void Awake()
        {
            level = FindAnyObjectByType<LevelManager>();
            ship = FindAnyObjectByType<ShipBuoyancy>();
        }

        void OnEnable()
        {
            active.Add(this);
            ShareBoxes();
        }

        void OnDisable()
        {
            active.Remove(this);
            ShareBoxes();
        }

        void LateUpdate()
        {
            if (!transform.hasChanged) return;
            transform.hasChanged = false;
            ShareBoxes();
        }

        static void ShareBoxes()
        {
            int count = Mathf.Min(active.Count, MaxShaderBoxes);
            for (int i = 0; i < count; i++) shaderBoxes[i] = active[i].transform.worldToLocalMatrix;
            Shader.SetGlobalMatrixArray(BoxesId, shaderBoxes);
            Shader.SetGlobalFloat(BoxCountId, count);
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying || !soaksShip || ship == null || level == null) return;
            Vector3 local = transform.InverseTransformPoint(ship.Body.worldCenterOfMass);
            if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f && local.y >= -0.5f && local.y < 0.5f)
                level.Fail(shipMessage);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
