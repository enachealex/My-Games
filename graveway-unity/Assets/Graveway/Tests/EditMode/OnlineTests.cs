// The online scoreboard's client side (Online.cs): how a run is sent, how a
// board is read, and which runs wait to go. The server's own checks are in
// graveway-unity/Server/scoreboard-test.sql.

using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Graveway.Tests
{
    public class OnlineTests
    {
        const string Key = "graveway.save.onlinetests";

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

        static RunRecord Run(int distance, int time = 120) =>
            new RunRecord { distance = distance, kills = 30, coins = 5, character = "wren", time = time, board = true };

        // The same as Postgres's left(md5(id::text), 12), whichever way the
        // phone wrote the id (it writes them without dashes).
        [Test]
        public void HashesAsTheServerDoes()
        {
            Assert.That(Online.Hash("00000000-0000-4000-8000-000000000002"), Is.EqualTo("9953167bc50f"));
            Assert.That(Online.Hash("5f3a9c2e1b4d4e8a9c7f2d6e8b1a4c3f"), Is.EqualTo("47be601e8e68"));
            Assert.That(Online.Hash("not an id"), Is.Empty);
        }

        [Test]
        public void SendsARunAsTheServerTakesIt()
        {
            Save.Bank(Run(345, 101));
            var s = Save.LastScore;
            Save.NameScore(s, "Jo");
            var o = JObject.Parse(Online.Body(s, dryRun: true));
            Assert.That((string)o["p_id"], Does.Match("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"));
            Assert.That((string)o["p_name"], Is.EqualTo("Jo"));
            Assert.That((string)o["p_survivor"], Is.EqualTo("wren"));
            Assert.That(((int)o["p_distance"], (int)o["p_kills"], (int)o["p_seconds"]), Is.EqualTo((345, 30, 101)));
            Assert.That((bool)o["p_dry_run"]);
        }

        [Test]
        public void ReadsABoard()
        {
            var b = Online.ParseBoard(@"{""country"": ""US"", ""region"": ""US"", ""scores"": [
                {""r"": 1, ""n"": ""Alex"", ""s"": ""bram"", ""d"": 1200, ""k"": 500, ""t"": 400, ""p"": 1, ""c"": ""US"", ""h"": ""9953167bc50f"", ""at"": ""2026-10-23T10:00:00Z""},
                {""r"": 2, ""n"": """", ""s"": ""wren"", ""d"": 900, ""k"": 300, ""t"": 240, ""p"": 0, ""c"": ""US"", ""h"": ""47be601e8e68"", ""at"": ""2026-10-23T10:00:00Z""}]}");
            Assert.That((b.country, b.region), Is.EqualTo(("US", "US")));
            Assert.That(b.scores.Count, Is.EqualTo(2));
            Assert.That((b.scores[0].n, b.scores[0].d, b.scores[0].p, b.scores[0].h), Is.EqualTo(("Alex", 1200, 1, "9953167bc50f")));
            Assert.That(b.scores[1].n, Is.Empty);
            var world = Online.ParseBoard(@"{""country"": """", ""region"": null, ""scores"": []}");
            Assert.That(world.region, Is.Null);
            Assert.That(world.scores, Is.Empty);
        }

        // Every run banked waits to go; once sent it doesn't, until it's renamed.
        // One the server refused never goes again. A run of no time at all
        // (before runs were timed) can't be sent.
        [Test]
        public void RunsWaitUntilTheyreSent()
        {
            Save.Bank(Run(200));
            var a = Save.LastScore;
            Save.Bank(Run(300));
            var b = Save.LastScore;
            Save.Bank(Run(100, time: 0));
            Assert.That(Online.Waiting(), Is.EquivalentTo(new[] { a, b }));
            a.sent = true;
            Assert.That(Online.Waiting(), Is.EqualTo(new[] { b }));
            Save.NameScore(a, "Sam");
            Assert.That(Online.Waiting(), Has.Member(a), "renamed: the new name goes too");
            b.sent = b.refused = true;
            Save.NameScore(b, "Sam");
            Assert.That(Online.Waiting(), Has.No.Member(b), "refused stays refused");
            Save.Persist();
            Save.Reload();
            Assert.That(Online.Waiting().Count, Is.EqualTo(1), "kept across restarts");
        }
    }
}
