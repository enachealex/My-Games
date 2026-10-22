// The whole game running: it boots itself, shows the menu, and survives a
// minute of play. Any exception or error logged along the way fails the
// test. Time is stepped at 20 frames a second, so a minute of play takes
// 1,200 frames however fast the machine is.
//
// Screenshots (only with a GPU, so not under -nographics), with the UI over
// the street, land in graveway-unity/Logs/shots/.

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Graveway.Tests
{
    public class SmokeTests
    {
        const float Step = 1f / 20;
        Game game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Save.UseKey("graveway.save.tests");
            game = Object.FindAnyObjectByType<Game>();
            for (int i = 0; i < 5 && game == null; i++)
            {
                yield return null;
                game = Object.FindAnyObjectByType<Game>();
            }
            Assert.That(game, Is.Not.Null, "the game didn't boot itself");
            AudioListener.volume = 0;                   // tests run silent
            Sfx.Enabled = false;
            Time.captureDeltaTime = Step;
            game.ShowMenu();
            game.hud.ShowMenu();
            yield return null;
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

        [UnityTest]
        public IEnumerator MenuShowsTheStreetWithZombies()
        {
            yield return Seconds(2);
            Assert.That(game.state, Is.EqualTo(Game.State.Menu));
            Assert.That(game.AliveCount(), Is.EqualTo(9));
        }

        [UnityTest]
        public IEnumerator SurvivesAMinuteRunningUpTheRoad()
        {
            game.nextSeed = 7;                                  // a street the straight-ahead autopilot can get up
            game.StartRun();
            game.invincible = true;
            game.Stick.autopilot = new Vector3(0, 0, -1);      // up the road is -z
            yield return Seconds(60);
            Assert.That(game.state, Is.EqualTo(Game.State.Playing));
            Assert.That(game.Distance, Is.GreaterThan(100), "didn't get far up the road");
            Assert.That(game.run.kills, Is.GreaterThan(0), "never shot anything");
        }

        [UnityTest]
        public IEnumerator DyingEndsTheRunAndBanksIt()
        {
            game.StartRun();
            yield return Seconds(2);
            int runs = Save.Data.runs;
            var p = game.player;
            game.HurtPlayer(p.hp + p.armor + 1, p.x, p.z - 1);
            Assert.That(game.state, Is.EqualTo(Game.State.Dying));
            yield return Seconds(2.5f);
            Assert.That(Save.Data.runs, Is.EqualTo(runs + 1), "the run wasn't saved");
        }

        [UnityTest]
        public IEnumerator Screenshots()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics), so nothing to see");
            using var photo = new Photo(game);
            yield return Seconds(3);
            photo.Shot("1-menu");
            game.StartRun();
            game.invincible = true;
            yield return Seconds(4);
            photo.Shot("2-run-start");
            game.Stick.autopilot = new Vector3(0.3f, 0, -1);
            yield return Seconds(25);
            Assert.That(game.state, Is.EqualTo(Game.State.Playing));
            photo.Shot("3-run-25s");
        }

        IEnumerator Seconds(float s)
        {
            for (int i = 0; i < s / Step; i++) yield return null;
        }
    }
}
