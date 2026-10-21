// The street: one endless road running away from the camera (-z), built in
// 32 m chunks just beyond the fog and torn down once they're behind you. A
// port of graveway/src/world.js. Each chunk is seeded by its index, so the
// same run seed always lays out the same road.
//
// Chunks hold scenery (road, buildings, lamps, litter), pickups, and props:
// wrecked cars, barricades, explosive barrels and brick walls. Props live in
// `props`; what happens when they're shot, blown up or shoved is in
// PropSystem. Whenever the set of standing props changes, `navDirty` is
// raised and the navigation grid rebuilds.

using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Graveway
{
    public sealed class Prop
    {
        public string kind;
        public PropDef def;
        public int chunk;
        public float x, z, y, rot, hw, hd, height, cos, sin, r;
        public float hp, maxHp;
        public bool indestructible;          // a burnt wreck (the web's hp: null)
        public bool blocking = true, destroyed, removed, wreck, airborne, moving;
        public PropModel model;
        public Transform group;
        public float flash;
        public float vx, vy, vz, va;
        public Vector3 spin;
        public Quaternion? quat;             // set once it tumbles; rot alone until then
        public Vfx.Emitter smoking, burning;
        public bool isLamp;
    }

    // Slow ground: rubble, a fallen lamp post.
    public sealed class RoughArea
    {
        public float x, z, hw, hd, cos, sin;
        public int chunk;
    }

    public sealed class Alley
    {
        public float x, z;
        public int side, chunk;
    }

    public sealed class Item
    {
        public string kind, power;           // 'health', 'armor' or 'power'
        public GameObject obj;
        public MeshRenderer decal;
        public float x, z;
        public bool taken;
        public int chunk;
        public readonly List<Transform> billboards = new List<Transform>();
    }

    public sealed class World
    {
        public const float RoadHalf = 6;            // asphalt from -6 to 6
        public const float WalkHalf = 7.4f;         // how far sideways anyone can go (the sidewalk edge)
        public const float StartPz = 2;             // where the survivor stands at 0 m
        public const float FacadeX = WalkHalf + 0.6f;   // where the building fronts are
        const float Chunk = 32;                     // metres; a multiple of the road texture's 16 m tile
        const float Edge = 14;                      // z where chunk 0 starts (behind the start barricade)
        const float BuildAhead = 78;                // keep road built this far past the survivor
        const float KeepBehind = 40;                // and this far behind

        sealed class Built
        {
            public int i;
            public float z0, z1;
            public GameObject group;
            public readonly List<Object> own = new List<Object>();
        }

        sealed class Animated
        {
            public string kind;
            public MeshRenderer[] flames;
            public MeshRenderer pool, head;
            public Material poolMat;
            public Color poolColor;
            public float seed;
            public int chunk;
            public Prop prop;
        }

        readonly Transform parent;
        readonly uint seed;
        public readonly float startZ;               // where this run's survivor starts
        readonly Dictionary<int, Built> chunks = new Dictionary<int, Built>();
        public List<Prop> props = new List<Prop>(); // every prop on the built road
        public List<RoughArea> rough = new List<RoughArea>();
        public List<Alley> alleys = new List<Alley>();
        public List<Item> items = new List<Item>();
        List<Animated> animated = new List<Animated>();
        public bool navDirty = true;
        public Action<List<Vector2>> onCoins;       // set by the game: (x, z) spots for a line of coins
        public bool lotsOfPowerups;                 // playtest tool: two power-ups on every chunk
        int first;

        public World(Transform parent, uint seed, float startZ)
        {
            this.parent = parent;
            this.seed = seed;
            this.startZ = startZ;
            first = Mathf.Max(0, Mathf.FloorToInt((Edge - (startZ + 30)) / Chunk));
        }

        public void Update(float pz, float t)
        {
            int last = Mathf.FloorToInt((Edge - (pz - BuildAhead)) / Chunk);
            for (int i = first; i <= last; i++) if (!chunks.ContainsKey(i)) Build(i);
            List<int> drop = null;
            foreach (var kv in chunks) if (kv.Value.z1 > pz + KeepBehind) (drop ??= new List<int>()).Add(kv.Key);
            if (drop != null) foreach (var i in drop) Drop(i);
            // Never rebuild what was torn down behind you.
            first = Mathf.Max(first, Mathf.FloorToInt((Edge - (pz + KeepBehind)) / Chunk));
            Animate(t);
        }

        public void AddRough(RoughArea r)
        {
            rough.Add(r);
            navDirty = true;
        }

        // Things spawned mid-run (rubble, scorch marks) go with the chunk they
        // landed in, so they're cleaned up along with it. Returns the chunk,
        // or -1 (and destroys the thing) if that stretch is gone.
        public int AddToChunk(float z, GameObject obj, params Object[] own)
        {
            int i = ChunkAt(z);
            if (!chunks.TryGetValue(i, out var ch))
            {
                foreach (var o in own) Object.Destroy(o);
                Object.Destroy(obj);
                return -1;
            }
            obj.transform.SetParent(ch.group.transform, true);
            ch.own.AddRange(own);
            return i;
        }

        public int ChunkAt(float z) => Mathf.FloorToInt((Edge - z) / Chunk);

        public void Dispose()
        {
            foreach (var i in new List<int>(chunks.Keys)) Drop(i);
        }

        void Drop(int i)
        {
            var ch = chunks[i];
            chunks.Remove(i);
            Object.Destroy(ch.group);
            Art.DestroyAll(ch.own);
            foreach (var p in props) if (p.chunk == i) p.removed = true;   // stops their smoke and fire
            props.RemoveAll(p => p.chunk == i);
            rough.RemoveAll(r => r.chunk == i);
            items.RemoveAll(it => it.chunk == i);
            alleys.RemoveAll(a => a.chunk == i);
            animated.RemoveAll(a => a.chunk == i);
            navDirty = true;
        }

        // ── Materials and shared ground meshes ───────────────────────────
        static Material[] facadeMats;
        static Material roadMat, walkMat, curbMat, underMat, litterMat;
        static Material[] awningMats;
        static Mesh roadMesh, walkMesh, curbMesh, underMesh;

        static void Shared()
        {
            if (facadeMats != null) return;
            var tints = new[] { 0x9a8f86, 0x7f8790, 0x8e7a6a, 0x6f746c, 0xa39a8a, 0x7a6e74 };
            var list = new List<Material>();
            foreach (var t in tints)
                for (int f = 0; f < Tex.Facades.Length; f++) list.Add(Art.Lit(Hex.C(t), 0.85f, Tex.Facades[f], Tex.FacadeLights[f]));
            facadeMats = list.ToArray();
            roadMat = Art.LitOwn(Color.white, Tex.Road);
            roadMat.mainTextureScale = new Vector2(1, Chunk / 16);
            walkMat = Art.LitOwn(Color.white, Tex.Sidewalk);
            walkMat.mainTextureScale = new Vector2(1, Chunk / 4);
            curbMat = Art.Lit(0x6a6a6c);
            underMat = Art.Lit(0x16171a);
            litterMat = Art.Lit(0x24262a);
            awningMats = new[] { Art.Lit(0x6a2a2a), Art.Lit(0x2a4a5a), Art.Lit(0x4a5a2a) };
            roadMesh = new GeoBuilder().Flat(RoadHalf * 2, Chunk, M.I).Build("road");
            walkMesh = new GeoBuilder().Box(3, 0.16f, Chunk, -(RoadHalf + 1.5f), 0.08f, 0).Box(3, 0.16f, Chunk, RoadHalf + 1.5f, 0.08f, 0).Build("sidewalks");
            curbMesh = new GeoBuilder().Box(0.18f, 0.2f, Chunk, -(RoadHalf + 0.09f), 0.1f, 0).Box(0.18f, 0.2f, Chunk, RoadHalf + 0.09f, 0.1f, 0).Build("kerbs");
            underMesh = new GeoBuilder().Flat(80, Chunk, M.T(0, -0.01f, 0)).Build("under");
        }

        // ── Building a chunk ─────────────────────────────────────────────
        void Build(int i)
        {
            Shared();
            var rng = new Rng(unchecked(seed * 7919u + (uint)i * 104729u));
            float Rand(float a, float b) => a + rng.Next() * (b - a);
            float z0 = Edge - i * Chunk, z1 = z0 - Chunk, zc = (z0 + z1) / 2;
            var ch = new Built { i = i, z0 = z0, z1 = z1, group = new GameObject($"chunk {i}") };
            ch.group.transform.SetParent(parent, false);
            chunks[i] = ch;
            var group = ch.group.transform;

            // Ground: four shared meshes per chunk.
            foreach (var (mesh, mat) in new[] { (roadMesh, roadMat), (walkMesh, walkMat), (curbMesh, curbMat), (underMesh, underMat) })
                Art.Show(group, mesh, mat, mesh.name).transform.localPosition = new Vector3(0, 0, zc);

            // Buildings, with the odd alley for ambushes, and now and then a
            // condemned building you can bring down across the road. One mesh
            // per facade material per chunk.
            var fronts = new GeoBuilder[facadeMats.Length];
            var awnings = new GeoBuilder[awningMats.Length];
            GeoBuilder Front(int k) => fronts[k] ??= new GeoBuilder();
            bool condemned = i % Tunables.CondemnedEvery == Tunables.CondemnedEvery - 2 && z0 < startZ - 40;
            int condemnedSide = rng.Next() < 0.5f ? -1 : 1;
            foreach (var s in new[] { -1, 1 })
            {
                float z = z0;
                bool setPiece = condemned && s == condemnedSide;
                while (z > z1 + 0.5f)
                {
                    if (setPiece && z - z1 > 11)
                    {
                        setPiece = false;
                        const float w = 9, h = 13;
                        AddProp("condemned", Models.Condemned(w, h), s * (FacadeX + 0.6f), z - 1 - w / 2, s > 0 ? Mathf.PI : 0, i, ch);
                        // What's left standing behind it once it falls.
                        Front((int)(rng.Next() * facadeMats.Length)).Box(7, 9, w + 2, M.T(s * (FacadeX + 2 + 3.5f), 4.5f, z - 1 - w / 2), 8, 8);
                        z -= w + 2;
                        continue;
                    }
                    float len = Mathf.Min(Rand(6, 13), z - z1);
                    if (rng.Next() < 0.15f && len > 4 && z < startZ - 8)
                    {
                        const float w = 3.2f;
                        alleys.Add(new Alley { x = s * (WalkHalf + 1.6f), z = z - w / 2, side = s, chunk = i });
                        Front((int)(rng.Next() * facadeMats.Length)).Box(1, 14, w + 2, M.T(s * (WalkHalf + 9), 7, z - w / 2), 8, 8);
                        z -= w;
                        continue;
                    }
                    float bh = Rand(6, 24), bd = Rand(7, 11);
                    Front((int)(rng.Next() * facadeMats.Length)).Box(bd, bh, len, M.T(s * (WalkHalf + 0.6f + bd / 2), bh / 2, z - len / 2), 8, 8);
                    if (rng.Next() < 0.3f)
                    {
                        int k = (int)(rng.Next() * 3);
                        (awnings[k] ??= new GeoBuilder()).Box(1.2f, 0.12f, len * 0.7f, M.T(s * (WalkHalf + 0.05f), 3, z - len / 2) * M.Rz(s * 0.25f));
                    }
                    z -= len;
                }
            }
            for (int k = 0; k < fronts.Length; k++) Emit(ch, fronts[k], facadeMats[k], "buildings");
            for (int k = 0; k < awnings.Length; k++) Emit(ch, awnings[k], awningMats[k], "awnings");

            // Lamps, every 16 m, alternating sides. They stop bullets and can
            // be knocked down.
            for (int k = 0; k < 2; k++)
            {
                int s = (i + k) % 2 == 1 ? 1 : -1;
                var lamp = Models.Lamp();
                var p = AddProp("lamp", lamp, s * (RoadHalf + 0.6f), z0 - 6 - k * 16, s > 0 ? -Mathf.PI / 2 : Mathf.PI / 2, i, ch);
                p.isLamp = true;
                if (rng.Next() < 0.22f) animated.Add(new Animated { kind = "flicker", head = lamp.head, pool = lamp.pool, seed = rng.Next() * 100, chunk = i, prop = p });
            }

            // Litter: one merged mesh for every bag and box in the chunk.
            var litter = new GeoBuilder();
            for (int k = 0; k < Chunk / 3; k++)
            {
                int s = rng.Next() < 0.5f ? -1 : 1;
                float lx = rng.Next() < 0.7f ? s * Rand(RoadHalf + 0.4f, WalkHalf) : Rand(-RoadHalf, RoadHalf);
                float sz = Rand(0.25f, 0.6f);
                litter.Box(sz, sz * Rand(0.4f, 0.9f), sz * Rand(0.8f, 1.4f), lx, sz * 0.3f, Rand(z1, z0));
            }
            Emit(ch, litter, litterMat, "litter");

            // The barricade behind the start: there's no going back.
            float backZ = startZ + 5.4f;
            if (backZ <= z0 && backZ > z1)
            {
                for (float x = -6; x <= 6; x += 2.4f)
                {
                    var bar = Models.Barrier();
                    bar.group.transform.SetParent(group, false);
                    bar.group.transform.localPosition = new Vector3(x, 0, backZ);
                    bar.group.transform.localRotation = M.Yaw(Rand(-0.08f, 0.08f));
                }
                var bus = Models.Car(rng);
                bus.group.transform.SetParent(group, false);
                bus.group.transform.localPosition = new Vector3(Rand(-3, 3), 0, backZ + 3);
                bus.group.transform.localRotation = M.Yaw(Mathf.PI / 2 + Rand(-0.2f, 0.2f));
                bus.group.transform.localScale = new Vector3(1.2f, 1.2f, 1.4f);
            }

            // Props. Nothing in the first stretch after the start.
            float clearUntil = startZ - 14;
            float rz = z0 - Rand(2, 8);
            while (rz > z1 + 2)
            {
                if (rz < clearUntil) PlaceRow(rng, rz, i, ch);
                rz -= Rand(9, 15);
            }

            // Coins and pickups.
            var spots = new List<Vector2>();
            for (float cz = z0 - Rand(2, 10); cz > z1; cz -= Rand(14, 22))
            {
                if (cz > startZ - 6) continue;
                float x = Rand(-5, 5);
                int n = 4 + (int)(rng.Next() * 4);
                float curve = rng.Next() < 0.4f ? Rand(-0.5f, 0.5f) : 0;
                for (int k = 0; k < n; k++)
                {
                    float px = Mathf.Clamp(x + curve * k, -6.5f, 6.5f), pz = cz - k * 1.3f;
                    if (pz > z1 && !Blocked(px, pz, 0.4f)) spots.Add(new Vector2(px, pz));
                }
            }
            if (onCoins != null && spots.Count > 0) onCoins(spots);
            if (z0 < startZ - 30)
            {
                if (rng.Next() < 0.4f) PlaceItem(rng, "health", Rand(z1 + 2, z0 - 2), i, ch);
                if (rng.Next() < 0.25f) PlaceItem(rng, "armor", Rand(z1 + 2, z0 - 2), i, ch);
            }
            bool lots = lotsOfPowerups;
            if (z0 < startZ - (lots ? 10 : Tunables.PowerUpFrom))
            {
                int n = lots ? 2 : rng.Next() < Tunables.PowerUpPerChunk ? 1 : 0;
                for (int k = 0; k < n; k++) PlaceItem(rng, "power", Rand(z1 + 2, z0 - 2), i, ch, Tunables.PickPowerup(rng.Next()));
            }
            navDirty = true;
        }

        static void Emit(Built ch, GeoBuilder b, Material mat, string name)
        {
            if (b == null || b.Count == 0) return;
            var mesh = b.Build(name);
            ch.own.Add(mesh);
            Art.Show(ch.group.transform, mesh, mat, name);
        }

        public bool Blocked(float x, float z, float pad)
        {
            foreach (var c in props)
                if (!c.destroyed && Mathf.Abs(c.x - x) < c.r + pad && Mathf.Abs(c.z - z) < c.r + pad) return true;
            return false;
        }

        // Puts a prop on the street: its model in the chunk, its collider in
        // `props`. Also used mid-run for slabs left by a collapse.
        public Prop AddProp(string kind, PropModel model, float x, float z, float rot, int chunk) =>
            chunks.TryGetValue(chunk, out var ch) ? AddProp(kind, model, x, z, rot, chunk, ch) : Discard(model);

        static Prop Discard(PropModel model)
        {
            Art.DestroyAll(model.own);
            Object.Destroy(model.group);
            return null;
        }

        Prop AddProp(string kind, PropModel model, float x, float z, float rot, int chunk, Built ch)
        {
            var t = model.group.transform;
            t.SetParent(ch.group.transform, false);
            t.localPosition = new Vector3(x, 0, z);
            t.localRotation = M.Yaw(rot);
            var def = Tunables.Props[kind];
            var p = new Prop
            {
                kind = kind, def = def, chunk = chunk, x = x, z = z, rot = rot, hw = model.hw, hd = model.hd, height = model.height,
                cos = Mathf.Cos(rot), sin = Mathf.Sin(rot), r = Mathf.Sqrt(model.hw * model.hw + model.hd * model.hd),
                hp = def.hp, maxHp = def.hp, model = model, group = t,
            };
            ch.own.AddRange(model.own);
            model.own.Clear();
            props.Add(p);
            navDirty = true;
            return p;
        }

        // Where a bullet's path meets a building front (outside an alley), as
        // a distance along it, or Infinity.
        public float FacadeT(float x, float z, float dx, float dz, float len)
        {
            if (Mathf.Abs(dx) < 1e-6f) return float.PositiveInfinity;
            int side = dx > 0 ? 1 : -1;
            float t = (side * FacadeX - x) / dx;
            if (t < 0 || t > len) return float.PositiveInfinity;
            float zc = z + dz * t;
            foreach (var a in alleys) if (a.side == side && Mathf.Abs(zc - a.z) < 1.5f) return float.PositiveInfinity;
            return t;
        }

        // One row of trouble across the road at z.
        void PlaceRow(Rng rng, float z, int chunk, Built ch)
        {
            float Rand(float a, float b) => a + rng.Next() * (b - a);
            float r = rng.Next();
            Prop Add(string kind, PropModel model, float x, float zz, float rot) => AddProp(kind, model, x, zz, rot, chunk, ch);

            if (z < startZ - Tunables.TankerFrom && rng.Next() < Tunables.TankerChance)
            {
                // A fuel tanker, mostly along the road. Give it a wide berth.
                float x = (rng.Next() < 0.5f ? -1 : 1) * Rand(2.4f, 4.4f);
                Add("tanker", Models.Tanker(rng), x, z, Rand(-0.2f, 0.2f) + (rng.Next() < 0.5f ? Mathf.PI : 0));
                return;
            }

            if (r < 0.36f)
            {
                // A wreck or two, with the odd explosive barrel tucked beside it.
                float x = rng.Next() < 0.5f ? Rand(-RoadHalf + 1.2f, -1.4f) : Rand(1.4f, RoadHalf - 1.2f);
                float rot = rng.Next() < 0.3f ? Rand(0.6f, 1.4f) * (rng.Next() < 0.5f ? -1 : 1) : Rand(-0.35f, 0.35f);
                Add("car", Models.Car(rng), x, z, rot);
                if (rng.Next() < 0.45f) Add("barrel", Models.ExplosiveBarrel(), x + Rand(-1.4f, 1.4f), z + (rng.Next() < 0.5f ? -2.6f : 2.6f), 0);
                if (rng.Next() < 0.25f) Add("car", Models.Car(rng), -Mathf.Sign(x) * Rand(3.9f, 5), z - Rand(-1.5f, 1.5f), Rand(-0.3f, 0.3f));
            }
            else if (r < 0.6f)
            {
                // Barricades: a short line, or a chokepoint across most of the road.
                bool choke = rng.Next() < 0.35f;
                int n = choke ? 4 : 1 + (int)(rng.Next() * 2);
                int gapAt = (int)(rng.Next() * (n + 1));
                float x = choke ? -RoadHalf + 1.2f : Rand(-4, 4 - n * 2.4f);
                float rot = choke ? 0 : Rand(-0.25f, 0.25f);
                for (int k = 0; k < n; k++)
                {
                    if (choke && k == gapAt) x += 2.8f;            // the way through
                    Add("barricade", Models.Barrier(), x, z, rot);
                    x += 2.45f;
                }
            }
            else if (r < 0.75f)
            {
                // A cluster of explosive barrels.
                float x = Rand(-5, 5);
                int n = 2 + (int)(rng.Next() * 3);
                for (int k = 0; k < n; k++) Add("barrel", Models.ExplosiveBarrel(), x + Rand(-1, 1), z + Rand(-1, 1), 0);
            }
            else if (r < 0.92f)
            {
                // A weak brick wall across part of the road.
                float w = Rand(3.5f, 6.5f);
                float x = rng.Next() < 0.5f ? -WalkHalf + w / 2 : WalkHalf - w / 2;
                Add("wall", Models.Wall(w), x, z, 0);
            }
            else
            {
                var fb = Models.FireBarrel();
                var poolMat = fb.pool.sharedMaterial;
                var p = Add("fire", fb, Rand(-5, 5), z, 0);
                animated.Add(new Animated { kind = "fire", flames = fb.flames, pool = fb.pool, poolMat = poolMat, poolColor = Hex.C(0xff7a2a), seed = rng.Next() * 100, chunk = chunk, prop = p });
            }
        }

        void PlaceItem(Rng rng, string kind, float z, int chunk, Built ch, string power = null)
        {
            for (int tries = 0; tries < 8; tries++)
            {
                float x = -5.5f + rng.Next() * 11;
                if (Blocked(x, z, 0.8f)) continue;
                items.Add(MakeItem(kind, x, z, chunk, ch, power));
                return;
            }
        }

        Item MakeItem(string kind, float x, float z, int chunk, Built ch, string power)
        {
            var it = new Item { kind = kind, power = power, x = x, z = z, chunk = chunk };
            var g = ch.group.transform;
            if (kind == "power")
            {
                it.obj = Models.PowerUp(power, it.billboards);
                it.decal = Art.GlowDecal(g, Tunables.PowerUp(power).color, 2.4f, 0.55f, Tex.Ring);
            }
            else
            {
                it.obj = kind == "health" ? Models.HealthPack() : Models.ArmorVest();
                it.decal = Art.GlowDecal(g, kind == "health" ? 0x4dff6a : 0x4aa8ff, 2.2f, 0.5f);
            }
            it.obj.transform.SetParent(g, false);
            it.obj.transform.localPosition = new Vector3(x, 0, z);
            it.decal.transform.localPosition = new Vector3(x, 0.04f, z);
            return it;
        }

        // Drops a pickup where a zombie fell.
        public void DropItem(string kind, float x, float z, string power = null)
        {
            int i = ChunkAt(z);
            if (!chunks.TryGetValue(i, out var ch)) return;
            items.Add(MakeItem(kind, x, z, i, ch, power));
        }

        void Animate(float t)
        {
            foreach (var a in animated)
            {
                if (a.prop != null && a.prop.destroyed) continue;      // knocked over: no more flicker or flames
                if (a.kind == "fire")
                {
                    float f = 0.85f + Mathf.Sin(t * 17 + a.seed) * 0.08f + Mathf.Sin(t * 29 + a.seed * 2) * 0.07f;
                    a.flames[0].transform.localScale = new Vector3(f, 1 / f, 1);
                    a.flames[1].transform.localScale = new Vector3(1 / f, f, 1);
                    var pc = a.poolColor;
                    a.poolMat.SetColor("_BaseColor", new Color(pc.r, pc.g, pc.b, 0.38f + (f - 0.85f) * 1.2f));
                }
                else
                {
                    bool on = Mathf.Sin(t * 13 + a.seed) + Mathf.Sin(t * 7.3f + a.seed * 3) > -0.6f;
                    if (a.head.enabled != on) { a.head.enabled = on; a.pool.enabled = on; }
                }
            }
        }

        // ── Collisions ───────────────────────────────────────────────────
        // Pushes a circle out of every standing prop it overlaps. Props are
        // rotated boxes, so the circle is moved into each box's own frame,
        // clamped, and moved back. Returns the last prop it was pushed out of.
        public static Prop ResolveColliders(List<Prop> props, ref float px, ref float pz, float radius)
        {
            Prop hit = null;
            foreach (var c in props)
            {
                if (!c.blocking || c.destroyed) continue;
                float dx = px - c.x, dz = pz - c.z;
                if (Mathf.Abs(dx) > c.r + radius || Mathf.Abs(dz) > c.r + radius) continue;
                float lx = dx * c.cos - dz * c.sin, lz = dx * c.sin + dz * c.cos;
                float cx = Mathf.Clamp(lx, -c.hw, c.hw), cz = Mathf.Clamp(lz, -c.hd, c.hd);
                float ox = lx - cx, oz = lz - cz, d2 = ox * ox + oz * oz;
                if (d2 >= radius * radius) continue;
                float nx, nz;
                if (d2 > 1e-8f)
                {
                    float d = Mathf.Sqrt(d2), push = radius - d;
                    nx = lx + ox / d * push;
                    nz = lz + oz / d * push;
                }
                else
                {
                    // centre is inside the box: leave by the nearest face
                    float fx = c.hw - Mathf.Abs(lx), fz = c.hd - Mathf.Abs(lz);
                    if (fx < fz) { nx = Sign(lx) * (c.hw + radius); nz = lz; }
                    else { nx = lx; nz = Sign(lz) * (c.hd + radius); }
                }
                px = c.x + nx * c.cos + nz * c.sin;
                pz = c.z - nx * c.sin + nz * c.cos;
                hit = c;
            }
            return hit;
        }

        static float Sign(float v) => v < 0 ? -1 : 1;     // Math.sign(v || 1)

        // The first standing, bullet-stopping prop a segment runs into: its
        // distance along the segment (Infinity if none). A slab test in each
        // box's own frame. `test` picks other props instead (Sight uses it
        // for the ones that block the view).
        public static float RayProps(List<Prop> props, float x, float z, float dx, float dz, float len, out Prop which, Func<Prop, bool> test = null)
        {
            float best = float.PositiveInfinity;
            which = null;
            foreach (var c in props)
            {
                if (test != null ? !test(c) : c.destroyed || !c.def.blocksBullets) continue;
                float ox = x - c.x, oz = z - c.z;
                if (Mathf.Abs(ox) > c.r + len + 1 || Mathf.Abs(oz) > c.r + len + 1) continue;
                float lx = ox * c.cos - oz * c.sin, lz = ox * c.sin + oz * c.cos;
                float ldx = dx * c.cos - dz * c.sin, ldz = dx * c.sin + dz * c.cos;
                float t0 = 0, t1 = len;
                if (!Slab(lx, ldx, c.hw, ref t0, ref t1) || !Slab(lz, ldz, c.hd, ref t0, ref t1)) continue;
                if (t0 < best) { best = t0; which = c; }
            }
            return best;
        }

        static bool Slab(float o, float d, float h, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-9f) return Mathf.Abs(o) <= h;
            float a = (-h - o) / d, b = (h - o) / d;
            if (a > b) (a, b) = (b, a);
            if (a > t0) t0 = a;
            if (b < t1) t1 = b;
            return t0 <= t1;
        }
    }
}
