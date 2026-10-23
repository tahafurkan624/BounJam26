using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Unit icosphere with shared vertices (evenly spread points, no poles), plus per-vertex neighbour lists.
    /// Subdivisions: 0 = 12 points, 1 = 42, 2 = 162, 3 = 642. Triangles face outward in Unity's winding.
    /// </summary>
    public static class IcoSphere
    {
        public static void Build(int subdivisions, out Vector3[] vertices, out int[] triangles)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var points = new List<Vector3>
            {
                new Vector3(-1f, t, 0f), new Vector3(1f, t, 0f), new Vector3(-1f, -t, 0f), new Vector3(1f, -t, 0f),
                new Vector3(0f, -1f, t), new Vector3(0f, 1f, t), new Vector3(0f, -1f, -t), new Vector3(0f, 1f, -t),
                new Vector3(t, 0f, -1f), new Vector3(t, 0f, 1f), new Vector3(-t, 0f, -1f), new Vector3(-t, 0f, 1f),
            };
            for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;

            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int s = 0; s < subdivisions; s++)
            {
                var midpoints = new Dictionary<long, int>();
                var next = new List<int>(faces.Count * 4);
                for (int f = 0; f < faces.Count; f += 3)
                {
                    int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                    int ab = Midpoint(a, b, points, midpoints);
                    int bc = Midpoint(b, c, points, midpoints);
                    int ca = Midpoint(c, a, points, midpoints);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = next;
            }

            vertices = points.ToArray();
            triangles = faces.ToArray();
        }

        public static int[][] Neighbours(int vertexCount, int[] triangles)
        {
            var sets = new HashSet<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++) sets[i] = new HashSet<int>();
            for (int f = 0; f < triangles.Length; f += 3)
            {
                int a = triangles[f], b = triangles[f + 1], c = triangles[f + 2];
                sets[a].Add(b); sets[a].Add(c);
                sets[b].Add(a); sets[b].Add(c);
                sets[c].Add(a); sets[c].Add(b);
            }

            var result = new int[vertexCount][];
            for (int i = 0; i < vertexCount; i++)
            {
                result[i] = new int[sets[i].Count];
                sets[i].CopyTo(result[i]);
            }
            return result;
        }

        /// <summary>Signed volume enclosed by a closed triangle mesh.</summary>
        public static float Volume(Vector3[] vertices, int[] triangles)
        {
            float sum = 0f;
            for (int f = 0; f < triangles.Length; f += 3)
                sum += Vector3.Dot(vertices[triangles[f]], Vector3.Cross(vertices[triangles[f + 1]], vertices[triangles[f + 2]]));
            return sum / 6f;
        }

        static int Midpoint(int a, int b, List<Vector3> points, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int index)) return index;
            index = points.Count;
            points.Add(((points[a] + points[b]) * 0.5f).normalized);
            cache.Add(key, index);
            return index;
        }
    }
}
