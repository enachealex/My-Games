// The UI's little drawings, made with UI Toolkit's vector painter so they
// stay sharp at any size: the gun silhouettes (the web version's SVG paths,
// parsed), and the gear, back, pause, heart, shield and pencil icons.

using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graveway
{
    public sealed class VectorIcon : VisualElement
    {
        readonly System.Action<Painter2D, Rect> draw;
        Color tint;

        public VectorIcon(System.Action<Painter2D, Rect> draw, Color tint)
        {
            this.draw = draw;
            this.tint = tint;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += ctx => { var p = ctx.painter2D; p.fillColor = this.tint; p.strokeColor = this.tint; this.draw(p, contentRect); };
        }

        public Color Tint
        {
            get => tint;
            set { if (tint != value) { tint = value; MarkDirtyRepaint(); } }
        }
    }

    public static class Icons
    {
        public static readonly Color Dim = Hex.C(0x9aa6a0), Text = Hex.C(0xeef2ec), Accent = Hex.C(0x9be15d);

        // The web version's gun silhouettes, on a 64 x 24 grid.
        static readonly Dictionary<string, string> GunPaths = new Dictionary<string, string>
        {
            { "pistol", "M14 6h28v6H24l-2 2v8h-7l1-10h-2z" },
            { "smg", "M6 7h40v5H30v9h-5v-9h-4l-2 4h-6l2-4H6zM46 8h8v3h-8z" },
            { "shotgun", "M2 9l10-2h46v4H28l-2 3H16l-4 4H4l3-5z" },
            { "rifle", "M2 8l9-1h41v2h8v2h-8v1H34v7h-4v-7h-6l-2 6h-5l1-6h-6L4 15z" },
            { "launcher", "M4 7h40v8H30v7h-6v-7h-4l-3 5h-7l3-5H4zM44 6h16v10H44zM24 4h8v3h-8z" },
            { "minigun", "M4 6h22v12H4zM26 7h34v2.5H26zM26 10.75h34v2.5H26zM26 14.5h34v2.5H26zM10 18h6v4h-6z" },
        };
        static readonly Dictionary<string, List<List<Vector2>>> parsed = new Dictionary<string, List<List<Vector2>>>();

        // M/L/H/V/Z, absolute or relative: all the gun paths use.
        static List<List<Vector2>> Parse(string d)
        {
            var shapes = new List<List<Vector2>>();
            List<Vector2> cur = null;
            var p = Vector2.zero;
            int i = 0;
            float Num()
            {
                while (i < d.Length && (d[i] == ' ' || d[i] == ',')) i++;
                int s = i;
                if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
                while (i < d.Length && (char.IsDigit(d[i]) || d[i] == '.')) i++;
                return float.Parse(d.Substring(s, i - s), CultureInfo.InvariantCulture);
            }
            bool MoreNumbers()
            {
                while (i < d.Length && (d[i] == ' ' || d[i] == ',')) i++;
                return i < d.Length && (char.IsDigit(d[i]) || d[i] == '-' || d[i] == '.');
            }
            char cmd = 'M';
            while (i < d.Length)
            {
                if (char.IsLetter(d[i])) cmd = d[i++];
                switch (cmd)
                {
                    case 'M': case 'm':
                        { float x = Num(), y = Num(); p = cmd == 'm' ? p + new Vector2(x, y) : new Vector2(x, y); cur = new List<Vector2> { p }; shapes.Add(cur); cmd = cmd == 'm' ? 'l' : 'L'; break; }
                    case 'L': case 'l':
                        { float x = Num(), y = Num(); p = cmd == 'l' ? p + new Vector2(x, y) : new Vector2(x, y); cur.Add(p); break; }
                    case 'H': case 'h':
                        { float x = Num(); p.x = cmd == 'h' ? p.x + x : x; cur.Add(p); break; }
                    case 'V': case 'v':
                        { float y = Num(); p.y = cmd == 'v' ? p.y + y : y; cur.Add(p); break; }
                    case 'Z': case 'z':
                        if (cur != null && cur.Count > 0) p = cur[0];
                        break;
                }
                if ((cmd == 'Z' || cmd == 'z') || !MoreNumbers()) { while (i < d.Length && !char.IsLetter(d[i])) i++; }
            }
            return shapes;
        }

        public static VectorIcon Gun(string id, Color tint)
        {
            if (!parsed.TryGetValue(id, out var shapes)) parsed[id] = shapes = Parse(GunPaths.TryGetValue(id, out var d) ? d : GunPaths["pistol"]);
            var e = new VectorIcon((p, r) =>
            {
                float k = Mathf.Min(r.width / 64, r.height / 24), ox = r.x + (r.width - 64 * k) / 2, oy = r.y + (r.height - 24 * k) / 2;
                foreach (var s in shapes)
                {
                    p.BeginPath();
                    p.MoveTo(new Vector2(ox + s[0].x * k, oy + s[0].y * k));
                    for (int j = 1; j < s.Count; j++) p.LineTo(new Vector2(ox + s[j].x * k, oy + s[j].y * k));
                    p.ClosePath();
                    p.Fill();
                }
            }, tint);
            e.AddToClassList("gun-icon");
            return e;
        }

        // Stroked icons on a 24 x 24 grid.
        static VectorIcon Stroke(Color tint, float width, System.Action<Painter2D, System.Func<float, float, Vector2>> lines)
        {
            var e = new VectorIcon((p, r) =>
            {
                float k = Mathf.Min(r.width, r.height) / 24, ox = r.x + (r.width - 24 * k) / 2, oy = r.y + (r.height - 24 * k) / 2;
                p.lineWidth = width * k;
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                lines(p, (x, y) => new Vector2(ox + x * k, oy + y * k));
            }, tint);
            e.AddToClassList("icon");
            return e;
        }

        public static VectorIcon Back() => Stroke(Text, 2.2f, (p, P) =>
        {
            p.BeginPath(); p.MoveTo(P(15, 18)); p.LineTo(P(9, 12)); p.LineTo(P(15, 6)); p.Stroke();
        });

        public static VectorIcon Pause() => Stroke(Text, 2.2f, (p, P) =>
        {
            p.BeginPath(); p.MoveTo(P(9, 5)); p.LineTo(P(9, 19)); p.Stroke();
            p.BeginPath(); p.MoveTo(P(15, 5)); p.LineTo(P(15, 19)); p.Stroke();
        });

        // A cog: a ring with eight teeth and a hole.
        public static VectorIcon Gear() => Stroke(Text, 2.2f, (p, P) =>
        {
            p.BeginPath();
            for (int i = 0; i < 16; i++)
            {
                float a0 = i / 16f * Mathf.PI * 2, a1 = (i + 1) / 16f * Mathf.PI * 2;
                float r = i % 2 == 0 ? 10 : 7.6f;
                var c0 = P(12 + Mathf.Cos(a0) * r, 12 + Mathf.Sin(a0) * r);
                if (i == 0) p.MoveTo(c0); else p.LineTo(c0);
                p.LineTo(P(12 + Mathf.Cos(a1) * r, 12 + Mathf.Sin(a1) * r));
            }
            p.ClosePath();
            p.Stroke();
            p.BeginPath(); p.Arc(P(12, 12), 3 * (P(1, 0).x - P(0, 0).x), 0, 360); p.Stroke();
        });

        public static VectorIcon Pencil() => Stroke(Dim, 2, (p, P) =>
        {
            p.BeginPath(); p.MoveTo(P(4, 20)); p.LineTo(P(8, 20)); p.LineTo(P(19, 9)); p.LineTo(P(15, 5)); p.LineTo(P(4, 16)); p.ClosePath(); p.Stroke();
            p.BeginPath(); p.MoveTo(P(13.5f, 6.5f)); p.LineTo(P(17.5f, 10.5f)); p.Stroke();
        });

        public static VectorIcon Heart()
        {
            var e = new VectorIcon((p, r) =>
            {
                float k = Mathf.Min(r.width, r.height) / 24;
                Vector2 P(float x, float y) => new Vector2(r.x + x * k, r.y + y * k);
                p.BeginPath();
                p.MoveTo(P(12, 21));
                p.BezierCurveTo(P(12, 21), P(4.5f, 16.4f), P(2.5f, 11.7f));
                p.BezierCurveTo(P(1.2f, 8.4f), P(3.3f, 5), P(6.8f, 5));
                p.BezierCurveTo(P(8.8f, 5), P(10.2f, 6), P(11, 7.4f));
                p.LineTo(P(13, 7.4f));
                p.BezierCurveTo(P(13.8f, 6), P(15.2f, 5), P(17.2f, 5));
                p.BezierCurveTo(P(20.7f, 5), P(22.8f, 8.4f), P(21.5f, 11.7f));
                p.BezierCurveTo(P(19.5f, 16.4f), P(12, 21), P(12, 21));
                p.ClosePath();
                p.Fill();
            }, Hex.C(0xff4d4d));
            e.AddToClassList("vbar-icon");
            return e;
        }

        public static VectorIcon Shield()
        {
            var e = new VectorIcon((p, r) =>
            {
                float k = Mathf.Min(r.width, r.height) / 24;
                Vector2 P(float x, float y) => new Vector2(r.x + x * k, r.y + y * k);
                p.BeginPath();
                p.MoveTo(P(12, 2)); p.LineTo(P(20, 5)); p.LineTo(P(20, 11));
                p.BezierCurveTo(P(20, 16), P(16.5f, 20.7f), P(12, 22));
                p.BezierCurveTo(P(7.5f, 20.7f), P(4, 16), P(4, 11));
                p.LineTo(P(4, 5));
                p.ClosePath();
                p.Fill();
            }, Hex.C(0x5db4ff));
            e.AddToClassList("vbar-icon");
            return e;
        }

        // ── Textures for what USS can't draw ─────────────────────────────
        static Texture2D coin, vignette;

        // A gold coin with a highlight, for prices and wallets.
        public static Texture2D Coin
        {
            get
            {
                if (coin) return coin;
                var g = new Paint(32, 32);
                g.Radial(11, 10, 0, 16, 16, 16, new[] { (0f, Hex.C(0xfff3b0)), (0.18f, Hex.C(0xfff3b0)), (0.4f, Hex.C(0xffc928)), (1f, Hex.C(0xb07a00)) },
                    0, 0, 32, 32, new Vector3(16, 16, 15.5f));
                g.Ring(16, 16, 14.5f, 2, new Color(0.47f, 0.31f, 0, 0.5f));
                return coin = g.ToTexture("coin icon");
            }
        }

        public static VisualElement CoinIcon()
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("coin");
            e.style.backgroundImage = Coin;
            return e;
        }

        // The menu's shade over the street (index.html's #menu gradient):
        // dark at the top, clear through the middle, darker at the bottom.
        static Texture2D menuShade;
        public static Texture2D MenuShade
        {
            get
            {
                if (menuShade) return menuShade;
                const int H = 128;
                var t = new Texture2D(1, H, TextureFormat.RGBA32, false) { name = "menu shade", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < H; y++)
                {
                    float f = 1 - (y + 0.5f) / H;                        // 0 at the top of the screen
                    float a = f < 0.3f ? Mathf.Lerp(0.85f, 0, f / 0.3f)
                            : f < 0.52f ? 0
                            : f < 0.82f ? Mathf.Lerp(0, 0.92f, (f - 0.52f) / 0.3f)
                            : 0.92f;
                    t.SetPixel(0, y, new Color(13 / 255f, 20 / 255f, 24 / 255f, a));
                }
                t.Apply(false, true);
                return menuShade = t;
            }
        }

        // The red edge that flashes when you're hurt.
        public static Texture2D Vignette
        {
            get
            {
                if (vignette) return vignette;
                var g = new Paint(64, 128);
                g.MaskAlpha((x, y) => 0);
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float dx = (x + 0.5f - 32) / 32, dy = (y + 0.5f - 64) / 64, d = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Sqrt(2);
                        float a = Mathf.Clamp01((d - 0.45f / Mathf.Sqrt(2)) / (1 - 0.45f / Mathf.Sqrt(2))) * 0.6f;
                        g.Rect(x, y, 1, 1, new Color(0.67f, 0, 0, a));
                    }
                return vignette = g.ToTexture("vignette");
            }
        }
    }
}
