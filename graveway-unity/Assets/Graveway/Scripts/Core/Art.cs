// Materials for the placeholder art, and a few helpers for putting meshes
// into the scene (models.js's lambert / basic material caches, and its
// glow and shadow decals).
//
// Materials are copies of the base materials in Resources/Materials (made by
// Graveway → Set up project), never shaders looked up by name: a phone build
// only keeps the shaders, and shader variants, that some material asset uses.
// The same colour (and texture) always gets the same material, so the SRP
// Batcher can draw everything that shares it together.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Graveway
{
    public static class Art
    {
        // The base materials, one per shader setup (see ProjectSetup.cs).
        public const string BaseLit = "Lit", BaseLitGlow = "LitGlow", BaseUnlit = "Unlit", BaseGlow = "Glow", BaseFade = "Shadow",
            BaseInstanced = "Instanced", BaseSprite = "Sprite", BaseSightShadow = "SightShadow", BaseEyes = "Eyes";

        static readonly Dictionary<string, Material> bases = new Dictionary<string, Material>();
        static readonly Dictionary<(string, Color, float, Texture, Texture), Material> cache =
            new Dictionary<(string, Color, float, Texture, Texture), Material>();

        public static Material Base(string name)
        {
            if (bases.TryGetValue(name, out var m) && m != null) return m;
            m = Resources.Load<Material>("Materials/" + name);
            if (m == null) throw new InvalidOperationException($"Graveway: Resources/Materials/{name}.mat is missing. Run Graveway → Set up project.");
            bases[name] = m;
            return m;
        }

        static bool Cached(string kind, Color c, float f, Texture a, Texture b, out Material m) =>
            cache.TryGetValue((kind, c, f, a, b), out m) && m != null;

        static Material Make(string baseName, string kind, Color c, float f, Texture a, Texture b)
        {
            var m = new Material(Base(baseName)) { name = $"{kind} #{ColorUtility.ToHtmlStringRGB(c)}" };
            cache[(kind, c, f, a, b)] = m;
            return m;
        }

        // three's MeshLambertMaterial: matte, lit. glow > 0 adds that much of
        // the colour as emission (or a glow map, lit windows).
        public static Material Lit(Color c, float glow = 0, Texture tex = null, Texture glowTex = null)
        {
            if (Cached("lit", c, glow, tex, glowTex, out var m)) return m;
            bool glows = glow > 0 || glowTex;
            m = Make(glows ? BaseLitGlow : BaseLit, "lit", c, glow, tex, glowTex);
            m.SetColor("_BaseColor", c);
            if (tex) m.SetTexture("_BaseMap", tex);
            if (glows)
            {
                m.SetColor("_EmissionColor", glowTex ? Color.white * glow : c * glow);
                if (glowTex) m.SetTexture("_EmissionMap", glowTex);
            }
            return m;
        }

        public static Material Lit(int rgb, float glow = 0) => Lit(Hex.C(rgb), glow);

        // A lit material of its own, not shared, for things that change colour
        // on their own (a damaged wall darkening). Destroy it with its owner.
        public static Material LitOwn(Color c, Texture tex = null)
        {
            var m = new Material(Base(BaseLit)) { name = "own lit" };
            m.SetColor("_BaseColor", c);
            if (tex) m.SetTexture("_BaseMap", tex);
            return m;
        }

        // three's MeshBasicMaterial: solid colour, no lighting.
        public static Material Unlit(Color c, Texture tex = null)
        {
            if (Cached("unlit", c, 0, tex, null, out var m)) return m;
            m = Make(BaseUnlit, "unlit", c, 0, tex, null);
            m.SetColor("_BaseColor", c);
            if (tex) m.SetTexture("_BaseMap", tex);
            return m;
        }

        public static Material Unlit(int rgb) => Unlit(Hex.C(rgb));

        // Zombie eyes: flat colour, no light and no fog (Shaders/Eyes.shader).
        public static Material Eyes(Color c)
        {
            if (Cached("eyes", c, 0, null, null, out var m)) return m;
            m = Make(BaseEyes, "eyes", c, 0, null, null);
            m.SetColor("_BaseColor", c);
            return m;
        }

        // Additive glow, for pools of light, flashes and flames. `opacity`
        // dims it, as three's additive opacity does (the material blends by
        // source alpha).
        public static Material Glow(Color c, Texture tex = null, float opacity = 1)
        {
            tex = tex ? tex : Tex.Glow;
            if (Cached("glow", c, opacity, tex, null, out var m)) return m;
            m = Make(BaseGlow, "glow", c, opacity, tex, null);
            m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, opacity));
            m.SetTexture("_BaseMap", tex);
            return m;
        }

        // See-through, unlit and blended: blob shadows and scorch marks.
        public static Material Fade(Color c, float opacity, Texture tex = null)
        {
            tex = tex ? tex : Tex.Glow;
            if (Cached("fade", c, opacity, tex, null, out var m)) return m;
            m = Make(BaseFade, "fade", c, opacity, tex, null);
            m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, opacity));
            m.SetTexture("_BaseMap", tex);
            return m;
        }

        // Hit flash: everything a zombie or prop is made of swaps to this.
        static Material flash;
        public static Material Flash => flash != null ? flash : (flash = Unlit(Hex.C(0xffd8c8)));

        // For InstancedBatch: lit like Lambert (or not), solid or additive.
        public static Material Instanced(bool lit, bool additive = false, Color? emission = null, float alpha = 1)
        {
            var m = new Material(Base(BaseInstanced)) { name = "instanced", enableInstancing = true };
            m.SetFloat("_Lit", lit ? 1 : 0);
            m.SetFloat("_Additive", additive ? 1 : 0);
            m.SetFloat("_Alpha", alpha);
            m.SetColor("_Emission", emission ?? Color.black);
            if (additive)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
                m.SetFloat("_ZWrite", 0);
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            return m;
        }

        // For SpriteBatch: additive (fire, sparks) or blended (smoke, dust).
        public static Material Sprite(Texture tex, bool additive)
        {
            var m = new Material(Base(BaseSprite)) { name = additive ? "glow sprites" : "smoke sprites", enableInstancing = true };
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Additive", additive ? 1 : 0);
            m.SetFloat("_SrcBlend", (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.renderQueue = (int)RenderQueue.Transparent + (additive ? 3 : 2);
            return m;
        }

        // ── Putting meshes in the scene ──────────────────────────────────
        public static MeshRenderer Show(Transform parent, Mesh mesh, Material mat, string name = "mesh")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        static Mesh flat;
        // A 1 x 1 square lying flat (three's PlaneGeometry rotated flat).
        public static Mesh FlatMesh => flat != null ? flat : (flat = new GeoBuilder().Flat(1, 1, M.I).Build("flat"));

        // A soft dark blob on the ground: the only shadows in the game.
        public static MeshRenderer BlobShadow(Transform parent, float size = 1.1f, float opacity = 0.55f)
        {
            var r = Show(parent, FlatMesh, Fade(Color.black, opacity), "shadow");
            r.transform.localScale = new Vector3(size, 1, size);
            r.transform.localPosition = new Vector3(0, 0.02f, 0);
            return r;
        }

        // A soft additive pool of light on the ground.
        public static MeshRenderer GlowDecal(Transform parent, Color c, float size, float opacity = 0.6f, Texture tex = null)
        {
            var r = Show(parent, FlatMesh, Glow(c, tex, opacity), "glow");
            r.transform.localScale = new Vector3(size, 1, size);
            r.transform.localPosition = new Vector3(0, 0.03f, 0);
            return r;
        }

        public static MeshRenderer GlowDecal(Transform parent, int rgb, float size, float opacity = 0.6f, Texture tex = null) =>
            GlowDecal(parent, Hex.C(rgb), size, opacity, tex);

        public static void DestroyAll(List<Object> owned)
        {
            foreach (var o in owned) if (o != null) Object.Destroy(o);
            owned.Clear();
        }
    }
}
