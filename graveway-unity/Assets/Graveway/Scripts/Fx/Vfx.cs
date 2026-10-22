// Fire, smoke, dust and sparks: a port of graveway/src/vfx.js.
//
// Two pools of camera-facing sprites, each drawn in one call: an additive
// pool for anything that glows (flash, fireball, sparks, embers, flames)
// and an ordinary blended pool for anything that hides what's behind it
// (smoke, dust). Every sprite has its own velocity, drag, buoyancy, size and
// colour over its life. Explosions are recipes built from those, plus a
// shock ring on the ground; burning wrecks and smoking cars are emitters
// that spawn sprites every frame until they run out.
//
// Sprite counts scale with the Graphics setting, because on a phone it's the
// overlapping smoke (overdraw), not the maths, that costs frames.

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public sealed class Vfx
    {
        const int MaxGlow = 420, MaxSmoke = 300, MaxRings = 6;

        // Colours are linear, as in the web version's shader.
        static Vector3 Rgb(int hex) => new Vector3(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);
        static readonly Vector3 Hot = Rgb(0xfff0c8), Fire = Rgb(0xff8a24), Deep = Rgb(0xe24a0c), Ember = Rgb(0xff5a14), DarkFire = Rgb(0x4a1206);
        static readonly Vector3 SmokeLight = Rgb(0x8a8680), SmokeDark = Rgb(0x262422), DustColor = Rgb(0x9a8e7c), Spark = Rgb(0xffd890);
        static Vector3 Hex3(int hex) => Rgb(hex);

        struct Sprite
        {
            public float x, y, z, vx, vy, vz, drag, lift, floor, life, age, s0, s1, grow, colorSpeed, a, fadeIn, fadePow, rot, spin;
            public Vector3 c0, c1;
        }

        sealed class Pool
        {
            // A ring, oldest first, so they draw in the order they came, as
            // on the web (smoke over older smoke).
            readonly Sprite[] items;
            int start, count;
            readonly SpriteBatch batch;

            public Pool(Material mat, int max)
            {
                items = new Sprite[max];
                batch = new SpriteBatch(mat, max);
            }

            // A full pool drops its oldest sprite, so a new explosion always shows.
            public void Add(in Sprite s)
            {
                if (count < items.Length) items[(start + count++) % items.Length] = s;
                else { items[start] = s; start = (start + 1) % items.Length; }
            }

            public void Update(float dt)
            {
                // The living move up behind each other, keeping their order.
                int n = items.Length, kept = 0;
                for (int j = 0; j < count; j++)
                {
                    int i = (start + j) % n;
                    ref var p = ref items[i];
                    p.age += dt;
                    if (p.age >= p.life) continue;
                    float drag = Mathf.Exp(-p.drag * dt);
                    p.vx *= drag; p.vy *= drag; p.vz *= drag;
                    p.vy += p.lift * dt;
                    p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
                    if (p.y < p.floor) { p.y = p.floor; p.vy = Mathf.Abs(p.vy) * 0.2f; }
                    p.rot += p.spin * dt;
                    if (kept != j) items[(start + kept) % n] = p;
                    kept++;
                }
                count = kept;
            }

            public void Draw()
            {
                for (int i = 0; i < count; i++)
                {
                    ref var p = ref items[(start + i) % items.Length];
                    float k = p.age / p.life;
                    // fade in over the first sliver, out over the rest
                    float fade = Mathf.Min(1, k / p.fadeIn) * (1 - Mathf.Pow(k, p.fadePow));
                    float ck = Mathf.Min(1, k * p.colorSpeed);
                    var c = p.c0 + (p.c1 - p.c0) * ck;
                    float size = p.s0 + (p.s1 - p.s0) * (1 - Mathf.Pow(1 - k, p.grow));
                    batch.Set(i, p.x, p.y, p.z, size, p.rot, new Vector4(c.x, c.y, c.z, p.a * fade));
                }
                batch.count = count;
                batch.Draw();
            }

            public void Clear() { count = 0; start = 0; }
            public int Count => count;
        }

        public sealed class Emitter
        {
            public string kind = "smoke";     // or "fire"
            public Prop follow;
            public float x, y, z, dy, rate = 10, life = 6, size = 1, spread = 0.6f, alpha = 0.45f, acc, age;
            public Vector3? color;
            public bool dead;
        }

        sealed class RingFx
        {
            public MeshRenderer mesh;
            public Material mat;
            public float t = 1, life = 0.4f, size = 1;
            public Color color;
        }

        readonly Pool glow, smokes;
        readonly List<Emitter> emitters = new List<Emitter>();
        readonly List<RingFx> rings = new List<RingFx>();
        public float scale = 1;               // 0.5 on Battery saver

        public Vfx(Transform parent)
        {
            glow = new Pool(Art.Sprite(Tex.Flame, true), MaxGlow);
            smokes = new Pool(Art.Sprite(Tex.Smoke, false), MaxSmoke);
            for (int i = 0; i < MaxRings; i++)
            {
                var mat = new Material(Art.Glow(Hex.C(0xffc890), Tex.Ring)) { name = "ring" };
                var r = Art.Show(parent, Art.FlatMesh, mat, "shock ring");
                r.transform.localPosition = new Vector3(0, 0.06f, 0);
                r.enabled = false;
                rings.Add(new RingFx { mesh = r, mat = mat });
            }
        }

        public void SetQuality(string q) => scale = q == "low" ? 0.5f : 1;

        public int N(float count) => Mathf.Max(1, Mathf.RoundToInt(count * scale));

        public int SpriteCount => glow.Count + smokes.Count;

        public void Clear()
        {
            glow.Clear();
            smokes.Clear();
            emitters.Clear();
            foreach (var r in rings) { r.t = r.life; r.mesh.enabled = false; }
        }

        static float R() => Random.value;

        static Sprite S(float x, float y, float z, float vx, float vy, float vz, float life, float s0, float s1, Vector3 c0, Vector3? c1 = null,
            float drag = 0, float lift = 0, float floor = float.NegativeInfinity, float a = 1, float fadeIn = 0.06f, float fadePow = 1.6f,
            float grow = 2, float colorSpeed = 1) => new Sprite
        {
            x = x, y = y, z = z, vx = vx, vy = vy, vz = vz, life = life, s0 = s0, s1 = s1, c0 = c0, c1 = c1 ?? c0,
            drag = drag, lift = lift, floor = floor, a = a, fadeIn = fadeIn, fadePow = fadePow, grow = grow, colorSpeed = colorSpeed,
            rot = R() * 6.28f, spin = (R() - 0.5f) * 1.5f,
        };

        // Lets the game add one-off sprites (a grenade's smoke trail).
        public void Smoke(float x, float y, float z, float vx, float vy, float vz, float life, float s0, float s1, Vector3 color,
            float drag = 0, float a = 1, float fadeIn = 0.06f) =>
            smokes.Add(S(x, y, z, vx, vy, vz, life, s0, s1, color, drag: drag, a: a, fadeIn: fadeIn));

        // ── Recipes ──────────────────────────────────────────────────────

        // A blast. size ~ radius in metres (barrel 3.8, car 5, tanker 9).
        public void Explosion(float x, float y, float z, float size = 4)
        {
            float s = size / 4;
            // 1. The flash: one big warm sprite for an instant.
            glow.Add(S(x, y + 0.8f, z, 0, 0, 0, 0.09f, size * 1.1f, size * 1.7f, new Vector3(1, 0.82f, 0.55f), a: 0.75f, fadeIn: 0.01f, fadePow: 1));
            // 2. The fireball: a few white-hot cores, then a mass of orange
            //    that rolls outward and upward, cooling to dark red as it slows.
            int n = N(22 * s + 6);
            for (int i = 0; i < n; i++)
            {
                float a = R() * 6.28f, up = R(), sp = (2.5f + R() * 5.5f) * s;
                bool core = i < 4;
                glow.Add(S(x + Mathf.Cos(a) * 0.3f, y + 0.4f + up * 0.8f, z + Mathf.Sin(a) * 0.3f,
                    Mathf.Cos(a) * sp * (1 - up * 0.5f), 1.5f + up * 6 * s, Mathf.Sin(a) * sp * (1 - up * 0.5f),
                    0.6f + R() * 0.6f, (0.8f + R()) * s, (2.6f + R() * 2) * s, core ? Hot : R() < 0.5f ? Fire : Deep, DarkFire,
                    drag: 3, lift: 1.6f, floor: 0.2f, a: core ? 0.8f : 0.55f, fadeIn: 0.03f, fadePow: 1.2f, colorSpeed: 1.2f));
            }
            // 3. Smoke: a dark column that keeps rising and spreading after the fire.
            n = N(14 * s + 4);
            for (int i = 0; i < n; i++)
            {
                float a = R() * 6.28f, sp = (1 + R() * 3) * s;
                smokes.Add(S(x + Mathf.Cos(a) * 0.5f, y + 0.8f + R() * 1.2f * s, z + Mathf.Sin(a) * 0.5f,
                    Mathf.Cos(a) * sp, 1.5f + R() * 2.5f * s, Mathf.Sin(a) * sp, 3 + R() * 3 * s, (1.2f + R()) * s, (4 + R() * 3) * s,
                    SmokeDark, SmokeLight, drag: 1.4f, lift: 0.9f, floor: 0.5f, a: 0.75f, fadeIn: 0.12f, fadePow: 1.1f, grow: 1.6f, colorSpeed: 0.7f));
            }
            // 4. Sparks: fast, small, falling.
            Sparks(x, y + 0.6f, z, N(26 * s + 8), 14 * s, 1.1f);
            // 5. Embers: slow glowing bits drifting up for a few seconds.
            n = N(10 * s);
            for (int i = 0; i < n; i++)
            {
                float a = R() * 6.28f;
                glow.Add(S(x, y + 1, z, Mathf.Cos(a) * 2 * s, 2 + R() * 3, Mathf.Sin(a) * 2 * s, 2 + R() * 2, 0.12f, 0.06f, Fire, Ember,
                    drag: 1.2f, lift: 0.6f, a: 0.9f));
            }
            // 6. Dust thrown along the ground, and the shock ring.
            this.Dust(x, z, size * 0.9f, N(10 * s + 4));
            Ring(x, z, size * 3.2f, 0.45f);
        }

        // dir: a cone back toward the shooter, when given.
        public void Sparks(float x, float y, float z, int n, float speed = 9, float life = 0.5f, Vector2? dir = null)
        {
            for (int i = 0; i < n; i++)
            {
                float vx, vy, vz;
                if (dir.HasValue)
                {
                    var d = dir.Value;
                    vx = d.x * speed * (0.3f + R()) + (R() - 0.5f) * speed * 0.7f;
                    vz = d.y * speed * (0.3f + R()) + (R() - 0.5f) * speed * 0.7f;
                    vy = R() * speed * 0.6f;
                }
                else
                {
                    float a = R() * 6.28f, up = R();
                    vx = Mathf.Cos(a) * speed * (0.3f + R() * 0.7f);
                    vz = Mathf.Sin(a) * speed * (0.3f + R() * 0.7f);
                    vy = up * speed * 0.8f;
                }
                glow.Add(S(x, y, z, vx, vy, vz, life * (0.4f + R() * 0.8f), 0.14f, 0.05f, Spark, Ember, drag: 1.5f, lift: -18, floor: 0.04f, a: 1, fadeIn: 0.01f));
            }
        }

        // A low ring of dust rolling out along the ground.
        public void Dust(float x, float z, float radius, int n, Vector3? color = null, float life = 2.2f)
        {
            var c = color ?? DustColor;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * 6.28f + R() * 0.4f, sp = radius * (0.8f + R() * 0.8f);
                smokes.Add(S(x + Mathf.Cos(a) * 0.4f, 0.4f + R() * 0.3f, z + Mathf.Sin(a) * 0.4f, Mathf.Cos(a) * sp, 0.3f + R() * 0.6f, Mathf.Sin(a) * sp,
                    life * (0.7f + R() * 0.6f), 0.8f + R() * 0.6f, 2.6f + R() * 1.8f, c, drag: 2.2f, lift: 0.15f, floor: 0.3f, a: 0.55f, fadeIn: 0.05f, grow: 1.4f));
            }
        }

        // A cloud of dust over an area, for collapses.
        public void DustCloud(float x, float z, float w, float d, float rot, int n, float height = 3)
        {
            float c = Mathf.Cos(rot), s = Mathf.Sin(rot);
            for (int i = 0; i < n; i++)
            {
                float lx = (R() - 0.5f) * w, lz = (R() - 0.5f) * d;
                float px = x + lx * c + lz * s, pz = z - lx * s + lz * c;
                smokes.Add(S(px, 0.5f + R() * height, pz, (R() - 0.5f) * 6, 0.5f + R() * 2, (R() - 0.5f) * 6, 3.5f + R() * 3,
                    1.6f + R(), 4.5f + R() * 3, DustColor, SmokeLight, drag: 1.2f, lift: 0.25f, floor: 0.4f, a: 0.6f, fadeIn: 0.08f, grow: 1.5f));
            }
        }

        // A puff where a bullet struck: grit off concrete and brick, sparks off metal.
        public void Impact(float x, float y, float z, string material, float dx, float dz)
        {
            var back = new Vector2(-dx, -dz);
            if (material == "metal")
            {
                Sparks(x, y, z, N(5), 6, 0.35f, back);
                return;
            }
            var col = material == "brick" ? Hex3(0x9a6a52) : material == "wood" ? Hex3(0x8a6a44) : DustColor;
            int n = N(2);
            for (int i = 0; i < n; i++)
                smokes.Add(S(x, y, z, back.x * 1.5f + (R() - 0.5f), 0.6f + R(), back.y * 1.5f + (R() - 0.5f), 0.7f + R() * 0.4f, 0.25f, 0.9f, col,
                    drag: 3, lift: 0.2f, a: 0.6f, fadeIn: 0.02f));
            if (R() < 0.4f) Sparks(x, y, z, 1, 4, 0.2f, back);
        }

        public void Ring(float x, float z, float size, float life, int color = 0xffc890) => Ring(x, z, size, life, Hex.C(color));

        public void Ring(float x, float z, float size, float life, Color color)
        {
            var r = rings.Find(q => q.t >= q.life) ?? rings[0];
            r.t = 0; r.life = life; r.size = size; r.color = color;
            r.mesh.transform.localPosition = new Vector3(x, 0.06f, z);
            r.mesh.enabled = true;
        }

        // One lick of flame, for something burning (a zombie set alight).
        public void Flame(float x, float y, float z, float size = 0.5f)
        {
            glow.Add(S(x, y, z, (R() - 0.5f) * 0.4f, 1.5f + R(), (R() - 0.5f) * 0.4f, 0.35f + R() * 0.3f, size * (0.8f + R() * 0.5f), size * 0.25f,
                R() < 0.3f ? Hot : Fire, Deep, drag: 1.2f, lift: 2, a: 0.8f, fadeIn: 0.05f, colorSpeed: 1.4f));
        }

        // The Shockwave power-up: a flash, two rings and dust rolling out
        // along the ground. No fire.
        public void Shockwave(float x, float z, float radius, Color color)
        {
            var c = new Vector3(color.r, color.g, color.b);
            glow.Add(S(x, 1, z, 0, 0, 0, 0.18f, 2, radius * 1.6f, c, a: 0.7f, fadeIn: 0.01f, fadePow: 1));
            Ring(x, z, radius * 2.4f, 0.45f, color);
            Ring(x, z, radius * 1.4f, 0.3f, Color.white);
            Dust(x, z, radius * 1.4f, N(20), DustColor, 1.4f);
            Sparks(x, 0.8f, z, N(24), 12, 0.5f);
        }

        // ── Emitters: things that keep burning or smoking ────────────────
        public Emitter Emit(Emitter e)
        {
            emitters.Add(e);
            return e;
        }

        public void Update(float dt)
        {
            for (int i = emitters.Count - 1; i >= 0; i--)
            {
                var e = emitters[i];
                e.age += dt;
                if (e.age >= e.life || e.dead) { emitters.RemoveAt(i); continue; }
                var src = e.follow;
                if (src != null && src.removed) { emitters.RemoveAt(i); continue; }
                if (src != null) { e.x = src.x; e.z = src.z; e.y = src.y + e.dy; }
                float fadeOut = Mathf.Min(1, (e.life - e.age) / 1.5f);
                e.acc += dt * e.rate * scale * fadeOut;
                while (e.acc >= 1)
                {
                    e.acc -= 1;
                    float jx = (R() - 0.5f) * e.spread, jz = (R() - 0.5f) * e.spread, sz = e.size;
                    if (e.kind == "fire")
                    {
                        glow.Add(S(e.x + jx, e.y, e.z + jz, (R() - 0.5f) * 0.6f, 1.8f + R() * 1.8f, (R() - 0.5f) * 0.6f, 0.55f + R() * 0.45f,
                            (0.9f + R() * 0.6f) * sz, (0.25f + R() * 0.25f) * sz, R() < 0.3f ? Hot : Fire, Deep,
                            drag: 1.4f, lift: 2.4f, a: 0.75f, fadeIn: 0.12f, colorSpeed: 1.3f));
                        if (R() < 0.35f)
                            smokes.Add(S(e.x + jx, e.y + 0.8f, e.z + jz, (R() - 0.5f) * 0.5f, 1.4f + R(), (R() - 0.5f) * 0.5f, 2.5f + R() * 1.5f,
                                0.8f * sz, 2.8f * sz, SmokeDark, SmokeLight, drag: 0.6f, lift: 0.4f, a: 0.55f, fadeIn: 0.15f, colorSpeed: 0.8f));
                    }
                    else
                    {
                        smokes.Add(S(e.x + jx, e.y, e.z + jz, (R() - 0.5f) * 0.4f, 1 + R() * 0.8f, (R() - 0.5f) * 0.4f, 2 + R() * 1.5f,
                            0.5f * sz, 2 * sz, e.color ?? SmokeLight, SmokeLight, drag: 0.5f, lift: 0.35f, a: e.alpha, fadeIn: 0.2f));
                    }
                }
            }
            glow.Update(dt);
            smokes.Update(dt);
            foreach (var r in rings)
            {
                if (r.t >= r.life) continue;
                r.t += dt;
                float k = Mathf.Min(1, r.t / r.life);
                float s = 0.5f + r.size * (1 - Mathf.Pow(1 - k, 2.5f));
                r.mesh.transform.localScale = new Vector3(s, 1, s);
                r.mat.SetColor("_BaseColor", new Color(r.color.r, r.color.g, r.color.b, 0.9f * (1 - k)));
                if (k >= 1) r.mesh.enabled = false;
            }
        }

        public void Draw()
        {
            glow.Draw();
            smokes.Draw();
        }
    }
}
