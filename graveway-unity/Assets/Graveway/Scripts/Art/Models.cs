// Guns, props and pickups built from boxes and cylinders: a port of
// models.js. Shared meshes and cached materials wherever copies can share
// them. This is the placeholder art; real models replace these functions one
// at a time, keeping what they return (a gun's length, a prop's collider
// size). The people are real already: see Rigs.cs.

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public sealed class GunModel
    {
        public GameObject group;
        public Vector3 muzzle;               // where bullets leave, in the gun's own space
        public Bounds bounds;                // its outline in its own space, when known (for pictures)
        public string key;                   // the fitted parts it was built with
        public readonly List<Object> own = new List<Object>();
    }

    // A prop's model and its collider: a box hw x hd (half sizes), height tall.
    public sealed class PropModel
    {
        public GameObject group;
        public readonly List<Renderer> meshes = new List<Renderer>();          // flashed when hit
        public readonly List<Material[]> mats = new List<Material[]>();         // their own materials
        readonly List<Material[]> flashed = new List<Material[]>();
        public float hw, hd, height;
        public readonly List<Object> own = new List<Object>();                  // destroy with the prop
        public MeshRenderer head, pool, shadow;                                  // lamps; fire barrels' pool
        public MeshRenderer[] flames;
        public MeshRenderer burnBody, burnCabin, burnLights, burnHeads;          // vehicles, for the burnt wreck
        public readonly List<Renderer> burnAlso = new List<Renderer>();         // and anything else that chars

        public T Keep<T>(T r) where T : Renderer
        {
            meshes.Add(r);
            mats.Add(r.sharedMaterials);
            var f = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < f.Length; i++) f[i] = Art.Flash;
            flashed.Add(f);
            return r;
        }

        public void SetFlash(bool on)
        {
            for (int i = 0; i < meshes.Count; i++) meshes[i].sharedMaterials = on ? flashed[i] : mats[i];
        }

        // Swaps a kept mesh's own materials for good (a burnt wreck).
        public void Replace(Renderer r, Material m)
        {
            var all = new Material[r.sharedMaterials.Length];
            for (int k = 0; k < all.Length; k++) all[k] = m;
            int i = meshes.IndexOf(r);
            if (i >= 0) mats[i] = all;
            r.sharedMaterials = all;
        }
    }

    public static class Models
    {
        public const float GunScale = 1.7f;

        // ── Shared geometry ──────────────────────────────────────────────
        sealed class Shared
        {
            public Mesh grenade, coin, tracer, particle, billboard;
        }
        static Shared g;

        static Shared G
        {
            get
            {
                if (g != null) return g;
                g = new Shared
                {
                    grenade = new GeoBuilder().Sphere(0.13f, 10, 8, M.I).Build("grenade"),
                    coin = new GeoBuilder().Cylinder(0.24f, 0.24f, 0.07f, 14, M.Rx(Mathf.PI / 2)).Build("coin"),
                    tracer = new GeoBuilder().Box(0.06f, 0.06f, 1.1f).Build("tracer"),
                    particle = new GeoBuilder().Box(1, 1, 1).Build("particle"),
                    billboard = BillboardQuad(),
                };
                return g;
            }
        }

        public static Mesh GrenadeMesh => G.grenade;
        public static Mesh CoinMesh => G.coin;
        public static Mesh TracerMesh => G.tracer;
        public static Mesh ParticleMesh => G.particle;

        // A 1 x 1 quad facing +z with its texture flipped left to right: a
        // camera-facing sprite turned to face the camera reads the right way
        // round in Unity (see Tunables.cs on the mirror image).
        static Mesh BillboardQuad()
        {
            var m = new Mesh { name = "billboard" };
            m.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
            m.SetNormals(new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            m.SetUVs(0, new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateBounds();
            return m;
        }

        static Transform Group(string name, Transform parent = null)
        {
            var t = new GameObject(name).transform;
            if (parent) t.SetParent(parent, false);
            return t;
        }

        static MeshRenderer At(Transform parent, Mesh mesh, Material mat, float x = 0, float y = 0, float z = 0, string name = "mesh")
        {
            var r = Art.Show(parent, mesh, mat, name);
            r.transform.localPosition = new Vector3(x, y, z);
            return r;
        }

        // ── Guns ─────────────────────────────────────────────────────────
        // Barrel along +z with the grip at the origin. Drawn bigger than life
        // (GunScale) so they read from the overhead camera, in mid-tone metal
        // with a light top rail and a little glow. Every part that changes the
        // outline shows: barrel length, muzzle, magazine, sight, stock, and
        // what hangs under the handguard.
        sealed class GunShape { public float len, w, h; public int color, barrel; public bool mag, barrels, drum; }
        static readonly Dictionary<string, GunShape> Shapes = new Dictionary<string, GunShape>
        {
            { "pistol", new GunShape { len = 0.4f, w = 0.15f, h = 0.16f, color = 0x5a5e68 } },
            { "smg", new GunShape { len = 0.5f, w = 0.15f, h = 0.16f, color = 0x52565f, mag = true } },
            { "shotgun", new GunShape { len = 0.82f, w = 0.14f, h = 0.13f, color = 0x8a5a36, barrel = 0x4e525a } },
            { "rifle", new GunShape { len = 0.8f, w = 0.14f, h = 0.15f, color = 0x5c6a48, mag = true } },
            { "minigun", new GunShape { len = 0.86f, w = 0.26f, h = 0.2f, color = 0x52565f, barrels = true } },
            { "launcher", new GunShape { len = 0.72f, w = 0.22f, h = 0.17f, color = 0x6a7a40, drum = true } },
        };
        const int Dark = 0x2a2c31;

        public static GunModel Gun(string model, IDictionary<string, string> fitted)
        {
            if (Guns.Has(model)) return Guns.Build(model, fitted);
            var s = Shapes.TryGetValue(model, out var shape) ? shape : Shapes["pistol"];
            var gun = new GunModel { group = new GameObject("gun " + model) };
            var t = gun.group.transform;
            // What it comes with for every slot it has, and nothing for the rest.
            var parts = new Dictionary<string, string>();
            var wdef = System.Array.Find(Tunables.Weapons, w => w.model == model);
            if (wdef != null) foreach (var slot in wdef.slots) parts[slot] = Tunables.FittedPart(wdef, slot, fitted).id;
            string F(string slot, string def = null) => parts.TryGetValue(slot, out var v) ? v : def;
            string barrel = F("barrel");
            float L = s.len * (barrel == "long" ? 1.22f : barrel == "short" ? 0.82f : 1), top = s.h / 2;

            void Add(GeoBuilder b, int color, float glow = 0.22f)
            {
                var mesh = b.Build("gun part");
                gun.own.Add(mesh);
                Art.Show(t, mesh, Art.Lit(Hex.C(color), glow), "part");
            }
            void AddUnlit(GeoBuilder b, int color)
            {
                var mesh = b.Build("gun light");
                gun.own.Add(mesh);
                Art.Show(t, mesh, Art.Unlit(color), "light");
            }

            // Receiver and barrel in one piece, with the grip.
            var body = new GeoBuilder().Box(s.w, s.h, L, 0, 0, L / 2);
            if (barrel == "heavy") body.Box(s.w * 1.25f, s.h * 0.9f, L * 0.45f, 0, 0, L * 0.76f);
            Add(body, s.color);
            string grip = F("grip");
            Add(new GeoBuilder().Box(s.w * (grip == "ergo" ? 0.95f : 0.8f), 0.18f, 0.1f, 0, -0.12f, 0.08f),
                grip == "rubber" ? 0x24262a : grip == "stippled" ? 0x6a5a40 : grip == "ergo" ? 0x3a3e46 : s.color);
            // The light strip along the top is what you see from above.
            Add(new GeoBuilder().Box(s.w * 0.4f, 0.03f, L * 0.85f, 0, top + 0.015f, L * 0.48f), 0x8c929c, 0.12f);
            if (s.barrel != 0) Add(new GeoBuilder().Box(0.06f, 0.06f, L * 0.55f, 0, 0.07f, L * 0.72f), s.barrel);

            // Muzzle
            float muzzle = L;
            string mz = F("muzzle", "none");
            if (mz == "comp") { Add(new GeoBuilder().Box(s.w * 1.05f, s.h * 0.75f, 0.08f, 0, 0, L + 0.04f), Dark); muzzle += 0.08f; }
            if (mz == "brake") { Add(new GeoBuilder().Box(s.w * 1.6f, s.h * 0.55f, 0.1f, 0, 0, L + 0.05f), Dark); muzzle += 0.1f; }
            if (mz == "choke") { Add(new GeoBuilder().Box(s.w * 0.8f, s.h * 0.8f, 0.05f, 0, 0.02f, L + 0.025f), Dark); muzzle += 0.05f; }
            if (mz == "suppressor")
            {
                float r = Mathf.Max(s.w, s.h) * 0.42f;
                Add(new GeoBuilder().Cylinder(r, r, 0.28f, 10, M.T(0, 0, L + 0.14f) * M.Rx(Mathf.PI / 2)), 0x34363c);
                muzzle += 0.28f;
            }
            if (s.barrels)
            {
                var b = new GeoBuilder();
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * Mathf.PI * 2;
                    b.Cylinder(0.025f, 0.025f, 0.5f, 6, M.T(Mathf.Cos(a) * 0.06f, Mathf.Sin(a) * 0.06f, L + 0.12f) * M.Rx(Mathf.PI / 2));
                }
                Add(b, 0x6a6e76, 0.12f);
                muzzle = L + 0.37f;
            }

            // Magazine: a box under the gun, in the grip for a pistol, a drum on the launcher.
            string mag = F("mag", "factory");
            if (s.drum)
            {
                float r = mag == "extended" ? 0.19f : 0.15f;
                Add(new GeoBuilder().Cylinder(r, r, 0.22f, 12, M.T(0, -0.02f, 0.22f) * M.Rx(Mathf.PI / 2)), 0x3e4630);
            }
            else if (mag == "drum")
                Add(new GeoBuilder().Cylinder(0.14f, 0.14f, s.w * 1.1f, 14, M.T(0, -top - 0.12f, L * 0.42f) * M.Rz(Mathf.PI / 2)), Dark);
            else if (s.mag || mag != "factory")
            {
                float lng = mag == "extended" ? 0.36f : 0.22f, atZ = s.mag ? L * 0.5f : 0.08f, y0 = s.mag ? -top : -0.21f;
                if (s.mag || mag == "extended") Add(new GeoBuilder().Box(s.w * 0.7f, lng, 0.1f, 0, y0 - lng / 2 + 0.03f, atZ), s.mag ? s.color : Dark);
            }
            if (mag == "quick") Add(new GeoBuilder().Box(s.w * 0.75f, 0.04f, 0.12f, 0, s.mag ? -top - 0.22f : -0.22f, s.mag ? L * 0.5f : 0.08f), 0xe0b020, 0.35f);

            // Sight
            string sight = F("sight", "iron");
            if (sight == "reddot")
            {
                Add(new GeoBuilder().Box(0.06f, 0.06f, 0.08f, 0, top + 0.06f, L * 0.32f), Dark);
                AddUnlit(new GeoBuilder().Box(0.025f, 0.025f, 0.01f, 0, top + 0.07f, L * 0.32f - 0.045f), 0xff3030);
            }
            else if (sight == "holo")
                Add(new GeoBuilder().Box(0.08f, 0.075f, 0.03f, 0, top + 0.065f, L * 0.36f).Box(0.08f, 0.02f, 0.1f, 0, top + 0.035f, L * 0.33f), Dark);
            else if (sight == "scope")
                Add(new GeoBuilder().Cylinder(0.04f, 0.045f, 0.3f, 10, M.T(0, top + 0.07f, L * 0.35f) * M.Rx(Mathf.PI / 2))
                    .Box(0.03f, 0.05f, 0.04f, 0, top + 0.03f, L * 0.3f), Dark);

            // Stock, behind the grip
            string stock = F("stock");
            if (stock == "light") Add(new GeoBuilder().Box(s.w * 0.25f, s.h * 0.9f, 0.24f, 0, -0.02f, -0.12f).Box(s.w * 0.5f, s.h * 1.1f, 0.04f, 0, -0.03f, -0.24f), Dark);
            else if (stock == "heavy") Add(new GeoBuilder().Box(s.w * 0.9f, s.h * 1.25f, 0.26f, 0, -0.03f, -0.13f).Box(s.w * 0.95f, s.h * 1.4f, 0.05f, 0, -0.04f, -0.27f), 0x24262a);
            else if (stock == "precision") Add(new GeoBuilder().Box(s.w * 0.7f, s.h * 1.05f, 0.32f, 0, -0.03f, -0.16f).Box(s.w * 0.6f, 0.04f, 0.16f, 0, top + 0.01f, -0.14f), s.color);
            else if (stock != null) Add(new GeoBuilder().Box(s.w * 0.7f, s.h * 0.95f, 0.22f, 0, -0.03f, -0.11f), s.color);

            // Under the handguard
            string rail = F("rail", "none");
            float rz = L * 0.7f, under = -s.h / 2;
            if (rail == "foregrip") Add(new GeoBuilder().Box(0.05f, 0.13f, 0.05f, 0, under - 0.065f, rz), Dark);
            if (rail == "angled") Add(new GeoBuilder().Box(0.05f, 0.05f, 0.16f, M.Rx(-0.5f) * M.T(0, under - 0.04f, rz)), Dark);
            if (rail == "laser" || rail == "light")
            {
                float tip = Mathf.Min(rz + 0.07f, L - 0.02f);
                Add(new GeoBuilder().Box(0.06f, 0.05f, 0.12f, 0, under - 0.03f, tip - 0.06f), Dark);
                AddUnlit(new GeoBuilder().Box(0.04f, 0.035f, 0.012f, 0, under - 0.03f, tip + 0.002f), rail == "laser" ? 0xff2a2a : 0xfff6d8);
            }

            t.localScale = Vector3.one * GunScale;
            gun.muzzle = new Vector3(0, 0.02f, muzzle);
            return gun;
        }

        // ── Props ────────────────────────────────────────────────────────
        static readonly int[] CarColors = { 0x6e2b26, 0x2d4660, 0x4b5a3a, 0x7a7466, 0x2a2a2e, 0x8a6a2a, 0x5a5f66 };
        static Mesh carBody, carCabin, carWheels, carLights, carHeads;

        // Burnt-out wreck materials, swapped in when a vehicle blows up.
        public static Material BurntBody => Art.Lit(0x1e1b18);
        public static Material BurntGlass => Art.Lit(0x0a0a0a);
        public static Material BurntLamp => Art.Unlit(0x1a1210);

        // A parked car: the kit's sports car or pickup (one in four with its
        // headlights left on), at the web version's size.
        public static PropModel Car(Rng rng)
        {
            if (Kit.Has("Vehicle_Sports")) return KitCar(rng);
            if (carBody == null)
            {
                carBody = new GeoBuilder().Box(1.9f, 0.62f, 4.2f, 0, 0.66f, 0).Box(1.84f, 0.12f, 0.3f, 0, 0.42f, 2.0f).Box(1.84f, 0.12f, 0.3f, 0, 0.42f, -2.0f).Build("car body");
                carCabin = new GeoBuilder().Box(1.7f, 0.6f, 2.1f, 0, 1.27f, -0.25f).Build("car cabin");
                var w = new GeoBuilder();
                foreach (var (x, z) in new[] { (-0.92f, 1.35f), (0.92f, 1.35f), (-0.92f, -1.35f), (0.92f, -1.35f) })
                    w.Cylinder(0.37f, 0.37f, 0.28f, 10, M.T(x, 0.37f, z) * M.Rz(Mathf.PI / 2));
                carWheels = w.Build("car wheels");
                carLights = new GeoBuilder().Box(0.34f, 0.12f, 0.04f, -0.66f, 0.78f, -2.11f).Box(0.34f, 0.12f, 0.04f, 0.66f, 0.78f, -2.11f).Build("tail lights");
                carHeads = new GeoBuilder().Box(0.34f, 0.12f, 0.04f, -0.66f, 0.78f, 2.11f).Box(0.34f, 0.12f, 0.04f, 0.66f, 0.78f, 2.11f).Build("headlights");
            }
            var p = new PropModel { group = new GameObject("car"), hw = 0.95f, hd = 2.1f, height = 1.6f };
            var t = p.group.transform;
            p.burnBody = p.Keep(At(t, carBody, Art.Lit(CarColors[(int)(rng.Next() * CarColors.Length)]), name: "body"));
            p.burnCabin = p.Keep(At(t, carCabin, Art.Lit(0x1a1f26), name: "cabin"));
            At(t, carWheels, Art.Lit(0x141414), name: "wheels");
            p.burnLights = At(t, carLights, Art.Unlit(0x7a1010), name: "tail lights");
            p.burnHeads = At(t, carHeads, Art.Unlit(rng.Next() < 0.25f ? 0xfff0b0 : 0x4a4a40), name: "headlights");
            p.shadow = Art.BlobShadow(t, 4.4f, 0.5f);
            return p;
        }

        static PropModel KitCar(Rng rng)
        {
            var p = new PropModel { group = new GameObject("car"), hw = 0.95f, hd = 2.1f, height = 1.6f };
            var t = p.group.transform;
            string id = rng.Next() < 0.5f ? "Vehicle_Sports" : "Vehicle_Pickup";
            bool lit = rng.Next() < 0.25f;
            var car = Kit.Place(id, t, 4.3f / Kit.Find(id).bounds.size.z,
                n => n == "Headlights" ? Art.Unlit(lit ? 0xfff0b0 : 0x4a4a40) : n == "BrakeLight" ? Art.Unlit(0x7a1010) : null);
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>())
            {
                p.Keep(r);
                if (r.sharedMaterials.Length > 1) p.burnBody = r;              // the body, with its lights
                else p.burnAlso.Add(r);                                        // the wheels
            }
            p.shadow = Art.BlobShadow(t, 4.4f, 0.5f);
            return p;
        }

        // A lorry from the kit, about 6 m long: the docks' and the highway's.
        public static PropModel Truck(Rng rng)
        {
            var b = Kit.Find("Vehicle_Truck").bounds;
            float s = 6f / b.size.z;
            var p = new PropModel { group = new GameObject("truck"), hw = b.size.x * s / 2, hd = b.size.z * s / 2, height = b.size.y * s };
            var t = p.group.transform;
            bool lit = rng.Next() < 0.25f;
            var truck = Kit.Place("Vehicle_Truck", t, s,
                n => n == "Headlights" ? Art.Unlit(lit ? 0xfff0b0 : 0x4a4a40) : n == "BrakeLight" ? Art.Unlit(0x7a1010) : null);
            foreach (var r in truck.GetComponentsInChildren<MeshRenderer>())
            {
                p.Keep(r);
                if (r.sharedMaterials.Length > 1) p.burnBody = r;
                else p.burnAlso.Add(r);
            }
            p.shadow = Art.BlobShadow(t, 6.4f, 0.5f);
            return p;
        }

        // A shipping container from the kit, 6 m long along its local z.
        public static PropModel Container(Rng rng)
        {
            var go = ContainerModel(rng, out float w, out float h);
            var p = new PropModel { group = new GameObject("container"), hw = w / 2, hd = 3, height = h };
            go.transform.SetParent(p.group.transform, false);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) p.Keep(r);
            return p;
        }

        // Just the look of one, for the stacks along the docks.
        public static GameObject ContainerModel(Rng rng, out float width, out float height)
        {
            string id = rng.Next() < 0.5f ? "Container_Green" : "Container_Red";
            var b = Kit.Find(id).bounds;
            float s = 6f / b.size.x;
            var holder = new GameObject("container");
            var inner = Kit.Place(id, holder.transform, s);
            inner.transform.localRotation = M.Yaw(Mathf.PI / 2);   // its length along z
            width = b.size.z * s;
            height = b.size.y * s;
            return holder;
        }

        // A concrete road barrier from the kit, 2 m long.
        public static PropModel Jersey()
        {
            var b = Kit.Find("TrafficBarrier_1").bounds;
            float s = 2f / b.size.x;
            var p = new PropModel { group = new GameObject("jersey"), hw = 1, hd = b.size.z * s / 2, height = b.size.y * s };
            foreach (var r in Kit.Place("TrafficBarrier_1", p.group.transform, s).GetComponentsInChildren<MeshRenderer>()) p.Keep(r);
            return p;
        }

        static Mesh crateBoxes;

        // A pallet of wooden crates: the kit's pallet, boxes on it.
        public static PropModel Crates(Rng rng)
        {
            crateBoxes ??= new GeoBuilder().Box(0.9f, 0.8f, 0.9f, -0.48f, 0.55f, -0.46f).Box(0.9f, 0.8f, 0.9f, 0.48f, 0.55f, -0.46f)
                .Box(0.9f, 0.8f, 0.9f, -0.48f, 0.55f, 0.48f).Box(0.9f, 0.8f, 0.9f, 0.48f, 0.55f, 0.48f)
                .Box(0.9f, 0.8f, 0.9f, -0.2f, 1.35f, 0).Build("crates");
            var p = new PropModel { group = new GameObject("crates"), hw = 1, hd = 1, height = 1.75f };
            var t = p.group.transform;
            var pb = Kit.Find("Pallet").bounds;
            foreach (var r in Kit.Place("Pallet", t, 2f / Mathf.Max(pb.size.x, pb.size.z)).GetComponentsInChildren<MeshRenderer>()) p.Keep(r);
            p.Keep(At(t, crateBoxes, Art.Lit(new[] { 0x8a6a44, 0x7a5a38, 0x6e5a3e }[(int)(rng.Next() * 3)]), name: "boxes"));
            p.shadow = Art.BlobShadow(t, 2.6f, 0.5f);
            return p;
        }

        static Mesh tankCab, tankGlass, tankChassis, tankTank, tankBands, tankWheels;

        // A fuel tanker: cab, chassis and a long tank. Rare, and very explosive.
        public static PropModel Tanker(Rng rng)
        {
            if (tankCab == null)
            {
                tankCab = new GeoBuilder().Box(2.2f, 1.5f, 1.9f, 0, 1.25f, 2.55f).Box(2.2f, 0.9f, 0.9f, 0, 2.4f, 2.4f).Build("tanker cab");
                tankGlass = new GeoBuilder().Box(2.0f, 0.6f, 0.06f, 0, 2.15f, 3.52f).Build("tanker glass");
                tankChassis = new GeoBuilder().Box(1.6f, 0.3f, 7.2f, 0, 0.75f, -0.1f).Build("tanker chassis");
                tankTank = new GeoBuilder().Cylinder(1.15f, 1.15f, 5.2f, 16, M.T(0, 2.0f, -1.1f) * M.Rx(Mathf.PI / 2)).Build("tank");
                var bands = new GeoBuilder();
                foreach (var z in new[] { -3.2f, -1.1f, 1.0f }) bands.Cylinder(1.18f, 1.18f, 0.14f, 16, M.T(0, 2.0f, z) * M.Rx(Mathf.PI / 2));
                tankBands = bands.Build("tank bands");
                var w = new GeoBuilder();
                foreach (var z in new[] { 2.6f, 1.0f, -1.6f, -2.8f })
                    foreach (var x in new[] { -1.05f, 1.05f }) w.Cylinder(0.46f, 0.46f, 0.32f, 10, M.T(x, 0.46f, z) * M.Rz(Mathf.PI / 2));
                tankWheels = w.Build("tanker wheels");
            }
            var p = new PropModel { group = new GameObject("tanker"), hw = 1.15f, hd = 3.6f, height = 3.1f };
            var t = p.group.transform;
            p.burnBody = p.Keep(At(t, tankCab, Art.Lit(new[] { 0x8a2a22, 0x2a4a6a, 0xd0c8b0 }[(int)(rng.Next() * 3)]), name: "cab"));
            p.burnCabin = p.Keep(At(t, tankTank, Art.Lit(0xb8b8b0), name: "tank"));
            p.burnLights = p.Keep(At(t, tankBands, Art.Unlit(0xe08a1a), name: "bands"));
            p.burnHeads = At(t, tankGlass, Art.Lit(0x1a1f26), name: "glass");
            At(t, tankChassis, Art.Lit(0x222222), name: "chassis");
            At(t, tankWheels, Art.Lit(0x141414), name: "wheels");
            p.shadow = Art.BlobShadow(t, 1, 0.55f);
            p.shadow.transform.localScale = new Vector3(3.2f, 1, 8);
            return p;
        }

        // A condemned building front on the sidewalk, scaffolded and taped off.
        // Its long side runs along the road (local z); it falls toward local +x.
        public static PropModel Condemned(float width, float height)
        {
            const float d = 1.2f;
            var p = new PropModel { group = new GameObject("condemned"), hw = d / 2 + 0.9f, hd = width / 2, height = height };
            var t = p.group.transform;
            var slabMesh = new GeoBuilder().Box(d, height, width, M.T(0, height / 2, 0), 8, 16).Build("condemned");
            var mat = Art.LitOwn(Color.white, Tex.Condemned);
            p.own.Add(slabMesh);
            p.own.Add(mat);
            p.Keep(At(t, slabMesh, mat, name: "front"));
            // Scaffolding: a grid of thin poles and planks on the road side.
            var bars = new GeoBuilder();
            for (float z = -width / 2; z <= width / 2 + 0.01f; z += width / 4) bars.Box(0.08f, height * 0.8f, 0.08f, d / 2 + 0.6f, height * 0.4f, z);
            for (float y = 2.5f; y < height * 0.8f; y += 2.5f)
            {
                bars.Box(0.06f, 0.06f, width, d / 2 + 0.6f, y, 0);
                bars.Box(0.7f, 0.05f, width, d / 2 + 0.3f, y, 0);
            }
            var scaffold = bars.Build("scaffolding");
            p.own.Add(scaffold);
            At(t, scaffold, Art.Lit(0x8a6a3a), name: "scaffolding");
            // The sign faces the road (+x). Flipped so the text reads in Unity's mirror image.
            var sign = new GeoBuilder().Plane(2.6f, 0.98f, M.T(d / 2 + 0.9f, 2.2f, 0) * M.Ry(Mathf.PI / 2) * M.S(-1, 1, 1)).Build("sign");
            p.own.Add(sign);
            At(t, sign, Art.Unlit(Color.white, Tex.Danger), name: "sign");
            var tape = new GeoBuilder().Box(0.04f, 0.12f, width, d / 2 + 0.95f, 1.1f, 0).Build("tape");
            p.own.Add(tape);
            At(t, tape, Art.Unlit(0xe8c21e), name: "tape");
            return p;
        }

        // A broken-off chunk of concrete for collapses.
        public static PropModel Slab(float w, float h, float d)
        {
            var p = new PropModel { group = new GameObject("slab"), hw = w / 2, hd = d / 2, height = h };
            var mesh = new GeoBuilder().Box(w, h, d, 0, h / 2, 0).Build("slab");
            p.own.Add(mesh);
            p.Keep(At(p.group.transform, mesh, Art.Lit(0x7a746c), name: "slab"));
            return p;
        }

        static Mesh barrierBlock, barrierStripe;

        // A barricade: two of the kit's plastic barriers, end to end.
        public static PropModel Barrier()
        {
            if (Kit.Has("PlasticBarrier"))
            {
                var b = new PropModel { group = new GameObject("barricade"), hw = 1.2f, hd = 0.32f, height = 0.95f };
                float s = 1.2f / Kit.Find("PlasticBarrier").bounds.size.x;
                foreach (float x in new[] { -0.6f, 0.6f })
                {
                    var half = Kit.Place("PlasticBarrier", b.group.transform, s);
                    half.transform.localPosition = new Vector3(x, 0, 0);
                    foreach (var r in half.GetComponentsInChildren<MeshRenderer>()) b.Keep(r);
                }
                return b;
            }
            if (barrierBlock == null)
            {
                barrierBlock = new GeoBuilder().Box(2.4f, 0.55f, 0.62f, 0, 0.275f, 0).Box(2.4f, 0.38f, 0.3f, 0, 0.74f, 0).Build("barrier");
                barrierStripe = new GeoBuilder().Box(2.42f, 0.12f, 0.32f, 0, 0.82f, 0).Build("barrier stripe");
            }
            var p = new PropModel { group = new GameObject("barricade"), hw = 1.2f, hd = 0.32f, height = 0.95f };
            p.Keep(At(p.group.transform, barrierBlock, Art.Lit(0x8d8a82), name: "block"));
            p.Keep(At(p.group.transform, barrierStripe, Art.Lit(0xd8582a), name: "stripe"));
            return p;
        }

        static Mesh lampPole, lampHead;

        // A street lamp. The collider is just the pole; the arm overhangs the road.
        // Bigger (`size`), with a wider pool of its own colour, it's a
        // floodlight on the docks.
        public static PropModel Lamp(float size = 1, int color = 0xff9a3a, float pool = 7.5f)
        {
            if (lampPole == null)
            {
                lampPole = new GeoBuilder().Cylinder(0.08f, 0.11f, 5.4f, 8, M.T(0, 2.7f, 0)).Box(0.1f, 0.1f, 1.6f, 0, 5.35f, 0.75f).Build("lamp pole");
                lampHead = new GeoBuilder().Box(0.34f, 0.12f, 0.5f).Build("lamp head");
            }
            var p = new PropModel { group = new GameObject("lamp"), hw = 0.13f, hd = 0.13f, height = 5.4f * size };
            var t = p.group.transform;
            var light = new Vector3(0, 5.28f, 1.5f);
            if (Kit.Has("StreetLights"))
            {
                // The kit's street light; its own glass dark, our glowing head
                // just under it (it flickers and goes out on its own).
                float s = 5.4f * size / Kit.Find("StreetLights").bounds.size.y;
                var lamp = Kit.Place("StreetLights", t, s, n => n == "Light" ? Art.Lit(0x2a2c30) : null);
                foreach (var r in lamp.GetComponentsInChildren<MeshRenderer>()) p.Keep(r);
                light = Kit.At("StreetLights", Kit.Find("StreetLights").light, s) - new Vector3(0, 0.06f, 0);
            }
            else p.Keep(At(t, lampPole, Art.Lit(0x3a3c40), name: "pole"));
            p.head = At(t, lampHead, Art.Unlit(0xffc979), light.x, light.y, light.z, "head");
            p.pool = Art.GlowDecal(t, color, pool, 0.32f);
            p.pool.transform.localPosition = new Vector3(light.x, 0.03f, light.z + 0.1f);
            return p;
        }

        static Mesh fireDrum, flameQuad;

        public static PropModel FireBarrel()
        {
            if (fireDrum == null)
            {
                fireDrum = new GeoBuilder().Cylinder(0.34f, 0.34f, 0.95f, 10, M.T(0, 0.475f, 0)).Build("fire barrel");
                flameQuad = new GeoBuilder().Plane(0.9f, 1.4f, M.I).Build("flame");
            }
            var p = new PropModel { group = new GameObject("fire barrel"), hw = 0.36f, hd = 0.36f, height = 0.95f };
            var t = p.group.transform;
            if (Kit.Has("Barrel"))
            {
                var drum = Kit.Place("Barrel", t, 0.95f / Kit.Find("Barrel").bounds.size.y);
                foreach (var r in drum.GetComponentsInChildren<MeshRenderer>()) p.Keep(r);
            }
            else p.Keep(At(t, fireDrum, Art.Lit(0x5a3a26), name: "drum"));
            p.flames = new MeshRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                p.flames[i] = At(t, flameQuad, Art.Glow(Color.white, Tex.Fire), 0, 1.55f, 0, "flame");
                p.flames[i].transform.localRotation = M.Yaw(i * Mathf.PI / 2);
            }
            // Its own pool material: the flicker changes its brightness.
            var poolMat = new Material(Art.Glow(Hex.C(0xff7a2a), Tex.Glow, 1));
            p.own.Add(poolMat);
            p.pool = Art.GlowDecal(t, 0xff7a2a, 6, 0.5f);
            p.pool.sharedMaterial = poolMat;
            return p;
        }

        static Mesh xBody, xBand, xLid;

        public static PropModel ExplosiveBarrel()
        {
            if (xBody == null)
            {
                xBody = new GeoBuilder().Cylinder(0.33f, 0.33f, 1.0f, 12, M.T(0, 0.5f, 0)).Build("barrel");
                xBand = new GeoBuilder().Cylinder(0.336f, 0.336f, 0.16f, 12, M.T(0, 0.62f, 0)).Build("barrel band");
                xLid = new GeoBuilder().Cylinder(0.28f, 0.28f, 0.04f, 12, M.T(0, 1.01f, 0)).Build("barrel lid");
            }
            var p = new PropModel { group = new GameObject("explosive barrel"), hw = 0.34f, hd = 0.34f, height = 1.0f };
            var t = p.group.transform;
            p.Keep(At(t, xBody, Art.Lit(0xb3261e), name: "body"));
            p.Keep(At(t, xBand, Art.Unlit(0xffc21a), name: "band"));
            p.Keep(At(t, xLid, Art.Lit(0x6a1a14), name: "lid"));
            Art.BlobShadow(t, 1.1f, 0.45f);
            return p;
        }

        public static PropModel Wall(float width)
        {
            const float h = 2.4f, d = 0.45f;
            var p = new PropModel { group = new GameObject("wall"), hw = width / 2, hd = d / 2, height = h };
            var t = p.group.transform;
            var mesh = new GeoBuilder().Box(width, h, d, M.T(0, h / 2, 0), 2, 1).Build("wall");
            // Its own material, so damage can darken this wall alone.
            var mat = Art.LitOwn(Color.white, Tex.Brick);
            var cap = new GeoBuilder().Box(width + 0.08f, 0.12f, d + 0.08f, 0, h + 0.06f, 0).Build("wall cap");
            p.own.Add(mesh); p.own.Add(mat); p.own.Add(cap);
            p.Keep(At(t, mesh, mat, name: "bricks"));
            p.Keep(At(t, cap, Art.Lit(0x6a6460), name: "cap"));
            return p;
        }

        // ── Pickups ──────────────────────────────────────────────────────
        static Mesh packBox, packCross, vestBody, vestBand;

        public static GameObject HealthPack()
        {
            if (packBox == null)
            {
                packBox = new GeoBuilder().Box(0.62f, 0.38f, 0.46f, 0, 0.19f, 0).Build("health pack");
                packCross = new GeoBuilder().Box(0.14f, 0.02f, 0.36f, 0, 0.39f, 0).Box(0.4f, 0.02f, 0.12f, 0, 0.39f, 0).Build("red cross");
            }
            var go = new GameObject("health pack");
            At(go.transform, packBox, Art.Lit(0xf2f2ee));
            At(go.transform, packCross, Art.Unlit(0xe02a2a));
            return go;
        }

        public static GameObject ArmorVest()
        {
            if (vestBody == null)
            {
                vestBody = new GeoBuilder().Box(0.62f, 0.56f, 0.22f, 0, 0.3f, 0).Box(0.16f, 0.2f, 0.2f, -0.2f, 0.66f, 0).Box(0.16f, 0.2f, 0.2f, 0.2f, 0.66f, 0).Build("vest");
                vestBand = new GeoBuilder().Box(0.64f, 0.08f, 0.24f, 0, 0.32f, 0).Build("vest band");
            }
            var go = new GameObject("armor vest");
            At(go.transform, vestBody, Art.Lit(0x2f4f7a));
            At(go.transform, vestBand, Art.Unlit(0x7ac8ff));
            return go;
        }

        // A power-up on the road: its badge floating in a soft halo of its
        // colour. Both are turned to face the camera every frame (Billboards).
        public static GameObject PowerUp(string id, List<Transform> billboards)
        {
            var def = Tunables.PowerUp(id);
            var go = new GameObject("power-up " + id);
            var halo = At(go.transform, G.billboard, Art.Glow(def.color, Tex.Glow, 0.7f), 0, 1.05f, 0, "halo");
            halo.transform.localScale = Vector3.one * 2.5f;
            var icon = At(go.transform, G.billboard, Art.Fade(Color.white, 1, Tex.Power(id)), 0, 1.05f, 0, "badge");
            icon.transform.localScale = Vector3.one * 1.35f;
            billboards.Add(halo.transform);
            billboards.Add(icon.transform);
            return go;
        }

        // ── Debris left lying about ──────────────────────────────────────
        // A low pile of broken bits covering w x d metres. One merged mesh.
        public static Mesh Rubble(Rng rng, float w, float d)
        {
            var b = new GeoBuilder();
            int n = Mathf.Max(6, Mathf.RoundToInt(w * d * 3));
            for (int i = 0; i < n; i++)
            {
                float s = 0.18f + rng.Next() * 0.4f;
                float bh = s * (0.4f + rng.Next() * 0.6f), bd = s * (0.6f + rng.Next());
                float x = (rng.Next() - 0.5f) * w, y = s * 0.2f, z = (rng.Next() - 0.5f) * d;
                // three rotates the geometry about its origin after placing it.
                b.Box(s, bh, bd, M.Ry((rng.Next() - 0.5f) * 0.6f) * M.T(x, y, z));
            }
            return b.Build("rubble");
        }

        public static MeshRenderer Scorch(Transform parent, float size)
        {
            var r = Art.Show(parent, Art.FlatMesh, Art.Fade(Color.black, 0.7f, Tex.Scorch), "scorch");
            r.transform.localScale = new Vector3(size, 1, size);
            return r;
        }
    }
}
