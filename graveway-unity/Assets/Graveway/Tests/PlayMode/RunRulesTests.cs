// Rules of a run that playtesting turned up (22 October 2026): tanks and
// bosses don't get stuck beating on a wreck, or on a prop that's no longer
// in their way; and the Coin Vacuum takes every coin behind you and on
// screen, however far.

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Graveway.Tests
{
    public class RunRulesTests
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
                game.Camera.ResetAspect();
            }
        }

        IEnumerator Seconds(float s)
        {
            for (int i = 0; i < s / Step; i++) yield return null;
        }

        Prop Car(float nearZ)
        {
            var car = game.world.props.Find(pr => pr.kind == "car" && !pr.destroyed && !pr.indestructible && pr.z < nearZ - 6 && pr.z > nearZ - 45);
            Assert.That(car, Is.Not.Null, "a car on the road ahead");
            return car;
        }

        // A tank up against a car, on the far side from the survivor, beating on it.
        Zombie TankAgainst(Prop car)
        {
            var p = game.player;
            float reachZ = car.hw * Mathf.Abs(car.sin) + car.hd * Mathf.Abs(car.cos);    // the box's half-depth along the road
            p.x = car.x;
            p.z = car.z - reachZ - 9;
            var tank = game.SpawnZombie("tank", car.x, car.z + reachZ + 0.4f, 1, chase: true);
            tank.hp = tank.maxHp = 1e6f;
            tank.state = Zombie.Mode.Smash;
            tank.smash = car;
            tank.attackT = 0;
            return tank;
        }

        [UnityTest]
        public IEnumerator ASmasherMovesOnFromAWreck()
        {
            var car = Car(game.player.z);
            car.hp = car.maxHp = 1e5f;                  // it lasts, so only the wreck ends this
            var tank = TankAgainst(car);
            yield return Seconds(2 / tank.t.attackRate);
            Assert.That(tank.state, Is.EqualTo(Zombie.Mode.Smash), "beating on a car in its way");
            Assert.That(tank.smash, Is.SameAs(car));

            game.propSys.Damage(car, 1e6f, 0, 1, "blast");
            Assert.That(car.indestructible, "the car's a burnt-out wreck now");
            yield return Seconds(0.2f);
            Assert.That(tank.state, Is.Not.EqualTo(Zombie.Mode.Smash), "and the tank's done with it");
            Assert.That(tank.smash, Is.Null);
        }

        [UnityTest]
        public IEnumerator ASmasherMovesOnFromAPropNoLongerInItsWay()
        {
            var car = Car(game.player.z);
            car.hp = car.maxHp = 1e5f;
            var tank = TankAgainst(car);
            yield return null;
            // The survivor goes round behind the tank: the car's no longer between them.
            game.player.z = tank.z + 9;
            yield return Seconds(1.2f / tank.t.attackRate);
            var pl = game.player;
            Assert.That(tank.state, Is.Not.EqualTo(Zombie.Mode.Smash),
                $"tank {tank.x:0.00},{tank.z:0.00} r{tank.radius:0.00} t{tank.attackT:0.00} player {pl.x:0.00},{pl.z:0.00} car {car.x:0.00},{car.z:0.00} {car.hw:0.00}x{car.hd:0.00} cos{car.cos:0.00} sin{car.sin:0.00} blocking {car.blocking} moving {car.moving} smash {(tank.smash == car)}");
            Assert.That(car.indestructible, Is.False, "it gave up on the car, not wrecked it");
        }

        [UnityTest]
        public IEnumerator TheCoinVacuumTakesEveryCoinBehindAndOnScreen()
        {
            game.Camera.aspect = 1440f / 3120;          // a phone held upright, like the playtest's
            yield return Seconds(1);
            var p = game.player;
            float x = p.x, behind = p.z + 38, ahead = p.z - 36;     // both further than the vacuum's 32 m
            var v = game.Camera.WorldToViewportPoint(new Vector3(x, 0.35f, ahead));
            Assert.That(v.z > 0 && v.y > 0 && v.y < 1, $"36 m up the road is on a phone's screen ({v})");
            for (int i = 0; i < 3; i++)
            {
                game.PlaceCoin(x - 1 + i, behind);
                game.PlaceCoin(x - 1 + i, ahead);
            }
            yield return Seconds(1);
            Assert.That(game.CoinsNear(x, behind, 2), Is.EqualTo(3), "too far for the coin magnet");
            Assert.That(game.CoinsNear(x, ahead, 2), Is.EqualTo(3));

            p.buffs["vacuum"] = Tunables.PowerUp("vacuum").duration;
            yield return Seconds(1.5f);
            Assert.That(game.CoinsNear(x, behind, 8), Is.Zero, "every coin left behind");
            Assert.That(game.CoinsNear(x, ahead, 8), Is.Zero, "and every coin on screen");
        }
    }
}
