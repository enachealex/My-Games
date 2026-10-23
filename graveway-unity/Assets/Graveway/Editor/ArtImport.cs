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

        const string HeadBone = "CharacterArmature/Root/Body/Hips/Abdomen/Torso/Neck/Head";
        static readonly string[] EyeBones = { HeadBone, HeadBone + "/Eyelid.L", HeadBone + "/Eyelid.R" };

        static bool Ours(string path) => path.StartsWith(Root) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

        // Bumped when the settings below change, so Unity imports again.
        public override uint GetVersion() => 3;

        void OnPreprocessModel()
        {
            if (!Ours(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            bool rigged = Rigged.IsMatch(assetPath);
            mi.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            mi.importAnimation = rigged;
            mi.avatarSetup = rigged ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.NoAvatar;
            // Zombies carry nothing, so their bones needn't be GameObjects:
            // with up to 64 about, that's a lot less for a phone to update.
            // (Survivors keep theirs: the gun hangs off a hand.)
            mi.optimizeGameObjects = assetPath.Contains("/Characters/Zombie_");
            // All but the bones the glowing eyes hang from (Rigs.cs): the
            // eyelids where there are some, else the head; the runner's a
            // skeleton with no head at all.
            // Readable, so Rigs.cs can find where their faces are on a phone.
            if (mi.optimizeGameObjects) mi.isReadable = true;
            if (mi.optimizeGameObjects)
                mi.extraExposedTransformPaths = assetPath.Contains("Zombie_Ribcage") ? new string[0]
                    : assetPath.Contains("Zombie_Chubby") ? new[] { HeadBone } : EyeBones;
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
            cat.hats = BuildHats();
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

        // Hats, lifted off the Ultimate Modular characters' heads: the parts
        // of a head in these materials (only those above the brow, for one
        // whose colour turns up lower down too), at this size. Their space
        // and riot helmets are whole heads that swallow a survivor's: not
        // used.
        static readonly (string id, string file, string[] mats, float size, bool aboveBrow)[] HatSources =
        {
            ("hardhat", "UltimateModularMen/Worker", new[] { "Worker_Yellow" }, 1, false),
            ("straw", "UltimateModularMen/Farmer", new[] { "Beige", "Red" }, 1, false),
            ("mohawk", "UltimateModularMen/Punk", new[] { "Red" }, 1, true),
            ("hood", "UltimateModularWomen/Medieval", new[] { "DarkBrown" }, 1, false),
            ("witch", "UltimateModularWomen/Witch", new[] { "Purple", "Gold" }, 0.8f, false),
            ("crown", "UltimateModularMen/King", new[] { "Gold" }, 1, false),
        };
        const string HatsDir = "Assets/Graveway/Resources/Art/Hats";

        // Every head in a pack is the same bald one underneath (where a hat
        // covers it, the skin's cut away, so it can't be measured off the
        // hat's own head): its top, and how wide it is.
        static readonly (Vector3 top, float width) MensHead = (new Vector3(0, 1.826f, 0.0565f), 0.214f);
        static readonly (Vector3 top, float width) WomensHead = (new Vector3(0, 1.789f, 0.0655f), 0.22f);

        // Each hat as it sat on its own head, moved to the top of that head;
        // then onto each survivor: the top of their head (the part of the
        // mesh their Head bone moves, hair and caps and all), as much bigger
        // as that head is wider, and into the Head bone's space by its bind
        // pose, so the hat goes where the head goes.
        static ArtCatalog.Hat[] BuildHats()
        {
            Directory.CreateDirectory(HatsDir);
            var heads = new System.Collections.Generic.List<(string id, Matrix4x4 toBone, Vector3 top, float width)>();
            foreach (var id in Rigs.SurvivorModel.Values)
            {
                var inst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}Characters/{id}.fbx"));
                var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh = smr.sharedMesh;
                int hb = System.Array.FindIndex(smr.bones, b => b.name == "Head");
                var toModel = smr.transform.localToWorldMatrix;
                var v = mesh.vertices;
                var w = mesh.boneWeights;
                var head = Enumerable.Range(0, v.Length).Where(i => w[i].boneIndex0 == hb && w[i].weight0 >= 0.5f)
                    .Select(i => toModel.MultiplyPoint3x4(v[i])).ToArray();
                var b = new Bounds(head[0], Vector3.zero);
                foreach (var p in head) b.Encapsulate(p);
                // The top of the skull (or the cap on it): the highest slice of
                // the head that's most of its width, so a crest of hair
                // doesn't hold a hat up.
                float slice = b.size.x * 0.04f, top = b.max.y;
                for (float y = b.max.y; y > b.center.y; y -= slice)
                {
                    var cut = head.Where(p => p.y <= y && p.y > y - slice).ToArray();
                    if (cut.Length > 0 && cut.Max(p => p.x) - cut.Min(p => p.x) >= b.size.x * 0.6f) { top = y; break; }
                }
                heads.Add((id, mesh.bindposes[hb] * toModel.inverse, new Vector3(b.center.x, top, b.center.z), b.size.x));
                Debug.Log($"Graveway: hat fit {id}: top {top:0.000} of {b.max.y:0.000}, width {b.size.x:0.000}");
                Object.DestroyImmediate(inst);
            }

            var hats = new System.Collections.Generic.List<ArtCatalog.Hat>();
            foreach (var (id, file, keep, size, aboveBrow) in HatSources)
            {
                var inst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ThirdParty/Quaternius/{file}.fbx"));
                var smr = inst.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name.EndsWith("_Head"));
                var src = smr.sharedMesh;
                var toModel = smr.transform.localToWorldMatrix;
                var v = src.vertices;
                var n = src.normals;
                var mats = smr.sharedMaterials;
                string Mat(int s) => s < mats.Length && mats[s] ? mats[s].name : "";
                var (top, width) = file.Contains("Women") ? WomensHead : MensHead;

                var verts = new System.Collections.Generic.List<Vector3>();
                var norms = new System.Collections.Generic.List<Vector3>();
                var subs = new System.Collections.Generic.List<int[]>();
                var colors = new System.Collections.Generic.List<Color>();
                for (int s = 0; s < src.subMeshCount; s++)
                {
                    if (keep != null && !keep.Contains(Mat(s))) continue;
                    var map = new System.Collections.Generic.Dictionary<int, int>();
                    var idx = src.GetIndices(s);
                    var tris = new System.Collections.Generic.List<int>();
                    for (int k = 0; k < idx.Length; k += 3)
                    {
                        var tri = new[] { idx[k], idx[k + 1], idx[k + 2] };
                        if (aboveBrow && tri.Average(i => toModel.MultiplyPoint3x4(v[i]).y) < top.y - 0.05f) continue;
                        foreach (int i in tri)
                        {
                            if (!map.TryGetValue(i, out int j))
                            {
                                map[i] = j = verts.Count;
                                verts.Add(toModel.MultiplyPoint3x4(v[i]) - top);
                                norms.Add(toModel.MultiplyVector(n[i]).normalized);
                            }
                            tris.Add(j);
                        }
                    }
                    subs.Add(tris.ToArray());
                    colors.Add(mats[s] && mats[s].HasProperty("_BaseColor") ? mats[s].GetColor("_BaseColor") : Color.gray);
                }
                var hat = new Mesh { name = "hat " + id };
                hat.SetVertices(verts);
                hat.SetNormals(norms);
                hat.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) hat.SetTriangles(subs[s], s);
                hat.RecalculateBounds();
                string path = $"{HatsDir}/{id}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (asset == null) { AssetDatabase.CreateAsset(hat, path); asset = hat; }
                else { EditorUtility.CopySerialized(hat, asset); asset.name = hat.name; }

                var on = heads.Select(h => Placed(h.id, asset, null, h.toBone * Matrix4x4.TRS(h.top, Quaternion.identity, Vector3.one * (size * h.width / width)))).ToArray();
                hats.Add(new ArtCatalog.Hat { id = id, mesh = asset, colors = colors.ToArray(), on = on });
                Object.DestroyImmediate(inst);
            }
            return hats.ToArray();
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
