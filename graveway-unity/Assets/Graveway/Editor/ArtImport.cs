// How the third-party art under Assets/ThirdParty/ is imported: characters
// keep their own (Generic) rigs and animations, with the walking, running
// and idling clips set to loop; everything else comes in as plain meshes.
//
// Report writes what was imported (clips, bones, sizes, triangles,
// materials) to Logs/art-report.txt, which is how the numbers in
// Scripts/Art/Rigs.cs were chosen:
//   Unity -batchmode -nographics -quit -projectPath graveway-unity
//         -executeMethod Graveway.EditorTools.ArtImport.Report

using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Graveway.EditorTools
{
    public sealed class ArtImport : AssetPostprocessor
    {
        const string Root = "Assets/ThirdParty/";
        static readonly Regex Rigged = new Regex(@"ZombieApocalypseKit/Characters/|UltimateModular(Men|Women)/|UniversalAnimationLibrary/", RegexOptions.IgnoreCase);
        static readonly Regex Looping = new Regex(@"idle|walk|run|jog|sprint|crawl|strafe|swim|dance|_loop", RegexOptions.IgnoreCase);

        static bool Ours(string path) => path.StartsWith(Root) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!Ours(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            bool rigged = Rigged.IsMatch(assetPath);
            mi.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            mi.importAnimation = rigged;
            mi.avatarSetup = rigged ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.NoAvatar;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
        }

        void OnPreprocessAnimation()
        {
            if (!Ours(assetPath) || !Rigged.IsMatch(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips) c.loopTime = Looping.IsMatch(c.name) && !Regex.IsMatch(c.name, "death|hit|start|end", RegexOptions.IgnoreCase);
            mi.clipAnimations = clips;
        }

        // The kit's palette: a few flat colours, kept exact.
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root) || !assetPath.EndsWith("Zombie_Atlas.png")) return;
            var ti = (TextureImporter)assetImporter;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
        }

        // ── The catalog the game loads (Resources/Art/Catalog.asset) ─────
        const string CatalogPath = "Assets/Graveway/Resources/Art/Catalog.asset";
        const string ControllersDir = "Assets/Graveway/Art/Controllers";
        const string Kit = "Assets/ThirdParty/Quaternius/ZombieApocalypseKit/";
        static readonly string[] Characters =
        {
            "Characters_Lis", "Characters_Matt", "Characters_Sam", "Characters_Shaun",
            "Zombie_Basic", "Zombie_Ribcage", "Zombie_Chubby", "Zombie_Arm",
        };

        // Each character, and an Animator controller with a state for each of
        // its clips (Idle by default). Rebuilt in place, and only when its
        // clips change, so the files don't churn.
        public static void BuildCatalog()
        {
            Directory.CreateDirectory(ControllersDir);
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
            // Imported before OnPreprocessTexture had a say: once more, with it.
            if (AssetImporter.GetAtPath(Kit + "Zombie_Atlas.png") is TextureImporter ti && ti.filterMode != FilterMode.Point) ti.SaveAndReimport();
            var entries = new System.Collections.Generic.List<ArtCatalog.Entry>();
            foreach (var id in Characters)
            {
                string path = $"{Kit}Characters/{id}.fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) throw new System.InvalidOperationException($"Graveway: {path} is missing.");
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(c => c.name.StartsWith("CharacterArmature|")).OrderBy(c => c.name).ToList();
                string cpath = $"{ControllersDir}/{id}.controller";
                var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(cpath)
                           ?? UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(cpath);
                var sm = ctrl.layers[0].stateMachine;
                bool same = sm.states.Length == clips.Count
                            && clips.All(c => sm.states.Any(s => s.state.name == Rigs.Short(c.name) && s.state.motion == c));
                if (!same)
                {
                    foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
                    foreach (var clip in clips)
                    {
                        var st = sm.AddState(Rigs.Short(clip.name));
                        st.motion = clip;
                        if (st.name == "Idle") sm.defaultState = st;
                    }
                    EditorUtility.SetDirty(ctrl);
                }
                entries.Add(new ArtCatalog.Entry { id = id, model = model, controller = ctrl });
            }
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<ArtCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
            }
            cat.models = entries.ToArray();
            cat.props = BuildProps();
            cat.guns = BuildGuns();
            cat.parts = BuildParts();
            cat.zombieAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Kit + "Zombie_Atlas.png");
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
        }

        // The kit's static models the street uses: each one's outline in its
        // own space, and the middle of its light (a lamp's glass, a car's
        // headlights) for what glows there.
        static readonly string[] KitProps =
        {
            "Vehicles/Vehicle_Sports", "Vehicles/Vehicle_Pickup",
            "Environment/PlasticBarrier", "Environment/StreetLights", "Environment/Barrel",
            "Environment/TrashBag_1", "Environment/TrashBag_2", "Environment/Wheel", "Environment/Pallet_Broken",
            "Environment/TrafficCone_1", "Environment/CinderBlock",
        };

        static ArtCatalog.Prop[] BuildProps()
        {
            var props = new System.Collections.Generic.List<ArtCatalog.Prop>();
            foreach (var name in KitProps)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}{name}.fbx");
                if (model == null) throw new System.InvalidOperationException($"Graveway: {Kit}{name}.fbx is missing.");
                var inst = Object.Instantiate(model);
                var b = new Bounds();
                bool first = true;
                Vector3 light = Vector3.zero;
                int lights = 0;
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                {
                    var m = mf.transform.localToWorldMatrix;
                    var verts = mf.sharedMesh.vertices;
                    foreach (var v in verts)
                    {
                        var w = m.MultiplyPoint3x4(v);
                        if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
                    }
                    var mats = mf.GetComponent<Renderer>().sharedMaterials;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount && s < mats.Length; s++)
                        if (mats[s] && (mats[s].name == "Light" || mats[s].name == "Headlights"))
                            foreach (int i in mf.sharedMesh.GetIndices(s)) { light += m.MultiplyPoint3x4(verts[i]); lights++; }
                }
                props.Add(new ArtCatalog.Prop { id = Path.GetFileName(name), model = model, bounds = b, light = lights > 0 ? light / lights : b.center });
                Object.DestroyImmediate(inst);
            }
            return props.ToArray();
        }

        // The kit's guns as a survivor holds them: Matt (any of the four would
        // do: they share a skeleton and hold the same way) in Idle_Gun, at his
        // in-game size, measured from the gun mount Rigs.Survivor makes
        // (grip in the hand, barrel along +z, metres). They're drawn big for
        // the kit's cartoon style; Guns.Build sizes them for this game.
        static readonly string[] KitGuns = { "Pistol", "SMG", "Shotgun", "Rifle" };

        static ArtCatalog.Gun[] BuildGuns()
        {
            const string who = "Characters_Matt";
            string path = $"{Kit}Characters/{who}.fbx";
            var inst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            inst.transform.localScale = Vector3.one * Rigs.SizeOf(who);
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => Rigs.Short(c.name) == "Idle_Gun").SampleAnimation(inst, 0);
            var hand = inst.GetComponentsInChildren<Transform>().First(t => t.name == "Middle1.L");
            var toMount = Matrix4x4.Translate(-hand.position);
            var guns = new System.Collections.Generic.List<ArtCatalog.Gun>();
            foreach (var name in KitGuns)
            {
                var w = hand.Find(name);
                var mesh = w.GetComponent<MeshFilter>().sharedMesh;
                var m = toMount * w.localToWorldMatrix;
                var g = new ArtCatalog.Gun { body = Placed(name, mesh, null, m) };
                Sockets(g, mesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray());
                guns.Add(g);
            }
            Object.DestroyImmediate(inst);
            return guns.ToArray();
        }

        // Where parts go, from the gun's own shape: the tip of the barrel, the
        // top of the receiver, under the handguard, the back end, and the
        // lowest point ahead of the grip (the magazine).
        static void Sockets(ArtCatalog.Gun g, Vector3[] p)
        {
            float z0 = p.Min(v => v.z), z1 = p.Max(v => v.z), len = z1 - z0;
            Vector3[] Slab(float a, float b) => p.Where(v => v.z >= z0 + a * len && v.z <= z0 + b * len).ToArray();
            var tip = Slab(0.96f, 1);
            g.muzzle = new Vector3(tip.Average(v => v.x), tip.Average(v => v.y), z1);
            // The top of the receiver, not the tallest spike on it (a rear sight).
            var mid = Slab(0.3f, 0.6f).OrderBy(v => v.y).ToArray();
            g.sight = new Vector3(mid.Average(v => v.x), mid[(int)(mid.Length * 0.9f)].y, z0 + 0.45f * len);
            // Far enough forward to miss a magazine.
            var front = Slab(0.72f, 0.88f);
            g.rail = new Vector3(front.Average(v => v.x), front.Min(v => v.y), z0 + 0.8f * len);
            var back = Slab(0, 0.05f);
            g.stock = new Vector3(back.Average(v => v.x), back.Average(v => v.y), z0);
            // The magazine, or a pistol's grip, which holds it.
            g.mag = Slab(0.1f, 0.65f).OrderBy(v => v.y).First();
            g.width = p.Max(v => v.x) - p.Min(v => v.x);
            g.length = len;
            g.bounds = new Bounds(p[0], Vector3.zero);
            foreach (var v in p) g.bounds.Encapsulate(v);
        }

        // Ultimate Guns accessories, each turned and sized so its origin is
        // where it attaches, +z is the way the barrel points, and +y is up:
        //   near / behind: lies along x and attaches at the end nearer its
        //         origin, pointing forward (suppressors) or back (the stock)
        //   bottom / top: lies along x, sits on (or hangs under) the rail,
        //         its bigger end forward (scope lens, torch head)
        //   hang: stands along y and hangs from its top (foregrip)
        static readonly (string id, string file, string how, float size)[] Accessories =
        {
            ("suppressor", "Silencer_1", "near", 0.3f),
            ("comp", "Silencer_Short", "near", 0.12f),
            ("scope", "Scope_1", "bottom", 0.4f),
            ("light", "Flashlight", "top", 0.14f),
            ("foregrip", "Grip", "hang", 0.17f),
            ("stock", "Stock", "behind", 0.36f),
        };

        static ArtCatalog.Placed[] BuildParts()
        {
            var parts = new System.Collections.Generic.List<ArtCatalog.Placed>();
            foreach (var (id, file, how, size) in Accessories)
            {
                string path = $"Assets/ThirdParty/Quaternius/UltimateGuns/Accessories/{file}.fbx";
                var inst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var mf = inst.GetComponentInChildren<MeshFilter>();
                var c = mf.transform.localToWorldMatrix;
                var p = mf.sharedMesh.vertices.Select(v => c.MultiplyPoint3x4(v)).ToArray();
                var b = new Bounds(p[0], Vector3.zero);
                foreach (var v in p) b.Encapsulate(v);
                // How big each end is across, to tell a lens from an eyepiece.
                float Across(float x0, float x1) { var s = p.Where(v => v.x >= x0 && v.x <= x1).ToArray(); return s.Length == 0 ? 0 : s.Max(v => v.y) - s.Min(v => v.y); }
                float end = b.size.x * 0.1f;
                bool bigAtMax = Across(b.max.x - end, b.max.x) > Across(b.min.x, b.min.x + end);
                bool nearMax = Mathf.Abs(b.max.x) < Mathf.Abs(b.min.x);
                Vector3 origin, along;
                float length;
                switch (how)
                {
                    case "near": case "behind":
                        origin = new Vector3(nearMax ? b.max.x : b.min.x, b.center.y, b.center.z);
                        along = nearMax ? Vector3.left : Vector3.right;           // away from where it attaches
                        length = b.size.x;
                        break;
                    case "bottom": case "top":
                        origin = new Vector3(b.center.x, how == "bottom" ? b.min.y : b.max.y, b.center.z);
                        along = bigAtMax ? Vector3.right : Vector3.left;          // towards the big end
                        length = b.size.x;
                        break;
                    default:                                                      // hang
                        origin = new Vector3(b.center.x, b.max.y, b.center.z);
                        along = Vector3.right;
                        length = b.size.y;
                        break;
                }
                var turn = Quaternion.FromToRotation(along, how == "behind" ? Vector3.back : Vector3.forward);
                var place = Matrix4x4.TRS(Vector3.zero, turn, Vector3.one * (size / length)) * Matrix4x4.Translate(-origin) * c;
                var colors = mf.GetComponent<Renderer>().sharedMaterials.Select(m => m && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.gray).ToArray();
                parts.Add(Placed(id, mf.sharedMesh, colors, place));
                Object.DestroyImmediate(inst);
            }
            return parts.ToArray();
        }

        static ArtCatalog.Placed Placed(string id, Mesh mesh, Color[] colors, Matrix4x4 m) => new ArtCatalog.Placed
        {
            id = id, mesh = mesh, colors = colors ?? new Color[0],
            position = m.GetPosition(), rotation = m.rotation, scale = m.lossyScale,
        };

        [MenuItem("Graveway/Art report")]
        public static void Report()
        {
            var sb = new StringBuilder();
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                sb.AppendLine($"== {path.Substring(Root.Length)}");
                var inst = Object.Instantiate(go);
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                bool any = false;
                int tris = 0;
                foreach (var r in inst.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                    var mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh != null) tris += mesh.triangles.Length / 3;
                }
                sb.AppendLine($"   size {F(bounds.size)} centre {F(bounds.center)} tris {tris} root scale {F(inst.transform.localScale)}");
                foreach (var r in inst.GetComponentsInChildren<Renderer>())
                {
                    var mats = string.Join(", ", r.sharedMaterials.Where(m => m).Select(m =>
                        $"{m.name}[{m.shader.name}{(m.HasProperty("_BaseColor") ? " " + ColorUtility.ToHtmlStringRGB(m.GetColor("_BaseColor")) : "")}{(m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? " tex " + m.GetTexture("_BaseMap").name : "")}]"));
                    sb.AppendLine($"   {(r is SkinnedMeshRenderer ? "skinned" : "mesh")} {Rel(r.transform, inst.transform)}: {mats}");
                }
                var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null)
                {
                    sb.AppendLine($"   bones {smr.bones.Length}, root {Rel(smr.rootBone, inst.transform)}");
                    sb.AppendLine("   " + string.Join(" ", smr.bones.Where(b => b).Select(b => b.name)));
                    foreach (var b in smr.bones.Where(b => b && Regex.IsMatch(b.name, "hand|head|wrist|palm|spine", RegexOptions.IgnoreCase)))
                        sb.AppendLine($"   bone {Rel(b, inst.transform)} at {F(b.position)}");
                }
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
                    sb.AppendLine($"   clip {clip.name} {clip.length:0.00}s{(clip.isLooping ? " loop" : "")}");
                Object.DestroyImmediate(inst);
            }
            Poses(sb);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/art-report.txt", sb.ToString());
            Debug.Log("Graveway: wrote Logs/art-report.txt");
        }

        // The characters standing in their idle pose: how tall, where the
        // head and hands are, which way they face (the knee pole targets sit
        // in front of the knees), and where the weapons they come with sit.
        static void Poses(StringBuilder sb)
        {
            sb.AppendLine().AppendLine("==== Poses");
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/Quaternius/ZombieApocalypseKit/Characters" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
                foreach (var want in new[] { "Idle", "Idle_Gun", "Walk", "Run" })
                {
                    var clip = clips.FirstOrDefault(c => c.name.EndsWith("|" + want) || c.name == want);
                    if (clip == null) continue;
                    var inst = Object.Instantiate(go);
                    clip.SampleAnimation(inst, 0);
                    // Baked, because a skinned renderer's bounds don't follow a sampled pose.
                    var b = new Bounds();
                    bool first = true;
                    foreach (var s in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var baked = new Mesh();
                        s.BakeMesh(baked, true);
                        var m = s.transform.localToWorldMatrix;
                        foreach (var v in baked.vertices)
                        {
                            var w = m.MultiplyPoint3x4(v);
                            if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
                        }
                        Object.DestroyImmediate(baked);
                    }
                    Transform Bone(string n) => inst.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == n);
                    string P(string n) { var t = Bone(n); return t ? F(t.position) : "-"; }
                    sb.AppendLine($"{Path.GetFileNameWithoutExtension(path)} {want}: feet {b.min.y:0.00} top {b.max.y:0.00} size {F(b.size)} centre {F(b.center)}");
                    sb.AppendLine($"   head {P("Head")} handL {P("Middle1.L")} handR {P("Middle1.R")} lowerArmL {P("LowerArm.L")} lowerArmR {P("LowerArm.R")} poleL {P("PoleTarget.L")} hips {P("Hips")}");
                    if (want == "Idle_Gun")
                    {
                        var hand = Bone("Middle1.L");
                        if (hand)
                            foreach (Transform w in hand)
                            {
                                var r = w.GetComponent<Renderer>();
                                sb.AppendLine($"   weapon {w.name}: local {F(w.localPosition)} rot {F(w.localEulerAngles)} scale {F(w.localScale)} world size {(r ? F(r.bounds.size) : "-")} world centre {(r ? F(r.bounds.center) : "-")} fwd {F(w.forward)} right {F(w.right)} up {F(w.up)}");
                            }
                    }
                    Object.DestroyImmediate(inst);
                }
            }
        }

        static string F(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";

        static string Rel(Transform t, Transform root)
        {
            if (t == null) return "-";
            var parts = new System.Collections.Generic.List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }
    }
}
