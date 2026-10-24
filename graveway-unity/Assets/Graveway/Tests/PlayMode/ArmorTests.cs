// The armored boss (Game.Armor.cs): every second boss wears plates that come
// off as it's hurt, each leaving a power-up; blasts break them faster; with
// them all off, it's exposed. With a GPU, pictures of it.

using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Graveway.Tests
{
    public class ArmorTests
    {
        const float Step = 1f / 20;
        Game game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Save.UseKey("graveway.save.tests");
            Save.Reset();
            game = Object.FindAnyObjectByType<Game>();
            for (int i = 0; i < 5 && game == null; i++)
            {
                yield return null;
                game = Object.FindAnyObjectByType<Game>();
            }
            Assert.That(game, Is.Not.Null, "the game didn't boot itself");
            AudioListener.volume = 0;
            Sfx.Enabled = false;
            Time.captureDeltaTime = Step;
            game.ShowMenu();
            game.hud.ShowMenu();
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

        static void Write(Texture2D tex, string name)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "shots", "armor"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        }

        // A run, invincible, with the next boss about to come: the first or
        // the second.
        IEnumerator Run()
        {
            game.nextSeed = 7;
            game.StartRun();
            game.invincible = true;
            yield return Seconds(0.5f);
        }

        Zombie Boss()
        {
            game.SpawnBoss();
            var b = game.run.boss;
            Assert.That(b, Is.Not.Null);
            return b;
        }

        int PowerUps() => game.world.items.FindAll(i => i.kind == "power" && !i.taken).Count;

        [UnityTest]
        public IEnumerator EverySecondBossIsArmored()
        {
            yield return Run();
            var first = Boss();
            Assert.That(first.plates, Is.Zero, "the first boss is a plain one");
            game.DamageZombie(first, first.hp + 1, 0, 1, 0);
            yield return Seconds(0.2f);
            var second = Boss();
            Assert.That(second.plates, Is.EqualTo(4), "the second wears four plates");
            Assert.That(second.armor, Is.EqualTo(second.maxHp * Tunables.BossArmor.Share).Within(0.01f));
            Assert.That(second.plateObjects, Has.All.Not.Null);
        }

        [UnityTest]
        public IEnumerator PlatesComeOffAndLeavePowerUps()
        {
            yield return Run();
            game.DamageZombie(Boss(), 1e6f, 0, 1, 0);
            yield return Seconds(0.2f);
            var b = Boss();
            float hp = b.hp, quarter = b.armorMax / 4, hit = 40;
            int powers = PowerUps();
            // Hit it until a plate comes off: a third of each hit gets through.
            float dealt = 0;
            while (b.plates == 4) { game.DamageZombie(b, hit, 0, 1, 0); dealt += hit; }
            Assert.That(dealt * (1 - Tunables.BossArmor.Through), Is.EqualTo(quarter).Within(hit), "a plate a quarter of the armor");
            Assert.That(hp - b.hp, Is.EqualTo(dealt * Tunables.BossArmor.Through).Within(0.5f), "a third got through");
            Assert.That(b.plateObjects[0], Is.Null, "the first off is a shoulder pad");
            yield return Seconds(2.5f);
            Assert.That(PowerUps(), Is.EqualTo(powers + 1), "it left a power-up where it fell");

            // Blasts against armor count double.
            float before = b.armor;
            game.DamageZombie(b, 30, 0, 1, 0, blast: true);
            Assert.That(before - b.armor, Is.EqualTo(30 * (1 - Tunables.BossArmor.Through) * 2).Within(0.01f));

            // Everything else off: exposed, faster, and every hit counts in full.
            float speed = b.speed;
            while (b.plates > 0) game.DamageZombie(b, hit, 0, 1, 0);
            Assert.That(b.speed, Is.EqualTo(speed * Tunables.BossArmor.Enraged).Within(0.001f), "angrier");
            float h = b.hp;
            game.DamageZombie(b, 50, 0, 1, 0);
            Assert.That(h - b.hp, Is.EqualTo(50).Within(0.001f));
            yield return Seconds(2.5f);
            Assert.That(PowerUps(), Is.EqualTo(powers + 4), "four plates, four power-ups");
        }

        [UnityTest]
        public IEnumerator Pictures()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics), so nothing to see");
            var angles = new[] { 0.4f, Mathf.PI / 2, Mathf.PI, -Mathf.PI / 2 };
            var sheet = Studio.ZombieShots("boss", true, angles);
            Write(sheet, "boss-armored");
            Object.Destroy(sheet);
            sheet = Studio.ZombieShots("boss", false, angles);
            Write(sheet, "boss-plain");
            Object.Destroy(sheet);

            // In a run: close to the survivor, then a plate coming off.
            yield return Run();
            game.DamageZombie(Boss(), 1e6f, 0, 1, 0);
            yield return Seconds(0.3f);
            var b = Boss();
            b.x = game.player.x + 1.5f;
            b.z = game.player.z - 7;
            using var photo = new Photo(game);
            yield return Seconds(1.2f);
            photo.Shot(Path.Combine("armor", "in-a-run"));
            while (b.plates == 4) game.DamageZombie(b, 40, 0, 1, 0);
            yield return Seconds(0.15f);
            photo.Shot(Path.Combine("armor", "plate-off"));
            yield return Seconds(2.5f);
            photo.Shot(Path.Combine("armor", "power-up-left"));
        }
    }
}
