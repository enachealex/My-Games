// The armored boss (playtest note 18): every second boss, the one before the
// evac, wears four plates, a pad on each arm, a chest plate and a helmet.
// While any is on, a hit does a third of its damage to the boss and the rest
// to the armor (blasts twice as much); each quarter of the armor's toughness
// dealt, a plate comes off, tumbles to the ground and leaves a power-up where
// it lands. With the last one gone, it's exposed and angrier.

using System.Collections.Generic;
using UnityEngine;
using A = Graveway.Tunables.BossArmor;

namespace Graveway
{
    public sealed partial class Game
    {
        sealed class FallenPlate
        {
            public Transform t;
            public Vector3 v, spin;
            public float age;
            public bool landed, reward;
        }

        readonly List<FallenPlate> fallen = new List<FallenPlate>();
        float sparkT;

        // The boss as it turns up: armored or not.
        void ArmorUp(Zombie z)
        {
            z.plateObjects = Rigs.Armor(z.model);
            if (z.plateObjects.Length == 0) return;              // no armor in the catalog: a plain boss
            z.plates = z.plateObjects.Length;
            z.armorMax = z.armor = z.maxHp * A.Share;
        }

        // Some of a hit taken by the armor: what's left for the boss.
        float HitArmor(Zombie z, float dmg, bool blast)
        {
            z.armor = Mathf.Max(0, z.armor - dmg * (1 - A.Through) * (blast ? A.BlastMult : 1));
            int left = z.armor <= 0 ? 0 : Mathf.CeilToInt(z.armor / z.armorMax * z.plateObjects.Length - 1e-3f);
            while (z.plates > left) BreakPlate(z, reward: true);
            if ((sparkT -= Time.deltaTime) <= 0)
            {
                sparkT = 0.12f;
                vfx.Sparks(z.x, 1.5f * z.scale, z.z, 3, 6, 0.3f);
                Sfx.Clang();
            }
            return dmg * A.Through;
        }

        // The next plate off: thrown from where it was, out and up.
        void BreakPlate(Zombie z, bool reward)
        {
            int i = z.plateObjects.Length - z.plates;
            z.plates--;
            var go = z.plateObjects[i];
            z.plateObjects[i] = null;
            if (go != null)
            {
                var t = go.transform;
                t.SetParent(worldRoot, true);
                var from = t.position - new Vector3(z.x, t.position.y, z.z);
                var out_ = from.sqrMagnitude > 1e-4f ? from.normalized : new Vector3(rng.Next() - 0.5f, 0, rng.Next() - 0.5f).normalized;
                fallen.Add(new FallenPlate
                {
                    t = t, reward = reward,
                    v = out_ * (2.5f + rng.Next() * 1.5f) + Vector3.up * (3.5f + rng.Next() * 2),
                    spin = new Vector3(rng.Next() - 0.5f, rng.Next() - 0.5f, rng.Next() - 0.5f) * 720,
                });
                vfx.Sparks(t.position.x, t.position.y, t.position.z, 14, 9, 0.5f);
            }
            Sfx.Clang();
            shake = Mathf.Max(shake, 0.35f);
            if (z.plates == 0 && !z.dead)
            {
                z.armor = 0;
                z.speed *= A.Enraged;
                hud.Toast("Its armor's off!");
            }
        }

        void UpdatePlates(float dt)
        {
            for (int i = fallen.Count - 1; i >= 0; i--)
            {
                var f = fallen[i];
                if (f.t == null) { fallen.RemoveAt(i); continue; }
                f.age += dt;
                if (!f.landed)
                {
                    f.v.y -= 20 * dt;
                    var p = f.t.position + f.v * dt;
                    f.t.rotation = Quaternion.Euler(f.spin * dt) * f.t.rotation;
                    float floor = 0.15f;
                    if (p.y <= floor && f.v.y < 0)
                    {
                        p.y = floor;
                        if (f.v.y < -3) { f.v = new Vector3(f.v.x * 0.5f, -f.v.y * 0.3f, f.v.z * 0.5f); f.spin *= 0.4f; Sfx.Clang(); }
                        else Land(f, ref p);
                    }
                    f.t.position = p;
                }
                // Lies there a while, then sinks away.
                if (f.age > 8)
                {
                    f.t.position += Vector3.down * dt * 0.4f;
                    if (f.age > 10) { Destroy(f.t.gameObject); fallen.RemoveAt(i); }
                }
            }
        }

        // Down for good: flat on the road, and its power-up beside it.
        void Land(FallenPlate f, ref Vector3 p)
        {
            f.landed = true;
            f.v = Vector3.zero;
            var e = f.t.rotation.eulerAngles;
            f.t.rotation = Quaternion.Euler(Mathf.Round(e.x / 90) * 90, e.y, Mathf.Round(e.z / 90) * 90);
            vfx.Dust(p.x, p.z, 0.6f, 4, null, 1);
            if (!f.reward || run == null) return;
            float x = Mathf.Clamp(p.x, -World.WalkHalf + 0.8f, World.WalkHalf - 0.8f);
            world.DropItem("power", x, p.z, Tunables.PickPowerup(rng.Next()));
        }

        // Its plates that are still on, gone with it (no power-ups for those).
        void ArmorOff(Zombie z)
        {
            while (z.plates > 0) BreakPlate(z, reward: false);
        }

        // What it was wearing goes back with it (it's in a pool for the next).
        static void StripArmor(Zombie z)
        {
            if (z.plateObjects == null) return;
            foreach (var go in z.plateObjects) if (go != null) Destroy(go);
            z.plateObjects = null;
            z.plates = 0;
            z.armor = 0;
        }

        void ClearPlates()
        {
            foreach (var f in fallen) if (f.t != null) Destroy(f.t.gameObject);
            fallen.Clear();
        }
    }
}
