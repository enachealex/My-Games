// The menus worked by their buttons, as a player would: unlocking a gun,
// fitting a part, buying a survivor upgrade, leaving a playtest note, and the
// back button unwinding screens. With a GPU, a picture of every screen too.

using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Graveway.Tests.TestUi;

namespace Graveway.Tests
{
    public class UiTests
    {
        const float Step = 1f / 20;
        Game game;
        VisualElement ui;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Save.UseKey("graveway.save.tests");
            Save.Reset();
            Save.Notes.Clear();
            Save.PersistNotes();
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

        [UnityTest]
        public IEnumerator BlackMarketUnlocksAGunAndFitsAPart()
        {
            Save.Data.coins = 100000;
            Click(ui, "Black Market");
            yield return null;
            var w = Tunables.Weapon("smg");
            Press(Find<Button>(ui, b => b.ClassListContains("wcard") && Says(b, w.name), "the SMG's card"));
            yield return null;

            int coins = Save.Data.coins;
            Press(Find<Button>(ui, b => Says(b, "Unlock for "), "the unlock button"));
            yield return null;
            Assert.That(Save.Data.owned, Does.Contain("smg"));
            Assert.That(Save.Data.coins, Is.EqualTo(coins - w.price));
            Assert.That(Save.Loadout(), Does.Contain("smg"), "a new gun goes straight into the loadout");

            string slot = w.slots[0];
            Press(Find<Button>(ui, b => b.ClassListContains("slotrow") && Says(b, Tunables.PartSlots[slot].ToUpperInvariant()), $"the {slot} slot"));
            yield return null;
            var parts = Tunables.PartsFor(w, slot);
            VisualElement Row(PartDef p) => Find<VisualElement>(ui, v => v.ClassListContains("part") && Says(v, p.name), $"the {p.name} row");
            coins = Save.Data.coins;
            Press(Row(parts[1]).Q<Button>());
            yield return null;
            Assert.That(Save.FittedParts("smg")[slot], Is.EqualTo(parts[1].id));
            Assert.That(Save.Data.coins, Is.EqualTo(coins - Tunables.PartCost(w, parts[1])));

            // Back to what it came with: free, and the bought part stays owned.
            coins = Save.Data.coins;
            Press(Row(parts[0]).Q<Button>());
            yield return null;
            Assert.That(Tunables.FittedPart(w, slot, Save.FittedParts("smg")).id, Is.EqualTo(parts[0].id));
            Assert.That(Save.Data.coins, Is.EqualTo(coins));
            Assert.That(Save.Parts("smg").owned, Does.Contain($"{slot}:{parts[1].id}"));

            game.hud.Back();
            yield return null;
            Assert.That(Visible(Button(ui, "PLAY")), "Back from the Black Market goes to the menu");
        }

        [UnityTest]
        public IEnumerator SurvivorUpgradesCostCoins()
        {
            var u = Tunables.PlayerUpgrades[0];
            int cost = Tunables.PlayerUpgradeCost(u.id, 0);
            Save.Data.coins = cost;
            Click(ui, "Black Market");
            yield return null;
            Click(ui, "Survivor");
            yield return null;
            Button Buy() => Find<VisualElement>(ui, v => v.ClassListContains("uprow") && Says(v, u.label), $"the {u.label} row").Q<Button>();
            Press(Buy());
            yield return null;
            Assert.That(Save.Upgrade(u.id), Is.EqualTo(1));
            Assert.That(Save.Data.coins, Is.Zero);
            Press(Buy());                               // can't afford the next level
            yield return null;
            Assert.That(Save.Upgrade(u.id), Is.EqualTo(1));
            game.hud.Back();
        }

        [UnityTest]
        public IEnumerator LeavesANoteFromThePauseMenu()
        {
            game.StartRun();
            game.invincible = true;
            yield return Seconds(1);
            game.Pause();
            yield return null;
            Click(ui, "Leave a note");
            yield return null;
            Click(ui, "Save note");                     // nothing written or picked: refused
            Assert.That(Save.Notes, Is.Empty);
            Click(ui, "Bug");
            Click(ui, "Save note");
            Assert.That(Save.Notes.Count, Is.EqualTo(1));
            var n = Save.Notes[0];
            Assert.That(n.tags, Is.EqualTo(new[] { "Bug" }));
            StringAssert.Contains(" HP · ", n.ctx, "a note made in a run says how the run was going");
            Assert.That(n.sent, Is.Null);
            game.Resume();
            Assert.That(game.state, Is.EqualTo(Game.State.Playing));
        }

        [UnityTest]
        public IEnumerator BackUnwindsScreens()
        {
            string run = $"RUN AS {Tunables.Character(Save.Data.character).name.ToUpperInvariant()}";
            Click(ui, "PLAY");
            yield return null;
            Assert.That(Visible(Button(ui, run)));
            game.hud.Back();
            yield return null;
            Assert.That(Visible(Button(ui, "PLAY")));
            Click(ui, "PLAY");
            Click(ui, run);
            yield return Seconds(1);
            Assert.That(game.state, Is.EqualTo(Game.State.Playing));
            game.hud.Back();
            Assert.That(game.state, Is.EqualTo(Game.State.Paused), "Back pauses a run");
            game.hud.Back();
            Assert.That(game.state, Is.EqualTo(Game.State.Playing), "and resumes it");
        }

        [UnityTest]
        public IEnumerator Screenshots()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics), so nothing to see");
            Save.Data.coins = 5000;
            Save.Data.owned.Add("smg");
            Save.Notes.Add(new Note { at = "2026-10-21T12:00:00.000Z", tags = { "Too hard" }, text = "The first boss hits very hard.", ctx = "Rook · 412 m (stage 2)" });
            using var photo = new Photo(game);
            yield return Seconds(3);                    // portraits are drawn a couple of frames in
            photo.Shot("4-menu");
            Click(ui, "PLAY");
            yield return Seconds(0.5f);
            photo.Shot("5-select");
            game.hud.Back();
            Click(ui, "Black Market");
            Press(Find<Button>(ui, b => b.ClassListContains("wcard") && Says(b, "SMG"), "the SMG's card"));
            yield return Seconds(0.5f);
            photo.Shot("6-market");
            var smg = Tunables.Weapon("smg");
            Press(Find<Button>(ui, b => b.ClassListContains("slotrow") && Says(b, Tunables.PartSlots[smg.slots[0]].ToUpperInvariant()), "the first slot"));
            yield return null;
            var scroll = ui.Query<ScrollView>(className: "a-body").First();
            scroll.scrollOffset = new Vector2(0, 100000);
            yield return Seconds(0.5f);
            photo.Shot("7-market-parts");
            Click(ui, "Survivor");
            yield return Seconds(0.5f);
            photo.Shot("8-market-survivor");
            game.hud.Back();
            yield return null;
            Press(Find<Button>(ui, b => b.ClassListContains("icon-btn") && b.parent.ClassListContains("menu-top"), "the settings button"));
            yield return Seconds(0.5f);
            photo.Shot("9-settings");
            Click(ui, "Playtest notes");
            yield return Seconds(0.5f);
            photo.Shot("10-notes");
            game.hud.Back();
            game.hud.Back();
            game.StartRun();
            game.invincible = true;
            yield return Seconds(3);
            photo.Shot("11-hud");
            game.Pause();
            yield return Seconds(0.5f);
            photo.Shot("12-pause");
            Click(ui, "Leave a note");
            Click(ui, "Looks");
            yield return Seconds(0.5f);
            photo.Shot("13-note");
            game.hud.Back();
            game.Resume();
            game.invincible = false;
            var p = game.player;
            game.HurtPlayer(p.hp + p.armor + 1, p.x, p.z - 1);
            yield return Seconds(4);
            photo.Shot("14-result");
        }

        IEnumerator Seconds(float s)
        {
            for (int i = 0; i < s / Step; i++) yield return null;
        }
    }
}
