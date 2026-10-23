// The scoreboard on the device: each run banked goes on it under the last
// name used, best first, the best 50 kept; runs with playtest tools on
// stay off it.

using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Graveway.Tests
{
    public class ScoreTests
    {
        const string Key = "graveway.save.scoretests";

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

        static RunRecord Run(int distance, int kills = 0, bool board = true) =>
            new RunRecord { distance = distance, kills = kills, coins = 5, character = "rook", time = 60, board = board };

        [Test]
        public void RunsGoOnBestFirst()
        {
            Save.Bank(Run(100));
            var first = Save.LastScore;
            Save.Bank(Run(300));
            Save.Bank(Run(200, 5));
            Save.Bank(Run(200, 9));
            var s = Save.Data.scores;
            Assert.That(s.Select(x => x.distance), Is.EqualTo(new[] { 300, 200, 200, 100 }));
            Assert.That(s[1].kills, Is.EqualTo(9), "level on distance: more kills first");
            Assert.That(Save.Rank(first), Is.EqualTo(4));
            Assert.That(s.Select(x => x.id), Is.Unique);
        }

        [Test]
        public void UnderTheLastNameUsed()
        {
            Save.Bank(Run(50));
            Assert.That(Save.LastScore.name, Is.Empty, "no name until one's given");
            Save.NameScore(Save.LastScore, "  Sam   the\tGreat  ");
            Assert.That(Save.LastScore.name, Is.EqualTo("Sam the Great"));
            Save.Bank(Run(60));
            Assert.That(Save.LastScore.name, Is.EqualTo("Sam the Great"), "the next run goes under it too");
            Save.NameScore(Save.LastScore, "Abcdefghijklmnopqrstuvwxyz");
            Assert.That(Save.LastScore.name, Is.EqualTo("Abcdefghijklmnop"));
            Assert.That(Save.RecentNames(), Is.EqualTo(new[] { "Abcdefghijklmnop", "Sam the Great" }));
        }

        [Test]
        public void PlaytestRunsAndGoingNowhereStayOff()
        {
            Save.Bank(Run(500, board: false));
            Assert.That(Save.LastScore, Is.Null);
            Assert.That(Save.Data.runs, Is.EqualTo(1), "banked all the same");
            Save.Bank(Run(0));
            Assert.That(Save.LastScore, Is.Null);
            Assert.That(Save.Data.scores, Is.Empty);
        }

        [Test]
        public void OnlyTheBestAreKept()
        {
            for (int i = 1; i <= Save.KeepScores + 5; i++) Save.Bank(Run(i * 10));
            Assert.That(Save.Data.scores.Count, Is.EqualTo(Save.KeepScores));
            Assert.That(Save.Data.scores.Last().distance, Is.EqualTo(60));
            Save.Bank(Run(5));
            Assert.That(Save.LastScore, Is.Null, "not good enough for the board");
        }

        [Test]
        public void KeptAcrossRestarts()
        {
            Save.Bank(Run(123, 4));
            Save.NameScore(Save.LastScore, "Jo");
            Save.Reload();
            var s = Save.Data.scores.Single();
            Assert.That((s.name, s.distance, s.kills, s.character), Is.EqualTo(("Jo", 123, 4, "rook")));
            Assert.That(s.id, Is.Not.Empty);
            Assert.That(Save.Data.playerName, Is.EqualTo("Jo"));
        }
    }
}
