// The Black Market in play: buying into the backpack from its screen, and
// every item called in from the backpack on the HUD doing what it says.
// With a GPU, pictures of the shop and of a run with help on the way.

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Graveway.Tests.TestUi;

namespace Graveway.Tests
{
    public class BlackMarketPlayTests
    {
        const float Step = 1f / 20;
        Game game;
        VisualElement ui;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Save.UseKey("graveway.save.tests");
            Save.Reset();
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
            if (game != null) game.invincible = false;
        }

        IEnumerator Seconds(float s)
        {
            for (int i = 0; i < s / Step; i++) yield return null;
        }

        Button BuyButton(string name) =>
            Find<VisualElement>(ui, v => v.ClassListContains("bm-item") && Says(v, name), $"the {name} card").Q<Button>(className: "bm-buy");

        [UnityTest]
        public IEnumerator SellsIntoTheBackpack()
        {
            Save.Data.coins = 2000;
            Click(ui, "Black Market");
            yield return null;
            Assert.That(Visible(Find<Label>(ui, l => l.text == "BLACK MARKET", "its title")));
            Press(BuyButton("Strafing run"));
            yield return null;
            Assert.That(Save.Data.coins, Is.EqualTo(2000 - 120));
            Assert.That(Save.Data.backpack, Is.EqualTo(new[] { "strafe" }));
            for (int i = 0; i < 5; i++) { Press(BuyButton("Strafing run")); yield return null; }
            Assert.That(Save.Data.backpack.Count, Is.EqualTo(6));
            int coins = Save.Data.coins;
            Press(BuyButton("Helicopter"));                         // the backpack's full
            yield return null;
            Assert.That(Save.Data.backpack.Count, Is.EqualTo(6));
            Assert.That(Save.Data.coins, Is.EqualTo(coins));
            game.hud.Back();
            yield return null;
            Assert.That(Visible(Button(ui, "PLAY")), "Back from the Black Market goes to the menu");
            Assert.That(Visible(Button(ui, "Armory")), "and the Armory's still there");
        }

        [UnityTest]
        public IEnumerator EveryItemDoesItsJob()
        {
            Save.Data.backpack.AddRange(new[] { "strafe", "cluster", "napalm", "backup", "heli", "skipboss" });
            game.nextSeed = 7;
            game.StartRun();
            game.invincible = true;
            yield return Seconds(1);
            var p = game.player;
            for (int i = 0; i < 12; i++) game.SpawnZombie("walker", p.x - 5 + i % 4 * 3, p.z - 12 - i / 4 * 4, 0, chase: true);
            yield return null;
            int buttons = ui.Query<VisualElement>(className: "pack-btn").ToList().Count;
            Assert.That(buttons, Is.EqualTo(6), "one button for each in the backpack");

            int kills = game.run.kills;
            Assert.That(game.CallIn("cluster"));
            Assert.That(game.CallIn("cluster"), Is.False, "one on its way at a time");
            Assert.That(game.CallIn("strafe"));
            yield return Seconds(5);
            Assert.That(game.run.kills, Is.GreaterThan(kills), "the airstrikes killed something");

            Assert.That(game.CallIn("heli"));
            Assert.That(game.CallIn("backup"));
            Assert.That(game.CallIn("napalm"));
            for (int i = 0; i < 8; i++) game.SpawnZombie("walker", p.x - 4 + i, p.z - 14, 0, chase: true);
            yield return Seconds(7);
            Assert.That(game.HeliLeft, Is.GreaterThan(0).And.LessThan(Tunables.Support.HeliTime), "the helicopter's firing");
            Assert.That(game.AllyCount, Is.EqualTo(1), "backup has arrived");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                for (int i = 0; i < 6; i++) game.SpawnZombie("walker", p.x - 4 + i * 1.6f, p.z - 13, 0, chase: true);
                using var photo = new Photo(game);
                yield return Seconds(1);
                photo.Shot("16-help-on-the-way");
            }

            Assert.That(game.CallIn("skipboss"));
            Assert.That(game.run.skipBoss);
            game.SpawnBoss();
            Assert.That(game.run.boss, Is.Null, "no boss");
            Assert.That(game.run.skipBoss, Is.False, "and the skip's used up");
            Assert.That(Save.Data.backpack, Is.Empty);
            yield return Seconds(Tunables.Support.HeliTime);
            Assert.That(game.HeliLeft, Is.LessThan(0), "the helicopter's gone");
        }

        [UnityTest]
        public IEnumerator Pictures()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics), so nothing to see");
            Save.Data.coins = 640;
            Save.Data.backpack.AddRange(new[] { "heli", "strafe", "strafe" });
            using var photo = new Photo(game);
            yield return Seconds(1);
            Click(ui, "Black Market");
            yield return Seconds(0.5f);
            photo.Shot("15-black-market");
            game.hud.Back();
            game.nextSeed = 7;
            game.StartRun();
            game.invincible = true;
            yield return Seconds(2);
            photo.Shot("15b-backpack");
        }
    }
}
