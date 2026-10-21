// The Game Director, ported from graveway/src/director.js: decides who
// comes for you, and when, from how far up the road you've made it. Every
// number it uses is in Tunables.Director; this is only the bookkeeping.
//
//   Distance: the furthest point reached. stage = distance / StageLength.
//   Hordes wait up the road every HordeEvery(stage) metres, spawned inside
//   the fog as you approach; some pour out of alleys instead.
//   Stragglers arrive every TrickleEvery(stage) seconds, from ahead, from
//   alleys or from behind, so standing still never helps.
//   Runners and tanks join at their unlock stage; the boss arrives at its
//   unlock stage and every BossEvery stages after.

using System.Collections.Generic;
using UnityEngine;
using D = Graveway.Tunables.Director;

namespace Graveway
{
    public sealed class Director
    {
        public const float SpawnAhead = 52;            // metres up the road, inside the fog

        readonly Game game;
        readonly List<Alley> near = new List<Alley>();
        int stage, nextBoss;
        float nextHordeAt, trickleT;

        public Director(Game game) { this.game = game; }

        public void Reset(float distance)
        {
            stage = Mathf.FloorToInt(distance / D.StageLength);
            nextHordeAt = distance + 6;
            trickleT = 4;
            // Starting a run on a boss stage (playtest tool) brings that boss at once.
            nextBoss = D.UnlockBoss;
            while (nextBoss < stage) nextBoss += D.BossEvery;
        }

        List<Alley> AlleysWhere(System.Predicate<Alley> keep)
        {
            near.Clear();
            foreach (var a in game.world.alleys) if (keep(a)) near.Add(a);
            return near;
        }

        public void Update(float dt, float distance)
        {
            var rng = game.rng;
            int s = Mathf.FloorToInt(distance / D.StageLength);
            if (s > stage) { stage = s; game.OnStage(s); }
            if (s >= nextBoss) { nextBoss += D.BossEvery; game.SpawnBoss(); }

            float pz = game.player.z;
            // Hordes up the road, by distance.
            if (distance >= nextHordeAt)
            {
                nextHordeAt = distance + D.HordeEvery(s) * (0.75f + rng.Next() * 0.5f);
                int lo = D.HordeMin(s), hi = D.HordeMax(s);
                int count = lo + (int)(rng.Next() * (hi - lo + 1));
                var alleys = AlleysWhere(a => a.z < pz - 12 && a.z > pz - 40);
                if (alleys.Count > 0 && rng.Next() < D.SideAmbush(s))
                    game.SpawnGroup(count, s, alley: alleys[(int)(rng.Next() * alleys.Count)]);
                else
                    game.SpawnGroup(count, s, z: pz - SpawnAhead - rng.Next() * 8);
            }

            // Stragglers, by time.
            trickleT -= dt;
            if (trickleT <= 0)
            {
                trickleT = D.TrickleEvery(s) * (0.7f + rng.Next() * 0.6f);
                float r = rng.Next();
                var alleys = AlleysWhere(a => Mathf.Abs(a.z - pz) < 30);
                if (r < 0.2f && s >= 1) game.SpawnGroup(1, s, z: pz + 22, behind: true);
                else if (r < 0.45f && alleys.Count > 0) game.SpawnGroup(1, s, alley: alleys[(int)(rng.Next() * alleys.Count)]);
                else game.SpawnGroup(1 + (rng.Next() < 0.3f ? 1 : 0), s, z: pz - SpawnAhead + rng.Next() * 6);
            }
        }

        // Which kind of zombie, for a spawn at this stage.
        public string PickKind(int s)
        {
            float r = game.rng.Next();
            float tank = s >= D.UnlockTank ? D.TankShare(s) : 0;
            float runner = s >= D.UnlockRunner ? D.RunnerShare(s) : 0;
            if (r < tank) return "tank";
            if (r < tank + runner) return "runner";
            return "walker";
        }
    }
}
