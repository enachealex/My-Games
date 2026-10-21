// Every texture, painted at start-up like the web version's (initTextures
// in models.js), so the placeholder art needs no image files. Real art
// replaces these as it arrives.

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public static class Tex
    {
        static bool made;
        static Texture2D glow, ring, fire, road, sidewalk, brick, scorch, smoke, flame, condemned, danger;
        static Texture2D[] facades, facadeLights;
        static readonly Dictionary<string, Texture2D> power = new Dictionary<string, Texture2D>();

        public static Texture2D Glow { get { Make(); return glow; } }
        public static Texture2D Ring { get { Make(); return ring; } }
        public static Texture2D Fire { get { Make(); return fire; } }
        public static Texture2D Road { get { Make(); return road; } }
        public static Texture2D Sidewalk { get { Make(); return sidewalk; } }
        public static Texture2D Brick { get { Make(); return brick; } }
        public static Texture2D Scorch { get { Make(); return scorch; } }
        public static Texture2D Smoke { get { Make(); return smoke; } }
        public static Texture2D Flame { get { Make(); return flame; } }
        public static Texture2D Condemned { get { Make(); return condemned; } }
        public static Texture2D Danger { get { Make(); return danger; } }
        public static Texture2D[] Facades { get { Make(); return facades; } }
        public static Texture2D[] FacadeLights { get { Make(); return facadeLights; } }
        public static Texture2D Power(string id) { Make(); return power[id]; }

        static Color C(int rgb, float a = 1) => Paint.Hex(rgb, a);
        static Color W(float a) => new Color(1, 1, 1, a);

        public static void Make()
        {
            if (made) return;
            made = true;
            foreach (var p in Tunables.PowerUps) power[p.id] = PowerBadge(p, 128);

            var g = new Paint(128, 128);
            g.Radial(64, 64, 0, 64, 64, 64, new[] { (0f, W(1)), (0.35f, W(0.55f)), (1f, W(0)) }, 0, 0, 128, 128);
            glow = g.ToTexture("glow");

            g = new Paint(256, 256);
            g.Radial(128, 128, 256 * 0.28f, 128, 128, 128, new[] { (0f, W(0)), (0.55f, W(1)), (0.7f, W(0.35f)), (1f, W(0)) }, 0, 0, 256, 256);
            g.Radial(128, 128, 0, 128, 128, 256 * 0.3f, new[] { (0f, W(0.35f)), (1f, W(0.05f)) }, 0, 0, 256, 256, new Vector3(128, 128, 256 * 0.3f));
            ring = g.ToTexture("ring");

            // A teardrop flame for the fire barrels.
            g = new Paint(64, 128);
            var drop = new List<Vector2>();
            for (int i = 0; i <= 24; i++)
            {
                float t = i / 24f, a = t * Mathf.PI * 2;
                float x = 32 + Mathf.Sin(a) * 30 * Mathf.Sin(t * Mathf.PI) * (0.6f + 0.4f * t), y = 128 * (0.5f - 0.5f * Mathf.Cos(t * Mathf.PI));
                drop.Add(new Vector2(i < 12 ? 32 + Mathf.Abs(x - 32) : 32 - Mathf.Abs(x - 32), y * 0.96f));
            }
            var mask = new Paint(64, 128);
            mask.Poly(drop, Color.white);
            g.Radial(32, 128 * 0.75f, 2, 32, 128 * 0.6f, 128 * 0.55f, new[]
            {
                (0f, Paint.Rgba(255, 250, 210, 1)), (0.25f, Paint.Rgba(255, 190, 60, 0.95f)),
                (0.6f, Paint.Rgba(230, 70, 10, 0.55f)), (1f, Paint.Rgba(120, 20, 0, 0)),
            }, 0, 0, 64, 128);
            g.MaskWith(mask);
            fire = g.ToTexture("fire");

            road = RoadTexture();
            sidewalk = SidewalkTexture();
            MakeFacades();
            brick = BrickTexture();

            g = new Paint(128, 128, 9);
            for (int i = 0; i < 9; i++)
            {
                float a = g.R() * Mathf.PI * 2, d = g.R() * 18, r = 24 + g.R() * 30;
                float x = 64 + Mathf.Cos(a) * d, y = 64 + Mathf.Sin(a) * d;
                g.Radial(64, 64, 4, 64, 64, 64, new[] { (0f, W(0.95f)), (0.5f, W(0.6f)), (1f, W(0)) }, x - r, y - r, r * 2, r * 2, new Vector3(x, y, r));
            }
            scorch = g.ToTexture("scorch");

            // Smoke: soft blobs with uneven shading, so a sprite reads as a
            // billow rather than a disc, with the edges faded.
            g = new Paint(128, 128, 13);
            for (int i = 0; i < 16; i++)
            {
                float a = g.R() * Mathf.PI * 2, d = g.R() * 128 * 0.2f;
                float x = 64 + Mathf.Cos(a) * d, y = 64 + Mathf.Sin(a) * d, r = 128 * (0.16f + g.R() * 0.16f);
                float v = Mathf.Round(185 + g.R() * 70);
                g.Radial(x, y, 0, x, y, r, new[] { (0f, Paint.Rgba(v, v, v, 0.32f)), (1f, Paint.Rgba(v, v, v, 0)) }, x - r, y - r, r * 2, r * 2, new Vector3(x, y, r));
            }
            g.MaskAlpha((x, y) =>
            {
                float d = Mathf.Sqrt((x - 64) * (x - 64) + (y - 64) * (y - 64));
                return 1 - Mathf.Clamp01((d - 128 * 0.2f) / (64 - 128 * 0.2f));
            });
            smoke = g.ToTexture("smoke");

            g = new Paint(64, 64);
            g.Radial(32, 32, 0, 32, 32, 32, new[] { (0f, W(1)), (0.18f, W(0.85f)), (0.45f, W(0.3f)), (1f, W(0)) }, 0, 0, 64, 64);
            flame = g.ToTexture("flame");

            condemned = CondemnedTexture();
            danger = DangerTexture();
        }

        // ── The road and the street ──────────────────────────────────────
        static Texture2D RoadTexture()
        {
            var g = new Paint(256, 512, 3);
            g.Fill(C(0x2b2d31));
            for (int i = 0; i < 2600; i++)
            {
                float v = 30 + g.R() * 40;
                g.Rect(g.R() * 256, g.R() * 512, 1 + g.R() * 2, 1 + g.R() * 2, Paint.Rgba(v, v, v + 4, 0.25f + g.R() * 0.4f));
            }
            // cracks and stains
            for (int i = 0; i < 7; i++)
            {
                float width = 1 + g.R() * 1.5f, x = g.R() * 256, y = g.R() * 512;
                var pts = new List<Vector2> { new Vector2(x, y) };
                for (int s = 0; s < 6; s++) { x += (g.R() - 0.5f) * 40; y += (g.R() - 0.5f) * 40; pts.Add(new Vector2(x, y)); }
                g.Lines(pts, width, Paint.Rgba(10, 10, 12, 0.55f));
            }
            for (int i = 0; i < 4; i++)
                g.Ellipse(g.R() * 256, g.R() * 512, 10 + g.R() * 24, 6 + g.R() * 14, g.R() * 3, Paint.Rgba(40 + g.R() * 30, 8, 8, 0.18f + g.R() * 0.2f));
            // edge lines and a dashed centre line
            g.Rect(10, 0, 5, 512, Paint.Rgba(210, 210, 200, 0.55f));
            g.Rect(256 - 15, 0, 5, 512, Paint.Rgba(210, 210, 200, 0.55f));
            for (int y = 0; y < 512; y += 128) g.Rect(128 - 3, y + 20, 6, 70, Paint.Rgba(220, 180, 60, 0.7f));
            return g.ToTexture("road", true);
        }

        static Texture2D SidewalkTexture()
        {
            var g = new Paint(128, 128, 4);
            g.Fill(C(0x4a4b4f));
            for (int i = 0; i < 900; i++)
            {
                float v = 60 + g.R() * 30;
                g.Rect(g.R() * 128, g.R() * 128, 2, 2, Paint.Rgba(v, v, v, 0.35f));
            }
            var seam = Paint.Rgba(20, 20, 22, 0.6f);
            for (int y = 0; y <= 128; y += 64) g.Line(0, y, 128, y, 2, seam);
            g.Line(64, 0, 64, 128, 2, seam);
            return g.ToTexture("sidewalk", true);
        }

        // Building fronts: a 4 x 4 grid of windows, a few lit. Each variant
        // has a matching glow map so the lit windows show through the fog.
        static void MakeFacades()
        {
            facades = new Texture2D[3];
            facadeLights = new Texture2D[3];
            var rnd = new System.Random(23);
            float R() => (float)rnd.NextDouble();
            for (int v = 0; v < 3; v++)
            {
                var lit = new bool[16];
                for (int i = 0; i < 16; i++) lit[i] = R() < 0.18f + v * 0.05f;
                var map = new Paint(256, 256, 100 + v);
                var glowMap = new Paint(256, 256, 200 + v);
                map.Fill(C(0x8a8580));
                glowMap.Fill(Color.black);
                for (int i = 0; i < 500; i++) map.Rect(map.R() * 256, map.R() * 256, 3, 3, new Color(0, 0, 0, map.R() * 0.12f));
                float cw = 64, ch = 64;
                for (int i = 0; i < 16; i++)
                {
                    float x = i % 4 * cw + cw * 0.22f, y = i / 4 * ch + ch * 0.2f;
                    bool broken = !lit[i] && R() < 0.15f;
                    glowMap.Rect(x, y, cw * 0.56f, ch * 0.6f, lit[i] ? C(R() < 0.7f ? 0xffcf7a : 0x9fd4ff) : Color.black);
                    map.Rect(x, y, cw * 0.56f, ch * 0.6f, C(lit[i] ? 0xf2d9a0 : broken ? 0x0b0b0d : 0x1d2129));
                    map.Rect(x, y + ch * 0.6f, cw * 0.56f, 3, new Color(0, 0, 0, 0.35f));
                }
                facades[v] = map.ToTexture($"facade {v}", true);
                facadeLights[v] = glowMap.ToTexture($"facade {v} lights", true);
            }
        }

        static Texture2D BrickTexture()
        {
            var g = new Paint(256, 128, 6);
            g.Fill(C(0x3a2a24));
            const float bw = 64, bh = 24;
            for (int row = 0; row * bh < 128; row++)
                for (int col = -1; col * bw < 256; col++)
                {
                    float x = col * bw + (row % 2 == 1 ? bw / 2 : 0), y = row * bh, v = 120 + g.R() * 50;
                    g.Rect(x + 2, y + 2, bw - 4, bh - 4, Paint.Rgba(v, v * 0.55f, v * 0.42f));
                    for (int k = 0; k < 6; k++) g.Rect(x + g.R() * bw, y + g.R() * bh, 4, 3, new Color(0, 0, 0, g.R() * 0.18f));
                }
            return g.ToTexture("brick", true);
        }

        // A condemned facade: grimy concrete panels with cracks and dead windows.
        static Texture2D CondemnedTexture()
        {
            var g = new Paint(256, 512, 8);
            g.Fill(C(0x6a6560));
            for (int i = 0; i < 1400; i++) g.Rect(g.R() * 256, g.R() * 512, 3, 3, new Color(0, 0, 0, g.R() * 0.15f));
            for (int y = 30; y < 512; y += 72)
                for (int x = 20; x < 256; x += 78)
                {
                    g.Rect(x, y, 44, 40, C(g.R() < 0.3f ? 0x0a0a0a : 0x1a1d22));
                    g.Rect(x - 3, y + 40, 50, 5, new Color(0, 0, 0, 0.4f));
                }
            for (int i = 0; i < 9; i++)
            {
                float width = 1.5f + g.R() * 2.5f, x = g.R() * 256, y = g.R() * 512;
                var pts = new List<Vector2> { new Vector2(x, y) };
                for (int s = 0; s < 9; s++) { x += (g.R() - 0.5f) * 50; y += g.R() * 40; pts.Add(new Vector2(x, y)); }
                g.Lines(pts, width, Paint.Rgba(15, 12, 10, 0.85f));
            }
            g.Rect(0, 512 * 0.82f, 256, 512 * 0.18f, Paint.Rgba(40, 20, 10, 0.25f));
            return g.ToTexture("condemned", true);
        }

        static Texture2D DangerTexture()
        {
            var g = new Paint(256, 96);
            var yellow = C(0xe8c21e);
            g.Fill(yellow);
            for (float x = -96; x < 256; x += 40)
                g.Poly(new[] { new Vector2(x, 0), new Vector2(x + 20, 0), new Vector2(x + 20 + 96, 96), new Vector2(x + 96, 96) }, C(0x141414));
            g.Rect(28, 18, 256 - 56, 96 - 36, C(0x141414));
            g.Text("CONDEMNED", 128, 48, 34, yellow, stretch: 0.9f);
            return g.ToTexture("danger");
        }

        // ── Power-up badges ──────────────────────────────────────────────
        // A dark disc ringed in the power-up's colour with a symbol in the
        // middle, on a 128 px grid. Used on the road and in the HUD.
        static Texture2D PowerBadge(PowerUpDef def, int size)
        {
            var g = new Paint(size, size);
            float k = size / 128f;
            Vector2 P(float x, float y) => new Vector2(x * k, y * k);
            var col = def.color;
            g.Circle(64 * k, 64 * k, 58 * k, Paint.Rgba(12, 16, 18, 0.9f));
            g.Ring(64 * k, 64 * k, 54 * k, 8 * k, col);
            switch (def.id)
            {
                case "frag":
                    // a grenade: body, cap, lever and pin
                    g.Circle(62 * k, 74 * k, 24 * k, col);
                    g.Rect(52 * k, 40 * k, 20 * k, 12 * k, col);
                    var lever = new List<Vector2>();
                    for (int i = 0; i <= 10; i++)
                    {
                        float t = i / 10f;
                        // quadratic curve (70,44) -> (88,70) through control (90,46)
                        float x = (1 - t) * (1 - t) * 70 + 2 * (1 - t) * t * 90 + t * t * 88, y = (1 - t) * (1 - t) * 44 + 2 * (1 - t) * t * 46 + t * t * 70;
                        lever.Add(P(x, y));
                    }
                    g.Lines(lever, 6 * k, col);
                    g.Ring(46 * k, 42 * k, 7 * k, 4 * k, col);
                    var dark = Paint.Rgba(12, 16, 18, 0.6f);
                    g.Line(38 * k, 74 * k, 86 * k, 74 * k, 3 * k, dark);
                    g.Line(62 * k, 50 * k, 62 * k, 98 * k, 3 * k, dark);
                    break;
                case "rapid":
                    // a lightning bolt
                    g.Poly(new[] { P(72, 22), P(40, 70), P(60, 70), P(52, 106), P(88, 54), P(67, 54), P(80, 22) }, col);
                    break;
                case "double":
                    g.Text("2×", 64 * k, 68 * k, 44 * k, col, stretch: 1.1f);
                    break;
                case "shock":
                    // a burst with rings round it
                    g.Circle(64 * k, 64 * k, 10 * k, col);
                    g.Ring(64 * k, 64 * k, 24 * k, 7 * k, col);
                    for (int q = 0; q < 4; q++)
                    {
                        float a = q * Mathf.PI / 2 + Mathf.PI / 4;
                        g.Ring(64 * k, 64 * k, 38 * k, 5 * k, col, a - 0.5f, a + 0.5f, true);
                    }
                    break;
                case "vacuum":
                    // a horseshoe magnet with pale tips
                    var shoe = new List<Vector2> { P(42, 34), P(42, 62) };
                    for (int i = 0; i <= 12; i++) { float a = Mathf.PI - i / 12f * Mathf.PI; shoe.Add(P(64 + Mathf.Cos(a) * 22, 62 + Mathf.Sin(a) * 22)); }
                    shoe.Add(P(86, 34));
                    g.Lines(shoe, 16 * k, col);
                    g.Rect(34 * k, 28 * k, 16 * k, 12 * k, C(0xeef2f0));
                    g.Rect(78 * k, 28 * k, 16 * k, 12 * k, C(0xeef2f0));
                    break;
            }
            return g.ToTexture("power " + def.id);
        }
    }
}
