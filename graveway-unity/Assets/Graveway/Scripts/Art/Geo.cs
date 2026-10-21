// Building meshes in code the way models.js does with three.js geometry:
// boxes, cylinders, planes and spheres, each placed by a transform and
// merged into one mesh per material (one draw call). Shapes match three's:
// BoxGeometry centred, CylinderGeometry standing on y, PlaneGeometry in its
// own xy plane facing +z.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Graveway
{
    // Transforms in radians, composed like three's geo.rotateX(a).translate(...):
    // the rightmost one applies first, so T(...) * Rx(...) rotates, then moves.
    public static class M
    {
        public static Matrix4x4 I => Matrix4x4.identity;
        public static Matrix4x4 T(float x, float y, float z) => Matrix4x4.Translate(new Vector3(x, y, z));
        public static Matrix4x4 S(float x, float y, float z) => Matrix4x4.Scale(new Vector3(x, y, z));
        public static Matrix4x4 Rx(float a) => Matrix4x4.Rotate(Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.right));
        public static Matrix4x4 Ry(float a) => Matrix4x4.Rotate(Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up));
        public static Matrix4x4 Rz(float a) => Matrix4x4.Rotate(Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.forward));

        // three's Euler (order XYZ) as a Unity rotation.
        public static Quaternion Euler(float x, float y, float z) =>
            Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(y * Mathf.Rad2Deg, Vector3.up) *
            Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward);

        public static Quaternion Yaw(float a) => Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up);
    }

    public sealed class GeoBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> norms = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        public int Count => verts.Count;

        int Vert(Matrix4x4 m, Vector3 p, Vector3 n, Vector2 uv)
        {
            verts.Add(m.MultiplyPoint3x4(p));
            norms.Add(m.MultiplyVector(n).normalized);
            uvs.Add(uv);
            return verts.Count - 1;
        }

        // Unity draws a triangle's front when its corners run clockwise as
        // seen from outside, i.e. when (b - a) x (c - a) points outward.
        void Tri(int a, int b, int c, Vector3 outward)
        {
            var n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (Vector3.Dot(n, outward) >= 0) { tris.Add(a); tris.Add(b); tris.Add(c); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); }
        }

        void Quad(int a, int b, int c, int d, Vector3 outward)
        {
            Tri(a, b, c, outward);
            Tri(a, c, d, outward);
        }

        static readonly Vector3[] FaceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        // A box, w x h x d, centred, then placed by m. With tileU / tileV, a
        // face's UVs run to (its size / tile), so one texture tiles the same
        // on a short shop and a tall tower; otherwise 0..1 per face.
        public GeoBuilder Box(float w, float h, float d, Matrix4x4 m, float tileU = 0, float tileV = 0)
        {
            var size = new Vector3(w, h, d);
            for (int f = 0; f < 6; f++)
            {
                var n = FaceN[f];
                // The face's own axes: v up the face (or along z on the top
                // and bottom), u across it.
                var v = f < 2 || f > 3 ? Vector3.up : Vector3.forward;
                var u = f < 2 ? Vector3.forward : Vector3.right;
                float hn = Abs(n, size) / 2, hu = Abs(u, size) / 2, hv = Abs(v, size) / 2;
                int b = verts.Count;
                for (int k = 0; k < 4; k++)
                {
                    float su = k == 1 || k == 2 ? 1 : -1, sv = k >= 2 ? 1 : -1;
                    float uu = (su + 1) / 2, vv = (sv + 1) / 2;
                    if (tileU > 0) uu *= hu * 2 / tileU;
                    if (tileV > 0) vv *= hv * 2 / tileV;
                    Vert(m, n * hn + u * (su * hu) + v * (sv * hv), n, new Vector2(uu, vv));
                }
                Quad(b, b + 1, b + 2, b + 3, m.MultiplyVector(n));
            }
            return this;
        }

        public GeoBuilder Box(float w, float h, float d, float x = 0, float y = 0, float z = 0) => Box(w, h, d, M.T(x, y, z));

        static float Abs(Vector3 axis, Vector3 size) =>
            Mathf.Abs(axis.x) * size.x + Mathf.Abs(axis.y) * size.y + Mathf.Abs(axis.z) * size.z;

        // three's CylinderGeometry: along y, centred, capped.
        public GeoBuilder Cylinder(float rTop, float rBottom, float height, int segments, Matrix4x4 m)
        {
            float hh = height / 2, slope = (rBottom - rTop) / height;
            int b = verts.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2, sx = Mathf.Sin(a), cz = Mathf.Cos(a);
                var n = new Vector3(sx, slope, cz).normalized;
                Vert(m, new Vector3(sx * rTop, hh, cz * rTop), n, new Vector2(i / (float)segments, 1));
                Vert(m, new Vector3(sx * rBottom, -hh, cz * rBottom), n, new Vector2(i / (float)segments, 0));
            }
            for (int i = 0; i < segments; i++)
            {
                int t0 = b + i * 2, b0 = t0 + 1, t1 = t0 + 2, b1 = t0 + 3;
                float a = (i + 0.5f) / segments * Mathf.PI * 2;
                Quad(t0, t1, b1, b0, m.MultiplyVector(new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a))));
            }
            Cap(rTop, hh, 1, segments, m);
            Cap(rBottom, -hh, -1, segments, m);
            return this;
        }

        void Cap(float r, float y, int up, int segments, Matrix4x4 m)
        {
            if (r <= 0) return;
            var n = Vector3.up * up;
            int c = Vert(m, new Vector3(0, y, 0), n, new Vector2(0.5f, 0.5f));
            int first = verts.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2;
                Vert(m, new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r), n, new Vector2(0.5f + Mathf.Sin(a) / 2, 0.5f + Mathf.Cos(a) / 2));
            }
            var outward = m.MultiplyVector(n);
            for (int i = 0; i < segments; i++) Tri(c, first + i, first + i + 1, outward);
        }

        // three's PlaneGeometry(w, h): in its own xy plane, facing +z.
        public GeoBuilder Plane(float w, float h, Matrix4x4 m, bool twoSided = false)
        {
            int b = verts.Count;
            Vert(m, new Vector3(-w / 2, -h / 2, 0), Vector3.forward, new Vector2(0, 0));
            Vert(m, new Vector3(w / 2, -h / 2, 0), Vector3.forward, new Vector2(1, 0));
            Vert(m, new Vector3(w / 2, h / 2, 0), Vector3.forward, new Vector2(1, 1));
            Vert(m, new Vector3(-w / 2, h / 2, 0), Vector3.forward, new Vector2(0, 1));
            var outward = m.MultiplyVector(Vector3.forward);
            Quad(b, b + 1, b + 2, b + 3, outward);
            if (twoSided) Quad(b, b + 1, b + 2, b + 3, -outward);
            return this;
        }

        // A flat square on the ground (three's PlaneGeometry rotated flat).
        public GeoBuilder Flat(float w, float d, Matrix4x4 m) => Plane(w, d, m * M.Rx(-Mathf.PI / 2));

        public GeoBuilder Sphere(float r, int wSeg, int hSeg, Matrix4x4 m)
        {
            int b = verts.Count;
            for (int y = 0; y <= hSeg; y++)
            {
                float v = y / (float)hSeg, th = v * Mathf.PI;
                for (int x = 0; x <= wSeg; x++)
                {
                    float u = x / (float)wSeg, ph = u * Mathf.PI * 2;
                    var n = new Vector3(-Mathf.Cos(ph) * Mathf.Sin(th), Mathf.Cos(th), Mathf.Sin(ph) * Mathf.Sin(th));
                    Vert(m, n * r, n, new Vector2(u, 1 - v));
                }
            }
            for (int y = 0; y < hSeg; y++)
                for (int x = 0; x < wSeg; x++)
                {
                    int a = b + y * (wSeg + 1) + x, c = a + wSeg + 1;
                    var mid = (verts[a] + verts[c + 1]) / 2 - m.MultiplyPoint3x4(Vector3.zero);
                    if (y != 0) Tri(a, c, a + 1, mid);
                    if (y != hSeg - 1) Tri(a + 1, c, c + 1, mid);
                }
            return this;
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
