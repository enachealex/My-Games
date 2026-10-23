// Hats: unlocked by what you've done, worn by any survivor, picked on the
// survivor's card. With a GPU, pictures of every hat on every survivor.

using System.Collections;
using System.Linq;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Graveway.Tests.TestUi;

namespace Graveway.Tests
{
    public class HatTests
    {
        const float Step = 1f / 20;
        Game game;
        VisualElement ui;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Save.UseKey("graveway.save.tests");
            Save.Reset();
            Save.Data.settings.allHats = false;         // Reset keeps settings, this playtest tool among them
            AudioListener.volume = 0;
            game = Object.FindAnyObjectByType<Game>();
            for (int i = 0; i < 5 && game == null; i++)
            {
                yield return null;
                game = Object.FindAnyObjectByType<Game>();
            }
            Assert.That(game, Is.Not.Null, "the game didn't boot itself");
            Sfx.Enabled = false;
            Time.captureDeltaTime = Step;
            game.ShowMenu();
            game.hud.ShowMenu();
            ui = Root(game);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0;
            Save.Data.settings.allHats = false;
            Save.Persist();
        }

        IEnumerator Seconds(float s)
        {
            for (int i = 0; i < s / Step; i++) yield return null;
        }

        // A picture from the GPU (pictures are kept there only) into another.
        static void Paste(Texture src, Texture2D into, int x)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            into.ReadPixels(new Rect(0, 0, src.width, src.height), x, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }

        static void Write(Texture tex, string name)
        {
            if (!(tex is Texture2D t2) || !t2.isReadable)
            {
                var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
                Paste(tex, copy, 0);
                copy.Apply();
                Write(copy, name);
                Object.Destroy(copy);
                return;
            }
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "shots", "hats"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), t2.EncodeToPNG());
        }

        bool Showing(string text) => ui.Query<Button>().ToList().Exists(b => Visible(b) && Says(b, text));

        Button HatCard(string name) => Find<Button>(ui, b => b.ClassListContains("hcard") && Says(b, name), $"the {name} card");

        // From the menu's survivor card to their hats: one that's yours goes
        // on (on the survivor on the street too); one that isn't stays off.
        [UnityTest]
        public IEnumerator PickedFromTheSurvivorsCard()
        {
            Save.Data.hatsOwned.Add("hardhat");
            Press(Find<Button>(ui, b => b.ClassListContains("m-card") && Says(b, "SURVIVOR"), "the survivor card"));
            yield return null;
            Assert.That(Visible(Find<Label>(ui, l => l.text == "None", "no hat yet")));
            Click(ui, "Hats");
            yield return null;
            Assert.That(Says(HatCard("Crown"), Tunables.Hat("crown").goal), "a locked hat shows what unlocks it");
            Press(HatCard("Crown"));
            yield return null;
            Assert.That(Save.Hat("rook"), Is.Empty);
            Press(HatCard("Hard hat"));
            yield return null;
            Assert.That(Save.Hat("rook"), Is.EqualTo("hardhat"));
            Assert.That(GameObject.Find("hat hardhat"), Is.Not.Null, "on Rook on the street");
            Assert.That(HatCard("Hard hat").ClassListContains("on"));

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                using var photo = new Photo(game);
                yield return Seconds(0.5f);
                photo.Shot("18-hats");
            }
            game.hud.Back();
            yield return null;
            Assert.That(Visible(Find<Label>(ui, l => l.text == "Hard hat", "the hat on the survivor's card")));
        }

        // A run that gets far enough unlocks a hat, and the results say so.
        [UnityTest]
        public IEnumerator ARunUnlocksAHat()
        {
            var tools = Save.Data.settings;
            tools.god = tools.powers = false;
            tools.startAt = 0;
            game.StartRun();
            yield return Seconds(1);
            var p = game.player;
            p.z -= 120;
            yield return Seconds(0.5f);
            game.HurtPlayer(p.hp + p.armor + 1, p.x, p.z - 1);
            yield return Seconds(3);
            Assert.That(Save.HasHat("hardhat"));
            var row = Find<VisualElement>(ui, v => v.ClassListContains("r-hat"), "the new hat on the results");
            Assert.That(Says(row, "Hard hat"));
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                using var photo = new Photo(game);
                yield return Seconds(0.5f);
                photo.Shot("14c-result-hat");
            }
            Click(ui, "Wear it");
            yield return null;
            Press(HatCard("Hard hat"));
            yield return null;
            Assert.That(Save.Hat("rook"), Is.EqualTo("hardhat"));
            Save.Wear("rook", "");
        }

        // In a run, from above, as it's played.
        [UnityTest]
        public IEnumerator SeenInARun()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics), so nothing to see");
            Save.Data.settings.allHats = true;
            Save.Wear("rook", "straw");
            game.nextSeed = 7;
            game.StartRun();
            game.invincible = true;
            using var photo = new Photo(game);
            yield return Seconds(2);
            photo.Shot("19-hat-in-a-run");
            game.invincible = false;
            Save.Wear("rook", "");
        }

        // Every hat on every survivor, as their portraits, side by side; and
        // each hat's own picture.
        [UnityTest]
        public IEnumerator Pictures()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics), so nothing to see");
            Save.Data.settings.allHats = true;
            const int S = 256;
            foreach (var h in Tunables.Hats)
            {
                foreach (var c in Tunables.Characters) Save.Data.hats[c.id] = h.id;
                var faces = Studio.Portraits(S);
                var row = new Texture2D(S * faces.Count, S, TextureFormat.RGB24, false);
                int i = 0;
                foreach (var c in Tunables.Characters) Paste(faces[c.id], row, S * i++);
                row.Apply();
                Write(row, "on-" + h.id);
                Object.Destroy(row);
                foreach (var t in faces.Values) Object.Destroy(t);
                var pic = Studio.HatPicture(h.id);
                Assert.That(pic, Is.Not.Null, h.id);
                Write(pic, "hat-" + h.id);
                yield return null;
            }
        }
    }
}
