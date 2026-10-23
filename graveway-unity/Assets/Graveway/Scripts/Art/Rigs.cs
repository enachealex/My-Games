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
        public string hat;                   // survivors: the hat they're wearing, by id; "" for none
        public GameObject hatObject;
        public SkinnedMeshRenderer skin;     // survivors: their body, and its mesh as it comes (own hat and all)
        public Mesh skinMesh;
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
        public static readonly IReadOnlyDictionary<string, string> SurvivorModel = new Dictionary<string, string>
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
            rig.skin = rig.model.GetComponentInChildren<SkinnedMeshRenderer>();
            rig.skinMesh = rig.skin ? rig.skin.sharedMesh : null;
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

        // A hat on a survivor's head, going where the head goes; none for ""
        // (or for a hat the catalog hasn't got). A survivor's own hat is part
        // of their model: wearing it is wearing the model as it comes, and
        // anything else (or nothing) takes it off.
        public static void WearHat(Rig rig, string hat)
        {
            hat ??= "";
            if (rig.hat == hat) return;
            if (rig.hatObject) Object.Destroy(rig.hatObject);
            rig.hat = hat;
            rig.hatObject = null;
            var cat = ArtCatalog.Get;
            var own = cat.OwnHatOf(rig.model.name);
            if (own != null && rig.skin) rig.skin.sharedMesh = hat == own.id ? rig.skinMesh : own.bare;
            if (own != null && hat == own.id) return;
            var h = hat.Length > 0 ? cat.FindHat(hat) : null;
            var place = h == null ? null : System.Array.Find(h.on, p => p.id == rig.model.name);
            var head = place == null ? null : Find(rig.model, "Head");
            if (!head) return;
            var go = HatModel(h);
            var t = go.transform;
            t.SetParent(head, false);
            t.localPosition = place.position;
            t.localRotation = place.rotation;
            t.localScale = place.scale;
            rig.hatObject = go;
        }

        // A hat on its own, as it sat on the top of the head it came off.
        public static GameObject HatModel(ArtCatalog.Hat h)
        {
            var go = new GameObject("hat " + h.id);
            go.AddComponent<MeshFilter>().sharedMesh = h.mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mats = new Material[h.mesh.subMeshCount];
            var cat = ArtCatalog.Get;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = !string.IsNullOrEmpty(h.model) ? Art.Lit(Hex.C(0xe7e7e7), 0, cat.zombieAtlas) : Art.Lit(h.colors[Mathf.Min(i, h.colors.Length - 1)]);
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        public static Rig Zombie(string kind)
        {
            string id = ZombieModel[kind];
            var rig = Make(id, kind, 1);
            Eyes(rig, id, Tunables.Zombies[kind].eyes);
            Art.BlobShadow(rig.root.transform, 1.1f);
            return rig;
        }

        // Glowing eyes, as the web version's zombies have: flat colour that
        // the dark and the fog don't touch, on the eyelids (or either side of
        // the head) where the face is, found once per model from its idle
        // pose and kept in each eye's bone's own space.
        static readonly Dictionary<string, Vector3[]> eyeSpots = new Dictionary<string, Vector3[]>();
        static Mesh eyeMesh;

        static void Eyes(Rig rig, string id, Color color)
        {
            Transform l = Find(rig.model, "Eyelid.L"), r = Find(rig.model, "Eyelid.R"), head = Find(rig.model, "Head");
            var bones = l && r ? new[] { l, r } : head ? new[] { head, head } : null;
            if (bones == null) return;                                      // the runner has no head
            rig.Snap("Idle");
            if (!eyeSpots.TryGetValue(id, out var spots)) eyeSpots[id] = spots = FindEyes(rig, bones, l == null);
            eyeMesh ??= new GeoBuilder().Box(1, 1, 1).Build("eye");
            var mat = Art.Eyes(color);
            for (int i = 0; i < 2; i++)
            {
                var eye = new GameObject("eye").transform;
                eye.SetParent(bones[i], false);
                eye.localPosition = spots[i];
                eye.rotation = rig.root.transform.rotation;
                var s = bones[i].lossyScale;
                eye.localScale = new Vector3(0.075f / s.x, 0.045f / s.y, 0.025f / s.z);   // metres, at walker size
                eye.gameObject.AddComponent<MeshFilter>().sharedMesh = eyeMesh;
                var mr = eye.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        // The front of the face in front of each eye bone: of the posed
        // mesh's vertices around it (or either side of the head), the one
        // furthest forward.
        static Vector3[] FindEyes(Rig rig, Transform[] bones, bool fromHead)
        {
            SkinnedMeshRenderer body = null;
            foreach (var smr in rig.model.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (body == null || smr.sharedMesh.vertexCount > body.sharedMesh.vertexCount) body = smr;
            var baked = new Mesh();
            body.BakeMesh(baked, false);
            var toWorld = body.transform.localToWorldMatrix;
            var verts = baked.vertices;
            Object.Destroy(baked);
            // (In metres: the rig stands Person tall here, before any kind's scale.)
            var spots = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                var b = bones[i].position;
                var at = fromHead ? b + new Vector3(i == 0 ? 0.08f : -0.08f, 0.1f, 0) : b;
                float best = float.NegativeInfinity;
                var front = at + Vector3.forward * 0.1f;
                foreach (var v in verts)
                {
                    var w = toWorld.MultiplyPoint3x4(v);
                    if (Mathf.Abs(w.x - at.x) > 0.04f || Mathf.Abs(w.y - at.y) > (fromHead ? 0.12f : 0.04f)) continue;
                    if (w.z > best) { best = w.z; front = w; }
                }
                spots[i] = bones[i].InverseTransformPoint(front + Vector3.forward * 0.01f);
            }
            return spots;
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
