// Rng must give the same numbers as the web version's mulberry32
// (graveway/src/world.js), so a run seed lays out the same road in both.

using NUnit.Framework;

namespace Graveway.Tests
{
    public class RngTests
    {
        // From the web version: mulberry32(seed) called five times.
        [TestCase(7u, new[] { 0.011704753, 0.061958258, 0.976907633, 0.699028706, 0.521445269 })]
        [TestCase(23456u, new[] { 0.383293553, 0.401646798, 0.722146230, 0.884692494, 0.464613765 })]
        public void MatchesTheWebVersion(uint seed, double[] expected)
        {
            var rng = new Rng(seed);
            foreach (var e in expected) Assert.That(rng.Next(), Is.EqualTo(e).Within(1e-6));
        }

        [Test]
        public void NeverReturnsOne()
        {
            Assert.That(Rng.ToUnit(uint.MaxValue), Is.LessThan(1f));
            Assert.That(Rng.ToUnit(0xFFFFFF80u), Is.LessThan(1f));
            Assert.That(Rng.ToUnit(0), Is.EqualTo(0f));
            Assert.That(Rng.ToUnit(0x80000000u), Is.EqualTo(0.5f));
        }

        [Test]
        public void PickStaysInTheArray()
        {
            var rng = new Rng(1);
            var items = new[] { 1, 2, 3 };
            for (int i = 0; i < 10000; i++) CollectionAssert.Contains(items, rng.Pick(items));
        }
    }
}
