// The derived numbers in Tunables, against the web version's config.js.

using NUnit.Framework;
using D = Graveway.Tunables.Director;

namespace Graveway.Tests
{
    public class TunablesTests
    {
        [Test]
        public void DirectorCurve()
        {
            Assert.That(D.MaxActive(0), Is.EqualTo(20));
            Assert.That(D.MaxActive(9), Is.EqualTo(64));
            Assert.That(D.MaxActive(30), Is.EqualTo(64));
            Assert.That(D.HordeEvery(0), Is.EqualTo(24).Within(1e-4));
            Assert.That(D.HordeEvery(10), Is.EqualTo(12).Within(1e-4));
            Assert.That(D.HordeEvery(20), Is.EqualTo(12).Within(1e-4));
            Assert.That(D.HordeMin(10), Is.EqualTo(8));
            Assert.That(D.HordeMax(10), Is.EqualTo(13));
            Assert.That(D.TrickleEvery(0), Is.EqualTo(3.5f).Within(1e-4));
            Assert.That(D.TrickleEvery(11), Is.EqualTo(0.8f).Within(1e-4));
            Assert.That(D.RunnerShare(1), Is.EqualTo(0.12f).Within(1e-4));
            Assert.That(D.RunnerShare(20), Is.EqualTo(0.35f).Within(1e-4));
            Assert.That(D.TankShare(9), Is.EqualTo(0.16f).Within(1e-4));
            Assert.That(D.StageBonus(5), Is.EqualTo(35));
        }

        [Test]
        public void StageScaling()
        {
            var s = Tunables.StageScaling(10);
            Assert.That(s.hp, Is.EqualTo(2.5f).Within(1e-4));
            Assert.That(s.damage, Is.EqualTo(1.5f).Within(1e-4));
            Assert.That(s.speed, Is.EqualTo(1.25f).Within(1e-4));
            Assert.That(Tunables.StageScaling(40).speed, Is.EqualTo(1.45f).Within(1e-4));
        }

        // Guns cost twice what they do in the web version (playtest, 22 October
        // 2026); parts cost the same.
        [Test]
        public void GunsCostTwiceTheWebVersions()
        {
            var web = new (string id, int price)[] { ("pistol", 0), ("smg", 350), ("shotgun", 750), ("rifle", 1500), ("launcher", 2200), ("minigun", 3200) };
            foreach (var (id, price) in web) Assert.That(Tunables.Weapon(id).price, Is.EqualTo(price * 2), id);
        }

        // Every part's price on every gun, from the web version's partCost.
        const string WebPartCosts =
            "pistol|barrel|long|250 pistol|barrel|short|200 pistol|barrel|heavy|400 pistol|muzzle|comp|200 pistol|muzzle|brake|300 " +
            "pistol|muzzle|suppressor|350 pistol|mag|extended|250 pistol|mag|drum|450 pistol|mag|quick|300 pistol|sight|reddot|200 " +
            "pistol|sight|holo|300 pistol|sight|scope|450 pistol|trigger|hair|300 pistol|trigger|match|300 pistol|grip|rubber|150 " +
            "pistol|grip|stippled|250 pistol|grip|ergo|250 pistol|rail|laser|300 pistol|rail|light|200 pistol|ammo|hollow|300 " +
            "pistol|ammo|ap|400 pistol|ammo|incendiary|450 smg|barrel|long|340 smg|barrel|short|270 smg|barrel|heavy|540 " +
            "smg|muzzle|comp|270 smg|muzzle|brake|405 smg|muzzle|suppressor|475 smg|mag|extended|340 smg|mag|drum|610 " +
            "smg|mag|quick|405 smg|sight|reddot|270 smg|sight|holo|405 smg|sight|scope|610 smg|stock|light|270 smg|stock|heavy|405 " +
            "smg|stock|precision|475 smg|trigger|hair|405 smg|trigger|match|405 smg|grip|rubber|205 smg|grip|stippled|340 " +
            "smg|grip|ergo|340 smg|rail|foregrip|340 smg|rail|angled|405 smg|rail|laser|405 smg|rail|light|270 smg|ammo|hollow|405 " +
            "smg|ammo|ap|540 smg|ammo|incendiary|610 shotgun|barrel|long|425 shotgun|barrel|short|340 shotgun|barrel|heavy|680 " +
            "shotgun|muzzle|brake|510 shotgun|muzzle|suppressor|595 shotgun|muzzle|choke|425 shotgun|mag|extended|425 " +
            "shotgun|mag|drum|765 shotgun|mag|quick|510 shotgun|sight|reddot|340 shotgun|sight|holo|510 shotgun|sight|scope|765 " +
            "shotgun|stock|light|340 shotgun|stock|heavy|510 shotgun|stock|precision|595 shotgun|trigger|hair|510 " +
            "shotgun|trigger|match|510 shotgun|grip|rubber|255 shotgun|grip|stippled|425 shotgun|grip|ergo|425 " +
            "shotgun|rail|foregrip|425 shotgun|rail|angled|510 shotgun|rail|laser|510 shotgun|rail|light|340 shotgun|ammo|slug|510 " +
            "shotgun|ammo|dragon|765 shotgun|ammo|flechette|595 rifle|barrel|long|515 rifle|barrel|short|410 rifle|barrel|heavy|820 " +
            "rifle|muzzle|comp|410 rifle|muzzle|brake|615 rifle|muzzle|suppressor|715 rifle|mag|extended|515 rifle|mag|drum|920 " +
            "rifle|mag|quick|615 rifle|sight|reddot|410 rifle|sight|holo|615 rifle|sight|scope|920 rifle|stock|light|410 " +
            "rifle|stock|heavy|615 rifle|stock|precision|715 rifle|trigger|hair|615 rifle|trigger|match|615 rifle|grip|rubber|310 " +
            "rifle|grip|stippled|515 rifle|grip|ergo|515 rifle|rail|foregrip|515 rifle|rail|angled|615 rifle|rail|laser|615 " +
            "rifle|rail|light|410 rifle|ammo|hollow|615 rifle|ammo|ap|820 rifle|ammo|incendiary|920 launcher|barrel|long|600 " +
            "launcher|barrel|short|480 launcher|barrel|heavy|960 launcher|mag|extended|600 launcher|mag|quick|720 " +
            "launcher|sight|reddot|480 launcher|sight|holo|720 launcher|sight|scope|1080 launcher|stock|light|480 " +
            "launcher|stock|heavy|720 launcher|stock|precision|840 launcher|trigger|hair|720 launcher|trigger|match|720 " +
            "launcher|grip|rubber|360 launcher|grip|stippled|600 launcher|grip|ergo|600 launcher|rail|foregrip|600 " +
            "launcher|rail|angled|720 launcher|rail|laser|720 launcher|rail|light|480 launcher|ammo|incendiary-g|1080 " +
            "launcher|ammo|cluster|1320 launcher|ammo|concussion|720 minigun|barrel|long|690 minigun|barrel|short|550 " +
            "minigun|barrel|heavy|1100 minigun|mag|extended|690 minigun|mag|drum|1240 minigun|mag|quick|825 " +
            "minigun|sight|reddot|550 minigun|sight|holo|825 minigun|sight|scope|1240 minigun|trigger|hair|825 " +
            "minigun|trigger|match|825 minigun|grip|rubber|415 minigun|grip|stippled|690 minigun|grip|ergo|690 " +
            "minigun|rail|foregrip|690 minigun|rail|angled|825 minigun|rail|laser|825 minigun|rail|light|550 " +
            "minigun|ammo|hollow|825 minigun|ammo|ap|1100 minigun|ammo|incendiary|1240";

        [Test]
        public void PartPricesMatchTheWebVersion()
        {
            var rows = WebPartCosts.Split(' ');
            Assert.That(rows.Length, Is.EqualTo(147));
            int priced = 0;
            foreach (var w in Tunables.Weapons)
                foreach (var slot in Tunables.PartSlots.Keys)
                    foreach (var p in Tunables.PartsFor(w, slot))
                        if (p.price > 0) priced++;
            Assert.That(priced, Is.EqualTo(rows.Length), "the parts each gun can take differ from the web version");
            foreach (var row in rows)
            {
                var f = row.Split('|');
                var gun = Tunables.Weapon(f[0]);
                var part = Tunables.PartsFor(gun, f[1]).Find(p => p.id == f[2]);
                Assert.That(part, Is.Not.Null, row);
                Assert.That(Tunables.PartCost(gun, part), Is.EqualTo(int.Parse(f[3])), row);
            }
        }
    }
}
