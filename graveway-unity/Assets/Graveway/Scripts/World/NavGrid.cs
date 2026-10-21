// Zombie navigation, for a street: a port of graveway/src/nav.js.
//
// A grid of 0.5 m cells covers the road around the survivor (it slides
// along as you walk). Every standing prop blocks the cells under it, and
// rubble marks cells as rough (slow, and costlier to path through). From
// that, a flow field gives every cell its walking distance to the
// survivor, so each zombie only has to step to its lowest neighbour.
//
// The grid is rebuilt whenever a prop breaks, falls over, gets shoved by a
// blast or the street streams in more road (World sets `dirty`), and the
// flow field whenever the survivor changes cell. Zombies therefore re-route
// around (or over) whatever the fighting leaves behind.

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public sealed class NavGrid
    {
        public const float Cell = 0.5f;
        const float Ahead = 60;          // metres of grid up the road from the survivor
        const float Behind = 26;         // and behind
        const float Inflate = 0.32f;     // how much props grow by, so zombie bodies clear them
        const float RoughCost = 3;       // rubble costs this much more to walk through
        const float RebuildEvery = 0.15f;
        const float Sqrt2 = 1.41421356f;

        public readonly float x0 = -World.WalkHalf;
        public readonly int cols, rows, n;
        readonly byte[] blocked, rough;
        readonly float[] dist;
        // Binary heap with lazy deletion: (key, cell) pairs.
        readonly float[] hKey;
        readonly int[] hVal;
        int size;
        public float z0;                 // z of the grid's first row (furthest up the road)
        int goal = -1;
        public bool dirty = true;
        float timer;
        public int builds;               // how many times the grid was rebuilt (for tests)

        public NavGrid()
        {
            cols = Mathf.CeilToInt(World.WalkHalf * 2 / Cell);
            rows = Mathf.CeilToInt((Ahead + Behind) / Cell);
            n = cols * rows;
            blocked = new byte[n];
            rough = new byte[n];
            dist = new float[n];
            for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;
            hKey = new float[n * 8];
            hVal = new int[n * 8];
        }

        public void Reset(float pz)
        {
            z0 = pz - Ahead;
            goal = -1;
            dirty = true;
            timer = 0;
        }

        public int CellOf(float x, float z)
        {
            int c = Mathf.FloorToInt((x - x0) / Cell), r = Mathf.FloorToInt((z - z0) / Cell);
            if (c < 0 || r < 0 || c >= cols || r >= rows) return -1;
            return r * cols + c;
        }

        public float Cx(int i) => x0 + (i % cols + 0.5f) * Cell;
        public float Cz(int i) => z0 + (i / cols + 0.5f) * Cell;

        public void Update(float dt, float px, float pz, List<Prop> props, List<RoughArea> roughs)
        {
            timer -= dt;
            // Slide the window along with the survivor, and rebuild right away:
            // until it is, every cell would describe ground 6 m away.
            float centre = z0 + Ahead;
            if (Mathf.Abs(pz - centre) > 6) { z0 = pz - Ahead; dirty = true; timer = 0; }
            int g = CellOf(px, pz);
            if ((g == goal && !dirty) || timer > 0) return;
            timer = RebuildEvery;
            if (dirty) { Rasterize(props, roughs); dirty = false; builds++; }
            goal = g;
            Flow(g);
        }

        // Mark every cell under a standing prop as blocked, and under rubble as rough.
        void Rasterize(List<Prop> props, List<RoughArea> roughs)
        {
            System.Array.Clear(blocked, 0, n);
            System.Array.Clear(rough, 0, n);
            foreach (var p in props) if (p.blocking && !p.destroyed) Mark(p.x, p.z, p.cos, p.sin, p.hw, p.hd, blocked, Inflate);
            foreach (var r in roughs) Mark(r.x, r.z, r.cos, r.sin, r.hw, r.hd, rough, 0);
        }

        void Mark(float ox, float oz, float cos, float sin, float hw, float hd, byte[] arr, float pad)
        {
            float ex = Mathf.Abs(cos) * hw + Mathf.Abs(sin) * hd + pad;
            float ez = Mathf.Abs(sin) * hw + Mathf.Abs(cos) * hd + pad;
            int c0 = Mathf.Max(0, Mathf.FloorToInt((ox - ex - x0) / Cell));
            int c1 = Mathf.Min(cols - 1, Mathf.FloorToInt((ox + ex - x0) / Cell));
            int r0 = Mathf.Max(0, Mathf.FloorToInt((oz - ez - z0) / Cell));
            int r1 = Mathf.Min(rows - 1, Mathf.FloorToInt((oz + ez - z0) / Cell));
            for (int r = r0; r <= r1; r++)
            {
                float z = z0 + (r + 0.5f) * Cell - oz;
                for (int c = c0; c <= c1; c++)
                {
                    float x = x0 + (c + 0.5f) * Cell - ox;
                    float lx = x * cos - z * sin, lz = x * sin + z * cos;
                    if (Mathf.Abs(lx) <= hw + pad && Mathf.Abs(lz) <= hd + pad) arr[r * cols + c] = 1;
                }
            }
        }

        // Test hooks: what the grid thinks of a spot.
        public bool IsBlocked(float x, float z) { int i = CellOf(x, z); return i >= 0 && blocked[i] == 1; }
        public float DistanceAt(float x, float z) { int i = CellOf(x, z); return i >= 0 ? dist[i] : float.PositiveInfinity; }

        void Push(float k, int v)
        {
            int i = size++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (hKey[p] <= k) break;
                hKey[i] = hKey[p]; hVal[i] = hVal[p]; i = p;
            }
            hKey[i] = k; hVal[i] = v;
        }

        int Pop()
        {
            int v = hVal[0];
            float k = hKey[--size];
            int vv = hVal[size];
            int i = 0;
            for (;;)
            {
                int c = 2 * i + 1;
                if (c >= size) break;
                if (c + 1 < size && hKey[c + 1] < hKey[c]) c++;
                if (hKey[c] >= k) break;
                hKey[i] = hKey[c]; hVal[i] = hVal[c]; i = c;
            }
            hKey[i] = k; hVal[i] = vv;
            return v;
        }

        // Dijkstra outward from the survivor's cell over 8 neighbours, never
        // cutting a blocked corner.
        void Flow(int g)
        {
            for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;
            if (g < 0) return;
            if (blocked[g] == 1)
            {
                // Standing hard against a prop: start from the nearest open cell.
                g = NearestOpen(g);
                if (g < 0) return;
            }
            size = 0;
            dist[g] = 0;
            Push(0, g);
            while (size > 0)
            {
                float d0 = hKey[0];
                int i = Pop();
                if (d0 > dist[i]) continue;
                int r = i / cols, c = i - r * cols;
                for (int dr = -1; dr <= 1; dr++)
                {
                    int nr = r + dr;
                    if (nr < 0 || nr >= rows) continue;
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        int nc = c + dc;
                        if (nc < 0 || nc >= cols) continue;
                        int j = nr * cols + nc;
                        if (blocked[j] == 1) continue;
                        if (dr != 0 && dc != 0 && (blocked[r * cols + nc] == 1 || blocked[nr * cols + c] == 1)) continue;
                        float step = (dr != 0 && dc != 0 ? Sqrt2 : 1) * (rough[j] == 1 ? RoughCost : 1);
                        float nd = d0 + step;
                        if (nd < dist[j])
                        {
                            dist[j] = nd;
                            if (size < hKey.Length) Push(nd, j);
                        }
                    }
                }
            }
        }

        int NearestOpen(int i)
        {
            int r0 = i / cols, c0 = i % cols;
            for (int rad = 1; rad <= 4; rad++)
                for (int dr = -rad; dr <= rad; dr++)
                    for (int dc = -rad; dc <= rad; dc++)
                    {
                        int r = r0 + dr, c = c0 + dc;
                        if (r < 0 || c < 0 || r >= rows || c >= cols) continue;
                        int j = r * cols + c;
                        if (blocked[j] == 0) return j;
                    }
            return -1;
        }

        // Which way should something at (x, z) walk to reach the survivor?
        // A unit direction, or false when there's no known path.
        public bool Steer(float x, float z, out float ox, out float oz)
        {
            ox = oz = 0;
            int i = CellOf(x, z);
            if (i < 0) return false;
            int r = i / cols, c = i - r * cols;
            bool wedged = blocked[i] == 1;
            float best = wedged ? float.PositiveInfinity : dist[i];
            int bi = -1;
            // Look two rings out when wedged inside a prop's padding.
            int reach = wedged ? 2 : 1;
            for (int dr = -reach; dr <= reach; dr++)
            {
                int nr = r + dr;
                if (nr < 0 || nr >= rows) continue;
                for (int dc = -reach; dc <= reach; dc++)
                {
                    int nc = c + dc;
                    if ((dr == 0 && dc == 0) || nc < 0 || nc >= cols) continue;
                    int j = nr * cols + nc;
                    if (blocked[j] == 1 || dist[j] >= best) continue;
                    if (reach == 1 && dr != 0 && dc != 0 && (blocked[r * cols + nc] == 1 || blocked[nr * cols + c] == 1)) continue;
                    best = dist[j]; bi = j;
                }
            }
            if (bi < 0) return false;
            float dx = Cx(bi) - x, dz = Cz(bi) - z, l = Mathf.Sqrt(dx * dx + dz * dz);
            if (l == 0) l = 1;
            ox = dx / l; oz = dz / l;
            return true;
        }

        public bool IsRough(float x, float z)
        {
            int i = CellOf(x, z);
            return i >= 0 && rough[i] == 1;
        }

        // True when nothing blocks the straight line between two points.
        public bool Clear(float ax, float az, float bx, float bz)
        {
            float d = Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            int steps = Mathf.CeilToInt(d / (Cell * 0.5f));
            for (int s = 1; s < steps; s++)
            {
                float t = s / (float)steps;
                int i = CellOf(ax + (bx - ax) * t, az + (bz - az) * t);
                if (i >= 0 && blocked[i] == 1) return false;
            }
            return true;
        }
    }
}
