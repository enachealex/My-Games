// Fog of war: the survivor can't see through things taller than a person.
// Walls, fuel tankers, condemned buildings and building fronts block the
// view; cars, slabs, barrels, barricades and lamp posts don't. A port of
// graveway/src/fog.js, in two halves:
//   CanSee()   is a zombie in view? Game hides the ones that aren't and
//              won't aim at them.
//   Update()   darkens the ground behind each blocker, as if the flashlight
//              cast shadows, so you can tell where the blind spots are. One
//              draw call: every shadow is a quad in one mesh, and the
//              stencil buffer stops overlapping ones doubling up.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Graveway
{
    public sealed class Sight
    {
        public const float SightHeight = 2;   // props at least this tall block the view (a car is 1.6)
        const float Reach = 36;               // blockers further than this cast no shadow
        const float Far = 44;                 // how far a shadow reaches
        const int MaxQuads = 48;
        const float Y = 0.17f;                // just above the kerb, so sidewalks shade too

        public static bool BlocksSight(Prop c) => !c.destroyed && !c.airborne && c.height >= SightHeight && Mathf.Min(c.hw, c.hd) > 0.2f;

        static readonly System.Func<Prop, bool> Blocks = BlocksSight;

        // Is (tx, tz) in view from (x, z)? `pad` is how much of the target has
        // to be behind cover before it counts as hidden.
        public static bool CanSee(World world, float x, float z, float tx, float tz, float pad = 0.3f)
        {
            float dx = tx - x, dz = tz - z, d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < 0.5f) return true;
            float ux = dx / d, uz = dz / d, len = d - pad;
            if (World.RayProps(world.props, x, z, ux, uz, len, out _, Blocks) < len) return false;
            return world.FacadeT(x, z, ux, uz, len) >= len;
        }

        readonly Mesh mesh;
        readonly Vector3[] verts = new Vector3[MaxQuads * 4];
        readonly int[] tris = new int[MaxQuads * 6];
        readonly MeshRenderer renderer;
        static readonly (float, float)[] Corners = { (1, 1), (1, -1), (-1, -1), (-1, 1) };

        public Sight(Transform parent)
        {
            mesh = new Mesh { name = "sight shadows" };
            mesh.MarkDynamic();
            mesh.vertices = verts;
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000);
            renderer = Art.Show(parent, mesh, Art.Base(Art.BaseSightShadow), "sight shadows");
        }

        // Rebuilds the shadows for the survivor at (px, pz).
        public void Update(float px, float pz, List<Prop> props)
        {
            int n = 0;
            foreach (var c in props)
            {
                if (n >= MaxQuads) break;
                if (!BlocksSight(c)) continue;
                float cx = c.x - px, cz = c.z - pz, cd = Mathf.Sqrt(cx * cx + cz * cz);
                if (cd > Reach + c.r || cd < c.r + 0.3f) continue;      // too far, or you're right against it
                // The two corners that make its outline from where you stand.
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                Vector2 a = default, b = default;
                foreach (var (sx, sz) in Corners)
                {
                    float lx = sx * c.hw, lz = sz * c.hd;
                    float wx = c.x + lx * c.cos + lz * c.sin, wz = c.z - lx * c.sin + lz * c.cos;
                    float vx = wx - px, vz = wz - pz;
                    float ang = Mathf.Atan2(cx * vz - cz * vx, cx * vx + cz * vz);
                    if (ang < lo) { lo = ang; a = new Vector2(wx, wz); }
                    if (ang > hi) { hi = ang; b = new Vector2(wx, wz); }
                }
                Vector2 FarOf(Vector2 q)
                {
                    float vx = q.x - px, vz = q.y - pz, l = Mathf.Sqrt(vx * vx + vz * vz);
                    if (l == 0) l = 1;
                    return new Vector2(q.x + vx / l * Far, q.y + vz / l * Far);
                }
                var a2 = FarOf(a);
                var b2 = FarOf(b);
                int v = n * 4, t = n * 6;
                verts[v] = new Vector3(a.x, Y, a.y);
                verts[v + 1] = new Vector3(b.x, Y, b.y);
                verts[v + 2] = new Vector3(b2.x, Y, b2.y);
                verts[v + 3] = new Vector3(a2.x, Y, a2.y);
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                n++;
            }
            // Unused quads collapse to nothing.
            for (int t = n * 6; t < tris.Length; t++) tris[t] = 0;
            mesh.vertices = verts;
            mesh.SetTriangles(tris, 0, false);
            renderer.enabled = n > 0;
        }

        public void Clear() => renderer.enabled = false;
    }
}
