// Driving and photographing the UI in play mode tests: find a visible
// button by what it says and press it, and take phone-shaped pictures of the
// street with the UI over it.

using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graveway.Tests
{
    public static class TestUi
    {
        public static VisualElement Root(Game game) => game.hud.GetComponentInChildren<UIDocument>().rootVisualElement;

        // Shown, as far as the HUD's own show/hide goes (it sets display inline).
        public static bool Visible(VisualElement v)
        {
            for (var e = v; e != null; e = e.hierarchy.parent)
                if (e.style.display == DisplayStyle.None) return false;
            return true;
        }

        // The element, or something in it, says exactly this.
        public static bool Says(VisualElement v, string text) => v.Query<TextElement>().Where(t => t.text == text).ToList().Count > 0;

        public static T Find<T>(VisualElement root, Func<T, bool> match, string what) where T : VisualElement
        {
            foreach (var e in root.Query<T>().ToList())
                if (Visible(e) && match(e)) return e;
            Assert.Fail($"nothing visible on screen matches: {what}");
            return null;
        }

        public static Button Button(VisualElement root, string text) => Find<Button>(root, b => Says(b, text), $"a \"{text}\" button");

        // What a tap does, without needing a pointer.
        public static void Press(Button b)
        {
            using var e = NavigationSubmitEvent.GetPooled();
            e.target = b;
            b.SendEvent(e);
        }

        public static void Click(VisualElement root, string text) => Press(Button(root, text));
    }

    // The camera and the UI both drawn into textures for as long as it's
    // open, so Shot can lay one over the other and save a PNG in
    // graveway-unity/Logs/shots/.
    public sealed class Photo : IDisposable
    {
        readonly Game game;
        readonly PanelSettings panel;
        readonly RenderTexture world, ui;
        readonly bool wasClearing;
        readonly Color wasClear;

        // A phone-shaped picture: 1080 x 2340 at half size.
        public Photo(Game game, int w = 540, int h = 1170)
        {
            this.game = game;
            world = new RenderTexture(w, h, 24);
            ui = new RenderTexture(w, h, 24);
            game.Camera.targetTexture = world;
            panel = game.hud.GetComponentInChildren<UIDocument>().panelSettings;
            wasClearing = panel.clearColor;
            wasClear = panel.colorClearValue;
            panel.clearColor = true;
            panel.colorClearValue = Color.clear;
            panel.targetTexture = ui;
        }

        public void Shot(string name)
        {
            var under = Read(world);
            var over = Read(ui);
            // The UI is drawn onto transparent black, so its colour is
            // already multiplied by its alpha.
            for (int i = 0; i < under.Length; i++)
            {
                var a = over[i];
                var b = under[i];
                int k = 255 - a.a;
                under[i] = new Color32((byte)Mathf.Min(255, a.r + b.r * k / 255), (byte)Mathf.Min(255, a.g + b.g * k / 255), (byte)Mathf.Min(255, a.b + b.b * k / 255), 255);
            }
            var tex = new Texture2D(world.width, world.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(under);
            tex.Apply();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "shots"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        static Color32[] Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            UnityEngine.Object.Destroy(tex);
            return px;
        }

        public void Dispose()
        {
            if (game != null && game.Camera != null) game.Camera.targetTexture = null;
            panel.targetTexture = null;
            panel.clearColor = wasClearing;
            panel.colorClearValue = wasClear;
            world.Release();
            ui.Release();
        }
    }
}
