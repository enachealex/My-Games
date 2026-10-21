// Progress kept on the device (Save.cs): written straight to its file,
// never lost to a damaged file or a half-finished write, carried over from
// where older versions kept it, and a run the app closed in the middle of
// banked the next time it starts.

using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Graveway.Tests
{
    public class SaveTests
    {
        const string Key = "graveway.save.edittests";

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

        [Test]
        public void ProgressIsInItsFileAtOnce()
        {
            Save.Data.coins = 123;
            Save.Data.owned.Add("smg");
            Save.Persist();
            Assert.That(File.Exists(Main));
            Save.Reload();
            Assert.That(Save.Data.coins, Is.EqualTo(123));
            Assert.That(Save.Data.owned, Does.Contain("smg"));
        }

        [Test]
        public void TheSaveBeforeIsKept()
        {
            Save.Data.coins = 1;
            Save.Persist();
            Save.Data.coins = 2;
            Save.Persist();
            StringAssert.Contains("\"coins\":1", File.ReadAllText(Main + ".bak"));
            StringAssert.Contains("\"coins\":2", File.ReadAllText(Main));
            Assert.That(File.Exists(Main + ".tmp"), Is.False);
        }

        [Test]
        public void ADamagedFileFallsBackAndIsKeptAside()
        {
            Save.Data.coins = 5;
            Save.Persist();
            Save.Data.coins = 6;
            Save.Persist();
            File.WriteAllText(Main, "{\"coins\": 6, \"bes");          // cut off mid-write
            Save.Reload();
            Assert.That(Save.Data.coins, Is.EqualTo(5), "the save before");
            Assert.That(Directory.GetFiles(Save.Folder, Path.GetFileName(Main) + ".unreadable-*"), Is.Not.Empty, "the damaged one kept");
            Save.Persist();
            Save.Reload();
            Assert.That(Save.Data.coins, Is.EqualTo(5));
        }

        [Test]
        public void ASaveStillBeingSwappedInIsPickedUp()
        {
            Save.Data.coins = 9;
            Save.Persist();
            File.Move(Main, Main + ".tmp");                          // the app died between the two moves
            Save.Reload();
            Assert.That(Save.Data.coins, Is.EqualTo(9));
        }

        [Test]
        public void TheOldPlayerPrefsSaveComesAcross()
        {
            PlayerPrefs.SetString(Key, "{\"coins\": 77, \"best\": 410, \"owned\": [\"pistol\", \"rifle\"]}");
            Save.Reload();
            Assert.That(Save.Data.coins, Is.EqualTo(77));
            Assert.That(Save.Data.best, Is.EqualTo(410));
            Assert.That(Save.Data.owned, Does.Contain("rifle"));
            Assert.That(File.Exists(Main), "moved into its file");
        }

        [Test]
        public void ARunCutShortIsBankedNextTime()
        {
            Save.Data.coins = 100;
            Save.Data.best = 300;
            Save.Running(new RunRecord { coins = 42, kills = 7, distance = 512 });
            Save.Reload();                                           // the app starts again
            var r = Save.BankUnfinished();
            Assert.That(r, Is.Not.Null);
            Assert.That(Save.Data.coins, Is.EqualTo(142));
            Assert.That(Save.Data.kills, Is.EqualTo(7));
            Assert.That(Save.Data.runs, Is.EqualTo(1));
            Assert.That(Save.Data.best, Is.EqualTo(512));
            Assert.That(Save.Data.running, Is.Null);
            Save.Reload();
            Assert.That(Save.BankUnfinished(), Is.Null, "only once");
            Assert.That(Save.Data.coins, Is.EqualTo(142));
        }

        [Test]
        public void ARunStartedFurtherUpSetsNoRecord()
        {
            Save.Data.best = 300;
            Assert.That(Save.Bank(new RunRecord { coins = 10, distance = 900, records = false }), Is.False);
            Assert.That(Save.Data.best, Is.EqualTo(300));
            Assert.That(Save.Data.coins, Is.EqualTo(10));
        }

        [Test]
        public void NotesHaveTheirOwnFile()
        {
            Save.Notes.Add(new Note { at = "2026-10-21T12:00:00.000Z", text = "keep me" });
            Save.PersistNotes();
            Save.Reset();
            Save.Reload();
            Assert.That(Save.Notes.Count, Is.EqualTo(1));
            Assert.That(Save.Notes[0].text, Is.EqualTo("keep me"));
        }
    }
}
