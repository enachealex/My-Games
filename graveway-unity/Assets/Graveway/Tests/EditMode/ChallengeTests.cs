// Challenges in a run (Game.Challenges.cs): what each asks, in words, and how
// much more it asks each time it's done again in a run.

using NUnit.Framework;
using C = Graveway.Tunables.Challenges;

namespace Graveway.Tests
{
    public class ChallengeTests
    {
        static ChallengeDef Kind(string id) => System.Array.Find(C.Kinds, k => k.id == id);

        [Test]
        public void FourStatsDamagePayingLeast()
        {
            Assert.That(C.Stats, Is.EqualTo(new[] { "damage", "accuracy", "range", "rate" }));
            Assert.That(C.Steps, Is.EqualTo(new[] { 0.01f, 0.05f, 0.05f, 0.05f }), "1% damage, 5% the rest");
            Assert.That(C.Pay(C.Damage), Is.EqualTo("+1% damage"));
            Assert.That(C.Pay(C.Rate, 3), Is.EqualTo("+15% fire rate"));
            Assert.That(C.Active, Is.EqualTo(3));
            Assert.That(C.Kinds.Length, Is.GreaterThanOrEqualTo(C.Active + 1), "always a different one to offer");
        }

        [Test]
        public void TwiceAsMuchEachTime()
        {
            Assert.That(C.Target(Kind("kills"), 0), Is.EqualTo(30));
            Assert.That(C.Target(Kind("kills"), 1), Is.EqualTo(60));
            Assert.That(C.Target(Kind("kills"), 2), Is.EqualTo(120));
            Assert.That(C.Target(Kind("tanks"), 0), Is.EqualTo(1));
            Assert.That(C.Target(Kind("tanks"), 2), Is.EqualTo(4));
            Assert.That(C.Target(Kind("blasts"), 2), Is.EqualTo(32));
            Assert.That(C.Target(Kind("untouched"), 1), Is.EqualTo(300));
            Assert.That(C.Target(Kind("streak"), 2), Is.EqualTo(20));
            Assert.That(C.Target(Kind("streak"), 3), Is.EqualTo(20), "40 in 3 seconds would be luck");
        }

        [Test]
        public void SaysWhatItAsks()
        {
            Assert.That(C.Describe("tanks", 1), Is.EqualTo("Down a tank"));
            Assert.That(C.Describe("tanks", 2), Is.EqualTo("Down 2 tanks"));
            Assert.That(C.Describe("streak", 5), Is.EqualTo("Down 5 in 3 seconds"));
            Assert.That(C.Describe("untouched", 150), Is.EqualTo("Go 150 m without a scratch"));
            foreach (var k in C.Kinds) Assert.That(C.Describe(k.id, k.target), Is.Not.EqualTo(k.id), $"{k.id} has words");
        }
    }
}
