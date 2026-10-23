// The places the road runs through (playtest note 6): a run moved on to
// the docks, the highway, and downtown again keeps its distance, and each
// has its own street and props, and is played through for a while. With a
// GPU, pictures of each.

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Graveway.Tests
{
    public class PlacesTests
    {
        const float Step = 1f / 20;
        Game game;

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

        IEnumerator Seconds(float s)
        {
            for (int i = 0; i < s / Step; i++) yield return null;
        }

        HashSet<string> Kinds()
        {
            var kinds = new HashSet<string>();
            foreach (var p in game.world.props) kinds.Add(p.kind);
            return kinds;
        }

        // Walks the survivor into the evac's ring from just short of it.
        IEnumerator ToTheHelicopter(float padZ)
        {
            var p = game.player;
            p.x = 0;
            p.z = padZ + 5;
            game.Stick.autopilot = new Vector3(0, 0, -1);
            for (float t = 0; t < 6 && !game.InCutScene; t += Step) yield return null;
            game.Stick.autopilot = null;
            Assert.That(game.InCutScene, "into the ring: the cut scene");
        }

        // Two bosses passed (skipped, here) bring the helicopter; aboard, the
        // run goes on in the next place, and so again after it, with a tap
        // skipping the cut scene now that one's been seen.
        [UnityTest]
        public IEnumerator TheEvacTakesYouOn()
        {
            Save.Data.seenEvac = false;
            game.nextSeed = 7;
            game.StartRun();
            game.invincible = true;
            yield return Seconds(1);
            using var photo = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null ? new Photo(game) : null;
            game.run.skipBoss = true;
            game.SpawnBoss();
            Assert.That(game.EvacCalled, Is.False, "not after one boss");
            game.run.skipBoss = true;
            float padZ = game.player.z - Tunables.EvacAhead;
            game.SpawnBoss();
            Assert.That(game.EvacCalled, "after the second");
            yield return Seconds(6.5f);
            photo?.Shot("21-evac-landed");
            float distance = game.Distance;
            int bonus = game.run.bonus;
            yield return ToTheHelicopter(padZ);
            yield return Seconds(0.6f);
            photo?.Shot("21b-evac-aboard");
            for (float t = 0; t < 8 && game.state != Game.State.Playing; t += Step) yield return null;
            Assert.That(game.state, Is.EqualTo(Game.State.Playing), "out in the new place");
            Assert.That(game.Place.id, Is.EqualTo("docks"));
            Assert.That(game.Distance, Is.GreaterThanOrEqualTo(distance - 1), "the distance carries on");
            Assert.That(game.run.bonus, Is.EqualTo(bonus + Tunables.EvacCoins), "coins for making it out");
            Assert.That(Save.Data.seenEvac);
            yield return Seconds(0.5f);
            photo?.Shot("21c-evac-out");
            yield return Seconds(4);
            Assert.That(game.EvacCalled, Is.False, "and it's gone");

            padZ = game.player.z - Tunables.EvacAhead;
            game.CallEvac();
            yield return Seconds(6.5f);
            yield return ToTheHelicopter(padZ);
            yield return Seconds(0.2f);
            game.hud.Back();                            // a tap: skip it
            yield return Seconds(0.5f);
            Assert.That(game.state, Is.EqualTo(Game.State.Playing));
            Assert.That(game.Place.id, Is.EqualTo("highway"));
            Assert.That(game.run.bonus, Is.EqualTo(bonus + Tunables.EvacCoins * 3), "100, then 200");
        }

        [UnityTest]
        public IEnumerator EachPlaceHasItsOwnRoad()
        {
            game.nextSeed = 7;
            game.StartRun();
            game.invincible = true;
            yield return Seconds(1);
            Assert.That(game.Place.id, Is.EqualTo("downtown"));
            Assert.That(Kinds(), Does.Contain("car"));
            using var photo = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null ? new Photo(game) : null;
            game.Stick.autopilot = new Vector3(0.2f, 0, -1);
            yield return Seconds(4);
            float before = game.Distance;

            string[] want = { "downtown", "docks", "highway", "downtown" };
            for (int n = 1; n <= 3; n++)
            {
                game.Relocate(n);
                yield return Seconds(1.5f);
                Assert.That(game.Place.id, Is.EqualTo(want[n]));
                Assert.That(game.Distance, Is.GreaterThanOrEqualTo(before), "the distance carries on");
                Assert.That(game.AliveCount(), Is.LessThan(10), "the old road's zombies stayed behind");
                photo?.Shot($"20-place-{n}-{want[n]}");
                yield return Seconds(12);
                Assert.That(game.state, Is.EqualTo(Game.State.Playing), $"still going in {want[n]}");
                var kinds = Kinds();
                if (want[n] == "docks") Assert.That(kinds.Overlaps(new[] { "container", "truck", "crates" }), $"the docks' props: {string.Join(", ", kinds)}");
                if (want[n] == "highway") Assert.That(kinds.Overlaps(new[] { "jersey", "truck" }), $"the highway's props: {string.Join(", ", kinds)}");
                photo?.Shot($"20-place-{n}-{want[n]}-later");
                before = game.Distance;
            }
        }
    }
}
