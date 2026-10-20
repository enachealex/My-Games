// The whole game running: it boots itself, shows the menu, and survives a
// minute of play. Any exception or error logged along the way fails the
// test. Time is stepped at 20 frames a second, so a minute of play takes
// 1,200 frames however fast the machine is.
//
// Screenshots (only with a GPU, so not under -nographics) land in
// graveway-unity/Logs/shots/.

using System.Collections;
using System.IO;
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
            Time.captureDeltaTime = Step;
            game.ShowMenu();
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
                if (game.Camera != null) game.Camera.targetTexture = null;
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
            game.StartRun();
            game.invincible = true;
            game.Stick.autopilot = new Vector3(0, 0, 1);
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
            // A phone-shaped picture: 1080 x 2340 at half size.
            var rt = new RenderTexture(540, 1170, 24);
            game.Camera.targetTexture = rt;
            yield return Seconds(3);
            Shot(rt, "1-menu");
            game.StartRun();
            game.invincible = true;
            yield return Seconds(4);
            Shot(rt, "2-run-start");
            game.Stick.autopilot = new Vector3(0.3f, 0, 1);
            yield return Seconds(25);
            Assert.That(game.state, Is.EqualTo(Game.State.Playing));
            Shot(rt, "3-run-25s");
            game.Camera.targetTexture = null;
            rt.Release();
        }

        IEnumerator Seconds(float s)
        {
            for (int i = 0; i < s / Step; i++) yield return null;
        }

        static void Shot(RenderTexture rt, string name)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "shots"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
