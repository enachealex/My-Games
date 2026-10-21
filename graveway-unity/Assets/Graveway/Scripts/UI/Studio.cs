// Pictures for the menus, rendered from the 3D models: each survivor's
// portrait (game.js renderPortraits) and each gun with its parts fitted
// (preview.js). A little set of its own, far below the street, with its own
// camera and lights; everything else's lights are switched off while it
// shoots. Pictures are cached until the parts change.
//
// The camera sits on the opposite side to the web version's, which turns
// Unity's mirror image back round (see Tunables.cs): muzzles point right.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Graveway
{
    public static class Studio
    {
        static readonly Vector3 Origin = new Vector3(0, -2000, 0);
        static readonly Dictionary<string, Texture2D> guns = new Dictionary<string, Texture2D>();

        public static bool Available => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        // Each survivor holding their own gun with its parts, three-quarters on.
        public static Dictionary<string, Texture2D> Portraits(int size = 256)
        {
            var output = new Dictionary<string, Texture2D>();
            if (!Available) return output;
            Shoot(size, size, (set, cam) =>
            {
                cam.fieldOfView = 30;
                cam.transform.localPosition = new Vector3(-1.1f, 1.45f, 3.2f);
                cam.transform.LookAt(set.position + new Vector3(0, 1.15f, 0));
                Key(set, Hex.C(0xfff0dd), 2.2f, new Vector3(-2, 3, 4));
                Key(set, Hex.C(0x9be15d), 1.6f, new Vector3(3, 2, -3));
                Ambient(Hex.C(0xc8d8ff), Hex.C(0x3a2a20), 1.6f);
                foreach (var c in Tunables.Characters)
                {
                    var m = Rigs.Survivor(c, ring: false);
                    var w = Tunables.Weapon(c.startingWeapon);
                    var gun = Models.Gun(w.model, Save.FittedParts(w.id));
                    gun.group.transform.SetParent(m.gunMount, false);
                    gun.group.transform.localScale *= 0.7f;       // full size hides the chest face-on
                    m.root.transform.SetParent(set, false);
                    m.root.transform.localRotation = M.Yaw(-0.7f);  // three-quarters, so the gun shows side-on
                    output[c.id] = Capture(cam, size, size, "portrait " + c.id);
                    // Hidden now: Destroy only happens at the end of the frame,
                    // and the next survivor is photographed before then.
                    m.root.SetActive(false);
                    Object.Destroy(m.root);
                    Art.DestroyAll(gun.own);
                }
            });
            return output;
        }

        // A gun side-on with its parts, muzzle to the right. Cached.
        public static Texture2D GunPicture(string model, IDictionary<string, string> fitted)
        {
            if (!Available) return null;
            var keys = new List<string>(fitted.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            string key = model + ":" + string.Join(",", keys.ConvertAll(k => k + "=" + fitted[k]));
            if (guns.TryGetValue(key, out var tex) && tex) return tex;
            const int W = 480, H = 200;
            Shoot(W, H, (set, cam) =>
            {
                Key(set, Hex.C(0xfff2e0), 2.4f, new Vector3(3, 4, 2));
                Key(set, Hex.C(0x9be15d), 1.2f, new Vector3(-2, 1, -3));
                Ambient(Hex.C(0xd8e4ff), Hex.C(0x30281e), 1.7f);
                var gun = Models.Gun(model, fitted);
                gun.group.transform.SetParent(set, false);
                gun.group.transform.localPosition = Vector3.zero;
                var b = Bounds(gun.group);
                var c = b.center;
                float half = Mathf.Max(b.size.z * 0.56f, b.size.y * 0.56f * W / H);
                cam.orthographic = true;
                cam.orthographicSize = half * H / W;
                cam.transform.position = c + new Vector3(6, 1.2f, -0.6f);
                cam.transform.LookAt(c);
                tex = Capture(cam, W, H, "gun " + key);
                gun.group.SetActive(false);
                Object.Destroy(gun.group);
                Art.DestroyAll(gun.own);
            });
            guns[key] = tex;
            return tex;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.one);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void Key(Transform set, Color c, float threeIntensity, Vector3 from)
        {
            var l = new GameObject("key light").AddComponent<Light>();
            l.transform.SetParent(set, false);
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = threeIntensity / Mathf.PI;       // three.js lights, converted as in Game.Awake
            l.shadows = LightShadows.None;
            l.transform.rotation = Quaternion.LookRotation(-from);
        }

        static void Ambient(Color sky, Color ground, float threeIntensity)
        {
            float k = Mathf.Sqrt(threeIntensity / Mathf.PI) * 0.9f;   // in gamma, roughly three's hemisphere light
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky * k;
            RenderSettings.ambientEquatorColor = Color.Lerp(sky, ground, 0.5f) * k;
            RenderSettings.ambientGroundColor = ground * k;
        }

        // Sets up the set and camera, lets `shots` take pictures, and puts
        // every light, the fog and the ambient light back as they were.
        static void Shoot(int w, int h, System.Action<Transform, Camera> shots)
        {
            var lights = Object.FindObjectsByType<Light>();
            var wasOn = new bool[lights.Length];
            for (int i = 0; i < lights.Length; i++) { wasOn[i] = lights[i].enabled; lights[i].enabled = false; }
            bool fog = RenderSettings.fog;
            var mode = RenderSettings.ambientMode;
            Color sky = RenderSettings.ambientSkyColor, eq = RenderSettings.ambientEquatorColor, gr = RenderSettings.ambientGroundColor;
            RenderSettings.fog = false;

            var set = new GameObject("studio").transform;
            set.position = Origin;
            var cam = new GameObject("studio camera").AddComponent<Camera>();
            cam.transform.SetParent(set, false);
            cam.enabled = false;                          // renders only when asked
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex.C(0x18221b);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            try { shots(set, cam); }
            finally
            {
                Object.Destroy(set.gameObject);
                RenderSettings.fog = fog;
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = eq; RenderSettings.ambientGroundColor = gr;
                for (int i = 0; i < lights.Length; i++) if (lights[i]) lights[i].enabled = wasOn[i];
            }
        }

        static Texture2D Capture(Camera cam, int w, int h, string name)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 1;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false) { name = name };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false, true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        // Parts changed: those pictures are stale.
        public static void Forget()
        {
            foreach (var t in guns.Values) if (t) Object.Destroy(t);
            guns.Clear();
        }
    }
}
