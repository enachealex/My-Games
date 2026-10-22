// Puts the project's settings and generated assets in place: the app's name
// and Android settings, the base materials Art.cs copies, the UI's
// PanelSettings, the art catalog (ArtImport.cs), and the main scene (empty apart from its lighting and fog;
// the game builds itself in code). Everything it makes is committed, so this
// only needs running again after changing it.
//
// From the menu: Graveway → Set up project. Without opening the editor:
//   Unity -batchmode -quit -projectPath graveway-unity
//         -executeMethod Graveway.EditorTools.ProjectSetup.Run
//
// The render pipeline itself (Settings/Graveway_URP*.asset) and the graphics
// settings that point at it are plain committed files.

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Graveway.EditorTools
{
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Graveway/Scenes/Main.unity";
        const string MaterialsDir = "Assets/Graveway/Resources/Materials";
        const string PanelPath = "Assets/Graveway/Resources/UI/GravewayPanel.asset";
        const string ThemePath = "Assets/Graveway/Resources/UI/GravewayTheme.tss";
        static readonly Color Fog = new Color32(0x0d, 0x14, 0x18, 0xff);

        [MenuItem("Graveway/Set up project")]
        public static void Run()
        {
            Player();
            Branding();
            Materials();
            Panel();
            ArtImport.BuildCatalog();
            Scene();
            AssetDatabase.SaveAssets();
            Debug.Log("Graveway: project set up.");
        }

        static void Player()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.productName = "Graveway";
            PlayerSettings.companyName = "The Jump Vault";
            PlayerSettings.SetApplicationIdentifier(android, "com.thejumpvault.gravewayunity");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // Vulkan where the phone has it, OpenGL ES 3 where it doesn't.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.Android.optimizedFramePacing = true;
            // Full screen comes from the default "requested visible insets"
            // (none: no status or navigation bar), with the HUD kept clear of
            // notches by Hud.SafeArea.
            PlayerSettings.Android.renderOutsideSafeArea = true;
            // For the update check (Updates.cs).
            PlayerSettings.Android.forceInternetPermission = true;
        }

        // The web version's icon (the dead road at night) on the launcher, and
        // no Unity splash: just the game's dark background while it loads.
        const string IconPath = "Assets/Graveway/Branding/Icon.png";
        const string ClearPath = "Assets/Graveway/Branding/Clear.png";

        static void Branding()
        {
            if (!File.Exists(ClearPath))
            {
                var clear = new Texture2D(432, 432, TextureFormat.RGBA32, false);
                clear.SetPixels32(new Color32[432 * 432]);
                File.WriteAllBytes(ClearPath, clear.EncodeToPNG());
                Object.DestroyImmediate(clear);
                AssetDatabase.ImportAsset(ClearPath);
            }
            foreach (var path in new[] { IconPath, ClearPath })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti.textureCompression == TextureImporterCompression.Uncompressed && !ti.mipmapEnabled) continue;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var none = AssetDatabase.LoadAssetAtPath<Texture2D>(ClearPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
#if UNITY_ANDROID
            void Set(PlatformIconKind kind, params Texture2D[] layers)
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var i in icons) i.SetTextures(layers);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
            // Adaptive (all Unity 6 makes): the whole picture as the background
            // layer, nothing in front; the launcher crops it to its own shape.
            Set(UnityEditor.Android.AndroidPlatformIconKind.Adaptive, icon, none);
#endif
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.backgroundColor = Fog;
        }

        // The base materials Art.cs copies, one per shader setup. Their shader
        // keywords are set here, in assets, so a phone build keeps the
        // variants they need.
        static void Materials()
        {
            Directory.CreateDirectory(MaterialsDir);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            // Matte like three's Lambert: no highlights or reflections (which
            // is also cheaper on a phone).
            void Matte(Material m)
            {
                m.SetFloat("_Smoothness", 0.12f);
                m.SetFloat("_SpecularHighlights", 0);
                m.SetFloat("_EnvironmentReflections", 0);
                m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
            Make("Lit", lit, m =>
            {
                Matte(m);
                m.DisableKeyword("_EMISSION");
            });
            Make("LitGlow", lit, m =>
            {
                Matte(m);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
            Make("Unlit", unlit, m => { });
            // Additive like three's AdditiveBlending (src alpha, one): pools
            // of light, flashes, fire.
            Make("Glow", particles, m =>
            {
                Transparent(m, BlendMode.SrcAlpha, BlendMode.One, blend: 2);
                m.SetFloat("_Cull", (float)CullMode.Off);
            });
            // Many copies of a mesh, a colour each (Batches.cs), camera-facing
            // sprites (Vfx.cs), and the fog of war's shadows (Sight.cs).
            Make("Instanced", Shader.Find("Graveway/Instanced"), m => { });
            Make("Sprite", Shader.Find("Graveway/Sprite"), m => { });
            Make("SightShadow", Shader.Find("Graveway/SightShadow"), m => m.SetColor("_Color", new Color32(0x02, 0x04, 0x06, 178)));
            // See-through dark blobs under people and things.
            Make("Shadow", unlit, m =>
            {
                Transparent(m, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, blend: 0);
                m.renderQueue = (int)RenderQueue.Transparent - 10;    // under every other see-through thing
            });
        }

        static void Make(string name, Shader shader, System.Action<Material> setup)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            m.SetColor("_BaseColor", Color.white);
            m.enableInstancing = true;
            setup(m);
            EditorUtility.SetDirty(m);
        }

        // URP's transparent surface, set up the way its material inspector does.
        static void Transparent(Material m, BlendMode src, BlendMode dst, int blend)
        {
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", blend);
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)dst);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        // The UI's panel settings, scaled to a 390 x 844 phone like the web
        // version's CSS.
        static void Panel()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, PanelPath);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(390, 844);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
        }

        // The only scene: empty, with the night's fog and ambient light (the
        // game sets them too, but a build keeps fog shaders only for the fog
        // its scenes use).
        static void Scene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Fog;
            RenderSettings.fogStartDistance = 14;
            RenderSettings.fogEndDistance = 50;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.skybox = null;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
