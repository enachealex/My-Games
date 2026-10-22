// The Black Market's items as designed (PORT_PLAN.md, playtest note 4), and
// the backpack they go in.

using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Graveway.Tests
{
    public class BlackMarketTests
    {
        [Test]
        public void ItemsAndPricesAsDesigned()
        {
            var want = new (string id, int price)[] { ("strafe", 120), ("cluster", 150), ("napalm", 180), ("backup", 200), ("heli", 250), ("skipboss", 300) };
            Assert.That(Tunables.Items.Length, Is.EqualTo(want.Length));
            foreach (var (id, price) in want)
            {
                var it = Tunables.Item(id);
                Assert.That(it, Is.Not.Null, id);
                Assert.That(it.price, Is.EqualTo(price), id);
                Assert.That(it.delay, id == "skipboss" ? Is.EqualTo(0) : Is.GreaterThanOrEqualTo(3), "none of them are immediate");
            }
            Assert.That(Tunables.BackpackSize, Is.EqualTo(6));
            Assert.That(Tunables.Support.HeliTime, Is.EqualTo(30));
        }

        [Test]
        public void TheBackpackIsKeptAndTidied()
        {
            const string key = "graveway.save.bmtests";
            void Clean()
            {
                if (Directory.Exists(Save.Folder)) foreach (var f in Directory.GetFiles(Save.Folder, key + "*")) File.Delete(f);
                PlayerPrefs.DeleteKey(key);
            }
            Clean();
            try
            {
                Save.UseKey(key);
                Save.Data.backpack.AddRange(new[] { "heli", "strafe" });
                Save.Persist();
                Save.Reload();
                Assert.That(Save.Data.backpack, Is.EqualTo(new[] { "heli", "strafe" }));

                // Something that's no longer sold goes; so does anything past six.
                Save.Data.backpack.Clear();
                Save.Data.backpack.AddRange(new[] { "nuke", "strafe", "strafe", "cluster", "cluster", "napalm", "heli", "backup" });
                Save.Persist();
                Save.Reload();
                Assert.That(Save.Data.backpack.Count, Is.EqualTo(6));
                Assert.That(Save.Data.backpack, Does.Not.Contain("nuke"));
            }
            finally
            {
                Clean();
                Save.UseKey("graveway.save.tests");
            }
        }
    }
}
