// Coins, health and armor, power-ups, and the little bursts of blood and
// sparkle: a port of that part of graveway/src/game.js. Coins and particles
// are drawn instanced, one call each.

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public sealed partial class Game
    {
        const int MaxCoins = 500, MaxParts = 700;

        struct Coin
        {
            public float x, y, z, vx, vy, vz, spin, age;
            public bool placed, pull;
        }

        struct Part
        {
            public float x, y, z, vx, vy, vz, life, max, size, gravity;
            public Color color;
        }

        // Frag Barrage grenades waiting their turn.
        struct Throw
        {
            public float t, x, z, damage;
            public Zombie zombie;
            public Blast blast;
        }

        readonly List<Coin> coins = new List<Coin>();
        readonly List<Part> parts = new List<Part>();
        readonly List<Throw> throws = new List<Throw>();
        readonly List<string> buffKeys = new List<string>();
        InstancedBatch coinBatch, partBatch;
        static readonly Color CoinColor = Hex.C(0xffc928);

        void InitPickups()
        {
            coinBatch = new InstancedBatch(Models.CoinMesh, Art.Instanced(true, emission: Hex.C(0x7a5200)), MaxCoins);
            partBatch = new InstancedBatch(Models.ParticleMesh, Art.Instanced(false), MaxParts);
        }

        void ClearPickups()
        {
            coins.Clear();
            parts.Clear();
        }

        public int CoinCount => coins.Count;

        // For tests: a coin lying on the road, and how many lie within r of a spot.
        public void PlaceCoin(float x, float z) => AddCoin(x, 0.35f, z, 0, 0, 0, placed: true);

        public int CoinsNear(float x, float z, float r)
        {
            int n = 0;
            foreach (var c in coins) if ((c.x - x) * (c.x - x) + (c.z - z) * (c.z - z) < r * r) n++;
            return n;
        }

        void AddCoin(float x, float y, float z, float vx, float vy, float vz, bool placed = false)
        {
            if (coins.Count >= MaxCoins) return;
            coins.Add(new Coin { x = x, y = y, z = z, vx = vx, vy = vy, vz = vz, spin = rng.Next() * Tau, placed = placed });
        }

        void Burst(float x, float y, float z, int n, Color color, float speed = 3, float up = 2, float size = 0.09f, float life = 0.6f, float gravity = 14)
        {
            for (int i = 0; i < n && parts.Count < MaxParts; i++)
            {
                float a = rng.Next() * Tau, s = speed * (0.4f + rng.Next() * 0.8f);
                parts.Add(new Part
                {
                    x = x, y = y, z = z, vx = Mathf.Cos(a) * s, vy = up * (0.3f + rng.Next()), vz = Mathf.Sin(a) * s,
                    life = life * (0.6f + rng.Next() * 0.6f), max = life, size = size * (0.6f + rng.Next() * 0.8f), color = color, gravity = gravity,
                });
            }
        }

        void TickBuffs(float dt)
        {
            var p = player;
            buffKeys.Clear();
            buffKeys.AddRange(p.buffs.Keys);
            foreach (var k in buffKeys) p.buffs[k] = Mathf.Max(0, p.buffs[k] - dt);
        }

        void UpdateCoins(float dt)
        {
            var p = player;
            bool playing = state == State.Playing;
            bool vacuum = BuffOn("vacuum");
            float mag = vacuum ? Tunables.PowerUp("vacuum").radius : p.stats.magnet;
            for (int i = coins.Count - 1; i >= 0; i--)
            {
                var c = coins[i];
                c.age += dt;
                c.spin += dt * 4;
                if (!c.placed)
                {
                    c.vy -= 22 * dt;
                    c.x += c.vx * dt; c.y += c.vy * dt; c.z += c.vz * dt;
                    if (c.y < 0.35f) { c.y = 0.35f; c.vy *= -0.35f; c.vx *= 0.6f; c.vz *= 0.6f; if (Mathf.Abs(c.vy) < 0.8f) c.placed = true; }
                }
                if (c.z > p.z + 45) { coins.RemoveAt(i); continue; }     // left behind on torn-down road
                if (playing)
                {
                    float dx = p.x - c.x, dz = p.z - c.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (!c.pull && c.age > 0.35f && (d < mag || vacuum && Vacuumed(c, p.z))) c.pull = true;
                    if (c.pull)
                    {
                        float s = vacuum ? 16 + d * 1.5f : 9 + (mag - Mathf.Min(d, mag)) * 4;
                        float step = Mathf.Min(s * dt, d), u = d > 0 ? d : 1;       // never past you
                        c.x += dx / u * step;
                        c.z += dz / u * step;
                        c.y += (1.1f - c.y) * Damp(8, dt);
                    }
                    if (d < 0.7f)
                    {
                        coins.RemoveAt(i);
                        run.coins += p.stats.coinMult;
                        Count("coins");
                        Sfx.Coin();
                        continue;
                    }
                }
                coins[i] = c;
            }
        }

        // The Coin Vacuum takes every coin, however far: any you've left
        // behind, and any on the screen (playtest, 22 October 2026).
        bool Vacuumed(in Coin c, float pz)
        {
            if (c.z > pz) return true;
            var v = cam.WorldToViewportPoint(worldRoot.TransformPoint(c.x, c.y, c.z));
            return v.z > 0 && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;
        }

        void UpdateItems(float dt)
        {
            var p = player;
            var camRot = cam.transform.rotation;
            foreach (var it in world.items)
            {
                if (it.taken) continue;
                var t = it.obj.transform;
                t.localPosition = new Vector3(it.x, 0.15f + Mathf.Sin(time * 3 + it.x) * 0.1f, it.z);
                if (it.kind == "power")
                {
                    float s = 2.4f + Mathf.Sin(time * 5 + it.z) * 0.3f;
                    it.decal.transform.localScale = new Vector3(s, 1, s);
                    // Badges always face the camera, like three's sprites.
                    foreach (var bb in it.billboards) bb.rotation = camRot;
                }
                else t.localRotation = M.Yaw(time * 1.6f + it.x);
                if (state != State.Playing) continue;
                float dx = p.x - it.x, dz = p.z - it.z;
                if (dx * dx + dz * dz >= 1.1f * 1.1f) continue;
                if (it.kind == "health" && p.hp >= p.maxHp) continue;
                if (it.kind == "armor" && p.armor >= Tunables.MaxArmor) continue;
                it.taken = true;
                it.obj.SetActive(false);
                it.decal.enabled = false;
                if (it.kind == "power")
                {
                    PowerUp(it.power);
                    Count("powerups");
                    Burst(it.x, 1, it.z, 16, Tunables.PowerUp(it.power).color, speed: 3, up: 5, gravity: 4, life: 0.9f);
                    continue;
                }
                if (it.kind == "health")
                {
                    p.hp = Mathf.Min(p.maxHp, p.hp + Tunables.PickupHealth);
                    hud.Toast($"+{Tunables.PickupHealth} health", "heal");
                }
                else
                {
                    p.armor = Mathf.Min(Tunables.MaxArmor, p.armor + Tunables.PickupArmor);
                    hud.Toast($"+{Tunables.PickupArmor} armor", "armor");
                }
                Sfx.Pickup();
                Burst(it.x, 0.6f, it.z, 12, Hex.C(it.kind == "health" ? 0x6dff8a : 0x7ac8ff), speed: 2, up: 4, gravity: 4, life: 0.8f);
            }
        }

        // ── Power-ups: used the moment they're picked up ─────────────────
        void PowerUp(string id)
        {
            var def = Tunables.PowerUp(id);
            var p = player;
            hud.Toast($"{def.name} · {def.tag}", "power", def.color);
            Sfx.Powerup();
            Haptics.Vibrate(30);
            vfx.Ring(p.x, p.z, 5, 0.4f, def.color);
            if (def.duration > 0)
            {
                p.buffs.TryGetValue(id, out var left);
                p.buffs[id] = Mathf.Min(left + def.duration, def.duration * 2);
                p.buffMax[id] = Mathf.Max(p.buffs[id], def.duration);
                // Rapid Fire starts every gun full, with no reload in the way.
                if (id == "rapid") foreach (var w in p.weapons) { w.CancelReload(); w.ammo = w.stats.ammoCapacity; }
            }
            else if (id == "frag") FragBarrage(def);
            else if (id == "shock") Shockwave(def);
        }

        // Grenades lobbed one after another at the thickest crowds in range,
        // each at a different crowd. Spares go up the road ahead.
        void FragBarrage(PowerUpDef def)
        {
            var p = player;
            float Dist(float ax, float az, float bx, float bz) => Mathf.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
            var near = zombies.FindAll(z => !z.dead && Dist(z.x, z.z, p.x, p.z) < def.range);
            float r = def.blast.radius;
            var aims = new List<Zombie>();
            float damage = def.damage * (BuffOn("double") ? Tunables.PowerUp("double").damageMult : 1);
            for (int k = 0; k < def.grenades; k++)
            {
                Zombie best = null;
                int bestN = 0;
                foreach (var z in near)
                {
                    if (Dist(z.x, z.z, p.x, p.z) < 3) continue;
                    if (aims.Exists(a => a != null && Dist(a.x, a.z, z.x, z.z) < r)) continue;
                    int n = 0;
                    foreach (var o in near) if (Dist(o.x, o.z, z.x, z.z) < r) n++;
                    if (n > bestN) { bestN = n; best = z; }
                }
                aims.Add(best);
                throws.Add(best != null
                    ? new Throw { t = k * 0.14f, zombie = best, x = best.x, z = best.z, damage = damage, blast = def.blast }
                    : new Throw { t = k * 0.14f, x = p.x + (rng.Next() - 0.5f) * 9, z = p.z - 7 - rng.Next() * 6, damage = damage, blast = def.blast });
            }
        }

        void UpdateThrows(float dt)
        {
            for (int i = throws.Count - 1; i >= 0; i--)
            {
                var th = throws[i];
                th.t -= dt;
                if (th.t > 0) { throws[i] = th; continue; }
                throws.RemoveAt(i);
                if (state != State.Playing) continue;
                var p = player;
                // Aim where the zombie is now, if it's still up.
                bool up = th.zombie != null && !th.zombie.dead;
                Lob(new Vector3(p.x, 1.7f, p.z), up ? th.zombie.x : th.x, up ? th.zombie.z : th.z, th.damage, th.blast, speed: 11, safe: true);
                Sfx.Toss();
            }
        }

        // Everything round you is hit and thrown back. Props are only shoved.
        void Shockwave(PowerUpDef def)
        {
            var p = player;
            for (int i = 0; i < zombies.Count; i++)
            {
                var z = zombies[i];
                if (z.dead) continue;
                float dx = z.x - p.x, dz = z.z - p.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > def.radius + z.radius) continue;
                float u = d > 0 ? d : 1, f = Mathf.Max(0.35f, 1 - d / def.radius);
                DamageZombie(z, def.damage * f, dx / u, dz / u, def.impulse * f, 5 * f / z.scale, blast: true);
            }
            propSys.Impulse(p.x, p.z, def.radius, def.impulse * 0.6f);
            world.navDirty = true;
            vfx.Shockwave(p.x, p.z, def.radius, def.color);
            shake = Mathf.Max(shake, 0.5f);
            Sfx.Shock();
            Haptics.Vibrate(60);
        }

        void UpdateParticles(float dt)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var q = parts[i];
                q.life -= dt;
                if (q.life <= 0) { parts.RemoveAt(i); continue; }
                q.vy -= q.gravity * dt;
                q.x += q.vx * dt; q.y += q.vy * dt; q.z += q.vz * dt;
                if (q.y < 0.03f) { q.y = 0.03f; q.vy = 0; q.vx *= 0.8f; q.vz *= 0.8f; }
                parts[i] = q;
            }
        }

        void DrawPickups()
        {
            for (int i = 0; i < coins.Count; i++)
            {
                var c = coins[i];
                float bob = c.placed && !c.pull ? Mathf.Sin(time * 3 + c.x + c.z) * 0.08f : 0;
                coinBatch.Set(i, new Vector3(c.x, c.y + bob, c.z), M.Yaw(c.spin), Vector3.one, CoinColor);
            }
            coinBatch.count = coins.Count;
            coinBatch.Draw();

            for (int i = 0; i < parts.Count; i++)
            {
                var q = parts[i];
                float s = q.size * Mathf.Min(1, q.life / q.max * 2);
                partBatch.Set(i, new Vector3(q.x, q.y, q.z), M.Euler(q.vx, q.vz, 0), Vector3.one * s, q.color);
            }
            partBatch.count = parts.Count;
            partBatch.Draw();
        }
    }
}
