// BlobShip - builds the thick rounded slab mesh used by the 3D liquid UI.
// Local space: x right, y up, the front faces -z (toward a Screen Space - Camera canvas camera).
// Two parts in one mesh: the soft elliptical edge band (uv1.x = 0) and a dense front grid that the waves move (uv1.x = 1).
// uv0 = position normalised to the rect (0..1), the same space as the wave simulation.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlobShip.UI.Liquid3D
{
    public static class LiquidSlabMesh
    {
        // points round a rounded rectangle, evenly spaced, with outward normals: (x, y, nx, ny)
        public static List<Vector4> Outline(float w, float d, float r, int n)
        {
            float a = w * 0.5f, b = d * 0.5f;
            r = Mathf.Max(0.0001f, Mathf.Min(r, Mathf.Min(a, b)));
            var segs = new List<float[]>();   // kind(0 line/1 arc), x0,y0,x1,y1,nx,ny | cx,cy,t0 ; last = length
            Line(segs, -a + r, -b, a - r, -b, 0, -1); Arc(segs, a - r, -b + r, -Mathf.PI * 0.5f, r);
            Line(segs, a, -b + r, a, b - r, 1, 0);    Arc(segs, a - r, b - r, 0f, r);
            Line(segs, a - r, b, -a + r, b, 0, 1);    Arc(segs, -a + r, b - r, Mathf.PI * 0.5f, r);
            Line(segs, -a, b - r, -a, -b + r, -1, 0); Arc(segs, -a + r, -b + r, Mathf.PI, r);
            float total = 0f; foreach (var s in segs) total += s[7];
            var pts = new List<Vector4>(n);
            for (int i = 0; i < n; i++)
            {
                float s = i / (float)n * total;
                for (int j = 0; j < segs.Count; j++)
                {
                    var g = segs[j];
                    if (s <= g[7] || j == segs.Count - 1)
                    {
                        float t = g[7] > 0f ? Mathf.Min(1f, s / g[7]) : 0f;
                        if (g[0] < 0.5f) pts.Add(new Vector4(g[1] + (g[3] - g[1]) * t, g[2] + (g[4] - g[2]) * t, g[5], g[6]));
                        else { float an = g[3] + t * Mathf.PI * 0.5f; pts.Add(new Vector4(g[1] + Mathf.Cos(an) * r, g[2] + Mathf.Sin(an) * r, Mathf.Cos(an), Mathf.Sin(an))); }
                        break;
                    }
                    s -= g[7];
                }
            }
            return pts;
        }

        static void Line(List<float[]> segs, float x0, float y0, float x1, float y1, float nx, float ny)
        {
            segs.Add(new[] { 0f, x0, y0, x1, y1, nx, ny, Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) });
        }

        static void Arc(List<float[]> segs, float cx, float cy, float t0, float r)
        {
            segs.Add(new[] { 1f, cx, cy, t0, 0f, 0f, 0f, r * Mathf.PI * 0.5f });
        }

        /// <param name="w">width</param><param name="d">height</param><param name="r">corner radius</param>
        /// <param name="rb">edge rounding (how far in from the outline the flat front starts)</param><param name="t">half thickness</param>
        public static Mesh Build(Mesh mesh, float w, float d, float r, float rb, float t, int nOut, int nPhi, int capX, int capY, float capPad)
        {
            if (mesh == null) mesh = new Mesh { name = "LiquidSlab" };
            mesh.Clear();
            rb = Mathf.Clamp(rb, 0.001f, Mathf.Min(r, Mathf.Min(w, d) * 0.5f) - 0.0005f);
            var inner = Outline(w - 2f * rb, d - 2f * rb, Mathf.Max(0.001f, r - rb), nOut);
            var pos = new List<Vector3>(); var nor = new List<Vector3>(); var uv0 = new List<Vector2>(); var uv1 = new List<Vector2>();
            var rim = new List<int>(); var cap = new List<int>();

            // edge band: phi 0 = back rim (z = +t), pi/2 = outline, pi = front rim (z = -t)
            for (int i = 0; i <= nOut; i++)
            {
                Vector4 q = inner[i % nOut];
                for (int j = 0; j <= nPhi; j++)
                {
                    float ph = j / (float)nPhi * Mathf.PI, s = Mathf.Sin(ph), c = Mathf.Cos(ph);
                    var p = new Vector3(q.x + q.z * rb * s, q.y + q.w * rb * s, t * c);
                    float rn = t * s, zn = rb * c, l = Mathf.Sqrt(rn * rn + zn * zn); if (l < 1e-6f) l = 1f;
                    pos.Add(p); nor.Add(new Vector3(q.z * rn / l, q.w * rn / l, zn / l));
                    uv0.Add(new Vector2(p.x / w + 0.5f, p.y / d + 0.5f)); uv1.Add(Vector2.zero);
                }
            }
            for (int i = 0; i < nOut; i++)
                for (int j = 0; j < nPhi; j++)
                {
                    int a = i * (nPhi + 1) + j, b = (i + 1) * (nPhi + 1) + j;
                    rim.Add(a); rim.Add(b); rim.Add(a + 1);
                    rim.Add(b); rim.Add(b + 1); rim.Add(a + 1);
                }

            // front grid (clipped to the inner outline in the shader, a little larger so there is no seam)
            int baseIdx = pos.Count;
            float cw = w - 2f * rb + capPad, cd = d - 2f * rb + capPad;
            for (int y = 0; y <= capY; y++)
                for (int x = 0; x <= capX; x++)
                {
                    var p = new Vector3((x / (float)capX - 0.5f) * cw, (y / (float)capY - 0.5f) * cd, -t);
                    pos.Add(p); nor.Add(new Vector3(0, 0, -1));
                    uv0.Add(new Vector2(p.x / w + 0.5f, p.y / d + 0.5f)); uv1.Add(new Vector2(1, 0));
                }
            for (int y = 0; y < capY; y++)
                for (int x = 0; x < capX; x++)
                {
                    int a = baseIdx + y * (capX + 1) + x, b = a + capX + 1;
                    cap.Add(a); cap.Add(b); cap.Add(a + 1);
                    cap.Add(b); cap.Add(b + 1); cap.Add(a + 1);
                }

            FixWinding(rim, pos, nor);
            FixWinding(cap, pos, nor);
            rim.AddRange(cap);

            mesh.indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(pos); mesh.SetNormals(nor); mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1);
            mesh.SetTriangles(rim, 0, true);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(w, d, 2f * t) * 1.2f);
            return mesh;
        }

        // Unity draws triangles whose Cross(b - a, c - a) points along the vertex normals (toward the viewer).
        static void FixWinding(List<int> tris, List<Vector3> pos, List<Vector3> nor)
        {
            float score = 0f;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = pos[tris[i]], b = pos[tris[i + 1]], c = pos[tris[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                score += Mathf.Sign(Vector3.Dot(n, nor[tris[i]] + nor[tris[i + 1]] + nor[tris[i + 2]]));
            }
            if (score >= 0f) return;
            for (int i = 0; i < tris.Count; i += 3) { int k = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = k; }
        }
    }
}
