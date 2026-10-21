// Firing, bullets, grenades and explosions: a port of that half of
// graveway/src/game.js. Bullets are swept along their path each frame so
// fast ones never skip a zombie, stop at props and building fronts, and
// carry their ammo's effects (hollow points, armour piercing, fire). Blasts
// hurt everything in reach, shove props, and set off whatever explodes.

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public sealed partial class Game
    {
        const int MaxBullets = 260, MaxGrenades = 24;
        const float Gravity = 20;
        // Tracer colours by ammo (and red-hot under Double Damage), grenade shells by type.
        static readonly Dictionary<string, int> TracerColor = new Dictionary<string, int>
        {
            { "fmj", 0xffe2a0 }, { "buck", 0xffe2a0 }, { "hollow", 0xfff3dc }, { "ap", 0x9fd2ff }, { "incendiary", 0xff8a30 },
            { "slug", 0xffffff }, { "dragon", 0xff6a1a }, { "flechette", 0xd2e2ff },
        };
        static readonly Dictionary<string, int> ShellColor = new Dictionary<string, int>
        {
            { "he", 0x4a5a2a }, { "incendiary-g", 0xa8481a }, { "cluster", 0x8a7a2a }, { "concussion", 0x3a5a7a },
        };

        sealed class Bullet
        {
            public float x, z, dx, dz, speed, life, damage, knock, travelled;
            public int pierce;
            public List<Zombie> hits;
            public PartFx fx;
            public Color color;
            public Vector3 offset;          // muzzle minus centre: where the tracer starts
        }

        sealed class Grenade
        {
            public float x, y, z, sx, sz, vx, vy, vz, life, damage, spin;
            public Blast blast;
            public bool safe;
            public PartFx fx;
            public Color color;
        }

        public struct BlastOpts
        {
            public bool mine, scorch, vehicle, safe;
            public float fireDps, fireTime;     // sets what it hits alight: dps as a share of the hit
        }

        struct Pending
        {
            public float t, x, z;
            public Explosion blast;
            public BlastOpts opts;
        }

        readonly List<Bullet> bullets = new List<Bullet>();
        readonly Stack<Bullet> spareBullets = new Stack<Bullet>();
        readonly List<Grenade> grenades = new List<Grenade>();
        readonly List<Pending> pending = new List<Pending>();      // delayed explosions (barrel chain reactions)
        InstancedBatch bulletBatch, grenadeBatch;
        MeshRenderer muzzleFlash, laser;
        static readonly PartFx NoFx = new PartFx();

        void InitBatches()
        {
            bulletBatch = new InstancedBatch(Models.TracerMesh, Art.Instanced(false, additive: true, alpha: 0.95f), MaxBullets);
            grenadeBatch = new InstancedBatch(Models.GrenadeMesh, Art.Instanced(true, emission: Hex.C(0x1a2a08)), MaxGrenades);
            InitPickups();
            muzzleFlash = Art.Show(transform, new GeoBuilder().Plane(0.9f, 0.9f, M.I).Build("muzzle flash"), Art.Glow(Hex.C(0xffd080)), "muzzle flash");
            muzzleFlash.enabled = false;
            // Laser sight: a thin red line from the muzzle to whatever it's on.
            laser = Art.Show(transform, new GeoBuilder().Box(0.025f, 0.025f, 1, 0, 0, 0.5f).Build("laser"),
                Art.Glow(Hex.C(0xff2a2a), Texture2D.whiteTexture, 0.75f), "laser");
            laser.enabled = false;
        }

        void ClearCombat()
        {
            foreach (var b in bullets) spareBullets.Push(b);
            bullets.Clear();
            grenades.Clear();
            pending.Clear();
            throws.Clear();
            ClearPickups();
        }

        void Fire(Target target)
        {
            var p = player;
            var w = p.Weapon;
            var s = w.stats;
            bool rapid = BuffOn("rapid");
            float damage = s.damage * (BuffOn("double") ? Tunables.PowerUp("double").damageMult : 1);
            bool emptied = w.Consume(rapid ? Tunables.PowerUp("rapid").fireRateMult : 1, rapid);
            var mz = Muzzle();
            string ammo = w.fitted.TryGetValue("ammo", out var a) ? a : w.Explosive ? "he" : "fmj";
            if (w.Explosive)
            {
                float tx = target.x + (rng.Next() - 0.5f) * 2 * w.Spread * 4, tz = target.z + (rng.Next() - 0.5f) * 2 * w.Spread * 4;
                Lob(mz, tx, tz, damage, s.blast, fx: s.fx, color: Hex.C(ShellColor.TryGetValue(ammo, out var sc) ? sc : ShellColor["he"]));
            }
            else
            {
                var color = Hex.C(TracerColor.TryGetValue(ammo, out var tc) ? tc : TracerColor["fmj"]);
                // The hit test starts at the survivor's centre, not the muzzle:
                // pressed against a wall, the muzzle is already past it. The
                // tracer starts at the muzzle and eases onto the true line.
                var offset = new Vector3(mz.x - p.x, mz.y - 1.4f, mz.z - p.z);
                for (int i = 0; i < s.pellets && bullets.Count < MaxBullets; i++)
                {
                    float ang = p.yaw + (rng.Next() * 2 - 1) * w.Spread, speed = 62 + rng.Next() * 8;
                    var b = spareBullets.Count > 0 ? spareBullets.Pop() : new Bullet();
                    b.x = p.x; b.z = p.z; b.dx = Mathf.Sin(ang); b.dz = Mathf.Cos(ang); b.speed = speed; b.life = s.range / speed + 0.05f;
                    b.damage = damage; b.pierce = s.pierce; b.knock = s.knockback; b.hits?.Clear(); b.fx = s.fx ?? NoFx; b.color = color;
                    b.offset = offset; b.travelled = 0;
                    bullets.Add(b);
                }
            }
            muzzleLight.transform.position = mz + Vector3.up * 0.1f;
            muzzleLight.intensity = 12;               // web: 28 with decay 1.5
            muzzleLight.enabled = true;
            muzzleFlash.transform.position = mz;
            muzzleFlash.transform.rotation = M.Euler(-Mathf.PI / 2, 0, rng.Next() * Tau);
            muzzleFlash.transform.localScale = Vector3.one * (s.pellets > 1 || w.Explosive ? 1.8f : 1.1f + rng.Next() * 0.5f);
            muzzleFlash.enabled = true;
            flashT = 0.045f;
            p.kick = 1;
            shake = Mathf.Max(shake, s.shake);
            Sfx.Shot(w.def.model);
            if (!s.fx.quiet) Noise(p.x, p.z);
            if (emptied) Reload();
        }

        // Gunfire brings idle zombies within earshot running (not through a
        // suppressor). Checked a few times a second, not every shot.
        void Noise(float x, float z)
        {
            if (time - noiseAt < 0.25f) return;
            noiseAt = time;
            float r2 = Tunables.GunfireNoise * Tunables.GunfireNoise;
            foreach (var zb in zombies)
            {
                if (zb.dead || zb.state != Zombie.Mode.Idle || zb.calm) continue;
                float dx = zb.x - x, dz = zb.z - z;
                if (dx * dx + dz * dz < r2) zb.state = Zombie.Mode.Chase;
            }
        }

        // Sets a zombie alight: `dps` damage a second for `time` seconds. A
        // new flame doesn't stack on an old one, it keeps the stronger.
        void Ignite(Zombie z, float dps, float t)
        {
            if (z.dead) return;
            if (!(z.burnT > 0)) z.burnDps = 0;
            z.burnT = Mathf.Max(z.burnT, t);
            z.burnDps = Mathf.Max(z.burnDps, dps);
        }

        // A grenade from `from` that lands on (tx, tz): fixed horizontal
        // speed, vertical speed solved to come down on the spot. A safe one
        // (thrown by a power-up) can't hurt you.
        void Lob(Vector3 from, float tx, float tz, float damage, Blast blast, float speed = 15, bool safe = false, PartFx fx = null, Color? color = null)
        {
            if (grenades.Count >= MaxGrenades) return;
            float dx = tx - from.x, dz = tz - from.z, d = Mathf.Sqrt(dx * dx + dz * dz), t = Mathf.Max(d / speed, 0.15f);
            grenades.Add(new Grenade
            {
                x = from.x, y = from.y, z = from.z, sx = from.x, sz = from.z, vx = dx / t, vz = dz / t,
                vy = (0 - from.y + 0.5f * Gravity * t * t) / t, life = t + 0.6f, damage = damage, blast = blast, safe = safe,
                fx = fx ?? NoFx, color = color ?? Hex.C(ShellColor["he"]),
            });
        }

        void UpdateBullets(float dt)
        {
            var props = world.props;
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var b = bullets[i];
                float remaining = b.speed * dt;
                bool alive = true;
                // The first prop or building front on this step's line stops it there.
                float wallT = World.RayProps(props, b.x, b.z, b.dx, b.dz, remaining, out var wallProp);
                float facade = world.FacadeT(b.x, b.z, b.dx, b.dz, remaining);
                if (facade < wallT) { wallT = facade; wallProp = null; }
                float toWall = wallT;
                // Sweep the segment so fast bullets never skip a zombie.
                while (alive && remaining > 0)
                {
                    Zombie hit = null;
                    float hitT = Mathf.Min(remaining, toWall);
                    foreach (var z in zombies)
                    {
                        if (z.dead || (b.hits != null && b.hits.Contains(z))) continue;
                        float ox = z.x - b.x, oz = z.z - b.z, t = ox * b.dx + oz * b.dz;
                        if (t < -z.radius || t > hitT + z.radius) continue;
                        float px = ox - b.dx * t, pz = oz - b.dz * t, r = z.radius + 0.12f;
                        if (px * px + pz * pz < r * r)
                        {
                            float tt = Mathf.Max(0, t - Mathf.Sqrt(r * r - (px * px + pz * pz)));
                            if (tt < hitT) { hitT = tt; hit = z; }
                        }
                    }
                    if (hit == null)
                    {
                        if (toWall <= remaining)
                        {
                            b.x += b.dx * toWall; b.z += b.dz * toWall;
                            var pr = wallProp;
                            string mat = pr != null ? pr.def.material : "concrete";
                            float y = pr != null ? Mathf.Min(1.35f, pr.y + pr.height - 0.08f) : 1.4f;
                            if (pr != null) propSys.Damage(pr, b.damage * b.fx.props, b.dx, b.dz, "bullet");
                            vfx.Impact(b.x, y, b.z, mat, b.dx, b.dz);
                            if (mat == "metal") Sfx.Ricochet(Pan(b.x)); else Sfx.Chip(Pan(b.x));
                            alive = false;
                        }
                        else { b.x += b.dx * remaining; b.z += b.dz * remaining; }
                        break;
                    }
                    b.x += b.dx * hitT; b.z += b.dz * hitT;
                    remaining -= hitT; toWall -= hitT;
                    bool big = hit.kind == "tank" || hit.kind == "boss";
                    DamageZombie(hit, b.damage * (big ? b.fx.big : b.fx.small), b.dx, b.dz, b.knock);
                    if (b.fx.fireDps > 0) Ignite(hit, b.damage * b.fx.fireDps, b.fx.fireTime);
                    if (b.pierce > 0) { b.pierce--; (b.hits ??= new List<Zombie>()).Add(hit); }
                    else alive = false;
                }
                b.life -= dt;
                b.travelled += b.speed * dt;
                if (!alive || b.life <= 0) { bullets.RemoveAt(i); spareBullets.Push(b); }
            }
        }

        void UpdateGrenades(float dt)
        {
            var props = world.props;
            for (int i = grenades.Count - 1; i >= 0; i--)
            {
                var g = grenades[i];
                g.vy -= Gravity * dt;
                g.x += g.vx * dt; g.y += g.vy * dt; g.z += g.vz * dt;
                g.spin += dt * 12;
                g.life -= dt;
                if (rng.Next() < 0.6f) vfx.Smoke(g.x, g.y, g.z, 0, 0.3f, 0, 0.7f, 0.15f, 0.5f, new Vector3(0.7f, 0.7f, 0.68f), drag: 2, a: 0.5f, fadeIn: 0.05f);
                bool boom = g.y <= 0.15f || g.life <= 0;
                if (!boom)
                    foreach (var z in zombies)
                        if (!z.dead && g.y < 2 * z.scale && Mathf.Sqrt((z.x - g.x) * (z.x - g.x) + (z.z - g.z) * (z.z - g.z)) < z.radius + 0.2f) { boom = true; break; }
                // Not armed against props until it's 1.5 m out, so firing over
                // a car you're leaning on doesn't blow up in your hands.
                if (!boom && Mathf.Sqrt((g.x - g.sx) * (g.x - g.sx) + (g.z - g.sz) * (g.z - g.sz)) > 1.5f)
                {
                    foreach (var c in props)
                    {
                        if (c.destroyed || g.y > c.height) continue;
                        float ox = g.x - c.x, oz = g.z - c.z;
                        if (Mathf.Abs(ox) > c.r + 0.2f || Mathf.Abs(oz) > c.r + 0.2f) continue;
                        float lx = ox * c.cos - oz * c.sin, lz = ox * c.sin + oz * c.cos;
                        if (Mathf.Abs(lx) < c.hw + 0.1f && Mathf.Abs(lz) < c.hd + 0.1f) { boom = true; break; }
                    }
                }
                if (!boom) continue;
                grenades.RemoveAt(i);
                var blast = new Explosion(g.blast.radius, g.damage, g.blast.impulse);
                var opts = new BlastOpts { mine = true, scorch = true, safe = g.safe, fireDps = g.fx.fireDps, fireTime = g.fx.fireTime };
                Explode(g.x, g.z, blast, opts);
                // Cluster grenades: smaller blasts thrown out around the first.
                int n = g.fx.cluster;
                for (int k = 0; k < n; k++)
                {
                    float a = (float)k / n * Tau + rng.Next(), r = blast.radius * (0.8f + rng.Next() * 0.5f);
                    ExplodeLater(g.x + Mathf.Cos(a) * r, g.z + Mathf.Sin(a) * r,
                        new Explosion(blast.radius * 0.6f, blast.damage * 0.55f, blast.impulse * 0.5f), 0.14f + k * 0.09f,
                        new BlastOpts { mine = true, safe = g.safe, fireDps = g.fx.fireDps, fireTime = g.fx.fireTime });
                }
            }
        }

        public void ExplodeLater(float x, float z, Explosion blast, float delay, bool vehicle = false) =>
            ExplodeLater(x, z, blast, delay, new BlastOpts { vehicle = vehicle });

        public void ExplodeLater(float x, float z, Explosion blast, float delay, BlastOpts opts)
        {
            if (delay <= 0) Explode(x, z, blast, opts);
            else pending.Add(new Pending { t = delay, x = x, z = z, blast = blast, opts = opts });
        }

        void UpdatePending(float dt)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var e = pending[i];
                e.t -= dt;
                if (e.t <= 0) { pending.RemoveAt(i); Explode(e.x, e.z, e.blast, e.opts); }
                else pending[i] = e;
            }
        }

        static float Falloff(float d, float r) => Mathf.Max(0.25f, Mathf.Pow(1 - Mathf.Min(d / r, 1), 0.7f));

        // A blast: damage falling off with distance to zombies, the survivor
        // and props; a shove for everything that can move; fire, smoke and a flash.
        void Explode(float x, float z, Explosion blast, BlastOpts o)
        {
            if (world == null) return;
            float radius = blast.radius, damage = blast.damage, impulse = blast.impulse;
            for (int i = 0; i < zombies.Count; i++)
            {
                var zb = zombies[i];
                if (zb.dead) continue;
                float dx = zb.x - x, dz = zb.z - z, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > radius + zb.radius) continue;
                float u = d > 0 ? d : 1, f = Falloff(d, radius + zb.radius);
                // Anything the blast kills is thrown, harder the closer it stood.
                DamageZombie(zb, damage * f, dx / u, dz / u, impulse * 0.5f, (4 + impulse * 0.35f) * f / zb.scale);
                if (o.fireDps > 0) Ignite(zb, damage * f * o.fireDps, o.fireTime);
            }
            var p = player;
            if (state == State.Playing && !o.safe)
            {
                float d = Mathf.Sqrt((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z));
                // Your own grenades sting less than a barrel going off beside you.
                if (d < radius) HurtPlayer(damage * Falloff(d, radius) * (o.mine ? 0.4f : 0.6f), x, z, blast: true);
            }
            var props = world.props;
            for (int i = 0; i < props.Count; i++)
            {
                var pr = props[i];
                if (pr.destroyed) continue;
                float d = Mathf.Sqrt((pr.x - x) * (pr.x - x) + (pr.z - z) * (pr.z - z));
                if (d > radius + pr.r) continue;
                propSys.Damage(pr, damage * Falloff(Mathf.Max(0, d - pr.r * 0.5f), radius), pr.x - x, pr.z - z, "blast");
            }
            propSys.Impulse(x, z, radius * 1.3f, impulse);
            world.navDirty = true;

            vfx.Explosion(x, 0.3f, z, radius);
            blastLight.transform.position = new Vector3(x, 1.8f, z);
            blastLight.range = radius * 4;
            blastPeak = Mathf.Max(blastT > 0 ? blastLight.intensity : 0, (60 + radius * 12) / Mathf.PI);
            blastLight.intensity = blastPeak;
            blastLight.enabled = true;
            blastT = 0.3f;
            float pd = Mathf.Sqrt((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z));
            shake = Mathf.Max(shake, Mathf.Max(0.25f, 0.6f + radius * 0.06f - pd / 30));
            if (o.vehicle || radius > 6) Sfx.BigBoom(); else Sfx.Boom();
            Haptics.Vibrate(pd < 10 ? 60 : 25);
            if (o.scorch) propSys.Scorch(x, z, radius * 1.2f);
        }

        // ── Lights and instanced drawing ─────────────────────────────────
        void UpdateFx(float dt)
        {
            var p = player;
            // Flashlight follows the survivor's aim; a weapon light makes it bigger.
            float sin = Mathf.Sin(p.yaw), cos = Mathf.Cos(p.yaw);
            flashlight.transform.position = new Vector3(p.x + sin * 0.3f, 2.3f, p.z + cos * 0.3f);
            flashlight.transform.LookAt(new Vector3(p.x + sin * 9, 0, p.z + cos * 9));
            var wfx = p.Weapon.stats.fx;
            bool lit = wfx.light;
            flashlight.intensity = lit ? 105 : 70;
            flashlight.range = lit ? 48 : 34;
            flashlight.spotAngle = (lit ? 0.8f : 0.6f) * 2 * Mathf.Rad2Deg;
            flashlight.innerSpotAngle = flashlight.spotAngle * (1 - 0.55f);
            flashlight.enabled = state != State.Dying;

            if (flashT > 0) { flashT -= dt; if (flashT <= 0) { muzzleLight.enabled = false; muzzleFlash.enabled = false; } }
            if (blastT > 0)
            {
                blastT -= dt;
                float k = Mathf.Max(0, blastT / 0.3f);
                blastLight.intensity = blastPeak * k * k;
                if (blastT <= 0) blastLight.enabled = false;
            }

            // Tracers, coloured by ammo, red-hot under Double Damage.
            bool hot = BuffOn("double");
            var hotColor = Hex.C(0xff7a5a);
            for (int i = 0; i < bullets.Count; i++)
            {
                var b = bullets[i];
                float k = Mathf.Clamp01(1 - b.travelled / 3);
                var at = new Vector3(b.x - b.dx * 0.5f, 1.4f, b.z - b.dz * 0.5f) + b.offset * k;
                bulletBatch.Set(i, at, M.Yaw(Mathf.Atan2(b.dx, b.dz)), Vector3.one, hot ? hotColor : b.color);
            }
            bulletBatch.count = bullets.Count;
            bulletBatch.Draw();

            // Grenades in flight
            for (int i = 0; i < grenades.Count; i++)
            {
                var g = grenades[i];
                grenadeBatch.Set(i, new Vector3(g.x, g.y, g.z), M.Euler(g.spin, g.spin * 0.7f, 0), Vector3.one, g.color);
            }
            grenadeBatch.count = grenades.Count;
            grenadeBatch.Draw();

            // Laser sight, while there's something to put it on
            bool laserOn = wfx.laser && p.target.has && state == State.Playing;
            laser.enabled = laserOn;
            if (laserOn)
            {
                var m = Muzzle();
                float dx = p.target.x - m.x, dz = p.target.z - m.z;
                laser.transform.position = m;
                laser.transform.rotation = M.Yaw(Mathf.Atan2(dx, dz));
                laser.transform.localScale = new Vector3(1, 1, Mathf.Sqrt(dx * dx + dz * dz));
            }

            DrawPickups();
            propSys.Draw();
            vfx.Draw();
        }
    }
}
