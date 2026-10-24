// Challenges in a real run: done by playing, they pay their bonus at once,
// another takes their place, being hit starts "without a scratch" over, and a
// new run starts at normal. With a GPU, a picture of the panel.

using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Graveway.Tests.TestUi;
using C = Graveway.Tunables.Challenges;

namespace Graveway.Tests
{
    public class ChallengeRunTests
    {
        const float Step = 1f / 20;
        Game game;
        VisualElement ui;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Save.UseKey("graveway.save.tests");
            Save.Reset();
            game = Object.FindAnyObjectByType<Game>();
            for (int i = 0; i < 5 && game == null; i++)
            {
                yield return null;
                game = Object.FindAnyObjectByType<Game>();
            }
            Assert.That(game, Is.Not.Null, "the game didn't boot itself");
            AudioListener.volume = 0;
            Sfx.Enabled = false;
            Time.captureDeltaTime = Step;
            game.ShowMenu();
            game.hud.ShowMenu();
            ui = Root(game);
            game.nextSeed = 7;
            game.StartRun();
            game.invincible = true;
            yield return Seconds(0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0;
            if (game != null)
            {
                game.invincible = false;
                game.Stick.autopilot = null;
            }
        }

        IEnumerator Seconds(float s)
        {
            for (int i = 0; i < s / Step; i++) yield return null;
        }

        void Kill(int n, bool blast = false)
        {
            var p = game.player;
            for (int i = 0; i < n; i++)
            {
                var z = game.SpawnZombie("walker", p.x + 3, p.z - 12, 0, chase: false);
                game.DamageZombie(z, 1e5f, 0, 1, 0, blast: blast);
            }
        }

        Challenge On(string id) => game.Challenges.FirstOrDefault(c => c.def.id == id);

        [UnityTest]
        public IEnumerator ThreeOnEachPayingSomethingDifferent()
        {
            Assert.That(game.Challenges.Count, Is.EqualTo(C.Active));
            Assert.That(game.Challenges.Select(c => c.stat).Distinct().Count(), Is.EqualTo(C.Active), "three different rewards to start");
            var panel = Find<VisualElement>(ui, v => v.ClassListContains("h-challenges"), "the challenge panel");
            Assert.That(panel.Query<VisualElement>(className: "hc-row").ToList().Count, Is.EqualTo(C.Active));
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                using var photo = new Photo(game);
                yield return Seconds(1);
                photo.Shot("23-challenges");
            }
        }

        [UnityTest]
        public IEnumerator DoneTheyPayAndMakeWay()
        {
            game.SetChallenges("kills", "blasts", "streak");
            var kills = On("kills");
            int stat = kills.stat;
            Kill(29);
            Assert.That(On("kills"), Is.SameAs(kills), "not yet");
            Assert.That(On("streak"), Is.Null, "29 at once: the streak one is done already");
            float before = game.Bonus(stat);
            Kill(1);
            yield return null;
            Assert.That(game.Bonus(stat), Is.EqualTo(before + C.Step).Within(0.001f), "paid");
            Assert.That(On("kills"), Is.Null, "and a different one in its place");
            Assert.That(game.Challenges.Count, Is.EqualTo(C.Active));
            Assert.That(game.run.challengesDone, Is.EqualTo(2));

            // Blown up counts as blown up.
            var blasts = On("blasts");
            int bs = blasts.stat;
            before = game.Bonus(bs);
            Kill(blasts.target, blast: true);
            Assert.That(game.Bonus(bs), Is.GreaterThanOrEqualTo(before + C.Step - 0.001f));
            yield return Seconds(0.5f);
            Assert.That(Visible(Find<Label>(ui, l => l.text.StartsWith("CHALLENGES ·"), "the bonuses earned, over the panel")));
        }

        [UnityTest]
        public IEnumerator BeingHitStartsTheScratchOneOver()
        {
            game.SetChallenges("untouched", "coins", "wrecks");
            var p = game.player;
            game.Stick.autopilot = new Vector3(0, 0, -1);
            yield return Seconds(4);
            float gone = On("untouched").progress;
            Assert.That(gone, Is.GreaterThan(10), "counting the metres");
            game.HurtPlayer(1, p.x, p.z - 1);
            yield return null;
            Assert.That(On("untouched").progress, Is.LessThan(gone), "from nothing again");
        }

        [UnityTest]
        public IEnumerator ANewRunStartsAtNormal()
        {
            game.SetChallenges("kills", "coins", "wrecks");
            Kill(30);
            Assert.That(game.run.challengesDone, Is.GreaterThanOrEqualTo(1));
            game.StartRun();
            yield return null;
            for (int s = 0; s < C.Stats.Length; s++) Assert.That(game.Bonus(s), Is.EqualTo(1));
            Assert.That(game.run.challengesDone, Is.Zero);
        }
    }
}
