using System;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The frozen blob's shape, for building the crystal's colliders: the skin's points joined up at a coarser detail
    /// into a closed mesh (the first 162 skin points are the level-2 icosphere, just as the first 42 are level 1).
    /// Colliders on a moving body have to be convex, and one convex collider around the whole shape fills in every dent
    /// the jelly took around a post or over an edge. So where that would cut into the level, <see cref="BlobForm"/> cuts
    /// the crystal into boxes: <see cref="Clip"/> gives the corners of the part of the shape inside a box, and their
    /// convex hull is that part, with only the dents inside the box filled in.
    /// </summary>
    public class FrozenShape
    {
        // Finer than level 2 gives each piece more points than a convex collider takes, and is slower to clip.
        const int MaxSubdivisions = 2;
        // Points this close outside a box still count as on it, m.
        const float Epsilon = 1e-5f;

        readonly int[] triangles;
        // Every edge of the mesh once, as pairs of point indices.
        readonly int[] edges;
        readonly Vector3[] points;
        // Bounds of each triangle, to skip the ones nowhere near quickly.
        readonly Vector3[] triangleMin;
        readonly Vector3[] triangleMax;

        /// <summary>How many of the skin's points the shape is made of (its first ones).</summary>
        public int PointCount => points.Length;
        /// <summary>Bounds of the shape, in the space of the points it was given.</summary>
        public Bounds Bounds { get; private set; }

        /// <summary>For a skin of <paramref name="skinPointCount"/> points, at any icosphere detail from level 1 up.</summary>
        public FrozenShape(int skinPointCount)
        {
            int subdivisions = 0;
            while (subdivisions < MaxSubdivisions && IcoPointCount(subdivisions + 1) <= skinPointCount) subdivisions++;
            IcoSphere.Build(subdivisions, out Vector3[] directions, out triangles);
            points = new Vector3[directions.Length];
            triangleMin = new Vector3[triangles.Length / 3];
            triangleMax = new Vector3[triangles.Length / 3];

            // The mesh is closed and faces outward, so every edge runs one way in one of its two triangles and the
            // other way in the other: taking each the way its lower index comes first finds it exactly once.
            var found = new List<int>(triangles.Length);
            for (int f = 0; f < triangles.Length; f += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = triangles[f + k], b = triangles[f + (k + 1) % 3];
                if (a >= b) continue;
                found.Add(a);
                found.Add(b);
            }
            edges = found.ToArray();
        }

        static int IcoPointCount(int subdivisions) => 10 * (1 << (2 * subdivisions)) + 2;

        /// <summary>Takes the shape from the skin's points (only the first <see cref="PointCount"/> are used).</summary>
        public void SetPoints(Vector3[] skinPoints)
        {
            Array.Copy(skinPoints, points, points.Length);
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 p in points) bounds.Encapsulate(p);
            Bounds = bounds;

            for (int t = 0, f = 0; f < triangles.Length; t++, f += 3)
            {
                Vector3 a = points[triangles[f]], b = points[triangles[f + 1]], c = points[triangles[f + 2]];
                triangleMin[t] = Vector3.Min(a, Vector3.Min(b, c));
                triangleMax[t] = Vector3.Max(a, Vector3.Max(b, c));
            }
        }

        /// <summary>
        /// Is the point inside the shape? A ray straight up from inside a closed surface crosses it an odd number of
        /// times, so this counts the triangles above the point.
        /// </summary>
        public bool Contains(Vector3 point)
        {
            // Nudged a fraction of a millimetre, so the ray can't run exactly through a point or along an edge.
            point.x += 1.3e-4f;
            point.z += 0.7e-4f;

            int crossings = 0;
            for (int t = 0, f = 0; f < triangles.Length; t++, f += 3)
            {
                Vector3 low = triangleMin[t], high = triangleMax[t];
                if (point.x < low.x || point.x > high.x || point.z < low.z || point.z > high.z || point.y > high.y) continue;
                Vector3 a = points[triangles[f]], b = points[triangles[f + 1]], c = points[triangles[f + 2]];
                if (Footprint(a, b, c, point, out float wa, out float wb, out float wc)
                    && wa * a.y + wb * b.y + wc * c.y > point.y)
                    crossings++;
            }
            return (crossings & 1) == 1;
        }

        /// <summary>
        /// The corners of the part of the shape inside <paramref name="box"/>: the shape's points inside it, where its
        /// edges cross the box's faces, the box's corners inside the shape and where the box's edges cross its surface.
        /// The convex hull of these is that part of the shape, with only its own dents filled in.
        /// </summary>
        public void Clip(Bounds box, List<Vector3> result)
        {
            result.Clear();
            Vector3 min = box.min, max = box.max;

            foreach (Vector3 p in points)
                if (Inside(p, min, max)) result.Add(p);

            for (int e = 0; e < edges.Length; e += 2)
            {
                Vector3 a = points[edges[e]], b = points[edges[e + 1]];
                for (int axis = 0; axis < 3; axis++)
                {
                    AddCrossing(a, b, axis, min[axis], min, max, result);
                    AddCrossing(a, b, axis, max[axis], min, max, result);
                }
            }

            for (int corner = 0; corner < 8; corner++)
            {
                var p = new Vector3((corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y,
                                    (corner & 4) == 0 ? min.z : max.z);
                if (Contains(p)) result.Add(p);
            }

            // The box's twelve edges: four along each axis, at each pair of sides across the other two.
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int side = 0; side < 4; side++)
                {
                    Vector3 from = min;
                    from[u] = (side & 1) == 0 ? min[u] : max[u];
                    from[v] = (side & 2) == 0 ? min[v] : max[v];
                    Vector3 to = from;
                    to[axis] = max[axis];
                    for (int t = 0, f = 0; f < triangles.Length; t++, f += 3)
                    {
                        Vector3 low = triangleMin[t], high = triangleMax[t];
                        if (from[u] < low[u] || from[u] > high[u] || from[v] < low[v] || from[v] > high[v]
                            || max[axis] < low[axis] || min[axis] > high[axis])
                            continue;
                        if (Crosses(from, to, points[triangles[f]], points[triangles[f + 1]], points[triangles[f + 2]],
                                    out Vector3 crossing))
                            result.Add(crossing);
                    }
                }
            }
        }

        /// <summary>
        /// Where to cut a piece (<paramref name="part"/>, the bounds of the shape inside it) that cuts into something
        /// (<paramref name="obstacle"/>, its bounds), both in the shape's space. First choice is along a side of the
        /// obstacle's bounds: everything beyond it is clear of the obstacle, so the side that clears the most. Else
        /// through the obstacle's middle, which parts the jelly on either side of a post, or through the piece's
        /// middle: across the obstacle's thinnest side first and never along its length, where both halves would still
        /// wrap around it. Neither half may be thinner than <paramref name="minSize"/>; false when there's no such cut.
        /// </summary>
        public static bool ChooseCut(Bounds part, Bounds obstacle, float minSize, out int axis, out float at)
        {
            axis = -1;
            at = 0f;
            float cleared = 0f;
            for (int a = 0; a < 3; a++)
            {
                float low = part.min[a] + minSize, high = part.max[a] - minSize;
                float face = obstacle.min[a];
                if (face >= low && face <= high && face - part.min[a] > cleared)
                {
                    axis = a;
                    at = face;
                    cleared = face - part.min[a];
                }
                face = obstacle.max[a];
                if (face >= low && face <= high && part.max[a] - face > cleared)
                {
                    axis = a;
                    at = face;
                    cleared = part.max[a] - face;
                }
            }
            if (axis >= 0) return true;

            // Thinnest side of the obstacle first; between sides about as thin, where the piece is longest.
            Vector3 size = obstacle.size;
            var order = new[] { 0, 1, 2 };
            Array.Sort(order, (a, b) => Mathf.Abs(size[a] - size[b]) > 0.01f ? size[a].CompareTo(size[b])
                                                                               : part.size[b].CompareTo(part.size[a]));
            float thinnest = size[order[0]];
            foreach (int a in order)
            {
                float low = part.min[a] + minSize, high = part.max[a] - minSize;
                if (size[a] > thinnest * 2f + minSize) break;
                if (low > high) continue;
                axis = a;
                float middle = obstacle.center[a];
                at = middle >= low && middle <= high ? middle : part.center[a];
                return true;
            }
            return false;
        }

        static bool Inside(Vector3 p, Vector3 min, Vector3 max)
            => p.x >= min.x - Epsilon && p.x <= max.x + Epsilon
               && p.y >= min.y - Epsilon && p.y <= max.y + Epsilon
               && p.z >= min.z - Epsilon && p.z <= max.z + Epsilon;

        /// <summary>Adds where the segment from a to b crosses the plane at <paramref name="face"/> along the axis, if inside the box.</summary>
        static void AddCrossing(Vector3 a, Vector3 b, int axis, float face, Vector3 min, Vector3 max, List<Vector3> result)
        {
            float da = a[axis] - face, db = b[axis] - face;
            if ((da < 0f) == (db < 0f)) return;
            Vector3 p = Vector3.Lerp(a, b, da / (da - db));
            p[axis] = face;
            if (Inside(p, min, max)) result.Add(p);
        }

        /// <summary>Where the segment from <paramref name="from"/> to <paramref name="to"/> crosses the triangle, if it does.</summary>
        static bool Crosses(Vector3 from, Vector3 to, Vector3 a, Vector3 b, Vector3 c, out Vector3 point)
        {
            point = default;
            Vector3 direction = to - from;
            Vector3 ab = b - a, ac = c - a;
            Vector3 p = Vector3.Cross(direction, ac);
            float determinant = Vector3.Dot(ab, p);
            if (Mathf.Abs(determinant) < 1e-12f) return false;
            float inverse = 1f / determinant;
            Vector3 fromA = from - a;
            float u = Vector3.Dot(fromA, p) * inverse;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(fromA, ab);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            float t = Vector3.Dot(ac, q) * inverse;
            if (t < 0f || t > 1f) return false;
            point = from + direction * t;
            return true;
        }

        /// <summary>Barycentric weights of the point's x and z in the triangle's footprint; false when outside it.</summary>
        static bool Footprint(Vector3 a, Vector3 b, Vector3 c, Vector3 point, out float wa, out float wb, out float wc)
        {
            wa = wb = wc = 0f;
            float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < 1e-8f) return false;
            wa = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / denominator;
            wb = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / denominator;
            wc = 1f - wa - wb;
            return wa >= 0f && wb >= 0f && wc >= 0f;
        }
    }
}
