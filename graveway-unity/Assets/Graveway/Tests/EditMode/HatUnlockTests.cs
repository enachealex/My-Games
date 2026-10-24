// Hats: each one in the art catalog for every survivor, unlocked by runs
// with no playtest tools on (and by what an older save had done already),
// and worn by any survivor once it's yours.

using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Graveway.Tests
{
    public class HatUnlockTests
    {
        const string Key = "graveway.save.hattests";

        [SetUp]
        public void SetUp()
        {
            Clean();
            Save.UseKey(Key);
        }

        [TearDown]
        public void TearDown()
        {
            Clean();
            Save.UseKey("graveway.save.tests");
        }

        static void Clean()
        {
            if (Directory.Exists(Save.Folder))
                foreach (var f in Directory.GetFiles(Save.Folder, Key + "*")) File.Delete(f);
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.DeleteKey(Key + ".notes");
        }

        static RunRecord Run(int distance, int kills = 0, int bosses = 0, bool board = true, int place = 0) =>
            new RunRecord { distance = distance, kills = kills, bosses = bosses, character = "rook", board = board, place = place };

        [Test]
        public void EveryHatFitsEverySurvivor()
        {
            var cat = ArtCatalog.Get;
            foreach (var h in Tunables.Hats)
            {
                var art = cat.FindHat(h.id);
                Assert.That(art, Is.Not.Null, $"{h.id} isn't in the art catalog (Graveway → Set up project)");
                Assert.That(art.mesh, Is.Not.Null, h.id);
                foreach (var model in Rigs.SurvivorModel.Values)
                    Assert.That(System.Array.Exists(art.on, p => p.id == model), $"{h.id} on {model}");
                Assert.That(h.target, Is.GreaterThan(0));
                Assert.That(new[] { "best", "metres", "kills", "runKills", "bosses", "runs", "place" }, Does.Contain(h.stat), h.id);
            }
            // And every survivor has a head to wear them on: hair kept in
            // under the hat, and no higher than their head as it comes.
            foreach (var model in Rigs.SurvivorModel.Values)
            {
                var head = cat.HatHeadOf(model);
                Assert.That(head?.hatted, Is.Not.Null, $"{model}'s head for hats");
                var full = cat.Find(model).model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                Assert.That(head.hatted.vertexCount, Is.EqualTo(full.vertexCount), model);
                Assert.That(head.hatted.bounds.max.y, Is.LessThanOrEqualTo(full.bounds.max.y + 1e-4f), model);
            }
        }

        // Every hat that's earned does something while it's worn; a survivor's
        // own hat (theirs from the start) doesn't.
        [Test]
        public void EarnedHatsAreBoosters()
        {
            foreach (var h in Tunables.Hats)
            {
                Assert.That(h.boost, Is.Not.Null, h.id);
                Assert.That(h.amount, Is.GreaterThan(0), h.id);
                Assert.That(Tunables.Booster(h), Does.StartWith("+").Or.Contains("faster"), h.id);
            }
            foreach (var h in Tunables.OwnHats) Assert.That(Tunables.Booster(h), Is.Empty, h.id);
            var crown = Tunables.Hat("crown");
            for (int s = 0; s < Tunables.Challenges.Stats.Length; s++) Assert.That(Tunables.HatBoost(crown, s), Is.EqualTo(0.05f), "the crown: all four");
            var mohawk = Tunables.Hat("mohawk");
            Assert.That(Tunables.HatBoost(mohawk, Tunables.Challenges.Damage), Is.EqualTo(0.05f));
            Assert.That(Tunables.HatBoost(mohawk, Tunables.Challenges.Range), Is.Zero);
            Assert.That(Tunables.Booster(Tunables.Hat("hardhat")), Is.EqualTo("+10 max health"));
        }

        [Test]
        public void RunsUnlockThem()
        {
            Assert.That(Save.HasHat("hardhat"), Is.False);
            Save.Bank(Run(120));
            Assert.That(Save.NewHats, Is.EqualTo(new[] { "hardhat" }));
            Assert.That(Save.HasHat("hardhat"));
            Save.Bank(Run(30, kills: 1000));
            Save.Bank(Run(30, kills: 600, bosses: 1));
            Assert.That(Save.NewHats, Is.EqualTo(new[] { "mohawk" }), "1,600 kills, but not in one run");
            Assert.That(Save.Stat("kills"), Is.EqualTo(1600));
            Assert.That(Save.Stat("runKills"), Is.EqualTo(1000));
            Assert.That(Save.Stat("metres"), Is.EqualTo(180));
            Assert.That(Save.Stat("runs"), Is.EqualTo(3));
            Save.Bank(Run(40, kills: 1500));
            Assert.That(Save.NewHats, Is.EqualTo(new[] { "straw" }));
            Save.Bank(Run(40));
            Assert.That(Save.NewHats, Is.Empty);
        }

        // Getting to the docks, and then the highway, by helicopter.
        [Test]
        public void PlacesUnlockTheirHats()
        {
            Save.Bank(Run(1200, place: 1));
            Assert.That(Save.NewHats, Does.Contain("beanie"));
            Assert.That(Save.LastScore.place, Is.EqualTo(1), "the scoreboard says how far it got");
            Save.Bank(Run(80));
            Assert.That(Save.Stat("place"), Is.EqualTo(1), "the furthest, not the last");
            Save.Bank(Run(2100, place: 2));
            Assert.That(Save.NewHats, Does.Contain("cone"));
        }

        [Test]
        public void PlaytestRunsDontCount()
        {
            Save.Bank(Run(2000, kills: 5000, bosses: 20, board: false));
            Assert.That(Save.NewHats, Is.Empty);
            Assert.That(Save.Data.hatsOwned, Is.Empty);
            Assert.That(Save.Stat("runs"), Is.Zero);
        }

        [Test]
        public void WhatASaveFromBeforeHadDoneCounts()
        {
            File.WriteAllText(Save.FileOf(Key), "{\"best\": 600, \"kills\": 700, \"runs\": 12}");
            Save.Reload();
            Assert.That(Save.HasHat("hardhat") && Save.HasHat("witch"));
            Assert.That(Save.HasHat("straw"), Is.False, "700 kills, but not in one run");
            Assert.That(Save.HasHat("crown"), Is.False);
        }

        // Kills in one run counted from 24 October 2026: this phone's
        // scoreboard has the most so far, and a straw hat won before stays.
        [Test]
        public void TheMostKillsInARunComeFromTheScoreboard()
        {
            File.WriteAllText(Save.FileOf(Key), "{\"progress\": {\"best\": 1700, \"kills\": 2000, \"runs\": 3}, \"scores\": ["
                + "{\"id\": \"a\", \"character\": \"rook\", \"distance\": 1700, \"kills\": 1600, \"at\": \"2026-10-20T10:00:00Z\"},"
                + "{\"id\": \"b\", \"character\": \"rook\", \"distance\": 200, \"kills\": 150, \"at\": \"2026-10-21T10:00:00Z\"}]}");
            Save.Reload();
            Assert.That(Save.Stat("runKills"), Is.EqualTo(1600));
            Assert.That(Save.HasHat("straw"));
            File.WriteAllText(Save.FileOf(Key), "{\"progress\": {\"best\": 300, \"kills\": 900, \"runs\": 3}, \"hatsOwned\": [\"straw\"]}");
            Save.Reload();
            Assert.That(Save.Stat("runKills"), Is.Zero);
            Assert.That(Save.HasHat("straw"), "won before, kept");
        }

        [Test]
        public void AnySurvivorWearsAnyHatThatsYours()
        {
            Save.Wear("bram", "crown");
            Assert.That(Save.Hat("bram"), Is.Empty, "not yours yet");
            Save.Data.hatsOwned.Add("crown");
            Assert.That(Save.Hat("bram"), Is.EqualTo("crown"));
            Assert.That(Save.Hat("wren"), Is.Empty, "each survivor wears their own choice");
            Save.Wear("rook", "crown");
            Save.Wear("bram", "");
            Save.Reload();
            Assert.That((Save.Hat("rook"), Save.Hat("bram")), Is.EqualTo(("crown", "")));
            Save.Data.settings.allHats = true;
            Save.Wear("juno", "witch");
            Assert.That(Save.Hat("juno"), Is.EqualTo("witch"), "the playtest tool lends every hat");
        }

        // Rook's cap and Juno's beanie are part of them: worn until they pick
        // another hat or none, and nobody else's to wear.
        [Test]
        public void TheirOwnHatsComeOff()
        {
            Assert.That((Save.Hat("rook"), Save.Hat("juno"), Save.Hat("bram")), Is.EqualTo(("rookcap", "junobeanie", "")));
            Save.Wear("rook", "");
            Assert.That(Save.Hat("rook"), Is.Empty, "no hat, cap and all");
            Save.Wear("juno", "junobeanie");
            Save.Data.settings.allHats = true;
            Save.Wear("bram", "rookcap");
            Assert.That(Save.Hat("bram"), Is.Empty, "Rook's cap is Rook's");
            Save.Reload();
            Assert.That((Save.Hat("rook"), Save.Hat("juno")), Is.EqualTo(("", "junobeanie")));
            var cat = ArtCatalog.Get;
            foreach (var h in Tunables.OwnHats)
            {
                var art = cat.FindHat(h.id);
                Assert.That(art, Is.Not.Null, h.id);
                Assert.That(art.bare, Is.Not.Null, $"{h.id}: their model without it");
                Assert.That(art.model, Is.EqualTo(Rigs.SurvivorModel[h.owner]));
                Assert.That(art.mesh.triangles.Length, Is.GreaterThan(0), h.id);
            }
        }
    }
}
