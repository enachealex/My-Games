// Just enough of an HTML canvas to paint the web version's textures the
// same way: rectangles, circles, rings, lines, polygons, ellipses, radial
// gradients and blocky text, blended over each other in sRGB like a canvas.
// Coordinates are canvas ones (origin top-left, y down); ToTexture flips the
// rows for Unity. Edges get a pixel of anti-aliasing.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public sealed class Paint
    {
        public readonly int w, h;
        readonly Color[] px;
        readonly System.Random random;

        public Paint(int w, int h, int seed = 1)
        {
            this.w = w;
            this.h = h;
            px = new Color[w * h];
            random = new System.Random(seed);
        }

        public float R() => (float)random.NextDouble();

        public static Color Rgba(float r, float g, float b, float a = 1) => new Color(r / 255f, g / 255f, b / 255f, a);
        public static Color Hex(int rgb, float a = 1) { var c = Graveway.Hex.C(rgb); c.a = a; return c; }

        // ── Pixels ───────────────────────────────────────────────────────
        void Blend(int x, int y, Color c, float cover = 1)
        {
            if ((uint)x >= (uint)w || (uint)y >= (uint)h) return;
            float a = c.a * cover;
            if (a <= 0) return;
            ref var d = ref px[y * w + x];
            float outA = a + d.a * (1 - a);
            if (outA <= 1e-6f) { d = new Color(0, 0, 0, 0); return; }
            d.r = (c.r * a + d.r * d.a * (1 - a)) / outA;
            d.g = (c.g * a + d.g * d.a * (1 - a)) / outA;
            d.b = (c.b * a + d.b * d.a * (1 - a)) / outA;
            d.a = outA;
        }

        public void Fill(Color c)
        {
            for (int i = 0; i < px.Length; i++) px[i] = c;
        }

        public void Rect(float x, float y, float rw, float rh, Color c)
        {
            int x0 = Mathf.Max(0, Mathf.RoundToInt(x)), y0 = Mathf.Max(0, Mathf.RoundToInt(y));
            int x1 = Mathf.Min(w, Mathf.RoundToInt(x + rw)), y1 = Mathf.Min(h, Mathf.RoundToInt(y + rh));
            if (x1 <= x0 && rw > 0) x1 = Mathf.Min(w, x0 + 1);
            if (y1 <= y0 && rh > 0) y1 = Mathf.Min(h, y0 + 1);
            for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++) Blend(xx, yy, c);
        }

        // Runs `shade` for every pixel within the bounding box, which returns
        // how much of the pixel is covered (0..1).
        void Shape(float minX, float minY, float maxX, float maxY, Func<float, float, float> cover, Color c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX - 1)), y0 = Mathf.Max(0, Mathf.FloorToInt(minY - 1));
            int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(maxX + 1)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(maxY + 1));
            for (int yy = y0; yy <= y1; yy++)
                for (int xx = x0; xx <= x1; xx++)
                {
                    float k = cover(xx + 0.5f, yy + 0.5f);
                    if (k > 0) Blend(xx, yy, c, Mathf.Min(1, k));
                }
        }

        public void Circle(float cx, float cy, float r, Color c) =>
            Shape(cx - r, cy - r, cx + r, cy + r, (x, y) => Mathf.Clamp01(r - Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) + 0.5f), c);

        // An arc stroke of `width`, between angles a0 and a1 (radians,
        // clockwise from +x as on a canvas); the whole ring by default.
        public void Ring(float cx, float cy, float r, float width, Color c, float a0 = 0, float a1 = Mathf.PI * 2, bool roundCaps = false)
        {
            float hw = width / 2;
            bool whole = a1 - a0 >= Mathf.PI * 2 - 1e-3f;
            Shape(cx - r - hw, cy - r - hw, cx + r + hw, cy + r + hw, (x, y) =>
            {
                float dx = x - cx, dy = y - cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (!whole)
                {
                    float a = Mathf.Atan2(dy, dx);
                    if (!AngleIn(a, a0, a1))
                    {
                        if (!roundCaps) return 0;
                        float ex0 = cx + Mathf.Cos(a0) * r, ey0 = cy + Mathf.Sin(a0) * r;
                        float ex1 = cx + Mathf.Cos(a1) * r, ey1 = cy + Mathf.Sin(a1) * r;
                        float e = Mathf.Min(Dist(x, y, ex0, ey0), Dist(x, y, ex1, ey1));
                        return Mathf.Clamp01(hw - e + 0.5f);
                    }
                }
                return Mathf.Clamp01(hw - Mathf.Abs(d - r) + 0.5f);
            }, c);
        }

        static bool AngleIn(float a, float a0, float a1)
        {
            float span = a1 - a0, t = Mathf.Repeat(a - a0, Mathf.PI * 2);
            return t <= span;
        }

        static float Dist(float x0, float y0, float x1, float y1) => Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

        static float SegmentDist(float x, float y, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
            float t = l2 > 0 ? Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / l2) : 0;
            return Dist(x, y, ax + vx * t, ay + vy * t);
        }

        // A stroked path through `pts` (x, y pairs), with round joins and caps.
        public void Lines(IList<Vector2> pts, float width, Color c)
        {
            float hw = width / 2, minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y); }
            Shape(minX - hw, minY - hw, maxX + hw, maxY + hw, (x, y) =>
            {
                float d = float.MaxValue;
                for (int i = 0; i + 1 < pts.Count; i++) d = Mathf.Min(d, SegmentDist(x, y, pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y));
                return Mathf.Clamp01(hw - d + 0.5f);
            }, c);
        }

        public void Line(float x0, float y0, float x1, float y1, float width, Color c) =>
            Lines(new[] { new Vector2(x0, y0), new Vector2(x1, y1) }, width, c);

        // A filled polygon (even-odd), with a little edge softening.
        public void Poly(IList<Vector2> pts, Color c)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y); }
            Shape(minX, minY, maxX, maxY, (x, y) =>
            {
                bool inside = false;
                float edge = float.MaxValue;
                for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
                {
                    var a = pts[i]; var b = pts[j];
                    if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                    edge = Mathf.Min(edge, SegmentDist(x, y, a.x, a.y, b.x, b.y));
                }
                return inside ? Mathf.Clamp01(edge + 0.5f) : Mathf.Clamp01(0.5f - edge);
            }, c);
        }

        public void Ellipse(float cx, float cy, float rx, float ry, float rot, Color c)
        {
            float r = Mathf.Max(rx, ry), cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
            Shape(cx - r, cy - r, cx + r, cy + r, (x, y) =>
            {
                float dx = x - cx, dy = y - cy;
                float lx = (dx * cs + dy * sn) / rx, ly = (-dx * sn + dy * cs) / ry;
                float d = Mathf.Sqrt(lx * lx + ly * ly);
                return Mathf.Clamp01((1 - d) * Mathf.Min(rx, ry) + 0.5f);
            }, c);
        }

        // A canvas radial gradient between two circles (x0, y0, r0) and
        // (x1, y1, r1), approximated by easing the centre from one to the
        // other; `stops` are (offset, colour). Painted over a rectangle, or
        // only inside `clip` (a circle) when given.
        public void Radial(float x0, float y0, float r0, float x1, float y1, float r1, (float at, Color c)[] stops,
            float fx, float fy, float fw, float fh, Vector3? clip = null)
        {
            int ix0 = Mathf.Max(0, Mathf.FloorToInt(fx)), iy0 = Mathf.Max(0, Mathf.FloorToInt(fy));
            int ix1 = Mathf.Min(w, Mathf.CeilToInt(fx + fw)), iy1 = Mathf.Min(h, Mathf.CeilToInt(fy + fh));
            for (int yy = iy0; yy < iy1; yy++)
                for (int xx = ix0; xx < ix1; xx++)
                {
                    float x = xx + 0.5f, y = yy + 0.5f, cover = 1;
                    if (clip.HasValue)
                    {
                        var k = clip.Value;
                        cover = Mathf.Clamp01(k.z - Dist(x, y, k.x, k.y) + 0.5f);
                        if (cover <= 0) continue;
                    }
                    float t = GradientT(x, y, x0, y0, r0, x1, y1, r1);
                    Blend(xx, yy, Stop(stops, t), cover);
                }
        }

        static float GradientT(float x, float y, float x0, float y0, float r0, float x1, float y1, float r1)
        {
            // Solve for the circle (interpolated centre and radius) through (x, y).
            float lo = 0, hi = 1;
            if (Dist(x, y, x0, y0) <= r0) return 0;
            if (Dist(x, y, x1, y1) >= r1) return 1;
            for (int i = 0; i < 14; i++)
            {
                float t = (lo + hi) / 2;
                float cx = x0 + (x1 - x0) * t, cy = y0 + (y1 - y0) * t, r = r0 + (r1 - r0) * t;
                if (Dist(x, y, cx, cy) > r) lo = t; else hi = t;
            }
            return (lo + hi) / 2;
        }

        static Color Stop((float at, Color c)[] stops, float t)
        {
            if (t <= stops[0].at) return stops[0].c;
            for (int i = 1; i < stops.Length; i++)
                if (t <= stops[i].at)
                {
                    float k = (t - stops[i - 1].at) / Mathf.Max(1e-5f, stops[i].at - stops[i - 1].at);
                    return Color.Lerp(stops[i - 1].c, stops[i].c, k);
                }
            return stops[stops.Length - 1].c;
        }

        // Multiplies every pixel's alpha by `alpha(x, y)` (a canvas
        // 'destination-in' fill with a gradient).
        public void MaskAlpha(Func<float, float, float> alpha)
        {
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++) px[yy * w + xx].a *= alpha(xx + 0.5f, yy + 0.5f);
        }

        // Keeps only what's inside another painting of the same size (its alpha).
        public void MaskWith(Paint mask)
        {
            for (int i = 0; i < px.Length; i++) px[i].a *= mask.px[i].a;
        }

        // ── Blocky text ──────────────────────────────────────────────────
        // A 5 x 7 font, enough for the signs and badges. Rows top to bottom,
        // 5 bits each (16 = leftmost).
        static readonly Dictionary<char, int[]> Glyphs = new Dictionary<char, int[]>
        {
            { 'A', new[] { 14, 17, 17, 31, 17, 17, 17 } }, { 'C', new[] { 14, 17, 16, 16, 16, 17, 14 } },
            { 'D', new[] { 30, 17, 17, 17, 17, 17, 30 } }, { 'E', new[] { 31, 16, 16, 30, 16, 16, 31 } },
            { 'F', new[] { 31, 16, 16, 30, 16, 16, 16 } }, { 'M', new[] { 17, 27, 21, 21, 17, 17, 17 } },
            { 'N', new[] { 17, 25, 21, 19, 17, 17, 17 } }, { 'O', new[] { 14, 17, 17, 17, 17, 17, 14 } },
            { 'S', new[] { 15, 16, 16, 14, 1, 1, 30 } }, { 'Z', new[] { 31, 1, 2, 4, 8, 16, 31 } },
            { '2', new[] { 14, 17, 1, 2, 4, 8, 31 } }, { '×', new[] { 0, 17, 10, 4, 10, 17, 0 } },
            { ' ', new[] { 0, 0, 0, 0, 0, 0, 0 } },
        };

        // Text centred on (cx, cy), `height` pixels tall. mirror flips it left
        // to right, for surfaces Unity shows mirrored (see Tunables.cs).
        public void Text(string s, float cx, float cy, float height, Color c, bool mirror = false, float stretch = 1)
        {
            float cell = height / 7, cw = cell * stretch, gap = cw, total = s.Length * 5 * cw + (s.Length - 1) * gap;
            float x0 = cx - total / 2, y0 = cy - height / 2;
            for (int i = 0; i < s.Length; i++)
            {
                if (!Glyphs.TryGetValue(char.ToUpperInvariant(s[i]), out var rows)) continue;
                float gx = x0 + i * (5 * cw + gap);
                for (int r = 0; r < 7; r++)
                    for (int col = 0; col < 5; col++)
                    {
                        if ((rows[r] & (16 >> col)) == 0) continue;
                        float x = gx + col * cw;
                        if (mirror) x = 2 * cx - x - cw;
                        Rect(x, y0 + r * cell, cw + 0.4f, cell + 0.4f, c);
                    }
            }
        }

        // ── Out ──────────────────────────────────────────────────────────
        public Texture2D ToTexture(string name, bool repeat = false, bool mipmaps = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mipmaps)
            {
                name = name, wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear, anisoLevel = 4,
            };
            var flipped = new Color[px.Length];
            for (int y = 0; y < h; y++) Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
            t.SetPixels(flipped);
            t.Apply(mipmaps, true);
            return t;
        }
    }
}
