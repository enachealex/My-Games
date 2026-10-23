// The online scoreboard for real, against the home server through Cloudflare:
// a run sent (as a dry run: checked and ranked, never kept) and the boards
// read, and the scoreboard's tabs showing them. Skipped without the online
// settings (Graveway → Set up project copies them in) or a network.

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Graveway.Tests.TestUi;

namespace Graveway.Tests
{
    public class OnlineBoardTests
    {
        const float Step = 1f / 20;
        Game game;
        VisualElement ui;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!Online.Configured) Assert.Ignore("no online settings in this project (%USERPROFILE%\\.graveway\\online.json)");
            Assert.That(Online.DryRun, "the editor only ever sends dry runs");
            Save.UseKey("graveway.save.tests");
            Save.Reset();
            Save.Data.settings.online = true;
            AudioListener.volume = 0;
            game = Object.FindAnyObjectByType<Game>();
            for (int i = 0; i < 5 && game == null; i++)
            {
                yield return null;
                game = Object.FindAnyObjectByType<Game>();
            }
            Assert.That(game, Is.Not.Null, "the game didn't boot itself");
            Sfx.Enabled = false;
            Time.captureDeltaTime = Step;
            game.ShowMenu();
            game.hud.ShowMenu();
            ui = Root(game);
            yield return null;
        }

        [TearDown]
        public void TearDown() => Time.captureDeltaTime = 0;

        [UnityTest]
        public IEnumerator SendsARunAndReadsTheBoards()
        {
            Save.Bank(new RunRecord { distance = 420, kills = 90, coins = 30, character = "rook", time = 180, board = true });
            var s = Save.LastScore;
            Save.NameScore(s, "Test");
            yield return Online.SendWaiting();
            if (!s.sent) Assert.Ignore("couldn't reach the scoreboard (no network?)");
            Assert.That(s.refused, Is.False);
            Assert.That(s.world, Is.GreaterThanOrEqualTo(1), "placed in the world");
            Assert.That(s.country, Does.Match("^([A-Z]{2})?$"));
            if (s.country.Length > 0) Assert.That(s.region, Is.GreaterThanOrEqualTo(1), "and in its country");

            Online.Board world = null, region = null;
            string problem = null;
            yield return Online.Fetch("world", (b, p) => { world = b; problem = p; });
            Assert.That(world, Is.Not.Null, problem);
            Assert.That(world.region, Is.Null);
            yield return Online.Fetch("region", (b, p) => { region = b; problem = p; });
            Assert.That(region, Is.Not.Null, problem);
            Assert.That(region.region ?? "", Is.EqualTo(s.country), "the board for where it was asked from");
            foreach (var r in region.scores) Assert.That(r.c, Is.EqualTo(s.country));
        }

        // The scoreboard's tabs: this phone, the world, the country; each
        // showing runs or saying there are none yet, never an error.
        [UnityTest]
        public IEnumerator TheScoreboardShowsTheBoards()
        {
            Save.Bank(new RunRecord { distance = 260, kills = 40, coins = 10, character = "bram", time = 120, board = true });
            Press(Find<Button>(ui, b => b.ClassListContains("m-best"), "the best run card"));
            yield return null;
            using var photo = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null ? new Photo(game) : null;
            foreach (var tab in new[] { "World", "This phone" })
            {
                Press(Find<Button>(ui, b => b.ClassListContains("tab") && Says(b, tab), $"the {tab} tab"));
                for (float t = 0; t < 10 && ui.Query<Label>().ToList().Exists(l => Visible(l) && l.text.StartsWith("Fetching")); t += Step) yield return null;
                yield return null;
                Assert.That(ui.Query<Label>().ToList().Exists(l => Visible(l) && (l.text.StartsWith("Couldn't") || l.text.Contains("isn't answering"))), Is.False,
                    $"the {tab} board came back");
                Assert.That(ui.Query<VisualElement>(className: "sc-row").ToList().Exists(Visible)
                            || ui.Query<Label>().ToList().Exists(l => Visible(l) && l.text.StartsWith("No runs")), $"the {tab} board shows something");
                photo?.Shot($"22-scoreboard-{tab.Replace(' ', '-').ToLowerInvariant()}");
            }
            var country = ui.Query<Button>(className: "tab").ToList().Find(b => Visible(b) && !Says(b, "World") && !Says(b, "This phone"));
            Press(country);
            for (float t = 0; t < 10 && ui.Query<Label>().ToList().Exists(l => Visible(l) && l.text.StartsWith("Fetching")); t += Step) yield return null;
            yield return null;
            photo?.Shot("22-scoreboard-country");
        }
    }
}
