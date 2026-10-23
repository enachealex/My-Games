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
                Assert.That(new[] { "best", "metres", "kills", "bosses", "runs", "place" }, Does.Contain(h.stat), h.id);
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

        [Test]
        public void RunsUnlockThem()
        {
            Assert.That(Save.HasHat("hardhat"), Is.False);
            Save.Bank(Run(120));
            Assert.That(Save.NewHats, Is.EqualTo(new[] { "hardhat" }));
            Assert.That(Save.HasHat("hardhat"));
            Save.Bank(Run(30, kills: 300));
            Save.Bank(Run(30, kills: 200, bosses: 1));
            Assert.That(Save.NewHats, Is.EquivalentTo(new[] { "straw", "mohawk" }));
            Assert.That(Save.Stat("kills"), Is.EqualTo(500));
            Assert.That(Save.Stat("metres"), Is.EqualTo(180));
            Assert.That(Save.Stat("runs"), Is.EqualTo(3));
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
            Assert.That(Save.HasHat("hardhat") && Save.HasHat("witch") && Save.HasHat("straw"));
            Assert.That(Save.HasHat("crown"), Is.False);
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
