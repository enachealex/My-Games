// Real guns: the Zombie Apocalypse Kit's pistol, SMG, shotgun and rifle
// (Quaternius, CC0), wearing Ultimate Guns accessories for the parts that
// show (suppressor, compensator, scope, foregrip, light, stock) and small
// built shapes for the rest (red dot, holo sight, magazines, barrels).
// The launcher and minigun have no model in the packs yet, so Models.Gun
// still builds those.
//
// Like Models.Gun: grip at the origin, barrel along +z. Parts are placed in
// the kit gun's own size (metres in a survivor's hand), where
// Editor/ArtImport.cs measured them from its mesh, and then the whole gun is
// scaled to Length.

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public static class Guns
    {
        // Stock to muzzle, as long as the web version's guns: bigger than
        // life, so they read from the camera. (The kit draws them bigger
        // still, for its cartoon style.)
        static readonly Dictionary<string, float> Length = new Dictionary<string, float>
        {
            { "pistol", 0.7f }, { "smg", 0.9f }, { "shotgun", 1.35f }, { "rifle", 1.45f },
        };

        static readonly Dictionary<string, string> KitGun = new Dictionary<string, string>
        {
            { "pistol", "Pistol" }, { "smg", "SMG" }, { "shotgun", "Shotgun" }, { "rifle", "Rifle" },
        };
        const int Dark = 0x2a2c31;

        public static bool Has(string model) =>
            KitGun.TryGetValue(model, out var id) && ArtCatalog.Get != null && ArtCatalog.Get.FindGun(id) != null;

        public static GunModel Build(string model, IDictionary<string, string> fitted)
        {
            var cat = ArtCatalog.Get;
            var g = cat.FindGun(KitGun[model]);
            var gun = new GunModel { group = new GameObject("gun " + model) };
            var t = gun.group.transform;
            Show(t, g.body, cat);

            // What it comes with for every slot it has, and nothing for the rest.
            var parts = new Dictionary<string, string>();
            var wdef = System.Array.Find(Tunables.Weapons, w => w.model == model);
            if (wdef != null) foreach (var slot in wdef.slots) parts[slot] = Tunables.FittedPart(wdef, slot, fitted).id;
            string F(string slot, string def = null) => parts.TryGetValue(slot, out var v) ? v : def;
            float bore = Mathf.Max(0.025f, g.width * 0.18f);    // about how thick the barrel is
            var muzzle = g.muzzle;

            void Add(GeoBuilder b, int color, float glow = 0.15f)
            {
                var mesh = b.Build("gun part");
                gun.own.Add(mesh);
                Art.Show(t, mesh, Art.Lit(Hex.C(color), glow), "part");
            }
            void Glint(GeoBuilder b, int color)
            {
                var mesh = b.Build("gun light");
                gun.own.Add(mesh);
                Art.Show(t, mesh, Art.Unlit(color), "light");
            }
            void Part(string id, Vector3 at, float tilt = 0, float stretch = 1, float thick = 1)
            {
                var p = cat.FindPart(id);
                if (p == null) return;
                var holder = new GameObject(id).transform;
                holder.SetParent(t, false);
                holder.localPosition = at;
                holder.localRotation = Quaternion.Euler(tilt, 0, 0);
                holder.localScale = new Vector3(thick, thick, stretch);
                Show(holder, p, cat);
            }

            // Barrel: longer sticks out further, heavy wears a sleeve.
            string barrel = F("barrel");
            if (barrel == "long")
            {
                Add(new GeoBuilder().Cylinder(bore * 0.5f, bore * 0.5f, 0.15f, 8, M.T(muzzle.x, muzzle.y, muzzle.z + 0.075f) * M.Rx(Mathf.PI / 2)), Dark);
                muzzle.z += 0.15f;
            }
            else if (barrel == "heavy")
                Add(new GeoBuilder().Cylinder(bore * 0.85f, bore * 0.85f, 0.18f, 8, M.T(muzzle.x, muzzle.y, muzzle.z - 0.1f) * M.Rx(Mathf.PI / 2)), Dark);
            else if (barrel == "short") muzzle.z -= 0.06f;

            // Muzzle
            string mz = F("muzzle", "none");
            if (mz == "suppressor") { Part("suppressor", muzzle); muzzle.z += 0.3f; }
            else if (mz == "comp" || mz == "brake" || mz == "choke")
            {
                Part("comp", muzzle, thick: mz == "brake" ? 1.3f : mz == "choke" ? 0.85f : 1);
                muzzle.z += 0.12f;
            }

            // Sight on top
            string sight = F("sight", "iron");
            if (sight == "scope") Part("scope", g.sight);
            else if (sight == "reddot")
            {
                Add(new GeoBuilder().Box(0.05f, 0.05f, 0.075f, g.sight.x, g.sight.y + 0.027f, g.sight.z), Dark);
                Glint(new GeoBuilder().Box(0.022f, 0.022f, 0.009f, g.sight.x, g.sight.y + 0.036f, g.sight.z - 0.04f), 0xff3030);
            }
            else if (sight == "holo")
                Add(new GeoBuilder().Box(0.075f, 0.068f, 0.027f, g.sight.x, g.sight.y + 0.045f, g.sight.z + 0.015f)
                    .Box(0.075f, 0.018f, 0.09f, g.sight.x, g.sight.y + 0.009f, g.sight.z), Dark);

            // Under the handguard
            string rail = F("rail", "none");
            if (rail == "foregrip") Part("foregrip", g.rail);
            else if (rail == "angled") Part("foregrip", g.rail, tilt: -35, stretch: 0.8f);
            else if (rail == "light" || rail == "laser")
            {
                Part("light", g.rail);
                Glint(new GeoBuilder().Box(0.03f, 0.03f, 0.009f, g.rail.x, g.rail.y - 0.04f, g.rail.z + 0.09f), rail == "laser" ? 0xff2a2a : 0xfff6d8);
            }

            // Stock, behind the grip
            // (A little into the gun, so it reads as fitted, not floating.)
            string stock = F("stock");
            var butt = g.stock + new Vector3(0, 0, 0.1f);
            if (stock == "light") Part("stock", butt, thick: 0.7f);
            else if (stock == "heavy") Part("stock", butt, thick: 1.3f);
            else if (stock == "precision") Part("stock", butt, stretch: 1.25f);

            // Magazine: longer, a drum, or a bright pull tab.
            string mag = F("mag", "factory");
            var well = g.mag;
            if (mag == "extended") Add(new GeoBuilder().Box(bore * 1.2f, 0.14f, 0.075f, well.x, well.y - 0.06f, well.z), Dark);
            else if (mag == "drum") Add(new GeoBuilder().Cylinder(0.1f, 0.1f, bore * 1.6f, 14, M.T(well.x, well.y - 0.08f, well.z) * M.Rz(Mathf.PI / 2)), Dark);
            else if (mag == "quick") Add(new GeoBuilder().Box(bore * 1.3f, 0.03f, 0.09f, well.x, well.y - 0.008f, well.z), 0xe0b020, 0.35f);

            // Its outline with the parts on, roughly, for framing pictures.
            var b = g.bounds;
            b.Encapsulate(muzzle);
            if (stock == "light" || stock == "heavy" || stock == "precision") b.Encapsulate(butt - new Vector3(0, 0.12f, 0.45f));
            if (mag != "factory") b.Encapsulate(well - new Vector3(0, 0.18f, 0));
            if (sight == "scope") b.Encapsulate(g.sight + new Vector3(0, 0.1f, 0));
            if (rail != "none") b.Encapsulate(g.rail - new Vector3(0, 0.17f, 0));
            gun.bounds = b;

            t.localScale = Vector3.one * (Length[model] / g.length);
            gun.muzzle = muzzle;
            return gun;
        }

        static void Show(Transform parent, ArtCatalog.Placed p, ArtCatalog cat)
        {
            var go = new GameObject(p.id);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = p.position;
            t.localRotation = p.rotation;
            t.localScale = p.scale;
            go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mats = new Material[p.mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = p.colors == null || p.colors.Length == 0
                    ? Art.Lit(Hex.C(0xe7e7e7), 0, cat.zombieAtlas)
                    // Lifted, so dark metal still shows at night.
                    : Art.Lit(Color.Lerp(p.colors[Mathf.Min(i, p.colors.Length - 1)], Hex.C(0x8c929c), 0.35f), 0.15f);
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
