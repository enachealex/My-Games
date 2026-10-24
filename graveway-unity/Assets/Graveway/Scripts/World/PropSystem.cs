// What happens to the street when the fighting reaches it: a port of
// graveway/src/props.js.
//
// - Every prop takes damage from bullets, blasts and smashing tanks, scaled
//   by its bulletMult / blastMult (Tunables.Props).
// - Cars and the fuel tanker smoke when hurt, catch fire, then blow up: the
//   blast throws the wreck into the air, it tumbles, lands (maybe on its
//   roof) and burns.
// - Explosive barrels go off and set off their neighbours.
// - Walls and lamp posts topple away from the hit and crush what they land
//   on. A condemned building creaks, then comes down across the road as a
//   rubble field with slabs in it.
// - Barricades, fire barrels and slabs break into flying debris and rubble.
// - Blasts shove anything with mass. Light things get thrown.
//
// Every change to what stands where sets world.navDirty, so the zombies'
// navigation grid is rebuilt and they re-route around (or over) the new
// layout. Debris chunks are instanced: one draw call for all of them.

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public sealed class PropSystem
    {
        const int MaxDebris = 420;
        const float Gravity = 22;

        static readonly Dictionary<string, int[]> DebrisColors = new Dictionary<string, int[]>
        {
            { "metal", new[] { 0x3a3c40, 0x5a5c60, 0x2a2622 } },
            { "concrete", new[] { 0x8d8a82, 0x7a766e, 0x6a665e } },
            { "brick", new[] { 0x8a5a44, 0x7a4a36, 0x6a6460 } },
            { "wood", new[] { 0x7a5a34, 0x5a4024 } },
        };

        struct Debris
        {
            public float x, y, z, vx, vy, vz, rx, ry, wx, wy, size, life;
            public Color color;
            public bool rest;
        }

        sealed class Topple
        {
            public Prop p;
            public float t, fx, fz, creak, dur;
            public Vector3 axis;
            public Quaternion q0;
        }

        // A fallen prop's resting place: a box from its base out along the fall.
        public struct Footprint
        {
            public float x, z, hw, hd, rot, cos, sin;
        }

        readonly Game game;
        readonly List<Debris> debris = new List<Debris>();
        readonly List<Topple> toppling = new List<Topple>();
        readonly InstancedBatch batch;

        public PropSystem(Game game)
        {
            this.game = game;
            batch = new InstancedBatch(Models.ParticleMesh, Art.Instanced(true), MaxDebris);
        }

        public void Reset()
        {
            debris.Clear();
            toppling.Clear();
        }

        World World => game.world;
        Vfx Vfx => game.vfx;
        Rng Rng => game.rng;

        // ── Damage ───────────────────────────────────────────────────────
        public void Damage(Prop p, float dmg, float dx = 0, float dz = 0, string cause = "bullet")
        {
            if (p.destroyed || p.indestructible) return;
            var def = p.def;
            if (cause == "blast") dmg *= def.blastMult;
            else if (cause == "bullet") dmg *= def.bulletMult;
            p.hp -= dmg;
            if (p.model.meshes.Count > 0 && !p.wreck)
            {
                if (p.flash <= 0) p.model.SetFlash(true);
                p.flash = 0.05f;
            }
            float k = Mathf.Max(0, p.hp / p.maxHp);
            // Walls and condemned buildings show their wounds.
            if ((p.kind == "wall" || p.kind == "condemned") && p.model.mats.Count > 0)
            {
                float v = 0.45f + 0.55f * k;
                p.model.mats[0][0].SetColor("_BaseColor", new Color(v, v, v));
            }
            // Vehicles smoke, then burn, before they go.
            if (def.burns && p.hp > 0)
            {
                float big = p.kind == "tanker" ? 1.6f : 1;
                if (k < def.smokeAt && p.smoking == null)
                    p.smoking = Vfx.Emit(new Vfx.Emitter { kind = "smoke", follow = p, dy = 1.3f, rate = 6, life = 1e9f, size = big, alpha = 0.4f });
                if (k < def.fireAt && p.burning == null)
                {
                    p.burning = Vfx.Emit(new Vfx.Emitter { kind = "fire", follow = p, dy = 1.1f, rate = 16, life = 1e9f, size = 1.1f * big, spread = 1 });
                    Sfx.Ignite();
                }
            }
            if (p.hp <= 0) Destroy(p, dx, dz, cause);
        }

        void Destroy(Prop p, float dx, float dz, string cause)
        {
            var g = game;
            g.ChallengeWreck(cause);
            p.hp = 0;
            p.model.SetFlash(false);
            p.flash = 0;
            World.navDirty = true;
            float l = Mathf.Sqrt(dx * dx + dz * dz);
            if (l == 0) l = 1;
            dx /= l; dz /= l;
            var def = p.def;

            if (p.kind == "car" || p.kind == "tanker" || p.kind == "truck")
            {
                Wreck(p, cause);
                return;
            }
            p.destroyed = true;
            if (def.topples)
            {
                StartTopple(p, dx, dz);
                return;
            }
            p.group.gameObject.SetActive(false);
            if (p.kind == "barrel")
            {
                Spray(p, 12, "metal", 7);
                // A beat of delay makes chain reactions ripple instead of popping at once.
                g.ExplodeLater(p.x, p.z, def.explodes, cause == "blast" ? 0.09f : 0);
                Scorch(p.x, p.z, 4.5f);
                return;
            }
            Spray(p, p.kind == "barricade" ? 18 : 12, def.material, 5);
            Vfx.Dust(p.x, p.z, 1.5f, Vfx.N(5), null, 1.6f);
            LeaveRubble(p, p.x, p.z, p.hw * 2, p.hd * 2 + 0.6f, p.rot);
            Sfx.Crumble();
            g.shake = Mathf.Max(g.shake, 0.2f);
        }

        // A vehicle goes up: blast, burnt-out shell thrown into the air, fire.
        void Wreck(Prop p, string cause)
        {
            var g = game;
            var def = p.def;
            var m = p.model;
            p.wreck = true;
            p.indestructible = true;               // what's left can't be destroyed again
            if (m.burnBody) m.Replace(m.burnBody, Models.BurntBody);
            if (m.burnCabin) m.Replace(m.burnCabin, Models.BurntGlass);
            if (m.burnLights) m.Replace(m.burnLights, Models.BurntLamp);
            if (m.burnHeads) m.Replace(m.burnHeads, Models.BurntLamp);
            foreach (var r in m.burnAlso) m.Replace(r, Models.BurntBody);
            if (p.smoking != null) p.smoking.dead = true;
            if (p.burning != null) p.burning.dead = true;
            bool big = p.kind == "tanker";
            g.ExplodeLater(p.x, p.z, def.explodes, cause == "blast" ? 0.12f : 0, vehicle: true);
            // Up it goes, tumbling.
            var R = Rng;
            p.airborne = true;
            p.y = Mathf.Max(p.y, 0.05f);
            p.vy = big ? 5 + R.Next() * 2 : 7 + R.Next() * 4;
            p.vx += (R.Next() - 0.5f) * 3;
            p.vz += (R.Next() - 0.5f) * 3;
            p.spin = new Vector3((R.Next() - 0.5f) * (big ? 1.5f : 5), (R.Next() - 0.5f) * 2, (R.Next() - 0.5f) * (big ? 1.5f : 6));
            p.quat = p.group.localRotation;
            p.moving = true;
            Spray(p, big ? 30 : 18, "metal", big ? 12 : 9, p.x, p.z, 1.2f);
            p.burning = Vfx.Emit(new Vfx.Emitter { kind = "fire", follow = p, dy = 0.9f, rate = big ? 34 : 22, life = big ? 16 : 11, size = big ? 2.6f : 1.7f, spread = big ? 3.4f : 1.6f });
            p.smoking = Vfx.Emit(new Vfx.Emitter { kind = "smoke", follow = p, dy = 1.6f, rate = big ? 10 : 6, life = 30, size = big ? 2.2f : 1.5f, alpha = 0.5f, color = new Vector3(0.16f, 0.15f, 0.14f) });
            Scorch(p.x, p.z, big ? 11 : 6.5f);
            if (big) g.hud.Banner("Tanker down!", null, 1.4f);
        }

        // Things that fall over: walls, lamp posts, the condemned building.
        void StartTopple(Prop p, float dx, float dz)
        {
            float fx, fz;
            if (p.kind == "condemned")
            {
                // Always into the road: its local +x.
                fx = p.cos; fz = -p.sin;
            }
            else if (p.kind == "wall")
            {
                // Along the wall's own z, whichever way the hit pushed it.
                float s = Mathf.Sign(dx * p.sin + dz * p.cos);       // (Mathf.Sign(0) is 1, like Math.sign(v || 1))
                fx = p.sin * s; fz = p.cos * s;
            }
            else
            {
                // Lamps fall the way they were hit, but never into the buildings.
                fx = dx; fz = dz;
                if (Mathf.Sign(fx) == Mathf.Sign(p.x) && Mathf.Abs(p.x) > 3) fx = -fx;
                float l = Mathf.Sqrt(fx * fx + fz * fz);
                if (l == 0) l = 1;
                fx /= l; fz /= l;
            }
            if (p.isLamp) { p.model.head.enabled = false; p.model.pool.enabled = false; }
            bool collapse = p.def.collapse;
            toppling.Add(new Topple
            {
                p = p, fx = fx, fz = fz, axis = new Vector3(fz, 0, -fx), q0 = p.group.localRotation,
                creak = collapse ? 1.3f : 0, dur = 0.35f + 0.28f * Mathf.Sqrt(p.height),
            });
            if (collapse)
            {
                game.hud.Banner("It's coming down!", "Get clear of the building", 1.6f);
                Sfx.Rumble();
                Haptics.Vibrate(40, 60, 40);
            }
            else Sfx.Crumble();
        }

        public Footprint FootprintOf(Prop p, float fx, float fz)
        {
            float len = p.height;
            // Its width across the fall direction.
            float px = -fz, pz = fx;
            float lpx = px * p.cos - pz * p.sin, lpz = px * p.sin + pz * p.cos;
            float halfW = Mathf.Abs(lpx) * p.hw + Mathf.Abs(lpz) * p.hd;
            float rot = Mathf.Atan2(fx, fz);
            return new Footprint
            {
                x = p.x + fx * len / 2, z = p.z + fz * len / 2, hw = Mathf.Max(halfW, 0.3f), hd = len / 2,
                rot = rot, cos = Mathf.Cos(rot), sin = Mathf.Sin(rot),
            };
        }

        void Land(Topple tp)
        {
            var g = game;
            var p = tp.p;
            var def = p.def;
            var fp = FootprintOf(p, tp.fx, tp.fz);
            g.Crush(fp, def.crush > 0 ? def.crush : 100, tp.fx, tp.fz);
            if (p.isLamp)
            {
                // The pole stays where it fell, a low thing to step over.
                World.AddRough(new RoughArea { x = fp.x, z = fp.z, hw = fp.hw, hd = fp.hd, cos = fp.cos, sin = fp.sin, chunk = p.chunk });
                Spray(p, 6, "metal", 4, fp.x, fp.z, 0.4f);
                Vfx.Sparks(p.x + tp.fx * p.height, 0.4f, p.z + tp.fz * p.height, Vfx.N(10), 6, 0.5f);
                Vfx.Dust(fp.x, fp.z, 1.2f, Vfx.N(4));
                Sfx.Clang();
                g.shake = Mathf.Max(g.shake, 0.2f);
                return;
            }
            p.group.gameObject.SetActive(false);
            if (p.kind == "condemned")
            {
                CollapseRubble(p, fp);
                return;
            }
            Spray(p, 26, def.material, 5, fp.x, fp.z, 0.6f);
            Vfx.DustCloud(fp.x, fp.z, fp.hw * 2, fp.hd * 2, fp.rot, Vfx.N(8), 1.5f);
            LeaveRubble(p, fp.x, fp.z, fp.hw * 2, fp.hd * 2, fp.rot);
            g.shake = Mathf.Max(g.shake, 0.45f);
            Sfx.Slam();
        }

        // The condemned building's landing: a rubble field with slabs left
        // standing in it, a wall of dust, a lot of shaking.
        void CollapseRubble(Prop p, Footprint fp)
        {
            var g = game;
            var R = Rng;
            var mesh = Models.Rubble(R, fp.hw * 2, fp.hd * 2);
            if (PlaceRubble(mesh, Hex.C(0x75706a), fp.x, fp.z, fp.rot))
                World.AddRough(new RoughArea { x = fp.x, z = fp.z, hw = fp.hw, hd = fp.hd, cos = fp.cos, sin = fp.sin, chunk = World.ChunkAt(fp.z) });
            // Two or three big slabs that block outright, with gaps between.
            int n = 2 + (int)(R.Next() * 2);
            for (int i = 0; i < n; i++)
            {
                float along = (i + 0.5f) / n * fp.hd * 2 - fp.hd + (R.Next() - 0.5f) * 1.5f;
                float across = (R.Next() - 0.5f) * fp.hw;
                float x = fp.x + across * fp.cos + along * fp.sin;
                float z = fp.z - across * fp.sin + along * fp.cos;
                if (Mathf.Abs(x) > World.WalkHalf - 1) continue;
                var slab = Models.Slab(1.6f + R.Next() * 1.4f, 0.9f + R.Next() * 0.8f, 1.2f + R.Next() * 1.2f);
                float tilt = (R.Next() - 0.5f) * 0.3f;
                var sp = World.AddProp("slab", slab, x, z, fp.rot + (R.Next() - 0.5f) * 0.8f, World.ChunkAt(z));
                if (sp != null) sp.group.localRotation = M.Euler(tilt, sp.rot, 0);
            }
            Spray(p, 60, "concrete", 8, fp.x, fp.z, 1.5f);
            Vfx.DustCloud(fp.x, fp.z, fp.hw * 2.4f, fp.hd * 2.2f, fp.rot, Vfx.N(46), 6);
            Vfx.Dust(fp.x, fp.z, 9, Vfx.N(22), null, 4);
            Vfx.Ring(fp.x, fp.z, 26, 0.7f);
            g.shake = Mathf.Max(g.shake, 1.1f);
            Sfx.Collapse();
            Haptics.Vibrate(120, 40, 200);
        }

        void LeaveRubble(Prop p, float x, float z, float w, float d, float rot)
        {
            if (!p.def.rubble) return;
            var color = DebrisColors.TryGetValue(p.def.material, out var cs) ? Hex.C(cs[0]) : Hex.C(0x6d625a);
            if (!PlaceRubble(Models.Rubble(Rng, w, d), color, x, z, rot)) return;
            World.AddRough(new RoughArea { x = x, z = z, hw = w / 2, hd = d / 2, cos = Mathf.Cos(rot), sin = Mathf.Sin(rot), chunk = World.ChunkAt(z) });
        }

        bool PlaceRubble(Mesh mesh, Color color, float x, float z, float rot)
        {
            var go = new GameObject("rubble");
            var r = Art.Show(go.transform, mesh, Art.Lit(color), "rubble");
            r.transform.localPosition = Vector3.zero;
            go.transform.position = new Vector3(x, 0, z);
            go.transform.rotation = M.Yaw(rot);
            return World.AddToChunk(z, go, mesh) >= 0;
        }

        public void Scorch(float x, float z, float size)
        {
            var go = new GameObject("scorch");
            Models.Scorch(go.transform, size);
            go.transform.position = new Vector3(x, 0.025f, z);
            go.transform.rotation = M.Yaw(Rng.Next() * 6.28f);
            World.AddToChunk(z, go);
        }

        // Bits flying off a broken prop.
        void Spray(Prop p, int n, string material, float speed) => Spray(p, n, material, speed, p.x, p.z, p.height * 0.6f);

        void Spray(Prop p, int n, string material, float speed, float x, float z, float y)
        {
            var rng = Rng;
            if (!DebrisColors.TryGetValue(material, out var colors)) colors = DebrisColors["concrete"];
            int count = Mathf.RoundToInt(n * (Vfx.scale < 1 ? 0.6f : 1));
            for (int i = 0; i < count; i++)
            {
                if (debris.Count >= MaxDebris) debris.RemoveAt(0);
                float a = rng.Next() * Mathf.PI * 2, s = speed * (0.3f + rng.Next() * 0.7f);
                bool big = rng.Next() < 0.15f;
                Color.RGBToHSV(Hex.C(colors[(int)(rng.Next() * colors.Length)]), out var hh, out var ss, out var vv);
                var c = Color.HSVToRGB(hh, ss, Mathf.Clamp01(vv + (rng.Next() - 0.5f) * 0.1f));
                debris.Add(new Debris
                {
                    x = x + (rng.Next() - 0.5f) * p.hw * 1.5f, y = y * (0.4f + rng.Next() * 0.8f), z = z + (rng.Next() - 0.5f) * p.hd * 1.5f,
                    vx = Mathf.Cos(a) * s, vy = 3 + rng.Next() * 6, vz = Mathf.Sin(a) * s,
                    rx = rng.Next() * 6, ry = rng.Next() * 6, wx = (rng.Next() - 0.5f) * 16, wy = (rng.Next() - 0.5f) * 16,
                    size = big ? 0.35f + rng.Next() * 0.35f : 0.1f + rng.Next() * 0.22f,
                    color = c, life = 6 + rng.Next() * 4,
                });
            }
        }

        // A blast's shove: props with mass slide, spin and (if light, or the
        // blast is big) fly; debris gets kicked about.
        public void Impulse(float x, float z, float radius, float strength)
        {
            var R = Rng;
            foreach (var p in World.props)
            {
                if (p.destroyed || p.def.mass <= 0) continue;
                float dx = p.x - x, dz = p.z - z, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > radius + p.r) continue;
                float k = strength * (1 - Mathf.Min(d, radius) / (radius + p.r)) / p.def.mass;
                float u = d > 0 ? d : 1;
                p.vx += dx / u * k;
                p.vz += dz / u * k;
                p.va += (R.Next() - 0.5f) * k * 0.6f;
                p.moving = true;
                if (k > 2.5f && !p.airborne)
                {
                    // Thrown: up into the air, tumbling.
                    p.airborne = true;
                    p.vy = Mathf.Min(12, k * 1.1f);
                    p.quat = p.group.localRotation;
                    p.spin = new Vector3((R.Next() - 0.5f) * k, (R.Next() - 0.5f) * k * 0.5f, (R.Next() - 0.5f) * k);
                }
            }
            for (int i = 0; i < debris.Count; i++)
            {
                var b = debris[i];
                float dx = b.x - x, dz = b.z - z, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > radius * 1.5f) continue;
                float k = strength * (1 - d / (radius * 1.5f)), u = d > 0 ? d : 1;
                b.vx += dx / u * k; b.vz += dz / u * k; b.vy += k * 0.6f; b.rest = false;
                debris[i] = b;
            }
        }

        // ── Frame ────────────────────────────────────────────────────────
        public void Update(float dt)
        {
            var props = World.props;
            foreach (var p in props)
            {
                if (p.flash > 0)
                {
                    p.flash -= dt;
                    if (p.flash <= 0) p.model.SetFlash(false);
                }
                if (!p.moving || p.destroyed) continue;
                p.x += p.vx * dt; p.z += p.vz * dt;
                if (p.airborne)
                {
                    p.vy -= Gravity * dt;
                    p.y += p.vy * dt;
                    // tumble: integrate angular velocity into the orientation
                    float w = p.spin.magnitude;
                    if (w > 1e-4f) p.quat = Quaternion.AngleAxis(w * dt * Mathf.Rad2Deg, p.spin / w) * p.quat.Value;
                    if (p.y <= 0 && p.vy < 0)
                    {
                        p.y = 0;
                        if (p.vy < -4)
                        {
                            // bounce, losing most of the spin
                            p.vy = -p.vy * 0.28f;
                            p.spin *= 0.45f;
                            p.vx *= 0.6f; p.vz *= 0.6f;
                            game.shake = Mathf.Max(game.shake, 0.25f);
                            Sfx.Clang();
                            Vfx.Dust(p.x, p.z, 1.6f, Vfx.N(4));
                        }
                        else Settle(p);
                    }
                    p.group.localRotation = p.quat.Value;
                    p.group.localPosition = new Vector3(p.x, p.y, p.z);
                }
                else
                {
                    p.rot += p.va * dt;
                    float sp = Mathf.Sqrt(p.vx * p.vx + p.vz * p.vz);
                    float decel = Mathf.Min(sp, 9 * dt);
                    if (sp > 0) { p.vx -= p.vx / sp * decel; p.vz -= p.vz / sp * decel; }
                    p.va *= Mathf.Exp(-3 * dt);
                    if (p.quat.HasValue)
                    {
                        p.quat = M.Yaw(p.va * dt) * p.quat.Value;
                        p.group.localRotation = p.quat.Value;
                    }
                    else p.group.localRotation = M.Yaw(p.rot);
                    if (sp < 0.05f && Mathf.Abs(p.va) < 0.05f) { p.moving = false; p.vx = p.vz = p.va = 0; World.navDirty = true; }
                }
                float lim = World.WalkHalf - Mathf.Min(p.hw, p.hd);
                if (Mathf.Abs(p.x) > lim) { p.x = Mathf.Sign(p.x) * lim; p.vx *= -0.3f; }
                p.cos = Mathf.Cos(p.rot); p.sin = Mathf.Sin(p.rot);
                var pos = p.group.localPosition;
                p.group.localPosition = new Vector3(p.x, pos.y, p.z);
            }

            // Toppling: creak (for a collapse), then swing down about the base.
            for (int i = toppling.Count - 1; i >= 0; i--)
            {
                var tp = toppling[i];
                tp.t += dt;
                var p = tp.p;
                if (tp.t < tp.creak)
                {
                    // shudder and shed dust before it goes
                    float j = Mathf.Sin(tp.t * 40) * 0.012f * (tp.t / tp.creak);
                    p.group.localRotation = Quaternion.AngleAxis((j + 0.02f * (tp.t / tp.creak)) * Mathf.Rad2Deg, tp.axis) * tp.q0;
                    if (Rng.Next() < 0.3f) Vfx.Dust(p.x + tp.fx * 1.5f, p.z + tp.fz * 1.5f, 0.8f, 1, null, 1.5f);
                    game.shake = Mathf.Max(game.shake, 0.12f);
                    continue;
                }
                float k = Mathf.Min((tp.t - tp.creak) / tp.dur, 1);
                p.group.localRotation = Quaternion.AngleAxis(90 * k * k, tp.axis) * tp.q0;
                if (k >= 1)
                {
                    toppling.RemoveAt(i);
                    Land(tp);
                }
            }

            // Debris: gravity, bounce, settle, fade.
            for (int i = debris.Count - 1; i >= 0; i--)
            {
                var b = debris[i];
                b.life -= dt;
                if (b.life <= 0) { debris.RemoveAt(i); continue; }
                if (!b.rest)
                {
                    b.vy -= Gravity * dt;
                    b.x += b.vx * dt; b.y += b.vy * dt; b.z += b.vz * dt;
                    b.rx += b.wx * dt; b.ry += b.wy * dt;
                    if (b.y < b.size / 2)
                    {
                        b.y = b.size / 2;
                        b.vy *= -0.3f; b.vx *= 0.55f; b.vz *= 0.55f; b.wx *= 0.5f; b.wy *= 0.5f;
                        if (Mathf.Abs(b.vy) < 0.6f && Mathf.Sqrt(b.vx * b.vx + b.vz * b.vz) < 0.3f) b.rest = true;
                    }
                }
                debris[i] = b;
            }
        }

        // An airborne prop that's come to rest: flatten it onto its wheels or
        // its roof, whichever is nearer, and tell the nav grid where it ended up.
        void Settle(Prop p)
        {
            p.airborne = false;
            p.vy = 0;
            p.spin = Vector3.zero;
            p.va = 0;
            var q = p.quat.Value;
            var up = q * Vector3.up;
            var fwd = q * Vector3.forward;
            float yaw = Mathf.Atan2(fwd.x, fwd.z);
            p.rot = Mathf.Abs(fwd.y) > 0.9f ? p.rot : yaw;
            q = M.Yaw(p.rot);
            if (up.y < 0)
            {
                // landed upside down: roll it onto its roof, sitting a little higher
                q *= Quaternion.AngleAxis(180, Vector3.forward);
                p.y = p.height;
            }
            p.quat = q;
            p.group.localRotation = q;
            p.group.localPosition = new Vector3(p.x, p.y, p.z);
            World.navDirty = true;
            Vfx.Dust(p.x, p.z, 2, Vfx.N(5));
        }

        public void Draw()
        {
            for (int i = 0; i < debris.Count; i++)
            {
                var b = debris[i];
                float s = b.size * Mathf.Min(1, b.life);
                batch.Set(i, new Vector3(b.x, b.y - (b.life < 1 ? (1 - b.life) * 0.2f : 0), b.z), M.Euler(b.rx, b.ry, 0), Vector3.one * s, b.color);
            }
            batch.count = debris.Count;
            batch.Draw();
        }
    }
}
