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
        // its clips (Idle by default). Rebuilt in place, so the controllers
        // keep their GUIDs and the catalog doesn't churn.
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
                foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
                foreach (var clip in clips)
                {
                    var st = sm.AddState(Rigs.Short(clip.name));
                    st.motion = clip;
                    if (st.name == "Idle") sm.defaultState = st;
                }
                EditorUtility.SetDirty(ctrl);
                entries.Add(new ArtCatalog.Entry { id = id, model = model, controller = ctrl });
            }
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<ArtCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
            }
            cat.models = entries.ToArray();
            cat.zombieAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Kit + "Zombie_Atlas.png");
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
        }

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
