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
    /// the crystal into convex regions, each bounded by planes: <see cref="Clip"/> gives the corners of the part of the
    /// shape inside a region, and their convex hull is that part, with only the dents inside the region filled in.
    /// </summary>
    public class FrozenShape
    {
        // Finer than level 2 gives each piece more points than a convex collider takes, and is slower to clip.
        const int MaxSubdivisions = 2;
        // Points this close outside a region still count as on it, m.
        const float Epsilon = 1e-5f;
        // A region's corners closer than this are one corner, m.
        const float SameCorner = 1e-4f;

        readonly int[] triangles;
        // Every edge of the mesh once, as pairs of point indices.
        readonly int[] edges;
        readonly Vector3[] points;
        // Bounds of each triangle, to skip the ones nowhere near quickly.
        readonly Vector3[] triangleMin;
        readonly Vector3[] triangleMax;
        // The corners of the region being clipped to, and which of its sides each lies on (one bit per side).
        readonly List<Vector3> corners = new List<Vector3>();
        readonly List<ulong> cornerSides = new List<ulong>();

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
        /// The corners of the part of the shape inside <paramref name="region"/> (a convex region: the space behind all
        /// of its planes, which face out of it): the shape's points inside it, where its edges cross the region's sides,
        /// the region's corners inside the shape and where the region's edges cross its surface. The convex hull of
        /// these is that part of the shape, with only its own dents filled in.
        /// </summary>
        public void Clip(IReadOnlyList<Plane> region, List<Vector3> result)
        {
            result.Clear();

            foreach (Vector3 p in points)
                if (Inside(p, region)) result.Add(p);

            for (int e = 0; e < edges.Length; e += 2)
            {
                Vector3 a = points[edges[e]], b = points[edges[e + 1]];
                for (int s = 0; s < region.Count; s++)
                {
                    float da = region[s].GetDistanceToPoint(a), db = region[s].GetDistanceToPoint(b);
                    if ((da < 0f) == (db < 0f)) continue;
                    Vector3 p = Vector3.Lerp(a, b, da / (da - db));
                    if (Inside(p, region)) result.Add(p);
                }
            }

            FindCorners(region);
            for (int c = 0; c < corners.Count; c++)
            {
                if (Contains(corners[c])) result.Add(corners[c]);
                // The region's edges: two corners on two of the same sides.
                for (int d = c + 1; d < corners.Count; d++)
                {
                    if (CountBits(cornerSides[c] & cornerSides[d]) < 2) continue;
                    Vector3 from = corners[c], to = corners[d];
                    Vector3 low = Vector3.Min(from, to), high = Vector3.Max(from, to);
                    for (int t = 0, f = 0; f < triangles.Length; t++, f += 3)
                    {
                        Vector3 tLow = triangleMin[t], tHigh = triangleMax[t];
                        if (high.x < tLow.x || low.x > tHigh.x || high.y < tLow.y || low.y > tHigh.y
                            || high.z < tLow.z || low.z > tHigh.z)
                            continue;
                        if (Crosses(from, to, points[triangles[f]], points[triangles[f + 1]], points[triangles[f + 2]],
                                    out Vector3 crossing))
                            result.Add(crossing);
                    }
                }
            }
        }

        /// <summary>Is the point behind every one of the region's planes (or within a hair of it)?</summary>
        public static bool Inside(Vector3 point, IReadOnlyList<Plane> region)
        {
            for (int s = 0; s < region.Count; s++)
                if (region[s].GetDistanceToPoint(point) > Epsilon) return false;
            return true;
        }

        /// <summary>Adds the six sides of <paramref name="box"/> to a region.</summary>
        public static void AddBox(Bounds box, List<Plane> region)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 normal = Vector3.zero;
                normal[axis] = 1f;
                region.Add(new Plane(normal, box.max));
                region.Add(new Plane(-normal, box.min));
            }
        }

        /// <summary>
        /// Where to cut a piece (<paramref name="part"/>, its points) that cuts into an obstacle: along one of the
        /// obstacle's <paramref name="sides"/> (planes that touch it and face out of it, so everything beyond one is clear
        /// of it), the side that parts off the most. Both halves have to be at least <paramref name="minSize"/> thick;
        /// false when no side parts the piece like that.
        /// </summary>
        public static bool ChooseCut(List<Vector3> part, IReadOnlyList<Plane> sides, float minSize, out Plane cut)
        {
            cut = default;
            float best = 0f;
            bool found = false;
            foreach (Plane side in sides)
            {
                float beyond = float.NegativeInfinity, within = float.NegativeInfinity;
                foreach (Vector3 p in part)
                {
                    float distance = side.GetDistanceToPoint(p);
                    beyond = Mathf.Max(beyond, distance);
                    within = Mathf.Max(within, -distance);
                }
                if (beyond < minSize || within < minSize || beyond <= best) continue;
                best = beyond;
                cut = side;
                found = true;
            }
            return found;
        }

        /// <summary>How far the piece (its points) reaches out past the farthest of the obstacle's sides.</summary>
        public static float Overhang(List<Vector3> part, IReadOnlyList<Plane> sides)
        {
            float overhang = float.NegativeInfinity;
            foreach (Plane side in sides)
            foreach (Vector3 p in part)
                overhang = Mathf.Max(overhang, side.GetDistanceToPoint(p));
            return overhang;
        }

        /// <summary>
        /// A cut for when none along the obstacle's sides will do: through the obstacle's middle, which parts the jelly
        /// on either side of a post, or through the piece's middle (<paramref name="part"/> and
        /// <paramref name="obstacle"/> are their bounds): across the obstacle's thinnest side first and never along its
        /// length, where both halves would still wrap around it. Neither half may be thinner than
        /// <paramref name="minSize"/>; false when there's no such cut.
        /// </summary>
        public static bool MiddleCut(Bounds part, Bounds obstacle, float minSize, out Plane cut)
        {
            cut = default;
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
                float middle = obstacle.center[a];
                Vector3 normal = Vector3.zero;
                normal[a] = 1f;
                cut = new Plane(normal, -(middle >= low && middle <= high ? middle : part.center[a]));
                return true;
            }
            return false;
        }

        /// <summary>
        /// The region's corners: where three of its planes meet, inside all the others. Where more than three meet (a
        /// cut through a box's corner), they're one corner on all of them.
        /// </summary>
        void FindCorners(IReadOnlyList<Plane> region)
        {
            corners.Clear();
            cornerSides.Clear();
            int count = Mathf.Min(region.Count, 64);
            for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
            for (int k = j + 1; k < count; k++)
            {
                Plane a = region[i], b = region[j], c = region[k];
                Vector3 bc = Vector3.Cross(b.normal, c.normal);
                float determinant = Vector3.Dot(a.normal, bc);
                if (Mathf.Abs(determinant) < 1e-6f) continue;
                Vector3 corner = (-a.distance * bc - b.distance * Vector3.Cross(c.normal, a.normal)
                                  - c.distance * Vector3.Cross(a.normal, b.normal)) / determinant;
                bool inside = true;
                for (int s = 0; s < count && inside; s++)
                    inside = region[s].GetDistanceToPoint(corner) <= SameCorner;
                if (!inside) continue;

                ulong sides = (1UL << i) | (1UL << j) | (1UL << k);
                int same = corners.FindIndex(p => (p - corner).sqrMagnitude < SameCorner * SameCorner);
                if (same >= 0) cornerSides[same] |= sides;
                else
                {
                    corners.Add(corner);
                    cornerSides.Add(sides);
                }
            }
        }

        static int CountBits(ulong bits)
        {
            int count = 0;
            for (; bits != 0; bits &= bits - 1) count++;
            return count;
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
