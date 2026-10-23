using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Loop subdivision of a closed triangle mesh, precomputed as fixed weights: every vertex of the smooth mesh is
    /// a weighted sum of the original vertices. Build once, then Apply each frame to turn the coarse simulated skin
    /// into a smooth surface for a handful of multiply-adds per vertex.
    /// </summary>
    public sealed class LoopSubdivision
    {
        public int VertexCount { get; private set; }
        public int[] Triangles { get; private set; }

        // Compressed rows: the weights of vertex v are entries start[v] .. start[v + 1] - 1.
        int[] start;
        int[] source;
        float[] weight;

        public static LoopSubdivision Build(int vertexCount, int[] triangles, int levels)
        {
            var weights = new List<Dictionary<int, float>>(vertexCount);
            for (int v = 0; v < vertexCount; v++) weights.Add(new Dictionary<int, float> { { v, 1f } });

            int[] faces = triangles;
            for (int level = 0; level < levels; level++)
                faces = Subdivide(weights, faces);

            var result = new LoopSubdivision { VertexCount = weights.Count, Triangles = faces };
            result.Compress(weights);
            return result;
        }

        public void Apply(Vector3[] coarse, Vector3[] smooth)
        {
            for (int v = 0; v < VertexCount; v++)
            {
                Vector3 sum = Vector3.zero;
                for (int k = start[v]; k < start[v + 1]; k++) sum += coarse[source[k]] * weight[k];
                smooth[v] = sum;
            }
        }

        static int[] Subdivide(List<Dictionary<int, float>> weights, int[] faces)
        {
            int count = weights.Count;
            var neighbours = new HashSet<int>[count];
            for (int v = 0; v < count; v++) neighbours[v] = new HashSet<int>();
            var opposite = new Dictionary<long, List<int>>();
            for (int f = 0; f < faces.Length; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                neighbours[a].Add(b); neighbours[a].Add(c);
                neighbours[b].Add(a); neighbours[b].Add(c);
                neighbours[c].Add(a); neighbours[c].Add(b);
                AddOpposite(opposite, a, b, c);
                AddOpposite(opposite, b, c, a);
                AddOpposite(opposite, c, a, b);
            }

            // Existing vertices move toward their neighbours.
            var next = new List<Dictionary<int, float>>(count * 4);
            for (int v = 0; v < count; v++)
            {
                int valence = neighbours[v].Count;
                float beta = valence == 3 ? 3f / 16f : 3f / (8f * valence);
                var w = new Dictionary<int, float>();
                AddScaled(w, weights[v], 1f - valence * beta);
                foreach (int n in neighbours[v]) AddScaled(w, weights[n], beta);
                next.Add(w);
            }

            // One new vertex per edge: 3/8 of each end, 1/8 of each opposite corner.
            var edgeVertex = new Dictionary<long, int>();
            int EdgeVertex(int a, int b)
            {
                long key = EdgeKey(a, b);
                if (edgeVertex.TryGetValue(key, out int index)) return index;
                var w = new Dictionary<int, float>();
                AddScaled(w, weights[a], 3f / 8f);
                AddScaled(w, weights[b], 3f / 8f);
                foreach (int o in opposite[key]) AddScaled(w, weights[o], 1f / 8f);
                index = next.Count;
                next.Add(w);
                edgeVertex.Add(key, index);
                return index;
            }

            var result = new int[faces.Length * 4];
            for (int f = 0, r = 0; f < faces.Length; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = EdgeVertex(a, b), bc = EdgeVertex(b, c), ca = EdgeVertex(c, a);
                result[r++] = a; result[r++] = ab; result[r++] = ca;
                result[r++] = b; result[r++] = bc; result[r++] = ab;
                result[r++] = c; result[r++] = ca; result[r++] = bc;
                result[r++] = ab; result[r++] = bc; result[r++] = ca;
            }

            weights.Clear();
            weights.AddRange(next);
            return result;
        }

        void Compress(List<Dictionary<int, float>> weights)
        {
            int total = 0;
            foreach (var w in weights) total += w.Count;
            start = new int[weights.Count + 1];
            source = new int[total];
            weight = new float[total];

            int k = 0;
            for (int v = 0; v < weights.Count; v++)
            {
                start[v] = k;
                foreach (var pair in weights[v])
                {
                    source[k] = pair.Key;
                    weight[k] = pair.Value;
                    k++;
                }
            }
            start[weights.Count] = k;
        }

        static void AddOpposite(Dictionary<long, List<int>> opposite, int a, int b, int corner)
        {
            long key = EdgeKey(a, b);
            if (!opposite.TryGetValue(key, out List<int> list)) opposite.Add(key, list = new List<int>(2));
            list.Add(corner);
        }

        static void AddScaled(Dictionary<int, float> target, Dictionary<int, float> source, float scale)
        {
            foreach (var pair in source)
            {
                target.TryGetValue(pair.Key, out float existing);
                target[pair.Key] = existing + pair.Value * scale;
            }
        }

        static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    }
}
