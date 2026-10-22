// Survivors bought with coins, Rook to start with, and each with a loadout
// of their own that never carries their own gun twice.

using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Graveway.Tests
{
    public class SurvivorTests
    {
        const string Key = "graveway.save.survivortests";

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

        static string Main => Save.FileOf(Key);

        static CharacterDef C(string id) => Tunables.Character(id);

        [Test]
        public void RookIsFreeAndTheRestCostMoreThanTheirGuns()
        {
            Assert.That(Tunables.Characters[0].id, Is.EqualTo("rook"));
            Assert.That(Tunables.Characters[0].price, Is.Zero);
            foreach (var c in Tunables.Characters.Skip(1))
                Assert.That(c.price, Is.GreaterThan(Tunables.Weapon(c.startingWeapon).price), c.id);
        }

        [Test]
        public void ANewSaveHasOnlyRook()
        {
            Assert.That(Save.Data.character, Is.EqualTo("rook"));
            foreach (var c in Tunables.Characters) Assert.That(Save.HasSurvivor(c), Is.EqualTo(c.price == 0), c.id);
        }

        [Test]
        public void ASaveFromBeforeKeepsTheSurvivorItRanAs()
        {
            File.WriteAllText(Main, "{\"coins\": 50, \"character\": \"juno\"}");
            Save.Reload();
            Assert.That(Save.Data.character, Is.EqualTo("juno"));
            Assert.That(Save.HasSurvivor(C("juno")));
            Assert.That(Save.HasSurvivor(C("wren")), Is.False);
            Assert.That(Save.HasSurvivor(C("bram")), Is.False);
        }

        [Test]
        public void NobodyRunsAsASurvivorTheyHaventGot()
        {
            File.WriteAllText(Main, "{\"character\": \"wren\", \"survivors\": [\"bram\", \"nobody\", \"bram\"]}");
            Save.Reload();
            Assert.That(Save.Data.character, Is.EqualTo("rook"));
            Assert.That(Save.Data.survivors, Is.EqualTo(new[] { "bram" }));
        }

        [Test]
        public void EachHasTheirOwnLoadoutAndNoGunTwice()
        {
            // From before: one loadout for everyone, with the SMG in it.
            File.WriteAllText(Main, "{\"owned\": [\"pistol\", \"smg\", \"shotgun\", \"rifle\"], \"loadout\": [\"smg\", \"shotgun\"], " +
                                    "\"survivors\": [\"wren\", \"bram\"]}");
            Save.Reload();
            Assert.That(Save.RunWeapons(C("rook")), Is.EqualTo(new[] { "pistol", "smg", "shotgun" }));
            Assert.That(Save.RunWeapons(C("wren")), Is.EqualTo(new[] { "smg", "shotgun" }), "her own SMG, once");
            Assert.That(Save.RunWeapons(C("bram")), Is.EqualTo(new[] { "shotgun", "smg" }));

            // The slot that frees up for Wren is hers alone.
            Save.Loadout("wren").Add("rifle");
            Assert.That(Save.RunWeapons(C("wren")), Is.EqualTo(new[] { "smg", "shotgun", "rifle" }));
            Assert.That(Save.Loadout("rook"), Is.EqualTo(new[] { "smg", "shotgun" }));

            Save.Persist();
            Save.Reload();
            Assert.That(Save.Loadout("wren"), Is.EqualTo(new[] { "shotgun", "rifle" }), "kept");
            foreach (var c in Tunables.Characters)
            {
                var guns = Save.RunWeapons(c);
                Assert.That(guns, Is.Unique, c.id);
                Assert.That(guns[0], Is.EqualTo(c.startingWeapon), c.id);
            }
        }
    }
}
