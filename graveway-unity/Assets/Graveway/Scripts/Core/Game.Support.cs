// The Black Market's items, called in from the backpack during a run
// (playtest note 4): airstrikes down the road ahead, a helicopter overhead,
// backup survivors beside you, and a boss that never comes. You hear each
// one coming before it arrives (Tunables.Items' delay). Numbers are in
// Tunables.Support.

using System.Collections.Generic;
using UnityEngine;
using S = Graveway.Tunables.Support;

namespace Graveway
{
    public sealed partial class Game
    {
        sealed class Incoming { public string id; public float t; }
        sealed class Strip { public float z0, z1, t, tick; }                // a strip of road on fire
        struct CannonHit { public float t, x, z; }

        sealed class Heli
        {
            public GameObject go;
            public Transform rotor, tailRotor;
            public MeshRenderer shadow;
            public float x, y, z, shotT, left = S.HeliTime, leaveT;
            public bool firing, leaving;
        }

        sealed class Ally
        {
            public Rig rig;
            public Weapon w;
            public GunModel gun;
            public CharacterDef who;
            public float x, z, yaw, hp = S.BackupHp, deadT, side;
            public bool dead;
        }

        readonly List<Incoming> incoming = new List<Incoming>();
        readonly List<Strip> strips = new List<Strip>();
        readonly List<CannonHit> cannon = new List<CannonHit>();
        readonly List<Ally> allies = new List<Ally>();
        Heli heli;

        // Whether an item can't be called in right now: one of each on its
        // way at a time, one helicopter, a few backups, one boss skipped.
        public bool Busy(string id)
        {
            if (run == null) return true;
            int coming = incoming.FindAll(i => i.id == id).Count;
            return id switch
            {
                "heli" => heli != null || coming > 0,
                "backup" => allies.FindAll(a => !a.dead).Count + coming >= S.MaxBackup,
                "skipboss" => run.skipBoss,
                _ => coming > 0,
            };
        }

        // Seconds of helicopter fire left, or -1.
        public float HeliLeft => heli != null && heli.firing && !heli.leaving ? heli.left : -1;
        public int AllyCount => allies.FindAll(a => !a.dead).Count;

        // From the backpack: out it comes, and it's on its way.
        public bool CallIn(string id)
        {
            var def = Tunables.Item(id);
            if (def == null || state != State.Playing || Busy(id) || !Save.Data.backpack.Remove(id)) return false;
            Save.Persist();
            if (id == "skipboss")
            {
                run.skipBoss = true;
                Sfx.Powerup();
                hud.Toast("The next boss won't come");
                return true;
            }
            if (id == "heli" || id == "backup") Sfx.Rotor(); else Sfx.Jet();
            incoming.Add(new Incoming { id = id, t = def.delay });
            hud.Toast($"{def.name} on its way");
            return true;
        }

        // The helicopter's rotor, for as long as it's here and the game's on.
        void SupportSound() => Sfx.Loop("rotor:loop", heli != null && state == State.Playing);

        void UpdateSupport(float dt)
        {
            for (int i = incoming.Count - 1; i >= 0; i--)
            {
                incoming[i].t -= dt;
                if (incoming[i].t > 0) continue;
                string id = incoming[i].id;
                incoming.RemoveAt(i);
                Arrive(id);
            }
            UpdateCannon(dt);
            UpdateStrips(dt);
            UpdateHeli(dt);
            UpdateAllies(dt);
        }

        void Arrive(string id)
        {
            var p = player;
            switch (id)
            {
                case "strafe":
                {
                    // A line of cannon hits walking up the road, away from you.
                    float x = Mathf.Clamp(p.x + (rng.Next() - 0.5f) * 3, -World.RoadHalf, World.RoadHalf);
                    for (int i = 0; i < S.StrafeHits; i++)
                        cannon.Add(new CannonHit { t = i * 0.035f, x = x + (rng.Next() - 0.5f) * 1.2f, z = p.z - S.StrafeAhead - i * S.StrafeLength / S.StrafeHits });
                    break;
                }
                case "cluster":
                    for (int i = 0; i < S.ClusterBombs; i++)
                    {
                        float x = Mathf.Clamp(p.x + (rng.Next() * 2 - 1) * S.ClusterWidth, -World.WalkHalf, World.WalkHalf);
                        float z = p.z - S.ClusterAhead + (rng.Next() * 2 - 1) * S.ClusterDepth;
                        ExplodeLater(x, z, S.ClusterBlast, 0.05f + i * 0.08f, new BlastOpts { safe = true, scorch = true });
                    }
                    break;
                case "napalm":
                {
                    float mid = p.z - S.NapalmAhead;
                    strips.Add(new Strip { z0 = mid + S.NapalmDepth / 2, z1 = mid - S.NapalmDepth / 2, t = S.NapalmTime });
                    for (float x = -World.WalkHalf + 1; x <= World.WalkHalf - 1; x += 2.2f)
                        vfx.Emit(new Vfx.Emitter { kind = "fire", x = x, y = 0.2f, z = mid + (rng.Next() - 0.5f) * S.NapalmDepth * 0.6f,
                            life = S.NapalmTime, rate = 7, size = 1.3f, spread = 1.8f });
                    vfx.Explosion(p.x, 0.3f, mid, 3);
                    Sfx.Ignite();
                    Sfx.Boom();
                    shake = Mathf.Max(shake, 0.3f);
                    break;
                }
                case "heli":
                    heli = MakeHeli();
                    break;
                case "backup":
                    allies.Add(MakeAlly());
                    break;
            }
        }

        // ── Airstrikes ───────────────────────────────────────────────────
        void UpdateCannon(float dt)
        {
            for (int i = cannon.Count - 1; i >= 0; i--)
            {
                var c = cannon[i];
                c.t -= dt;
                if (c.t > 0) { cannon[i] = c; continue; }
                cannon.RemoveAt(i);
                foreach (var zb in zombies)
                {
                    if (zb.dead) continue;
                    float dx = zb.x - c.x, dz = zb.z - c.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < S.StrafeRadius + zb.radius) DamageZombie(zb, S.StrafeDamage, d > 0 ? dx / d : 0, d > 0 ? dz / d : -1, 2);
                }
                foreach (var pr in world.props)
                {
                    if (pr.destroyed) continue;
                    float dx = pr.x - c.x, dz = pr.z - c.z;
                    if (dx * dx + dz * dz < (S.StrafeRadius + pr.r) * (S.StrafeRadius + pr.r)) propSys.Damage(pr, S.StrafePropDamage, dx, dz, "bullet");
                }
                vfx.Impact(c.x, 0.05f, c.z, "concrete", 0, -1);
                vfx.Dust(c.x, c.z, 0.7f, vfx.N(5));
                Sfx.Shot("minigun");
                shake = Mathf.Max(shake, 0.12f);
            }
        }

        void UpdateStrips(float dt)
        {
            for (int i = strips.Count - 1; i >= 0; i--)
            {
                var s = strips[i];
                s.t -= dt;
                if (s.t <= 0) { strips.RemoveAt(i); continue; }
                s.tick -= dt;
                if (s.tick > 0) continue;
                s.tick = 0.25f;
                foreach (var zb in zombies)
                    if (!zb.dead && zb.z <= s.z0 && zb.z >= s.z1)
                    {
                        Ignite(zb, S.NapalmDps, S.NapalmBurn);
                        if (zb.state == Zombie.Mode.Idle) zb.state = Zombie.Mode.Chase;
                    }
            }
        }

        // ── The helicopter ───────────────────────────────────────────────
        static Mesh heliBody, heliRotor, heliTail;

        Heli MakeHeli()
        {
            if (heliBody == null)
            {
                heliBody = new GeoBuilder().Box(1.5f, 1.3f, 3.2f, 0, 0, 0).Box(1.2f, 0.7f, 1.2f, 0, -0.05f, 1.9f)
                    .Box(0.35f, 0.35f, 3.6f, 0, 0.25f, -3.3f).Box(0.12f, 1.1f, 0.6f, 0, 0.75f, -4.9f)
                    .Box(0.08f, 0.08f, 3.0f, -0.75f, -1.0f, 0).Box(0.08f, 0.08f, 3.0f, 0.75f, -1.0f, 0)
                    .Box(0.08f, 0.45f, 0.08f, -0.75f, -0.8f, 0.9f).Box(0.08f, 0.45f, 0.08f, 0.75f, -0.8f, 0.9f)
                    .Box(0.08f, 0.45f, 0.08f, -0.75f, -0.8f, -0.9f).Box(0.08f, 0.45f, 0.08f, 0.75f, -0.8f, -0.9f)
                    .Build("helicopter");
                heliRotor = new GeoBuilder().Box(7.5f, 0.06f, 0.3f).Box(0.3f, 0.06f, 7.5f).Build("rotor");
                heliTail = new GeoBuilder().Box(0.06f, 1.6f, 0.2f).Build("tail rotor");
            }
            var p = player;
            var h = new Heli { x = p.x - 14, y = 12, z = p.z - 32 };   // in from up the road
            h.go = HeliModel(out h.rotor, out h.tailRotor);
            h.go.transform.SetParent(worldRoot, false);
            h.shadow = Art.BlobShadow(worldRoot, 5, 0.45f);
            return h;
        }

        // The helicopter itself (the evac's too): body, rotor, tail rotor.
        static GameObject HeliModel(out Transform rotor, out Transform tail)
        {
            if (heliBody == null)
            {
                heliBody = new GeoBuilder().Box(1.5f, 1.3f, 3.2f, 0, 0, 0).Box(1.2f, 0.7f, 1.2f, 0, -0.05f, 1.9f)
                    .Box(0.35f, 0.35f, 3.6f, 0, 0.25f, -3.3f).Box(0.12f, 1.1f, 0.6f, 0, 0.75f, -4.9f)
                    .Box(0.08f, 0.08f, 3.0f, -0.75f, -1.0f, 0).Box(0.08f, 0.08f, 3.0f, 0.75f, -1.0f, 0)
                    .Box(0.08f, 0.45f, 0.08f, -0.75f, -0.8f, 0.9f).Box(0.08f, 0.45f, 0.08f, 0.75f, -0.8f, 0.9f)
                    .Box(0.08f, 0.45f, 0.08f, -0.75f, -0.8f, -0.9f).Box(0.08f, 0.45f, 0.08f, 0.75f, -0.8f, -0.9f)
                    .Build("helicopter");
                heliRotor = new GeoBuilder().Box(7.5f, 0.06f, 0.3f).Box(0.3f, 0.06f, 7.5f).Build("rotor");
                heliTail = new GeoBuilder().Box(0.06f, 1.6f, 0.2f).Build("tail rotor");
            }
            var go = new GameObject("helicopter");
            var t = go.transform;
            Art.Show(t, heliBody, Art.Lit(Hex.C(0x3c4a32), 0.12f), "body");
            rotor = Art.Show(t, heliRotor, Art.Lit(Hex.C(0x1c1e20)), "rotor").transform;
            rotor.localPosition = new Vector3(0, 0.85f, 0);
            tail = Art.Show(t, heliTail, Art.Lit(Hex.C(0x1c1e20)), "tail rotor").transform;
            tail.localPosition = new Vector3(0.2f, 0.75f, -4.9f);
            return go;
        }

        void UpdateHeli(float dt)
        {
            var h = heli;
            if (h == null) return;
            var p = player;
            h.rotor.Rotate(0, 1500 * dt, 0, Space.Self);
            h.tailRotor.Rotate(2200 * dt, 0, 0, Space.Self);
            Vector3 want;
            if (h.leaving)
            {
                h.leaveT += dt;
                want = new Vector3(h.x - 2, 18, h.z - 40);
                if (h.leaveT > 4) { DropHeli(); return; }
            }
            // Low over the road's edge just ahead, under the camera's eye
            // rather than in front of its lens.
            else want = new Vector3(p.x * 0.55f - 2.8f, 5.5f, p.z - 5);
            float k = 1 - Mathf.Exp(-(h.leaving ? 0.8f : 1.6f) * dt);
            h.x += (want.x - h.x) * k; h.y += (want.y - h.y) * k; h.z += (want.z - h.z) * k;
            var t = h.go.transform;
            t.localPosition = new Vector3(h.x, h.y, h.z);
            t.localRotation = M.Yaw(Mathf.PI) * M.Euler(h.leaving ? 0.25f : 0.08f, 0, Mathf.Sin(time * 0.9f) * 0.05f);   // nose up the road
            h.shadow.transform.localPosition = new Vector3(h.x, 0.04f, h.z);
            if (h.leaving) return;

            // Close enough: it opens up on the nearest zombie it can see, and
            // the 30 seconds start with its first shot.
            if (h.firing) { h.left -= dt; if (h.left <= 0) { h.leaving = true; return; } }
            h.shotT -= dt;
            if (h.shotT > 0 || Mathf.Abs(h.z - want.z) > 4) return;
            Zombie target = null;
            float best = S.HeliRange * S.HeliRange;
            foreach (var zb in zombies)
            {
                if (zb.dead || !zb.seen) continue;
                float dx = zb.x - h.x, dz = zb.z - h.z, d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; target = zb; }
            }
            if (target == null) return;
            h.firing = true;
            h.shotT += 1 / S.HeliRate;
            if (h.shotT < 0) h.shotT = 0;
            float ang = Mathf.Atan2(target.x - h.x, target.z - h.z) + (rng.Next() - 0.5f) * 0.08f;
            Shoot(new Vector3(h.x, h.y - 1, h.z), ang, S.HeliDamage, S.HeliRange + 4, Hex.C(0xffe2a0));
            Sfx.Shot("minigun");
        }

        void DropHeli()
        {
            if (heli == null) return;
            Destroy(heli.go);
            Destroy(heli.shadow.gameObject);
            heli = null;
            Sfx.Loop("rotor:loop", false);
        }

        // A bullet from anywhere: the helicopter's gun, a backup's. Tracers
        // start where it's fired from and ease onto the line, as the survivor's do.
        void Shoot(Vector3 from, float ang, float damage, float range, Color color, int pierce = 0, float knock = 1)
        {
            if (bullets.Count >= MaxBullets) return;
            float speed = 62 + rng.Next() * 8;
            var b = spareBullets.Count > 0 ? spareBullets.Pop() : new Bullet();
            b.x = from.x; b.z = from.z; b.dx = Mathf.Sin(ang); b.dz = Mathf.Cos(ang); b.speed = speed; b.life = range / speed + 0.05f;
            b.damage = damage; b.pierce = pierce; b.knock = knock; b.hits?.Clear(); b.fx = NoFx; b.color = color;
            b.offset = new Vector3(0, from.y - 1.4f, 0); b.travelled = 0;
            bullets.Add(b);
        }

        // ── Backup survivors ─────────────────────────────────────────────
        Ally MakeAlly()
        {
            var p = player;
            var others = System.Array.FindAll(Tunables.Characters, c => c.id != character.id);
            var who = others[(int)(rng.Next() * others.Length)];
            var w = new Weapon(Tunables.Weapons[(int)(rng.Next() * Tunables.Weapons.Length)].id, null, who);
            float side = allies.FindAll(a => !a.dead).Count % 2 == 0 ? -1 : 1;
            var a = new Ally { who = who, w = w, side = side, x = Mathf.Clamp(p.x + side * 2, -World.WalkHalf, World.WalkHalf), z = p.z + 12, yaw = Mathf.PI };
            a.rig = Rigs.Survivor(who, ring: false);
            a.rig.root.transform.SetParent(worldRoot, false);
            Art.GlowDecal(a.rig.root.transform, 0x5db4ff, 1.7f, 0.35f, Tex.Ring);          // blue: on your side
            a.gun = Models.Gun(w.def.model, null);
            a.gun.group.transform.SetParent(a.rig.gunMount, false);
            hud.Toast($"{who.name} has your back · {w.Name}");
            return a;
        }

        void UpdateAllies(float dt)
        {
            var p = player;
            for (int i = allies.Count - 1; i >= 0; i--)
            {
                var a = allies[i];
                var root = a.rig.root.transform;
                if (a.dead)
                {
                    a.deadT += dt;
                    if (a.deadT > 3.5f) { DropAlly(a); allies.RemoveAt(i); }
                    continue;
                }
                // Keep up: a step behind and to one side of you.
                float tx = p.x + a.side * 1.8f, tz = p.z + 1.6f, dx = tx - a.x, dz = tz - a.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                float speed = Mathf.Min(d * 3, p.stats.speed * 1.15f), vx = 0, vz = 0;
                if (d > 0.3f) { vx = dx / d * speed; vz = dz / d * speed; }
                a.x += vx * dt; a.z += vz * dt;
                World.ResolveColliders(world.props, ref a.x, ref a.z, 0.4f);
                a.x = Mathf.Clamp(a.x, -World.WalkHalf, World.WalkHalf);

                // Shoot the nearest zombie in range.
                var w = a.w;
                Zombie target = null;
                float best = w.stats.range * 0.95f;
                best *= best;
                foreach (var zb in zombies)
                {
                    if (zb.dead || !zb.seen) continue;
                    float ex = zb.x - a.x, ez = zb.z - a.z, d2 = ex * ex + ez * ez;
                    if (d2 < best) { best = d2; target = zb; }
                }
                w.Tick(dt, target != null);
                if (target != null)
                {
                    a.yaw = Mathf.Atan2(target.x - a.x, target.z - a.z);
                    if (w.CanFire)
                    {
                        var mz = a.gun.group.transform.TransformPoint(a.gun.muzzle);
                        if (w.Explosive) Lob(mz, target.x, target.z, w.stats.damage, w.stats.blast, safe: true, color: Hex.C(0x4a5a2a));
                        else
                            for (int k = 0; k < w.stats.pellets; k++)
                                Shoot(new Vector3(a.x, mz.y, a.z), a.yaw + (rng.Next() * 2 - 1) * w.Spread, w.stats.damage, w.stats.range,
                                    Hex.C(0x9fd2ff), w.stats.pierce, w.stats.knockback);
                        Sfx.Shot(w.def.model);
                        if (w.Consume()) w.StartReload();
                    }
                }
                else if (speed > 0.5f) a.yaw = Mathf.Atan2(vx, vz);

                // Zombies on top of them do them harm (they're after you, not
                // them, but they'll take a swipe at whoever's in the way).
                foreach (var zb in zombies)
                {
                    if (zb.dead || zb.calm) continue;
                    float ex = zb.x - a.x, ez = zb.z - a.z;
                    float reach = zb.radius + 0.4f + zb.t.reach * 0.5f;
                    if (ex * ex + ez * ez < reach * reach) a.hp -= zb.damage * zb.t.attackRate * 0.5f * dt;
                }
                if (a.hp <= 0)
                {
                    a.dead = true;
                    a.rig.Restart("Death", 0.1f);
                    Sfx.Hurt();
                    hud.Toast($"{a.who.name} is down");
                    continue;
                }

                root.localPosition = new Vector3(a.x, 0, a.z);
                root.localRotation = M.Yaw(a.yaw);
                if (speed < 0.3f) a.rig.Play("Idle_Gun");
                else if (speed < 2.6f) a.rig.Play("Walk_Gun", 0.15f, Mathf.Clamp(speed / 1.4f, 0.6f, 1.6f));
                else a.rig.Play("Run_Gun", 0.15f, Mathf.Clamp(speed / 4.5f, 0.7f, 1.5f));
            }
        }

        void DropAlly(Ally a)
        {
            Destroy(a.rig.root);
            Art.DestroyAll(a.gun.own);
        }

        // Off to another place: what's been called in but not come yet goes
        // back in the backpack; a helicopter already here, and fires and
        // cannon fire, stay behind; backup survivors come along.
        void SupportAlong()
        {
            foreach (var inc in incoming)
                if (Save.Data.backpack.Count < Tunables.BackpackSize) Save.Data.backpack.Add(inc.id);
            Save.Persist();
            incoming.Clear();
            strips.Clear();
            cannon.Clear();
            DropHeli();
            foreach (var a in allies.FindAll(a => a.dead)) DropAlly(a);
            allies.RemoveAll(a => a.dead);
            foreach (var a in allies)
            {
                a.x = Mathf.Clamp(player.x + a.side * 1.8f, -World.WalkHalf, World.WalkHalf);
                a.z = player.z + 1.6f;
            }
        }

        // Everything called in goes when the run does.
        void ClearSupport()
        {
            incoming.Clear();
            strips.Clear();
            cannon.Clear();
            DropHeli();
            foreach (var a in allies) DropAlly(a);
            allies.Clear();
        }
    }
}
