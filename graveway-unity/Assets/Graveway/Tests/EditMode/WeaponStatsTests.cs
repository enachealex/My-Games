// A gun's numbers with parts and perks, against weaponStats in the web
// version's config.js.

using System.Collections.Generic;
using NUnit.Framework;

namespace Graveway.Tests
{
    public class WeaponStatsTests
    {
        static WeaponStats Stats(string gun, string survivor = null, params string[] slotParts)
        {
            var fitted = new Dictionary<string, string>();
            foreach (var sp in slotParts)
            {
                var kv = sp.Split(':');
                fitted[kv[0]] = kv[1];
            }
            var perk = survivor != null ? Tunables.Character(survivor).perk : null;
            return WeaponStats.Of(Tunables.Weapon(gun), fitted, perk);
        }

        // A survivor's own gun is better in their hands; anyone else's isn't.
        [Test]
        public void TheirOwnGunIsBetterInTheirHands()
        {
            var juno = Tunables.Character("juno");
            var plain = new Weapon("launcher", null, null).stats;
            var hers = new Weapon("launcher", null, juno).stats;
            Assert.That(hers.damage, Is.EqualTo(plain.damage * 1.25f).Within(1e-3f));
            Assert.That(hers.fireRate, Is.EqualTo(plain.fireRate * 1.25f).Within(1e-4f));
            Assert.That(hers.reloadTime, Is.EqualTo(plain.reloadTime * 0.75f).Within(1e-4f));
            Assert.That(hers.blast.radius, Is.EqualTo(plain.blast.radius * 1.25f).Within(1e-4f));
            Assert.That(new Weapon("launcher", null, Tunables.Character("bram")).stats.damage, Is.EqualTo(plain.damage), "not Bram's");
            Assert.That(new Weapon("smg", null, Tunables.Character("wren")).stats.ammoCapacity, Is.EqualTo(37), "32, and 15%");
            var rook = new Weapon("pistol", null, Tunables.Character("rook")).stats;
            Assert.That(rook.damage, Is.EqualTo(22 * 1.1f).Within(1e-3f));
            Assert.That(rook.reloadTime, Is.EqualTo(0.6f).Within(1e-4f), "his perk, on any gun");
        }

        [Test]
        public void FactoryPistolIsItsDefinition()
        {
            var s = Stats("pistol");
            Assert.That(s.damage, Is.EqualTo(22));
            Assert.That(s.fireRate, Is.EqualTo(3.2f).Within(1e-5));
            Assert.That(s.spread, Is.EqualTo(0.03f).Within(1e-6));
            Assert.That(s.ammoCapacity, Is.EqualTo(12));
            Assert.That(s.reloadTime, Is.EqualTo(1f).Within(1e-5));
            Assert.That(s.range, Is.EqualTo(17));
            Assert.That(s.pellets, Is.EqualTo(1));
            Assert.That(s.pierce, Is.EqualTo(0));
        }

        [Test]
        public void RookReloadsFaster() =>
            Assert.That(Stats("pistol", "rook").reloadTime, Is.EqualTo(0.6f).Within(1e-5));

        [Test]
        public void LongBarrelReachesFurther()
        {
            var s = Stats("pistol", null, "barrel:long");
            Assert.That(s.range, Is.EqualTo(20.4f).Within(1e-4));
            Assert.That(s.spread, Is.EqualTo(0.0255f).Within(1e-6));
            Assert.That(s.fireRate, Is.EqualTo(3.04f).Within(1e-4));
        }

        [Test]
        public void SlugsMakeTheShotgunOneHeavyBall()
        {
            var s = Stats("shotgun", null, "ammo:slug");
            Assert.That(s.pellets, Is.EqualTo(1));
            Assert.That(s.damage, Is.EqualTo(88).Within(1e-3));
            Assert.That(s.pierce, Is.EqualTo(1));
            Assert.That(s.range, Is.EqualTo(14.3f).Within(1e-3));
            Assert.That(s.fx.props, Is.EqualTo(2));
        }

        [Test]
        public void HollowPointsNeverGoThrough()
        {
            var s = Stats("rifle", null, "ammo:hollow");
            Assert.That(s.pierce, Is.EqualTo(0));
            Assert.That(s.fx.small, Is.EqualTo(1.3f).Within(1e-5));
            Assert.That(s.fx.big, Is.EqualTo(0.8f).Within(1e-5));
        }

        [Test]
        public void DrumMagazineRoundsToWholeRounds()
        {
            var s = Stats("smg", null, "mag:drum");
            Assert.That(s.ammoCapacity, Is.EqualTo(70));            // 32 x 2.2 = 70.4
            Assert.That(s.reloadTime, Is.EqualTo(2.1f).Within(1e-4));
        }

        [Test]
        public void ConcussionGrenadesWidenTheBlastWithoutTouchingTheDefinition()
        {
            var s = Stats("launcher", null, "ammo:concussion");
            Assert.That(s.blast.radius, Is.EqualTo(4.59f).Within(1e-4));
            Assert.That(s.blast.impulse, Is.EqualTo(20).Within(1e-4));
            Assert.That(s.damage, Is.EqualTo(72).Within(1e-3));
            Assert.That(Tunables.Weapon("launcher").blast.radius, Is.EqualTo(3.4f).Within(1e-5));
        }

        [Test]
        public void PartsThatDontFitAreIgnored()
        {
            // The pistol takes no stock; a choke only fits the shotgun.
            var s = Stats("pistol", null, "stock:heavy", "muzzle:choke");
            Assert.That(s.spread, Is.EqualTo(0.03f).Within(1e-6));
            Assert.That(s.range, Is.EqualTo(17));
        }
    }
}
