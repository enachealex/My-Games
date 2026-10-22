// Props and litter from Quaternius's Zombie Apocalypse Kit (CC0): static
// models sharing the kit's palette, placed at whatever size the game gives
// them, standing on the ground and centred on their parent's origin.

using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Graveway
{
    public static class Kit
    {
        public static bool Has(string id) => ArtCatalog.Get != null && ArtCatalog.Get.FindProp(id) != null;

        public static ArtCatalog.Prop Find(string id) => ArtCatalog.Get.FindProp(id);

        // The kit's palette: what most of every model is painted with.
        public static Material Palette => Art.Lit(Hex.C(0xe7e7e7), 0, ArtCatalog.Get.zombieAtlas);

        // A copy of the model under parent, scaled by `scale`. Its materials
        // are the palette, or what `special` gives for one by name
        // ("Headlights", "BrakeLight", "Light"); null keeps the palette.
        public static GameObject Place(string id, Transform parent, float scale, Func<string, Material> special = null)
        {
            var e = Find(id);
            var holder = new GameObject(id).transform;
            holder.SetParent(parent, false);
            holder.localScale = Vector3.one * scale;
            var inst = Object.Instantiate(e.model, holder, false);
            inst.transform.localPosition = new Vector3(-e.bounds.center.x, -e.bounds.min.y, -e.bounds.center.z);
            var palette = Palette;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = (mats[i] ? special?.Invoke(mats[i].name) : null) ?? palette;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return holder.gameObject;
        }

        // Where a point of the model ends up under its parent, at this scale.
        public static Vector3 At(string id, Vector3 p, float scale)
        {
            var b = Find(id).bounds;
            return new Vector3(p.x - b.center.x, p.y - b.min.y, p.z - b.center.z) * scale;
        }
    }
}
