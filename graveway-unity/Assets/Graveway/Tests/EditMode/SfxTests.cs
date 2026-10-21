// Every sound recipe from graveway/src/audio.js renders to a clean clip:
// numbers all through, audible, and never past full scale.

using NUnit.Framework;
using UnityEngine;

namespace Graveway.Tests
{
    public class SfxTests
    {
        // The sfx names in audio.js, with each gun model's shot and both groans.
        static readonly string[] Names =
        {
            "shot:pistol", "shot:launcher", "shot:smg", "shot:shotgun", "shot:rifle", "shot:minigun",
            "hit", "die", "groan", "groan:big", "coin", "pickup", "powerup", "shock", "toss", "hurt",
            "reload", "swap", "click", "buy", "denied", "win", "lose", "crumble", "boom", "bigBoom",
            "thunk", "ignite", "clang", "ricochet", "chip", "rumble", "collapse", "milestone", "slam",
        };

        [Test]
        public void HasEveryRecipe()
        {
            CollectionAssert.AreEquivalent(Names, Sfx.Recipes);
        }

        [TestCaseSource(nameof(Names))]
        public void RendersCleanly(string name)
        {
            int n = Sfx.Variants(name);
            Assert.That(n, Is.GreaterThan(0));
            for (int v = 0; v < n; v++)
            {
                var data = Sfx.Render(name, v);
                Assert.That(data, Is.Not.Null.And.Not.Empty, $"{name} #{v}");
                float peak = 0;
                int bad = -1;
                for (int i = 0; i < data.Length; i++)
                {
                    if (float.IsNaN(data[i]) || float.IsInfinity(data[i])) { bad = i; break; }
                    peak = Mathf.Max(peak, Mathf.Abs(data[i]));
                }
                Assert.That(bad, Is.EqualTo(-1), $"{name} #{v}: not a number at sample {bad}");
                Assert.That(peak, Is.GreaterThan(0.01f).And.LessThanOrEqualTo(1f), $"{name} #{v}");
            }
        }

        [Test]
        public void TakesComeOutTheSame()
        {
            CollectionAssert.AreEqual(Sfx.Render("boom", 1), Sfx.Render("boom", 1));
        }

        [Test]
        public void PanReachesFullyOverAtTenMetres()
        {
            Assert.That(Sfx.Pan(5, 0), Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(Sfx.Pan(-25, 0), Is.EqualTo(-1f));
            Assert.That(Sfx.Pan(3, 3), Is.EqualTo(0f));
        }
    }
}
