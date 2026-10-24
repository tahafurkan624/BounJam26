using UnityEngine;

namespace BounJam
{
    /// <summary>Ray tests against a plain vertex and triangle list, for surfaces that have no collider.</summary>
    public static class MeshRay
    {
        /// <summary>
        /// Where a ray from inside a closed surface leaves it: the farthest crossing along <paramref name="direction"/>
        /// (normalised). The normal is the crossed triangle's, turned to point out along the ray.
        /// </summary>
        public static bool Exit(Vector3[] vertices, int[] triangles, Vector3 origin, Vector3 direction,
                                out Vector3 point, out Vector3 normal)
        {
            float farthest = -1f;
            normal = Vector3.zero;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                // Möller-Trumbore, both windings.
                Vector3 a = vertices[triangles[t]];
                Vector3 ab = vertices[triangles[t + 1]] - a, ac = vertices[triangles[t + 2]] - a;
                Vector3 p = Vector3.Cross(direction, ac);
                float determinant = Vector3.Dot(ab, p);
                if (determinant > -1e-10f && determinant < 1e-10f) continue;

                float inverse = 1f / determinant;
                Vector3 s = origin - a;
                float u = Vector3.Dot(s, p) * inverse;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, ab);
                float v = Vector3.Dot(direction, q) * inverse;
                if (v < 0f || u + v > 1f) continue;

                float distance = Vector3.Dot(ac, q) * inverse;
                if (distance <= farthest) continue;
                farthest = distance;
                normal = Vector3.Cross(ab, ac);
            }

            if (farthest < 0f)
            {
                point = origin;
                return false;
            }
            point = origin + direction * farthest;
            normal = Vector3.Dot(normal, direction) < 0f ? -normal.normalized : normal.normalized;
            return true;
        }
    }
}
