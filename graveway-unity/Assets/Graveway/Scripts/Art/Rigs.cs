// The survivors and zombies: Quaternius's Zombie Apocalypse Kit (CC0),
// animated by their own clips. A Rig is one of them in the world; the game
// places and turns its root, picks what it's doing with Play, and flashes it
// when it's hit.
//
// The models face +z, like everything else here. Sizes come from
// Logs/art-report.txt (Editor/ArtImport.cs): each is scaled so it stands
// Person tall, before a zombie kind's own scale.

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public sealed class Rig
    {
        public GameObject root;              // placed and turned by the game
        public Transform model;              // the character, at its size
        public Animator animator;
        public Transform gunMount;           // survivors: grip at the origin, barrel along +z, in metres
        public readonly Dictionary<string, GunModel> guns = new Dictionary<string, GunModel>();
        readonly HashSet<int> states = new HashSet<int>();
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<Material> mats = new List<Material>();
        int current;
        bool flashing;

        public void Keep(Renderer r) { renderers.Add(r); mats.Add(r.sharedMaterial); }

        public bool Has(string state) => states.Contains(Animator.StringToHash(state));

        // Blends into a state, unless it's already playing. Hidden (by the
        // fog of war), it carries on with what it was doing.
        public void Play(string state, float fade = 0.15f, float speed = 1)
        {
            if (!animator.isActiveAndEnabled) return;
            animator.speed = speed;
            int h = Animator.StringToHash(state);
            if (h == current) return;
            current = h;
            animator.CrossFadeInFixedTime(h, fade);
        }

        // From the start again, as for another punch.
        public void Restart(string state, float fade = 0.08f, float speed = 1)
        {
            if (!animator.isActiveAndEnabled) return;
            animator.speed = speed;
            current = Animator.StringToHash(state);
            animator.CrossFadeInFixedTime(current, fade, 0, 0);
        }

        // Straight into a state, some way through it, with no blend: a pooled
        // zombie coming back, or a pose to photograph.
        public void Snap(string state, float at = 0)
        {
            animator.speed = 1;
            current = Animator.StringToHash(state);
            animator.Play(current, 0, at);
            animator.Update(0);
        }

        public void SetFlash(bool on)
        {
            if (on == flashing) return;
            flashing = on;
            for (int i = 0; i < renderers.Count; i++) renderers[i].sharedMaterial = on ? Art.Flash : mats[i];
        }

        internal void Learn(RuntimeAnimatorController c)
        {
            foreach (var clip in c.animationClips) states.Add(Animator.StringToHash(Rigs.Short(clip.name)));
        }
    }

    public static class Rigs
    {
        public const float Person = 1.75f;   // how tall a survivor or a walker stands, in metres

        // Who plays whom.
        static readonly Dictionary<string, string> SurvivorModel = new Dictionary<string, string>
        {
            { "rook", "Characters_Shaun" }, { "bram", "Characters_Matt" }, { "juno", "Characters_Sam" }, { "wren", "Characters_Lis" },
        };
        static readonly Dictionary<string, string> ZombieModel = new Dictionary<string, string>
        {
            { "walker", "Zombie_Basic" }, { "runner", "Zombie_Ribcage" }, { "tank", "Zombie_Chubby" }, { "boss", "Zombie_Arm" },
        };

        // Each model's height standing idle, in its own units (Lis's hair
        // doesn't count).
        static readonly Dictionary<string, float> Height = new Dictionary<string, float>
        {
            { "Characters_Lis", 1.38f }, { "Characters_Matt", 1.36f }, { "Characters_Sam", 1.38f }, { "Characters_Shaun", 1.38f },
            { "Zombie_Basic", 1.18f }, { "Zombie_Ribcage", 0.92f }, { "Zombie_Chubby", 1.5f }, { "Zombie_Arm", 1.42f },
        };

        public static string Short(string clip) => clip.Substring(clip.LastIndexOf('|') + 1);

        // How much a model is scaled up to stand Person tall.
        public static float SizeOf(string id) => Person / Height[id];

        // A survivor holding a gun, with a bright ring underfoot so you never
        // lose yourself in a crowd.
        public static Rig Survivor(CharacterDef c, bool ring = true)
        {
            var rig = Make(SurvivorModel[c.id], c.name, c.look != null ? c.look.bulk : 1);
            // The kit's characters come holding every weapon in the kit: put
            // them away, and mount ours where they were, as held in Idle_Gun.
            Transform hand = Find(rig.model, "Middle1.L");
            foreach (Transform w in hand) if (w.GetComponent<MeshRenderer>()) w.gameObject.SetActive(false);
            var pose = System.Array.Find(rig.animator.runtimeAnimatorController.animationClips, k => Short(k.name) == "Idle_Gun");
            pose.SampleAnimation(rig.model.gameObject, 0);
            rig.gunMount = new GameObject("gun mount").transform;
            rig.gunMount.SetParent(hand, false);
            rig.gunMount.SetPositionAndRotation(hand.position, rig.root.transform.rotation);
            var s = hand.lossyScale;
            rig.gunMount.localScale = new Vector3(1 / s.x, 1 / s.y, 1 / s.z);
            if (ring) Art.GlowDecal(rig.root.transform, 0x9be15d, 2.1f, 0.4f, Tex.Ring);
            Art.BlobShadow(rig.root.transform, 1.1f);
            rig.Snap("Idle_Gun");
            return rig;
        }

        public static Rig Zombie(string kind)
        {
            var rig = Make(ZombieModel[kind], kind, 1);
            Art.BlobShadow(rig.root.transform, 1.1f);
            return rig;
        }

        static Rig Make(string id, string name, float bulk)
        {
            var cat = ArtCatalog.Get;
            var e = cat.Find(id);
            var rig = new Rig { root = new GameObject(name) };
            var inst = Object.Instantiate(e.model, rig.root.transform, false);
            inst.name = id;
            rig.model = inst.transform;
            rig.model.localScale = Vector3.one * (SizeOf(id) * bulk);
            rig.animator = inst.GetComponent<Animator>();
            if (!rig.animator) rig.animator = inst.AddComponent<Animator>();
            rig.animator.runtimeAnimatorController = e.controller;
            rig.animator.applyRootMotion = false;
            rig.animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            rig.animator.keepAnimatorStateOnDisable = true;    // hidden by the fog, it carries on where it was
            rig.Learn(e.controller);
            var mat = Art.Lit(Hex.C(0xe7e7e7), 0, cat.zombieAtlas);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer || r.gameObject.activeSelf) rig.Keep(r);
            }
            return rig;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = Find(c, name);
                if (f) return f;
            }
            return null;
        }
    }
}
