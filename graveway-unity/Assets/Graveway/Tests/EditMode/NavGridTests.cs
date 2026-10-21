// The zombies' flow field (NavGrid.cs, from nav.js): distances on open
// road, routing round a wall through its gap, rubble costing more, broken
// props no longer blocking, and the grid sliding along with the survivor.

using System.Collections.Generic;
using NUnit.Framework;

namespace Graveway.Tests
{
    public class NavGridTests
    {
        const float Pz = -40;                   // the survivor, some way up the road (-z)
        NavGrid nav;
        List<Prop> props;
        List<RoughArea> roughs;

        [SetUp]
        public void SetUp()
        {
            nav = new NavGrid();
            nav.Reset(Pz);
            props = new List<Prop>();
            roughs = new List<RoughArea>();
        }

        // The grid counts in cells; this is metres.
        float Metres(float x, float z) => nav.DistanceAt(x, z) * NavGrid.Cell;

        void Build()
        {
            nav.dirty = true;
            nav.Update(1, 0, Pz, props, roughs);
        }

        // A wall across the road from the left kerb to x = 3.4, 8 m ahead.
        Prop Wall()
        {
            var p = new Prop { kind = "wall", x = -2, z = Pz - 8, hw = 5.4f, hd = 0.3f, cos = 1, sin = 0 };
            props.Add(p);
            return p;
        }

        [Test]
        public void OpenRoadIsAsTheCrowFlies()
        {
            Build();
            Assert.That(Metres(0, Pz), Is.LessThan(0.5f));
            Assert.That(Metres(0, Pz - 10), Is.EqualTo(10).Within(0.6f));
            Assert.That(nav.Steer(0, Pz - 10, out float ox, out float oz));
            Assert.That(oz, Is.GreaterThan(0.9f), "straight back down the road, toward the survivor");
            Assert.That(ox, Is.EqualTo(0).Within(0.25f), "give or take where it stands in its cell");
        }

        [Test]
        public void ZombiesGoRoundAWallThroughItsGap()
        {
            Wall();
            Build();
            Assert.That(nav.IsBlocked(-2, Pz - 8));
            Assert.That(nav.IsBlocked(5.5f, Pz - 8), Is.False, "the gap");
            Assert.That(nav.Clear(-2, Pz - 12, -2, Pz), Is.False);
            Assert.That(nav.Clear(5.5f, Pz - 12, 5.5f, Pz));
            Assert.That(Metres(-2, Pz - 12), Is.GreaterThan(14), "the long way round");
            // Walk one there: it has to cross the wall's line through the gap.
            float x = -2, z = Pz - 12, crossedAt = float.NaN;
            for (int i = 0; i < 400 && x * x + (z - Pz) * (z - Pz) > 1; i++)
            {
                Assert.That(nav.Steer(x, z, out float ox, out float oz), $"lost at ({x:0.0}, {z:0.0})");
                if (z < Pz - 8 && z + oz * 0.2f >= Pz - 8) crossedAt = x;
                x += ox * 0.2f;
                z += oz * 0.2f;
            }
            Assert.That(crossedAt, Is.GreaterThan(3.4f), "went through the gap");
            Assert.That(x * x + (z - Pz) * (z - Pz), Is.LessThanOrEqualTo(1), "and reached the survivor");
        }

        [Test]
        public void RubbleCostsMoreToCross()
        {
            Build();
            float open = Metres(0, Pz - 10);
            roughs.Add(new RoughArea { x = 0, z = Pz - 5, hw = 7.4f, hd = 1, cos = 1, sin = 0 });
            Build();
            Assert.That(nav.IsRough(0, Pz - 5));
            Assert.That(Metres(0, Pz - 10), Is.GreaterThan(open + 3), "2 m of rubble at three times the cost");
        }

        [Test]
        public void BrokenPropsStopBlocking()
        {
            var wall = Wall();
            Build();
            Assert.That(nav.IsBlocked(-2, Pz - 8));
            wall.destroyed = true;
            Build();
            Assert.That(nav.IsBlocked(-2, Pz - 8), Is.False);
            Assert.That(Metres(-2, Pz - 12), Is.LessThan(13));
        }

        [Test]
        public void TheGridSlidesAlongWithTheSurvivor()
        {
            Build();
            int builds = nav.builds;
            float z0 = nav.z0;
            nav.Update(1, 0, Pz - 10, props, roughs);
            Assert.That(nav.builds, Is.EqualTo(builds + 1), "rebuilt once the survivor is 6 m off centre");
            Assert.That(nav.z0, Is.EqualTo(z0 - 10).Within(0.01f));
            Assert.That(Metres(0, Pz - 10), Is.LessThan(0.5f));
        }
    }
}
