using System;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Boolean operations on closed meshes at runtime: subtracting one solid from another, like the post a crystal
    /// froze around. Each solid is a set of convex polygons kept in a BSP tree, the way Evan Wallace's csg.js does
    /// it: clipping one solid's surface against the other's tree keeps only the parts of it that bound the result.
    /// The solid cut from may fold through itself (a squeezed skin); the one cut out has to be closed and clean.
    /// Doubles throughout, so the slivers the cuts leave don't crack. The trees are walked with explicit stacks: a
    /// mostly convex mesh (the blob) makes a tree about as deep as it has triangles.
    /// </summary>
    public sealed class MeshCsg
    {
        // Points this close to a plane count as on it, m.
        const double Epsilon = 1e-5;
        const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

        // Per-vertex scratch for splitting; one polygon at a time, on whichever thread is splitting.
        [ThreadStatic] static double[] distances;
        [ThreadStatic] static int[] sides;

        readonly List<Polygon> polygons;

        /// <summary>How many polygons bound the solid.</summary>
        public int PolygonCount => polygons.Count;

        MeshCsg(List<Polygon> polygons) => this.polygons = polygons;

        /// <summary>
        /// The solid a closed mesh bounds, with every point moved by <paramref name="toSpace"/>. Triangles too small to
        /// have a facing are left out.
        /// </summary>
        public static MeshCsg FromMesh(Mesh mesh, Matrix4x4 toSpace) => FromTriangles(mesh.vertices, mesh.triangles, toSpace);

        /// <summary>The solid a closed mesh (its points and triangles) bounds, with every point moved by <paramref name="toSpace"/>.</summary>
        public static MeshCsg FromTriangles(Vector3[] vertices, int[] triangles, Matrix4x4 toSpace)
        {
            var result = new List<Polygon>(triangles.Length / 3);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                var a = new Vec(toSpace.MultiplyPoint3x4(vertices[triangles[t]]));
                var b = new Vec(toSpace.MultiplyPoint3x4(vertices[triangles[t + 1]]));
                var c = new Vec(toSpace.MultiplyPoint3x4(vertices[triangles[t + 2]]));
                if (Polygon.TryMake(new List<Vec>(3) { a, b, c }, out Polygon polygon)) result.Add(polygon);
            }
            return new MeshCsg(result);
        }

        /// <summary>A box of <paramref name="size"/> around <paramref name="center"/>, with every point moved by <paramref name="toSpace"/>.</summary>
        public static MeshCsg Box(Vector3 center, Vector3 size, Matrix4x4 toSpace)
        {
            var corners = new Vec[8];
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                corners[i] = new Vec(toSpace.MultiplyPoint3x4(center + Vector3.Scale(corner, size)));
            }
            Vec middle = new Vec(toSpace.MultiplyPoint3x4(center));

            // The four corners of each side, in order around it (x, y and z low, then high).
            int[][] faces =
            {
                new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 },
                new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 },
                new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 },
            };
            var result = new List<Polygon>(6);
            foreach (int[] face in faces)
            {
                var points = new List<Vec>(4);
                foreach (int corner in face) points.Add(corners[corner]);
                if (!Polygon.TryMake(points, out Polygon polygon)) continue;
                // Facing out of the box, whichever way round the corners were listed (or a mirroring move turned them).
                if (Vec.Dot(polygon.Plane.Normal, points[0] - middle) < 0.0) polygon.Flip();
                result.Add(polygon);
            }
            return new MeshCsg(result);
        }

        /// <summary>
        /// This solid with everything inside <paramref name="other"/> taken out. Neither solid changes. The other has to
        /// be a clean closed solid; this one may fold through itself, like a frozen skin squeezed against something.
        /// </summary>
        public MeshCsg Subtract(MeshCsg other)
        {
            // This solid's surface outside the other: the other is clean, so its tree tells inside from outside.
            // Turned inside out for the clip, as csg.js does it, so faces lying on the other's surface go too.
            Node cutter = Node.Build(Clone(other.polygons));
            List<Polygon> inverted = Clone(polygons);
            foreach (Polygon polygon in inverted) polygon.Flip();
            List<Polygon> result = cutter.Clip(inverted);
            foreach (Polygon polygon in result) polygon.Flip();

            // The other's surface inside this solid, which walls the hole. Where this solid folds through itself, its
            // tree can't tell inside from outside, and csg.js leaves gaps in the walls there (a tenth of them, around a
            // test fold). So its tree only splits the other's surface where the two cross, and each piece is kept when
            // this solid's surface winds around it, which folds don't fool.
            Node self = Node.Build(Clone(polygons));
            foreach (Polygon piece in self.Divide(Clone(other.polygons)))
            {
                if (WindingAround(polygons, piece.Centroid()) < 0.5) continue;
                piece.Flip();
                result.Add(piece);
            }
            return new MeshCsg(result);
        }

        /// <summary>
        /// How many times the surface winds around the point: 1 inside a closed surface, 0 outside, about 1 still
        /// inside one that folds through itself. The solid angles its triangles fill, seen from the point, over a whole
        /// sphere's.
        /// </summary>
        static double WindingAround(List<Polygon> surface, Vec point)
        {
            double total = 0.0;
            foreach (Polygon polygon in surface)
            {
                List<Vec> corners = polygon.Vertices;
                Vec a = corners[0] - point;
                double la = a.Length;
                for (int k = 1; k + 1 < corners.Count; k++)
                {
                    Vec b = corners[k] - point, c = corners[k + 1] - point;
                    double lb = b.Length, lc = c.Length;
                    // Van Oosterom and Strackee's solid angle of a triangle.
                    double numerator = Vec.Dot(a, Vec.Cross(b, c));
                    double denominator = la * lb * lc + Vec.Dot(a, b) * lc + Vec.Dot(a, c) * lb + Vec.Dot(b, c) * la;
                    total += 2.0 * Math.Atan2(numerator, denominator);
                }
            }
            return Math.Abs(total) / (4.0 * Math.PI);
        }

        /// <summary>
        /// This solid without its loose bits: the parts that hang together with nothing else and are smaller across
        /// than <paramref name="share"/> of the biggest part, or hold less than <paramref name="volumeShare"/> of its
        /// volume, like scraps and thin sheets a cut left floating.
        /// </summary>
        public MeshCsg WithoutLooseBits(double share, double volumeShare)
        {
            int count = polygons.Count;
            if (count == 0) return this;

            // Polygons with a point in common hang together.
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            var firstAt = new Dictionary<(long, long, long), int>();
            for (int i = 0; i < count; i++)
                foreach (Vec point in polygons[i].Vertices)
                {
                    var key = (Weld(point.X), Weld(point.Y), Weld(point.Z));
                    if (firstAt.TryGetValue(key, out int other)) Join(parent, i, other);
                    else firstAt.Add(key, i);
                }

            var low = new Dictionary<int, Vec>();
            var high = new Dictionary<int, Vec>();
            var volume = new Dictionary<int, double>();
            for (int i = 0; i < count; i++)
            {
                int part = Root(parent, i);
                foreach (Vec point in polygons[i].Vertices)
                {
                    low[part] = low.TryGetValue(part, out Vec l) ? Vec.Min(l, point) : point;
                    high[part] = high.TryGetValue(part, out Vec h) ? Vec.Max(h, point) : point;
                }
                volume[part] = (volume.TryGetValue(part, out double v) ? v : 0.0) + polygons[i].SignedVolume();
            }
            double biggest = 0.0, mostVolume = 0.0;
            foreach (int part in low.Keys)
            {
                biggest = Math.Max(biggest, (high[part] - low[part]).Length);
                mostVolume = Math.Max(mostVolume, Math.Abs(volume[part]));
            }

            var kept = new List<Polygon>(count);
            for (int i = 0; i < count; i++)
            {
                int part = Root(parent, i);
                if ((high[part] - low[part]).Length >= biggest * share && Math.Abs(volume[part]) >= mostVolume * volumeShare)
                    kept.Add(polygons[i]);
            }
            return kept.Count == count ? this : new MeshCsg(kept);
        }

        // Points closer than about this are one point when telling which polygons hang together, m.
        static long Weld(double value) => (long)Math.Round(value * 1000.0);

        static int Root(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        static void Join(int[] parent, int a, int b)
        {
            a = Root(parent, a);
            b = Root(parent, b);
            if (a != b) parent[a] = b;
        }

        /// <summary>The solid's surface as triangles, each polygon with points of its own.</summary>
        public void GetTriangles(out Vector3[] vertices, out int[] triangles)
        {
            var points = new List<Vector3>();
            var indices = new List<int>();
            foreach (Polygon polygon in polygons)
            {
                int start = points.Count;
                foreach (Vec point in polygon.Vertices) points.Add(point.ToVector3());
                for (int k = 1; k + 1 < polygon.Vertices.Count; k++)
                {
                    indices.Add(start);
                    indices.Add(start + k);
                    indices.Add(start + k + 1);
                }
            }
            vertices = points.ToArray();
            triangles = indices.ToArray();
        }

        /// <summary>
        /// The solid's surface as flat facets (each polygon its own vertices and normal), written into
        /// <paramref name="into"/> or a new mesh.
        /// </summary>
        public Mesh ToMesh(Mesh into = null)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            foreach (Polygon polygon in polygons)
            {
                int start = vertices.Count;
                Vector3 normal = polygon.Plane.Normal.ToVector3();
                foreach (Vec point in polygon.Vertices)
                {
                    vertices.Add(point.ToVector3());
                    normals.Add(normal);
                }
                // Convex, so a fan covers it; the order keeps the facing.
                for (int k = 1; k + 1 < polygon.Vertices.Count; k++)
                {
                    triangles.Add(start);
                    triangles.Add(start + k);
                    triangles.Add(start + k + 1);
                }
            }

            Mesh mesh = into != null ? into : new Mesh { name = "Csg Result" };
            mesh.Clear();
            mesh.indexFormat = vertices.Count > ushort.MaxValue
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static List<Polygon> Clone(List<Polygon> source)
        {
            var copy = new List<Polygon>(source.Count);
            foreach (Polygon polygon in source) copy.Add(polygon.Clone());
            return copy;
        }

        /// <summary>
        /// Sorts a polygon by which side of <paramref name="plane"/> it's on, cutting it in two where it spans the
        /// plane. One lying in the plane goes with those in front or behind by which way it faces.
        /// </summary>
        static void Split(in Plane plane, Polygon polygon, List<Polygon> coplanarFront, List<Polygon> coplanarBack,
                          List<Polygon> front, List<Polygon> back)
        {
            List<Vec> points = polygon.Vertices;
            int count = points.Count;
            if (distances == null || distances.Length < count)
            {
                distances = new double[Math.Max(count, 16)];
                sides = new int[Math.Max(count, 16)];
            }

            int whole = Coplanar;
            for (int i = 0; i < count; i++)
            {
                double distance = Vec.Dot(plane.Normal, points[i]) - plane.W;
                int side = distance < -Epsilon ? Back : distance > Epsilon ? Front : Coplanar;
                distances[i] = distance;
                sides[i] = side;
                whole |= side;
            }

            switch (whole)
            {
                case Coplanar:
                    (Vec.Dot(plane.Normal, polygon.Plane.Normal) > 0.0 ? coplanarFront : coplanarBack).Add(polygon);
                    return;
                case Front:
                    front.Add(polygon);
                    return;
                case Back:
                    back.Add(polygon);
                    return;
            }

            var inFront = new List<Vec>(count + 1);
            var behind = new List<Vec>(count + 1);
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                int si = sides[i], sj = sides[j];
                Vec pi = points[i];
                if (si != Back) inFront.Add(pi);
                if (si != Front) behind.Add(pi);
                if ((si | sj) != Spanning) continue;
                Vec crossing = Vec.Lerp(pi, points[j], distances[i] / (distances[i] - distances[j]));
                inFront.Add(crossing);
                behind.Add(crossing);
            }
            // The halves keep the whole's plane: three points of a thin sliver would give a poor one.
            if (inFront.Count >= 3) front.Add(new Polygon(inFront, polygon.Plane));
            if (behind.Count >= 3) back.Add(new Polygon(behind, polygon.Plane));
        }

        readonly struct Vec
        {
            public readonly double X, Y, Z;

            public Vec(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public Vec(Vector3 v) : this(v.x, v.y, v.z) { }

            public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec operator -(Vec a) => new Vec(-a.X, -a.Y, -a.Z);
            public static Vec operator *(Vec a, double k) => new Vec(a.X * k, a.Y * k, a.Z * k);
            public static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            public static Vec Cross(Vec a, Vec b) => new Vec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            public static Vec Lerp(Vec a, Vec b, double t) => a + (b - a) * t;
            public static Vec Min(Vec a, Vec b) => new Vec(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
            public static Vec Max(Vec a, Vec b) => new Vec(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
            public double Length => Math.Sqrt(Dot(this, this));
            public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);
        }

        readonly struct Plane
        {
            public readonly Vec Normal;
            public readonly double W;

            public Plane(Vec normal, double w)
            {
                Normal = normal;
                W = w;
            }

            public Plane Flipped => new Plane(-Normal, -W);
        }

        /// <summary>A flat convex polygon; its points run clockwise seen from the side it faces, as Unity's triangles do.</summary>
        sealed class Polygon
        {
            public readonly List<Vec> Vertices;
            public Plane Plane;

            public Polygon(List<Vec> vertices, Plane plane)
            {
                Vertices = vertices;
                Plane = plane;
            }

            /// <summary>The polygon through these points, facing the way their order says; false when they don't span a plane.</summary>
            public static bool TryMake(List<Vec> vertices, out Polygon polygon)
            {
                polygon = null;
                Vec normal = Vec.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]);
                double length = normal.Length;
                if (length < 1e-12) return false;
                normal *= 1.0 / length;
                polygon = new Polygon(vertices, new Plane(normal, Vec.Dot(normal, vertices[0])));
                return true;
            }

            public Polygon Clone() => new Polygon(new List<Vec>(Vertices), Plane);

            /// <summary>The middle of its points: inside it, as it's convex.</summary>
            public Vec Centroid()
            {
                Vec sum = new Vec(0.0, 0.0, 0.0);
                foreach (Vec point in Vertices) sum += point;
                return sum * (1.0 / Vertices.Count);
            }

            /// <summary>Its volume against the origin: summed over a closed surface, the volume inside, signed by facing.</summary>
            public double SignedVolume()
            {
                double volume = 0.0;
                for (int k = 1; k + 1 < Vertices.Count; k++)
                    volume += Vec.Dot(Vertices[0], Vec.Cross(Vertices[k], Vertices[k + 1]));
                return volume / 6.0;
            }

            public void Flip()
            {
                Vertices.Reverse();
                Plane = Plane.Flipped;
            }
        }

        /// <summary>
        /// One node of a BSP tree: the polygons lying in its plane, and the trees of those in front of it and behind it.
        /// A node without a plane is an empty tree.
        /// </summary>
        sealed class Node
        {
            Plane plane;
            bool hasPlane;
            Node front;
            Node back;
            readonly List<Polygon> polygons = new List<Polygon>();

            public static Node Build(List<Polygon> polygons)
            {
                var root = new Node();
                root.Add(polygons);
                return root;
            }

            /// <summary>Puts polygons into the tree; each new node splits on the plane of the first polygon that reaches it.</summary>
            public void Add(List<Polygon> list)
            {
                var work = new Stack<(Node, List<Polygon>)>();
                work.Push((this, list));
                while (work.Count > 0)
                {
                    (Node node, List<Polygon> next) = work.Pop();
                    if (next.Count == 0) continue;
                    if (!node.hasPlane)
                    {
                        node.plane = next[0].Plane;
                        node.hasPlane = true;
                    }
                    var inFront = new List<Polygon>();
                    var behind = new List<Polygon>();
                    foreach (Polygon polygon in next) Split(node.plane, polygon, node.polygons, node.polygons, inFront, behind);
                    if (inFront.Count > 0) work.Push((node.front ??= new Node(), inFront));
                    if (behind.Count > 0) work.Push((node.back ??= new Node(), behind));
                }
            }

            /// <summary>The parts of <paramref name="list"/> outside this tree's solid.</summary>
            public List<Polygon> Clip(List<Polygon> list)
            {
                var result = new List<Polygon>();
                if (!hasPlane)
                {
                    result.AddRange(list);
                    return result;
                }

                var work = new Stack<(Node, List<Polygon>)>();
                work.Push((this, list));
                while (work.Count > 0)
                {
                    (Node node, List<Polygon> next) = work.Pop();
                    var inFront = new List<Polygon>();
                    var behind = new List<Polygon>();
                    foreach (Polygon polygon in next) Split(node.plane, polygon, inFront, behind, inFront, behind);
                    if (node.front == null) result.AddRange(inFront);
                    else if (inFront.Count > 0) work.Push((node.front, inFront));
                    // Behind a leaf is inside the solid: those parts go.
                    if (node.back != null && behind.Count > 0) work.Push((node.back, behind));
                }
                return result;
            }

            /// <summary>
            /// <paramref name="list"/> cut up along this tree's planes until no piece crosses one, all pieces kept,
            /// inside the solid or not.
            /// </summary>
            public List<Polygon> Divide(List<Polygon> list)
            {
                var result = new List<Polygon>();
                if (!hasPlane)
                {
                    result.AddRange(list);
                    return result;
                }

                var work = new Stack<(Node, List<Polygon>)>();
                work.Push((this, list));
                while (work.Count > 0)
                {
                    (Node node, List<Polygon> next) = work.Pop();
                    var inFront = new List<Polygon>();
                    var behind = new List<Polygon>();
                    foreach (Polygon polygon in next) Split(node.plane, polygon, inFront, behind, inFront, behind);
                    if (node.front == null) result.AddRange(inFront);
                    else if (inFront.Count > 0) work.Push((node.front, inFront));
                    if (node.back == null) result.AddRange(behind);
                    else if (behind.Count > 0) work.Push((node.back, behind));
                }
                return result;
            }
        }
    }
}
