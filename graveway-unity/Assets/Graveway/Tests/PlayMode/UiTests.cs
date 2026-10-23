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
        public IEnumerator ArmoryUnlocksAGunAndFitsAPart()
        {
            Save.Data.coins = 100000;
            Click(ui, "Armory");
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
            Assert.That(Visible(Button(ui, "PLAY")), "Back from the Armory goes to the menu");
        }

        bool Showing(string text) => ui.Query<Button>().ToList().Exists(b => Visible(b) && Says(b, text));

        // The menu's survivor card: where you change who you run as.
        void OpenSurvivors() => Press(Find<Button>(ui, b => b.ClassListContains("m-card") && Says(b, "SURVIVOR"), "the survivor card"));

        [UnityTest]
        public IEnumerator SurvivorsAreBought()
        {
            var wren = Tunables.Character("wren");
            Save.Data.coins = wren.price;
            OpenSurvivors();
            yield return null;
            Assert.That(Showing("RUN AS ROOK"));
            Click(ui, "‹");                             // round to the last: Wren
            yield return null;
            Assert.That(Visible(Find<Label>(ui, l => l.text == "WREN", "her name")));
            Assert.That(Showing("RUN AS WREN"), Is.False, "she isn't yours yet");
            Assert.That(Save.Data.character, Is.EqualTo("rook"), "looking at her doesn't make her the one you run as");
            Button Unlock() => Find<Button>(ui, b => b.ClassListContains("c-buy"), "the unlock button");
            Press(Unlock());
            yield return null;
            Assert.That(Save.HasSurvivor(wren));
            Assert.That(Save.Data.coins, Is.Zero);
            Assert.That(Save.Data.character, Is.EqualTo("wren"));
            Assert.That(Showing("RUN AS WREN"));

            Click(ui, "‹");                             // Juno: more than what's left
            yield return null;
            Press(Unlock());
            yield return null;
            Assert.That(Save.HasSurvivor(Tunables.Character("juno")), Is.False);
            Assert.That(Save.Data.character, Is.EqualTo("wren"));
            game.hud.Back();
            yield return null;
            Assert.That(Visible(Button(ui, "PLAY")));
        }

        // Wren's SMG is the Armory's SMG: the parts fitted there are on it,
        // and it never takes a loadout slot as well.
        [UnityTest]
        public IEnumerator ASurvivorsOwnGunIsTheArmorysGun()
        {
            Save.Data.coins = 100000;
            Save.Data.survivors.Add("wren");
            Save.Data.character = "wren";
            Click(ui, "Armory");
            yield return null;
            var w = Tunables.Weapon("smg");
            Press(Find<Button>(ui, b => b.ClassListContains("wcard") && Says(b, w.name), "the SMG's card"));
            yield return null;
            Press(Find<Button>(ui, b => Says(b, "Unlock for "), "the unlock button"));
            yield return null;
            Assert.That(Save.Data.owned, Does.Contain("smg"));
            Assert.That(Save.Loadout("wren"), Does.Not.Contain("smg"), "she carries it anyway");
            Assert.That(Showing("Equip"), Is.False, "nothing to equip");
            var strip = ui.Query<VisualElement>(className: "lo-slot").ToList().FindAll(Visible);
            Assert.That(strip.FindAll(s => Says(s, "SMG")), Is.Empty, "no second SMG in her loadout");

            string slot = w.slots[0];
            Press(Find<Button>(ui, b => b.ClassListContains("slotrow") && Says(b, Tunables.PartSlots[slot].ToUpperInvariant()), $"the {slot} slot"));
            yield return null;
            var part = Tunables.PartsFor(w, slot)[1];
            Press(Find<VisualElement>(ui, v => v.ClassListContains("part") && Says(v, part.name), $"the {part.name} row").Q<Button>());
            yield return null;
            game.hud.Back();
            yield return null;

            game.StartRun();
            yield return Seconds(0.5f);
            var guns = game.player.weapons;
            Assert.That(guns[0].def.id, Is.EqualTo("smg"));
            Assert.That(guns[0].fitted[slot], Is.EqualTo(part.id), "with the part fitted in the Armory");
            Assert.That(guns.ConvertAll(g => g.def.id), Is.Unique);
        }

        // A run goes on the scoreboard; the name it's under is typed (or
        // picked) on the results, and the scoreboard opens from there and
        // from the menu's best run.
        [UnityTest]
        public IEnumerator RunsGoOnTheScoreboardUnderAName()
        {
            var tools = Save.Data.settings;
            tools.god = tools.powers = false;
            tools.startAt = 0;
            Save.Data.scores.Add(new Score { id = "alex", name = "Alex", character = "bram", distance = 900, kills = 300, at = "2026-10-20T12:00:00.0000000Z" });
            Click(ui, "PLAY");
            yield return Seconds(1);
            Assert.That(game.state, Is.EqualTo(Game.State.Playing), "PLAY goes straight into a run");
            var p = game.player;
            p.z -= 40;                                  // 40 m up the road
            yield return Seconds(0.5f);
            game.HurtPlayer(p.hp + p.armor + 1, p.x, p.z - 1);
            yield return Seconds(3);

            var mine = Save.LastScore;
            Assert.That(mine, Is.Not.Null, "on the scoreboard");
            Assert.That(Save.Rank(mine), Is.EqualTo(2));
            Assert.That(Visible(Find<Label>(ui, l => l.text == "#2 on this phone", "where it went")));
            var field = Find<TextField>(ui, f => f.ClassListContains("name-field"), "the name box");
            Assert.That(Showing("Alex"), "a name already on the scoreboard, a tap away");
            field.value = "  Sam  ";
            yield return null;
            Assert.That(mine.name, Is.EqualTo("Sam"));
            Assert.That(Save.Data.playerName, Is.EqualTo("Sam"), "and the next run goes under it too");

            using (var photo = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null ? new Photo(game) : null)
            {
                if (photo != null) { yield return Seconds(0.5f); photo.Shot("14b-result-name"); }
                Click(ui, "Scoreboard");
                yield return Seconds(0.5f);
                var rows = ui.Query<VisualElement>(className: "sc-row").ToList().FindAll(Visible);
                Assert.That(rows.Count, Is.EqualTo(2));
                Assert.That(Says(rows[0], "Alex") && Says(rows[1], "Sam"));
                Assert.That(rows[1].ClassListContains("mine"), "this run picked out");
                if (photo != null) photo.Shot("17-scoreboard");
            }
            game.hud.Back();
            yield return null;
            Press(Find<Button>(ui, b => b.ClassListContains("m-best"), "the best run card"));
            yield return null;
            Assert.That(Visible(Find<Label>(ui, l => l.text == "SCOREBOARD", "the scoreboard's title")));
        }

        [UnityTest]
        public IEnumerator SurvivorUpgradesCostCoins()
        {
            var u = Tunables.PlayerUpgrades[0];
            int cost = Tunables.PlayerUpgradeCost(u.id, 0);
            Save.Data.coins = cost;
            Click(ui, "Armory");
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
        public IEnumerator QuittingARunKeepsItsCoins()
        {
            game.StartRun();
            game.invincible = true;
            yield return Seconds(1);
            int coins = Save.Data.coins, runs = Save.Data.runs;
            game.run.coins = 12;
            game.Pause();
            yield return null;
            Click(ui, "Quit");
            yield return null;
            Assert.That(Save.Data.coins, Is.EqualTo(coins + 12));
            Assert.That(Save.Data.runs, Is.EqualTo(runs + 1));
            Assert.That(Save.Data.running, Is.Null);
            Save.Reload();
            Assert.That(Save.Data.coins, Is.EqualTo(coins + 12), "and it's on the disk");
        }

        [UnityTest]
        public IEnumerator ARunTheAppClosesOnIsBankedNextTime()
        {
            game.StartRun();
            game.invincible = true;
            yield return Seconds(1);
            int coins = Save.Data.coins;
            game.run.coins = 20;
            game.SendMessage("OnApplicationPause", true);            // into the background...
            Assert.That(game.state, Is.EqualTo(Game.State.Paused));
            game.run.banked = true;                                  // ...and Android closes the app
            Save.Reload();                                           // it starts again
            var r = Save.BankUnfinished();
            Assert.That(r, Is.Not.Null);
            Assert.That(r.coins, Is.EqualTo(20));
            Assert.That(Save.Data.coins, Is.EqualTo(coins + 20));
        }

        // The real thing, against GitHub: from the editor (build 0), every
        // published build is newer. Skipped without a network.
        [UnityTest]
        public IEnumerator FindsTheReleasesOnGitHub()
        {
            var u = game.hud.updates;
            yield return u.Check(true);
            if (u.newer == null && u.status.StartsWith("Couldn't")) Assert.Ignore("no network: " + u.status);
            Assert.That(u.newer, Is.Not.Null, u.status);
            StringAssert.StartsWith("https://github.com/enachealex/My-Games/releases/download/graveway-unity-v", u.newer.apk);
            StringAssert.EndsWith(".apk", u.newer.apk);
            Assert.That(u.newer.code, Is.GreaterThanOrEqualTo(1));
            yield return null;
            Assert.That(Visible(Button(ui, $"New version {u.newer.version} · tap to get it")), "offered on the menu");
        }

        [UnityTest]
        public IEnumerator BackUnwindsScreens()
        {
            string run = $"RUN AS {Tunables.Character(Save.Data.character).name.ToUpperInvariant()}";
            OpenSurvivors();
            yield return null;
            Assert.That(Visible(Button(ui, run)));
            game.hud.Back();
            yield return null;
            Assert.That(Visible(Button(ui, "PLAY")));
            Click(ui, "PLAY");                          // straight into a run
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
            OpenSurvivors();
            yield return Seconds(0.5f);
            photo.Shot("5-select");
            Click(ui, "›");
            yield return Seconds(0.5f);
            photo.Shot("5b-select-locked");
            game.hud.Back();
            Click(ui, "Armory");
            Press(Find<Button>(ui, b => b.ClassListContains("wcard") && Says(b, "SMG"), "the SMG's card"));
            yield return Seconds(0.5f);
            photo.Shot("6-armory");
            var smg = Tunables.Weapon("smg");
            Press(Find<Button>(ui, b => b.ClassListContains("slotrow") && Says(b, Tunables.PartSlots[smg.slots[0]].ToUpperInvariant()), "the first slot"));
            yield return null;
            var scroll = ui.Query<ScrollView>(className: "a-body").First();
            scroll.scrollOffset = new Vector2(0, 100000);
            yield return Seconds(0.5f);
            photo.Shot("7-armory-parts");
            Click(ui, "Survivor");
            yield return Seconds(0.5f);
            photo.Shot("8-armory-survivor");
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

        // Every gun bare and with a full set of parts, as the Armory
        // shows them, to Logs/shots/gun-*.png.
        [UnityTest]
        public IEnumerator GunPictures()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics), so nothing to see");
            yield return null;
            var loaded = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>
            {
                { "rifle", new System.Collections.Generic.Dictionary<string, string> { { "barrel", "long" }, { "muzzle", "suppressor" }, { "sight", "scope" }, { "rail", "light" }, { "stock", "heavy" }, { "mag", "extended" }, { "grip", "stippled" } } },
                { "smg", new System.Collections.Generic.Dictionary<string, string> { { "barrel", "short" }, { "muzzle", "comp" }, { "sight", "reddot" }, { "rail", "foregrip" }, { "stock", "light" }, { "mag", "drum" }, { "grip", "rubber" } } },
                { "shotgun", new System.Collections.Generic.Dictionary<string, string> { { "barrel", "heavy" }, { "muzzle", "choke" }, { "sight", "holo" }, { "rail", "laser" }, { "stock", "precision" }, { "mag", "quick" } } },
                { "pistol", new System.Collections.Generic.Dictionary<string, string> { { "muzzle", "suppressor" }, { "sight", "reddot" }, { "rail", "laser" }, { "mag", "extended" } } },
            };
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Logs", "shots"));
            System.IO.Directory.CreateDirectory(dir);
            foreach (var w in Tunables.Weapons)
            {
                var bare = Studio.GunPicture(w.model, new System.Collections.Generic.Dictionary<string, string>());
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"gun-{w.id}.png"), Png(bare));
                if (loaded.TryGetValue(w.id, out var parts))
                {
                    var full = Studio.GunPicture(w.model, parts);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"gun-{w.id}-parts.png"), Png(full));
                }
                yield return null;
            }
        }

        // The studio's pictures live on the GPU only: copy one back to save it.
        static byte[] Png(Texture tex)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var png = copy.EncodeToPNG();
            Object.Destroy(copy);
            return png;
        }

        IEnumerator Seconds(float s)
        {
            for (int i = 0; i < s / Step; i++) yield return null;
        }
    }
}
