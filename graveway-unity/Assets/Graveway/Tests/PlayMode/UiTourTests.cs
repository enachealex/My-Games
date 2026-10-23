// A tour of every screen, as pictures in Logs/shots/tour/<shape>/, at three
// shapes of screen: a tall phone like the playtest one, an older 16:9
// phone, and a 3:4 tablet. For looking the UI over; beyond each screen
// being there, nothing's checked. Needs a GPU.

using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Graveway.Tests.TestUi;

namespace Graveway.Tests
{
    public class UiTourTests
    {
        const float Step = 1f / 20;
        static readonly (int w, int h, string name)[] Shapes = { (540, 1170, "tall"), (720, 1280, "16x9"), (900, 1200, "tablet") };
        Game game;
        VisualElement ui;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Save.UseKey("graveway.save.tests");
            Save.Reset();
            var tools = Save.Data.settings;
            tools.god = tools.powers = tools.allHats = false;
            tools.startAt = 0;
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

        // A save that's been played a while: some of everything to show.
        static void Played()
        {
            var d = Save.Data;
            d.coins = 2340;
            d.best = 529;
            d.runs = 14;
            d.kills = 1234;
            d.owned.AddRange(new[] { "smg", "shotgun" });
            d.survivors.Add("wren");
            d.hatsOwned.AddRange(new[] { "hardhat", "witch" });
            d.hats["rook"] = "witch";
            d.backpack.AddRange(new[] { "heli", "strafe", "strafe", "cluster" });
            d.playerName = "Alex";
            string[] names = { "Alex", "Sam", "Alex", "Jordan the Great", "Sam" };
            int[] metres = { 529, 488, 402, 377, 120 };
            for (int i = 0; i < names.Length; i++)
                d.scores.Add(new Score { id = "s" + i, name = names[i], character = Tunables.Characters[i % 4].id, distance = metres[i], kills = metres[i] / 2,
                    at = "2026-10-2" + i + "T12:00:00.0000000Z" });
            Save.Notes.Add(new Note { at = "2026-10-21T12:00:00.000Z", tags = { "Too hard" }, text = "The first boss hits very hard.", ctx = "Rook · 412 m (stage 2)" });
            Save.Persist();
        }

        ScrollView Body() => Find<ScrollView>(ui, s => s.ClassListContains("a-body"), "the screen's body");

        [UnityTest]
        public IEnumerator Tour([Values(0, 1, 2)] int k)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics), so nothing to see");
            var (w, h, shape) = Shapes[k];
            Played();
            game.hud.SendMessage("RefreshPortraits");     // with the hats the save has on
            game.ShowMenu();
            game.hud.ShowMenu();
            using var photo = new Photo(game, w, h);
            string dir = Path.Combine("tour", shape);
            void Shot(string name) => photo.Shot(Path.Combine(dir, name));
            yield return Seconds(3);                    // portraits are drawn a couple of frames in
            Shot("01-menu");

            Press(Find<Button>(ui, b => b.ClassListContains("m-card") && Says(b, "SURVIVOR"), "the survivor card"));
            yield return Seconds(0.5f);
            Shot("02-survivor");
            Drag(Find<ScrollView>(ui, s => s.ClassListContains("c-card") && !s.ClassListContains("side"), "the survivor's card"), -w * 0.15f);
            yield return Seconds(0.1f);
            Shot("03-survivor-mid-swipe");
            yield return Seconds(0.5f);
            Shot("04-survivor-locked");
            Press(ui.Query<Button>(className: "c-dot").AtIndex(0));
            yield return Seconds(0.3f);
            var card = Find<ScrollView>(ui, s => s.ClassListContains("c-card") && !s.ClassListContains("side"), "the survivor's card");
            card.scrollOffset = new Vector2(0, 100000);
            yield return Seconds(0.3f);
            Shot("05-survivor-scrolled");
            Click(ui, "Hats");
            yield return Seconds(0.5f);
            Shot("06-hats");
            game.hud.Back();
            game.hud.Back();
            yield return null;

            Click(ui, "Armory");
            Click(ui, "Weapons");
            Press(Find<Button>(ui, b => b.ClassListContains("wcard") && Says(b, "SMG"), "the SMG's card"));
            yield return Seconds(0.5f);
            Shot("07-armory");
            var smg = Tunables.Weapon("smg");
            Press(Find<Button>(ui, b => b.ClassListContains("slotrow") && Says(b, Tunables.PartSlots[smg.slots[0]].ToUpperInvariant()), "the first slot"));
            yield return null;
            Body().scrollOffset = new Vector2(0, 100000);
            yield return Seconds(0.5f);
            Shot("08-armory-parts");
            Click(ui, "Survivor");
            yield return Seconds(0.5f);
            Shot("09-armory-survivor");
            game.hud.Back();
            yield return null;

            Click(ui, "Black Market");
            yield return Seconds(0.5f);
            Shot("10-black-market");
            game.hud.Back();
            Press(Find<Button>(ui, b => b.ClassListContains("m-best"), "the best run card"));
            yield return Seconds(0.5f);
            Shot("11-scoreboard");
            game.hud.Back();
            yield return null;

            Press(Find<Button>(ui, b => b.ClassListContains("icon-btn") && b.parent.ClassListContains("menu-top"), "the settings button"));
            yield return Seconds(0.5f);
            Shot("12-settings");
            var sv = ui.Query<ScrollView>(className: "card").ToList().Find(Visible);
            if (sv != null)
            {
                sv.scrollOffset = new Vector2(0, 100000);
                yield return Seconds(0.3f);
                Shot("13-settings-end");
            }
            Click(ui, "Playtest notes");
            yield return Seconds(0.5f);
            Shot("14-notes");
            game.hud.Back();
            game.hud.Back();
            yield return null;

            game.nextSeed = 7;
            game.StartRun();
            game.invincible = true;
            yield return Seconds(3);
            Shot("15-hud");
            game.SpawnBoss();
            game.hud.Toast("Run saved · +12 coins");
            yield return Seconds(1.5f);
            Shot("16-hud-boss");
            game.Pause();
            yield return Seconds(0.5f);
            Shot("17-pause");
            Click(ui, "Leave a note");
            Click(ui, "Looks");
            yield return Seconds(0.5f);
            Shot("18-note");
            game.hud.Back();
            game.Resume();
            game.invincible = false;
            var p = game.player;
            p.z -= 150;
            yield return Seconds(0.5f);
            game.HurtPlayer(p.hp + p.armor + 1, p.x, p.z - 1);
            yield return Seconds(4);
            Shot("19-result");
            var res = ui.Query<ScrollView>(className: "card").ToList().FindLast(Visible);
            if (res != null)
            {
                res.scrollOffset = new Vector2(0, 100000);
                yield return Seconds(0.3f);
                Shot("20-result-end");
            }
        }
    }
}
