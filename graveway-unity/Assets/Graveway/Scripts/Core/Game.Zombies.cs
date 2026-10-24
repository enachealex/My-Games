// Zombies: spawning, wandering, chasing round props, smashing through them,
// attacking, burning and dying. A port of the zombie half of
// graveway/src/game.js. Zombie models are pooled: one that's gone comes back
// as the next spawn of its kind, in new clothes.

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public sealed class Zombie
    {
        public enum Mode { Idle, Chase, Attack, Smash }

        public string kind;
        public ZombieKind t;
        public Rig model;
        public float x, z, yaw, scale, hp, maxHp, speed, damage, radius;
        public int stage;
        public Mode state;
        public float attackT, wanderT, wx, wz, kx, kz, flash, deadT;
        public bool hitDone, dead, calm, emerging, los, stuck, seen = true;
        public float losT, seeT, hidT, burnT, burnDps, burnTick;
        public Prop smash;
        public bool blastKill;               // the last hit was a blast: for the challenges
        public GameObject[] plateObjects;    // the armored boss's plates still on (null where one's come off)
        public int plates;
        public float armor, armorMax;
        public bool flying;
        public float flyY, flyVy, flySx, flySz;
    }

    public sealed partial class Game
    {
        readonly Dictionary<string, Stack<Rig>> zombiePool = new Dictionary<string, Stack<Rig>>();

        public Zombie SpawnZombie(string kind, float x, float z, int stage, bool calm = false, Alley alley = null, bool chase = false)
        {
            var t = Tunables.Zombies[kind];
            var sc = Tunables.StageScaling(stage);
            var model = TakeModel(kind);
            float scale = t.scale * (kind == "walker" ? 0.92f + rng.Next() * 0.16f : 1);
            model.root.transform.localScale = Vector3.one * scale;
            float hp = t.hp * sc.hp;
            var zb = new Zombie
            {
                kind = kind, t = t, model = model, x = x, z = z, yaw = Mathf.PI * (rng.Next() * 2 - 1), scale = scale, stage = stage,
                hp = hp, maxHp = hp, speed = (t.speedMin + rng.Next() * (t.speedMax - t.speedMin)) * sc.speed,
                damage = t.damage * sc.damage, radius = t.radius * (scale / t.scale),
                state = chase ? Zombie.Mode.Chase : Zombie.Mode.Idle, calm = calm, emerging = alley != null,
                losT = rng.Next() * 0.3f,
            };
            zombies.Add(zb);
            return zb;
        }

        Rig TakeModel(string kind)
        {
            if (!zombiePool.TryGetValue(kind, out var pool)) zombiePool[kind] = pool = new Stack<Rig>();
            Rig m;
            if (pool.Count > 0)
            {
                m = pool.Pop();
                m.root.SetActive(true);
            }
            else
            {
                m = Rigs.Zombie(kind);
                m.root.transform.SetParent(worldRoot, false);
            }
            m.root.transform.localRotation = Quaternion.identity;
            m.Snap("Idle", rng.Next());                 // out of step with the rest
            m.SetFlash(false);
            return m;
        }

        void Recycle(Zombie z)
        {
            StripArmor(z);
            z.model.root.SetActive(false);
            zombiePool[z.kind].Push(z.model);
        }

        // Called by the Director.
        public void SpawnGroup(int count, int stage, float z = 0, Alley alley = null, bool behind = false)
        {
            int room = Tunables.Director.MaxActive(stage) - AliveCount();
            int n = Mathf.Min(count, room);
            for (int i = 0; i < n; i++)
            {
                string kind = director.PickKind(stage);
                if (alley != null)
                    SpawnZombie(kind, alley.x + alley.side * rng.Next() * 2.5f, alley.z + (rng.Next() - 0.5f) * 2, stage, alley: alley, chase: true);
                else
                {
                    float px = (rng.Next() - 0.5f) * 12.5f, pz = z + (rng.Next() - 0.5f) * 8;
                    World.ResolveColliders(world.props, ref px, ref pz, 0.6f);
                    SpawnZombie(kind, px, pz, stage, chase: behind);
                }
            }
            if (alley != null && n >= 3) hud.Banner("Ambush!", null, 1.2f);
        }

        public void SpawnBoss()
        {
            if (run != null) run.bossesSpawned++;
            // The Black Market's "Skip the boss": this one never turns up.
            if (run != null && run.skipBoss)
            {
                run.skipBoss = false;
                hud.Banner("No boss this time", "The Black Market saw to it");
                BossPassed();
                return;
            }
            float x = 0, z = player.z - 38;
            World.ResolveColliders(world.props, ref x, ref z, 1.3f);
            run.boss = SpawnZombie("boss", x, z, Stage, chase: true);
            if (run.bossesSpawned % Tunables.BossArmor.Every == 0) ArmorUp(run.boss);
            if (run.boss.plates > 0) hud.Banner("Armored boss", "Break its armor off: every plate leaves a power-up");
            else hud.Banner("Something big is coming", "Put it down for a fat reward");
            Sfx.Groan(0, true);
        }

        float Pan(float x) => Sfx.Pan(x, player.x);

        void UpdateZombies(float dt)
        {
            var p = player;
            bool playing = state == State.Playing;
            var props = world.props;
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                var z = zombies[i];
                var m = z.model;
                var root = m.root.transform;
                if (z.dead)
                {
                    z.deadT += dt;
                    z.x += z.kx * dt; z.z += z.kz * dt;
                    float y = root.localPosition.y;
                    if (z.flying)
                    {
                        // thrown by a blast: up, tumbling, down
                        z.flyVy -= 20 * dt;
                        z.flyY += z.flyVy * dt;
                        root.localRotation = root.localRotation * M.Euler(z.flySx * dt, 0, z.flySz * dt);
                        if (z.flyY <= 0 && z.flyVy < 0)
                        {
                            z.flying = false;
                            root.localRotation = M.Yaw(z.yaw);
                            vfx.Dust(z.x, z.z, 0.8f, 2, null, 1);
                        }
                        y = Mathf.Max(0, z.flyY);
                    }
                    else { z.kx *= 1 - Damp(6, dt); z.kz *= 1 - Damp(6, dt); }
                    if (z.deadT > 3) y -= dt * 0.5f;
                    root.localPosition = new Vector3(z.x, y, z.z);
                    if (z.deadT > 4.5f) { Recycle(z); zombies.RemoveAt(i); }
                    continue;
                }
                // Far behind the survivor, off the built road: gone. A boss gets
                // more rope, but one you've outrun is gone too.
                bool isBoss = run != null && z == run.boss;
                if (z.z > p.z + (isBoss ? 60 : 45))
                {
                    if (isBoss) { run.boss = null; BossPassed(); }
                    Recycle(z);
                    zombies.RemoveAt(i);
                    continue;
                }

                if (z.burnT > 0)
                {
                    z.burnT -= dt;
                    z.burnTick += dt;
                    if (z.burnTick >= 0.25f)
                    {
                        z.burnTick -= 0.25f;
                        z.hp -= z.burnDps * 0.25f;
                        if (z.state == Zombie.Mode.Idle) z.state = Zombie.Mode.Chase;
                        if (z.hp <= 0) { KillZombie(z, 0, 1); continue; }
                    }
                    if (rng.Next() < dt * 14) vfx.Flame(z.x + (rng.Next() - 0.5f) * 0.4f, 0.6f + rng.Next() * 0.9f * z.scale, z.z + (rng.Next() - 0.5f) * 0.4f, 0.5f * z.scale);
                }

                // Fog of war: behind something tall, a zombie can't be seen (or
                // aimed at). Hidden after a moment out of view; back the
                // instant it's in it.
                if (state == State.Playing || state == State.Dying)
                {
                    z.seeT -= dt;
                    if (z.seeT <= 0)
                    {
                        z.seeT = 0.1f;
                        if (Sight.CanSee(world, p.x, p.z, z.x, z.z, z.radius)) { z.seen = true; z.hidT = 0; }
                        else if ((z.hidT += 0.1f) >= 0.15f) z.seen = false;
                    }
                }
                else z.seen = true;
                if (m.root.activeSelf != z.seen) m.root.SetActive(z.seen);

                float dx = p.x - z.x, dz = p.z - z.z, dist = Mathf.Max(1e-4f, Mathf.Sqrt(dx * dx + dz * dz));
                float vx = 0, vz = 0;
                float? faceYaw = null;
                float slow = nav.IsRough(z.x, z.z) ? Tunables.RubbleSlow : 1;

                if (z.state == Zombie.Mode.Idle)
                {
                    z.wanderT -= dt;
                    if (z.wanderT <= 0)
                    {
                        z.wanderT = 1.5f + rng.Next() * 3;
                        float a = rng.Next() * Tau;
                        bool go = rng.Next() < 0.7f;
                        z.wx = go ? Mathf.Sin(a) : 0; z.wz = go ? Mathf.Cos(a) : 0;
                    }
                    vx = z.wx * z.speed * 0.35f; vz = z.wz * z.speed * 0.35f;
                    if (vx != 0 || vz != 0) faceYaw = Mathf.Atan2(vx, vz);
                    if (playing && dist < z.t.aggro)
                    {
                        z.state = Zombie.Mode.Chase;
                        if (rng.Next() < 0.4f) Sfx.Groan(Pan(z.x), z.t.smashes);
                    }
                }
                else if (z.state == Zombie.Mode.Chase)
                {
                    if (!playing) z.state = Zombie.Mode.Idle;
                    else
                    {
                        float reach = z.t.reach * (z.scale / z.t.scale) + p.stats.radius;
                        if (dist < reach) { z.state = Zombie.Mode.Attack; z.attackT = 0; z.hitDone = false; }
                        // Walk straight at you when nothing's in the way;
                        // otherwise follow the flow field around the props.
                        z.losT -= dt;
                        if (z.losT <= 0) { z.losT = 0.25f; z.los = dist < 14 && nav.Clear(z.x, z.z, p.x, p.z); }
                        float ux = dx / dist, uz = dz / dist;
                        z.stuck = false;
                        if (!z.los && !z.emerging)
                        {
                            if (nav.Steer(z.x, z.z, out var sx, out var sz)) { ux = sx; uz = sz; }
                            else if (nav.CellOf(z.x, z.z) >= 0) z.stuck = true;    // no path: go straight and break through
                        }
                        vx = ux * z.speed * slow; vz = uz * z.speed * slow;
                        faceYaw = Mathf.Atan2(vx, vz);
                    }
                }
                else if (z.state == Zombie.Mode.Attack)
                {
                    z.attackT += dt;
                    faceYaw = Mathf.Atan2(dx, dz);
                    float period = 1 / z.t.attackRate, windup = Mathf.Min(0.42f, period * 0.5f);
                    if (!z.hitDone && z.attackT >= windup)
                    {
                        z.hitDone = true;
                        float reach = z.t.reach * (z.scale / z.t.scale) + p.stats.radius + 0.35f;
                        if (dist < reach && playing)
                        {
                            if (z.kind == "boss") { Sfx.Slam(); shake = Mathf.Max(shake, 0.6f); }
                            HurtPlayer(z.damage, z.x, z.z);
                        }
                    }
                    if (z.attackT >= period) z.state = playing ? Zombie.Mode.Chase : Zombie.Mode.Idle;
                }
                else
                {
                    // Beating on a prop in the way, until it's gone. A vehicle
                    // that goes up leaves a wreck that can't be broken any
                    // further, which counts as gone (beating on it is how tanks
                    // and bosses used to get stuck). Also done with it once the
                    // survivor's within reach or it's no longer in the way.
                    var pr = z.smash;
                    z.attackT += dt;
                    faceYaw = Mathf.Atan2(pr.x - z.x, pr.z - z.z);
                    float period = 1 / z.t.attackRate;
                    if (!z.hitDone && z.attackT >= Mathf.Min(0.42f, period * 0.5f))
                    {
                        z.hitDone = true;
                        propSys.Damage(pr, z.damage * (z.t.smashes ? 3 : 1.5f), pr.x - z.x, pr.z - z.z, "smash");
                        Sfx.Thunk();
                    }
                    bool gone = pr.destroyed || pr.indestructible || pr.airborne || !pr.blocking;
                    if (gone || z.attackT >= period)
                    {
                        z.attackT = 0; z.hitDone = false;
                        float reach = z.t.reach * (z.scale / z.t.scale) + p.stats.radius;
                        if (gone || !playing || dist < Mathf.Max(2, reach) || !InTheWay(z, pr, dx, dz, dist))
                        {
                            z.smash = null;
                            z.state = playing ? Zombie.Mode.Chase : Zombie.Mode.Idle;
                        }
                    }
                }

                // Keep zombies from piling into one spot.
                for (int j = 0; j < zombies.Count; j++)
                {
                    var o = zombies[j];
                    if (o == z || o.dead) continue;
                    float ox = z.x - o.x, oz = z.z - o.z, min = z.radius + o.radius;
                    if (Mathf.Abs(ox) > min || Mathf.Abs(oz) > min) continue;
                    float d2 = ox * ox + oz * oz;
                    if (d2 < min * min && d2 > 1e-6f)
                    {
                        float d = Mathf.Sqrt(d2), push = (min - d) * 0.5f;
                        z.x += ox / d * push; z.z += oz / d * push;
                    }
                }

                z.x += (vx + z.kx) * dt;
                z.z += (vz + z.kz) * dt;
                z.kx *= 1 - Damp(9, dt); z.kz *= 1 - Damp(9, dt);
                var bumped = World.ResolveColliders(props, ref z.x, ref z.z, z.radius);
                // Tanks beat down whatever they walk into on the way to you;
                // anyone with no way round does the same. The big ones shove
                // wrecks aside.
                if (bumped != null && z.state == Zombie.Mode.Chase && (z.t.smashes || z.stuck))
                {
                    if (!bumped.indestructible)
                    {
                        float ex = p.x - z.x, ez = p.z - z.z, ed = Mathf.Max(1e-4f, Mathf.Sqrt(ex * ex + ez * ez));
                        if (InTheWay(z, bumped, ex, ez, ed)) { z.state = Zombie.Mode.Smash; z.smash = bumped; z.attackT = 0; z.hitDone = false; }
                    }
                    else if (z.t.smashes && bumped.def.mass > 0)
                    {
                        float shove = (z.kind == "boss" ? 160 : 60) * dt / bumped.def.mass / (z.speed > 0 ? z.speed : 1);
                        bumped.vx += vx * shove;
                        bumped.vz += vz * shove;
                        bumped.moving = true;
                    }
                }
                if (z.emerging) { if (Mathf.Abs(z.x) < World.WalkHalf - 0.3f) z.emerging = false; }
                else z.x = Mathf.Clamp(z.x, -World.WalkHalf, World.WalkHalf);
                if (run != null && z.z > run.startZ + 4.5f) z.z = run.startZ + 4.5f;
                // Zombies can't walk through the survivor (the survivor's side
                // of this is in UpdatePlayer).
                if (playing && dist < z.radius + p.stats.radius)
                {
                    float push = (z.radius + p.stats.radius - dist) * 0.5f;
                    z.x -= dx / dist * push; z.z -= dz / dist * push;
                }
                if (faceYaw.HasValue) z.yaw = LerpAngle(z.yaw, faceYaw.Value, Damp(z.state == Zombie.Mode.Idle ? 3 : 8, dt));

                // Animation: a swing for each attack, otherwise shamble or run
                // at a pace to match (the clips cover about 1.1 and 3 m/s at
                // walker size). Runners are the bony ones, and always run.
                float moveSpeed = Mathf.Sqrt(vx * vx + vz * vz), big = z.scale;
                if (z.state == Zombie.Mode.Attack || z.state == Zombie.Mode.Smash)
                {
                    if (z.attackT < dt * 0.5f) m.Restart(m.Has("Punch") ? "Punch" : "Jump", 0.08f, Mathf.Max(0.6f, 0.77f * z.t.attackRate));
                }
                else if (moveSpeed < 0.25f) m.Play("Idle");
                else if (z.kind == "runner" || moveSpeed > 2.8f * big) m.Play("Run", 0.2f, Mathf.Clamp(moveSpeed / (3 * big), 0.6f, 1.8f));
                else m.Play("Walk", 0.2f, Mathf.Clamp(moveSpeed / (1.1f * big), 0.5f, 1.8f));

                if (z.flash > 0) { z.flash -= dt; if (z.flash <= 0) m.SetFlash(false); }
                root.localPosition = new Vector3(z.x, 0, z.z);
                root.localRotation = M.Yaw(z.yaw);
            }

            // An occasional moan from somewhere close.
            if (playing)
            {
                groanT -= dt;
                if (groanT <= 0)
                {
                    groanT = 2.5f + rng.Next() * 4;
                    var near = zombies.Find(z => !z.dead && z.state != Zombie.Mode.Idle && Mathf.Abs(z.z - p.z) < 18);
                    if (near != null) Sfx.Groan(Pan(near.x), near.t.smashes);
                }
            }
        }

        // Whether a prop still stands between a zombie and the survivor: the
        // zombie up against it (by the box World.ResolveColliders uses), on
        // the survivor's side.
        static bool InTheWay(Zombie z, Prop pr, float dx, float dz, float dist)
        {
            float rx = z.x - pr.x, rz = z.z - pr.z;
            float lx = Mathf.Clamp(rx * pr.cos - rz * pr.sin, -pr.hw, pr.hw);
            float lz = Mathf.Clamp(rx * pr.sin + rz * pr.cos, -pr.hd, pr.hd);
            float cx = pr.x + lx * pr.cos + lz * pr.sin - z.x, cz = pr.z - lx * pr.sin + lz * pr.cos - z.z;
            float d = Mathf.Sqrt(cx * cx + cz * cz);
            if (d > z.radius + 0.6f) return false;                 // pushed off it, or it moved
            return d < 1e-3f || (cx * dx + cz * dz) / (d * dist) > 0.2f;
        }

        // blast: from an explosion or a shockwave (against armor, it counts double).
        public void DamageZombie(Zombie z, float dmg, float dx, float dz, float knock, float launch = 0, bool blast = false)
        {
            if (z.dead) return;
            if (z.plates > 0) dmg = HitArmor(z, dmg, blast);
            z.hp -= dmg;
            z.blastKill = blast;
            if (z.state == Zombie.Mode.Idle) z.state = Zombie.Mode.Chase;
            float k = knock / (z.scale * z.scale);
            z.kx += dx * k * 2.2f; z.kz += dz * k * 2.2f;
            z.model.SetFlash(true);
            z.flash = 0.07f;
            Burst(z.x, 1.2f * z.scale, z.z, 3, Hex.C(0x6e0b0b), speed: 2.5f, size: 0.08f);
            Sfx.Hit(Pan(z.x));
            if (z.hp <= 0) KillZombie(z, dx, dz, launch);
        }

        // Falling walls, lamp posts and buildings: everything under fp is hit.
        public void Crush(PropSystem.Footprint fp, float dmg, float fx, float fz)
        {
            for (int i = 0; i < zombies.Count; i++)
            {
                var z = zombies[i];
                if (!z.dead && InBox(fp, z.x, z.z, z.radius)) DamageZombie(z, dmg, fx, fz, 2, 3);
            }
            var p = player;
            if (state == State.Playing && InBox(fp, p.x, p.z, p.stats.radius)) HurtPlayer(Mathf.Min(dmg * 0.35f, 70), p.x - fx, p.z - fz);
        }

        void KillZombie(Zombie z, float dx, float dz, float launch = 0)
        {
            z.dead = true;
            ArmorOff(z);
            z.deadT = 0;
            if (launch > 0.5f)
            {
                z.flying = true; z.flyY = 0; z.flyVy = launch;
                z.flySx = (rng.Next() - 0.5f) * 10; z.flySz = (rng.Next() - 0.5f) * 10;
            }
            z.yaw = Mathf.Atan2(-dx, -dz);                // fall away from the shot
            z.model.root.transform.localRotation = M.Yaw(z.yaw);
            z.model.SetFlash(false);
            z.model.Restart("Death", 0.05f);          // (behind cover, it falls unseen, as on the web)
            z.flash = 0;
            Burst(z.x, 1.1f * z.scale, z.z, (int)(10 + z.scale * 6), Hex.C(0x5a0909), speed: 3.5f * z.scale, size: 0.11f * z.scale);
            Sfx.Die(Pan(z.x));
            if (run != null) { run.kills++; ChallengeKill(z, z.blastKill); }
            int n = z.t.coins + (rng.Next() < 0.3f ? 1 : 0);
            for (int i = 0; i < n; i++)
            {
                float a = rng.Next() * Tau, s = 1.5f + rng.Next() * 2.5f;
                AddCoin(z.x, 1, z.z, Mathf.Cos(a) * s, 4 + rng.Next() * 3, Mathf.Sin(a) * s);
            }
            if (z.kind == "boss")
            {
                if (run != null) { run.bosses++; BossPassed(); }
                shake = 0.8f;
                hud.Banner("Boss down!", $"+{z.t.coins} coins on the ground");
                Sfx.Slam();
                world.DropItem("armor", z.x - 0.8f, z.z);
                world.DropItem("power", z.x + 0.8f, z.z, Tunables.PickPowerup(rng.Next()));
            }
            else if (z.kind == "tank")
            {
                float r = rng.Next();
                if (r < 0.3f) world.DropItem("health", z.x, z.z);
                else if (r < 0.3f + Tunables.PowerUpFromTank) world.DropItem("power", z.x, z.z, Tunables.PickPowerup(rng.Next()));
            }
        }
    }
}
